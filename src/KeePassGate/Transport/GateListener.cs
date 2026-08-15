using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;

namespace KeePassGate.Transport
{
    /// <summary>
    /// The accept loop, the gate and the TLS: everything that has to happen before a request is a
    /// request.
    /// </summary>
    /// <remarks>
    /// Owned rather than delegated, and `DESIGN.md` §104 says why: each open database has its
    /// own host certificate, the server has to choose one before it has seen anything but the
    /// ClientHello, and no library on this platform can select one by name. So the hello is read
    /// off the socket, the name is resolved, a certificate is chosen, and the bytes already read
    /// are replayed into the TLS server.
    ///
    /// **The TLS is managed code rather than SChannel**, because SChannel will not serve with a key
    /// that has not been written to the user's profile. `GateTlsServer` says the rest.
    ///
    /// Two refusals happen here and nowhere else, both silent by design. A connection that is not
    /// TLS is dropped after five bytes. A name that resolves to no open database is dropped with no
    /// response at all, so an attacker who cannot compute a tag learns nothing, and an invitation
    /// that has expired is indistinguishable from one that never existed.
    ///
    /// It binds loopback only. Reaching this from another machine is a tunnel's job.
    /// </remarks>
    public sealed class GateListener : IGateStatus, IConnections, IDisposable
    {
        private readonly GateOptions _options;
        private readonly GateResolver _resolver;
        private readonly IGateHandler _handler;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;
        private readonly object _stateLock = new object();

        /// <summary>How often a run of refusals above the ceiling is reported. See `Refused`.</summary>
        private static readonly TimeSpan RefusalInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Monotonic, because the wall clock can go backwards and would take the warning with it.
        /// </summary>
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private TcpListener _listener;
        private Thread _accepting;
        private volatile bool _running;
        private int _conversations;
        private TimeSpan _saidRefusedAt;
        private bool _saidRefused;
        private int _refusedSince;

        /// <summary>
        /// Every connection being served right now, so that all of them can be closed at once.
        /// </summary>
        /// <remarks>
        /// `PAIRING.md` §308 requires this on **both** lock transitions, and unlock is the
        /// interesting one. Closing at lock is obvious: a socket thread must not be reading a
        /// database the user interface thread is closing. Closing at unlock is about privilege, and
        /// it is the half that is easy to miss: a connection that completed its handshake against
        /// the responder certificate was authenticated against nothing, because validating a client
        /// certificate needs the authority, which was not loaded. Letting it survive into the
        /// unlocked state would promote it to an authority it never proved anything to.
        /// </remarks>
        private readonly HashSet<TcpClient> _live = new HashSet<TcpClient>();

        public GateListener(GateOptions options, GateResolver resolver, IGateHandler handler, IGateLog log)
            : this(options, resolver, handler, null, log)
        {
        }

        public GateListener(
            GateOptions options,
            GateResolver resolver,
            IGateHandler handler,
            GateIncidents incidents,
            IGateLog log)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _options = options;
            _resolver = resolver;
            _handler = handler;
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        /// <summary>The port actually bound, which is not the requested one when that was zero.</summary>
        public int Port { get; private set; }

        public bool IsListening
        {
            get { return _running; }
        }

        /// <summary>
        /// Binds and starts accepting. False when the port is taken, which is reported rather than
        /// thrown: a plugin that cannot listen must still leave KeePass working.
        /// </summary>
        public bool Start()
        {
            lock (_stateLock)
            {
                if (_running)
                {
                    return true;
                }

                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, _options.Port);
                    _listener.Start();
                    Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                }
                catch (SocketException error)
                {
                    _log.Error("could not listen on 127.0.0.1:" + _options.Port, error);
                    _listener = null;
                    return false;
                }

                _running = true;
                _accepting = new Thread(Accept) { IsBackground = true, Name = "KeePassGate accept" };
                _accepting.Start();
            }

            _log.Info("listening on 127.0.0.1:" + Port);
            return true;
        }

        public void Stop()
        {
            Thread accepting;

            lock (_stateLock)
            {
                if (!_running)
                {
                    return;
                }

                accepting = _accepting;
            }

            Retire();

            // What the class remark has always said happens here. `Retire` closes the listener and
            // nothing else: conversations already running kept their target references and ran on
            // to their own deadline, up to the pairing budget, so a pairing could still hand out a
            // certificate after the plugin had been told to stop.
            CloseAll("the gate is stopping");

            if (accepting != null)
            {
                // Long enough for the accept call to notice, short enough that KeePass is never
                // held up by it.
                accepting.Join(TimeSpan.FromSeconds(2));
            }

            _log.Info("stopped listening");
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Closes every connection being served, without stopping the listener. What a lock and an
        /// unlock both do.
        /// </summary>
        /// <remarks>
        /// Closing the socket is what ends the conversation: the thread serving it is blocked in a
        /// read, and it wakes with a failure it already knows how to treat as a client that hung
        /// up. Nothing is joined, because this runs on the user interface thread during a lock and
        /// waiting on a socket thread there is how a password manager stops responding.
        /// </remarks>
        public int CloseAll(string why)
        {
            TcpClient[] closing;

            lock (_stateLock)
            {
                closing = new TcpClient[_live.Count];
                _live.CopyTo(closing);
                _live.Clear();
            }

            foreach (var client in closing)
            {
                try
                {
                    client.Close();
                }
                catch (Exception error) when (Expected.OfConnection(error))
                {
                    // Already gone, which is the outcome being asked for.
                }
            }

            if (closing.Length > 0)
            {
                _log.Info("closed " + closing.Length + " connection" +
                          (closing.Length == 1 ? "" : "s") + ": " + (why ?? "no reason given"));
            }

            return closing.Length;
        }

        private void Accept()
        {
            try
            {
                while (_running)
                {
                    TcpClient client;
                    try
                    {
                        var listener = _listener;
                        if (listener == null)
                        {
                            return;
                        }

                        client = listener.AcceptTcpClient();
                    }
                    catch (SocketException error)
                    {
                        // Stopping looks like this, and so does failing. `Retire` clears
                        // `_running` before it closes the listener, so an accept that fails while
                        // the flag is still set is the case the `finally` below names: one nobody
                        // chose. Saying nothing there was the whole of the report. The gate stops
                        // listening for the rest of the session, and the log, which is the one
                        // place a reason could be, had the reason and threw it away. An accept
                        // refused for want of buffers or of handles is what this is for, and the
                        // socket error code is the whole of what distinguishes one.
                        if (_running)
                        {
                            _log.Error(
                                "the accept loop failed: socket error " + error.SocketErrorCode,
                                error);
                        }

                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }

                    if (!Take(client))
                    {
                        continue;
                    }

                    var accepted = client;

                    // Tracked before it is queued. `Converse` takes it out again in a finally,
                    // and `Stop` closes whatever is still in here, so a connection that was
                    // never added would outlive the listener that accepted it.
                    //
                    // And dropped rather than tracked once the gate has stopped, which is the
                    // same guarantee read from the other end. This thread can already be past
                    // `AcceptTcpClient` when `Stop` runs, and `Stop` empties `_live` once: a
                    // connection registered after that is one nothing will ever close, and it is
                    // served to its own deadline with the listener gone. `Retire` clears
                    // `_running` under this lock before `CloseAll` takes it, so asking here, with
                    // the set, is what closes the window rather than narrowing it.
                    bool stopped;
                    lock (_stateLock)
                    {
                        stopped = !_running;
                        if (!stopped)
                        {
                            _live.Add(accepted);
                        }
                    }

                    if (stopped)
                    {
                        // Nothing will run, so nothing will give the place back.
                        Interlocked.Decrement(ref _conversations);
                        Drop(accepted);
                        return;
                    }

                    if (!ThreadPool.QueueUserWorkItem(delegate { Converse(accepted); }))
                    {
                        // Nothing will run, so nothing will give the place back.
                        Interlocked.Decrement(ref _conversations);
                        lock (_stateLock)
                        {
                            _live.Remove(accepted);
                        }

                        Drop(accepted);
                    }
                }
            }
            finally
            {
                // Whatever ended this thread, nothing is being accepted any more. Said here rather
                // than only in `Stop`, because an accept that fails for a reason nobody chose used
                // to leave `IsListening` answering true forever: the tab reported a gate that was
                // listening, and `Start` returned true without binding anything.
                Retire();
            }
        }

        /// <summary>
        /// Whether there is room for another conversation. A connection past the ceiling is closed
        /// at once rather than queued, because queuing it is what would hold the thread.
        /// </summary>
        private bool Take(TcpClient client)
        {
            if (Interlocked.Increment(ref _conversations) <= _options.MaxConnections)
            {
                return true;
            }

            Interlocked.Decrement(ref _conversations);
            Refused();
            Drop(client);
            return false;
        }

        /// <summary>
        /// Says that a connection was refused above the ceiling, at most once an interval.
        /// </summary>
        /// <remarks>
        /// Every refusal used to write a line of its own, and a refusal costs the peer one packet.
        /// A peer that has proved nothing can hold the slots whatever this says, and then
        /// every further connect is a line, each one opening and closing the log file, for as long
        /// as it cares to keep connecting. Whatever else the log was for is buried, and once the
        /// file reaches its bound, pushed out of it altogether.
        ///
        /// The first refusal of a run is said at once, because that is the one worth reading, and
        /// the rest are counted and reported with the next line that goes out. A run that stops is
        /// missing its tail, which is what the first line being immediate costs. No lock, because
        /// `Take` is reached from the accept thread and from nowhere else.
        /// </remarks>
        private void Refused()
        {
            var now = _clock.Elapsed;
            if (_saidRefused && now - _saidRefusedAt < RefusalInterval)
            {
                _refusedSince++;
                return;
            }

            _log.Warn(
                "refused a connection: " + _options.MaxConnections + " are already being served" +
                (_refusedSince > 0
                    ? ", and " + _refusedSince + " more since the last of these"
                    : string.Empty));

            _saidRefused = true;
            _saidRefusedAt = now;
            _refusedSince = 0;
        }

        /// <summary>
        /// Closes a socket the accept thread is giving up on, without letting the failure out.
        /// </summary>
        /// <remarks>
        /// **The accept thread is a root**, in the sense <see cref="GateIncidents"/> means: it is
        /// this plugin's own thread, nothing is above it, and an exception escaping it is an
        /// unhandled exception in the password manager's process, which is what ends the process.
        /// It is not one of the places that catch everything, and what makes that safe rather than
        /// lucky is that every call it makes is total or filtered: the counter is interlocked, the
        /// set is behind a lock, and the log guards its own writes where it makes them (see
        /// `TraceGateLog`). A bare `Close` was the exception to that. It raises a
        /// `SocketException` like any other socket call, and there were two of them on this
        /// thread with nothing around them.
        ///
        /// Anything added to the accept loop has to keep that true, or the loop needs a catch-all
        /// of its own and <see cref="GateIncidents"/> needs a fifth place in its list.
        /// </remarks>
        private static void Drop(TcpClient client)
        {
            try
            {
                client.Close();
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                // Already gone, which is the outcome being asked for.
            }
        }

        /// <summary>Gives up the port, whether it was asked for or the accept loop simply ended.</summary>
        private void Retire()
        {
            lock (_stateLock)
            {
                _running = false;
                _accepting = null;

                try
                {
                    if (_listener != null)
                    {
                        _listener.Stop();
                    }
                }
                catch (Exception error) when (Expected.OfConnection(error))
                {
                    _log.Error("the listener did not stop cleanly", error);
                }
                finally
                {
                    _listener = null;
                }
            }
        }

        private void Converse(TcpClient client)
        {
            try
            {
                using (client)
                {
                    // Registered by the accept loop before this thread starts, and forgotten here
                    // however it ends, so a connection that finished normally is not closed twice.
                    // Outside the `Deadline`, because a timer that will not start would otherwise
                    // leave this client registered for the life of the process.
                    try
                    {
                        // The whole connection, not one read of it. `Timeout` on the socket bounds
                        // a single read, so a peer sending one byte just inside it holds a thread
                        // for as long as the caps allow, which is hours. Closing the socket is what
                        // ends that peer's connection, and the failure it raises is one the paths
                        // below already expect.
                        using (var deadline = new Deadline(client, _options.Budget))
                        {
                            var timeout = (int)_options.Timeout.TotalMilliseconds;
                            client.ReceiveTimeout = timeout;
                            client.SendTimeout = timeout;
                            client.NoDelay = true;

                            var raw = client.GetStream();

                            var peek = ClientHelloReader.Read(raw);
                            if (peek == null)
                            {
                                // Not TLS. Five bytes, and nothing said back.
                                return;
                            }

                            // In a `using`, because a resolved target arrives holding a reference
                            // taken for this connection. Giving it back here is what lets the cache
                            // release the material the moment nothing is serving from it.
                            using (var resolution = _resolver.Resolve(peek.ServerName))
                            {
                                if (!resolution.IsResolved)
                                {
                                    _log.Info(
                                        "dropped a connection: " +
                                        resolution.Outcome.ToString().ToLowerInvariant());
                                    return;
                                }

                                // Not while locked: the name a pairing resolves by survives the lock,
                                // so a holder of a still-valid invitation could otherwise hold every
                                // connection slot for the whole pairing budget against a database that
                                // will only ever answer `503`, with no dialog and nobody asked.
                                if (resolution.IsPairing && !resolution.Target.IsLocked)
                                {
                                    // The caller named an invitation, so this connection is about to
                                    // wait on the operator rather than on the wire. The ordinary budget
                                    // would close the socket while the dialog is still up, and
                                    // everything after that point still happens: the invitation is
                                    // spent and the record is written and saved, with nothing left to
                                    // hand the certificate back on.
                                    deadline.Extend(_options.PairingBudget);
                                }

                                Serve(raw, peek, resolution, Peer(client));
                            }
                        }
                    }
                    finally
                    {
                        lock (_stateLock)
                        {
                            _live.Remove(client);
                        }
                    }
                }
            }
            catch (Exception error) when (Expected.OfConnection(error) || error is CryptographicException)
            {
                // A client that left, and a database whose material was released while this
                // connection was still holding it: closing a file is what raises the second, and
                // neither is a defect. Named rather than filtered through `OfCryptography`, which
                // also covers `ArgumentException` and would swallow a real one.
                _log.Info("a connection ended early: " + error.Message);
            }
            catch (Exception error)
            {
                // One of the catch-alls. This runs on a thread pool thread, so an exception
                // that escapes it is an unhandled exception in the password manager's process, and
                // the process is what would end. A client chooses when this happens, which is why
                // what it caught is counted where the operator can see it.
                _incidents.Record("a connection failed", error);
            }
            finally
            {
                Interlocked.Decrement(ref _conversations);
            }
        }

        /// <summary>
        /// Closes a connection once it has had long enough, whatever it is blocked on.
        /// </summary>
        /// <remarks>
        /// A timer rather than a poll, and one object per connection, because the reads underneath
        /// are blocking and there is nothing to check between them.
        /// </remarks>
        private sealed class Deadline : IDisposable
        {
            private readonly Timer _timer;

            public Deadline(TcpClient client, TimeSpan budget)
            {
                _timer = new Timer(
                    delegate
                    {
                        try
                        {
                            client.Close();
                        }
                        catch (Exception error) when (Expected.OfConnection(error))
                        {
                            // A root as well, and for the same reason as the accept thread: a
                            // timer callback runs on a thread pool thread with nothing above it,
                            // so this filter is the whole of what stands between a socket that
                            // will not close and the end of KeePass. Closing is all this does,
                            // and everything closing raises is in the filter.
                        }
                    },
                    null,
                    budget,
                    Timeout.InfiniteTimeSpan);
            }

            /// <summary>
            /// Gives the connection a longer budget, counted again from now.
            /// </summary>
            /// <remarks>
            /// For the one exchange that waits on a person. The budget exists to stop a peer
            /// holding a thread by feeding it slowly, and a pairing sitting in front of an approval
            /// dialog is neither slow nor a peer's doing, so it is measured against a budget of its
            /// own. Only ever called after the name resolved to an invitation, which is a secret the
            /// caller had to hold, so nothing an unknown peer can say reaches this.
            /// </remarks>
            public void Extend(TimeSpan budget)
            {
                _timer.Change(budget, Timeout.InfiniteTimeSpan);
            }

            public void Dispose()
            {
                _timer.Dispose();
            }
        }

        /// <summary>
        /// What the pairing routes need and nothing above the transport can work out: the
        /// invitation, the certificate this connection was actually served with, and the nonce out
        /// of the name it arrived on. Null on every connection that named a database.
        /// </summary>
        private static PairingConnection Pairing(
            GateResolution resolution, ClientHelloPeek peek, IGateTarget target, string peer)
        {
            if (!resolution.IsPairing)
            {
                return null;
            }

            byte[] nonce;
            byte[] tag;
            if (!SniName.TryParse(peek.ServerName, out nonce, out tag))
            {
                // Unreachable: the name resolved, so it parsed. Refusing rather than assuming is
                // still the cheaper half of being wrong about that.
                return null;
            }

            return new PairingConnection(
                resolution.Invitation, target, PairingProof.Binding(target.Host.CertificateDer), nonce, peer);
        }

        private static string Peer(TcpClient client)
        {
            try
            {
                var endpoint = client.Client.RemoteEndPoint;
                return endpoint == null ? string.Empty : endpoint.ToString();
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                // A socket that has already gone has no address to report.
                return string.Empty;
            }
        }

        private void Serve(System.IO.Stream raw, ClientHelloPeek peek, GateResolution resolution, string peer)
        {
            var target = resolution.Target;
            var replayed = new PrependingStream(peek.Bytes, raw);
            var server = new GateTlsServer(new BcTlsCrypto(new SecureRandom()), target.Host);
            var protocol = new TlsServerProtocol(replayed);

            try
            {
                try
                {
                    protocol.Accept(server);
                }
                catch (TlsFatalAlert alert)
                {
                    if (server.Failure != null)
                    {
                        // Part of the connection catch-all, kept here rather than left to it, for
                        // the reason `GateTlsServer.Failure` gives: Bouncy Castle made an alert of
                        // this side's own failure, and an alert is an `IOException`, which every
                        // filter on this thread reads as a peer that went away. A peer's own
                        // failure leaves nothing there, and is logged below as it always was.
                        _incidents.Record("the handshake failed on this side", server.Failure);
                        return;
                    }

                    // A client offering something we do not is refused here, and told so in the
                    // alert.
                    _log.Info("a handshake failed: " + alert.Message);
                    return;
                }
                catch (Exception error) when (Expected.OfConnection(error))
                {
                    if (server.Failure != null)
                    {
                        // The same, for one that arrived as an `IOException` rather than an alert.
                        _incidents.Record("the handshake failed on this side", server.Failure);
                        return;
                    }

                    // Everything a handshake can fail with that is not an alert: a client that
                    // hung up, a stream that closed, bytes that were never TLS.
                    _log.Info("a handshake failed: " + error.Message);
                    return;
                }

                var client = ClientCredentials.For(server.PresentedCertificate, target, _log);
                if (client.IsLocked)
                {
                    // Here rather than in the router, so that nothing of what the client asked is
                    // read: see `GateConversation.Locked`. The identity is still built first,
                    // because building one is what refuses to look at the certificate at all.
                    _log.Info("connection to " + CertificateDigest.Shorten(target.DatabaseId) +
                              " while it is locked");
                    GateConversation.Locked(protocol.Stream, _options, _log);
                    return;
                }

                _log.Info("connection to " + CertificateDigest.Shorten(target.DatabaseId) +
                          (client.IsPaired
                              ? " from " + CertificateDigest.Shorten(client.AdmissionKey)
                              : resolution.IsPairing
                                  ? " on invitation " + resolution.Invitation.Id
                                  : " from an unpaired client"));

                GateConversation.Serve(
                    protocol.Stream, client, _handler, _options, _log,
                    Pairing(resolution, peek, target, peer), _incidents);
            }
            finally
            {
                // Outside the handshake, not after it. Under TLS 1.3 a client's certificate arrives
                // before its `Finished` is checked, so one that then fails verification has already
                // left a certificate context here, and a client chooses how often that happens.
                var presented = server.PresentedCertificate;
                if (presented != null)
                {
                    presented.Reset();
                }

                try
                {
                    protocol.Close();
                }
                catch (Exception error) when (Expected.OfConnection(error))
                {
                    // The client may already have gone. Nothing to say about it.
                }
            }
        }
    }
}
