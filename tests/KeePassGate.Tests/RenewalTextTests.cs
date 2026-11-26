using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Authority;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The texts of the versions of a standing answer to a renewal and their object, `STORE.md`
    /// §618: composed and read in a single place, strictly and totally, and held byte for byte to
    /// the texts and the records the measured code printed.
    /// </summary>
    /// <remarks>
    /// A reader that took a second spelling of an answer would read a refusal nobody gave, and a
    /// reader that throws would stop a hold over a text nobody of the plugin wrote. So every text
    /// read in form is held to compose back to itself, over the vectors' texts and over texts made
    /// up here.
    /// </remarks>
    [TestFixture]
    public sealed class RenewalTextTests
    {
        private const string Client = "1a9bd859c58e662a169cfafcbc5b4d0bdb6eaeb54802d57f609cf055fb627840";

        private static IEnumerable<TestCaseData> Texts()
        {
            foreach (var text in (JObject)RuleVectors.Records["renewalTexts"])
            {
                yield return new TestCaseData(text.Key).SetArgDisplayNames("\"" + text.Key + "\"");
            }
        }

        private static IEnumerable<TestCaseData> Answers()
        {
            return ((JObject)RuleVectors.Records["records"]).Properties()
                .Where(record => (string)record.Value["kind"] == "version"
                                 && ((string)record.Value["members"]["object"]).StartsWith("renew:", StringComparison.Ordinal))
                .Select(record => new TestCaseData(record.Name).SetArgDisplayNames(record.Name));
        }

        [TestCaseSource(nameof(Texts))]
        public void A_text_of_the_vectors_reads_in_form_or_out_of_form_as_printed(string text)
        {
            var printed = RuleVectors.Records["renewalTexts"][text];
            DateTimeOffset moment;
            var kind = RenewalText.Read(text, out moment);

            if (printed.Type == JTokenType.Null)
            {
                Assert.That(kind, Is.EqualTo(RenewalTextKind.OutOfForm));
                Assert.That(moment, Is.EqualTo(DateTimeOffset.MinValue), "no moment for a text out of form");
                return;
            }

            Assert.That(Word(kind), Is.EqualTo((string)printed["answer"]));
            if (kind == RenewalTextKind.Until)
            {
                Assert.That(Moments.Wire(moment), Is.EqualTo((string)printed["moment"]));
            }

            Assert.That(Composed(kind, moment), Is.EqualTo(text), "composed again from what was read, the same bytes");
        }

        [TestCaseSource(nameof(Answers))]
        public void A_version_of_an_answer_in_the_vectors_reads_its_client_and_its_text_as_composed(string name)
        {
            var record = (JObject)RuleVectors.Records["records"][name];
            var objectText = (string)record["members"]["object"];
            var doc = (string)record["members"]["doc"];

            string client;
            Assert.That(RenewalText.ReadObject(objectText, out client), Is.True, objectText);
            Assert.That(client, Is.EqualTo(Client));
            Assert.That(RenewalText.ObjectOf(client), Is.EqualTo(objectText));
            Assert.That((string)record["subject"], Is.EqualTo(objectText), "the subject is the whole object");
            Assert.That((int)record["number"], Is.EqualTo(0));

            DateTimeOffset moment;
            var kind = RenewalText.Read(doc, out moment);
            if (name.Contains("out of form"))
            {
                Assert.That(kind, Is.EqualTo(RenewalTextKind.OutOfForm), doc);
                return;
            }

            Assert.That(kind, Is.Not.EqualTo(RenewalTextKind.OutOfForm), doc);
            Assert.That(Composed(kind, moment), Is.EqualTo(doc));
        }

        [Test]
        public void The_texts_are_the_ones_the_records_hold()
        {
            Assert.That(RenewalText.Forever, Is.EqualTo("refuse:forever"));
            Assert.That(RenewalText.UntilStart, Is.EqualTo("refuse:until:"));
            Assert.That(RenewalText.Ask, Is.EqualTo("ask"));
            Assert.That(RenewalText.Of(RenewalRefusal.Forever()), Is.EqualTo("refuse:forever"));

            var moment = new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.FromHours(1));
            Assert.That(RenewalText.Of(RenewalRefusal.Until(moment)), Is.EqualTo("refuse:until:2026-12-14T23:00:00Z"), "in UTC, to the second");
            Assert.That(
                RenewalText.Of(RenewalRefusal.Until(moment.AddMilliseconds(999))), Is.EqualTo("refuse:until:2026-12-14T23:00:00Z"), "a fraction is not written");
            Assert.That(() => RenewalText.Of(RenewalRefusal.ThisSession()), Throws.ArgumentException, "an answer for this session has no record");
            Assert.That(RenewalText.ObjectOf(Client), Is.EqualTo("renew:" + Client));
        }

        [Test]
        public void An_object_that_holds_anything_but_a_client_key_is_the_object_of_nothing()
        {
            var outOfForm = new[]
            {
                null,
                string.Empty,
                "renew:",
                "renew:" + Client.Substring(1),
                "renew:" + Client + "0",
                "renew:" + Client.ToUpperInvariant(),
                "renew:" + Client.Substring(1) + "g",
                "renew: " + Client.Substring(1),
                "Renew:" + Client,
                "renew/" + Client,
                "switch:" + Client.Substring(0, 32),
                "rule:" + Client.Substring(0, 32) + "/pf",
            };
            foreach (var objectText in outOfForm)
            {
                string client;
                Assert.That(RenewalText.ReadObject(objectText, out client), Is.False, objectText ?? "null");
                Assert.That(client, Is.Null);
            }
        }

        [Test]
        public void No_text_makes_the_reader_throw_and_every_text_it_reads_composes_back_to_itself()
        {
            // The texts in form, each with every single character changed, dropped or added, and
            // random texts over the characters the texts use: a text read in form composes again to
            // the same bytes, and nothing throws.
            var seeds = new[] { RenewalText.Forever, RenewalText.Ask, RenewalText.UntilStart + "2026-12-14T23:00:00Z", RenewalText.UntilStart + "9999-12-31T23:59:59Z" };
            var alphabet = "0123456789-:TZtz.+ " + new string(seeds.SelectMany(seed => seed).Distinct().ToArray());
            var texts = new List<string>(seeds);
            foreach (var seed in seeds)
            {
                for (var at = 0; at < seed.Length; at++)
                {
                    texts.Add(seed.Remove(at, 1));
                    foreach (var c in alphabet)
                    {
                        texts.Add(seed.Substring(0, at) + c + seed.Substring(at + 1));
                        texts.Add(seed.Insert(at, c.ToString()));
                    }
                }
            }

            var random = new Random(618);
            var drawn = new StringBuilder();
            for (var made = 0; made < 20000; made++)
            {
                drawn.Clear().Append(RenewalText.UntilStart);
                var length = random.Next(0, 24);
                for (var at = 0; at < length; at++)
                {
                    drawn.Append(alphabet[random.Next(alphabet.Length)]);
                }

                texts.Add(drawn.ToString());
            }

            var read = 0;
            foreach (var text in texts)
            {
                DateTimeOffset moment;
                var kind = RenewalText.Read(text, out moment);
                if (kind != RenewalTextKind.OutOfForm)
                {
                    read++;
                    Assert.That(Composed(kind, moment), Is.EqualTo(text), text);
                }
            }

            Assert.That(read, Is.GreaterThan(seeds.Length), "the texts made up here hold texts in form");
        }

        [Test]
        public void A_text_of_a_rule_is_no_answer()
        {
            foreach (var text in ((JObject)RuleVectors.Records["texts"]).Properties().Select(text => text.Name))
            {
                DateTimeOffset moment;
                Assert.That(RenewalText.Read(text, out moment), Is.EqualTo(RenewalTextKind.OutOfForm), text);
            }
        }

        /// <summary>The measured reader's word for a kind.</summary>
        private static string Word(RenewalTextKind kind)
        {
            switch (kind)
            {
                case RenewalTextKind.Ask:
                    return "ask";
                case RenewalTextKind.Forever:
                    return "forever";
                case RenewalTextKind.Until:
                    return "until";
                default:
                    return null;
            }
        }

        /// <summary>The text composed again from what was read.</summary>
        private static string Composed(RenewalTextKind kind, DateTimeOffset moment)
        {
            switch (kind)
            {
                case RenewalTextKind.Ask:
                    return RenewalText.Ask;
                case RenewalTextKind.Forever:
                    return RenewalText.Of(RenewalRefusal.Forever());
                case RenewalTextKind.Until:
                    return RenewalText.Of(RenewalRefusal.Until(moment));
                default:
                    return null;
            }
        }
    }
}
