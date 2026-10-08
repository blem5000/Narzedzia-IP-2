using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace NarzedziaIP
{
    // Wybór aktywnego IP: selekcja z listy wyników > pojedynczy wynik >
    // adres wpisany ręcznie. Czysta logika, testowana w NarzedziaIP.Tests.
    public static class IpSelection
    {
        public static string ResolveActiveIp(IEnumerable<string> activeIps, string selectedIp, string directText)
        {
            List<string> distinct = (activeIps ?? Enumerable.Empty<string>())
                .Where(IsIPv4)
                .Select(s => s.Trim())
                .Distinct()
                .ToList();

            if (!string.IsNullOrWhiteSpace(selectedIp))
            {
                string sel = selectedIp.Trim();
                if (IsIPv4(sel) && distinct.Contains(sel))
                    return sel;
            }

            if (distinct.Count == 1)
                return distinct[0];

            string direct = (directText ?? string.Empty).Trim();
            if (IsIPv4(direct))
                return direct;

            return null;
        }

        private static bool IsIPv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            IPAddress address;
            return IPAddress.TryParse(value.Trim(), out address)
                && address.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
