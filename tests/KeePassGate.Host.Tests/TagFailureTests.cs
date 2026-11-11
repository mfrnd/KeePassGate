using System.Security.Cryptography;
using System.Text;
using KeePassGate.Bridge;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What the vault does when a node's tag cannot be taken, against a real database whose
    /// attachments are protected through a hook that can be told to refuse.
    /// </summary>
    /// <remarks>
    /// **The hook is KeePass's own**, `ProtectedBinary.ExtCrypt`, which another plugin can set and
    /// which stands here for every way a decryption fails once: memory protection refusing, or an
    /// allocation running out. It applies to an attachment made while it is set, so the attachment
    /// <see cref="SetUp"/> makes is read without it and every one a test makes afterwards through it.
    /// It is a static of KeePass's, so <see cref="TearDown"/> takes it away again once the database
    /// that holds such attachments is gone.
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
        private bool _refusing;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _refusing = false;
            _saves = 0;
            _database = TemporaryDatabase.New();
            _vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, Save, null, null, null),
                TestTags.Fixed,
                null);

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _entry.Binaries.Set("notes.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("invented")));
            _database.RootGroup.AddEntry(_entry, true);
            _database.Save();

            ProtectedBinary.ExtCrypt = (data, flags, id) =>
            {
                if (flags == PbCryptFlags.Decrypt && _refusing)
                {
                    throw new CryptographicException("an invented decryption that fails");
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
            // The replacement is an attachment made through the hook, so the tag the answer carries
            // is the first thing to decrypt it.
            var revisions = _entry.History.UCount;
            var modified = _database.Database.Modified;
            var held = _database.Database.HashOfLastIO;

            _refusing = true;
            Assert.Throws<CryptographicException>(() => _vault.UpdateAttachment(
                Uuid(_entry), "notes.txt", Encoding.UTF8.GetBytes("written by a client"), VaultAskings.Tagged(null, true)));
            _refusing = false;

            Assert.That(Encoding.UTF8.GetString(_entry.Binaries.Get("notes.txt").ReadData()), Is.EqualTo("invented"),
                "the attachment holds what it held");
            Assert.That(_entry.History.UCount, Is.EqualTo(revisions), "with no revision left behind");
            Assert.That(_database.Database.Modified, Is.EqualTo(modified), "and nothing left for a save to write");
            Assert.That(_database.Database.HashOfLastIO, Is.EqualTo(held));
            Assert.That(_saves, Is.Zero, "nothing was saved");
        }

        [Test]
        public void An_attachment_that_failed_to_decrypt_once_is_tagged_once_it_decrypts()
        {
            _entry.Binaries.Set("later.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("made through the hook")));

            _refusing = true;
            Assert.Throws<CryptographicException>(() => _vault.EntryTag(Uuid(_entry)));
            _refusing = false;

            Assert.That(_vault.EntryTag(Uuid(_entry)), Does.Match("^[0-9a-f]{32}$"),
                "the failure is not what the next tag is answered with");
        }

        [Test]
        public void A_change_takes_no_tag_that_neither_its_condition_nor_its_answer_reads()
        {
            // An attachment that will not decrypt, so that any tag taken of the entry throws, and a
            // write that takes one where nothing reads it is a write that fails for nothing.
            _entry.Binaries.Set("later.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("made through the hook")));
            _refusing = true;

            var taken = _vault.DeleteAttachment(Uuid(_entry), "later.txt", VaultAskings.Tagged(null, true));
            Assert.That(taken.Ok, Is.True, "no condition, and an answer tagging what is left: " + taken.Reason);
            Assert.That(taken.Tag, Does.Match("^[0-9a-f]{32}$"));

            _entry.Binaries.Set("later.txt", new ProtectedBinary(true, Encoding.UTF8.GetBytes("made through the hook")));

            var any = _vault.UpdateField(
                Uuid(_entry), PwDefs.TitleField, "renamed", VaultAskings.Tagged(VaultPrecondition.Read("*"), false));
            Assert.That(any.Ok, Is.True, "a condition every entry that is there meets: " + any.Reason);

            var none = _vault.UpdateField(
                Uuid(_entry), PwDefs.TitleField, "renamed again", VaultAskings.Tagged(VaultPrecondition.Read("W/\"0\""), false));
            Assert.That(none.IsPreconditionFailed, Is.True, "a condition naming no tag, which no entry meets: " + none.Reason);
            Assert.That(_entry.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo("renamed"));
        }

        /// <summary>A save, with decryption going through while the file is written.</summary>
        /// <remarks>
        /// What is asked here is the tag, and a save reads every attachment the database holds,
        /// history included, so a decryption refused there would fail the save rather than the tag.
        /// </remarks>
        private void Save(PwDatabase database)
        {
            _saves++;

            var refusing = _refusing;
            _refusing = false;
            try
            {
                database.Save(null);
            }
            finally
            {
                _refusing = refusing;
            }
        }

        private static string Uuid(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }
    }
}
