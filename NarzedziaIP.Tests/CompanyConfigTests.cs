using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class CompanyConfigTests
    {
        [TestMethod]
        public void RegistryWinsOverUserSettings()
        {
            var reg = new CompanyConfig.TacticalConfig { ApiUrl = "https://reg.local" };
            var user = new CompanyConfig.TacticalConfig
            {
                ApiUrl = "https://user.local",
                ApiKey = "user-key",
                DashboardUrl = "https://dash.local"
            };
            var m = CompanyConfig.Merge(reg, user);
            Assert.AreEqual("https://reg.local", m.ApiUrl);
            Assert.AreEqual("user-key", m.ApiKey);
            Assert.AreEqual("https://dash.local", m.DashboardUrl);
            Assert.IsTrue(m.AnyPresent);
        }

        [TestMethod]
        public void EmptyRegistryFallsBackToUser()
        {
            var m = CompanyConfig.Merge(
                new CompanyConfig.TacticalConfig(),
                new CompanyConfig.TacticalConfig { ApiKey = "k" });
            Assert.IsNull(m.ApiUrl);
            Assert.AreEqual("k", m.ApiKey);
            Assert.IsTrue(m.AnyPresent);
        }

        [TestMethod]
        public void AllEmptyGivesNulls()
        {
            var m = CompanyConfig.Merge(null, null);
            Assert.IsNull(m.ApiUrl);
            Assert.IsNull(m.ApiKey);
            Assert.IsNull(m.DashboardUrl);
            Assert.IsFalse(m.AnyPresent);
        }

        [TestMethod]
        public void MissingKeyGivesNull()
        {
            Assert.IsNull(CompanyConfig.ReadValue("NieMaTakiego", @"Software\NarzedziaIP2_Tests_Brak"));
            Assert.IsNull(CompanyConfig.ReadValue(null));
            Assert.IsNull(CompanyConfig.ReadValue("X", null));
        }

        [TestMethod]
        public void RegistryRoundtripOnTempKey()
        {
            string path = @"Software\NarzedziaIP2_Tests_" + System.Guid.NewGuid().ToString("N");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path))
                    key.SetValue("TacticalApiUrl", "https://test.local");
                Assert.AreEqual("https://test.local", CompanyConfig.ReadValue("TacticalApiUrl", path));
                Assert.IsNull(CompanyConfig.ReadValue("TacticalApiKey", path));
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(path); }
                catch { }
            }
        }
    }

    [TestClass]
    public class ProtectedTextTests
    {
        [TestMethod]
        public void Roundtrip()
        {
            string cipher = ProtectedText.Protect("tajny-klucz-123");
            Assert.IsTrue(cipher.StartsWith("DPAPI:"));
            string plain;
            Assert.IsTrue(ProtectedText.TryUnprotect(cipher, out plain));
            Assert.AreEqual("tajny-klucz-123", plain);
        }

        [TestMethod]
        public void EmptyStaysEmpty()
        {
            Assert.AreEqual("", ProtectedText.Protect(""));
            Assert.AreEqual("", ProtectedText.Protect(null));
        }

        [TestMethod]
        public void GarbageFails()
        {
            string plain;
            Assert.IsFalse(ProtectedText.TryUnprotect("DPAPI:!!!nie-base64!!!", out plain));
            Assert.IsFalse(ProtectedText.TryUnprotect("jawny-tekst", out plain));
            Assert.IsFalse(ProtectedText.TryUnprotect("", out plain));
            Assert.IsFalse(ProtectedText.TryUnprotect(null, out plain));
        }

        [TestMethod]
        public void ResolveApiKey()
        {
            Assert.AreEqual("reg", ProtectedText.ResolveApiKey("reg", "DPAPI:xxx"));
            string cipher = ProtectedText.Protect("sekret");
            Assert.AreEqual("sekret", ProtectedText.ResolveApiKey(null, cipher));
            Assert.AreEqual("stary-jawny", ProtectedText.ResolveApiKey(" ", "stary-jawny"));
            Assert.IsNull(ProtectedText.ResolveApiKey(null, null));
            Assert.IsNull(ProtectedText.ResolveApiKey("  ", " "));
        }
    }
}
