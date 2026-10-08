using System.Windows;

namespace NarzedziaIPUpdater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Double-click bez parametrów = tryb standalone: updater sam
            // wykrywa program obok siebie i proponuje aktualizację.
            if (e.Args == null || e.Args.Length == 0)
            {
                MainWindow = new UpdaterWindow();
                MainWindow.Show();
                return;
            }

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

            MainWindow = new UpdaterWindow(opts);
            MainWindow.Show();
        }
    }
}
