using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NarzedziaIP
{
    // Log bledow - miejsce, gdzie trafiaja wyjatki przechwycone przez kod,
    // ktory woli nie przerywac pracy (albo nie ma wyboru). Bez tego bledy
    // znikaja bez sladu i nie da sie zdiagnozowac problemu zgloszonego
    // przez uzytkownika.
    //
    // Plik: %LOCALAPPDATA%\NarzedziaIP2\bledy.log (rotowany, jak historia).
    // Powtarzajacy sie ten sam blad jest zapisywany raz na 60 sekund
    // (z licznikiem powtorzen), zeby log nie zalewała pliku.
    //
    // Logowanie NIGDY nie moze rzucac wyjatkiem - inaczej obsluga bledu
    // sprowokowalaby nowy blad.
    public static class AppLog
    {
        public const string FileName = "bledy.log";

        private const long TrimAboveBytes = 1024 * 1024;
        public const int MaxLines = 5000;
        private const int DuplicateWindowSeconds = 60;

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, int> _recent = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> _recentAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);

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

        // Kontekst brany z pierwszej ramki stack trace (metoda, w ktorej
        // wyjatek zostal przechwycony).
        public static void Error(Exception ex)
        {
            Error(null, ex);
        }

        public static void Error(string context, Exception ex)
        {
            ErrorTo(DefaultPath(), context, ex);
        }

        public static void Error(string context, string message)
        {
            ErrorTo(DefaultPath(), context, message);
        }

        // Wersja z jawna sciezka - testy pisza do wlasnego pliku.
        public static void ErrorTo(string path, string context, Exception ex)
        {
            try
            {
                if (ex == null)
                {
                    ErrorTo(path, context, "null");
                    return;
                }
                Debug.WriteLine("[" + (context ?? "?") + "] " + ex);
                Write(path, context, ex.GetType().Name + ": " + ex.Message, ex.ToString());
            }
            catch (Exception)
            {
                // Logowanie nie moze nic zepsuc - nawet awarii logowania.
            }
        }

        public static void ErrorTo(string path, string context, string message)
        {
            try
            {
                Debug.WriteLine("[" + (context ?? "?") + "] " + message);
                Write(path, context, message, null);
            }
            catch (Exception)
            {
                // j.w.
            }
        }

        private static void Write(string path, string context, string message, string details)
        {
            if (string.IsNullOrWhiteSpace(context))
                context = FirstFrame(details) ?? "?";

            string key = (path ?? "") + "|" + context + "|" + message;
            DateTime now = DateTime.Now;
            int repeats = 0;

            lock (_lock)
            {
                DateTime at;
                int seen;
                if (_recent.TryGetValue(key, out seen)
                    && _recentAt.TryGetValue(key, out at)
                    && (now - at).TotalSeconds < DuplicateWindowSeconds)
                {
                    // ten sam blad w krotce - liczymy, ale nie dopisujemy
                    _recent[key] = seen + 1;
                    _recentAt[key] = now;
                    return;
                }
                if (_recent.TryGetValue(key, out seen) && seen > 1)
                    repeats = seen;
                _recent[key] = 1;
                _recentAt[key] = now;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append(now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append(" [ERROR] ");
            sb.Append(context);
            sb.AppendLine();
            sb.Append("  ");
            sb.AppendLine(message);
            if (!string.IsNullOrEmpty(details))
            {
                foreach (string line in details.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    sb.Append("  ");
                    sb.AppendLine(line);
                }
            }
            if (repeats > 0)
                sb.AppendLine("  (ten sam blad powtorzyl sie " + repeats + " razy w ciagu ostatniej minuty)");

            AppendToFile(path, sb.ToString());
        }

        private static string FirstFrame(string details)
        {
            if (string.IsNullOrEmpty(details))
                return null;
            string[] lines = details.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
                return null;
            return lines[0].Trim();
        }

        private static void AppendToFile(string path, string content)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                if (!File.Exists(path))
                    File.WriteAllText(path, string.Empty, Encoding.UTF8);
                File.AppendAllText(path, content, Encoding.UTF8);
                try
                {
                    if (new FileInfo(path).Length > TrimAboveBytes)
                        TrimToMaxLines(path, MaxLines);
                }
                catch (Exception) { }
            }
            catch (Exception)
            {
                // brak miejsca na dysku, brak uprawnien - nie ma co zrobic.
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
                if (lines.Length <= maxLines)
                    return true;
                string[] kept = new string[maxLines];
                Array.Copy(lines, lines.Length - maxLines, kept, 0, maxLines);
                File.WriteAllLines(path, kept, Encoding.UTF8);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
