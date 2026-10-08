using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NarzedziaIP
{
    // Manual update dialog, like _show_update_dialog() in app.py:
    // release notes + Download&Install / Skip version / Later + progress.
    public partial class UpdateDialog : Window
    {
        private readonly ReleaseInfo _info;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public UpdateDialog(ReleaseInfo info)
        {
            InitializeComponent();
            _info = info;

            string notes = (info.Body ?? string.Empty).Trim();
            if (notes.Length > 2000)
                notes = notes.Substring(0, 2000) + "...";
            if (string.IsNullOrWhiteSpace(notes))
                notes = "-";

            lblTitle.Text = "Dostępna jest nowa wersja: " + info.Tag
                + " (obecna: " + Updater.CurrentVersion() + ")";
            txtNotes.Text = string.IsNullOrWhiteSpace(info.Name) || info.Name == info.Tag
                ? notes
                : info.Name + Environment.NewLine + Environment.NewLine + notes;
        }

        private async void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            btnInstall.IsEnabled = false;
            btnSkip.IsEnabled = false;
            btnLater.IsEnabled = false;

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
                    lblStatus.Text = "Pobieranie " + _info.Tag + "... " + Pct(p);
                });
                await Updater.DownloadAssetAsync(_info.ZipUrl, dest, progress, _cts.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Cleanup(dest);
                lblStatus.Text = string.Empty;
                btnInstall.IsEnabled = true;
                btnSkip.IsEnabled = true;
                btnLater.IsEnabled = true;
                MessageBox.Show(this, "Błąd pobierania aktualizacji:\n\n" + ex.Message,
                    "Aktualizacja", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // No extra confirmation: the user already chose Download&Install.
            try
            {
                UpdateFlow.LaunchInstaller(dest, _info.Tag);
            }
            catch (Exception ex)
            {
                Cleanup(dest);
                lblStatus.Text = string.Empty;
                btnInstall.IsEnabled = true;
                btnSkip.IsEnabled = true;
                btnLater.IsEnabled = true;
                MessageBox.Show(this, "Nie udało się uruchomić instalatora:\n\n" + ex.Message,
                    "Aktualizacja", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void btnSkip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                global::Narzedzia_IP_2.Properties.Settings.Default.UpdateSkipped = _info.Tag;
                global::Narzedzia_IP_2.Properties.Settings.Default.Save();
            }
            catch
            {
            }
            DialogResult = false;
            Close();
        }

        private void btnLater_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
