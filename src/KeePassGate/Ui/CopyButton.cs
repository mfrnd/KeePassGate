using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A small square button carrying a copy icon, drawn rather than written or shipped.
    /// </summary>
    /// <remarks>
    /// **Drawn, for two reasons.** A character would depend on the glyph existing in whatever font
    /// WinForms falls back to and on it rendering as an icon rather than as a color emoji or a
    /// missing-glyph box, and a box in place of an icon is worse than no icon. An image would be a
    /// resource to ship, and a second one to ship for a display that is not at 100 percent. Two
    /// rectangles depend on neither and are sized from the button, so they follow the font size and
    /// the display scaling the rest of the tab follows.
    ///
    /// The shape is the usual one: a sheet, and the corner of a second sheet behind it. Only the
    /// exposed edges of the one behind are drawn, so nothing has to be filled and the button looks
    /// the same whatever the theme paints behind it.
    /// </remarks>
    internal sealed class CopyButton : Button
    {
        public CopyButton()
        {
            // The icon is the caption, so there is no text to size to and no text for UI automation
            // to read. `AccessibleName` is what it reads instead, and the panel sets it.
            Text = string.Empty;
            AutoSize = false;
            FlatStyle = FlatStyle.Standard;
        }

        /// <summary>Gray rather than black once the button cannot be pressed.</summary>
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The frame, the background and the focus rectangle are the base class's, and are worth
            // keeping: they are what makes this look like a button rather than a picture.
            base.OnPaint(e);

            var box = Math.Min(ClientSize.Width, ClientSize.Height);
            if (box < 8)
            {
                return;
            }

            // The sheet is half the button, and the one behind it is offset by a third of that,
            // which leaves the icon clear of the button's own frame. Proportions rather than
            // pixels, so this is right at any font size.
            var sheet = Math.Max(6, box / 2);
            var offset = Math.Max(2, sheet / 3);

            var left = (ClientSize.Width - sheet - offset) / 2;
            var top = (ClientSize.Height - sheet - offset) / 2;

            var front = new Rectangle(left, top + offset, sheet, sheet);

            // The exposed edges of the sheet behind: up its left side from where the front sheet
            // covers it, across the top, down the right, and back in to where the front sheet
            // starts covering it again.
            var behind = new[]
            {
                new Point(front.Left + offset, front.Top),
                new Point(front.Left + offset, front.Top - offset),
                new Point(front.Right + offset, front.Top - offset),
                new Point(front.Right + offset, front.Bottom - offset),
                new Point(front.Right, front.Bottom - offset)
            };

            using (var pen = new Pen(Enabled ? SystemColors.ControlText : SystemColors.GrayText))
            {
                // Straight lines on whole pixels: anti-aliasing an icon this small blurs it.
                e.Graphics.SmoothingMode = SmoothingMode.None;
                e.Graphics.DrawLines(pen, behind);
                e.Graphics.DrawRectangle(pen, front);
            }
        }
    }
}
