using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NarzedziaIP
{
    public sealed class DhcpLease
    {
        public string HostName { get; set; }
        public string IPAddress { get; set; }
        public string MacAddress { get; set; }
    }

    // Rezerwacja DHCP (adres przypisany do komputera na stałe, host moze
    // byc wylaczony). Opis to zwykle wskazanie wlasciciela lub roli.
    public sealed class DhcpReservation
    {
        public string HostName { get; set; }
        public string IPAddress { get; set; }
        public string Description { get; set; }
    }

    // Wynik wyszukiwania wstecznego (po adresie IP): komputer, ktory
    // aktualnie ma ten adres (dzierzawa) albo jest na niego zarezerwowany.
    public sealed class LookupResult
    {
        public string IPAddress { get; set; }
        public string HostName { get; set; }
        public string MacAddress { get; set; }
        public string Kind { get; set; }
        public string Description { get; set; }
    }

    // Zapytania DHCP przez powershell.exe, wydzielone z MainWindow.
    // Czyste parsowanie + cache zakresów są testowalne bez serwera DHCP.
    public static class DhcpClient
    {
        // Wynik sprawdzenia rezerwacji DHCP. Unknown = nie wiadomo (blad/timeout),
        // taki wynik NIE moze trafic do cache - inaczej przejściowy blad zostalby
        // zapamietany jako "brak rezerwacji" na zawsze.
        public enum ReservationStatus
        {
            Unknown = 0,
            Found = 1,
            NotFound = 2
        }

        public const int ScopeCacheMinutes = 10;
        public const int MaxParallelScopes = 4;

        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, Tuple<DateTime, List<string>>> _scopeCache =
            new Dictionary<string, Tuple<DateTime, List<string>>>(StringComparer.OrdinalIgnoreCase);

        // ---- parsowanie (pure) ----

        public static List<string> ParseScopeIds(string output)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(output))
                return result;
            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string id = line.Trim();
                IPAddress addr;
                if (!string.IsNullOrWhiteSpace(id)
                    && IPAddress.TryParse(id, out addr)
                    && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !result.Contains(id))
                {
                    result.Add(id);
                }
            }
            return result;
        }

        public static List<DhcpLease> ParseLeaseLines(string output)
        {
            List<DhcpLease> result = new List<DhcpLease>();
            if (string.IsNullOrWhiteSpace(output))
                return result;
            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
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

        // Parsuje rezerwacje (nazwa \t ip \t opis). Opis moze byc pusty,
        // wiec wymagamy conajmniej dwoch kolumn.
        public static List<DhcpReservation> ParseReservationLines(string output)
        {
            List<DhcpReservation> result = new List<DhcpReservation>();
            if (string.IsNullOrWhiteSpace(output))
                return result;
            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 2)
                    continue;
                result.Add(new DhcpReservation
                {
                    HostName = parts[0].Trim(),
                    IPAddress = parts[1].Trim(),
                    Description = parts.Length > 2 ? parts[2].Trim() : string.Empty
                });
            }
            return result;
        }

        // ---- cache zakresów ----

        public static bool TryGetCachedScopes(string server, TimeSpan maxAge, out List<string> scopes)
        {
            scopes = null;
            if (string.IsNullOrWhiteSpace(server))
                return false;
            lock (_cacheLock)
            {
                Tuple<DateTime, List<string>> entry;
                if (!_scopeCache.TryGetValue(server.Trim(), out entry))
                    return false;
                if (DateTime.UtcNow - entry.Item1 > maxAge)
                    return false;
                scopes = new List<string>(entry.Item2);
                return true;
            }
        }

        public static void StoreScopes(string server, List<string> scopes)
        {
            StoreScopes(server, scopes, DateTime.UtcNow);
        }

        public static void StoreScopes(string server, List<string> scopes, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(server))
                return;
            lock (_cacheLock)
            {
                _scopeCache[server.Trim()] = Tuple.Create(utcNow, new List<string>(scopes ?? new List<string>()));
            }
        }

        public static void ClearScopeCache()
        {
            lock (_cacheLock)
            {
                _scopeCache.Clear();
            }
        }

        // ---- budowa komend (czysty string, testowalny) ----

        public static string EscapePowerShellSingleQuotedString(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        public static string EncodePowerShellCommand(string command)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(command ?? string.Empty);
            return Convert.ToBase64String(bytes);
        }

        public static string BuildScopesCommand(string dhcpServer)
        {
            string safe = EscapePowerShellSingleQuotedString(dhcpServer);
            return "$ErrorActionPreference = 'Stop'\r\n"
                + "Get-DhcpServerv4Scope -ComputerName '" + safe + "' -ErrorAction Stop | "
                + "ForEach-Object { $_.ScopeId.IPAddressToString }\r\n";
        }

        public static string BuildScopeLeasesCommand(string dhcpServer, string scopeId, string hostname)
        {
            return BuildScopeLeasesCommand(dhcpServer, new[] { scopeId }, hostname);
        }

        // Jedna komenda na PACZKĘ zakresów (jeden proces PowerShell na paczkę,
        // nie na zakres!). Proces na zakres zabijał wydajność przy wielu
        // zakresach: start powershell.exe + ładowanie modułu DHCP za każdym
        // razem było droższe niż same zapytania.
        public static string BuildScopeLeasesCommand(string dhcpServer, IEnumerable<string> scopeIds, string hostname)
        {
            string safeServer = EscapePowerShellSingleQuotedString(dhcpServer);
            string safeName = EscapePowerShellSingleQuotedString(hostname);
            List<string> ids = (scopeIds ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => "'" + EscapePowerShellSingleQuotedString(s.Trim()) + "'")
                .ToList();
            return "$ErrorActionPreference = 'Stop'\r\n"
                + "$server = '" + safeServer + "'\r\n"
                + "$name = '" + safeName + "'\r\n"
                + "$scopeIds = @(" + string.Join(", ", ids) + ")\r\n"
                + "foreach ($scopeId in $scopeIds) {\r\n"
                + "try {\r\n"
                + "Get-DhcpServerv4Lease -ComputerName $server -ScopeId $scopeId -ErrorAction Stop |\r\n"
                + "Where-Object { $_.HostName -and $_.HostName -like ($name + '*') } |\r\n"
                + "ForEach-Object {\r\n"
                + "    $ip = $_.IPAddress.IPAddressToString\r\n"
                + "    if ([string]::IsNullOrWhiteSpace($ip)) { $ip = $_.IPAddress.ToString() }\r\n"
                + "    Write-Output ($_.HostName + \"`t\" + $ip + \"`t\" + $_.ClientId)\r\n"
                + "}\r\n"
                + "} catch { }\r\n"
                + "}\r\n";
        }

        // Wyszukiwanie WSTECZNE: dzierżawy dla konkretnego adresu IP.
        // Ten sam format wyjscia co BuildScopeLeasesCommand, tylko filtr
        // po adresie zamiast po nazwie hosta.
        public static string BuildScopeLeasesByIpCommand(string dhcpServer, IEnumerable<string> scopeIds, string ip)
        {
            string safeServer = EscapePowerShellSingleQuotedString(dhcpServer);
            string safeIp = EscapePowerShellSingleQuotedString(ip);
            List<string> ids = (scopeIds ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => "'" + EscapePowerShellSingleQuotedString(s.Trim()) + "'")
                .ToList();
            return "$ErrorActionPreference = 'Stop'\r\n"
                + "$server = '" + safeServer + "'\r\n"
                + "$targetIP = '" + safeIp + "'\r\n"
                + "$scopeIds = @(" + string.Join(", ", ids) + ")\r\n"
                + "foreach ($scopeId in $scopeIds) {\r\n"
                + "try {\r\n"
                + "Get-DhcpServerv4Lease -ComputerName $server -ScopeId $scopeId -ErrorAction Stop |\r\n"
                + "Where-Object { $_.IPAddress -and $_.IPAddress.IPAddressToString -eq $targetIP } |\r\n"
                + "ForEach-Object {\r\n"
                + "    $ip = $_.IPAddress.IPAddressToString\r\n"
                + "    if ([string]::IsNullOrWhiteSpace($ip)) { $ip = $_.IPAddress.ToString() }\r\n"
                + "    Write-Output ($_.HostName + \"`t\" + $ip + \"`t\" + $_.ClientId)\r\n"
                + "}\r\n"
                + "} catch { }\r\n"
                + "}\r\n";
        }

        // Rezerwacje DHCP dla konkretnego adresu IP (adres moze byc
        // zarezerwowany, a host wylaczony - wtedy nie ma dzierżawy, ale
        // rezerwacja wskazuje komputera). Format: nazwa \t ip \t opis.
        public static string BuildReservationsByIpCommand(string dhcpServer, string ip)
        {
            string safeServer = EscapePowerShellSingleQuotedString(dhcpServer);
            string safeIp = EscapePowerShellSingleQuotedString(ip);
            return "$ErrorActionPreference = 'Stop'\r\n"
                + "$server = '" + safeServer + "'\r\n"
                + "$targetIP = '" + safeIp + "'\r\n"
                + "Get-DhcpServerv4Reservation -ComputerName $server -ErrorAction Stop |\r\n"
                + "Where-Object { $_.IPAddress -and $_.IPAddress.IPAddressToString -eq $targetIP } |\r\n"
                + "ForEach-Object {\r\n"
                + "    $ip = $_.IPAddress.IPAddressToString\r\n"
                + "    if ([string]::IsNullOrWhiteSpace($ip)) { $ip = $_.IPAddress.ToString() }\r\n"
                + "    $name = $_.HostName\r\n"
                + "    if ([string]::IsNullOrWhiteSpace($name)) { $name = '' }\r\n"
                + "    $desc = $_.Description\r\n"
                + "    if ($null -eq $desc) { $desc = '' }\r\n"
                + "    Write-Output ($name + \"`t\" + $ip + \"`t\" + $desc)\r\n"
                + "}\r\n";
        }

        // Rezerwacja DHCP dla konkretnego IP. Skrypt wypisuje 'FOUND' albo
        // 'NOTFOUND' - czytelne rozpoznanie bez polegania na kod wyjscia.
        public static string BuildReservationCommand(string dhcpServer, string ip)
        {            string safeServer = EscapePowerShellSingleQuotedString(dhcpServer);
            string safeIp = EscapePowerShellSingleQuotedString(ip);
            return "$ErrorActionPreference = 'Stop'\r\n"
                + "$server = '" + safeServer + "'\r\n"
                + "$targetIP = '" + safeIp + "'\r\n"
                + "$scopes = Get-DhcpServerv4Scope -ComputerName $server\r\n"
                + "foreach ($scope in $scopes) {\r\n"
                + "try {\r\n"
                + "$res = Get-DhcpServerv4Reservation -ComputerName $server -ScopeId $scope.ScopeId -ErrorAction Stop |\r\n"
                + "       Where-Object { $_.IPAddress.IPAddressToString -eq $targetIP }\r\n"
                + "if ($res) {\r\n"
                + "Write-Output 'FOUND'\r\n"
                + "return\r\n"
                + "}\r\n"
                + "} catch { }\r\n"
                + "}\r\n"
                + "Write-Output 'NOTFOUND'\r\n";
        }

        // Parsuje output skryptu rezerwacji. Porownanie DOKLADNE (linia po
        // linii), bo "NOTFOUND" zawiera w sobie "FOUND" - Contains() daloby
        // falszywe trafienie.
        public static ReservationStatus ParseReservationStatus(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return ReservationStatus.Unknown;

            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string t = line.Trim();
                if (t.Equals("FOUND", StringComparison.OrdinalIgnoreCase))
                    return ReservationStatus.Found;
                if (t.Equals("NOTFOUND", StringComparison.OrdinalIgnoreCase))
                    return ReservationStatus.NotFound;
            }
            return ReservationStatus.Unknown;
        }

        // Dzielenie zakresów na co najwyżej maxParts paczek (kolejność zachowana).
        public static List<List<T>> Partition<T>(List<T> items, int maxParts)
        {
            List<List<T>> result = new List<List<T>>();
            if (items == null || items.Count == 0 || maxParts <= 0)
                return result;
            int parts = Math.Min(maxParts, items.Count);
            int size = (items.Count + parts - 1) / parts;
            for (int i = 0; i < items.Count; i += size)
                result.Add(items.GetRange(i, Math.Min(size, items.Count - i)));
            return result;
        }

        // ---- wykonanie ----

        public static Task<string> RunPowerShellAsync(string psCommand, int timeoutMs)
        {
            return RunPowerShellAsync(psCommand, timeoutMs, CancellationToken.None);
        }

        public static async Task<string> RunPowerShellAsync(string psCommand, int timeoutMs, CancellationToken cancel)
        {
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

                // Anulowanie: rejestracja w tokenie (dispose w `using`) zamiast
                // Task.Delay(Timeout.Infinite, cancel) - tamten zostawial
                // niezdjeta rejestracje w tokenie przy kazdym zapytaniu.
                TaskCompletionSource<bool> cancelled = new TaskCompletionSource<bool>();
                using (cancel.Register(() => cancelled.TrySetResult(true)))
                {
                    Task<bool> waitTask = Task.Run(() =>
                    {
                        try { return process.WaitForExit(timeoutMs <= 0 ? 30000 : timeoutMs); }
                        catch (Exception err)
                        {
                            AppLog.Error(err);
                            return false;
                        }
                    });

                    Task done = await Task.WhenAny(waitTask, cancelled.Task).ConfigureAwait(false);
                    if (done != waitTask || !waitTask.Result)
                    {
                        try { process.Kill(); }
                        catch (Exception err) { AppLog.Error(err); }
                        cancel.ThrowIfCancellationRequested();
                        throw new TimeoutException(
                            "Przekroczono limit czasu zapytania PowerShell (" + (timeoutMs <= 0 ? 30000 : timeoutMs) / 1000 + " s).");
                    }
                }

                string output = await outputTask.ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new Exception(
                        "PowerShell DHCP query failed." + Environment.NewLine + Environment.NewLine + error);
                }

                return output;
            }
        }
    }
}
