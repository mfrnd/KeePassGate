using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
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

        private TestAuthority(
            X509Certificate2 authority, X509Certificate2 host, string hostBlob, HostKeyMaterial hostKey, byte[] gateKey)
        {
            _authority = authority;
            _host = host;
            _hostBlob = hostBlob;
            _hostKey = hostKey;
            _gateKey = gateKey;
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
            var certificate = CertificateBlob.Load(_hostBlob, X509KeyStorageFlags.Exportable);
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

            // One reference per caller, as the real source takes. Whoever asked gives them
            // back, and the fixture keeps holding its own until teardown.
            foreach (var target in _targets)
            {
                var counted = target as TestAuthority;
                if (counted != null)
                {
                    counted.Retain();
                }
            }

            return _targets;
        }
    }
}
