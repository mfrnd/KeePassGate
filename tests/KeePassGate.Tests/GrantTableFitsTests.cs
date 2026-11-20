using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the grant table has to fit, measured on a table that has actually been laid out.
    /// </summary>
    /// <remarks>
    /// Its own fixture, and shown, because a `DataGridView` with no window handle never sizes
    /// anything: it answers with whatever was written down and the measurement believes it.
    /// `GrantPanelTests` cannot see this for the same reason it cannot see a dropdown open.
    ///
    /// **Shown, so it runs alone and holds the desktop**: a window shown takes the activation
    /// from whatever held it, in this process or another test process on the same screen.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    public sealed class GrantTableFitsTests
    {
        private const int ProfileColumn = GrantTab.ProfileColumn;
        private const int MaskColumn = GrantTab.MaskColumn;

        [Test]
        public void The_column_headings_are_as_tall_as_the_font_they_are_drawn_in()
        {
            // A `DataGridView` writes its heading row down as 23 pixels and draws it in a font
            // that follows the desktop's scaling, so on a display at 200 percent the headings are
            // taller than the row holding them and lose their tops. **The row gives way, not the
            // text.** Shrinking the font to fit a written down height would make this table the one
            // control on the tab reading smaller than everything around it, and it would do it
            // only where the text was already hardest to read.
            using (var form = OffScreenForm.New())
            using (var panel = Table())
            {
                Lay(form, panel);

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(grid, Is.Not.Null);

                var ordinary = grid.ColumnHeadersHeight;
                Assert.That(ordinary, Is.GreaterThanOrEqualTo(Tallest(grid)),
                    "the headings do not fit at the font this display is already using");

                grid.Font = new Font(grid.Font.FontFamily, grid.Font.Size * 2f);
                OffScreenForm.Pump();

                Assert.That(grid.ColumnHeadersHeight, Is.GreaterThan(ordinary),
                    "a heading row that does not move is a heading row for one display");
                Assert.That(grid.ColumnHeadersHeight, Is.GreaterThanOrEqualTo(Tallest(grid)),
                    "the headings are taller than the row they are drawn in");
            }
        }

        [Test]
        public void A_column_that_is_headed_at_all_is_readable_rather_than_shortened()
        {
            // The heading is what the letters in the cells below it are explained by, so a column
            // whose heading is cut is a column of masks nobody has a word for. This is the same
            // measurement across the table rather than on the one column that happens to be
            // longest, because which one that is depends on the font.
            using (var form = OffScreenForm.New())
            using (var panel = Table())
            {
                Lay(form, panel);

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (string.IsNullOrEmpty(column.HeaderText))
                    {
                        continue;
                    }

                    var needed = TextRenderer.MeasureText(column.HeaderText, Heading(grid)).Height;

                    Assert.That(grid.ColumnHeadersHeight, Is.GreaterThanOrEqualTo(needed),
                        "\"" + column.HeaderText + "\" needs " + needed
                        + " and the row is " + grid.ColumnHeadersHeight);
                }
            }
        }

        [Test]
        public void The_permissions_heading_fits_the_column_it_heads()
        {
            // The heading is what the letters below it are explained by, so a column that cuts it
            // is a column of masks with no word for what they are. It is measured rather than
            // given a written down floor: a floor of 70 pixels fitted "Permissions" on a display
            // at 100 percent and cut it on one at 200, which is the same defect as a heading row
            // written down as 23.
            using (var form = OffScreenForm.New())
            using (var panel = Table())
            {
                Lay(form, panel);

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                var mask = grid.Columns[MaskColumn];

                Assert.That(mask.HeaderText, Is.EqualTo(GrantTab.MaskColumnHeader));
                Assert.That(mask.Width, Is.GreaterThanOrEqualTo(Needs(mask)),
                    "the permissions heading is cut off by the column it heads");
            }
        }

        [Test]
        public void The_permissions_column_measures_rather_than_being_written_down()
        {
            // What makes the heading fit at every scaling is that the column is sized to what is
            // in it, the heading counted among it. A width or a floor in pixels is a width for the
            // display somebody had open when they wrote it.
            using (var form = OffScreenForm.New())
            using (var panel = Table())
            {
                Lay(form, panel);

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                var mask = grid.Columns[MaskColumn];

                Assert.That(mask.AutoSizeMode, Is.EqualTo(DataGridViewAutoSizeColumnMode.AllCells));

                // And the floor under it, which keeps a table with no rule yet as wide as a table
                // of rules, is measured off a mask in the column's own font: a floor that stays
                // where it was at a larger font is a floor for a single display.
                var floor = mask.MinimumWidth;
                using (var larger = new Font(panel.Font.FontFamily, panel.Font.Size * 2f))
                {
                    panel.Font = larger;
                    OffScreenForm.Pump();

                    Assert.That(mask.MinimumWidth, Is.GreaterThan(floor), "the floor stayed where it was");
                    Assert.That(mask.Width, Is.GreaterThanOrEqualTo(Needs(mask)), "the heading is cut at the larger font");
                }
            }
        }

        [Test]
        public void The_permissions_column_leaves_the_profile_the_room_it_needs()
        {
            // A column that is as wide as its heading is not a column that has taken the table.
            // The profile name is the one thing here with no known length, so it takes what the
            // other two leave, and what they leave has to be most of it.
            using (var form = OffScreenForm.New())
            using (var panel = Table())
            {
                Lay(form, panel);

                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(grid.Columns[ProfileColumn].Width,
                    Is.GreaterThan(grid.Columns[MaskColumn].Width),
                    "the column with no known length is the narrow one");
            }
        }

        [Test]
        public void The_columns_keep_their_widths_whatever_a_press_changes()
        {
            // A press takes rows away, and with them the vertical scroll bar, and changes the words
            // the State column shows. Left to the grid, the profile column gives the scroll bar its
            // room as the bar comes and goes, and the State column is as wide as the words shown;
            // no column moves.
            var sheet = GrantSheet.Of(
                Rules.Stored(("deploy", "row:R"), ("deploy", "row:RL"), ("deploy", "row:CRL"), ("ops", "row:RL"), (null, "sw:deny")),
                Sheets.Arriving(("reader", "RL")),
                new[] { "deploy", "ops", "reader" },
                null);

            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(sheet, GrantTab.GroupScope, null, null, null))
            {
                Lay(form, panel);
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                // Room for four rows and a half: the six rows the table starts with scroll, and the
                // four left once two versions are removed do not.
                var row = grid.Rows[0].Height;
                form.ClientSize = new Size(form.ClientSize.Width, form.ClientSize.Height - grid.Height + grid.ColumnHeadersHeight + (row * 9 / 2));
                OffScreenForm.Pump();
                Assert.That(Scrolls(grid), Is.True, "the table starts with no scroll bar, so its going is not measured");
                Assert.That(Sideways(grid), Is.False, "the table scrolls sideways beside the scroll bar");

                var before = Widths(grid);
                TestContext.Out.WriteLine("widths " + string.Join(", ", before) + ": " + string.Join(" | ", panel.Lines));

                foreach (var version in sheet.Rows.Where(shown => shown.Profile == "deploy").Take(2).Select(shown => shown.Version).ToList())
                {
                    panel.Press(grid.Rows.Cast<DataGridViewRow>().Single(line => !line.IsNewRow && ((GrantRow)line.Tag).Version == version).Index, GrantTab.ForgetColumn);
                    OffScreenForm.Pump();
                }

                Assert.That(Scrolls(grid), Is.False, "the rows left still scroll, so the bar's going is not measured: " + string.Join(" | ", panel.Lines));
                Assert.That(Widths(grid), Is.EqualTo(before), "the scroll bar went: " + string.Join(" | ", panel.Lines));
                Assert.That(Sideways(grid), Is.False, "the table scrolls sideways with the scroll bar gone");

                panel.PressBox();
                OffScreenForm.Pump();
                Assert.That(panel.Lines.Any(line => line.Contains(GrantTab.RefusedHereWord)), Is.False, "the box still refuses, so no word changed");
                Assert.That(Widths(grid), Is.EqualTo(before), "the words changed: " + string.Join(" | ", panel.Lines));
                Assert.That(Sideways(grid), Is.False);
            }
        }

        [Test]
        public void A_table_s_first_rule_moves_no_column()
        {
            using (var form = OffScreenForm.New())
            using (var panel = new GrantPanel(GrantSheet.Of(NodeRule.None, null, new[] { "reader" }, null), GrantTab.GroupScope, null, null, null))
            {
                Lay(form, panel);
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                var before = Widths(grid);

                Assert.That(panel.AddGrant("reader"), Is.Null);
                OffScreenForm.Pump();

                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader ........" }));
                Assert.That(Widths(grid), Is.EqualTo(before), "widths before " + string.Join(", ", before) + ", after " + string.Join(", ", Widths(grid)));
            }
        }

        /// <summary>The width of every column of the table, in order.</summary>
        private static int[] Widths(DataGridView grid)
        {
            return grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width).ToArray();
        }

        /// <summary>Whether the table shows its vertical scroll bar.</summary>
        private static bool Scrolls(DataGridView grid)
        {
            return grid.Controls.OfType<VScrollBar>().Any(bar => bar.Visible);
        }

        /// <summary>Whether the table shows a scroll bar across, which nothing on a tab may.</summary>
        private static bool Sideways(DataGridView grid)
        {
            return grid.Controls.OfType<HScrollBar>().Any(bar => bar.Visible);
        }

        /// <summary>What this column's heading needs, which is what the grid says it needs.</summary>
        private static int Needs(DataGridViewColumn column)
        {
            return column.GetPreferredWidth(DataGridViewAutoSizeColumnMode.ColumnHeader, true);
        }

        /// <summary>The tallest heading in the table, which is what the row has to hold.</summary>
        private static int Tallest(DataGridView grid)
        {
            var tallest = 0;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (string.IsNullOrEmpty(column.HeaderText))
                {
                    continue;
                }

                var height = TextRenderer.MeasureText(column.HeaderText, Heading(grid)).Height;
                if (height > tallest)
                {
                    tallest = height;
                }
            }

            return tallest;
        }

        /// <summary>What a heading is drawn in, which is the grid's own font unless one is set.</summary>
        private static Font Heading(DataGridView grid)
        {
            return grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
        }

        [Test]
        public void The_state_column_is_as_wide_as_its_longest_word_and_its_heading()
        {
            // A word cut in its column is a state nobody can read, and the State column is sized
            // to what is in it, as the Permissions column is, rather than to a width in pixels.
            var shown = new List<string>();
            foreach (var refusing in new[] { false, true })
            {
                using (var form = OffScreenForm.New())
                using (var panel = EveryState(refusing))
                {
                    Lay(form, panel);

                    var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                    var state = grid.Columns[GrantTab.StateColumn];
                    var font = grid.DefaultCellStyle.Font ?? grid.Font;
                    var words = grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
                        .Select(row => (string)row.Cells[GrantTab.StateColumn].Value).ToList();
                    shown.AddRange(words);

                    Assert.That(state.AutoSizeMode, Is.EqualTo(DataGridViewAutoSizeColumnMode.AllCells));
                    foreach (var word in words)
                    {
                        Assert.That(state.Width, Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(word, font).Width), word);
                    }

                    Assert.That(state.Width, Is.GreaterThanOrEqualTo(Needs(state)), "the heading is cut off by the column it heads");
                    Assert.That(grid.Columns[ProfileColumn].Width,
                        Is.GreaterThanOrEqualTo(TextRenderer.MeasureText(GrantTab.AddRowPrompt, grid.Font).Width),
                        "the prompt of the empty line is cut");
                }
            }

            Assert.That(shown, Is.SupersetOf(Words), "every word was on a table measured");
        }

        /// <summary>Every word the State column can show.</summary>
        internal static readonly string[] Words =
        {
            GrantTab.NoProfileWord, GrantTab.LostWord, GrantTab.VersionWord, GrantTab.TakenOffWord,
            GrantTab.LimitWord, GrantTab.RefusedHereWord, GrantTab.InheritedWord,
        };

        private static GrantPanel Table()
        {
            return new GrantPanel(
                GrantSheet.Of(Rules.Node(false, ("editor", "RU")), Sheets.Arriving(("reader", "R")), new[] { "reader", "editor" }, null),
                GrantTab.GroupScope,
                null, null, null);
        }

        /// <summary>
        /// A table with a row of every state a box allows, a full mask among the rows: the box
        /// cleared shows the limit, the version taken off and the name gone, and the box ticked
        /// shows what the box refuses.
        /// </summary>
        internal static GrantPanel EveryState(bool refusing)
        {
            var versions = new List<(string Profile, string Text)>
            {
                ("deploy", "row:CRUSDLAP"), ("deploy", "off:R"), ("lost", null), ("ops", "off:R"), ("gone", "row:R"),
            };
            if (refusing)
            {
                versions.Add((null, "sw:deny"));
            }

            var above = Sheets.Arriving(("ops", "CRUSDLAP"), ("reader", "RL"));
            var sheet = GrantSheet.Of(Rules.Stored(versions.ToArray()), above, new[] { "deploy", "lost", "ops", "reader" }, null);
            return new GrantPanel(sheet, GrantTab.GroupScope, null, null, null);
        }

        private static void Lay(Form form, GrantPanel panel)
        {
            panel.Dock = DockStyle.Fill;
            form.Controls.Add(panel);
            form.Show();
            OffScreenForm.Pump();
        }

    }
}
