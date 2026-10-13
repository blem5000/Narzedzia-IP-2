using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace NarzedziaIP
{
    // Wiersz tabeli pingowania (multi-device). Powiadomienia dla DataGrid;
    // zielony wiersz = ostatni ping OK, czerwony = ostatni ping błąd.
    public sealed class PingRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Display { get; private set; }
        public string Ip { get; private set; }

        private readonly PingStats _stats = new PingStats();
        private long _ttlSum;
        private int _ttlCount;

        public PingRow(string display, string ip)
        {
            Display = display;
            Ip = ip;
            LastTtl = -1;
        }

        public void Record(bool success, long rttMs, int ttl)
        {
            _stats.Record(success, rttMs);
            LastOk = success;
            if (success)
            {
                LastTtl = ttl;
                if (ttl >= 0)
                {
                    _ttlSum += ttl;
                    _ttlCount++;
                }
                LastSuccessAt = DateTime.Now;
            }
            else
            {
                LastFailAt = DateTime.Now;
            }
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null)
                h(this, new PropertyChangedEventArgs(string.Empty));
        }

        public int Sent { get { return _stats.Sent; } }
        public int Succeeded { get { return _stats.Received; } }

        public string SuccessPctText
        {
            get { return Sent == 0 ? "—" : string.Format("{0:0}%", 100.0 * Succeeded / Sent); }
        }

        public bool? LastOk { get; private set; }
        public int LastTtl { get; private set; }

        public double AvgTtl
        {
            get { return _ttlCount == 0 ? 0 : (double)_ttlSum / _ttlCount; }
        }

        public DateTime? LastSuccessAt { get; private set; }
        public DateTime? LastFailAt { get; private set; }
        public long MinMs { get { return Succeeded == 0 ? 0 : _stats.MinMs; } }
        public long MaxMs { get { return _stats.MaxMs; } }
        public double AvgMs { get { return _stats.AvgMs; } }

        public string StatusText
        {
            get { return LastOk == null ? "—" : (LastOk.Value ? "Tak" : "Nie"); }
        }

        public string LastOkText
        {
            get { return LastOk == null ? "—" : (LastOk.Value ? "Tak" : "Nie"); }
        }

        public string LastTtlText
        {
            get { return LastTtl < 0 ? "—" : LastTtl.ToString(); }
        }

        public string AvgTtlText
        {
            get { return _ttlCount == 0 ? "—" : string.Format("{0:0}", AvgTtl); }
        }

        public string LastSuccessText
        {
            get { return LastSuccessAt == null ? "—" : ((DateTime)LastSuccessAt).ToString("HH:mm:ss"); }
        }

        public string LastFailText
        {
            get { return LastFailAt == null ? "—" : ((DateTime)LastFailAt).ToString("HH:mm:ss"); }
        }

        public string MinText
        {
            get { return Succeeded == 0 ? "—" : MinMs + " ms"; }
        }

        public string MaxText
        {
            get { return Succeeded == 0 ? "—" : MaxMs + " ms"; }
        }

        public string AvgText
        {
            get { return Succeeded == 0 ? "—" : string.Format("{0:0} ms", AvgMs); }
        }

        public string Summary()
        {
            return _stats.Summary(Ip);
        }
    }

    public static class PingTargets
    {
        // Dzieli wpisane cele (przecinki / nowe linie / średniki), deduplikuje.
        public static List<string> ParseInput(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            string[] parts = text.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in parts)
            {
                string v = raw.Trim();
                if (string.IsNullOrEmpty(v) || !seen.Add(v))
                    continue;
                result.Add(v);
            }
            return result;
        }
    }
}
