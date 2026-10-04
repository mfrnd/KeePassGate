using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Admission records against a real KeePass database, written to a real KDBX4 file and read
    /// back out of it.
    /// </summary>
    /// <remarks>
    /// The unit tests prove the record survives a dictionary. Only this proves it survives KeePass:
    /// a record is a multi-line string in custom data, which is the part of a KDBX most likely to
    /// come back with different line endings or not at all.
    /// </remarks>
    [TestFixture]
    public sealed class AdmissionInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private AuthorityService _authority;
        private AdmissionStore _admissions;
        private X509Certificate2 _client;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            var store = new CustomDataStore(_database.Database);
            _authority = new AuthorityService(store, NullGateLog.Instance);
            _admissions = new AdmissionStore(store, NullGateLog.Instance);

            var created = _authority.Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            using (var reading = _authority.Read())
            {
                _client = new AuthorityIssuer().IssueClient(reading.Material.Authority, "agent-fictitious", new[] { "ops" });
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Guarded, because NUnit runs this after a `SetUp` that threw. Without it a failure to
            // create the authority is reported as a null reference here and the message saying what
            // really went wrong is lost, along with the temporary directory this promises to delete.
            if (_client != null)
            {
                _client.Reset();
                _client = null;
            }

            if (_database != null)
            {
                _database.Dispose();
                _database = null;
            }
        }

        [Test]
        public void A_record_lands_under_its_own_key_in_the_databases_own_custom_data()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));

            var key = AuthorityKeys.ClientPrefix + CertificateDigest.Of(_client);

            Assert.That(_database.Database.CustomData.Exists(key), Is.True,
                "records live in the one dictionary KeePass merges key by key");
            Assert.That(_database.RootGroup.CustomData.Exists(key), Is.False,
                "and no copy is left where a merge would take them all from one replica");
        }

        [Test]
        public void Anything_this_plugin_stores_takes_the_file_from_KDBX_4_0_to_4_1()
        {
            // What the dictionary costs, written down where it cannot be forgotten. KeePass stamps each
            // key of the database's own custom data with a modification time, and a file carrying
            // one of those stamps is KDBX 4.1: `PwDatabase.GetMinKdbxVersion` says so. A database
            // this plugin has been used on is therefore a 4.1 file, and nothing on the tab says it
            // happened. That is a format and not a gate: KeePass 2.47 opens such a file rather than
            // refusing it, and saves it back as 4.0 without the stamps.
            //
            // Written about the authority rather than about a record, because the authority is
            // what comes first: creating one is enough, and every database that is served has one.
            // A fixture that measured the record alone would be inconclusive, because the authority
            // it is stored beside has already taken the file there.
            using (var untouched = TemporaryDatabase.New())
            {
                untouched.Save();
                Assert.That(FileVersionOf(untouched.Path), Is.EqualTo(0x00040000),
                    "a database with nothing of this plugin's in it is a 4.0 file");
            }

            _database.Save();
            Assert.That(FileVersionOf(_database.Path), Is.EqualTo(0x00040001),
                "and the authority alone is what takes it to 4.1");

            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));
            _database.Save();

            Assert.That(FileVersionOf(_database.Path), Is.EqualTo(0x00040001),
                "a record keeps it there rather than moving it again");
        }

        /// <summary>The KDBX version out of the file header: two signatures, then minor and major.</summary>
        private static int FileVersionOf(string path)
        {
            var header = new byte[12];
            using (var file = System.IO.File.OpenRead(path))
            {
                var read = file.Read(header, 0, header.Length);
                Assume.That(read, Is.EqualTo(header.Length), "the header is shorter than a KDBX header");
            }

            var minor = BitConverter.ToUInt16(header, 8);
            var major = BitConverter.ToUInt16(header, 10);
            return (major << 16) | minor;
        }

        [Test]
        public void The_authority_sits_beside_them_under_its_own_key()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));

            Assert.That(
                _database.Database.CustomData.Count(p => AuthorityKeys.FingerprintInAuthority(p.Key) != null),
                Is.EqualTo(1),
                "the authority is one key, so a merge cannot take its parts from different replicas");
            Assert.That(_database.RootGroup.CustomData.Count, Is.Zero,
                "and the root group carries nothing of this plugin's at all");
        }

        [Test]
        public void It_survives_being_saved_and_opened_again()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops", "readonly" }));

            _database.SaveCloseAndReopen();

            var afterwards = new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance);
            var record = afterwards.Find(CertificateDigest.Of(_client));

            Assert.That(record, Is.Not.Null, "a record that does not survive a save admits nobody tomorrow");
            Assert.That(record.Subject, Is.EqualTo("CN=agent-fictitious"));
            Assert.That(record.Profiles, Is.EqualTo(new[] { "ops", "readonly" }));
            Assert.That(record.CertificateDer, Is.EqualTo(_client.RawData));
        }

        [Test]
        public void The_certificate_in_it_still_reads_after_the_round_trip()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, null));
            _database.SaveCloseAndReopen();

            var afterwards = new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance);

            using (var certificate = afterwards.All().Records.Single().Certificate())
            {
                Assert.That(certificate.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(certificate.NotAfter, Is.EqualTo(_client.NotAfter));
            }
        }

        [Test]
        public void Revoking_survives_the_file_as_well()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));
            _database.Save();

            _admissions.Remove(CertificateDigest.Of(_client));
            _database.SaveCloseAndReopen();

            var afterwards = new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance);
            Assert.That(afterwards.Find(CertificateDigest.Of(_client)), Is.Null);
        }

        [Test]
        public void Recreating_the_authority_takes_the_records_with_it()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));

            var result = _authority.Recreate();

            Assert.That(result.RecordsRemoved, Is.EqualTo(1));
            Assert.That(_admissions.Count(out _), Is.Zero, "a record issued under an authority that is gone admits nobody");
        }

        [Test]
        public void Two_clients_are_two_keys_so_a_replicated_database_can_merge_them()
        {
            using (var reading = _authority.Read())
            using (var second = new AuthorityIssuer().IssueClient(reading.Material.Authority, "agent-two", null))
            {
                _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));
                _admissions.Write(AdmissionRecord.For(second, DateTimeOffset.UtcNow, null));

                _database.SaveCloseAndReopen();

                var afterwards = new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance);
                Assert.That(afterwards.All().Records.Select(r => r.Subject).OrderBy(s => s),
                    Is.EqualTo(new[] { "CN=agent-fictitious", "CN=agent-two" }));
            }
        }

        [Test]
        public void The_gate_sees_a_record_written_after_it_started_serving()
        {
            // The target caches certificates and must not cache records: pairing writes one while
            // the gate is already running, and revocation deletes one.
            var databases = new[] { _database.Database }.ToList();
            using (var targets = new OpenDatabaseTargets(delegate { return databases; }, NullGateLog.Instance))
            {
                // `Current` retains, so the target is given back. Its sibling below already
                // does this and this one did not: the count never reached zero, so a perfectly
                // readable database's authority material was never wiped.
                using (var target = targets.Current().Single())
                {
                    Assume.That(target.Admission(CertificateDigest.Of(_client)), Is.Null);

                    _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));

                    Assert.That(target.Admission(CertificateDigest.Of(_client)), Is.Not.Null);

                    _admissions.Remove(CertificateDigest.Of(_client));

                    Assert.That(target.Admission(CertificateDigest.Of(_client)), Is.Null);
                }
            }
        }

        [Test]
        public void A_client_whose_database_closed_under_its_connection_is_told_it_is_locked()
        {
            // KeePass empties the database before it says why, so a connection resolved a moment
            // earlier holds a target whose records are gone. Answered as no record, an admitted
            // and unrevoked client was told `401 unpaired`, which says to pair again, and the
            // reference client's `revoke` deleted its identity.
            var databases = new[] { _database.Database }.ToList();
            using (var targets = new OpenDatabaseTargets(delegate { return databases; }, NullGateLog.Instance))
            {
                _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));

                using (var held = targets.Current().Single())
                {
                    Assume.That(KeePassGate.Pipeline.ClientCredentials.For(_client, held, NullGateLog.Instance).IsPaired, Is.True);

                    _database.Database.Close();

                    var identity = KeePassGate.Pipeline.ClientCredentials.For(_client, held, NullGateLog.Instance);

                    Assert.That(identity.IsPaired, Is.False);
                    Assert.That(identity.IsLocked, Is.True, "answered as the lock it is");
                    Assert.Throws<DatabaseShut>(() => held.Admission(CertificateDigest.Of(_client)));
                }
            }
        }

        [Test]
        public void The_gate_reads_the_custom_data_a_locked_and_unlocked_database_came_back_with()
        {
            // The lock cycle a cached target survives. Nothing prunes the cache but a connection,
            // so a workspace locked and unlocked with none arriving in between leaves the target in
            // place, and the authority it is compared against is byte for byte the same. A target
            // holding a store built at construction would answer from the dictionary that was replaced,
            // and a revoked client would go on being served for the rest of the session.
            var databases = new[] { _database.Database }.ToList();
            using (var targets = new OpenDatabaseTargets(delegate { return databases; }, NullGateLog.Instance))
            {
                _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }));

                using (var before = targets.Current().Single())
                {
                    Assume.That(before.Admission(CertificateDigest.Of(_client)), Is.Not.Null);
                }

                _database.LockAndUnlock();

                var live = new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance);
                Assume.That(live.Remove(CertificateDigest.Of(_client)), Is.True);

                using (var afterwards = targets.Current().Single())
                {
                    Assert.That(afterwards.Admission(CertificateDigest.Of(_client)), Is.Null,
                        "the record was revoked in the custom data the database came back with");
                }
            }
        }
    }
}
