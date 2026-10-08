using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class TacticalUrlTests
    {
        [TestMethod]
        public void NormalizeTrimsSlash()
        {
            Assert.AreEqual("https://api.example.com", TacticalRmm.NormalizeBaseUrl("https://api.example.com/"));
            Assert.AreEqual("https://api.example.com", TacticalRmm.NormalizeBaseUrl("  https://api.example.com// "));
            Assert.IsNull(TacticalRmm.NormalizeBaseUrl(null));
            Assert.IsNull(TacticalRmm.NormalizeBaseUrl("   "));
        }

        [TestMethod]
        public void AgentsUrl()
        {
            Assert.AreEqual("https://api.example.com/agents/",
                TacticalRmm.AgentsUrl("https://api.example.com/"));
            Assert.IsNull(TacticalRmm.AgentsUrl(""));
        }

        [TestMethod]
        public void TakeControlUrl()
        {
            Assert.AreEqual("https://rmm.example.com/takecontrol/abc-123",
                TacticalRmm.TakeControlUrl("https://rmm.example.com/", "abc-123"));
            Assert.IsNull(TacticalRmm.TakeControlUrl("", "abc-123"));
            Assert.IsNull(TacticalRmm.TakeControlUrl("https://rmm.example.com", ""));
        }

        [DataTestMethod]
        [DataRow("PC123.DOMENA.LOCAL", "pc123")]
        [DataRow("PC123", "pc123")]
        [DataRow(null, "")]
        [DataRow("   ", "")]
        public void ShortName(string input, string expected)
        {
            Assert.AreEqual(expected, TacticalRmm.ShortName(input));
        }
    }

    [TestClass]
    public class TacticalParseTests
    {
        [TestMethod]
        public void ParsesAgentsSkippingNoId()
        {
            string json = "[{\"agent_id\":\"id-1\",\"hostname\":\"PC1\",\"extra\":1},"
                + "{\"hostname\":\"bez-id\"},"
                + "{\"agent_id\":\"id-2\",\"hostname\":\"PC2\"}]";
            List<TacticalAgent> r = TacticalRmm.ParseAgentsJson(json);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual("id-1", r[0].AgentId);
            Assert.AreEqual("PC1", r[0].Hostname);
        }

        [TestMethod]
        public void EmptyArrayGivesEmpty()
        {
            Assert.AreEqual(0, TacticalRmm.ParseAgentsJson("[]").Count);
        }

        [TestMethod]
        public void StatusAndLastSeenParsed()
        {
            var r = TacticalRmm.ParseAgentsJson(
                "[{\"agent_id\":\"a\",\"hostname\":\"PC1\",\"status\":\"offline\",\"last_seen\":\"2026-10-08 10:00\"}]");
            Assert.AreEqual(1, r.Count);
            Assert.AreEqual("offline", r[0].Status);
            Assert.AreEqual("2026-10-08 10:00", r[0].LastSeen);
            Assert.IsTrue(r[0].IsOffline);
        }

        [TestMethod]
        public void MissingStatusGivesNull()
        {
            var r = TacticalRmm.ParseAgentsJson("[{\"agent_id\":\"a\",\"hostname\":\"PC1\"}]");
            Assert.IsNull(r[0].Status);
            Assert.IsNull(r[0].LastSeen);
            Assert.IsFalse(r[0].IsOffline);
        }

        [TestMethod]
        public void IsOfflineCaseInsensitive()
        {
            var r = TacticalRmm.ParseAgentsJson("[{\"agent_id\":\"a\",\"hostname\":\"PC1\",\"status\":\"Offline\"}]");
            Assert.IsTrue(r[0].IsOffline);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void InvalidJsonThrows()
        {
            TacticalRmm.ParseAgentsJson("to nie json");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void NonArrayThrows()
        {
            TacticalRmm.ParseAgentsJson("{\"agent_id\":\"x\"}");
        }

        [TestMethod]
        public void ExactMatchBeatsShortName()
        {
            List<TacticalAgent> agents = new List<TacticalAgent>
            {
                new TacticalAgent { AgentId = "short", Hostname = "PC1" },
                new TacticalAgent { AgentId = "exact", Hostname = "PC1.domena.local" }
            };
            Assert.AreEqual("exact", TacticalRmm.FindByHostname(agents, "pc1.DOMENA.local").AgentId);
        }

        [TestMethod]
        public void ShortNameFallback()
        {
            List<TacticalAgent> agents = new List<TacticalAgent>
            {
                new TacticalAgent { AgentId = "id-1", Hostname = "PC77" }
            };
            Assert.AreEqual("id-1", TacticalRmm.FindByHostname(agents, "pc77.domena.local").AgentId);
        }

        [TestMethod]
        public void NoMatchGivesNull()
        {
            List<TacticalAgent> agents = new List<TacticalAgent>
            {
                new TacticalAgent { AgentId = "id-1", Hostname = "PC1" }
            };
            Assert.IsNull(TacticalRmm.FindByHostname(agents, "PC2"));
            Assert.IsNull(TacticalRmm.FindByHostname(agents, null));
            Assert.IsNull(TacticalRmm.FindByHostname(null, "PC1"));
        }
    }

    [TestClass]
    public class TacticalHttpTests
    {
        private static int StartStubServer(string body, int statusCode)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task.Run(() =>
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        string line;
                        do { line = reader.ReadLine(); } while (!string.IsNullOrEmpty(line));
                        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                        string reason = statusCode == 200 ? "OK" : "Unauthorized";
                        string header = "HTTP/1.1 " + statusCode + " " + reason + "\r\n"
                            + "Content-Type: application/json\r\n"
                            + "Content-Length: " + bodyBytes.Length + "\r\n"
                            + "Connection: close\r\n\r\n";
                        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                        stream.Write(headerBytes, 0, headerBytes.Length);
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                }
                catch { }
                finally
                {
                    try { listener.Stop(); }
                    catch { }
                }
            });
            return port;
        }

        [TestMethod]
        public async Task FindAgentThroughHttp()
        {
            int port = StartStubServer(
                "[{\"agent_id\":\"abc-1\",\"hostname\":\"Komputer01\"}]", 200);
            TacticalAgent a = await TacticalRmm.FindAgentAsync(
                "http://127.0.0.1:" + port, "KEY", "komputer01", 10);
            Assert.IsNotNull(a);
            Assert.AreEqual("abc-1", a.AgentId);
        }

        [TestMethod]
        public async Task MissingAgentGivesNull()
        {
            int port = StartStubServer("[{\"agent_id\":\"abc-1\",\"hostname\":\"Inny\"}]", 200);
            TacticalAgent a = await TacticalRmm.FindAgentAsync(
                "http://127.0.0.1:" + port, "KEY", "NieMa", 10);
            Assert.IsNull(a);
        }

        [TestMethod]
        public async Task UnauthorizedThrowsFriendly()
        {
            int port = StartStubServer("{}", 401);
            try
            {
                await TacticalRmm.FindAgentAsync("http://127.0.0.1:" + port, "ZLY", "PC1", 10);
                Assert.Fail("miał rzucić");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "klucz API");
            }
        }

        [TestMethod]
        public async Task MissingConfigThrowsFriendly()
        {
            try
            {
                await TacticalRmm.FindAgentAsync("", "KEY", "PC1", 10);
                Assert.Fail("miał rzucić");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Ustawienia");
            }
            try
            {
                await TacticalRmm.FindAgentAsync("http://x", "", "PC1", 10);
                Assert.Fail("miał rzucić");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Ustawienia");
            }
        }
    }

    [TestClass]
    public class TacticalCacheTests
    {
        [TestInitialize]
        public void Setup()
        {
            TacticalRmm.ClearAgentsCache();
        }

        [TestCleanup]
        public void Teardown()
        {
            TacticalRmm.ClearAgentsCache();
        }

        [TestMethod]
        public void MissThenHit()
        {
            List<TacticalAgent> agents;
            Assert.IsFalse(TacticalRmm.TryGetCachedAgents("k", TimeSpan.FromMinutes(5), out agents));
            TacticalRmm.StoreAgents("k", new List<TacticalAgent>
            {
                new TacticalAgent { AgentId = "id-1", Hostname = "PC1" }
            });
            Assert.IsTrue(TacticalRmm.TryGetCachedAgents("k", TimeSpan.FromMinutes(5), out agents));
            Assert.AreEqual(1, agents.Count);
            Assert.AreEqual("id-1", agents[0].AgentId);
        }

        [TestMethod]
        public void StaleMisses()
        {
            TacticalRmm.StoreAgents("k", new List<TacticalAgent>(), DateTime.UtcNow.AddMinutes(-6));
            List<TacticalAgent> agents;
            Assert.IsFalse(TacticalRmm.TryGetCachedAgents("k", TimeSpan.FromMinutes(5), out agents));
        }

        [TestMethod]
        public void KeyCaseInsensitiveAndBlankSafe()
        {
            TacticalRmm.StoreAgents("Key", new List<TacticalAgent>());
            List<TacticalAgent> agents;
            Assert.IsTrue(TacticalRmm.TryGetCachedAgents("key", TimeSpan.FromMinutes(5), out agents));
            Assert.IsFalse(TacticalRmm.TryGetCachedAgents(" ", TimeSpan.FromMinutes(5), out agents));
            Assert.IsFalse(TacticalRmm.TryGetCachedAgents(null, TimeSpan.FromMinutes(5), out agents));
        }

        [TestMethod]
        public async Task SecondFetchUsesCacheWithoutHttp()
        {
            int hits = 0;
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() =>
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                    {
                        System.Threading.Interlocked.Increment(ref hits);
                        string line;
                        do { line = reader.ReadLine(); } while (!string.IsNullOrEmpty(line));
                        string body = "[{\"agent_id\":\"a\",\"hostname\":\"H\"}]";
                        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                        string header = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                            + "Content-Length: " + bodyBytes.Length + "\r\nConnection: close\r\n\r\n";
                        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                        stream.Write(headerBytes, 0, headerBytes.Length);
                        stream.Write(bodyBytes, 0, bodyBytes.Length);
                    }
                }
                catch { }
                finally
                {
                    try { listener.Stop(); }
                    catch { }
                }
            });
            try
            {
                string api = "http://127.0.0.1:" + port;
                var first = await TacticalRmm.GetAgentsCachedAsync(api, "K", 10);
                var second = await TacticalRmm.GetAgentsCachedAsync(api, "K", 10);
                Assert.AreEqual(1, first.Count);
                Assert.AreEqual(1, second.Count);
                await server;
                Assert.AreEqual(1, hits, "drugie wywołanie miało nie uderzać w HTTP");
            }
            finally
            {
                try { listener.Stop(); }
                catch { }
            }
        }
    }
}
