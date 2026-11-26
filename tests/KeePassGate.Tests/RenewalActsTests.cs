using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The acts that write a standing answer to a renewal, `STORE.md` §618: a refusal and a lift,
    /// each naming what its writer was shown and nothing that arrived since.
    /// </summary>
    /// <remarks>
    /// The records are held to `tests/Shared/StoreRecordVectors.json`, printed by the measured
    /// code and never from this side: the members of each record an act composes, its nonce aside,
    /// and composed again with the vector's nonce, its content, its check, its identifier and every
    /// string of its entry. The stores an act is composed over are made by the tests' own composer.
    /// </remarks>
    [TestFixture]
    public sealed class RenewalActsTests
    {
        private const string Client = "1a9bd859c58e662a169cfafcbc5b4d0bdb6eaeb54802d57f609cf055fb627840";
        private const string Earlier = "2026-12-14T23:00:00Z";
        private const string Later = "2027-03-31T22:00:00Z";

        private const string Forever = "version, an answer refusing renewals forever";
        private const string UntilEarlier = "version, an answer refusing renewals until a moment";
        private const string UntilLater = "version, an answer refusing renewals until the later moment, made apart";
        private const string LostForever = "version, an answer refusing forever whose content no store holds, lost";
        private const string LiftNamingForever = "version, a lift naming the refusal it replaces";
        private const string LiftSettlingLost = "version, a lift settling a lost answer";

        [Test]
        public void A_refusal_on_a_store_that_holds_no_answer_names_nothing_and_holds_no_lost()
        {
            using (var reading = Measured().Read())
            {
                string refused;
                AssertMeasured(RenewalActs.Refuse(reading, Client, RenewalRefusal.Forever(), Array.Empty<string>(), out refused), Forever);
                Assert.That(refused, Is.Null);
                AssertMeasured(RenewalActs.Refuse(reading, Client, RenewalRefusal.Until(Moment(Earlier)), Array.Empty<string>(), out refused), UntilEarlier);
            }
        }

        [Test]
        public void A_lift_names_the_refusals_it_was_shown_that_still_stand()
        {
            var one = Measured(MeasuredVersion(Forever));
            using (var reading = one.Read())
            {
                AssertMeasured(RenewalActs.Lift(reading, Client, Ids(reading), Array.Empty<string>()), LiftNamingForever);
            }

            var two = Measured(MeasuredVersion(Forever), MeasuredVersion(UntilLater));
            using (var reading = two.Read())
            {
                Assert.That(Ids(reading), Has.Count.EqualTo(2));
                AssertMeasured(RenewalActs.Lift(reading, Client, Ids(reading), Array.Empty<string>()), "version, a lift naming two refusals made apart");
            }
        }

        [Test]
        public void A_lift_settles_in_lost_the_lost_versions_it_was_shown_and_names_none_of_them_in_after()
        {
            var lost = Measured();
            lost.Add(Admission());
            lost.Deleted.Add(MeasuredId(LostForever));
            using (var reading = lost.Read())
            {
                var shownLost = reading.InForce.LostOf(RenewalText.ObjectOf(Client));
                Assert.That(shownLost, Is.EqualTo(new[] { (string)Records()[LostForever]["id"] }), "the lost answer");
                AssertMeasured(RenewalActs.Lift(reading, Client, Array.Empty<string>(), shownLost), "version, a lift settling a lost answer");
            }

            var both = Measured(MeasuredVersion(UntilLater));
            both.Add(Admission());
            both.Deleted.Add(MeasuredId(LostForever));
            using (var reading = both.Read())
            {
                AssertMeasured(
                    RenewalActs.Lift(reading, Client, Ids(reading), reading.InForce.LostOf(RenewalText.ObjectOf(Client))),
                    "version, a lift replacing a refusal and settling a lost answer");
            }
        }

        [Test]
        public void A_refusal_names_the_lift_it_was_shown_and_what_the_lift_names()
        {
            // The lift's refusal is deleted, as a write of the plugin's deletes what it replaces;
            // its identifier is at the place of the client's answer, so the refusal carries it on.
            var store = Measured(MeasuredVersion(LiftNamingForever));
            store.Deleted.Add(MeasuredId(Forever));
            using (var reading = store.Read())
            {
                string refused;
                AssertMeasured(
                    RenewalActs.Refuse(reading, Client, RenewalRefusal.Until(Moment(Later)), Ids(reading), out refused),
                    "version, a refusal naming the lift it replaces and what the lift replaced");
            }
        }

        [Test]
        public void A_refusal_replacing_a_lift_that_settled_a_lost_answer_holds_no_lost()
        {
            var store = SettledByALift();
            using (var reading = store.Read())
            {
                Assert.That(StoredRenewals.Of(reading.InForce, Client).Lost, Is.Empty, "the lift settles the lost answer");
                string refused;
                var refusal = RenewalActs.Refuse(reading, Client, RenewalRefusal.Until(Moment(Later)), Ids(reading), out refused);
                Assert.That(refusal, Is.Not.Null, refused);
                var record = refusal.Records.Single();
                Assert.That(record.Member(StoreKinds.After), Is.EqualTo((string)Records()[LiftSettlingLost]["id"]), "the lift it replaces");
                Assert.That(record.Members.ContainsKey(StoreKinds.Lost), Is.False, "a refusal holds no lost");
                refusal.Forget();
            }
        }

        [Test]
        public void An_answer_a_lift_settled_is_lost_again_once_a_refusal_replaces_the_lift()
        {
            StoreComposer.Composed written;
            using (var reading = SettledByALift().Read())
            {
                string refused;
                var refusal = RenewalActs.Refuse(reading, Client, RenewalRefusal.Until(Moment(Later)), Ids(reading), out refused);
                var record = refusal.Records.Single();
                written = StoreComposer.Version(
                    StoreComposer.BytesOf((string)RuleVectors.Records["key"]), record.Member(StoreKinds.ObjectMember), record.Content);
                refusal.Forget();
            }

            // The refusal written, and the lift it replaces deleted with a deletion record.
            var after = Measured(written);
            after.Add(Admission());
            after.Deleted.Add(MeasuredId(LostForever));
            after.Deleted.Add(MeasuredId(LiftSettlingLost));
            using (var reading = after.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var standing = StoredRenewals.Of(reading.InForce, Client);
                Assert.That(standing.Lost, Is.EqualTo(new[] { (string)Records()[LostForever]["id"] }), "named by nothing, lost again");
                Assert.That(standing.Cause, Is.EqualTo(RenewalCause.Lost));
                Assert.That(standing.Refuses(Moment(Later).AddDays(1)), Is.True, "refused forever, past the refusal's moment as well");
            }
        }

        [Test]
        public void A_version_not_shown_is_named_by_nothing()
        {
            // A refusal that arrived by a merge while the dialog stood, and the press of a page that
            // showed a single refusal of two.
            var store = Measured(MeasuredVersion(Forever), MeasuredVersion(UntilLater));
            var forever = (string)Records()[Forever]["id"];
            using (var reading = store.Read())
            {
                string refused;
                var refusal = RenewalActs.Refuse(reading, Client, RenewalRefusal.Until(Moment(Earlier)), new[] { forever }, out refused);
                Assert.That(refusal.Records.Single().Member(StoreKinds.After), Is.EqualTo(forever));

                var lift = RenewalActs.Lift(reading, Client, new[] { forever }, Array.Empty<string>());
                Assert.That(lift.Records.Single().Member(StoreKinds.After), Is.EqualTo(forever));
                Assert.That(lift.Records.Single().Members.ContainsKey(StoreKinds.Lost), Is.False, "no lost where it settles nothing");

                var nothing = RenewalActs.Refuse(reading, Client, RenewalRefusal.Forever(), Array.Empty<string>(), out refused);
                Assert.That(nothing.Records.Single().Member(StoreKinds.After), Is.Empty, "nothing shown, nothing named");
            }
        }

        [Test]
        public void A_lift_whose_versions_were_all_replaced_since_comes_to_nothing_and_is_composed_again_under_a_later_key()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            var client = new string('3', 64);
            StoreSeeds.Refuse(store, client, RenewalRefusal.Forever());

            StoreAct press;
            using (var reading = store.Read())
            {
                var shown = StoredRenewals.Of(reading.InForce, client);
                press = RenewalActs.Lift(reading, client, shown.Standing, shown.Lost);
                Assert.That(press, Is.Not.Null);
            }

            using (var later = store.Read())
            {
                var again = press.Again(later);
                Assert.That(again.KeyName, Is.EqualTo(later.InForce.Key.Name), "under the key of the later reading");
                Assert.That(again.Records.Single().Member(StoreKinds.After), Is.EqualTo(press.Records.Single().Member(StoreKinds.After)));
                again.Forget();
            }

            // Answered under the dialog by the renewal dialog: the refusal shown is replaced.
            StoreSeeds.Refuse(store, client, RenewalRefusal.Until(Moment(Later)));
            using (var later = store.Read())
            {
                Assert.That(press.Again(later), Is.Null, "every version the press was shown was replaced since");
                var standing = StoredRenewals.Of(later.InForce, client);
                Assert.That(RenewalActs.Lift(later, client, Array.Empty<string>(), Array.Empty<string>()), Is.Null, "a lift shown nothing names nothing");
                Assert.That(standing.Answer.NotBefore, Is.EqualTo(Moment(Later)));
            }

            press.Forget();
        }

        [Test]
        public void An_answer_and_a_lift_written_replace_what_they_name_and_delete_it_with_a_deletion_record()
        {
            var store = new MemoryStore();
            StoreSeeds.Authority(store);
            var client = new string('3', 64);
            StoreSeeds.Refuse(store, client, RenewalRefusal.Forever());
            string first;
            using (var reading = store.Read())
            {
                first = StoredRenewals.Of(reading.InForce, client).Standing.Single();
            }

            StoreSeeds.Lift(store, client);
            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                var standing = StoredRenewals.Of(reading.InForce, client);
                Assert.That(standing.Answer, Is.Null, "the lift asks");
                Assert.That(standing.Standing, Has.Count.EqualTo(1));
                Assert.That(reading.InForce.Records.Select(record => record.Hex), Does.Not.Contain(first), "the refusal replaced is deleted");
                Assert.That(reading.Deletions.Select(KeePassGate.Hex.Of), Does.Contain(first), "with a deletion record");
                Assert.That(reading.InForce.HeadsInForce.Single().Form.Items.Count(item => item.Kind == StoreKinds.Version), Is.EqualTo(1), "the summary entry restates the lift alone");
            }
        }

        [Test]
        public void A_version_whose_item_does_not_fit_is_not_composed()
        {
            // A standing refusal whose `after` holds identifiers at the client's place up to the
            // ceiling: a version naming it and everything it names no longer fits.
            var key = StoreComposer.MeasuredKey("K");
            var store = new ComposedStore(key);
            var objectText = RenewalText.ObjectOf(Client);
            var named = new List<string>();
            var random = new Random(618);
            while ((named.Count + 1) * 33 < StoreFormat.MaxWritten - 100)
            {
                var check = new byte[32];
                random.NextBytes(check);
                named.Add(StoreComposer.HexOf(StoreComposer.IdentifierOf(key, "version", objectText, 0, StoreComposer.HexOf(check))));
            }

            var crowded = store.Add(StoreComposer.RuleVersion(key, objectText, RenewalText.Forever, named.ToArray()));
            store.RestateAll(new[] { StoreComposer.RuleVersionItem(crowded, objectText, RenewalText.Forever, named.ToArray()) });
            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), reading.Reason);
                string refused;
                Assert.That(RenewalActs.Refuse(reading, Client, RenewalRefusal.Until(Moment(Later)), Ids(reading), out refused), Is.Null);
                Assert.That(refused, Is.Not.Null.And.Not.Empty);
                Assert.That(RenewalActs.Lift(reading, Client, Ids(reading), Array.Empty<string>()), Is.Null);
                Assert.That(RenewalActs.Refuse(reading, Client, RenewalRefusal.Forever(), Array.Empty<string>(), out refused), Is.Not.Null, "naming nothing, it fits");
            }
        }

        [Test]
        public void An_answer_is_composed_only_about_a_client_key()
        {
            using (var reading = Measured().Read())
            {
                string refused;
                Assert.That(() => RenewalActs.Refuse(reading, Client.ToUpperInvariant(), RenewalRefusal.Forever(), Array.Empty<string>(), out refused), Throws.ArgumentException);
                Assert.That(() => RenewalActs.Lift(reading, Client.Substring(1), Array.Empty<string>(), Array.Empty<string>()), Throws.ArgumentException);
                Assert.That(() => RenewalActs.Refuse(reading, Client, RenewalRefusal.ThisSession(), Array.Empty<string>(), out refused), Throws.ArgumentException, "an answer for this session has no record");
            }
        }

        /// <summary>A store under the measured key holding these versions, each restated by a single summary entry.</summary>
        private static ComposedStore Measured(params StoreComposer.Composed[] versions)
        {
            var key = StoreComposer.BytesOf((string)RuleVectors.Records["key"]);
            var store = new ComposedStore(key);
            foreach (var version in versions)
            {
                store.Add(version);
            }

            store.RestateAll(versions.Select(version =>
            {
                var content = JObject.Parse(version.Strings[StoreComposer.Data]);
                var lost = (string)content["lost"];
                return StoreComposer.RuleVersionItem(
                    version, (string)content["object"], (string)content["doc"], Listed((string)content["after"]), Listed(lost));
            }));
            return store;
        }

        /// <summary>A record of the vectors, composed again under the measured key from its content.</summary>
        private static StoreComposer.Composed MeasuredVersion(string vector)
        {
            var key = StoreComposer.BytesOf((string)RuleVectors.Records["key"]);
            var measured = (JObject)Records()[vector];
            var composed = StoreComposer.Version(key, (string)measured["members"]["object"], (string)measured["content"]);
            Assert.That(composed.Hex, Is.EqualTo((string)measured["id"]), vector);
            return composed;
        }

        /// <summary>
        /// A store whose lift settles a lost refusal forever: the refusal deleted with a deletion
        /// record, and the client's key known to the file.
        /// </summary>
        private static ComposedStore SettledByALift()
        {
            var store = Measured(MeasuredVersion(LiftSettlingLost));
            store.Add(Admission());
            store.Deleted.Add(MeasuredId(LostForever));
            return store;
        }

        /// <summary>A record of kind `client` of the measured client, which makes its key known to the file.</summary>
        private static StoreComposer.Composed Admission()
        {
            var key = StoreComposer.BytesOf((string)RuleVectors.Records["key"]);
            return StoreComposer.Record(key, "client", "client:" + Client, StoreComposer.Content(("certificate", Client), ("client", Client)));
        }

        private static byte[] MeasuredId(string vector)
        {
            return StoreComposer.BytesOf((string)Records()[vector]["id"]);
        }

        private static IReadOnlyCollection<string> Ids(StoreReading reading)
        {
            return StoredRenewals.Of(reading.InForce, Client).Standing.ToList();
        }

        private static string[] Listed(string list)
        {
            return string.IsNullOrEmpty(list) ? Array.Empty<string>() : list.Split(' ');
        }

        private static DateTimeOffset Moment(string text)
        {
            return DateTimeOffset.ParseExact(
                text, Moments.WireFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
        }

        private static JObject Records()
        {
            return (JObject)RuleVectors.Records["records"];
        }

        /// <summary>
        /// The record an act composed, held to the measured record: the same members, the nonce
        /// aside, and composed again with the measured nonce, the same bytes.
        /// </summary>
        private static void AssertMeasured(StoreAct act, string vector)
        {
            Assert.That(act, Is.Not.Null, vector + ": an act");
            var record = act.Records.Single();
            var measured = (JObject)Records()[vector];
            var members = record.Members.Where(member => member.Key != StoreKinds.Nonce)
                .ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            var expected = ((JObject)measured["members"]).Properties()
                .Where(member => member.Name != StoreKinds.Nonce)
                .ToDictionary(member => member.Name, member => (string)member.Value, StringComparer.Ordinal);

            Assert.That(record.Kind, Is.EqualTo((string)measured["kind"]), vector + ": the kind");
            Assert.That(members, Is.EquivalentTo(expected), vector + ": the members");

            members[StoreKinds.Nonce] = (string)measured["members"][StoreKinds.Nonce];
            var again = StoreRecord.Compose(StoreComposer.BytesOf((string)RuleVectors.Records["key"]), record.Kind, members);
            Assert.That(again.Content, Is.EqualTo((string)measured["content"]), vector + ": the content");
            Assert.That(again.Check, Is.EqualTo((string)measured["check"]), vector + ": the check");
            Assert.That(again.Hex, Is.EqualTo((string)measured["id"]), vector + ": the identifier");
            Assert.That(
                again.Strings,
                Is.EquivalentTo(((JObject)measured["strings"]).Properties().ToDictionary(member => member.Name, member => (string)member.Value)),
                vector + ": the strings");
            act.Forget();
        }
    }
}
