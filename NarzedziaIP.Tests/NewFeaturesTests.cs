using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class LookupTests
    {
        [TestMethod]
        public void ByIpCommandFiltersByAddress()
        {
            string cmd = DhcpClient.BuildScopeLeasesByIpCommand("srv", new[] { "10.0.0.0" }, "10.0.0.55");
            StringAssert.Contains(cmd, "Get-DhcpServerv4Lease");
            StringAssert.Contains(cmd, "$targetIP = '10.0.0.55'");
            StringAssert.Contains(cmd, "$_.IPAddress.IPAddressToString -eq $targetIP");
            Assert.IsFalse(cmd.Contains("HostName -like"),
                "wyszukiwanie po IP nie moze filtrowac po nazwie");
        }

        [TestMethod]
        public void ByIpCommandEscapesQuotes()
        {
            string cmd = DhcpClient.BuildScopeLeasesByIpCommand("sr'v", new[] { "10.0.0.0" }, "10.0.0.5'");
            StringAssert.Contains(cmd, "'sr''v'");
            StringAssert.Contains(cmd, "'10.0.0.5'''");
        }

        [TestMethod]
        public void ReservationsByIpCommand()
        {
            string cmd = DhcpClient.BuildReservationsByIpCommand("srv", "10.0.0.5");
            StringAssert.Contains(cmd, "Get-DhcpServerv4Reservation");
            StringAssert.Contains(cmd, "$targetIP = '10.0.0.5'");
            StringAssert.Contains(cmd, "$_.Description");
        }

        [TestMethod]
        public void ParseReservationLines()
        {
            string output = "PC-01\t10.0.0.5\tDzial IT - laptop Kowalskiego\r\n"
                + "PC-02\t10.0.0.5\t\r\n"
                + "uszkodzona-linia\r\n";
            List<DhcpReservation> r = DhcpClient.ParseReservationLines(output);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual("PC-01", r[0].HostName);
            Assert.AreEqual("10.0.0.5", r[0].IPAddress);
            Assert.AreEqual("Dzial IT - laptop Kowalskiego", r[0].Description);
            Assert.AreEqual("PC-02", r[1].HostName);
            Assert.AreEqual("", r[1].Description);
        }

        [TestMethod]
        public void ParseReservationLinesEmpty()
        {
            Assert.AreEqual(0, DhcpClient.ParseReservationLines(null).Count);
            Assert.AreEqual(0, DhcpClient.ParseReservationLines("").Count);
            Assert.AreEqual(0, DhcpClient.ParseReservationLines("   \r\n  ").Count);
        }
    }

    [TestClass]
    public class PingEventLogTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_pingev_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        [TestMethod]
        public void FirstMeasurementIsNotATransition()
        {
            Assert.IsFalse(PingEventLog.IsTransition(null, true));
            Assert.IsFalse(PingEventLog.IsTransition(null, false));
        }

        [TestMethod]
        public void SameStateIsNotATransition()
        {
            Assert.IsFalse(PingEventLog.IsTransition(true, true));
            Assert.IsFalse(PingEventLog.IsTransition(false, false));
        }

        [TestMethod]
        public void ChangedStateIsATransition()
        {
            Assert.IsTrue(PingEventLog.IsTransition(true, false));
            Assert.IsTrue(PingEventLog.IsTransition(false, true));
        }

        [TestMethod]
        public void StateText()
        {
            Assert.AreEqual("UP", PingEventLog.StateText(true));
            Assert.AreEqual("DOWN", PingEventLog.StateText(false));
        }

        [TestMethod]
        public void FormatDuration()
        {
            Assert.AreEqual("", PingEventLog.FormatDuration(null));
            Assert.AreEqual("<1 s", PingEventLog.FormatDuration(TimeSpan.FromMilliseconds(200)));
            Assert.AreEqual("45 s", PingEventLog.FormatDuration(TimeSpan.FromSeconds(45)));
            Assert.AreEqual("12 min 30 s", PingEventLog.FormatDuration(TimeSpan.FromSeconds(750)));
            Assert.AreEqual("3 godz 5 min", PingEventLog.FormatDuration(TimeSpan.FromMinutes(185)));
            Assert.AreEqual("2 dni 4 godz", PingEventLog.FormatDuration(TimeSpan.FromHours(52)));
        }

        [TestMethod]
        public void EscapeForExcel()
        {
            Assert.AreEqual("proste", PingEventLog.Escape("proste"));
            Assert.AreEqual("", PingEventLog.Escape(null));
            Assert.AreEqual("\"z;semikolon\"", PingEventLog.Escape("z;semikolon"));
            Assert.AreEqual("\"cudzyslow\"\"\"", PingEventLog.Escape("cudzyslow\""));
        }

        [TestMethod]
        public void FormatLineHasAllColumns()
        {
            string line = PingEventLog.FormatLine(new DateTime(2026, 10, 21, 14, 32, 5),
                "PC-01 (10.0.0.5)", "10.0.0.5", "DOWN", TimeSpan.FromMinutes(12));
            string[] parts = line.Split(';');
            Assert.AreEqual(6, parts.Length);
            Assert.AreEqual("2026-10-21", parts[0]);
            Assert.AreEqual("14:32:05", parts[1]);
            Assert.AreEqual("PC-01 (10.0.0.5)", parts[2]);
            Assert.AreEqual("10.0.0.5", parts[3]);
            Assert.AreEqual("DOWN", parts[4]);
            Assert.AreEqual("12 min 0 s", parts[5]);
        }

        [TestMethod]
        public void AppendWritesHeaderAndRows()
        {
            string path = Path.Combine(_tmp, "ev.csv");
            PingEventLog.AppendTo(path, new DateTime(2026, 10, 21, 10, 0, 0), "PC-01", "10.0.0.5", false, TimeSpan.FromMinutes(5));
            PingEventLog.AppendTo(path, new DateTime(2026, 10, 21, 10, 7, 0), "PC-01", "10.0.0.5", true, TimeSpan.FromMinutes(7));

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(3, lines.Length);
            StringAssert.StartsWith(lines[0], "Data;Godzina;Urzadzenie");
            StringAssert.Contains(lines[1], "DOWN");
            StringAssert.Contains(lines[2], "UP");
        }

        [TestMethod]
        public void ReadLastReturnsTail()
        {
            string path = Path.Combine(_tmp, "ev.csv");
            for (int i = 0; i < 20; i++)
                PingEventLog.AppendTo(path, new DateTime(2026, 10, 21, 10, i, 0), "PC" + i, "10.0.0.5", true, null);

            List<string> last = PingEventLog.ReadLast(path, 5);
            Assert.AreEqual(5, last.Count);
            StringAssert.Contains(last[4], "PC19");
        }

        [TestMethod]
        public void TrimKeepsHeaderAndTail()
        {
            string path = Path.Combine(_tmp, "ev.csv");
            for (int i = 0; i < 30; i++)
                PingEventLog.AppendTo(path, new DateTime(2026, 10, 21, 10, i, 0), "PC" + i, "10.0.0.5", true, null);

            Assert.IsTrue(PingEventLog.TrimToMaxLines(path, 10));
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(11, lines.Length);
            StringAssert.StartsWith(lines[0], "Data;");
            StringAssert.Contains(lines[10], "PC29");
        }

        [TestMethod]
        public void AppendToBadPathDoesNotThrow()
        {
            PingEventLog.AppendTo("Z:\\nie\\istnieje\\ev.csv", DateTime.Now, "PC", "10.0.0.5", true, null);
            PingEventLog.AppendTo(null, DateTime.Now, "PC", "10.0.0.5", true, null);
            PingEventLog.AppendTo("", DateTime.Now, "PC", "10.0.0.5", true, null);
        }

        [TestMethod]
        public void LogTransitionWritesOnlyOnChange()
        {
            string path = Path.Combine(_tmp, "ev.csv");
            DateTime now = new DateTime(2026, 10, 21, 14, 32, 5);
            DateTime changed = now.AddMinutes(-12);

            // pierwszy pomiar - to nie zmiana
            Assert.IsFalse(PingEventLog.LogTransition(path, now, "PC-01", "10.0.0.5", null, null, true));
            Assert.IsFalse(File.Exists(path));

            // ten sam stan - to nie zmiana
            Assert.IsFalse(PingEventLog.LogTransition(path, now, "PC-01", "10.0.0.5", true, changed, true));
            Assert.IsFalse(File.Exists(path));

            // host przestal odpowiadać - zmiana stanu
            Assert.IsTrue(PingEventLog.LogTransition(path, now, "PC-01", "10.0.0.5", true, changed, false));
            Assert.IsTrue(File.Exists(path));

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(2, lines.Length);
            StringAssert.Contains(lines[1], "PC-01");
            StringAssert.Contains(lines[1], "DOWN");
            StringAssert.Contains(lines[1], "12 min 0 s");
        }

        [TestMethod]
        public void LogTransitionRecordsRecovery()
        {
            string path = Path.Combine(_tmp, "ev.csv");
            DateTime now = new DateTime(2026, 10, 21, 14, 35, 0);

            PingEventLog.LogTransition(path, now, "PC-01", "10.0.0.5", true, now.AddMinutes(-3), false);
            PingEventLog.LogTransition(path, now.AddMinutes(3), "PC-01", "10.0.0.5", false, now, true);

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(3, lines.Length);
            StringAssert.Contains(lines[1], "DOWN");
            StringAssert.Contains(lines[2], "UP");
            StringAssert.Contains(lines[2], "3 min 0 s");
        }
    }

    [TestClass]
    public class PingRowStateTests
    {
        [TestMethod]
        public void StateChangeIsTracked()
        {
            PingRow row = new PingRow("PC-01", "10.0.0.5");
            Assert.IsNull(row.LastOk);
            Assert.IsNull(row.LastStateChangeAt);

            row.Record(true, 10, 128);
            Assert.AreEqual(true, row.LastOk);
            Assert.IsNotNull(row.LastStateChangeAt);
            DateTime first = row.LastStateChangeAt.Value;

            // ten sam stan - czas zmiany sie nie zmienia
            System.Threading.Thread.Sleep(20);
            row.Record(true, 11, 128);
            Assert.AreEqual(first, row.LastStateChangeAt.Value);

            // zmiana stanu - nowy czas
            System.Threading.Thread.Sleep(20);
            row.Record(false, 0, -1);
            Assert.AreEqual(false, row.LastOk);
            Assert.IsTrue(row.LastStateChangeAt.Value > first);
        }
    }
}
