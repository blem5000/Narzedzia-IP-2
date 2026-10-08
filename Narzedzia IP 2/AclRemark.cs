using System;
using System.Linq;

namespace NarzedziaIP
{
    // Remark z osobą jak w cisco-acl-helper: pojedyncze spacje, max 100
    // znaków; blok zamyka "Osoba (koniec)". Puste = brak remarków.
    public static class AclRemark
    {
        public const int MaxLength = 100;

        public static string CleanOwner(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
                return string.Empty;
            string clean = string.Join(" ", owner
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return clean.Length > MaxLength ? clean.Substring(0, MaxLength) : clean;
        }

        public static string EndMark(string owner)
        {
            string clean = CleanOwner(owner);
            return string.IsNullOrEmpty(clean) ? string.Empty : clean + " (koniec)";
        }
    }
}
