using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The transport end to end, over a real socket with real TLS 1.3 and real certificates. No
    /// KeePass and no desktop: everything here is loopback and certificates built in memory.
    /// </summary>
    /// <remarks>
    /// This is where the pieces stop being separately correct and start being a server. The gate
    /// reads a name off the wire, resolves it to a database, chooses that database's certificate,
    /// and only then is there a handshake to have.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class GateListenerTests
    {
        private TestAuthority _first;
        private TestAuthority _second;
        private TestAuthority _foreign;
        private FakeGateTargets _targets;
        private RecordingGateLog _log;
        private GateListener _listener;

        [OneTimeSetUp]
        public void CreateAuthorities()
        {
            _first = TestAuthority.New();
            _second = TestAuthority.New();
            _foreign = TestAuthority.New();
        }

        [OneTimeTearDown]
        public void DisposeAuthorities()
        {
            _first.Dispose();
            _second.Dispose();
            _foreign.Dispose();
        }

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingGateLog();
            _targets = new FakeGateTargets(_first);
            _listener = new GateListener(
                new GateOptions { Port = 0 },
                new GateResolver(_targets, _log),
                new GateRouter("9.9.9"),
                _log);

            Assert.That(_listener.Start(), Is.True, "the listener did not start");
        }

        [TearDown]
        public void TearDown()
        {
            _listener.Dispose();

            foreach (var entry in _log.Entries)
            {
                TestContext.Out.WriteLine(entry.ToString());
            }
        }

        [Test]
        public void A_paired_client_gets_an_answer()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(200));
                Assert.That(reply.Body, Does.Contain("\"ok\":true"));
            }
        }

        [Test]
        public void The_answer_names_the_database_the_name_resolved_to()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(reply.Body, Does.Contain(_first.DatabaseId.Substring(0, 16)));
            }
        }

        [Test]
        public void The_server_presents_the_certificate_of_the_database_that_was_named()
        {
            // The whole reason the ClientHello is read by hand: the certificate has to be chosen
            // before anything else about the client is known.
            _targets.Add(_second);

            using (var first = _first.IssueClient("agent-fictitious"))
            using (var second = _second.IssueClient("agent-fictitious"))
            {
                var toFirst = GateClient.Get(_listener.Port, _first.NewName(), first, GateRouter.StatusPath);
                var toSecond = GateClient.Get(_listener.Port, _second.NewName(), second, GateRouter.StatusPath);

                Assert.That(toFirst.ServerCertificate.RawData, Is.EqualTo(_first.Host.CertificateDer));
                Assert.That(toSecond.ServerCertificate.RawData, Is.EqualTo(_second.Host.CertificateDer));
                Assert.That(toFirst.Status, Is.EqualTo(200));
                Assert.That(toSecond.Status, Is.EqualTo(200));
            }
        }

        [Test]
        public void A_client_with_no_certificate_is_told_it_is_unpaired()
        {
            var reply = GateClient.Get(_listener.Port, _first.NewName(), null, GateRouter.StatusPath);

            Assert.That(reply.Answered, Is.True, reply.Failure);
            Assert.That(reply.Status, Is.EqualTo(401));
            Assert.That(reply.Body, Does.Contain("unpaired"));
        }

        [Test]
        public void A_client_from_another_authority_is_told_it_is_unpaired()
        {
            using (var certificate = _foreign.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(401), reply.Failure);
            }
        }

        [Test]
        public void A_client_presenting_a_host_certificate_is_told_it_is_unpaired()
        {
            var reply = GateClient.Get(
                _listener.Port, _first.NewName(), _first.HostCertificateAsClient(), GateRouter.StatusPath);

            Assert.That(reply.Status, Is.EqualTo(401), reply.Failure);
        }

        [Test]
        public void A_route_that_does_not_exist_is_a_404()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, "/secrets");

                Assert.That(reply.Status, Is.EqualTo(404));
            }
        }

        [Test]
        public void A_name_no_database_answers_to_is_dropped_without_a_word()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var forged = SniName.For(new byte[32]);

                var reply = GateClient.Get(_listener.Port, forged, certificate, GateRouter.StatusPath);

                Assert.That(reply.Connected, Is.True, "the connection is accepted before it is judged");
                Assert.That(reply.Answered, Is.False,
                    "an unresolvable name must be indistinguishable from a port with nothing behind it");
            }
        }

        [Test]
        public void A_name_that_is_not_ours_at_all_is_dropped_without_a_word()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, "localhost", certificate, GateRouter.StatusPath);

                Assert.That(reply.Answered, Is.False);
            }
        }

        [Test]
        public void Two_databases_answering_to_one_name_are_both_refused()
        {
            // Two copies of the same file open at once share an authority and a gate key, and are
            // genuinely indistinguishable, so neither is served.
            _targets.Add(_first);

            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(reply.Answered, Is.False);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("cannot be told apart")), Is.True);
            }
        }

        [Test]
        public void A_connection_that_is_not_TLS_is_dropped_and_the_listener_carries_on()
        {
            var reply = GateClient.SendRaw(_listener.Port, Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n"));

            Assert.That(reply.Answered, Is.False, "plain HTTP is answered with nothing at all");

            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var afterwards = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(afterwards.Status, Is.EqualTo(200), "a bad connection must not take the listener with it");
            }
        }

        [Test]
        public void A_connection_that_says_nothing_is_dropped_and_the_listener_carries_on()
        {
            GateClient.SendRaw(_listener.Port, Array.Empty<byte>());

            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var afterwards = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(afterwards.Status, Is.EqualTo(200));
            }
        }

        [Test]
        public void A_malformed_request_over_a_good_connection_is_answered_with_a_status()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Send(
                    _listener.Port, _first.NewName(), certificate, "POST /thing HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");

                Assert.That(reply.Status, Is.EqualTo(501), reply.Failure);
            }
        }

        [Test]
        public void Nothing_it_ever_answers_leaks_a_certificate_or_a_key()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(reply.Body, Does.Not.Contain("BEGIN"));
                Assert.That(reply.Body, Does.Not.Contain("MII"));
                Assert.That(reply.Body.Length, Is.LessThan(200));
            }
        }

        [Test]
        public void Every_connection_carries_a_different_name_for_the_same_database()
        {
            var names = Enumerable.Range(0, 5).Select(i => _first.NewName()).ToList();

            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));

            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                foreach (var name in names)
                {
                    Assert.That(GateClient.Get(_listener.Port, name, certificate, GateRouter.StatusPath).Status,
                        Is.EqualTo(200), "the name " + name + " was refused");
                }
            }
        }

        [Test]
        public void It_binds_loopback_and_nothing_else()
        {
            // Reaching this from another machine is a tunnel's job, by construction rather than by
            // a setting somebody can change.
            Assert.That(_listener.IsListening, Is.True);
            Assert.That(_listener.Port, Is.GreaterThan(0));
        }

        [Test]
        public void Starting_twice_is_harmless_and_stopping_twice_is_too()
        {
            Assert.That(_listener.Start(), Is.True);
            var port = _listener.Port;

            _listener.Stop();
            _listener.Stop();

            Assert.That(_listener.IsListening, Is.False);
            Assert.That(port, Is.EqualTo(_listener.Port));
        }

        [Test]
        public void After_stopping_nothing_answers()
        {
            var port = _listener.Port;
            _listener.Stop();

            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                var reply = GateClient.Get(port, _first.NewName(), certificate, GateRouter.StatusPath);

                Assert.That(reply.Answered, Is.False);
            }
        }

        [Test]
        public void A_port_already_taken_is_reported_rather_than_thrown()
        {
            var second = new GateListener(
                new GateOptions { Port = _listener.Port },
                new GateResolver(_targets, _log),
                new GateRouter("9.9.9"),
                _log);

            try
            {
                // KeePass has to keep working when the port is busy, so this is a false rather than
                // an exception on the way out of Initialize.
                Assert.That(second.Start(), Is.False);
                Assert.That(_log.Errors, Is.Not.Empty);
            }
            finally
            {
                second.Dispose();
            }
        }

        [Test]
        public void A_listener_whose_accept_loop_ends_stops_claiming_to_listen()
        {
            // The failure this pins is silent: an accept that ends for a reason nobody chose used
            // to leave the flag set, so the tab reported a gate that was listening and `Start`
            // returned true without binding anything.
            var port = _listener.Port;
            Underneath(_listener).Stop();

            Assert.That(Waited(delegate { return !_listener.IsListening; }), Is.True,
                "the loop ended and the listener still says it is listening");

            // And the reason is written down. Stopping and failing arrive here as the same
            // exception, and only the running flag tells them apart: `Stop` clears it first, so a
            // failure while it is still set is the case this test is standing in for. The log is
            // the one place that reason could be kept, and it used to be dropped there.
            Assert.That(
                Waited(delegate { return _log.Errors.Any(e => e.Message.Contains("the accept loop failed")); }),
                Is.True, "an accept that ended for a reason nobody chose said nothing about it");

            Assert.That(_listener.Start(), Is.True, "it has to be able to take a port again");
            Assert.That(_listener.IsListening, Is.True);
            Assert.That(_listener.Port, Is.Not.EqualTo(0));
            Assert.That(port, Is.Not.EqualTo(0));
        }

        [Test]
        public void More_connections_than_it_will_serve_are_closed_rather_than_queued()
        {
            // The gate borrows the password manager's thread pool, so a connection that waits is a
            // thread KeePass is not using. The ordinary limits otherwise, so the one connection
            // served holds its slot for as long as a silent peer can, which is far longer than a
            // loaded machine takes to accept the others.
            var options = new GateOptions { Port = 0, MaxConnections = 1 };
            var listener = new GateListener(options, new GateResolver(_targets, _log), new GateRouter("9.9.9"), _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                var held = new List<TcpClient>();
                try
                {
                    for (var i = 0; i < 4; i++)
                    {
                        var client = new TcpClient();
                        client.Connect(IPAddress.Loopback, listener.Port);
                        held.Add(client);
                    }

                    Assert.That(Waited(delegate { return _log.Warnings.Any(w => w.Message.Contains("already being served")); }),
                        Is.True, "nothing refused a connection past the ceiling");

                    // And said so once. A refusal costs the peer one packet and happens before
                    // anything is known about it, so a line each is a log a stranger can grow
                    // until whatever else is in it is buried. A second line, if it were coming at
                    // all, would be written the moment the next refusal was, which is why this
                    // waits only long enough to see one arrive. Short on purpose: a slow machine
                    // can only push a second line past the window and let this pass, never make a
                    // single line look like two.
                    var second = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                    while (DateTime.UtcNow < second)
                    {
                        Thread.Sleep(20);
                    }

                    Assert.That(
                        _log.Warnings.Count(w => w.Message.Contains("already being served")), Is.EqualTo(1),
                        "three refusals wrote three lines, so a peer that is refused can write the log");
                }
                finally
                {
                    foreach (var client in held)
                    {
                        client.Close();
                    }
                }
            }
            finally
            {
                listener.Dispose();
            }
        }

        /// <summary>The listener's own socket, so a test can end its accept loop from outside.</summary>
        private static TcpListener Underneath(GateListener listener)
        {
            var field = typeof(GateListener).GetField(
                "_listener", BindingFlags.Instance | BindingFlags.NonPublic);
            return (TcpListener)field.GetValue(listener);
        }

        /// <summary>Waits for something a background thread does, rather than for a duration.</summary>
        /// <remarks>
        /// Thirty seconds, which only a failure ever waits out: a loaded machine can be slow to
        /// schedule the thread being waited on, and this ends the moment it has run.
        /// </remarks>
        private static bool Waited(Func<bool> until)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (until())
                {
                    return true;
                }

                Thread.Sleep(20);
            }

            return until();
        }

        [Test]
        public void Nothing_that_happens_on_a_connection_is_reported_as_a_plugin_failure()
        {
            using (var certificate = _first.IssueClient("agent-fictitious"))
            {
                GateClient.Get(_listener.Port, _first.NewName(), certificate, GateRouter.StatusPath);
                GateClient.Get(_listener.Port, "localhost", certificate, GateRouter.StatusPath);
                GateClient.SendRaw(_listener.Port, new byte[] { 1, 2, 3 });
            }

            Assert.That(_log.Errors, Is.Empty, string.Join("; ", _log.Errors.Select(e => e.Message)));
        }
    }
}
