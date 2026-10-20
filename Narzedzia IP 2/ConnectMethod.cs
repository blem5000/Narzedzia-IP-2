using System;

namespace NarzedziaIP
{
    // Sposob laczenia z komputerem: MSRA (Pomoc zdalna Microsoft), RDP
    // albo TacticalRMM (pulpit Mesh). Wybor zapisywany jest w ustawieniach
    // uzytkownika, wiec przezywa zamkniecie programu.
    public static class ConnectMethod
    {
        public const string Msra = "msra";
        public const string Rdp = "rdp";
        public const string Tactical = "tactical";

        // Nieznana wartosc -> MSRA (bezpieczny domysl).
        // Przyjmujemy tez etykiete "TacticalRMM" - na wypadek gdyby w
        // ustawieniach zostala zapisana nazwa widoczna w interfejsie.
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Msra;
            string v = value.Trim();
            if (v.Equals(Rdp, StringComparison.OrdinalIgnoreCase))
                return Rdp;
            if (v.Equals(Tactical, StringComparison.OrdinalIgnoreCase)
                || v.Equals("TacticalRMM", StringComparison.OrdinalIgnoreCase))
                return Tactical;
            return Msra;
        }

        public static bool IsRdp(string value)
        {
            return Normalize(value) == Rdp;
        }

        public static bool IsTactical(string value)
        {
            return Normalize(value) == Tactical;
        }

        public static string Label(string value)
        {
            string m = Normalize(value);
            if (m == Rdp) return "RDP";
            if (m == Tactical) return "TacticalRMM";
            return "MSRA";
        }
    }
}
