using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The KPG tab's buffer for the plugin's store, `STORE.md` §627: acts composed at the press,
    /// read beside the store, written at OK held to their ground, and dropped at Cancel.
    /// </summary>
    [TestFixture]
    public sealed class PendingRecordsTests
    {
        [Test]
        public void A_page_reads_what_OK_would_leave_and_nothing_reaches_the_store_before_it()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Create().Succeeded, Is.True);

            Assert.That(StoreAuthorities.Served(pending.Store.Read()), Is.Not.Null, "the page reads the authority");
            Assert.That(live.Records.Candidates, Is.Empty, "the store holds nothing yet");
            Assert.That(pending.AuthorityWaiting, Is.True);

            Assert.That(pending.Apply(), Is.True);
            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.Not.Null, "OK writes it");
            Assert.That(pending.AuthorityWaiting, Is.False);
        }

        [Test]
        public void A_Create_and_a_Delete_on_the_same_dialog_write_nothing()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Create().Succeeded, Is.True);
            Assert.That(Service(pending).Delete().Succeeded, Is.True);
            Assert.That(StoreAuthorities.Served(pending.Store.Read()), Is.Null);
            Assert.That(Held(pending), Is.Empty, "the buffer holds what the store would be written with");

            Assert.That(pending.Apply(), Is.False, "an OK that wrote nothing is no change");

            Assert.That(live.Records.Candidates, Is.Empty, "no authority, no retirement and no store key");
            Assert.That(live.MarkedModifiedCount, Is.EqualTo(0), "and nothing is marked for it");
        }

        [Test]
        public void A_Recreate_and_a_Delete_on_one_dialog_write_what_the_Delete_alone_writes()
        {
            // A client revoked, then Recreate and Delete on one dialog, OK, and a copy
            // synchronized before the revocation merged in: the authority that stood is retired,
            // the retirement holding the store key, and nothing is served for the copy's record.
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            MemoryStore stale;
            Revoked(live, first, out stale);
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            Assert.That(Service(pending).Delete().Succeeded, Is.True);
            Assert.That(StoreAuthorities.Served(pending.Store.Read()), Is.Null, "the page reads it deleted");
            Assert.That(Held(pending), Is.EqualTo(new[] { "Delete" }), "the buffer holds the Delete of the authority the database holds");
            Assert.That(pending.Apply(), Is.True);
            live.Records.MergeWithoutDeletions(stale);

            var reading = live.Store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAuthorities.Served(reading), Is.Null);
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { first }), "the authority made between reached no database");
            Assert.That(
                reading.InForce.Records.Where(record => record.Is("retired")).Select(record => record.Form.StoreKey != null),
                Is.EqualTo(new[] { true }),
                "and the retirement holds the store key");
        }

        [Test]
        public void Create_Delete_and_Create_on_one_dialog_write_the_last_Create()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Create().Succeeded, Is.True);
            Assert.That(Service(pending).Delete().Succeeded, Is.True);
            Assert.That(Service(pending).Create().Succeeded, Is.True);
            var last = StoreAuthorities.Served(pending.Store.Read());
            Assert.That(Held(pending), Is.EqualTo(new[] { "Create" }));

            Assert.That(pending.Apply(), Is.True);

            var reading = live.Store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(last));
            Assert.That(reading.InForce.Taken("retired"), Is.Empty, "the authority made between reached no database");
            Assert.That(reading.InForce.Records.Count(record => record.Is("authority")), Is.EqualTo(1));
        }

        [Test]
        public void Recreate_Delete_and_Create_on_one_dialog_retire_the_database_s_authority_and_write_the_new_one()
        {
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            Assert.That(Service(pending).Delete().Succeeded, Is.True);
            Assert.That(Service(pending).Create().Succeeded, Is.True);
            var last = StoreAuthorities.Served(pending.Store.Read());
            Assert.That(Held(pending), Is.EqualTo(new[] { "Delete", "Create" }));

            Assert.That(pending.Apply(), Is.True);

            var reading = live.Store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(last));
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { first }));
            Assert.That(reading.InForce.Records.Count(record => record.Is("authority")), Is.EqualTo(1));
        }

        [Test]
        public void A_Create_and_a_Recreate_on_a_database_with_no_key_are_held_and_written_as_a_Create()
        {
            // What is left of the two starts the store under the key the Create made, which is
            // what a Create writes, and its name is what the log gives for an act left out.
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Create().Succeeded, Is.True);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            var last = StoreAuthorities.Served(pending.Store.Read());
            Assert.That(Held(pending), Is.EqualTo(new[] { "Create" }));

            Assert.That(pending.Apply(), Is.True);

            var reading = live.Store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(last), "written under the key the Create made");
            Assert.That(reading.InForce.Taken("retired"), Is.Empty, "the authority made between reached no database");
            Assert.That(reading.InForce.Records.Count(record => record.Is("authority")), Is.EqualTo(1));
        }

        [Test]
        public void A_Recreate_a_synchronize_that_retires_the_authority_and_a_Delete_leave_the_store_under_its_key_at_OK()
        {
            // Another copy's answer retiring the database's authority arrives while the dialog
            // stands, between the Recreate and the Delete: whatever OK writes passes under the
            // store key the database holds.
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);

            StoreSeeds.Retire(live.Store, first);
            string key;
            using (var before = live.Store.Read())
            {
                key = before.InForce.Key.Name;
            }

            // The Recreate's ground went with the authority it retires, so the page reads the store
            // as it stands, with no authority standing: the Delete has nothing to retire, folds
            // into nothing, and OK leaves the Recreate out.
            Assert.That(Service(pending).Delete().Succeeded, Is.True);
            Assert.That(Held(pending), Is.EqualTo(new[] { "Recreate" }));
            var writes = live.Records.Writes;

            pending.Apply();

            Assert.That(live.Records.Writes, Is.EqualTo(writes), "nothing is written");
            var reading = live.Store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Key.Name, Is.EqualTo(key), "every record OK wrote passes under the store key");
            Assert.That(reading.Damage, Is.Zero);
            Assert.That(StoreAuthorities.Served(reading), Is.Null);
        }

        [Test]
        public void A_press_is_refused_with_nothing_held_where_the_whole_buffer_with_it_would_be_refused()
        {
            // An act held, and the store grown under the dialog until it no longer fits with that
            // act: the page reads the store as it is, where a further act fits by itself, and OK
            // would write neither of them.
            var live = new FakeGateDatabase();
            live.Records.Put(StoreRecords.Authority(StoreRecords.One, 'a'));
            live.Records.Put(StoreRecords.Head(StoreRecords.One, 1, string.Empty, RetiredItems(0, StoreFormat.MaxItems - 4)));
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(pending.Store.Write(new[] { Retirements('b', 'c') }).Taken, Is.True);

            live.Records.Put(StoreRecords.Head(StoreRecords.One, 1, string.Empty, RetiredItems(StoreFormat.MaxItems, 2)));
            Assume.That(live.Store.Read().State, Is.EqualTo(StoreState.Whole));

            var press = pending.Store.Write(new[] { Retirements('d') });

            Assert.That(press.Taken, Is.False);
            Assert.That(((PendingRecords)pending.Store).Acts, Has.Count.EqualTo(1), "nothing more is held");
        }

        [Test]
        public void A_store_near_the_ceiling_of_its_room_reads_through_the_buffer_as_the_single_write_OK_makes_of_every_act_held()
        {
            // Room for two more writes, and three acts held: OK writes them in a single write,
            // which names a single summary entry more, and the page reads the store as that write
            // leaves it, with as many writes left as OK leaves.
            var live = new FakeGateDatabase();
            live.Records.Put(StoreRecords.Authority(StoreRecords.One, 'a'));
            live.Records.Put(StoreRecords.Head(StoreRecords.One, 1, Named(7939)));
            int named;
            using (var reading = live.Store.Read())
            {
                Assume.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assume.That(reading.Room.Writes, Is.EqualTo(2), "room for two more writes");
                named = reading.Room.Named;
            }

            var pending = new PendingDatabase(live, NullGateLog.Instance);
            foreach (var id in new[] { 'b', 'c', 'd' })
            {
                Assert.That(pending.Store.Write(new[] { Retirements(id) }).Taken, Is.True);
            }

            int writes;
            using (var buffered = pending.Store.Read())
            {
                Assert.That(buffered.State, Is.EqualTo(StoreState.Whole), buffered.Reason);
                Assert.That(buffered.Room.Named, Is.EqualTo(named + 1), "the room of a single write spent");
                writes = buffered.Room.Writes;
            }

            Assert.That(pending.Apply(), Is.True);

            using (var written = live.Store.Read())
            {
                Assert.That(written.State, Is.EqualTo(StoreState.Whole), written.Reason);
                Assert.That(written.Room.Writes, Is.EqualTo(writes), "the page counts the writes OK leaves");
            }
        }

        [Test]
        public void A_write_refused_at_OK_writes_nothing_and_names_what_was_not_written()
        {
            // As the writer's guard refuses a write where KeePass is part way through something of
            // its own.
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var client = Revoked(live, first, out _);
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            live.Records.RefuseWrites = "KeePass is part way through a save";

            Assert.That(pending.Apply(), Is.False);

            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.EqualTo(first));
            Assert.That(live.Store.Read().InForce.Taken("revoked"), Does.Contain(client), "the revocation stays as it was");
            Assert.That(live.MarkedModifiedCount, Is.EqualTo(0));
            Assert.That(log.Warnings.Single().Message, Does.Contain("Recreate").And.Contain("part way through a save"));
            Assert.That(log.Errors, Is.Empty, "a refusal the operator's OK can meet is no defect");
        }

        [Test]
        public void An_act_left_out_at_OK_leaves_the_revocation_refusing_a_copy_s_admission()
        {
            // Another authority arrives under the dialog, as a synchronize brings it, so the
            // Recreate's ground goes; once the operator keeps the first, the revocation still
            // refuses the admission a copy from before it brings back.
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            MemoryStore stale;
            var client = Revoked(live, first, out stale);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            var other = StoreSeeds.Beside(live.Store);

            pending.Apply();
            Assert.That(
                new AuthorityService(live.Store, NullGateLog.Instance).Keep(first, new[] { first, other }).Succeeded,
                Is.True);
            live.Records.MergeWithoutDeletions(stale);

            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.EqualTo(first));
            Assert.That(live.Store.Read().InForce.Taken("revoked"), Does.Contain(client));
            Assert.That(new Admissions(live).Find(client), Is.Null);
        }

        [Test]
        public void A_revocation_pressed_before_a_Recreate_left_out_at_OK_still_lands()
        {
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var client = Admitted(live, first);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(new Admissions(pending).Remove(client), Is.True);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            StoreSeeds.Retire(live.Store, first);

            pending.Apply();

            Assert.That(live.Store.Read().InForce.Taken("revoked"), Does.Contain(client), "the Recreate was left out, and the revocation is the operator's own");
            Assert.That(new Admissions(live).Find(client), Is.Null);
        }

        [Test]
        public void A_revocation_held_for_OK_is_composed_under_the_key_the_store_holds_at_OK()
        {
            // The buffer holds the client to revoke, and the record is composed at OK against the
            // store as it stands then: here a store started again under another key between the
            // press and OK, under which the revocation is written.
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var client = Admitted(live, first);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(new Admissions(pending).Remove(client), Is.True);

            foreach (var candidate in live.Records.Candidates.ToList())
            {
                live.Records.Drop(candidate.Hex);
            }

            StoreSeeds.Authority(live.Store);

            Assert.That(pending.Apply(), Is.True);
            Assert.That(live.Store.Read().InForce.Taken("revoked"), Does.Contain(client));
        }

        [Test]
        public void A_sitting_of_presses_composes_each_act_once_until_the_store_or_the_acts_held_change()
        {
            // What the buffer composed is kept while the store and the acts held stay as they
            // were: a press composes its own act over what the acts before it leave, and a read
            // composes nothing a press or a read before it composed. A press and a read each read
            // the live store as often whatever the acts held. A change to the store composes every
            // act again.
            var live = new FakeGateDatabase();
            var served = StoreSeeds.Authority(live.Store);
            var clients = Enumerable.Range(0, 4).Select(index => Admitted(live, served)).ToList();
            var records = new PendingRecords(() => live.Store);
            var kept = new List<StoreAct>();
            var atPress = new List<int>();
            var atRead = new List<int>();
            foreach (var client in clients)
            {
                StoreAct act;
                using (var reading = records.Read())
                {
                    act = AdmissionActs.Revoke(reading, client);
                }

                var reads = live.Records.Reads;
                Assert.That(records.Write(new[] { act }).Waits, Is.True);
                atPress.Add(live.Records.Reads - reads);

                reads = live.Records.Reads;
                records.Read().Dispose();
                atRead.Add(live.Records.Reads - reads);

                var composed = records.Composed();
                Assert.That(composed, Has.Count.EqualTo(kept.Count + 1));
                Assert.That(
                    kept.Select((before, at) => ReferenceEquals(before, composed[at])), Is.All.True, "composed once, at its own press");
                kept = composed.ToList();
            }

            Assert.That(atPress, Is.All.EqualTo(atPress[0]), "a press reads the live store as often whatever the acts held");
            Assert.That(atRead, Is.All.EqualTo(atRead[0]), "and so does a read of the buffer");

            Admitted(live, served);

            var again = records.Composed();
            Assert.That(kept.Select((before, at) => ReferenceEquals(before, again[at])), Is.All.False, "composed again over a store that changed");
            records.Discard();
        }

        [Test]
        public void A_revocation_held_while_the_store_lost_its_key_stays_as_pressed_and_writes_nothing()
        {
            // Composed at OK over a store that holds no key, the act is the act pressed, under a
            // key that no longer stands, and the plan leaves it out for that: OK writes nothing
            // under it.
            var live = new FakeGateDatabase();
            var served = StoreSeeds.Authority(live.Store);
            var client = Admitted(live, served);
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            Assert.That(new Admissions(pending).Remove(client), Is.True);
            var pressed = ((PendingRecords)pending.Store).Acts.Single();

            foreach (var candidate in live.Records.Candidates.ToList())
            {
                live.Records.Drop(candidate.Hex);
            }

            var offered = live.Records.Offered.Count;
            Assert.That(pending.Apply(), Is.False);
            Assert.That(live.Records.Offered.Skip(offered).Single(), Is.SameAs(pressed), "composed at OK over the store as it stands then");
            Assert.That(log.Warnings.Single().Message, Does.Contain("composed under a store key that does not stand"));
            Assert.That(live.Records.Candidates, Is.Empty, "no revocation under a key that no longer stands");
        }

        [Test]
        public void An_act_written_at_OK_deletes_every_admission_of_the_authority_it_retires_as_the_store_stands_then()
        {
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            Admitted(live, first);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            Assert.That(new Admissions(pending).All().Records, Is.Empty, "the page reads them gone");

            // A client admitted under the dialog, as a pairing lands while it is open.
            var later = Admitted(live, first);

            Assert.That(pending.Apply(), Is.True);

            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.Not.EqualTo(first));
            Assert.That(live.Store.Read().InForce.Admissions, Is.Empty,
                "every admission went, the admission landed under the dialog among them");
            Assert.That(new Admissions(live).Find(later), Is.Null);
        }

        [Test]
        public void Where_nothing_can_be_written_a_press_is_refused_and_OK_takes_nothing_away()
        {
            // As where the dialogs that would take a write back are not watched.
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var client = Revoked(live, first, out _);
            live.Records.Unwritable = "the dialogs that would take a write back could not be watched";
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            var recreated = Service(pending).Recreate();

            Assert.That(recreated.Succeeded, Is.False);
            Assert.That(recreated.Message, Does.Contain("could not be watched"));
            Assert.That(pending.AuthorityWaiting, Is.False, "nothing is held that OK could not write");
            Assert.That(pending.Apply(), Is.False);
            Assert.That(live.Store.Read().InForce.Taken("revoked"), Does.Contain(client));
            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.EqualTo(first));
        }

        [Test]
        public void Two_Recreates_on_the_same_dialog_leave_the_last_authority_and_retire_the_first_alone()
        {
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store);
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            var between = StoreAuthorities.Served(pending.Store.Read());
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            var last = StoreAuthorities.Served(pending.Store.Read());

            pending.Apply();

            var reading = live.Store.Read();
            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(last));
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { first }), "the authority made between reached no database");
            Assert.That(reading.InForce.Records.Any(record => record.Form.Member("id") == between), Is.False);
        }

        [Test]
        public void An_act_whose_ground_went_under_the_dialog_is_left_out_at_OK_and_an_act_with_none_is_written()
        {
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);

            // The authority is deleted underneath the dialog, as another dialog's OK or a
            // synchronize leaves it.
            Assert.That(new AuthorityService(live.Store, NullGateLog.Instance).Delete().Succeeded, Is.True);

            // Beside the Recreate, an act that holds to no ground, as a revocation is.
            var key = (byte[])live.Store.Read().InForce.Key.Bytes.Clone();
            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            Assert.That(pending.Store.Write(new[] { new StoreAct("a name", key, StoreFormat.KeyName(key), new[] { name }, null) }).Taken, Is.True);

            pending.Apply();

            var reading = live.Store.Read();
            Assert.That(StoreAuthorities.Served(reading), Is.Null, "the Recreate was left out");
            Assert.That(reading.InForce.Records.Any(record => record.Hex == name.Hex), Is.True, "the act with no ground was written");
        }

        [Test]
        public void A_single_act_whose_ground_went_writes_nothing_at_OK_and_is_no_refusal_of_the_guard()
        {
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            Assert.That(new AuthorityService(live.Store, NullGateLog.Instance).Delete().Succeeded, Is.True);
            var before = live.Records.Writes;

            pending.Apply();

            Assert.That(live.Records.Writes, Is.EqualTo(before), "the Recreate is not written");
            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.Null);
        }

        [Test]
        public void Cancel_drops_every_act_and_the_page_reads_the_store_as_it_is()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Create().Succeeded, Is.True);

            pending.Discard();

            Assert.That(pending.AuthorityWaiting, Is.False);
            Assert.That(pending.Store.Read().State, Is.EqualTo(StoreState.None));
            pending.Apply();
            Assert.That(live.Records.Candidates, Is.Empty);
        }

        [Test]
        public void A_press_on_a_store_that_holds_the_gate_back_is_refused_with_its_word()
        {
            var live = new FakeGateDatabase();
            live.Records.Put(StoreRecords.Authority(StoreRecords.One, 'a'));
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            var created = Service(pending).Create();

            Assert.That(created.Succeeded, Is.False);
            Assert.That(created.Message, Does.Contain("stopped"));
            Assert.That(pending.AuthorityWaiting, Is.False);
        }

        [Test]
        public void An_OK_whose_write_throws_overwrites_the_keys_of_the_acts_it_held()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Create().Succeeded, Is.True);
            var held = ((PendingRecords)pending.Store).Acts.Single();
            live.Records.ThrowOnWrite = new InvalidOperationException("a database that went under the dialog");

            Assert.Catch<InvalidOperationException>(() => pending.Apply());

            Assert.That(held.Key(), Is.All.EqualTo((byte)0));
            Assert.That(pending.AuthorityWaiting, Is.False);
        }

        [Test]
        public void An_OK_whose_lines_throw_after_the_write_landed_lets_the_buffer_go_last()
        {
            // The line telling what landed comes as soon as the write's outcome is known, the
            // lines of what landed after it, and the buffer, with the store key each act holds, is
            // let go after both, whatever they throw.
            var live = new FakeGateDatabase();
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            Assert.That(Service(pending).Create().Succeeded, Is.True);
            var held = ((PendingRecords)pending.Store).Acts.Single();
            var heldAtTheLines = false;

            Assert.Catch<InvalidOperationException>(() => pending.Apply(() =>
            {
                var key = held.Key();
                heldAtTheLines = key.Any(value => value != 0);
                Array.Clear(key, 0, key.Length);
                throw new InvalidOperationException("a defect writing the lines of what landed");
            }));

            Assert.That(heldAtTheLines, Is.True, "the buffer is let go after the lines of what landed");
            Assert.That(
                log.Entries.Where(entry => entry.Level == GateLogFormat.InfoLevel).Select(entry => entry.Message),
                Has.Some.StartsWith("the plugin's store holds what the " + GateName.Product + " tab held: Create"));
            Assert.That(live.MarkedModifiedCount, Is.EqualTo(1), "the database is marked for what landed");
            Assert.That(held.Key(), Is.All.EqualTo((byte)0), "and the buffer is let go whatever the lines threw");
            Assert.That(pending.AuthorityWaiting, Is.False);
        }

        [Test]
        public void A_name_added_and_removed_on_a_dialog_writes_nothing_and_neither_does_a_name_removed_and_added_again()
        {
            // `STORE.md` §627: the buffer holds the operator's last press for each name, and a
            // press with nothing left to write against the store holds nothing. Written as records,
            // either pair would leave an item in every later summary entry for good.
            var live = Keyed("held");
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            var page = new DatabaseProfiles(pending, null);

            Assert.That(page.Add("fresh"), Is.Null);
            Assert.That(page.Names(), Does.Contain("fresh"), "the page reads a name added as on the list before OK");
            Assert.That(page.Remove("fresh"), Is.Null);
            Assert.That(page.Remove("held"), Is.Null);
            Assert.That(page.Names(), Does.Not.Contain("held"), "and a name removed as off it");
            Assert.That(page.Add("held"), Is.Null);

            Assert.That(Held(pending), Is.Empty, "the last press for each name has nothing left to write");
            Assert.That(pending.ProfilesWaiting, Is.False);
            var writes = live.Records.Writes;

            pending.Apply();

            Assert.That(live.Records.Writes, Is.EqualTo(writes), "nothing reached the store");
            Assert.That(NamesOf(live), Is.EqualTo(new[] { "held" }));
        }

        [Test]
        public void An_Add_of_a_name_a_pairing_puts_on_the_list_under_the_dialog_writes_nothing_more()
        {
            var live = Keyed();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);
            Assume.That(pending.ProfilesWaiting, Is.True);

            // A pairing approved for the name lands on the live store while the dialog stands.
            StoreSeeds.Name(live.Store, "ops");

            pending.Apply();

            using (var reading = live.Records.Read())
            {
                Assert.That(KeePassGate.Acl.ProfileNames.Of(reading).Names, Is.EqualTo(new[] { "ops" }));
                Assert.That(reading.InForce.Additions, Has.Count.EqualTo(1), "the pairing's addition, and no second");
            }
        }

        [Test]
        public void A_Remove_pressed_on_a_name_a_pairing_added_under_the_dialog_is_written_and_deletes_that_addition()
        {
            var live = Keyed();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            var page = new DatabaseProfiles(pending, null);
            StoreSeeds.Name(live.Store, "ops");
            Assume.That(page.Names(), Does.Contain("ops"), "the page shows the name the pairing added");

            Assert.That(page.Remove("ops"), Is.Null);
            pending.Apply();

            using (var reading = live.Records.Read())
            {
                Assert.That(KeePassGate.Acl.ProfileNames.Of(reading).Names, Is.Empty);
                Assert.That(reading.InForce.Additions, Is.Empty, "the addition that did not see the removal is deleted");
                Assert.That(reading.InForce.RemovalsOf("ops"), Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void An_Add_a_removal_merged_in_under_the_dialog_overtook_writes_nothing_and_the_log_names_it()
        {
            // An addition names the removals the page showed at its press: a removal made apart,
            // which the operator never saw, is not overruled by a press made before it arrived.
            var live = Keyed();
            StoreSeeds.Name(live.Store, "ops");
            StoreSeeds.Unname(live.Store, "ops");
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            var page = new DatabaseProfiles(pending, null);
            Assert.That(page.Add("ops"), Is.Null);

            var copy = live.Records.Copy();
            StoreSeeds.Name(copy, "ops");
            StoreSeeds.Unname(copy, "ops");
            live.Records.Synchronize(copy);
            Assert.That(page.Names(), Does.Not.Contain("ops"), "the page reads what OK would write");
            Assert.That(page.Overtaken("ops"), Is.True, "and tells why");
            var writes = live.Records.Writes;
            Assert.That(pending.OvertakenNow(), Is.EqualTo(new[] { "ops" }), "found before OK");
            Assert.That(live.Records.Writes, Is.EqualTo(writes), "by reading alone");
            Assert.That(pending.ProfilesWaiting, Is.True, "with the press still held");

            pending.Apply();

            Assert.That(NamesOf(live), Is.Empty, "the name is off the list");
            using (var reading = live.Records.Read())
            {
                Assert.That(reading.InForce.Additions.Where(addition => addition.Form.Member(StoreKinds.NameMember) == "ops"), Is.Empty,
                    "and no addition of it is written");
            }

            Assert.That(log.Warnings.Any(entry => entry.Message.Contains("adding a profile ops")), Is.True, "the log names it");
        }

        [Test]
        public void An_Add_of_a_name_whose_removal_only_a_deletion_record_tells_of_is_asked_about_shown_and_written_in_force()
        {
            // The page shows the name off the list and asks before adding it back, as for any
            // removed name a rule still names. The addition names the removal that the deletion
            // record tells of once the name is known, so the page shows the name added and OK
            // writes it in force, `STORE.md` §616.
            var live = Keyed();
            var group = new string('a', 32);
            live.Nodes = new TestRuledNodes(new TestRuledNode(group));
            TestRuledNodes.Rule(live.Records, group, "ops", "row:R");
            StoreRecords.RemovedApart(live.Records, "ops");
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            var page = new DatabaseProfiles(pending, null);
            Assert.That(page.Names(), Does.Not.Contain("ops"), "off the list");
            Assert.That(page.Returning("ops"), Is.EqualTo("Adding ops again puts the rule naming it back in force."));

            Assert.That(page.Add("ops"), Is.Null);
            Assert.That(page.Names(), Does.Contain("ops"), "the page reads what OK would write");
            Assert.That(page.Overtaken("ops"), Is.False);

            pending.Apply();

            Assert.That(NamesOf(live), Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void An_Add_a_removal_only_a_deletion_record_tells_of_overtook_under_the_dialog_writes_nothing()
        {
            // A removal made in another copy after the press, of which the file keeps nothing but
            // its deletion record, is a removal the operator was not shown.
            var live = Keyed();
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            var page = new DatabaseProfiles(pending, null);
            Assert.That(page.Add("ops"), Is.Null);

            StoreRecords.RemovedApart(live.Records, "ops");
            Assert.That(page.Names(), Does.Not.Contain("ops"), "the page reads what OK would write");
            Assert.That(page.Overtaken("ops"), Is.True, "and tells why");

            pending.Apply();

            Assert.That(NamesOf(live), Is.Empty);
            using (var reading = live.Records.Read())
            {
                Assert.That(reading.InForce.Additions.Where(addition => addition.Form.Member(StoreKinds.NameMember) == "ops"), Is.Empty,
                    "no addition of it is written");
            }

            Assert.That(log.Warnings.Any(entry => entry.Message.Contains("adding a profile ops")), Is.True, "the log names it");
        }

        [Test]
        public void An_Add_a_store_split_under_the_dialog_leaves_out_is_not_overtaken()
        {
            // An authority under a key of its own, as a copy that made a store of its own leaves
            // after a synchronize: the page lists no name, and no removal made apart is why.
            var live = Keyed();
            var page = new DatabaseProfiles(new PendingDatabase(live, NullGateLog.Instance), null);
            Assert.That(page.Add("ops"), Is.Null);

            live.Records.Put(StoreRecords.Authority(StoreRecords.Two, 'b'));

            Assert.That(page.Names(), Does.Not.Contain("ops"));
            Assert.That(page.Overtaken("ops"), Is.False);
        }

        [Test]
        public void An_Add_a_pairing_made_good_under_the_dialog_is_not_overtaken()
        {
            // Nothing is left of the press to write, since the name is on the list already: not a
            // removal the page's line names.
            var live = Keyed();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);

            StoreSeeds.Name(live.Store, "ops");

            Assert.That(pending.OvertakenNow(), Is.Empty);
        }

        [Test]
        public void A_name_added_beside_a_Create_held_on_the_dialog_is_written_under_the_key_the_Create_brings()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Create().Succeeded, Is.True);
            Assert.That(new DatabaseProfiles(pending, null).Add("ops"), Is.Null);

            Assert.That(pending.Apply(), Is.True);

            Assert.That(StoreSeeds.Served(live.Store), Is.Not.Null);
            Assert.That(NamesOf(live), Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_name_added_beside_a_Create_folded_away_on_the_dialog_is_left_out_and_named()
        {
            // Only an authority starts a store, `STORE.md` §627: with the Create taken back by a
            // Delete on the same dialog, the name has no key to be written under.
            var live = new FakeGateDatabase();
            var log = new RecordingGateLog();
            var pending = new PendingDatabase(live, log);
            var page = new DatabaseProfiles(pending, null);
            Assert.That(Service(pending).Create().Succeeded, Is.True);
            Assert.That(page.Add("ops"), Is.Null);
            Assert.That(Service(pending).Delete().Succeeded, Is.True, "the Delete is taken");
            Assert.That(page.Names(), Does.Not.Contain("ops"), "the page reads what OK would write");
            Assert.That(page.Overtaken("ops"), Is.False, "and no removal made apart is why");

            pending.Apply();

            Assert.That(live.Records.Candidates, Is.Empty, "nothing reached the store");
            Assert.That(log.Warnings.Any(entry => entry.Message.Contains(KeePassGate.Acl.ProfileActs.AddName)), Is.True,
                "the log names the press left out");
        }

        /// <summary>A database whose store holds an authority and these names.</summary>
        private static FakeGateDatabase Keyed(params string[] names)
        {
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store);
            foreach (var name in names)
            {
                StoreSeeds.Name(live.Store, name);
            }

            return live;
        }

        /// <summary>The names on the list of the live store.</summary>
        private static IReadOnlyList<string> NamesOf(FakeGateDatabase live)
        {
            using (var reading = live.Records.Read())
            {
                return KeePassGate.Acl.ProfileNames.Of(reading).Names;
            }
        }

        private static AuthorityService Service(PendingDatabase pending)
        {
            return new AuthorityService(pending.Store, NullGateLog.Instance);
        }

        /// <summary>The name of each act the buffer holds, in the order it holds them.</summary>
        private static IEnumerable<string> Held(PendingDatabase pending)
        {
            return ((PendingRecords)pending.Store).Acts.Select(act => act.Name);
        }

        /// <summary>
        /// The `after` of a summary entry naming this many identifiers no store holds, numbered
        /// from zero.
        /// </summary>
        private static string Named(int count)
        {
            return string.Join(" ", Enumerable.Range(0, count).Select(index => index.ToString("x32", CultureInfo.InvariantCulture)));
        }

        /// <summary>Items retiring authorities no store holds, numbered from a start.</summary>
        private static string[] RetiredItems(int start, int count)
        {
            return Enumerable.Range(start, count)
                .Select(index => StoreComposer.Item(("authority", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "retired")))
                .ToArray();
        }

        /// <summary>An act under the store key the tests compose, retiring authorities no store holds.</summary>
        private static StoreAct Retirements(params char[] ids)
        {
            var key = StoreRecords.One;
            return new StoreAct(
                "retirements",
                key,
                StoreFormat.KeyName(key),
                ids.Select(id => StoreRecord.Compose(key, "retired", new Dictionary<string, string> { { "authority", StoreRecords.Hex(id) } })).ToList(),
                null);
        }

        /// <summary>A client of this authority admitted in the live database, answering its admission key.</summary>
        private static string Admitted(FakeGateDatabase live, string authority)
        {
            using (var client = new AuthorityIssuer().IssueClient(StoreSeeds.KeyOf(live.Store, authority), "agent-fictitious", new[] { "reader" }).Read())
            {
                StoreSeeds.Client(live.Store, AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "reader" }, authority).Members());
                return CertificateDigest.Of(client);
            }
        }

        /// <summary>
        /// A client of this authority admitted and revoked in the live database, answering its
        /// admission key and a copy of the store made before the revocation.
        /// </summary>
        private static string Revoked(FakeGateDatabase live, string authority, out MemoryStore stale)
        {
            var client = Admitted(live, authority);
            stale = live.Records.Copy();
            StoreSeeds.Revoke(live.Store, client);
            Assert.That(live.Store.Read().InForce.Taken("revoked"), Does.Contain(client));
            return client;
        }
    }
}
