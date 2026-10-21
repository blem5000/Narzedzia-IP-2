using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    // Symulacja sciezki aktualizacji na ZBUDOWANEJ paczce:
    // rozpakowanie -> walidacja -> podmiana plikow -> sprawdzenie
    // nowej wersji. Wychwytuje bledy pakowania, ktore psulyby instalacje
    // u uzytkownikow.
    //
    // W release.yml paczka jest budowana PRZED uruchomieniem testow, wiec
    // ten test zawsze widzi ja. Przy zwyklym "dotnet test" bez paczki test
    // jest nieokreslony (Inconclusive), a nie bledny.
    [TestClass]
    public class ReleasePackageTests
    {
        private const string PackageRelativePath = @"dist\NarzedziaIP2-windows.zip";

        private static string FindPackage()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            // ...\NarzedziaIP.Tests\bin\Release\  ->  repo root
            for (int i = 0; i < 5 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                string candidate = Path.Combine(dir, PackageRelativePath);
                if (File.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        [TestMethod]
        public void BuiltPackageInstallsCleanly()
        {
            string package = FindPackage();
            if (package == null)
            {
                Assert.Inconclusive("Brak zbudowanej paczki (" + PackageRelativePath + ") - test dziala w release.yml.");
            }

            string tmp = Path.Combine(Path.GetTempPath(), "narz_pkg_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                // 1. rozpakowanie
                string staging = Path.Combine(tmp, "staging");
                Updater.ExtractZip(package, staging, null);
                string source = Updater.ResolveSource(staging, "Narzedzia IP 2.exe");
                Assert.IsTrue(Directory.Exists(source), "nie znaleziono katalogu zrodlowego paczki");

                // 2. walidacja (ta sama, ktora wykonuje instalator)
                Updater.ValidatePackageFiles(source, "Narzedzia IP 2.exe");

                // 3. zawartosc paczki - dokladnie te pliki, ktore program
                //    wykorzystuje. Kazdy nowy plik musi miec rozszerzenie
                //    dopuszczalne przez STARY updater (patrz
                //    PackagePassesValidationOfOlderUpdaters).
                List<string> files = new List<string>();
                foreach (string f in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    files.Add(Path.GetFileName(f));
                files.Sort(StringComparer.OrdinalIgnoreCase);

                CollectionAssert.AreEquivalent(
                    new List<string> { "Instrukcja.txt", "Narzedzia IP 2.exe", "Narzedzia IP 2.exe.config", "NarzedziaIP.Updater.exe" },
                    files,
                    "zawartosc paczki sie zmienila - sprawdz czy nowy plik przechodzi walidacje starego updatera");

                // 4. podmiana plikow w symulowanej instalacji
                string install = Path.Combine(tmp, "install");
                Directory.CreateDirectory(install);
                Updater.CopyTree(source, install, Updater.PreservedFiles, null, 1);

                string newExe = Path.Combine(install, "Narzedzia IP 2.exe");
                Assert.IsTrue(File.Exists(newExe), "po instalacji brak glownego exe");
                Assert.IsTrue(File.Exists(Path.Combine(install, "Instrukcja.txt")),
                    "po instalacji brak instrukcji");
                Assert.IsTrue(File.Exists(Path.Combine(install, "NarzedziaIP.Updater.exe")),
                    "po instalacji brak updatera");

                // 5. nowa wersja w zainstalowanym pliku = wersja z paczki
                string installedVersion = Updater.InstalledVersion(newExe);
                string packagedVersion = Updater.InstalledVersion(Path.Combine(source, "Narzedzia IP 2.exe"));
                Assert.AreEqual(packagedVersion, installedVersion, "zainstalowana wersja rozni sie od wersji w paczce");
                Assert.IsFalse(string.IsNullOrWhiteSpace(installedVersion), "zainstalowany plik nie ma wersji");
            }
            finally
            {
                try { Directory.Delete(tmp, true); }
                catch { }
            }
        }
    }
}
