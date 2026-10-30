using System;
using System.Collections.Generic;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Testing;
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
        public void A_walk_taken_while_the_tree_and_the_rules_move_raises_nothing()
        {
            // Why the resolver's walk sits outside `KdbxVault.Reading` and loses nothing by it.
            // The node is found through that retry, but `IAclNode.Document` and `IAclNode.Parent`
            // are read as the walk reaches them, which is after it has returned. What the retry
            // takes again is an enumerator or a copy that met a tree in motion, and a walk is
            // neither: two field reads per level and one lookup in a sorted dictionary. An
            // exception escaping here would reach the catch-all on the connection thread, which
            // answers 500 and records a defect in this plugin for somebody typing in KeePass.
            var churnedGroup = GroupUnderRoot("Fictitious");
            var otherGroup = GroupUnderRoot("Also Fictitious");
            var churned = EntryIn(churnedGroup);

            // A second chain nothing writes to, walked on every pass beside the first one. It
            // says the walk really is climbing and really is parsing what it finds, which a run
            // that only counted refusals could not tell from a walk that stopped at the entry.
            var quietGroup = GroupUnderRoot("Undisturbed");
            Grant(quietGroup, AclDefault.Inherit, "reader", AclVerb.Read);
            var quiet = EntryIn(quietGroup);

            // Read and written through `Volatile` rather than plainly, because a flag a loop only
            // ever reads is a flag the jit may hoist out of it, and the failure that buys is a
            // suite that hangs rather than one that reports.
            var stop = false;
            Exception churnFailure = null;

            var churn = new Thread(() =>
            {
                churnFailure = Thrown.By(() =>
                {
                    var round = 0;
                    while (!Volatile.Read(ref stop))
                    {
                        Grant(churned, AclDefault.Inherit, "reader", AclVerb.Read);
                        Grant(churnedGroup, AclDefault.Deny, "reader", AclVerb.Read);
                        Grant(otherGroup, AclDefault.Inherit, "editor", AclVerb.Update);

                        // Enough keys either side of the rule to make the dictionary rebalance
                        // around it, which is the moment a lookup would have to survive.
                        churned.CustomData.Set("KPG.Fictitious." + (round % 64), "x");
                        churnedGroup.CustomData.Set("KPG.Fictitious." + (round % 64), "x");

                        churned.CustomData.Remove(AclDocument.CustomDataKey);
                        churnedGroup.CustomData.Remove(AclDocument.CustomDataKey);
                        otherGroup.CustomData.Remove(AclDocument.CustomDataKey);
                        churned.CustomData.Remove("KPG.Fictitious." + ((round + 9) % 64));
                        churnedGroup.CustomData.Remove("KPG.Fictitious." + ((round + 9) % 64));

                        // And the tree itself moving under the walk, so that the parent read is
                        // as contended as the rule read.
                        churned.ParentGroup.Entries.Remove(churned);
                        ((round % 2) == 0 ? otherGroup : churnedGroup).AddEntry(churned, true);

                        round++;
                    }
                });
            });

            churn.Start();

            Exception escaped = null;
            var quietRefusals = 0;
            var walks = 0;

            escaped = Thrown.By(() =>
            {
                for (; walks < 100000; walks++)
                {
                    AclResolver.Permits(KdbxNodes.Of(churned), Reader, AclVerb.Read);

                    if (!AclResolver.Permits(KdbxNodes.Of(quiet), Reader, AclVerb.Read))
                    {
                        quietRefusals++;
                    }
                }
            });

            Volatile.Write(ref stop, true);
            churn.Join();

            Assert.That(escaped, Is.Null,
                "a walk over a tree in motion raises nothing, so nothing is left for a retry to take again");
            Assert.That(churnFailure, Is.Null, "and the thread doing the writing got through its own work");
            Assert.That(quietRefusals, Is.Zero,
                "the chain nobody touched answered the same way on all " + walks + " passes");
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
