using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using KeePassGate.Testing;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// Renders one surface in one state and writes it out, for somebody to look at.
    /// </summary>
    /// <remarks>
    /// **These pictures are not assertions and nothing compares them to anything.** What they are
    /// for is the class of defect no assertion in this repository has ever caught: a label wrapping
    /// one word early, a column padded out around a heading, a list that has quietly grown a scroll
    /// bar, a warning band that costs a line when it has nothing to say. Every one of those was
    /// found by looking at a screenshot, and every one of them was invisible from the code.
    ///
    /// So the gallery screenshots each surface at its own logic boundaries: the states where the
    /// layout is a different shape rather than the same shape with different words in it. Empty and
    /// full, warning and no warning, one row and more rows than fit.
    ///
    /// **Drawn rather than screenshotted.** `Control.DrawToBitmap` paints the control into a bitmap
    /// of its own, so this needs a window station but not a monitor, and it does not care what is in
    /// front of what: the documentation screenshots drive a real KeePass across a real desktop and
    /// come out black when nothing is attached to it. This does not.
    ///
    /// **The output is an artifact and is ignored.** It is rewritten on every run, so there is
    /// never a stale picture to mistake for a current one, and nothing about it belongs in the
    /// history: `docs/images` is the set that is committed, and it is committed because those are
    /// published.
    /// </remarks>
    internal static class Gallery
    {
        /// <summary>
        /// One name for the fixtures that draw rather than assert.
        /// </summary>
        /// <remarks>
        /// Named so that a run can leave them out, not so that it has to. They need no KeePass, no
        /// desktop and no network, and they take about as long as the tests around them, so the
        /// ordinary run takes them: a picture nobody generates is a picture nobody looks at.
        /// </remarks>
        public const string Category = "Gallery";

        /// <summary>
        /// The database settings tab as KeePass gives it, which is what the three pages have to fit.
        /// </summary>
        /// <remarks>
        /// Measured against the real dialog by `GateTabFitsTests`, which is where the number is
        /// asserted; here it is only what the picture is rendered at, so that what a reviewer sees
        /// is what an operator gets rather than whatever a form defaulted to.
        ///
        /// **One size for each dialog a page is on and no other, and that is a decision rather than
        /// a gap.** There was a further one, deliberately too small, for the states that are about
        /// running out of room. It was answering a question nobody can ask: every one of these
        /// pages is on a KeePass dialog, and a KeePass dialog has a minimum size it will not go
        /// under. A picture of a page at a width no operator can produce is a defect report nobody
        /// can act on, and it costs a reviewer the same attention as a real one.
        /// </remarks>
        public static readonly Size SettingsPage = new Size(449, 314);

        /// <summary>
        /// The group dialog's page with the plugin's tab on it, which is the least room the grant
        /// tab is ever given: the entry dialog's is larger.
        /// </summary>
        /// <remarks>
        /// Measured against the real dialog by `GrantTabFitsTests`, which prints it. The grant
        /// pictures were drawn at the settings page's room until it was measured that this is 143
        /// pixels shorter, so they showed more table than any group dialog leaves in exactly the
        /// states they are drawn for.
        /// </remarks>
        public static readonly Size GroupPage = new Size(356, 171);

        private static readonly object Lock = new object();

        private static string _directory;

        /// <summary>
        /// Where the pictures go: `KPG_GALLERY_DIR`, or `.gallery` beside the solution.
        /// </summary>
        /// <remarks>
        /// Emptied once per run rather than per fixture, so a picture that stops being taken stops
        /// being there. A directory of images from three different runs is worse than no directory,
        /// because nothing on it says which is which.
        /// </remarks>
        public static string Directory
        {
            get
            {
                lock (Lock)
                {
                    if (_directory == null)
                    {
                        _directory = Prepare();
                    }

                    return _directory;
                }
            }
        }

        /// <summary>Renders a control at a size and writes it out under this name.</summary>
        public static void Shot(Control control, Size size, string name)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            using (var host = OffScreen(size, name))
            {
                control.Dock = DockStyle.Fill;
                host.Controls.Add(control);

                // Taken back out whatever happens below, because the control belongs to the caller
                // and the host does not. Leaving it parented means the host's own `Dispose` takes
                // the caller's control down with it, so a fixture whose picture failed to render
                // would go on to fail again, differently, on a control that had been disposed
                // underneath it. The second failure is the one that gets reported.
                try
                {
                    host.Show();

                    // Let the layout settle. A grid sizes its columns and a flow panel its rows
                    // on the messages that follow being shown, and a bitmap taken before those are
                    // delivered is a picture of the state this is meant to be past.
                    //
                    // Twice, with a layout forced between, because one round is not enough for a
                    // grid that sizes its rows to their contents: the heights are computed against
                    // whatever width the grid had when it was filled, and the columns take their
                    // share of the real width only once it has one. The second round is where the
                    // scroll bar it needs turns up.
                    Settle();
                    host.PerformLayout();
                    Settle();
                    host.Refresh();
                    Settle();

                    // The whole window, frame and caption included, rather than the client area
                    // inside it. What is under review is a surface that lives in a window, and a
                    // rectangle cut out of one is read as though it had the room right up to its
                    // own edge: the border and the caption are what say where the room stops.
                    using (var bitmap = Paint(host, host.Size))
                    {
                        Write(bitmap, name);
                    }
                }
                finally
                {
                    host.Controls.Remove(control);
                }

                host.Close();
            }
        }

        /// <summary>Renders a form that owns its own size, such as a dialog.</summary>
        public static void Shot(Form form, string name)
        {
            if (form == null)
            {
                throw new ArgumentNullException(nameof(form));
            }

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            form.ShowInTaskbar = false;
            form.Show();
            Settle();

            using (var bitmap = Paint(form, form.Size))
            {
                Write(bitmap, name);
            }

            form.Hide();
        }

        /// <summary>Lets the message loop deliver what a layout is waiting on.</summary>
        public static void Settle()
        {
            for (var i = 0; i < 12; i++)
            {
                Application.DoEvents();
            }
        }

        /// <summary>
        /// Paints a window into a bitmap, asking Windows rather than WinForms.
        /// </summary>
        /// <remarks>
        /// **`Control.DrawToBitmap` leaves a scroll bar blank**, and it is exactly the states this
        /// gallery exists for that grow one. It works by sending `WM_PRINT` down the tree, and a
        /// grid's scroll bars are native controls that do not answer it: the picture comes back
        /// with a bar-shaped hole where the bar is, so a list that has quietly started scrolling
        /// looks like a list that has not.
        ///
        /// `PrintWindow` is the operating system drawing its own window, which has no such gap and
        /// costs one import. `DrawToBitmap` stays as the fallback, because a picture with a hole in
        /// it is still worth more than no picture.
        /// </remarks>
        private static Bitmap Paint(Control window, Size size)
        {
            var bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            var printed = false;

            using (var canvas = Graphics.FromImage(bitmap))
            {
                var hdc = canvas.GetHdc();

                try
                {
                    printed = User32.PrintWindow(window.Handle, hdc, PrintWholeWindow);
                }
                finally
                {
                    canvas.ReleaseHdc(hdc);
                }
            }

            if (!printed)
            {
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            }

            return bitmap;
        }

        /// <summary>
        /// No flags, which asks the window to paint itself and its children.
        /// </summary>
        /// <remarks>
        /// **`PW_RENDERFULLCONTENT` was measured here and is worse.** Two renders what the desktop
        /// composited rather than what the window paints, and on this dialog that lost the Approve
        /// button altogether, leaving white where it had been, and restyled the caption into
        /// something no operator running this would see. It did not fix the one thing it was tried
        /// for either.
        ///
        /// **What neither renders is a themed scroll bar.** A list that needs one draws it on its
        /// non-client area through the theme, and a window asked to paint itself does not repeat
        /// that, so the bar arrives as a pale strip with no thumb and no arrows and reads as
        /// disabled. The control underneath is not: the profile list has a real seventeen pixel
        /// bar and scrolls, which `ScratchScrollProbe` established by asking it. A picture from
        /// here is evidence about layout and wording, and is not evidence about a scroll bar.
        /// </remarks>
        private const uint PrintWholeWindow = 0;

        private static void Write(Bitmap bitmap, string name)
        {
            var path = Path.Combine(Directory, name + ".png");
            bitmap.Save(path, ImageFormat.Png);
            TestContext.Out.WriteLine("gallery: " + path);
        }

        /// <summary>
        /// A window to screenshot the surface in, sized so its inside is the room under review.
        /// </summary>
        /// <remarks>
        /// **A window rather than a bare rectangle.** These pictures used to be of a frameless
        /// form, which is a crop with no edge in it: a page that is running out of room looks the
        /// same as one that has room to spare, because there is nothing in the picture to say where
        /// the room ends. A border and a caption say it.
        ///
        /// **`ClientSize` rather than `Size`**, so the inside stays exactly the room KeePass gives
        /// the page and the frame is added around it. Sizing the window instead would take the
        /// border out of the page's own width, and every picture would be of a page two pixels
        /// narrower than the one an operator gets.
        ///
        /// The caption is the picture's own name. It is not KeePass's caption and does not pretend
        /// to be: what it buys is that a reviewer with a dozen of these open can tell which is
        /// which without reading the file name off the title bar of the viewer.
        /// </remarks>
        private static Form OffScreen(Size client, string name)
        {
            return new Form
            {
                // Fixed, because nothing here is resized and a sizing border is four pixels of
                // grab handle around every picture that says nothing about the page inside it.
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = name,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = client,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,

                // Off the desktop, because a form that appears in front of somebody working is a
                // test interrupting them, and nothing here needs to be seen to be drawn.
                Location = new Point(-4000, -4000),

                // The tab pages this stands in for are painted in the dialog's own color, and a
                // control screenshotted on the wrong ground reads as the wrong color.
                BackColor = SystemColors.Control
            };
        }

        private static string Prepare()
        {
            var chosen = Environment.GetEnvironmentVariable("KPG_GALLERY_DIR");
            var where = string.IsNullOrEmpty(chosen) ? Path.Combine(Root(), ".gallery") : chosen;

            if (System.IO.Directory.Exists(where))
            {
                foreach (var stale in System.IO.Directory.GetFiles(where, "*.png"))
                {
                    File.Delete(stale);
                }
            }
            else
            {
                System.IO.Directory.CreateDirectory(where);
            }

            return where;
        }

        /// <summary>
        /// The repository, found by walking up from the test binary until the solution is beside us.
        /// </summary>
        /// <remarks>
        /// Rather than a path relative to the assembly, which is four directories of build layout
        /// written down in a test and wrong the day one of them changes.
        /// </remarks>
        private static string Root()
        {
            var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (at != null)
            {
                if (File.Exists(Path.Combine(at.FullName, "KeePassGate.sln")))
                {
                    return at.FullName;
                }

                at = at.Parent;
            }

            return TestContext.CurrentContext.TestDirectory;
        }
    }
}
