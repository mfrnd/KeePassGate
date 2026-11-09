using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
using KeePassGate.Spec;

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

        /// <summary>
        /// How often a run of refusals above the ceiling, or of drops before TLS, is reported. See
        /// `Refused` and `Dropped`.
        /// </summary>
        private static readonly TimeSpan PaceInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Monotonic, because the wall clock can go backwards and would take the warning with it.
        /// </summary>
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>
        /// The culture every connection is served in: the culture of the thread that made this
        /// listener, which in the plugin is KeePass's own.
        /// </summary>
        /// <remarks>
        /// **KeePass's dictionaries look a key up in the culture of the thread asking.** Custom
        /// data, an entry's strings and its attachments are each a `SortedDictionary` under the
        /// default comparer, which collates in the calling thread's culture, and KeePass's thread
        /// runs in its translation's, which `MainForm` sets from the language file. A connection
        /// is served on a thread of its own, and that thread starts in the culture of the regional
        /// format: KeePass.exe declares no target framework, and .NET Framework then lets no
        /// culture flow to a new thread or a pool item. Where the two collate differently, a tree
        /// KeePass's thread built is searched in the other's order, and a key that is there is not
        /// found. A KeePass translated to Danish on an English format answered an admitted client
        /// `401` because its record's key began `aa`, and served an empty value for a field it
        /// listed; a KeePass translated to Lithuanian walked past a node's own seal to the grant
        /// above it.
        ///
        /// **Taken once, when this is made, as `KeePassPaths` takes it**, because the plugin makes
        /// the listener on KeePass's thread and KeePass takes a new language only at a restart.
        /// Set for the whole of a conversation rather than around each lookup, because the lookups
        /// are KeePass's own as often as this plugin's, inside any call that finds something by
        /// name. The accept thread and a connection's deadline read nothing of a database, so they
        /// keep the culture they started with.
        /// </remarks>
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

        private TcpListener _listener;
        private Thread _accepting;
        private volatile bool _running;
        private int _conversations;

        /// <summary>The line for a connection refused above the ceiling: see `Refused`.</summary>
        private readonly PacedLine _refusals = new PacedLine(PaceInterval);

        /// <summary>
        /// The line for each reason a connection is dropped before TLS, a pace of its own for each
        /// so that a run of one reason hides none of the others: see `Dropped`.
        /// </summary>
        private readonly Dictionary<GateOutcome, PacedLine> _drops =
            new Dictionary<GateOutcome, PacedLine>
            {
                { GateOutcome.NoName, new PacedLine(PaceInterval) },
                { GateOutcome.NoMatch, new PacedLine(PaceInterval) },
                { GateOutcome.Ambiguous, new PacedLine(PaceInterval) },
            };

        /// <summary>
        /// Every connection being served right now, so that all of them can be closed at once.
        /// </summary>
        /// <remarks>
        /// `PAIRING.md` §308: a lock closes all of them but those waiting on the operator, since a
        /// socket thread must not be reading a database the user interface thread is closing and
        /// KeePass locks every database at once. A close and an unlock close only their own
        /// database's, by <see cref="_servedFor"/>, and a close spares those waiting on the
        /// operator as a lock does.
        /// </remarks>
        private readonly HashSet<TcpClient> _live = new HashSet<TcpClient>();

        /// <summary>
        /// The live connections whose request is a pairing or a renewal, which wait on the operator
        /// and which a lock and a close of their database spare: see
        /// <see cref="CloseAllButAwaiting"/> and <see cref="CloseButAwaiting"/>.
        /// </summary>
        private readonly HashSet<TcpClient> _awaiting = new HashSet<TcpClient>();

        /// <summary>
        /// The authority each live connection's name resolved to, which is how a close or an unlock
        /// finds that database's connections and no others: see
        /// <see cref="Close(IReadOnlyCollection{string}, string)"/>.
        /// </summary>
        private readonly Dictionary<TcpClient, string> _servedFor = new Dictionary<TcpClient, string>();

        /// <summary>
        /// When each authority's connections were last closed, counted in <see cref="_closings"/>,
        /// for a connection whose name resolved just before: see <see cref="Serving"/>.
        /// </summary>
        private readonly Dictionary<string, long> _closedAt = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>
        /// How many closes by authority there have been, under <see cref="_stateLock"/>.
        /// </summary>
        private long _closings;

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

        /// <summary>Why the port was not taken, or null while it was.</summary>
        public string Refusal { get; private set; }

        /// <summary>
        /// What runs on a connection's own thread once its conversation is over, or null for
        /// nothing.
        /// </summary>
        /// <remarks>
        /// `DroppedLocks.Settle`, the lock KeePass was asked for while this plugin's save was
        /// running, which waits for the answer to the request that saved. Under the connection
        /// thread's catch-all, and not run after a conversation that ended in a failure, whose lock
        /// the next conversation makes.
        /// </remarks>
        public Action Answered { get; set; }

        /// <summary>
        /// Records why this listener is never started, for a port refused before the socket was
        /// asked for it. A listener that is already running keeps listening and ignores this.
        /// </summary>
        /// <remarks>
        /// Kept here rather than beside the listener, because this is the one place that answers
        /// why nothing is listening, and a port that was never tried is the same answer to that
        /// question as a port that could not be taken: nothing tries again in this process, so it
        /// is what the plugin's own window shows for the rest of the session.
        /// </remarks>
        public void Refuse(string reason)
        {
            lock (_stateLock)
            {
                if (!_running)
                {
                    Refusal = reason;
                }
            }
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
                    Refusal = null;
                }
                catch (SocketException error)
                {
                    _log.Error("could not listen on 127.0.0.1:" + _options.Port, error);
                    Release(_listener);
                    _listener = null;

                    // Kept rather than only logged. Nothing will try this port again in this
                    // process, so the reason has to outlive the attempt: it is what the plugin's
                    // own window has to show for the rest of the session.
                    Refusal = "127.0.0.1:" + _options.Port + " could not be taken: " + error.Message;
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
            bool listening;

            lock (_stateLock)
            {
                listening = _running;
                accepting = _accepting;
            }

            if (listening)
            {
                Retire();
            }

            // What the class remark has always said happens here. `Retire` closes the listener and
            // nothing else: conversations already running kept their target references and ran on
            // to their own deadline, up to the pairing budget, so a pairing could still hand out a
            // certificate after the plugin had been told to stop.
            //
            // **Whether or not anything is still listening.** An accept loop that failed retired
            // the port itself, and a stop that then found nothing listening returned at once, so
            // every conversation accepted before the failure ran on past the plugin's teardown,
            // an ordinary write's with no deadline at all.
            CloseAll("the gate is stopping");

            if (accepting != null)
            {
                // Long enough for the accept call to notice, short enough that KeePass is never
                // held up by it.
                accepting.Join(TimeSpan.FromSeconds(2));
            }

            if (listening)
            {
                _log.Info("stopped listening");
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Closes every connection being served, without stopping the listener. What a stop does.
        /// </summary>
        /// <remarks>
        /// Closing the socket is what ends the conversation: the thread serving it is blocked in a
        /// read, and it wakes with a failure it already knows how to treat as a client that hung
        /// up. Nothing is joined, because a lock runs on the user interface thread and waiting on a
        /// socket thread there is how a password manager stops responding.
        /// </remarks>
        public int CloseAll(string why)
        {
            return Close(client => true, why);
        }

        /// <summary>
        /// Closes every connection except a pairing's or a renewal's, which a lock leaves to answer.
        /// </summary>
        /// <remarks>
        /// `PAIRING.md` §308. The lock has already closed the dialog such a connection waits on, or
        /// kept one from being shown, so what is left to it is answering `503 locked`; closing it
        /// here was what left the client with no answer at all. It reads nothing more of the
        /// database, which writes nothing while locked, and its deadline still bounds it. A stop
        /// closes it with the rest.
        /// </remarks>
        public int CloseAllButAwaiting(string why)
        {
            return Close(client => !_awaiting.Contains(client), why);
        }

        /// <summary>
        /// Closes every connection to a database with any of these authorities, a pairing's and a
        /// renewal's included, and leaves every other database's.
        /// </summary>
        /// <remarks>
        /// What an unlock does, and what a close did until the operator's ruling of 2026-11-08,
        /// <see cref="CloseButAwaiting"/>. Both used to close every connection, the operator's
        /// ruling R1: closing or unlocking a second database reset every request to the first,
        /// which was open all along, and closed the connection of a pairing waiting on the first
        /// database's dialog, which the operator then approved into a closed socket. A connection
        /// whose name has not resolved yet is about no database, and is left alone, unless it
        /// resolves to any of these before it is recorded: see <see cref="Serving"/>.
        /// </remarks>
        public int Close(IReadOnlyCollection<string> databaseIds, string why)
        {
            return Close(databaseIds, why, false);
        }

        /// <summary>
        /// Closes every connection to a database with any of these authorities except a pairing's
        /// or a renewal's, which a close leaves to answer as a lock does, and leaves every other
        /// database's.
        /// </summary>
        /// <remarks>
        /// What a close does, the operator's ruling of 2026-11-08. The close has already closed the
        /// dialog such a connection waits on, or kept that dialog from being shown, so what is left
        /// to it is answering `503 locked`; closing it here was what left the client with no answer
        /// at all. It is recorded before the close, since it resolved before its request was read,
        /// so <see cref="Serving"/> never ends it, and its deadline still bounds it.
        /// </remarks>
        public int CloseButAwaiting(IReadOnlyCollection<string> databaseIds, string why)
        {
            return Close(databaseIds, why, true);
        }

        /// <summary>
        /// Closes the connections to a database with any of these authorities, sparing those
        /// waiting on the operator when asked to.
        /// </summary>
        private int Close(IReadOnlyCollection<string> databaseIds, string why, bool sparingAwaiting)
        {
            if (databaseIds == null || databaseIds.Count == 0)
            {
                return 0;
            }

            var closed = new HashSet<string>(databaseIds, StringComparer.Ordinal);
            return Close(
                client =>
                {
                    string served;
                    return _servedFor.TryGetValue(client, out served) && closed.Contains(served)
                           && !(sparingAwaiting && _awaiting.Contains(client));
                },
                why,
                closed);
        }

        /// <summary>
        /// Closes the live connections this chooses, which it is asked about under the lock.
        /// </summary>
        /// <param name="closedFor">
        /// The authorities this closes for, noted so that a connection resolving to any of them as
        /// this runs is ended too, or null when the close is not by authority.
        /// </param>
        private int Close(Func<TcpClient, bool> chosen, string why, ISet<string> closedFor = null)
        {
            TcpClient[] closing;

            lock (_stateLock)
            {
                if (closedFor != null)
                {
                    _closings++;
                    foreach (var databaseId in closedFor)
                    {
                        _closedAt[databaseId] = _closings;
                    }
                }

                closing = _live.Where(chosen).ToArray();
                foreach (var client in closing)
                {
                    _live.Remove(client);
                    _awaiting.Remove(client);
                    _servedFor.Remove(client);
                }
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
                        // listening for the rest of the session, the plugin's own window says only
                        // that it is not listening, and the log, which is the one place a reason
                        // could be, had the reason and threw it away. An accept refused for want
                        // of buffers or of handles is what this is for, and the socket error code
                        // is the whole of what distinguishes one.
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

                    // Tracked before its thread starts. `Converse` takes it out again in a finally,
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

                    if (!Started(accepted))
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
        /// Starts the thread a conversation is served on, or reports that none could be started.
        /// </summary>
        /// <remarks>
        /// **A thread of the gate's own, never a thread of KeePass's thread pool**, because a
        /// conversation holds its thread through every wait it makes: a ClientHello that never
        /// comes, the writer's lock, a save on KeePass's thread, the pacing of a client's lines.
        /// The pool hands out only as many threads at once as there are processors and adds about
        /// a thread a second past that, and KeePass waits on it from its own thread: its managed
        /// Argon2, which it falls back to without its native library, queues its lanes there and
        /// blocks the save or the unlock on them without pumping. Thirty-one writes held on the
        /// pool froze the first save for seventeen to twenty-two seconds and answered some of them
        /// `503 busy`, and thirty-one silent sockets from a peer that proved nothing held KeePass's
        /// own pool work back for about ten seconds, seventeen on four processors. On threads of
        /// their own the pool stays KeePass's, and the ceiling bounds the threads.
        ///
        /// Background, so a connection still being served never keeps KeePass from exiting, and
        /// started at the accept, so a connection's deadline counts from there rather than from
        /// whenever a pool thread came free. Total, as everything on the accept thread has to be,
        /// see `Drop`: starting a thread fails only for want of memory to give it.
        /// </remarks>
        private bool Started(TcpClient client)
        {
            var thread = new Thread(delegate() { Converse(client); })
            {
                IsBackground = true,
                Name = "KeePassGate connection"
            };

            try
            {
                thread.Start();
                return true;
            }
            catch (OutOfMemoryException)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether there is room for another conversation. A connection past the ceiling is closed
        /// at once rather than queued, because queuing it is what would hold the thread.
        /// </summary>
        /// <remarks>
        /// **The slot is counted before anything is known about the peer**, and that is a
        /// concession rather than an oversight. It is taken when the socket is accepted, which is
        /// before the ClientHello is read and before any name resolves, so a peer holding no gate
        /// key, no certificate and no invitation takes one by connecting, and about thirty-two of
        /// them, reopened as each is cut, keep every admitted client out. What an admitted client
        /// gets then is this: the socket closed before TLS, no status, and a warning in a log
        /// nobody is watching.
        ///
        /// It is conceded because the remedies do not remedy it. A per-source cap has nothing to
        /// tell apart, since the gate binds loopback and every peer arrives from `127.0.0.1`,
        /// tunneled traffic included. A shorter deadline before the name resolves only makes the
        /// attacker reconnect more often, and `Converse` has one for that reason and no other.
        /// Closing it means reserving slots for connections whose name has resolved, or evicting
        /// the oldest that has not, and that is a design decision rather than a fix.
        ///
        /// What it costs is the gate's availability and nothing else: no secret is reachable from a
        /// slot and no write, and since each slot holds a thread of the gate's own, see `Started`,
        /// none of them holds anything KeePass is waiting on. The ceiling goes on doing what it is
        /// for, which is bounding how many of those threads a stranger can make. The peer it leaves
        /// is somebody under another Windows account, or a user on the far end of the tunnel the
        /// documentation recommends, since local code running as the operator is conceded
        /// throughout anyway.
        /// </remarks>
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
        /// missing its tail, which is what the first line being immediate costs: see
        /// <see cref="PacedLine"/>.
        /// </remarks>
        private void Refused()
        {
            int since;
            if (!_refusals.Due(_clock.Elapsed, out since))
            {
                return;
            }

            _log.Warn(
                "refused a connection: " + _options.MaxConnections + " are already being served" +
                Since(since));
        }

        /// <summary>
        /// Writes that a connection was dropped before TLS for a name that resolved to nothing, at
        /// most once an interval for each reason.
        /// </summary>
        /// <remarks>
        /// **For the reason <see cref="Refused"/> is paced.** A ClientHello naming nothing, or a
        /// name no open database answers to, needs no gate key, no certificate and no invitation,
        /// only a connect and one packet, and a line each let one loop of them fill the plugin's
        /// log at its bound in under a minute and push out the file kept before it in about two,
        /// errors and incidents with it. A connection that is not TLS at all writes nothing, as it
        /// always did.
        ///
        /// A pace for each reason rather than a single pace for all of them, so that a run of
        /// names nobody answers to does not hide the first name that two open databases answer
        /// to, which is the operator's to act on.
        /// </remarks>
        private void Dropped(GateOutcome outcome)
        {
            var since = 0;
            PacedLine line;
            if (_drops.TryGetValue(outcome, out line) && !line.Due(_clock.Elapsed, out since))
            {
                return;
            }

            _log.Info("dropped a connection: " + outcome.ToString().ToLowerInvariant() + Since(since));
        }

        /// <summary>The count a paced line carries of the occasions it did not write.</summary>
        private static string Since(int since)
        {
            return since > 0 ? ", and " + since + " more since the last of these" : string.Empty;
        }

        /// <summary>
        /// A line written at most once an interval, with the occasions in between counted into the
        /// next line written.
        /// </summary>
        /// <remarks>
        /// Under a lock of its own, because the drops before TLS are written from every
        /// connection's thread at once. The clock is handed in rather than read here, so that a
        /// test can choose what time it is.
        /// </remarks>
        internal sealed class PacedLine
        {
            private readonly object _lock = new object();
            private readonly TimeSpan _interval;
            private bool _written;
            private TimeSpan _writtenAt;
            private int _since;

            public PacedLine(TimeSpan interval)
            {
                _interval = interval;
            }

            /// <summary>
            /// Whether the line is written now, <paramref name="now"/> on a clock that never goes
            /// back, with how many occasions went unwritten since the last line that was. False
            /// counts this occasion among them.
            /// </summary>
            public bool Due(TimeSpan now, out int since)
            {
                lock (_lock)
                {
                    if (_written && now - _writtenAt < _interval)
                    {
                        _since++;
                        since = 0;
                        return false;
                    }

                    since = _since;
                    _written = true;
                    _writtenAt = now;
                    _since = 0;
                    return true;
                }
            }
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
        /// of its own, a kind `DESIGN.md` §141 then has to list.
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
                        Release(_listener);
                    }
                }
                finally
                {
                    _listener = null;
                }
            }
        }

        /// <summary>
        /// Stops a listener and closes every socket it holds, which <see cref="TcpListener.Stop"/>
        /// alone does not.
        /// </summary>
        /// <remarks>
        /// On .NET Framework `Stop` closes the listener's socket and makes another, unbound, for a
        /// `Start` that never comes here, and a listener whose `Start` was refused for a port
        /// already taken still holds the socket its constructor made, because the bind that failed
        /// is outside what `Start` unwinds. Either was a handle left open in the password manager
        /// with nothing referring to it, until a collection got round to it. Total, since the
        /// accept thread retires through it: see `Drop`.
        /// </remarks>
        private void Release(TcpListener listener)
        {
            try
            {
                listener.Stop();
                listener.Server.Close();
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                _log.Error("the listener did not stop cleanly", error);
            }
        }

        /// <summary>One conversation, on the thread <see cref="Started"/> gave it.</summary>
        /// <remarks>
        /// Internal so that a test can hand it a connection a lock closed before its thread began,
        /// which no timing a test controls can arrange: the thread starts as the connection is
        /// accepted, and a close lands before its first line only by chance.
        /// </remarks>
        internal void Converse(TcpClient client)
        {
            // In KeePass's culture, see `_culture`. The thread is this conversation's alone and
            // ends with it, so there is nothing to give back.
            Thread.CurrentThread.CurrentCulture = _culture;

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
                        //
                        // **It ends a peer that stops as well as a peer that keeps sending.**
                        // Closing the socket wakes a read blocked with no traffic at all, at once,
                        // with the failure a peer that hung up raises, so the deadline bounds
                        // silence too, and `Timeout` is the bound on each single read beneath it.
                        //
                        // **It is granted in stages, and this is the first of them.** Until a name
                        // resolves there is nothing owed but a ClientHello, which a client sends
                        // in one packet, so this phase gets the shorter of a single read and the
                        // whole budget rather than the whole budget. A peer holding no gate key,
                        // no certificate and no invitation takes a connection slot the moment it
                        // connects, which `Take` concedes, and what this bounds is how long one
                        // costs: a peer that dribbles a byte inside each read window used
                        // to keep a slot for the ninety seconds an admitted client's whole
                        // conversation is worth.
                        var unnamed = _options.Timeout < _options.Budget
                            ? _options.Timeout
                            : _options.Budget;

                        using (var deadline = new Deadline(client, unnamed))
                        {
                            Socket socket;
                            var raw = Opened(client, (int)_options.Timeout.TotalMilliseconds, out socket);
                            if (raw == null)
                            {
                                // Closed by a lock or a stop before this thread began: see `Opened`.
                                _log.Info("a connection was closed before it was served");
                                return;
                            }

                            var peek = ClientHelloReader.Read(raw);
                            if (peek == null)
                            {
                                // Not TLS. Five bytes, and nothing said back.
                                return;
                            }

                            // In a `using`, because a resolved target arrives holding a reference
                            // taken for this connection. Giving it back here is what lets the cache
                            // release the material the moment nothing is serving from it.
                            var since = Closings();
                            using (var resolution = Resolve(peek.ServerName))
                            {
                                if (resolution == null)
                                {
                                    // Counted as this side's by `Resolve`, and nothing is served.
                                    return;
                                }

                                if (!resolution.IsResolved)
                                {
                                    Dropped(resolution.Outcome);
                                    return;
                                }

                                if (!Serving(client, resolution.Target.DatabaseId, since))
                                {
                                    _log.Info("dropped a connection whose database was closed as it resolved");
                                    return;
                                }

                                // The second stage: the name resolved, so the connection is one
                                // somebody could have meant, and the budget the design writes
                                // down begins here. Counted from now rather than from the accept,
                                // which gives a connection that has named something the whole of
                                // it however long its hello took.
                                deadline.Extend(_options.Budget);

                                // The third stage, taken when the whole request is off the wire.
                                // For an ordinary connection that is no deadline at all while the
                                // plugin's own work runs, since no number bounds it, see
                                // `Deadline.Disarm`. For a pairing it is a longer deadline, because
                                // there the remaining work is a sum somebody wrote down: the
                                // screen, the dialog, the grace on it, and the write. The
                                // ordinary budget would close the socket while the dialog was
                                // still up. Everything after that point happens anyway: the
                                // invitation is spent and the record is written and saved, with
                                // nothing left to hand the certificate back on.
                                //
                                // **Granted where phase 4 begins, not where the name resolves.**
                                // The reason for the longer budget is that the connection is about
                                // to wait on a person, and that becomes true when a request has
                                // been read rather than when an invitation has been named. A
                                // connection that has sent no request waits on nobody, and it used
                                // to be handed six minutes of a connection slot for naming a secret
                                // it held: a holder of one invitation could take every slot with
                                // thirty-two slow connections, each sending a byte inside every
                                // read window, and hold them for as long as the invitation lived
                                // and six minutes more. That is the harm the locked case was
                                // guarded against, and it was reachable against an unlocked
                                // database all along. The locked case cannot reach this at all
                                // now, because `GateConversation.Locked` answers `503` without
                                // reading a request, so the guard is the shape of the code rather
                                // than a condition beside it.
                                //
                                // **A renewal waits on a person too**, and is measured the same way
                                // for the same reason: what is left after its request is the screen,
                                // the dialog, the grace on it and the write. It is told apart by what
                                // it asked for, which is only known here, once the request is read.
                                //
                                // **And the last stage is the answer's write**, which an ordinary
                                // connection is given the ordinary budget for once more, because
                                // how fast an answer is read is the peer's doing: see
                                // `Deadline.Writing`. A pairing or a renewal keeps its own.
                                Serve(
                                    raw, peek, resolution, Peer(socket),
                                    head =>
                                    {
                                        if (resolution.IsPairing || Renewing(head))
                                        {
                                            deadline.Extend(_options.PairingBudget);
                                            Awaiting(client);
                                        }
                                        else
                                        {
                                            deadline.Disarm();
                                        }
                                    },
                                    () => deadline.Writing(_options.Budget));
                            }

                            // And closed the way an answered connection is, so that whatever the
                            // peer sent after its request is not the reset that throws the answer
                            // away: see `Linger`. Once the target is given back, since nothing
                            // here reads the database.
                            Linger(socket);
                        }
                    }
                    finally
                    {
                        lock (_stateLock)
                        {
                            _live.Remove(client);
                            _awaiting.Remove(client);
                            _servedFor.Remove(client);
                        }
                    }
                }

                // Once the conversation is over and its answer has gone, which is when a lock
                // KeePass was asked for during this plugin's own save is made.
                var answered = Answered;
                if (answered != null)
                {
                    answered();
                }
            }
            catch (Exception error) when (Expected.OfConnection(error) || error is CryptographicException)
            {
                // A client that left, and a database whose material was released while this
                // connection was still holding it: closing a file is what raises the second, and
                // neither is a defect. Named rather than filtered through `OfCryptography`, which
                // also covers `ArgumentException` and would swallow a real one. An `IOException`
                // here is the socket's or the TLS stream's, since resolving a name, which reads
                // neither, counts its own: see `Resolve`.
                _log.Info("a connection ended early: " + error.Message);
            }
            // codeql[cs/catch-of-all-exceptions] the connection thread, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // A kind of catch-all `DESIGN.md` §141 lists. This is the top of a thread with
                // nothing above it, so an exception that escapes it is an unhandled exception in
                // the password manager's process, and the process is what would end. A client
                // chooses when this happens, which is why what it caught is counted where the
                // operator can see it.
                _incidents.Record("a connection failed", error);
            }
            finally
            {
                Interlocked.Decrement(ref _conversations);
            }
        }

        /// <summary>
        /// What the name a client asked for resolves to, or null when resolving it failed in a way
        /// that is this side's, which is counted.
        /// </summary>
        /// <remarks>
        /// **An `IOException` here is a defect, never a client leaving**, because resolving reads no
        /// stream: it asks the open databases, and what they hold is stored bytes. Bouncy Castle
        /// reports bytes that will not parse as an `IOException`, and the catch around the
        /// connection thread reads that type as a peer that hung up, logged at `Info` with nothing
        /// counted: a stored authority whose key identifier would not read did that to every
        /// connection to every open database. Each place that reads stored bytes answers for its
        /// own now, `Expected.OfCryptography`, and this keeps one that does not on the operator's
        /// window.
        /// </remarks>
        private GateResolution Resolve(string serverName)
        {
            try
            {
                return _resolver.Resolve(serverName);
            }
            catch (IOException error)
            {
                _incidents.Record("a name could not be resolved", error);
                return null;
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

            /// <summary>
            /// Whether <see cref="Disarm"/> has run, which only the connection's own thread asks or
            /// sets.
            /// </summary>
            private bool _disarmed;

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
            /// **The budget is granted in stages**, because a connection that has named nothing
            /// has said nothing anybody could have meant: it is owed a ClientHello and no more,
            /// and it is holding a connection slot while it says it. This is where a connection
            /// that has named a database is given the span the design writes down for delivering
            /// its request, and where the two exchanges that wait on a person, a pairing and a
            /// renewal, are given the longer span written down for that. A peer that named nothing
            /// reaches neither.
            ///
            /// The pairing budget exists because the budget is there to stop a peer holding a
            /// thread by feeding it slowly, and a pairing sitting in front of an approval dialog
            /// is neither slow nor a peer's doing.
            /// </remarks>
            public void Extend(TimeSpan budget)
            {
                _timer.Change(budget, Timeout.InfiniteTimeSpan);
            }

            /// <summary>
            /// Stops the connection being closed at all, once there is nothing left to wait for
            /// the peer to send.
            /// </summary>
            /// <remarks>
            /// **The budget bounds what the peer controls, and that ends with the body.** After
            /// the request is read the thread is blocked on this plugin rather than on the socket,
            /// and closing the socket frees nothing: not the thread, which runs on to the end of
            /// its write and its save, and not the connection slot, which is given back when that
            /// thread finishes rather than when the socket closes. All it does is destroy the
            /// answer. A write that outlasts the budget then lands, is saved, and is reported to
            /// nobody: the client is told the connection closed without an answer, holds no
            /// identifier for what it created, and a retry makes a second one.
            ///
            /// **And there is no number that would do instead**, which is why this is a disarm
            /// rather than a longer budget. What is left is a marshaled write whose save is a
            /// whole-database write of whatever size the database is, over whatever storage it is
            /// on, and on a failure a message box that waits on a person. None of that is knowable
            /// before it runs. `DatabaseWriter.WriteTimeout` bounds the wait for the marshaled
            /// call to *start*, not the call, so no sum of this plugin's timeouts bounds it either.
            /// Writing the answer is the peer's to slow down again, and is armed afresh by
            /// <see cref="Writing"/>.
            ///
            /// **Not for a pairing or a renewal**, the two connections whose remaining work is a sum
            /// somebody wrote down: the screen, the dialog, the grace on it, the settling of a busy
            /// KeePass, and the write.
            /// <see cref="GateOptions.PairingBudget"/> outlasts those together, so there the
            /// deadline still means something.
            /// </remarks>
            public void Disarm()
            {
                _disarmed = true;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            /// <summary>
            /// Gives writing the answer a budget of its own, counted from now, on a connection
            /// whose deadline was disarmed with its request.
            /// </summary>
            /// <remarks>
            /// **How fast the answer is read is the peer's doing**, as the pace of its request was,
            /// and nothing else bounds it. The socket's `SendTimeout` bounds one send, which is one
            /// TLS record of up to sixteen kilobytes, so a peer that read a record inside every
            /// timeout held a thread and a connection slot for as long as a large answer lasted:
            /// an attachment of half a megabyte, two thirds of a megabyte as base64, read at under
            /// two kilobytes a second, is about seven minutes.
            ///
            /// The plugin's own work is over by now, so closing the socket here costs only an
            /// answer the peer was not reading. A pairing or a renewal keeps the deadline it was
            /// given with its request, which already outlasts the answer it writes.
            /// </remarks>
            public void Writing(TimeSpan budget)
            {
                if (_disarmed)
                {
                    _timer.Change(budget, Timeout.InfiniteTimeSpan);
                }
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

        /// <summary>
        /// The connection's stream, with its timeouts set, or null when it was closed before this
        /// thread reached it.
        /// </summary>
        /// <remarks>
        /// **A lock can close a connection whose thread has not started.** The accept loop tracks a
        /// connection before it starts its thread, so that `CloseAll` reaches every one, and a lock
        /// or an unlock landing in between closes it there. A `TcpClient` closed before its stream
        /// was taken lets go of its socket and answers `Client` with null, so every property that
        /// goes through it, the timeouts first, raised a `NullReferenceException` that no filter
        /// names: a lock the operator took was counted on the plugin's window as a defect. The
        /// socket is read once here instead. Null is a connection that ended before it began, and a
        /// socket closed after that raises `ObjectDisposedException` or an `IOException`, which the
        /// conversation already reads as a client that left. The stream is made on that socket
        /// rather than asked of the client, because `GetStream` reads `Client` again, and so does
        /// anything else that is handed the client rather than the socket.
        ///
        /// **The stream does not own the socket**, so Bouncy Castle closing the stream it was
        /// given ends the TLS and leaves the socket open for <see cref="Linger"/>. The client
        /// closes it, when `Converse` lets it go, and so does the deadline.
        /// </remarks>
        internal static NetworkStream Opened(TcpClient client, int timeout, out Socket socket)
        {
            socket = client.Client;
            if (socket == null)
            {
                return null;
            }

            socket.ReceiveTimeout = timeout;
            socket.SendTimeout = timeout;
            socket.NoDelay = true;
            return new NetworkStream(socket, false);
        }

        /// <summary>
        /// Ends an answered connection the way RFC 9112 section 9.6 asks of a server closing a
        /// connection: its own sending half first, and then whatever the peer still sends is read
        /// and dropped, until the peer closes, stays quiet for <see cref="GateOptions.Linger"/>,
        /// or has sent as much as one more request could hold.
        /// </summary>
        /// <remarks>
        /// **A socket closed with the peer's bytes unread is answered by the stack with a reset**,
        /// and the reset discards whatever reached the peer and was not read yet, the answer
        /// among it. The body a refused head announced is taken already, by
        /// `HttpRequestReader.Discard`; this is for what nothing announced: a second request
        /// pipelined behind the first, a line break written as a record of its own, or the
        /// `close_notify` of a client that shut its own sending half once its request was out,
        /// which RFC 8446 section 6.1 allows. Any of those turned the close into a reset, and a
        /// client reading a moment late lost the answer to a write that had landed, a `201`
        /// among them, and held no identifier for what it made.
        ///
        /// What is read is TLS records, dropped with nothing decrypted. The deadline bounds the
        /// whole of it: the answer's own for an ordinary connection, a pairing's or a renewal's,
        /// or the budget a name earned for a connection whose request was never read. The cap is
        /// for a peer that keeps sending fast, which it can do only up to the size of one more
        /// request.
        /// https://www.rfc-editor.org/rfc/rfc9112#section-9.6
        /// </remarks>
        private void Linger(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Send);
                if (_options.Linger <= TimeSpan.Zero)
                {
                    return;
                }

                // Zero is no timeout at all to a socket, so a linger shorter than a millisecond
                // waits a millisecond rather than for ever.
                socket.ReceiveTimeout = Math.Max(1, (int)_options.Linger.TotalMilliseconds);

                var chunk = new byte[LingerChunkBytes];
                var left = (long)_options.MaxHeadBytes + _options.MaxBodyBytes;
                while (left > 0)
                {
                    var got = socket.Receive(
                        chunk, 0, (int)Math.Min(chunk.Length, left), SocketFlags.None);
                    if (got <= 0)
                    {
                        return;
                    }

                    left -= got;
                }
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                // The peer closed, reset, or stayed quiet for the linger, or the deadline closed
                // the socket under the read. Each is the end the linger was waiting for.
            }
        }

        /// <summary>How much <see cref="Linger"/> takes off the socket at a time.</summary>
        private const int LingerChunkBytes = 8 * 1024;

        private static string Peer(Socket socket)
        {
            try
            {
                var endpoint = socket.RemoteEndPoint;
                return endpoint == null ? string.Empty : endpoint.ToString();
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                // A socket that has already gone has no address to report.
                return string.Empty;
            }
        }

        /// <summary>How many closes by authority there have been so far.</summary>
        private long Closings()
        {
            lock (_stateLock)
            {
                return _closings;
            }
        }

        /// <summary>
        /// Records the authority a live connection's name resolved to, and answers whether it is
        /// still to be served.
        /// </summary>
        /// <remarks>
        /// **Not when its database was closed or unlocked while the name was resolving.** A close
        /// by authority finds a connection by what is recorded here, and a name that resolved just
        /// before the close, to a target taken before the database was let go, is recorded just
        /// after it: the close missed it, and it would go on reading a database KeePass had
        /// emptied. <paramref name="since"/> is how many closes there had been before the name was
        /// asked about, so a close of this authority after that ends the connection here instead.
        /// Nor when something has closed it already, which a lock and a stop do to every live
        /// connection whatever it resolved to.
        /// </remarks>
        private bool Serving(TcpClient client, string databaseId, long since)
        {
            lock (_stateLock)
            {
                long closedAt;
                if (!_live.Contains(client) ||
                    (_closedAt.TryGetValue(databaseId ?? string.Empty, out closedAt) && closedAt > since))
                {
                    return false;
                }

                _servedFor[client] = databaseId;
                return true;
            }
        }

        /// <summary>
        /// Marks a live connection as waiting on the operator, unless something has already closed
        /// it.
        /// </summary>
        private void Awaiting(TcpClient client)
        {
            lock (_stateLock)
            {
                if (_live.Contains(client))
                {
                    _awaiting.Add(client);
                }
            }
        }

        /// <summary>Whether a request is a renewal, which waits on a person as a pairing does.</summary>
        /// <remarks>
        /// Matched the way the router matches it, segment by segment after decoding, so an escaped
        /// spelling the router serves as a renewal is measured as one: compared as it arrived,
        /// `/%67ate/renew` raised the dialog with its deadline disarmed.
        /// </remarks>
        private static bool Renewing(HttpRequestHead head)
        {
            IReadOnlyDictionary<string, string> captured;
            return head != null
                   && string.Equals(head.Method, "POST", StringComparison.Ordinal)
                   && RenewalPath.TryMatch(head.Path, out captured);
        }

        private static readonly PathTemplate RenewalPath = PathTemplate.Parse(RenewalHandler.Path);

        private void Serve(
            System.IO.Stream raw,
            ClientHelloPeek peek,
            GateResolution resolution,
            string peer,
            Action<HttpRequestHead> arrived,
            Action answering)
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
                catch (Exception error) when (Expected.OfConnection(error))
                {
                    // An alert and every other way a handshake fails, in one catch, because the
                    // question asked of each is the same: an alert is an `IOException`, and so is
                    // what Bouncy Castle throws on unwrapped from a callback. One check for both
                    // is a check the test of a failing callback reaches whichever arrives.
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

                    // A client offering something we do not, refused and told so in the alert, a
                    // client that hung up, a stream that closed, bytes that were never TLS.
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

                if (client.UnavailableError != null)
                {
                    // Served by nobody for now, for the reason the word gives, and answered the
                    // same way as a lock and for the same reason.
                    _log.Info("connection to " + CertificateDigest.Shorten(target.DatabaseId) +
                              " while it is " + client.UnavailableError);
                    GateConversation.Unavailable(protocol.Stream, client.UnavailableError, _options, _log);
                    return;
                }

                // The invitation first, and both facts when both hold. A client can arrive holding
                // an admitted certificate on an invitation's name, and that connection reaches
                // nothing at all: the router counts it as admitted only while no invitation
                // brought it, and the pairing routes are for clients that are not paired. Saying
                // only "from <key>" was true and left out the one fact that explained the
                // refusals, so the operator read an ordinary admitted connection being refused
                // everywhere and had nothing to go on.
                var who = client.IsPaired
                    ? " from " + CertificateDigest.Shorten(client.AdmissionKey)
                    : " from an unpaired client";

                _log.Info("connection to " + CertificateDigest.Shorten(target.DatabaseId) +
                          (resolution.IsPairing
                              ? " on invitation " + resolution.Invitation.Id +
                                (client.IsPaired ? who + ", which is admitted and so reaches neither" : string.Empty)
                              : who));

                GateConversation.Serve(
                    protocol.Stream, client, _handler, _options, _log,
                    Pairing(resolution, peek, target, peer), _incidents, target, peer, arrived,
                    answering);
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
