using System;
using System.Collections.Generic;
using System.Reflection;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
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
    /// The rules a request is decided by are those of the reading its own hold took, worked out
    /// once for that hold and kept for no other, `STORE.md` §620 and §617.
    /// </summary>
    /// <remarks>
    /// The target and the vault read the store through a single `StoreReadings`, so a hold
    /// answers the client's admission, the names, the guard and the rules from a single reading.
    /// These hold that the rules add no hold and no reading to a request: a node's rules within a
    /// hold are the very rules every other node of that hold reads, a rule written under the hold
    /// is not read until the next, and a rule written between two holds is read by the second. A
    /// further reading would hand a node rules worked out again, and a rule kept from an earlier
    /// hold would hand it rules written over since; each case tells both apart.
    /// </remarks>
    [TestFixture]
    public sealed class RulesInTheHoldTests
    {
        private static readonly string[] Reader = { "reader" };

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.AddGroup(_group, true);
            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _group.AddEntry(_entry, true);
            DatabaseRules.Rule(_database.Database, _group, ("reader", "RL"));
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_hold_reads_the_rules_of_its_own_reading_and_works_them_out_once()
        {
            var database = _database.Database;
            var hold = new Stamped();
            using (var readings = new StoreReadings(database, hold, null))
            {
                Func<StoredRules> rules = () => readings.Rules(database, database.RootGroup);

                var inside = hold.Hold(() =>
                {
                    var first = KdbxNodes.Of(_entry, hold, rules).Parent.Rule;
                    var again = KdbxNodes.Of(_group, hold, rules).Rule;

                    // The operator's rule landing under the hold, which nothing on KeePass's
                    // thread can do while a connection holds it, and a reading of this hold's own
                    // has to leave unread.
                    DatabaseRules.Rule(database, _group, ("reader", "RUL"));
                    var written = KdbxNodes.Of(_group, hold, rules).Rule;
                    var permits = AclResolver.Permits(KdbxNodes.Of(_entry, hold, rules), Reader, AclVerb.Update);

                    Assert.That(again, Is.SameAs(first), "the rules of a hold are worked out once, for every node of it");
                    Assert.That(written, Is.SameAs(first), "and are the rules of the reading the hold took");
                    Assert.That(permits, Is.False);
                    return first;
                });

                var next = hold.Hold(() => KdbxNodes.Of(_group, hold, rules).Rule);
                Assert.That(next, Is.Not.SameAs(inside), "nothing of a rule is kept from one hold to the next");
                Assert.That(hold.Hold(() => AclResolver.Permits(KdbxNodes.Of(_entry, hold, rules), Reader, AclVerb.Update)), Is.True,
                    "the next hold reads the rule written since");
            }
        }

        [Test]
        public void A_rule_narrowed_between_two_holds_grants_what_it_was_narrowed_to_in_the_second()
        {
            var database = _database.Database;
            var hold = new Stamped();
            using (var readings = new StoreReadings(database, hold, null))
            {
                Func<StoredRules> rules = () => readings.Rules(database, database.RootGroup);
                Assume.That(AclResolver.Permits(KdbxNodes.Of(_entry, hold, rules), Reader, AclVerb.Read), Is.True);

                DatabaseRules.Rule(database, _group, ("reader", "L"));

                Assert.That(AclResolver.Permits(KdbxNodes.Of(_entry, hold, rules), Reader, AclVerb.Read), Is.False);
                Assert.That(AclResolver.Permits(KdbxNodes.Of(_entry, hold, rules), Reader, AclVerb.List), Is.True);
            }
        }

        [TestCase("split")]
        [TestCase("outdated")]
        public void A_store_whose_records_are_not_read_as_in_force_grants_nothing(string how)
        {
            // A second store key standing beside the first, or a record of a kind this plugin does
            // not know: nothing of the store is read as in force, so no rule of it grants anything.
            var database = _database.Database;
            Assume.That(Permits(Reader, AclVerb.Read), Is.True);

            byte[] key;
            using (var reading = DatabaseAuthority.Store(database).Read())
            {
                key = (byte[])reading.InForce.Key.Bytes.Clone();
            }

            var record = how == "split"
                ? StoreComposer.Record(
                    StoreComposer.Key("another"), "authority", "authority:" + new string('b', 64),
                    StoreComposer.Content(("id", new string('b', 64)), ("storeKey", StoreComposer.HexOf(StoreComposer.Key("another")))))
                : StoreComposer.Record(key, "future", "future", StoreComposer.Content(("about", "something later")));
            database.RootGroup.AddEntry(StoreEntries.Of(record), true);

            Assert.That(Permits(Reader, AclVerb.Read), Is.False);
            Assert.That(Permits(Reader, AclVerb.List), Is.False);
        }

        [Test]
        public void The_hold_a_connection_is_judged_in_works_out_no_rules()
        {
            // The store's word for a connection reads the store and asks no rule, so the rules
            // cost that hold nothing; a hold that walks works them out, and a target let go at a
            // lock keeps none.
            var database = _database.Database;
            var hold = new Stamped();
            using (var readings = new StoreReadings(database, hold, null))
            {
                var authority = StoreSeeds.Served(DatabaseAuthority.Store(database));
                Assume.That(hold.Hold(() => readings.WordFor(authority)), Is.Null, "the store serves the authority");
                Assert.That(KeptRules(readings), Is.Null);

                hold.Hold(() => KdbxNodes.Of(_entry, hold, () => readings.Rules(database, database.RootGroup)).Parent.Rule);
                Assert.That(KeptRules(readings), Is.Not.Null, "kept with the reading of the hold that walked");

                readings.Forget();
                Assert.That(KeptRules(readings), Is.Null, "and let go with it");
            }
        }

        [Test]
        public void A_change_s_turn_reads_its_rules_of_the_turn_s_reading_and_nothing_outside_it()
        {
            // On KeePass's own thread nothing is kept but inside a change's turn, from the lookup
            // of its node to the change it hands the writer: every question of the turn is of a
            // single reading, the rules among them, and each question outside a turn reads anew.
            var database = _database.Database;
            var thread = new KeePassThread();
            using (var readings = new StoreReadings(database, thread, null))
            {
                Func<StoredRules> rules = () => readings.Rules(database, database.RootGroup);

                readings.InTurn(() =>
                {
                    var first = KdbxNodes.Of(_group, thread, rules).Rule;
                    var again = KdbxNodes.Of(_entry, thread, rules).Parent.Rule;
                    Assert.That(again, Is.SameAs(first));
                    Assert.That(thread.Readings, Is.EqualTo(1), "the turn read the store once");
                    return 0;
                });

                thread.Readings = 0;
                var outside = KdbxNodes.Of(_group, thread, rules).Rule;
                var later = KdbxNodes.Of(_group, thread, rules).Rule;
                Assert.That(later, Is.Not.SameAs(outside));
                Assert.That(thread.Readings, Is.EqualTo(2), "each question outside a turn reads anew");
            }
        }

        [TestCase("entry")]
        [TestCase("listing")]
        [TestCase("group listing")]
        [TestCase("access")]
        public void A_read_decided_by_the_store_s_rules_takes_a_single_hold(string what)
        {
            // A sweep of every entry is a listing of the root group.
            DatabaseRules.Rule(_database.Database, _database.RootGroup, ("reader", "L"));
            var thread = new HoldsCounted();
            var answer = Send(thread, "GET", Target(what), null);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(thread.Holds, Is.EqualTo(1), "the client, the node, the rules and the answer of a single hold");
            Assert.That(thread.Turned, Is.False);
        }

        [Test]
        public void A_change_decided_by_the_store_s_rules_takes_a_single_hold_and_reads_the_store_once_in_its_turn()
        {
            DatabaseRules.Rule(_database.Database, _group, ("reader", "RUL"));
            var thread = new HoldsCounted();

            var answer = Send(thread, "PUT", "/entries/" + _entry.Uuid.ToHexString(), Bodies.Of((PwDefs.UserNameField, "written by a client")));

            Assert.That(answer.Status, Is.EqualTo(200), answer.Document());
            Assert.That(thread.Holds, Is.EqualTo(1), "the first asking in a single hold");
            Assert.That(thread.Turned, Is.True);
            Assert.That(thread.TurnReadings, Is.EqualTo(1), "and the turn's lookup, client and rules of a single reading");
        }

        [Test]
        public void A_node_of_a_database_closed_under_the_walk_grants_nothing_and_is_unsettled()
        {
            // The vault's nodes read the rules of the tree the vault looks through. A tree that
            // went under the walk is a read KeePass's thread was not held still for, never rules
            // from before, and is what every read that met KeePass's thread moving raises.
            var vault = new KdbxVault(_database.Database, null, TestTags.Fixed, null);
            var node = vault.Entry(_entry.Uuid.ToHexString()).Node;
            Assume.That(AclResolver.Permits(node, Reader, AclVerb.Read), Is.True);

            _database.Database.Close();

            Assert.Throws<VaultUnsettled>(() => AclResolver.Permits(node, Reader, AclVerb.Read));
        }

        [Test]
        public void A_database_locked_under_the_walk_of_a_hold_that_lapsed_is_answered_busy_by_the_router()
        {
            // KeePass's thread let go of a read that outlasted its hold and locked the database
            // before the read's walk asked its rules. The read is answered as every read whose
            // hold did not last is answered, `503 busy`, and nothing escapes the router, which a
            // conversation would count as a defect and answer `500`.
            var database = _database.Database;
            var hold = new LapsedAtTheWalk(() => database.Close());
            using (var readings = new StoreReadings(database, hold, null))
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var vault = new KdbxVault(database, null, TestTags.Fixed, hold, null, readings);
                var context = new GateContext(
                    new HttpRequestHead(
                        "GET",
                        "/entries/" + _entry.Uuid.ToHexString(),
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        0),
                    string.Empty,
                    ClientIdentity.Paired("d1e2f3", certificate, Reader));
                new ServedDatabase(vault).PutOn(context);

                Assert.DoesNotThrow(() => GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance)).Handle(context));

                Assert.That(hold.Locked, Is.True, "the database was locked under the walk");
                Assert.That(context.Status, Is.EqualTo(503));
                Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody("busy")));
            }
        }

        [Test]
        public void A_hold_of_a_target_the_cache_let_go_still_works_its_rules_out_once()
        {
            // A connection can hold a target the cache let go of at a lock or a change of what is
            // served, a pairing for the whole of its budget, and from then on nothing of the store
            // is kept from a hold to the next. Inside a hold the rules are still worked out once,
            // from a single reading, however many levels, profiles and verbs the walks ask about.
            var database = _database.Database;
            var hold = new Stamped();
            using (var readings = new StoreReadings(database, hold, null))
            {
                Func<StoredRules> rules = () => readings.Rules(database, database.RootGroup);
                readings.Forget();
                var counted = new ReadingsCounted(readings);
                hold.Asked = counted.Look;

                var first = hold.Hold(() =>
                {
                    var rule = KdbxNodes.Of(_entry, hold, rules).Parent.Rule;
                    foreach (var verb in new[] { AclVerb.Read, AclVerb.List, AclVerb.Update, AclVerb.Delete })
                    {
                        AclResolver.Permits(KdbxNodes.Of(_entry, hold, rules), new[] { "reader", "writer" }, verb);
                    }

                    Assert.That(KdbxNodes.Of(_group, hold, rules).Rule, Is.SameAs(rule), "the rules of the hold, worked out once");
                    return rule;
                });
                counted.Look();

                Assert.That(counted.Readings, Is.EqualTo(1), "a single reading for every rule the hold asked");
                Assert.That(hold.Hold(() => KdbxNodes.Of(_group, hold, rules).Rule), Is.Not.SameAs(first), "and none kept for the next hold");
            }
        }

        private string Target(string what)
        {
            switch (what)
            {
                case "entry":
                    return "/entries/" + _entry.Uuid.ToHexString();
                case "listing":
                    return "/entries";
                case "group listing":
                    return "/groups/" + _group.Uuid.ToHexString() + "/entries";
                default:
                    return "/entries/" + _entry.Uuid.ToHexString() + "/access";
            }
        }

        private GateContext Send(HoldsCounted thread, string method, string path, string body)
        {
            var database = _database.Database;
            using (var readings = new StoreReadings(database, thread, null))
            using (var certificate = Minted.Named("CN=agent-fictitious").SelfSigned().Read())
            {
                var vault = new KdbxVault(
                    database,
                    new DatabaseWriter(thread, saved => saved.Save(null), null, null, null),
                    TestTags.Fixed,
                    thread,
                    null,
                    readings);
                var context = new GateContext(
                    new HttpRequestHead(
                        method,
                        path,
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        Bodies.LengthOf(body)),
                    body,
                    ClientIdentity.Paired("d1e2f3", certificate, Reader));
                new ServedDatabase(vault).PutOn(context);

                GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance)).Handle(context);
                context.Document();
                return context;
            }
        }

        /// <summary>Whether a client holding these profiles may do this at the entry, decided in a hold of its own.</summary>
        private bool Permits(string[] profiles, AclVerb verb)
        {
            var database = _database.Database;
            var hold = new Stamped();
            using (var readings = new StoreReadings(database, hold, null))
            {
                return AclResolver.Permits(
                    KdbxNodes.Of(_entry, hold, () => readings.Rules(database, database.RootGroup)), profiles, verb);
            }
        }

        /// <summary>The rules the readings keep, read where they are kept.</summary>
        private static object KeptRules(StoreReadings readings)
        {
            return typeof(StoreReadings).GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(readings);
        }

        /// <summary>A connection's thread: a stamp of its own for each outermost hold.</summary>
        private sealed class Stamped : IReadHold
        {
            private long _given;
            private long _stamp;

            /// <summary>What is told each time the stamp is asked for, or null.</summary>
            public Action Asked { get; set; }

            public long Stamp
            {
                get
                {
                    Asked?.Invoke();
                    return _stamp;
                }
            }

            public T Hold<T>(Func<T> read)
            {
                if (_stamp != 0)
                {
                    return read();
                }

                _stamp = ++_given;
                try
                {
                    return read();
                }
                finally
                {
                    _stamp = 0;
                }
            }
        }

        /// <summary>
        /// A connection's thread whose hold lapses at the second hold asked for inside it, the
        /// walk up the rules of a read once its node is found: KeePass's thread, let go, does
        /// what it is given there, and the hold is refused once the read ends.
        /// </summary>
        private sealed class LapsedAtTheWalk : IReadHold
        {
            private readonly Action _meanwhile;
            private long _given;
            private long _stamp;
            private int _inside;

            public LapsedAtTheWalk(Action meanwhile)
            {
                _meanwhile = meanwhile;
            }

            /// <summary>Whether KeePass's thread did what it was given under the read.</summary>
            public bool Locked { get; private set; }

            public long Stamp
            {
                get { return _stamp; }
            }

            public T Hold<T>(Func<T> read)
            {
                if (_stamp != 0)
                {
                    if (++_inside == 2)
                    {
                        Locked = true;
                        _meanwhile();
                    }

                    return read();
                }

                _stamp = ++_given;
                T result;
                try
                {
                    result = read();
                }
                finally
                {
                    _stamp = 0;
                }

                if (Locked)
                {
                    throw new VaultUnsettled("the read took longer than KeePass's thread could be held for");
                }

                return result;
            }
        }

        /// <summary>
        /// The readings a `StoreReadings` makes, each told by the checked forms it puts in the
        /// place of those it kept before, looked at each time it is asked to.
        /// </summary>
        private sealed class ReadingsCounted
        {
            private static readonly FieldInfo Kept = typeof(StoreReadings).GetField("_kept", BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly StoreReadings _readings;
            private object _last;

            public ReadingsCounted(StoreReadings readings)
            {
                _readings = readings;
                _last = Kept.GetValue(readings);
            }

            /// <summary>How many readings were made between the first look and the last.</summary>
            public int Readings { get; private set; }

            /// <summary>Notes a reading made since the look before.</summary>
            public void Look()
            {
                var now = Kept.GetValue(_readings);
                if (!ReferenceEquals(now, _last))
                {
                    Readings++;
                    _last = now;
                }
            }
        }

        /// <summary>KeePass's own thread: no stamp, every question outside a turn its own reading, each counted.</summary>
        private sealed class KeePassThread : IReadHold
        {
            public int Readings { get; set; }

            public long Stamp
            {
                get
                {
                    Readings++;
                    return 0;
                }
            }

            public T Hold<T>(Func<T> read)
            {
                return read();
            }
        }

        /// <summary>
        /// KeePass's thread, held still for a connection's reads and running a change's turn,
        /// counting the holds and the readings of the turn.
        /// </summary>
        /// <remarks>
        /// A hold inside a hold is the same hold. In the turn there is no stamp, and `StoreReadings`
        /// asks for it once for each reading it makes there, which is how the turn's readings
        /// are counted.
        /// </remarks>
        private sealed class HoldsCounted : IUiThread, IReadHold
        {
            private long _given;
            private long _stamp;
            private bool _inTurn;

            public bool Turned { get; private set; }

            public int Holds { get; private set; }

            public int TurnReadings { get; private set; }

            public long Stamp
            {
                get
                {
                    if (_inTurn)
                    {
                        TurnReadings++;
                        return 0;
                    }

                    return _stamp;
                }
            }

            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                Turned = true;
                _inTurn = true;
                try
                {
                    work();
                }
                finally
                {
                    _inTurn = false;
                }

                return true;
            }

            public T Hold<T>(Func<T> read)
            {
                if (_inTurn || _stamp != 0)
                {
                    return read();
                }

                Holds++;
                _stamp = ++_given;
                try
                {
                    return read();
                }
                finally
                {
                    _stamp = 0;
                }
            }
        }
    }
}
