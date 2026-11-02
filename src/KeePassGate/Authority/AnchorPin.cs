using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
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
    /// without stopping the request. `spikes/tls-and-certificates` holds the first, `ChainProbe`.
    /// The second was measured by a test of `AnchorPinTests` while a chain engine answered,
    /// `A_foreign_leaf_that_names_a_download_is_refused_without_waiting_for_it`, which went with
    /// the engine and is in the history under that name.
    ///
    /// The hierarchy is one authority and the leaves it signs, `CA.md` §207, so the whole question
    /// is small enough to ask directly: the anchor is an authority that signed itself, the leaf
    /// is not an authority, it names the anchor as its issuer in the very bytes the anchor is
    /// named in, and the anchor's key signed it. Bouncy Castle checks the two signatures, which
    /// is all it is asked. Nothing here reaches a network or a machine store, so nothing a
    /// certificate names can make it, and nothing about the machine can change the answer.
    ///
    /// **Only the one shape this plugin writes is asked about at all**, `CA.md` §203: DER as
    /// Bouncy Castle writes it back, signed with ecdsa-with-SHA256 and naming that in both places a
    /// certificate names it, under an authority whose key is on P-256. Everything else is refused
    /// before anything about it is believed. Bouncy Castle builds a verifier from whatever
    /// algorithm the certificate names, so a certificate free to name one chose its own check:
    /// SHA-224, SHA3 and plain ECDSA over SHA-1 verified against the anchor's key, and Ed25519,
    /// Ed448, ML-DSA and SLH-DSA threw `InvalidCastException` out of the verifier's own cast. And
    /// it verifies over the structure as it writes it rather than the bytes it was given, so a
    /// certificate re-encoded with a length in long form was believed signed and named byte for
    /// byte when neither was true of the bytes received. Written back the same, the bytes
    /// compared and the bytes verified are the bytes that arrived.
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
        ///
        /// **A leaf that is itself an authority is refused**, and that is what refuses the anchor
        /// presented as its own leaf, in every encoding it has. Comparing the two certificates'
        /// bytes refused only the one: an ECDSA signature `(r, s)` verifies as `(r, n - s)` as
        /// well, so the anchor with its signature turned that way is other bytes its own key
        /// signed, naming itself, and it was answered as a leaf of its own.
        ///
        /// Whatever will not parse or verify is an answer, and the answer is no: the leaf arrives
        /// from whoever opened the connection, and a certificate Bouncy Castle cannot read is not
        /// one this authority issued. That is a promise kept by the checks first: the filter below
        /// names only what is still left to throw once they have refused everything of another
        /// shape.
        /// </remarks>
        public static bool Chains(X509Certificate2 leaf, X509Certificate2 anchor)
        {
            if (leaf == null || anchor == null)
            {
                return false;
            }

            try
            {
                var issued = OfOurShape(leaf.RawData);
                var authority = OfOurShape(anchor.RawData);

                return issued != null
                       && authority != null
                       && IsAuthority(authority)
                       && !IsCertificateAuthority(issued)
                       && UnderstandsEverythingCritical(issued)
                       && FixedTime.Equals(issued.IssuerDN.GetEncoded(), authority.SubjectDN.GetEncoded())
                       && issued.IsSignatureValid(authority.GetPublicKey());
            }
            catch (Exception error) when (error is CryptographicException
                                          || error is GeneralSecurityException
                                          || error is IOException
                                          || error is ArgumentException
                                          || error is FormatException)
            {
                // What the checks cannot see coming, each fed to the pin by a test of
                // `AnchorPinTests` that would throw without it. `CryptographicException` is Windows
                // asked for the bytes of a certificate it no longer holds. `GeneralSecurityException`
                // is Bouncy Castle refusing a basic constraints or key usage value it cannot parse,
                // which it parses as it reads the certificate and Windows does not. `IOException` is
                // a signature whose bit string does not end on a whole byte. `ArgumentException` and
                // `FormatException` are an anchor's point that is not on its curve or not encoded as
                // a point at all, which nothing reads until the key is asked for.
                return false;
            }
        }

        /// <summary>
        /// The certificate as Bouncy Castle reads it, or null when it is not of the shape this
        /// plugin writes: the very DER Bouncy Castle writes back, and ecdsa-with-SHA256 with its
        /// parameters absent, `CA.md` §203, in the signature and in the signed part alike.
        /// </summary>
        /// <remarks>
        /// Both places, because Bouncy Castle compares them itself only after an absent parameter
        /// and an explicit NULL have been made equal, and a certificate signed over one while it
        /// names the other is not one this plugin writes. Asked before any signature is checked,
        /// since the algorithm named is the one a verifier would be built for.
        /// </remarks>
        private static BcCertificate OfOurShape(byte[] der)
        {
            var certificate = new BcCertificate(der);

            // `FixedTime.Equals` rather than a comparison that stops at the first difference,
            // which is what `CA.md` §214 writes down. It defends nothing on its own: an anchor is
            // a public certificate and every client that can reach this code was handed a copy of
            // it at pairing. It is here because every comparison of certificate bytes in this
            // plugin goes through that one door, and one that did not would be a question a reader
            // has to answer again.
            if (!FixedTime.Equals(certificate.GetEncoded(), der))
            {
                return null;
            }

            return IsEcdsaWithSha256(certificate.SignatureAlgorithm)
                   && IsEcdsaWithSha256(certificate.TbsCertificate.Signature)
                ? certificate
                : null;
        }

        private static bool IsEcdsaWithSha256(AlgorithmIdentifier algorithm)
        {
            return X9ObjectIdentifiers.ECDsaWithSha256.Equals(algorithm.Algorithm) && algorithm.Parameters == null;
        }

        /// <summary>
        /// An authority as this plugin makes one: a key on P-256, `CA:TRUE`, allowed to sign
        /// certificates, nothing critical it does not understand, named as its own issuer in its
        /// own bytes, and signed by its own key.
        /// </summary>
        /// <remarks>
        /// **Stricter than the chain engine was.** It refused an anchor marked `CA:FALSE`, one
        /// without `keyCertSign`, one carrying a critical extension it did not know and one whose
        /// own signature did not check. It tolerated an anchor with no basic constraints at all, as
        /// an untrusted root, and one naming another issuer and a subordinate whose issuer it could
        /// not find, as a partial chain, and this refuses all three. A certificate that is not an
        /// authority vouches for nothing, and one signed by somebody else is the subordinate
        /// `pathLen 0` forbids.
        ///
        /// The key is asked about before it is read, because reading it is where Bouncy Castle
        /// decides what kind of key it is: an anchor naming no curve, which X9.62 calls implicitly
        /// CA, threw `NotImplementedException` out of that read, and a key of another kind threw
        /// out of the verifier's cast.
        /// </remarks>
        private static bool IsAuthority(BcCertificate authority)
        {
            var key = authority.SubjectPublicKeyInfo.Algorithm;
            if (!X9ObjectIdentifiers.IdECPublicKey.Equals(key.Algorithm)
                || !X9ObjectIdentifiers.Prime256v1.Equals(key.Parameters))
            {
                return false;
            }

            if (!IsCertificateAuthority(authority))
            {
                return false;
            }

            // `GetKeyUsage` answers null when there is no key usage, which restricts nothing, and
            // otherwise one flag per bit and never fewer than nine, `keyCertSign` the sixth: RFC 5280
            // section 4.2.1.3.
            var usage = authority.GetKeyUsage();
            if (usage != null && !usage[KeyCertSign])
            {
                return false;
            }

            return UnderstandsEverythingCritical(authority)
                   && FixedTime.Equals(authority.IssuerDN.GetEncoded(), authority.SubjectDN.GetEncoded())
                   && authority.IsSignatureValid(authority.GetPublicKey());
        }

        /// <summary>The position of `keyCertSign` among the key usage bits.</summary>
        private const int KeyCertSign = 5;

        /// <summary>Whether the certificate's basic constraints mark it `CA:TRUE`.</summary>
        /// <remarks>
        /// Read as Bouncy Castle parses the extension, and asked `IsCA` and nothing else. Never
        /// `GetBasicConstraints`, which answers -1 for an end entity, the path length for an
        /// authority that sets one and `int.MaxValue` for one that does not, and throws
        /// `ArithmeticException` for a path length past `int`, which a stored authority is free to
        /// carry. A path length constrains what lies beneath a subordinate, and the pin accepts no
        /// leaf that is one, so there is nothing here for it to constrain.
        /// </remarks>
        private static bool IsCertificateAuthority(BcCertificate certificate)
        {
            var constraints = BasicConstraints.FromExtensions(certificate.TbsCertificate.Extensions);
            return constraints != null && constraints.IsCA();
        }

        /// <summary>
        /// Whether every extension the certificate marks critical is one this plugin understands.
        /// A certificate with no extensions at all marks nothing critical, and is answered by the
        /// checks either side of this one.
        /// </summary>
        private static bool UnderstandsEverythingCritical(BcCertificate certificate)
        {
            var extensions = certificate.TbsCertificate.Extensions;
            if (extensions == null)
            {
                return true;
            }

            foreach (var oid in extensions.GetCriticalExtensionOids())
            {
                if (!Understood.Contains(oid))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
