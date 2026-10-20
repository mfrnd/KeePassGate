using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Host.Tests.Live;
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
    /// admitted client had expired, taken by a fixture nobody was watching.
    ///
    /// So the date is asserted here, with enough warning to act on. This test failing is not a bug:
    /// it is the fixture asking to be regenerated, along with the pictures that show its fingerprint.
    ///
    /// Read out of the stored line rather than through `AdmissionRecord`, which does not exist yet
    /// at the step `SeededAuthority` arrives in and would put a parser between the assertion and
    /// the bytes it is about.
    /// </remarks>
    [TestFixture]
    public sealed class SeededAuthorityTests
    {
        private const string Field = "certificate=";

        private static readonly TimeSpan Warning = TimeSpan.FromDays(90);

        [Test]
        public void The_seeded_client_certificate_is_not_close_to_expiring()
        {
            var line = SeededAuthority.ClientRecord
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
                    ". Regenerate SeededAuthority, and the screenshots that show its fingerprint.");
            }
        }
    }
}
