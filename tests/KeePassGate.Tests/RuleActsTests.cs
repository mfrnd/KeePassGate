using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What an OK on the KPG tab of a node's dialog writes, `STORE.md` §617 and §629: each act
    /// composed over the reading the write is planned against, from the presses the tab handed
    /// over and what the tab showed at each of them.
    /// </summary>
    /// <remarks>
    /// The records are held to `tests/Shared/StoreRecordVectors.json`, printed by the measured
    /// code and never from this side: the members of each record an act composes, its nonce aside,
    /// and composed again with the vector's nonce, its content, its check, its identifier and every
    /// string of its entry. The stores an act is composed over are made by the tests' own composer.
    /// </remarks>
    [TestFixture]
    public sealed class RuleActsTests
    {
        private const string Root = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string N = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string M = "cccccccccccccccccccccccccccccccc";
        private const string G = "dddddddddddddddddddddddddddddddd";
        private const string E = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        private const string Nonce = "00112233445566778899aabbccddeeff";

        private static readonly string[] Alphabet = { string.Empty, "R", "U", "A", "RU", "RA", "UA", "RUA" };

        [TestCase("version, a row that grants", "pf", "RU")]
        [TestCase("version, an empty rule", "pg", "")]
        public void A_rule_chosen_is_the_measured_record(string vector, string profile, string mask)
        {
            var store = new ComposedStore(MeasuredKey());
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Set(profile, mask, NodeRule.None)), out var left);

                Assert.That(left, Is.Empty);
                AssertMeasured(act.Records.Single(), vector);
            }
        }

        [TestCase("version, a rule taken off, with the mask that arrived", "RLA")]
        [TestCase("version, a rule taken off, nothing having arrived", "")]
        public void A_rule_taken_off_where_the_switch_lets_through_is_the_measured_record(string vector, string arriving)
        {
            var store = new ComposedStore(MeasuredKey());
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(TakeOff("pf", arriving, NodeRule.None)), out _);

                AssertMeasured(act.Records.Single(), vector);
            }
        }

        [Test]
        public void A_rule_taken_off_where_the_switch_refuses_is_the_measured_record()
        {
            var store = new ComposedStore(MeasuredKey());
            var refusing = store.Add(StoreComposer.RuleVersion(MeasuredKey(), SwitchOf(N), "sw:deny"));
            store.Restate(StoreComposer.RuleVersionItem(refusing, SwitchOf(N), "sw:deny"));
            using (var reading = store.Read())
            {
                var shown = StoredRules.Of(reading).At(N);
                Assume.That(shown.Switch.Refuses, Is.True);

                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(TakeOff("pf", "RU", shown)), out _);

                AssertMeasured(act.Records.Single(), "version, a rule taken off while the switch refused");
            }
        }

        [Test]
        public void A_rule_chosen_over_two_versions_made_apart_names_both_as_measured()
        {
            var store = new ComposedStore(MeasuredKey());
            var granting = store.Add(Measured(RowOf(N, "pf"), "row:RU"));
            var takenOff = store.Add(Measured(RowOf(N, "pf"), "off:RLA"));
            store.RestateAll(new[]
            {
                StoreComposer.RuleVersionItem(granting, RowOf(N, "pf"), "row:RU"),
                StoreComposer.RuleVersionItem(takenOff, RowOf(N, "pf"), "off:RLA"),
            });
            Assume.That(granting.Hex, Is.EqualTo((string)Records()["version, a row that grants"]["id"]));
            Assume.That(takenOff.Hex, Is.EqualTo((string)Records()["version, a rule taken off, with the mask that arrived"]["id"]));

            using (var reading = store.Read())
            {
                var shown = StoredRules.Of(reading).At(N);
                Assume.That(shown.Row("pf").Versions, Has.Count.EqualTo(2));

                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Set("pf", "R", shown)), out _);

                AssertMeasured(act.Records.Single(), "version, a row naming the versions it replaces");
            }
        }

        [Test]
        public void A_limit_settled_renews_the_rule_taken_off_as_measured()
        {
            var store = new ComposedStore(MeasuredKey());
            var takenOff = store.Add(Measured(RowOf(N, "pf"), "off:RLA"));
            store.Restate(StoreComposer.RuleVersionItem(takenOff, RowOf(N, "pf"), "off:RLA"));
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Settle("pf", "CRUDLA", StoredRules.Of(reading).At(N))), out _);

                AssertMeasured(act.Records.Single(), "version, a rule taken off renewed by a write above");
            }
        }

        [Test]
        public void A_limit_settled_renews_the_inheriting_switch_as_measured()
        {
            const string Inheriting = "sw:inherit Zeta=C p-b=U pa=RL pf=RLA pg=R";
            var store = new ComposedStore(MeasuredKey());
            var inheriting = store.Add(Measured(SwitchOf(N), Inheriting));
            store.Restate(StoreComposer.RuleVersionItem(inheriting, SwitchOf(N), Inheriting));
            Assume.That(inheriting.Hex, Is.EqualTo((string)Records()["version, the switch inheriting, with a mask for each profile"]["id"]));
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Settle("pf", "CRUDLA", StoredRules.Of(reading).At(N))), out _);

                AssertMeasured(act.Records.Single(), "version, the switch renewed by a write above");
            }
        }

        [Test]
        public void The_box_ticked_is_the_measured_record()
        {
            var store = new ComposedStore(MeasuredKey());
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Box(true, NodeRule.None), out _);

                AssertMeasured(act.Records.Single(), "version, the switch refusing");
            }
        }

        [TestCase("version, the switch inheriting, nothing having arrived", "")]
        [TestCase("version, the switch inheriting, with a mask for each profile", "Zeta=C p-b=U pa=RL pf=RLA pg=R")]
        public void The_box_cleared_is_the_measured_record(string vector, string arriving)
        {
            // The tab showed no version of the switch, and a version refusing reached the store
            // under the dialog: the store does not give what the box was cleared to, so the version
            // is written, naming nothing the tab did not show.
            var store = new ComposedStore(MeasuredKey());
            var landed = store.Add(StoreComposer.RuleVersion(MeasuredKey(), SwitchOf(N), "sw:deny"));
            store.Restate(StoreComposer.RuleVersionItem(landed, SwitchOf(N), "sw:deny"));
            var pairs = arriving.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => (pair.Split('=')[0], pair.Split('=')[1]))
                .ToArray();
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Box(false, NodeRule.None, pairs), out _);

                AssertMeasured(act.Records.Single(), vector);
            }
        }

        [Test]
        public void A_rule_chosen_names_the_versions_shown_and_everything_they_name()
        {
            var store = Store();
            var first = Put(store, RowOf(N, "pf"), "row:");
            var second = Put(store, RowOf(N, "pf"), "row:R", first.Hex);
            var shown = Shown(store);

            var act = Act(store, Rows(Set("pf", "RU", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "row:RU", Sorted(first.Hex, second.Hex)) }));
            Write(store, act);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:RU" }));
        }

        [Test]
        public void A_rule_chosen_as_the_store_holds_it_in_a_single_version_writes_nothing()
        {
            var store = Store();
            Put(store, RowOf(N, "pf"), "row:R");

            Assert.That(Act(store, Rows(Set("pf", "R", Shown(store)))), Is.Null);
        }

        [Test]
        public void A_rule_chosen_as_one_of_two_versions_holds_it_is_written_and_names_both()
        {
            var store = Store();
            var narrow = Put(store, RowOf(N, "pf"), "row:R");
            var wide = Put(store, RowOf(N, "pf"), "row:RU");

            var act = Act(store, Rows(Set("pf", "R", Shown(store))));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "row:R", Sorted(narrow.Hex, wide.Hex)) }));
        }

        [Test]
        public void A_rule_taken_off_where_the_switch_lets_through_records_what_the_tab_showed_arriving()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");

            var act = Act(store, Rows(TakeOff("pf", "RUA", Shown(store))));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:RUA", Sorted(granting.Hex)) }));
        }

        [Test]
        public void A_rule_taken_off_renews_each_inheriting_version_shown_that_records_other_letters_for_the_profile()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            var narrow = Put(store, SwitchOf(N), "sw:inherit pf=R pg=L");
            Put(store, SwitchOf(N), "sw:inherit pf=RU pg=R");

            var act = Act(store, Rows(TakeOff("pf", "RU", Shown(store))));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "off:RU", Sorted(granting.Hex)),
                (SwitchOf(N), "sw:inherit pf=RU pg=L", Sorted(narrow.Hex)),
            }));
        }

        [Test]
        public void A_rule_taken_off_where_the_switch_refuses_is_off_deny_and_renews_no_switch()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            Put(store, SwitchOf(N), "sw:deny");

            var act = Act(store, Rows(TakeOff("pf", "RU", Shown(store))));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:deny", Sorted(granting.Hex)) }));
        }

        [TestCase("sw:deny", "sw:inherit pf=RU")]
        [TestCase("sw:inherit pf=RU", "sw:deny")]
        public void A_rule_taken_off_where_versions_of_the_switch_differ_is_off_deny(string first, string second)
        {
            var store = Store();
            Put(store, RowOf(N, "pf"), "row:R");
            Put(store, SwitchOf(N), first);
            Put(store, SwitchOf(N), second);
            Assume.That(Shown(store).Switch.Mixed, Is.True);

            var act = Act(store, Rows(TakeOff("pf", "RU", Shown(store))));

            Assert.That(Written(act).Select(version => version.Doc), Is.EqualTo(new[] { "off:deny" }));
        }

        [Test]
        public void A_rule_taken_off_and_the_box_ticked_on_the_same_tab_write_off_deny_whichever_came_first()
        {
            // `STORE.md` §617: the text follows the switch as the OK leaves it. The press on the
            // row was made while the box was cleared, and the box was ticked after it.
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            var shown = Shown(store);

            var act = Act(store, new RuleChanges(new[] { TakeOff("pf", "RU", shown) }, new SwitchChange(true, shown, null, null)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "off:deny", Sorted(granting.Hex)),
                (SwitchOf(N), "sw:deny", Sorted()),
            }));
        }

        [Test]
        public void A_rule_taken_off_under_a_ticked_box_that_is_then_cleared_records_what_arrives()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            var refusing = Put(store, SwitchOf(N), "sw:deny");
            var shown = Shown(store);

            var act = Act(store, new RuleChanges(
                new[] { TakeOff("pf", "RU", shown) },
                new SwitchChange(false, shown, Arriving(("pf", "RU"), ("pg", "R")), null)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "off:RU", Sorted(granting.Hex)),
                (SwitchOf(N), "sw:inherit pf=RU pg=R", Sorted(refusing.Hex)),
            }));
        }

        [Test]
        public void The_box_ticked_writes_a_refusing_version_naming_the_versions_shown_and_what_they_name()
        {
            var store = Store();
            var first = Put(store, SwitchOf(N), "sw:deny");
            var inheriting = Put(store, SwitchOf(N), "sw:inherit pf=R", first.Hex);

            var act = Act(store, Box(true, Shown(store)));

            Assert.That(Written(act), Is.EqualTo(new[] { (SwitchOf(N), "sw:deny", Sorted(first.Hex, inheriting.Hex)) }));
        }

        [Test]
        public void The_box_ticked_where_a_single_refusing_version_stands_writes_nothing()
        {
            var store = Store();
            Put(store, SwitchOf(N), "sw:deny");

            Assert.That(Act(store, Box(true, Shown(store))), Is.Null);
        }

        [Test]
        public void The_box_cleared_records_what_arrives_and_renews_each_rule_taken_off_that_records_other_letters()
        {
            var store = Store();
            var refusing = Put(store, SwitchOf(N), "sw:deny");
            var denied = Put(store, RowOf(N, "pf"), "off:deny");
            Put(store, RowOf(N, "pg"), "off:R");
            Put(store, RowOf(N, "ph"), "row:R");

            var act = Act(store, Box(false, Shown(store), ("pf", "RU"), ("pg", "R"), ("px", "C")));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (SwitchOf(N), "sw:inherit pf=RU pg=R px=C", Sorted(refusing.Hex)),
                (RowOf(N, "pf"), "off:RU", Sorted(denied.Hex)),
            }));
        }

        [Test]
        public void The_box_cleared_renews_off_deny_to_nothing_where_nothing_arrives()
        {
            var store = Store();
            Put(store, SwitchOf(N), "sw:deny");
            var denied = Put(store, RowOf(N, "pf"), "off:deny");

            var act = Act(store, Box(false, Shown(store)));

            Assert.That(Written(act).Where(version => version.Object == RowOf(N, "pf")), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:", Sorted(denied.Hex)) }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void The_box_cleared_where_the_store_gives_inherit_from_a_single_version_or_none_writes_nothing(bool inheriting)
        {
            var store = Store();
            if (inheriting)
            {
                Put(store, SwitchOf(N), "sw:inherit pf=R");
            }

            Put(store, RowOf(N, "pf"), "off:R");

            Assert.That(Act(store, Box(false, Shown(store), ("pf", "RU"))), Is.Null);
        }

        [Test]
        public void The_box_cleared_over_two_inheriting_versions_writes_a_single_version_naming_both()
        {
            var store = Store();
            var first = Put(store, SwitchOf(N), "sw:inherit pf=R");
            var second = Put(store, SwitchOf(N), "sw:inherit pf=RU");

            var act = Act(store, Box(false, Shown(store), ("pf", "RU")));

            Assert.That(Written(act), Is.EqualTo(new[] { (SwitchOf(N), "sw:inherit pf=RU", Sorted(first.Hex, second.Hex)) }));
        }

        [Test]
        public void The_box_cleared_and_a_rule_chosen_on_the_same_tab_write_the_row_once()
        {
            var store = Store();
            var refusing = Put(store, SwitchOf(N), "sw:deny");
            var denied = Put(store, RowOf(N, "pf"), "off:deny");
            var shown = Shown(store);

            var act = Act(store, new RuleChanges(new[] { Set("pf", "L", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")), null)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (SwitchOf(N), "sw:inherit pf=RU", Sorted(refusing.Hex)),
                (RowOf(N, "pf"), "row:L", Sorted(denied.Hex)),
            }));
        }

        [Test]
        public void The_box_cleared_and_a_rule_taken_off_on_the_same_tab_record_what_both_presses_showed_arriving()
        {
            var store = Store();
            var refusing = Put(store, SwitchOf(N), "sw:deny");
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            var shown = Shown(store);

            var act = Act(store, new RuleChanges(new[] { TakeOff("pf", "RUA", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")), null)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (SwitchOf(N), "sw:inherit pf=RU", Sorted(refusing.Hex)),
                (RowOf(N, "pf"), "off:RU", Sorted(granting.Hex)),
            }));
        }

        [Test]
        public void A_limit_settled_renews_each_version_shown_that_cuts_the_profile()
        {
            var store = Store();
            var takenOff = Put(store, RowOf(N, "pf"), "off:R");
            var inheriting = Put(store, SwitchOf(N), "sw:inherit pf=R pg=L");

            var act = Act(store, Rows(Settle("pf", "RU", Shown(store))));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "off:RU", Sorted(takenOff.Hex)),
                (SwitchOf(N), "sw:inherit pf=RU pg=L", Sorted(inheriting.Hex)),
            }));
        }

        [Test]
        public void A_limit_settled_where_the_switch_refuses_at_the_write_is_left_and_told()
        {
            var store = Store();
            Put(store, RowOf(N, "pf"), "off:R");
            var shown = Shown(store);

            // The switch turned to refuse reached the store while the dialog stood.
            Put(store, SwitchOf(N), "sw:deny");
            IReadOnlyList<RuleLeft> left;
            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Settle("pf", "RU", shown)), out left);
            }

            Assert.That(act, Is.Null);
            Assert.That(Told(left), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.NotAllowed) }));
        }

        [Test]
        public void A_rule_taken_off_under_a_box_the_tab_showed_ticked_is_off_deny_whatever_reached_the_store_under_the_dialog()
        {
            // Both copies hold the switch refusing and a rule for pf at N, with RU arriving. The
            // other copy clears the box; this copy's tab shows the box ticked, and the X on pf's
            // row sees the profile refused here. The other copy's switch reaches the store while
            // the dialog stands.
            var here = Store();
            Put(here, RowOf(Root, "pf"), "row:RU");
            Put(here, SwitchOf(N), "sw:deny");
            var granting = Put(here, RowOf(N, "pf"), "row:R");
            var there = here.Copy();
            Write(there, ActAt(there, N, Box(false, ShownAt(there, N), ("pf", "RU"))));
            var shown = Shown(here);

            here.Synchronize(there);
            Assume.That(ShownAt(here, N).Switch.Refuses, Is.False, "the box cleared in the other copy stands");
            var act = ActAt(here, N, Rows(TakeOff("pf", "RU", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:deny", Sorted(granting.Hex)) }));
            Write(here, act);
            Assert.That(Gives(here, N, "pf"), Is.Empty, "the profile gets nothing at N, as both tables gave");
        }

        [Test]
        public void A_rule_taken_off_once_the_box_was_cleared_on_the_tab_records_what_arrives_whatever_reached_the_store()
        {
            var here = Store();
            Put(here, RowOf(Root, "pf"), "row:RU");
            Put(here, SwitchOf(N), "sw:deny");
            var granting = Put(here, RowOf(N, "pf"), "row:R");
            var there = here.Copy();
            Write(there, ActAt(there, N, Box(false, ShownAt(there, N), ("pf", "RU"))));
            var shown = Shown(here);

            // The box cleared on this tab too, before the X: the operator saw the switch let
            // through what arrives. The store gives the box as cleared already, so the box writes
            // nothing.
            here.Synchronize(there);
            var act = ActAt(here, N, new RuleChanges(new[] { TakeOff("pf", "RU", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")), null)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:RU", Sorted(granting.Hex)) }));
        }

        [Test]
        public void The_box_cleared_writes_anew_the_rules_taken_off_of_a_profile_whose_own_press_wrote_nothing()
        {
            var store = Store();
            var refusing = Put(store, SwitchOf(N), "sw:deny");
            var denied = Put(store, RowOf(N, "pf"), "off:deny");
            Put(store, RowOf(N, "pf"), "row:R", Crowd(store, RowOf(N, "pf"), "row:R"));
            var shown = Shown(store);

            IReadOnlyList<RuleLeft> left;
            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(
                    reading, N, _ => Open(), null, new RuleChanges(new[] { Set("pf", "L", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")), null)), out left);
            }

            Assert.That(Told(left), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.TooLong) }), "the rule chosen does not fit");
            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (SwitchOf(N), "sw:inherit pf=RU", Sorted(refusing.Hex)),
                (RowOf(N, "pf"), "off:RU", Sorted(denied.Hex)),
            }));
        }

        [Test]
        public void Removing_a_lost_version_writes_each_version_left_again_naming_the_lost_version()
        {
            var store = Store();
            var narrow = Put(store, RowOf(N, "pf"), "row:R");
            var wide = Put(store, RowOf(N, "pf"), "row:RU");
            var lost = Lost(store, RowOf(N, "pf"), "row:L");
            var shown = Shown(store);
            Assume.That(shown.Row("pf").Lost, Is.EqualTo(new[] { lost.Hex }));

            var act = Act(store, Rows(Remove(shown, "pf", lost.Hex)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "row:R", Sorted(lost.Hex, narrow.Hex)),
                (RowOf(N, "pf"), "row:RU", Sorted(lost.Hex, wide.Hex)),
            }));
            Write(store, act);
            Assert.That(Shown(store).Row("pf").Refuses, Is.False, "the loss is answered for the row");
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:R", "row:RU" }));
        }

        [Test]
        public void Removing_a_version_beside_a_version_out_of_form_writes_each_readable_version_left_and_leaves_the_unreadable_standing()
        {
            var store = Store();
            var narrow = Put(store, RowOf(N, "pf"), "row:R");
            var wide = Put(store, RowOf(N, "pf"), "row:RU");
            var unreadable = Put(store, RowOf(N, "pf"), "row:UR");
            var shown = Shown(store);
            Assume.That(shown.Row("pf").Versions.Count(version => version.Kind == RowVersionKind.OutOfForm), Is.EqualTo(1));

            var act = Act(store, Rows(Remove(shown, "pf", narrow.Hex)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "row:RU", Sorted(narrow.Hex, wide.Hex)) }));
            Write(store, act);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:RU", "row:UR" }));

            var again = Act(store, Rows(Remove(Shown(store), "pf", unreadable.Hex)));
            Write(store, again);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:RU" }));
            Assert.That(Shown(store).Row("pf").Answers, Is.True, "the unreadable version removed, the row answers");
        }

        [Test]
        public void Removing_the_only_standing_version_beside_a_lost_version_is_left_and_told()
        {
            var store = Store();
            var only = Put(store, RowOf(N, "pf"), "row:R");
            Lost(store, RowOf(N, "pf"), "row:RU");
            var shown = Shown(store);

            IReadOnlyList<RuleLeft> left;
            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Remove(shown, "pf", only.Hex)), out left);
            }

            Assert.That(act, Is.Null);
            Assert.That(Told(left), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.NotAllowed) }));
        }
        [Test]
        public void A_version_removed_from_a_bound_group_writes_each_version_left_with_its_own_text()
        {
            var store = Store();
            var before = Put(store, RowOf(N, "pf"), "row:");
            var narrow = Put(store, RowOf(N, "pf"), "row:R", before.Hex);
            var wide = Put(store, RowOf(N, "pf"), "row:RU", before.Hex);
            var takenOff = Put(store, RowOf(N, "pf"), "off:RL");

            var act = Act(store, Rows(Remove(Shown(store), "pf", narrow.Hex)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "row:RU", Sorted(before.Hex, narrow.Hex, wide.Hex)),
                (RowOf(N, "pf"), "off:RL", Sorted(before.Hex, narrow.Hex, takenOff.Hex)),
            }));
            Write(store, act);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "off:RL", "row:RU" }));
        }

        [Test]
        public void A_version_removed_where_the_store_holds_only_the_versions_left_writes_nothing()
        {
            var store = Store();
            var narrow = Put(store, RowOf(N, "pf"), "row:R");
            var wide = Put(store, RowOf(N, "pf"), "row:RU");
            var shown = Shown(store);

            // A copy that removed the same version reached the store under the dialog.
            Put(store, RowOf(N, "pf"), "row:RU", narrow.Hex, wide.Hex);

            Assert.That(Act(store, Rows(Remove(shown, "pf", narrow.Hex))), Is.Null);
        }

        [Test]
        public void A_version_that_reached_the_store_under_the_dialog_is_named_by_nothing_and_stands_beside()
        {
            var store = Store();
            var shownVersion = Put(store, RowOf(N, "pf"), "row:R");
            var shown = Shown(store);
            var landed = Put(store, RowOf(N, "pf"), "row:RL", shownVersion.Hex);

            var act = Act(store, Rows(Set("pf", "RU", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "row:RU", Sorted(shownVersion.Hex)) }));
            Assert.That(Written(act).Single().After, Does.Not.Contain(landed.Hex));
            Write(store, act);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:RL", "row:RU" }));
        }

        [Test]
        public void A_switch_renewed_is_only_a_version_the_tab_showed_that_the_write_reads_standing()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            var shownSwitch = Put(store, SwitchOf(N), "sw:inherit pf=R");
            var shown = Shown(store);
            Put(store, SwitchOf(N), "sw:inherit pf=C", shownSwitch.Hex);
            Put(store, SwitchOf(N), "sw:inherit pf=A");

            var act = Act(store, Rows(TakeOff("pf", "RU", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:RU", Sorted(granting.Hex)) }));
        }

        [Test]
        public void A_rule_chosen_over_a_lost_version_names_the_lost_version_and_grants_again()
        {
            var store = Store();
            var standing = Put(store, RowOf(N, "pf"), "row:R");
            var lost = Lost(store, RowOf(N, "pf"), "row:RU");
            var shown = Shown(store);
            Assume.That(shown.Row("pf").Lost, Is.EqualTo(new[] { lost.Hex }));
            Assume.That(shown.Row("pf").Refuses, Is.True);

            var act = Act(store, Rows(Set("pf", "L", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "row:L", Sorted(standing.Hex, lost.Hex)) }));
            Write(store, act);
            var row = Shown(store).Row("pf");
            Assert.That(row.Refuses, Is.False);
            Assert.That(row.Answers, Is.True);
        }

        [Test]
        public void No_act_grants_a_letter_that_was_not_pressed()
        {
            // Every press but a rule chosen writes no version that grants, and a version left of a
            // bound group grants what it granted: no letter is ever granted that nobody pressed.
            foreach (var recorded in Alphabet)
            {
                foreach (var arriving in Alphabet)
                {
                    var store = Store();
                    var granting = Put(store, RowOf(N, "pf"), "row:" + recorded);
                    Put(store, RowOf(N, "pf"), "off:" + recorded);
                    Put(store, SwitchOf(N), "sw:deny");
                    var shown = Shown(store);

                    var changes = new[]
                    {
                        Rows(TakeOff("pf", arriving, shown)),
                        Rows(Settle("pf", arriving, shown)),
                        Rows(Remove(shown, "pf", granting.Hex)),
                        Box(false, shown, ("pf", arriving)),
                        new RuleChanges(new[] { TakeOff("pf", arriving, shown) }, new SwitchChange(false, shown, Arriving(("pf", arriving)), null)),
                    };

                    foreach (var act in changes.Select(change => Act(store, change)))
                    {
                        var granted = act == null ? Enumerable.Empty<string>() : Written(act).Select(version => version.Doc).Where(doc => doc.StartsWith("row:", StringComparison.Ordinal));
                        Assert.That(granted, Is.Empty, recorded + " under " + arriving);
                    }
                }
            }
        }

        [Test]
        public void No_act_records_a_mask_wider_than_the_letters_shown_arriving_nor_than_what_arrives_now()
        {
            foreach (var recorded in Alphabet)
            {
                foreach (var arriving in Alphabet)
                {
                    foreach (var atTheBox in Alphabet)
                    {
                        foreach (var now in new[] { "RUA", "RU", "A", string.Empty })
                        {
                            var store = Store();
                            Put(store, RowOf(N, "pf"), "off:" + recorded);
                            Put(store, RowOf(N, "pg"), "row:R");
                            Put(store, SwitchOf(N), "sw:deny");
                            Put(store, SwitchOf(N), "sw:inherit pf=" + (recorded.Length == 0 ? "C" : recorded));
                            var shown = Shown(store);
                            var where = recorded + " under " + arriving + ", the box shown " + atTheBox + ", arriving now " + now;

                            StoreAct act;
                            using (var reading = store.Read())
                            {
                                act = RuleActs.Of(reading, N, _ => Giving(("pf", now), ("pg", now)), null, new RuleChanges(
                                    new[] { TakeOff("pg", arriving, shown) },
                                    new SwitchChange(false, shown, Arriving(("pf", atTheBox), ("pg", atTheBox)), null)), out _);
                            }

                            // The bound by letters, each a letter of every mask the presses showed
                            // and of what arrives now.
                            var bound = new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                { "pf", Common(atTheBox, now) },
                                { "pg", Common(Common(arriving, atTheBox), now) },
                            };
                            foreach (var version in Written(act))
                            {
                                foreach (var profile in bound.Keys.Where(profile => version.Object == SwitchOf(N) || version.Object == RowOf(N, profile)))
                                {
                                    var letters = RecordedLetters(version.Doc, profile);
                                    Assert.That(letters == null || letters.All(bound[profile].Contains), Is.True, where + ": " + version.Doc + " for " + profile);
                                }
                            }
                        }
                    }
                }
            }
        }
        [Test]
        public void A_narrowing_above_that_reached_the_store_under_the_dialog_is_recorded_and_no_widening_made_apart_undoes_it()
        {
            // Copy X narrows the root and the narrowing reaches copy Y while Y's dialog at N
            // stands, its tab showing the wider mask arriving. Copy U, which holds the narrowing and
            // N's empty rule, widens the root again apart. Once Y and U meet, N gives what Y gave.
            var y = Store();
            Put(y, RowOf(Root, "pf"), "row:RU");
            var empty = Put(y, RowOf(N, "pf"), "row:");
            var x = y.Copy();
            Write(x, ActAt(x, Root, Rows(Set("pf", "R", ShownAt(x, Root)))));
            var u = x.Copy();
            var shown = Shown(y);
            Assume.That(Gives(y, Root, "pf"), Is.EqualTo("RU"), "the tab showed what arrived then");

            y.Synchronize(x);
            Assume.That(Gives(y, Root, "pf"), Is.EqualTo("R"), "the narrowing landed under the dialog");
            var act = ActAt(y, N, Rows(TakeOff("pf", "RU", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:R", Sorted(empty.Hex)) }));
            Write(y, act);
            Write(u, ActAt(u, Root, Rows(Set("pf", "RU", ShownAt(u, Root)))));
            Assume.That(Gives(u, N, "pf"), Is.Empty, "the copy that widened gave N nothing");

            y.Synchronize(u);
            Assert.That(Gives(y, Root, "pf"), Is.EqualTo("RU"), "the widening stands");
            Assert.That(Gives(y, N, "pf"), Is.EqualTo("R"), "N gives what the copy that took the rule off gave, and no more");
        }

        [Test]
        public void The_box_cleared_records_for_each_profile_no_more_than_arrives_now()
        {
            var store = Store();
            var refusing = Put(store, SwitchOf(N), "sw:deny");

            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(
                    reading, N, _ => Giving(("pf", "R"), ("pg", "RL")), null, Box(false, StoredRules.Of(reading).At(N), ("pf", "RU"), ("pg", "RL"), ("px", "C")), out _);
            }

            Assert.That(Written(act), Is.EqualTo(new[] { (SwitchOf(N), "sw:inherit pf=R pg=RL", Sorted(refusing.Hex)) }));
        }

        [Test]
        public void A_mask_not_narrowed_since_the_press_is_recorded_as_shown()
        {
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:RUA");
            Put(store, RowOf(Root, "pg"), "row:RL");
            var refusing = Put(store, SwitchOf(N), "sw:deny");
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            var shown = Shown(store);

            var act = ActAt(store, N, new RuleChanges(new[] { TakeOff("pf", "RUA", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RUA"), ("pg", "RL")), null)));

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (SwitchOf(N), "sw:inherit pf=RUA pg=RL", Sorted(refusing.Hex)),
                (RowOf(N, "pf"), "off:RUA", Sorted(granting.Hex)),
            }));
        }

        [Test]
        public void A_mask_cut_to_nothing_is_off_with_no_letter_told_apart_from_off_deny_and_lets_nothing_through()
        {
            var store = Store();
            Put(store, RowOf(Root, "pg"), "row:R");
            var granting = Put(store, RowOf(N, "pf"), "row:R");

            // Nothing arrives for pf now, whatever the tab showed at the press.
            var act = ActAt(store, N, Rows(TakeOff("pf", "RU", Shown(store))));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:", Sorted(granting.Hex)) }));
            Assert.That(RuleText.ReadRow("off:", out var none), Is.EqualTo(RowVersionKind.TakenOff));
            Assert.That(none.Count, Is.Zero);
            Assert.That(RuleText.ReadRow("off:deny", out _), Is.EqualTo(RowVersionKind.TakenOffRefusing));

            Write(store, act);
            Write(store, ActAt(store, Root, Rows(Set("pf", "RU", ShownAt(store, Root)))));
            Assert.That(Gives(store, N, "pf"), Is.Empty, "a rule taken off seeing nothing lets nothing through, until it is written anew");

            var denied = Store();
            Put(denied, RowOf(Root, "pf"), "row:RU");
            Put(denied, RowOf(N, "pf"), "off:deny");
            Assert.That(Gives(denied, N, "pf"), Is.Empty, "and neither does `off:deny`");
        }

        [Test]
        public void The_cut_walks_up_the_tree_once_for_each_profile_a_mask_is_recorded_for()
        {
            var profiles = Enumerable.Range(0, 30).Select(at => "p" + at.ToString("d2", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var store = Store();
            Put(store, SwitchOf(N), "sw:deny");
            var walks = 0;
            var above = TestNode.Root("root", () => walks++).Holding(Rules.Node(false, profiles.Select(profile => (profile, "RL")).ToArray()));
            for (var depth = 1; depth < 8; depth++)
            {
                above = above.Child("group " + depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(reading, N, _ => above, null, Box(false, StoredRules.Of(reading).At(N), profiles.Select(profile => (profile, "RU")).ToArray()), out _);
            }

            Assert.That(walks, Is.EqualTo(profiles.Length), "a walk for each profile recorded");
            Assert.That(Written(act).Single().Doc, Is.EqualTo("sw:inherit " + string.Join(" ", profiles.Select(profile => profile + "=R"))));
        }

        [Test]
        public void A_version_names_no_version_of_another_object_whatever_the_tab_hands_over()
        {
            var store = Store();
            var own = Put(store, RowOf(N, "pf"), "row:R");
            var otherProfile = Put(store, RowOf(N, "pg"), "row:R");
            var otherNode = Put(store, RowOf(M, "pf"), "row:R");
            var otherSwitch = Put(store, SwitchOf(N), "sw:inherit pf=R");

            // A rule shown for the row holding versions of other objects, as a defect in whatever
            // made the changes would hand them over.
            var shown = new NodeRule(new[]
            {
                new RuleObject("pf", new[]
                {
                    new RuleVersion(own.Hex, "row:R", null),
                    new RuleVersion(otherProfile.Hex, "row:R", null),
                    new RuleVersion(otherNode.Hex, "row:R", null),
                    new RuleVersion(otherSwitch.Hex, "row:R", new[] { otherNode.Hex }),
                }, null),
            });

            var act = Act(store, Rows(Set("pf", "RU", shown)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "row:RU", Sorted(own.Hex)) }));
        }

        [Test]
        public void A_rule_pressed_that_no_longer_fits_is_left_and_told_as_pressed_and_the_rest_is_written()
        {
            var store = Store();
            var crowded = Put(store, RowOf(N, "pf"), "row:R", Crowd(store, RowOf(N, "pf"), "row:R"));
            var shown = Shown(store);
            Assume.That(shown.Row("pf").Versions.Single().Id, Is.EqualTo(crowded.Hex));

            IReadOnlyList<RuleLeft> left;
            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Set("pf", "RU", shown), Set("pg", "R", shown)), out left);
            }

            Assert.That(Told(left), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.TooLong) }));
            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pg"), "row:R", Sorted()) }));
            var written = store.Write(new[] { act });
            Assert.That(written.Taken, Is.True, written.Reason);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:R" }));
        }

        [Test]
        public void A_renewal_that_no_longer_fits_is_left_and_told_as_a_renewal_and_the_rest_is_written()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            Put(store, SwitchOf(N), "sw:inherit pf=R", Crowd(store, SwitchOf(N), "sw:inherit pf=R"));

            IReadOnlyList<RuleLeft> left;
            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(reading, N, _ => Open(), null, Rows(TakeOff("pf", "RU", StoredRules.Of(reading).At(N))), out left);
            }

            Assert.That(Told(left), Is.EqualTo(new (string, bool, RuleLeftReason)[] { (null, false, RuleLeftReason.TooLong) }));
            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:RU", Sorted(granting.Hex)) }));
            var written = store.Write(new[] { act });
            Assert.That(written.Taken, Is.True, written.Reason);
        }

        [Test]
        public void The_box_that_no_longer_fits_is_left_and_every_other_version_follows_the_switch_as_it_stands()
        {
            var store = Store();
            var granting = Put(store, RowOf(N, "pf"), "row:R");
            Put(store, RowOf(N, "pg"), "off:deny");
            Put(store, SwitchOf(N), "sw:deny", Crowd(store, SwitchOf(N), "sw:deny"));
            var shown = Shown(store);

            IReadOnlyList<RuleLeft> left;
            StoreAct act;
            using (var reading = store.Read())
            {
                act = RuleActs.Of(
                    reading, N, _ => Open(), null, new RuleChanges(new[] { TakeOff("pf", "RU", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU"), ("pg", "R")), null)), out left);
            }

            // The box cleared is left, so the switch still refuses once the OK is written: the rule
            // taken off is `off:deny`, and no rule taken off is written anew for the box.
            Assert.That(Told(left), Is.EqualTo(new (string, bool, RuleLeftReason)[] { (null, true, RuleLeftReason.TooLong) }));
            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(N, "pf"), "off:deny", Sorted(granting.Hex)) }));
        }

        [Test]
        public void A_rule_taken_off_as_the_store_holds_it_writes_nothing()
        {
            var store = Store();
            Put(store, RowOf(N, "pf"), "off:deny");
            Put(store, SwitchOf(N), "sw:deny");

            Assert.That(Act(store, Rows(TakeOff("pf", "RU", Shown(store)))), Is.Null);
        }

        [TestCase("not a profile")]
        [TestCase("default")]
        [TestCase(null)]
        public void A_change_for_a_profile_name_the_issuer_could_not_issue_is_left_and_told(string profile)
        {
            var store = Store();

            Assert.That(Composed(store, Rows(Set(profile, "R", NodeRule.None)), out var left), Is.Null);
            Assert.That(Told(left), Is.EqualTo(new (string, bool, RuleLeftReason)[] { (profile, true, RuleLeftReason.OutOfForm) }));
        }

        [Test]
        public void A_rule_chosen_with_no_letters_given_is_left_and_told()
        {
            var store = Store();

            Assert.That(Composed(store, Rows(new RowChange(RowChangeKind.Set, "pf", null, null, NodeRule.None, null)), out var left), Is.Null);
            Assert.That(Told(left), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
        }

        [Test]
        public void Versions_removed_that_the_tab_did_not_show_or_that_leave_no_version_are_left_and_told()
        {
            var store = Store();
            var only = Put(store, RowOf(N, "pf"), "row:R");
            var shown = Shown(store);

            Assert.That(Composed(store, Rows(Remove(shown, "pf", new string('e', 32))), out var unknown), Is.Null);
            Assert.That(Told(unknown), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
            Assert.That(Composed(store, Rows(Remove(shown, "pf", only.Hex)), out var nothingLeft), Is.Null);
            Assert.That(Told(nothingLeft), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
        }

        [Test]
        public void A_limit_settled_that_the_tab_showed_refused_here_is_left_and_told()
        {
            var store = Store();
            Put(store, RowOf(N, "pf"), "off:R");
            Put(store, SwitchOf(N), "sw:deny");

            Assert.That(Composed(store, Rows(Settle("pf", "RU", Shown(store))), out var left), Is.Null);
            Assert.That(Told(left), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
        }

        [Test]
        public void A_change_out_of_form_is_left_and_told_and_takes_nothing_else_of_the_OK_with_it()
        {
            var store = Store();
            var shown = Shown(store);

            var act = Composed(
                store,
                new RuleChanges(
                    new[] { Set("pf", "R", shown), Set("not a profile", "R", shown), Remove(shown, "pg", new string('e', 32)) },
                    new SwitchChange(true, shown, null, null)),
                out var left);

            Assert.That(Written(act), Is.EquivalentTo(new[]
            {
                (SwitchOf(N), "sw:deny", Sorted()),
                (RowOf(N, "pf"), "row:R", Sorted()),
            }));
            Assert.That(Told(left), Is.EquivalentTo(new[] { ("not a profile", true, RuleLeftReason.OutOfForm), ("pg", true, RuleLeftReason.OutOfForm) }));
        }

        [TestCase("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
        [TestCase("bbbb")]
        [TestCase(null)]
        public void Every_change_at_a_node_out_of_form_is_left_and_told(string node)
        {
            var store = Store();
            using (var reading = store.Read())
            {
                Assert.That(
                    RuleActs.Of(reading, node, _ => Open(), null, new RuleChanges(new[] { Set("pf", "R", NodeRule.None) }, new SwitchChange(true, NodeRule.None, null, null)), out var left),
                    Is.Null);
                Assert.That(Told(left), Is.EquivalentTo(new (string, bool, RuleLeftReason)[] { ("pf", true, RuleLeftReason.OutOfForm), (null, true, RuleLeftReason.OutOfForm) }));
                Assert.That(left.Select(change => change.Node), Is.All.EqualTo(node), "each change is told at the node it was handed over at");
            }
        }

        [Test]
        public void Every_change_at_a_node_of_the_plugins_store_is_left_and_told()
        {
            var store = Store();
            using (var reading = store.Read())
            {
                var entry = reading.Candidates[0].Hex;
                var group = KeePassGate.Hex.Of(StoreFormat.NewGroupIdentifier());

                Assert.That(RuleActs.Of(reading, entry, _ => Open(), null, Rows(Set("pf", "R", NodeRule.None)), out var atEntry), Is.Null);
                Assert.That(Told(atEntry), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
                Assert.That(RuleActs.Of(reading, group, _ => Open(), null, Rows(Set("pf", "R", NodeRule.None)), out var atGroup), Is.Null);
                Assert.That(Told(atGroup), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
            }
        }

        [Test]
        public void Every_change_where_no_store_key_stands_or_the_store_cannot_be_read_as_a_whole_is_left_and_told()
        {
            using (var reading = new MemoryStore().Read())
            {
                Assert.That(RuleActs.Of(reading, N, _ => Open(), null, Rows(Set("pf", "R", NodeRule.None)), out var noKey), Is.Null);
                Assert.That(Told(noKey), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.Unread) }));
            }

            // A version no summary entry restates, as a program that keeps no summary entry writes
            // it: the store stops.
            var store = Store();
            var key = KeyOf(store);
            store.Put(StoreComposer.RuleVersion(key, RowOf(N, "pg"), "row:R"));
            Array.Clear(key, 0, key.Length);
            using (var reading = store.Read())
            {
                Assume.That(reading.State, Is.Not.EqualTo(StoreState.Whole));
                Assert.That(RuleActs.Of(reading, N, _ => Open(), null, Rows(Set("pf", "R", NodeRule.None)), out var stopped), Is.Null);
                Assert.That(Told(stopped), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.Unread) }));
            }
        }

        [Test]
        public void Nothing_pressed_is_no_act_and_nothing_told()
        {
            var store = Store();

            Assert.That(Composed(store, RuleChanges.None, out var left), Is.Null);
            Assert.That(left, Is.Empty);
        }
        [Test]
        public void An_act_is_composed_under_the_key_of_the_reading_it_is_composed_over()
        {
            var store = Store();
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, _ => Open(), null, Rows(Set("pf", "R", NodeRule.None)), out _);

                Assert.That(act.Against, Is.EqualTo(reading.InForce.Key.Name));
                Assert.That(act.KeyName, Is.EqualTo(reading.InForce.Key.Name));
            }
        }

        [Test]
        public void The_changes_hold_a_single_press_for_each_profile_and_no_letters_for_a_box_ticked()
        {
            Assert.That(
                () => new RuleChanges(new[] { Set("pf", "R", NodeRule.None), TakeOff("pf", "R", NodeRule.None) }, null),
                Throws.ArgumentException);

            var changes = new RuleChanges(new[] { Set("pg", "R", null), Set("pf", "U", null) }, new SwitchChange(true, null, Arriving(("pf", "R")), null));
            Assert.That(changes.Rows.Select(row => row.Profile), Is.EqualTo(new[] { "pf", "pg" }));
            Assert.That(changes.Rows.All(row => row.Shown == NodeRule.None), Is.True, "nothing shown is no rule shown");
            Assert.That(changes.Switch.Arriving, Is.Empty, "a press that ticks the box records nothing arriving");
            Assert.That(changes.IsEmpty, Is.False);
            Assert.That(RuleChanges.None.IsEmpty, Is.True);
        }

        [Test]
        public void The_seeds_write_what_an_OK_writes()
        {
            var store = Store();

            StoreSeeds.Seal(store, N, ("pf", "RU"));
            Assert.That(Standing(store, SwitchOf(N)), Is.EqualTo(new[] { "sw:deny" }));
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:RU" }));

            StoreSeeds.TakeOff(store, N, "pf", "R");
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "off:deny" }));

            StoreSeeds.Unseal(store, N, ("pf", "RL"));
            Assert.That(Standing(store, SwitchOf(N)), Is.EqualTo(new[] { "sw:inherit pf=RL" }));
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "off:RL" }));

            StoreSeeds.Rule(store, N, ("pf", "R"), ("pg", ".R......"));
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "row:R" }));
            Assert.That(Standing(store, RowOf(N, "pg")), Is.EqualTo(new[] { "row:R" }));
        }

        [Test]
        public void A_widening_above_renews_each_version_below_that_let_through_all_that_arrived_the_nearer_first()
        {
            // `STORE.md` §617, the write above: G follows the root, and N follows G as G lets it
            // through once renewed, so N is written with what arrives after both.
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            var group = Put(store, RowOf(G, "pf"), "off:R");
            var node = Put(store, RowOf(N, "pf"), "off:R");
            var tree = Three();

            var act = Ok(store, Root, tree, (shown, below) => Rows(Set("pf", "RU", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RU", Sorted(root.Hex)),
                (RowOf(N, "pf"), "off:RU", Sorted(node.Hex)),
                (RowOf(G, "pf"), "off:RU", Sorted(group.Hex)),
            }));
            Write(store, act);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "off:RU" }), "the version renewed replaces the version it names");
            Assert.That(new[] { G, N, M, E }.Select(at => GivesIn(store, tree, at, "pf")), Is.EqualTo(new[] { "RU", "RU", "RU", "RU" }));
        }

        [Test]
        public void No_renewal_widens_a_version_that_was_cutting_nor_records_more_than_arrives_after_the_act()
        {
            // Every mask of a small alphabet at the root before and after the act, and recorded at
            // G and at N below it, masks that hold each other and masks that do not: a version
            // renewed held all that arrived before, or recorded nothing where nothing arrived,
            // records exactly what arrives after in the write's reading, and such a version is
            // renewed wherever what arrives changed and it records otherwise. Whether a mask holds
            // another is asked of the letters themselves, so that no count of them can stand in.
            var masks = new[] { string.Empty, "R", "U", "RU", "RA", "UA" };
            var tree = Three();
            var start = Store();
            foreach (var before in masks)
            {
                foreach (var after in masks)
                {
                    foreach (var atG in masks)
                    {
                        foreach (var atN in masks)
                        {
                            var store = start.Copy();
                            Put(store, RowOf(Root, "pf"), "row:" + before);
                            Put(store, RowOf(G, "pf"), "off:" + atG);
                            Put(store, RowOf(N, "pf"), "off:" + atN);
                            var arrived = new Dictionary<string, string> { { G, GivesIn(store, tree, Root, "pf") }, { N, GivesIn(store, tree, G, "pf") } };
                            var recorded = new Dictionary<string, string> { { G, atG }, { N, atN } };
                            var where = "the root " + before + " to " + after + ", G off:" + atG + ", N off:" + atN;

                            var act = Ok(store, Root, tree, (shown, below) => Rows(Set("pf", after, shown, below)));
                            if (act == null)
                            {
                                Assert.That(before, Is.EqualTo(after), where + ": nothing written");
                                continue;
                            }

                            var renewed = Written(act).Where(version => version.Object != RowOf(Root, "pf")).ToList();
                            Write(store, act);
                            var arriving = new Dictionary<string, string> { { G, GivesIn(store, tree, Root, "pf") }, { N, GivesIn(store, tree, G, "pf") } };
                            foreach (var node in new[] { G, N })
                            {
                                var whole = arrived[node].Length == 0 ? recorded[node].Length == 0 : arrived[node].All(recorded[node].Contains);
                                var renewal = renewed.Where(version => version.Object == RowOf(node, "pf")).Select(version => version.Doc).ToList();
                                var follows = whole && arrived[node] != arriving[node] && recorded[node] != arriving[node];
                                Assert.That(renewal, Is.EqualTo(follows ? new[] { "off:" + arriving[node] } : Array.Empty<string>()), where + ": at " + node.Substring(0, 2));
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void A_loss_above_set_again_leaves_every_version_below_that_recorded_letters_as_it_was()
        {
            // `STORE.md` §617 and §639: G granted RL, N took its rule off recording R and M turned
            // its switch off recording R, each a limit made apart. G's row is then lost, so nothing
            // arrives below it, and G is set to RL again. Nothing tells whether a version that
            // recorded letters was cutting while nothing arrived, so N and M are left and go on
            // cutting, while E below N, which recorded nothing, follows what N lets through.
            var store = Store();
            var standing = Put(store, RowOf(G, "pf"), "row:RL");
            var lost = Lost(store, RowOf(G, "pf"), "row:RL");
            Put(store, RowOf(N, "pf"), "off:R");
            Put(store, SwitchOf(M), "sw:inherit pf=R");
            var follower = Put(store, RowOf(E, "pf"), "off:");
            var tree = Three();
            Assume.That(ShownAt(store, G).Row("pf").Refuses, Is.True, "G's row is lost");
            Assume.That(new[] { N, M, E }.Select(at => GivesIn(store, tree, at, "pf")), Is.EqualTo(new[] { string.Empty, string.Empty, string.Empty }));

            var act = Ok(store, G, tree, (shown, below) => Rows(Set("pf", "RL", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(G, "pf"), "row:RL", Sorted(standing.Hex, lost.Hex)),
                (RowOf(E, "pf"), "off:R", Sorted(follower.Hex)),
            }));
            Write(store, act);
            Assert.That(new[] { N, M, E }.Select(at => GivesIn(store, tree, at, "pf")), Is.EqualTo(new[] { "R", "R", "R" }));
        }

        [Test]
        public void The_box_ticked_and_cleared_above_leaves_a_limit_below_and_carries_the_version_that_followed()
        {
            // `STORE.md` §617: the root grants RL; under G, N took its rule off recording R, which
            // cuts, and M recording RL, which follows. G's box ticked renews M to nothing and
            // leaves N. G's box cleared then renews M, which recorded that nothing arrived, and
            // leaves N, whose letters nothing tells apart from a limit while nothing arrived.
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:RL");
            Put(store, RowOf(N, "pf"), "off:R");
            var follower = Put(store, RowOf(M, "pf"), "off:RL");
            var tree = Three();

            var ticked = Ok(store, G, tree, (shown, below) => new RuleChanges(null, new SwitchChange(true, shown, null, below)));
            Assert.That(
                Written(ticked).Where(version => version.Object != SwitchOf(G)),
                Is.EqualTo(new[] { (RowOf(M, "pf"), "off:", Sorted(follower.Hex)) }));
            Write(store, ticked);
            Assume.That(new[] { N, M }.Select(at => GivesIn(store, tree, at, "pf")), Is.EqualTo(new[] { string.Empty, string.Empty }));

            var cleared = Ok(store, G, tree, (shown, below) => new RuleChanges(null, new SwitchChange(false, shown, Arriving(("pf", "RL")), below)));
            Assert.That(
                Written(cleared).Where(version => version.Object != SwitchOf(G)).Select(version => (version.Object, version.Doc)),
                Is.EqualTo(new[] { (RowOf(M, "pf"), "off:RL") }));
            Write(store, cleared);
            Assert.That(GivesIn(store, tree, N, "pf"), Is.EqualTo("R"), "the limit stays");
            Assert.That(GivesIn(store, tree, M, "pf"), Is.EqualTo("RL"), "the version that followed follows");
        }

        [Test]
        public void A_rule_set_where_nothing_arrived_carries_a_version_below_that_recorded_nothing()
        {
            // `STORE.md` §617: no rule stands above N, so nothing arrived when its rule was taken
            // off and it recorded nothing; the rule set at the root reaches it.
            var store = Store();
            var node = Put(store, RowOf(N, "pf"), "off:");
            var tree = Three();

            var act = Ok(store, Root, tree, (shown, below) => Rows(Set("pf", "R", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:R", string.Empty),
                (RowOf(N, "pf"), "off:R", Sorted(node.Hex)),
            }));
            Write(store, act);
            Assert.That(GivesIn(store, tree, N, "pf"), Is.EqualTo("R"));
        }

        [Test]
        public void A_version_below_is_renewed_only_where_the_row_s_press_and_the_box_s_both_kept_it()
        {
            // `STORE.md` §617: the box was pressed before the reading that brought M's version, and
            // the row after it, so the row's press alone kept M's version. N, which both presses
            // kept, follows; M is left and told, where holding what either press kept would have
            // widened it.
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:R");
            var node = Put(store, RowOf(N, "pf"), "off:R");
            var tree = Three();
            var first = Tab(store, G, tree);
            var other = Put(store, RowOf(M, "pf"), "off:R");
            var second = Tab(store, G, tree);
            Assume.That(first.Below, Is.EquivalentTo(new[] { node.Hex }));
            Assume.That(second.Below, Is.EquivalentTo(new[] { node.Hex, other.Hex }));

            var changes = new RuleChanges(
                new[] { Set("pf", "RU", second.Shown, second.Below) },
                new SwitchChange(true, first.Shown, null, first.Below));
            var act = Above(store, G, tree, changes, out var left);

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(N, "pf"), "off:RU", Sorted(node.Hex)),
                (RowOf(G, "pf"), "row:RU", string.Empty),
                (SwitchOf(G), "sw:deny", string.Empty),
            }));
            Assert.That(
                left.Select(version => (version.Node, version.Profile, version.Pressed, version.Reason)),
                Is.EqualTo(new[] { (M, "pf", false, RuleLeftReason.NotHeld) }));
        }

        [Test]
        public void A_narrowing_above_is_carried_as_a_widening_is()
        {
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:RU");
            var group = Put(store, RowOf(G, "pf"), "off:RU");
            var node = Put(store, RowOf(N, "pf"), "off:RU");

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "R", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:R", Sorted(root.Hex)),
                (RowOf(N, "pf"), "off:R", Sorted(node.Hex)),
                (RowOf(G, "pf"), "off:R", Sorted(group.Hex)),
            }));
        }

        [Test]
        public void A_version_that_was_cutting_already_is_left_and_goes_on_cutting()
        {
            // N recorded L while R arrived: it was made apart, or the operator never settled it.
            // A widening above renews G, which followed, and never widens N.
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            var group = Put(store, RowOf(G, "pf"), "off:R");
            Put(store, RowOf(N, "pf"), "off:L");
            var tree = Three();

            var act = Ok(store, Root, tree, (shown, below) => Rows(Set("pf", "RUL", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RUL", Sorted(root.Hex)),
                (RowOf(G, "pf"), "off:RUL", Sorted(group.Hex)),
            }));
            Write(store, act);
            Assert.That(Standing(store, RowOf(N, "pf")), Is.EqualTo(new[] { "off:L" }));
            Assert.That(GivesIn(store, tree, N, "pf"), Is.EqualTo("L"), "cut to what N recorded, and no more");
            Assert.That(GivesIn(store, tree, M, "pf"), Is.EqualTo("RUL"));
        }

        [Test]
        public void A_node_held_back_above_passes_on_nothing_new_to_the_nodes_below_it()
        {
            // G was cutting and is left; what arrives at N after the act is what G lets through,
            // which the widening did not change, so N is not written either.
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(G, "pf"), "off:L");
            Put(store, RowOf(N, "pf"), "off:");

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "RU", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(Root, "pf"), "row:RU", Sorted(root.Hex)) }));
        }

        [TestCase("off:deny", "", "sw:inherit pg=R")]
        [TestCase("off:", "RU", "sw:inherit pf=RU pg=R")]
        public void Off_deny_below_is_left_and_off_with_nothing_arrived_follows(string recorded, string gives, string switchAfter)
        {
            // `STORE.md` §617: nothing arrived for pf, and N's switch was turned to inherit apart.
            // Off deny was let through by nobody and leaves every version of N for pf as it is, the
            // switch's among them; off with no letter was following what arrived, and follows.
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:");
            Put(store, RowOf(Root, "pg"), "row:R");
            Put(store, RowOf(N, "pf"), recorded);
            Put(store, SwitchOf(N), "sw:inherit pg=R");
            var tree = Three();

            var act = Ok(store, Root, tree, (shown, below) => Rows(Set("pf", "RU", shown, below)));
            Write(store, act);

            Assert.That(GivesIn(store, tree, N, "pf"), Is.EqualTo(gives));
            Assert.That(Standing(store, SwitchOf(N)), Is.EqualTo(new[] { switchAfter }));
            Assert.That(GivesIn(store, tree, N, "pg"), Is.EqualTo("R"), "the other profile is kept");
        }

        [Test]
        public void A_switch_turned_to_refuse_above_renews_the_versions_below_that_followed_holding_no_letter()
        {
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(Root, "pg"), "row:L");
            var taken = Put(store, RowOf(N, "pf"), "off:R");
            var other = Put(store, RowOf(N, "pg"), "off:L");
            var inheriting = Put(store, SwitchOf(N), "sw:inherit pf=R pg=L");
            Put(store, RowOf(M, "pf"), "row:R");

            var act = Ok(store, G, Three(), (shown, below) => new RuleChanges(null, new SwitchChange(true, shown, null, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(N, "pf"), "off:", Sorted(taken.Hex)),
                (RowOf(N, "pg"), "off:", Sorted(other.Hex)),
                (SwitchOf(N), "sw:inherit", Sorted(inheriting.Hex)),
                (SwitchOf(G), "sw:deny", string.Empty),
            }));
        }

        [Test]
        public void An_inheriting_version_below_is_renewed_with_that_profile_s_letters_changed_and_every_other_kept()
        {
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(Root, "pg"), "row:L");
            var first = Put(store, SwitchOf(N), "sw:inherit pf=C pg=L");
            var inheriting = Put(store, SwitchOf(N), "sw:inherit pf=R pg=L", first.Hex);

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "RU", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RU", Sorted(root.Hex)),
                (SwitchOf(N), "sw:inherit pf=RU pg=L", Sorted(inheriting.Hex, first.Hex)),
            }));
        }

        [Test]
        public void A_renewal_names_the_version_it_renews_and_what_that_version_names_and_no_version_beside_it()
        {
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:R");
            var earlier = Put(store, RowOf(N, "pf"), "off:R");
            var renewed = Put(store, RowOf(N, "pf"), "off:R", earlier.Hex);
            var apart = Put(store, RowOf(N, "pf"), "off:RUA");
            Assume.That(ShownAt(store, N).Row("pf").Versions.Select(version => version.Id), Is.EquivalentTo(new[] { renewed.Hex, apart.Hex }));

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "RU", shown, below)));

            Assert.That(Written(act).Where(version => version.Object == RowOf(N, "pf")), Is.EquivalentTo(new[]
            {
                (RowOf(N, "pf"), "off:RU", Sorted(renewed.Hex, earlier.Hex)),
                (RowOf(N, "pf"), "off:RU", Sorted(apart.Hex)),
            }));
        }

        [Test]
        public void A_renewal_is_only_for_a_profile_whose_arrival_the_act_changed()
        {
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(Root, "pg"), "row:R");
            Put(store, RowOf(N, "pf"), "off:R");
            Put(store, RowOf(N, "pg"), "off:R");
            Put(store, SwitchOf(N), "sw:inherit pf=R pg=R");

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "RU", shown, below)));

            Assert.That(Written(act).Select(version => (version.Object, version.Doc)), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RU"),
                (RowOf(N, "pf"), "off:RU"),
                (SwitchOf(N), "sw:inherit pf=RU pg=R"),
            }));
        }

        [Test]
        public void A_node_that_answers_or_refuses_by_itself_is_not_looked_at()
        {
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(N, "pf"), "row:R");
            Put(store, SwitchOf(N), "sw:inherit pf=R");
            Put(store, RowOf(M, "pf"), "off:R");
            Put(store, SwitchOf(M), "sw:deny");
            Put(store, SwitchOf(M), "sw:inherit pf=R");

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "RU", shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(Root, "pf"), "row:RU", Sorted(root.Hex)) }));
        }

        [Test]
        public void A_version_of_a_rule_in_several_versions_removed_above_is_an_act_above()
        {
            var store = Store();
            var narrow = Put(store, RowOf(Root, "pf"), "row:R");
            var wide = Put(store, RowOf(Root, "pf"), "row:RU");
            var node = Put(store, RowOf(N, "pf"), "off:R");

            var act = Ok(store, Root, Three(), (shown, below) =>
                Rows(new RowChange(RowChangeKind.RemoveVersions, "pf", null, new[] { narrow.Hex }, shown, below)));

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RU", Sorted(narrow.Hex, wide.Hex)),
                (RowOf(N, "pf"), "off:RU", Sorted(node.Hex)),
            }));
        }

        [TestCase("a version made apart, where none stood")]
        [TestCase("a version that grants, beside the version kept")]
        [TestCase("a version of the switch, beside the version kept")]
        [TestCase("a version replacing the version kept")]
        public void A_node_where_a_version_stands_that_the_tab_did_not_keep_is_left(string landed)
        {
            // `STORE.md` §617: only versions the tab's reading held at the press are renewed, and
            // where a version reached the node while the dialog stood, every version of that node
            // for the profile is left.
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            var group = Put(store, RowOf(G, "pf"), "off:R");
            StoreComposer.Composed kept = null;
            if (landed != "a version made apart, where none stood")
            {
                kept = Put(store, RowOf(N, "pf"), "off:R");
            }

            var tree = Three();
            var tab = Tab(store, Root, tree);
            Assume.That(tab.Below, Has.Member(group.Hex));
            switch (landed)
            {
                case "a version made apart, where none stood":
                    Put(store, RowOf(N, "pf"), "off:R");
                    break;
                case "a version that grants, beside the version kept":
                    Put(store, RowOf(N, "pf"), "row:RUL");
                    break;
                case "a version of the switch, beside the version kept":
                    Put(store, SwitchOf(N), "sw:inherit pf=RUL");
                    break;
                default:
                    Put(store, RowOf(N, "pf"), "off:R", kept.Hex);
                    break;
            }

            var act = Above(store, Root, tree, Rows(Set("pf", "RU", tab.Shown, tab.Below)), out var left);

            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RU", Sorted(root.Hex)),
                (RowOf(G, "pf"), "off:RU", Sorted(group.Hex)),
            }));
            Assert.That(
                left.Select(version => (version.Node, version.Profile, version.Pressed, version.Reason)),
                Is.EqualTo(new[] { (N, "pf", false, RuleLeftReason.NotHeld) }),
                "told at the node below, as no press, so that the log names why the change above did not reach it");
        }

        [Test]
        public void Before_is_the_write_s_own_reading_and_not_the_tab_s()
        {
            // `STORE.md` §617: G's rule was taken off in another copy, and the version landed under
            // the dialog at the root. N held all that arrived when the tab read it, and is cutting
            // by the OK, so the widening at the root does not carry it along.
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:RU");
            var granting = Put(store, RowOf(G, "pf"), "row:R");
            Put(store, RowOf(N, "pf"), "off:R");
            var tree = Three();
            var tab = Tab(store, Root, tree);

            Put(store, RowOf(G, "pf"), "off:RU", granting.Hex);
            var act = Above(store, Root, tree, Rows(Set("pf", "RUA", tab.Shown, tab.Below)), out _);

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(Root, "pf"), "row:RUA", Sorted(root.Hex)) }));
            Write(store, act);
            Assert.That(GivesIn(store, tree, N, "pf"), Is.EqualTo("R"));
        }

        [Test]
        public void A_node_not_below_the_dialog_s_node_in_the_write_s_tree_is_not_renewed()
        {
            // N was below G when the tab read, and is moved out before the OK; M was elsewhere and
            // is moved in. Neither is carried along: N is no longer below, and M's versions are not
            // what the tab kept.
            var store = Store();
            var group = Put(store, RowOf(G, "pf"), "row:R");
            Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(N, "pf"), "off:R");
            Put(store, RowOf(M, "pf"), "off:R");
            var tree = TestRuledTree.Of((Root, null), (G, Root), (N, G), (M, Root));
            var tab = Tab(store, G, tree);
            Assume.That(tab.Below, Has.Count.EqualTo(1), "N's version, and not M's");

            var moved = tree.Moved(N, Root).Moved(M, G);
            var act = Above(store, G, moved, Rows(Set("pf", "RU", tab.Shown, tab.Below)), out _);
            var unmoved = Above(store, G, tree, Rows(Set("pf", "RU", tab.Shown, tab.Below)), out _);

            Assert.That(Written(act), Is.EqualTo(new[] { (RowOf(G, "pf"), "row:RU", Sorted(group.Hex)) }));
            Assert.That(Written(unmoved).Select(version => version.Object), Is.EqualTo(new[] { RowOf(N, "pf"), RowOf(G, "pf") }), "where it was not moved, N follows");
        }

        [Test]
        public void The_result_does_not_depend_on_the_order_nodes_or_profiles_are_walked()
        {
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:R");
            Put(store, RowOf(Root, "pg"), "row:R");
            Put(store, RowOf(G, "pg"), "off:R");
            Put(store, SwitchOf(G), "sw:inherit pf=R pg=R");
            foreach (var node in new[] { N, M, E })
            {
                Put(store, RowOf(node, "pf"), "off:R");
                Put(store, RowOf(node, "pg"), "off:R");
            }

            Put(store, SwitchOf(E), "sw:inherit pf=R");
            var tree = Three();

            var walked = Written(Ok(store, Root, tree, (shown, below) => Rows(Set("pg", "RU", shown, below), Set("pf", "RU", shown, below))));
            var reversed = Written(Ok(store, Root, tree.Reversed(), (shown, below) => Rows(Set("pf", "RU", shown, below), Set("pg", "RU", shown, below))));

            Assert.That(reversed, Is.EqualTo(walked));
            Assert.That(
                walked.Select(version => version.Doc),
                Has.Exactly(6).StartsWith("off:RU"),
                "G for pg, N and M for both, and E for pf alone, since E's switch records nothing for pg");
        }

        [Test]
        public void A_renewal_that_no_longer_fits_is_left_told_as_a_renewal_and_cuts_below_it()
        {
            // `STORE.md` §643: N's version is left as it was and the rest of the OK is written; E
            // below it then receives what N still lets through, which the act did not change.
            var store = Store();
            var root = Put(store, RowOf(Root, "pf"), "row:R");
            var group = Put(store, RowOf(G, "pf"), "off:R");
            Put(store, RowOf(N, "pf"), "off:R", Crowd(store, RowOf(N, "pf"), "off:R"));
            Put(store, RowOf(E, "pf"), "off:R");
            var tree = Three();
            var tab = Tab(store, Root, tree);

            var act = Above(store, Root, tree, Rows(Set("pf", "RU", tab.Shown, tab.Below)), out var left);

            Assert.That(
                left.Select(version => (version.Node, version.Profile, version.Pressed, version.Reason)),
                Is.EqualTo(new[] { (N, "pf", false, RuleLeftReason.TooLong) }),
                "told at the node below, as a version written anew and not as a press");
            Assert.That(Written(act), Is.EqualTo(new[]
            {
                (RowOf(Root, "pf"), "row:RU", Sorted(root.Hex)),
                (RowOf(G, "pf"), "off:RU", Sorted(group.Hex)),
            }));
            Write(store, act);
            Assert.That(GivesIn(store, tree, E, "pf"), Is.EqualTo("R"));
        }

        [Test]
        public void An_OK_with_its_renewals_is_a_single_write_whose_summary_entry_holds_no_item_more()
        {
            // `STORE.md` §643: k nodes below that follow a row of the root write k + 1 versions and
            // k + 2 deletion records, the summary entry replaced among them, and an item each
            // replaced.
            var store = Store();
            Put(store, RowOf(Root, "pf"), "row:R");
            foreach (var node in new[] { G, N, M, E })
            {
                Put(store, RowOf(node, "pf"), "off:R");
            }

            int items;
            int deletions;
            using (var reading = store.Read())
            {
                items = reading.InForce.HeadsInForce.Single().Form.Items.Count;
                deletions = reading.Deletions.Count;
            }

            var act = Ok(store, Root, Three(), (shown, below) => Rows(Set("pf", "RU", shown, below)));
            Assert.That(act.Records, Has.Count.EqualTo(5));
            Write(store, act);

            using (var reading = store.Read())
            {
                Assert.That(reading.InForce.HeadsInForce, Has.Count.EqualTo(1));
                Assert.That(reading.InForce.HeadsInForce.Single().Form.Items, Has.Count.EqualTo(items));
                Assert.That(reading.Deletions.Count - deletions, Is.EqualTo(6));
            }
        }

        [Test]
        public void The_residual_of_two_rules_taken_off_apart_one_above_the_other_ends_as_measured()
        {
            // `STORE.md` §639, accepted: G's rule gave what arrived at G anyway, so taking it off in
            // a copy cut nothing and marked nothing; N's rule was taken off in the other. Once the
            // copies meet, a widening at the root carries G and N along, as the model does.
            var tree = Three();
            var first = Store();
            Put(first, RowOf(Root, "pf"), "row:R");
            Put(first, RowOf(Root, "pg"), "row:R");
            Write(first, Ok(first, G, tree, (shown, below) => Rows(Set("pf", "R", shown, below))));
            Write(first, Ok(first, N, tree, (shown, below) => Rows(Set("pf", string.Empty, shown, below))));
            var second = first.Copy();

            Write(first, Ok(first, N, tree, (shown, below) => Rows(TakeOff("pf", "R", shown, below))));
            Write(second, Ok(second, G, tree, (shown, below) => Rows(TakeOff("pf", "R", shown, below))));
            second.Synchronize(first);
            first.Synchronize(second);
            Assume.That(GivesIn(first, tree, N, "pf"), Is.EqualTo("R"), "no merge is beyond both copies");

            Write(first, Ok(first, Root, tree, (shown, below) => Rows(Set("pf", "RU", shown, below))));

            Assert.That(GivesIn(first, tree, N, "pf"), Is.EqualTo("RU"));
        }

        [Test]
        public void The_residual_of_a_narrowing_above_widened_again_apart_ends_as_measured()
        {
            // `STORE.md` §639, accepted: a narrowing above reaches the copy that took N's rule off
            // in a single direction, and the other copy widens the root again apart. N gives what it
            // gave when its rule was taken off, beyond what either copy gave just before they met.
            var tree = Three();
            var first = Store();
            Put(first, RowOf(Root, "pf"), "row:R");
            Put(first, RowOf(Root, "pg"), "row:R");
            Write(first, Ok(first, N, tree, (shown, below) => Rows(Set("pf", string.Empty, shown, below))));
            var second = first.Copy();

            Write(first, Ok(first, N, tree, (shown, below) => Rows(TakeOff("pf", "R", shown, below))));
            Write(second, Ok(second, Root, tree, (shown, below) => Rows(Set("pf", string.Empty, shown, below))));
            first.Synchronize(second);
            Assume.That(GivesIn(first, tree, N, "pf"), Is.Empty);

            Write(second, Ok(second, Root, tree, (shown, below) => Rows(Set("pf", "R", shown, below))));
            Assume.That(GivesIn(second, tree, N, "pf"), Is.Empty);
            first.Synchronize(second);

            Assert.That(GivesIn(first, tree, N, "pf"), Is.EqualTo("R"));
        }

        /// <summary>A store with an authority, under a key of its own.</summary>
        private static MemoryStore Store()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            return store;
        }

        /// <summary>
        /// A version of this object put into the store as another copy writes it: its record, and
        /// a summary entry restating it beside what the summary entries in force restate, naming
        /// them.
        /// </summary>
        private static StoreComposer.Composed Put(MemoryStore store, string objectName, string doc, params string[] after)
        {
            var key = KeyOf(store);
            try
            {
                var version = StoreComposer.RuleVersion(key, objectName, doc, after);
                var restating = StoreComposer.RuleVersionItem(version, objectName, doc, after);
                using (var reading = store.Read())
                {
                    var heads = reading.InForce.HeadsInForce;
                    var items = heads.SelectMany(head => head.Form.Items)
                        .Select(item => StoreComposer.Item(item.Members.Select(member => (member.Key, member.Value))
                            .Concat(new[] { ("kind", item.Kind) }).ToArray()))
                        .Concat(new[] { restating })
                        .Distinct()
                        .ToList();
                    var height = heads.Max(head => head.Form.Number) + 1;
                    var named = string.Join(" ", reading.InForce.After.OrderBy(hex => hex, StringComparer.Ordinal));
                    store.Put(version);
                    store.Put(StoreComposer.Record(
                        key,
                        "head",
                        "store",
                        StoreComposer.Content(("after", named), ("height", height.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                        height,
                        items));
                    foreach (var head in heads)
                    {
                        store.Drop(head.Hex);
                        store.Delete(StoreComposer.BytesOf(head.Hex));
                    }
                }

                return version;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// A version of this object that existed and is nowhere: a deletion record tells of it,
        /// and no record and no item carries it.
        /// </summary>
        private static StoreComposer.Composed Lost(MemoryStore store, string objectName, string doc)
        {
            var key = KeyOf(store);
            try
            {
                var version = StoreComposer.RuleVersion(key, objectName, doc);
                store.Delete(version.Identifier);
                return version;
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// Identifiers a version of this object could have, as many as fit a version of this text
        /// naming them, with a single identifier more not fitting.
        /// </summary>
        private static string[] Crowd(MemoryStore store, string objectName, string doc)
        {
            var key = KeyOf(store);
            try
            {
                var place = StoreComposer.Version(key, objectName, StoreComposer.Content(("doc", doc))).Hex.Substring(0, 20);
                var names = new List<string>();
                var count = (StoreFormat.MaxWritten / 33) - 20;
                for (var at = 0; at < count; at++)
                {
                    names.Add(place + at.ToString("x12", System.Globalization.CultureInfo.InvariantCulture));
                }

                // As many as the item that restates such a version holds within the ceiling.
                while (Item(key, objectName, doc, names.Concat(new[] { place + "ffffffffffff" })).Length <= StoreFormat.MaxWritten)
                {
                    names.Add(place + names.Count.ToString("x12", System.Globalization.CultureInfo.InvariantCulture));
                }

                Assume.That(Item(key, objectName, doc, names).Length, Is.LessThanOrEqualTo(StoreFormat.MaxWritten));
                return names.ToArray();
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private static string Item(byte[] key, string objectName, string doc, IEnumerable<string> after)
        {
            var version = StoreComposer.RuleVersion(key, objectName, doc, after.ToArray());
            return StoreComposer.RuleVersionItem(version, objectName, doc, after.ToArray());
        }

        private static byte[] KeyOf(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return (byte[])reading.InForce.Key.Bytes.Clone();
            }
        }

        /// <summary>The node's rules as the store holds them now, which is what a tab opened now shows.</summary>
        private static NodeRule Shown(IGateStore store)
        {
            using (var reading = store.Read())
            {
                return StoredRules.Of(reading).At(N);
            }
        }

        private static StoreAct Act(MemoryStore store, RuleChanges changes)
        {
            using (var reading = store.Read())
            {
                return RuleActs.Of(reading, N, _ => Open(), null, changes, out _);
            }
        }

        /// <summary>
        /// The act at a node of the tree the tests use, the root with N under it, over a reading of
        /// the store, the node's parent holding the rules of the same reading.
        /// </summary>
        private static StoreAct ActAt(MemoryStore store, string node, RuleChanges changes)
        {
            using (var reading = store.Read())
            {
                return RuleActs.Of(reading, node, node == Root ? null : (Func<StoredRules, IAclNode>)(_ => Tree(reading).Item1), null, changes, out _);
            }
        }

        /// <summary>The rules at a node as the store holds them now.</summary>
        private static NodeRule ShownAt(MemoryStore store, string node)
        {
            using (var reading = store.Read())
            {
                return StoredRules.Of(reading).At(node);
            }
        }

        /// <summary>What a node of the tree gives a profile, as the store holds the rules now.</summary>
        private static string Gives(MemoryStore store, string node, string profile)
        {
            using (var reading = store.Read())
            {
                var tree = Tree(reading);
                var grant = AclResolver.Arrives(node == Root ? tree.Item1 : tree.Item2, profile);
                return string.Concat(Enumerable.Range(0, VerbMask.Width).Where(at => grant.Holds(AclVerbs.Grantable[at])).Select(at => VerbMask.Letters[at]));
            }
        }

        /// <summary>The root and N under it, each holding the rules of this reading.</summary>
        private static (TestNode, TestNode) Tree(StoreReading reading)
        {
            var rules = StoredRules.Of(reading);
            var root = TestNode.Root("root").Holding(rules.At(Root));
            return (root, root.Child("N").Holding(rules.At(N)));
        }

        /// <summary>A parent letting every letter through for each profile the tests name.</summary>
        private static TestNode Open()
        {
            return Giving(new[] { "Zeta", "p-b", "pa", "pf", "pg", "ph", "px" }.Select(profile => (profile, "CRUSDLAP")).ToArray());
        }

        /// <summary>A parent giving each profile its mask, and nothing to any other.</summary>
        private static TestNode Giving(params (string Profile, string Mask)[] masks)
        {
            var given = masks.Where(mask => mask.Mask.Length > 0).ToArray();
            return given.Length == 0 ? TestNode.Root("above") : TestNode.Root("above").Holding(Rules.Node(false, given));
        }

        private static void Write(MemoryStore store, StoreAct act)
        {
            try
            {
                var written = store.Write(new[] { act });
                Assert.That(written.Taken, Is.True, written.Reason);
            }
            finally
            {
                act.Forget();
            }
        }

        /// <summary>The texts of the standing versions of an object, in ordinal order.</summary>
        private static string[] Standing(MemoryStore store, string objectName)
        {
            using (var reading = store.Read())
            {
                return reading.InForce.VersionsOf(objectName)
                    .Select(version => version.Members[StoreKinds.Doc])
                    .OrderBy(doc => doc, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        /// <summary>Each version an act writes: its object, its text and what it names, in ordinal order.</summary>
        private static List<(string Object, string Doc, string After)> Written(StoreAct act)
        {
            Assert.That(act, Is.Not.Null, "an act is composed");
            return act.Records
                .Select(record => (record.Member(StoreKinds.ObjectMember), record.Member(StoreKinds.Doc), record.Member(StoreKinds.After)))
                .OrderBy(version => version.Item1, StringComparer.Ordinal)
                .ThenBy(version => version.Item2, StringComparer.Ordinal)
                .ToList();
        }

        private static string Sorted(params string[] identifiers)
        {
            return string.Join(" ", identifiers.Distinct().OrderBy(identifier => identifier, StringComparer.Ordinal));
        }

        /// <summary>The act over the store as it stands, at N under a parent letting everything through, with what it told of.</summary>
        private static StoreAct Composed(MemoryStore store, RuleChanges changes, out IReadOnlyList<RuleLeft> left)
        {
            using (var reading = store.Read())
            {
                return RuleActs.Of(reading, N, _ => Open(), null, changes, out left);
            }
        }

        /// <summary>What each change told of holds: its profile, whether it was pressed, and why it was not written.</summary>
        private static IEnumerable<(string, bool, RuleLeftReason)> Told(IEnumerable<RuleLeft> left)
        {
            return left.Select(version => (version.Profile, version.Pressed, version.Reason)).ToList();
        }

        /// <summary>The letters of the first mask that the second holds too, read as text.</summary>
        private static string Common(string first, string second)
        {
            return new string(first.Where(second.Contains).ToArray());
        }

        /// <summary>
        /// The letters a text records for a profile, read as text: those after `off:` for a rule
        /// taken off, those of the profile's item for the switch inheriting, and null for any other
        /// text or a profile the switch's text does not name.
        /// </summary>
        private static string RecordedLetters(string doc, string profile)
        {
            if (doc.StartsWith("off:", StringComparison.Ordinal))
            {
                return doc == "off:deny" ? string.Empty : doc.Substring("off:".Length);
            }

            if (!doc.StartsWith("sw:inherit", StringComparison.Ordinal))
            {
                return null;
            }

            return doc.Split(' ').Skip(1)
                .Where(item => item.StartsWith(profile + "=", StringComparison.Ordinal))
                .Select(item => item.Substring(profile.Length + 1))
                .FirstOrDefault();
        }
        private static RuleChanges Rows(params RowChange[] rows)
        {
            return new RuleChanges(rows, null);
        }

        private static RuleChanges Box(bool refuses, NodeRule shown, params (string Profile, string Mask)[] arriving)
        {
            return new RuleChanges(null, new SwitchChange(refuses, shown, Arriving(arriving), null));
        }

        private static IEnumerable<KeyValuePair<string, AclGrant>> Arriving(params (string Profile, string Mask)[] arriving)
        {
            return arriving.Select(pair => new KeyValuePair<string, AclGrant>(pair.Profile, Rules.Letters(pair.Mask))).ToList();
        }

        private static RowChange Set(string profile, string mask, NodeRule shown, IEnumerable<string> below = null)
        {
            return new RowChange(RowChangeKind.Set, profile, Rules.Letters(mask), null, shown, below);
        }

        private static RowChange TakeOff(string profile, string arriving, NodeRule shown, IEnumerable<string> below = null)
        {
            return new RowChange(RowChangeKind.TakeOff, profile, Rules.Letters(arriving), null, shown, below);
        }

        private static RowChange Settle(string profile, string arriving, NodeRule shown, IEnumerable<string> below = null)
        {
            return new RowChange(RowChangeKind.Settle, profile, Rules.Letters(arriving), null, shown, below);
        }

        private static RowChange Remove(NodeRule shown, string profile, params string[] removed)
        {
            return new RowChange(RowChangeKind.RemoveVersions, profile, null, removed, shown, null);
        }

        /// <summary>
        /// The tree the writes above are carried down: G under the root, N and M under G, and E
        /// under N.
        /// </summary>
        private static TestRuledTree Three()
        {
            return TestRuledTree.Of((Root, null), (G, Root), (N, G), (M, G), (E, N));
        }

        /// <summary>The nodes of a tree, each holding the rules of a reading.</summary>
        private static Dictionary<string, TestNode> Nodes(StoreReading reading, IRuledTree tree, IEnumerable<string> known)
        {
            var rules = StoredRules.Of(reading);
            var nodes = new Dictionary<string, TestNode>(StringComparer.Ordinal);
            TestNode Placed(string node)
            {
                if (!nodes.TryGetValue(node, out var placed))
                {
                    var above = tree.Above(node);
                    placed = (above == null ? TestNode.Root(node.Substring(0, 2)) : Placed(above).Child(node.Substring(0, 2))).Holding(rules.At(node));
                    nodes[node] = placed;
                }

                return placed;
            }

            foreach (var node in known)
            {
                Placed(node);
            }

            return nodes;
        }

        /// <summary>
        /// What the KPG tab of a node's dialog keeps as it opens on the store as it stands now: the
        /// node's rules and the standing versions below it.
        /// </summary>
        private static (NodeRule Shown, IReadOnlyCollection<string> Below) Tab(MemoryStore store, string node, IRuledTree tree)
        {
            using (var reading = store.Read())
            {
                var rules = StoredRules.Of(reading);
                return (rules.At(node), rules.Below(node, tree));
            }
        }

        /// <summary>
        /// The act of an OK at a node of the tree, composed over a reading made now, its parent
        /// holding the rules of that reading and the write above carried down the tree.
        /// </summary>
        private static StoreAct Above(MemoryStore store, string node, IRuledTree tree, RuleChanges changes, out IReadOnlyList<RuleLeft> left)
        {
            using (var reading = store.Read())
            {
                var above = tree.Above(node);
                return RuleActs.Of(reading, node, above == null ? null : (Func<StoredRules, IAclNode>)(_ => Nodes(reading, tree, new[] { above })[above]), _ => tree, changes, out left);
            }
        }

        /// <summary>
        /// An OK at a node whose tab was opened on the store as it stands now, as the model composes
        /// every act at its write: the tab's reading is the write's own.
        /// </summary>
        private static StoreAct Ok(MemoryStore store, string node, IRuledTree tree, Func<NodeRule, IReadOnlyCollection<string>, RuleChanges> press)
        {
            var tab = Tab(store, node, tree);
            return Above(store, node, tree, press(tab.Shown, tab.Below), out _);
        }

        /// <summary>What a node of the tree gives a profile, as the store holds the rules now.</summary>
        private static string GivesIn(MemoryStore store, IRuledTree tree, string node, string profile)
        {
            using (var reading = store.Read())
            {
                var grant = AclResolver.Arrives(Nodes(reading, tree, new[] { node })[node], profile);
                return string.Concat(Enumerable.Range(0, VerbMask.Width).Where(at => grant.Holds(AclVerbs.Grantable[at])).Select(at => VerbMask.Letters[at]));
            }
        }

        private static string RowOf(string node, string profile)
        {
            return StoreComposer.RowObject(node, profile);
        }

        private static string SwitchOf(string node)
        {
            return StoreComposer.SwitchObject(node);
        }

        private static JObject Records()
        {
            return (JObject)RuleVectors.Records["records"];
        }

        private static byte[] MeasuredKey()
        {
            return StoreComposer.BytesOf((string)RuleVectors.Records["key"]);
        }

        /// <summary>A version of this object holding this text, with the nonce the vectors were printed with.</summary>
        private static StoreComposer.Composed Measured(string objectName, string doc)
        {
            return StoreComposer.Version(
                MeasuredKey(), objectName, StoreComposer.Content(("after", string.Empty), ("doc", doc), ("nonce", Nonce), ("object", objectName)));
        }

        /// <summary>
        /// A record an act composed, held to the measured record: the same members, the nonce
        /// aside, and composed again with the measured nonce, the same bytes.
        /// </summary>
        private static void AssertMeasured(StoreRecord record, string vector)
        {
            var measured = (JObject)Records()[vector];
            var members = record.Members.Where(member => member.Key != StoreKinds.Nonce)
                .ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            var expected = ((JObject)measured["members"]).Properties()
                .Where(member => member.Name != StoreKinds.Nonce)
                .ToDictionary(member => member.Name, member => (string)member.Value, StringComparer.Ordinal);

            Assert.That(record.Kind, Is.EqualTo((string)measured["kind"]), vector + ": the kind");
            Assert.That(members, Is.EquivalentTo(expected), vector + ": the members");

            members[StoreKinds.Nonce] = (string)measured["members"][StoreKinds.Nonce];
            var again = StoreRecord.Compose(MeasuredKey(), record.Kind, members);
            Assert.That(again.Content, Is.EqualTo((string)measured["content"]), vector + ": the content");
            Assert.That(again.Check, Is.EqualTo((string)measured["check"]), vector + ": the check");
            Assert.That(again.Hex, Is.EqualTo((string)measured["id"]), vector + ": the identifier");
            Assert.That(
                again.Strings,
                Is.EquivalentTo(((JObject)measured["strings"]).Properties().ToDictionary(member => member.Name, member => (string)member.Value)),
                vector + ": the strings");
        }
    }
}
