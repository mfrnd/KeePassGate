using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePass.Forms;
using KeePass.UI;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Transport;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Pairing against a real KeePass database, saved to a real file and opened again.
    /// </summary>
    /// <remarks>
    /// The loopback tests prove the protocol. This proves the half of phase 5 that only a real
    /// database can answer: that the record reaches the file before the certificate is handed over,
    /// that a group dialog holding a snapshot stops it, and that KeePass's own save is what
    /// "written" is made of.
    ///
    /// The one thing it cannot cover is `MainForm.SaveDatabase`, because there is no main window
    /// here. The end to end tier pairs against a real KeePass and finds the record in the file
    /// afterwards, which is where that is settled.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class PairingInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private GlobalWindowManagerWindows _windows;
        private GroupDialogs _dialogs;
        private DatabaseWriter _writer;
        private RecordingLog _log;
        private int _saves;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _windows = new GlobalWindowManagerWindows();
            _dialogs = new GroupDialogs(_windows, NullGateLog.Instance);
            _dialogs.Start();
            _log = new RecordingLog();
            _saves = 0;

            _writer = new DatabaseWriter(CurrentThread.Instance, Save, _dialogs, _log);

            var created = new AuthorityService(new CustomDataStore(_database.Database), null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
        }

        [TearDown]
        public void TearDown()
        {
            _dialogs.Dispose();
            _windows.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_record_is_written_and_the_database_is_saved()
        {
            using (var certificate = IssueClient())
            {
                var result = _writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(_saves, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_record_that_lands_is_announced_once_it_is_on_the_disk()
        {
            // What the clients page listens to. The moment worth announcing is this one and not
            // the write before it: a record put in the database and not saved is taken back out
            // again, and a page told about that would list a client the next request refuses.
            var announced = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance, Save, () => announced++, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.True, result.Reason);
                Assert.That(announced, Is.EqualTo(1));
            }
        }

        [Test]
        public void A_write_that_never_landed_is_not_announced()
        {
            // A save that does not reach the disk is undone, and the record is gone with it. A
            // page told about that would put a client on the list that is in no database.
            var announced = 0;
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                delegate { throw new System.IO.IOException("the disk said no"); },
                () => announced++,
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assume.That(result.Ok, Is.False, "this write was supposed to fail");
                Assert.That(announced, Is.EqualTo(0));
            }
        }

        [Test]
        public void KeePass_saying_it_has_no_unsaved_changes_is_what_written_is_made_of()
        {
            // Measured rather than assumed. KeePass's save tells its caller nothing, so the writer
            // reads this instead, and it would be reading a lie if this ever stopped being true.
            using (var certificate = IssueClient())
            {
                _database.Database.Modified = true;
                _database.Save();

                Assert.That(_database.Database.Modified, Is.False,
                    "a saved database that still says it is modified would break the pairing readback");
                Assert.That(certificate, Is.Not.Null);
            }
        }

        [Test]
        public void The_record_is_in_the_file_and_not_only_in_memory()
        {
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                Assume.That(_writer.WriteAdmission(_database.Database, record).Ok, Is.True);

                _database.SaveCloseAndReopen();

                var found = new AdmissionStore(new CustomDataStore(_database.Database), null)
                    .Find(record.AdmissionKey);

                Assert.That(found, Is.Not.Null, "a record that is not in the file admits nobody tomorrow");
                Assert.That(found.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void A_group_dialog_open_on_the_root_group_refuses_the_write()
        {
            using (var certificate = IssueClient())
            using (var form = GroupDialogOnTheRoot())
            {
                GlobalWindowManager.AddWindow(form);

                try
                {
                    var record = Record(certificate, "ops");

                    Assert.That(_writer.Blocked(_database.Database), Is.Not.Null);

                    var result = _writer.WriteAdmission(_database.Database, record);

                    Assert.That(result.Ok, Is.False);
                    Assert.That(result.IsBusy, Is.True, "it is worth trying again once that dialog is gone");
                    Assert.That(_saves, Is.Zero, "nothing was written, so nothing was saved");
                    Assert.That(_database.RootGroup.CustomData.Exists(record.StorageKey), Is.False);
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        [Test]
        public void A_save_that_does_not_happen_is_reported_rather_than_believed()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Reason, Does.Contain("unsaved changes"));
            }
        }

        [Test]
        public void A_save_that_does_not_happen_leaves_no_record_behind_in_the_database()
        {
            // **The refusal has to be true of the database as well as of the answer.** The record
            // is put into the group before the save is attempted, so a save that does not reach the
            // disk leaves it sitting in memory: the client is told the pairing failed and its
            // certificate is destroyed unsent, and then whatever the operator saves next writes an
            // admission record for a certificate nobody was ever issued: a client on the tab that
            // cannot be accounted for and was never handed anything.
            var writer = new DatabaseWriter(CurrentThread.Instance, database => { }, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var store = new CustomDataStore(_database.Database);

                // From a database with nothing outstanding, so that anything left over afterwards
                // is this write's doing and nobody else's.
                _database.Database.Modified = false;

                Assume.That(writer.WriteAdmission(_database.Database, record).Ok, Is.False);

                Assert.That(store.Get(record.StorageKey), Is.Null,
                    "a record that did not land must not be left in the group");
                Assert.That(_database.Database.Modified, Is.False,
                    "and the database must not be left asking to be saved for it");
            }
        }

        [Test]
        public void A_save_that_throws_leaves_no_record_behind_either()
        {
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new InvalidOperationException("the disk went away"); },
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                var store = new CustomDataStore(_database.Database);

                Assume.That(writer.WriteAdmission(_database.Database, record).Ok, Is.False);

                Assert.That(store.Get(record.StorageKey), Is.Null);
            }
        }

        [Test]
        public void A_save_that_throws_is_reported_rather_than_thrown()
        {
            var writer = new DatabaseWriter(
                CurrentThread.Instance,
                database => { throw new InvalidOperationException("the disk went away"); },
                _dialogs,
                _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.Reason, Does.Contain("disk went away"));
                Assert.That(_log.Errors, Is.Not.Empty);
            }
        }

        [Test]
        public void A_user_interface_that_never_answers_fails_the_pairing_rather_than_holding_it()
        {
            var writer = new DatabaseWriter(new NeverRuns(), Save, _dialogs, _log);

            using (var certificate = IssueClient())
            {
                var result = writer.WriteAdmission(_database.Database, Record(certificate, "ops"));

                Assert.That(result.Ok, Is.False);
                Assert.That(result.IsBusy, Is.True);
            }
        }

        [Test]
        public void There_is_nothing_to_write_to_when_the_database_is_closed()
        {
            using (var certificate = IssueClient())
            {
                var record = Record(certificate, "ops");
                _database.Database.Close();

                Assert.That(_writer.Blocked(_database.Database), Is.Not.Null);
                Assert.That(_writer.WriteAdmission(_database.Database, record).Ok, Is.False);
            }
        }

        [Test]
        public void Two_writers_at_once_take_their_turn_rather_than_interleaving()
        {
            // The single writer, which is the whole of the concurrency story on this path.
            using (var one = IssueClient())
            using (var two = IssueClient())
            {
                var inside = 0;
                var overlapped = false;

                var writer = new DatabaseWriter(
                    CurrentThread.Instance,
                    database =>
                    {
                        if (Interlocked.Increment(ref inside) > 1)
                        {
                            overlapped = true;
                        }

                        Thread.Sleep(50);
                        Interlocked.Decrement(ref inside);
                        Save(database);
                    },
                    _dialogs,
                    _log);

                var first = new Thread(() => writer.WriteAdmission(_database.Database, Record(one, "ops")));
                var second = new Thread(() => writer.WriteAdmission(_database.Database, Record(two, "ops")));

                first.Start();
                second.Start();
                first.Join();
                second.Join();

                Assert.That(overlapped, Is.False, "two pairings wrote at the same time");
            }
        }

        [Test]
        public void A_client_pairs_against_a_real_database_and_the_record_survives_the_file()
        {
            // The whole of step 8 against a real database: an invitation, both proofs, an operator
            // who agrees, and a certificate that the reopened file still admits.
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);

                var context = Pair(book, target, invitation, "agent-fictitious", new[] { "ops" });

                Assert.That(context.Status, Is.EqualTo(200), context.Json);

                using (var issued = IdentityIn(context))
                {
                    _database.SaveCloseAndReopen();

                    using (var afterwards = Targets())
                    {
                        var reopened = afterwards.Current().Single();
                        var identity = ClientCredentials.For(issued, reopened, _log);

                        Assert.That(identity.IsPaired, Is.True,
                            "the certificate handed over is not admitted by the file it was recorded in");
                        Assert.That(identity.Profiles, Is.EqualTo(new[] { "ops" }));
                    }
                }
            }
        }

        [Test]
        public void A_group_dialog_open_on_the_root_group_refuses_the_pairing_and_keeps_the_invitation()
        {
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, _log))
            using (var targets = Targets())
            using (var form = GroupDialogOnTheRoot())
            {
                var target = targets.Current().Single();
                var invitation = book.Invite(target.DatabaseId);

                GlobalWindowManager.AddWindow(form);

                try
                {
                    var context = Pair(book, target, invitation, "agent-fictitious", Array.Empty<string>());

                    Assert.That(context.Status, Is.EqualTo(503));
                    Assert.That(book.Outstanding().Count, Is.EqualTo(1),
                        "an open dialog costs an attempt, never the invitation");
                }
                finally
                {
                    GlobalWindowManager.RemoveWindow(form);
                }
            }
        }

        private OpenDatabaseTargets Targets()
        {
            var databases = new List<PwDatabase> { _database.Database };
            return new OpenDatabaseTargets(delegate { return databases; }, _writer, _log);
        }

        /// <summary>The whole exchange, at the level of the handler, against a real target.</summary>
        private GateContext Pair(
            IInvitations book, IGateTarget target, Invitation invitation, string name, string[] profiles)
        {
            var token = invitation.Token;
            var binding = PairingProof.Binding(target.Host.CertificateDer);
            var nonce = new byte[SniName.NonceLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            var connection = new PairingConnection(invitation, target, binding, nonce, "127.0.0.1:60001");
            var handler = new PairingHandler(book, new AgreeingOperator(), _log);

            var hello = Post(
                PairingHandler.HelloPath,
                "{\"proof\":\"" + Convert.ToBase64String(PairingProof.Client(token, binding, nonce)) + "\"}",
                connection);

            handler.Hello(hello);

            if (hello.Status != 200)
            {
                return hello;
            }

            var challenge = Convert.FromBase64String(JsonBody.Read(hello.Json).Text("challenge"));
            var proof = PairingProof.Request(
                token, binding, nonce, challenge, PairingProof.Asked(name, profiles));

            var body = new System.Text.StringBuilder();
            body.Append("{\"name\":\"").Append(name).Append("\",\"profiles\":[");
            for (var i = 0; i < profiles.Length; i++)
            {
                if (i > 0)
                {
                    body.Append(',');
                }

                body.Append('"').Append(profiles[i]).Append('"');
            }

            body.Append("],\"proof\":\"").Append(Convert.ToBase64String(proof)).Append("\"}");

            var request = Post(PairingHandler.RequestPath, body.ToString(), connection);
            handler.Request(request);
            return request;
        }

        private static GateContext Post(string path, string body, PairingConnection connection)
        {
            var head = new HttpRequestHead(
                "POST",
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                body.Length);

            var context = new GateContext(head, body, ClientIdentity.Stranger("database"));
            connection.PutOn(context);
            return context;
        }

        private static X509Certificate2 IdentityIn(GateContext context)
        {
            var body = JsonBody.Read(context.Json);
            Assert.That(body, Is.Not.Null, context.Json);

            return new X509Certificate2(
                Convert.FromBase64String(body.Text("identity")),
                (string)null,
                CertificateBlob.SigningFlags);
        }

        private X509Certificate2 IssueClient()
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), null).Read())
            {
                return new AuthorityIssuer().IssueClient(
                    reading.Material.Authority, "agent-fictitious", new[] { "ops" });
            }
        }

        private static AdmissionRecord Record(X509Certificate2 certificate, params string[] profiles)
        {
            return AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles);
        }

        private GroupForm GroupDialogOnTheRoot()
        {
            var form = new GroupForm();
            form.InitEx(_database.RootGroup, false, null, _database.Database);
            return form;
        }

        private void Save(PwDatabase database)
        {
            _saves++;
            database.Save(null);
        }

        /// <summary>An operator who agrees to exactly what was asked for.</summary>
        private sealed class AgreeingOperator : IPairingApproval
        {
            public PairingApproval Ask(PairingRequest request, TimeSpan timeout)
            {
                return PairingApproval.Approved(request.Profiles);
            }
        }

        /// <summary>A user interface thread that is busy elsewhere and never gets to the work.</summary>
        private sealed class NeverRuns : IUiThread
        {
            public bool Run(Action work, TimeSpan timeout)
            {
                return false;
            }
        }

        /// <summary>A log that remembers, so that a failure can be shown to have been reported.</summary>
        private sealed class RecordingLog : IGateLog
        {
            private readonly List<string> _errors = new List<string>();

            public IReadOnlyList<string> Errors
            {
                get { return _errors; }
            }

            public void Info(string message)
            {
                TestContext.Out.WriteLine("INFO  " + message);
            }

            public void Warn(string message)
            {
                TestContext.Out.WriteLine("WARN  " + message);
            }

            public void Error(string message, Exception error)
            {
                _errors.Add(message);
                TestContext.Out.WriteLine("ERROR " + message + " | " + error);
            }

            public void Unexpected(string message, Exception error)
            {
                // Its own level, because these are the ones that mean there is a defect.
                Error(message, error);
            }
        }
    }
}
