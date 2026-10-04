using System;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The Approve button on the pairing and renewal dialogs, which no stray key presses.
    /// </summary>
    /// <remarks>
    /// <see cref="ApprovalDialog"/> says why. Three things an ordinary button does are taken away:
    /// a mnemonic, which a bare letter fires whenever a button has the focus, Alt or not; being
    /// drawn as the default button once it has the focus; and the form's Enter, which the dialog
    /// answers itself. What is left is a click, and Space while it has the focus.
    /// </remarks>
    public sealed class ApproveButton : Button
    {
        public ApproveButton()
        {
            Name = GateTab.ApproveButtonName;
            Text = GateTab.ApproveButtonText;

            // An ampersand in the text is then an ampersand, so a caption changed later cannot
            // bring a mnemonic back.
            UseMnemonic = false;
        }

        /// <summary>Never drawn as the button Enter presses, because it never is.</summary>
        public override void NotifyDefault(bool value)
        {
            base.NotifyDefault(false);
        }

        /// <summary>
        /// Asked as a press arrives: true while that press is the one that closed a menu on the
        /// same dialog, which ends the menu and does nothing else.
        /// </summary>
        internal Func<bool> ClosingAMenu { get; set; }

        /// <summary>
        /// A press that closed a menu does not go on to press this, which it would otherwise do:
        /// WinForms closes an open menu on a press outside it and then delivers the press to
        /// whatever it landed on, so one click meant to put a menu away approved.
        /// </summary>
        // The parameter keeps the name WinForms declares, which CA1725 holds this to.
        // cspell:ignore mevent
        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (ClosingAMenu != null && ClosingAMenu())
            {
                return;
            }

            base.OnMouseDown(mevent);
        }

        /// <summary>No letter presses it, with Alt or without.</summary>
        protected override bool ProcessMnemonic(char charCode)
        {
            return false;
        }
    }
}
