using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Bridge;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The plugin's store is absent on every route the routing document declares, and nothing a
    /// route writes reaches it, `STORE.md` §633.
    /// </summary>
    /// <remarks>
    /// **The test the store's authority entry is conditional on**, decision 8 as `STORE.md` §633
    /// cites it: an authority's private key and the store key are kept in an ordinary entry only
    /// while no route can reach that entry. So the cases are generated from the document the router
    /// is built from, and an operation with no case here fails, as the router refuses to load an
    /// operation with no handler: a route added later is held to the store before it ships.
    ///
    /// **One list, <see cref="Cases"/>, of how each operation names a node**, and each label is
    /// held to the operation's own shape, so a case cannot claim that a route names less than it
    /// does. Every way an operation names a node is driven with every node of the store: the
    /// plugin's group, a group inside it and a second group with its prefix elsewhere, records
    /// inside the group and outside it, in the recycle bin, under a second store key, damaged,
    /// stripped, a copy under another identifier and a record of a kind the plugin does not
    /// know. Each answer is held, byte for byte with its headers, to the same request about a
    /// node that is not there. A listing, and a read of an ordinary node, is held to its answer
    /// before the store was added, so nothing of the store is in any listing.
    ///
    /// **With a rule granting everything at the root**, every verb and both switches, so that
    /// nothing the store is answered with can be put down to a rule: a rule the product wrote into
    /// the store it keeps, beside rules granting everything at the plugin's own group and at a
    /// record of it, which reach nothing.
    ///
    /// **And again with what a merge of rules brings**: rules changed apart in two copies by the
    /// product's own OKs, a renewal below among them, and merged, every record of the plugin's
    /// group then a node of the store each route is driven with.
    ///
    /// The real router over the real vault and a real database, as `DecidedAtTheReadTests` drives
    /// them, since identifiers, groups and the flat sweep are KeePass's.
    /// </remarks>
    [TestFixture]
    public sealed class StoreOnEveryRouteTests
    {
        /// <summary>
        /// How each declared operation names a node, by operation: the one list of these cases,
        /// which `STORE.md` §633 points to.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, Names> Cases = new Dictionary<string, Names>(StringComparer.Ordinal)
        {
            // No group or entry: the gate itself, the caller's own certificate, and pairing.
            { "getStatus", Names.Nothing },
            { "headStatus", Names.Nothing },
            { "getIdentity", Names.Nothing },
            { "headIdentity", Names.Nothing },
            { "revokeCertificate", Names.Nothing },
            { "renewCertificate", Names.Nothing },
            { "pairHello", Names.Nothing },
            { "pairRequest", Names.Nothing },

            // The whole tree, anchored at the root.
            { "listGroups", Names.Tree },
            { "headGroups", Names.Tree },
            { "listEntries", Names.Tree },
            { "headEntries", Names.Tree },

            // A group, by the identifier in the path.
            { "readGroup", Names.Group },
            { "headGroup", Names.Group },
            { "updateGroup", Names.Group },
            { "deleteGroup", Names.Group },
            { "listSubgroups", Names.Group },
            { "headSubgroups", Names.Group },
            { "createGroup", Names.Group },
            { "listGroupEntries", Names.Group },
            { "headGroupEntries", Names.Group },
            { "createEntry", Names.Group },
            { "getGroupAccess", Names.Group },
            { "headGroupAccess", Names.Group },

            // An entry, by the identifier in the path.
            { "readEntry", Names.Entry },
            { "headEntry", Names.Entry },
            { "updateEntry", Names.Entry },
            { "deleteEntry", Names.Entry },
            { "listFields", Names.Entry },
            { "headFields", Names.Entry },
            { "listAttachments", Names.Entry },
            { "headAttachments", Names.Entry },
            { "createAttachment", Names.Entry },
            { "getEntryAccess", Names.Entry },
            { "headEntryAccess", Names.Entry },

            // An entry, and a string of it by name.
            { "createField", Names.Entry | Names.FieldInBody },
            { "readField", Names.Entry | Names.Field },
            { "headField", Names.Entry | Names.Field },
            { "updateField", Names.Entry | Names.Field },
            { "stripField", Names.Entry | Names.Field },

            // An entry, and an attachment of it by name.
            { "readAttachment", Names.Entry | Names.Attachment },
            { "headAttachment", Names.Entry | Names.Attachment },
            { "updateAttachment", Names.Entry | Names.Attachment },
            { "stripAttachment", Names.Entry | Names.Attachment },
        };

        /// <summary>
        /// The members a body may carry that name no group, entry or string: a group's and an
        /// entry's own values, a string's value, an attachment's bytes, and what pairing and
        /// renewal send. `name` is held apart, by the route it arrives on.
        /// </summary>
        /// <remarks>
        /// A member missing from this list fails the shape of its operation, so a body that comes
        /// to name a node, as a destination of a move would, is a case to add before it ships.
        /// </remarks>
        private static readonly ISet<string> NamingNoNode = new HashSet<string>(StringComparer.Ordinal)
        {
            "Name", "Notes", "Title", "UserName", "Password", "URL", "expires",
            "value", "protected", "content", "lifetime", "proof", "profiles",
        };

        private const string Profile = "agent";

        /// <summary>The profile of the rules a merge brings, which the caller does not hold.</summary>
        private const string Other = "other";

        /// <summary>Every verb and both switches, at the root.</summary>
        private const string Everything = "CRUSDLAP";

        private const string Stored = "a value the store wrote";

        private TemporaryDatabase _database;
        private SpecRouter _router;

        private PwEntry _service;
        private PwEntry _stranger;
        private PwGroup _fictitious;
        private PwGroup _holder;
        private PwGroup _shelf;
        private PwGroup _bin;

        private readonly List<PwGroup> _storeGroups = new List<PwGroup>();
        private readonly List<PwEntry> _storeEntries = new List<PwEntry>();

        [Flags]
        private enum Names
        {
            Unknown = 0,
            Nothing = 1,
            Tree = 2,
            Group = 4,
            Entry = 8,
            Field = 16,
            FieldInBody = 32,
            Attachment = 64,
        }

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            var root = _database.RootGroup;
            DatabaseRules.Rule(_database.Database, root, (Profile, Everything));

            // Rules naming the plugin's own group and a record of it, which the product writes for
            // no node of the store and a file can hold all the same: they reach nothing.
            var plugin = root.Groups.Single(StoreNodes.Holds);
            DatabaseRules.Crafted(_database.Database, DatabaseRules.Id(plugin.Uuid), Profile, "row:" + Everything);
            DatabaseRules.Crafted(_database.Database, DatabaseRules.Id(plugin.Entries.First().Uuid), Profile, "row:" + Everything);
            _storeGroups.Add(plugin);
            _storeEntries.AddRange(plugin.Entries);

            _fictitious = Group(root, "Fictitious");
            _holder = Group(root, "Holder");
            _shelf = Group(root, "Shelf");

            _service = Entry(_fictitious, "an invented service");
            _service.Strings.Set("Account", new ProtectedString(false, "an invented account"));
            _service.Binaries.Set("notes.txt", new ProtectedBinary(false, new byte[] { 1, 2, 3 }));
            _stranger = Entry(_fictitious, "an entry another program wrote");

            _bin = Group(root, "Recycle Bin");
            _database.Database.RecycleBinUuid = _bin.Uuid;
            Entry(_bin, "something the operator deleted");

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
        }

        [TearDown]
        public void TearDown()
        {
            _storeGroups.Clear();
            _storeEntries.Clear();
            _database.Dispose();
        }

        private static IEnumerable<TestCaseData> Declared()
        {
            return GateOperations.Document.Operations
                .Select(operation => new TestCaseData(operation.OperationId).SetArgDisplayNames(operation.OperationId));
        }

        [Test]
        public void Every_case_is_an_operation_the_document_declares()
        {
            var declared = GateOperations.Document.Operations.Select(operation => operation.OperationId).ToList();

            Assert.That(Cases.Keys.Except(declared, StringComparer.Ordinal), Is.Empty, "a case for an operation nobody declares");
        }

        [TestCaseSource(nameof(Declared))]
        public void Every_declared_operation_answers_the_store_as_an_absent_node(string operationId)
        {
            AnswersAsAbsent(operationId, AddTheStore);
        }

        [TestCaseSource(nameof(Declared))]
        public void Every_declared_operation_answers_what_a_merge_of_rules_changed_apart_brought_as_an_absent_node(string operationId)
        {
            // `STORE.md` §633 and §617: what two copies wrote apart for their rules, and what a
            // merge brings of it, is absent on every route as the rest of the store is: versions
            // of a row made apart, a renewal the write above made of a version below, and the
            // summary entries of both copies. The rules are of a profile the caller does not
            // hold, so every ordinary answer is the answer it was.
            AnswersAsAbsent(operationId, AddMergedRules);
        }

        /// <summary>
        /// An operation held to the store that <paramref name="add"/> puts into the database: each
        /// ordinary answer as it was, each request naming a node of the store answered as a node
        /// that is not there, and nothing of the store changed.
        /// </summary>
        private void AnswersAsAbsent(string operationId, Action add)
        {
            var operation = GateOperations.Document.Operations.Single(o => o.OperationId == operationId);

            Names names;
            if (!Cases.TryGetValue(operationId, out names))
            {
                Assert.Fail(operationId + " has no case: add how it names a node to " + nameof(Cases)
                    + ", the list STORE.md §633 points to, before it ships");
            }

            Assert.That(Shape(operation), Is.EqualTo(names), operationId + " names a node in a way its case does not");

            if (names == Names.Nothing)
            {
                // Nothing to name the store with: no identifier in the path, no member of a body
                // and no query name that names a node, which the shape has just held it to.
                return;
            }

            // Ordinary nodes as a read answers them before the store is there.
            var ordinary = Ordinary(operation, names).ToList();
            var before = ordinary.Select(request => Send(request)).ToList();

            add();
            var store = Snapshot();

            foreach (var pair in ordinary.Zip(before, (request, answer) => new { request, answer }))
            {
                AssertSame(Send(pair.request), pair.answer, operationId + " " + pair.request + " answers otherwise beside the store");
            }

            foreach (var request in Naming(operation, names))
            {
                var answer = Send(request.Store);
                AssertSame(answer, Send(request.Absent), operationId + " " + request.Store + " against " + request.Absent);
                AssertNothingOfTheStoreIn(answer, operationId + " " + request.Store);
            }

            Assert.That(Snapshot(), Is.EqualTo(store), operationId + " changed the store");
        }

        [Test]
        public void A_group_that_holds_only_the_store_is_refused_its_removal_as_any_group_holding_something()
        {
            // STORE.md §633: the answer the recycle bin gets, and like it, it tells a client that
            // something it cannot see is there. A group holding a record and a group holding a
            // group of the store's prefix each answer what a group holding a visible entry does.
            AddTheStore();
            var store = Snapshot();

            var holding = Send(new Request("DELETE", "/groups/" + Id(_fictitious)));
            Assume.That(holding.Status, Is.EqualTo(409), holding.Document());

            AssertSame(Send(new Request("DELETE", "/groups/" + Id(_holder))), holding, "a group holding a record");
            AssertSame(Send(new Request("DELETE", "/groups/" + Id(_shelf))), holding, "a group holding the store's group");
            Assert.That(Snapshot(), Is.EqualTo(store));
        }

        [Test]
        public void A_removal_into_a_bin_that_is_the_plugin_s_group_leaves_the_records_as_they_were()
        {
            // An arrangement STORE.md §637 names as unsupported: the operator names the plugin's
            // group as the recycle bin. A removal then moves the entry into that group, where it
            // is absent for every client as an entry in the bin is, and no record is touched.
            AddTheStore();
            _database.Database.RecycleBinUuid = _storeGroups[0].Uuid;
            var records = Records();
            var absent = Send(new Request("GET", "/entries/" + new PwUuid(true).ToHexString()));

            var removal = Send(new Request("DELETE", "/entries/" + Id(_service)));

            Assert.That(removal.Status, Is.EqualTo(200), removal.Document());
            Assert.That(_service.ParentGroup, Is.SameAs(_storeGroups[0]));
            AssertSame(Send(new Request("GET", "/entries/" + Id(_service))), absent, "the removed entry");
            Assert.That(Records(), Is.EqualTo(records));
        }

        /// <summary>
        /// How an operation names a node, read off the operation itself, or
        /// <see cref="Names.Unknown"/> for a way this test does not know how to drive.
        /// </summary>
        private static Names Shape(SpecOperation operation)
        {
            var path = operation.Path.Text;
            var names = Names.Unknown;

            if (path == "/groups" || path == "/entries")
            {
                names = Names.Tree;
            }
            else if (path.StartsWith("/groups/{uuid}", StringComparison.Ordinal))
            {
                names = Names.Group;
            }
            else if (path.StartsWith("/entries/{uuid}", StringComparison.Ordinal))
            {
                names = Names.Entry;
            }
            else if (!path.Contains("{") && operation.Verb == AclVerb.None)
            {
                names = Names.Nothing;
            }
            else
            {
                return Names.Unknown;
            }

            if (path.EndsWith("/fields/{name}", StringComparison.Ordinal))
            {
                names |= Names.Field;
            }
            else if (path.EndsWith("/attachments/{name}", StringComparison.Ordinal))
            {
                names |= Names.Attachment;
            }

            var captures = operation.Path.Parameters.Where(parameter => parameter != "uuid" && parameter != "name");
            if (captures.Any() || (path.Contains("{name}") && (names & (Names.Field | Names.Attachment)) == 0))
            {
                return Names.Unknown;
            }

            var members = operation.Body == null ? Enumerable.Empty<string>() : operation.Body.Properties.Keys;
            foreach (var member in members)
            {
                if (member != "name")
                {
                    if (!NamingNoNode.Contains(member))
                    {
                        return Names.Unknown;
                    }
                }
                else if (path.EndsWith("/fields", StringComparison.Ordinal))
                {
                    names |= Names.FieldInBody;
                }
                else if (!path.EndsWith("/attachments", StringComparison.Ordinal) && names != Names.Nothing)
                {
                    // An attachment's name, or the name a client gives itself when it pairs, and
                    // nowhere else.
                    return Names.Unknown;
                }
            }

            if (operation.Query.Any(name => name != "includeExpired"))
            {
                return Names.Unknown;
            }

            return names;
        }

        /// <summary>
        /// The requests about ordinary nodes an operation that changes nothing is held to, beside
        /// the store, as it answered them before.
        /// </summary>
        private IEnumerable<Request> Ordinary(SpecOperation operation, Names names)
        {
            if (operation.Method != "GET" && operation.Method != "HEAD")
            {
                yield break;
            }

            var path = operation.Path.Text;
            var targets = new List<string>();

            if ((names & Names.Tree) != 0)
            {
                targets.Add(path);
            }
            else if ((names & Names.Group) != 0)
            {
                foreach (var group in new[] { "root", Id(_fictitious), Id(_holder), Id(_shelf) })
                {
                    targets.Add(path.Replace("{uuid}", group));
                }
            }
            else
            {
                foreach (var entry in new[] { _service, _stranger })
                {
                    var name = (names & Names.Attachment) != 0 ? "notes.txt" : PwDefs.TitleField;
                    targets.Add(path.Replace("{uuid}", Id(entry)).Replace("{name}", name));
                }
            }

            foreach (var target in targets)
            {
                foreach (var request in Variants(operation, new Request(operation.Method, target)))
                {
                    yield return request;
                }
            }
        }

        /// <summary>
        /// Every way the operation names a node, each with the store's node and with a node that
        /// is not there.
        /// </summary>
        private IEnumerable<Pair> Naming(SpecOperation operation, Names names)
        {
            var path = operation.Path.Text;
            var body = Body(operation);

            if ((names & Names.Tree) != 0)
            {
                // A listing names no node, and is held to its answer before the store above.
                yield break;
            }

            var absent = new PwUuid(true).ToHexString();
            var nodes = (names & Names.Group) != 0
                ? _storeGroups.Select(Id)
                : _storeEntries.Select(Id);

            var name = (names & Names.Attachment) != 0 ? "notes.txt" : StoreFormat.DataField;

            foreach (var node in nodes)
            {
                var named = new Request(operation.Method, Target(path, node, name), body);
                foreach (var store in Variants(operation, named))
                {
                    yield return new Pair(store, store.With(Target(path, absent, name)));
                }
            }

            if ((names & (Names.Field | Names.FieldInBody)) == 0)
            {
                yield break;
            }

            // A string of the store on an entry that is not the store's: absent as a string the
            // entry does not hold is, whether the entry holds it or not.
            var stranger = Id(_stranger);
            foreach (var field in new[] { "KPG.Note", StoreFormat.DataField, "KPG.New" })
            {
                if ((names & Names.FieldInBody) != 0)
                {
                    var created = Bodies.Of(("name", field), ("value", Stored));
                    var store = new Request(operation.Method, Target(path, stranger, null), created);
                    yield return new Pair(store, store.With(Target(path, absent, null)));
                    continue;
                }

                var named = new Request(operation.Method, Target(path, stranger, field), body);
                foreach (var store in Variants(operation, named))
                {
                    yield return new Pair(store, store.With(Target(path, stranger, "Absent.Field")));
                }
            }
        }

        private static string Target(string path, string node, string name)
        {
            return name == null ? path.Replace("{uuid}", node) : path.Replace("{uuid}", node).Replace("{name}", name);
        }

        /// <summary>
        /// A request as it is sent, and with each condition and query it may carry.
        /// </summary>
        private static IEnumerable<Request> Variants(SpecOperation operation, Request request)
        {
            yield return request;

            if (operation.TakesIfMatch)
            {
                yield return request.Matching("\"" + new string('0', 32) + "\"");
                yield return request.Matching("*");
            }

            if (operation.Query.Count > 0)
            {
                yield return request.Asking("includeExpired", "false");
            }
        }

        /// <summary>A body the operation takes, naming nothing of the store.</summary>
        private static string Body(SpecOperation operation)
        {
            switch (operation.OperationId)
            {
                case "createGroup":
                case "updateGroup":
                    return Bodies.Of(("Name", "an invented group"));
                case "createEntry":
                case "updateEntry":
                    return Bodies.Of(("Title", "an invented title"));
                case "createField":
                    return Bodies.Of(("name", "Invented"), ("value", Stored));
                case "updateField":
                    return Bodies.Of(("value", Stored));
                case "createAttachment":
                    return Bodies.Of(("name", "more.txt"), ("content", Convert.ToBase64String(new byte[] { 4, 5 })));
                case "updateAttachment":
                    return Bodies.Of(("content", Convert.ToBase64String(new byte[] { 4, 5 })));
                default:
                    return null;
            }
        }

        /// <summary>
        /// Puts a store into the database the way a later part of the plugin, a merge or a person
        /// leaves a store, touching no time of any node already there.
        /// </summary>
        private void AddTheStore()
        {
            var root = _database.RootGroup;
            var one = Key("one");
            var two = Key("two");
            byte[] kept;
            using (var reading = DatabaseRules.Keyed(_database.Database).Read())
            {
                kept = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            // The plugin's group under the root, with records in it and a group inside it.
            var group = StoreGroup(root);

            // The store the product wrote, kept over the keys below as the operator keeps a
            // store, so its rules are the rules read beside every record that follows.
            foreach (var other in new[] { one, two })
            {
                Record(
                    group, kept, "kept", "key:" + StoreComposer.KeyName(other),
                    Data(("after", string.Empty), ("key", StoreComposer.KeyName(other))));
            }
            var authority = Record(
                group, one, "authority", "authority:" + Hex('a'), Data(("id", Hex('a')), ("storeKey", Hex(one))));
            Record(
                group,
                one,
                "client",
                "client:" + Hex('c'),
                Data(("certificate", Hex('d')), ("client", Hex('c')), ("issuer", Hex('a')), ("profiles", Profile)));
            Record(group, one, "head", "store", Data(("after", string.Empty), ("height", "1")), 1);
            Record(
                group, two, "retired", "authority:" + Hex('b'), Data(("authority", Hex('b')), ("storeKey", Hex(two))));
            var inside = Group(group, "Inside");
            _storeGroups.Add(inside);
            _storeEntries.Add(Entry(inside, "an entry inside the plugin's group"));

            // A second group with the prefix, somewhere else, S11.
            var second = StoreGroup(_shelf);
            _storeEntries.Add(Entry(second, "an entry inside a second group of the plugin's"));

            // Records outside the group, which count wherever they sit, STORE.md §621.
            Record(_holder, one, "revoked", "client:" + Hex('e'), Data(("client", Hex('e'))));
            Record(
                _bin, one, "version", "rule:" + Hex('f'), Data(("after", string.Empty), ("doc", "{}"), ("object", "rule:" + Hex('f'))));
            Record(_fictitious, two, "profile", "profile:" + Profile, Data(("after", string.Empty), ("name", Profile)));

            // The names as the plugin writes them, `STORE.md` §616: an addition in the plugin's
            // group under the key that stands, and a removal of another name in the bin.
            Record(group, one, "profile", "profile:" + Profile, Data(("after", string.Empty), ("name", Profile)));
            Record(_bin, one, "removed", "profile:retired", Data(("name", "retired")));

            // The kinds the measured code never wrote, `STORE.md` §658, each of which a tag tells.
            Record(_holder, one, "withdrawn", "certificate:" + Hex('d'), Data(("certificate", Hex('d'))));
            Record(
                _holder, one, "kept", "key:" + StoreComposer.KeyName(two),
                Data(("after", string.Empty), ("key", StoreComposer.KeyName(two))));
            Record(group, one, "format", "format:1", Data(("format", "1"), ("plugin", "0.3.0")), 1);

            // Damaged: an identifier of the store, and a check nobody computed, for a kind the
            // measured code wrote and for a kind it did not.
            var damaged = Record(_fictitious, one, "removed", "profile:other", Data(("name", "other")));
            damaged.Strings.Set(StoreFormat.CheckField, new ProtectedString(false, new string('0', 64)));
            var unmeasured = Record(
                _fictitious, two, "kept", "key:" + StoreComposer.KeyName(one),
                Data(("after", string.Empty), ("key", StoreComposer.KeyName(one))));
            unmeasured.Strings.Set(StoreFormat.CheckField, new ProtectedString(false, new string('0', 64)));

            // Stripped of every string of the store, STORE.md §621.
            var stripped = Record(_fictitious, one, "client", "client:" + Hex('9'), Data(("client", Hex('9'))));
            foreach (var name in stripped.Strings.Select(pair => pair.Key).Where(StoreFormat.IsField).ToList())
            {
                stripped.Strings.Remove(name);
            }

            // A copy of the authority's entry, under an identifier of its own, and a record of a
            // kind the plugin does not know, which its tag cannot tell.
            var copy = Entry(_fictitious, "KPG authority copy");
            foreach (var pair in authority.Strings.Where(pair => StoreFormat.IsField(pair.Key)))
            {
                copy.Strings.Set(pair.Key, pair.Value);
            }

            _storeEntries.Add(copy);
            var unknown = Entry(_fictitious, "KPG future");
            Strings(unknown, one, "future", Data(("about", "something later")));
            _storeEntries.Add(unknown);

            // Strings of the store on an entry that is not the store's.
            _stranger.Strings.Set("KPG.Note", new ProtectedString(false, Stored));
            _stranger.Strings.Set(StoreFormat.DataField, new ProtectedString(true, Stored));

            Assume.That(_storeEntries.Count, Is.GreaterThan(10));
        }

        /// <summary>
        /// Rules changed apart in a copy of the database and in the database itself, for a
        /// profile the caller does not hold, each written as an OK on a group's dialog writes it,
        /// and the copy then merged in by Overwrite if newer: every entry of the plugin's group is
        /// then a node of the store.
        /// </summary>
        private void AddMergedRules()
        {
            var root = _database.RootGroup;
            Assert.That(NodeOks.Pressed(_database.Database, root, sheet => sheet.Set(Other, ".R......") == null), Is.True);
            Assert.That(NodeOks.Pressed(_database.Database, _fictitious, sheet => sheet.Set(Other, "........") == null), Is.True);
            Assert.That(NodeOks.Pressed(_database.Database, _fictitious, sheet => sheet.Remove(Other)), Is.True, "the rule taken off");
            _database.Save();
            using (var copy = TemporaryDatabase.New())
            {
                System.IO.File.Copy(_database.Path, copy.Path, true);
                copy.Reopen();

                // Apart: the root widened here, which renews the version below that follows it,
                // and a rule set at the group in the copy.
                Assert.That(NodeOks.Pressed(_database.Database, root, sheet => sheet.Set(Other, ".RU.....") == null), Is.True);
                Assert.That(
                    NodeOks.Pressed(copy.Database, copy.RootGroup.FindGroup(_fictitious.Uuid, true), sheet => sheet.Set(Other, ".R...L..") == null),
                    Is.True);
                _database.Database.MergeIn(copy.Database, PwMergeMethod.OverwriteIfNewer, null);
            }

            using (var reading = DatabaseRules.Keyed(_database.Database).Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(
                    reading.InForce.VersionsOf(StoreComposer.RowObject(DatabaseRules.Id(_fictitious.Uuid), Other)),
                    Has.Count.EqualTo(2),
                    "the versions made apart both stand");
            }

            _storeEntries.Clear();
            _storeEntries.AddRange(_storeGroups.SelectMany(group => group.Entries));
        }

        private PwGroup StoreGroup(PwGroup parent)
        {
            var identifier = Encoding.ASCII.GetBytes("KPG/STOR").Concat(new PwUuid(true).UuidBytes.Take(8)).ToArray();
            var group = new PwGroup(true, true, "KeePassGate", PwIcon.TerminalEncrypted)
            {
                Uuid = new PwUuid(identifier),
                Notes = "KeePassGate data.",
                EnableSearching = false,
                EnableAutoType = false,
            };

            parent.AddGroup(group, true);
            _storeGroups.Add(group);
            return group;
        }

        /// <summary>
        /// A record as `STORE.md` §607 to §609 have it, in the form the store was measured with.
        /// </summary>
        private PwEntry Record(PwGroup parent, byte[] key, string kind, string subject, string data, int number = 0)
        {
            var entry = StoreEntries.Of(StoreComposer.Record(key, kind, subject, data, number));
            parent.AddEntry(entry, true);
            _storeEntries.Add(entry);
            return entry;
        }

        private static void Strings(PwEntry entry, byte[] key, string kind, string data)
        {
            StoreEntries.Put(entry, StoreComposer.Record(key, kind, kind, data));
        }

        private static string Data(params (string Name, string Value)[] members)
        {
            return StoreComposer.Content(members);
        }

        private static byte[] Key(string label)
        {
            return StoreComposer.Key(label);
        }

        private static string Hex(byte[] bytes)
        {
            return KeePassGate.Hex.Of(bytes);
        }

        private static string Hex(char digit)
        {
            return new string(digit, 64);
        }

        private static PwGroup Group(PwGroup parent, string name)
        {
            var group = new PwGroup(true, true, name, PwIcon.Folder);
            parent.AddGroup(group, true);
            return group;
        }

        private static PwEntry Entry(PwGroup parent, string title)
        {
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, "an invented user"));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "an invented password"));
            entry.Binaries.Set("notes.txt", new ProtectedBinary(false, new byte[] { 7, 8 }));
            parent.AddEntry(entry, true);
            return entry;
        }

        private static string Id(PwGroup group)
        {
            return group.Uuid.ToHexString();
        }

        private static string Id(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }

        /// <summary>
        /// Everything of the store a route could change, and the strings of the store on the
        /// entry that is not the store's, as one text.
        /// </summary>
        private string Snapshot()
        {
            var text = new StringBuilder();
            foreach (var group in _storeGroups)
            {
                text.Append("group ").Append(Id(group)).Append(' ').Append(group.ParentGroup == null ? "-" : Id(group.ParentGroup))
                    .Append(' ').Append(group.Name).Append(' ').Append(group.Notes)
                    .Append(' ').Append(group.Groups.UCount).Append(' ').Append(group.Entries.UCount).Append('\n');
            }

            return text.Append(Records()).Append("deleted ").Append(_database.Database.DeletedObjects.UCount).ToString();
        }

        /// <summary>
        /// The store's entries and the strings of the store on the entry that is not the store's,
        /// as one text: where each sits and everything it holds.
        /// </summary>
        private string Records()
        {
            var text = new StringBuilder();
            foreach (var entry in _storeEntries.Concat(new[] { _stranger }))
            {
                text.Append("entry ").Append(Id(entry)).Append(' ').Append(entry.ParentGroup == null ? "-" : Id(entry.ParentGroup))
                    .Append(' ').Append(entry.History.UCount).Append(' ').Append(entry.LastModificationTime.Ticks).Append('\n');
                foreach (var pair in entry.Strings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    text.Append("  ").Append(pair.Key).Append('=').Append(pair.Value.ReadString()).Append('\n');
                }

                foreach (var pair in entry.Binaries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    var bytes = Convert.ToBase64String(pair.Value.ReadData());
                    text.Append("  ").Append(pair.Key).Append(':').Append(bytes).Append('\n');
                }
            }

            return text.ToString();
        }

        private void AssertNothingOfTheStoreIn(GateContext answer, string what)
        {
            var document = answer.Document() + " " + (answer.Headers == null ? string.Empty : answer.Headers.ToString());
            foreach (var node in _storeGroups.Select(Id).Concat(_storeEntries.Select(Id)))
            {
                Assert.That(document, Does.Not.Contain(node).IgnoreCase, what);
            }

            Assert.That(document, Does.Not.Contain(Stored).And.Not.Contain("KPG."), what);
        }

        private static void AssertSame(GateContext answer, GateContext expected, string what)
        {
            Assert.That(
                Answer(answer),
                Is.EqualTo(Answer(expected)),
                what);
        }

        private static string Answer(GateContext context)
        {
            var headers = context.Headers == null ? string.Empty : context.Headers.ToString();
            return context.Status + "\n" + headers + "\n" + context.Document();
        }

        private GateContext Send(Request request)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, null),
                TestTags.Fixed,
                CurrentThread.Instance,
                null);

            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (request.IfMatch != null)
                {
                    headers["If-Match"] = request.IfMatch;
                }

                var context = new GateContext(
                    new HttpRequestHead(
                        request.Method,
                        request.Path,
                        request.Query,
                        headers,
                        Bodies.LengthOf(request.Body)),
                    request.Body,
                    ClientIdentity.Paired("d1e2f3", certificate, new[] { Profile }));
                new ServedDatabase(vault).PutOn(context);

                _router.Handle(context);
                context.Document();
                return context;
            }
        }

        private sealed class Request
        {
            public Request(
                string method,
                string path,
                string body = null,
                string ifMatch = null,
                IReadOnlyDictionary<string, string> query = null)
            {
                Method = method;
                Path = path;
                Body = body;
                IfMatch = ifMatch;
                Query = query ?? new Dictionary<string, string>();
            }

            public string Method { get; private set; }

            public string Path { get; private set; }

            public string Body { get; private set; }

            public string IfMatch { get; private set; }

            public IReadOnlyDictionary<string, string> Query { get; private set; }

            public Request With(string path)
            {
                return new Request(Method, path, Body, IfMatch, Query);
            }

            public Request Matching(string tag)
            {
                return new Request(Method, Path, Body, tag, Query);
            }

            public Request Asking(string name, string value)
            {
                return new Request(Method, Path, Body, IfMatch, new Dictionary<string, string> { { name, value } });
            }

            public override string ToString()
            {
                return Method + " " + Path + (IfMatch == null ? string.Empty : " If-Match " + IfMatch)
                       + string.Concat(Query.Select(pair => " ?" + pair.Key + "=" + pair.Value));
            }
        }

        private sealed class Pair
        {
            public Pair(Request store, Request absent)
            {
                Store = store;
                Absent = absent;
            }

            public Request Store { get; private set; }

            public Request Absent { get; private set; }
        }
    }
}
