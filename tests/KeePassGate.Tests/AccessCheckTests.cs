using System;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Pipeline;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The admission record and the rules, combined, and what gets written down about it.
    /// </summary>
    /// <remarks>
    /// The tests that matter are the ones showing that neither half can be widened by the other: a
    /// record naming profiles no rule mentions opens nothing, and a rule naming a profile no record
    /// carries grants nobody anything.
    /// </remarks>
    [TestFixture]
    public sealed class AccessCheckTests
    {
        private RecordingAudit _audit;
        private AccessCheck _check;

        [SetUp]
        public void SetUp()
        {
            _audit = new RecordingAudit();
            _check = new AccessCheck(_audit, () => At);
        }

        [Test]
        public void A_client_holding_a_profile_the_rules_grant_may_do_it()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(_check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, entry).Permitted,
                Is.True);
        }

        [Test]
        public void Removing_a_node_asks_the_group_it_leaves_and_the_node_itself()
        {
            // A removal is a move, so it is asked on both sides of it. The group says removals are
            // allowed here; the node says it is not held back from them.
            var group = TestNode.Root("root").Granting("reader", AclVerb.Delete);
            var entry = group.Child("an entry");

            Assert.That(
                _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, entry).Permitted,
                Is.True, "the group allows removals and the entry inherits that");
        }

        [Test]
        public void A_group_that_allows_removals_inside_it_is_not_thereby_removable()
        {
            // The grant this exists for. A client may create and remove entries in here, including
            // ones it creates later, and the group itself stays because nothing above it allows
            // removals.
            var root = TestNode.Root("root");
            var group = root.Child("a group").Granting("reader", AclVerb.Delete);
            var entry = group.Child("an entry");

            Assert.That(
                _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, entry).Permitted,
                Is.True, "what is inside the group may go");

            Assert.That(
                _check.Decide(Client("reader"), "deleteGroup", AclVerb.Delete, AclReach.Fields, group).Permitted,
                Is.False, "and the group itself may not, because nothing above it says so");
        }

        [Test]
        public void An_entry_whose_own_rule_allows_no_removal_is_held_back_from_one_that_frees_its_siblings()
        {
            var group = TestNode.Root("root").Granting("reader", AclVerb.Delete);
            var kept = group.Child("a kept entry").Granting("reader", AclVerb.Strip);
            var sibling = group.Child("another entry");

            Assert.That(
                _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, kept).Permitted,
                Is.False, "its own rule does not allow removals");

            Assert.That(
                _check.Decide(Client("reader"), "stripField", AclVerb.Strip, AclReach.Fields, kept).Permitted,
                Is.True, "and its fields are still open to stripping");

            Assert.That(
                _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, sibling).Permitted,
                Is.True, "while a sibling with no rule of its own goes");
        }

        [Test]
        public void Nothing_above_the_root_allows_it_to_be_removed()
        {
            // Not a special case in the code: the root has no container to ask, and a removal asks
            // the container.
            var root = TestNode.Root("root").Granting("reader", AclVerb.Delete);

            Assert.That(
                _check.Decide(Client("reader"), "deleteGroup", AclVerb.Delete, AclReach.Fields, root).Permitted,
                Is.False);
        }

        [Test]
        public void A_rule_about_a_profile_the_record_does_not_carry_grants_nothing()
        {
            // Half the intersection. The rules can say what they like about `editor`; a client
            // whose record does not name it is not an editor.
            var entry = TestNode.Root("root").Granting("editor", AclVerb.Read).Child("an entry");

            Assert.That(_check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, entry).Permitted,
                Is.False);
        }

        [Test]
        public void A_record_naming_profiles_no_rule_mentions_opens_nothing()
        {
            // The other half. A generous pairing does not become access on its own: somebody still
            // has to write a rule.
            var entry = TestNode.Root("root").Child("an entry");

            Assert.That(
                _check.Decide(
                    Client("reader", "editor", "auditor"), "readEntry", AclVerb.Read, AclReach.Fields, entry)
                    .Permitted,
                Is.False);
        }

        [Test]
        public void The_verb_is_the_one_the_operation_declared()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(
                _check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, entry).Permitted,
                Is.True);
            Assert.That(
                _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, entry).Permitted,
                Is.False);
        }

        [Test]
        public void An_operation_reaching_an_attachment_needs_the_rule_to_reach_that_far()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(
                _check.Decide(Client("reader"), "readAttachment", AclVerb.Read, AclReach.Attachments, entry).Permitted,
                Is.False);
        }

        [Test]
        public void A_caller_that_is_not_admitted_is_refused_here_as_well()
        {
            // The router refuses a stranger long before this, so arriving here would be a defect.
            // A check that trusted its caller to have checked would stop being one.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(_check.Decide(null, "readEntry", AclVerb.Read, AclReach.Fields, entry).Permitted, Is.False);
            Assert.That(
                _check.Decide(ClientIdentity.Stranger("d1"), "readEntry", AclVerb.Read, AclReach.Fields, entry).Permitted,
                Is.False);
        }

        [Test]
        public void Every_question_is_written_down_whichever_way_it_goes()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            _check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, entry);
            _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, entry);

            Assert.That(_audit.Entries.Select(e => e.OperationId), Is.EqualTo(new[] { "readEntry", "deleteEntry" }));
            Assert.That(_audit.Entries[0].Permitted, Is.True);
            Assert.That(_audit.Entries[1].Permitted, Is.False);
        }

        [Test]
        public void What_is_written_down_names_the_client_the_database_and_the_node()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            _check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, entry);

            var recorded = _audit.Entries.Single();

            Assert.That(recorded.DatabaseId, Is.EqualTo("d1e2f3"));
            Assert.That(recorded.AdmissionKey, Is.Not.Null.And.Not.Empty);
            Assert.That(recorded.Node, Is.EqualTo("an entry"), "by whatever the node calls itself, a UUID in a database");
            Assert.That(recorded.At, Is.EqualTo(At));
            Assert.That(recorded.Reason, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void What_is_written_down_names_the_certificate_and_the_client_its_record_belongs_to()
        {
            // A client that renewed: the certificate asking is not the one it paired with, and the
            // line names both, the second as the record carried it to the credential check.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
            var pairedWith = new string('c', 64);

            ClientIdentity client;
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                client = ClientIdentity.Paired("d1e2f3", certificate, new[] { "reader" }, null, pairedWith);
            }

            _check.Decide(client, "readEntry", AclVerb.Read, AclReach.Fields, entry);
            _check.Decide(client, "deleteEntry", AclVerb.Delete, AclReach.Fields, entry);

            Assert.That(_audit.Entries.Select(line => line.AdmissionKey), Is.All.EqualTo(client.AdmissionKey));
            Assert.That(_audit.Entries.Select(line => line.ClientKey), Is.All.EqualTo(pairedWith),
                "permitted and refused alike");
            Assert.That(_audit.Entries.Single(line => line.Permitted).Line(),
                Does.Contain("\"admissionKey\":\"" + client.AdmissionKey + "\",\"clientKey\":\"" + pairedWith + "\""));
        }

        [Test]
        public void A_refused_certificate_is_written_down_by_its_own_key_and_no_client()
        {
            // No record stands behind it, so there is no client key to name; the admission key is
            // the one the Clients page revoked.
            ClientIdentity refused;
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                refused = ClientIdentity.Refused("d1e2f3", certificate, "this certificate has no admission record");
            }

            _check.Refuse(refused, "readEntry");

            var recorded = _audit.Entries.Single();
            Assert.That(recorded.AdmissionKey, Is.EqualTo(refused.AdmissionKey));
            Assert.That(recorded.ClientKey, Is.Null);
            Assert.That(recorded.Line(), Does.Contain("\"clientKey\":null"));
        }

        [Test]
        public void What_is_written_down_carries_no_profile()
        {
            // A log line says what was decided about a node, not what the client is. Which profiles
            // a client holds is in the database already, and a log repeating them is a second copy
            // of the thing worth stealing.
            //
            // The verb is not asserted here and cannot usefully be: `AuditEntry` has no member for
            // one, so its absence is structural rather than something a line could be caught
            // carrying. The operation is named, and naming it is the point.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            _check.Decide(Client("reader", "editor"), "readEntry", AclVerb.Read, AclReach.Fields, entry);

            var line = _audit.Entries.Single().Line();

            Assert.That(line, Does.Not.Contain("reader"));
            Assert.That(line, Does.Not.Contain("editor"));
        }

        [Test]
        public void A_refusal_a_removed_profile_would_have_permitted_says_so_and_names_it()
        {
            // The record holds `ops` and the list does not, so the client asks holding `reader`
            // alone and the rule for `ops` answers about nobody. Written as the rules refusing,
            // that sends the operator looking for a rule; the reason is that they removed a name.
            var entry = TestNode.Root("root").Holding(Rules.Granting("ops", "R", "audit", "R")).Child("an entry");

            var permitted = _check.Decide(
                Removing(new[] { "reader" }, "ops", "audit", "legacy"), "readEntry", AclVerb.Read, AclReach.Fields, entry)
                .Permitted;

            var recorded = _audit.Entries.Single();

            Assert.That(permitted, Is.False, "the second walk decides what is written, never what is answered");
            Assert.That(recorded.Permitted, Is.False);
            Assert.That(recorded.Reason, Is.EqualTo(AccessCheck.ProfileRemoved));
            Assert.That(recorded.RemovedProfiles, Is.EqualTo(new[] { "ops", "audit" }),
                "the removed names that would each have permitted it, and not `legacy`, which would not");
            Assert.That(recorded.DecidedAt, Is.EqualTo("root"), "where the rule that would have is");

            var line = recorded.Line();
            Assert.That(line, Does.Contain("\"removedProfiles\":[\"ops\",\"audit\"]"));
            Assert.That(line, Does.Not.Contain("reader"), "what the client still holds is never written");
        }

        [Test]
        public void A_refusal_no_removed_profile_would_have_permitted_is_the_rules_refusing()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.List).Child("an entry");

            _check.Decide(Removing(new[] { "reader" }, "ops"), "readEntry", AclVerb.Read, AclReach.Fields, entry);

            var recorded = _audit.Entries.Single();

            Assert.That(recorded.Reason, Is.Not.EqualTo(AccessCheck.ProfileRemoved));
            Assert.That(recorded.RemovedProfiles, Is.Null);
            Assert.That(recorded.Line(), Does.Not.Contain("ops"));
        }

        [Test]
        public void A_removal_two_removed_profiles_allow_only_together_names_both()
        {
            // A removal is asked of the group it leaves and of the node itself, so one removed name
            // can be what the container needed and another what the node did: `ops` may remove
            // from the root and not this entry, `cleaner` this entry and nothing from the root.
            var entry = TestNode.Root("root").Granting("ops", AclVerb.Delete)
                .Child("an entry").Holding(Rules.Granting("cleaner", "D", "ops", ""));

            _check.Decide(Removing(Array.Empty<string>(), "ops", "cleaner"), "deleteEntry", AclVerb.Delete, AclReach.Fields, entry);

            var recorded = _audit.Entries.Single();

            Assert.That(recorded.Reason, Is.EqualTo(AccessCheck.ProfileRemoved));
            Assert.That(recorded.RemovedProfiles, Is.EqualTo(new[] { "ops", "cleaner" }));
        }

        [Test]
        public void The_line_names_the_rule_that_decided_and_not_only_the_node_asked_about()
        {
            // Nearest wins, so a refusal is usually decided somewhere above the node in question.
            // Without this the operator reads "a rule here does not grant it" against an entry,
            // opens that entry, finds no rule on it, and concludes the audit is wrong. A removal is
            // worse again, being asked on two nodes, and the line could not say which said no.
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, entry);

            var recorded = _audit.Entries.Single();

            Assert.That(recorded.Node, Is.EqualTo("an entry"), "which node was asked about");
            Assert.That(recorded.DecidedAt, Is.EqualTo("root"), "and which node's rule answered");
        }

        [Test]
        public void A_request_that_never_reached_a_node_still_says_so()
        {
            _check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, null);

            Assert.That(_audit.Entries.Single().Node, Is.Null);
            Assert.That(_audit.Entries.Single().Permitted, Is.False);
        }

        [Test]
        public void Removing_a_node_that_is_not_there_is_recorded_as_nothing_to_decide_about()
        {
            // The one place a node the rules refused and a node that was never there are meant to
            // be told apart. A caller gets the same 404 for both deliberately, so the operator's
            // file is where the difference has to survive, and a removal was the one verb that
            // lost it: an absent node has no parent, and the two-sided walk read that as nothing
            // above it allowing removals, which names a rule that was never consulted.
            _check.Decide(Client("reader"), "deleteEntry", AclVerb.Delete, AclReach.Fields, null);

            var recorded = _audit.Entries.Single();

            Assert.That(recorded.Permitted, Is.False);
            Assert.That(
                recorded.Reason,
                Is.EqualTo(Refused("readEntry", AclVerb.Read)),
                "and it says what every other verb says about a node that is not there");
        }

        /// <summary>What the resolver writes down about a node that is not there.</summary>
        /// <remarks>
        /// Read off a decision rather than spelled out here, so that the assertion is that a
        /// removal says the same thing as every other verb rather than that both say one
        /// particular sentence.
        /// </remarks>
        private static string Refused(string operationId, AclVerb verb)
        {
            var audit = new RecordingAudit();
            new AccessCheck(audit).Decide(Client("reader"), operationId, verb, AclReach.Fields, null);

            return audit.Entries.Single().Reason;
        }

        [Test]
        public void An_access_check_with_nowhere_to_record_still_decides()
        {
            var check = new AccessCheck(null);
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            Assert.That(
                check.Decide(Client("reader"), "readEntry", AclVerb.Read, AclReach.Fields, entry).Permitted,
                Is.True);
        }

        [Test]
        public void A_question_about_every_verb_is_written_down_once_whichever_way_it_goes()
        {
            var entry = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");

            var permitted = _check.Decide(Client("reader"), "getEntryAccess", AclVerb.Any, AclReach.Fields, entry);
            var refused = _check.Decide(Client("editor"), "getEntryAccess", AclVerb.Any, AclReach.Fields, entry);

            Assert.That(permitted.Permitted, Is.True);
            Assert.That(refused.Permitted, Is.False);
            Assert.That(_audit.Entries.Select(line => line.Permitted), Is.EqualTo(new[] { true, false }));
            Assert.That(_audit.Entries[0].DecidedAt, Is.EqualTo("root"), "the rule that granted something");
        }

        [Test]
        public void A_question_about_every_verb_carries_what_each_profile_is_granted()
        {
            var entry = TestNode.Root("root")
                .Holding(Rules.Granting("reader", "R", "editor", "U"))
                .Child("an entry");

            var decision = _check.Decide(
                Client("reader", "editor"), "getEntryAccess", AclVerb.Any, AclReach.Fields, entry);

            Assert.That(
                decision.Grants.Select(grant => grant.Key + " " + VerbMask.Of(grant.Value)),
                Is.EqualTo(new[] { "reader .R......", "editor ..U....." }));
        }

        [Test]
        public void A_question_about_every_verb_is_not_a_removal()
        {
            // A `D` at the node is its rule saying removals are allowed there, and asking about it
            // asks the node alone. The group above is asked when something is actually removed.
            var group = TestNode.Root("root").Child("a group").Granting("reader", AclVerb.Delete);

            Assert.That(
                _check.Decide(Client("reader"), "getGroupAccess", AclVerb.Any, AclReach.Fields, group).Permitted,
                Is.True);
            Assert.That(
                _check.Decide(Client("reader"), "deleteGroup", AclVerb.Delete, AclReach.Fields, group).Permitted,
                Is.False, "while removing the group itself is still refused");
        }

        [Test]
        public void A_question_about_every_verb_a_removed_profile_would_have_answered_says_so()
        {
            var entry = TestNode.Root("root").Granting("ops", AclVerb.Read).Child("an entry");

            var decision = _check.Decide(
                Removing(new[] { "reader" }, "ops"), "getEntryAccess", AclVerb.Any, AclReach.Fields, entry);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(_audit.Entries.Single().Reason, Is.EqualTo(AccessCheck.ProfileRemoved));
            Assert.That(_audit.Entries.Single().RemovedProfiles, Is.EqualTo(new[] { "ops" }));
            Assert.That(decision.Grants.Select(grant => grant.Key), Is.EqualTo(new[] { "reader" }),
                "and the answer is about the profiles the client holds, never the removed ones");
        }

        private static readonly DateTimeOffset At = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        private static ClientIdentity Client(params string[] profiles)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, profiles);
            }
        }

        /// <summary>A client holding these profiles, whose record also holds these removed ones.</summary>
        private static ClientIdentity Removing(string[] held, params string[] removed)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, held, removed);
            }
        }
    }
}
