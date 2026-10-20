using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
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
    /// The recycle bin against the one dialog that holds a copy of which group it is.
    /// </summary>
    /// <remarks>
    /// `SnapshotHazardTests` establishes that KeePass's database settings dialog copies no custom
    /// data, which is what makes the KPG tab's own buttons safe on it. That is true and it is not
    /// the whole of what that dialog writes back: it also fills its recycle bin combo from
    /// `RecycleBinUuid` when it loads and writes the choice back on OK. A database with no bin
    /// therefore has that dialog holding "none", and a removal that makes the first bin underneath
    /// one is undone by its OK, leaving what the removal moved in an ordinary group under the root
    /// that this API lists, reads and addresses. That is the relocation `DESIGN.md` §138 closes by
    /// putting the bin out of reach.
    ///
    /// Measured in both halves, as the snapshot hazard is: what KeePass's own OK does to a bin made
    /// underneath it, and that the plugin now refuses rather than making one.
    ///
    /// Everything in here is invented, and the database is built and deleted by the fixture.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class RecycleBinDialogTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private IVault _vault;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _database.Database.RecycleBinEnabled = true;
            _database.Database.RecycleBinUuid = PwUuid.Zero;

            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            Assert.That(_dialogs.Start(), Is.True);

            _vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(
                    CurrentThread.Instance, database => database.Save(null), null, _dialogs, null),
                null);
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void The_settings_dialog_takes_back_a_recycle_bin_made_underneath_it()
        {
            // The hazard itself, driven through KeePass's own OK handler. The bin is made straight
            // on the database rather than through the vault, because the vault refuses that now and
            // this is what it is refusing.
            using (var dialog = Opened())
            {
                var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
                _database.RootGroup.AddGroup(bin, true);
                _database.Database.RecycleBinUuid = bin.Uuid;

                KeePassDialogs.PressOk(dialog.Form);

                Assert.That(_database.Database.RecycleBinUuid.Equals(PwUuid.Zero), Is.True,
                    "the dialog put back the choice it loaded, which named no group");
                Assert.That(_database.RootGroup.Groups.Any(g => ReferenceEquals(g, bin)), Is.True,
                    "and the group it pointed at is still there, now an ordinary one under the root");
            }
        }

        [Test]
        public void A_removal_that_would_make_the_first_bin_is_refused_while_that_dialog_is_open()
        {
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var entry = Created(_vault.CreateEntry(group, Named("an invented service")));

            using (Opened())
            {
                var refused = _vault.DeleteEntry(entry);

                Assert.That(refused.IsBusy, Is.True, refused.Reason);
                Assert.That(refused.Reason, Does.Contain("database settings dialog"));
                Assert.That(_database.Database.RecycleBinUuid.Equals(PwUuid.Zero), Is.True,
                    "no bin was made, so there is none for that dialog's OK to take back");
                Assert.That(_vault.Entries(group).Select(e => e.Uuid), Is.EqualTo(new[] { entry }),
                    "and the entry is where it was, which is what a refusal has to leave behind");
            }
        }

        [Test]
        public void A_group_removal_is_refused_the_same_way()
        {
            // Removing a group is a move into the bin too, so it makes one on the same branch.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));

            using (Opened())
            {
                var refused = _vault.DeleteGroup(group);

                Assert.That(refused.IsBusy, Is.True, refused.Reason);
                Assert.That(_vault.Group(group), Is.Not.Null);
                Assert.That(_database.Database.RecycleBinUuid.Equals(PwUuid.Zero), Is.True);
            }
        }

        [Test]
        public void A_bin_that_was_already_there_when_the_dialog_opened_is_not_refused()
        {
            // What the dialog copied, the dialog puts back, so only the branch that makes the first
            // bin is a hazard. Refusing more than that would stop every removal on a database whose
            // operator has the settings dialog open, and that dialog is where the KPG tab lives.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var first = Created(_vault.CreateEntry(group, Named("an invented service")));
            var second = Created(_vault.CreateEntry(group, Named("another invented service")));

            Assume.That(_vault.DeleteEntry(first).Ok, Is.True, "which makes the bin");
            Assume.That(_database.Database.RecycleBinUuid.Equals(PwUuid.Zero), Is.False);

            using (Opened())
            {
                Assert.That(_vault.DeleteEntry(second).Ok, Is.True);
                Assert.That(_vault.Entry(second), Is.Null);
            }
        }

        [Test]
        public void A_removal_is_not_refused_by_a_dialog_on_another_database()
        {
            using (var other = TemporaryDatabase.New())
            using (new SettingsDialog(other.Database))
            {
                var entry = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

                Assert.That(_vault.DeleteEntry(entry).Ok, Is.True);
                Assert.That(_database.Database.RecycleBinUuid.Equals(PwUuid.Zero), Is.False);
            }
        }

        [Test]
        public void Closing_it_lets_the_removal_through_again()
        {
            var entry = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            using (var dialog = Opened())
            {
                Assume.That(_vault.DeleteEntry(entry).IsBusy, Is.True);

                // The gesture under test, and the only place the closing is not simply cleanup.
                dialog.Close();

                Assert.That(_vault.DeleteEntry(entry).Ok, Is.True);
                Assert.That(_vault.Entry(entry), Is.Null);
            }
        }

        [Test]
        public void A_removal_is_refused_while_nothing_is_being_watched()
        {
            // Fail closed, as every other question this watch answers does. An empty table means
            // nothing was noticed rather than that nothing is open, and a settings dialog on the
            // desktop is exactly what a watch that never started cannot see.
            using (var never = new GroupDialogs(new GlobalWindowManagerWindows(), NullGateLog.Instance))
            {
                Assert.That(never.BlockingRecycleBin(_database.Database), Is.Not.Null);
            }

            Assert.That(_dialogs.BlockingRecycleBin(_database.Database), Is.Null,
                "and the fixture's own, which did start and has nothing open");
        }

        [Test]
        public void A_settings_dialog_that_has_not_been_told_its_database_refuses_every_database()
        {
            // Fail closed. A dialog whose database cannot be read might be on this one.
            using (var bare = KeePassDialogs.NewDatabaseSettingsForm())
            {
                GlobalWindowManager.AddWindow(bare);

                try
                {
                    Assert.That(_dialogs.BlockingRecycleBin(_database.Database), Is.Not.Null);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(bare);
                }
            }
        }

        [Test]
        public void A_settings_dialog_is_not_one_of_the_dialogs_that_are_counted()
        {
            // It carries no node's custom data, so it is not one of the dialogs whose OK puts a
            // node back; what it does copy, the recycle bin choice, is asked about separately.
            using (Opened())
            {
                Assert.That(_dialogs.Count, Is.Zero);
                Assert.That(_dialogs.Blocking(_database.RootGroup), Is.Null,
                    "and it copies no custom data, so it blocks no rule and no record either");
            }
        }

        private SettingsDialog Opened()
        {
            return new SettingsDialog(_database.Database);
        }

        private static IReadOnlyDictionary<string, string> Named(string title)
        {
            return new Dictionary<string, string> { { PwDefs.TitleField, title } };
        }

        private static string Created(VaultWrite write)
        {
            Assert.That(write.Ok, Is.True, write.Reason);
            Assert.That(write.Uuid, Is.Not.Null.And.Not.Empty);
            return write.Uuid;
        }

        /// <summary>
        /// A real database settings dialog on a database, registered the way KeePass registers one.
        /// </summary>
        /// <remarks>
        /// Disposable rather than a form and a `finally`, because `GlobalWindowManager` is static
        /// and outlives the fixture: a dialog left in it by an assertion that failed early is one
        /// every later test in the tier walks. Closing twice is closing once, so the test where the
        /// closing is the gesture can do it in the middle and still be tidied up after.
        /// </remarks>
        private sealed class SettingsDialog : IDisposable
        {
            private readonly Form _form;
            private bool _open;

            public SettingsDialog(PwDatabase database)
            {
                _form = KeePassDialogs.NewDatabaseSettingsForm();

                try
                {
                    KeePassDialogs.InitializeFor(_form, database);
                    GlobalWindowManager.AddWindow(_form);
                    _open = true;
                }
                catch (Exception)
                {
                    _form.Dispose();
                    throw;
                }
            }

            public Form Form
            {
                get { return _form; }
            }

            public void Close()
            {
                if (!_open)
                {
                    return;
                }

                GlobalWindowManager.RemoveWindow(_form);
                _open = false;
            }

            public void Dispose()
            {
                Close();
                _form.Dispose();
            }
        }
    }
}
