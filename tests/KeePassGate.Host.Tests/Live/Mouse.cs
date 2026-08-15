using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Clicks where something is, with a real pointer.
    /// </summary>
    /// <remarks>
    /// For the parts of a window the accessibility tree can find but cannot operate. A grid cell
    /// that opens a dropdown when it is clicked exposes no pattern for being clicked: it is not a
    /// button, it holds no value any more, and what it does is a decision the panel makes about a
    /// mouse. Synthesized input reaches it the same way a person does, which is also the only
    /// version of the path worth proving.
    ///
    /// It moves the actual pointer, so nothing else may be driving the desktop at the same time.
    /// Every fixture that uses this is already <c>NonParallelizable</c> for the same reason.
    /// </remarks>
    internal static class Mouse
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extraInfo);

        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;

        /// <summary>Clicks the middle of whatever the tree says this element occupies.</summary>
        public static void Click(AutomationElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            var box = element.Current.BoundingRectangle;
            if (box.IsEmpty || box.Width <= 0 || box.Height <= 0)
            {
                throw new InvalidOperationException(
                    "The element occupies nothing on screen, so there is nowhere to click it.");
            }

            Click((int)(box.Left + (box.Width / 2)), (int)(box.Top + (box.Height / 2)));
        }

        public static void Click(int x, int y)
        {
            SetCursorPos(x, y);

            // The pause is not politeness. A window that has just been moved to the front is still
            // settling, and a press that lands in the same message as the move is a press some
            // controls never see.
            Thread.Sleep(150);

            mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(300);
        }
    }
}
