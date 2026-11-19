using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Who a store admits, `STORE.md` §615: each rule alone, over stores the test composes, and
    /// the rules that could turn on the order of entries in every order.
    /// </summary>
    /// <remarks>
    /// The authority served is the stand-in `ComposedStore` begins with, filed under `Hex('a')`.
    /// Every admission holds a certificate made here, since an admission reads only where its
    /// `der` decodes to the certificate its `certificate` names; whose authority issued it is the
    /// anchor pin's question, asked before any admission is looked up, and not asked here.
    /// </remarks>
    [TestFixture]
    public sealed class StoreAdmissionsTests
    {
        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero);

        private X509Certificate2 _first;
        private X509Certificate2 _second;
        private X509Certificate2 _third;

        [OneTimeSetUp]
        public void Issue()
        {
            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();
            _first = issuer.IssueClient(authority, "agent-invented", new[] { "ops" }).Read();
            _second = issuer.IssueClient(authority, "agent-invented", new[] { "ops" }).Read();
            _third = issuer.IssueClient(authority, "backup-invented", new[] { "reader" }).Read();
        }

        [OneTimeTearDown]
        public void Release()
        {
            _first.Reset();
            _second.Reset();
            _third.Reset();
        }

        [Test]
        public void An_admission_that_reads_admits_its_certificate_with_what_it_holds()
        {
            var store = new ComposedStore(One);
            var entry = store.Add(Admission(_first, profiles: "ops reader"));

            var admitted = StoreAdmissions.Admitted(store.Read(), Key(_first));

            Assert.That(admitted, Is.Not.Null);
            Assert.That(admitted.Profiles, Is.EqualTo(new[] { "ops", "reader" }));
            Assert.That(admitted.ClientKey, Is.EqualTo(Key(_first)), "a pairing's client key is its own certificate's");
            Assert.That(admitted.Issuer, Is.EqualTo(Hex('a')));
            Assert.That(admitted.Admitted, Is.EqualTo(Then));
            Assert.That(admitted.Entry, Is.EqualTo(entry.Hex), "what an approval held for a renewal is compared with");
            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_second)), Is.Null, "a certificate with no admission");
        }

        [Test]
        public void An_admission_key_is_compared_whole()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_first));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first).ToUpperInvariant()), Is.Null);
            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first).Substring(0, 63)), Is.Null);
            Assert.That(StoreAdmissions.Admitted(store.Read(), string.Empty), Is.Null);
            Assert.That(StoreAdmissions.Admitted(store.Read(), null), Is.Null);
        }

        // Not `client`, the member the record's subject is made of, without which the record is
        // no record of its kind at all and stops the store as damage does, `STORE.md` §658.
        [TestCase("certificate")]
        [TestCase("der")]
        [TestCase("issuer")]
        [TestCase("profiles")]
        [TestCase("subject")]
        [TestCase("admitted")]
        public void A_record_lacking_a_member_admits_nobody_and_stops_nothing(string member)
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_first, without: member));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAdmissions.Admitted(reading, Key(_first)), Is.Null);
        }

        private static IEnumerable<TestCaseData> OutOfForm()
        {
            yield return new TestCaseData("certificate", "upper").SetArgDisplayNames("certificate in upper case");
            yield return new TestCaseData("client", "abc").SetArgDisplayNames("client not a key");
            yield return new TestCaseData("issuer", "short").SetArgDisplayNames("issuer not an id");
            yield return new TestCaseData("from", "abc").SetArgDisplayNames("from not a key");
            yield return new TestCaseData("admitted", "2026-08-15").SetArgDisplayNames("admitted without a time");
            yield return new TestCaseData("admitted", "2026-08-15T09:14:22.000Z").SetArgDisplayNames("admitted in another spelling");
            yield return new TestCaseData("der", "not base64").SetArgDisplayNames("der not base64");
            yield return new TestCaseData("der", "other").SetArgDisplayNames("der of another certificate");
            yield return new TestCaseData("der", "empty").SetArgDisplayNames("der empty");
        }

        [TestCaseSource(nameof(OutOfForm))]
        public void A_record_holding_a_member_out_of_its_form_admits_nobody_and_stops_nothing(string member, string form)
        {
            string value;
            switch (form)
            {
                case "upper":
                    value = Key(_first).ToUpperInvariant();
                    break;
                case "other":
                    value = Convert.ToBase64String(_second.RawData);
                    break;
                case "empty":
                    value = string.Empty;
                    break;
                default:
                    value = form;
                    break;
            }

            var store = new ComposedStore(One);
            store.Add(Admission(_first, change: (member, value)));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAdmissions.Admitted(reading, Key(_first)), Is.Null);
        }

        [Test]
        public void A_member_no_row_names_is_passed_over()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_first, change: ("note", "anything")));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first)), Is.Not.Null);
        }

        [Test]
        public void Profiles_are_read_as_a_set()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_first, profiles: "reader ops reader"));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first)).Profiles, Is.EqualTo(new[] { "ops", "reader" }));
        }

        [Test]
        public void An_issuer_other_than_the_authority_served_admits_nobody()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(_first, issuer: Hex('b')));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first)), Is.Null);
        }

        [Test]
        public void A_store_that_serves_no_authority_admits_nobody()
        {
            // Two authorities standing and none chosen.
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(One, 'b'));
            store.Add(Admission(_first));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first)), Is.Null);
        }

        [Test]
        public void An_admission_beside_a_deletion_record_naming_it_admits_nobody()
        {
            var store = new ComposedStore(One);
            var entry = store.Add(Admission(_first));
            store.Delete(entry);

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first)), Is.Null);
        }

        private static IEnumerable<TestCaseData> Revocations()
        {
            yield return new TestCaseData("a record").SetArgDisplayNames("a record with the item that restates it");
            yield return new TestCaseData("an item").SetArgDisplayNames("an item alone");
            yield return new TestCaseData("a deletion record").SetArgDisplayNames("a deletion record alone");
        }

        [TestCaseSource(nameof(Revocations))]
        public void A_revocation_refuses_every_certificate_under_its_client_key(string by)
        {
            // The second certificate a renewal of the first, under the same client key, as a copy
            // that had not read the revocation renews it.
            var store = new ComposedStore(One);
            store.Add(Admission(_first));
            store.Add(Admission(_second, client: Key(_first), from: Key(_first)));
            store.Add(Admission(_third));
            Revoke(store, Key(_first), by);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAdmissions.Admitted(reading, Key(_first)), Is.Null);
            Assert.That(StoreAdmissions.Admitted(reading, Key(_second)), Is.Null, "renewed in a copy that had not read the revocation");
            Assert.That(StoreAdmissions.Admitted(reading, Key(_third)), Is.Not.Null, "another client");
        }

        [TestCaseSource(nameof(Revocations))]
        public void A_certificate_taken_away_is_refused_while_its_admission_stays_beside_the_statement(string by)
        {
            // What a merge or a copy that has not written since leaves: the admission still in
            // force beside a statement that took its certificate away. The statement refuses it,
            // and nothing else here does.
            var store = new ComposedStore(One);
            store.Add(Admission(_first));
            store.Add(Admission(_second));
            TakeAway(store, Key(_first), by);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAdmissions.Admitted(reading, Key(_first)), Is.Null);
            Assert.That(StoreAdmissions.Admitted(reading, Key(_second)), Is.Not.Null, "another certificate");
            Assert.That(StoreAdmissions.Listing(reading).Waiting, Is.EqualTo(1), "refused, and deleted by the next write");
        }

        [TestCaseSource(nameof(Revocations))]
        public void A_retirement_refuses_every_certificate_its_authority_issued(string by)
        {
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(One, 'b'));
            store.Add(Admission(_first, issuer: Hex('b')));
            Retire(store, Hex('b'), by);

            var reading = store.Read();
            Assume.That(StoreAuthorities.Served(reading), Is.EqualTo(Hex('a')));

            Assert.That(StoreAdmissions.Admitted(reading, Key(_first)), Is.Null);
            Assert.That(StoreAdmissions.Listing(reading).Waiting, Is.EqualTo(1), "refused, and deleted by the next write");
        }

        [Test]
        public void Several_admissions_of_a_certificate_admit_nobody_in_every_order()
        {
            var entries = new[] { Admission(_first, profiles: "ops"), Admission(_first, profiles: "ops reader") };

            foreach (var store in Orders(entries).Select(order => Composed(order)))
            {
                Assert.That(StoreAdmissions.Admitted(store.Read(), Key(_first)), Is.Null);

                var listing = StoreAdmissions.Listing(store.Read());
                Assert.That(listing.Records, Has.Count.EqualTo(2));
                Assert.That(listing.Records.Select(listing.StandingOf), Is.All.EqualTo(AdmissionStanding.Twice));
            }
        }

        [Test]
        public void A_deletion_record_naming_all_but_a_single_admission_of_a_certificate_admits_by_that_admission()
        {
            var kept = Admission(_first, profiles: "ops");
            var deleted = Admission(_first, profiles: "ops reader");

            foreach (var store in Orders(new[] { kept, deleted }).Select(order => Composed(order)))
            {
                store.Delete(deleted);

                var admitted = StoreAdmissions.Admitted(store.Read(), Key(_first));
                Assert.That(admitted, Is.Not.Null);
                Assert.That(admitted.Entry, Is.EqualTo(kept.Hex));
            }
        }

        [Test]
        public void Revoking_the_client_of_an_admission_of_a_certificate_two_admissions_name_leaves_it_admitted_under_the_other()
        {
            // `STORE.md` §615: an admission a decision refuses is not counted among several, so the
            // answer is the same before the write that deletes it and after.
            var kept = Admission(_first, client: Key(_third), profiles: "ops");
            var refused = Admission(_first, profiles: "ops reader");

            foreach (var store in Orders(new[] { kept, refused }).Select(order => Composed(order)))
            {
                Revoke(store, Key(_first), "a record");

                var before = store.Read();
                var admitted = StoreAdmissions.Admitted(before, Key(_first));
                Assert.That(admitted, Is.Not.Null, "before the write");
                Assert.That(admitted.ClientKey, Is.EqualTo(Key(_third)));

                var write = StoreWrite.Plan(before, new[] { Neutral() }, "0.3.0");
                Assume.That(write.Delete, Does.Contain(refused.Hex));
                var applied = write.Applied(before.Candidates, before.Deletions);
                var after = StoreReading.Afresh(applied.Key, applied.Value);
                Assume.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);

                Assert.That(StoreAdmissions.Admitted(after, Key(_first)).ClientKey, Is.EqualTo(Key(_third)), "after the write");

                var listing = StoreAdmissions.Listing(before);
                Assert.That(listing.Records.Select(record => record.Entry), Is.EqualTo(new[] { kept.Hex }));
                Assert.That(listing.StandingOf(listing.Records[0]), Is.EqualTo(AdmissionStanding.Admitted));
            }
        }

        [Test]
        public void A_record_that_does_not_read_beside_an_admission_leaves_it_admitted_in_every_order()
        {
            var reads = Admission(_first);
            var unread = Admission(_first, change: ("der", "not base64"));

            foreach (var reading in Orders(new[] { reads, unread }).Select(order => Composed(order).Read()))
            {
                var admitted = StoreAdmissions.Admitted(reading, Key(_first));
                Assert.That(admitted, Is.Not.Null);
                Assert.That(admitted.Entry, Is.EqualTo(reads.Hex));

                var listing = StoreAdmissions.Listing(reading);
                Assert.That(listing.Records.Select(record => record.Entry), Is.EqualTo(new[] { reads.Hex }));
                Assert.That(listing.StandingOf(listing.Records[0]), Is.EqualTo(AdmissionStanding.Admitted));
                Assert.That(listing.Unreadable.Select(row => row.Entry), Is.EqualTo(new[] { unread.Hex }));
                Assert.That(listing.Unreadable[0].Certificate, Is.EqualTo(Key(_first)));
            }
        }

        [Test]
        public void A_listing_holds_each_admission_with_what_it_comes_to_newest_first()
        {
            var store = new ComposedStore(One);
            store.Add(StoreRecords.Authority(One, 'b'));
            var older = store.Add(Admission(_first, admitted: Then));
            var newer = store.Add(Admission(_third, admitted: Then.AddDays(1)));
            store.Add(Admission(_second, issuer: Hex('b'), admitted: Then.AddDays(2)));
            var latest = store.Add(Admission(_second, client: Key(_third), admitted: Then.AddDays(3)));
            Retire(store, Hex('b'), "a record");

            var listing = StoreAdmissions.Listing(store.Read());

            Assert.That(listing.Complete, Is.True);
            Assert.That(
                listing.Records.Select(record => record.Entry),
                Is.EqualTo(new[] { latest.Hex, newer.Hex, older.Hex }),
                "the admission the retirement refuses is not listed");
            Assert.That(listing.Records.Select(listing.StandingOf), Is.All.EqualTo(AdmissionStanding.Admitted));
            Assert.That(listing.Waiting, Is.EqualTo(1), "the client of the refused admission");
        }

        [Test]
        public void A_listing_of_a_store_that_cannot_be_judged_lists_nobody_and_says_it_could_not()
        {
            Assert.That(StoreAdmissions.Listing(null).Complete, Is.False);

            var empty = StoreReading.Afresh(Array.Empty<StoreCandidate>(), Array.Empty<byte[]>());
            Assume.That(empty.State, Is.EqualTo(StoreState.None));
            Assert.That(StoreAdmissions.Listing(empty).Complete, Is.True, "no store: nobody can be admitted, and that is the whole answer");
            Assert.That(StoreAdmissions.Listing(empty).Records, Is.Empty);
        }

        [Test]
        public void The_certificates_named_are_those_of_every_record_of_kind_client_in_force_less_a_write_s_deletions_and_with_its_additions()
        {
            var store = new ComposedStore(One);
            var gone = store.Add(Admission(_first));
            store.Add(Admission(_second, change: ("der", "not base64")));
            Revoke(store, Key(_first), "a record");

            var reading = store.Read();
            Assert.That(StoreAdmissions.Named(reading, null), Is.EquivalentTo(new[] { Key(_first), Key(_second) }));

            var write = StoreWrite.Plan(reading, new[] { Neutral() }, "0.3.0");
            Assume.That(write.Delete, Does.Contain(gone.Hex));
            Assert.That(StoreAdmissions.Named(reading, write), Is.EquivalentTo(new[] { Key(_second) }));
        }

        /// <summary>
        /// A record of kind `client` holding an admission of this certificate, as the product
        /// writes it.
        /// </summary>
        private static StoreComposer.Composed Admission(
            X509Certificate2 certificate,
            string client = null,
            string issuer = null,
            string from = null,
            string profiles = "ops",
            DateTimeOffset? admitted = null,
            string without = null,
            (string Name, string Value)? change = null)
        {
            var record = AdmissionRecord.For(
                certificate,
                admitted ?? Then,
                profiles.Split(' '),
                issuer ?? Hex('a'),
                client ?? Key(certificate),
                from);
            var members = record.Members().ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);

            // As written, so that a test of the reader holds names a writer would not write.
            members[StoreKinds.Profiles] = profiles;
            if (without != null)
            {
                members.Remove(without);
            }

            if (change != null)
            {
                members[change.Value.Name] = change.Value.Value;
            }

            // The subject is made of the `client` the content holds, as the core reads it.
            return StoreComposer.Record(
                One, "client", "client:" + members[StoreKinds.ClientMember],
                StoreComposer.Content(members.Select(member => (member.Key, member.Value)).ToArray()));
        }

        private static string Key(X509Certificate2 certificate)
        {
            return CertificateDigest.Of(certificate);
        }

        /// <summary>
        /// A revocation of this client key, by a record and its item, an item, or a deletion
        /// record.
        /// </summary>
        private static void Revoke(ComposedStore store, string client, string by)
        {
            var item = StoreComposer.Item(("client", client), ("kind", "revoked"));
            switch (by)
            {
                case "a record":
                    store.Add(StoreComposer.Record(One, "revoked", "client:" + client, StoreComposer.Content(("client", client))));
                    store.Restate(item);
                    break;
                case "an item":
                    store.Restate(item);
                    break;
                default:
                    store.Deleted.Add(StoreComposer.IdentifierOf(One, "revoked", "client:" + client, 0, new string('5', 64)));
                    break;
            }
        }

        /// <summary>
        /// A statement taking this certificate away, by a record and its item, an item, or a
        /// deletion record, naming no renewal.
        /// </summary>
        private static void TakeAway(ComposedStore store, string certificate, string by)
        {
            switch (by)
            {
                case "a record":
                    var record = StoreComposer.Record(
                        One, "withdrawn", "certificate:" + certificate, StoreComposer.Content(("certificate", certificate), ("renewed", string.Empty)));
                    store.Add(record);
                    store.Restate(StoreComposer.Item(
                        ("certificate", certificate), ("id", KeePassGate.Hex.Of(record.Identifier)), ("kind", "withdrawn"), ("renewed", string.Empty)));
                    break;
                case "an item":
                    store.Restate(StoreComposer.Item(
                        ("certificate", certificate), ("id", new string('6', 32)), ("kind", "withdrawn"), ("renewed", string.Empty)));
                    break;
                default:
                    store.Deleted.Add(StoreComposer.IdentifierOf(One, "withdrawn", "certificate:" + certificate, 0, new string('5', 64)));
                    break;
            }
        }

        /// <summary>
        /// A retirement of this authority, by a record and its item, an item, or a deletion record.
        /// </summary>
        private static void Retire(ComposedStore store, string authority, string by)
        {
            var item = StoreComposer.Item(("authority", authority), ("kind", "retired"));
            switch (by)
            {
                case "a record":
                    store.Add(StoreComposer.Record(One, "retired", "authority:" + authority, StoreComposer.Content(("authority", authority))));
                    store.Restate(item);
                    break;
                case "an item":
                    store.Restate(item);
                    break;
                default:
                    store.Deleted.Add(StoreComposer.IdentifierOf(One, "retired", "authority:" + authority, 0, new string('5', 64)));
                    break;
            }
        }

        /// <summary>
        /// A store holding these records beside its authority and summary entry, in this order.
        /// </summary>
        private static ComposedStore Composed(IEnumerable<StoreComposer.Composed> records)
        {
            var store = new ComposedStore(One);
            foreach (var record in records)
            {
                store.Add(record);
            }

            return store;
        }

        /// <summary>Every order of these records.</summary>
        private static IEnumerable<IReadOnlyList<StoreComposer.Composed>> Orders(IReadOnlyList<StoreComposer.Composed> records)
        {
            if (records.Count <= 1)
            {
                yield return records;
                yield break;
            }

            for (var first = 0; first < records.Count; first++)
            {
                var rest = records.Where((record, at) => at != first).ToList();
                foreach (var order in Orders(rest))
                {
                    yield return new[] { records[first] }.Concat(order).ToList();
                }
            }
        }

        /// <summary>
        /// A write that adds nothing of its own: a retirement of an authority nobody holds.
        /// </summary>
        private static StoreAct Neutral()
        {
            var record = StoreRecord.Compose(One, "retired", new Dictionary<string, string> { { "authority", Hex('9') } });
            return new StoreAct("a write", One, StoreFormat.KeyName(One), new[] { record }, null);
        }
    }
}
