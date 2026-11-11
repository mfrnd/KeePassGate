using System;
using System.Collections.Generic;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The rules against a real KeePass tree, written into a real KDBX4 file and read back out.
    /// </summary>
    /// <remarks>
    /// `AclResolverTests` settles what the rules mean, on a tree that is invented. This asks what
    /// only KeePass can answer: **an entry carries custom data at all**, which is a KDBX4 feature
    /// and not a KDBX3 one, so a grant written on an entry is a grant that has somewhere to live;
    /// **a key nobody wrote reads back as nothing**; and **the parent chain reaches the root**,
    /// because everything about inheritance depends on `ParentGroup` being set on both entries and
    /// groups by the calls that put them in a tree. Then it runs the resolver's own rules over a
    /// real tree to see them hold there: nearest wins, a seal, a tree with no rule granting nothing,
    /// the rules surviving a save and a reopen, a decision naming its node by identifier rather
    /// than by title, and a walk taken while another thread rewrites the rules.
    ///
    /// Everything in here is invented: the groups, the entries, the profile names. Nothing reads or
    /// resembles the author's own database.
    /// </remarks>
    [TestFixture]
    public sealed class AclInRealDatabaseTests
    {
        private static readonly string[] Reader = { "reader" };
        private static readonly string[] Editor = { "editor" };

        private TemporaryDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_grant_on_a_group_reaches_an_entry_inside_it()
        {
            var group = GroupUnderRoot("Fictitious");
            Grant(group, AclDefault.Inherit, "reader", AclVerb.Read);
            var entry = EntryIn(group);

            Assert.That(AclResolver.Permits(KdbxNodes.Of(entry), Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(entry), Reader, AclVerb.Update), Is.False);
        }

        [Test]
        public void A_grant_on_an_entry_wins_over_the_group_it_is_in()
        {
            // Which is only possible because an entry has custom data of its own, and that is a
            // KDBX4 feature. On a KDBX3 file there would be nowhere to write this.
            var group = GroupUnderRoot("Fictitious");
            Grant(group, AclDefault.Inherit, "reader", AclVerb.Read, AclVerb.Update);
            var entry = EntryIn(group);
            Grant(entry, AclDefault.Inherit, "reader", AclVerb.Read);

            Assert.That(AclResolver.Permits(KdbxNodes.Of(entry), Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(entry), Reader, AclVerb.Update), Is.False,
                "the nearer rule says less, and nearest wins");
        }

        [Test]
        public void The_chain_runs_from_an_entry_all_the_way_to_the_root_group()
        {
            // A rule at the top of a database reaches the bottom of it, which is the whole of what
            // inheritance is for and depends entirely on KeePass setting `ParentGroup`.
            Grant(_database.RootGroup, AclDefault.Inherit, "reader", AclVerb.Read);

            var deep = EntryIn(GroupUnder(GroupUnder(GroupUnderRoot("One"), "Two"), "Three"));

            Assert.That(AclResolver.Permits(KdbxNodes.Of(deep), Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_group_can_seal_a_subtree_off_from_what_the_root_granted()
        {
            Grant(_database.RootGroup, AclDefault.Inherit, "reader", AclVerb.Read);

            var open = GroupUnderRoot("Open");
            var closed = GroupUnderRoot("Closed");
            Grant(closed, AclDefault.Deny, "editor", AclVerb.Read);

            Assert.That(AclResolver.Permits(KdbxNodes.Of(EntryIn(open)), Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(EntryIn(closed)), Reader, AclVerb.Read), Is.False);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(EntryIn(closed)), Editor, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_grant_survives_being_saved_and_opened_again()
        {
            // Believed only after a save, a close and a reopen, because a value present in memory
            // and a value in the file are two different claims and this repository has been caught
            // by the difference before.
            var group = GroupUnderRoot("Fictitious");
            Grant(group, AclDefault.Deny, "reader", AclVerb.Read);
            var entry = EntryIn(group);
            Grant(entry, AclDefault.Inherit, "editor", AclVerb.Read, AclVerb.Update);

            var groupUuid = group.Uuid;
            var entryUuid = entry.Uuid;

            _database.SaveCloseAndReopen();

            var reopened = _database.RootGroup.FindEntry(entryUuid, true);
            Assert.That(reopened, Is.Not.Null, "the entry came back");
            Assert.That(_database.RootGroup.FindGroup(groupUuid, true), Is.Not.Null, "and so did its group");

            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), Editor, AclVerb.Update), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), Reader, AclVerb.Read), Is.True);
            Assert.That(AclResolver.Permits(KdbxNodes.Of(reopened), new[] { "nobody" }, AclVerb.Read), Is.False,
                "and so did the group's refusal of anything it does not name");
        }

        [Test]
        public void A_node_is_named_by_its_identifier_and_never_by_its_title()
        {
            // Refusals reach a log, and a log is meant to be readable by somebody who is not
            // entitled to know what the database holds. A group is called "Banking" and an entry is
            // called something worse.
            var group = GroupUnderRoot("Something Revealing");
            Grant(group, AclDefault.Deny, "editor", AclVerb.Read);
            var entry = EntryIn(group);
            entry.Strings.Set(PwDefs.TitleField, new KeePassLib.Security.ProtectedString(false, "Also Revealing"));

            var decision = AclResolver.Decide(KdbxNodes.Of(entry), Reader, AclVerb.Read);

            Assert.That(decision.Permitted, Is.False);
            Assert.That(decision.DecidedAt, Is.EqualTo(group.Uuid.ToHexString()),
                "the identifier, which is what a log may carry");
            Assert.That(decision.Reason, Does.Not.Contain("Revealing"),
                "and the reason is the resolver's own words, never the node's");
        }

        [Test]
        public void A_tree_nobody_has_written_a_rule_on_grants_nothing()
        {
            // The state every database is in on the day this plugin is installed. It is not a
            // database that cannot be reached, it is a database nothing may reach until somebody
            // says otherwise.
            var entry = EntryIn(GroupUnderRoot("Fictitious"));

            foreach (var verb in new[] { AclVerb.Read, AclVerb.Update, AclVerb.Create, AclVerb.Delete })
            {
                Assert.That(AclResolver.Permits(KdbxNodes.Of(entry), Reader, verb), Is.False, AclVerbs.Write(verb));
            }
        }

        [Test]
        public void A_node_nobody_wrote_a_key_on_reads_back_as_nothing()
        {
            // Measured rather than assumed, because the resolver's walk turns on the difference:
            // nothing means carry on up the tree, and a document that will not read means stop. On
            // this KeePass an absent key is null, and a key left blank is treated the same way.
            var group = GroupUnderRoot("Fictitious");
            var entry = EntryIn(group);

            Assert.That(group.CustomData.Get(AclDocument.CustomDataKey), Is.Null);
            Assert.That(entry.CustomData.Get(AclDocument.CustomDataKey), Is.Null);

            Assert.That(KdbxNodes.Of(group).Document, Is.Null);
            Assert.That(KdbxNodes.Of(entry).Document, Is.Null);
        }

        [Test]
        public void A_node_that_is_not_there_is_nothing_to_decide_about()
        {
            Assert.That(KdbxNodes.Of((PwEntry)null), Is.Null);
            Assert.That(KdbxNodes.Of((PwGroup)null), Is.Null);
        }

        [Test]
        public void The_root_group_is_the_top_of_the_chain()
        {
            Assert.That(KdbxNodes.Of(_database.RootGroup).Parent, Is.Null);
        }

        [Test]
        public void A_walk_held_still_finds_a_seal_while_KeePass_rewrites_the_keys_around_it()
        {
            // A walk reads each level's rule out of a sorted dictionary, and a lookup racing a
            // rebalance on KeePass's thread misses a key rather than throwing: a seal missed that
            // way sends the walk on to the grant above it, which the seal exists to stop. Held, the
            // rewrite waits for the walk. Rewritten on a real message loop, as KeePass rewrites
            // custom data, in bursts on a timer: keys either side of the rule put in and taken out
            // until the dictionary turns over around it, and the entry moved between two groups
            // that both grant what the seal refuses.
            var granting = GroupUnderRoot("Fictitious");
            var alsoGranting = GroupUnderRoot("Also Fictitious");
            Grant(granting, AclDefault.Inherit, "reader", AclVerb.Read);
            Grant(alsoGranting, AclDefault.Inherit, "reader", AclVerb.Read);
            var sealedEntry = EntryIn(granting);
            Grant(sealedEntry, AclDefault.Deny, "reader");

            // A second chain nothing writes to, walked on every pass beside the first one. It
            // says the walk really is climbing and really is parsing what it finds, which a run
            // that only counted refusals could not tell from a walk that stopped at the entry.
            var quietGroup = GroupUnderRoot("Undisturbed");
            Grant(quietGroup, AclDefault.Inherit, "reader", AclVerb.Read);
            var quiet = EntryIn(quietGroup);

            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control, KeePassMerges.Watch())))
            using (var churn = loop.Own(() => new System.Windows.Forms.Timer { Interval = 1 }))
            {
                var round = 0;
                var changing = 0;
                var watched = new Watched(ui.Value, () => Volatile.Read(ref changing) != 0);

                loop.Invoke(() =>
                {
                    churn.Value.Tick += delegate
                    {
                        Volatile.Write(ref changing, 1);
                        for (var i = 0; i < 64; i++)
                        {
                            sealedEntry.CustomData.Set("KPG.Fictitious." + i, "x");
                        }

                        for (var i = 0; i < 64; i++)
                        {
                            sealedEntry.CustomData.Remove("KPG.Fictitious." + i);
                        }

                        sealedEntry.ParentGroup.Entries.Remove(sealedEntry);
                        ((round % 2) == 0 ? alsoGranting : granting).AddEntry(sealedEntry, true);
                        round++;
                        Volatile.Write(ref changing, 0);
                    };
                    churn.Value.Start();
                });

                var walks = 0;
                var passedTheSeal = 0;
                var quietRefusals = 0;
                var until = DateTime.UtcNow.AddSeconds(2);
                while (DateTime.UtcNow < until)
                {
                    // Anything leaving here fails the test by escaping, which is an assertion
                    // too: it would reach the catch-all on the connection thread as a defect.
                    if (AclResolver.Permits(KdbxNodes.Of(sealedEntry, watched), Reader, AclVerb.Read))
                    {
                        passedTheSeal++;
                    }

                    if (!AclResolver.Permits(KdbxNodes.Of(quiet, watched), Reader, AclVerb.Read))
                    {
                        quietRefusals++;
                    }

                    walks++;
                }

                Assume.That(Volatile.Read(ref round), Is.GreaterThan(20), "KeePass's thread really was rewriting the rules");
                Assume.That(walks, Is.GreaterThan(100), "and the walks really were being served beside it");
                Assert.That(watched.Holds, Is.EqualTo(2 * walks), "each walk was one hold, however many levels it climbed");
                Assert.That(watched.Overlapped, Is.Zero, "and no walk ran while KeePass's thread was rewriting the rules");
                Assert.That(passedTheSeal, Is.Zero, "the seal was found on every one of " + walks + " walks");
                Assert.That(quietRefusals, Is.Zero, "and the chain nobody touched answered the same way on every one");
            }
        }

        /// <summary>
        /// The hold a walk asks for, counting how often it was asked and whether what it held ever
        /// overlapped KeePass's thread changing the rules.
        /// </summary>
        private sealed class Watched : IReadHold
        {
            private readonly IReadHold _hold;
            private readonly Func<bool> _changing;

            public Watched(IReadHold hold, Func<bool> changing)
            {
                _hold = hold;
                _changing = changing;
            }

            public int Holds { get; private set; }

            public int Overlapped { get; private set; }

            public T Hold<T>(Func<T> read)
            {
                return _hold.Hold(() =>
                {
                    Holds++;
                    var before = _changing();
                    var answer = read();
                    if (before || _changing())
                    {
                        Overlapped++;
                    }

                    return answer;
                });
            }
        }

        private PwGroup GroupUnderRoot(string name)
        {
            return GroupUnder(_database.RootGroup, name);
        }

        private static PwGroup GroupUnder(PwGroup parent, string name)
        {
            var group = new PwGroup(true, true, name, PwIcon.Folder);
            parent.AddGroup(group, true);
            return group;
        }

        private static PwEntry EntryIn(PwGroup group)
        {
            var entry = new PwEntry(true, true);
            group.AddEntry(entry, true);
            return entry;
        }

        private static void Grant(PwGroup group, AclDefault fallback, string profile, params AclVerb[] verbs)
        {
            group.CustomData.Set(AclDocument.CustomDataKey, Document(fallback, profile, verbs));
        }

        private static void Grant(PwEntry entry, AclDefault fallback, string profile, params AclVerb[] verbs)
        {
            entry.CustomData.Set(AclDocument.CustomDataKey, Document(fallback, profile, verbs));
        }

        private static string Document(AclDefault fallback, string profile, IEnumerable<AclVerb> verbs)
        {
            return AclDocument.Of(
                fallback,
                new Dictionary<string, IEnumerable<AclVerb>> { { profile, verbs } }).Write();
        }
    }
}
