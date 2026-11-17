using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own merges do to the authority in the plugin's store, against real
    /// databases.
    /// </summary>
    /// <remarks>
    /// A retirement, once written, stays wherever the copy that wrote it takes part, so an
    /// authority a stale copy brings back is served by nothing. What cannot be decided that way is
    /// two authorities made apart, and those arrive side by side for the operator rather than
    /// either winning on a clock.
    ///
    /// **Create new IDs brings no record**: every entry of the other copy arrives under another
    /// identifier, where it is a copy the store passes over, so the copy imported into stays as it
    /// was, `STORE.md` §621 and §640. Each merge here is from the other copy's file opened afresh,
    /// as KeePass's import and synchronize open it, since `PwDatabase.MergeIn` with Create new IDs
    /// gives the source's own items new identifiers in place.
    /// </remarks>
    [TestFixture]
    public sealed class AuthorityMergeTests
    {
        private static readonly PwMergeMethod[] Methods =
        {
            PwMergeMethod.Synchronize,
            PwMergeMethod.OverwriteIfNewer,
            PwMergeMethod.OverwriteExisting,
            PwMergeMethod.CreateNewUuids
        };

        [Test]
        public void Two_copies_that_each_recreate_meet_as_a_choice_between_the_two()
        {
            foreach (var method in Methods)
            {
                using (var first = TemporaryDatabase.New())
                {
                    Assume.That(Authority(first).Create().Succeeded, Is.True);
                    var original = Digest(first);

                    using (var second = CopyOf(first))
                    {
                        Assume.That(Authority(first).Recreate().Succeeded, Is.True);
                        Assume.That(Authority(second).Recreate().Succeeded, Is.True);
                        var mine = Digest(first);
                        var theirs = Digest(second);

                        MergeIn(first, second, method);

                        if (method == PwMergeMethod.CreateNewUuids)
                        {
                            Assert.That(Digest(first), Is.EqualTo(mine), method + ": the copy imported into stays as it was");
                            continue;
                        }

                        using (var reading = Authority(first).Read())
                        {
                            Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided), method.ToString());
                            Assert.That(reading.Candidates.Select(c => c.Fingerprint),
                                Is.EquivalentTo(new[] { mine, theirs }),
                                method + ": both new ones, and never the one both copies retired");
                            Assert.That(reading.Candidates.Select(c => c.Fingerprint), Does.Not.Contain(original));
                        }
                    }
                }
            }
        }

        [Test]
        public void Three_copies_that_each_recreate_meet_as_a_choice_between_the_three()
        {
            using (var first = TemporaryDatabase.New())
            {
                Assume.That(Authority(first).Create().Succeeded, Is.True);

                using (var second = CopyOf(first))
                using (var third = CopyOf(first))
                {
                    foreach (var copy in new[] { first, second, third })
                    {
                        Assume.That(Authority(copy).Recreate().Succeeded, Is.True);
                    }

                    var expected = new[] { Digest(first), Digest(second), Digest(third) };

                    MergeIn(first, second, PwMergeMethod.Synchronize);
                    MergeIn(third, first, PwMergeMethod.Synchronize);

                    using (var reading = Authority(third).Read())
                    {
                        Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Undecided));
                        Assert.That(reading.Candidates.Select(c => c.Fingerprint), Is.EquivalentTo(expected));
                    }
                }
            }
        }

        [Test]
        public void A_choice_survives_a_merge_with_each_copy_that_lost_in_either_direction()
        {
            foreach (var method in Methods)
            {
                foreach (var intoChooser in new[] { true, false })
                {
                    using (var chooser = TemporaryDatabase.New())
                    {
                        Assume.That(Authority(chooser).Create().Succeeded, Is.True);

                        using (var second = CopyOf(chooser))
                        using (var third = CopyOf(chooser))
                        {
                            foreach (var copy in new[] { chooser, second, third })
                            {
                                Assume.That(Authority(copy).Recreate().Succeeded, Is.True);
                            }

                            var kept = Digest(chooser);
                            MergeIn(chooser, second, PwMergeMethod.Synchronize);
                            MergeIn(chooser, third, PwMergeMethod.Synchronize);

                            var choice = Keep(chooser, kept);
                            Assume.That(choice.Succeeded, Is.True, choice.Message);

                            // Each loser as it was before it heard of the choice, which is exactly
                            // what a copy synchronized later still is.
                            foreach (var loser in new[] { second, third })
                            {
                                var target = intoChooser ? chooser : loser;
                                var source = intoChooser ? loser : chooser;

                                // Create new IDs leaves a copy that lost as it was, serving its own.
                                var expected = method == PwMergeMethod.CreateNewUuids ? Digest(target) : kept;
                                MergeIn(target, source, method);

                                using (var reading = Authority(target).Read())
                                {
                                    var named = method + (intoChooser ? ", into the copy that chose" : ", into a copy that lost");
                                    Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present), named);
                                    Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(expected), named);
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void Copies_that_keep_different_authorities_retire_every_authority_and_read_as_retired()
        {
            // Three copies reach the same choice between three authorities before any answer is
            // synchronized, which is machines sharing a file whose synchronize ran before anybody
            // answered. The first and the third keep the first copy's authority, and the second
            // keeps its own: every answer retires what it did not keep, so the two different answers
            // between them retire all three, the authority two copies agreed on among them. The
            // reading reports every authority as retired, rather than as never having been made.
            foreach (var method in Methods)
            {
                using (var first = TemporaryDatabase.New())
                {
                    Assume.That(Authority(first).Create().Succeeded, Is.True);

                    using (var second = CopyOf(first))
                    using (var third = CopyOf(first))
                    {
                        var copies = new[] { first, second, third };
                        foreach (var copy in copies)
                        {
                            Assume.That(Authority(copy).Recreate().Succeeded, Is.True);
                        }

                        var agreed = Digest(first);
                        var own = Digest(second);

                        MergeIn(first, second, PwMergeMethod.Synchronize);
                        MergeIn(first, third, PwMergeMethod.Synchronize);
                        MergeIn(second, first, PwMergeMethod.Synchronize);
                        MergeIn(third, first, PwMergeMethod.Synchronize);

                        var choices = new[]
                        {
                            Keep(first, agreed),
                            Keep(second, own),
                            Keep(third, agreed)
                        };

                        foreach (var choice in choices)
                        {
                            Assume.That(choice.Succeeded, Is.True, choice.Message);
                        }

                        MergeIn(first, second, method);
                        MergeIn(first, third, method);

                        if (method == PwMergeMethod.CreateNewUuids)
                        {
                            Assert.That(Digest(first), Is.EqualTo(agreed), method + ": the copy imported into stays as it was");
                            continue;
                        }

                        using (var reading = Authority(first).Read())
                        {
                            Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None), method.ToString());
                            Assert.That(reading.IsRetired, Is.True, method + ": every authority it held is retired");
                        }
                    }
                }
            }
        }

        [Test]
        public void A_deleted_authority_stays_deleted_whatever_copy_is_merged_in()
        {
            foreach (var method in Methods)
            {
                using (var deleted = TemporaryDatabase.New())
                {
                    Assume.That(Authority(deleted).Create().Succeeded, Is.True);

                    using (var stale = CopyOf(deleted))
                    {
                        var before = Digest(stale);
                        Assume.That(Authority(deleted).Delete().Succeeded, Is.True);

                        MergeIn(deleted, stale, method);
                        MergeIn(stale, deleted, method);

                        // Create new IDs brings nothing into the stale copy either, which goes on
                        // serving the authority it held: the import fails to bring it up to date.
                        var copies = method == PwMergeMethod.CreateNewUuids ? new[] { deleted } : new[] { deleted, stale };
                        foreach (var copy in copies)
                        {
                            using (var reading = Authority(copy).Read())
                            {
                                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None), method.ToString());
                                Assert.That(reading.IsRetired, Is.True,
                                    method + ": and reads as retired, rather than as never having had an authority");
                            }
                        }

                        if (method == PwMergeMethod.CreateNewUuids)
                        {
                            Assert.That(Digest(stale), Is.EqualTo(before), method + ": the stale copy stays as it was");
                        }
                    }
                }
            }
        }

        [Test]
        public void A_retired_authority_a_stale_copy_brings_back_serves_nothing_and_the_next_write_deletes_it_again()
        {
            foreach (var method in Methods.Where(method => method != PwMergeMethod.CreateNewUuids))
            {
                using (var first = TemporaryDatabase.New())
                {
                    Assume.That(Authority(first).Create().Succeeded, Is.True);
                    var old = Digest(first);

                    using (var stale = CopyOf(first))
                    {
                        Assume.That(Authority(first).Recreate().Succeeded, Is.True);
                        var current = Digest(first);

                        MergeIn(first, stale, method);

                        Assert.That(Digest(first), Is.EqualTo(current), method + ": the authority served is the authority recreated");

                        Assume.That(Authority(first).Recreate().Succeeded, Is.True);
                        Assert.That(EntriesHolding(first, old), Is.Zero, method + ": and no entry holds the retired authority after the next write");
                    }
                }
            }
        }

        /// <summary>How many entries of the database hold an authority's record filed under this `id`.</summary>
        private static int EntriesHolding(TemporaryDatabase database, string id)
        {
            return database.RootGroup.GetEntries(true).Count(entry =>
                entry.Strings.ReadSafe("KPG.Kind") == "authority" && entry.Strings.ReadSafe("KPG.Data").Contains(id));
        }

        /// <summary>
        /// A second copy of one database, as a file copied to another machine is: the original
        /// saved, and its file opened again in a directory of its own.
        /// </summary>
        private static TemporaryDatabase CopyOf(TemporaryDatabase original)
        {
            original.Save();

            var copy = TemporaryDatabase.New();
            System.IO.File.Copy(original.Path, copy.Path, true);
            copy.Reopen();
            return copy;
        }

        /// <summary>
        /// Merges the source copy into the target from the source's file opened afresh, as
        /// KeePass's import and synchronize open the other copy, so that the source as it stands
        /// in memory is not what the merge changes.
        /// </summary>
        private static void MergeIn(TemporaryDatabase target, TemporaryDatabase source, PwMergeMethod method)
        {
            using (var opened = CopyOf(source))
            {
                target.Database.MergeIn(opened.Database, method, null);
            }
        }

        private static AuthorityService Authority(TemporaryDatabase database)
        {
            return DatabaseAuthority.Of(database.Database, NullGateLog.Instance);
        }

        /// <summary>
        /// Keeps an authority as an operator shown every authority the copy holds would.
        /// </summary>
        private static AuthorityChange Keep(TemporaryDatabase database, string fingerprint)
        {
            var authority = Authority(database);

            string[] offered;
            using (var reading = authority.Read())
            {
                offered = reading.Candidates.Select(candidate => candidate.Fingerprint).ToArray();
            }

            return authority.Keep(fingerprint, offered);
        }

        /// <summary>The one authority a copy serves, which the caller expects there to be.</summary>
        private static string Digest(TemporaryDatabase database)
        {
            using (var reading = Authority(database).Read())
            {
                Assert.That(reading.IsPresent, Is.True, "expected exactly one authority, and found " + reading.Presence);
                return reading.Material.AuthorityDigest;
            }
        }
    }
}
