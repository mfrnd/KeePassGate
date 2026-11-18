using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The authority against a real KeePass database, saved to a real file and opened again.
    /// </summary>
    /// <remarks>
    /// The unit tests prove the logic against a store in memory. This proves the two things a
    /// store in memory cannot: that KeePass's database really carries what we put in it, and that
    /// it survives being written to a KDBX4 file and read back. Nothing is believed until it has
    /// been through the file.
    /// </remarks>
    [TestFixture]
    public sealed class AuthorityInRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private AuthorityService _service;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _service = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_new_database_carries_no_authority()
        {
            using (var reading = _service.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }
        }

        [Test]
        public void It_survives_being_saved_and_opened_again()
        {
            var created = _service.Create();
            Assume.That(created.Succeeded, Is.True, created.Message);

            string digest;
            byte[] gateKey;
            using (var before = _service.Read())
            {
                digest = before.Material.AuthorityDigest;
                gateKey = before.Material.SniKey;
            }

            _database.SaveCloseAndReopen();

            var afterReopening = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance);
            using (var after = afterReopening.Read())
            {
                Assert.That(after.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(after.Material.AuthorityDigest, Is.EqualTo(digest));
                Assert.That(after.Material.SniKey, Is.EqualTo(gateKey));
                Assert.That(after.Material.AuthorityKey.PrivateKey.IsPrivate, Is.True,
                    "A signing key that does not survive the file is no authority at all.");
                Assert.That(after.Material.HostKey.PrivateKey.IsPrivate, Is.True);
            }
        }

        [Test]
        public void The_authority_can_still_issue_after_the_round_trip()
        {
            _service.Create();
            _database.SaveCloseAndReopen();

            var afterReopening = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance);

            using (var reading = afterReopening.Read())
            using (var issued = new AuthorityIssuer().IssueHost(reading.Material.AuthorityKey).Read())
            {
                Assert.That(AnchorPin.Chains(issued, reading.Material.Authority), Is.True);
            }
        }

        [Test]
        public void Deleting_survives_the_file_as_well()
        {
            _service.Create();
            _database.Save();
            var digest = Digest();
            _service.Delete();

            _database.SaveCloseAndReopen();

            var store = new KdbxStore(_database.Database, GateVersion.Current, null);
            var afterReopening = new AuthorityService(store, NullGateLog.Instance);
            using (var reading = afterReopening.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }

            Assert.That(store.Read().InForce.Taken(StoreKinds.Retired), Does.Contain(digest),
                "the retirement is what keeps the deletion when a copy still holding the authority is merged in");
        }

        [Test]
        public void Deleting_deletes_the_admissions_of_the_authority_it_retires_from_the_file_and_keeps_every_revocation()
        {
            // `STORE.md` §615 and §627: the write that retires an authority deletes the admissions
            // it issued, with deletion records, and a revocation is never taken away.
            _service.Create();

            var admissions = new StoredAdmissions(_database.Database);
            var keys = new List<string>();
            using (var reading = _service.Read())
            {
                foreach (var name in new[] { "agent-fictitious", "agent-revoked-fictitious" })
                {
                    using (var client = new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, new[] { "ops" }).Read())
                    {
                        var record = AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }, StoredAdmissions.AnyIssuer);
                        admissions.Write(record);
                        keys.Add(record.AdmissionKey);
                    }
                }
            }

            Assume.That(admissions.Remove(keys[1]), Is.True);
            _database.Save();

            Assume.That(_service.Delete().Succeeded, Is.True);
            _database.SaveCloseAndReopen();

            var afterwards = new StoredAdmissions(_database.Database);
            Assert.That(keys.Where(key => afterwards.Holds(StoreKinds.Client, key)), Is.Empty);
            Assert.That(afterwards.Entries(StoreKinds.Revoked), Has.Count.EqualTo(1), "the revocation stays");
        }

        [Test]
        public void Nothing_of_ours_is_left_on_the_root_group()
        {
            _service.Create();

            Assert.That(_database.RootGroup.CustomData.Count(p => p.Key.StartsWith("KPG", StringComparison.Ordinal)), Is.Zero,
                "The group dialog copies that dictionary and writes the copy back over anything put there.");
        }

        /// <summary>The digest of the authority the database holds now.</summary>
        private string Digest()
        {
            using (var reading = _service.Read())
            {
                Assume.That(reading.IsPresent, "there has to be an authority to name");
                return reading.Material.AuthorityDigest;
            }
        }
    }
}
