using System;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What each command for the authority writes into the plugin's store, `STORE.md` §614 and
    /// §632: the key a Create starts and a Recreate and a Delete keep, and the retirements of
    /// keeping an authority, each holding the key (S11).
    /// </summary>
    [TestFixture]
    public sealed class AuthorityCommandsInStoreTests
    {
        [Test]
        public void Create_where_no_key_stands_starts_a_store_with_its_version_record()
        {
            var store = new MemoryStore();

            Assert.That(Service(store).Create().Succeeded, Is.True);

            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Records.Count(record => record.Is("format")), Is.EqualTo(1));
            Assert.That(reading.InForce.Plugin, Is.EqualTo(store.Plugin));
            Assert.That(reading.InForce.Authorities.Single().Form.StoreKey, Is.Not.Null, "the authority holds the key");
        }

        [TestCase("written")]
        [TestCase("refused")]
        [TestCase("thrown")]
        public void An_act_handed_to_a_store_that_writes_it_has_its_key_overwritten_whatever_came_of_it(string outcome)
        {
            // `CA.md` §208: the store key's bytes are overwritten as each holder is done with them,
            // the act a command composes among them, once the write was planned and applied,
            // refused or thrown.
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            store.Offered.Clear();
            if (outcome == "refused")
            {
                store.RefuseWrites = "a write the writer refuses";
            }
            else if (outcome == "thrown")
            {
                store.ThrowOnWrite = new InvalidOperationException("a database that went");
            }

            try
            {
                Service(store).Delete();
            }
            catch (InvalidOperationException) when (outcome == "thrown")
            {
                // What a database that went raises reaches the caller as it did.
            }

            Assert.That(store.Offered.Single().Key(), Is.All.EqualTo((byte)0));
        }

        [Test]
        public void Recreate_retires_the_authority_deletes_its_entry_and_carries_the_key()
        {
            var store = new MemoryStore();
            var first = StoreSeeds.Authority(store);
            var key = store.Read().InForce.Key.Name;

            Assert.That(Service(store).Recreate().Succeeded, Is.True);

            var reading = store.Read();
            Assert.That(reading.InForce.Key.Name, Is.EqualTo(key), "the same store key");
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { first }));
            Assert.That(reading.InForce.Records.Count(record => record.Is("authority")), Is.EqualTo(1), "the retired authority's entry is deleted");
            Assert.That(StoreAuthorities.Served(reading), Is.Not.EqualTo(first));
        }

        [Test]
        public void Delete_writes_a_retirement_holding_the_key_and_a_Create_after_it_keeps_that_key()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            var key = store.Read().InForce.Key.Name;

            Assert.That(Service(store).Delete().Succeeded, Is.True);
            var deleted = store.Read();
            Assert.That(deleted.State, Is.EqualTo(StoreState.Whole), deleted.Reason);
            Assert.That(deleted.InForce.Key.Name, Is.EqualTo(key), "the retirement holds the key");
            Assert.That(deleted.InForce.Authorities, Is.Empty);

            Assert.That(Service(store).Create().Succeeded, Is.True);
            Assert.That(store.Read().InForce.Key.Name, Is.EqualTo(key), "Create takes the key the retirement holds");
        }

        [Test]
        public void Keeping_an_authority_retires_each_of_the_others_each_retirement_holding_the_key()
        {
            var store = new MemoryStore();
            var ids = StoreSeeds.Undecided(store);

            var kept = Service(store).Keep(ids[0], ids);

            Assert.That(kept.Succeeded, Is.True, kept.Message);
            var reading = store.Read();
            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(ids[0]));
            var retirement = reading.InForce.Records.Single(record => record.Is("retired"));
            Assert.That(retirement.Form.Member("authority"), Is.EqualTo(ids[1]));
            Assert.That(retirement.Form.StoreKey, Is.Not.Null, "the retirement holds the key");
        }

        [Test]
        public void Keeping_an_authority_among_a_set_that_changed_is_overtaken_and_writes_nothing()
        {
            var store = new MemoryStore();
            var ids = StoreSeeds.Undecided(store);
            StoreSeeds.Beside(store);
            var before = store.Writes;

            var kept = Service(store).Keep(ids[0], ids);

            Assert.That(kept.IsOvertaken, Is.True);
            Assert.That(store.Writes, Is.EqualTo(before));
        }

        [Test]
        public void Create_on_a_stopped_store_is_refused_and_writes_nothing()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            Assert.That(Service(store).Delete().Succeeded, Is.True);
            var head = store.Read().InForce.Heads.Single().Hex;
            store.Drop(head);
            Assume.That(store.Read().State, Is.EqualTo(StoreState.Stopped));
            var before = store.Writes;

            var created = Service(store).Create();

            Assert.That(created.Succeeded, Is.False);
            Assert.That(created.Message, Does.Contain("stopped"));
            Assert.That(store.Writes, Is.EqualTo(before));
        }

        [TestCase("Recreate")]
        [TestCase("Delete")]
        public void A_command_that_retires_the_authority_deletes_the_records_of_kind_client_it_issued_and_no_other(string command)
        {
            // `STORE.md` §627 step 3, the write's own retirement counting as present.
            var store = new MemoryStore();
            var first = StoreSeeds.Authority(store);
            var key = (byte[])store.Read().InForce.Key.Bytes.Clone();
            var issued = Client(key, 'c', first);
            var other = Client(key, 'e', new string('9', 64));
            store.Put(issued);
            store.Put(other);

            var service = Service(store);
            Assert.That((command == "Recreate" ? service.Recreate() : service.Delete()).Succeeded, Is.True);

            var reading = store.Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(store.Candidates.Select(candidate => candidate.Hex), Does.Not.Contain(issued.Hex));
            Assert.That(store.Deletions.Select(KeePassGate.Hex.Of), Does.Contain(issued.Hex), "deleted with a deletion record");
            Assert.That(store.Candidates.Select(candidate => candidate.Hex), Does.Contain(other.Hex));
        }

        /// <summary>A record of kind `client` under a store key, of a certificate this authority issued.</summary>
        private static StoreComposer.Composed Client(byte[] key, char client, string issuer)
        {
            var clientKey = new string(client, 64);
            return StoreComposer.Record(
                key, "client", "client:" + clientKey,
                StoreComposer.Content(("certificate", clientKey), ("client", clientKey), ("issuer", issuer)));
        }

        [Test]
        public void A_record_written_twice_is_in_the_file_once()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            var key = (byte[])store.Read().InForce.Key.Bytes.Clone();
            var name = StoreRecord.Compose(key, "profile", new System.Collections.Generic.Dictionary<string, string>
            {
                { "after", string.Empty },
                { "name", "reader" },
                { "nonce", new string('0', 2 * StoreFormat.NonceLength) },
            });
            var act = new StoreAct("a name", key, StoreFormat.KeyName(key), new[] { name }, null);

            Assert.That(store.Write(new[] { act, act }).Taken, Is.True);
            store.Write(new[] { act });

            Assert.That(store.Candidates.Count(candidate => candidate.Hex == name.Hex), Is.EqualTo(1));
        }

        [Test]
        public void A_write_the_store_does_not_keep_is_reported_and_not_believed()
        {
            var store = new MemoryStore { SwallowWrites = true };

            var created = Service(store).Create();

            Assert.That(created.Succeeded, Is.False);
            Assert.That(created.Message, Does.Contain("nothing was stored"));
        }

        private static AuthorityService Service(MemoryStore store)
        {
            return new AuthorityService(store, NullGateLog.Instance);
        }
    }
}
