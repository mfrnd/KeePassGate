using System;
using System.Linq;
using System.Reflection;
using KeePassLib.Collections;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The authority against a real KeePass database, saved to a real file and opened again.
    /// </summary>
    /// <remarks>
    /// The unit tests prove the logic against a dictionary. This proves the two things a
    /// dictionary cannot: that KeePass's custom data really carries what we put in it, and that it
    /// survives being written to a KDBX4 file and read back. Nothing is believed until it has been
    /// through the file.
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
            _service = new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance);
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
        public void An_authority_lands_in_the_databases_own_custom_data()
        {
            _service.Create();

            Assert.That(_database.Database.CustomData.Exists(AuthorityKeys.Authority), Is.True,
                "one key, so a merge cannot take the three parts from different replicas");
            Assert.That(_database.RootGroup.CustomData.Exists(AuthorityKeys.Authority), Is.False,
                "and nothing of this plugin's is left where the group dialog's snapshot reaches");
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

            var afterReopening = new AuthorityService(
                new CustomDataStore(_database.Database), NullGateLog.Instance);
            using (var after = afterReopening.Read())
            {
                Assert.That(after.Presence, Is.EqualTo(AuthorityPresence.Present));
                Assert.That(after.Material.AuthorityDigest, Is.EqualTo(digest));
                Assert.That(after.Material.SniKey, Is.EqualTo(gateKey));
                Assert.That(after.Material.Authority.HasPrivateKey, Is.True,
                    "A signing key that does not survive the file is no authority at all.");
                Assert.That(after.Material.Host.HasPrivateKey, Is.True);
            }
        }

        [Test]
        public void The_authority_can_still_issue_after_the_round_trip()
        {
            _service.Create();
            _database.SaveCloseAndReopen();

            var afterReopening = new AuthorityService(
                new CustomDataStore(_database.Database), NullGateLog.Instance);

            using (var reading = afterReopening.Read())
            using (var issued = new AuthorityIssuer().IssueHost(reading.Material.Authority))
            {
                Assert.That(AnchorPin.Chains(issued, reading.Material.Authority, false), Is.True);
            }
        }

        [Test]
        public void Deleting_survives_the_file_as_well()
        {
            _service.Create();
            _database.Save();
            _service.Delete();

            _database.SaveCloseAndReopen();

            var afterReopening = new AuthorityService(
                new CustomDataStore(_database.Database), NullGateLog.Instance);
            using (var reading = afterReopening.Read())
            {
                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None));
            }
        }

        [Test]
        public void Writing_stamps_the_key_so_a_replica_can_merge_it()
        {
            // The dictionary does this itself, which is the property the authority was moved here
            // for: a change with no modification time behind it is one another copy cannot prefer.
            _service.Create();

            // Read by reflection because KeePass keeps the accessor internal, which is also why
            // this is worth pinning: the whole placement rests on a stamp no public API reports.
            var accessor = typeof(StringDictionaryEx).GetMethod(
                "GetLastModificationTime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(accessor, Is.Not.Null,
                "KeePass no longer stamps a key of a database's own custom data, which is what a merge orders by");

            var stamped = (DateTime?)accessor.Invoke(
                _database.Database.CustomData, new object[] { AuthorityKeys.Authority });

            Assert.That(stamped, Is.Not.Null, "the authority was written without a modification time");
            Assert.That(stamped.Value, Is.GreaterThan(DateTime.UtcNow.AddMinutes(-5)));

            var onTheGroup = (DateTime?)accessor.Invoke(
                _database.RootGroup.CustomData, new object[] { AuthorityKeys.Authority });
            Assert.That(onTheGroup, Is.Null,
                "a group's dictionary keeps no per-key time, which is why nothing of ours is on it");
        }

        [Test]
        public void Nothing_of_ours_is_left_on_the_root_group()
        {
            _service.Create();

            Assert.That(_database.RootGroup.CustomData.Count(p => AuthorityKeys.IsOurs(p.Key)), Is.Zero,
                "The group dialog copies that dictionary and writes the copy back over anything put there.");
        }

        [Test]
        public void The_stored_form_is_text_that_a_kdbx_can_hold()
        {
            _service.Create();

            var stored = _database.Database.CustomData.Get(AuthorityKeys.Authority);
            Assert.That(stored, Is.Not.Null.And.Not.Empty);

            var body = JsonBody.Read(stored);
            Assert.That(body, Is.Not.Null, "what was written has to read back as a document");

            foreach (var part in new[]
                     {
                         AuthorityDocument.AuthorityPartKey,
                         AuthorityDocument.HostPartKey,
                         AuthorityDocument.SniKeyPartKey
                     })
            {
                var value = body.Text(part);
                Assert.That(value, Is.Not.Null.And.Not.Empty, part + " is missing");
                Assert.DoesNotThrow(() => Convert.FromBase64String(value), part + " is not base64");
            }
        }
    }
}
