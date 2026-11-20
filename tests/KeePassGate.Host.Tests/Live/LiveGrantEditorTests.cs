using System;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Acl;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The end to end test of the grant editor: a grant made from the KPG tab of a real KeePass,
    /// written into the plugin's store by that dialog's OK, and still in the file afterwards.
    /// </summary>
    /// <remarks>
    /// This is the test that closes the loop the editor is built around: the plugin writes the rule
    /// as the dialog closes with OK, KeePass saves the database the write marked, and the rule is in
    /// the `.kdbx` when the process is gone.
    ///
    /// The dialog is closed with **OK**, which is the point of this test: any other close throws
    /// the tab's changes away. The other live tests close a dialog with Escape unless OK is what
    /// keeps something they need.
    ///
    /// **On the entry dialog, with nothing of the entry changed**, because there KeePass marks
    /// nothing at OK: the mark the save needs is the store's own. On the group dialog KeePass marks
    /// the database at every OK, so a write that marked nothing would be saved all the same.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveGrantEditorTests
    {
        private const string Profile = "reader";

        /// <summary>What the plugin logs once the rules changed on a node's tab are written and the database is marked.</summary>
        private const string WrittenLine = " were written, and KeePass now has changes to save";

        /// <summary>How long the write at the dialog's close is waited for.</summary>
        private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

        /// <summary>How long KeePass is given to draw the mark of an unsaved change in its title.</summary>
        private static readonly TimeSpan TitleTimeout = TimeSpan.FromSeconds(5);

        private LiveSession _session;

        private bool _tabWasThere;
        private string _scopeOnTheTab;
        private bool _accepted;
        private bool _written;
        private string _titleAfterOk;
        private bool _exited;
        private string _node;
        private string _root;
        private NodeRule _stored;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin();

            // An entry to open: a database fresh from the session holds none.
            _session.KeePass.FillWithInventedContent();

            // Before KeePass opens the file, because the editor writes rules for profiles this
            // database names and offers no way to invent one. Naming it here is the same thing the
            // Profiles page does, done to a file rather than through a window.
            _session.KeePass.NameProfiles(Profile);

            GrantFromTheTabAndSave();

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
        public void The_access_control_tab_is_on_the_real_entry_dialog()
        {
            Assert.That(_tabWasThere, Is.True,
                "A real KeePass showed no " + GrantTab.Text + " tab on its entry dialog.");
            Assert.That(_scopeOnTheTab, Is.EqualTo(GrantTab.EntryScope));
        }

        [Test]
        public void The_OK_marks_the_database_with_nothing_of_the_entry_changed()
        {
            Assert.That(_accepted, Is.True, "The entry dialog was never accepted.");
            Assert.That(_titleAfterOk, Does.Contain("*"),
                "KeePass's title shows no unsaved change after the OK, so the store's write did not mark the database.");
        }

        [Test]
        public void A_rule_made_there_is_in_the_file_after_KeePass_has_gone()
        {
            Assert.That(_accepted, Is.True, "The entry dialog was never accepted.");
            Assert.That(_written, Is.True, "The plugin never logged the rules as written after the dialog's OK.");
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly.");

            Assert.That(_node, Is.Not.Null,
                "The store holds no rule for the profile at any node, so the dialog's OK did not write the rule made on it.");
            Assert.That(_node, Is.Not.EqualTo(_root), "the rule is at the root rather than at the entry");
            Assert.That(_stored.Profiles, Does.Contain(Profile), "the profile that was chosen is not in the file");
            Assert.That(_stored.Switch.Refuses, Is.True, "the box that was ticked is not either");
        }

        [Test]
        public void The_rule_answers_for_a_client_holding_that_profile()
        {
            // Which is the whole reason a node carries a rule: read as the walk reads a node's
            // rules, so this is about permission rather than about the records.
            Assert.That(_stored.Row(Profile).Answers, Is.True);
            Assert.That(_stored.Row(Profile).Grants, Is.Empty, "the rule chosen grants nothing");
            Assert.That(_stored.Row("somebody-else").Versions, Is.Empty);
            Assert.That(_stored.Switch.Refuses, Is.True,
                "the box that was ticked refuses every profile the node does not name");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private void GrantFromTheTabAndSave()
        {
            var driver = KeePassDriver.Start(_session.KeePass);

            var dialog = driver.OpenEntryDialog();
            var tab = SelectTheAccessTab(dialog);

            _tabWasThere = tab != null;
            if (!_tabWasThere)
            {
                driver.CloseDialog(dialog);
                driver.ShutDown();
                return;
            }

            _scopeOnTheTab = Uia.Name(
                Uia.ById(dialog, ControlType.Text, GrantTab.ScopeLabelName));

            MakeTheRule(dialog);

            // OK rather than Escape, deliberately, and it is what this test is about: the rule is
            // written into the plugin's store as the dialog closes with OK, and at no other close.
            _accepted = driver.PressButton(dialog, "OK");

            // Saved only once the plugin's log holds the line it writes when the write has landed
            // and marked the database: a save before that would save a database the write has not
            // reached yet, and the title read before it would show no mark.
            _written = _accepted && Logged(WrittenLine, WriteTimeout);

            // KeePass draws its title on its own thread after the write: read until the mark
            // shows or the time is up, and keep what was read last for the assertion.
            var titleDeadline = DateTime.UtcNow + TitleTimeout;
            do
            {
                _titleAfterOk = driver.MainWindowTitle;
                if (_titleAfterOk != null && _titleAfterOk.Contains("*"))
                {
                    break;
                }

                Thread.Sleep(250);
            }
            while (DateTime.UtcNow < titleDeadline);

            driver.Save();
            _exited = driver.ShutDown();

            var read = _session.KeePass.ReadRules();
            _root = read.Root;
            _node = read.Rules.Named(Profile).SingleOrDefault();
            _stored = _node == null ? NodeRule.None : read.Rules.At(_node);
        }

        /// <summary>
        /// Whether the plugin's log holds this text before the time is up, read again every
        /// quarter of a second, as the other live tests wait for a state the plugin reports.
        /// </summary>
        private bool Logged(string marker, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_session.KeePass.ReadPluginLog().IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return false;
        }

        private static AutomationElement SelectTheAccessTab(AutomationElement dialog)
        {
            var tab = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.TabItem, name => name == GrantTab.Text),
                TimeSpan.FromSeconds(10));

            if (tab == null)
            {
                return null;
            }

            object pattern;
            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                ((SelectionItemPattern)pattern).Select();
                Thread.Sleep(700);
            }

            return tab;
        }

        /// <summary>
        /// Makes a rule the way a person does: click the empty line, choose the profile, decline to
        /// say what it may do, then check the switch that refuses everything else.
        /// </summary>
        /// <remarks>
        /// **With a real pointer and real keystrokes**, because nothing on this table can be typed
        /// into. Neither column takes a value: a mask spelled by hand is a rule nobody meant, and a
        /// profile spelled by hand is a rule for a name that may not exist, which grants nobody
        /// anything and reads exactly like one that works. Both are chosen from a list, and a list
        /// is not something the accessibility tree can be asked to operate.
        ///
        /// So: one click on the empty line opens the profiles this database names, Enter takes the
        /// one that is highlighted, and the rule picker opens straight after it because choosing a
        /// profile is half a decision. Escape leaves that half undone, which is deliberate: a rule
        /// granting nothing is one of the two states worth proving survives the dialog's OK.
        ///
        /// The seal is toggled through the tree, since a check box is a thing the tree can work.
        /// </remarks>
        private static void MakeTheRule(AutomationElement dialog)
        {
            var grid = Uia.WaitFor(
                "the grant table",
                () => Uia.ById(dialog, ControlType.Table, GrantTab.GridControlName));

            // The only Profile cell on an empty table is the empty line's, which is the one to
            // press: the whole of what it does is offer the profiles that have no rule here yet.
            var cell = Uia.WaitFor(
                "the Profile cell",
                () => Uia.Descendant(grid, ControlType.Edit,
                    name => name != null && name.StartsWith("Profile", StringComparison.Ordinal)));

            Keyboard.Front(dialog);
            Mouse.Click(cell);

            // One profile is named in this database, so it is the one highlighted and Enter takes
            // it. The picker owns Enter while it is open, which is what keeps this from being the
            // entry dialog's OK.
            Keyboard.SendEnter();

            // And Escape out of the rule picker that opens next, leaving the profile granting
            // nothing. The same key would be the entry dialog's Cancel with nothing open, which is
            // why the picker takes that one away as well.
            Keyboard.SendEscape();

            var seal = Uia.WaitFor(
                "the seal check box",
                () => Uia.ById(dialog, ControlType.CheckBox, GrantTab.SealCheckBoxName));

            object toggle;
            Assert.That(seal.TryGetCurrentPattern(TogglePattern.Pattern, out toggle), Is.True,
                "The seal check box cannot be toggled.");

            ((TogglePattern)toggle).Toggle();
            Thread.Sleep(400);
        }
    }
}
