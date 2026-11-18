using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A renewal made from a certificate that was taken away, `STORE.md` §648 rules 3 to 5: who
    /// the chain of origins of an admission lets count, over stores the test composes.
    /// </summary>
    /// <remarks>
    /// **The certificates are copies of a single issued certificate with their serial numbers
    /// changed**: each decodes as a certificate of its own with a digest of its own, which is all
    /// an admission is read by, and none of them is signed again, so a chain longer than a summary
    /// entry can restate costs no signature. The authority served is the stand-in
    /// `ComposedStore` begins with, filed under `Hex('a')`.
    /// </remarks>
    [TestFixture]
    public sealed class StoreAdmissionChainTests
    {
        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero);

        private static byte[] _template;

        /// <summary>Where the template's serial number ends.</summary>
        private static int _serialEnd;

        [OneTimeSetUp]
        public static void Issue()
        {
            var issuer = new AuthorityIssuer();
            _template = issuer.IssueClient(issuer.CreateAuthority(), "agent-invented", new[] { "ops" }).CertificateDer;

            var at = Inside(_template, Inside(_template, 0));
            if (_template[at] == 0xA0)
            {
                at = Over(_template, at);
            }

            Assert.That(_template[at], Is.EqualTo(0x02), "the serial number");
            Assert.That(_template[at + 1], Is.InRange(5, 0x7F), "a serial long enough to number");
            _serialEnd = at + 2 + _template[at + 1];
        }

        private static IEnumerable<TestCaseData> Carriers()
        {
            yield return new TestCaseData("a record").SetArgDisplayNames("a record with the item that restates it");
            yield return new TestCaseData("an item").SetArgDisplayNames("an item alone");
        }

        [TestCaseSource(nameof(Carriers))]
        public void A_renewal_a_statement_names_is_admitted_and_a_renewal_it_does_not_name_is_not(string by)
        {
            var store = new Chained();
            store.Add(Admission(1, from: 0));
            store.Add(Admission(2, from: 0));
            store.TakeAway(0, new[] { 1 }, null, by);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAdmissions.Admitted(reading, Key(1)), Is.Not.Null, "named");
            Assert.That(StoreAdmissions.Admitted(reading, Key(2)), Is.Null, "not named");

            var listing = StoreAdmissions.Listing(reading);
            Assert.That(listing.StandingOf(listing.Records.Single(record => record.AdmissionKey == Key(2))),
                Is.EqualTo(AdmissionStanding.Uncounted), "the page reads it as the gate does");
            Assert.That(listing.StandingOf(listing.Records.Single(record => record.AdmissionKey == Key(1))),
                Is.EqualTo(AdmissionStanding.Admitted));
        }

        [Test]
        public void A_renewal_onward_from_a_renewal_that_does_not_count_is_not_admitted_also_once_the_certificate_between_was_given_up()
        {
            // The shape a writer leaves where the statement about the first certificate was
            // written before anything renewed from it reached its copy: the renewal and the
            // statement about the certificate between came from another copy after the first was
            // taken away. A writer whose copy held that statement names the certificate between
            // in the statement it writes about the first, and leaves no such shape.
            var store = new Chained();
            store.TakeAway(0, Array.Empty<int>(), null, "a record");
            var between = store.Add(Admission(1, from: 0));
            store.Add(Admission(2, from: 1));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Null, "renewed from a certificate taken away without naming it");
            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(2)), Is.Null, "and onward from it");

            // The certificate between given up at a gate whose copy holds the renewal onward: its
            // statement names that renewal and holds its own origin, and its admission is deleted.
            store.Drop(between);
            store.TakeAway(1, new[] { 2 }, 0, "a record");

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(2)), Is.Null,
                "the chain goes back through the statement's own origin to the certificate first taken away");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Certificates_taken_away_in_either_order_leave_the_renewal_after_them_admitted(bool betweenFirst)
        {
            // A client paired, renewed twice, and then the first two certificates taken away, by
            // the product's own act through a store that writes: the newest certificate is named
            // by each statement on its way back to the pairing, whichever certificate went first.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            var paired = Admit(store, null);
            var between = Admit(store, paired);
            var newest = Admit(store, between);

            foreach (var taken in betweenFirst ? new[] { between, paired } : new[] { paired, between })
            {
                StoreSeeds.TakeAway(store, taken.AdmissionKey);
            }

            using (var reading = store.Read())
            {
                Assert.That(StoreAdmissions.Admitted(reading, newest.AdmissionKey), Is.Not.Null, "the certificate neither act was about");
                Assert.That(StoreAdmissions.Admitted(reading, between.AdmissionKey), Is.Null);
                Assert.That(StoreAdmissions.Admitted(reading, paired.AdmissionKey), Is.Null);
            }
        }

        [Test]
        public void A_renewal_onward_from_a_certificate_whose_record_does_not_read_stays_refused_once_that_certificate_is_taken_away()
        {
            // A client paired, renewed to a certificate whose record does not read, renewed onward
            // from it twice, and gave the first of those up. The certificate whose record does not
            // read admitted nobody, so taking it away ends nothing renewed from it: its statement
            // holds the origin its record names and names nothing, and what was renewed onward
            // stays refused.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            var paired = Admit(store, null);
            var unread = Unread(store, paired);
            var between = Admit(store, unread);
            var onward = Admit(store, between);
            StoreSeeds.TakeAway(store, between.AdmissionKey);
            using (var reading = store.Read())
            {
                Assume.That(StoreAdmissions.Admitted(reading, onward.AdmissionKey), Is.Null, "renewed from a certificate that admits nobody");
            }

            StoreSeeds.TakeAway(store, unread.AdmissionKey);

            using (var reading = store.Read())
            {
                Assert.That(StoreAdmissions.Admitted(reading, onward.AdmissionKey), Is.Null);
                Assert.That(StoreAdmissions.Admitted(reading, paired.AdmissionKey), Is.Not.Null, "the certificate it was renewed from");
            }
        }

        [Test]
        public void A_renewal_from_a_certificate_only_a_deletion_record_tells_of_is_not_admitted()
        {
            var store = new Chained();
            store.Add(Admission(1, from: 0));
            store.TakeAway(0, Array.Empty<int>(), null, "a deletion record");

            var reading = store.Read();

            Assume.That(reading.InForce.Taken(StoreKinds.Withdrawn), Has.Member(Key(0)), "the renewal's `from` tells which certificate");
            Assert.That(StoreAdmissions.Admitted(reading, Key(1)), Is.Null, "a statement whose content is nowhere names no renewal");
        }

        [Test]
        public void A_deletion_record_of_a_record_an_item_restates_changes_nothing()
        {
            var store = new Chained();
            store.Add(Admission(1, from: 0));
            var record = store.Statement(0, new[] { 1 }, null);
            store.Restating(record);
            store.Deleting(record.Identifier);

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Not.Null);
        }

        [Test]
        public void An_admission_whose_certificate_of_origin_is_nowhere_is_not_admitted()
        {
            var store = new Chained();
            store.Add(Admission(1, from: 9));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Null, "neither admitted nor taken away");

            store.Add(Admission(9));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Not.Null, "its origin admitted");
        }

        [Test]
        public void Statements_of_a_certificate_holding_other_origins_let_a_renewal_count_only_where_each_origin_passes()
        {
            var store = new Chained();
            store.Add(Admission(0));
            store.Add(Admission(2, from: 1));
            store.TakeAway(1, new[] { 2 }, 0, "a record");
            store.TakeAway(1, new[] { 2 }, 5, "a record");

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(2)), Is.Null, "an origin that is nowhere");

            store.Add(Admission(5));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(2)), Is.Not.Null, "each origin admitted");
        }

        [Test]
        public void Where_several_statements_took_a_certificate_away_a_renewal_each_names_counts_and_a_renewal_any_does_not_name_does_not_in_every_order()
        {
            var records = new List<StoreComposer.Composed>
            {
                Admission(1, from: 0),
                Admission(2, from: 0),
                Statement(0, new[] { 1, 2 }, null),
                Statement(0, new[] { 1 }, null),
            };

            foreach (var order in Orders(records))
            {
                var store = new Chained();
                foreach (var record in order)
                {
                    store.Add(record);
                    if (record.Strings["KPG.Kind"] == "withdrawn")
                    {
                        store.Restating(record);
                    }
                }

                var reading = store.Read();
                Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(StoreAdmissions.Admitted(reading, Key(1)), Is.Not.Null, "named by each");
                Assert.That(StoreAdmissions.Admitted(reading, Key(2)), Is.Null, "a statement does not name it");
            }
        }

        [Test]
        public void A_certificate_taken_away_while_its_admission_is_in_force_is_asked_of_both_origins()
        {
            // A copy that received the statement by a merge and has not written since: the
            // admission of the certificate taken away is still there, and its `from` is asked
            // beside the statement's, which only refuses.
            var asked = new Chained();
            asked.Add(Admission(0));
            asked.Add(Admission(1, from: 9));
            asked.Add(Admission(2, from: 1));
            asked.TakeAway(1, new[] { 2 }, 0, "a record");

            Assert.That(StoreAdmissions.Admitted(asked.Read(), Key(2)), Is.Null, "the admission's origin is nowhere");

            var agreeing = new Chained();
            agreeing.Add(Admission(0));
            agreeing.Add(Admission(1, from: 0));
            agreeing.Add(Admission(2, from: 1));
            agreeing.TakeAway(1, new[] { 2 }, 0, "a record");

            Assert.That(StoreAdmissions.Admitted(agreeing.Read(), Key(2)), Is.Not.Null, "both origins pass");

            var deleted = new Chained();
            deleted.Add(Admission(0));
            deleted.Add(Admission(2, from: 1));
            deleted.TakeAway(1, new[] { 2 }, 0, "a record");

            Assert.That(StoreAdmissions.Admitted(deleted.Read(), Key(2)), Is.Not.Null, "once the admission is deleted, the statement alone");
        }

        [Test]
        public void A_chain_that_leads_back_into_itself_admits_nobody()
        {
            var admissions = new Chained();
            admissions.Add(Admission(1, from: 2));
            admissions.Add(Admission(2, from: 1));

            Assert.That(StoreAdmissions.Admitted(admissions.Read(), Key(1)), Is.Null);
            Assert.That(StoreAdmissions.Admitted(admissions.Read(), Key(2)), Is.Null);

            var statements = new Chained();
            statements.Add(Admission(2, from: 1));
            statements.TakeAway(1, new[] { 2 }, 2, "a record");

            Assert.That(StoreAdmissions.Admitted(statements.Read(), Key(2)), Is.Null, "through a statement's origin");
        }

        [Test]
        public void A_renewal_from_a_certificate_several_admissions_name_and_that_was_not_taken_away_is_not_admitted_in_every_order()
        {
            var records = new List<StoreComposer.Composed>
            {
                Admission(0),
                Admission(0, client: 8),
                Admission(1, from: 0),
            };

            foreach (var order in Orders(records))
            {
                var store = new Chained();
                foreach (var record in order)
                {
                    store.Add(record);
                }

                Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Null, "its origin is admitted by none of them");
            }
        }

        [Test]
        public void A_renewal_from_a_certificate_several_admissions_name_that_was_taken_away_is_answered_by_the_statements()
        {
            var store = new Chained();
            var first = store.Add(Admission(0));
            var second = store.Add(Admission(0, client: 8));
            store.Add(Admission(1, from: 0));
            store.TakeAway(0, new[] { 1 }, null, "a record");

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Not.Null, "before the write that deletes its admissions");

            store.Drop(first);
            store.Drop(second);

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Not.Null, "and after it");
        }

        [Test]
        public void A_chain_of_admissions_several_times_longer_than_the_ceiling_on_items_is_answered_without_recursion()
        {
            // Each renewal a client makes and keeps adds a step, and a store holds far more
            // admissions than a summary entry holds items. Followed by a recursion, a chain this
            // long ends the process rather than failing the test.
            var length = 3 * StoreFormat.MaxItems;
            var store = new Chained();
            store.Add(Admission(0));
            for (var at = 1; at <= length; at++)
            {
                store.Add(Admission(at, from: at - 1));
            }

            var reading = store.Read();

            Assert.That(StoreAdmissions.Admitted(reading, Key(length)), Is.Not.Null);

            store.Add(Admission(0, client: 8));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(length)), Is.Null, "its first certificate listed twice ends every step after it");
        }

        [Test]
        public void A_renewal_from_a_certificate_admitted_under_another_authority_is_not_admitted()
        {
            // The single admission of the origin names an authority that is not served, so the
            // origin is admitted by nothing the gate serves.
            var store = new Chained();
            store.Add(Admission(0, issuer: Hex('b')));
            store.Add(Admission(1, from: 0));

            Assert.That(StoreAdmissions.Admitted(store.Read(), Key(1)), Is.Null);
        }

        [Test]
        public void A_renewal_from_a_certificate_whose_client_was_revoked_is_not_admitted()
        {
            // The origin admitted under a client key of its own, which a revocation names: the
            // renewal's own client key is not revoked, and its origin admits nobody all the same.
            var store = new Chained();
            store.Add(Admission(0, client: 8));
            store.Add(Admission(1, from: 0));
            store.Revoke(8);

            var reading = store.Read();

            Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAdmissions.Admitted(reading, Key(1)), Is.Null);
        }

        [Test]
        public void A_chain_through_as_many_statements_as_a_summary_entry_holds_is_answered_at_each_step()
        {
            // Every certificate between the first and the last taken away by a statement naming
            // the next and holding the certificate before, each statement restated by an item, as
            // many as a store holds beside the item stating its format and still has room for its
            // next write.
            var length = StoreFormat.MaxItems - 2;
            var named = new Chained();
            var broken = new Chained();
            foreach (var store in new[] { named, broken })
            {
                store.Add(Admission(0));
                store.Add(Admission(length + 1, from: length));
            }

            for (var at = 1; at <= length; at++)
            {
                named.TakeAway(at, new[] { at + 1 }, at - 1, "an item");
                broken.TakeAway(at, at == length / 2 ? Array.Empty<int>() : new[] { at + 1 }, at - 1, "an item");
            }

            var reading = named.Read();
            Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            Assert.That(StoreAdmissions.Admitted(reading, Key(length + 1)), Is.Not.Null, "each statement names the next");
            Assert.That(StoreAdmissions.Admitted(broken.Read(), Key(length + 1)), Is.Null, "a statement in the middle names none");
        }

        [Test]
        public void A_statement_whose_renewed_is_absent_names_no_renewal()
        {
            // An item alone, restating a record of kind `withdrawn` that holds no `renewed`, which
            // the form lets it leave out.
            var store = new Chained();
            store.Add(Admission(1, from: 0));
            var record = StoreComposer.Record(One, "withdrawn", "certificate:" + Key(0), StoreComposer.Content(("certificate", Key(0))));
            store.RestatingItem(StoreComposer.Item(
                ("certificate", Key(0)), ("id", KeePassGate.Hex.Of(record.Identifier)), ("kind", "withdrawn")));

            var reading = store.Read();

            Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assume.That(reading.InForce.Taken(StoreKinds.Withdrawn), Has.Member(Key(0)));
            Assert.That(StoreAdmissions.Admitted(reading, Key(1)), Is.Null);
        }

        [TestCase("nowhere")]
        [TestCase("a loop")]
        [TestCase("listed twice")]
        [TestCase("further back")]
        public void The_page_reads_a_renewal_whose_chain_fails_as_the_gate_does(string failing)
        {
            var store = new Chained();
            switch (failing)
            {
                case "nowhere":
                    store.Add(Admission(2, from: 9));
                    break;
                case "a loop":
                    store.Add(Admission(1, from: 2));
                    store.Add(Admission(2, from: 1));
                    break;
                case "listed twice":
                    store.Add(Admission(0));
                    store.Add(Admission(0, client: 8));
                    store.Add(Admission(2, from: 0));
                    break;
                default:
                    store.TakeAway(0, Array.Empty<int>(), null, "a record");
                    store.Add(Admission(1, from: 0));
                    store.Add(Admission(2, from: 1));
                    break;
            }

            var reading = store.Read();
            var listing = StoreAdmissions.Listing(reading);

            Assert.That(listing.StandingOf(listing.Records.Single(record => record.AdmissionKey == Key(2))),
                Is.EqualTo(AdmissionStanding.Uncounted));
            Assert.That(StoreAdmissions.Admitted(reading, Key(2)), Is.Null, "as the gate reads it");
        }

        /// <summary>
        /// A record of kind `client` admitting the certificate of this number, under the client key
        /// of the certificate numbered 0 unless another is given, renewed from the certificate
        /// numbered as `from`, issued by the authority served unless another is given.
        /// </summary>
        private static StoreComposer.Composed Admission(int number, int? client = null, int? from = null, string issuer = null)
        {
            var der = Certificate(number);
            var clientKey = Key(client ?? 0);
            var members = new List<(string Name, string Value)>
            {
                ("admitted", Moments.Wire(Then)),
                ("certificate", CertificateDigest.Of(der)),
                ("client", clientKey),
                ("der", Convert.ToBase64String(der)),
                ("issuer", issuer ?? Hex('a')),
                ("profiles", "ops"),
                ("subject", "CN=agent-invented"),
            };
            if (from != null)
            {
                members.Add(("from", Key(from.Value)));
            }

            return StoreComposer.Record(One, "client", "client:" + clientKey, StoreComposer.Content(members.ToArray()));
        }

        /// <summary>
        /// An admission of a certificate the store's authority issues, written by the product's
        /// own act: a pairing's, or a renewal's from the origin given.
        /// </summary>
        private static AdmissionRecord Admit(MemoryStore store, AdmissionRecord origin)
        {
            var served = StoreSeeds.Served(store);
            using (var certificate = new AuthorityIssuer().IssueClient(StoreSeeds.KeyOf(store, served), "agent-invented", new[] { "ops" }).Read())
            {
                var record = origin == null
                    ? AdmissionRecord.For(certificate, Then, new[] { "ops" }, served)
                    : AdmissionRecord.For(certificate, Then, origin.Profiles, served, origin.ClientKey, origin.AdmissionKey);
                StoreSeeds.Admit(store, record);
                return record;
            }
        }

        /// <summary>
        /// A renewal from the origin given whose record does not read: an admission of nobody that
        /// still names its certificate, its client and its origin.
        /// </summary>
        private static AdmissionRecord Unread(MemoryStore store, AdmissionRecord origin)
        {
            var served = StoreSeeds.Served(store);
            using (var certificate = new AuthorityIssuer().IssueClient(StoreSeeds.KeyOf(store, served), "agent-invented", new[] { "ops" }).Read())
            {
                var record = AdmissionRecord.For(certificate, Then, origin.Profiles, served, origin.ClientKey, origin.AdmissionKey);
                var members = record.Members().ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
                members["der"] = "not base64";
                StoreSeeds.Client(store, members);
                return record;
            }
        }

        /// <summary>
        /// A record of kind `withdrawn` taking the certificate of this number away.
        /// </summary>
        private static StoreComposer.Composed Statement(int number, IEnumerable<int> renewed, int? from)
        {
            var members = new List<(string Name, string Value)>
            {
                ("certificate", Key(number)),
                ("renewed", Renewed(renewed)),
            };
            if (from != null)
            {
                members.Add(("from", Key(from.Value)));
            }

            return StoreComposer.Record(One, "withdrawn", "certificate:" + Key(number), StoreComposer.Content(members.ToArray()));
        }

        /// <summary>
        /// The item that restates a record of kind `withdrawn`, with its identifier.
        /// </summary>
        private static string ItemOf(StoreComposer.Composed record)
        {
            var content = Newtonsoft.Json.Linq.JObject.Parse(record.Strings["KPG.Data"]);
            var members = new List<(string Name, string Value)>
            {
                ("certificate", (string)content["certificate"]),
                ("id", KeePassGate.Hex.Of(record.Identifier)),
                ("kind", "withdrawn"),
                ("renewed", (string)content["renewed"]),
            };
            if (content["from"] != null)
            {
                members.Add(("from", (string)content["from"]));
            }

            return StoreComposer.Item(members.ToArray());
        }

        private static string Renewed(IEnumerable<int> renewed)
        {
            return string.Join(" ", renewed.Select(Key).OrderBy(key => key, StringComparer.Ordinal));
        }

        /// <summary>The admission key of the certificate of this number.</summary>
        private static string Key(int number)
        {
            return CertificateDigest.Of(Certificate(number));
        }

        /// <summary>
        /// The certificate of this number: the template with the last four bytes of its serial
        /// number set to the number, which leaves the serial's length and sign as they were.
        /// </summary>
        private static byte[] Certificate(int number)
        {
            var der = (byte[])_template.Clone();
            der[_serialEnd - 4] = (byte)(number >> 24);
            der[_serialEnd - 3] = (byte)(number >> 16);
            der[_serialEnd - 2] = (byte)(number >> 8);
            der[_serialEnd - 1] = (byte)number;
            return der;
        }

        /// <summary>Where the contents of the element at this place begin.</summary>
        private static int Inside(byte[] der, int at)
        {
            var length = der[at + 1];
            return at + 2 + (length < 0x80 ? 0 : length & 0x7F);
        }

        /// <summary>Where the element after the element at this place begins.</summary>
        private static int Over(byte[] der, int at)
        {
            var length = der[at + 1];
            int size = length;
            if (length >= 0x80)
            {
                size = 0;
                for (var index = 0; index < (length & 0x7F); index++)
                {
                    size = (size << 8) | der[at + 2 + index];
                }
            }

            return Inside(der, at) + size;
        }

        private static IEnumerable<IReadOnlyList<StoreComposer.Composed>> Orders(IReadOnlyList<StoreComposer.Composed> records)
        {
            if (records.Count <= 1)
            {
                yield return records;
                yield break;
            }

            for (var first = 0; first < records.Count; first++)
            {
                var rest = records.Where((record, index) => index != first).ToList();
                foreach (var order in Orders(rest))
                {
                    yield return new[] { records[first] }.Concat(order).ToList();
                }
            }
        }

        /// <summary>
        /// The records, deletion records and items of a store, composed afresh for each reading
        /// with the authority served and a single summary entry over every item.
        /// </summary>
        private sealed class Chained
        {
            private readonly List<StoreComposer.Composed> _records = new List<StoreComposer.Composed>();
            private readonly List<byte[]> _deleted = new List<byte[]>();
            private readonly List<string> _items = new List<string>();

            public StoreComposer.Composed Add(StoreComposer.Composed record)
            {
                _records.Add(record);
                return record;
            }

            public void Drop(StoreComposer.Composed record)
            {
                _records.RemoveAll(added => added.Identifier.SequenceEqual(record.Identifier));
            }

            /// <summary>A deletion record of this identifier.</summary>
            public void Deleting(byte[] identifier)
            {
                _deleted.Add(identifier);
            }

            /// <summary>The item that restates this record, without the record.</summary>
            public void Restating(StoreComposer.Composed record)
            {
                _items.Add(ItemOf(record));
            }

            /// <summary>An item as it is given, with no record.</summary>
            public void RestatingItem(string item)
            {
                _items.Add(item);
            }

            /// <summary>
            /// A revocation of the client key of the certificate of this number, by a record with
            /// the item that restates it.
            /// </summary>
            public void Revoke(int client)
            {
                Add(StoreComposer.Record(One, "revoked", "client:" + Key(client), StoreComposer.Content(("client", Key(client)))));
                _items.Add(StoreComposer.Item(("client", Key(client)), ("kind", "revoked")));
            }

            /// <summary>
            /// A record of kind `withdrawn` taking this certificate away, not yet added.
            /// </summary>
            public StoreComposer.Composed Statement(int number, IEnumerable<int> renewed, int? from)
            {
                return StoreAdmissionChainTests.Statement(number, renewed, from);
            }

            /// <summary>
            /// A statement taking this certificate away, by a record with the item that restates
            /// it, by an item alone, or by a deletion record alone.
            /// </summary>
            public void TakeAway(int number, IEnumerable<int> renewed, int? from, string by)
            {
                var record = StoreAdmissionChainTests.Statement(number, renewed, from);
                switch (by)
                {
                    case "a record":
                        Add(record);
                        Restating(record);
                        break;
                    case "an item":
                        Restating(record);
                        break;
                    default:
                        Deleting(record.Identifier);
                        break;
                }
            }

            public StoreReading Read()
            {
                var store = new ComposedStore(One);
                foreach (var record in _records)
                {
                    store.Add(record);
                }

                store.Deleted.AddRange(_deleted);
                store.RestateAll(_items);
                return store.Read();
            }
        }
    }
}
