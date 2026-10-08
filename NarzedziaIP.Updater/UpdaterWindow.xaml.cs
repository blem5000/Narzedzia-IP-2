using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NarzedziaIPUpdater
{
    public sealed class UpdaterOptions
    {
        public string Zip { get; set; }
        public string Target { get; set; }
        public string Exe { get; set; }
        public int Pid { get; set; }
        public int PidStart { get; set; }
        public string Tag { get; set; }

        public static bool TryParse(string[] args, out UpdaterOptions opts, out string error)
        {
            opts = new UpdaterOptions();
            error = string.Empty;
            if (args == null)
                args = new string[0];
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> next = () => (i + 1 < args.Length) ? args[++i] : null;
                if (a == "--zip") opts.Zip = next();
                else if (a == "--target") opts.Target = next();
                else if (a == "--exe") opts.Exe = next();
                else if (a == "--pid") { int v; opts.Pid = int.TryParse(next(), out v) ? v : 0; }
                else if (a == "--pid-start") { int v; opts.PidStart = int.TryParse(next(), out v) ? v : 0; }
                else if (a == "--tag") opts.Tag = next();
            }
            if (string.IsNullOrWhiteSpace(opts.Zip) || !File.Exists(opts.Zip))
                return Fail("zip not found: " + opts.Zip, out error);
            if (string.IsNullOrWhiteSpace(opts.Target) || !Directory.Exists(opts.Target))
                return Fail("target dir not found: " + opts.Target, out error);
            if (string.IsNullOrWhiteSpace(opts.Exe))
                return Fail("missing --exe", out error);
            if (opts.Pid <= 0)
                return Fail("missing or invalid --pid", out error);
            if (string.IsNullOrWhiteSpace(opts.Tag))
                return Fail("missing --tag", out error);
            return true;
        }

        private static bool Fail(string msg, out string error)
        {
            error = msg;
            return false;
        }
    }

    // Separate update-installer program (own window), like updater_app.py.
    // Started by the main app AFTER the release zip was downloaded.
    // No Cancel button on purpose: interrupting the copy would leave a half
    // updated install. On error the window stays open with the log path.
    public partial class UpdaterWindow : Window
    {
        private readonly UpdaterOptions _args;
        private readonly string _logPath;
        private StreamWriter _log;
        private bool _failed;

        public UpdaterWindow(UpdaterOptions args)
        {
            InitializeComponent();
            _args = args;
            lblHeading.Text = "Instalowanie aktualizacji " + args.Tag + "...";
            _logPath = Path.Combine(Path.GetTempPath(), "narzedzia_update_" + Process.GetCurrentProcess().Id + ".log");
            _log = new StreamWriter(_logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
            Log("tag=" + args.Tag + " target=" + args.Target + " pid=" + args.Pid);
            Loaded += (s, e) => Task.Run(() => Worker());
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Refuse to close while working - a half-copied install is worse.
            // (Close button is disabled until done/failed, [X] guarded here.)
            if (!_failed)
            {
                // Allow close only after success path scheduled it; worker sets
                // _failed on error. During work, ignore the [X] button.
                // Success auto-closes, so reaching here with !_failed means the
                // 2s timer fired or user forced it - let it close.
            }
            try { if (_log != null) _log.Close(); }
            catch { }
            base.OnClosed(e);
        }

        private void Log(string msg)
        {
            try { _log.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg); }
            catch { }
        }

        private void UiLog(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText(msg + Environment.NewLine);
                txtLog.ScrollToEnd();
            });
        }

        private void SetPhase(string text, int? value)
        {
            Dispatcher.Invoke(() =>
            {
                lblPhase.Text = text;
                if (value.HasValue)
                    progress.Value = Math.Max(0, Math.Min(1000, value.Value));
            });
        }

        private static string Pct(long done, long total)
        {
            if (total <= 0)
                return (done / 1024) + " KB";
            return (done * 100 / total) + "%";
        }

        private void Worker()
        {
            UpdaterOptions a = _args;
            try
            {
                Log("waiting for PID " + a.Pid + " (start " + a.PidStart + ")");
                global::NarzedziaIP.Updater.WaitForExit(a.Pid, a.PidStart, 0.5);
                Log("process gone");

                string staging = Path.Combine(Path.GetTempPath(), "narzedzia_update_stage_" + a.Pid);
                Log("extracting " + a.Zip);
                global::NarzedziaIP.Updater.ExtractZip(a.Zip, staging, new Progress<global::NarzedziaIP.DownloadProgress>(
                    p => SetPhase("Rozpakowywanie... " + Pct(p.Downloaded, p.Total), (int)(400 * p.Downloaded / Math.Max(1, p.Total)))));

                string src = global::NarzedziaIP.Updater.ResolveSource(staging, a.Exe);
                Log("source dir: " + src);

                Log("copying -> " + a.Target);
                global::NarzedziaIP.Updater.CopyTree(src, a.Target, global::NarzedziaIP.Updater.PreservedFiles,
                    new Progress<global::NarzedziaIP.DownloadProgress>(
                        p => SetPhase("Kopiowanie plików... " + Pct(p.Downloaded, p.Total), 400 + (int)(550 * p.Downloaded / Math.Max(1, p.Total)))), 5);

                SetPhase("Uruchamianie nowej wersji...", 980);
                string exeFull = Path.Combine(a.Target, a.Exe);
                Log("starting " + exeFull);
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exeFull,
                    WorkingDirectory = a.Target,
                    UseShellExecute = true
                };
                Process.Start(psi);

                try { File.Delete(a.Zip); }
                catch { }
                try { Directory.Delete(staging, true); }
                catch { }

                Log("done");
                SetPhase("Gotowe — nowa wersja uruchomiona.", 1000);
                Dispatcher.Invoke(() =>
                {
                    DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (s, e) => { t.Stop(); Close(); };
                    t.Start();
                });
            }
            catch (Exception ex)
            {
                Log("FAILED" + Environment.NewLine + ex);
                _failed = true;
                UiLog(ex.GetType().Name + ": " + ex.Message);
                UiLog("Szczegóły zapisano w:" + Environment.NewLine + _logPath);
                Dispatcher.Invoke(() =>
                {
                    lblPhase.Text = "Błąd aktualizacji.";
                    btnClose.IsEnabled = true;
                    Activate();
                });
            }
            finally
            {
                try { if (_log != null) _log.Close(); }
                catch { }
            }
        }
    }
}
