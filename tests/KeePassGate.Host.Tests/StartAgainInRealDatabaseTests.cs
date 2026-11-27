using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Starting the store again in a real database, `STORE.md` §632: a whole, a stopped, a
    /// no-room, a split and an outdated store made with KeePass's own types, the start pressed on
    /// the tab's buffer and written by its OK through the real writer, the new store served under
    /// its new key, the earlier group and its entries left in place, and what the start wrote kept
    /// by a save.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class StartAgainInRealDatabaseTests
    {
        private TemporaryDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            var created = DatabaseAuthority.Of(_database.Database, null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [TestCase("whole")]
        [TestCase("stopped")]
        [TestCase("no room")]
        [TestCase("split")]
        [TestCase("outdated")]
        public void A_store_started_again_at_OK_serves_under_a_new_key_beside_the_earlier_entries_and_is_kept_by_a_save(string state)
        {
            StoreSeeds.Revoke(Seeds(), new string('c', 64));
            string earlier;
            string earlierAuthority;
            using (var reading = Seeds().Read())
            {
                earlier = reading.InForce.Key.Name;
                earlierAuthority = StoreAuthorities.Served(reading);
            }

            Hold(state);
            var entries = Entries().ToList();
            var deletions = _database.Database.DeletedObjects.UCount;
            var groups = Groups().Count;

            StartAtOk();

            Assert.That(Entries().Select(entry => entry.Uuid.ToHexString()), Is.SupersetOf(entries.Select(entry => entry.Uuid.ToHexString())), "nothing deleted");
            Assert.That(_database.Database.DeletedObjects.UCount, Is.EqualTo(deletions), "no deletion record");
            Assert.That(Groups(), Has.Count.EqualTo(groups + 1), "a group of its own, beside the earlier group");
            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.Standing, Has.Count.EqualTo(1));
                Assert.That(reading.InForce.Key.Name, Is.Not.EqualTo(earlier), "a new key stands");
                Assert.That(reading.Found.Select(key => key.Name), Does.Contain(earlier), "the earlier key found, and set aside");
                Assert.That(StoreAuthorities.Served(reading), Is.Not.Null.And.Not.EqualTo(earlierAuthority), "the new authority served");
                Assert.That(reading.InForce.Taken(StoreKinds.Revoked), Is.Empty, "nothing of the earlier store counts");
                Assert.That(reading.Room.HasRoom, Is.True);
            }
        }

        [Test]
        public void The_new_authority_s_entry_deleted_for_good_leaves_the_earlier_store_stopped_and_the_seal_keeps_it()
        {
            // `measured-started-again` T11 and T13, over a real database: the stop holds the
            // earlier store, and Seal again brings it back with its own authority.
            string earlierAuthority;
            using (var reading = Seeds().Read())
            {
                earlierAuthority = StoreAuthorities.Served(reading);
            }

            StartAtOk();
            _database.Save();
            string started;
            using (var reading = Seeds().Read())
            {
                started = StoreAuthorities.Served(reading);
            }

            var carrier = Entries().Single(entry => entry.Strings.ReadSafe(StoreComposer.Kind) == "authority" && Data(entry).Contains("\"" + started + "\""));
            carrier.ParentGroup.Entries.Remove(carrier);
            _database.Database.DeletedObjects.Add(new PwDeletedObject(carrier.Uuid, DateTime.UtcNow.AddSeconds(1)));
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Stopped), reading.Reason);
                Assert.That(StoreLosses.Of(reading).SetAside, Has.Count.EqualTo(1), "the stop listed apart");
            }

            var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            var pending = new PendingDatabase(new OpenDatabase(_database.Database, delegate { }, writer, NullGateLog.Instance), NullGateLog.Instance);
            Assert.That(new AuthorityService(pending.Store, NullGateLog.Instance).Seal().Succeeded, Is.True);
            Assert.That(pending.Apply(), Is.True);

            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(earlierAuthority), "the earlier store kept");
            }
        }

        /// <summary>The store held in a state a start applies to.</summary>
        private void Hold(string state)
        {
            byte[] key;
            string head;
            using (var reading = Seeds().Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
                head = reading.InForce.HeadsInForce.Single().Hex;
            }

            try
            {
                switch (state)
                {
                    case "whole":
                        break;
                    case "stopped":
                        DeleteForGood(head);
                        break;
                    case "no room":
                        // A summary entry with as many items as one has names for, over the one in
                        // force, so that the next write has no room.
                        var items = Enumerable.Range(0, StoreFormat.MaxItems - 1)
                            .Select(index => StoreComposer.Item(("client", index.ToString("x64", CultureInfo.InvariantCulture)), ("kind", "revoked")))
                            .Concat(new[] { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", GateVersion.Current)) });
                        Put(StoreComposer.Record(key, "head", "store", StoreComposer.Content(("after", head), ("height", "9")), 9, items));
                        DeleteForGood(head);
                        break;
                    case "split":
                        var second = StoreFormat.NewKey();
                        Put(StoreComposer.Record(
                            second, "authority", "authority:" + new string('b', 64),
                            StoreComposer.Content(("id", new string('b', 64)), ("storeKey", StoreComposer.HexOf(second)))));
                        break;
                    case "outdated":
                        Put(StoreComposer.Record(key, "later", "later", StoreComposer.Content(("held", "by a later plugin"))));
                        break;
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }

            using (var reading = Seeds().Read())
            {
                var expected = state == "whole" ? StoreState.Whole
                    : state == "split" ? StoreState.Split
                    : state == "outdated" ? StoreState.Outdated
                    : StoreState.Stopped;
                Assume.That(reading.State, Is.EqualTo(expected), reading.Reason);
                Assume.That(state != "no room" || !reading.Room.HasRoom, "no room");
            }
        }

        /// <summary>Start store again pressed on the tab's buffer over the live database, and the dialog's OK.</summary>
        private void StartAtOk()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            var pending = new PendingDatabase(new OpenDatabase(_database.Database, delegate { }, writer, NullGateLog.Instance), NullGateLog.Instance);
            var pressed = new AuthorityService(pending.Store, NullGateLog.Instance).StartAgain();
            Assert.That(pressed.Succeeded, Is.True, pressed.Message);
            Assert.That(pending.Apply(), Is.True, "OK wrote the start");
            Assert.That(pending.Started, Is.Not.Null);
        }

        private IGateStore Seeds()
        {
            return DatabaseAuthority.Store(_database.Database);
        }

        private IEnumerable<PwEntry> Entries()
        {
            return _database.RootGroup.GetEntries(true).Where(entry => entry.Strings.ReadSafe(StoreComposer.Kind).Length > 0).ToList();
        }

        private List<PwGroup> Groups()
        {
            return _database.RootGroup.GetGroups(true).Where(group => group.Name == StoreFormat.GroupName).ToList();
        }

        private static string Data(PwEntry entry)
        {
            return entry.Strings.ReadSafe(StoreComposer.Data);
        }

        private void Put(StoreComposer.Composed record)
        {
            _database.RootGroup.AddEntry(StoreEntries.Of(record), true);
        }

        /// <summary>An entry deleted for good, with a deletion record dated a second after the clock.</summary>
        private void DeleteForGood(string hex)
        {
            var entry = _database.RootGroup.GetEntries(true).Single(found => found.Uuid.ToHexString().Equals(hex, StringComparison.OrdinalIgnoreCase));
            entry.ParentGroup.Entries.Remove(entry);
            _database.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow.AddSeconds(1)));
        }
    }
}
