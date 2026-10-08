using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NarzedziaIP
{
    // Port of cisco-acl-helper/updater.py (stdlib only).
    //
    // Flow:
    //   1. FetchLatestReleaseAsync() -> ReleaseInfo from GitHub Releases API.
    //   2. IsNewer(latestTag, current) compares "v1.1.0"-style tags.
    //   3. DownloadAssetAsync() streams the release zip to temp with progress.
    //   4. Preferred: StageGuiUpdater() + LaunchGuiUpdater() run the separate
    //      NarzedziaIP.Updater.exe (own window with progress, no cmd /
    //      powershell windows). It waits for our PID, extracts the zip,
    //      copies files over the app dir, restarts the exe and cleans up.
    //   5. Fallback (updater exe missing): WriteApplyScript() creates a hidden
    //      .bat doing the same steps via Expand-Archive + robocopy.
    //
    // NOTE: unlike the Python version there is no config.json with user files
    // in the app dir yet, so PRESERVED_FILES is empty by default. If user
    // files ever land next to the exe, add their basenames here and they will
    // survive updates (both GUI updater and .bat honour the list).

    public sealed class ReleaseInfo
    {
        public string Tag { get; set; }
        public string Name { get; set; }
        public string Body { get; set; }
        public string ZipUrl { get; set; }
        public long ZipSize { get; set; }
    }

    public sealed class DownloadProgress
    {
        public DownloadProgress(long downloaded, long total)
        {
            Downloaded = downloaded;
            Total = total;
        }

        public long Downloaded { get; private set; }
        public long Total { get; private set; }
    }

    public static class Updater
    {
        public const string GithubRepo = "blem5000/Narzedzia-IP-2";
        public const string AssetName = "NarzedziaIP2-windows.zip";

        // Release zips contain a top-level folder with the installed files.
        public const string InnerFolderName = "NarzedziaIP2";
        public const string UpdaterExeName = "NarzedziaIP.Updater.exe";

        public static string ApiLatest
        {
            get { return "https://api.github.com/repos/" + GithubRepo + "/releases/latest"; }
        }

        public static readonly string[] PreservedFiles = new string[0];

        public static string AppDir()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir))
                    return baseDir;
            }
            catch
            {
            }
            return Path.GetDirectoryName(ExePath());
        }

        public static string ExePath()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                string location = asm.Location;
                if (!string.IsNullOrWhiteSpace(location))
                    return location;
            }
            catch
            {
            }
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        public static string ExeName()
        {
            string name = Path.GetFileName(ExePath());
            if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return "Narzedzia IP 2.exe";
            return name;
        }

        // Single source of truth, like version.py: assembly version "1.1.0.0" -> "1.1.0".
        public static string CurrentVersion()
        {
            try
            {
                Assembly asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                Version v = asm.GetName().Version;
                if (v == null)
                    return "1.0.0";
                if (v.Revision == 0)
                    return v.Major + "." + v.Minor + "." + v.Build;
                return v.ToString();
            }
            catch
            {
                return "1.0.0";
            }
        }

        public static int[] ParseVersion(string s)
        {
            List<int> parts = new List<int>();
            string t = (s ?? string.Empty).Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(1);
            foreach (string chunk in t.Split('.'))
            {
                StringBuilder num = new StringBuilder();
                foreach (char ch in chunk)
                {
                    if (char.IsDigit(ch))
                        num.Append(ch);
                    else
                        break;
                }
                int n;
                parts.Add(int.TryParse(num.ToString(), out n) ? n : 0);
            }
            while (parts.Count < 3)
                parts.Add(0);
            return parts.ToArray();
        }

        public static bool IsNewer(string latestTag, string current)
        {
            int[] latest = ParseVersion(latestTag);
            int[] cur = ParseVersion(string.IsNullOrWhiteSpace(current) ? CurrentVersion() : current);
            int len = Math.Max(latest.Length, cur.Length);
            for (int i = 0; i < len; i++)
            {
                int a = i < latest.Length ? latest[i] : 0;
                int b = i < cur.Length ? cur[i] : 0;
                if (a != b)
                    return a > b;
            }
            return false;
        }

        private static void EnsureTls12()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public static Task<ReleaseInfo> FetchLatestReleaseAsync(int timeoutSeconds)
        {
            return FetchLatestReleaseAsync(timeoutSeconds, ApiLatest);
        }

        // Overload z jawnym URL - produkcja woła ApiLatest, testy stawiają
        // lokalny stub HTTP i nie zależą od sieci ani GitHuba.
        public static async Task<ReleaseInfo> FetchLatestReleaseAsync(int timeoutSeconds, string apiUrl)
        {
            EnsureTls12();
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds <= 0 ? 15 : timeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NarzedziaIP2-updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

                string json;
                try
                {
                    json = await client.GetStringAsync(apiUrl).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("GitHub API error: " + ex.Message, ex);
                }

                return ParseReleaseJson(json);
            }
        }

        // Visible for testing; parses the /releases/latest payload.
        public static ReleaseInfo ParseReleaseJson(string json)
        {
            Dictionary<string, object> data;
            try
            {
                data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("GitHub API: invalid JSON: " + ex.Message, ex);
            }
            if (data == null)
                throw new InvalidOperationException("GitHub API: empty response");

            object tagObj;
            string tag = data.TryGetValue("tag_name", out tagObj) ? Convert.ToString(tagObj).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(tag))
                throw new InvalidOperationException("GitHub API: empty tag_name");

            object nameObj;
            object bodyObj;
            string name = data.TryGetValue("name", out nameObj) && nameObj != null ? Convert.ToString(nameObj) : tag;
            string body = data.TryGetValue("body", out bodyObj) && bodyObj != null ? Convert.ToString(bodyObj) : string.Empty;

            object assetsObj;
            ArrayList assets = data.TryGetValue("assets", out assetsObj) ? assetsObj as ArrayList : null;
            Dictionary<string, object> chosen = null;
            if (assets != null)
            {
                foreach (object item in assets)
                {
                    Dictionary<string, object> a = item as Dictionary<string, object>;
                    if (a == null)
                        continue;
                    object n;
                    if (a.TryGetValue("name", out n) && string.Equals(Convert.ToString(n), AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = a;
                        break;
                    }
                }
                if (chosen == null)
                {
                    foreach (object item in assets)
                    {
                        Dictionary<string, object> a = item as Dictionary<string, object>;
                        if (a == null)
                            continue;
                        object n;
                        if (a.TryGetValue("name", out n) && Convert.ToString(n).EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = a;
                            break;
                        }
                    }
                }
            }
            if (chosen == null)
                throw new InvalidOperationException("No .zip asset found in latest release");

            object urlObj;
            string url = chosen.TryGetValue("browser_download_url", out urlObj) ? Convert.ToString(urlObj) : string.Empty;
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Release asset has no download URL");

            long size = 0;
            object sizeObj;
            if (chosen.TryGetValue("size", out sizeObj) && sizeObj != null)
            {
                try { size = Convert.ToInt64(sizeObj); }
                catch { size = 0; }
            }

            return new ReleaseInfo
            {
                Tag = tag,
                Name = name,
                Body = body,
                ZipUrl = url,
                ZipSize = size
            };
        }

        public static async Task DownloadAssetAsync(string url, string destPath, IProgress<DownloadProgress> progress, CancellationToken cancel)
        {
            EnsureTls12();
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NarzedziaIP2-updater");
                using (HttpResponseMessage resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? 0;
                    using (Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[256 * 1024];
                        long done = 0;
                        long lastReport = 0;
                        int read;
                        while ((read = await src.ReadAsync(buf, 0, buf.Length, cancel).ConfigureAwait(false)) > 0)
                        {
                            dst.Write(buf, 0, read);
                            done += read;
                            if (progress != null && (done - lastReport >= 512 * 1024 || (total > 0 && done == total)))
                            {
                                lastReport = done;
                                progress.Report(new DownloadProgress(done, total));
                            }
                        }
                        if (progress != null)
                            progress.Report(new DownloadProgress(done, total > 0 ? total : done));
                    }
                }
            }
        }

        public static string TempDownloadPath(string tag)
        {
            StringBuilder safe = new StringBuilder();
            foreach (char c in (tag ?? "update"))
            {
                safe.Append(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' ? c : '_');
            }
            return Path.Combine(Path.GetTempPath(), "narzedzia_update_" + safe + "_" + Guid.NewGuid().ToString("N") + ".zip");
        }

        // Unix timestamp (seconds) of process start. 0 when unknown/gone.
        // Guards against PID reuse, like proc_start_unix() in updater.py.
        public static int ProcStartUnix(int pid)
        {
            if (pid <= 0)
                return 0;
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    DateTime start = p.StartTime.ToUniversalTime();
                    DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    return (int)(start - epoch).TotalSeconds;
                }
            }
            catch
            {
                return 0;
            }
        }

        public static int OwnStartUnix()
        {
            try
            {
                return ProcStartUnix(Process.GetCurrentProcess().Id);
            }
            catch
            {
                return 0;
            }
        }

        // Block until pid exited (or is held by a different process). Always true.
        public static bool WaitForExit(int pid, int expectedStart, double pollSeconds)
        {
            if (pid <= 0)
                return true;
            TimeSpan poll = TimeSpan.FromMilliseconds(Math.Max(50, (pollSeconds <= 0 ? 0.5 : pollSeconds) * 1000));
            while (true)
            {
                Process p = null;
                try
                {
                    p = Process.GetProcessById(pid);
                }
                catch
                {
                    return true; // no such process
                }
                using (p)
                {
                    try
                    {
                        if (expectedStart != 0)
                        {
                            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                            int started = (int)(p.StartTime.ToUniversalTime() - epoch).TotalSeconds;
                            if (started != expectedStart)
                                return true; // PID reused by a different process
                        }
                        if (p.WaitForExit((int)poll.TotalMilliseconds))
                            return true; // exited
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
        }

        public static void ExtractZip(string zipPath, string staging, IProgress<DownloadProgress> progress)
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                List<ZipArchiveEntry> entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) || e.FullName.EndsWith("/")).ToList();
                long total = entries.Sum(e => e.Length);
                if (total <= 0)
                    total = 1;
                long done = 0;
                foreach (ZipArchiveEntry entry in entries)
                {
                    string target = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    if (entry.FullName.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    done += entry.Length;
                    if (progress != null)
                        progress.Report(new DownloadProgress(done, total));
                }
                if (progress != null)
                    progress.Report(new DownloadProgress(total, total));
            }
        }

        // Release zips contain a top-level NarzedziaIP2/ folder; return it when
        // present, else the staging dir itself.
        public static string ResolveSource(string staging, string exeName)
        {
            string inner = Path.Combine(staging, InnerFolderName);
            if (File.Exists(Path.Combine(inner, exeName)))
                return inner;
            return staging;
        }

        public static void CopyTree(string src, string dst, IEnumerable<string> excludeNames, IProgress<DownloadProgress> progress, int retries)
        {
            HashSet<string> excluded = new HashSet<string>(excludeNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            List<Tuple<string, string, long>> jobs = new List<Tuple<string, string, long>>();
            long total = 0;
            foreach (string full in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                if (excluded.Contains(Path.GetFileName(full)))
                    continue;
                string rel = full.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                long size = 0;
                try { size = new FileInfo(full).Length; }
                catch { size = 0; }
                jobs.Add(Tuple.Create(full, rel, size));
                total += size;
            }
            if (total <= 0)
                total = 1;
            long done = 0;
            foreach (Tuple<string, string, long> job in jobs)
            {
                string target = Path.Combine(dst, job.Item2);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Exception lastErr = null;
                for (int attempt = 0; attempt < Math.Max(1, retries); attempt++)
                {
                    try
                    {
                        File.Copy(job.Item1, target, true);
                        lastErr = null;
                        break;
                    }
                    catch (IOException ex)
                    {
                        lastErr = ex;
                        Thread.Sleep(1000);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        lastErr = ex;
                        Thread.Sleep(1000);
                    }
                }
                if (lastErr != null)
                    throw new InvalidOperationException("Cannot replace " + job.Item2 + ": " + lastErr.Message, lastErr);
                done += job.Item3;
                if (progress != null)
                    progress.Report(new DownloadProgress(done, total));
            }
            if (progress != null)
                progress.Report(new DownloadProgress(total, total));
        }

        // The updater must NOT run from the app dir: it replaces every file
        // there (including its own bundled copy, which would be locked).
        public static string StageGuiUpdater(string srcExe)
        {
            string runDir = Path.Combine(Path.GetTempPath(), "narzedzia_updater_run");
            Directory.CreateDirectory(runDir);
            string dst = Path.Combine(runDir, UpdaterExeName);
            try
            {
                File.Copy(srcExe, dst, true);
                return dst;
            }
            catch (IOException)
            {
                string alt = Path.Combine(runDir, "NarzedziaIP.Updater_" + Process.GetCurrentProcess().Id + ".exe");
                File.Copy(srcExe, alt, true);
                return alt;
            }
        }

        // Start the GUI updater (windowed exe: no console ever). Caller quits.
        public static void LaunchGuiUpdater(string updaterExe, string zipPath, string targetDir, string exeName, int pid, int pidStart, string tag)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = updaterExe,
                Arguments = BuildUpdaterArguments(zipPath, targetDir, exeName, pid, pidStart, tag),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(psi);
        }

        public static string BuildUpdaterArguments(string zipPath, string targetDir, string exeName, int pid, int pidStart, string tag)
        {
            return "--zip " + Quote(zipPath)
                + " --target " + Quote(targetDir)
                + " --exe " + Quote(exeName)
                + " --pid " + pid
                + " --pid-start " + pidStart
                + " --tag " + Quote(tag);
        }

        // Hidden .bat fallback: waits for our PID, extracts via Expand-Archive,
        // copies via robocopy (exit codes 0-7 mean success), restarts the exe.
        public static string WriteApplyScript(string zipPath, string targetDir, int pidToWait)
        {
            string exeName = ExeName();
            string staging = Path.Combine(Path.GetTempPath(), "narzedzia_update_stage_" + pidToWait);
            string batPath = Path.Combine(Path.GetTempPath(), "narz_apply_" + pidToWait + "_" + Guid.NewGuid().ToString("N") + ".bat");
            string xf = string.Join(" ", PreservedFiles);
            int pidStart = OwnStartUnix();
            StringBuilder bat = new StringBuilder();
            bat.AppendLine("@echo off");
            bat.AppendLine("setlocal");
            bat.AppendLine("set \"ZIP=" + zipPath + "\"");
            bat.AppendLine("set \"DST=" + targetDir + "\"");
            bat.AppendLine("set \"STAGE=" + staging + "\"");
            bat.AppendLine("set \"PIDW=" + pidToWait + "\"");
            bat.AppendLine("set \"PIDSTART=" + pidStart + "\"");
            bat.AppendLine("set \"EXE=" + exeName + "\"");
            bat.AppendLine("set \"LOG=%TEMP%\\narzedzia_update_" + pidToWait + ".log\"");
            bat.AppendLine("echo [%date% %time%] waiting for PID %PIDW% (start %PIDSTART%) > \"%LOG%\"");
            bat.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                + "\"$t = %PIDSTART%; try { $p = Get-Process -Id %PIDW% -ErrorAction Stop; "
                + "if ($t -ne 0) { $e = [datetime]::new(1970,1,1,0,0,0,[DateTimeKind]::Utc); "
                + "$s = [int][double]($p.StartTime.ToUniversalTime().Subtract($e).TotalSeconds); "
                + "if ($s -ne $t) { exit 0 } }; $p.WaitForExit() } catch { }\" >> \"%LOG%\" 2>&1");
            bat.AppendLine("echo [%date% %time%] process gone, extracting >> \"%LOG%\"");
            bat.AppendLine("if exist \"%STAGE%\" rmdir /s /q \"%STAGE%\"");
            bat.AppendLine("mkdir \"%STAGE%\"");
            bat.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
                + "\"Expand-Archive -LiteralPath '\"%ZIP%\"' -DestinationPath '\"%STAGE%\"' -Force\" >> \"%LOG%\" 2>&1");
            bat.AppendLine("if errorlevel 1 (");
            bat.AppendLine("  echo Update failed: cannot extract zip. See \"%LOG%\". >> \"%LOG%\"");
            bat.AppendLine("  start \"\" /wait cmd /c \"echo Update failed while extracting. See \"%LOG%\" & pause\"");
            bat.AppendLine("  exit /b 1");
            bat.AppendLine(")");
            bat.AppendLine("set \"SRC=%STAGE%\"");
            bat.AppendLine("if exist \"%STAGE%\\" + InnerFolderName + "\\%EXE%\" set \"SRC=%STAGE%\\" + InnerFolderName + "\"");
            bat.AppendLine("robocopy \"%SRC%\" \"%DST%\" /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS"
                + (string.IsNullOrWhiteSpace(xf) ? string.Empty : " /XF " + xf) + " >> \"%LOG%\" 2>&1");
            bat.AppendLine("ver>nul");
            bat.AppendLine("echo [%date% %time%] copy done, restarting >> \"%LOG%\"");
            bat.AppendLine("start \"\" \"%DST%\\%EXE%\"");
            bat.AppendLine("del \"%ZIP%\" >nul 2>&1");
            bat.AppendLine("rmdir /s /q \"%STAGE%\" >nul 2>&1");
            bat.AppendLine("(goto) 2>nul & del \"%~f0\" >nul 2>&1");
            File.WriteAllText(batPath, bat.ToString(), Encoding.ASCII);
            return batPath;
        }

        public static void LaunchApplyAndExit(string batPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = "/c \"" + batPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetTempPath()
            };
            Process.Start(psi);
        }

        public static string Quote(string value)
        {
            // Cudzysłowy są nielegalne w ścieżkach Windows - wytnij je,
            // zamiast produkować nieparsowalne argumenty.
            string v = (value ?? string.Empty).Replace("\"", string.Empty);
            // CommandLineToArgvW: parzysta liczba końcowych backslashy +
            // cudzysłów = dosłowne backslashe, nieparzysta = ostatni backslash
            // escapuje cudzysłów zamykający i parsowanie się rozjeżdża
            // (typowe dla katalogów kończących się "\", np. AppDir() albo
            // udziały UNC ze spacjami: "\\srv\udział z dir\").
            int trailing = 0;
            for (int i = v.Length - 1; i >= 0 && v[i] == '\\'; i--)
                trailing++;
            if (trailing % 2 == 1)
                v += "\\";
            return "\"" + v + "\"";
        }
    }
}
