using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The URIs a certificate's subject alternative name carries, for the tests that check what
    /// the authority wrote there.
    /// </summary>
    /// <remarks>
    /// **Here rather than in the plugin, because the plugin reads no subject alternative name.**
    /// It writes two kinds, the profile claims and the responder marker, and the marker is there
    /// for the client to read, `CA.md` §208. A reader in the plugin would be parsing bytes
    /// somebody else chose for no caller at all.
    ///
    /// Read through Bouncy Castle's `GeneralNames` and never through `X509Extension.Format`,
    /// whose output is localized and is not a data format, `CA.md` §211. Nothing here is
    /// defensive: every certificate it is handed was minted by the test asking, so bytes that
    /// will not decode are that test failing, and are thrown as such.
    ///
    /// In `tests/Shared`, compiled into both tiers, because both ask it.
    /// </remarks>
    internal static class CertificateUris
    {
        /// <summary>`id-ce-subjectAltName`, RFC 5280 section 4.2.1.6.</summary>
        private const string SubjectAlternativeName = "2.5.29.17";

        /// <summary>Every URI in the extension, in the order it is stored, or none without one.</summary>
        public static string[] Of(X509Certificate2 certificate)
        {
            var uris = new List<string>();

            var extension = certificate.Extensions[SubjectAlternativeName];
            if (extension == null)
            {
                return uris.ToArray();
            }

            foreach (var name in GeneralNames.GetInstance(Asn1Object.FromByteArray(extension.RawData)).GetNames())
            {
                if (name.TagNo == GeneralName.UniformResourceIdentifier)
                {
                    uris.Add(((IAsn1String)name.Name).GetString());
                }
            }

            return uris.ToArray();
        }
    }
}
