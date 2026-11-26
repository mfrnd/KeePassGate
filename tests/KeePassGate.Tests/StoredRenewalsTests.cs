using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static KeePassGate.Tests.StoreRecords;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a reading of the store holds about a client's renewals, `STORE.md` §618: the standing
    /// and the lost versions of `renew:` and the client key, and the answer they give together,
    /// held to what the measured reading answers for every store of the meaning vectors.
    /// </summary>
    /// <remarks>
    /// The stores are composed by the measured code, never from this side, and read here by the
    /// product's own reading. The measured reading reads no clock; what an answer refuses is asked
    /// here at each of the three moments the vectors name.
    /// </remarks>
    [TestFixture]
    public sealed class StoredRenewalsTests
    {
        private static readonly string Client = Hex('1');
        private static readonly string OtherClient = Hex('2');

        private const string Earlier = "2026-12-14T23:00:00Z";
        private const string Later = "2027-03-31T22:00:00Z";

        private static IEnumerable<TestCaseData> Stores()
        {
            return ((JObject)RuleVectors.Meanings["meanings"]).Properties()
                .Where(store => store.Name.StartsWith("renewals:", StringComparison.Ordinal))
                .Select(store => new TestCaseData(store.Name).SetArgDisplayNames(store.Name));
        }

        [TestCaseSource(nameof(Stores))]
        public void A_store_answers_for_each_client_what_the_measured_reading_answers(string name)
        {
            // `tests/Shared/StoreMeaningVectors.json` holds, for each client key the measured
            // reading knows, the cause, the texts that stand, the lost versions, the moment from
            // which renewals are asked about again and whether a renewal is refused at each of
            // three moments of a clock.
            var store = (JObject)RuleVectors.Meanings["meanings"][name];
            var renewals = (JObject)store["renewals"];

            using (var reading = Read(store))
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var inForce = reading.InForce;
                foreach (var client in renewals.Properties())
                {
                    var expected = (JObject)client.Value;
                    var where = name + ", " + client.Name.Substring(0, 8);
                    var standing = StoredRenewals.Of(inForce, client.Name);

                    Assert.That(Cause(standing.Cause), Is.EqualTo((string)expected["cause"]), where + ", the cause");
                    Assert.That(Value(standing.Answer), Is.EqualTo((string)expected["value"]), where + ", the answer");
                    Assert.That(
                        standing.Answer == null || !standing.Answer.NotBefore.HasValue ? null : Moments.Wire(standing.Answer.NotBefore.Value),
                        Is.EqualTo((string)expected["notBefore"]),
                        where + ", from when renewals are asked about again");
                    Assert.That(standing.Lost, Is.EqualTo(((JArray)expected["lost"]).Select(id => (string)id)), where + ", the lost versions");
                    Assert.That(
                        inForce.VersionsOf(RenewalText.ObjectOf(client.Name)).Select(version => version.Members[StoreKinds.Doc]).OrderBy(doc => doc, StringComparer.Ordinal),
                        Is.EqualTo(((JArray)expected["standing"]).Select(doc => (string)doc)),
                        where + ", the texts that stand");
                    Assert.That(
                        standing.Standing,
                        Is.EqualTo(inForce.VersionsOf(RenewalText.ObjectOf(client.Name)).Select(version => version.Id)),
                        where + ", the versions that stand");
                    foreach (var at in (JObject)expected["refuses"])
                    {
                        Assert.That(standing.Refuses(Moment(at.Key)), Is.EqualTo((bool)at.Value), where + ", refused at " + at.Key);
                    }
                }

                // A client key the measured reading answers nothing for is no client of any answer.
                foreach (var objectText in inForce.Objects)
                {
                    string key;
                    if (RenewalText.ReadObject(objectText, out key))
                    {
                        Assert.That(renewals.Property(key), Is.Not.Null, name + ": an answer about a client the measured reading knows");
                    }
                }
            }
        }

        [Test]
        public void No_version_asks_and_each_text_alone_gives_its_own_answer()
        {
            Assert.That(StoredRenewals.Of(Answered().Read().InForce, Client).Cause, Is.EqualTo(RenewalCause.None));
            Assert.That(StoredRenewals.Of(Answered().Read().InForce, Client).Answer, Is.Null, "no version: the operator is asked");

            var texts = new Dictionary<string, (RenewalCause Cause, string Value)>
            {
                { RenewalText.Forever, (RenewalCause.Stands, "forever") },
                { RenewalText.UntilStart + Earlier, (RenewalCause.Stands, "until:" + Earlier) },
                { RenewalText.Ask, (RenewalCause.Stands, "ask") },
            };
            foreach (var text in texts)
            {
                var standing = Of(Answered(Answer(Client, text.Key)));
                Assert.That(standing.Cause, Is.EqualTo(text.Value.Cause), text.Key);
                Assert.That(Value(standing.Answer), Is.EqualTo(text.Value.Value), text.Key);
            }
        }

        [Test]
        public void Of_versions_made_apart_the_stricter_is_the_answer_and_of_two_moments_the_later_whatever_their_order()
        {
            // `STORE.md` §618: forever above the later moment above the earlier above `ask`,
            // compared as texts, so neither the order of the entries nor of the identifiers decides.
            var pairs = new[]
            {
                (RenewalText.Forever, RenewalText.UntilStart + Later, "forever"),
                (RenewalText.UntilStart + Earlier, RenewalText.UntilStart + Later, "until:" + Later),
                (RenewalText.Ask, RenewalText.UntilStart + Earlier, "until:" + Earlier),
                (RenewalText.Ask, RenewalText.Forever, "forever"),
            };
            foreach (var (first, second, value) in pairs)
            {
                foreach (var order in new[] { new[] { first, second }, new[] { second, first } })
                {
                    var standing = Of(Answered(order.Select(text => Answer(Client, text)).ToArray()));
                    Assert.That(standing.Standing, Has.Count.EqualTo(2), first + " beside " + second);
                    Assert.That(Value(standing.Answer), Is.EqualTo(value), first + " beside " + second);
                }
            }
        }

        [Test]
        public void A_lift_naming_a_refusal_asks_and_a_lift_that_did_not_see_it_leaves_it()
        {
            var refusal = Answer(Client, RenewalText.Forever);
            var seen = Answer(Client, RenewalText.Ask, refusal.Hex);
            Assert.That(Of(Answered(refusal, seen)).Answer, Is.Null, "the lift replaces what it names");
            Assert.That(Of(Answered(refusal, seen)).Standing, Is.EqualTo(new[] { seen.Hex }));

            var unseen = Answer(Client, RenewalText.Ask, new string('7', 32));
            Assert.That(Value(Of(Answered(refusal, unseen)).Answer), Is.EqualTo("forever"), "a lift made without having seen the refusal loses to it");
        }

        [Test]
        public void A_lost_version_refuses_forever_whatever_stands_until_a_version_of_the_answer_names_it_in_lost()
        {
            // `STORE.md` §617 and §618: a version beside its own deletion record that nothing in
            // force names or restates is lost, and refuses as forever; a lift naming it in `lost`
            // settles it.
            var lost = Answer(Client, RenewalText.Ask);
            var store = Answered(Answer(Client, RenewalText.Ask));
            store.Add(lost);
            store.Delete(lost);
            var standing = Of(store);
            Assert.That(standing.Cause, Is.EqualTo(RenewalCause.Lost));
            Assert.That(standing.Lost, Is.EqualTo(new[] { lost.Hex }));
            Assert.That(standing.Refuses(Moment(Later).AddYears(100)), Is.True, "forever");

            var settled = Answered(StoreComposer.RuleVersion(One, RenewalText.ObjectOf(Client), RenewalText.Ask, Array.Empty<string>(), new[] { lost.Hex }));
            settled.Add(lost);
            settled.Delete(lost);
            Assert.That(Of(settled).Cause, Is.EqualTo(RenewalCause.Stands), "settled in lost");
            Assert.That(Of(settled).Answer, Is.Null);
        }

        [Test]
        public void A_version_only_an_item_carries_stands_as_its_record_would()
        {
            var store = new ComposedStore(One);
            var carried = Answer(Client, RenewalText.Forever);
            store.RestateAll(new[] { ItemOf(carried, RenewalText.Forever) });

            var standing = Of(store);
            Assert.That(standing.Standing, Is.EqualTo(new[] { carried.Hex }));
            Assert.That(Value(standing.Answer), Is.EqualTo("forever"));
        }

        [Test]
        public void A_text_out_of_form_refuses_forever_whatever_else_stands_and_stops_nothing()
        {
            foreach (var odd in new[] { "allowed", RenewalText.UntilStart + "2026-12-15", RenewalText.Forever + " " })
            {
                var store = Answered(Answer(Client, odd), Answer(Client, RenewalText.Ask), Answer(OtherClient, RenewalText.Ask));
                using (var reading = store.Read())
                {
                    Assert.That(reading.State, Is.EqualTo(StoreState.Whole), odd + ": the store stops for nothing");
                    var standing = StoredRenewals.Of(reading.InForce, Client);
                    Assert.That(standing.Cause, Is.EqualTo(RenewalCause.Unreadable), odd);
                    Assert.That(Value(standing.Answer), Is.EqualTo("forever"), odd);
                    Assert.That(StoredRenewals.Of(reading.InForce, OtherClient).Answer, Is.Null, odd + ": another client is answered as before");
                }
            }
        }

        [Test]
        public void A_refusal_until_a_moment_refuses_before_the_moment_and_not_from_it()
        {
            var standing = Of(Answered(Answer(Client, RenewalText.UntilStart + Earlier)));
            var moment = Moment(Earlier);
            Assert.That(standing.Refuses(moment.AddSeconds(-1)), Is.True);
            Assert.That(standing.Refuses(moment), Is.False);
            Assert.That(standing.Refuses(moment.AddDays(1)), Is.False);
            Assert.That(standing.Answer.NotBefore, Is.EqualTo(moment));
        }

        [Test]
        public void An_object_that_holds_no_client_key_is_the_object_of_nothing()
        {
            var upper = Hex('C');
            var store = Answered(Answer(upper, RenewalText.Forever), Answer(Client.Substring(1), RenewalText.Forever));
            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                Assert.That(StoredRenewals.Of(reading.InForce, upper).Answer, Is.Null, "a key in upper case");
                Assert.That(StoredRenewals.Of(reading.InForce, Client.Substring(1)).Answer, Is.Null, "a key a character short");
                Assert.That(StoredRenewals.Of(reading.InForce, Client).Answer, Is.Null, "the client whose key a shorter object begins");
            }
        }

        private static StoreComposer.Composed Answer(string client, string doc, params string[] after)
        {
            return StoreComposer.RuleVersion(One, RenewalText.ObjectOf(client), doc, after);
        }

        private static string ItemOf(StoreComposer.Composed version, string doc, params string[] after)
        {
            var content = JObject.Parse(version.Strings[StoreComposer.Data]);
            return StoreComposer.RuleVersionItem(version, (string)content["object"], doc, after);
        }

        /// <summary>A store under `One` of these versions, each restated by a single summary entry.</summary>
        private static ComposedStore Answered(params StoreComposer.Composed[] versions)
        {
            var store = new ComposedStore(One);
            foreach (var version in versions)
            {
                store.Add(version);
            }

            store.RestateAll(versions.Select(version =>
            {
                var content = JObject.Parse(version.Strings[StoreComposer.Data]);
                var lost = (string)content["lost"];
                return StoreComposer.RuleVersionItem(
                    version,
                    (string)content["object"],
                    (string)content["doc"],
                    Listed((string)content["after"]),
                    Listed(lost));
            }));
            return store;
        }

        private static string[] Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? Array.Empty<string>() : list.Split(' ');
        }

        private static RenewalStanding Of(ComposedStore store)
        {
            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                return StoredRenewals.Of(reading.InForce, Client);
            }
        }

        /// <summary>The measured reading's word for a cause.</summary>
        private static string Cause(RenewalCause cause)
        {
            return cause.ToString().ToLowerInvariant();
        }

        /// <summary>The measured reading's word for an answer: `ask`, `forever`, or `until:` and the moment.</summary>
        private static string Value(RenewalRefusal answer)
        {
            if (answer == null)
            {
                return "ask";
            }

            return answer.NotBefore.HasValue ? "until:" + Moments.Wire(answer.NotBefore.Value) : "forever";
        }

        private static DateTimeOffset Moment(string text)
        {
            return DateTimeOffset.ParseExact(text, Moments.WireFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        /// <summary>The store's entries read as the gate reads them, beside no tree.</summary>
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
            return StoreReading.AfreshInTree(candidates, deletions, null, () => Array.Empty<byte[]>());
        }

        private static IReadOnlyDictionary<string, string> Strings(JObject strings)
        {
            return strings.Properties().ToDictionary(property => property.Name, property => (string)property.Value);
        }
    }
}
