using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace NarzedziaIP
{
    // Zamienia bloki instrukcji (z ManualParser) na dokument WPF.
    // Naglowki, akapity, pogrubienie, kursywa, kod, listy punktowane
    // i numerowane.
    public static class ManualRenderer
    {
        public static FlowDocument ToDocument(List<ManualBlock> blocks)
        {
            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(0);
            doc.FontFamily = new FontFamily("Segoe UI");
            doc.FontSize = 14;

            if (blocks == null)
                return doc;

            foreach (ManualBlock block in blocks)
            {
                if (block == null)
                    continue;

                switch (block.Kind)
                {
                    case ManualBlockKind.Heading:
                        doc.Blocks.Add(Heading(block));
                        break;
                    case ManualBlockKind.Paragraph:
                        doc.Blocks.Add(Paragraph(block.Spans, 0, 0, 8));
                        break;
                    case ManualBlockKind.BulletList:
                        doc.Blocks.Add(BulletList(block.Items));
                        break;
                    case ManualBlockKind.NumberedList:
                        foreach (Paragraph p in NumberedList(block.Items))
                            doc.Blocks.Add(p);
                        break;
                }
            }

            return doc;
        }

        private static Paragraph Heading(ManualBlock block)
        {
            double size = block.Level == 1 ? 20 : (block.Level == 2 ? 16 : 14);
            Paragraph p = Paragraph(block.Spans, 0, block.Level == 1 ? 0 : 12, 6);
            p.FontSize = size;
            p.FontWeight = FontWeights.Bold;
            return p;
        }

        private static Paragraph Paragraph(List<ManualSpan> spans, double left, double top, double bottom)
        {
            Paragraph p = new Paragraph();
            p.Margin = new Thickness(left, top, 0, bottom);
            if (spans != null)
            {
                foreach (ManualSpan s in spans)
                    p.Inlines.Add(Run(s));
            }
            return p;
        }

        private static List BulletList(List<List<ManualSpan>> items)
        {
            List list = new List();
            list.Margin = new Thickness(0, 0, 0, 8);
            if (items != null)
            {
                foreach (List<ManualSpan> item in items)
                {
                    ListItem li = new ListItem();
                    li.Margin = new Thickness(0, 0, 0, 4);
                    Paragraph p = new Paragraph();
                    p.Margin = new Thickness(0, 0, 0, 0);
                    if (item != null)
                    {
                        foreach (ManualSpan s in item)
                            p.Inlines.Add(Run(s));
                    }
                    li.Blocks.Add(p);
                    list.ListItems.Add(li);
                }
            }
            return list;
        }

        // Numerowana lista jako akapity z wcieciem wiszacym - WPF nie ma
        // natywnej listy numerowanej.
        private static IEnumerable<Paragraph> NumberedList(List<List<ManualSpan>> items)
        {
            int n = 1;
            if (items == null)
                yield break;

            foreach (List<ManualSpan> item in items)
            {
                Paragraph p = new Paragraph();
                p.Margin = new Thickness(24, 0, 0, 4);
                p.TextIndent = -24;
                Run num = new Run((n++).ToString() + ".  ");
                num.FontWeight = FontWeights.Bold;
                p.Inlines.Add(num);
                if (item != null)
                {
                    foreach (ManualSpan s in item)
                        p.Inlines.Add(Run(s));
                }
                yield return p;
            }
        }

        private static Run Run(ManualSpan span)
        {
            if (span == null)
                return new Run();

            Run run = new Run(span.Text ?? string.Empty);
            if (span.Bold)
                run.FontWeight = FontWeights.Bold;
            if (span.Italic)
                run.FontStyle = FontStyles.Italic;
            if (span.Code)
                run.FontFamily = new FontFamily("Consolas");
            return run;
        }
    }
}
