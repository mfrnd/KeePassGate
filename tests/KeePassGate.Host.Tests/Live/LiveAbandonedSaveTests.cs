using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using Microsoft.CSharp;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A write whose save KeePass abandoned before writing, over a file another program rewrote,
    /// and what the main window shows once the change is taken back out.
    /// </summary>
    /// <remarks>
    /// **What only this proves** is the window. `MainForm.SaveDatabase` returns without
    /// `UpdateUIState` when its own look at the file finds it rewritten and the operator cancels,
    /// so the flag the change raised was never drawn, and the put back leaves it up over a file
    /// that is not what KeePass read. The host tier counts the redraw; this reads the title bar.
    ///
    /// **A second plugin is the other program**, compiled here and installed beside the gate. It
    /// appends to the database file from `FileSavingPre`, which KeePass raises after the writer's
    /// own look at the file and before its own, and only once a marker beside the database asks
    /// it to, so nothing at start up is touched. A sync client writing its copy in that gap is
    /// what it stands for, and no timing on the test's side could land in the gap reliably.
    ///
    /// **The file is put back before the session ends**, byte for byte, so that the save the test
    /// makes finds the file KeePass read and goes through without a question, and KeePass exits
    /// without asking about a change nobody wants.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveAbandonedSaveTests
    {
        /// <summary>The marker the second plugin looks for beside the database.</summary>
        private const string Marker = "rewrite-inside-save.marker";

        /// <summary>The second plugin's name, which is its file's, its namespace's and its directory's.</summary>
        private const string SecondPlugin = "RewritesInsideSave";

        /// <summary>The main window's title while the database holds changes nobody has saved.</summary>
        private const string UnsavedMark = "* - ";

        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private static readonly string Source =
            "using System.IO;\r\n" +
            "using System.Reflection;\r\n" +
            "using KeePass.Forms;\r\n" +
            "using KeePass.Plugins;\r\n" +
            "[assembly: AssemblyProduct(\"KeePass Plugin\")]\r\n" +
            "[assembly: AssemblyVersion(\"1.0.0.0\")]\r\n" +
            "namespace " + SecondPlugin + "\r\n" +
            "{\r\n" +
            "    public sealed class " + SecondPlugin + "Ext : Plugin\r\n" +
            "    {\r\n" +
            "        private IPluginHost _host;\r\n" +
            "        public override bool Initialize(IPluginHost host)\r\n" +
            "        {\r\n" +
            "            _host = host;\r\n" +
            "            host.MainWindow.FileSavingPre += OnSaving;\r\n" +
            "            return true;\r\n" +
            "        }\r\n" +
            "        public override void Terminate()\r\n" +
            "        {\r\n" +
            "            _host.MainWindow.FileSavingPre -= OnSaving;\r\n" +
            "        }\r\n" +
            "        private static void OnSaving(object sender, FileSavingEventArgs e)\r\n" +
            "        {\r\n" +
            "            var path = e.Database.IOConnectionInfo.Path;\r\n" +
            "            var marker = Path.Combine(Path.GetDirectoryName(path), \"" + Marker + "\");\r\n" +
            "            if (!File.Exists(marker)) { return; }\r\n" +
            "            File.Delete(marker);\r\n" +
            "            File.AppendAllText(path, \"rewritten by another program\");\r\n" +
            "        }\r\n" +
            "    }\r\n" +
            "}\r\n";

        private LiveSession _session;
        private bool _asked;
        private LiveReply _write;
        private string _titleAfterTheWrite;
        private string _titleAfterSaving;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            keePass.Grant(("agent", "CRL"));
            InstallSecondPlugin(keePass);

            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(admitted, "agent");

                var driver = KeePassDriver.Start(keePass);
                LiveApiTests.WaitUntilServing(keePass, keePass.Process);

                var asRead = File.ReadAllBytes(keePass.DatabasePath);
                var marker = Paths.Join(Path.GetDirectoryName(keePass.DatabasePath), Marker);
                File.WriteAllText(marker, "now");

                var write = Task.Run(() => LiveGateClient.Send(
                    keePass.Port, keePass.NewGateName(), admitted, "POST", "/groups/root/entries",
                    Bodies.Of(("Title", "Invented")), LiveGateClient.OnTheOperator));

                // KeePass's own question about a file that changed since it was read, titled with
                // its name alone where the main window's title names the database as well.
                var question = Uia.WaitForOrNull(
                    () =>
                    {
                        var window = NativeWindows.Find(keePass.Process, title => title == "KeePass");
                        return window == null ? null : AutomationElement.FromHandle(window.Handle);
                    },
                    DialogTimeout);
                _asked = question != null;
                Assert.That(question, Is.Not.Null,
                    "KeePass asked nothing. Windows: " + NativeWindows.Titles(keePass.Process) +
                    Environment.NewLine + keePass.ReadPluginLog());
                Assert.That(driver.PressButton(question, "Cancel"), Is.True, driver.Diagnostics);

                _write = write.Wait(LiveGateClient.OnTheOperator)
                    ? write.Result
                    : new LiveReply { Failure = "no answer in time" };
                _titleAfterTheWrite = WaitForTitle(driver, true);

                File.WriteAllBytes(keePass.DatabasePath, asRead);
                driver.Save();
                _titleAfterSaving = WaitForTitle(driver, false);

                _exited = driver.ShutDown();
                TestContext.Out.WriteLine(driver.Diagnostics);
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
        }

        [Test]
        public void KeePass_asked_about_the_file_another_program_rewrote()
        {
            Assert.That(_asked, Is.True);
        }

        [Test]
        public void The_client_was_told_its_write_was_not_saved()
        {
            Assert.That(_write.Status, Is.EqualTo(500), _write.Failure ?? _write.Body);
            Assert.That(_write.Body, Does.Contain("not saved"));
        }

        [Test]
        public void The_database_was_left_marked_unsaved()
        {
            Assert.That(_pluginLog, Does.Contain("is being left saying it has unsaved changes"));
        }

        [Test]
        public void And_the_title_bar_shows_it()
        {
            Assert.That(_titleAfterTheWrite, Does.Contain(UnsavedMark),
                "KeePass's title bar was '" + _titleAfterTheWrite + "'");
        }

        [Test]
        public void A_save_over_the_file_as_KeePass_read_it_clears_the_mark_and_KeePass_exits()
        {
            Assert.That(_titleAfterSaving, Does.Not.Contain(UnsavedMark),
                "KeePass's title bar was '" + _titleAfterSaving + "'");
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");
        }

        /// <summary>
        /// Compiles the second plugin into a directory of its own under `plugins`, before KeePass
        /// starts and reads that directory.
        /// </summary>
        private static void InstallSecondPlugin(ThrowawayKeePass keePass)
        {
            var directory = Paths.Join(Path.GetDirectoryName(keePass.ExePath), "plugins", SecondPlugin);
            Directory.CreateDirectory(directory);

            // `System.Windows.Forms` for `IPluginHost.MainWindow`, which is a form: the compiler
            // has to be given the assembly of every type a member it reads is declared with.
            var options = new CompilerParameters(
                new[] { "System.dll", "System.Windows.Forms.dll", keePass.ExePath })
            {
                OutputAssembly = Paths.Join(directory, SecondPlugin + ".dll"),
                GenerateInMemory = false,
            };

            using (var compiler = new CSharpCodeProvider())
            {
                var compiled = compiler.CompileAssemblyFromSource(options, Source);
                Assert.That(compiled.Errors.HasErrors, Is.False,
                    string.Join(Environment.NewLine, compiled.Errors.Cast<CompilerError>().Select(e => e.ToString())));
            }
        }

        /// <summary>
        /// KeePass's title bar once it shows what is expected of it, or as it last read.
        /// </summary>
        private static string WaitForTitle(KeePassDriver driver, bool unsaved)
        {
            var deadline = DateTime.UtcNow + DialogTimeout;
            var title = driver.MainWindowTitle;

            while (title.Contains(UnsavedMark) != unsaved && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
                title = driver.MainWindowTitle;
            }

            return title;
        }
    }
}
