using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A database as the transport sees it, with a real authority behind it: real certificates,
    /// really signed, and a real gate key. Nothing here is a stub except the database.
    /// </summary>
    internal sealed class TestAuthority : IGateTarget
    {
        // The fixture holds the first; `FakeGateTargets.Current` takes one per caller.
        private int _holders = 1;

        private readonly X509Certificate2 _authority;
        private readonly X509Certificate2 _host;
        private readonly string _hostBlob;
        private readonly HostKeyMaterial _hostKey;
        private readonly byte[] _gateKey;
        private readonly List<X509Certificate2> _issued = new List<X509Certificate2>();
        private readonly InMemoryKeyValueStore _records = new InMemoryKeyValueStore();
        private readonly AdmissionStore _admissions;
        private readonly List<string> _named = new List<string>();

        private TestAuthority(
            X509Certificate2 authority, X509Certificate2 host, string hostBlob, HostKeyMaterial hostKey, byte[] gateKey)
        {
            _authority = authority;
            _host = host;
            _hostBlob = hostBlob;
            _hostKey = hostKey;
            _gateKey = gateKey;
            _admissions = new AdmissionStore(_records, NullGateLog.Instance);

            DatabaseName = "Invented Vault";
        }

        public static TestAuthority New()
        {
            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();

            // Kept in both forms: the key material the managed TLS server serves with, and a
            // Windows certificate for the tests that only want to read it.
            var host = issuer.IssueHost(authority);
            var stored = CertificateBlob.Save(host);
            var hostKey = HostKeyMaterial.From(CertificateBlob.Bytes(stored));

            var gateKey = new byte[AuthorityMaterial.SniKeyLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(gateKey);
            }

            return new TestAuthority(authority, host, stored, hostKey, gateKey);
        }

        public string DatabaseId
        {
            get { return CertificateDigest.Of(_authority); }
        }

        /// <summary>What a screen would call it. Invented, like everything else here.</summary>
        public string DatabaseName { get; set; }

        /// <summary>
        /// The profiles this database names. Settable, because what the approval dialog marks as
        /// new is exactly what is not in here, and a name taken out of it is one no client holds.
        /// </summary>
        /// <remarks>
        /// Admitting a client names its profiles, because that is what pairing does for real: a
        /// record is written in the same operation as the names it holds, and a client whose names
        /// were not on the list would reach nothing.
        /// </remarks>
        public IReadOnlyList<string> KnownProfiles
        {
            get { return _named.ToArray(); }
            set
            {
                _named.Clear();
                Name(value);
            }
        }

        /// <summary>Set to say the list of profiles will not read, as a damaged value would.</summary>
        public string ProfilesUnreadable { get; set; }

        /// <summary>Takes a name off the list, as the Profiles page does.</summary>
        public void Forget(string profile)
        {
            _named.Remove(profile);
        }

        private void Name(IEnumerable<string> profiles)
        {
            foreach (var profile in profiles ?? Array.Empty<string>())
            {
                if (profile != null && !_named.Contains(profile))
                {
                    _named.Add(profile);
                }
            }
        }


        public byte[] GateKey
        {
            get { return (byte[])_gateKey.Clone(); }
        }

        public HostKeyMaterial Host
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

        /// <summary>Set to make listing the records fail, as a write racing the read does.</summary>
        public Exception AdmissionThrows { get; set; }

        /// <summary>The records this database holds, as the gate reads them.</summary>
        public AdmissionRecord Admission(string admissionKey)
        {
            if (AdmissionThrows != null)
            {
                throw AdmissionThrows;
            }

            return _admissions.Find(admissionKey);
        }

        /// <summary>The admitted clients whose records hold a profile.</summary>
        public IReadOnlyList<AdmissionRecord> Holding(string profile)
        {
            var holding = new List<AdmissionRecord>();

            foreach (var record in _admissions.All().Records)
            {
                if (System.Linq.Enumerable.Contains(record.Profiles, profile))
                {
                    holding.Add(record);
                }
            }

            return holding;
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

        /// <summary>Never: this one is always served.</summary>
        public string Unavailable
        {
            get { return null; }
        }

        /// <summary>Set to fail the write after it was allowed to start.</summary>
        public AdmissionWrite AdmitAnswers { get; set; }

        /// <summary>Set to have the write throw rather than answer, which must not reach the client.</summary>
        public Exception AdmitThrows { get; set; }

        /// <summary>What the last pairing wrote, so a test can look at it.</summary>
        public AdmissionRecord Admitted { get; private set; }

        /// <summary>
        /// Run inside the write, where the writer holds its lock, before its condition is asked: a
        /// test stands a revocation or a change to the list there that landed just before.
        /// </summary>
        public Action BeforeWrite { get; set; }

        /// <summary>Set to say this database has retired its authority, as recreating it does.</summary>
        public bool Retired { get; set; }

        /// <summary>
        /// This fake's records, its list of profiles and its authority as one database's custom
        /// data, which is what a write's condition reads.
        /// </summary>
        private sealed class ConditionStore : IKeyValueStore
        {
            private readonly TestAuthority _authority;

            public ConditionStore(TestAuthority authority)
            {
                _authority = authority;
            }

            public IEnumerable<string> Keys
            {
                get { return _authority._records.Keys; }
            }

            public string Get(string key)
            {
                if (key == KeePassGate.Acl.ProfileNames.CustomDataKey)
                {
                    if (_authority.ProfilesUnreadable != null)
                    {
                        return "{ not a list";
                    }

                    var names = KeePassGate.Acl.ProfileNames.Read(null);
                    foreach (var name in _authority._named)
                    {
                        names.Add(name);
                    }

                    return names.Write();
                }

                if (key == AuthorityKeys.AuthorityKey(_authority.DatabaseId))
                {
                    return "the authority";
                }

                if (key == AuthorityKeys.RetirementKey(_authority.DatabaseId))
                {
                    return _authority.Retired ? "retired" : null;
                }

                return _authority._records.Get(key);
            }

            public void Set(string key, string value)
            {
                _authority._records.Set(key, value);
            }

            public void Remove(string key)
            {
                _authority._records.Remove(key);
            }
        }

        public AdmissionWrite Admit(AdmissionRecord record, Func<IKeyValueStore, string> condition)
        {
            if (AdmitThrows != null)
            {
                throw AdmitThrows;
            }

            // Busy is what the writer answers before its condition is asked, so the condition is
            // not asked; every other answer comes after it, as the writer's do.
            if (AdmitAnswers != null && AdmitAnswers.IsBusy)
            {
                return AdmitAnswers;
            }

            if (BeforeWrite != null)
            {
                BeforeWrite();
            }

            if (condition != null)
            {
                var withdrawn = condition(new ConditionStore(this));
                if (withdrawn != null)
                {
                    return AdmissionWrite.Withdrawn(withdrawn);
                }
            }

            if (AdmitAnswers != null)
            {
                return AdmitAnswers;
            }

            _admissions.Write(record);
            Name(record.Profiles);
            Admitted = record;
            return AdmissionWrite.Done();
        }

        /// <summary>Set to answer a client's revocation of itself with something other than doing it.</summary>
        public KeePassGate.Vault.VaultWrite RevokeAnswers { get; set; }

        /// <summary>Set to make reading a standing answer fail, as a database that will not read does.</summary>
        public Exception StandingRefusalThrows { get; set; }

        /// <summary>The operator's standing answers to renewals, as the gate reads them.</summary>
        public RenewalRefusal StandingRefusal(string admissionKey)
        {
            if (StandingRefusalThrows != null)
            {
                throw StandingRefusalThrows;
            }

            return _admissions.StandingRefusal(admissionKey);
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

            _admissions.RefuseRenewal(admissionKey, refusal);
            return KeePassGate.Vault.VaultWrite.Done();
        }

        /// <summary>A client's revocation of itself, made in this fake's own records.</summary>
        public KeePassGate.Vault.VaultWrite Revoke(string admissionKey)
        {
            if (RevokeAnswers != null)
            {
                return RevokeAnswers;
            }

            if (_admissions.Find(admissionKey) == null)
            {
                return KeePassGate.Vault.VaultWrite.Missing();
            }

            return _admissions.Remove(admissionKey)
                ? KeePassGate.Vault.VaultWrite.Done()
                : KeePassGate.Vault.VaultWrite.Failed("the revocation did not land");
        }

        /// <summary>
        /// Admits a client, which is what pairing will do for real. Without this a certificate this
        /// authority issued opens nothing, which is the point of step 7.
        /// </summary>
        public void Admit(X509Certificate2 certificate, params string[] profiles)
        {
            _admissions.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles));
            Name(profiles);
        }

        /// <summary>
        /// Revocation: a revocation beside the record, the record emptied, and the certificate
        /// stops working.
        /// </summary>
        public bool Revoke(X509Certificate2 certificate)
        {
            return _admissions.Remove(CertificateDigest.Of(certificate));
        }

        /// <summary>A certificate issued by this authority and admitted in one step.</summary>
        public X509Certificate2 IssueAdmittedClient(params string[] profiles)
        {
            var certificate = IssueClient("agent-fictitious", profiles);
            Admit(certificate, profiles);
            return certificate;
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
            // ephemeral, so this one is loaded with a container. The plugin itself never does that
            // any more: only the client side of these tests still goes through SChannel.
            X509Certificate2 certificate;
            using (var issued = new AuthorityIssuer().IssueClient(_authority, subject, profiles, lifetime))
            using (KeePassGate.Testing.PrivateKeyFiles.Lock())
            {
                certificate = CertificateBlob.Load(CertificateBlob.Save(issued), X509KeyStorageFlags.Exportable);
            }

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

            using (var authority = issuer.CreateAuthority())
            {
                return issuer.IssueClient(authority, subject, profiles, TimeSpan.FromDays(1));
            }
        }

        /// <summary>
        /// The host certificate as something an SslStream client can actually present, which means
        /// a key with a container. It must still be refused, on its extended key usage rather than
        /// on anything about its key.
        /// </summary>
        public X509Certificate2 HostCertificateAsClient()
        {
            X509Certificate2 certificate;
            using (KeePassGate.Testing.PrivateKeyFiles.Lock())
            {
                certificate = CertificateBlob.Load(_hostBlob, X509KeyStorageFlags.Exportable);
            }

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

                var undecided = target as UndecidedTarget;
                if (undecided != null)
                {
                    undecided.Retain();
                }
            }

            return _targets;
        }
    }
}
