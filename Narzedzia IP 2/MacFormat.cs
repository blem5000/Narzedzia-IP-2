using System.Linq;

namespace NarzedziaIP
{
    // Formatowanie adresu MAC przy kopiowaniu: zwykły (bez separatorów),
    // z dwukropkami albo ciscowy aabb.ccdd.eeff. Czysta logika, testowana.
    public static class MacFormat
    {
        public const string Plain = "plain";
        public const string Colon = "colon";
        public const string Cisco = "cisco";

        // Sprowadź do 12 znaków hex (wielkie litery); null gdy niepoprawny.
        public static string Normalize(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                return null;
            string hex = new string(mac.Where(c =>
                (c >= '0' && c <= '9') ||
                (c >= 'a' && c <= 'f') ||
                (c >= 'A' && c <= 'F')).ToArray());
            if (hex.Length != 12)
                return null;
            return hex.ToUpperInvariant();
        }

        public static string Format(string mac, string style)
        {
            string hex = Normalize(mac);
            if (hex == null)
                return null;
            if (style == Colon)
                return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
            if (style == Cisco)
                return (hex.Substring(0, 4) + "." + hex.Substring(4, 4) + "." + hex.Substring(8, 4)).ToLowerInvariant();
            return hex;
        }
    }
}
