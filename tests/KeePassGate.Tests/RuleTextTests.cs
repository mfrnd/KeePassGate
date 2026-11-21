using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

// cspell:ignore CRUSDLAPC RLLA inheritpf CRUDLA
namespace KeePassGate.Tests
{
    /// <summary>
    /// The texts of the versions of a node's rules and their objects, `STORE.md` §617: composed and
    /// read in a single place, strictly and totally, and held byte for byte to the texts and the
    /// records the measured code printed.
    /// </summary>
    /// <remarks>
    /// A reader that took a second spelling of a rule would read a version the plugin never wrote
    /// as a version it wrote, and a reader that throws would stop a hold over a text nobody of the
    /// plugin wrote. So every text read in form is held to compose back to itself, over the
    /// vectors' texts and over texts made up here.
    /// </remarks>
    [TestFixture]
    public sealed class RuleTextTests
    {
        private const string Node = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        /// <summary>The longest text of a version of a row, as the records hold it: `off:` and every letter.</summary>
        private const int MostRowText = 12;

        /// <summary>
        /// The longest item of a version of the switch, as the records hold it: a space, the
        /// longest name the issuer issues, `=` and every letter.
        /// </summary>
        private const int MostSwitchItem = 74;

        private static IEnumerable<TestCaseData> Texts()
        {
            foreach (var text in (JObject)RuleVectors.Records["texts"])
            {
                yield return new TestCaseData(text.Key, text.Value.Type == JTokenType.Null)
                    .SetArgDisplayNames("\"" + text.Key + "\"");
            }
        }

        private static IEnumerable<TestCaseData> Versions()
        {
            return ((JObject)RuleVectors.Records["records"]).Properties()
                .Where(record => (string)record.Value["kind"] == "version")
                .Select(record => new TestCaseData(record.Name).SetArgDisplayNames(record.Name));
        }

        [TestCaseSource(nameof(Texts))]
        public void A_text_of_the_vectors_reads_in_form_or_out_of_form_as_printed(string text, bool outOfForm)
        {
            AclGrant row;
            IReadOnlyDictionary<string, AclGrant> items;
            var asRow = RuleText.ReadRow(text, out row);
            var asSwitch = RuleText.ReadSwitch(text, out items);

            if (outOfForm)
            {
                Assert.That(asRow, Is.EqualTo(RowVersionKind.OutOfForm), "as a row's text");
                Assert.That(asSwitch, Is.EqualTo(SwitchVersionKind.OutOfForm), "as a switch's text");
                Assert.That(row.Count, Is.Zero, "no letter for a text out of form");
                Assert.That(items, Is.Empty);
                return;
            }

            // In form as exactly one of the two, and composed again from what was read, the same
            // bytes: there is no second spelling of a rule.
            Assert.That(
                (asRow != RowVersionKind.OutOfForm) ^ (asSwitch != SwitchVersionKind.OutOfForm), Is.True,
                "in form as a row's text or as a switch's, and not as both");
            Assert.That(asRow != RowVersionKind.OutOfForm ? Composed(asRow, row) : Composed(asSwitch, items), Is.EqualTo(text));
        }

        [TestCaseSource(nameof(Versions))]
        public void A_version_of_the_vectors_reads_and_composes_to_its_object_and_its_text(string name)
        {
            var members = (JObject)RuleVectors.Records["records"][name]["members"];
            var objectText = (string)members["object"];
            var doc = (string)members["doc"];

            string node;
            string profile;
            Assert.That(RuleText.ReadObject(objectText, out node, out profile), Is.True, objectText);
            Assert.That(node, Is.EqualTo(Node));
            Assert.That(
                profile == null ? RuleText.SwitchObject(node) : RuleText.RowObject(node, profile), Is.EqualTo(objectText));

            if (profile == null)
            {
                IReadOnlyDictionary<string, AclGrant> items;
                var kind = RuleText.ReadSwitch(doc, out items);
                Assert.That(kind, Is.Not.EqualTo(SwitchVersionKind.OutOfForm), doc);
                Assert.That(Composed(kind, items), Is.EqualTo(doc));
            }
            else
            {
                AclGrant letters;
                var kind = RuleText.ReadRow(doc, out letters);
                Assert.That(kind, Is.Not.EqualTo(RowVersionKind.OutOfForm), doc);
                Assert.That(Composed(kind, letters), Is.EqualTo(doc));
            }
        }

        [Test]
        public void The_texts_and_their_bounds_are_the_ones_the_records_hold()
        {
            Assert.That(RuleText.Row(Rules.Letters("RU")), Is.EqualTo("row:RU"));
            Assert.That(RuleText.Row(AclGrant.Nothing), Is.EqualTo("row:"), "an empty rule");
            Assert.That(RuleText.TakenOff(Rules.Letters("RLA")), Is.EqualTo("off:RLA"));
            Assert.That(RuleText.TakenOff(AclGrant.Nothing), Is.EqualTo("off:"), "nothing arrived");
            Assert.That(RuleText.TakenOffRefusing, Is.EqualTo("off:deny"));
            Assert.That(RuleText.SwitchRefusing, Is.EqualTo("sw:deny"));
            Assert.That(RuleText.SwitchInheriting, Is.EqualTo("sw:inherit"));
            Assert.That(RuleText.RowStart, Is.EqualTo("row:"));
            Assert.That(RuleText.TakenOffStart, Is.EqualTo("off:"));

            Assert.That(RuleText.Row(AclGrant.Every), Is.EqualTo("row:CRUSDLAP"));
            Assert.That(RuleText.TakenOff(AclGrant.Every), Has.Length.EqualTo(MostRowText));
            Assert.That(RuleText.Row(AclGrant.Every), Has.Length.EqualTo(MostRowText));

            var longest = new string('q', 64);
            var inheriting = RuleText.Inheriting(new[] { Pair(longest, AclGrant.Every) });
            Assert.That(inheriting, Is.EqualTo("sw:inherit " + longest + "=CRUSDLAP"));
            Assert.That(inheriting.Length - RuleText.SwitchInheriting.Length, Is.EqualTo(MostSwitchItem));
        }

        [Test]
        public void A_switch_names_each_profile_once_ascending_by_ordinal_and_only_with_a_letter()
        {
            var arrived = new[]
            {
                Pair("pf", Rules.Letters("RLA")),
                Pair("pa", Rules.Letters("RL")),
                Pair("none", AclGrant.Nothing),
                Pair("Zeta", Rules.Letters("C")),
                Pair("p-b", Rules.Letters("U")),
                Pair("pg", Rules.Letters("R")),
            };

            Assert.That(RuleText.Inheriting(arrived), Is.EqualTo("sw:inherit Zeta=C p-b=U pa=RL pf=RLA pg=R"));
            Assert.That(RuleText.Inheriting(arrived.Reverse()), Is.EqualTo(RuleText.Inheriting(arrived)), "in any order given");
            Assert.That(RuleText.Inheriting(Array.Empty<KeyValuePair<string, AclGrant>>()), Is.EqualTo("sw:inherit"));
            Assert.That(RuleText.Inheriting(null), Is.EqualTo("sw:inherit"));
        }

        [TestCase("default")]
        [TestCase("Default")]
        [TestCase("p.f")]
        [TestCase("p f")]
        [TestCase("p=f")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq")]
        public void A_name_the_issuer_could_not_issue_is_left_out_of_a_switch(string name)
        {
            Assert.That(
                RuleText.Inheriting(new[] { Pair(name, Rules.Letters("R")), Pair("pf", Rules.Letters("U")) }),
                Is.EqualTo("sw:inherit pf=U"));
        }

        [Test]
        public void A_profile_given_twice_is_written_once_with_what_both_hold()
        {
            // Never a name twice, which reads out of form; and never more than either, so the
            // switch lets through no letter that an entry given for the profile left out.
            Assert.That(
                RuleText.Inheriting(new[] { Pair("pf", Rules.Letters("RU")), Pair("pf", Rules.Letters("RL")) }),
                Is.EqualTo("sw:inherit pf=R"));
            Assert.That(
                RuleText.Inheriting(new[] { Pair("pf", Rules.Letters("RU")), Pair("pf", Rules.Letters("L")) }),
                Is.EqualTo("sw:inherit"));
        }

        [TestCase("row:UR", TestName = "Row, letters out of order")]
        [TestCase("row:RR", TestName = "Row, a letter repeated")]
        [TestCase("row:r", TestName = "Row, a lowercase letter")]
        [TestCase("row: ", TestName = "Row, a character after the letters")]
        [TestCase("row:RU ", TestName = "Row, a space after the letters")]
        [TestCase("row:W", TestName = "Row, a letter of no verb")]
        [TestCase("row:CRUSDLAPC", TestName = "Row, longer than the longest")]
        [TestCase("row:deny", TestName = "Row, deny after row")]
        [TestCase("off:-", TestName = "Taken off, a dash")]
        [TestCase("off:ALR", TestName = "Taken off, letters out of order")]
        [TestCase("off:RLLA", TestName = "Taken off, a letter repeated")]
        [TestCase("off:R|U", TestName = "Taken off, a separator")]
        [TestCase("off:Deny", TestName = "Taken off, deny in another case")]
        [TestCase("off:deny ", TestName = "Taken off, a space after deny")]
        [TestCase("off:denyR", TestName = "Taken off, a letter after deny")]
        [TestCase("off", TestName = "Taken off, no colon")]
        [TestCase("ROW:R", TestName = "Row, the start in capitals")]
        [TestCase(" row:R", TestName = "Row, a space before")]
        [TestCase("cut:R", TestName = "A start of no text")]
        [TestCase("", TestName = "Nothing at all")]
        [TestCase(null, TestName = "No text")]
        public void A_row_s_text_out_of_form_reads_as_out_of_form_with_no_letter(string text)
        {
            AclGrant letters;
            Assert.That(RuleText.ReadRow(text, out letters), Is.EqualTo(RowVersionKind.OutOfForm));
            Assert.That(letters, Is.Not.Null);
            Assert.That(letters.Count, Is.Zero);
        }

        [TestCase("sw:", TestName = "Switch, the start alone")]
        [TestCase("sw:allow", TestName = "Switch, a word of neither")]
        [TestCase("sw:Inherit", TestName = "Switch, inherit in another case")]
        [TestCase("sw:deny pf=R", TestName = "Switch, deny with an item")]
        [TestCase("sw:deny ", TestName = "Switch, a space after deny")]
        [TestCase("sw:inherit ", TestName = "Switch, a space and no item")]
        [TestCase("sw:inherit  pf=R", TestName = "Switch, a second space")]
        [TestCase("sw:inherit pf=R ", TestName = "Switch, a space after the last item")]
        [TestCase("sw:inherit pf=R  pg=R", TestName = "Switch, two spaces between items")]
        [TestCase("sw:inherit pf", TestName = "Switch, an item with no equals")]
        [TestCase("sw:inherit pf=", TestName = "Switch, an item with no letter")]
        [TestCase("sw:inherit =R", TestName = "Switch, an item with no name")]
        [TestCase("sw:inherit pf=RR", TestName = "Switch, a letter repeated")]
        [TestCase("sw:inherit pf=UR", TestName = "Switch, letters out of order")]
        [TestCase("sw:inherit pf=r", TestName = "Switch, a lowercase letter")]
        [TestCase("sw:inherit pf=R=U", TestName = "Switch, a second equals")]
        [TestCase("sw:inherit pf=R pf=U", TestName = "Switch, a name repeated")]
        [TestCase("sw:inherit pg=R pf=R", TestName = "Switch, names out of order")]
        [TestCase("sw:inherit pa=R Zeta=C", TestName = "Switch, names out of ordinal order")]
        [TestCase("sw:inherit default=R", TestName = "Switch, the reserved word")]
        [TestCase("sw:inherit DEFAULT=R", TestName = "Switch, the reserved word in capitals")]
        [TestCase("sw:inherit p.f=R", TestName = "Switch, a name with a dot")]
        [TestCase("sw:inherit qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq=R", TestName = "Switch, a name of 65")]
        [TestCase("sw:inherit/deny", TestName = "Switch, a slash")]
        [TestCase("sw:inheritpf=R", TestName = "Switch, no space before the item")]
        [TestCase("row:R", TestName = "Switch, a row's text")]
        [TestCase("", TestName = "Switch, nothing at all")]
        [TestCase(null, TestName = "Switch, no text")]
        public void A_switch_s_text_out_of_form_reads_as_out_of_form_with_no_letter(string text)
        {
            IReadOnlyDictionary<string, AclGrant> items;
            Assert.That(RuleText.ReadSwitch(text, out items), Is.EqualTo(SwitchVersionKind.OutOfForm));
            Assert.That(items, Is.Not.Null);
            Assert.That(items, Is.Empty);
        }

        [Test]
        public void Each_text_in_form_reads_as_its_kind_with_its_letters()
        {
            AclGrant letters;
            Assert.That(RuleText.ReadRow("row:RU", out letters), Is.EqualTo(RowVersionKind.Grants));
            Assert.That(letters.SameAs(Rules.Letters("RU")), Is.True);
            Assert.That(RuleText.ReadRow("row:", out letters), Is.EqualTo(RowVersionKind.Grants));
            Assert.That(letters.Count, Is.Zero, "an empty rule");
            Assert.That(RuleText.ReadRow("off:CRUDLA", out letters), Is.EqualTo(RowVersionKind.TakenOff));
            Assert.That(letters.SameAs(Rules.Letters("CRUDLA")), Is.True);
            Assert.That(RuleText.ReadRow("off:", out letters), Is.EqualTo(RowVersionKind.TakenOff));
            Assert.That(letters.Count, Is.Zero, "nothing arrived");
            Assert.That(RuleText.ReadRow("off:deny", out letters), Is.EqualTo(RowVersionKind.TakenOffRefusing));
            Assert.That(letters.Count, Is.Zero);

            IReadOnlyDictionary<string, AclGrant> items;
            Assert.That(RuleText.ReadSwitch("sw:deny", out items), Is.EqualTo(SwitchVersionKind.Refuses));
            Assert.That(items, Is.Empty);
            Assert.That(RuleText.ReadSwitch("sw:inherit", out items), Is.EqualTo(SwitchVersionKind.Inherits));
            Assert.That(items, Is.Empty, "nothing arrived for any profile");
            Assert.That(RuleText.ReadSwitch("sw:inherit Zeta=C pf=RLA", out items), Is.EqualTo(SwitchVersionKind.Inherits));
            Assert.That(items.Keys, Is.EquivalentTo(new[] { "Zeta", "pf" }));
            Assert.That(items["pf"].SameAs(Rules.Letters("RLA")), Is.True);
            Assert.That(items["Zeta"].SameAs(Rules.Letters("C")), Is.True);
        }

        [Test]
        public void An_object_reads_as_a_node_and_a_profile_or_as_a_node_s_switch()
        {
            string node;
            string profile;

            Assert.That(RuleText.RowObject(Node, "pf"), Is.EqualTo("rule:" + Node + "/pf"));
            Assert.That(RuleText.SwitchObject(Node), Is.EqualTo("switch:" + Node));

            Assert.That(RuleText.ReadObject("rule:" + Node + "/pf", out node, out profile), Is.True);
            Assert.That(node, Is.EqualTo(Node));
            Assert.That(profile, Is.EqualTo("pf"));

            Assert.That(RuleText.ReadObject("switch:" + Node, out node, out profile), Is.True);
            Assert.That(node, Is.EqualTo(Node));
            Assert.That(profile, Is.Null);
        }

        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", TestName = "Object, a rule with no profile")]
        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/", TestName = "Object, an empty name")]
        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/default", TestName = "Object, the reserved word")]
        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/p.f", TestName = "Object, a name with a dot")]
        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/pf/pg", TestName = "Object, a second slash")]
        [TestCase("rule:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB/pf", TestName = "Object, a node in capitals")]
        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/pf", TestName = "Object, a node one short")]
        [TestCase("rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/pf", TestName = "Object, a node one long")]
        [TestCase("rule:gggggggggggggggggggggggggggggggg/pf", TestName = "Object, a node of no hexadecimal")]
        [TestCase("switch:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/pf", TestName = "Object, a switch with a profile")]
        [TestCase("switch:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", TestName = "Object, a switch one short")]
        [TestCase("switch:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", TestName = "Object, a switch in capitals")]
        [TestCase("renew:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", TestName = "Object, an answer to a renewal")]
        [TestCase("Rule:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/pf", TestName = "Object, the type in capitals")]
        [TestCase("sw:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", TestName = "Object, the model's own name of a switch")]
        [TestCase("", TestName = "Object, nothing at all")]
        [TestCase(null, TestName = "Object, no text")]
        public void An_object_in_no_other_form_is_the_rule_of_no_node(string text)
        {
            string node;
            string profile;
            Assert.That(RuleText.ReadObject(text, out node, out profile), Is.False);
            Assert.That(node, Is.Null);
            Assert.That(profile, Is.Null);
        }

        [Test]
        public void Every_mask_composes_and_reads_back_as_itself_in_every_text()
        {
            foreach (var letters in Masks())
            {
                AclGrant read;
                Assert.That(RuleText.ReadRow(RuleText.Row(letters), out read), Is.EqualTo(RowVersionKind.Grants));
                Assert.That(read.SameAs(letters), Is.True, RuleText.Row(letters));
                Assert.That(RuleText.ReadRow(RuleText.TakenOff(letters), out read), Is.EqualTo(RowVersionKind.TakenOff));
                Assert.That(read.SameAs(letters), Is.True, RuleText.TakenOff(letters));

                IReadOnlyDictionary<string, AclGrant> items;
                var inheriting = RuleText.Inheriting(new[] { Pair("pf", letters) });
                Assert.That(RuleText.ReadSwitch(inheriting, out items), Is.EqualTo(SwitchVersionKind.Inherits), inheriting);
                Assert.That(letters.Count == 0 ? !items.ContainsKey("pf") : items["pf"].SameAs(letters), Is.True, inheriting);
            }
        }

        [Test]
        public void No_text_makes_a_reader_throw_and_none_read_in_form_has_a_second_spelling()
        {
            // Every text of up to four characters after each start over an alphabet holding the
            // letters, their lowercase, a letter of no verb and every separator the texts use, and
            // switches of up to three items over a few names; then random texts. A text read in
            // form is composed again from what was read: any other bytes would be a second
            // spelling the composer never writes.
            const string alphabet = "CRUSDLAPrxW =:-";
            var starts = new[] { "row:", "off:", "sw:inherit ", "sw:", string.Empty };
            var read = 0;

            foreach (var start in starts)
            {
                foreach (var tail in Strings(alphabet, 4))
                {
                    read += Holds(start + tail);
                }
            }

            var names = new[] { "pf", "pg", "Zeta", "p-b", "default", "p.f" };
            foreach (var first in Items(names))
            {
                read += Holds("sw:inherit " + first);
                foreach (var second in Items(names))
                {
                    read += Holds("sw:inherit " + first + " " + second);
                    read += Holds("sw:inherit " + first + "  " + second);
                    foreach (var third in Items(names).Take(12))
                    {
                        read += Holds("sw:inherit " + first + " " + second + " " + third);
                    }
                }
            }

            var random = new Random(617);
            var text = new StringBuilder();
            for (var i = 0; i < 20000; i++)
            {
                text.Clear().Append(starts[random.Next(starts.Length)]);
                var length = random.Next(12);
                for (var j = 0; j < length; j++)
                {
                    text.Append(random.Next(8) == 0 ? (char)random.Next(0x10000) : alphabet[random.Next(alphabet.Length)]);
                }

                read += Holds(text.ToString());
            }

            Assert.That(read, Is.GreaterThan(1000), "the texts made up here hold texts in form");
        }

        /// <summary>
        /// Reads a text both ways; asserts that a text read in form composes back to itself; and
        /// answers 1 where it read in form.
        /// </summary>
        private static int Holds(string text)
        {
            AclGrant letters;
            IReadOnlyDictionary<string, AclGrant> items;
            var row = RuleText.ReadRow(text, out letters);
            var sw = RuleText.ReadSwitch(text, out items);

            string node;
            string profile;
            if (RuleText.ReadObject(text, out node, out profile))
            {
                Assert.That(profile == null ? RuleText.SwitchObject(node) : RuleText.RowObject(node, profile), Is.EqualTo(text));
            }

            if (row != RowVersionKind.OutOfForm)
            {
                Assert.That(Composed(row, letters), Is.EqualTo(text), "a second spelling of a row's text");
                return 1;
            }

            if (sw != SwitchVersionKind.OutOfForm)
            {
                Assert.That(Composed(sw, items), Is.EqualTo(text), "a second spelling of a switch's text");
                return 1;
            }

            Assert.That(letters.Count, Is.Zero);
            Assert.That(items, Is.Empty);
            return 0;
        }

        private static string Composed(RowVersionKind kind, AclGrant letters)
        {
            switch (kind)
            {
                case RowVersionKind.Grants:
                    return RuleText.Row(letters);
                case RowVersionKind.TakenOff:
                    return RuleText.TakenOff(letters);
                case RowVersionKind.TakenOffRefusing:
                    return RuleText.TakenOffRefusing;
                default:
                    return null;
            }
        }

        private static string Composed(SwitchVersionKind kind, IReadOnlyDictionary<string, AclGrant> items)
        {
            switch (kind)
            {
                case SwitchVersionKind.Refuses:
                    return RuleText.SwitchRefusing;
                case SwitchVersionKind.Inherits:
                    return RuleText.Inheriting(items);
                default:
                    return null;
            }
        }

        private static IEnumerable<AclGrant> Masks()
        {
            for (var bits = 0; bits < 1 << AclVerbs.Grantable.Count; bits++)
            {
                yield return AclGrant.Of(AclVerbs.Grantable.Where((verb, at) => (bits & (1 << at)) != 0));
            }
        }

        private static IEnumerable<string> Strings(string alphabet, int most)
        {
            yield return string.Empty;
            var level = new List<string> { string.Empty };
            for (var length = 1; length <= most; length++)
            {
                var next = new List<string>();
                foreach (var prefix in level)
                {
                    foreach (var letter in alphabet)
                    {
                        next.Add(prefix + letter);
                    }
                }

                foreach (var text in next)
                {
                    yield return text;
                }

                level = next;
            }
        }

        private static IEnumerable<string> Items(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                foreach (var mask in new[] { "R", "UR", "RU", "CRUSDLAP", string.Empty, "RR", "r" })
                {
                    yield return name + "=" + mask;
                }

                yield return name;
            }
        }

        private static KeyValuePair<string, AclGrant> Pair(string name, AclGrant letters)
        {
            return new KeyValuePair<string, AclGrant>(name, letters);
        }
    }
}
