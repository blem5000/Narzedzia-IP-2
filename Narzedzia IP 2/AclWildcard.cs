using System;
using System.Collections.Generic;
using System.Linq;

namespace NarzedziaIP
{
    // Agregacja adresów IP w bloki CIDR zapisane jako (network, wildcard),
    // jak w zakładce ACL. Czysta logika bez zależności od UI, żeby dało się
    // ją testować poza WPF.
    public static class AclWildcard
    {
        public static uint IPToUint(string ip)
        {
            byte[] bytes = System.Net.IPAddress.Parse(ip).GetAddressBytes();
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static string UintToIP(uint ip)
        {
            byte[] bytes = BitConverter.GetBytes(ip);
            Array.Reverse(bytes);
            return new System.Net.IPAddress(bytes).ToString();
        }

        // Wildcard dla bloku o rozmiarze potęgi dwójki. Poprawny dla
        // dowolnego rozmiaru (wcześniej switch obsługiwał tylko do /25,
        // większe bloki dostawały błędne 0.0.0.0).
        public static string WildcardForSize(uint size)
        {
            int bits = 0;
            uint s = size;
            while (s > 1)
            {
                s >>= 1;
                bits++;
            }
            uint wild = bits == 0 ? 0 : bits >= 32 ? 0xFFFFFFFFu : ((1u << bits) - 1);
            return UintToIP(wild);
        }

        // Czy maska IPv4 jest ciągła (jedynki potem zera)? Np. 255.255.254.0
        // tak, 255.0.255.0 nie.
        public static bool IsContiguousMask(string mask)
        {
            if (string.IsNullOrWhiteSpace(mask))
                return false;
            System.Net.IPAddress addr;
            if (!System.Net.IPAddress.TryParse(mask.Trim(), out addr))
                return false;
            byte[] b = addr.GetAddressBytes();
            if (b.Length != 4)
                return false;
            uint m = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            uint inv = ~m;
            return (inv & (inv + 1)) == 0;
        }

        public static List<(string Network, string Wildcard)> MergeToWildcard(List<string> ips)
        {
            List<uint> ipInts = ips
                .Select(IPToUint)
                .OrderBy(x => x)
                .ToList();

            List<(string Network, string Wildcard)> result = new List<(string, string)>();

            while (ipInts.Count > 0)
            {
                HashSet<uint> set = new HashSet<uint>(ipInts);

                uint start = ipInts[0];
                uint size = 1;

                while (true)
                {
                    uint nextSize = size * 2;

                    if (start % nextSize != 0)
                        break;

                    IEnumerable<uint> range = Enumerable.Range(0, (int)nextSize)
                        .Select(i => start + (uint)i);

                    if (range.All(r => set.Contains(r)))
                        size = nextSize;
                    else
                        break;
                }

                result.Add((UintToIP(start), WildcardForSize(size)));

                ipInts = ipInts
                    .Where(x => x > start + size - 1)
                    .ToList();
            }

            return result;
        }
    }
}
