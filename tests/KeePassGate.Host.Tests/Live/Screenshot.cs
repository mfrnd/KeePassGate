using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Writes a picture of a real window to the documentation's image directory.
    /// </summary>
    /// <remarks>
    /// **Only when somebody asked for one.** The directory arrives in an environment variable that
    /// `build.ps1 -Task Screenshots` sets, and without it every fixture that captures is ignored.
    /// A test that writes into the working tree as a side effect of an ordinary run would put a
    /// diff in front of whoever ran it, and the pictures are checked in, so that diff would be a
    /// dozen changed binaries nobody asked for.
    ///
    /// **What is captured is a window rather than the screen.** The file holds the dialog and
    /// nothing around it: no desktop, no wallpaper and no other program's window. That matters
    /// more here than it looks. These images are published, and everything visible in one is
    /// published with it.
    ///
    /// **What is behind the window is measured away rather than captured.** Windows 11 rounds
    /// the corners of a top-level window when it composes the desktop, so a screen copy over the
    /// window's rectangle holds the desktop in those corners. Taking the same window over a
    /// black backdrop and again over a white one gives the background at both ends, and a pixel
    /// composited over a background is `color * alpha + background * (1 - alpha)`, so
    /// `alpha = 1 - (white - black)` and the color is `black / alpha`.
    ///
    /// **Nothing about the shape is written down here**, which is the point: a corner radius
    /// asserted in this file would go quietly wrong the day Windows changed it.
    ///
    /// Two other methods were tried and measured wrong, and a survey of everything else Windows
    /// offers is on `spikes/window-capture` rather than here.
    /// </remarks>
    internal static class Screenshot
    {
        /// <summary>Where the pictures go, set by the build task that asks for them.</summary>
        public const string DirectoryVariable = "KPG_SCREENSHOT_DIR";

        /// <summary>How long a repainted backdrop needs before the screen shows it.</summary>
        private static readonly TimeSpan Repaint = TimeSpan.FromMilliseconds(250);

        /// <summary>`WM_CHANGEUISTATE`, which asks Windows to settle how a window indicates.</summary>
        private const int ChangeUiState = 0x0127;

        /// <summary>`UIS_SET`, meaning turn the named indicator off.</summary>
        private const int SetUiState = 1;

        /// <summary>`UISF_HIDEFOCUS` and `UISF_HIDEACCEL`: the focus rectangle and the
        /// underline under a button's letter, which Windows keeps in one word and shows
        /// separately.</summary>
        private const int KeyboardCues = 0x1 | 0x2;

        /// <summary>
        /// How long to let a window settle before reading the screen. A dialog reports itself to
        /// the accessibility tree before it has finished painting, and a picture taken then has
        /// gray where its controls belong.
        /// </summary>
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(900);

        /// <summary>
        /// The directory to write to, or an ignored test. Every fixture that captures starts here.
        /// </summary>
        public static string DirectoryOrIgnore()
        {
            var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (string.IsNullOrEmpty(directory))
            {
                Assert.Ignore(
                    "Screenshots are only taken when asked for. Run `.\\build.ps1 -Task Screenshots`, " +
                    "which sets " + DirectoryVariable + ".");
            }

            Directory.CreateDirectory(directory);
            return directory;
        }


        /// <summary>
        /// The window's rectangle once it has stopped changing.
        /// </summary>
        /// <remarks>
        /// **A dialog that sizes itself is not its final size when it first appears.** The approval
        /// dialog grows to fit what it has to say, and screenshotting it the moment it is on screen
        /// caught it mid-decision: the picture stopped above the profile check boxes and the
        /// buttons, which are exactly what the prose beside it describes. Waiting for two readings
        /// to agree costs a few hundred milliseconds and is the difference between a screenshot and
        /// a screenshot of half a dialog.
        /// </remarks>
        private static System.Windows.Rect Settled(AutomationElement window, string name)
        {
            var previous = System.Windows.Rect.Empty;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var bounds = window.Current.BoundingRectangle;

                if (!bounds.IsEmpty && bounds.Width >= 1 && bounds.Height >= 1 && bounds == previous)
                {
                    return bounds;
                }

                previous = bounds;
                Thread.Sleep(100);
            }

            if (previous.IsEmpty || previous.Width < 1 || previous.Height < 1)
            {
                throw new InvalidOperationException(
                    "The window called " + name + " reports no rectangle, so it is not on screen.");
            }

            // It kept moving. Screenshotting the last reading is better than not screenshotting it,
            // and the size is written to the log beside the file for whoever compares them.
            return previous;
        }

        /// <summary>
        /// Brings a window to the front, lets it paint, and writes what is on the screen where it
        /// is. Answers with the path written, so a test can assert the file arrived.
        /// </summary>
        /// <param name="parkFocusOn">
        /// Where to leave the keyboard focus before the shutter, or null to leave it alone. See
        /// <see cref="ParkTheFocus"/> for what it is for and why the caller has to name it.
        /// </param>
        /// <param name="moment">
        /// Something on this window that changes on its own, and the moment it will hold still for,
        /// or null if nothing on it moves. See <see cref="ISteadyMoment"/>.
        /// </param>
        public static string Capture(
            string directory,
            AutomationElement window,
            string name,
            AutomationElement parkFocusOn = null,
            ISteadyMoment moment = null)
        {
            var path = Paths.Join(directory, name + ".png");

            Rectangle area;
            using (var picture = Take(window, name, parkFocusOn, moment, out area))
            {
                picture.Save(path, ImageFormat.Png);
            }

            TestContext.Out.WriteLine("wrote " + path + " (" + area.Width + " by " + area.Height + ")");
            return path;
        }

        /// <summary>
        /// The picture <see cref="Capture"/> writes, kept in memory instead, which is what the
        /// frames of the moving picture are composed from.
        /// </summary>
        /// <param name="name">What the picture is of, for the messages a failure gives.</param>
        /// <param name="area">
        /// Where on the screen the picture was taken from, so that a caller can find a control in
        /// it by the rectangle UI automation reports.
        /// </param>
        public static Bitmap Take(
            AutomationElement window,
            string name,
            AutomationElement parkFocusOn,
            ISteadyMoment moment,
            out Rectangle area)
        {
            return Take(window, name, parkFocusOn, moment, Rectangle.Empty, out area);
        }

        /// <summary>
        /// The same, taking in a part of the screen beyond the window as well.
        /// </summary>
        /// <param name="reach">
        /// More of the screen to take, or empty for none: a dropdown the window opened that runs
        /// past its edge, which a picture of the window alone would cut off halfway through its
        /// own rows. The backdrop goes behind all of it, so it is solved like the window is.
        /// </param>
        public static Bitmap Take(
            AutomationElement window,
            string name,
            AutomationElement parkFocusOn,
            ISteadyMoment moment,
            Rectangle reach,
            out Rectangle area)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            Keyboard.BringToFront(window);
            HideKeyboardCues(window);
            ParkTheFocus(parkFocusOn);
            Thread.Sleep(Settle);

            var bounds = Settled(window, name);

            area = new Rectangle(
                (int)Math.Round(bounds.X),
                (int)Math.Round(bounds.Y),
                (int)Math.Round(bounds.Width),
                (int)Math.Round(bounds.Height));

            if (!reach.IsEmpty)
            {
                area = Rectangle.Union(area, reach);
            }

            // Clear of the window before the shutter, so nothing is hovering when it opens. The
            // pointer is not in a screen copy, but what it is over is: a button under it renders
            // in its hover state.
            Mouse.Park(area.Left, area.Top, area.Right, area.Bottom);

            var handle = new IntPtr(window.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "The window for " + name + " has no handle, so nothing can be put behind it.");
            }

            // Only now. Bringing the window forward makes a page describe itself again, and this
            // tab rebuilds its labels from the database when it does, so anything written before
            // that is gone by the time the shutter opens.
            var steadied = Steadied.Apply(window);

            using (var backdrop = new Backdrop(area))
            {
                Bitmap overBlack = null;
                Bitmap overWhite = null;
                try
                {
                    // Up and behind the window before the moment rather than inside it, so what has
                    // to fit in one second is two color changes and two screen copies.
                    backdrop.Prepare(handle);

                    if (moment != null)
                    {
                        moment.WaitForIt();
                    }

                    overBlack = backdrop.Expose(handle, Color.Black, area);
                    overWhite = backdrop.Expose(handle, Color.White, area);

                    // The instant the second copy returns, before anything is taken down or solved:
                    // what this reports on is the window those two exposures were of.
                    if (moment != null)
                    {
                        moment.ConfirmItHeld();
                    }

                    var solved = Solve(overBlack, overWhite);

                    // Last, and over as little as possible: two values that cannot be settled at
                    // the source, covered where the controls holding them actually are.
                    Redacted.Over(solved, area, window);
                    return solved;
                }
                finally
                {
                    if (overBlack != null) { overBlack.Dispose(); }
                    if (overWhite != null) { overWhite.Dispose(); }

                    // Put back before anything else looks: the flow reads the invitation out of the
                    // box it was just captured in, and pairs with it.
                    Steadied.Restore(steadied);
                }
            }
        }

        /// <summary>
        /// Tells the window not to draw keyboard cues, whichever way it was last reached.
        /// </summary>
        /// <remarks>
        /// Windows hides the focus rectangle and the underline under a button's letter until
        /// somebody navigates by keyboard, and shows them from then on. So a page reached by
        /// clicking and the same page reached by typing are different pictures, and which one a
        /// fixture takes is an accident of how it got there: one page differed between two runs by
        /// a dotted rectangle on a single row, and another by an underline eight pixels long.
        ///
        /// `WM_CHANGEUISTATE` is the message that decides it. The system answers by sending
        /// `WM_UPDATEUISTATE` to the window and everything on it, so one message settles the whole
        /// dialog. Hidden is chosen because it is what reaching the page with the mouse looks like,
        /// and because it is where a window starts.
        /// </remarks>
        private static void HideKeyboardCues(AutomationElement window)
        {
            var handle = new IntPtr(window.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var wanted = new IntPtr((KeyboardCues << 16) | SetUiState);
            User32.SendMessage(handle, ChangeUiState, wanted, IntPtr.Zero);
        }

        /// <summary>
        /// Leaves the keyboard focus on a control that draws no caret, so that the picture does not
        /// depend on which half of a blink the shutter caught.
        /// </summary>
        /// <remarks>
        /// A caret is on for half a second and off for half a second, so two exposures a quarter of
        /// a second apart can fall either side of a blink, and the alpha solved from them is then
        /// wrong down one column. It differs between runs for the same reason. Measured, over ten
        /// regenerations: the status window came back in two versions differing by thirteen pixels
        /// in a single column, and by nothing else.
        ///
        /// **Absent rather than hidden, and this reverses hiding it rather than refining it.**
        /// `HideCaret` hides a caret only if a window in the *calling process* owns it, and
        /// `DestroyCaret` destroys one only under the same condition; `AttachThreadInput` shares an
        /// input queue rather than ownership. The caret in these pictures belongs to KeePass and
        /// this runs in the test host, so both were answering without doing anything, and the only
        /// thing they could have reported is the attach, which is the part that worked.
        ///
        /// **The caller names the control, and that is the point.** Asking the window instead was
        /// measured wrong in both directions: on the status window the focused element is a `Pane`
        /// named for the row the spin box sits in, because the spin box publishes no editable
        /// child, so nothing matches and the caret stays; on the two dialogs carrying a grid, a
        /// cell *does* publish as a focused edit, so it matches there and the focus moves in a
        /// picture that was already steady. A capture that guesses changes pictures nobody was
        /// fixing.
        ///
        /// It parks the focus the way <see cref="Mouse.Park"/> parks the pointer, and for the same
        /// reason: what is resting on a control is in the picture of it.
        /// </remarks>
        private static void ParkTheFocus(AutomationElement onto)
        {
            if (onto == null)
            {
                return;
            }

            onto.SetFocus();
            Thread.Sleep(Repaint);
        }

        /// <summary>What the screen holds over one rectangle, with no alpha to speak of.</summary>
        private static Bitmap FromScreen(Rectangle area)
        {
            var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }

        /// <summary>
        /// Recovers the window and its transparency from the two exposures.
        /// </summary>
        /// <remarks>
        /// Solved per channel and averaged, because the three should agree and averaging is what
        /// keeps a rounding error in one of them from showing as a colored fringe.
        ///
        /// Where nothing of the window is left, the color underneath it is meaningless and is
        /// written as zero rather than as whichever backdrop happened to show through.
        /// </remarks>
        private static Bitmap Solve(Bitmap overBlack, Bitmap overWhite)
        {
            var solved = new Bitmap(overBlack.Width, overBlack.Height, PixelFormat.Format32bppArgb);

            for (var y = 0; y < solved.Height; y++)
            {
                for (var x = 0; x < solved.Width; x++)
                {
                    var black = overBlack.GetPixel(x, y);
                    var white = overWhite.GetPixel(x, y);

                    var alpha = (Between(255 - (white.R - black.R))
                        + Between(255 - (white.G - black.G))
                        + Between(255 - (white.B - black.B))) / 3;

                    if (alpha == 0)
                    {
                        solved.SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                        continue;
                    }

                    solved.SetPixel(x, y, Color.FromArgb(
                        alpha,
                        Between(black.R * 255 / alpha),
                        Between(black.G * 255 / alpha),
                        Between(black.B * 255 / alpha)));
                }
            }

            return solved;
        }

        /// <summary>Keeps a solved value inside the range a byte can hold.</summary>
        private static int Between(int value)
        {
            if (value < 0) { return 0; }
            if (value > 255) { return 255; }
            return value;
        }

        /// <summary>
        /// A sheet of one color, put directly behind the window being captured.
        /// </summary>
        /// <remarks>
        /// **It must not take the focus**, because a window that loses it draws a different title
        /// bar, and the picture is meant to be of the window an operator is working in.
        /// `ShowWithoutActivation` and `WS_EX_NOACTIVATE` between them see to that.
        ///
        /// It covers the whole screen the window is on rather than just the window, so that a
        /// shadow reaching past the frame falls on it too.
        /// </remarks>
        private sealed class Backdrop : Form
        {
            private const uint NoSize = 0x0001;
            private const uint NoMove = 0x0002;
            private const uint NoActivate = 0x0010;
            private const int NoActivateStyle = 0x08000000;
            private const int ToolWindowStyle = 0x00000080;

            public Backdrop(Rectangle around)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = Screen.FromRectangle(around).Bounds;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var parameters = base.CreateParams;
                    parameters.ExStyle |= NoActivateStyle | ToolWindowStyle;
                    return parameters;
                }
            }

            /// <summary>
            /// Shows this and puts it directly behind the window, ahead of any exposure.
            /// </summary>
            /// <remarks>
            /// Directly behind rather than merely somewhere below it: anything of KeePass's own
            /// that stayed in between would be captured instead of this.
            ///
            /// Apart from <see cref="Expose"/> because showing a screen-sized form is the slow
            /// half of it, and one picture has a deadline. The approval dialog's countdown ticks
            /// once a second and both exposures have to fall inside the same one, so everything
            /// that can be done before the shutter is done before it.
            /// </remarks>
            public void Prepare(IntPtr window)
            {
                if (!Visible)
                {
                    Show();
                }

                User32.SetWindowPos(Handle, window, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
                Refresh();
                Application.DoEvents();
                Thread.Sleep(Repaint);
            }

            /// <summary>Puts this behind the window in one color, and reads the screen.</summary>
            public Bitmap Expose(IntPtr window, Color color, Rectangle area)
            {
                BackColor = color;

                // Behind it again, which costs almost nothing. Nothing should have come between the
                // two of them, and a picture of whatever did would be worth less than this call.
                User32.SetWindowPos(Handle, window, 0, 0, 0, 0, NoMove | NoSize | NoActivate);

                Refresh();
                Application.DoEvents();
                Thread.Sleep(Repaint);

                return FromScreen(area);
            }
        }
    }
}
