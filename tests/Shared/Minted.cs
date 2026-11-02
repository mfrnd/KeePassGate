using System;
using KeePassGate.Authority;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace KeePassGate.Testing
{
    /// <summary>
    /// A certificate a test makes for itself, of a shape `AuthorityIssuer` never would: another
    /// name, dates of its own, an extension a stranger is free to write.
    /// </summary>
    /// <remarks>
    /// Through Bouncy Castle, as the plugin makes its own, so that a test pins what is done with a
    /// certificate rather than which library happened to make it, and so that no test needs a key
    /// Windows holds. ECDSA on P-256 over SHA-256, a fresh serial, a subject key identifier, and
    /// valid from five minutes ago for thirty days unless the test says otherwise; nothing else
    /// unless the test asks for it.
    ///
    /// In `tests/Shared` because both tiers make certificates, for the reason
    /// <see cref="PrivateKeyFiles"/> gives.
    /// </remarks>
    internal sealed class Minted
    {
        private readonly SecureRandom _random = new SecureRandom();
        private readonly AsymmetricCipherKeyPair _key;
        private readonly X509V3CertificateGenerator _generator = new X509V3CertificateGenerator();
        private readonly X509Name _subject;

        private Minted(string subject)
        {
            var keys = new ECKeyPairGenerator();
            keys.Init(new ECKeyGenerationParameters(X9ObjectIdentifiers.Prime256v1, _random));
            _key = keys.GenerateKeyPair();
            _subject = new X509Name(subject);

            _generator.SetSerialNumber(new BigInteger(1, AuthorityIssuer.NewSerialNumber()));
            _generator.SetSubjectDN(_subject);
            _generator.SetPublicKey(_key.Public);
            _generator.AddExtension(
                X509Extensions.SubjectKeyIdentifier,
                false,
                X509ExtensionUtilities.CreateSubjectKeyIdentifier(_key.Public));

            var now = DateTimeOffset.UtcNow;
            Lasting(now - AuthorityIssuer.Backdate, now.AddDays(30));
        }

        /// <summary>A certificate for the distinguished name given, written the way `X509Name` parses it.</summary>
        public static Minted Named(string subject)
        {
            return new Minted(subject);
        }

        /// <summary>Valid between the two moments, each kept to the second, which is all a certificate holds.</summary>
        public Minted Lasting(DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            _generator.SetNotBefore(Seconds(notBefore));
            _generator.SetNotAfter(Seconds(notAfter));
            return this;
        }

        /// <summary>`CA:TRUE` with `pathLen 0`, and allowed to sign certificates.</summary>
        public Minted AsAuthority()
        {
            _generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(0));
            return With(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
        }

        /// <summary>`CA:FALSE`, and for the one extended key usage named.</summary>
        public Minted ForUsage(string usageOid)
        {
            _generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            return With(
                X509Extensions.ExtendedKeyUsage,
                false,
                new ExtendedKeyUsage(new[] { new DerObjectIdentifier(usageOid) }));
        }

        /// <summary>Any extension at all, as the test writes it.</summary>
        public Minted With(DerObjectIdentifier oid, bool critical, Asn1Encodable value)
        {
            _generator.AddExtension(oid, critical, value);
            return this;
        }

        /// <summary>Signed with its own key, so its issuer is its own subject.</summary>
        public CertifiedKey SelfSigned()
        {
            _generator.SetIssuerDN(_subject);
            return Signed(_key.Private);
        }

        /// <summary>
        /// Signed by the authority given, naming it as the issuer in its own bytes and pointing
        /// back at its key, as a leaf the authority issued would.
        /// </summary>
        public CertifiedKey IssuedBy(CertifiedKey authority)
        {
            _generator.SetIssuerDN(authority.Certificate.SubjectDN);

            var stored = authority.Certificate.GetExtensionValue(X509Extensions.SubjectKeyIdentifier);
            if (stored != null)
            {
                var identifier = SubjectKeyIdentifier.GetInstance(X509ExtensionUtilities.FromExtensionValue(stored));
                _generator.AddExtension(
                    X509Extensions.AuthorityKeyIdentifier,
                    false,
                    new AuthorityKeyIdentifier(identifier.GetKeyIdentifier()));
            }

            return Signed(authority.PrivateKey);
        }

        private CertifiedKey Signed(AsymmetricKeyParameter signer)
        {
            // cspell:ignore WITHECDSA
            var certificate = _generator.Generate(new Asn1SignatureFactory("SHA256WITHECDSA", signer, _random));
            return new CertifiedKey(certificate, _key.Private);
        }

        private static DateTime Seconds(DateTimeOffset moment)
        {
            var ticks = moment.UtcDateTime.Ticks;
            return new DateTime(ticks - ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }
    }
}
