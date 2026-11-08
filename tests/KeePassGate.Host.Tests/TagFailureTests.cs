using System.Security.Cryptography;
using System.Text;
using KeePassGate.Bridge;
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
            _database = TemporaryDatabase.New();
            _vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, database => { _saves++; database.Save(null); }, null, null, null),
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
            _database.Dispose();
            ProtectedBinary.ExtCrypt = null;
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
                Uuid(_entry), "notes.txt", Encoding.UTF8.GetBytes("written by a client"), new VaultTagging(null, true)));
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

        private static string Uuid(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }
    }
}
