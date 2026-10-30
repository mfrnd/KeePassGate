using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// KeePass's "Prevent certain screen captures", applied to this plugin's own windows.
    /// </summary>
    /// <remarks>
    /// KeePass applies the option to every window it registers, as the window's handle is made,
    /// by setting the window's display affinity to the monitor alone. This plugin's windows do not
    /// register: the approval dialogs because a registered dialog holds off every lock, and the rest
    /// because nothing they do needs KeePass to know about them. So the one thing registering
    /// would have given them, this, is applied here, the way KeePass applies it, and the pairing
    /// and renewal dialogs, the question of a day, the chooser and the window under Tools are kept
    /// out of a capture whenever KeePass's own windows are.
    /// </remarks>
    internal static class CaptureGuard
    {
        private const uint MonitorOnly = 0x00000001;

        /// <summary>
        /// Whether KeePass is set to keep its windows out of screen captures, asked as each window
        /// is made. The plugin sets it from KeePass's configuration as it starts, and puts it back
        /// as it ends; a test sets it to say what it is measuring.
        /// </summary>
        internal static Func<bool> Wanted { get; set; } = () => false;

        /// <summary>Keeps a window out of screen captures when KeePass keeps its own out.</summary>
        internal static void Apply(Form form)
        {
            if (form == null || !form.IsHandleCreated)
            {
                return;
            }

            var wanted = Wanted;
            if (wanted != null && wanted())
            {
                // What it returns goes unread, as KeePass leaves it unread for its own windows. It
                // fails only for a window that is not top level or not this process's, and these
                // are both, while the one desktop it leaves unprotected, Windows 7 without
                // composition, can answer success anyway, so reading it would catch nothing. Unlike
                // KeePass this never sets a window back to no affinity, which errs toward keeping
                // the window out.
                NativeMethods.SetWindowDisplayAffinity(form.Handle, MonitorOnly);
            }
        }
    }
}
