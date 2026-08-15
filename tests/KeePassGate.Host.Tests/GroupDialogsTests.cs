using System;
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

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);

            Assert.That(_dialogs.Start(), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
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
            // TODO(step 8): the approval dialog arrives with operator approval.
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
        public void Asking_about_nothing_blocks_nothing()
        {
            Assert.That(_dialogs.Blocking(null), Is.Null);
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
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
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
