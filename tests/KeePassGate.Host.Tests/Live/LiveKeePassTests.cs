using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for step 4 of the build order, stated as the step states it:
    /// the plugin loads into KeePass, shows a KPG tab on the database settings dialog, and stops
    /// cleanly.
    /// </summary>
    /// <remarks>
    /// It drives a throwaway KeePass through the desktop, so it needs an interactive session and
    /// it takes some seconds. That is why it carries the Live category and is left out of the
    /// default test run. Everything it touches is created under `CommonApplicationData` and deleted
    /// afterwards, including the database.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveKeePassTests
    {
        private LiveSession _session;

        private bool _tabWasFound;
        private string _tabsOnTheDialog;
        private bool _statusWindowOpened;
        private string _textOnTheStatusWindow;
        private string _statusText;
        private string _textOnTheTab;
        private bool _exited;
        private int _exitCode;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin();

            var keePass = KeePassDriver.Start(_session.KeePass);
            var dialog = keePass.OpenDatabaseSettings();

            var tab = keePass.SelectGateTab(dialog);
            _tabWasFound = tab != null;
            _tabsOnTheDialog = Uia.DescribeTabs(dialog);

            if (tab != null)
            {
                _statusText = keePass.TextStartingWith(dialog, "This database ");
                _textOnTheTab = Uia.Describe(dialog, ControlType.Text);
            }

            keePass.CloseDialog(dialog);

            // The plugin's own window, from KeePass's own Tools menu, which is where the listening
            // socket lives. The version is deliberately not repeated there, which is what the test
            // further down this file asserts. Reached without a database dialog in the way, which is the point
            // of it being there.
            var status = keePass.OpenStatusWindow();
            _statusWindowOpened = status != null;
            if (status != null)
            {
                _textOnTheStatusWindow = Uia.Describe(status, ControlType.Text);
                keePass.CloseDialog(status);
            }

            _exited = keePass.ShutDown();
            _exitCode = keePass.ExitCode;
            _pluginLog = _session.KeePass.ReadPluginLog();

            TestContext.Out.WriteLine(keePass.Diagnostics);
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
        public void The_database_settings_dialog_shows_a_KPG_tab()
        {
            Assert.That(_tabWasFound, Is.True, "Tabs on the dialog were: " + _tabsOnTheDialog);
        }

        [Test]
        public void The_tab_sits_after_the_tabs_KeePass_brings()
        {
            Assert.That(_tabsOnTheDialog, Does.EndWith(GateTab.Text));
        }

        [Test]
        public void The_tools_menu_opens_the_plugin_s_own_window()
        {
            Assert.That(_statusWindowOpened, Is.True,
                "KeePass showed no " + GateTab.MenuItemText + " item under Tools, or it opened nothing.");
        }

        [Test]
        public void That_window_does_not_repeat_what_KeePass_already_reports()
        {
            // It used to say which version was loaded. KeePass has a plugins dialog of its own that
            // lists every plugin with its version, which is where somebody checking one looks and
            // where the answer is right for all of them at once.
            Assert.That(_textOnTheStatusWindow, Does.Not.Contain(GateVersion.Current),
                "Text on the status window was: " + _textOnTheStatusWindow);
        }

        [Test]
        public void That_window_says_where_the_gate_is_listening()
        {
            Assert.That(_textOnTheStatusWindow, Does.Contain("127.0.0.1:"),
                "Text on the status window was: " + _textOnTheStatusWindow);
        }

        [Test]
        public void The_tab_reports_the_state_of_the_authority()
        {
            Assert.That(_statusText, Is.EqualTo(AuthorityDescription.AbsentHeadline),
                "Text on the dialog was: " + _textOnTheTab);
        }

        [Test]
        public void The_plugin_recorded_that_it_started_and_decorated_the_dialog()
        {
            Assert.That(_pluginLog, Does.Contain("initializing version " + GateVersion.Current));
            Assert.That(_pluginLog, Does.Contain("attached the " + GateTab.Text + " tab"));
        }

        [Test]
        public void KeePass_shut_down_by_itself_and_reported_success()
        {
            Assert.That(_exited, Is.True, "KeePass was still running when the test gave up.");
            Assert.That(_exitCode, Is.Zero);
        }

        [Test]
        public void The_plugin_stopped_before_KeePass_did()
        {
            Assert.That(_pluginLog, Does.Contain("detached the " + GateTab.Text + " tab"));
            Assert.That(_pluginLog, Does.Contain("stopped"));
        }

        [Test]
        public void Nothing_was_reported_as_a_failure_in_the_whole_session()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }
    }

    /// <summary>
    /// A throwaway KeePass under `CommonApplicationData`, and the plumbing to clean it up. Shared by
    /// the live fixtures so that each one is about what it is testing.
    /// </summary>
    internal sealed class LiveSession : IDisposable
    {
        private readonly string _workingDirectory;
        private readonly ThrowawayKeePass _keePass;

        private LiveSession(string workingDirectory, ThrowawayKeePass keePass)
        {
            _workingDirectory = workingDirectory;
            _keePass = keePass;
        }

        public ThrowawayKeePass KeePass
        {
            get { return _keePass; }
        }

        public static LiveSession Begin()
        {
            return Begin(false);
        }

        /// <summary>With an authority already in the database, for a test about the gate.</summary>
        public static LiveSession Begin(bool withAuthority)
        {
            string reason;
            if (!KeePassDev.MatchesTheAssemblyUnderTest(out reason))
            {
                Assert.Ignore(reason);
            }

            var directory = Path.Combine(Root(), SessionName());
            EndOrphans();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }

            CreatePrivately(directory);
            return new LiveSession(directory, ThrowawayKeePass.Create(directory, withAuthority));
        }

        /// <summary>
        /// Creates the session directory so that only this account, the administrators and the
        /// system can put anything in it or below it.
        /// </summary>
        /// <remarks>
        /// **The common application data directory lets every local account add folders and files
        /// below it**, and what a session puts there is a copy of KeePass that this account then
        /// starts. KeePass loads every assembly beside its program and in `plugins` below it, so
        /// another account that dropped one in between the copy and the start, or while a fixture
        /// restarts KeePass, would have its code run as whoever ran the tests. The data here is
        /// invented, which is not the point: the code is.
        ///
        /// **Created with its own list and inheriting nothing**, so there is no moment when the
        /// directory is open: nothing is in it before the list is. **Checked afterwards**, because
        /// a directory somebody else made at that path in the meantime keeps its own list and
        /// creating it again succeeds without a word. **And a root owned by another account is
        /// refused**, since an owner can put a directory of its own in place of any directory
        /// inside it, whatever that directory's list says.
        /// </remarks>
        private static void CreatePrivately(string directory)
        {
            var root = Root();
            Directory.CreateDirectory(root);

            var owner = Directory.GetAccessControl(root).GetOwner(typeof(SecurityIdentifier));
            if (!IsTrusted(owner))
            {
                Assert.Fail(root + " is owned by " + Named(owner) + ", which can replace anything a " +
                            "session puts in it. Delete it, and run again.");
            }

            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var who in Trusted())
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    who,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            Directory.CreateDirectory(directory, security);

            var created = Directory.GetAccessControl(directory);
            if (!created.AreAccessRulesProtected || !IsTrusted(created.GetOwner(typeof(SecurityIdentifier))))
            {
                Assert.Fail(directory + " was made by somebody else while this session was creating it.");
            }
        }

        /// <summary>This account, the administrators and the system: who may own or change a session.</summary>
        private static SecurityIdentifier[] Trusted()
        {
            return new[]
            {
                WindowsIdentity.GetCurrent().User,
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)
            };
        }

        private static bool IsTrusted(IdentityReference owner)
        {
            return owner != null && Trusted().Contains(owner);
        }

        private static string Named(IdentityReference owner)
        {
            try
            {
                return owner == null ? "nobody" : owner.Translate(typeof(NTAccount)).Value;
            }
            catch (IdentityNotMappedException)
            {
                return owner.Value;
            }
        }

        /// <summary>
        /// The directory this session works in, named after the fixture that asked for it.
        /// </summary>
        /// <remarks>
        /// **A name rather than an identifier, because this path is published.** KeePass puts the
        /// open database's path in the banner of its settings dialog, most of the documentation
        /// screenshots are of that dialog, and they are committed. Anything fresh per run would
        /// mean regenerating them produced a diff whether or not the screen had changed, which is
        /// the one thing that would make the diff worth reading.
        ///
        /// The fixture's own name keeps two of them out of one directory. A leftover from an
        /// interrupted run is deleted rather than worked around: the live tier needs the desktop to
        /// itself, so two sessions of one fixture at once is not a case that exists.
        /// </remarks>
        private static string SessionName()
        {
            var name = TestContext.CurrentContext.Test.ClassName ?? string.Empty;
            var dot = name.LastIndexOf('.');
            var bare = dot < 0 ? name : name.Substring(dot + 1);
            return string.IsNullOrEmpty(bare) ? "session" : bare;
        }

        /// <summary>
        /// Where a session builds its throwaway KeePass, chosen so a picture of it can be published.
        /// </summary>
        /// <remarks>
        /// **Not the temporary directory, and the reason is secrecy rather than tidiness.** KeePass
        /// puts the open database's path in the banner of its settings dialog, and `%TEMP%` on
        /// Windows is inside the user's own profile, so that path carries their account name. The
        /// documentation screenshots are of that dialog and they are committed and published: a
        /// session under `%TEMP%` puts whoever ran the build into a file in the repository.
        ///
        /// The common application data directory is on every Windows, is there before anybody asks
        /// for it, and names nobody.
        /// </remarks>
        private static string Root()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KeePassGateLive");
        }

        /// <summary>
        /// Ends every KeePass still running out of a session directory, which only an interrupted
        /// run leaves behind.
        /// </summary>
        /// <remarks>
        /// **A test host that dies without its tear down leaves its KeePass running**, because
        /// nothing else ever ends one. That KeePass holds its own program and its database open, so
        /// the next session of the same fixture could not delete the directory and failed on a
        /// file rather than on the process, run after run until somebody ended it by hand; and it
        /// kept the documented port, so every fixture after it was quietly given another. Nothing
        /// of the developer's runs from under this root, and the tier needs the desktop to itself,
        /// so whatever is found there is one of these.
        ///
        /// A process whose program cannot be read is not this account's to judge, and is left.
        /// </remarks>
        private static void EndOrphans()
        {
            var root = Root() + Path.DirectorySeparatorChar;

            foreach (var process in Process.GetProcessesByName("KeePass"))
            {
                using (process)
                {
                    try
                    {
                        if (!process.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        process.Kill();
                        process.WaitForExit(10000);
                    }
                    catch (Exception error) when (
                        error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
                    {
                        // Gone already, another account's, or out of reach. None of those holds a
                        // session directory this account made.
                    }
                }
            }
        }

        /// <summary>Every line the plugin logged as a warning or an error, which should be none.</summary>
        public static string[] FailuresIn(string log)
        {
            if (string.IsNullOrEmpty(log))
            {
                return Array.Empty<string>();
            }

            return log
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(" ERROR ") || line.Contains(" WARN ") || line.Contains(" UNEXPECTED "))
                .ToArray();
        }

        /// <summary>
        /// Ends the KeePass, and deletes the session directory unless its fixture failed.
        /// </summary>
        /// <remarks>
        /// **Kept whole when the fixture failed**, because the plugin's log inside it is the only
        /// record of what the plugin did in a process the test does not otherwise hold, and
        /// `build.ps1` prints it with the failures. NUnit runs a fixture's tear down after its one
        /// time set up has thrown, and what a set up printed never reaches the result file, so
        /// deleting here took the one account of every fixture that failed before its tests ran.
        /// The next session of the same fixture deletes the directory as it begins.
        /// </remarks>
        public void Dispose()
        {
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                _keePass.Keep();
                TestContext.Progress.WriteLine("The fixture failed, so " + _workingDirectory + " is kept.");
                return;
            }

            _keePass.Dispose();

            try
            {
                if (Directory.Exists(_workingDirectory))
                {
                    Directory.Delete(_workingDirectory, true);
                }
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException)
            {
                // A KeePass still letting go of its own files holds them either way round:
                // locked reads as one of these and denied as the other. Neither is worth
                // failing a teardown over, and `ThrowawayKeePass` already tolerates both.
            }
        }
    }
}
