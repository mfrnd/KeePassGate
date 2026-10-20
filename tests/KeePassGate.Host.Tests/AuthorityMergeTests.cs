using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What KeePass's own merges do to the authority, one key per authority and one retirement per
    /// authority replaced, against real databases.
    /// </summary>
    /// <remarks>
    /// The layout rests on one rule `CustomDataMergeRuleTests` pins: a merge never removes a key
    /// the target holds and never reads a value. So a retirement, once written, stays wherever the
    /// copy that wrote it takes part, and a document a stale copy brings back is served by
    /// nothing. What cannot be decided that way is two authorities made apart, and those arrive
    /// side by side for the operator rather than one of them winning on a clock.
    ///
    /// Every method that copies custom data at all is run: `KeepExisting` copies none, so it
    /// teaches the target nothing and changes nothing, which is the probe's "not learned".
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

                        first.Database.MergeIn(second.Database, method, null);

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

                    first.Database.MergeIn(second.Database, PwMergeMethod.Synchronize, null);
                    third.Database.MergeIn(first.Database, PwMergeMethod.Synchronize, null);

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
                            chooser.Database.MergeIn(second.Database, PwMergeMethod.Synchronize, null);
                            chooser.Database.MergeIn(third.Database, PwMergeMethod.Synchronize, null);

                            var choice = Authority(chooser).Keep(kept);
                            Assume.That(choice.Succeeded, Is.True, choice.Message);

                            // Each loser as it was before it heard of the choice, which is exactly
                            // what a copy synchronized later still is.
                            foreach (var loser in new[] { second, third })
                            {
                                var target = intoChooser ? chooser : loser;
                                var source = intoChooser ? loser : chooser;
                                target.Database.MergeIn(source.Database, method, null);

                                using (var reading = Authority(target).Read())
                                {
                                    var named = method + (intoChooser ? ", into the copy that chose" : ", into a copy that lost");
                                    Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.Present), named);
                                    Assert.That(reading.Material.AuthorityDigest, Is.EqualTo(kept), named);
                                }
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void A_stale_copy_brings_a_pruned_document_back_and_it_serves_nothing()
        {
            foreach (var method in Methods)
            {
                using (var current = TemporaryDatabase.New())
                {
                    Assume.That(Authority(current).Create().Succeeded, Is.True);
                    var old = Digest(current);

                    using (var stale = CopyOf(current))
                    {
                        Assume.That(Authority(current).Recreate().Succeeded, Is.True);
                        var replacement = Digest(current);
                        Assume.That(current.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(old)), Is.False,
                            "recreating takes the old document away");

                        current.Database.MergeIn(stale.Database, method, null);
                        current.SaveCloseAndReopen();

                        Assert.That(current.Database.CustomData.Exists(AuthorityKeys.AuthorityKey(old)), Is.True,
                            method + ": a merge never removes a key, so the stale copy's document comes back");
                        Assert.That(Digest(current), Is.EqualTo(replacement),
                            method + ": and the retirement beside it means it is served by nothing");

                        var pruned = new AuthorityStore(new CustomDataStore(current.Database), NullGateLog.Instance).Prune();
                        Assert.That(pruned.Select(p => p.Key), Is.EqualTo(new[] { AuthorityKeys.AuthorityKey(old) }),
                            method + ": and the next write that prunes takes it away again");
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
                        Assume.That(Authority(deleted).Delete().Succeeded, Is.True);

                        deleted.Database.MergeIn(stale.Database, method, null);
                        stale.Database.MergeIn(deleted.Database, method, null);

                        foreach (var copy in new[] { deleted, stale })
                        {
                            using (var reading = Authority(copy).Read())
                            {
                                Assert.That(reading.Presence, Is.EqualTo(AuthorityPresence.None), method.ToString());
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A second copy of one database, as a file copied to another machine is: the same root
        /// group, since KeePass matches groups by identifier, and the same custom data.
        /// </summary>
        private static TemporaryDatabase CopyOf(TemporaryDatabase original)
        {
            var copy = TemporaryDatabase.New();
            copy.Database.RootGroup.Uuid = original.Database.RootGroup.Uuid;

            foreach (var pair in original.Database.CustomData.ToList())
            {
                copy.Database.CustomData.Set(pair.Key, pair.Value);
            }

            return copy;
        }

        private static AuthorityService Authority(TemporaryDatabase database)
        {
            return new AuthorityService(new CustomDataStore(database.Database), NullGateLog.Instance);
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
