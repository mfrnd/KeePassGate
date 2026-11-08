using System.Linq;
using System.Windows.Forms;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a screen reader is told about a column of buttons whose heading is a picture.
    /// </summary>
    /// <remarks>
    /// The clients, the profiles and the rules each end in a column of Xs under a painted bin, and
    /// a table names a cell to UI automation by its column's heading and its row. So the heading
    /// carries the words, and they must cost the column nothing: not its width, which is an X's,
    /// and not a taller heading row, which every page has to find room for.
    /// </remarks>
    internal static class ButtonColumns
    {
        /// <summary>
        /// Asserts that the heading names the column and every button in it, on one line and in
        /// no more width than the buttons take.
        /// </summary>
        public static void AssertNamedBy(Control owner, DataGridView grid, string column, string heading)
        {
            // Made, so that the columns and the heading row are sized as a window sizes them.
            owner.CreateControl();

            var buttons = grid.Columns[column];
            Assert.That(buttons, Is.Not.Null, "the table has no column called " + column);
            Assert.That(buttons.HeaderCell.AccessibilityObject.Name, Is.EqualTo(heading),
                "the heading names nothing to a screen reader");

            var rows = grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).ToList();
            Assert.That(rows, Is.Not.Empty, "there is no button to ask about");
            foreach (var row in rows)
            {
                Assert.That(row.Cells[column].AccessibilityObject.Name, Does.StartWith(heading),
                    "a button is named by its row alone, with nothing about what pressing it does");
            }

            var font = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
            Assert.That(buttons.Width, Is.LessThan(TextRenderer.MeasureText(heading, font).Width),
                "the heading's words widened the column");
            Assert.That(grid.ColumnHeadersHeight, Is.LessThan(2 * font.Height),
                "the heading's words made the heading row taller");
        }
    }
}
