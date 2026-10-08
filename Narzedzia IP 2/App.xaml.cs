using System.Windows;

namespace NarzedziaIP
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool updated = BootUpdate.TrySilentUpdate();
            if (updated)
            {
                Shutdown();
                return;
            }

            // Start od razu, bez blokującego okienka: DHCP sprawdzany jest
            // w tle przez MainWindow (status widać w pasku tytułu).
            string dhcpIp;
            try { dhcpIp = global::Narzedzia_IP_2.Properties.Settings.Default.DhcpServer; }
            catch { dhcpIp = null; }

            MainWindow mainWindow = new MainWindow(dhcpIp);

            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
    }
}