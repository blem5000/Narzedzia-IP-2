using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class ConnectMethodTests
    {
        [TestMethod]
        public void NormalizeKeepsKnownValues()
        {
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize(ConnectMethod.Msra));
            Assert.AreEqual(ConnectMethod.Rdp, ConnectMethod.Normalize(ConnectMethod.Rdp));
            Assert.AreEqual(ConnectMethod.Tactical, ConnectMethod.Normalize(ConnectMethod.Tactical));
            Assert.AreEqual(ConnectMethod.Rdp, ConnectMethod.Normalize("RDP"));
            Assert.AreEqual(ConnectMethod.Tactical, ConnectMethod.Normalize("TacticalRMM"));
            Assert.AreEqual(ConnectMethod.Tactical, ConnectMethod.Normalize("  tactical  "));
        }

        [TestMethod]
        public void NormalizeFallsBackToMsra()
        {
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize(null));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize(""));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("   "));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("cos innego"));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("rdpx"));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("tacticalx"));
        }

        [TestMethod]
        public void IsRdpAndIsTactical()
        {
            Assert.IsTrue(ConnectMethod.IsRdp(ConnectMethod.Rdp));
            Assert.IsFalse(ConnectMethod.IsRdp(ConnectMethod.Msra));
            Assert.IsFalse(ConnectMethod.IsRdp(null));
            Assert.IsTrue(ConnectMethod.IsTactical(ConnectMethod.Tactical));
            Assert.IsFalse(ConnectMethod.IsTactical(ConnectMethod.Msra));
            Assert.IsFalse(ConnectMethod.IsTactical(null));
        }

        [TestMethod]
        public void Label()
        {
            Assert.AreEqual("RDP", ConnectMethod.Label(ConnectMethod.Rdp));
            Assert.AreEqual("MSRA", ConnectMethod.Label(ConnectMethod.Msra));
            Assert.AreEqual("TacticalRMM", ConnectMethod.Label(ConnectMethod.Tactical));
            Assert.AreEqual("MSRA", ConnectMethod.Label(null));
        }
    }
}
