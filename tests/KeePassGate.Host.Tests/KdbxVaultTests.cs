using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Utility;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The API's view of a database, against a real one.
    /// </summary>
    /// <remarks>
    /// Everything the handlers do goes through `IVault`, and this is the only implementation that
    /// knows what a `PwEntry` is. So this is where the questions only KeePass can answer get asked:
    /// that a written field is really on the entry, that a delete honors the recycle bin, and that
    /// a change survives the file rather than only the object graph.
    ///
    /// Everything in here is invented. No group, entry, field or attachment name resembles anything
    /// out of anybody's real database.
    /// </remarks>
    [TestFixture]
    public sealed class KdbxVaultTests
    {
        private TemporaryDatabase _database;
        private IVault _vault;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _saves = 0;
            _vault = new KdbxVault(_database.Database, Writer(), null);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void The_root_group_answers_to_a_name_as_well_as_to_an_identifier()
        {
            // Which is what lets a client holding `create` and nothing else put an entry somewhere
            // without first being granted `list` to find out where.
            var byName = _vault.Group(_vault.RootAlias);
            var byId = _vault.Group(_database.RootGroup.Uuid.ToHexString());

            Assert.That(byName, Is.Not.Null);
            Assert.That(byName.Uuid, Is.EqualTo(byId.Uuid));
            Assert.That(byName.Parent, Is.Null, "the root is the one group with nothing above it");
        }

        [Test]
        public void An_identifier_that_is_not_one_finds_nothing_rather_than_failing()
        {
            foreach (var nonsense in new[] { "", "..", "nope", "7b1c", new string('z', 32) })
            {
                Assert.That(_vault.Group(nonsense), Is.Null, nonsense);
                Assert.That(_vault.Entry(nonsense), Is.Null, nonsense);
            }
        }

        [Test]
        public void A_group_holds_what_was_put_in_it_and_the_tree_holds_all_of_it()
        {
            var outer = Created(_vault.CreateGroup(_vault.RootAlias, "Outer", null));
            var inner = Created(_vault.CreateGroup(outer, "Inner", "invented"));

            Assert.That(_vault.Subgroups(outer).Select(g => g.Uuid), Is.EqualTo(new[] { inner }));
            Assert.That(_vault.Subgroups(inner), Is.Empty);

            var all = _vault.Groups().Select(g => g.Name).ToList();

            Assert.That(all, Does.Contain("Outer").And.Contain("Inner"));
            Assert.That(all.First(), Is.EqualTo(_database.RootGroup.Name), "the root comes first");
        }

        [Test]
        public void An_entry_lands_where_it_was_asked_to_and_carries_what_it_was_given()
        {
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));

            var uuid = Created(_vault.CreateEntry(group, new Dictionary<string, string>
            {
                { PwDefs.TitleField, "an invented service" },
                { PwDefs.UserNameField, "agent-fictitious" },
                { PwDefs.PasswordField, "not-a-real-password" },
            }));

            var entry = _vault.Entry(uuid);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Group, Is.EqualTo(group));
            Assert.That(entry.Title, Is.EqualTo("an invented service"));

            var read = _vault.Read(uuid);

            Assert.That(read.Fields[PwDefs.UserNameField], Is.EqualTo("agent-fictitious"));
            Assert.That(read.Protected, Does.Contain(PwDefs.PasswordField),
                "a password is protected because this database says that kind of field is");
        }

        [Test]
        public void Listing_a_group_is_one_level_and_listing_the_database_is_all_of_them()
        {
            var outer = Created(_vault.CreateGroup(_vault.RootAlias, "Outer", null));
            var inner = Created(_vault.CreateGroup(outer, "Inner", null));

            _vault.CreateEntry(outer, Named("first"));
            _vault.CreateEntry(inner, Named("second"));

            Assert.That(_vault.Entries(outer).Select(e => e.Title), Is.EqualTo(new[] { "first" }));
            Assert.That(_vault.Entries().Select(e => e.Title), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void Fields_are_named_without_being_quoted()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            _vault.CreateField(uuid, "Invented", "a value", false);

            var fields = _vault.Fields(uuid);

            Assert.That(fields.Select(f => f.Name), Does.Contain("Invented"));
            Assert.That(fields.Single(f => f.Name == PwDefs.PasswordField).IsProtected, Is.True);
            Assert.That(_vault.Field(uuid, "Invented"), Is.EqualTo("a value"));
            Assert.That(_vault.Field(uuid, "NotThere"), Is.Null);
        }

        [Test]
        public void A_field_is_created_once_and_changed_afterwards()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            Assert.That(_vault.CreateField(uuid, "Invented", "first", false).Ok, Is.True);
            Assert.That(_vault.CreateField(uuid, "Invented", "again", false).IsConflict, Is.True,
                "create adds what is not there, and says so when it already is");

            Assert.That(_vault.UpdateField(uuid, "Invented", "second").Ok, Is.True);
            Assert.That(_vault.Field(uuid, "Invented"), Is.EqualTo("second"));

            Assert.That(_vault.UpdateField(uuid, "NotThere", "x").Ok, Is.False,
                "and update changes what is there, and says nothing is when it is not");
        }

        [Test]
        public void Changing_a_protected_field_leaves_it_protected()
        {
            // Whether a field is protected is something the operator arranged. Writing a value is
            // not the moment to decide it differently.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            _vault.UpdateField(uuid, PwDefs.PasswordField, "a different one");

            Assert.That(_vault.Read(uuid).Protected, Does.Contain(PwDefs.PasswordField));
        }

        [Test]
        public void A_standard_field_the_database_protects_is_reported_protected_whatever_the_string_says()
        {
            // The two halves of one rule, which used to disagree in the direction that leaks. A
            // `.kdbx` written by something other than KeePass can hold a standard string with no
            // protection flag while the database's `MemoryProtection` protects that name, and
            // KeePass only normalizes the five standard names on its own next save. Until then the
            // write side refused the field and the read side handed the value over in clear,
            // reporting it as never having been a secret.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(_vault.Protects(PwDefs.PasswordField), Is.True, "this database protects passwords");

            Unflag(uuid, PwDefs.PasswordField, "not-a-real-password");

            Assert.That(_vault.Read(uuid).Protected, Does.Contain(PwDefs.PasswordField),
                "the database protects the name, so the value is a secret however it was stored");
            Assert.That(_vault.Fields(uuid).Single(f => f.Name == PwDefs.PasswordField).IsProtected, Is.True,
                "and the field listing, which is what the field route reads, has to agree");
        }

        [Test]
        public void A_title_the_database_protects_is_withheld_from_a_listing_whatever_the_string_says()
        {
            // The listing half. A title and a URL are the two values `list` alone hands over, so
            // they are the two a rule without the `protected` scope must not be given, and both
            // are answered for here rather than by the handler.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            _database.Database.MemoryProtection.ProtectTitle = true;
            _database.Database.MemoryProtection.ProtectUrl = true;

            Assert.That(_vault.Entry(uuid).TitleProtected, Is.True);
            Assert.That(_vault.Entry(uuid).UrlProtected, Is.True);
        }

        [Test]
        public void A_read_answers_the_strings_in_the_order_KeePass_keeps_them()
        {
            // The reference client replaces an entry's own placeholders in the order the gate
            // lists them, and which of two goes first decides the answer.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            foreach (var name in new[] { "Zeta brace", "Brace", "middle", "Alpha" })
            {
                Assume.That(_vault.CreateField(uuid, name, "a value", false).Ok, Is.True);
            }

            var entry = _database.Database.RootGroup.FindEntry(new PwUuid(KeePassLib.Utility.MemUtil.HexStringToByteArray(uuid)), true);
            Assume.That(entry, Is.Not.Null);

            Assert.That(_vault.Read(uuid).Fields.Keys, Is.EqualTo(entry.Strings.GetKeys()));
        }

        [Test]
        public void A_custom_field_is_protected_only_when_the_string_says_so()
        {
            // The other half of the same rule, and why the flag term stays. The database says
            // nothing about a name it does not know, so a custom field's protection is whatever
            // the operator marked when it was created and nothing else.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            Assert.That(_vault.CreateField(uuid, "Invented", "a value", false).Ok, Is.True);
            Assert.That(_vault.CreateField(uuid, "AlsoInvented", "a value", true).Ok, Is.True);

            Assert.That(_vault.Read(uuid).Protected, Has.No.Member("Invented"));
            Assert.That(_vault.Read(uuid).Protected, Does.Contain("AlsoInvented"));
        }

        /// <summary>
        /// Stores a standard string with no protection flag, which is a state this plugin never
        /// writes and a `.kdbx` from other software can hold.
        /// </summary>
        private void Unflag(string uuid, string name, string value)
        {
            Entry(uuid).Strings.Set(name, new KeePassLib.Security.ProtectedString(false, value));
            Assume.That(Entry(uuid).Strings.Get(name).IsProtected, Is.False);
        }

        [Test]
        public void A_password_stored_under_a_name_the_database_reads_as_the_password_is_protected()
        {
            // The other direction of the alias class, and it arrives rather than being made here:
            // KeePass's loader sets an entry's strings one at a time and the dictionary keeps
            // whichever key it saw first, so a file whose look-alike element precedes the password
            // element carries the password under the look-alike key, through saves and reopens.
            // KeePass reads it as the password; this plugin asked byte for byte and read a custom
            // field carrying no flag, and handed it to every client with `read` and no `protected`.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var entry = Entry(uuid);

            entry.Strings.Remove(PwDefs.PasswordField);
            entry.Strings.Set(PasswordAlias, new KeePassLib.Security.ProtectedString(false, "not-a-real-password"));

            Assume.That(entry.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo("not-a-real-password"),
                "KeePass itself reads this as the entry's password");

            Assert.That(_vault.Protects(PasswordAlias), Is.True);
            Assert.That(_vault.Read(uuid).Protected, Does.Contain(PasswordAlias));
            Assert.That(_vault.Fields(uuid).Single(f => f.Name == PasswordAlias).IsProtected, Is.True,
                "and the field listing, which is what the field route reads, has to agree");
        }

        [Test]
        public void A_field_can_be_taken_off_again()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            _vault.CreateField(uuid, "Invented", "a value", false);

            Assert.That(_vault.DeleteField(uuid, "Invented").Ok, Is.True);
            Assert.That(_vault.Field(uuid, "Invented"), Is.Null);
            Assert.That(_vault.DeleteField(uuid, "Invented").Ok, Is.False);
        }

        [Test]
        public void An_attachment_goes_in_whole_and_comes_out_whole()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var content = Encoding.UTF8.GetBytes("an invented attachment, several bytes long");

            Assert.That(_vault.CreateAttachment(uuid, "notes..txt", content).Ok, Is.True);

            var listed = _vault.Attachments(uuid).Single();

            Assert.That(listed.Name, Is.EqualTo("notes..txt"));
            Assert.That(listed.Size, Is.EqualTo(content.Length));
            Assert.That(Stored(uuid, "notes..txt"), Is.EqualTo(content));
        }

        [Test]
        public void An_attachment_larger_than_asked_for_comes_back_measured_and_not_read()
        {
            // KeePass keeps an attachment's length beside its encrypted bytes, so the size is
            // answered without the plaintext, which is the copy a refusal for size is there to
            // avoid, and in the same read, so nothing can replace it in between.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(_vault.CreateAttachment(uuid, "big.bin", new byte[] { 1, 2, 3, 4 }).Ok, Is.True);

            var measured = _vault.Attachment(uuid, "big.bin", 3);

            Assert.That(measured.Size, Is.EqualTo(4));
            Assert.That(measured.Content, Is.Null);
            Assert.That(_vault.Attachment(uuid, "big.bin", 4).Content, Is.EqualTo(new byte[] { 1, 2, 3, 4 }),
                "and one no larger than that is read");
        }

        [Test]
        public void An_attachment_is_created_once_and_replaced_afterwards()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            Assert.That(_vault.CreateAttachment(uuid, "one", new byte[] { 1 }).Ok, Is.True);
            Assert.That(_vault.CreateAttachment(uuid, "one", new byte[] { 2 }).IsConflict, Is.True);

            Assert.That(_vault.UpdateAttachment(uuid, "one", new byte[] { 3, 4 }).Ok, Is.True);
            Assert.That(Stored(uuid, "one"), Is.EqualTo(new byte[] { 3, 4 }));

            Assert.That(_vault.DeleteAttachment(uuid, "one").Ok, Is.True);
            Assert.That(_vault.Attachments(uuid), Is.Empty);
        }

        /// <summary>
        /// A spelling of `Password` the database's own dictionary cannot tell from the real one.
        /// </summary>
        /// <remarks>
        /// U+00AD, the soft hyphen. KeePass keeps an entry's strings in a `SortedDictionary` under
        /// the default comparer, which compares by the current culture, and the collation gives
        /// this character no weight: measured against the pinned KeePass, `Strings.Exists` answers
        /// true for this on an entry that carries only `Password`. U+200D, U+2060 and U+FEFF
        /// behave the same way, and so does a name written in the other normal form, which is
        /// why the second alias below is a `Cafe` whose accent is a combining character.
        /// </remarks>
        private const string PasswordAlias = "Pass\u00ADword";

        [Test]
        public void A_field_name_the_database_cannot_tell_from_another_reaches_neither()
        {
            // The guards above this compare names byte for byte and the store compares them by the
            // culture, so a name that is a custom field to one and the password to the other was
            // applied to the password: `update` without `protected` overwrote it, and `strip` then
            // `create` took the real one off and left an unprotected value where KeePass reads the
            // password from.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            _vault.UpdateField(uuid, PwDefs.PasswordField, "not-a-real-password");

            Assume.That(Entry(uuid).Strings.Exists(PasswordAlias), Is.True,
                "the database itself cannot tell the two names apart, which is what this is about");

            Assert.That(_vault.Field(uuid, PasswordAlias), Is.Null, "reading it finds nothing");
            Assert.That(_vault.UpdateField(uuid, PasswordAlias, "planted").Ok, Is.False, "writing it");
            Assert.That(_vault.DeleteField(uuid, PasswordAlias).Ok, Is.False, "taking it off");
            Assert.That(_vault.CreateField(uuid, PasswordAlias, "planted", false).IsConflict, Is.True,
                "and putting one there is the conflict a name the entry already has gets");

            Assert.That(Entry(uuid).Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo("not-a-real-password"));
            Assert.That(_vault.Read(uuid).Protected, Does.Contain(PwDefs.PasswordField));
        }

        [Test]
        public void An_attachment_name_the_database_cannot_tell_from_another_reaches_neither()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            _vault.CreateAttachment(uuid, "notes.txt", new byte[] { 1, 2, 3 });

            var alias = "notes\u00AD.txt";
            Assume.That(Entry(uuid).Binaries.Get(alias), Is.Not.Null, "the same collation, on the other dictionary");

            Assert.That(_vault.Attachment(uuid, alias, int.MaxValue), Is.Null);
            Assert.That(_vault.UpdateAttachment(uuid, alias, new byte[] { 9 }).Ok, Is.False);
            Assert.That(_vault.DeleteAttachment(uuid, alias).Ok, Is.False);
            Assert.That(_vault.CreateAttachment(uuid, alias, new byte[] { 9 }).IsConflict, Is.True);

            Assert.That(Stored(uuid, "notes.txt"), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void An_entry_route_refuses_a_whole_body_that_carries_such_a_name()
        {
            // The entry routes take a map rather than one name, so they ask about all of it before
            // writing any of it: a body holding both spellings would otherwise have the second
            // land on the first's key, and the guard was asked about the second.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var aliased = new Dictionary<string, string> { { PasswordAlias, "planted" } };

            Assert.That(_vault.UpdateEntry(uuid, aliased).IsConflict, Is.True);
            Assert.That(Entry(uuid).Strings.ReadSafe(PwDefs.PasswordField), Is.Empty,
                "and nothing of the body reached the entry");

            Assert.That(_vault.CreateEntry(_vault.RootAlias, aliased).IsConflict, Is.True);
            Assert.That(_vault.Entries(_vault.RootAlias).Count, Is.EqualTo(1), "nor was an entry left behind");
        }

        [Test]
        public void Two_spellings_of_one_custom_name_in_one_body_are_refused_together()
        {
            // Neither is stored yet, so neither collides with the entry: they collide with each
            // other, and the store would keep whichever arrived first.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var both = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Caf\u00E9", "one" },
                { "Cafe\u0301", "another" },
            };

            Assert.That(_vault.UpdateEntry(uuid, both).IsConflict, Is.True);
            Assert.That(_vault.Fields(uuid).Select(f => f.Name), Has.No.Member("Caf\u00E9"));
        }

        [Test]
        public void A_name_that_could_not_travel_in_a_path_is_still_a_name_here()
        {
            // The vault stores whatever a database can hold. What can be addressed is the routing
            // table's business, and after the path fix that includes a name with a separator in it.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            Assert.That(_vault.CreateAttachment(uuid, "a/b", new byte[] { 1 }).Ok, Is.True);
            Assert.That(Stored(uuid, "a/b"), Is.Not.Null);
        }

        [Test]
        public void Deleting_a_group_that_still_holds_something_is_refused()
        {
            // A recursive delete would destroy entries that were never individually decided or
            // recorded, on the strength of one grant on a parent.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            _vault.CreateEntry(group, Named("an invented service"));

            var refused = _vault.DeleteGroup(group);

            Assert.That(refused.IsConflict, Is.True);
            Assert.That(_vault.Group(group), Is.Not.Null);
        }

        [Test]
        public void The_root_group_cannot_be_removed()
        {
            Assert.That(_vault.DeleteGroup(_vault.RootAlias).IsConflict, Is.True);
            Assert.That(_vault.Group(_vault.RootAlias), Is.Not.Null);
        }

        [Test]
        public void An_empty_group_goes()
        {
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));

            Assert.That(_vault.DeleteGroup(group).Ok, Is.True);
            Assert.That(_vault.Group(group), Is.Null);
        }

        [Test]
        public void A_deleted_entry_goes_to_the_recycle_bin_and_out_of_this_API_s_reach()
        {
            // Two halves, and the second is the decision. KeePass keeps the entry, which is what a
            // recycle bin is for and is why an operator can get it back. This API stops seeing it,
            // because the bin is where a removal moves things to and a destination a client can
            // reach is a way to read what it may not read by removing it first.
            _database.Database.RecycleBinEnabled = true;
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = Created(_vault.CreateEntry(group, Named("an invented service")));

            Assert.That(_vault.DeleteEntry(uuid).Ok, Is.True);

            var kept = _database.Database.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(uuid)), true);

            Assert.That(kept, Is.Not.Null, "it is still in the database, which is what a recycle bin is");
            Assert.That(_vault.Entry(uuid), Is.Null, "and this API cannot see it there");
            Assert.That(_vault.Entries(group), Is.Empty);
        }

        [Test]
        public void The_recycle_bin_is_not_a_group_this_API_lists_or_addresses()
        {
            _database.Database.RecycleBinEnabled = true;
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = Created(_vault.CreateEntry(group, Named("an invented service")));
            Assert.That(_vault.DeleteEntry(uuid).Ok, Is.True);

            var bin = _database.Database.RootGroup.FindGroup(_database.Database.RecycleBinUuid, true);

            Assert.That(bin, Is.Not.Null, "KeePass made one to put the entry in");
            Assert.That(_vault.Groups().Select(g => g.Uuid), Has.No.Member(Hex(bin.Uuid)),
                "and this API does not list it");
            Assert.That(_vault.Subgroups(_vault.RootAlias).Select(g => g.Uuid), Has.No.Member(Hex(bin.Uuid)),
                "nor as a child of the root, which is where KeePass puts it");
            Assert.That(_vault.Group(Hex(bin.Uuid)), Is.Null, "nor answer for it by name");
            Assert.That(_vault.Entries(Hex(bin.Uuid)), Is.Null, "nor say what is in it, even that it is empty");
        }

        [Test]
        public void A_listing_of_something_not_there_is_null_rather_than_empty()
        {
            // An empty list is an answer about a group or an entry, that it holds nothing. One that
            // is not there holds nothing for a client to be told about, and answering it as empty is
            // what made a group removed under a request read as an empty group.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var nowhere = new string('a', 32);

            Assert.That(_vault.Subgroups(group), Is.Empty, "a group with nothing in it is empty");
            Assert.That(_vault.Entries(group), Is.Empty);
            Assert.That(_vault.Attachments(uuid), Is.Empty, "and so is an entry with no attachments");

            Assert.That(_vault.Subgroups(nowhere), Is.Null);
            Assert.That(_vault.Entries(nowhere), Is.Null);
            Assert.That(_vault.Fields(nowhere), Is.Null);
            Assert.That(_vault.Attachments(nowhere), Is.Null);
            Assert.That(_vault.IsOpen, Is.True, "and the database is open, so these are absences");
        }

        [Test]
        public void A_closed_database_says_so_and_lists_nothing()
        {
            // What a lock does underneath a request: KeePass closes the database the vault reads,
            // every lookup finds nothing, and this is what tells that apart from a node that has
            // gone. Closed the way a lock closes it, and opened again the way an unlock does.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(_vault.IsOpen, Is.True);

            _database.Lock();

            Assert.That(_vault.IsOpen, Is.False);
            Assert.That(_vault.Groups(), Is.Null, "no tree to read is not an empty one");
            Assert.That(_vault.Entries(), Is.Null);
            Assert.That(_vault.Subgroups(_vault.RootAlias), Is.Null);
            Assert.That(_vault.Entries(_vault.RootAlias), Is.Null);
            Assert.That(_vault.Fields(uuid), Is.Null);

            _database.Unlock();

            Assert.That(_vault.IsOpen, Is.True, "the same object, open again, as after an unlock");
            Assert.That(_vault.Fields(uuid), Is.Not.Empty);
        }

        /// <summary>A node's identifier as this API spells it, which is what the tests compare.</summary>
        private static string Hex(PwUuid uuid)
        {
            return uuid.ToHexString();
        }

        [Test]
        public void A_deleted_entry_is_gone_when_the_database_keeps_no_bin()
        {
            _database.Database.RecycleBinEnabled = false;
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            Assert.That(_vault.DeleteEntry(uuid).Ok, Is.True);
            Assert.That(_vault.Entry(uuid), Is.Null);
            Assert.That(_database.Database.DeletedObjects.UCount, Is.GreaterThan(0u),
                "and it is written down as deleted, so a merge does not bring it back");
        }

        [Test]
        public void An_update_naming_nothing_changes_nothing_and_saves_nothing()
        {
            // It used to be a full write: nothing was set, and `Touch` moved the modification time,
            // the access time and the usage count anyway, with a history revision taken before it
            // and the whole database saved after it. So a client holding `update` and no other
            // verb could push every real revision of an entry out of its history, ten calls at the
            // default `HistoryMaxItems`, and be told `200` each time.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = Created(_vault.CreateEntry(group, Named("an invented service")));
            _vault.UpdateEntry(uuid, Named("renamed"));

            var entry = _database.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(uuid)), true);
            var modified = entry.LastModificationTime;
            var used = entry.UsageCount;
            var revisions = entry.History.UCount;
            var saves = _saves;

            Assert.That(_vault.UpdateEntry(uuid, new Dictionary<string, string>()).Ok, Is.True,
                "an update that asks for nothing is not a refusal: the entry holds what it asked");
            Assert.That(_vault.UpdateGroup(group, null, null).Ok, Is.True);

            Assert.That(entry.LastModificationTime, Is.EqualTo(modified));
            Assert.That(entry.UsageCount, Is.EqualTo(used));
            Assert.That(entry.History.UCount, Is.EqualTo(revisions), "and it keeps its own history");
            Assert.That(_saves, Is.EqualTo(saves), "nothing changed, so there was nothing to save");
        }

        [Test]
        public void An_update_naming_nothing_still_answers_for_a_node_that_is_not_there()
        {
            // The lookup is what keeps this from being an `ok` about nothing at all. A node that is
            // not there gets what every other route gives it.
            Assert.That(_vault.UpdateEntry(new string('a', 32), new Dictionary<string, string>()).Ok,
                Is.False);
            Assert.That(_vault.UpdateGroup(new string('a', 32), null, null).Ok, Is.False);
        }

        [Test]
        public void An_update_naming_only_what_is_already_there_changes_nothing_and_saves_nothing()
        {
            // The same request as one naming nothing, made by an agent writing back what it has
            // just read to be sure of it. It took a revision of the entry on every call, so
            // `update` alone pushed the real ones out of the history again, one call at a time.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", "invented notes"));
            var uuid = Created(_vault.CreateEntry(group, Named("an invented service"), VaultExpiry.At(Moment)));
            Assume.That(_vault.CreateField(uuid, "Invented", "a value", false).Ok, Is.True);
            Assume.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "not-a-real-password").Ok, Is.True);
            Assume.That(_vault.CreateAttachment(uuid, "note.txt", new byte[] { 1, 2, 3 }).Ok, Is.True);

            var entry = Entry(uuid);
            var parent = entry.ParentGroup;
            var modified = entry.LastModificationTime;
            var used = entry.UsageCount;
            var revisions = entry.History.UCount;
            var groupModified = parent.LastModificationTime;
            var groupUsed = parent.UsageCount;
            var saves = _saves;
            Assume.That(_database.Database.Modified, Is.False, "every write so far was saved");

            Assert.That(_vault.UpdateEntry(uuid, Named("an invented service"), VaultExpiry.At(Moment)).Ok, Is.True,
                "an entry");
            Assert.That(_vault.UpdateField(uuid, "Invented", "a value").Ok, Is.True, "a field");
            Assert.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "not-a-real-password").Ok, Is.True,
                "a protected one, compared without being handed out");
            Assert.That(_vault.UpdateAttachment(uuid, "note.txt", new byte[] { 1, 2, 3 }).Ok, Is.True,
                "an attachment");
            Assert.That(_vault.UpdateGroup(group, "Fictitious", "invented notes").Ok, Is.True, "a group");
            Assert.That(_vault.UpdateGroup(group, "Fictitious", null).Ok, Is.True, "and half of one");

            Assert.That(entry.History.UCount, Is.EqualTo(revisions), "no revision is added");
            Assert.That(entry.LastModificationTime, Is.EqualTo(modified));
            Assert.That(entry.UsageCount, Is.EqualTo(used));
            Assert.That(parent.LastModificationTime, Is.EqualTo(groupModified));
            Assert.That(parent.UsageCount, Is.EqualTo(groupUsed));
            Assert.That(_saves, Is.EqualTo(saves), "nothing changed, so there was nothing to save");
            Assert.That(_database.Database.Modified, Is.False, "and nothing is waiting to be saved either");
        }

        [Test]
        public void An_update_naming_one_value_that_is_not_there_writes_the_whole_of_it()
        {
            // The rule is about the request, not about each value in it: one value the node does not
            // hold makes it a change, and a change writes everything it names and keeps a revision.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service"), VaultExpiry.At(Moment)));
            var saves = _saves;
            var asked = new Dictionary<string, string>
            {
                { PwDefs.TitleField, "an invented service" },
                { "Invented", "a value" },
            };

            Assert.That(_vault.UpdateEntry(uuid, asked, VaultExpiry.At(Moment)).Ok, Is.True,
                "a name the entry does not carry is a string to add");
            Assert.That(_vault.UpdateEntry(uuid, Named("an invented service"), VaultExpiry.At(Moment.AddSeconds(1))).Ok,
                Is.True, "and a moment it does not expire at is one to move to");

            Assert.That(Entry(uuid).Strings.ReadSafe("Invented"), Is.EqualTo("a value"));
            Assert.That(Entry(uuid).ExpiryTime, Is.EqualTo(Moment.AddSeconds(1).UtcDateTime));
            Assert.That(Entry(uuid).History.UCount, Is.EqualTo(2u), "one revision for each change");
            Assert.That(_saves, Is.EqualTo(saves + 2));
            Assert.That(_database.Database.Modified, Is.False);
        }

        [Test]
        public void An_expiry_is_already_there_only_when_it_is_the_one_in_force()
        {
            // KeePass keeps a moment for an entry that does not expire as well, so the flag is what
            // decides: no expiry is already there whatever moment is kept, and a moment is already
            // there only while the entry expires at it.
            var never = Created(_vault.CreateEntry(_vault.RootAlias, Named("never")));
            var dated = Created(_vault.CreateEntry(_vault.RootAlias, Named("dated"), VaultExpiry.At(Moment)));
            Entry(never).ExpiryTime = Moment.UtcDateTime;
            var none = new Dictionary<string, string>();
            var saves = _saves;

            Assert.That(_vault.UpdateEntry(never, none, VaultExpiry.Never).Ok, Is.True);
            Assert.That(_vault.UpdateEntry(dated, none, VaultExpiry.At(Moment)).Ok, Is.True);
            Assert.That(_saves, Is.EqualTo(saves), "both of those were already there");
            Assert.That(Entry(never).History.UCount + Entry(dated).History.UCount, Is.Zero);

            Assert.That(_vault.UpdateEntry(never, none, VaultExpiry.At(Moment)).Ok, Is.True);
            Assert.That(_vault.UpdateEntry(dated, none, VaultExpiry.Never).Ok, Is.True);
            Assert.That(_saves, Is.EqualTo(saves + 2), "and these two were not");
            Assert.That(Entry(never).Expires, Is.True);
            Assert.That(Entry(dated).Expires, Is.False);
        }

        [Test]
        public void An_attachment_holding_the_same_bytes_unprotected_is_left_as_it_is()
        {
            // The route stores what it writes protected, which is its own choice and not something
            // the request asks for, so the same bytes kept unprotected are already there. A file
            // from other software can hold an attachment that way.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(_vault.CreateAttachment(uuid, "note.txt", new byte[] { 1 }).Ok, Is.True);
            Entry(uuid).Binaries.Set("note.txt", new KeePassLib.Security.ProtectedBinary(false, new byte[] { 4, 5 }));
            var saves = _saves;

            Assert.That(_vault.UpdateAttachment(uuid, "note.txt", new byte[] { 4, 5 }).Ok, Is.True);

            Assert.That(_saves, Is.EqualTo(saves));
            Assert.That(Entry(uuid).Binaries.Get("note.txt").IsProtected, Is.False, "left exactly as it was");
            Assert.That(Entry(uuid).History.UCount, Is.EqualTo(1u), "the create's revision and no other");
        }

        [Test]
        public void Every_change_is_saved_before_it_is_reported()
        {
            // A client told that something was written has to be able to believe it, which is what
            // the writer's confirmation is for. Here it is only that the save happened at all.
            _vault.CreateEntry(_vault.RootAlias, Named("an invented service"));

            Assert.That(_saves, Is.EqualTo(1));
        }

        [Test]
        public void What_was_written_is_in_the_file_afterwards()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            _vault.CreateField(uuid, "Invented", "a value", false);

            _database.SaveCloseAndReopen();

            var reopened = new KdbxVault(_database.Database, Writer(), null);

            Assert.That(reopened.Field(uuid, "Invented"), Is.EqualTo("a value"));
        }

        [Test]
        public void A_vault_with_no_writer_reads_and_refuses_to_change_anything()
        {
            var readOnly = new KdbxVault(_database.Database, null, null);

            Assert.That(readOnly.WriteBlocked, Is.Not.Null);
            Assert.That(readOnly.Group(readOnly.RootAlias), Is.Not.Null);
            Assert.That(readOnly.CreateEntry(readOnly.RootAlias, Named("x")).Ok, Is.False);
        }

        [Test]
        public void An_update_leaves_a_field_as_protected_as_it_found_it()
        {
            // The flag is the operator's decision, and an update is not the moment to revisit it.
            // Re-deriving it from the database default cleared it for any name KeePass does not
            // normalize. One legitimate write therefore handed the value to every later client
            // with `read` and no `protected`, and reopening the file did not put it back.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = Created(_vault.CreateEntry(group, Named("an invented service")));

            Assert.That(_vault.CreateField(uuid, "Invented", "a value", true).Ok, Is.True);
            Assume.That(_vault.Read(uuid).Protected, Does.Contain("Invented"));

            Assert.That(_vault.UpdateEntry(uuid, new Dictionary<string, string> { { "Invented", "another" } }).Ok,
                Is.True);

            Assert.That(_vault.Read(uuid).Protected, Does.Contain("Invented"),
                "an update must not quietly unprotect what it writes");

            _database.SaveCloseAndReopen();

            // A new vault, because reopening builds a new `PwDatabase` and the old one is closed.
            var reopened = new KdbxVault(_database.Database, Writer(), null);

            Assert.That(reopened.Read(uuid).Protected, Does.Contain("Invented"),
                "and the file has to agree, because this name is not one KeePass puts back");
        }

        [Test]
        public void A_recycled_entry_records_where_it_came_from()
        {
            // `LocationChanged` is what a merge compares, and it is stamped only by the overload
            // that is told to. Without it the entry reached the bin still claiming to have been
            // moved when it was created, a replica holding it in its old group tied on the
            // comparison, and the strict `>` in `RelocateEntries` left the replica's copy in
            // place. The next sync then lifted a deleted secret back out of the bin.
            _database.Database.RecycleBinEnabled = true;
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = Created(_vault.CreateEntry(group, Named("an invented service")));

            var before = Entry(uuid).LocationChanged;
            Thread.Sleep(10);

            Assert.That(_vault.DeleteEntry(uuid).Ok, Is.True);

            var recycled = Entry(uuid);

            Assert.That(recycled, Is.Not.Null, "the bin keeps it, which is the point of a bin");
            Assert.That(recycled.LocationChanged, Is.GreaterThan(before), "the move is stamped");
            Assert.That(recycled.PreviousParentGroup.ToHexString(), Is.EqualTo(group),
                "and it remembers the group it left, as KeePass's own delete records");
        }

        [Test]
        public void A_deleted_group_goes_to_the_recycle_bin_too()
        {
            // The documents promise this without qualifying it to entries. What was being
            // destroyed outright was the group's name, its notes and the `KPG` rule on it, which
            // is the part an operator would most want back.
            _database.Database.RecycleBinEnabled = true;
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var uuid = new PwUuid(MemUtil.HexStringToByteArray(group));

            Assert.That(_vault.DeleteGroup(group).Ok, Is.True);

            var bin = _database.Database.RootGroup.FindGroup(_database.Database.RecycleBinUuid, true);

            Assert.That(bin, Is.Not.Null);
            Assert.That(bin.FindGroup(uuid, true), Is.Not.Null, "the group is in the bin, not gone");
            Assert.That(_vault.Group(group), Is.Null, "and out of this API's reach, as an entry is");
        }

        [Test]
        public void A_change_keeps_the_entry_as_it_was_in_the_entry_s_history()
        {
            // The recovery an operator has when an agent overwrites something. KeePass's own
            // editor makes a revision on OK, so an entry changed through the gate that carried
            // none was an entry whose previous password existed nowhere: the audit records no
            // field name and no value on purpose, and no route reads or clears history.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "the first one").Ok, Is.True);

            Assert.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "the second one").Ok, Is.True);

            var history = Entry(uuid).History;

            Assert.That(history.UCount, Is.EqualTo(2u), "one revision per change, as the editor makes");
            Assert.That(history.GetAt(1).Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo("the first one"),
                "and the newest revision holds what the change wrote over");
        }

        [Test]
        public void Every_shape_of_change_to_an_entry_leaves_a_revision_behind()
        {
            // One per shape, because each of them mutates the entry a different way and a shape
            // that forgot would be the one an agent reached for. Creating the entry is not among
            // them: there is no earlier state to keep, and KeePass's editor makes none either.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(Entry(uuid).History.UCount, Is.Zero, "a new entry starts with none");

            var changes = new Func<VaultWrite>[]
            {
                () => _vault.UpdateEntry(uuid, Named("renamed")),
                () => _vault.CreateField(uuid, "Invented", "a value", false),
                () => _vault.UpdateField(uuid, "Invented", "another value"),
                () => _vault.DeleteField(uuid, "Invented"),
                () => _vault.CreateAttachment(uuid, "note.txt", new byte[] { 1 }),
                () => _vault.UpdateAttachment(uuid, "note.txt", new byte[] { 2 }),
                () => _vault.DeleteAttachment(uuid, "note.txt"),
            };

            for (var i = 0; i < changes.Length; i++)
            {
                Assert.That(changes[i]().Ok, Is.True, "change " + i);
                Assert.That(Entry(uuid).History.UCount, Is.EqualTo((uint)(i + 1)), "change " + i);
            }
        }

        [Test]
        public void A_revision_is_in_the_file_afterwards()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            Assume.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "the first one").Ok, Is.True);
            Assert.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "the second one").Ok, Is.True);

            _database.SaveCloseAndReopen();

            var history = Entry(uuid).History;

            Assert.That(history.UCount, Is.EqualTo(2u));
            Assert.That(history.GetAt(1).Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo("the first one"));
        }

        [Test]
        public void How_much_history_is_kept_is_the_database_s_own_setting()
        {
            // Rather than a switch of this plugin's own. `HistoryMaxItems` is what an operator
            // sets in Database Settings, it governs KeePass's own edits, and a second answer here
            // to the same question would be one of them being wrong on somebody's database.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            _database.Database.HistoryMaxItems = 2;

            for (var i = 0; i < 5; i++)
            {
                Assert.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "value " + i).Ok, Is.True);
            }

            Assert.That(Entry(uuid).History.UCount, Is.EqualTo(2u), "the limit the operator set");

            _database.Database.HistoryMaxItems = 0;
            Assert.That(_vault.UpdateField(uuid, PwDefs.PasswordField, "and one more").Ok, Is.True);

            Assert.That(Entry(uuid).History.UCount, Is.Zero,
                "an operator keeping no history keeps none through the API either");
        }

        /// <summary>A moment with nothing below the second, which is all a `.kdbx` keeps of one.</summary>
        private static readonly DateTimeOffset Moment = new DateTimeOffset(2031, 2, 3, 4, 5, 6, TimeSpan.Zero);

        [Test]
        public void An_expiry_is_read_as_KeePass_keeps_it()
        {
            // KeePass keeps whether an entry expires and when, and keeps the moment while the
            // entry does not expire as well. The moment means something only while the flag says
            // so, and a moment kept for an entry that does not expire is not one to answer with.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var entry = Entry(uuid);

            entry.ExpiryTime = Moment.UtcDateTime;
            entry.Expires = false;

            Assert.That(_vault.Entry(uuid).Expires, Is.Null, "a moment kept for an entry that does not expire");

            entry.Expires = true;

            Assert.That(_vault.Entry(uuid).Expires, Is.EqualTo(Moment));
            Assert.That(_vault.Read(uuid).Entry.Expires, Is.EqualTo(Moment));
            Assert.That(_vault.Entries().Single(e => e.Uuid == uuid).Expires, Is.EqualTo(Moment),
                "and a listing carries it for the handler to judge");
        }

        [Test]
        public void An_expiry_given_taken_off_and_moved_is_in_the_file_afterwards()
        {
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service"), VaultExpiry.At(Moment)));

            _database.SaveCloseAndReopen();

            Assert.That(Entry(uuid).Expires, Is.True);
            Assert.That(Entry(uuid).ExpiryTime, Is.EqualTo(Moment.UtcDateTime));

            var reopened = new KdbxVault(_database.Database, Writer(), null);
            Assert.That(reopened.UpdateEntry(uuid, new Dictionary<string, string>(), VaultExpiry.Never).Ok, Is.True);

            _database.SaveCloseAndReopen();

            Assert.That(Entry(uuid).Expires, Is.False);
            Assert.That(Entry(uuid).ExpiryTime, Is.EqualTo(Moment.UtcDateTime),
                "the moment is left where it was, as KeePass's own dialog leaves it");

            var later = Moment.AddYears(1);
            reopened = new KdbxVault(_database.Database, Writer(), null);
            Assert.That(reopened.UpdateEntry(uuid, new Dictionary<string, string>(), VaultExpiry.At(later)).Ok, Is.True);

            _database.SaveCloseAndReopen();

            Assert.That(Entry(uuid).Expires, Is.True);
            Assert.That(Entry(uuid).ExpiryTime, Is.EqualTo(later.UtcDateTime));
        }

        [Test]
        public void An_update_naming_only_an_expiry_is_a_change_like_any_other()
        {
            // Saved, and kept in the entry's history, which is where KeePass's own Expire Now
            // leaves the entry as it was too: an operator wanting the old expiry back finds it
            // where every other overwritten value is.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var saves = _saves;

            Assert.That(_vault.UpdateEntry(uuid, new Dictionary<string, string>(), VaultExpiry.At(Moment)).Ok, Is.True);

            Assert.That(_saves, Is.EqualTo(saves + 1));
            Assert.That(Entry(uuid).History.UCount, Is.EqualTo(1u));
            Assert.That(Entry(uuid).Expires, Is.True);
        }

        [Test]
        public void Auto_gives_a_new_entry_what_KeePass_s_options_give_one()
        {
            // Read at the moment the entry is made, as KeePass's own Add Entry reads it, and
            // restored afterwards, because it is the configuration of the KeePass running these.
            var was = KeePass.Program.Config.Defaults.NewEntryExpiresInDays;

            try
            {
                KeePass.Program.Config.Defaults.NewEntryExpiresInDays = -1;
                var off = Created(_vault.CreateEntry(_vault.RootAlias, Named("off"), VaultExpiry.Auto));

                KeePass.Program.Config.Defaults.NewEntryExpiresInDays = 0;
                var early = DateTime.UtcNow;
                var today = Created(_vault.CreateEntry(_vault.RootAlias, Named("today"), VaultExpiry.Auto));
                var late = DateTime.UtcNow;

                KeePass.Program.Config.Defaults.NewEntryExpiresInDays = 30;
                var from = DateTime.UtcNow.AddDays(30);
                var month = Created(_vault.CreateEntry(_vault.RootAlias, Named("a month"), VaultExpiry.Auto));
                var to = DateTime.UtcNow.AddDays(30);

                var silent = Created(_vault.CreateEntry(_vault.RootAlias, Named("silent")));

                Assert.That(Entry(off).Expires, Is.False, "the option switched off");
                Assert.That(Entry(today).Expires, Is.True, "zero days is now, which KeePass reads as expired at once");
                Assert.That(Entry(today).ExpiryTime, Is.InRange(early, late));
                Assert.That(Entry(month).Expires, Is.True);
                Assert.That(Entry(month).ExpiryTime, Is.InRange(from, to));
                Assert.That(Entry(silent).Expires, Is.False, "asked for and never assumed");
            }
            finally
            {
                KeePass.Program.Config.Defaults.NewEntryExpiresInDays = was;
            }
        }

        [Test]
        public void Auto_gives_nothing_in_the_entry_templates_group_as_KeePass_gives_nothing_there()
        {
            // A template's expiry would be copied into every entry made from it, which is why
            // KeePass's own Add Entry applies no new-entry default in that group or under it.
            var was = KeePass.Program.Config.Defaults.NewEntryExpiresInDays;

            try
            {
                KeePass.Program.Config.Defaults.NewEntryExpiresInDays = 30;
                var templates = Created(_vault.CreateGroup(_vault.RootAlias, "Invented templates", null));
                var inside = Created(_vault.CreateGroup(templates, "Invented kinds", null));
                _database.Database.EntryTemplatesGroup = new PwUuid(MemUtil.HexStringToByteArray(templates));

                var template = Created(_vault.CreateEntry(templates, Named("a template"), VaultExpiry.Auto));
                var deeper = Created(_vault.CreateEntry(inside, Named("a deeper template"), VaultExpiry.Auto));
                var dated = Created(_vault.CreateEntry(templates, Named("a dated template"), VaultExpiry.At(Moment)));
                var outside = Created(_vault.CreateEntry(_vault.RootAlias, Named("an entry"), VaultExpiry.Auto));

                Assert.That(Entry(template).Expires, Is.False);
                Assert.That(Entry(deeper).Expires, Is.False);
                Assert.That(Entry(dated).ExpiryTime, Is.EqualTo(Moment.UtcDateTime), "a moment asked for outright");
                Assert.That(Entry(outside).Expires, Is.True);
            }
            finally
            {
                KeePass.Program.Config.Defaults.NewEntryExpiresInDays = was;
            }
        }

        [Test]
        public void An_update_asking_for_auto_is_a_failure_on_this_side_and_writes_nothing()
        {
            // The handler refuses it with `400`, so only a defect above the vault can send one.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service"), VaultExpiry.At(Moment)));
            var saves = _saves;

            var write = _vault.UpdateEntry(uuid, Named("renamed"), VaultExpiry.Auto);

            Assert.That(write.IsFailed, Is.True, write.Reason);
            Assert.That(_saves, Is.EqualTo(saves));
            Assert.That(Entry(uuid).Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("an invented service"));
            Assert.That(Entry(uuid).ExpiryTime, Is.EqualTo(Moment.UtcDateTime));
        }

        [Test]
        public void A_read_taken_while_the_tree_moves_answers_or_says_it_could_not()
        {
            // KeePass's lists are not built to be read from another thread, and this plugin reads
            // on a connection thread while the user interface thread changes them. What must never
            // escape is one of KeePass's own collection exceptions: those reach the catch-all that
            // reports a defect in this plugin, so an agent reading while somebody typed answered
            // 500 and put a false incident on the operator's window.
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var behind = _database.Database.RootGroup.FindGroup(
                new PwUuid(MemUtil.HexStringToByteArray(group)), true);

            for (var i = 0; i < 200; i++)
            {
                behind.AddEntry(new PwEntry(true, true), true);
            }

            var stop = false;
            var churn = new Thread(() =>
            {
                while (!stop)
                {
                    behind.AddEntry(new PwEntry(true, true), true);
                    if (behind.Entries.UCount > 400)
                    {
                        behind.Entries.Remove(behind.Entries.GetAt(0));
                    }
                }
            });

            churn.Start();
            var unsettled = 0;

            try
            {
                for (var i = 0; i < 200; i++)
                {
                    // Anything other than `VaultUnsettled` leaving here fails the test by escaping,
                    // which is the assertion: those are the ones that used to become a 500.
                    try
                    {
                        _vault.Entries();
                    }
                    catch (VaultUnsettled)
                    {
                        unsettled++;
                    }
                }
            }
            finally
            {
                stop = true;
                churn.Join();
            }

            Assert.That(unsettled, Is.LessThan(200), "a settled answer has to be reachable");
        }

        [Test]
        public void A_lock_landing_during_a_lookup_finds_nothing_rather_than_failing()
        {
            // A lock clears the database on the user interface thread, and clearing it is setting
            // its root group to null, while a lookup reads that same root on a connection thread.
            // A lookup that asked whether the root was there and then read it again met a null
            // the second time: a `NullReferenceException`, which no filter names, answered `500`
            // and counted against this plugin for a lock the operator took on purpose. Cleared
            // the way `PwDatabase.Close` clears it, because its own setter refuses a null.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var field = typeof(PwDatabase).GetField(
                "m_pgRootGroup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assume.That(field, Is.Not.Null, "where PwDatabase keeps its root group");

            var database = _database.Database;
            var root = database.RootGroup;
            var stop = false;
            var locking = new Thread(() =>
            {
                while (!stop)
                {
                    field.SetValue(database, null);
                    field.SetValue(database, root);
                }
            });

            locking.Start();

            try
            {
                for (var i = 0; i < 20000; i++)
                {
                    // Anything leaving here fails the test by escaping, which is the assertion.
                    _vault.Entry(uuid);
                    _vault.Group(group);
                    _vault.Subgroups(_vault.RootAlias);
                }
            }
            finally
            {
                stop = true;
                locking.Join();
                field.SetValue(database, root);
            }
        }

        [Test]
        public void A_reference_is_served_as_the_text_it_is()
        {
            // KeePass resolves a reference against the whole open database, which knows nothing
            // about the rules. Compiled on the way out, a reference in an entry a client may read
            // would hand it the password of one it may not, so what is served is what is stored.
            var secret = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented vault")));
            Assert.That(_vault.UpdateField(secret, PwDefs.PasswordField, "an-invented-secret").Ok, Is.True);

            var reference = "{REF:P@I:" + secret + "}";
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named(reference)));
            Assert.That(_vault.CreateField(uuid, "Pointer", reference, false).Ok, Is.True);
            Assert.That(_vault.CreateField(uuid, "Variable", "%TEMP%", false).Ok, Is.True);

            Assert.That(_vault.Entry(uuid).Title, Is.EqualTo(reference), "a listing");
            Assert.That(_vault.Read(uuid).Fields["Pointer"], Is.EqualTo(reference), "a read");
            Assert.That(_vault.Field(uuid, "Pointer"), Is.EqualTo(reference), "one field");
            Assert.That(_vault.Field(uuid, "Variable"), Is.EqualTo("%TEMP%"), "a placeholder");
        }

        [Test]
        public void A_node_with_no_name_is_refused_as_a_failure_on_this_side()
        {
            // The routing table's lengths keep an empty name from ever reaching here from a client,
            // so only a defect above the vault could send one. It is refused rather than handed to
            // KeePass, and refused as this side's failure, since the client did nothing wrong.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            Assert.That(_vault.CreateGroup(_vault.RootAlias, string.Empty, null).IsFailed, Is.True, "a group");
            Assert.That(_vault.CreateField(uuid, string.Empty, "a value", false).IsFailed, Is.True, "a field");
            Assert.That(_vault.CreateAttachment(uuid, string.Empty, new byte[] { 1 }).IsFailed, Is.True,
                "an attachment");

            Assert.That(_vault.Groups(), Has.Count.EqualTo(1), "no group was made");
            Assert.That(_vault.Fields(uuid).Select(f => f.Name), Has.No.Member(string.Empty));
            Assert.That(_vault.Attachments(uuid), Is.Empty);
            Assert.That(_saves, Is.EqualTo(1), "and nothing but the entry was saved");
        }

        [Test]
        public void A_standard_field_can_be_emptied_and_not_taken_off()
        {
            // Every entry KeePass makes has all five, and an entry left without its title read as
            // an empty title in a listing and as no title at all in a read: a client could not
            // tell a stripped field from one it was refused.
            var uuid = Created(_vault.CreateEntry(_vault.RootAlias, Named("an invented service")));

            foreach (var name in new[] { "Title", "UserName", "Password", "URL", "Notes" })
            {
                Assert.That(_vault.DeleteField(uuid, name).IsConflict, Is.True, name);
            }

            Assert.That(_vault.Fields(uuid).Select(f => f.Name),
                Is.SupersetOf(new[] { "Title", "UserName", "Password", "URL", "Notes" }));
            Assert.That(_vault.UpdateField(uuid, "Title", string.Empty).Ok, Is.True, "emptying is the way");
            Assert.That(_vault.CreateField(uuid, "Invented", "a value", false).Ok, Is.True);
            Assert.That(_vault.DeleteField(uuid, "Invented").Ok, Is.True, "and any other field goes as before");
        }

        [Test]
        public void A_change_whose_save_never_lands_leaves_the_database_as_it_was()
        {
            // The whole point of taking a change back out. A caller told the write did not land
            // stops there; the operator's next save would otherwise commit it anyway, under a
            // `uuid` nobody was given. Every shape of change is here because each takes itself back
            // a different way, and one that forgot how would leave the tree holding something
            // nobody meant to put in it.
            _database.Database.RecycleBinEnabled = true;
            var group = Created(_vault.CreateGroup(_vault.RootAlias, "Fictitious", null));
            var entry = Created(_vault.CreateEntry(group, Named("an invented service"), VaultExpiry.At(Moment)));

            // A group of its own for the removal, because a group only goes when it is empty and
            // the entry above is put back by its own undo before that line is reached.
            var spare = Created(_vault.CreateGroup(_vault.RootAlias, "Empty", null));
            Assert.That(_vault.CreateField(entry, "Invented", "a value", true).Ok, Is.True);
            Assert.That(_vault.CreateAttachment(entry, "note.txt", new byte[] { 1, 2, 3 }).Ok, Is.True);

            var before = Describe();

            // A writer whose save does nothing, so `Modified` stays true and every change below is
            // answered as unsaved and taken back out.
            var stuck = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, database => { }, null, null, null),
                null);

            Assert.That(stuck.CreateEntry(group, Named("never written")).IsUnsaved, Is.True);
            Assert.That(stuck.CreateGroup(group, "never written", null).IsUnsaved, Is.True);
            Assert.That(stuck.UpdateEntry(entry, Named("renamed")).IsUnsaved, Is.True);
            Assert.That(stuck.UpdateEntry(entry, Named("renamed"), VaultExpiry.At(Moment.AddYears(1))).IsUnsaved, Is.True);
            Assert.That(stuck.UpdateEntry(entry, new Dictionary<string, string>(), VaultExpiry.Never).IsUnsaved, Is.True);
            Assert.That(stuck.UpdateGroup(group, "renamed", "notes").IsUnsaved, Is.True);
            Assert.That(stuck.CreateField(entry, "Another", "x", false).IsUnsaved, Is.True);
            Assert.That(stuck.UpdateField(entry, "Invented", "changed").IsUnsaved, Is.True);
            Assert.That(stuck.DeleteField(entry, "Invented").IsUnsaved, Is.True);
            Assert.That(stuck.CreateAttachment(entry, "second.txt", new byte[] { 9 }).IsUnsaved, Is.True);
            Assert.That(stuck.UpdateAttachment(entry, "note.txt", new byte[] { 7 }).IsUnsaved, Is.True);
            Assert.That(stuck.DeleteAttachment(entry, "note.txt").IsUnsaved, Is.True);

            // And through a name the store cannot tell from a stored one. Such a name is not one
            // the entry carries, so the strip reaches nothing and there is nothing to put back:
            // putting it back under the caller's spelling is what renamed the password's key.
            Assert.That(stuck.DeleteField(entry, PasswordAlias).IsUnsaved, Is.False);
            Assert.That(stuck.DeleteAttachment(entry, "note\u00AD.txt").IsUnsaved, Is.False);

            Assert.That(stuck.DeleteEntry(entry).IsUnsaved, Is.True);
            Assert.That(stuck.DeleteGroup(spare).IsUnsaved, Is.True);

            Assert.That(Describe(), Is.EqualTo(before),
                "each of those was taken back out, so the tree reads exactly as it did before");
        }

        [Test]
        public void A_refused_change_says_why_in_the_log()
        {
            // A client is told `503 busy`, `409 conflict` or `500` and nothing more, which is all it
            // can act on. What the operator has to clear, a dialog left open or a file that changed
            // on disk, was worked out and then written nowhere, so an agent retrying a busy write
            // for as long as it was allowed to left nothing to find.
            var log = new RecordingLog();
            var vault = new KdbxVault(_database.Database, Writer(), log);
            var uuid = Created(vault.CreateEntry(vault.RootAlias, Named("an invented service")));

            Assert.That(vault.CreateField(uuid, "Invented", "a value", false).Ok, Is.True);
            Assert.That(vault.CreateField(uuid, "Invented", "again", false).IsConflict, Is.True);
            Assert.That(new KdbxVault(_database.Database, null, log).CreateEntry(vault.RootAlias, Named("x")).IsFailed,
                Is.True);

            var key = new KeePassLib.Keys.CompositeKey();
            key.AddUserKey(new KeePassLib.Keys.KcpPassword("an invented passphrase"));
            var nowhere = new PwDatabase();
            nowhere.New(new KeePassLib.Serialization.IOConnectionInfo(), key);

            try
            {
                Assert.That(new KdbxVault(nowhere, Writer(), log).CreateEntry(KdbxVault.Root, Named("x")).IsBusy, Is.True);
            }
            finally
            {
                nowhere.Close();
            }

            Assert.That(log.Warnings, Has.Some.Contains("that field is already there"));
            Assert.That(log.Warnings, Has.Some.Contains("served without a way to write to it"));
            Assert.That(log.Warnings, Has.Some.Contains("the database has no file"));
            Assert.That(log.Warnings, Has.Count.EqualTo(3), "and nothing for the writes that landed");
        }

        /// <summary>Everything a change of any shape here would move.</summary>
        /// <remarks>
        /// Compared as one string, so a difference names itself rather than arriving as a count
        /// that is one out. The timestamps are in it because `Touch` moves them and a merge reads
        /// them: an entry put back carrying a newer `LocationChanged` than the copy in somebody's
        /// replica is an entry a later sync relocates out of where it belongs.
        ///
        /// The history is in it because every change to an entry now adds a revision, and making
        /// one can prune an older one to stay inside the operator's limits. A change taken back
        /// out therefore has to put the whole list back, not merely drop what it added. The
        /// expiry is in it because an update can move it or take it off, flag and moment alike.
        /// </remarks>
        private string Describe()
        {
            var text = new StringBuilder();
            text.Append("bin=").Append(_database.Database.RecycleBinUuid.ToHexString()).Append('\n');

            foreach (var group in _database.Database.RootGroup.GetGroups(true))
            {
                text.Append("group ").Append(group.Uuid.ToHexString())
                    .Append(' ').Append(group.Name)
                    .Append(' ').Append(group.Notes)
                    .Append(' ').Append(group.ParentGroup == null ? "-" : group.ParentGroup.Uuid.ToHexString())
                    .Append(' ').Append(group.LastModificationTime.Ticks)
                    .Append(' ').Append(group.LocationChanged.Ticks).Append('\n');
            }

            foreach (var entry in _database.Database.RootGroup.GetEntries(true))
            {
                text.Append("entry ").Append(entry.Uuid.ToHexString())
                    .Append(' ').Append(entry.ParentGroup.Uuid.ToHexString())
                    .Append(' ').Append(entry.LastModificationTime.Ticks)
                    .Append(' ').Append(entry.LocationChanged.Ticks)
                    .Append(' ').Append(entry.Expires)
                    .Append(' ').Append(entry.ExpiryTime.Ticks).Append('\n');

                foreach (var pair in entry.Strings)
                {
                    text.Append("  string ").Append(pair.Key).Append('=').Append(pair.Value.ReadString())
                        .Append(' ').Append(pair.Value.IsProtected).Append('\n');
                }

                foreach (var pair in entry.Binaries)
                {
                    text.Append("  binary ").Append(pair.Key).Append('=')
                        .Append(MemUtil.ByteArrayToHexString(pair.Value.ReadData())).Append('\n');
                }

                text.Append("  history ").Append(entry.History.UCount).Append('\n');

                foreach (var revision in entry.History)
                {
                    text.Append("  revision ").Append(revision.LastModificationTime.Ticks)
                        .Append(' ').Append(revision.Strings.UCount)
                        .Append(' ').Append(revision.Binaries.UCount).Append('\n');
                }
            }

            foreach (var gone in _database.Database.DeletedObjects)
            {
                text.Append("deleted ").Append(gone.Uuid.ToHexString()).Append('\n');
            }

            return text.ToString();
        }

        private PwEntry Entry(string uuid)
        {
            return _database.Database.RootGroup.FindEntry(
                new PwUuid(MemUtil.HexStringToByteArray(uuid)), true);
        }

        private DatabaseWriter Writer()
        {
            return new DatabaseWriter(CurrentThread.Instance, Save, null, null, null);
        }

        private void Save(PwDatabase database)
        {
            _saves++;
            database.Save(null);
        }

        private sealed class RecordingLog : IGateLog
        {
            public List<string> Warnings { get; } = new List<string>();

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
                Warnings.Add(message);
            }

            public void Error(string message, Exception error)
            {
            }

            public void Unexpected(string message, Exception error)
            {
            }
        }

        private static IReadOnlyDictionary<string, string> Named(string title)
        {
            return new Dictionary<string, string> { { PwDefs.TitleField, title } };
        }

        private static string Created(VaultWrite write)
        {
            Assert.That(write.Ok, Is.True, write.Reason);
            Assert.That(write.Uuid, Is.Not.Null.And.Not.Empty);
            return write.Uuid;
        }

        /// <summary>What an attachment holds, read with no ceiling, or null when it is not there.</summary>
        private byte[] Stored(string uuid, string name)
        {
            var read = _vault.Attachment(uuid, name, int.MaxValue);
            return read == null ? null : read.Content;
        }
    }
}
