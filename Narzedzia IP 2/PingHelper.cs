using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace NarzedziaIP
{
    // Ping wydzielony z MainWindow, żeby dało się go testować
    // (m.in. 127.0.0.1 odpowiada też na runnerach CI).
    public static class PingHelper
    {
        public static async Task<bool> PingHostAsync(string ipAddress, int timeoutMs)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        PingReply reply = ping.Send(ipAddress, timeoutMs <= 0 ? 1000 : timeoutMs);
                        return reply.Status == IPStatus.Success;
                    }
                }
                catch
                {
                    return false;
                }
            }).ConfigureAwait(false);
        }

        public static Task<bool> PingHostAsync(string ipAddress)
        {
            return PingHostAsync(ipAddress, 1000);
        }
    }
}
