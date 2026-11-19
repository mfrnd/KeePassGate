using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Acl;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Store;
using KeePassGate.Testing;
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
                Assert.That(incidents.Recent.Single(), Does.Contain("did not run to its end"));
            }
        }

        [Test]
        public void A_store_write_the_writer_refuses_at_OK_is_logged_and_counts_no_incident()
        {
            // As the writer's guard refuses a write where KeePass is part way through something of
            // its own: an answer and no defect, so no incident, and the log names what was not
            // written.
            var incidents = new GateIncidents(_log);
            var factory = new AuthorityPanelFactory(
                new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents,
                new AdmissionAudit(NullGateAudit.Instance), new SessionRefusals());

            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                panel.CreateAuthority();
                _database.Records.RefuseWrites = "KeePass is part way through a save";

                form.DialogResult = DialogResult.OK;
                form.RaiseClosed();
            }

            Assert.That(incidents.Count, Is.EqualTo(0));
            Assert.That(
                _log.Warnings.Select(entry => entry.Message),
                Has.Some.Matches<string>(message => message.Contains("Create") && message.Contains("part way through a save")));
            Assert.That(StoreAuthorities.Served(_database.Store.Read()), Is.Null);
        }

        [Test]
        public void A_defect_naming_the_clients_at_OK_keeps_no_revocation_from_landing()
        {
            // The clients' certificates are named for the audit's lines before OK's write deletes
            // their admissions, and a failure there, in the same step as applying what the tab
            // held, dropped every revocation the clients page had already shown gone.
            var incidents = new GateIncidents(_log);
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(client);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents,
                    new AdmissionAudit(NullGateAudit.Instance), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.Revoke(key), Is.Null);

                    _database.Records.NextReadThrows = new NullReferenceException("a defect naming the clients");
                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(Admitted(key), Is.Null, "the revocation never landed");
                Assert.That(incidents.Recent, Has.Exactly(1).Contains(nameof(NullReferenceException)));
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
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            string databaseId;
            using (var reading = new AuthorityService(_database.Store, _log).Read())
            {
                databaseId = reading.Material.AuthorityDigest;
            }

            using (var issuer = TestAuthority.New())
            using (var paired = issuer.IssueClient("agent-fictitious", "reader"))
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                // A client that renewed, so the renewal's client key is not its certificate's own:
                // each line names a certificate and the client the revocation ended.
                var pairedWith = Admit(paired);
                var key = Admit(client, pairedWith);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                RevokeAndClose(factory, pairedWith, DialogResult.Cancel);

                Assert.That(audit.Entries, Is.Empty, "a Cancel took it away again");
                Assert.That(Admitted(key), Is.Not.Null);

                RevokeAndClose(factory, pairedWith, DialogResult.OK);

                Assert.That(audit.Entries.Select(entry => entry.AdmissionKey), Is.EquivalentTo(new[] { pairedWith, key }));
                var line = audit.Entries.Single(entry => entry.AdmissionKey == key);
                Assert.That(line.AdmissionKey, Is.EqualTo(key), "read before OK's write deleted the admission");
                Assert.That(line.ClientKey, Is.EqualTo(pairedWith));
                Assert.That(Admitted(key), Is.Null, "and the client is revoked all the same");
                Assert.That(line.OperationId, Is.Null, "no route asked for it");
                Assert.That(line.Decision, Is.EqualTo(AuditEntry.Revoked));
                Assert.That(line.Reason, Is.EqualTo(AdmissionAudit.RevokedByOperatorReason));
                Assert.That(line.DatabaseId, Is.EqualTo(databaseId), "named as the gate names the database");
                Assert.That(line.Line(), Does.Not.Contain("reader"), "never a profile");
            }
        }

        [Test]
        public void A_revocation_writes_a_line_for_each_certificate_of_the_client_and_the_client_key_alone_where_the_copy_holds_none()
        {
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var first = issuer.IssueClient("agent-fictitious", "reader"))
            using (var renewed = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var client = Admit(first);
                var second = Admit(renewed, client);
                var elsewhere = new string('e', 64);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.Revoke(client), Is.Null);
                    Assert.That(panel.Revoke(elsewhere), Is.Null, "a client this copy holds no certificate of");

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(
                    audit.Entries.Select(line => (line.AdmissionKey, line.ClientKey)),
                    Is.EquivalentTo(new[] { (client, client), (second, client), (null, elsewhere) }));
            }
        }

        [Test]
        public void A_revocation_writes_lines_only_for_the_certificates_this_copy_admitted()
        {
            // The client holds a certificate that admits, a certificate under an authority that is
            // not served, and a certificate a second client's admission names as well. Only the
            // first admitted anybody before the OK, so only the first was ended by it.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var admitted = issuer.IssueClient("agent-fictitious", "reader"))
            using (var elsewhere = issuer.IssueClient("agent-fictitious", "reader"))
            using (var twice = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var client = Admit(admitted);
                StoreSeeds.Client(_database.Store, AdmissionRecord.For(
                    elsewhere, DateTimeOffset.UtcNow, new[] { "reader" }, new string('b', 64), client, client).Members());
                var served = StoreSeeds.Served(_database.Store);
                StoreSeeds.Client(_database.Store, AdmissionRecord.For(
                    twice, DateTimeOffset.UtcNow, new[] { "reader" }, served, client, client).Members());
                StoreSeeds.Client(_database.Store, AdmissionRecord.For(
                    twice, DateTimeOffset.UtcNow, new[] { "reader" }, served, new string('d', 64), null).Members());
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                RevokeAndClose(factory, client, DialogResult.OK);

                Assert.That(
                    audit.Entries.Select(line => (line.AdmissionKey, line.ClientKey)),
                    Is.EqualTo(new[] { (client, client) }));
            }
        }

        [Test]
        public void A_revocation_s_lines_are_written_once_the_store_holds_it_whatever_the_custom_data_half_comes_to()
        {
            // The store's write lands the revocation first, and the custom data half of the OK runs
            // after it, where a defect reaches the dialog close's catch-all. The lines are written
            // in between, so a revocation that stands is never left without them.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var certificate = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(certificate);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.Revoke(key), Is.Null);
                    _database.NextReadThrows = new NullReferenceException("a defect in the custom data half");

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assume.That(_database.NextReadThrows, Is.Null, "the custom data half was reached, and threw");
                Assert.That(Admitted(key), Is.Null, "the revocation is in the store");
                Assert.That(audit.Entries.Select(line => (line.AdmissionKey, line.ClientKey)), Is.EqualTo(new[] { (key, key) }));
            }
        }

        [Test]
        public void A_defect_writing_a_revocation_s_lines_loses_nothing_else_of_the_OK()
        {
            // The lines are written once the store holds the revocation and before the custom data
            // half, and a defect in writing them reaches the dialog close's catch-all once that
            // half has run: a profile added on the same OK is on the list all the same.
            var incidents = new GateIncidents(_log);
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var certificate = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(certificate);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents,
                    new AdmissionAudit(new ThrowingAudit()), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    var profiles = ControlTree.FindByName<ProfileList>(panel, ProfileList.ControlName);
                    profiles.Add("deploy");
                    Assume.That(profiles.Shown, Has.Member("deploy"), "the profile waits for OK");
                    Assert.That(panel.Revoke(key), Is.Null);

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(Admitted(key), Is.Null, "the revocation is in the store");
                Assert.That(Named("deploy"), Is.True, "and the profile added on the same OK is on the list");
                Assert.That(incidents.Recent, Has.Exactly(1).Contains(nameof(NullReferenceException)), "the defect reached the catch-all");
                Assert.That(incidents.Recent.Single(), Does.StartWith("the OK of the " + GateName.Product + " tab did not run to its end"),
                    "true of an OK whose store's part was written");
                Assert.That(
                    _log.Entries.Where(entry => entry.Level == GateLogFormat.InfoLevel).Select(entry => entry.Message),
                    Has.Some.Contains("the plugin's store holds what the " + GateName.Product + " tab held").And.Contains(CertificateDigest.Shorten(key)),
                    "and a line telling what landed is written before the lines that threw");
            }
        }

        [Test]
        public void A_defect_writing_a_revocation_s_lines_keeps_no_answer_lifted_for_the_session_standing()
        {
            // The steps after the database's half run whatever the lines came to, as that half
            // does: an answer lifted for this session on the same OK is lifted, and its line is in
            // the log, and the defect reaches the dialog close's catch-all once they have run.
            var incidents = new GateIncidents(_log);
            var session = new SessionRefusals();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var revoked = issuer.IssueClient("agent-fictitious", "reader"))
            using (var refused = issuer.IssueClient("agent-invented", "reader"))
            {
                var key = Admit(revoked);
                var other = Admit(refused);
                session.Refuse(other);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents,
                    new AdmissionAudit(new ThrowingAudit()), session);

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.AllowRenewal(other), Is.Null);
                    Assert.That(panel.Revoke(key), Is.Null);

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(Admitted(key), Is.Null, "the revocation is in the store");
                Assert.That(session.Refuses(other), Is.False, "the answer for this session is lifted");
                Assert.That(
                    _log.Entries.Select(entry => entry.Message),
                    Has.Some.Contains("renewals of " + CertificateDigest.Shorten(other)).And.Contains("for this session"));
                Assert.That(incidents.Recent, Has.Exactly(1).Contains(nameof(NullReferenceException)), "the defect reached the catch-all");
            }
        }

        [Test]
        public void A_certificate_taken_away_is_written_down_when_the_dialogs_OK_lands_it_and_never_on_Cancel()
        {
            // The line of a certificate the operator took away: the certificate, the client its
            // admission named, and the operator's revocation as the reason.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            string databaseId;
            using (var reading = new AuthorityService(_database.Store, _log).Read())
            {
                databaseId = reading.Material.AuthorityDigest;
            }

            using (var issuer = TestAuthority.New())
            using (var paired = issuer.IssueClient("agent-fictitious", "reader"))
            using (var renewed = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var first = Admit(paired);
                var second = Admit(renewed, first);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                TakeAwayAndClose(factory, first, DialogResult.Cancel);

                Assert.That(audit.Entries, Is.Empty, "a Cancel took it away again");
                Assert.That(Admitted(first), Is.Not.Null);

                TakeAwayAndClose(factory, first, DialogResult.OK);

                var line = audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(first));
                Assert.That(line.ClientKey, Is.EqualTo(first));
                Assert.That(line.Decision, Is.EqualTo(AuditEntry.Revoked));
                Assert.That(line.Reason, Is.EqualTo(AdmissionAudit.RevokedByOperatorReason));
                Assert.That(line.DatabaseId, Is.EqualTo(databaseId));
                Assert.That(Admitted(first), Is.Null, "the certificate is taken away");
                Assert.That(Admitted(second), Is.Not.Null, "and the client is not revoked");
            }
        }

        [Test]
        public void A_defect_writing_a_taken_certificate_s_line_loses_nothing_else_of_the_OK()
        {
            var incidents = new GateIncidents(_log);
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var certificate = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(certificate);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, incidents,
                    new AdmissionAudit(new ThrowingAudit()), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    ControlTree.FindByName<ProfileList>(panel, ProfileList.ControlName).Add("deploy");
                    Assert.That(panel.TakeAway(key), Is.Null);

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(Admitted(key), Is.Null, "the certificate is taken away");
                Assert.That(Named("deploy"), Is.True, "and the profile added on the same OK is on the list");
                Assert.That(incidents.Recent, Has.Exactly(1).Contains(nameof(NullReferenceException)));
            }
        }

        [Test]
        public void A_certificate_taken_away_and_its_client_revoked_on_one_OK_is_written_down_once()
        {
            // The revocation ends the certificate as well and writes its line; the certificate
            // taken away beside it writes no second line.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var paired = issuer.IssueClient("agent-fictitious", "reader"))
            using (var renewed = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var first = Admit(paired);
                var second = Admit(renewed, first);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.TakeAway(first), Is.Null);
                    Assert.That(panel.Revoke(first), Is.Null);

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(audit.Entries.Select(line => line.AdmissionKey), Is.EquivalentTo(new[] { first, second }));
                Assert.That(audit.Entries.Select(line => line.ClientKey), Is.All.EqualTo(first));
            }
        }

        [TestCase("listed twice")]
        [TestCase("not read")]
        public void A_certificate_that_admitted_nobody_before_OK_is_written_down_once_with_no_client(string how)
        {
            // The operator's act is on record, and no client is named as having held what
            // admitted nobody.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var certificate = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = CertificateDigest.Of(certificate);
                var members = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "reader" }, Served(), key, null)
                    .Members().ToDictionary(member => member.Key, member => member.Value);
                if (how == "listed twice")
                {
                    Admit(certificate);
                    members["client"] = new string('e', 64);
                }
                else
                {
                    members["der"] = "not base64";
                }

                StoreSeeds.Client(_database.Store, members);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                TakeAwayAndClose(factory, key, DialogResult.OK);

                var line = audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(key));
                Assert.That(line.ClientKey, Is.Null);
            }
        }

        [Test]
        public void A_certificate_its_client_gave_up_before_OK_writes_nothing_at_OK_and_no_line()
        {
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var certificate = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(certificate);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.TakeAway(key), Is.Null);
                    StoreSeeds.TakeAway(_database.Store, key);

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(audit.Entries, Is.Empty);
                using (var reading = _database.Store.Read())
                {
                    Assert.That(reading.InForce.StatementsOf(key), Has.Count.EqualTo(1), "the statement its client wrote, and none of the OK's");
                }

                Assert.That(
                    _log.Entries.Where(entry => entry.Level == GateLogFormat.InfoLevel).Select(entry => entry.Message),
                    Has.Some.Contains(CertificateDigest.Shorten(key)).And.Contains("nothing was left to write"),
                    "the press that came to nothing is in the log");
            }
        }

        [Test]
        public void A_certificate_taken_away_with_the_store_unreadable_before_OK_is_written_down_with_no_client()
        {
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var certificate = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(certificate);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                using (var form = Dialogs.WithNestedTabControl("General"))
                using (var panel = (AuthorityPanel)factory.CreatePanel(form))
                {
                    Assert.That(panel.TakeAway(key), Is.Null);
                    _database.Records.NextReadThrows = new InvalidOperationException("a store that will not read");

                    form.DialogResult = DialogResult.OK;
                    form.RaiseClosed();
                }

                Assert.That(Admitted(key), Is.Null, "taken away all the same");
                var line = audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.EqualTo(key));
                Assert.That(line.ClientKey, Is.Null);
            }
        }

        /// <summary>The authority the database serves now.</summary>
        private string Served()
        {
            using (var reading = _database.Store.Read())
            {
                return StoreAuthorities.Served(reading);
            }
        }

        [Test]
        public void A_revocation_in_a_database_serving_no_authority_names_no_database()
        {
            // The line names the authority served once OK is through, and where two copies each
            // made an authority there is none. Nothing was admitted under no authority, so the
            // line names the client key alone.
            var audit = new RecordingAudit();
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var key = Admit(client);
                StoreSeeds.Beside(_database.Store);
                var factory = new AuthorityPanelFactory(
                    new FakeDatabaseSource(_database), new FakePrompt(true), _log, null, null, null, new AdmissionAudit(audit), new SessionRefusals());

                RevokeAndClose(factory, key, DialogResult.OK);

                var line = audit.Entries.Single();
                Assert.That(line.AdmissionKey, Is.Null);
                Assert.That(line.ClientKey, Is.EqualTo(key), "a client that never renewed is its own client key");
                Assert.That(line.DatabaseId, Is.Null);
            }
        }

        /// <summary>
        /// Takes a certificate away on a fresh tab and closes its dialog with the answer given.
        /// </summary>
        private static void TakeAwayAndClose(AuthorityPanelFactory factory, string admissionKey, DialogResult result)
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                Assert.That(panel.TakeAway(admissionKey), Is.Null);

                form.DialogResult = result;
                form.RaiseClosed();
            }
        }

        /// <summary>
        /// Presses revoke on a fresh tab and closes its dialog with the answer given.
        /// </summary>
        private static void RevokeAndClose(AuthorityPanelFactory factory, string clientKey, DialogResult result)
        {
            using (var form = Dialogs.WithNestedTabControl("General"))
            using (var panel = (AuthorityPanel)factory.CreatePanel(form))
            {
                Assert.That(panel.Revoke(clientKey), Is.Null);

                form.DialogResult = result;
                form.RaiseClosed();
            }
        }

        /// <summary>
        /// Admits a certificate under the authority the database serves, as a pairing does, or as
        /// a renewal of the client given; answers its admission key.
        /// </summary>
        private string Admit(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, string client = null)
        {
            string served;
            using (var reading = _database.Store.Read())
            {
                served = StoreAuthorities.Served(reading);
            }

            var key = CertificateDigest.Of(certificate);
            StoreSeeds.Admit(_database.Store, AdmissionRecord.For(
                certificate, DateTimeOffset.UtcNow, new[] { "reader" }, served, client ?? key, client));
            return key;
        }

        /// <summary>Whether a profile name is on the list of the database's store now.</summary>
        private bool Named(string profile)
        {
            using (var reading = _database.Records.Read())
            {
                return ProfileNames.Of(reading).Holds(profile);
            }
        }

        /// <summary>The admission of a certificate as the gate reads it now.</summary>
        private AdmissionRecord Admitted(string key)
        {
            using (var reading = _database.Store.Read())
            {
                return StoreAdmissions.Admitted(reading, key);
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
            Assume.That(new AuthorityService(_database.Store, _log).Create().Succeeded, Is.True);

            using (var issuer = TestAuthority.New())
            using (var client = issuer.IssueClient("agent-fictitious", "reader"))
            {
                var store = new RenewalAnswers(_database.PluginCustomData, _log);
                var key = Admit(client);

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
                Assert.That(Admitted(key), Is.Not.Null, "and the client is still admitted");
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

        private static bool Stands(RenewalAnswers store, SessionRefusals session, string key)
        {
            return store.StandingRefusal(key) != null || session.Refuses(key);
        }

        /// <summary>An audit with a defect: every line it is given throws.</summary>
        private sealed class ThrowingAudit : IGateAudit
        {
            public bool Keeps
            {
                get { return true; }
            }

            public void Record(AuditEntry entry)
            {
                throw new NullReferenceException("a defect writing an audit line");
            }
        }
    }
}
