using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;

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
        private string _versionText;
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
                _versionText = keePass.TextStartingWith(dialog, "Version ");
                _statusText = keePass.TextStartingWith(dialog, "This database ");
                _textOnTheTab = Uia.Describe(dialog, ControlType.Text);
            }

            keePass.CloseDialog(dialog);
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
        public void The_tab_reports_the_version_that_was_loaded()
        {
            Assert.That(_versionText, Is.EqualTo("Version " + GateVersion.Current),
                "Text on the dialog was: " + _textOnTheTab);
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

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "KeePassGateLive",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new LiveSession(directory, ThrowawayKeePass.Create(directory, withAuthority));
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

        public void Dispose()
        {
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
