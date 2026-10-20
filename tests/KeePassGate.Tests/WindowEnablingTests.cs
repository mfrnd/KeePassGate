using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a save made inside somebody else's modal dialog does to the window underneath it.
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
                    enabledAfterTheSave = IsWindowEnabled(main.Handle);
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
                    Assert.That(IsWindowEnabled(main.Handle), Is.False, "the modal loop disabled it");
                    Assert.That(main.Enabled, Is.True, "and did it without touching the property");

                    KeePassBlocksAndUnblocks(main);
                    enabledAfterTheSave = IsWindowEnabled(main.Handle);
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

                Assert.That(IsWindowEnabled(main.Handle), Is.True,
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
                        try
                        {
                            check(main, dialog);
                        }
                        catch (Exception error)
                        {
                            failure = error;
                        }
                        finally
                        {
                            dialog.Close();
                        }
                    };

                    dialog.ShowDialog(main);

                    if (failure != null)
                    {
                        throw failure;
                    }
                }
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowEnabled(IntPtr window);
    }
}
