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
            Assert.AreEqual(ConnectMethod.Rdp, ConnectMethod.Normalize("RDP"));
            Assert.AreEqual(ConnectMethod.Rdp, ConnectMethod.Normalize("  rdp  "));
        }

        [TestMethod]
        public void NormalizeFallsBackToMsra()
        {
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize(null));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize(""));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("   "));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("cos innego"));
            Assert.AreEqual(ConnectMethod.Msra, ConnectMethod.Normalize("rdpx"));
        }

        [TestMethod]
        public void IsRdpAndLabel()
        {
            Assert.IsTrue(ConnectMethod.IsRdp(ConnectMethod.Rdp));
            Assert.IsFalse(ConnectMethod.IsRdp(ConnectMethod.Msra));
            Assert.IsFalse(ConnectMethod.IsRdp(null));
            Assert.AreEqual("RDP", ConnectMethod.Label(ConnectMethod.Rdp));
            Assert.AreEqual("MSRA", ConnectMethod.Label(ConnectMethod.Msra));
            Assert.AreEqual("MSRA", ConnectMethod.Label(null));
        }
    }
}
