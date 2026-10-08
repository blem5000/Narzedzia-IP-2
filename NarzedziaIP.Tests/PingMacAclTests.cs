using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class MacFormatTests
    {
        [DataTestMethod]
        [DataRow("AA-BB-CC-DD-EE-FF", "AABBCCDDEEFF")]
        [DataRow("aa:bb:cc:dd:ee:ff", "AABBCCDDEEFF")]
        [DataRow("aabb.ccdd.eeff", "AABBCCDDEEFF")]
        [DataRow("AABBCCDDEEFF", "AABBCCDDEEFF")]
        [DataRow("aa-bb-cc-dd-ee-ff", "AABBCCDDEEFF")]
        public void Normalize(string input, string expected)
        {
            Assert.AreEqual(expected, MacFormat.Normalize(input));
        }

        [DataTestMethod]
        [DataRow("xyz")]
        [DataRow("AA-BB-CC")]
        [DataRow("AA-BB-CC-DD-EE-FF-00")]
        [DataRow("")]
        [DataRow(null)]
        [DataRow("GG-HH-II-JJ-KK-LL")]
        public void NormalizeRejectsGarbage(string input)
        {
            Assert.IsNull(MacFormat.Normalize(input));
        }

        [TestMethod]
        public void Formats()
        {
            const string mac = "AA-BB-CC-DD-EE-FF";
            Assert.AreEqual("AABBCCDDEEFF", MacFormat.Format(mac, MacFormat.Plain));
            Assert.AreEqual("AABBCCDDEEFF", MacFormat.Format(mac, "unknown-style"));
            Assert.AreEqual("AA:BB:CC:DD:EE:FF", MacFormat.Format(mac, MacFormat.Colon));
            Assert.AreEqual("aabb.ccdd.eeff", MacFormat.Format(mac, MacFormat.Cisco));
        }

        [TestMethod]
        public void FormatGarbageGivesNull()
        {
            Assert.IsNull(MacFormat.Format("nie-mac", MacFormat.Cisco));
        }
    }

    [TestClass]
    public class AclInputTests
    {
        [TestMethod]
        public void MixedSeparatorsAndWhitespace()
        {
            var r = AclInput.SplitIps("10.0.0.1, 10.0.0.2\n10.0.0.3\r\n10.0.0.4;10.0.0.1");
            CollectionAssert.AreEqual(
                new System.Collections.Generic.List<string> { "10.0.0.1", "10.0.0.2", "10.0.0.3", "10.0.0.4" },
                r.Valid);
            Assert.AreEqual(0, r.Rejected.Count);
        }

        [TestMethod]
        public void RejectedCollectedDeduplicated()
        {
            var r = AclInput.SplitIps("10.0.0.1, zly, zly, 999.1.1.1, host.local");
            Assert.AreEqual(1, r.Valid.Count);
            CollectionAssert.AreEqual(
                new System.Collections.Generic.List<string> { "zly", "999.1.1.1", "host.local" },
                r.Rejected);
        }

        [TestMethod]
        public void Ipv6IsRejected()
        {
            var r = AclInput.SplitIps("::1, 10.0.0.1");
            Assert.AreEqual(1, r.Valid.Count);
            Assert.AreEqual(1, r.Rejected.Count);
            Assert.AreEqual("::1", r.Rejected[0]);
        }

        [TestMethod]
        public void EmptyGivesEmpty()
        {
            var r = AclInput.SplitIps("  \n,; ");
            Assert.AreEqual(0, r.Valid.Count);
            Assert.AreEqual(0, r.Rejected.Count);
            r = AclInput.SplitIps(null);
            Assert.AreEqual(0, r.Valid.Count);
            Assert.AreEqual(0, r.Rejected.Count);
        }
    }

    [TestClass]
    public class PingStatsTests
    {
        [TestMethod]
        public void EmptySummary()
        {
            Assert.AreEqual("1.1.1.1: brak pomiarów", new PingStats().Summary("1.1.1.1"));
        }

        [TestMethod]
        public void AllLostSummary()
        {
            PingStats s = new PingStats();
            s.Record(false, 0);
            s.Record(false, 0);
            Assert.AreEqual(2, s.Sent);
            Assert.AreEqual(0, s.Received);
            Assert.AreEqual(100.0, s.LossPct);
            StringAssert.Contains(s.Summary("h"), "100%");
        }

        [TestMethod]
        public void AggregatesRtt()
        {
            PingStats s = new PingStats();
            s.Record(true, 10);
            s.Record(false, 0);
            s.Record(true, 20);
            s.Record(true, 30);
            Assert.AreEqual(4, s.Sent);
            Assert.AreEqual(3, s.Received);
            Assert.AreEqual(1, s.Lost);
            Assert.AreEqual(25.0, s.LossPct);
            Assert.AreEqual(10, s.MinMs);
            Assert.AreEqual(30, s.MaxMs);
            Assert.AreEqual(20.0, s.AvgMs);
            StringAssert.Contains(s.Summary("h"), "min/śr/max 10/20/30 ms");
        }

        [DataTestMethod]
        [DataRow("5", 1, 5)]
        [DataRow(" 30 ", 1, 30)]
        [DataRow("0", 7, 7)]
        [DataRow("-3", 7, 7)]
        [DataRow("301", 7, 7)]
        [DataRow("abc", 7, 7)]
        [DataRow("", 7, 7)]
        [DataRow(null, 7, 7)]
        public void ParseInterval(string text, int fallback, int expected)
        {
            Assert.AreEqual(expected, PingHelper.ParseIntervalSeconds(text, fallback));
        }

        [TestMethod]
        public void ParseIntervalBadFallbackGivesOne()
        {
            Assert.AreEqual(1, PingHelper.ParseIntervalSeconds("x", 0));
        }
    }
}
