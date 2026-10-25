using KeePassGate.Acl;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What the rules answer, on a tree that is not a database.
    /// </summary>
    /// <remarks>
    /// Every tree here is invented, and so is every profile name on it. A grant document names
    /// profiles and nothing else, so there is nothing in these that could come from anybody's real
    /// database even by accident.
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
        public void An_empty_rule_takes_everything_away_from_there_down()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("private").Holding(Rules.Granting(("reader", "")));

            Assert.That(AclResolver.Permits(root.Child("shared"), Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(group.Child("an entry"), Reader, AclVerb.Read), Is.False);
        }

        [Test]
        public void A_rule_about_another_profile_is_not_a_rule_about_this_one()
        {
            // Silence, so the walk carries on past it rather than stopping. A document that mentioned
            // any profile stopping the walk for all of them would make one rule answer for another.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var entry = root.Child("group").Granting("editor", AclVerb.Update).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_default_of_deny_stops_the_walk_for_a_profile_it_does_not_name()
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
        public void Reading_a_node_whole_is_one_rule_reaching_every_part()
        {
            // What decides who is handed a tag. One profile's `read` over attachments and another's
            // over protected values do not add up to reading an entry whole, because the gate
            // refuses any request needing both from two rules.
            var entry = TestNode.Root("root")
                .Holding(Rules.Granting(("files", "RA"), ("secrets", "RP"), ("both", "RAP")))
                .Child("an entry");

            Assert.That(AclResolver.ReadsWhole(entry, new[] { "files" }, AclReach.Attachments), Is.True);
            Assert.That(AclResolver.ReadsWhole(entry, new[] { "files", "secrets" }, AclReach.Attachments, AclReach.Protected),
                Is.False, "two rules each reaching part of it");
            Assert.That(AclResolver.ReadsWhole(entry, new[] { "files", "both" }, AclReach.Attachments, AclReach.Protected),
                Is.True, "and one reaching all of it is enough among others");
            Assert.That(AclResolver.ReadsWhole(entry, new[] { "secrets" }), Is.True, "with nothing further in to reach");
            Assert.That(AclResolver.ReadsWhole(entry, new[] { "nobody" }), Is.False);
            Assert.That(AclResolver.ReadsWhole(entry, null), Is.False);
            Assert.That(AclResolver.ReadsWhole(null, new[] { "both" }), Is.False);

            var listed = TestNode.Root("root").Granting("lister", AclVerb.List, AclVerb.Attachments).Child("an entry");
            Assert.That(AclResolver.ReadsWhole(listed, new[] { "lister" }), Is.False, "`list` is not `read`");
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
            // The half of that guard nothing was asking about. `strip` is defined as reaching
            // the parts inside a node, its attachments among them, so a rule holding it and
            // neither switch removes an attachment and a protected value alike. It does so
            // without `delete`, because a grant is a set rather than a ladder. Written down
            // here because `...S....` is exactly the shape the design spells out, and because
            // the resolver had no test naming this verb at all.
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
            // It says how far a rule reaches, so "may this profile attachments" is not a question.
            var entry = TestNode.Root("root")
                .Granting("reader", AclVerb.Read, AclVerb.Attachments).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Attachments), Is.False);
        }

        [Test]
        public void A_tree_with_no_rules_on_it_grants_nothing()
        {
            var entry = TestNode.Root("root").Child("group").Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Read), Is.False);
            Assert.That(AclResolver.Decide(entry, Reader, AclVerb.Read).DecidedAt, Is.Null);
        }

        [Test]
        public void A_document_that_will_not_read_refuses_rather_than_being_skipped()
        {
            // The one that would be tempting to get wrong. Carrying on up the tree past a broken
            // document would answer with a permission that the broken rule may have been taking
            // away, so a rule nobody can read is a refusal.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var group = root.Child("group").Holding("{\"reader\":[\"purge\"]}");

            var decision = AclResolver.Decide(group.Child("an entry"), Reader, AclVerb.Read);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.DecidedAt, Is.EqualTo("group"));
        }

        [Test]
        public void A_nearer_rule_is_answered_before_a_document_above_that_will_not_read()
        {
            // Refused where it is found, and a walk that stops at a nearer rule never finds it.
            // The editor's inheritance has to say the same, which `AclInheritanceTests` asks.
            var root = TestNode.Root("root").Holding("{\"reader\":[\"purge\"]}");
            var entry = root.Child("group").Granting("reader", AclVerb.Read).Child("an entry");

            var decision = AclResolver.Decide(entry, Reader, AclVerb.Read);

            Assert.That(decision.Permitted, Is.True);
            Assert.That(decision.DecidedAt, Is.EqualTo("group"));
            Assert.That(AclResolver.Decide(entry, new[] { "editor" }, AclVerb.Read).DecidedAt,
                Is.EqualTo("root"), "and a profile nothing nearer names is refused at the broken one");
        }

        [Test]
        public void An_empty_document_is_not_a_document()
        {
            // Absence, not a rule that will not read. A key left blank rather than removed is the
            // shape a hand-edited database arrives in, and reading it as a broken rule would seal
            // off a subtree that nobody meant to seal off.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);

            Assert.That(AclResolver.Permits(root.Child("group").Holding(string.Empty).Child("an entry"),
                Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void There_is_nothing_to_decide_about_nothing()
        {
            Assert.That(AclResolver.Permits(null, Reader, AclVerb.Read), Is.False);
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
            var entry = TestNode.Root("root").Holding(Rules.Granting(("reader", "R"))).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.None), Is.False);
        }

        [Test]
        public void A_parent_chain_that_loops_ends_rather_than_running()
        {
            // No tree KeePass builds can do this. A bug in the seam could, and this runs on a
            // connection thread, so the walk is bounded rather than trusted.
            var looping = TestNode.Root("root").InACycle();

            var decision = AclResolver.Decide(looping, Reader, AclVerb.Read);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.Reason, Does.Contain("deeper"));
        }

        [Test]
        public void A_decision_says_what_answered_it()
        {
            // What step 14 writes into the audit log, which is why it names a node rather than
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
        public void Documents_a_listing_has_already_read_answer_as_they_would_have_and_are_read_once()
        {
            // A listing asks about every entry it walks, and every walk read each document on its
            // way to the root afresh: one rule at the root was parsed once per entry. Remembering
            // them by their text changes nothing about the answers.
            var read = new AclDocuments();
            var root = TestNode.Root("root").Granting("reader", AclVerb.List);
            var open = root.Child("open").Child("an entry");
            var closed = root.Child("closed").Sealing("editor", AclVerb.List).Child("an entry");

            foreach (var node in new IAclNode[] { open, closed, open, closed })
            {
                Assert.That(
                    AclResolver.Permits(node, Reader, AclVerb.List, AclReach.Fields, read),
                    Is.EqualTo(AclResolver.Permits(node, Reader, AclVerb.List, AclReach.Fields)),
                    node.Name);
            }

            Assert.That(read.Read(root.Document), Is.SameAs(read.Read(root.Document)));
        }

        [Test]
        public void A_document_changed_since_it_was_read_is_read_again()
        {
            // Keyed by the text, so a rule rewritten while a listing is being written is a new
            // document and not the one remembered, and one that will not read refuses however
            // often it is met.
            var read = new AclDocuments();
            var root = TestNode.Root("root").Granting("reader", AclVerb.List);
            var entry = root.Child("an entry");

            Assume.That(AclResolver.Permits(entry, Reader, AclVerb.List, AclReach.Fields, read), Is.True);

            root.Granting("reader", AclVerb.Read);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.List, AclReach.Fields, read), Is.False,
                "the rule as it is now, not as it was first read");

            root.Holding("{\"reader\":");
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.List, AclReach.Fields, read), Is.False);
            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.List, AclReach.Fields, read), Is.False,
                "and again, from what was remembered");
        }
    }
}
