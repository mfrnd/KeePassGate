using System;
using System.Collections.Generic;
using System.Linq;
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
    /// A write is allowed by what the database holds when the write lands, and by nothing older.
    /// </summary>
    /// <remarks>
    /// The router decides on the connection's thread, under a single short hold of KeePass's
    /// thread, and the write then waits for a turn of its own: behind another write's save, which
    /// is as long as the database is large, or behind a dialog the operator is about to accept.
    /// Whatever the operator does in between is done to a database the decision never saw. Every
    /// case here is the real router over the real vault, and every case but the count of holds
    /// has an operator's edit landing on KeePass's thread just before the queued write.
    /// </remarks>
    [TestFixture]
    public sealed class DecidedAtTheWriteTests
    {
        private const string Name = PwDefs.UserNameField;
        private const string Typed = "a secret typed in KeePass";

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;
        private Lines _lines;
        private SpecRouter _router;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _group = new PwGroup(true, true, "Fictitious", PwIcon.Folder);
            _database.RootGroup.AddGroup(_group, true);

            _entry = new PwEntry(true, true);
            _entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "an invented service"));
            _entry.Strings.Set(Name, new ProtectedString(false, "kept in the open"));
            _group.AddEntry(_entry, true);

            _lines = new Lines();
            _router = GateOperations.Router("9.9.9", null, new AccessCheck(_lines));
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [TestCase("entry")]
        [TestCase("field")]
        public void A_value_made_protected_before_the_queued_write_is_not_overwritten(string route)
        {
            // A rule that may update and may not reach protected values. The handler finds the
            // string unprotected, and the operator's OK on the entry dialog marks it and types a
            // secret into it before the write's turn comes.
            DatabaseRules.Rule(_database.Database, _group, ("agent", "RUL"));
            var operatorFirst = new OperatorFirst(() =>
            {
                _entry.Strings.Set(Name, new ProtectedString(true, Typed));
                _entry.Touch(true, false);
            });

            var answer = route == "entry"
                ? Send(operatorFirst, "PUT", "/entries/" + Uuid(_entry), Bodies.Of((Name, "written by a client")))
                : Send(
                    operatorFirst,
                    "PUT",
                    "/entries/" + Uuid(_entry) + "/fields/" + Name,
                    Bodies.Of(("value", "written by a client")));

            Assert.That(operatorFirst.Ran, Is.True, answer.Status + " " + answer.Json);
            Assert.That(_entry.Strings.ReadSafe(Name), Is.EqualTo(Typed), answer.Status + " " + answer.Json);
            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
        }

        [Test]
        public void A_rule_taken_away_before_the_queued_write_refuses_the_write()
        {
            DatabaseRules.Rule(_database.Database, _group, ("agent", "RUL"));
            var operatorFirst = new OperatorFirst(
                () => DatabaseRules.Rule(_database.Database, _group, ("agent", "RL")));

            var answer = Send(
                operatorFirst,
                "PUT",
                "/entries/" + Uuid(_entry) + "/fields/" + Name,
                Bodies.Of(("value", "written by a client")));

            Assert.That(operatorFirst.Ran, Is.True, answer.Status + " " + answer.Json);
            Assert.That(_entry.Strings.ReadSafe(Name), Is.EqualTo("kept in the open"), answer.Status + " " + answer.Json);
            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
        }

        [Test]
        public void An_entry_moved_before_the_queued_removal_is_asked_about_where_it_is()
        {
            // Removal is allowed in the group the entry was found in and nowhere else. The operator
            // drags the entry into a group whose rule lets the same client read it and no more.
            DatabaseRules.Rule(_database.Database, _group, ("agent", "RDL"));
            var kept = new PwGroup(true, true, "Kept", PwIcon.Folder);
            _database.RootGroup.AddGroup(kept, true);
            DatabaseRules.Seal(_database.Database, kept, ("agent", "RL"));
            var operatorFirst = new OperatorFirst(() =>
            {
                _group.Entries.Remove(_entry);
                kept.AddEntry(_entry, true, true);
            });

            var answer = Send(operatorFirst, "DELETE", "/entries/" + Uuid(_entry), string.Empty);

            Assert.That(operatorFirst.Ran, Is.True, answer.Status + " " + answer.Json);
            Assert.That(_entry.ParentGroup, Is.SameAs(kept), answer.Status + " " + answer.Json);
            Assert.That(answer.Status, Is.EqualTo(404), answer.Json);
        }

        [TestCase("PUT", "RUL", null, 200, "group")]
        [TestCase("PUT", "RL", null, 404, "group")]
        [TestCase("DELETE", "RDL", null, 200, "group")]
        [TestCase("DELETE", "RDL", "RL", 404, "entry")]
        public void A_change_asked_first_in_a_single_hold_is_answered_and_written_down_as_its_rules_decide(
            string method, string granted, string narrowed, int status, string decidedAt)
        {
            // The router finds the node and asks the rules before the change waits for its turn,
            // a removal of both its sides, and all of that is of a single hold of KeePass's
            // thread. The answer and the line the request leaves are the rules' answer at the
            // node, whichever hold the lookup and the walks are part of.
            DatabaseRules.Rule(_database.Database, _group, ("agent", granted));
            if (narrowed != null)
            {
                DatabaseRules.Rule(_database.Database, _entry, ("agent", narrowed));
            }

            var thread = new HoldsCounted();
            var answer = Send(
                thread,
                thread,
                method,
                "/entries/" + Uuid(_entry),
                method == "PUT" ? Bodies.Of((Name, "written by a client")) : string.Empty);

            Assert.That(answer.Status, Is.EqualTo(status), answer.Json);
            Assert.That(thread.Turned, Is.EqualTo(status == 200), "a change the first asking refuses has no turn");

            var line = _lines.Recorded.Single();
            Assert.That(
                line.OperationId,
                Is.EqualTo(method == "PUT" ? GateOperations.UpdateEntry : GateOperations.DeleteEntry));
            Assert.That(line.Node, Is.EqualTo(Uuid(_entry)));
            Assert.That(line.Permitted, Is.EqualTo(status == 200));
            Assert.That(
                line.Reason,
                Is.EqualTo(status == 200 ? "a rule here grants it" : "a rule here does not grant it"));
            Assert.That(
                line.DecidedAt,
                Is.EqualTo(decidedAt == "group" ? _group.Uuid.ToHexString() : Uuid(_entry)));

            Assert.That(thread.BeforeTheTurn, Is.EqualTo(1), "the lookup and every walk are of a single hold");
        }

        [TestCase("an entry, making the recycle bin")]
        [TestCase("an entry, into the recycle bin")]
        [TestCase("a group, making the recycle bin")]
        public void A_removal_into_the_recycle_bin_is_asked_first_in_a_single_hold(string removed)
        {
            // The real vault's removals: an entry's or a group's to the recycle bin, which the
            // first removal makes. Each asks both of its sides before its turn, and all of that
            // is of a single hold of KeePass's thread, as every change's first asking is.
            var database = _database.Database;
            DatabaseRules.Rule(database, _database.RootGroup, ("agent", "RDL"));
            database.RecycleBinEnabled = true;

            PwGroup bin = null;
            if (removed == "an entry, into the recycle bin")
            {
                bin = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin);
                _database.RootGroup.AddGroup(bin, true);
                database.RecycleBinUuid = bin.Uuid;
            }

            var empty = new PwGroup(true, true, "Empty", PwIcon.Folder);
            _database.RootGroup.AddGroup(empty, true);
            var isGroup = removed.StartsWith("a group", StringComparison.Ordinal);
            Assert.That(
                database.RecycleBinUuid.Equals(PwUuid.Zero), Is.EqualTo(bin == null), "the bin is there or not as named");

            var thread = new HoldsCounted();
            var target = isGroup ? "/groups/" + empty.Uuid.ToHexString() : "/entries/" + Uuid(_entry);
            var answer = Send(thread, thread, "DELETE", target, string.Empty);

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(thread.Turned, Is.True);

            var parent = isGroup ? empty.ParentGroup : _entry.ParentGroup;
            Assert.That(parent, Is.Not.Null);
            Assert.That(parent.Uuid, Is.EqualTo(database.RecycleBinUuid), "the removal went to the bin");
            if (bin != null)
            {
                Assert.That(parent, Is.SameAs(bin), "into the bin that was there");
            }

            var line = _lines.Recorded.Single();
            Assert.That(line.OperationId, Is.EqualTo(isGroup ? GateOperations.DeleteGroup : GateOperations.DeleteEntry));
            Assert.That(line.Permitted, Is.True);
            Assert.That(line.DecidedAt, Is.EqualTo(_database.RootGroup.Uuid.ToHexString()));

            Assert.That(thread.BeforeTheTurn, Is.EqualTo(1), "the lookup and both sides are of a single hold");
        }

        private GateContext Send(IUiThread thread, string method, string path, string body)
        {
            return Send(thread, CurrentThread.Instance, method, path, body);
        }

        private GateContext Send(IUiThread thread, IReadHold hold, string method, string path, string body)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(thread, database => database.Save(null), null, null, null),
                TestTags.Fixed,
                hold,
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
                    ClientIdentity.Paired("d1e2f3", certificate, new[] { "agent" }));
                new ServedDatabase(vault).PutOn(context);

                _router.Handle(context);
                return context;
            }
        }

        private static string Uuid(PwEntry entry)
        {
            return entry.Uuid.ToHexString();
        }

        /// <summary>An operator's edit that KeePass's thread runs just before the queued write.</summary>
        private sealed class OperatorFirst : IUiThread
        {
            private readonly Action _edit;

            public OperatorFirst(Action edit)
            {
                _edit = edit;
            }

            public bool Ran { get; private set; }

            public string Stalled
            {
                get { return null; }
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                if (!Ran)
                {
                    Ran = true;
                    _edit();
                }

                work();
                return true;
            }
        }

        /// <summary>
        /// KeePass's thread, held still for a connection's reads and running a change's turn,
        /// counting the holds taken before the first turn.
        /// </summary>
        /// <remarks>
        /// A hold inside a hold is the same hold, and a read in the turn is a read on KeePass's
        /// own thread, which holds nothing, as both are on the real thread.
        /// </remarks>
        private sealed class HoldsCounted : IUiThread, IReadHold
        {
            private long _given;
            private bool _inTurn;

            /// <summary>Whether a change's turn ran.</summary>
            public bool Turned { get; private set; }

            /// <summary>
            /// How many holds were taken before the first turn, or in all where no turn ran.
            /// </summary>
            public int BeforeTheTurn { get; private set; }

            public long Stamp { get; private set; }

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
                if (_inTurn || Stamp != 0)
                {
                    return read();
                }

                if (!Turned)
                {
                    BeforeTheTurn++;
                }

                Stamp = ++_given;
                try
                {
                    return read();
                }
                finally
                {
                    Stamp = 0;
                }
            }
        }

        /// <summary>The lines the audit was handed, in order.</summary>
        private sealed class Lines : IGateAudit
        {
            public List<AuditEntry> Recorded { get; } = new List<AuditEntry>();

            public bool Keeps
            {
                get { return true; }
            }

            public void Record(AuditEntry entry)
            {
                Recorded.Add(entry);
            }
        }
    }
}
