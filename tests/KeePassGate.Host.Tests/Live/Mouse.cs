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

        /// <summary>
        /// The pointer moved in physical pixels, whatever this process's DPI awareness, which is
        /// what UI automation's bounding rectangles are in.
        /// </summary>
        /// <remarks>
        /// `SetCursorPos` takes the logical coordinates of the calling thread's awareness, and the
        /// test host declares none, so at any scaling but a hundred percent a press made with it at a
        /// point read off the tree landed somewhere else.
        /// </remarks>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetPhysicalCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extraInfo);

        private const uint LeftDown = 0x0002;
        private const uint LeftUp = 0x0004;
        private const uint RightDown = 0x0008;
        private const uint RightUp = 0x0010;

        /// <summary>
        /// The right button pressed and released in the middle of an element, which is what opens
        /// a menu on it.
        /// </summary>
        public static void RightClick(AutomationElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            var box = element.Current.BoundingRectangle;
            if (box.IsEmpty || box.Width <= 0 || box.Height <= 0)
            {
                throw new InvalidOperationException(
                    "The element occupies nothing on screen, so there is nowhere to press it.");
            }

            SetPhysicalCursorPos((int)(box.Left + (box.Width / 2)), (int)(box.Top + (box.Height / 2)));
            Thread.Sleep(150);

            mouse_event(RightDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            mouse_event(RightUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(300);
        }

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

        /// <summary>
        /// Moves the pointer clear of a rectangle and waits for what was under it to settle.
        /// </summary>
        /// <remarks>
        /// A pointer resting on the control it just clicked is a pointer hovering, and hovering
        /// raises a tooltip. In a screenshot that tooltip covers whatever it is pointing at, which
        /// on the access tab is the column the prose beside the picture is describing. Parking is
        /// cheaper than teaching every caller to move afterwards, and it is the last thing done
        /// before the shutter rather than the first thing after a click, because the tooltip
        /// appears on its own schedule.
        /// </remarks>
        public static void Park(int left, int top, int right, int bottom)
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            var x = right + 40 < screen.Right ? right + 40 : (left - 40 > screen.Left ? left - 40 : screen.Left + 2);
            var y = bottom + 40 < screen.Bottom ? bottom + 40 : (top - 40 > screen.Top ? top - 40 : screen.Top + 2);

            SetCursorPos(x, y);
            Thread.Sleep(500);
        }

        /// <summary>A press at a point in physical pixels, as the accessibility tree gives them.</summary>
        public static void Click(int x, int y)
        {
            SetPhysicalCursorPos(x, y);

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
