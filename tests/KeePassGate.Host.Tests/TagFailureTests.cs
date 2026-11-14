using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
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
    /// What the vault does when a node's tag cannot be taken, and with an attachment kept
    /// through another plugin's cipher, against a real database while that cipher stands.
    /// </summary>
    /// <remarks>
    /// **The hook is KeePass's own**, `ProtectedBinary.ExtCrypt`, which another plugin can set.
    /// KeePass keeps a protected attachment made while it is set through that cipher, and the
    /// gate reads an attachment a piece at a time out of KeePass's own buffer only where
    /// Windows' protection of process memory keeps the buffer. An attachment made under the
    /// hook is read through KeePass's own read, `StoredBinaries`, and so through the hook:
    /// every read of it and every tag over it is served as any is. The attachment
    /// <see cref="SetUp"/> makes is made before the hook, and digested before it, so it is read
    /// in place and nothing of it goes through the hook.
    ///
    /// **The hook can also be told to refuse**, which stands for a decryption or an encryption
    /// failing once: memory protection refusing, or an allocation running out. What goes
    /// through it of the gate's own is a read of an attachment kept through it, and the
    /// digest a tag keeps for a protected attachment, kept in a container of KeePass's and so
    /// through whatever cipher stands.
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
            // The replacement is an attachment made through the hook, so the tag the answer
            // carries is the first thing to decrypt it.
            var revisions = _entry.History.UCount;
            var modified = _database.Database.Modified;
            var held = _database.Database.HashOfLastIO;
            var notes = _entry.Binaries.Get("notes.txt");

            _refusing = true;
            Assert.Throws<CryptographicException>(() => _vault.UpdateAttachment(
                Uuid(_entry), "notes.txt", Encoding.UTF8.GetBytes("written by a client"), VaultAskings.Tagged(null, true)));
            _refusing = false;

            Assert.That(_entry.Binaries.Get("notes.txt"), Is.SameAs(notes), "the attachment is the object it was");
            Assert.That(Encoding.UTF8.GetString(notes.ReadData()), Is.EqualTo("invented"), "and holds what it held");
            Assert.That(_entry.Binaries.UCount, Is.EqualTo(1));
            Assert.That(_entry.History.UCount, Is.EqualTo(revisions), "with no revision left behind");
            Assert.That(_database.Database.Modified, Is.EqualTo(modified), "and nothing left for a save to write");
            Assert.That(_database.Database.HashOfLastIO, Is.EqualTo(held));
            Assert.That(_saves, Is.Zero, "nothing was saved");
        }

        [Test]
        public void An_attachment_that_failed_to_decrypt_once_is_tagged_once_it_decrypts()
        {
            Plant("later.txt", "made through the hook");

            _refusing = true;
            Assert.Throws<CryptographicException>(() => _vault.EntryTag(Uuid(_entry)));
            _refusing = false;

            var tag = _vault.EntryTag(Uuid(_entry));
            Assert.That(tag, Does.Match("^[0-9a-f]{32}$"), "the failure is not what the next tag is answered with");
            Assert.That(_vault.EntryTag(Uuid(_entry)), Is.EqualTo(tag));
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
            // An attachment that will not decrypt, so that any tag taken of the entry throws,
            // and a write that takes a tag where nothing reads it is a write that fails for
            // nothing.
            Plant("later.txt", "made through the hook");
            _refusing = true;
            Assert.Throws<CryptographicException>(() => _vault.EntryTag(Uuid(_entry)));

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
        public void An_attachment_written_while_another_plugin_s_cipher_stands_is_stored_and_read_back()
        {
            // The vault keeps what a client wrote as a protected attachment, and KeePass keeps a
            // protected attachment made while the hook stands through the hook's cipher. The
            // gate reads no piece of such an attachment in place and has KeePass read it, so
            // the write goes ahead, its answer carries a tag over what it stored, and a read
            // after it hands back what was written.
            var uuid = Uuid(_entry);
            var written = Encoding.UTF8.GetBytes("written by a client");

            var made = _vault.CreateAttachment(uuid, "added.txt", written, VaultAskings.Tagged(null, true));
            Assert.That(made.Ok, Is.True, made.Reason);
            Assert.That(made.Tag, Does.Match("^[0-9a-f]{32}$"));
            Assert.That(KeePassMemory.KeptAs(_entry.Binaries.Get("added.txt")), Is.EqualTo("ExtCrypt"));

            var replaced = _vault.UpdateAttachment(uuid, "notes.txt", written, VaultAskings.Tagged(null, true));
            Assert.That(replaced.Ok, Is.True, replaced.Reason);
            Assert.That(replaced.Tag, Does.Match("^[0-9a-f]{32}$").And.Not.EqualTo(made.Tag));

            var view = _vault.Whole(uuid);
            Assert.That(view.Attachment("added.txt").Bytes(), Is.EqualTo(written));
            Assert.That(view.Attachment("notes.txt").Bytes(), Is.EqualTo(written));
            Assert.That(view.Tag, Is.EqualTo(replaced.Tag), "a read is tagged as the change that left the entry so");
            Assert.That(_saves, Is.EqualTo(2));
        }

        [Test]
        public void Every_read_of_an_entry_holding_an_attachment_kept_through_another_plugin_s_cipher_is_served()
        {
            // Through the real router. A caller that reads attachments is handed a tag over
            // every attachment of the entry, so every read of the entry by that caller reads
            // the attachment the gate reads no piece of in place: the entry, its fields, the
            // listing of its attachments, the attachment itself, and an attachment beside it.
            // Each is served, under a single tag, and the attachment holds what was stored.
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
            var tags = new HashSet<string>();
            foreach (var path in reads)
            {
                foreach (var method in new[] { "GET", "HEAD" })
                {
                    var context = Asked(method, path, "agent");

                    Assert.That(context.Status, Is.EqualTo(200), method + " " + path);
                    Assert.That(context.Headers["ETag"], Is.Not.Null, method + " " + path);
                    tags.Add(context.Headers["ETag"]);
                }
            }

            Assert.That(tags, Has.Count.EqualTo(1), "the reads of an entry nothing changed disagree about its tag");

            var read = Newtonsoft.Json.Linq.JObject.Parse(Asked("GET", entry + "/attachments/later.txt", "agent").Document());
            Assert.That(
                (string)read["content"],
                Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes("made through the hook"))));
            Assert.That(KeePassMemory.KeptAs(_entry.Binaries.Get("later.txt")), Is.EqualTo("ExtCrypt"));
        }

        [Test]
        public void An_attachment_KeePass_came_to_keep_another_way_since_its_digest_was_kept_is_served_as_it_was()
        {
            // KeePass keeps a protected attachment through whatever cipher stands when it last
            // read the attachment itself: `ReadData` decrypts the buffer and encrypts it again,
            // and a save reads every attachment. So an attachment the gate read in place, and
            // digested, can be an attachment the gate reads through KeePass by the time it is
            // asked for. What it holds is what it held, so it is served under the tag it was
            // served under, holding the bytes it held.
            Granting(("agent", "RLA"));
            var path = "/entries/" + Uuid(_entry) + "/attachments/notes.txt";
            var before = Asked("GET", path, "agent");
            Assert.That(before.Status, Is.EqualTo(200));

            var notes = _entry.Binaries.Get("notes.txt");
            Assume.That(KeePassMemory.KeptAs(notes), Is.EqualTo("ProtectedMemory"));
            Assert.That(notes.ReadData(), Is.EqualTo(Encoding.UTF8.GetBytes("invented")));
            Assert.That(
                KeePassMemory.KeptAs(notes),
                Is.EqualTo("ExtCrypt"),
                "KeePass kept it through the cipher that stood when it was read");

            var after = Asked("GET", path, "agent");
            Assert.That(after.Status, Is.EqualTo(200));
            Assert.That(after.Headers["ETag"], Is.EqualTo(before.Headers["ETag"]));
            Assert.That(after.Document(), Is.EqualTo(before.Document()));
            Assert.That(
                (string)Newtonsoft.Json.Linq.JObject.Parse(after.Document())["content"],
                Is.EqualTo(Convert.ToBase64String(Encoding.UTF8.GetBytes("invented"))));
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
