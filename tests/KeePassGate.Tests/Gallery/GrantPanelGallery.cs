using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// The grant tab in every state a row, the box and the page can be in.
    /// </summary>
    /// <remarks>
    /// What varies here is what each row stands for and how much room the table is left with: a
    /// reason over the table takes a band, the rows of a rule in several versions and of a limit
    /// take room of their own, and more rows than fit take a scroll bar. The texts of every cell,
    /// line and tooltip are written to the test's output beside each picture, so that a reviewer
    /// reads what a tooltip holds without hovering over a picture.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    [Desktop]
    [Category(Gallery.Category)]
    public sealed class GrantPanelGallery
    {
        private static readonly string[] Names = { "ops", "reader", "deploy" };

        [Test]
        public void Screenshot()
        {
            // Nothing on the node and nothing to grant to: the state a database is in before
            // anybody has done anything, and the page points at where a profile comes from.
            Shoot("grant-01-no-profiles-named", GrantSheet.Of(NodeRule.None, null, Array.Empty<string>(), null), panel => panel.OpenProfilePicker(0));

            // Rules of the node's own beside rules from the group above, with what a rule here
            // does to the rule it replaces in the tooltip of its mask.
            Shoot(
                "grant-02-own-and-inherited",
                Sheet(Rules.Node(false, ("ops", "CRUSDLAP"), ("reader", "R")), Sheets.Arriving(("reader", "RL"), ("deploy", "RLA"))));

            // A profile the node cuts: the inherited row, and bound under it the limit.
            var cut = Sheet(Rules.Stored(("reader", "off:R"), ("ops", "row:RL")), Sheets.Arriving(("reader", "CRUL"), ("ops", "R")));
            Shoot("grant-03-limit", cut);

            // The same after the X on the limit: the inherited row stands alone until OK.
            cut.Settle("reader");
            Shoot("grant-04-limit-settled", cut);

            // A rule in several versions made in copies before the copies met, a version that
            // took the rule off among them: each version a row of its own, bound, with an X and
            // no list.
            Shoot(
                "grant-05-versions",
                Sheet(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "off:R"), ("ops", "row:R")), Sheets.Arriving(("reader", "CRUL"))));

            // A rule taken off on the tab: the inherited rule in the row's place until OK, changed.
            var taken = Sheet(Rules.Node(false, ("reader", "R"), ("ops", "RL")), Sheets.Arriving(("reader", "RL")));
            taken.Remove("reader");
            Shoot("grant-06-taken-off-pending", taken);

            // The box ticked, which refuses the rule from above.
            Shoot("grant-07-box-ticked", Sheet(Rules.Node(true, ("ops", "RL")), Sheets.Arriving(("reader", "RL"))));

            // The box cleared by a press, changed until OK.
            var cleared = Sheet(Rules.Node(true, ("ops", "RL")), Sheets.Arriving(("reader", "RL")));
            cleared.PressBox();
            Shoot("grant-08-box-cleared", cleared);

            // The box in versions that differ: the mixed mark, counted as ticked.
            Shoot("grant-09-box-mixed", Sheet(Rules.Stored((null, "sw:deny"), (null, "sw:inherit"), ("ops", "row:RL")), Sheets.Arriving(("reader", "RL"))));

            // A lost rule, a rule that cannot be read, and a name the database no longer names.
            Shoot(
                "grant-10-lost-and-no-profile",
                GrantSheet.Of(
                    Rules.Stored(("reader", null), ("ops", "row:x"), ("retired", "row:R")),
                    Sheets.Arriving(("deploy", "R")),
                    Names,
                    null));

            // Read only, with the reason over the table.
            Shoot(
                "grant-11-read-only",
                GrantSheet.Of(
                    Rules.Stored(("reader", "row:RL"), ("reader", "row:R"), ("ops", "off:R")),
                    Sheets.Arriving(("ops", "RL")),
                    Names,
                    GrantTab.EarlierVersion));

            // Read only before the database has an authority.
            Shoot("grant-12-no-authority", GrantSheet.Of(NodeRule.None, Sheets.Arriving(("reader", "RL")), Names, GrantTab.NoAuthorityYet));

            // The longest texts at once: the longest reason, the widest masks, every state word,
            // a long name.
            Shoot(
                "grant-13-longest-texts",
                GrantSheet.Of(
                    Rules.Stored(
                        ("a-profile-with-a-name-somebody-really-did-type", "row:CRUSDLAP"),
                        ("deploy", "row:CRUSDLAP"), ("deploy", "off:CRUSDLAP"), ("ops", "off:R"), ("reader", null)),
                    Sheets.Arriving(("ops", "CRUSDLAP"), ("retired", "CRUSDLAP")),
                    new[] { "a-profile-with-a-name-somebody-really-did-type", "deploy", "ops", "reader" },
                    GrantTab.EarlierVersion));

            // More rows than the table shows at once, with the box ticked over all of them.
            Shoot(
                "grant-14-scrolling",
                Sheet(
                    Rules.Node(true, Enumerable.Range(0, 12).Select(i => ("profile-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "RL")).ToArray()),
                    Sheets.Arriving(("reader", "RL"))));
        }

        private static GrantSheet Sheet(NodeRule rule, AclInheritance above)
        {
            return GrantSheet.Of(rule, above, Names, null);
        }

        /// <summary>
        /// Draws the tab as the group dialog shows it, in the room that dialog gives it, and writes
        /// the texts of every cell, line and tooltip beside the picture.
        /// </summary>
        /// <remarks>
        /// The group dialog's room rather than the settings dialog's, because it is the least this
        /// tab is ever given and the states drawn here are about what that room leaves the table.
        /// </remarks>
        private static void Shoot(string name, GrantSheet sheet, Action<GrantPanel> then = null)
        {
            using (var panel = new GrantPanel(sheet, GrantTab.GroupScope, null, null, null))
            {
                if (then != null)
                {
                    then(panel);
                }

                Gallery.Shot(panel, Gallery.GroupPage, name);
                TestContext.Out.WriteLine(Texts(name, panel, sheet));
            }
        }

        /// <summary>Every text the tab shows or holds in a tooltip, as a reviewer reads it.</summary>
        private static string Texts(string name, GrantPanel panel, GrantSheet sheet)
        {
            var text = new StringBuilder();
            text.AppendLine("## " + name);
            text.AppendLine("line: " + GrantTab.GroupScope);
            if (panel.Warning != null)
            {
                text.AppendLine("over the table: " + panel.Warning);
            }

            var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                {
                    text.AppendLine("empty line: " + GrantTab.AddRowPrompt + " | tooltip of the Permissions cell: " + GrantTab.MaskNeedsAProfile);
                    continue;
                }

                var shown = (GrantRow)row.Tag;
                text.AppendLine("row: " + string.Join(" | ", row.Cells.Cast<DataGridViewCell>()
                    .Select(cell => Convert.ToString(cell.Value, System.Globalization.CultureInfo.InvariantCulture))));
                text.AppendLine("  tooltip Profile: " + shown.Profile);
                Line(text, "  tooltip Permissions: ", GrantTab.MaskTip(shown, sheet.CanEdit));
                Line(text, "  tooltip State: ", GrantTab.StateTip(shown, sheet.CanEdit));
                Line(text, "  accessible text of the row: ", row.AccessibilityObject.Description);
            }

            var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);
            text.AppendLine("box: " + box.Text + " (" + box.CheckState + ", " + sheet.Box + (box.Enabled ? string.Empty : ", disabled") + ")");
            Line(text, "  tooltip: ", box.AccessibleDescription);
            return text.ToString();
        }

        /// <summary>A line of the texts, each line of a tooltip after a slash, where there is any text.</summary>
        private static void Line(StringBuilder text, string label, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                text.AppendLine(label + value.Replace(Environment.NewLine, " / "));
            }
        }
    }
}
