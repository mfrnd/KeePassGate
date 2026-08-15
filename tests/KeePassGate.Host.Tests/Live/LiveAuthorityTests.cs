using System;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for step 5 of the build order: an authority created from the
    /// KPG tab of a real KeePass, saved, and still there when KeePass is started again.
    /// </summary>
    /// <remarks>
    /// This is the "believe it only after reopening" discipline as a test. Everything else in the
    /// suite proves that the plugin writes what it means to write. Only this proves that KeePass
    /// keeps it: through the dialog closing, through a save, through the process ending, and
    /// through the file being opened again.
    ///
    /// The settings dialog is closed with OK, which is the half of this test that matters most:
    /// the tab holds its changes until the dialog it is on is accepted, so Escape would leave
    /// nothing behind to survive anything. KeePass writes back a snapshot of custom data when a
    /// group dialog is dismissed with OK, and the host tests measure that the settings dialog
    /// carries no such snapshot, so accepting here cannot destroy what accepting a group dialog
    /// would.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveAuthorityTests
    {
        private LiveSession _session;

        private string _statusAfterCreating;
        private string _fingerprintAfterCreating;
        private string _statusAfterRestarting;
        private string _fingerprintAfterRestarting;
        private string _textOnTheTab;
        private bool _firstRunExited;
        private bool _secondRunExited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin();

            CreateTheAuthorityAndSave();
            ReadItBackAfterRestarting();

            _pluginLog = _session.KeePass.ReadPluginLog();
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
        public void Creating_from_the_tab_gives_the_database_an_authority()
        {
            Assert.That(_statusAfterCreating, Is.EqualTo(AuthorityDescription.PresentHeadline),
                "Text on the tab was: " + _textOnTheTab);
        }

        [Test]
        public void The_tab_names_the_authority_by_its_fingerprint()
        {
            Assert.That(_fingerprintAfterCreating, Is.Not.Null.And.Not.Empty);

            // The value on its own, because the tab lays its facts out as a table: the name is in
            // one label and what it says is in another.
            Assert.That(_fingerprintAfterCreating, Does.Match(@"^[0-9a-f]{16}$"));
        }

        [Test]
        public void The_authority_is_still_there_after_KeePass_has_been_restarted()
        {
            Assert.That(_statusAfterRestarting, Is.EqualTo(AuthorityDescription.PresentHeadline));
        }

        [Test]
        public void It_is_the_same_authority_and_not_a_new_one()
        {
            Assert.That(_fingerprintAfterRestarting, Is.EqualTo(_fingerprintAfterCreating));
        }

        [Test]
        public void Both_KeePass_sessions_shut_down_cleanly()
        {
            Assert.That(_firstRunExited, Is.True, "The first KeePass did not exit.");
            Assert.That(_secondRunExited, Is.True, "The second KeePass did not exit.");
        }

        [Test]
        public void The_plugin_recorded_creating_it_once_and_nothing_going_wrong()
        {
            Assert.That(_pluginLog, Does.Contain("authority created"));
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private void CreateTheAuthorityAndSave()
        {
            var keePass = KeePassDriver.Start(_session.KeePass);
            var dialog = keePass.OpenDatabaseSettings();

            var tab = keePass.SelectGateTab(dialog);
            Assert.That(tab, Is.Not.Null, "The KPG tab is missing, so there is nothing to create from.");

            Assert.That(keePass.PressButton(dialog, GateTab.CreateButtonText), Is.True,
                keePass.Diagnostics);

            _statusAfterCreating = keePass.TextStartingWith(dialog, "This database ");
            _fingerprintAfterCreating = keePass.TextMatching(dialog, "^[0-9a-f]{16}$");
            _textOnTheTab = Uia.Describe(dialog, ControlType.Text);

            // OK rather than Escape, and that is what this half of the test is now about: the tab
            // holds its changes until the dialog it is on is accepted, so Escape here would leave a
            // database with no authority in it and nothing to say why.
            Assert.That(keePass.AcceptDialog(dialog), Is.True, keePass.Diagnostics);

            keePass.Save();
            _firstRunExited = keePass.ShutDown();
        }

        private void ReadItBackAfterRestarting()
        {
            var keePass = KeePassDriver.Start(_session.KeePass);
            var dialog = keePass.OpenDatabaseSettings();

            var tab = keePass.SelectGateTab(dialog);
            Assert.That(tab, Is.Not.Null, "The KPG tab is missing after the restart.");

            _statusAfterRestarting = keePass.TextStartingWith(dialog, "This database ");
            _fingerprintAfterRestarting = keePass.TextMatching(dialog, "^[0-9a-f]{16}$");

            keePass.CloseDialog(dialog);
            _secondRunExited = keePass.ShutDown();
        }
    }
}
