using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for step 8 of the build order: a stranger becomes a client of a
    /// real KeePass, and nothing about it is simulated.
    /// </summary>
    /// <remarks>
    /// The operator presses the invite button on the real tab. The token is read off the real
    /// screen. The client is a separate object holding nothing but that token and a port number,
    /// and it derives its own connection names from it. The approval dialog is a real window,
    /// approved by a real button press, on a thread that is not the one waiting for the answer.
    /// And the record is looked for in the `.kdbx` file after KeePass has exited, because a record
    /// that is only in memory admits nobody tomorrow.
    ///
    /// What that chain establishes, and nothing smaller does: that pairing writes through the
    /// plugin into the database KeePass is holding, that KeePass's own save is what makes it
    /// durable, and that the certificate handed over at the end opens a route on the very next
    /// connection.
    ///
    /// The settings dialog is still closed with Escape. It costs nothing here: an invitation is
    /// never written to the database, so there is nothing on that dialog for OK to keep.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class LivePairingTests
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;

        private string _token;
        private string _outstanding;
        private bool _serverProved;
        private PairingReply _paired;
        private PairingReply _refused;
        private int _statusAfterPairing;
        private string _statusBody;
        private IReadOnlyList<AdmissionRecord> _recordsInTheFile;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;

            var driver = KeePassDriver.Start(keePass);
            WaitUntilServing(keePass, driver);

            InviteFromTheTab(driver);

            var client = new PairingClient(keePass.Port, _token);
            var hello = client.Hello();
            _serverProved = hello.ServerProved;

            // Here rather than only in the test that reads it, because a request needs the hello
            // to have proved the server: without it the request is never sent, and what the run
            // would report instead is an approval dialog that never appeared, a minute later.
            Assert.That(_serverProved, Is.True, "the hello did not prove the server: " + hello);

            _paired = ApproveTheNextRequest(driver, client, "agent-fictitious", "ops");

            if (_paired.Status == 200)
            {
                UseWhatItWasGiven(keePass, client);
            }

            _refused = RefuseASecondAttempt(driver, keePass);

            _exited = driver.ShutDown();
            _recordsInTheFile = keePass.ReadAdmissions();

            _pluginLog = keePass.ReadPluginLog();
            TestContext.Out.WriteLine("Plugin log:");
            TestContext.Out.WriteLine(_pluginLog);
            TestContext.Out.WriteLine(driver.Diagnostics);
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
        public void The_tab_hands_the_operator_a_token()
        {
            Assert.That(_token, Is.Not.Null.And.Not.Empty, "nothing was on the tab to give anybody");
            Assert.That(PairingToken.Decode(_token), Is.Not.Null, "what was on screen is not a pairing token");
        }

        [Test]
        public void The_tab_says_the_invitation_is_outstanding()
        {
            Assert.That(_outstanding, Does.StartWith("The in-memory code is good for another"));
        }

        [Test]
        public void The_server_proves_itself_to_a_client_that_has_no_anchor()
        {
            Assert.That(_serverProved, Is.True,
                "without this a client would accept whatever certificate answered the connection");
        }

        [Test]
        public void An_approved_request_comes_back_with_a_certificate()
        {
            Assert.That(_paired.Status, Is.EqualTo(200), _paired.Failure ?? _paired.Body);
        }

        [Test]
        public void The_certificate_it_was_given_opens_a_route_on_the_next_connection()
        {
            // Everything at once: the record reached the database KeePass is holding, the gate key
            // it was handed resolves, and the certificate it was issued is admitted.
            Assert.That(_statusAfterPairing, Is.EqualTo(200), _statusBody);
            Assert.That(_statusBody, Does.Contain("\"ok\":true"));
        }

        [Test]
        public void The_record_is_in_the_file_after_KeePass_has_gone()
        {
            Assert.That(_recordsInTheFile, Is.Not.Null);
            Assert.That(_recordsInTheFile.Count, Is.EqualTo(1),
                "pairing has to save, or the client stops working the next time KeePass starts");
            Assert.That(_recordsInTheFile[0].Subject, Is.EqualTo("CN=agent-fictitious"));
            Assert.That(_recordsInTheFile[0].Profiles, Is.EqualTo(new[] { "ops" }));
        }

        [Test]
        public void A_refused_request_is_told_so_and_gets_nothing()
        {
            Assert.That(_refused.Status, Is.EqualTo(403), _refused.Failure ?? _refused.Body);
            Assert.That(_refused.Body, Does.Contain("refused"));
        }

        [Test]
        public void A_refusal_leaves_nothing_extra_in_the_file()
        {
            Assert.That(_recordsInTheFile.Count, Is.EqualTo(1), "a refusal wrote a record");
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so something was still holding it.");
            Assert.That(_pluginLog, Does.Contain("stopped listening"));
        }

        [Test]
        public void The_audit_wrote_down_the_pairing_by_its_invitation_and_the_refusal_too()
        {
            // The production wiring of the record of admissions: `StartListening` hands the pairing
            // handler the access check's own, and only a running plugin reaches that line.
            var pairings = File.ReadAllLines(_session.KeePass.AuditPath)
                .Where(line => line.Contains("\"operation\":\"" + GateOperations.PairRequest + "\""))
                .ToArray();

            Assert.That(
                pairings.Count(line => line.Contains("\"decision\":\"" + AuditEntry.Approved + "\"")
                    && line.Contains("\"admissionKey\":null,\"clientKey\":\"" + _recordsInTheFile[0].AdmissionKey + "\"")
                    && line.Contains("\"issued\":\"" + _recordsInTheFile[0].AdmissionKey + "\"")),
                Is.EqualTo(1), string.Join(Environment.NewLine, pairings));
            Assert.That(
                pairings.Count(line => line.Contains("\"decision\":\"" + AuditEntry.Refused + "\"")
                    && line.Contains("\"admissionKey\":null,\"clientKey\":null")),
                Is.EqualTo(1), "a refusal admitted nobody, so it names no client");
            Assert.That(
                pairings.Count(line => line.Contains("\"decision\":\"" + AuditEntry.Refused + "\"")),
                Is.EqualTo(1), string.Join(Environment.NewLine, pairings));
            Assert.That(pairings, Has.All.Contains("\"invitation\":\""), "each names its invitation");
            Assert.That(pairings, Has.None.Contains("ops"), "and none names a profile");
        }

        [Test]
        public void The_plugin_recorded_the_pairing_and_the_refusal_and_nothing_else()
        {
            Assert.That(_pluginLog, Does.Contain("paired CN=agent-fictitious"));

            var unexpected = LiveSession.FailuresIn(_pluginLog)
                .Where(line => !line.Contains("was refused, and it is spent"))
                .ToArray();

            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        /// <summary>Phase 0, on the real tab: the operator invites somebody and reads out the token.</summary>
        private void InviteFromTheTab(KeePassDriver driver)
        {
            var dialog = driver.OpenDatabaseSettings();
            var tab = driver.SelectGateTab(dialog);
            Assert.That(tab, Is.Not.Null, "The KPG tab is missing, so nobody can be invited.");
            Assert.That(driver.SelectPage(dialog, GateTab.ClientsPageText), Is.Not.Null,
                "The clients page is missing, so nobody can be invited.");

            Assert.That(driver.PressButton(dialog, GateTab.InviteButtonText), Is.True, driver.Diagnostics);

            // By the identifier now that `ValueOf` looks one up, rather than by the words. The box
            // announces `InvitationBoxLabel` to a screen reader, which is a phrase for somebody
            // listening rather than a handle for a test, and `InvitationBoxName` is ours.
            _token = driver.ValueOf(dialog, GateTab.InvitationBoxName);
            _outstanding = driver.TextStartingWith(dialog, "The in-memory code is good");

            // With OK, which keeps the code: closing the dialog any other way takes it back.
            Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);
        }

        /// <summary>
        /// Phase 4, for real. The request blocks until somebody answers the dialog, so it runs on
        /// its own thread while this one waits for the window and presses the button.
        /// </summary>
        private PairingReply ApproveTheNextRequest(
            KeePassDriver driver, PairingClient client, string name, params string[] profiles)
        {
            return Decide(driver, client, name, profiles, GateTab.ApproveButtonText);
        }

        private PairingReply RefuseASecondAttempt(KeePassDriver driver, ThrowawayKeePass keePass)
        {
            var dialog = driver.OpenDatabaseSettings();
            driver.SelectGateTab(dialog);
            driver.SelectPage(dialog, GateTab.ClientsPageText);
            Assert.That(driver.PressButton(dialog, GateTab.InviteButtonText), Is.True, driver.Diagnostics);
            var token = driver.ValueOf(dialog, GateTab.InvitationBoxName);
            Assert.That(driver.AcceptDialog(dialog), Is.True, driver.Diagnostics);

            // KeePass marks the database changed whenever its settings dialog is accepted, and a
            // refusal writes nothing that would save it again, so it is saved here: otherwise
            // closing KeePass at the end stops at the question whether to save.
            driver.Save();

            var client = new PairingClient(keePass.Port, token);
            Assert.That(client.Hello().ServerProved, Is.True);

            return Decide(driver, client, "agent-unwanted", Array.Empty<string>(), GateTab.RefuseButtonText);
        }

        private static PairingReply Decide(
            KeePassDriver driver, PairingClient client, string name, string[] profiles, string button)
        {
            PairingReply reply = null;
            Exception failed = null;

            // Whatever the request throws is kept and reported, because on a thread of its own it
            // would otherwise end that thread and nothing else: the test went on to wait a minute
            // for a dialog and then blamed the dialog.
            var asking = new Thread(() =>
            {
                failed = Thrown.By(() => reply = client.Request(name, profiles));
            }) { IsBackground = true };
            asking.Start();

            var approval = driver.WaitForWindow("asking to pair", DialogTimeout);
            Assert.That(approval, Is.Not.Null,
                "The approval dialog never appeared. " +
                (failed == null ? string.Empty : "The request failed first: " + failed + " ") +
                driver.Diagnostics);
            Assert.That(Uia.Describe(approval, ControlType.Text), Does.Contain(name),
                "the dialog does not say who is asking");

            Assert.That(driver.PressButton(approval, button), Is.True, driver.Diagnostics);
            Assert.That(asking.Join(TimeSpan.FromSeconds(60)), Is.True,
                "the client was still waiting after the operator answered");
            Assert.That(failed, Is.Null, "the request failed after the operator answered: " + failed);

            return reply;
        }

        /// <summary>
        /// What a paired client does next, using only what pairing gave it: a gate key to build a
        /// name from and a certificate to present.
        /// </summary>
        private void UseWhatItWasGiven(ThrowawayKeePass keePass, PairingClient client)
        {
            var reply = LiveGateClient.Get(
                keePass.Port, SniName.For(client.GateKey), client.Identity, GateOperations.StatusPath);

            _statusAfterPairing = reply.Status;
            _statusBody = reply.Body ?? reply.Failure;
        }

        /// <summary>
        /// Ready is the database being served, not the socket being open.
        /// </summary>
        /// <remarks>
        /// The listener starts inside the plugin's `Initialize`, and KeePass opens the database it
        /// was given on its command line only once its plugins are initialized. In between, the
        /// resolver has no target for the rotating name, so the connection is dropped before any
        /// TLS answer and a client reports the remote party closing the stream. That is the gate
        /// being fail-closed and is correct; a fixture that goes on `listening on` is racing the
        /// startup, and on a cold machine it loses. `serving a database with authority` is written
        /// when the target is built, which the unlock this file's opening does, and is the first
        /// moment a name resolves.
        ///
        /// Both lines, in that order, because the serving line on its own would not tell a gate
        /// that never started from one that started and has nothing to serve yet. And the tab this
        /// fixture presses the invite button on needs the database quite as much as the wire does:
        /// there is no invitation to hand out until there is an authority to hand one out for.
        /// </remarks>
        internal static void WaitUntilServing(ThrowawayKeePass keePass, KeePassDriver driver)
        {
            WaitForLog(keePass, driver, "listening on 127.0.0.1:" + keePass.Port,
                "The plugin never reported a listening gate.");

            WaitForLog(keePass, driver, "serving a database with authority",
                "The plugin never reported serving the database KeePass was started with.");
        }

        private static void WaitForLog(
            ThrowawayKeePass keePass, KeePassDriver driver, string marker, string failure)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().Contains(marker))
                {
                    return;
                }

                Thread.Sleep(250);
            }

            Assert.Fail(failure + " " + driver.Diagnostics + Environment.NewLine +
                        "Log so far:" + Environment.NewLine + keePass.ReadPluginLog());
        }
    }
}
