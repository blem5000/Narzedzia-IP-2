using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    // Testy przepływu autoupdate: mają łapać powtórki dwóch realnych awarii:
    // (1) deadlock startu v1.1.0 - blokowanie wątku UI na metodzie async
    //     wznawiającej się na kontekście UI (wisiał proces bez okna),
    // (2) rozjechane argumenty instalatora dla ścieżek UNC ze spacjami.
    // Logika decyzji jest czysta (EvaluateBootAction), fetch idzie przez
    // lokalny stub HTTP (zero zależności od sieci/GitHuba).
    [TestClass]
    public class BootDecisionTests
    {
        private static ReleaseInfo Info(string tag)
        {
            return new ReleaseInfo { Tag = tag, Name = tag, Body = "", ZipUrl = "https://example.com/u.zip", ZipSize = 1 };
        }

        [TestMethod]
        public void AutoDisabledStartsApp()
        {
            Assert.AreEqual(BootAction.StartApp,
                BootUpdate.EvaluateBootAction(false, Info("v9.9.9"), "1.0.0", null, true));
        }

        [TestMethod]
        public void FetchFailureStartsApp()
        {
            Assert.AreEqual(BootAction.StartApp,
                BootUpdate.EvaluateBootAction(true, null, "1.0.0", null, true));
        }

        [TestMethod]
        public void SameVersionStartsApp()
        {
            Assert.AreEqual(BootAction.StartApp,
                BootUpdate.EvaluateBootAction(true, Info("v1.1.3"), "1.1.3", null, true));
        }

        [TestMethod]
        public void OlderReleaseStartsApp()
        {
            Assert.AreEqual(BootAction.StartApp,
                BootUpdate.EvaluateBootAction(true, Info("v1.0.0"), "1.1.3", null, true));
        }

        [TestMethod]
        public void SkippedVersionStartsApp()
        {
            Assert.AreEqual(BootAction.StartApp,
                BootUpdate.EvaluateBootAction(true, Info("v1.2.0"), "1.1.3", "v1.2.0", true));
        }

        [TestMethod]
        public void NewerVersionShowsDialog()
        {
            Assert.AreEqual(BootAction.ShowDownloadDialog,
                BootUpdate.EvaluateBootAction(true, Info("v1.2.0"), "1.1.3", null, true));
        }

        [TestMethod]
        public void OtherSkippedVersionStillShowsDialog()
        {
            Assert.AreEqual(BootAction.ShowDownloadDialog,
                BootUpdate.EvaluateBootAction(true, Info("v1.2.0"), "1.1.3", "v9.9.9", true));
        }

        [TestMethod]
        public void MissingUpdaterDefersToMainDialog()
        {
            Assert.AreEqual(BootAction.DeferToMainDialog,
                BootUpdate.EvaluateBootAction(true, Info("v1.2.0"), "1.1.3", null, false));
        }
    }

    [TestClass]
    public class FetchHttpTests
    {
        // Minimalny stub HTTP na TcpListener (bez http.sys i admina):
        // jedno połączenie, jedna odpowiedź, koniec.
        private static int StartStubServer(string body, int statusCode)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run(() =>
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        string line;
                        do { line = reader.ReadLine(); } while (!string.IsNullOrEmpty(line));
                        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                        string reason = statusCode == 200 ? "OK" : "Not Found";
                        string header = "HTTP/1.1 " + statusCode + " " + reason + "\r\n"
                            + "Content-Type: application/json\r\n"
                            + "Content-Length: " + bodyBytes.Length + "\r\n"
                            + "Connection: close\r\n\r\n";
                        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                        stream.Write(headerBytes, 0, headerBytes.Length);
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                }
                catch { }
                finally
                {
                    try { listener.Stop(); }
                    catch { }
                }
            });
            return port;
        }

        private static string ReleaseJson(string tag, string asset, string url)
        {
            return "{\"tag_name\":\"" + tag + "\",\"name\":\"" + tag + "\",\"body\":\"n\","
                + "\"assets\":[{\"name\":\"" + asset + "\",\"browser_download_url\":\"" + url + "\",\"size\":7}]}";
        }

        [TestMethod]
        public async Task FetchParsesStubRelease()
        {
            int port = StartStubServer(
                ReleaseJson("v9.9.9", Updater.AssetName, "https://example.com/x.zip"), 200);
            ReleaseInfo r = await Updater.FetchLatestReleaseAsync(10,
                "http://127.0.0.1:" + port + "/releases/latest");
            Assert.AreEqual("v9.9.9", r.Tag);
            Assert.AreEqual("https://example.com/x.zip", r.ZipUrl);
        }

        [TestMethod]
        public async Task FetchHttpErrorThrows()
        {
            int port = StartStubServer("{}", 404);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                Updater.FetchLatestReleaseAsync(10, "http://127.0.0.1:" + port + "/releases/latest"));
        }

        [TestMethod]
        public async Task FetchInvalidJsonThrows()
        {
            int port = StartStubServer("nie-json", 200);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                Updater.FetchLatestReleaseAsync(10, "http://127.0.0.1:" + port + "/releases/latest"));
        }

        // Kontekst wątku UI, który nigdy nie pompuje komunikatów - dokładnie
        // sytuacja z App.OnStartup. Gdyby fetch wznawiał się na kontekście
        // (ConfigureAwait(true)), blokujące wywołanie wisiałoby jak v1.1.0.
        private sealed class NeverPumpContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object state) { }
            public override void Send(SendOrPostCallback d, object state) { }
        }

        [TestMethod]
        public void BlockingFetchFromUiLikeThreadDoesNotDeadlock()
        {
            int port = StartStubServer(
                ReleaseJson("v9.9.9", Updater.AssetName, "https://example.com/x.zip"), 200);
            string url = "http://127.0.0.1:" + port + "/releases/latest";

            ReleaseInfo result = null;
            Exception error = null;
            Thread ui = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new NeverPumpContext());
                try
                {
                    // Ten sam kształt co BootUpdate.TrySilentUpdate():
                    // blokada wątku "UI" na Task.Run(...).GetResult().
                    // Kontekst ustawiony też w workerze (Task.Run nie musi
                    // go propagować) - deterministyczna symulacja v1.1.0.
                    result = Task.Run(() =>
                    {
                        SynchronizationContext.SetSynchronizationContext(new NeverPumpContext());
                        return Updater.FetchLatestReleaseAsync(10, url).GetAwaiter().GetResult();
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            ui.IsBackground = true;
            ui.Start();
            bool finished = ui.Join(TimeSpan.FromSeconds(30));

            Assert.IsTrue(finished, "DEADLOCK: blokujące wywołanie z wątku UI nie wróciło w 30 s (regresja v1.1.0)");
            Assert.IsNull(error, "fetch rzucił: " + error);
            Assert.IsNotNull(result);
            Assert.AreEqual("v9.9.9", result.Tag);
        }
    }

    [TestClass]
    public class InstallPipelineTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_pipe_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [TestMethod]
        public void FullPipelineUpdatesBinariesAndPreservesUserFiles()
        {
            // "Zainstalowana" stara wersja.
            string appDir = Path.Combine(_tmp, "app");
            Directory.CreateDirectory(Path.Combine(appDir, "sub"));
            File.WriteAllText(Path.Combine(appDir, "app.exe"), "OLD-BINARY");
            File.WriteAllText(Path.Combine(appDir, "sub", "old.txt"), "old");
            File.WriteAllText(Path.Combine(appDir, "user.log"), "user-data");

            // Paczka wydania z folderem wewnętrznym jak w build_release.ps1.
            string pkgInner = Path.Combine(_tmp, "pkg", Updater.InnerFolderName);
            Directory.CreateDirectory(pkgInner);
            File.WriteAllText(Path.Combine(pkgInner, "app.exe"), "NEW-BINARY");
            File.WriteAllText(Path.Combine(pkgInner, "new.txt"), "new");
            string zip = Path.Combine(_tmp, "u.zip");
            ZipFile.CreateFromDirectory(Path.Combine(_tmp, "pkg"), zip);

            // Ta sama sekwencja co instalator GUI (UpdaterWindow.Worker),
            // minus restart procesu.
            Process p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p.WaitForExit();
            int deadPid = p.Id;
            p.Dispose();

            Assert.IsTrue(Updater.WaitForExit(deadPid, 0, 0.5));
            string staging = Path.Combine(_tmp, "stage");
            Updater.ExtractZip(zip, staging, null);
            string src = Updater.ResolveSource(staging, "app.exe");
            Updater.CopyTree(src, appDir, new[] { "user.log" }, null, 1);

            Assert.AreEqual("NEW-BINARY", File.ReadAllText(Path.Combine(appDir, "app.exe")));
            Assert.AreEqual("new", File.ReadAllText(Path.Combine(appDir, "new.txt")));
            Assert.AreEqual("user-data", File.ReadAllText(Path.Combine(appDir, "user.log")),
                "plik użytkownika musi przetrwać aktualizację");
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(appDir, "sub", "old.txt")),
                "kopiowanie nie usuwa plików spoza paczki (jak robocopy bez /PURGE)");
        }
    }

    [TestClass]
    public class UpdaterPackageTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_pkg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        private string Pkg(params string[] names)
        {
            string dir = Path.Combine(_tmp, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (string n in names)
            {
                string full = Path.Combine(dir, n);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, "x");
            }
            return dir;
        }

        [TestMethod]
        public void ValidPackagePasses()
        {
            string dir = Pkg("app.exe", Updater.UpdaterExeName, "app.exe.config", "lib.dll", "notes.txt");
            Updater.ValidatePackageFiles(dir, "app.exe");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void MissingTargetThrows()
        {
            Updater.ValidatePackageFiles(Pkg(Updater.UpdaterExeName), "app.exe");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ScriptFileThrows()
        {
            Updater.ValidatePackageFiles(Pkg("app.exe", "run.ps1"), "app.exe");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UnexpectedExeThrows()
        {
            Updater.ValidatePackageFiles(Pkg("app.exe", "evil.exe"), "app.exe");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ExtensionlessFileThrows()
        {
            Updater.ValidatePackageFiles(Pkg("app.exe", "README"), "app.exe");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void NestedScriptThrows()
        {
            Updater.ValidatePackageFiles(Pkg("app.exe", "sub\\evil.bat"), "app.exe");
        }
    }

    [TestClass]
    public class UpdaterRenameTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_ren_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [TestMethod]
        public void MissingTargetRenamesTrivially()
        {
            Assert.IsTrue(Updater.TryRenameLockedFile(Path.Combine(_tmp, "nie-ma.exe")));
            Assert.IsFalse(Updater.TryRenameLockedFile(null));
            Assert.IsFalse(Updater.TryRenameLockedFile(""));
        }

        [TestMethod]
        public void NormalCopyLeavesNoBackup()
        {
            string src = Path.Combine(_tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.exe"), "new");
            string dst = Path.Combine(_tmp, "dst");
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, "a.exe"), "old");

            Updater.CopyTree(src, dst, null, null, 1);

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(dst, "a.exe")));
            Assert.IsFalse(File.Exists(Path.Combine(dst, "a.exe.old")));
        }

        [TestMethod]
        public void LockedExeIsSwappedViaRename()
        {
            // Blokada jak mapowany obraz exe: zapis zabroniony, rename dozwolony.
            // Bez rename-swap kopiowanie by rzuciło; po swapie jest nowa zawartość
            // (backup *.old sprzątany jest przez CopyTree, więc sprawdzamy treść).
            string src = Path.Combine(_tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "app.exe"), "NEW-BINARY");
            string dst = Path.Combine(_tmp, "dst");
            Directory.CreateDirectory(dst);
            string target = Path.Combine(dst, "app.exe");
            File.WriteAllText(target, "OLD-BINARY");

            using (FileStream locked = new FileStream(target, FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                Updater.CopyTree(src, dst, null, null, 2);
                Assert.AreEqual("NEW-BINARY", File.ReadAllText(target));
            }

            Updater.CleanupOldBackups(dst);
            Assert.IsFalse(File.Exists(target + ".old"));
        }

        [TestMethod]
        public void TryRenameMovesLockedFileAside()
        {
            string target = Path.Combine(_tmp, "app.exe");
            File.WriteAllText(target, "OLD-BINARY");

            using (FileStream locked = new FileStream(target, FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                Assert.IsTrue(Updater.TryRenameLockedFile(target));
                Assert.IsFalse(File.Exists(target));
                Assert.AreEqual("OLD-BINARY", File.ReadAllText(target + ".old"));
            }
            File.Delete(target + ".old");
        }

        [TestMethod]
        public void CleanupKeepsForeignOldFiles()
        {
            string dst = Path.Combine(_tmp, "dst");
            Directory.CreateDirectory(dst);
            File.WriteAllText(Path.Combine(dst, "app.exe"), "x");
            File.WriteAllText(Path.Combine(dst, "app.exe.old"), "stare");
            File.WriteAllText(Path.Combine(dst, "notatki.old"), "cudze");

            Updater.CleanupOldBackups(dst);

            Assert.IsFalse(File.Exists(Path.Combine(dst, "app.exe.old")));
            Assert.IsTrue(File.Exists(Path.Combine(dst, "notatki.old")));
        }

        [TestMethod]
        public void HardLockedFileStillThrows()
        {
            string src = Path.Combine(_tmp, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "app.exe"), "NEW");
            string dst = Path.Combine(_tmp, "dst");
            Directory.CreateDirectory(dst);
            string target = Path.Combine(dst, "app.exe");
            File.WriteAllText(target, "OLD");

            using (FileStream locked = new FileStream(target, FileMode.Open,
                FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    Updater.CopyTree(src, dst, null, null, 1);
                    Assert.Fail("miał rzucić");
                }
                catch (InvalidOperationException ex)
                {
                    StringAssert.Contains(ex.Message, "app.exe");
                }
            }
        }
    }
}
