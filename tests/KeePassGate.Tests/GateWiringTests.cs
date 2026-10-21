using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The whole plugin except the two classes that cannot exist without KeePass: real host, real
    /// injector, real panel, with only the window notifications faked. This is the closest a
    /// hermetic test gets to what a user sees, and it fails if any of the pieces stop fitting.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class GateWiringTests
    {
        private FakeWindowEventSource _windows;
        private RecordingGateLog _log;
        private TabPageInjector _injector;
        private FakeGateDatabase _database;
        private GateHost _host;

        [SetUp]
        public void SetUp()
        {
            _windows = new FakeWindowEventSource();
            _log = new RecordingGateLog();
            _database = new FakeGateDatabase();
            _injector = new TabPageInjector(
                new AuthorityPanelFactory(new FakeDatabaseSource(_database), new FakePrompt(false), _log, null),
                _log);
            _host = new GateHost(_windows, new TypeNameFormMatcher(typeof(TestDialogForm).FullName), _injector, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
        }

        [Test]
        public void A_settings_dialog_opening_gains_a_KPG_tab_showing_the_authority()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General", "Security"))
            {
                _windows.RaiseWindowAdded(form);

                var page = ControlTree.FindByName<TabPage>(form, GateTab.PageControlName);
                Assert.That(page, Is.Not.Null);
                Assert.That(page.Text, Is.EqualTo(GateTab.Text));

                var status = ControlTree.FindByName<Label>(form, GateTab.StatusLabelName);
                Assert.That(status.Text, Is.EqualTo(AuthorityDescription.AbsentHeadline));
            }
        }

        [Test]
        public void The_dialog_closing_takes_the_tab_with_it()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _windows.RaiseWindowAdded(form);
                _windows.RaiseWindowRemoved(form);

                Assert.That(ControlTree.FindByName<TabPage>(form, GateTab.PageControlName), Is.Null);
                Assert.That(Dialogs.TabsOf(form).TabPages.Cast<TabPage>().Select(p => p.Text),
                    Is.EqualTo(new[] { "General" }));
            }
        }

        [Test]
        public void Stopping_with_a_dialog_still_open_takes_the_tab_back()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _windows.RaiseWindowAdded(form);

                _host.Stop();

                Assert.That(ControlTree.FindByName<TabPage>(form, GateTab.PageControlName), Is.Null);
                Assert.That(_injector.AttachedCount, Is.Zero);
            }
        }

        [Test]
        public void Nothing_in_the_whole_run_was_reported_as_a_failure()
        {
            _host.Start();

            using (var form = Dialogs.WithNestedTabControl("General"))
            {
                _windows.RaiseWindowAdded(form);
                _windows.RaiseWindowRemoved(form);
            }

            _host.Stop();

            Assert.That(_log.Errors, Is.Empty);
            Assert.That(_log.Warnings, Is.Empty);
        }

        [Test]
        public void A_failure_applying_the_tab_on_OK_is_counted_where_the_Tools_window_reads_it()
        {
            // H042: the handler applying what the tab held is one of KeePass's window event
            // handlers that catch everything, and it logged at the level that means a defect
            // without counting one, so the Tools window's incidents never showed it.
            var incidents = new GateIncidents(_log);
            var factory = new AuthorityPanelFactory(
                new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents,
                new AdmissionAudit(NullGateAudit.Instance), new SessionRefusals());

            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                panel.CreateAuthority();
                _database.MarkThrows = new InvalidOperationException("invented for the test");

                form.DialogResult = DialogResult.OK;
                typeof(Form).GetMethod(
                        "OnFormClosed",
                        BindingFlags.NonPublic | BindingFlags.Instance,
                        null,
                        new[] { typeof(FormClosedEventArgs) },
                        null)
                    .Invoke(form, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });

                Assert.That(incidents.Count, Is.EqualTo(1), "the failure was not counted");
                Assert.That(incidents.Recent.Single(), Does.Contain("could not be written"));
            }
        }

        [Test]
        public void The_factory_the_plugin_builds_cannot_be_given_no_audit()
        {
            // A factory given none wrote nothing down and said nothing, so whether the operator's
            // revocations reached the file was one argument in the wiring that nothing read. The
            // plugin's own load builds it with this constructor, so a null there stops the load.
            Assert.Throws<ArgumentNullException>(() => new AuthorityPanelFactory(
                new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, null, new SessionRefusals()));
        }

        [Test]
        public void A_revocation_is_written_down_when_the_dialogs_OK_lands_it_and_never_on_Cancel()
        {
            // The moment it takes effect, which is OK: until then it waits in the dialog's buffer
            // with everything else on the tab, and Cancel takes it away with nothing written.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.PluginCustomData, _log).Create().Succeeded, Is.True);

            string databaseId;
            using (var reading = new AuthorityService(_database.PluginCustomData, _log).Read())
            {
                databaseId = reading.Material.AuthorityDigest;
            }

            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                // A client that renewed, so the record's client key is not this certificate's own:
                // the line names the one on the record the revocation empties.
                var pairedWith = new string('c', 64);
                new AdmissionStore(_database.PluginCustomData, _log)
                    .Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "reader" }, pairedWith));
                var key = CertificateDigest.Of(client);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                RevokeAndClose(factory, key, DialogResult.Cancel);

                Assert.That(audit.Entries, Is.Empty, "a Cancel took it away again");

                RevokeAndClose(factory, key, DialogResult.OK);

                var line = audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(key));
                Assert.That(line.ClientKey, Is.EqualTo(pairedWith), "read before OK emptied the record");
                Assert.That(new AdmissionStore(_database.PluginCustomData, _log).Find(key), Is.Null,
                    "and the record is emptied all the same");
                Assert.That(line.OperationId, Is.Null, "no route asked for it");
                Assert.That(line.Decision, Is.EqualTo(AuditEntry.Revoked));
                Assert.That(line.Reason, Is.EqualTo(AdmissionAudit.RevokedByOperatorReason));
                Assert.That(line.DatabaseId, Is.EqualTo(databaseId), "named as the gate names the database");
                Assert.That(line.Line(), Does.Not.Contain("reader"), "never a profile");
            }
        }

        [Test]
        public void A_revocation_in_a_database_serving_no_authority_names_no_database()
        {
            // The line names the authority served once OK is through, and here there is none.
            var audit = new RecordingAudit();

            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                new AdmissionStore(_database.PluginCustomData, _log)
                    .Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "reader" }));
                var key = CertificateDigest.Of(client);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                RevokeAndClose(factory, key, DialogResult.OK);

                var line = audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(key));
                Assert.That(line.ClientKey, Is.EqualTo(key), "a client that never renewed is its own client key");
                Assert.That(line.DatabaseId, Is.Null);
            }
        }

        /// <summary>Presses revoke on a fresh tab and closes its dialog with the answer given.</summary>
        private static void RevokeAndClose(AuthorityPanelFactory factory, string key, DialogResult result)
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                Assert.That(panel.Revoke(key), Is.Null);

                form.DialogResult = result;
                form.RaiseClosed();
            }
        }

        [TestCase("forever")]
        [TestCase("until")]
        [TestCase("this session")]
        public void Allow_renewal_lifts_each_kind_of_answer_on_OK_and_leaves_it_on_Cancel(string kind)
        {
            // M001: Allow renewal on the clients page lifts whichever answer stands, and waits for
            // the dialog's OK as a revocation does. The stored two are the database's, this
            // session's is the plugin's memory, and the gate reads both.
            var session = new SessionRefusals();

            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var store = new AdmissionStore(_database.PluginCustomData, _log);
                store.Write(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "reader" }));
                var key = CertificateDigest.Of(client);

                switch (kind)
                {
                    case "forever":
                        store.RefuseRenewal(key, RenewalRefusal.Forever());
                        break;
                    case "until":
                        store.RefuseRenewal(key, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(7)));
                        break;
                    default:
                        session.Refuse(key);
                        break;
                }

                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null,
                    new AdmissionAudit(NullGateAudit.Instance), session);

                AllowAndClose(factory, key, DialogResult.Cancel);
                Assert.That(Stands(store, session, key), Is.True, "Cancel took the press away");

                AllowAndClose(factory, key, DialogResult.OK);
                Assert.That(Stands(store, session, key), Is.False, "OK lifted it");
                Assert.That(store.Find(key), Is.Not.Null, "and the client is still admitted");
            }
        }

        /// <summary>Presses Allow renewal on a fresh tab and closes its dialog with the answer given.</summary>
        private static void AllowAndClose(AuthorityPanelFactory factory, string key, DialogResult result)
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                var list = ControlTree.FindByName<ClientList>(panel, ClientList.ControlName);
                Assert.That(list.StandingAt(0), Is.Not.Null, "the page shows the answer");

                Assert.That(panel.AllowRenewal(key), Is.Null);
                panel.Describe();
                Assert.That(list.StandingAt(0), Is.Null, "and stops showing it at once");

                form.DialogResult = result;
                form.RaiseClosed();
            }
        }

        private static bool Stands(AdmissionStore store, SessionRefusals session, string key)
        {
            return store.StandingRefusal(key) != null || session.Refuses(key);
        }
    }
}
