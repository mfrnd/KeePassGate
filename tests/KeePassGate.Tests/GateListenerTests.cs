using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
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
        [NonParallelizable]
        public void A_port_that_could_not_be_taken_leaves_no_socket_behind()
        {
            // A `TcpListener` makes its socket as it is constructed, and a `Start` refused for a
            // port already taken leaves it open, since the bind that failed is outside what `Start`
            // unwinds; `Stop` closes it and makes another. Dropped with nothing closing them, each
            // refusal was a handle the password manager held until a collection, so this runs
            // where nothing collects, and alone, since the count it reads is the process's.
            const int refusals = 64;
            var refused = Enumerable.Range(0, refusals)
                .Select(i => new GateListener(
                    new GateOptions { Port = _listener.Port },
                    new GateResolver(_targets, _log),
                    GateOperations.Router("9.9.9"),
                    _log))
                .ToList();

            var before = Handles();
            Assume.That(GC.TryStartNoGCRegion(4 * 1024 * 1024), Is.True, "a region with no collection");
            try
            {
                foreach (var listener in refused)
                {
                    Assert.That(listener.Start(), Is.False, "the port is already this fixture's");
                }
            }
            finally
            {
                if (System.Runtime.GCSettings.LatencyMode == System.Runtime.GCLatencyMode.NoGCRegion)
                {
                    GC.EndNoGCRegion();
                }
            }

            var after = Handles();

            Assert.That(after - before, Is.LessThan(refusals / 2),
                refusals + " refusals left " + (after - before) + " handles open");
        }

        /// <summary>How many handles the test process holds right now.</summary>
        private static int Handles()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return process.HandleCount;
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
            var material = _first.IssueAdmittedClientKey();

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
        public void A_run_of_names_nothing_answers_to_writes_one_line_and_each_reason_its_own()
        {
            // A name no open database answers to costs a peer one packet and proves nothing, so a
            // line for each let a single loop of them fill the plugin's log at its bound and push
            // out the file kept before it in under two minutes. The first of a run is written and
            // the rest are counted into the next, as for refusals above the ceiling, and a run of
            // one reason hides none of the others.
            var forged = ClientHelloBuilder.WithName(SniName.For(new byte[32]));
            for (var i = 0; i < 4; i++)
            {
                GateClient.SendRaw(_listener.Port, forged);
            }

            GateClient.SendRaw(_listener.Port, new ClientHelloBuilder().Build());

            Assert.That(Waited(() => Drops(GateOutcome.NoName) == 1), Is.True,
                "a nameless hello after a run of unanswered names was not written");
            Assert.That(Drops(GateOutcome.NoMatch), Is.EqualTo(1),
                "four connections naming nothing wrote a line each, so a peer that proves nothing writes the log");
        }

        [Test]
        public void A_paced_line_is_written_once_an_interval_and_counts_what_it_did_not_write()
        {
            var line = new GateListener.PacedLine(TimeSpan.FromMinutes(1));
            int since;

            Assert.That(line.Due(TimeSpan.FromSeconds(10), out since), Is.True, "the first is written at once");
            Assert.That(since, Is.Zero);
            Assert.That(line.Due(TimeSpan.FromSeconds(20), out since), Is.False);
            Assert.That(line.Due(TimeSpan.FromSeconds(69), out since), Is.False);
            Assert.That(line.Due(TimeSpan.FromSeconds(70), out since), Is.True, "and the next a whole interval later");
            Assert.That(since, Is.EqualTo(2), "carrying the two that were not written");
            Assert.That(line.Due(TimeSpan.FromSeconds(71), out since), Is.False);
        }

        /// <summary>How many lines of this fixture's log tell of a drop for this reason.</summary>
        private int Drops(GateOutcome outcome)
        {
            var start = "dropped a connection: " + outcome.ToString().ToLowerInvariant();
            return _log.Entries.Count(entry => entry.Message.StartsWith(start, StringComparison.Ordinal));
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
                var unreadable = new CertifiedKey(broken.Host.Certificate, broken.Host.PrivateKey);
                typeof(CertifiedKey)
                    .GetField("_certificateDer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(unreadable, new byte[] { 1, 2, 3 });
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
        public void An_IO_failure_resolving_a_name_is_counted_rather_than_put_down_to_the_peer()
        {
            // Resolving reads no stream, so an `IOException` out of it is this side's: Bouncy
            // Castle raises one for stored bytes that will not parse, and the connection's catch
            // read it as a client that hung up, at Info, with nothing counted.
            using (var authority = TestAuthority.New())
            {
                var log = new QueueLog();
                var incidents = new KeePassGate.Diagnostics.GateIncidents(log);
                var targets = new FakeGateTargets(authority) { Throws = new IOException("unknown tag 13 encountered") };
                using (var listener = new GateListener(
                    new GateOptions { Port = 0 },
                    new GateResolver(targets, log),
                    GateOperations.Router("9.9.9"),
                    incidents,
                    log))
                {
                    Assert.That(listener.Start(), Is.True);

                    var reply = GateClient.Get(listener.Port, authority.NewName(), null, GateOperations.StatusPath);

                    Assert.That(reply.Answered, Is.False, "nothing is served under a name that did not resolve");
                    Assert.That(Waited(() => incidents.Count == 1), Is.True, string.Join("\n", log.Lines));
                    Assert.That(incidents.Recent.Single(), Does.Contain("a name could not be resolved"));
                    Assert.That(log.Lines.Any(line => line.Contains("a connection ended early")), Is.False,
                        string.Join("\n", log.Lines));
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

        [Test]
        public void A_connection_closed_before_its_thread_began_is_logged_as_that_and_counted_as_nothing()
        {
            // The test above pins the framework and `Opened`; this pins the thread that uses it,
            // whose own reading of the timeouts was what counted a lock as a defect. Served here,
            // on a connection already closed, because nothing a test can time puts a close
            // between the accept and the thread's first line.
            using (var authority = TestAuthority.New())
            {
                var log = new QueueLog();
                var incidents = new KeePassGate.Diagnostics.GateIncidents(log);
                var accepting = new TcpListener(IPAddress.Loopback, 0);
                accepting.Start();

                try
                {
                    using (var listener = new GateListener(
                        new GateOptions { Port = 0 },
                        new GateResolver(new FakeGateTargets(authority), log),
                        GateOperations.Router("9.9.9"),
                        incidents,
                        log))
                    using (var peer = new TcpClient())
                    {
                        peer.Connect(IPAddress.Loopback, ((IPEndPoint)accepting.LocalEndpoint).Port);
                        var accepted = accepting.AcceptTcpClient();
                        accepted.Close();

                        listener.Converse(accepted);

                        Assert.That(incidents.Count, Is.Zero, string.Join("\n", log.Lines));
                        Assert.That(log.Lines, Has.Member("INFO a connection was closed before it was served"));
                    }
                }
                finally
                {
                    accepting.Stop();
                }
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
                    _listener.Port, _first.NewName(), certificate,
                    "POST /thing HTTP/1.1\r\nHost: gate\r\nTransfer-Encoding: chunked\r\n\r\n");

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
            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
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
        }

        [TestCase("PUT /gate/status|x HTTP/1.1\r\n", 400)]
        [TestCase("POST /gate/status HTTP/2.0\r\n", 505)]
        [TestCase("POST /gate/status HTTP/1.1\r\nBad Name: x\r\n", 400)]
        public void A_head_refused_before_its_length_was_parsed_is_answered_to_a_client_streaming_a_body(
            string start, int status)
        {
            // The target, the version and a header ahead of the length are each refused with the
            // whole head in hand, its length among it. A refusal that announced nothing left the
            // body unread, and the reset that closing over it brings discarded the answer.
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Send(
                    _listener.Port, _first.NewName(), certificate, WithABody(start + "Host: gate\r\n"));

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(status), reply.Raw);
            }
        }

        [Test]
        public void A_head_holding_a_byte_that_is_not_ascii_is_answered_to_a_client_streaming_a_body()
        {
            // Refused before anything in it is decoded, and so before the length was parsed, for
            // the same reason as the cases above. The byte is written as a character here, because
            // the head is encoded a byte for each character up to U+00FF.
            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Send(
                    _listener.Port, _first.NewName(), certificate,
                    WithABody("POST /gate/status HTTP/1.1\r\nHost: gate\r\nX-Note: " + (char)0x80 + "\r\n"));

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(400), reply.Raw);
            }
        }

        [Test]
        public void A_head_over_the_cap_is_answered_with_a_431_the_client_can_read()
        {
            // Long enough to run several TLS records past the record the reader stopped in at the
            // cap, so the answer is read only if the rest of the head is taken off the wire, and
            // the body it announced after it.
            var padding = new StringBuilder();
            for (var i = 0; i < 1024; i++)
            {
                padding.Append("X-Padding-").Append(i).Append(": ").Append('p', 48).Append("\r\n");
            }

            using (var certificate = _first.IssueAdmittedClient())
            {
                var reply = GateClient.Send(
                    _listener.Port, _first.NewName(), certificate,
                    WithABody("POST /gate/status HTTP/1.1\r\nHost: gate\r\n" + padding));

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(431), reply.Raw);
            }
        }

        /// <summary>
        /// A head finished with a length and the body it announces, as bytes, a byte for each
        /// character up to U+00FF so that a test can put a byte that is not ASCII in the head.
        /// </summary>
        /// <remarks>
        /// Big enough that the client is still writing when the gate refuses it, which is what
        /// makes the reset certain rather than a race a small body sometimes wins.
        /// </remarks>
        internal static byte[] WithABody(string start)
        {
            var body = new string('x', 512 * 1024);
            return Encoding.GetEncoding(28591).GetBytes(
                start + "Content-Length: " + body.Length + "\r\n\r\n" + body);
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
            using (var second = new GateListener(
                new GateOptions { Port = _listener.Port },
                new GateResolver(_targets, _log),
                GateOperations.Router("9.9.9"),
                _log))
            {
                // KeePass has to keep working when the port is busy, so this is a false rather than
                // an exception on the way out of Initialize.
                Assert.That(second.Start(), Is.False);
                Assert.That(_log.Errors, Is.Not.Empty);
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
        public void A_stop_after_the_accept_loop_ended_still_closes_what_it_accepted()
        {
            // An accept loop that fails retires the port itself, and the stop that followed found
            // nothing listening and returned before closing anything, so a connection accepted
            // before the failure was served on past the plugin's teardown. A read here may block
            // for a minute, and so may the phase before the name resolves, so a connection this
            // closes is the stop's.
            var options = new GateOptions
            {
                Port = 0,
                Timeout = TimeSpan.FromSeconds(60),
                Budget = TimeSpan.FromSeconds(60),
            };

            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
            {
                Assert.That(listener.Start(), Is.True);

                using (var peer = new TcpClient())
                {
                    peer.Connect(IPAddress.Loopback, listener.Port);
                    Assert.That(Waited(() => Conversations(listener) == 1), Is.True, "the connection was never taken");

                    Underneath(listener).Stop();
                    Assert.That(Waited(() => !listener.IsListening), Is.True, "the accept loop did not end");

                    listener.Stop();

                    peer.ReceiveTimeout = 30000;
                    var spent = Stopwatch.StartNew();
                    int read;
                    try
                    {
                        read = peer.GetStream().ReadByte();
                    }
                    catch (IOException)
                    {
                        read = -1;
                    }

                    spent.Stop();

                    Assert.That(read, Is.EqualTo(-1), "nothing arrives on a connection the gate closed");
                    Assert.That(spent.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)),
                        "the connection outlived the stop and ended only at a deadline of its own");
                }
            }
        }

        [Test]
        public void More_connections_than_it_will_serve_are_closed_rather_than_queued()
        {
            // The gate borrows the password manager's thread pool, so a connection that waits is a
            // thread KeePass is not using. The ordinary limits otherwise, so the one connection
            // served holds its slot for as long as a silent peer can, which is far longer than a
            // loaded machine takes to accept the others.
            var options = new GateOptions { Port = 0, MaxConnections = 1 };
            using (var listener = new GateListener(options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
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
            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
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
        }

        [Test]
        public void A_peer_that_dribbles_its_body_is_cut_off_at_the_budget()
        {
            // The budget ends with the request, which is the whole of it including the body: it is
            // dropped, or extended for a pairing or a renewal, only once the body has been read.
            // Dropped as the head arrived, it would let a peer that had proved nothing announce a
            // body and feed it a byte inside each read window, holding a thread for as long as the
            // body cap allows. The head goes at once here and the body a byte every quarter of a
            // second, a minute of it, against a budget of five seconds that is generous because
            // the handshake is inside it.
            var options = new GateOptions { Port = 0, Budget = TimeSpan.FromSeconds(5) };
            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
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
            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), slow, _log))
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
        }

        [Test]
        public void An_answer_written_as_it_is_sent_reaches_a_client_whole_over_the_socket()
        {
            // The answer to a read of an entry or of a field, over real TLS: several chunks,
            // each a record, with text beyond ASCII in the last of them. What the client takes
            // out of the chunks is the text the handler wrote, and nothing else.
            var text = "{\"notes\":\"" + new string('n', 5 * 16 * 1024) + " naïve ✓\"}";
            var handler = new WritingHandler(writer => writer.Write(text));
            using (var listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, _log), handler, _log))
            {
                Assert.That(listener.Start(), Is.True);

                var reply = GateClient.Get(listener.Port, _first.NewName(), null, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(reply.Status, Is.EqualTo(200));
                Assert.That(reply.Raw.Contains("Transfer-Encoding: chunked\r\n"), Is.True, "the answer names no chunks");
                Assert.That(reply.Raw.Contains("Content-Length"), Is.False, "an answer in chunks names a length");
                Assert.That(reply.Body == text, Is.True, "the chunks carry other text than the handler wrote");
            }
        }

        [Test]
        public void Content_that_fails_part_way_is_counted_and_the_client_is_handed_no_whole_answer()
        {
            // What the content of an answer throws has no answer left to give: the status is on
            // the wire. It reaches the connection's thread, which counts it where the operator
            // sees it and closes the connection, and the client finds an answer with no last
            // chunk, or no answer at all where the close reached it first.
            var log = new QueueLog();
            var incidents = new KeePassGate.Diagnostics.GateIncidents(log);
            var handler = new WritingHandler(writer =>
            {
                writer.Write("{\"half\":");
                throw new InvalidOperationException("an invented read that fails");
            });

            using (var listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, log), handler, incidents, log))
            {
                Assert.That(listener.Start(), Is.True);

                var reply = GateClient.Get(listener.Port, _first.NewName(), null, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.False, "an answer cut short was taken for a whole answer: " + reply.Raw);
                Assert.That(Waited(() => incidents.Count == 1), Is.True, string.Join("\n", log.Lines));
                Assert.That(incidents.Recent.Single(), Does.Contain("a connection failed"));
            }
        }

        [TestCase("\r\n")]
        [TestCase("GET /gate/status HTTP/1.1\r\nHost: gate\r\n\r\n")]
        public void A_client_that_sends_more_after_its_request_still_reads_the_answer(string more)
        {
            // A socket closed with the peer's bytes unread is answered by the stack with a reset,
            // which discards whatever reached the peer and was not read yet. A record sent after
            // the request, a line break written on its own or a second request pipelined behind
            // the first, was never read, so the close was a reset and a client reading a moment
            // late lost the answer, a landed write's `201` as much as this.
            var handler = new ThreadHandler();
            using (var listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, _log), handler, _log))
            {
                Assert.That(listener.Start(), Is.True);

                string answer;
                using (var tcp = new TcpClient())
                {
                    tcp.Connect(IPAddress.Loopback, listener.Port);
                    tcp.ReceiveTimeout = 10000;
                    using (var tls = new System.Net.Security.SslStream(
                        tcp.GetStream(), false, delegate { return true; }))
                    {
                        tls.AuthenticateAsClient(
                            _first.NewName(), null, System.Security.Authentication.SslProtocols.Tls13,
                            false);

                        var records = new[] { "GET /gate/status HTTP/1.1\r\nHost: gate\r\n\r\n", more };
                        foreach (var bytes in records.Select(record => Encoding.ASCII.GetBytes(record)))
                        {
                            tls.Write(bytes, 0, bytes.Length);
                            tls.Flush();
                        }

                        // Long enough for the gate to have answered and closed before this reads.
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                        answer = ReadAll(tls);
                    }
                }

                Assert.That(answer, Does.StartWith("HTTP/1.1 200 "), "the answer was lost to a reset");
                Assert.That(answer, Does.EndWith("{}"), "and was read whole");
            }
        }

        /// <summary>What a client read before the connection ended, or why it ended without a word.</summary>
        private static string ReadAll(System.IO.Stream stream)
        {
            using (var read = new MemoryStream())
            {
                var buffer = new byte[4096];
                try
                {
                    int got;
                    while ((got = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        read.Write(buffer, 0, got);
                    }
                }
                catch (IOException error)
                {
                    return read.Length > 0 ? Encoding.ASCII.GetString(read.ToArray()) : "no answer: " + error.Message;
                }

                return Encoding.ASCII.GetString(read.ToArray());
            }
        }

        [Test]
        public void An_answer_its_peer_does_not_read_is_cut_off_at_the_budget()
        {
            // How fast an answer is read is the peer's doing, as the pace of its request was. The
            // socket's send timeout bounds one send, which is one TLS record, so a peer reading a
            // record inside each timeout held a thread and a slot for as long as the answer
            // lasted. Here a read may block for two minutes and the budget is five seconds, which
            // is generous because the handshake is inside it, and nothing reads the answer, which
            // is far larger than the buffers between the two ends: the write is closed under at
            // the budget, or at the end of a send timeout this waits a quarter of.
            var options = new GateOptions
            {
                Port = 0,
                Timeout = TimeSpan.FromSeconds(120),
                Budget = TimeSpan.FromSeconds(5),
            };

            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), new LargeHandler(16 * 1024 * 1024), _log))
            {
                Assert.That(listener.Start(), Is.True);

                using (var tcp = new TcpClient())
                {
                    tcp.Connect(IPAddress.Loopback, listener.Port);
                    using (var tls = new System.Net.Security.SslStream(
                        tcp.GetStream(), false, delegate { return true; }))
                    {
                        tls.AuthenticateAsClient(
                            _first.NewName(), null, System.Security.Authentication.SslProtocols.Tls13,
                            false);
                        var request = Encoding.ASCII.GetBytes(
                            "GET " + GateOperations.StatusPath + " HTTP/1.1\r\nHost: gate\r\n\r\n");
                        tls.Write(request, 0, request.Length);
                        tls.Flush();

                        Assert.That(
                            Waited(() => _log.Warnings.Any(w => w.Message.Contains("could not be written"))),
                            Is.True, "an answer nobody read held its connection past the budget");
                    }
                }
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
            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
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

            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
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

        [Test]
        public void A_connection_is_served_in_the_culture_of_the_thread_that_made_the_listener()
        {
            // KeePass's dictionaries look a key up in the culture of the thread asking, and the
            // plugin makes the listener on KeePass's thread, which runs in its translation's
            // culture. A new thread starts in the regional format's, and a tree sorted in either
            // culture and searched in the other misses keys that are there: a Danish KeePass on an
            // English format refused an admitted client whose record's key began `aa`. So a
            // connection has to be served in the culture the listener was made in.
            var made = CultureInfo.GetCultureInfo("da-DK");
            Assume.That(CultureInfo.CurrentCulture.Name, Is.Not.EqualTo(made.Name),
                "a machine whose own format is Danish cannot tell the two apart");

            var handler = new ThreadHandler();
            var thread = Thread.CurrentThread;
            var own = thread.CurrentCulture;
            GateListener listener;

            thread.CurrentCulture = made;
            try
            {
                listener = new GateListener(
                    new GateOptions { Port = 0 }, new GateResolver(_targets, _log), handler, _log);
            }
            finally
            {
                thread.CurrentCulture = own;
            }

            using (listener)
            {
                Assert.That(listener.Start(), Is.True);

                var reply = GateClient.Get(listener.Port, _first.NewName(), null, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(handler.Culture, Is.EqualTo(made.Name),
                    "the request was served in the culture its thread started in rather than KeePass's");
            }
        }

        [Test]
        public void Closing_one_database_s_connections_leaves_another_s_to_be_answered()
        {
            // The operator's ruling R1, which a close and an unlock carry out. Every connection
            // used to close, so closing or unlocking a second database reset every request to the
            // first, a pairing waiting on its dialog among them.
            _targets.Add(_second);
            var handler = new HeldHandler();
            using (var listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, _log), handler, _log))
            {
                Assert.That(listener.Start(), Is.True);

                var first = System.Threading.Tasks.Task.Run(
                    () => GateClient.Get(listener.Port, _first.NewName(), null, GateOperations.StatusPath));
                var second = System.Threading.Tasks.Task.Run(
                    () => GateClient.Get(listener.Port, _second.NewName(), null, GateOperations.StatusPath));
                Assert.That(Waited(() => handler.Entered == 2), Is.True, "both requests reached the handler");

                Assert.That(listener.Close(new[] { _second.DatabaseId }, "a database was closed"), Is.EqualTo(1));
                handler.Release();

                Assert.That(first.Result.Status, Is.EqualTo(200), first.Result.Failure);
                Assert.That(second.Result.Answered, Is.False, "the closed database's request was answered");
            }
        }

        [Test]
        public void A_name_that_resolves_as_its_database_closes_is_not_served()
        {
            // A close finds a database's connections by the authority each connection's name
            // resolved to, and a name that resolved just before the close is recorded just after
            // it: missed, it would go on reading a database KeePass had emptied.
            var handler = new ThreadHandler();
            using (var resolving = new ManualResetEventSlim())
            using (var resume = new ManualResetEventSlim())
            using (var listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, _log), handler, _log))
            {
                _targets.Asking = delegate
                {
                    resolving.Set();
                    resume.Wait(TimeSpan.FromSeconds(30));
                };

                try
                {
                    Assert.That(listener.Start(), Is.True);

                    var reply = System.Threading.Tasks.Task.Run(
                        () => GateClient.Get(listener.Port, _first.NewName(), null, GateOperations.StatusPath));
                    Assert.That(resolving.Wait(TimeSpan.FromSeconds(10)), Is.True, "the name was never asked about");

                    Assert.That(listener.Close(new[] { _first.DatabaseId }, "a database was closed"), Is.Zero,
                        "nothing had resolved to it yet");
                    resume.Set();

                    Assert.That(reply.Result.Answered, Is.False, reply.Result.ToString());
                    Assert.That(handler.OnThePool, Is.Null, "the request was served");
                    Assert.That(_log.Entries.Select(entry => entry.Message), Has.Some.Contains("closed as it resolved"));
                }
                finally
                {
                    // Taken off before the events go, so nothing still resolving waits on one.
                    _targets.Asking = null;
                    resume.Set();
                }
            }
        }

        [Test]
        public void A_connection_is_served_on_a_thread_of_its_own_rather_than_the_pool()
        {
            // KeePass waits on the thread pool from its own thread: its managed Argon2 queues its
            // lanes there and blocks a save on them. A conversation holds its thread through every
            // wait it makes, so a conversation served from the pool is a thread a save of KeePass's
            // can be left waiting for.
            var handler = new ThreadHandler();
            using (var listener = new GateListener(
                new GateOptions { Port = 0 }, new GateResolver(_targets, _log), handler, _log))
            {
                Assert.That(listener.Start(), Is.True);

                var reply = GateClient.Get(listener.Port, _first.NewName(), null, GateOperations.StatusPath);

                Assert.That(reply.Answered, Is.True, reply.Failure);
                Assert.That(handler.OnThePool, Is.False, "the request was served on a thread of the pool");
            }
        }

        [Test]
        public void Connections_that_send_nothing_leave_the_thread_pool_free()
        {
            // The pool hands out as many threads at once as its minimum, which is the number of
            // processors, and adds about a thread a second past that. Served from it, connections
            // that sent nothing held every item queued behind them until they timed out, KeePass's
            // own work among them: thirty-one held it for ten seconds. So this opens more of them
            // than the minimum and asks the pool for a thread while they are all still waiting.
            int workers;
            int ports;
            ThreadPool.GetMinThreads(out workers, out ports);
            var silent = workers + 8;

            var options = new GateOptions
            {
                Port = 0,
                MaxConnections = silent + 1,
                Timeout = TimeSpan.FromSeconds(30)
            };

            using (var listener = new GateListener(
                options, new GateResolver(_targets, _log), GateOperations.Router("9.9.9"), _log))
            {
                Assert.That(listener.Start(), Is.True);

                var peers = new List<TcpClient>();
                try
                {
                    for (var i = 0; i < silent; i++)
                    {
                        var peer = new TcpClient();
                        peers.Add(peer);
                        peer.Connect("127.0.0.1", listener.Port);
                    }

                    Assert.That(Waited(() => Conversations(listener) == silent), Is.True,
                        Conversations(listener) + " of " + silent + " connections were taken");

                    using (var ran = new ManualResetEventSlim())
                    {
                        Assert.That(ThreadPool.QueueUserWorkItem(delegate { ran.Set(); }), Is.True);
                        Assert.That(ran.Wait(TimeSpan.FromSeconds(3)), Is.True,
                            "an item queued on the pool behind " + silent + " silent connections did not run");
                    }
                }
                finally
                {
                    foreach (var peer in peers)
                    {
                        peer.Close();
                    }
                }
            }
        }

        /// <summary>How many conversations the listener counts as holding a slot.</summary>
        private static int Conversations(GateListener listener)
        {
            var field = typeof(GateListener).GetField(
                "_conversations", BindingFlags.Instance | BindingFlags.NonPublic);
            return (int)field.GetValue(listener);
        }

        /// <summary>
        /// A handler that notes the thread it was called on: its culture, and whether it is the
        /// pool's.
        /// </summary>
        private sealed class ThreadHandler : IGateHandler
        {
            private string _culture;
            private int _onThePool = -1;

            public string Culture
            {
                get { return Volatile.Read(ref _culture); }
            }

            /// <summary>Null until a request arrives.</summary>
            public bool? OnThePool
            {
                get
                {
                    var noted = Volatile.Read(ref _onThePool);
                    return noted < 0 ? (bool?)null : noted == 1;
                }
            }

            public void Handle(IGateContext context)
            {
                Volatile.Write(ref _culture, CultureInfo.CurrentCulture.Name);
                Volatile.Write(ref _onThePool, Thread.CurrentThread.IsThreadPoolThread ? 1 : 0);
                context.Respond(200, "{}");
            }
        }

        /// <summary>A handler that holds every request until the test lets them go.</summary>
        private sealed class HeldHandler : IGateHandler
        {
            private readonly ManualResetEventSlim _released = new ManualResetEventSlim();
            private int _entered;

            public int Entered
            {
                get { return Volatile.Read(ref _entered); }
            }

            public void Release()
            {
                _released.Set();
            }

            public void Handle(IGateContext context)
            {
                Interlocked.Increment(ref _entered);
                _released.Wait(TimeSpan.FromSeconds(30));
                context.Respond(200, "{}");
            }
        }

        /// <summary>A handler whose answer is one string member this many characters long.</summary>
        private sealed class LargeHandler : IGateHandler
        {
            private readonly string _answer;

            public LargeHandler(int length)
            {
                _answer = "{\"a\":\"" + new string('a', length) + "\"}";
            }

            public void Handle(IGateContext context)
            {
                context.Respond(200, _answer);
            }
        }

        /// <summary>
        /// A handler whose answer is written as it is sent, by what it was handed, as a read
        /// of an entry is answered.
        /// </summary>
        private sealed class WritingHandler : IGateHandler
        {
            private readonly Action<TextWriter> _content;

            public WritingHandler(Action<TextWriter> content)
            {
                _content = content;
            }

            public void Handle(IGateContext context)
            {
                context.Respond(200, _content, null);
            }
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
