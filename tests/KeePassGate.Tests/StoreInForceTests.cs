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
            var reading = Read(store);

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
        public void An_item_with_no_subject_takes_away_from_every_subject_the_file_names_whose_keyed_tag_and_subject_hash_it_holds()
        {
            // `STORE.md` §611: a revocation and a retirement by the keyed tag and subject hash of
            // their deletion records, a removal by its identifier, each in the form the measured
            // model writes, and each telling of a subject the file names by another record.
            var store = new ComposedStore(One);
            store.Add(StoreComposer.Record(One, "client", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            store.Add(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("client", Hex('e')))));
            store.Add(Profile("reader"));
            var removal = StoreComposer.HexOf(Told(One, "removed", "profile:reader"));
            store.RestateAll(new[]
            {
                HashItem("revoked", Told(One, "revoked", "client:" + Hex('c'))),
                HashItem("retired", Told(One, "retired", "authority:" + Hex('a'))),
                StoreComposer.Item(("id", removal), ("kind", "removed")),
            });

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Taken("revoked"), Is.EqualTo(new[] { Hex('c') }), "the client the hash is of, and no other");
            Assert.That(reading.InForce.Taken("retired"), Is.EqualTo(new[] { Hex('a') }));
            Assert.That(reading.Answering, Is.Empty, "the authority retired stands no more");
            Assert.That(reading.InForce.RemovalsOf("reader"), Is.EqualTo(new[] { removal }));
            Assert.That(reading.InForce.ByDeletion("revoked"), Is.Empty, "an item carries it, and no deletion record alone");
            Assert.That(Unnamed(reading, "revoked"), Is.Empty, "restated by its subject, which the file names");
            Assert.That(Unnamed(reading, "retired"), Is.Empty);
            Assert.That(Unnamed(reading, "removed"), Is.Empty);
        }

        [Test]
        public void An_item_with_no_subject_restates_every_record_of_its_kind_its_hash_begins_and_a_removal_by_its_identifier()
        {
            // `STORE.md` §611 and §624: the revocation records of a client under two identifiers,
            // a retirement and a removal, restated by items with no subject; a hash under another
            // kind's tag restates nothing.
            var store = new ComposedStore(One);
            var revoked = store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            var again = store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')), ("note", "made apart"))));
            var retired = store.Add(Retired(One, '9'));
            var removed = store.Add(Removal("reader"));
            Assume.That(again.Hex, Is.Not.EqualTo(revoked.Hex));
            store.RestateAll(new[]
            {
                StoreComposer.Item(("hash", revoked.Hex.Substring(0, 16)), ("kind", "revoked")),
                StoreComposer.Item(("hash", retired.Hex.Substring(0, 16)), ("kind", "retired")),
                StoreComposer.Item(("id", removed.Hex), ("kind", "removed")),
            });

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.NotRestated(), Is.Empty);

            var otherTag = new ComposedStore(One);
            var record = otherTag.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            otherTag.Restate(StoreComposer.Item(("hash", record.Hex.Substring(0, 16)), ("kind", "retired")));
            var refused = otherTag.Read();
            Assert.That(refused.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(refused.InForce.NotRestated().Select(found => found.Hex), Is.EqualTo(new[] { record.Hex }));
        }

        [Test]
        public void A_decision_whose_subject_the_file_names_nowhere_takes_nothing_away_and_is_held_until_the_subject_is_named()
        {
            // `STORE.md` §611: a deletion record and an item with no subject, each of a subject
            // no record or item names, are held by what an item with no subject is to hold: the
            // keyed tag and subject hash, and a removal's identifier; two deletion records of a
            // single revocation are a single decision.
            var store = new ComposedStore(One);
            var revocation = Told(One, "revoked", "client:" + Hex('c'));
            var again = StoreComposer.IdentifierOf(One, "revoked", "client:" + Hex('c'), 0, new string('6', 64));
            store.Deleted.Add(revocation);
            store.Deleted.Add(again);
            var retirement = Told(One, "retired", "authority:" + Hex('9'));
            store.Restate(HashItem("retired", retirement));
            var removal = Told(One, "removed", "profile:reader");
            store.Deleted.Add(removal);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Taken("revoked"), Is.Empty);
            Assert.That(reading.InForce.Taken("retired"), Is.Empty);
            Assert.That(reading.InForce.Taken("removed"), Is.Empty);
            Assert.That(Unnamed(reading, "revoked"), Is.EqualTo(new[] { StoreComposer.HexOf(revocation).Substring(0, 16) }));
            Assert.That(Unnamed(reading, "retired"), Is.EqualTo(new[] { StoreComposer.HexOf(retirement).Substring(0, 16) }));
            Assert.That(Unnamed(reading, "removed"), Is.EqualTo(new[] { StoreComposer.HexOf(removal) }));
            Assert.That(
                reading.InForce.UnnamedByDeletion.Select(decision => decision.Kind + " " + string.Join(" ", decision.Told)),
                Is.EquivalentTo(new[]
                {
                    "revoked " + string.Join(" ", new[] { StoreComposer.HexOf(revocation), StoreComposer.HexOf(again) }.OrderBy(hex => hex, System.StringComparer.Ordinal)),
                    "removed " + StoreComposer.HexOf(removal),
                }),
                "the retirement is carried by an item, and no deletion record alone");

            store.Add(StoreComposer.Record(One, "client", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            store.Add(StoreRecords.Authority(One, '9'));
            store.Add(Profile("reader"));
            var named = store.Read();
            Assert.That(named.InForce.Taken("revoked"), Is.EqualTo(new[] { Hex('c') }), "once the file names the client");
            Assert.That(named.InForce.Taken("retired"), Is.EqualTo(new[] { Hex('9') }), "once the file names the authority");
            Assert.That(named.InForce.RemovalsOf("reader"), Is.EqualTo(new[] { StoreComposer.HexOf(removal) }), "once the file names the name");
            Assert.That(Unnamed(named, "revoked"), Is.Empty);
            Assert.That(Unnamed(named, "retired"), Is.Empty);
            Assert.That(Unnamed(named, "removed"), Is.Empty);
        }

        [Test]
        public void The_removals_an_addition_names_include_those_an_item_with_no_subject_holds_once_the_name_is_known()
        {
            // `STORE.md` §616 and §611: an item with no subject counts as a carrier of the removal
            // wherever a deletion record of it does.
            var store = new ComposedStore(One);
            var removal = StoreComposer.HexOf(Told(One, "removed", "profile:ops"));
            store.Restate(StoreComposer.Item(("id", removal), ("kind", "removed")));

            var reading = store.Read();

            Assert.That(reading.InForce.RemovalsOf("ops"), Is.Empty, "a name the file does not know");
            Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Is.EqualTo(new[] { removal }), "told once it is known");
            Assert.That(reading.InForce.RemovalsOnceKnown("reader"), Is.Empty);
        }

        [Test]
        public void An_item_of_a_decision_with_neither_its_subject_nor_a_hash_in_form_is_no_item_of_its_kind()
        {
            foreach (var item in new[]
                     {
                         StoreComposer.Item(("kind", "revoked")),
                         StoreComposer.Item(("hash", new string('a', 15)), ("kind", "revoked")),
                         StoreComposer.Item(("hash", new string('A', 16)), ("kind", "retired")),
                         StoreComposer.Item(("hash", new string('a', 32)), ("kind", "retired")),
                         StoreComposer.Item(("hash", new string('a', 16)), ("kind", "removed")),
                         StoreComposer.Item(("id", "not an identifier"), ("kind", "removed")),
                         StoreComposer.Item(("id", new string('a', 31)), ("kind", "removed")),
                         StoreComposer.Item(("id", new string('A', 32)), ("kind", "removed")),
                     })
            {
                var store = new ComposedStore(One);
                store.Restate(item);
                Assert.That(store.Read().State, Is.Not.EqualTo(StoreState.Whole), item + ": the summary entry does not read as its kind");
            }
        }

        /// <summary>
        /// What a summary entry written over this reading restates with no subject for a kind: of a
        /// revocation and a retirement each keyed tag and subject hash, and of a removal each
        /// identifier, whose subject the file names nowhere.
        /// </summary>
        private static IEnumerable<string> Unnamed(StoreReading reading, string kind)
        {
            return reading.InForce.Restated()
                .Where(item => item.Kind == kind && item.Member(StoreKinds.Find(kind).RestatedBy) == null)
                .Select(item => item.Member(kind == StoreKinds.Removed ? StoreKinds.Id : StoreKinds.Hash))
                .OrderBy(carried => carried, System.StringComparer.Ordinal);
        }

        /// <summary>An item with no subject restating the decision a deletion record of this identifier tells of.</summary>
        private static string HashItem(string kind, byte[] told)
        {
            return StoreComposer.Item(("hash", StoreComposer.HexOf(told).Substring(0, 16)), ("kind", kind));
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

        [Test]
        public void Every_summary_entry_told_of_that_nothing_in_force_replaces_is_held_and_the_reading_names_the_first()
        {
            // `STORE.md` §624: two summary entries deleted that nothing present stands in for, and
            // one the summary entry in force names, which is no loss.
            var store = new ComposedStore(One);
            var first = Told(One, "head", "store", 7);
            var second = StoreComposer.IdentifierOf(One, "head", "store", 8, new string('6', 64));
            store.Deleted.Add(second);
            store.Deleted.Add(first);
            store.Restate();

            var reading = store.Read();

            var lost = new[] { StoreComposer.HexOf(first), StoreComposer.HexOf(second) }.OrderBy(id => id, System.StringComparer.Ordinal).ToList();
            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(reading.InForce.LostHeads, Is.EqualTo(lost), "both, ascending, and not the summary entry the one in force names");
            Assert.That(reading.Reason, Does.EndWith(lost[0].Substring(0, 8)));
        }

        [Test]
        public void Every_record_no_summary_entry_restates_is_held_and_the_reading_names_the_first()
        {
            var store = new ComposedStore(One);
            var revoked = store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            var retired = store.Add(Retired(One, '9'));
            store.Revoked('d');

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(reading.InForce.NotRestated().Select(record => record.Hex), Is.EquivalentTo(new[] { revoked.Hex, retired.Hex }));
            Assert.That(reading.Reason, Does.EndWith(reading.InForce.NotRestated()[0].Hex.Substring(0, 8)));
        }

        [Test]
        public void What_a_deletion_record_alone_takes_away_is_held_apart_from_what_a_record_or_an_item_takes_away()
        {
            // `STORE.md` §623 step 2: each counts by the union, and only what no record present and
            // no item of a summary entry present names is known by its deletion record alone.
            var store = new ComposedStore(One);
            store.Revoked('c');
            store.Add(StoreComposer.Record(One, "client", "client:" + Hex('e'), StoreComposer.Content(("client", Hex('e')))));
            store.Deleted.Add(Told(One, "revoked", "client:" + Hex('c')));
            store.Deleted.Add(Told(One, "revoked", "client:" + Hex('e')));
            store.Deleted.Add(Told(One, "retired", "authority:" + Hex('a')));
            store.Add(Profile("reader"));
            var removed = store.Add(Removal("writer"));
            store.Add(Profile("writer"));
            store.Restate(StoreComposer.Item(("id", removed.Hex), ("kind", "removed"), ("name", "writer")));
            var byDeletion = Told(One, "removed", "profile:reader");
            store.Deleted.Add(byDeletion);
            store.Deleted.Add(removed.Identifier);

            var inForce = store.Read().InForce;

            Assert.That(inForce.Taken("revoked"), Is.EquivalentTo(new[] { Hex('c'), Hex('e') }));
            Assert.That(inForce.ByDeletion("revoked"), Is.EquivalentTo(new[] { Hex('e') }));
            Assert.That(inForce.ByDeletion("retired"), Is.EquivalentTo(new[] { Hex('a') }));
            Assert.That(
                inForce.RemovalsByDeletion.Select(pair => pair.Key + " " + pair.Value),
                Is.EquivalentTo(new[] { "reader " + StoreComposer.HexOf(byDeletion) }),
                "the removal of writer is a record and an item as well as a deletion record");
        }

        [Test]
        public void A_damaged_version_tells_of_what_its_deletion_record_would_make_refuse_and_makes_nothing_refuse_now()
        {
            // `STORE.md` §625: once the seal deletes a damaged version with a deletion record, that
            // record is told of at its place, unless a version in force names it in `after`: here
            // at a node no object names, found by the tree, and at a row whose version names it.
            StoreComposer.Composed standing;
            var store = Ruled(out standing);
            var damaged = store.Add(StoreComposer.RuleVersion(One, RowAt(Below, "pf"), "row:R"));
            store.Replace(damaged, "{\"edited\":\"\"}");
            var named = StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:C");
            var naming = StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:RU", standing.Hex, named.Hex);
            store.Add(named);
            store.Replace(named, "{\"edited\":\"\"}");
            store.Add(naming);
            store.RestateAll(new[] { naming }.Select(ItemOf));

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Stopped));
            Assert.That(reading.Damage, Is.EqualTo(2));
            Assert.That(Lost(reading.InForce), Is.Empty, "nothing is lost while the damaged entries stand");
            Assert.That(reading.InForce.RefusesAfter, Is.EqualTo(new[] { RowAt(Below, "pf") }), "the version in force at the node names its damaged version");
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

        private static IEnumerable<TestCaseData> MeasuredRules()
        {
            // `rules:` the stores of rules, and `rules, ` those of the lost versions a version
            // settles, which the measured reading reads as the product does.
            foreach (var meaning in ((JObject)Vectors()["meanings"]).Properties()
                         .Where(meaning => meaning.Name.StartsWith("rules:", System.StringComparison.Ordinal)
                                           || meaning.Name.StartsWith("rules, ", System.StringComparison.Ordinal)))
            {
                yield return new TestCaseData(meaning.Name).SetArgDisplayNames(meaning.Name);
            }
        }

        [TestCaseSource(nameof(MeasuredRules))]
        public void What_stands_and_what_is_lost_is_what_the_measured_reading_found(string name)
        {
            // `tests/Shared/StoreMeaningVectors.json` holds, for each store of rules, the texts of
            // the versions that stand for each object and the objects that are lost, as
            // `probe/read.py` found them, read beside the tree the store holds.
            var store = (JObject)Vectors()["meanings"][name];
            var rules = (JObject)store["rules"];

            using (var reading = Read(store))
            {
                Assert.That(reading.State, Is.EqualTo(States[(string)store["status"]]), reading.Reason);
                var inForce = reading.InForce;
                Assert.That(
                    inForce.Objects
                        .SelectMany(objectText => inForce.VersionsOf(objectText).Select(version => objectText + " " + version.Members["doc"]))
                        .OrderBy(line => line, System.StringComparer.Ordinal),
                    Is.EqualTo(((JObject)rules["standing"]).Properties()
                        .SelectMany(standing => standing.Value.Select(doc => standing.Name + " " + (string)doc))
                        .OrderBy(line => line, System.StringComparer.Ordinal)),
                    "the texts of the versions that stand, by object");
                Assert.That(
                    inForce.Objects.Where(objectText => inForce.LostOf(objectText).Count > 0).OrderBy(objectText => objectText, System.StringComparer.Ordinal),
                    Is.EqualTo(((JArray)rules["lost"]).Select(lost => (string)lost).OrderBy(lost => lost, System.StringComparer.Ordinal)),
                    "the objects lost");
            }
        }

        [Test]
        public void A_version_stands_by_its_record_in_force_or_by_an_item_of_a_summary_entry_in_force_where_no_version_of_its_object_names_it()
        {
            // `STORE.md` §617 and §623 step 3. A version carried by an item of a summary entry in
            // force alone stands as its record would; a version carried by an item of a summary
            // entry out of force alone is in force by nothing; and a version that a version of
            // another object names is not replaced by it.
            var store = new ComposedStore(One);
            var byRecord = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            var replaced = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:R"));
            var replacing = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:RU", replaced.Hex));
            var crossed = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "ph"), "row:C"));
            var crossing = store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "ph"), "row:D", crossed.Hex));
            var apart = new[]
            {
                store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "pg"), "row:R")),
                store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "pg"), "row:U")),
            };
            var itemOnly = StoreComposer.RuleVersion(One, SwitchAt(Node), "sw:deny");
            var outOfForce = StoreComposer.RuleVersion(One, RowAt(Other, "pf"), "row:L");
            var inForce = new[] { byRecord, replacing, crossed, crossing, apart[0], apart[1], itemOnly }.Select(ItemOf).ToList();
            var first = store.Head;
            store.RestateAll(inForce.Concat(new[] { ItemOf(outOfForce) }));
            var older = store.Head;
            store.RestateAll(inForce, first.Hex);
            store.Add(older);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(
                    reading.InForce.HeadsInForce.Select(head => head.Hex), Does.Not.Contain(older.Hex),
                    "the summary entry carrying the version alone is out of force");
                var found = reading.InForce;
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "pf"))), Is.EqualTo(new[] { byRecord.Hex }), "by its record");
                Assert.That(
                    Ids(found.VersionsOf(RowAt(Node, "pg"))), Is.EqualTo(new[] { replacing.Hex }), "the version its object's version names does not stand");
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "ph"))), Is.EqualTo(new[] { crossed.Hex }), "named by a version of another object");
                Assert.That(
                    Ids(found.VersionsOf(RowAt(Other, "pg"))),
                    Is.EqualTo(apart.Select(version => version.Hex).OrderBy(id => id, System.StringComparer.Ordinal)),
                    "made apart, ascending by identifier");
                var carried = found.VersionsOf(SwitchAt(Node)).Single();
                Assert.That(carried.Id, Is.EqualTo(itemOnly.Hex), "by an item of a summary entry in force");
                Assert.That(carried.Members["doc"], Is.EqualTo("sw:deny"));
                Assert.That(carried.After, Is.Empty);
                Assert.That(
                    found.VersionsOf(RowAt(Node, "pg")).Single().Members["after"], Is.EqualTo(replaced.Hex), "what it names, as its record holds it");
                Assert.That(found.VersionsOf(RowAt(Other, "pf")), Is.Empty, "by an item of a summary entry out of force alone");
                Assert.That(
                    found.Objects,
                    Is.EquivalentTo(new[] { RowAt(Node, "pf"), RowAt(Node, "pg"), RowAt(Node, "ph"), RowAt(Other, "ph"), RowAt(Other, "pg"), SwitchAt(Node) }));
                Assert.That(Lost(found), Is.Empty, "nothing lost");
                Assert.That(found.VersionsOf(RowAt(Below, "pf")), Is.Empty, "an object nothing stands for");
                Assert.That(found.LostOf(RowAt(Below, "pf")), Is.Empty);
            }
        }

        [Test]
        public void A_version_present_beside_its_own_deletion_record_that_nothing_in_force_names_or_restates_is_lost_whatever_else_stands()
        {
            // `STORE.md` §617: told of by its own object, with nothing hashed. Restated by an item
            // of a summary entry in force it is in force, and named by a version in force of its
            // own object it is replaced; a version of another object naming it does neither.
            var store = new ComposedStore(One);
            var lost = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            store.Delete(lost);
            var standing = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:RU"));
            var restated = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:R"));
            store.Delete(restated);
            var named = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "ph"), "row:R"));
            store.Delete(named);
            var naming = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "ph"), "row:RU", named.Hex));
            var elsewhere = store.Add(StoreComposer.RuleVersion(One, SwitchAt(Node), "sw:deny"));
            store.Delete(elsewhere);
            var namingElsewhere = store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "pf"), "row:R", elsewhere.Hex));
            var outOfForce = StoreComposer.RuleVersion(One, RowAt(Other, "pg"), "row:L");
            store.Delete(outOfForce);
            var inForce = new[] { standing, restated, naming, namingElsewhere }.Select(ItemOf).ToList();
            var first = store.Head;
            store.RestateAll(inForce.Concat(new[] { ItemOf(outOfForce) }));
            var older = store.Head;
            store.RestateAll(inForce, first.Hex);
            store.Add(older);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var found = reading.InForce;
                Assert.That(found.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { lost.Hex }), "named and restated by nothing");
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "pf"))), Is.EqualTo(new[] { standing.Hex }), "and another version stands");
                Assert.That(found.LostOf(RowAt(Node, "pg")), Is.Empty, "restated by an item of a summary entry in force");
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "pg"))), Is.EqualTo(new[] { restated.Hex }));
                Assert.That(found.LostOf(RowAt(Node, "ph")), Is.Empty, "named by a version in force of its own object");
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "ph"))), Is.EqualTo(new[] { naming.Hex }));
                Assert.That(found.LostOf(SwitchAt(Node)), Is.EqualTo(new[] { elsewhere.Hex }), "named only by a version of another object");
                Assert.That(found.VersionsOf(SwitchAt(Node)), Is.Empty);
                Assert.That(
                    found.LostOf(RowAt(Other, "pg")), Is.EqualTo(new[] { outOfForce.Hex }), "carried by an item of a summary entry out of force alone");
                Assert.That(found.Objects, Does.Contain(SwitchAt(Node)).And.Contain(RowAt(Other, "pg")));
                Assert.That(RuleHashes(reading), Is.Empty, "nothing hashed");
                Assert.That(store.Walks, Is.EqualTo(0), "the tree is not asked for");
            }
        }

        [Test]
        public void A_deletion_record_that_nothing_in_force_names_tells_of_a_lost_row_and_a_lost_switch_whatever_else_stands()
        {
            // `STORE.md` §617: by the tag of `version`, the subject hash of `rule:` and a node, and
            // the tag of a name the file knows; and for a switch the subject hash of `switch:` and
            // a node, with 0.
            var store = new ComposedStore(One);
            var row = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            var turned = store.Add(StoreComposer.RuleVersion(One, SwitchAt(Node), "sw:inherit"));
            store.RestateAll(new[] { ItemOf(row), ItemOf(turned) });
            var rowTold = ToldOf(RowAt(Node, "pf"));
            var switchTold = ToldOf(SwitchAt(Node));
            store.Deleted.Add(rowTold);
            store.Deleted.Add(switchTold);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var found = reading.InForce;
                Assert.That(found.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { KeePassGate.Hex.Of(rowTold) }));
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "pf"))), Is.EqualTo(new[] { row.Hex }), "the version beside it still stands");
                Assert.That(found.LostOf(SwitchAt(Node)), Is.EqualTo(new[] { KeePassGate.Hex.Of(switchTold) }), "a lost switch");
                Assert.That(Ids(found.VersionsOf(SwitchAt(Node))), Is.EqualTo(new[] { turned.Hex }));
                Assert.That(found.LostOf(RowAt(Node, "pg")), Is.Empty, "a name the file does not know");
                Assert.That(store.Walks, Is.EqualTo(0), "each told at the node an object names");
            }
        }

        [Test]
        public void A_deletion_record_that_a_version_in_force_of_its_own_object_names_or_carries_tells_nothing_and_hashes_no_subject()
        {
            // `STORE.md` §617 and §620: a deletion record a version in force of the object names,
            // by its record or by an item of a summary entry in force, or that an item in force
            // carries, is answered for that object. A version present at its place places it with
            // no subject hashed, and the tags hashed are those of the names no row record shows.
            var store = new ComposedStore(One);
            var byRecord = ToldOf(RowAt(Node, "pf"));
            var byItem = ToldOf(RowAt(Node, "pg"));
            var namingByRecord = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:RU", KeePassGate.Hex.Of(byRecord)));
            var namingByItem = StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:RU", KeePassGate.Hex.Of(byItem));
            var carried = StoreComposer.RuleVersion(One, RowAt(Node, "ph"), "row:R");
            store.RestateAll(new[] { namingByRecord, namingByItem, carried }.Select(ItemOf));
            store.Deleted.Add(byRecord);
            store.Deleted.Add(byItem);
            store.Delete(carried);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var found = reading.InForce;
                Assert.That(Lost(found), Is.Empty, "nothing lost");
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "ph"))), Is.EqualTo(new[] { carried.Hex }), "carried by an item in force, it stands");
                Assert.That(
                    RuleHashes(reading), Is.EquivalentTo(new[] { "pg", "ph" }),
                    "no subject, and the tag of each name the file knows that no row record shows");
                Assert.That(store.Walks, Is.EqualTo(0), "the tree is not asked for");
            }
        }

        [Test]
        public void A_deletion_record_named_only_by_a_version_of_another_object_tells_of_the_object_whose_place_it_holds()
        {
            // `STORE.md` §617: a deletion record is answered by the versions of the objects at its
            // place, so an identifier a version at another place names answers nothing there.
            var store = new ComposedStore(One);
            var switchTold = ToldOf(SwitchAt(Node));
            var rowTold = ToldOf(RowAt(Node, "pg"));
            var row = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            var elsewhere = store.Add(StoreComposer.RuleVersion(
                One, RowAt(Other, "pg"), "row:R", KeePassGate.Hex.Of(switchTold), KeePassGate.Hex.Of(rowTold)));
            store.RestateAll(new[] { row, elsewhere }.Select(ItemOf));
            store.Deleted.Add(switchTold);
            store.Deleted.Add(rowTold);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var found = reading.InForce;
                Assert.That(found.LostOf(SwitchAt(Node)), Is.EqualTo(new[] { KeePassGate.Hex.Of(switchTold) }), "the switch");
                Assert.That(found.LostOf(RowAt(Node, "pg")), Is.EqualTo(new[] { KeePassGate.Hex.Of(rowTold) }), "the row");
                Assert.That(found.LostOf(RowAt(Other, "pg")), Is.Empty, "the object of the version naming them");
                Assert.That(Ids(found.VersionsOf(RowAt(Other, "pg"))), Is.EqualTo(new[] { elsewhere.Hex }));
            }
        }

        [Test]
        public void A_row_of_a_name_whose_tag_meets_a_lost_row_s_settles_the_loss_in_lost_for_its_own_row_alone()
        {
            // `STORE.md` §609 and §617. Two names whose tags meet under the key; the root grants
            // the first every letter; its empty rule at the node is deleted for good with the
            // summary entry restating it, so the deletion record tells of both rows. A version of
            // the second row naming that deletion record in `lost`, as a press on that row alone
            // writes it, settles the second row and not the first, until a version of the first
            // names it too: the file cannot tell whose the lost version was. In either order.
            var lost = KeePassGate.Hex.Of(ToldOf(RowAt(Node, MeetingFirst)));
            var above = MeetingAbove();
            var secondSet = StoreComposer.RuleVersion(One, RowAt(Node, MeetingSecond), "row:R", NoNames, new[] { lost });
            var firstSet = StoreComposer.RuleVersion(One, RowAt(Node, MeetingFirst), "row:", NoNames, new[] { lost });

            using (var reading = WithLost(lost, above))
            {
                Assert.That(reading.InForce.LostOf(RowAt(Node, MeetingFirst)), Is.EqualTo(new[] { lost }), "the row whose version was deleted");
                Assert.That(reading.InForce.LostOf(RowAt(Node, MeetingSecond)), Is.EqualTo(new[] { lost }), "the other row of its tag");
            }

            foreach (var (settled, refusing, version) in new[] { (MeetingSecond, MeetingFirst, secondSet), (MeetingFirst, MeetingSecond, firstSet) })
            {
                using (var reading = WithLost(lost, above.Concat(new[] { version }).ToArray()))
                {
                    Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                    Assert.That(reading.InForce.LostOf(RowAt(Node, settled)), Is.Empty, settled + ": settled by a version of its own row");
                    Assert.That(Ids(reading.InForce.VersionsOf(RowAt(Node, settled))), Is.EqualTo(new[] { version.Hex }));
                    Assert.That(reading.InForce.LostOf(RowAt(Node, refusing)), Is.EqualTo(new[] { lost }), refusing + ": the other row still refuses");
                    Assert.That(reading.InForce.VersionsOf(RowAt(Node, refusing)), Is.Empty);
                    Assert.That(RuleHashes(reading), Is.Empty, "placed by the version at its place, and each name's tag shown by a row record");
                }
            }

            using (var reading = WithLost(lost, above.Concat(new[] { secondSet, firstSet }).ToArray()))
            {
                Assert.That(Lost(reading.InForce), Is.Empty, "each row settled by a version of its own");
            }
        }

        [Test]
        public void A_deletion_record_a_version_at_its_place_names_in_after_is_answered_for_every_row_of_its_tag()
        {
            // `STORE.md` §617: a version names in `after` only versions of its own object that stood
            // with their content, so a deletion record a version of either row names there was that
            // row's, and neither row refuses for it. The second row's ordinary edit, as a press on
            // that row writes it: the version it replaced deleted with a deletion record, which
            // tells of both rows.
            var above = MeetingAbove();
            var firstRow = StoreComposer.RuleVersion(One, RowAt(Node, MeetingFirst), "row:RU");
            var replaced = StoreComposer.RuleVersion(One, RowAt(Node, MeetingSecond), "row:R");
            var replacing = StoreComposer.RuleVersion(One, RowAt(Node, MeetingSecond), "row:RU", replaced.Hex);

            using (var reading = WithLost(replaced.Hex, above.Concat(new[] { firstRow, replacing }).ToArray()))
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(Lost(reading.InForce), Is.Empty, "neither row refuses for the version the second replaced");
                Assert.That(Ids(reading.InForce.VersionsOf(RowAt(Node, MeetingFirst))), Is.EqualTo(new[] { firstRow.Hex }));
                Assert.That(Ids(reading.InForce.VersionsOf(RowAt(Node, MeetingSecond))), Is.EqualTo(new[] { replacing.Hex }));
            }

            // Named in `lost` instead, the same deletion record is settled for the namer alone.
            var settling = StoreComposer.RuleVersion(One, RowAt(Node, MeetingSecond), "row:RU", NoNames, new[] { replaced.Hex });
            using (var reading = WithLost(replaced.Hex, above.Concat(new[] { firstRow, settling }).ToArray()))
            {
                Assert.That(reading.InForce.LostOf(RowAt(Node, MeetingFirst)), Is.EqualTo(new[] { replaced.Hex }), "the first row refuses");
                Assert.That(reading.InForce.LostOf(RowAt(Node, MeetingSecond)), Is.Empty, "settled for the second");
            }
        }

        [Test]
        public void A_version_beside_its_own_deletion_record_is_answered_by_its_own_object_in_after_or_in_lost_and_by_no_other()
        {
            // `STORE.md` §617: a version present beside its deletion record is told of by its own
            // object alone, with nothing hashed, and a version in force of that object naming it
            // in `after` or in `lost` answers for it; a version of another row of its tag naming
            // it answers nothing.
            var above = MeetingAbove();
            var beside = StoreComposer.RuleVersion(One, RowAt(Node, MeetingFirst), "row:R");
            var ways = new Dictionary<string, (StoreComposer.Composed Version, bool Lost)>
            {
                { "its own row in after", (StoreComposer.RuleVersion(One, RowAt(Node, MeetingFirst), "row:RU", beside.Hex), false) },
                { "its own row in lost", (StoreComposer.RuleVersion(One, RowAt(Node, MeetingFirst), "row:RU", NoNames, new[] { beside.Hex }), false) },
                { "the other row in lost", (StoreComposer.RuleVersion(One, RowAt(Node, MeetingSecond), "row:RU", NoNames, new[] { beside.Hex }), true) },
            };

            foreach (var way in ways)
            {
                var store = new ComposedStore(One);
                foreach (var version in above.Concat(new[] { beside, way.Value.Version }))
                {
                    store.Add(version);
                }

                store.RestateAll(above.Concat(new[] { way.Value.Version }).Select(ItemOf));
                store.Delete(beside);
                using (var reading = store.Read())
                {
                    Assert.That(reading.State, Is.EqualTo(StoreState.Whole), way.Key + ": " + reading.Reason);
                    Assert.That(
                        reading.InForce.LostOf(RowAt(Node, MeetingFirst)),
                        Is.EqualTo(way.Value.Lost ? new[] { beside.Hex } : System.Array.Empty<string>()),
                        way.Key);
                    Assert.That(reading.InForce.LostOf(RowAt(Node, MeetingSecond)), Is.Empty, way.Key + ": the other row is not told of");
                }
            }
        }

        [Test]
        public void A_loss_at_the_place_a_tag_of_0_shares_with_the_object_with_no_slash_is_settled_in_lost_for_the_row_alone()
        {
            // `STORE.md` §609 and §617: the deletion record tells of the row whose name's tag is 0
            // and of the rule's object with no `/` at its node; named in `lost` by the row it is
            // settled for the row, and the object with no slash, which is no node's rule, still
            // refuses; named in `after` by a version of the object with no slash it is answered
            // for both.
            const string zero = "agent-144433";
            Assert.That(StoreComposer.ProfileTag(One, zero), Is.EqualTo(0), "the name the search found");
            var told = KeePassGate.Hex.Of(ToldOf(RowAt(Node, zero)));
            var bare = StoreComposer.RuleVersion(One, "rule:" + Node, "row:R");
            var zeroSet = StoreComposer.RuleVersion(One, RowAt(Node, zero), "row:R", NoNames, new[] { told });

            using (var reading = WithLost(told, bare, zeroSet))
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.LostOf(RowAt(Node, zero)), Is.Empty, "settled for the row");
                Assert.That(reading.InForce.LostOf("rule:" + Node), Is.EqualTo(new[] { told }), "the object with no slash");
            }

            var bareNaming = StoreComposer.RuleVersion(One, "rule:" + Node, "row:R", told);
            using (var reading = WithLost(told, bareNaming, StoreComposer.RuleVersion(One, RowAt(Node, zero), "row:R")))
            {
                Assert.That(Lost(reading.InForce), Is.Empty, "named in after at its place, answered for both");
            }
        }

        [Test]
        public void A_lost_version_of_the_switch_is_settled_by_a_version_of_the_switch_naming_it_in_lost()
        {
            // `STORE.md` §617: a deletion record under the subject of a switch tells of that switch
            // alone, and a version of the switch naming it in `lost` settles it.
            var told = KeePassGate.Hex.Of(ToldOf(SwitchAt(Node)));
            var row = StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R");

            using (var reading = WithLost(told, row))
            {
                Assert.That(reading.InForce.LostOf(SwitchAt(Node)), Is.EqualTo(new[] { told }));
            }

            var settling = StoreComposer.RuleVersion(One, SwitchAt(Node), "sw:deny", NoNames, new[] { told });
            using (var reading = WithLost(told, row, settling))
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(Lost(reading.InForce), Is.Empty, "settled by the switch");
                Assert.That(Ids(reading.InForce.VersionsOf(SwitchAt(Node))), Is.EqualTo(new[] { settling.Hex }));
            }
        }

        [Test]
        public void A_version_s_lost_is_restated_by_its_item_and_an_item_restating_it_otherwise_leaves_no_single_text()
        {
            // `STORE.md` §611 and §617: the item `version` restates `lost` as the version holds it,
            // and none where the version holds none; an item that restates it otherwise, by
            // holding another `lost` or none, restates the version otherwise.
            var told = KeePassGate.Hex.Of(ToldOf(RowAt(Node, "pf")));
            var settling = StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R", NoNames, new[] { told });

            var alike = new ComposedStore(One);
            alike.Add(settling);
            alike.Restate(ItemOf(settling));
            alike.Deleted.Add(StoreComposer.BytesOf(told));
            using (var reading = alike.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(Lost(reading.InForce), Is.Empty);
                Assert.That(
                    reading.InForce.Restated().Where(item => item.Kind == "version").Select(item => item.Member("lost")),
                    Is.EqualTo(new[] { told }),
                    "what a write restates holds the version's lost");
            }

            var otherwise = new ComposedStore(One);
            otherwise.Add(settling);
            otherwise.Restate(StoreComposer.RuleVersionItem(settling, RowAt(Node, "pf"), "row:R"));
            using (var reading = otherwise.Read())
            {
                Assert.That(reading.InForce.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { settling.Hex }), "an item with no lost restates it otherwise");
            }
        }

        [Test]
        public void A_version_whose_record_and_an_item_in_force_disagree_is_lost_whatever_order_their_entries_are_walked_in()
        {
            // A version's text is read from its record where a record carries it; an item of a
            // summary entry in force that restates it otherwise leaves no single text to read, so
            // its object refuses, and so does every object such an item names.
            var record = StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R");
            var differing = new Dictionary<string, (string Object, string Doc, string After)>
            {
                { "its text", (RowAt(Node, "pf"), "row:RU", string.Empty) },
                { "what it names", (RowAt(Node, "pf"), "row:R", new string('7', 32)) },
                { "its object", (RowAt(Node, "pg"), "row:R", string.Empty) },
            };

            foreach (var pair in differing)
            {
                foreach (var recordFirst in new[] { true, false })
                {
                    var store = new ComposedStore(One);
                    var item = StoreComposer.Item(
                        ("after", pair.Value.After), ("doc", pair.Value.Doc), ("id", record.Hex), ("kind", "version"), ("object", pair.Value.Object));
                    if (recordFirst)
                    {
                        store.Add(record);
                        store.Restate(item);
                    }
                    else
                    {
                        store.Restate(item);
                        store.Add(record);
                    }

                    var why = pair.Key + (recordFirst ? ", the record walked first" : ", the summary entry walked first");
                    using (var reading = store.Read())
                    {
                        Assert.That(reading.State, Is.EqualTo(StoreState.Whole), why + ": " + reading.Reason);
                        Assert.That(reading.InForce.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { record.Hex }), why);
                        Assert.That(reading.InForce.VersionsOf(RowAt(Node, "pf")), Is.Empty, why);
                        Assert.That(reading.InForce.LostOf(pair.Value.Object), Is.EqualTo(new[] { record.Hex }), why + ": the object the item names");
                    }
                }
            }
        }

        [Test]
        public void A_version_carried_by_items_in_force_that_disagree_is_lost_and_by_items_that_agree_stands_as_its_record_holds_it()
        {
            // Two summary entries in force, made apart, each restating a version: where they
            // restate it alike it stands, with the text of its record where a record carries it,
            // and where they restate it otherwise, with no record or beside one, it is lost.
            var version = StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R");
            foreach (var withRecord in new[] { true, false })
            {
                var alike = new ComposedStore(One);
                var start = alike.Head.Hex;
                if (withRecord)
                {
                    alike.Add(version);
                }

                alike.Restate(ItemOf(version));
                alike.Add(Head(One, 2, start, ItemOf(version)));
                using (var reading = alike.Read())
                {
                    Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                    Assert.That(reading.InForce.HeadsInForce, Has.Count.EqualTo(2), "two summary entries in force");
                    var standing = reading.InForce.VersionsOf(RowAt(Node, "pf")).Single();
                    Assert.That(standing.Id, Is.EqualTo(version.Hex), "restated alike, with a record: " + withRecord);
                    Assert.That(standing.Members["doc"], Is.EqualTo("row:R"));
                    Assert.That(reading.InForce.LostOf(RowAt(Node, "pf")), Is.Empty);
                }

                var apart = new ComposedStore(One);
                start = apart.Head.Hex;
                if (withRecord)
                {
                    apart.Add(version);
                }

                apart.Restate(ItemOf(version));
                apart.Add(Head(One, 2, start, StoreComposer.RuleVersionItem(version, RowAt(Node, "pf"), "row:RU")));
                using (var reading = apart.Read())
                {
                    Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                    Assert.That(reading.InForce.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { version.Hex }), "restated otherwise, with a record: " + withRecord);
                    Assert.That(reading.InForce.VersionsOf(RowAt(Node, "pf")), Is.Empty);
                }
            }
        }

        [Test]
        public void A_deletion_record_of_a_row_of_a_name_the_file_does_not_know_tells_nothing_until_the_name_is_known()
        {
            // `STORE.md` §617: the file knows a name from a record of kind `profile` or `removed`
            // present, in force or not, from an item `removed` of a summary entry present, and
            // from the object of a row's version present.
            var ways = new Dictionary<string, System.Action<ComposedStore>>
            {
                { "nothing", store => { } },
                { "an addition", store => store.Add(Profile("ops")) },
                { "an addition out of force", store => store.Delete(store.Add(Profile("ops"))) },
                { "a removal", store => store.Add(Removal("ops")) },
                { "a row at another node", store => store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "ops"), "row:R")) },
            };

            foreach (var way in ways)
            {
                var store = new ComposedStore(One);
                var row = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
                var told = ToldOf(RowAt(Node, "ops"));
                store.Deleted.Add(told);
                way.Value(store);

                using (var reading = store.Read())
                {
                    Assert.That(
                        reading.InForce.LostOf(RowAt(Node, "ops")),
                        Is.EqualTo(way.Key == "nothing" ? System.Array.Empty<string>() : new[] { KeePassGate.Hex.Of(told) }),
                        way.Key);
                    Assert.That(Ids(reading.InForce.VersionsOf(RowAt(Node, "pf"))), Is.EqualTo(new[] { row.Hex }), way.Key);
                }
            }

            var byItem = new ComposedStore(One);
            byItem.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            byItem.Restate(StoreComposer.Item(("id", new string('7', 32)), ("kind", "removed"), ("name", "ops")));
            var toldByItem = ToldOf(RowAt(Node, "ops"));
            byItem.Deleted.Add(toldByItem);
            using (var reading = byItem.Read())
            {
                Assert.That(reading.InForce.LostOf(RowAt(Node, "ops")), Is.EqualTo(new[] { KeePassGate.Hex.Of(toldByItem) }), "an item of a removal");
            }
        }

        [Test]
        public void A_deletion_record_under_a_tag_two_names_share_tells_of_the_rows_of_both()
        {
            // `STORE.md` §609 and §617: a tag can mean several names, so a deletion record tells
            // of every name of its tag the file knows, and each of those rows is lost. The names
            // were found by a search run once over `agent-` and a counter under the key `one`.
            Assert.That(StoreComposer.ProfileTag(One, "agent-455"), Is.EqualTo(StoreComposer.ProfileTag(One, "agent-604")), "the names the search found");
            var store = new ComposedStore(One);
            var first = store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "agent-455"), "row:R"));
            var second = store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "agent-604"), "row:R"));
            var turned = store.Add(StoreComposer.RuleVersion(One, SwitchAt(Node), "sw:inherit"));
            store.RestateAll(new[] { first, second, turned }.Select(ItemOf));
            var told = ToldOf(RowAt(Node, "agent-455"));
            store.Deleted.Add(told);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var found = reading.InForce;
                Assert.That(found.LostOf(RowAt(Node, "agent-455")), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }));
                Assert.That(found.LostOf(RowAt(Node, "agent-604")), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }), "the other name of the tag");
                Assert.That(found.LostOf(SwitchAt(Node)), Is.Empty);
                Assert.That(
                    Ids(found.VersionsOf(RowAt(Other, "agent-455"))), Is.EqualTo(new[] { first.Hex }), "a version carries its object, and is read by no tag");
                Assert.That(Ids(found.VersionsOf(RowAt(Other, "agent-604"))), Is.EqualTo(new[] { second.Hex }));
            }
        }

        [Test]
        public void A_tag_of_0_is_a_tag_and_tells_of_a_rule_s_object_with_no_slash_at_its_node_as_well()
        {
            // `STORE.md` §609: the ten keyed bytes do not tell a rule's object with no `/` from a
            // row at its node whose name's tag is 0, so a deletion record tells of each that the
            // file knows. The name was found by a search run once under the key `one`.
            Assert.That(StoreComposer.ProfileTag(One, "agent-144433"), Is.EqualTo(0), "the name the search found");
            var store = new ComposedStore(One);
            var bare = store.Add(StoreComposer.RuleVersion(One, "rule:" + Node, "row:R"));
            var known = store.Add(StoreComposer.RuleVersion(One, RowAt(Other, "agent-144433"), "row:R"));
            store.RestateAll(new[] { bare, known }.Select(ItemOf));
            var told = ToldOf(RowAt(Node, "agent-144433"));
            store.Deleted.Add(told);

            using (var reading = store.Read())
            {
                var found = reading.InForce;
                Assert.That(found.LostOf(RowAt(Node, "agent-144433")), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }), "a row whose name's tag is 0");
                Assert.That(found.LostOf("rule:" + Node), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }), "the object with no slash");
                Assert.That(Ids(found.VersionsOf("rule:" + Node)), Is.EqualTo(new[] { bare.Hex }));
                Assert.That(found.LostOf(SwitchAt(Node)), Is.Empty, "a switch has a subject of its own");
            }
        }

        [Test]
        public void The_tree_is_asked_for_only_where_a_deletion_record_is_left_that_no_object_the_file_names_explains()
        {
            // `STORE.md` §620: a deletion record at the place of a version present is placed with
            // nothing hashed; for the rest the nodes of the objects the file names are tried first,
            // and the tree is walked for its nodes' subjects only where a deletion record is still
            // not told of. A name's tag is read off a row record where one shows it and hashed
            // otherwise; each hash is kept with the key.
            Assert.That(StoreComposer.ProfileTag(One, "pf"), Is.Not.EqualTo(0), "a name whose tag is not 0, so that no switch is asked about");
            Assert.That(StoreComposer.ProfileTag(One, "pg"), Is.Not.EqualTo(StoreComposer.ProfileTag(One, "pf")));

            var none = Ruled(out _);
            using (var reading = none.Read())
            {
                Assert.That(RuleHashes(reading), Is.Empty, "no deletion record of a version: nothing hashed");
                Assert.That(none.Walks, Is.EqualTo(0));
            }

            var placed = Ruled(out _);
            placed.Deleted.Add(ToldOf(RowAt(Node, "pf")));
            using (var reading = placed.Read())
            {
                Assert.That(reading.InForce.LostOf(RowAt(Node, "pf")), Has.Count.EqualTo(1));
                Assert.That(placed.Walks, Is.EqualTo(0), "placed by the version at its place");
                Assert.That(RuleHashes(reading), Is.Empty, "nothing hashed, the name's tag shown by its row record");
            }

            var named = Ruled(out _);
            named.Add(Profile("pg"));
            named.Deleted.Add(ToldOf(RowAt(Node, "pg")));
            named.Deleted.Add(ToldOf(SwitchAt(Node)));
            using (var reading = named.Read())
            {
                Assert.That(reading.InForce.LostOf(RowAt(Node, "pg")), Has.Count.EqualTo(1), "a row at a node an object names");
                Assert.That(reading.InForce.LostOf(SwitchAt(Node)), Has.Count.EqualTo(1), "a switch at a node an object names");
                Assert.That(named.Walks, Is.EqualTo(0), "told at the node an object names");
                Assert.That(
                    RuleHashes(reading), Is.EquivalentTo(new[] { "rule:" + Node, "switch:" + Node, "pg" }),
                    "the subjects of each node the objects name, and the tag of the name no row record shows");
            }

            var below = Ruled(out _);
            var told = ToldOf(RowAt(Below, "pf"));
            below.Deleted.Add(told);
            var switchTold = ToldOf(SwitchAt(Below));
            below.Deleted.Add(switchTold);
            using (var reading = below.Read())
            {
                Assert.That(reading.InForce.LostOf(RowAt(Below, "pf")), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }), "a row at a node the tree holds");
                Assert.That(
                    reading.InForce.LostOf(SwitchAt(Below)), Is.EqualTo(new[] { KeePassGate.Hex.Of(switchTold) }), "a switch at a node the tree holds");
                Assert.That(below.Walks, Is.EqualTo(1), "walked once");
                Assert.That(
                    RuleHashes(reading),
                    Is.EquivalentTo(new[] { "rule:" + Node, "switch:" + Node, "rule:" + Root, "switch:" + Root, "rule:" + Below, "switch:" + Below }),
                    "each node the objects name, then each node of the tree not tried yet");
            }

            var nowhere = Ruled(out _);
            nowhere.Deleted.Add(ToldOf(RowAt(Other, "pf")));
            using (var reading = nowhere.Read())
            {
                Assert.That(Lost(reading.InForce), Is.Empty, "a node neither the objects nor the tree name: nothing lost");
                Assert.That(nowhere.Walks, Is.EqualTo(1));
            }
        }

        [Test]
        public void The_same_versions_stand_and_are_lost_before_and_after_a_write_deleted_what_was_replaced()
        {
            // `STORE.md` §617 and §627 step 3: a replaced version is no carrier, so the write that
            // deletes it, with a deletion record a version that stays names, changes no answer.
            var first = StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:R");
            var second = StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:RU", first.Hex);
            var store = Ruled(out _, second, StoreComposer.RuleVersion(One, RowAt(Other, "pf"), "row:L"));
            store.Add(first);
            store.Deleted.Add(ToldOf(RowAt(Below, "pf")));

            using (var before = store.Read())
            {
                Assert.That(before.State, Is.EqualTo(StoreState.Whole), before.Reason);
                Assert.That(before.InForce.LostOf(RowAt(Below, "pf")), Has.Count.EqualTo(1), "a row lost before the write");

                var write = StoreWrite.Plan(before, new[] { Retire('z') }, "0.3.0");
                Assert.That(write.Refused, Is.Null);
                Assert.That(write.Delete, Does.Contain(first.Hex), "the version replaced is deleted");
                var applied = write.Applied(before.Candidates, before.Deletions);
                using (var after = StoreReading.AfreshInTree(applied.Key, applied.Value, null, () => store.Tree))
                {
                    Assert.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);
                    Assert.That(Index(after.InForce), Is.EqualTo(Index(before.InForce)));
                }
            }
        }

        [Test]
        public void A_store_whose_deletion_records_are_the_plugin_s_own_asks_for_no_tree_and_hashes_nothing_at_any_hold()
        {
            // `STORE.md` §620: a write of the plugin's own deletes a version it replaces with a
            // deletion record that a version staying in force names, so a store it leaves hashes
            // no node, now or at a later hold.
            var first = StoreComposer.RuleVersion(One, RowAt(Node, "pg"), "row:R");
            var store = Ruled(out _, first);
            var replacing = StoreRecord.Compose(One, "version", new Dictionary<string, string>
            {
                { "after", first.Hex },
                { "doc", "row:RU" },
                { "object", RowAt(Node, "pg") },
            });
            var walks = 0;
            System.Func<IEnumerable<byte[]>> tree = () =>
            {
                walks++;
                return store.Tree;
            };

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var write = StoreWrite.Plan(
                    reading, new[] { new StoreAct("a write of the test's", One, StoreFormat.KeyName(One), new[] { replacing }, null) }, "0.3.0");
                Assert.That(write.Refused, Is.Null);
                Assert.That(write.Delete, Does.Contain(first.Hex), "the write deletes the version it replaces");
                var applied = write.Applied(reading.Candidates, reading.Deletions);

                var kept = new StoreKept();
                using (StoreReading.Of(applied.Key, applied.Value, null, kept, tree))
                using (var later = StoreReading.Of(applied.Key, applied.Value, kept, new StoreKept(), tree))
                {
                    Assert.That(later.State, Is.EqualTo(StoreState.Whole), later.Reason);
                    Assert.That(Ids(later.InForce.VersionsOf(RowAt(Node, "pg"))), Is.EqualTo(new[] { replacing.Hex }));
                    Assert.That(Lost(later.InForce), Is.Empty);
                    Assert.That(walks, Is.EqualTo(0), "the tree is not asked for");
                    Assert.That(RuleHashes(later), Is.Empty, "nothing hashed");
                }
            }
        }

        [Test]
        public void A_deletion_record_the_file_holds_twice_tells_of_what_it_tells_of_once()
        {
            // A file can hold a deletion record twice, as two copies merged can leave it. The
            // reading passes over the deletion records as they are listed, and what they tell of
            // is each told once: a summary entry, a lost row and a removal.
            var store = new ComposedStore(One);
            var first = store.Head;
            var standing = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            store.RestateAll(new[] { ItemOf(standing) });
            store.Deleted.Add(first.Identifier);
            var rowTold = ToldOf(RowAt(Node, "pf"));
            store.Deleted.Add(rowTold);
            store.Deleted.Add(rowTold);
            var removal = Removal("ops");
            store.Delete(removal);
            store.Delete(removal);
            store.Add(Profile("ops"));

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(store.Deleted.Count(identifier => identifier.SequenceEqual(first.Identifier)), Is.EqualTo(2), "the summary entry deleted twice");
                Assert.That(reading.InForce.Told, Is.EqualTo(new[] { first.Hex }), "the summary entry told of once");
                Assert.That(reading.InForce.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { KeePassGate.Hex.Of(rowTold) }), "the row lost once");
                Assert.That(reading.InForce.RemovalsOnceKnown("ops"), Is.EqualTo(new[] { removal.Hex }), "the removal known once");
            }
        }

        [Test]
        public void A_version_whose_id_is_no_identifier_places_no_deletion_record_and_stands()
        {
            // An item's `id` is a text, `STORE.md` §658, so a version carried by an item alone may
            // be under a text no identifier is, as short as a word. It stands where it is in force
            // and nothing names it, and it is no place a deletion record lies at, so a deletion
            // record of a version beside it is told of by the versions whose identifiers are
            // identifiers, as it is without it.
            var store = new ComposedStore(One);
            var standing = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            var odd = StoreComposer.Item(("after", string.Empty), ("doc", "row:R"), ("id", "odd"), ("kind", "version"), ("object", RowAt(Other, "pf")));
            store.RestateAll(new[] { ItemOf(standing), odd });
            var told = ToldOf(RowAt(Node, "pf"));
            store.Deleted.Add(told);

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var found = reading.InForce;
                Assert.That(Ids(found.VersionsOf(RowAt(Other, "pf"))), Is.EqualTo(new[] { "odd" }), "carried by an item in force alone, it stands");
                Assert.That(found.LostOf(RowAt(Node, "pf")), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }), "the deletion record at the row's place");
                Assert.That(Ids(found.VersionsOf(RowAt(Node, "pf"))), Is.EqualTo(new[] { standing.Hex }));
                Assert.That(store.Walks, Is.EqualTo(0), "placed by the version at its place");
            }
        }

        [Test]
        public void A_deletion_record_of_an_answer_tells_of_its_object_by_each_way_the_file_knows_the_client_key()
        {
            // `STORE.md` §618: a deletion record under the tag of `version` with the subject hash
            // of `renew:` and a client key and 0 tells of that client's answer where the file
            // knows the key: from the `client` of a record of kind `client` present, in force or
            // not, of a record `revoked` and of an item `revoked`. A key the file does not know
            // tells of nothing, and the same deletion record refuses from the hold in which the
            // file knows the key again.
            var ways = new Dictionary<string, System.Action<ComposedStore>>
            {
                { "nothing", store => { } },
                { "an admission", store => store.Add(AdmissionOf(Hex('e'))) },
                { "an admission out of force", store => store.Delete(store.Add(AdmissionOf(Hex('e')))) },
                { "a revocation", store => store.Revoked('e') },
                { "an item of a revocation", store => store.Restate(StoreComposer.Item(("client", Hex('e')), ("kind", "revoked"))) },
                { "an admission of a key in upper case", store => store.Add(AdmissionOf(Hex('E'))) },
            };

            foreach (var way in ways)
            {
                var store = new ComposedStore(One);
                var told = ToldOf(AnswerOf(Hex('e')));
                store.Deleted.Add(told);
                way.Value(store);

                using (var reading = store.Read())
                {
                    Assert.That(reading.State, Is.EqualTo(StoreState.Whole), way.Key + ": " + reading.Reason);
                    var known = way.Key != "nothing" && !way.Key.Contains("upper case");
                    Assert.That(
                        reading.InForce.LostOf(AnswerOf(Hex('e'))),
                        Is.EqualTo(known ? new[] { KeePassGate.Hex.Of(told) } : System.Array.Empty<string>()),
                        way.Key);
                    Assert.That(RenewHashes(reading), Is.EqualTo(known ? new[] { AnswerOf(Hex('e')) } : System.Array.Empty<string>()), way.Key + ": each key known, once");
                    Assert.That(
                        store.Walks, Is.EqualTo(known ? 0 : 1),
                        way.Key + (known ? ": the tree is not asked for" : ": the tree is asked for, as for a deletion record nothing explains"));
                }
            }
        }

        [Test]
        public void A_deletion_record_at_the_place_of_an_answer_present_is_that_answer_s_and_costs_no_hash()
        {
            // `STORE.md` §618 and §620: an answer's subject is its whole object, so a version of it
            // present places every deletion record of its object with nothing hashed.
            var store = new ComposedStore(One);
            var standing = store.Add(StoreComposer.RuleVersion(One, AnswerOf(Hex('e')), "refuse:forever"));
            store.RestateAll(new[] { ItemOf(standing) });
            var told = ToldOf(AnswerOf(Hex('e')));
            store.Deleted.Add(told);
            store.Tree.AddRange(new[] { Root, Node }.Select(StoreComposer.BytesOf));

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.LostOf(AnswerOf(Hex('e'))), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }));
                Assert.That(Ids(reading.InForce.VersionsOf(AnswerOf(Hex('e')))), Is.EqualTo(new[] { standing.Hex }), "the version beside it still stands");
                Assert.That(RenewHashes(reading), Is.Empty, "nothing hashed");
                Assert.That(store.Walks, Is.EqualTo(0), "the tree is not asked for");
            }
        }

        [Test]
        public void A_store_whose_answers_the_plugin_s_own_writes_replaced_hashes_nothing_and_walks_no_tree()
        {
            // `STORE.md` §620 and §627 step 3: the version an answer replaces is deleted with a
            // deletion record that the version replacing it names, at its place.
            var store = new ComposedStore(One);
            store.Add(AdmissionOf(Hex('e')));
            var first = StoreComposer.RuleVersion(One, AnswerOf(Hex('e')), "refuse:forever");
            var lift = store.Add(StoreComposer.RuleVersion(One, AnswerOf(Hex('e')), "ask", first.Hex));
            var again = store.Add(StoreComposer.RuleVersion(One, AnswerOf(Hex('f')), "refuse:until:2026-12-14T23:00:00Z"));
            store.RestateAll(new[] { ItemOf(lift), ItemOf(again) });
            store.Delete(first);
            store.Tree.AddRange(new[] { Root, Node }.Select(StoreComposer.BytesOf));

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(Lost(reading.InForce), Is.Empty);
                Assert.That(Ids(reading.InForce.VersionsOf(AnswerOf(Hex('e')))), Is.EqualTo(new[] { lift.Hex }));
                Assert.That(RenewHashes(reading), Is.Empty, "nothing hashed");
                Assert.That(store.Walks, Is.EqualTo(0), "the tree is not asked for");
            }
        }

        [Test]
        public void A_lost_answer_is_told_before_the_tree_is_walked_and_a_row_lost_below_still_walks_it()
        {
            // `STORE.md` §620: the client keys are tried after the nodes the objects name and
            // before the tree.
            var answer = Ruled(out _);
            answer.Add(AdmissionOf(Hex('e')));
            var told = ToldOf(AnswerOf(Hex('e')));
            answer.Deleted.Add(told);
            using (var reading = answer.Read())
            {
                Assert.That(reading.InForce.LostOf(AnswerOf(Hex('e'))), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }));
                Assert.That(answer.Walks, Is.EqualTo(0), "told by a client key the file knows");
                Assert.That(RenewHashes(reading), Is.EqualTo(new[] { AnswerOf(Hex('e')) }));
            }

            var both = Ruled(out _);
            both.Add(AdmissionOf(Hex('e')));
            both.Deleted.Add(told);
            var below = ToldOf(RowAt(Below, "pf"));
            both.Deleted.Add(below);
            using (var reading = both.Read())
            {
                Assert.That(reading.InForce.LostOf(AnswerOf(Hex('e'))), Is.EqualTo(new[] { KeePassGate.Hex.Of(told) }));
                Assert.That(reading.InForce.LostOf(RowAt(Below, "pf")), Is.EqualTo(new[] { KeePassGate.Hex.Of(below) }));
                Assert.That(both.Walks, Is.EqualTo(1), "walked once, for the row");
            }
        }

        /// <summary>The object of the answers about a client.</summary>
        private static string AnswerOf(string client)
        {
            return "renew:" + client;
        }

        /// <summary>A record of kind `client` of this client key, admitting a certificate of the same key.</summary>
        private static StoreComposer.Composed AdmissionOf(string client)
        {
            return StoreComposer.Record(
                One, "client", "client:" + client, StoreComposer.Content(("certificate", client), ("client", client)));
        }

        /// <summary>What was hashed under the reading's key for the answers to renewals and kept with it.</summary>
        private static IEnumerable<string> RenewHashes(StoreReading reading)
        {
            return Subjects(reading.InForce.Key).Where(subject => subject.StartsWith("renew:", System.StringComparison.Ordinal)).ToList();
        }

        /// <summary>The node most rules here are at, its identifier as 32 hexadecimal characters.</summary>
        private static readonly string Node = new string('1', 32);

        /// <summary>A node no tree here holds.</summary>
        private static readonly string Other = new string('2', 32);

        /// <summary>A node of the tree that no object of <see cref="Ruled"/> names.</summary>
        private static readonly string Below = new string('3', 32);

        /// <summary>The tree's root.</summary>
        private static readonly string Root = new string('0', 32);

        private static string RowAt(string node, string profile)
        {
            return StoreComposer.RowObject(node, profile);
        }

        private static string SwitchAt(string node)
        {
            return StoreComposer.SwitchObject(node);
        }

        /// <summary>
        /// A store under `One` with a row standing at <see cref="Node"/>, beside a tree of the
        /// root, that node and <see cref="Below"/>.
        /// </summary>
        /// <param name="more">Versions put beside it, each restated with it by a single summary entry.</param>
        private static ComposedStore Ruled(out StoreComposer.Composed standing, params StoreComposer.Composed[] more)
        {
            var store = new ComposedStore(One);
            standing = store.Add(StoreComposer.RuleVersion(One, RowAt(Node, "pf"), "row:R"));
            foreach (var version in more)
            {
                store.Add(version);
            }

            store.RestateAll(new[] { standing }.Concat(more).Select(ItemOf));
            store.Tree.AddRange(new[] { Root, Node, Below }.Select(StoreComposer.BytesOf));
            return store;
        }

        /// <summary>
        /// A store under `One` of these versions, each restated by a single summary entry, beside a
        /// deletion record of this identifier and a tree of the root and <see cref="Node"/>.
        /// </summary>
        private static StoreReading WithLost(string lost, params StoreComposer.Composed[] versions)
        {
            var store = new ComposedStore(One);
            foreach (var version in versions)
            {
                store.Add(version);
            }

            store.RestateAll(versions.Select(ItemOf));
            store.Deleted.Add(StoreComposer.BytesOf(lost));
            store.Tree.AddRange(new[] { Root, Node }.Select(StoreComposer.BytesOf));
            return store.Read();
        }

        /// <summary>
        /// The identifier of a deletion record of a version of this object whose content is
        /// nowhere: its place under `One`, as the composer places a version, and the start of a
        /// check no record here has.
        /// </summary>
        private static byte[] ToldOf(string objectName)
        {
            var slash = objectName.StartsWith("rule:", System.StringComparison.Ordinal) ? objectName.IndexOf('/') : -1;
            return slash < 0
                ? StoreComposer.IdentifierOf(One, "version", objectName, 0, new string('5', 64))
                : StoreComposer.IdentifierOf(
                    One, "version", objectName.Substring(0, slash), StoreComposer.ProfileTag(One, objectName.Substring(slash + 1)), new string('5', 64));
        }

        /// <summary>The item `version` that restates this version, as its record holds it, `lost` among it where the record holds it.</summary>
        private static string ItemOf(StoreComposer.Composed version)
        {
            var content = JObject.Parse(version.Strings[StoreComposer.Data]);
            return StoreComposer.RuleVersionItem(
                version, (string)content["object"], (string)content["doc"], Listed((string)content["after"]), Listed((string)content["lost"]));
        }

        /// <summary>The identifiers a list holds, none for a list that is empty or absent.</summary>
        private static string[] Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? System.Array.Empty<string>() : list.Split(' ');
        }

        /// <summary>
        /// The first of two names whose tags meet under `One`, found by a search run once over
        /// `agent-` and a counter under that key.
        /// </summary>
        private const string MeetingFirst = "agent-455";

        /// <summary>The second of the two names whose tags meet under `One`.</summary>
        private const string MeetingSecond = "agent-604";

        private static readonly string[] NoNames = System.Array.Empty<string>();

        /// <summary>
        /// The root's rows of the two names whose tags meet: the first granted every letter and the
        /// second read, the tags asserted to meet.
        /// </summary>
        private static StoreComposer.Composed[] MeetingAbove()
        {
            Assert.That(StoreComposer.ProfileTag(One, MeetingFirst), Is.EqualTo(StoreComposer.ProfileTag(One, MeetingSecond)), "the names a search under the key found");
            return new[]
            {
                StoreComposer.RuleVersion(One, RowAt(Root, MeetingFirst), "row:CRUSDLAP"),
                StoreComposer.RuleVersion(One, RowAt(Root, MeetingSecond), "row:R"),
            };
        }

        private static StoreComposer.Composed Profile(string name)
        {
            return StoreComposer.Record(One, "profile", "profile:" + name, StoreComposer.Content(("after", string.Empty), ("name", name)));
        }

        private static StoreComposer.Composed Removal(string name)
        {
            return StoreComposer.Record(One, "removed", "profile:" + name, StoreComposer.Content(("name", name)));
        }

        /// <summary>An act retiring an authority, which touches no version.</summary>
        private static StoreAct Retire(char authority)
        {
            var record = StoreRecord.Compose(One, "retired", new Dictionary<string, string> { { "authority", Hex(authority) } });
            return new StoreAct("a write of the test's", One, StoreFormat.KeyName(One), new[] { record }, null);
        }

        private static IEnumerable<string> Ids(IEnumerable<StoreNaming> versions)
        {
            return versions.Select(version => version.Id);
        }

        /// <summary>What stands and what is lost, object by object, as lines.</summary>
        private static IEnumerable<string> Index(StoreInForce found)
        {
            return found.Objects
                .OrderBy(objectText => objectText, System.StringComparer.Ordinal)
                .Select(objectText => objectText
                                      + " stands " + string.Join(" ", Ids(found.VersionsOf(objectText)))
                                      + " lost " + string.Join(" ", found.LostOf(objectText)))
                .ToList();
        }

        /// <summary>The identifiers lost, of every object.</summary>
        private static IEnumerable<string> Lost(StoreInForce found)
        {
            return found.Objects.SelectMany(objectText => found.LostOf(objectText)).ToList();
        }

        /// <summary>
        /// What was hashed under the reading's key for the rules and kept with it: the subjects of
        /// a node's rows and of its switch, and the names whose tags were made.
        /// </summary>
        private static IEnumerable<string> RuleHashes(StoreReading reading)
        {
            var key = reading.InForce.Key;
            var names = typeof(FoundKey).GetField("_names", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return Subjects(key)
                .Where(subject => subject.StartsWith("rule:", System.StringComparison.Ordinal) || subject.StartsWith("switch:", System.StringComparison.Ordinal))
                .Concat(((Dictionary<string, int>)names.GetValue(key)).Keys)
                .ToList();
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

        private static StoreReading Read(JObject store)
        {
            return RuleVectors.Reading(store);
        }
    }
}
