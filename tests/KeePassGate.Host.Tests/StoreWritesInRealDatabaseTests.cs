using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;
using Application = KeePassGate.Bridge.StoreEntries;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Writes of the plugin's own into the store of a real database, `STORE.md` §606, §607, §627
    /// and §628: the entries KeePass keeps, the group written into, the deletions and their
    /// records, a record set back, a save and a reload, and copies brought together.
    /// </summary>
    [TestFixture]
    public sealed class StoreWritesInRealDatabaseTests
    {
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

        [Test]
        public void An_authority_is_an_entry_as_KeePass_keeps_one_with_its_content_protected_and_marked()
        {
            var database = Database();
            var id = StoreSeeds.Authority(Store(database));

            var group = database.RootGroup.Groups.Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
            Assert.That(group.Name, Is.EqualTo("KeePassGate"));
            Assert.That(group.Notes, Is.EqualTo("KeePassGate data."));
            Assert.That(group.IconId, Is.EqualTo(PwIcon.TerminalEncrypted));
            Assert.That(group.EnableSearching, Is.False);
            Assert.That(group.EnableAutoType, Is.False);

            var authority = group.Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");
            Assert.That(authority.Strings.Get("KPG.Data").IsProtected, Is.True);
            Assert.That(authority.Strings.Get("KPG.Data").ReadString(), Does.Contain(id));
            Assert.That(authority.Strings.ReadSafe(PwDefs.TitleField), Does.EndWith(" (do not delete)"));
            Assert.That(authority.Strings.ReadSafe(PwDefs.NotesField), Does.StartWith("Do not delete this entry."));
            Assert.That(authority.Strings.Get(PwDefs.PasswordField).IsProtected, Is.True);
            Assert.That(authority.Strings.ReadSafe(PwDefs.UserNameField), Is.Empty);
            Assert.That(authority.AutoType.Enabled, Is.False);

            var head = group.Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "head");
            Assert.That(head.Strings.Get("KPG.Data").IsProtected, Is.False);
            Assert.That(database.Database.Modified, Is.True, "marked, and not saved");
        }

        [Test]
        public void An_entry_with_every_string_of_the_store_gone_stays_where_it_is_and_is_told_of_as_deleted_to_nobody()
        {
            // `STORE.md` §627: present is every entry of the tree. A summary entry that a later
            // summary entry names, back in the tree with every string of the store gone and its
            // deletion record lost, is neither deleted by the next write nor named by a deletion
            // record of its own, which a merge would take as the entry deleted.
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var first = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "head").Uuid;
            Assert.That(NameWritten(database), Is.True);

            var stripped = new PwEntry(false, false) { Uuid = first };
            PluginGroup(database).AddEntry(stripped, true);
            var told = database.Database.DeletedObjects.Single(deleted => deleted.Uuid.Equals(first));
            database.Database.DeletedObjects.Remove(told);
            Assume.That(Store(database).Read().State, Is.EqualTo(StoreState.Whole));

            Assert.That(NameWritten(database), Is.True);

            Assert.That(database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(first)), Is.False);
            Assert.That(database.Database.RootGroup.FindEntry(first, true), Is.SameAs(stripped));
        }

        [Test]
        public void A_second_group_with_the_prefix_that_holds_no_record_is_not_written_into()
        {
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var group = PluginGroup(database);
            var stranger = new PwGroup(true, true, "KeePassGate", PwIcon.Folder)
            {
                Uuid = new PwUuid(StoreFormat.NewGroupIdentifier()),
            };
            database.RootGroup.AddGroup(stranger, true);

            Assert.That(Service(database).Recreate().Succeeded, Is.True);

            Assert.That(stranger.Entries.UCount, Is.EqualTo(0u));
            Assert.That(group.Entries.Count(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority"), Is.EqualTo(1));
        }

        [Test]
        public void A_store_whose_group_was_deleted_into_the_bin_gets_a_new_group_and_every_record_in_the_bin_counts()
        {
            var database = Database();
            var first = StoreSeeds.Authority(Store(database));
            var bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
            database.RootGroup.AddGroup(bin, true);
            database.Database.RecycleBinEnabled = true;
            database.Database.RecycleBinUuid = bin.Uuid;
            var group = PluginGroup(database);
            database.RootGroup.Groups.Remove(group);
            bin.AddGroup(group, true);

            Assert.That(Service(database).Recreate().Succeeded, Is.True);

            var groups = database.RootGroup.GetGroups(true).Where(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes)).ToList();
            Assert.That(groups, Has.Count.EqualTo(2), "a group of the plugin's own made outside the bin");
            var made = groups.Single(found => found != group);
            Assert.That(made.ParentGroup, Is.SameAs(database.RootGroup));
            Assert.That(made.Entries.UCount, Is.GreaterThan(0u), "what the write added went there");
            var reading = Store(database).Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.InForce.Taken("retired"), Is.EquivalentTo(new[] { first }), "the retirement written beside the records in the bin");
        }

        [Test]
        public void A_record_whose_title_and_notes_were_changed_reads_as_it_did()
        {
            // `STORE.md` §607: no text is covered by the check or read by the plugin.
            var database = Database();
            var id = StoreSeeds.Authority(Store(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");

            authority.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "renamed by hand"));
            authority.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "notes of the operator's own"));

            using (var reading = Store(database).Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.ByHistory, Is.EqualTo(0));
                Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(id));
            }
        }

        [TestCase("Delete")]
        [TestCase("keeping an authority")]
        public void A_retirement_holding_the_store_key_is_an_entry_with_its_content_protected_and_marked(string command)
        {
            // `STORE.md` §607 and §628 (S18): every record whose content holds the store key, a
            // retirement among them, is protected and marked, and not the authority's alone.
            var database = Database();
            if (command == "Delete")
            {
                StoreSeeds.Authority(Store(database));
                Assert.That(Service(database).Delete().Succeeded, Is.True);
            }
            else
            {
                var both = StoreSeeds.Undecided(Store(database));
                Assert.That(Service(database).Keep(both[0], both).Succeeded, Is.True);
            }

            var retirement = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "retired");
            Assert.That(retirement.Strings.ReadSafe("KPG.Data"), Does.Contain("\"storeKey\""));
            Assert.That(retirement.Strings.Get("KPG.Data").IsProtected, Is.True);
            Assert.That(retirement.Strings.ReadSafe(PwDefs.TitleField), Does.EndWith(" (do not delete)"));
            Assert.That(retirement.Strings.ReadSafe(PwDefs.NotesField), Does.StartWith("Do not delete this entry."));
        }

        [TestCase(2, 1)]
        [TestCase(1, 1)]
        public void A_write_goes_into_the_group_holding_the_most_records_and_the_lowest_identifier_among_equals(int moved, int left)
        {
            // `STORE.md` §606 (S11). Create leaves a version record, an authority and a summary
            // entry in a group of the plugin's own; some are moved into a second such group, and
            // the rest stay or go to the root, which no write is put into.
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var first = PluginGroup(database);
            var second = new PwGroup(true, true, StoreFormat.GroupName, PwIcon.TerminalEncrypted)
            {
                Uuid = new PwUuid(StoreFormat.NewGroupIdentifier()),
            };
            database.RootGroup.AddGroup(second, true);

            var entries = first.Entries.OrderBy(entry => entry.Strings.ReadSafe("KPG.Kind"), System.StringComparer.Ordinal).ToList();
            foreach (var entry in entries.Take(moved))
            {
                first.Entries.Remove(entry);
                second.AddEntry(entry, true);
            }

            foreach (var entry in entries.Skip(moved + left))
            {
                first.Entries.Remove(entry);
                database.RootGroup.AddEntry(entry, true);
            }

            var expected = moved > left
                ? second
                : string.CompareOrdinal(KeePassGate.Hex.Of(first.Uuid.UuidBytes), KeePassGate.Hex.Of(second.Uuid.UuidBytes)) < 0 ? first : second;

            Assert.That(NameWritten(database), Is.True);

            var name = database.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "profile");
            Assert.That(name.ParentGroup, Is.SameAs(expected));
        }

        [Test]
        public void A_write_applied_to_the_database_reads_as_the_write_applied_in_memory_reads()
        {
            // `PendingRecords` shows the page what `StoreWrite.Applied` makes of a write, and OK
            // writes what `StoreEntries.Apply` makes of it: the two are read alike, a record set
            // back by its history and a retired authority's entry deleted among them.
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var format = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "format");
            Edit(database, format, "{\"format\":\"1\"}", false);

            StoreWrite write;
            KeyValuePair<IReadOnlyList<StoreCandidate>, IReadOnlyList<byte[]>> applied;
            using (var reading = Store(database).Read())
            {
                Assume.That(reading.ByHistory, Is.EqualTo(1));
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                var retirement = StoreRecord.Compose(key, "retired", new Dictionary<string, string>
                {
                    { "authority", reading.Answering.Single().Form.Member("id") },
                    { "storeKey", KeePassGate.Hex.Of(key) },
                });
                write = StoreWrite.Plan(reading, new[] { new StoreAct("Delete", key, StoreFormat.KeyName(key), new[] { retirement }, null) }, GateVersion.Current);
                Assume.That(write.Refused, Is.Null);
                Assume.That(write.SetBack, Has.Count.EqualTo(1), "the version record set back");
                applied = write.Applied(reading.Candidates, reading.Deletions);
                Application.Apply(database.Database, write, reading, Tree(database));
            }

            using (var inMemory = StoreReading.Afresh(applied.Key, applied.Value))
            using (var inDatabase = Store(database).Read())
            {
                Assert.That(inDatabase.State, Is.EqualTo(inMemory.State));
                Assert.That(inDatabase.InForce.Records.Select(record => record.Hex), Is.EquivalentTo(inMemory.InForce.Records.Select(record => record.Hex)));
                Assert.That(inDatabase.Deletions.Select(KeePassGate.Hex.Of), Is.EquivalentTo(inMemory.Deletions.Select(KeePassGate.Hex.Of)));
                Assert.That(inDatabase.InForce.Taken("retired"), Is.EquivalentTo(inMemory.InForce.Taken("retired")));
                Assert.That(StoreAuthorities.Served(inDatabase), Is.EqualTo(StoreAuthorities.Served(inMemory)));
                Assert.That(inDatabase.ByHistory, Is.EqualTo(inMemory.ByHistory));
            }
        }

        [Test]
        public void A_retired_authority_s_entry_is_deleted_with_a_deletion_record()
        {
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");

            Assert.That(Service(database).Recreate().Succeeded, Is.True);

            Assert.That(database.RootGroup.FindEntry(authority.Uuid, true), Is.Null);
            Assert.That(database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(authority.Uuid)), Is.True);
        }

        [Test]
        public void A_write_deletes_a_version_it_finds_replaced_and_its_undo_puts_the_version_back_with_its_deletion_record_removed()
        {
            // `STORE.md` §627 step 3: a version that a version of the write names is deleted with
            // a deletion record, and a write taken back puts the entry back where it was and takes
            // its deletion record away.
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var node = new string('1', 32);
            var replaced = Versioned(database, "switch:" + node, string.Empty, "sw:deny");
            var entry = database.RootGroup.FindEntry(new PwUuid(replaced.Identifier), true);
            Assert.That(entry, Is.Not.Null, "the version is an entry of the store");
            var before = Snapshot(database);

            System.Action undo;
            using (var reading = Store(database).Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                var replacing = StoreRecord.Compose(key, "version", new Dictionary<string, string>
                {
                    { "after", replaced.Hex },
                    { "doc", "sw:inherit" },
                    { "object", "switch:" + node },
                });
                var write = StoreWrite.Plan(reading, new[] { new StoreAct("a switch", key, StoreFormat.KeyName(key), new[] { replacing }, null) }, GateVersion.Current);
                Assert.That(write.Refused, Is.Null, write.Refused);
                Assert.That(write.Delete, Does.Contain(replaced.Hex));
                undo = Application.Apply(database.Database, write, reading, Tree(database));
            }

            Assert.That(database.RootGroup.FindEntry(new PwUuid(replaced.Identifier), true), Is.Null, "the version replaced is deleted");
            Assert.That(database.Database.DeletedObjects.Count(deleted => deleted.Uuid.Equals(entry.Uuid)), Is.EqualTo(1), "with a deletion record");
            using (var reading = Store(database).Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            }

            undo();

            Assert.That(database.RootGroup.FindEntry(new PwUuid(replaced.Identifier), true), Is.SameAs(entry), "the version is back");
            Assert.That(database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(entry.Uuid)), Is.False, "and its deletion record gone");
            Assert.That(Snapshot(database), Is.EqualTo(before));
        }

        [Test]
        public void A_write_deletes_every_entry_under_an_identifier_it_deletes_and_its_undo_puts_them_all_back()
        {
            // Two entries under a single identifier, each reading as the record: a write that
            // deletes the record deletes both, with a single deletion record, and taken back puts
            // both back where they were.
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var node = "switch:" + new string('1', 32);
            var replaced = Versioned(database, node, string.Empty, "sw:deny");
            var first = database.RootGroup.FindEntry(new PwUuid(replaced.Identifier), true);
            first.ParentGroup.AddEntry(first.CloneDeep(), true);
            Assert.That(Under(database, replaced), Is.EqualTo(2));
            var before = Snapshot(database);

            System.Action undo;
            using (var reading = Store(database).Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                var replacing = StoreRecord.Compose(key, "version", new Dictionary<string, string>
                {
                    { "after", replaced.Hex },
                    { "doc", "sw:inherit" },
                    { "object", node },
                });
                var write = StoreWrite.Plan(reading, new[] { new StoreAct("a switch", key, StoreFormat.KeyName(key), new[] { replacing }, null) }, GateVersion.Current);
                Assert.That(write.Refused, Is.Null, write.Refused);
                undo = Application.Apply(database.Database, write, reading, Tree(database));
            }

            Assert.That(Under(database, replaced), Is.EqualTo(0), "both entries deleted");
            Assert.That(database.Database.DeletedObjects.Count(deleted => deleted.Uuid.Equals(first.Uuid)), Is.EqualTo(1), "with a single deletion record");

            undo();

            Assert.That(Under(database, replaced), Is.EqualTo(2), "both back");
            Assert.That(Snapshot(database), Is.EqualTo(before));
        }

        [Test]
        public void A_deletion_taken_back_after_its_group_changed_puts_each_entry_back_and_keeps_what_came_since()
        {
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var replaced = Versioned(database, "switch:" + new string('1', 32), string.Empty, "sw:deny");
            var entry = database.RootGroup.FindEntry(new PwUuid(replaced.Identifier), true);
            var group = entry.ParentGroup;
            var at = group.Entries.IndexOf(entry);
            var undo = new Stack<System.Action>();
            Application.Delete(database.Database, Tree(database), new[] { replaced.Hex }, Application.DeletionsHeld(database.Database), undo);
            Assert.That(group.Entries.IndexOf(entry), Is.EqualTo(-1));

            var since = new PwEntry(true, true);
            group.AddEntry(since, true);
            Application.Undo(undo);

            Assert.That(group.Entries.IndexOf(entry), Is.EqualTo(at), "back at its place");
            Assert.That(group.Entries.IndexOf(since), Is.Not.EqualTo(-1), "and what came since stays");
            Assert.That(database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(entry.Uuid)), Is.False);
        }

        /// <summary>How many entries of the tree are under the identifier of this record.</summary>
        private static int Under(TemporaryDatabase database, StoreRecord record)
        {
            return database.RootGroup.GetEntries(true).Count(entry => entry.Uuid.Equals(new PwUuid(record.Identifier)));
        }

        [TestCase(10)]
        [TestCase(1)]
        public void A_record_edited_in_the_entry_dialog_is_set_back_at_the_next_write_with_the_edit_kept_in_its_history(int limit)
        {
            var database = Database();
            database.Database.HistoryMaxItems = limit;
            StoreSeeds.Authority(Store(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");
            var written = authority.Strings.Get("KPG.Data").ReadString();
            Edit(database, authority, "{\"id\":\"edited\"}");
            Assume.That(Store(database).Read().ByHistory, Is.EqualTo(1));

            Assert.That(NameWritten(database), Is.True);

            Assert.That(authority.Strings.Get("KPG.Data").ReadString(), Is.EqualTo(written));
            Assert.That(authority.Strings.Get("KPG.Data").IsProtected, Is.True);
            Assert.That(authority.History.Any(item => item.Strings.ReadSafe("KPG.Data") == "{\"id\":\"edited\"}"), Is.True, "the edit is kept");
            var reading = Store(database).Read();
            Assert.That(reading.ByHistory, Is.EqualTo(0));
            Assert.That(StoreAuthorities.Served(reading), Is.Not.Null);
        }

        [Test]
        public void A_record_edited_where_the_database_keeps_no_history_has_no_version_left_and_its_key_with_it()
        {
            var database = Database();
            database.Database.HistoryMaxItems = 0;
            StoreSeeds.Authority(Store(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");

            Edit(database, authority, "{\"id\":\"edited\"}");

            Assert.That(Store(database).Read().State, Is.EqualTo(StoreState.None), "the only carrier of the key has no version that passes");
        }

        [Test]
        public void An_authority_s_content_with_its_protection_taken_off_is_protected_again_at_the_next_write()
        {
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");
            var content = authority.Strings.Get("KPG.Data");
            authority.CreateBackup(database.Database);
            authority.Strings.Set("KPG.Data", content.WithProtection(false));

            Assert.That(NameWritten(database), Is.True);

            Assert.That(authority.Strings.Get("KPG.Data").IsProtected, Is.True);
            Assert.That(authority.Strings.Get("KPG.Data").ReadString(), Is.EqualTo(content.ReadString()));
            Assert.That(authority.History.Any(item => !item.Strings.Get("KPG.Data").IsProtected), Is.True, "the unprotected version in the history");
            Assert.That(StoreAuthorities.Served(Store(database).Read()), Is.Not.Null, "and it passes as before");
        }

        [Test]
        public void A_store_written_is_read_the_same_after_a_save_and_a_reload()
        {
            var database = Database();
            var id = StoreSeeds.Authority(Store(database));

            database.SaveCloseAndReopen();

            var reading = Store(database).Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(StoreAuthorities.Served(reading), Is.EqualTo(id));
            using (var authority = Service(database).Read())
            {
                Assert.That(authority.Presence, Is.EqualTo(AuthorityPresence.Present));
            }
        }

        [Test]
        public void Copies_that_keep_different_authorities_synchronized_hold_the_key_serve_nothing_and_take_a_Create_under_it()
        {
            // `STORE.md` §614 (S11), item 28 of §644, by KeePass's own Synchronize.
            var first = Database();
            var ids = StoreSeeds.Undecided(Store(first));
            var key = Store(first).Read().InForce.Key.Name;
            var second = CopyOf(first);

            Assert.That(Service(first).Keep(ids[0], ids).Succeeded, Is.True);
            Assert.That(Service(second).Keep(ids[1], ids).Succeeded, Is.True);

            first.Database.MergeIn(second.Database, PwMergeMethod.Synchronize);

            var reading = Store(first).Read();
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
            Assert.That(reading.Answering, Is.Empty, "every authority is retired between them");
            Assert.That(reading.InForce.Key.Name, Is.EqualTo(key), "the retirements hold the key");

            Assert.That(Service(first).Create().Succeeded, Is.True);
            Assert.That(Store(first).Read().InForce.Key.Name, Is.EqualTo(key), "Create takes that key");
        }

        [Test]
        public void Two_copies_that_each_made_a_first_authority_synchronized_read_as_split()
        {
            var first = Database();
            var second = CopyOf(first);
            StoreSeeds.Authority(Store(first));
            StoreSeeds.Authority(Store(second));

            first.Database.MergeIn(second.Database, PwMergeMethod.Synchronize);

            Assert.That(Store(first).Read().State, Is.EqualTo(StoreState.Split));
        }

        [Test]
        public void A_write_is_staged_only_against_the_reading_its_store_made_last_for_a_write()
        {
            // `STORE.md` §627: a write finds the entries it touches in the walk of the reading it
            // is planned against, so a reading made without every entry, a reading of another
            // store, a reading before the last made for a write and a reading staged against
            // already are each refused before anything is planned.
            var database = Database();
            StoreSeeds.Authority(Store(database));
            var store = Store(database);
            using (var plain = store.Read())
            using (var another = Store(database).ForWrite())
            using (var earlier = store.ForWrite())
            using (var last = store.ForWrite())
            {
                var key = (byte[])last.InForce.Key.Bytes.Clone();
                var version = StoreRecord.Compose(key, "version", new Dictionary<string, string>
                {
                    { "after", string.Empty },
                    { "doc", "sw:deny" },
                    { "object", "switch:" + new string('1', 32) },
                });
                var acts = new[] { new StoreAct("a switch", key, StoreFormat.KeyName(key), new[] { version }, null) };

                Assert.That(() => store.Stage(plain, acts), Throws.ArgumentException, "a reading without every entry");
                Assert.That(() => store.Stage(another, acts), Throws.ArgumentException, "a reading of another store");
                Assert.That(() => store.Stage(earlier, acts), Throws.ArgumentException, "a reading before the last");
                var staged = store.Stage(last, acts);
                Assert.That(staged.Refused ?? staged.Held, Is.Null, "the last reading made for a write");
                Assert.That(() => store.Stage(last, acts), Throws.ArgumentException, "a reading staged against already");
                staged.Apply();
            }

            Assert.That(database.RootGroup.GetEntries(true).Count(entry => entry.Strings.ReadSafe("KPG.Kind") == "version"), Is.EqualTo(1));
        }

        [TestCase("the mark")]
        [TestCase("the group made")]
        [TestCase("the entries added")]
        [TestCase("the entries deleted")]
        [TestCase("the deletion records")]
        [TestCase("the records set back")]
        [TestCase("the protection")]
        [TestCase("every part, in the order a write takes them")]
        public void A_part_of_a_write_taken_back_by_its_undo_leaves_the_database_as_it_was(string part)
        {
            // `STORE.md` §627: the entries added are removed with no deletion record, the entries
            // deleted are put back with their deletion records removed, a record set back is put
            // back as it was edited, with the history it had, and a group made is removed. What
            // takes a failed application back is the undo of each part, run in reverse,
            // `StoreEntries.Undo`.
            var database = Database();
            var ids = StoreSeeds.Undecided(Store(database));
            var kept = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Data").Contains(ids[0]));
            Edit(database, kept, "{\"id\":\"edited\"}", false);
            database.Database.Modified = false;
            var before = Snapshot(database);

            using (var reading = Store(database).Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                var retirement = StoreRecord.Compose(key, "retired", new Dictionary<string, string>
                {
                    { "authority", ids[1] },
                    { "storeKey", KeePassGate.Hex.Of(key) },
                });
                var write = StoreWrite.Plan(reading, new[] { new StoreAct("a keep", key, StoreFormat.KeyName(key), new[] { retirement }, null) }, GateVersion.Current);
                Assume.That(write.Refused, Is.Null);
                Assume.That(write.Delete, Is.Not.Empty);
                Assume.That(write.SetBack, Is.Not.Empty);

                var undo = new Stack<System.Action>();
                var db = database.Database;
                var tree = Tree(database);
                switch (part)
                {
                    case "the mark":
                        Application.Mark(db, undo);
                        break;
                    case "the group made":
                        Application.Group(db, null, undo);
                        break;
                    case "the entries added":
                        Application.Add(Application.Group(db, reading, undo), write.Add, undo);
                        break;
                    case "the entries deleted":
                        Application.Delete(db, tree, write.Delete, Application.DeletionsHeld(db), undo);
                        break;
                    case "the deletion records":
                        Application.Record(db, write.Delete, Application.DeletionsHeld(db), undo);
                        break;
                    case "the records set back":
                        Application.SetBack(db, tree, write.SetBack, undo);
                        break;
                    case "the protection":
                        Application.Protect(db, tree, reading, Enumerable.Empty<string>(), undo);
                        break;
                    default:
                        Application.Mark(db, undo);
                        Application.Add(Application.Group(db, reading, undo), write.Add, undo);
                        var held = Application.DeletionsHeld(db);
                        Application.Delete(db, tree, write.Delete, held, undo);
                        Application.Record(db, write.Deletions, held, undo);
                        Application.SetBack(db, tree, write.SetBack, undo);
                        Application.Protect(db, tree, reading, write.Delete, undo);
                        break;
                }

                Assume.That(Snapshot(database), Is.Not.EqualTo(before), "the part changed the database");
                Application.Undo(undo);
            }

            Assert.That(Snapshot(database), Is.EqualTo(before));
        }
        private TemporaryDatabase Database()
        {
            var database = TemporaryDatabase.New();
            _databases.Add(database);
            return database;
        }

        /// <summary>A copy of a database, from here on apart, as a file copied and opened elsewhere is.</summary>
        private TemporaryDatabase CopyOf(TemporaryDatabase original)
        {
            var copy = Database();
            copy.Database.RootGroup.Uuid = original.Database.RootGroup.Uuid;
            copy.Database.MergeIn(original.Database, PwMergeMethod.Synchronize);
            return copy;
        }

        private static KdbxStore Store(TemporaryDatabase database)
        {
            return new KdbxStore(database.Database, GateVersion.Current, NullGateLog.Instance);
        }

        /// <summary>The entries of a database's tree by their identifiers, as the walk of a reading for a write passes them.</summary>
        private static TreeEntries Tree(TemporaryDatabase database)
        {
            return new TreeEntries(database.RootGroup.GetEntries(true).ToList());
        }

        private static AuthorityService Service(TemporaryDatabase database)
        {
            return new AuthorityService(Store(database), NullGateLog.Instance);
        }

        private static PwGroup PluginGroup(TemporaryDatabase database)
        {
            return database.RootGroup.GetGroups(true).Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes)
                                                                       && found.Entries.UCount > 0);
        }

        /// <summary>
        /// Everything of a database a write of the plugin's own can touch, as text: each group and
        /// where it is, each entry with its strings, their protection, its times and its history,
        /// the deletion records, and the mark.
        /// </summary>
        private static string Snapshot(TemporaryDatabase database)
        {
            var text = new StringBuilder();
            foreach (var group in database.RootGroup.GetGroups(true))
            {
                text.Append("group ").Append(group.Uuid.ToHexString()).Append(" in ").Append(group.ParentGroup.Uuid.ToHexString()).AppendLine();
            }

            foreach (var entry in database.RootGroup.GetEntries(true))
            {
                text.Append("entry ").Append(entry.Uuid.ToHexString()).Append(" in ").Append(entry.ParentGroup.Uuid.ToHexString())
                    .Append(' ').Append(entry.LastModificationTime.Ticks).AppendLine();
                Strings(text, entry);
                foreach (var version in entry.History)
                {
                    text.Append("  version ").Append(version.LastModificationTime.Ticks).AppendLine();
                    Strings(text, version);
                }
            }

            foreach (var deleted in database.Database.DeletedObjects)
            {
                text.Append("deleted ").Append(deleted.Uuid.ToHexString()).AppendLine();
            }

            return text.Append("modified ").Append(database.Database.Modified).ToString();
        }

        private static void Strings(StringBuilder text, PwEntry entry)
        {
            foreach (var pair in entry.Strings.OrderBy(pair => pair.Key, System.StringComparer.Ordinal))
            {
                text.Append("    ").Append(pair.Key).Append(pair.Value.IsProtected ? " protected " : " ").Append(pair.Value.ReadString()).AppendLine();
            }
        }

        /// <summary>An edit of a record's content in KeePass's entry dialog: the version before into the history.</summary>
        private static void Edit(TemporaryDatabase database, PwEntry entry, string content, bool protect = true)
        {
            entry.CreateBackup(database.Database);
            entry.Strings.Set("KPG.Data", new ProtectedString(protect, content));
            entry.Touch(true, false);
        }

        /// <summary>
        /// A version of this object written by itself, under the store key that stands, naming
        /// these identifiers as replaced: the record it wrote.
        /// </summary>
        private static StoreRecord Versioned(TemporaryDatabase database, string objectName, string after, string doc)
        {
            var store = Store(database);
            byte[] key;
            using (var reading = store.Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            var version = StoreRecord.Compose(key, "version", new Dictionary<string, string>
            {
                { "after", after },
                { "doc", doc },
                { "object", objectName },
            });
            var written = store.Write(new[] { new StoreAct("a version", key, StoreFormat.KeyName(key), new[] { version }, null) });
            Assert.That(written.Taken, Is.True, written.Reason);
            return version;
        }

        /// <summary>
        /// A write of the plugin's own that touches no authority, a profile name: what sets the
        /// store's other records back.
        /// </summary>
        private static bool NameWritten(TemporaryDatabase database)
        {
            var store = Store(database);
            var key = (byte[])store.Read().InForce.Key.Bytes.Clone();
            var name = StoreRecord.Compose(key, "profile", new Dictionary<string, string> { { "after", string.Empty }, { "name", "reader" } });
            return store.Write(new[] { new StoreAct("a name", key, StoreFormat.KeyName(key), new[] { name }, null) }).Taken;
        }
    }
}
