using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
    /// A store started again in a copy of a real database and brought together with another copy
    /// by each of KeePass's five merge methods in both directions, `STORE.md` §632 and §649, as T4
    /// to T7 and T14 of `measured-started-again` have it: a stale gate's pairing under the earlier
    /// key counts nowhere the keep record reaches, the earlier authority brought back counts for
    /// nothing, two copies started apart leave two keys standing, a start after a start sets both
    /// earlier keys aside, and a new key brought back to a store sealed over its loss meets the
    /// seal's keep record and splits the copy it is brought into.
    /// </summary>
    /// <remarks>
    /// **Brought together by `PwDatabase.MergeIn`**, the copy merged from opened afresh from its
    /// saved file, and each merge followed by a save and a reopening, as `StoreSealMergeTests`
    /// brings copies together. A merge by new identifiers takes no record of the other copy, whose
    /// records do not pass under identifiers of their own, so the copy merged into reads as it did:
    /// its state, the keys that stand and the records in force, held in every such cell.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class StoreStartAgainMergeTests
    {
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
        public void T4_a_stale_gate_s_pairing_under_the_earlier_key_counts_nowhere_the_keep_record_reaches()
        {
            var started = Seeded();
            started.Save();
            var stale = Opened(started);
            StartAtOk(started);
            var paired = Paired(started, 'a');
            started.Save();
            var stalePaired = Paired(stale, 'b');
            stale.Save();
            var newKey = KeyOf(started);

            AssertCells(started, stale, (cell, target) =>
            {
                using (var reading = Store(target).Read())
                {
                    if (reading.State != StoreState.Whole || reading.InForce.Key.Name != newKey)
                    {
                        return "the store started again does not stand alone: " + reading.State + ", " + reading.Reason;
                    }

                    var counted = new HashSet<string>(reading.InForce.Records.Select(record => record.Hex), StringComparer.Ordinal);
                    return counted.Contains(stalePaired) ? "the stale gate's pairing counts"
                        : !counted.Contains(paired) ? "the pairing under the new key is lost"
                        : null;
                }
            });
        }

        [Test]
        public void T5_the_earlier_authority_brought_back_counts_for_nothing()
        {
            var started = Seeded();
            started.Save();
            var stale = Opened(started);
            StartAtOk(started);
            var newKey = KeyOf(started);
            HashSet<string> counted;
            using (var reading = Store(started).Read())
            {
                counted = new HashSet<string>(reading.InForce.Records.Select(record => record.Hex), StringComparer.Ordinal);
            }

            var earlier = Entries(started, StoreKinds.Authority).Single(entry => !counted.Contains(Hex.Of(entry.Uuid.UuidBytes)));
            earlier.ParentGroup.Entries.Remove(earlier);
            started.Database.DeletedObjects.Add(new PwDeletedObject(earlier.Uuid, DateTime.UtcNow.AddSeconds(1)));
            started.Save();

            AssertCells(started, stale, (cell, target) =>
            {
                using (var reading = Store(target).Read())
                {
                    return reading.State == StoreState.Whole && reading.InForce.Key.Name == newKey
                        ? null
                        : "the store started again does not stand alone: " + reading.State + ", " + reading.Reason;
                }
            });
        }

        [Test]
        public void T6_two_copies_started_apart_leave_two_keys_standing_and_a_start_on_the_merged_file_sets_both_aside()
        {
            var first = Seeded();
            first.Save();
            var second = Opened(first);
            StartAtOk(first);
            first.Save();
            StartAtOk(second);
            second.Save();
            var keys = new[] { KeyOf(first), KeyOf(second) };

            AssertCells(first, second, (cell, target) =>
            {
                using (var reading = Store(target).Read())
                {
                    if (reading.State != StoreState.Split || !reading.Standing.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(keys.OrderBy(key => key, StringComparer.Ordinal)))
                    {
                        return "the two keys started apart do not both stand: " + reading.State + ", " + reading.Reason;
                    }
                }

                StartAtOk(target);
                using (var reading = Store(target).Read())
                {
                    return reading.State == StoreState.Whole && reading.Found.Count == 4 && !keys.Contains(reading.InForce.Key.Name)
                        ? null
                        : "a start on the merged file did not set every key aside: " + reading.State + ", " + reading.Reason;
                }
            });
        }

        [Test]
        public void T7_a_start_after_a_start_sets_both_earlier_keys_aside_in_every_copy()
        {
            var started = Seeded();
            StartAtOk(started);
            started.Save();
            var synchronized = Opened(started);
            StartAtOk(started);
            started.Save();
            var newKey = KeyOf(started);

            AssertCells(started, synchronized, (cell, target) =>
            {
                using (var reading = Store(target).Read())
                {
                    return reading.State == StoreState.Whole && reading.InForce.Key.Name == newKey && reading.Found.Count == 3
                        ? null
                        : "the second start does not stand alone over both earlier keys: " + reading.State + ", " + reading.Reason;
                }
            });
        }

        [Test]
        public void T14_a_new_key_brought_back_to_a_store_sealed_over_its_loss_splits_the_copy_no_deletion_record_reaches()
        {
            // At copy 0 a start, synchronized into copy 1; then at copy 0 the new authority's
            // entry deleted for good, which leaves the earlier store stopped by the start's stop,
            // and Seal again, which keeps the earlier store over the new key. By Synchronize the
            // deletion record travels and the earlier store is served in both copies; into copy 1
            // by every other method that carries records, the new key brought back meets the
            // seal's keep record and both keys stand: a circle, the store split, nothing served.
            var started = Seeded();
            string earlier;
            using (var reading = Store(started).Read())
            {
                earlier = reading.InForce.Key.Name;
            }

            StartAtOk(started);
            started.Save();
            var synchronized = Opened(started);
            var newKey = KeyOf(started);
            HashSet<string> counted;
            using (var reading = Store(started).Read())
            {
                counted = new HashSet<string>(reading.InForce.Records.Select(record => record.Hex), StringComparer.Ordinal);
            }

            var made = Entries(started, StoreKinds.Authority).Single(entry => counted.Contains(Hex.Of(entry.Uuid.UuidBytes)));
            made.ParentGroup.Entries.Remove(made);
            started.Database.DeletedObjects.Add(new PwDeletedObject(made.Uuid, DateTime.UtcNow.AddSeconds(1)));
            using (var reading = Store(started).Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Stopped), "the stop holds the earlier store: " + reading.Reason);
            }

            SealAtOk(started);
            started.Save();
            Assert.That(KeyOf(started), Is.EqualTo(earlier), "the seal keeps the earlier store");

            AssertCells(started, synchronized, (cell, target) =>
            {
                using (var reading = Store(target).Read())
                {
                    var intoStarted = cell.Into == synchronized;
                    if (intoStarted && cell.Method != PwMergeMethod.Synchronize)
                    {
                        return reading.State == StoreState.Split
                               && reading.Standing.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(new[] { earlier, newKey }.OrderBy(key => key, StringComparer.Ordinal))
                               && StoreAuthorities.Served(reading) == null
                            ? null
                            : "the new key brought back does not split the copy: " + reading.State + ", " + reading.Reason;
                    }

                    return reading.State == StoreState.Whole && reading.InForce.Key.Name == earlier && StoreAuthorities.Served(reading) != null
                        ? null
                        : "the earlier store is not served alone: " + reading.State + ", " + reading.Reason;
                }
            });
        }

        /// <summary>
        /// Each method in both directions, the copy merged into read after a save and a reopening:
        /// held to what is wrong with it by the caller, and by new identifiers to the copy as it read
        /// before the merge.
        /// </summary>
        private void AssertCells(TemporaryDatabase left, TemporaryDatabase right, Func<Cell, TemporaryDatabase, string> wrongWith)
        {
            var wrong = new List<string>();
            var measured = new StringBuilder();
            foreach (var method in Methods)
            {
                foreach (var cell in new[] { new Cell(method, left, right, method + ", the second copy into the first"), new Cell(method, right, left, method + ", the first copy into the second") })
                {
                    using (var target = Opened(cell.Into))
                    using (var source = Opened(cell.From))
                    {
                        var before = ReadAs(target);
                        target.Database.MergeIn(source.Database, cell.Method, null);
                        target.SaveCloseAndReopen();
                        using (var reading = Store(target).Read())
                        {
                            measured.AppendLine(cell.Name + ": " + reading.State + ", " + reading.Standing.Count + " standing of " + reading.Found.Count + " found");
                        }

                        // A copy merged into by new identifiers takes no record of the other.
                        var after = ReadAs(target);
                        var why = cell.Method == PwMergeMethod.CreateNewUuids
                            ? (after == before ? null : "the copy merged into by new identifiers reads otherwise: " + after + " where it read " + before)
                            : wrongWith(cell, target);
                        if (why != null)
                        {
                            wrong.Add(cell.Name + ": " + why);
                        }
                    }
                }
            }

            TestContext.WriteLine(measured.ToString());
            Assert.That(wrong, Is.Empty, measured.ToString());
        }

        /// <summary>
        /// How a copy reads: its state, the keys that stand, and the records in force, by their
        /// identifiers.
        /// </summary>
        private static string ReadAs(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.State + "; " + string.Join(" ", reading.Standing.OrderBy(key => key, StringComparer.Ordinal)) + "; "
                       + (reading.InForce == null ? string.Empty : string.Join(" ", reading.InForce.Records.Select(record => record.Hex).OrderBy(hex => hex, StringComparer.Ordinal)));
            }
        }

        /// <summary>Seal again pressed on the tab's buffer over the live database, and the dialog's OK.</summary>
        private static void SealAtOk(TemporaryDatabase database)
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, saved => { }, null, null, NullGateLog.Instance);
            var pending = new PendingDatabase(new OpenDatabase(database.Database, delegate { }, writer, NullGateLog.Instance), NullGateLog.Instance);
            var pressed = new AuthorityService(pending.Store, NullGateLog.Instance).Seal();
            Assert.That(pressed.Succeeded, Is.True, pressed.Message);
            Assert.That(pending.Apply(), Is.True, "OK wrote the seal");
        }

        /// <summary>A new database holding an authority made by Create.</summary>
        private TemporaryDatabase Seeded()
        {
            var database = TemporaryDatabase.New();
            _databases.Add(database);
            var created = DatabaseAuthority.Of(database.Database, null).Create();
            Assume.That(created.Succeeded, Is.True, created.Message);
            return database;
        }

        /// <summary>Start store again pressed on the tab's buffer over the live database, and the dialog's OK.</summary>
        private static void StartAtOk(TemporaryDatabase database)
        {
            var writer = new DatabaseWriter(CurrentThread.Instance, saved => { }, null, null, NullGateLog.Instance);
            var pending = new PendingDatabase(new OpenDatabase(database.Database, delegate { }, writer, NullGateLog.Instance), NullGateLog.Instance);
            var pressed = new AuthorityService(pending.Store, NullGateLog.Instance).StartAgain();
            Assert.That(pressed.Succeeded, Is.True, pressed.Message);
            Assert.That(pending.Apply(), Is.True, "OK wrote the start");
        }

        /// <summary>A record of kind `client` for a client key of this digit, written under the key that stands; its identifier.</summary>
        private static string Paired(TemporaryDatabase database, char digit)
        {
            var client = new string(digit, 64);
            StoreSeeds.Client(Store(database), new Dictionary<string, string> { { "client", client }, { "certificate", client } });
            using (var reading = Store(database).Read())
            {
                return reading.InForce.Records.Single(record => record.Is(StoreKinds.Client) && record.Form.Member(StoreKinds.ClientMember) == client).Hex;
            }
        }

        private static string KeyOf(TemporaryDatabase database)
        {
            using (var reading = Store(database).Read())
            {
                return reading.InForce.Key.Name;
            }
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

        private static IEnumerable<PwEntry> Entries(TemporaryDatabase database, string kind)
        {
            return database.RootGroup.GetEntries(true).Where(entry => entry.Strings.ReadSafe(StoreComposer.Kind) == kind).ToList();
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
