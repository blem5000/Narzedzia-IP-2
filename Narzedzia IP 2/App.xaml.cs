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