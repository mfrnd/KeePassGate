using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The question an operation declaring `any` asks: what each profile may do at a node.
    /// </summary>
    /// <remarks>
    /// Every tree here is invented, and so is every profile name on it, as in the resolver's own
    /// tests: a grant document names profiles and nothing else.
    /// </remarks>
    [TestFixture]
    public sealed class AclDecideAnyTests
    {
        private static readonly string[] Reader = { "reader" };

        [Test]
        public void Each_profile_is_answered_with_what_its_nearest_rule_grants()
        {
            var entry = TestNode.Root("root")
                .Holding("{\"reader\":[\"read\",\"list\",\"protected\"],\"editor\":[\"create\",\"update\"]}")
                .Child("an entry");

            var decision = AclResolver.DecideAny(entry, new[] { "reader", "editor" });

            Assert.That(decision.Permitted, Is.True);
            Assert.That(Masks(decision), Is.EqualTo(new[] { "reader .R...L.P", "editor C.U....." }),
                "one member per profile, in the order they were asked about");
            Assert.That(decision.DecidedAt, Is.EqualTo("root"));
        }

        [Test]
        public void One_profile_s_switch_never_completes_another_s_verb()
        {
            // The case one mask per profile exists for. `R` from one rule and `P` from another do
            // not add up to reading a protected value, because `P` reaches only the verbs of its
            // own rule, and a union of the two masks would say that they do.
            var entry = TestNode.Root("root")
                .Holding("{\"reader\":[\"read\"],\"auditor\":[\"list\",\"protected\"]}")
                .Child("an entry");
            var both = new[] { "reader", "auditor" };

            var decision = AclResolver.DecideAny(entry, both);

            Assert.That(Masks(decision), Is.EqualTo(new[] { "reader .R......", "auditor .....L.P" }));
            Assert.That(AclResolver.Permits(entry, both, AclVerb.Read, AclReach.Protected), Is.False,
                "and the question a read asks agrees that nobody here may read a protected value");
        }

        [Test]
        public void Two_profiles_granted_the_same_verb_keep_their_own_switches()
        {
            // Both may read; only the auditor's rule says protected values too. A switch decided
            // over every profile's rules would give the reader's mask a `P` its own rule does not
            // hold, which the case above cannot see, since there the two share no verb.
            var entry = TestNode.Root("root")
                .Holding("{\"reader\":[\"read\",\"attachments\"],\"auditor\":[\"read\",\"protected\"]}")
                .Child("an entry");

            var decision = AclResolver.DecideAny(entry, new[] { "reader", "auditor" });

            Assert.That(Masks(decision), Is.EqualTo(new[] { "reader .R....A.", "auditor .R.....P" }));
        }

        [Test]
        public void What_it_reports_is_what_each_operation_s_own_question_would_answer()
        {
            // Through the same walk, so a letter is a question an operation would be permitted and
            // a dot one it would be refused, for every verb, both switches, and every shape of
            // tree the rules have an answer for.
            var root = TestNode.Root("root").Holding(
                "{\"reader\":[\"read\",\"list\",\"attachments\"],\"editor\":[\"create\",\"update\",\"protected\"]," +
                "\"auditor\":[\"strip\",\"delete\",\"attachments\",\"protected\"]}");
            var nearer = root.Child("nearer").Holding("{\"reader\":[\"list\",\"protected\"],\"editor\":[]}");
            var sealedGroup = root.Child("sealed").Sealing("auditor", AclVerb.Read, AclVerb.Attachments);
            var broken = root.Child("broken").Holding("{\"reader\":");
            var switchesOnly = root.Child("switches").Holding("{\"reader\":[\"attachments\",\"protected\"]}");

            var nodes = new IAclNode[]
            {
                root, root.Child("an entry"), nearer, nearer.Child("an entry"), sealedGroup.Child("an entry"),
                broken.Child("an entry"), switchesOnly, switchesOnly.Child("an entry"),
            };
            var profiles = new[] { "reader", "editor", "auditor", "nobody" };

            foreach (var node in nodes)
            {
                var masks = AclResolver.DecideAny(node, profiles).Grants
                    .ToDictionary(grant => grant.Key, grant => VerbMask.Of(grant.Value));

                foreach (var profile in profiles)
                {
                    var one = new[] { profile };
                    var where = node.Name + ", " + profile + ": " + masks[profile];

                    foreach (var verb in new[]
                    {
                        AclVerb.Create, AclVerb.Read, AclVerb.Update, AclVerb.Strip, AclVerb.Delete, AclVerb.List,
                    })
                    {
                        Assert.That(
                            masks[profile].IndexOf(VerbMask.LetterOf(verb)) >= 0,
                            Is.EqualTo(AclResolver.Permits(node, one, verb, AclReach.Fields)),
                            where + ", " + AclVerbs.Write(verb));
                    }

                    foreach (var scope in new[] { AclVerb.Attachments, AclVerb.Protected })
                    {
                        var reach = scope == AclVerb.Attachments ? AclReach.Attachments : AclReach.Protected;
                        var reaches = new[] { AclVerb.Create, AclVerb.Read, AclVerb.Update, AclVerb.List }
                            .Any(verb => AclResolver.Permits(node, one, verb, reach));

                        Assert.That(masks[profile].IndexOf(VerbMask.LetterOf(scope)) >= 0, Is.EqualTo(reaches),
                            where + ", " + AclVerbs.Write(scope));
                    }
                }
            }
        }

        [Test]
        public void A_profile_granted_nothing_there_is_answered_as_nothing()
        {
            // Eight dots rather than left out, so the answer names every profile the rules were
            // asked about. One profile granting something is enough for the whole question.
            var entry = TestNode.Root("root").Holding("{\"reader\":[\"read\"],\"editor\":[]}").Child("an entry");

            var decision = AclResolver.DecideAny(entry, new[] { "editor", "reader" });

            Assert.That(decision.Permitted, Is.True);
            Assert.That(Masks(decision), Is.EqualTo(new[] { "editor ........", "reader .R......" }));
        }

        [Test]
        public void The_node_is_asked_alone_as_the_tab_shows_its_rule()
        {
            // What the rules grant at the node rather than what would succeed there: a `delete`
            // the group above refuses is still reported, though removing the node asks that
            // group too, and so is a `strip` on a group, though no operation strips a group.
            var group = TestNode.Root("root").Child("group").Holding("{\"reader\":[\"strip\"]}");
            var entry = group.Child("an entry").Holding("{\"reader\":[\"delete\"]}");

            var atGroup = AclResolver.DecideAny(group, Reader);
            var atEntry = AclResolver.DecideAny(entry, Reader);

            Assert.That(atGroup.Permitted, Is.True);
            Assert.That(Masks(atGroup), Is.EqualTo(new[] { "reader ...S...." }));
            Assert.That(atEntry.Permitted, Is.True);
            Assert.That(Masks(atEntry), Is.EqualTo(new[] { "reader ....D..." }));
        }

        [Test]
        public void Nothing_granted_to_any_profile_is_refused_with_the_first_reason()
        {
            var decision = AclResolver.DecideAny(TestNode.Root("root").Child("an entry"), Reader);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("no rule anywhere grants it"));
            Assert.That(decision.DecidedAt, Is.Null);
            Assert.That(Masks(decision), Is.EqualTo(new[] { "reader ........" }));
        }

        [Test]
        public void A_seal_refuses_where_it_stands()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var sealedGroup = root.Child("sealed").Sealing("editor", AclVerb.Update);

            var decision = AclResolver.DecideAny(sealedGroup.Child("an entry"), Reader);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("a rule here refuses anything it does not name"));
            Assert.That(decision.DecidedAt, Is.EqualTo("sealed"));
        }

        [Test]
        public void A_rule_that_will_not_read_refuses_every_verb_where_it_is_found()
        {
            var root = TestNode.Root("root").Granting("reader", AclVerb.Read);
            var broken = root.Child("broken").Holding("{\"reader\":");

            var decision = AclResolver.DecideAny(broken.Child("an entry"), Reader);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("a rule here could not be read"));
            Assert.That(Masks(decision), Is.EqualTo(new[] { "reader ........" }));
        }

        [Test]
        public void A_switch_beside_no_verb_it_qualifies_is_not_shown()
        {
            // A removal takes attachments and protected values without asking, so a switch beside
            // `strip` alone reaches nothing further, and a switch beside no verb grants nothing.
            var stripping = TestNode.Root("root").Granting("reader", AclVerb.Strip, AclVerb.Attachments)
                .Child("an entry");
            var scopeOnly = TestNode.Root("root").Granting("reader", AclVerb.Attachments).Child("an entry");

            Assert.That(Masks(AclResolver.DecideAny(stripping, Reader)), Is.EqualTo(new[] { "reader ...S...." }));

            var decision = AclResolver.DecideAny(scopeOnly, Reader);
            Assert.That(decision.Permitted, Is.False, "a scope over no verbs grants nothing");
            Assert.That(Masks(decision), Is.EqualTo(new[] { "reader ........" }));
        }

        [Test]
        public void Removals_allowed_at_a_node_are_shown_where_the_node_itself_cannot_be_removed()
        {
            // `D` is what the rule at the node says, as the tab shows it. The root cannot be
            // removed, since nothing above it allows that, and what is inside it can.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Delete);

            Assert.That(Masks(AclResolver.DecideAny(root, Reader)), Is.EqualTo(new[] { "reader ....D..." }));
        }

        [Test]
        public void A_permitted_answer_names_a_rule_that_grants_something()
        {
            // The first grant found, so the node a line in the audit names holds a rule that does
            // grant. The first refusal would send the operator to a rule that refused the profile
            // asked about first.
            var root = TestNode.Root("root").Granting("editor", AclVerb.Update);
            var group = root.Child("a group").Holding("{\"reader\":[]}");

            var decision = AclResolver.DecideAny(group.Child("an entry"), new[] { "reader", "editor" });

            Assert.That(decision.Permitted, Is.True);
            Assert.That(decision.DecidedAt, Is.EqualTo("root"));
            Assert.That(decision.Reason, Is.EqualTo("a rule here grants it"));
        }

        [Test]
        public void A_node_that_is_not_there_is_nothing_to_decide_about()
        {
            var decision = AclResolver.DecideAny(null, Reader);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.Reason, Is.EqualTo("there is nothing to decide about"));
            Assert.That(decision.Grants, Is.Null, "no node, so nothing anybody holds there");
        }

        [Test]
        public void A_client_holding_no_profiles_is_refused()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(AclResolver.DecideAny(entry, Array.Empty<string>()).Permitted, Is.False);
            Assert.That(AclResolver.DecideAny(entry, null).Reason, Is.EqualTo("the client holds no profiles"));
        }

        [Test]
        public void A_profile_named_twice_is_answered_once()
        {
            // Each profile is a member of the answer, and a member named twice is not JSON anybody
            // can read back.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(Masks(AclResolver.DecideAny(entry, new[] { "reader", "reader" })),
                Is.EqualTo(new[] { "reader .R......" }));
        }

        [Test]
        public void No_rule_can_hold_the_question_itself()
        {
            // Asked one verb at a time, `any` is not something a rule grants, so nothing that asks
            // the single question can be permitted by it.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(AclResolver.Permits(entry, Reader, AclVerb.Any), Is.False);
        }

        private static IEnumerable<string> Masks(AclDecision decision)
        {
            return decision.Grants.Select(grant => grant.Key + " " + VerbMask.Of(grant.Value));
        }
    }
}
