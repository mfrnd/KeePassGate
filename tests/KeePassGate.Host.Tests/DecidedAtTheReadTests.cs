using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Bridge;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A read is allowed by what the database holds at the moment it is read, and serves the
    /// database of that same moment.
    /// </summary>
    /// <remarks>
    /// The router asked the rules under one hold of KeePass's thread and the handler read under
    /// others, so whatever the operator did between two of them was served by a decision that
    /// never saw it: a rule taken away, and then a value typed into the entry it had opened, went
    /// out to the client the rule had been taken from. Every case here is the real router over
    /// the real vault, with an operator's edit landing on KeePass's thread the moment one of the
    /// request's holds is let go, which is the only kind of moment KeePass's thread has for it.
    ///
    /// **The edit is tried after each hold a request might take**, the first to the ninth, which
    /// is as many as any of these requests ever took. A request takes one, so the later cases
    /// find no hold to follow and the edit never runs; they are here for the request that comes
    /// to take two again, and each of them is a place the value typed last used to get out.
    /// </remarks>
    [TestFixture]
    public sealed class DecidedAtTheReadTests
    {
        private const string Name = PwDefs.UserNameField;
        private const string Typed = "typed after the rule went";

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;
        private SpecRouter _router;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RLA")));
            _database.RootGroup.AddGroup(_group, true);

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _entry.Strings.Set(Name, new ProtectedString(false, "kept in the open"));
            _entry.Binaries.Set("notes.txt", new ProtectedBinary(false, new byte[] { 1, 2, 3 }));
            _group.AddEntry(_entry, true);

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_rule_taken_away_under_a_read_serves_nothing_typed_after_it(
            [Values("/entries/{entry}", "/entries/{entry}/fields/UserName")] string path,
            [Range(1, 9)] int after)
        {
            // The operator seals the group against the client and only then types into the
            // entry. The read either came first, and serves the entry as it stood, or came
            // second, and is refused: what was typed behind the seal is in neither answer.
            var operatorNext = new OperatorNext(after, () =>
            {
                _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Sealing());
                _entry.Strings.Set(Name, new ProtectedString(false, Typed));
                _entry.Touch(true, false);
            });

            var answer = Send(operatorNext, path);

            Assert.That(answer.Document(), Does.Not.Contain(Typed), answer.Status + " " + answer.Document());
            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(answer.Document(), Does.Contain("kept in the open"));
        }

        [Test]
        public void An_entry_moved_behind_a_seal_under_a_read_is_not_served_from_there([Range(1, 9)] int after)
        {
            // The same for a move: the entry is dragged into a group the client reaches nothing
            // in, and renamed there.
            var kept = new PwGroup(true, true, "Kept", PwIcon.Folder);
            kept.CustomData.Set(AclDocument.CustomDataKey, Rules.Sealing());
            _database.RootGroup.AddGroup(kept, true);
            var operatorNext = new OperatorNext(after, () =>
            {
                _group.Entries.Remove(_entry);
                kept.AddEntry(_entry, true, true);
                _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Typed));
            });

            var answer = Send(operatorNext, "/entries/{entry}");

            Assert.That(answer.Document(), Does.Not.Contain(Typed), answer.Status + " " + answer.Document());
            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(answer.Document(), Does.Contain("an invented service"));
        }

        [Test]
        public void The_first_hold_of_a_read_is_one_the_operator_s_edit_does_follow()
        {
            // So that the cases above are about an edit that ran: after the first hold it does,
            // and the answer was made before it.
            var operatorNext = new OperatorNext(1, () =>
                _entry.Strings.Set(Name, new ProtectedString(false, Typed)));

            var answer = Send(operatorNext, "/entries/{entry}");

            Assert.That(operatorNext.Ran, Is.True);
            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(answer.Document(), Does.Not.Contain(Typed));
        }

        [TestCase("/entries/{entry}")]
        [TestCase("/entries/{entry}/fields")]
        [TestCase("/entries/{entry}/fields/Longer")]
        [TestCase("/entries/{entry}/fields/Open")]
        [TestCase("/entries/{entry}/attachments")]
        [TestCase("/entries/{entry}/attachments/notes.txt")]
        public void What_the_operator_does_once_the_hold_has_ended_is_in_neither_the_answer_nor_its_tag(string path)
        {
            // A read takes its view of the entry inside the hold and asks the view for values,
            // lengths, attachments and the tag once the hold has ended, on its own thread. The
            // operator's edit is the first thing KeePass's thread does when the hold is let go,
            // so it lands before any of that. The view keeps the objects the entry held, which
            // KeePass replaces and does not change, so the answer and its tag are of the entry
            // as it stood inside the hold. One value is protected and held as bytes the way a
            // file's are, and is digested from a copy of those bytes. Another is kept in the
            // open and is longer than a piece, so it is digested a piece at a time. Both are
            // put back in new objects before the request the operator follows, so that its tag
            // is the first over them and their digests are taken after the edit has landed.
            _database.RootGroup.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RLAP")));
            var longer = new string('k', 40000);
            _entry.Strings.Set("Longer", new ProtectedString(true, System.Text.Encoding.UTF8.GetBytes(longer)));
            _entry.Strings.Set("Open", new ProtectedString(false, new string('m', (2 * NodeTags.Pieces.Characters) + 7)));
            var held = _entry.Strings.Get("Longer");

            var before = Send(CurrentThread.Instance, path);
            Assume.That(before.Status, Is.EqualTo(200), before.Document());
            Assume.That(before.Headers["ETag"], Is.Not.Null);

            // The first request left the protected value as it found it: a read copies the
            // bytes out and leaves KeePass's object holding them.
            Assert.That(_entry.Strings.Get("Longer"), Is.SameAs(held));
            Assert.That(KeePassMemory.KeepsText(held), Is.False, "a read left KeePass the text of a protected value");

            // The same values in objects no tag has been taken over.
            _entry.Strings.Set("Longer", new ProtectedString(true, System.Text.Encoding.UTF8.GetBytes(longer)));
            _entry.Strings.Set("Open", new ProtectedString(false, new string('m', (2 * NodeTags.Pieces.Characters) + 7)));

            var operatorNext = new OperatorNext(1, () =>
            {
                _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Typed));
                _entry.Strings.Set("Longer", new ProtectedString(true, Typed));
                _entry.Strings.Set("Open", new ProtectedString(false, Typed));
                _entry.Strings.Remove(Name);
                _entry.Strings.Set("Added", new ProtectedString(false, Typed));
                _entry.Binaries.Set("notes.txt", new ProtectedBinary(false, new byte[] { 9, 9, 9, 9 }));
                _entry.Binaries.Set("more.txt", new ProtectedBinary(false, new byte[] { 7 }));
                _entry.Expires = true;
                _entry.ExpiryTime = DateTime.UtcNow.AddDays(3);
                _entry.Touch(true, false);
            });

            var answer = Send(operatorNext, path);

            Assert.That(operatorNext.Ran, Is.True);
            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(answer.Document(), Is.EqualTo(before.Document()));
            Assert.That(answer.Headers["ETag"], Is.EqualTo(before.Headers["ETag"]));

            var after = Send(CurrentThread.Instance, path);
            Assert.That(after.Status, Is.EqualTo(200), after.Document());
            Assert.That(after.Headers["ETag"], Is.Not.EqualTo(before.Headers["ETag"]), "the edit did move the tag");
        }

        [TestCase("/entries")]
        [TestCase("/groups/{group}/entries")]
        public void A_listing_written_once_the_hold_has_ended_is_of_the_entries_as_they_stood_inside(string path)
        {
            // Which entries a caller is shown is decided inside the hold, and the listing is
            // written after it, each title and address read then. The description a listing is
            // written from keeps the string the entry held inside the hold, so a title the
            // operator types the moment the hold is let go, an entry added and an entry removed
            // are in the listing that follows and not in this listing.
            var before = Send(CurrentThread.Instance, path);
            Assume.That(before.Status, Is.EqualTo(200), before.Document());
            Assume.That(before.Document(), Does.Contain("an invented service"));

            var operatorNext = new OperatorNext(1, () =>
            {
                _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Typed));
                _entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://typed.example/"));

                var added = new PwEntry(true, true);
                added.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "added once the hold had ended"));
                _group.AddEntry(added, true);
            });

            var answer = Send(operatorNext, path);

            Assert.That(operatorNext.Ran, Is.True);
            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(answer.Document(), Is.EqualTo(before.Document()));

            var after = Send(CurrentThread.Instance, path);
            Assert.That(after.Document(), Does.Contain(Typed));
            Assert.That(after.Document(), Does.Contain("added once the hold had ended"));
        }

        [TestCase("/entries/{entry}")]
        [TestCase("/entries/{entry}/fields")]
        [TestCase("/entries/{entry}/fields/Password")]
        public void A_setting_the_operator_changes_once_the_hold_has_ended_does_not_open_a_value(string path)
        {
            // Whether the database protects a name is one of its settings, and a view's strings
            // are made after the hold. The settings are copied inside the hold, so a password
            // the database protected when the rules were asked is withheld from a caller
            // without `protected`, though the operator unprotects passwords the moment the hold
            // is let go. Read from the live settings afterwards, the value went out to a rule
            // that had been asked about a protected string.
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(false, "not-a-real-password"));
            Assume.That(_database.Database.MemoryProtection.ProtectPassword, Is.True);

            var operatorNext = new OperatorNext(
                1, () => _database.Database.MemoryProtection.ProtectPassword = false);

            var answer = Send(operatorNext, path);

            Assert.That(operatorNext.Ran, Is.True);
            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(answer.Document(), Does.Not.Contain("not-a-real-password"));
            Assert.That(answer.Document(), Does.Contain("Password"));

            if (!path.EndsWith("/fields", StringComparison.Ordinal))
            {
                var after = Send(CurrentThread.Instance, path);
                Assert.That(
                    after.Document(), Does.Contain("not-a-real-password"), "the setting did change what is read");
            }
        }

        [TestCase("/groups")]
        [TestCase("/groups/{group}")]
        [TestCase("/groups/{group}/groups")]
        [TestCase("/groups/{group}/entries")]
        [TestCase("/groups/{group}/access")]
        [TestCase("/entries")]
        [TestCase("/entries/{entry}")]
        [TestCase("/entries/{entry}/fields")]
        [TestCase("/entries/{entry}/fields/UserName")]
        [TestCase("/entries/{entry}/attachments")]
        [TestCase("/entries/{entry}/attachments/notes.txt")]
        [TestCase("/entries/{entry}/access")]
        public void A_request_that_changes_nothing_holds_KeePass_s_thread_once(string path)
        {
            var operatorNext = new OperatorNext(1, () => { });

            var answer = Send(operatorNext, path);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(operatorNext.Holds, Is.EqualTo(1));
        }

        private GateContext Send(IReadHold hold, string path)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, null),
                TestTags.Fixed,
                hold,
                null);

            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var context = new GateContext(
                    new HttpRequestHead(
                        "GET",
                        path.Replace("{group}", _group.Uuid.ToHexString()).Replace("{entry}", _entry.Uuid.ToHexString()),
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        0),
                    string.Empty,
                    ClientIdentity.Paired("d1e2f3", certificate, new[] { "agent" }));
                new ServedDatabase(vault).PutOn(context);

                _router.Handle(context);

                // An answer written as it is sent is written here, as the transport writes it, so
                // a read of an entry or of a field driven from here reads its values.
                context.Document();
                return context;
            }
        }

        /// <summary>
        /// An operator's edit that KeePass's thread runs as soon as one of the request's holds
        /// is let go: the first, or the second, or whichever is asked for.
        /// </summary>
        private sealed class OperatorNext : IReadHold
        {
            private readonly int _after;
            private readonly Action _edit;
            private int _depth;

            public OperatorNext(int after, Action edit)
            {
                _after = after;
                _edit = edit;
            }

            public bool Ran { get; private set; }

            /// <summary>How many times the thread was taken, a hold inside a hold not counted.</summary>
            public int Holds { get; private set; }

            public T Hold<T>(Func<T> read)
            {
                if (_depth > 0)
                {
                    return read();
                }

                Holds++;
                _depth++;

                T value;
                try
                {
                    value = read();
                }
                finally
                {
                    _depth--;
                }

                if (Holds == _after)
                {
                    Ran = true;
                    _edit();
                }

                return value;
            }
        }
    }
}
