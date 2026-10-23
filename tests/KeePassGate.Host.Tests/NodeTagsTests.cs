using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KeePassGate.Bridge;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The tag a group or an entry is answered with, against real KeePass objects changed the way
    /// KeePass itself changes them.
    /// </summary>
    /// <remarks>
    /// **The changes here are made the way KeePass makes them, not through the API**, because the
    /// tag is worth something only if it moves for the changes this plugin did not make: the
    /// operator's edit, a drag to another group, a merge, another plugin setting a value. Each of
    /// those cases is one that KeePass's own modification time was measured to miss, or one it
    /// moves for without anything a route serves having changed.
    ///
    /// Everything in here is invented.
    /// </remarks>
    [TestFixture]
    public sealed class NodeTagsTests
    {
        /// <summary>A moment on a whole second, so that a tag taken of it is the tag of that second.</summary>
        private static readonly DateTime Moment = new DateTime(2030, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        private TemporaryDatabase _database;
        private IVault _vault;
        private PwGroup _group;
        private PwEntry _entry;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _vault = Vault(CurrentThread.Instance);

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.AddGroup(_group, true);

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "an invented name"));
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not-a-real-password"));
            _entry.Binaries.Set("notes.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("invented")));
            _group.AddEntry(_entry, true);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void One_entry_holding_the_same_gives_the_same_tag_however_often_it_is_asked()
        {
            var tag = EntryTag();

            Assert.That(tag, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(EntryTag(), Is.EqualTo(tag), "asked again");
            Assert.That(Vault(CurrentThread.Instance).EntryTag(Uuid(_entry)), Is.EqualTo(tag),
                "and asked of another vault keyed the same, which is what KeePass started again builds");
        }

        [Test]
        public void A_tag_is_keyed_by_the_host_certificate_it_was_derived_from()
        {
            var one = Encoding.UTF8.GetBytes("one stored host certificate");
            var other = Encoding.UTF8.GetBytes("another stored host certificate");

            Assert.That(NodeTags.KeyFrom(one), Is.EqualTo(NodeTags.KeyFrom(one)), "the same file gives the same key");
            Assert.That(NodeTags.KeyFrom(one), Is.Not.EqualTo(NodeTags.KeyFrom(other)));

            var keyed = new NodeTags(NodeTags.KeyFrom(one));
            var rekeyed = new NodeTags(NodeTags.KeyFrom(other));
            Assert.That(keyed.Of(_group), Is.Not.EqualTo(rekeyed.Of(_group)),
                "an authority recreated is a new host certificate, and every tag changes with it");

            Assert.Throws<ArgumentException>(() => NodeTags.KeyFrom(null));
            Assert.Throws<ArgumentException>(() => NodeTags.KeyFrom(Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => new NodeTags(null));
        }

        [Test]
        public void A_lookup_says_whether_the_entry_holds_a_protected_value_and_a_listing_does_not()
        {
            Assert.That(_vault.Entry(Uuid(_entry)).HoldsProtected, Is.True);
            Assert.That(_vault.Read(Uuid(_entry)).Entry.HoldsProtected, Is.True);

            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, string.Empty));
            _database.Database.MemoryProtection.ProtectPassword = false;
            Assert.That(_vault.Entry(Uuid(_entry)).HoldsProtected, Is.False);

            _database.Database.MemoryProtection.ProtectPassword = true;
            Assert.That(_vault.Entry(Uuid(_entry)).HoldsProtected, Is.True,
                "a standard string the database protects is protected, whatever its own flag says");

            foreach (var listed in _vault.Entries())
            {
                Assert.That(listed.HoldsProtected, Is.Null);
            }
        }

        [Test]
        public void Every_kind_of_change_a_route_could_serve_is_a_new_tag()
        {
            // In order, each on top of the one before, so that no two of them could pass by
            // landing back on a tag already seen.
            var changes = new List<KeyValuePair<string, Action>>
            {
                Change("a value", () => Title(false, "renamed")),
                Change("a new string", () => _entry.Strings.Set("Account", new ProtectedString(false, "invented"))),
                Change("a string taken off", () => _entry.Strings.Remove(PwDefs.PasswordField)),
                Change("a string's protection", () => Title(true, "renamed")),
                Change("an attachment's bytes", () => Attach("notes.txt", "changed")),
                Change("an attachment's name", () => Rename(_entry, "notes.txt", "renamed.txt")),
                Change("an expiry put on", () =>
                {
                    _entry.Expires = true;
                    _entry.ExpiryTime = new DateTime(2031, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                }),
                Change("an expiry moved", () => _entry.ExpiryTime = new DateTime(2032, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
                Change("a move to another group", () => Move(_entry, _database.RootGroup)),
                Change("a modification time", () => _entry.LastModificationTime = Moment),
                Change("a creation time", () => _entry.CreationTime = Moment),
            };

            var seen = new HashSet<string> { EntryTag() };

            foreach (var change in changes)
            {
                change.Value();

                Assert.That(seen.Add(EntryTag()), Is.True, change.Key + " left the tag where it was");
            }
        }

        [Test]
        public void What_no_route_serves_leaves_the_tag_where_it_was()
        {
            // The rules, the history, the access time and the usage count are not in what a route
            // answers, and a moment is served to the second, so a change to any of those is no
            // reason to refuse a write made against the content.
            _entry.LastModificationTime = Moment;
            var tag = EntryTag();

            _entry.CustomData.Set("KPG", "{\"reader\":[\"read\"]}");
            _entry.CreateBackup(_database.Database);
            _entry.LastAccessTime = Moment.AddDays(1);
            _entry.UsageCount += 5;
            _entry.LastModificationTime = Moment.AddMilliseconds(600);

            Assert.That(EntryTag(), Is.EqualTo(tag));
        }

        [Test]
        public void Putting_back_what_was_there_gives_back_the_tag_that_was_there()
        {
            var tag = EntryTag();

            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "renamed"));
            Assert.That(EntryTag(), Is.Not.EqualTo(tag));

            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            Assert.That(EntryTag(), Is.EqualTo(tag));

            // An expiry taken off keeps its moment, as KeePass's own dialog leaves it, and a moment
            // that is not in force is not something a route serves.
            _entry.Expires = true;
            _entry.ExpiryTime = new DateTime(2031, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            Assert.That(EntryTag(), Is.Not.EqualTo(tag));

            _entry.Expires = false;
            Assert.That(EntryTag(), Is.EqualTo(tag));
        }

        [Test]
        public void An_attachment_larger_than_the_gate_serves_enters_by_its_size_alone()
        {
            var size = VaultAttachment.MostServed + 1;
            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(size, 1)));
            var tag = EntryTag();

            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(size, 2)));
            Assert.That(EntryTag(), Is.EqualTo(tag), "bytes no route can read or write are not hashed");

            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(size + 1, 2)));
            Assert.That(EntryTag(), Is.Not.EqualTo(tag), "and its size is still served, so it still counts");

            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(VaultAttachment.MostServed, 1)));
            var served = EntryTag();
            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(VaultAttachment.MostServed, 2)));
            Assert.That(EntryTag(), Is.Not.EqualTo(served), "one the gate does serve is hashed whole");
        }

        [Test]
        public void A_value_that_is_not_UTF_8_is_tagged_as_it_is_served()
        {
            // Bytes another program could have written, and which a read serves with the bad
            // sequence replaced. A tag taken of the stored bytes changed the first time the value
            // was read as a string, so a client's first tag was refused for nothing anybody wrote.
            _entry.Strings.Set("Invented", new ProtectedString(true, new byte[] { 0x41, 0xFF, 0x42 }));
            var tag = EntryTag();

            _vault.Read(Uuid(_entry));

            Assert.That(EntryTag(), Is.EqualTo(tag));
        }

        [Test]
        public void Two_entries_holding_the_same_never_share_a_tag()
        {
            var twin = _entry.CloneDeep();
            twin.SetUuid(new PwUuid(true), false);
            _group.AddEntry(twin, true);

            Assert.That(_vault.EntryTag(Uuid(twin)), Is.Not.EqualTo(EntryTag()));
        }

        [Test]
        public void A_database_saved_and_opened_again_keeps_every_tag()
        {
            // The case a tag holding the time to a fraction cannot survive: the file keeps whole
            // seconds, so every moment read back is a different moment, and such a tag would
            // refuse every write made against what was read before the database was opened again.
            _entry.LastModificationTime = Moment.AddMilliseconds(250);
            var entry = EntryTag();
            var group = GroupTag();

            _database.LockAndUnlock();

            Assert.That(_vault.EntryTag(Uuid(_entry)), Is.EqualTo(entry));
            Assert.That(_vault.GroupTag(Uuid(_group)), Is.EqualTo(group));

            _database.SaveCloseAndReopen();
            var reopened = Vault(CurrentThread.Instance);

            Assert.That(reopened.EntryTag(Uuid(_entry)), Is.EqualTo(entry), "and a database closed and opened again");
            Assert.That(reopened.GroupTag(Uuid(_group)), Is.EqualTo(group));
        }

        [Test]
        public void A_merge_that_takes_other_strings_at_the_same_moment_is_a_new_tag()
        {
            // Measured against KeePass 2.61.1: a synchronize between two copies whose entry carries
            // the same modification time takes the other copy's strings and leaves the time where
            // it was, so a tag made of the time would have stood over content it no longer
            // described.
            var source = Copied(_database.Database);
            var theirs = source.RootGroup.FindEntry(_entry.Uuid, true);
            theirs.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "changed elsewhere"));
            theirs.LastModificationTime = _entry.LastModificationTime;

            var tag = EntryTag();
            var moment = _entry.LastModificationTime;

            _database.Database.MergeIn(source, PwMergeMethod.Synchronize, null);

            var merged = _database.RootGroup.FindEntry(_entry.Uuid, true);
            Assert.That(merged.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("changed elsewhere"));
            Assert.That(merged.LastModificationTime, Is.EqualTo(moment), "KeePass left the time as it was");
            Assert.That(EntryTag(), Is.Not.EqualTo(tag), "and the tag moved anyway");
        }

        [Test]
        public void A_group_s_tag_is_its_own_name_notes_times_and_place()
        {
            var tag = GroupTag();
            var seen = new HashSet<string> { tag };

            _group.AddEntry(new PwEntry(true, true), true);
            Assert.That(GroupTag(), Is.EqualTo(tag), "what is inside a group is not the group's");

            var changes = new List<KeyValuePair<string, Action>>
            {
                Change("a name", () => _group.Name = "Renamed"),
                Change("notes", () => _group.Notes = "invented notes"),
                Change("a move", () => MoveGroup(_group)),
                Change("a modification time", () => _group.LastModificationTime = Moment),
            };

            foreach (var change in changes)
            {
                change.Value();

                Assert.That(seen.Add(GroupTag()), Is.True, change.Key + " left the tag where it was");
            }
        }

        [Test]
        public void A_write_against_a_tag_the_operator_has_changed_since_is_refused_and_changes_nothing()
        {
            _entry.Strings.Set("Invented", new ProtectedString(false, "a value"));
            var stale = Asking(EntryTag());

            // As the entry dialog's OK leaves it.
            _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "typed in KeePass"));
            _entry.Touch(true, false);

            var before = _database.Database.HashOfLastIO;
            var held = EntryTag();
            var revisions = _entry.History.UCount;

            foreach (var refused in new[]
            {
                _vault.UpdateEntry(Uuid(_entry), Fields(PwDefs.UserNameField, "written by a client"), stale),
                _vault.UpdateField(Uuid(_entry), PwDefs.TitleField, "x", stale),
                _vault.CreateField(Uuid(_entry), "Account", "x", false, stale),
                _vault.DeleteField(Uuid(_entry), "Invented", stale),
                _vault.CreateAttachment(Uuid(_entry), "other.txt", new byte[] { 1 }, stale),
                _vault.UpdateAttachment(Uuid(_entry), "notes.txt", new byte[] { 1 }, stale),
                _vault.DeleteAttachment(Uuid(_entry), "notes.txt", stale),
                _vault.DeleteEntry(Uuid(_entry), stale),
            })
            {
                Assert.That(refused.IsPreconditionFailed, Is.True, refused.Reason);
                Assert.That(refused.Tag, Is.Null, "a refusal hands back no tag");
            }

            Assert.That(_vault.Entry(Uuid(_entry)), Is.Not.Null, "the entry is where it was");
            Assert.That(EntryTag(), Is.EqualTo(held), "and holds what it held");
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("typed in KeePass"));
            Assert.That(_entry.History.UCount, Is.EqualTo(revisions), "with no revision taken for a write that never ran");
            Assert.That(_database.Database.HashOfLastIO, Is.EqualTo(before), "and nothing was saved");
        }

        [Test]
        public void An_update_that_would_change_nothing_is_still_asked_its_condition()
        {
            // Every way an update can turn out to change nothing, which writes nothing, and which
            // used to be answered without asking the condition, or before it was asked.
            var stale = Asking("0123456789abcdef0123456789abcdef");
            var empty = new Dictionary<string, string>();

            foreach (var refused in new[]
            {
                _vault.UpdateEntry(Uuid(_entry), empty, stale),
                _vault.UpdateEntry(Uuid(_entry), Fields(PwDefs.TitleField, "an invented service"), stale),
                _vault.UpdateField(Uuid(_entry), PwDefs.TitleField, "an invented service", stale),
                _vault.UpdateAttachment(Uuid(_entry), "notes.txt", Encoding.UTF8.GetBytes("invented"), stale),
                _vault.UpdateGroup(Uuid(_group), null, null, stale),
                _vault.UpdateGroup(Uuid(_group), "Fictitious", null, stale),
            })
            {
                Assert.That(refused.IsPreconditionFailed, Is.True, refused.Reason);
            }

            var before = _database.Database.HashOfLastIO;
            var held = Asking(EntryTag());
            var unchanged = _vault.UpdateEntry(Uuid(_entry), empty, held);

            Assert.That(unchanged.Ok, Is.True, unchanged.Reason);
            Assert.That(unchanged.Tag, Is.EqualTo(EntryTag()), "and one that holds answers the tag the entry still has");
            Assert.That(_database.Database.HashOfLastIO, Is.EqualTo(before), "having saved nothing");
        }

        [Test]
        public void A_refusal_that_does_not_depend_on_the_tag_comes_before_it()
        {
            // RFC 9110 section 13.2.1: a node gone, a name taken or a group that is not empty is
            // answered as it would be without the condition, since no tag would have made the
            // request one that could be served.
            var stale = Asking("0123456789abcdef0123456789abcdef");
            _group.AddEntry(new PwEntry(true, true), true);

            Assert.That(_vault.DeleteGroup(Uuid(_group), stale).IsConflict, Is.True, "a group holding something");
            Assert.That(_vault.DeleteGroup(_vault.RootAlias, stale).IsConflict, Is.True, "the root group");
            Assert.That(_vault.CreateField(Uuid(_entry), PwDefs.TitleField, "x", false, stale).IsConflict, Is.True);
            Assert.That(_vault.CreateAttachment(Uuid(_entry), "notes.txt", new byte[] { 1 }, stale).IsConflict, Is.True);
            Assert.That(_vault.DeleteField(Uuid(_entry), PwDefs.TitleField, stale).IsConflict, Is.True,
                "a standard field, which is emptied and not taken off");

            foreach (var missing in new[]
            {
                _vault.UpdateField(Uuid(_entry), "NotThere", "x", stale),
                _vault.DeleteField(Uuid(_entry), "NotThere", stale),
                _vault.UpdateAttachment(Uuid(_entry), "not-there.txt", new byte[] { 1 }, stale),
                _vault.DeleteAttachment(Uuid(_entry), "not-there.txt", stale),
                _vault.UpdateEntry(new PwUuid(true).ToHexString(), Fields(PwDefs.TitleField, "x"), stale),
            })
            {
                Assert.That(missing.IsPreconditionFailed || missing.IsConflict || missing.Ok, Is.False, missing.Reason);
            }
        }

        [Test]
        public void A_write_against_the_tag_it_read_goes_ahead_and_hands_back_the_tag_a_read_then_gives()
        {
            var write = _vault.UpdateEntry(
                Uuid(_entry), Fields(PwDefs.UserNameField, "written by a client"), Asking(EntryTag()));

            Assert.That(write.Ok, Is.True, write.Reason);
            Assert.That(write.Tag, Is.EqualTo(EntryTag()));

            var field = _vault.UpdateField(Uuid(_entry), PwDefs.TitleField, "renamed", Asking(write.Tag));

            Assert.That(field.Ok, Is.True, field.Reason);
            Assert.That(field.Tag, Is.EqualTo(EntryTag()), "a field's write hands back its entry's tag");

            var unasked = _vault.UpdateField(Uuid(_entry), PwDefs.TitleField, "renamed again", VaultTagging.None);
            Assert.That(unasked.Ok, Is.True, unasked.Reason);
            Assert.That(unasked.Tag, Is.Null, "and a write not asked for one hands back none");
        }

        [Test]
        public void Taking_a_field_or_an_attachment_off_hands_back_the_tag_of_the_entry_that_is_left()
        {
            _entry.Strings.Set("Invented", new ProtectedString(false, "a value"));

            var field = _vault.DeleteField(Uuid(_entry), "Invented", Asking(EntryTag()));
            Assert.That(field.Ok, Is.True, field.Reason);
            Assert.That(field.Tag, Is.EqualTo(EntryTag()));

            var attachment = _vault.DeleteAttachment(Uuid(_entry), "notes.txt", Asking(EntryTag()));
            Assert.That(attachment.Ok, Is.True, attachment.Reason);
            Assert.That(attachment.Tag, Is.EqualTo(EntryTag()));

            var entry = _vault.DeleteEntry(Uuid(_entry), Asking(EntryTag()));
            Assert.That(entry.Ok, Is.True, entry.Reason);
            Assert.That(entry.Tag, Is.Null, "and an entry taken away leaves nothing to tag");
        }

        [Test]
        public void A_group_write_asks_the_group_s_own_tag()
        {
            var stale = Asking(GroupTag());
            _group.Name = "Renamed in KeePass";

            Assert.That(_vault.UpdateGroup(Uuid(_group), null, "invented notes", stale).IsPreconditionFailed, Is.True);
            Assert.That(_group.Notes, Is.Empty);

            var write = _vault.UpdateGroup(Uuid(_group), null, "invented notes", Asking(GroupTag()));
            Assert.That(write.Ok, Is.True, write.Reason);
            Assert.That(write.Tag, Is.EqualTo(GroupTag()));

            // An empty one, since a group holding something is a `409` whatever the tag says.
            var empty = new PwGroup(true, true, "Empty", PwIcon.Folder);
            _database.RootGroup.AddGroup(empty, true);
            var emptied = Asking(_vault.GroupTag(Uuid(empty)));
            empty.Notes = "changed in KeePass";

            Assert.That(_vault.DeleteGroup(Uuid(empty), emptied).IsPreconditionFailed, Is.True);
            Assert.That(_vault.Group(Uuid(empty)), Is.Not.Null);
        }

        [TestCase("entry")]
        [TestCase("field")]
        public void An_operator_edit_landing_before_the_queued_write_is_refused(string route)
        {
            // The condition is asked inside the change, on KeePass's thread under the writer's
            // lock. Asked against the handler's own read, the operator's OK landing between that
            // read and the queued write was carried straight over, and answered 200.
            var operatorFirst = new OperatorFirst(() =>
            {
                _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "typed in KeePass"));
                _entry.Touch(true, false);
            });
            var vault = Vault(operatorFirst);

            // Read as the connection thread reads: fresh, before the write is queued.
            var fresh = Asking(vault.EntryTag(Uuid(_entry)));

            var write = route == "entry"
                ? vault.UpdateEntry(Uuid(_entry), Fields(PwDefs.UserNameField, "written by a client"), fresh)
                : vault.UpdateField(Uuid(_entry), PwDefs.UserNameField, "written by a client", fresh);

            Assert.That(operatorFirst.Ran, Is.True);
            Assert.That(write.IsPreconditionFailed, Is.True, write.Reason);
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("typed in KeePass"));
        }

        private IVault Vault(IUiThread thread)
        {
            return new KdbxVault(
                _database.Database,
                new DatabaseWriter(thread, database => database.Save(null), null, null, null),
                TestTags.Fixed,
                null);
        }

        /// <summary>A write made only while the node holds what this tag says, answering the tag it leaves.</summary>
        private static VaultTagging Asking(string tag)
        {
            return new VaultTagging(VaultPrecondition.Read("\"" + tag + "\""), true);
        }

        private static IReadOnlyDictionary<string, string> Fields(string name, string value)
        {
            return new Dictionary<string, string> { { name, value } };
        }

        private static byte[] Filled(int size, byte value)
        {
            var bytes = new byte[size];
            for (var i = 0; i < size; i++)
            {
                bytes[i] = value;
            }

            return bytes;
        }

        private string EntryTag()
        {
            return _vault.EntryTag(Uuid(_entry));
        }

        private string GroupTag()
        {
            return _vault.GroupTag(Uuid(_group));
        }

        private static string Uuid(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }

        private static string Uuid(PwGroup group)
        {
            return group.Uuid.ToHexString();
        }

        /// <summary>A second copy of the database, as another machine's replica of the file is.</summary>
        private static PwDatabase Copied(PwDatabase database)
        {
            var file = new MemoryStream();
            new KdbxFile(database).Save(file, null, KdbxFormat.Default, null);

            var copy = new PwDatabase { MasterKey = database.MasterKey, RootGroup = new PwGroup(true, true) };
            new KdbxFile(copy).Load(new MemoryStream(file.ToArray()), KdbxFormat.Default, null);
            return copy;
        }

        private static KeyValuePair<string, Action> Change(string what, Action change)
        {
            return new KeyValuePair<string, Action>(what, change);
        }

        private void Title(bool isProtected, string value)
        {
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(isProtected, value));
        }

        private void Attach(string name, string content)
        {
            _entry.Binaries.Set(name, new ProtectedBinary(true, Encoding.UTF8.GetBytes(content)));
        }

        private static void Rename(PwEntry entry, string from, string to)
        {
            var binary = entry.Binaries.Get(from);
            entry.Binaries.Remove(from);
            entry.Binaries.Set(to, binary);
        }

        /// <summary>A move as the main window makes one, by drag or by Move To.</summary>
        private static void Move(PwEntry entry, PwGroup to)
        {
            entry.ParentGroup.Entries.Remove(entry);
            to.AddEntry(entry, true, true);
        }

        private void MoveGroup(PwGroup group)
        {
            var other = new PwGroup(true, true, "Other", PwIcon.Folder);
            _database.RootGroup.AddGroup(other, true);

            group.ParentGroup.Groups.Remove(group);
            other.AddGroup(group, true, true);
        }

        /// <summary>An operator's edit that KeePass's thread runs just before the queued write.</summary>
        private sealed class OperatorFirst : IUiThread
        {
            private readonly Action _edit;

            public OperatorFirst(Action edit)
            {
                _edit = edit;
            }

            public bool Ran { get; private set; }

            public bool Run(Action work, TimeSpan timeout)
            {
                if (!Ran)
                {
                    Ran = true;
                    _edit();
                }

                work();
                return true;
            }
        }
    }
}
