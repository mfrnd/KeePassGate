using System;
using System.Runtime.InteropServices;

namespace MonoProbe
{
    /// <summary>
    /// The six `user32` calls in `NativeMethods`, each with the declaration the plugin uses and a
    /// null window, so that on Windows each answers harmlessly about no window at all. What is
    /// measured is what the call does on a runtime with no `user32`: the type of what it throws is
    /// what an `Expected` filter would have to name.
    /// </summary>
    internal static class NativeProbe
    {
        internal static void Run()
        {
            Report.Measure("native.is-window-enabled", () => User32.IsWindowEnabled(IntPtr.Zero));
            Report.Measure("native.enable-window", () => User32.EnableWindow(IntPtr.Zero, true));
            Report.Measure("native.get-window", () => User32.GetWindow(IntPtr.Zero, 4).ToInt64());
            Report.Measure("native.get-ancestor", () => User32.GetAncestor(IntPtr.Zero, 3).ToInt64());
            Report.Measure("native.flash-window-ex", () =>
            {
                var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = IntPtr.Zero };
                return User32.FlashWindowEx(ref info);
            });
            Report.Measure("native.set-window-display-affinity", () => User32.SetWindowDisplayAffinity(IntPtr.Zero, 0));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
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
            internal static extern bool IsWindowEnabled(IntPtr window);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnableWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool enable);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetWindow(IntPtr window, uint relation);

            [DllImport("user32.dll")]
            internal static extern IntPtr GetAncestor(IntPtr window, uint relation);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FlashWindowEx(ref FlashInfo info);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        }
    }
}
