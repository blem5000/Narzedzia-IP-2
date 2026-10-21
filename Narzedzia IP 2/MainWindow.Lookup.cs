using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NarzedziaIP
{
    // Wyszukiwanie WSTECZNE: adres IP -> nazwa hosta (i MAC).
    //
    // Zgloszenie brzmi "komputer na 10.202.130.55 nie wstal" - wtedy
    // potrzebne jest odwrotne spojrzenie na DHCP niz zwykłe wyszukiwanie
    // po nazwie. Sprawdzamy:
    //   - dzierzawe (aktywna dzierżawa DHCP - host jest wlaczony),
    //   - rezerwacje (adres przypisany do komputera na stale - host moze
    //     byc wylaczony, a opis rezerwacji zwykle mowi kto odbiera).
    public partial class MainWindow
    {
        private const int LookupTimeoutMs = 60000;

        private async void btnLookupIp_Click(object sender, RoutedEventArgs e)
        {
            string ip = (txtLookupIp.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(ip))
            {
                lblLookupStatus.Text = "Wpisz adres IP.";
                return;
            }
            if (!IsIPv4Address(ip))
            {
                lblLookupStatus.Text = "To nie jest poprawny adres IPv4.";
                return;
            }

            btnLookupIp.IsEnabled = false;
            lblLookupStatus.Text = "Szukam w DHCP...";
            try
            {
                List<LookupResult> results = await LookupByIpAsync(ip);
                dgLookup.ItemsSource = results;
                if (results.Count == 0)
                    lblLookupStatus.Text = "Nie znaleziono komputera z adresem " + ip + ".";
                else
                    lblLookupStatus.Text = "Znaleziono: " + results.Count + " wpisów dla " + ip + ".";
            }
            catch (Exception ex)
            {
                lblLookupStatus.Text = "Błąd: " + ex.Message;
                AppLog.Error("btnLookupIp_Click", ex);
            }
            finally
            {
                btnLookupIp.IsEnabled = true;
            }
        }

        private async Task<List<LookupResult>> LookupByIpAsync(string ip)
        {
            List<LookupResult> results = new List<LookupResult>();

            using (CancellationTokenSource cts = new CancellationTokenSource(LookupTimeoutMs))
            {
                try
                {
                    List<string> scopes = await GetDhcpScopesAsync(_dhcpIp, cts.Token).ConfigureAwait(false);

                    // Dzierżawy (host wlaczony) - paczka zakresow, jak przy
                    // zwyklym wyszukiwaniu po nazwie.
                    List<List<string>> chunks = DhcpClient.Partition(scopes, DhcpClient.MaxParallelScopes);
                    List<Task<List<DhcpLease>>> tasks = chunks
                        .Select(chunk => QueryScopesByIpChunkAsync(_dhcpIp, ip, chunk, cts.Token))
                        .ToList();
                    List<DhcpLease>[] perChunk = await Task.WhenAll(tasks).ConfigureAwait(false);

                    foreach (DhcpLease lease in perChunk.SelectMany(x => x))
                    {
                        results.Add(new LookupResult
                        {
                            IPAddress = lease.IPAddress,
                            HostName = lease.HostName,
                            MacAddress = lease.MacAddress,
                            Kind = "Dzierżawa",
                            Description = string.Empty
                        });
                    }

                    // Rezerwacje (adres przypisany na stale).
                    List<DhcpReservation> reservations = await QueryReservationsByIpAsync(_dhcpIp, ip, cts.Token).ConfigureAwait(false);
                    foreach (DhcpReservation r in reservations)
                    {
                        results.Add(new LookupResult
                        {
                            IPAddress = r.IPAddress,
                            HostName = r.HostName,
                            MacAddress = string.Empty,
                            Kind = "Rezerwacja",
                            Description = r.Description
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException(
                        $"Zapytanie DHCP przekroczyło limit czasu {LookupTimeoutMs / 1000} sekund. Serwer DHCP: {_dhcpIp}"
                    );
                }
            }

            return results;
        }

        private static async Task<List<DhcpLease>> QueryScopesByIpChunkAsync(string dhcpServer, string ip, List<string> scopeIds, CancellationToken cancel)
        {
            try
            {
                string output = await DhcpClient.RunPowerShellAsync(
                    DhcpClient.BuildScopeLeasesByIpCommand(dhcpServer, scopeIds, ip), 45000, cancel).ConfigureAwait(false);
                return DhcpClient.ParseLeaseLines(output);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new List<DhcpLease>(); // nieudany fragment - pomijamy
            }
        }

        private static async Task<List<DhcpReservation>> QueryReservationsByIpAsync(string dhcpServer, string ip, CancellationToken cancel)
        {
            try
            {
                string output = await DhcpClient.RunPowerShellAsync(
                    DhcpClient.BuildReservationsByIpCommand(dhcpServer, ip), 45000, cancel).ConfigureAwait(false);
                return DhcpClient.ParseReservationLines(output);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Brak rezerwacji to nie blad - brak modul na serwerze tez nie.
                return new List<DhcpReservation>();
            }
        }
    }
}
