using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
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
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
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

                // And a floor is not what holds it up, so there is none to go stale.
                Assert.That(mask.MinimumWidth, Is.LessThanOrEqualTo(Needs(mask)));
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

        private static GrantPanel Table()
        {
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);

            return new GrantPanel(
                new GrantSheet("{\"editor\":[\"read\",\"update\"]}", above, new[] { "reader", "editor" }, null),
                new FakeGrantStore("{\"editor\":[\"read\",\"update\"]}") { Refusal = new InvalidOperationException("this measurement writes nothing") },
                GrantTab.GroupScope,
                null);
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
