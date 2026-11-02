using System;

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
    /// format Bouncy Castle, Windows and the client's `cryptography` all read, never as a security
    /// boundary.
    ///
    /// **Bouncy Castle writes it and reads it, so no key this plugin holds ever reaches Windows**,
    /// let alone the disk. Windows loading one is what used to need `EphemeralKeySet` to keep a key
    /// out of the profile, and a key loaded that way cannot be used with SChannel at all, in either
    /// direction, which is measured in `EphemeralKeyProbe.cs` on `spikes/tls-and-certificates` and
    /// is why `Org.BouncyCastle.Tls` terminates the transport.
    /// </remarks>
    public static class CertificateBlob
    {
        public static string Save(CertifiedKey certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            var pkcs12 = certificate.ToPkcs12();
            try
            {
                return Convert.ToBase64String(pkcs12);
            }
            finally
            {
                // A private key in the clear, overwritten once the base64 is made, `CA.md` §208.
                Array.Clear(pkcs12, 0, pkcs12.Length);
            }
        }

        /// <summary>
        /// Reads one back. Returns null for anything that is not a certificate with its own private
        /// key, because a half readable authority has to be reported as damage rather than used.
        /// </summary>
        public static CertifiedKey Load(string blob)
        {
            var pkcs12 = Bytes(blob);
            try
            {
                return CertifiedKey.From(pkcs12);
            }
            finally
            {
                // Overwritten once Bouncy Castle has read them, since what it read is its own,
                // `CA.md` §208.
                if (pkcs12 != null)
                {
                    Array.Clear(pkcs12, 0, pkcs12.Length);
                }
            }
        }

        /// <summary>
        /// The stored bytes, as they are stored. Null when unreadable. They hold a private key, so
        /// the caller overwrites them once it is done with them.
        /// </summary>
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
    }
}
