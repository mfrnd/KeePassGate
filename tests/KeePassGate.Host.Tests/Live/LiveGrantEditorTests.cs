using System;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for step 13 of the build order: a grant made from the KPG tab of a
    /// real KeePass, kept by that dialog's OK, and still in the file afterwards.
    /// </summary>
    /// <remarks>
    /// This is the one test that closes the loop the whole editor was built around. Everything else
    /// proves the plugin writes into the dialog's own copy of the group's settings. Only this proves
    /// that KeePass then writes that copy back over the group, saves it, and that the rule is in the
    /// `.kdbx` when the process is gone.
    ///
    /// The dialog is closed with **OK**, which is the opposite of every other live test here and is
    /// the point of this one: a rule that a dialog's OK destroys is exactly the failure the working
    /// copy exists to prevent.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveGrantEditorTests
    {
        private const string Profile = "reader";

        private LiveSession _session;

        private bool _tabWasThere;
        private string _scopeOnTheTab;
        private bool _accepted;
        private bool _exited;
        private string _storedDocument;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin();

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
        public void The_access_control_tab_is_on_the_real_group_dialog()
        {
            Assert.That(_tabWasThere, Is.True,
                "A real KeePass showed no " + GrantTab.Text + " tab on its group dialog.");
            Assert.That(_scopeOnTheTab, Is.EqualTo(GrantTab.GroupScope));
        }

        [Test]
        public void A_rule_typed_there_is_in_the_file_after_KeePass_has_gone()
        {
            Assert.That(_accepted, Is.True, "The group dialog was never accepted.");
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly.");

            Assert.That(_storedDocument, Is.Not.Null,
                "The group carries no rules, so the dialog's OK discarded what was typed.");

            var document = AclDocument.Read(_storedDocument);

            Assert.That(document, Is.Not.Null, "What was stored does not read as a document.");
            Assert.That(document.Grant(Profile), Is.Not.Null, "the profile that was typed is not in the file");
            Assert.That(document.Default, Is.EqualTo(AclDefault.Deny), "the switch that was checked is not either");
        }

        [Test]
        public void The_rule_answers_for_a_client_holding_that_profile()
        {
            // Which is the whole reason a group carries one. Asked through the resolver rather than
            // read out of the text, so this is about permission rather than about JSON.
            var group = new PwGroup(true, true, "Invented", PwIcon.Folder);
            group.CustomData.Set(AclDocument.CustomDataKey, _storedDocument);

            var node = KdbxNodes.Of(group);

            // A rule granting nothing, on a node that refuses everything it does not name: the two
            // halves the editor could reach, and both of them answer the same way.
            Assert.That(AclResolver.Permits(node, new[] { Profile }, AclVerb.Read), Is.False);
            Assert.That(AclResolver.Permits(node, new[] { "somebody else" }, AclVerb.List), Is.False,
                "the switch that was checked refuses every profile the node does not name");
        }

        [Test]
        public void The_plugin_logged_nothing_that_needs_looking_at()
        {
            Assert.That(LiveSession.FailuresIn(_pluginLog), Is.Empty);
        }

        private void GrantFromTheTabAndSave()
        {
            var driver = KeePassDriver.Start(_session.KeePass);

            var dialog = driver.OpenGroupDialog();
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

            // OK rather than Escape, deliberately, and it is what this test is about: the dialog
            // writes its own copy of the group's settings back over the group at exactly this
            // moment, and a rule written anywhere else would be destroyed here.
            _accepted = driver.PressButton(dialog, "OK");
            Thread.Sleep(1000);

            driver.Save();
            _exited = driver.ShutDown();

            _storedDocument = _session.KeePass.ReadRootCustomData(AclDocument.CustomDataKey);
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
            // group dialog's OK.
            Keyboard.SendEnter();

            // And Escape out of the rule picker that opens next, leaving the profile granting
            // nothing. The same key would be the group dialog's Cancel with nothing open, which is
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
