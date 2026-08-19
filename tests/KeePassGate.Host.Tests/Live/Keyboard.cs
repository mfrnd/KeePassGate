using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Drives menus with real keystrokes rather than through the accessibility tree.
    /// </summary>
    /// <remarks>
    /// Two measured reasons for every part of this. Choosing a menu item through the accessibility
    /// tree does not return until the dialog it opens is closed, and while that call is pending
    /// every other automation call against the same process times out, including the one waiting
    /// for the dialog. And <c>SendKeys</c> is not usable from a test runner: on .NET Framework it
    /// delivers through a journal hook, which needs a message loop the runner does not have, so
    /// the keystrokes are swallowed in silence. Synthesizing input directly avoids both. The keys
    /// themselves are read from the accessibility tree rather than written down here, so a
    /// KeePass in another language names its own accelerators.
    ///
    /// **Only an ASCII accelerator survives the conversion, and that is a known limitation.**
    /// `Send` maps the character to a virtual key by casting its upper case form, which is the
    /// identity for A to Z and 0 to 9 and wrong for everything else. `VkKeyScan` is what maps the
    /// rest, and it reports a shift state that has to be pressed alongside. Reading the key from
    /// the tree is therefore necessary, and for a KeePass whose accelerators are not ASCII it is
    /// not sufficient.
    /// </remarks>
    internal static class Keyboard
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

        private const int RestoreWindow = 9;
        private const uint KeyUp = 0x0002;
        private const byte AltKey = 0x12;
        private const byte EscapeKey = 0x1B;
        private const byte ControlKey = 0x11;
        private const byte EnterKey = 0x0D;

        /// <summary>One keystroke, as the accessibility tree describes it.</summary>
        public struct Stroke
        {
            public Stroke(bool withAlt, char key)
            {
                WithAlt = withAlt;
                Key = key;
            }

            public bool WithAlt { get; }

            public char Key { get; }

            public override string ToString()
            {
                return (WithAlt ? "Alt+" : string.Empty) + Key;
            }
        }

        /// <summary>
        /// Turns what the accessibility tree reports, such as "Alt+f" or "d", into a keystroke.
        /// Returns null when there is nothing usable, so the caller can say so plainly.
        /// </summary>
        public static Stroke? ParseAccessKey(string accessKey)
        {
            if (string.IsNullOrEmpty(accessKey))
            {
                return null;
            }

            var withAlt = accessKey.IndexOf("Alt", StringComparison.OrdinalIgnoreCase) >= 0;
            var key = accessKey;

            var plus = key.LastIndexOf('+');
            if (plus >= 0 && plus < key.Length - 1)
            {
                key = key.Substring(plus + 1);
            }

            key = key.Trim();
            return key.Length == 1 ? new Stroke(withAlt, key[0]) : (Stroke?)null;
        }

        /// <summary>
        /// Windows refuses SetForegroundWindow to a process that does not already hold the
        /// foreground, which a test runner does not. Attaching to the current foreground thread's
        /// input queue for the duration lifts that refusal.
        /// </summary>
        /// <returns>True when the window really did come to the front.</returns>
        public static bool BringToFront(AutomationElement window)
        {
            var handle = new IntPtr(window.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            ShowWindow(handle, RestoreWindow);

            var foreground = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
            var target = GetWindowThreadProcessId(handle, IntPtr.Zero);
            var attached = foreground != target && AttachThreadInput(foreground, target, true);

            try
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(foreground, target, false);
                }
            }

            Thread.Sleep(300);
            return GetForegroundWindow() == handle;
        }

        /// <summary>
        /// Brings the window to the front, and fails the test when it will not come.
        /// </summary>
        /// <remarks>
        /// **For whatever types next.** A synthesized keystroke goes to whichever window holds the
        /// foreground, so one sent after <see cref="BringToFront"/> came back false lands in
        /// whatever took it: Ctrl+S saves somebody else's document and Enter answers somebody
        /// else's question, and the test then fails far from the cause, on a symptom that points
        /// at the plugin. The tier asks for a desktop left alone, and this is where one that was
        /// not is said rather than typed into.
        ///
        /// Asked three times before failing, because a window that has only just opened is
        /// sometimes refused the foreground once. That narrows the race and does not close it:
        /// something can still take the foreground between this and the keystroke.
        /// </remarks>
        public static void Front(AutomationElement window)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (BringToFront(window))
                {
                    return;
                }
            }

            Assert.Fail("'" + NameOf(window) + "' could not be brought to the front, so nothing was typed. " +
                        "In front instead: '" + TitleOf(GetForegroundWindow()) + "'.");
        }

        private static string NameOf(AutomationElement window)
        {
            try
            {
                return window.Current.Name;
            }
            catch (ElementNotAvailableException)
            {
                return "a window that has gone";
            }
        }

        private static string TitleOf(IntPtr window)
        {
            var text = new StringBuilder(512);
            return window == IntPtr.Zero || GetWindowText(window, text, text.Capacity) <= 0
                ? string.Empty
                : text.ToString();
        }

        public static void Send(Stroke stroke)
        {
            var key = (byte)char.ToUpperInvariant(stroke.Key);

            if (stroke.WithAlt)
            {
                keybd_event(AltKey, 0, 0, UIntPtr.Zero);
            }

            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyUp, UIntPtr.Zero);

            if (stroke.WithAlt)
            {
                keybd_event(AltKey, 0, KeyUp, UIntPtr.Zero);
            }

            Thread.Sleep(400);
        }

        /// <summary>A keystroke with control held, such as the save shortcut.</summary>
        public static void SendControl(char key)
        {
            var code = (byte)char.ToUpperInvariant(key);

            keybd_event(ControlKey, 0, 0, UIntPtr.Zero);
            keybd_event(code, 0, 0, UIntPtr.Zero);
            keybd_event(code, 0, KeyUp, UIntPtr.Zero);
            keybd_event(ControlKey, 0, KeyUp, UIntPtr.Zero);
            Thread.Sleep(400);
        }

        public static void SendEscape()
        {
            keybd_event(EscapeKey, 0, 0, UIntPtr.Zero);
            keybd_event(EscapeKey, 0, KeyUp, UIntPtr.Zero);
            Thread.Sleep(400);
        }

        /// <summary>
        /// Enter, which is how a dropdown is accepted, and how a file dialog opens what its name box
        /// holds.
        /// </summary>
        /// <remarks>
        /// Only ever with one of the plugin's own pickers open, or with the platform's file dialog
        /// in front. On a dialog with nothing open it is that dialog's OK, which is the whole reason
        /// the picker takes the key away while it is showing, and is exactly what the file dialog
        /// is pressed for.
        /// </remarks>
        public static void SendEnter()
        {
            keybd_event(EnterKey, 0, 0, UIntPtr.Zero);
            keybd_event(EnterKey, 0, KeyUp, UIntPtr.Zero);
            Thread.Sleep(400);
        }
    }
}
