using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Bridge;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A value KeePass protects in memory is still protected there once the gate has read it,
    /// digested it into a tag or written it.
    /// </summary>
    /// <remarks>
    /// KeePass holds a protected value a file was loaded with as encrypted bytes, and
    /// `ProtectedString.ReadString` decodes it, keeps the decoded string inside the object and
    /// lets the bytes go, so read that way a value is in KeePass's memory in the open for as
    /// long as the entry is loaded. A tag covers every value in its caller's sight, so a listing
    /// of an entry's fields left every protected value of the entry in the open. Every case
    /// here is the real router over the real vault, and asks KeePass's own object afterwards
    /// whether it keeps the text: `KeePassContractTests` pins that the question still means
    /// what it meant.
    ///
    /// **Every route that reads or changes an entry is sent.** Each read of the entry, of its
    /// strings and of its attachments, under `GET` and under `HEAD`, the listings of entries
    /// and what the caller may do at the entry; and each change: a string made, updated and
    /// stripped, an attachment made, replaced and stripped, the entry updated, made and
    /// removed. The routes about a group alone and about the gate itself are left out, since
    /// none of them is answered from an entry's strings.
    ///
    /// **The question is asked of the entry's strings and of every revision in its history.**
    /// A change keeps the entry as it was in a revision, which holds the same objects, so a
    /// value a change replaced or took off is still in KeePass's memory there and is asked
    /// as the rest are.
    ///
    /// Everything in here is invented.
    /// </remarks>
    [TestFixture]
    public sealed class ProtectedInMemoryTests
    {
        private const string Password = "not-a-real-password";
        private const string Pin = "0000";

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;
        private SpecRouter _router;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "CRUSDLAP")));
            _database.RootGroup.AddGroup(_group, true);

            // A database that protects titles and addresses as well as passwords, so that the
            // strings a listing reads are protected too. KeePass writes each of an entry's own
            // five strings to the file by the database's setting for it, whatever the string's
            // flag, and reads as text what that setting leaves in the open: a title flagged
            // protected in a database that does not protect titles is in the open after
            // KeePass's own save, by KeePass's own hand.
            _database.Database.MemoryProtection.ProtectTitle = true;
            _database.Database.MemoryProtection.ProtectUrl = true;

            // Held as a file's protected values are held once loaded: as bytes, encrypted.
            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, Sealed("an invented service"));
            _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "an invented name"));
            _entry.Strings.Set(PwDefs.PasswordField, Sealed(Password));
            _entry.Strings.Set(PwDefs.UrlField, Sealed("https://invented.example/"));
            _entry.Strings.Set("PIN", Sealed(Pin));
            _entry.Binaries.Set("notes.txt", new ProtectedBinary(true, new byte[] { 1, 2, 3 }));
            _group.AddEntry(_entry, true);

            Assume.That(InTheOpen(), Is.Empty);

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [TestCase("/entries/{entry}", Password)]
        [TestCase("/entries/{entry}/fields", "\"PIN\"")]
        [TestCase("/entries/{entry}/fields/Password", Password)]
        [TestCase("/entries/{entry}/fields/Title", "an invented service")]
        [TestCase("/entries/{entry}/fields/UserName", "an invented name")]
        [TestCase("/entries/{entry}/attachments", "notes.txt")]
        [TestCase("/entries/{entry}/attachments/notes.txt", "notes.txt")]
        [TestCase("/entries/{entry}/access", "\"agent\"")]
        [TestCase("/groups/{group}/entries", "an invented service")]
        [TestCase("/entries", "https://invented.example/")]
        public void A_read_leaves_every_protected_value_as_KeePass_held_it(string path, string served)
        {
            foreach (var method in new[] { "GET", "HEAD" })
            {
                var answer = Asked(method, path, null);

                Assert.That(answer.Status, Is.EqualTo(200), method + " " + path + " " + answer.Json);
                Assert.That(InTheOpen(), Is.Empty, method + " " + path);

                if (method == "GET")
                {
                    Assert.That(answer.Json, Does.Contain(served), path);
                }
            }
        }

        [Test]
        public void A_change_asked_against_a_tag_leaves_every_protected_value_as_KeePass_held_it()
        {
            // A change takes the entry's tag as it finds the entry and again as it leaves it,
            // each over every value in its caller's sight.
            var tag = Asked("GET", "/entries/{entry}/fields/UserName", null).Headers["ETag"];
            Assume.That(tag, Is.Not.Null);

            var answer = Asked(
                "PUT", "/entries/{entry}/fields/UserName", Bodies.Of(("value", "another invented name")), tag);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Json, Does.Contain("\"etag\""));
            Assert.That(InTheOpen(), Is.Empty);
        }

        [Test]
        public void An_update_that_would_change_nothing_is_found_so_without_opening_a_value()
        {
            // The stored value is compared with what was sent, for a caller that may read it,
            // and an update found to change nothing writes nothing. Held to that, since a
            // comparison that answered "different" would write the same value over the stored
            // string and leave nothing in the open either: the string the entry holds is the
            // object it held, and no revision was taken.
            var held = _entry.Strings.Get("PIN");
            var revisions = _entry.History.UCount;

            var answer = Asked("PUT", "/entries/{entry}/fields/PIN", Bodies.Of(("value", Pin)));

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_entry.Strings.Get("PIN"), Is.SameAs(held), "the value was written over");
            Assert.That(_entry.History.UCount, Is.EqualTo(revisions), "a revision was taken for a change that changed nothing");
            Assert.That(InTheOpen(), Is.Empty);
        }

        [TestCase("DELETE", "/entries/{entry}/fields/PIN", TestName = "A protected field stripped is still protected in memory")]
        [TestCase("POST", "/entries/{entry}/attachments", TestName = "An attachment made leaves every protected value protected in memory")]
        [TestCase("PUT", "/entries/{entry}/attachments/notes.txt", TestName = "An attachment replaced leaves every protected value protected in memory")]
        [TestCase("DELETE", "/entries/{entry}/attachments/notes.txt", TestName = "An attachment stripped leaves every protected value protected in memory")]
        public void A_change_that_sets_no_string_leaves_every_protected_value_as_KeePass_held_it(string method, string path)
        {
            // Asked against the entry's tag, so each takes the tag of the entry as it finds it
            // and again as it leaves it, over every value in its caller's sight. A removal asks
            // no switch and shows nobody anything, and its tags are taken all the same.
            var pin = _entry.Strings.Get("PIN");
            var tag = Asked("GET", "/entries/{entry}/fields/UserName", null).Headers["ETag"];
            Assume.That(tag, Is.Not.Null);

            var answer = Asked(method, path, Attaching(method), tag);

            Assert.That(answer.Status, Is.EqualTo(method == "POST" ? 201 : 200), method + " " + path + " " + answer.Json);
            Assert.That(answer.Json, Does.Contain("\"etag\""), method + " " + path);
            Assert.That(InTheOpen(), Is.Empty, method + " " + path);

            // A string a strip took off is in none of the entry's own strings any longer. The
            // revision the change kept holds it, where it is asked with the rest, and it is
            // asked here as the object it is, for a database that keeps no history.
            Assert.That(KeePassMemory.KeepsText(pin), Is.False, method + " " + path);
        }

        [Test]
        public void An_entry_removed_holds_every_protected_value_as_KeePass_held_it_wherever_the_entry_went()
        {
            // To the recycle bin where the database keeps a bin, and out of the tree where it
            // keeps none. The entry is the same object either way and its strings the same
            // objects, so the question is asked of the entry this fixture still holds. Asked
            // against the entry's tag, so the removal takes that tag as it finds the entry,
            // over every value in its caller's sight.
            var tag = Asked("GET", "/entries/{entry}/fields/UserName", null).Headers["ETag"];
            Assume.That(tag, Is.Not.Null);

            var answer = Asked("DELETE", "/entries/{entry}", null, tag);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_group.Entries.IndexOf(_entry), Is.LessThan(0), "the entry is still in its group");
            Assert.That(InTheOpen(), Is.Empty);
        }

        [TestCase("PUT", "/entries/{entry}/fields/PIN", "PIN", TestName = "A protected field updated is protected in memory")]
        [TestCase("POST", "/entries/{entry}/fields", "Recovery", TestName = "A protected field created is protected in memory")]
        [TestCase("PUT", "/entries/{entry}", "Password", TestName = "A password updated with its entry is protected in memory")]
        public void A_protected_value_written_through_the_gate_is_held_as_KeePass_holds_a_loaded_one(
            string method, string path, string name)
        {
            const string Chosen = "chosen-by-the-caller";

            var answer = Asked(method, path, Writing(method, path, name, Chosen));

            Assert.That(answer.Status, Is.EqualTo(method == "POST" ? 201 : 200), answer.Json);

            var written = _entry.Strings.Get(name);
            Assert.That(written, Is.Not.Null);
            Assert.That(written.IsProtected, Is.True);
            Assert.That(InTheOpen(), Is.Empty);
            Assert.That(Encoding.UTF8.GetString(written.ReadUtf8()), Is.EqualTo(Chosen));
        }

        [Test]
        public void An_entry_created_through_the_gate_holds_its_password_protected_in_memory()
        {
            var answer = Asked(
                "POST",
                "/groups/{group}/entries",
                Bodies.Of(("Title", "another invented service"), ("Password", Password)));

            Assert.That(answer.Status, Is.EqualTo(201), answer.Json);

            var created = _group.Entries.Single(entry => !ReferenceEquals(entry, _entry));
            var password = created.Strings.Get(PwDefs.PasswordField);
            Assert.That(password.IsProtected, Is.True);
            Assert.That(KeePassMemory.KeepsText(password), Is.False);
            Assert.That(Encoding.UTF8.GetString(password.ReadUtf8()), Is.EqualTo(Password));
        }

        [Test]
        public void A_value_KeePass_keeps_in_the_open_is_read_as_it_is()
        {
            // The other half, so that none above is a count that counts: a value that is not
            // protected is a string in KeePass's memory from the start, and reading it changes
            // nothing about it.
            Assert.That(KeePassMemory.KeepsText(_entry.Strings.Get(PwDefs.UserNameField)), Is.True);

            var answer = Asked("GET", "/entries/{entry}/fields/UserName", null);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Json, Does.Contain("an invented name"));
        }

        /// <summary>
        /// The body that writes a protected value under this name: a field made, a field
        /// updated, or the entry updated with it.
        /// </summary>
        private static string Writing(string method, string path, string name, string value)
        {
            if (method == "POST")
            {
                return Bodies.Of(("name", name), ("value", value), ("protected", "true"));
            }

            return path.EndsWith("{entry}", StringComparison.Ordinal)
                ? Bodies.Of((name, value))
                : Bodies.Of(("value", value));
        }

        /// <summary>
        /// The body a change to an attachment carries: a name and bytes for an attachment made,
        /// bytes for an attachment replaced, and none for a removal.
        /// </summary>
        private static string Attaching(string method)
        {
            var content = Convert.ToBase64String(new byte[] { 4, 5, 6, 7 });

            switch (method)
            {
                case "POST":
                    return Bodies.Of(("name", "added.txt"), ("content", content));
                case "PUT":
                    return Bodies.Of(("content", content));
                default:
                    return null;
            }
        }

        /// <summary>A protected string held as KeePass holds a loaded file's: as bytes.</summary>
        private static ProtectedString Sealed(string value)
        {
            return new ProtectedString(true, Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// The names of the protected strings KeePass now keeps as text: the entry's own, and
        /// those of every revision in its history, each named for the revision it is in.
        /// </summary>
        /// <remarks>
        /// The revisions too, because a change keeps the entry as it was in a revision, which
        /// holds the same objects: a value a change replaced or took off is in none of the
        /// entry's own strings afterwards and is still in KeePass's memory.
        /// </remarks>
        private List<string> InTheOpen()
        {
            return Open(_entry.Strings, string.Empty)
                .Concat(_entry.History.SelectMany(
                    (revision, index) => Open(revision.Strings, "revision " + index + " holds ")))
                .ToList();
        }

        /// <summary>
        /// The protected strings among these that KeePass keeps as text, each by its name
        /// behind what tells where it was found.
        /// </summary>
        private static IEnumerable<string> Open(
            IEnumerable<KeyValuePair<string, ProtectedString>> strings, string found)
        {
            return strings
                .Where(pair => pair.Value.IsProtected && KeePassMemory.KeepsText(pair.Value))
                .Select(pair => found + pair.Key);
        }

        private GateContext Asked(string method, string path, string body)
        {
            return Asked(method, path, body, null);
        }

        private GateContext Asked(string method, string path, string body, string ifMatch)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(new Immediate(), database => database.Save(null), null, null, null),
                TestTags.Fixed,
                null);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (ifMatch != null)
            {
                headers["If-Match"] = ifMatch;
            }

            var text = body ?? string.Empty;
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var context = new GateContext(
                    new HttpRequestHead(
                        method,
                        path.Replace("{group}", _group.Uuid.ToHexString()).Replace("{entry}", _entry.Uuid.ToHexString()),
                        new Dictionary<string, string>(),
                        headers,
                        Bodies.LengthOf(text)),
                    text,
                    ClientIdentity.Paired("d1e2f3", certificate, new[] { "agent" }));
                new ServedDatabase(vault).PutOn(context);

                _router.Handle(context);
                return context;
            }
        }

        /// <summary>KeePass's thread, for a test that is already on the only thread there is.</summary>
        private sealed class Immediate : IUiThread
        {
            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                work();
                return true;
            }
        }
    }

    /// <summary>
    /// What KeePass keeps of a protected string in memory, read off the object itself.
    /// </summary>
    /// <remarks>
    /// Through a private field of KeePass's, `m_strPlainText`, which is where
    /// `ProtectedString.ReadString` keeps the text it decoded. Nothing public answers it.
    /// `KeePassContractTests` holds the field to being there and to meaning this, so a KeePass
    /// that keeps the text elsewhere fails there and not as a test here passing for nothing.
    /// </remarks>
    internal static class KeePassMemory
    {
        /// <summary>The name of the field a `ProtectedString` keeps its text in, once it has any.</summary>
        public const string TextField = "m_strPlainText";

        /// <summary>Whether KeePass keeps this string's value as text, in the open.</summary>
        public static bool KeepsText(ProtectedString value)
        {
            var field = typeof(ProtectedString).GetField(TextField, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "KeePass no longer keeps a string's text under " + TextField);

            return field.GetValue(value) != null;
        }
    }
}
