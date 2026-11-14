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
    /// A write is never refused for what its entry's strings come to, so no write tells its
    /// caller how long a value it may not read is.
    /// </summary>
    /// <remarks>
    /// A write that would take an entry's strings past <see cref="VaultEntryView.MostCharacters"/>
    /// was refused `413` with the entry's whole count, protected values included. A caller that
    /// may write and not read padded the entry to just under the ceiling and read the count off
    /// the refusal, or searched for the padding the refusal began at, and had the length of every
    /// value it was withheld. Every case here is the real router over the real vault and the
    /// rules as an operator writes them: two entries alike but for the length of a password the
    /// caller may not read, the same padding in both, and the same write sent to each. The
    /// operator's ruling of 2026-11-10 takes the refusal away, so both writes are made and
    /// answered alike, and the ceiling is held on reads, over what the reader is handed.
    /// </remarks>
    [TestFixture]
    public sealed class NoSizeToldToAWriterTests
    {
        private const string Title = "an invented service";
        private const string Pad = "Pad";

        // What every write here adds to the entry: a value where an empty string was, or a name
        // and a value together.
        private const string Written = "0123456789";

        // The two passwords differ by more than the room the padding leaves, so that under the
        // refusal the same write fit the entry holding the first and not the entry holding the
        // second.
        private const int Room = 5;
        private static readonly string Shorter = new string('p', 10);
        private static readonly string Longer = new string('p', 10 + (2 * Room));

        private TemporaryDatabase _database;
        private PwGroup _group;
        private SpecRouter _router;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.AddGroup(_group, true);

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
            _saves = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [TestCase("field", "UP", TestName = "A field updated by a caller that may not read")]
        [TestCase("entry", "UP", TestName = "An entry updated by a caller that may not read")]
        [TestCase("new field", "CP", TestName = "A field added by a caller that may not read")]
        [TestCase("field", "RU", TestName = "A field updated by a caller that reads no protected value")]
        public void A_write_past_the_ceiling_is_answered_alike_whatever_a_withheld_value_holds(
            string what, string mask)
        {
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", mask)));
            var shorter = Padded(Shorter);
            var longer = Padded(Longer);

            var first = Send(what, shorter);
            var second = Send(what, longer);

            Assert.That(first.Status, Is.EqualTo(what == "new field" ? 201 : 200), first.Json);
            Assert.That(second.Status, Is.EqualTo(first.Status), second.Json);
            Assert.That(WithoutTag(second.Json), Is.EqualTo(WithoutTag(first.Json)));
            Assert.That(_saves, Is.EqualTo(2), "both writes were made");
            Assert.That(Characters(shorter), Is.LessThanOrEqualTo(VaultEntryView.MostCharacters));
            Assert.That(Characters(longer), Is.GreaterThan(VaultEntryView.MostCharacters),
                "the second entry is past what a read hands out whole, and was written to all the same");
            Assert.That(shorter.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo(Shorter));
            Assert.That(longer.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo(Longer));
        }

        [Test]
        public void A_read_is_held_to_what_its_caller_is_handed_and_never_to_a_withheld_value()
        {
            // The same two entries, read by a caller whose rule reaches no protected value. Each
            // view holds the same names and the same open values, so each read is answered the
            // same, and the password's length is in neither answer.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RU")));
            var shorter = Padded(Shorter);
            var longer = Padded(Longer);
            Assume.That(Send("field", shorter).Status, Is.EqualTo(200));
            Assume.That(Send("field", longer).Status, Is.EqualTo(200));
            Assume.That(Characters(longer), Is.GreaterThan(VaultEntryView.MostCharacters));

            var first = Asked("GET", Path(shorter), null, "agent");
            var second = Asked("GET", Path(longer), null, "agent");

            Assert.That(first.Status, Is.EqualTo(200), "what this caller is handed is under the ceiling");
            Assert.That(second.Status, Is.EqualTo(200), "and the longer password it is withheld adds nothing");

            // Each view stands under the ceiling by the room left and by the shorter password,
            // which is in neither. A character more than that in sight takes both views over, by
            // the same count.
            var over = new string('u', Written.Length + Room + Shorter.Length + 1);
            Assume.That(Asked("PUT", Path(shorter) + "/fields/" + PwDefs.UserNameField,
                Bodies.Of(("value", over)), "agent").Status, Is.EqualTo(200));
            Assume.That(Asked("PUT", Path(longer) + "/fields/" + PwDefs.UserNameField,
                Bodies.Of(("value", over)), "agent").Status, Is.EqualTo(200));

            first = Asked("GET", Path(shorter), null, "agent");
            second = Asked("GET", Path(longer), null, "agent");

            Assert.That(first.Status, Is.EqualTo(413));
            Assert.That(second.Status, Is.EqualTo(413));
            Assert.That(second.Json, Is.EqualTo(first.Json), "the count is of the view, which is the same for both");
        }

        [Test]
        public void A_withheld_field_past_the_ceiling_is_answered_as_withheld()
        {
            // The single field read decides whether the value is withheld before it measures
            // anything, so a protected string of any length is the `null` every withheld value is.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "R")));
            var entry = Padded(new string('p', VaultEntryView.MostCharacters + 1));

            var read = Asked("GET", Path(entry) + "/fields/" + PwDefs.PasswordField, null, "agent");

            Assert.That(read.Status, Is.EqualTo(200), read.Json);
            Assert.That(read.Json, Is.EqualTo("{\"name\":\"Password\",\"value\":null,\"protected\":true}"));
        }

        /// <summary>
        /// An entry holding this password and padding in the open, left so that the write
        /// <see cref="Send"/> makes brings its open strings, with a password of
        /// <see cref="Shorter"/>'s length, to <see cref="Room"/> characters under the ceiling.
        /// </summary>
        /// <remarks>
        /// The padding is planted, where the caller would have written it a field at a time: it
        /// is the same in both entries and is the caller's own, so nothing is learned from it.
        /// </remarks>
        private PwEntry Padded(string password)
        {
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, Title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, string.Empty));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
            entry.Strings.Set(Pad, new ProtectedString(false, string.Empty));

            var held = Characters(entry) - password.Length + Shorter.Length;
            var padding = VaultEntryView.MostCharacters - Room - held - Written.Length;
            entry.Strings.Set(Pad, new ProtectedString(false, new string('x', padding)));

            _group.AddEntry(entry, true);
            return entry;
        }

        private GateContext Send(string what, PwEntry entry)
        {
            switch (what)
            {
                case "field":
                    return Asked("PUT", Path(entry) + "/fields/" + PwDefs.UserNameField,
                        Bodies.Of(("value", Written)), "agent");
                case "entry":
                    return Asked("PUT", Path(entry), Bodies.Of((PwDefs.UserNameField, Written)), "agent");
                case "new field":
                    return Asked("POST", Path(entry) + "/fields",
                        Bodies.Of(("name", Written.Substring(0, 4)), ("value", Written.Substring(4))), "agent");
                default:
                    throw new ArgumentOutOfRangeException(nameof(what), what, null);
            }
        }

        private static string Path(PwEntry entry)
        {
            return "/entries/" + entry.Uuid.ToHexString();
        }

        private static int Characters(PwEntry entry)
        {
            return entry.Strings.Sum(pair => pair.Key.Length + pair.Value.Length);
        }

        /// <summary>
        /// An answer's body without the tag a reader is handed, which is of its own entry and
        /// differs between two entries for their identifiers and times alone.
        /// </summary>
        private static string WithoutTag(string json)
        {
            var at = json.IndexOf(",\"etag\"", StringComparison.Ordinal);
            return at < 0 ? json : json.Substring(0, at) + "}";
        }

        private GateContext Asked(string method, string path, string body, params string[] profiles)
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
                        method,
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
