using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace NarzedziaIP
{
    // Serwer DHCP sprawdzany w tle (bez blokującego okienka startowego).
    // Status widać w pasku tytułu: "- trwa sprawdzanie dhcp" znika po
    // poprawnym sprawdzeniu; przy braku połączenia tytuł pokazuje
    // "- DHCP niedostępny", a serwer zmienisz w zakładce Ustawienia.
    public partial class MainWindow : Window
    {
        private const string BaseTitle = "Narzedzia IP";

        private void SetTitleSuffix(string suffix)
        {
            try
            {
                Title = string.IsNullOrWhiteSpace(suffix)
                    ? BaseTitle
                    : BaseTitle + " - " + suffix.Trim();
            }
            catch
            {
            }
        }

        private void SetDhcpServerStatus(string text, Brush brush)
        {
            try
            {
                lblDhcpServerStatus.Text = text ?? string.Empty;
                lblDhcpServerStatus.Foreground = brush;
            }
            catch
            {
            }
        }

        private async void BeginDhcpBackgroundCheck()
        {
            try { txtDhcpServer.Text = _dhcpIp; }
            catch { }

            SetTitleSuffix("trwa sprawdzanie dhcp");
            SetDhcpServerStatus("Sprawdzanie...", Brushes.Gray);
            try { btnTestDhcp.IsEnabled = false; }
            catch { }

            bool ok = false;
            try { ok = await TestDhcpServerAsync(_dhcpIp); }
            catch { ok = false; }

            try { btnTestDhcp.IsEnabled = true; }
            catch { }

            if (ok)
            {
                SetTitleSuffix(string.Empty);
                SetDhcpServerStatus("Połączono: " + _dhcpIp, Brushes.Green);
            }
            else
            {
                SetTitleSuffix("DHCP niedostępny");
                SetDhcpServerStatus("Brak połączenia. Wpisz inny adres i kliknij Sprawdź.", Brushes.Red);
            }
        }

        private async void btnTestDhcp_Click(object sender, RoutedEventArgs e)
        {
            string entered = null;
            try { entered = txtDhcpServer.Text.Trim(); }
            catch { entered = null; }

            if (string.IsNullOrWhiteSpace(entered))
            {
                SetDhcpServerStatus("Wpisz adres serwera DHCP.", Brushes.Red);
                return;
            }

            try { btnTestDhcp.IsEnabled = false; }
            catch { }
            SetTitleSuffix("trwa sprawdzanie dhcp");
            SetDhcpServerStatus("Sprawdzanie " + entered + "...", Brushes.Orange);

            bool ok = false;
            try { ok = await TestDhcpServerAsync(entered); }
            catch { ok = false; }

            try { btnTestDhcp.IsEnabled = true; }
            catch { }

            if (!ok)
            {
                SetTitleSuffix("DHCP niedostępny");
                SetDhcpServerStatus("Serwer nie odpowiada albo brak uprawnień / modułu DHCP.", Brushes.Red);
                return;
            }

            _dhcpIp = entered;
            try
            {
                global::Narzedzia_IP_2.Properties.Settings.Default.DhcpServer = entered;
                global::Narzedzia_IP_2.Properties.Settings.Default.Save();
            }
            catch { }

            SetTitleSuffix(string.Empty);
            SetDhcpServerStatus("Połączono: " + entered, Brushes.Green);
        }

        private async Task<bool> TestDhcpServerAsync(string dhcpServer)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string safeDhcpServer = EscapePowerShellSingleQuotedString(dhcpServer);

                    string psCommand =
                        "$ErrorActionPreference = 'Stop'\r\n" +
                        "Get-DhcpServerv4Scope -ComputerName '" + safeDhcpServer + "' | Select-Object -First 1 | Out-Null\r\n";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + EncodePowerShellCommand(psCommand),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (Process process = new Process())
                    {
                        process.StartInfo = psi;
                        process.Start();

                        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                        Task<string> errorTask = process.StandardError.ReadToEndAsync();

                        bool exited = process.WaitForExit(15000);

                        if (!exited)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch
                            {
                                // Ignore kill errors
                            }

                            return false;
                        }

                        string output = outputTask.Result;
                        string error = errorTask.Result;

                        return process.ExitCode == 0;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }
    }
}
