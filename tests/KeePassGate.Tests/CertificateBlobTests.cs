using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Testing;
using NUnit.Framework;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Oiw;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace KeePassGate.Tests
{
    [TestFixture]
    public sealed class CertificateBlobTests
    {
        private CertifiedKey _certificate;

        [SetUp]
        public void SetUp()
        {
            _certificate = new AuthorityIssuer().CreateAuthority();
        }

        [Test]
        public void A_certificate_survives_the_round_trip_with_its_key()
        {
            var blob = CertificateBlob.Save(_certificate);

            var restored = CertificateBlob.Load(blob);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.CertificateDer, Is.EqualTo(_certificate.CertificateDer));
            Assert.That(((ECPrivateKeyParameters)restored.PrivateKey).D,
                Is.EqualTo(((ECPrivateKeyParameters)_certificate.PrivateKey).D));
        }

        [Test]
        public void The_stored_form_is_the_shape_Windows_wrote()
        {
            // A SHA-1 MAC and both bags under three-key triple DES, which is what a database
            // already holds. The certificate's bag is the one that would have changed: Bouncy
            // Castle's own choice is RC2 with a forty-bit key, which OpenSSL 3 reads only through
            // its legacy provider, and the client's `cryptography` would have refused the identity.
            var pfx = Pfx.GetInstance(Asn1Object.FromByteArray(CertificateBlob.Bytes(CertificateBlob.Save(_certificate))));
            Assert.That(pfx.MacData.Mac.DigestAlgorithm.Algorithm, Is.EqualTo(OiwObjectIdentifiers.IdSha1));

            var inside = Asn1OctetString.GetInstance(pfx.AuthSafe.Content).GetOctets();
            var algorithms = AuthenticatedSafe.GetInstance(Asn1Object.FromByteArray(inside)).GetContentInfo()
                .SelectMany(Algorithms)
                .ToArray();

            Assert.That(algorithms, Has.Length.EqualTo(2), "one bag for the key and one for the certificate");
            Assert.That(algorithms, Is.All.EqualTo(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc));
        }

        [Test]
        public void Windows_still_reads_the_stored_form_with_its_key()
        {
            // Nothing in the plugin asks it to, and the test clients and anybody importing an
            // identity a pairing handed out do.
            var stored = CertificateBlob.Bytes(CertificateBlob.Save(_certificate));
            using (var windows = new X509Certificate2(
                       stored, (string)null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable))
            {
                Assert.That(windows.RawData, Is.EqualTo(_certificate.CertificateDer));
                Assert.That(windows.HasPrivateKey, Is.True);
                Assert.That(windows.GetECDsaPrivateKey(), Is.Not.Null);
            }
        }

        [Test]
        public void A_certificate_Windows_stored_still_loads_with_its_key_and_its_name()
        {
            // Windows read it when it was stored, and its digest is the name every record and
            // retirement in that database carries.
            var stored = CertificateBlob.Load(SeededAuthority.Authority);

            Assert.That(stored, Is.Not.Null);
            using (var windows = new X509Certificate2(
                       CertificateBlob.Bytes(SeededAuthority.Authority), (string)null, X509KeyStorageFlags.EphemeralKeySet))
            {
                Assert.That(CertificateDigest.Of(stored.CertificateDer), Is.EqualTo(CertificateDigest.Of(windows)),
                    "the digest a database names it by has not moved");
            }
        }

        [Test]
        public void The_stored_form_is_base64_so_it_can_live_in_a_string_dictionary()
        {
            var blob = CertificateBlob.Save(_certificate);

            Assert.DoesNotThrow(() => Convert.FromBase64String(blob));
            Assert.That(blob, Does.Not.Contain("\n"));
            Assert.That(blob, Does.Not.Contain("\r"));
        }

        [Test]
        [NonParallelizable]
        public void No_key_reaches_the_disk_from_minting_one_to_loading_it_back()
        {
            // Locked against every other test process this account runs, so that a key one of
            // them writes never lands inside this window: `PrivateKeyFiles.Lock` says why.
            using (PrivateKeyFiles.Lock())
            {
                No_key_reaches_the_disk_from_minting_one_to_loading_it_back_while_locked();
            }
        }

        private void No_key_reaches_the_disk_from_minting_one_to_loading_it_back_while_locked()
        {
            // Measured: importing a PKCS#12 without EphemeralKeySet writes a key file into the
            // user's profile, which would put a key outside the database that is meant to be the
            // only place it lives. Nothing the plugin loads hands Windows a key at all now, and
            // keeping one off the disk is what decided the transport, `CA.md` §222: an ephemeral
            // key cannot be used with SChannel at all, so the platform's own TLS would have meant a
            // host key on the disk.
            //
            // The baseline used to be taken after this fixture had already minted an authority,
            // which left the two steps most likely to write a key outside what was measured:
            // generating the key pair, and giving each leaf its key. Every step from the first key
            // pair to the last load is inside the window now.
            //
            // **Asked more than once, because the directories are not ours.** They belong to the
            // whole logon session, so a key any other process writes inside the window, the other
            // test assembly `dotnet test` runs beside this one among them, reads exactly like a
            // regression. A path that writes a key writes one every time, so one attempt that sees
            // nothing acquits it, and only every attempt seeing something accuses it. A quiet
            // moment afterwards then tells a machine writing keys throughout from the code under
            // test, and says so rather than guessing. Each attempt mints keys of its own, so none
            // of them reuses a key an earlier one already put somewhere.
            var seen = new System.Collections.Generic.List<string>();

            for (var attempt = 0; attempt < KeyAttempts; attempt++)
            {
                var appeared = KeysAppearingFromMintingToLoading();
                if (appeared.Length == 0)
                {
                    return;
                }

                seen.AddRange(appeared);
            }

            var idle = PrivateKeyFiles.Names();
            System.Threading.Thread.Sleep(KeyQuietWindow);
            if (PrivateKeyFiles.Appeared(idle).Length > 0)
            {
                Assert.Inconclusive(
                    "something else on this machine is writing private keys throughout, so this run " +
                    "cannot tell whether minting and loading wrote one");
            }

            Assert.Fail("a private key reached the disk on every attempt: " + string.Join(", ", seen));
        }

        /// <summary>How many attempts in a row have to write a key before the code is accused.</summary>
        private const int KeyAttempts = 3;

        /// <summary>How long nothing is done while the key directories are watched for anybody else.</summary>
        private static readonly TimeSpan KeyQuietWindow = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// The key files that appeared from minting an authority, a host and a client under it, to
        /// holding them as the plugin does and loading the stored host back.
        /// </summary>
        private static string[] KeysAppearingFromMintingToLoading()
        {
            var before = PrivateKeyFiles.Names();

            var issuer = new AuthorityIssuer();
            var authority = issuer.CreateAuthority();
            var host = issuer.IssueHost(authority);
            issuer.IssueClient(authority, "agent-fictitious", new[] { "ops" });
            using (var reading = new AuthorityMaterial(authority, host, new byte[AuthorityMaterial.SniKeyLength]))
            {
                Assert.That(CertificateBlob.Load(CertificateBlob.Save(reading.HostKey)), Is.Not.Null);
                return PrivateKeyFiles.Appeared(before);
            }
        }

        [Test]
        public void Saving_nothing_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => CertificateBlob.Save(null));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not base64 at all")]
        [TestCase("AAAA")]
        public void Anything_unreadable_comes_back_as_nothing(string blob)
        {
            Assert.That(CertificateBlob.Load(blob), Is.Null);
        }

        [Test]
        public void A_certificate_stored_without_its_key_comes_back_as_nothing()
        {
            // Half an authority is damage, and damage must never be mistaken for something usable.
            var withoutKey = Convert.ToBase64String(_certificate.CertificateDer);

            Assert.That(CertificateBlob.Load(withoutKey), Is.Null);
        }

        [Test]
        public void A_PKCS12_holding_the_certificate_and_no_key_comes_back_as_nothing()
        {
            var store = new Pkcs12StoreBuilder()
                .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .Build();
            store.SetCertificateEntry("certificate only", new X509CertificateEntry(_certificate.Certificate));

            using (var bytes = new MemoryStream())
            {
                store.Save(bytes, Array.Empty<char>(), new SecureRandom());
                Assert.That(CertificateBlob.Load(Convert.ToBase64String(bytes.ToArray())), Is.Null);
            }
        }

        [Test]
        public void A_truncated_blob_comes_back_as_nothing()
        {
            var blob = CertificateBlob.Save(_certificate);
            var bytes = Convert.FromBase64String(blob);
            var truncated = Convert.ToBase64String(bytes.Take(bytes.Length / 2).ToArray());

            Assert.That(CertificateBlob.Load(truncated), Is.Null);
        }

        /// <summary>The algorithm each bag in one part of a PKCS#12 is encrypted under.</summary>
        private static DerObjectIdentifier[] Algorithms(ContentInfo part)
        {
            if (part.ContentType.Equals(PkcsObjectIdentifiers.EncryptedData))
            {
                return new[] { EncryptedData.GetInstance(part.Content).EncryptionAlgorithm.Algorithm };
            }

            var bags = Asn1Sequence.GetInstance(
                Asn1Object.FromByteArray(Asn1OctetString.GetInstance(part.Content).GetOctets()));
            return bags.Cast<Asn1Encodable>()
                .Select(SafeBag.GetInstance)
                .Where(bag => bag.BagID.Equals(PkcsObjectIdentifiers.Pkcs8ShroudedKeyBag))
                .Select(bag => EncryptedPrivateKeyInfo.GetInstance(bag.BagValue).EncryptionAlgorithm.Algorithm)
                .ToArray();
        }
    }
}
