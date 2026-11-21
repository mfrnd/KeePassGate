using System;
using System.Collections.Generic;
using System.Drawing;
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

            // A profile the node cuts: the inherited row, and bound under it the limit, At most.
            var cut = Sheet(Rules.Stored(("reader", "off:R"), ("ops", "row:RL")), Sheets.Arriving(("reader", "CRUL"), ("ops", "R")));
            Shoot("grant-03-limit", cut);

            // The same after the X on the At most row: the inherited row stands alone until OK.
            cut.Settle("reader");
            Shoot("grant-04-limit-settled", cut);

            // A rule in several versions made in copies before the copies met, Unmerged, a version
            // that took the rule off among them, Removed: each version a row of its own, bound, with
            // an X and no list.
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

            // A rule whose record is missing, a rule that cannot be read, and a name the database
            // no longer names.
            Shoot(
                "grant-10-missing-unreadable-and-no-profile",
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

            // The longest texts at once: the longest reason, the widest masks, the longest words of
            // the State column, a long name, bound with every letter apart.
            Shoot(
                "grant-13-longest-texts",
                GrantSheet.Of(
                    Rules.Stored(
                        ("a-profile-with-a-name-somebody-really-did-type", "row:CRUSDLAP"),
                        ("a-profile-with-a-name-somebody-really-did-type", "row:"),
                        ("deploy", "row:CRUSDLAP"), ("deploy", "off:CRUSDLAP"), ("ops", "off:R"), ("reader", null), ("unread", "row:x")),
                    Sheets.Arriving(("ops", "CRUSDLAP"), ("retired", "CRUSDLAP")),
                    new[] { "a-profile-with-a-name-somebody-really-did-type", "deploy", "ops", "reader", "unread" },
                    GrantTab.EarlierVersion));

            // More rows than the table shows at once, with the box ticked over all of them.
            Shoot(
                "grant-14-scrolling",
                Sheet(
                    Rules.Node(true, Enumerable.Range(0, 12).Select(i => ("profile-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "RL")).ToArray()),
                    Sheets.Arriving(("reader", "RL"))));

            // A rule in two versions: bound, with the letter the versions do not agree on in bold.
            Shoot("grant-15-two-versions", Sheet(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("ops", "row:R")), null));

            // A version removed from three: the two left bound and changed at once.
            var removed = Sheet(Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "off:R"), ("ops", "row:R")), Sheets.Arriving(("reader", "CRUL")));
            removed.RemoveVersion("reader", removed.Rows.First(row => row.Profile == "reader").Version);
            Shoot("grant-16-version-removed", removed);

            // A rule set on the tab, changed until OK.
            var set = Sheet(Rules.Node(false, ("reader", "R"), ("ops", "RL")), Sheets.Arriving(("deploy", "RL")));
            set.Set("reader", "CR...L..");
            Shoot("grant-17-rule-set", set);

            // Every mark at once: a changed row, two versions bound, a rule Refused, the box
            // in the mixed mark. Under a high contrast setting the colors go and the rest stays.
            var every = Sheet(
                Rules.Stored((null, "sw:deny"), (null, "sw:inherit"), ("reader", "row:RL"), ("reader", "row:CRL"), ("ops", "off:R"), ("deploy", "row:R")),
                Sheets.Arriving(("ops", "CRUL")));
            every.Set("deploy", "CR......");
            Shoot("grant-18-every-mark", every);

            // A rule in three versions among more rows than the table shows, scrolled so that the
            // table's edge cuts the group: the bracket stays open there.
            var versions = new List<(string Profile, string Text)> { ("a-reader", "row:R"), ("a-reader", "row:RL"), ("a-reader", "row:CRL") };
            versions.AddRange(Enumerable.Range(0, 6).Select(i => ("profile-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "row:RL")));
            Shoot(
                "grant-19-group-cut-by-scrolling",
                GrantSheet.Of(Rules.Stored(versions.ToArray()), null, versions.Select(version => version.Profile).Distinct(), null),
                null,
                panel => ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName).FirstDisplayedScrollingRowIndex = 1);

            // The box cleared over a version that takes the rule off: each version with the letters
            // OK writes it with, changed, and still bound.
            var cleared2 = Sheet(Rules.Stored(("reader", "row:R"), ("reader", "off:deny"), (null, "sw:deny"), ("ops", "row:RL")), Sheets.Arriving(("reader", "RL")));
            cleared2.PressBox();
            Shoot("grant-20-box-cleared-over-versions", cleared2);

            // Versions removed down to a rule taken off with nothing arriving: a row with no letter
            // that holds the change until OK.
            var nothing = Sheet(Rules.Stored(("reader", "row:RL"), ("reader", "off:R"), ("ops", "row:RL")), null);
            nothing.RemoveVersion("reader", nothing.Rows.First(row => row.Profile == "reader" && row.Mask == ".R...L..").Version);
            Shoot("grant-21-pending-with-no-letter", nothing);

            // A group in which no version reads: every row takes a rule, still bound.
            Shoot("grant-22-no-version-reads", Sheet(Rules.Stored(("reader", null), ("reader", "row:x"), ("ops", "row:RL")), Sheets.Arriving(("reader", "RL"))));

            // Presses on the table, then a reading made again that finds the database no longer
            // taking rules: the changes stay on the table, and nothing is marked as OK writing them.
            var stopped = Rules.Stored(("reader", "row:RL"), ("reader", "row:CRL"), ("reader", "row:R"), ("ops", "row:R"));
            var notWritten = Sheet(stopped, null);
            notWritten.RemoveVersion("reader", notWritten.Rows.First(row => row.Profile == "reader").Version);
            notWritten.Set("ops", "CR......");
            notWritten.Rebase(stopped, null, Names, GrantTab.StoreUnusable);
            Shoot("grant-23-changes-not-written", notWritten);
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
        private static void Shoot(string name, GrantSheet sheet, Action<GrantPanel> then = null, Action<GrantPanel> shown = null)
        {
            using (var panel = new GrantPanel(sheet, GrantTab.GroupScope, null, null, null))
            {
                if (then != null)
                {
                    then(panel);
                }

                Gallery.Shot(panel, Gallery.GroupPage, name, shown == null ? (Action)null : () => shown(panel));
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
            var marks = GrantMarks.Of(sheet.Rows, sheet.CanEdit);
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
                Line(text, "  marks GrantMarks.Of answers, and the color set behind the row: ", Drawn(marks[row.Index], row.DefaultCellStyle.BackColor));
            }

            var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);
            text.AppendLine("box: " + box.Text + " (" + box.CheckState + ", " + sheet.Box + (box.Enabled ? string.Empty : ", disabled") + ")");
            Line(text, "  the color set behind the box: ", Drawn(null, box.BackColor == panel.BackColor ? Color.Empty : box.BackColor));
            Line(text, "  tooltip: ", box.AccessibleDescription);
            return text.ToString();
        }

        /// <summary>
        /// What a row or the box is given to draw beyond its texts: the bracket and the places in bold
        /// as `GrantMarks.Of` answers them, and the color set behind the row or the box.
        /// </summary>
        private static string Drawn(GrantMarks marks, Color behind)
        {
            var drawn = new List<string>();
            if (marks != null && marks.Bound)
            {
                drawn.Add("bracket" + (marks.First ? ", closed over" : string.Empty) + (marks.Last ? ", closed under" : string.Empty));
            }

            if (marks != null && marks.Bold.Count > 0)
            {
                drawn.Add("bold at " + string.Join(",", marks.Bold));
            }

            if (!behind.IsEmpty)
            {
                drawn.Add(behind == RowTints.ChangedColor ? "light yellow" : behind == RowTints.BoundColor ? "light red" : behind.Name);
            }

            return string.Join("; ", drawn);
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
