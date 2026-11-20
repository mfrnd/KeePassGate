using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What each act on admissions composes, `STORE.md` §615 and §648: its records, the key they
    /// are under, and what each act is held to at the write.
    /// </summary>
    /// <remarks>
    /// The records' forms are held byte for byte to the measured code by `StoreRecordTests`; what
    /// is held here is which records each act composes from a store as it stands.
    /// </remarks>
    [TestFixture]
    public sealed class AdmissionActsTests
    {
        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero);

        private X509Certificate2 _first;
        private X509Certificate2 _second;
        private X509Certificate2 _third;
        private X509Certificate2 _fourth;

        [OneTimeSetUp]
        public void Issue()
        {
            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();
            _first = issuer.IssueClient(authority, "agent-invented", new[] { "ops" }).Read();
            _second = issuer.IssueClient(authority, "agent-invented", new[] { "ops" }).Read();
            _third = issuer.IssueClient(authority, "agent-invented", new[] { "ops" }).Read();
            _fourth = issuer.IssueClient(authority, "agent-invented", new[] { "ops" }).Read();
        }

        [OneTimeTearDown]
        public void Release()
        {
            _first.Reset();
            _second.Reset();
            _third.Reset();
            _fourth.Reset();
        }

        [Test]
        public void An_admission_is_a_single_record_of_kind_client_holding_its_members_under_the_key_that_stands()
        {
            var store = new ComposedStore(One);
            store.Add(Addition("reader", string.Empty));
            store.Add(Addition("ops", string.Empty));
            var record = AdmissionRecord.For(_second, Then, new[] { "reader", "ops" }, Hex('a'), Key(_first), Key(_first));

            var act = AdmissionActs.Admit(store.Read(), record);

            Assert.That(act.Name, Is.EqualTo(AdmissionActs.AdmitName));
            Assert.That(act.Against, Is.EqualTo(StoreFormat.KeyName(One)));
            Assert.That(act.Records.Select(written => written.Kind), Is.EqualTo(new[] { "client" }), "the list holds each name");
            Assert.That(
                act.Records[0].Members.Where(member => member.Key != "nonce"),
                Is.EquivalentTo(record.Members()),
                "every member of the admission, `profiles` in ordinal order and `from` for a renewal");
            Assert.That(act.Records[0].Member("profiles"), Is.EqualTo("ops reader"));
            Assert.That(act.Records[0].Member("from"), Is.EqualTo(Key(_first)));

            var read = AdmissionRecord.Read(act.Records[0].Hex, act.Records[0].Members);
            Assert.That(read, Is.Not.Null, "read back as it was written");
            Assert.That(read.Members(), Is.EquivalentTo(record.Members()));
        }

        [Test]
        public void An_admission_adds_each_approved_name_the_list_does_not_hold_after_its_record()
        {
            // `STORE.md` §616: the names and the admission are a single act, planned, saved and
            // taken back together; an addition names every removal of its name the reading holds.
            var store = new ComposedStore(One);
            store.Add(Addition("reader", string.Empty));
            var removal = store.Add(Removal("ops"));
            store.Restate(RemovalItem(removal.Hex, "ops"));
            var record = AdmissionRecord.For(_first, Then, new[] { "writer", "reader", "ops" }, Hex('a'));

            var act = AdmissionActs.Admit(store.Read(), record);

            Assert.That(act.Records.Select(written => written.Kind), Is.EqualTo(new[] { "client", "profile", "profile" }));
            Assert.That(act.Records.Skip(1).Select(written => written.Member("name")), Is.EqualTo(new[] { "ops", "writer" }),
                "each name the list does not hold, in ordinal order, and none it holds");
            Assert.That(act.Records[1].Member("after"), Is.EqualTo(removal.Hex), "the removal of a name that was removed");
            Assert.That(act.Records[2].Member("after"), Is.Empty);
        }

        [Test]
        public void An_admission_names_a_removal_of_a_name_it_adds_that_only_a_deletion_record_tells_of()
        {
            // A name approved after its removal is back on the list, whatever is left of the
            // removal but its deletion record, which the reading tells of once the name is known
            // to the file, `STORE.md` §616.
            var store = new ComposedStore(One);
            var removal = Removal("ops");
            store.Deleted.Add(removal.Identifier);
            var reading = store.Read();
            Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty, "a name the file does not know");

            var act = AdmissionActs.Admit(reading, AdmissionRecord.For(_first, Then, new[] { "ops" }, Hex('a')));

            Assert.That(act.Records[1].Member("after"), Is.EqualTo(removal.Hex));
            var applied = StoreWrite.Plan(reading, new[] { act }, GateVersion.Current).Applied(reading.Candidates, reading.Deletions);
            using (var written = StoreReading.Afresh(applied.Key, applied.Value))
            {
                Assert.That(ProfileNames.Of(written).Names, Is.EqualTo(new[] { "ops" }), "on the list once written");
            }
        }

        [Test]
        public void An_admission_adds_every_new_name_however_many_the_list_holds()
        {
            // No ceiling on the list, `STORE.md` §616: a pairing adds every name it approves.
            var store = new ComposedStore(One);
            foreach (var index in Enumerable.Range(0, 100))
            {
                store.Add(Addition("held-" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), string.Empty));
            }

            var asked = Enumerable.Range(0, 32).Select(index => "new-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var act = AdmissionActs.Admit(store.Read(), AdmissionRecord.For(_first, Then, asked, Hex('a')));

            Assert.That(act.Records.Skip(1).Select(written => written.Member("name")), Is.EqualTo(asked));
        }

        [Test]
        public void A_plan_that_leaves_the_admission_out_adds_no_name()
        {
            var store = new ComposedStore(One);
            var act = AdmissionActs.Admit(store.Read(), AdmissionRecord.For(_first, Then, new[] { "writer" }, Hex('b')));
            Assume.That(act.Records.Count(written => written.Kind == "profile"), Is.EqualTo(1));

            var write = StoreWrite.Plan(store.Read(), new[] { act }, GateVersion.Current);

            Assert.That(write.Refused, Is.Not.Null, "its issuer does not stand");
            Assert.That(write.Add, Is.Empty);
        }

        [Test]
        public void A_write_that_adds_no_admission_is_not_one_a_pairing_is_believed_on()
        {
            // A plan adds no record the tree already holds, so an act planned against a store
            // holding its own record, with its name on the list, plans nothing. Believed on a save
            // of nothing, a pairing or a renewal would hand a certificate over with no admission in
            // the file.
            var store = new ComposedStore(One);
            store.Add(Addition("ops", string.Empty));
            var act = AdmissionActs.Admit(store.Read(), AdmissionRecord.For(_first, Then, new[] { "ops" }, Hex('a')));
            Assert.That(act.Records.Select(written => written.Kind), Is.EqualTo(new[] { "client" }), "the list holds the name");

            Assert.That(AdmissionActs.AddsAdmission(StoreWrite.Plan(store.Read(), new[] { act }, GateVersion.Current)), Is.True);

            var written = act.Records[0];
            store.Add(new StoreComposer.Composed(
                written.Identifier,
                new SortedDictionary<string, string>(
                    written.Strings.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal)));
            var write = StoreWrite.Plan(store.Read(), new[] { act }, GateVersion.Current);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Empty, Is.True, "a store that holds the act's own record plans nothing");
            Assert.That(AdmissionActs.AddsAdmission(write), Is.False);
        }

        [Test]
        public void An_admission_is_held_to_its_issuer_standing_alone()
        {
            var record = AdmissionRecord.For(_first, Then, new[] { "ops" }, Hex('a'));
            var act = AdmissionActs.Admit(new ComposedStore(One).Read(), record);

            Assert.That(act.Why(new StoreGround(StoreFormat.KeyName(One), new[] { Hex('a') })), Is.Null);
            Assert.That(act.Why(new StoreGround(StoreFormat.KeyName(One), new[] { Hex('b') })), Is.Not.Null, "another authority stands");
            Assert.That(act.Why(new StoreGround(StoreFormat.KeyName(One), new[] { Hex('a'), Hex('b') })), Is.Not.Null, "none is served");
            Assert.That(act.Why(new StoreGround(StoreFormat.KeyName(One), Array.Empty<string>())), Is.Not.Null, "none stands");
        }

        [Test]
        public void A_revocation_is_a_single_record_holding_the_client_key_alone_with_no_ground()
        {
            var act = AdmissionActs.Revoke(new ComposedStore(One).Read(), Key(_first));

            Assert.That(act.Name, Is.EqualTo(AdmissionActs.RevokeName));
            Assert.That(act.Records.Select(written => written.Kind), Is.EqualTo(new[] { "revoked" }));
            Assert.That(act.Records[0].Members.Keys, Is.EquivalentTo(new[] { "client", "nonce" }));
            Assert.That(act.Records[0].Member("client"), Is.EqualTo(Key(_first)));
            Assert.That(act.Why(new StoreGround(StoreFormat.KeyName(One), Array.Empty<string>())), Is.Null);
        }

        [Test]
        public void A_certificate_taken_away_names_every_renewal_in_force_that_reads_and_its_origin()
        {
            // The client paired with the first, renewed it to the second, and the second to the
            // third and the fourth; the fourth's admission does not read, and is named by none.
            var store = new ComposedStore(One);
            store.Add(Admission(_first, Key(_first), null));
            store.Add(Admission(_second, Key(_first), Key(_first)));
            store.Add(Admission(_third, Key(_first), Key(_second)));
            store.Add(Admission(_fourth, Key(_first), Key(_second), ("der", "not base64")));

            var act = AdmissionActs.TakeAway(store.Read(), Key(_second));

            Assert.That(act.Name, Is.EqualTo(AdmissionActs.TakeAwayName));
            Assert.That(act.Records.Select(written => written.Kind), Is.EqualTo(new[] { "withdrawn" }));
            Assert.That(act.Records[0].Member("certificate"), Is.EqualTo(Key(_second)));
            Assert.That(act.Records[0].Member("renewed"), Is.EqualTo(Key(_third)));
            Assert.That(act.Records[0].Member("from"), Is.EqualTo(Key(_first)));
            Assert.That(act.Why(new StoreGround(StoreFormat.KeyName(One), Array.Empty<string>())), Is.Null, "no ground");
        }

        [Test]
        public void A_certificate_taken_away_names_its_renewals_in_ordinal_order_whatever_refuses_them()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_first, Key(_first), null));
            store.Add(Admission(_second, Key(_first), Key(_first)));
            store.Add(Admission(_third, Key(_first), Key(_first), ("issuer", Hex('b'))));
            store.Add(Admission(_third, Key(_third), Key(_first)));
            var revoked = Key(_fourth);
            store.Add(Admission(_fourth, revoked, Key(_first)));
            store.Add(StoreComposer.Record(One, "revoked", "client:" + revoked, StoreComposer.Content(("client", revoked))));
            store.Restate(StoreComposer.Item(("client", revoked), ("kind", "revoked")));

            var act = AdmissionActs.TakeAway(store.Read(), Key(_first));

            Assert.That(
                act.Records[0].Member("renewed").Split(' '),
                Is.EqualTo(new[] { Key(_second), Key(_third), Key(_fourth) }.OrderBy(key => key, StringComparer.Ordinal)),
                "each once, an issuer not served, several admissions and a revocation among them");
            Assert.That(act.Records[0].Members.ContainsKey("from"), Is.False, "a pairing's certificate holds no origin");
        }

        [Test]
        public void A_certificate_whose_admissions_hold_other_origins_is_taken_away_by_a_record_for_each()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_third, Key(_first), Key(_first)));
            store.Add(Admission(_third, Key(_second), Key(_second)));
            store.Add(Admission(_third, Key(_third), null));
            store.Add(Admission(_fourth, Key(_first), Key(_third)));

            var act = AdmissionActs.TakeAway(store.Read(), Key(_third));

            Assert.That(act.Records.Select(written => written.Kind), Is.All.EqualTo("withdrawn"));
            Assert.That(
                act.Records.Select(written => written.Members.ContainsKey("from") ? written.Member("from") : null),
                Is.EquivalentTo(new[] { Key(_first), Key(_second), null }),
                "an absent origin among them");
            Assert.That(act.Records.Select(written => written.Member("renewed")), Is.All.EqualTo(Key(_fourth)), "each with the same renewals");
        }

        [TestCase("a record")]
        [TestCase("an item")]
        public void A_certificate_taken_away_names_each_certificate_a_statement_took_away_after_it_was_renewed_from_it(string by)
        {
            // The client renewed the first to the second and the second to the third, and the
            // second was taken away: the statement holds the first as its origin, so the first
            // taken away names the second beside the admissions renewed from it, by the record
            // with its item and by the item alone.
            var store = new ComposedStore(One);
            store.Add(Admission(_first, Key(_first), null));
            store.Add(Admission(_fourth, Key(_first), Key(_first)));
            store.Add(Admission(_third, Key(_first), Key(_second)));
            var record = Withdrawn(_second, Key(_third), Key(_first));
            if (by == "a record")
            {
                store.Add(record);
            }

            store.Restate(ItemOf(record, Key(_second), Key(_third), Key(_first)));

            var act = AdmissionActs.TakeAway(store.Read(), Key(_first));

            Assert.That(
                act.Records.Single().Member("renewed").Split(' '),
                Is.EqualTo(new[] { Key(_second), Key(_fourth) }.OrderBy(key => key, StringComparer.Ordinal)));
        }

        [Test]
        public void A_certificate_taken_away_names_nothing_a_deletion_record_alone_tells_of()
        {
            // A deletion record holds no origin, so nothing it tells of is named as renewed from
            // the certificate taken away.
            var store = new ComposedStore(One);
            store.Add(Admission(_first, Key(_first), null));
            store.Add(Admission(_third, Key(_first), Key(_second)));
            store.Deleted.Add(Withdrawn(_second, Key(_third), Key(_first)).Identifier);

            var reading = store.Read();
            Assume.That(reading.InForce.Taken(StoreKinds.Withdrawn), Has.Member(Key(_second)), "the deletion record tells of it");

            Assert.That(AdmissionActs.TakeAway(reading, Key(_first)).Records.Single().Member("renewed"), Is.Empty);
        }

        [Test]
        public void A_certificate_no_admission_names_is_taken_away_with_no_origin_and_no_renewal()
        {
            var act = AdmissionActs.TakeAway(new ComposedStore(One).Read(), Key(_first));

            Assert.That(act.Records, Has.Count.EqualTo(1));
            Assert.That(act.Records[0].Member("renewed"), Is.Empty);
            Assert.That(act.Records[0].Members.ContainsKey("from"), Is.False);
        }

        [Test]
        public void A_certificate_whose_record_does_not_read_is_taken_away_with_the_origin_the_record_holds_and_naming_nothing()
        {
            // The second's record does not read, so the second admits nobody: the statement holds
            // the origin that record names, so that the chain is asked onward from it, and names
            // nothing renewed from the second, which stays refused. A `from` that is no admission
            // key is no origin.
            var store = new ComposedStore(One);
            store.Add(Admission(_first, Key(_first), null));
            store.Add(Admission(_second, Key(_first), Key(_first), ("der", "not base64")));
            store.Add(Admission(_second, Key(_second), null, ("der", "not base64"), ("from", "not an admission key")));
            store.Add(Admission(_third, Key(_first), Key(_second)));

            var act = AdmissionActs.TakeAway(store.Read(), Key(_second));

            Assert.That(act.Records.Select(written => written.Members.ContainsKey("from") ? written.Member("from") : null), Is.EqualTo(new[] { Key(_first) }));
            Assert.That(act.Records.Single().Member("renewed"), Is.Empty, "a certificate that admitted nobody ends nothing renewed from it");
        }

        [Test]
        public void A_store_with_no_key_is_given_no_act()
        {
            var none = StoreReading.Afresh(Array.Empty<StoreCandidate>(), Array.Empty<byte[]>());
            var record = AdmissionRecord.For(_first, Then, new[] { "ops" }, Hex('a'));

            Assert.That(AdmissionActs.Admit(none, record), Is.Null);
            Assert.That(AdmissionActs.Revoke(none, Key(_first)), Is.Null);
            Assert.That(AdmissionActs.TakeAway(none, Key(_first)), Is.Null);
        }

        private static StoreComposer.Composed Admission(
            X509Certificate2 certificate, string client, string from, params (string Name, string Value)[] changes)
        {
            var record = AdmissionRecord.For(certificate, Then, new[] { "ops" }, Hex('a'), client, from);
            var members = record.Members().ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            foreach (var change in changes)
            {
                members[change.Name] = change.Value;
            }

            return StoreComposer.Record(
                One, "client", "client:" + client, StoreComposer.Content(members.Select(member => (member.Key, member.Value)).ToArray()));
        }

        /// <summary>A record of kind `withdrawn` taking this certificate away.</summary>
        private static StoreComposer.Composed Addition(string name, string after)
        {
            return StoreComposer.Record(One, "profile", "profile:" + name, StoreComposer.Content(("after", after), ("name", name)));
        }

        private static StoreComposer.Composed Removal(string name)
        {
            return StoreComposer.Record(One, "removed", "profile:" + name, StoreComposer.Content(("name", name)));
        }

        private static string RemovalItem(string id, string name)
        {
            return StoreComposer.Item(("id", id), ("kind", "removed"), ("name", name));
        }

        private static StoreComposer.Composed Withdrawn(X509Certificate2 certificate, string renewed, string from)
        {
            return StoreComposer.Record(
                One,
                "withdrawn",
                "certificate:" + Key(certificate),
                StoreComposer.Content(("certificate", Key(certificate)), ("from", from), ("renewed", renewed)));
        }

        /// <summary>
        /// The item that restates a record of kind `withdrawn`, with its identifier.
        /// </summary>
        private static string ItemOf(StoreComposer.Composed record, string certificate, string renewed, string from)
        {
            return StoreComposer.Item(
                ("certificate", certificate),
                ("from", from),
                ("id", KeePassGate.Hex.Of(record.Identifier)),
                ("kind", "withdrawn"),
                ("renewed", renewed));
        }

        private static string Key(X509Certificate2 certificate)
        {
            return CertificateDigest.Of(certificate);
        }
    }
}
