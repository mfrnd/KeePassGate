using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KeePassGate.Authority
{
    /// <summary>
    /// SHA-256 over a certificate's DER, lowercase hex. This is the identifier the design uses
    /// everywhere a certificate has to be named.
    /// </summary>
    /// <remarks>
    /// Not <see cref="X509Certificate2.Thumbprint"/>, which is SHA-1: measured at 40 hex
    /// characters. A design that says SHA-256 throughout should not key anything on it.
    /// </remarks>
    public static class CertificateDigest
    {
        public static string Of(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return Of(certificate.RawData);
        }

        public static string Of(byte[] der)
        {
            if (der == null)
            {
                throw new ArgumentNullException(nameof(der));
            }

            using (var sha256 = SHA256.Create())
            {
                return KeePassGate.Hex.Of(sha256.ComputeHash(der));
            }
        }

        /// <summary>The first bytes of a digest, for a display that has to fit on one line.</summary>
        public static string Shorten(string digest)
        {
            if (string.IsNullOrEmpty(digest))
            {
                return string.Empty;
            }

            return digest.Length <= 16 ? digest : digest.Substring(0, 16);
        }

        /// <summary>
        /// Whether this is a digest as <see cref="Of(byte[])"/> writes one: sixty-four characters
        /// of lowercase hex, and nothing else.
        /// </summary>
        public static bool IsDigest(string text)
        {
            if (text == null || text.Length != 64)
            {
                return false;
            }

            foreach (var c in text)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
