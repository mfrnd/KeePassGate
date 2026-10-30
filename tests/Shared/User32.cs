using System;
using System.Runtime.InteropServices;
using System.Text;

namespace KeePassGate.Testing
{
    /// <summary>
    /// Every call the tests make into Windows itself, each through the method here of the same
    /// name.
    /// </summary>
    /// <remarks>
    /// What the tests ask Windows directly is what WinForms and UI automation either do not offer
    /// or offer only as their own account of it: a key posted to a window's queue, the focus and
    /// the activation as Windows holds them, the windows of another process, a real pointer.
    ///
    /// **Each declaration is called once, by the method of its name in this class**, which is what
    /// every test calls. CodeQL reports every declaration of unmanaged code and every call to one,
    /// and both are answered here and nowhere else: the declaration with why nothing managed does
    /// its job, and its one call as the one there is. Not `NativeMethods`, which is the plugin's
    /// own class of the kind, and a test that imports the plugin's `KeePassGate.Ui` would find
    /// two of them.
    /// </remarks>
    internal static class User32
    {
        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        internal static bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.AttachThreadInput(attachTo, attachFrom, attach);
        }

        internal static bool BringWindowToTop(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.BringWindowToTop(window);
        }

        internal static bool EnumWindows(EnumWindowsProc callback, IntPtr parameter)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.EnumWindows(callback, parameter);
        }

        internal static IntPtr GetActiveWindow()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetActiveWindow();
        }

        internal static IntPtr GetFocus()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetFocus();
        }

        internal static IntPtr GetForegroundWindow()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetForegroundWindow();
        }

        internal static bool GetWindowDisplayAffinity(IntPtr window, out uint affinity)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetWindowDisplayAffinity(window, out affinity);
        }

        internal static int GetWindowLong(IntPtr window, int index)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetWindowLong(window, index);
        }

        internal static int GetWindowText(IntPtr window, StringBuilder text, int count)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetWindowText(window, text, count);
        }

        internal static uint GetWindowThreadProcessId(IntPtr window, out uint processId)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.GetWindowThreadProcessId(window, out processId);
        }

        internal static bool IsWindowEnabled(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.IsWindowEnabled(window);
        }

        internal static bool IsWindowVisible(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.IsWindowVisible(window);
        }

        internal static void keybd_event(byte key, byte scanCode, uint flags, UIntPtr extraInfo)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            Imported.keybd_event(key, scanCode, flags, extraInfo);
        }

        internal static void mouse_event(uint flags, int x, int y, uint data, UIntPtr extraInfo)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            Imported.mouse_event(flags, x, y, data, extraInfo);
        }

        internal static bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr low)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.PostMessage(window, message, wide, low);
        }

        internal static bool PrintWindow(IntPtr window, IntPtr canvas, uint flags)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.PrintWindow(window, canvas, flags);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr low)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SendMessage(window, message, wide, low);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, string text)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SendMessage(window, message, wide, text);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, StringBuilder text)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SendMessage(window, message, wide, text);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref Rect rect)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SendMessage(window, message, wide, ref rect);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref ToolInfo tool)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SendMessage(window, message, wide, ref tool);
        }

        internal static IntPtr SetActiveWindow(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SetActiveWindow(window);
        }

        internal static bool SetForegroundWindow(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SetForegroundWindow(window);
        }

        /// <summary>
        /// The pointer moved in physical pixels, whatever this process's DPI awareness, which is
        /// what UI automation's bounding rectangles are in.
        /// </summary>
        /// <remarks>
        /// `SetCursorPos` takes the logical coordinates of the calling thread's awareness, and the
        /// test host declares none, so at any scaling but a hundred percent a press made with it at a
        /// point read off the tree landed somewhere else.
        /// </remarks>
        internal static bool SetPhysicalCursorPos(int x, int y)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SetPhysicalCursorPos(x, y);
        }

        internal static bool SetWindowPos(
            IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.SetWindowPos(window, after, x, y, width, height, flags);
        }

        internal static bool ShowWindow(IntPtr window, int command)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Imported.ShowWindow(window, command);
        }

        /// <summary>`RECT`, the four edges of a rectangle in pixels.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>One tool of a tooltip control, as the control describes it.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ToolInfo
        {
            public int Size;
            public int Flags;
            public IntPtr Window;
            public IntPtr Id;
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public IntPtr Instance;
            public IntPtr Text;
            public IntPtr Parameter;
        }

        private static class Imported
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed joins one thread's input to another's
            internal static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed raises another process's window
            internal static extern bool BringWindowToTop(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] the window manager's list, where UI automation is a second opinion
            internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] `Form.ActiveForm` answers for the foreground, not for this thread
            internal static extern IntPtr GetActiveWindow();

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] the focus as Windows holds it, which `ActiveControl` only follows
            internal static extern IntPtr GetFocus();

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] nothing managed names the desktop's foreground window
            internal static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] display affinity has no managed reader
            internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] WinForms offers a window's style only as it is made, not as it is
            internal static extern int GetWindowLong(IntPtr window, int index);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            // codeql[cs/unmanaged-code] nothing managed reads the title of another process's window
            internal static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] nothing managed names the thread or the process owning a window
            internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] a modal loop disables the window, which `Control.Enabled` never reads
            internal static extern bool IsWindowEnabled(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed reads whether another process's window is visible
            internal static extern bool IsWindowVisible(IntPtr window);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] `SendKeys` needs a message loop, which a test runner does not have
            internal static extern void keybd_event(byte key, byte scanCode, uint flags, UIntPtr extraInfo);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] nothing managed presses a real mouse button
            internal static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extraInfo);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] a posted message meets the loop's own handling, as real input does
            internal static extern bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr low);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] a window painted by Windows, where `DrawToBitmap` leaves holes
            internal static extern bool PrintWindow(IntPtr window, IntPtr canvas, uint flags);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            // codeql[cs/unmanaged-code] .NET Framework has no public way to send a window a message
            internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr low);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            // codeql[cs/unmanaged-code] .NET Framework has no public way to send a window a message
            internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, string text);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            // codeql[cs/unmanaged-code] .NET Framework has no public way to send a window a message
            internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, StringBuilder text);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            // codeql[cs/unmanaged-code] .NET Framework has no public way to send a window a message
            internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref Rect rect);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            // codeql[cs/unmanaged-code] .NET Framework has no public way to send a window a message
            internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref ToolInfo tool);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] `Form.Activate` takes the foreground as well, and this does not
            internal static extern IntPtr SetActiveWindow(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed brings another process's window to the front
            internal static extern bool SetForegroundWindow(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] physical pixels, which `Cursor.Position` does not take
            internal static extern bool SetPhysicalCursorPos(int x, int y);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed puts a window directly behind another
            internal static extern bool SetWindowPos(
                IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed restores another process's window
            internal static extern bool ShowWindow(IntPtr window, int command);
        }
    }
}
