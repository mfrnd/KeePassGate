using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The rules against a real KeePass tree, written into the plugin's store of a real KDBX4 file
    /// by the product's own writes and read back out by the gate.
    /// </summary>
    /// <remarks>
    /// `AclResolverTests` settles what the rules mean, on a tree that is invented. This asks what
    /// only KeePass can answer: **the parent chain reaches the root**, because everything about
    /// inheritance depends on `ParentGroup` being set on both entries and groups by the calls that
    /// put them in a tree; **a rule belongs to its node by identifier**, so it follows a group
    /// moved and stays behind when a node is duplicated; and **nothing of a rule is on any node**.
    /// Then it runs the resolver's own rules over a real tree to see them hold there: nearest wins,
    /// a seal, a tree with no rule granting nothing, the rules surviving a save and a reopen, a
    /// decision naming its node by identifier rather than by title, and a walk taken while
    /// KeePass's thread moves the tree around it.
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
        private IGateStore _store;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
            _store = DatabaseAuthority.Store(_database.Database);
            StoreSeeds.Authority(_store);
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
            StoreSeeds.Rule(_store, Id(group), ("reader", "R"));
            var entry = EntryIn(group);

            Assert.That(Permits(entry, Reader, AclVerb.Read), Is.True);
            Assert.That(Permits(entry, Reader, AclVerb.Update), Is.False);
        }

        [Test]
        public void A_grant_on_an_entry_wins_over_the_group_it_is_in()
        {
            var group = GroupUnderRoot("Fictitious");
            StoreSeeds.Rule(_store, Id(group), ("reader", "RU"));
            var entry = EntryIn(group);
            StoreSeeds.Rule(_store, Id(entry), ("reader", "R"));

            Assert.That(Permits(entry, Reader, AclVerb.Read), Is.True);
            Assert.That(Permits(entry, Reader, AclVerb.Update), Is.False,
                "the nearer rule grants less, and nearest wins");
        }

        [Test]
        public void The_chain_runs_from_an_entry_all_the_way_to_the_root_group()
        {
            // A rule at the top of a database reaches the bottom of it, which is the whole of what
            // inheritance is for and depends entirely on KeePass setting `ParentGroup`.
            StoreSeeds.Rule(_store, Id(_database.RootGroup), ("reader", "R"));

            var deep = EntryIn(GroupUnder(GroupUnder(GroupUnderRoot("One"), "Two"), "Three"));

            Assert.That(Permits(deep, Reader, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_group_can_seal_a_subtree_off_from_what_the_root_granted()
        {
            StoreSeeds.Rule(_store, Id(_database.RootGroup), ("reader", "R"));

            var open = GroupUnderRoot("Open");
            var closed = GroupUnderRoot("Closed");
            StoreSeeds.Seal(_store, Id(closed), ("editor", "R"));

            Assert.That(Permits(EntryIn(open), Reader, AclVerb.Read), Is.True);
            Assert.That(Permits(EntryIn(closed), Reader, AclVerb.Read), Is.False);
            Assert.That(Permits(EntryIn(closed), Editor, AclVerb.Read), Is.True);
        }

        [Test]
        public void A_grant_survives_being_saved_and_opened_again()
        {
            // Believed only after a save, a close and a reopen, because a value present in memory
            // and a value in the file are two different claims and this repository has been caught
            // by the difference before.
            var group = GroupUnderRoot("Fictitious");
            StoreSeeds.Seal(_store, Id(group), ("reader", "R"));
            var entry = EntryIn(group);
            StoreSeeds.Rule(_store, Id(entry), ("editor", "RU"));

            var groupUuid = group.Uuid;
            var entryUuid = entry.Uuid;

            _database.SaveCloseAndReopen();

            var reopened = _database.RootGroup.FindEntry(entryUuid, true);
            Assert.That(reopened, Is.Not.Null, "the entry came back");
            Assert.That(_database.RootGroup.FindGroup(groupUuid, true), Is.Not.Null, "and so did its group");

            Assert.That(Permits(reopened, Editor, AclVerb.Update), Is.True);
            Assert.That(Permits(reopened, Reader, AclVerb.Read), Is.True);
            Assert.That(Permits(reopened, new[] { "nobody" }, AclVerb.Read), Is.False,
                "and so did the group's refusal of anything it does not name");
        }

        [Test]
        public void A_group_moved_keeps_its_rule()
        {
            // A rule names its node by identifier, so it goes wherever the node goes, and what it
            // seals stays sealed under a parent that grants.
            var granting = GroupUnderRoot("Granting");
            StoreSeeds.Rule(_store, Id(granting), ("reader", "RU"));
            var moving = GroupUnderRoot("Moving");
            StoreSeeds.Seal(_store, Id(moving), ("reader", "R"));
            var entry = EntryIn(moving);

            _database.RootGroup.Groups.Remove(moving);
            granting.AddGroup(moving, true);

            Assert.That(Permits(entry, Reader, AclVerb.Read), Is.True);
            Assert.That(Permits(entry, Reader, AclVerb.Update), Is.False, "the moved group's own rule answers");
            Assert.That(Permits(entry, Editor, AclVerb.Read), Is.False, "and its seal moved with it");
        }

        [Test]
        public void A_duplicate_holds_no_rule_of_its_own()
        {
            // A copy is a node under another identifier, so it is read under the rule of the place
            // it lands in: an entry its own rule narrowed is reached, as a copy, under its group's.
            var group = GroupUnderRoot("Fictitious");
            StoreSeeds.Rule(_store, Id(group), ("reader", "RU"));
            var entry = EntryIn(group);
            StoreSeeds.Rule(_store, Id(entry), ("reader", "R"));

            var copy = entry.Duplicate();
            group.AddEntry(copy, true);
            var sealedGroup = GroupUnderRoot("Sealed");
            StoreSeeds.Seal(_store, Id(sealedGroup), ("editor", "R"));
            var copiedGroup = sealedGroup.Duplicate();
            _database.RootGroup.AddGroup(copiedGroup, true);

            Assume.That(copy.Uuid.Equals(entry.Uuid), Is.False, "the copy is a node of its own");
            Assert.That(Permits(entry, Reader, AclVerb.Update), Is.False);
            Assert.That(Permits(copy, Reader, AclVerb.Update), Is.True, "the group's rule, not the entry's");
            Assert.That(Permits(EntryIn(copiedGroup), Editor, AclVerb.Read), Is.False, "the copied group carries no rule");
            Assert.That(Rule(copy).Profiles, Is.Empty);
            Assert.That(Rule(copiedGroup).Profiles, Is.Empty);
            Assert.That(Rule(copiedGroup).Switch.Versions, Is.Empty);
        }

        [Test]
        public void A_rule_for_one_node_is_never_read_for_another()
        {
            // Identifiers compared exactly: a group whose identifier differs from another's in a
            // single byte is another node, and its rule reaches nothing of the first.
            var bytes = new PwUuid(true).UuidBytes;
            var near = (byte[])bytes.Clone();
            near[15] ^= 0x01;
            var ruled = new PwGroup(false, true, "Ruled", PwIcon.Folder) { Uuid = new PwUuid(bytes) };
            var other = new PwGroup(false, true, "Other", PwIcon.Folder) { Uuid = new PwUuid(near) };
            _database.RootGroup.AddGroup(ruled, true);
            _database.RootGroup.AddGroup(other, true);
            StoreSeeds.Rule(_store, Id(ruled), ("reader", "R"));

            Assert.That(Permits(EntryIn(ruled), Reader, AclVerb.Read), Is.True);
            Assert.That(Permits(EntryIn(other), Reader, AclVerb.Read), Is.False);
            Assert.That(Rule(other).Profiles, Is.Empty);
        }

        [Test]
        public void A_rule_written_for_the_recycle_bin_or_the_plugin_s_store_reaches_nothing()
        {
            // Neither is reached by the gate, and a rule written for either applies to nothing
            // the gate reaches. The product writes no rule for a node of the store, and a file
            // can hold such a rule all the same.
            var bin = GroupUnderRoot("Bin");
            _database.Database.RecycleBinUuid = bin.Uuid;
            var discarded = EntryIn(bin);
            var plugin = _database.RootGroup.Groups.Single(StoreNodes.Holds);
            var record = plugin.Entries.First();
            StoreSeeds.Rule(_store, Id(bin), ("reader", "CRUSDLAP"));
            DatabaseRules.Crafted(_database.Database, Id(plugin), "reader", "row:CRUSDLAP");
            DatabaseRules.Crafted(_database.Database, Id(record), "reader", "row:CRUSDLAP");
            var elsewhere = EntryIn(GroupUnderRoot("Elsewhere"));

            using (var readings = new StoreReadings(_database.Database, CurrentThread.Instance, null))
            {
                var vault = new KdbxVault(_database.Database, null, TestTags.Fixed, CurrentThread.Instance, null, readings);

                Assert.That(vault.Entry(Id(discarded)), Is.Null);
                Assert.That(vault.Group(Id(bin)), Is.Null);
                Assert.That(vault.Group(Id(plugin)), Is.Null);
                Assert.That(vault.Entry(Id(record)), Is.Null);
                Assert.That(
                    AclResolver.Permits(vault.Entry(Id(elsewhere)).Node, Reader, AclVerb.Read), Is.False,
                    "and no node the gate reaches is granted anything");
            }
        }

        [Test]
        public void A_node_is_named_by_its_identifier_and_never_by_its_title()
        {
            // Refusals reach a log, and a log is meant to be readable by somebody who is not
            // entitled to know what the database holds. A group is called "Banking" and an entry is
            // called something worse.
            var group = GroupUnderRoot("Something Revealing");
            StoreSeeds.Seal(_store, Id(group), ("editor", "R"));
            var entry = EntryIn(group);
            entry.Strings.Set(PwDefs.TitleField, new KeePassLib.Security.ProtectedString(false, "Also Revealing"));

            AclDecision decision;
            using (var readings = new StoreReadings(_database.Database, CurrentThread.Instance, null))
            {
                decision = AclResolver.Decide(Gate(entry, readings), Reader, AclVerb.Read);
            }

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
                Assert.That(Permits(entry, Reader, verb), Is.False, AclVerbs.Write(verb));
            }
        }

        [Test]
        public void Nothing_of_a_rule_is_written_on_any_node()
        {
            // The rules are records of the plugin's store, and no group and no entry the operator
            // keeps holds anything of the plugin's, before a save or after it.
            var group = GroupUnderRoot("Fictitious");
            var entry = EntryIn(group);
            StoreSeeds.Seal(_store, Id(group), ("reader", "R"), ("editor", "CRUSDLAP"));
            StoreSeeds.Rule(_store, Id(entry), ("reader", ""));
            StoreSeeds.Rule(_store, Id(_database.RootGroup), ("reader", "L"));
            Assume.That(Permits(entry, Editor, AclVerb.Read), Is.True, "the rules were written and decide");

            _database.SaveCloseAndReopen();

            var root = _database.RootGroup;
            var groups = new[] { root }.Concat(root.GetGroups(true)).Where(node => !StoreNodes.Holds(node)).ToList();
            var entries = root.GetEntries(true).Where(node => !StoreNodes.Holds(node.ParentGroup)).ToList();
            Assert.That(groups.Sum(node => node.CustomData.Count), Is.Zero);
            Assert.That(entries.Sum(node => node.CustomData.Count), Is.Zero);
            Assert.That(entries.Sum(node => node.Strings.Count(field => field.Key.StartsWith("KPG", StringComparison.Ordinal))), Is.Zero);
            Assert.That(Permits(root.FindEntry(entry.Uuid, true), Editor, AclVerb.Read), Is.True, "and still decide");
        }

        [Test]
        public void A_node_that_is_not_there_is_nothing_to_decide_about()
        {
            Assert.That(KdbxNodes.Of((PwEntry)null, CurrentThread.Instance, () => StoredRules.Of(null)), Is.Null);
            Assert.That(KdbxNodes.Of((PwGroup)null, CurrentThread.Instance, () => StoredRules.Of(null)), Is.Null);
            PwGroup absent = null;
            Assert.That(KdbxNodes.Of(absent, StoredRules.Of(null)), Is.Null);
        }

        [Test]
        public void The_root_group_is_the_top_of_the_chain()
        {
            Assert.That(KdbxNodes.Of(_database.RootGroup, StoredRules.Of(null)).Parent, Is.Null);
            Assert.That(KdbxNodes.Of(_database.RootGroup, CurrentThread.Instance, () => StoredRules.Of(null)).Parent, Is.Null);
        }

        [Test]
        public void The_editor_s_view_reads_the_rules_it_is_given_at_every_level()
        {
            // The tab reads the store once as it opens, and walks from the parent of the dialog's
            // node with the rules of that reading, on KeePass's own thread.
            var group = GroupUnderRoot("Fictitious");
            StoreSeeds.Rule(_store, Id(group), ("reader", "RL"));
            StoreSeeds.Rule(_store, Id(_database.RootGroup), ("ops", "R"));

            StoredRules rules;
            using (var reading = _store.Read())
            {
                rules = StoredRules.Of(reading);
            }

            var node = KdbxNodes.Of(group, rules);
            Assert.That(node.Rule.Profiles, Is.EqualTo(new[] { "reader" }));
            Assert.That(node.Parent.Rule.Profiles, Is.EqualTo(new[] { "ops" }));
            Assert.That(AclResolver.Arrives(node, "reader").SameAs(Rules.Letters("RL")), Is.True);

            StoreSeeds.Rule(_store, Id(group), ("reader", "R"));
            Assert.That(
                AclResolver.Arrives(KdbxNodes.Of(group, rules), "reader").SameAs(Rules.Letters("RL")), Is.True,
                "the rules of the tab's reading, whatever was written since");
        }

        [Test]
        public void The_editor_s_view_answers_for_the_root_a_child_of_the_root_and_a_group_an_entry_is_added_to()
        {
            // What a write at a dialog's OK cuts to is what arrives at the dialog's node: the walk
            // from its parent group, which is the root for a group under the root, nothing for
            // the root, and the group an entry is added to in the Add Entry dialog, where the
            // entry is in no group yet.
            StoreSeeds.Rule(_store, Id(_database.RootGroup), ("reader", "RUL"));
            var child = GroupUnderRoot("Fictitious");
            StoreSeeds.Rule(_store, Id(child), ("reader", "R"));
            var adding = new PwEntry(true, true);

            StoredRules rules;
            using (var reading = _store.Read())
            {
                rules = StoredRules.Of(reading);
            }

            var root = KdbxNodes.Of(_database.RootGroup, rules);
            Assert.That(root.Parent, Is.Null, "the root is the top of the walk");
            Assert.That(AclResolver.Arrives(root, "reader").SameAs(Rules.Letters("RUL")), Is.True, "under the root, the root's rule");
            Assert.That(AclResolver.Arrives(root.Parent, "reader").SameAs(AclGrant.Nothing), Is.True, "above the root, nothing");

            var group = KdbxNodes.Of(child, rules);
            Assert.That(group.Rule.Row("reader").Answers, Is.True);
            Assert.That(group.Parent.Name, Is.EqualTo(_database.RootGroup.Uuid.ToHexString()), "a child of the root walks to the root");
            Assert.That(AclResolver.Arrives(group, "reader").SameAs(Rules.Letters("R")), Is.True);

            Assert.That(adding.ParentGroup, Is.Null, "the entry is added to its group only at OK");
            Assert.That(AclResolver.Arrives(KdbxNodes.Of(child, rules), "reader").SameAs(Rules.Letters("R")), Is.True,
                "what an entry added to the group receives, walked from the group");
        }

        [Test]
        public void The_editor_s_view_walks_where_it_is_asked_and_asks_KeePass_s_thread_for_no_hold()
        {
            // The write at a dialog's OK walks up from the node's parent inside the writer's turn,
            // on KeePass's own thread, where a hold asked for could never be given. So the
            // editor's view walks on the thread asking: in a turn on KeePass's thread, and on any
            // other thread while KeePass's thread is busy with something else.
            var child = GroupUnderRoot("Fictitious");
            StoreSeeds.Rule(_store, Id(child), ("reader", "R"));
            StoredRules rules;
            using (var reading = _store.Read())
            {
                rules = StoredRules.Of(reading);
            }

            using (var loop = new UiLoop())
            using (var ui = loop.Own(() => new ControlUiThread(loop.Control, KeePassMerges.Watch())))
            {
                var keePass = 0;
                var walkedOn = 0;
                AclGrant inTurn = null;
                Assume.That(ui.Value.Run(
                    () =>
                    {
                        keePass = Environment.CurrentManagedThreadId;
                        var node = KdbxNodes.Of(child, rules);
                        walkedOn = node.Held(() => Environment.CurrentManagedThreadId);
                        inTurn = AclResolver.Arrives(node, "reader");
                    },
                    TimeSpan.FromSeconds(10)), Is.True, "the turn ran");

                Assert.That(walkedOn, Is.EqualTo(keePass), "walked in the turn, on KeePass's thread");
                Assert.That(inTurn.SameAs(Rules.Letters("R")), Is.True);

                using (var entered = new ManualResetEventSlim())
                using (var release = new ManualResetEventSlim())
                {
                    var busy = System.Threading.Tasks.Task.Run(() => ui.Value.Run(
                        () =>
                        {
                            entered.Set();
                            release.Wait(TimeSpan.FromSeconds(30));
                        },
                        TimeSpan.FromSeconds(30)));
                    Assume.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True, "KeePass's thread is busy");

                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    AclGrant beside;
                    try
                    {
                        beside = AclResolver.Arrives(KdbxNodes.Of(child, rules), "reader");
                    }
                    finally
                    {
                        release.Set();
                    }

                    Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "nothing waited for KeePass's thread");
                    Assert.That(beside.SameAs(Rules.Letters("R")), Is.True);
                    Assert.That(busy.Wait(TimeSpan.FromSeconds(30)), Is.True);
                }
            }
        }

        [Test]
        public void A_walk_held_still_finds_a_seal_while_KeePass_moves_the_tree_around_it()
        {
            // A walk reads each level's parent out of KeePass's lists, and a lookup racing a
            // rebalance on KeePass's thread misses rather than throwing: a seal missed that way
            // sends the walk on to the grant above it, which the seal exists to stop. Held, the
            // move waits for the walk. Moved on a real message loop, as KeePass moves a node, in
            // bursts on a timer: keys either side of a node's custom data put in and taken out,
            // and the entry moved between two groups that both grant what the seal refuses.
            var granting = GroupUnderRoot("Fictitious");
            var alsoGranting = GroupUnderRoot("Also Fictitious");
            StoreSeeds.Rule(_store, Id(granting), ("reader", "R"));
            StoreSeeds.Rule(_store, Id(alsoGranting), ("reader", "R"));
            var sealedEntry = EntryIn(granting);
            StoreSeeds.Seal(_store, Id(sealedEntry));

            // A second chain nothing moves, walked on every pass beside the first chain. It shows
            // the walk really is climbing and really is reading what it finds, which a run that
            // only counted refusals could not tell from a walk that stopped at the entry.
            var quietGroup = GroupUnderRoot("Undisturbed");
            StoreSeeds.Rule(_store, Id(quietGroup), ("reader", "R"));
            var quiet = EntryIn(quietGroup);
            var database = _database.Database;

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
                using (var readings = new StoreReadings(database, watched, null))
                {
                    Func<StoredRules> rules = () => readings.Rules(database, database.RootGroup);
                    var until = DateTime.UtcNow.AddSeconds(2);
                    while (DateTime.UtcNow < until)
                    {
                        // Anything leaving here fails the test by escaping, which is an assertion
                        // too: it would reach the catch-all on the connection thread as a defect.
                        if (AclResolver.Permits(KdbxNodes.Of(sealedEntry, watched, rules), Reader, AclVerb.Read))
                        {
                            passedTheSeal++;
                        }

                        if (!AclResolver.Permits(KdbxNodes.Of(quiet, watched, rules), Reader, AclVerb.Read))
                        {
                            quietRefusals++;
                        }

                        walks++;
                    }
                }

                Assume.That(Volatile.Read(ref round), Is.GreaterThan(20), "KeePass's thread really was moving the tree");
                Assume.That(walks, Is.GreaterThan(100), "and the walks really were being served beside it");
                Assert.That(watched.Holds, Is.EqualTo(2 * walks), "each walk was one hold, however many levels it climbed");
                Assert.That(watched.Overlapped, Is.Zero, "and no walk ran while KeePass's thread was moving the tree");
                Assert.That(passedTheSeal, Is.Zero, "the seal was found on every one of " + walks + " walks");
                Assert.That(quietRefusals, Is.Zero, "and the chain nobody touched answered the same way on every one");
            }
        }

        /// <summary>
        /// The hold a walk asks for, counting how often it was asked and whether what it held ever
        /// overlapped KeePass's thread moving the tree.
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

            public long Stamp
            {
                get { return _hold.Stamp; }
            }

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

        /// <summary>
        /// Whether these profiles may do this at the entry, asked as the gate asks it: the rules of
        /// the store as it stands, read for the walk.
        /// </summary>
        private bool Permits(PwEntry entry, IEnumerable<string> profiles, AclVerb verb)
        {
            using (var readings = new StoreReadings(_database.Database, CurrentThread.Instance, null))
            {
                return AclResolver.Permits(Gate(entry, readings), profiles, verb);
            }
        }

        /// <summary>The rules the store holds for a node now, as the gate reads them.</summary>
        private NodeRule Rule(PwEntry entry)
        {
            using (var readings = new StoreReadings(_database.Database, CurrentThread.Instance, null))
            {
                return Gate(entry, readings).Rule;
            }
        }

        /// <summary>The rules the store holds for a group now, as the gate reads them.</summary>
        private NodeRule Rule(PwGroup group)
        {
            using (var readings = new StoreReadings(_database.Database, CurrentThread.Instance, null))
            {
                return KdbxNodes.Of(group, CurrentThread.Instance, () => readings.Rules(_database.Database, _database.RootGroup)).Rule;
            }
        }

        /// <summary>The gate's view of an entry, its rules read through these readings.</summary>
        private IAclNode Gate(PwEntry entry, StoreReadings readings)
        {
            var database = _database.Database;
            return KdbxNodes.Of(entry, CurrentThread.Instance, () => readings.Rules(database, database.RootGroup));
        }

        private static string Id(PwGroup group)
        {
            return Hex.Of(group.Uuid.UuidBytes);
        }

        private static string Id(PwEntry entry)
        {
            return Hex.Of(entry.Uuid.UuidBytes);
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
    }
}
