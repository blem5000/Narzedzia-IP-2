using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace NarzedziaIP
{
    public partial class DhcpWindow : Window
    {
        private const string DefaultDhcpIp = "150.150.222.20";

        public string DhcpIp { get; private set; }

        public DhcpWindow()
        {
            InitializeComponent();

            DhcpIp = DefaultDhcpIp;
            txtServerIP.Text = DefaultDhcpIp;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtServerIP.Focus();
            txtServerIP.SelectAll();

            await CheckDefaultDhcpAsync();
        }

        private async Task CheckDefaultDhcpAsync()
        {
            SetCheckingState(true, "Sprawdzam domyślny DHCP: " + DefaultDhcpIp + "...");

            bool valid = await TestDhcpServerAsync(DefaultDhcpIp);

            SetCheckingState(false, "");

            if (valid)
            {
                DhcpIp = DefaultDhcpIp;
                DialogResult = true;
                Close();
                return;
            }

            txtStatus.Text = "Domyślny DHCP nie odpowiada. Podaj adres ręcznie.";
            txtServerIP.Focus();
            txtServerIP.SelectAll();
        }

        private async void btnOK_Click(object sender, RoutedEventArgs e)
        {
            string enteredIp = txtServerIP.Text.Trim();

            if (string.IsNullOrWhiteSpace(enteredIp))
            {
                enteredIp = DefaultDhcpIp;
                txtServerIP.Text = enteredIp;
            }

            SetCheckingState(true, "Sprawdzam DHCP: " + enteredIp + "...");

            bool valid = await TestDhcpServerAsync(enteredIp);

            SetCheckingState(false, "");

            if (!valid)
            {
                MessageBox.Show(
                    "Nie udało się połączyć z podanym serwerem DHCP lub nie można pobrać zakresów DHCP.\n\nSprawdź adres IP, uprawnienia oraz dostępność modułu DHCP/RSAT.",
                    "Niepoprawny DHCP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                txtStatus.Text = "Niepoprawny adres DHCP lub brak dostępu.";
                txtServerIP.Focus();
                txtServerIP.SelectAll();
                return;
            }

            DhcpIp = enteredIp;
            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SetCheckingState(bool isChecking, string status)
        {
            progressCheck.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;
            txtStatus.Text = status;

            btnOK.IsEnabled = !isChecking;
            btnCancel.IsEnabled = !isChecking;
            txtServerIP.IsEnabled = !isChecking;
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

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            if (value == null)
                return string.Empty;

            return value.Replace("'", "''");
        }

        private static string EncodePowerShellCommand(string command)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(command);
            return Convert.ToBase64String(bytes);
        }
    }
}