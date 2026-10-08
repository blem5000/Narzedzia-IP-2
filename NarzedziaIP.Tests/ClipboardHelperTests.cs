using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class ClipboardHelperTests
    {
        [TestMethod]
        public void SuccessFirstTryCallsOnce()
        {
            int calls = 0;
            bool ok = ClipboardHelper.ExecuteWithRetries(() => { calls++; return true; }, 5, 1);
            Assert.IsTrue(ok);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void FlakyActionRetriesThenSucceeds()
        {
            int calls = 0;
            bool ok = ClipboardHelper.ExecuteWithRetries(() =>
            {
                calls++;
                if (calls < 3)
                    throw new InvalidOperationException("schowek zajęty");
                return true;
            }, 5, 1);
            Assert.IsTrue(ok);
            Assert.AreEqual(3, calls);
        }

        [TestMethod]
        public void AlwaysFailingGivesUpAfterAttempts()
        {
            int calls = 0;
            bool ok = ClipboardHelper.ExecuteWithRetries(() =>
            {
                calls++;
                throw new InvalidOperationException("zajęty");
            }, 4, 1);
            Assert.IsFalse(ok);
            Assert.AreEqual(4, calls);
        }

        [TestMethod]
        public void FalseResultRetriesToo()
        {
            int calls = 0;
            bool ok = ClipboardHelper.ExecuteWithRetries(() => { calls++; return false; }, 3, 1);
            Assert.IsFalse(ok);
            Assert.AreEqual(3, calls);
        }

        [TestMethod]
        public void BadAttemptsNormalizedToOne()
        {
            int calls = 0;
            bool ok = ClipboardHelper.ExecuteWithRetries(() => { calls++; return true; }, 0, 1);
            Assert.IsTrue(ok);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void NullActionIsFalse()
        {
            Assert.IsFalse(ClipboardHelper.ExecuteWithRetries(null, 3, 1));
        }

        [TestMethod]
        public void NullOrEmptyTextNeverTouchesClipboard()
        {
            Assert.IsFalse(ClipboardHelper.TrySetText(null));
            Assert.IsFalse(ClipboardHelper.TrySetText(""));
        }
    }
}
