using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Store;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A database as the transport sees it, with a real authority behind it: real certificates,
    /// really signed, and a real gate key. Nothing here is a stub except the database, whose
    /// store is a `MemoryStore` holding that authority, written and read by the product's own
    /// acts and readings.
    /// </summary>
    internal sealed class TestAuthority : IGateTarget
    {
        // The fixture holds the first; `FakeGateTargets.Current` takes one per caller.
        private int _holders = 1;

        private readonly CertifiedKey _authorityKey;
        private readonly X509Certificate2 _authority;
        private readonly X509Certificate2 _host;
        private readonly CertifiedKey _hostKey;
        private readonly byte[] _gateKey;
        private readonly List<X509Certificate2> _issued = new List<X509Certificate2>();
        private readonly MemoryStore _store = new MemoryStore();
        private readonly InMemoryKeyValueStore _answers = new InMemoryKeyValueStore();
        private readonly RenewalAnswers _renewals;

        private TestAuthority(
            CertifiedKey authority, CertifiedKey host, byte[] gateKey)
        {
            _authorityKey = authority;
            _authority = authority.Read();
            _host = host.Read();
            _hostKey = host;
            _gateKey = gateKey;
            _renewals = new RenewalAnswers(_answers, NullGateLog.Instance);
            using (var material = new AuthorityMaterial(authority, host, gateKey))
            {
                StoreSeeds.AuthorityOf(_store, material);
            }

            DatabaseName = "Invented Vault";
        }

        /// <summary>The database's store, as the product writes and reads it.</summary>
        public MemoryStore Store
        {
            get { return _store; }
        }

        public static TestAuthority New()
        {
            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();

            // Kept in both forms, as the plugin keeps them: the key material the managed TLS
            // server serves with, and a Windows certificate for the tests that only want to read it.
            var host = issuer.IssueHost(authority);

            var gateKey = new byte[AuthorityMaterial.SniKeyLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(gateKey);
            }

            return new TestAuthority(authority, host, gateKey);
        }

        public string DatabaseId
        {
            get { return CertificateDigest.Of(_authority); }
        }

        /// <summary>What a screen would call it. Invented, like everything else here.</summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// The profiles this database's store names. Settable, because what the approval dialog
        /// marks as new is exactly what is not in here, and a name taken out of it is a name no
        /// client holds: setting it adds and removes names through the product's own acts.
        /// </summary>
        /// <remarks>
        /// Admitting a client names its profiles, because that is what pairing does for real: the
        /// admission's act adds each name the list does not hold, and a client whose names were not
        /// on the list would reach nothing.
        /// </remarks>
        public IReadOnlyList<string> KnownProfiles
        {
            get
            {
                using (var reading = _store.Read())
                {
                    return ProfileNames.Of(reading).Names;
                }
            }
            set
            {
                var wanted = new HashSet<string>(value ?? Array.Empty<string>(), StringComparer.Ordinal);
                foreach (var profile in KnownProfiles.Where(profile => !wanted.Contains(profile)))
                {
                    StoreSeeds.Unname(_store, profile);
                }

                Name(wanted);
            }
        }

        /// <summary>Takes a name off the list, as the Profiles page does.</summary>
        public void Forget(string profile)
        {
            if (KnownProfiles.Contains(profile, StringComparer.Ordinal))
            {
                StoreSeeds.Unname(_store, profile);
            }
        }

        /// <summary>Adds each of these names the list does not hold, as the Profiles page does.</summary>
        public void Name(IEnumerable<string> profiles)
        {
            var known = new HashSet<string>(KnownProfiles, StringComparer.Ordinal);
            foreach (var profile in (profiles ?? Array.Empty<string>()).Where(profile => profile != null && known.Add(profile)))
            {
                StoreSeeds.Name(_store, profile);
            }
        }

        public byte[] GateKey
        {
            get { return (byte[])_gateKey.Clone(); }
        }

        public CertifiedKey Host
        {
            get { return _hostKey; }
        }

        /// <summary>The same certificate as Windows sees it, for tests that compare what was sent.</summary>
        public X509Certificate2 HostCertificate
        {
            get { return _host; }
        }

        public X509Certificate2 Authority
        {
            get { return _authority; }
        }

        public CertifiedKey AuthorityKey
        {
            get { return _authorityKey; }
        }

        /// <summary>Set to make listing the records fail, as a write racing the read does.</summary>
        public Exception AdmissionThrows { get; set; }

        /// <summary>
        /// The admission of a certificate, as the gate reads it from the store, with the profiles
        /// this database names.
        /// </summary>
        public Admitted Admission(string admissionKey)
        {
            if (AdmissionThrows != null)
            {
                throw AdmissionThrows;
            }

            using (var reading = _store.Read())
            {
                var record = StoreAdmissions.Admitted(reading, admissionKey);
                return record == null ? null : new Admitted(record, ProfileNames.Of(reading).Names);
            }
        }

        /// <summary>
        /// Set to stand for a database that closed after a pairing's connection was judged: the
        /// dialog's reading throws, as the real target's does.
        /// </summary>
        public bool Shut { get; set; }

        /// <summary>Set to make the dialog's reading fail, as a reading of a store that cannot be made does.</summary>
        public Exception AskedThrows { get; set; }

        /// <summary>
        /// What this fake's store holds of the profiles a pairing asks for, read from a single
        /// reading with the store key as the real target reads it, where the store serves this
        /// authority, and the word the write answers otherwise, judged as the real target judges
        /// it. Throws as the real target does where the database has gone, <see cref="Shut"/>.
        /// </summary>
        public AskedProfiles Asked(IReadOnlyCollection<string> profiles)
        {
            if (Shut)
            {
                throw new KeePassGate.Diagnostics.DatabaseShut("the database closed before the dialog was read");
            }

            if (AskedThrows != null)
            {
                throw AskedThrows;
            }

            using (var reading = _store.Read())
            {
                return StoreReadings.Asked(reading, DatabaseId, profiles, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// Set to refuse the write, which is how a test stands in for a group dialog being open.
        /// </summary>
        public string AdmissionBlocked { get; set; }

        /// <summary>
        /// Nothing. This fake is about the transport, and the transport carries a vault without
        /// ever asking it anything, so there is nothing here to carry.
        /// </summary>
        public KeePassGate.Vault.IVault Vault
        {
            get { return null; }
        }

        /// <summary>Never: a locked database is a `LockedTarget`, which this is not.</summary>
        public bool IsLocked
        {
            get { return false; }
        }

        private string _unavailable;

        /// <summary>
        /// Set to the word a database nobody is served from for now answers with, and null while
        /// this database is served.
        /// </summary>
        public string Unavailable
        {
            get
            {
                if (UnavailableThrows != null)
                {
                    throw UnavailableThrows;
                }

                return _unavailable;
            }

            set
            {
                _unavailable = value;
            }
        }

        /// <summary>
        /// Set to make asking whether the database is served fail, as a reading of its store that
        /// could not be made does.
        /// </summary>
        public Exception UnavailableThrows { get; set; }

        /// <summary>Set to fail the write after it was allowed to start.</summary>
        public AdmissionWrite AdmitAnswers { get; set; }

        /// <summary>Set to have the write throw rather than answer, which must not reach the client.</summary>
        public Exception AdmitThrows { get; set; }

        /// <summary>What the last pairing wrote, so a test can look at it.</summary>
        public AdmissionRecord Admitted { get; private set; }

        /// <summary>
        /// Run once a write has landed and its audit line is written, where the writer tells the
        /// Clients page. A test sets it to throw, to stand for a defect in that listener.
        /// </summary>
        public Action WhenLanded { get; set; }

        /// <summary>
        /// Run inside the write, where the writer holds its lock, before its plan is made and its
        /// condition asked: a test stands a revocation or a change to the list there that landed
        /// just before.
        /// </summary>
        public Action BeforeWrite { get; set; }

        /// <summary>
        /// Set where this database has retired its authority, as recreating it does: the write's
        /// plan then leaves the admission out.
        /// </summary>
        public bool Retired { get; set; }

        /// <summary>
        /// What a write's condition is held to: the admissions and the profile names of this fake's
        /// store, read in the write's turn.
        /// </summary>
        private sealed class ConditionGround : IWriteGround
        {
            private readonly StoreReading _reading;

            public ConditionGround(StoreReading reading)
            {
                _reading = reading;
            }

            public AdmissionRecord Admission(string admissionKey)
            {
                return StoreAdmissions.Admitted(_reading, admissionKey);
            }

            public bool Named(string profile)
            {
                return ProfileNames.Of(_reading).Holds(profile);
            }

            public IReadOnlyCollection<string> Removals(string profile)
            {
                return _reading.InForce == null ? (IReadOnlyCollection<string>)Array.Empty<string>() : _reading.InForce.RemovalsOnceKnown(profile);
            }

            public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Regaining(IReadOnlyCollection<string> profiles)
            {
                return AskedProfiles.Regaining(_reading, profiles, DateTimeOffset.UtcNow);
            }
        }

        /// <summary>
        /// An admission, written into this fake's store by the product's own act, with its audit
        /// line written as the writer writes it: once, when the admission lands, and before
        /// anything else hears of it.
        /// </summary>
        public AdmissionWrite Admit(AdmissionRecord record, Func<IWriteGround, string> condition, Action audit)
        {
            try
            {
                return Admitting(record, condition, audit);
            }
            finally
            {
                if (AfterWrite != null)
                {
                    AfterWrite();
                }
            }
        }

        /// <summary>
        /// Run once a write has answered, whatever it answered: a test stands there what lands
        /// between a write and what the handler does next.
        /// </summary>
        public Action AfterWrite { get; set; }

        /// <summary>
        /// Set where the database closed under the write: the write finds nothing to write to and
        /// answers so before its condition is asked, as the writer does.
        /// </summary>
        public bool ClosedUnderTheWrite { get; set; }

        private AdmissionWrite Admitting(AdmissionRecord record, Func<IWriteGround, string> condition, Action audit)
        {
            if (AdmitThrows != null)
            {
                throw AdmitThrows;
            }

            // Busy and the store's word are what the writer answers before its condition is
            // asked, so a canned answer of either is given without asking it, and so is an issuer
            // the store does not serve, below; every other answer comes after it, as the
            // writer's do.
            if (AdmitAnswers != null && (AdmitAnswers.IsBusy || AdmitAnswers.Word != null))
            {
                return AdmitAnswers;
            }

            if (BeforeWrite != null)
            {
                BeforeWrite();
            }

            if (ClosedUnderTheWrite)
            {
                return AdmissionWrite.Failed("the database is not open");
            }

            using (var reading = _store.Read())
            {
                var word = StoreReadings.WordOf(reading, record.Issuer);
                if (word != null)
                {
                    return AdmissionWrite.Unavailable(word);
                }

                // The writer's plan leaves an admission out, before its condition is asked, where
                // the authority that issued the certificate is not the authority served alone.
                if (Retired || !string.Equals(StoreAuthorities.Served(reading), record.Issuer, StringComparison.Ordinal))
                {
                    return AdmissionWrite.Unserved("the authority that issued its certificate does not stand alone");
                }

                if (condition != null)
                {
                    var withdrawn = condition(new ConditionGround(reading));
                    if (withdrawn != null)
                    {
                        return AdmissionWrite.Failed(withdrawn);
                    }
                }

                if (AdmitAnswers != null)
                {
                    if (AdmitAnswers.Ok)
                    {
                        Landed(audit);
                    }

                    return AdmitAnswers;
                }

                var written = Write(AdmissionActs.Admit(reading, record));
                if (written != null)
                {
                    return AdmissionWrite.Failed(written);
                }
            }

            Admitted = record;
            Landed(audit);
            return AdmissionWrite.Done();
        }

        /// <summary>
        /// Writes an act into this fake's store, answering why it was refused, or null.
        /// </summary>
        private string Write(StoreAct act)
        {
            if (act == null)
            {
                return "the store holds no key";
            }

            try
            {
                var written = _store.Write(new[] { act });
                return written.Taken ? null : written.Reason;
            }
            finally
            {
                act.Forget();
            }
        }

        /// <summary>
        /// Set to answer a client's giving up of its own certificate with something other than
        /// doing it.
        /// </summary>
        public KeePassGate.Vault.VaultWrite GiveUpAnswers { get; set; }

        /// <summary>Set to make reading a standing answer fail, as a database that will not read does.</summary>
        public Exception StandingRefusalThrows { get; set; }

        /// <summary>The operator's standing answers to renewals, as the gate reads them.</summary>
        public RenewalRefusal StandingRefusal(string admissionKey)
        {
            if (StandingRefusalThrows != null)
            {
                throw StandingRefusalThrows;
            }

            return _renewals.StandingRefusal(admissionKey);
        }

        /// <summary>Set to answer a standing refusal's write with something other than doing it.</summary>
        public KeePassGate.Vault.VaultWrite RefuseRenewalAnswers { get; set; }

        /// <summary>
        /// Run while a standing answer is on its way to the database, before it lands, which is how
        /// a test puts a request in the gap between the dialog closing and the write.
        /// </summary>
        public Action WhileRefusing { get; set; }

        /// <summary>A standing answer, written in this fake's own records.</summary>
        public KeePassGate.Vault.VaultWrite RefuseRenewal(string admissionKey, RenewalRefusal refusal)
        {
            if (WhileRefusing != null)
            {
                WhileRefusing();
            }

            if (RefuseRenewalAnswers != null)
            {
                return RefuseRenewalAnswers;
            }

            _renewals.RefuseRenewal(admissionKey, refusal);
            return KeePassGate.Vault.VaultWrite.Done();
        }

        /// <summary>Takes a standing answer away, as the clients page's Allow renewal does at OK.</summary>
        public bool LiftRenewalRefusal(string admissionKey)
        {
            return _renewals.LiftRenewalRefusal(admissionKey);
        }

        /// <summary>
        /// A client's giving up of its own certificate, written into this fake's store by the
        /// product's own act, with its audit line written as the writer writes it: whenever the
        /// record is left in force, saved and verified or not verified, and never otherwise.
        /// </summary>
        public KeePassGate.Vault.VaultWrite GiveUp(string admissionKey, Action audit)
        {
            if (GiveUpAnswers != null)
            {
                if (GiveUpAnswers.Ok || GiveUpAnswers.IsUnverified)
                {
                    Landed(audit);
                }

                return GiveUpAnswers;
            }

            using (var reading = _store.Read())
            {
                var word = StoreReadings.WordOf(reading, DatabaseId);
                if (word != null)
                {
                    return KeePassGate.Vault.VaultWrite.Unavailable(word);
                }

                if (StoreAdmissions.Admitted(reading, admissionKey) == null)
                {
                    return KeePassGate.Vault.VaultWrite.Missing();
                }

                var refused = Write(AdmissionActs.TakeAway(reading, admissionKey));
                if (refused != null)
                {
                    return KeePassGate.Vault.VaultWrite.Failed(refused);
                }
            }

            Landed(audit);
            return KeePassGate.Vault.VaultWrite.Done();
        }

        /// <summary>The writer's order for a change that landed: the line, and then the listener.</summary>
        private void Landed(Action audit)
        {
            if (audit != null)
            {
                audit();
            }

            if (WhenLanded != null)
            {
                WhenLanded();
            }
        }

        /// <summary>
        /// Admits a client, which is what pairing does for real. Without this a certificate this
        /// authority issued opens nothing, which is the point of the admission record.
        /// </summary>
        public void Admit(X509Certificate2 certificate, params string[] profiles)
        {
            StoreSeeds.Admit(_store, AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles, DatabaseId));
        }

        /// <summary>
        /// Revocation of the client this certificate was admitted for, as the clients page writes
        /// it at OK, and every certificate of that client stops working; false where the
        /// certificate is not admitted.
        /// </summary>
        public bool Revoke(X509Certificate2 certificate)
        {
            AdmissionRecord admission;
            using (var reading = _store.Read())
            {
                admission = StoreAdmissions.Admitted(reading, CertificateDigest.Of(certificate));
            }

            if (admission == null)
            {
                return false;
            }

            StoreSeeds.Revoke(_store, admission.ClientKey);
            return true;
        }

        /// <summary>A certificate issued by this authority and admitted in one step.</summary>
        public X509Certificate2 IssueAdmittedClient(params string[] profiles)
        {
            var certificate = IssueClient("agent-fictitious", profiles);
            Admit(certificate, profiles);
            return certificate;
        }

        /// <summary>
        /// The same, as the certificate and key Bouncy Castle holds, for a client that is not
        /// SChannel.
        /// </summary>
        public CertifiedKey IssueAdmittedClientKey(params string[] profiles)
        {
            var issued = IssueClientKey("agent-fictitious", profiles);
            using (var certificate = issued.Read())
            {
                Admit(certificate, profiles);
            }

            return issued;
        }

        /// <summary>
        /// A client certificate and its key as Bouncy Castle holds them, for a test that needs the
        /// stored form or a client that is not SChannel.
        /// </summary>
        public CertifiedKey IssueClientKey(string subject, params string[] profiles)
        {
            return new AuthorityIssuer().IssueClient(_authorityKey, subject, profiles);
        }

        /// <summary>A name this database will answer to, fresh each time.</summary>
        public string NewName()
        {
            return SniName.For(_gateKey);
        }

        public X509Certificate2 IssueClient(string subject, params string[] profiles)
        {
            return IssueClient(subject, Lifetime.Unasked, profiles);
        }

        /// <summary>The same, lasting as long as the caller says.</summary>
        public X509Certificate2 IssueClient(string subject, TimeSpan lifetime, params string[] profiles)
        {
            // The test client is an SslStream, which will not present a certificate whose key is
            // ephemeral, so this one is loaded with a container. The plugin itself never does that:
            // only the client side of these tests still goes through SChannel.
            var certificate = new AuthorityIssuer().IssueClient(_authorityKey, subject, profiles, lifetime).WithKeyFile();
            _issued.Add(certificate);
            return certificate;
        }

        /// <summary>
        /// A client certificate that ended days ago, from an authority of its own made a week ago,
        /// for the caller to dispose.
        /// </summary>
        /// <remarks>
        /// The one way to hold a certificate that has expired at the real moment rather than at a
        /// moment a test passes in: nothing can be issued from before its authority began, so the
        /// authority has to be older than the certificate. Whose authority it is does not matter
        /// to anything that reads a record's dates.
        /// </remarks>
        public static X509Certificate2 IssueExpiredClient(string subject, params string[] profiles)
        {
            var then = DateTimeOffset.UtcNow.AddDays(-7);
            var issuer = new AuthorityIssuer(() => then);

            return issuer.IssueClient(issuer.CreateAuthority(), subject, profiles, TimeSpan.FromDays(1)).Read();
        }

        /// <summary>
        /// The host certificate as something an SslStream client can actually present, which means
        /// a key with a container. It must still be refused, on its extended key usage rather than
        /// on anything about its key.
        /// </summary>
        public X509Certificate2 HostCertificateAsClient()
        {
            var certificate = _hostKey.WithKeyFile();
            _issued.Add(certificate);
            return certificate;
        }

        /// <summary>Takes a reference, as the real cache does when it hands a target out.</summary>
        public void Retain()
        {
            System.Threading.Interlocked.Increment(ref _holders);
        }

        /// <summary>
        /// How many references are out, so a test can see one that was never given back.
        /// </summary>
        /// <remarks>
        /// The fixture holds the first, so this reads one when nothing else is holding it. A
        /// reference the resolver keeps and never hands out is invisible in every other way: the
        /// connection is refused, nothing throws, and the material simply stays in memory.
        /// </remarks>
        public int Holders
        {
            get { return System.Threading.Volatile.Read(ref _holders); }
        }

        /// <summary>
        /// Gives one back, and releases only when the fixture itself lets go.
        /// </summary>
        /// <remarks>
        /// Counted for the same reason the real target is: a connection releases what it was
        /// handed, and a fake that took that literally would destroy the fixture's certificates
        /// after the first connection. The fixture holds the first reference and gives it back in
        /// its own teardown.
        /// </remarks>
        public void Dispose()
        {
            if (System.Threading.Interlocked.Decrement(ref _holders) != 0)
            {
                return;
            }

            foreach (var certificate in _issued)
            {
                certificate.Reset();
            }

            _issued.Clear();
            _host.Reset();
            _authority.Reset();
            Array.Clear(_gateKey, 0, _gateKey.Length);
        }
    }

    /// <summary>The databases a gate can serve, as a test decides them.</summary>
    internal sealed class FakeGateTargets : IGateTargets
    {
        private readonly List<IGateTarget> _targets = new List<IGateTarget>();

        public FakeGateTargets(params IGateTarget[] targets)
        {
            _targets.AddRange(targets);
        }

        public Exception Throws { get; set; }

        public int Asked { get; private set; }

        /// <summary>
        /// Run each time a connection asks, before it is answered, so a test can hold a name
        /// part way through resolving. Null for nothing.
        /// </summary>
        public Action Asking { get; set; }

        public void Add(IGateTarget target)
        {
            _targets.Add(target);
        }

        public void Clear()
        {
            _targets.Clear();
        }

        public IReadOnlyList<IGateTarget> Current()
        {
            Asked++;

            var asking = Asking;
            if (asking != null)
            {
                asking();
            }

            if (Throws != null)
            {
                throw Throws;
            }

            // One reference per caller, as the real source takes, locked targets included: handing
            // one out bare is the defect the real source was fixed for, and a fake that did it
            // would let that fix go without a test noticing. Whoever asked gives them back, and
            // the fixture keeps holding its own until teardown.
            foreach (var target in _targets)
            {
                var counted = target as TestAuthority;
                if (counted != null)
                {
                    counted.Retain();
                }

                var locked = target as LockedTarget;
                if (locked != null)
                {
                    locked.Retain();
                }

                var undecided = target as HeldBackTarget;
                if (undecided != null)
                {
                    undecided.Retain();
                }
            }

            return _targets;
        }
    }
}
