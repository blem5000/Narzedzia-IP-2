using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NarzedziaIP
{
    public partial class BootUpdateWindow : Window
    {
        private readonly ReleaseInfo _info;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public bool DownloadFailed { get; private set; }

        public BootUpdateWindow(ReleaseInfo info)
        {
            InitializeComponent();
            _info = info;
            lblPhase.Text = "Pobieranie aktualizacji " + info.Tag + "...";
            Loaded += BootUpdateWindow_Loaded;
        }

        private async void BootUpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string dest = null;
            try
            {
                dest = Updater.TempDownloadPath(_info.Tag);
                var progress = new Progress<DownloadProgress>(p =>
                {
                    if (p.Total > 0)
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = Math.Max(0, Math.Min(1000, (int)(1000 * p.Downloaded / p.Total)));
                    }
                    else
                    {
                        progressBar.IsIndeterminate = true;
                    }
                    lblPhase.Text = "Pobieranie aktualizacji " + _info.Tag + "... " + Pct(p);
                });
                await Updater.DownloadAssetAsync(_info.ZipUrl, dest, progress, _cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Cleanup(dest);
                DialogResult = false;
                Close();
                return;
            }
            catch (Exception ex)
            {
                DownloadFailed = true;
                Cleanup(dest);
                Debug.WriteLine("Boot update download failed: " + ex);
                DialogResult = false;
                Close();
                return;
            }

            if (_cts.IsCancellationRequested)
            {
                Cleanup(dest);
                DialogResult = false;
                Close();
                return;
            }

            try
            {
                string bundled = Path.Combine(Updater.AppDir(), Updater.UpdaterExeName);
                string staged = Updater.StageGuiUpdater(bundled);
                Updater.LaunchGuiUpdater(staged, dest, Updater.AppDir(), Updater.ExeName(),
                    Process.GetCurrentProcess().Id, Updater.OwnStartUnix(), _info.Tag);
            }
            catch (Exception ex)
            {
                DownloadFailed = true;
                Cleanup(dest);
                Debug.WriteLine("Boot update launch failed: " + ex);
                DialogResult = false;
                Close();
                return;
            }

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            btnCancel.IsEnabled = false;
            lblPhase.Text = "Anulowanie...";
            try { _cts.Cancel(); }
            catch { }
        }

        private static void Cleanup(string dest)
        {
            if (string.IsNullOrWhiteSpace(dest))
                return;
            try { File.Delete(dest); }
            catch { }
        }

        private static string Pct(DownloadProgress p)
        {
            if (p.Total > 0)
                return (p.Downloaded * 100 / p.Total) + "%";
            return (p.Downloaded / 1024) + " KB";
        }
    }
}
