using System;
using System.Threading.Tasks;
using System.Windows;

namespace NarzedziaIP
{
    // Self-update wiring for the main window, like the update section in app.py:
    // version stamp + auto-update checkbox + manual check button (in the
    // Instrukcja tab) and the post-startup dialog fallback.
    public partial class MainWindow : Window
    {
        private bool _updateChecking;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                lblAppVersion.Text = "Wersja: " + Updater.CurrentVersion();
                chkUpdateAuto.IsChecked = global::Narzedzia_IP_2.Properties.Settings.Default.UpdateAuto;
            }
            catch
            {
            }

            BeginDhcpBackgroundCheck();

            // Post-startup fallback: the boot check found an update but could
            // not install it silently (e.g. updater exe missing) - offer it now.
            ReleaseInfo pending = BootUpdate.Pending;
            if (pending != null)
            {
                BootUpdate.ClearPending();
                ShowUpdateDialog(pending);
            }
        }

        private void chkUpdateAuto_Checked(object sender, RoutedEventArgs e)
        {
            SaveUpdateAuto();
        }

        private void chkUpdateAuto_Unchecked(object sender, RoutedEventArgs e)
        {
            SaveUpdateAuto();
        }

        private void SaveUpdateAuto()
        {
            try
            {
                global::Narzedzia_IP_2.Properties.Settings.Default.UpdateAuto = chkUpdateAuto.IsChecked == true;
                global::Narzedzia_IP_2.Properties.Settings.Default.Save();
            }
            catch
            {
            }
        }

        private async void btnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_updateChecking)
                return;
            _updateChecking = true;
            btnCheckUpdate.IsEnabled = false;
            try
            {
                ReleaseInfo info = await Updater.FetchLatestReleaseAsync(15).ConfigureAwait(true);
                bool newer;
                try { newer = Updater.IsNewer(info.Tag, null); }
                catch { newer = false; }
                if (!newer)
                {
                    MessageBox.Show(this,
                        "Masz aktualną wersję (" + Updater.CurrentVersion() + ").",
                        "Aktualizacja", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ShowUpdateDialog(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Błąd sprawdzania aktualizacji:\n\n" + ex.Message,
                    "Aktualizacja", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _updateChecking = false;
                btnCheckUpdate.IsEnabled = true;
            }
        }

        private void ShowUpdateDialog(ReleaseInfo info)
        {
            var dlg = new UpdateDialog(info) { Owner = this };
            bool? result = dlg.ShowDialog();
            if (result == true)
                UpdateFlow.ExitForUpdate();
        }
    }
}
