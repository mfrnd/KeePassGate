using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A store sealed again in a copy of a real database and brought together with a copy that
    /// was not, by each of KeePass's five merge methods in both directions, `STORE.md` §625 and
    /// §639: what the seal took away stays away, and the class §639 row 5 accepts is pinned by
    /// name.
    /// </summary>
    /// <remarks>
    /// **Brought together by `PwDatabase.MergeIn`**, the copy merged from opened afresh from its
    /// saved file, as `StoreRuleMergeTests` brings copies together, and each merge followed by a
    /// save and a reopening, so that what is read is what the file holds.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class StoreSealMergeTests
    {
        private const string Revoked = "1a9bd859c58e662a169cfafcbc5b4d0bdb6eaeb54802d57f609cf055fb627840";
        private const string Profile = "ops";

        /// <summary>A rule granting nothing, as a row's version writes it.</summary>
        private static readonly string Nothing = RuleText.Row(AclGrant.Nothing);

        private static readonly PwMergeMethod[] Methods =
        {
            PwMergeMethod.Synchronize,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting,
            PwMergeMethod.CreateNewUuids,
        };

        private readonly List<TemporaryDatabase> _databases = new List<TemporaryDatabase>();

        [TearDown]
        public void TearDown()
        {
            foreach (var database in _databases)
            {
                database.Dispose();
            }

            _databases.Clear();
        }

        [Test]
        public void A_row_lost_with_its_summary_entry_and_sealed_grants_nothing_after_every_merge_with_a_copy_that_still_holds_it()
        {
            // The copy not sealed holds the row's version granting RU and the summary entry that
            // restates it; the sealed copy deleted both with deletion records and wrote a version
            // granting nothing, naming the lost version in `lost`. Whichever comes back with a
            // merge, the version the seal settled grants nothing beside the seal's.
            var original = Seeded();
            StoreSeeds.Rule(Store(original), Node(original), (Profile, "RU"));
            original.Save();
            var stale = Opened(original);

            DeleteForGood(original, Entries(original, StoreKinds.Version).Single());
            DeleteForGood(original, Entries(original, StoreKinds.Head).Single());
            Assume.That(State(original), Is.EqualTo(StoreState.Stopped));
            SealAtOk(original);
            original.Save();

            var wrong = new List<string>();
            var measured = new StringBuilder();
            foreach (var cell in Cells(original, stale))
            {
                using (var target = Opened(cell.Into))
                using (var source = Opened(cell.From))
                {
                    var into = RuleText.Row(Arrives(target));
                    target.Database.MergeIn(source.Database, cell.Method, null);
                    target.SaveCloseAndReopen();
                    var state = State(target);
                    var arrives = RuleText.Row(state == StoreState.Whole ? Arrives(target) : AclGrant.Nothing);
                    measured.AppendLine(cell.Name + ": " + state + ", " + arrives);

                    // As decided in every cell, `measured-seal` R3: a copy that stops serves
                    // nothing, which grants nothing as well, and is no decision kept.
                    if (state != StoreState.Whole)
                    {
                        wrong.Add(cell.Name + ": the copy merged into is " + state);
                    }
                    else if (cell.Method == PwMergeMethod.CreateNewUuids)
                    {
                        // A copy merged into by new identifiers takes no record of the other.
                        if (arrives != into)
                        {
                            wrong.Add(cell.Name + ": the copy merged into gives " + arrives + " where it gave " + into);
                        }
                    }
                    else if (cell.Into == original && arrives != Nothing)
                    {
                        wrong.Add(cell.Name + ": the sealed copy gives " + arrives + " after the merge");
                    }
                    else if (cell.Into == stale && arrives != Nothing)
                    {
                        wrong.Add(cell.Name + ": the seal brought in gives " + arrives);
                    }
                }
            }

            TestContext.WriteLine(measured.ToString());
            Assert.That(wrong, Is.Empty, measured.ToString());
        }

        [Test]
        public void A_revocation_whose_summary_entry_was_deleted_for_good_stands_after_the_seal_and_every_merge_brings_it_into_a_copy_from_before_it()
        {
            // `measured-seal` R1: the revocation's record stays, its summary entry is deleted for
            // good and the seal restates it. The copy from before the revocation holds neither,
            // so every method that takes the other copy's records in brings it there, and a merge
            // by new identifiers takes none and leaves that copy admitting the client.
            var original = Seeded();
            original.Save();
            var stale = Opened(original);
            StoreSeeds.Revoke(Store(original), Revoked);
            original.Save();

            DeleteForGood(original, Entries(original, StoreKinds.Head).Single());
            Assume.That(State(original), Is.EqualTo(StoreState.Stopped));
            SealAtOk(original);
            original.Save();

            var wrong = new List<string>();
            var measured = new StringBuilder();
            foreach (var cell in Cells(original, stale))
            {
                using (var target = Opened(cell.Into))
                using (var source = Opened(cell.From))
                {
                    target.Database.MergeIn(source.Database, cell.Method, null);
                    target.SaveCloseAndReopen();
                    var state = State(target);
                    var refused = RevokedIn(target);
                    measured.AppendLine(cell.Name + ": " + state + (refused ? ", refused" : ", not refused by a revocation"));
                    if (state != StoreState.Whole)
                    {
                        wrong.Add(cell.Name + ": the copy merged into is " + state);
                    }
                    else if (cell.Method == PwMergeMethod.CreateNewUuids)
                    {
                        // A copy merged into by new identifiers takes no record of the other.
                        if (refused != (cell.Into == original))
                        {
                            wrong.Add(cell.Name + ": the copy merged into " + (refused ? "refuses" : "admits") + " where it did not");
                        }
                    }
                    else if (!refused)
                    {
                        wrong.Add(cell.Name + ": the revoked client is admitted");
                    }
                }
            }

            TestContext.WriteLine(measured.ToString());
            Assert.That(wrong, Is.Empty, measured.ToString());
        }

        [Test]
        public void A_revocation_whose_client_nothing_names_is_restated_by_the_seal_and_refuses_the_client_a_copy_from_before_brings_back()
        {
            // F5 of `measured-seal-restates`, `STORE.md` §611: the revocation deleted the client's
            // record, and the revocation and its summary entry are then deleted with a deletion
            // record each, so that nothing left names the client. The seal shows it and restates it
            // by the keyed tag and subject hash of its deletion record; a copy from before the
            // revocation still holds the client's record, and whichever method brings that record
            // back, into either copy and with the sealed copy's deletion records kept or cleared
            // as Database Maintenance clears them, the client is refused.
            var original = Seeded();
            StoreSeeds.Client(Store(original), new Dictionary<string, string> { { StoreKinds.ClientMember, Revoked }, { StoreKinds.Certificate, Revoked } });
            original.Save();
            var stale = Opened(original);

            StoreSeeds.Revoke(Store(original), Revoked);
            Assume.That(Entries(original, StoreKinds.Client), Is.Empty, "the revocation took the client's record away");
            DeleteForGood(original, Entries(original, StoreKinds.Revoked).Single());
            DeleteForGood(original, Entries(original, StoreKinds.Head).Single());
            Assume.That(State(original), Is.EqualTo(StoreState.Stopped));
            using (var reading = Store(original).Read())
            {
                var losses = StoreLosses.Of(reading);
                Assert.That(losses.Revoked, Is.Empty, "nothing names the client");
                Assert.That(losses.Unnamed.Select(decision => decision.Kind), Is.EqualTo(new[] { StoreKinds.Revoked }), "the seal is shown the revocation");
            }

            SealAtOk(original);
            original.Save();
            var cleared = Opened(original);
            cleared.Database.DeletedObjects.Clear();
            cleared.Database.Modified = true;
            cleared.SaveCloseAndReopen();
            Assert.That(State(cleared), Is.EqualTo(StoreState.Whole), "the seal holds once Database Maintenance clears its deletion records");

            var wrong = new List<string>();
            var measured = new StringBuilder();
            foreach (var sealedCopy in new[] { original, cleared })
            {
                foreach (var cell in Cells(sealedCopy, stale))
                {
                    var name = (sealedCopy == cleared ? "deletion records cleared, " : "as sealed, ") + cell.Name;
                    using (var target = Opened(cell.Into))
                    using (var source = Opened(cell.From))
                    {
                        var before = Refuses(target);
                        target.Database.MergeIn(source.Database, cell.Method, null);
                        target.SaveCloseAndReopen();
                        var refused = Refuses(target);
                        var state = State(target);
                        measured.AppendLine(name + ": " + state + (refused ? ", refused" : ", admitted"));
                        if (state != StoreState.Whole)
                        {
                            wrong.Add(name + ": the copy merged into is " + state);
                        }
                        else if (cell.Method == PwMergeMethod.CreateNewUuids)
                        {
                            // A copy merged into by new identifiers takes no record of the other,
                            // and the copy from before never held the revocation.
                            if (refused != before)
                            {
                                wrong.Add(name + ": the copy merged into " + (refused ? "refuses" : "admits") + " where it did not");
                            }
                        }
                        else if (!refused)
                        {
                            wrong.Add(name + ": the revoked client is admitted");
                        }
                    }
                }
            }

            TestContext.WriteLine(measured.ToString());
            Assert.That(wrong, Is.Empty, measured.ToString());
        }

        [Test]
        public void Accepted_a_revocation_lost_with_no_deletion_record_is_not_shown_by_the_seal_and_a_stale_copy_does_not_bring_it_back()
        {
            // `STORE.md` §639 row 5, accepted: a revocation and its summary entry removed with no
            // deletion record leave nothing for the seal to show, and the window states that what
            // was deleted with no record cannot be shown. The seal restates no revocation, and a
            // copy from before the revocation holds none to bring back: the client is admitted in
            // the sealed copy and after every merge with the stale one.
            var original = Seeded();
            original.Save();
            var stale = Opened(original);

            StoreSeeds.Revoke(Store(original), Revoked);
            Assume.That(RevokedIn(original), Is.True);
            RemoveWithNoRecord(original, Entries(original, StoreKinds.Revoked).Single());
            RemoveWithNoRecord(original, Entries(original, StoreKinds.Head).Single());
            Assume.That(State(original), Is.EqualTo(StoreState.Stopped));
            using (var reading = Store(original).Read())
            {
                var losses = StoreLosses.Of(reading);
                Assert.That(losses.Revoked, Is.Empty, "nothing shows the revocation");
            }

            SealAtOk(original);
            original.Save();
            Assert.That(RevokedIn(original), Is.False, "the seal restates no revocation it was not shown");

            var measured = new StringBuilder();
            var wrong = new List<string>();
            foreach (var cell in Cells(original, stale))
            {
                using (var target = Opened(cell.Into))
                using (var source = Opened(cell.From))
                {
                    target.Database.MergeIn(source.Database, cell.Method, null);
                    target.SaveCloseAndReopen();
                    var state = State(target);
                    var refused = RevokedIn(target);
                    measured.AppendLine(cell.Name + ": " + state + (refused ? ", refused" : ", not refused by a revocation"));
                    if (state != StoreState.Whole)
                    {
                        wrong.Add(cell.Name + ": the copy merged into is " + state);
                    }
                    else if (refused)
                    {
                        wrong.Add(cell.Name + ": a revocation neither copy held is in force");
                    }
                }
            }

            TestContext.WriteLine(measured.ToString());
            Assert.That(wrong, Is.Empty, measured.ToString());
        }

        /// <summary>Each method in both directions between the two copies.</summary>
        private static IEnumerable<Cell> Cells(TemporaryDatabase sealedCopy, TemporaryDatabase stale)
        {
            foreach (var method in Methods)
            {
                yield return new Cell(method, sealedCopy, stale, method + ", the stale copy into the sealed");
                yield return new Cell(method, stale, sealedCopy, method + ", the sealed copy into the stale");
            }
        }

        /// <summary>A new database holding an authority and the profile name, the root's rule given to it.</summary>
        private TemporaryDatabase Seeded()
        {
            var database = TemporaryDatabase.New();
            _databases.Add(database);
            var created = DatabaseAuthority.Of(database.Database, null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
            StoreSeeds.Name(Store(database), Profile);
            return database;
        }

        /// <summary>The seal pressed on the tab's buffer over the live database, and the dialog's OK.</summary>
        private static void SealAtOk(TemporaryDatabase database)
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, saved => { }, null, null, NullGateLog.Instance);
            var pending = new PendingDatabase(new OpenDatabase(database.Database, delegate { }, writer, NullGateLog.Instance), NullGateLog.Instance);
            var pressed = new AuthorityService(pending.Store, NullGateLog.Instance).Seal();
            Assert.That(pressed.Succeeded, Is.True, pressed.Message);
            Assert.That(pending.Apply(), Is.True, "OK wrote the seal");
            Assert.That(State(database), Is.EqualTo(StoreState.Whole), "the seal lifted the stop");
        }

        /// <summary>
        /// A saved database opened afresh from a copy of its file, as KeePass's import and
        /// synchronize open the other copy.
        /// </summary>
        private TemporaryDatabase Opened(TemporaryDatabase saved)
        {
            var copy = TemporaryDatabase.New();
            _databases.Add(copy);
            File.Copy(saved.Path, copy.Path, true);
            copy.Reopen();
            return copy;
        }

        private static IGateStore Store(TemporaryDatabase database)
        {
            return new KdbxStore(database.Database, GateVersion.Current, NullGateLog.Instance);
        }

        private static StoreState State(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.State;
            }
        }

        private static bool RevokedIn(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.State == StoreState.Whole && reading.InForce.Taken(StoreKinds.Revoked).Contains(Revoked);
            }
        }

        /// <summary>
        /// Whether the store refuses the revoked client: it is not whole, no record of kind
        /// `client` names the client key, so there is nothing to admit, or a revocation of it is
        /// in force.
        /// </summary>
        private static bool Refuses(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.State != StoreState.Whole
                       || !reading.InForce.Admissions.Any(record => record.Form.Member(StoreKinds.ClientMember) == Revoked)
                       || reading.InForce.Taken(StoreKinds.Revoked).Contains(Revoked);
            }
        }

        /// <summary>What a node directly under the root receives for the profile.</summary>
        private static AclGrant Arrives(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                if (reading.State != StoreState.Whole)
                {
                    return AclGrant.Nothing;
                }

                return AclResolver.Arrives(KdbxNodes.Of(database.RootGroup, StoredRules.Of(reading)), Profile);
            }
        }

        private static string Node(TemporaryDatabase database)
        {
            return Hex.Of(database.RootGroup.Uuid.UuidBytes);
        }

        private static IEnumerable<PwEntry> Entries(TemporaryDatabase database, string kind)
        {
            return database.RootGroup.GetEntries(true).Where(entry => entry.Strings.ReadSafe(StoreComposer.Kind) == kind).ToList();
        }

        /// <summary>A deletion for good, which leaves a deletion record dated a second after the clock.</summary>
        private static void DeleteForGood(TemporaryDatabase database, PwEntry entry)
        {
            entry.ParentGroup.Entries.Remove(entry);
            database.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow.AddSeconds(1)));
        }

        /// <summary>An entry removed with no deletion record, as another program can leave a copy.</summary>
        private static void RemoveWithNoRecord(TemporaryDatabase database, PwEntry entry)
        {
            entry.ParentGroup.Entries.Remove(entry);
            database.Database.Modified = true;
        }

        private sealed class Cell
        {
            public Cell(PwMergeMethod method, TemporaryDatabase into, TemporaryDatabase from, string name)
            {
                Method = method;
                Into = into;
                From = from;
                Name = name;
            }

            public PwMergeMethod Method { get; private set; }

            public TemporaryDatabase Into { get; private set; }

            public TemporaryDatabase From { get; private set; }

            public string Name { get; private set; }
        }
    }
}
