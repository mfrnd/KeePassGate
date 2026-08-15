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
    public sealed class OpenDatabaseTargets : IGateTargets, IDisposable
    {
        private readonly Func<IEnumerable<PwDatabase>> _openDatabases;
        private readonly AdmissionWriter _writer;
        private readonly IGateLog _log;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<PwDatabase, CachedTarget> _cache =
            new Dictionary<PwDatabase, CachedTarget>();

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
            }
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
                _cache[database].Dispose();
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
            _cache[database] = new CachedTarget(stored, target);
            _log.Info("serving a database with authority " + CertificateDigest.Shorten(target.DatabaseId));
            return target;
        }

        private sealed class CachedTarget : IDisposable
        {
            private readonly string _stored;
            private readonly MaterialTarget _target;

            public CachedTarget(string stored, MaterialTarget target)
            {
                _stored = stored;
                _target = target;
            }

            public MaterialTarget Target
            {
                get { return _target; }
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
                    // Locked, between this and the check `Current` made. Nothing is admitted by a
                    // database that is not open, which is the same answer the transport gives.
                    return null;
                }

                return new AdmissionStore(new CustomDataStore(_database), _log).Find(admissionKey);
            }
        }
    }
}
