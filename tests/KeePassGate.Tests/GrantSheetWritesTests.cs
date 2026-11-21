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
    ///
    /// **With nodes below**, `STORE.md` §617: in a start that names them, the nodes below the node
    /// hold rules taken off for two profiles, in the shapes a write above meets: a version that
    /// follows what the node gives, `off:deny`, a limit that records letters of its own, a switch
    /// that follows for both profiles, two versions made apart, a node below a node below, and a
    /// version that reached the store after the tab read it. After every OK, what each node below
    /// holds is held to what the write above has to leave there, worked out here by letters from
    /// the texts the store holds and from nothing the product resolves, and each node the write
    /// left because the tab had not held its version is told, as no press.
    /// </remarks>
    [TestFixture]
    public sealed class GrantSheetWritesTests
    {
        /// <summary>The node the tab is about, as the store names a node.</summary>
        private const string Node = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        /// <summary>A node below the node, whose rule was taken off seeing what the node gave, for both profiles.</summary>
        private const string Following = "cccccccccccccccccccccccccccccccc";

        /// <summary>A node below the node, whose rule was taken off as nothing let through.</summary>
        private const string HeldBack = "dddddddddddddddddddddddddddddddd";

        /// <summary>A node below the node, whose rule was taken off recording letters of its own.</summary>
        private const string Limit = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        /// <summary>A node below the node, whose switch was turned to let the walk go on seeing what the node gave both profiles.</summary>
        private const string Switched = "ffffffffffffffffffffffffffffffff";

        /// <summary>A node below the node holding two versions made apart, each seeing what the node gave.</summary>
        private const string Twice = "11111111111111111111111111111111";

        /// <summary>A node below the node whose version reached the store after the tab read it.</summary>
        private const string Landed = "22222222222222222222222222222222";

        /// <summary>A node below the node that follows, whose rule was taken off seeing what that node gave.</summary>
        private const string Deeper = "33333333333333333333333333333333";

        private const string Profile = "reader";

        /// <summary>The second profile, which the nodes below rule beside the first.</summary>
        private const string Other = "ops";

        /// <summary>What arrives for the second profile in a start above nodes.</summary>
        private const string OtherArriving = "RU";

        /// <summary>What the limit below records: letters of its own, beside whatever the node gives.</summary>
        private const string Limited = "L";

        /// <summary>The letters of a rule, in their order.</summary>
        private const string Letters = "CRUSDLAP";

        /// <summary>What a lost version is held as among the texts of its object.</summary>
        private const string LostText = "(lost)";

        private static readonly string[] Profiles = { Profile, Other };

        /// <summary>The node and the nodes below it, as the write above is carried down them.</summary>
        private static readonly TestRuledTree Tree = TestRuledTree.Of(
            (Node, null), (Following, Node), (HeldBack, Node), (Limit, Node), (Switched, Node), (Twice, Node), (Landed, Node), (Deeper, Following));

        /// <summary>The nodes below the node, the nearer first, each with the node directly above it.</summary>
        private static readonly (string Node, string Above)[] Below =
        {
            (Following, Node), (HeldBack, Node), (Limit, Node), (Switched, Node), (Twice, Node), (Landed, Node), (Deeper, Following),
        };

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

            /// <summary>Whether the nodes below the node hold the rules taken off that the write above reaches.</summary>
            public bool NodesBelow { get; private set; }

            /// <summary>This start, with the nodes below the node holding their rules.</summary>
            public Start AboveNodes()
            {
                NodesBelow = true;
                return this;
            }

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
            new Start("a rule set here, above nodes", "RL", true, (false, "row:R")).AboveNodes(),
            new Start("an empty rule set here, above nodes", "RL", true, (false, "row:")).AboveNodes(),
            new Start("a rule inherited, above nodes", "RL", true).AboveNodes(),
            new Start("a cut by the row, above nodes", "RL", true, (false, "off:R")).AboveNodes(),
            new Start("a cut by a rule taken off under a refusing box, above nodes", "RL", true, (false, "off:deny")).AboveNodes(),
            new Start("a cut by the switch, above nodes", "RL", true, (true, "sw:inherit reader=R")).AboveNodes(),
            new Start("a group with a rule taken off, above nodes", "RUL", true, (false, "row:RL"), (false, "off:R")).AboveNodes(),
            new Start("a rule set here under a refusing box, above nodes", "RL", true, (false, "row:R"), (true, "sw:deny")).AboveNodes(),
        };

        [Test]
        [TestCaseSource(nameof(Starts))]
        public void Every_press_offered_is_written_as_the_table_showed_it(Start start)
        {
            var composed = Compose(start);
            foreach (var presses in Sequences(composed, start))
            {
                var store = composed.Copy();
                var above = Above(start);
                var sheet = Sheet(store, start);

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

                if (start.NodesBelow)
                {
                    // A version below reaches the store after the tab read it, as a merge brings
                    // it while the dialog stands.
                    var gives = Gives(Read(store), start)[Node][Profile];
                    Put(store, StoreComposer.RowObject(Landed, Profile), "off:" + gives);
                }

                var before = Read(store);
                IReadOnlyList<RuleLeft> left;
                StoreAct act;
                using (var reading = store.Read())
                {
                    act = RuleActs.Of(reading, Node, _ => above, _ => Tree, changes, out left);
                }

                Assert.That(left.Where(change => change.Pressed), Is.Empty, where + ": a press was left out of the write");
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

                var after = Sheet(store, start);
                Assert.That(Lines(after, false), Is.EquivalentTo(shown), where + ": the table after OK is not the table before OK");
                Assert.That(after.Box, Is.EqualTo(sheet.Box), where + ": the box after OK");
                if (start.NodesBelow)
                {
                    AssertBelow(before, Read(store), start, left, where);
                }
                else
                {
                    Assert.That(left, Is.Empty, where + ": a version was left out of the write");
                }
            }
        }

        /// <summary>
        /// What each node below holds after OK, held to what the write above has to leave there,
        /// worked out by letters, the nearer node first. For a profile, a node below follows where
        /// its switch does not refuse, its row does not answer by itself, what arrives there
        /// changed with the act, every version letting the profile through held all that arrived
        /// before, or recorded nothing where nothing arrived, and the tab held every version
        /// standing there for the profile. Each that follows records what arrives after, and each
        /// that would follow but for the tab is told, as no press.
        /// </summary>
        private static void AssertBelow(
            Dictionary<string, NodeTexts> before, Dictionary<string, NodeTexts> after, Start start, IReadOnlyList<RuleLeft> left, string where)
        {
            var arrivedAt = Gives(before, start);
            var givesAfter = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                { Node, Profiles.ToDictionary(profile => profile, profile => GivesByLetters(after[Node], profile, Arriving(start, profile)), StringComparer.Ordinal) },
            };
            var expected = new Dictionary<string, NodeTexts>(StringComparer.Ordinal);
            var told = new List<(string Node, string Profile)>();
            foreach (var (node, parent) in Below)
            {
                var texts = before[node];
                var follows = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var profile in Profiles)
                {
                    var row = texts.Row(profile);
                    var lets = row.Where(text => text.StartsWith("off:", StringComparison.Ordinal)).ToList();
                    var inheriting = texts.Switch.Where(text => text.StartsWith("sw:inherit", StringComparison.Ordinal)).ToList();
                    var answers = row.Count > 0 && lets.Count == 0;
                    if (texts.Switch.Contains("sw:deny") || answers || (lets.Count == 0 && inheriting.Count == 0))
                    {
                        continue;
                    }

                    var arrived = arrivedAt[parent][profile];
                    var arrives = givesAfter[parent][profile];
                    var whole = lets.All(text => text != "off:deny" && Followed(text.Substring("off:".Length), arrived))
                                && inheriting.All(text => Followed(MaskFor(text, profile), arrived));
                    if (arrived == arrives || !whole)
                    {
                        continue;
                    }

                    if (node == Landed)
                    {
                        told.Add((node, profile));
                    }
                    else
                    {
                        follows[profile] = arrives;
                    }
                }

                expected[node] = texts.Renewed(follows);
                givesAfter[node] = Profiles.ToDictionary(
                    profile => profile, profile => GivesByLetters(expected[node], profile, givesAfter[parent][profile]), StringComparer.Ordinal);
            }

            foreach (var (node, _) in Below)
            {
                foreach (var profile in Profiles)
                {
                    Assert.That(after[node].Row(profile), Is.EqualTo(expected[node].Row(profile)), where + ": the rule of " + profile + " at " + node.Substring(0, 2));
                }

                Assert.That(after[node].Switch, Is.EqualTo(expected[node].Switch), where + ": the switch at " + node.Substring(0, 2));
            }

            Assert.That(
                left.Select(change => (change.Node, change.Profile, change.Reason)),
                Is.EquivalentTo(told.Select(pair => (pair.Node, pair.Profile, RuleLeftReason.NotHeld))),
                where + ": each node the write above left because the tab had not held its version, and no other");
        }

        /// <summary>What each node of the tree gives each profile, by letters, from these texts and what the start has arriving.</summary>
        private static Dictionary<string, Dictionary<string, string>> Gives(Dictionary<string, NodeTexts> texts, Start start)
        {
            var gives = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                { Node, Profiles.ToDictionary(profile => profile, profile => GivesByLetters(texts[Node], profile, Arriving(start, profile)), StringComparer.Ordinal) },
            };
            foreach (var (node, parent) in Below)
            {
                gives[node] = Profiles.ToDictionary(profile => profile, profile => GivesByLetters(texts[node], profile, gives[parent][profile]), StringComparer.Ordinal);
            }

            return gives;
        }

        /// <summary>
        /// What a node gives a profile, by letters, from the texts it holds and what arrives there,
        /// `STORE.md` §617: nothing where the row refuses, the letters every granting version
        /// holds where all of them grant, nothing where the switch refuses, and otherwise what
        /// arrives cut to the letters of each version of the row and each version of the switch.
        /// </summary>
        private static string GivesByLetters(NodeTexts texts, string profile, string arriving)
        {
            var row = texts.Row(profile);
            if (row.Any(text => !RowInForm(text)))
            {
                return string.Empty;
            }

            if (row.Count > 0 && row.All(text => text.StartsWith("row:", StringComparison.Ordinal)))
            {
                return row.Select(text => text.Substring("row:".Length)).Aggregate(Letters, Common);
            }

            if (texts.Switch.Any(text => !text.StartsWith("sw:inherit", StringComparison.Ordinal)))
            {
                return string.Empty;
            }

            var limit = row.Select(text => text == "off:deny" ? string.Empty : text.Substring(text.IndexOf(':') + 1))
                .Concat(texts.Switch.Select(text => MaskFor(text, profile)))
                .Aggregate(Letters, Common);
            return Common(arriving, limit);
        }

        /// <summary>Whether a version recording these letters followed what arrived: held all of it, or recorded nothing where nothing arrived.</summary>
        private static bool Followed(string recorded, string arrived)
        {
            return arrived.Length == 0 ? recorded.Length == 0 : arrived.All(recorded.Contains);
        }

        /// <summary>Whether a row's text is a text the reading takes: letters granted, a rule taken off with its letters, or `off:deny`.</summary>
        private static bool RowInForm(string text)
        {
            if (text == "off:deny")
            {
                return true;
            }

            var letters = text.StartsWith("row:", StringComparison.Ordinal) ? text.Substring("row:".Length)
                : text.StartsWith("off:", StringComparison.Ordinal) ? text.Substring("off:".Length)
                : null;
            return letters != null && letters.All(Letters.Contains);
        }

        /// <summary>The letters a switch's text records for a profile, none where it names the profile not.</summary>
        private static string MaskFor(string text, string profile)
        {
            return text.Split(' ').Skip(1)
                .Where(item => item.StartsWith(profile + "=", StringComparison.Ordinal))
                .Select(item => item.Substring(profile.Length + 1))
                .FirstOrDefault() ?? string.Empty;
        }

        /// <summary>The letters both masks hold, in the order of a rule.</summary>
        private static string Common(string first, string second)
        {
            return new string(Letters.Where(letter => first.Contains(letter) && second.Contains(letter)).ToArray());
        }

        /// <summary>What arrives at the node for a profile in a start: the start's mask for the first, and for the second above nodes.</summary>
        private static string Arriving(Start start, string profile)
        {
            return profile == Profile ? start.Arriving ?? string.Empty : start.NodesBelow ? OtherArriving : string.Empty;
        }

        /// <summary>The texts of the standing and the lost versions at every node of the tree, as a reading of the store holds them.</summary>
        private static Dictionary<string, NodeTexts> Read(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return new[] { Node }.Concat(Below.Select(below => below.Node)).ToDictionary(
                    node => node,
                    node => new NodeTexts(
                        Profiles.ToDictionary(profile => profile, profile => TextsOf(reading, StoreComposer.RowObject(node, profile)), StringComparer.Ordinal),
                        TextsOf(reading, StoreComposer.SwitchObject(node))),
                    StringComparer.Ordinal);
            }
        }

        /// <summary>The texts of an object's standing versions, and a mark for each version lost, in ordinal order.</summary>
        private static IReadOnlyList<string> TextsOf(StoreReading reading, string objectText)
        {
            return reading.InForce.VersionsOf(objectText)
                .Select(version => version.Members[StoreKinds.Doc])
                .Concat(reading.InForce.LostOf(objectText).Select(lost => LostText))
                .OrderBy(text => text, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The tab's sheet over the store as it stands, with the versions below the node it keeps for each press.</summary>
        private static GrantSheet Sheet(MemoryStore store, Start start)
        {
            using (var reading = store.Read())
            {
                var rules = StoredRules.Of(reading);
                var sheet = GrantSheet.Of(rules.At(Node), AclResolver.Above(Above(start)), Names(start), null);
                sheet.TakeBelow(rules.Below(Node, Tree));
                return sheet;
            }
        }

        /// <summary>The texts at a node: each profile's row and the switch, each in ordinal order.</summary>
        private sealed class NodeTexts
        {
            private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _rows;

            public NodeTexts(IReadOnlyDictionary<string, IReadOnlyList<string>> rows, IReadOnlyList<string> switches)
            {
                _rows = rows;
                Switch = switches;
            }

            public IReadOnlyList<string> Switch { get; }

            public IReadOnlyList<string> Row(string profile)
            {
                return _rows.TryGetValue(profile, out var row) ? row : Array.Empty<string>();
            }

            /// <summary>
            /// The texts once each profile that follows is written anew: every rule taken off as
            /// what arrives, and in every version of the switch that profile's letters changed and
            /// every other profile's kept.
            /// </summary>
            public NodeTexts Renewed(IReadOnlyDictionary<string, string> follows)
            {
                var rows = _rows.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value
                        .Select(text => follows.TryGetValue(pair.Key, out var arrives) && text.StartsWith("off:", StringComparison.Ordinal) ? "off:" + arrives : text)
                        .OrderBy(text => text, StringComparer.Ordinal)
                        .ToList(),
                    StringComparer.Ordinal);
                var switches = Switch.Select(text => text.StartsWith("sw:inherit", StringComparison.Ordinal) && follows.Count > 0 ? Inheriting(text, follows) : text)
                    .OrderBy(text => text, StringComparer.Ordinal)
                    .ToList();
                return new NodeTexts(rows, switches);
            }

            /// <summary>A switch's text with each profile that follows recording what arrives, a profile that nothing arrives for named not.</summary>
            private static string Inheriting(string text, IReadOnlyDictionary<string, string> follows)
            {
                var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in text.Split(' ').Skip(1))
                {
                    items[item.Substring(0, item.IndexOf('='))] = item.Substring(item.IndexOf('=') + 1);
                }

                foreach (var follow in follows)
                {
                    items[follow.Key] = follow.Value;
                }

                return "sw:inherit" + string.Concat(items.Where(item => item.Value.Length > 0).Select(item => " " + item.Key + "=" + item.Value));
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
        private static IEnumerable<IList<Press>> Sequences(MemoryStore store, Start start)
        {
            foreach (var first in Offered(Sheet(store, start)))
            {
                yield return new[] { first };

                var sheet = Sheet(store, start);
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
            return start.Named ? new[] { Profile, Other } : new[] { Other };
        }

        /// <summary>A parent giving each profile what the start has arriving for it.</summary>
        private static TestNode Above(Start start)
        {
            var given = Profiles.Select(profile => (profile, Arriving(start, profile))).Where(pair => pair.Item2.Length > 0).ToArray();
            return given.Length == 0 ? TestNode.Root("above") : TestNode.Root("above").Holding(Rules.Node(false, given));
        }

        /// <summary>
        /// A store with an authority and every version of the start, each written apart, and for a
        /// start above nodes, the rules below it: a version for each profile and a switch that
        /// record what the node gives, `off:deny`, a limit, two versions made apart, and below the
        /// node that follows, a version recording what it gives.
        /// </summary>
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

            if (start.NodesBelow)
            {
                var gives = Gives(Read(store), start)[Node];
                Put(store, StoreComposer.RowObject(Following, Profile), "off:" + gives[Profile]);
                Put(store, StoreComposer.RowObject(Following, Other), "off:" + gives[Other]);
                Put(store, StoreComposer.RowObject(HeldBack, Profile), RuleText.TakenOffRefusing);
                Put(store, StoreComposer.RowObject(Limit, Profile), "off:" + Limited);
                Put(store, StoreComposer.SwitchObject(Switched), "sw:inherit" + string.Concat(
                    new[] { Other, Profile }.Where(profile => gives[profile].Length > 0).Select(profile => " " + profile + "=" + gives[profile])));
                Put(store, StoreComposer.RowObject(Twice, Profile), "off:" + gives[Profile]);
                Put(store, StoreComposer.RowObject(Twice, Profile), "off:" + gives[Profile]);
                Put(store, StoreComposer.RowObject(Deeper, Profile), "off:" + Gives(Read(store), start)[Following][Profile]);
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
