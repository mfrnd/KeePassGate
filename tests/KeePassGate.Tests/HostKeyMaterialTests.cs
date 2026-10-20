using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Gate;
using NUnit.Framework;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Oiw;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A stored host certificate Bouncy Castle cannot read, in each of the ways it says so.
    /// </summary>
    /// <remarks>
    /// Built from a real host certificate and then altered. The first two are the ones that
    /// matter, because Windows accepts them: a second key bag beside the host's own, under a MAC
    /// that is still valid. `X509Certificate2` is what vets a stored host certificate, so a
    /// database somebody crafted can hold either, and only this parser ever meets the extra bag.
    /// </remarks>
    [TestFixture]
    public sealed class HostKeyMaterialTests
    {
        private byte[] _stored;

        [SetUp]
        public void SetUp()
        {
            var issuer = new AuthorityIssuer();
            using (var authority = issuer.CreateAuthority())
            using (var host = issuer.IssueHost(authority))
            {
                _stored = CertificateBlob.Bytes(CertificateBlob.Save(host));
            }
        }

        [Test]
        public void A_second_key_bag_that_will_not_decrypt_is_no_host_rather_than_an_exception()
        {
            // Bouncy Castle answers the padding it cannot undo with `InvalidCipherTextException`,
            // which is a `CryptoException` and neither of the two types this used to name.
            var crafted = WithBag(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc, 2000);

            AssumeWindowsAccepts(crafted);
            Assert.That(HostKeyMaterial.From(crafted), Is.Null);
        }

        [Test]
        public void A_second_key_bag_with_a_negative_iteration_count_is_no_host_either()
        {
            // `InvalidOperationException`, the same type an ASN.1 length that runs out arrives as.
            var crafted = WithBag(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc, -1);

            AssumeWindowsAccepts(crafted);
            Assert.That(HostKeyMaterial.From(crafted), Is.Null);
        }

        [Test]
        public void A_MAC_under_an_algorithm_nobody_knows_is_no_host_either()
        {
            // `SecurityUtilityException`, which derives from nothing but `Exception`. Windows
            // refuses this one, so it arrives only as bytes nothing vetted, but the promise is
            // about anything that will not parse.
            var pfx = Pfx.GetInstance(Asn1Object.FromByteArray(_stored));
            var mac = pfx.MacData;
            var unknown = new MacData(
                new DigestInfo(new AlgorithmIdentifier(new DerObjectIdentifier("1.2.3.4.5")), mac.Mac.Digest.GetOctets()),
                mac.GetSalt(),
                mac.IterationCount.IntValueExact);

            Assert.That(HostKeyMaterial.From(new Pfx(pfx.AuthSafe, unknown).GetDerEncoded()), Is.Null);
        }

        [Test]
        public void The_host_itself_still_reads_after_all_of_that()
        {
            Assert.That(HostKeyMaterial.From(_stored), Is.Not.Null);
        }

        private static void AssumeWindowsAccepts(byte[] crafted)
        {
            using (var loaded = CertificateBlob.Load(Convert.ToBase64String(crafted)))
            {
                Assume.That(loaded, Is.Not.Null,
                    "Windows accepts it, which is what makes it something a database can hold");
            }
        }

        /// <summary>
        /// The stored host certificate with one more shrouded key bag, whose contents are bytes
        /// that were never encrypted, and a MAC computed again over the result.
        /// </summary>
        private byte[] WithBag(DerObjectIdentifier algorithm, int iterations)
        {
            var pfx = Pfx.GetInstance(Asn1Object.FromByteArray(_stored));
            var mac = pfx.MacData;
            var inside = Asn1OctetString.GetInstance(pfx.AuthSafe.Content).GetOctets();
            var contents = AuthenticatedSafe.GetInstance(Asn1Object.FromByteArray(inside)).GetContentInfo();

            var garbage = Enumerable.Range(0, 64).Select(i => (byte)(i * 37 + 11)).ToArray();
            var parameters = new Pkcs12PbeParams(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, iterations);
            var bag = new SafeBag(
                PkcsObjectIdentifiers.Pkcs8ShroudedKeyBag,
                new EncryptedPrivateKeyInfo(new AlgorithmIdentifier(algorithm, parameters), garbage).ToAsn1Object());
            var extra = new ContentInfo(PkcsObjectIdentifiers.Data, new DerOctetString(new DerSequence(bag).GetEncoded()));

            var data = new AuthenticatedSafe(contents.Concat(new[] { extra }).ToArray()).GetEncoded();
            var salt = mac.GetSalt();
            var count = mac.IterationCount.IntValueExact;
            var digest = new DigestInfo(new AlgorithmIdentifier(OiwObjectIdentifiers.IdSha1, DerNull.Instance), Mac(data, salt, count));

            return new Pfx(new ContentInfo(PkcsObjectIdentifiers.Data, new DerOctetString(data)), new MacData(digest, salt, count))
                .GetDerEncoded();
        }

        /// <summary>The PKCS#12 MAC under the empty password, which is what the stored form uses.</summary>
        private static byte[] Mac(byte[] data, byte[] salt, int iterations)
        {
            var generator = new Pkcs12ParametersGenerator(new Sha1Digest());
            generator.Init(PbeParametersGenerator.Pkcs12PasswordToBytes(Array.Empty<char>()), salt, iterations);

            var hmac = new HMac(new Sha1Digest());
            hmac.Init((KeyParameter)generator.GenerateDerivedMacParameters(160));
            hmac.BlockUpdate(data, 0, data.Length);

            var output = new byte[hmac.GetMacSize()];
            hmac.DoFinal(output, 0);
            return output;
        }
    }
}
