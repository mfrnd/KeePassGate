using System;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The write an OK on a node's dialog makes into the store of a real database, `STORE.md`
    /// §629: its act composed over the reading the write is planned against, in a single turn,
    /// and the reading made beside the tree.
    /// </summary>
    [TestFixture]
    public sealed class RuleWritesInRealDatabaseTests
    {
        [Test]
        public void A_write_plans_against_the_very_reading_its_act_was_composed_over_and_cuts_to_what_arrives_in_it()
        {
            using (var database = TemporaryDatabase.New())
            {
                var store = Store(database);
                StoreSeeds.Authority(store);
                var root = Hex.Of(database.RootGroup.Uuid.UuidBytes);
                var group = new PwGroup(true, true);
                database.RootGroup.AddGroup(group, true);
                var node = Hex.Of(group.Uuid.UuidBytes);
                StoreSeeds.Rule(store, root, ("pf", "RU"));
                StoreSeeds.Rule(store, node, ("pf", "R"));
                NodeRule shown;
                using (var reading = store.Read())
                {
                    shown = StoredRules.Of(reading).At(node);
                }

                // The root narrowed while the dialog at the group stands, its tab showing RU
                // arriving.
                StoreSeeds.Rule(store, root, ("pf", "R"));
                StoreComposer.Composed landed = null;

                var written = store.Write(reading =>
                {
                    var act = RuleActs.Of(
                        reading,
                        node,
                        RealNode.Of(database.RootGroup, StoredRules.Of(reading)),
                        new RuleChanges(new[] { new RowChange(RowChangeKind.TakeOff, "pf", Rules.Letters("RU"), null, shown) }, null),
                        out _);

                    // A version of another row reaches the database between the composing and the
                    // plan: a plan read again would restate it, and a plan against the reading the
                    // act was composed over does not hold it.
                    landed = Put(database, reading, StoreComposer.RowObject(node, "pg"), "row:L");
                    return act;
                });

                Assert.That(written.Taken, Is.True, written.Reason);
                using (var reading = store.Read())
                {
                    var newest = reading.InForce.Heads.OrderByDescending(head => head.Form.Number).First();
                    var restated = newest.Form.Items.Where(item => item.Kind == StoreKinds.Version).Select(item => item.Member(StoreKinds.Doc));
                    Assert.That(restated, Is.EquivalentTo(new[] { "row:R", "off:R" }), "the summary entry written restates what its reading held and the act");
                    Assert.That(
                        newest.Form.Items.Select(item => item.Member(StoreKinds.Id)),
                        Does.Not.Contain(landed.Hex),
                        "the version that reached the database after the reading is not restated");
                    Assert.That(
                        reading.InForce.VersionsOf(StoreComposer.RowObject(node, "pf")).Select(version => version.Members[StoreKinds.Doc]),
                        Is.EqualTo(new[] { "off:R" }),
                        "the rule taken off records what arrives in the write's reading, and not the wider mask the tab showed");
                }
            }
        }

        [Test]
        public void An_act_composed_as_nothing_writes_nothing_and_leaves_the_database_unmarked()
        {
            using (var database = TemporaryDatabase.New())
            {
                var store = Store(database);
                StoreSeeds.Authority(store);
                database.Save();
                Assume.That(database.Database.Modified, Is.False);
                var entries = database.RootGroup.GetEntries(true).UCount;
                var asked = 0;

                var written = store.Write(reading =>
                {
                    asked++;
                    return null;
                });

                Assert.That(asked, Is.EqualTo(1), "composed once");
                Assert.That(written.Taken, Is.True);
                Assert.That(written.Wrote, Is.False);
                Assert.That(database.Database.Modified, Is.False, "nothing marked");
                Assert.That(database.RootGroup.GetEntries(true).UCount, Is.EqualTo(entries), "no entry added");
            }
        }

        [Test]
        public void A_rule_written_marks_the_database_and_decides_from_the_next_reading()
        {
            using (var database = TemporaryDatabase.New())
            {
                var store = Store(database);
                StoreSeeds.Authority(store);
                var group = new PwGroup(true, true);
                database.RootGroup.AddGroup(group, true);
                var node = Hex.Of(group.Uuid.UuidBytes);
                database.Save();
                Assume.That(database.Database.Modified, Is.False);

                var written = store.Write(reading => RuleActs.Of(
                    reading, node, RealNode.Of(database.RootGroup, StoredRules.Of(reading)), Chosen("pf", "RL", StoredRules.Of(reading).At(node)), out _));

                Assert.That(written.Taken && written.Wrote, Is.True, written.Reason);
                Assert.That(database.Database.Modified, Is.True, "the database is marked, and nothing is saved");
                using (var reading = store.Read())
                {
                    var row = StoredRules.Of(reading).At(node).Row("pf");
                    Assert.That(row.Answers, Is.True);
                    Assert.That(row.Grants.SameAs(Rules.Letters("RL")), Is.True);
                }
            }
        }

        [Test]
        public void A_reading_tells_of_a_rule_lost_at_a_node_no_object_of_the_file_names()
        {
            // `STORE.md` §617: a deletion record tells of a version of a row at a node of the
            // tree, which nothing else in the file names, so only the tree's nodes find it.
            using (var database = TemporaryDatabase.New())
            {
                var store = Store(database);
                StoreSeeds.Authority(store);
                StoreSeeds.Name(store, "pf");
                var group = new PwGroup(true, true);
                database.RootGroup.AddGroup(group, true);
                var node = Hex.Of(group.Uuid.UuidBytes);
                using (var reading = store.Read())
                {
                    Assume.That(StoredRules.Of(reading).At(node).Row("pf").Refuses, Is.False);
                    var lost = Composed(reading, StoreComposer.RowObject(node, "pf"), "row:R");
                    database.Database.DeletedObjects.Add(new PwDeletedObject(new PwUuid(lost.Identifier), DateTime.UtcNow));
                }

                using (var reading = store.Read())
                {
                    var row = StoredRules.Of(reading).At(node).Row("pf");
                    Assert.That(row.Refuses, Is.True, "the row refuses for its lost version");
                    Assert.That(row.Lost, Has.Count.EqualTo(1));
                }
            }
        }

        private static KdbxStore Store(TemporaryDatabase database)
        {
            return new KdbxStore(database.Database, GateVersion.Current, null);
        }

        private static RuleChanges Chosen(string profile, string mask, NodeRule shown)
        {
            return new RuleChanges(new[] { new RowChange(RowChangeKind.Set, profile, Rules.Letters(mask), null, shown) }, null);
        }

        /// <summary>A version of this object composed under the reading's store key, as another copy composes it.</summary>
        private static StoreComposer.Composed Composed(StoreReading reading, string objectName, string doc)
        {
            var key = (byte[])reading.InForce.Key.Bytes.Clone();
            try
            {
                return StoreComposer.RuleVersion(key, objectName, doc);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>A group of the tree with the rules of a reading, walked where it is asked.</summary>
        private sealed class RealNode : IAclNode
        {
            private readonly PwGroup _group;
            private readonly StoredRules _rules;

            private RealNode(PwGroup group, StoredRules rules)
            {
                _group = group;
                _rules = rules;
            }

            public NodeRule Rule
            {
                get { return _rules.At(Hex.Of(_group.Uuid.UuidBytes)); }
            }

            public IAclNode Parent
            {
                get { return Of(_group.ParentGroup, _rules); }
            }

            public string Name
            {
                get { return Hex.Of(_group.Uuid.UuidBytes).Substring(0, 8); }
            }

            /// <summary>The group with these rules, or null for no group.</summary>
            public static IAclNode Of(PwGroup group, StoredRules rules)
            {
                return group == null ? null : new RealNode(group, rules);
            }

            public T Held<T>(Func<T> walk)
            {
                return walk();
            }
        }

        /// <summary>A version of this object put into the plugin's group, as an entry another program wrote.</summary>
        private static StoreComposer.Composed Put(TemporaryDatabase database, StoreReading reading, string objectName, string doc)
        {
            var version = Composed(reading, objectName, doc);
            var group = database.RootGroup.Groups.Single(found => StoreFormat.IsGroupIdentifier(found.Uuid.UuidBytes));
            group.AddEntry(StoreEntries.Of(version), true);
            return version;
        }
    }
}
