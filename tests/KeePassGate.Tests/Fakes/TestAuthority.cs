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
            _admissions = new AdmissionStore(new InMemoryKeyValueStore(), NullGateLog.Instance);

            DatabaseName = "Invented Vault";
        }

        public static TestAuthority New()
        {
            return New(null);
        }

        /// <summary>
        /// The same database, minted by a clock the caller chooses, which is the only way to get
        /// the one state a test cannot wait for: an authority whose ten years have run out.
        /// </summary>
        /// <remarks>
        /// Such a database is served like any other, because `AuthorityStore.Read` ignores expiry
        /// on purpose, and only issuing finds out. `CA.md` §219.
        /// </remarks>
        public static TestAuthority New(Func<DateTimeOffset> clock)
        {
            var issuer = new AuthorityIssuer(clock);
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

        /// <summary>The records this database holds, as the gate reads them.</summary>
        public AdmissionRecord Admission(string admissionKey)
        {
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

        /// <summary>Set to fail the write after it was allowed to start.</summary>
        public AdmissionWrite AdmitAnswers { get; set; }

        /// <summary>Set to have the write throw rather than answer, which must not reach the client.</summary>
        public Exception AdmitThrows { get; set; }

        /// <summary>What the last pairing wrote, so a test can look at it.</summary>
        public AdmissionRecord Admitted { get; private set; }

        public AdmissionWrite Admit(AdmissionRecord record)
        {
            if (AdmitThrows != null)
            {
                throw AdmitThrows;
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
            // The test client is an SslStream, which will not present a certificate whose key is
            // ephemeral, so this one is loaded with a container. The plugin itself never does that
            // any more: only the client side of these tests still goes through SChannel.
            X509Certificate2 certificate;
            using (var issued = new AuthorityIssuer().IssueClient(_authority, subject, profiles))
            using (KeePassGate.Testing.PrivateKeyFiles.Lock())
            {
                certificate = CertificateBlob.Load(CertificateBlob.Save(issued), X509KeyStorageFlags.Exportable);
            }

            _issued.Add(certificate);
            return certificate;
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
            }

            return _targets;
        }
    }
}
