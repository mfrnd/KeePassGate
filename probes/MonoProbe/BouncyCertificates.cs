using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace MonoProbe
{
    /// <summary>A certificate Bouncy Castle made, and the key that goes with it.</summary>
    internal sealed class Issued
    {
        internal Issued(AsymmetricCipherKeyPair key, X509Certificate certificate)
        {
            Key = key;
            Certificate = certificate;
        }

        internal AsymmetricCipherKeyPair Key { get; }

        internal X509Certificate Certificate { get; }

        internal byte[] Der => Certificate.GetEncoded();
    }

    /// <summary>
    /// The same shapes the plugin issues, P-256 and SHA-256 with the same extensions, made by Bouncy
    /// Castle rather than by the runtime. The chain and TLS probes take their certificates from here
    /// so that their answers do not depend on whether the runtime can issue one.
    /// </summary>
    internal static class BouncyCertificates
    {
        private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
        private const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";

        private static readonly SecureRandom Random = new SecureRandom();

        internal static Issued Authority(string name, DateTime notAfter)
        {
            var key = NewKey();
            var generator = Start(name, name, key, notAfter);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(0));
            generator.AddExtension(X509Extensions.KeyUsage, true, new KeyUsage(KeyUsage.KeyCertSign | KeyUsage.CrlSign));
            return new Issued(key, generator.Generate(new Asn1SignatureFactory("SHA256WITHECDSA", key.Private, Random)));
        }

        internal static Issued Leaf(Issued issuer, string name, bool server, DateTime notAfter, string caIssuersUrl = null)
        {
            var key = NewKey();
            var generator = Start(issuer.Certificate.SubjectDN.ToString(), name, key, notAfter);
            generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(false));
            generator.AddExtension(X509Extensions.KeyUsage, true,
                new KeyUsage(KeyUsage.DigitalSignature | KeyUsage.KeyAgreement));
            generator.AddExtension(X509Extensions.ExtendedKeyUsage, false, new ExtendedKeyUsage(
                new List<DerObjectIdentifier> { new DerObjectIdentifier(server ? ServerAuthOid : ClientAuthOid) }));
            if (caIssuersUrl != null)
            {
                generator.AddExtension(X509Extensions.AuthorityInfoAccess, false, new AuthorityInformationAccess(
                    new AccessDescription(AccessDescription.IdADCAIssuers,
                        new GeneralName(GeneralName.UniformResourceIdentifier, caIssuersUrl))));
            }

            return new Issued(key, generator.Generate(
                new Asn1SignatureFactory("SHA256WITHECDSA", issuer.Key.Private, Random)));
        }

        /// <summary>A password-less PKCS#12 of the certificate and its key, as `CertificateBlob` stores one.</summary>
        internal static byte[] Pkcs12(Issued issued)
        {
            var store = new Pkcs12StoreBuilder().Build();
            var entry = new X509CertificateEntry(issued.Certificate);
            store.SetKeyEntry("probe", new AsymmetricKeyEntry(issued.Key.Private), new[] { entry });
            using (var stream = new MemoryStream())
            {
                store.Save(stream, Array.Empty<char>(), Random);
                return stream.ToArray();
            }
        }

        private static AsymmetricCipherKeyPair NewKey()
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, Random));
            return generator.GenerateKeyPair();
        }

        private static X509V3CertificateGenerator Start(string issuer, string subject, AsymmetricCipherKeyPair key, DateTime notAfter)
        {
            var generator = new X509V3CertificateGenerator();
            generator.SetSerialNumber(new BigInteger(120, Random).Add(BigInteger.One));
            generator.SetIssuerDN(new X509Name(issuer));
            generator.SetSubjectDN(new X509Name(subject));
            generator.SetNotBefore(DateTime.UtcNow.AddMinutes(-5));
            generator.SetNotAfter(notAfter);
            generator.SetPublicKey(key.Public);
            return generator;
        }
    }
}
