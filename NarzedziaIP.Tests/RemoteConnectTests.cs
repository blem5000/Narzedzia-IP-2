using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class RemoteConnectTests
    {
        [DataTestMethod]
        [DataRow("10.0.0.1", "/v:10.0.0.1")]
        [DataRow("  150.150.222.20  ", "/v:150.150.222.20")]
        public void MstscArgs(string ip, string expected)
        {
            Assert.AreEqual(expected, RemoteConnect.BuildMstscArguments(ip));
        }

        [DataTestMethod]
        [DataRow("10.0.0.1", "/offerra 10.0.0.1")]
        public void MsraArgs(string ip, string expected)
        {
            Assert.AreEqual(expected, RemoteConnect.BuildMsraArguments(ip));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("nie-ip")]
        [DataRow("::1")]
        [DataRow("10.0.0.1, 10.0.0.2")]
        public void InvalidIpGivesNull(string ip)
        {
            Assert.IsNull(RemoteConnect.BuildMstscArguments(ip));
            Assert.IsNull(RemoteConnect.BuildMsraArguments(ip));
        }
    }
}
