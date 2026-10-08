using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
using System.Media;

namespace NarzedziaIP
{
    public partial class MainWindow : Window
    {
        private const string DefaultDhcpIp = "150.150.222.20";

        private string _dhcpIp;

        private readonly List<string> poprawnyIP = new List<string>();
        private readonly List<string> poprawnyMAC = new List<string>();
        private readonly List<string> niePoprawnyIP = new List<string>();
        private readonly List<string> niePoprawnyMAC = new List<string>();

        private readonly List<DhcpLease> _activeLeases = new List<DhcpLease>();

        private readonly DispatcherTimer pingTimer = new DispatcherTimer();
        private readonly List<string> pingIPs = new List<string>();
        private int _pingIndex;

        private DispatcherTimer _dhcpTypingTimer = new DispatcherTimer();


        public MainWindow() : this(DefaultDhcpIp)
        {
        }

        public MainWindow(string dhcpIp)
        {
            InitializeComponent();

            _dhcpIp = string.IsNullOrWhiteSpace(dhcpIp)
                ? DefaultDhcpIp
                : dhcpIp.Trim();

            SetTitleSuffix("trwa sprawdzanie dhcp");

            Loaded += MainWindow_Loaded;

            pingTimer.Interval = TimeSpan.FromSeconds(1);
            pingTimer.Tick += PingTimer_Tick;

            btnCopyIP.IsEnabled = false;
            btnCopyMAC.IsEnabled = false;
            btnMSRA.IsEnabled = false;
            btnStopPing.IsEnabled = false;

            txtHostname.KeyDown += txtHostname_KeyDown;
            txtHostname2.KeyDown += txtHostname2_KeyDown;

            // LIVE DHCP CHECK
            txtAclSource.TextChanged += TxtAclSource_TextChanged;
            _dhcpTypingTimer.Interval = TimeSpan.FromMilliseconds(500);
            _dhcpTypingTimer.Tick += DhcpTypingTimer_Tick;
        }

        // ============================================================
        // BUTTON EVENTS FROM MainWindow.xaml
        // ============================================================

        private async void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            await SzukajHostnameAsync();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearHostnameSearch();
        }

        private void btnCopyIP_Click(object sender, RoutedEventArgs e)
        {
            string ip = ResolveActiveIp();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                Clipboard.SetText(ip);
                return;
            }

            if (DistinctActiveIpCount() > 1)
            {
                MessageBox.Show(
                    "Wybierz jeden adres IP z listy.",
                    "Kopiuj IP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void btnCopyMAC_Click(object sender, RoutedEventArgs e)
        {
            string mac = ResolveActiveMac();
            if (!string.IsNullOrWhiteSpace(mac))
            {
                Clipboard.SetText(mac.Replace("-", ""));
                return;
            }

            if (DistinctActiveIpCount() > 1)
            {
                MessageBox.Show(
                    "Wybierz jeden adres z listy.",
                    "Kopiuj MAC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void btnMSRA_Click(object sender, RoutedEventArgs e)
        {
            StartMSRA();
        }

        private async void btnStartPing_Click(object sender, RoutedEventArgs e)
        {
            await StartPingAsync();
        }

        private void btnStopPing_Click(object sender, RoutedEventArgs e)
        {
            pingTimer.Stop();

            btnStopPing.IsEnabled = false;
            btnStartPing.IsEnabled = true;
        }

        // ============================================================
        // ENTER KEY EVENTS
        // ============================================================

        private async void txtHostname_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SearchAndConnectAsync();
            }
        }

        private async void txtHostname2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await StartPingAsync();
            }
        }

        // ============================================================
        // CLEAR
        // ============================================================

        private void ClearHostnameSearch()
        {
            txtHostname.Clear();

            lblIP.Text = "";
            lblMAC.Text = "";

            _activeLeases.Clear();
            RefreshResultList();

            lblDnsWarning.Text = "";
            lblDnsWarning.Visibility = Visibility.Collapsed;

            btnCopyIP.IsEnabled = false;
            btnCopyMAC.IsEnabled = false;
            btnMSRA.IsEnabled = false;

            txtHostname.Focus();
        }

        private void RefreshResultList()
        {
            try
            {
                lstResults.ItemsSource = null;
                lstResults.ItemsSource = _activeLeases
                    .Select(l => l.IPAddress + "  " + l.MacAddress)
                    .ToList();
                lstResults.SelectedIndex = _activeLeases.Count == 1 ? 0 : -1;
            }
            catch { }
        }

        private string SelectedLeaseIp()
        {
            try
            {
                int i = lstResults.SelectedIndex;
                if (i >= 0 && i < _activeLeases.Count)
                    return _activeLeases[i].IPAddress;
            }
            catch { }
            return null;
        }

        private string SelectedLeaseMac()
        {
            try
            {
                int i = lstResults.SelectedIndex;
                if (i >= 0 && i < _activeLeases.Count)
                    return _activeLeases[i].MacAddress;
            }
            catch { }
            return null;
        }

        private string ResolveActiveIp()
        {
            List<string> actives;
            try { actives = _activeLeases.Select(l => l.IPAddress).ToList(); }
            catch { actives = new List<string>(); }
            string direct = null;
            try { direct = lblIP.Text; }
            catch { direct = null; }
            return IpSelection.ResolveActiveIp(actives, SelectedLeaseIp(), direct);
        }

        private string ResolveActiveMac()
        {
            string m = SelectedLeaseMac();
            if (!string.IsNullOrWhiteSpace(m))
                return m;
            List<string> macs;
            try { macs = _activeLeases.Select(l => l.MacAddress).Distinct().ToList(); }
            catch { macs = new List<string>(); }
            return macs.Count == 1 ? macs[0] : null;
        }

        private int DistinctActiveIpCount()
        {
            try { return _activeLeases.Select(l => l.IPAddress).Distinct().Count(); }
            catch { return 0; }
        }

        // ============================================================
        // DHCP SEARCH
        // ============================================================

        private async Task SzukajHostnameAsync()
        {
            string hostname = txtHostname.Text.Trim();

            if (string.IsNullOrWhiteSpace(hostname))
            {
                lblIP.Text = "Brak hostname!";
                lblMAC.Text = "Brak hostname!";
                return;
            }

            if (IsIPv4Address(hostname))
            {
                lblIP.Text = hostname;
                lblMAC.Text = "Nie dotyczy - wpisano adres IP";

                _activeLeases.Clear();
                RefreshResultList();

                btnCopyIP.IsEnabled = true;
                btnCopyMAC.IsEnabled = false;
                btnMSRA.IsEnabled = true;

                txtHistoria.AppendText(
                    $"{hostname}\tWpisano IP bez wyszukiwania DHCP\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                );

                txtHistoria.ScrollToEnd();

                return;
            }

            SetSearchInProgress(true, "Szukam w DHCP...");

            poprawnyIP.Clear();
            poprawnyMAC.Clear();
            niePoprawnyIP.Clear();
            niePoprawnyMAC.Clear();

            lblIP.Text = "Szukam...";
            lblMAC.Text = "Szukam...";

            lblDnsWarning.Text = "";
            lblDnsWarning.Visibility = Visibility.Collapsed;

            try
            {
                List<DhcpLease> leases = await GetDhcpLeasesAsync(_dhcpIp, hostname);

                if (leases.Count == 0)
                {
                    lblIP.Text = "Brak adresu IP w DHCP";
                    lblMAC.Text = "Brak adresu MAC w DHCP";
                    _activeLeases.Clear();
                    RefreshResultList();
                    return;
                }

                List<DhcpLease> active = new List<DhcpLease>();

                foreach (DhcpLease lease in leases)
                {
                    bool pingOk = await PingHostAsync(lease.IPAddress);

                    if (pingOk)
                    {
                        poprawnyIP.Add(lease.IPAddress);
                        poprawnyMAC.Add(lease.MacAddress);
                        active.Add(lease);
                    }
                    else
                    {
                        niePoprawnyIP.Add(lease.IPAddress);
                        niePoprawnyMAC.Add(lease.MacAddress);
                    }
                }

                if (poprawnyIP.Count > 0)
                {
                    lblIP.Text = string.Join(", ", poprawnyIP.Distinct());
                    lblMAC.Text = string.Join(", ", poprawnyMAC.Distinct());

                    _activeLeases.Clear();
                    _activeLeases.AddRange(active);
                    RefreshResultList();

                    txtHistoria.AppendText(
                        $"{hostname}\t{lblIP.Text}\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                    );

                    txtHistoria.ScrollToEnd();

                    await CheckDnsVsDhcpAsync(hostname, poprawnyIP);

                    btnCopyIP.IsEnabled = true;
                    btnCopyMAC.IsEnabled = true;
                    btnMSRA.IsEnabled = true;
                }
                else
                {
                    lblIP.Text = "Nie znaleziono aktywnego IP";
                    lblMAC.Text = "Nie znaleziono aktywnego MAC";

                    _activeLeases.Clear();
                    RefreshResultList();

                    if (niePoprawnyIP.Count > 0)
                    {
                        txtHistoria.AppendText(
                            $"{hostname}\tNieaktywne: {string.Join(", ", niePoprawnyIP.Distinct())}\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                        );

                        txtHistoria.ScrollToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                lblIP.Text = "Błąd DHCP";
                lblMAC.Text = "";

                txtHistoria.AppendText(
                    $"Błąd DHCP: {ex.Message}{Environment.NewLine}"
                );

                txtHistoria.ScrollToEnd();

                MessageBox.Show(
                    ex.Message,
                    "Błąd podczas wyszukiwania DHCP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SetSearchInProgress(false);
            }
        }
        private async void btnSearchConnect_Click(object sender, RoutedEventArgs e)
        {
            await SearchAndConnectAsync();
        }
        private async Task SearchAndConnectAsync()
        {
            string input = txtHostname.Text.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                lblIP.Text = "Brak hostname!";
                lblMAC.Text = "Brak hostname!";
                return;
            }

            if (IsIPv4Address(input))
            {
                lblIP.Text = input;
                lblMAC.Text = "Nie dotyczy - wpisano adres IP";

                btnCopyIP.IsEnabled = true;
                btnCopyMAC.IsEnabled = false;
                btnMSRA.IsEnabled = true;

                txtHistoria.AppendText(
                    $"{input}\tPołączenie MSRA bez wyszukiwania DHCP\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                );

                txtHistoria.ScrollToEnd();

                StartMSRA();
                return;
            }

            await SzukajHostnameAsync();

            StartMSRA();
        }

        // ============================================================
        // DHCP POWERSHELL QUERY
        // ============================================================

        private async Task<List<DhcpLease>> GetDhcpLeasesAsync(string dhcpServer, string hostname)
        {
            return await Task.Run(() =>
            {
                const int timeoutMs = 60000; // 60 seconds

                string safeDhcpServer = EscapePowerShellSingleQuotedString(dhcpServer);
                string safeHostname = EscapePowerShellSingleQuotedString(hostname);

                string psCommand = $@"
$ErrorActionPreference = 'Stop'

$server = '{safeDhcpServer}'
$name = '{safeHostname}'

$scopes = Get-DhcpServerv4Scope -ComputerName $server -ErrorAction Stop

foreach ($scope in $scopes) {{
    try {{
        Get-DhcpServerv4Lease -ComputerName $server -ScopeId $scope.ScopeId -ErrorAction Stop |
        Where-Object {{
            $_.HostName -and $_.HostName -like ($name + '*')
        }} |
        ForEach-Object {{
            $ip = $_.IPAddress.IPAddressToString

            if ([string]::IsNullOrWhiteSpace($ip)) {{
                $ip = $_.IPAddress.ToString()
            }}

            Write-Output ($_.HostName + ""`t"" + $ip + ""`t"" + $_.ClientId)
        }}
    }}
    catch {{
        # Ignore failed scope and continue with next one
    }}
}}
";

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

                    bool exited = process.WaitForExit(timeoutMs);

                    if (!exited)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // ignore kill errors
                        }

                        throw new TimeoutException(
                            $"Zapytanie DHCP przekroczyło limit czasu {timeoutMs / 1000} sekund. Serwer DHCP: {dhcpServer}"
                        );
                    }

                    string output = outputTask.Result;
                    string error = errorTask.Result;

                    if (process.ExitCode != 0)
                    {
                        throw new Exception(
                            $"PowerShell DHCP query failed.{Environment.NewLine}{Environment.NewLine}{error}"
                        );
                    }

                    List<DhcpLease> result = new List<DhcpLease>();

                    string[] lines = output.Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.RemoveEmptyEntries
                    );

                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('\t');

                        if (parts.Length < 3)
                            continue;

                        result.Add(new DhcpLease
                        {
                            HostName = parts[0].Trim(),
                            IPAddress = parts[1].Trim(),
                            MacAddress = parts[2].Trim()
                        });
                    }

                    return result;
                }
            });
        }

        // ============================================================
        // PING HELPERS
        // ============================================================

        private async Task<bool> PingHostAsync(string ipAddress)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(ipAddress, 1000);
                        return reply.Status == IPStatus.Success;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        // ============================================================
        // PING TAB
        // ============================================================

        private async Task StartPingAsync()
        {
            pingTimer.Stop();
            pingIPs.Clear();
            _pingIndex = 0;

            string hostname = txtHostname2.Text.Trim();

            if (string.IsNullOrWhiteSpace(hostname))
            {
                txtPing.AppendText($"Brak hostname{Environment.NewLine}");
                txtPing.ScrollToEnd();
                return;
            }

            if (IsIPv4Address(hostname))
            {
                txtPing.Clear();

                pingIPs.Clear();
                pingIPs.Add(hostname);

                txtPing.AppendText($"Pinguję wpisany adres IP: {hostname}{Environment.NewLine}");
                txtPing.ScrollToEnd();

                btnStartPing.IsEnabled = false;
                btnStopPing.IsEnabled = true;

                pingTimer.Start();

                return;
            }

            txtPing.Clear();
            txtPing.AppendText($"Szukam hosta w DHCP: {hostname}{Environment.NewLine}");

            SetPingSearchInProgress(true, "Szukam hosta w DHCP...");

            try
            {
                List<DhcpLease> leases = await GetDhcpLeasesAsync(_dhcpIp, hostname);

                foreach (DhcpLease lease in leases)
                {
                    bool pingOk = await PingHostAsync(lease.IPAddress);

                    if (pingOk)
                    {
                        pingIPs.Add(lease.IPAddress);
                    }
                }

                if (pingIPs.Count == 0)
                {
                    txtPing.AppendText(
                        $"Nie znaleziono aktywnego adresu IP dla: {hostname}{Environment.NewLine}"
                    );

                    txtPing.ScrollToEnd();
                    return;
                }

                txtPing.AppendText($"Pinguję: {pingIPs[0]}{Environment.NewLine}");
                txtPing.ScrollToEnd();

                btnStartPing.IsEnabled = false;
                btnStopPing.IsEnabled = true;

                pingTimer.Start();
            }
            catch (Exception ex)
            {
                txtPing.AppendText($"Błąd DHCP: {ex.Message}{Environment.NewLine}");
                txtPing.ScrollToEnd();
            }
            finally
            {
                SetPingSearchInProgress(false);

                if (pingTimer.IsEnabled)
                {
                    btnStartPing.IsEnabled = false;
                    btnStopPing.IsEnabled = true;
                }
            }
        }

        private bool _pingBusy;

        private async void PingTimer_Tick(object sender, EventArgs e)
        {
            if (_pingBusy)
                return;

            if (pingIPs.Count == 0)
            {
                pingTimer.Stop();

                btnStopPing.IsEnabled = false;
                btnStartPing.IsEnabled = true;

                return;
            }

            string ip = pingIPs[_pingIndex++ % pingIPs.Count];

            _pingBusy = true;

            try
            {
                using (Ping p = new Ping())
                {
                    PingReply reply = await p.SendPingAsync(ip, 1000);

                    bool success = reply.Status == IPStatus.Success;

                    txtPing.AppendText(
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {ip} - {reply.Status}{Environment.NewLine}"
                    );

                    PlayPingSound(success);
                }
            }
            catch (Exception ex)
            {
                txtPing.AppendText(
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Ping error: {ex.Message}{Environment.NewLine}"
                );

                PlayPingSound(false);
            }
            finally
            {
                _pingBusy = false;
            }

            txtPing.ScrollToEnd();
        }


        // ============================================================
        // MSRA
        // ============================================================

        private void StartMSRA()
        {
            if (DistinctActiveIpCount() > 1 && string.IsNullOrWhiteSpace(SelectedLeaseIp()))
            {
                MessageBox.Show(
                    "Znaleziono więcej niż jeden aktywny adres IP. Wybierz jeden z listy.",
                    "MSRA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            string ip = ResolveActiveIp();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            if (!IsIPv4Address(ip))
            {
                MessageBox.Show(
                    "Aktualna wartość IP nie wygląda jak poprawny adres IPv4.",
                    "MSRA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            string msraPathSysnative = System.IO.Path.Combine(windowsDir, "Sysnative", "msra.exe");
            string msraPathSystem32 = System.IO.Path.Combine(windowsDir, "System32", "msra.exe");

            string msraPath = null;

            // If app runs as 32-bit on 64-bit Windows, Sysnative gives access to real System32
            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess && System.IO.File.Exists(msraPathSysnative))
            {
                msraPath = msraPathSysnative;
            }
            else if (System.IO.File.Exists(msraPathSystem32))
            {
                msraPath = msraPathSystem32;
            }

            if (string.IsNullOrWhiteSpace(msraPath))
            {
                MessageBox.Show(
                    "Nie znaleziono pliku msra.exe.\n\nSprawdź, czy Pomoc zdalna Microsoft jest dostępna na tym komputerze.",
                    "MSRA - brak pliku",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = msraPath,
                    Arguments = "/offerra " + ip,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się uruchomić MSRA.\n\n" + ex.Message,
                    "Błąd MSRA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }


        // ============================================================
        // HELPERS
        // ============================================================
        private readonly Dictionary<string, bool> _dhcpCache = new Dictionary<string, bool>();

        private async void DhcpTypingTimer_Tick(object sender, EventArgs e)
        {
            _dhcpTypingTimer.Stop();

            string ip = txtAclSource.Text.Trim();

            if (!IPAddress.TryParse(ip, out _))
            {
                lblDhcpStatus.Text = "❌ Niepoprawny IP";
                lblDhcpStatus.Foreground = Brushes.Red;

                txtAclSource.Background = Brushes.MistyRose;
                return;
            }

            // 🔄 checking
            lblDhcpStatus.Text = "⏳ Sprawdzanie DHCP...";
            lblDhcpStatus.Foreground = Brushes.Orange;

            bool exists = await DhcpReservationExistsAsync(ip);

            if (exists)
            {
                lblDhcpStatus.Text = "✅ Rezerwacja DHCP OK";
                lblDhcpStatus.Foreground = Brushes.Green;

                txtAclSource.Background = Brushes.LightGreen;
            }
            else
            {
                lblDhcpStatus.Text = "❌ Brak rezerwacji DHCP";
                lblDhcpStatus.Foreground = Brushes.Red;

                txtAclSource.Background = Brushes.MistyRose;
            }
        }
        private void TxtAclSource_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _dhcpTypingTimer.Stop();
            _dhcpTypingTimer.Start();

            // status podczas wpisywania
            lblDhcpStatus.Text = "⌨️ Wpisywanie...";
            lblDhcpStatus.Foreground = System.Windows.Media.Brushes.Gray;
        }

        private async Task<bool> DhcpReservationExistsAsync(string ip)
        {
            // ✅ CACHE HIT
            if (_dhcpCache.ContainsKey(ip))
                return _dhcpCache[ip];

            bool result = await Task.Run(() =>
            {
                try
                {
                    string safeDhcp = EscapePowerShellSingleQuotedString(_dhcpIp);
                    string safeIp = EscapePowerShellSingleQuotedString(ip);

                    string ps = $@"
$ErrorActionPreference = 'Stop'

$server = '{safeDhcp}'
$targetIP = '{safeIp}'

$scopes = Get-DhcpServerv4Scope -ComputerName $server

foreach ($scope in $scopes)
{{
    try
    {{
        $res = Get-DhcpServerv4Reservation -ComputerName $server -ScopeId $scope.ScopeId -ErrorAction Stop |
               Where-Object {{ $_.IPAddress.IPAddressToString -eq $targetIP }}

        if ($res)
        {{
            Write-Output 'FOUND'
            return
        }}
    }}
    catch {{}}
}}

Write-Output 'NOTFOUND'
";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + EncodePowerShellCommand(ps),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using (Process p = new Process())
                    {
                        p.StartInfo = psi;
                        p.Start();

                        string output = p.StandardOutput.ReadToEnd();

                        // ✅ DEBUG – TU!!!
                        System.Diagnostics.Debug.WriteLine($"DHCP OUTPUT for {ip}: [{output}]");

                        p.WaitForExit(15000);

                        return output.Trim().Equals("FOUND", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    return false;
                }
            });

            // ✅ zapis do cache
            _dhcpCache[ip] = result;

            return result;
        }

        // Agregacja wildcardów: patrz AclWildcard.cs (poprawka: bloki > /25).

        private async void BtnGenerateAcl_Click(object sender, RoutedEventArgs e)
        {
            btnGenerateAcl.IsEnabled = false;
            try
            {
                string sourceIP = txtAclSource.Text.Trim();
                lblDhcpStatus.Text = "⏳ Sprawdzanie DHCP...";
                lblDhcpStatus.Foreground = Brushes.Orange;

                Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                // ===== DHCP CHECK SOURCE IP =====


                    // ===== FAST DHCP CHECK =====
                    bool reservationExists = await DhcpReservationExistsAsync(sourceIP);

                    if (reservationExists)
                    {
                        lblDhcpStatus.Text = "✅ Rezerwacja DHCP OK";
                        lblDhcpStatus.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        lblDhcpStatus.Text = "❌ Brak rezerwacji DHCP";
                        lblDhcpStatus.Foreground = System.Windows.Media.Brushes.Red;
                    }    

                if (!IPAddress.TryParse(sourceIP, out _))
                {
                    MessageBox.Show("Niepoprawny IP źródłowy");
                    return;
                }

                var destIPs = txtAclDest.Text
                    .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => IPAddress.TryParse(x, out _))
                    .Distinct()
                    .ToList();

                if (destIPs.Count == 0)
                {
                    MessageBox.Show("Brak poprawnych IP docelowych");
                    return;
                }

                // ===== SUBNETY / NAZWY / SEQ z zakładki Ustawienia =====
                string subnet1Net;
                string subnet1Mask;
                string subnet2Net;
                string subnet2Mask;
                string inAcl1;
                string outAcl1;
                string inAcl2;
                string outAcl2;
                int seqIn;
                int seqOut;
                if (!TryReadAclSettings(out subnet1Net, out subnet1Mask, out subnet2Net, out subnet2Mask,
                    out inAcl1, out outAcl1, out inAcl2, out outAcl2, out seqIn, out seqOut))
                {
                    return;
                }

                // ===== SUBNET CHECK =====
                bool InSubnet(string ip, string net, string mask)
                {
                    var ipBytes = IPAddress.Parse(ip).GetAddressBytes();
                    var netBytes = IPAddress.Parse(net).GetAddressBytes();
                    var maskBytes = IPAddress.Parse(mask).GetAddressBytes();

                    for (int i = 0; i < 4; i++)
                    {
                        if ((ipBytes[i] & maskBytes[i]) != (netBytes[i] & maskBytes[i]))
                            return false;
                    }
                    return true;
                }

                // ===== SEQ =====
                int NextSeq(int seq)
                {
                    do { seq++; }
                    while (seq % 10 == 0);
                    return seq;
                }

                // ===== PODZIAŁ =====
                var group1 = destIPs.Where(ip => InSubnet(ip, subnet1Net, subnet1Mask)).ToList();
                var group2 = destIPs.Where(ip => InSubnet(ip, subnet2Net, subnet2Mask)).ToList();

                // ✅ GLOBAL CHECK
                if (group1.Count == 0 && group2.Count == 0)
                {
                    txtAclOutput.Text = "Żaden adres nie pasuje do znanych podsieci (10.202 / 10.207).";
                    return;
                }

                var sb = new StringBuilder();

                sb.AppendLine("conf t");

                sb.AppendLine("ip access-list resequence " + inAcl1 + " 10 10");
                sb.AppendLine("ip access-list resequence " + outAcl1 + " 10 10");
                sb.AppendLine("ip access-list resequence " + inAcl2 + " 10 10");
                sb.AppendLine("ip access-list resequence " + outAcl2 + " 10 10");
                sb.AppendLine();

                // ===== SUBNET 1 =====
                if (group1.Count > 0)
                {
                    var blocks = AclWildcard.MergeToWildcard(group1.OrderBy(AclWildcard.IPToUint).ToList());

                    sb.AppendLine($"ip access-list extended {inAcl1}");

                    foreach (var b in blocks)
                    {
                        seqIn = NextSeq(seqIn);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqIn} permit ip host {b.Network} host {sourceIP}");
                        else
                            sb.AppendLine($"{seqIn} permit ip {b.Network} {b.Wildcard} host {sourceIP}");
                    }

                    sb.AppendLine($"ip access-list extended {outAcl1}");

                    foreach (var b in blocks)
                    {
                        seqOut = NextSeq(seqOut);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} host {b.Network}");
                        else
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} {b.Network} {b.Wildcard}");
                    }

                    sb.AppendLine();
                }

                // ===== SUBNET 2 =====
                if (group2.Count > 0)
                {
                    var blocks = AclWildcard.MergeToWildcard(group2.OrderBy(AclWildcard.IPToUint).ToList());

                    sb.AppendLine($"ip access-list extended {inAcl2}");

                    foreach (var b in blocks)
                    {
                        seqIn = NextSeq(seqIn);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqIn} permit ip host {b.Network} host {sourceIP}");
                        else
                            sb.AppendLine($"{seqIn} permit ip {b.Network} {b.Wildcard} host {sourceIP}");
                    }

                    sb.AppendLine($"ip access-list extended {outAcl2}");

                    foreach (var b in blocks)
                    {
                        seqOut = NextSeq(seqOut);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} host {b.Network}");
                        else
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} {b.Network} {b.Wildcard}");
                    }

                    sb.AppendLine();
                }

                // ===== RESEQUENCE =====
                sb.AppendLine("ip access-list resequence " + inAcl1 + " 10 10");
                sb.AppendLine("ip access-list resequence " + outAcl1 + " 10 10");
                sb.AppendLine("ip access-list resequence " + inAcl2 + " 10 10");
                sb.AppendLine("ip access-list resequence " + outAcl2 + " 10 10");

                sb.AppendLine("end");
                sb.AppendLine("wr");

                txtAclOutput.Text = sb.ToString();
            }

            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                btnGenerateAcl.IsEnabled = true;
            }

        }

        private void LoadAclSettings()
        {
            try
            {
                var s = global::Narzedzia_IP_2.Properties.Settings.Default;
                txtAclSubnet1Net.Text = s.AclSubnet1Net;
                txtAclSubnet1Mask.Text = s.AclSubnet1Mask;
                txtAclSubnet2Net.Text = s.AclSubnet2Net;
                txtAclSubnet2Mask.Text = s.AclSubnet2Mask;
                txtAclIn1.Text = s.AclIn1;
                txtAclOut1.Text = s.AclOut1;
                txtAclIn2.Text = s.AclIn2;
                txtAclOut2.Text = s.AclOut2;
                txtAclSeqIn.Text = s.AclSeqIn.ToString();
                txtAclSeqOut.Text = s.AclSeqOut.ToString();
            }
            catch { }
        }

        private bool TryReadAclSettings(out string subnet1Net, out string subnet1Mask,
            out string subnet2Net, out string subnet2Mask,
            out string inAcl1, out string outAcl1, out string inAcl2, out string outAcl2,
            out int seqIn, out int seqOut)
        {
            subnet1Net = subnet1Mask = subnet2Net = subnet2Mask = null;
            inAcl1 = outAcl1 = inAcl2 = outAcl2 = null;
            seqIn = seqOut = 0;
            try
            {
                subnet1Net = txtAclSubnet1Net.Text.Trim();
                subnet1Mask = txtAclSubnet1Mask.Text.Trim();
                subnet2Net = txtAclSubnet2Net.Text.Trim();
                subnet2Mask = txtAclSubnet2Mask.Text.Trim();
                inAcl1 = txtAclIn1.Text.Trim();
                outAcl1 = txtAclOut1.Text.Trim();
                inAcl2 = txtAclIn2.Text.Trim();
                outAcl2 = txtAclOut2.Text.Trim();
                string seqInText = txtAclSeqIn.Text.Trim();
                string seqOutText = txtAclSeqOut.Text.Trim();

                if (!IsIPv4Address(subnet1Net) || !AclWildcard.IsContiguousMask(subnet1Mask))
                {
                    MessageBox.Show("Niepoprawna podsieć 1 (adres sieci lub maska).");
                    return false;
                }
                if (!IsIPv4Address(subnet2Net) || !AclWildcard.IsContiguousMask(subnet2Mask))
                {
                    MessageBox.Show("Niepoprawna podsieć 2 (adres sieci lub maska).");
                    return false;
                }
                foreach (string n in new[] { inAcl1, outAcl1, inAcl2, outAcl2 })
                {
                    if (string.IsNullOrWhiteSpace(n) || n.Any(char.IsWhiteSpace))
                    {
                        MessageBox.Show("Nazwy ACL nie mogą być puste ani zawierać spacji.");
                        return false;
                    }
                }
                if (!int.TryParse(seqInText, out seqIn) || seqIn <= 0
                    || !int.TryParse(seqOutText, out seqOut) || seqOut <= 0)
                {
                    MessageBox.Show("Numery SEQ muszą być dodatnimi liczbami.");
                    return false;
                }

                var s = global::Narzedzia_IP_2.Properties.Settings.Default;
                s.AclSubnet1Net = subnet1Net;
                s.AclSubnet1Mask = subnet1Mask;
                s.AclSubnet2Net = subnet2Net;
                s.AclSubnet2Mask = subnet2Mask;
                s.AclIn1 = inAcl1;
                s.AclOut1 = outAcl1;
                s.AclIn2 = inAcl2;
                s.AclOut2 = outAcl2;
                s.AclSeqIn = seqIn;
                s.AclSeqOut = seqOut;
                s.Save();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Błąd ustawień ACL:\n\n" + ex.Message);
                return false;
            }
        }

        private void BtnCopyAcl_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtAclOutput.Text))
            {
                MessageBox.Show("Brak danych do skopiowania!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }


            Clipboard.SetText(txtAclOutput.Text);

            // ✅ zapamiętaj poprzedni stan
            string prevText = lblDhcpStatus.Text;
            var prevColor = lblDhcpStatus.Foreground;

            // ✅ pokaż info o kopiowaniu
            lblDhcpStatus.Text = "📋 Skopiowano do schowka";
            lblDhcpStatus.Foreground = Brushes.Blue;

            // ✅ flash przycisku
            btnCopyAcl.Content = "✅ Skopiowano";
            btnCopyAcl.Background = Brushes.LightGreen;

            // ✅ reset po 2s
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);

                Dispatcher.Invoke(() =>
                {
                    // ✅ przywróć poprzedni status
                    lblDhcpStatus.Text = prevText;
                    lblDhcpStatus.Foreground = prevColor;

                    // ✅ przywróć przycisk
                    btnCopyAcl.Content = "Kopiuj do schowka";
                    btnCopyAcl.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                });
            });


        }
        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return value.Replace("'", "''");
        }

        private static string EncodePowerShellCommand(string command)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(command);
            return Convert.ToBase64String(bytes);
        }

        private class DhcpLease
        {
            public string HostName { get; set; }
            public string IPAddress { get; set; }
            public string MacAddress { get; set; }
        }
        private void SetSearchInProgress(bool inProgress, string statusText = "")
        {
            progressSearch.Visibility = inProgress ? Visibility.Visible : Visibility.Collapsed;
            txtStatus.Text = statusText;

            btnSearch.IsEnabled = !inProgress;
            btnClear.IsEnabled = !inProgress;
            btnSearchConnect.IsEnabled = !inProgress;

            // Do NOT disable txtHostname.
            // Disabling it can make it look like it disappeared or became unusable.
            txtHostname.IsEnabled = true;

            if (inProgress)
            {
                btnCopyIP.IsEnabled = false;
                btnCopyMAC.IsEnabled = false;
                btnMSRA.IsEnabled = false;
                btnSearchConnect.IsEnabled = true;
            }
        }

        private void SetPingSearchInProgress(bool inProgress, string statusText = "")
        {
            progressPing.Visibility = inProgress ? Visibility.Visible : Visibility.Collapsed;
            txtPingStatus.Text = statusText;

            btnStartPing.IsEnabled = !inProgress;
            txtHostname2.IsEnabled = !inProgress;

            if (inProgress)
            {
                btnStopPing.IsEnabled = false;
            }
        }
        private static bool IsIPv4Address(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return IPAddress.TryParse(value.Trim(), out IPAddress address)
                   && address.AddressFamily == AddressFamily.InterNetwork;
        }

        private void PlayPingSound(bool success)
        {
            if (rbtnSoundOff.IsChecked == true)
                return;

            if (success && rbtnSoundSuccess.IsChecked == true)
            {
                // SystemSounds.Asterisk.Play();
                // SystemSounds.Exclamation.Play();
                SystemSounds.Beep.Play();
                return;
            }

            if (!success && rbtnSoundFail.IsChecked == true)
            {
                SystemSounds.Hand.Play();
                return;
            }
        }

        private async Task<List<string>> GetDnsIPv4AddressesAsync(string hostname)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(hostname);

                    return addresses
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.ToString())
                        .Distinct()
                        .ToList();
                }
                catch
                {
                    return new List<string>();
                }
            });
        }

        private async Task CheckDnsVsDhcpAsync(string hostname, IEnumerable<string> dhcpIps)
        {
            lblDnsWarning.Text = "";
            lblDnsWarning.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(hostname))
                return;

            if (IsIPv4Address(hostname))
                return;

            List<string> dnsIps = await GetDnsIPv4AddressesAsync(hostname);
            List<string> dhcpIpList = dhcpIps
                .Where(ip => IsIPv4Address(ip))
                .Distinct()
                .ToList();

            if (dnsIps.Count == 0 || dhcpIpList.Count == 0)
                return;

            bool anyMatch = dnsIps.Any(dnsIp => dhcpIpList.Contains(dnsIp));

            if (!anyMatch)
            {
                lblDnsWarning.Text =
                    "IP w DNS jest inny niż w DHCP. DNS: " + string.Join(", ", dnsIps);

                lblDnsWarning.Visibility = Visibility.Visible;

                txtHistoria.AppendText(
                    $"{hostname}\tDNS różni się od DHCP. DNS: {string.Join(", ", dnsIps)}\tDHCP: {string.Join(", ", dhcpIpList)}\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"
                );

                txtHistoria.ScrollToEnd();
            }
        }
    }
}