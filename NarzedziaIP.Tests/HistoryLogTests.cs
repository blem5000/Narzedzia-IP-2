using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class HistoryLogTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_hist_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [DataTestMethod]
        [DataRow("zwykły", "zwykły")]
        [DataRow("a;b", "\"a;b\"")]
        [DataRow("c\"d", "\"c\"\"d\"")]
        [DataRow("l1\nl2", "\"l1\nl2\"")]
        [DataRow(null, "")]
        [DataRow("", "")]
        public void EscapeCsv(string input, string expected)
        {
            Assert.AreEqual(expected, HistoryLog.EscapeCsv(input));
        }

        [TestMethod]
        public void AppendWritesHeaderOnce()
        {
            string path = Path.Combine(_tmp, "h.csv");
            DateTime t = new DateTime(2026, 10, 8, 12, 0, 0);
            Assert.IsTrue(HistoryLog.AppendTo(path, t, "Szukaj", "PC1", "10.0.0.1", ""));
            Assert.IsTrue(HistoryLog.AppendTo(path, t, "MSRA", "10.0.0.1", "10.0.0.1", ""));
            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("Data;Akcja;Cel;Wynik;Uwaga", lines[0]);
            Assert.AreEqual("2026-10-08 12:00:00;Szukaj;PC1;10.0.0.1;", lines[1]);
            Assert.AreEqual("2026-10-08 12:00:00;MSRA;10.0.0.1;10.0.0.1;", lines[2]);
        }

        [TestMethod]
        public void InvalidPathReturnsFalseWithoutThrow()
        {
            Assert.IsFalse(HistoryLog.AppendTo("", DateTime.Now, "a", "b", "c", "d"));
            Assert.IsFalse(HistoryLog.AppendTo(null, DateTime.Now, "a", "b", "c", "d"));
        }

        [TestMethod]
        public void DefaultPathPointsToProfile()
        {
            string path = HistoryLog.DefaultLogPath();
            Assert.IsTrue(path.EndsWith("historia.csv"), path);
            StringAssert.Contains(path, "NarzedziaIP2");
        }
    }
}
