using System;
using System.Collections.Generic;
using System.Linq;
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
    /// An update that would change nothing is skipped only for a caller that may read every
    /// value it names.
    /// </summary>
    /// <remarks>
    /// A skipped update takes no revision, moves no time and saves nothing, and an update that
    /// writes does all three. A caller that may write a value and not read it could therefore
    /// send a guess and learn whether the stored value was its guess, from the listing's
    /// `modified` or from how long the answer took. Every case here is the real router over the
    /// real vault and the rules as an operator writes them, and what is counted is the save,
    /// since a guess that reached a save cost what a wrong guess costs.
    /// </remarks>
    [TestFixture]
    public sealed class SkippedOnlyForAReaderTests
    {
        private const string Open = "kept in the open";
        private const string Title = "an invented service";
        private const string Secret = "not-a-real-password";
        private const string Notes = "invented notes";

        private static readonly byte[] Bytes = { 1, 2, 3 };

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;
        private SpecRouter _router;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _group.Notes = Notes;
            _database.RootGroup.AddGroup(_group, true);

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Title));
            _entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, Open));
            _entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Secret));
            _entry.Binaries.Set("note.txt", new ProtectedBinary(true, Bytes));
            _group.AddEntry(_entry, true);

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
            _saves = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        // What is sent is what the database already holds, every time. A rule that reads what
        // the request names is answered without a write, and a rule that does not is written.
        [TestCase("field", "RU", true, TestName = "An open field its caller may read")]
        [TestCase("field", "U", false, TestName = "An open field its caller may not read")]
        [TestCase("field", "UL", false, TestName = "An open field its caller may only list")]
        [TestCase("secret", "RUP", true, TestName = "A protected field its caller may read")]
        [TestCase("secret", "UP", false, TestName = "A protected field its caller may only set")]
        [TestCase("secret", "ULP", false, TestName = "A protected field its caller may set and list")]
        [TestCase("entry", "RU", true, TestName = "An entry's open field its caller may read")]
        [TestCase("entry", "U", false, TestName = "An entry's open field its caller may not read")]
        [TestCase("entry secret", "RUP", true, TestName = "An entry's password its caller may read")]
        [TestCase("entry secret", "UP", false, TestName = "An entry's password its caller may only set")]
        [TestCase("attachment", "RUA", true, TestName = "An attachment its caller may read")]
        [TestCase("attachment", "UA", false, TestName = "An attachment its caller may only replace")]
        [TestCase("group", "RU", true, TestName = "A group its caller may read")]
        [TestCase("group", "U", false, TestName = "A group its caller may not read")]
        [TestCase("group", "UL", false, TestName = "A group its caller may only list")]
        public void What_is_already_there_is_skipped_only_for_a_caller_that_may_read_it(
            string what, string mask, bool skipped)
        {
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", mask)));
            var revisions = _entry.History.UCount;

            var answer = Send(what, "agent");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(skipped ? 0 : 1), "saves");
            Assert.That(_entry.Strings.ReadSafe(PwDefs.UserNameField), Is.EqualTo(Open));
            Assert.That(_entry.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo(Secret));
            Assert.That(_entry.Binaries.Get("note.txt").ReadData(), Is.EqualTo(Bytes));
            Assert.That(_group.Notes, Is.EqualTo(Notes));

            if (what != "group")
            {
                Assert.That(_entry.History.UCount, Is.EqualTo(skipped ? revisions : revisions + 1), "revisions");
            }
        }

        [Test]
        public void Reading_through_one_profile_and_writing_through_another_is_reading()
        {
            // Profiles are combined with or, as a read combines them: a client that can read the
            // value through any profile it holds learns nothing from a skipped write.
            _group.CustomData.Set(
                AclDocument.CustomDataKey,
                Rules.Granting(("reader", "R"), ("writer", "U")));

            var answer = Send("field", "reader", "writer");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(0));
        }

        [Test]
        public void Reading_the_entry_and_setting_its_secret_through_another_rule_is_not_reading_the_secret()
        {
            // `read` and `protected` in one rule, as a read is held to: a rule that reads the
            // entry without its protected values beside a rule that may set them reads no secret.
            _group.CustomData.Set(
                AclDocument.CustomDataKey,
                Rules.Granting(("reader", "R"), ("writer", "UP")));

            var answer = Send("secret", "reader", "writer");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(1));
        }

        [TestCase("title", "R LP UP", true, TestName = "A protected title a listing shows its caller, beside read")]
        [TestCase("entry title", "R LP UP", true, TestName = "An entry's protected title a listing shows its caller, beside read")]
        [TestCase("title", "R UP", false, TestName = "A protected title its caller reads the entry without")]
        [TestCase("title", "UP", false, TestName = "A protected title its caller may only set")]
        [TestCase("title", "LP UP", false, TestName = "A protected title its caller is listed and does not read the entry of")]
        [TestCase("secret", "R LP UP", false, TestName = "A password beside a title a listing shows its caller")]
        public void A_protected_title_already_there_is_skipped_only_for_a_caller_whose_view_holds_it(
            string what, string masks, bool skipped)
        {
            // A caller's view of an entry holds what any route shows that caller, so a protected
            // title a listing shows it, under `list` with `protected`, is a value it reads once
            // it holds `read` beside: writing it again tells that caller nothing, and is skipped.
            // The listing's reach opens no other protected string, and without `read` a view
            // holds no value at all. Each mask is a rule of its own, under a profile of its own.
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(true, Title));
            var rules = masks.Split(' ').Select((mask, index) => ("profile" + index, mask)).ToArray();
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(rules));
            var revisions = _entry.History.UCount;

            var answer = Send(what, rules.Select(rule => rule.Item1).ToArray());

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(skipped ? 0 : 1), "saves");
            Assert.That(_entry.History.UCount, Is.EqualTo(skipped ? revisions : revisions + 1), "revisions");
            Assert.That(_entry.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo(Title));
            Assert.That(_entry.Strings.Get(PwDefs.TitleField).IsProtected, Is.True);
        }

        [TestCase("R LP UP", true)]
        [TestCase("R UP", false)]
        public void A_title_the_database_protects_is_skipped_only_for_a_caller_whose_view_holds_it(
            string masks, bool skipped)
        {
            // The same, for a title protected by the database's setting and carrying no flag of
            // its own: what counts as protected is the database's answer, for the skip as for
            // the listing and the read.
            _database.Database.MemoryProtection.ProtectTitle = true;
            var rules = masks.Split(' ').Select((mask, index) => ("profile" + index, mask)).ToArray();
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(rules));

            var answer = Send("title", rules.Select(rule => rule.Item1).ToArray());

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(skipped ? 0 : 1), "saves");
            Assert.That(_entry.Strings.ReadSafe(PwDefs.TitleField), Is.EqualTo(Title));
        }

        [TestCase("nothing")]
        [TestCase("group nothing")]
        public void A_request_naming_nothing_is_skipped_for_everybody(string what)
        {
            // Nothing in it is a guess at anything.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "U")));

            var answer = Send(what, "agent");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(0));
        }

        [Test]
        public void An_expiry_alone_is_a_value_its_caller_has_to_read_to_be_skipped()
        {
            _entry.Expires = false;
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "U")));

            var answer = Send("expiry", "agent");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(_saves, Is.EqualTo(1));
        }

        private GateContext Send(string what, params string[] profiles)
        {
            var entry = "/entries/" + _entry.Uuid.ToHexString();

            switch (what)
            {
                case "field":
                    return Put(entry + "/fields/" + PwDefs.UserNameField, Bodies.Of(("value", Open)), profiles);
                case "secret":
                    return Put(entry + "/fields/" + PwDefs.PasswordField, Bodies.Of(("value", Secret)), profiles);
                case "title":
                    return Put(entry + "/fields/" + PwDefs.TitleField, Bodies.Of(("value", Title)), profiles);
                case "entry title":
                    return Put(entry, Bodies.Of((PwDefs.TitleField, Title)), profiles);
                case "entry":
                    return Put(entry, Bodies.Of((PwDefs.UserNameField, Open)), profiles);
                case "entry secret":
                    return Put(entry, Bodies.Of((PwDefs.PasswordField, Secret)), profiles);
                case "expiry":
                    return Put(entry, Bodies.Of(("expires", string.Empty)), profiles);
                case "nothing":
                    return Put(entry, "{}", profiles);
                case "attachment":
                    return Put(
                        entry + "/attachments/note.txt",
                        Bodies.Of(("content", Convert.ToBase64String(Bytes))),
                        profiles);
                case "group":
                    return Put("/groups/" + _group.Uuid.ToHexString(), Bodies.Of(("Notes", Notes)), profiles);
                case "group nothing":
                    return Put("/groups/" + _group.Uuid.ToHexString(), "{}", profiles);
                default:
                    throw new ArgumentOutOfRangeException(nameof(what), what, null);
            }
        }

        private GateContext Put(string path, string body, string[] profiles)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(
                    new Immediate(),
                    database =>
                    {
                        _saves++;
                        database.Save(null);
                    },
                    null,
                    null,
                    null),
                TestTags.Fixed,
                null);

            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var context = new GateContext(
                    new HttpRequestHead(
                        "PUT",
                        path,
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        Bodies.LengthOf(body)),
                    body,
                    ClientIdentity.Paired("d1e2f3", certificate, profiles));
                new ServedDatabase(vault).PutOn(context);

                _router.Handle(context);
                return context;
            }
        }

        /// <summary>KeePass's thread with nothing ahead of the write.</summary>
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
}
