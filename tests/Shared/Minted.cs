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

        /// <summary>
        /// The subject key identifier's contents as they are written, or null for none. Written
        /// when the certificate is signed rather than when it is begun, so that a test can leave it
        /// out or write it as bytes no issuer would.
        /// </summary>
        private byte[] _keyIdentifier;

        private Minted(X509Name subject)
        {
            var keys = new ECKeyPairGenerator();
            keys.Init(new ECKeyGenerationParameters(X9ObjectIdentifiers.Prime256v1, _random));
            _key = keys.GenerateKeyPair();
            _subject = subject;

            _generator.SetSerialNumber(new BigInteger(1, AuthorityIssuer.NewSerialNumber()));
            _generator.SetSubjectDN(_subject);
            _generator.SetPublicKey(_key.Public);
            _keyIdentifier = X509ExtensionUtilities.CreateSubjectKeyIdentifier(_key.Public).GetEncoded();

            var now = DateTimeOffset.UtcNow;
            Lasting(now - AuthorityIssuer.Backdate, now.AddDays(30));
        }

        /// <summary>A certificate for the distinguished name given, written the way `X509Name` parses it.</summary>
        public static Minted Named(string subject)
        {
            return new Minted(new X509Name(subject));
        }

        /// <summary>A certificate for the name given, in exactly the string types it holds.</summary>
        public static Minted Named(X509Name subject)
        {
            return new Minted(subject);
        }

        /// <summary>With no subject key identifier at all, which nothing this plugin issues lacks.</summary>
        public Minted WithoutKeyIdentifier()
        {
            _keyIdentifier = null;
            return this;
        }

        /// <summary>With the subject key identifier's contents written as the bytes given, whatever they are.</summary>
        public Minted WithKeyIdentifier(byte[] written)
        {
            _keyIdentifier = written;
            return this;
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
            return IssuedBy(authority, authority.Certificate.SubjectDN);
        }

        /// <summary>
        /// The same, naming the authority as the issuer in the bytes the test gives rather than in
        /// the authority's own.
        /// </summary>
        /// <remarks>
        /// The pointer is computed from the authority's key, the first method of RFC 5280 section
        /// 4.2.1.2, rather than copied out of the identifier it carries: that is the same value for
        /// every authority anybody issues, and it keeps a leaf possible under an authority a test
        /// wrote an identifier for that does not read.
        /// </remarks>
        public CertifiedKey IssuedBy(CertifiedKey authority, X509Name naming)
        {
            _generator.SetIssuerDN(naming);
            _generator.AddExtension(
                X509Extensions.AuthorityKeyIdentifier,
                false,
                X509ExtensionUtilities.CreateAuthorityKeyIdentifier(authority.Certificate.GetPublicKey()));

            return Signed(authority.PrivateKey);
        }

        private CertifiedKey Signed(AsymmetricKeyParameter signer)
        {
            if (_keyIdentifier != null)
            {
                // As the extension's value itself: the overload taking bytes wraps them in an
                // octet string of its own, which would make any bytes a readable identifier.
                _generator.AddExtension(
                    X509Extensions.SubjectKeyIdentifier, new X509Extension(false, new DerOctetString(_keyIdentifier)));
            }

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
