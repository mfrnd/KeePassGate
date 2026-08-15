using System;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Tests.Fakes;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The three things an operator can do, and the two rules that run through all of them:
    /// nothing is destroyed implicitly, and nothing is believed without being read back.
    /// </summary>
    [TestFixture]
    public sealed class AuthorityServiceTests
    {
        private InMemoryKeyValueStore _values;
        private RecordingGateLog _log;
        private AuthorityService _service;

        [SetUp]
        public void SetUp()
        {
            _values = new InMemoryKeyValueStore();
            _log = new RecordingGateLog();
            _service = new AuthorityService(_values, _log);
        }

        [Test]
        public void Creating_puts_an_authority_in_the_database()
        {
            var result = _service.Create();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(reading.Material.Authority.HasPrivateKey, Is.True);
                Assert.That(reading.Material.Host.HasPrivateKey, Is.True);
                Assert.That(reading.Material.SniKey.Length, Is.EqualTo(AuthorityMaterial.SniKeyLength));
            }
        }

        [Test]
        public void The_message_names_the_authority_by_its_fingerprint()
        {
            var result = _service.Create();

            using (var reading = _service.Read())
            {
                Assert.That(result.Message,
                    Does.Contain(CertificateDigest.Shorten(reading.Material.AuthorityDigest)));
            }
        }

        [Test]
        public void Every_gate_key_is_a_new_one()
        {
            _service.Create();
            byte[] first;
            using (var reading = _service.Read())
            {
                first = reading.Material.SniKey;
            }

            _service.Recreate();
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.SniKey, Is.Not.EqualTo(first));
            }
        }

        [Test]
        public void Creating_over_an_authority_is_refused_rather_than_replacing_it()
        {
            _service.Create();
            string digest;
            using (var reading = _service.Read())
            {
                digest = reading.Material.AuthorityDigest;
            }

            var second = _service.Create();

            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Message, Does.Contain("already has an authority"));
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(digest), "Nothing may be replaced silently.");
            }
        }

        [Test]
        public void Creating_over_damage_is_refused_and_says_what_to_do_instead()
        {
            _values.Plant(AuthorityKeys.Authority, "rubbish");

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("Recreate"));
        }

        [Test]
        public void Recreating_replaces_the_authority()
        {
            _service.Create();
            string first;
            using (var reading = _service.Read())
            {
                first = reading.Material.AuthorityDigest;
            }

            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.Not.EqualTo(first));
            }
        }

        [Test]
        public void Recreating_repairs_damage()
        {
            _values.Plant(AuthorityKeys.Authority, "rubbish");

            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present));
            }
        }

        [Test]
        public void Recreating_revokes_every_client_and_says_how_many()
        {
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            _values.Plant(AuthorityKeys.ClientPrefix + "bbb", "record");

            var result = _service.Recreate();

            Assert.That(result.RecordsRemoved, Is.EqualTo(2));
            Assert.That(result.Message, Does.Contain("2 clients lost their admission"));
            using (var reading = _service.Read())
            {
                Assert.That(reading.ClientRecordCount, Is.Zero);
            }
        }

        [Test]
        public void One_client_is_counted_in_words_that_read_properly()
        {
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");

            var result = _service.Recreate();

            Assert.That(result.Message, Does.Contain("One client lost its admission"));
        }

        [Test]
        public void Recreating_with_no_clients_says_nothing_about_clients()
        {
            _service.Create();

            var result = _service.Recreate();

            Assert.That(result.Message, Does.Not.Contain("admission"));
        }

        [Test]
        public void Deleting_leaves_the_database_with_nothing_of_ours()
        {
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            _values.Plant("SomebodyElse", "left alone");

            var result = _service.Delete();

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.RecordsRemoved, Is.EqualTo(1));
            Assert.That(_values.Keys, Is.EqualTo(new[] { "SomebodyElse" }));
        }

        [Test]
        public void Deleting_nothing_is_not_an_error()
        {
            var result = _service.Delete();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.RecordsRemoved, Is.Zero);
        }

        [Test]
        public void A_write_that_vanishes_is_reported_rather_than_believed()
        {
            // What a stale dialog snapshot does to a write: it is accepted and it is not there.
            _values.SwallowWrites = true;

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("could not be read back"));
            Assert.That(result.Message, Does.Contain("nothing was stored"));
            Assert.That(_log.Warnings, Is.Not.Empty);
        }

        [Test]
        public void A_write_that_lands_unusable_is_not_reported_as_nothing_having_happened()
        {
            // The other half of a failed read back, and the one the message used to get wrong.
            // The value is in the database and cannot be read, so the next press of Create is
            // answered with "recreate it": a refusal saying nothing changed contradicts the very
            // next thing the tab does.
            _values.DamageWritesTo = "rubbish";

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("now holds one that cannot be used"));
            Assert.That(result.Message, Does.Not.Contain("nothing"));

            _values.DamageWritesTo = null;
            var second = _service.Create();
            Assert.That(second.Message, Does.Contain("cannot be read"),
                "which is the answer the refusal above has to be consistent with");
        }

        [Test]
        public void A_recreation_that_fails_after_the_clearing_says_what_it_already_took_away()
        {
            // The old authority and every admission record are taken away before the replacement
            // is written, so a failure from there on has already changed the database. Reported as
            // a refusal with nothing else said, the operator walks away from a database that
            // serves nobody.
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            _values.DamageWritesTo = "rubbish";

            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("now holds one that cannot be used"));
            Assert.That(result.Message, Does.Contain("may be gone as well"));
        }

        [Test]
        public void A_creation_that_fails_says_nothing_about_an_old_authority()
        {
            // Creating clears nothing, so the sentence recreation adds would be a lie here.
            _values.ThrowOnWrite = new InvalidOperationException("the database said no");

            var result = _service.Create();

            Assert.That(result.Message, Does.Not.Contain("may be gone as well"));
        }

        [Test]
        public void A_recreation_that_did_not_land_is_not_reported_as_having_worked()
        {
            // A swallowed write leaves the authority that was already there, and reading back only
            // far enough to find one present is satisfied by it: the operator was told the
            // recreation had worked, and told how many clients it had revoked, while the old
            // authority and every one of those clients were untouched.
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            string before;
            using (var reading = _service.Read())
            {
                before = reading.Material.AuthorityDigest;
            }

            _values.SwallowWrites = true;
            var result = _service.Recreate();

            Assert.That(result.Succeeded, Is.False, result.Message);

            _values.SwallowWrites = false;
            using (var reading = _service.Read())
            {
                Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(before),
                    "the refusal has to be about the authority that is really there");
            }
        }

        [Test]
        public void A_deletion_that_does_not_land_does_not_claim_the_database_is_untouched()
        {
            // The admission records are removed before the authority is read back, so a delete
            // that comes apart halfway is exactly the case being reported. Saying nothing was
            // changed on disk was a guess, and the wrong one.
            _service.Create();
            _values.Plant(AuthorityKeys.ClientPrefix + "aaa", "record");
            _values.SwallowWrites = true;

            var result = _service.Delete();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("still in the database"));
            Assert.That(result.Message, Does.Not.Contain("Nothing was changed"));
        }

        [Test]
        public void A_store_that_throws_is_reported_rather_than_thrown()
        {
            _values.ThrowOnWrite = new InvalidOperationException("the database said no");

            var result = _service.Create();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Message, Does.Contain("the database said no"));
            Assert.That(_log.Errors, Is.Not.Empty);
        }

        [Test]
        public void Reading_a_store_that_throws_reports_damage_rather_than_absence()
        {
            var service = new AuthorityService(new ThrowingKeyValueStore(), _log);

            using (var reading = service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Damaged));
                Assert.That(reading.Problems, Is.Not.Empty);
            }
        }

        [Test]
        public void The_log_never_carries_the_material_itself()
        {
            _service.Create();
            _service.Recreate();
            _service.Delete();

            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }

            foreach (var entry in _log.Entries)
            {
                Assert.That(entry.Message, Does.Not.Contain("MII"), "A certificate blob has no business in a log.");
                Assert.That(entry.Message.Length, Is.LessThan(200));
            }
        }

        [Test]
        public void A_missing_store_is_refused_at_construction()
        {
            Assert.Throws<ArgumentNullException>(() => new AuthorityService(null, _log));
        }

        private sealed class ThrowingKeyValueStore : IKeyValueStore
        {
            public string Get(string key)
            {
                throw new InvalidOperationException("no");
            }

            public void Set(string key, string value)
            {
                throw new InvalidOperationException("no");
            }

            public void Remove(string key)
            {
                throw new InvalidOperationException("no");
            }

            public System.Collections.Generic.IEnumerable<string> Keys
            {
                get { throw new InvalidOperationException("no"); }
            }
        }
    }
}
