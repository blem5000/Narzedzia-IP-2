using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NarzedziaIP
{
    // Podział wklejonej listy IP na poprawne i odrzucone (zakładka ACL).
    // Odrzucone są zgłaszane użytkownikowi zamiast cichego gubienia.
    public static class AclInput
    {
        public sealed class SplitResult
        {
            public List<string> Valid = new List<string>();
            public List<string> Rejected = new List<string>();
        }

        public static SplitResult SplitIps(string text)
        {
            SplitResult result = new SplitResult();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            string[] parts = text.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> seenValid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenRejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in parts)
            {
                string v = raw.Trim();
                if (string.IsNullOrEmpty(v))
                    continue;
                IPAddress addr;
                if (IPAddress.TryParse(v, out addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (seenValid.Add(v))
                        result.Valid.Add(v);
                }
                else
                {
                    if (seenRejected.Add(v))
                        result.Rejected.Add(v);
                }
            }
            return result;
        }
    }
}
