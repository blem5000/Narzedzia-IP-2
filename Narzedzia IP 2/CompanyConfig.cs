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

            // null = brak decyzji (uzyj ustawienia uzytkownika).
            // true = zgoda na nieszyfrowane HTTP do API Tactical.
            public bool? AllowInsecureHttp;

            public bool AnyPresent
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(ApiUrl)
                        || !string.IsNullOrWhiteSpace(ApiKey)
                        || !string.IsNullOrWhiteSpace(DashboardUrl)
                        || AllowInsecureHttp.HasValue;
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
                DashboardUrl = ReadValue("TacticalDashboardUrl"),
                AllowInsecureHttp = ReadBool("TacticalAllowInsecureHttp", RegistryPath)
            };
        }

        // Rejestr trzyma wartosci jako tekst ("1"/"true"/"tak" itd.).
        // null = wartosc nieustalona (brak wpisu albo nieczytelna).
        public static bool? ReadBool(string name, string rootPath)
        {
            string s = ReadValue(name, rootPath);
            if (string.IsNullOrWhiteSpace(s))
                return null;
            string v = s.Trim();
            if (v.Equals("1", StringComparison.OrdinalIgnoreCase)
                || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("tak", StringComparison.OrdinalIgnoreCase)
                || v.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (v.Equals("0", StringComparison.OrdinalIgnoreCase)
                || v.Equals("false", StringComparison.OrdinalIgnoreCase)
                || v.Equals("no", StringComparison.OrdinalIgnoreCase)
                || v.Equals("nie", StringComparison.OrdinalIgnoreCase)
                || v.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            return null;
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
                DashboardUrl = FirstNonEmpty(reg.DashboardUrl, user.DashboardUrl),
                AllowInsecureHttp = reg.AllowInsecureHttp ?? user.AllowInsecureHttp
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
