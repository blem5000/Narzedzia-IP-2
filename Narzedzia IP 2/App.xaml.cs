using System.Windows;

namespace NarzedziaIP
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Prevent WPF from shutting down when the first dialog window closes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Silent self-update check (like cisco-acl-helper boot check).
            // When a newer GitHub release is downloaded, the GUI updater
            // takes over - just exit here.
            bool updated = BootUpdate.TrySilentUpdateAsync().GetAwaiter().GetResult();
            if (updated)
            {
                Shutdown();
                return;
            }

            DhcpWindow dhcpWindow = new DhcpWindow();

            bool? result = dhcpWindow.ShowDialog();

            if (result == true)
            {
                MainWindow mainWindow = new MainWindow(dhcpWindow.DhcpIp);

                // Set real main window
                MainWindow = mainWindow;

                // From now on, close the application when MainWindow closes
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}