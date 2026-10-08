using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class DhcpClientParseTests
    {
        [TestMethod]
        public void ParseScopeIdsFiltersGarbage()
        {
            string output = "10.202.130.0\r\n\r\n   \r\nnie-scope\r\n10.207.156.0\n10.202.130.0\n::1\n";
            CollectionAssert.AreEqual(
                new List<string> { "10.202.130.0", "10.207.156.0" },
                DhcpClient.ParseScopeIds(output));
        }

        [TestMethod]
        public void ParseScopeIdsEmptyGivesEmpty()
        {
            Assert.AreEqual(0, DhcpClient.ParseScopeIds(null).Count);
            Assert.AreEqual(0, DhcpClient.ParseScopeIds("  \r\n ").Count);
        }

        [TestMethod]
        public void ParseLeaseLinesSkipsMalformed()
        {
            string output = "PC1\t10.0.0.1\tAA-BB-CC\r\n"
                + "broken-line\r\n"
                + "PC2\t10.0.0.2\r\n"
                + "\r\n"
                + "  PC3  \t  10.0.0.3  \t  DD-EE-FF  \r\n";
            List<DhcpLease> r = DhcpClient.ParseLeaseLines(output);
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual("PC1", r[0].HostName);
            Assert.AreEqual("10.0.0.1", r[0].IPAddress);
            Assert.AreEqual("AA-BB-CC", r[0].MacAddress);
            Assert.AreEqual("PC3", r[1].HostName);
        }

        [TestMethod]
        public void EscapeDoublesQuotes()
        {
            Assert.AreEqual("a''b", DhcpClient.EscapePowerShellSingleQuotedString("a'b"));
            Assert.AreEqual("", DhcpClient.EscapePowerShellSingleQuotedString(null));
        }

        [TestMethod]
        public void EncodeRoundtripsUnicode()
        {
            const string cmd = "zażółć $x 'cytat'";
            string back = Encoding.Unicode.GetString(Convert.FromBase64String(DhcpClient.EncodePowerShellCommand(cmd)));
            Assert.AreEqual(cmd, back);
        }

        [TestMethod]
        public void BuildScopesCommandHasCmdletAndServer()
        {
            string cmd = DhcpClient.BuildScopesCommand("1.2.3.4");
            StringAssert.Contains(cmd, "Get-DhcpServerv4Scope");
            StringAssert.Contains(cmd, "'1.2.3.4'");
        }

        [TestMethod]
        public void BuildScopeLeasesCommandEscapes()
        {
            string cmd = DhcpClient.BuildScopeLeasesCommand("srv", "10.0.0.0", "pc'o");
            StringAssert.Contains(cmd, "Get-DhcpServerv4Lease");
            StringAssert.Contains(cmd, "-ScopeId $scopeId");
            StringAssert.Contains(cmd, "'pc''o'");
            StringAssert.Contains(cmd, "-like ($name + '*')");
        }

        [TestMethod]
        public void BuildChunkCommandHasAllScopesOnce()
        {
            string cmd = DhcpClient.BuildScopeLeasesCommand("srv",
                new System.Collections.Generic.List<string> { "10.0.0.0", "10.0.1.0" }, "pc");
            StringAssert.Contains(cmd, "'10.0.0.0'");
            StringAssert.Contains(cmd, "'10.0.1.0'");
            StringAssert.Contains(cmd, "foreach ($scopeId in $scopeIds)");
            int first = cmd.IndexOf("Get-DhcpServerv4Lease");
            Assert.IsTrue(first >= 0);
            Assert.AreEqual(-1, cmd.IndexOf("Get-DhcpServerv4Lease", first + 1),
                "jedna komenda na paczkę, nie na zakres");
        }

        [TestMethod]
        public void PartitionSplitsEvenly()
        {
            var parts = DhcpClient.Partition(
                new System.Collections.Generic.List<int> { 1, 2, 3, 4 }, 2);
            Assert.AreEqual(2, parts.Count);
            CollectionAssert.AreEqual(new System.Collections.Generic.List<int> { 1, 2 }, parts[0]);
            CollectionAssert.AreEqual(new System.Collections.Generic.List<int> { 3, 4 }, parts[1]);
        }

        [TestMethod]
        public void PartitionCapsPartsAndKeepsOrder()
        {
            var items = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 10; i++)
                items.Add(i);
            var parts = DhcpClient.Partition(items, 4);
            Assert.AreEqual(4, parts.Count);
            var flat = new System.Collections.Generic.List<int>();
            foreach (var p in parts)
                flat.AddRange(p);
            CollectionAssert.AreEqual(items, flat);
            Assert.IsTrue(parts.TrueForAll(p => p.Count <= 3));
        }

        [TestMethod]
        public void PartitionEdgeCases()
        {
            Assert.AreEqual(0, DhcpClient.Partition(new System.Collections.Generic.List<int>(), 4).Count);
            Assert.AreEqual(0, DhcpClient.Partition<int>(null, 4).Count);
            Assert.AreEqual(0, DhcpClient.Partition(new System.Collections.Generic.List<int> { 1 }, 0).Count);
            var one = DhcpClient.Partition(new System.Collections.Generic.List<int> { 1, 2 }, 10);
            Assert.AreEqual(2, one.Count);
        }
    }

    [TestClass]
    public class DhcpScopeCacheTests
    {
        [TestInitialize]
        public void Setup()
        {
            DhcpClient.ClearScopeCache();
        }

        [TestCleanup]
        public void Teardown()
        {
            DhcpClient.ClearScopeCache();
        }

        [TestMethod]
        public void MissOnUnknownServer()
        {
            List<string> scopes;
            Assert.IsFalse(DhcpClient.TryGetCachedScopes("9.9.9.9", TimeSpan.FromMinutes(10), out scopes));
            Assert.IsNull(scopes);
        }

        [TestMethod]
        public void StoreThenHit()
        {
            DhcpClient.StoreScopes("1.1.1.1", new List<string> { "10.0.0.0" });
            List<string> scopes;
            Assert.IsTrue(DhcpClient.TryGetCachedScopes("1.1.1.1", TimeSpan.FromMinutes(10), out scopes));
            CollectionAssert.AreEqual(new List<string> { "10.0.0.0" }, scopes);
        }

        [TestMethod]
        public void StaleEntryMisses()
        {
            DhcpClient.StoreScopes("2.2.2.2", new List<string> { "10.0.0.0" }, DateTime.UtcNow.AddMinutes(-11));
            List<string> scopes;
            Assert.IsFalse(DhcpClient.TryGetCachedScopes("2.2.2.2", TimeSpan.FromMinutes(10), out scopes));
        }

        [TestMethod]
        public void ServerKeyIsCaseInsensitive()
        {
            DhcpClient.StoreScopes("SRV", new List<string> { "10.0.0.0" });
            List<string> scopes;
            Assert.IsTrue(DhcpClient.TryGetCachedScopes("srv", TimeSpan.FromMinutes(10), out scopes));
        }
    }

    [TestClass]
    public class PowerShellRunnerTests
    {
        [TestMethod]
        public async Task SimpleCommandReturnsOutput()
        {
            string output = await DhcpClient.RunPowerShellAsync("Write-Output 'ok-123'", 15000);
            StringAssert.Contains(output, "ok-123");
        }

        [TestMethod]
        public async Task NonZeroExitThrows()
        {
            await Assert.ThrowsExceptionAsync<Exception>(() =>
                DhcpClient.RunPowerShellAsync("exit 5", 15000));
        }

        [TestMethod]
        public async Task TimeoutKillsAndThrows()
        {
            await Assert.ThrowsExceptionAsync<TimeoutException>(() =>
                DhcpClient.RunPowerShellAsync("Start-Sleep -Seconds 30", 2000));
        }

        [TestMethod]
        public async Task CancellationThrowsOperationCanceled()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
                    DhcpClient.RunPowerShellAsync("Write-Output 'x'", 15000, cts.Token));
            }
        }
    }
}
