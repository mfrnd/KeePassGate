using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
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
                GateOperations.Router("9.9.9"),
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
        public void A_port_that_could_not_be_taken_keeps_why_rather_than_only_logging_it()
        {
            // Nothing tries this port again in the process, so the reason has to outlive the
            // attempt: it is what the window under Tools shows for the rest of the session, and
            // what the box at startup says instead of sending somebody to read a log.
            var taken = _listener.Port;

            using (var second = new GateListener(
                new GateOptions { Port = taken },
                new GateResolver(_targets, _log),
                GateOperations.Router("9.9.9"),
                _log))
            {
                Assert.That(second.Start(), Is.False, "the port is already this fixture's");

                Assert.That(second.Refusal, Is.Not.Null,
                    "a refusal nobody kept is a refusal the operator is told to go and look for");
                Assert.That(second.Refusal, Does.Contain(taken.ToString(CultureInfo.InvariantCulture)),
                    "and it names the port, because the operator chose one");
            }
        }

        [Test]
        public void A_port_refused_before_it_was_tried_is_kept_as_why_nothing_listens()
        {
            // A `KEEPASSGATE_PORT` that names something other than a port stops the gate before any
            // socket is asked for, and the window under Tools reads why from here as it does for a
            // port that could not be taken.
            string refused;
            GateOptions.PortToTake(0, "80", out refused);
            Assert.That(refused, Is.Not.Null);

            using (var never = new GateListener(
                new GateOptions(), new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
            {
                never.Refuse(refused);

                Assert.That(never.IsListening, Is.False);
                Assert.That(never.Refusal, Is.EqualTo(refused));
            }

            // One that is running goes on running and has nothing to explain.
            _listener.Refuse(refused);
            Assert.That(_listener.IsListening, Is.True);
            Assert.That(_listener.Refusal, Is.Null);
        }

        [Test]
        public void A_paired_client_gets_an_answer()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(200));
                Assert.That(reply.Body, Does.Contain("\"ok\":true"));
            }
        }

        [Test]
        public void The_answer_names_the_database_the_name_resolved_to()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Body, Does.Contain(_first.DatabaseId.Substring(0, 16)));
            }
        }

        [Test]
        public void The_server_presents_the_certificate_of_the_database_that_was_named()
        {
            // The whole reason the ClientHello is read by hand: the certificate has to be chosen
            // before anything else about the client is known.
            _targets.Add(_second);

            using (var first = _first.IssueAdmittedClient())
            using (var second = _second.IssueAdmittedClient())
            {
                var toFirst = GateClient.Get(_listener.Port, _first.NewName(), first, GateOperations.StatusPath);
                var toSecond = GateClient.Get(_listener.Port, _second.NewName(), second, GateOperations.StatusPath);

                Assert.That(toFirst.ServerCertificate.RawData, Is.EqualTo(_first.Host.CertificateDer));
                Assert.That(toSecond.ServerCertificate.RawData, Is.EqualTo(_second.Host.CertificateDer));
                Assert.That(toFirst.Status, Is.EqualTo(200));
                Assert.That(toSecond.Status, Is.EqualTo(200));
            }
        }

        [Test]
        public void A_client_with_no_certificate_is_told_it_is_unpaired()
        {
            var reply = GateClient.Get(_listener.Port, _first.NewName(), null, GateOperations.StatusPath);

            Assert.That(reply.Answered, Is.True, reply.Failure);
            Assert.That(reply.Status, Is.EqualTo(401));
            Assert.That(reply.Body, Does.Contain("unpaired"));
        }

        [Test]
        public void A_client_from_another_authority_is_told_it_is_unpaired()
        {
            using (var certificate = _foreign.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(401), reply.Failure);
            }
        }

        [Test]
        public void A_client_presenting_a_host_certificate_is_told_it_is_unpaired()
        {
            var reply = GateClient.Get(
                _listener.Port, _first.NewName(), _first.HostCertificateAsClient(), GateOperations.StatusPath);

            Assert.That(reply.Status, Is.EqualTo(401), reply.Failure);
        }

        [Test]
        public void A_client_this_database_never_admitted_is_told_it_is_unpaired()
        {
            // The certificate is this authority's, current, and for client authentication. Without
            // a record it opens nothing, which is what makes a lost record survivable and what a
            // revocation relies on.
            using (var certificate = _first.IssueClient("agent-unadmitted"))
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(401), reply.Failure);
                Assert.That(reply.Body, Does.Contain("unpaired"));
            }
        }

        /// <summary>
        /// An admitted certificate presented by somebody who does not hold its key is refused in
        /// the handshake, before anything asks the database about it.
        /// </summary>
        /// <remarks>
        /// **The one check this plugin does not make itself.** It reads the first certificate the
        /// client sent and asks the anchor and the record about it, and whether the client holds
        /// the key behind it is TLS 1.3's CertificateVerify, verified inside Bouncy Castle. Every
        /// other client in these tests is `SslStream`, which always proves possession, so nothing
        /// held the gate to it: a dependency upgrade or a change to the transport that stopped
        /// checking would have admitted anybody holding a copy of a public certificate. Signed with
        /// the right key the same client is answered, which is what says the refusal is about the
        /// key. The status route is written out because what names it changes as the routing does.
        /// </remarks>
        [Test]
        public void A_certificate_presented_without_its_key_is_refused_in_the_handshake()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var material = HostKeyMaterial.From(
                    KeePassGate.Authority.CertificateBlob.Bytes(KeePassGate.Authority.CertificateBlob.Save(certificate)));
                Assert.That(material, Is.Not.Null);

                var forged = BouncyCastleGateClient.Get(
                    _listener.Port, _first.NewName(), material.CertificateDer, BouncyCastleGateClient.AnotherKey(),
                    "/gate/status");
                var genuine = BouncyCastleGateClient.Get(
                    _listener.Port, _first.NewName(), material.CertificateDer, material.PrivateKey, "/gate/status");

                Assert.That(genuine.Status, Is.EqualTo(200), "the client itself works: " + genuine);
                Assert.That(forged.Answered, Is.False, "a certificate without its key was answered: " + forged);

                // Why it was refused is asked of the gate's log rather than of the client. The gate
                // closes a refused connection with the client's request still unread, Windows answers
                // that with a reset, and a reset that reaches the client before its first read takes
                // the alert with it: a client made to wait half a second before reading never saw
                // `decrypt_error` at all. And waited for rather than read at once, because the line
                // is written by the connection's own thread once the alert has gone, so a client
                // can be told before the log is.
                Assert.That(
                    Waited(() => _log.Entries.Any(entry =>
                        entry.Message.StartsWith("a handshake failed", StringComparison.Ordinal)
                        && entry.Message.Contains("decrypt_error"))),
                    Is.True,
                    "refused in the handshake for its signature, which ends before the anchor or the record is asked");
            }
        }

        [Test]
        public void Revoking_a_client_refuses_it_on_the_very_next_connection()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                Assume.That(GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath).Status,
                    Is.EqualTo(200));

                _first.Revoke(certificate);

                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(401),
                    "a revocation takes effect at once, with no cache in the way");
            }
        }

        [Test]
        public void A_client_that_revokes_itself_is_refused_on_its_next_connection()
        {
            // Through the whole transport, which is what carries the database's records to the
            // handler: the route writes in the target the connection resolved to and nowhere else.
            using (var certificate = _first.IssueAdmittedClient())
            {
                var name = _first.NewName();
                var revoked = GateClient.Send(
                    _listener.Port,
                    name,
                    certificate,
                    "DELETE " + GateOperations.PathOf(GateOperations.RevokeCertificate) +
                    " HTTP/1.1\r\nHost: " + name + "\r\n\r\n");

                Assert.That(revoked.Status, Is.EqualTo(200), revoked.ToString());

                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Status, Is.EqualTo(401), "refused as a certificate the operator revoked is");
            }
        }

        [Test]
        public void A_route_that_does_not_exist_is_a_404()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, "/secrets");

                Assert.That(reply.Status, Is.EqualTo(404));
            }
        }

        [Test]
        public void A_name_no_database_answers_to_is_dropped_without_a_word()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var forged = SniName.For(new byte[32]);

                var reply = GateClient.Get(_listener.Port, forged, certificate, GateOperations.StatusPath);

                Assert.That(reply.Connected, Is.True, "the connection is accepted before it is judged");
                Assert.That(reply.Answered, Is.False,
                    "an unresolvable name must be indistinguishable from a port with nothing behind it");
            }
        }

        [Test]
        public void A_name_that_is_not_ours_at_all_is_dropped_without_a_word()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, "localhost", certificate, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.False);
            }
        }

        [Test]
        public void Two_databases_answering_to_one_name_are_both_refused()
        {
            // Two copies of the same file open at once share an authority and a gate key, and are
            // genuinely indistinguishable, so neither is served.
            _targets.Add(_first);

            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.False);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("cannot be told apart")), Is.True);
            }
        }

        [Test]
        public void A_connection_that_is_not_TLS_is_dropped_and_the_listener_carries_on()
        {
            var reply = GateClient.SendRaw(_listener.Port, Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n"));

            Assert.That(reply.Answered, Is.False, "plain HTTP is answered with nothing at all");

            using (var certificate = _first.IssueAdmittedClient())
            {
                var afterwards = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(afterwards.Status, Is.EqualTo(200), "a bad connection must not take the listener with it");
            }
        }

        [Test]
        public void A_connection_that_says_nothing_is_dropped_and_the_listener_carries_on()
        {
            GateClient.SendRaw(_listener.Port, Array.Empty<byte>());

            using (var certificate = _first.IssueAdmittedClient())
            {
                var afterwards = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(afterwards.Status, Is.EqualTo(200));
            }
        }

        [Test]
        public void A_handshake_this_side_fails_is_counted_rather_than_put_down_to_the_peer()
        {
            // Bouncy Castle catches whatever the server's own callbacks throw and raises an alert
            // in its place, so a defect there read as a client whose handshake failed: a line at
            // Info with the cause dropped, and nothing counted. Host material whose certificate
            // will not parse makes the credentials callback throw, as a defect there would.
            using (var broken = TestAuthority.New())
            {
                var unreadable = (HostKeyMaterial)typeof(HostKeyMaterial)
                    .GetConstructor(
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(byte[]), typeof(Org.BouncyCastle.Crypto.AsymmetricKeyParameter) },
                        null)
                    .Invoke(new object[] { new byte[] { 1, 2, 3 }, broken.Host.PrivateKey });
                typeof(TestAuthority)
                    .GetField("_hostKey", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(broken, unreadable);

                var log = new QueueLog();
                var incidents = new KeePassGate.Diagnostics.GateIncidents(log);
                using (var listener = new GateListener(
                    new GateOptions { Port = 0 },
                    new GateResolver(new FakeGateTargets(broken), log),
                    GateOperations.Router("9.9.9"),
                    incidents,
                    log))
                {
                    Assert.That(listener.Start(), Is.True);

                    // A peer's own failure first, which stays what it was: a client offering only
                    // TLS 1.2 is refused by the handshake itself, before any callback here runs.
                    using (var tcp = new TcpClient())
                    {
                        tcp.Connect("127.0.0.1", listener.Port);
                        using (var tls = new System.Net.Security.SslStream(tcp.GetStream(), false, delegate { return true; }))
                        {
                            Assert.Catch(() => tls.AuthenticateAsClient(
                                broken.NewName(), null, System.Security.Authentication.SslProtocols.Tls12, false));
                        }
                    }

                    Assert.That(Waited(() => log.Lines.Any(line => line.StartsWith("INFO a handshake failed", StringComparison.Ordinal))),
                        Is.True, string.Join("\n", log.Lines));
                    Assert.That(incidents.Count, Is.Zero, "a peer's failure is not this side's");

                    var reply = GateClient.Get(listener.Port, broken.NewName(), null, GateOperations.StatusPath);

                    Assert.That(reply.Answered, Is.False, "there is no certificate to answer with");
                    Assert.That(Waited(() => incidents.Count == 1), Is.True, string.Join("\n", log.Lines));
                    Assert.That(incidents.Recent.Single(), Does.Contain("the handshake failed on this side"));
                }
            }
        }

        [Test]
        public void A_connection_closed_before_its_thread_starts_is_one_that_ended()
        {
            // A lock closes every connection the accept loop has tracked, and that includes one
            // whose thread has not started yet. Closed that early, a `TcpClient` answers `Client`
            // with null, so reaching its timeouts through it was a null reference, which the
            // connection's catch-all counted as a defect in the plugin for a lock.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Socket socket;

                using (var peer = new TcpClient())
                {
                    peer.Connect(IPAddress.Loopback, port);
                    var accepted = listener.AcceptTcpClient();
                    accepted.Close();

                    Assert.That(accepted.Client, Is.Null, "what a client closed that early answers");
                    Assert.Throws<NullReferenceException>(() => accepted.ReceiveTimeout = 1000,
                        "and so what reaching through it did");
                    Assert.That(GateListener.Opened(accepted, 1000, out socket), Is.Null,
                        "a connection that ended before it began");
                }

                using (var peer = new TcpClient())
                {
                    // And one closed a moment later, once its socket has been read: that fails the
                    // way every other connection closed under a read does.
                    peer.Connect(IPAddress.Loopback, port);
                    var accepted = listener.AcceptTcpClient();
                    var stream = GateListener.Opened(accepted, 1000, out socket);
                    Assume.That(stream, Is.Not.Null);
                    accepted.Close();

                    var read = -1;
                    var failure = Assert.Catch(() => read = stream.Read(new byte[1], 0, 1));
                    Assert.That(read, Is.EqualTo(-1), "nothing was read");
                    Assert.That(KeePassGate.Diagnostics.Expected.OfConnection(failure), Is.True, failure.GetType().Name);
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>A log a test can read while connection threads write to it.</summary>
        private sealed class QueueLog : KeePassGate.Diagnostics.IGateLog
        {
            public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } =
                new System.Collections.Concurrent.ConcurrentQueue<string>();

            public void Info(string message)
            {
                Lines.Enqueue("INFO " + message);
            }

            public void Warn(string message)
            {
                Lines.Enqueue("WARN " + message);
            }

            public void Error(string message, Exception error)
            {
                Lines.Enqueue("ERROR " + message);
            }

            public void Unexpected(string message, Exception error)
            {
                Lines.Enqueue("UNEXPECTED " + message);
            }
        }

        [Test]
        public void A_malformed_request_over_a_good_connection_is_answered_with_a_status()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Send(
                    _listener.Port, _first.NewName(), certificate, "POST /thing HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n");

                Assert.That(reply.Status, Is.EqualTo(501), reply.Failure);
            }
        }

        [Test]
        public void A_body_over_the_cap_is_refused_with_a_413_the_client_can_read()
        {
            // `DESIGN.md` §110 promises this refusal, and a client streaming a body without asking
            // first reaches it only if the announced bytes are taken off the wire: a connection
            // closed while they are unread is reset by the stack, and the reset discards the 413
            // already delivered, leaving a transport failure the caller cannot tell from a stopped
            // gate or a wrong port.
            var options = new GateOptions { Port = 0, MaxBodyBytes = 64 };
            var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                using (var certificate = _first.IssueAdmittedClient())
                {
                    // Big enough that the client is still writing when the gate refuses it, which
                    // is what makes the reset certain rather than a race a small body sometimes
                    // wins.
                    var body = new string('x', 512 * 1024);
                    var reply = GateClient.Send(
                        listener.Port,
                        _first.NewName(),
                        certificate,
                        "POST /gate/status HTTP/1.1\r\nHost: gate\r\nContent-Length: " +
                        body.Length + "\r\n\r\n" + body);

                    Assert.That(reply.Answered, Is.True, reply.Failure);
                    Assert.That(reply.Status, Is.EqualTo(413), reply.Raw);
                    Assert.That(reply.Body, Does.Contain("over 64 bytes"));
                }
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Test]
        public void Nothing_it_ever_answers_leaks_a_certificate_or_a_key()
        {
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);

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

            using (var certificate = _first.IssueAdmittedClient())
            {
                foreach (var name in names)
                {
                    Assert.That(GateClient.Get(_listener.Port, name, certificate, GateOperations.StatusPath).Status,
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

            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Get(port, _first.NewName(), certificate, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.False);
            }
        }

        [Test]
        public void A_port_already_taken_is_reported_rather_than_thrown()
        {
            var second = new GateListener(
                new GateOptions { Port = _listener.Port },
                new GateResolver(_targets, _log),
                GateOperations.Router("9.9.9"),
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
            var listener = new GateListener(options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

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

        [Test]
        public void A_peer_that_dribbles_its_head_is_cut_off_at_the_budget()
        {
            // `Timeout` bounds one read and nothing else, so a peer sending a byte just inside it
            // satisfies every read and still never finishes: a thread pool thread held inside the
            // password manager for as long as the caps allow. The budget bounds the connection
            // itself, and it is the only thing that does. Five seconds, which is generous: the
            // handshake is inside it, and a loaded machine can take seconds over one.
            var options = new GateOptions { Port = 0, Budget = TimeSpan.FromSeconds(5) };
            var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                // A whole head, from a peer that has proved nothing, at a byte every quarter of a
                // second: a minute to arrive, padded to be that long. A connection nothing cuts
                // short is therefore answered rather than left hanging, so this fails by being
                // served late rather than by waiting for ever.
                var request = "GET " + GateOperations.StatusPath + " HTTP/1.1\r\nHost: gate\r\n" +
                              "X-Padding: " + new string('a', 190) + "\r\n\r\n";
                var spent = Stopwatch.StartNew();

                var reply = GateClient.Dribble(
                    listener.Port, _first.NewName(), null, request, TimeSpan.FromMilliseconds(250));

                spent.Stop();

                Assert.That(reply.Answered, Is.False,
                    "the socket is closed at the budget, so there is nothing to answer with: " + reply.Raw);
                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)),
                    "cut at the budget rather than at the end of a head that takes a minute to arrive");
                Assert.That(
                    Waited(delegate { return _log.Warnings.Any(w => w.Message.Contains("the head ended early")); }),
                    Is.True, "closing the socket is what ends the read the peer was holding");
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Test]
        public void A_peer_that_dribbles_its_body_is_cut_off_at_the_budget()
        {
            // The budget ends with the request, which is the whole of it including the body: it is
            // dropped, or extended for a pairing, only once the body has been read.
            // Dropped as the head arrived, it would let a peer that had proved nothing announce a
            // body and feed it a byte inside each read window, holding a thread for as long as the
            // body cap allows. The head goes at once here and the body a byte every quarter of a
            // second, a minute of it, against a budget of five seconds that is generous because
            // the handshake is inside it.
            var options = new GateOptions { Port = 0, Budget = TimeSpan.FromSeconds(5) };
            var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                var body = new string('a', 240);
                var head = "POST " + GateOperations.StatusPath + " HTTP/1.1\r\nHost: gate\r\n" +
                           "Content-Type: application/json\r\nContent-Length: " + body.Length + "\r\n\r\n";
                var spent = Stopwatch.StartNew();

                var reply = GateClient.DribbleBody(
                    listener.Port, _first.NewName(), null, head, body, TimeSpan.FromMilliseconds(250));

                spent.Stop();

                Assert.That(reply.Answered, Is.False,
                    "the socket is closed at the budget, so there is nothing to answer with: " + reply.Raw);
                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)),
                    "cut at the budget rather than at the end of a body that takes a minute to arrive");
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Test]
        public void A_write_that_outlasts_the_budget_is_still_answered()
        {
            // The budget exists for a peer that feeds the connection slowly, and once the whole
            // request is off the wire the peer has nothing left to feed. What is left is this
            // plugin's own write and save, which no number bounds: a save is a whole-database
            // write of whatever size the database is, over whatever storage it is on. Closing the
            // socket under one frees nothing, because the thread runs on to the end of the save
            // and the connection slot is given back when the thread finishes. All it destroys is
            // the answer, so the write lands, the client is told the connection closed with no
            // answer, and it holds no identifier for what it created.
            //
            // The budget is generous because the handshake is inside it and a loaded machine can
            // take seconds over one, and the handler outlasts it by two seconds while still
            // answering well inside the ten the client waits for a read.
            var options = new GateOptions { Port = 0, Budget = TimeSpan.FromSeconds(5) };
            var slow = new SleepingHandler(TimeSpan.FromSeconds(7));
            var listener = new GateListener(
                options, new GateResolver(_targets, _log), slow, _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                var spent = Stopwatch.StartNew();
                var reply = GateClient.Get(
                    listener.Port, _first.NewName(), null, GateOperations.StatusPath);
                spent.Stop();

                Assert.That(slow.Calls, Is.EqualTo(1), "the request was served");
                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(201));
                Assert.That(spent.Elapsed, Is.GreaterThan(options.Budget),
                    "and it took longer than the budget, which is the whole point of the case");
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Test]
        public void A_peer_that_dribbles_a_client_hello_is_cut_off_before_there_is_a_handshake()
        {
            // Something bounds the peek as well as the conversation, which is what cuts off a peer
            // that has not offered a whole ClientHello yet and never will. Here that is the budget,
            // because it is the shorter of the two: the phase before a name resolves is measured
            // against a single read or the whole budget, whichever is less.
            //
            // A read is allowed a minute here, so that a close well inside that is the budget's.
            var options = new GateOptions
            {
                Port = 0,
                Timeout = TimeSpan.FromSeconds(60),
                Budget = TimeSpan.FromSeconds(2),
            };
            var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                // All of a hello but its last byte, which the reader will wait for. Most of a
                // minute of dribble against a two second budget, so the close is the budget's and
                // not the end of the bytes, and the bound below leaves a loaded machine many
                // seconds to be late in closing it.
                var hello = ClientHelloBuilder.WithName(_first.NewName());
                var opening = new byte[hello.Length - 1];
                Array.Copy(hello, opening, opening.Length);

                var spent = Stopwatch.StartNew();

                var reply = GateClient.DribbleRaw(listener.Port, opening, TimeSpan.FromMilliseconds(250));

                spent.Stop();

                Assert.That(reply.Answered, Is.False);
                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)),
                    "the read waiting for the rest of the record is bounded by the budget and nothing else");
                Assert.That(_log.Errors, Is.Empty,
                    "a peer given up on is not a failure of the plugin's");
            }
            finally
            {
                listener.Dispose();
            }
        }

        [Test]
        public void A_peer_that_has_named_nothing_is_cut_off_before_the_budget_it_never_earned()
        {
            // A connection slot is taken when the socket is accepted, before anything is known
            // about the peer, so what a peer holding no gate key, no certificate and no invitation
            // costs is how long it can keep one. Until a name resolves nothing is owed but a
            // ClientHello, which a client sends in one packet, so that phase is measured against a
            // single read rather than against the span a whole conversation is worth. A budget far
            // longer than the timeout is what tells the two apart.
            var options = new GateOptions
            {
                Port = 0,
                Timeout = TimeSpan.FromSeconds(1),
                Budget = TimeSpan.FromSeconds(30),
            };

            var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log);

            try
            {
                Assert.That(listener.Start(), Is.True);

                // A byte every half second satisfies every read, so no read ever times out and only
                // a deadline ends this: all of a hello but its last byte, which takes longer to
                // send than the budget of thirty seconds lasts, so a connection measured against
                // the budget outlives the assertion and one measured against a read does not. The
                // bound leaves a loaded machine many seconds to be late in closing it and is still
                // ten short of the budget. A read that times out because the machine was slow to
                // send a byte ends the connection early too, which can only let this pass.
                var hello = ClientHelloBuilder.WithName(_first.NewName());
                var opening = new byte[hello.Length - 1];
                Array.Copy(hello, opening, opening.Length);

                var spent = Stopwatch.StartNew();

                var reply = GateClient.DribbleRaw(listener.Port, opening, TimeSpan.FromMilliseconds(500));

                spent.Stop();

                Assert.That(reply.Answered, Is.False);
                Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)),
                    "a peer that named nothing held the slot for a budget it had not earned");
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
            using (var certificate = _first.IssueAdmittedClient())
            {
                GateClient.Get(_listener.Port, _first.NewName(), certificate, GateOperations.StatusPath);
                GateClient.Get(_listener.Port, "localhost", certificate, GateOperations.StatusPath);
                GateClient.SendRaw(_listener.Port, new byte[] { 1, 2, 3 });
            }

            Assert.That(_log.Errors, Is.Empty, string.Join("; ", _log.Errors.Select(e => e.Message)));
        }

        /// <summary>
        /// A handler that takes longer than the connection budget, which is what a write to a
        /// large database on slow storage is.
        /// </summary>
        private sealed class SleepingHandler : IGateHandler
        {
            private readonly TimeSpan _long;

            public SleepingHandler(TimeSpan howLong)
            {
                _long = howLong;
            }

            public int Calls { get; private set; }

            public void Handle(IGateContext context)
            {
                Calls++;
                Thread.Sleep(_long);

                // The answer a create gives, because that is the one whose loss costs a client an
                // identifier it can never ask for again.
                context.Respond(201, "{\"uuid\":\"abcd\"}");
            }
        }
    }
}
