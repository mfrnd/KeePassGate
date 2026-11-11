using System;
using System.Collections.Generic;
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
    /// A write is allowed by what the database holds when the write lands, and by nothing older.
    /// </summary>
    /// <remarks>
    /// The router and the handler decide on the connection's thread, each under a short hold of
    /// KeePass's thread, and the write then waits for a turn of its own: behind another write's
    /// save, which is as long as the database is large, or behind a dialog the operator is about
    /// to accept. Whatever the operator does in between is done to a database the decision never
    /// saw. Every case here is the real router over the real vault, with an operator's edit
    /// landing on KeePass's thread just before the queued write.
    /// </remarks>
    [TestFixture]
    public sealed class DecidedAtTheWriteTests
    {
        private const string Name = PwDefs.UserNameField;
        private const string Typed = "a secret typed in KeePass";

        private TemporaryDatabase _database;
        private PwGroup _group;
        private PwEntry _entry;
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

            _router = GateOperations.Router("9.9.9", null, new AccessCheck(NullGateAudit.Instance));
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
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RUL")));
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
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RUL")));
            var operatorFirst = new OperatorFirst(
                () => _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RL"))));

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
            _group.CustomData.Set(AclDocument.CustomDataKey, Rules.Granting(("agent", "RDL")));
            var kept = new PwGroup(true, true, "Kept", PwIcon.Folder);
            kept.CustomData.Set(AclDocument.CustomDataKey, Rules.Sealing(("agent", "RL")));
            _database.RootGroup.AddGroup(kept, true);
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

        private GateContext Send(IUiThread thread, string method, string path, string body)
        {
            var vault = new KdbxVault(
                _database.Database,
                new DatabaseWriter(thread, database => database.Save(null), null, null, null),
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
    }
}
