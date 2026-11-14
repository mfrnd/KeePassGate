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
    /// A write that would take an entry's strings past <see cref="Ceiling"/> was refused `413`
    /// with the entry's whole count, protected values included. A caller that may write and not
    /// read padded the entry to just under the ceiling and read the count off the refusal, or
    /// searched for the padding the refusal began at, and had the length of every value it was
    /// withheld. Every case here is the real router over the real vault and the rules as an
    /// operator writes them: two entries alike but for the length of a password the caller may
    /// not read, the same padding in both, and the same write sent to each. The operator's
    /// ruling of 2026-11-10 takes the refusal away, so both writes are made and answered alike.
    /// The ruling of 2026-11-14 takes the ceiling off reads as well: the answer to a read of an
    /// entry or of a field is written as it is sent, so no read is refused for size, and a
    /// withheld value is still no part of what its caller is handed.
    /// </remarks>
    [TestFixture]
    public sealed class NoSizeToldToAWriterTests
    {
        /// <summary>
        /// The number a write was refused past, and a read after it. Nothing is held to it
        /// now.
        /// </summary>
        private const int Ceiling = 512 * 1024;

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
        public void A_write_past_the_number_is_answered_alike_whatever_a_withheld_value_holds(
            string what, string mask)
        {
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", mask)));
            var shorter = Padded(Shorter);
            var longer = Padded(Longer);

            var first = Send(what, shorter);
            var second = Send(what, longer);

            Assert.That(first.Status, Is.EqualTo(what == "new field" ? 201 : 200), first.Document());
            Assert.That(second.Status, Is.EqualTo(first.Status), second.Document());
            Assert.That(WithoutTag(second.Document()), Is.EqualTo(WithoutTag(first.Document())));
            Assert.That(_saves, Is.EqualTo(2), "both writes were made");
            Assert.That(Characters(shorter), Is.LessThanOrEqualTo(Ceiling));
            Assert.That(Characters(longer), Is.GreaterThan(Ceiling),
                "the second entry is past the number a write was refused at, and was written to all the same");
            Assert.That(shorter.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo(Shorter));
            Assert.That(longer.Strings.ReadSafe(PwDefs.PasswordField), Is.EqualTo(Longer));
        }

        [Test]
        public void A_read_is_answered_alike_whatever_a_withheld_value_holds_and_however_much_is_in_sight()
        {
            // The same two entries, read by a caller whose rule reaches no protected value. Each
            // view holds the same names and the same open values, so each read is answered the
            // same, and the password's length is in neither answer.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RU")));
            var shorter = Padded(Shorter);
            var longer = Padded(Longer);
            Assume.That(Send("field", shorter).Status, Is.EqualTo(200));
            Assume.That(Send("field", longer).Status, Is.EqualTo(200));
            Assume.That(Characters(longer), Is.GreaterThan(Ceiling));

            var first = Asked("GET", Path(shorter), null, "agent");
            var second = Asked("GET", Path(longer), null, "agent");

            Assert.That(first.Status, Is.EqualTo(200));
            Assert.That(second.Status, Is.EqualTo(200));
            Assert.That(Fields(second), Is.EqualTo(Fields(first)), "the longer password it is withheld adds nothing");
            Assert.That(Fields(first), Does.Contain("\"Password\":null"));

            // What is in sight grows past the number a read was refused at, in both entries
            // alike. Both are still served, each value as it is stored, and still alike.
            var over = new string('u', Written.Length + Room + Shorter.Length + 1);
            Assume.That(Asked("PUT", Path(shorter) + "/fields/" + PwDefs.UserNameField,
                Bodies.Of(("value", over)), "agent").Status, Is.EqualTo(200));
            Assume.That(Asked("PUT", Path(longer) + "/fields/" + PwDefs.UserNameField,
                Bodies.Of(("value", over)), "agent").Status, Is.EqualTo(200));
            Assume.That(
                Characters(shorter) - Shorter.Length,
                Is.GreaterThan(Ceiling),
                "what this caller is handed has to be past the number for this to test anything");

            first = Asked("GET", Path(shorter), null, "agent");
            second = Asked("GET", Path(longer), null, "agent");

            Assert.That(first.Status, Is.EqualTo(200), "no read is refused for what it hands over");
            Assert.That(second.Status, Is.EqualTo(200));
            Assert.That(Fields(second) == Fields(first), Is.True, "the answers differ in what they hand over");
            Assert.That(Fields(first), Does.Contain("\"UserName\":\"" + over + "\"").And.Contain("\"Password\":null"));
        }

        [Test]
        public void A_withheld_field_of_any_length_is_answered_as_withheld_and_read_whole_by_a_caller_it_is_open_to()
        {
            // A withheld value has nothing behind it to read, so a protected string of any
            // length is the `null` every withheld value is. A caller whose rule reaches
            // protected values is handed the same string whole, since no read is refused for
            // how long a value is.
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "R"), ("keeper", "RP")));
            var password = new string('p', Ceiling + 1);
            var entry = Padded(password);

            var read = Asked("GET", Path(entry) + "/fields/" + PwDefs.PasswordField, null, "agent");

            Assert.That(read.Status, Is.EqualTo(200), read.Document());
            Assert.That(read.Document(), Is.EqualTo("{\"name\":\"Password\",\"value\":null,\"protected\":true}"));

            var open = Asked("GET", Path(entry) + "/fields/" + PwDefs.PasswordField, null, "keeper");

            Assert.That(open.Status, Is.EqualTo(200));
            Assert.That(
                open.Document() == "{\"name\":\"Password\",\"value\":\"" + password + "\",\"protected\":true}",
                Is.True,
                "the value came back other than it is stored");
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
            var padding = Ceiling - Room - held - Written.Length;
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

        /// <summary>
        /// The strings a read of an entry answered with, as they were written: what two entries
        /// alike in what their caller is handed answer alike, where their identifiers and times
        /// differ.
        /// </summary>
        private static string Fields(GateContext read)
        {
            var json = read.Document();
            var at = json.IndexOf("\"fields\":", StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0), "the answer names no fields");
            return json.Substring(at);
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

                // An answer written as it is sent is written here, as the transport writes it, so
                // a read of an entry or of a field driven from here reads its values.
                context.Document();
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
