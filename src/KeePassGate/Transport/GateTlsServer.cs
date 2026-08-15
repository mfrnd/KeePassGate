using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Gate;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using BcCertificate = Org.BouncyCastle.Tls.Certificate;
using BcCertificateRequest = Org.BouncyCastle.Tls.CertificateRequest;
using BcHashAlgorithm = Org.BouncyCastle.Tls.HashAlgorithm;
using KeePassGate.Diagnostics;

namespace KeePassGate.Transport
{
    /// <summary>
    /// The server half of one TLS 1.3 handshake, in managed code.
    /// </summary>
    /// <remarks>
    /// **Why this exists rather than `SslStream`.** SChannel refuses to serve with a key that has
    /// not been written to the user's profile, so using it meant putting the host key on the disk
    /// for as long as a database was served. That contradicted `CA.md` §202, so the TLS is ours
    /// now and the key never leaves memory.
    ///
    /// Two other things come with that, both of which had already cost time. The offer is
    /// **TLS 1.3 and nothing else**, decided here rather than by the operating system's version.
    /// And whether a client certificate is asked for is decided here too, so serving a stranger on
    /// the pairing path no longer depends on a flag whose plain reading is the opposite of what it
    /// does: `ClientCertificateRequired = false` made SChannel send no request at all under
    /// TLS 1.3, and `= true` required nothing.
    ///
    /// What has not changed is who decides. This class asks for a certificate and reports what
    /// arrived. Whether it is worth anything is `ClientCredentials`, on the anchor pin and an
    /// explicit extended key usage check.
    /// </remarks>
    internal sealed class GateTlsServer : DefaultTlsServer
    {
        private readonly BcTlsCrypto _crypto;
        private readonly HostKeyMaterial _host;
        private BcCertificate _serverCertificate;

        public GateTlsServer(BcTlsCrypto crypto, HostKeyMaterial host)
            : base(crypto)
        {
            if (crypto == null)
            {
                throw new ArgumentNullException(nameof(crypto));
            }

            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            _crypto = crypto;
            _host = host;
        }

        /// <summary>
        /// What the client presented, as the rest of the plugin reads certificates. Null when it
        /// presented nothing, which is a stranger rather than an error.
        /// </summary>
        public X509Certificate2 PresentedCertificate { get; private set; }

        /// <summary>
        /// What one of this server's own callbacks threw, or null when none did.
        /// </summary>
        /// <remarks>
        /// **Kept here because Bouncy Castle does not keep it.** Whatever a server callback throws
        /// is caught inside the handshake and raised again as an alert, which is what Bouncy Castle
        /// 2.7.0's `SafeReadRecord` and its siblings do with every exception, read off its IL. So a
        /// defect in this class reached the listener as a failed handshake, logged at `Info` by
        /// the alert's message with the cause dropped, and counted nowhere. None of these
        /// callbacks reads what the peer sent without a guard of its own, so anything recorded
        /// here is a failure on this side and never the peer's.
        /// </remarks>
        public Exception Failure { get; private set; }

        protected override ProtocolVersion[] GetSupportedVersions()
        {
            return ProtocolVersion.TLSv13.Only();
        }

        public override TlsCredentials GetCredentials()
        {
            try
            {
                if (_serverCertificate == null)
                {
                    var entry = new CertificateEntry(new BcTlsCertificate(_crypto, _host.CertificateDer), null);
                    _serverCertificate = new BcCertificate(TlsUtilities.EmptyBytes, new[] { entry });
                }

                return new BcDefaultTlsCredentialedSigner(
                    new TlsCryptoParameters(m_context),
                    _crypto,
                    _host.PrivateKey,
                    _serverCertificate,
                    SignatureAndHashAlgorithm.GetInstance(BcHashAlgorithm.sha256, SignatureAlgorithm.ecdsa));
            }
            catch (Exception error)
            {
                // Noted and thrown on, so nothing is caught here: see `Failure`.
                Note(error);
                throw;
            }
        }

        /// <summary>
        /// Always asks. A client with nothing to show completes the handshake anyway and is served
        /// as a stranger, which is what the pairing path needs and what the route table refuses for
        /// everything else.
        /// </summary>
        public override BcCertificateRequest GetCertificateRequest()
        {
            try
            {
                var signatureAlgorithms = new List<SignatureAndHashAlgorithm>
                {
                    SignatureAndHashAlgorithm.GetInstance(BcHashAlgorithm.sha256, SignatureAlgorithm.ecdsa)
                };

                // No certificate authorities are named. Naming ours would tell an unauthenticated
                // client which database it had reached, and the gate exists to avoid exactly that.
                return new BcCertificateRequest(TlsUtilities.EmptyBytes, signatureAlgorithms, null, null);
            }
            catch (Exception error)
            {
                // Noted and thrown on, as above.
                Note(error);
                throw;
            }
        }

        public override void NotifyClientCertificate(BcCertificate clientCertificate)
        {
            try
            {
                if (clientCertificate == null || clientCertificate.IsEmpty)
                {
                    return;
                }

                try
                {
                    PresentedCertificate = new X509Certificate2(clientCertificate.GetCertificateAt(0).GetEncoded());
                }
                catch (Exception error) when (Expected.OfCryptography(error))
                {
                    // Unreadable is the same as absent: a stranger, refused by the route table.
                    PresentedCertificate = null;
                }
            }
            catch (Exception error)
            {
                // Noted and thrown on, as above. What a peer can make fail here is the reading
                // just above, and that is answered rather than thrown.
                Note(error);
                throw;
            }
        }

        /// <summary>Keeps the first failure, which is the one the handshake died of.</summary>
        private void Note(Exception error)
        {
            if (Failure == null)
            {
                Failure = error;
            }
        }
    }
}
