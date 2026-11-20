using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

// cspell:ignore CRUDLA CRUL CRUA
namespace KeePassGate.Tests
{
    /// <summary>
    /// What the rules answer, on a tree that is not a database.
    /// </summary>
    /// <remarks>
    /// Every tree here is invented, and so is every profile name on it. A rule names profiles and
    /// nothing else, so there is nothing in these that could come from anybody's real database
    /// even by accident.
    ///
    /// The walk of `STORE.md` §617 is held here a step at a time, each alone, and then over every
    /// pair and every triple of versions of a small alphabet: whatever versions stand, the answer
    /// is what each of them alone allows, by letters and by the questions the gate asks, and never
    /// more.
    /// </remarks>
    [TestFixture]
    public sealed class AclResolverTests
    {
        private static readonly string[] Reader = { "reader" };

        [Test]
        public void A_rule_further_up_reaches_down()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var entry = root.Child("shared").Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_rule_grants_only_the_verbs_it_names()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Update), Is.False);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Create), Is.False);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Delete), Is.False);
        }

        [Test]
        public void The_nearest_rule_wins_even_when_it_grants_less()
        {
            // The point of nearest wins, and the reason there is no deny syntax: a rule that says
            // less takes away what a rule above it gave, by being closer.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read, AclVerb.Update);
            var group = root.Child("read only").Granting("reader", AclVerb.Read);
            var entry = group.Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Update), Is.False);
        }

        [Test]
        public void The_nearest_rule_wins_even_when_it_grants_more()
        {
            // A row that grants answers with its own letters whatever arrives from above: its
            // writer saw what it gives at that node, so nothing it holds is cut.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("open").Holding(Rules.Stored(("reader", "off:R")));
            var entry = group.Child("an entry").Granting("reader", AclVerb.Read, AclVerb.Update);

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Update), Is.True);
        }

        [Test]
        public void An_empty_rule_takes_everything_away_from_there_down()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("private").Holding(Rules.Node(false, ("reader", "")));

            Assert.That(AclResolver.Permits(root.Child("shared"), Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(group.Child("an entry"), Reader, AclVerb.Read), Is.False);
        }

        [Test]
        public void A_rule_about_another_profile_is_not_a_rule_about_this_one()
        {
            // Silence, so the walk carries on past it rather than stopping. A node that named any
            // profile stopping the walk for all of them would make one rule answer for another.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var entry = root.Child("group").Granting("editor", AclVerb.Update).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_switch_that_refuses_stops_the_walk_for_a_profile_it_does_not_name()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var sealedGroup = root.Child("sealed").Sealing("editor", AclVerb.Update);

            Assert.That(AclResolver.Permits(sealedGroup.Child("an entry"), Reader, AclVerb.Read), Is.False,
                "the group above granted it and this group refuses anything it does not name");
            Assert.That(AclResolver.Permits(sealedGroup.Child("an entry"), new[] { "editor" }, AclVerb.Update), Is.True,
                "and it still grants what it does name");
        }

        [Test]
        public void Holding_either_of_two_profiles_is_enough()
        {
            // Combined by or, which is the only combination that does not depend on the order the
            // profiles happen to be in.
            var entry = TestNode.Root("root").Granting("editor", AclVerb.Update).Child("an entry");

            Assert.That(AclResolver.Permits(entry, new[] { "reader", "editor" }, AclVerb.Update), Is.True);
            Assert.That(AclResolver.Permits(entry, new[] { "editor", "reader" }, AclVerb.Update), Is.True);
        }

        [Test]
        public void One_profile_being_stopped_does_not_stop_another()
        {
            // Each profile is walked on its own. Resolving them together would let the nearest rule
            // about one of them decide for all of them, and a client would lose a permission by
            // holding an extra profile.
            var root = TestNode.Root("root").Granting("editor", AclVerb.Update);
            var group = root.Child("group").Granting("reader");

            Assert.That(AclResolver.Permits(group.Child("an entry"), new[] { "reader" }, AclVerb.Update), Is.False);
            Assert.That(AclResolver.Permits(group.Child("an entry"), new[] { "editor" }, AclVerb.Update), Is.True);
            Assert.That(AclResolver.Permits(group.Child("an entry"), new[] { "reader", "editor" }, AclVerb.Update), Is.True);
        }

        [Test]
        public void Reading_into_a_node_counts_a_switch_only_in_a_rule_holding_read()
        {
            // What decides what a tag covers, which is what its caller may read. A switch reaches
            // as far as the verbs of its own rule, so `read` in one rule and a switch in another
            // read no further in, while two rules each holding `read` with a switch open both
            // parts, as two reads through them would.
            var entry = TestNode.Root("root")
                .Holding(Rules.Node(false, ("files", "RA"), ("secrets", "RP"), ("plain", "R"), ("writer", "UAP")))
                .Child("an entry");

            Func<string[], AclReach, bool> reads = (profiles, reach) =>
                AclResolver.Permits(entry, profiles, AclVerb.Read, reach);

            Assert.That(reads(new[] { "files" }, AclReach.Attachments), Is.True);
            Assert.That(reads(new[] { "files" }, AclReach.Protected), Is.False);
            Assert.That(reads(new[] { "plain", "writer" }, AclReach.Fields), Is.True);
            Assert.That(reads(new[] { "plain", "writer" }, AclReach.Attachments), Is.False,
                "a switch beside `update` in another rule opens nothing to `read`");
            Assert.That(reads(new[] { "plain", "writer" }, AclReach.Protected), Is.False);
            Assert.That(
                reads(new[] { "files", "secrets" }, AclReach.Attachments)
                && reads(new[] { "files", "secrets" }, AclReach.Protected),
                Is.True,
                "each part through the rule that reads it");
            Assert.That(reads(new[] { "writer" }, AclReach.Fields), Is.False, "`update` is not `read`");
            Assert.That(reads(new[] { "nobody" }, AclReach.Fields), Is.False);
            Assert.That(reads(null, AclReach.Fields), Is.False);
            Assert.That(AclResolver.Permits(null, new[] { "files" }, AclVerb.Read), Is.False);

            var listed = TestNode.Root("root").Granting("lister", AclVerb.List, AclVerb.Attachments).Child("an entry");
            Assert.That(AclResolver.Permits(listed, new[] { "lister" }, AclVerb.Read), Is.False, "`list` is not `read`");
            Assert.That(AclResolver.Permits(listed, new[] { "lister" }, AclVerb.List, AclReach.Attachments), Is.True);
        }

        [Test]
        public void Reaching_a_protected_value_needs_the_verb_and_the_switch_together()
        {
            var plain = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var secret = TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.Protected).Child("an entry");

            Assert.That(AclResolver.Permits(plain, Reader, AclVerb.Read, AclReach.Fields), Is.True);
            Assert.That(AclResolver.Permits(plain, Reader, AclVerb.Read, AclReach.Protected), Is.False,
                "the rule reaches the entry and not the values the database protects");

            Assert.That(AclResolver.Permits(secret, Reader, AclVerb.Read, AclReach.Protected), Is.True);
        }

        [Test]
        public void The_secrets_switch_covers_whichever_verbs_the_rule_holds()
        {
            // What the letter means: not "may read secrets" but "and the protected values too",
            // for whichever of the six this rule holds. An agent that may correct a URL does not
            // thereby get to overwrite a password.
            var editor = new[] { "editor" };
            var entry = TestNode.Root("root")
                .Granting("editor", AclVerb.Update).Child("an entry");
            var trusted = TestNode.Root("root")
                .Granting("editor", AclVerb.Update, AclVerb.Protected).Child("an entry");

            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Update, AclReach.Fields), Is.True);
            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Update, AclReach.Protected), Is.False);
            Assert.That(AclResolver.Permits(trusted, editor, AclVerb.Update, AclReach.Protected), Is.True);
        }

        [Test]
        public void A_rule_that_reaches_protected_values_and_grants_nothing_grants_nothing()
        {
            var node = TestNode.Root("root").Granting("reader", AclVerb.Protected).Child("an entry");

            Assert.That(AclResolver.Permits(node, Reader, AclVerb.Read, AclReach.Fields), Is.False);
            Assert.That(AclResolver.Permits(node, Reader, AclVerb.Read, AclReach.Protected), Is.False,
                "a scope over no verbs is a scope over nothing");
        }

        [Test]
        public void Reaching_an_attachment_needs_the_verb_and_the_switch_together()
        {
            var fields = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var inside = TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.Attachments).Child("an entry");

            Assert.That(AclResolver.Permits(fields, Reader, AclVerb.Read, AclReach.Fields), Is.True);
            Assert.That(AclResolver.Permits(fields, Reader, AclVerb.Read, AclReach.Attachments), Is.False,
                "the rule reaches the entry and not what is inside it");

            Assert.That(AclResolver.Permits(inside, Reader, AclVerb.Read, AclReach.Attachments), Is.True);
        }

        [Test]
        public void Removing_needs_no_switch_because_removing_shows_nobody_anything()
        {
            // A rule with `delete` already takes the whole entry, attachments and all. Asking for
            // the switch before one of them can be removed on its own forbids the smaller act and
            // allows the larger.
            var entry = TestNode.Root("root")
                .Granting("editor", AclVerb.Read, AclVerb.Delete).Child("an entry");

            var editor = new[] { "editor" };

            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Delete, AclReach.Attachments), Is.True);

            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Read, AclReach.Attachments), Is.False,
                "and the switch still answers for everything that shows somebody something");
        }

        [Test]
        public void Stripping_needs_no_switch_either_and_needs_no_delete_beside_it()
        {
            // `strip` reaches the parts inside a node, its attachments among them, so a rule
            // holding it and neither switch removes an attachment and a protected value alike. It
            // does so without `delete`, because a grant is a set rather than a ladder.
            var entry = TestNode.Root("root")
                .Granting("tidier", AclVerb.Read, AclVerb.Update, AclVerb.Strip).Child("an entry");

            var tidier = new[] { "tidier" };

            Assert.That(AclResolver.Permits(entry, tidier, AclVerb.Strip, AclReach.Attachments), Is.True);
            Assert.That(AclResolver.Permits(entry, tidier, AclVerb.Strip, AclReach.Protected), Is.True);

            Assert.That(AclResolver.Permits(entry, tidier, AclVerb.Delete, AclReach.Attachments), Is.False,
                "and holding the smaller removal is not holding the larger one");
            Assert.That(AclResolver.Permits(entry, tidier, AclVerb.Read, AclReach.Attachments), Is.False,
                "while everything that shows somebody something still answers to the switch");
        }

        [Test]
        public void The_switch_reaches_every_verb_the_same_rule_grants()
        {
            // What the letter means: not "may read attachments" but "and the attachments too", for
            // whichever of the six this rule holds.
            var entry = TestNode.Root("root")
                .Granting("editor", AclVerb.Read, AclVerb.Update, AclVerb.Delete, AclVerb.Attachments)
                .Child("an entry");

            var editor = new[] { "editor" };

            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Update, AclReach.Attachments), Is.True);
            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Delete, AclReach.Attachments), Is.True);
            Assert.That(AclResolver.Permits(entry, editor, AclVerb.Create, AclReach.Attachments), Is.False,
                "and only those, because the rule does not grant create at all");
        }

        [Test]
        public void A_nearer_rule_without_the_switch_refuses_rather_than_deferring_upwards()
        {
            // The scope is answered by the same rule that answers the verb. Walking on to find a
            // rule above that has the switch would let a group take back a verb and keep handing
            // out what it reaches.
            var root = TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.Attachments);
            var entry = root.Child("group").Granting("reader", AclVerb.Read).Child("an entry");

            var decision = AclResolver.Decide(entry, Reader, AclVerb.Read, AclReach.Attachments);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.DecidedAt, Is.EqualTo("group"));
            Assert.That(decision.Reason, Does.Contain("attachments"));
        }

        [Test]
        public void The_switch_is_not_something_to_ask_about()
        {
            // A switch names how far a rule reaches, so "may this profile attachments" is not a
            // question, and neither is the same of protected values.
            var entry = TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.Attachments, AclVerb.Protected).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Attachments), Is.False);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Protected), Is.False);
        }

        [Test]
        public void A_tree_with_no_rules_on_it_grants_nothing()
        {
            var entry = TestNode.Root("root").Child("group").Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.False);
            Assert.That(AclResolver.Decide(entry, Reader, AclVerb.Read).DecidedAt, Is.Null);
            Assert.That(AclResolver.Decide(entry, Reader, AclVerb.Read).Reason, Is.EqualTo("no rule anywhere grants it"));
        }

        [Test]
        public void There_is_nothing_to_decide_about_nothing()
        {
            Assert.That(AclResolver.Permits(null, Reader, AclVerb.Read), Is.False);
            Assert.That(AclResolver.Arrives(null, "reader").Count, Is.Zero);
        }

        [Test]
        public void A_client_holding_no_profiles_is_permitted_nothing()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(AclResolver.Permits(entry, null, AclVerb.Read), Is.False);
            Assert.That(AclResolver.Permits(entry, System.Array.Empty<string>(), AclVerb.Read), Is.False);
            Assert.That(AclResolver.Permits(entry, new[] { string.Empty }, AclVerb.Read), Is.False);
        }

        [Test]
        public void The_verb_that_is_not_grantable_is_refused_rather_than_answered()
        {
            // An operation declaring `none` touches no data and never asks this. If one ever did,
            // the answer is no, because no rule grants it and a rule that seemed to would be a
            // rule that had been read wrongly.
            var entry = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "R"))).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.None), Is.False);
        }

        [Test]
        public void A_parent_chain_that_loops_ends_rather_than_running()
        {
            // No tree KeePass builds can do this. A bug in the seam could, and this runs on a
            // connection thread, so the walk is bounded rather than trusted.
            var looping = TestNode.Root("root").Holding(Rules.Stored(("reader", "off:R"))).InACycle();

            var decision = AclResolver.Decide(looping, Reader, AclVerb.Read);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.Reason, Does.Contain("deeper"));
            Assert.That(AclResolver.Arrives(looping, "reader").Count, Is.Zero);
        }

        [Test]
        public void A_decision_says_what_answered_it()
        {
            // What the router writes into the audit log, which is why it names a node rather than
            // quoting one. The name is a UUID by the time a real database supplies it.
            var granted = AclResolver.Decide(
                TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry"), Reader, AclVerb.Read);

            Assert.That(granted.Permitted, Is.True);
            Assert.That(granted.DecidedAt, Is.EqualTo("root"));
            Assert.That(granted.ToString(), Does.StartWith("permitted"));

            var refused = AclResolver.Decide(
                TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry"), Reader, AclVerb.Delete);

            Assert.That(refused.DecidedAt, Is.EqualTo("root"));
            Assert.That(refused.ToString(), Does.StartWith("refused"));
        }

        [Test]
        public void A_refusal_reports_the_first_profile_it_tried()
        {
            // Two profiles, refused for two different reasons, and the account given is the first
            // one rather than whichever happened to be tried last.
            var entry = TestNode.Root("root").Sealing("editor", AclVerb.Update).Child("an entry");

            var decision = AclResolver.Decide(entry, new[] { "reader", "editor" }, AclVerb.Delete);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.DecidedAt, Is.EqualTo("root"));
            Assert.That(decision.Reason, Does.Contain("does not name"), "which is what reader was refused for");
        }

        [Test]
        public void A_row_that_refuses_ends_the_walk_whatever_stands_above_or_beside_it()
        {
            // The first step: a lost version or a text out of form refuses the profile at the
            // node, and is never passed over for what a group above grants.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read, AclVerb.Update);

            var lost = root.Child("lost").Holding(Rules.Stored(("reader", "row:RU"), ("reader", null), (null, "sw:inherit reader=RU")));
            var unread = root.Child("unread").Holding(Rules.Stored(("reader", "row:RU"), ("reader", "row:UR")));

            var byLoss = AclResolver.Decide(lost.Child("an entry"), Reader, AclVerb.Read);
            Assert.That(byLoss.Permitted, Is.False);
            Assert.That(byLoss.Reason, Is.EqualTo("a rule here was lost"));
            Assert.That(byLoss.DecidedAt, Is.EqualTo("lost"));

            var byForm = AclResolver.Decide(unread.Child("an entry"), Reader, AclVerb.Read);
            Assert.That(byForm.Permitted, Is.False);
            Assert.That(byForm.Reason, Is.EqualTo("a rule here could not be read"));
            Assert.That(byForm.DecidedAt, Is.EqualTo("unread"));

            Assert.That(AclResolver.Permits(lost.Child("an entry"), new[] { "editor" }, AclVerb.Read), Is.False);
            Assert.That(
                AclResolver.Permits(lost.Child("an entry").Granting("reader", AclVerb.Read), Reader, AclVerb.Read), Is.True,
                "and a nearer rule below still answers first");
        }

        [Test]
        public void Rows_whose_versions_all_grant_give_what_all_of_them_hold_and_read_no_record()
        {
            // The second step: no record is read where the row answers, the row's or the
            // switch's, and the letters are what every granting version holds, a switch counting
            // as a letter.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var node = root.Child("node").Holding(Rules.Stored(
                ("reader", "row:RUA"), ("reader", "row:RA"), (null, "sw:inherit reader=C")));

            Assert.That(Letters(AclResolver.Arrives(node, "reader")), Is.EqualTo("RA"));
            Assert.That(AclResolver.Permits(node.Child("an entry"), Reader, AclVerb.Read, AclReach.Attachments), Is.True);

            var apart = AclResolver.Decide(node.Child("an entry"), Reader, AclVerb.Update);
            Assert.That(apart.Permitted, Is.False);
            Assert.That(apart.Reason, Is.EqualTo("rules here made apart do not all grant it"));
            Assert.That(apart.DecidedAt, Is.EqualTo("node"));
        }

        [Test]
        public void A_switch_that_refuses_ends_the_walk_by_a_version_a_loss_or_a_text_out_of_form()
        {
            // The third step, for a profile whose row does not answer by itself.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var cases = new Dictionary<string, NodeRule>
            {
                { "a rule here refuses anything it does not name", Rules.Stored((null, "sw:deny")) },
                { "a rule here was lost", Rules.Stored((null, "sw:inherit reader=R"), (null, null)) },
                { "a rule here could not be read", Rules.Stored((null, "sw:inherit reader=r")) },
            };

            foreach (var refusing in cases)
            {
                var node = root.Child("node").Holding(refusing.Value);
                var decision = AclResolver.Decide(node.Child("an entry"), Reader, AclVerb.Read);

                Assert.That(decision.Permitted, Is.False, refusing.Key);
                Assert.That(decision.Reason, Is.EqualTo(refusing.Key));
                Assert.That(decision.DecidedAt, Is.EqualTo("node"));
            }

            var taken = root.Child("node").Holding(Rules.Stored(("reader", "off:R"), (null, "sw:deny")));
            Assert.That(AclResolver.Permits(taken, Reader, AclVerb.Read), Is.False, "a row taken off lets nothing through a switch that refuses");
        }

        [Test]
        public void A_node_that_lets_through_cuts_what_arrives_to_what_its_versions_recorded()
        {
            // The fourth step: the limit is cut to the letters all granting versions hold, to the
            // letters each version taking the rule off recorded, `off:deny` recording none, and to
            // what each inheriting switch recorded for the profile.
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "CRUDLA")));

            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:RUL"))), "reader")), Is.EqualTo("RUL"));
            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:RUL"), ("reader", "row:RLA"))), "reader")), Is.EqualTo("RL"));
            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:RUL"), ("reader", "off:CRL"))), "reader")), Is.EqualTo("RL"));
            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:RUL"), ("reader", "off:deny"))), "reader")), Is.Empty);
            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored((null, "sw:inherit reader=UL"))), "reader")), Is.EqualTo("UL"));
            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:RUL"), (null, "sw:inherit reader=UA"))), "reader")), Is.EqualTo("U"));
        }

        [Test]
        public void The_switch_s_letters_are_read_for_a_profile_the_node_names_wherever_its_row_lets_through()
        {
            // The fifth step: a profile with a row taken off is cut by the switch as well, and a
            // profile the switch recorded nothing for gets nothing through it.
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "RU"), ("editor", "RU")));
            var node = Under(root, Rules.Stored(("reader", "off:RU"), ("editor", "off:RU"), (null, "sw:inherit reader=R")));

            Assert.That(Letters(AclResolver.Arrives(node, "reader")), Is.EqualTo("R"));
            Assert.That(Letters(AclResolver.Arrives(node, "editor")), Is.Empty);
        }

        [Test]
        public void A_row_or_a_switch_with_no_version_cuts_nothing()
        {
            // The sixth step, which is a node never ruled and a switch never set.
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "CRUSDLAP")));
            var node = root.Child("group").Child("node");

            Assert.That(Letters(AclResolver.Arrives(node, "reader")), Is.EqualTo("CRUSDLAP"));
            Assert.That(NodeRule.None.Limit("reader").SameAs(AclGrant.Every), Is.True);
        }

        [Test]
        public void Above_the_root_nothing_arrives_and_a_limit_names_the_node_that_set_it()
        {
            // The seventh step: a walk that leaves the root gives nothing, whatever limits it met,
            // and a refusal by a limit names the nearest node that cut the letter away.
            var root = TestNode.Root("root").Holding(Rules.Stored(("reader", "off:CRUSDLAP")));
            Assert.That(Letters(AclResolver.Arrives(root, "reader")), Is.Empty);

            var granting = TestNode.Root("granting").Granting("reader", AclVerb.Read, AclVerb.Update, AclVerb.Attachments);
            var group = granting.Child("group").Holding(Rules.Stored(("reader", "off:RUA")));
            var middle = group.Child("middle").Holding(Rules.Stored((null, "sw:inherit reader=RA")));
            var entry = middle.Child("entry").Holding(Rules.Stored(("reader", "off:RU")));

            var update = AclResolver.Decide(entry, Reader, AclVerb.Update);
            Assert.That(update.Permitted, Is.False);
            Assert.That(update.Reason, Is.EqualTo("a limit here does not let it through"));
            Assert.That(update.DecidedAt, Is.EqualTo("middle"));

            var attachments = AclResolver.Decide(entry, Reader, AclVerb.Read, AclReach.Attachments);
            Assert.That(attachments.Reason, Is.EqualTo("a limit here does not let it through"));
            Assert.That(attachments.DecidedAt, Is.EqualTo("entry"), "the nearest node that cut the switch away");

            var read = AclResolver.Decide(entry, Reader, AclVerb.Read);
            Assert.That(read.Permitted, Is.True);
            Assert.That(read.DecidedAt, Is.EqualTo("granting"));
        }

        [Test]
        public void Nothing_is_refused_for_a_record_that_holds_more_than_arrives()
        {
            // The eighth step: a version that recorded more than arrives now lets through what
            // arrives, and a version that recorded less cuts; neither refuses by itself.
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "RU")));

            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:CRUDLA"))), "reader")), Is.EqualTo("RU"));
            Assert.That(Letters(AclResolver.Arrives(Under(root, Rules.Stored(("reader", "off:R"))), "reader")), Is.EqualTo("R"));
            Assert.That(AclResolver.Permits(Under(root, Rules.Stored(("reader", "off:R"))), Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_cut_at_a_group_and_a_cut_at_a_node_along_a_single_walk_both_hold()
        {
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "CRUL"), ("editor", "CRUL")));
            var group = root.Child("group").Holding(Rules.Stored(("reader", "off:RUL"), (null, "sw:inherit editor=CRU reader=CRUL")));
            var node = group.Child("node").Holding(Rules.Stored(("reader", "off:CRL"), (null, "sw:inherit editor=RUL reader=CRUL")));
            var entry = node.Child("an entry");

            Assert.That(Letters(AclResolver.Arrives(group, "reader")), Is.EqualTo("RUL"));
            Assert.That(Letters(AclResolver.Arrives(group, "editor")), Is.EqualTo("CRU"));
            Assert.That(Letters(AclResolver.Arrives(node, "reader")), Is.EqualTo("RL"));
            Assert.That(Letters(AclResolver.Arrives(node, "editor")), Is.EqualTo("RU"));
            Assert.That(AclResolver.Decide(entry, new[] { "reader" }, AclVerb.Update).DecidedAt, Is.EqualTo("node"));
            Assert.That(AclResolver.Decide(entry, new[] { "reader" }, AclVerb.Create).DecidedAt, Is.EqualTo("group"));
            Assert.That(AclResolver.Decide(entry, new[] { "editor" }, AclVerb.Create).DecidedAt, Is.EqualTo("node"));
            Assert.That(AclResolver.Decide(entry, new[] { "editor" }, AclVerb.List).DecidedAt, Is.EqualTo("group"));
        }

        [Test]
        public void A_mask_left_with_a_switch_and_no_verb_grants_nothing()
        {
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "RA")));
            var node = Under(root, Rules.Stored(("reader", "off:UA")));

            Assert.That(Letters(AclResolver.Arrives(node, "reader")), Is.EqualTo("A"));
            foreach (var verb in AclVerbs.Grantable.Where(verb => !AclVerbs.IsScope(verb)))
            {
                foreach (var reach in new[] { AclReach.Fields, AclReach.Attachments, AclReach.Protected })
                {
                    Assert.That(AclResolver.Permits(node, Reader, verb, reach), Is.False, verb + " " + reach);
                }
            }

            Assert.That(AclResolver.DecideAny(node, Reader).Permitted, Is.False);
            Assert.That(VerbMask.Of(AclResolver.DecideAny(node, Reader).Grants.Single().Value), Is.EqualTo("........"));
        }

        [Test]
        public void The_answer_does_not_depend_on_the_order_versions_or_profiles_are_met()
        {
            var root = TestNode.Root("root").Holding(Rules.Node(false, ("reader", "CRUA"), ("editor", "RUL")));
            var versions = new List<(string Profile, string Text)>
            {
                ("reader", "off:RUA"), ("reader", "off:CRU"), (null, "sw:inherit editor=RL reader=RUA"),
                (null, "sw:inherit editor=RUL reader=CRUA"), ("editor", "off:RUL"),
            };
            var expected = Answers(Under(root, Rules.Stored(versions.ToArray())));

            var random = new Random(617);
            for (var round = 0; round < 30; round++)
            {
                var node = Under(root, Rules.Stored(versions.OrderBy(version => random.Next()).ToArray()));
                Assert.That(Answers(node), Is.EqualTo(expected), "round " + round);
            }

            Assert.That(expected, Is.EqualTo(new[] { "reader RU", "editor RL" }));
        }

        [Test]
        public void Every_pair_and_triple_of_a_row_s_versions_gives_what_each_alone_allows()
        {
            // Over an alphabet of two verbs and a switch: each version that grants, takes the rule
            // off or takes it off while the switch refused, under every mask arriving and every
            // state of the node's switch. Whatever versions stand, the node gives exactly what
            // each of them alone gives, letter for letter and question for question, so never a
            // verb, or a verb with a switch, that no version alone allows, and in any order.
            var arriving = Masks();
            var switches = new List<string> { null, "sw:deny" }.Concat(arriving.Select(mask => "sw:inherit reader=" + mask)).ToList();
            var versions = arriving.Select(mask => "row:" + mask)
                .Concat(arriving.Select(mask => "off:" + mask))
                .Concat(new[] { "off:deny" })
                .ToList();

            var checkedSets = 0;
            foreach (var mask in arriving)
            {
                var root = TestNode.Root("root").Holding(Rules.Stored(("reader", "row:" + mask)));
                foreach (var sw in switches)
                {
                    var alone = versions.ToDictionary(version => version, version => Given(root, sw, version));

                    for (var first = 0; first < versions.Count; first++)
                    {
                        for (var second = first; second < versions.Count; second++)
                        {
                            checkedSets += Holds(root, sw, alone, versions[first], versions[second]);
                            for (var third = second; third < versions.Count; third++)
                            {
                                checkedSets += Holds(root, sw, alone, versions[first], versions[second], versions[third]);
                            }
                        }
                    }
                }
            }

            Assert.That(checkedSets, Is.GreaterThan(80000));
        }

        [Test]
        public void Every_pair_of_the_switch_s_versions_gives_what_each_alone_allows()
        {
            var arriving = Masks();
            var switches = new List<string> { "sw:deny", "sw:allow" }.Concat(arriving.Select(mask => "sw:inherit reader=" + mask)).ToList();
            var rows = new List<string> { null, "off:deny" }.Concat(arriving.Select(mask => "off:" + mask)).Concat(arriving.Select(mask => "row:" + mask)).ToList();

            foreach (var mask in arriving)
            {
                var root = TestNode.Root("root").Holding(Rules.Stored(("reader", "row:" + mask)));
                foreach (var row in rows)
                {
                    foreach (var first in switches)
                    {
                        foreach (var second in switches)
                        {
                            var both = Answer(Under(root, Node(row, first, second)));
                            var alone = Intersection(Answer(Under(root, Node(row, first))), Answer(Under(root, Node(row, second))));
                            if (!both.Equals(alone))
                            {
                                Assert.Fail(mask + ", " + row + ", " + first + " and " + second + " gave " + both + " where each alone allows " + alone);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The node given by these versions of the reader's row, and the same versions given in
        /// another order, against the intersection of what each gives alone; 1 for a set held.
        /// </summary>
        private static int Holds(TestNode root, string sw, IReadOnlyDictionary<string, Answered> alone, params string[] texts)
        {
            // Asserted only where they differ: the sets number tens of thousands.
            var expected = texts.Select(text => alone[text]).Aggregate(Intersection);
            var given = Given(root, sw, texts);
            if (!given.Equals(expected))
            {
                Assert.Fail(string.Join(", ", texts) + " under " + (sw ?? "no switch") + " gave " + given + " where each alone allows " + expected);
            }

            var reversed = Given(root, sw, texts.Reverse().ToArray());
            if (!reversed.Equals(given))
            {
                Assert.Fail(string.Join(", ", texts) + " under " + (sw ?? "no switch") + " gave " + reversed + " in another order and " + given);
            }

            return 1;
        }

        private static Answered Given(TestNode root, string sw, params string[] texts)
        {
            var versions = texts.Select(text => ("reader", text)).ToList();
            if (sw != null)
            {
                versions.Add((null, sw));
            }

            return Answer(Under(root, Rules.Stored(versions.ToArray())));
        }

        private static NodeRule Node(string row, params string[] switches)
        {
            var versions = switches.Select<string, (string Profile, string Text)>(sw => (null, sw)).ToList();
            if (row != null)
            {
                versions.Add(("reader", row));
            }

            return Rules.Stored(versions.ToArray());
        }

        /// <summary>What the node gives the reader, by letters and by each question the gate asks of the alphabet.</summary>
        private static Answered Answer(IAclNode node)
        {
            var questions = new[] { AclVerb.Read, AclVerb.Update }
                .SelectMany(verb => new[] { AclReach.Fields, AclReach.Attachments }, (verb, reach) => (Verb: verb, Reach: reach))
                .Where(question => AclResolver.Permits(node, Reader, question.Verb, question.Reach))
                .Select(question => question.Verb + "+" + question.Reach);

            return new Answered(Letters(AclResolver.Arrives(node, "reader")), string.Join(" ", questions));
        }

        private static Answered Intersection(Answered left, Answered right)
        {
            return new Answered(
                string.Concat(left.Letters.Where(letter => right.Letters.IndexOf(letter) >= 0)),
                string.Join(" ", left.Questions.Split(' ').Where(question => right.Questions.Split(' ').Contains(question))));
        }

        private static IEnumerable<string> Answers(IAclNode node)
        {
            return new[] { "reader", "editor" }.Select(profile => profile + " " + Letters(AclResolver.Arrives(node, profile)));
        }

        private static List<string> Masks()
        {
            return new[] { string.Empty, "R", "U", "A", "RU", "RA", "UA", "RUA" }.ToList();
        }

        private static TestNode Under(TestNode above, NodeRule rule)
        {
            return above.Child("node").Holding(rule);
        }

        private static string Letters(AclGrant grant)
        {
            return string.Concat(AclVerbs.Grantable.Where(grant.Holds).Select(VerbMask.LetterOf));
        }

        /// <summary>What a node gives, by letters and by the questions it permits.</summary>
        private struct Answered : IEquatable<Answered>
        {
            public Answered(string letters, string questions)
            {
                Letters = letters;
                Questions = questions;
            }

            public string Letters { get; }

            public string Questions { get; }

            public bool Equals(Answered other)
            {
                return Letters == other.Letters && Questions == other.Questions;
            }

            public override bool Equals(object obj)
            {
                return obj is Answered other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (Letters + "|" + Questions).GetHashCode();
            }

            public override string ToString()
            {
                return "[" + Letters + "] " + Questions;
            }
        }
    }
}
