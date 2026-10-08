using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NarzedziaIP
{
    // Konfiguracja firmowa z rejestru (HKCU) - wdrażana plikiem .reg
    // leżącym na share obok programu. Szablon: TacticalRMM-config-TEMPLATE.reg.
    // Rejestr WYGRYWA z ustawieniami użytkownika (polityka centralna).
    public static class CompanyConfig
    {
        public const string RegistryPath = @"Software\NarzedziaIP2";

        public sealed class TacticalConfig
        {
            public string ApiUrl;
            public string ApiKey;
            public string DashboardUrl;

            public bool AnyPresent
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(ApiUrl)
                        || !string.IsNullOrWhiteSpace(ApiKey)
                        || !string.IsNullOrWhiteSpace(DashboardUrl);
                }
            }
        }

        public static string ReadValue(string name)
        {
            return ReadValue(name, RegistryPath);
        }

        // rootPath parametryzowany dla testów (produkcja: RegistryPath).
        public static string ReadValue(string name, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rootPath))
                return null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(rootPath, false))
                {
                    if (key == null)
                        return null;
                    object v = key.GetValue(name);
                    string s = v as string;
                    if (string.IsNullOrWhiteSpace(s))
                        return null;
                    return s.Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        public static TacticalConfig ReadTactical()
        {
            return new TacticalConfig
            {
                ApiUrl = ReadValue("TacticalApiUrl"),
                ApiKey = ReadValue("TacticalApiKey"),
                DashboardUrl = ReadValue("TacticalDashboardUrl")
            };
        }

        // Czyste scalanie: rejestr wygrywa, puste wpisy nie nadpisują.
        public static TacticalConfig Merge(TacticalConfig reg, TacticalConfig user)
        {
            if (reg == null)
                reg = new TacticalConfig();
            if (user == null)
                user = new TacticalConfig();
            return new TacticalConfig
            {
                ApiUrl = FirstNonEmpty(reg.ApiUrl, user.ApiUrl),
                ApiKey = FirstNonEmpty(reg.ApiKey, user.ApiKey),
                DashboardUrl = FirstNonEmpty(reg.DashboardUrl, user.DashboardUrl)
            };
        }

        private static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrWhiteSpace(a))
                return a.Trim();
            if (!string.IsNullOrWhiteSpace(b))
                return b.Trim();
            return null;
        }
    }

    // Klucz API w ustawieniach użytkownika szyfrowany DPAPI (tylko to konto
    // Windows je odczyta). Rejestr firmowy zostaje jawnym tekstem (DPAPI nie
    // przeszłoby między maszynami). Stare jawne klucze migrują się same.
    public static class ProtectedText
    {
        private static readonly byte[] Salt = Encoding.UTF8.GetBytes("NarzedziaIP2.Tactical.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return string.Empty;
            try
            {
                byte[] cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), Salt, DataProtectionScope.CurrentUser);
                return "DPAPI:" + Convert.ToBase64String(cipher);
            }
            catch
            {
                return plain;
            }
        }

        public static bool TryUnprotect(string stored, out string plain)
        {
            plain = null;
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("DPAPI:", StringComparison.Ordinal))
                return false;
            try
            {
                byte[] cipher = Convert.FromBase64String(stored.Substring("DPAPI:".Length));
                plain = Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, Salt, DataProtectionScope.CurrentUser));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Klucz efektywny: rejestr > DPAPI > stary jawny tekst (migracja).
        public static string ResolveApiKey(string registryKey, string storedKey)
        {
            if (!string.IsNullOrWhiteSpace(registryKey))
                return registryKey.Trim();
            string plain;
            if (TryUnprotect(storedKey, out plain))
                return plain;
            return string.IsNullOrWhiteSpace(storedKey) ? null : storedKey;
        }
    }
}
