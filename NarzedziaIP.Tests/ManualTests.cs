using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class ManualParserTests
    {
        [TestMethod]
        public void EmptyInputGivesNoBlocks()
        {
            Assert.AreEqual(0, ManualParser.Parse(null).Count);
            Assert.AreEqual(0, ManualParser.Parse("").Count);
            Assert.AreEqual(0, ManualParser.Parse("   \n  \n").Count);
        }

        [TestMethod]
        public void Headings()
        {
            List<ManualBlock> blocks = ManualParser.Parse("# Tytul\n\n## Sekcja\n\n### Podsekcja\n");
            Assert.AreEqual(3, blocks.Count);
            Assert.AreEqual(ManualBlockKind.Heading, blocks[0].Kind);
            Assert.AreEqual(1, blocks[0].Level);
            Assert.AreEqual("Tytul", blocks[0].Spans[0].Text);
            Assert.AreEqual(2, blocks[1].Level);
            Assert.AreEqual(3, blocks[2].Level);
        }

        [TestMethod]
        public void Paragraphs()
        {
            List<ManualBlock> blocks = ManualParser.Parse("Pierwszy akapit.\nDrugie zdanie.\n\nDrugi akapit.\n");
            Assert.AreEqual(2, blocks.Count);
            Assert.AreEqual(ManualBlockKind.Paragraph, blocks[0].Kind);
            Assert.AreEqual("Pierwszy akapit. Drugie zdanie.", blocks[0].Spans[0].Text);
            Assert.AreEqual("Drugi akapit.", blocks[1].Spans[0].Text);
        }

        [TestMethod]
        public void BulletList()
        {
            List<ManualBlock> blocks = ManualParser.Parse("- jeden\n- dwa\n- trzy\n");
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(ManualBlockKind.BulletList, blocks[0].Kind);
            Assert.AreEqual(3, blocks[0].Items.Count);
            Assert.AreEqual("jeden", blocks[0].Items[0][0].Text);
            Assert.AreEqual("trzy", blocks[0].Items[2][0].Text);
        }

        [TestMethod]
        public void NumberedList()
        {
            List<ManualBlock> blocks = ManualParser.Parse("1. pierwszy\n2. drugi\n");
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual(ManualBlockKind.NumberedList, blocks[0].Kind);
            Assert.AreEqual(2, blocks[0].Items.Count);
            Assert.AreEqual("pierwszy", blocks[0].Items[0][0].Text);
        }

        [TestMethod]
        public void InlineBoldItalicCode()
        {
            List<ManualSpan> spans = ManualParser.ParseInline("zwykly **pogrubiony** *kursywa* `kod` koniec");

            string all = "";
            foreach (ManualSpan s in spans)
                all += s.Text;
            Assert.AreEqual("zwykly pogrubiony kursywa kod koniec", all);

            ManualSpan bold = null;
            ManualSpan italic = null;
            ManualSpan code = null;
            foreach (ManualSpan s in spans)
            {
                if (s.Text == "pogrubiony") bold = s;
                if (s.Text == "kursywa") italic = s;
                if (s.Text == "kod") code = s;
            }
            Assert.IsNotNull(bold);
            Assert.IsTrue(bold.Bold);
            Assert.IsNotNull(italic);
            Assert.IsTrue(italic.Italic);
            Assert.IsNotNull(code);
            Assert.IsTrue(code.Code);
        }

        [TestMethod]
        public void EscapedAsteriskIsLiteral()
        {
            // "*.old" musi zostac tekstem, a nie kursywa.
            // Markery formatowania znikaja - zostaje sama tresc.
            List<ManualSpan> spans = ManualParser.ParseInline(@"na bok (*\*.old*)");
            string all = "";
            foreach (ManualSpan s in spans)
                all += s.Text;
            Assert.AreEqual("na bok (*.old)", all);
        }

        [TestMethod]
        public void EscapedAsteriskOutsideFormatting()
        {
            // "\*" to literalna gwiazdka (backslash jest znakiem escapowania).
            // Literalny backslash zapisujemy jako "\\".
            List<ManualSpan> spans = ManualParser.ParseInline(@"c:\katalog\*\nazwa");
            string all = "";
            foreach (ManualSpan s in spans)
                all += s.Text;
            Assert.AreEqual(@"c:\katalog*\nazwa", all);

            List<ManualSpan> spans2 = ManualParser.ParseInline(@"c:\\katalog");
            string all2 = "";
            foreach (ManualSpan s in spans2)
                all2 += s.Text;
            Assert.AreEqual(@"c:\katalog", all2);
        }

        [TestMethod]
        public void NestedFormatting()
        {
            // kursywa wewnatrz pogrubienia
            List<ManualSpan> spans = ManualParser.ParseInline("**pogrubiony i *kursywa* tekst**");
            string all = "";
            foreach (ManualSpan s in spans)
                all += s.Text;
            Assert.AreEqual("pogrubiony i kursywa tekst", all);

            // wersja z kursywa na samym koncu ("***")
            List<ManualSpan> spans2 = ManualParser.ParseInline("**pogrubiony i *kursywa***");
            string all2 = "";
            foreach (ManualSpan s in spans2)
                all2 += s.Text;
            Assert.AreEqual("pogrubiony i kursywa", all2);
        }

        [TestMethod]
        public void UnclosedMarkerIsLiteralText()
        {
            List<ManualSpan> spans = ManualParser.ParseInline("gwiazdka * bez zamkniecia");
            string all = "";
            foreach (ManualSpan s in spans)
                all += s.Text;
            Assert.AreEqual("gwiazdka * bez zamkniecia", all);
        }
    }

    [TestClass]
    public class ManualLoaderTests
    {
        [TestMethod]
        public void EmbeddedVersionExists()
        {
            string text = ManualLoader.LoadEmbedded();
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), "instrukcja wbudowana nie istnieje");
            Assert.IsTrue(text.Contains("#"), "instrukcja powinna byc w Markdown");
            Assert.IsTrue(text.Contains("ManualParser") == false, "tresc nie powinna zawierac nazw klas");
        }

        [TestMethod]
        public void ReadFileUtf8HandlesBom()
        {
            string dir = Path.Combine(Path.GetTempPath(), "narz_manual_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "Instrukcja.md");
                File.WriteAllText(path, "# Test\n", new System.Text.UTF8Encoding(true));
                string text = ManualLoader.ReadFileUtf8(path);
                Assert.IsTrue(text.StartsWith("# Test"), "BOM nie moze zostac w tekscie");
            }
            finally
            {
                try { Directory.Delete(dir, true); }
                catch { }
            }
        }

        [TestMethod]
        public void FindFileReturnsNullForMissing()
        {
            Assert.IsNull(ManualLoader.FindFile("nie-ma-takiego-pliku-12345.md"));
        }

        [TestMethod]
        public void LoadFallsBackToEmbeddedWhenNoOverride()
        {
            // w katalogu testowym nie ma plikow nadpisujacych
            string text = ManualLoader.Load();
            Assert.IsFalse(string.IsNullOrWhiteSpace(text));
        }
    }
}
