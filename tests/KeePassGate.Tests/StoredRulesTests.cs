using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a node holds, `STORE.md` §617: the rows and the switch as the standing and the lost
    /// versions of their objects read, and the rules of a reading of the store, held to what the
    /// measured reading gives at every node of every store of the meaning vectors.
    /// </summary>
    /// <remarks>
    /// The stores are composed by the measured code, never from this side, and read here by the
    /// product's own reading; what each node gives is asked of the resolver, which walks the rules
    /// read. A store a reading has nothing in force for gives no rule at all.
    /// </remarks>
    [TestFixture]
    public sealed class StoredRulesTests
    {
        private const string Root = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Node = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private static IEnumerable<TestCaseData> Stores()
        {
            // `rules:` the stores of rules, and `rules, ` those of the lost versions a version
            // settles, which the measured reading reads as the product does.
            return ((JObject)RuleVectors.Meanings["meanings"]).Properties()
                .Where(store => store.Name.StartsWith("rules:", StringComparison.Ordinal) || store.Name.StartsWith("rules, ", StringComparison.Ordinal))
                .Select(store => new TestCaseData(store.Name).SetArgDisplayNames(store.Name));
        }

        private static IEnumerable<TestCaseData> Texts()
        {
            foreach (var text in (JObject)RuleVectors.Records["texts"])
            {
                yield return new TestCaseData(text.Key, text.Value.Type == JTokenType.Null)
                    .SetArgDisplayNames("\"" + text.Key + "\"");
            }
        }

        [TestCaseSource(nameof(Stores))]
        public void A_store_gives_at_every_node_what_the_measured_reading_gives(string name)
        {
            var store = (JObject)RuleVectors.Meanings["meanings"][name];
            var rules = (JObject)store["rules"];

            using (var reading = Read(store))
            {
                Assert.That(reading.InForce, Is.Not.Null, "the store holds something in force");
                var stored = StoredRules.Of(reading);
                var nodes = Tree((JObject)store["tree"], stored);

                foreach (var given in (JObject)rules["gives"])
                {
                    foreach (var profile in (JObject)given.Value)
                    {
                        var where = name + ", at " + given.Key.Substring(0, 2) + ", " + profile.Key;
                        var arrives = AclResolver.Arrives(nodes[given.Key], profile.Key);

                        Assert.That(Letters(arrives), Is.EqualTo((string)profile.Value), where + ", by letters");
                        Assert.That(
                            Capabilities(nodes[given.Key], profile.Key),
                            Is.EqualTo(((JArray)rules["capabilities"][given.Key][profile.Key]).Select(cap => (string)cap)),
                            where + ", by capabilities");
                    }
                }
            }
        }

        [TestCaseSource(nameof(Stores))]
        public void A_store_holds_at_every_node_the_versions_the_measured_reading_holds(string name)
        {
            var store = (JObject)RuleVectors.Meanings["meanings"][name];
            var rules = (JObject)store["rules"];
            var lost = new HashSet<string>(((JArray)rules["lost"]).Select(item => (string)item), StringComparer.Ordinal);

            using (var reading = Read(store))
            {
                var stored = StoredRules.Of(reading);
                var objects = new HashSet<string>(((JObject)rules["standing"]).Properties().Select(item => item.Name), StringComparer.Ordinal);
                objects.UnionWith(lost);

                foreach (var objectText in objects)
                {
                    string node;
                    string profile;
                    if (!RuleText.ReadObject(objectText, out node, out profile))
                    {
                        // The object `rule:` and a node with no `/`, which shares the place of a
                        // row whose name's tag is 0 and is no node's rule, `STORE.md` §658.
                        Assert.That(objectText, Does.Match("^rule:[0-9a-f]{32}$"), "an object the vectors hold that is no node's rule");
                        continue;
                    }


                    var standing = rules["standing"][objectText] == null
                        ? Array.Empty<string>()
                        : ((JArray)rules["standing"][objectText]).Select(text => (string)text).ToArray();
                    var at = stored.At(node);

                    var values = rules["values"][objectText];
                    if (profile == null)
                    {
                        Assert.That(at.Switch.Versions.Select(Text).OrderBy(text => text, StringComparer.Ordinal), Is.EqualTo(standing), objectText);
                        Assert.That(at.Switch.Lost.Count > 0, Is.EqualTo(lost.Contains(objectText)), objectText + ", lost");
                        Assert.That(at.Switch.Refuses, Is.EqualTo(Refuses(rules, objectText, lost)), objectText + ", refuses");

                        if (values != null)
                        {
                            Assert.That(
                                at.Switch.Versions.Where(version => version.Kind == SwitchVersionKind.Inherits)
                                    .Select(version => string.Join(" ", version.Profiles.Select(name => name + "=" + Letters(version.LettersFor(name)))))
                                    .Distinct(StringComparer.Ordinal)
                                    .OrderBy(record => record, StringComparer.Ordinal),
                                Is.EqualTo(((JArray)values["records"])
                                    .Select(record => string.Join(" ", ((JObject)record).Properties()
                                        .OrderBy(item => item.Name, StringComparer.Ordinal)
                                        .Select(item => item.Name + "=" + (string)item.Value)))
                                    .Distinct(StringComparer.Ordinal)
                                    .OrderBy(record => record, StringComparer.Ordinal)),
                                objectText + ", what each version that inherits recorded");
                        }
                    }
                    else
                    {
                        var row = at.Row(profile);
                        Assert.That(row.Versions.Select(Text).OrderBy(text => text, StringComparer.Ordinal), Is.EqualTo(standing), objectText);
                        Assert.That(row.Lost.Count > 0, Is.EqualTo(lost.Contains(objectText)), objectText + ", lost");
                        Assert.That(row.Refuses, Is.EqualTo(Refuses(rules, objectText, lost)), objectText + ", refuses");

                        if (values != null)
                        {
                            // The measured reading gives a row that refuses no letter and no record,
                            // and the records of the others each once.
                            var takenOff = row.Refuses
                                ? Array.Empty<RowVersion>()
                                : row.Versions.Where(version => version.Kind != RowVersionKind.Grants).ToArray();
                            Assert.That(
                                row.Grants == null ? null : Letters(row.Grants),
                                Is.EqualTo((string)values["grants"]),
                                objectText + ", the letters all granting versions hold");
                            Assert.That(takenOff.Length > 0, Is.EqualTo((bool)values["takenOff"]), objectText + ", taken off");
                            Assert.That(
                                takenOff.Select(version => version.Kind == RowVersionKind.TakenOffRefusing ? "deny" : Letters(version.Letters))
                                    .Distinct(StringComparer.Ordinal)
                                    .OrderBy(record => record, StringComparer.Ordinal),
                                Is.EqualTo(((JArray)values["records"]).Select(record => (string)record).OrderBy(record => record, StringComparer.Ordinal)),
                                objectText + ", what each version that takes the rule off recorded");
                        }
                    }
                }
            }
        }

        [Test]
        public void A_reading_with_nothing_in_force_holds_no_rule()
        {
            var stored = StoredRules.Of(null);

            Assert.That(stored.Profiles, Is.Empty);
            Assert.That(stored.Named("pf"), Is.Empty);
            Assert.That(Same(stored.At(Node), NodeRule.None), Is.True);
            Assert.That(stored.At(null), Is.Not.Null);
        }

        [Test]
        public void The_rules_of_a_reading_name_their_profiles_and_the_nodes_whose_rows_grant()
        {
            var key = StoreComposer.Key("one");
            var store = new ComposedStore(key);
            var grants = Ruled(store, key, StoreComposer.RowObject(Root, "pf"), "row:RU");
            var empty = Ruled(store, key, StoreComposer.RowObject(Node, "pf"), "row:");
            var takenOff = Ruled(store, key, StoreComposer.RowObject(Node, "pg"), "off:R");
            var refusing = Ruled(store, key, StoreComposer.SwitchObject(Node), "sw:deny");
            store.RestateAll(new[] { grants, empty, takenOff, refusing });

            using (var reading = store.Read())
            {
                var stored = StoredRules.Of(reading);

                Assert.That(stored.Profiles, Is.EqualTo(new[] { "pf", "pg" }));
                Assert.That(stored.Named("pf"), Is.EquivalentTo(new[] { Root, Node }), "an empty rule among them");
                Assert.That(stored.Named("pg"), Is.Empty, "a row only taken off");
                Assert.That(stored.Named("px"), Is.Empty);
                Assert.That(stored.At(Node).Profiles, Is.EqualTo(new[] { "pf", "pg" }));
                Assert.That(stored.At(Node).Switch.Refuses, Is.True);
                Assert.That(Same(stored.At(new string('c', 32)), NodeRule.None), Is.True, "a node the reading holds nothing for");
            }
        }

        [Test]
        public void A_version_read_from_the_store_names_what_its_after_holds_in_its_order()
        {
            // Read where the acts or the editor ask, and the same answer however often it is asked.
            var key = StoreComposer.Key("one");
            var store = new ComposedStore(key);
            var named = new[] { new string('9', 32), new string('1', 32), new string('5', 32) };
            var row = StoreComposer.RowObject(Node, "pf");
            var sw = StoreComposer.SwitchObject(Node);
            var rowVersion = store.Add(StoreComposer.RuleVersion(key, row, "off:R", named));
            var switchVersion = store.Add(StoreComposer.RuleVersion(key, sw, "sw:inherit pf=R", named[1]));
            var alone = store.Add(StoreComposer.RuleVersion(key, StoreComposer.RowObject(Node, "pg"), "row:R"));
            store.RestateAll(new[]
            {
                StoreComposer.RuleVersionItem(rowVersion, row, "off:R", named),
                StoreComposer.RuleVersionItem(switchVersion, sw, "sw:inherit pf=R", named[1]),
                StoreComposer.RuleVersionItem(alone, StoreComposer.RowObject(Node, "pg"), "row:R"),
            });

            using (var reading = store.Read())
            {
                var rule = StoredRules.Of(reading).At(Node);

                Assert.That(rule.Row("pf").Versions.Single().After, Is.EqualTo(named));
                Assert.That(rule.Row("pf").Versions.Single().After, Is.SameAs(rule.Row("pf").Versions.Single().After));
                Assert.That(rule.Switch.Versions.Single().After, Is.EqualTo(new[] { named[1] }));
                Assert.That(rule.Row("pg").Versions.Single().After, Is.Empty);
            }
        }

        [Test]
        public void An_object_out_of_form_is_the_rule_of_no_node_and_stops_nothing()
        {
            var key = StoreComposer.Key("one");
            var store = new ComposedStore(key);
            var items = new List<string>
            {
                Ruled(store, key, StoreComposer.RowObject(Root, "pf"), "row:R"),
                Ruled(store, key, "rule:" + Node + "/default", "row:CRUSDLAP"),
                Ruled(store, key, "rule:" + Node.ToUpperInvariant() + "/pf", "row:CRUSDLAP"),
                Ruled(store, key, "switch:" + Node + "/pf", "sw:deny"),
                Ruled(store, key, "rule:" + Node, "row:"),
            };
            store.RestateAll(items);

            using (var reading = store.Read())
            {
                var stored = StoredRules.Of(reading);

                Assert.That(stored.Profiles, Is.EqualTo(new[] { "pf" }));
                Assert.That(Same(stored.At(Node), NodeRule.None), Is.True, "nothing read at the node, so nothing stopped there");
                Assert.That(Letters(AclResolver.Arrives(TestNode.Root("root").Holding(stored.At(Root)).Child("node").Holding(stored.At(Node)), "pf")), Is.EqualTo("R"));
            }
        }

        [Test]
        public void Nothing_a_store_holds_makes_the_reading_of_its_rules_throw()
        {
            // Versions of every object and text the texts' cases hold, in form and out of form,
            // with `after` naming each other, restated and not, and deleted with deletion records.
            var key = StoreComposer.Key("one");
            var random = new Random(617);
            var texts = ((JObject)RuleVectors.Records["texts"]).Properties().Select(text => text.Name)
                .Concat(new[] { string.Empty, "row:R", "off:", "sw:inherit pf=R" }).ToArray();
            var objects = new[]
            {
                StoreComposer.RowObject(Root, "pf"), StoreComposer.RowObject(Node, "pf"), StoreComposer.RowObject(Node, "pg"),
                StoreComposer.SwitchObject(Node), StoreComposer.SwitchObject(Root), "rule:" + Node, "switch:", "renew:" + Node,
            };

            for (var round = 0; round < 60; round++)
            {
                var store = new ComposedStore(key);
                var items = new List<string>();
                var made = new List<StoreComposer.Composed>();
                for (var i = 0; i < 1 + random.Next(8); i++)
                {
                    var objectName = objects[random.Next(objects.Length)];
                    var doc = texts[random.Next(texts.Length)];
                    var after = made.Count == 0 || random.Next(2) == 0 ? Array.Empty<string>() : new[] { made[random.Next(made.Count)].Hex };
                    var version = store.Add(StoreComposer.RuleVersion(key, objectName, doc, after));
                    made.Add(version);
                    if (random.Next(3) > 0)
                    {
                        items.Add(StoreComposer.RuleVersionItem(version, objectName, doc, after));
                    }

                    if (random.Next(5) == 0)
                    {
                        store.Drop(version);
                        store.Delete(version);
                    }
                }

                store.RestateAll(items);

                using (var reading = store.Read())
                {
                    Assert.DoesNotThrow(
                        () =>
                        {
                            var stored = StoredRules.Of(reading);
                            foreach (var walked in new[] { Root, Node }.Select(node => Under(stored.At(Root), stored.At(node))))
                            {
                                AclResolver.Arrives(walked, "pf");
                                AclResolver.Decide(walked, new[] { "pf", "pg" }, AclVerb.Read, AclReach.Protected);
                                AclResolver.Above(walked);
                            }
                        },
                        "round " + round);
                }
            }
        }

        [TestCaseSource(nameof(Texts))]
        public void A_text_out_of_form_refuses_where_it_is_found_and_a_text_in_form_reads_as_its_kind(string text, bool outOfForm)
        {
            // Under a root granting every letter, at a node holding the text as the version of a
            // row, or of the switch where it is a switch's text.
            var asSwitch = text.StartsWith("sw:", StringComparison.Ordinal);
            var rule = asSwitch ? Rules.Stored((null, text)) : Rules.Stored(("pf", text));
            var node = Under(Rules.Node(false, ("pf", "CRUSDLAP")), rule);

            if (outOfForm)
            {
                Assert.That(asSwitch ? rule.Switch.Refuses : rule.Row("pf").Refuses, Is.True);
                Assert.That(Letters(AclResolver.Arrives(node, "pf")), Is.Empty, "refused where it is found, never passed over");
                Assert.That(AclResolver.Decide(node, new[] { "pf" }, AclVerb.Read).Reason, Is.EqualTo("a rule here could not be read"));
                return;
            }

            Assert.That(asSwitch ? rule.Switch.Versions.Single().Kind != SwitchVersionKind.OutOfForm : rule.Row("pf").Versions.Single().Kind != RowVersionKind.OutOfForm, Is.True);
        }

        [Test]
        public void A_lost_version_refuses_its_row_or_the_switch_whatever_else_stands()
        {
            var lostRow = Rules.Stored(("pf", "row:CRUSDLAP"), ("pf", null));
            var lostSwitch = Rules.Stored((null, "sw:inherit pf=CRUSDLAP"), (null, null), ("pg", "off:R"));

            Assert.That(lostRow.Row("pf").Refuses, Is.True);
            Assert.That(lostRow.Row("pf").Answers, Is.False);
            Assert.That(lostRow.Row("pf").Lost, Has.Count.EqualTo(1));
            Assert.That(lostRow.Row("pf").Grants, Is.Null, "a row that refuses grants no letter, whatever stands beside the loss");
            Assert.That(lostRow.Profiles, Is.EqualTo(new[] { "pf" }));
            Assert.That(lostSwitch.Switch.Refuses, Is.True);
            Assert.That(lostSwitch.Switch.Lost, Has.Count.EqualTo(1));
            Assert.That(lostSwitch.Limit("pg").Count, Is.Zero);

            var underRow = Under(Rules.Node(false, ("pf", "RU")), lostRow);
            var underSwitch = Under(Rules.Node(false, ("pf", "RU"), ("pg", "RU")), lostSwitch);

            Assert.That(Letters(AclResolver.Arrives(underRow, "pf")), Is.Empty);
            Assert.That(AclResolver.Decide(underRow, new[] { "pf" }, AclVerb.Read).Reason, Is.EqualTo("a rule here was lost"));
            Assert.That(Letters(AclResolver.Arrives(underSwitch, "pf")), Is.Empty, "a profile the node does not name");
            Assert.That(Letters(AclResolver.Arrives(underSwitch, "pg")), Is.Empty, "and a profile whose row lets through");
            Assert.That(AclResolver.Decide(underSwitch, new[] { "pg" }, AclVerb.Read).Reason, Is.EqualTo("a rule here was lost"));
        }

        [Test]
        public void The_switch_lets_through_what_it_recorded_for_a_profile_and_nothing_for_a_profile_it_names_not()
        {
            var rule = Rules.Stored((null, "sw:inherit pf=R"), ("pg", "off:RU"));
            var node = Under(Rules.Node(false, ("pf", "RU"), ("pg", "RU"), ("px", "RU")), rule);

            Assert.That(Letters(rule.Switch.Limit("pf")), Is.EqualTo("R"));
            Assert.That(Letters(rule.Switch.Limit("pg")), Is.Empty, "the switch recorded nothing for it");
            Assert.That(Letters(AclResolver.Arrives(node, "pf")), Is.EqualTo("R"), "a profile the node does not name");
            Assert.That(Letters(AclResolver.Arrives(node, "pg")), Is.Empty, "a profile the switch does not name, read where its row lets through");
            Assert.That(Letters(AclResolver.Arrives(node, "px")), Is.Empty);
            Assert.That(
                AclResolver.Decide(node, new[] { "pf" }, AclVerb.Update).Reason,
                Is.EqualTo("a limit here does not let it through"));
        }

        [Test]
        public void A_switch_s_letters_are_never_read_where_the_row_answers()
        {
            var rule = Rules.Stored((null, "sw:inherit pf=R"), ("pg", "row:RU"));
            var node = Under(Rules.Node(false, ("pf", "RU"), ("pg", "R")), rule);

            Assert.That(Letters(AclResolver.Arrives(node, "pg")), Is.EqualTo("RU"), "the row answers by itself");
            Assert.That(Letters(rule.Limit("pg")), Is.Empty, "though the switch recorded nothing for the profile");
        }

        [Test]
        public void Versions_of_the_switch_that_differ_refuse_and_inheriting_versions_cut_to_what_all_recorded()
        {
            var mixed = Rules.Stored((null, "sw:deny"), (null, "sw:inherit pf=RU pg=RU"), ("pf", "row:R"));
            var inheriting = Rules.Stored((null, "sw:inherit pf=RU pg=R"), (null, "sw:inherit pf=R pg=RU"));

            Assert.That(mixed.Switch.Refuses, Is.True);
            Assert.That(mixed.Switch.Mixed, Is.True);
            Assert.That(inheriting.Switch.Refuses, Is.False);
            Assert.That(inheriting.Switch.Mixed, Is.False, "versions that inherit and differ in their letters are no mixed mark");
            Assert.That(Letters(inheriting.Switch.Limit("pf")), Is.EqualTo("R"));
            Assert.That(Letters(inheriting.Switch.Limit("pg")), Is.EqualTo("R"));

            var above = Rules.Node(false, ("pf", "RU"), ("pg", "RU"));
            Assert.That(Letters(AclResolver.Arrives(Under(above, mixed), "pf")), Is.EqualTo("R"), "the row answers before the switch");
            Assert.That(Letters(AclResolver.Arrives(Under(above, mixed), "pg")), Is.Empty);
            Assert.That(
                AclResolver.Decide(Under(above, mixed), new[] { "pg" }, AclVerb.Read).Reason,
                Is.EqualTo("a rule here refuses anything it does not name"));
            Assert.That(Letters(AclResolver.Arrives(Under(above, inheriting), "pf")), Is.EqualTo("R"));
            Assert.That(Letters(AclResolver.Arrives(Under(above, inheriting), "pg")), Is.EqualTo("R"));
        }

        [Test]
        public void A_switch_text_out_of_form_beside_a_version_that_inherits_refuses_and_is_no_mixed_mark()
        {
            var rule = Rules.Stored((null, "sw:inherit pf=RU"), (null, "sw:allow"));

            Assert.That(rule.Switch.Refuses, Is.True);
            Assert.That(rule.Switch.Mixed, Is.False);
            Assert.That(Letters(AclResolver.Arrives(Under(Rules.Node(false, ("pf", "RU")), rule), "pf")), Is.Empty);
        }

        [Test]
        public void A_row_reads_its_versions_as_the_walk_reads_them()
        {
            var granting = Rules.Stored(("pf", "row:RU"), ("pf", "row:RL")).Row("pf");
            Assert.That(granting.Answers, Is.True);
            Assert.That(Letters(granting.Grants), Is.EqualTo("R"));

            var mixed = Rules.Stored(("pf", "row:RU"), ("pf", "off:RL"), ("pf", "off:deny")).Row("pf");
            Assert.That(mixed.Answers, Is.False);
            Assert.That(mixed.Refuses, Is.False);
            Assert.That(Letters(mixed.Grants), Is.EqualTo("RU"));
            Assert.That(Letters(mixed.Limit), Is.Empty, "`off:deny` lets nothing through");

            var takenOff = Rules.Stored(("pf", "off:RU"), ("pf", "off:RL")).Row("pf");
            Assert.That(takenOff.Answers, Is.False);
            Assert.That(takenOff.Grants, Is.Null, "no version grants");
            Assert.That(Letters(takenOff.Limit), Is.EqualTo("R"));

            var none = NodeRule.None.Row("pf");
            Assert.That(none.Versions, Is.Empty);
            Assert.That(none.Answers, Is.False);
            Assert.That(none.Refuses, Is.False);
            Assert.That(none.Limit.SameAs(AclGrant.Every), Is.True, "a row with no version cuts nothing");
            Assert.That(NodeRule.None.Switch.Limit("pf").SameAs(AclGrant.Every), Is.True, "nor does a switch with no version");
            Assert.That(NodeRule.None.Limit("pf").SameAs(AclGrant.Every), Is.True);
            Assert.That(NodeRule.None.Profiles, Is.Empty);
        }

        [Test]
        public void A_node_s_rules_are_the_same_in_whatever_order_its_objects_and_versions_are_given()
        {
            var versions = new List<(string Profile, string Text)>
            {
                ("pf", "row:RU"), ("pf", "off:RL"), ("pg", "off:R"), ("pg", null), (null, "sw:inherit pf=RU pg=R"),
                (null, "sw:inherit pf=R"), ("px", "row:"), ("pf", "row:RLA"),
            };
            var first = Rules.Stored(versions.ToArray());
            var objects = Objects(first).ToList();
            Assert.That(objects.Sum(item => item.Standing.Count + item.Lost.Count), Is.EqualTo(versions.Count), "every version given");

            var random = new Random(617);
            for (var round = 0; round < 40; round++)
            {
                var shuffled = objects
                    .Select(item => new RuleObject(item.Profile, item.Standing.OrderBy(version => random.Next()), item.Lost.OrderBy(id => random.Next())))
                    .OrderBy(item => random.Next())
                    .ToList();

                Assert.That(Same(new NodeRule(shuffled), first), Is.True, "round " + round);
            }

            // And an object given in two parts reads as the object whole.
            var parted = objects.SelectMany(item => new[]
            {
                new RuleObject(item.Profile, item.Standing.Take(1), item.Lost),
                new RuleObject(item.Profile, item.Standing.Skip(1), Array.Empty<string>()),
            });
            Assert.That(Same(new NodeRule(parted), first), Is.True);
        }

        /// <summary>The store's entries read as the gate reads them, beside the store's own tree.</summary>
        private static StoreReading Read(JObject store)
        {
            var versions = new StoreVersions();
            var candidates = new List<StoreCandidate>();
            foreach (var entry in (JObject)store["entries"])
            {
                var current = Strings((JObject)entry.Value["strings"]);
                var history = ((JArray)entry.Value["history"]).Select(item => Strings((JObject)item)).Reverse();
                candidates.Add(versions.Candidate(StoreComposer.BytesOf(entry.Key), new[] { current }.Concat(history).ToArray()));
            }

            var deletions = ((JArray)store["deletions"]).Select(identifier => StoreComposer.BytesOf((string)identifier)).ToList();
            var tree = (JObject)store["tree"];
            var nodes = new[] { (string)tree["root"] }.Concat(((JObject)tree["groups"]).Properties().Select(group => group.Name))
                .Select(StoreComposer.BytesOf).ToList();

            return StoreReading.AfreshInTree(candidates, deletions, null, () => nodes);
        }

        /// <summary>The store's tree as nodes the resolver walks, each holding the rules read for it.</summary>
        private static IDictionary<string, TestNode> Tree(JObject tree, StoredRules stored)
        {
            var root = (string)tree["root"];
            var nodes = new Dictionary<string, TestNode>(StringComparer.Ordinal) { { root, TestNode.Root(root).Holding(stored.At(root)) } };
            var groups = ((JObject)tree["groups"]).Properties().ToDictionary(group => group.Name, group => (string)group.Value, StringComparer.Ordinal);

            while (nodes.Count <= groups.Count)
            {
                var added = 0;
                foreach (var group in groups.Where(group => !nodes.ContainsKey(group.Key) && nodes.ContainsKey(group.Value)))
                {
                    nodes[group.Key] = nodes[group.Value].Child(group.Key).Holding(stored.At(group.Key));
                    added++;
                }

                Assert.That(added, Is.GreaterThan(0), "every group of the tree is under the root");
            }

            return nodes;
        }

        /// <summary>
        /// Each verb a profile is granted at a node, and each verb granted with a switch of the
        /// same rule, as `V` and `V+A`, asked of the resolver one question at a time.
        /// </summary>
        private static IEnumerable<string> Capabilities(IAclNode node, string profile)
        {
            var asked = new[] { profile };
            var capabilities = new List<string>();
            foreach (var verb in AclVerbs.Grantable.Where(verb => !AclVerbs.IsScope(verb)))
            {
                var letter = VerbMask.LetterOf(verb).ToString();
                if (!AclResolver.Permits(node, asked, verb, AclReach.Fields))
                {
                    continue;
                }

                capabilities.Add(letter);
                if (verb == AclVerb.Strip || verb == AclVerb.Delete)
                {
                    // A removal asks no switch, so a switch beside it is no capability of its own.
                    continue;
                }

                if (AclResolver.Permits(node, asked, verb, AclReach.Attachments))
                {
                    capabilities.Add(letter + "+A");
                }

                if (AclResolver.Permits(node, asked, verb, AclReach.Protected))
                {
                    capabilities.Add(letter + "+P");
                }
            }

            return capabilities;
        }

        private static bool Refuses(JObject rules, string objectText, ISet<string> lost)
        {
            var values = rules["values"][objectText];
            return lost.Contains(objectText) || (values != null && (bool)values["refuses"]);
        }

        private static string Ruled(ComposedStore store, byte[] key, string objectName, string doc)
        {
            var version = store.Add(StoreComposer.RuleVersion(key, objectName, doc));
            return StoreComposer.RuleVersionItem(version, objectName, doc);
        }

        private static TestNode Under(NodeRule above, NodeRule rule)
        {
            return TestNode.Root("root").Holding(above).Child("node").Holding(rule);
        }

        private static string Text(RowVersion version)
        {
            switch (version.Kind)
            {
                case RowVersionKind.Grants:
                    return RuleText.Row(version.Letters);
                case RowVersionKind.TakenOff:
                    return RuleText.TakenOff(version.Letters);
                case RowVersionKind.TakenOffRefusing:
                    return RuleText.TakenOffRefusing;
                default:
                    return null;
            }
        }

        private static string Text(SwitchVersion version)
        {
            switch (version.Kind)
            {
                case SwitchVersionKind.Refuses:
                    return RuleText.SwitchRefusing;
                case SwitchVersionKind.Inherits:
                    return RuleText.Inheriting(version.Profiles.Select(profile => new KeyValuePair<string, AclGrant>(profile, version.LettersFor(profile))));
                default:
                    return null;
            }
        }

        /// <summary>What the store held of each object of a node's rules, built again from what was read.</summary>
        private static IEnumerable<RuleObject> Objects(NodeRule rule)
        {
            foreach (var profile in rule.Profiles)
            {
                var row = rule.Row(profile);
                yield return new RuleObject(profile, row.Versions.Select(version => new RuleVersion(version.Id, Text(version) ?? "out of form", version.After)), row.Lost);
            }

            yield return new RuleObject(
                null, rule.Switch.Versions.Select(version => new RuleVersion(version.Id, Text(version) ?? "out of form", version.After)), rule.Switch.Lost);
        }

        /// <summary>Whether two nodes' rules hold the same versions and read the same for every profile.</summary>
        private static bool Same(NodeRule left, NodeRule right)
        {
            if (!left.Profiles.SequenceEqual(right.Profiles)
                || !left.Switch.Versions.Select(version => version.Id + Text(version)).SequenceEqual(right.Switch.Versions.Select(version => version.Id + Text(version)))
                || !left.Switch.Lost.SequenceEqual(right.Switch.Lost)
                || left.Switch.Refuses != right.Switch.Refuses
                || left.Switch.Mixed != right.Switch.Mixed)
            {
                return false;
            }

            foreach (var profile in left.Profiles.Concat(new[] { "px", "nobody" }))
            {
                var one = left.Row(profile);
                var other = right.Row(profile);
                if (!one.Versions.Select(version => version.Id + Text(version)).SequenceEqual(other.Versions.Select(version => version.Id + Text(version)))
                    || !one.Lost.SequenceEqual(other.Lost)
                    || one.Refuses != other.Refuses
                    || one.Answers != other.Answers
                    || (one.Grants == null) != (other.Grants == null)
                    || (one.Grants != null && !one.Grants.SameAs(other.Grants))
                    || !one.Limit.SameAs(other.Limit)
                    || !left.Limit(profile).SameAs(right.Limit(profile)))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Letters(AclGrant grant)
        {
            return string.Concat(AclVerbs.Grantable.Where(grant.Holds).Select(VerbMask.LetterOf));
        }

        private static IReadOnlyDictionary<string, string> Strings(JObject strings)
        {
            return strings.Properties().ToDictionary(property => property.Name, property => (string)property.Value);
        }
    }
}
