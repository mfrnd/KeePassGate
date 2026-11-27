using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Security;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Sealing the store again in a real database, `STORE.md` §625: each stop of §624 made with
    /// KeePass's own types, the seal pressed on the tab's buffer and written by its OK through the
    /// real writer, the store served again, and what the seal wrote kept by a save.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SealInRealDatabaseTests
    {
        private const string Client = "1a9bd859c58e662a169cfafcbc5b4d0bdb6eaeb54802d57f609cf055fb627840";

        private TemporaryDatabase _database;
        private readonly List<TemporaryDatabase> _copies = new List<TemporaryDatabase>();

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            var created = DatabaseAuthority.Of(_database.Database, null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
            foreach (var copy in _copies)
            {
                copy.Dispose();
            }

            _copies.Clear();
        }

        [Test]
        public void A_summary_entry_deleted_for_good_is_sealed_at_OK_served_again_and_kept_by_a_save()
        {
            StoreSeeds.Revoke(Seeds(), Client);
            DeleteForGood(Entries("head").Single());
            Assume.That(State(), Is.EqualTo(StoreState.Stopped));

            SealAtOk();

            Assert.That(State(), Is.EqualTo(StoreState.Whole));
            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(StoreAuthorities.Served(reading), Is.Not.Null, "served again");
                Assert.That(reading.InForce.Taken(StoreKinds.Revoked), Has.Member(Client), "the revocation the deleted summary entry restated stands");
            }
        }

        [TestCase("revoked", false)]
        [TestCase("revoked", true)]
        [TestCase("client", false)]
        [TestCase("profile", false)]
        [TestCase("head", false)]
        [TestCase("version", false)]
        [TestCase("version", true)]
        public void An_entry_edited_past_the_history_limit_is_deleted_by_the_seal_and_what_a_summary_entry_held_stands(string kind, bool noHistory)
        {
            // `STORE.md` §644 item 2: an edit past KeePass's history limit, or a single edit where
            // the database keeps no history, leaves no version that passes. The seal deletes it
            // with a deletion record; a revocation and a rule its summary entry restated stand,
            // and an addition no summary entry restates is gone, which is narrower.
            var seeds = Seeds();
            StoreSeeds.Revoke(seeds, Client);
            StoreSeeds.Client(seeds, new Dictionary<string, string> { { "client", new string('e', 64) }, { "certificate", new string('e', 64) } });
            StoreSeeds.Name(seeds, "ops");
            var node = _database.RootGroup.Uuid.ToHexString().ToLowerInvariant();
            StoreSeeds.Rule(seeds, node, ("ops", "R"));
            Assume.That(State(), Is.EqualTo(StoreState.Whole));

            var entry = Entries(kind).OrderByDescending(found => found.LastModificationTime).First();
            if (noHistory)
            {
                _database.Database.HistoryMaxItems = 0;
                Edit(entry, Read(entry) + " ");
            }
            else
            {
                Edit(entry, Read(entry) + " ");
                for (var edit = 2; edit <= 11; edit++)
                {
                    QuickEdit(entry);
                }
            }

            using (var reading = Seeds().Read())
            {
                Assume.That(reading.State, Is.EqualTo(StoreState.Stopped), reading.Reason);
                Assume.That(reading.Damaged.Single().Hex, Is.EqualTo(entry.Uuid.ToHexString().ToLowerInvariant()));
            }

            SealAtOk();

            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(_database.RootGroup.FindEntry(entry.Uuid, true), Is.Null, "the damaged entry is deleted");
                Assert.That(_database.Database.DeletedObjects.Any(deleted => deleted.Uuid.Equals(entry.Uuid)), Is.True, "with a deletion record");
                Assert.That(reading.InForce.Taken(StoreKinds.Revoked), Has.Member(Client), "the revocation stands, by its item where it was the entry edited");
                Assert.That(
                    reading.InForce.VersionsOf(KeePassGate.Acl.RuleText.RowObject(node, "ops")).Select(version => version.Members[StoreKinds.Doc]),
                    Is.EqualTo(new[] { "row:R" }),
                    "the rule stands, by its item where it was the entry edited");
                if (kind == "profile")
                {
                    Assert.That(KeePassGate.Acl.ProfileNames.Of(reading).Names, Does.Not.Contain("ops"), "the name is off the list: narrower");
                }

                if (kind == "client")
                {
                    Assert.That(
                        reading.InForce.Admissions.Select(admission => admission.Form.Member(StoreKinds.ClientMember)),
                        Does.Not.Contain(new string('e', 64)),
                        "the client is refused, its admission no summary entry restates gone: narrower");
                }
            }
        }

        [Test]
        public void A_summary_entry_made_apart_and_deleted_for_good_beside_one_in_force_is_sealed_at_OK()
        {
            // `STORE.md` §624, a summary entry deleted that nothing in force replaces: two copies
            // write apart, a synchronize leaves both summary entries in force, and one of them is
            // deleted for good. What each restated stands after the seal.
            _database.Save();
            var apart = Opened(_database);
            StoreSeeds.Revoke(Seeds(), Client);
            StoreSeeds.Name(DatabaseAuthority.Store(apart.Database), "ops");
            apart.Save();
            using (var other = Opened(apart))
            {
                _database.Database.MergeIn(other.Database, PwMergeMethod.Synchronize, null);
            }

            Assume.That(Entries("head"), Has.Count.EqualTo(2), "both summary entries in force");
            DeleteForGood(Entries("head").Single(head => !Restates(head, Client)));
            using (var reading = Seeds().Read())
            {
                Assert.That(StoreLosses.Of(reading).Stops, Is.EqualTo(new[] { StoreStop.SummaryEntryDeleted }), reading.Reason);
            }

            SealAtOk();

            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(StoreAuthorities.Served(reading), Is.Not.Null, "served again");
                Assert.That(reading.InForce.Taken(StoreKinds.Revoked), Has.Member(Client), "the revocation stands");
                Assert.That(KeePassGate.Acl.ProfileNames.Of(reading).Names, Does.Contain("ops"), "the name the deleted summary entry was written with stands");
            }
        }

        [Test]
        public void A_record_no_summary_entry_restates_is_sealed_at_OK_and_restated_by_the_seal()
        {
            // `STORE.md` §624, a record present that no summary entry restates: a revocation's
            // entry carried into a copy from before it without the summary entry written with
            // it, as a program that copies entries alone leaves it.
            _database.Save();
            var revoking = Opened(_database);
            StoreSeeds.Revoke(DatabaseAuthority.Store(revoking.Database), Client);
            var revocation = revoking.RootGroup.GetEntries(true).Single(entry => entry.Strings.ReadSafe(StoreComposer.Kind) == "revoked");
            Entries("head").Single().ParentGroup.AddEntry(revocation.CloneDeep(), true);
            using (var reading = Seeds().Read())
            {
                Assert.That(StoreLosses.Of(reading).Stops, Is.EqualTo(new[] { StoreStop.NotRestated }), reading.Reason);
            }

            SealAtOk();

            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(reading.InForce.Taken(StoreKinds.Revoked), Has.Member(Client), "the revocation stands");
                Assert.That(
                    reading.InForce.HeadsInForce.Single().Form.Items.Where(item => item.Kind == StoreKinds.Revoked).Select(item => item.Member(StoreKinds.ClientMember)),
                    Is.EqualTo(new[] { Client }),
                    "restated by the seal's summary entry");
            }
        }

        [Test]
        public void A_version_of_a_row_deleted_with_its_summary_entry_is_sealed_as_a_rule_granting_nothing()
        {
            var node = _database.RootGroup.Uuid.ToHexString().ToLowerInvariant();
            StoreSeeds.Name(Seeds(), "ops");
            StoreSeeds.Rule(Seeds(), node, ("ops", "RU"));
            DeleteForGood(Entries("version").Single());
            DeleteForGood(Entries("head").Single());
            Assume.That(State(), Is.EqualTo(StoreState.Stopped));

            SealAtOk();

            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var row = KeePassGate.Acl.RuleText.RowObject(node, "ops");
                Assert.That(reading.InForce.VersionsOf(row).Single().Members[StoreKinds.Doc], Is.EqualTo("row:"));
                Assert.That(reading.InForce.LostOf(row), Is.Empty, "the seal settled the loss");
            }
        }

        [Test]
        public void A_lost_answer_to_a_renewal_is_sealed_as_refused_forever()
        {
            StoreSeeds.Client(Seeds(), new Dictionary<string, string> { { "client", Client }, { "certificate", Client } });
            StoreSeeds.Refuse(Seeds(), Client, RenewalRefusal.Until(DateTimeOffset.UtcNow.AddDays(30)));
            DeleteForGood(Entries("version").Single());
            DeleteForGood(Entries("head").Single());
            Assume.That(State(), Is.EqualTo(StoreState.Stopped));

            SealAtOk();

            _database.SaveCloseAndReopen();
            using (var reading = Seeds().Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var answer = StoredRenewals.AnswerOf(reading.InForce, Client);
                Assert.That(answer, Is.Not.Null);
                Assert.That(answer.IsForever, Is.True, "refused forever");
                var answerObject = RenewalText.ObjectOf(Client);
                Assert.That(reading.InForce.VersionsOf(answerObject).Single().Members[StoreKinds.Doc], Is.EqualTo("refuse:forever"), "by a version the seal wrote");
                Assert.That(reading.InForce.LostOf(answerObject), Is.Empty, "the seal settled the loss");
            }
        }

        [Test]
        public void A_store_sealed_and_never_saved_is_stopped_again_once_reopened()
        {
            // The settings dialog's OK marks the database; the operator's save keeps the seal.
            DeleteForGood(Entries("head").Single());
            _database.Save();
            SealAtOk();
            Assert.That(State(), Is.EqualTo(StoreState.Whole), "the seal lifted the stop before the reopening");

            _database.CloseAndReopenWithoutSaving();

            Assert.That(State(), Is.EqualTo(StoreState.Stopped));
        }

        /// <summary>The seal pressed on the tab's buffer over the live database, and the dialog's OK.</summary>
        private void SealAtOk()
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, database => database.Save(null), null, null, NullGateLog.Instance);
            var pending = new PendingDatabase(new OpenDatabase(_database.Database, delegate { }, writer, NullGateLog.Instance), NullGateLog.Instance);
            var pressed = new AuthorityService(pending.Store, NullGateLog.Instance).Seal();
            Assert.That(pressed.Succeeded, Is.True, pressed.Message);
            Assert.That(pending.Apply(), Is.True, "OK wrote the seal");
            Assert.That(pending.Sealed, Is.Not.Null);
        }

        /// <summary>
        /// A saved database opened afresh from a copy of its file, as KeePass's import and
        /// synchronize open the other copy.
        /// </summary>
        private TemporaryDatabase Opened(TemporaryDatabase saved)
        {
            var copy = TemporaryDatabase.New();
            _copies.Add(copy);
            File.Copy(saved.Path, copy.Path, true);
            copy.Reopen();
            return copy;
        }

        /// <summary>Whether a summary entry restates a revocation of this client.</summary>
        private bool Restates(PwEntry head, string client)
        {
            using (var reading = Seeds().Read())
            {
                return reading.InForce.Heads.Any(found => string.Equals(found.Hex, Hex.Of(head.Uuid.UuidBytes), StringComparison.Ordinal)
                    && found.Form.Items.Any(item => item.Kind == StoreKinds.Revoked && item.Member(StoreKinds.ClientMember) == client));
            }
        }

        private KeePassGate.Store.IGateStore Seeds()
        {
            return DatabaseAuthority.Store(_database.Database);
        }

        private StoreState State()
        {
            using (var reading = Seeds().Read())
            {
                return reading.State;
            }
        }

        private IEnumerable<PwEntry> Entries(string kind)
        {
            return _database.RootGroup.GetEntries(true).Where(entry => entry.Strings.ReadSafe(StoreComposer.Kind) == kind).ToList();
        }

        private static string Read(PwEntry entry)
        {
            return entry.Strings.ReadSafe(StoreComposer.Data);
        }

        /// <summary>An edit of the content in the entry dialog, which backs the entry up first.</summary>
        private void Edit(PwEntry entry, string content)
        {
            entry.CreateBackup(_database.Database);
            entry.Strings.Set(StoreComposer.Data, new ProtectedString(entry.Strings.Get(StoreComposer.Data).IsProtected, content));
            entry.Touch(true, false);
        }

        /// <summary>A quick edit, which backs the entry up as well.</summary>
        private void QuickEdit(PwEntry entry)
        {
            entry.CreateBackup(_database.Database);
            entry.Expires = !entry.Expires;
            entry.Touch(true, false);
        }

        /// <summary>A deletion for good, with a deletion record dated a second after the clock.</summary>
        private void DeleteForGood(PwEntry entry)
        {
            entry.ParentGroup.Entries.Remove(entry);
            _database.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow.AddSeconds(1)));
        }
    }
}
