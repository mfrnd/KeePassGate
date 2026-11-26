using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Admissions against a real KeePass database, written into its store, saved to a real KDBX4
    /// file and read back out of it, `STORE.md` §615.
    /// </summary>
    /// <remarks>
    /// The unit tests prove an admission survives a store held in memory. Only this proves it
    /// survives KeePass: an admission is an entry of the plugin's group whose content is a string
    /// KeePass writes into the file and reads back, which is where a format loses things.
    /// </remarks>
    [TestFixture]
    public sealed class AdmissionInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private AuthorityService _authority;
        private StoredAdmissions _admissions;
        private X509Certificate2 _client;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();

            _authority = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance);
            _admissions = new StoredAdmissions(_database.Database);

            var created = _authority.Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            using (var reading = _authority.Read())
            {
                _client = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, "agent-fictitious", new[] { "ops" }).Read();
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
        public void An_admission_is_an_entry_of_the_plugin_s_group_with_its_content_unprotected_and_unmarked()
        {
            var record = AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer);
            _admissions.Write(record);

            var group = _database.RootGroup.Groups.Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
            var admission = group.Entries.Single(entry => entry.Strings.ReadSafe("KPG.Kind") == "client");

            Assert.That(admission.Strings.Get("KPG.Data").IsProtected, Is.False, "an admission holds no secret");
            Assert.That(admission.Strings.ReadSafe("KPG.Data"), Does.Contain(record.AdmissionKey));
            Assert.That(admission.Strings.ReadSafe(KeePassLib.PwDefs.TitleField),
                Is.EqualTo("KPG client " + admission.Uuid.ToHexString().Substring(0, 8).ToLowerInvariant()));
            Assert.That(admission.Strings.ReadSafe(KeePassLib.PwDefs.NotesField), Does.Not.StartWith("Do not delete"),
                "and it carries no mark, holding no store key");
            Assert.That(admission.Strings.Get(KeePassLib.PwDefs.PasswordField).IsProtected, Is.True);
            Assert.That(admission.AutoType.Enabled, Is.False);
            Assert.That(_database.Database.CustomData.Count, Is.Zero, "and nothing of it is kept in the custom data");
        }

        [Test]
        public void An_admission_and_a_profile_name_in_the_store_leave_the_file_KDBX_4_0()
        {
            // What the store costs, written down where it cannot be forgotten. KeePass stamps each
            // key of the database's own custom data with a modification time, and a file carrying
            // such a stamp is KDBX 4.1: `PwDatabase.GetMinKdbxVersion` holds so. The authority, the
            // admissions and the profile names are entries of the plugin's store, which need
            // nothing above 4.0.
            using (var untouched = TemporaryDatabase.New())
            {
                untouched.Save();
                Assert.That(FileVersionOf(untouched.Path), Is.EqualTo(0x00040000),
                    "a database with nothing of this plugin's in it is a 4.0 file");
            }

            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            _database.Save();

            Assume.That(_database.Database.CustomData.Count, Is.Zero, "no custom data of the plugin's");
            Assert.That(FileVersionOf(_database.Path), Is.EqualTo(0x00040000),
                "an authority and an admission in the store leave it a 4.0 file");

            StoreSeeds.Name(_admissions.Store, "ops");
            _database.Save();

            Assert.That(_database.Database.CustomData.Count, Is.Zero, "the name is no custom data either");
            Assert.That(FileVersionOf(_database.Path), Is.EqualTo(0x00040000), "and a profile name beside them leaves it 4.0");
        }

        [Test]
        public void A_pairing_leaves_the_file_KDBX_4_0_whatever_it_names_and_whatever_is_refused()
        {
            // Measured over the product's own pairing write and KeePass's save, each in a database
            // of its own: a client paired for no profile; a client paired for a profile, which the
            // write adds to the list as an entry of the store; and a client paired for no profile
            // with a standing answer to its renewal written beside it. The store's entries need
            // nothing above 4.0, `STORE.md` §612, and the answer is a version of the store, about
            // the client, `STORE.md` §618, where a key of the database's own custom data carried a
            // modification time, a 4.1 field.
            var none = VersionAfterPairing(Array.Empty<string>(), false);
            var profile = VersionAfterPairing(new[] { "ops" }, false);
            var answer = VersionAfterPairing(Array.Empty<string>(), true);
            TestContext.Out.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "paired for no profile {0:x8}, for a profile {1:x8}, for no profile beside a standing answer {2:x8}",
                none,
                profile,
                answer));

            Assert.That(none, Is.EqualTo(0x00040000), "a client paired for no profile leaves a 4.0 file");
            Assert.That(profile, Is.EqualTo(0x00040000), "a client paired for a profile the list then names, a 4.0 file");
            Assert.That(answer, Is.EqualTo(0x00040000), "and a standing answer beside a client paired for none, a 4.0 file");
        }

        /// <summary>
        /// The KDBX version of a database with an authority once a client is paired for these
        /// profiles through the product's own write and KeePass's save, and, where asked, once a
        /// standing answer to its renewal is written beside it and the database saved again.
        /// </summary>
        private static int VersionAfterPairing(string[] profiles, bool withAnswer)
        {
            using (var database = TemporaryDatabase.New())
            {
                var authority = DatabaseAuthority.Of(database.Database, NullGateLog.Instance);
                Assume.That(authority.Create().Succeeded, Is.True);
                database.Save();

                AdmissionRecord admission;
                using (var reading = authority.Read())
                using (var certificate = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, "agent-fictitious", profiles).Read())
                {
                    admission = AdmissionRecord.For(certificate, DateTimeOffset.UtcNow, profiles, reading.Material.AuthorityDigest);
                }

                var written = new DatabaseWriter(CurrentThread.Instance, saved => saved.Save(null), null, null, null)
                    .WriteAdmission(database.Database, admission);
                Assert.That(written.Ok, Is.True, "the pairing was written: " + written.Reason);

                if (withAnswer)
                {
                    new StoredAdmissions(database.Database).Refuse(admission.ClientKey, RenewalRefusal.Forever());
                    Assume.That(database.Database.CustomData.Count, Is.Zero, "no custom data of the plugin's");
                    database.Save();
                }

                return FileVersionOf(database.Path);
            }
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
        public void It_survives_being_saved_and_opened_again()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops", "readonly" }, StoredAdmissions.AnyIssuer));

            _database.SaveCloseAndReopen();

            var afterwards = new StoredAdmissions(_database.Database);
            var record = afterwards.Find(CertificateDigest.Of(_client));

            Assert.That(record, Is.Not.Null, "a record that does not survive a save admits nobody tomorrow");
            Assert.That(record.Subject, Is.EqualTo("CN=agent-fictitious"));
            Assert.That(record.Profiles, Is.EqualTo(new[] { "ops", "readonly" }));
            using (var carried = record.Certificate())
            {
                Assert.That(carried.RawData, Is.EqualTo(_client.RawData));
            }
        }

        [Test]
        public void The_certificate_in_it_still_reads_after_the_round_trip()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, null, StoredAdmissions.AnyIssuer));
            _database.SaveCloseAndReopen();

            var afterwards = new StoredAdmissions(_database.Database);

            using (var certificate = afterwards.All().Records.Single().Certificate())
            {
                Assert.That(certificate.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(certificate.NotAfter, Is.EqualTo(_client.NotAfter));
            }
        }

        [Test]
        public void Revoking_survives_the_file_as_well()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
            _database.Save();

            _admissions.Remove(CertificateDigest.Of(_client));
            _database.SaveCloseAndReopen();

            var afterwards = new StoredAdmissions(_database.Database);
            Assert.That(afterwards.Find(CertificateDigest.Of(_client)), Is.Null);
        }

        [Test]
        public void Recreating_the_authority_takes_the_records_with_it()
        {
            _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

            var result = _authority.Recreate();

            Assert.That(result.Succeeded, Is.True, result.Message);
            var listed = _admissions.All();
            Assert.That(listed.Records, Is.Empty, "an admission issued under an authority that is gone admits nobody");
            Assert.That(listed.Waiting, Is.Zero, "and the write that retired the authority deleted it");
        }

        [Test]
        public void Two_clients_are_two_entries_so_a_replicated_database_can_merge_them()
        {
            using (var reading = _authority.Read())
            using (var second = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, "agent-two", null).Read())
            {
                _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));
                _admissions.Write(AdmissionRecord.For(second, DateTimeOffset.UtcNow, null, StoredAdmissions.AnyIssuer));

                _database.SaveCloseAndReopen();

                var afterwards = new StoredAdmissions(_database.Database);
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

                    _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

                    Assert.That(target.Admission(CertificateDigest.Of(_client)), Is.Not.Null);

                    _admissions.Remove(CertificateDigest.Of(_client));

                    Assert.That(target.Admission(CertificateDigest.Of(_client)), Is.Null);
                }
            }
        }

        [Test]
        public void A_target_held_across_a_recreated_authority_admits_nobody()
        {
            // A renewal holds its connection's target while the operator decides, and asks it
            // again for the record afterwards. Recreation takes every record with it, and one that
            // comes back, by a merge or by a pairing that landed while the authority went, chains
            // to the authority this target serves, which the database has retired.
            var databases = new[] { _database.Database }.ToList();
            using (var targets = new OpenDatabaseTargets(delegate { return databases; }, NullGateLog.Instance))
            {
                var record = AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer);
                _admissions.Write(record);

                using (var held = targets.Current().Single())
                {
                    Assume.That(held.Admission(CertificateDigest.Of(_client)), Is.Not.Null);

                    Assume.That(_authority.Recreate().Succeeded, Is.True);
                    _admissions.Write(record);
                    Assume.That(_admissions.Find(CertificateDigest.Of(_client)), Is.Not.Null);

                    Assert.That(held.Admission(CertificateDigest.Of(_client)), Is.Null);
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
                _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

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
        public void The_gate_reads_the_store_a_locked_and_unlocked_database_came_back_with()
        {
            // The lock cycle a cached target survives. Nothing prunes the cache but a connection,
            // so a workspace locked and unlocked with none arriving in between leaves the target in
            // place, and the authority it is compared against is byte for byte the same. A target
            // holding a store built at construction would answer from the tree that was replaced,
            // and a revoked client would go on being served for the rest of the session.
            var databases = new[] { _database.Database }.ToList();
            using (var targets = new OpenDatabaseTargets(delegate { return databases; }, NullGateLog.Instance))
            {
                _admissions.Write(AdmissionRecord.For(_client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer));

                using (var before = targets.Current().Single())
                {
                    Assume.That(before.Admission(CertificateDigest.Of(_client)), Is.Not.Null);
                }

                _database.LockAndUnlock();

                var live = new StoredAdmissions(_database.Database);
                Assume.That(live.Remove(CertificateDigest.Of(_client)), Is.True);

                using (var afterwards = targets.Current().Single())
                {
                    Assert.That(afterwards.Admission(CertificateDigest.Of(_client)), Is.Null,
                        "the client was revoked in the store the database came back with");
                }
            }
        }
    }
}
