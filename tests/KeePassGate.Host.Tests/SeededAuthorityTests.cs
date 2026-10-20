using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Host.Tests.Live;
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
    /// regeneration. What that buys in stability it pays for in a date: the certificate it carries
    /// expires, and the first sign would otherwise be a published screenshot of a database whose one
    /// admitted client had expired, taken by a fixture nobody was watching. `SeededCopies`, written
    /// down for the picture of the authority chooser, carries three more.
    ///
    /// So the date is asserted here, with enough warning to act on. This test failing is not a bug:
    /// it is the fixture asking to be regenerated, along with the pictures that show its fingerprint.
    ///
    /// Read out of the stored line rather than through `AdmissionRecord`, which does not exist yet
    /// at the step `SeededAuthority` arrives in and would put a parser between the assertion and the
    /// bytes it is about.
    /// </remarks>
    [TestFixture]
    public sealed class SeededAuthorityTests
    {
        private const string Field = "certificate=";

        private const string Admitted = "admitted=";

        private static readonly TimeSpan Warning = TimeSpan.FromDays(90);

        /// <summary>Every written down client record, and the class to regenerate when one expires.</summary>
        private static readonly object[] Seeded =
        {
            new object[] { nameof(SeededAuthority), SeededAuthority.ClientRecord },
            new object[] { nameof(SeededCopies), SeededCopies.AgentRecord },
            new object[] { nameof(SeededCopies), SeededCopies.BackupRecord },
            new object[] { nameof(SeededCopies), SeededCopies.OtherAgentRecord }
        };

        [TestCaseSource(nameof(Seeded))]
        public void The_seeded_client_certificate_is_not_close_to_expiring(string seededBy, string record)
        {
            var line = record
                .Split('\n')
                .Single(l => l.StartsWith(Field, StringComparison.Ordinal));

            var der = Convert.FromBase64String(line.Substring(Field.Length));

            using (var certificate = new X509Certificate2(der))
            {
                var expires = certificate.NotAfter.ToUniversalTime();

                Assert.That(
                    expires - DateTime.UtcNow,
                    Is.GreaterThan(Warning),
                    "the seeded certificate expires on " + expires.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                    ". Regenerate " + seededBy + ", and the screenshots that show its fingerprint.");
            }
        }

        [TestCaseSource(nameof(Seeded))]
        public void The_seeded_client_was_admitted_while_its_certificate_was_valid(string seededBy, string record)
        {
            // Pairing stamps a record as it writes it, after the certificate is issued, and the
            // clients page shows that moment beside the certificate's end. `SeededAuthority` said
            // March, five months before its certificate began, which no pairing can produce.
            var lines = record.Split('\n');
            var admitted = DateTimeOffset.Parse(
                lines.Single(l => l.StartsWith(Admitted, StringComparison.Ordinal)).Substring(Admitted.Length),
                CultureInfo.InvariantCulture).UtcDateTime;
            var der = Convert.FromBase64String(
                lines.Single(l => l.StartsWith(Field, StringComparison.Ordinal)).Substring(Field.Length));

            using (var certificate = new X509Certificate2(der))
            {
                Assert.That(
                    admitted,
                    Is.InRange(certificate.NotBefore.ToUniversalTime(), certificate.NotAfter.ToUniversalTime()),
                    seededBy + " admits a client at a moment its certificate does not cover");
            }
        }

        /// <summary>
        /// The written down client is a record the plugin reads, filed under its key and known by
        /// the certificate it paired with.
        /// </summary>
        /// <remarks>
        /// Asked here rather than found out from a screenshot run, for the reason the test below
        /// gives: a record the plugin will not read is listed as one that cannot be, and every
        /// picture of the clients page would show that.
        /// </remarks>
        [Test]
        public void The_seeded_client_record_reads_as_the_client_it_was_paired_as()
        {
            var admissionKey = AuthorityKeys.AdmissionKeyIn(SeededAuthority.ClientKey);
            var record = AdmissionRecord.Load(admissionKey, SeededAuthority.ClientRecord);

            Assert.That(record, Is.Not.Null);
            Assert.That(record.ClientKey, Is.EqualTo(admissionKey));
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
                var store = new CustomDataStore(merged.Database);
                Write(store, SeededCopies.ThisCopyAuthority, SeededCopies.ThisCopyHost, SeededCopies.ThisCopySniKey);
                Write(store, SeededCopies.OtherCopyAuthority, SeededCopies.OtherCopyHost, SeededCopies.OtherCopySniKey);
                store.Set(SeededCopies.AgentKey, SeededCopies.AgentRecord);
                store.Set(SeededCopies.BackupKey, SeededCopies.BackupRecord);
                store.Set(SeededCopies.OtherAgentKey, SeededCopies.OtherAgentRecord);

                using (var reading = new AuthorityStore(store, NullGateLog.Instance).Read())
                {
                    var choice = AuthorityChoice.From(
                        reading, new AdmissionStore(store, NullGateLog.Instance).All(), null, NullGateLog.Instance);

                    var offered = choice.Options.Select(option => option.CanKeep
                        ? string.Join(", ", option.Clients.Select(client => client.Name))
                        : option.Problem);

                    Assert.That(offered, Is.EquivalentTo(new[] { "backup-invented, agent-invented", "agent-fictitious" }));
                }
            }
        }

        private static void Write(CustomDataStore store, string authority, string host, string sniKey)
        {
            using (var certificate = CertificateBlob.Load(authority))
            {
                store.Set(
                    AuthorityKeys.AuthorityKey(CertificateDigest.Of(certificate)),
                    AuthorityDocument.Save(authority, host, sniKey));
            }
        }
    }
}
