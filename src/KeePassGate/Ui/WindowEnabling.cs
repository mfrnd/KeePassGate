using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Keeps KeePass's main window disabled under a modal dialog, against the call of KeePass's
    /// own that enables it.
    /// </summary>
    /// <remarks>
    /// **What enables it.** `MainForm.UIBlockInteraction` is `Enabled = !UIIsInteractionBlocked()`,
    /// and KeePass brackets a save, a synchronize and every other operation that blocks the window
    /// with a call to block it and a call to unblock it, so the closing call assigns
    /// `Enabled = true`. For KeePass that is harmless under its own dialogs, because each of them
    /// is registered with `GlobalWindowManager`, and its periodic trigger, which is what runs a
    /// synchronize or a save with nobody asking, fires only while that manager counts no window.
    /// It is not harmless under this plugin's dialogs, which are not registered, the approval
    /// dialogs on purpose, `PAIRING.md` §308, so the trigger fires inside their modal loops and a
    /// synchronize or a save it runs ends by unblocking the window behind them. And this plugin
    /// saves inside a modal loop itself, deliberately: a write to a node no open dialog is holding
    /// is allowed, and the write and its save happen together, which is inside whatever dialog the
    /// operator has open at the time.
    ///
    /// **`ShowDialog` disables the owner natively and leaves `Control.Enabled` true.** So the pair
    /// of assignments moves the managed property twice and the window twice, and the second of
    /// them calls `EnableWindow(true)` on a window the modal loop had disabled. From there the
    /// operator can click and type in the main window behind the dialog that is still open: open a
    /// second editor on the entry that dialog is editing, where whichever OK is pressed last
    /// silently wins, move or delete that entry, open the database settings behind a question
    /// about which authority to keep, or close the database out from under it. Nothing in KeePass
    /// puts the window back.
    ///
    /// **Natively rather than through `Control.Enabled`**, because the managed property is not what
    /// is wrong. It reads true before the save and true after it, exactly as WinForms left it, and
    /// assigning it again would be a third change to the half that was already right. What is
    /// wrong is the window, which is also what the modal loop itself set, and which that loop sets
    /// back when it ends: that is what makes putting it back here a restoration rather than
    /// something that has to be undone later.
    ///
    /// **And only when a modal loop had disabled it.** A save with no dialog open runs on an
    /// enabled window and leaves an enabled window, and disabling the window then would take it
    /// away from an operator who has nothing open to give it back.
    /// </remarks>
    public static class WindowEnabling
    {
        /// <summary>
        /// Runs <paramref name="work"/>, and re-disables the window if it was disabled before and
        /// is not afterwards.
        /// </summary>
        /// <remarks>
        /// On the thread that owns the window, which for the save is the one it is marshaled to.
        /// A window with no handle yet is nothing to preserve, and asking for one would create it.
        /// </remarks>
        public static void Preserve(Control window, Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            if (window == null || !window.IsHandleCreated)
            {
                work();
                return;
            }

            var handle = window.Handle;
            var wasDisabled = !NativeMethods.IsWindowEnabled(handle);

            try
            {
                work();
            }
            finally
            {
                if (wasDisabled && NativeMethods.IsWindowEnabled(handle))
                {
                    NativeMethods.EnableWindow(handle, false);
                }
            }
        }

        /// <summary>
        /// Shows a dialog of this plugin's modally, and keeps <paramref name="window"/> disabled
        /// for as long as it stands, whatever KeePass assigns to the window meanwhile.
        /// </summary>
        /// <param name="dialog">The dialog, shown with `ShowDialog`.</param>
        /// <param name="owner">The dialog's owner, or null for the window that is active.</param>
        /// <param name="window">KeePass's main window, or null where there is none to hold.</param>
        /// <remarks>
        /// On the thread that owns the window, which is the thread every dialog of this plugin's is
        /// shown on.
        ///
        /// **Through `EnabledChanged`**, because that is raised exactly when the unblock moves the
        /// property back to true, and after `Control.OnEnabledChanged` has enabled the window:
        /// disabling it again there leaves no message between the two in which the window takes
        /// input. The modal loop's own end enables the window again without touching the property,
        /// so nothing here runs then, and nothing is left disabled once the dialog has gone.
        ///
        /// **Held only if the dialog's modal loop disabled it**, which is the window disabled and
        /// its property still true when the dialog is shown. A loop disables only the windows that
        /// were visible and enabled as it began, and enables again only those: a main window hidden
        /// in the notification area, or blocked by KeePass at that moment, is not among them, and
        /// disabling it here would leave it disabled after the dialog with nothing to enable it.
        /// </remarks>
        public static void ShowDialog(Form dialog, IWin32Window owner, Control window)
        {
            if (dialog == null)
            {
                throw new ArgumentNullException(nameof(dialog));
            }

            if (window == null)
            {
                dialog.ShowDialog(owner);
                return;
            }

            var held = false;
            EventHandler shown = delegate
            {
                held = window.IsHandleCreated && window.Enabled && !NativeMethods.IsWindowEnabled(window.Handle);
            };

            EventHandler enabled = delegate
            {
                if (held && window.IsHandleCreated && window.Enabled && NativeMethods.IsWindowEnabled(window.Handle))
                {
                    NativeMethods.EnableWindow(window.Handle, false);
                }
            };

            dialog.Shown += shown;
            window.EnabledChanged += enabled;
            try
            {
                dialog.ShowDialog(owner);
            }
            finally
            {
                window.EnabledChanged -= enabled;
                dialog.Shown -= shown;
            }
        }
    }
}
