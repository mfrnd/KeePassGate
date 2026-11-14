using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
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
    /// What the vault does when a node's tag cannot be taken, and with an attachment the gate
    /// cannot read, against a real database while another plugin's cipher stands.
    /// </summary>
    /// <remarks>
    /// **The hook is KeePass's own**, `ProtectedBinary.ExtCrypt`, which another plugin can set.
    /// KeePass keeps a protected attachment made while it is set through that cipher, and the
    /// gate reads an attachment a piece at a time out of KeePass's own buffer, which it can do
    /// only where Windows' protection of process memory keeps the buffer. So an attachment
    /// made under the hook is an attachment the gate reads no piece of, for as long as it is
    /// kept so: every read of it and every tag over it is refused `AttachmentUnreadable`, and
    /// a write that would make such an attachment is refused before anything is changed. The
    /// attachment <see cref="SetUp"/> makes is made before the hook, and digested before it.
    ///
    /// **The hook can also be told to refuse**, which stands for a decryption or an encryption
    /// failing once: memory protection refusing, or an allocation running out. What goes
    /// through it of the gate's own is the digest a tag keeps for a protected attachment, kept
    /// in a container of KeePass's and so through whatever cipher stands.
    ///
    /// It is a static of KeePass's, so <see cref="TearDown"/> takes it away again once the
    /// database that holds such attachments is gone.
    ///
    /// Everything in here is invented.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class TagFailureTests
    {
        private TemporaryDatabase _database;
        private IVault _vault;
        private PwEntry _entry;
        private SpecRouter _router;
        private bool _refusing;
        private bool _refusingToKeep;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _refusing = false;
            _refusingToKeep = false;
            _saves = 0;
            _database = TemporaryDatabase.New();
            _vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, Save, null, null, null),
                TestTags.Fixed,
                null);
            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _entry.Binaries.Set("notes.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("invented")));
            _database.RootGroup.AddEntry(_entry, true);
            _database.Save();

            // The entry's tag once before the hook stands, so that the digest kept for the
            // attachment made above is taken while the gate can still read the attachment, and
            // kept as that attachment is, outside the hook.
            _vault.EntryTag(Uuid(_entry));

            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                if (flags == PbCryptFlags.Decrypt && _refusing)
                {
                    throw new CryptographicException("an invented decryption that fails");
                }

                if (flags == PbCryptFlags.Encrypt && _refusingToKeep)
                {
                    throw new CryptographicException("an invented encryption that fails");
                }

                for (var i = 0; i < data.Length; i++)
                {
                    data[i] ^= 0x5A;
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            _refusing = false;
            _refusingToKeep = false;
            try
            {
                _database.Dispose();
            }
            finally
            {
                ProtectedBinary.ExtCrypt = null;
            }
        }

        [Test]
        public void A_change_whose_answer_tag_cannot_be_taken_is_taken_back_out()
        {
            // An attachment left on the entry while the hook stood, as KeePass or another plugin
            // can leave it: the gate reads no piece of it, so the tag the answer to a change
            // carries cannot be taken once the change is made, and the change is taken back.
            Plant("later.txt", "made through the hook");
            var revisions = _entry.History.UCount;
            var modified = _database.Database.Modified;
            var held = _database.Database.HashOfLastIO;
            var notes = _entry.Binaries.Get("notes.txt");

            var thrown = Thrown.By(
                () => _vault.DeleteAttachment(Uuid(_entry), "notes.txt", VaultAskings.Tagged(null, true)));

            Assert.That(Unreadable(thrown), Is.True, "the change was not refused for the attachment: " + thrown);
            Assert.That(_entry.Binaries.Get("notes.txt"), Is.SameAs(notes), "the attachment is the object it was");
            Assert.That(Encoding.UTF8.GetString(notes.ReadData()), Is.EqualTo("invented"), "and holds what it held");
            Assert.That(_entry.Binaries.UCount, Is.EqualTo(2));
            Assert.That(_entry.History.UCount, Is.EqualTo(revisions), "with no revision left behind");
            Assert.That(_database.Database.Modified, Is.EqualTo(modified), "and nothing left for a save to write");
            Assert.That(_database.Database.HashOfLastIO, Is.EqualTo(held));
            Assert.That(_saves, Is.Zero, "nothing was saved");
        }

        [TestCase(false, TestName = "A digest that could not be read back once is handed to the next tag once it can")]
        [TestCase(true, TestName = "A digest that could not be kept once is taken again by the next tag")]
        public void A_tag_that_failed_once_is_taken_once_nothing_fails(bool keeping)
        {
            // An attachment made with no hook standing, so the gate reads it. Its digest is kept
            // as the attachment is, encrypted, in a container of KeePass's, and that container
            // is made and read through the hook: refused on the way in, no digest is kept, and
            // refused on the way out, the digest kept is not handed over. Neither failure is
            // what the next tag is answered with.
            var hook = ProtectedBinary.ExtCrypt;
            ProtectedBinary.ExtCrypt = null;
            try
            {
                _entry.Binaries.Set(
                    "later.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("made with no hook")));
            }
            finally
            {
                ProtectedBinary.ExtCrypt = hook;
            }

            _refusingToKeep = keeping;
            _refusing = !keeping;
            Assert.Throws<CryptographicException>(() => _vault.EntryTag(Uuid(_entry)));
            _refusingToKeep = false;
            _refusing = false;

            var tag = _vault.EntryTag(Uuid(_entry));
            Assert.That(tag, Does.Match("^[0-9a-f]{32}$"), "the failure is not what the next tag is answered with");
            Assert.That(_vault.EntryTag(Uuid(_entry)), Is.EqualTo(tag));
        }

        [Test]
        public void A_change_takes_no_tag_that_neither_its_condition_nor_its_answer_reads()
        {
            // An attachment the gate reads no piece of, so that any tag taken of the entry
            // throws, and a write that takes a tag where nothing reads it is a write that fails
            // for nothing.
            Plant("later.txt", "made through the hook");
            Assert.Throws<AttachmentUnreadable>(() => _vault.EntryTag(Uuid(_entry)));

            var taken = _vault.DeleteAttachment(Uuid(_entry), "later.txt", VaultAskings.Tagged(null, true));
            Assert.That(taken.Ok, Is.True, "no condition, and an answer tagging what is left: " + taken.Reason);
            Assert.That(taken.Tag, Does.Match("^[0-9a-f]{32}$"));

            Plant("later.txt", "made through the hook");

            var any = _vault.UpdateField(
                Uuid(_entry), PwDefs.TitleField, "renamed", VaultAskings.Tagged(VaultPrecondition.Read("*"), false));
            Assert.That(any.Ok, Is.True, "a condition every entry that is there meets: " + any.Reason);

            var none = _vault.UpdateField(
                Uuid(_entry), PwDefs.TitleField, "renamed again", VaultAskings.Tagged(VaultPrecondition.Read("W/\"0\""), false));
            Assert.That(none.IsPreconditionFailed, Is.True, "a condition naming no tag, which no entry meets: " + none.Reason);
            Assert.That(_entry.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("renamed"));
        }

        [Test]
        public void A_write_that_would_make_an_attachment_the_gate_cannot_read_is_refused_with_nothing_changed()
        {
            // The vault keeps what a client wrote as a protected attachment, and KeePass keeps a
            // protected attachment made while the hook stands through the hook's cipher. Stored,
            // it would fail the tag of the change that stored it, and every read of its entry
            // after that, so the write is refused before anything on the entry is changed: no
            // attachment added, the attachment it would have replaced still the object it was,
            // no revision, nothing touched and nothing saved.
            var uuid = Uuid(_entry);
            var notes = _entry.Binaries.Get("notes.txt");
            var revisions = _entry.History.UCount;
            var modified = _database.Database.Modified;
            var held = _database.Database.HashOfLastIO;
            var touched = _entry.LastModificationTime;
            var tag = _vault.EntryTag(uuid);
            var written = Encoding.UTF8.GetBytes("written by a client");
            var tagged = VaultAskings.Tagged(null, true);

            var writes = new Dictionary<string, Func<VaultWrite>>
            {
                { "create", () => _vault.CreateAttachment(uuid, "added.txt", written, VaultAskings.Free) },
                { "create, tagged", () => _vault.CreateAttachment(uuid, "added.txt", written, tagged) },
                { "replace", () => _vault.UpdateAttachment(uuid, "notes.txt", written, VaultAskings.Free) },
                { "replace, tagged", () => _vault.UpdateAttachment(uuid, "notes.txt", written, tagged) },
            };

            foreach (var write in writes)
            {
                var thrown = Thrown.By(() => write.Value());

                Assert.That(Unreadable(thrown), Is.True, write.Key + " was not refused for the attachment: " + thrown);
                Assert.That(_entry.Binaries.UCount, Is.EqualTo(1), write.Key + " left an attachment behind");
                Assert.That(_entry.Binaries.Get("notes.txt"), Is.SameAs(notes), write.Key + " replaced the attachment");
                Assert.That(_entry.History.UCount, Is.EqualTo(revisions), write.Key + " left a revision behind");
                Assert.That(_entry.LastModificationTime, Is.EqualTo(touched), write.Key + " touched the entry");
                Assert.That(
                    _database.Database.Modified,
                    Is.EqualTo(modified),
                    write.Key + " left something for a save to write");
                Assert.That(_database.Database.HashOfLastIO, Is.EqualTo(held), write.Key);
                Assert.That(_saves, Is.Zero, write.Key + " saved");
            }

            // Asked before KeePass's own read below: that read keeps the attachment through
            // the cipher that stands, after which no tag over it is taken.
            Assert.That(_vault.EntryTag(uuid), Is.EqualTo(tag), "the entry is tagged as it was");
            Assert.That(
                Encoding.UTF8.GetString(notes.ReadData()),
                Is.EqualTo("invented"),
                "and the attachment holds what it held");

            // An attachment of no bytes is kept as it is whatever cipher stands, so it is no
            // attachment the gate cannot read, and writing it goes ahead. Asked for no tag,
            // since KeePass's read above left the other attachment kept through the hook.
            Assert.That(
                _vault.CreateAttachment(uuid, "empty.txt", Array.Empty<byte>(), VaultAskings.Free).Ok,
                Is.True);
            Assert.That(_vault.Whole(uuid).Attachment("empty.txt").Size, Is.Zero);
        }

        [Test]
        public void The_same_writes_go_ahead_once_no_other_cipher_stands()
        {
            // The other half, so that the refusals above are for the hook and nothing else.
            var uuid = Uuid(_entry);
            var written = Encoding.UTF8.GetBytes("written by a client");
            ProtectedBinary.ExtCrypt = null;

            var made = _vault.CreateAttachment(uuid, "added.txt", written, VaultAskings.Tagged(null, true));
            Assert.That(made.Ok, Is.True, made.Reason);
            Assert.That(made.Tag, Does.Match("^[0-9a-f]{32}$"));

            var replaced = _vault.UpdateAttachment(uuid, "notes.txt", written, VaultAskings.Tagged(null, true));
            Assert.That(replaced.Ok, Is.True, replaced.Reason);
            Assert.That(replaced.Tag, Does.Match("^[0-9a-f]{32}$").And.Not.EqualTo(made.Tag));

            var view = _vault.Whole(uuid);
            Assert.That(view.Attachment("added.txt").Bytes(), Is.EqualTo(written));
            Assert.That(view.Attachment("notes.txt").Bytes(), Is.EqualTo(written));
            Assert.That(_saves, Is.EqualTo(2));
        }

        [Test]
        public void A_read_that_has_to_read_such_an_attachment_throws_and_a_read_that_does_not_is_served()
        {
            // Through the real router, which lets what a handler throws out to the transport,
            // where it is answered `500 unreadable`, `GateConversationTests`. A caller that
            // reads attachments is handed a tag over every attachment of the entry, so every
            // read of the entry by that caller meets the attachment the gate cannot read: the
            // entry, the listing of its attachments, the attachment itself, and an attachment
            // beside it. A caller whose rules reach no attachment's bytes meets nothing of it.
            Plant("later.txt", "made through the hook");
            var entry = "/entries/" + Uuid(_entry);
            var reads = new[]
            {
                entry,
                entry + "/fields",
                entry + "/fields/Title",
                entry + "/attachments",
                entry + "/attachments/later.txt",
                entry + "/attachments/notes.txt",
            };

            Granting(("agent", "RLA"));
            foreach (var path in reads)
            {
                foreach (var method in new[] { "GET", "HEAD" })
                {
                    GateContext context = null;
                    var thrown = Thrown.By(() => context = Asked(method, path, "agent"));

                    Assert.That(Unreadable(thrown), Is.True, method + " " + path + ": " + thrown);
                    Assert.That(context, Is.Null, method + " " + path + " was answered");
                }
            }

            // `read` and `list` with no `attachments`: the entry and its strings are served, and
            // the routes about attachments are an entry that is not there.
            Granting(("agent", "RL"));
            foreach (var path in reads)
            {
                var about = path.Contains("/attachments");
                var context = Asked("GET", path, "agent");

                Assert.That(context.Status, Is.EqualTo(about ? 404 : 200), path + " " + context.Document());
                Assert.That(context.Document(), Does.Not.Contain("later.txt"), path);
                Assert.That(Asked("HEAD", path, "agent").Status, Is.EqualTo(context.Status), path);
            }

            // `read` beside a rule that lists attachments and does not read them: the listing is
            // served by name and size, under a tag that covers no attachment's bytes.
            Granting(("agent", "R"), ("lister", "LA"));
            var listed = Asked("GET", entry + "/attachments", "agent", "lister");
            Assert.That(listed.Status, Is.EqualTo(200), listed.Document());
            Assert.That(
                listed.Document(),
                Does.Contain("\"name\":\"later.txt\"").And.Contain("\"name\":\"notes.txt\""));
            Assert.That(listed.Headers["ETag"], Is.Not.Null);
            Assert.That(Asked("GET", entry, "agent", "lister").Status, Is.EqualTo(200));
        }

        [Test]
        public void An_attachment_unreadable_since_its_digest_was_kept_is_refused_before_any_answer()
        {
            // KeePass keeps a protected attachment through whatever cipher stands when it last
            // read the attachment itself: `ReadData` decrypts the buffer and encrypts it again,
            // and a save reads every attachment. So an attachment the gate could read, and
            // digested, can be an attachment the gate cannot read by the time it is asked for.
            // The digest kept is not what decides: how the attachment is kept is asked for
            // every tag, so every answer carrying a tag over it is refused before any status is
            // given, as it is where no digest was kept. Tagged from the digest kept, a read of
            // the attachment was answered `200` and failed where its content was written.
            var notes = _entry.Binaries.Get("notes.txt");
            Assume.That(KeePassMemory.KeptAs(notes), Is.EqualTo("ProtectedMemory"));
            Assert.That(notes.ReadData(), Is.EqualTo(Encoding.UTF8.GetBytes("invented")));
            Assert.That(
                KeePassMemory.KeptAs(notes),
                Is.EqualTo("ExtCrypt"),
                "KeePass kept it through the cipher that stood when it was read");

            Granting(("agent", "RLA"));

            foreach (var under in new[] { "/attachments/notes.txt", "/attachments", string.Empty })
            {
                var thrown = Assert.Catch(() => Asked("GET", "/entries/" + Uuid(_entry) + under, "agent"));
                Assert.That(Unreadable(thrown), Is.True, under + ": " + thrown);
            }
        }

        /// <summary>
        /// Puts a protected attachment on the entry with nothing touched, as KeePass or another
        /// plugin does, made while the hook stands and so kept through it.
        /// </summary>
        private void Plant(string name, string content)
        {
            var planted = new ProtectedBinary(true, Encoding.UTF8.GetBytes(content));
            Assume.That(KeePassMemory.KeptAs(planted), Is.EqualTo("ExtCrypt"));
            _entry.Binaries.Set(name, planted);
        }

        /// <summary>Writes the rules on the root group, as an operator does.</summary>
        private void Granting(params (string Profile, string Mask)[] rules)
        {
            _database.RootGroup.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(rules));
        }

        /// <summary>
        /// Whether what was thrown is, or was thrown on for, an attachment the gate cannot
        /// read, which is how the transport decides what to answer.
        /// </summary>
        private static bool Unreadable(Exception thrown)
        {
            for (var cause = thrown; cause != null; cause = cause.InnerException)
            {
                if (cause is AttachmentUnreadable)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A request through the real router, by a client holding these profiles, with the
        /// answer's content left as the handler handed it over.
        /// </summary>
        private GateContext Asked(string method, string path, params string[] profiles)
        {
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var context = new GateContext(
                    new HttpRequestHead(
                        method,
                        path,
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        0),
                    string.Empty,
                    ClientIdentity.Paired("d1e2f3", certificate, profiles));
                new ServedDatabase(_vault).PutOn(context);

                _router.Handle(context);
                return context;
            }
        }

        /// <summary>A save, with the hook refusing nothing while the file is written.</summary>
        /// <remarks>
        /// What is asked here is the tag, and a save reads every attachment the database holds,
        /// history included, so a decryption refused there would fail the save rather than the tag.
        /// </remarks>
        private void Save(PwDatabase database)
        {
            _saves++;

            var refusing = _refusing;
            var keeping = _refusingToKeep;
            _refusing = false;
            _refusingToKeep = false;
            try
            {
                database.Save(null);
            }
            finally
            {
                _refusing = refusing;
                _refusingToKeep = keeping;
            }
        }

        private static string Uuid(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }
    }
}
