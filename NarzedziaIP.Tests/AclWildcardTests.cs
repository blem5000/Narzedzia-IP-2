using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class AclWildcardTests
    {
        private static string Show(List<(string Network, string Wildcard)> blocks)
        {
            return string.Join(";", blocks.Select(b => b.Network + "|" + b.Wildcard));
        }

        private static List<string> Octets(string a, string b, string c, int from, int to)
        {
            List<string> r = new List<string>();
            for (int i = from; i <= to; i++)
                r.Add(a + "." + b + "." + c + "." + i);
            return r;
        }

        [TestMethod]
        public void SingleHostGivesSlash32()
        {
            Assert.AreEqual("10.0.0.5|0.0.0.0",
                Show(AclWildcard.MergeToWildcard(new List<string> { "10.0.0.5" })));
        }

        [TestMethod]
        public void AlignedPairGivesSlash31()
        {
            Assert.AreEqual("10.0.0.0|0.0.0.1",
                Show(AclWildcard.MergeToWildcard(new List<string> { "10.0.0.0", "10.0.0.1" })));
        }

        [TestMethod]
        public void UnalignedPairStaysTwoSlash32()
        {
            Assert.AreEqual("10.0.0.1|0.0.0.0;10.0.0.2|0.0.0.0",
                Show(AclWildcard.MergeToWildcard(new List<string> { "10.0.0.1", "10.0.0.2" })));
        }

        [TestMethod]
        public void AlignedQuadGivesSlash30()
        {
            Assert.AreEqual("10.0.0.0|0.0.0.3",
                Show(AclWildcard.MergeToWildcard(Octets("10", "0", "0", 0, 3))));
        }

        [TestMethod]
        public void TripleSplitsIntoSlash31PlusSlash32()
        {
            Assert.AreEqual("10.0.0.0|0.0.0.1;10.0.0.2|0.0.0.0",
                Show(AclWildcard.MergeToWildcard(Octets("10", "0", "0", 0, 2))));
        }

        [TestMethod]
        public void FullSlash24IsOneBlock()
        {
            // Regresja: stary switch dawał tu błędne 0.0.0.0.
            Assert.AreEqual("10.1.2.0|0.0.0.255",
                Show(AclWildcard.MergeToWildcard(Octets("10", "1", "2", 0, 255))));
        }

        [TestMethod]
        public void Slash23AcrossOctetBoundaryIsOneBlock()
        {
            List<string> ips = Octets("10", "0", "0", 0, 255);
            ips.AddRange(Octets("10", "0", "1", 0, 255));
            Assert.AreEqual("10.0.0.0|0.0.1.255",
                Show(AclWildcard.MergeToWildcard(ips)));
        }

        [TestMethod]
        public void UnsortedInputWithDuplicatesMerges()
        {
            Assert.AreEqual("10.0.0.0|0.0.0.1",
                Show(AclWildcard.MergeToWildcard(new List<string> { "10.0.0.1", "10.0.0.0", "10.0.0.1" })));
        }

        [TestMethod]
        public void GapSplitsIntoTwoBlocks()
        {
            List<string> ips = Octets("10", "0", "0", 0, 3);
            ips.AddRange(Octets("10", "0", "0", 8, 11));
            Assert.AreEqual("10.0.0.0|0.0.0.3;10.0.0.8|0.0.0.3",
                Show(AclWildcard.MergeToWildcard(ips)));
        }

        [DataTestMethod]
        [DataRow(1u, "0.0.0.0")]      // /32
        [DataRow(2u, "0.0.0.1")]      // /31
        [DataRow(4u, "0.0.0.3")]      // /30
        [DataRow(128u, "0.0.0.127")]  // /25 - granica starego switcha
        [DataRow(256u, "0.0.0.255")]  // /24
        [DataRow(512u, "0.0.1.255")]  // /23
        [DataRow(65536u, "0.0.255.255")] // /16
        public void WildcardForSize(uint size, string expected)
        {
            Assert.AreEqual(expected, AclWildcard.WildcardForSize(size));
        }

        [TestMethod]
        public void RoundtripUintConversion()
        {
            Assert.AreEqual("10.202.130.7", AclWildcard.UintToIP(AclWildcard.IPToUint("10.202.130.7")));
            Assert.AreEqual(0x0ACB9C01u, AclWildcard.IPToUint("10.203.156.1"));
        }

        [DataTestMethod]
        [DataRow("255.255.255.0", true)]
        [DataRow("255.255.254.0", true)]
        [DataRow("255.255.0.0", true)]
        [DataRow("255.0.0.0", true)]
        [DataRow("0.0.0.0", true)]
        [DataRow("255.255.255.255", true)]
        [DataRow("255.0.255.0", false)]
        [DataRow("1.2.3.4", false)]
        [DataRow("abc", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        [DataRow("::1", false)]
        public void ContiguousMaskCheck(string mask, bool expected)
        {
            Assert.AreEqual(expected, AclWildcard.IsContiguousMask(mask));
        }
    }
}
