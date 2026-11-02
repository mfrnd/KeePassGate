using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Security;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A certificate this plugin wrote, taken apart and put back in a shape it never would: signed
    /// again under another algorithm, named with one it cannot check, written in other bytes, or
    /// carrying a value Bouncy Castle cannot parse.
    /// </summary>
    /// <remarks>
    /// Worked on the ASN.1 rather than through a generator, because a generator writes only what it
    /// means and every shape here is one nobody means. What each method keeps is everything it does
    /// not name, so a test that changes one thing has changed one thing. `Minted` is for the
    /// certificates a stranger is free to issue properly; this is for the ones that are not proper.
    /// </remarks>
    internal static class Reshaped
    {
        /// <summary>The algorithm this plugin signs with, by Bouncy Castle's name for it.</summary>
        // cspell:ignore WITHECDSA
        public const string Ours = "SHA256WITHECDSA";

        /// <summary>
        /// The certificate with its signed part changed as given, then signed again by
        /// <paramref name="signer"/> under <paramref name="algorithm"/>, which it names in both
        /// places a certificate names one.
        /// </summary>
        public static byte[] SignedAgain(
            byte[] der, CertifiedKey signer, Func<Asn1Sequence, Asn1Sequence> change = null, string algorithm = Ours)
        {
            var factory = new Asn1SignatureFactory(algorithm, signer.PrivateKey, new SecureRandom());
            var named = (AlgorithmIdentifier)factory.AlgorithmDetails;
            var tbs = WithSignatureField((change ?? (same => same))(SignedPart(der)), named);
            return Assembled(tbs, named, Signature(factory, tbs));
        }

        /// <summary>
        /// The certificate naming <paramref name="outer"/> as its signature algorithm and
        /// <paramref name="inner"/> in its signed part, and signed by <paramref name="signer"/> as
        /// this plugin signs, so that a verifier built for what is named would check it.
        /// </summary>
        public static byte[] Naming(byte[] der, AlgorithmIdentifier outer, AlgorithmIdentifier inner, CertifiedKey signer)
        {
            var factory = new Asn1SignatureFactory(Ours, signer.PrivateKey, new SecureRandom());
            var tbs = WithSignatureField(SignedPart(der), inner);
            return Assembled(tbs, outer, Signature(factory, tbs));
        }

        /// <summary>
        /// The same bytes but for the certificate's own length, written in one more octet than DER
        /// allows, which every reader here takes as the same certificate.
        /// </summary>
        public static byte[] LongForm(byte[] der)
        {
            if (der.Length < 4 || der[0] != 0x30 || der[1] != 0x82)
            {
                throw new ArgumentException("expected a certificate whose length takes two octets", nameof(der));
            }

            return new byte[] { 0x30, 0x83, 0x00, der[2], der[3] }.Concat(der.Skip(4)).ToArray();
        }

        /// <summary>
        /// The certificate with its ECDSA signature `(r, s)` turned to `(r, n - s)`, which verifies
        /// wherever the first does and is other bytes. P-256's order, which is every signer's here.
        /// </summary>
        public static byte[] Turned(byte[] der)
        {
            var certificate = Asn1Sequence.GetInstance(der);
            var signature = Asn1Sequence.GetInstance(DerBitString.GetInstance(certificate[2]).GetOctets());
            var order = ECNamedCurveTable.GetByOid(X9ObjectIdentifiers.Prime256v1).N;
            var turned = new DerSequence(
                signature[0], new DerInteger(order.Subtract(DerInteger.GetInstance(signature[1]).Value)));

            return new DerSequence(certificate[0], certificate[1], new DerBitString(turned.GetDerEncoded())).GetDerEncoded();
        }

        /// <summary>
        /// The certificate with its signature's bit string declaring one unused bit, the last bit
        /// cleared so that it is still DER. Nothing verifies it, and nothing has to: what is asked
        /// is what reading it does.
        /// </summary>
        public static byte[] SignaturePadded(byte[] der)
        {
            var certificate = Asn1Sequence.GetInstance(der);
            var bits = DerBitString.GetInstance(certificate[2]).GetBytes();
            bits[bits.Length - 1] &= 0xFE;
            return new DerSequence(certificate[0], certificate[1], new DerBitString(bits, 1)).GetDerEncoded();
        }

        /// <summary>
        /// The certificate with the octets of its subject's public key changed as given, and
        /// signed again by <paramref name="signer"/>. The key is read before any signature made by
        /// it could be checked, so what the signature is matters to nothing here.
        /// </summary>
        public static byte[] WithPoint(byte[] der, CertifiedKey signer, Action<byte[]> change)
        {
            return SignedAgain(der, signer, tbs =>
            {
                var at = KeyIndex(tbs);
                var key = SubjectPublicKeyInfo.GetInstance(tbs[at]);
                var point = key.PublicKey.GetOctets();
                change(point);
                return Replaced(tbs, at, new SubjectPublicKeyInfo(key.Algorithm, point));
            });
        }

        /// <summary>The signed part naming <paramref name="issuer"/> as its issuer.</summary>
        public static Asn1Sequence WithIssuer(Asn1Sequence tbs, X509Name issuer)
        {
            // After the serial and the signature algorithm.
            return Replaced(tbs, IsVersioned(tbs) ? 3 : 2, issuer);
        }

        /// <summary>
        /// The signed part naming <paramref name="algorithm"/> for the subject's key, whose octets
        /// are kept as they were.
        /// </summary>
        public static Asn1Sequence WithKeyAlgorithm(Asn1Sequence tbs, AlgorithmIdentifier algorithm)
        {
            var at = KeyIndex(tbs);
            var key = SubjectPublicKeyInfo.GetInstance(tbs[at]);
            return Replaced(tbs, at, new SubjectPublicKeyInfo(algorithm, key.PublicKey));
        }

        /// <summary>
        /// The signed part with the value of one extension it carries replaced by the octets given,
        /// keeping whether it is critical.
        /// </summary>
        public static Asn1Sequence WithExtensionValue(Asn1Sequence tbs, DerObjectIdentifier oid, byte[] value)
        {
            var at = ExtensionsIndex(tbs);
            var extensions = Asn1Sequence.GetInstance(Asn1TaggedObject.GetInstance(tbs[at]).GetExplicitBaseObject());
            var found = false;
            var kept = new Asn1EncodableVector();

            foreach (var extension in extensions.Select(element => Asn1Sequence.GetInstance(element)))
            {
                if (!DerObjectIdentifier.GetInstance(extension[0]).Equals(oid))
                {
                    kept.Add(extension);
                    continue;
                }

                found = true;
                kept.Add(extension.Count == 3
                    ? new DerSequence(extension[0], extension[1], new DerOctetString(value))
                    : new DerSequence(extension[0], new DerOctetString(value)));
            }

            if (!found)
            {
                throw new ArgumentException("the certificate carries no " + oid, nameof(oid));
            }

            return Replaced(tbs, at, new DerTaggedObject(true, 3, new DerSequence(kept)));
        }

        private static Asn1Sequence SignedPart(byte[] der)
        {
            return Asn1Sequence.GetInstance(Asn1Sequence.GetInstance(der)[0]);
        }

        private static Asn1Sequence WithSignatureField(Asn1Sequence tbs, AlgorithmIdentifier algorithm)
        {
            // After the version when there is one, which an explicit tag zero is, and the serial.
            return Replaced(tbs, IsVersioned(tbs) ? 2 : 1, algorithm);
        }

        private static byte[] Signature(ISignatureFactory factory, Asn1Sequence tbs)
        {
            var calculator = factory.CreateCalculator();
            var signed = tbs.GetDerEncoded();
            using (var stream = calculator.Stream)
            {
                stream.Write(signed, 0, signed.Length);
            }

            return calculator.GetResult().Collect();
        }

        private static byte[] Assembled(Asn1Sequence tbs, AlgorithmIdentifier algorithm, byte[] signature)
        {
            return new DerSequence(tbs, algorithm, new DerBitString(signature)).GetDerEncoded();
        }

        private static bool IsVersioned(Asn1Sequence tbs)
        {
            return tbs[0] is Asn1TaggedObject;
        }

        private static int KeyIndex(Asn1Sequence tbs)
        {
            // Serial, signature, issuer, validity and subject come before it.
            return IsVersioned(tbs) ? 6 : 5;
        }

        private static int ExtensionsIndex(Asn1Sequence tbs)
        {
            for (var at = 0; at < tbs.Count; at++)
            {
                var tagged = tbs[at] as Asn1TaggedObject;
                if (tagged != null && tagged.TagNo == 3)
                {
                    return at;
                }
            }

            throw new ArgumentException("the certificate carries no extensions", nameof(tbs));
        }

        private static Asn1Sequence Replaced(Asn1Sequence sequence, int at, Asn1Encodable value)
        {
            var elements = new List<Asn1Encodable>();
            for (var index = 0; index < sequence.Count; index++)
            {
                elements.Add(index == at ? value : sequence[index]);
            }

            return new DerSequence(elements.ToArray());
        }
    }
}
