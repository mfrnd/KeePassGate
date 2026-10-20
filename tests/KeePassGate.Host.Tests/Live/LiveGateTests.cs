using System;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The acceptance test for step 6 of the build order: a real KeePass, with the plugin
    /// loaded and a database that has an authority, answering a real client over mutual TLS.
    /// </summary>
    /// <remarks>
    /// Nothing in this fixture is a stand-in. KeePass is a separate process. The client is an
    /// ordinary `SslStream` in this one. The certificate it presents was issued by the authority
    /// inside the database file, which the test can do because it holds the file and its password,
    /// exactly as the design says: possession of the file is possession of the authority.
    ///
    /// What it proves that the loopback tests cannot: that the plugin starts a listener inside
    /// KeePass, finds the open database, reads its authority out of custom data, and serves with a
    /// certificate whose private key came back out of a KDBX file.
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.Live)]
    [NonParallelizable]
    public sealed class LiveGateTests
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

        private LiveSession _session;
        private string _databaseId;
        private string _refusedDigest;

        private Reply _paired;
        private Reply _unpaired;
        private Reply _noRecord;
        private Reply _wrongName;
        private Reply _notFound;
        private bool _exited;
        private string _pluginLog;

        [OneTimeSetUp]
        public void RunTheWholeSession()
        {
            _session = LiveSession.Begin(true);
            var keePass = _session.KeePass;
            _databaseId = keePass.DatabaseId();

            // Both certificates are issued and one is admitted before KeePass starts, because a
            // running KeePass serves the database it holds in memory: a record written to the file
            // underneath it would be invisible and then overwritten. Pairing writes through the
            // plugin for exactly that reason, and it is step 8.
            using (var admitted = keePass.IssueClientCertificate("agent-fictitious"))
            using (var never = keePass.IssueClientCertificate("agent-unadmitted"))
            {
                keePass.Admit(admitted, "ops");

                var process = keePass.Start();
                WaitForListening(keePass, process);
                WaitForDatabase(keePass, process);

                _paired = Ask(keePass.Port, keePass.NewGateName(), admitted, GateRouter.StatusPath);
                _notFound = Ask(keePass.Port, keePass.NewGateName(), admitted, "/secrets");
                _noRecord = Ask(keePass.Port, keePass.NewGateName(), never, GateRouter.StatusPath);
                _refusedDigest = CertificateDigest.Shorten(CertificateDigest.Of(never));
                _unpaired = Ask(keePass.Port, keePass.NewGateName(), null, GateRouter.StatusPath);
                _wrongName = Ask(keePass.Port, SniName.For(new byte[32]), admitted, GateRouter.StatusPath);

                process.CloseMainWindow();
                _exited = process.WaitForExit(30000);
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
        public void KeePass_listens_as_soon_as_the_plugin_is_loaded()
        {
            Assert.That(_pluginLog, Does.Contain("listening on 127.0.0.1:" + _session.KeePass.Port));
        }

        [Test]
        public void A_paired_client_is_answered()
        {
            Assert.That(_paired.Status, Is.EqualTo(200), _paired.Failure);
            Assert.That(_paired.Body, Does.Contain("\"ok\":true"));
        }

        [Test]
        public void The_answer_names_the_database_inside_that_KeePass()
        {
            Assert.That(_paired.Body, Does.Contain(_databaseId.Substring(0, 16)));
        }

        [Test]
        public void The_certificate_it_serves_with_came_out_of_the_database()
        {
            Assert.That(_paired.ServerSubject, Is.EqualTo("CN=KeePassGate host"));
        }

        [Test]
        public void A_certificate_from_the_same_authority_with_no_record_is_refused()
        {
            // Everything about it is right except the one thing that grants: this authority issued
            // it, it is current, and it is for client authentication. It opens nothing.
            Assert.That(_noRecord.Status, Is.EqualTo(401), _noRecord.Failure);
            Assert.That(_noRecord.Body, Does.Contain("unpaired"));
        }

        [Test]
        public void A_client_with_no_certificate_is_told_it_is_unpaired()
        {
            Assert.That(_unpaired.Status, Is.EqualTo(401), _unpaired.Failure);
        }

        [Test]
        public void A_route_that_does_not_exist_is_a_404()
        {
            Assert.That(_notFound.Status, Is.EqualTo(404), _notFound.Failure);
        }

        [Test]
        public void A_name_that_KeePass_cannot_resolve_is_dropped_without_a_word()
        {
            Assert.That(_wrongName.Answered, Is.False,
                "an unresolvable name must look exactly like a port with nothing behind it");
        }

        [Test]
        public void KeePass_shut_down_by_itself_afterwards()
        {
            Assert.That(_exited, Is.True, "KeePass did not exit, so the listener held it open.");
            Assert.That(_pluginLog, Does.Contain("stopped listening"));
        }

        [Test]
        public void The_only_thing_reported_was_the_refusal_this_test_asked_for()
        {
            var unexpected = LiveSession.FailuresIn(_pluginLog)
                .Where(line => !line.Contains("refused a certificate with no admission record"))
                .ToArray();

            Assert.That(unexpected, Is.Empty, string.Join(Environment.NewLine, unexpected));
        }

        [Test]
        public void The_refusal_was_recorded_with_the_certificate_it_refused()
        {
            // Both halves. The fixed half says a refusal happened; the digest says which
            // certificate, which is the half an operator needs and the half a test that matched
            // only the prefix would never have missed going away.
            Assert.That(_pluginLog, Does.Contain("refused a certificate with no admission record"));
            Assert.That(_pluginLog, Does.Contain(_refusedDigest));
        }

        [Test]
        public void It_happened_once_and_not_to_the_admitted_client()
        {
            // The count, not just the phrase. Filtering the failure log by that phrase would
            // otherwise pass a session in which the record never landed and every single request
            // was refused, which is exactly the failure the admitted half is meant to catch.
            var refusals = LiveSession.FailuresIn(_pluginLog)
                .Count(line => line.Contains("refused a certificate with no admission record"));

            Assert.That(refusals, Is.EqualTo(1), _pluginLog);
        }

        private static void WaitForListening(ThrowawayKeePass keePass, System.Diagnostics.Process process)
        {
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (keePass.ReadPluginLog().Contains("listening on 127.0.0.1:" + keePass.Port))
                {
                    return;
                }

                if (process.HasExited)
                {
                    Assert.Fail("KeePass exited during start up with code " + process.ExitCode + ".");
                }

                Thread.Sleep(250);
            }

            Assert.Fail("The plugin never reported a listening gate. Log so far:" +
                        Environment.NewLine + keePass.ReadPluginLog());
        }

        /// <summary>
        /// Ready is the database being open, not the socket.
        /// </summary>
        /// <remarks>
        /// The listener starts inside the plugin's `Initialize`, and KeePass opens the database it
        /// was given on its command line only once its plugins are initialized. In between, the
        /// resolver has no target for the rotating name, so the connection is dropped before any
        /// TLS answer. That is the gate being fail-closed and is correct; a fixture that asks on
        /// `listening on` is racing the startup, and on a cold machine it loses. The plugin writes
        /// nothing at that moment, since it builds a target only when a connection asks for one,
        /// so what says it is KeePass's title bar, which names the file once it is open.
        /// </remarks>
        private static void WaitForDatabase(ThrowawayKeePass keePass, System.Diagnostics.Process process)
        {
            var name = System.IO.Path.GetFileName(keePass.DatabasePath);
            var title = string.Empty;
            var deadline = DateTime.UtcNow + StartTimeout;
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    Assert.Fail("KeePass exited during start up with code " + process.ExitCode + ".");
                }

                process.Refresh();
                title = process.MainWindowTitle;
                if (title.Contains(name))
                {
                    return;
                }

                Thread.Sleep(250);
            }

            Assert.Fail("KeePass never showed " + name + " open. Its title read: " + title);
        }

        /// <summary>
        /// One request against the gate. `LiveGateClient` is the client, shared with the pairing
        /// test so that both of them are talking to the same server in the same way.
        /// </summary>
        private static Reply Ask(int port, string serverName, X509Certificate2 certificate, string path)
        {
            var answer = LiveGateClient.Get(port, serverName, certificate, path);

            return new Reply
            {
                Answered = answer.Answered,
                Status = answer.Status,
                Body = answer.Body,
                ServerSubject = answer.ServerSubject,
                Failure = answer.Failure
            };
        }

        /// <summary>What came back from the gate, or why nothing did.</summary>
        private sealed class Reply
        {
            public bool Answered { get; set; }

            public int Status { get; set; }

            public string Body { get; set; }

            public string ServerSubject { get; set; }

            public string Failure { get; set; }
        }
    }
}
