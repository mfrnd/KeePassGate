using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Transport;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Renewals, a client's revocation of itself and other clients' writes together, through a real
    /// <see cref="ControlUiThread"/> on a message loop of its own, a save that pumps as KeePass's
    /// does, the real approval prompt and a real database.
    /// </summary>
    /// <remarks>
    /// Every other fixture builds the writer on <see cref="CurrentThread"/>, where nothing is ever
    /// delivered inside anything else, and every prompt test fakes KeePass's thread. So a dialog
    /// opened inside another client's save, an approval thrown away because the plugin's own save
    /// read as KeePass being busy, a revocation overtaken by a renewal after its last read and an
    /// approval written after a lock all passed every tier. This is the tier where each of them is
    /// asked for.
    ///
    /// **The save is KeePass's save as far as the message queue is concerned**: it raises the
    /// interaction block the writer asks about, pumps on the way in, pumps again while it
    /// serializes, and pumps on the way out, which is what `MainForm.SaveDatabase` does through
    /// `UIBlockInteraction` and its status bar logger. The operator is a dialog that shows nowhere
    /// and answers after a moment's thought, through the prompt's own <c>Show</c>, so a lock
    /// closes it as it closes the real one.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class ApprovalsUnderLoadTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        private TemporaryDatabase _database;
        private UiLoop _ui;
        private DatabaseWriter _writer;
        private OpenDatabaseTargets _targets;
        private PairingApprovalPrompt _prompt;
        private ApprovalBook _held;
        private SessionRefusals _session;
        private OrderedAudit _audit;
        private int _blocked;
        private int _insideSave;
        private int _shown;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            var created = new AuthorityService(new CustomDataStore(_database.Database), null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
            _database.Save();

            _ui = new UiLoop();
            _blocked = 0;
            _insideSave = 0;
            _shown = 0;

            var ui = new ControlUiThread(_ui.Control);
            _writer = new DatabaseWriter(
                ui, KeePassLikeSave, () => _blocked > 0 ? "KeePass is in the middle of an operation of its own" : null,
                null, null, null, NullGateLog.Instance);

            var databases = new List<PwDatabase> { _database.Database };
            _targets = new OpenDatabaseTargets(delegate { return databases; }, _writer, NullGateLog.Instance);
            _session = new SessionRefusals();
            _held = new ApprovalBook();
            _audit = new OrderedAudit();

            _prompt = new PairingApprovalPrompt(ui, _targets.Serving, _writer, NullGateLog.Instance);
            _prompt.RenewalDialog = (request, timeout) =>
            {
                var shown = Show();
                return shown ? RenewalApproval.Approved(request.Profiles, request.Lifetime) : RenewalApproval.TimedOut();
            };
        }

        [TearDown]
        public void TearDown()
        {
            _targets.Dispose();
            _ui.Dispose();
            _database.Dispose();
        }

        [Test]
        public void No_dialog_opens_inside_a_save_and_no_approval_is_lost_to_another_clients_writes()
        {
            // Four clients renewing while two others write back to back. Before the prompt asked
            // what was pumping, a share of the dialogs opened inside another client's save; before
            // the writer told its own save apart, renewals were turned away or their approvals
            // thrown away for saves that only had to be waited for.
            var renewing = new[] { Admit("agent-one"), Admit("agent-two"), Admit("agent-three"), Admit("agent-four") };
            using (var writing = new CancellationTokenSource())
            {
                var written = 0;
                var refused = new ConcurrentBag<string>();

                var writers = Enumerable.Range(0, 2).Select(index => new Thread(() =>
                {
                    while (!writing.IsCancellationRequested)
                    {
                        var outcome = _writer.Change(_database.Database, () =>
                        {
                            var group = new PwGroup(
                                true, true, "invented-" + index + "-" + Interlocked.Increment(ref written), PwIcon.Folder);
                            _database.Database.RootGroup.AddGroup(group, true);
                            return new VaultChange(VaultWrite.Created(group.Uuid.ToHexString()),
                                () => _database.Database.RootGroup.Groups.Remove(group));
                        });

                        if (!outcome.Ok)
                        {
                            refused.Add(outcome.Reason);
                        }
                    }
                }) { IsBackground = true }).ToList();

                writers.ForEach(thread => thread.Start());

                try
                {
                    var asking = renewing
                        .Select(certificate => System.Threading.Tasks.Task.Run(() => Renew(certificate)))
                        .ToList();
                    var answers = asking.Select(task => task.Result).ToList();

                    foreach (var answer in answers)
                    {
                        Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
                    }
                }
                finally
                {
                    writing.Cancel();
                    writers.ForEach(thread => thread.Join(Patience));
                    foreach (var certificate in renewing)
                    {
                        certificate.Reset();
                    }
                }

                Assert.That(_shown, Is.EqualTo(4), "each renewal was put to the operator once");
                Assert.That(_insideSave, Is.Zero, "and never inside a save");
                Assert.That(written, Is.GreaterThan(0), "the other clients were writing all along");
                Assert.That(refused, Is.Empty, "and none of their writes was turned away");
            }
        }

        [Test]
        public void A_revocation_racing_an_approved_renewal_is_never_overtaken()
        {
            // The client's own revocation, sent on another connection the moment the dialog
            // closes. Whichever lands first, a revoked certificate must not end the race renewed.
            for (var round = 0; round < 8; round++)
            {
                using (var old = Admit("agent-racing-" + round))
                using (var revoking = new ManualResetEventSlim())
                {
                    var key = CertificateDigest.Of(old);
                    _prompt.RenewalDialog = (request, timeout) =>
                    {
                        var shown = Show();
                        revoking.Set();
                        return shown ? RenewalApproval.Approved(request.Profiles, request.Lifetime) : RenewalApproval.TimedOut();
                    };

                    var revoked = new Thread(() =>
                    {
                        revoking.Wait(Patience);
                        Thread.Sleep(round * 3);
                        using (var target = _targets.Current().Single())
                        {
                            target.Revoke(key, () => _audit.Note("revoked " + key));
                        }
                    }) { IsBackground = true };

                    revoked.Start();
                    var answer = Renew(old);
                    revoked.Join(Patience);

                    var order = _audit.Lines.ToList();
                    var renewedAt = order.FindIndex(line => line == "renewed " + key);
                    var revokedAt = order.FindIndex(line => line == "revoked " + key);

                    Assert.That(revokedAt, Is.Not.EqualTo(-1), "the revocation landed");
                    if (answer.Status == 200)
                    {
                        Assert.That(renewedAt, Is.LessThan(revokedAt), "a renewal answered 200 landed first");
                    }
                    else
                    {
                        Assert.That(answer.Status, Is.EqualTo(401), answer.Json);
                        Assert.That(renewedAt, Is.EqualTo(-1), "and one that lost wrote nothing");
                    }
                }
            }
        }

        [Test]
        public void An_approval_still_settling_when_KeePass_locks_is_answered_as_the_lock_and_written_nowhere()
        {
            // KeePass busy with something of its own as the operator answers, then a lock: the lock
            // takes the approval away, and the renewal is told the database is locked instead of
            // being written into the database a quick unlock opens again.
            using (var old = Admit("agent-locked-out"))
            {
                _prompt.RenewalDialog = (request, timeout) =>
                {
                    var shown = Show();
                    Interlocked.Increment(ref _blocked);
                    new Thread(() =>
                    {
                        Thread.Sleep(300);
                        _ui.Invoke(() =>
                        {
                            ((IApprovalScreen)_held).Locked();
                            _database.Database.Close();
                        });
                        Interlocked.Decrement(ref _blocked);
                    }) { IsBackground = true }.Start();
                    return shown ? RenewalApproval.Approved(request.Profiles, request.Lifetime) : RenewalApproval.TimedOut();
                };

                var answer = Renew(old);

                Assert.That(answer.Status, Is.EqualTo(503), answer.Json);
                Assert.That(answer.Json, Is.EqualTo(HttpResponse.ErrorBody(LockedTarget.Error)));
                Assert.That(_audit.Lines.Any(line => line.StartsWith("renewed", StringComparison.Ordinal)), Is.False);
                Assert.That(_held.Count, Is.Zero, "nothing is held across the lock");
            }
        }

        /// <summary>
        /// What KeePass's save does to the message queue: the interaction block raised for as long
        /// as it runs, a pump on the way in, one per step of the serialization and one on the way
        /// out. A dialog delivered by any of them was delivered inside the save.
        /// </summary>
        private void KeePassLikeSave(PwDatabase database)
        {
            Interlocked.Increment(ref _blocked);
            try
            {
                Application.DoEvents();
                for (var step = 0; step < 10; step++)
                {
                    Thread.Sleep(10);
                    Application.DoEvents();
                }

                database.Save(null);
                Application.DoEvents();
            }
            finally
            {
                Interlocked.Decrement(ref _blocked);
            }
        }

        /// <summary>The operator reading a dialog for a moment, through the prompt's own show.</summary>
        private bool Show()
        {
            if (_blocked > 0)
            {
                Interlocked.Increment(ref _insideSave);
            }

            Interlocked.Increment(ref _shown);
            using (var dialog = new ThinkingDialog(150))
            {
                _prompt.Show(dialog);
                return dialog.Answered;
            }
        }

        private X509Certificate2 Admit(string name)
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), null).Read())
            {
                var certificate = new AuthorityIssuer().IssueClient(reading.Material.Authority, name, new[] { "ops" });
                var written = _writer.WriteAdmission(
                    _database.Database, AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));
                Assume.That(written.Ok, Is.True, written.Reason);
                return certificate;
            }
        }

        private GateContext Renew(X509Certificate2 certificate)
        {
            using (var target = _targets.Current().Single())
            {
                var head = new HttpRequestHead(
                    "POST",
                    RenewalHandler.Path,
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    2);

                var context = new GateContext(
                    head, "{}", ClientIdentity.Paired(target.DatabaseId, certificate, new[] { "ops" }));
                new ServedDatabase(null, target).PutOn(context);
                ConnectionPeer.PutOn(context, "127.0.0.1:60001");

                var audit = new AdmissionAudit(_audit);
                new RenewalHandler(_prompt, new PairingOptions(), new AuthorityIssuer(), null, _session, audit, NullGateLog.Instance, _held)
                    .Renew(context);
                return context;
            }
        }

        /// <summary>KeePass's thread: a control with a handle, on a message loop of its own.</summary>
        private sealed class UiLoop : IDisposable
        {
            private readonly Thread _thread;
            private readonly ManualResetEventSlim _ready = new ManualResetEventSlim();

            public UiLoop()
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "KeePass's thread" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                _ready.Wait(Patience);
            }

            public Control Control { get; private set; }

            public void Invoke(Action action)
            {
                Control.Invoke(action);
            }

            public void Dispose()
            {
                try
                {
                    Control.BeginInvoke((MethodInvoker)(() =>
                    {
                        Control.Dispose();
                        Application.ExitThread();
                    }));
                }
                catch (InvalidOperationException)
                {
                    // Gone already.
                }

                _thread.Join(Patience);
                _ready.Dispose();
            }

            private void Run()
            {
                Control = new Control();
                Control.CreateControl();
                GC.KeepAlive(Control.Handle);
                _ready.Set();
                using (var context = new ApplicationContext())
                {
                    Application.Run(context);
                }
            }
        }

        /// <summary>
        /// An approval dialog that shows nowhere and is answered after a moment, and that a lock
        /// closes as it closes the real one.
        /// </summary>
        private sealed class ThinkingDialog : ApprovalDialog
        {
            private readonly System.Windows.Forms.Timer _timer;

            public ThinkingDialog(int thinkMilliseconds)
            {
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-8000, -8000);
                Size = new Size(1, 1);
                FormBorderStyle = FormBorderStyle.None;
                Opacity = 0;
                _timer = new System.Windows.Forms.Timer { Interval = thinkMilliseconds };
                _timer.Tick += delegate
                {
                    _timer.Stop();
                    Answered = true;
                    Close();
                };
                Shown += delegate { _timer.Start(); };
            }

            public bool Answered { get; private set; }

            internal override void Withdraw()
            {
                _timer.Stop();
                Close();
            }

            protected override void RefuseByKey()
            {
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _timer.Dispose();
                }

                base.Dispose(disposing);
            }
        }

        /// <summary>Every renewal and revocation that landed, in the order it did.</summary>
        private sealed class OrderedAudit : IGateAudit
        {
            private readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();

            public IEnumerable<string> Lines
            {
                get { return _lines; }
            }

            public void Note(string line)
            {
                _lines.Enqueue(line);
            }

            public void Record(AuditEntry entry)
            {
                if (entry != null && entry.OperationId == "renewCertificate" && entry.Decision == AuditEntry.Approved)
                {
                    _lines.Enqueue("renewed " + entry.AdmissionKey);
                }
            }
        }
    }
}
