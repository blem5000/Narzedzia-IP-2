using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NarzedziaIP
{
    // Log zmian stanu monitorowanych komputerow (up/down).
    //
    // Dotad liczniki w tabeli ping pokazywaly tylko stan biezacy - tracony
    // byl najwazniejszy fakt dla zgloszenia "sieci padla o 14:32": kiedy
    // host znikl i kiedy wrocil. Ten log to zapisuje.
    //
    // Plik: %LOCALAPPDATA%\NarzedziaIP2\ping-wydarzenia.csv (Excel PL).
    public static class PingEventLog
    {
        public const string FileName = "ping-wydarzenia.csv";
        private const string Header = "Data;Godzina;Urzadzenie;IP;Stan;CzasPoprzedniegoStanu";

        // Limit rozmiaru pliku - powyzej tego przycinamy do MaxLines.
        private const long TrimAboveBytes = 1024 * 1024;
        public const int MaxLines = 20000;

        public const string StateUp = "UP";
        public const string StateDown = "DOWN";

        public static string DefaultDirectory()
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NarzedziaIP2");
            }
            catch (Exception)
            {
                return Path.GetTempPath();
            }
        }

        public static string DefaultPath()
        {
            return Path.Combine(DefaultDirectory(), FileName);
        }

        // Czy to zmiana stanu? Pierwszy pomiar (previous == null) to nie
        // zmiana - host dopiero zaczyna być monitorowany.
        public static bool IsTransition(bool? previous, bool current)
        {
            if (!previous.HasValue)
                return false;
            return previous.Value != current;
        }

        // Wykrywa zmiane stanu i od razu dopisuje ja do logu.
        // Zwraca true, gdy zdarzenie zostalo zapisane.
        // previous  - stan sprzed pomiaru (null = pierwszy pomiar)
        // previousChange - kiedy stan zmienil sie poprzednio
        public static bool LogTransition(string path, DateTime now, string device, string ip,
                                        bool? previous, DateTime? previousChange, bool nowUp)
        {
            if (!IsTransition(previous, nowUp))
                return false;

            TimeSpan? duration = null;
            if (previousChange.HasValue)
            {
                TimeSpan d = now - previousChange.Value;
                if (d > TimeSpan.Zero)
                    duration = d;
            }

            AppendTo(path, now, device, ip, nowUp, duration);
            return true;
        }

        public static string StateText(bool up)
        {
            return up ? StateUp : StateDown;
        }

        // Czytelny czas trwania poprzedniego stanu (np. "12 min 30 s").
        // Uzywamy skladowych calkowitych TimeSpan (Minutes/Hours/Days) - TotalMinutes
        // jest ulamkiem i przy formatowaniu zaokraglal w gore (750 s -> "13 min").
        public static string FormatDuration(TimeSpan? duration)
        {
            if (!duration.HasValue)
                return string.Empty;
            TimeSpan d = duration.Value;
            if (d.TotalSeconds < 1)
                return "<1 s";
            if (d.TotalMinutes < 1)
                return string.Format("{0:0} s", d.Seconds);
            if (d.TotalHours < 1)
                return string.Format("{0:0} min {1:0} s", d.Minutes, d.Seconds);
            if (d.TotalDays < 1)
                return string.Format("{0:0} godz {1:0} min", d.Hours, d.Minutes);
            return string.Format("{0:0} dni {1:0} godz", d.Days, d.Hours);
        }

        public static string FormatLine(DateTime time, string device, string ip, string state, TimeSpan? previousDuration)
        {
            return time.ToString("yyyy-MM-dd") + ";"
                + time.ToString("HH:mm:ss") + ";"
                + Escape(device) + ";"
                + Escape(ip) + ";"
                + Escape(state) + ";"
                + Escape(FormatDuration(previousDuration));
        }

        // Excel PL - separator ";", cudzyslowy gdy w wartosci jest ";" lub ".
        public static string Escape(string value)
        {
            if (value == null)
                return string.Empty;
            if (value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static void Append(string device, string ip, bool up, TimeSpan? previousDuration)
        {
            AppendTo(DefaultPath(), DateTime.Now, device, ip, up, previousDuration);
        }

        // Zapis do wskazanej sciezki (testy uzywaja wlasnego pliku).
        public static void AppendTo(string path, DateTime time, string device, string ip, bool up, TimeSpan? previousDuration)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(path))
                    File.WriteAllText(path, Header + Environment.NewLine, Encoding.UTF8);

                File.AppendAllText(path,
                    FormatLine(time, device, ip, StateText(up), previousDuration) + Environment.NewLine,
                    Encoding.UTF8);

                try
                {
                    if (new FileInfo(path).Length > TrimAboveBytes)
                        TrimToMaxLines(path, MaxLines);
                }
                catch (Exception) { }
            }
            catch (Exception)
            {
                // Log nie moze wywracac monitora.
            }
        }

        // Zostawia ostatnie maxLines linii.
        public static bool TrimToMaxLines(string path, int maxLines)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;
                if (maxLines <= 0)
                    return true;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length <= maxLines + 1)
                    return true;
                string[] kept = new string[maxLines + 1];
                kept[0] = lines[0];
                Array.Copy(lines, lines.Length - maxLines, kept, 1, maxLines);
                File.WriteAllLines(path, kept, Encoding.UTF8);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Ostatnie zdarzenia (do podgladu w programie).
        public static List<string> ReadLast(string path, int maxLines)
        {
            List<string> result = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return result;
                if (maxLines <= 0)
                    maxLines = 50;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - maxLines);
                for (int i = start; i < lines.Length; i++)
                    result.Add(lines[i]);
            }
            catch (Exception)
            {
            }
            return result;
        }
    }
}
