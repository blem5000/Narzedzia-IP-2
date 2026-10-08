using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class UpdaterVersionTests
    {
        [DataTestMethod]
        [DataRow("v1.1.0", "1.1.0")]
        [DataRow("1.35.0", "1.35.0")]
        [DataRow("v2", "2.0.0")]
        [DataRow("v1.10.0", "1.10.0")]
        [DataRow("  V3.4b.5 ", "3.4.5")]
        [DataRow(null, "0.0.0")]
        [DataRow("", "0.0.0")]
        public void ParseVersion(string input, string expected)
        {
            Assert.AreEqual(expected, string.Join(".", Updater.ParseVersion(input)));
        }

        [DataTestMethod]
        [DataRow("v1.1.0", "1.0.0", true)]
        [DataRow("v1.1.0", "1.1.0", false)]
        [DataRow("v1.2.0", "1.10.0", false)] // numerycznie, nie leksykograficznie
        [DataRow("v2.0", "1.99.99", true)]
        [DataRow("v1.0.0", "1.1.0", false)]
        [DataRow("v1.1.1", "1.1.0", true)]
        public void IsNewer(string latest, string current, bool expected)
        {
            Assert.AreEqual(expected, Updater.IsNewer(latest, current));
        }

        [TestMethod]
        public void CurrentVersionLooksLikeDottedNumber()
        {
            string v = Updater.CurrentVersion();
            Assert.IsFalse(string.IsNullOrWhiteSpace(v));
            Assert.IsTrue(v.Split('.').Length >= 3);
        }
    }

    [TestClass]
    public class UpdaterReleaseJsonTests
    {
        private const string BaseJson = "{\"tag_name\":\"v1.1.0\",\"name\":\"v1.1.0\",\"body\":\"notes\",";

        [TestMethod]
        public void ExactAssetNameWins()
        {
            string json = BaseJson + "\"assets\":["
                + "{\"name\":\"other.zip\",\"browser_download_url\":\"https://example.com/other.zip\",\"size\":1},"
                + "{\"name\":\"NarzedziaIP2-windows.zip\",\"browser_download_url\":\"https://example.com/main.zip\",\"size\":12345}]}";
            ReleaseInfo r = Updater.ParseReleaseJson(json);
            Assert.AreEqual("v1.1.0", r.Tag);
            Assert.AreEqual("https://example.com/main.zip", r.ZipUrl);
            Assert.AreEqual(12345, r.ZipSize);
            Assert.AreEqual("notes", r.Body);
        }

        [TestMethod]
        public void FallsBackToFirstZip()
        {
            string json = "{\"tag_name\":\"v1.2.0\",\"assets\":["
                + "{\"name\":\"foo.zip\",\"browser_download_url\":\"https://example.com/foo.zip\"}]}";
            Assert.AreEqual("https://example.com/foo.zip", Updater.ParseReleaseJson(json).ZipUrl);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void NoZipAssetThrows()
        {
            Updater.ParseReleaseJson("{\"tag_name\":\"v1.2.0\",\"assets\":[]}");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EmptyTagThrows()
        {
            Updater.ParseReleaseJson("{\"tag_name\":\"\",\"assets\":[]}");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void InvalidJsonThrows()
        {
            Updater.ParseReleaseJson("to nie jest json");
        }
    }

    [TestClass]
    public class UpdaterFileTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [TestMethod]
        public void TempDownloadPathIsUniqueZip()
        {
            string a = Updater.TempDownloadPath("v1.1.0");
            string b = Updater.TempDownloadPath("v1.1.0");
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(File.Exists(a), "sama ścieżka nie tworzy pliku");
        }

        [TestMethod]
        public void ProcStartUnixOwnProcessPositive()
        {
            Assert.IsTrue(Updater.ProcStartUnix(Process.GetCurrentProcess().Id) > 0);
        }

        [TestMethod]
        public void ProcStartUnixInvalidPidIsZero()
        {
            Assert.AreEqual(0, Updater.ProcStartUnix(0));
            Assert.AreEqual(0, Updater.ProcStartUnix(-5));
        }

        [TestMethod]
        public void WaitForExitOnDeadProcessReturnsTrue()
        {
            Process p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 0",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p.WaitForExit();
            int pid = p.Id;
            p.Dispose();
            Assert.IsTrue(Updater.WaitForExit(pid, 0, 0.1));
        }

        [TestMethod]
        public void ResolveSourcePrefersInnerFolder()
        {
            string staging = Path.Combine(_tmp, "stage");
            string inner = Path.Combine(staging, Updater.InnerFolderName);
            Directory.CreateDirectory(inner);
            File.WriteAllText(Path.Combine(inner, "Narzedzia IP 2.exe"), "x");
            Assert.AreEqual(inner, Updater.ResolveSource(staging, "Narzedzia IP 2.exe"));
        }

        [TestMethod]
        public void ResolveSourceFallsBackToStaging()
        {
            string staging = Path.Combine(_tmp, "flat");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "Narzedzia IP 2.exe"), "x");
            Assert.AreEqual(staging, Updater.ResolveSource(staging, "Narzedzia IP 2.exe"));
        }

        [TestMethod]
        public void CopyTreeCopiesAndSkipsExcluded()
        {
            string src = Path.Combine(_tmp, "src");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "b");
            File.WriteAllText(Path.Combine(src, "user.log"), "keep-me");
            string dst = Path.Combine(_tmp, "dst");

            Updater.CopyTree(src, dst, new[] { "user.log" }, null, 1);

            Assert.AreEqual("a", File.ReadAllText(Path.Combine(dst, "a.txt")));
            Assert.AreEqual("b", File.ReadAllText(Path.Combine(dst, "sub", "b.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(dst, "user.log")));
        }

        [TestMethod]
        public void ExtractZipRoundtrip()
        {
            string src = Path.Combine(_tmp, "zsrc");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "f.txt"), "hello");
            string zip = Path.Combine(_tmp, "a.zip");
            ZipFile.CreateFromDirectory(src, zip);
            string staging = Path.Combine(_tmp, "zstage");

            Updater.ExtractZip(zip, staging, null);

            Assert.AreEqual("hello", File.ReadAllText(Path.Combine(staging, "f.txt")));
        }

        [TestMethod]
        public void WriteApplyScriptContainsMarkers()
        {
            string zip = Path.Combine(_tmp, "u.zip");
            File.WriteAllText(zip, "x");
            string bat = null;
            try
            {
                bat = Updater.WriteApplyScript(zip, _tmp, 1234);
                string content = File.ReadAllText(bat);
                StringAssert.Contains(content, "robocopy");
                StringAssert.Contains(content, "1234");
                StringAssert.Contains(content, "Expand-Archive");
                StringAssert.Contains(content, Updater.ExeName());
            }
            finally
            {
                if (bat != null)
                    try { File.Delete(bat); }
                    catch { }
            }
        }
    }
}
