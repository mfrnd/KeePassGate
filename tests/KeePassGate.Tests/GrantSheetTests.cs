using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the grant editor decides, with no window anywhere near it: what each row of a node's
    /// rules stands for, which press each row takes, and what OK is handed.
    /// </summary>
    /// <remarks>
    /// The table is the easy half. What is worth testing is what a row shows for each way the
    /// store can hold a rule, and that a press changes the sheet and nothing else, because the
    /// sheet is what the dialog's OK writes and Cancel throws away.
    /// </remarks>
    [TestFixture]
    public sealed class GrantSheetTests
    {
        private static readonly string[] Reader = { "reader" };

        [Test]
        public void A_node_with_no_rules_and_nothing_above_shows_no_row()
        {
            var sheet = Sheets.Of(NodeRule.None, null);

            Assert.That(sheet.Rows, Is.Empty);
            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(sheet.CanEdit, Is.True);
            Assert.That(sheet.Warning, Is.Null);
        }

        [Test]
        public void A_rule_set_here_in_a_single_version_is_a_row_of_its_own_with_no_state()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "RL")), null);

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Own));
            Assert.That(row.Mask, Is.EqualTo(".R...L.."));
            Assert.That(row.States, Is.Empty);
            Assert.That(row.Version, Is.Not.Null.And.Length.EqualTo(32), "the version the row shows");
            Assert.That(row.Changed, Is.False);
            Assert.That(row.TakesRule && row.Removable && !row.Bound, Is.True);
        }

        [Test]
        public void An_empty_rule_is_a_rule_of_its_own_showing_no_letter()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "")), Sheets.Arriving(("reader", "RL")));

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Own));
            Assert.That(row.Mask, Is.EqualTo("........"));
            Assert.That(row.InheritedMask, Is.EqualTo(".R...L.."), "what the rule here replaces");
        }

        [Test]
        public void An_inherited_rule_shows_the_mask_that_arrives_and_takes_no_X()
        {
            var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("reader", "RL")));

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Inherited));
            Assert.That(row.Mask, Is.EqualTo(".R...L.."));
            Assert.That(row.States, Is.EqualTo(new[] { GrantState.Inherited }));
            Assert.That(row.Version, Is.Null);
            Assert.That(row.Removable, Is.False);
            Assert.That(row.TakesRule, Is.True, "a rule chosen on it replaces it here");
        }

        [Test]
        public void A_rule_from_above_the_box_refuses_is_refused_here()
        {
            var sheet = Sheets.Of(Rules.Node(true), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Ticked));
            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Inherited));
            Assert.That(row.States, Is.EqualTo(new[] { GrantState.RefusedHere }));
            Assert.That(row.Granted, Is.EqualTo("........"));
        }

        [Test]
        public void A_profile_the_row_cuts_shows_its_inherited_row_and_a_limit_bound_under_it()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:R")), Sheets.Arriving(("reader", "RL")));

            var rows = sheet.Rows;
            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader .R...L.. Inherited:Inherited",
                "reader .R...... Limit:Limit",
            }));
            Assert.That(rows.All(row => row.Bound && row.Group == 2), Is.True);
            Assert.That(rows[1].Version, Is.Null, "a limit is no version of its own");
            Assert.That(rows[1].TakesRule, Is.False, "a limit offers its X and no list");
            Assert.That(rows[1].Removable, Is.True);
            Assert.That(rows[0].TakesRule, Is.True, "a rule can be chosen on the inherited row");
            Assert.That(rows[1].Granted, Is.EqualTo(".R......"), "what both rows share");
        }

        [Test]
        public void A_profile_the_switch_cuts_shows_a_single_limit_row_as_well()
        {
            var sheet = Sheets.Of(Rules.Stored((null, "sw:inherit reader=R")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader .R...L.. Inherited:Inherited",
                "reader .R...... Limit:Limit",
            }));
        }

        [Test]
        public void A_profile_the_row_and_the_switch_both_cut_shows_a_single_limit_of_what_both_hold()
        {
            var sheet = Sheets.Of(
                Rules.Stored(("reader", "off:RL"), (null, "sw:inherit reader=CR")),
                Sheets.Arriving(("reader", "CRL")));

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader CR...L.. Inherited:Inherited",
                "reader .R...... Limit:Limit",
            }));
        }

        [Test]
        public void A_rule_taken_off_under_a_box_that_refused_limits_to_no_letter()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:deny")), Sheets.Arriving(("reader", "RL")));

            var limit = sheet.Rows.Single(row => row.Kind == GrantRowKind.Limit);
            Assert.That(limit.Mask, Is.EqualTo("........"));
            Assert.That(limit.Unreadable, Is.True, "the limit of a rule taken off while the node refused");
        }

        [Test]
        public void Nothing_shows_where_nothing_is_cut_also_for_a_version_that_cut_before()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:RL")), Sheets.Arriving(("reader", "R")));

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...... Inherited:Inherited" }));
        }

        [Test]
        public void A_rule_taken_off_when_nothing_arrived_limits_to_no_letter_and_is_no_rule_taken_off_under_the_box()
        {
            // `off:` with no letter lets nothing through, as `off:deny` does, and is told apart from
            // it: the limit of a rule taken off when nothing arrived, with the limit's own sentence.
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:")), Sheets.Arriving(("reader", "RL")));

            var limit = sheet.Rows.Single(row => row.Kind == GrantRowKind.Limit);
            Assert.That(limit.Mask, Is.EqualTo("........"));
            Assert.That(limit.Unreadable, Is.False);
            Assert.That(limit.Granted, Is.EqualTo("........"));
            Assert.That(GrantTab.StateTip(limit, true), Is.EqualTo(GrantTab.LimitTip + " " + GrantTab.LimitGrants + " " + GrantTab.LimitRemoval));
        }

        [Test]
        public void A_version_that_lets_through_with_nothing_arriving_shows_no_row()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:R")), null);

            Assert.That(sheet.Rows, Is.Empty);
            Assert.That(sheet.Available, Does.Contain("reader"));
        }

        [Test]
        public void No_limit_shows_while_the_box_refuses()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:R"), (null, "sw:deny")), Sheets.Arriving(("reader", "RL")));

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:RefusedHere" }));
        }

        [Test]
        public void The_press_that_clears_the_box_brings_every_inherited_row_in_with_no_limit()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:R"), (null, "sw:deny")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.PressBox(), Is.True);

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(sheet.BoxChanged, Is.True);
            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:Inherited changed" }),
                "the rule taken off is written anew with what arrives");
        }

        [Test]
        public void Clearing_the_box_shows_each_version_that_takes_the_rule_off_as_OK_writes_the_version()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", "off:deny"), (null, "sw:deny")), Sheets.Arriving(("reader", "RL")));

            sheet.PressBox();

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader .R...... Version:Version changed",
                "reader .R...L.. Version:TakenOff changed",
            }));
            Assert.That(sheet.Rows.All(row => row.Granted == ".R......"), Is.True, "what the gate grants once OK is written");
        }

        [Test]
        public void A_rule_chosen_and_taken_off_again_leaves_the_limit_as_the_store_gives_the_limit()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:CRL"), (null, "sw:inherit reader=R")), Sheets.Arriving(("reader", "CRL")));
            var before = Sheets.Lines(sheet);
            Assume.That(before, Is.EqualTo(new[] { "reader CR...L.. Inherited:Inherited", "reader .R...... Limit:Limit" }));

            Assert.That(sheet.Set("reader", "CRUSDLAP"), Is.Null);
            Assert.That(sheet.Forget(sheet.Rows.Single()), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(before), "the limit row and its X are back");
            Assert.That(sheet.Changes.IsEmpty, Is.True);
        }

        [Test]
        public void Versions_removed_down_to_a_rule_taken_off_with_nothing_arriving_keep_a_row_until_OK()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "off:R")), null);

            Assert.That(sheet.RemoveVersion("reader", sheet.Rows.Single(row => row.States.Contains(GrantState.Version)).Version), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader ........ Inherited:Inherited changed" }));
            Assert.That(sheet.Available, Does.Not.Contain("reader"), "the profile of a change is not offered again");
            Assert.That(sheet.Changes.Rows.Single().Kind, Is.EqualTo(RowChangeKind.RemoveVersions));
        }

        [Test]
        public void An_X_where_nothing_arrives_leaves_a_row_refused_here_under_a_ticked_box_and_inherited_under_a_cleared_box()
        {
            // Under a ticked box the row an X leaves is refused as every row the node does not
            // name is refused, whether or not a rule from above arrives for the profile.
            var ticked = Sheets.Of(Rules.Node(true, ("ops", "RL")), Sheets.Arriving(("reader", "RL")));
            var cleared = Sheets.Of(Rules.Node(false, ("ops", "RL")), Sheets.Arriving(("reader", "RL")));

            Assert.That(ticked.Remove("ops"), Is.True);
            Assert.That(cleared.Remove("ops"), Is.True);

            Assert.That(Sheets.Lines(ticked), Is.EqualTo(new[] { "ops ........ Inherited:RefusedHere changed", "reader .R...L.. Inherited:RefusedHere" }));
            Assert.That(Sheets.Single(ticked, "ops").Granted, Is.EqualTo("........"));
            Assert.That(Sheets.Lines(cleared), Is.EqualTo(new[] { "ops ........ Inherited:Inherited changed", "reader .R...L.. Inherited:Inherited" }));
        }

        [Test]
        public void A_rule_chosen_as_the_store_holds_the_rule_takes_back_the_X_before_the_choice()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "RL")), Sheets.Arriving(("reader", "RL")));
            sheet.Remove("reader");
            Assume.That(sheet.Changes.IsEmpty, Is.False);

            Assert.That(sheet.Set("reader", ".R...L.."), Is.Null);

            Assert.That(sheet.Changes.IsEmpty, Is.True);
            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Own" }));
        }

        [Test]
        public void The_last_version_that_reads_beside_a_lost_version_offers_no_X()
        {
            // Nothing would be left to write again: the lost version is removed first.
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", null), ("reader", "row:x")), null);

            Assert.That(sheet.Rows.Single(row => row.Kind == GrantRowKind.Version).Removable, Is.False);
            Assert.That(sheet.Rows.Where(row => row.Kind == GrantRowKind.Lost).All(row => row.Removable && !row.TakesRule), Is.True);
            Assert.That(sheet.RemoveVersion("reader", sheet.Rows.Single(row => row.Kind == GrantRowKind.Version).Version), Is.False);
        }

        [Test]
        public void A_group_no_version_of_which_reads_takes_a_rule_on_each_row_and_each_X_takes_the_rule_off()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", null), ("reader", "row:x")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Rows.All(row => row.Bound && row.TakesRule && row.Removable), Is.True);
            Assert.That(GrantTab.StateTip(sheet.Rows[0], true), Does.EndWith(GrantTab.EveryVersionChoice + " " + GrantTab.EveryVersionRemoval));

            Assert.That(sheet.Forget(sheet.Rows[0]), Is.True);

            Assert.That(sheet.Changes.Rows.Single().Kind, Is.EqualTo(RowChangeKind.TakeOff));
            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:Inherited changed" }));
        }

        [Test]
        public void A_reading_made_since_with_a_reason_makes_the_tab_read_only_and_no_later_reading_lifts_the_reason()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "R")), null);
            sheet.Set("reader", "CR......");

            sheet.Rebase(Rules.Node(false, ("reader", "R")), null, Sheets.Names, GrantTab.StoreUnusable);

            Assert.That(sheet.CanEdit, Is.False);
            Assert.That(sheet.Warning, Is.EqualTo(GrantTab.StoreUnusable));
            Assert.That(sheet.Changes.IsEmpty, Is.True, "nothing is handed to OK");
            Assert.That(sheet.Pending, Is.True, "and the press is still pending, which OK tells");
            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader CR...... Own changed" }));
            Assert.That(GrantTab.MaskTip(sheet.Rows.Single(), sheet.CanEdit), Does.EndWith(GrantTab.ChangedNotWritten));

            sheet.Rebase(Rules.Node(false, ("reader", "R")), null, Sheets.Names, null);
            Assert.That(sheet.CanEdit, Is.False, "a later reading does not lift the reason");

            var never = GrantSheet.Of(NodeRule.None, null, Sheets.Names, GrantTab.NotWatched);
            never.Rebase(NodeRule.None, null, Sheets.Names, null);
            Assert.That(never.ReadOnlyReason, Is.EqualTo(GrantTab.NotWatched));
        }

        [Test]
        public void Two_inheriting_versions_show_the_box_cleared_and_the_limit_they_set()
        {
            var sheet = Sheets.Of(
                Rules.Stored((null, "sw:inherit reader=RL"), (null, "sw:inherit reader=R")),
                Sheets.Arriving(("reader", "CRL")));

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared), "no mixed mark for versions that both inherit");
            Assert.That(sheet.Rows.Single(row => row.Kind == GrantRowKind.Limit).Mask, Is.EqualTo(".R......"));

            sheet.PressBox();
            sheet.PressBox();

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(sheet.BoxChanged, Is.True, "a box ticked and cleared over two versions is written");
            Assert.That(sheet.Rows.Any(row => row.Kind == GrantRowKind.Limit), Is.False, "and settles every profile");
        }

        [Test]
        public void A_box_never_set_ticked_and_cleared_holds_no_change()
        {
            var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("reader", "RL")));

            sheet.PressBox();
            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Ticked));
            Assert.That(sheet.BoxChanged, Is.True);
            Assert.That(Sheets.Single(sheet, "reader").States, Is.EqualTo(new[] { GrantState.RefusedHere }));

            sheet.PressBox();
            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(sheet.BoxChanged, Is.False);
        }

        [Test]
        public void A_ticked_box_cleared_and_ticked_again_holds_no_change()
        {
            var sheet = Sheets.Of(Rules.Node(true), null);

            sheet.PressBox();
            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(sheet.BoxChanged, Is.True);

            sheet.PressBox();
            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Ticked));
            Assert.That(sheet.BoxChanged, Is.False);
        }

        [Test]
        public void A_box_set_differently_in_copies_shows_the_mixed_mark_and_no_press_brings_it_back()
        {
            var sheet = Sheets.Of(Rules.Stored((null, "sw:deny"), (null, "sw:inherit")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Mixed));
            Assert.That(Sheets.Single(sheet, "reader").States, Is.EqualTo(new[] { GrantState.RefusedHere }),
                "the mixed mark counts as ticked");

            var seen = new List<GrantBox>();
            for (var press = 0; press < 4; press++)
            {
                Assert.That(sheet.PressBox(), Is.True);
                seen.Add(sheet.Box);
            }

            Assert.That(seen, Is.EqualTo(new[] { GrantBox.Ticked, GrantBox.Cleared, GrantBox.Ticked, GrantBox.Cleared }));
        }

        [Test]
        [TestCase(null)]
        [TestCase("sw:refuse")]
        public void A_box_whose_version_is_lost_or_out_of_form_shows_lost_and_a_press_clears_it(string text)
        {
            var sheet = Sheets.Of(Rules.Stored((null, text)), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Lost));
            Assert.That(Sheets.Single(sheet, "reader").States, Is.EqualTo(new[] { GrantState.RefusedHere }));

            sheet.PressBox();

            Assert.That(sheet.Box, Is.EqualTo(GrantBox.Cleared));
            Assert.That(sheet.BoxChanged, Is.True);
        }

        [Test]
        public void A_rule_in_several_versions_shows_a_row_for_each_bound_in_the_order_of_their_masks()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:CR"), ("reader", "row:RL")), Sheets.Arriving(("reader", "CRUL")));

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader .R...L.. Version:Version",
                "reader CR...... Version:Version",
            }));

            var rows = sheet.Rows;
            Assert.That(rows.Select(row => row.Version).Distinct().Count(), Is.EqualTo(2), "each row a version of its own");
            Assert.That(rows.All(row => row.Bound && !row.TakesRule && row.Removable), Is.True, "an X and no list");
            Assert.That(rows.All(row => row.Granted == ".R......"), Is.True, "what all versions hold");
        }

        [Test]
        public void A_version_that_takes_the_rule_off_in_a_group_shows_the_letters_it_recorded()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "off:R")), Sheets.Arriving(("reader", "RUL")));

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader .R...... Version:TakenOff",
                "reader .R...L.. Version:Version",
            }));
            Assert.That(sheet.Rows.Any(row => row.Kind == GrantRowKind.Limit), Is.False, "a group is no cut");
            Assert.That(sheet.Rows[0].Granted, Is.EqualTo(".R......"));
        }

        [Test]
        public void More_than_two_versions_show_as_more_than_two_rows()
        {
            var sheet = Sheets.Of(
                Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("reader", "row:CRL")),
                null);

            Assert.That(sheet.Rows.Count, Is.EqualTo(3));
            Assert.That(sheet.Rows.All(row => row.Group == 3), Is.True);
        }

        [Test]
        public void A_lost_rule_standing_alone_shows_no_letter_and_takes_a_rule_and_an_X()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", null)), Sheets.Arriving(("reader", "RL")));

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Lost));
            Assert.That(row.Mask, Is.EqualTo("........"));
            Assert.That(row.States, Is.EqualTo(new[] { GrantState.Lost }));
            Assert.That(row.Unreadable, Is.False);
            Assert.That(row.TakesRule && row.Removable && !row.Bound, Is.True);
        }

        [Test]
        public void A_rule_whose_text_is_out_of_form_shows_as_lost_and_unreadable()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:lr")), null);

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Lost));
            Assert.That(row.Unreadable, Is.True);
            Assert.That(row.Mask, Is.EqualTo("........"));
        }

        [Test]
        public void A_lost_version_beside_a_standing_version_is_a_row_of_the_group_and_the_group_grants_nothing()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", null)), Sheets.Arriving(("reader", "RL")));

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader ........ Lost:Lost",
                "reader .R...... Version:Version",
            }));
            Assert.That(sheet.Rows.All(row => row.Bound && row.Granted == "........"), Is.True,
                "a lost version refuses the profile at the node, whatever stands beside it");
        }

        [Test]
        public void A_rule_whose_profile_the_database_no_longer_names_shows_no_profile_first()
        {
            var sheet = GrantSheet.Of(Rules.Node(false, ("reader", "R")), null, new[] { "ops" }, null);

            Assert.That(Sheets.Single(sheet, "reader").States, Is.EqualTo(new[] { GrantState.NoProfile }));
        }

        [Test]
        public void An_inherited_rule_refused_here_for_a_name_the_database_no_longer_names_holds_both()
        {
            var sheet = GrantSheet.Of(Rules.Node(true), Sheets.Arriving(("reader", "R")), new[] { "ops" }, null);

            Assert.That(Sheets.Single(sheet, "reader").States, Is.EqualTo(new[] { GrantState.NoProfile, GrantState.RefusedHere }));
        }

        [Test]
        public void A_sheet_given_no_list_marks_no_row()
        {
            var sheet = GrantSheet.Of(Rules.Node(false, ("reader", "R")), null, null, null);

            Assert.That(Sheets.Single(sheet, "reader").States, Is.Empty);
            Assert.That(sheet.Available, Is.Empty);
        }

        [Test]
        public void A_rule_chosen_on_an_inherited_row_is_a_rule_of_the_node_changed_on_the_tab()
        {
            var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Set("reader", ".R......"), Is.Null);

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Own));
            Assert.That(row.Mask, Is.EqualTo(".R......"));
            Assert.That(row.Changed, Is.True);
            Assert.That(row.Version, Is.Null, "no version until OK writes one");
        }

        [Test]
        public void A_rule_chosen_back_to_what_the_store_holds_holds_no_change()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "RL")), null);

            Assert.That(sheet.Set("reader", ".R......"), Is.Null);
            Assert.That(Sheets.Single(sheet, "reader").Changed, Is.True);

            Assert.That(sheet.Set("reader", ".R...L.."), Is.Null);
            Assert.That(Sheets.Single(sheet, "reader").Changed, Is.False);
        }

        [Test]
        public void The_X_takes_the_rule_off_and_the_inherited_rule_shows_in_its_place_until_OK()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "R")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Remove("reader"), Is.True);

            var row = Sheets.Single(sheet, "reader");
            Assert.That(row.Kind, Is.EqualTo(GrantRowKind.Inherited));
            Assert.That(row.Mask, Is.EqualTo(".R...L.."));
            Assert.That(row.States, Is.EqualTo(new[] { GrantState.Inherited }));
            Assert.That(row.Changed, Is.True);
            Assert.That(sheet.Available, Does.Not.Contain("reader"), "the row is on the table until OK");
        }

        [Test]
        public void The_X_with_nothing_arriving_leaves_a_row_with_no_letter_until_OK()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "R")), null);

            Assert.That(sheet.Remove("reader"), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader ........ Inherited:Inherited changed" }));
        }

        [Test]
        public void The_X_takes_the_rule_off_under_a_box_that_refuses_as_well()
        {
            var sheet = Sheets.Of(Rules.Node(true, ("reader", "R")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Remove("reader"), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:RefusedHere changed" }));
        }

        [Test]
        public void The_X_on_a_rule_chosen_on_the_tab_for_a_profile_the_store_holds_nothing_for_brings_the_row_back()
        {
            var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("reader", "RL")));

            sheet.Set("reader", ".R......");
            Assert.That(sheet.Remove("reader"), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:Inherited" }));
        }

        [Test]
        public void The_X_on_a_lost_row_standing_alone_takes_the_rule_off()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", null)), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Remove("reader"), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:Inherited changed" }));
        }

        [Test]
        public void An_inherited_row_has_nothing_to_take_off()
        {
            var sheet = Sheets.Of(NodeRule.None, Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Remove("reader"), Is.False);
            Assert.That(sheet.Rows.Single().Changed, Is.False);
        }

        [Test]
        public void A_row_of_a_group_takes_no_rule_and_no_X_of_a_row_standing_alone()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", "row:RL")), null);

            Assert.That(sheet.Rows.Any(row => row.TakesRule), Is.False, "no list on a row of a group");
            Assert.That(sheet.Remove("reader"), Is.False);
            Assert.That(sheet.Rows.All(row => !row.Changed), Is.True);
        }

        [Test]
        public void The_X_on_a_version_removes_that_version_and_a_single_row_left_is_a_row_like_any_other()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", "row:RL")), Sheets.Arriving(("reader", "CRL")));
            var wider = sheet.Rows.Single(row => row.Mask == ".R...L..");

            Assert.That(sheet.RemoveVersion("reader", wider.Version), Is.True);

            var left = Sheets.Single(sheet, "reader");
            Assert.That(left.Kind, Is.EqualTo(GrantRowKind.Own));
            Assert.That(left.Mask, Is.EqualTo(".R......"));
            Assert.That(left.Changed, Is.True);
            Assert.That(left.TakesRule, Is.True);

            Assert.That(sheet.Set("reader", "CR......"), Is.Null, "a rule chosen once a single row is left");
            Assert.That(Sheets.Single(sheet, "reader").Mask, Is.EqualTo("CR......"));
        }

        [Test]
        public void Versions_are_removed_one_at_a_time_and_never_the_last()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("reader", "row:CRL")), null);
            var versions = sheet.Rows.Select(row => row.Version).ToList();

            Assert.That(sheet.RemoveVersion("reader", versions[0]), Is.True);
            Assert.That(sheet.Rows.Count, Is.EqualTo(2));
            Assert.That(sheet.Rows.All(row => row.Bound && row.Changed), Is.True);

            Assert.That(sheet.RemoveVersion("reader", versions[1]), Is.True);
            Assert.That(sheet.RemoveVersion("reader", versions[2]), Is.False, "the row left stands alone");
            Assert.That(sheet.RemoveVersion("reader", "00000000000000000000000000000000"), Is.False);
        }

        [Test]
        public void Removing_down_to_a_version_that_takes_the_rule_off_leaves_the_cut_it_holds()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "off:R")), Sheets.Arriving(("reader", "RL")));
            var granting = sheet.Rows.Single(row => row.States.Contains(GrantState.Version));

            sheet.RemoveVersion("reader", granting.Version);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "reader .R...L.. Inherited:Inherited changed",
                "reader .R...... Limit:Limit changed",
            }));
        }

        [Test]
        public void The_X_on_a_limit_settles_and_the_inherited_row_stands_alone_until_OK()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:R")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Settle("reader"), Is.True);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader .R...L.. Inherited:Inherited changed" }));
        }

        [Test]
        public void A_profile_with_no_limit_has_nothing_to_settle()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:RL")), Sheets.Arriving(("reader", "R")));

            Assert.That(sheet.Settle("reader"), Is.False);
            Assert.That(sheet.Settle("ops"), Is.False);
        }

        [Test]
        public void A_rule_can_be_chosen_on_the_inherited_row_of_a_profile_that_is_cut()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "off:R")), Sheets.Arriving(("reader", "RL")));

            Assert.That(sheet.Set("reader", "CRUSDLAP"), Is.Null);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[] { "reader CRUSDLAP Own changed" }));
        }

        [Test]
        public void No_ceiling_of_rows_is_kept()
        {
            var names = Enumerable.Range(0, 40).Select(i => "p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var sheet = GrantSheet.Of(NodeRule.None, null, names, null);

            foreach (var name in names)
            {
                Assert.That(sheet.Set(name, ".R...L.."), Is.Null);
            }

            Assert.That(sheet.Rows.Count, Is.EqualTo(names.Count));
        }

        [Test]
        public void A_name_or_a_mask_that_is_not_one_is_refused_and_nothing_is_taken()
        {
            var sheet = Sheets.Of(NodeRule.None, null);

            Assert.That(sheet.Set(" ", ".R......"), Is.EqualTo(GrantTab.NameTheProfile));
            Assert.That(sheet.Set("default", ".R......"), Is.EqualTo(GrantTab.ProfileNameRule));
            Assert.That(sheet.Set(" reader", ".R......"), Is.EqualTo(GrantTab.ProfileNameRule));
            Assert.That(sheet.Set("reader", "RL"), Is.EqualTo(GrantTab.MaskRule));
            Assert.That(sheet.Rows, Is.Empty);
        }

        [Test]
        public void The_pick_list_leaves_out_every_profile_the_table_shows()
        {
            var sheet = GrantSheet.Of(
                Rules.Node(false, ("reader", "R")),
                Sheets.Arriving(("ops", "RL")),
                new[] { "reader", "ops", "deploy", "default" },
                null);

            Assert.That(sheet.Offered, Is.EqualTo(new[] { "deploy", "ops", "reader" }), "a name no certificate can carry is not offered");
            Assert.That(sheet.Available, Is.EqualTo(new[] { "deploy" }));
            Assert.That(sheet.Shows("ops"), Is.True);
        }

        [Test]
        [TestCase("This is an earlier version of the entry.")]
        public void A_tab_that_takes_no_press_shows_the_rules_and_takes_no_press(string reason)
        {
            var sheet = GrantSheet.Of(
                Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("ops", "off:R")),
                Sheets.Arriving(("ops", "RL"), ("deploy", "R")),
                Sheets.Names,
                reason);
            var before = Sheets.Lines(sheet);

            Assert.That(sheet.CanEdit, Is.False);
            Assert.That(sheet.Warning, Is.EqualTo(reason));
            Assert.That(sheet.Set("deploy", "CRUSDLAP"), Is.EqualTo(GrantTab.NothingToChange));
            Assert.That(sheet.Remove("deploy"), Is.False);
            Assert.That(sheet.RemoveVersion("reader", sheet.Rows.First(row => row.Profile == "reader").Version), Is.False);
            Assert.That(sheet.Settle("ops"), Is.False);
            Assert.That(sheet.PressBox(), Is.False);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(before));
            Assert.That(before.Count, Is.EqualTo(5), "the group, the cut and the inherited row are all shown");
        }

        [Test]
        public void A_node_deeper_than_the_walk_is_told_so()
        {
            var node = TestNode.Root("root").Granting("reader", AclVerb.Read);
            for (var level = 1; level < AclResolver.MaxDepth + 1; level++)
            {
                node = node.Child("level " + level);
            }

            var sheet = GrantSheet.Of(NodeRule.None, AclResolver.Above(node.Parent), Reader, null);

            Assert.That(sheet.Warning, Is.EqualTo(GrantTab.TooDeep));
        }

        [Test]
        public void A_reading_made_since_moves_the_rows_nobody_pressed_and_keeps_the_rows_pressed()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "R"), ("ops", "R")), null);
            sheet.Set("ops", "CR......");

            sheet.Rebase(Rules.Node(false, ("reader", "RL"), ("ops", "RL")), Sheets.Arriving(("deploy", "R")), Sheets.Names, null);

            Assert.That(Sheets.Lines(sheet), Is.EqualTo(new[]
            {
                "deploy .R...... Inherited:Inherited",
                "ops CR...... Own changed",
                "reader .R...L.. Own",
            }));
        }

        [Test]
        public void A_reading_made_since_takes_the_list_of_names_as_well()
        {
            var sheet = GrantSheet.Of(Rules.Node(false, ("reader", "R")), null, new[] { "ops" }, null);
            Assert.That(Sheets.Single(sheet, "reader").States, Is.EqualTo(new[] { GrantState.NoProfile }));

            sheet.Rebase(Rules.Node(false, ("reader", "R")), null, new[] { "ops", "reader" }, null);

            Assert.That(Sheets.Single(sheet, "reader").States, Is.Empty);
            Assert.That(sheet.Offered, Is.EqualTo(new[] { "ops", "reader" }));
        }

        [Test]
        public void No_press_reaches_anything_but_the_sheet()
        {
            // The rules the sheet was given are what the store holds, and a press changes the
            // sheet and nothing else: the rule handed in reads as it did before every press.
            var rule = Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("ops", "off:R"), ("deploy", "row:C"), (null, "sw:inherit ops=R"));
            var sheet = Sheets.Of(rule, Sheets.Arriving(("ops", "RL")));

            sheet.RemoveVersion("reader", sheet.Rows.First(row => row.Profile == "reader").Version);
            sheet.Settle("ops");
            sheet.Remove("deploy");
            sheet.PressBox();

            Assert.That(rule.Row("reader").Versions.Count, Is.EqualTo(2));
            Assert.That(rule.Row("deploy").Versions.Count, Is.EqualTo(1));
            Assert.That(rule.Switch.Versions.Count, Is.EqualTo(1));
        }

        [Test]
        public void OK_is_handed_nothing_where_nothing_was_pressed()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), (null, "sw:deny")), Sheets.Arriving(("ops", "RL")));

            Assert.That(sheet.Changes.IsEmpty, Is.True);
        }

        [Test]
        public void OK_is_handed_nothing_for_presses_that_brought_every_row_and_the_box_back()
        {
            var sheet = Sheets.Of(Rules.Node(true, ("reader", "RL")), Sheets.Arriving(("ops", "R")));

            sheet.Set("reader", ".R......");
            sheet.Set("reader", ".R...L..");
            sheet.Set("ops", "CR......");
            sheet.Remove("ops");
            sheet.PressBox();
            sheet.PressBox();

            Assert.That(sheet.Changes.IsEmpty, Is.True);
        }

        [Test]
        public void OK_is_handed_a_rule_chosen_with_the_letters_wanted_and_the_rules_the_tab_showed()
        {
            var rule = Rules.Stored(("reader", "row:R"), ("reader", null));
            var sheet = Sheets.Of(rule, null);
            sheet.RemoveVersion("reader", sheet.Rows.Single(row => row.Mask == ".R......").Version);

            Assert.That(sheet.Set("reader", "CR......"), Is.Null);

            var change = sheet.Changes.Rows.Single();
            Assert.That(change.Kind, Is.EqualTo(RowChangeKind.Set));
            Assert.That(change.Profile, Is.EqualTo("reader"));
            Assert.That(VerbMask.Of(change.Letters), Is.EqualTo("CR......"));
            Assert.That(change.Removed, Is.Empty);
            Assert.That(change.Shown, Is.SameAs(rule), "the versions a chosen rule names: every version the tab showed");
            Assert.That(sheet.Changes.Switch, Is.Null);
        }

        [Test]
        public void OK_is_handed_a_rule_taken_off_with_the_letters_arriving_at_the_press()
        {
            var rule = Rules.Node(false, ("reader", "R"));
            var sheet = Sheets.Of(rule, Sheets.Arriving(("reader", "RL")));

            sheet.Remove("reader");
            sheet.Rebase(rule, Sheets.Arriving(("reader", "CRL")), Sheets.Names, null);

            var change = sheet.Changes.Rows.Single();
            Assert.That(change.Kind, Is.EqualTo(RowChangeKind.TakeOff));
            Assert.That(VerbMask.Of(change.Letters), Is.EqualTo(".R...L.."), "the letters the tab showed at the press");
            Assert.That(change.Shown, Is.SameAs(rule));
        }

        [Test]
        public void OK_is_handed_each_version_removed_from_a_group()
        {
            var rule = Rules.Stored(("reader", "row:R"), ("reader", "row:RL"), ("reader", "row:CRL"));
            var sheet = Sheets.Of(rule, null);
            var removed = sheet.Rows.Take(2).Select(row => row.Version).ToList();

            sheet.RemoveVersion("reader", removed[0]);
            sheet.RemoveVersion("reader", removed[1]);

            var change = sheet.Changes.Rows.Single();
            Assert.That(change.Kind, Is.EqualTo(RowChangeKind.RemoveVersions));
            Assert.That(change.Letters, Is.Null);
            Assert.That(change.Removed, Is.EqualTo(removed.OrderBy(id => id, System.StringComparer.Ordinal)));
            Assert.That(change.Shown, Is.SameAs(rule));
        }

        [Test]
        public void OK_is_handed_a_limit_settled_with_the_letters_arriving_at_the_press()
        {
            var rule = Rules.Stored(("reader", "off:R"));
            var sheet = Sheets.Of(rule, Sheets.Arriving(("reader", "RL")));

            sheet.Settle("reader");

            var change = sheet.Changes.Rows.Single();
            Assert.That(change.Kind, Is.EqualTo(RowChangeKind.Settle));
            Assert.That(VerbMask.Of(change.Letters), Is.EqualTo(".R...L.."));
            Assert.That(change.Shown, Is.SameAs(rule));
        }

        [Test]
        public void OK_is_handed_a_rule_taken_off_where_a_limit_is_settled_after_versions_were_removed()
        {
            var sheet = Sheets.Of(Rules.Stored(("reader", "row:RL"), ("reader", "off:R")), Sheets.Arriving(("reader", "RL")));
            sheet.RemoveVersion("reader", sheet.Rows.Single(row => row.States.Contains(GrantState.Version)).Version);

            Assert.That(sheet.Settle("reader"), Is.True);

            var change = sheet.Changes.Rows.Single();
            Assert.That(change.Kind, Is.EqualTo(RowChangeKind.TakeOff), "a single version naming every version shown");
            Assert.That(VerbMask.Of(change.Letters), Is.EqualTo(".R...L.."));
        }

        [Test]
        public void OK_is_handed_the_box_ticked_with_no_letter_arriving()
        {
            var rule = NodeRule.None;
            var sheet = Sheets.Of(rule, Sheets.Arriving(("reader", "RL")));

            sheet.PressBox();

            var box = sheet.Changes.Switch;
            Assert.That(box.Refuses, Is.True);
            Assert.That(box.Arriving, Is.Empty);
            Assert.That(box.Shown, Is.SameAs(rule));
            Assert.That(sheet.Changes.Rows, Is.Empty);
        }

        [Test]
        public void OK_is_handed_the_box_cleared_with_the_letters_arriving_for_each_profile_any_arrive_for()
        {
            var rule = Rules.Node(true, ("ops", "R"));
            var above = TestNode.Root("a group above").Holding(Rules.Node(false, ("reader", "RL"), ("ops", "CR"), ("deploy", "")));
            var sheet = GrantSheet.Of(rule, AclResolver.Above(above), Sheets.Names, null);

            sheet.PressBox();

            var box = sheet.Changes.Switch;
            Assert.That(box.Refuses, Is.False);
            Assert.That(box.Arriving.Keys, Is.EquivalentTo(new[] { "ops", "reader" }), "a profile nothing arrives for is left out");
            Assert.That(VerbMask.Of(box.Arriving["ops"]), Is.EqualTo("CR......"), "with a rule at the node or not");
            Assert.That(VerbMask.Of(box.Arriving["reader"]), Is.EqualTo(".R...L.."));
        }

        [Test]
        public void OK_is_handed_a_change_for_each_profile_pressed_in_order_and_a_press_kept_across_a_reading()
        {
            var sheet = Sheets.Of(Rules.Node(false, ("reader", "R"), ("ops", "R")), null);
            sheet.Set("reader", "CR......");
            sheet.Set("ops", "CR...L..");
            var shown = sheet.Changes.Rows.Select(change => change.Shown).ToList();

            sheet.Rebase(Rules.Node(false, ("reader", "RL"), ("ops", "RL")), null, Sheets.Names, null);

            var changes = sheet.Changes.Rows;
            Assert.That(changes.Select(change => change.Profile), Is.EqualTo(new[] { "ops", "reader" }));
            Assert.That(changes.Select(change => change.Shown), Is.EqualTo(shown), "the rules the tab showed at each press");
        }
    }
}
