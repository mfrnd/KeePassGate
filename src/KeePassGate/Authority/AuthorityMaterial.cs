using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace KeePassGate.Authority
{
    /// <summary>
    /// An authority, in memory: the signing certificate, the host certificate it issued, and the
    /// key the gate derives connection names from.
    /// </summary>
    /// <remarks>
    /// Each certificate is held twice, and that is the split between reading and signing. Bouncy
    /// Castle holds each with its key, which is what the issuer signs with and the TLS server
    /// presents, and Windows holds each without one, for everything that only reads a
    /// certificate: its digest, its dates, and what the tab shows.
    ///
    /// Disposable, and worth disposing. The Windows certificates hold handles, and the gate key is
    /// zeroed rather than left for a memory dump to find. That is hygiene rather than defense:
    /// local code execution as the user is conceded throughout, and this material is readable from
    /// the open database anyway.
    /// </remarks>
    public sealed class AuthorityMaterial : IDisposable
    {
        /// <summary>Thirty-two bytes, as `CA.md` §204 specifies.</summary>
        public const int SniKeyLength = 32;

        private readonly CertifiedKey _authorityKey;
        private readonly CertifiedKey _hostKey;
        private readonly X509Certificate2 _authority;
        private readonly X509Certificate2 _host;
        private readonly byte[] _sniKey;
        private readonly byte[] _hostPkcs12;
        private bool _disposed;

        public AuthorityMaterial(CertifiedKey authority, CertifiedKey host, byte[] sniKey)
            : this(authority, host, sniKey, null)
        {
        }

        /// <summary>
        /// The same, keeping the host's stored bytes, which a database's tags are derived from.
        /// </summary>
        public AuthorityMaterial(CertifiedKey authority, CertifiedKey host, byte[] sniKey, byte[] hostPkcs12)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (sniKey == null)
            {
                throw new ArgumentNullException(nameof(sniKey));
            }

            if (sniKey.Length != SniKeyLength)
            {
                throw new ArgumentException(
                    "The gate key is " + SniKeyLength + " bytes, not " + sniKey.Length + ".", nameof(sniKey));
            }

            _authority = new X509Certificate2(authority.CertificateDer);
            try
            {
                _host = new X509Certificate2(host.CertificateDer);
            }
            catch (CryptographicException)
            {
                // Defensive, and reached by nothing measured: these are bytes Bouncy Castle parsed
                // and wrote again, and Windows has read every such certificate tried. It is here so
                // that a refusal would not leave the authority's handle open behind the throw.
                _authority.Reset();
                throw;
            }

            _authorityKey = authority;
            _hostKey = host;
            _sniKey = (byte[])sniKey.Clone();
            _hostPkcs12 = hostPkcs12 == null ? null : (byte[])hostPkcs12.Clone();
        }

        /// <summary>The authority, to read. It holds no key: <see cref="AuthorityKey"/> signs.</summary>
        public X509Certificate2 Authority
        {
            get { return _authority; }
        }

        /// <summary>The host certificate, to read. It holds no key: <see cref="HostKey"/> serves.</summary>
        public X509Certificate2 Host
        {
            get { return _host; }
        }

        /// <summary>The authority and its key, which everything this database vouches for is issued with.</summary>
        public CertifiedKey AuthorityKey
        {
            get { return _authorityKey; }
        }

        /// <summary>The host certificate and its key, which the TLS server presents.</summary>
        public CertifiedKey HostKey
        {
            get { return _hostKey; }
        }

        /// <summary>A copy, so that a caller cannot zero what the plugin is still using.</summary>
        public byte[] SniKey
        {
            get { return (byte[])_sniKey.Clone(); }
        }

        /// <summary>
        /// The host certificate and key as they are stored, which a database's tags are derived
        /// from. Null when the material was built rather than read.
        /// </summary>
        public byte[] HostPkcs12
        {
            get { return _hostPkcs12 == null ? null : (byte[])_hostPkcs12.Clone(); }
        }

        /// <summary>SHA-256 over the authority's DER, which is how an authority is named.</summary>
        public string AuthorityDigest
        {
            get { return CertificateDigest.Of(_authority); }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Array.Clear(_sniKey, 0, _sniKey.Length);

            if (_hostPkcs12 != null)
            {
                Array.Clear(_hostPkcs12, 0, _hostPkcs12.Length);
            }

            _authority.Reset();
            _host.Reset();
        }
    }
}
