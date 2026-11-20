using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The rows of an approval dialog's table of profiles, read off the controls the table is
    /// made of: each row a panel holding its check box and a cell of text for each column after
    /// the boxes, in the order of the columns.
    /// </summary>
    internal static class ProfileRows
    {
        /// <summary>The table of this name on a dialog, asserted to be there.</summary>
        public static Control Table(Control dialog, string name)
        {
            var table = ControlTree.FindByName<Control>(dialog, name);
            Assert.That(table, Is.Not.Null, "no table of profiles called " + name);
            return table;
        }

        /// <summary>Every row of a table, in the order of the rows.</summary>
        public static IReadOnlyList<Control> Of(Control table)
        {
            return ControlTree.Descendants(table)
                .Where(control => control.Name == GateTab.ProfileRowName)
                .ToList();
        }

        /// <summary>The check box of a row.</summary>
        public static CheckBox Box(Control row)
        {
            return ControlTree.FindByName<CheckBox>(row, GateTab.ProfileBoxName);
        }

        /// <summary>The check box of every row, in the order of the rows.</summary>
        public static IReadOnlyList<CheckBox> Boxes(Control table)
        {
            return Of(table).Select(Box).ToList();
        }

        /// <summary>Whether each row's box is checked, in the order of the rows.</summary>
        public static bool[] Checks(Control table)
        {
            return Boxes(table).Select(box => box.Checked).ToArray();
        }

        /// <summary>The cell of a row under the column of this name.</summary>
        public static Label Cell(Control row, string column)
        {
            var cell = ControlTree.FindByName<Label>(row, column);
            Assert.That(cell, Is.Not.Null, "no cell under " + column);
            return cell;
        }

        /// <summary>The cells of text of every row, in the order of the columns.</summary>
        public static IReadOnlyList<string[]> Texts(Control table)
        {
            return Of(table).Select(row => row.Controls.OfType<Label>().Select(cell => cell.Text).ToArray()).ToList();
        }

        /// <summary>
        /// What UI automation reads of each row's box, which is the row's text read out, in the
        /// order of the rows.
        /// </summary>
        public static IReadOnlyList<string> Spoken(Control table)
        {
            return Boxes(table).Select(box => box.AccessibleName).ToList();
        }

        /// <summary>The table's tooltips, which are a component rather than a control.</summary>
        public static ToolTip Tips(Control table)
        {
            var field = table.GetType().GetField("_tips", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "the table keeps no tooltips");
            return (ToolTip)field.GetValue(table);
        }

        /// <summary>The panel the rows scroll in, which is the parent of every row.</summary>
        public static ScrollableControl View(Control table)
        {
            var rows = Of(table);
            Assert.That(rows, Is.Not.Empty, "a table with no row has no view to find");
            return (ScrollableControl)rows[0].Parent;
        }
    }
}
