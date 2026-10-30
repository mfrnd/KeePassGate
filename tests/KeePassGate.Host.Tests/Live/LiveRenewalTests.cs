using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Transport;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for renewal: an admitted client asks a real KeePass for a new
    /// certificate, a real dialog is approved by a real button press, the new certificate opens a
    /// route on the next connection, and the old one is refused once the client has given it up.
    /// </summary>
    /// <remarks>
    /// The old record is left valid by the renewal on purpose, so the client revokes it itself,
    /// as the reference client does, presenting the old certificate. What this establishes that
    /// nothing smaller does: that the new record reaches KeePass's own save before the answer, that
    /// the bundle it answers with is one a client can connect with, and that the file holds the new
    /// record and not the old once KeePass has gone.
    ///
    /// **And a refusal chosen from the Refuse button's menu**, in KeePass's own modal loop, which
    /// is where a menu on a dialog either owns its clicks and its keys or does not. The new
    /// certificate asks to renew in turn and is refused in this session, by a press on the arrow
    /// and a press on the choice; asking again is then answered `409 this session` with nobody
    /// asked, and the file holds no answer of it once KeePass has gone.
    ///
    /// **And that answer lifted from the clients page**, as the operator does it: the row's menu
    /// opened with Shift and F10, Allow renewal pressed, and the settings dialog accepted. The next
    /// renewal of that certificate then raises the dialog again, which is refused once.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LiveRenewalTests
    {
        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;

        private LiveReply _renewed;
        private LiveReply _withTheNew;
        private LiveReply _refusedInThisSession;
        private LiveReply _askedAgain;
        private string _menuSaid;
        private LiveReply _afterLifting;
        private LiveReply _givenUp;
        private LiveReply _withTheOld;
        private string _oldKey;
        private string _newKey;
        private bool _exited;
        private string _pluginLog;
        private string[] _savedBeforeTheAnswer;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            using (var old = keePass.IssueClientCertificate("agent-fictitious"))
            {
                keePass.Admit(old, "reader");
                _oldKey = CertificateDigest.Of(old);

                var driver = KeePassDriver.Start(keePass);
                LivePairingTests.WaitUntilServing(keePass, driver);

                _renewed = Approve(driver, keePass, old);
                Assert.That(_renewed.Status, Is.EqualTo(200), _renewed.Failure ?? _renewed.Body);

                // Read now, before the old certificate's revocation saves the whole database again
                // and would carry a record whose own save was skipped into the file regardless.
                _savedBeforeTheAnswer = keePass.ReadAdmissions().Select(record => record.AdmissionKey).ToArray();

                using (var renewed = Identity(_renewed.Body))
                {
                    _newKey = CertificateDigest.Of(renewed);
                    _withTheNew = LiveGateClient.Get(
                        keePass.Port, keePass.NewGateName(), renewed, GateOperations.StatusPath);

                    _refusedInThisSession = RefuseInThisSession(driver, keePass, renewed);
                    _askedAgain = LiveGateClient.Send(
                        keePass.Port, keePass.NewGateName(), renewed, "POST", RenewalHandler.Path, "{}");

                    _menuSaid = AllowRenewalOnTheClientsPage(driver);
                    _afterLifting = RefuseOnce(driver, keePass, renewed);
                }

                _givenUp = LiveGateClient.Send(
                    keePass.Port, keePass.NewGateName(), old, "DELETE",
                    GateOperations.PathOf(GateOperations.RevokeCertificate), null);
                _withTheOld = LiveGateClient.Get(
                    keePass.Port, keePass.NewGateName(), old, GateOperations.StatusPath);

                _exited = driver.ShutDown();
            }

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            if (_session != null)
            {
                _session.Dispose();
            }
        }

        [Test]
        public void The_renewed_certificate_opens_a_route_on_the_next_connection()
        {
            Assert.That(_withTheNew.Status, Is.EqualTo(200), _withTheNew.Failure ?? _withTheNew.Body);
        }

        [Test]
        public void A_renewal_refused_in_this_session_is_answered_without_asking_and_kept_nowhere()
        {
            Assert.That(_refusedInThisSession.Status, Is.EqualTo(403),
                _refusedInThisSession.Failure ?? _refusedInThisSession.Body);
            Assert.That(_askedAgain.Status, Is.EqualTo(409), _askedAgain.Failure ?? _askedAgain.Body);
            Assert.That(_askedAgain.Body, Does.Contain("\"this session\""));

            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");
            Assert.That(_session.KeePass.ReadStandingRefusal(_newKey), Is.Null,
                "an answer for this session reached the file");
        }

        [Test]
        public void An_answer_lifted_on_the_clients_page_lets_the_next_renewal_raise_the_dialog_again()
        {
            Assert.That(_menuSaid, Does.Contain("Renewal refused in this session"),
                "the row's menu did not state the answer it was about to lift");
            Assert.That(_afterLifting.Status, Is.EqualTo(403), _afterLifting.Failure ?? _afterLifting.Body);
        }

        [Test]
        public void The_old_certificate_is_refused_once_the_client_has_given_it_up()
        {
            Assert.That(_givenUp.Status, Is.EqualTo(200), _givenUp.Failure ?? _givenUp.Body);
            Assert.That(_withTheOld.Status, Is.EqualTo(401), "refused as a certificate the operator revoked is");
        }

        [Test]
        public void The_file_holds_the_new_record_and_not_the_old()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit cleanly");

            var admitted = _session.KeePass.ReadAdmissions().Select(record => record.AdmissionKey).ToArray();
            Assert.That(admitted, Has.Member(_newKey));
            Assert.That(admitted, Has.No.Member(_oldKey));
            Assert.That(_savedBeforeTheAnswer, Has.Member(_newKey),
                "the renewal's own save put the new record in the file before it answered");
        }

        [Test]
        public void The_audit_wrote_down_the_renewal_its_refusals_and_the_old_certificate_given_up()
        {
            // The production wiring: the renewal handler and the revocation both write through the
            // access check's own record of admissions, which only a running plugin reaches.
            var lines = File.ReadAllLines(_session.KeePass.AuditPath);

            // The client paired with the old certificate, so the old key is its client key on
            // every line, the new certificate's own requests included.
            Assert.That(
                lines.Count(line => line.Contains("\"operation\":\"" + GateOperations.RenewCertificate + "\"")
                    && line.Contains("\"decision\":\"" + AuditEntry.Approved + "\"")
                    && line.Contains(Keys(_oldKey, _oldKey))
                    && line.Contains("\"issued\":\"" + _newKey + "\"")),
                Is.EqualTo(1), string.Join(Environment.NewLine, lines));
            Assert.That(
                lines.Count(line => line.Contains("\"operation\":\"" + GateOperations.RenewCertificate + "\"")
                    && line.Contains(Keys(_newKey, _oldKey))
                    && line.Contains("\"reason\":\"" + AdmissionAudit.RefusedThisSessionReason + "\"")),
                Is.EqualTo(1), "the new certificate's renewal, refused in this session");
            Assert.That(
                lines.Count(line => line.Contains("\"operation\":\"" + GateOperations.RenewCertificate + "\"")
                    && line.Contains(Keys(_newKey, _oldKey))
                    && line.Contains("\"reason\":\"" + AdmissionAudit.StandingThisSessionReason + "\"")),
                Is.EqualTo(1), "and the one answered without asking");
            Assert.That(
                lines.Count(line => line.Contains("\"operation\":\"" + GateOperations.RenewCertificate + "\"")
                    && line.Contains(Keys(_newKey, _oldKey))
                    && line.Contains("\"reason\":\"" + AdmissionAudit.RefusedOnceReason + "\"")),
                Is.EqualTo(1), "and the one the dialog raised again, once the answer was lifted on the page");
            Assert.That(
                lines.Count(line => line.Contains("\"operation\":\"" + GateOperations.RevokeCertificate + "\"")
                    && line.Contains(Keys(_oldKey, _oldKey))),
                Is.EqualTo(1), "the old certificate given up, by its own key");
            Assert.That(lines, Has.None.Contains("reader"), "never a profile");
        }

        /// <summary>A line's certificate and client, as the audit writes them, side by side.</summary>
        private static string Keys(string admissionKey, string clientKey)
        {
            return "\"admissionKey\":\"" + admissionKey + "\",\"clientKey\":\"" + clientKey + "\"";
        }

        [Test]
        public void The_plugin_said_what_was_renewed_and_reported_only_the_operators_refusals()
        {
            var old = CertificateDigest.Shorten(_oldKey);
            var renewed = CertificateDigest.Shorten(_newKey);
            Assert.That(_pluginLog, Does.Contain("renewed " + old + " as " + renewed));

            // The old certificate is presented once more after it was given up, on purpose, and
            // the gate reports that refusal at a level an operator looks at; the operator's refusal
            // of the new one is reported at the same level, in this session and then once after the
            // answer was lifted. So those three lines, once each, and nothing else: the answer given
            // without asking is not a failure of anything, and neither is lifting it.
            var failures = LiveSession.FailuresIn(_pluginLog);
            var refusals = failures
                .Where(line => line.Contains("refused a certificate with no admission record: " + old))
                .ToArray();
            var inThisSession = failures
                .Where(line => line.Contains("a renewal by " + renewed + " was refused in this session"))
                .ToArray();
            var once = failures
                .Where(line => line.Contains("a renewal by " + renewed + " was refused once"))
                .ToArray();
            var unexpected = failures.Except(refusals).Except(inThisSession).Except(once).ToArray();

            Assert.That(refusals, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(inThisSession, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(once, Has.Length.EqualTo(1), _pluginLog);
            Assert.That(_pluginLog, Does.Contain("renewals of " + renewed + " are asked about again"),
                "the settings dialog's OK lifted the answer and said so");
            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        /// <summary>
        /// The renewal, for real: it blocks until somebody answers the dialog, so it runs on its
        /// own thread while this one waits for the window and presses Approve.
        /// </summary>
        private static LiveReply Approve(KeePassDriver driver, ThrowawayKeePass keePass, X509Certificate2 old)
        {
            LiveReply reply = null;
            Exception failed = null;

            var asking = new Thread(() =>
            {
                failed = Thrown.By(() =>
                {
                    reply = LiveGateClient.Send(
                        keePass.Port, keePass.NewGateName(), old, "POST", RenewalHandler.Path, "{}",
                        LiveGateClient.OnTheOperator);
                });
            }) { IsBackground = true };
            asking.Start();

            var dialog = driver.WaitForWindow("asking to renew", DialogTimeout);
            Assert.That(dialog, Is.Not.Null,
                "The renewal dialog never appeared. " +
                (failed == null ? string.Empty : "The request failed first: " + failed + " ") +
                driver.Diagnostics);
            Assert.That(Uia.Describe(dialog, ControlType.Text), Does.Contain("agent-fictitious"),
                "the dialog does not say who is asking");

            Assert.That(driver.PressButton(dialog, GateTab.ApproveButtonText), Is.True, driver.Diagnostics);
            Assert.That(asking.Join(TimeSpan.FromSeconds(60)), Is.True,
                "the client was still waiting after the operator answered");
            Assert.That(failed, Is.Null, "the renewal failed after the operator answered: " + failed);

            return reply;
        }

        /// <summary>
        /// A renewal refused in this session from the Refuse button's menu, for real: opened by a
        /// press on the arrow and answered by a press on the choice, as an operator with a mouse
        /// does, while the request waits on its own thread.
        /// </summary>
        private static LiveReply RefuseInThisSession(KeePassDriver driver, ThrowawayKeePass keePass, X509Certificate2 asking)
        {
            LiveReply reply = null;
            Exception failed = null;

            var renewing = new Thread(() =>
            {
                failed = Thrown.By(() =>
                {
                    reply = LiveGateClient.Send(
                        keePass.Port, keePass.NewGateName(), asking, "POST", RenewalHandler.Path, "{}",
                        LiveGateClient.OnTheOperator);
                });
            }) { IsBackground = true };
            renewing.Start();

            var dialog = driver.WaitForWindow("asking to renew", DialogTimeout);
            Assert.That(dialog, Is.Not.Null,
                "The second renewal dialog never appeared. " +
                (failed == null ? string.Empty : "The request failed first: " + failed + " ") +
                driver.Diagnostics);

            var menu = driver.OpenRefuseMenu(dialog);
            Assert.That(menu, Is.Not.Null, driver.Diagnostics);

            var choice = Uia.Descendant(menu, ControlType.MenuItem, name => name == GateTab.RefuseThisSessionText);
            Assert.That(choice, Is.Not.Null,
                "the menu offers no '" + GateTab.RefuseThisSessionText + "': " + Uia.Inventory(menu));
            Mouse.Click(choice);

            Assert.That(renewing.Join(TimeSpan.FromSeconds(60)), Is.True,
                "the client was still waiting after the operator answered");
            Assert.That(failed, Is.Null, "the renewal failed after the operator answered: " + failed);

            return reply;
        }

        /// <summary>
        /// Lifts the answer standing on the newest row of the program, from the clients page, as an
        /// operator at the keyboard does, and accepts the settings dialog. Answers with what the
        /// row's menu said.
        /// </summary>
        /// <remarks>
        /// The newest row, because the list is newest first and the renewed certificate is the one
        /// refused in this session; the menu's first line, stating that answer, is what proves the
        /// menu was opened on it.
        /// </remarks>
        private static string AllowRenewalOnTheClientsPage(KeePassDriver driver)
        {
            var settings = driver.OpenDatabaseSettings();
            driver.SelectGateTab(settings);
            driver.SelectPage(settings, GateTab.ClientsPageText);
            Thread.Sleep(900);

            var menu = driver.OpenClientMenu(settings, "agent-fictitious", true);
            Assert.That(menu, Is.Not.Null, driver.Diagnostics);
            var said = Uia.Inventory(menu);

            var allow = Uia.Descendant(menu, ControlType.MenuItem, name => name == "Allow renewal");
            Assert.That(allow, Is.Not.Null, "the menu offers no Allow renewal: " + said);
            Mouse.Click(allow);
            Thread.Sleep(500);

            Assert.That(driver.AcceptDialog(settings), Is.True, driver.Diagnostics);
            return said;
        }

        /// <summary>A renewal refused once, by a press on Refuse, while the request waits on its own thread.</summary>
        private static LiveReply RefuseOnce(KeePassDriver driver, ThrowawayKeePass keePass, X509Certificate2 asking)
        {
            LiveReply reply = null;
            Exception failed = null;

            var renewing = new Thread(() =>
            {
                failed = Thrown.By(() =>
                {
                    reply = LiveGateClient.Send(
                        keePass.Port, keePass.NewGateName(), asking, "POST", RenewalHandler.Path, "{}",
                        LiveGateClient.OnTheOperator);
                });
            }) { IsBackground = true };
            renewing.Start();

            var dialog = driver.WaitForWindow("asking to renew", DialogTimeout);
            Assert.That(dialog, Is.Not.Null,
                "The renewal dialog did not come back once the answer was lifted. " +
                (failed == null ? string.Empty : "The request failed first: " + failed + " ") +
                driver.Diagnostics);

            Assert.That(driver.PressButton(dialog, GateTab.RefuseButtonText), Is.True, driver.Diagnostics);
            Assert.That(renewing.Join(TimeSpan.FromSeconds(60)), Is.True,
                "the client was still waiting after the operator answered");
            Assert.That(failed, Is.Null, "the renewal failed after the operator answered: " + failed);

            return reply;
        }

        /// <summary>The certificate a bundle carries, loaded so that TLS can present it.</summary>
        private static X509Certificate2 Identity(string bundle)
        {
            var body = JsonBody.Read(bundle);
            Assert.That(body, Is.Not.Null, "the renewal did not answer with a bundle: " + bundle);
            using (PrivateKeyFiles.Lock())
            {
                return CertificateBlob.Load(body.Text("identity"), X509KeyStorageFlags.Exportable);
            }
        }
    }
}
