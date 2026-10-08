using System;
using System.IO;
using System.Text;

namespace NarzedziaIP
{
    // Historia wyszukiwań i połączeń na dysku (profil użytkownika, nie katalog
    // programu - ten leży czasem na share i jest nadpisywany przy aktualizacji).
    // Format CSV ze średnikiem (Excel PL). Nic nigdy nie rzuca.
    public static class HistoryLog
    {
        public const string FileName = "historia.csv";
        private const string Header = "Data;Akcja;Cel;Wynik;Uwaga";

        public static string DefaultDirectory()
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NarzedziaIP2");
            }
            catch
            {
                return Path.GetTempPath();
            }
        }

        public static string DefaultLogPath()
        {
            return Path.Combine(DefaultDirectory(), FileName);
        }

        public static string EscapeCsv(string value)
        {
            if (value == null)
                return string.Empty;
            if (value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string FormatLine(DateTime time, string akcja, string cel, string wynik, string uwaga)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss") + ";"
                + EscapeCsv(akcja) + ";"
                + EscapeCsv(cel) + ";"
                + EscapeCsv(wynik) + ";"
                + EscapeCsv(uwaga);
        }

        public static bool Append(string akcja, string cel, string wynik, string uwaga)
        {
            return AppendTo(DefaultLogPath(), DateTime.Now, akcja, cel, wynik, uwaga);
        }

        public static bool AppendTo(string path, DateTime time, string akcja, string cel, string wynik, string uwaga)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return false;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(path))
                    File.WriteAllText(path, Header + Environment.NewLine, Encoding.UTF8);
                File.AppendAllText(path,
                    FormatLine(time, akcja, cel, wynik, uwaga) + Environment.NewLine, Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
