using System.Net;
using System.Net.Sockets;

namespace NarzedziaIP
{
    // Uruchamianie zdalnego pulpitu (mstsc) jako alternatywa dla MSRA.
    // Czysta logika argumentów, testowana w NarzedziaIP.Tests.
    public static class RemoteConnect
    {
        public static string BuildMstscArguments(string ip)
        {
            if (!IsIPv4(ip))
                return null;
            return "/v:" + ip.Trim();
        }

        public static string BuildMsraArguments(string ip)
        {
            if (!IsIPv4(ip))
                return null;
            return "/offerra " + ip.Trim();
        }

        private static bool IsIPv4(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            IPAddress address;
            return IPAddress.TryParse(value.Trim(), out address)
                && address.AddressFamily == AddressFamily.InterNetwork;
        }
    }
}
