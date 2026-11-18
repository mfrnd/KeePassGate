using System;
using System.Linq;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The plugin's store read out of real databases, `STORE.md` §620 and §621: the walk, the
    /// history as KeePass makes it, a save and a reload, and the hold.
    /// </summary>
    [TestFixture]
    public sealed partial class StoreInRealDatabaseTests
    {
        private static readonly byte[] One = StoreComposer.Key("one");
        private static readonly byte[] Two = StoreComposer.Key("two");

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwGroup _bin;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _group = Group(_database.RootGroup, "KeePassGate");
            _bin = Group(_database.RootGroup, "Recycle Bin");
            _database.Database.RecycleBinUuid = _bin.Uuid;
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_record_counts_in_the_plugin_s_group_in_another_group_in_the_bin_and_in_a_group_inside_it()
        {
            var inside = Group(_bin, "Deleted group");
            var revoked = Add(inside, Revoked(One, 'c'));
            Add(Group(_database.RootGroup, "Elsewhere"), Revoked(One, 'd'));
            Add(inside, Authority(One, 'a'));

            var reading = Readings(CurrentThread.Instance).Now();

            Assert.That(reading.Found(One, Two), Is.EqualTo(1), "the key is found in a group inside the bin");
            Assert.That(reading.Holds(revoked.Uuid.UuidBytes), Is.True);
        }

        [Test]
        public void A_key_whose_only_passing_version_is_the_oldest_history_item_is_found()
        {
            var authority = Add(_group, Authority(One, 'a'));
            Edit(authority, "{\"id\":\"edited once\"}");
            Edit(authority, "{\"id\":\"edited twice\"}");

            Assume.That(authority.History.UCount, Is.EqualTo(2u));
            Assert.That(Readings(CurrentThread.Instance).Now().Found(One, Two), Is.EqualTo(1));
        }

        [Test]
        public void A_string_replaced_with_the_entry_s_times_left_is_read_as_changed_at_the_next_reading()
        {
            var authority = Add(_group, Authority(One, 'a'));
            var readings = Readings(CurrentThread.Instance);
            Assume.That(readings.Now().Found(One, Two), Is.EqualTo(1));

            // As pykeepass changes a string: no history item, and the times as they were.
            var modified = authority.LastModificationTime;
            authority.Strings.Set(StoreComposer.Data, new ProtectedString(true, "{\"id\":\"changed\"}"));
            authority.LastModificationTime = modified;

            Assert.That(readings.Now().Found(One, Two), Is.EqualTo(0), "the kept form of the earlier object was not used");
        }

        [Test]
        public void A_second_question_inside_a_hold_walks_nothing_and_two_holds_walk_twice()
        {
            Add(_group, Authority(One, 'a'));
            var hold = new Stamped();
            var readings = Readings(hold);

            var inOne = hold.Hold(() => new[] { readings.Now(), readings.Now() });
            var inAnother = hold.Hold(readings.Now);

            Assert.That(inOne[1], Is.SameAs(inOne[0]), "the second question of a hold is answered from its reading");
            Assert.That(inAnother, Is.Not.SameAs(inOne[0]), "a reading is used by no other hold");
        }

        [Test]
        public void A_reading_on_KeePass_s_own_thread_is_never_kept()
        {
            Add(_group, Authority(One, 'a'));
            var readings = Readings(CurrentThread.Instance);

            Assert.That(readings.Now(), Is.Not.SameAs(readings.Now()));
        }

        [Test]
        public void A_target_let_go_answers_a_database_that_shut()
        {
            Add(_group, Authority(One, 'a'));
            using (var readings = Readings(CurrentThread.Instance))
            {
                readings.Now();

                readings.Dispose();

                Assert.Throws<DatabaseShut>(() => readings.Now());
            }
        }

        [Test]
        public void A_save_and_a_reload_read_the_same_and_use_nothing_kept_from_before()
        {
            Add(_group, Authority(One, 'a'));
            Add(_group, Authority(Two, 'b'));
            var readings = Readings(CurrentThread.Instance);
            Assume.That(readings.Now().Found(One, Two), Is.EqualTo(2));

            _database.LockAndUnlock();

            // The protected content of an authority, as a file loaded hands it over, is read.
            Assert.That(Readings(CurrentThread.Instance).Now().Found(One, Two), Is.EqualTo(2));
            Assert.That(readings.Now().Found(One, Two), Is.EqualTo(2), "read from the objects the reload made");
        }

        [Test]
        public void A_stop_that_lasts_is_logged_once_and_its_end_once()
        {
            var authority = Add(_group, Authority(One, 'a'));
            var log = new Lines();
            var readings = new StoreReadings(_database.Database, CurrentThread.Instance, log);

            for (var request = 0; request < 10; request++)
            {
                Assert.That(readings.Word, Is.EqualTo("stopped"), "an authority and no summary entry");
            }

            Add(_group, StoreComposer.Record(
                One, "head", "store", StoreComposer.Content(("after", string.Empty), ("height", "1")), 1,
                new[] { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")) }));
            Assert.That(readings.Word, Is.Null);
            Assert.That(readings.Word, Is.Null);

            Assert.That(log.Warnings, Has.Count.EqualTo(1));
            Assert.That(log.Warnings[0], Does.Contain("stopped").And.Not.Contain(authority.Strings.ReadSafe(StoreComposer.Data)));
            Assert.That(log.Infos, Has.Count.EqualTo(1));
        }

        [Test]
        public void A_store_past_half_its_room_is_named_in_the_log_once_while_it_stays_there()
        {
            Add(_group, Authority(One, 'a'));
            var head = Add(_group, HeadNaming(3971));
            var log = new Lines();
            var readings = new StoreReadings(_database.Database, CurrentThread.Instance, log);

            for (var request = 0; request < 10; request++)
            {
                Assert.That(readings.Word, Is.Null, "a store past half its room is served");
            }

            Assert.That(log.Warnings, Has.Count.EqualTo(1));
            Assert.That(log.Warnings[0], Does.Contain("past half its room").And.Contain("3972").And.Contain("262144"));
            Assert.That(log.Infos, Is.Empty);

            // Below half again, then past it again: named again.
            head.ParentGroup.Entries.Remove(head);
            Add(_group, HeadNaming(0));
            Assert.That(readings.Word, Is.Null);
            Add(_group, HeadNaming(3971));
            Assert.That(readings.Word, Is.Null);
            Assert.That(log.Warnings, Has.Count.EqualTo(2));
        }

        [Test]
        public void A_store_below_half_its_room_puts_nothing_in_the_log()
        {
            Add(_group, Authority(One, 'a'));
            Add(_group, HeadNaming(3970));
            var log = new Lines();
            var readings = new StoreReadings(_database.Database, CurrentThread.Instance, log);

            Assert.That(readings.Word, Is.Null);
            Assert.That(log.Warnings, Is.Empty);
            Assert.That(log.Infos, Is.Empty);
        }

        [Test]
        public void A_store_with_no_room_for_its_next_write_is_not_served_and_the_log_names_its_room()
        {
            Add(_group, Authority(One, 'a'));
            Add(_group, HeadNaming(7941));
            var log = new Lines();
            var readings = new StoreReadings(_database.Database, CurrentThread.Instance, log);

            Assert.That(readings.Word, Is.EqualTo("stopped"));
            Assert.That(readings.Word, Is.EqualTo("stopped"));

            Assert.That(log.Warnings, Has.Count.EqualTo(1));
            Assert.That(log.Warnings[0], Does.Contain("stopped").And.Contain("no room").And.Contain("7942"));
        }

        [Test]
        public void What_is_kept_goes_when_the_cache_lets_go_whoever_still_reads()
        {
            // A connection can hold a target after the cache has let go of it at a lock, a pairing
            // for its whole budget: the key the kept forms hold is overwritten then, and the
            // readings go on, keeping nothing.
            Add(_group, Authority(One, 'a'));
            var readings = Readings(CurrentThread.Instance);
            readings.Now();
            var keys = KeptKeys(readings);
            Assume.That(keys, Is.Not.Empty);

            readings.Forget();

            Assert.That(keys.SelectMany(key => key), Is.All.EqualTo(0), "the key kept is overwritten");
            Assert.That(readings.Now().Found(One, Two), Is.EqualTo(1), "the readings go on");
            Assert.That(KeptKeys(readings), Is.Empty, "and keep nothing");
        }

        [Test]
        public void A_reading_in_progress_does_not_hold_up_the_target_being_let_go()
        {
            // What KeePass's thread does at a lock: let the target go, which must not wait for a
            // reading a connection thread is still making, as one whose hold lapsed is. Each entry
            // carries the same large content, which the reading parses for each, so it lasts.
            Add(_group, Authority(One, 'a'));
            var large = new ProtectedString(false, "{\"about\":\"" + new string('x', 900000) + "\"}");
            for (var entry = 0; entry < 150; entry++)
            {
                var carrying = new PwEntry(true, true);
                carrying.Strings.Set(StoreComposer.Kind, new ProtectedString(false, "future"));
                carrying.Strings.Set(StoreComposer.Data, large);
                carrying.Strings.Set(StoreComposer.Check, new ProtectedString(false, new string('0', 64)));
                _group.AddEntry(carrying, true);
            }

            using (var readings = Readings(CurrentThread.Instance))
            using (var started = new System.Threading.ManualResetEventSlim())
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var readingEnded = TimeSpan.Zero;
                var reading = new System.Threading.Thread(() =>
                {
                    started.Set();
                    Thrown.By(() => readings.Now());
                    readingEnded = clock.Elapsed;
                });
                reading.Start();
                started.Wait();
                System.Threading.Thread.Sleep(50);

                readings.Dispose();
                var letGo = clock.Elapsed;
                reading.Join();

                TestContext.Out.WriteLine("figure: let go at " + letGo.TotalMilliseconds + " ms, the reading ended at " + readingEnded.TotalMilliseconds + " ms");
                Assert.That(letGo, Is.LessThan(readingEnded), "letting go waited for the reading");
                Assert.Throws<DatabaseShut>(() => readings.Now());
            }
        }

        [Test]
        public void A_change_reads_the_store_once_for_its_turn_however_often_it_is_asked()
        {
            // A change's turn on KeePass's thread, where every question used to read afresh: the
            // lookup of its node, and the client read again inside the rules, `ClientCredentials`,
            // which asks whether the store holds the gate back.
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _database.RootGroup.AddEntry(entry, true);
            Add(_group, Authority(One, 'a'));

            var hold = new Counted();
            var readings = new StoreReadings(_database.Database, hold, null);
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, null),
                TestTags.Fixed,
                hold,
                null,
                readings);
            var asking = new KeePassGate.Vault.VaultAsking(
                found =>
                {
                    Assert.That(readings.Word, Is.EqualTo("stopped"));
                    return true;
                },
                null,
                null,
                false);

            var written = vault.UpdateEntry(
                entry.Uuid.ToHexString(),
                new System.Collections.Generic.Dictionary<string, string> { { PwDefs.TitleField, "renamed" } },
                asking);

            Assert.That(written.Ok, Is.True, written.Reason);
            Assert.That(hold.Readings, Is.EqualTo(1), "the store was read once for the turn");
        }

        /// <summary>The bytes of every key the readings keep, read off their kept forms.</summary>
        private static System.Collections.Generic.List<byte[]> KeptKeys(StoreReadings readings)
        {
            const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var kept = typeof(StoreReadings).GetField("_kept", Private).GetValue(readings);
            var forms = (System.Collections.IDictionary)kept.GetType().GetField("_forms", Private).GetValue(kept);
            var keys = new System.Collections.Generic.List<byte[]>();
            foreach (System.Collections.IEnumerable list in forms.Values)
            {
                foreach (KeePassGate.Store.StoreChecked form in list)
                {
                    if (form.StoreKey != null)
                    {
                        keys.Add(form.StoreKey.Bytes);
                    }
                }
            }

            return keys;
        }

        private StoreReadings Readings(IReadHold hold)
        {
            return new StoreReadings(_database.Database, hold, null);
        }

        private static PwEntry Add(PwGroup group, StoreComposer.Composed record)
        {
            var entry = StoreEntries.Of(record);
            group.AddEntry(entry, true);
            return entry;
        }

        /// <summary>An edit of the content in KeePass's entry dialog: the version before into the history.</summary>
        private void Edit(PwEntry entry, string content)
        {
            entry.CreateBackup(_database.Database);
            entry.Strings.Set(StoreComposer.Data, new ProtectedString(true, content));
            entry.Touch(true, false);
        }

        private static StoreComposer.Composed Authority(byte[] key, char id)
        {
            return StoreComposer.Record(
                key, "authority", "authority:" + Hex(id),
                StoreComposer.Content(("id", Hex(id)), ("storeKey", StoreComposer.HexOf(key))));
        }

        /// <summary>
        /// A summary entry of height 1 naming this many summary entries the file does not hold,
        /// so that the next write names one more.
        /// </summary>
        private static StoreComposer.Composed HeadNaming(int count)
        {
            var after = string.Join(
                " ",
                Enumerable.Range(0, count).Select(index => index.ToString("x32", System.Globalization.CultureInfo.InvariantCulture)));
            return StoreComposer.Record(
                One, "head", "store", StoreComposer.Content(("after", after), ("height", "1")), 1,
                new[] { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")) });
        }

        private static StoreComposer.Composed Revoked(byte[] key, char client)
        {
            return StoreComposer.Record(key, "revoked", "client:" + Hex(client), StoreComposer.Content(("client", Hex(client))));
        }

        private static string Hex(char digit)
        {
            return new string(digit, 64);
        }

        private static PwGroup Group(PwGroup parent, string name)
        {
            var group = new PwGroup(true, true, name, PwIcon.Folder);
            parent.AddGroup(group, true);
            return group;
        }

        private sealed class Lines : KeePassGate.Diagnostics.IGateLog
        {
            public System.Collections.Generic.List<string> Warnings { get; } = new System.Collections.Generic.List<string>();

            public System.Collections.Generic.List<string> Infos { get; } = new System.Collections.Generic.List<string>();

            public void Info(string message)
            {
                Infos.Add(message);
            }

            public void Warn(string message)
            {
                Warnings.Add(message);
            }

            public void Error(string message, Exception error)
            {
                Warnings.Add(message);
            }

            public void Unexpected(string message, Exception error)
            {
                Warnings.Add(message);
            }
        }
        /// <summary>
        /// KeePass's own thread, which every question reads afresh on: counting each time a
        /// reading asks which hold it is in, which is once for each reading made.
        /// </summary>
        private sealed class Counted : IReadHold
        {
            public int Readings { get; private set; }

            public long Stamp
            {
                get
                {
                    Readings++;
                    return 0;
                }
            }

            public T Hold<T>(Func<T> read)
            {
                return read();
            }
        }

        /// <summary>
        /// A hold with a stamp of its own for every outermost hold, as a connection's holds have,
        /// run where it is asked.
        /// </summary>
        private sealed class Stamped : IReadHold
        {
            private long _given;

            public long Stamp { get; private set; }

            public T Hold<T>(Func<T> read)
            {
                if (Stamp != 0)
                {
                    return read();
                }

                Stamp = ++_given;
                try
                {
                    return read();
                }
                finally
                {
                    Stamp = 0;
                }
            }
        }
    }
}
