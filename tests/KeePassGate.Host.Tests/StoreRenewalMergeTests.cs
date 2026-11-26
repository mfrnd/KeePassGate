using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Standing answers to a renewal given apart in copies of a real database that the product
    /// wrote, brought together by KeePass's own merge, `STORE.md` §618 and §639: what the copy
    /// merged into answers about the client once the copies have met.
    /// </summary>
    /// <remarks>
    /// **Written by the product's own acts**, `RenewalActs`, each naming what the copy it is
    /// written in holds standing as it is read there, through the store's write, and saved. No
    /// record is composed here, but where a cell needs an entry deleted by hand, which no act does.
    ///
    /// **Brought together by `PwDatabase.MergeIn`**, the copy merged from opened afresh from its
    /// file as KeePass opens the other file, under each method KeePass has, in each direction, and
    /// under Synchronize with the entries of the store of either copy dated a day later, which
    /// decides whether a deletion record applies. The copy merged into is read through its file
    /// after the merge, and read again after a write of the plugin's own that touches no answer,
    /// a profile name added, which deletes what a merge brought back beside the version replacing
    /// it, `STORE.md` §627.
    ///
    /// **Held to the answer `STORE.md` §618 gives**, written down beside each sequence and never
    /// read from the product: the stricter of two answers made apart, a lift made without having
    /// seen a refusal losing to it. An import under new identifiers is passed over by the copy
    /// merged into, which keeps its own answer (§640).
    /// </remarks>
    [TestFixture]
    public sealed class StoreRenewalMergeTests
    {
        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero);
        private static readonly DateTimeOffset Earlier = new DateTimeOffset(2026, 12, 14, 23, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset Later = new DateTimeOffset(2027, 3, 31, 22, 0, 0, TimeSpan.Zero);

        private static readonly PwMergeMethod[] Methods =
        {
            PwMergeMethod.Synchronize,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.KeepExisting,
            PwMergeMethod.CreateNewUuids,
        };

        private enum Times
        {
            AsWritten,
            IntoLater,
            FromLater,
        }

        [Test]
        public void Forever_in_a_copy_and_nothing_in_the_other_refuses_wherever_the_record_arrives()
        {
            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Forever());
                world.Cells(
                    (into, method) => into == 0 || method != PwMergeMethod.CreateNewUuids ? "forever" : "ask",
                    "forever in copy 0");
            }
        }

        [Test]
        public void Forever_beside_until_a_moment_made_apart_gives_forever_and_two_moments_the_later()
        {
            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Forever());
                world.Refuse(1, RenewalRefusal.Until(Later));
                world.Cells(
                    (into, method) => method != PwMergeMethod.CreateNewUuids ? "forever" : into == 0 ? "forever" : "until:2027-03-31T22:00:00Z",
                    "forever in copy 0, until the later moment in copy 1");
            }

            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Until(Earlier));
                world.Refuse(1, RenewalRefusal.Until(Later));
                world.Cells(
                    (into, method) => method != PwMergeMethod.CreateNewUuids || into == 1 ? "until:2027-03-31T22:00:00Z" : "until:2026-12-14T23:00:00Z",
                    "until the earlier moment in copy 0, until the later in copy 1");
            }
        }

        [Test]
        public void A_refusal_both_copies_hold_lifted_in_a_copy_asks_where_the_lift_arrives_and_stays_refused_where_it_does_not()
        {
            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Forever());
                world.Merge(1, 0, PwMergeMethod.Synchronize);
                Assert.That(world.Answer(1), Is.EqualTo("forever"), "copy 1 holds the refusal");
                world.Lift(1);
                Assert.That(world.Answer(0), Is.EqualTo("forever"), "copy 0 has not received the lift");
                world.Cells(
                    (into, method) => into == 0 && method == PwMergeMethod.CreateNewUuids ? "forever" : "ask",
                    "a refusal both hold, lifted in copy 1");
            }
        }

        [Test]
        public void A_lift_in_a_copy_that_never_held_the_other_copy_s_refusal_loses_to_it_once_they_meet()
        {
            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Until(Earlier));
                world.Merge(1, 0, PwMergeMethod.Synchronize);
                world.Refuse(0, RenewalRefusal.Forever());
                world.Lift(1);
                Assert.That(world.Answer(1), Is.EqualTo("ask"));
                world.Cells(
                    (into, method) => into == 1 && method == PwMergeMethod.CreateNewUuids ? "ask" : "forever",
                    "forever in copy 0 that copy 1 never held, the earlier answer lifted in copy 1");
            }
        }

        [Test]
        public void An_answer_deleted_by_hand_refuses_while_a_carrier_or_a_deletion_record_is_left_and_is_asked_again_where_none_is()
        {
            // `STORE.md` §618 and §639 row 5: a version deleted while a summary entry in force
            // restates it is not lost and stands; deleted with that summary entry and their
            // deletion records the store stops; deleted with both carriers and no deletion record,
            // nothing of it is left and the client is asked about again, the accepted class.
            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Forever());
                world.DeleteAnswer(0, recorded: true);
                Assert.That(world.Answer(0), Is.EqualTo("forever"), "restated by the summary entry, it stands");
                Assert.That(world.Cause(0), Is.EqualTo(RenewalCause.Stands));
            }

            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Forever());
                world.DeleteAnswer(0, recorded: true);
                world.DeleteNewestHead(0, recorded: true);
                Assert.That(world.State(0), Is.EqualTo(StoreState.Stopped), "a deleted summary entry nothing stands in for");
                Assert.That(world.Answer(0), Is.EqualTo("forever"), "and the answer lost refuses");
                Assert.That(world.Cause(0), Is.EqualTo(RenewalCause.Lost));
            }

            using (var world = new World())
            {
                world.Refuse(0, RenewalRefusal.Forever());
                world.DeleteAnswer(0, recorded: false);
                world.DeleteNewestHead(0, recorded: false);
                Assert.That(world.Answer(0), Is.EqualTo("ask"), "the accepted class of STORE.md §639 row 5: nothing of the answer is left");
            }
        }

        /// <summary>The copies of a database a sequence is played over, with a client admitted in the first before the second is copied.</summary>
        private sealed class World : IDisposable
        {
            private readonly List<TemporaryDatabase> _copies = new List<TemporaryDatabase>();
            private readonly X509Certificate2 _certificate;

            public World()
            {
                var first = TemporaryDatabase.New();
                _copies.Add(first);
                var cheapest = new AesKdf().GetDefaultParameters();
                cheapest.SetUInt64(AesKdf.ParamRounds, 1);
                first.Database.KdfParameters = cheapest;
                var created = DatabaseAuthority.Of(first.Database, NullGateLog.Instance).Create();
                Assert.That(created.Succeeded, Is.True, created.Message);
                _certificate = Minted.Named("CN=agent-fictitious").Lasting(Then.AddDays(-1), Then.AddYears(1)).SelfSigned().Read();
                Client = StoreSeeds.Admit(Store(first), _certificate, Then, "pf").ClientKey;
                first.Save();
                _copies.Add(Opened(first));
            }

            public string Client { get; private set; }

            public void Refuse(int copy, RenewalRefusal refusal)
            {
                StoreSeeds.Refuse(Store(_copies[copy]), Client, refusal);
                _copies[copy].Save();
            }

            public void Lift(int copy)
            {
                StoreSeeds.Lift(Store(_copies[copy]), Client);
                _copies[copy].Save();
            }

            /// <summary>The answer of a copy about the client, as the clients page and the gate read it: `ask`, `forever`, or `until:` and the moment.</summary>
            public string Answer(int copy)
            {
                return Answer(_copies[copy]);
            }

            public RenewalCause Cause(int copy)
            {
                using (var reading = Store(_copies[copy]).Read())
                {
                    return StoredRenewals.Of(reading.InForce, Client).Cause;
                }
            }

            public StoreState State(int copy)
            {
                using (var reading = Store(_copies[copy]).Read())
                {
                    return reading.State;
                }
            }

            /// <summary>The client's standing version deleted for good, with a deletion record or with none.</summary>
            public void DeleteAnswer(int copy, bool recorded)
            {
                string hex;
                using (var reading = Store(_copies[copy]).Read())
                {
                    hex = StoredRenewals.Of(reading.InForce, Client).Standing.Single();
                }

                Delete(_copies[copy], hex, recorded);
            }

            /// <summary>The newest summary entry deleted for good, with a deletion record or with none.</summary>
            public void DeleteNewestHead(int copy, bool recorded)
            {
                string hex;
                using (var reading = Store(_copies[copy]).Read())
                {
                    var top = reading.InForce.Heads.Max(head => head.Form.Number);
                    hex = reading.InForce.Heads.Single(head => head.Form.Number == top).Hex;
                }

                Delete(_copies[copy], hex, recorded);
            }

            /// <summary>Copy <paramref name="from"/> merged into copy <paramref name="into"/>, the copy merged from opened afresh from its file.</summary>
            public void Merge(int into, int from, PwMergeMethod method)
            {
                _copies[from].Save();
                _copies[into].SaveCloseAndReopen();
                using (var source = Opened(_copies[from]))
                {
                    _copies[into].Database.MergeIn(source.Database, method, null);
                }

                _copies[into].SaveCloseAndReopen();
            }

            /// <summary>
            /// Copies 0 and 1 as they stand met once by every single merge, each way, and under
            /// Synchronize with either copy's store dated later: the answer of the copy merged into
            /// held to what <paramref name="expected"/> gives for that cell, after the merge and
            /// after a write of the plugin's own that touches no answer.
            /// </summary>
            public void Cells(Func<int, PwMergeMethod, string> expected, string sequence)
            {
                _copies[0].Save();
                _copies[1].Save();
                var wrong = new List<string>();
                foreach (var method in Methods)
                {
                    foreach (var times in method == PwMergeMethod.Synchronize ? new[] { Times.AsWritten, Times.IntoLater, Times.FromLater } : new[] { Times.AsWritten })
                    {
                        foreach (var (into, from) in new[] { (0, 1), (1, 0) })
                        {
                            var named = sequence + ", " + method + (times == Times.AsWritten ? string.Empty : ", " + times) + ", copy " + from + " into copy " + into;
                            using (var target = Opened(_copies[into]))
                            using (var source = Opened(_copies[from]))
                            {
                                var later = DateTime.UtcNow.AddDays(1);
                                if (times == Times.IntoLater)
                                {
                                    Date(target, later);
                                }
                                else if (times == Times.FromLater)
                                {
                                    Date(source, later);
                                }

                                target.Database.MergeIn(source.Database, method, null);
                                target.SaveCloseAndReopen();
                                var wanted = expected(into, method);
                                var given = Answer(target);
                                if (given != wanted)
                                {
                                    wrong.Add(named + ": " + given + " where " + wanted + " was expected");
                                }

                                using (var reading = Store(target).Read())
                                {
                                    if (reading.State != StoreState.Whole)
                                    {
                                        wrong.Add(named + ": the store is " + reading.State);
                                        continue;
                                    }
                                }

                                StoreSeeds.Name(Store(target), "after-the-merge");
                                target.SaveCloseAndReopen();
                                var again = Answer(target);
                                if (again != wanted)
                                {
                                    wrong.Add(named + ", after a write of the plugin's own: " + again + " where " + wanted + " was expected");
                                }
                            }
                        }
                    }
                }

                Assert.That(wrong, Is.Empty, string.Join(Environment.NewLine, wrong));
            }

            public void Dispose()
            {
                foreach (var copy in _copies)
                {
                    copy.Dispose();
                }

                _certificate.Reset();
            }

            private string Answer(TemporaryDatabase copy)
            {
                using (var reading = Store(copy).Read())
                {
                    var answer = StoredRenewals.Of(reading.InForce, Client).Answer;
                    return answer == null ? "ask" : answer.NotBefore.HasValue ? "until:" + Moments.Wire(answer.NotBefore.Value) : "forever";
                }
            }

            private static void Delete(TemporaryDatabase copy, string hex, bool recorded)
            {
                var entry = copy.Database.RootGroup.FindEntry(new PwUuid(StoreComposer.BytesOf(hex)), true);
                Assert.That(entry, Is.Not.Null, "an entry of the store under " + hex);
                entry.ParentGroup.Entries.Remove(entry);
                if (recorded)
                {
                    copy.Database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
                }

                copy.Save();
            }

            private static TemporaryDatabase Opened(TemporaryDatabase saved)
            {
                var copy = TemporaryDatabase.New();
                File.Copy(saved.Path, copy.Path, true);
                copy.Reopen();
                return copy;
            }

            /// <summary>Every entry of the plugin's group, and every deletion record, dated to this moment.</summary>
            private static void Date(TemporaryDatabase database, DateTime when)
            {
                var group = database.Database.RootGroup.Groups.Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
                foreach (var entry in group.Entries)
                {
                    entry.LastModificationTime = when;
                    entry.LocationChanged = when;
                }

                foreach (var deleted in database.Database.DeletedObjects)
                {
                    deleted.DeletionTime = when;
                }
            }

            private static KdbxStore Store(TemporaryDatabase copy)
            {
                return new KdbxStore(copy.Database, GateVersion.Current, NullGateLog.Instance);
            }
        }
    }
}
