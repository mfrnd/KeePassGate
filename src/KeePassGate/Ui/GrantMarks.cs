using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the grant table draws on a row beyond the row's texts, `STORE.md` §657: the bracket
    /// binding the rows of a group, the letters in bold where the rows of a group differ, and the
    /// color behind the row.
    /// </summary>
    /// <remarks>
    /// **Worked out where the rows are painted, from the rows the sheet hands over.** A group is
    /// the rows of a profile bound together, which the sheet puts next to each other: the
    /// versions of a rule made in copies of the database, or a limit under its inherited rule.
    /// What the rows of a group differ in is read off the masks that read place by place, and a
    /// place where those masks do not all hold the same character is drawn in bold on each of
    /// them, so the eye reading down the column finds what the versions disagree on in the same
    /// place on each. A missing or unreadable row and a mask struck through take no bold.
    ///
    /// **A mark beside the words, never in their place.** Each row keeps its word in the State
    /// column, its tooltip and its accessible text; the bracket and the bold letters are drawn in
    /// the text's own color and font, so they hold under every color alike and under a high
    /// contrast setting, where no row takes a color of its own.
    ///
    /// **The bracket stands in a padding before the name**, the width of the ordinary spacing,
    /// drawn as a line as thick as the tight spacing, closed over the first row of the group and
    /// under the last, and open where the table's scrolling cuts the group, since a row scrolled
    /// out of sight is a row not drawn. The padding and the line grow with the text, so the
    /// bracket keeps its shape at every scaling.
    /// </remarks>
    internal sealed class GrantMarks
    {
        /// <summary>
        /// How a text box cell lays out text that does not wrap, which is where a mask drawn a
        /// letter at a time has to start for its letters to stand where the column measured them.
        /// </summary>
        private const TextFormatFlags Drawn = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                                              | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

        private const TextFormatFlags Measured = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        /// <summary>The pixels a text box cell leaves above text that does not wrap, inside its border.</summary>
        private const int TextMarginTop = 2;

        /// <summary>The pixel a text box cell leaves under its text, inside its border.</summary>
        private const int TextMarginBottom = 1;

        /// <summary>The border a cell owns on its right and at its bottom.</summary>
        private const int CellBorder = 1;

        private static readonly int[] NoPlace = Array.Empty<int>();

        private GrantMarks(bool bound, bool first, bool last, bool changed, IReadOnlyList<int> bold)
        {
            Bound = bound;
            First = first;
            Last = last;
            Changed = changed;
            Bold = bold;
        }

        /// <summary>Whether the row is bound together with other rows of its profile.</summary>
        public bool Bound { get; }

        /// <summary>Whether the row is the first of its group, over which the bracket closes.</summary>
        public bool First { get; }

        /// <summary>Whether the row is the last of its group, under which the bracket closes.</summary>
        public bool Last { get; }

        /// <summary>
        /// Whether the row shows a change that OK writes: a press on the tab changed the row, and
        /// the tab can still write the change.
        /// </summary>
        public bool Changed { get; }

        /// <summary>The places of the mask drawn in bold, counted from its first letter.</summary>
        public IReadOnlyList<int> Bold { get; }

        /// <summary>The marks of each row, in the order of the rows.</summary>
        /// <param name="writes">
        /// Whether the tab's OK writes the changes on the rows. Where a reading made again found
        /// the database no longer taking rules, a change stays on its row and is not marked as a
        /// change, since the light yellow tells that OK writes the change.
        /// </param>
        public static IList<GrantMarks> Of(IList<GrantRow> rows, bool writes)
        {
            var marks = new List<GrantMarks>(rows.Count);
            var start = 0;
            while (start < rows.Count)
            {
                var end = start + 1;
                if (rows[start].Bound)
                {
                    while (end < rows.Count && rows[end].Bound
                           && string.Equals(rows[end].Profile, rows[start].Profile, StringComparison.Ordinal))
                    {
                        end++;
                    }
                }

                var bound = end - start > 1;
                var bold = bound ? Differing(rows, start, end) : NoPlace;
                for (var at = start; at < end; at++)
                {
                    var places = Readable(rows[at]) ? bold : NoPlace;
                    marks.Add(new GrantMarks(bound, bound && at == start, bound && at == end - 1, writes && rows[at].Changed, places));
                }

                start = end;
            }

            return marks;
        }

        /// <summary>
        /// The color behind the row: the light yellow of a change over the light red of a group,
        /// or empty for the table's own background.
        /// </summary>
        public Color Tint(RowTints tints)
        {
            if (Changed)
            {
                return tints.Changed;
            }

            return Bound ? tints.Bound : Color.Empty;
        }

        /// <summary>The padding before the name that the bracket stands in, at the size of this text.</summary>
        public static int Room(Font font)
        {
            return Scaled(Spacing.Gap, font);
        }

        /// <summary>How thick the bracket's line is drawn, at the size of this text.</summary>
        public static int Stroke(Font font)
        {
            return Scaled(Spacing.Tight, font);
        }

        /// <summary>
        /// Draws the row's part of the bracket in the room before the name of the profile's cell:
        /// the line down the row, and an arm toward the name over the first row and under the last.
        /// </summary>
        /// <param name="cell">The cell's bounds, its border at the bottom included.</param>
        /// <param name="rightToLeft">Whether the table reads right to left, which puts the room at the cell's right.</param>
        public void DrawBracket(Graphics graphics, Rectangle cell, Color color, Font font, bool rightToLeft)
        {
            if (!Bound)
            {
                return;
            }

            var stroke = Stroke(font);
            var arm = Room(font) - (2 * stroke);

            // Down the whole row, across the line between rows, so the rows of a group read as a
            // single bracket, and stopped at the arm on the first and the last row.
            var top = First ? cell.Top + stroke : cell.Top;
            var bottom = Last ? cell.Bottom - CellBorder - stroke : cell.Bottom;

            using (var brush = new SolidBrush(color))
            {
                graphics.FillRectangle(brush, Across(cell, stroke, stroke, top, bottom - top, rightToLeft));

                if (First)
                {
                    graphics.FillRectangle(brush, Across(cell, stroke, arm, cell.Top + stroke, stroke, rightToLeft));
                }

                if (Last)
                {
                    graphics.FillRectangle(brush, Across(cell, stroke, arm, cell.Bottom - CellBorder - (2 * stroke), stroke, rightToLeft));
                }
            }
        }

        /// <summary>
        /// Draws a mask a letter at a time where a text box cell draws text, in bold in the places
        /// the rows of the group differ.
        /// </summary>
        /// <remarks>
        /// **Each letter in the place the column measured for it**, which is where the mask in its
        /// ordinary weight puts the letter, so a bold letter changes the weight and nothing else:
        /// the masks of every row stand letter under letter, and the column the grid sized to the
        /// ordinary weight holds the bold one.
        ///
        /// **Read left to right in either direction**, at the cell's leading edge. A mask is a fixed
        /// order of places, and a mask read right to left puts the dots at its ends on the wrong
        /// side of its letters.
        /// </remarks>
        /// <param name="cell">The cell's bounds, its borders included.</param>
        public void DrawMask(Graphics graphics, Rectangle cell, DataGridViewCellStyle style, string mask, Color color, bool rightToLeft)
        {
            // The cell's own border stands on its trailing side: at its right left to right, and at
            // its left right to left.
            var area = new Rectangle(
                cell.Left + (rightToLeft ? CellBorder + style.Padding.Right : style.Padding.Left),
                cell.Top + style.Padding.Top + TextMarginTop,
                cell.Width - CellBorder - style.Padding.Horizontal,
                cell.Height - CellBorder - style.Padding.Vertical - TextMarginTop - TextMarginBottom);
            if (area.Width <= 0 || area.Height <= 0)
            {
                return;
            }

            var font = style.Font;
            var flags = Drawn | (rightToLeft ? TextFormatFlags.Right : TextFormatFlags.Left);
            var whole = Width(graphics, mask, font);

            using (var bold = Bold.Count == 0 ? null : new Font(font, font.Style | FontStyle.Bold))
            {
                for (var at = 0; at < mask.Length; at++)
                {
                    var place = rightToLeft
                        ? new Rectangle(area.Left, area.Top, area.Width - (whole - Width(graphics, mask.Substring(0, at + 1), font)), area.Height)
                        : Inset(area, Width(graphics, mask.Substring(0, at), font));

                    TextRenderer.DrawText(graphics, mask.Substring(at, 1), Bold.Contains(at) ? bold : font, place, color, flags);
                }
            }
        }

        /// <summary>
        /// The places where the masks that read among these rows do not all hold the same
        /// character, or none where fewer than two masks read.
        /// </summary>
        /// <remarks>
        /// A missing or unreadable version shows a mask that holds no letter of any version, and a rule
        /// for a name the database no longer names is struck through and grants nothing: neither
        /// is a version the others can disagree with, and counted, either would put in bold the
        /// letters of versions that agree.
        /// </remarks>
        private static IReadOnlyList<int> Differing(IList<GrantRow> rows, int start, int end)
        {
            var masks = Enumerable.Range(start, end - start)
                .Select(at => rows[at])
                .Where(Readable)
                .Select(row => row.Mask ?? string.Empty)
                .ToList();
            if (masks.Count < 2)
            {
                return NoPlace;
            }

            var longest = masks.Max(mask => mask.Length);
            var places = new List<int>();
            for (var place = 0; place < longest; place++)
            {
                if (masks.Select(mask => place < mask.Length ? mask[place] : '\0').Distinct().Count() > 1)
                {
                    places.Add(place);
                }
            }

            return places.AsReadOnly();
        }

        /// <summary>Whether a row's mask is a version's letters drawn plainly: not missing, not unreadable, not struck through.</summary>
        private static bool Readable(GrantRow row)
        {
            return row.Kind != GrantRowKind.Lost
                   && !row.States.Contains(GrantState.NoProfile)
                   && !row.States.Contains(GrantState.Refused);
        }

        /// <summary>
        /// An amount at the size KeePass's dialogs are designed at, grown with the text as the
        /// dialogs grow, and never under the amount as designed.
        /// </summary>
        private static int Scaled(int designed, Font font)
        {
            return Math.Max(designed, (int)Math.Round((double)designed * font.Height / Spacing.Designed.Height));
        }

        /// <summary>A band across the cell an offset in from its leading edge, as wide as given.</summary>
        private static Rectangle Across(Rectangle cell, int offset, int width, int top, int height, bool rightToLeft)
        {
            var left = rightToLeft ? cell.Right - offset - width : cell.Left + offset;
            return new Rectangle(left, top, width, height);
        }

        private static Rectangle Inset(Rectangle area, int by)
        {
            return new Rectangle(area.Left + by, area.Top, area.Width - by, area.Height);
        }

        private static int Width(Graphics graphics, string text, Font font)
        {
            return text.Length == 0 ? 0 : TextRenderer.MeasureText(graphics, text, font, Size.Empty, Measured).Width;
        }
    }
}
