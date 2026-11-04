using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Utility;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassGate.Vault;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The operator's standing answer to a renewal, written into a real KeePass database, saved to
    /// a real file and opened again.
    /// </summary>
    /// <remarks>
    /// The renewed record itself is written exactly as pairing writes one, which
    /// `PairingInRealDatabaseTests` follows to the file. What is new here is the key a refusal
    /// leaves when the operator means more than this once.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class RenewalInRealDatabaseTests
    {
        private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _dialogs.Start();
            _saves = 0;
            _failSaves = false;

            _restoreAfterSave = null;

            _writer = new DatabaseWriter(CurrentThread.Instance, Save, null, _dialogs, null);
            _savingWriter = _writer;
            _database.Save();
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_standing_answer_is_in_the_file_and_not_only_in_memory()
        {
            var result = _writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(_saves, Is.EqualTo(1));

            _database.SaveCloseAndReopen();

            var standing = new AdmissionStore(new CustomDataStore(_database.Database), null).StandingRefusal(Key);
            Assert.That(standing, Is.Not.Null);
            Assert.That(standing.IsForever, Is.True);
        }

        [Test]
        public void A_later_standing_answer_saved_replaces_the_one_before_it_in_the_file()
        {
            var earlier = RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero));
            Assume.That(_writer.RefuseRenewal(_database.Database, Key, earlier).Ok, Is.True);

            var result = _writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

            Assert.That(result.Ok, Is.True, result.Reason);
            _database.SaveCloseAndReopen();

            var standing = new AdmissionStore(new CustomDataStore(_database.Database), null).StandingRefusal(Key);
            Assert.That(standing.IsForever, Is.True);
        }

        [Test]
        public void The_maintenance_dialog_open_on_the_database_refuses_it()
        {
            using (var form = new DatabaseOperationsForm())
            {
                form.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var result = _writer.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

                    Assert.That(result.IsBusy, Is.True, result.Reason);
                    Assert.That(_saves, Is.Zero);
                    Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.RenewalRefusalKey(Key)), Is.False);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void One_no_save_kept_is_taken_back_out_and_the_answer_before_it_put_back()
        {
            var earlier = RenewalRefusal.Until(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero));
            Assume.That(_writer.RefuseRenewal(_database.Database, Key, earlier).Ok, Is.True);
            _database.Database.Modified = false;

            var failing = new DatabaseWriter(CurrentThread.Instance, database => { }, null, _dialogs, null);
            var result = failing.RefuseRenewal(_database.Database, Key, RenewalRefusal.Forever());

            Assert.That(result.IsUnsaved, Is.True, result.Reason);
            Assert.That(_database.Database.CustomData.Get(AuthorityKeys.RenewalRefusalKey(Key)), Is.EqualTo(earlier.Write()));
            Assert.That(_database.Database.Modified, Is.False);
        }

        [Test]
        public void KeePass_busy_with_something_of_its_own_refuses_a_renewal_before_the_operator_is_asked()
        {
            // A renewal through the writer the plugin runs, asked whether KeePass is interrupting
            // the way the plugin asks it: here by the count of windows KeePass is waiting on,
            // which an export raises. The real writer used to be handed nothing to ask.
            using (var renewal = new RealRenewal(this))
            {
                MessageService.ExternalIncrementMessageCount();
                try
                {
                    var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                    Assert.That(context.Status, Is.EqualTo(503), context.Json);
                    Assert.That(renewal.Asked, Is.Zero, "nobody was disturbed");
                    Assert.That(renewal.Records, Is.EqualTo(1), "and nothing was written");
                }
                finally
                {
                    MessageService.ExternalDecrementMessageCount();
                }
            }
        }

        [Test]
        public void KeePass_busy_by_the_time_the_operator_approves_writes_nothing()
        {
            using (var renewal = new RealRenewal(this))
            {
                var raised = false;
                try
                {
                    var context = renewal.Ask(
                        RenewalApproval.Approved(new[] { "ops" }),
                        () =>
                        {
                            MessageService.ExternalIncrementMessageCount();
                            raised = true;
                        });

                    Assert.That(context.Status, Is.EqualTo(503), context.Json);
                    Assert.That(renewal.Asked, Is.EqualTo(1));
                    Assert.That(context.Json, Does.Contain("heldUntil"), "the approval is held for the retry");
                    Assert.That(renewal.Records, Is.EqualTo(1), "and nothing is issued");
                }
                finally
                {
                    if (raised)
                    {
                        MessageService.ExternalDecrementMessageCount();
                    }
                }
            }
        }

        [Test]
        public void The_dialog_is_built_from_the_request_the_handler_made_out_of_the_database()
        {
            // The step from what the handler read to what the operator sees, which every dialog
            // test skipped by building its request by hand.
            using (var renewal = new RealRenewal(this))
            {
                var names = ProfileNames.Read(null);
                Assume.That(names.Add("audit"), Is.Null);
                _database.Database.CustomData.Set(ProfileNames.CustomDataKey, names.Write());

                Assume.That(renewal.Ask(RenewalApproval.Refused(null), null).Status, Is.EqualTo(403));

                var request = renewal.LastRequest;
                Assert.That(request.Profiles, Is.Empty, "\"ops\" is off the list, so it is not offered");
                Assert.That(request.Dropped, Is.EqualTo(new[] { "ops" }));

                using (var dialog = new RenewalApprovalForm(request, TimeSpan.FromMinutes(2), null))
                {
                    var shown = new System.Text.StringBuilder();
                    foreach (var control in ControlTree.Descendants(dialog))
                    {
                        shown.AppendLine(control.Text);
                    }

                    Assert.That(shown.ToString(), Does.Contain(RenewalApprovalForm.Dropped(request)));
                    Assert.That(shown.ToString(), Does.Contain("agent-fictitious"));
                }
            }
        }

        [Test]
        public void A_revocation_landing_between_the_last_read_and_the_write_is_not_overtaken()
        {
            // The handler reads the record again after the dialog, on its own thread; a revocation
            // that lands after that read and before the write takes the writer's lock was
            // overtaken, and the revoked certificate's holder was issued a new one. The write
            // compares the record under that lock now.
            using (var renewal = new RealRenewal(this))
            {
                renewal.BeforeTheWrite = () => Assume.That(
                    new AdmissionStore(new CustomDataStore(_database.Database), null).Remove(renewal.Key), Is.True);

                var context = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);

                Assert.That(context.Status, Is.EqualTo(401), context.Json);
                Assert.That(renewal.Records, Is.Zero, "the revoked record is all there was, and nothing was added");
                Assert.That(_saves, Is.Zero, "nothing was written");
            }
        }

        [Test]
        public void KeePass_busy_when_a_standing_answer_is_written_holds_it_from_memory_until_the_next_write()
        {
            // The operator's ruling of 2026-10-14: the answer takes effect at once, like one for
            // this session, and the plugin's next write to the database that lands writes it.
            using (var renewal = new RealRenewal(this))
            {
                var raised = false;
                try
                {
                    var context = renewal.Ask(
                        RenewalApproval.Refused(RenewalRefusal.Forever()),
                        () =>
                        {
                            MessageService.ExternalIncrementMessageCount();
                            raised = true;
                        });

                    Assert.That(context.Status, Is.EqualTo(403), "the refusal stands");
                    Assert.That(renewal.Standing, Is.Null, "the database could not take the answer");
                    Assert.That(_saves, Is.Zero);
                }
                finally
                {
                    if (raised)
                    {
                        MessageService.ExternalDecrementMessageCount();
                    }
                }

                var again = renewal.Ask(RenewalApproval.Approved(new[] { "ops" }), null);
                Assert.That(again.Status, Is.EqualTo(409), again.Json);
                Assert.That(again.Json, Does.Contain("forever"), "answered from memory, without the dialog");
                Assert.That(renewal.Asked, Is.EqualTo(1));

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));
                Assume.That(written.Ok, Is.True, written.Reason);

                Assert.That(renewal.Standing, Is.Not.Null, "written with the plugin's next write");
                Assert.That(renewal.Standing.IsForever, Is.True);
                Assert.That(renewal.Session.Owed(), Is.Empty, "and the database's copy answers from here");

                _database.SaveCloseAndReopen();
                Assert.That(
                    new AdmissionStore(new CustomDataStore(_database.Database), null).StandingRefusal(renewal.Key),
                    Is.Not.Null, "and it is in the file");
            }
        }

        [Test]
        public void An_owed_answer_rides_on_a_write_that_did_not_land_and_stays_owed()
        {
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);
                _failSaves = true;

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

                Assert.That(written.Ok, Is.False);
                Assert.That(renewal.Standing, Is.Null, "taken back out with the change");
                Assert.That(renewal.Session.Owed(), Has.Count.EqualTo(1), "and still owed");
            }
        }

        [TestCase(true, TestName = "an owed answer whose authority was recreated is settled by the next write")]
        [TestCase(false, TestName = "an owed answer whose authority was deleted is settled by the next write")]
        public void An_owed_answer_whose_authority_was_retired_is_settled_by_the_next_write(bool recreate)
        {
            // A recreate or a delete retires the authority and takes every record with it, so
            // nothing is left for the answer to refuse and no renewal can ask about it again.
            // Skipped rather than settled, it stayed owed for the session, and the exit counted it
            // as an answer whose client would be asked about again.
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);
                var authority = new AuthorityService(new CustomDataStore(_database.Database), null);
                var retired = recreate ? authority.Recreate() : authority.Delete();
                Assume.That(retired.Succeeded, Is.True, retired.Message);

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));
                Assume.That(written.Ok, Is.True, written.Reason);

                Assert.That(renewal.Session.Owed(), Is.Empty);
                Assert.That(renewal.Standing, Is.Null, "and nothing was written for a record that has gone");
            }
        }

        [Test]
        public void An_owed_answer_gives_way_to_an_answer_written_since_it_was_given()
        {
            // Refused forever while KeePass was busy, then until a date on a later dialog, which
            // landed. That write took the owed answer as well, after its own change, and wrote the
            // older forever over the date it had just written.
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);
                var until = RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(30));

                var written = renewal.Writer.RefuseRenewal(_database.Database, renewal.Key, until);
                Assume.That(written.Ok, Is.True, written.Reason);

                Assert.That(RenewalRefusal.Same(renewal.Standing, until), Is.True, "the later answer stands");
                Assert.That(renewal.Session.Owed(), Is.Empty, "and the earlier answer is let go");
            }
        }

        [Test]
        public void An_owed_answer_gives_way_to_an_answer_a_synchronize_brought_in()
        {
            // A merge writes the key without asking anybody, which is how another copy's answer
            // arrives, and the owed answer was given over what the key held before it.
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(
                    renewal.DatabaseId, renewal.Key, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(30)), null);
                _database.Database.CustomData.Set(AuthorityKeys.RenewalRefusalKey(renewal.Key), RenewalRefusal.ForeverValue);

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));
                Assume.That(written.Ok, Is.True, written.Reason);

                Assert.That(renewal.Standing.IsForever, Is.True, "the answer the merge brought stands");
                Assert.That(renewal.Session.Owed(), Is.Empty);
            }
        }

        [Test]
        public void An_owed_answer_whose_day_has_begun_is_let_go_unwritten()
        {
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(
                    renewal.DatabaseId, renewal.Key, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(-1)), null);

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));
                Assume.That(written.Ok, Is.True, written.Reason);

                Assert.That(renewal.Standing, Is.Null, "nothing is written that refuses nothing");
                Assert.That(renewal.Session.Owed(), Is.Empty);
            }
        }

        [Test]
        public void An_owed_answer_lifted_on_the_clients_page_is_written_by_no_write_before_OK()
        {
            // Allow renewal on an answer that is only owed buffers nothing in the database, since
            // no key holds it yet, and a write landing before OK wrote the answer and let it go
            // from memory: OK then found nothing to lift, and the client stayed refused.
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);
                var pending = new PendingSessionRefusals(renewal.Session);
                Assume.That(pending.Lift(renewal.Key), Is.True);

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));
                Assume.That(written.Ok, Is.True, written.Reason);

                Assert.That(renewal.Standing, Is.Null, "not written while the lift waits for OK");
                Assert.That(renewal.Session.Answer(renewal.Key, DateTimeOffset.UtcNow), Is.Not.Null,
                    "and the gate still refuses until then");

                pending.Apply();

                Assert.That(renewal.Session.Answer(renewal.Key, DateTimeOffset.UtcNow), Is.Null, "OK lifts it");
                Assert.That(renewal.Session.Owed(), Is.Empty);
            }
        }

        [Test]
        public void An_owed_answer_is_written_by_a_save_the_plugin_did_not_make()
        {
            // The operator keeps an authority, or presses the KPG tab's OK, and saves: neither is a
            // write of the plugin's own, and the answers rode on those alone, so the file went
            // without them and the exit told the operator nothing had been written since.
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);

                SaveAsKeePass(renewal.Writer, _database.Database, true);

                Assert.That(renewal.Standing, Is.Not.Null, "the operator's save carried it");
                Assert.That(renewal.Standing.IsForever, Is.True);
                Assert.That(renewal.Session.Owed(), Is.Empty, "and the database's copy answers from here");
            }
        }

        [Test]
        public void An_owed_answer_waits_through_a_copy_and_a_save_that_failed()
        {
            using (var renewal = new RealRenewal(this))
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);

                renewal.Writer.BeforeSave(_database.Database, true);
                Assert.That(renewal.Standing, Is.Null, "a copy leaves the database's own file as it was");

                SaveAsKeePass(renewal.Writer, _database.Database, false);
                Assert.That(renewal.Standing, Is.Null, "taken back out with the save that did not land");
                Assert.That(renewal.Session.Owed(), Has.Count.EqualTo(1), "and still owed");
            }
        }

        [Test]
        public void An_owed_answer_is_not_written_under_an_open_maintenance_dialog()
        {
            // The dialog writes its copy of the custom data back when it closes whenever the number
            // of keys differs, so an answer written underneath it went with that, after memory had
            // already let it go.
            using (var renewal = new RealRenewal(this))
            using (var maintenance = new DatabaseOperationsForm())
            {
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);
                maintenance.InitEx(_database.Database);
                GlobalWindowManager.AddWindow(maintenance);

                try
                {
                    SaveAsKeePass(renewal.Writer, _database.Database, true);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(maintenance);
                }

                Assert.That(renewal.Standing, Is.Null, "nothing written under the dialog");
                Assert.That(renewal.Session.Owed(), Has.Count.EqualTo(1), "and still owed for the next save");
            }
        }

        [Test]
        public void An_owed_answer_stays_owed_after_a_write_whose_file_did_not_read_back()
        {
            // Settled there, an answer whose place in the file nobody knew left memory, and a file
            // that lacked it lost it, with no word at exit.
            using (var renewal = new RealRenewal(this))
            {
                _database.Save();
                _restoreAfterSave = System.IO.File.ReadAllBytes(_database.Path);
                renewal.Session.Owe(renewal.DatabaseId, renewal.Key, RenewalRefusal.Forever(), null);

                var written = renewal.Writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

                Assert.That(written.IsUnverified, Is.True, written.Reason);
                Assert.That(renewal.Session.Owed(), Has.Count.EqualTo(1), "still owed");
            }
        }

        private bool _failSaves;

        /// <summary>The file as it was, put back after a save, which the writer then cannot read back.</summary>
        private byte[] _restoreAfterSave;

        /// <summary>
        /// The writer whose saves raise what `MainForm.SaveDatabase` raises either side of a save,
        /// as KeePass's own events reach the plugin: the fixture's, or a renewal's.
        /// </summary>
        private DatabaseWriter _savingWriter;

        private void Save(PwDatabase database)
        {
            _saves++;
            SaveAsKeePass(_savingWriter, database, !_failSaves);

            if (_restoreAfterSave != null)
            {
                System.IO.File.WriteAllBytes(_database.Path, _restoreAfterSave);
            }
        }

        /// <summary>
        /// A save as `MainForm.SaveDatabase` makes it, with `FileSavingPre` before it and `FileSaved`
        /// after it, saying whether it landed; a save that does not land writes nothing.
        /// </summary>
        private static void SaveAsKeePass(DatabaseWriter writer, PwDatabase database, bool lands)
        {
            if (writer != null)
            {
                writer.BeforeSave(database, false);
            }

            if (lands)
            {
                database.Save(null);
            }

            if (writer != null)
            {
                writer.AfterSave(database, lands);
            }
        }

        /// <summary>
        /// A renewal of an admitted client through the targets and the writer the plugin builds,
        /// with the writer asking KeePass's count of the windows it is waiting on.
        /// </summary>
        private sealed class RealRenewal : IDisposable
        {
            private readonly RenewalInRealDatabaseTests _test;
            private readonly OpenDatabaseTargets _targets;
            private readonly IGateTarget _target;
            private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _client;
            private readonly string _key;

            public RealRenewal(RenewalInRealDatabaseTests test)
            {
                _test = test;
                var database = test._database.Database;
                var created = new AuthorityService(new CustomDataStore(database), null).Create();
                Assume.That(created.Succeeded, Is.True, created.Message);

                var writer = new DatabaseWriter(
                    new Hooked(this),
                    test.Save,
                    () => MessageService.CurrentMessageCount != 0 ? "KeePass is waiting for an answer of its own" : null,
                    null,
                    null,
                    test._dialogs,
                    null);

                var databases = new List<PwDatabase> { database };
                writer.Owed = _session;
                Writer = writer;
                test._savingWriter = writer;
                _targets = new OpenDatabaseTargets(delegate { return databases; }, writer, null);
                _target = _targets.Current().Single();

                _client = new AuthorityIssuer().IssueClient(_target.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read();
                var record = AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" });
                Assume.That(writer.WriteAdmission(database, record).Ok, Is.True);
                _key = record.AdmissionKey;
                test._saves = 0;
            }

            public int Asked { get; private set; }

            /// <summary>The plugin's memory of standing answers, as the plugin wires it to the writer.</summary>
            private readonly SessionRefusals _session = new SessionRefusals();

            /// <summary>The writer the plugin would build, for a test that writes something else.</summary>
            public DatabaseWriter Writer { get; private set; }

            public SessionRefusals Session
            {
                get { return _session; }
            }

            /// <summary>What the operator was last shown.</summary>
            public RenewalRequest LastRequest { get; private set; }

            /// <summary>Run once, on the next write's turn, before the write itself.</summary>
            public Action BeforeTheWrite { get; set; }

            public string Key
            {
                get { return _key; }
            }

            public string DatabaseId
            {
                get { return _target.DatabaseId; }
            }

            public int Records
            {
                get { return new AdmissionStore(new CustomDataStore(_test._database.Database), null).All().Records.Count; }
            }

            public RenewalRefusal Standing
            {
                get { return new AdmissionStore(new CustomDataStore(_test._database.Database), null).StandingRefusal(_key); }
            }

            public GateContext Ask(RenewalApproval answer, Action meanwhile)
            {
                var head = new HttpRequestHead(
                    "POST",
                    RenewalHandler.Path,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    2);

                var context = new GateContext(
                    head, "{}", ClientIdentity.Paired(_target.DatabaseId, _client, new[] { "ops" }));
                new ServedDatabase(null, _target).PutOn(context);
                ConnectionPeer.PutOn(context, "127.0.0.1:60001");

                var handler = new RenewalHandler(new Operator(this, answer, meanwhile), _session, null, null);
                handler.Renew(context);
                return context;
            }

            public void Dispose()
            {
                _target.Dispose();
                _targets.Dispose();
                _client.Reset();
            }

            /// <summary>KeePass's thread as the writer sees it, with a moment in front of the next write.</summary>
            private sealed class Hooked : IUiThread
            {
                private readonly RealRenewal _renewal;

                public Hooked(RealRenewal renewal)
                {
                    _renewal = renewal;
                }

                public string Stalled
                {
                    get { return null; }
                }

                public bool Run(Action work, TimeSpan timeout)
                {
                    var before = _renewal.BeforeTheWrite;
                    _renewal.BeforeTheWrite = null;
                    if (before != null)
                    {
                        before();
                    }

                    work();
                    return true;
                }
            }

            private sealed class Operator : IRenewalApproval
            {
                private readonly RealRenewal _renewal;
                private readonly RenewalApproval _answer;
                private readonly Action _meanwhile;

                public Operator(RealRenewal renewal, RenewalApproval answer, Action meanwhile)
                {
                    _renewal = renewal;
                    _answer = answer;
                    _meanwhile = meanwhile;
                }

                public RenewalApproval Ask(RenewalRequest request, TimeSpan timeout)
                {
                    _renewal.Asked++;
                    _renewal.LastRequest = request;
                    if (_meanwhile != null)
                    {
                        _meanwhile();
                    }

                    return _answer;
                }
            }
        }
    }
}
