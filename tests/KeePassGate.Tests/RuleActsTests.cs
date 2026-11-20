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
        private const string Nonce = "00112233445566778899aabbccddeeff";

        private static readonly string[] Alphabet = { string.Empty, "R", "U", "A", "RU", "RA", "UA", "RUA" };

        [TestCase("version, a row that grants", "pf", "RU")]
        [TestCase("version, an empty rule", "pg", "")]
        public void A_rule_chosen_is_the_measured_record(string vector, string profile, string mask)
        {
            var store = new ComposedStore(MeasuredKey());
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, Open(), Rows(Set(profile, mask, NodeRule.None)), out var left);

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
                var act = RuleActs.Of(reading, N, Open(), Rows(TakeOff("pf", arriving, NodeRule.None)), out _);

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

                var act = RuleActs.Of(reading, N, Open(), Rows(TakeOff("pf", "RU", shown)), out _);

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

                var act = RuleActs.Of(reading, N, Open(), Rows(Set("pf", "R", shown)), out _);

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
                var act = RuleActs.Of(reading, N, Open(), Rows(Settle("pf", "CRUDLA", StoredRules.Of(reading).At(N))), out _);

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
                var act = RuleActs.Of(reading, N, Open(), Rows(Settle("pf", "CRUDLA", StoredRules.Of(reading).At(N))), out _);

                AssertMeasured(act.Records.Single(), "version, the switch renewed by a write above");
            }
        }

        [Test]
        public void The_box_ticked_is_the_measured_record()
        {
            var store = new ComposedStore(MeasuredKey());
            using (var reading = store.Read())
            {
                var act = RuleActs.Of(reading, N, Open(), Box(true, NodeRule.None), out _);

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
                var act = RuleActs.Of(reading, N, Open(), Box(false, NodeRule.None, pairs), out _);

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

            var act = Act(store, new RuleChanges(new[] { TakeOff("pf", "RU", shown) }, new SwitchChange(true, shown, null)));

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
                new SwitchChange(false, shown, Arriving(("pf", "RU"), ("pg", "R")))));

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

            var act = Act(store, new RuleChanges(new[] { Set("pf", "L", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")))));

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

            var act = Act(store, new RuleChanges(new[] { TakeOff("pf", "RUA", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")))));

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
                act = RuleActs.Of(reading, N, Open(), Rows(Settle("pf", "RU", shown)), out left);
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
            var act = ActAt(here, N, new RuleChanges(new[] { TakeOff("pf", "RU", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")))));

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
                    reading, N, Open(), new RuleChanges(new[] { Set("pf", "L", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU")))), out left);
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
                act = RuleActs.Of(reading, N, Open(), Rows(Remove(shown, "pf", only.Hex)), out left);
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
                        new RuleChanges(new[] { TakeOff("pf", arriving, shown) }, new SwitchChange(false, shown, Arriving(("pf", arriving)))),
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
                                act = RuleActs.Of(reading, N, Giving(("pf", now), ("pg", now)), new RuleChanges(
                                    new[] { TakeOff("pg", arriving, shown) },
                                    new SwitchChange(false, shown, Arriving(("pf", atTheBox), ("pg", atTheBox)))), out _);
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
                    reading, N, Giving(("pf", "R"), ("pg", "RL")), Box(false, StoredRules.Of(reading).At(N), ("pf", "RU"), ("pg", "RL"), ("px", "C")), out _);
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

            var act = ActAt(store, N, new RuleChanges(new[] { TakeOff("pf", "RUA", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RUA"), ("pg", "RL")))));

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
                act = RuleActs.Of(reading, N, above, Box(false, StoredRules.Of(reading).At(N), profiles.Select(profile => (profile, "RU")).ToArray()), out _);
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
                act = RuleActs.Of(reading, N, Open(), Rows(Set("pf", "RU", shown), Set("pg", "R", shown)), out left);
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
                act = RuleActs.Of(reading, N, Open(), Rows(TakeOff("pf", "RU", StoredRules.Of(reading).At(N))), out left);
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
                    reading, N, Open(), new RuleChanges(new[] { TakeOff("pf", "RU", shown) }, new SwitchChange(false, shown, Arriving(("pf", "RU"), ("pg", "R")))), out left);
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

            Assert.That(Composed(store, Rows(new RowChange(RowChangeKind.Set, "pf", null, null, NodeRule.None)), out var left), Is.Null);
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
                    new SwitchChange(true, shown, null)),
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
                Assert.That(RuleActs.Of(reading, node, Open(), new RuleChanges(new[] { Set("pf", "R", NodeRule.None) }, new SwitchChange(true, NodeRule.None, null)), out var left), Is.Null);
                Assert.That(Told(left), Is.EquivalentTo(new (string, bool, RuleLeftReason)[] { ("pf", true, RuleLeftReason.OutOfForm), (null, true, RuleLeftReason.OutOfForm) }));
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

                Assert.That(RuleActs.Of(reading, entry, Open(), Rows(Set("pf", "R", NodeRule.None)), out var atEntry), Is.Null);
                Assert.That(Told(atEntry), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
                Assert.That(RuleActs.Of(reading, group, Open(), Rows(Set("pf", "R", NodeRule.None)), out var atGroup), Is.Null);
                Assert.That(Told(atGroup), Is.EqualTo(new[] { ("pf", true, RuleLeftReason.OutOfForm) }));
            }
        }

        [Test]
        public void Every_change_where_no_store_key_stands_or_the_store_cannot_be_read_as_a_whole_is_left_and_told()
        {
            using (var reading = new MemoryStore().Read())
            {
                Assert.That(RuleActs.Of(reading, N, Open(), Rows(Set("pf", "R", NodeRule.None)), out var noKey), Is.Null);
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
                Assert.That(RuleActs.Of(reading, N, Open(), Rows(Set("pf", "R", NodeRule.None)), out var stopped), Is.Null);
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
                var act = RuleActs.Of(reading, N, Open(), Rows(Set("pf", "R", NodeRule.None)), out _);

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

            var changes = new RuleChanges(new[] { Set("pg", "R", null), Set("pf", "U", null) }, new SwitchChange(true, null, Arriving(("pf", "R"))));
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
                return RuleActs.Of(reading, N, Open(), changes, out _);
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
                return RuleActs.Of(reading, node, node == Root ? null : Tree(reading).Item1, changes, out _);
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
                return RuleActs.Of(reading, N, Open(), changes, out left);
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
            return new RuleChanges(null, new SwitchChange(refuses, shown, Arriving(arriving)));
        }

        private static IEnumerable<KeyValuePair<string, AclGrant>> Arriving(params (string Profile, string Mask)[] arriving)
        {
            return arriving.Select(pair => new KeyValuePair<string, AclGrant>(pair.Profile, Rules.Letters(pair.Mask))).ToList();
        }

        private static RowChange Set(string profile, string mask, NodeRule shown)
        {
            return new RowChange(RowChangeKind.Set, profile, Rules.Letters(mask), null, shown);
        }

        private static RowChange TakeOff(string profile, string arriving, NodeRule shown)
        {
            return new RowChange(RowChangeKind.TakeOff, profile, Rules.Letters(arriving), null, shown);
        }

        private static RowChange Settle(string profile, string arriving, NodeRule shown)
        {
            return new RowChange(RowChangeKind.Settle, profile, Rules.Letters(arriving), null, shown);
        }

        private static RowChange Remove(NodeRule shown, string profile, params string[] removed)
        {
            return new RowChange(RowChangeKind.RemoveVersions, profile, null, removed, shown);
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
