using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Bridge;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What a real database's store reads as after what KeePass does to it: an edit as KeePass
    /// makes one, a deletion with its deletion record, and a merge by each of KeePass's methods,
    /// `STORE.md` §622 to §624 and the rows of §639 to §642 the store's core holds alone.
    /// </summary>
    [TestFixture]
    public sealed class StoreStatesInRealDatabaseTests
    {
        private static readonly byte[] One = StoreComposer.Key("one");
        private static readonly byte[] Two = StoreComposer.Key("two");

        /// <summary>Every method of KeePass's merge that keeps an entry's identifier.</summary>
        private static readonly PwMergeMethod[] Keeping =
        {
            PwMergeMethod.Synchronize, PwMergeMethod.OverwriteExisting, PwMergeMethod.KeepExisting,
            PwMergeMethod.OverwriteIfNewer,
        };

        private readonly List<TemporaryDatabase> _databases = new List<TemporaryDatabase>();

        [TearDown]
        public void TearDown()
        {
            foreach (var database in _databases)
            {
                database.Dispose();
            }

            _databases.Clear();
        }

        // The history rule, with the history KeePass makes, `STORE.md` §622.

        [Test]
        public void An_edit_of_a_record_s_content_reads_by_the_version_KeePass_kept()
        {
            var store = Store(One);
            var revoked = store.Decision("revoked");
            Edit(store, revoked, "{\"client\":\"edited\"}");

            Assert.That(State(store), Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void A_record_whose_content_string_was_removed_reads_by_the_version_KeePass_kept()
        {
            var store = Store(One);
            var revoked = store.Decision("revoked");
            revoked.CreateBackup(store.Database);
            revoked.Strings.Remove(StoreComposer.Data);

            Assert.That(State(store), Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void Ten_edits_keep_the_written_version_and_the_eleventh_stops_the_store()
        {
            var store = Store(One);
            Assume.That(store.Database.HistoryMaxItems, Is.EqualTo(10));
            var revoked = store.Decision("revoked");

            Edit(store, revoked, "{\"client\":\"edited\"}");
            for (var edit = 2; edit <= 10; edit++)
            {
                QuickEdit(store, revoked);
            }

            Assert.That(State(store), Is.EqualTo(StoreState.Whole), "the tenth edit");

            QuickEdit(store, revoked);
            Assert.That(State(store), Is.EqualTo(StoreState.Stopped), "the eleventh edit");
        }

        [Test]
        public void A_quick_edit_and_a_move_leave_the_record_passing_in_its_current_version()
        {
            var store = Store(One);
            var revoked = store.Decision("revoked");
            QuickEdit(store, revoked);
            var elsewhere = new PwGroup(true, true, "Elsewhere", PwIcon.Folder);
            store.RootGroup.AddGroup(elsewhere, true);
            revoked.ParentGroup.Entries.Remove(revoked);
            elsewhere.AddEntry(revoked, true);

            Assert.That(State(store), Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void Where_the_database_keeps_no_history_the_first_edit_stops_the_store()
        {
            var store = Store(One);
            store.Database.HistoryMaxItems = 0;
            var revoked = store.Decision("revoked");
            Edit(store, revoked, "{\"client\":\"edited\"}");

            Assert.That(State(store), Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void The_authority_s_entry_edited_past_the_limit_leaves_no_key_and_no_store()
        {
            var store = Store(One);
            store.Decision("revoked");
            Edit(store, store.Authority, "{\"id\":\"edited\"}");
            for (var edit = 2; edit <= 11; edit++)
            {
                QuickEdit(store, store.Authority);
            }

            Assert.That(State(store), Is.EqualTo(StoreState.None));
        }

        // Deletion records, `STORE.md` §623 and §624.

        [Test]
        public void A_summary_entry_deleted_for_good_stops_the_store()
        {
            var store = Store(One);
            DeleteForGood(store, store.Head);

            Assert.That(State(store), Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void A_revocation_deleted_for_good_and_restated_and_an_ordinary_entry_s_deletion_change_nothing()
        {
            var store = Store(One);
            var revoked = store.Decision("revoked");
            DeleteForGood(store, revoked);
            var ordinary = new PwEntry(true, true);
            store.RootGroup.AddEntry(ordinary, true);
            DeleteForGood(store, ordinary);

            Assert.That(State(store), Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void An_entry_of_the_store_in_the_recycle_bin_still_counts()
        {
            // Row 11 of `STORE.md` §639.
            var store = Store(One);
            var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
            store.RootGroup.AddGroup(bin, true);
            store.Database.RecycleBinUuid = bin.Uuid;
            var head = store.Head;
            head.ParentGroup.Entries.Remove(head);
            bin.AddEntry(head, true);

            Assert.That(State(store), Is.EqualTo(StoreState.Whole));
        }

        // Merges, by `PwDatabase.MergeIn`.

        [Test]
        public void A_summary_entry_deleted_in_a_copy_stops_both_once_synchronized()
        {
            // Row 17 of `STORE.md` §642.
            var gate = Store(One);
            var copy = CopyOf(gate);
            DeleteForGood(copy, copy.Head);

            gate.Database.MergeIn(copy.Database, PwMergeMethod.Synchronize);
            copy.Database.MergeIn(gate.Database, PwMergeMethod.Synchronize);

            Assert.That(State(gate), Is.EqualTo(StoreState.Stopped));
            Assert.That(State(copy), Is.EqualTo(StoreState.Stopped));
        }

        [Test]
        public void An_import_with_new_identifiers_leaves_a_stale_copy_as_it_was()
        {
            // Row 6 of `STORE.md` §640: what arrives are copies, and copies are passed over.
            var newer = Store(One);
            var stale = CopyOf(newer);
            newer.Add(StoreComposer.Record(One, "future", "x", StoreComposer.Content(("about", "x"))));
            Assume.That(State(newer), Is.EqualTo(StoreState.Outdated));

            stale.Database.MergeIn(newer.Database, PwMergeMethod.CreateNewUuids);

            Assert.That(State(stale), Is.EqualTo(StoreState.Whole));
        }

        [Test]
        public void Where_only_copies_of_the_store_s_entries_are_left_no_key_is_found()
        {
            // Row 18 of `STORE.md` §642.
            var source = Store(One);
            var target = Empty();
            target.Database.MergeIn(source.Database, PwMergeMethod.CreateNewUuids);

            Assert.That(State(target), Is.EqualTo(StoreState.None));
        }

        [Test]
        public void A_file_that_brings_a_store_key_of_its_own_splits_the_store_by_every_method_that_keeps_identifiers()
        {
            foreach (var method in Keeping)
            {
                var gate = Store(One);
                var outsider = Store(Two);

                gate.Database.MergeIn(outsider.Database, method);

                Assert.That(State(gate), Is.EqualTo(StoreState.Split), method.ToString());
            }
        }

        [Test]
        public void Copies_that_keep_different_stores_are_split_once_they_meet()
        {
            var gate = Store(One);
            gate.Add(Authority(Two, 'b'));
            var copy = CopyOf(gate);
            gate.Restate(gate.Kept(One, Two));
            copy.Restate(copy.Kept(Two, One), Two);
            Assume.That(State(gate), Is.EqualTo(StoreState.Whole));

            gate.Database.MergeIn(copy.Database, PwMergeMethod.Synchronize);

            Assert.That(State(gate), Is.EqualTo(StoreState.Split));
        }

        [Test]
        public void A_store_raised_above_the_plugin_is_outdated_also_by_the_deletion_record_of_its_version_record()
        {
            var raised = Store(One);
            var record = raised.Add(Format(One, StoreFormat.Number + 1));
            Assert.That(State(raised), Is.EqualTo(StoreState.Outdated));

            DeleteForGood(raised, record);
            Assert.That(State(raised), Is.EqualTo(StoreState.Outdated), "the deletion record tells of it");

            var gate = Store(One);
            gate.Database.MergeIn(raised.Database, PwMergeMethod.Synchronize);
            Assert.That(State(gate), Is.EqualTo(StoreState.Outdated), "and a synchronize carries it");
        }

        [Test]
        public void A_record_of_a_kind_the_plugin_does_not_know_stops_it_with_no_version_record_anywhere()
        {
            // "The store's format number is lost in every carrier", `STORE.md` §639: the record of
            // an unknown kind still holds the plugin back.
            var store = Store(One);
            store.Add(StoreComposer.Record(One, "future", "x", StoreComposer.Content(("about", "x"))));

            Assert.That(State(store), Is.EqualTo(StoreState.Outdated));
        }

        private ComposedStore Store(byte[] key)
        {
            var database = TemporaryDatabase.New();
            _databases.Add(database);
            return new ComposedStore(database.Database, key);
        }

        private TemporaryDatabase Empty()
        {
            var database = TemporaryDatabase.New();
            _databases.Add(database);
            return database;
        }

        /// <summary>A second database holding the same entries under the same identifiers.</summary>
        private ComposedStore CopyOf(ComposedStore store)
        {
            var copy = Empty();
            copy.Database.MergeIn(store.Database, PwMergeMethod.Synchronize);
            return new ComposedStore(copy.Database, store);
        }

        private static StoreState State(ComposedStore store)
        {
            return new StoreReadings(store.Database, CurrentThread.Instance, null).Now().State;
        }

        private static StoreState State(TemporaryDatabase database)
        {
            return new StoreReadings(database.Database, CurrentThread.Instance, null).Now().State;
        }

        /// <summary>An edit in KeePass's entry dialog: the version before into the history, kept to the limit.</summary>
        private static void Edit(ComposedStore store, PwEntry entry, string content)
        {
            entry.CreateBackup(store.Database);
            entry.Strings.Set(StoreComposer.Data, new ProtectedString(true, content));
            entry.Touch(true, false);
        }

        /// <summary>A quick edit, such as expiring the entry now, which backs it up as well.</summary>
        private static void QuickEdit(ComposedStore store, PwEntry entry)
        {
            entry.CreateBackup(store.Database);
            entry.Expires = !entry.Expires;
            entry.Touch(true, false);
        }

        /// <summary>A deletion for good, which leaves a deletion record, as KeePass's delete without the bin does.</summary>
        private static void DeleteForGood(ComposedStore store, PwEntry entry)
        {
            entry.ParentGroup.Entries.Remove(entry);
            // A second after now, as a deletion made after the entry was written is: a merge
            // applies a deletion record only to an entry last changed before it.
            store.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow.AddSeconds(1)));
        }

        private static StoreComposer.Composed Authority(byte[] key, char id)
        {
            return StoreComposer.Record(
                key, "authority", "authority:" + new string(id, 64),
                StoreComposer.Content(("id", new string(id, 64)), ("storeKey", StoreComposer.HexOf(key))));
        }

        private static StoreComposer.Composed Format(byte[] key, int number)
        {
            var text = number.ToString(CultureInfo.InvariantCulture);
            return StoreComposer.Record(
                key, "format", "format:" + text, StoreComposer.Content(("format", text), ("plugin", "0.3.0")), number);
        }

        /// <summary>
        /// A store in a real database: an authority and a summary entry in a group of the
        /// plugin's, to which a test adds decisions and restates them as a write would.
        /// </summary>
        private sealed class ComposedStore
        {
            private readonly byte[] _key;
            private readonly List<string> _items = new List<string>();
            private int _height = 1;

            public ComposedStore(PwDatabase database, byte[] key)
            {
                Database = database;
                _key = key;
                Group = new PwGroup(true, true, "KeePassGate", PwIcon.TerminalEncrypted);
                database.RootGroup.AddGroup(Group, true);
                Authority = Add(StoreStatesInRealDatabaseTests.Authority(key, 'a'));
                Head = AddEntry(HeadOf(string.Empty));
            }

            /// <summary>The same store, as a copy of it holds it.</summary>
            public ComposedStore(PwDatabase database, ComposedStore original)
            {
                Database = database;
                _key = original._key;
                _items.AddRange(original._items);
                _height = original._height;
                Group = database.RootGroup.FindGroup(original.Group.Uuid, true);
                Authority = database.RootGroup.FindEntry(original.Authority.Uuid, true);
                Head = database.RootGroup.FindEntry(original.Head.Uuid, true);
            }

            public PwDatabase Database { get; private set; }

            public PwGroup RootGroup
            {
                get { return Database.RootGroup; }
            }

            public PwGroup Group { get; private set; }

            public PwEntry Authority { get; private set; }

            public PwEntry Head { get; private set; }

            public PwEntry Add(StoreComposer.Composed record)
            {
                return AddEntry(record);
            }

            /// <summary>A revocation and the summary entry that restates it, as a write leaves them.</summary>
            public PwEntry Decision(string kind)
            {
                var client = new string('c', 64);
                var record = AddEntry(StoreComposer.Record(_key, kind, "client:" + client, StoreComposer.Content(("client", client))));
                Restate(StoreComposer.Item(("client", client), ("kind", kind)));
                return record;
            }

            public string Kept(byte[] under, byte[] notKept)
            {
                var record = StoreComposer.Record(
                    under, "kept", "key:" + StoreComposer.KeyName(notKept),
                    StoreComposer.Content(("after", string.Empty), ("key", StoreComposer.KeyName(notKept))));
                AddEntry(record);
                return StoreComposer.Item(("after", string.Empty), ("id", record.Hex), ("key", StoreComposer.KeyName(notKept)), ("kind", "kept"));
            }

            /// <summary>
            /// A new summary entry over every item restated so far and this one, replacing the one
            /// in force: deleted with a deletion record and named, as a write of the plugin's own.
            /// </summary>
            public void Restate(string item, byte[] key = null)
            {
                _items.Add(item);
                var replaced = Head;
                _height++;
                Head = AddEntry(HeadOf(replaced.Uuid.ToHexString().ToLowerInvariant(), key ?? _key));
                DeleteForGood(this, replaced);
            }

            private StoreComposer.Composed HeadOf(string after, byte[] key = null)
            {
                return StoreComposer.Record(
                    key ?? _key, "head", "store",
                    StoreComposer.Content(("after", after), ("height", _height.ToString(CultureInfo.InvariantCulture))),
                    _height,
                    new[] { StoreComposer.Item(("format", "1"), ("kind", "format"), ("plugin", "0.3.0")) }.Concat(_items));
            }

            private PwEntry AddEntry(StoreComposer.Composed record)
            {
                var entry = StoreEntries.Of(record);
                Group.AddEntry(entry, true);
                return entry;
            }
        }
    }
}
