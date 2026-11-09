using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib.Collections;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The KPG tab's OK replayed over the real store while the plugin's own writer lands changes
    /// underneath the dialog, the three that `PendingDatabase` merges rather than overwrites.
    /// </summary>
    /// <remarks>
    /// **The cases `PendingChangesTests` settles over a dictionary, run over KeePass's.** A pairing
    /// naming a profile while the Profiles page holds an edit, a client revoking itself while its
    /// row's X waits, and a renewal answered again while Allow renewal waits: each is a second
    /// writer on a key the tab buffered, and what the operator's OK keeps of each is decided by
    /// the merge `PendingStore.Reconcile` registers. The unit tier lands the second write straight
    /// into its store. Here it lands through `DatabaseWriter`, by the target a connection would
    /// use, saved and read back, onto `CustomDataStore` over a real `PwDatabase`, whose own rules
    /// are what is under test: a write that changes nothing skipped, and every key stamped by
    /// KeePass.
    ///
    /// `Apply` stands for OK, as it does in `OpenDatabaseTests`; `AuthorityPanelFactory` calls it
    /// for that answer and for no other.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TabReplayOverRealDatabaseTests
    {
        private TemporaryDatabase _database;
        private OpenDatabaseTargets _targets;
        private IGateTarget _target;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            var created = new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance).Create();
            Assert.That(created.Succeeded, Is.True, created.Message);
            _database.Save();

            var writer = new DatabaseWriter(
                CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            _targets = new OpenDatabaseTargets(() => new[] { _database.Database }, writer, NullGateLog.Instance);
            _target = _targets.Current().Single();
        }

        [TearDown]
        public void TearDown()
        {
            _target.Dispose();
            _targets.Dispose();
            _database.Dispose();
        }

        [Test]
        public void A_profile_edit_and_a_pairing_landing_under_it_both_survive_OK()
        {
            var seeded = new DatabaseProfiles(Live(), NullGateLog.Instance);
            Assume.That(seeded.Add("P1"), Is.Null);
            Assume.That(seeded.Add("Pz"), Is.Null);
            _database.Save();

            var pending = Pending();
            var page = new DatabaseProfiles(pending, NullGateLog.Instance);
            Assert.That(page.Add("P2"), Is.Null);
            Assert.That(page.Remove("P1"), Is.Null);

            using (var client = Client("agent-invented", "P3"))
            {
                var landed = _target.Admit(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "P3" }), null, null);
                Assert.That(landed.Ok, Is.True, "the pairing did not land");

                Assert.That(pending.Apply(), Is.True);

                Assert.That(Names(), Is.EquivalentTo(new[] { "P2", "P3", "Pz" }),
                    "the operator's edit and the name the pairing approved, and not one over the other");
                Assert.That(Admitted(), Does.Contain(CertificateDigest.Of(client)), "and the record the pairing wrote");
            }
        }

        [Test]
        public void A_client_s_own_revocation_landing_under_its_waiting_X_is_left_as_it_landed()
        {
            using (var client = Client("agent-invented", "ops"))
            {
                var key = CertificateDigest.Of(client);
                Assume.That(_target.Admit(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }), null, null).Ok, Is.True);

                var pending = Pending();
                using (var panel = new AuthorityPanel(pending, new NeverAsked(), NullGateLog.Instance))
                {
                    Assert.That(panel.Revoke(key), Is.Null, "the X was not taken");
                }

                Assert.That(_target.Revoke(key, null).Ok, Is.True, "the client's own revocation did not land");
                var landed = Snapshot();
                _database.Database.Modified = false;

                pending.Apply();

                Assert.That(Snapshot(), Is.EqualTo(landed),
                    "the operator's revocation, of a client already revoked, changed the keys, their values or their times");
                Assert.That(_database.Database.Modified, Is.False, "and left nothing to save");
                Assert.That(_target.Admission(key), Is.Null, "the client is revoked");
            }
        }

        [Test]
        public void A_renewal_answered_again_under_a_waiting_Allow_renewal_keeps_the_later_answer()
        {
            using (var client = Client("agent-invented", "ops"))
            {
                var key = CertificateDigest.Of(client);
                Assume.That(_target.Admit(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }), null, null).Ok, Is.True);
                Assume.That(_target.RefuseRenewal(key, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(1))).Ok, Is.True);

                var pending = Pending();
                using (var panel = new AuthorityPanel(pending, new NeverAsked(), NullGateLog.Instance))
                {
                    Assert.That(panel.AllowRenewal(key), Is.Null, "Allow renewal was not taken");
                }

                var later = RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(2));
                Assert.That(_target.RefuseRenewal(key, later).Ok, Is.True, "the renewal's answer did not land");
                var landed = Snapshot();

                pending.Apply();

                Assert.That(Snapshot(), Is.EqualTo(landed), "OK took away an answer given after Allow renewal was pressed");
                Assert.That(_target.StandingRefusal(key).Write(), Is.EqualTo(later.Write()),
                    "the gate answers with the later answer");
            }
        }

        private OpenDatabase Live()
        {
            return new OpenDatabase(_database.Database, delegate { });
        }

        private PendingDatabase Pending()
        {
            return new PendingDatabase(Live(), NullGateLog.Instance);
        }

        private X509Certificate2 Client(string name, params string[] profiles)
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), NullGateLog.Instance).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, profiles).Read();
            }
        }

        private IReadOnlyList<string> Names()
        {
            return ProfileNames.Read(_database.Database.CustomData.Get(ProfileNames.CustomDataKey)).Names;
        }

        private IEnumerable<string> Admitted()
        {
            return new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance)
                .All().Records.Select(record => record.AdmissionKey);
        }

        /// <summary>
        /// Every key of this plugin's in the database's own custom data, with its value and the
        /// time KeePass stamped it with, which is what a merge of the file decides by.
        /// </summary>
        private List<string> Snapshot()
        {
            var stamp = typeof(StringDictionaryEx).GetMethod(
                "GetLastModificationTime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(stamp, Is.Not.Null, "KeePass no longer records when a key was written");

            return _database.Database.CustomData
                .Where(pair => pair.Key.StartsWith("KPG", StringComparison.Ordinal))
                .Select(pair => pair.Key + " = " + pair.Value + " @ " +
                                (DateTime?)stamp.Invoke(_database.Database.CustomData, new object[] { pair.Key }))
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();
        }
    }
}
