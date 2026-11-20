using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The table of profiles on an approval dialog: a row of headings that stays where it is above
    /// rows that scroll by pixels, each row a check box, where the row has a box, and a cell of
    /// text for each column, a cell taking a line for each line of its text.
    /// </summary>
    /// <remarks>
    /// **Real controls rather than a list view or a grid**, since neither holds a row taller than a
    /// line to its last line: every row of a list view is a single line tall, and a grid scrolls
    /// by whole rows, so a row taller than the grid is never shown to its end. Here each row is a
    /// `TableLayoutPanel` holding a `CheckBox` and a label for each cell, which gives the rest
    /// through the controls themselves: Space toggles the focused box, UI automation reads each
    /// box as a check box named by the row's text read out, a right to left window mirrors the
    /// columns, the theme's colors draw every part, a tooltip belongs to each cell, the row's
    /// background is its highlight over its whole height, and a check is the box's own state,
    /// which nothing laying the table again resets.
    ///
    /// - **Every cell keeps to the top of its row**, the box level with the first line of text,
    ///   and **every line of a cell is a line of its own**, cut with an ellipsis where its column
    ///   is too narrow and shown whole in the cell's tooltip.
    /// - **A row of a profile not granted has a red X in place of a box**, drawn on the row where
    ///   a box sits, and its text in the system's gray, <see cref="Omit"/>.
    /// - **A thin line between two rows**, in the system's color for a control's light edge, so
    ///   that a row of several lines reads as a single row and two highlighted rows as two; none
    ///   under the last row. **A tall row scrolls as the rest of the rows do**: its box, name and
    ///   state leave the view with its first line, and the line between two rows tells where the
    ///   next row begins. **A scroll reaches the screen whole**: the view and its rows are painted
    ///   into a single buffer, which reaches the screen in one piece, and every scroll paints the
    ///   whole view, a scroll by the wheel, the keys or the scroll bar before the next message,
    ///   <see cref="PaintNow"/>, so that no picture of the screen shows rows standing neither
    ///   where the scroll found them nor where it left them.
    /// - **A line cut in half at the view's bottom tells that the table scrolls**: rows taller
    ///   than the most the view shows give the view as many whole lines as that room holds from
    ///   the top of the rows and half the line after them, <see cref="ViewHeight"/>, so that the
    ///   view at the top of its rows cuts that line in half; scrolled, its bottom cuts whatever
    ///   line it meets. Rows that fit give the view their own height and no half line of empty
    ///   room.
    /// - **The rows end on the edge of a line**: the view scrolls as far as the first edge of a
    ///   line at or past the place where the last row's end meets the view's bottom, a row's top
    ///   or the top of a line of a row past its first, so that the view's top cuts no line and no
    ///   box once it is scrolled to the end, a row brought into view there among them, and less
    ///   than a row is left below the last row, inside the view, <see cref="Lay"/>.
    /// - **No sideways scrolling**: the columns are laid at the width the view leaves once a
    ///   vertical scroll bar is taken out, whether or not a scroll bar is shown, and a row reaches
    ///   the scroll bar's edge, or the view's where no scroll bar is shown, so that a highlight
    ///   reaches the edge too. **Every line can be reached**: the wheel scrolls by lines, the
    ///   parts of a notch a precision touchpad sends added up, the arrows by a line, Page Up and
    ///   Page Down by a view less a line, Home and End to either end, and Tab moves between the
    ///   boxes, a box out of sight bringing its row's top to the top of the view. **The keys
    ///   scroll with a box focused as with the view focused**, and the view stays where a key put
    ///   it: the box keeps the focus out of sight, its focus drawn nowhere, and nothing brings it
    ///   back into view until the focus moves to it or a Refresh brings its row into view. Space
    ///   on a box out of sight brings its row into view and changes no check; the press after it
    ///   changes the check in sight.
    /// - **A letter typed on a box is the box's own**, and a box ignores it: a letter on a button
    ///   otherwise reaches the dialog's mnemonics without Alt, and the letter of Refuse refused.
    ///   So is a letter typed on the view the rows scroll in.
    /// - **The view takes the focus where the dialog hands the focus to the table**,
    ///   <see cref="TakeFocus"/>, and is a stop of its own before the boxes, from which Tab goes
    ///   on to the first box. **The focus is drawn wherever it is, whatever turned the focus cues
    ///   on or left them off**: round the view, on a frame of the table's own around the view,
    ///   and round a box, on its row, clear of the box and a pixel inside the row and the view,
    ///   so that the dots stand on the row's own background, never on the blue of a checked box
    ///   and never on the line between rows or the view's frame.
    ///   **A double click on a box is the single click it begins with**, since a box double
    ///   clicked otherwise toggles twice, which reads as a click that did nothing.
    /// - **Every part UI automation reads has a name of its own**: the headings by
    ///   <see cref="HeadingsRead"/>, a row with a box by its profile and the box by the row's
    ///   text read out, a row with no box by its text read out, and every cell by its text; an
    ///   empty cell is hidden, since an unnamed part is read by the name of the part before it.
    ///   **Whether a part is off the screen is UI automation's own answer**: a row scrolled out of
    ///   the view is read as off the screen and its box and cells, which lie inside the row, as on
    ///   it, and a box or a cell answering off the screen in its own state changes nothing UI
    ///   automation reads.
    /// - **Laid by the dialog that holds it**, <see cref="Lay"/>, as the dialog is built, once it
    ///   has a window and after a change of its rows, and never in answer to its own size.
    /// </remarks>
    internal sealed class ProfileTable : Panel
    {
        /// <summary>The panel of headings above the rows.</summary>
        internal const string HeadingsName = "KpgProfileHeadings";

        /// <summary>The panel the rows scroll in.</summary>
        internal const string RowsName = "KpgProfileRows";

        /// <summary>What UI automation reads as the row of headings.</summary>
        internal const string HeadingsRead = "Headings";

        /// <summary>How a cell's line is drawn and measured, which has to be a single answer.</summary>
        private const TextFormatFlags LineShape =
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        /// <summary>
        /// The width of the frame of the table's own round the view, in pixels at every scaling,
        /// as a focus rectangle is: where the focus is drawn while the view holds the focus.
        /// </summary>
        private const int Frame = 1;

        private readonly ProfileColumn _box;
        private readonly IReadOnlyList<ProfileColumn> _columns;
        private readonly int _mostRows;
        private readonly RowPanel _headings;
        private readonly RowsPanel _rows;
        private readonly List<Row> _order = new List<Row>();
        private readonly Dictionary<string, Row> _byProfile = new Dictionary<string, Row>(StringComparer.Ordinal);

        /// <summary>
        /// The tips of every cell and box, shown always, since the approval dialog comes up without
        /// being activated, and a tip shown only on an active window would wait for a click the
        /// operator may not make.
        /// </summary>
        private readonly ToolTip _tips = new ToolTip { ShowAlways = true, AutoPopDelay = 32000, InitialDelay = 300, ReshowDelay = 100 };

        /// <summary>The height of a line of a cell, which the box fits in, measured by <see cref="Measure"/>.</summary>
        private int _line;

        /// <summary>The room above and below the lines of a row.</summary>
        private int _pad;

        /// <summary>The room each side of a cell's text, half a gap, so that two columns' texts stand a gap apart.</summary>
        private int _inset;

        /// <summary>The size a box asks for, its glyph and the room WinForms keeps beside the glyph.</summary>
        private Size _boxSize;

        /// <summary>The size of a box's glyph, which each box is given, measured by <see cref="Measure"/>.</summary>
        private Size _glyph;

        /// <summary>
        /// The place the view scrolls to furthest, in pixels from the top of the rows: the first
        /// edge of a line at or past the place where the last row's end meets the view's bottom,
        /// and the top where the rows fit the view, <see cref="Lay"/>.
        /// </summary>
        private int _end;

        /// <summary>A table of profiles with a column of boxes first and these columns of text after it.</summary>
        /// <param name="box">The column of boxes: its name and its heading.</param>
        /// <param name="columns">The columns of text, in order.</param>
        /// <param name="mostRows">How many rows of a single line the view shows at most before it scrolls.</param>
        public ProfileTable(ProfileColumn box, IReadOnlyList<ProfileColumn> columns, int mostRows)
        {
            _box = box ?? throw new ArgumentNullException(nameof(box));
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            _mostRows = mostRows;
            BorderStyle = BorderStyle.FixedSingle;

            _headings = NewRow(HeadingsName);
            _headings.AccessibleName = HeadingsRead;
            _headings.Controls.Add(new TableCell { Name = _box.Name, Text = _box.Heading, Anchor = AnchorStyles.Top | AnchorStyles.Left }, 0, 0);
            for (var column = 0; column < _columns.Count; column++)
            {
                _headings.Controls.Add(
                    new TableCell { Name = _columns[column].Name, Text = _columns[column].Heading, Anchor = AnchorStyles.Top | AnchorStyles.Left },
                    column + 1,
                    0);
            }

            _rows = new RowsPanel(this)
            {
                Name = RowsName,
                AutoScroll = true,
                BackColor = SystemColors.Window,
                ForeColor = SystemColors.WindowText,
                Margin = Padding.Empty
            };

            // The frame round the view is the table's own, and shows whether the view holds the
            // focus: drawn again as the view takes the focus and loses it.
            _rows.GotFocus += (sender, e) => Invalidate();
            _rows.LostFocus += (sender, e) => Invalidate();

            Controls.Add(_headings);
            Controls.Add(_rows);
        }

        /// <summary>The profiles whose rows have a box that is checked, in the order of the rows.</summary>
        internal IReadOnlyList<string> Checked
        {
            get { return _order.Where(row => row.Box != null && row.Box.Checked).Select(row => row.Profile).ToList(); }
        }

        /// <summary>Adds a row for a profile, with a box checked or not, or with no box, and empty cells.</summary>
        internal void Add(string profile, bool boxed, bool isChecked)
        {
            var row = new Row(profile, NewRow(GateTab.ProfileRowName), _columns.Count);
            row.Panel.TabIndex = _order.Count;
            if (boxed)
            {
                var box = new ProfileBox
                {
                    Name = GateTab.ProfileBoxName,
                    AutoSize = false,
                    Text = string.Empty,
                    Checked = isChecked,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left,
                    BringIntoSight = () => Reveal(row)
                };

                row.Box = box;
                row.Box.CheckedChanged += (sender, e) =>
                    WindowHandlers.Run("a changed check could not be brought into view", () => Reveal(row));

                // The focus moving to the box, by Tab or a click, brings the box into view, and
                // nothing else brings a box into view, `RowsPanel.ScrollToControl`.
                row.Box.Enter += (sender, e) =>
                    WindowHandlers.Run("a box given the focus could not be brought into view", () => Reveal(row));

                // The row draws the focus round its box, so the row is drawn again as the box
                // takes the focus and loses it.
                row.Box.GotFocus += (sender, e) => row.Panel.Invalidate();
                row.Box.LostFocus += (sender, e) => row.Panel.Invalidate();
                row.Panel.Box = row.Box;
                row.Panel.Controls.Add(row.Box, 0, 0);

                // Named by its profile, which is the row's own name: a part with no name of its
                // own is read by the name of the part before it, which was the clients of the row
                // above. The box carries the row's text read out, `Show`.
                row.Panel.AccessibleName = profile;
            }

            for (var column = 0; column < _columns.Count; column++)
            {
                row.Cells[column] = new TableCell { Name = _columns[column].Name, Anchor = AnchorStyles.Top | AnchorStyles.Left };
                row.Panel.Controls.Add(row.Cells[column], column + 1, 0);
            }

            _order.Add(row);
            _byProfile[profile] = row;
            _rows.Controls.Add(row.Panel);
        }

        /// <summary>
        /// Gives a profile's row its cells, a line of a cell for each line of its text, the tip of
        /// each cell, and the text its box, or the row where it has no box, is read out by. Shown
        /// once the table is laid again.
        /// </summary>
        /// <remarks>
        /// **The text read out is the whole row, however long**: a box of a profile thirty clients
        /// regain is read with every client, since the approval of the box rests on every client
        /// it names, and a text read out that left some of them out would approve a client the
        /// operator was not told of.
        /// </remarks>
        internal void Show(string profile, IReadOnlyList<string> cells, IReadOnlyList<string> tips, string spoken)
        {
            if (!_byProfile.TryGetValue(profile, out var row))
            {
                return;
            }

            for (var column = 0; column < row.Cells.Length; column++)
            {
                row.Cells[column].Text = column < cells.Count ? cells[column] ?? string.Empty : string.Empty;
                row.Tips[column] = column < tips.Count ? tips[column] : null;
            }

            // On the box, which is what a keyboard reaches; on the row where the row has no box, so
            // that UI automation reads that row as its name and what its cells state.
            if (row.Box != null)
            {
                row.Box.AccessibleName = spoken;
            }
            else
            {
                row.Panel.AccessibleName = spoken;
            }
        }

        /// <summary>
        /// Highlights a profile's row over its whole height in these colors, with this tip under
        /// every cell's own; a highlight is never taken away.
        /// </summary>
        internal void Mark(string profile, string tip, Color back, Color fore)
        {
            if (!_byProfile.TryGetValue(profile, out var row))
            {
                return;
            }

            row.Tip = tip;
            row.Panel.BackColor = back;
            row.Panel.ForeColor = fore;
        }

        /// <summary>
        /// Marks a profile's row with no box as a profile the dialog grants nothing: an X in place
        /// of a box, the row's text in the system's gray, and this tip on the X and under every
        /// cell's own. Shown once the table is laid again.
        /// </summary>
        /// <remarks>
        /// **Drawn on the row and no control of its own**, so that the X takes neither the focus
        /// nor a press and is nothing of its own to UI automation, which reads the row by the text
        /// <see cref="Show"/> gives it: a control would be read a second time, and an X that looks
        /// like the grant editor's would read as a button that removes the row.
        /// </remarks>
        internal void Omit(string profile, string tip)
        {
            if (!_byProfile.TryGetValue(profile, out var row) || row.Box != null)
            {
                return;
            }

            row.Tip = tip;
            row.Panel.Crossed = true;
            row.Panel.ForeColor = SystemColors.GrayText;
        }

        /// <summary>Checks or unchecks a profile's box, where its row has a box.</summary>
        internal void Check(string profile, bool isChecked)
        {
            if (_byProfile.TryGetValue(profile, out var row) && row.Box != null)
            {
                row.Box.Checked = isChecked;
            }
        }

        /// <summary>Scrolls a profile's row into view, as <see cref="Reveal(Row)"/> does.</summary>
        internal void Reveal(string profile)
        {
            if (_byProfile.TryGetValue(profile, out var row))
            {
                Reveal(row);
            }
        }

        /// <summary>
        /// Gives the focus to the view the rows scroll in, and answers whether the view took it.
        /// </summary>
        /// <remarks>
        /// **The view and never a box**: a box given the focus after a click showed no sign of it,
        /// and a Space a moment later checked the row of that box, which was the row a Refresh had
        /// just brought to the top and unchecked. On the view, Space checks nothing, the keys that
        /// scroll the table scroll it, and Tab goes on to the first box. Nothing scrolls.
        /// </remarks>
        internal bool TakeFocus()
        {
            return _rows.Focus();
        }

        /// <summary>
        /// Scrolls a row into view unless its box, or the row where it has no box, is in view
        /// already, and answers whether the view moved: the row's top comes to the top of the
        /// view where the rows below it fill the view, and otherwise as far up as the view
        /// scrolls, which for a row near the end puts the view's top on the edge of a line and
        /// leaves the rest of the view below the last row.
        /// </summary>
        /// <remarks>
        /// **The whole view left to paint, at the next paint**, unlike a scroll by the wheel or the
        /// keys, since the focus moving to a box draws the box's focus after this: the scroll and
        /// the focus reach the screen in the same paint.
        /// </remarks>
        private bool Reveal(Row row)
        {
            var top = row.Panel.Top + (row.Box == null ? 0 : row.Box.Top);
            var height = row.Box == null ? row.Panel.Height : row.Box.Height;
            if (top >= 0 && top + height <= _rows.ClientSize.Height)
            {
                return false;
            }

            var at = _rows.AutoScrollPosition;
            _rows.AutoScrollPosition = new Point(-at.X, Within(-at.Y + row.Panel.Top));
            _rows.Invalidate(true);
            return _rows.AutoScrollPosition != at;
        }

        /// <summary>
        /// Scrolls the rows to this many pixels from their top, as far as they scroll, and paints
        /// the whole view at once, <see cref="PaintNow"/>.
        /// </summary>
        private void ScrollTo(int position)
        {
            _rows.AutoScrollPosition = new Point(0, Within(position));
            PaintNow();
        }

        /// <summary>A place to scroll to, in pixels from the top, as far as the rows scroll.</summary>
        private int Within(int position)
        {
            return Math.Max(0, Math.Min(position, _end));
        }

        /// <summary>
        /// Paints the whole view and every row in it at once, in the single buffer the view is
        /// painted in, <see cref="RowsPanel"/>: after every scroll by the wheel, the keys or the
        /// scroll bar.
        /// </summary>
        /// <remarks>
        /// **The whole view, and before the next message.** The system moves what a scroll keeps
        /// by copying it, and the rows paint what the scroll uncovered afterwards, each window on
        /// its own: in between, the screen showed the strip the scroll uncovered holding the lines
        /// it held before the scroll, a line standing twice or a row missing. A view painted into
        /// a single buffer shows nothing of a paint until the paint ends, and shows of a copy only
        /// what a paint after the copy covers: so every scroll paints all of the view, and the
        /// screen goes from the rows where the scroll found them to the rows where it left them
        /// with no picture between. A paint waits for a loop with nothing else to do, which a
        /// wheel turned fast never leaves it, so the view is painted here and not left to it.
        /// </remarks>
        private void PaintNow()
        {
            _rows.Invalidate(true);
            _rows.Update();
        }

        /// <summary>
        /// Where a box sits on a row this wide: a box's glyph, its column's margin from the row's
        /// start, which is its right edge in a right to left window, and level with the first line.
        /// </summary>
        private Rectangle CrossPlace(int width)
        {
            var x = RightToLeft == RightToLeft.Yes ? width - _inset - _glyph.Width : _inset;
            return new Rectangle(new Point(x, _pad + ((_line - _glyph.Height) / 2)), _glyph);
        }

        /// <summary>
        /// Lays the headings and the rows at the table's width, each row as tall as its tallest
        /// cell, keeping the place scrolled to, and makes the view as tall as
        /// <see cref="ViewHeight"/> answers for the rows: with <paramref name="fit"/>, exactly;
        /// without it, only where that is taller than the view stands, so that the view grows with
        /// its rows and never shrinks.
        /// </summary>
        /// <remarks>
        /// **Fitted exactly where the dialog is sized**, as it is built and once it has a window,
        /// and **grown only after a change of the rows**: a dialog holding its table grows with
        /// the table, so that rows grown taller than the view are brought into sight up to the
        /// most the view shows, and rows grown shorter leave the view's height and an empty room
        /// under the last row rather than moving what stands below the table back up.
        /// </remarks>
        internal void Lay(bool fit)
        {
            // The view takes the focus, so UI automation reads the view by the table's name.
            _rows.AccessibleName = AccessibleName;
            Measure();
            var bar = SystemInformation.VerticalScrollBarWidth;
            var inner = ClientSize.Width;
            var wide = Math.Max(1, inner - (2 * Frame));
            var room = Math.Max(1, wide - bar);
            var widths = Widths(room);
            var single = _line + (2 * _pad);
            var heights = _order.Select(row => (2 * _pad) + (_line * row.Cells.Select(cell => Lines(cell.Text).Length).DefaultIfEmpty(1).Max())).ToList();
            var content = heights.Sum();
            var standing = Math.Max(0, ClientSize.Height - single - (2 * Frame));
            var fitting = ViewHeight(heights, fit ? 0 : standing);
            if (fit || fitting != standing)
            {
                Height = (Height - ClientSize.Height) + single + fitting + (2 * Frame);
            }

            var height = Math.Max(0, ClientSize.Height - single - (2 * Frame));
            var scrolls = content > height;
            var mirrored = RightToLeft == RightToLeft.Yes;

            SuspendLayout();
            LayRow(_headings, null, widths, new Rectangle(Frame + (mirrored ? bar : 0), 0, room, single));
            _rows.Bounds = new Rectangle(Frame, single + Frame, wide, height);

            // From the top, and then back to the place scrolled to as far as the rows still reach:
            // a row placed while the view is scrolled is placed against the scrolled origin. A row
            // is as wide as the view leaves beside a scroll bar, or as the view where none is
            // shown, its columns laid from its start in either direction, so that a highlight
            // reaches the edge; a line under every row but the last.
            var scrolled = -_rows.AutoScrollPosition.Y;
            _rows.SuspendLayout();
            _rows.AutoScrollPosition = Point.Empty;
            var y = 0;
            for (var at = 0; at < _order.Count; at++)
            {
                var row = _order[at];
                row.Panel.Ruled = at < _order.Count - 1;
                row.Panel.Rule = RuleColor(SystemInformation.HighContrast);
                row.Panel.Cross = CrossColor(SystemInformation.HighContrast);
                row.Panel.Around = _pad;
                LayRow(row.Panel, row.Box, widths, new Rectangle(0, y, scrolls ? room : wide, heights[at]));
                row.Panel.CrossAt = CrossPlace(row.Panel.Width);
                y += heights[at];
            }

            // The rows end on the edge of a line, where the view has room left below them for the
            // rest: the furthest the view scrolls, and a size of the rows' own to scroll through
            // that reaches it, as tall as the rows or taller by less than a row.
            _end = scrolls ? Edge(content - height, heights) : 0;
            _rows.AutoScrollMinSize = scrolls ? new Size(0, _end + height) : Size.Empty;
            _rows.Line = _line;
            _rows.ResumeLayout(true);
            _rows.AutoScrollPosition = new Point(0, Math.Min(scrolled, _end));
            ResumeLayout(false);

            // Painted whole at the next paint, as a scroll is: the rows moved here were copied as
            // they moved, and a copy reaches the screen only where a later paint covers it.
            _rows.Invalidate(true);
            Tip();
        }

        /// <summary>
        /// The height the view fits rows of these heights in, no lower than
        /// <paramref name="least"/>: as tall as the rows, and two rows of a single line at the
        /// least; and where the rows are taller than the most rows of a single line the view
        /// shows, as many whole lines as that room holds from the top of the rows and half the line
        /// after them.
        /// </summary>
        /// <param name="heights">The rows' heights, in order.</param>
        /// <param name="least">
        /// The height the view keeps, since it never shrinks; 0 where the view is sized afresh.
        /// </param>
        /// <remarks>
        /// **Half the line the view's bottom edge meets**, whatever that line is: a row of a single
        /// line, the first or the last line of a taller row, which carry the row's room above or
        /// below the text, or a line between, so that the line cut is cut in half and reads as a
        /// line going on below the view, neither a sliver of a line nor a line nearly whole. A
        /// view of rows that fit ends where the rows end, with no half line of empty room.
        ///
        /// **A view kept taller than that, whose rows still scroll**, grows on to the next height
        /// that cuts a line in half, as far as a row of a single line past the most, so that the
        /// line its bottom meets is cut in half there too.
        /// </remarks>
        private int ViewHeight(IReadOnlyList<int> heights, int least)
        {
            var single = _line + (2 * _pad);
            var content = heights.Sum();
            var most = _mostRows * single;
            var edges = Edges(heights).Concat(new[] { content }).ToList();
            var cuts = edges.Zip(edges.Skip(1), (above, below) => above + ((below - above) / 2)).ToList();
            var fitted = content <= most
                ? Math.Max(2 * single, content)
                : cuts[edges.FindLastIndex(edge => edge <= most)];
            var height = Math.Max(least, fitted);
            if (content > height && height > fitted)
            {
                height = cuts.Where(cut => cut >= height && cut <= most + single).DefaultIfEmpty(height).First();
            }

            return height;
        }

        /// <summary>
        /// The first edge of a line at or past this place, in pixels from the top of the rows of
        /// these heights: a row's top, or the top of a line of a row past its first, where the
        /// view's top cuts no line; and the place itself where no edge lies at or past it.
        /// </summary>
        private int Edge(int position, IReadOnlyList<int> heights)
        {
            return Edges(heights).Where(edge => edge >= position).DefaultIfEmpty(position).First();
        }

        /// <summary>
        /// The edges of the lines of rows of these heights, in pixels from the top of the rows and
        /// in order: each row's top and the top of each line of a row past its first. The end of
        /// the rows is not among them.
        /// </summary>
        private IEnumerable<int> Edges(IReadOnlyList<int> heights)
        {
            var top = 0;
            foreach (var height in heights)
            {
                yield return top;
                var lines = (height - (2 * _pad)) / Math.Max(1, _line);
                for (var line = 1; line < lines; line++)
                {
                    yield return top + _pad + (line * _line);
                }

                top += height;
            }
        }

        /// <summary>
        /// The color of the line between two rows: the system's color for a control's light edge,
        /// and under a high contrast setting the theme's text color.
        /// </summary>
        /// <param name="highContrast">Whether a high contrast setting is on, `SystemInformation.HighContrast`.</param>
        /// <remarks>
        /// **A system color either way**, never a fixed color, so that a theme draws the line in a
        /// color the theme keeps apart from its background: the light edge of a contrast theme can
        /// be the color of its background, and its text color never is.
        /// </remarks>
        internal static Color RuleColor(bool highContrast)
        {
            return highContrast ? SystemColors.WindowText : SystemColors.ControlLight;
        }

        /// <summary>
        /// The red of the X a row of a profile not granted draws in place of a box,
        /// <see cref="Omit"/>: 5.9 to 1 against white and 5.3 to 1 against the highlight of a
        /// changed row, `PairingApprovalForm.ChangedBackground`.
        /// </summary>
        internal static readonly Color CrossRed = Color.FromArgb(0xC4, 0x1E, 0x1E);

        /// <summary>
        /// The color of the X in place of a box: <see cref="CrossRed"/>, and under a high contrast
        /// setting the theme's text color, so that the shape alone carries the meaning there.
        /// </summary>
        /// <param name="highContrast">Whether a high contrast setting is on, `SystemInformation.HighContrast`.</param>
        internal static Color CrossColor(bool highContrast)
        {
            return highContrast ? SystemColors.WindowText : CrossRed;
        }

        /// <summary>Scrolls the rows by this many pixels, down where positive, <see cref="ScrollTo"/>.</summary>
        private void ScrollBy(int pixels)
        {
            ScrollTo(-_rows.AutoScrollPosition.Y + pixels);
        }

        /// <summary>
        /// The arrows, Page Up, Page Down, Home and End scroll the rows while a box or the view
        /// holds the focus, by keys rather than by any answer to a size.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            return WindowHandlers.Run("a key on a table of profiles could not be handled", () =>
            {
                if ((keyData & Keys.Modifiers) == Keys.None && _rows.ContainsFocus)
                {
                    var page = Math.Max(_line, _rows.ClientSize.Height - _line);
                    switch (keyData & Keys.KeyCode)
                    {
                        case Keys.Up:
                            ScrollBy(-_line);
                            return true;

                        case Keys.Down:
                            ScrollBy(_line);
                            return true;

                        case Keys.PageUp:
                            ScrollBy(-page);
                            return true;

                        case Keys.PageDown:
                            ScrollBy(page);
                            return true;

                        case Keys.Home:
                            ScrollBy(int.MinValue / 2);
                            return true;

                        case Keys.End:
                            ScrollBy(int.MaxValue / 2);
                            return true;
                    }
                }

                return base.ProcessDialogKey(keyData);
            }, true);
        }

        /// <summary>
        /// Draws the frame round the view in the view's background, and the focus round the view
        /// on the frame while the view holds the focus, whatever turned the focus cues on.
        /// </summary>
        /// <remarks>
        /// **On a frame of the table's own**, since the rows cover the view to its edges and a
        /// rectangle the view drew on itself would be drawn under them.
        /// </remarks>
        protected override void OnPaint(PaintEventArgs e)
        {
            WindowHandlers.Run("the focus of a table of profiles could not be drawn", () =>
            {
                base.OnPaint(e);
                if (e == null)
                {
                    return;
                }

                var frame = Rectangle.Inflate(_rows.Bounds, Frame, Frame);
                using (var background = new SolidBrush(_rows.BackColor))
                {
                    e.Graphics.FillRectangle(background, frame);
                }

                if (_rows.Focused)
                {
                    ControlPaint.DrawFocusRectangle(e.Graphics, frame, _rows.ForeColor, _rows.BackColor);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tips.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>What ends a line of a cell's text.</summary>
        private static readonly string[] LineEnds = { "\r\n", "\n" };

        /// <summary>The lines of a cell's text, a single empty line for none.</summary>
        private static string[] Lines(string text)
        {
            return (text ?? string.Empty).Split(LineEnds, StringSplitOptions.None);
        }

        /// <summary>A row of the table, or its headings: a column for the boxes and a column for each column of text.</summary>
        private RowPanel NewRow(string name)
        {
            var panel = new RowPanel
            {
                Name = name,
                ColumnCount = _columns.Count + 1,
                RowCount = 1,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (var column = 0; column <= _columns.Count; column++)
            {
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0f));
            }

            return panel;
        }

        /// <summary>Measures the line, the room round it, the box and its glyph, in the table's font.</summary>
        /// <remarks>
        /// **Each box as large as its glyph**: a check box with no text asks for a pixel more than
        /// its glyph below the glyph and two beside it, whatever its font, the glyph at its top
        /// left, and the focus a pixel inside a row of a single line runs through that pixel below,
        /// which a box of the size it asks for covers. The line stays as tall as the box asks for,
        /// so that a row is as tall either way.
        /// </remarks>
        private void Measure()
        {
            _inset = LogicalToDeviceUnits(Spacing.Gap) / 2;
            _pad = LogicalToDeviceUnits(Spacing.Tight);
            var box = _order.Select(row => row.Box).FirstOrDefault(each => each != null);
            if (box != null)
            {
                _boxSize = box.GetPreferredSize(Size.Empty);
            }
            else
            {
                using (var probe = new CheckBox { AutoSize = true, Text = string.Empty, Font = Font })
                {
                    _boxSize = probe.GetPreferredSize(Size.Empty);
                }
            }

            _glyph = new Size(Math.Max(1, _boxSize.Width - 2), Math.Max(1, _boxSize.Height - 1));
            _line = Math.Max(Font.Height, _boxSize.Height);
        }

        /// <summary>How wide a cell has to be to hold this text whole on a line.</summary>
        private int Fitting(string text)
        {
            return TextRenderer.MeasureText(text ?? string.Empty, Font, Size.Empty, LineShape).Width + (2 * _inset);
        }

        /// <summary>
        /// The widths of the columns in this room: the boxes' as wide as a box or its heading, a
        /// column with texts to fit as wide as the widest of them, and the others sharing the rest.
        /// </summary>
        private int[] Widths(int room)
        {
            var widths = new int[_columns.Count + 1];
            widths[0] = Math.Max(_boxSize.Width + (2 * _inset), Fitting(_box.Heading));
            var left = room - widths[0];
            var sharing = new List<int>();
            for (var column = 0; column < _columns.Count; column++)
            {
                var fitted = _columns[column].Fitted;
                if (fitted == null)
                {
                    sharing.Add(column + 1);
                    continue;
                }

                widths[column + 1] = fitted.Concat(new[] { _columns[column].Heading }).Max(text => Fitting(text));
                left -= widths[column + 1];
            }

            for (var at = 0; at < sharing.Count; at++)
            {
                widths[sharing[at]] = at == sharing.Count - 1
                    ? Math.Max(0, left - ((left / sharing.Count) * at))
                    : Math.Max(0, left / sharing.Count);
            }

            return widths;
        }

        /// <summary>Lays a row at these bounds, its box and its cells at the top of their columns.</summary>
        private void LayRow(TableLayoutPanel panel, CheckBox box, int[] widths, Rectangle bounds)
        {
            panel.SuspendLayout();
            for (var column = 0; column < widths.Length; column++)
            {
                panel.ColumnStyles[column].Width = widths[column];
            }

            panel.Bounds = bounds;
            if (box != null)
            {
                box.Size = _glyph;
                box.Margin = new Padding(_inset, _pad + ((_line - _glyph.Height) / 2), _inset, 0);
            }

            // A cell with nothing in it is hidden, which draws the same and leaves UI automation no
            // cell to read: an empty label is read by the name of the label before it, so the empty
            // cell beside "adds the profile" was read as "adds the profile" a second time.
            foreach (var cell in panel.Controls.OfType<TableCell>())
            {
                var column = panel.GetColumn(cell);
                cell.Line = _line;
                cell.Margin = new Padding(_inset, _pad, _inset, 0);
                cell.Size = new Size(Math.Max(1, widths[column] - (2 * _inset)), _line * Lines(cell.Text).Length);
                cell.Visible = !string.IsNullOrEmpty(cell.Text);
            }

            panel.ResumeLayout(true);
        }

        /// <summary>
        /// Gives every cell its tooltip: its whole text where a line of it is cut, under that the
        /// cell's own tip, and under that the row's; and every box and row the row's.
        /// </summary>
        /// <remarks>
        /// **A tip shown at the time whose words change is taken away**, so that no tooltip goes on
        /// stating what a row stated before the table was laid again until the pointer moves: the
        /// next rest of the pointer shows the words that stand.
        /// </remarks>
        private void Tip()
        {
            var changed = false;
            foreach (var cell in _headings.Controls.OfType<TableCell>())
            {
                changed |= Tip(cell, cell.Cut ? cell.Text : null);
            }

            foreach (var row in _order)
            {
                changed |= Tip(row.Panel, row.Tip);
                if (row.Box != null)
                {
                    changed |= Tip(row.Box, row.Tip);
                }

                for (var column = 0; column < row.Cells.Length; column++)
                {
                    var cell = row.Cells[column];
                    var lines = new[] { cell.Cut ? cell.Text : null, row.Tips[column], row.Tip }
                        .Where(line => !string.IsNullOrEmpty(line))
                        .ToList();
                    changed |= Tip(cell, lines.Count == 0 ? null : string.Join(Environment.NewLine, lines));
                }
            }

            if (changed && _tips.Active)
            {
                _tips.Active = false;
                _tips.Active = true;
            }
        }

        /// <summary>Gives a control this tip, folded, and answers whether that changed its words.</summary>
        private bool Tip(Control control, string text)
        {
            var tip = Tooltips.Wrapped(text) ?? string.Empty;
            if (string.Equals(_tips.GetToolTip(control), tip, StringComparison.Ordinal))
            {
                return false;
            }

            _tips.SetToolTip(control, tip.Length == 0 ? null : tip);
            return true;
        }

        /// <summary>A row and what the table keeps of it.</summary>
        private sealed class Row
        {
            public Row(string profile, RowPanel panel, int columns)
            {
                Profile = profile;
                Panel = panel;
                Cells = new TableCell[columns];
                Tips = new string[columns];
            }

            public string Profile { get; }

            public RowPanel Panel { get; }

            public CheckBox Box { get; set; }

            public TableCell[] Cells { get; }

            /// <summary>Each cell's own tip, or null.</summary>
            public string[] Tips { get; }

            /// <summary>The row's own tip, under every cell's, or null.</summary>
            public string Tip { get; set; }
        }

        /// <summary>
        /// A cell of text drawn a line at a time, each line in a band of its own, in a buffer, so
        /// that a heading painted again, outside the buffer the view is painted in, is shown whole
        /// and never its background first.
        /// </summary>
        private sealed class TableCell : Label
        {
            public TableCell()
            {
                UseMnemonic = false;
                AutoSize = false;
                DoubleBuffered = true;
            }

            /// <summary>The height of the band each line is drawn in, centered.</summary>
            public int Line { get; set; }

            /// <summary>Whether a line of the text is wider than the cell.</summary>
            public bool Cut
            {
                get
                {
                    return Lines(Text).Any(line => TextRenderer.MeasureText(line, Font, Size.Empty, LineShape).Width > ClientSize.Width);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                WindowHandlers.Run("a cell of a table of profiles could not be drawn", () =>
                {
                    // Not the base implementation, which wraps words and would put a client's
                    // name on two lines; the background is painted by `OnPaintBackground`.
                    var band = Math.Max(1, Line);
                    var shape = LineShape | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter
                                | (RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
                    var lines = Lines(Text);
                    for (var at = 0; at < lines.Length; at++)
                    {
                        TextRenderer.DrawText(
                            e.Graphics, lines[at], Font, new Rectangle(0, at * band, ClientSize.Width, band), ForeColor, shape);
                    }
                });
            }
        }

        /// <summary>
        /// A row's box: a letter typed on it is its own and does nothing, a double click is the
        /// single click it begins with, and Space on it out of sight brings it into sight and
        /// changes no check. Its row draws the focus round it, <see cref="RowPanel"/>.
        /// </summary>
        private sealed class ProfileBox : CheckBox
        {
            /// <summary>What the second press of a left double click arrives as.</summary>
            private const int DoubleClickMessage = 0x0203;

            /// <summary>
            /// Whether the Space held down now brought the box into sight, so that neither its
            /// repeats nor its release change the check.
            /// </summary>
            private bool _brought;

            /// <summary>
            /// Brings the box's row into view where the box is out of sight, and answers whether
            /// the view moved, <see cref="Reveal(Row)"/>.
            /// </summary>
            public Func<bool> BringIntoSight { get; set; }

            protected override bool IsInputChar(char charCode)
            {
                return true;
            }

            /// <summary>
            /// Space on the box out of sight brings its row into view and changes no check: Approve
            /// grants every profile checked, and a check changed out of sight changes unseen. The
            /// press after it, on the box in sight, changes the check as ever.
            /// </summary>
            protected override void OnKeyDown(KeyEventArgs e)
            {
                WindowHandlers.Run("Space on a box out of sight could not be handled", () =>
                {
                    var space = e != null && e.KeyData == Keys.Space;
                    if (space && (_brought || (BringIntoSight != null && BringIntoSight())))
                    {
                        _brought = true;
                        e.Handled = true;
                        return;
                    }

                    base.OnKeyDown(e);
                });
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                WindowHandlers.Run("Space on a box out of sight could not be handled", () =>
                {
                    if (e != null && e.KeyCode == Keys.Space && _brought)
                    {
                        _brought = false;
                        e.Handled = true;
                        return;
                    }

                    base.OnKeyUp(e);
                });
            }

            /// <summary>A Space whose release never reaches the box holds nothing back.</summary>
            protected override void OnLostFocus(EventArgs e)
            {
                _brought = false;
                base.OnLostFocus(e);
            }

            /// <summary>False: the box draws no focus of its own, since its row draws the focus clear of it.</summary>
            protected override bool ShowFocusCues
            {
                get { return false; }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == DoubleClickMessage)
                {
                    return;
                }

                base.WndProc(ref m);
            }
        }

        /// <summary>
        /// A row, or the headings: draws a line along its bottom where it is not the last row, the
        /// focus round its box while the box holds the focus, and an X in place of a box on the row
        /// of a profile not granted.
        /// </summary>
        /// <remarks>
        /// **The focus round the box, clear of it**, on the row's own background, in the row's
        /// colors, a gap round the box as far as the row leaves room: a check box draws the focus
        /// round its text and so, with no text, nowhere, and a rectangle on the box's own edge was
        /// dots on the blue of a checked box. **A pixel inside the row and inside the view**, since
        /// the row's top edge meets the line under the row above it or the view's frame, and its
        /// bottom edge is the line under it, and a focus drawn along either merges with that line;
        /// on a row of a single line that leaves the focus beside the box's glyph above and below.
        /// Drawn whatever the focus cues are, since the focus given to a box after a click turns no
        /// cue on, and **only where the box is in the view**: a box the keys scrolled out of sight
        /// keeps the focus, and a focus drawn at the view's edge would stand round nothing.
        ///
        /// **Drawn in a buffer**, as a cell is, so that the headings painted again, outside the
        /// buffer the view is painted in, are shown whole and never their background first.
        /// </remarks>
        private sealed class RowPanel : TableLayoutPanel
        {
            public RowPanel()
            {
                DoubleBuffered = true;
            }

            /// <summary>The row's box, or null for a row with no box and for the headings.</summary>
            public CheckBox Box { get; set; }

            /// <summary>Whether a line is drawn along the row's bottom.</summary>
            public bool Ruled { get; set; }

            /// <summary>The color of the line along the row's bottom.</summary>
            public Color Rule { get; set; }

            /// <summary>How far round the box the focus is drawn, in pixels.</summary>
            public int Around { get; set; }

            /// <summary>Whether the row draws an X in place of a box, <see cref="Omit"/>.</summary>
            public bool Crossed { get; set; }

            /// <summary>Where a box sits on the row, which the X is drawn inside.</summary>
            public Rectangle CrossAt { get; set; }

            /// <summary>The color of the X, <see cref="CrossColor"/>.</summary>
            public Color Cross { get; set; }

            protected override void OnPaint(PaintEventArgs e)
            {
                WindowHandlers.Run("a row of a table of profiles could not be drawn", () =>
                {
                    base.OnPaint(e);
                    if (e == null)
                    {
                        return;
                    }

                    if (Ruled)
                    {
                        using (var pen = new Pen(Rule))
                        {
                            e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
                        }
                    }

                    if (Crossed)
                    {
                        DrawCross(e.Graphics);
                    }

                    // A row scrolled up stands at a negative place in the view, and a box the keys
                    // scrolled out of sight keeps the focus with none of it drawn.
                    var view = Parent == null ? Height : Parent.ClientSize.Height;
                    if (Box != null && Box.Focused && Top + Box.Bottom > 0 && Top + Box.Top < view)
                    {
                        var at = Box.Bounds;
                        var top = Math.Max(at.Top - Around, 1 - Math.Min(0, Top));
                        var bottom = Math.Min(at.Bottom + Around, Height - 2);
                        ControlPaint.DrawFocusRectangle(
                            e.Graphics, Rectangle.FromLTRB(at.Left - Around, top, at.Right + Around, bottom), ForeColor, BackColor);
                    }
                });
            }

            /// <summary>
            /// Draws the X where a box sits, as large as a box's check mark: two strokes corner to
            /// corner of the box's glyph less a fifth of it all round, a sixth of the glyph thick
            /// and two pixels at the least, smoothed.
            /// </summary>
            private void DrawCross(Graphics graphics)
            {
                var at = CrossAt;
                var inset = Math.Max(1, at.Width / 5);
                var left = at.Left + inset;
                var top = at.Top + inset;
                var right = at.Right - 1 - inset;
                var bottom = at.Bottom - 1 - inset;
                var smoothing = graphics.SmoothingMode;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Cross, Math.Max(2f, at.Height / 6f)))
                {
                    graphics.DrawLine(pen, left, top, right, bottom);
                    graphics.DrawLine(pen, right, top, left, bottom);
                }

                graphics.SmoothingMode = smoothing;
            }
        }

        /// <summary>
        /// The panel the rows scroll in: the wheel scrolls it by lines, and it brings nothing into
        /// view of its own accord, <see cref="ScrollToControl"/>. It takes the focus, as a stop of
        /// its own, and a letter typed on it does nothing. It and its rows are painted into a
        /// single buffer, and wherever it scrolls the whole view is painted: at once after the
        /// wheel, the keys or the scroll bar, <see cref="PaintNow"/>, and with the focus after a
        /// box given the focus, <see cref="Reveal(Row)"/>.
        /// </summary>
        private sealed class RowsPanel : Panel
        {
            /// <summary>What a single notch of a wheel reports.</summary>
            private const int WheelNotch = 120;

            /// <summary>What the scroll bar sends as it scrolls the view.</summary>
            private const int ScrollMessage = 0x0115;

            /// <summary>
            /// `WS_EX_COMPOSITED`: the window and every window in it painted, bottom to top, into
            /// a single buffer, which reaches the screen once the paint is over.
            /// </summary>
            private const int Composited = 0x02000000;

            private readonly ProfileTable _table;

            public RowsPanel(ProfileTable table)
            {
                _table = table;
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;
            }

            /// <summary>The style of a panel, painted into a single buffer with its rows.</summary>
            /// <remarks>
            /// **The view and no more**: the headings and the frame round the view stand outside
            /// it and are painted on their own, as they never move.
            /// </remarks>
            protected override CreateParams CreateParams
            {
                get
                {
                    var parameters = base.CreateParams;
                    parameters.ExStyle |= Composited;
                    return parameters;
                }
            }

            /// <summary>
            /// Paints the whole view at once after the scroll bar's scroll, as
            /// <see cref="ScrollTo"/> does: a message of the scroll bar's own, which nothing here
            /// sends, and which a thumb dragged sends as fast as the pointer moves.
            /// </summary>
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == ScrollMessage)
                {
                    WindowHandlers.Run("the rows of a table of profiles could not be painted", _table.PaintNow);
                }
            }

            /// <summary>
            /// A letter typed on the view is the view's own, which it ignores, as a box does: on a
            /// control that leaves a letter to the dialog, the letter reaches the mnemonics.
            /// </summary>
            protected override bool IsInputChar(char charCode)
            {
                return true;
            }

            /// <summary>The height of a line of a cell, which the wheel scrolls by.</summary>
            public int Line { get; set; }

            /// <summary>
            /// The part of a pixel the wheel has turned and not yet scrolled by, in pixels times
            /// <see cref="WheelNotch"/>, down where positive.
            /// </summary>
            /// <remarks>
            /// **Kept between messages**, since a precision touchpad sends a notch as many small
            /// turns, and a turn divided on its own scrolled by little or nothing.
            /// </remarks>
            private long _turned;

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                WindowHandlers.Run("the wheel on a table of profiles could not be handled", () =>
                {
                    var lines = SystemInformation.MouseWheelScrollLines;
                    var notch = lines < 0 ? ClientSize.Height : lines * Math.Max(1, Line);
                    var turned = _turned - ((long)e.Delta * notch);
                    var by = turned / WheelNotch;
                    _turned = turned - (by * WheelNotch);
                    _table.ScrollTo((int)Math.Max(0, Math.Min(int.MaxValue / 2, -AutoScrollPosition.Y + by)));
                    if (e is HandledMouseEventArgs handled)
                    {
                        handled.Handled = true;
                    }
                });
            }

            /// <summary>
            /// Answers the place the view stands at, so that the view brings nothing into view of
            /// its own accord: the table brings a box into view where the focus moves to it, and a
            /// row where a Refresh or a changed check asks, <see cref="Reveal(Row)"/>.
            /// </summary>
            /// <remarks>
            /// **Asked at every layout of the dialog**, for the control holding the focus, and the
            /// dialog was laid out at every change of its countdown's words, every second, while
            /// the countdown was sized by them, ApprovalDialog.CountdownSize. A key
            /// that scrolls the table leaves the focus on its box, as the focused item of any list
            /// that scrolls keeps it, and the box may leave the view: brought back into view at the
            /// next layout, the view went back to where the key found it within a second, and a
            /// row taller than the view was never shown to its last line by the keys with a box
            /// focused. **A box out of sight stays out of sight** until the focus moves to it or a
            /// Refresh brings its row into view.
            /// </remarks>
            protected override Point ScrollToControl(Control activeControl)
            {
                return AutoScrollPosition;
            }
        }
    }

    /// <summary>A column of a <see cref="ProfileTable"/>: its name, its heading, and how wide it is.</summary>
    internal sealed class ProfileColumn
    {
        /// <summary>A column of this name under this heading, as wide as these texts.</summary>
        /// <param name="name">The name of the heading and of every cell under it.</param>
        /// <param name="heading">The heading's text.</param>
        /// <param name="fitted">
        /// The texts the column is as wide as the widest of, with its heading, or null for a column
        /// that shares what the others leave.
        /// </param>
        public ProfileColumn(string name, string heading, IReadOnlyList<string> fitted = null)
        {
            Name = name;
            Heading = heading;
            Fitted = fitted;
        }

        public string Name { get; }

        public string Heading { get; }

        public IReadOnlyList<string> Fitted { get; }
    }
}
