using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class AclRemarkTests
    {
        [DataTestMethod]
        [DataRow("Jan Kowalski", "Jan Kowalski")]
        [DataRow("  Jan   Kowalski  ", "Jan Kowalski")]
        [DataRow("Roman Pakholok", "Roman Pakholok")]
        [DataRow("", "")]
        [DataRow("   ", "")]
        [DataRow(null, "")]
        public void CleanOwner(string input, string expected)
        {
            Assert.AreEqual(expected, AclRemark.CleanOwner(input));
        }

        [TestMethod]
        public void CleanOwnerTruncatesTo100()
        {
            string long_ = new string('x', 150);
            string clean = AclRemark.CleanOwner(long_);
            Assert.AreEqual(100, clean.Length);
            Assert.AreEqual(new string('x', 100), clean);
        }

        [TestMethod]
        public void CleanOwnerCollapsesWhitespace()
        {
            Assert.AreEqual("Anna Nowak", AclRemark.CleanOwner("Anna\t\n Nowak"));
        }

        [DataTestMethod]
        [DataRow("Jan Kowalski", "Jan Kowalski (koniec)")]
        [DataRow("", "")]
        [DataRow(null, "")]
        [DataRow("   ", "")]
        public void EndMark(string input, string expected)
        {
            Assert.AreEqual(expected, AclRemark.EndMark(input));
        }
    }
}
