using System;

namespace NarzedziaIP
{
    // Sposob laczenia z komputerem: MSRA (Pomoc zdalna Microsoft) albo RDP.
    // Wybor zapisywany jest w ustawieniach uzytkownika, wiec przezywa
    // zamkniecie programu.
    public static class ConnectMethod
    {
        public const string Msra = "msra";
        public const string Rdp = "rdp";

        // Nieznana wartosc -> MSRA (bezpieczny domysl).
        public static string Normalize(string value)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && value.Trim().Equals(Rdp, StringComparison.OrdinalIgnoreCase))
                return Rdp;
            return Msra;
        }

        public static bool IsRdp(string value)
        {
            return Normalize(value) == Rdp;
        }

        public static string Label(string value)
        {
            return IsRdp(value) ? "RDP" : "MSRA";
        }
    }
}
