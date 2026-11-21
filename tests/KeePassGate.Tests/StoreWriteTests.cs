using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The plan of a write of the plugin's own, `STORE.md` §627: each step alone, each state's
    /// refusal, the version record, the summary entry, the ceilings, and the room it agrees with.
    /// </summary>
    [TestFixture]
    public sealed class StoreWriteTests
    {
        private const string Plugin = "0.3.0";

        [Test]
        public void A_Create_where_no_key_stands_starts_a_store_with_a_version_record_and_a_summary_entry()
        {
            var key = StoreFormat.NewKey();
            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Create(key, 'a') }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Starts, Is.True);
            Assert.That(write.Add.Select(record => record.Kind), Is.EqualTo(new[] { "format", "authority", "head" }));
            var head = SummaryOf(write);
            Assert.That(head.Member("height"), Is.EqualTo("1"));
            Assert.That(head.Member("after"), Is.Empty);
            Assert.That(Items(head), Is.EqualTo(new[] { "{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.3.0\"}" }));
            Assert.That(write.Delete, Is.Empty);
            Assert.That(write.Deletions, Is.Empty);
        }

        [Test]
        public void A_store_written_reads_whole_with_its_authority_standing()
        {
            var store = new MemoryStore();
            var key = StoreFormat.NewKey();
            Assert.That(store.Write(new[] { Create(key, 'a') }).Taken, Is.True);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.Answering.Select(record => record.Form.Member("id")), Is.EqualTo(new[] { Hex('a') }));
        }

        [Test]
        public void Every_state_but_a_whole_store_and_no_key_refuses_a_write_with_its_word_Create_among_them()
        {
            var split = new ComposedStore(One);
            split.Add(StoreRecords.Authority(Two, 'b'));
            AssertRefused(split.Read(), Create(One, 'c'), "split");

            AssertRefused(new ComposedStore(One, StoreFormat.Number + 1).Read(), Create(One, 'c'), "outdated");

            // A stopped store with no authority standing: Create is refused all the same, `STORE.md`
            // §624.
            var stopped = new ComposedStore(One);
            stopped.Drop(stopped.Authority);
            stopped.Add(StoreComposer.Record(
                One, "retired", "authority:" + Hex('a'),
                StoreComposer.Content(("authority", Hex('a')), ("storeKey", StoreComposer.HexOf(One)))));
            AssertRefused(stopped.Read(), Create(One, 'c'), "stopped");
        }

        [Test]
        public void Where_no_key_stands_only_an_act_that_brings_an_authority_is_taken()
        {
            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Starting(One, Retirement(One, 'a')) }, Plugin);

            Assert.That(write.Refused, Does.Contain("only an authority starts a store"));
        }

        [Test]
        public void A_Create_composed_under_a_store_key_is_left_out_where_no_key_stands_and_starts_nothing()
        {
            // `STORE.md` §614 and §632: Create starts a store under a new key where no key stands,
            // and a Create composed under the key that stood at the press, planned once that key
            // went, would start the store again under it, with every record still in the file.
            var key = StoreFormat.NewKey();
            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Act(key, AuthorityOf(key, 'a')) }, Plugin);

            Assert.That(write.Refused, Does.Contain("composed under a store key that does not stand"));
            Assert.That(write.Starts, Is.False);
        }

        [Test]
        public void An_act_composed_where_no_key_stood_is_left_out_where_a_key_stands()
        {
            var store = Started(out var key, 'a');

            var write = StoreWrite.Plan(store.Read(), new[] { Starting(key, Retirement(key, 'b')) }, Plugin);

            Assert.That(write.Refused, Does.Contain("composed where no store key stood"));
        }

        [Test]
        public void A_retirement_dropped_with_the_authority_it_retires_leaves_its_hold_on_the_key_to_the_retirements_left()
        {
            // A Recreate and then a Delete on one dialog, `STORE.md` §627: the authority the
            // Recreate made goes with the Delete's retirement, which held the key, so the
            // Recreate's retirement holds it in their place and the write is the Delete of what
            // stood.
            var store = Started(out var key, 'a');
            var recreate = new StoreAct(
                "Recreate", key, StoreFormat.KeyName(key), new[] { WithoutKey(key, 'a'), AuthorityOf(key, 'b') }, null);

            var write = StoreWrite.Plan(store.Read(), new[] { recreate, Retire(key, 'b') }, Plugin);

            Assert.That(write.Refused, Is.Null);
            var retirements = write.Add.Where(record => record.Kind == "retired").ToList();
            Assert.That(retirements.Select(record => record.Member("authority")), Is.EqualTo(new[] { Hex('a') }));
            Assert.That(retirements.Single().Protected, Is.True, "the retirement left holds the key");
            Assert.That(write.Add.Any(record => record.Kind == "authority"), Is.False);
        }

        [Test]
        public void An_entry_still_in_the_tree_gets_no_deletion_record_and_no_second_entry_under_its_identifier()
        {
            // `STORE.md` §627: present is every entry of the tree, a record or not. A summary entry
            // that a later summary entry names, back in the tree with every string of the store
            // gone and no deletion record, is neither told of as deleted nor written again.
            var store = Started(out var key, 'a');
            Assert.That(store.Write(new[] { Retire(key, 'z') }).Taken, Is.True);
            var first = store.Deletions.Single();
            var tree = store.Candidates.Select(candidate => candidate.Identifier).Concat(new[] { first }).ToList();

            using (var reading = StoreReading.Afresh(store.Candidates, Array.Empty<byte[]>(), tree))
            {
                Assert.That(StoreWrite.Plan(reading, new[] { Retire(key, 'y') }, Plugin).Deletions, Does.Not.Contain(KeePassGate.Hex.Of(first)));
            }

            using (var reading = StoreReading.Afresh(store.Candidates, Array.Empty<byte[]>()))
            {
                Assert.That(
                    StoreWrite.Plan(reading, new[] { Retire(key, 'y') }, Plugin).Deletions,
                    Does.Contain(KeePassGate.Hex.Of(first)),
                    "an entry the tree does not hold is told of");
            }

            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            using (var reading = StoreReading.Afresh(store.Candidates, store.Deletions, tree.Concat(new[] { name.Identifier })))
            {
                Assert.That(StoreWrite.Plan(reading, new[] { Act(key, name) }, Plugin).Empty, Is.True);
            }
        }

        [Test]
        public void An_act_whose_ground_went_is_left_out_and_named_and_a_single_act_is_refused_for_it()
        {
            var store = Started(out var key, 'a');
            var stale = new StoreAct(
                "a stale act", key, StoreFormat.KeyName(key), new[] { Retirement(key, 'a') }, ground => ground.Standing.Contains(Hex('z')) ? null : "z does not stand");

            var alone = StoreWrite.Plan(store.Read(), new[] { stale }, Plugin);
            Assert.That(alone.Refused, Is.EqualTo("a stale act: z does not stand"));

            var beside = StoreWrite.Plan(store.Read(), new[] { stale, Retire(key, 'a') }, Plugin);
            Assert.That(beside.Refused, Is.Null);
            Assert.That(beside.LeftOut, Is.EqualTo(new[] { "a stale act: z does not stand" }));
            Assert.That(beside.Taken, Has.Count.EqualTo(1));
        }

        [Test]
        public void An_act_composed_under_a_key_that_does_not_stand_is_left_out()
        {
            var store = Started(out var key, 'a');

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(StoreFormat.NewKey(), 'a') }, Plugin);

            Assert.That(write.Refused, Does.Contain("under a store key that does not stand"));
        }

        [Test]
        public void An_act_is_held_to_its_ground_with_the_acts_before_it_taken()
        {
            var store = new MemoryStore();
            var key = StoreFormat.NewKey();
            var create = Create(key, 'a');
            var needsA = new StoreAct(
                "needs a", key, StoreFormat.KeyName(key), new[] { Retirement(key, 'a') }, ground => ground.Standing.Contains(Hex('a')) ? null : "a does not stand");

            var write = StoreWrite.Plan(store.Read(), new[] { create, needsA }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Taken, Has.Count.EqualTo(2));
            Assert.That(write.Add.Select(record => record.Kind), Does.Not.Contain("authority"), "an authority added and retired in one write is never added");
        }

        [Test]
        public void A_retirement_deletes_the_authority_it_names_and_every_summary_entry_present()
        {
            var store = Started(out var key, 'a');
            var before = store.Read();
            var authority = before.InForce.Authorities[0].Hex;
            var head = before.InForce.Heads[0].Hex;

            var write = StoreWrite.Plan(before, new[] { Retire(key, 'a') }, Plugin);

            Assert.That(write.Delete, Is.EquivalentTo(new[] { authority, head }));
            Assert.That(SummaryOf(write).Member("after"), Is.EqualTo(head));
            Assert.That(SummaryOf(write).Member("height"), Is.EqualTo("2"));
            Assert.That(Items(SummaryOf(write)), Does.Contain("{\"authority\":\"" + Hex('a') + "\",\"kind\":\"retired\"}"));

            Assert.That(store.Write(new[] { Retire(key, 'a') }).Taken, Is.True);
            var after = store.Read();
            Assert.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);
            Assert.That(after.Answering, Is.Empty);
            Assert.That(after.Found(key), Is.EqualTo(1), "the retirement holds the key");
        }

        [Test]
        public void A_write_that_would_leave_no_entry_holding_the_store_key_is_refused()
        {
            var store = Started(out var key, 'a');
            var bare = StoreRecord.Compose(key, "retired", new Dictionary<string, string> { { "authority", Hex('a') } });

            var write = StoreWrite.Plan(store.Read(), new[] { Act(key, bare) }, Plugin);

            Assert.That(write.Refused, Is.EqualTo("the write would leave no entry holding the store key"));
        }

        [Test]
        public void A_record_the_file_holds_or_a_deletion_record_names_is_not_written_again()
        {
            // `STORE.md` §627: a record is written once. The identifier is the record's own, so a
            // second entry under it would be two entries the tree tells apart by nothing, and a
            // record a deletion record names would be added only to be out of force.
            var store = Started(out var key, 'a');
            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            Assert.That(store.Write(new[] { Act(key, name) }).Taken, Is.True);

            var again = StoreWrite.Plan(store.Read(), new[] { Act(key, name) }, Plugin);
            Assert.That(again.Empty, Is.True, "the record the file holds");

            store.Drop(name.Hex);
            store.Delete(name.Identifier);
            var told = StoreWrite.Plan(store.Read(), new[] { Act(key, name) }, Plugin);
            Assert.That(told.Empty, Is.True, "the record a deletion record names");

            var beside = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "writer" } });
            var both = StoreWrite.Plan(store.Read(), new[] { Act(key, name), Act(key, beside), Act(key, beside) }, Plugin);
            Assert.That(both.Add.Where(record => record.Kind == "profile").Select(record => record.Hex), Is.EqualTo(new[] { beside.Hex }),
                "and a record an act before it in the same write adds, once");
        }

        [Test]
        public void A_summary_entry_named_and_not_in_the_file_gets_a_deletion_record()
        {
            var store = new ComposedStore(One);
            store.Restate();
            var named = store.Head.Strings[StoreComposer.Data];
            var reading = store.Read();
            var earlier = reading.InForce.After.Single(identifier => identifier != store.Head.Hex);

            var write = StoreWrite.Plan(reading, new[] { Retire(One, 'a') }, Plugin);

            Assert.That(named, Does.Contain(earlier));
            Assert.That(write.Deletions, Is.Empty, "the file holds its deletion record already");

            var dropped = new ComposedStore(One);
            dropped.Drop(dropped.Head);
            dropped.Add(Head(One, 1, new string('7', 32)));
            Assert.That(StoreWrite.Plan(dropped.Read(), new[] { Retire(One, 'a') }, Plugin).Deletions, Is.EqualTo(new[] { new string('7', 32) }));
        }

        [Test]
        public void The_version_record_is_written_at_the_first_write_and_by_a_higher_plugin_and_by_no_other()
        {
            var store = Started(out var key, 'a');
            Assert.That(store.Read().InForce.Plugin, Is.EqualTo("0.3.0"));

            var same = StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "0.3.0");
            Assert.That(same.Add.Select(record => record.Kind), Does.Not.Contain("format"));

            var lower = StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "0.2.9");
            Assert.That(lower.Add.Select(record => record.Kind), Does.Not.Contain("format"));
            Assert.That(Items(SummaryOf(lower)), Does.Contain("{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.3.0\"}"), "the highest stated");

            var higher = StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "0.10.0");
            Assert.That(higher.Add.Count(record => record.Kind == "format"), Is.EqualTo(1));
            Assert.That(Items(SummaryOf(higher)), Does.Contain("{\"format\":\"1\",\"kind\":\"format\",\"plugin\":\"0.10.0\"}"));

            Assert.That(StoreWrite.Plan(store.Read(), new[] { Retire(key, 'a') }, "unknown").Refused, Does.Contain("cannot state its version"));
        }

        [Test]
        public void The_height_is_a_step_above_the_highest_and_stays_at_the_last()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            store.Add(Head(One, 65535, string.Empty));

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(SummaryOf(write).Member("height"), Is.EqualTo("65535"));
        }

        [Test]
        public void Items_of_every_kind_are_carried_whatever_this_build_gives_them_to_mean()
        {
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            var version = store.Add(Version(One, "rule:" + new string('1', 32), string.Empty));
            var kept = store.Kept(One, Three);
            var removal = store.Add(StoreComposer.Record(One, "removed", "profile:reader", StoreComposer.Content(("name", "reader")))).Hex;
            store.Add(Head(
                One, 2, string.Empty,
                VersionItem(version, "rule:" + new string('1', 32), string.Empty),
                KeptItem(kept, Three),
                StoreComposer.Item(("id", removal), ("kind", "removed"), ("name", "reader")),
                StoreComposer.Item(("client", Hex('c')), ("kind", "revoked")),
                Statement(new string('6', 32), Hex('d'), Hex('1'), Hex('2'))));
            Assume.That(store.Read().State, Is.EqualTo(StoreState.Whole), store.Read().Reason);

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin);

            var items = Items(SummaryOf(write));
            Assert.That(items, Does.Contain(VersionItem(version, "rule:" + new string('1', 32), string.Empty)));
            Assert.That(items, Does.Contain(KeptItem(kept, Three)));
            Assert.That(items, Does.Contain(StoreComposer.Item(("id", removal), ("kind", "removed"), ("name", "reader"))));
            Assert.That(items, Does.Contain(StoreComposer.Item(("client", Hex('c')), ("kind", "revoked"))));
            Assert.That(items, Does.Contain(Statement(new string('6', 32), Hex('d'), Hex('1'), Hex('2'))), "as it is, every member with it");
        }

        [Test]
        public void A_deletion_record_nothing_answers_for_is_restated_with_no_renewal_and_a_record_an_item_restates_is_not_again()
        {
            // `STORE.md` §648: an item of the deletion record's identifier for a record whose
            // content is nowhere, and nothing more for a deletion record of a record restated.
            var store = new ComposedStore(One);
            store.Drop(store.Head);
            var record = store.Add(StoreComposer.Record(
                One, "withdrawn", "certificate:" + Hex('d'), StoreComposer.Content(("certificate", Hex('d')), ("renewed", Hex('e')))));
            store.Add(Head(One, 2, string.Empty, Statement(record.Hex, Hex('d'), Hex('e'), null)));
            store.Drop(record);
            store.Delete(record);
            var lost = StoreComposer.IdentifierOf(One, "withdrawn", "certificate:" + Hex('e'), 0, new string('5', 64));
            store.Deleted.Add(lost);
            Assume.That(store.Read().State, Is.EqualTo(StoreState.Whole), store.Read().Reason);

            var items = Items(SummaryOf(StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin)))
                .Where(item => item.Contains("\"withdrawn\""))
                .ToList();

            Assert.That(items, Is.EquivalentTo(new[]
            {
                Statement(record.Hex, Hex('d'), Hex('e'), null),
                Statement(KeePassGate.Hex.Of(lost), Hex('e'), string.Empty, null),
            }));
        }

        private static string Statement(string id, string certificate, string renewed, string from)
        {
            var members = new List<(string Name, string Value)> { ("certificate", certificate), ("id", id), ("kind", "withdrawn"), ("renewed", renewed) };
            if (from != null)
            {
                members.Add(("from", from));
            }

            return StoreComposer.Item(members.ToArray());
        }

        [Test]
        public void An_admission_of_a_revoked_client_is_deleted_by_a_revocation_as_a_record_an_item_a_deletion_record_and_the_write_s_own()
        {
            // `STORE.md` §627 step 3: the records of kind `client` a revocation refuses, by the
            // client key each holds, the write's own revocation counting as present.
            AssertDeletedBy(
                Admission('c', ("certificate", Hex('d')), ("issuer", Hex('a'))),
                Admission('e', ("certificate", Hex('f')), ("issuer", Hex('a'))),
                new Dictionary<string, Func<ComposedStore, StoreAct>>
                {
                    {
                        "a record with the item that restates it", store =>
                        {
                            store.Revoked('c');
                            return Neutral();
                        }
                    },
                    { "an item", store => Restating(store, StoreComposer.Item(("client", Hex('c')), ("kind", "revoked"))) },
                    { "a deletion record", store => Telling(store, "revoked", "client:" + Hex('c')) },
                    { "the write's own record", store => Act(One, Recorded("revoked", ("client", Hex('c')))) },
                });
        }

        [Test]
        public void An_admission_under_a_retired_issuer_is_deleted_by_a_retirement_as_a_record_an_item_a_deletion_record_and_the_write_s_own()
        {
            AssertDeletedBy(
                Admission('c', ("certificate", Hex('d')), ("issuer", Hex('b'))),
                Admission('e', ("certificate", Hex('f')), ("issuer", Hex('a'))),
                new Dictionary<string, Func<ComposedStore, StoreAct>>
                {
                    {
                        "a record with the item that restates it", store =>
                        {
                            store.Add(StoreComposer.Record(One, "retired", "authority:" + Hex('b'), StoreComposer.Content(("authority", Hex('b')))));
                            return Restating(store, StoreComposer.Item(("authority", Hex('b')), ("kind", "retired")));
                        }
                    },
                    { "an item", store => Restating(store, StoreComposer.Item(("authority", Hex('b')), ("kind", "retired"))) },
                    {
                        // The authority's entry, beside its deletion record, is what the file
                        // knows the authority by.
                        "a deletion record", store =>
                        {
                            store.Delete(store.Add(StoreRecords.Authority(One, 'b')));
                            return Telling(store, "retired", "authority:" + Hex('b'));
                        }
                    },
                    { "the write's own record", store => Retire(One, 'b') },
                });
        }

        [Test]
        public void An_admission_of_a_certificate_taken_away_is_deleted_by_a_record_an_item_a_deletion_record_and_the_write_s_own()
        {
            // The deletion record is told by the certificate the admission names, `STORE.md`
            // §623 step 2; the client's other certificate stays.
            AssertDeletedBy(
                Admission('c', ("certificate", Hex('d')), ("issuer", Hex('a'))),
                Admission('c', ("certificate", Hex('f')), ("issuer", Hex('a'))),
                new Dictionary<string, Func<ComposedStore, StoreAct>>
                {
                    { "a record with the item that restates it", store => Restating(store, store.Decision("withdrawn")) },
                    { "an item", store => Restating(store, Statement(new string('6', 32), Hex('d'), string.Empty, null)) },
                    { "a deletion record", store => Telling(store, "withdrawn", "certificate:" + Hex('d')) },
                    { "the write's own record", store => Act(One, Recorded("withdrawn", ("certificate", Hex('d')), ("renewed", string.Empty))) },
                });
        }

        [Test]
        public void An_admission_that_reads_is_deleted_by_what_refuses_it_and_one_nothing_refuses_is_left()
        {
            // The plan reads no certificate, so an admission that reads goes by the same texts as
            // a record that does not: its client revoked, its issuer retired, its certificate
            // taken away, each by the write's own record.
            using (var refusedCertificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            using (var leftCertificate = Minted.Named("CN=agent-other-fictitious").SelfSigned().Read())
            {
                var taken = KeePassGate.Authority.CertificateDigest.Of(refusedCertificate);
                AssertDeletedBy(
                    Readable(refusedCertificate, Hex('c'), Hex('b')),
                    Readable(leftCertificate, Hex('e'), Hex('a')),
                    new Dictionary<string, Func<ComposedStore, StoreAct>>
                    {
                        { "its client revoked", store => Act(One, Recorded("revoked", ("client", Hex('c')))) },
                        { "its issuer retired", store => Retire(One, 'b') },
                        { "its certificate taken away", store => Act(One, Recorded("withdrawn", ("certificate", taken), ("renewed", string.Empty))) },
                    });
            }
        }

        [Test]
        public void A_record_of_kind_client_is_deleted_by_the_texts_it_holds_in_force_or_not_and_whether_or_not_it_reads()
        {
            // `STORE.md` §627 step 3: what an admission comes to is not read, so a record beside
            // a deletion record naming it, and a record that does not read as an admission, go
            // where what they name is refused.
            var store = new ComposedStore(One);
            var gone = store.Add(Admission('c', ("certificate", Hex('5')), ("issuer", Hex('a'))));
            store.Delete(gone);
            var unread = store.Add(Admission('c', ("certificate", Hex('6')), ("der", "not base64"), ("issuer", Hex('a'))));
            store.Revoked('c');
            Assume.That(store.Read().State, Is.EqualTo(StoreState.Whole), store.Read().Reason);

            var reading = store.Read();
            var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

            Assert.That(write.Delete, Does.Contain(gone.Hex), "beside a deletion record naming it");
            Assert.That(write.Delete, Does.Contain(unread.Hex), "a record that does not read");

            var applied = write.Applied(reading.Candidates, reading.Deletions);
            Assert.That(applied.Key.Select(candidate => candidate.Hex), Does.Not.Contain(gone.Hex).And.Not.Contain(unread.Hex));
            Assert.That(applied.Value.Count(identifier => KeePassGate.Hex.Of(identifier) == gone.Hex), Is.EqualTo(1),
                "the file holds its deletion record already, and no second is written");
            Assert.That(applied.Value.Count(identifier => KeePassGate.Hex.Of(identifier) == unread.Hex), Is.EqualTo(1));
        }

        [Test]
        public void A_record_of_kind_client_that_names_nothing_refused_is_left_whatever_it_comes_to()
        {
            // `STORE.md` §627 step 3 names the revoked, the retired and the taken away and no
            // other: a renewal that does not count, the admissions of a certificate several name,
            // a record that does not read, a record without the member asked, and a text that
            // differs from what is refused in its case, stay.
            var store = new ComposedStore(One);
            store.Add(StoreComposer.Record(One, "revoked", "client:" + Hex('c'), StoreComposer.Content(("client", Hex('c')))));
            store.Add(StoreComposer.Record(One, "retired", "authority:" + Hex('b'), StoreComposer.Content(("authority", Hex('b')))));
            var withdrawn = store.Add(StoreComposer.Record(
                One, "withdrawn", "certificate:" + Hex('d'), StoreComposer.Content(("certificate", Hex('d')), ("renewed", string.Empty))));
            store.RestateAll(new[]
            {
                StoreComposer.Item(("client", Hex('c')), ("kind", "revoked")),
                StoreComposer.Item(("authority", Hex('b')), ("kind", "retired")),
                Statement(withdrawn.Hex, Hex('d'), string.Empty, null),
            });

            var left = new[]
            {
                Admission('g'),
                Admission('h', ("certificate", Hex('8')), ("from", Hex('d')), ("issuer", Hex('a'))),
                Admission('k', ("certificate", Hex('7')), ("issuer", Hex('a'))),
                Admission('m', ("certificate", Hex('7')), ("issuer", Hex('a'))),
                Admission('n', ("certificate", Hex('6')), ("der", "not base64"), ("issuer", Hex('a'))),
                Admission('p', ("certificate", Hex('D')), ("issuer", Hex('B'))),
            };
            foreach (var record in left)
            {
                store.Add(record);
            }

            var reading = store.Read();
            Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Delete.Intersect(left.Select(record => record.Hex)), Is.Empty);
        }

        [Test]
        public void A_write_that_revokes_a_client_deletes_its_admission_with_a_deletion_record_and_leaves_the_store_whole()
        {
            var store = Started(out var key, 'a');
            var admission = StoreComposer.Record(
                key, "client", "client:" + Hex('c'), StoreComposer.Content(("certificate", Hex('d')), ("client", Hex('c')), ("issuer", Hex('a'))));
            store.Put(admission);

            var written = store.Write(new[] { Act(key, StoreRecord.Compose(key, "revoked", new Dictionary<string, string> { { "client", Hex('c') } })) });
            Assert.That(written.Taken, Is.True, written.Reason);

            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(store.Candidates.Select(candidate => candidate.Hex), Does.Not.Contain(admission.Hex));
            Assert.That(store.Deletions.Select(KeePassGate.Hex.Of), Does.Contain(admission.Hex));
            Assert.That(reading.InForce.Taken("revoked"), Is.EqualTo(new[] { Hex('c') }));
        }

        [Test]
        public void A_write_that_takes_a_certificate_away_restates_its_record_by_an_item_of_its_own_and_a_single_item_more()
        {
            var store = Started(out var key, 'a');
            var before = store.Read().InForce.Room.Items;
            var record = StoreRecord.Compose(key, "withdrawn", new Dictionary<string, string>
            {
                { "certificate", Hex('d') },
                { "from", Hex('f') },
                { "renewed", Hex('e') },
            });

            var write = StoreWrite.Plan(store.Read(), new[] { Act(key, record) }, Plugin);

            var items = Items(SummaryOf(write));
            Assert.That(items, Does.Contain(Statement(record.Hex, Hex('d'), Hex('e'), Hex('f'))));
            Assert.That(items, Has.Count.EqualTo(before + 1));
        }

        [Test]
        public void An_addition_that_did_not_see_a_removal_of_its_name_is_deleted_by_a_record_an_item_a_deletion_record_and_the_write_s_own()
        {
            // `STORE.md` §627 step 3 and §616: an addition is deleted once a removal of its name
            // that it does not name is known, the write's own removal counting as known; an
            // addition of another name stays.
            AssertDeletedBy(
                Addition("reader", string.Empty),
                Addition("writer", string.Empty),
                new Dictionary<string, Func<ComposedStore, StoreAct>>
                {
                    { "a record with the item that restates it", store => Restating(store, store.Decision("removed")) },
                    { "an item", store => Restating(store, RemovalItem(new string('7', 32), "reader")) },
                    { "a deletion record", store => Telling(store, "removed", "profile:reader") },
                    { "the write's own record", store => Act(One, Recorded("removed", ("name", "reader"))) },
                });
        }

        [Test]
        public void An_addition_naming_every_removal_of_its_name_is_left_and_an_addition_naming_a_single_removal_of_two_is_deleted()
        {
            var store = new ComposedStore(One);
            var first = store.Add(Removal("reader"));
            var second = store.Add(Removal("reader"));
            store.RestateAll(new[] { RemovalItem(first.Hex, "reader"), RemovalItem(second.Hex, "reader") });
            var both = store.Add(Addition("reader", string.Join(" ", new[] { first.Hex, second.Hex }.OrderBy(hex => hex, StringComparer.Ordinal))));
            var single = store.Add(Addition("reader", first.Hex));
            var reading = store.Read();
            Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Delete, Does.Contain(single.Hex), "an addition that did not see the second removal");
            Assert.That(write.Delete, Does.Not.Contain(both.Hex), "an addition that names every removal of its name");
            Assert.That(write.Delete, Does.Not.Contain(first.Hex).And.Not.Contain(second.Hex), "a removal is never deleted");
        }

        [Test]
        public void An_addition_out_of_force_or_whose_name_is_no_profile_name_goes_by_the_same_rule()
        {
            // `STORE.md` §627 step 3 tells additions by the texts the core keeps: an addition beside
            // a deletion record naming it, and an addition whose name could travel as no claim, are
            // deleted where a removal they do not name is known, and left where none is.
            var store = new ComposedStore(One);
            var gone = store.Add(Addition("reader", string.Empty));
            store.Delete(gone);
            var odd = store.Add(Addition("not a name", string.Empty));
            var oddLeft = store.Add(Addition("not a name either", string.Empty));
            var removal = store.Add(Removal("reader"));
            var oddRemoval = store.Add(Removal("not a name"));
            store.RestateAll(new[] { RemovalItem(removal.Hex, "reader"), RemovalItem(oddRemoval.Hex, "not a name") });
            var reading = store.Read();
            Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

            Assert.That(write.Delete, Does.Contain(gone.Hex), "beside a deletion record naming it");
            Assert.That(write.Delete, Does.Contain(odd.Hex), "a name that could travel as no claim");
            Assert.That(write.Delete, Does.Not.Contain(oddLeft.Hex), "no removal of its name is known");

            var applied = write.Applied(reading.Candidates, reading.Deletions);
            Assert.That(applied.Value.Count(identifier => KeePassGate.Hex.Of(identifier) == gone.Hex), Is.EqualTo(1),
                "the file holds its deletion record already, and no second is written");
            Assert.That(applied.Value.Count(identifier => KeePassGate.Hex.Of(identifier) == odd.Hex), Is.EqualTo(1));
        }

        [Test]
        public void A_write_that_removes_a_name_deletes_the_addition_it_defeats_and_restates_the_removal_by_a_single_item_more()
        {
            var store = Started(out var key, 'a');
            var addition = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            Assert.That(store.Write(new[] { Act(key, addition) }).Taken, Is.True);
            var before = store.Read().InForce.Room.Items;
            var removal = StoreRecord.Compose(key, "removed", new Dictionary<string, string> { { "name", "reader" } });

            var write = StoreWrite.Plan(store.Read(), new[] { Act(key, removal) }, Plugin);

            Assert.That(write.Delete, Does.Contain(addition.Hex));
            var items = Items(SummaryOf(write));
            Assert.That(items, Does.Contain(RemovalItem(removal.Hex, "reader")));
            Assert.That(items, Has.Count.EqualTo(before + 1));

            var written = store.Write(new[] { Act(key, removal) });
            Assert.That(written.Taken, Is.True, written.Reason);
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(store.Candidates.Select(candidate => candidate.Hex), Does.Not.Contain(addition.Hex));
            Assert.That(store.Deletions.Select(KeePassGate.Hex.Of), Does.Contain(addition.Hex));
            Assert.That(reading.InForce.RemovalsOf("reader"), Is.EqualTo(new[] { removal.Hex }), "and the removal stays");
        }

        [Test]
        public void An_addition_the_write_itself_adds_is_not_deleted_by_a_removal_beside_it()
        {
            // `STORE.md` §627 step 3 deletes what a record present replaces; what the write adds is
            // composed by the write, and an addition and a removal of a name in a single write are
            // the composer's to avoid.
            var store = Started(out var key, 'a');
            var addition = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            var removal = StoreRecord.Compose(key, "removed", new Dictionary<string, string> { { "name", "reader" } });

            var write = StoreWrite.Plan(store.Read(), new[] { Act(key, removal), Act(key, addition) }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Add.Select(record => record.Hex), Does.Contain(addition.Hex));
            Assert.That(write.Delete, Does.Not.Contain(addition.Hex));
        }

        [Test]
        public void A_version_a_version_in_force_names_is_deleted_with_a_deletion_record_by_a_record_an_item_and_the_write_s_own()
        {
            // `STORE.md` §627 step 3: every record of kind `version` that a version in force names
            // in `after`, by its record, by an item of a summary entry in force, or by a version
            // the write adds. A version of another node that nothing names stays.
            var cases = new Dictionary<string, Func<ComposedStore, StoreComposer.Composed, StoreComposer.Composed, StoreAct>>
            {
                {
                    "a record", (store, replaced, left) =>
                    {
                        var replacing = store.Add(VersionOf(SwitchOf('1'), replaced));
                        store.RestateAll(new[] { ItemOf(left), ItemOf(replacing) });
                        return Neutral();
                    }
                },
                {
                    "an item of a summary entry in force", (store, replaced, left) =>
                    {
                        store.RestateAll(new[] { ItemOf(left), ItemOf(VersionOf(SwitchOf('1'), replaced)) });
                        return Neutral();
                    }
                },
                {
                    "the write's own record", (store, replaced, left) =>
                    {
                        store.RestateAll(new[] { ItemOf(left), ItemOf(replaced) });
                        return Act(One, Recorded("version", ("after", replaced.Hex), ("doc", "a rule"), ("object", SwitchOf('1'))));
                    }
                },
            };

            foreach (var pair in cases)
            {
                var store = new ComposedStore(One);
                var replaced = store.Add(VersionOf(SwitchOf('1')));
                var left = store.Add(VersionOf(SwitchOf('2')));
                var act = pair.Value(store, replaced, left);
                var reading = store.Read();
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), pair.Key + ": " + reading.Reason);

                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);

                Assert.That(write.Refused, Is.Null, pair.Key);
                Assert.That(write.Delete, Does.Contain(replaced.Hex), pair.Key + ": the version replaced");
                Assert.That(write.Delete, Does.Not.Contain(left.Hex), pair.Key + ": the version nothing names");

                var applied = write.Applied(reading.Candidates, reading.Deletions);
                Assert.That(applied.Key.Select(candidate => candidate.Hex), Does.Not.Contain(replaced.Hex), pair.Key);
                Assert.That(applied.Value.Count(identifier => KeePassGate.Hex.Of(identifier) == replaced.Hex), Is.EqualTo(1),
                    pair.Key + ": a single deletion record");
                using (var after = StoreReading.Afresh(applied.Key, applied.Value))
                {
                    Assert.That(after.State, Is.EqualTo(StoreState.Whole), pair.Key + ": " + after.Reason);
                }
            }
        }

        [Test]
        public void A_version_named_by_a_version_in_force_goes_in_force_or_not_and_a_version_named_only_by_a_version_out_of_force_is_left()
        {
            // `STORE.md` §623 step 3: a version out of force is no version of its object, so what
            // it names stands, and deleting that would take a standing version away. A version a
            // version in force names goes, beside a deletion record of its own or not.
            var store = new ComposedStore(One);
            var gone = store.Add(VersionOf(SwitchOf('1')));
            store.Delete(gone);
            var replacing = store.Add(VersionOf(SwitchOf('1'), gone));
            var byRecord = store.Add(VersionOf(SwitchOf('2')));
            store.Delete(store.Add(VersionOf(SwitchOf('2'), byRecord)));
            var byItem = store.Add(VersionOf(SwitchOf('3')));
            var first = store.Head;
            store.RestateAll(new[] { ItemOf(replacing), ItemOf(byRecord), ItemOf(byItem), ItemOf(VersionOf(SwitchOf('3'), byItem)) });
            var outOfForce = store.Head;
            store.RestateAll(new[] { ItemOf(replacing), ItemOf(byRecord), ItemOf(byItem) }, first.Hex);
            store.Add(outOfForce);
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Delete, Does.Contain(gone.Hex), "a version beside a deletion record, named by a version in force");
            Assert.That(write.Delete, Does.Not.Contain(byRecord.Hex), "named only by a record out of force");
            Assert.That(write.Delete, Does.Not.Contain(byItem.Hex), "named only by an item of a summary entry out of force");
            var items = Items(SummaryOf(write));
            Assert.That(items, Does.Contain(ItemOf(byRecord)).And.Contain(ItemOf(byItem)), "both stand, and are restated");
        }

        [Test]
        public void A_standing_version_is_never_deleted_however_many_stand_beside_it()
        {
            // Two versions made apart, each naming the version both had read: both stand, and the
            // version they name goes. A version the write adds that names the first takes the
            // first away and leaves the second, `STORE.md` §610.
            var store = new ComposedStore(One);
            var both = store.Add(VersionOf(SwitchOf('1')));
            var first = store.Add(VersionOf(SwitchOf('1'), both));
            var second = store.Add(VersionOf(SwitchOf('1'), both));
            store.RestateAll(new[] { ItemOf(first), ItemOf(second) });
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var neutral = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);
            Assert.That(neutral.Delete, Does.Contain(both.Hex));
            Assert.That(neutral.Delete, Does.Not.Contain(first.Hex).And.Not.Contain(second.Hex));

            var own = Recorded("version", ("after", Listed(first.Hex, both.Hex)), ("doc", "a rule"), ("object", SwitchOf('1')));
            var changed = StoreWrite.Plan(reading, new[] { Act(One, own) }, Plugin);
            Assert.That(changed.Delete, Does.Contain(first.Hex).And.Contain(both.Hex));
            Assert.That(changed.Delete, Does.Not.Contain(second.Hex), "the version the write's own did not name stands beside it");
        }

        [Test]
        public void An_identifier_a_version_names_that_the_tree_does_not_hold_gets_a_deletion_record_a_single_time()
        {
            // `STORE.md` §627 step 3: a deletion record for a replaced version this copy does not
            // hold, so that it stays out of force when a merge brings it; once, however many
            // versions of its object name it, and not again while a deletion record names it. A
            // version of another object that names it replaces nothing, and an entry of the tree
            // under the identifier, a record or not, gets none.
            var store = Started(out var key, 'a');
            var elsewhere = Versioned(key, SwitchOf('1'), string.Empty).Hex;
            var first = Versioned(key, SwitchOf('1'), elsewhere);
            var beside = Versioned(key, SwitchOf('1'), elsewhere);
            var other = Versioned(key, SwitchOf('2'), elsewhere);

            var both = StoreWrite.Plan(store.Read(), new[] { Act(key, first), Act(key, beside), Act(key, other) }, Plugin);
            Assert.That(both.Refused, Is.Null);
            Assert.That(both.Deletions.Count(identifier => identifier == elsewhere), Is.EqualTo(1), "named twice, told of once");
            Assert.That(store.Write(new[] { Act(key, first), Act(key, beside), Act(key, other) }).Taken, Is.True);
            Assert.That(store.Read().State, Is.EqualTo(StoreState.Whole), store.Read().Reason);

            var again = Versioned(key, SwitchOf('1'), Listed(first.Hex, beside.Hex, elsewhere));
            var next = StoreWrite.Plan(store.Read(), new[] { Act(key, again) }, Plugin);
            Assert.That(next.Deletions, Does.Not.Contain(elsewhere), "a deletion record names it already");
            Assert.That(next.Delete, Does.Contain(first.Hex).And.Contain(beside.Hex));
            Assert.That(next.Delete, Does.Not.Contain(other.Hex), "a version of another object");

            var ofTheOther = Versioned(key, SwitchOf('2'), string.Empty).Hex;
            var alone = StoreWrite.Plan(store.Read(), new[] { Act(key, Versioned(key, SwitchOf('1'), ofTheOther)) }, Plugin);
            Assert.That(alone.Deletions, Does.Not.Contain(ofTheOther), "a version of another object names nothing of this object");

            var stripped = Versioned(key, SwitchOf('3'), string.Empty).Hex;
            var tree = store.Candidates.Select(candidate => candidate.Identifier).Concat(new[] { StoreComposer.BytesOf(stripped) });
            using (var reading = StoreReading.Afresh(store.Candidates, store.Deletions, tree))
            {
                var write = StoreWrite.Plan(reading, new[] { Act(key, Versioned(key, SwitchOf('3'), stripped)) }, Plugin);
                Assert.That(write.Deletions, Does.Not.Contain(stripped), "an entry the tree holds");
                Assert.That(write.Delete, Does.Not.Contain(stripped), "and it is no record, so it stays");
            }
        }

        [Test]
        public void The_summary_entry_restates_each_standing_version_and_no_version_replaced()
        {
            var store = new ComposedStore(One);
            var replaced = store.Add(VersionOf(SwitchOf('1')));
            var standing = store.Add(VersionOf(SwitchOf('1'), replaced));
            var changed = store.Add(VersionOf(SwitchOf('2')));
            store.RestateAll(new[] { ItemOf(standing), ItemOf(changed) });
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            var own = Recorded("version", ("after", changed.Hex), ("doc", "a rule"), ("object", SwitchOf('2')));

            var write = StoreWrite.Plan(reading, new[] { Act(One, own) }, Plugin);

            var items = Items(SummaryOf(write)).Where(item => item.Contains("\"version\"")).ToList();
            Assert.That(items, Is.EquivalentTo(new[]
            {
                ItemOf(standing),
                VersionItemOf(own),
            }));

            var applied = write.Applied(reading.Candidates, reading.Deletions);
            using (var after = StoreReading.Afresh(applied.Key, applied.Value))
            {
                Assert.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);
            }
        }

        [Test]
        public void A_version_replaces_only_versions_of_its_own_object_and_a_write_deletes_no_other()
        {
            // `STORE.md` §617: a version replaces the versions of its own object it names. A
            // version of another node that a version names stands, is restated, and is never
            // deleted, by a write that adds nothing of either object or by a version of the write.
            var store = new ComposedStore(One);
            var other = store.Add(VersionOf(SwitchOf('2')));
            var naming = store.Add(VersionOf(SwitchOf('1'), other));
            store.RestateAll(new[] { ItemOf(naming), ItemOf(other) });
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(
                reading.InForce.Restated().Where(item => item.Kind == "version").Select(item => item.Member("id")),
                Does.Contain(other.Hex),
                "the reading holds it standing");

            var own = Recorded("version", ("after", Listed(naming.Hex, other.Hex)), ("doc", "a rule"), ("object", SwitchOf('1')));
            foreach (var act in new[] { Neutral(), Act(One, own) })
            {
                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);

                Assert.That(write.Refused, Is.Null, act.Name);
                Assert.That(write.Delete, Does.Not.Contain(other.Hex), act.Name);
                Assert.That(write.Deletions, Does.Not.Contain(other.Hex), act.Name);
                Assert.That(Items(SummaryOf(write)), Does.Contain(ItemOf(other)), act.Name + ": restated");
            }
        }

        [Test]
        public void An_identifier_no_entry_carries_is_told_of_only_where_a_version_of_the_naming_object_could_have_it()
        {
            // `STORE.md` §609 and §627 step 3: a name that no entry carries gets a deletion record
            // only where its tag, its subject and its number are those a version of the naming
            // object has, a row's profile tag among them; any other name, a group's identifier or
            // an identifier under the tag of another kind among them, is passed over.
            var row = "rule:" + new string('1', 32) + "/pf";
            var fits = Recorded("version", ("after", string.Empty), ("doc", "a rule"), ("object", row)).Hex;
            var fitsSwitch = Recorded("version", ("after", string.Empty), ("doc", "a rule"), ("object", SwitchOf('1'))).Hex;
            var passedOver = new Dictionary<string, string>
            {
                { "a version of another profile's rule at the node", Recorded("version", ("after", string.Empty), ("doc", "a rule"), ("object", "rule:" + new string('1', 32) + "/pg")).Hex },
                { "a version of another node", Recorded("version", ("after", string.Empty), ("doc", "a rule"), ("object", SwitchOf('2'))).Hex },
                { "a revocation", Recorded("revoked", ("client", Hex('c'))).Hex },
                { "a summary entry", KeePassGate.Hex.Of(StoreComposer.IdentifierOf(One, "head", "store", 7, new string('0', 64))) },
                { "a version record", KeePassGate.Hex.Of(StoreComposer.IdentifierOf(One, "format", "format:1", 1, new string('0', 64))) },
                { "the plugin's group", KeePassGate.Hex.Of(StoreFormat.NewGroupIdentifier()) },
                { "an identifier of nothing", new string('7', 32) },
            };

            var named = Listed(passedOver.Values.Concat(new[] { fits }).ToArray());
            var own = Recorded("version", ("after", named), ("doc", "a rule"), ("object", row));
            var byOwn = StoreWrite.Plan(new ComposedStore(One).Read(), new[] { Act(One, own) }, Plugin);
            Assert.That(byOwn.Refused, Is.Null);
            Assert.That(byOwn.Deletions, Is.EqualTo(new[] { fits }), "by a version of the write");

            var store = new ComposedStore(One);
            var inForce = store.Add(Version(One, row, named));
            var switched = store.Add(Version(One, SwitchOf('1'), Listed(passedOver.Values.Concat(new[] { fitsSwitch }).ToArray())));
            store.RestateAll(new[] { ItemOf(inForce), ItemOf(switched) });
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            var byRecord = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);
            Assert.That(byRecord.Deletions, Is.EquivalentTo(new[] { fits, fitsSwitch }), "by versions in force");
        }

        [Test]
        public void A_version_is_deleted_only_where_every_version_it_names_stays_named()
        {
            // A deletion changes no answer, `STORE.md` §627: a version the write's own replaces,
            // and that names a version nothing staying names, stays and goes on naming it, so the
            // version it names is deleted with a deletion record that a version in force names.
            var store = new ComposedStore(One);
            var first = store.Add(VersionOf(SwitchOf('1')));
            var second = store.Add(VersionOf(SwitchOf('1'), first));
            store.RestateAll(new[] { ItemOf(second) });
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var open = Recorded("version", ("after", second.Hex), ("doc", "a rule"), ("object", SwitchOf('1')));
            var write = StoreWrite.Plan(reading, new[] { Act(One, open) }, Plugin);
            Assert.That(write.Delete, Does.Contain(first.Hex), "named by the version that stays");
            Assert.That(write.Delete, Does.Not.Contain(second.Hex), "it names a version only it names");
            var applied = write.Applied(reading.Candidates, reading.Deletions);
            using (var after = StoreReading.Afresh(applied.Key, applied.Value))
            {
                Assert.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);
                Assert.That(
                    after.InForce.Records.Single(record => record.Hex == second.Hex).InForce, Is.True,
                    "the version naming the version deleted stays in force, and names it");
                Assert.That(
                    after.InForce.Restated().Where(item => item.Kind == "version").Select(item => item.Member("id")),
                    Is.EqualTo(new[] { open.Hex }),
                    "the write's own version alone stands");
            }

            var closed = Recorded("version", ("after", Listed(first.Hex, second.Hex)), ("doc", "a rule"), ("object", SwitchOf('1')));
            Assert.That(
                StoreWrite.Plan(reading, new[] { Act(One, closed) }, Plugin).Delete,
                Does.Contain(first.Hex).And.Contain(second.Hex),
                "a version that names all it replaces takes both");
        }

        [Test]
        public void A_write_whose_version_names_a_version_of_the_same_write_is_refused()
        {
            // `STORE.md` §610: a version names what its writer had read, and a version the same
            // write adds was read by nobody. Written, it would stand beside its own deletion
            // record. A version of another object naming it replaces nothing, and is written.
            var store = new ComposedStore(One);
            var first = store.Add(VersionOf(SwitchOf('1')));
            store.RestateAll(new[] { ItemOf(first) });
            var reading = store.Read();
            var added = Recorded("version", ("after", first.Hex), ("doc", "a rule"), ("object", SwitchOf('1')));
            var naming = Recorded("version", ("after", added.Hex), ("doc", "a rule"), ("object", SwitchOf('1')));
            var elsewhere = Recorded("version", ("after", added.Hex), ("doc", "a rule"), ("object", SwitchOf('2')));

            var refused = StoreWrite.Plan(reading, new[] { Act(One, added), Act(One, naming) }, Plugin);
            Assert.That(refused.Refused, Is.EqualTo("a version of the write names a version the same write adds"));
            Assert.That(refused.Add, Is.Empty);

            var written = StoreWrite.Plan(reading, new[] { Act(One, added), Act(One, elsewhere) }, Plugin);
            Assert.That(written.Refused, Is.Null);
            Assert.That(written.Deletions, Does.Not.Contain(added.Hex));
        }

        [Test]
        public void A_write_that_starts_a_store_tells_of_what_its_versions_name()
        {
            // `STORE.md` §627 step 3, where no key stood: the names a version of the write gives
            // are held to its object under the key the write brings.
            var key = StoreFormat.NewKey();
            var fits = Versioned(key, SwitchOf('1'), string.Empty).Hex;
            var version = Versioned(key, SwitchOf('1'), Listed(fits, new string('7', 32)));

            var write = StoreWrite.Plan(new MemoryStore().Read(), new[] { Create(key, 'a'), Act(key, version) }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(write.Starts, Is.True);
            Assert.That(write.Deletions, Is.EqualTo(new[] { fits }));
        }

        [Test]
        public void A_version_carried_only_by_an_item_is_restated_while_it_alone_names_a_version()
        {
            // The summary entry in force carries W and V, and V, with no record, is replaced by W
            // and alone names X, a record in force. Left out of the summary entry, V would name
            // nothing, and X would stand restated by nothing, which stops the store.
            var store = new ComposedStore(One);
            var x = store.Add(VersionOf(SwitchOf('1')));
            var v = VersionOf(SwitchOf('1'), x);
            var w = store.Add(VersionOf(SwitchOf('1'), v));
            store.RestateAll(new[] { ItemOf(w), ItemOf(v) });

            AssertRestatedAndWhole(store, v, x);
        }

        [Test]
        public void A_version_carried_only_by_an_item_is_restated_while_it_alone_names_a_deletion_record()
        {
            var store = new ComposedStore(One);
            var x = VersionOf(SwitchOf('1'));
            store.Delete(x);
            var v = VersionOf(SwitchOf('1'), x);
            var w = store.Add(VersionOf(SwitchOf('1'), v));
            store.RestateAll(new[] { ItemOf(w), ItemOf(v) });

            var write = AssertRestatedAndWhole(store, v, x);
            Assert.That(write.Deletions, Does.Not.Contain(x.Hex), "its deletion record is the file's already");
        }

        [Test]
        public void A_version_whose_record_is_out_of_force_and_whose_item_is_in_force_is_kept_while_it_alone_names_a_version()
        {
            // In force by its item, a version names what its item names, whatever became of its
            // record, `STORE.md` §623 step 3.
            var store = new ComposedStore(One);
            var x = store.Add(VersionOf(SwitchOf('1')));
            var v = store.Add(VersionOf(SwitchOf('1'), x));
            store.Delete(v);
            var w = store.Add(VersionOf(SwitchOf('1'), v));
            store.RestateAll(new[] { ItemOf(w), ItemOf(v) });

            AssertRestatedAndWhole(store, v, x);
        }

        [Test]
        public void With_every_list_closed_a_write_restates_the_standing_versions_alone()
        {
            var store = new ComposedStore(One);
            var first = store.Add(VersionOf(SwitchOf('1')));
            var second = store.Add(VersionOf(SwitchOf('1'), first));
            var third = store.Add(VersionOf(SwitchOf('1'), first, second));
            store.RestateAll(new[] { ItemOf(third), ItemOf(second) });
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var neutral = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);
            Assert.That(VersionItems(neutral), Is.EqualTo(new[] { ItemOf(third) }));
            Assert.That(neutral.Delete, Does.Contain(first.Hex).And.Contain(second.Hex));

            var own = Recorded("version", ("after", Listed(first.Hex, second.Hex, third.Hex)), ("doc", "a rule"), ("object", SwitchOf('1')));
            var changed = StoreWrite.Plan(reading, new[] { Act(One, own) }, Plugin);
            Assert.That(VersionItems(changed), Is.EqualTo(new[] { VersionItemOf(own) }));
            Assert.That(changed.Delete, Does.Contain(first.Hex).And.Contain(second.Hex).And.Contain(third.Hex));
        }

        [Test]
        public void What_a_write_keeps_of_versions_replaced_is_the_same_in_whatever_order_their_entries_are_walked()
        {
            // W names M and N, which each name Y; W does not name Y. Each copy keeps both M and N
            // and deletes Y, whatever order its tree holds them in, so copies that meet keep alike.
            foreach (var mFirst in new[] { true, false })
            {
                var store = new ComposedStore(One);
                var y = store.Add(VersionOf(SwitchOf('1')));
                var m = VersionOf(SwitchOf('1'), y);
                var n = VersionOf(SwitchOf('1'), y);
                store.Add(mFirst ? m : n);
                store.Add(mFirst ? n : m);
                var w = store.Add(VersionOf(SwitchOf('1'), m, n));
                store.RestateAll(new[] { ItemOf(w) });
                var reading = store.Read();
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

                var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

                var named = mFirst ? "M walked first" : "N walked first";
                Assert.That(write.Delete, Does.Contain(y.Hex), named);
                Assert.That(write.Delete, Does.Not.Contain(m.Hex).And.Not.Contain(n.Hex), named + ": both stay, and name Y");
            }
        }

        [Test]
        public void An_identifier_alone_tells_neither_two_names_whose_tags_meet_nor_a_bare_rule_from_a_row_whose_tag_is_0()
        {
            // `STORE.md` §609: a name no entry carries is told by its first ten bytes, the tag, the
            // subject hash and the number. Two names whose tags meet under a key, found by a search
            // run once, give their rows at a node the same ten bytes, and so do a rule's object with
            // no slash and a row whose name's tag is 0: a name of either is told of for the other.
            var node = new string('1', 32);
            Assert.That(StoreFormat.ProfileTag(One, "agent-455"), Is.EqualTo(StoreFormat.ProfileTag(One, "agent-604")));
            Assert.That(StoreFormat.ProfileTag(One, "agent-144433"), Is.EqualTo(0));
            var meeting = Recorded("version", ("after", string.Empty), ("doc", "a rule"), ("object", "rule:" + node + "/agent-604")).Hex;
            var bare = Recorded("version", ("after", string.Empty), ("doc", "a rule"), ("object", "rule:" + node)).Hex;

            var byMeeting = Recorded("version", ("after", meeting), ("doc", "a rule"), ("object", "rule:" + node + "/agent-455"));
            Assert.That(StoreWrite.Plan(new ComposedStore(One).Read(), new[] { Act(One, byMeeting) }, Plugin).Deletions, Is.EqualTo(new[] { meeting }));

            var byZero = Recorded("version", ("after", bare), ("doc", "a rule"), ("object", "rule:" + node + "/agent-144433"));
            Assert.That(StoreWrite.Plan(new ComposedStore(One).Read(), new[] { Act(One, byZero) }, Plugin).Deletions, Is.EqualTo(new[] { bare }));
        }

        /// <summary>
        /// Plans a write that adds nothing of the versions over this store, and holds the version
        /// replaced restated, the version it names not, and the store the write leaves whole and
        /// whole again after a further write.
        /// </summary>
        private static StoreWrite AssertRestatedAndWhole(ComposedStore store, StoreComposer.Composed replaced, StoreComposer.Composed named)
        {
            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);

            var write = StoreWrite.Plan(reading, new[] { Neutral() }, Plugin);

            Assert.That(write.Refused, Is.Null);
            Assert.That(VersionItems(write), Does.Contain(ItemOf(replaced)), "restated, so that it goes on naming");
            Assert.That(VersionItems(write), Does.Not.Contain(ItemOf(named)));
            var applied = write.Applied(reading.Candidates, reading.Deletions);
            using (var after = StoreReading.Afresh(applied.Key, applied.Value))
            {
                Assert.That(after.State, Is.EqualTo(StoreState.Whole), after.Reason);
                var again = StoreWrite.Plan(after, new[] { Retire(One, 'y') }, Plugin);
                Assert.That(VersionItems(again), Does.Contain(ItemOf(replaced)), "and restated again");
                var twice = again.Applied(applied.Key, applied.Value);
                using (var later = StoreReading.Afresh(twice.Key, twice.Value))
                {
                    Assert.That(later.State, Is.EqualTo(StoreState.Whole), later.Reason);
                }
            }

            return write;
        }

        /// <summary>The items `version` of a write's summary entry.</summary>
        private static IReadOnlyList<string> VersionItems(StoreWrite write)
        {
            return Items(SummaryOf(write)).Where(item => item.Contains("\"kind\":\"version\"")).ToList();
        }

        /// <summary>The object of a node's switch, the node's identifier this digit 32 times.</summary>
        private static string SwitchOf(char node)
        {
            return "switch:" + new string(node, 32);
        }

        /// <summary>A record of kind `version` under `One` of this object, naming these versions as replaced.</summary>
        private static StoreComposer.Composed VersionOf(string objectName, params StoreComposer.Composed[] replaced)
        {
            return Version(One, objectName, Listed(replaced.Select(version => version.Hex).ToArray()));
        }

        /// <summary>The item `version` that restates this record of kind `version`.</summary>
        private static string ItemOf(StoreComposer.Composed version)
        {
            var content = Newtonsoft.Json.Linq.JObject.Parse(version.Strings[StoreComposer.Data]);
            return VersionItem(version, (string)content["object"], (string)content["after"]);
        }

        /// <summary>The item `version` that restates a version the product composed.</summary>
        private static string VersionItemOf(StoreRecord version)
        {
            return StoreComposer.Item(
                ("after", version.Member("after")), ("doc", version.Member("doc")), ("id", version.Hex), ("kind", "version"),
                ("object", version.Member("object")));
        }

        /// <summary>A version of this object composed by the product under this key, naming these identifiers.</summary>
        private static StoreRecord Versioned(byte[] key, string objectName, string after)
        {
            return StoreRecord.Compose(key, "version", new Dictionary<string, string>
            {
                { "after", after },
                { "doc", "a rule" },
                { "object", objectName },
            });
        }

        /// <summary>Identifiers as a list of `STORE.md` §607: in ordinal order, separated by single spaces.</summary>
        private static string Listed(params string[] identifiers)
        {
            return string.Join(" ", identifiers.OrderBy(identifier => identifier, StringComparer.Ordinal));
        }

        /// <summary>A record of kind `profile` under `One`: an addition of this name naming these removals.</summary>
        private static StoreComposer.Composed Addition(string name, string after)
        {
            return StoreComposer.Record(One, "profile", "profile:" + name, StoreComposer.Content(("after", after), ("name", name)));
        }

        /// <summary>A record of kind `removed` under `One`: a removal of this name.</summary>
        private static StoreComposer.Composed Removal(string name)
        {
            return StoreComposer.Record(One, "removed", "profile:" + name, StoreComposer.Content(("name", name)));
        }

        /// <summary>The item `removed` that restates a removal of this name under this identifier.</summary>
        private static string RemovalItem(string id, string name)
        {
            return StoreComposer.Item(("id", id), ("kind", "removed"), ("name", name));
        }

        /// <summary>
        /// Plans a write with each case's act over a store holding these two records, and holds
        /// the first deleted and the second left.
        /// </summary>
        private static void AssertDeletedBy(
            StoreComposer.Composed refused, StoreComposer.Composed left, IReadOnlyDictionary<string, Func<ComposedStore, StoreAct>> cases)
        {
            foreach (var pair in cases)
            {
                var store = new ComposedStore(One);
                store.Add(refused);
                store.Add(left);
                var act = pair.Value(store);
                var reading = store.Read();
                Assume.That(reading.State, Is.EqualTo(StoreState.Whole), pair.Key + ": " + reading.Reason);

                var write = StoreWrite.Plan(reading, new[] { act }, Plugin);

                Assert.That(write.Refused, Is.Null, pair.Key);
                Assert.That(write.Delete, Does.Contain(refused.Hex), pair.Key + ": the record refused");
                Assert.That(write.Delete, Does.Not.Contain(left.Hex), pair.Key + ": the record nothing refuses");
            }
        }

        /// <summary>A record of kind `client` under `One`, holding the members given beside its client key.</summary>
        private static StoreComposer.Composed Admission(char client, params (string Name, string Value)[] members)
        {
            var all = new List<(string Name, string Value)>(members) { ("client", Hex(client)) };
            return StoreComposer.Record(One, "client", "client:" + Hex(client), StoreComposer.Content(all.ToArray()));
        }

        /// <summary>
        /// A record of kind `client` under `One` that reads as an admission of this certificate,
        /// under this client key and issued by this authority.
        /// </summary>
        private static StoreComposer.Composed Readable(
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, string client, string issuer)
        {
            var members = new KeePassGate.Authority.AdmissionRecord(
                KeePassGate.Authority.CertificateDigest.Of(certificate),
                certificate.Subject,
                client,
                new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero),
                new[] { "ops" },
                certificate.RawData,
                issuer).Members();
            Assume.That(KeePassGate.Authority.AdmissionRecord.Read(null, members), Is.Not.Null, "the record reads as an admission");
            return StoreComposer.Record(
                One, "client", "client:" + client, StoreComposer.Content(members.Select(member => (member.Key, member.Value)).ToArray()));
        }

        /// <summary>A record of the test's under `One`, composed by the product.</summary>
        private static StoreRecord Recorded(string kind, params (string Name, string Value)[] members)
        {
            return StoreRecord.Compose(One, kind, members.ToDictionary(member => member.Name, member => member.Value));
        }

        /// <summary>A summary entry restating this item put in place, and a write that refuses nothing more.</summary>
        private static StoreAct Restating(ComposedStore store, string item)
        {
            store.Restate(item);
            return Neutral();
        }

        /// <summary>A deletion record of a record of this kind and subject, and a write that refuses nothing more.</summary>
        private static StoreAct Telling(ComposedStore store, string kind, string subject)
        {
            store.Deleted.Add(StoreComposer.IdentifierOf(One, kind, subject, 0, new string('5', 64)));
            return Neutral();
        }

        /// <summary>A write that takes away nothing a test's records name: a retirement of an authority none of them names.</summary>
        private static StoreAct Neutral()
        {
            return Retire(One, 'z');
        }

        [Test]
        public void A_record_counting_by_its_history_is_set_back_to_the_version_that_passes()
        {
            var store = new ComposedStore(One);
            var revoked = store.Revoked('c');
            store.Edit(revoked, "{\"client\":\"edited\"}", "{\"client\":\"edited again\"}");

            var write = StoreWrite.Plan(store.Read(), new[] { Retire(One, 'a') }, Plugin);

            Assert.That(write.SetBack, Is.EqualTo(new[] { new KeyValuePair<string, int>(revoked.Hex, 2) }));
        }

        [Test]
        public void A_write_whose_strings_pass_the_plugin_s_ceiling_by_a_character_is_refused_and_a_write_at_it_is_not()
        {
            // `STORE.md` §643: the same write at the ceiling and a character beyond it.
            var store = Started(out var key, 'a');
            var near = Noted(key, StoreFormat.MaxWritten - 300).Content.Length;
            var at = Noted(key, StoreFormat.MaxWritten - 300 + (StoreFormat.MaxWritten - near));
            var beyond = Noted(key, StoreFormat.MaxWritten - 300 + (StoreFormat.MaxWritten - near) + 1);
            Assume.That(at.Content.Length, Is.EqualTo(StoreFormat.MaxWritten));
            Assume.That(beyond.Content.Length, Is.EqualTo(StoreFormat.MaxWritten + 1));

            Assert.That(StoreWrite.Plan(store.Read(), new[] { Act(key, at) }, Plugin).Refused, Is.Null);
            var refused = StoreWrite.Plan(store.Read(), new[] { Act(key, beyond) }, Plugin);
            Assert.That(
                refused.Refused,
                Is.EqualTo("the write needs a string of 262145 characters, and the plugin writes none longer than 262144"));
        }

        /// <summary>A retirement holding the key, with a member nothing reads of this many characters.</summary>
        private static StoreRecord Noted(byte[] key, int length)
        {
            return StoreRecord.Compose(key, "retired", new Dictionary<string, string>
            {
                { "authority", Hex('a') },
                { "note", new string('x', length) },
                { "storeKey", StoreComposer.HexOf(key) },
            });
        }

        [Test]
        public void A_write_the_room_allows_is_never_refused_for_a_ceiling_and_the_first_the_room_refuses_is_refused()
        {
            // A store an item short of its room takes a write that adds an item; with that item
            // in place it has no room, and the plan refuses it as stopped.
            var full = Holding(StoreFormat.MaxItems - 1);
            Assume.That(full.Read().Room.HasRoom, Is.True);
            var written = full.Write(new[] { Retire(One, 'a') });
            Assert.That(written.Taken, Is.True, written.Reason);

            var after = full.Read();
            Assert.That(after.Room.HasRoom, Is.False);
            var refused = StoreWrite.Plan(after, new[] { Retire(One, 'b') }, Plugin);
            Assert.That(refused.Refused, Does.StartWith("the store is stopped"));

            // Named summary entries the same way: the last that fits, and none after it.
            var named = Naming(7940);
            Assume.That(named.Read().Room.HasRoom, Is.True);
            Assert.That(named.Write(new[] { Retire(One, 'a') }).Taken, Is.True);
            Assert.That(named.Read().Room.HasRoom, Is.True);
            Assert.That(named.Write(new[] { Retire(One, 'b') }).Taken, Is.True);
            Assert.That(named.Read().Room.HasRoom, Is.False);
            Assert.That(StoreWrite.Plan(named.Read(), new[] { Retire(One, 'c') }, Plugin).Refused, Does.StartWith("the store is stopped"));
        }

        [Test]
        public void Applied_in_memory_the_plan_leaves_what_the_reading_of_the_file_would()
        {
            var store = Started(out var key, 'a');
            Assert.That(store.Write(new[] { Retire(key, 'a'), Made(key, 'b') }).Taken, Is.True);

            var reading = store.Read();

            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.Answering.Select(record => record.Form.Member("id")), Is.EqualTo(new[] { Hex('b') }));
            Assert.That(reading.InForce.Taken("retired"), Is.EqualTo(new[] { Hex('a') }));
            Assert.That(reading.InForce.Records.Count(record => record.Is("authority")), Is.EqualTo(1), "the retired authority's entry is deleted");
        }

        private static void AssertRefused(StoreReading reading, StoreAct act, string word)
        {
            var write = StoreWrite.Plan(reading, new[] { act }, Plugin);
            Assert.That(write.Refused, Does.StartWith("the store is " + word + ": "));
        }

        private static MemoryStore Started(out byte[] key, char authority)
        {
            key = StoreFormat.NewKey();
            var store = new MemoryStore();
            Assert.That(store.Write(new[] { Create(key, authority) }).Taken, Is.True);
            return store;
        }

        private static MemoryStore Holding(int items)
        {
            var store = new MemoryStore();
            store.Put(StoreRecords.Authority(One, 'a'));
            store.Put(Head(
                One, 1, string.Empty,
                Enumerable.Range(0, items - 1)
                    .Select(index => StoreComposer.Item(("client", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "revoked")))
                    .ToArray()));
            return store;
        }

        private static MemoryStore Naming(int named)
        {
            var store = new MemoryStore();
            store.Put(StoreRecords.Authority(One, 'a'));
            store.Put(Head(
                One, 1, string.Join(" ", Enumerable.Range(0, named - 1).Select(index => index.ToString("x32", CultureInfo.InvariantCulture)))));
            return store;
        }

        /// <summary>A Create where no store key stands, bringing this key.</summary>
        private static StoreAct Create(byte[] key, char id)
        {
            return Starting(key, AuthorityOf(key, id));
        }

        /// <summary>An authority added under the store key that stands, as a Recreate adds it.</summary>
        private static StoreAct Made(byte[] key, char id)
        {
            return Act(key, AuthorityOf(key, id));
        }

        private static StoreRecord AuthorityOf(byte[] key, char id)
        {
            return StoreRecord.Compose(key, "authority", new Dictionary<string, string>
            {
                { "id", Hex(id) },
                { "storeKey", StoreComposer.HexOf(key) },
            });
        }

        private static StoreAct Retire(byte[] key, char id)
        {
            return Act(key, Retirement(key, id));
        }

        /// <summary>A retirement holding no key, as a Recreate's does, `STORE.md` §614.</summary>
        private static StoreRecord WithoutKey(byte[] key, char id)
        {
            return StoreRecord.Compose(key, "retired", new Dictionary<string, string> { { "authority", Hex(id) } });
        }

        /// <summary>A retirement holding the key, as Delete's does, `STORE.md` §614.</summary>
        private static StoreRecord Retirement(byte[] key, char id)
        {
            return StoreRecord.Compose(key, "retired", new Dictionary<string, string>
            {
                { "authority", Hex(id) },
                { "storeKey", StoreComposer.HexOf(key) },
            });
        }

        /// <summary>An act composed against the store key that stands, its records under that key.</summary>
        private static StoreAct Act(byte[] key, StoreRecord record)
        {
            return new StoreAct("an act of the test's", key, StoreFormat.KeyName(key), new[] { record }, null);
        }

        /// <summary>An act composed where no store key stood, bringing this key.</summary>
        private static StoreAct Starting(byte[] key, StoreRecord record)
        {
            return new StoreAct("an act of the test's", key, null, new[] { record }, null);
        }

        private static StoreRecord SummaryOf(StoreWrite write)
        {
            return write.Add[write.Add.Count - 1];
        }

        private static IReadOnlyList<string> Items(StoreRecord head)
        {
            return head.Strings.Where(pair => pair.Key.StartsWith("KPG.S.", StringComparison.Ordinal)).Select(pair => pair.Value).ToList();
        }
    }
}
