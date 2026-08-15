using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The allowlist, and the two things it has to get right: a certificate with no record is
    /// nobody, and a revoked one stays nobody even when its record comes back.
    /// </summary>
    [TestFixture]
    public sealed class AdmissionStoreTests
    {
        private TestAuthority _authority;
        private InMemoryKeyValueStore _values;
        private RecordingGateLog _log;
        private AdmissionStore _store;

        [SetUp]
        public void SetUp()
        {
            _authority = TestAuthority.New();
            _values = new InMemoryKeyValueStore();
            _log = new RecordingGateLog();
            _store = new AdmissionStore(_values, _log);
        }

        [TearDown]
        public void TearDown()
        {
            _authority.Dispose();
        }

        [Test]
        public void A_database_with_no_records_admits_nobody()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                Assert.That(_store.Find(CertificateDigest.Of(certificate)), Is.Null);
                Assert.That(_store.All().Records, Is.Empty);
                Assert.That(_store.Count(out _), Is.Zero);

                // Empty and read, which is what says nobody is admitted rather than that
                // nobody could be counted.
                Assert.That(_store.All().Complete, Is.True);
            }
        }

        [Test]
        public void A_written_record_comes_back()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));

                var found = _store.Find(CertificateDigest.Of(certificate));

                Assert.That(found, Is.Not.Null);
                Assert.That(found.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(found.Profiles, Is.EqualTo(new[] { "ops" }));
            }
        }

        [Test]
        public void Each_client_gets_a_key_of_its_own()
        {
            // One key per client rather than one document, because a replicated database merges
            // per key and a blob would be last-writer-wins.
            using (var first = _authority.IssueClient("agent-one"))
            using (var second = _authority.IssueClient("agent-two"))
            {
                _store.Write(AdmissionRecord.For(first, DateTimeOffset.UtcNow, null));
                _store.Write(AdmissionRecord.For(second, DateTimeOffset.UtcNow, null));

                Assert.That(_values.Keys.Count(AuthorityKeys.IsClientRecord), Is.EqualTo(2));
                Assert.That(_store.All().Records.Select(r => r.Subject).OrderBy(s => s),
                    Is.EqualTo(new[] { "CN=agent-one", "CN=agent-two" }));
            }
        }

        [Test]
        public void Writing_the_same_client_twice_replaces_its_record()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "readonly" }));

                Assert.That(_store.Count(out _), Is.EqualTo(1));
                Assert.That(_store.Find(CertificateDigest.Of(certificate)).Profiles,
                    Is.EqualTo(new[] { "readonly" }));
            }
        }

        [Test]
        public void Removing_a_record_is_how_a_client_is_revoked()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));

                Assert.That(_store.Remove(key), Is.True);

                Assert.That(_store.Find(key), Is.Null, "and it takes effect at once");
                Assert.That(_store.Remove(key), Is.False, "and removing it again is not an error");
            }
        }

        [Test]
        public void A_revocation_is_a_key_of_its_own_holding_the_certificates_end()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));

                Assume.That(_store.Remove(key), Is.True);

                var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)),
                    Is.EqualTo(notAfter.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", System.Globalization.CultureInfo.InvariantCulture)),
                    "the value is what says when it may be pruned");
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(key)), Is.Empty,
                    "and the record is still emptied, so it carries no profiles whatever happens to the revocation");
            }
        }

        [Test]
        public void A_record_that_comes_back_beside_its_revocation_admits_nobody()
        {
            // What a stale copy merged in does: the record is back, with its profiles, and the
            // revocation beside it is what refuses it.
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                _store.Write(record);
                Assume.That(_store.Remove(key), Is.True);

                _values.Plant(record.StorageKey, record.Save());

                Assert.That(_store.Find(key), Is.Null);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("came back")), Is.True,
                    "the log says why, because nothing else will");
                Assert.That(_store.All().Records, Is.Empty, "it is not listed as a client");
                Assert.That(_store.All().CameBack, Is.EqualTo(1), "it is counted as what it is");
                Assert.That(_store.Count(out _), Is.Zero, "and it is not counted as admitted");
            }
        }

        [Test]
        public void Revoking_a_record_that_came_back_empties_it_and_leaves_the_revocation_alone()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                _values.Plant(record.StorageKey, record.Save());
                _values.Plant(AuthorityKeys.RevocationKey(key), "as it was written");

                Assert.That(_store.Remove(key), Is.True);

                Assert.That(_values.Get(record.StorageKey), Is.Empty);
                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.EqualTo("as it was written"),
                    "a revocation is never changed: a changed one has a time a merge can lose to");
                Assert.That(_store.All().CameBack, Is.Zero);
            }
        }

        [Test]
        public void A_record_that_came_back_is_taken_away_by_the_next_write_and_by_nothing_else()
        {
            // The same rule as every prune here: opening, reading and serving a database change
            // nothing in it, and the next write this plugin makes anyway takes the record with it.
            using (var revoked = _authority.IssueClient("agent-fictitious"))
            using (var other = _authority.IssueClient("agent-invented"))
            {
                var key = CertificateDigest.Of(revoked);
                var record = AdmissionRecord.For(revoked, DateTimeOffset.UtcNow, new[] { "ops" });
                _store.Write(record);
                Assume.That(_store.Remove(key), Is.True);
                _values.Plant(record.StorageKey, record.Save());

                _store.Find(key);
                _store.All();
                _store.Count(out _);
                _store.Write(AdmissionRecord.For(other, DateTimeOffset.UtcNow, new[] { "ops" }));
                Assume.That(_values.Get(record.StorageKey), Is.Not.Empty, "nothing but a write prunes");

                Assert.That(_store.Remove(CertificateDigest.Of(other)), Is.True);

                Assert.That(_values.Get(record.StorageKey), Is.Null, "the record that came back is gone");
                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null,
                    "and its revocation stays, which is what refuses it if a stale copy brings it back again");
                Assert.That(_store.All().CameBack, Is.Zero);
            }
        }

        [Test]
        public void Pruning_the_records_that_came_back_says_what_it_took_and_leaves_the_rest()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            using (var client = _authority.IssueClient("agent-invented"))
            {
                var key = CertificateDigest.Of(certificate);
                var record = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" });
                var kept = AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" });
                _store.Write(kept);
                _values.Plant(record.StorageKey, record.Save());
                _values.Plant(AuthorityKeys.RevocationKey(key), "2999-01-01T00:00:00Z");
                _values.Plant(AuthorityKeys.ClientRecordKey(new string('e', 64)), string.Empty);
                _values.Plant(AuthorityKeys.RevocationKey(new string('e', 64)), "2999-01-01T00:00:00Z");

                var pruned = _store.PruneCameBack();

                Assert.That(pruned.Select(p => p.Key), Is.EqualTo(new[] { record.StorageKey }));
                Assert.That(pruned.Single().Value, Is.EqualTo(record.Save()), "so that a write that fails can put it back");
                Assert.That(_values.Get(kept.StorageKey), Is.Not.Null, "a client is not a record that came back");
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(new string('e', 64))), Is.Empty,
                    "and an emptied record is a revocation doing its work, not one that came back");
                Assert.That(_store.PruneCameBack(), Is.Empty, "and a second prune finds nothing");
            }
        }

        [Test]
        public void An_expired_revocation_is_pruned_by_the_next_revocation_and_by_nothing_else()
        {
            var expired = AuthorityKeys.RevocationKey(new string('a', 64));
            var current = AuthorityKeys.RevocationKey(new string('b', 64));
            var undated = AuthorityKeys.RevocationKey(new string('c', 64));
            var cameBack = AuthorityKeys.RevocationKey(new string('d', 64));
            _values.Plant(expired, "2020-01-01T00:00:00Z");
            _values.Plant(current, "2999-01-01T00:00:00Z");
            _values.Plant(undated, string.Empty);
            _values.Plant(cameBack, "2020-01-01T00:00:00Z");
            _values.Plant(AuthorityKeys.ClientRecordKey(new string('d', 64)), "a record that came back");

            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                // Reading and writing a record are not the write the prune rides on, so a database
                // that is only opened, served from and paired with is never changed by it.
                _store.Find(new string('a', 64));
                _store.All();
                _store.Count(out _);
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));
                Assume.That(_values.Get(expired), Is.Not.Null, "nothing but a revocation prunes");

                Assert.That(_store.Remove(CertificateDigest.Of(certificate)), Is.True);

                Assert.That(_values.Get(expired), Is.Null, "past its certificate's end it refuses nothing");
                Assert.That(_values.Get(current), Is.Not.Null, "before it, it still does");
                Assert.That(_values.Get(undated), Is.Not.Null, "one with no date is never pruned");
                Assert.That(_values.Get(cameBack), Is.Null,
                    "one whose record came back goes like any other, since its certificate is refused on its dates");
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(new string('d', 64))), Is.Null,
                    "and the record goes in the same write");
            }
        }

        [Test]
        public void The_revocation_just_written_is_not_pruned_by_its_own_write()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));

                Assert.That(_store.Remove(key, DateTimeOffset.UtcNow.AddYears(100)), Is.True);

                Assert.That(_values.Get(AuthorityKeys.RevocationKey(key)), Is.Not.Null);
            }
        }

        [Test]
        public void A_certificate_that_was_never_admitted_finds_nothing()
        {
            using (var admitted = _authority.IssueClient("agent-one"))
            using (var stranger = _authority.IssueClient("agent-two"))
            {
                _store.Write(AdmissionRecord.For(admitted, DateTimeOffset.UtcNow, new[] { "ops" }));

                Assert.That(_store.Find(CertificateDigest.Of(stranger)), Is.Null);
            }
        }

        [Test]
        public void A_record_that_cannot_be_read_admits_nobody_and_is_reported()
        {
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                var key = CertificateDigest.Of(certificate);
                _values.Plant(AuthorityKeys.ClientPrefix + key, "this is not a record");

                Assert.That(_store.Find(key), Is.Null);
                Assert.That(_log.Warnings, Is.Not.Empty);
            }
        }

        [Test]
        public void A_damaged_record_does_not_hide_the_others()
        {
            using (var good = _authority.IssueClient("agent-one"))
            using (var other = _authority.IssueClient("agent-two"))
            {
                _store.Write(AdmissionRecord.For(good, DateTimeOffset.UtcNow, new[] { "ops" }));
                _values.Plant(AuthorityKeys.ClientPrefix + CertificateDigest.Of(other), "rubbish");

                Assert.That(_store.Find(CertificateDigest.Of(good)), Is.Not.Null);
                Assert.That(_store.All().Records.Count, Is.EqualTo(1));
                Assert.That(_store.Count(out _), Is.EqualTo(2), "the count is of keys, which is what the tab shows");
            }
        }

        [Test]
        public void Nothing_that_belongs_to_somebody_else_is_read_as_a_record()
        {
            _values.Plant("AnotherPlugin.Something", "not ours");
            _values.Plant(AuthorityKeys.AuthorityKey(new string('d', 64)), "ours but not a record");

            Assert.That(_store.All().Records, Is.Empty);
            Assert.That(_store.Count(out _), Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Looking_up_nothing_finds_nothing(string admissionKey)
        {
            Assert.That(_store.Find(admissionKey), Is.Null);
            Assert.That(_store.Remove(admissionKey), Is.False);
        }

        [Test]
        public void A_store_that_throws_is_reported_and_admits_nobody()
        {
            var store = new AdmissionStore(new ThrowingKeyValueStore(), _log);

            Assert.That(store.All().Records, Is.Empty);
            Assert.That(store.Find(new string('a', 64)), Is.Null);
            Assert.That(_log.Errors, Is.Not.Empty);

            // And it says which of the two it is. Everything above is also true of a database
            // that has admitted nobody, and one of those two is a screen telling the operator
            // that nobody has access.
            Assert.That(store.All().Complete, Is.False);
        }

        [Test]
        public void A_revocation_that_does_not_land_is_reported_as_a_failure()
        {
            // The answer this gives is what the operator is shown. Telling somebody a client is
            // revoked when the write did not land is not noticed until that client connects again.
            using (var certificate = _authority.IssueClient("agent-fictitious"))
            {
                _store.Write(AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, new[] { "ops" }));

                _values.SwallowWrites = true;

                Assert.That(_store.Remove(CertificateDigest.Of(certificate)), Is.False);
            }
        }

        [Test]
        public void A_missing_store_is_refused_at_construction()
        {
            Assert.Throws<ArgumentNullException>(() => new AdmissionStore(null, _log));
        }
    }
}
