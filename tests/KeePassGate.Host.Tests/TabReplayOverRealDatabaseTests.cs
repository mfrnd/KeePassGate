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
using KeePassGate.Store;
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
    /// naming a profile while the Profiles page holds an edit, a client giving its certificate up
    /// while its row's X waits, and a renewal answered again while Allow renewal waits: each is a
    /// second writer on what the tab holds, and what the operator's OK keeps of each is decided by
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
        private DatabaseWriter _writer;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            var created = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance).Create();
            Assert.That(created.Succeeded, Is.True, created.Message);
            _database.Save();

            _writer = new DatabaseWriter(
                CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            _targets = new OpenDatabaseTargets(() => new[] { _database.Database }, _writer, NullGateLog.Instance);
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
                var landed = _target.Admit(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "P3" }, Served()), null, null);
                Assert.That(landed.Ok, Is.True, "the pairing did not land");

                Assert.That(pending.Apply(), Is.True);

                Assert.That(Names(), Is.EquivalentTo(new[] { "P2", "P3", "Pz" }),
                    "the operator's edit and the name the pairing approved, and not one over the other");
                Assert.That(Admitted(), Does.Contain(CertificateDigest.Of(client)), "and the record the pairing wrote");
            }
        }

        [Test]
        public void A_certificate_given_up_under_its_client_s_waiting_X_and_the_revocation_both_stand_at_OK()
        {
            // The client gives its certificate up while the operator's revocation of it waits for
            // OK. Each is a record of its own, and the revocation has no ground for the other to
            // take away, so OK writes it beside the record the client's write left.
            using (var client = Client("agent-invented", "ops"))
            {
                var key = CertificateDigest.Of(client);
                Assume.That(_target.Admit(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }, Served()), null, null).Ok, Is.True);

                var pending = Pending();
                using (var panel = new AuthorityPanel(pending, new NeverAsked(), NullGateLog.Instance))
                {
                    Assert.That(panel.Revoke(key), Is.Null, "the X was not taken");
                }

                Assert.That(_target.GiveUp(key, null).Ok, Is.True, "the client's own certificate given up did not land");

                pending.Apply();

                var admissions = new StoredAdmissions(_database.Database);
                Assert.That(admissions.Holds("withdrawn", key), Is.True, "the record the client's write left");
                Assert.That(admissions.Holds("revoked", key), Is.True, "and the operator's revocation beside it");
                Assert.That(_target.Admission(key), Is.Null, "the client is refused");
            }
        }

        [Test]
        public void A_renewal_answered_again_under_a_waiting_Allow_renewal_keeps_the_later_answer()
        {
            using (var client = Client("agent-invented", "ops"))
            {
                var key = CertificateDigest.Of(client);
                Assume.That(_target.Admit(AdmissionRecord.For(client, DateTimeOffset.UtcNow, new[] { "ops" }, Served()), null, null).Ok, Is.True);
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

        [Test]
        public void An_Add_a_removal_overtook_under_the_dialog_writes_no_addition_and_the_log_names_it()
        {
            // A removal of the name that the press did not see lands under the dialog, as a copy
            // merged in brings it. The removal wins over the addition that did not see it: OK
            // writes no addition, the name is off the list in the file, and the log names the
            // press.
            var log = new WarningsLog();
            var pending = new PendingDatabase(Live(), log);
            Assert.That(new DatabaseProfiles(pending, NullGateLog.Instance).Add("ops"), Is.Null);

            var store = new StoredAdmissions(_database.Database).Store;
            StoreSeeds.Name(store, "ops");
            StoreSeeds.Unname(store, "ops");

            pending.Apply();
            _database.SaveCloseAndReopen();

            using (var reading = new StoredAdmissions(_database.Database).Store.Read())
            {
                Assert.That(ProfileNames.Of(reading).Holds("ops"), Is.False, "the name is off the list");
                Assert.That(reading.InForce.Additions.Where(addition => addition.Form.Member(StoreKinds.NameMember) == "ops"), Is.Empty,
                    "and no addition of it is written");
            }

            Assert.That(log.Warnings, Has.Some.Matches<string>(line => line.Contains("adding a profile ops") && line.Contains("not written")),
                "the log names the press");
        }

        private OpenDatabase Live()
        {
            return new OpenDatabase(_database.Database, delegate { }, _writer, NullGateLog.Instance);
        }

        private PendingDatabase Pending()
        {
            return new PendingDatabase(Live(), NullGateLog.Instance);
        }

        private X509Certificate2 Client(string name, params string[] profiles)
        {
            using (var reading = DatabaseAuthority.Of(_database.Database, NullGateLog.Instance).Read())
            {
                return new AuthorityIssuer().IssueClient(reading.Material.AuthorityKey, name, profiles).Read();
            }
        }

        private string Served()
        {
            return StoreSeeds.Served(new StoredAdmissions(_database.Database).Store);
        }

        private IReadOnlyList<string> Names()
        {
            using (var reading = new StoredAdmissions(_database.Database).Store.Read())
            {
                return ProfileNames.Of(reading).Names;
            }
        }

        private IEnumerable<string> Admitted()
        {
            return new StoredAdmissions(_database.Database)
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

        /// <summary>The warnings logged, every other line dropped.</summary>
        private sealed class WarningsLog : IGateLog
        {
            public List<string> Warnings { get; } = new List<string>();

            public void Info(string message)
            {
            }

            public void Warn(string message)
            {
                Warnings.Add(message);
            }

            public void Error(string message, Exception error)
            {
                Warnings.Add(message);
            }

            public void Unexpected(string message, Exception error)
            {
                Warnings.Add(message);
            }
        }
    }
}
