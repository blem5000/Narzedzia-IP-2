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

        [TestMethod]
        public void TrimKeepsHeaderPlusLastLines()
        {
            string path = Path.Combine(_tmp, "h.csv");
            DateTime t = new DateTime(2026, 10, 8, 12, 0, 0);
            for (int i = 0; i < 8; i++)
                HistoryLog.AppendTo(path, t.AddMinutes(i), "Szukaj", "PC" + i, "10.0.0." + i, "");
            Assert.IsTrue(HistoryLog.TrimToMaxLines(path, 5));
            string[] lines = File.ReadAllLines(path);
            Assert.AreEqual(6, lines.Length);
            Assert.AreEqual("Data;Akcja;Cel;Wynik;Uwaga", lines[0]);
            StringAssert.Contains(lines[1], "PC3");
            StringAssert.Contains(lines[5], "PC7");
        }

        [TestMethod]
        public void TrimShortFileUntouched()
        {
            string path = Path.Combine(_tmp, "h.csv");
            HistoryLog.AppendTo(path, DateTime.Now, "Szukaj", "PC1", "10.0.0.1", "");
            Assert.IsTrue(HistoryLog.TrimToMaxLines(path, 5000));
            Assert.AreEqual(2, File.ReadAllLines(path).Length);
        }

        [TestMethod]
        public void TrimMissingFileIsFalse()
        {
            Assert.IsFalse(HistoryLog.TrimToMaxLines(Path.Combine(_tmp, "nie-ma.csv"), 5));
            Assert.IsFalse(HistoryLog.TrimToMaxLines(null, 5));
        }
    }
}
