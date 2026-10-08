using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    // Regresja: uruchomienie z katalogu kończącego się "\" i zawierającego
    // spacje (np. "\\gimel\it$\Scripts\skrypty GO\sprawdzenie kompow w dhcp\")
    // rozjeżdżało parsowanie argumentów instalatora (CommandLineToArgvW:
    // backslash przed zamykającym cudzysłowem go escapuje).
    [TestClass]
    public class UpdaterQuotingTests
    {
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        // Dzieli linię komend DOKŁADNIE tak, jak zrobi to startujący updater.
        private static string[] SplitLikeWindows(string argsWithoutExe)
        {
            IntPtr argv = CommandLineToArgvW("updater.exe " + argsWithoutExe, out int argc);
            if (argv == IntPtr.Zero)
                throw new InvalidOperationException("CommandLineToArgvW failed");
            try
            {
                string[] res = new string[argc - 1];
                for (int i = 1; i < argc; i++)
                    res[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                return res;
            }
            finally
            {
                LocalFree(argv);
            }
        }

        [TestMethod]
        public void UncPathWithSpacesAndTrailingSlashRoundtrips()
        {
            string zip = "C:\\Temp\\u.zip";
            string target = "\\\\gimel\\it$\\Scripts\\skrypty GO\\sprawdzenie kompow w dhcp\\";
            string args = Updater.BuildUpdaterArguments(zip, target, "Narzedzia IP 2.exe", 1234, 5678, "v1.1.3");

            string[] tokens = SplitLikeWindows(args);

            string[] expected =
            {
                "--zip", zip,
                "--target", target,
                "--exe", "Narzedzia IP 2.exe",
                "--pid", "1234",
                "--pid-start", "5678",
                "--tag", "v1.1.3"
            };
            CollectionAssert.AreEqual(expected, tokens);
        }

        [TestMethod]
        public void TrailingBackslashIsDoubled()
        {
            string args = Updater.BuildUpdaterArguments("a.zip", "C:\\dir\\", "e.exe", 1, 0, "v1");
            StringAssert.Contains(args, "--target \"C:\\dir\\\\\"");
        }

        [TestMethod]
        public void PlainPathsStayQuoted()
        {
            string args = Updater.BuildUpdaterArguments("C:\\a\\u.zip", "C:\\App", "e.exe", 1, 0, "v1");
            StringAssert.Contains(args, "--zip \"C:\\a\\u.zip\"");
            StringAssert.Contains(args, "--target \"C:\\App\"");
            string[] tokens = SplitLikeWindows(args);
            Assert.AreEqual("C:\\App", tokens[3]);
        }

        [TestMethod]
        public void EmbeddedQuotesAreStripped()
        {
            Assert.AreEqual("\"abc\"", Updater.Quote("a\"b\"c"));
        }
    }
}
