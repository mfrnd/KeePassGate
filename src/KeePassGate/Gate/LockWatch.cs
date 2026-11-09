using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate.Gate
{
    /// <summary>The databases the gate serves, as locking sees them.</summary>
    public interface ILockableTargets
    {
        /// <summary>
        /// Drops everything privileged about this database and keeps what answers a connection:
        /// the gate key and the responder certificate. Nothing at all if it was never served.
        /// </summary>
        bool Lock(string path);

        /// <summary>
        /// Serves it in full again, with a freshly minted responder, and answers the authority of
        /// the lock this lifted. Empty when it was a database being opened for the first time.
        /// </summary>
        IReadOnlyCollection<string> Unlock(string path);

        /// <summary>
        /// Forgets it entirely, which is what closing a database really means, and answers every
        /// authority something was being held for. Empty when there was nothing.
        /// </summary>
        IReadOnlyCollection<string> Forget(string path);

        /// <summary>
        /// Reads every open database again, so that a lock or close about to follow finds what
        /// they hold now rather than what the last connection happened to see.
        /// </summary>
        void Refresh();
    }

    /// <summary>The connections being served, as locking sees them.</summary>
    public interface IConnections
    {
        /// <summary>
        /// Closes every connection to a database with any of these authorities, waiting on the
        /// operator or not, and answers how many it closed. What an unlock does.
        /// </summary>
        int Close(IReadOnlyCollection<string> databaseIds, string why);

        /// <summary>
        /// Closes every connection to a database with any of these authorities except those
        /// waiting on the operator, and answers how many it closed. What a close does, because
        /// <see cref="IApprovalScreen.Closed"/> has already taken their question away and each
        /// answers `503 locked` itself, as at a lock.
        /// </summary>
        int CloseButAwaiting(IReadOnlyCollection<string> databaseIds, string why);

        /// <summary>
        /// Closes every connection except those waiting on the operator, and answers how many it
        /// closed. What a lock does, because <see cref="IApprovalScreen.Locked"/> has already
        /// taken their question away and each answers `503 locked` itself.
        /// </summary>
        int CloseAllButAwaiting(string why);
    }

    /// <summary>The questions put to the operator, and their answers, as locking sees them.</summary>
    public interface IApprovalScreen
    {
        /// <summary>
        /// KeePass is locking: a pairing or renewal dialog standing closes as unanswered, and an
        /// approval waiting to be written is let go, on KeePass's thread.
        /// </summary>
        void Locked();

        /// <summary>
        /// A database was closed for real: a pairing or renewal dialog standing for any of these
        /// authorities closes as unanswered, as at a lock, and an approval waiting to be written for
        /// any of them is let go, on KeePass's thread. Null when the database could not be named,
        /// which closes whatever dialog stands and lets every approval go.
        /// </summary>
        void Closed(IReadOnlyCollection<string> databaseIds);
    }

    /// <summary>Several of them, told in the order they were given.</summary>
    public sealed class ApprovalScreens : IApprovalScreen
    {
        private readonly IApprovalScreen[] _screens;

        public ApprovalScreens(params IApprovalScreen[] screens)
        {
            _screens = screens ?? Array.Empty<IApprovalScreen>();
        }

        public void Locked()
        {
            foreach (var screen in _screens.Where(screen => screen != null))
            {
                screen.Locked();
            }
        }

        public void Closed(IReadOnlyCollection<string> databaseIds)
        {
            foreach (var screen in _screens.Where(screen => screen != null))
            {
                screen.Closed(databaseIds);
            }
        }
    }

    /// <summary>
    /// What happens to the gate when a database is locked, unlocked or closed.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §308, and the state machine there is this class. Written against seams
    /// rather than against KeePass so that all of it can be exercised without a password manager,
    /// which matters more here than usual: locking is the one transition a test cannot easily
    /// arrange by hand in a running KeePass.
    ///
    /// **A lock closes every connection, and a close or an unlock only that database's**, the
    /// operator's ruling R1. Closing at lock is obvious: a socket thread must not be reading a
    /// database the user interface thread is closing, that race needs a real gate because the lock
    /// fires on the user interface thread, and KeePass locks every database at once. A close is
    /// the same race for the one database it empties. An unlock lifts the lock on one database, and
    /// closes the connections its responder was answering, so that a client part way through a
    /// handshake with the responder connects again rather than being told `503 locked` by a
    /// database that no longer is.
    ///
    /// **Both used to close every connection**, the other databases' too, so unlocking or closing
    /// a second database reset every request to the first, and closed the connection of a pairing
    /// waiting on that database's dialog, which the operator then approved into a closed socket.
    /// The unlock's was argued as privilege, a responder's connection promoted to an authority it
    /// never proved anything to, and nothing promotes such a connection: it keeps the target it
    /// resolved, and a responder's is answered `503 locked` before a request is read.
    ///
    /// **An unlock that lifts no lock closes nothing**, and that is a refinement the first end to
    /// end run forced. KeePass announces a database being opened and a database being unlocked the
    /// same way, so a KeePass starting with a database on its command line raised an unlock while a
    /// client was already connecting, and closed a connection that had never seen a responder. The
    /// close is for the responder's connections, so it follows a lock actually being lifted rather
    /// than the event that announces both.
    ///
    /// Order matters at a lock, and what it buys is narrower than it looks. The material is let go
    /// **before** the connections are closed, but letting go is releasing a reference, not wiping:
    /// a connection already in flight holds a reference of its own and the material lives until
    /// it ends. A pairing's or a renewal's deadline bounds that. Nothing of this plugin's bounds an
    /// ordinary write in flight, whose deadline was disarmed with its request: closing its socket
    /// frees no thread, so the material is held until its write and save end, and then for as long
    /// as its answer's write is allowed. Nor is the order what keeps a later connection away from
    /// it: KeePass shuts the database before it raises the close, so a connection arriving in
    /// between finds it shut whichever runs first. What the order is for is the guard on the
    /// close: connections are closed only when the lock took something away, and only letting go
    /// can answer that, so it runs first.
    /// The data path is closed either way, because a locked target answers no records at all.
    ///
    /// **A lock spares the connections waiting on the operator**, and closes the question they are
    /// waiting on instead. A pairing or renewal dialog holds no lock off, so a lock can fall under
    /// one, and closing its connection left the dialog offering an Approve over a locked database
    /// while the client was told nothing at all. The screen is told first, so the dialog closes as
    /// unanswered and its connection answers `503 locked`, `PAIRING.md` §308; every other
    /// connection is closed as before. What the spared ones go on to do touches nothing: a locked
    /// database writes nothing, and their deadline still bounds them.
    ///
    /// **A close spares them too**, the operator's ruling of 2026-11-08: the close of their own
    /// database takes their dialog away as a lock does, and each answers `503 locked`. It ended
    /// them with the database's other connections, so the dialog its close withdrew answered into
    /// a closed socket and the client heard nothing. A closed database writes nothing either. An
    /// unlock still ends its database's connections, the waiting ones among them.
    /// </remarks>
    public sealed class LockWatch : IDisposable
    {
        private readonly IDatabaseEvents _databases;
        private readonly ILockableTargets _targets;
        private readonly Func<IConnections> _connections;
        private readonly IApprovalScreen _screen;
        private readonly IGateLog _log;
        private readonly GateIncidents _incidents;
        private readonly object _stateLock = new object();

        private bool _started;

        public LockWatch(
            IDatabaseEvents databases, ILockableTargets targets, IConnections connections, IGateLog log)
            : this(databases, targets, () => connections, log, null, null)
        {
        }

        /// <summary>
        /// A watch that subscribes to the database events only once <see cref="Start"/> is called.
        /// </summary>
        /// <param name="connections">
        /// Asked for the connections each time rather than handed them once, so that a watch built
        /// before the listener exists still finds it. The listener is no longer replaced while the
        /// plugin runs, because a port is bound once at startup, so this is about construction
        /// order rather than about rebinding.
        /// </param>
        /// <param name="incidents">
        /// Where an unpredicted failure is reported. These handlers run on KeePass's own thread from
        /// KeePass's own events, raised with nothing around them, so each of the three is one of
        /// the places that catch everything. What escapes one stops KeePass's open, close or lock
        /// where it was: a lock of every database stops at the one being closed and leaves the ones
        /// after it unlocked, and a lock the inactivity timer began leaves the timer's critical
        /// section held, since KeePass releases it outside a `finally`, so that timer locks nothing
        /// again that session. That much is read off KeePass 2.61.1, not driven. The exception then
        /// reaches WinForms, which puts its own error dialog over KeePass, offering to quit.
        /// </param>
        /// <param name="screen">
        /// The questions put to the operator, which a lock takes away, or null where there is no
        /// screen.
        /// </param>
        public LockWatch(
            IDatabaseEvents databases,
            ILockableTargets targets,
            Func<IConnections> connections,
            IGateLog log,
            GateIncidents incidents,
            IApprovalScreen screen)
        {
            if (databases == null)
            {
                throw new ArgumentNullException(nameof(databases));
            }

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            _databases = databases;
            _targets = targets;
            _connections = connections;
            _screen = screen;
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        /// <summary>Subscribes to the database events, once however often it is asked.</summary>
        /// <remarks>
        /// Nothing to report and nothing caught, because subscribing cannot fail: the events
        /// KeePass's main window raises reach this through `MainWindowDatabases`, whose events
        /// are field-like, and adding a handler to such an event throws nothing. A catch here, and
        /// a result answering whether it ran, described a failure that cannot happen to a caller
        /// that never asked.
        /// </remarks>
        public void Start()
        {
            lock (_stateLock)
            {
                if (_started)
                {
                    return;
                }

                _databases.Opened += OnOpened;
                _databases.Closing += OnClosing;
                _databases.Closed += OnClosed;
                _started = true;
            }
        }

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
            }

            // Nothing caught, for the reason `Start` catches nothing: removing a handler from a
            // field-like event throws nothing either.
            _databases.Opened -= OnOpened;
            _databases.Closing -= OnClosing;
            _databases.Closed -= OnClosed;
        }

        /// <summary>
        /// An unlock, or a first open. Both mean the same thing to the gate: everything is
        /// available again, and the responder is minted fresh rather than reused from before the
        /// lock.
        /// </summary>
        private void OnOpened(object sender, GateDatabaseEventArgs e)
        {
            // On KeePass's thread, from KeePass's event. Nothing here may throw, and nothing here
            // may take long: it runs while the operator is waiting for their database to appear.
            try
            {
                if (e == null)
                {
                    return;
                }

                var lifted = _targets.Unlock(e.Path);
                if (lifted.Count > 0)
                {
                    Close(lifted, "a database was unlocked");
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // One of the places that catches everything, and this is why: it runs on KeePass's
                // own thread from KeePass's own event. The work below it reads custom data, parses
                // two PKCS#12 blobs, asks the anchor pin whether the authority signed the host and
                // mints a certificate, and a null reference or a `TypeLoadException` after a
                // KeePass upgrade matches no named filter; escaping here would stop KeePass's open
                // or unlock part way, and then reach WinForms' own error dialog.
                _incidents.Record("an unlock could not be handled", error);
            }
        }

        /// <summary>
        /// A close or a lock is coming, and the databases can still be read. Nothing is dropped
        /// and nothing is closed here: the close may yet be canceled.
        /// </summary>
        /// <remarks>
        /// **What a lock answers with is only ever what the gate last read**, because by the time
        /// the lock is announced the database has been cleared and cannot be asked. Reading used
        /// to happen only when a connection arrived or a database opened, so everything that
        /// changed since then without either was invisible to the lock: a Save As left the entry
        /// under the old path and the lock matched nothing, an authority created on the KPG tab
        /// had no entry and the lock answered nothing, and one deleted there still had one and the
        /// lock answered `503 locked` for it. Reading here closes all three. It costs a comparison
        /// of stored text per open database whose reading was kept, which is one served or one
        /// holding several authorities, and a key pair only where an authority is new. **A
        /// database whose authority does not read is read in full every time**, both PKCS#12 blobs
        /// parsed again at every lock, unlock, close, open and save, because nothing remembers a
        /// reading that served nothing.
        /// </remarks>
        private void OnClosing(object sender, EventArgs e)
        {
            try
            {
                _targets.Refresh();
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // KeePass's thread and KeePass's event, as the two handlers either side of this.
                _incidents.Record("a database about to close could not be read", error);
            }
        }

        private void OnClosed(object sender, GateDatabaseEventArgs e)
        {
            try
            {
                if (e == null)
                {
                    return;
                }

                // **Each half runs whatever the first threw**, which the `finally` is for: in one
                // step, a failure closing the question skipped letting go of the database, so a
                // locked KeePass kept its host key and authority in memory, kept its connections
                // and went on putting pairing requests in front of the operator. What the first
                // threw still reaches the catch below, once the second has run.
                if (e.IsLocking)
                {
                    try
                    {
                        // The question first, whichever database this is: KeePass locks every open
                        // database at once, so a lock of any of them is a lock of the one a dialog
                        // is about.
                        if (_screen != null)
                        {
                            _screen.Locked();
                        }
                    }
                    finally
                    {
                        // The material is let go first, then the connections, because whether
                        // anything was let go decides whether they are closed at all: guarded the
                        // way the unlock is. A lock that held nothing has taken nothing away, and
                        // closing every connection then kills handshakes and parked pairings
                        // belonging to databases that are still perfectly open. Letting go does
                        // not wipe the material while a connection holds its own reference.
                        if (_targets.Lock(e.Path))
                        {
                            CloseAllButAwaiting("a database was locked");
                        }
                    }

                    return;
                }

                IReadOnlyCollection<string> closed = null;
                try
                {
                    closed = _targets.Forget(e.Path);
                }
                finally
                {
                    try
                    {
                        // The question on the screen and an approval waiting to be written go with
                        // their database, as at a lock: an approval held across a close would be
                        // written into whatever is opened under that authority next. Only its
                        // database's, R1, since every other database is still open, and every
                        // database's when forgetting it failed and nothing can tell which went.
                        if (_screen != null)
                        {
                            _screen.Closed(closed);
                        }
                    }
                    finally
                    {
                        // Its connections but those waiting on the operator, whose question the
                        // screen has just taken away: each answers `503 locked`, as at a lock.
                        if (closed != null && closed.Count > 0)
                        {
                            CloseButAwaiting(closed, "a database was closed");
                        }
                    }
                }
            }
            // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
            catch (Exception error)
            {
                // The same reason as the unlock above: KeePass's thread, KeePass's event, and
                // the same reporting, so that the window under Tools counts both halves.
                _incidents.Record("a lock or close could not be handled", error);
            }
        }

        /// <summary>Closes the connections to the databases with these authorities.</summary>
        private void Close(IReadOnlyCollection<string> databaseIds, string why)
        {
            Closing(connections => connections.Close(databaseIds, why), why);
        }

        /// <summary>
        /// Closes the connections to the databases with these authorities but those waiting on the
        /// operator.
        /// </summary>
        private void CloseButAwaiting(IReadOnlyCollection<string> databaseIds, string why)
        {
            Closing(connections => connections.CloseButAwaiting(databaseIds, why), why);
        }

        /// <summary>Closes every connection but those waiting on the operator.</summary>
        private void CloseAllButAwaiting(string why)
        {
            Closing(connections => connections.CloseAllButAwaiting(why), why);
        }

        private void Closing(Func<IConnections, int> close, string why)
        {
            if (_connections == null)
            {
                return;
            }

            try
            {
                var connections = _connections();
                if (connections != null)
                {
                    close(connections);
                }
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                _log.Error("the connections could not be closed after " + why, error);
            }
        }
    }
}
