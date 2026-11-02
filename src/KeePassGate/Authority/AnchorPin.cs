using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Does this certificate chain to exactly this authority, by its bytes.
    /// </summary>
    /// <remarks>
    /// **No chain engine, and that is two measurements.** `X509Chain` accepted a certificate from a
    /// completely foreign authority: `ExtraStore` supplies candidates without constraining the
    /// anchor, and `AllowUnknownCertificateAuthority` tolerates an untrusted root without saying
    /// which one, so a build succeeded against the machine's own root stores, and the anchor had
    /// to be pinned by comparing the last element's DER. And before `Build` returned it downloaded
    /// whatever a foreign leaf's authority information access extension named, RFC 5280 section
    /// 4.2.2.1, so a stranger who reached this code made the KeePass process open a connection to
    /// an address of their choosing, held for 15.0 s until a millisecond's timeout bounded the wait
    /// without stopping the request. `spikes/tls-and-certificates` holds both measurements.
    ///
    /// The hierarchy is one authority and the leaves it signs, `CA.md` §207, so the whole question
    /// is small enough to ask directly: the anchor is an authority that signed itself, the leaf
    /// names it as its issuer in the very bytes it is named in, and the anchor's key signed the
    /// leaf. Bouncy Castle checks the two signatures, which is all it is asked. Nothing here
    /// reaches a network or a machine store, so nothing a certificate says can make it, and
    /// nothing about the machine can change the answer.
    ///
    /// Comparing names alone would not do: every authority this plugin issues carries the same
    /// subject, so a foreign one matches by name and differs by every byte that matters, and the
    /// signature is what refuses it.
    ///
    /// **Dates are not asked about here.** The connection path checks them itself, so that the
    /// clock skew allowed is this plugin's, and every other caller asks which authority a
    /// certificate belongs to, which an expired one still does, and reports its expiry on its own
    /// terms. `CertificateValidity` is where dates are judged.
    /// </remarks>
    public static class AnchorPin
    {
        /// <summary>
        /// The extensions a certificate here may mark critical and still be understood, which are
        /// the ones this plugin writes and the identifiers beside them. RFC 5280 section 4.2 refuses
        /// a certificate with a critical extension its reader does not recognize, and the chain
        /// engine did, so this does.
        /// </summary>
        private static readonly HashSet<DerObjectIdentifier> Understood = new HashSet<DerObjectIdentifier>
        {
            X509Extensions.BasicConstraints,
            X509Extensions.KeyUsage,
            X509Extensions.ExtendedKeyUsage,
            X509Extensions.SubjectAlternativeName,
            X509Extensions.SubjectKeyIdentifier,
            X509Extensions.AuthorityKeyIdentifier,
        };

        /// <summary>
        /// True when the leaf was issued by the anchor and the anchor by nobody but itself.
        /// </summary>
        /// <remarks>
        /// A leaf and the authority that signed it, and nothing between them: `CA.md` §207 requires
        /// `pathLen 0` on the authority so that it can never mint a subordinate, so a certificate
        /// whose issuer is anything but the anchor is either not ours or not one this would accept.
        /// The anchor presented as its own leaf is refused too, because it is not a leaf.
        ///
        /// Whatever will not parse or verify is an answer, and the answer is no: the leaf arrives
        /// from whoever opened the connection, and a certificate Bouncy Castle cannot read is not
        /// one this authority issued.
        /// </remarks>
        public static bool Chains(X509Certificate2 leaf, X509Certificate2 anchor)
        {
            if (leaf == null || anchor == null)
            {
                return false;
            }

            var leafDer = leaf.RawData;
            var anchorDer = anchor.RawData;

            // `FixedTime.Equals` rather than a comparison that stops at the first difference, which
            // is what `CA.md` §214 writes down. It defends nothing on its own: an anchor is a public
            // certificate and every client that can reach this code was handed a copy of it at
            // pairing. It is here because every comparison of certificate bytes in this plugin goes
            // through that one door, and one that did not would be a question a reader has to
            // answer again.
            if (FixedTime.Equals(leafDer, anchorDer))
            {
                return false;
            }

            try
            {
                var issued = new BcCertificate(leafDer);
                var authority = new BcCertificate(anchorDer);

                return IsAuthority(authority)
                       && UnderstandsEverythingCritical(authority)
                       && UnderstandsEverythingCritical(issued)
                       && FixedTime.Equals(issued.IssuerDN.GetEncoded(), authority.SubjectDN.GetEncoded())
                       && issued.IsSignatureValid(authority.GetPublicKey());
            }
            catch (Exception error) when (error is ArgumentException
                                          || error is IOException
                                          || error is InvalidOperationException
                                          || error is CryptoException
                                          || error is GeneralSecurityException
                                          || error is SecurityUtilityException)
            {
                // Bouncy Castle's own types, as `CertifiedKey.From` names them, and
                // `GeneralSecurityException` beside them, which is what a signature or a key of a
                // kind that cannot check it arrives as. `AnchorPinTests` feeds it each.
                return false;
            }
        }

        /// <summary>
        /// An authority as this plugin makes one: `CA:TRUE`, allowed to sign certificates, named
        /// as its own issuer in its own bytes, and signed by its own key.
        /// </summary>
        /// <remarks>
        /// What the chain engine asked of the root it ended at. A certificate that is not an
        /// authority vouches for nothing, and one signed by somebody else is the subordinate
        /// `pathLen 0` forbids.
        /// </remarks>
        private static bool IsAuthority(BcCertificate authority)
        {
            // `GetBasicConstraints` answers -1 for an end entity, and the path length otherwise.
            if (authority.GetBasicConstraints() < 0)
            {
                return false;
            }

            // `GetKeyUsage` answers null when there is no key usage, which restricts nothing, and
            // otherwise one flag per bit, `keyCertSign` the sixth: RFC 5280 section 4.2.1.3.
            var usage = authority.GetKeyUsage();
            if (usage != null && (usage.Length <= KeyCertSign || !usage[KeyCertSign]))
            {
                return false;
            }

            return FixedTime.Equals(authority.IssuerDN.GetEncoded(), authority.SubjectDN.GetEncoded())
                   && authority.IsSignatureValid(authority.GetPublicKey());
        }

        /// <summary>The position of `keyCertSign` among the key usage bits.</summary>
        private const int KeyCertSign = 5;

        private static bool UnderstandsEverythingCritical(BcCertificate certificate)
        {
            var critical = certificate.GetCriticalExtensionOids();
            if (critical == null)
            {
                return true;
            }

            foreach (var oid in critical)
            {
                if (!Understood.Contains(new DerObjectIdentifier(oid)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
