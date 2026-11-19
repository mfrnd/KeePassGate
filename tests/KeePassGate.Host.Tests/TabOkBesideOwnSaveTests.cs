using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The KPG tab's OK pressed while a save of the plugin's own pumps under the dialog, over a
    /// real database and the real writer: what the OK does is made once that save has returned,
    /// and the writer's guard against a write inside a save never meets it.
    /// </summary>
    /// <remarks>
    /// **Over a plain modal form, and not KeePass's own settings dialog**, which cannot be shown in
    /// this process: shown outside a running KeePass it throws inside the message pump, and
    /// WinForms answers with a dialog of its own, `KeePassDialogs.Close`. What decides where the
    /// close is raised is the form's own modal loop, which a plain form has as KeePass's dialog
    /// has, and `ModalDialogCloseTests` holds that fact alone. The tab is attached to the form by
    /// the factory as it is to KeePass's dialog, over a source that answers the database.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class TabOkBesideOwnSaveTests
    {
        [Test]
        public void The_tab_s_OK_pressed_inside_a_pairing_s_save_is_made_once_that_save_has_returned()
        {
            using (var database = TemporaryDatabase.New())
            using (var dialog = new Form
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Left = -2000,
                Top = -2000
            })
            using (var ok = new Button { DialogResult = DialogResult.OK })
            using (var guard = new System.Windows.Forms.Timer { Interval = 30000 })
            {
                var created = DatabaseAuthority.Of(database.Database, null).Create();
                Assume.That(created.Succeeded, Is.True, created.Message);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;

                var log = new WarningsLog();
                var saving = false;
                var saves = 0;
                var pressedInside = false;
                ok.Click += (sender, e) => pressedInside |= saving;
                var writer = new DatabaseWriter(
                    CurrentThread.Instance,
                    saved =>
                    {
                        // The save of a pairing, pumping as KeePass's save does, with the dialog's
                        // OK pressed while it runs.
                        saving = true;
                        try
                        {
                            dialog.BeginInvoke(new Action(ok.PerformClick));
                            Application.DoEvents();
                            Application.DoEvents();
                            saved.Save(null);
                            saves++;
                        }
                        finally
                        {
                            saving = false;
                        }
                    },
                    // KeePass's own answer while it saves, which blocks its interaction for the
                    // length of the save: the writer's guard asks it.
                    () => saving ? "KeePass is saving a database" : null,
                    null,
                    null,
                    null,
                    log);

                var lines = new LinesAudit(() => saving);
                var factory = new AuthorityPanelFactory(
                    new TheDatabase(new OpenDatabase(database.Database, null, writer, log)),
                    new Agreeing(),
                    log,
                    null,
                    null,
                    null,
                    new AdmissionAudit(lines),
                    new SessionRefusals());

                using (var revoked = Issue(database, "agent-revoked"))
                using (var paired = Issue(database, "agent-paired"))
                {
                    var served = StoreSeeds.Served(new StoredAdmissions(database.Database).Store);
                    var revokedKey = CertificateDigest.Of(revoked);
                    new StoredAdmissions(database.Database).Write(AdmissionRecord.For(revoked, DateTimeOffset.UtcNow, new[] { "ops" }, served));
                    var pairing = AdmissionRecord.For(paired, DateTimeOffset.UtcNow, new[] { "ops" }, served);

                    using (var panel = (AuthorityPanel)factory.CreatePanel(dialog))
                    {
                        Assume.That(panel.Revoke(revokedKey), Is.Null, "the revocation waits on the tab for OK");

                        guard.Tick += (sender, e) =>
                        {
                            guard.Stop();
                            dialog.DialogResult = DialogResult.Abort;
                        };
                        guard.Start();

                        AdmissionWrite written = null;
                        EventHandler idle = null;
                        idle = (sender, e) =>
                        {
                            Application.Idle -= idle;
                            written = writer.WriteAdmission(database.Database, pairing);
                        };
                        Application.Idle += idle;

                        var result = dialog.ShowDialog();

                        Assert.That(result, Is.EqualTo(DialogResult.OK));
                        Assert.That(written, Is.Not.Null, "the pairing's write ran under the dialog");
                        Assert.That(pressedInside, Is.True, "the OK was pressed while the save ran");
                        Assert.That(lines.Recorded, Is.EqualTo(new[] { false }),
                            "the revocation's line is written once, after the save returned");
                        Assert.That(log.Warnings, Has.None.Contains("was not written"),
                            "the writer's guard refused nothing of the OK");
                        Assert.That(written.Ok, Is.True, written.Reason);
                        Assert.That(saves, Is.EqualTo(1));

                        var admissions = new StoredAdmissions(database.Database);
                        Assert.That(admissions.Holds("revoked", revokedKey), Is.True, "the operator's revocation is in the store");
                        Assert.That(admissions.Holds("client", pairing.AdmissionKey), Is.True, "beside the pairing's admission");
                    }
                }
            }
        }

        private static X509Certificate2 Issue(TemporaryDatabase database, string name)
        {
            using (var reading = DatabaseAuthority.Of(database.Database, null).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, new[] { "ops" }).Read();
            }
        }

        /// <summary>Answers the same database whatever the dialog.</summary>
        private sealed class TheDatabase : IGateDatabaseSource
        {
            private readonly IGateDatabase _database;

            public TheDatabase(IGateDatabase database)
            {
                _database = database;
            }

            public IGateDatabase ForDialog(Form dialog)
            {
                return _database;
            }
        }

        /// <summary>An operator who agrees to every question.</summary>
        private sealed class Agreeing : IOperatorPrompt
        {
            public bool Confirm(string title, string question)
            {
                return true;
            }

            public void Tell(string title, string news)
            {
            }
        }

        /// <summary>Whether a save was running as each line was recorded.</summary>
        private sealed class LinesAudit : IGateAudit
        {
            private readonly Func<bool> _saving;

            public LinesAudit(Func<bool> saving)
            {
                _saving = saving;
            }

            public List<bool> Recorded { get; } = new List<bool>();

            public bool Keeps
            {
                get { return true; }
            }

            public void Record(AuditEntry entry)
            {
                Recorded.Add(_saving());
            }
        }

        /// <summary>The warnings logged, every other line dropped.</summary>
        private sealed class WarningsLog : IGateLog
        {
            public List<string> Warnings { get; } = new List<string>();

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
                Warnings.Add(message);
            }

            public void Error(string message, Exception error)
            {
                Warnings.Add(message);
            }

            public void Unexpected(string message, Exception error)
            {
                Warnings.Add(message);
            }
        }
    }
}
