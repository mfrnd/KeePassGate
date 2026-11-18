using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// A real KeePass setting back a record of its store whose content was deleted in KeePass's
    /// own entry dialog, at the next OK on the KPG tab, `STORE.md` §628.
    /// </summary>
    /// <remarks>
    /// **What no other tier shows**: the edit made the way a person makes it, in the entry
    /// dialog's string list, and the set back made by the plugin's own write at a real settings
    /// dialog's OK, through the writer's guard on KeePass's real thread, saved by KeePass's own
    /// save and read back out of the file after KeePass has exited. The record edited is the
    /// retirement Delete wrote, which after a Delete is the only entry holding the store key, so
    /// the key is found by the entry's history alone until the write sets the entry back.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveStoreSetBackTests
    {
        private LiveSession _session;
        private string _retirement;
        private string _title;
        private string _written;
        private string _headlineBefore;
        private string _headlineAfter;
        private PwEntry _afterExit;
        private StoreState _stateAfterExit;
        private bool _servedAfterExit;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin();
            var keePass = _session.KeePass;

            keePass.ChangeTheStore(database =>
            {
                var service = new AuthorityService(Store(database), new CustomDataStore(database), NullGateLog.Instance);
                Assume.That(service.Create().Succeeded, Is.True);
                Assume.That(service.Delete().Succeeded, Is.True);

                var retirement = database.RootGroup.GetEntries(true)
                    .Single(entry => entry.Strings.ReadSafe(StoreFormat.KindField) == StoreKinds.Retired);
                _retirement = retirement.Uuid.ToHexString();
                _title = retirement.Strings.ReadSafe(PwDefs.TitleField);
                _written = retirement.Strings.ReadSafe(StoreFormat.DataField);
            });

            var driver = KeePassDriver.Start(keePass);

            DeleteTheContentInTheEntryDialog(driver);

            var dialog = driver.OpenDatabaseSettings();
            Assert.That(driver.SelectGateTab(dialog), Is.Not.Null, "The KPG tab is missing.");
            _headlineBefore = driver.TextStartingWith(dialog, "Every authority ");

            Assert.That(driver.PressButton(dialog, GateTab.CreateButtonText), Is.True, driver.Diagnostics);
            _headlineAfter = driver.TextStartingWith(dialog, "This database ");
            Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);

            driver.Save();
            _exited = driver.ShutDown();
            TestContext.Out.WriteLine(driver.Diagnostics);

            _afterExit = keePass.ReadEntries().Single(entry => entry.Uuid.ToHexString() == _retirement);
            keePass.ChangeTheStore(database =>
            {
                using (var reading = Store(database).Read())
                {
                    _stateAfterExit = reading.State;
                    _servedAfterExit = StoreAuthorities.Served(reading) != null;
                }
            });

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
        public void The_tab_read_the_store_by_the_history_of_its_only_key_carrier()
        {
            Assert.That(_headlineBefore, Is.EqualTo(AuthorityDescription.RetiredHeadline));
            Assert.That(_headlineAfter, Is.EqualTo(AuthorityDescription.PresentHeadline));
        }

        [Test]
        public void The_record_holds_its_content_again_protected_after_the_OK_and_the_save()
        {
            var content = _afterExit.Strings.Get(StoreFormat.DataField);

            Assert.That(content, Is.Not.Null, "the content was not set back");
            Assert.That(content.ReadString(), Is.EqualTo(_written));
            Assert.That(content.IsProtected, Is.True);
        }

        [Test]
        public void The_edit_is_kept_in_the_entry_s_history()
        {
            Assert.That(_afterExit.History.Any(version => version.Strings.Get(StoreFormat.DataField) == null), Is.True);
        }

        [Test]
        public void The_store_reads_whole_and_serves_the_authority_created_under_the_same_key()
        {
            Assert.That(_stateAfterExit, Is.EqualTo(StoreState.Whole));
            Assert.That(_servedAfterExit, Is.True);
        }

        [Test]
        public void KeePass_exited_cleanly_with_nothing_going_wrong()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly.");
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        /// <summary>
        /// The plugin's group chosen in the tree, the retirement in the list, and its content
        /// deleted on the entry dialog's Advanced tab, then OK, as a person does it.
        /// </summary>
        private void DeleteTheContentInTheEntryDialog(KeePassDriver driver)
        {
            Choose(Uia.WaitFor(
                "the plugin's group",
                () => Uia.Descendant(driver.MainWindow, ControlType.TreeItem, name => name == StoreFormat.GroupName)));

            var list = Uia.WaitFor("the entry list", () => Uia.ById(driver.MainWindow, "m_lvEntries"));
            Choose(Uia.WaitFor(
                "the retirement " + _title,
                () => Uia.Descendant(list, ControlType.ListItem, name => name == _title)
                      ?? Uia.Descendant(list, ControlType.DataItem, name => name == _title)));

            var entry = driver.OpenChosenEntryDialog();
            Assert.That(driver.SelectPage(entry, "Advanced"), Is.Not.Null, Uia.DescribeTabs(entry));

            var strings = Uia.WaitFor("the string list", () => Uia.ById(entry, "m_lvStrings"));
            Choose(Uia.WaitFor(
                "the content string",
                () => Uia.Descendant(strings, ControlType.ListItem, name => name == StoreFormat.DataField)
                      ?? Uia.Descendant(strings, ControlType.DataItem, name => name == StoreFormat.DataField)));

            // By its identifier: the entry dialog holds a button captioned Delete for each list on
            // its pages, and the button meant is the string list's.
            var delete = Uia.WaitFor("the string list's Delete button", () => Uia.ById(entry, ControlType.Button, "m_btnStrDelete"));
            ((InvokePattern)delete.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Thread.Sleep(1000);
            Assert.That(driver.AcceptDialog(entry), Is.True, driver.Diagnostics);
        }

        private static void Choose(AutomationElement element)
        {
            object pattern;
            Assert.That(element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern), Is.True, Uia.Name(element));
            ((SelectionItemPattern)pattern).Select();
            Thread.Sleep(400);
        }

        private static KdbxStore Store(PwDatabase database)
        {
            return new KdbxStore(database, GateVersion.Current, NullGateLog.Instance);
        }
    }
}
