using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace NarzedziaIP
{
    // Zrodlo tekstu instrukcji (najwyzszy priorytet pierwszy):
    //
    // 1) "Instrukcja.local.md" / "Instrukcja.local.txt" obok programu - wlasna
    //    wersja (np. wlasne procedury dla Twojej lokalizacji). Te pliki NIE
    //    sa dostarczane z programem, wiec aktualizacje ich nie ruszaja.
    // 2) "Instrukcja.txt" obok programu - wersja dostarczona z programem
    //    (aktualizowana przy kazdej aktualizacji, dobra jako szablon).
    // 3) Wersja wbudowana w exe - gdy program lezy tam, gdzie plikow
    //    nie da sie odczytac (np. katalog programu tylko do odczytu).
    //
    // UWAGA o rozszerzeniu: dostarczana instrukcja ma nazwe ".txt", a nie
    // ".md" - stary updater (do 1.1.32) dopuszcza tylko .dll .config .pdb
    // .xml .txt i odrzucilby paczke z .md ("Paczka zawiera niedozwolone
    // pliki"), przez co aktualizacja by sie nie powiodla.
    //
    // Uszkodzony plik nie wywraca programu - jest wtedy lapany blad
    // i uzywana jest kolejna wersja.
    public static class ManualLoader
    {
        public const string FileName = "Instrukcja.txt";
        public const string LocalFileName = "Instrukcja.local.txt";
        public const string EmbeddedResourceName = "NarzedziaIP.Instrukcja.md";

        // Kolejnosc szukania: pliki uzytkownika, potem dostarczone.
        private static readonly string[] CandidateNames =
        {
            "Instrukcja.local.md",
            "Instrukcja.local.txt",
            "Instrukcja.txt",
            "Instrukcja.md"
        };

        // Sciezka pliku obok programu (null gdy tam go nie ma).
        public static string FindFile(string name)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                    return null;
                string path = Path.Combine(dir, name);
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string OverridePath()
        {
            foreach (string name in CandidateNames)
            {
                string path = FindFile(name);
                if (path != null)
                    return path;
            }
            return null;
        }

        // Zwraca tekst instrukcji. Pusty string, gdy nie da sie wczytac
        // zadnej wersji.
        public static string Load()
        {
            foreach (string name in CandidateNames)
            {
                string path = FindFile(name);
                if (path == null)
                    continue;
                try
                {
                    string text = ReadFileUtf8(path);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
                catch (Exception ex)
                {
                    AppLog.Error("ManualLoader.Load (" + name + ")", ex);
                }
            }
            return LoadEmbedded() ?? string.Empty;
        }

        // Wersja wbudowana w exe.
        public static string LoadEmbedded()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (s == null)
                        return null;
                    using (StreamReader reader = new StreamReader(s, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("ManualLoader.LoadEmbedded", ex);
                return null;
            }
        }

        // Czyta plik jako UTF-8 (z wykryciem BOM). Plik bez BOM-a
        // traktujemy jako UTF-8, nie ANSI - inaczej polskie znaki
        // po zapisie w Notatniku na Windowsie bylyby zepsute.
        public static string ReadFileUtf8(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader reader = new StreamReader(fs, Encoding.UTF8, true))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
