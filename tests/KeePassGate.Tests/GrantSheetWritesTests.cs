using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Every press the KPG tab offers, handed to the real acts over a store composed by the tests:
    /// what OK writes is what the table showed before OK, and no press offered comes to nothing.
    /// </summary>
    /// <remarks>
    /// From each start, every sequence of up to two presses, each press one the tab offers where
    /// the press before left it. A version lost with no other version of its object stands in no
    /// start: the store composed here walks no tree, and a reading finds such a version only by
    /// walking the tree. `GrantSheetTests` holds the rows of a lost rule standing alone.
    /// </remarks>
    [TestFixture]
    public sealed class GrantSheetWritesTests
    {
        /// <summary>The node the tab is about, as the store names a node.</summary>
        private const string Node = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private const string Profile = "reader";

        /// <summary>A start: the versions of the node's row and switch, what arrives, and the names.</summary>
        public sealed class Start
        {
            public Start(string name, string arriving, bool named, params (bool Switch, string Text)[] versions)
            {
                Name = name;
                Arriving = arriving;
                Named = named;
                Versions = versions;
            }

            public string Name { get; }

            /// <summary>The mask arriving for the profile, or null where nothing arrives.</summary>
            public string Arriving { get; }

            public bool Named { get; }

            /// <summary>Each version, of the switch or of the row, a null text for a lost version.</summary>
            public (bool Switch, string Text)[] Versions { get; }

            public override string ToString()
            {
                return Name;
            }
        }

        private static readonly Start[] Starts =
        {
            new Start("a rule set here", "RL", true, (false, "row:R")),
            new Start("an empty rule set here", "RL", true, (false, "row:")),
            new Start("a rule inherited", "RL", true),
            new Start("a cut by the row", "RL", true, (false, "off:R")),
            new Start("a cut by a rule taken off under a refusing box", "RL", true, (false, "off:deny")),
            new Start("a cut by the switch", "RL", true, (true, "sw:inherit reader=R")),
            new Start("a cut by the switch beside a rule taken off with what arrived", "CRL", true, (false, "off:CRL"), (true, "sw:inherit reader=R")),
            new Start("a group of rules", "CRL", true, (false, "row:R"), (false, "row:RL")),
            new Start("a group with a rule taken off", "RUL", true, (false, "row:RL"), (false, "off:R")),
            new Start("a group with off:deny under a refusing box", "RL", true, (false, "row:R"), (false, "off:deny"), (true, "sw:deny")),
            new Start("a rule out of form", "RL", true, (false, "row:x")),
            new Start("a group with a lost version", "RL", true, (false, "row:R"), (false, null)),
            new Start("a group with a version out of form", "RL", true, (false, "row:R"), (false, "row:x")),
            new Start("a group of lost versions", "RL", true, (false, null), (false, "row:x")),
            new Start("a rule set here with nothing arriving", null, true, (false, "row:R")),
            new Start("a group with a rule taken off and nothing arriving", null, true, (false, "row:RL"), (false, "off:R")),
            new Start("a rule set here under a refusing box", "RL", true, (false, "row:R"), (true, "sw:deny")),
            new Start("a box set apart", "RL", true, (true, "sw:deny"), (true, "sw:inherit reader=RL")),
            new Start("a box with a lost version", "RL", true, (true, "sw:inherit reader=RL"), (true, null)),
            new Start("a rule for a name the database no longer names", "RL", false, (false, "row:R")),
        };

        [Test]
        [TestCaseSource(nameof(Starts))]
        public void Every_press_offered_is_written_as_the_table_showed_it(Start start)
        {
            foreach (var presses in Sequences(start))
            {
                var store = Compose(start);
                var above = Above(start);
                GrantSheet sheet;
                using (var reading = store.Read())
                {
                    sheet = GrantSheet.Of(StoredRules.Of(reading).At(Node), AclResolver.Above(above), Names(start), null);
                }

                var done = new List<string>();
                foreach (var press in presses)
                {
                    done.Add(press.Name);
                    Assert.That(press.Apply(sheet), Is.True, start + ": " + string.Join(", ", done) + " was offered and refused");
                }

                var where = start + ": " + string.Join(", ", done);
                var changes = sheet.Changes;
                var shown = Lines(sheet, true);
                foreach (var change in changes.Rows)
                {
                    Assert.That(sheet.Shows(change.Profile), Is.True, where + ": a change waiting for OK has no row");
                }

                IReadOnlyList<RuleLeft> left;
                StoreAct act;
                using (var reading = store.Read())
                {
                    act = RuleActs.Of(reading, Node, above, changes, out left);
                }

                Assert.That(left, Is.Empty, where + ": a press was left out of the write");
                if (changes.IsEmpty)
                {
                    // Nothing handed over, so the table has to show what the store gives.
                    Assert.That(act, Is.Null, where + ": nothing was pressed and something is written");
                }
                else
                {
                    Assert.That(act, Is.Not.Null, where + ": a press offered on the tab came to nothing at OK");
                    try
                    {
                        var written = store.Write(new[] { act });
                        Assert.That(written.Taken, Is.True, where + ": " + written.Reason);
                    }
                    finally
                    {
                        act.Forget();
                    }
                }

                GrantSheet after;
                using (var reading = store.Read())
                {
                    after = GrantSheet.Of(StoredRules.Of(reading).At(Node), AclResolver.Above(above), Names(start), null);
                }

                Assert.That(Lines(after, false), Is.EquivalentTo(shown), where + ": the table after OK is not the table before OK");
                Assert.That(after.Box, Is.EqualTo(sheet.Box), where + ": the box after OK");
            }
        }

        /// <summary>A press offered on the tab, by what it is called.</summary>
        private sealed class Press
        {
            public Press(string name, Func<GrantSheet, bool> apply)
            {
                Name = name;
                Apply = apply;
            }

            public string Name { get; }

            public Func<GrantSheet, bool> Apply { get; }
        }

        /// <summary>Every sequence of up to two presses, each offered where the press before left the tab.</summary>
        private static IEnumerable<IList<Press>> Sequences(Start start)
        {
            var store = Compose(start);
            GrantSheet Fresh()
            {
                using (var reading = store.Read())
                {
                    return GrantSheet.Of(StoredRules.Of(reading).At(Node), AclResolver.Above(Above(start)), Names(start), null);
                }
            }

            foreach (var first in Offered(Fresh()))
            {
                yield return new[] { first };

                var sheet = Fresh();
                first.Apply(sheet);
                foreach (var second in Offered(sheet))
                {
                    yield return new[] { first, second };
                }
            }
        }

        /// <summary>Every press the tab offers on a sheet: a rule chosen on each row that takes one, the X of each row that has one, and the box.</summary>
        private static IEnumerable<Press> Offered(GrantSheet sheet)
        {
            var presses = new List<Press>();
            var rows = sheet.Rows;
            for (var at = 0; at < rows.Count; at++)
            {
                var row = rows[at];
                var label = row.Profile + " " + row.Mask + " " + string.Join("/", row.States) + " (" + row.Kind + ")";
                if (row.TakesRule)
                {
                    foreach (var mask in new[] { "........", row.Mask, ".R......", "CRUSDLAP" }.Distinct())
                    {
                        presses.Add(new Press("chose " + mask + " on " + label, tab => tab.Set(row.Profile, mask) == null));
                    }
                }

                if (row.Removable)
                {
                    var index = at;
                    presses.Add(new Press("X on " + label, tab => tab.Forget(tab.Rows[index])));
                }
            }

            presses.Add(new Press("the box", tab => tab.PressBox()));
            return presses;
        }

        /// <summary>The rows of a sheet as text, leaving out whether a row changed, and the row a change keeps where nothing would show once OK is written.</summary>
        private static List<string> Lines(GrantSheet sheet, bool pending)
        {
            return sheet.Rows
                .Where(row => !(pending && row.Changed && row.Kind == GrantRowKind.Inherited && row.InheritedMask == null))
                .Select(row => row.Profile + " " + row.Kind + " " + row.Mask + " " + string.Join("/", row.States)
                               + " granting " + row.Granted + " of " + row.Group)
                .ToList();
        }

        private static IEnumerable<string> Names(Start start)
        {
            return start.Named ? new[] { Profile, "ops" } : new[] { "ops" };
        }

        /// <summary>A parent giving the profile what the start has arriving.</summary>
        private static TestNode Above(Start start)
        {
            return start.Arriving == null
                ? TestNode.Root("above")
                : TestNode.Root("above").Holding(Rules.Node(false, (Profile, start.Arriving)));
        }

        /// <summary>A store with an authority and every version of the start, each written apart.</summary>
        private static MemoryStore Compose(Start start)
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            foreach (var version in start.Versions)
            {
                var objectName = version.Switch ? StoreComposer.SwitchObject(Node) : StoreComposer.RowObject(Node, Profile);
                if (version.Text == null)
                {
                    Lost(store, objectName);
                }
                else
                {
                    Put(store, objectName, version.Text);
                }
            }

            return store;
        }

        /// <summary>A version put into the store as another copy writes it, restated beside what is in force.</summary>
        private static void Put(MemoryStore store, string objectName, string doc)
        {
            var key = KeyOf(store);
            try
            {
                var version = StoreComposer.RuleVersion(key, objectName, doc);
                var restating = StoreComposer.RuleVersionItem(version, objectName, doc);
                using (var reading = store.Read())
                {
                    var heads = reading.InForce.HeadsInForce;
                    var items = heads.SelectMany(head => head.Form.Items)
                        .Select(item => StoreComposer.Item(item.Members.Select(member => (member.Key, member.Value))
                            .Concat(new[] { ("kind", item.Kind) }).ToArray()))
                        .Concat(new[] { restating })
                        .Distinct()
                        .ToList();
                    var height = heads.Max(head => head.Form.Number) + 1;
                    var named = string.Join(" ", reading.InForce.After.OrderBy(hex => hex, StringComparer.Ordinal));
                    store.Put(version);
                    store.Put(StoreComposer.Record(
                        key,
                        "head",
                        "store",
                        StoreComposer.Content(("after", named), ("height", height.ToString(CultureInfo.InvariantCulture))),
                        height,
                        items));
                    foreach (var head in heads)
                    {
                        store.Drop(head.Hex);
                        store.Delete(StoreComposer.BytesOf(head.Hex));
                    }
                }
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>A version that existed and is nowhere: a deletion record tells of it, and nothing carries it.</summary>
        private static void Lost(MemoryStore store, string objectName)
        {
            var key = KeyOf(store);
            try
            {
                store.Delete(StoreComposer.RuleVersion(key, objectName, "row:CRUSDLAP").Identifier);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private static byte[] KeyOf(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return (byte[])reading.InForce.Key.Bytes.Clone();
            }
        }
    }
}
