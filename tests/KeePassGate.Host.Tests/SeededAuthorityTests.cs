using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Host.Tests.Live;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The shelf life of the fixture the documentation is drawn from.
    /// </summary>
    /// <remarks>
    /// `SeededAuthority` is written down rather than minted, because ECDSA picks a random k and a
    /// certificate made twice has a different fingerprint, which would change the pictures on every
    /// regeneration. What that buys in stability it pays for in dates: the certificates it carries
    /// expire, the client's, the host's and the authority's, and the first sign would otherwise be a
    /// published screenshot of a database whose one admitted client had expired, or a retake whose
    /// pairing failed at the handshake, taken by a fixture nobody was watching. `SeededCopies`,
    /// written down for the picture of the authority chooser, carries two more authorities, their
    /// hosts and three more clients.
    ///
    /// So the dates are asserted here, with enough warning to act on. This test failing is not a
    /// bug: it is the fixture asking to be regenerated, along with the pictures that show its
    /// fingerprint.
    ///
    /// A client's dates are read out of the written down certificate rather than through
    /// `AdmissionRecord`, which would put a parser between the assertion and the bytes it is
    /// about; the admission is read through it to ask whether it reads as the client it was paired
    /// as. An authority's and a host's are in a PKCS#12, which only a parser opens, and they are
    /// read through the one the plugin loads them with.
    /// </remarks>
    [TestFixture]
    public sealed class SeededAuthorityTests
    {
        private static readonly TimeSpan Warning = TimeSpan.FromDays(90);

        /// <summary>
        /// Every written down client certificate, and the class to regenerate when one expires.
        /// </summary>
        private static readonly object[] Seeded =
        {
            new object[] { nameof(SeededAuthority), "agent-invented", SeededAuthority.ClientCertificate },
            new object[] { nameof(SeededCopies), "agent-invented", SeededCopies.AgentCertificate },
            new object[] { nameof(SeededCopies), "backup-invented", SeededCopies.BackupCertificate },
            new object[] { nameof(SeededCopies), "agent-fictitious", SeededCopies.OtherAgentCertificate }
        };

        /// <summary>
        /// Every written down admission, with the authority that issued its certificate.
        /// </summary>
        private static readonly object[] SeededAdmissions =
        {
            new object[] { nameof(SeededAuthority), "agent-invented", SeededAuthority.Client(), SeededAuthority.Authority },
            new object[] { nameof(SeededCopies), "agent-invented", SeededCopies.Agent(), SeededCopies.ThisCopyAuthority },
            new object[] { nameof(SeededCopies), "backup-invented", SeededCopies.Backup(), SeededCopies.ThisCopyAuthority },
            new object[] { nameof(SeededCopies), "agent-fictitious", SeededCopies.OtherAgent(), SeededCopies.OtherCopyAuthority }
        };

        /// <summary>Every written down authority and host certificate, as the stored PKCS#12.</summary>
        private static readonly object[] SeededAuthorities =
        {
            new object[] { nameof(SeededAuthority), "authority", SeededAuthority.Authority },
            new object[] { nameof(SeededAuthority), "host", SeededAuthority.Host },
            new object[] { nameof(SeededCopies), "authority of this copy", SeededCopies.ThisCopyAuthority },
            new object[] { nameof(SeededCopies), "host of this copy", SeededCopies.ThisCopyHost },
            new object[] { nameof(SeededCopies), "authority of the other copy", SeededCopies.OtherCopyAuthority },
            new object[] { nameof(SeededCopies), "host of the other copy", SeededCopies.OtherCopyHost }
        };

        [TestCaseSource(nameof(Seeded))]
        public void The_seeded_client_certificate_is_not_close_to_expiring(string seededBy, string client, string certificate)
        {
            using (var parsed = new X509Certificate2(Convert.FromBase64String(certificate)))
            {
                var expires = parsed.NotAfter.ToUniversalTime();

                Assert.That(
                    expires - DateTime.UtcNow,
                    Is.GreaterThan(Warning),
                    "the seeded certificate of " + client + " expires on " + expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                    ". Regenerate " + seededBy + ", and the screenshots that show its fingerprint.");
            }
        }

        [TestCaseSource(nameof(SeededAuthorities))]
        public void The_seeded_authority_and_host_certificates_are_not_close_to_expiring(
            string seededBy, string part, string blob)
        {
            // The film pairs against the written down host and the client verifies it against
            // the written down authority, so either expiring stops the pictures being retaken.
            // These were written down under an earlier profile, ten years for an authority and
            // five for a host, and so expire long before a hundred years would.
            var expires = CertificateBlob.Load(blob).Certificate.NotAfter;

            Assert.That(
                expires - DateTime.UtcNow,
                Is.GreaterThan(Warning),
                seededBy + "'s " + part + " expires on " + expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                ". Regenerate " + seededBy + ", and the screenshots that show its fingerprint.");
        }

        [TestCaseSource(nameof(SeededAdmissions))]
        public void The_seeded_client_was_admitted_while_its_certificate_was_valid(
            string seededBy, string client, AdmissionRecord admission, string authority)
        {
            // Pairing stamps an admission as it writes it, after the certificate is issued, and
            // the clients page shows that moment beside the certificate's end. `SeededAuthority`
            // held March, five months before its certificate began, which no pairing can produce.
            using (var certificate = admission.Certificate())
            {
                Assert.That(
                    admission.Admitted.UtcDateTime,
                    Is.InRange(certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime()),
                    seededBy + " admits " + client + " at a moment its certificate does not cover");
            }
        }

        /// <summary>
        /// The written down admission is a record the plugin reads, known by the certificate it
        /// paired with and issued by the authority written down beside it.
        /// </summary>
        /// <remarks>
        /// Asked here rather than found out from a screenshot run, for the reason the test below
        /// gives: a record the plugin will not read is listed as one that cannot be, and every
        /// picture of the clients page would show that.
        /// </remarks>
        [TestCaseSource(nameof(SeededAdmissions))]
        public void The_seeded_admission_reads_back_as_the_client_it_was_paired_as(
            string seededBy, string client, AdmissionRecord admission, string authority)
        {
            var read = AdmissionRecord.Read(null, admission.Members());

            Assert.That(read, Is.Not.Null, seededBy + " writes an admission of " + client + " that does not read");
            Assert.That(read.ClientKey, Is.EqualTo(read.AdmissionKey), "a client known by the certificate it paired with");
            Assert.That(read.Issuer, Is.EqualTo(CertificateDigest.Of(CertificateBlob.Load(authority).CertificateDer)));
            Assert.That(read.Subject, Is.EqualTo("CN=" + client));
        }

        /// <summary>
        /// What the picture of the authority chooser has to show, read the way the dialog reads it.
        /// </summary>
        /// <remarks>
        /// Asked here rather than found out from a screenshot run, because an authority whose
        /// written down parts did not belong together would be listed as one that cannot be kept,
        /// and a record filed under the wrong key would not be listed at all: either is a published
        /// picture of a state nobody meant to show.
        /// </remarks>
        [Test]
        public void The_two_written_down_copies_merged_offer_each_authority_with_its_own_clients()
        {
            using (var merged = TemporaryDatabase.New())
            {
                var records = new KdbxStore(merged.Database, GateVersion.Current, null);
                StoreSeeds.WrittenDown(records, SeededCopies.ThisCopyAuthority, SeededCopies.ThisCopyHost, SeededCopies.ThisCopySniKey);
                StoreSeeds.WrittenDown(records, SeededCopies.OtherCopyAuthority, SeededCopies.OtherCopyHost, SeededCopies.OtherCopySniKey);
                StoreSeeds.Client(records, SeededCopies.Agent().Members());
                StoreSeeds.Client(records, SeededCopies.Backup().Members());
                StoreSeeds.Client(records, SeededCopies.OtherAgent().Members());

                using (var reading = new AuthorityService(records, NullGateLog.Instance).Read())
                {
                    var choice = AuthorityChoice.From(reading, StoreSeeds.Listed(records), null, NullGateLog.Instance);

                    var offered = choice.Options.Select(option => option.CanKeep
                        ? string.Join(", ", option.Clients.Select(client => client.Name))
                        : option.Problem);

                    Assert.That(offered, Is.EquivalentTo(new[] { "backup-invented, agent-invented", "agent-fictitious" }));
                }
            }
        }
    }
}
