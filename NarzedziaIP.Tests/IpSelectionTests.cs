using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class IpSelectionTests
    {
        [TestMethod]
        public void SingleLeaseNeedsNoSelection()
        {
            Assert.AreEqual("10.0.0.1", IpSelection.ResolveActiveIp(
                new List<string> { "10.0.0.1" }, null, ""));
        }

        [TestMethod]
        public void SelectionWinsOverList()
        {
            Assert.AreEqual("10.0.0.2", IpSelection.ResolveActiveIp(
                new List<string> { "10.0.0.1", "10.0.0.2" }, "10.0.0.2", ""));
        }

        [TestMethod]
        public void MultiWithoutSelectionGivesNull()
        {
            Assert.IsNull(IpSelection.ResolveActiveIp(
                new List<string> { "10.0.0.1", "10.0.0.2" }, null, ""));
        }

        [TestMethod]
        public void MultiWithoutSelectionFallsBackToTypedIp()
        {
            Assert.AreEqual("192.168.1.7", IpSelection.ResolveActiveIp(
                new List<string> { "10.0.0.1", "10.0.0.2" }, null, "192.168.1.7"));
        }

        [TestMethod]
        public void StaleSelectionIsIgnored()
        {
            Assert.AreEqual("10.0.0.1", IpSelection.ResolveActiveIp(
                new List<string> { "10.0.0.1" }, "10.9.9.9", ""));
            Assert.IsNull(IpSelection.ResolveActiveIp(
                new List<string> { "10.0.0.1", "10.0.0.2" }, "10.9.9.9", "nie-ip"));
        }

        [TestMethod]
        public void GarbageEntriesAreSkipped()
        {
            Assert.AreEqual("10.0.0.1", IpSelection.ResolveActiveIp(
                new List<string> { "Brak hostname!", "10.0.0.1", null }, null, ""));
            Assert.IsNull(IpSelection.ResolveActiveIp(
                new List<string> { "a", "b" }, null, null));
            Assert.IsNull(IpSelection.ResolveActiveIp(null, null, null));
        }
    }
}
