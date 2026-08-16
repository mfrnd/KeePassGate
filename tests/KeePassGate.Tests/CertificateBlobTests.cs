using System;
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
        [NonParallelizable]
        public void No_key_reaches_the_disk_from_minting_one_to_loading_it_back()
        {
            // Measured: importing a PKCS#12 without EphemeralKeySet writes a key file into the
            // user's profile, which would put a key outside the database that is meant to be the
            // only place it lives. Nothing the plugin loads asks for that any more, and that is
            // what decided the transport, `CA.md` §222: an ephemeral key cannot be used with
            // SChannel at all, so the platform's own TLS would have meant a host key on the disk.
            //
            // The baseline used to be taken after this fixture had already minted an authority,
            // which left the two steps most likely to write a key outside what was measured:
            // generating the key pair, and `CopyWithPrivateKey`, which is how every leaf under
            // that authority gets its key. Every step from the first key pair to the last import
            // is inside the window now.
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
        /// loading the stored host back.
        /// </summary>
        private static string[] KeysAppearingFromMintingToLoading()
        {
            var before = PrivateKeyFiles.Names();

            var issuer = new AuthorityIssuer();
            using (var authority = issuer.CreateAuthority())
            using (var host = issuer.IssueHost(authority))
            using (var client = issuer.IssueClient(authority, "agent-fictitious", new[] { "ops" }))
            using (var restored = CertificateBlob.Load(CertificateBlob.Save(host)))
            {
                Assert.That(restored.HasPrivateKey, Is.True);
                Assert.That(client.HasPrivateKey, Is.True);
                return PrivateKeyFiles.Appeared(before);
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
