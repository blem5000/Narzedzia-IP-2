using System;
using System.Threading;
using System.Windows;

namespace NarzedziaIP
{
    // Bezpieczne kopiowanie do schowka. Clipboard.SetText rzuca
    // ExternalException, gdy schowek jest chwilowo zablokowany (RDP,
    // menedżery schowka) - bez tego program by się wywracał.
    // Logika retry jest czysta i testowalna bez prawdziwego schowka.
    public static class ClipboardHelper
    {
        public const int DefaultAttempts = 5;
        public const int DefaultDelayMs = 100;

        public static bool TrySetText(string text)
        {
            return TrySetText(text, DefaultAttempts, DefaultDelayMs);
        }

        public static bool TrySetText(string text, int attempts, int delayMs)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return ExecuteWithRetries(() =>
            {
                Clipboard.SetText(text);
                return true;
            }, attempts, delayMs);
        }

        public static bool ExecuteWithRetries(Func<bool> action, int attempts, int delayMs)
        {
            if (action == null)
                return false;
            if (attempts <= 0)
                attempts = 1;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    if (action())
                        return true;
                }
                catch
                {
                    // zajęty schowek / błąd przejściowy - próbuj dalej
                }
                if (i + 1 < attempts && delayMs > 0)
                {
                    try { Thread.Sleep(delayMs); }
                    catch { }
                }
            }
            return false;
        }
    }
}
