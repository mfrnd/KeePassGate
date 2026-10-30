using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The gate that keeps pairing away from a group KeePass is holding a copy of, measured against
    /// the real dialog and the real window notifications.
    /// </summary>
    /// <remarks>
    /// `SnapshotHazardTests` establishes that the hazard is real. This establishes that the plugin
    /// notices it. The dialog is created and initialized but never shown, because what is being
    /// asked is what the watcher can see from the notification, and a window on somebody's desktop
    /// is not needed to answer it.
    ///
    /// Note what is deliberately not counted: how many windows are open. The plugin's own approval
    /// dialog is a window, and it is certain to be open at exactly the moment this check runs.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class GroupDialogsTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private ImageList _icons;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);

            // The entry dialog refuses a null one, and what is in it is never looked at here.
            _icons = new ImageList();

            Assert.That(_dialogs.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _icons.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_watch_that_never_started_refuses_rather_than_counting_nothing()
        {
            // A count is a number and cannot say "I do not know". A watch that never started
            // answers zero exactly as an empty desktop does, which is why nothing refuses on the
            // count: the one guard this design rests on is the last place where silence may be
            // read as permission. `Blocking` folds the question into a sentence and so cannot be
            // misread this way.
            using (var never = new GroupDialogs(new GlobalWindowManagerWindows(), NullGateLog.Instance))
            {
                Assert.That(never.Count, Is.EqualTo(0),
                    "which is the number an empty desktop gives too, which is the whole problem");
                Assert.That(never.Blocking(_database.Database.RootGroup), Is.Not.Null,
                    "the sentence, which says it rather than leaving it to be inferred");
            }

            Assert.That(_dialogs.Blocking(_database.Database.RootGroup), Is.Null,
                "and the fixture's own, which did start, refuses nothing with nothing open");
        }

        [Test]
        public void Every_dialog_it_watches_for_is_in_the_KeePass_it_runs_in()
        {
            // The same names the contract tests pin, asked the way `Start` asks them: of the KeePass
            // that is loaded rather than of the constants.
            Assert.That(GroupDialogs.Watched, Has.Count.EqualTo(6));
            Assert.That(GroupDialogs.Unrecognized(typeof(GroupForm).Assembly, GroupDialogs.Watched, null), Is.Empty);
        }

        [Test]
        public void A_dialog_this_KeePass_does_not_have_leaves_every_question_refusing()
        {
            // A KeePass that renamed one of these would open it under a name nothing compares
            // against: never recorded, so every question answered that nothing was open, the count
            // said none, and the watch said it was running. That is allowing the one write this
            // class exists to refuse, so a name that is not there is a watch that never starts.
            var renamed = new[] { KeePassContract.GroupFormTypeName, "KeePass.Forms.GroupEditForm" };

            using (var windows = new GlobalWindowManagerWindows())
            using (var watch = new GroupDialogs(windows, null, NullGateLog.Instance, renamed))
            {
                Assert.That(watch.Start(), Is.False);
                Assert.That(watch.Blocking(_database.RootGroup), Is.Not.Null);
                Assert.That(watch.Blocking(EntryUnderRoot()), Is.Not.Null);
                Assert.That(watch.BlockingRecords(_database.Database), Is.Not.Null);
                Assert.That(watch.Working(), Is.Not.Null);
            }
        }

        [Test]
        public void Nothing_is_blocked_while_no_group_dialog_is_open()
        {
            Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Null);
            Assert.That(_dialogs.Count, Is.Zero);
        }

        [Test]
        public void A_group_dialog_on_the_root_group_blocks_writing_to_it()
        {
            using (var form = GroupDialogFor(_database.RootGroup))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Not.Null);
                    Assert.That(_dialogs.Blocking(_database.RootGroup), Does.Contain("group dialog is open"));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void Closing_it_unblocks_the_group_again()
        {
            using (var form = GroupDialogFor(_database.RootGroup))
            {
                var registered = true;

                GlobalWindowManager.AddWindow(form);
                try
                {
                    Assume.That(_dialogs.Blocking(_database.RootGroup), Is.Not.Null);

                    // The gesture under test, and the only place this is not simply cleanup.
                    GlobalWindowManager.RemoveWindow(form);
                    registered = false;

                    Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Null);
                    Assert.That(_dialogs.Count, Is.Zero);
                }
                finally
                {
                    // In a `finally` like every other test here. `GlobalWindowManager` is static
                    // and outlives the fixture, so an assertion that fails before the removal above
                    // would leave a disposed form in it for the rest of the process, and every
                    // later test that enumerates it would walk one.
                    if (registered)
                    {
                        GlobalWindowManager.RemoveWindow(form);
                    }
                }
            }
        }

        [Test]
        public void A_group_dialog_on_some_other_group_blocks_nothing_here()
        {
            var other = new PwGroup(true, true, "Somewhere else", PwIcon.Folder);
            _database.RootGroup.AddGroup(other, true);

            using (var form = GroupDialogFor(other))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Null,
                        "only the group being written to matters");
                    Assert.That(_dialogs.Blocking(other), Is.Not.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void Any_other_window_blocks_nothing()
        {
            // The plugin's own approval dialog is one of these, and it is open at exactly the
            // moment a pairing write happens. Counting windows would deadlock every pairing.
            using (var form = new Form())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Null);
                    Assert.That(_dialogs.Count, Is.Zero);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_group_dialog_that_has_not_been_told_its_group_blocks_everything()
        {
            // Fail closed. A dialog whose group cannot be read might be editing this one.
            using (var form = new GroupForm())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Not.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void An_entry_dialog_on_an_entry_blocks_writing_to_it()
        {
            // The entry half of what the group tests above pin. It had no test at all, which is
            // how the bulk case below went unnoticed.
            var entry = EntryUnderRoot();

            using (var form = EntryDialogFor(entry))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(entry), Is.Not.Null);
                    Assert.That(_dialogs.Blocking(entry), Does.Contain("entry dialog is open"));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void An_entry_dialog_that_no_longer_says_whether_it_is_several_is_taken_for_both()
        {
            // A KeePass that renamed the merged selection's field. Read as holding nothing, that
            // took every Edit Entries dialog for one entry's, and the synthetic entry behind it is
            // none of the real ones, so nothing was refused while it was open. It might as well be
            // an Add Entry, which fills a group on OK, so it is taken for that too.
            var first = EntryUnderRoot();
            var second = EntryUnderRoot();

            using (var form = EntryDialogWithoutASelectionField(first))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(first), Is.Not.Null);
                    Assert.That(_dialogs.Blocking(second), Is.Not.Null,
                        "every entry of its database, since which of them it is about cannot be told");
                    Assert.That(_dialogs.BlockingRemoval(_database.RootGroup), Is.Not.Null,
                        "and every group it might be filling");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        /// <summary>
        /// A form KeePass's entry dialog in name and in its node fields, with no merged selection.
        /// </summary>
        /// <remarks>
        /// Emitted rather than declared, because a class of that full name in this assembly would
        /// shadow KeePass's own `PwEntryForm` for every other test in it.
        /// </remarks>
        private Form EntryDialogWithoutASelectionField(PwEntry entry)
        {
            var module = AppDomain.CurrentDomain
                .DefineDynamicAssembly(
                    new AssemblyName("EntryFormWithoutASelection"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
                .DefineDynamicModule("EntryFormWithoutASelection");
            var type = module.DefineType(
                KeePassContract.EntryFormTypeName, TypeAttributes.Public | TypeAttributes.Class, typeof(Form));
            type.DefineField(KeePassContract.EntryFormEntryField, typeof(PwEntry), FieldAttributes.Private);
            type.DefineField(KeePassContract.NodeFormDatabaseField, typeof(PwDatabase), FieldAttributes.Private);
            type.DefineDefaultConstructor(MethodAttributes.Public);

            var built = type.CreateType();
            var dialog = (Form)Activator.CreateInstance(built);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            built.GetField(KeePassContract.EntryFormEntryField, flags).SetValue(dialog, entry);
            built.GetField(KeePassContract.NodeFormDatabaseField, flags).SetValue(dialog, _database.Database);
            return dialog;
        }

        [Test]
        public void A_bulk_entry_dialog_blocks_every_entry_it_might_be_editing()
        {
            // KeePass's `Edit Entries` over a selection. The entry the dialog holds is a synthetic
            // one it merged, so it is reference-equal to neither of these, and matching on it
            // answered "nothing is open" for both. OK meanwhile applies the merged copy to the
            // whole selection and deletes any key the copy no longer carries. A write landing
            // underneath was therefore destroyed across every selected entry at once.
            var first = EntryUnderRoot();
            var second = EntryUnderRoot();

            using (var form = BulkEntryDialogFor(first, second))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Blocking(first), Is.Not.Null,
                        "the first selected entry is one this dialog will write over");
                    Assert.That(_dialogs.Blocking(second), Is.Not.Null,
                        "and so is the second");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_watch_that_never_started_refuses_removals_and_additions_too()
        {
            // The same answer `Blocking` gives, for the same reason: an empty table means nothing
            // was noticed rather than that nothing is open.
            using (var never = new GroupDialogs(new GlobalWindowManagerWindows(), NullGateLog.Instance))
            {
                Assert.That(never.BlockingRemoval(_database.RootGroup), Is.Not.Null);
                Assert.That(never.BlockingAdditions(_database.RootGroup), Is.Not.Null);
            }
        }

        [Test]
        public void An_add_entry_dialog_keeps_the_group_it_will_fill_in_the_tree()
        {
            // KeePass's Add Entry names the group as the new entry's parent and puts the entry into
            // it on OK and not before. Until then the group is empty and no group dialog is open on
            // it, so asking about the node a dialog holds found nothing, and a removal underneath
            // sent what the operator typed into the recycle bin, or with no bin out of the file.
            var group = new PwGroup(true, true, "Empty", PwIcon.Folder);
            _database.RootGroup.AddGroup(group, true);
            var other = new PwGroup(true, true, "Other", PwIcon.Folder);
            _database.RootGroup.AddGroup(other, true);

            var typed = new PwEntry(true, true);
            typeof(PwEntry).GetProperty("ParentGroup").SetValue(typed, group, null);

            using (var form = new PwEntryForm())
            {
                form.InitEx(typed, PwEditMode.AddNewEntry, _database.Database, _icons, false, true);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assume.That(group.Entries.UCount, Is.Zero, "the entry is not in the group yet");

                    Assert.That(_dialogs.BlockingRemoval(group), Is.Not.Null,
                        "the group this dialog fills on OK must stay where it is");
                    Assert.That(_dialogs.Blocking(group), Is.Null,
                        "and nothing else about it is refused, since the dialog holds none of it");
                    Assert.That(_dialogs.BlockingAdditions(group), Is.Null);
                    Assert.That(_dialogs.BlockingRemoval(other), Is.Null, "nor anything about another group");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_dialogs.BlockingRemoval(group), Is.Null, "and closing it lets the group go again");
        }

        [Test]
        public void The_tan_wizard_keeps_the_group_it_will_fill_in_the_tree()
        {
            // Tools then TAN Wizard is handed the selected group and adds one entry per TAN to it
            // on OK. A group made for TANs is empty until then, and removed underneath, the TANs
            // went into the recycle bin, or with no bin into a group no longer in the tree.
            var group = new PwGroup(true, true, "Bank", PwIcon.Folder);
            _database.RootGroup.AddGroup(group, true);

            using (var form = new TanWizardForm())
            {
                form.InitEx(_database.Database, group);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.BlockingRemoval(group), Is.Not.Null,
                        "the group the wizard fills on OK must stay where it is");
                    Assert.That(_dialogs.Blocking(group), Is.Null,
                        "and nothing else about it is refused, since the wizard holds none of it");
                    Assert.That(_dialogs.Count, Is.Zero,
                        "nor is it counted, since it puts back no custom data");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_dialogs.BlockingRemoval(group), Is.Null, "and closing it lets the group go again");
        }

        [Test]
        public void An_add_group_dialog_refuses_additions_to_its_group_and_an_edit_group_dialog_does_not()
        {
            // KeePass's Add Group puts the new group into the tree before the dialog opens and takes
            // it back out on Cancel, with no deleted object and no bin. Anything created in it in
            // between is answered as created and dropped by the next save. Edit Group holds no list
            // of what is inside, so adding to a group it is open on stays allowed.
            var adding = new PwGroup(true, true, "Adding", PwIcon.Folder);
            _database.RootGroup.AddGroup(adding, true);
            var editing = new PwGroup(true, true, "Editing", PwIcon.Folder);
            _database.RootGroup.AddGroup(editing, true);

            using (var add = new GroupForm())
            using (var edit = GroupDialogFor(editing))
            {
                add.InitEx(adding, true, null, _database.Database);
                GlobalWindowManager.AddWindow(add);
                GlobalWindowManager.AddWindow(edit);

                try
                {
                    Assert.That(_dialogs.BlockingAdditions(adding), Is.Not.Null,
                        "a group its Cancel takes away takes nothing in");
                    Assert.That(_dialogs.Blocking(adding), Is.Not.Null,
                        "and the group itself is held, as under any group dialog");
                    Assert.That(_dialogs.BlockingAdditions(editing), Is.Null,
                        "a group being edited stays open to additions");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(edit);
                    GlobalWindowManager.RemoveWindow(add);
                }
            }

            Assert.That(_dialogs.BlockingAdditions(adding), Is.Null, "and closing it lets additions in again");
        }

        [Test]
        public void A_bulk_entry_dialog_blocks_nothing_in_another_database()
        {
            // The selection behind `Edit Entries` is always one database's, and the gate serves
            // every database that is open. Refusing every entry everywhere turned one operator's
            // bulk edit into a `503 busy` for clients of databases nobody was looking at.
            var first = EntryUnderRoot();
            var second = EntryUnderRoot();

            using (var elsewhere = TemporaryDatabase.New())
            {
                var unrelated = new PwEntry(true, true);
                elsewhere.RootGroup.AddEntry(unrelated, true);

                using (var form = BulkEntryDialogFor(first, second))
                {
                    GlobalWindowManager.AddWindow(form);

                    try
                    {
                        Assume.That(_dialogs.Blocking(first), Is.Not.Null);

                        Assert.That(_dialogs.Blocking(unrelated), Is.Null,
                            "an entry of another database is none this dialog can write over");
                    }
                    finally
                    {
                        GlobalWindowManager.RemoveWindow(form);
                    }
                }
            }
        }

        [Test]
        public void Asking_about_nothing_blocks_nothing()
        {
            Assert.That(_dialogs.Blocking((KeePassLib.PwGroup)null), Is.Null);
            Assert.That(_dialogs.Blocking((KeePassLib.PwEntry)null), Is.Null);
        }

        [Test]
        public void The_maintenance_dialog_refuses_records_for_its_own_database_only()
        {
            // It copies the database's own custom data when it opens and puts the copy back when
            // it closes whenever the count differs, so a record written while it is open is
            // destroyed with nobody touching anything. Measured on the real dialog, because the
            // guard recognizes it by name and a name is only worth what the dialog still answers.
            using (var other = TemporaryDatabase.New())
            using (var form = new DatabaseOperationsForm())
            {
                form.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.BlockingRecords(_database.Database), Is.Not.Null,
                        "a record written now would be put back over when the dialog closes");
                    Assert.That(_dialogs.BlockingRecords(other.Database), Is.Null,
                        "and it holds nothing of any other database");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_dialogs.BlockingRecords(_database.Database), Is.Null,
                "and closing it lets records be written again");
        }

        [Test]
        public void KeePass_s_progress_window_says_it_is_working_until_it_closes()
        {
            // The one sign XML Replace gives of merging into the live database, which it does
            // with this window as its status logger. The real window, registered the way its own
            // `OnFormLoad` registers it, because the guard recognizes it by name.
            Assert.That(_dialogs.Working(), Is.Null, "nothing is running yet");

            using (var form = new StatusProgressForm())
            {
                form.InitEx("XML Replace", true, false, null);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Working(), Does.Contain("progress window"));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_dialogs.Working(), Is.Null, "and closing it lets writes through again");

            using (var never = new GroupDialogs(new GlobalWindowManagerWindows(), NullGateLog.Instance))
            {
                Assert.That(never.Working(), Is.Not.Null,
                    "a watch that is not running cannot say nothing is, so it refuses");
            }
        }

        [Test]
        public void Stopping_the_watch_forgets_what_was_open()
        {
            using (var form = GroupDialogFor(_database.RootGroup))
            {
                GlobalWindowManager.AddWindow(form);
                Assume.That(_dialogs.Count, Is.EqualTo(1));

                _dialogs.Dispose();

                try
                {
                    Assert.That(_dialogs.Count, Is.Zero);

                    // **And it refuses rather than allowing.** Forgetting is not the same as
                    // knowing nothing is open: the dialog above is still on screen, and the watch
                    // that would have noticed it has stopped. Answering null here would say a
                    // record may be written into a group whose dialog is about to put its own copy
                    // back over it, which is the one failure this class exists to prevent.
                    Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Not.Null,
                        "a watch that has stopped must refuse, not allow");

                    // Both halves, because only the group half had this and the entry half read an
                    // empty table as "nothing is open" and allowed the write.
                    Assert.That(_dialogs.Blocking(EntryUnderRoot()), Is.Not.Null,
                        "and it must refuse entries on the same reasoning");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        private PwEntry EntryUnderRoot()
        {
            var entry = new PwEntry(true, true);
            _database.RootGroup.AddEntry(entry, true);
            return entry;
        }

        private PwEntryForm EntryDialogFor(PwEntry entry)
        {
            var form = new PwEntryForm();
            form.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
            return form;
        }

        /// <summary>
        /// KeePass's `Edit Entries` over a selection of this fixture's database, never shown.
        /// </summary>
        /// <remarks>
        /// Built by reflection because the constructor is internal to KeePass. That is the point
        /// rather than an obstacle: this is a shape only KeePass can make, so only KeePass can say
        /// whether the guard still recognizes it.
        /// </remarks>
        private PwEntryForm BulkEntryDialogFor(params PwEntry[] selection)
        {
            var merged = new object[] { selection, _database.Database, null };
            var context = typeof(PwEntryForm).Assembly
                .GetType("KeePass.Util.MultipleValues.MultipleValuesEntryContext")
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0]
                .Invoke(merged);

            var form = new PwEntryForm();
            form.InitEx((PwEntry)merged[2], PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
            typeof(PwEntryForm)
                .GetProperty("MultipleValuesEntryContext")
                .SetValue(form, context, null);
            return form;
        }

        [Test]
        public void The_watch_can_be_started_twice_without_watching_twice()
        {
            Assert.That(_dialogs.Start(), Is.True);

            using (var form = GroupDialogFor(_database.RootGroup))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Count, Is.EqualTo(1));
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void An_open_entry_dialog_counts_as_a_dialog_that_is_open()
        {
            // An entry dialog puts its entry's custom data back on OK exactly as a group dialog
            // puts its group's, so it is one of the dialogs this watches, and blocks a write to the
            // entry it holds.
            var entry = new PwEntry(true, true);
            _database.RootGroup.AddEntry(entry, true);

            using (var form = new PwEntryForm())
            {
                form.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Count, Is.EqualTo(1),
                        "an entry dialog is a dialog that puts custom data back on OK");
                    Assert.That(_dialogs.Blocking(entry), Is.Not.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_dialogs.Count, Is.Zero, "and it stops counting when it closes");
        }

        [Test]
        public void A_bulk_entry_dialog_counts_as_a_dialog_that_is_open()
        {
            // Kept in a table of its own, so that it refuses only its own database's entries, and
            // still a dialog whose OK puts custom data back, over every entry it was opened on.
            using (var form = BulkEntryDialogFor(EntryUnderRoot(), EntryUnderRoot()))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assert.That(_dialogs.Count, Is.EqualTo(1),
                        "an Edit Entries dialog is a dialog that puts custom data back on OK");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_dialogs.Count, Is.Zero, "and it stops counting when it closes");
        }

        [Test]
        public void Stopping_the_watch_forgets_an_entry_dialog_too()
        {
            // Two questions here, and they have opposite answers. The **count** must go to zero,
            // because that is what refuses a profile deletion and a stale entry would refuse one
            // for ever. **Blocking must not**: a watch that has stopped cannot tell a table it
            // never filled from one that is empty, and the dialog below is still on screen.
            // Asserting null on both inverted the group half's own rule, which calls answering
            // null there "the one failure this class exists to prevent".
            var entry = new PwEntry(true, true);
            _database.RootGroup.AddEntry(entry, true);

            using (var form = new PwEntryForm())
            {
                form.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assume.That(_dialogs.Count, Is.EqualTo(1));

                    _dialogs.Dispose();

                    Assert.That(_dialogs.Count, Is.Zero);
                    Assert.That(_dialogs.Blocking(entry), Is.Not.Null,
                        "a watch that has stopped must refuse, not allow");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        /// <summary>
        /// A real group dialog, told which group it is editing, and never shown. `InitEx` is what
        /// sets the field the watcher reads, so this is the state the watcher meets in KeePass.
        /// </summary>
        private GroupForm GroupDialogFor(PwGroup group)
        {
            var form = new GroupForm();
            form.InitEx(group, false, null, _database.Database);
            return form;
        }
    }
}
