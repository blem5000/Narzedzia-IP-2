using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace NarzedziaIP
{
    // Ping wydzielony z MainWindow, żeby dało się go testować
    // (m.in. 127.0.0.1 odpowiada też na runnerach CI).
    public static class PingHelper
    {
        public static async Task<bool> PingHostAsync(string ipAddress, int timeoutMs)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(ipAddress, timeoutMs <= 0 ? 1000 : timeoutMs);
                        return reply.Status == IPStatus.Success;
                    }
                }
                catch
                {
                    return false;
                }
            }).ConfigureAwait(false);
        }

        public static Task<bool> PingHostAsync(string ipAddress)
        {
            return PingHostAsync(ipAddress, 1000);
        }

        // Interwał pingowania w sekundach z pola tekstowego (1-300, fallback).
        public static int ParseIntervalSeconds(string text, int fallback)
        {
            int v;
            if (!int.TryParse((text ?? string.Empty).Trim(), out v) || v < 1 || v > 300)
                return fallback <= 0 ? 1 : fallback;
            return v;
        }

        public static string PingLogFileName(System.DateTime time)
        {
            return "ping-" + time.ToString("yyyyMMdd-HHmmss") + ".log";
        }
    }

    // Statystyki pingowania jednego IP. Czysta logika, testowana.
    public sealed class PingStats
    {
        public int Sent { get; private set; }
        public int Received { get; private set; }
        public int Lost { get { return Sent - Received; } }
        public double LossPct { get { return Sent == 0 ? 0 : 100.0 * Lost / Sent; } }
        public long MinMs { get; private set; }
        public long MaxMs { get; private set; }
        public double AvgMs { get; private set; }

        private long _sumMs;

        public PingStats()
        {
            MinMs = -1;
        }

        public void Record(bool success, long rttMs)
        {
            Sent++;
            if (!success)
                return;
            Received++;
            if (MinMs < 0 || rttMs < MinMs)
                MinMs = rttMs;
            if (rttMs > MaxMs)
                MaxMs = rttMs;
            _sumMs += rttMs;
            AvgMs = (double)_sumMs / Received;
        }

        public string Summary(string ip)
        {
            if (Sent == 0)
                return ip + ": brak pomiarów";
            if (Received == 0)
                return string.Format("{0}: wysłano {1}, odebrano 0 (straty 100%)", ip, Sent);
            return string.Format("{0}: wysłano {1}, odebrano {2}, straty {3:0}%, min/śr/max {4}/{5:0}/{6} ms",
                ip, Sent, Received, LossPct, MinMs, AvgMs, MaxMs);
        }
    }
}
