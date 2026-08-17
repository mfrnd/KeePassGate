using System;
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
        /// Serves it in full again, with a freshly minted responder. True when this really lifted a
        /// lock, false when it was a database being opened for the first time.
        /// </summary>
        bool Unlock(string path);

        /// <summary>
        /// Forgets it entirely, which is what closing a database really means. True when there was
        /// something being held for it.
        /// </summary>
        bool Forget(string path);

        /// <summary>
        /// Reads every open database again, so that a lock or close about to follow finds what
        /// they hold now rather than what the last connection happened to see.
        /// </summary>
        void Refresh();
    }

    /// <summary>The connections being served, as locking sees them.</summary>
    public interface IConnections
    {
        /// <summary>Closes every one of them, and says how many there were.</summary>
        int CloseAll(string why);
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
    /// **Both transitions close every connection**, and unlock is the one worth explaining. Closing
    /// at lock is obvious: a socket thread must not be reading a database the user interface thread
    /// is closing, and that race needs a real gate because the lock fires on the user interface
    /// thread. Closing at unlock is about privilege. A connection that completed its handshake
    /// against the responder certificate was authenticated against nothing, because validating a
    /// client certificate needs the authority, which was not loaded. Letting it survive into the
    /// unlocked state would promote a connection to an authority it never proved anything to.
    ///
    /// **An unlock that lifts no lock closes nothing**, and that is a refinement the first end to
    /// end run forced. KeePass announces a database being opened and a database being unlocked the
    /// same way, so a KeePass starting with a database on its command line raised an unlock while a
    /// client was already connecting, and closed a connection that had never seen a responder. The
    /// privilege argument only applies where there was a responder to be promoted from, so the
    /// close now follows a lock actually being lifted rather than the event that announces both.
    ///
    /// Order matters at a lock, and what it buys is narrower than it looks. The material is let go
    /// **before** the connections are closed, but letting go is releasing a reference, not wiping:
    /// a connection already in flight holds one of its own and the material lives until it finishes,
    /// which its deadline bounds. What the order does guarantee is that no connection arriving after
    /// the lock can reach it, and that the ones still running are being closed rather than served.
    /// The data path is closed either way, because a locked target answers no records at all.
    /// </remarks>
    public sealed class LockWatch : IDisposable
    {
        private readonly IDatabaseEvents _databases;
        private readonly ILockableTargets _targets;
        private readonly Func<IConnections> _connections;
        private readonly IGateLog _log;
        private readonly GateIncidents _incidents;
        private readonly object _stateLock = new object();

        private bool _started;

        public LockWatch(
            IDatabaseEvents databases, ILockableTargets targets, IConnections connections, IGateLog log)
            : this(databases, targets, () => connections, log, null)
        {
        }

        /// <param name="connections">
        /// Asked for the connections each time rather than handed them once, so that a watch built
        /// before the listener exists still finds it. The listener is no longer replaced while the
        /// plugin runs, because a port is bound once at startup, so this is about construction
        /// order rather than about rebinding.
        /// </param>
        /// <param name="incidents">
        /// Where an unpredicted failure is reported. These handlers run on KeePass's own thread from
        /// KeePass's own events, so each of the three is one of the places that catch everything:
        /// what escapes here reaches the message loop and ends the password manager.
        /// </param>
        public LockWatch(
            IDatabaseEvents databases,
            ILockableTargets targets,
            Func<IConnections> connections,
            IGateLog log,
            GateIncidents incidents)
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
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        public bool Start()
        {
            lock (_stateLock)
            {
                if (_started)
                {
                    return true;
                }

                try
                {
                    _databases.Opened += OnOpened;
                    _databases.Closing += OnClosing;
                    _databases.Closed += OnClosed;
                    _started = true;
                    return true;
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("the lock transitions could not be watched", error);
                    return false;
                }
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

            try
            {
                _databases.Opened -= OnOpened;
                _databases.Closing -= OnClosing;
                _databases.Closed -= OnClosed;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the lock watch could not be released", error);
            }
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

                if (_targets.Unlock(e.Path))
                {
                    Close("a database was unlocked");
                }
            }
            catch (Exception error)
            {
                // One of the places that catches everything, and this is why: it runs on KeePass's
                // own thread from KeePass's own event. The work below it parses a PKCS#12, reads
                // custom data, builds a chain and mints a certificate, and a null reference or a
                // `TypeLoadException` after a KeePass upgrade matches no named filter; escaping
                // here ends the password manager.
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
        /// of stored text per open database, and a key pair only where an authority is new.
        /// </remarks>
        private void OnClosing(object sender, EventArgs e)
        {
            try
            {
                _targets.Refresh();
            }
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

                if (e.IsLocking)
                {
                    // The material is let go first, then the connections. That does not wipe it
                    // while a connection is still holding its own reference; what it does is stop
                    // any later connection reaching it, and start closing the ones that are.
                    // Guarded the way the unlock is. A lock that held nothing has taken nothing
                    // away, and closing every connection then kills handshakes and parked pairings
                    // belonging to databases that are still perfectly open.
                    if (_targets.Lock(e.Path))
                    {
                        Close("a database was locked");
                    }

                    return;
                }

                if (_targets.Forget(e.Path))
                {
                    Close("a database was closed");
                }
            }
            catch (Exception error)
            {
                // The same reason as the unlock above: KeePass's thread, KeePass's event, and
                // the same reporting, so that the window under Tools counts both halves.
                _incidents.Record("a lock or close could not be handled", error);
            }
        }

        private void Close(string why)
        {
            if (_connections == null)
            {
                return;
            }

            try
            {
                var connections = _connections();
                if (connections == null)
                {
                    return;
                }

                connections.CloseAll(why);
            }
            catch (Exception error) when (Expected.OfConnection(error))
            {
                _log.Error("the connections could not be closed after " + why, error);
            }
        }
    }
}
