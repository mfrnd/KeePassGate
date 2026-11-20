using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
    /// The grant editor as a control: what it draws, what each press does to the sheet, and that
    /// only the dialog's OK hands the sheet to the writer.
    /// </summary>
    /// <remarks>
    /// Driven through the panel's own methods rather than its controls, because
    /// `Button.PerformClick` does nothing on a form that was never shown, silently, and a test that
    /// pressed a button would assert against a table nobody touched.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GrantPanelTests
    {
        private const int ProfileColumn = GrantTab.ProfileColumn;
        private const int MaskColumn = GrantTab.MaskColumn;
        private const int StateColumn = GrantTab.StateColumn;
        private const int ForgetColumn = GrantTab.ForgetColumn;

        [Test]
        public void A_node_with_nothing_on_it_draws_an_empty_table()
        {
            using (var panel = Panel(NodeRule.None, null))
            {
                Assert.That(panel.Lines, Is.Empty);
                Assert.That(panel.Warning, Is.Null);
            }
        }

        [Test]
        public void The_table_is_named_to_anything_that_cannot_see_it()
        {
            using (var panel = Panel(NodeRule.None, null))
            {
                var grid = Grid(panel);

                Assert.That(grid.AccessibleName, Is.EqualTo(GrantTab.GridLabel));
                Assert.That(grid.AccessibleName, Is.Not.EqualTo(grid.Name),
                    "a table announcing its own lookup name tells a listener nothing");
            }
        }

        [Test]
        public void The_panel_is_named_to_a_screen_reader_by_a_name_of_its_own()
        {
            // With no name of its own, UI automation names the panel after the label before the
            // page on KeePass's dialog, "UUID:".
            using (var panel = Panel(NodeRule.None, null))
            {
                Assert.That(panel.AccessibleName, Is.EqualTo(GrantTab.PanelLabel));
            }
        }

        [Test]
        public void Every_X_is_named_to_a_screen_reader_by_what_pressing_it_does()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "R")), null))
            {
                ButtonColumns.AssertNamedBy(panel, Grid(panel), GrantTab.ForgetColumnName, GrantTab.ForgetColumnHeader);
            }
        }

        [Test]
        public void The_columns_are_the_profile_the_permissions_the_state_and_the_bin_each_with_its_sentence()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "R")), null))
            {
                var columns = Grid(panel).Columns;

                Assert.That(columns.Count, Is.EqualTo(4));
                Assert.That(columns[ProfileColumn].HeaderText, Is.EqualTo(GrantTab.ProfileColumnHeader));
                Assert.That(columns[MaskColumn].HeaderText, Is.EqualTo(GrantTab.MaskColumnHeader));
                Assert.That(columns[StateColumn].HeaderText, Is.EqualTo(GrantTab.StateColumnHeader));
                Assert.That(columns[StateColumn].Name, Is.EqualTo(GrantTab.StateColumnName));
                Assert.That(columns[ProfileColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.ProfileColumnHelp)));
                Assert.That(columns[MaskColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.MaskColumnHelp)));
                Assert.That(columns[StateColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.StateColumnHelp)));
                Assert.That(columns[ForgetColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.ForgetColumnHelp)));
                Assert.That(columns.Cast<DataGridViewColumn>().All(column => column.SortMode == DataGridViewColumnSortMode.NotSortable), Is.True);
            }
        }

        [Test]
        public void Each_row_is_drawn_as_its_profile_its_mask_and_the_word_of_its_first_state()
        {
            var rule = Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("ops", "off:R"), ("deploy", null));

            using (var panel = Panel(rule, Sheets.Arriving(("ops", "RL"), ("zeta", "C"))))
            {
                Assert.That(panel.Lines, Is.EqualTo(new[]
                {
                    "deploy ........ " + GrantTab.LostWord,
                    "ops .R...L.. " + GrantTab.InheritedWord,
                    "ops .R...... " + GrantTab.LimitWord,
                    "reader .R...... " + GrantTab.VersionWord,
                    "reader .R...L.. " + GrantTab.VersionWord,
                    "zeta C....... " + GrantTab.NoProfileWord,
                }));
            }
        }

        [Test]
        public void Every_state_word_has_a_tooltip_telling_what_the_gate_does_and_the_same_accessible_description()
        {
            var rule = Rules.Stored(("reader", "row:R"), ("reader", "off:R"), ("ops", "off:R"), ("deploy", null), ("lost", "row:x"));
            var names = new[] { "reader", "ops", "deploy", "lost", "above" };

            using (var panel = new GrantPanel(
                GrantSheet.Of(rule, Sheets.Arriving(("ops", "RL"), ("above", "R"), ("gone", "R")), names, null),
                GrantTab.GroupScope,
                null, null, null))
            {
                var seen = new HashSet<string>();
                foreach (DataGridViewRow row in Grid(panel).Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }

                    var cell = row.Cells[StateColumn];
                    var word = (string)cell.Value;
                    seen.Add(word);

                    var tip = GrantTab.StateTip((GrantRow)row.Tag, true);
                    Assert.That(cell.ToolTipText, Is.EqualTo(Tooltips.Wrapped(tip)), word);
                    Assert.That(cell.AccessibilityObject.Description, Is.EqualTo(tip), word);

                    // UI automation reads a cell's help, never its description, as its help text.
                    Assert.That(cell.AccessibilityObject.Help, Is.EqualTo(tip), word);
                    Assert.That(tip, Does.Contain("gate grants").Or.Contain("grants nothing"), word);
                }

                Assert.That(seen, Is.EquivalentTo(new[]
                {
                    GrantTab.InheritedWord, GrantTab.LimitWord, GrantTab.VersionWord, GrantTab.TakenOffWord,
                    GrantTab.LostWord, GrantTab.NoProfileWord,
                }));
            }
        }

        [Test]
        public void The_tooltip_of_each_state_is_its_sentence_and_what_a_press_on_the_row_does()
        {
            var sheet = GrantSheet.Of(
                Rules.Stored(
                    ("reader", "row:R"), ("reader", "row:RL"), ("tk", "row:RL"), ("tk", "off:R"), ("ops", "off:R"),
                    ("deploy", null), ("lost", "row:x"), ("deny", "off:deny")),
                Sheets.Arriving(("ops", "RL"), ("above", "CR"), ("deny", "R"), ("gone", "R")),
                new[] { "reader", "tk", "ops", "deploy", "lost", "above", "deny" },
                null);
            var rows = sheet.Rows;

            string Tip(string profile, GrantRowKind kind) =>
                GrantTab.StateTip(rows.First(row => row.Profile == profile && row.Kind == kind), true);

            Assert.That(Tip("above", GrantRowKind.Inherited), Is.EqualTo(
                GrantTab.InheritedTip + " " + GrantTab.InheritedGrants + " " + GrantTab.InheritedChoice));
            Assert.That(Tip("ops", GrantRowKind.Inherited), Is.EqualTo(
                GrantTab.InheritedCutTip + " " + GrantTab.InheritedCutGrants + " " + GrantTab.InheritedChoice));
            Assert.That(Tip("ops", GrantRowKind.Limit), Is.EqualTo(GrantTab.LimitTip + " " + GrantTab.LimitGrants + " " + GrantTab.LimitRemoval));
            Assert.That(Tip("deny", GrantRowKind.Limit), Is.EqualTo(GrantTab.LimitRefusingTip + " " + GrantTab.LimitRemoval));
            Assert.That(Tip("deploy", GrantRowKind.Lost), Is.EqualTo(GrantTab.LostTip + " " + GrantTab.LostChoice));
            Assert.That(Tip("lost", GrantRowKind.Lost), Is.EqualTo(GrantTab.UnreadableTip + " " + GrantTab.UnreadableChoice));
            Assert.That(Tip("reader", GrantRowKind.Version), Is.EqualTo(
                "This rule stands in 2 versions, made in copies of this database before the copies met. Until a single version is "
                + "left, the gate grants .R...... at this node. " + GrantTab.VersionRemoval));
            Assert.That(GrantTab.StateTip(rows.First(row => row.States.Contains(GrantState.TakenOff)), true), Is.EqualTo(
                "In this version the rule was removed, so the version lets a grant from above through only as far as the "
                + "permissions shown. Until a single version is left, the gate grants ........ at this node. "
                + GrantTab.VersionRemoval));
            Assert.That(Tip("gone", GrantRowKind.Inherited), Is.EqualTo(
                GrantTab.NoProfileTip + Environment.NewLine + GrantTab.InheritedTip + " " + GrantTab.InheritedChoice));
        }

        [Test]
        public void No_tooltip_tells_a_grant_for_a_profile_the_database_no_longer_names()
        {
            // A rule for a name the list no longer holds grants nothing, whatever the row shows: no
            // state of such a row tells of a grant, read only or not.
            var sheet = GrantSheet.Of(
                Rules.Stored(("gone", "row:R"), ("cut", "off:R"), ("group", "row:R"), ("group", "row:RL"), ("taken", "row:RL"), ("taken", "off:R")),
                Sheets.Arriving(("above", "RL"), ("cut", "RL"), ("taken", "RUL")),
                new[] { "ops" },
                null);
            var grants = new Regex(@"gate grants (?!nothing)");

            var rows = sheet.Rows.Where(row => row.States.Contains(GrantState.NoProfile)).ToList();
            Assert.That(rows.Select(row => row.Kind).Distinct(), Is.SupersetOf(new[]
            {
                GrantRowKind.Own, GrantRowKind.Inherited, GrantRowKind.Limit, GrantRowKind.Version,
            }));
            Assert.That(rows.SelectMany(row => row.States).Distinct(), Does.Contain(GrantState.TakenOff));
            foreach (var row in rows)
            {
                foreach (var tip in new[] { true, false }.Select(editable => GrantTab.StateTip(row, editable)))
                {
                    Assert.That(grants.IsMatch(tip), Is.False, row + ": " + tip);
                }
            }
        }

        [Test]
        public void The_row_an_X_leaves_where_nothing_arrives_tells_that_nothing_arrives()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "R")), null);
            Assert.That(sheet.Remove("reader"), Is.True);

            Assert.That(GrantTab.StateTip(Sheets.Single(sheet, "reader"), true), Is.EqualTo(
                GrantTab.NothingArrives + " " + GrantTab.NothingArrivesChoice + Environment.NewLine + GrantTab.ChangedHere));
        }

        [Test]
        public void A_lost_version_in_a_group_is_removed_by_its_X_and_takes_no_rule()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", null)), null);

            Assert.That(GrantTab.StateTip(sheet.Rows.Single(row => row.Kind == GrantRowKind.Lost), true),
                Is.EqualTo(GrantTab.LostTip + " " + GrantTab.VersionRemoval));
        }

        [Test]
        public void A_state_tooltip_tells_of_no_press_where_the_tab_takes_none()
        {
            var sheet = GrantSheet.Of(Rules.Stored(("ops", "off:R")), Sheets.Arriving(("ops", "RL")), Sheets.Names, "read only");

            Assert.That(sheet.Rows.Select(row => GrantTab.StateTip(row, false)), Is.EqualTo(new[]
            {
                GrantTab.InheritedCutTip + " " + GrantTab.InheritedCutGrants,
                GrantTab.LimitTip + " " + GrantTab.LimitGrants,
            }));
        }

        [Test]
        public void A_rule_refused_here_tells_what_the_box_does_and_a_name_gone_beside_it_tells_both()
        {
            var sheet = GrantSheet.Of(Rules.Node(true), Sheets.Arriving(("reader", "R"), ("gone", "R")), new[] { "reader" }, null);

            Assert.That(GrantTab.StateTip(Sheets.Single(sheet, "reader"), true),
                Is.EqualTo(GrantTab.RefusedHereTip + " " + GrantTab.RefusedHereChoice));
            Assert.That(GrantTab.StateTip(Sheets.Single(sheet, "gone"), true), Is.EqualTo(GrantTab.NoProfileRefusedTip));
        }

        [Test]
        public void A_row_changed_on_the_tab_tells_so_in_its_tooltips_and_its_accessible_text()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "R"), ("ops", "R")), Sheets.Arriving(("reader", "RL"))))
            {
                Assert.That(panel.SetGrant("reader", "CR...L.."), Is.Null);

                var grid = Grid(panel);
                var changed = Row(grid, "reader");
                var untouched = Row(grid, "ops");

                Assert.That(((GrantRow)changed.Tag).Changed, Is.True);
                Assert.That(changed.AccessibilityObject.Description, Is.EqualTo(GrantTab.ChangedHere));
                Assert.That(changed.AccessibilityObject.Help, Is.EqualTo(GrantTab.ChangedHere), "the help text UI automation reads");
                Assert.That(changed.Cells[StateColumn].AccessibilityObject.Description, Is.EqualTo(GrantTab.ChangedHere));
                Assert.That(changed.Cells[StateColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.ChangedHere)));
                Assert.That(changed.Cells[MaskColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(
                    "Widens .R...L.." + GrantTab.InheritedFrom + Environment.NewLine + GrantTab.ChangedHere)));

                Assert.That(untouched.AccessibilityObject.Description ?? string.Empty, Does.Not.Contain(GrantTab.ChangedHere));
                Assert.That(untouched.Cells[StateColumn].ToolTipText, Is.Empty.Or.Null);
            }
        }

        [Test]
        public void No_tooltip_but_the_profile_cell_holds_a_profile_s_name()
        {
            var rule = Rules.Stored(("zz-reader", "row:R"), ("zz-reader", "off:R"), ("zz-ops", "off:R"), ("zz-lost", null), ("zz-own", "row:CR"));
            var names = new[] { "zz-reader", "zz-ops", "zz-lost", "zz-own", "zz-above" };

            using (var panel = new GrantPanel(
                GrantSheet.Of(rule, Sheets.Arriving(("zz-ops", "RL"), ("zz-above", "R"), ("zz-gone", "R"), ("zz-own", "C")), names, null),
                GrantTab.GroupScope,
                null, null, null))
            {
                panel.SetGrant("zz-above", "CR......");
                panel.PressBox();

                foreach (DataGridViewRow row in Grid(panel).Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }

                    Assert.That(row.Cells[ProfileColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(((GrantRow)row.Tag).Profile)),
                        "the whole name, and nothing else");
                    foreach (var column in new[] { MaskColumn, StateColumn, ForgetColumn })
                    {
                        Assert.That(row.Cells[column].ToolTipText ?? string.Empty, Does.Not.Contain("zz-"));
                        Assert.That(row.Cells[column].AccessibilityObject.Description ?? string.Empty, Does.Not.Contain("zz-"));
                    }

                    Assert.That(row.AccessibilityObject.Description ?? string.Empty, Does.Not.Contain("zz-"));
                }

                var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);
                Assert.That(box.AccessibleDescription, Does.Not.Contain("zz-"));
                Assert.That(panel.Warning ?? string.Empty, Does.Not.Contain("zz-"));
            }
        }

        [Test]
        public void A_press_changes_the_sheet_and_the_rules_given_are_as_they_were()
        {
            var rule = Rules.Node(false, ("reader", "R"));

            using (var panel = Panel(rule, Sheets.Arriving(("ops", "RL"))))
            {
                Assert.That(panel.SetGrant("ops", "CR......"), Is.Null);
                panel.Press(Row(Grid(panel), "reader").Index, ForgetColumn);
                panel.PressBox();

                Assert.That(panel.Lines, Is.EqualTo(new[]
                {
                    "ops CR......",
                    "reader ........ " + GrantTab.RefusedHereWord,
                }));
                Assert.That(rule.Row("reader").Versions.Single().Kind, Is.EqualTo(RowVersionKind.Grants));
                Assert.That(rule.Switch.Versions, Is.Empty);
            }
        }

        [Test]
        public void The_X_on_a_version_removes_that_version_and_the_X_on_a_limit_settles()
        {
            var rule = Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("ops", "off:R"));

            using (var panel = Panel(rule, Sheets.Arriving(("ops", "RL"))))
            {
                var grid = Grid(panel);
                Assert.That(grid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow)
                    .All(row => (string)row.Cells[ForgetColumn].Value == "X"), Is.False, "the inherited row has no X");

                panel.Press(RowIndex(grid, "reader", ".R...L.."), ForgetColumn);
                Assert.That(panel.Lines.Where(line => line.StartsWith("reader", StringComparison.Ordinal)),
                    Is.EqualTo(new[] { "reader .R......" }));

                panel.Press(RowIndex(grid, "ops", ".R......"), ForgetColumn);
                Assert.That(panel.Lines.Where(line => line.StartsWith("ops", StringComparison.Ordinal)),
                    Is.EqualTo(new[] { "ops .R...L.. " + GrantTab.InheritedWord }));
            }
        }

        [Test]
        public void The_X_takes_a_rule_off_and_the_inherited_rule_shows_in_its_place_until_OK()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "R")), Sheets.Arriving(("reader", "RL"))))
            {
                var grid = Grid(panel);
                panel.Press(Row(grid, "reader").Index, ForgetColumn);

                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.. " + GrantTab.InheritedWord }));
                Assert.That(((GrantRow)Row(grid, "reader").Tag).Changed, Is.True);
                Assert.That(Row(grid, "reader").Cells[ForgetColumn].Value, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void The_box_shows_ticked_cleared_and_the_mixed_mark_and_a_press_only_ticks_or_clears()
        {
            using (var panel = Panel(Rules.Stored((null, "sw:deny"), (null, "sw:inherit")), null))
            {
                var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);

                Assert.That(box.Text, Is.EqualTo(GrantTab.SealCheckBoxText));
                Assert.That(box.CheckState, Is.EqualTo(CheckState.Indeterminate));
                Assert.That(box.AccessibleDescription, Is.EqualTo(GrantTab.SealCheckBoxMixed));

                panel.PressBox();
                Assert.That(box.CheckState, Is.EqualTo(CheckState.Checked));
                Assert.That(box.AccessibleDescription, Is.EqualTo(GrantTab.SealCheckBoxHelp + Environment.NewLine + GrantTab.ChangedHere));

                panel.PressBox();
                Assert.That(box.CheckState, Is.EqualTo(CheckState.Unchecked));
                Assert.That(box.Text, Is.EqualTo(GrantTab.SealCheckBoxText), "the caption in every state");
            }
        }

        [Test]
        public void A_lost_box_shows_ticked_with_its_own_tooltip()
        {
            using (var panel = Panel(Rules.Stored((null, null)), null))
            {
                var box = ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName);

                Assert.That(box.CheckState, Is.EqualTo(CheckState.Checked));
                Assert.That(box.AccessibleDescription, Is.EqualTo(GrantTab.SealCheckBoxLost));
            }
        }

        [Test]
        public void A_tab_that_takes_no_press_shows_every_row_with_no_X_no_list_and_the_box_disabled()
        {
            var rule = Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("ops", "off:R"), (null, "sw:inherit ops=R"));

            using (var panel = new GrantPanel(
                GrantSheet.Of(rule, Sheets.Arriving(("ops", "RL")), Sheets.Names, GrantTab.EarlierVersion),
                GrantTab.EntryScope,
                null, null, null))
            {
                var grid = Grid(panel);
                var before = panel.Lines;

                Assert.That(before.Count, Is.EqualTo(4));
                Assert.That(panel.Warning, Is.EqualTo(GrantTab.EarlierVersion));
                Assert.That(ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName).Visible, Is.False,
                    "the line telling that a rule can be overwritten stands over no table that takes no change");
                Assert.That(grid.AllowUserToAddRows, Is.False);
                Assert.That(grid.Rows.Cast<DataGridViewRow>().All(row => (string)row.Cells[ForgetColumn].Value == string.Empty), Is.True);
                Assert.That(ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName).Enabled, Is.False);
                Assert.That(grid.Columns[MaskColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(GrantTab.MaskColumnReadOnlyHelp)));

                for (var index = 0; index < grid.Rows.Count; index++)
                {
                    panel.Press(index, ForgetColumn);
                }

                panel.PressBox();
                Assert.That(panel.SetGrant("deploy", "CR......"), Is.EqualTo(GrantTab.NothingToChange));
                Assert.That(panel.Lines, Is.EqualTo(before));
            }
        }

        [Test]
        public void An_inherited_rule_is_gray_and_a_rule_the_box_refuses_is_struck_through()
        {
            using (var panel = Panel(Rules.Node(true, ("ops", "RL")), Sheets.Arriving(("reader", "RL"))))
            {
                var grid = Grid(panel);
                var refused = Row(grid, "reader");

                Assert.That(refused.DefaultCellStyle.ForeColor, Is.EqualTo(SystemColors.GrayText));
                Assert.That(refused.Cells[MaskColumn].Style.Font.Strikeout, Is.True);
                Assert.That(refused.Cells[StateColumn].Value, Is.EqualTo(GrantTab.RefusedHereWord), "told by the word too");
                Assert.That(Row(grid, "ops").Cells[MaskColumn].Style.Font, Is.Null, "a rule of the node's own is drawn plainly");
            }
        }

        [Test]
        public void The_row_an_X_leaves_where_nothing_arrives_under_a_ticked_box_is_refused_here_and_struck_through()
        {
            using (var panel = Panel(Rules.Node(true, ("ops", "RL")), Sheets.Arriving(("reader", "RL"))))
            {
                Assert.That(ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName).Visible, Is.True, "the line at the top stands on a tab that takes a press");

                var grid = Grid(panel);
                panel.Press(Row(grid, "ops").Index, ForgetColumn);

                var left = Row(grid, "ops");
                Assert.That(left.Cells[StateColumn].Value, Is.EqualTo(GrantTab.RefusedHereWord));
                Assert.That(left.Cells[MaskColumn].Value, Is.EqualTo("........"));
                Assert.That(left.Cells[MaskColumn].Style.Font.Strikeout, Is.True);
                Assert.That(left.Cells[StateColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(
                    GrantTab.RefusedHereTip + " " + GrantTab.RefusedHereChoice + Environment.NewLine + GrantTab.ChangedHere)));
            }
        }

        [Test]
        public void A_rule_for_a_profile_the_database_no_longer_names_is_struck_through_and_told_by_its_word()
        {
            using (var panel = new GrantPanel(
                GrantSheet.Of(Rules.Node(false, ("ops", "RL")), Sheets.Arriving(("retired", "R")), new[] { "reader" }, null),
                GrantTab.GroupScope,
                null, null, null))
            {
                var grid = Grid(panel);
                foreach (var profile in new[] { "ops", "retired" })
                {
                    var row = Row(grid, profile);
                    Assert.That(row.Cells[MaskColumn].Style.Font.Strikeout, Is.True, profile);
                    Assert.That(row.Cells[StateColumn].Value, Is.EqualTo(GrantTab.NoProfileWord), profile);
                }

                Assert.That(panel.ProfileChoices, Is.EqualTo(new[] { "reader" }));
            }
        }

        [Test]
        public void The_masks_follow_the_panel_to_a_larger_font()
        {
            using (var panel = Panel(Rules.Node(true), Sheets.Arriving(("reader", "RL"))))
            using (var larger = new Font(panel.Font.FontFamily, 12f))
            {
                panel.Font = larger;

                var grid = Grid(panel);
                var plain = grid.Columns[MaskColumn].DefaultCellStyle.Font;
                var refused = Row(grid, "reader").Cells[MaskColumn].Style.Font;

                Assert.That(plain.SizeInPoints, Is.EqualTo(larger.SizeInPoints + 1f).Within(0.01f),
                    "the masks kept the size the panel was built at");
                Assert.That(refused.SizeInPoints, Is.EqualTo(plain.SizeInPoints).Within(0.01f),
                    "a mask the box refuses kept the size the panel was built at");
                Assert.That(refused.Strikeout, Is.True, "and is still struck through");
            }
        }

        [Test]
        [TestCase("R", "RL", "Narrows")]
        [TestCase("CRP", "R", "Widens")]
        [TestCase("RU", "RL", "Replaces")]
        [TestCase("R", "R", "The same as")]
        [TestCase("", "A", "The same as")]
        [TestCase("D", "DA", "The same as")]
        public void A_rule_here_tells_on_its_mask_which_way_it_replaces_the_rule_above(string here, string above, string word)
        {
            using (var panel = Panel(Rules.Node(false, ("reader", here)), Sheets.Arriving(("reader", above))))
            {
                Assert.That(Row(Grid(panel), "reader").Cells[MaskColumn].ToolTipText, Is.EqualTo(Tooltips.Wrapped(
                    word + " " + VerbMask.Of(Rules.Letters(above)) + GrantTab.InheritedFrom)));
            }
        }

        [Test]
        public void A_rule_here_over_nothing_inherited_tells_nothing_on_its_mask()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "R")), null))
            {
                Assert.That(Row(Grid(panel), "reader").Cells[MaskColumn].ToolTipText, Is.Empty.Or.Null);
            }
        }

        [Test]
        public void The_pick_list_holds_the_profiles_the_table_does_not_show()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "R")), Sheets.Arriving(("ops", "R"))))
            {
                Assert.That(panel.ProfileChoices, Is.EqualTo(new[] { "deploy" }));
            }
        }

        [Test]
        public void A_profile_chosen_from_the_list_arrives_granting_nothing_and_changed()
        {
            using (var panel = Panel(NodeRule.None, null))
            {
                Assert.That(panel.AddGrant("ops"), Is.Null);

                Assert.That(panel.Lines, Is.EqualTo(new[] { "ops ........" }));
                Assert.That(((GrantRow)Row(Grid(panel), "ops").Tag).Changed, Is.True);
            }
        }

        [Test]
        public void A_profile_the_table_shows_is_refused_rather_than_started_again()
        {
            using (var panel = Panel(Rules.Node(false, ("reader", "RL")), Sheets.Arriving(("ops", "R"))))
            {
                Assert.That(panel.AddGrant("reader"), Is.EqualTo(GrantTab.AlreadyShown));
                Assert.That(panel.AddGrant("ops"), Is.EqualTo(GrantTab.AlreadyShown));
                Assert.That(panel.Lines, Is.EqualTo(new[] { "ops .R...... " + GrantTab.InheritedWord, "reader .R...L.." }));
            }
        }

        [Test]
        public void A_database_that_names_no_profile_tells_so_over_the_table_before_any_press_and_offers_no_empty_line()
        {
            // The table holds a rule of a profile the database no longer names, which the box and
            // the X still take a press on: the tab is not read only, so the line at the top stands.
            using (var panel = new GrantPanel(
                GrantSheet.Of(Rules.Node(false, ("retired", "R")), null, Array.Empty<string>(), null), GrantTab.GroupScope, null, null, null))
            {
                var grid = Grid(panel);

                Assert.That(panel.Warning, Is.EqualTo(GrantTab.NoProfilesYet), "told before any press");
                Assert.That(GrantTab.NoProfilesYet, Does.Contain(GateName.OnATab).And.Contain("Profiles page"));
                Assert.That(grid.AllowUserToAddRows, Is.False, "no empty line offers a profile where none is named");
                Assert.That(ControlTree.FindByName<CheckBox>(panel, GrantTab.SealCheckBoxName).Enabled, Is.True);
                Assert.That(ControlTree.FindByName<Label>(panel, GrantTab.ScopeLabelName).Visible, Is.True);

                panel.Press(0, ForgetColumn);
                Assert.That(panel.Lines, Is.EqualTo(new[] { "retired ........ " + GrantTab.NoProfileWord }), "the X takes a press");
                Assert.That(panel.Warning, Is.EqualTo(GrantTab.NoProfilesYet), "and the notice stands");
            }
        }

        [Test]
        public void A_record_landing_reads_the_rules_and_the_names_again_and_keeps_every_press()
        {
            var notice = new AdmissionNotice();
            var reading = new GrantReading(Rules.Node(false, ("reader", "R"), ("ops", "R")), null, new[] { "reader", "ops" }, null);

            using (var panel = new GrantPanel(
                GrantSheet.Of(reading.Rule, reading.Above, reading.Names, null), GrantTab.GroupScope, null, () => reading, notice))
            {
                panel.SetGrant("ops", "CR......");

                reading = new GrantReading(
                    Rules.Node(false, ("reader", "RL"), ("ops", "RL")), Sheets.Arriving(("deploy", "R")), new[] { "reader", "ops", "deploy", "new" }, null);
                notice.Raise();

                Assert.That(panel.Lines, Is.EqualTo(new[]
                {
                    "deploy .R...... " + GrantTab.InheritedWord,
                    "ops CR......",
                    "reader .R...L..",
                }));
                Assert.That(panel.ProfileChoices, Is.EqualTo(new[] { "new" }));
            }
        }

        [Test]
        public void A_tab_that_has_been_disposed_is_not_told_about_the_next_record()
        {
            var read = 0;
            var notice = new AdmissionNotice();

            using (new GrantPanel(
                GrantSheet.Of(NodeRule.None, null, Sheets.Names, null),
                GrantTab.GroupScope,
                null,
                () =>
                {
                    read++;
                    return new GrantReading(NodeRule.None, null, Sheets.Names, null);
                },
                notice))
            {
            }

            notice.Raise();
            Assert.That(read, Is.Zero, "a disposed tab read the database again");
        }

        [Test]
        public void Nothing_is_written_at_a_press_and_OK_writes_once_as_the_dialog_closes()
        {
            var rules = new FakeNodeRules();
            var prompt = new FakePrompt(true);

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                window.Show();
                var sheet = Sheets.Of(Rules.Node(false, ("reader", "R")), Sheets.Arriving(("ops", "RL")));

                using (var panel = (GrantPanel)Factory(sheet, rules, prompt, window).CreatePanel(dialog))
                {
                    dialog.Show();
                    panel.SetGrant("ops", "CR......");
                    panel.Press(Row(Grid(panel), "reader").Index, ForgetColumn);
                    panel.PressBox();
                    OffScreenForm.Pump();

                    Assert.That(rules.Written, Is.Empty, "a press changes the sheet and nothing else");
                }

                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                OffScreenForm.Pump();

                var written = rules.Written.Single();
                Assert.That(written.Rows.Select(change => change.Profile), Is.EqualTo(new[] { "ops", "reader" }));
                Assert.That(written.Switch, Is.Not.Null);
                Assert.That(prompt.Told, Is.Zero, "nothing to tell where every change was kept");
            }
        }

        [Test]
        [TestCase(DialogResult.Cancel)]
        [TestCase(DialogResult.None)]
        public void Any_close_but_OK_writes_nothing(DialogResult result)
        {
            var rules = new FakeNodeRules();

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("ops", "RL")));
                using (var panel = (GrantPanel)Factory(sheet, rules, new FakePrompt(true), window).CreatePanel(dialog))
                {
                    dialog.Show();
                    panel.SetGrant("ops", "CR......");
                }

                dialog.DialogResult = result;
                dialog.Close();
                OffScreenForm.Pump();

                Assert.That(rules.Written, Is.Empty);
            }
        }

        [Test]
        public void An_OK_with_nothing_changed_writes_nothing()
        {
            var rules = new FakeNodeRules();

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                var sheet = Sheets.Of(Rules.Node(false, ("reader", "R")), null);
                var prompt = new FakePrompt(true);
                using (var panel = (GrantPanel)Factory(sheet, rules, prompt, window).CreatePanel(dialog))
                {
                    dialog.Show();
                    panel.SetGrant("reader", "CR......");
                    panel.SetGrant("reader", ".R......");
                }

                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                OffScreenForm.Pump();

                Assert.That(rules.Written, Is.Empty);
                Assert.That(prompt.Told, Is.Zero, "nothing is told where nothing is pressed");
            }
        }

        [Test]
        public void An_OK_once_the_tab_stopped_taking_presses_writes_nothing_and_tells_each_press_not_kept()
        {
            // A record landed while the dialog stood, and the reading made again gives a reason no
            // rule can be written: the tab takes no press from there on, keeps the presses made on
            // the table as changes that are not written, and its OK tells so.
            var rules = new FakeNodeRules();
            var prompt = new FakePrompt(true);
            var notice = new AdmissionNotice();
            var reading = new GrantReading(NodeRule.None, Sheets.Arriving(("ops", "RL")), Sheets.Names, null);

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                window.Show();
                var sheet = GrantSheet.Of(reading.Rule, reading.Above, reading.Names, null);
                var factory = new GrantPanelFactory(
                    new FakeNodeDialogs(new NodeTab(sheet, GrantTab.GroupScope, () => reading, rules)), null, notice, prompt, window, null);
                using (var panel = (GrantPanel)factory.CreatePanel(dialog))
                {
                    dialog.Show();
                    Assert.That(panel.SetGrant("ops", "CR......"), Is.Null);

                    reading = new GrantReading(NodeRule.None, Sheets.Arriving(("ops", "RL")), Sheets.Names, GrantTab.StoreUnusable);
                    notice.Raise();

                    Assert.That(panel.Warning, Is.EqualTo(GrantTab.StoreUnusable));
                    Assert.That(panel.Lines, Is.EqualTo(new[] { "ops CR......" }), "the press stays on the table");
                    var row = Row(Grid(panel), "ops");
                    Assert.That(row.AccessibilityObject.Description, Is.EqualTo(GrantTab.ChangedNotWritten));
                    Assert.That(row.Cells[ForgetColumn].Value, Is.EqualTo(string.Empty), "and the tab takes no press");
                    Assert.That(panel.SetGrant("ops", ".R......"), Is.EqualTo(GrantTab.NothingToChange));
                }

                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                OffScreenForm.Pump();

                Assert.That(rules.Written, Is.Empty);
                Assert.That(prompt.Told, Is.EqualTo(1));
                Assert.That(prompt.LastNews, Is.EqualTo(GrantTab.NotKept + GrantTab.NoLongerWritable));
            }
        }

        [Test]
        public void A_refused_OK_is_told_once_the_dialog_has_closed_and_never_at_a_press()
        {
            var rules = new FakeNodeRules { Refusal = GrantTab.NotKept + "the database maintenance dialog is open on this database" };
            var prompt = new FakePrompt(true);

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                window.Show();
                var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("ops", "RL")));
                using (var panel = (GrantPanel)Factory(sheet, rules, prompt, window).CreatePanel(dialog))
                {
                    dialog.Show();
                    panel.SetGrant("ops", "CR......");
                    OffScreenForm.Pump();
                    Assert.That(prompt.Told, Is.Zero);
                }

                var toldAtTheClose = -1;
                dialog.FormClosed += (sender, e) => toldAtTheClose = prompt.Told;
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                OffScreenForm.Pump();

                Assert.That(toldAtTheClose, Is.Zero, "posted, never raised inside the close");
                Assert.That(prompt.Told, Is.EqualTo(1));
                Assert.That(prompt.LastNews, Is.EqualTo(rules.Refusal));
            }
        }

        [Test]
        public void A_tab_that_takes_no_press_watches_no_close()
        {
            var rules = new FakeNodeRules();

            using (var window = OffScreenForm.New())
            using (var dialog = OffScreenForm.New())
            {
                var sheet = GrantSheet.Of(NodeRule.None, Sheets.Arriving(("ops", "RL")), Sheets.Names, GrantTab.NotWatched);
                using (Factory(sheet, rules, new FakePrompt(true), window).CreatePanel(dialog))
                {
                    dialog.Show();
                }

                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                OffScreenForm.Pump();

                Assert.That(rules.Written, Is.Empty);
            }
        }

        [Test]
        public void Every_sentence_of_the_tab_names_its_noun_and_none_of_the_words_ruled_out()
        {
            // A sentence names no profile and no client, and no pronoun stands for a subject or an
            // object: the words a pronoun is spelled with are looked for, and so are the words the
            // conventions rule out.
            var dashes = new string(new[] { (char)0x2013, (char)0x2014 });
            var ruledOut = new Regex(@"\b(it|its|they|them|their|one|ones|sa(?:ys?|id|y(?:ing)))\b|\s-\s|[" + dashes + "]", RegexOptions.IgnoreCase);
            var sentences = typeof(GrantTab)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(string) && !field.Name.EndsWith("Name", StringComparison.Ordinal))
                .Select(field => (string)field.GetValue(null))
                .ToList();

            Assert.That(sentences.Count, Is.GreaterThan(30), "the sentences were found");
            foreach (var sentence in sentences)
            {
                Assert.That(ruledOut.IsMatch(sentence), Is.False, sentence);
            }
        }

        private static GrantPanelFactory Factory(GrantSheet sheet, FakeNodeRules rules, FakePrompt prompt, Control window)
        {
            return new GrantPanelFactory(
                new FakeNodeDialogs(new NodeTab(sheet, GrantTab.GroupScope, null, rules)),
                null,
                null,
                prompt,
                window,
                null);
        }

        private static GrantPanel Panel(NodeRule rule, AclInheritance above)
        {
            return new GrantPanel(Sheets.Of(rule, above), GrantTab.GroupScope, null, null, null);
        }

        private static DataGridView Grid(Control panel)
        {
            var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
            Assert.That(grid, Is.Not.Null);
            return grid;
        }

        private static DataGridViewRow Row(DataGridView grid, string profile)
        {
            var rows = grid.Rows.Cast<DataGridViewRow>()
                .Where(row => !row.IsNewRow && ((GrantRow)row.Tag).Profile == profile)
                .ToList();
            Assert.That(rows.Count, Is.EqualTo(1), profile + " shows on a single row");
            return rows[0];
        }

        private static int RowIndex(DataGridView grid, string profile, string mask)
        {
            return grid.Rows.Cast<DataGridViewRow>()
                .Single(row => !row.IsNewRow && ((GrantRow)row.Tag).Profile == profile && ((GrantRow)row.Tag).Mask == mask)
                .Index;
        }
    }
}
