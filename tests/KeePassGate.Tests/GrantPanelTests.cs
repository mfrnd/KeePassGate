using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The grant editor as a control: what it draws, and what it writes.
    /// </summary>
    /// <remarks>
    /// Driven through the panel's own methods rather than its controls, because
    /// `Button.PerformClick` does nothing on a form that was never shown, silently, and a test that
    /// pressed one would assert against a table nobody touched.
    /// </remarks>
    [TestFixture]
    public sealed class GrantPanelTests
    {
        [Test]
        public void A_node_with_nothing_on_it_draws_an_empty_table()
        {
            using (var panel = Panel(new FakeGrantStore(null), null))
            {
                Assert.That(panel.Lines, Is.Empty);
                Assert.That(panel.Warning, Is.Null);
                Assert.That(panel.Writes, Is.Zero);
            }
        }

        [Test]
        public void The_table_says_what_it_is_to_anything_that_cannot_see_it()
        {
            // It has no text of its own and it is the whole page, so without a name a screen reader
            // announced the KPG tab on a group or an entry by its control type alone. The clients
            // and profiles tables were given one; this is the third.
            using (var panel = Panel(new FakeGrantStore(null), null))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(grid.AccessibleName, Is.EqualTo(GrantTab.GridLabel));
                Assert.That(grid.AccessibleName, Is.Not.EqualTo(grid.Name),
                    "a table announcing its own lookup name tells a listener nothing");
            }
        }

        [Test]
        public void A_rule_is_drawn_as_its_mask()
        {
            var store = new FakeGrantStore("{\"reader\":[\"read\",\"list\"]}");

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.." }));
            }
        }

        [Test]
        public void Writing_a_rule_stores_it_and_redraws()
        {
            var store = new FakeGrantStore(null);

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.SetGrant("reader", ".R...LA."), Is.Null);

                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...LA." }));
                Assert.That(panel.Writes, Is.EqualTo(1));
                Assert.That(AclDocument.Read(store.Current).Grant("reader").CoversAttachments, Is.True);
            }
        }

        [Test]
        public void Opening_a_tab_and_changing_nothing_writes_nothing()
        {
            // The one that decides whether looking at a database leaves it modified. The grid
            // commits for reasons that are not edits, and every one of them lands here.
            var store = new FakeGrantStore("{\"default\":\"inherit\",\"reader\":[\"read\"]}");

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.SetGrant("reader", ".R......"), Is.Null, "the same rule again");
                Assert.That(panel.RemoveGrant("nobody-has-this"), Is.False);

                Assert.That(panel.Writes, Is.Zero);
                Assert.That(store.Writes, Is.Zero);
            }
        }

        [Test]
        public void Writing_the_same_rule_twice_writes_once()
        {
            var store = new FakeGrantStore(null);

            using (var panel = Panel(store, null))
            {
                panel.SetGrant("reader", ".R......");
                panel.SetGrant("reader", ".R......");

                Assert.That(panel.Writes, Is.EqualTo(1));
            }
        }

        [Test]
        public void Removing_the_last_rule_takes_the_key_away()
        {
            var store = new FakeGrantStore("{\"default\":\"inherit\",\"reader\":[\"read\"]}");

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.RemoveGrant("reader"), Is.True);

                Assert.That(store.Current, Is.Null, "no rules is the absent key");
                Assert.That(panel.Lines, Is.Empty);
            }
        }

        [Test]
        public void A_rule_that_is_not_one_is_refused_and_nothing_is_written()
        {
            var store = new FakeGrantStore(null);

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.SetGrant("has a space", ".R......"), Is.Not.Null);
                Assert.That(panel.SetGrant("reader", "nonsense"), Is.Not.Null);

                Assert.That(store.Writes, Is.Zero);
                Assert.That(panel.Lines, Is.Empty);
            }
        }

        [Test]
        public void A_mask_is_stored_in_one_spelling_however_it_arrives()
        {
            // Nothing on the tab types a mask any more, but a document can hold either case and the
            // panel is what the end to end test drives, so what it accepts is worth pinning: the
            // table shows one spelling whatever it was given.
            var store = new FakeGrantStore(null);

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.SetGrant("reader", ".r...la."), Is.Null, "typed in any case");

                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...LA." }),
                    "and rewritten to the one spelling as soon as it commits");
            }
        }

        [Test]
        public void A_rule_stored_for_no_profile_says_what_it_is_when_it_is_pressed()
        {
            // A document written by hand can hold one, since the reader takes any member name.
            // Pressing its rule said "Choose a profile first", a sentence about the new line at the
            // bottom, on a row that has no profile to choose and can only be removed.
            using (var panel = Panel(new FakeGrantStore("{\"\":[\"read\"]}"), null))
            {
                Assert.That(panel.Lines.Count, Is.EqualTo(1), "the rule is on the table");

                panel.OpenPicker(0);

                Assert.That(panel.Picking, Is.False);
                Assert.That(panel.Warning, Is.EqualTo(GrantTab.RuleWithoutProfile));
            }
        }

        [Test]
        public void What_a_group_above_grants_is_drawn_among_the_local_rules()
        {
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);

            using (var panel = Panel(new FakeGrantStore("{\"editor\":[\"read\",\"update\"]}"), above))
            {
                Assert.That(panel.Lines, Is.EqualTo(new[] { "editor .RU.....", "reader .R......" }));
            }
        }

        [Test]
        public void An_inherited_rule_is_drawn_as_one_and_offers_nothing_to_remove()
        {
            // A table that draws a rule from a group above exactly as it draws a local one invites
            // an operator to remove something this node does not have, and reads as if this node
            // granted what a group above it grants. Gray, and no mark in the column that takes a
            // rule away, are the whole of the difference on screen.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);

            using (var panel = Panel(new FakeGrantStore("{\"editor\":[\"read\"]}"), above))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                Assert.That(grid, Is.Not.Null);

                var local = Row(grid, "editor");
                var inherited = Row(grid, "reader");

                Assert.That(local.Cells[2].Value, Is.Not.Null.And.Not.EqualTo(string.Empty),
                    "a local rule can be taken away, so it says so");
                Assert.That(inherited.Cells[2].Value, Is.EqualTo(string.Empty),
                    "an inherited rule has nothing here to remove");
                Assert.That(inherited.DefaultCellStyle.ForeColor, Is.EqualTo(SystemColors.GrayText));

                // And gray while selected, because a grid takes a row as soon as it has the focus
                // and a row in the selection color is a row that looks local.
                Assert.That(inherited.DefaultCellStyle.SelectionForeColor, Is.EqualTo(SystemColors.GrayText));
                Assert.That(local.DefaultCellStyle.SelectionForeColor, Is.Not.EqualTo(SystemColors.GrayText));
            }
        }

        [Test]
        public void A_local_rule_says_on_its_mask_what_it_is_narrowing()
        {
            // One row per profile however the rule got there, so a local rule replaces the
            // inherited one on the table and what it narrowed is nowhere else on the tab. On the
            // mask rather than in a column of its own: nothing on this tab scrolls and the group
            // dialog gives it less room than any other page this plugin is on, so a fourth column
            // would come out of the two that are already sharing. `GrantTabFitsTests` measures it.
            // TODO(step 13): that test arrives with the tab.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read, AclVerb.Update);

            using (var panel = Panel(new FakeGrantStore("{\"reader\":[\"read\"]}"), above))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);
                var narrowed = Row(grid, "reader");

                Assert.That(narrowed.Cells[1].ToolTipText, Does.Contain(".RU....."),
                    "so narrowing is visibly narrowing");
                Assert.That(narrowed.Cells[1].ToolTipText, Does.StartWith("Narrows"));
            }
        }

        [Test]
        public void A_local_rule_granting_more_than_the_one_above_says_it_widens_it()
        {
            // Nearest wins, so the rule here is in force in full. Said to narrow the group's rule,
            // it told an operator who had just widened an entry that the group still bounded it.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);

            using (var panel = Panel(new FakeGrantStore("{\"reader\":[\"create\",\"read\",\"protected\"]}"), above))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(Row(grid, "reader").Cells[1].ToolTipText, Does.StartWith("Widens .R......"));
            }
        }

        [Test]
        public void A_local_rule_neither_inside_nor_around_the_one_above_says_it_replaces_it()
        {
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read, AclVerb.List);

            using (var panel = Panel(new FakeGrantStore("{\"reader\":[\"read\",\"update\"]}"), above))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(Row(grid, "reader").Cells[1].ToolTipText, Does.StartWith("Replaces .R...L.."));
            }
        }

        [Test]
        public void A_local_rule_granting_what_the_one_above_grants_says_it_is_the_same()
        {
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);

            using (var panel = Panel(new FakeGrantStore("{\"reader\":[\"read\"]}"), above))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(Row(grid, "reader").Cells[1].ToolTipText, Does.StartWith("The same as .R......"));
            }
        }

        [Test]
        public void A_local_rule_narrowing_nothing_says_nothing_on_its_mask()
        {
            // Nothing above names this profile, so there is no narrowing to report and a tooltip
            // saying so would be a sentence about an absence.
            using (var panel = Panel(new FakeGrantStore("{\"reader\":[\"read\"]}"), null))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(Row(grid, "reader").Cells[1].ToolTipText, Is.Empty);
            }
        }

        [Test]
        public void Nothing_is_chosen_until_somebody_chooses_it()
        {
            // A grid selects its first row for itself. On an entry whose only rules come from a
            // group above, that paints an inherited rule in the selection color and hides the gray
            // that says this node does not own it.
            var above = TestNode.Root("root").Granting("reader", AclVerb.Read);

            using (var panel = Panel(new FakeGrantStore(null), above))
            {
                var grid = ControlTree.FindByName<DataGridView>(panel, GrantTab.GridControlName);

                Assert.That(grid.SelectedRows, Is.Empty);
                Assert.That(grid.SelectedCells, Is.Empty);
            }
        }

        private static DataGridViewRow Row(DataGridView grid, string profile)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Equals(row.Cells[0].Value, profile))
                {
                    return row;
                }
            }

            Assert.Fail("no row for " + profile);
            return null;
        }

        [Test]
        public void An_unreadable_rule_here_locks_the_table_and_offers_to_discard_it()
        {
            var store = new FakeGrantStore("{\"reader\":[\"purge\"]}");

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.Warning, Does.Contain("cannot be read"));
                Assert.That(panel.SetGrant("reader", ".R......"), Is.Not.Null);
                Assert.That(store.Writes, Is.Zero, "and it is left exactly as it was found");

                panel.Discard();

                Assert.That(panel.Warning, Is.Null);
                Assert.That(store.Current, Is.Null);
                Assert.That(panel.SetGrant("reader", ".R......"), Is.Null, "and now it can be edited");
            }
        }

        [Test]
        public void The_button_that_discards_unreadable_rules_says_why_they_refuse()
        {
            // The warning over the table says what is wrong and what to do, in the two lines the
            // group dialog leaves it. Why the rules refuse rather than being skipped is for whoever
            // is deciding whether to discard them, so it is on the button they would press.
            using (var panel = Panel(new FakeGrantStore("{\"reader\":[\"purge\"]}"), null))
            {
                var discard = ControlTree.FindByName<Button>(panel, GrantTab.DiscardButtonName);
                Assert.That(discard, Is.Not.Null);

                var tips = (ToolTip)typeof(GrantPanel)
                    .GetField("_tips", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(panel);

                Assert.That(tips.GetToolTip(discard), Is.EqualTo(Tooltips.Wrapped(GrantTab.DiscardButtonHelp)));
                Assert.That(panel.Warning, Does.Not.Contain("safe reading"), "and it is said once");
            }
        }

        [Test]
        public void An_edit_that_could_not_be_kept_is_said_on_the_tab_and_refused()
        {
            var store = new FakeGrantStore("{\"reader\":[\"read\"]}");
            var sheet = new GrantSheet(store.Read(), null, null, "This KeePass keeps it somewhere else.");

            using (var panel = new GrantPanel(sheet, store, GrantTab.GroupScope, null))
            {
                Assert.That(panel.Warning, Is.EqualTo("This KeePass keeps it somewhere else."));
                Assert.That(panel.Lines, Is.Not.Empty, "the rules are still worth reading");
                Assert.That(panel.SetGrant("editor", ".R......"), Is.Not.Null);
                Assert.That(store.Writes, Is.Zero);
            }
        }

        [Test]
        public void A_store_that_throws_says_so_rather_than_taking_KeePass_with_it()
        {
            var store = new FakeGrantStore { Refusal = new System.Reflection.TargetInvocationException(new System.Exception("nowhere to put it")) };

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.SetGrant("reader", ".R......"), Is.Null);

                Assert.That(panel.Warning, Does.Contain("not in force"));
                Assert.That(panel.Writes, Is.Zero);
            }
        }

        [Test]
        public void A_rule_the_store_would_not_take_is_off_the_table_as_well_as_out_of_force()
        {
            // The sentence says it is not in force, so the table must not still be showing it.
            // The sheet must not still be holding it either, or the next write of anything at
            // all would carry it in, with nothing said the second time.
            var store = new FakeGrantStore { Refusal = new System.Reflection.TargetInvocationException(new System.Exception("nowhere to put it")) };

            using (var panel = Panel(store, null))
            {
                panel.SetGrant("reader", ".R......");

                Assert.That(panel.Warning, Does.Contain("not in force"));
                Assert.That(panel.Lines, Is.Empty, "the refused rule is not on the table");
                Assert.That(panel.Writes, Is.Zero);
            }
        }

        [Test]
        public void The_pick_list_holds_the_profiles_that_have_no_rule_here_yet()
        {
            var store = new FakeGrantStore("{\"reader\":[\"read\"]}");

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.ProfileChoices, Is.EqualTo(new[] { "editor" }));
            }
        }

        [Test]
        public void A_profile_chosen_from_the_list_arrives_granting_nothing()
        {
            // Which is half a decision and the safe half. What it may do is the next thing chosen,
            // and until it is, the rule is on the table refusing everything it can refuse.
            var store = new FakeGrantStore(null);

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.AddGrant("reader"), Is.Null);

                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader ........" }));
                Assert.That(AclDocument.Read(store.Current).Grant("reader").Count, Is.Zero);
            }
        }

        [Test]
        public void A_profile_that_already_has_a_rule_here_is_refused_rather_than_started_again()
        {
            var store = new FakeGrantStore("{\"reader\":[\"read\",\"list\"]}");

            using (var panel = Panel(store, null))
            {
                Assert.That(panel.AddGrant("reader"), Does.Contain("already a rule"));

                // The rule it would have replaced, untouched.
                Assert.That(panel.Lines, Is.EqualTo(new[] { "reader .R...L.." }));
                Assert.That(panel.Writes, Is.Zero);
            }
        }

        [Test]
        public void A_table_with_every_profile_on_it_says_nothing()
        {
            // There was a sentence here, and what it said was that the rows already on the table
            // are all of them, which the rows say. It cost four lines of a page that has 314
            // pixels. The case below, where the database names no profiles at all, is the one
            // worth a sentence: there the table is empty and nothing says where one comes from.
            var store = new FakeGrantStore("{\"reader\":[\"read\"],\"editor\":[\"update\"]}");

            using (var panel = Panel(store, null))
            {
                panel.OpenProfilePicker(0);

                Assert.That(panel.Warning, Is.Null);
                Assert.That(panel.Picking, Is.False);
            }
        }

        [Test]
        public void A_database_that_names_no_profiles_says_where_one_is_made()
        {
            var store = new FakeGrantStore(null);

            using (var panel = new GrantPanel(
                new GrantSheet(null, null, null, null), store, GrantTab.GroupScope, null))
            {
                panel.OpenProfilePicker(0);

                Assert.That(panel.Warning, Is.EqualTo(GrantTab.NoProfilesYet));
                Assert.That(GrantTab.NoProfilesYet, Does.Contain("Pair a client"),
                    "which is the only way a profile exists at all so far");
            }
        }

        [Test]
        public void An_inherited_profile_is_narrowed_on_the_row_it_is_already_on()
        {
            // So it is not in the pick list, and choosing a rule on that row is what makes it this
            // node's own. Two rows for one profile is a thing the document cannot hold.
            var above = TestNode.Root("root").Granting("editor", AclVerb.Read, AclVerb.List);
            var store = new FakeGrantStore(null);

            using (var panel = Panel(store, above))
            {
                Assert.That(panel.ProfileChoices, Is.EqualTo(new[] { "reader" }));
                Assert.That(panel.Lines, Is.EqualTo(new[] { "editor .R...L.." }));

                Assert.That(panel.SetGrant("editor", ".R......"), Is.Null);

                Assert.That(panel.Lines, Is.EqualTo(new[] { "editor .R......" }));
                Assert.That(panel.ProfileChoices, Is.EqualTo(new[] { "reader" }));
            }
        }

        private static GrantPanel Panel(IGrantStore store, IAclNode above)
        {
            return new GrantPanel(
                new GrantSheet(store.Read(), above, new[] { "reader", "editor" }, null),
                store,
                GrantTab.GroupScope,
                null);
        }

    }
}
