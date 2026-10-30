using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Runs something that re-enables a window, and leaves the window as it found it.
    /// </summary>
    /// <remarks>
    /// One caller, and it is the save. `MainForm.SaveDatabase` brackets its work with
    /// `UIBlockInteraction(true)` and `UIBlockInteraction(false)`, and that method is
    /// `Enabled = !UIIsInteractionBlocked()`, so the closing call assigns `Enabled = true` on
    /// KeePass's main window. For KeePass that is harmless, because none of its own callers saves
    /// inside a modal loop. This plugin does, deliberately: a write to a node no open dialog is
    /// holding is allowed, and the write and its save happen together, which is inside whatever
    /// dialog the operator has open at the time.
    ///
    /// **`ShowDialog` disables the owner natively and leaves `Control.Enabled` true.** So the pair
    /// of assignments moves the managed property twice and the window twice, and the second of
    /// them calls `EnableWindow(true)` on a window the modal loop had disabled. From there the
    /// operator can click and type in the main window behind the dialog that is still open: open a
    /// second editor on the entry that dialog is editing, where whichever OK is pressed last
    /// silently wins, move or delete that entry, or close the database out from under it. KeePass
    /// never puts itself in that state, so nothing in it ever puts it back.
    ///
    /// **Natively rather than through `Control.Enabled`**, because the managed property is not what
    /// is wrong. It reads true before the save and true after it, exactly as WinForms left it, and
    /// assigning it again would be a third change to the half that was already right. What is
    /// wrong is the window, which is also what the modal loop itself set, and which that loop sets
    /// back when it ends: that is what makes putting it back here a restoration rather than
    /// something that has to be undone later.
    ///
    /// **And only when it moved.** A save with no dialog open runs on an enabled window and leaves
    /// an enabled window, and disabling one then would take the main window away from an operator
    /// who has nothing open to give it back.
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
    }
}
