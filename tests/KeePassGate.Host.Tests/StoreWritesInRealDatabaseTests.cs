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
            var id = StoreSeeds.Authority(Store(database), Admissions(database));

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
            StoreSeeds.Authority(Store(database), Admissions(database));
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
            StoreSeeds.Authority(Store(database), Admissions(database));
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
            var first = StoreSeeds.Authority(Store(database), Admissions(database));
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
        public void A_retired_authority_s_entry_is_deleted_with_a_deletion_record()
        {
            var database = Database();
            StoreSeeds.Authority(Store(database), Admissions(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");

            Assert.That(Service(database).Recreate().Succeeded, Is.True);

            Assert.That(database.RootGroup.FindEntry(authority.Uuid, true), Is.Null);
            Assert.That(database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(authority.Uuid)), Is.True);
        }

        [TestCase(10)]
        [TestCase(1)]
        public void A_record_edited_in_the_entry_dialog_is_set_back_at_the_next_write_with_the_edit_kept_in_its_history(int limit)
        {
            var database = Database();
            database.Database.HistoryMaxItems = limit;
            StoreSeeds.Authority(Store(database), Admissions(database));
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
            StoreSeeds.Authority(Store(database), Admissions(database));
            var authority = PluginGroup(database).Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "authority");

            Edit(database, authority, "{\"id\":\"edited\"}");

            Assert.That(Store(database).Read().State, Is.EqualTo(StoreState.None), "the only carrier of the key has no version that passes");
        }

        [Test]
        public void An_authority_s_content_with_its_protection_taken_off_is_protected_again_at_the_next_write()
        {
            var database = Database();
            StoreSeeds.Authority(Store(database), Admissions(database));
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
            var id = StoreSeeds.Authority(Store(database), Admissions(database));

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
            var ids = StoreSeeds.Undecided(Store(first), Admissions(first));
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
            StoreSeeds.Authority(Store(first), Admissions(first));
            StoreSeeds.Authority(Store(second), Admissions(second));

            first.Database.MergeIn(second.Database, PwMergeMethod.Synchronize);

            Assert.That(Store(first).Read().State, Is.EqualTo(StoreState.Split));
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
            var ids = StoreSeeds.Undecided(Store(database), Admissions(database));
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
                        Application.Delete(db, write.Delete, Application.DeletionsHeld(db), undo);
                        break;
                    case "the deletion records":
                        Application.Record(db, write.Delete, Application.DeletionsHeld(db), undo);
                        break;
                    case "the records set back":
                        Application.SetBack(db, write.SetBack, undo);
                        break;
                    case "the protection":
                        Application.Protect(db, reading, Enumerable.Empty<string>(), undo);
                        break;
                    default:
                        Application.Mark(db, undo);
                        Application.Add(Application.Group(db, reading, undo), write.Add, undo);
                        var held = Application.DeletionsHeld(db);
                        Application.Delete(db, write.Delete, held, undo);
                        Application.Record(db, write.Deletions, held, undo);
                        Application.SetBack(db, write.SetBack, undo);
                        Application.Protect(db, reading, write.Delete, undo);
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

        private static CustomDataStore Admissions(TemporaryDatabase database)
        {
            return new CustomDataStore(database.Database);
        }

        private static AuthorityService Service(TemporaryDatabase database)
        {
            return new AuthorityService(Store(database), Admissions(database), NullGateLog.Instance);
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
