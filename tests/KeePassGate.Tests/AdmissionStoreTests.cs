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

            // The emptied records the three revocations were written beside.
            _values.Plant(AuthorityKeys.ClientRecordKey(new string('a', 64)), string.Empty);
            _values.Plant(AuthorityKeys.ClientRecordKey(new string('b', 64)), string.Empty);
            _values.Plant(AuthorityKeys.ClientRecordKey(new string('c', 64)), string.Empty);

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

                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(new string('a', 64))), Is.Null,
                    "an emptied record goes with its revocation, since past that end it refuses nothing either");
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(new string('b', 64))), Is.Empty,
                    "and stays while its revocation does");
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(new string('c', 64))), Is.Empty,
                    "which for one with no date is always");
            }
        }

        [Test]
        public void The_record_of_an_expired_certificate_is_pruned_by_the_next_revocation_and_by_nothing_else()
        {
            using (var ended = TestAuthority.IssueExpiredClient("agent-nonexistent", "ops"))
            using (var current = _authority.IssueClient("agent-invented", "ops"))
            using (var revoked = _authority.IssueClient("agent-fictitious", "ops"))
            {
                var expired = AdmissionRecord.For(ended, DateTimeOffset.UtcNow.AddDays(-7), new[] { "ops" });
                var kept = AdmissionRecord.For(current, DateTimeOffset.UtcNow, new[] { "ops" });
                _store.Write(expired);
                _store.Write(kept);
                _store.Write(AdmissionRecord.For(revoked, DateTimeOffset.UtcNow, new[] { "ops" }));
                var unreadable = AuthorityKeys.ClientRecordKey(new string('f', 64));
                _values.Plant(unreadable, "a record nobody can read");

                // Reading is not the write the prune rides on, and nor is a record being written:
                // a database that is only opened, served from and paired with by hand is never
                // changed by it.
                _store.Find(expired.AdmissionKey);
                _store.All();
                _store.Count(out _);
                Assume.That(_values.Get(expired.StorageKey), Is.Not.Null, "nothing but a write prunes");

                Assert.That(_store.Remove(CertificateDigest.Of(revoked)), Is.True);

                Assert.That(_values.Get(expired.StorageKey), Is.Null,
                    "past its certificate's end the gate refuses it on its dates, so the record has nothing left to say");
                Assert.That(_values.Get(kept.StorageKey), Is.Not.Null, "before that end it stays");
                Assert.That(_values.Get(unreadable), Is.Not.Null,
                    "and a record that cannot be read gives no date, so it stays too");
            }
        }

        [Test]
        public void Pruning_the_records_of_expired_certificates_says_what_it_took_and_leaves_the_rest()
        {
            using (var ended = TestAuthority.IssueExpiredClient("agent-nonexistent", "ops"))
            using (var current = _authority.IssueClient("agent-invented", "ops"))
            {
                var expired = AdmissionRecord.For(ended, DateTimeOffset.UtcNow.AddDays(-7), new[] { "ops" });
                var kept = AdmissionRecord.For(current, DateTimeOffset.UtcNow, new[] { "ops" });
                _store.Write(expired);
                _store.Write(kept);
                _values.Plant(AuthorityKeys.ClientRecordKey(new string('e', 64)), string.Empty);

                var pruned = _store.PruneExpired(DateTimeOffset.UtcNow);

                Assert.That(pruned.Select(p => p.Key), Is.EqualTo(new[] { expired.StorageKey }));
                Assert.That(pruned.Single().Value, Is.EqualTo(expired.Save()), "so that a write that fails can put it back");
                Assert.That(_values.Get(kept.StorageKey), Is.Not.Null);
                Assert.That(_values.Get(AuthorityKeys.ClientRecordKey(new string('e', 64))), Is.Empty,
                    "an emptied record gives no date, and goes with its revocation instead");
                Assert.That(_store.PruneExpired(DateTimeOffset.UtcNow), Is.Empty, "and a second prune finds nothing");
            }
        }

        [Test]
        public void What_a_prune_takes_on_this_machines_clock_is_named_in_the_log()
        {
            // A wrong clock refuses a client that setting it right admits again; a key taken out
            // of the file comes back by nothing, so the log is where one lost that way is found.
            using (var ended = TestAuthority.IssueExpiredClient("agent-nonexistent", "ops"))
            {
                var expired = AdmissionRecord.For(ended, DateTimeOffset.UtcNow.AddDays(-7), new[] { "ops" });
                _store.Write(expired);

                Assume.That(_store.PruneExpired(DateTimeOffset.UtcNow), Is.Not.Empty);

                var line = _log.Entries.Single(entry => entry.Message.Contains("by this machine's clock"));
                Assert.That(line.Level, Is.EqualTo("INFO").IgnoreCase);
                Assert.That(line.Message, Does.Contain(CertificateDigest.Shorten(expired.AdmissionKey)));
            }
        }

        [Test]
        public void An_expired_record_a_stale_copy_brings_back_is_taken_away_again()
        {
            using (var ended = TestAuthority.IssueExpiredClient("agent-nonexistent", "ops"))
            using (var revoked = _authority.IssueClient("agent-fictitious", "ops"))
            {
                var expired = AdmissionRecord.For(ended, DateTimeOffset.UtcNow.AddDays(-7), new[] { "ops" });
                _store.Write(expired);
                Assume.That(_store.PruneExpired(DateTimeOffset.UtcNow), Is.Not.Empty);

                // What a merge with a copy from before the prune does: the key comes back as it
                // was. It admits nobody meanwhile, because the gate reads its certificate's dates.
                _values.Plant(expired.StorageKey, expired.Save());
                _store.Write(AdmissionRecord.For(revoked, DateTimeOffset.UtcNow, new[] { "ops" }));

                Assert.That(_store.Remove(CertificateDigest.Of(revoked)), Is.True);

                Assert.That(_values.Get(expired.StorageKey), Is.Null, "the next write takes it away again");
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
        public void A_damaged_record_is_named_by_its_key_so_that_it_can_be_taken_away()
        {
            // K024: listed nowhere, it was counted and warned about and could not be removed short
            // of recreating the authority. Named, the clients page gives it a row and an X.
            using (var good = _authority.IssueClient("agent-one"))
            using (var other = _authority.IssueClient("agent-two"))
            {
                var damaged = CertificateDigest.Of(other);
                _store.Write(AdmissionRecord.For(good, DateTimeOffset.UtcNow, new[] { "ops" }));
                _values.Plant(AuthorityKeys.ClientPrefix + damaged, "rubbish");

                Assert.That(_store.All().Unreadable, Is.EqualTo(new[] { damaged }));

                Assert.That(_store.Remove(damaged), Is.True, "the X that removes it is the ordinary revocation");

                var after = _store.All();
                Assert.That(after.Unreadable, Is.Empty);
                Assert.That(after.CameBack, Is.Zero);
                Assert.That(after.Records.Count, Is.EqualTo(1));
                Assert.That(_store.Count(out _), Is.EqualTo(1));
                Assert.That(_values.Get(AuthorityKeys.RevocationKey(damaged)), Is.Empty,
                    "with no date to be pruned by");
            }
        }

        [Test]
        public void A_damaged_record_beside_its_revocation_came_back_rather_than_being_listed()
        {
            // The revocation is asked about first, as it is for a readable record: what refuses it
            // is the revocation, and the next write this plugin makes takes it away.
            var damaged = new string('f', 64);
            _values.Plant(AuthorityKeys.ClientRecordKey(damaged), "rubbish");
            _values.Plant(AuthorityKeys.RevocationKey(damaged), string.Empty);

            var listing = _store.All();

            Assert.That(listing.Unreadable, Is.Empty);
            Assert.That(listing.CameBack, Is.EqualTo(1));
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
