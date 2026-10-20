using System;
using System.Collections.Generic;
using System.Text;

namespace NarzedziaIP
{
    public enum ManualBlockKind
    {
        Heading,
        Paragraph,
        BulletList,
        NumberedList
    }

    // Pojedynczy fragment tekstu z formatowaniem (pogrubienie / kursywa / kod).
    public sealed class ManualSpan
    {
        public string Text { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Code { get; set; }

        public override string ToString()
        {
            return (Bold ? "**" : "") + (Italic ? "*" : "") + (Code ? "`" : "") + Text
                 + (Code ? "`" : "") + (Italic ? "*" : "") + (Bold ? "**" : "");
        }
    }

    // Blok instrukcji: naglowek, akapit albo lista.
    public sealed class ManualBlock
    {
        public ManualBlockKind Kind { get; set; }
        public int Level { get; set; }
        public List<ManualSpan> Spans { get; set; }
        public List<List<ManualSpan>> Items { get; set; }

        public static ManualBlock Heading(int level, List<ManualSpan> spans)
        {
            return new ManualBlock { Kind = ManualBlockKind.Heading, Level = level, Spans = spans };
        }

        public static ManualBlock Paragraph(List<ManualSpan> spans)
        {
            return new ManualBlock { Kind = ManualBlockKind.Paragraph, Spans = spans };
        }

        public static ManualBlock List(ManualBlockKind kind, List<List<ManualSpan>> items)
        {
            return new ManualBlock { Kind = kind, Items = items };
        }
    }

    // Parser Markdown (podzbior: naglowki, akapity, listy, pogrubienie,
    // kursywa, kod w nawiasie wstecznym). Czysta logika - bez WPF,
    // dzieki czemu testowalna bez okna.
    public static class ManualParser
    {
        public static List<ManualBlock> Parse(string markdown)
        {
            List<ManualBlock> blocks = new List<ManualBlock>();
            if (string.IsNullOrEmpty(markdown))
                return blocks;

            string[] lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            List<string> paragraph = new List<string>();
            ManualBlock list = null;

            Action flushParagraph = () =>
            {
                if (paragraph.Count == 0)
                    return;
                string joined = string.Join(" ", paragraph.ToArray()).Trim();
                paragraph.Clear();
                if (joined.Length > 0)
                    blocks.Add(ManualBlock.Paragraph(ParseInline(joined)));
            };

            foreach (string raw in lines)
            {
                string line = (raw ?? string.Empty).Trim();

                if (line.Length == 0)
                {
                    flushParagraph();
                    list = null;
                    continue;
                }

                // Naglowek: 1-3 znaczki '#' + spacja
                int hashes = 0;
                while (hashes < line.Length && line[hashes] == '#')
                    hashes++;
                if (hashes >= 1 && hashes <= 3 && hashes < line.Length && line[hashes] == ' ')
                {
                    flushParagraph();
                    list = null;
                    string title = line.Substring(hashes + 1).Trim();
                    blocks.Add(ManualBlock.Heading(hashes, ParseInline(title)));
                    continue;
                }

                // Lista punktowana: "- "
                if (line.Length > 2 && line[0] == '-' && line[1] == ' ')
                {
                    flushParagraph();
                    if (list == null || list.Kind != ManualBlockKind.BulletList)
                    {
                        list = ManualBlock.List(ManualBlockKind.BulletList, new List<List<ManualSpan>>());
                        blocks.Add(list);
                    }
                    list.Items.Add(ParseInline(line.Substring(2).Trim()));
                    continue;
                }

                // Lista numerowana: "1. " (maks 3 cyfry)
                int dot = line.IndexOf(". ");
                if (dot >= 1 && dot <= 3 && IsAllDigits(line.Substring(0, dot)))
                {
                    flushParagraph();
                    if (list == null || list.Kind != ManualBlockKind.NumberedList)
                    {
                        list = ManualBlock.List(ManualBlockKind.NumberedList, new List<List<ManualSpan>>());
                        blocks.Add(list);
                    }
                    list.Items.Add(ParseInline(line.Substring(dot + 2).Trim()));
                    continue;
                }

                // Akapit - kolejne linie doklejane
                list = null;
                paragraph.Add(line);
            }

            flushParagraph();
            return blocks;
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            foreach (char c in s)
            {
                if (c < '0' || c > '9')
                    return false;
            }
            return true;
        }

        // Formatowanie inline: **pogrubienie**, *kursywa*, `kod`.
        // Formaty moga sie zagniezdzac.
        public static List<ManualSpan> ParseInline(string text)
        {
            List<ManualSpan> spans = new List<ManualSpan>();
            ParseInline(text, false, false, false, spans);
            return spans;
        }

        private static void ParseInline(string text, bool bold, bool italic, bool code, List<ManualSpan> spans)
        {
            if (string.IsNullOrEmpty(text))
                return;

            StringBuilder current = new StringBuilder();

            Action flush = () =>
            {
                if (current.Length == 0)
                    return;
                spans.Add(new ManualSpan { Text = current.ToString(), Bold = bold, Italic = italic, Code = code });
                current.Length = 0;
            };

            int i = 0;
            while (i < text.Length)
            {
                // Escape: \* \* \` \\ -> literalny znak
                if (text[i] == '\\' && i + 1 < text.Length
                    && (text[i + 1] == '*' || text[i + 1] == '`' || text[i + 1] == '\\'))
                {
                    current.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (!code && text[i] == '`')
                {
                    int end = FindUnescaped(text, "`", i + 1);
                    if (end > i)
                    {
                        flush();
                        ParseInline(text.Substring(i + 1, end - i - 1), bold, italic, true, spans);
                        i = end + 1;
                        continue;
                    }
                }

                if (!code && !bold && text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = FindUnescaped(text, "**", i + 2);
                    if (end > i)
                    {
                        int segEnd = end;
                        int next = end + 2;
                        if (next < text.Length && text[next] == '*')
                        {
                            // "***" na koncu: kursywa zamyka sie pierwsza gwiazdka,
                            // pogrubienie dwa ostatnie ("**a *b***").
                            segEnd = end + 1;
                            next = end + 3;
                        }
                        flush();
                        ParseInline(text.Substring(i + 2, segEnd - (i + 2)), true, italic, false, spans);
                        i = next;
                        continue;
                    }
                }

                if (!code && text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
                {
                    int end = FindUnescaped(text, "*", i + 1);
                    if (end > i)
                    {
                        flush();
                        ParseInline(text.Substring(i + 1, end - i - 1), bold, true, false, spans);
                        i = end + 1;
                        continue;
                    }
                }

                current.Append(text[i]);
                i++;
            }

            flush();
        }

        // Znajdje token, pomijajac wystapienia escapowane (poprzedzone '\').
        private static int FindUnescaped(string text, string token, int from)
        {
            int i = from;
            while (i < text.Length)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    i += 2;
                    continue;
                }
                if (i + token.Length <= text.Length
                    && string.CompareOrdinal(text, i, token, 0, token.Length) == 0)
                    return i;
                i++;
            }
            return -1;
        }
    }
}
