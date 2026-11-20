using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What is in force under the store key, `STORE.md` §623, and which authorities a word is
    /// answered under, `STORE.md` §656.
    /// </summary>
    [TestFixture]
    public sealed class StoreInForceTests
    {
        /// <summary>The measured reading's words for a store, and the states the product names them.</summary>
        private static readonly Dictionary<string, StoreState> States = new Dictionary<string, StoreState>
        {
            { "ok", StoreState.Whole },
            { "incomplete", StoreState.Stopped },
            { "nostore", StoreState.None },
        };

        private static IEnumerable<TestCaseData> Measured()
        {
            // The stores about an admission hold what an admission means, which this reading
            // does not read.
            foreach (var meaning in ((JObject)Vectors()["meanings"]).Properties()
                         .Where(meaning => !meaning.Name.StartsWith("an admission", System.StringComparison.Ordinal)))
            {
                yield return new TestCaseData(meaning.Name).SetArgDisplayNames(meaning.Name);
            }
        }

        [TestCaseSource(nameof(Measured))]
        public void What_is_in_force_is_what_the_measured_reading_found_in_force(string name)
        {
            // `tests/Shared/StoreMeaningVectors.json` holds what `probe/read.py` answered, with its
            // history rule on, for stores composed by `probe/store.py`. Compared: the state, every
            // authority that stands, and every retirement and revocation in force. Not compared:
            // which authority is served, since every authority there holds an invented
            // certificate and would be judged damaged, `STORE.md` §614; and the admissions and
            // the names, which this reading gives no meaning.
            var store = (JObject)Vectors()["meanings"][name];
            var versions = new StoreVersions();

            var candidates = new List<StoreCandidate>();
            foreach (var entry in (JObject)store["entries"])
            {
                var current = Strings((JObject)entry.Value["strings"]);
                var history = ((JArray)entry.Value["history"]).Select(item => Strings((JObject)item)).Reverse();
                candidates.Add(versions.Candidate(StoreComposer.BytesOf(entry.Key), new[] { current }.Concat(history).ToArray()));
            }

            var deletions = ((JArray)store["deletions"]).Select(identifier => StoreComposer.BytesOf((string)identifier)).ToList();
            var reading = StoreReading.Of(candidates, deletions, null, new StoreKept());

            Assert.That(reading.State, Is.EqualTo(States[(string)store["status"]]), reading.Reason);
            Assert.That(
                reading.Answering.Select(record => record.Form.Member("id")).OrderBy(id => id, System.StringComparer.Ordinal),
                Is.EqualTo(((JArray)store["auths"]).Select(id => (string)id)),
                "the authorities that stand");

            var refusals = reading.InForce.Taken("retired").Select(subject => "retired " + subject)
                .Concat(reading.InForce.Taken("revoked").Select(subject => "revoked " + subject))
                .OrderBy(refusal => refusal, System.StringComparer.Ordinal);
            Assert.That(
                refusals,
                Is.EqualTo(((JArray)store["refusals"]).Select(refusal => (string)refusal[0] + " " + (string)refusal[1])),
                "what the retirements and the revocations take away");
        }

        [Test]
        public void A_retirement_takes_its_authority_away_by_its_record()
        {
            var store = new ComposedStore(One);
            store.Add(Retired(One, 'a'));
            store.Restate(RetiredItem('a'));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { Hex('a') }));
            Assert.That(reading.InForce.Authorities, Is.Empty, "no authority stands");
            Assert.That(reading.Answering, Is.Empty, "no authority to answer under");
        }

        [Test]
        public void A_retirement_takes_its_authority_away_by_an_item_of_a_summary_entry_present_in_force_or_not()
        {
            // A summary entry that restates the retirement, replaced and beside its deletion record
            // but still in the file, `STORE.md` §623 step 2.
            var store = new ComposedStore(One);
            var earlier = store.Add(Head(One, 2, store.Head.Hex, RetiredItem('a')));
            store.Delete(earlier);
            store.Restate(after: earlier.Hex);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.HeadsInForce.Select(head => head.Hex), Does.Not.Contain(earlier.Hex));
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { Hex('a') }));
            Assert.That(reading.InForce.Authorities, Is.Empty);
        }

        [Test]
        public void A_retirement_takes_its_authority_away_by_a_deletion_record_that_tells_of_it()
        {
            var store = new ComposedStore(One);
            store.Deleted.Add(Told(One, "retired", "authority:" + Hex('a')));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { Hex('a') }));
            Assert.That(reading.Answering, Is.Empty);
        }

        [Test]
        public void A_deletion_record_with_the_tag_and_a_subject_the_file_does_not_know_tells_nothing()
        {
            var store = new ComposedStore(One);
            store.Deleted.Add(Told(One, "retired", "authority:" + Hex('9')));
            store.Deleted.Add(Told(One, "revoked", "client:" + Hex('c')));

            var reading = store.Read();

            Assert.That(reading.InForce.Taken("retired"), Is.Empty);
            Assert.That(reading.InForce.Taken("revoked"), Is.Empty);
            Assert.That(reading.Answering.Select(record => record.Hex), Is.EqualTo(new[] { store.Authority.Hex }));
        }

        [Test]
        public void A_reading_hashes_a_subject_only_where_a_deletion_record_carries_the_tag_of_a_kind_about_it()
        {
            // Every hash a reading makes is kept with the key, so what is kept is what was made.
            var quiet = new ComposedStore(One);
            quiet.Drop(quiet.Head);
            quiet.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            quiet.Add(Head(One, 1, string.Empty, StoreComposer.Item(("client", Hex('c')), ("kind", "revoked"))));
            quiet.Add(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("client", Hex('e')))));
            quiet.Deleted.Add(Told(One, "authority", "authority:" + Hex('9')));

            var reading = quiet.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(Hashed(reading), Is.EqualTo(0), "no deletion record carries the tag of a kind about a client");

            quiet.Deleted.Add(Told(One, "revoked", "client:" + Hex('e')));
            Assert.That(Hashed(quiet.Read()), Is.EqualTo(2), "each client the file knows, once");
        }

        [Test]
        public void A_deletion_record_tells_only_under_the_tag_of_its_own_kind()
        {
            // An authority and a retirement share a subject: the deletion record of the authority
            // takes the authority out of force, and is no retirement.
            var store = new ComposedStore(One);
            store.Deleted.Add(Told(One, "authority", "authority:" + Hex('b')));
            var other = store.Add(StoreRecords.Authority(One, 'b'));

            var reading = store.Read();

            Assert.That(reading.InForce.Taken("retired"), Is.Empty);
            Assert.That(
                reading.InForce.Authorities.Select(record => record.Hex),
                Is.EquivalentTo(new[] { store.Authority.Hex, other.Hex }),
                "the deletion record names another entry of that authority, and not the entry present");
        }

        [Test]
        public void An_authority_beside_a_deletion_record_naming_it_is_out_of_force_and_still_gives_its_key()
        {
            var store = new ComposedStore(One);
            store.Delete(store.Authority);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.Found(One), Is.EqualTo(1), "the key is still found");
            var authority = reading.InForce.Records.Single(record => record.Hex == store.Authority.Hex);
            Assert.That(authority.BesideDeletion, Is.True);
            Assert.That(authority.InForce, Is.False);
            Assert.That(reading.InForce.Authorities, Is.Empty);
            Assert.That(reading.InForce.Taken("retired"), Is.Empty, "taken out of force, and retired by nothing");
        }

        [Test]
        public void A_revocation_takes_its_client_away_by_its_record_by_an_item_and_by_a_deletion_record()
        {
            var byRecord = new ComposedStore(One);
            byRecord.Revoked('c');
            Assert.That(byRecord.Read().InForce.Taken("revoked"), Is.EquivalentTo(new[] { Hex('c') }), "its record");

            var byItem = new ComposedStore(One);
            byItem.Restate(StoreComposer.Item(("client", Hex('d')), ("kind", "revoked")));
            Assert.That(byItem.Read().InForce.Taken("revoked"), Is.EquivalentTo(new[] { Hex('d') }), "an item");

            // A client the file knows from its admission, and a deletion record that tells of its
            // revocation.
            var byDeletion = new ComposedStore(One);
            byDeletion.Add(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("client", Hex('e')))));
            byDeletion.Deleted.Add(Told(One, "revoked", "client:" + Hex('e')));
            Assert.That(byDeletion.Read().InForce.Taken("revoked"), Is.EquivalentTo(new[] { Hex('e') }), "a deletion record");
        }

        [Test]
        public void A_removal_takes_its_name_away_by_its_record_by_an_item_and_by_a_deletion_record()
        {
            var store = new ComposedStore(One);
            store.Decision("removed");
            store.Add(StoreComposer.Record(
                One, "profile", "profile:writer", StoreComposer.Content(("after", string.Empty), ("name", "writer"))));
            store.Deleted.Add(Told(One, "removed", "profile:writer"));
            store.Restate(StoreComposer.Item(("id", new string('7', 32)), ("kind", "removed"), ("name", "auditor")));

            var reading = store.Read();

            Assert.That(reading.InForce.Taken("removed"), Is.EquivalentTo(new[] { "reader", "writer", "auditor" }));
        }

        [Test]
        public void The_removals_of_a_name_are_known_by_record_by_item_and_by_deletion_record_each_once()
        {
            // `STORE.md` §616: a removal is known as a record, as an item of a summary entry
            // present and as a deletion record that tells of it, each by its own identifier, so a
            // removal known all three ways is a single removal.
            var store = new ComposedStore(One);
            var thrice = store.Add(StoreComposer.Record(One, "removed", "profile:reader", StoreComposer.Content(("name", "reader"))));
            store.Delete(thrice);
            var itemOnly = new string('7', 32);
            store.Add(StoreComposer.Record(One, "profile", "profile:writer", StoreComposer.Content(("after", string.Empty), ("name", "writer"))));
            var told = Told(One, "removed", "profile:writer");
            store.Deleted.Add(told);
            store.RestateAll(new[]
            {
                StoreComposer.Item(("id", thrice.Hex), ("kind", "removed"), ("name", "reader")),
                StoreComposer.Item(("id", itemOnly), ("kind", "removed"), ("name", "auditor")),
            });

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.RemovalsOf("reader"), Is.EqualTo(new[] { thrice.Hex }), "its record, its item and its deletion record");
            Assert.That(reading.InForce.RemovalsOf("auditor"), Is.EqualTo(new[] { itemOnly }), "an item alone");
            Assert.That(reading.InForce.RemovalsOf("writer"), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }), "a deletion record alone");
            Assert.That(reading.InForce.RemovalsOf("nobody"), Is.Empty);
            Assert.That(reading.InForce.RemovalsOf("Reader"), Is.Empty, "a name is compared exactly");
        }

        [Test]
        public void The_removals_an_addition_names_are_those_known_and_those_a_deletion_record_tells_of_once_the_name_is_known_each_once()
        {
            // `STORE.md` §616: the removals of a name the reading holds, and every removal a
            // deletion record tells of once the name counts as known, which the addition makes it;
            // a removal known several ways, and known both by the reading and once the name is
            // known, is a single removal.
            var store = new ComposedStore(One);
            var thrice = store.Add(StoreComposer.Record(One, "removed", "profile:reader", StoreComposer.Content(("name", "reader"))));
            store.Delete(thrice);
            var further = Told(One, "removed", "profile:reader");
            store.Deleted.Add(further);
            var itemOnly = new string('7', 32);
            var unknown = Told(One, "removed", "profile:ops");
            store.Deleted.Add(unknown);
            store.RestateAll(new[]
            {
                StoreComposer.Item(("id", thrice.Hex), ("kind", "removed"), ("name", "reader")),
                StoreComposer.Item(("id", itemOnly), ("kind", "removed"), ("name", "auditor")),
            });

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            var both = new[] { thrice.Hex, KeePassGate.Hex.Of(further) };
            Assert.That(reading.InForce.RemovalsOf("reader"), Is.EquivalentTo(both), "known by the reading");
            Assert.That(reading.InForce.RemovalsOnceKnown("reader"), Is.EquivalentTo(both), "each once, however it is known");
            Assert.That(reading.InForce.RemovalsOnceKnown("auditor"), Is.EqualTo(new[] { itemOnly }), "an item alone");
            Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty, "a name the file does not know");
            Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Is.EqualTo(new[] { KeePassGate.Hex.Of(unknown) }), "told once it is known");
            Assert.That(reading.InForce.RemovalsOnceKnown("nobody"), Is.Empty);
            Assert.That(reading.InForce.RemovalsOnceKnown("Ops"), Is.Empty, "a name is compared exactly");

            var none = new ComposedStore(One);
            none.Restate(none.Decision("removed"));
            var held = none.Read();
            Assert.That(held.InForce.RemovalsOf("reader"), Has.Count.EqualTo(1));
            Assert.That(held.InForce.RemovalsOnceKnown("reader"), Is.EqualTo(held.InForce.RemovalsOf("reader")),
                "with no deletion record of a removal, what the reading holds");
        }

        [Test]
        public void The_removals_an_addition_names_are_refused_from_a_reading_whose_key_was_overwritten()
        {
            // A reading let go overwrites its key, and a name hashed under the bytes left would
            // find none of the deletion records that tell of its removals.
            var store = new Fakes.MemoryStore();
            StoreSeeds.Authority(store);
            RemovedApart(store, "reader");
            StoreInForce letGo;
            using (var reading = store.Read())
            {
                letGo = reading.InForce;
                Assert.That(letGo.RemovalsOnceKnown("reader"), Has.Count.EqualTo(1));
            }

            Assert.Throws<System.InvalidOperationException>(() => letGo.RemovalsOnceKnown("reader"));
        }

        [Test]
        public void The_additions_are_every_record_of_kind_profile_in_force_or_not()
        {
            var store = new ComposedStore(One);
            var standing = store.Add(StoreComposer.Record(
                One, "profile", "profile:reader", StoreComposer.Content(("after", string.Empty), ("name", "reader"))));
            var gone = store.Add(StoreComposer.Record(
                One, "profile", "profile:writer", StoreComposer.Content(("after", string.Empty), ("name", "writer"))));
            store.Delete(gone);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Additions.Select(record => record.Hex), Is.EquivalentTo(new[] { standing.Hex, gone.Hex }));
            Assert.That(reading.InForce.Additions.Single(record => record.Hex == gone.Hex).InForce, Is.False, "beside a deletion record naming it");
        }

        [Test]
        public void A_certificate_taken_away_is_taken_away_by_its_record_and_by_an_item()
        {
            var store = new ComposedStore(One);
            store.Decision("withdrawn");
            store.Restate(StoreComposer.Item(("certificate", Hex('e')), ("id", new string('6', 32)), ("kind", "withdrawn"), ("renewed", string.Empty)));

            Assert.That(store.Read().InForce.Taken("withdrawn"), Is.EquivalentTo(new[] { Hex('d'), Hex('e') }));
        }

        [Test]
        public void Records_that_took_a_certificate_away_and_differ_in_what_they_hold_are_each_restated_apart()
        {
            // `STORE.md` §648: each record is restated by an item of its own, with its identifier.
            var store = new ComposedStore(One);
            var first = Withdrawal(store, 'd', Hex('1'));
            var second = Withdrawal(store, 'd', string.Empty);
            store.RestateAll(new[] { StatementOf(first.Hex, 'd', Hex('1')), StatementOf(second.Hex, 'd', string.Empty) });

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Taken("withdrawn"), Is.EquivalentTo(new[] { Hex('d') }));
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(3), "the item format and a statement for each record");
        }

        [TestCase("renewed")]
        [TestCase("from")]
        [TestCase("id")]
        public void A_record_whose_item_differs_from_it_in_a_member_stops_the_store(string member)
        {
            var store = new ComposedStore(One);
            var record = Withdrawal(store, 'd', Hex('1'), Hex('2'));
            store.Restate(StatementOf(
                member == "id" ? new string('6', 32) : record.Hex,
                'd',
                member == "renewed" ? string.Empty : Hex('1'),
                member == "from" ? null : Hex('2')));

            Assert.That(store.Read().State, Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_deletion_record_of_a_record_an_item_restates_adds_no_statement()
        {
            var store = new ComposedStore(One);
            var record = Withdrawal(store, 'd', string.Empty);
            store.Restate(StatementOf(record.Hex, 'd', string.Empty));
            store.Drop(record);
            store.Delete(record);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(2), "the item format and the item that restates the record");
        }

        [Test]
        public void A_deletion_record_of_a_record_whose_content_is_nowhere_is_a_statement_of_its_own_with_no_renewal()
        {
            // The admission key is known from the `renewed` of an item of the kind: `STORE.md`
            // §623 step 2.
            var store = new ComposedStore(One);
            store.Restate(StatementOf(new string('6', 32), 'd', Hex('e')));
            var told = Told(One, "withdrawn", "certificate:" + Hex('e'));
            store.Deleted.Add(told);

            var reading = store.Read();

            Assert.That(reading.InForce.Taken("withdrawn"), Is.EquivalentTo(new[] { Hex('d'), Hex('e') }));
            var restated = reading.InForce.Restated().Where(item => item.Kind == "withdrawn").ToList();
            Assert.That(restated, Has.Count.EqualTo(2));
            var lost = restated.Single(item => item.Member("certificate") == Hex('e'));
            Assert.That(lost.Member("id"), Is.EqualTo(KeePassGate.Hex.Of(told)));
            Assert.That(lost.Member("renewed"), Is.Empty);
            Assert.That(lost.Members.ContainsKey("from"), Is.False);
        }

        [Test]
        public void A_deletion_record_tells_of_a_certificate_taken_away_by_the_certificate_an_admission_names_in_force_or_not()
        {
            // `STORE.md` §623 step 2: the record that took the certificate away and its item are
            // gone, and the admission of that certificate still holds its content in the file.
            foreach (var inForce in new[] { true, false })
            {
                var store = new ComposedStore(One);
                var admission = store.Add(Admission(Hex('d')));
                if (!inForce)
                {
                    store.Delete(admission);
                }

                var told = Told(One, "withdrawn", "certificate:" + Hex('d'));
                store.Deleted.Add(told);

                var reading = store.Read();

                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.Taken("withdrawn"), Is.EquivalentTo(new[] { Hex('d') }), "in force: " + inForce);
                Assert.That(reading.InForce.Room.Items, Is.EqualTo(2), "the item format and a statement for the deletion record");
                Assert.That(
                    reading.InForce.Restated().Single(item => item.Kind == "withdrawn").Members,
                    Is.EquivalentTo(new Dictionary<string, string>
                    {
                        { "id", KeePassGate.Hex.Of(told) },
                        { "certificate", Hex('d') },
                        { "renewed", string.Empty },
                    }),
                    "a statement of its own, which names no renewal");
            }
        }

        [Test]
        public void A_deletion_record_tells_of_a_certificate_taken_away_by_the_certificate_a_renewal_names_as_its_origin()
        {
            // The admission of the certificate taken away went with the write that took it away:
            // only the renewal's `from` still names it, `STORE.md` §623 step 2.
            var store = new ComposedStore(One);
            store.Add(Admission(Hex('e'), Hex('d')));
            store.Deleted.Add(Told(One, "withdrawn", "certificate:" + Hex('d')));

            var reading = store.Read();

            Assert.That(reading.InForce.Taken("withdrawn"), Is.EquivalentTo(new[] { Hex('d') }));
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(2));
        }

        [Test]
        public void A_text_an_admission_holds_that_is_no_admission_key_tells_of_nothing()
        {
            var store = new ComposedStore(One);
            store.Add(Admission(Hex('D'), "short"));
            store.Deleted.Add(Told(One, "withdrawn", "certificate:" + Hex('D')));
            store.Deleted.Add(Told(One, "withdrawn", "certificate:short"));

            var reading = store.Read();

            Assert.That(reading.InForce.Taken("withdrawn"), Is.Empty);
            Assert.That(reading.InForce.Room.Items, Is.EqualTo(1), "the item format alone");
        }

        [Test]
        public void A_reading_hashes_the_keys_admissions_name_only_where_a_deletion_record_carries_the_tag_of_a_certificate_taken_away()
        {
            // A deletion record of another kind's tag, which names no admission key.
            var store = new ComposedStore(One);
            store.Add(Admission(Hex('d')));
            store.Add(Admission(Hex('e'), Hex('d')));
            store.Deleted.Add(Told(One, "authority", "authority:" + Hex('9')));

            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(Hashed(reading), Is.EqualTo(0), "no deletion record carries the tag of a certificate taken away");

            store.Deleted.Add(Told(One, "withdrawn", "certificate:" + Hex('9')));
            Assert.That(Hashed(store.Read()), Is.EqualTo(2), "each admission key the admissions name, once");
        }

        /// <summary>A record of kind `client` of the client `c`, for a renewal with the certificate it was renewed from.</summary>
        private static StoreComposer.Composed Admission(string certificate, string from = null)
        {
            var members = new List<(string Name, string Value)> { ("certificate", certificate), ("client", Hex('c')) };
            if (from != null)
            {
                members.Add(("from", from));
            }

            return StoreComposer.Record(One, "client", "client:" + Hex('c'), StoreComposer.Content(members.ToArray()));
        }

        [Test]
        public void A_statement_is_restated_with_every_member_it_holds_and_none_it_lacks()
        {
            var store = new ComposedStore(One);
            var record = Withdrawal(store, 'd', Hex('1'), Hex('2'));
            store.Restate(StatementOf(record.Hex, 'd', Hex('1'), Hex('2')));

            var item = store.Read().InForce.Restated().Single(found => found.Kind == "withdrawn");

            Assert.That(item.Members, Is.EquivalentTo(new Dictionary<string, string>
            {
                { "id", record.Hex },
                { "certificate", Hex('d') },
                { "renewed", Hex('1') },
                { "from", Hex('2') },
            }));
        }

        [Test]
        public void An_item_of_a_certificate_taken_away_without_an_id_is_no_item_of_its_kind()
        {
            var store = new ComposedStore(One);
            store.Restate(StoreComposer.Item(("certificate", Hex('d')), ("kind", "withdrawn"), ("renewed", string.Empty)));

            Assert.That(store.Read().State, Is.Not.EqualTo(StoreState.Whole), "the summary entry does not read as its kind");
        }

        private static StoreComposer.Composed Withdrawal(ComposedStore store, char certificate, string renewed, string from = null)
        {
            var members = new List<(string Name, string Value)> { ("certificate", Hex(certificate)), ("renewed", renewed) };
            if (from != null)
            {
                members.Add(("from", from));
            }

            return store.Add(StoreComposer.Record(
                One, "withdrawn", "certificate:" + Hex(certificate), StoreComposer.Content(members.ToArray())));
        }

        private static string StatementOf(string id, char certificate, string renewed, string from = null)
        {
            var members = new List<(string Name, string Value)> { ("certificate", Hex(certificate)), ("id", id), ("kind", "withdrawn"), ("renewed", renewed) };
            if (from != null)
            {
                members.Add(("from", from));
            }

            return StoreComposer.Item(members.ToArray());
        }

        [Test]
        public void The_summary_entries_present_in_force_and_told_by_deletion_records()
        {
            var store = new ComposedStore(One);
            var first = store.Head;
            store.Restate();
            var second = store.Head;

            var reading = store.Read();

            Assert.That(reading.InForce.Heads.Select(head => head.Hex), Is.EqualTo(new[] { second.Hex }));
            Assert.That(reading.InForce.HeadsInForce.Select(head => head.Hex), Is.EqualTo(new[] { second.Hex }));
            Assert.That(reading.InForce.Told, Is.EquivalentTo(new[] { first.Hex }));
        }

        [Test]
        public void The_highest_format_number_and_the_highest_plugin_version_are_each_the_greatest_stated()
        {
            var records = new ComposedStore(One);
            records.Add(Format(One, 1, "0.9.9"));
            records.Add(Format(One, 1, "0.3.12"));
            var reading = records.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Format, Is.EqualTo(1));
            Assert.That(reading.InForce.Plugin, Is.EqualTo("0.9.9"), "a record above the summary entry's 0.3.0");

            var item = new ComposedStore(One);
            item.Drop(item.Head);
            item.Add(StoreComposer.Record(
                One, "head", "store",
                StoreComposer.Content(("after", string.Empty), ("height", "1")),
                1,
                new[] { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.10.0")) }));
            item.Add(Format(One, 1, "0.9.9"));
            Assert.That(item.Read().InForce.Plugin, Is.EqualTo("0.10.0"), "compared as numbers, and not as text");
        }

        [Test]
        public void A_deletion_record_of_a_version_record_states_its_format_number_and_no_plugin_version()
        {
            // No summary entry and no version record: the deletion record alone states a format.
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Deleted.Add(Told(One, "format", "format:1", 1));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(reading.InForce.Format, Is.EqualTo(1));
            Assert.That(reading.InForce.Plugin, Is.Null);
        }

        [Test]
        public void A_record_states_whether_a_deletion_record_names_it_and_whether_it_counts_by_its_history()
        {
            var store = new ComposedStore(One);
            var revoked = store.Revoked('c');
            store.Edit(revoked, "{\"client\":\"edited\"}");
            store.Delete(revoked);

            var record = store.Read().InForce.Records.Single(found => found.Hex == revoked.Hex);

            Assert.That(record.ByHistory, Is.True);
            Assert.That(record.BesideDeletion, Is.True);
            Assert.That(record.InForce, Is.True, "a revocation counts beside a deletion record naming it");
        }

        [Test]
        public void What_is_in_force_is_worked_out_for_a_store_that_is_stopped_or_whole_and_for_no_other()
        {
            Assert.That(new ComposedStore(One).Read().InForce, Is.Not.Null, "whole");

            var stopped = new ComposedStore(One);
            stopped.Drop(stopped.Head);
            Assert.That(stopped.Read().InForce, Is.Not.Null, "stopped");

            var none = new ComposedStore(One);
            none.Drop(none.Authority);
            Assert.That(none.Read().InForce, Is.Null, "no key");

            var split = new ComposedStore(One);
            split.Add(StoreRecords.Authority(Two, 'b'));
            Assert.That(split.Read().InForce, Is.Null, "several keys");

            Assert.That(new ComposedStore(One, StoreFormat.Number + 1).Read().InForce, Is.Null, "a later format");

            var unknown = new ComposedStore(One);
            unknown.Add(StoreComposer.Record(One, "future", "x", StoreComposer.Content(("x", "y"))));
            Assert.That(unknown.Read().InForce, Is.Null, "a kind the plugin does not know");
        }

        [Test]
        public void A_word_is_answered_under_each_authority_that_stands_under_each_key_that_stands()
        {
            // Split: the authority under each key, and a retirement under one key takes nothing
            // away under another, `STORE.md` §656.
            var split = new ComposedStore(One);
            var second = split.Add(StoreRecords.Authority(Two, 'b'));
            split.Add(Retired(One, 'b'));
            Assert.That(
                split.Read().Answering.Select(record => record.Hex),
                Is.EquivalentTo(new[] { split.Authority.Hex, second.Hex }));

            var retiredUnderItsOwnKey = new ComposedStore(One);
            retiredUnderItsOwnKey.Add(StoreRecords.Authority(Two, 'b'));
            retiredUnderItsOwnKey.Add(Retired(Two, 'b'));
            Assert.That(
                retiredUnderItsOwnKey.Read().Answering.Select(record => record.Hex),
                Is.EqualTo(new[] { retiredUnderItsOwnKey.Authority.Hex }));

            var outdated = new ComposedStore(One, StoreFormat.Number + 1);
            Assert.That(outdated.Read().Answering.Select(record => record.Hex), Is.EqualTo(new[] { outdated.Authority.Hex }));

            var stopped = new ComposedStore(One);
            stopped.Drop(stopped.Head);
            Assert.That(stopped.Read().Answering.Select(record => record.Hex), Is.EqualTo(new[] { stopped.Authority.Hex }));

            var several = new ComposedStore(One);
            var other = several.Add(StoreRecords.Authority(One, 'b'));
            Assert.That(
                several.Read().Answering.Select(record => record.Hex),
                Is.EquivalentTo(new[] { several.Authority.Hex, other.Hex }),
                "several under a single key, as for undecided");

            var none = new ComposedStore(One);
            none.Drop(none.Authority);
            Assert.That(none.Read().Answering, Is.Empty);
        }

        private static StoreComposer.Composed Retired(byte[] key, char authority)
        {
            return StoreComposer.Record(key, "retired", "authority:" + Hex(authority), StoreComposer.Content(("authority", Hex(authority))));
        }

        private static string RetiredItem(char authority)
        {
            return StoreComposer.Item(("authority", Hex(authority)), ("kind", "retired"));
        }

        private static StoreComposer.Composed Format(byte[] key, int number, string plugin)
        {
            var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StoreComposer.Record(
                key, "format", "format:" + text, StoreComposer.Content(("format", text), ("plugin", plugin)), number);
        }

        /// <summary>
        /// The identifier of a deletion record of a record of this kind and subject: its tag, its
        /// subject hash and its number under the key, and the start of a check no record here has.
        /// </summary>
        private static byte[] Told(byte[] key, string kind, string subject, int number = 0)
        {
            return StoreComposer.IdentifierOf(key, kind, subject, number, new string('5', 64));
        }

        [Test]
        public void A_split_store_hashes_no_subject_under_any_key_but_the_id_of_an_authority_present()
        {
            // `STORE.md` §649 and §656: of a store under several keys only the authorities that
            // stand are worked out, so no value of an outsider's records is hashed or kept with
            // its key, and a retirement told by a deletion record still takes its authority away.
            var split = new ComposedStore(One);
            var second = split.Add(StoreRecords.Authority(Two, 'b'));
            split.Add(StoreRecords.Authority(Two, 'e'));
            split.Add(StoreComposer.Record(Two, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            split.Add(StoreComposer.Record(Two, "removed", "profile:reader", StoreComposer.Content(("name", "reader"))));
            split.Deleted.Add(Told(Two, "revoked", "client:" + Hex('c')));
            split.Deleted.Add(Told(Two, "removed", "profile:reader"));
            split.Deleted.Add(Told(Two, "format", "format:2", 2));
            split.Deleted.Add(Told(Two, "retired", "authority:" + Hex('e')));
            split.Deleted.Add(Told(One, "revoked", "client:" + Hex('c')));

            var reading = split.Read();

            Assume.That(reading.State, Is.EqualTo(StoreState.Split));
            Assert.That(reading.Answering.Select(record => record.Hex), Is.EquivalentTo(new[] { split.Authority.Hex, second.Hex }),
                "the authority a deletion record retires under its own key does not stand");
            var subjects = new[] { split.Authority, second }
                .Select(record => reading.Answering.First(found => found.Hex == record.Hex).Form.StoreKey)
                .SelectMany(Subjects)
                .ToList();
            // A key's name is hashed under the other keys to tell what sets which aside, §649.
            Assert.That(
                subjects,
                Is.SubsetOf(new[]
                {
                    "authority:" + Hex('a'), "authority:" + Hex('b'), "authority:" + Hex('e'),
                    "key:" + StoreComposer.KeyName(One), "key:" + StoreComposer.KeyName(Two),
                }),
                "nothing of a record's but an authority's id is hashed: " + string.Join(", ", subjects));
            Assert.That(subjects, Does.Contain("authority:" + Hex('e')), "the retirement told by a deletion record was looked for");
        }

        /// <summary>The subjects hashed under a key and kept with it.</summary>
        private static IEnumerable<string> Subjects(FoundKey key)
        {
            var kept = typeof(FoundKey).GetField("_subjects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return ((Dictionary<string, string>)kept.GetValue(key)).Keys.ToList();
        }

        /// <summary>How many subjects were hashed under the store key and kept with it.</summary>
        private static int Hashed(StoreReading reading)
        {
            var key = reading.InForce.Records.Select(record => record.Form.StoreKey).First(found => found != null);
            var kept = typeof(FoundKey).GetField("_subjects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return ((System.Collections.ICollection)kept.GetValue(key)).Count;
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreMeaningVectors.json");
                if (File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreMeaningVectors.json is not above the test's directory");
        }

        private static IReadOnlyDictionary<string, string> Strings(JObject strings)
        {
            return strings.Properties().ToDictionary(property => property.Name, property => (string)property.Value);
        }
    }
}
