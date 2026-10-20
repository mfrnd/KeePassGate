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
    /// format .NET reads and writes without help, never as a security boundary.
    ///
    /// Measured, and the reason <see cref="LoadFlags"/> reads the way it does: importing a PKCS#12
    /// on .NET Framework writes a key file into the user's profile unless `EphemeralKeySet` is
    /// asked for, which would put the authority's private key on disk outside the database that is
    /// supposed to be the only place it lives. With the flag, nothing touches the disk.
    ///
    /// **It does not follow that SChannel will serve with one.** Measured: an `EphemeralKeySet`
    /// certificate signs, and SChannel refuses TLS with it in either direction, a server getting
    /// `SEC_E_NO_CREDENTIALS`. That is why the transport is Bouncy Castle rather than
    /// `SslStream`. `FINDINGS.md` records the measurement and the reversal.
    /// </remarks>
    public static class CertificateBlob
    {
        /// <summary>
        /// `EphemeralKeySet` keeps the private key out of the user's profile. `Exportable` is what
        /// lets the same material be written back out again.
        /// </summary>
        public const X509KeyStorageFlags LoadFlags =
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
                certificate = new X509Certificate2(bytes, (string)null, LoadFlags);
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
