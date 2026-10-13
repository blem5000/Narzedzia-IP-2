using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class PingRowTests
    {
        [TestMethod]
        public void FreshRowShowsDashes()
        {
            PingRow r = new PingRow("h (1.1.1.1)", "1.1.1.1");
            Assert.AreEqual("h (1.1.1.1)", r.Display);
            Assert.AreEqual("1.1.1.1", r.Ip);
            Assert.AreEqual(0, r.Sent);
            Assert.IsNull(r.LastOk);
            Assert.AreEqual("—", r.StatusText);
            Assert.AreEqual("—", r.SuccessPctText);
            Assert.AreEqual("—", r.LastTtlText);
            Assert.AreEqual("—", r.AvgTtlText);
            Assert.AreEqual("—", r.LastSuccessText);
            Assert.AreEqual("—", r.LastFailText);
            Assert.AreEqual("—", r.MinText);
        }

        [TestMethod]
        public void SuccessUpdatesAllFields()
        {
            PingRow r = new PingRow("h", "1.1.1.1");
            r.Record(true, 10, 64);
            r.Record(true, 20, 128);
            Assert.AreEqual(2, r.Sent);
            Assert.AreEqual(2, r.Succeeded);
            Assert.AreEqual(true, r.LastOk);
            Assert.AreEqual("Tak", r.StatusText);
            Assert.AreEqual("100%", r.SuccessPctText);
            Assert.AreEqual(128, r.LastTtl);
            Assert.AreEqual("128", r.LastTtlText);
            Assert.AreEqual(96.0, r.AvgTtl);
            Assert.AreEqual("96", r.AvgTtlText);
            Assert.IsNotNull(r.LastSuccessAt);
            Assert.IsNull(r.LastFailAt);
            Assert.AreEqual("10 ms", r.MinText);
            Assert.AreEqual("20 ms", r.MaxText);
        }

        [TestMethod]
        public void FailureMarksRedAndKeepsTtl()
        {
            PingRow r = new PingRow("h", "1.1.1.1");
            r.Record(true, 10, 64);
            r.Record(false, 0, -1);
            Assert.AreEqual(false, r.LastOk);
            Assert.AreEqual("Nie", r.StatusText);
            Assert.AreEqual("50%", r.SuccessPctText);
            Assert.AreEqual(64, r.LastTtl);
            Assert.IsNotNull(r.LastFailAt);
            Assert.IsNotNull(r.LastSuccessAt);
        }

        [TestMethod]
        public void SummaryDelegatesToStats()
        {
            PingRow r = new PingRow("h", "9.9.9.9");
            r.Record(true, 5, 32);
            StringAssert.Contains(r.Summary(), "9.9.9.9");
        }
    }

    [TestClass]
    public class PingTargetsTests
    {
        [TestMethod]
        public void SplitsAndDedupes()
        {
            var r = PingTargets.ParseInput("a, b\nc\r\nd; A ; ;");
            CollectionAssert.AreEqual(new List<string> { "a", "b", "c", "d" }, r);
        }

        [TestMethod]
        public void EmptyGivesEmpty()
        {
            Assert.AreEqual(0, PingTargets.ParseInput(null).Count);
            Assert.AreEqual(0, PingTargets.ParseInput("  \n,; ").Count);
        }

        [TestMethod]
        public void KeepsIpsVerbatim()
        {
            var r = PingTargets.ParseInput("10.0.0.1, 10.0.0.2");
            Assert.AreEqual(2, r.Count);
        }
    }
}
