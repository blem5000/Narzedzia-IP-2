using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NarzedziaIP;

namespace NarzedziaIP.Tests
{
    [TestClass]
    public class AppLogTests
    {
        private string _tmp;

        [TestInitialize]
        public void Setup()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "narz_applog_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmp);
        }

        [TestCleanup]
        public void Teardown()
        {
            try { Directory.Delete(_tmp, true); }
            catch { }
        }

        private string Path_(string name)
        {
            return Path.Combine(_tmp, name);
        }

        // StackTrace jest wypelniany dopiero po rzuceniu wyjatku.
        private static Exception Thrown(string message)
        {
            try
            {
                throw new InvalidOperationException(message);
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        [TestMethod]
        public void WritesContextMessageAndStack()
        {
            string path = Path_("bledy.log");
            AppLog.ErrorTo(path, "Test.Kontekst", Thrown("cos sie zepsulo"));

            Assert.IsTrue(File.Exists(path));
            string content = File.ReadAllText(path, Encoding.UTF8);
            StringAssert.Contains(content, "[ERROR] Test.Kontekst");
            StringAssert.Contains(content, "InvalidOperationException: cos sie zepsulo");
            StringAssert.Contains(content, "at NarzedziaIP.Tests.AppLogTests");
        }

        [TestMethod]
        public void WritesPlainMessage()
        {
            string path = Path_("bledy.log");
            AppLog.ErrorTo(path, "Test.Plain", "zwykly komunikat");
            string content = File.ReadAllText(path, Encoding.UTF8);
            StringAssert.Contains(content, "zwykly komunikat");
            StringAssert.Contains(content, "[ERROR] Test.Plain");
        }

        [TestMethod]
        public void ContextFallsBackToStackFrame()
        {
            string path = Path_("bledy.log");
            AppLog.ErrorTo(path, null, Thrown("bez kontekstu"));
            string content = File.ReadAllText(path, Encoding.UTF8);
            StringAssert.Contains(content, "at NarzedziaIP.Tests.AppLogTests");
        }

        [TestMethod]
        public void RepeatsWithinWindowAreSuppressed()
        {
            string path = Path_("bledy.log");
            for (int i = 0; i < 20; i++)
                AppLog.ErrorTo(path, "Test.Duplikat", new Exception("ten sam blad"));

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int entries = 0;
            foreach (string l in lines)
                if (l.Contains("[ERROR]")) entries++;

            Assert.AreEqual(1, entries, "ten sam blad w krotce nie moze zalewać logu");
        }

        [TestMethod]
        public void DifferentErrorsAreAllWritten()
        {
            string path = Path_("bledy.log");
            AppLog.ErrorTo(path, "Test.A", new Exception("blad A"));
            AppLog.ErrorTo(path, "Test.B", new Exception("blad B"));
            AppLog.ErrorTo(path, "Test.C", new Exception("blad C"));

            string content = File.ReadAllText(path, Encoding.UTF8);
            StringAssert.Contains(content, "blad A");
            StringAssert.Contains(content, "blad B");
            StringAssert.Contains(content, "blad C");
        }

        [TestMethod]
        public void LogNeverThrowsOnBadPath()
        {
            // sciezka w nieistniejacej aplikacji - nie moze nic rzucic
            AppLog.ErrorTo("Z:\\nie\\istnieje\\bledy.log", "Test", new Exception("x"));
            AppLog.ErrorTo(null, "Test", new Exception("x"));
            AppLog.ErrorTo("", "Test", (Exception)null);
            AppLog.ErrorTo("", "Test", (string)null);
        }

        [TestMethod]
        public void TrimKeepsLastLines()
        {
            string path = Path_("bledy.log");
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 100; i++)
                sb.AppendLine("linia " + i);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            Assert.IsTrue(AppLog.TrimToMaxLines(path, 10));
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Assert.AreEqual(10, lines.Length);
            Assert.AreEqual("linia 99", lines[9]);
        }

        [TestMethod]
        public void DefaultPathIsInUserProfile()
        {
            string p = AppLog.DefaultPath();
            StringAssert.Contains(p, "NarzedziaIP2");
            StringAssert.EndsWith(p, AppLog.FileName);
            Assert.IsTrue(p.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        }
    }
}
