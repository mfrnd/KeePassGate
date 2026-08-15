using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassLib;
using KeePassLib.Utility;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The open KeePass databases that carry an authority, as the gate needs them.
    /// </summary>
    /// <remarks>
    /// Read on the connection's own thread, without marshaling, which is what `DESIGN.md` §119
    ///  sanctions: KeePass's object model documents no thread affinity for reads, and marshaling
    /// them would make liveness depend on the user interface thread. Nothing here mutates a
    /// database.
    ///
    /// The certificates are cached per database and rebuilt only when what is stored changes,
    /// compared by the stored text rather than by parsing it. Nothing is written anywhere by a
    /// rebuild any more, now that the TLS is managed code, so this is only about not parsing a
    /// PKCS#12 and a pair of certificates on every connection.
    /// </remarks>
    public sealed class OpenDatabaseTargets : IGateTargets, ILockableTargets, IDisposable
    {
        private readonly Func<IEnumerable<PwDatabase>> _openDatabases;
        private readonly AdmissionWriter _writer;
        private readonly IGateLog _log;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<PwDatabase, CachedTarget> _cache =
            new Dictionary<PwDatabase, CachedTarget>();

        /// <summary>
        /// The databases that are locked rather than closed, by the file they came from.
        /// </summary>
        /// <remarks>
        /// Keyed on the path rather than on the `PwDatabase`, because KeePass clears that object
        /// when it closes it and the path is the only thing a lock and the unlock after it have in
        /// common. Compared as KeePass compares paths, which is <see cref="KeePassPaths"/>.
        /// </remarks>
        private readonly Dictionary<string, LockedTarget> _locked;

        /// <summary>
        /// What a database that has gone away would be locked with, held until the event arrives
        /// that says whether it was locked or closed.
        /// </summary>
        /// <remarks>
        /// KeePass clears the `PwDatabase` first and raises `FileClosed` afterwards, so a
        /// connection arriving in between finds the database shut and `DropClosed` stops serving
        /// it. Everything a lock needs used to go with it, and the lock that followed found
        /// nothing to build a `LockedTarget` from: the database that should have gone on answering
        /// `503 locked` answered nothing at all, which is the one case `PAIRING.md` §308 exists to
        /// remove.
        ///
        /// **Nothing in here is served.** Until the event arrives the plugin has not been told
        /// which transition this was, and answering `locked` for a database that is being closed
        /// would be saying something nobody said. What it does is keep the answer available for
        /// the lock that may be about to arrive.
        ///
        /// Only the responder and the gate key are kept, which is exactly what a locked database
        /// is left with: the authority and the normal host key go the moment the database does,
        /// whichever transition this turns out to be.
        /// </remarks>
        private readonly Dictionary<string, LockedTarget> _closing;

        /// <summary>How two paths are told to be one database, taken as the plugin starts.</summary>
        private readonly KeePassPaths _paths = new KeePassPaths();

        public OpenDatabaseTargets(Func<IEnumerable<PwDatabase>> openDatabases, IGateLog log)
            : this(openDatabases, null, log)
        {
        }

        /// <summary>
        /// The same, with the one writer pairing goes through. Without it a database can be served
        /// and read, and nothing can be admitted to it, which is what a plugin with no user
        /// interface amounts to.
        /// </summary>
        public OpenDatabaseTargets(
            Func<IEnumerable<PwDatabase>> openDatabases, AdmissionWriter writer, IGateLog log)
        {
            if (openDatabases == null)
            {
                throw new ArgumentNullException(nameof(openDatabases));
            }

            _openDatabases = openDatabases;
            _writer = writer;
            _log = log ?? NullGateLog.Instance;
            _locked = new Dictionary<string, LockedTarget>(_paths);
            _closing = new Dictionary<string, LockedTarget>(_paths);
        }

        public IReadOnlyList<IGateTarget> Current()
        {
            var open = new List<PwDatabase>();
            try
            {
                var databases = _openDatabases();
                if (databases != null)
                {
                    foreach (var database in databases)
                    {
                        if (database != null && database.IsOpen && database.RootGroup != null)
                        {
                            open.Add(database);
                        }
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the open databases could not be listed", error);
                return new List<IGateTarget>();
            }

            var targets = new List<IGateTarget>();

            var handedOver = false;

            lock (_cacheLock)
            {
                DropClosed(open);

                try
                {
                    foreach (var database in open)
                    {
                        var target = TargetFor(database);
                        if (target != null)
                        {
                            // One reference per caller, taken while the cache still holds its own,
                            // so that nothing can release the material between here and the
                            // connection.
                            target.Retain();
                            targets.Add(target);
                        }
                    }

                    handedOver = true;
                }
                finally
                {
                    // A later database that cannot be read leaves the references already taken held
                    // by nobody: the count never reaches zero, so the gate key and the host PKCS#12
                    // of a database that was perfectly readable are never wiped. Given back here
                    // rather than in a `catch`, so the throw goes on to the resolver untouched and
                    // this stays out of the places that catch everything.
                    if (!handedOver)
                    {
                        foreach (var taken in targets)
                        {
                            taken.Dispose();
                        }
                    }
                }

                // A locked database is still served, by a responder that can say so and nothing
                // else. Without this the gate resolves no name for it and a client gets a bare
                // connection failure, which is what `PAIRING.md` §308 exists to avoid.
                foreach (var locked in _locked.Values)
                {
                    // One reference per caller, exactly as above. Handing this out bare meant the
                    // first caller to give it back destroyed it for every later connection.
                    locked.Retain();
                    targets.Add(locked);
                }
            }

            return targets;
        }

        public void Dispose()
        {
            lock (_cacheLock)
            {
                foreach (var cached in _cache.Values)
                {
                    cached.Dispose();
                }

                _cache.Clear();

                foreach (var locked in _locked.Values)
                {
                    locked.Dispose();
                }

                _locked.Clear();

                foreach (var kept in _closing.Values)
                {
                    kept.Dispose();
                }

                _closing.Clear();
            }
        }

        /// <summary>
        /// A database was locked. What is privileged goes; what answers a connection stays.
        /// </summary>
        /// <remarks>
        /// The authority key and the host certificate are inside the `AuthorityReading` this
        /// disposes, which overwrites the stored PKCS#12 and releases both handles. The key the TLS
        /// server signs with is not in there: it is the `HostKeyMaterial` on the target, and it
        /// goes when the target does, which is the last reference going rather than anything being
        /// overwritten, because a BouncyCastle key is an immutable `BigInteger`. `CA.md` §208 says
        /// what that is and is not worth. The
        /// admission records were never here to drop: they are read from the live database on every
        /// connection, and locking cuts them off by closing the database rather than by clearing
        /// anything. The gate key is copied out before the material goes, because disposing it
        /// zeroes the key, and the responder was minted while the database was open and is held on
        /// its own.
        ///
        /// A lock that finds nothing cached is not necessarily a database nobody served: a
        /// connection may have found it shut before this arrived, in which case what it will
        /// answer with is in <see cref="_closing"/> rather than in the cache.
        /// </remarks>
        public bool Lock(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            lock (_cacheLock)
            {
                PwDatabase found = null;
                CachedTarget cached = null;

                foreach (var pair in _cache)
                {
                    // The path the entry last saw, which `TargetFor` keeps level with the file and
                    // `Refresh` brings level just before every lock, since a Save As moves it with
                    // no connection to notice. Read live here it would be empty: a lock arrives
                    // after the database is closed.
                    if (_paths.Equals(pair.Value.Path, path))
                    {
                        found = pair.Key;
                        cached = pair.Value;
                        break;
                    }
                }

                if (cached == null)
                {
                    // A connection may have found the database shut before this event arrived, in
                    // which case what it will answer with was kept then rather than dropped.
                    LockedTarget kept;
                    if (_closing.TryGetValue(path, out kept))
                    {
                        _closing.Remove(path);
                        Replace(path, kept);
                        _log.Info("locked, answering with the responder kept when it closed: " +
                                  CertificateDigest.Shorten(kept.DatabaseId));
                        return true;
                    }

                    // Never served: no authority, or locked before anything reached it. There is
                    // nothing to answer with and nothing to drop. Said out loud, because a
                    // database that answers nothing while it is locked says nothing else about
                    // why. By the file name alone, as `DatabaseName` names one: the log is a file
                    // anybody reading a support request may be sent, and a path says where
                    // somebody keeps their passwords.
                    _log.Info("locked, but nothing was being served for it: " + UrlUtil.GetFileName(path));
                    return false;
                }

                var answering = Answering(cached);
                if (answering != null)
                {
                    Replace(path, answering);
                    _log.Info("locked, answering with the responder: " +
                              CertificateDigest.Shorten(answering.DatabaseId));
                }
                else
                {
                    // No responder was minted, so there is nothing to answer with. `CA.md`
                    // §207 names this case and accepts it: the connection really is a bare
                    // failure until somebody unlocks.
                    // `Release` rather than `Forget`: the cache entry is disposed below, and
                    // `Forget` would drop it here as well, releasing one reference for two.
                    Release(path);
                    _log.Warn("locked with no responder, so nothing answers for this database");
                }

                cached.Dispose();
                _cache.Remove(found);
                return true;
            }
        }

        /// <summary>
        /// What this database answers with once it is locked, built from what is cached for it.
        /// Null when there is no responder to answer with at all. Called with the lock held.
        /// </summary>
        /// <remarks>
        /// **Minted here rather than reused, so its life is spent on the lock.** The one minted
        /// when the database was first served has already spent however long the session lasted,
        /// and a certificate that expires mid-lock is one `PAIRING.md` §304 requires the client to
        /// refuse: the operator then gets the bare handshake failure the responder exists to
        /// replace. Nothing re-mints on a timer and nothing can once the authority is gone, so the
        /// only moment worth minting at is the last one where it is still possible, and this is it
        /// whether the lock announced itself or a connection got there first.
        ///
        /// The one from the cache is the fallback, and only while it is still valid: minting fails
        /// for an authority whose own dates have passed, which is reported rather than thrown, and
        /// an aged responder answers nothing anyway.
        /// </remarks>
        private LockedTarget Answering(CachedTarget cached)
        {
            var responder = Responder(cached.Target.Reading);
            if (responder == null && cached.Responder != null && StillCurrent(cached.Responder))
            {
                _log.Info("a fresh responder could not be minted, so the lock answers with the one held");
                responder = cached.Responder;
            }

            if (responder == null)
            {
                return null;
            }

            // Into a local and zeroed afterwards. The property hands out a fresh copy of the gate
            // key each time it is read, and `LockedTarget` takes a copy of its own.
            var gateKey = cached.Target.GateKey;
            try
            {
                return new LockedTarget(cached.Target.DatabaseId, gateKey, responder);
            }
            finally
            {
                Array.Clear(gateKey, 0, gateKey.Length);
            }
        }

        /// <summary>
        /// Keeps what a lock would answer with, for a database KeePass has closed without yet
        /// saying why. Called with the lock held.
        /// </summary>
        /// <remarks>
        /// Built now rather than when the lock arrives, because building it needs the authority and
        /// this is the last moment the authority is here: the material is let go as soon as this
        /// returns. A close that follows takes it away again, having answered nothing in the
        /// meantime.
        /// </remarks>
        private void Keep(CachedTarget cached)
        {
            var path = cached.Path;
            if (string.IsNullOrEmpty(path))
            {
                // Nothing to recognize it by, so no lock could ask for it. A database served before
                // it was ever written to a file is the case, and KeePass has no file to lock it
                // from either.
                return;
            }

            var answering = Answering(cached);
            if (answering == null)
            {
                return;
            }

            LockedTarget existing;
            if (_closing.TryGetValue(path, out existing))
            {
                existing.Dispose();
            }

            _closing[path] = answering;
        }

        /// <summary>
        /// Whether a responder minted earlier is still usable, read off its own bytes.
        /// </summary>
        /// <remarks>
        /// `HostKeyMaterial` keeps the certificate as DER rather than as an object, so this parses
        /// to ask. That costs a fraction of a millisecond once per lock, against a certificate a
        /// client is required to reject if it has expired.
        /// </remarks>
        private static bool StillCurrent(HostKeyMaterial responder)
        {
            try
            {
                using (var certificate = new X509Certificate2(responder.CertificateDer))
                {
                    return CertificateValidity.IsCurrent(certificate, DateTimeOffset.UtcNow);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                // Unreadable is not current, and minting another is the safe answer either way.
                return false;
            }
        }

        /// <summary>
        /// A database was opened or unlocked. Anything held for it while it was locked goes, and it
        /// is read in full again straight away.
        /// </summary>
        /// <remarks>
        /// **Straight away rather than on the next connection**, and that is the whole reason this
        /// method does more than forget. Reading every database is what puts a cache entry there,
        /// and a lock answers out of that entry: the responder it presents is minted from the
        /// authority the entry holds. Left until the next connection, a database that nobody
        /// happened to connect to between an unlock and the next lock reaches its lock with nothing
        /// cached and nothing to mint from, and the operator gets a bare connection failure for a
        /// KeePass that is merely locked, which is exactly the case the responder exists to remove.
        /// `PAIRING.md` §308 is where the mint at unlock is named, and it is this one.
        ///
        /// It costs one key pair and one signature on the user interface thread, at a moment when
        /// KeePass is already opening a database.
        ///
        /// **It answers whether a lock was lifted, and nothing else.** A database being opened for
        /// the first time can have been read already, by a connection that got there before the
        /// event did, and dropping that reading lifts no lock: `LockWatch` closes every connection
        /// on a lifted lock, and closing them over a first open is what `LockWatch` says the first
        /// end to end run found.
        /// </remarks>
        public bool Unlock(string path)
        {
            bool lifted;
            LetGo(path, out lifted);

            // Reading them all is what builds the target, and building it is what mints the
            // responder. Which database was unlocked does not matter: the rest are cached.
            Refresh();

            return lifted;
        }

        /// <summary>
        /// Reads every open database now, keeping the cache level with what they hold.
        /// </summary>
        /// <remarks>
        /// Called at an unlock, and at the moment before every close or lock, which is the last
        /// one at which a database can still be read. A lock can only answer out of the cache,
        /// so whatever changed since the last read without a connection to notice it, a path
        /// moved by Save As, an authority created or deleted on the KPG tab, has to be read here
        /// or the lock acts on what used to be true.
        /// </remarks>
        public void Refresh()
        {
            // Given back at once. `Current` takes a reference per caller, so a caller that wanted
            // only the reading still has to release: without this the count never returned to zero
            // and the authority's private key was never wiped, at this lock or any later one.
            foreach (var target in Current())
            {
                target.Dispose();
            }
        }

        /// <summary>
        /// A database was closed for real. Nothing is kept, and nothing answers for it. True when
        /// anything was held for it, which is when a connection may still be reading it: what it
        /// was served with, what a lock would have answered with, or what a lock was answering with.
        /// </summary>
        /// <remarks>
        /// It answered only for a lock, so closing a database that was being served and never
        /// locked closed no connection, and one resolved before the close ran on to its deadline
        /// against a database KeePass had already emptied. `LockWatch` closes them for the reason
        /// it closes them at a lock: a socket thread must not be reading a database the user
        /// interface thread is closing.
        /// </remarks>
        public bool Forget(string path)
        {
            bool lifted;
            return LetGo(path, out lifted);
        }

        /// <summary>
        /// Lets go of everything held for this path, and says whether there was anything.
        /// </summary>
        /// <param name="lifted">Whether a lock was among it, which is all an unlock is asked.</param>
        private bool LetGo(string path, out bool lifted)
        {
            lifted = false;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            lock (_cacheLock)
            {
                lifted = Release(path);

                // The cache too. A close that was never a lock still has to drop the authority's
                // private key, and the only other reaper is `DropClosed`, which runs inside
                // `Current` and so never runs again for a database nobody will ask about.
                var dropped = DropCached(path);

                // And whatever was kept in case this turned out to be a lock. This is the event
                // that says it was not. It counts all the same, because it is kept only for a
                // database that was being served until a connection found it shut, and whatever
                // was reading it then may be reading it still.
                var discarded = Discard(path);

                return lifted || dropped || discarded;
            }
        }

        /// <summary>Lets go of whatever was answering for this path while it was locked.</summary>
        private bool Release(string path)
        {
            LockedTarget locked;
            if (!_locked.TryGetValue(path, out locked))
            {
                return false;
            }

            _locked.Remove(path);
            locked.Dispose();
            return true;
        }

        /// <summary>
        /// Lets go of what was kept in case this path turned out to be a lock, and says whether
        /// anything was.
        /// </summary>
        private bool Discard(string path)
        {
            LockedTarget kept;
            if (string.IsNullOrEmpty(path) || !_closing.TryGetValue(path, out kept))
            {
                return false;
            }

            _closing.Remove(path);
            kept.Dispose();
            return true;
        }

        /// <summary>
        /// Drops the served material for this path, if any is still held, and says whether it was.
        /// </summary>
        private bool DropCached(string path)
        {
            foreach (var pair in _cache)
            {
                if (!_paths.Equals(pair.Value.Path, path))
                {
                    continue;
                }

                pair.Value.Dispose();
                _cache.Remove(pair.Key);
                return true;
            }

            return false;
        }

        /// <summary>Called with the lock held.</summary>
        private void Replace(string path, LockedTarget target)
        {
            LockedTarget existing;
            if (_locked.TryGetValue(path, out existing))
            {
                existing.Dispose();
            }

            _locked[path] = target;
        }

        private void DropClosed(List<PwDatabase> open)
        {
            var gone = new List<PwDatabase>();
            foreach (var known in _cache.Keys)
            {
                if (!open.Contains(known))
                {
                    gone.Add(known);
                }
            }

            foreach (var database in gone)
            {
                var cached = _cache[database];

                // Kept before it is let go, because this may be a lock that has not been announced
                // yet: KeePass clears the database first and says why afterwards. What is
                // privileged goes either way.
                Keep(cached);

                cached.Dispose();
                _cache.Remove(database);
                _log.Info("stopped serving a database that is no longer open");
            }
        }

        private MaterialTarget TargetFor(PwDatabase database)
        {
            // Read once and held. `Current` checked this a moment ago, but locking a database sets
            // it to null on the user interface thread, and a null reference raised here matches no
            // filter on the way out: it would reach the connection catch-all and be counted against
            // the plugin for something the operator did on purpose.
            var root = database.RootGroup;
            if (root == null)
            {
                return null;
            }

            string stored;
            try
            {
                // The database's own custom data, not the root group's: that is where everything
                // of this plugin's that belongs to the database lives. Every authority and every
                // retirement, because which authority is served is decided by all of them.
                stored = new AuthorityStore(new CustomDataStore(database), _log).Stored();
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a database's authority could not be reached", error);
                return null;
            }

            CachedTarget cached;
            if (_cache.TryGetValue(database, out cached))
            {
                if (cached.Matches(stored))
                {
                    cached.Follow(PathOf(database));
                    return cached.Target;
                }

                cached.Dispose();
                _cache.Remove(database);
            }

            var reading = new AuthorityStore(new CustomDataStore(database), _log).Read();
            if (!reading.IsPresent)
            {
                // No authority, or one that cannot be read. Either way there is nothing to serve
                // and nothing to say: the tab is where an operator is told about it.
                reading.Dispose();
                return null;
            }

            // Into a local and zeroed afterwards. The property hands out a fresh copy of the host's
            // key each time it is read, and a copy nobody clears is the one thing a dump would find
            // that `AuthorityMaterial.Dispose` went to the trouble of wiping.
            HostKeyMaterial host;
            var pkcs12 = reading.Material.HostPkcs12;
            try
            {
                host = HostKeyMaterial.From(pkcs12);
            }
            finally
            {
                if (pkcs12 != null)
                {
                    Array.Clear(pkcs12, 0, pkcs12.Length);
                }
            }

            if (host == null)
            {
                _log.Warn("a database's host certificate could not be read, so it is not served");
                reading.Dispose();
                return null;
            }

            // The certificate that will go on the wire against the one that was checked against the
            // authority. They are two readings of the same stored bytes by two different parsers,
            // and a PKCS#12 carrying more than one key entry is a thing a `.kdbx` can be crafted to
            // hold, so the one served has to be the one that was believed.
            if (!FixedTime.Equals(host.CertificateDer, reading.Material.Host.RawData))
            {
                _log.Warn("a database's host certificate is not the one its authority vouched for, " +
                          "so it is not served");
                reading.Dispose();
                return null;
            }

            var target = new MaterialTarget(
                database, reading, host, _writer, _log);
            var path = PathOf(database);

            // Open again, which is the other answer to what was kept when it went away. Without
            // this a database that came back with no unlock being announced would leave a gate key
            // held until KeePass exits.
            Discard(path);

            _cache[database] = new CachedTarget(stored, path, Responder(reading), target);
            _log.Info("serving a database with authority " + CertificateDigest.Shorten(target.DatabaseId));
            return target;
        }

        /// <summary>
        /// A responder for this database, minted while the authority is still available.
        /// </summary>
        /// <remarks>
        /// Called at both ends of a session. Once the first time the database is served after being
        /// opened, which is the mint at unlock `PAIRING.md` §308 names and is the fallback if the
        /// second one fails; and once at the lock this responder is about to answer, which is what
        /// leaves its whole life ahead of that lock. A failure here is not a failure to serve, so it
        /// is reported and the database is served without one, which costs a readable refusal while
        /// locked and nothing else.
        /// </remarks>
        private HostKeyMaterial Responder(AuthorityReading reading)
        {
            try
            {
                using (var responder = new AuthorityIssuer().IssueResponder(reading.Material.Authority))
                {
                    return HostKeyMaterial.From(CertificateBlob.Bytes(CertificateBlob.Save(responder)));
                }
            }
            catch (Exception error) when (
                Expected.OfCryptography(error) || error is InvalidOperationException)
            {
                // `InvalidOperationException` as well: that is what issuance raises for an authority
                // whose own dates have passed, and `AuthorityStore.Read` ignores expiry, so one reaches
                // here. Escaping left the target half built and made the resolver answer nothing for
                // every open database, not only the expired one.
                _log.Error("a responder certificate could not be minted, so a lock will answer nothing", error);
                return null;
            }
        }

        private static string PathOf(PwDatabase database)
        {
            var connection = database.IOConnectionInfo;
            return connection == null ? null : connection.Path;
        }

        private sealed class CachedTarget : IDisposable
        {
            private readonly string _stored;
            private string _path;
            private readonly HostKeyMaterial _responder;
            private readonly MaterialTarget _target;

            public CachedTarget(string stored, string path, HostKeyMaterial responder, MaterialTarget target)
            {
                _stored = stored;
                _path = path;
                _responder = responder;
                _target = target;
            }

            public MaterialTarget Target
            {
                get { return _target; }
            }

            /// <summary>
            /// Keeps the remembered path level with the file, when there is one to read.
            /// </summary>
            /// <remarks>
            /// `SaveAs` moves `IOConnectionInfo.Path` under the same `PwDatabase`, measured on
            /// KeePass 2.61.1, so a path taken once when this was first served goes stale and the
            /// lock that follows matches nothing. Refreshed only while there is a path: a lock is
            /// announced after the database is closed, and a closed one has no path at all.
            ///
            /// KeePass announces a Save As only as a save, which this plugin does not listen for,
            /// so what catches it is the read just before the close,
            /// <see cref="OpenDatabaseTargets.Refresh"/>. Following only on a connection left a
            /// Save As and a lock with none in between matching nothing.
            /// </remarks>
            public void Follow(string path)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    _path = path;
                }
            }

            /// <summary>The file this was read from, which is how a lock finds it again.</summary>
            public string Path
            {
                get { return _path; }
            }

            /// <summary>What will answer once this database is locked. Null when none could be minted.</summary>
            public HostKeyMaterial Responder
            {
                get { return _responder; }
            }

            public bool Matches(string stored)
            {
                return string.Equals(_stored, stored, StringComparison.Ordinal);
            }

            public void Dispose()
            {
                _target.Dispose();
            }
        }

        /// <summary>One database's authority, held open for as long as it is being served.</summary>
        private sealed class MaterialTarget : IGateTarget
        {
            private readonly AuthorityReading _reading;
            private readonly HostKeyMaterial _host;
            private readonly string _databaseId;
            private PwDatabase _database;
            private readonly AdmissionWriter _writer;
            private readonly IGateLog _log;

            // The cache holds the first of these; `Current` takes one more per caller.
            private int _holders = 1;

            public MaterialTarget(
                PwDatabase database,
                AuthorityReading reading,
                HostKeyMaterial host,
                AdmissionWriter writer,
                IGateLog log)
            {
                _database = database;
                _reading = reading;
                _host = host;
                _log = log ?? NullGateLog.Instance;

                // Taken once. It is a digest over bytes that cannot change while this target lives,
                // and the connection path reads it twice, so recomputing it per read is a hash and
                // a certificate export for an answer that was already known.
                _databaseId = reading.Material.AuthorityDigest;
                _writer = writer;
            }

            public string DatabaseId
            {
                get { return _databaseId; }
            }

            public byte[] GateKey
            {
                get { return _reading.Material.SniKey; }
            }

            /// <summary>What this was built from, which is what a fresh responder is minted from.</summary>
            public AuthorityReading Reading
            {
                get { return _reading; }
            }

            public HostKeyMaterial Host
            {
                get { return _host; }
            }

            public X509Certificate2 Authority
            {
                get { return _reading.Material.Authority; }
            }

            /// <summary>Takes a reference, for a caller about to use this outside the cache lock.</summary>
            public void Retain()
            {
                Interlocked.Increment(ref _holders);
            }

            /// <summary>
            /// What KeePass calls this database, read now because it can be renamed while it is
            /// open, and empty when it has no name and no file to be named after.
            /// </summary>
            public string DatabaseName
            {
                get
                {
                    var database = _database;
                    if (database == null)
                    {
                        return string.Empty;
                    }

                    // The name the operator gave it in the database settings, which is the one they
                    // recognize. Most databases have never been given one, so the file it was
                    // opened from is the fallback: the file name alone, never the path, because
                    // this is read out onto a dialog and a path says where somebody keeps their
                    // passwords.
                    var named = database.Name;
                    if (!string.IsNullOrEmpty(named))
                    {
                        return named;
                    }

                    var connection = database.IOConnectionInfo;
                    return connection == null || string.IsNullOrEmpty(connection.Path)
                        ? string.Empty
                        : UrlUtil.GetFileName(connection.Path);
                }
            }

            public string AdmissionBlocked
            {
                get
                {
                    return _writer == null
                        ? "this database is served without a way to write to it"
                        : _writer.Blocked(_database);
                }
            }

            public AdmissionWrite Admit(AdmissionRecord record)
            {
                if (_writer == null)
                {
                    return AdmissionWrite.Failed("this database is served without a way to write to it");
                }

                return _writer.Write(_database, record);
            }

            /// <summary>Never: a locked database is served by <see cref="LockedTarget"/> instead.</summary>
            public bool IsLocked
            {
                get { return false; }
            }

            /// <summary>
            /// Gives a reference back, and releases the material when the last holder does.
            /// </summary>
            /// <remarks>
            /// Counted, because the cache and a connection hold the same target at the same time.
            /// Replacing a cached entry used to reset the certificates a connection in flight was
            /// validating against, and the client was refused with a 401 it had done nothing to
            /// earn. Disposal is still prompt; it is prompt at the moment the last holder lets go
            /// rather than at the moment the next connection asks.
            /// </remarks>
            public void Dispose()
            {
                // The database goes with the last reference, so a target nobody holds answers
                // nothing rather than answering out of a live `PwDatabase` while its own
                // certificates have already been reset. `Admission` reads the group on every call,
                // and without this it would go on reporting clients as admitted.
                if (Interlocked.Decrement(ref _holders) == 0)
                {
                    _database = null;
                    _reading.Dispose();
                }
            }

            /// <summary>
            /// The record admitting this certificate, read now and from the custom data the
            /// database holds now.
            /// </summary>
            /// <remarks>
            /// **The store reads through the `PwDatabase`, and that is the whole of it.** A target
            /// is cached while the authority is unchanged, and a database that is locked and opened
            /// again comes back with its custom data replaced behind the same `PwDatabase`, so
            /// anything holding the dictionary itself would go on reading the one that was
            /// replaced: a revoked client would keep being admitted and a newly paired one would
            /// never be. `CustomDataStore` asks the database for its dictionary on every access,
            /// and is built per call besides, as `OpenDatabase` builds its own per read.
            ///
            /// **What it does not do is keep the database out of the cache.** The target holds the
            /// `PwDatabase` and the cache is keyed on it, so a target that outlives its usefulness
            /// until the next connection prunes it pins that object. It pins no entries, because
            /// KeePass empties a `PwDatabase` when it locks or closes it, and the last holder to let
            /// go of the target drops the reference besides.
            /// </remarks>
            public AdmissionRecord Admission(string admissionKey)
            {
                var root = _database == null ? null : _database.RootGroup;
                if (root == null)
                {
                    // Locked, between this and the check `Current` made, nearly always: KeePass
                    // empties the database before it says why. Not answered as no record, which
                    // told an admitted client `401 unpaired`; the transport answers a locked
                    // database `503 locked`, and this says the same.
                    throw new DatabaseShut("the database closed under this connection");
                }

                return new AdmissionStore(new CustomDataStore(_database), _log).Find(admissionKey);
            }
        }
    }
}
