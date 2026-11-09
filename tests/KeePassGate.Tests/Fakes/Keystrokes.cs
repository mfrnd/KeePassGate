using System;
using System.Windows.Forms;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>Keys put on a control's queue the way a keyboard would deliver them.</summary>
    public static class Keystrokes
    {
        private const int SystemKeyDownMessage = 0x0104;
        private const int SystemKeyUpMessage = 0x0105;

        /// <summary>The bit of a key's state that holds it down.</summary>
        private const byte Down = 0x80;

        /// <summary>A key's first press with Alt held: one repeat, and the context bit.</summary>
        private const int AltHeld = 0x20000001;

        /// <summary>The same key let go: the context bit, and the two bits of a release.</summary>
        private const int AltReleased = unchecked((int)0xE0000001);

        /// <summary>
        /// Puts Alt+Down on the queue for a control, with Alt held, and lets the loop deliver it.
        /// </summary>
        /// <remarks>
        /// **Held in the thread's own keys rather than carried in the message.** A key with Alt
        /// down arrives as a system key, whose message carries Alt in its context bit, but
        /// WinForms reads every modifier off the thread's keyboard state, and a posted message
        /// changes none of that: posted with the bit and nothing else, the control heard Down
        /// alone. So Alt is set in this thread's state for the delivery and put back after it,
        /// which touches no other thread and no real keyboard.
        /// </remarks>
        public static void AltDown(Control control)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            var keys = new byte[256];
            Assert.That(User32.GetKeyboardState(keys), Is.True, "the thread's keys could not be read");

            var held = (byte[])keys.Clone();
            held[(int)Keys.Menu] |= Down;
            held[(int)Keys.LMenu] |= Down;
            Assert.That(User32.SetKeyboardState(held), Is.True, "Alt could not be held");

            try
            {
                User32.PostMessage(control.Handle, SystemKeyDownMessage, (IntPtr)(int)Keys.Down, (IntPtr)AltHeld);
                User32.PostMessage(control.Handle, SystemKeyUpMessage, (IntPtr)(int)Keys.Down, (IntPtr)AltReleased);
                OffScreenForm.Pump();
            }
            finally
            {
                User32.SetKeyboardState(keys);
            }
        }
    }
}
