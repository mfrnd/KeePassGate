using System;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// A revocation names the authority that issued its certificate, and goes once that authority
    /// is no longer held, rather than only once the certificate expires.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-11-08. Recreating or deleting the authority takes every
    /// revocation away, and a merge with a copy from before brings them back, where nothing pruned
    /// them until their certificates expired and the clients page had no row to take them away
    /// from. The retirement refuses everything the old authority issued, so they had nothing left
    /// to refuse.
    /// </remarks>
    [TestFixture]
    public sealed class RevocationAuthorityTests
    {
        private InMemoryKeyValueStore _values;
        private RecordingGateLog _log;
        private AuthorityService _service;
        private AdmissionStore _admissions;

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _log = new RecordingGateLog();
            _service = new AuthorityService(_values, _log);
            _admissions = new AdmissionStore(_values, _log);
            Assume.That(_service.Create().Succeeded, Is.True);
        }

        [Test]
        public void A_revocation_names_its_certificates_end_and_the_authority_that_issued_it()
        {
            using (var certificate = Admitted("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);

                Assert.That(_admissions.Remove(key), Is.True);

                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.EqualTo(End(certificate) + " " + Served()));
            }
        }

        [Test]
        public void A_revocation_a_merge_brings_back_after_a_recreation_goes_with_the_next_write()
        {
            using (var certificate = Admitted("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                Assume.That(_admissions.Remove(key), Is.True);
                var revocation = _values.Get(AuthorityKeys.RevocationKey(key));

                Assume.That(_service.Recreate().Succeeded, Is.True);
                Assume.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.Null, "recreating took it away");

                // What a synchronize with a copy from before the recreation puts back, beside a
                // revocation written before revocations named their authority.
                _values.Plant(AuthorityKeys.RevocationKey(key), revocation);
                _values.Plant(AuthorityKeys.ClientRecordKey(key), string.Empty);
                var older = new string('e', 64);
                _values.Plant(AuthorityKeys.RevocationKey(older), End(certificate));
                _values.Plant(AuthorityKeys.ClientRecordKey(older), string.Empty);

                var removed = new AuthorityStore(_values, _log).Prune();

                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.Null,
                    "the revocation of a retired authority's certificate is taken away");
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(key)), Is.Null, "with its emptied record");
                Assert.That(removed, Has.Some.Matches<System.Collections.Generic.KeyValuePair<string, string>>(
                    pair => pair.Key == AuthorityKeys.RevocationKey(key) && pair.Value == revocation),
                    "and named among what went, so that a write that does not land puts it back");
                Assert.That(_values.Get(AuthorityKeys.RevocationKey(older)), Is.EqualTo(End(certificate)),
                    "one naming no authority is left to expire");
            }
        }

        [Test]
        public void A_revocation_of_its_own_takes_away_those_of_an_authority_no_longer_held()
        {
            string stale;
            string revocation;
            using (var certificate = Admitted("agent-fictitious"))
            {
                stale = CertificateDigest.Of(certificate);
                Assume.That(_admissions.Remove(stale), Is.True);
                revocation = _values.Get(AuthorityKeys.RevocationKey(stale));
            }

            Assume.That(_service.Recreate().Succeeded, Is.True);
            _values.Plant(AuthorityKeys.RevocationKey(stale), revocation);
            _values.Plant(AuthorityKeys.ClientRecordKey(stale), string.Empty);

            using (var certificate = Admitted("agent-invented"))
            {
                Assert.That(_admissions.Remove(CertificateDigest.Of(certificate)), Is.True);
            }

            Assert.That(_values.Get(AuthorityKeys.RevocationKey(stale)), Is.Null);
            Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(stale)), Is.Null);
        }

        [Test]
        public void A_database_holding_several_authorities_keeps_the_revocations_of_each()
        {
            // Retired or gone is the test, never served: until the operator keeps one, neither of
            // two authorities side by side is served, and either may be the one kept.
            using (var certificate = Admitted("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                Assume.That(_admissions.Remove(key), Is.True);

                var elsewhere = new InMemoryKeyValueStore();
                Assume.That(new AuthorityService(elsewhere, _log).Create().Succeeded, Is.True);
                foreach (var stored in elsewhere.Keys)
                {
                    _values.Plant(stored, elsewhere.Get(stored));
                }

                using (var reading = _service.Read())
                {
                    Assume.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                }

                new AuthorityStore(_values, _log).Prune();

                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null);
            }
        }

        [Test]
        public void A_value_that_names_something_other_than_a_digest_names_no_authority()
        {
            Assert.That(AuthorityKeys.AuthorityInRevocation("2027-01-01T00:00:00Z not-a-digest"), Is.Null);
            Assert.That(AuthorityKeys.AuthorityInRevocation("2027-01-01T00:00:00Z"), Is.Null);
            Assert.That(AuthorityKeys.AuthorityInRevocation(string.Empty), Is.Null);
            Assert.That(AuthorityKeys.NotAfterInRevocation("2027-01-01T00:00:00Z " + new string('a', 64)),
                Is.EqualTo("2027-01-01T00:00:00Z"));
        }

        /// <summary>A certificate the served authority issued, with a record admitting it.</summary>
        private X509Certificate2 Admitted(string name)
        {
            using (var reading = _service.Read())
            {
                var certificate = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, null).Read();
                _admissions.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));
                return certificate;
            }
        }

        /// <summary>The digest of the authority the store serves now.</summary>
        private string Served()
        {
            using (var reading = _service.Read())
            {
                return reading.Material.AuthorityDigest;
            }
        }

        /// <summary>A certificate's end, as a revocation writes it.</summary>
        private static string End(X509Certificate2 certificate)
        {
            return Moments.Wire(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
        }
    }
}
