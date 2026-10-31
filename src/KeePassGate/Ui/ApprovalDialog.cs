using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the pairing dialog and the renewal dialog share about being raised by somebody else's
    /// program: neither takes the focus, and no key the operator meant for something else answers
    /// one with a yes.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §303 and §312. A client chooses when either dialog comes up, and the operator
    /// may be typing into KeePass at that moment, which is the whole hazard: a dialog that takes
    /// the focus takes the next keystroke too, and a Tab, a word and an Enter meant for an entry's
    /// password field were measured approving a renewal with every profile checked. The operator's
    /// ruling is that taking the focus is the most severe part of that, because it is what lets
    /// the typing land on the dialog at all.
    ///
    /// - **It comes up on top without being activated**, and KeePass's taskbar entry flashes until
    ///   the operator activates it, by clicking it or the taskbar. `TopMost` is not set, because
    ///   showing a form that has it focuses the form's first control, and focusing a window of this
    ///   thread activates it; the window is made topmost by its style instead.
    /// - **Enter and Escape refuse**, wherever the focus is on it, unless an open list or an open
    ///   menu takes them first, as a list or a menu does everywhere. A focused button would
    ///   otherwise become the default button and take Enter for itself.
    /// - **Approve is <see cref="ApproveButton"/>**: no mnemonic, never the default button, and
    ///   after Refuse in the tab order. A click or Space on it while it has the focus is what
    ///   approves, and nothing else does.
    /// </remarks>
    public abstract class ApprovalDialog : Form
    {
        private const int TopmostStyle = 0x00000008;

        /// <summary>The window whose taskbar entry is flashing, or nothing.</summary>
        private IntPtr _flashing;

        /// <summary>
        /// The window this dialog last asked the taskbar to flash, so a test can see which.
        /// </summary>
        internal IntPtr Flashed { get; private set; }

        /// <summary>Shown without taking the activation from whatever has it.</summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        /// <summary>Topmost by its style rather than by <see cref="Form.TopMost"/>, as the remarks say.</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= TopmostStyle;
                return parameters;
            }
        }

        /// <summary>Refuses, as Enter and Escape do; what refusing means is the dialog's own.</summary>
        protected abstract void RefuseByKey();

        /// <summary>
        /// Closes the dialog as unanswered because KeePass locked, unless it was answered first.
        /// </summary>
        /// <remarks>
        /// On KeePass's thread, from inside this dialog's own modal loop, which is where a lock
        /// arrives while it stands: the loop ends once the lock's handler returns. `PAIRING.md`
        /// §308 says what the client is told.
        /// </remarks>
        internal abstract void Withdraw();

        /// <summary>
        /// Enter and Escape refuse, with Shift or without it, whatever has the focus.
        /// </summary>
        /// <remarks>
        /// Asked only once a control has declined the key, so an open list still picks with Enter
        /// and closes with Escape. What this takes away is the form's own handling, which presses
        /// the default button, and the default button is whichever button has the focus.
        /// </remarks>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & (Keys.Alt | Keys.Control)) == Keys.None)
            {
                var key = keyData & Keys.KeyCode;
                if (key == Keys.Enter || key == Keys.Escape)
                {
                    RefuseByKey();
                    return true;
                }
            }

            return base.ProcessDialogKey(keyData);
        }

        /// <summary>Kept out of screen captures when KeePass keeps its own windows out.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CaptureGuard.Apply(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // KeePass's entry rather than this dialog's own, because KeePass is what the operator
            // is looking for on the taskbar: the root of whatever owns this, which `ShowDialog`
            // with no owner makes the window that was active. A dialog with no owner flashes its
            // own entry.
            var owner = NativeMethods.GetWindow(Handle, OwnerWindow);
            var root = owner == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(owner, RootOwner);
            Flash(root == IntPtr.Zero ? Handle : root);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            StopFlashing();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopFlashing();
            base.OnFormClosed(e);
        }

        private void Flash(IntPtr window)
        {
            _flashing = window;
            Flashed = window;
            Send(window, FlashTray | FlashUntilStopped);
        }

        private void StopFlashing()
        {
            if (_flashing != IntPtr.Zero)
            {
                Send(_flashing, FlashStop);
                _flashing = IntPtr.Zero;
            }
        }

        private static void Send(IntPtr window, uint flags)
        {
            var info = new NativeMethods.FlashInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.FlashInfo>(),
                Window = window,
                Flags = flags,
                Count = 0,
                Timeout = 0
            };

            NativeMethods.FlashWindowEx(ref info);
        }

        private const uint FlashStop = 0;
        private const uint FlashTray = 2;
        private const uint FlashUntilStopped = 4;
        private const uint OwnerWindow = 4;
        private const uint RootOwner = 3;
    }
}
