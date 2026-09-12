using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Puts fixed text into the one control whose contents are new on every run and can be written
    /// to from outside, so a regenerated screenshot differs only where the screen did.
    /// </summary>
    /// <remarks>
    /// **What varies is not the plugin's doing.** An invitation code is thirty-two random bytes.
    /// Nothing chose it, and a picture carrying it changes on every regeneration whether or not
    /// anything on screen did, which makes its own diff useless for seeing what changed.
    ///
    /// **Written from outside, with `WM_SETTEXT`.** That is Windows' own behavior on any control
    /// that reads its own window text, present whether or not this fixture uses it, so nothing here
    /// adds a way in that was not already there. A message of the plugin's own for the same purpose
    /// was considered and refused: it would ship in the product, any process running as the user
    /// could send it, and what it would forge is a value on the dialog an operator reads before
    /// letting a program into their database.
    ///
    /// **Only reaches a control that reads its own window text**, which is a much smaller set than
    /// having a handle. A `TextBox` reads it, so the invitation can be replaced. A `Label` paints
    /// from its managed text and never looks, which is the same fact `Redacted` rests on, so the
    /// fingerprint cannot be written from here and is not attempted. It needs nothing: the
    /// authority behind it is a written-down blob in `SeededAuthority`, so what that row says is
    /// the same in every run already.
    ///
    /// The clients table is a `DataGridView`, whose cells are drawn rather than windowed, so its
    /// two timestamps have no handle at all. Fixing those means seeding the database instead, which
    /// is what is done.
    ///
    /// **Put back afterwards, and that is not tidiness.** The flow reads the invitation out of the
    /// box it was just captured in and pairs with it, so a code left replaced is a code the
    /// client cannot use: doing this without restoring took the whole pairing down and, because
    /// nothing was then admitted, made two pictures agree by being equally empty.
    /// </remarks>
    internal static class Steadied
    {
        /// <summary>
        /// What to put where, by the identifier the control carries.
        /// </summary>
        /// <remarks>
        /// One entry, because one control on all the pictures both moves between runs and can be
        /// written to from outside. The invitation is forty-three characters because a token is
        /// thirty-two bytes in base64url, and one of a different length would be a picture of a
        /// code that could not exist.
        /// </remarks>
        private static readonly Dictionary<string, string> Fixed = new Dictionary<string, string>
        {
            { KeePassGate.Ui.GateTab.InvitationBoxName, "DemoInvitationCode-DemoInvitationCode-Demo0" },
        };

        private const uint SetText = 0x000C;
        private const uint GetText = 0x000D;
        private const uint GetTextLength = 0x000E;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wide, string text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(
            IntPtr window, uint message, IntPtr wide, System.Text.StringBuilder text);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wide, IntPtr low);

        /// <summary>One control, and what it said before this touched it.</summary>
        internal sealed class Replaced
        {
            public Replaced(IntPtr handle, string was)
            {
                Handle = handle;
                Was = was;
            }

            public IntPtr Handle { get; private set; }

            public string Was { get; private set; }
        }

        /// <summary>
        /// Replaces whatever this window shows in the places listed above, answering with what it
        /// took away so it can be given back.
        /// </summary>
        public static IList<Replaced> Apply(AutomationElement window)
        {
            var replaced = new List<Replaced>();
            if (window == null)
            {
                return replaced;
            }

            foreach (var wanted in Fixed)
            {
                var handle = HandleOf(window, wanted.Key);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                // **Only where there is something to settle.** This exists to hold a value still,
                // not to supply one: a box the plugin has emptied is saying that there is no
                // invitation outstanding, and writing a code into it publishes a picture of a
                // state the program cannot be in. `Redacted` says the same thing about painting a
                // number in, and it is the case this one nearly became.
                var was = TextOf(handle);
                if (string.IsNullOrEmpty(was))
                {
                    continue;
                }

                replaced.Add(new Replaced(handle, was));
                SendMessage(handle, SetText, IntPtr.Zero, wanted.Value);
            }

            return replaced;
        }

        /// <summary>Gives every control back what it said.</summary>
        public static void Restore(IEnumerable<Replaced> replaced)
        {
            if (replaced == null)
            {
                return;
            }

            foreach (var one in replaced)
            {
                SendMessage(one.Handle, SetText, IntPtr.Zero, one.Was ?? string.Empty);
            }
        }

        /// <summary>What a control says now.</summary>
        private static string TextOf(IntPtr handle)
        {
            var length = SendMessage(handle, GetTextLength, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (length <= 0)
            {
                return string.Empty;
            }

            var built = new System.Text.StringBuilder(length + 1);
            SendMessage(handle, GetText, new IntPtr(built.Capacity), built);
            return built.ToString();
        }

        /// <summary>
        /// The window handle of the control carrying an identifier, or nothing.
        /// </summary>
        /// <remarks>
        /// By identifier rather than by name, for the reason `CONVENTIONS.md` gives: a name is what
        /// a screen reader reads and changes with the wording, and the identifier is
        /// `Control.Name`, which is ours.
        /// </remarks>
        private static IntPtr HandleOf(AutomationElement window, string identifier)
        {
            var found = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, identifier));

            foreach (AutomationElement element in found)
            {
                try
                {
                    var handle = element.Current.NativeWindowHandle;
                    if (handle != 0)
                    {
                        return new IntPtr(handle);
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
            }

            return IntPtr.Zero;
        }
    }
}
