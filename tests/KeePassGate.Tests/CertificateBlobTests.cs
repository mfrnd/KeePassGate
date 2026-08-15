using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    public sealed class CertificateBlobTests
    {
        private X509Certificate2 _certificate;

        [SetUp]
        public void SetUp()
        {
            _certificate = new AuthorityIssuer().CreateAuthority();
        }

        [TearDown]
        public void TearDown()
        {
            if (_certificate != null)
            {
                _certificate.Reset();
            }
        }

        [Test]
        public void A_certificate_survives_the_round_trip_with_its_key()
        {
            var blob = CertificateBlob.Save(_certificate);

            using (var restored = CertificateBlob.Load(blob))
            {
                Assert.That(restored, Is.Not.Null);
                Assert.That(restored.RawData, Is.EqualTo(_certificate.RawData));
                Assert.That(restored.HasPrivateKey, Is.True);
                Assert.That(restored.GetECDsaPrivateKey(), Is.Not.Null);
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
        public void The_private_key_never_reaches_the_disk()
        {
            // Measured: importing a PKCS#12 without EphemeralKeySet writes a key file into the
            // user's profile, which would put the authority's key outside the database that is
            // meant to be the only place it lives.
            var keyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Crypto\Keys");

            var before = Directory.Exists(keyDirectory) ? Directory.GetFiles(keyDirectory).Length : 0;

            using (var restored = CertificateBlob.Load(CertificateBlob.Save(_certificate)))
            {
                Assert.That(restored.HasPrivateKey, Is.True);
                var during = Directory.Exists(keyDirectory) ? Directory.GetFiles(keyDirectory).Length : 0;
                Assert.That(during, Is.EqualTo(before));
            }
        }

        [Test]
        public void A_certificate_without_its_key_is_refused_rather_than_stored_uselessly()
        {
            using (var publicOnly = new X509Certificate2(_certificate.RawData))
            {
                Assert.Throws<ArgumentException>(() => CertificateBlob.Save(publicOnly));
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
            var withoutKey = Convert.ToBase64String(_certificate.Export(X509ContentType.Cert));

            Assert.That(CertificateBlob.Load(withoutKey), Is.Null);
        }

        [Test]
        public void A_truncated_blob_comes_back_as_nothing()
        {
            var blob = CertificateBlob.Save(_certificate);
            var bytes = Convert.FromBase64String(blob);
            var truncated = Convert.ToBase64String(bytes.Take(bytes.Length / 2).ToArray());

            Assert.That(CertificateBlob.Load(truncated), Is.Null);
        }
    }
}
