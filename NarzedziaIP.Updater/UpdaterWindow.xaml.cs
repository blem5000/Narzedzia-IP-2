using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NarzedziaIPUpdater
{
    public sealed class UpdaterOptions
    {
        public string Zip { get; set; }
        public string Target { get; set; }
        public string Exe { get; set; }
        public int Pid { get; set; }
        public int PidStart { get; set; }
        public string Tag { get; set; }

        public static bool TryParse(string[] args, out UpdaterOptions opts, out string error)
        {
            opts = new UpdaterOptions();
            error = string.Empty;
            if (args == null)
                args = new string[0];
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> next = () => (i + 1 < args.Length) ? args[++i] : null;
                if (a == "--zip") opts.Zip = next();
                else if (a == "--target") opts.Target = next();
                else if (a == "--exe") opts.Exe = next();
                else if (a == "--pid") { int v; opts.Pid = int.TryParse(next(), out v) ? v : 0; }
                else if (a == "--pid-start") { int v; opts.PidStart = int.TryParse(next(), out v) ? v : 0; }
                else if (a == "--tag") opts.Tag = next();
            }
            if (string.IsNullOrWhiteSpace(opts.Zip) || !File.Exists(opts.Zip))
                return Fail("zip not found: " + opts.Zip, out error);
            if (string.IsNullOrWhiteSpace(opts.Target) || !Directory.Exists(opts.Target))
                return Fail("target dir not found: " + opts.Target, out error);
            if (string.IsNullOrWhiteSpace(opts.Exe))
                return Fail("missing --exe", out error);
            if (opts.Pid <= 0)
                return Fail("missing or invalid --pid", out error);
            if (string.IsNullOrWhiteSpace(opts.Tag))
                return Fail("missing --tag", out error);
            return true;
        }

        private static bool Fail(string msg, out string error)
        {
            error = msg;
            return false;
        }
    }

    // Separate update-installer program (own window), like updater_app.py.
    // Started by the main app AFTER the release zip was downloaded.
    // No Cancel button on purpose: interrupting the copy would leave a half
    // updated install. On error the window stays open with the log path.
    public partial class UpdaterWindow : Window
    {
        private readonly UpdaterOptions _args;
        private readonly bool _standalone;
        private readonly string _logPath;
        private StreamWriter _log;
        private bool _failed;
        private bool _busy;
        private global::NarzedziaIP.ReleaseInfo _standaloneInfo;
        private string _standaloneExe;

        public UpdaterWindow(UpdaterOptions args)
        {
            InitializeComponent();
            _args = args;
            _busy = true;
            lblHeading.Text = "Instalowanie aktualizacji " + args.Tag + "...";
            _logPath = Path.Combine(Path.GetTempPath(), "narzedzia_update_" + Process.GetCurrentProcess().Id + ".log");
            _log = new StreamWriter(_logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
            Log("tag=" + args.Tag + " target=" + args.Target + " pid=" + args.Pid);
            Loaded += (s, e) => Task.Run(() => Worker());
        }

        // Tryb standalone: double-click bez parametrów. Updater sam wykrywa
        // program obok siebie, sprawdza wersję i proponuje instalację.
        public UpdaterWindow()
        {
            InitializeComponent();
            _args = null;
            _standalone = true;
            _busy = true;
            lblHeading.Text = "Aktualizacja Narzędzia IP";
            lblPhase.Text = "Sprawdzanie dostępnej wersji...";
            _logPath = Path.Combine(Path.GetTempPath(), "narzedzia_update_" + Process.GetCurrentProcess().Id + ".log");
            _log = new StreamWriter(_logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
            Log("standalone start");
            Loaded += (s, e) => Task.Run(() => StandaloneCheck());
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Przerwanie kopiowania zostawiłoby połowiczną instalację.
            if (_busy && !_failed)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Refuse to close while working - a half-copied install is worse.
            // (Close button is disabled until done/failed, [X] guarded here.)
            if (!_failed)
            {
                // Allow close only after success path scheduled it; worker sets
                // _failed on error. During work, ignore the [X] button.
                // Success auto-closes, so reaching here with !_failed means the
                // 2s timer fired or user forced it - let it close.
            }
            try { if (_log != null) _log.Close(); }
            catch { }
            base.OnClosed(e);
        }

        private void Log(string msg)
        {
            try { _log.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg); }
            catch { }
        }

        private void UiLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(msg + Environment.NewLine);
                txtLog.ScrollToEnd();
            });
        }

        private void SetPhase(string text, int? value)
        {
            Dispatcher.Invoke(() =>
            {
                lblPhase.Text = text;
                if (value.HasValue)
                    progress.Value = Math.Max(0, Math.Min(1000, value.Value));
            });
        }

        private static string Pct(long done, long total)
        {
            if (total <= 0)
                return (done / 1024) + " KB";
            return (done * 100 / total) + "%";
        }

        private void Worker()
        {
            UpdaterOptions a = _args;
            try
            {
                Log("waiting for PID " + a.Pid + " (start " + a.PidStart + ")");
                global::NarzedziaIP.Updater.WaitForExit(a.Pid, a.PidStart, 0.5);
                Log("process gone");
                InstallFromZip(a.Zip, a.Target, a.Exe, "stage-" + a.Pid);
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
            finally
            {
                try { if (_log != null) _log.Close(); }
                catch { }
            }
        }

        // Wspólny ogon instalacji dla obu trybów. Rzuca przy błędzie.
        private void InstallFromZip(string zip, string targetDir, string exeName, string stagingSuffix)
        {
            string staging = Path.Combine(Path.GetTempPath(), "narzedzia_update_stage_" + stagingSuffix);
            Log("extracting " + zip);
            global::NarzedziaIP.Updater.ExtractZip(zip, staging, new Progress<global::NarzedziaIP.DownloadProgress>(
                p => SetPhase("Rozpakowywanie... " + Pct(p.Downloaded, p.Total), (int)(400 * p.Downloaded / Math.Max(1, p.Total)))));

            string src = global::NarzedziaIP.Updater.ResolveSource(staging, exeName);
            Log("source dir: " + src);

            Log("copying -> " + targetDir);
            global::NarzedziaIP.Updater.CopyTree(src, targetDir, global::NarzedziaIP.Updater.PreservedFiles,
                new Progress<global::NarzedziaIP.DownloadProgress>(
                    p => SetPhase("Kopiowanie plików... " + Pct(p.Downloaded, p.Total), 400 + (int)(550 * p.Downloaded / Math.Max(1, p.Total)))), 5);

            SetPhase("Uruchamianie nowej wersji...", 980);
            string exeFull = Path.Combine(targetDir, exeName);
            Log("starting " + exeFull);
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exeFull,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            };
            Process.Start(psi);

            try { File.Delete(zip); }
            catch { }
            try { Directory.Delete(staging, true); }
            catch { }

            Log("done");
            SetPhase("Gotowe — nowa wersja uruchomiona.", 1000);
            Dispatcher.Invoke(() =>
            {
                _busy = false;
                DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (s, e) => { t.Stop(); Close(); };
                t.Start();
            });
        }

        private void Fail(Exception ex)
        {
            try { Log("FAILED" + Environment.NewLine + ex); }
            catch { }
            _failed = true;
            _busy = false;
            UiLog(ex.GetType().Name + ": " + ex.Message);
            UiLog("Szczegóły zapisano w:" + Environment.NewLine + _logPath);
            Dispatcher.Invoke(() =>
            {
                lblPhase.Text = "Błąd aktualizacji.";
                try { btnInstall.Visibility = Visibility.Collapsed; }
                catch { }
                btnClose.IsEnabled = true;
                try { Activate(); }
                catch { }
            });
        }

        private void StandaloneCheck()
        {
            try
            {
                string selfDir = null;
                try { selfDir = Path.GetDirectoryName(global::NarzedziaIP.Updater.ExePath()); }
                catch { selfDir = null; }
                if (string.IsNullOrWhiteSpace(selfDir))
                    selfDir = AppDomain.CurrentDomain.BaseDirectory;
                Log("dir=" + selfDir);

                string mainExe = global::NarzedziaIP.Updater.FindMainExe(selfDir);
                if (string.IsNullOrWhiteSpace(mainExe))
                    throw new InvalidOperationException(
                        "Nie znaleziono programu do aktualizacji obok instalatora (" + selfDir + ").");
                string installed = global::NarzedziaIP.Updater.InstalledVersion(mainExe);
                if (string.IsNullOrWhiteSpace(installed))
                    throw new InvalidOperationException("Nie udało się odczytać wersji: " + mainExe);
                Log("installed=" + installed + " exe=" + mainExe);

                SetPhase("Sprawdzanie dostępnej wersji...", null);
                global::NarzedziaIP.ReleaseInfo info;
                try
                {
                    info = Task.Run(() =>
                        global::NarzedziaIP.Updater.FetchLatestReleaseAsync(15)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Błąd sprawdzania aktualizacji: " + ex.Message, ex);
                }

                bool newer;
                try { newer = global::NarzedziaIP.Updater.IsNewer(info.Tag, installed); }
                catch { newer = false; }
                if (!newer)
                {
                    Log("up to date");
                    SetPhase("Masz aktualną wersję (" + installed + ").", 1000);
                    Dispatcher.Invoke(() =>
                    {
                        _busy = false;
                        btnClose.IsEnabled = true;
                    });
                    return;
                }

                _standaloneInfo = info;
                _standaloneExe = mainExe;
                Log("update available: " + info.Tag);
                string notes = (info.Body ?? string.Empty).Trim();
                if (notes.Length > 2000)
                    notes = notes.Substring(0, 2000) + "...";
                Dispatcher.Invoke(() =>
                {
                    lblHeading.Text = "Dostępna jest nowa wersja: " + info.Tag + " (obecna: " + installed + ")";
                    lblPhase.Text = "Pobierz i zainstaluj nową wersję.";
                    if (!string.IsNullOrWhiteSpace(notes))
                        txtLog.AppendText(notes + Environment.NewLine);
                    btnInstall.Visibility = Visibility.Visible;
                    btnInstall.IsEnabled = true;
                    btnClose.IsEnabled = true;
                    _busy = false;
                });
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private async void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!_standalone)
                return;
            global::NarzedziaIP.ReleaseInfo info = _standaloneInfo;
            string mainExe = _standaloneExe;
            if (info == null || string.IsNullOrWhiteSpace(mainExe))
                return;

            btnInstall.IsEnabled = false;
            btnClose.IsEnabled = false;
            _busy = true;

            string targetDir = Path.GetDirectoryName(mainExe);
            string exeName = Path.GetFileName(mainExe);
            string dest = null;
            try
            {
                dest = global::NarzedziaIP.Updater.TempDownloadPath(info.Tag);
                var dlProgress = new Progress<global::NarzedziaIP.DownloadProgress>(p =>
                {
                    if (p.Total > 0)
                    {
                        progress.IsIndeterminate = false;
                        progress.Value = Math.Max(0, Math.Min(1000, (int)(1000 * p.Downloaded / p.Total)));
                    }
                    else
                    {
                        progress.IsIndeterminate = true;
                    }
                    lblPhase.Text = "Pobieranie " + info.Tag + "... " + Pct(p.Downloaded, p.Total);
                });
                await global::NarzedziaIP.Updater.DownloadAssetAsync(
                    info.ZipUrl, dest, dlProgress, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                try { if (dest != null) File.Delete(dest); }
                catch { }
                Fail(ex);
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    foreach (Tuple<int, int> r in FindRunningApp(mainExe))
                    {
                        SetPhase("Oczekiwanie na zamknięcie programu...", null);
                        Log("waiting for PID " + r.Item1);
                        global::NarzedziaIP.Updater.WaitForExit(r.Item1, r.Item2, 0.5);
                    }
                    Log("app closed, installing");
                    InstallFromZip(dest, targetDir, exeName, "standalone");
                }).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                try { if (dest != null) File.Delete(dest); }
                catch { }
                Fail(ex);
            }
        }

        // PID-y działających instancji programu z NASZEGO katalogu
        // (cudze procesy o tej samej nazwie ignorujemy).
        private static List<Tuple<int, int>> FindRunningApp(string mainExe)
        {
            List<Tuple<int, int>> res = new List<Tuple<int, int>>();
            string dir = null;
            string name = null;
            try
            {
                dir = Path.GetDirectoryName(mainExe);
                name = Path.GetFileNameWithoutExtension(mainExe);
            }
            catch { }
            if (string.IsNullOrWhiteSpace(name))
                return res;
            Process[] procs;
            try { procs = Process.GetProcessesByName(name); }
            catch { return res; }
            foreach (Process p in procs)
            {
                try
                {
                    int pid = p.Id;
                    string path = null;
                    try { path = p.MainModule.FileName; }
                    catch { path = null; }
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        if (string.IsNullOrWhiteSpace(path)
                            || !string.Equals(Path.GetDirectoryName(path), dir, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }
                    res.Add(Tuple.Create(pid, global::NarzedziaIP.Updater.ProcStartUnix(pid)));
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            return res;
        }
    }
}
