using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using KeePass.Forms;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The OK of a group dialog's KPG tab pressed while a save of the plugin's own pumps under the
    /// dialog, over a real database and the real writer: the rule is written once that save has
    /// returned, and the writer's guard against a write inside a save never meets it.
    /// </summary>
    /// <remarks>
    /// **Over a plain modal form carrying the tab of KeePass's own group dialog**, which cannot be
    /// shown in this process, `KeePassDialogs.Close`. What decides where the close is raised is the
    /// form's own modal loop, which a plain form has as KeePass's dialog has, and
    /// `ModalDialogCloseTests` holds that fact alone. A write made at the press would be made
    /// inside the save, refused by the writer's guard, and the save would clear the mark the write
    /// set; made at the close, the write comes once the save has returned.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class NodeDialogOkBesideSaveTests
    {
        [Test]
        public void An_OK_pressed_inside_a_pairing_s_save_is_written_once_that_save_has_returned()
        {
            using (var database = TemporaryDatabase.New())
            using (var dialog = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 })
            using (var window = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Left = -2000, Top = -2000 })
            using (var ok = new Button { DialogResult = DialogResult.OK })
            using (var guard = new System.Windows.Forms.Timer { Interval = 30000 })
            using (var group = new GroupForm())
            {
                StoreSeeds.Authority(DatabaseAuthority.Store(database.Database));
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;
                window.Show();

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
                    // KeePass's own answer while it saves: the writer's guard asks it.
                    () => saving ? "KeePass is saving a database" : null,
                    null,
                    null,
                    null,
                    NullGateLog.Instance);

                group.InitEx(database.RootGroup, false, null, database.Database);
                var tab = new NodeDialogs(NullGateLog.Instance, writer).TabFor(group);
                Assume.That(tab.Sheet.CanEdit, Is.True, tab.Sheet.ReadOnlyReason);

                var prompt = new Counting();
                var factory = new GrantPanelFactory(new TheTab(tab), NullGateLog.Instance, null, prompt, window, null);

                using (var paired = Issue(database))
                using (var panel = (GrantPanel)factory.CreatePanel(dialog))
                {
                    var served = StoreSeeds.Served(DatabaseAuthority.Store(database.Database));
                    var pairing = AdmissionRecord.For(paired, DateTimeOffset.UtcNow, new[] { "ops" }, served);
                    Assume.That(panel.SetGrant("ops", ".R...L.."), Is.Null, "the rule waits on the tab for OK");

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
                    for (var i = 0; i < 8; i++)
                    {
                        Application.DoEvents();
                    }

                    Assert.That(result, Is.EqualTo(DialogResult.OK));
                    Assert.That(written, Is.Not.Null, "the pairing's write ran under the dialog");
                    Assert.That(pressedInside, Is.True, "the OK was pressed while the save ran");
                    Assert.That(written.Ok, Is.True, written.Reason);
                    Assert.That(saves, Is.EqualTo(1));
                    Assert.That(prompt.Told, Is.Zero, "the writer's guard refused nothing of the OK");
                    Assert.That(database.Database.Modified, Is.True, "the rule's write marked the database after the save");

                    using (var reading = new KdbxStore(database.Database, GateVersion.Current, null).Read())
                    {
                        var row = StoredRules.Of(reading).At(Hex.Of(database.RootGroup.Uuid.UuidBytes)).Row("ops");
                        Assert.That(VerbMask.Of(row.Grants), Is.EqualTo(".R...L.."), "the rule is in the store");
                    }
                }
            }
        }

        private static X509Certificate2 Issue(TemporaryDatabase database)
        {
            using (var reading = DatabaseAuthority.Of(database.Database, null).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, "agent-paired", new[] { "ops" }).Read();
            }
        }

        /// <summary>A source handing every dialog the tab KeePass's dialog gave.</summary>
        private sealed class TheTab : INodeDialogSource
        {
            private readonly NodeTab _tab;

            public TheTab(NodeTab tab)
            {
                _tab = tab;
            }

            public NodeTab TabFor(Form dialog)
            {
                return _tab;
            }
        }

        /// <summary>An operator told things, counted.</summary>
        private sealed class Counting : IOperatorPrompt
        {
            public int Told { get; private set; }

            public bool Confirm(string title, string question)
            {
                return false;
            }

            public void Tell(string title, string news)
            {
                Told++;
            }
        }
    }
}
