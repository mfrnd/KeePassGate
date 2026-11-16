using System;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a save made inside a modal dialog does to the window underneath it: the plugin's own
    /// save inside somebody else's dialog, and KeePass's inside a dialog of the plugin's.
    /// </summary>
    /// <remarks>
    /// On a real form and a real modal loop, because the whole of this is the difference between
    /// what WinForms records and what the window is. `ShowDialog` disables the owner by calling
    /// `EnableWindow` and never touches `Control.Enabled`, so a test that asks the property gets
    /// "enabled" throughout and sees nothing at all. Both halves are pinned here: the hazard, so
    /// that a KeePass or a WinForms that stopped behaving this way says so, and the repair.
    ///
    /// The body under test is `MainForm.UIBlockInteraction`'s, which is
    /// `Enabled = !UIIsInteractionBlocked()` run once at the start of a save and once at the end.
    /// With nothing else blocking, that is `Enabled = false` and then `Enabled = true`.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class WindowEnablingTests
    {
        [Test]
        public void A_save_inside_a_dialog_leaves_the_window_behind_it_disabled()
        {
            var enabledAfterTheSave = true;

            InsideAModalDialogOver(
                (main, dialog) =>
                {
                    WindowEnabling.Preserve(main, () => KeePassBlocksAndUnblocks(main));
                    enabledAfterTheSave = User32.IsWindowEnabled(main.Handle);
                });

            Assert.That(enabledAfterTheSave, Is.False,
                "otherwise the operator can use the main window behind a dialog that is still open");
        }

        [Test]
        public void And_that_is_not_what_KeePass_leaves_by_itself()
        {
            // The finding rather than the fix, so that this stops passing if either side changes:
            // KeePass's save if it stops assigning `Enabled`, WinForms if a modal loop starts
            // disabling the owner through the property instead of through the window.
            var enabledAfterTheSave = false;

            InsideAModalDialogOver(
                (main, dialog) =>
                {
                    Assert.That(User32.IsWindowEnabled(main.Handle), Is.False, "the modal loop disabled it");
                    Assert.That(main.Enabled, Is.True, "and did it without touching the property");

                    KeePassBlocksAndUnblocks(main);
                    enabledAfterTheSave = User32.IsWindowEnabled(main.Handle);
                });

            Assert.That(enabledAfterTheSave, Is.True);
        }

        [Test]
        public void A_save_with_no_dialog_open_leaves_the_window_alone()
        {
            using (var main = OffScreenForm.New())
            {
                main.Show();
                OffScreenForm.Pump();

                WindowEnabling.Preserve(main, () => KeePassBlocksAndUnblocks(main));

                Assert.That(User32.IsWindowEnabled(main.Handle), Is.True,
                    "nothing disabled it, so nothing here may");
                Assert.That(main.Enabled, Is.True);
            }
        }

        [Test]
        public void A_window_that_was_never_shown_is_nothing_to_preserve()
        {
            // Asking an unshown control for its handle creates one, which is a window this plugin
            // would then own. There is nothing to restore either way.
            var ran = 0;

            using (var main = new Form())
            {
                WindowEnabling.Preserve(main, () => ran++);

                Assert.That(ran, Is.EqualTo(1));
                Assert.That(main.IsHandleCreated, Is.False);
            }

            WindowEnabling.Preserve(null, () => ran++);
            Assert.That(ran, Is.EqualTo(2), "and no window at all is the same answer");
        }

        [Test]
        public void A_window_held_under_a_dialog_stays_disabled_when_KeePass_unblocks_it()
        {
            // KeePass's periodic trigger, inside the modal loop of a dialog it does not count,
            // running a synchronize that ends in `UIBlockInteraction(false)`.
            using (var main = OffScreenForm.New())
            {
                main.Show();
                OffScreenForm.Pump();

                var under = UnblockedUnder(main, main);

                Assert.That(under.DisabledBefore, Is.True, "the modal loop disabled it");
                Assert.That(under.EnabledAfter, Is.False,
                    "otherwise the operator can use the main window behind a dialog that is still open");
                Assert.That(User32.IsWindowEnabled(main.Handle), Is.True, "and it is back once the dialog closes");
                Assert.That(main.Enabled, Is.True);
            }
        }

        [Test]
        public void And_the_same_under_a_dialog_shown_with_no_owner()
        {
            // As the approval dialogs and the window under Tools are shown: a modal loop disables
            // every window of its thread, the main window among them, whoever the owner is.
            using (var main = OffScreenForm.New())
            {
                main.Show();
                OffScreenForm.Pump();

                var under = UnblockedUnder(main, null);

                Assert.That(under.DisabledBefore, Is.True, "the modal loop disabled it");
                Assert.That(under.EnabledAfter, Is.False);
                Assert.That(User32.IsWindowEnabled(main.Handle), Is.True, "and it is back once the dialog closes");
            }
        }

        [Test]
        public void A_window_the_dialog_did_not_disable_is_left_enabled()
        {
            // A main window hidden in the notification area: the modal loop passes over a window
            // that is not visible, and enables again only what it disabled, so a window disabled
            // here would stay disabled with nothing left to enable it.
            using (var main = OffScreenForm.New())
            {
                Assume.That(main.Handle, Is.Not.EqualTo(IntPtr.Zero), "a window that exists and is not shown");

                var under = UnblockedUnder(main, null);

                Assert.That(under.DisabledBefore, Is.False, "the modal loop left it alone");
                Assert.That(under.EnabledAfter, Is.True);
                Assert.That(User32.IsWindowEnabled(main.Handle), Is.True, "and nothing disabled it afterwards");
            }
        }

        [Test]
        public void There_is_no_dialog_to_show_without_one()
        {
            Assert.Throws<ArgumentNullException>(() => WindowEnabling.ShowDialog(null, null, null));
        }

        /// <summary>
        /// Shows a dialog through <see cref="WindowEnabling.ShowDialog"/>, holding the main window
        /// under it, and has KeePass block and unblock the main window from a timer inside the
        /// dialog's own loop, which is where KeePass's periodic trigger runs.
        /// </summary>
        private static Unblocked UnblockedUnder(Form main, IWin32Window owner)
        {
            var under = new Unblocked();

            using (var dialog = OffScreenForm.New())
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                timer.Tick += delegate
                {
                    timer.Stop();
                    under.DisabledBefore = !User32.IsWindowEnabled(main.Handle);
                    KeePassBlocksAndUnblocks(main);
                    under.EnabledAfter = User32.IsWindowEnabled(main.Handle);
                    dialog.Close();
                };

                timer.Start();
                WindowEnabling.ShowDialog(dialog, owner, main);
            }

            return under;
        }

        /// <summary>The main window under the dialog, either side of KeePass's unblock.</summary>
        private sealed class Unblocked
        {
            public bool DisabledBefore { get; set; }

            public bool EnabledAfter { get; set; }
        }

        /// <summary>What `MainForm.SaveDatabase` does to the main window around its save.</summary>
        private static void KeePassBlocksAndUnblocks(Form main)
        {
            main.Enabled = false;
            main.Enabled = true;
        }

        /// <summary>
        /// Runs the check inside a real modal dialog owned by a real main window, and closes it.
        /// </summary>
        /// <remarks>
        /// From `Shown` rather than before `ShowDialog`, because the owner is disabled by the call
        /// that shows the dialog, which is the state being measured. The dialog closes itself so
        /// that a failing assertion cannot leave a modal window holding the test run.
        /// </remarks>
        private static void InsideAModalDialogOver(Action<Form, Form> check)
        {
            using (var main = OffScreenForm.New())
            {
                main.Show();
                OffScreenForm.Pump();

                using (var dialog = OffScreenForm.New())
                {
                    Exception failure = null;

                    dialog.Shown += delegate
                    {
                        failure = Thrown.By(() => check(main, dialog));

                        dialog.Close();
                    };

                    dialog.ShowDialog(main);

                    if (failure != null)
                    {
                        throw failure;
                    }
                }
            }
        }
    }
}
