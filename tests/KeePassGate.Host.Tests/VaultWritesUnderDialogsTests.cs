using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.UI;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The API's writes against a dialog KeePass really has open on the node they are about.
    /// </summary>
    /// <remarks>
    /// `SnapshotHazardTests` establishes that a dialog destroys what is written underneath it on
    /// OK, `GroupDialogsTests` establishes that the watcher notices one, and `KdbxVaultTests`
    /// establishes what each write does to a database. Nothing joined the three: `KdbxVaultTests`
    /// builds its writer with no watch, which makes `DatabaseWriter.Blocking` answer null for
    /// everything, so the rule `KdbxVault` documents was true only of its source. A change that
    /// asked about the group above an entry, or stopped asking on one route, would pass that
    /// fixture with nothing to say about it.
    ///
    /// Both halves of the rule are here, because half of it is an allowance rather than a refusal:
    /// **the node being written and only that node**. Neither dialog holds the list of what is
    /// inside a group, so an entry added to a group somebody is looking at survives that dialog's
    /// OK and is allowed. The dialogs that add something are the exception in both directions: a
    /// group Add Entry or the TAN wizard will fill stays in the tree, and a group Add Group may take
    /// back out takes nothing in.
    ///
    /// The dialogs are built and initialized and never shown, as they are in `GroupDialogsTests`.
    /// What is under test is what the watcher sees from the window notification, and a modal window
    /// would stop the run rather than answer anything.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class VaultWritesUnderDialogsTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private ImageList _icons;
        private IVault _vault;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _icons = new ImageList();
            _saves = 0;

            Assert.That(_dialogs.Start(), Is.True);

            _vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, Save, null, _dialogs, NullGateLog.Instance),
                TestTags.Fixed,
                NullGateLog.Instance);
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
        public void An_entry_dialog_refuses_every_write_to_the_entry_it_is_holding()
        {
            // Ten routes reach an entry and each of them asks separately, so each of them is asked
            // here. The dialog hands its own copy of the strings and the binaries to its tabs and
            // writes that copy back over the entry on OK, which takes out anything written
            // underneath it in between.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Outer", null));
            var entry = Created(_vault.CreateEntry(group, Named("Invented")));

            Assert.That(_vault.CreateField(entry, "Ticket", "4b21", false, VaultTagging.None).Ok, Is.True);
            Assert.That(_vault.CreateAttachment(entry, "note.txt", new byte[] { 1, 2, 3 }, VaultTagging.None).Ok, Is.True);

            using (var form = EntryDialogFor(Entry(entry)))
            {
                GlobalWindowManager.AddWindow(form);
                _saves = 0;

                try
                {
                    Refused(_vault.UpdateEntry(entry, Named("Renamed"), VaultTagging.None), "UpdateEntry");
                    Refused(_vault.CreateField(entry, "Second", "9c30", false, VaultTagging.None), "CreateField");
                    Refused(_vault.UpdateField(entry, "Ticket", "0000", VaultTagging.None), "UpdateField");
                    Refused(_vault.DeleteField(entry, "Ticket", VaultTagging.None), "DeleteField");
                    Refused(_vault.CreateAttachment(entry, "other.txt", new byte[] { 4 }, VaultTagging.None), "CreateAttachment");
                    Refused(_vault.UpdateAttachment(entry, "note.txt", new byte[] { 5 }, VaultTagging.None), "UpdateAttachment");
                    Refused(_vault.DeleteAttachment(entry, "note.txt", VaultTagging.None), "DeleteAttachment");
                    Refused(_vault.DeleteEntry(entry, VaultTagging.None), "DeleteEntry");

                    Assert.That(_saves, Is.Zero, "a refused write saves nothing");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            // And the entry is as it was, rather than changed and put back.
            Assert.That(Entry(entry).Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("Invented"));
            Assert.That(Entry(entry).Strings.ReadSafe("Ticket"), Is.EqualTo("4b21"));
            Assert.That(Entry(entry).Binaries.Get("note.txt"), Is.Not.Null);
        }

        [Test]
        public void An_entry_dialog_refuses_nothing_about_a_different_entry()
        {
            // Per node rather than wholesale. A dialog open somewhere in the database is the
            // ordinary state of a password manager somebody is using, and refusing every write
            // while one is open would make the API answer `503 busy` for as long as it stays open.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Outer", null));
            var watched = Created(_vault.CreateEntry(group, Named("Watched")));
            var other = Created(_vault.CreateEntry(group, Named("Other")));

            using (var form = EntryDialogFor(Entry(watched)))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Assume.That(_vault.UpdateEntry(watched, Named("Renamed"), VaultTagging.None).IsBusy, Is.True);

                    var outcome = _vault.UpdateEntry(other, Named("Renamed"), VaultTagging.None);

                    Assert.That(outcome.Ok, Is.True, outcome.Reason);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_group_dialog_refuses_the_group_and_lets_through_what_goes_into_it()
        {
            // Both halves of the documented rule in one place, because they are one decision. The
            // dialog copies the group's own name, notes and custom data and puts that copy back on
            // OK; it copies nothing about which entries and subgroups are in there, and the tree
            // it shows is rebuilt from the database afterwards.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Outer", null));

            using (var form = GroupDialogFor(Group(group)))
            {
                GlobalWindowManager.AddWindow(form);
                _saves = 0;

                try
                {
                    Refused(_vault.UpdateGroup(group, "Renamed", null, VaultTagging.None), "UpdateGroup");
                    Refused(_vault.DeleteGroup(group, VaultTagging.None), "DeleteGroup");
                    Assert.That(_saves, Is.Zero);

                    var entry = _vault.CreateEntry(group, Named("Invented"));
                    Assert.That(entry.Ok, Is.True, entry.Reason);

                    var inner = _vault.CreateGroup(group, "Inner", null);
                    Assert.That(inner.Ok, Is.True, inner.Reason);

                    Assert.That(_saves, Is.EqualTo(2), "and both of those reached the disk");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(Group(group).Name, Is.EqualTo("Outer"), "the refused rename never happened");
        }

        [Test]
        public void A_group_dialog_refuses_nothing_about_the_entries_inside_it()
        {
            // The node a route is about is the nearest addressable one, so an entry is answered for
            // by itself rather than by whatever holds it. Asking about the group instead would
            // refuse every write in a subtree because somebody opened the folder above it.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Outer", null));
            var entry = Created(_vault.CreateEntry(group, Named("Invented")));

            using (var form = GroupDialogFor(Group(group)))
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var outcome = _vault.UpdateEntry(entry, Named("Renamed"), VaultTagging.None);

                    Assert.That(outcome.Ok, Is.True, outcome.Reason);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_group_that_add_entry_will_fill_is_not_removed_underneath_it()
        {
            // Add Entry puts its entry into the group on OK and not before, so the group is empty
            // while the dialog is open. Removed then, the operator's OK put what they typed into the
            // recycle bin, or with the bin off into a group no longer in the tree and never saved.
            _database.Database.RecycleBinEnabled = false;
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Empty", null));

            var typed = new PwEntry(true, true);
            typeof(PwEntry).GetProperty("ParentGroup").SetValue(typed, Group(group), null);

            using (var form = new PwEntryForm())
            {
                form.InitEx(typed, PwEditMode.AddNewEntry, _database.Database, _icons, false, true);
                GlobalWindowManager.AddWindow(form);
                _saves = 0;

                try
                {
                    Refused(_vault.DeleteGroup(group, VaultTagging.None), "DeleteGroup");
                    Assert.That(_saves, Is.Zero, "a refused write saves nothing");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            // What the dialog's OK does, and the entry is somewhere the file will carry it.
            Group(group).AddEntry(typed, true);
            Assert.That(Entry(typed.Uuid.ToHexString()), Is.SameAs(typed));
        }

        [Test]
        public void A_group_the_tan_wizard_will_fill_is_not_removed_underneath_it()
        {
            // The same hazard from Tools then TAN Wizard, which fills the group on OK.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Bank", null));

            using (var form = new TanWizardForm())
            {
                form.InitEx(_database.Database, Group(group));
                GlobalWindowManager.AddWindow(form);

                try
                {
                    Refused(_vault.DeleteGroup(group, VaultTagging.None), "DeleteGroup");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(_vault.DeleteGroup(group, VaultTagging.None).Ok, Is.True, "and once it is closed the group may go");
        }

        [Test]
        public void Nothing_is_created_in_a_group_add_group_may_take_away()
        {
            // Add Group puts the group into the tree before the dialog opens and takes it back out on
            // Cancel, with no deleted object and no bin. An entry created in it in between was
            // answered 201 and then dropped from the file by the next save.
            var adding = new PwGroup(true, true, "Adding", PwIcon.Folder);
            _database.Database.RootGroup.AddGroup(adding, true);
            var uuid = adding.Uuid.ToHexString();

            using (var form = new GroupForm())
            {
                form.InitEx(adding, true, null, _database.Database);
                GlobalWindowManager.AddWindow(form);
                _saves = 0;

                try
                {
                    Refused(_vault.CreateEntry(uuid, Named("Invented")), "CreateEntry");
                    Refused(_vault.CreateGroup(uuid, "Inner", null), "CreateGroup");
                    Assert.That(_saves, Is.Zero, "a refused write saves nothing");

                    var beside = _vault.CreateEntry(_vault.RootAlias, Named("Beside"));
                    Assert.That(beside.Ok, Is.True, "and the group it was added to is not refused: " + beside.Reason);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }

            Assert.That(adding.Entries.UCount + adding.Groups.UCount, Is.Zero,
                "nothing went into a group whose Cancel would have taken it away");
        }

        [Test]
        public void A_write_delivered_inside_xml_replace_s_merge_is_refused_and_the_merge_finishes()
        {
            // XML Replace merges its result into the live database with KeePass's progress window
            // as the status logger, and that window's `ContinueWork` pumps on every call. Neither
            // of KeePass's own busy signals is raised, so the window is the only thing the write
            // can see. Driven with the real window as the pump and the real merge, posting the
            // write the moment `ApplyDeletions` is walking the root group's children, which is
            // where a group added to the root breaks the enumeration.
            var merge = MergeWithAWriteInside(true);

            Assert.That(merge.Landed, Is.Not.Null, "the merge's own pump delivered the write");
            Assert.That(merge.Inside, Is.True, "while ApplyDeletions was on the stack");
            Assert.That(merge.Landed.IsBusy, Is.True, merge.Landed.Reason);
            Assert.That(merge.Landed.Reason, Does.Contain("progress window"));
            Assert.That(merge.Thrown, Is.Null, "so the merge was not broken");
            Assert.That(merge.Left, Is.Zero, "and it finished: every deletion the replica carried was applied");
            Assert.That(_saves, Is.Zero, "a refused write saves nothing");
        }

        [Test]
        public void Without_the_window_the_same_write_breaks_the_merge()
        {
            // The hazard the test above is guarding, pinned as well as the guard, because it is a
            // claim about KeePass rather than about this code: the write lands, is saved half way
            // through the merge, and the merge then throws out of the walk over the root group's
            // children. Whatever it deleted after that save is in memory only, with `Modified`
            // cleared by the save, and whatever it had still to do it never does.
            var merge = MergeWithAWriteInside(false);

            Assert.That(merge.Inside, Is.True);
            Assert.That(merge.Landed.Ok, Is.True, "nothing refused it");
            Assert.That(merge.Thrown, Is.InstanceOf<InvalidOperationException>(),
                "the root group's children moved under ApplyDeletions");
            Assert.That(_saves, Is.EqualTo(1), "after the write was saved half way through the merge");
            Assert.That(_database.Database.Modified, Is.False,
                "and the database says nothing is unsaved, though it no longer holds what the file does");
        }

        /// <summary>
        /// Synchronizes a replica that deletes forty entries into the fixture's database, with
        /// KeePass's progress window as the logger, and posts a group creation under the root the
        /// first time the merge asks that window to pump from inside `ApplyDeletions`.
        /// </summary>
        /// <remarks>
        /// The window is built and never shown, like every dialog in this fixture, and registered
        /// the way its own `OnFormLoad` registers it, or not at all. The write is posted to this
        /// thread's queue exactly as `ControlUiThread` posts one, and so is delivered by the next
        /// `Application.DoEvents` the window runs, which is the next step of the same loop.
        /// </remarks>
        private (VaultWrite Landed, bool Inside, Exception Thrown, uint Left) MergeWithAWriteInside(bool registered)
        {
            var merged = new PwGroup(true, true, "Merged", PwIcon.Folder);
            _database.Database.RootGroup.AddGroup(merged, true);

            var replica = new PwDatabase();
            replica.New(new KeePassLib.Serialization.IOConnectionInfo(), _database.Database.MasterKey);
            replica.RootGroup.Uuid = _database.Database.RootGroup.Uuid;

            var later = DateTime.UtcNow.AddMinutes(1);
            for (var i = 0; i < 40; i++)
            {
                var entry = new PwEntry(true, true);
                merged.AddEntry(entry, true);
                replica.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, later));
            }

            _database.Save();
            _saves = 0;

            VaultWrite landed = null;
            var inside = false;
            Exception thrown = null;

            using (var queue = new Control())
            using (var window = new StatusProgressForm())
            {
                Assume.That(queue.Handle, Is.Not.EqualTo(IntPtr.Zero));
                window.InitEx("XML Replace", true, false, null);

                var logger = new PostsOnceInside("ApplyDeletions", window, () => queue.BeginInvoke(new MethodInvoker(() =>
                {
                    inside = OnTheStack("ApplyDeletions");
                    landed = _vault.CreateGroup(_vault.RootAlias, "Landed", null);
                })));

                if (registered)
                {
                    GlobalWindowManager.AddWindow(window);
                }

                try
                {
                    _database.Database.MergeIn(replica, PwMergeMethod.Synchronize, logger);
                }
                catch (InvalidOperationException error)
                {
                    thrown = error;
                }
                finally
                {
                    if (registered)
                    {
                        GlobalWindowManager.RemoveWindow(window);
                    }
                }
            }

            return (landed, inside, thrown, merged.Entries.UCount);
        }

        private static bool OnTheStack(string method)
        {
            foreach (var frame in new System.Diagnostics.StackTrace().GetFrames())
            {
                if (frame.GetMethod() != null && frame.GetMethod().Name == method)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A status logger that is KeePass's progress window, and posts one piece of work the
        /// first time it is asked anything from inside the named method.
        /// </summary>
        private sealed class PostsOnceInside : KeePassLib.Interfaces.IStatusLogger
        {
            private readonly string _method;
            private readonly KeePassLib.Interfaces.IStatusLogger _window;
            private readonly Action _post;
            private bool _posted;

            public PostsOnceInside(string method, KeePassLib.Interfaces.IStatusLogger window, Action post)
            {
                _method = method;
                _window = window;
                _post = post;
            }

            public void StartLogging(string operation, bool writeToLog)
            {
                _window.StartLogging(operation, writeToLog);
            }

            public void EndLogging()
            {
                _window.EndLogging();
            }

            public bool SetProgress(uint percent)
            {
                Post();
                return _window.SetProgress(percent);
            }

            public bool SetText(string text, KeePassLib.Interfaces.LogStatusType type)
            {
                Post();
                return _window.SetText(text, type);
            }

            public bool ContinueWork()
            {
                Post();
                return _window.ContinueWork();
            }

            private void Post()
            {
                if (!_posted && OnTheStack(_method))
                {
                    _posted = true;
                    _post();
                }
            }
        }

        private static void Refused(VaultWrite write, string route)
        {
            Assert.That(write.IsBusy, Is.True, route + " answered " + write.Reason);
            Assert.That(write.Reason, Does.Contain("dialog is open"), route);
        }

        private static string Created(VaultWrite write)
        {
            Assert.That(write.Ok, Is.True, write.Reason);
            return write.Uuid;
        }

        private static IReadOnlyDictionary<string, string> Named(string title)
        {
            return new Dictionary<string, string> { { PwDefs.TitleField, title } };
        }

        private PwEntry Entry(string uuid)
        {
            return _database.Database.RootGroup.FindEntry(
                new PwUuid(KeePassLib.Utility.MemUtil.HexStringToByteArray(uuid)), true);
        }

        private PwGroup Group(string uuid)
        {
            return _database.Database.RootGroup.FindGroup(
                new PwUuid(KeePassLib.Utility.MemUtil.HexStringToByteArray(uuid)), true);
        }

        private PwEntryForm EntryDialogFor(PwEntry entry)
        {
            var form = new PwEntryForm();
            form.InitEx(entry, PwEditMode.EditExistingEntry, _database.Database, _icons, false, false);
            return form;
        }

        private GroupForm GroupDialogFor(PwGroup group)
        {
            var form = new GroupForm();
            form.InitEx(group, false, null, _database.Database);
            return form;
        }

        private void Save(PwDatabase database)
        {
            _saves++;
            database.Save(null);
        }
    }
}
