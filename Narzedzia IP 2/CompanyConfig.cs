using System;
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
}
