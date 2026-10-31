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
    /// Disposable, and worth disposing. The certificates hold key handles, and the gate key is
    /// zeroed rather than left for a memory dump to find. That is hygiene rather than defense:
    /// local code execution as the user is conceded throughout, and this material is readable from
    /// the open database anyway.
    /// </remarks>
    public sealed class AuthorityMaterial : IDisposable
    {
        /// <summary>Thirty-two bytes, as `CA.md` §204 specifies.</summary>
        public const int SniKeyLength = 32;

        private readonly X509Certificate2 _authority;
        private readonly X509Certificate2 _host;
        private readonly byte[] _sniKey;
        private readonly byte[] _hostPkcs12;
        private bool _disposed;

        public AuthorityMaterial(X509Certificate2 authority, X509Certificate2 host, byte[] sniKey)
            : this(authority, host, sniKey, null)
        {
        }

        /// <summary>
        /// The same, keeping the host's stored bytes. The TLS server parses those itself rather
        /// than taking a Windows certificate, because a key Windows has loaded is a key on the
        /// disk.
        /// </summary>
        public AuthorityMaterial(X509Certificate2 authority, X509Certificate2 host, byte[] sniKey, byte[] hostPkcs12)
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

            _authority = authority;
            _host = host;
            _sniKey = (byte[])sniKey.Clone();
            _hostPkcs12 = hostPkcs12 == null ? null : (byte[])hostPkcs12.Clone();
        }

        public X509Certificate2 Authority
        {
            get { return _authority; }
        }

        public X509Certificate2 Host
        {
            get { return _host; }
        }

        /// <summary>A copy, so that a caller cannot zero what the plugin is still using.</summary>
        public byte[] SniKey
        {
            get { return (byte[])_sniKey.Clone(); }
        }

        /// <summary>
        /// The host certificate and key as they are stored, for the TLS server to parse. Null when
        /// the material was built rather than read.
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

            try
            {
                _authority.Reset();
            }
            catch (CryptographicException)
            {
                // Releasing a key handle that has already gone. Nothing is owed to anybody here.
            }

            try
            {
                _host.Reset();
            }
            catch (CryptographicException)
            {
                // The host's key handle, gone already in the same way: there is nothing left to release.
            }
        }
    }
}
