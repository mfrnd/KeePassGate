using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A button with an arrow beside its caption: pressing the caption does what the button says,
    /// and the arrow, or Alt and Down while it has the focus, opens a menu of what else it can mean.
    /// </summary>
    /// <remarks>
    /// **WinForms has no split button**, so this is one built from a button and a menu, and drawn to
    /// look like the platform's. The button keeps everything a button is: a press, Space, the
    /// letter its caption underlines, being a form's accept button, and what a screen reader is
    /// told. What is added is the arrow, and what pressing it does.
    ///
    /// **Drawn by the base class first**, as <see cref="CopyButton"/> is, so its frame, its states
    /// and its focus rectangle are those of the button beside it on whatever theme is running. The
    /// arrow's part is kept clear of the caption by being part of the padding, which is also what
    /// makes it count when the button sizes itself.
    ///
    /// **The menu owns Enter and Escape while it is open**, and that was measured rather than
    /// assumed, because the grant editor's pickers are the opposite: a dropdown hosting a list
    /// lets both through to the dialog's own buttons, which is what `DialogKeys` exists for. A
    /// menu holding nothing but its items does not. Enter clicks the highlighted item and nothing
    /// else, or does nothing when none is highlighted; Escape closes it; the dialog's accept button
    /// and its own handling of keys see neither. So nothing is taken from the dialog here.
    ///
    /// **A choice is acted on a message after the menu has closed.** `ItemClicked` is raised while
    /// the menu is still on screen, and what a choice does here closes the dialog or opens another,
    /// neither of which belongs inside the menu's own event. The menu is never disposed there
    /// either, which is the trap `GrantPanel.Finish` records: it is made once and goes with the
    /// button.
    ///
    /// **Nothing closes the menu by code**, and that was measured too. A menu closed by `Close`
    /// and then gone with its window left the next window's menu closing the moment it opened, as
    /// an `AppFocusChange`. A choice, Escape, a press elsewhere, or the dialog ending with the menu
    /// still open and taking it along, leave nothing of the kind behind, so those are the only
    /// ways it ends.
    /// </remarks>
    internal sealed class SplitButton : Button
    {
        /// <summary>
        /// How far the line between the caption and the arrow stays from the button's top and
        /// bottom edges, inside the frame the theme draws.
        /// </summary>
        private const int SeparatorInset = 4;

        private readonly ContextMenuStrip _menu;
        private readonly IGateLog _log;

        /// <summary>What was chosen while the menu was open, acted on once it has closed.</summary>
        private ToolStripItem _chosen;

        /// <summary>
        /// Whether the menu has just been closed by a press outside it, which is the press that
        /// would otherwise open it again when it landed on the arrow.
        /// </summary>
        private bool _closedByAPress;

        public SplitButton(IGateLog log)
        {
            _log = log ?? NullGateLog.Instance;

            _menu = new ContextMenuStrip { ShowImageMargin = false };
            _menu.ItemClicked += (sender, e) => _chosen = e.ClickedItem;
            _menu.Closing += OnMenuClosing;
            _menu.Closed += OnMenuClosed;

            Reserve();
        }

        /// <summary>
        /// Raised once a choice made from the menu can be acted on: after the menu has closed, in
        /// the message after the one that closed it.
        /// </summary>
        /// <remarks>
        /// Raised on the message loop, where whatever escapes reaches the program around the
        /// dialog rather than the code that chose: a failure of a kind the user interface throws
        /// is caught here and written to the log, and nothing else is.
        /// </remarks>
        public event EventHandler<ToolStripItemClickedEventArgs> ItemChosen;

        /// <summary>The menu the arrow opens. Its items are the caller's to add.</summary>
        public ContextMenuStrip Menu
        {
            get { return _menu; }
        }

        /// <summary>Whether the menu is on screen now.</summary>
        public bool MenuShown
        {
            get { return _menu.Visible; }
        }

        /// <summary>The part of the button the arrow occupies, which opens the menu when pressed.</summary>
        public Rectangle ArrowBounds
        {
            get
            {
                var width = ArrowPartWidth(Font);
                return new Rectangle(Math.Max(0, ClientSize.Width - width), 0, width, ClientSize.Height);
            }
        }

        /// <summary>
        /// How wide the arrow's part of the button is at a font: a little more than a line of its
        /// text is tall, so that it grows with the text the way the caption does.
        /// </summary>
        public static int ArrowPartWidth(Font font)
        {
            var height = font == null ? Control.DefaultFont.Height : font.Height;
            return height + (Spacing.Gap / 2);
        }

        /// <summary>
        /// Opens the menu above the button, exactly as pressing the arrow does. Opened from the
        /// keyboard, its first item is highlighted, so that Enter has something to choose.
        /// </summary>
        /// <remarks>
        /// **Above, right edges together**, because this sits at the bottom of a dialog: the choices
        /// then cover the dialog they belong to rather than hanging past its edge.
        ///
        /// Reachable without a press, for the tests that are about what follows one.
        /// </remarks>
        public void ShowMenu(bool fromTheKeyboard)
        {
            if (_menu.Visible || _menu.Items.Count == 0 || !Enabled)
            {
                return;
            }

            _chosen = null;
            _menu.Show(this, new Point(ClientSize.Width, 0), ToolStripDropDownDirection.AboveLeft);

            if (!fromTheKeyboard)
            {
                return;
            }

            foreach (ToolStripItem item in _menu.Items)
            {
                if (item.Enabled && item.Available)
                {
                    item.Select();
                    break;
                }
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Reserve();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        /// <summary>
        /// What one key does to this button beyond what a button does with it. True when it was
        /// taken: Alt and Down open the menu, as they open a list on a dialog.
        /// </summary>
        /// <remarks>
        /// Reachable without the message loop, because a test cannot hold Alt down: the state of a
        /// modifier is read from the keyboard, not from a message a test can post.
        /// </remarks>
        internal bool Press(Keys keyData)
        {
            if (keyData != (Keys.Alt | Keys.Down))
            {
                return false;
            }

            ShowMenu(true);
            return true;
        }

        /// <summary>
        /// Offered every key while this has the focus, before the dialog is.
        /// </summary>
        /// <remarks>
        /// Here rather than on the dialog, so that the keys open this menu only while the focus is
        /// on this button: a list elsewhere on the same dialog keeps the same keys for itself. Down
        /// alone is left to the dialog, which moves the focus with it.
        /// </remarks>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            return Press(keyData) || base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// A press on the arrow opens the menu and is not a press of the button.
        /// </summary>
        /// <remarks>
        /// The base class is not told, so nothing is pressed and no click follows when the mouse
        /// comes up. A press on the arrow that closed the menu is the end of the menu rather than
        /// the start of another: the menu has already gone by the time the press arrives here,
        /// measured, and opening it again would make the arrow impossible to close it with.
        /// </remarks>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e != null && e.Button == MouseButtons.Left && ArrowBounds.Contains(e.Location))
            {
                if (_closedByAPress)
                {
                    _closedByAPress = false;
                    return;
                }

                // Let go of the pointer, which the control took as the press arrived, so that it is
                // the menu's to have.
                Capture = false;
                ShowMenu(false);
                return;
            }

            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The frame, the background, the caption and the focus rectangle are the base class's,
            // as on the button beside it.
            base.OnPaint(e);

            if (e == null)
            {
                return;
            }

            var arrow = ArrowBounds;
            if (arrow.Width <= 0 || arrow.Height <= SeparatorInset * 2)
            {
                return;
            }

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.None;

            using (var line = new Pen(Enabled ? SystemColors.ControlDark : SystemColors.GrayText))
            {
                graphics.DrawLine(line, arrow.Left, arrow.Top + SeparatorInset, arrow.Left, arrow.Bottom - SeparatorInset - 1);
            }

            // A triangle pointing down, as wide as half the part and odd, so that it has a middle,
            // and half that tall. Filled on whole pixels: anti-aliasing a glyph this small blurs it.
            var width = Math.Max(5, ((arrow.Width / 2) / 2 * 2) + 1);
            var height = (width + 1) / 2;
            var left = arrow.Left + ((arrow.Width - width) / 2) + 1;
            var top = arrow.Top + ((arrow.Height - height) / 2);

            using (var brush = new SolidBrush(Enabled ? SystemColors.ControlText : SystemColors.GrayText))
            {
                graphics.FillPolygon(brush, new[]
                {
                    new Point(left, top),
                    new Point(left + width, top),
                    new Point(left + (width / 2), top + height)
                });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _menu.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Keeps the arrow's part out of the caption's room, by making it part of the padding.
        /// </summary>
        private void Reserve()
        {
            var padding = Padding;
            var right = ArrowPartWidth(Font);
            if (padding.Right != right)
            {
                Padding = new Padding(padding.Left, padding.Top, right, padding.Bottom);
            }
        }

        private void OnMenuClosing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason != ToolStripDropDownCloseReason.AppClicked)
            {
                return;
            }

            // The press that closed the menu is delivered to whatever is under it once the menu is
            // gone. When that is the arrow, `OnMouseDown` must not open the menu straight back up,
            // and when it is anything else the mark has to go before the next press arrives.
            _closedByAPress = true;
            Later(() => _closedByAPress = false);
        }

        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            Later(() =>
            {
                Invalidate();

                var chosen = _chosen;
                _chosen = null;

                if (chosen != null && ItemChosen != null)
                {
                    ItemChosen(this, new ToolStripItemClickedEventArgs(chosen));
                }
            });
        }

        /// <summary>
        /// Runs something once the current message is finished with, guarded where it runs.
        /// </summary>
        /// <remarks>
        /// Straight through when there is no window, which is how a test reaches the same path.
        /// </remarks>
        private void Later(Action work)
        {
            Action guarded = () =>
            {
                try
                {
                    work();
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("a choice from a menu could not be carried out", error);
                }
            };

            if (!IsHandleCreated || IsDisposed)
            {
                guarded();
                return;
            }

            try
            {
                BeginInvoke(guarded);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a choice from a menu could not be carried out: " + error.Message);
            }
        }
    }
}
