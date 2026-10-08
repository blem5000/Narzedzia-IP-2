using System.Windows;

namespace NarzedziaIPUpdater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            UpdaterOptions opts;
            string error;
            if (!UpdaterOptions.TryParse(e.Args, out opts, out error))
            {
                MessageBox.Show(
                    "Niepoprawne parametry instalatora aktualizacji.\n\n" + error,
                    "Aktualizacja - błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            UpdaterWindow win = new UpdaterWindow(opts);
            MainWindow = win;
            win.Show();
        }
    }
}
