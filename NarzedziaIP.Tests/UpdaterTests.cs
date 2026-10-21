using System;
using System.Collections.Generic;
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

        // Zip Slip: nazwa wpisu w archiwum moze wskazac poza katalog staging.
        // ZipFileExtensions.ExtractToFile nie waliduje sciezki, wiec
        // Updater.ExtractZip musi pilnowac tego sam.
        [TestMethod]
        public void ExtractZipRejectsPathTraversalEntry()
        {
            string zip = Path.Combine(_tmp, "evil.zip");
            using (FileStream fs = new FileStream(zip, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../evil_slipped.txt");
                using (StreamWriter w = new StreamWriter(entry.Open()))
                    w.Write("pwned");
            }

            string staging = Path.Combine(_tmp, "zstage");
            try
            {
                Updater.ExtractZip(zip, staging, null);
                Assert.Fail("ExtractZip powinien odrzucic wpis wychodzacy poza staging");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Zip Slip");
            }

            Assert.IsFalse(File.Exists(Path.Combine(_tmp, "evil_slipped.txt")),
                "plik nie moze trafic poza katalog staging");
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetTempPath(), "evil_slipped.txt")),
                "plik nie moze trafic do katalogu tymczasowego");
        }

        [TestMethod]
        public void ExtractZipRejectsTraversalDirectoryEntry()
        {
            string zip = Path.Combine(_tmp, "evildir.zip");
            using (FileStream fs = new FileStream(zip, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("../evil_dir/");
                using (StreamWriter w = new StreamWriter(entry.Open()))
                    w.Write("x");
            }

            string staging = Path.Combine(_tmp, "zstage");
            try
            {
                Updater.ExtractZip(zip, staging, null);
                Assert.Fail("ExtractZip powinien odrzucic katalog wychodzacy poza staging");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Zip Slip");
            }

            Assert.IsFalse(Directory.Exists(Path.Combine(_tmp, "evil_dir")));
        }

        [TestMethod]
        public void ExtractZipRejectsAbsoluteEntryPath()
        {
            string zip = Path.Combine(_tmp, "abs.zip");
            using (FileStream fs = new FileStream(zip, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("C:/evil_abs.txt");
                using (StreamWriter w = new StreamWriter(entry.Open()))
                    w.Write("x");
            }

            string staging = Path.Combine(_tmp, "zstage");
            try
            {
                Updater.ExtractZip(zip, staging, null);
                Assert.Fail("ExtractZip powinien odrzucic bezwzgledna sciezke");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Zip Slip");
            }
        }

        [TestMethod]
        public void ResolveSafeTargetKeepsNestedPathsInside()
        {
            string staging = Path.Combine(_tmp, "stage");
            Directory.CreateDirectory(staging);
            string root = Path.GetFullPath(staging);

            string nested = Updater.ResolveSafeTarget(staging, "sub/dir/file.txt");
            Assert.IsTrue(nested.StartsWith(root, StringComparison.OrdinalIgnoreCase));

            // ".." w srodku sciezki jest legalny, o ile wynik zostaje w staging
            string normalized = Updater.ResolveSafeTarget(staging, "sub/../ok.txt");
            Assert.IsTrue(normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void ResolveSafeTargetRejectsEscapes()
        {
            string staging = Path.Combine(_tmp, "stage");
            Directory.CreateDirectory(staging);

            AssertThrowsZipSlip(staging, "../evil.txt");
            AssertThrowsZipSlip(staging, "..\\evil.txt");
            AssertThrowsZipSlip(staging, "sub/../../evil.txt");
            AssertThrowsZipSlip(staging, "C:/evil.txt");
            AssertThrowsZipSlip(staging, "");
        }

        private static void AssertThrowsZipSlip(string staging, string entryName)
        {
            try
            {
                Updater.ResolveSafeTarget(staging, entryName);
                Assert.Fail("ResolveSafeTarget powinien odrzucic: " + entryName);
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Zip Slip");
            }
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

        [TestMethod]
        public void ApplyScriptPassesPathsViaEnvironment()
        {
            // Sciezki nie moga byc wkładane w cudzyslowy PowerShella -
            // apostrof w sciezce rozbilby '...' i skrypt wysypalby sie
            // (albo, gorzej, wstrzyknal komende).
            string zip = Path.Combine(_tmp, "u.zip");
            File.WriteAllText(zip, "x");
            string bat = null;
            try
            {
                bat = Updater.WriteApplyScript(zip, _tmp, 1234);
                string content = File.ReadAllText(bat);
                StringAssert.Contains(content, "$env:ZIP");
                StringAssert.Contains(content, "$env:STAGE");
                Assert.IsFalse(content.Contains("-LiteralPath '"),
                    "sciezka nie moze byc wkładana w cudzyslowy PowerShella");
            }
            finally
            {
                if (bat != null)
                    try { File.Delete(bat); }
                    catch { }
            }
        }

        // Skrypt .bat zawiera komende PowerShell - w niej "catch { }",
        // a nie C#-owe "catch (Exception ...)".
        [TestMethod]
        public void ApplyScriptUsesValidPowerShellCatchSyntax()
        {
            string zip = Path.Combine(_tmp, "u.zip");
            File.WriteAllText(zip, "x");
            string bat = null;
            try
            {
                bat = Updater.WriteApplyScript(zip, _tmp, 1234);
                string content = File.ReadAllText(bat);
                StringAssert.Contains(content, "catch { }");
                Assert.IsFalse(content.Contains("catch (Exception"),
                    "skrypt PowerShell nie zna 'catch (Exception)'");
            }
            finally
            {
                if (bat != null)
                    try { File.Delete(bat); }
                    catch { }
            }
        }
    }

    [TestClass]
    public class UpdaterPackageValidationTests
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

        // Dokladnie taki zestaw plikow jak w paczce release'a.
        private void WriteReleaseLikePackage()
        {
            File.WriteAllText(Path.Combine(_tmp, "Narzedzia IP 2.exe"), "x");
            File.WriteAllText(Path.Combine(_tmp, "Narzedzia IP 2.exe.config"), "<configuration/>");
            File.WriteAllText(Path.Combine(_tmp, "NarzedziaIP.Updater.exe"), "x");
            // instrukcja jest dostarczana jako .txt (nie .md) - patrz test
            // PackagePassesValidationOfOlderUpdaters
            File.WriteAllText(Path.Combine(_tmp, "Instrukcja.txt"), "# instrukcja");
        }

        [TestMethod]
        public void AcceptsRealPackageContent()
        {
            // Instrukcja w paczce musi przechodzic walidacje, inaczej
            // aktualizacja konczy sie bledem "Paczka zawiera niedozwolone
            // pliki" i programu nie da sie zaktualizowac.
            WriteReleaseLikePackage();
            Updater.ValidatePackageFiles(_tmp, "Narzedzia IP 2.exe");
        }

        [TestMethod]
        public void PackagePassesValidationOfOlderUpdaters()
        {
            // Paczke waliduje ZAINSTALOWANY updater, nie nowy. Gdy uzytkownik
            // ma stara wersje programu, ten stary updater musi te paczke
            // przyjac - inaczej aktualizacja do nowej wersji sie nie powiedzie.
            // Dozwolone rozszerzenia w wersji 1.1.32: .dll .config .pdb .xml .txt
            // (+ dwa .exe o znanych nazwach).
            WriteReleaseLikePackage();

            HashSet<string> oldAllowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".dll", ".config", ".pdb", ".xml", ".txt"
            };
            HashSet<string> oldAllowedExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Narzedzia IP 2.exe", "NarzedziaIP.Updater.exe"
            };

            foreach (string file in Directory.GetFiles(_tmp, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file) ?? string.Empty;
                string name = Path.GetFileName(file);
                if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                    Assert.IsTrue(oldAllowedExe.Contains(name), "stary updater odrzuci: " + name);
                else
                    Assert.IsTrue(oldAllowedExt.Contains(ext), "stary updater odrzuci: " + name);
            }
        }

        [TestMethod]
        public void RejectsUnknownExe()
        {
            WriteReleaseLikePackage();
            File.WriteAllText(Path.Combine(_tmp, "zlosliwy.exe"), "x");
            try
            {
                Updater.ValidatePackageFiles(_tmp, "Narzedzia IP 2.exe");
                Assert.Fail("nieznany .exe powinien byc odrzucony");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "niedozwolone pliki");
            }
        }

        [TestMethod]
        public void RejectsScripts()
        {
            WriteReleaseLikePackage();
            File.WriteAllText(Path.Combine(_tmp, "skrypt.ps1"), "x");
            try
            {
                Updater.ValidatePackageFiles(_tmp, "Narzedzia IP 2.exe");
                Assert.Fail("skrypt powinien byc odrzucony");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "niedozwolone pliki");
            }
        }

        [TestMethod]
        public void RequiresMainExe()
        {
            File.WriteAllText(Path.Combine(_tmp, "Instrukcja.txt"), "# instrukcja");
            try
            {
                Updater.ValidatePackageFiles(_tmp, "Narzedzia IP 2.exe");
                Assert.Fail("brak glownego exe powinien byc wykryty");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "wymaganego pliku");
            }
        }
    }

    [TestClass]
    public class UpdaterSha256Tests
    {
        private const string ValidHash =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_sha_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [TestMethod]
        public void ParsesHashFromReleaseNotes()
        {
            string body = "Automatyczny release z CI.\n\nSHA256: " + ValidHash + "\n";
            Assert.AreEqual(ValidHash, Updater.ParseReleaseSha256(body));
        }

        [TestMethod]
        public void ParsesHashWithEqualsAndSpaces()
        {
            Assert.AreEqual(ValidHash, Updater.ParseReleaseSha256("SHA256=" + ValidHash));
            Assert.AreEqual(ValidHash, Updater.ParseReleaseSha256("sha256:   " + ValidHash.ToUpperInvariant()));
        }

        [TestMethod]
        public void NoHashInNotesGivesNull()
        {
            Assert.IsNull(Updater.ParseReleaseSha256(null));
            Assert.IsNull(Updater.ParseReleaseSha256(""));
            Assert.IsNull(Updater.ParseReleaseSha256("Release bez hasha."));
            Assert.IsNull(Updater.ParseReleaseSha256("SHA256: za-krotki"),
                "hash musi miec 64 znaki hex");
        }

        [TestMethod]
        public void ReleaseJsonCarriesHash()
        {
            string json = "{\"tag_name\":\"v1.1.26\",\"name\":\"v1.1.26\",\"body\":\"SHA256: " + ValidHash + "\","
                + "\"assets\":[{\"name\":\"NarzedziaIP2-windows.zip\","
                + "\"browser_download_url\":\"https://example.com/main.zip\",\"size\":1}]}";
            ReleaseInfo r = Updater.ParseReleaseJson(json);
            Assert.AreEqual(ValidHash, r.Sha256);
        }

        [TestMethod]
        public void ComputesHashOfFile()
        {
            string file = Path.Combine(_tmp, "dane.txt");
            File.WriteAllText(file, "abc");
            // sha256("abc")
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                Updater.ComputeFileSha256(file));
        }

        [TestMethod]
        public void VerifyPassesForMatchingHash()
        {
            string file = Path.Combine(_tmp, "dane.txt");
            File.WriteAllText(file, "abc");
            string hash = Updater.ComputeFileSha256(file);
            Updater.VerifyAssetSha256(file, hash); // nie rzuca
            Updater.VerifyAssetSha256(file, hash.ToUpperInvariant()); // case-insensitive
        }

        [TestMethod]
        public void VerifyThrowsForMismatchedHash()
        {
            string file = Path.Combine(_tmp, "dane.txt");
            File.WriteAllText(file, "abc");
            try
            {
                Updater.VerifyAssetSha256(file, ValidHash);
                Assert.Fail("niezgodny SHA-256 powinien przerwac aktualizacje");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "SHA-256");
            }
        }

        [TestMethod]
        public void VerifySkipsWhenNoHashPublished()
        {
            // Starsze release'y nie maja hasha - pomijamy weryfikacje,
            // inaczej aktualizacja do v1.1.25 bylaby niemozliwa.
            string file = Path.Combine(_tmp, "dane.txt");
            File.WriteAllText(file, "abc");
            Updater.VerifyAssetSha256(file, null);
            Updater.VerifyAssetSha256(file, "");
        }
    }
}
