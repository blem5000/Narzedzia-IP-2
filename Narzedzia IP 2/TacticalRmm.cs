using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NarzedziaIP
{
    public sealed class TacticalAgent
    {
        public string AgentId { get; set; }
        public string Hostname { get; set; }
        public string Status { get; set; }
        public string LastSeen { get; set; }

        public bool IsOffline
        {
            get { return string.Equals(Status, "offline", StringComparison.OrdinalIgnoreCase); }
        }
    }

    // Integracja z TacticalRMM (wariant 2): resolve hostname -> agent_id
    // przez REST API (klucz X-API-KEY), potem deep-link do dashboardu
    // {dashboard}/takecontrol/{agent_id} - tam technik klika pulpit Mesh.
    // Pulpit Mesh nie zabiera sesji jak RDP (podgląd sesji konsoli).
    public static class TacticalRmm
    {
        public static string NormalizeBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;
            return url.Trim().TrimEnd('/');
        }

        public static string AgentsUrl(string apiUrl)
        {
            string baseUrl = NormalizeBaseUrl(apiUrl);
            if (baseUrl == null)
                return null;
            return baseUrl + "/agents/";
        }

        public static string TakeControlUrl(string dashboardUrl, string agentId)
        {
            string baseUrl = NormalizeBaseUrl(dashboardUrl);
            if (baseUrl == null || string.IsNullOrWhiteSpace(agentId))
                return null;
            return baseUrl + "/takecontrol/" + agentId.Trim();
        }

        public static string ShortName(string hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname))
                return string.Empty;
            string s = hostname.Trim().ToLowerInvariant();
            int dot = s.IndexOf('.');
            return dot < 0 ? s : s.Substring(0, dot);
        }

        // Parsuje odpowiedź GET /agents/ (tablica {agent_id, hostname, ...}).
        // Wpisy bez agent_id pomijane.
        public static List<TacticalAgent> ParseAgentsJson(string json)
        {
            object raw;
            try
            {
                raw = new JavaScriptSerializer().DeserializeObject(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Tactical API: nieprawidłowy JSON: " + ex.Message, ex);
            }
            ArrayList data = raw as ArrayList;
            List<object> items = new List<object>();
            if (data != null)
            {
                foreach (object o in data)
                    items.Add(o);
            }
            else if (raw is object[])
            {
                items.AddRange((object[])raw);
            }
            else if (raw is Array)
            {
                foreach (object o in (Array)raw)
                    items.Add(o);
            }
            else
            {
                throw new InvalidOperationException("Tactical API: oczekiwano tablicy agentów");
            }

            List<TacticalAgent> result = new List<TacticalAgent>();
            foreach (object item in items)
            {
                Dictionary<string, object> a = item as Dictionary<string, object>;
                if (a == null)
                    continue;
                object idObj;
                string id = a.TryGetValue("agent_id", out idObj) && idObj != null
                    ? Convert.ToString(idObj).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                object hostObj;
                string host = a.TryGetValue("hostname", out hostObj) && hostObj != null
                    ? Convert.ToString(hostObj).Trim() : string.Empty;
                object statusObj;
                string status = a.TryGetValue("status", out statusObj) && statusObj != null
                    ? Convert.ToString(statusObj).Trim() : null;
                object seenObj;
                string seen = a.TryGetValue("last_seen", out seenObj) && seenObj != null
                    ? Convert.ToString(seenObj).Trim() : null;
                result.Add(new TacticalAgent { AgentId = id, Hostname = host, Status = status, LastSeen = seen });
            }
            return result;
        }

        // Najpierw dokładne trafienie (case-insensitive), potem krótka nazwa.
        public static TacticalAgent FindByHostname(IEnumerable<TacticalAgent> agents, string hostname)
        {
            if (agents == null || string.IsNullOrWhiteSpace(hostname))
                return null;
            string want = hostname.Trim();
            TacticalAgent exact = agents.FirstOrDefault(a =>
                a != null && string.Equals(a.Hostname, want, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
            string shortWant = ShortName(want);
            if (string.IsNullOrEmpty(shortWant))
                return null;
            return agents.FirstOrDefault(a =>
                a != null && ShortName(a.Hostname) == shortWant);
        }

        public static async Task<List<TacticalAgent>> GetAgentsAsync(string apiUrl, string apiKey, int timeoutSeconds)
        {
            string url = AgentsUrl(apiUrl);
            if (url == null)
                throw new InvalidOperationException("Nie ustawiono adresu API TacticalRMM (zakładka Ustawienia).");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Nie ustawiono klucza API TacticalRMM (zakładka Ustawienia).");

            using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds <= 0 ? 15 : timeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("NarzedziaIP2-tactical");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.DefaultRequestHeaders.Add("X-API-KEY", apiKey.Trim());

                System.Net.Http.HttpResponseMessage resp;
                try
                {
                    resp = await client.GetAsync(url).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Tactical API: błąd połączenia: " + ex.Message, ex);
                }
                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                        throw new InvalidOperationException("Tactical API: odmowa dostępu - sprawdź klucz API i uprawnienia.");
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException("Tactical API: błąd HTTP " + (int)resp.StatusCode + " " + resp.ReasonPhrase);
                    string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return ParseAgentsJson(json);
                }
            }
        }

        public static async Task<TacticalAgent> FindAgentAsync(string apiUrl, string apiKey, string hostname, int timeoutSeconds)
        {
            List<TacticalAgent> agents = await GetAgentsCachedAsync(apiUrl, apiKey, timeoutSeconds).ConfigureAwait(false);
            return FindByHostname(agents, hostname);
        }

        // Cache listy agentów (auto-sprawdzanie przy każdym szukaniu
        // nie powinno za każdym razem ciągnąć pełnej listy z API).
        public static readonly TimeSpan AgentsCacheAge = TimeSpan.FromMinutes(5);

        private static readonly object _agentsLock = new object();
        private static readonly Dictionary<string, Tuple<DateTime, List<TacticalAgent>>> _agentsCache =
            new Dictionary<string, Tuple<DateTime, List<TacticalAgent>>>(StringComparer.OrdinalIgnoreCase);

        public static bool TryGetCachedAgents(string key, TimeSpan maxAge, out List<TacticalAgent> agents)
        {
            agents = null;
            if (string.IsNullOrWhiteSpace(key))
                return false;
            lock (_agentsLock)
            {
                Tuple<DateTime, List<TacticalAgent>> entry;
                if (!_agentsCache.TryGetValue(key.Trim(), out entry))
                    return false;
                if (DateTime.UtcNow - entry.Item1 > maxAge)
                    return false;
                agents = new List<TacticalAgent>(entry.Item2);
                return true;
            }
        }

        public static void StoreAgents(string key, List<TacticalAgent> agents)
        {
            StoreAgents(key, agents, DateTime.UtcNow);
        }

        public static void StoreAgents(string key, List<TacticalAgent> agents, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;
            lock (_agentsLock)
            {
                _agentsCache[key.Trim()] = Tuple.Create(utcNow, new List<TacticalAgent>(agents ?? new List<TacticalAgent>()));
            }
        }

        public static void ClearAgentsCache()
        {
            lock (_agentsLock)
            {
                _agentsCache.Clear();
            }
        }

        public static async Task<List<TacticalAgent>> GetAgentsCachedAsync(string apiUrl, string apiKey, int timeoutSeconds)
        {
            string key = (apiUrl ?? string.Empty) + "\n" + (apiKey ?? string.Empty);
            List<TacticalAgent> cached;
            if (TryGetCachedAgents(key, AgentsCacheAge, out cached))
                return cached;
            List<TacticalAgent> fresh = await GetAgentsAsync(apiUrl, apiKey, timeoutSeconds).ConfigureAwait(false);
            StoreAgents(key, fresh);
            return fresh;
        }
    }
}
