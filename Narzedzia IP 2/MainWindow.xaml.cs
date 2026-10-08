using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
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

        private TacticalAgent _tacticalAgent;
        private bool _tacticalChecked;
        private string _tacticalCheckedHost;

        private readonly DispatcherTimer pingTimer = new DispatcherTimer();
        private readonly List<string> pingIPs = new List<string>();
        private readonly Dictionary<string, PingStats> _pingStats = new Dictionary<string, PingStats>();
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

            try
            {
                var ws = global::Narzedzia_IP_2.Properties.Settings.Default;
                if (ws.MainWidth >= MinWidth && ws.MainHeight >= MinHeight)
                {
                    Width = ws.MainWidth;
                    Height = ws.MainHeight;
                }
            }
            catch { }

            Closing += MainWindow_Closing;

            Loaded += MainWindow_Loaded;

            pingTimer.Interval = TimeSpan.FromSeconds(1);
            pingTimer.Tick += PingTimer_Tick;

            btnCopyIP.IsEnabled = false;
            btnCopyMAC.IsEnabled = false;
            btnMSRA.IsEnabled = false;
            btnRDP.IsEnabled = false;
            btnTactical.IsEnabled = false;
            btnStopPing.IsEnabled = false;

            txtHostname.KeyDown += txtHostname_KeyDown;
            txtHostname2.KeyDown += txtHostname2_KeyDown;

            // LIVE DHCP CHECK
            txtAclSource.TextChanged += TxtAclSource_TextChanged;
            _dhcpTypingTimer.Interval = TimeSpan.FromMilliseconds(500);
            _dhcpTypingTimer.Tick += DhcpTypingTimer_Tick;
        }

        private void cmbMacFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                string[] styles = { MacFormat.Plain, MacFormat.Colon, MacFormat.Cisco };
                int i = cmbMacFormat.SelectedIndex;
                if (i >= 0 && i < styles.Length)
                {
                    global::Narzedzia_IP_2.Properties.Settings.Default.MacFormat = styles[i];
                    global::Narzedzia_IP_2.Properties.Settings.Default.Save();
                }
            }
            catch { }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                var s = global::Narzedzia_IP_2.Properties.Settings.Default;
                s.MainWidth = Width;
                s.MainHeight = Height;
                s.Save();
            }
            catch { }
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
                if (!ClipboardHelper.TrySetText(ip))
                    ShowClipboardError("Kopiuj IP");
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
                string style = MacFormat.Plain;
                try
                {
                    string saved = global::Narzedzia_IP_2.Properties.Settings.Default.MacFormat;
                    if (saved == MacFormat.Colon || saved == MacFormat.Cisco)
                        style = saved;
                }
                catch { }
                string formatted = MacFormat.Format(mac, style) ?? mac.Replace("-", "");
                if (!ClipboardHelper.TrySetText(formatted))
                    ShowClipboardError("Kopiuj MAC");
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

        private void ShowClipboardError(string title)
        {
            try
            {
                MessageBox.Show(
                    this,
                    "Nie udało się skopiować do schowka (schowek jest zajęty?).",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { }
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

            AppendPingSummaries();

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
            ResetTacticalState();

            lblDnsWarning.Text = "";
            lblDnsWarning.Visibility = Visibility.Collapsed;

            btnCopyIP.IsEnabled = false;
            btnCopyMAC.IsEnabled = false;
            btnMSRA.IsEnabled = false;
            btnRDP.IsEnabled = false;
            btnTactical.IsEnabled = false;

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

            ResetTacticalState();

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
                btnRDP.IsEnabled = true;
                btnTactical.IsEnabled = true;

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
                    await CheckTacticalAgentAsync(hostname);
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

                    await CheckTacticalAgentAsync(hostname);

                    btnCopyIP.IsEnabled = true;
                    btnCopyMAC.IsEnabled = true;
                    btnMSRA.IsEnabled = true;
                    btnRDP.IsEnabled = true;
                    btnTactical.IsEnabled = true;
                }
                else
                {
                    lblIP.Text = "Nie znaleziono aktywnego IP";
                    lblMAC.Text = "Nie znaleziono aktywnego MAC";

                    _activeLeases.Clear();
                    RefreshResultList();
                    await CheckTacticalAgentAsync(hostname);

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
                btnRDP.IsEnabled = true;
                btnTactical.IsEnabled = true;

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

        // Zakresy w paczkach po max MaxParallelScopes procesów (nie proces
        // na zakres - to zabijało wydajność przy wielu zakresach).
        // Zachowanie z zewnątrz jak dotąd: TimeoutException po 60 s,
        // padnięty zakres jest pomijany.
        private async Task<List<DhcpLease>> GetDhcpLeasesAsync(string dhcpServer, string hostname)
        {
            const int timeoutMs = 60000; // 60 seconds

            using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    List<string> scopes;
                    List<string> cached;
                    if (DhcpClient.TryGetCachedScopes(dhcpServer, TimeSpan.FromMinutes(DhcpClient.ScopeCacheMinutes), out cached))
                    {
                        scopes = cached;
                    }
                    else
                    {
                        string scopesOutput = await DhcpClient.RunPowerShellAsync(
                            DhcpClient.BuildScopesCommand(dhcpServer), 15000, cts.Token).ConfigureAwait(false);
                        scopes = DhcpClient.ParseScopeIds(scopesOutput);
                        DhcpClient.StoreScopes(dhcpServer, scopes);
                    }

                    List<List<string>> chunks = DhcpClient.Partition(scopes, DhcpClient.MaxParallelScopes);
                    List<Task<List<DhcpLease>>> tasks = chunks
                        .Select(chunk => QueryScopesChunkAsync(dhcpServer, hostname, chunk, cts.Token))
                        .ToList();
                    List<DhcpLease>[] perChunk = await Task.WhenAll(tasks).ConfigureAwait(false);
                    return perChunk.SelectMany(x => x).ToList();
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(
                        $"Zapytanie DHCP przekroczyło limit czasu {timeoutMs / 1000} sekund. Serwer DHCP: {dhcpServer}"
                    );
                }
            }
        }

        private static async Task<List<DhcpLease>> QueryScopesChunkAsync(string dhcpServer, string hostname, List<string> scopeIds, CancellationToken cancel)
        {
            try
            {
                string output = await DhcpClient.RunPowerShellAsync(
                    DhcpClient.BuildScopeLeasesCommand(dhcpServer, scopeIds, hostname), 45000, cancel).ConfigureAwait(false);
                return DhcpClient.ParseLeaseLines(output);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new List<DhcpLease>(); // ignore failed chunk and continue
            }
        }

        // ============================================================
        // PING HELPERS (implementacja: PingHelper.cs - testowalna)
        // ============================================================

        private Task<bool> PingHostAsync(string ipAddress)
        {
            return PingHelper.PingHostAsync(ipAddress);
        }

        // ============================================================
        // PING TAB
        // ============================================================

        private async Task StartPingAsync()
        {
            pingTimer.Stop();
            pingIPs.Clear();
            _pingIndex = 0;
            _pingStats.Clear();

            int secs = 1;
            try
            {
                secs = PingHelper.ParseIntervalSeconds(txtPingInterval.Text, 1);
                txtPingInterval.Text = secs.ToString();
            }
            catch { secs = 1; }
            pingTimer.Interval = TimeSpan.FromSeconds(secs);

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
                    long rtt = 0;
                    try { rtt = reply.RoundtripTime; }
                    catch { rtt = 0; }

                    PingStats stats = GetPingStats(ip);
                    stats.Record(success, rtt);

                    txtPing.AppendText(
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {ip} - {reply.Status}{Environment.NewLine}"
                    );

                    if (stats.Sent % 10 == 0)
                        txtPing.AppendText(stats.Summary(ip) + Environment.NewLine);

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


        private PingStats GetPingStats(string ip)
        {
            PingStats stats;
            if (!_pingStats.TryGetValue(ip, out stats))
            {
                stats = new PingStats();
                _pingStats[ip] = stats;
            }
            return stats;
        }

        private void AppendPingSummaries()
        {
            try
            {
                foreach (var kv in _pingStats.OrderBy(kv => kv.Key))
                {
                    txtPing.AppendText(kv.Value.Summary(kv.Key) + Environment.NewLine);
                }
                txtPing.ScrollToEnd();
            }
            catch { }
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
                var answer = MessageBox.Show(
                    "Nie znaleziono pliku msra.exe.\n\nSprawdź, czy Pomoc zdalna Microsoft jest dostępna na tym komputerze.\n\nUżyć zamiast tego RDP (mstsc)?",
                    "MSRA - brak pliku",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (answer == MessageBoxResult.Yes)
                    StartRDP();

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

        private void btnRDP_Click(object sender, RoutedEventArgs e)
        {
            StartRDP();
        }

        private void StartRDP()
        {
            if (DistinctActiveIpCount() > 1 && string.IsNullOrWhiteSpace(SelectedLeaseIp()))
            {
                MessageBox.Show(
                    "Znaleziono więcej niż jeden aktywny adres IP. Wybierz jeden z listy.",
                    "RDP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            string ip = ResolveActiveIp();

            if (string.IsNullOrWhiteSpace(ip))
                return;

            string args = RemoteConnect.BuildMstscArguments(ip);
            if (args == null)
            {
                MessageBox.Show(
                    "Aktualna wartość IP nie wygląda jak poprawny adres IPv4.",
                    "RDP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            string mstscPathSysnative = System.IO.Path.Combine(windowsDir, "Sysnative", "mstsc.exe");
            string mstscPathSystem32 = System.IO.Path.Combine(windowsDir, "System32", "mstsc.exe");

            string mstscPath = null;

            if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess && System.IO.File.Exists(mstscPathSysnative))
            {
                mstscPath = mstscPathSysnative;
            }
            else if (System.IO.File.Exists(mstscPathSystem32))
            {
                mstscPath = mstscPathSystem32;
            }

            if (string.IsNullOrWhiteSpace(mstscPath))
            {
                MessageBox.Show(
                    "Nie znaleziono pliku mstsc.exe.",
                    "RDP - brak pliku",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = mstscPath,
                    Arguments = args,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Nie udało się uruchomić RDP.\n\n" + ex.Message,
                    "Błąd RDP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }


        private void ResetTacticalState()
        {
            _tacticalAgent = null;
            _tacticalChecked = false;
            _tacticalCheckedHost = null;
            try
            {
                btnTactical.IsEnabled = true;
                btnTactical.ToolTip = "Otwórz pulpit w TacticalRMM";
            }
            catch { }
        }

        private async Task CheckTacticalAgentAsync(string hostname)
        {
            string apiUrl = null;
            string apiKey = null;
            GetEffectiveTactical(out apiUrl, out apiKey, out string _);
            if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
                return; // brak konfiguracji - przycisk działa jak dotąd

            TacticalAgent found = null;
            try
            {
                List<TacticalAgent> agents = await TacticalRmm.GetAgentsCachedAsync(apiUrl, apiKey, 15);
                found = TacticalRmm.FindByHostname(agents, hostname);
            }
            catch
            {
                return; // offline / błąd API - zostaw domyślny stan
            }

            _tacticalAgent = found;
            _tacticalChecked = true;
            _tacticalCheckedHost = hostname;
            try
            {
                if (found != null)
                {
                    btnTactical.IsEnabled = true;
                    btnTactical.ToolTip = $"TacticalRMM: {found.Hostname} ({found.AgentId})";
                }
                else
                {
                    btnTactical.IsEnabled = false;
                    btnTactical.ToolTip = "Nie znaleziono komputera w TacticalRMM";
                }
            }
            catch { }
        }

        private string ResolveTacticalHostname()
        {
            string typed = null;
            try { typed = txtHostname.Text.Trim(); }
            catch { typed = null; }

            if (string.IsNullOrWhiteSpace(typed))
                return null;

            if (!IsIPv4Address(typed))
                return typed;

            try
            {
                foreach (DhcpLease lease in _activeLeases)
                {
                    if (string.Equals(lease.IPAddress, typed, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(lease.HostName))
                        return lease.HostName.Trim();
                }
            }
            catch { }

            return null;
        }

        private void GetEffectiveTactical(out string apiUrl, out string apiKey, out string dashUrl)
        {
            string setApi = null;
            string setKey = null;
            string setDash = null;
            try
            {
                var s = global::Narzedzia_IP_2.Properties.Settings.Default;
                setApi = s.TacticalApiUrl;
                setKey = s.TacticalApiKey;
                setDash = s.TacticalDashboardUrl;
            }
            catch { }
            CompanyConfig.TacticalConfig merged;
            try
            {
                merged = CompanyConfig.Merge(CompanyConfig.ReadTactical(), new CompanyConfig.TacticalConfig
                {
                    ApiUrl = setApi,
                    ApiKey = setKey,
                    DashboardUrl = setDash
                });
            }
            catch
            {
                merged = new CompanyConfig.TacticalConfig { ApiUrl = setApi, ApiKey = setKey, DashboardUrl = setDash };
            }
            apiUrl = merged.ApiUrl;
            apiKey = merged.ApiKey;
            dashUrl = merged.DashboardUrl;
        }

        private void LoadTacticalSettings()
        {
            try
            {
                GetEffectiveTactical(out string apiUrl, out string apiKey, out string dashUrl);
                txtTacticalApiUrl.Text = apiUrl ?? string.Empty;
                txtTacticalApiKey.Password = apiKey ?? string.Empty;
                txtTacticalDashboardUrl.Text = dashUrl ?? string.Empty;
                CompanyConfig.TacticalConfig reg = null;
                try { reg = CompanyConfig.ReadTactical(); }
                catch { reg = null; }
                if (reg != null && reg.AnyPresent)
                    SetTacticalStatus("Część ustawień pochodzi z rejestru (zarządzane centralnie).", Brushes.Gray);
            }
            catch { }
        }

        private void SaveTacticalSettings()
        {
            try
            {
                var s = global::Narzedzia_IP_2.Properties.Settings.Default;
                s.TacticalApiUrl = txtTacticalApiUrl.Text.Trim();
                s.TacticalApiKey = txtTacticalApiKey.Password;
                s.TacticalDashboardUrl = txtTacticalDashboardUrl.Text.Trim();
                s.Save();
            }
            catch { }
        }

        private void SetTacticalStatus(string text, Brush brush)
        {
            try
            {
                lblTacticalStatus.Text = text ?? string.Empty;
                lblTacticalStatus.Foreground = brush;
            }
            catch { }
        }

        private async void btnTestTactical_Click(object sender, RoutedEventArgs e)
        {
            SaveTacticalSettings();
            btnTestTactical.IsEnabled = false;
            SetTacticalStatus("Sprawdzanie...", Brushes.Gray);
            try
            {
                GetEffectiveTactical(out string apiUrl, out string apiKey, out string dashUrl);
                var agents = await TacticalRmm.GetAgentsAsync(apiUrl, apiKey, 15);
                SetTacticalStatus($"OK: {agents.Count} agentów.", Brushes.Green);
            }
            catch (Exception ex)
            {
                SetTacticalStatus("Błąd: " + ex.Message, Brushes.Red);
            }
            finally
            {
                try { btnTestTactical.IsEnabled = true; }
                catch { }
            }
        }

        private async void btnTactical_Click(object sender, RoutedEventArgs e)
        {
            string host = ResolveTacticalHostname();
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(
                    "Wpisz hostname albo wyszukaj komputer (potrzebna nazwa hosta, nie sam adres IP).",
                    "TacticalRMM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string apiUrl = null;
            string apiKey = null;
            string dashUrl = null;
            GetEffectiveTactical(out apiUrl, out apiKey, out dashUrl);

            if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(dashUrl))
            {
                MessageBox.Show(
                    "Uzupełnij adres API, klucz API i adres dashboardu w zakładce Ustawienia.",
                    "TacticalRMM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            SaveTacticalSettings();

            btnTactical.IsEnabled = false;
            txtStatus.Text = "Szukam w TacticalRMM...";
            try
            {
                TacticalAgent agent = null;
                if (_tacticalChecked && string.Equals(_tacticalCheckedHost, host, StringComparison.OrdinalIgnoreCase))
                    agent = _tacticalAgent;
                else
                {
                    List<TacticalAgent> agents = await TacticalRmm.GetAgentsCachedAsync(apiUrl, apiKey, 15);
                    agent = TacticalRmm.FindByHostname(agents, host);
                    _tacticalAgent = agent;
                    _tacticalChecked = true;
                    _tacticalCheckedHost = host;
                }
                if (agent == null)
                {
                    MessageBox.Show(
                        $"Nie znaleziono agenta '{host}' w TacticalRMM.\n\nSprawdź nazwę albo czy komputer ma zainstalowanego agenta.",
                        "TacticalRMM",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string url = TacticalRmm.TakeControlUrl(dashUrl, agent.AgentId);
                if (url == null)
                {
                    MessageBox.Show(
                        "Niepoprawny adres dashboardu w Ustawieniach.",
                        "TacticalRMM",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    txtHistoria.AppendText($"{host}\tTacticalRMM: {agent.AgentId}\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
                    txtHistoria.ScrollToEnd();
                }
                catch { }

                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Błąd TacticalRMM:\n\n" + ex.Message,
                    "TacticalRMM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                txtStatus.Text = "";
                try { btnTactical.IsEnabled = true; }
                catch { }
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
                    string safeDhcp = DhcpClient.EscapePowerShellSingleQuotedString(_dhcpIp);
                    string safeIp = DhcpClient.EscapePowerShellSingleQuotedString(ip);

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
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + DhcpClient.EncodePowerShellCommand(ps),
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

                var split = AclInput.SplitIps(txtAclDest.Text);
                var destIPs = split.Valid;

                if (split.Rejected.Count > 0)
                {
                    var show = split.Rejected.Take(8).ToList();
                    MessageBox.Show(
                        $"Pominięto niepoprawne adresy ({split.Rejected.Count}): {string.Join(", ", show)}" +
                        (split.Rejected.Count > show.Count ? ", ..." : string.Empty),
                        "ACL",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

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

                // ===== OSOBA (remark jak w cisco-acl-helper) =====
                string owner = string.Empty;
                string ownerEnd = string.Empty;
                try { owner = AclRemark.CleanOwner(txtAclOwner.Text); }
                catch { owner = string.Empty; }
                try { ownerEnd = AclRemark.EndMark(owner); }
                catch { ownerEnd = string.Empty; }

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

                // ===== REMARK ALLOC (numerowane jak wpisy permit) =====
                string TakeRemark(ref int seq)
                {
                    seq = NextSeq(seq);
                    return seq + " remark " + owner;
                }

                string TakeRemarkEnd(ref int seq)
                {
                    seq = NextSeq(seq);
                    return seq + " remark " + ownerEnd;
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

                    if (!string.IsNullOrEmpty(owner))
                        sb.AppendLine(TakeRemark(ref seqIn));

                    foreach (var b in blocks)
                    {
                        seqIn = NextSeq(seqIn);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqIn} permit ip host {b.Network} host {sourceIP}");
                        else
                            sb.AppendLine($"{seqIn} permit ip {b.Network} {b.Wildcard} host {sourceIP}");
                    }

                    if (!string.IsNullOrEmpty(ownerEnd))
                        sb.AppendLine(TakeRemarkEnd(ref seqIn));

                    sb.AppendLine($"ip access-list extended {outAcl1}");

                    foreach (var b in blocks)
                    {
                        seqOut = NextSeq(seqOut);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} host {b.Network}");
                        else
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} {b.Network} {b.Wildcard}");
                    }

                    if (!string.IsNullOrEmpty(ownerEnd))
                        sb.AppendLine(TakeRemarkEnd(ref seqOut));

                    sb.AppendLine();
                }

                // ===== SUBNET 2 =====
                if (group2.Count > 0)
                {
                    var blocks = AclWildcard.MergeToWildcard(group2.OrderBy(AclWildcard.IPToUint).ToList());

                    sb.AppendLine($"ip access-list extended {inAcl2}");

                    if (!string.IsNullOrEmpty(owner))
                        sb.AppendLine(TakeRemark(ref seqIn));

                    foreach (var b in blocks)
                    {
                        seqIn = NextSeq(seqIn);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqIn} permit ip host {b.Network} host {sourceIP}");
                        else
                            sb.AppendLine($"{seqIn} permit ip {b.Network} {b.Wildcard} host {sourceIP}");
                    }

                    if (!string.IsNullOrEmpty(ownerEnd))
                        sb.AppendLine(TakeRemarkEnd(ref seqIn));

                    sb.AppendLine($"ip access-list extended {outAcl2}");

                    foreach (var b in blocks)
                    {
                        seqOut = NextSeq(seqOut);

                        if (b.Wildcard == "0.0.0.0")
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} host {b.Network}");
                        else
                            sb.AppendLine($"{seqOut} permit ip host {sourceIP} {b.Network} {b.Wildcard}");
                    }

                    if (!string.IsNullOrEmpty(ownerEnd))
                        sb.AppendLine(TakeRemarkEnd(ref seqOut));

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


            if (!ClipboardHelper.TrySetText(txtAclOutput.Text))
            {
                ShowClipboardError("Kopiuj do schowka");
                return;
            }

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
        // Escape/Encode PowerShell + DhcpLease: patrz DhcpClient.cs.
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
            btnRDP.IsEnabled = false;
            btnTactical.IsEnabled = false;
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
