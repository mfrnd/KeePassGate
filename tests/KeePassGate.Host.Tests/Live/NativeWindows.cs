using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Finds a process's windows through the window manager rather than through the accessibility
    /// tree. Used to locate windows and to report what was on screen when something did not
    /// appear, because the accessibility tree is a second opinion and a failure message wants the
    /// first.
    /// </summary>
    internal static class NativeWindows
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        public sealed class Window
        {
            public Window(IntPtr handle, string title)
            {
                Handle = handle;
                Title = title;
            }

            public IntPtr Handle { get; }

            public string Title { get; }
        }

        /// <summary>Every visible, titled top level window the process owns.</summary>
        public static List<Window> Visible(Process process)
        {
            return Enumerate(process, untitled: false);
        }

        /// <summary>
        /// Every visible top level window the process owns, the untitled ones as well.
        /// </summary>
        /// <remarks>
        /// For finding a window by its identifier rather than by its title. A drop-down menu is a top
        /// level window with no title, so a search among the titled ones never finds it: the renewal
        /// dialog's Refuse menu was open on screen while the search for it reported nothing there.
        /// </remarks>
        public static List<Window> VisibleUntitledToo(Process process)
        {
            return Enumerate(process, untitled: true);
        }

        private static List<Window> Enumerate(Process process, bool untitled)
        {
            var found = new List<Window>();
            var wanted = (uint)process.Id;

            EnumWindows((window, parameter) =>
            {
                uint owner;

                // A window that went away between being enumerated and being asked about answers
                // with a thread of zero, and belongs to nobody rather than to us.
                if (GetWindowThreadProcessId(window, out owner) == 0 ||
                    owner != wanted || !IsWindowVisible(window))
                {
                    return true;
                }

                var text = new StringBuilder(512);
                var length = GetWindowText(window, text, text.Capacity);
                var title = length <= 0 ? string.Empty : text.ToString();
                if (untitled || title.Length > 0)
                {
                    found.Add(new Window(window, title));
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        public static Window Find(Process process, Func<string, bool> titleMatches)
        {
            foreach (var window in Visible(process))
            {
                if (titleMatches(window.Title))
                {
                    return window;
                }
            }

            return null;
        }

        public static string Titles(Process process)
        {
            var titles = Visible(process).ConvertAll(w => "'" + w.Title + "'");
            return titles.Count == 0 ? "(none)" : string.Join(", ", titles.ToArray());
        }
    }
}
