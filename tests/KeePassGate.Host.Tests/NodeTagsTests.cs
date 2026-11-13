using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using KeePassGate.Bridge;
using KeePassGate.Testing;
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
        public void A_long_value_is_digested_in_pieces_as_the_value_whole_is()
        {
            // A value longer than a piece is encoded a piece at a time, so that a tag over
            // megabytes allocates no second copy of them. The bytes digested have to be the
            // bytes of the value encoded whole, wherever a piece ends: inside a character of
            // several bytes, between the halves of a pair of surrogates, on a surrogate with no
            // other half, which the encoding replaces. Held against the same part written whole,
            // its length before its bytes.
            const int Piece = NodeTags.Pieces.Characters;
            const string Pair = "\U0001F511";
            var values = new List<string>
            {
                string.Empty,
                "short",
                new string('a', Piece),
                new string('a', Piece + 1),
                new string('\u00E9', (2 * Piece) + 1),
                new string('\u20AC', (3 * Piece) + 7),
                new string('a', Piece - 1) + Pair + new string('b', Piece),
                new string('a', Piece - 2) + Pair + new string('b', Piece),
                new string('a', (2 * Piece) - 1) + Pair,
                new string('a', Piece - 1) + "\uD83D" + new string('b', Piece),
                new string('a', Piece) + "\uDD11" + new string('b', Piece),
                new string('a', Piece - 1) + "\uDD11\uD83D" + new string('b', Piece),
                new string('a', (2 * Piece) - 1) + "\uD83D",
                string.Concat(Enumerable.Repeat(Pair, Piece + 3)),
            };

            using (var pieces = new NodeTags.Pieces())
            {
                foreach (var value in values)
                {
                    byte[] digested;
                    using (var sha256 = SHA256.Create())
                    {
                        NodeTags.Value(sha256, value, pieces);
                        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        digested = sha256.Hash;
                    }

                    var bytes = Encoding.UTF8.GetBytes(value);
                    var length = new[]
                    {
                        (byte)(bytes.Length >> 24), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 8), (byte)bytes.Length,
                    };

                    byte[] whole;
                    using (var sha256 = SHA256.Create())
                    {
                        whole = sha256.ComputeHash(length.Concat(bytes).ToArray());
                    }

                    Assert.That(digested, Is.EqualTo(whole), "a value of " + value.Length + " characters");
                }
            }
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
                Change("the database protecting a name", () => _database.Database.MemoryProtection.ProtectUserName = true),
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
        public void A_withheld_value_is_not_in_the_view_and_never_moves_its_tag()
        {
            // A caller whose rule reads the entry and not its protected values. The tag of its
            // view covers what the view holds, so nothing about the password is in it: not the
            // value, and not how long the value is.
            var sight = new VaultSight(true, false, VaultAttachmentSight.Read);
            var view = Seen(sight);
            var password = view.Field(PwDefs.PasswordField);

            Assert.That(password.IsProtected, Is.True);
            Assert.That(password.IsWithheld, Is.True);
            Assert.That(password.Value, Is.Null);
            Assert.That(view.Field(PwDefs.TitleField).Value, Is.EqualTo("an invented service"));
            Assert.That(view.Protected, Does.Contain(PwDefs.PasswordField), "that it is protected is still told");

            var tag = view.Tag;
            var whole = EntryTag();
            Assert.That(tag, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(tag, Is.Not.EqualTo(whole), "a view holding less is tagged as what it holds");

            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "another password, and a longer password"));
            Assert.That(Seen(sight).Tag, Is.EqualTo(tag));
            Assert.That(EntryTag(), Is.Not.EqualTo(whole), "while the whole entry's tag moved");

            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, string.Empty));
            Assert.That(Seen(sight).Tag, Is.EqualTo(tag), "a value emptied is as far out of sight as a value set");

            Title(false, "renamed");
            Assert.That(Seen(sight).Tag, Is.Not.EqualTo(tag), "and a string in sight moves it as ever");
        }

        [Test]
        public void Attachments_out_of_sight_are_not_in_the_view_and_no_change_to_them_moves_its_tag()
        {
            var sight = new VaultSight(true, true, VaultAttachmentSight.None);
            var view = Seen(sight);

            Assert.That(view.Attachments, Is.Null);
            Assert.That(view.Attachment("notes.txt"), Is.Null);
            Assert.That(view.Field(PwDefs.PasswordField).Value, Is.EqualTo("not-a-real-password"));

            var tag = view.Tag;
            var whole = EntryTag();
            Assert.That(tag, Is.Not.EqualTo(whole));

            var changes = new List<KeyValuePair<string, Action>>
            {
                Change("an attachment's bytes", () => Attach("notes.txt", "changed")),
                Change("an attachment's size", () => Attach("notes.txt", "changed, and longer")),
                Change("an attachment's name", () => Rename(_entry, "notes.txt", "renamed.txt")),
                Change("an attachment added", () => Attach("another.txt", "invented")),
                Change("every attachment taken off", () => _entry.Binaries.Clear()),
            };

            foreach (var change in changes)
            {
                change.Value();

                Assert.That(Seen(sight).Tag, Is.EqualTo(tag), change.Key + " moved the tag of a view that holds none");
                Assert.That(EntryTag(), Is.Not.EqualTo(whole), change.Key + " left the whole entry's tag where it was");
                whole = EntryTag();
            }

            // An entry holding no attachment shows a caller that may read attachments what it
            // shows a caller that may not, which is none, so the two views are one tag: nothing
            // about how far a rule reaches is in a tag.
            Assert.That(Seen(Sights.Whole).Attachments, Is.Empty);
            Assert.That(Seen(Sights.Whole).Tag, Is.EqualTo(tag),
                "a view holding no attachment is tagged by what it holds, whatever its sight reaches");
        }

        [Test]
        public void A_view_asked_for_with_no_sight_is_taken_with_the_least()
        {
            foreach (var view in new[]
            {
                _vault.View(Uuid(_entry), null),
                _vault.View(Uuid(_entry), found => null),
            })
            {
                Assert.That(view.Field(PwDefs.PasswordField).IsWithheld, Is.True);
                Assert.That(view.Field(PwDefs.PasswordField).Value, Is.Null);
                Assert.That(view.Attachments, Is.Null);
                Assert.That(view.Field(PwDefs.UserNameField).IsWithheld, Is.True);
                Assert.That(view.Field(PwDefs.UserNameField).Value, Is.Null,
                    "the least does not read, so a string nothing protects is out of its sight too");
                Assert.That(view.Protected, Does.Contain(PwDefs.PasswordField), "names and flags are still told");
                Assert.That(view.Sight.Reads, Is.False);
                Assert.That(view.Tag, Is.Null, "and the least is handed no tag");
            }
        }

        [Test]
        public void Attachments_listed_and_not_read_are_in_the_view_by_name_and_size_and_in_its_tag_by_no_more()
        {
            // The three levels of the same entry, changed the way KeePass or another plugin
            // changes an attachment, with nothing touched: a tag moves with what its view holds
            // and with nothing else.
            var none = new VaultSight(true, true, VaultAttachmentSight.None);
            var listed = new VaultSight(true, true, VaultAttachmentSight.Listed);
            var read = Sights.Whole;

            var view = Seen(listed);
            var attachment = view.Attachment("notes.txt");
            Assert.That(view.Attachments, Has.Count.EqualTo(1));
            Assert.That(attachment.Name, Is.EqualTo("notes.txt"));
            Assert.That(attachment.Size, Is.EqualTo(8));
            Assert.That(attachment.IsWithheld, Is.True);
            Assert.That(attachment.Content, Is.Null, "nothing reads the bytes of an attachment that is only listed");
            Assert.That(attachment.Digest, Is.Null);
            Assert.That(Seen(read).Attachment("notes.txt").IsWithheld, Is.False);

            var tags = new[] { Seen(none).Tag, Seen(listed).Tag, Seen(read).Tag };
            Assert.That(tags, Is.Unique, "three views holding different things of the attachments");
            Assert.That(tags, Has.All.Match("^[0-9a-f]{32}$"));

            Attach("notes.txt", "INVENTED");
            Assert.That(Seen(none).Tag, Is.EqualTo(tags[0]), "bytes changed, to a view holding no attachment");
            Assert.That(Seen(listed).Tag, Is.EqualTo(tags[1]), "bytes changed at the same size, to a view listing them");
            Assert.That(Seen(read).Tag, Is.Not.EqualTo(tags[2]), "bytes changed, to a view reading them");
            tags[2] = Seen(read).Tag;

            var changes = new List<KeyValuePair<string, Action>>
            {
                Change("an attachment added", () => Attach("another.txt", "invented")),
                Change("an attachment renamed", () => Rename(_entry, "notes.txt", "renamed.txt")),
                Change("an attachment's size", () => Attach("renamed.txt", "invented, and longer")),
                Change("an attachment taken off", () => _entry.Binaries.Remove("another.txt")),
            };

            foreach (var change in changes)
            {
                change.Value();

                Assert.That(Seen(none).Tag, Is.EqualTo(tags[0]), change.Key + " moved the tag of a view holding none");
                Assert.That(Seen(listed).Tag, Is.Not.EqualTo(tags[1]), change.Key + " left the tag of a view listing them");
                Assert.That(Seen(read).Tag, Is.Not.EqualTo(tags[2]), change.Key + " left the tag of a view reading them");
                tags[1] = Seen(listed).Tag;
                tags[2] = Seen(read).Tag;
            }

            // With every attachment gone the three views hold the same, so they are one tag: a
            // rule widened over an entry where nothing new comes into sight moves no tag.
            _entry.Binaries.Clear();
            Assert.That(Seen(none).Tag, Is.EqualTo(tags[0]));
            Assert.That(Seen(listed).Tag, Is.EqualTo(tags[0]), "no attachment listed is no attachment in the view");
            Assert.That(Seen(read).Tag, Is.EqualTo(tags[0]), "and no attachment to read is the same");
        }

        [Test]
        public void An_attachment_larger_than_the_gate_serves_is_tagged_alike_listed_and_read()
        {
            // Its bytes are in neither view, so its name and size are all either holds of it.
            _entry.Binaries.Set("notes.txt", new ProtectedBinary(true, Filled(VaultViewAttachment.MostServed + 1, 7)));

            var none = new VaultSight(true, true, VaultAttachmentSight.None);
            var listed = new VaultSight(true, true, VaultAttachmentSight.Listed);

            Assert.That(Seen(listed).Tag, Is.EqualTo(EntryTag()));
            Assert.That(Seen(none).Tag, Is.Not.EqualTo(EntryTag()), "while a view holding no attachment is another");
        }

        [Test]
        public void A_view_whose_sight_does_not_read_has_no_tag_and_nothing_read_reaches_into_it()
        {
            // Who is handed a tag is decided by the view: a sight without `read` has none, and
            // is built reaching nothing `read` opens, whatever it was handed.
            var blind = new VaultSight(false, true, VaultAttachmentSight.Read);
            Assert.That(blind.Reads, Is.False);
            Assert.That(blind.Protected, Is.False);
            Assert.That(blind.Attachments, Is.EqualTo(VaultAttachmentSight.None));

            // `list` and `attachments` with no `read`: the listing of attachments and no tag.
            var lister = new VaultSight(false, false, VaultAttachmentSight.Listed);
            var view = Seen(lister);
            Assert.That(view.Attachments, Has.Count.EqualTo(1));
            Assert.That(view.Attachment("notes.txt").Content, Is.Null);
            Assert.That(view.Tag, Is.Null);

            // And no value of any string, protected or not: names and flags are what a
            // caller that only lists is served.
            foreach (var field in view.Fields)
            {
                Assert.That(field.IsWithheld, Is.True, field.Name);
                Assert.That(field.Value, Is.Null, field.Name);
            }

            Assert.That(view.Fields, Has.Count.EqualTo(3));
            Assert.That(view.Characters, Is.GreaterThan(0), "measured without a value read");
            Assert.That(Seen(blind).Tag, Is.Null);

            // And the digest refuses it as well, were anything to ask it directly.
            var created = new DateTimeOffset(Moment);
            var byHand = new VaultEntryView(
                Described("A1", "B1", created, created, null),
                lister,
                Array.Empty<VaultViewField>(),
                Array.Empty<VaultViewAttachment>(),
                TestTags.Fixed.Of);
            Assert.That(TestTags.Fixed.Of(byHand), Is.Null);
            Assert.That(byHand.Tag, Is.Null);
        }

        [Test]
        public void The_sight_is_asked_once_of_the_entry_found_whatever_strings_the_entry_holds()
        {
            var asked = new List<VaultFound>();
            Func<VaultFound, VaultSight> sight = found =>
            {
                asked.Add(found);
                return Sights.Whole;
            };

            _vault.View(Uuid(_entry), sight);

            Assert.That(asked, Has.Count.EqualTo(1));
            Assert.That(asked[0].Entry.Uuid, Is.EqualTo(Uuid(_entry)));
            Assert.That(asked[0].Guarded, Is.False,
                "a view names no string, so how far its caller reads is asked whatever the entry holds");

            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, string.Empty));
            _database.Database.MemoryProtection.ProtectPassword = false;
            _vault.View(Uuid(_entry), sight);

            Assert.That(asked[1].Guarded, Is.False);
            Assert.That(asked[1].Entry.Uuid, Is.EqualTo(Uuid(_entry)));
            Assert.That(_vault.View(new PwUuid(true).ToHexString(), sight), Is.Null);
            Assert.That(asked, Has.Count.EqualTo(2), "an entry that is not there is asked nothing about");
        }

        [Test]
        public void A_whole_view_s_tag_is_the_tag_of_a_view_that_withholds_nothing_there_is()
        {
            // The tag is of what the view holds, and never of the sight it was taken with: an
            // entry with no protected string and no reach to protected values withholds nothing,
            // so it is tagged as the whole entry is, and a rule widened over an entry where
            // nothing new comes into sight moves no tag.
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, "not-a-real-password"));
            _database.Database.MemoryProtection.ProtectPassword = false;

            Assert.That(Seen(new VaultSight(true, false, VaultAttachmentSight.Read)).Tag, Is.EqualTo(EntryTag()));
        }

        [Test]
        public void A_view_s_tag_moves_with_every_part_the_view_holds()
        {
            // Views made by hand, each differing from the first in one part, so that what is
            // pinned is the digest of a view and not what a vault happens to put into a view.
            var created = new DateTimeOffset(Moment);
            var bytes = Encoding.UTF8.GetBytes("invented");

            var views = new List<KeyValuePair<string, VaultEntryView>>
            {
                Viewed("the first", "A1", "B1", created, created, null, false, "a value", "notes.txt", bytes),
                Viewed("the identifier", "A2", "B1", created, created, null, false, "a value", "notes.txt", bytes),
                Viewed("the group", "A1", "B2", created, created, null, false, "a value", "notes.txt", bytes),
                Viewed("when it was created", "A1", "B1", created.AddSeconds(1), created, null, false, "a value", "notes.txt", bytes),
                Viewed("when it was changed", "A1", "B1", created, created.AddSeconds(1), null, false, "a value", "notes.txt", bytes),
                Viewed("an expiry", "A1", "B1", created, created, created.AddDays(1), false, "a value", "notes.txt", bytes),
                Viewed("another expiry", "A1", "B1", created, created, created.AddDays(2), false, "a value", "notes.txt", bytes),
                Viewed("a string's protection", "A1", "B1", created, created, null, true, "a value", "notes.txt", bytes),
                Viewed("a string's value", "A1", "B1", created, created, null, false, "another value", "notes.txt", bytes),
                Viewed("an attachment's name", "A1", "B1", created, created, null, false, "a value", "other.txt", bytes),
                Viewed("an attachment's bytes", "A1", "B1", created, created, null, false, "a value", "notes.txt", Encoding.UTF8.GetBytes("changed!")),
                Viewed("an attachment's size", "A1", "B1", created, created, null, false, "a value", "notes.txt", Encoding.UTF8.GetBytes("longer than it was")),
            };

            var tags = TestTags.Fixed;
            var seen = new HashSet<string>();

            foreach (var view in views)
            {
                Assert.That(seen.Add(tags.Of(view.Value)), Is.True, view.Key + " left the tag where it was");
            }

            Assert.That(
                tags.Of(Viewed("a fraction of a second", "A1", "B1", created.AddMilliseconds(400), created, null, false, "a value", "notes.txt", bytes).Value),
                Is.EqualTo(tags.Of(views[0].Value)),
                "a moment enters as it is served, to the second");
            Assert.That(tags.Of((VaultEntryView)null), Is.Null);
        }

        [Test]
        public void A_withheld_value_enters_a_tag_as_the_same_mark_whatever_it_is()
        {
            // The digest itself, apart from any vault: two views that differ only in a value
            // neither holds are one tag, and a view holding the value is another.
            var created = new DateTimeOffset(Moment);
            var blind = new VaultSight(true, false, VaultAttachmentSight.Read);

            Func<VaultSight, string, VaultEntryView> viewed = (sight, value) => new VaultEntryView(
                Described("A1", "B1", created, created, null),
                sight,
                new[] { new VaultViewField("Password", true, () => value.Length, sight, () => value) },
                Array.Empty<VaultViewAttachment>(),
                null);

            var tags = TestTags.Fixed;

            Assert.That(tags.Of(viewed(blind, "one")), Is.EqualTo(tags.Of(viewed(blind, "another, and longer"))));
            Assert.That(tags.Of(viewed(blind, "withheld")), Is.Not.EqualTo(tags.Of(viewed(Sights.Whole, "withheld"))),
                "and a value that spells the mark is not the mark");
            Assert.That(tags.Of(viewed(Sights.Whole, "one")), Is.Not.EqualTo(tags.Of(viewed(Sights.Whole, "another"))));
        }

        [Test]
        public void What_no_route_serves_leaves_the_tag_where_it_was()
        {
            // The rules, the history, the access time and the usage count are not in what a route
            // answers, and a moment is served to the second, so a change to any of those is no
            // reason to refuse a write made against the content.
            _entry.LastModificationTime = Moment;
            var tag = EntryTag();

            _entry.CustomData.Set("KPG", Rules.Granting(("reader", "R")));
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
            var size = VaultViewAttachment.MostServed + 1;
            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(size, 1)));
            var tag = EntryTag();

            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(size, 2)));
            Assert.That(EntryTag(), Is.EqualTo(tag), "bytes no route can read or write are not hashed");

            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(size + 1, 2)));
            Assert.That(EntryTag(), Is.Not.EqualTo(tag), "and its size is still served, so it still counts");

            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(VaultViewAttachment.MostServed, 1)));
            var served = EntryTag();
            _entry.Binaries.Set("large.bin", new ProtectedBinary(true, Filled(VaultViewAttachment.MostServed, 2)));
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

            Assert.That(_vault.Field(Uuid(_entry), "Invented"), Is.Not.Null, "read as a string, as a route reads it");

            Assert.That(EntryTag(), Is.EqualTo(tag));
        }

        [Test]
        public void An_entry_s_tag_does_not_depend_on_the_culture_its_names_were_sorted_under()
        {
            // KeePass's dictionaries order their names by the culture they were compared under,
            // which is the culture the file was loaded in. The two sort these apart.
            // cspell:ignore Ärende
            var tags = new List<string>();
            var uuid = new PwUuid(true);

            foreach (var culture in new[] { "de-DE", "sv-SE" })
            {
                var entry = new PwEntry(false, false) { Uuid = uuid, CreationTime = Moment, LastModificationTime = Moment };
                var was = Thread.CurrentThread.CurrentCulture;
                try
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                    foreach (var name in new[] { "Zone", "Ärende", "Bank" })
                    {
                        entry.Strings.Set(name, new ProtectedString(false, "invented"));
                        entry.Binaries.Set(name + ".txt", new ProtectedBinary(false, Encoding.UTF8.GetBytes("invented")));
                    }
                }
                finally
                {
                    Thread.CurrentThread.CurrentCulture = was;
                }

                // Tagged through the vault, which is the only way to a tag: of the entry's view. In
                // the same group both times, and taken out again, so the two differ in nothing but
                // the order their names were sorted in.
                _group.AddEntry(entry, true);
                try
                {
                    tags.Add(_vault.EntryTag(uuid.ToHexString()));
                }
                finally
                {
                    _group.Entries.Remove(entry);
                }
            }

            Assert.That(tags[1], Is.EqualTo(tags[0]));
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
                Change("a creation time", () => _group.CreationTime = Moment.AddDays(-1)),
            };

            foreach (var change in changes)
            {
                change.Value();

                Assert.That(seen.Add(GroupTag()), Is.True, change.Key + " left the tag where it was");
            }
        }

        [Test]
        public void An_attachment_is_decrypted_for_the_first_tag_and_not_for_the_tags_after_it()
        {
            // Counted through `ProtectedBinary.ExtCrypt`, the hook KeePass decrypts a protected
            // attachment through whenever one is set, and set here only for this test. Nothing else
            // on the entry is protected in memory, so every decryption counted is the attachment's.
            var decrypted = 0;
            var was = ProtectedBinary.ExtCrypt;
            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                if (flags == PbCryptFlags.Decrypt)
                {
                    Interlocked.Increment(ref decrypted);
                }
            };

            try
            {
                _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, "not-a-real-password"));
                _entry.Binaries.Remove("notes.txt");
                Attach("counted.txt", "invented");

                var first = EntryTag();
                var once = Volatile.Read(ref decrypted);

                Assert.That(EntryTag(), Is.EqualTo(first));
                Assert.That(EntryTag(), Is.EqualTo(first));
                Assert.That(once, Is.EqualTo(1), "the first tag decrypts the attachment to hash it");
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(once), "and the tags after it hash it again");
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }
        }

        [Test]
        public void A_view_is_built_and_measured_with_no_attachment_decrypted_and_reads_a_value_when_asked_for_it()
        {
            // Counted through the same hook, which KeePass decrypts a protected attachment through
            // and, measured here, not a protected string made from a string, which KeePass keeps
            // as it was given: so what is counted is the attachment, made while the hook stands,
            // and the strings are held to the rest by what the view carries. A protected string
            // KeePass holds as bytes is the test after this. A view carries neither the title nor
            // the URL beside its fields, which is what a description of the entry would have read
            // to build it.
            var decrypted = 0;
            var was = ProtectedBinary.ExtCrypt;
            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                if (flags == PbCryptFlags.Decrypt)
                {
                    Interlocked.Increment(ref decrypted);
                }
            };

            try
            {
                _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(true, "a protected title"));
                _entry.Strings.Set(PwDefs.UrlField, new ProtectedString(true, "https://invented.example/"));
                _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "not-a-real-password"));
                Attach("notes.txt", "invented");
                Volatile.Write(ref decrypted, 0);

                var sights = new[]
                {
                    Sights.Whole,
                    new VaultSight(true, false, VaultAttachmentSight.Listed),
                    VaultSight.Least,
                };

                foreach (var view in sights.Select(Seen))
                {
                    Assert.That(view.Entry.Title, Is.Null, "a title is in a view as a field or not at all");
                    Assert.That(view.Entry.Url, Is.Null);
                    Assert.That(view.Characters, Is.GreaterThan("a protected title".Length));
                    Assert.That(
                        view.Field(PwDefs.PasswordField).Characters,
                        Is.EqualTo(PwDefs.PasswordField.Length
                            + (view.Sight.Protected ? "not-a-real-password".Length : 0)),
                        "a value is measured for a view holding it, and a withheld value has no length in a view");
                    Assert.That(view.Protected, Does.Contain(PwDefs.TitleField).And.Contain(PwDefs.PasswordField));

                    if (view.Attachments != null)
                    {
                        Assert.That(view.Attachments[0].Size, Is.EqualTo(8), "an attachment is measured too");
                    }
                }

                Assert.That(Volatile.Read(ref decrypted), Is.Zero, "building and measuring a view decrypted an attachment");

                var whole = Seen(Sights.Whole);
                Assert.That(whole.Field(PwDefs.PasswordField).Value, Is.EqualTo("not-a-real-password"));
                Assert.That(Volatile.Read(ref decrypted), Is.Zero, "reading a string read the attachment");

                Assert.That(whole.Attachment("notes.txt").Content, Has.Length.EqualTo(8));
                Assert.That(whole.Attachment("notes.txt").Content, Has.Length.EqualTo(8));
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(1), "bytes are read when asked for, and once");

                // A view that holds no bytes of the attachment reads none of them for its tag,
                // whether it lists the attachment or is shown nothing of it.
                Volatile.Write(ref decrypted, 0);
                var listed = Seen(new VaultSight(true, false, VaultAttachmentSight.Listed));
                var narrow = Seen(new VaultSight(true, false, VaultAttachmentSight.None));
                Assert.That(narrow.Field(PwDefs.TitleField).Value, Is.Null);
                Assert.That(listed.Tag, Is.Not.Null.And.Not.EqualTo(narrow.Tag));
                Assert.That(Volatile.Read(ref decrypted), Is.Zero, "a tag decrypted bytes its view withholds");
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
            }
        }

        [Test]
        public void A_protected_value_held_as_bytes_is_decrypted_once_to_be_measured_and_never_for_a_view_withholding_it()
        {
            // A file's protected values are held as bytes once loaded, and KeePass counts the
            // characters of such a value by decrypting it, the first time its length is asked,
            // and keeps the count. So building a view asks no length, a view that withholds the
            // value neither reads nor measures it, whatever is asked of the view, and the first
            // measurement by a view holding it is a decryption inside KeePass's own type.
            var decrypted = 0;
            var was = ProtectedBinary.ExtCrypt;
            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                if (flags == PbCryptFlags.Decrypt)
                {
                    Interlocked.Increment(ref decrypted);
                }
            };

            try
            {
                var secret = Encoding.UTF8.GetBytes("not-a-real-password");
                _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, secret));
                Volatile.Write(ref decrypted, 0);

                var narrow = Seen(new VaultSight(true, false, VaultAttachmentSight.None));
                Assert.That(narrow.Field(PwDefs.PasswordField).Characters, Is.EqualTo(PwDefs.PasswordField.Length));
                Assert.That(narrow.Characters, Is.GreaterThan(0));
                Assert.That(narrow.Field(PwDefs.PasswordField).Value, Is.Null);
                Assert.That(narrow.Tag, Is.Not.Null);
                Assert.That(Volatile.Read(ref decrypted), Is.Zero, "a withheld value was read or measured");

                var whole = Seen(Sights.Whole);
                Assert.That(whole.NameCharacters, Is.GreaterThan(0));
                Assert.That(Volatile.Read(ref decrypted), Is.Zero, "building a view measured a value");

                Assert.That(
                    whole.Field(PwDefs.PasswordField).Characters,
                    Is.EqualTo(PwDefs.PasswordField.Length + "not-a-real-password".Length));
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(1), "KeePass decrypts the value to count it");

                Assert.That(Seen(Sights.Whole).Characters, Is.GreaterThan("not-a-real-password".Length));
                Assert.That(Volatile.Read(ref decrypted), Is.EqualTo(1), "and keeps the count");
            }
            finally
            {
                ProtectedBinary.ExtCrypt = was;
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
        public void A_refusal_names_whether_the_node_changed_or_the_condition_could_never_be_met()
        {
            var stale = _vault.UpdateEntry(
                Uuid(_entry), Fields(PwDefs.UserNameField, "written by a client"), Asking("0123456789abcdef0123456789abcdef"));
            Assert.That(stale.IsPreconditionFailed, Is.True);
            Assert.That(stale.Reason, Does.Contain("no longer holds"));

            // A client the gate hands no tag, which the operator would otherwise go looking for an
            // edit to the entry over.
            var withheld = _vault.UpdateEntry(
                Uuid(_entry), Fields(PwDefs.UserNameField, "written by a client"),
                VaultAskings.Tagged(VaultPrecondition.Withheld(), false));
            Assert.That(withheld.IsPreconditionFailed, Is.True);
            Assert.That(withheld.Reason, Is.EqualTo(VaultPrecondition.Withheld().Refusal));
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo("an invented name"));
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

            var unasked = _vault.UpdateField(Uuid(_entry), PwDefs.TitleField, "renamed again", VaultAskings.Free);
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
        public void Adding_a_field_or_an_attachment_and_replacing_an_attachment_hand_back_the_entry_s_tag()
        {
            // The answer a client writes again under, so it is the tag a read then gives.
            var field = _vault.CreateField(Uuid(_entry), "Invented", "a value", false, Asking(EntryTag()));
            Assert.That(field.Ok, Is.True, field.Reason);
            Assert.That(field.Tag, Is.Not.Null.And.EqualTo(EntryTag()));

            var added = _vault.CreateAttachment(Uuid(_entry), "added.txt", Filled(3, 1), Asking(EntryTag()));
            Assert.That(added.Ok, Is.True, added.Reason);
            Assert.That(added.Tag, Is.Not.Null.And.EqualTo(EntryTag()));

            var replaced = _vault.UpdateAttachment(Uuid(_entry), "added.txt", Filled(3, 2), Asking(EntryTag()));
            Assert.That(replaced.Ok, Is.True, replaced.Reason);
            Assert.That(replaced.Tag, Is.Not.Null.And.EqualTo(EntryTag()));
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
        private static VaultAsking Asking(string tag)
        {
            return VaultAskings.Tagged(VaultPrecondition.Read("\"" + tag + "\""), true);
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

        /// <summary>The entry as a caller with this sight may read it.</summary>
        private VaultEntryView Seen(VaultSight sight)
        {
            return _vault.View(Uuid(_entry), found => sight);
        }

        /// <summary>
        /// An entry described by hand, with no rules behind it, for a view made by hand.
        /// </summary>
        private static VaultEntry Described(
            string uuid, string group, DateTimeOffset created, DateTimeOffset modified, DateTimeOffset? expires)
        {
            return new VaultEntry(
                uuid, group, "an invented service", string.Empty, created, modified, null, false, false, expires);
        }

        /// <summary>
        /// The whole of an invented entry holding one string and one attachment, named for the
        /// part it differs in.
        /// </summary>
        private static KeyValuePair<string, VaultEntryView> Viewed(
            string what,
            string uuid,
            string group,
            DateTimeOffset created,
            DateTimeOffset modified,
            DateTimeOffset? expires,
            bool guarded,
            string value,
            string attachment,
            byte[] bytes)
        {
            Func<byte[]> digest = () =>
            {
                using (var sha256 = SHA256.Create())
                {
                    return sha256.ComputeHash(bytes);
                }
            };

            var view = new VaultEntryView(
                Described(uuid, group, created, modified, expires),
                Sights.Whole,
                new[] { new VaultViewField("Invented", guarded, () => value.Length, Sights.Whole, () => value) },
                new[] { new VaultViewAttachment(attachment, bytes.Length, Sights.Whole, () => bytes, digest) },
                null);

            return new KeyValuePair<string, VaultEntryView>(what, view);
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
            // codeql[cs/local-not-disposed] `KdbxFile` disposes the stream it is handed, as KeePass 2.61.1 does
            var file = new MemoryStream();
            new KdbxFile(database).Save(file, null, KdbxFormat.Default, null);

            var copy = new PwDatabase { MasterKey = database.MasterKey, RootGroup = new PwGroup(true, true) };
            // codeql[cs/local-not-disposed] `KdbxFile` disposes the stream it is handed, as KeePass 2.61.1 does
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

            public string Stalled
            {
                get { return null; }
            }

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
