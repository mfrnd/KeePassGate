using System.Collections.Generic;
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
            Assert.That(pending.StoreWaiting, Is.True);

            Assert.That(pending.Apply(), Is.True);
            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.Not.Null, "OK writes it");
            Assert.That(pending.StoreWaiting, Is.False);
        }

        [Test]
        public void A_Create_and_a_Delete_on_the_same_dialog_write_nothing()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);

            Assert.That(Service(pending).Create().Succeeded, Is.True);
            Assert.That(Service(pending).Delete().Succeeded, Is.True);
            Assert.That(StoreAuthorities.Served(pending.Store.Read()), Is.Null);

            pending.Apply();

            Assert.That(live.Records.Candidates, Is.Empty, "no authority, no retirement and no store key");
            Assert.That(pending.StoreRefusal, Is.Null);
        }

        [Test]
        public void Two_Recreates_on_the_same_dialog_leave_the_last_authority_and_retire_the_first_alone()
        {
            var live = new FakeGateDatabase();
            var first = StoreSeeds.Authority(live.Store, live.Values);
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
            StoreSeeds.Authority(live.Store, live.Values);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);

            // The authority is deleted underneath the dialog, as another dialog's OK or a
            // synchronize leaves it.
            Assert.That(new AuthorityService(live.Store, live.Values, NullGateLog.Instance).Delete().Succeeded, Is.True);

            // Beside the Recreate, an act that holds to no ground, as a revocation is.
            var key = (byte[])live.Store.Read().InForce.Key.Bytes.Clone();
            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            Assert.That(pending.Store.Write(new[] { new StoreAct("a name", key, new[] { name }, null) }).Taken, Is.True);

            pending.Apply();

            var reading = live.Store.Read();
            Assert.That(StoreAuthorities.Served(reading), Is.Null, "the Recreate was left out");
            Assert.That(reading.InForce.Records.Any(record => record.Hex == name.Hex), Is.True, "the act with no ground was written");
        }

        [Test]
        public void A_single_act_whose_ground_went_writes_nothing_at_OK_and_is_no_refusal_of_the_guard()
        {
            var live = new FakeGateDatabase();
            StoreSeeds.Authority(live.Store, live.Values);
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Recreate().Succeeded, Is.True);
            Assert.That(new AuthorityService(live.Store, live.Values, NullGateLog.Instance).Delete().Succeeded, Is.True);
            var before = live.Records.Writes;

            pending.Apply();

            Assert.That(live.Records.Writes, Is.EqualTo(before), "the Recreate is not written");
            Assert.That(StoreAuthorities.Served(live.Store.Read()), Is.Null);
            Assert.That(pending.StoreRefusal, Is.Null, "a ground that went under the dialog is the log's to name, and no incident");
        }

        [Test]
        public void Cancel_drops_every_act_and_the_page_reads_the_store_as_it_is()
        {
            var live = new FakeGateDatabase();
            var pending = new PendingDatabase(live, NullGateLog.Instance);
            Assert.That(Service(pending).Create().Succeeded, Is.True);

            pending.Discard();

            Assert.That(pending.StoreWaiting, Is.False);
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
            Assert.That(pending.StoreWaiting, Is.False);
        }

        private static AuthorityService Service(PendingDatabase pending)
        {
            return new AuthorityService(pending.Store, pending.PluginCustomData, NullGateLog.Instance);
        }
    }
}
