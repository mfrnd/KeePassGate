using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The profiles page against a database: the list in its custom data, and the rules everywhere.
    /// </summary>
    /// <remarks>
    /// The symmetry is the thing worth pinning. Adding a name and removing one each touch one
    /// value, the list, and neither writes a rule: a client holds only the names the list still
    /// names, so the list is what decides whether the rules for a name are in force.
    /// </remarks>
    [TestFixture]
    public sealed class DatabaseProfilesTests
    {
        /// <summary>
        /// One authority for the whole fixture, because a record has to carry a real certificate.
        /// </summary>
        /// <remarks>
        /// A record is keyed on the digest of its own certificate and refuses to load unless the
        /// two agree and the bytes parse, so invented bytes make a record that stores and never
        /// reads back: every count over it is zero and every assertion about zero passes for the
        /// wrong reason. Issuing is the slow part, so it is done once and the certificates are
        /// handed out.
        /// </remarks>
        private static TestAuthority _authority;

        private static readonly Dictionary<string, X509Certificate2> Issued =
            new Dictionary<string, X509Certificate2>(StringComparer.Ordinal);

        [OneTimeSetUp]
        public void Issue()
        {
            _authority = TestAuthority.New();
        }

        [OneTimeTearDown]
        public void Done()
        {
            foreach (var certificate in Issued.Values)
            {
                certificate.Dispose();
            }

            Issued.Clear();
        }

        [Test]
        public void A_profile_added_is_written_where_the_database_keeps_them()
        {
            var database = new FakeGateDatabase();
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Add("ops"), Is.Null);

            Assert.That(profiles.Names(), Is.EqualTo(new[] { "ops" }));
            Assert.That(
                ProfileNames.Read(database.PluginCustomData.Get(ProfileNames.CustomDataKey)).Holds("ops"),
                Is.True);
            Assert.That(database.MarkedModifiedCount, Is.EqualTo(1), "or KeePass has nothing to save");
        }

        [Test]
        public void Adding_the_same_name_twice_says_so_rather_than_marking_the_database_again()
        {
            var database = new FakeGateDatabase();
            var profiles = new DatabaseProfiles(database, null);

            profiles.Add("ops");

            Assert.That(profiles.Add("ops"), Does.Contain("already named"));
            Assert.That(database.MarkedModifiedCount, Is.EqualTo(1), "the second one wrote nothing");
        }

        [Test]
        public void Removing_a_profile_leaves_its_rules_where_they_are_and_says_so()
        {
            // The list is the only thing removal touches. A client holds only the names the list
            // still names, so the rules are inert without being rewritten, and adding the name
            // back is a full undo rather than a fresh start.
            var group = new TestRuledNode("a group", "{\"ops\":[\"read\"]}");
            var entry = new TestRuledNode("an entry", "{\"ops\":[\"read\"],\"reader\":[\"list\"]}");
            var database = new FakeGateDatabase
            {
                Nodes = new TestRuledNodes(group, entry, new TestRuledNode("another", "{\"reader\":[\"list\"]}"))
            };

            var profiles = new DatabaseProfiles(database, null);
            profiles.Add("ops");

            Assert.That(profiles.Remove("ops"), Is.Null);

            Assert.That(profiles.Names(), Is.Empty);
            Assert.That(profiles.LastRemoval, Does.Contain("grants nothing").And.Contain("2 rules naming it stay"));
            Assert.That(group.Current, Is.EqualTo("{\"ops\":[\"read\"]}"), "untouched");
            Assert.That(entry.Current, Is.EqualTo("{\"ops\":[\"read\"],\"reader\":[\"list\"]}"));
            Assert.That(database.MarkedModifiedCount, Is.EqualTo(2), "the add and the removal, each one write");
        }

        [Test]
        public void A_removal_no_rule_named_says_only_what_the_name_does_now()
        {
            // Because "gone" and "gone, with rules waiting for it" are different pieces of news.
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);
            profiles.Add("ops");

            Assert.That(profiles.Remove("ops"), Is.Null);

            Assert.That(profiles.LastRemoval, Is.EqualTo("The profile ops is gone, so it grants nothing."));
        }

        [Test]
        public void A_profile_that_was_never_named_cannot_be_removed()
        {
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Remove("ops"), Does.Contain("not named here"));
            Assert.That(profiles.LastRemoval, Is.Null);
        }

        [Test]
        public void Adding_a_removed_name_back_says_how_many_rules_come_back_and_restores_them()
        {
            // A full undo: nothing was rewritten, so the rules that were waiting are the rules in
            // force the moment the name is listed again.
            var group = new TestRuledNode("a group", "{\"ops\":[\"read\"]}");
            var entry = new TestRuledNode("an entry", "{\"ops\":[],\"reader\":[\"list\"]}");
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes(group, entry) };
            var profiles = new DatabaseProfiles(database, null);

            profiles.Add("ops");
            Assert.That(profiles.Returning("ops"), Is.Null, "a listed name has nothing to bring back");

            profiles.Remove("ops");
            Assert.That(profiles.Returning("ops"),
                Is.EqualTo("Adding ops again puts the 2 rules naming it back in force."));

            Assert.That(profiles.Add("ops"), Is.Null);
            Assert.That(profiles.Names(), Is.EqualTo(new[] { "ops" }));
            Assert.That(group.Current, Is.EqualTo("{\"ops\":[\"read\"]}"));
            Assert.That(entry.Current, Is.EqualTo("{\"ops\":[],\"reader\":[\"list\"]}"));
        }

        [Test]
        public void A_name_no_rule_names_comes_back_without_a_warning()
        {
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Returning("ops"), Is.Null);
            Assert.That(new DatabaseProfiles(new FakeGateDatabase { IsOpen = false }, null).Returning("ops"), Is.Null);
        }

        [Test]
        public void The_rules_are_counted_rather_than_remembered()
        {
            // They are edited on other dialogs entirely, so a number kept here would be stale the
            // moment somebody pressed OK on one of them.
            var node = new TestRuledNode("a group", null);
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes(node) };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(Counted(profiles.Rules(), "ops"), Is.Zero);

            node.Current = "{\"ops\":[\"read\"]}";

            Assert.That(Counted(profiles.Rules(), "ops"), Is.EqualTo(1));
        }

        [Test]
        public void The_clients_holding_a_profile_are_counted_the_same_way()
        {
            // Counted rather than remembered for the same reason the rules are, and a stronger
            // one: a pairing writes its record while this dialog is open, so a number kept here
            // would be wrong about the client the operator approved a moment ago.
            var database = new FakeGateDatabase();
            var profiles = new DatabaseProfiles(database, null);
            var store = new AdmissionStore(database.PluginCustomData, null);

            Assert.That(Counted(profiles.Clients(), "ops"), Is.Zero);

            store.Write(Admitting("agent-one", "ops"));
            store.Write(Admitting("agent-two", "ops", "reader"));
            store.Write(Admitting("agent-three", "reader"));

            Assert.That(Counted(profiles.Clients(), "ops"), Is.EqualTo(2));
            Assert.That(Counted(profiles.Clients(), "reader"), Is.EqualTo(2));
            Assert.That(Counted(profiles.Clients(), "nobody-holds-this"), Is.Zero);
        }

        [Test]
        public void A_client_holding_a_profile_twice_is_still_one_client()
        {
            // The record is what is counted, and a record naming a profile is one client whatever
            // the list inside it looks like.
            var database = new FakeGateDatabase();
            var profiles = new DatabaseProfiles(database, null);

            new AdmissionStore(database.PluginCustomData, null).Write(
                Admitting("agent-one", "ops", "ops"));

            Assert.That(Counted(profiles.Clients(), "ops"), Is.EqualTo(1));
        }

        [Test]
        public void Removing_a_profile_leaves_every_record_as_it_was_approved_and_says_who_holds_it()
        {
            // A record keeps every name it was approved for. The gate reads it beside the list, so
            // a name off the list grants nothing, and the operator is told who would regain it.
            var database = new FakeGateDatabase
            {
                Nodes = new TestRuledNodes(new TestRuledNode("a group", "{\"ops\":[\"read\"]}"))
            };
            var profiles = new DatabaseProfiles(database, null);
            var store = new AdmissionStore(database.PluginCustomData, null);

            profiles.Add("ops");
            var both = Admitting("old-agent", "ops", "reader");
            store.Write(both);
            store.Write(Admitting("ops-agent", "ops"));

            Assert.That(profiles.Remove("ops"), Is.Null);

            Assert.That(store.Find(both.AdmissionKey).Profiles, Is.EqualTo(new[] { "ops", "reader" }),
                "the record is exactly what was approved");
            Assert.That(Counted(profiles.Clients(), "ops"), Is.EqualTo(2));
            Assert.That(profiles.LastRemoval, Is.EqualTo(
                "The profile ops is gone, so it grants nothing. The rule naming it stays, inert. "
                + "2 clients still hold it."));
        }

        [Test]
        public void Adding_a_removed_name_back_names_every_client_that_regains_it()
        {
            // Which clients regain a name is the whole of what the operator is agreeing to, so the
            // warning names each of them, as the Clients page calls it, before the rules.
            var database = new FakeGateDatabase
            {
                Nodes = new TestRuledNodes(
                    new TestRuledNode("a group", "{\"ops\":[\"read\"]}"),
                    new TestRuledNode("an entry", "{\"ops\":[\"list\"]}"))
            };
            var profiles = new DatabaseProfiles(database, null);
            var store = new AdmissionStore(database.PluginCustomData, null);

            store.Write(Admitting("ci-runner", "ops"));
            store.Write(Admitting("agent-seven", "ops", "reader"));
            store.Write(Admitting("backup", "reader"));

            Assert.That(profiles.Returning("ops"), Is.EqualTo(
                "2 clients still hold ops from before and will regain it: agent-seven, ci-runner. "
                + "Adding ops again puts the 2 rules naming it back in force."));
        }

        [Test]
        public void A_client_that_renewed_is_one_client_in_the_count_and_the_warning()
        {
            // A renewal leaves a second record with the same client key until the client gives
            // the older up. Counted by record it was two clients holding the profile, and named
            // twice in the warning about adding it back.
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);
            var store = new AdmissionStore(database.PluginCustomData, null);

            foreach (var record in Renewed("agent-renewing", "ops"))
            {
                store.Write(record);
            }

            store.Write(Admitting("ci-runner", "ops"));

            Assert.That(Counted(profiles.Clients(), "ops"), Is.EqualTo(2), "two clients, three records");
            Assert.That(profiles.Returning("ops"), Is.EqualTo(
                "2 clients still hold ops from before and will regain it: agent-renewing, ci-runner."));
        }

        [Test]
        public void Two_programs_paired_under_one_name_are_two_clients_named_apart()
        {
            // A client chooses its own name, so two programs may pair under one, such as one
            // program on two machines. Each is its own client key, so they are two clients in the
            // count and two in the warning, each named with the start of its key.
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);
            var store = new AdmissionStore(database.PluginCustomData, null);

            var first = Admitting("agent", "ops");
            store.Write(first);
            string second;
            using (var other = _authority.IssueClient("agent", new[] { "ops" }))
            {
                var der = other.RawData;
                second = CertificateDigest.Of(der);
                store.Write(new AdmissionRecord(
                    second, "CN=agent", second, new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero),
                    new[] { "ops" }, der));
            }

            var named = new[] { first.ClientKey, second }
                .OrderBy(key => key, StringComparer.Ordinal)
                .Select(key => "agent (" + key.Substring(0, 8) + ")");

            Assert.That(Counted(profiles.Clients(), "ops"), Is.EqualTo(2));
            Assert.That(profiles.Returning("ops"), Is.EqualTo(
                "2 clients still hold ops from before and will regain it: " + string.Join(", ", named) + "."));
        }

        [Test]
        public void A_client_whose_certificate_has_expired_is_not_named_as_regaining_anything()
        {
            // The gate refuses it before its record is read, and the next write the plugin makes
            // prunes the record, so naming it said it would regain a name it never could.
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);
            var store = new AdmissionStore(database.PluginCustomData, null);

            store.Write(Admitting("ci-runner", "ops"));
            store.Write(Expired("agent-gone", "ops"));

            Assert.That(profiles.Returning("ops"),
                Is.EqualTo("One client still holds ops from before and will regain it: ci-runner."));
        }

        [Test]
        public void A_name_only_a_record_holds_still_warns()
        {
            // No rule names it, but a client holds it, and a rule written for it later would reach
            // that client the moment the name is listed.
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes() };
            var profiles = new DatabaseProfiles(database, null);
            new AdmissionStore(database.PluginCustomData, null).Write(Admitting("ci-runner", "ops"));

            Assert.That(profiles.Returning("ops"),
                Is.EqualTo("One client still holds ops from before and will regain it: ci-runner."));
        }

        [Test]
        public void A_closed_database_counts_no_clients_rather_than_throwing()
        {
            var profiles = new DatabaseProfiles(new FakeGateDatabase { IsOpen = false }, null);

            Assert.That(Counted(profiles.Clients(), "ops"), Is.Zero);
        }

        [Test]
        public void Rules_nobody_can_read_are_not_counted()
        {
            var node = new TestRuledNode("a group", "{this will not read");
            var database = new FakeGateDatabase { Nodes = new TestRuledNodes(node) };
            var profiles = new DatabaseProfiles(database, null);
            profiles.Add("ops");

            Assert.That(Counted(profiles.Rules(), "ops"), Is.Zero);
            Assert.That(profiles.Remove("ops"), Is.Null);
            Assert.That(profiles.LastRemoval, Is.EqualTo("The profile ops is gone, so it grants nothing."));
        }

        [Test]
        public void Every_profile_is_counted_in_one_walk()
        {
            // The walk is the cost, over every group and entry, and the page asks for a count on
            // every row.
            var tree = new TestRuledNodes(
                new TestRuledNode("a group", "{\"ops\":[\"read\"],\"reader\":[\"list\"]}"),
                new TestRuledNode("an entry", "{\"ops\":[]}"),
                new TestRuledNode("another", null));
            var profiles = new DatabaseProfiles(new FakeGateDatabase { Nodes = tree }, null);

            var counts = profiles.Rules();

            Assert.That(tree.Walks, Is.EqualTo(1));
            Assert.That(Counted(counts, "ops"), Is.EqualTo(2), "a rule granting nothing still names it");
            Assert.That(Counted(counts, "reader"), Is.EqualTo(1));
            Assert.That(counts.ContainsKey("nobody"), Is.False);
        }

        /// <summary>One profile's count, which is none when the count does not name it.</summary>
        private static int Counted(IReadOnlyDictionary<string, int> counts, string name)
        {
            int found;
            return counts.TryGetValue(name, out found) ? found : 0;
        }

        [Test]
        public void A_closed_database_answers_rather_than_throwing()
        {
            var database = new FakeGateDatabase { IsOpen = false };
            var profiles = new DatabaseProfiles(database, null);

            Assert.That(profiles.Add("ops"), Does.Contain("No database"));
            Assert.That(profiles.Remove("ops"), Does.Contain("No database"));
            Assert.That(Counted(profiles.Rules(), "ops"), Is.Zero);
        }
        /// <summary>
        /// Two records of one client, as a renewal leaves them until the older certificate is
        /// given up: the second carries the client key of the first.
        /// </summary>
        private static AdmissionRecord[] Renewed(string subject, params string[] profiles)
        {
            var records = new AdmissionRecord[2];
            for (var i = 0; i < records.Length; i++)
            {
                using (var certificate = _authority.IssueClient(subject, profiles))
                {
                    var admitted = new DateTimeOffset(2026, 8, 15 + i, 9, 0, 0, TimeSpan.Zero);
                    records[i] = i == 0
                        ? AdmissionRecord.For(certificate, admitted, profiles)
                        : AdmissionRecord.For(certificate, admitted, profiles, records[0].ClientKey);
                }
            }

            return records;
        }

        /// <summary>A record for a certificate that ended yesterday.</summary>
        internal static AdmissionRecord Expired(string subject, params string[] profiles)
        {
            using (var key = System.Security.Cryptography.ECDsa.Create())
            {
                var request = new CertificateRequest(
                    "CN=" + subject, key, System.Security.Cryptography.HashAlgorithmName.SHA256);
                var now = DateTimeOffset.UtcNow;
                using (var certificate = request.CreateSelfSigned(now.AddDays(-30), now.AddDays(-1)))
                {
                    var der = certificate.RawData;
                    return new AdmissionRecord(
                        CertificateDigest.Of(der), "CN=" + subject, CertificateDigest.Of(der), now.AddDays(-30), profiles, der);
                }
            }
        }

        private static AdmissionRecord Admitting(string subject, params string[] profiles)
        {
            X509Certificate2 certificate;

            if (!Issued.TryGetValue(subject, out certificate))
            {
                certificate = _authority.IssueClient(subject, profiles);
                Issued[subject] = certificate;
            }

            var der = certificate.RawData;

            return new AdmissionRecord(
                CertificateDigest.Of(der),
                "CN=" + subject,
                CertificateDigest.Of(der),
                new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero),
                profiles,
                der);
        }
    }
}
