using System;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// How a certificate and its private key become a string, and come back.
    /// </summary>
    /// <remarks>
    /// PKCS#12 with no password, base64 encoded. No password because there is nothing for one to
    /// protect: the database's own encryption is what guards this, and a passphrase here would be
    /// cryptography written by us over cryptography that already works. See `CA.md` §202.
    ///
    /// PKCS#12 is RFC 7292. https://www.rfc-editor.org/rfc/rfc7292 It is used here only as the
    /// format .NET and Bouncy Castle both read, never as a security boundary.
    ///
    /// **Everything is loaded with `EphemeralKeySet`, so no key this plugin holds ever reaches the
    /// disk.** That is only possible because the TLS server is managed code: a certificate loaded
    /// this way cannot be used with SChannel at all, in either direction, which is measured in
    /// `EphemeralKeyProbe.cs` on `spikes/tls-and-certificates` and is why `Org.BouncyCastle.Tls`
    /// terminates the transport.
    /// Bouncy Castle parses the stored bytes itself, so a host key is an object on the heap and
    /// nothing more.
    /// </remarks>
    public static class CertificateBlob
    {
        /// <summary>Nothing reaches the disk, which is what every key this plugin holds needs.</summary>
        public const X509KeyStorageFlags SigningFlags =
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;

        public static string Save(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (!certificate.HasPrivateKey)
            {
                throw new ArgumentException("A certificate is only ever stored with its key.", nameof(certificate));
            }

            return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12));
        }

        /// <summary>
        /// Reads one back. Returns null for anything that is not a certificate with a usable
        /// private key, because a half readable authority has to be reported as damage rather than
        /// used.
        /// </summary>
        public static X509Certificate2 Load(string blob)
        {
            return Load(blob, SigningFlags);
        }

        /// <summary>The stored bytes, for the TLS server to parse itself. Null when unreadable.</summary>
        public static byte[] Bytes(string blob)
        {
            if (string.IsNullOrEmpty(blob))
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(blob);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// The same, with the storage flags chosen by the caller.
        /// </summary>
        /// <remarks>
        /// **Nothing in the plugin calls this, and nothing should.** It is here for the test
        /// client, which drives SChannel, and SChannel will not do TLS with an `EphemeralKeySet`
        /// key in either direction. A client that has to be the platform's own therefore loads
        /// without the flag and pays for it with a key file under the profile for as long as the
        /// certificate is held. Production loads through <see cref="Load(string)"/>, where the
        /// guarantee that no key reaches the disk is not a parameter anybody can pass.
        /// </remarks>
        public static X509Certificate2 Load(string blob, X509KeyStorageFlags flags)
        {
            if (string.IsNullOrEmpty(blob))
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(blob);
            }
            catch (FormatException)
            {
                return null;
            }

            X509Certificate2 certificate = null;
            try
            {
                certificate = new X509Certificate2(bytes, (string)null, flags);
                if (!certificate.HasPrivateKey)
                {
                    certificate.Reset();
                    return null;
                }

                return certificate;
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                if (certificate != null)
                {
                    certificate.Reset();
                }

                return null;
            }
        }
    }
}
