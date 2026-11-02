using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;

namespace KeePassGate.Testing
{
    /// <summary>
    /// A certificate the plugin issued, in the two forms most tests hand Windows: without its key,
    /// to read, and with a key file, for an `SslStream` client.
    /// </summary>
    /// <remarks>
    /// In `tests/Shared` because both tiers do it, for the reason <see cref="PrivateKeyFiles"/>
    /// gives. A test asking what Windows makes of the stored form itself loads the PKCS#12 under
    /// `EphemeralKeySet` where it asks, rather than through here.
    /// </remarks>
    internal static class Certificates
    {
        /// <summary>
        /// Windows's reading of the certificate, without its key, which is the only form in which
        /// the plugin gives Windows a certificate whose key it holds. For the caller to dispose.
        /// </summary>
        public static X509Certificate2 Read(this CertifiedKey certified)
        {
            return new X509Certificate2(certified.CertificateDer);
        }

        /// <summary>
        /// The certificate with its key in a container, which is what an `SslStream` client has to
        /// present and what the plugin never makes. For the caller to dispose.
        /// </summary>
        /// <remarks>
        /// The key is written under the profile for as long as the certificate is held, so the load
        /// takes <see cref="PrivateKeyFiles.Lock"/>, and a test watching for key files never sees
        /// one appear from here.
        /// </remarks>
        public static X509Certificate2 WithKeyFile(this CertifiedKey certified)
        {
            var pkcs12 = certified.ToPkcs12();
            using (PrivateKeyFiles.Lock())
            {
                return new X509Certificate2(pkcs12, (string)null, X509KeyStorageFlags.Exportable);
            }
        }
    }
}
