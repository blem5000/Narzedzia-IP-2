using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    // Tryb standalone updatera (double-click bez parametrów):
    // wykrywanie exe obok siebie + odczyt jego wersji.
    [TestClass]
    public class UpdaterStandaloneTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_sa_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [TestMethod]
        public void MissingDirGivesNull()
        {
            Assert.IsNull(Updater.FindMainExe(Path.Combine(_tmp, "nope")));
            Assert.IsNull(Updater.FindMainExe(null));
            Assert.IsNull(Updater.FindMainExe(""));
        }

        [TestMethod]
        public void EmptyDirGivesNull()
        {
            Assert.IsNull(Updater.FindMainExe(_tmp));
        }

        [TestMethod]
        public void KnownExeNameWins()
        {
            File.WriteAllText(Path.Combine(_tmp, Updater.MainExeName), "x");
            File.WriteAllText(Path.Combine(_tmp, "inne.exe"), "y");
            Assert.AreEqual(Path.Combine(_tmp, Updater.MainExeName), Updater.FindMainExe(_tmp));
        }

        [TestMethod]
        public void FallbackToOtherExe()
        {
            string other = Path.Combine(_tmp, "program.exe");
            File.WriteAllText(other, "y");
            Assert.AreEqual(other, Updater.FindMainExe(_tmp));
        }

        [TestMethod]
        public void InstalledVersionReadsFileVersion()
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            string v = Updater.InstalledVersion(self);
            Assert.IsFalse(string.IsNullOrWhiteSpace(v));
            Assert.IsTrue(v.Split('.').Length >= 3);
        }

        [TestMethod]
        public void InstalledVersionMissingGivesNull()
        {
            Assert.IsNull(Updater.InstalledVersion(Path.Combine(_tmp, "nie-ma.exe")));
            Assert.IsNull(Updater.InstalledVersion(null));
        }

        [TestMethod]
        public void StandaloneDecisionNewerMeansUpdate()
        {
            // Ta sama reguła co dialog: porównanie wykrytej wersji z tagiem.
            Assert.IsTrue(Updater.IsNewer("v9.9.9", "1.0.0"));
            Assert.IsFalse(Updater.IsNewer("v0.0.1", "99.99.99"));
        }
    }
}
