using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KeePassGate.Testing
{
    /// <summary>
    /// Every call the tests make into Windows itself, each through the method here of the same
    /// name.
    /// </summary>
    /// <remarks>
    /// What the tests ask Windows directly is what WinForms, UI automation and .NET Framework either
    /// do not offer or offer only as their own account of it: a key posted to a window's queue, the
    /// focus and the activation as Windows holds them, the windows of another process, a real
    /// pointer, where the shell drew a window's icon in the notification area, the desktop a thread
    /// is on and the window station holding it, a desktop a test makes for itself, and the backup
    /// and restore privileges enabled on a copy of the process's token.
    ///
    /// **Each declaration is called once, by the method of its name in this class**, which is what
    /// every test calls, and is declared in the private class of the DLL it comes from. CodeQL
    /// reports every declaration of unmanaged code and every call to one, and both are answered
    /// here and nowhere else: the declaration with why nothing managed does its job, and its one
    /// call as the one there is. Not `NativeMethods`, which is the plugin's own class of the kind,
    /// and a test that imports the plugin's `KeePassGate.Ui` would find two of them.
    /// </remarks>
    internal static class TestNativeMethods
    {
        internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        internal static bool AdjustTokenPrivileges(
            SafeAccessTokenHandle token, bool disableAll, ref TokenPrivilege state, int length,
            IntPtr previous, IntPtr returned)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Advapi32.AdjustTokenPrivileges(token, disableAll, ref state, length, previous, returned);
        }

        internal static bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.AttachThreadInput(attachTo, attachFrom, attach);
        }

        internal static bool BringWindowToTop(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.BringWindowToTop(window);
        }

        internal static bool CloseDesktop(IntPtr desktop)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.CloseDesktop(desktop);
        }

        internal static IntPtr CreateDesktop(
            string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.CreateDesktop(name, device, mode, flags, access, attributes);
        }

        internal static bool DuplicateTokenEx(
            SafeAccessTokenHandle existing, uint access, IntPtr attributes, int level, int type,
            out SafeAccessTokenHandle copy)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Advapi32.DuplicateTokenEx(existing, access, attributes, level, type, out copy);
        }

        internal static bool EnumWindows(EnumWindowsProc callback, IntPtr parameter)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.EnumWindows(callback, parameter);
        }

        internal static IntPtr GetActiveWindow()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetActiveWindow();
        }

        internal static uint GetCurrentThreadId()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Kernel32.GetCurrentThreadId();
        }

        internal static IntPtr GetFocus()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetFocus();
        }

        internal static IntPtr GetForegroundWindow()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetForegroundWindow();
        }

        internal static bool GetKeyboardState(byte[] keys)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetKeyboardState(keys);
        }

        internal static IntPtr GetProcessWindowStation()
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetProcessWindowStation();
        }

        internal static IntPtr GetThreadDesktop(uint thread)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetThreadDesktop(thread);
        }

        internal static bool GetUserObjectInformation(
            IntPtr handle, int index, char[] information, int length, out int needed)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetUserObjectInformation(handle, index, information, length, out needed);
        }

        internal static bool GetWindowDisplayAffinity(IntPtr window, out uint affinity)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetWindowDisplayAffinity(window, out affinity);
        }

        internal static int GetWindowLong(IntPtr window, int index)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetWindowLong(window, index);
        }

        internal static bool GetWindowRect(IntPtr window, out Rect rect)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetWindowRect(window, out rect);
        }

        internal static int GetWindowText(IntPtr window, StringBuilder text, int count)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetWindowText(window, text, count);
        }

        internal static uint GetWindowThreadProcessId(IntPtr window, out uint processId)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.GetWindowThreadProcessId(window, out processId);
        }

        internal static bool IsWindowEnabled(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.IsWindowEnabled(window);
        }

        internal static bool IsWindowVisible(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.IsWindowVisible(window);
        }

        internal static void keybd_event(byte key, byte scanCode, uint flags, UIntPtr extraInfo)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            User32.keybd_event(key, scanCode, flags, extraInfo);
        }

        internal static bool LookupPrivilegeValue(string system, string name, out long identifier)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Advapi32.LookupPrivilegeValue(system, name, out identifier);
        }

        internal static void mouse_event(uint flags, int x, int y, uint data, UIntPtr extraInfo)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            User32.mouse_event(flags, x, y, data, extraInfo);
        }

        internal static bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr low)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.PostMessage(window, message, wide, low);
        }

        internal static bool PrintWindow(IntPtr window, IntPtr canvas, uint flags)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.PrintWindow(window, canvas, flags);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr low)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SendMessage(window, message, wide, low);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, string text)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SendMessage(window, message, wide, text);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, StringBuilder text)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SendMessage(window, message, wide, text);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref Rect rect)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SendMessage(window, message, wide, ref rect);
        }

        internal static IntPtr SendMessage(IntPtr window, int message, IntPtr wide, ref ToolInfo tool)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SendMessage(window, message, wide, ref tool);
        }

        internal static IntPtr SetActiveWindow(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SetActiveWindow(window);
        }

        internal static bool SetForegroundWindow(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SetForegroundWindow(window);
        }

        internal static bool SetKeyboardState(byte[] keys)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SetKeyboardState(keys);
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
            return User32.SetPhysicalCursorPos(x, y);
        }

        internal static bool SetThreadDesktop(IntPtr desktop)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SetThreadDesktop(desktop);
        }

        internal static bool SetWindowPos(
            IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.SetWindowPos(window, after, x, y, width, height, flags);
        }

        internal static int Shell_NotifyIconGetRect(
            ref NotifyIconIdentifier identifier, out Rect where)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return Shell32.Shell_NotifyIconGetRect(ref identifier, out where);
        }

        internal static bool ShowWindow(IntPtr window, int command)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the tests
            return User32.ShowWindow(window, command);
        }

        /// <summary>
        /// An icon in the notification area as `Shell_NotifyIconGetRect` takes it: by the window
        /// that put it there and the number that window gave it.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct NotifyIconIdentifier
        {
            public uint Size;
            public IntPtr Window;
            public uint Id;
            public Guid Item;
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

        /// <summary>A `TOKEN_PRIVILEGES` holding one privilege, its identifier and what to do with it.</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct TokenPrivilege
        {
            public uint Count;
            public long Identifier;
            public uint Attributes;
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

        private static class User32
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
            // codeql[cs/unmanaged-code] a desktop handle has no managed owner to close it
            internal static extern bool CloseDesktop(IntPtr desktop);

            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            // codeql[cs/unmanaged-code] nothing managed makes a desktop
            internal static extern IntPtr CreateDesktop(
                string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);

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
            // codeql[cs/unmanaged-code] the thread's keys as Windows holds them, to put back after a test
            internal static extern bool GetKeyboardState(byte[] keys);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] nothing managed names the window station a process is in
            internal static extern IntPtr GetProcessWindowStation();

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] nothing managed names the desktop a thread is on
            internal static extern IntPtr GetThreadDesktop(uint thread);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed reads the name of a desktop or a window station
            internal static extern bool GetUserObjectInformation(
                IntPtr handle, int index, [Out] char[] information, int length, out int needed);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] display affinity has no managed reader
            internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] WinForms offers a window's style only as it is made, not as it is
            internal static extern int GetWindowLong(IntPtr window, int index);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed reads where a window that is not a control stands
            internal static extern bool GetWindowRect(IntPtr window, out Rect rect);

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
            // codeql[cs/unmanaged-code] a posted key carries no Alt, which only the thread's own keys hold
            internal static extern bool SetKeyboardState(byte[] keys);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] physical pixels, which `Cursor.Position` does not take
            internal static extern bool SetPhysicalCursorPos(int x, int y);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed moves a thread to another desktop
            internal static extern bool SetThreadDesktop(IntPtr desktop);

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

        private static class Kernel32
        {
            [DllImport("kernel32.dll")]
            // codeql[cs/unmanaged-code] `ManagedThreadId` is the runtime's number, not the identifier Windows asks for
            internal static extern uint GetCurrentThreadId();
        }

        private static class Shell32
        {
            [DllImport("shell32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            // codeql[cs/unmanaged-code] nothing managed asks the shell where a tray icon is
            internal static extern int Shell_NotifyIconGetRect(
                ref NotifyIconIdentifier identifier, out Rect where);
        }

        // cspell:ignore advapi
        private static class Advapi32
        {
            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed copies a token rather than its handle
            internal static extern bool DuplicateTokenEx(
                SafeAccessTokenHandle existing, uint access, IntPtr attributes, int level, int type,
                out SafeAccessTokenHandle copy);

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed names a privilege's identifier
            internal static extern bool LookupPrivilegeValue(string system, string name, out long identifier);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed enables a privilege
            internal static extern bool AdjustTokenPrivileges(
                SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
                ref TokenPrivilege state, int length, IntPtr previous, IntPtr returned);
        }
    }
}
