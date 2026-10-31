using System;
using System.Runtime.InteropServices;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Every call the plugin makes into Windows itself, each through the method here of the same
    /// name.
    /// </summary>
    /// <remarks>
    /// What WinForms leaves unanswered: whether a window is enabled as Windows sees it, which
    /// window owns another, how a taskbar entry is flashed, and how a window is kept out of screen
    /// captures.
    ///
    /// **Each declaration is called once, by the method of its name in this class**, which is what
    /// the rest of the plugin calls. CodeQL reports every declaration of unmanaged code and every
    /// call to one, and both are answered here and nowhere else: the declaration with why nothing
    /// managed does its job, and its one call as the one there is. A new call into Windows is
    /// therefore a change to this file, with its reason beside it.
    /// </remarks>
    internal static class NativeMethods
    {
        internal static bool IsWindowEnabled(IntPtr window)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the plugin
            return User32.IsWindowEnabled(window);
        }

        internal static bool EnableWindow(IntPtr window, bool enable)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the plugin
            return User32.EnableWindow(window, enable);
        }

        internal static IntPtr GetWindow(IntPtr window, uint relation)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the plugin
            return User32.GetWindow(window, relation);
        }

        internal static IntPtr GetAncestor(IntPtr window, uint relation)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the plugin
            return User32.GetAncestor(window, relation);
        }

        internal static bool FlashWindowEx(ref FlashInfo info)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the plugin
            return User32.FlashWindowEx(ref info);
        }

        internal static bool SetWindowDisplayAffinity(IntPtr window, uint affinity)
        {
            // codeql[cs/call-to-unmanaged-code] the one call to it, for every caller in the plugin
            return User32.SetWindowDisplayAffinity(window, affinity);
        }

        /// <summary>What <see cref="FlashWindowEx"/> is told: which window, how, and how long.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        private static class User32
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] a modal loop disables the window, which `Control.Enabled` never reads
            internal static extern bool IsWindowEnabled(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] `Control.Enabled` would move WinForms' flag as well as the window
            internal static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] `Form.Owner` stays empty when `ShowDialog` picks the owner itself
            internal static extern IntPtr GetWindow(IntPtr window, uint relation);

            [DllImport("user32.dll")]
            // codeql[cs/unmanaged-code] WinForms walks no chain of owners up to its root
            internal static extern IntPtr GetAncestor(IntPtr window, uint relation);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] .NET Framework has no way of its own to flash a taskbar entry
            internal static extern bool FlashWindowEx(ref FlashInfo info);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            // codeql[cs/unmanaged-code] nothing managed sets display affinity, and KeePass makes this same call
            internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        }
    }
}
