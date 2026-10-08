using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class PingHelperTests
    {
        [TestMethod]
        public async Task LoopbackResponds()
        {
            Assert.IsTrue(await PingHelper.PingHostAsync("127.0.0.1", 2000));
        }

        [TestMethod]
        public async Task InvalidAddressIsFalse()
        {
            Assert.IsFalse(await PingHelper.PingHostAsync("999.999.999.999", 2000));
        }

        [TestMethod]
        public async Task EmptyAddressIsFalse()
        {
            Assert.IsFalse(await PingHelper.PingHostAsync("", 500));
            Assert.IsFalse(await PingHelper.PingHostAsync(null, 500));
        }

        [TestMethod]
        public async Task UnroutableTestNetIsFalseFast()
        {
            // 192.0.2.0/24 (TEST-NET-1) nie jest routowalny - ma paść na timeout.
            Stopwatch sw = Stopwatch.StartNew();
            bool ok = await PingHelper.PingHostAsync("192.0.2.1", 500);
            sw.Stop();
            Assert.IsFalse(ok);
            Assert.IsTrue(sw.Elapsed < System.TimeSpan.FromSeconds(20),
                "ping przekroczył rozsądny czas: " + sw.Elapsed);
        }

        [TestMethod]
        public async Task DefaultTimeoutOverloadWorks()
        {
            Assert.IsTrue(await PingHelper.PingHostAsync("127.0.0.1"));
        }
    }
}
