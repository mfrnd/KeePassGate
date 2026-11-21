using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the grant table draws beyond its texts, read off the pixels of a table that was shown:
    /// the bracket binding the rows of a group, the letters in bold where the rows of a group differ,
    /// and both of them under every color alike, right to left and at a larger size of text.
    /// </summary>
    /// <remarks>
    /// **Shown, so it runs alone and holds the desktop**, for the reason `GrantTableFitsTests` gives:
    /// a grid with no window lays nothing out and draws nothing.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class GrantDrawingTests
    {
        private const int ProfileColumn = GrantTab.ProfileColumn;
        private const int MaskColumn = GrantTab.MaskColumn;
        private const int StateColumn = GrantTab.StateColumn;

        /// <summary>A rule standing alone above a rule in three versions, a version taking the rule off among them.</summary>
        private static readonly NodeRule AloneAndThree = Rules.Stored(
            ("ops", "row:CRL"), ("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "off:R"));

        [Test]
        public void A_bracket_binds_the_rows_of_a_group_closed_at_its_first_and_its_last_row()
        {
            Show(AloneAndThree, RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                Assert.That(Lines(panel), Is.EqualTo(new[]
                {
                    "ops CR...L..", "reader .R...... Removed", "reader .R...L.. Unmerged", "reader CR...L.. Unmerged",
                }));

                var bracket = Bracket.Of(grid, false);
                Assert.That(bracket.Bar(picture, 0), Is.LessThan(bracket.BarHeight(0)), "a row standing alone is bound to nothing");

                foreach (var row in new[] { 1, 2, 3 })
                {
                    Assert.That(bracket.Bar(picture, row), Is.EqualTo(bracket.BarHeight(row)), "the line runs down row " + row);
                }

                Assert.That(bracket.TopArm(picture, 1), Is.GreaterThan(0), "closed over the first row");
                Assert.That(bracket.BottomArm(picture, 1), Is.Zero);
                Assert.That(bracket.TopArm(picture, 2) + bracket.BottomArm(picture, 2), Is.Zero, "open through the middle");
                Assert.That(bracket.Edge(picture, 2, true) && bracket.Edge(picture, 2, false), Is.True, "and joined to the rows beside it");
                Assert.That(bracket.TopArm(picture, 3), Is.Zero);
                Assert.That(bracket.BottomArm(picture, 3), Is.GreaterThan(0), "closed under the last row");
            });
        }

        [Test]
        public void The_bracket_stays_in_the_padding_and_leaves_the_name_whole_at_a_larger_size_of_text()
        {
            foreach (var scaling in new[] { 1f, 2f })
            {
                // The name as a row standing alone draws the name, to hold the bound rows to.
                var name = 0;
                Show(Rules.Stored(("reader", "row:RL")), RightToLeft.No, scaling, (panel, grid, picture) =>
                    name = Bracket.Of(grid, false).Text(picture, 0).Width);

                Show(AloneAndThree, RightToLeft.No, scaling, (panel, grid, picture) =>
                {
                    var bracket = Bracket.Of(grid, false);

                    Assert.That(GrantMarks.Stroke(grid.Font), Is.GreaterThanOrEqualTo((int)(Spacing.Tight * scaling)), "at " + scaling);
                    Assert.That(bracket.BarWidth(picture, 2), Is.EqualTo(GrantMarks.Stroke(grid.Font)), "at " + scaling);

                    for (var row = 1; row <= 3; row++)
                    {
                        var text = bracket.Text(picture, row);
                        Assert.That(text.Left, Is.GreaterThanOrEqualTo(bracket.Room), "the name runs into the bracket on row " + row + " at " + scaling);
                        Assert.That(text.Width, Is.EqualTo(name), "the name is cut on row " + row + " at " + scaling);
                    }

                    Assert.That(bracket.Text(picture, 0).Left, Is.LessThan(bracket.Room), "a name standing alone starts where it always did");
                });
            }
        }

        [Test]
        public void The_bracket_is_open_where_scrolling_cuts_the_group()
        {
            var versions = new List<(string Profile, string Text)> { ("a", "row:R"), ("a", "row:RL"), ("a", "row:CRL") };
            versions.AddRange(Enumerable.Range(0, 24).Select(i => ("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "row:R")));

            Show(Rules.Stored(versions.ToArray()), RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                var bracket = Bracket.Of(grid, false);
                Assert.That(grid.FirstDisplayedScrollingRowIndex, Is.EqualTo(1), "the table scrolled past the first version");
                Assert.That(bracket.TopArm(picture, 1), Is.Zero, "the group goes on above the table's edge");
                Assert.That(bracket.Edge(picture, 1, true), Is.True);
                Assert.That(bracket.BottomArm(picture, 2), Is.GreaterThan(0));
            }, grid => grid.FirstDisplayedScrollingRowIndex = 1);
        }

        [Test]
        [TestCase(RightToLeft.No)]
        [TestCase(RightToLeft.Yes)]
        public void The_letters_two_versions_do_not_agree_on_are_bold_and_every_other_letter_is_drawn_as_on_a_row_alone(RightToLeft direction)
        {
            var rule = Rules.Stored(("ops", "row:CRL"), ("reader", "row:RL"), ("reader", "row:CRL"));

            Show(rule, direction, 1f, (panel, grid, picture) =>
            {
                Assert.That(Lines(panel), Is.EqualTo(new[] { "ops CR...L..", "reader .R...L.. Unmerged", "reader CR...L.. Unmerged" }));

                var slots = Slots.Of(grid, picture, 0);
                var bold = slots.Ink(picture, 2);
                var plain = slots.Ink(picture, 0);

                Assert.That(bold[0], Is.GreaterThan(plain[0]), "the C the versions do not agree on is bold");
                for (var at = 1; at < bold.Length; at++)
                {
                    Assert.That(bold[at], Is.EqualTo(plain[at]).Within(3), "the letter at " + at + " is drawn as on the row alone, the paper behind aside");
                }
            });
        }

        [Test]
        public void The_letters_three_versions_do_not_agree_on_are_bold_a_version_taking_the_rule_off_among_them()
        {
            Show(AloneAndThree, RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                var slots = Slots.Of(grid, picture, 0);
                var bold = slots.Ink(picture, 3);
                var plain = slots.Ink(picture, 0);
                var off = slots.Ink(picture, 1);

                Assert.That(bold[0], Is.GreaterThan(plain[0]));
                Assert.That(bold[5], Is.GreaterThan(plain[5]));
                Assert.That(bold[1], Is.EqualTo(plain[1]).Within(3));
                Assert.That(off[1], Is.EqualTo(plain[1]).Within(3), "the R all three hold is drawn plain on the version taking the rule off");
            });
        }

        [Test]
        public void The_bracket_and_the_bold_letters_are_drawn_in_the_text_s_own_color_and_the_words_and_the_change_beside_them()
        {
            // What the colors mark is told without them too: the bracket in the table's text color
            // and the bold letters in the row's, so the marks stay with every color alike and under
            // a high contrast setting, where no row takes a color.
            Show(AloneAndThree, RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                var bracket = Bracket.Of(grid, false);
                Assert.That(bracket.Bar(picture, 2), Is.EqualTo(bracket.BarHeight(2)));
                Assert.That(bracket.TopArm(picture, 1), Is.GreaterThan(0));
                Assert.That(bracket.LineColor(picture, 2).ToArgb(), Is.EqualTo(grid.DefaultCellStyle.ForeColor.ToArgb()));

                var slots = Slots.Of(grid, picture, 0);
                Assert.That(slots.Ink(picture, 3)[0], Is.GreaterThan(slots.Ink(picture, 0)[0]));

                Assert.That(
                    grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => (string)row.Cells[StateColumn].Value),
                    Is.EqualTo(new[] { string.Empty, GrantTab.RemovedWord, GrantTab.UnmergedWord, GrantTab.UnmergedWord }));
                Assert.That(grid.Rows[0].AccessibilityObject.Description, Is.EqualTo(GrantTab.ChangedHere));
                Assert.That(GrantTab.MaskTip((GrantRow)grid.Rows[0].Tag, true), Does.EndWith(GrantTab.ChangedHere));
                Assert.That(grid.Rows[0].Cells[MaskColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.MaskTip((GrantRow)grid.Rows[0].Tag, true))));
            }, null, panel => panel.SetGrant("ops", "CRU....."));
        }

        [Test]
        public void Right_to_left_the_bracket_stands_before_the_name_at_the_right_of_its_cell()
        {
            Show(AloneAndThree, RightToLeft.Yes, 1f, (panel, grid, picture) =>
            {
                var bracket = Bracket.Of(grid, true);
                Assert.That(bracket.Bar(picture, 0), Is.LessThan(bracket.BarHeight(0)));
                Assert.That(bracket.Bar(picture, 2), Is.EqualTo(bracket.BarHeight(2)));
                Assert.That(bracket.TopArm(picture, 1), Is.GreaterThan(0));
                Assert.That(bracket.BottomArm(picture, 3), Is.GreaterThan(0));

                Assert.That(bracket.BarWidth(picture, 2), Is.EqualTo(bracket.Stroke), "the line as thick as left to right");
                Assert.That(bracket.Edge(picture, 2, true) && bracket.Edge(picture, 2, false), Is.True);

                var text = bracket.Text(picture, 2);
                Assert.That(text.Left, Is.GreaterThanOrEqualTo(bracket.Room), "the name runs into the bracket");
            });
        }

        [Test]
        [TestCase(1f, RightToLeft.No)]
        [TestCase(1.25f, RightToLeft.No)]
        [TestCase(1.5f, RightToLeft.No)]
        [TestCase(2f, RightToLeft.No)]
        [TestCase(1f, RightToLeft.Yes)]
        [TestCase(2f, RightToLeft.Yes)]
        public void A_mask_drawn_a_letter_at_a_time_is_the_mask_the_cell_draws_on_its_own(float scaling, RightToLeft direction)
        {
            // Every pixel of a plain mask, a row standing alone and unchanged, against the grid's
            // own drawing of the same text in a cell of the same size, font and direction. A mask
            // with a letter at each end, which reads the same in either direction.
            // cspell:ignore CRLP
            Show(Rules.Stored(("ops", "row:CRLP")), direction, scaling, (panel, grid, picture) =>
            {
                Assert.That(Lines(panel)[0], Is.EqualTo("ops CR...L.P"), "a rule standing alone, unchanged");
                var cell = InPicture(grid, MaskColumn, 0);
                var font = grid.Columns[MaskColumn].DefaultCellStyle.Font;

                Assert.That(Ink(picture, cell), Is.EqualTo(Plain("CR...L.P", font, SystemColors.ControlText, cell.Size, direction)));
            });
        }

        [Test]
        public void A_struck_mask_in_a_group_is_drawn_whole_with_no_letter_in_bold()
        {
            // Versions for a name the database no longer names: struck through, and with no letter
            // in bold, since a rule that grants nothing has nothing to differ in.
            Show(Rules.Stored(("retired", "row:R"), ("retired", "row:RL")), RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                var at = Lines(panel).IndexOf("retired .R...L.. " + GrantTab.NoProfileWord);
                Assert.That(at, Is.GreaterThanOrEqualTo(0), string.Join("; ", Lines(panel)));
                Assert.That(((GrantRow)grid.Rows[at].Tag).Bound, Is.True);
                var cell = InPicture(grid, MaskColumn, at);
                var struck = grid.Rows[at].Cells[MaskColumn].Style.Font;
                Assert.That(struck.Strikeout, Is.True);

                Assert.That(Ink(picture, cell), Is.EqualTo(Plain(".R...L..", struck, SystemColors.ControlText, cell.Size, RightToLeft.No)));
            });
        }

        [Test]
        public void A_limit_group_is_drawn_with_the_inherited_rule_in_gray_its_bold_letters_and_a_bracket_in_the_text_color()
        {
            Show(Rules.Stored(("reader", "off:R")), RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                Assert.That(Lines(panel), Is.EqualTo(new[] { "reader CRU..L.. " + GrantTab.InheritedWord, "reader .R...... " + GrantTab.AtMostWord }));

                var inherited = InPicture(grid, MaskColumn, 0);
                var gray = SystemColors.GrayText;
                Assert.That(Darkest(picture, inherited), Is.GreaterThanOrEqualTo(((gray.R + gray.G + gray.B) / 3) - 16), "the inherited rule is gray");
                Assert.That(Darkest(picture, InPicture(grid, MaskColumn, 1)), Is.LessThan(64), "the limit in the text color");

                var font = grid.Columns[MaskColumn].DefaultCellStyle.Font;
                Assert.That(Ink(picture, inherited).Length, Is.GreaterThan(Plain("CRU..L..", font, gray, inherited.Size, RightToLeft.No).Length),
                    "the letters the limit takes away are bold on the gray row");

                var bracket = Bracket.Of(grid, false);
                Assert.That(bracket.LineColor(picture, 0).ToArgb(), Is.EqualTo(grid.DefaultCellStyle.ForeColor.ToArgb()), "never gray on the inherited row");
                Assert.That(bracket.LineColor(picture, 1).ToArgb(), Is.EqualTo(grid.DefaultCellStyle.ForeColor.ToArgb()));
            });
        }

        [Test]
        public void A_selected_cell_takes_the_selection_s_colors_on_every_kind_of_row_and_the_bracket_its_text_color()
        {
            var tints = RowTints.For(SystemInformation.HighContrast);
            Assume.That(tints.Bound.IsEmpty, Is.False, "the colors of a display in its ordinary colors");

            // A limit group, an inherited rule in gray among its rows: the inherited row's name
            // selected, then the limit's permissions.
            foreach (var selected in new[] { (Row: 0, Column: ProfileColumn), (Row: 1, Column: MaskColumn) })
            {
                Show(Rules.Stored(("reader", "off:R")), RightToLeft.No, 1f, (panel, grid, picture) =>
                {
                    var cell = InPicture(grid, selected.Column, selected.Row);
                    var other = InPicture(grid, selected.Column == ProfileColumn ? MaskColumn : ProfileColumn, selected.Row);

                    Assert.That(Paper(picture, cell).ToArgb(), Is.EqualTo(grid.DefaultCellStyle.SelectionBackColor.ToArgb()), "the selected cell");
                    Assert.That(Paper(picture, other).ToArgb(), Is.EqualTo(tints.Bound.ToArgb()), "the rest of the row keeps its color");

                    var bracket = Bracket.Of(grid, false);
                    var line = selected.Column == ProfileColumn ? grid.DefaultCellStyle.SelectionForeColor : grid.DefaultCellStyle.ForeColor;
                    Assert.That(bracket.LineColor(picture, 0).ToArgb(), Is.EqualTo((selected.Row == 0 ? line : grid.DefaultCellStyle.ForeColor).ToArgb()));
                    Assert.That(bracket.LineColor(picture, 1).ToArgb(), Is.EqualTo(grid.DefaultCellStyle.ForeColor.ToArgb()));
                }, grid =>
                {
                    grid.CurrentCell = grid.Rows[selected.Row].Cells[selected.Column];
                    grid.CurrentCell.Selected = true;
                });
            }
        }

        [Test]
        public void The_box_in_the_mixed_mark_takes_its_color_on_its_own_line_and_not_against_the_table()
        {
            var tints = RowTints.For(SystemInformation.HighContrast);
            Assume.That(tints.Bound.IsEmpty, Is.False, "the colors of a display in its ordinary colors");

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(
                GrantSheet.Of(Rules.Stored((null, "sw:deny"), (null, "sw:inherit"), ("ops", "row:RL")), null, Sheets.Names, null), GrantTab.GroupScope, null, null, null))
            {
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);
                Assert.That(box.CheckState, Is.EqualTo(CheckState.Indeterminate));

                using (var picture = new Bitmap(panel.Width, panel.Height))
                {
                    panel.DrawToBitmap(picture, new Rectangle(0, 0, panel.Width, panel.Height));
                    var x = box.Right - 3;

                    Assert.That(picture.GetPixel(x, grid.Bottom + 1).ToArgb(), Is.Not.EqualTo(tints.Bound.ToArgb()), "the color stops short of the table");
                    Assert.That(picture.GetPixel(x, box.Bottom - 2).ToArgb(), Is.EqualTo(tints.Bound.ToArgb()), "on the box's own line");
                }

                form.Close();
                OffScreenForm.Pump();
            }
        }

        [Test]
        public void The_colors_a_row_takes_are_drawn_behind_the_row()
        {
            var tints = RowTints.For(SystemInformation.HighContrast);

            Show(AloneAndThree, RightToLeft.No, 1f, (panel, grid, picture) =>
            {
                Assert.That(Paper(picture, InPicture(grid, StateColumn, 0)).ToArgb(), Is.EqualTo((tints.Changed.IsEmpty ? SystemColors.Window : tints.Changed).ToArgb()), "a changed row");
                Assert.That(Paper(picture, InPicture(grid, StateColumn, 2)).ToArgb(), Is.EqualTo((tints.Bound.IsEmpty ? SystemColors.Window : tints.Bound).ToArgb()), "a bound row");
            }, null, panel => panel.SetGrant("ops", "CRU....."));
        }

        [Test]
        public void A_fault_drawing_the_marks_is_recorded_once_and_the_table_drawn_without_marks_until_filled_again()
        {
            var incidents = new GateIncidents(new RecordingGateLog());
            WindowHandlers.Incidents = incidents;
            try
            {
                Show(AloneAndThree, RightToLeft.No, 1f, (panel, grid, picture) =>
                {
                    // Marks that cannot be drawn, on a row whose mask is drawn from them.
                    var broken = GrantMarks.Of(new[] { (GrantRow)grid.Rows[3].Tag }, true)[0];
                    typeof(GrantMarks).GetField("<Bold>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(broken, null);
                    grid.Rows[3].GetType().GetProperty("Marks").SetValue(grid.Rows[3], broken);

                    // Drawn into a picture each time: a window off the screen is sent no paint.
                    for (var repaint = 0; repaint < 3; repaint++)
                    {
                        grid.DrawToBitmap(picture, new Rectangle(0, 0, grid.Width, grid.Height));
                    }

                    Assert.That(incidents.Recent.Count(line => line.StartsWith("the rules could not be drawn", StringComparison.Ordinal)), Is.EqualTo(1));
                    Assert.That(Lines(panel), Has.Count.EqualTo(4), "the words are all there");

                    panel.SetGrant("ops", "CRU.....");
                    grid.DrawToBitmap(picture, new Rectangle(0, 0, grid.Width, grid.Height));
                    Assert.That(incidents.Recent.Count(line => line.StartsWith("the rules could not be drawn", StringComparison.Ordinal)), Is.EqualTo(1),
                        "filled again, the marks are drawn from marks that can be");
                });
            }
            finally
            {
                WindowHandlers.Incidents = null;
            }
        }

        private static IList<string> Lines(GrantPanel panel)
        {
            return panel.Lines;
        }

        /// <summary>
        /// A shown window with the editor on it at a size of text, drawn once the table has settled,
        /// torn down whatever the test does.
        /// </summary>
        private static void Show(
            NodeRule rule,
            RightToLeft direction,
            float scaling,
            Action<GrantPanel, DataGridView, Bitmap> work,
            Action<DataGridView> before = null,
            Action<GrantPanel> press = null)
        {
            var names = new[] { "ops", "reader", "a" }.Concat(Enumerable.Range(0, 24).Select(i => "p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)));
            var sheet = GrantSheet.Of(rule, Sheets.Arriving(("reader", "CRUL")), names, null);

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(sheet, GrantTab.GroupScope, null, null, null))
            using (var font = new Font(form.Font.FontFamily, form.Font.Size * scaling))
            {
                form.ClientSize = new Size((int)(560 * scaling), (int)(420 * scaling));
                form.RightToLeft = direction;
                panel.Font = font;
                panel.Dock = DockStyle.Fill;
                form.Controls.Add(panel);
                form.Show();
                OffScreenForm.Pump();

                if (press != null)
                {
                    press(panel);
                }

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(grid, Is.Not.Null);
                Assert.That(grid.RightToLeft, Is.EqualTo(direction), "the table follows the window");

                grid.CurrentCell = null;
                grid.ClearSelection();

                if (before != null)
                {
                    before(grid);
                }

                OffScreenForm.Pump();

                try
                {
                    using (var picture = new Bitmap(grid.Width, grid.Height))
                    {
                        grid.DrawToBitmap(picture, new Rectangle(0, 0, grid.Width, grid.Height));
                        work(panel, grid, picture);
                    }
                }
                finally
                {
                    form.Close();
                    OffScreenForm.Pump();
                }
            }
        }

        /// <summary>
        /// Every drawn pixel of a cell, as offsets from the cell's corner, leaving out the cell's
        /// own lines at its sides and its bottom.
        /// </summary>
        private static string[] Ink(Bitmap picture, Rectangle cell)
        {
            var drawn = new List<string>();
            for (var y = 0; y < cell.Height - 1; y++)
            {
                drawn.AddRange(Enumerable.Range(1, cell.Width - 2)
                    .Where(x => Inked(picture, cell.Left + x, cell.Top + y))
                    .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + y.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            return drawn.ToArray();
        }

        /// <summary>
        /// What the grid draws on its own for a text in a cell of this size, font, color and
        /// direction, with nothing of the editor's over it, as <see cref="Ink"/> reads a cell.
        /// </summary>
        private static string[] Plain(string text, Font font, Color color, Size size, RightToLeft direction)
        {
            using (var form = OffScreenForm.New())
            using (var grid = new DataGridView
            {
                RowHeadersVisible = false,
                ColumnHeadersVisible = false,
                AllowUserToAddRows = false,
                ScrollBars = ScrollBars.None,
                BorderStyle = BorderStyle.None,
                BackgroundColor = SystemColors.Window,
                RightToLeft = direction,
                Dock = DockStyle.Fill,
            })
            {
                var column = new DataGridViewTextBoxColumn { Width = size.Width, SortMode = DataGridViewColumnSortMode.NotSortable };
                column.DefaultCellStyle.Font = font;
                column.DefaultCellStyle.ForeColor = color;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                grid.Columns.Add(column);
                grid.RowTemplate.Height = size.Height;
                grid.Rows.Add(text);

                form.Controls.Add(grid);
                form.Show();
                OffScreenForm.Pump();
                grid.CurrentCell = null;
                grid.ClearSelection();
                OffScreenForm.Pump();

                var cell = InPicture(grid, 0, 0);
                Assert.That(cell.Size, Is.EqualTo(size), "the cell drawn on its own is the size of the cell compared");

                using (var picture = new Bitmap(grid.Width, grid.Height))
                {
                    grid.DrawToBitmap(picture, new Rectangle(0, 0, grid.Width, grid.Height));
                    var drawn = Ink(picture, cell);
                    form.Close();
                    OffScreenForm.Pump();
                    return drawn;
                }
            }
        }

        /// <summary>The darkest of a cell's pixels, as the mean of its channels.</summary>
        private static int Darkest(Bitmap picture, Rectangle cell)
        {
            var darkest = 255;
            for (var y = cell.Top + 1; y < cell.Bottom - 1; y++)
            {
                for (var x = cell.Left + 1; x < cell.Right - 1; x++)
                {
                    var pixel = picture.GetPixel(x, y);
                    darkest = Math.Min(darkest, (pixel.R + pixel.G + pixel.B) / 3);
                }
            }

            return darkest;
        }

        /// <summary>The color of a cell's paper, read near its top at the side away from its text.</summary>
        private static Color Paper(Bitmap picture, Rectangle cell)
        {
            return picture.GetPixel(cell.Right - 3, cell.Top + 2);
        }

        /// <summary>Whether a pixel is drawn rather than paper, whichever color the paper is.</summary>
        private static bool Inked(Bitmap picture, int x, int y)
        {
            var pixel = picture.GetPixel(x, y);
            return (pixel.R + pixel.G + pixel.B) / 3 < 128;
        }

        /// <summary>
        /// Where a cell of the table is in a picture of the whole table: inside the frame the
        /// picture holds and the cell's rectangle does not.
        /// </summary>
        private static Rectangle InPicture(DataGridView grid, int column, int row)
        {
            var cell = grid.GetCellDisplayRectangle(column, row, false);
            Assert.That(cell.Width, Is.GreaterThan(0), "row " + row + " was never laid out");
            cell.Offset((grid.Width - grid.ClientSize.Width) / 2, (grid.Height - grid.ClientSize.Height) / 2);
            return cell;
        }

        /// <summary>Where the bracket of a row is drawn in the profile's cell, by its gap and its line.</summary>
        private sealed class Bracket
        {
            private readonly DataGridView _grid;
            private readonly bool _mirrored;

            private Bracket(DataGridView grid, bool mirrored)
            {
                _grid = grid;
                _mirrored = mirrored;
                Room = GrantMarks.Room(grid.Font);
                Stroke = GrantMarks.Stroke(grid.Font);
            }

            public int Room { get; }

            public int Stroke { get; }

            public static Bracket Of(DataGridView grid, bool mirrored)
            {
                return new Bracket(grid, mirrored);
            }

            /// <summary>
            /// How many pixels are drawn down the fullest column of the bracket's room, between the
            /// arms' places: the whole height on a bound row, and less on a name standing alone.
            /// </summary>
            public int Bar(Bitmap picture, int row)
            {
                var cell = Cell(row);
                var fullest = 0;
                for (var offset = 0; offset < Room; offset++)
                {
                    var x = X(cell, offset);
                    var drawn = 0;
                    for (var y = cell.Top + Band(cell); y < cell.Bottom - 1 - Band(cell); y++)
                    {
                        drawn += Inked(picture, x, y) ? 1 : 0;
                    }

                    fullest = Math.Max(fullest, drawn);
                }

                return fullest;
            }

            /// <summary>How many pixels <see cref="Bar"/> looks at on a row.</summary>
            public int BarHeight(int row)
            {
                var cell = Cell(row);
                return cell.Height - 1 - (2 * Band(cell));
            }

            /// <summary>How wide the line is drawn halfway down a row.</summary>
            public int BarWidth(Bitmap picture, int row)
            {
                var cell = Cell(row);
                var y = cell.Top + (cell.Height / 2);
                var drawn = 0;
                for (var offset = 0; offset < Room - Stroke; offset++)
                {
                    drawn += Inked(picture, X(cell, offset), y) ? 1 : 0;
                }

                return drawn;
            }

            /// <summary>How much is drawn past the line in the top band of a row, where the arm closing over the row reaches toward the name.</summary>
            public int TopArm(Bitmap picture, int row)
            {
                var cell = Cell(row);
                return Arm(picture, cell, Line(picture, row), cell.Top);
            }

            /// <summary>How much is drawn past the line in the bottom band of a row, where the arm closing under the row reaches toward the name.</summary>
            public int BottomArm(Bitmap picture, int row)
            {
                var cell = Cell(row);
                return Arm(picture, cell, Line(picture, row), cell.Bottom - 1 - Band(cell));
            }

            /// <summary>The color the line of a bound row is drawn in, halfway down the row.</summary>
            public Color LineColor(Bitmap picture, int row)
            {
                var cell = Cell(row);
                return picture.GetPixel(X(cell, Stroke), cell.Top + (cell.Height / 2));
            }

            /// <summary>Whether the line reaches the top or the bottom edge of a row.</summary>
            public bool Edge(Bitmap picture, int row, bool top)
            {
                var cell = Cell(row);
                return Inked(picture, X(cell, Line(picture, row)), top ? cell.Top : cell.Bottom - 1);
            }

            /// <summary>
            /// Where the name is drawn in a row's cell, as offsets from the cell's leading edge:
            /// the first drawn column past the bracket's arms, and the width up to the last drawn
            /// column.
            /// </summary>
            public Rectangle Text(Bitmap picture, int row)
            {
                var cell = Cell(row);
                var past = Bar(picture, row) == BarHeight(row) ? Room - Stroke : 0;
                var first = -1;
                var last = -1;
                for (var offset = past; offset < cell.Width - 1; offset++)
                {
                    var x = X(cell, offset);
                    var drawn = false;
                    for (var y = cell.Top; y < cell.Bottom - 1 && !drawn; y++)
                    {
                        drawn = Inked(picture, x, y);
                    }

                    if (drawn)
                    {
                        first = first < 0 ? offset : first;
                        last = offset;
                    }
                }

                Assert.That(first, Is.GreaterThanOrEqualTo(0), "no name drawn on row " + row);
                return new Rectangle(first, 0, last - first + 1, 0);
            }

            /// <summary>How much is drawn in a band from <paramref name="top"/>, between the line and the name.</summary>
            private int Arm(Bitmap picture, Rectangle cell, int line, int top)
            {
                var drawn = 0;
                for (var offset = line + Stroke; offset < Room; offset++)
                {
                    for (var y = top; y < top + Band(cell); y++)
                    {
                        drawn += Inked(picture, X(cell, offset), y) ? 1 : 0;
                    }
                }

                return drawn;
            }

            /// <summary>Where the line of a bound row starts, as an offset from the leading edge.</summary>
            private int Line(Bitmap picture, int row)
            {
                var cell = Cell(row);
                for (var offset = 0; offset < Room; offset++)
                {
                    var whole = true;
                    for (var y = cell.Top + Band(cell); y < cell.Bottom - 1 - Band(cell) && whole; y++)
                    {
                        whole = Inked(picture, X(cell, offset), y);
                    }

                    if (whole)
                    {
                        return offset;
                    }
                }

                Assert.Fail("row " + row + " holds no line in the room of a bracket");
                return -1;
            }

            /// <summary>
            /// How deep the bands at a row's top and bottom are, where an arm is drawn: three lines
            /// of the bracket, and no more than a quarter of the row however the row is sized.
            /// </summary>
            private int Band(Rectangle cell)
            {
                return Math.Max(1, Math.Min(3 * Stroke, cell.Height / 4));
            }

            private Rectangle Cell(int row)
            {
                return InPicture(_grid, ProfileColumn, row);
            }

            /// <summary>The column a distance in from the cell's leading edge, which is its right right to left.</summary>
            private int X(Rectangle cell, int offset)
            {
                return _mirrored ? cell.Right - 1 - offset : cell.Left + offset;
            }
        }

        /// <summary>The eight places of a mask, measured off a row whose mask starts with a letter.</summary>
        private sealed class Slots
        {
            private readonly DataGridView _grid;
            private readonly int _origin;
            private readonly double _pitch;

            private Slots(DataGridView grid, int origin, double pitch)
            {
                _grid = grid;
                _origin = origin;
                _pitch = pitch;
            }

            /// <summary>The places, taken from where the first letter of a row's mask is drawn.</summary>
            public static Slots Of(DataGridView grid, Bitmap picture, int row)
            {
                var font = grid.Columns[MaskColumn].DefaultCellStyle.Font;
                var pitch = TextRenderer.MeasureText(VerbMask.Letters, font, Size.Empty, TextFormatFlags.NoPadding).Width
                            / (double)VerbMask.Width;

                var cell = Area(grid, row);
                var origin = -1;
                for (var x = cell.Left; x < cell.Right && origin < 0; x++)
                {
                    for (var y = cell.Top; y < cell.Bottom; y++)
                    {
                        if (Inked(picture, x, y))
                        {
                            origin = x;
                            break;
                        }
                    }
                }

                Assert.That(origin, Is.GreaterThanOrEqualTo(0), "no mask drawn on row " + row);

                // A letter's first drawn column is a pixel or so into its place.
                return new Slots(grid, origin - 1, pitch);
            }

            /// <summary>How much is drawn in each place of a row's mask.</summary>
            public int[] Ink(Bitmap picture, int row)
            {
                var cell = Area(_grid, row);
                var ink = new int[VerbMask.Width];
                for (var x = cell.Left; x < cell.Right; x++)
                {
                    var at = (int)Math.Floor((x - _origin) / _pitch);
                    if (at < 0 || at >= ink.Length)
                    {
                        continue;
                    }

                    for (var y = cell.Top; y < cell.Bottom; y++)
                    {
                        ink[at] += Inked(picture, x, y) ? 1 : 0;
                    }
                }

                return ink;
            }

            private static Rectangle Area(DataGridView grid, int row)
            {
                var cell = InPicture(grid, MaskColumn, row);
                cell.Inflate(-1, -1);
                return cell;
            }
        }
    }
}
