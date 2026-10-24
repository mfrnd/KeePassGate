using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassGate.Testing;
using KeePassGate;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Collections;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The grant editor against KeePass's real group and entry dialogs.
    /// </summary>
    /// <remarks>
    /// `GrantSheetTests` and `GrantPanelTests` settle what the editor decides. This settles the one
    /// thing only KeePass can answer, and it is the thing the whole design turns on: **a rule typed
    /// here lands in the copy the dialog writes back on OK**, not on the live node, so the dialog's
    /// own buttons mean what they say.
    ///
    /// The dialogs are constructed and read, never shown. `Show` throws inside the message pump
    /// outside a running KeePass, and WinForms answers with a dialog that blocks the run until
    /// something kills it. Pressing OK for real belongs to the tier below this one.
    /// </remarks>
    [TestFixture]
    public sealed class GrantEditorInRealDialogsTests
    {
        private TemporaryDatabase _database;
        private ImageList _icons;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            // The entry dialog refuses a null one, and what is in it is never looked at here.
            _icons = new ImageList();
        }

        [TearDown]
        public void TearDown()
        {
            _icons.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_group_dialog_is_read_as_the_group_it_is_about()
        {
            var group = GroupUnderRoot("Fictitious");

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                var node = NodeDialog.Of(dialog, null);

                Assert.That(node, Is.Not.Null);
                Assert.That(node.Group, Is.SameAs(group));
                Assert.That(node.Entry, Is.Null);
                Assert.That(node.HasNode, Is.True);
                Assert.That(node.ReadOnlyReason, Is.Null);
                Assert.That(node.Scope, Is.EqualTo(GrantTab.GroupScope));
            }
        }

        [Test]
        public void Several_entries_edited_together_get_a_tab_that_cannot_write()
        {
            // The worst thing this editor could do. KeePass merges the selection into one
            // synthetic entry, replacing every value they disagree about with a cue string, and
            // writes the result back over all of them. That write removes any key the merged copy
            // no longer carries. Read as a document, that cue says the rules are unreadable, which
            // is false of every entry in the selection, and the tab's answer to unreadable is a
            // button that discards them.
            var group = GroupUnderRoot("Fictitious");
            var first = EntryIn(group);
            var second = EntryIn(group);
            first.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));
            second.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("editor", "U"));

            // Built by reflection because the constructor is internal to KeePass. That is
            // the point of the test rather than an obstacle to it: this is a shape only
            // KeePass can make, so only KeePass can say whether the guard still matches it.
            var merged = new object[] { new[] { first, second }, _database.Database, null };
            var context = typeof(PwEntryForm).Assembly
                .GetType("KeePass.Util.MultipleValues.MultipleValuesEntryContext")
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0]
                .Invoke(merged);

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(
                    (PwEntry)merged[2], PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                typeof(PwEntryForm)
                    .GetProperty("MultipleValuesEntryContext")
                    .SetValue(dialog, context, null);

                var node = NodeDialog.Of(dialog, null);

                Assert.That(node, Is.Not.Null);
                Assert.That(node.ReadOnlyReason, Is.Not.Null.And.Contains("Several entries"));
                Assert.That(node.Store(), Is.Null,
                    "no store, or the tab would offer to discard rules it must not touch");
                Assert.That(new NodeDialogs(null).SheetFor(dialog).Rows.Any(row => row.Local), Is.False,
                    "the merge is no entry's rules, so none are shown as this one's");
            }
        }

        [Test]
        public void A_history_revision_gets_a_tab_that_cannot_write()
        {
            // KeePass returns from SaveEntry on its first instruction in this mode, so anything
            // written here would be thrown away without a word. And the entry behind the dialog
            // is the old revision, so the rules shown are not the ones in force.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.ViewReadOnlyEntry, _database.Database, _icons, false, false);

                var node = NodeDialog.Of(dialog, null);

                Assert.That(node, Is.Not.Null);
                Assert.That(node.ReadOnlyReason, Is.Not.Null.And.Contains("earlier version"));
                Assert.That(node.Store(), Is.Null);
            }
        }

        [Test]
        public void A_history_revision_shows_the_rules_it_had_then()
        {
            // The tab says these are the rules the revision had, and an operator reads it before
            // restoring one. Built from the store alone, it showed only what the group grants.
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "RLP"));
            var entry = EntryIn(group);
            entry.CustomData.Set(AclDocument.CustomDataKey, Rules.Sealing("editor", "U"));
            entry.CreateBackup(_database.Database);
            entry.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("auditor", "L"));
            var revision = entry.History.GetAt(0);

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(revision, PwEditMode.ViewReadOnlyEntry, _database.Database, _icons, false, false);

                var sheet = new NodeDialogs(null).SheetFor(dialog);

                Assert.That(sheet.CanEdit, Is.False);
                Assert.That(sheet.Default, Is.EqualTo(AclDefault.Deny), "the revision's own seal");
                Assert.That(sheet.Rows.Single(row => row.Local).Profile, Is.EqualTo("editor"),
                    "the revision's rule, and not the one in force today");
            }
        }

        [Test]
        public void A_dialog_whose_copy_cannot_be_reached_still_shows_the_rules()
        {
            // A KeePass that renamed the dialog's copy. The tab says the rules can be read but
            // not changed, so they have to be there: a table showing only the inherited rows
            // reads as a group granting nothing of its own while it hands out passwords.
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "RLP"));

            using (var dialog = GroupFormWithoutACopy(group))
            {
                var node = NodeDialog.Of(dialog, null);
                Assume.That(node.Store(), Is.Null);
                Assume.That(node.ReadOnlyReason, Does.Contain("can be read but not changed"));

                var sheet = new NodeDialogs(null).SheetFor(dialog);

                Assert.That(sheet.CanEdit, Is.False);
                Assert.That(sheet.Rows.Single(row => row.Local).Mask, Is.EqualTo(".R...L.P"));
            }
        }

        /// <summary>
        /// A form KeePass's group dialog in name and in its node fields, with no working copy.
        /// </summary>
        /// <remarks>
        /// Emitted rather than declared, because a class of that full name in this assembly would
        /// shadow KeePass's own `GroupForm` for every other test in it.
        /// </remarks>
        private Form GroupFormWithoutACopy(PwGroup group)
        {
            var module = AppDomain.CurrentDomain
                .DefineDynamicAssembly(
                    new AssemblyName("GroupFormWithoutACopy"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
                .DefineDynamicModule("GroupFormWithoutACopy");
            var type = module.DefineType(
                KeePassContract.GroupFormTypeName, TypeAttributes.Public | TypeAttributes.Class, typeof(Form));
            type.DefineField(KeePassContract.GroupFormGroupField, typeof(PwGroup), FieldAttributes.Private);
            type.DefineField(KeePassContract.NodeFormDatabaseField, typeof(PwDatabase), FieldAttributes.Private);
            type.DefineDefaultConstructor(MethodAttributes.Public);

            var built = type.CreateType();
            var dialog = (Form)Activator.CreateInstance(built);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            built.GetField(KeePassContract.GroupFormGroupField, flags).SetValue(dialog, group);
            built.GetField(KeePassContract.NodeFormDatabaseField, flags).SetValue(dialog, _database.Database);
            return dialog;
        }

        [TestCase(KeePassContract.EntryFormMultipleField)]
        [TestCase(KeePassContract.EntryFormEditModeField)]
        public void An_entry_dialog_that_cannot_say_whether_a_write_would_be_kept_cannot_write(string missing)
        {
            // A KeePass that renamed the merged selection or the mode. Read as a field holding
            // nothing, either one was a single entry as it stands, and the tab wrote into the copy
            // of a merged selection, whose OK deletes whatever it no longer carries from every
            // entry in it, or of a revision KeePass keeps nothing of.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            entry.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            using (var dialog = EntryFormWithout(missing, entry))
            {
                var node = NodeDialog.Of(dialog, null);

                Assert.That(node, Is.Not.Null);
                Assert.That(node.ReadOnlyReason, Does.Contain("can be read but not changed"));
                Assert.That(node.Store(), Is.Null);
                Assert.That(new NodeDialogs(null).SheetFor(dialog).Rows.Single(row => row.Local).Mask,
                    Is.EqualTo(".R......"), "and the rules are still shown");
            }
        }

        /// <summary>
        /// A form KeePass's entry dialog in name and in its fields, less the one named.
        /// </summary>
        /// <remarks>
        /// Emitted for the reason <see cref="GroupFormWithoutACopy"/> is. What it stands for is a
        /// KeePass that renamed whichever field is left out.
        /// </remarks>
        private Form EntryFormWithout(string missing, PwEntry entry)
        {
            var module = AppDomain.CurrentDomain
                .DefineDynamicAssembly(
                    new AssemblyName("EntryFormWithout"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
                .DefineDynamicModule("EntryFormWithout");
            var type = module.DefineType(
                KeePassContract.EntryFormTypeName, TypeAttributes.Public | TypeAttributes.Class, typeof(Form));
            type.DefineField(KeePassContract.EntryFormEntryField, typeof(PwEntry), FieldAttributes.Private);
            type.DefineField(KeePassContract.NodeFormDatabaseField, typeof(PwDatabase), FieldAttributes.Private);
            type.DefineField(KeePassContract.NodeFormCustomDataField, typeof(StringDictionaryEx), FieldAttributes.Private);

            if (missing != KeePassContract.EntryFormMultipleField)
            {
                type.DefineField(KeePassContract.EntryFormMultipleField, typeof(object), FieldAttributes.Private);
            }

            if (missing != KeePassContract.EntryFormEditModeField)
            {
                type.DefineField(KeePassContract.EntryFormEditModeField, typeof(PwEditMode), FieldAttributes.Private);
            }

            type.DefineDefaultConstructor(MethodAttributes.Public);

            var built = type.CreateType();
            var dialog = (Form)Activator.CreateInstance(built);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            built.GetField(KeePassContract.EntryFormEntryField, flags).SetValue(dialog, entry);
            built.GetField(KeePassContract.NodeFormDatabaseField, flags).SetValue(dialog, _database.Database);
            built.GetField(KeePassContract.NodeFormCustomDataField, flags).SetValue(dialog, entry.CustomData.CloneDeep());

            var mode = built.GetField(KeePassContract.EntryFormEditModeField, flags);
            if (mode != null)
            {
                mode.SetValue(dialog, PwEditMode.EditExistingEntry);
            }

            return dialog;
        }

        [Test]
        public void A_dialog_that_does_not_say_which_database_it_belongs_to_says_why_it_cannot_write()
        {
            // No database is no list of profiles, and a rule is started by choosing one. The tab
            // was left writable with nothing to choose from, and the pick list's one sentence for
            // that said this database names no profiles, which is false of the database and sends
            // the operator to the wrong page.
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            using (var dialog = GroupFormWithoutADatabase(group))
            {
                var node = NodeDialog.Of(dialog, null);

                Assert.That(node.ReadOnlyReason, Does.Contain("which database"));
                Assert.That(node.Store(), Is.Null);
                Assert.That(new NodeDialogs(null).SheetFor(dialog).Rows.Single(row => row.Local).Mask,
                    Is.EqualTo(".R......"), "and the rules are still shown");
            }
        }

        /// <summary>
        /// A form KeePass's group dialog in name, with its group and its copy and no database.
        /// </summary>
        private static Form GroupFormWithoutADatabase(PwGroup group)
        {
            var module = AppDomain.CurrentDomain
                .DefineDynamicAssembly(
                    new AssemblyName("GroupFormWithoutADatabase"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
                .DefineDynamicModule("GroupFormWithoutADatabase");
            var type = module.DefineType(
                KeePassContract.GroupFormTypeName, TypeAttributes.Public | TypeAttributes.Class, typeof(Form));
            type.DefineField(KeePassContract.GroupFormGroupField, typeof(PwGroup), FieldAttributes.Private);
            type.DefineField(KeePassContract.NodeFormCustomDataField, typeof(StringDictionaryEx), FieldAttributes.Private);
            type.DefineDefaultConstructor(MethodAttributes.Public);

            var built = type.CreateType();
            var dialog = (Form)Activator.CreateInstance(built);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            built.GetField(KeePassContract.GroupFormGroupField, flags).SetValue(dialog, group);
            built.GetField(KeePassContract.NodeFormCustomDataField, flags).SetValue(dialog, group.CustomData.CloneDeep());
            return dialog;
        }

        [Test]
        public void A_rule_deleted_on_the_Plugin_Data_tab_is_gone_from_this_one_too()
        {
            // KeePass's Plugin Data tab on the same dialog edits the same copy, and its Delete takes
            // `KPG` out of it. This tab went on showing the rule the dialog's OK was about to drop,
            // and choosing the same rule again wrote nothing. Brought forward, it reads the copy
            // again, and the copy wins.
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);
                var node = NodeDialog.Of(dialog, null);

                using (var panel = new GrantPanel(new NodeDialogs(null).SheetFor(dialog), node.Store(), node.Scope, null))
                {
                    Load(dialog, group.CustomData);
                    Assume.That(panel.Lines, Has.Some.StartsWith("reader"));

                    // What `UIUtil.StrDictListDeleteSel` does to the copy.
                    WorkingCopyOf(dialog).Remove(AclDocument.CustomDataKey);
                    panel.Resync();

                    Assert.That(panel.Lines, Has.None.StartsWith("reader"), "the table shows what OK will keep");

                    Assert.That(panel.SetGrant("reader", ".R......"), Is.Null);
                    Assert.That(panel.Writes, Is.EqualTo(1), "and the same rule chosen again is written");
                    Assert.That(WorkingCopyOf(dialog).Get(AclDocument.CustomDataKey), Does.Contain("reader"));
                }
            }
        }

        [Test]
        public void An_entry_dialog_is_read_as_the_entry_it_is_about()
        {
            var entry = EntryIn(GroupUnderRoot("Fictitious"));

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);

                var node = NodeDialog.Of(dialog, null);

                Assert.That(node, Is.Not.Null);
                Assert.That(node.Entry, Is.SameAs(entry));
                Assert.That(node.Group, Is.Null);
                Assert.That(node.Scope, Is.EqualTo(GrantTab.EntryScope));
            }
        }

        [Test]
        public void The_tab_lands_on_a_real_group_dialog()
        {
            var group = GroupUnderRoot("Fictitious");

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                var injector = new TabPageInjector(
                    new GrantPanelFactory(new NodeDialogs(null), null),
                    null,
                    KeePassContract.NodeFormTabControlName,
                    GrantTab.Text,
                    GrantTab.PageControlName);

                Assert.That(injector.Attach(dialog), Is.True, "the dialog has the tab strip we look for");
                Assert.That(PageOn(dialog), Is.Not.Null);
                Assert.That(PanelOn(dialog), Is.Not.Null);

                injector.DetachAll();
                Assert.That(PageOn(dialog), Is.Null, "and nothing of ours is left behind");
            }
        }

        [Test]
        public void Opening_a_group_dialog_and_changing_nothing_leaves_the_node_and_the_database_alone()
        {
            // The tab is on a dialog whose OK writes its own copy of the node's settings back over
            // the node, so anything this panel touches while merely being looked at is a change the
            // operator did not make and is then asked to save.
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            var written = group.CustomData.Get(AclDocument.CustomDataKey);
            var touched = group.LastModificationTime;
            _database.Database.Modified = false;

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                var injector = Injector();
                Assert.That(injector.Attach(dialog), Is.True);

                var panel = ControlTree.FindByName<GrantPanel>(dialog, GrantTab.PanelControlName);
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R......" }), "it read the rule");
                Assert.That(panel.Writes, Is.Zero, "and wrote nothing back");

                injector.DetachAll();
            }

            Assert.That(group.CustomData.Get(AclDocument.CustomDataKey), Is.EqualTo(written));
            Assert.That(group.LastModificationTime, Is.EqualTo(touched));
            Assert.That(_database.Database.Modified, Is.False,
                "nothing on this plugin's tab asked KeePass to save anything");
        }

        [Test]
        public void Opening_an_entry_dialog_and_changing_nothing_leaves_the_entry_and_the_database_alone()
        {
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            entry.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            var written = entry.CustomData.Get(AclDocument.CustomDataKey);
            var touched = entry.LastModificationTime;
            _database.Database.Modified = false;

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);

                var injector = Injector(KeePassContract.NodeFormTabControlName);
                Assert.That(injector.Attach(dialog), Is.True);

                var panel = ControlTree.FindByName<GrantPanel>(dialog, GrantTab.PanelControlName);
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.Writes, Is.Zero);

                injector.DetachAll();
            }

            Assert.That(entry.CustomData.Get(AclDocument.CustomDataKey), Is.EqualTo(written));
            Assert.That(entry.LastModificationTime, Is.EqualTo(touched));
            Assert.That(_database.Database.Modified, Is.False);
        }

        private static TabPageInjector Injector(string tabControlName = null)
        {
            return new TabPageInjector(
                new GrantPanelFactory(new NodeDialogs(null), null),
                null,
                tabControlName ?? KeePassContract.NodeFormTabControlName,
                GrantTab.Text,
                GrantTab.PageControlName);
        }

        [Test]
        public void The_tab_lands_on_a_real_entry_dialog_too()
        {
            var entry = EntryIn(GroupUnderRoot("Fictitious"));

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);

                var injector = new TabPageInjector(
                    new GrantPanelFactory(new NodeDialogs(null), null),
                    null,
                    KeePassContract.NodeFormTabControlName,
                    GrantTab.Text,
                    GrantTab.PageControlName);

                Assert.That(injector.Attach(dialog), Is.True);
                Assert.That(PanelOn(dialog), Is.Not.Null);

                injector.DetachAll();
            }
        }

        [Test]
        public void A_dialog_that_is_about_neither_is_not_one_of_these()
        {
            using (var other = new Form())
            {
                Assert.That(NodeDialog.Of(other, null), Is.Null);
                Assert.That(NodeDialog.Of(null, null), Is.Null);
            }
        }

        [Test]
        public void A_rule_typed_on_a_group_dialog_lands_in_the_copy_and_not_on_the_group()
        {
            // The measurement the whole editor rests on. A grant written to the live group would
            // survive Cancel and be destroyed by OK, which is the worst way for this to fail.
            var group = GroupUnderRoot("Fictitious");

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);
                Load(dialog, group.CustomData);

                var store = NodeDialog.Of(dialog, null).Store();
                Assert.That(store, Is.Not.Null);

                var rule = Rules.Granting("reader", "R");
                store.Write(rule);

                Assert.That(group.CustomData.Get(AclDocument.CustomDataKey), Is.Null,
                    "the live group is untouched until the dialog is accepted");
                Assert.That(WorkingCopyOf(dialog).Get(AclDocument.CustomDataKey),
                    Is.EqualTo(rule));
            }
        }

        [Test]
        public void A_rule_typed_on_an_entry_dialog_lands_in_the_copy_and_not_on_the_entry()
        {
            // The same measurement on the other dialog, which the group one does not stand for.
            // `PwEntryForm` takes its copy in `InitPropertiesTab` and puts it back in `SaveEntry`,
            // neither of which is where `GroupForm` does it, so a field of the same name is
            // evidence that a copy exists and not that the editor writes into the one this dialog
            // will keep.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                Load(dialog, entry.CustomData);

                var store = NodeDialog.Of(dialog, null).Store();
                Assert.That(store, Is.Not.Null);

                var rule = Rules.Granting("reader", "R");
                store.Write(rule);

                Assert.That(entry.CustomData.Get(AclDocument.CustomDataKey), Is.Null,
                    "the live entry is untouched until the dialog is accepted");
                Assert.That(WorkingCopyOf(dialog).Get(AclDocument.CustomDataKey),
                    Is.EqualTo(rule));
            }
        }

        [Test]
        public void An_entry_tab_built_before_the_dialog_has_copied_anything_still_shows_the_rules()
        {
            // The ordering again, and it is a different one here: the group dialog makes its copy
            // in `OnFormLoad` and the entry dialog in `InitPropertiesTab`. Both run after the
            // window is announced, so the tab is built while the copy is still null either way.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            var rule = Rules.Granting("reader", "R");
            entry.CustomData.Set(AclDocument.CustomDataKey, rule);

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);

                Assert.That(WorkingCopyOf(dialog), Is.Null, "measured, not assumed");
                Assert.That(NodeDialog.Of(dialog, null).Store().Read(), Is.EqualTo(rule));
            }
        }

        [Test]
        public void A_grant_made_through_an_entry_dialog_survives_the_file_it_is_written_to()
        {
            // Accepting is the half a test that only reads cannot reach, and on this dialog it is
            // the destructive one: `SaveEntry` assigns its whole copy over the entry's custom data,
            // so a rule the editor had written anywhere else would be gone here rather than kept.
            // What follows is the half only a real KDBX4 answers, an entry's custom data being a
            // 4.1 field: a rule that never reached the file reads exactly like one that did until
            // the file is opened again.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            var uuid = entry.Uuid;

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                Load(dialog, entry.CustomData);
                NodeDialog.Of(dialog, null).Store().Write(Rules.Sealing("reader", "RL"));

                AcceptAsKeePassWould(dialog, entry);
            }

            _database.SaveCloseAndReopen();

            var reopened = _database.RootGroup.FindEntry(uuid, true);

            Assert.That(reopened, Is.Not.Null);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), new[] { "reader" }, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), new[] { "nobody" }, AclVerb.Read), Is.False);
        }

        [Test]
        public void An_entry_dialog_accepted_with_the_last_rule_gone_leaves_no_key_behind()
        {
            // The other direction through the same assignment. A rule taken off has to reach the
            // entry as an absent key rather than as an empty one, because the resolver reads an
            // empty document as a rule granting nothing from there down, which is not what taking
            // the last rule away means.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));
            entry.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                Load(dialog, entry.CustomData);
                NodeDialog.Of(dialog, null).Store().Write(null);

                AcceptAsKeePassWould(dialog, entry);
            }

            Assert.That(entry.CustomData.Exists(AclDocument.CustomDataKey), Is.False);
        }

        [Test]
        public void A_tab_built_before_the_dialog_has_copied_anything_still_shows_the_rules()
        {
            // The ordering that decides the whole adapter. KeePass makes its copy while the dialog
            // loads and announces the window during that same load, so the tab is built while the
            // copy is still null. Reading from the node is what stops an editor opened on a group
            // with rules from showing none.
            var group = GroupUnderRoot("Fictitious");
            var rule = Rules.Granting("reader", "R");
            group.CustomData.Set(AclDocument.CustomDataKey, rule);

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                Assert.That(WorkingCopyOf(dialog), Is.Null, "measured, not assumed");
                Assert.That(NodeDialog.Of(dialog, null).Store().Read(), Is.EqualTo(rule));
            }
        }

        [Test]
        public void A_rule_already_on_a_group_is_read_back_through_the_dialog()
        {
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "RL"));

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                var sheet = new NodeDialogs(null).SheetFor(dialog);

                Assert.That(sheet, Is.Not.Null);
                Assert.That(sheet.CanEdit, Is.True);
                Assert.That(sheet.Rows.Single().Mask, Is.EqualTo(".R...L.."));
            }
        }

        [Test]
        public void Removing_the_last_rule_takes_the_key_out_of_the_copy()
        {
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "R"));

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);
                Load(dialog, group.CustomData);

                var store = NodeDialog.Of(dialog, null).Store();
                store.Write(null);

                Assert.That(WorkingCopyOf(dialog).Exists(AclDocument.CustomDataKey), Is.False,
                    "no rules is the absent key, so a node that was cleared looks like one that never had any");
            }
        }

        [Test]
        public void An_entry_inherits_from_the_group_it_is_in()
        {
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("reader", "RA"));
            var entry = EntryIn(group);

            using (var dialog = new PwEntryForm())
            {
                dialog.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);

                var sheet = new NodeDialogs(null).SheetFor(dialog);
                var row = sheet.Rows.Single();

                Assert.That(row.Profile, Is.EqualTo("reader"));
                Assert.That(row.Local, Is.False, "shown, but this entry does not own it");
                Assert.That(row.Mask, Is.EqualTo(".R....A."));
            }
        }

        [Test]
        public void The_root_group_inherits_nothing_because_it_is_the_widest_rule_there_is()
        {
            using (var dialog = new GroupForm())
            {
                dialog.InitEx(_database.RootGroup, false, null, _database.Database);

                var node = NodeDialog.Of(dialog, null);

                Assert.That(node.Above, Is.Null);
                Assert.That(new NodeDialogs(null).SheetFor(dialog).Rows.Where(row => !row.Local), Is.Empty);
            }
        }

        [Test]
        public void The_profiles_offered_are_the_ones_this_database_names()
        {
            // From the database's own list, because that is where a profile lives: a name can be
            // written down before any client holds it, and given its rules then.
            Name("reader", "editor");

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(GroupUnderRoot("Fictitious"), false, null, _database.Database);

                var sheet = new NodeDialogs(null).SheetFor(dialog);

                Assert.That(sheet.Offered, Is.EqualTo(new[] { "editor", "reader" }));
            }
        }

        [Test]
        public void A_profile_only_an_admission_record_still_holds_is_not_offered()
        {
            // What a removal on the Profiles page leaves: the list has let the name go and a
            // client's record still holds it. Offering it would let a rule be written for a name
            // that grants nothing, which reads exactly like one that works.
            Name("reader");
            Admit("reader", "ops");

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(GroupUnderRoot("Fictitious"), false, null, _database.Database);

                var sheet = new NodeDialogs(null).SheetFor(dialog);

                Assert.That(sheet.Offered, Is.EqualTo(new[] { "reader" }));
                Assert.That(sheet.Available, Does.Not.Contain("ops"));
            }
        }

        [Test]
        public void A_rule_left_behind_by_a_removed_profile_is_shown_granting_nothing()
        {
            // Removing a profile touches the list and nothing else, so a group keeps its rule for
            // the name. The dialog shows it, marked, because adding the name again brings it back
            // into force and this is the node it would be in force on.
            Name("reader");
            var group = GroupUnderRoot("Fictitious");
            group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting("ops", "R", "reader", "L"));

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);

                var sheet = new NodeDialogs(null).SheetFor(dialog);

                Assert.That(sheet.Rows.Select(row => row.Profile + (row.Named ? "" : " (not named)")),
                    Is.EqualTo(new[] { "ops (not named)", "reader" }));
            }
        }

        [Test]
        public void A_grant_made_through_a_dialog_survives_the_file_it_is_written_to()
        {
            // The dialog's copy is only a copy until KeePass puts it back, which is what pressing
            // OK does and what this stands in for. What it then proves is the half only a real
            // KDBX4 can: the rule is still there after a save, a close and an open.
            var group = GroupUnderRoot("Fictitious");
            var uuid = group.Uuid;

            using (var dialog = new GroupForm())
            {
                dialog.InitEx(group, false, null, _database.Database);
                Load(dialog, group.CustomData);
                NodeDialog.Of(dialog, null).Store().Write(Rules.Sealing("reader", "RL"));

                AcceptAsKeePassWould(dialog, group);
            }

            _database.SaveCloseAndReopen();

            var reopened = _database.RootGroup.FindGroup(uuid, true);

            Assert.That(reopened, Is.Not.Null);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), new[] { "reader" }, AclVerb.List), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), new[] { "nobody" }, AclVerb.Read), Is.False);
        }

        /// <summary>
        /// What KeePass does to a node's custom data when its dialog is accepted, which cannot be
        /// driven here because showing the dialog is what would raise it.
        /// </summary>
        /// <remarks>
        /// **The whole dictionary, not the one key**, because that is what both dialogs do and what
        /// makes the write-back destructive rather than a merge: `GroupForm.OnBtnOK` assigns
        /// `m_sdCustomData` over the group's custom data, and `PwEntryForm.SaveEntry` assigns it
        /// over the entry it was handed. A helper that copied only this plugin's key would pass
        /// whether or not the editor had written into the copy the dialog keeps, which is the one
        /// thing these tests exist to decide. Only the timing is ours, as in `SnapshotHazardTests`,
        /// and the setter is reached by reflection because KeePass keeps it internal rather than
        /// because a plugin would ever want it.
        /// </remarks>
        private static void AcceptAsKeePassWould(Form dialog, PwGroup group)
        {
            WriteBack(typeof(PwGroup), group, WorkingCopyOf(dialog));
        }

        /// <summary>The same, on the dialog whose accept is `SaveEntry` rather than `OnBtnOK`.</summary>
        private static void AcceptAsKeePassWould(Form dialog, PwEntry entry)
        {
            WriteBack(typeof(PwEntry), entry, WorkingCopyOf(dialog));
        }

        private static void WriteBack(Type node, object target, StringDictionaryEx copy)
        {
            node.GetProperty("CustomData")
                .GetSetMethod(true)
                .Invoke(target, new object[] { copy });
        }

        private static TabPage PageOn(Form dialog)
        {
            return ControlTree.FindByName<TabPage>(dialog, GrantTab.PageControlName);
        }

        private static Control PanelOn(Form dialog)
        {
            return ControlTree.FindByName<GrantPanel>(dialog, GrantTab.PanelControlName);
        }

        private static StringDictionaryEx WorkingCopyOf(Form dialog)
        {
            return (StringDictionaryEx)WorkingCopyField(dialog).GetValue(dialog);
        }

        private static FieldInfo WorkingCopyField(Form dialog)
        {
            var field = dialog.GetType().GetField(
                KeePassContract.NodeFormCustomDataField, BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null, "the dialog no longer keeps a copy, which the contract test says too");
            return field;
        }

        /// <summary>
        /// What KeePass does while a dialog loads: takes its own copy of the node's custom data.
        /// Done here because showing the dialog is what would otherwise raise it, and showing one
        /// outside a running KeePass hangs the run.
        /// </summary>
        private static void Load(Form dialog, StringDictionaryEx live)
        {
            WorkingCopyField(dialog).SetValue(dialog, live.CloneDeep());
        }

        /// <summary>Writes these names into the database's own profile list, as the page does.</summary>
        private void Name(params string[] profiles)
        {
            var names = ProfileNames.Empty();
            foreach (var profile in profiles)
            {
                Assume.That(names.Add(profile), Is.Null);
            }

            _database.Database.CustomData.Set(ProfileNames.CustomDataKey, names.Write());
        }

        /// <summary>Admits an invented client into these profiles, the way pairing does.</summary>
        private void Admit(params string[] profiles)
        {
            var store = new CustomDataStore(_database.Database);
            var authority = new AuthorityService(store, null);

            var created = authority.Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            using (var reading = authority.Read())
            {
                var client = new AuthorityIssuer().IssueClient(
                    reading.Material.Authority, "agent-fictitious", profiles);

                try
                {
                    new AdmissionStore(store, null).Write(
                        AdmissionRecord.For(client, System.DateTimeOffset.UtcNow, profiles));
                }
                finally
                {
                    client.Reset();
                }
            }
        }

        private PwGroup GroupUnderRoot(string name)
        {
            var group = new PwGroup(true, true, name, PwIcon.Folder);
            _database.RootGroup.AddGroup(group, true);
            return group;
        }

        private static PwEntry EntryIn(PwGroup group)
        {
            var entry = new PwEntry(true, true);
            group.AddEntry(entry, true);
            return entry;
        }
    }
}
