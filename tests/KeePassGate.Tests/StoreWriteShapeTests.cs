using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The shape of the product's writes, held to the writes the measured model made in the same
    /// sequence, `STORE.md` §627: what each write deletes and tells of as deleted, what is present
    /// after it, how high its summary entry is, how many summary entries it names and how many
    /// items it holds, how many store keys stand, and what the store answers.
    /// </summary>
    /// <remarks>
    /// `tests/Shared/StoreWriteVectors.json` holds the file after each act of sequences that
    /// `model/crdt_model.py` played, written out by `probe/store.py`'s `Encoder`. Its records hold
    /// stand-ins, short names and nonces of the model's own, so the product is held by shape and
    /// never by bytes: by kind, by the number a summary entry carries, and by count.
    ///
    /// **Every sequence is played, through the product's own acts.** The commands of the authority
    /// are `AuthorityService`'s, and a pairing, a renewal and a revocation are `AdmissionActs`'s,
    /// each written as the store writes an act: a certificate is issued for each of the model's
    /// clients, a pairing admits it under the authority served, a renewal admits a further
    /// certificate under the same client key, and a revocation names that client key. The
    /// admission the model's start holds is put into the store beside the product's Create, so
    /// the records of kind `client` are compared like every other kind: what each write deletes of
    /// them because a revocation or a retirement refuses them, `STORE.md` §627, and what stays. Not
    /// compared are the versions of the root's rows the model's start holds, a kind these
    /// sequences do not write, where the product's start holds the version record a Create writes,
    /// so neither the model's items `version` nor the product's item `format` are counted among a
    /// summary entry's items; nor are the names outside the sequence
    /// of names, since the model's pairing writes no addition for a name it approves and the
    /// product's does, `STORE.md` §627. The sequence of names compares both kinds of a name.
    /// Where the model writes otherwise than `STORE.md` fixes, as a renewal's admission without
    /// `from`, the shape is the same and the members are left to the product's own tests.
    ///
    /// **The sequences of rules are played through the acts of an OK** on a node's dialog,
    /// `RuleActs`, each press over the rules the copy holds as the model composes every act at its
    /// write, with what arrives taken from the model's own answer before the act, and the write
    /// above carried down the sequence's tree. Their start holds the root's rows as the model's
    /// does, restated by its first summary entry. Held beside the shape: the versions written and
    /// deleted, by object, the texts that stand, what every node gives every profile, and what
    /// each new version names, the model's identifiers taken for the product's, the versions
    /// renewed below the node acted on among them.
    /// </remarks>
    [TestFixture]
    public sealed partial class StoreWriteShapeTests
    {
        /// <summary>The kinds compared: every kind this build writes or the sequences add.</summary>
        private static readonly string[] Compared = { "authority", "client", "head", "retired", "revoked" };

        /// <summary>The kinds the sequence of names compares: those above and the two of a name.</summary>
        private static readonly string[] NameKinds = Compared.Concat(new[] { "profile", "removed" }).ToArray();

        private static readonly DateTimeOffset Then = new DateTimeOffset(2026, 8, 15, 9, 14, 22, TimeSpan.Zero);

        [Test]
        public void One_copy_paired_revoked_recreated_deleted_and_created_again_writes_what_the_model_wrote()
        {
            var steps = (JArray)Vectors()["sequences"]["one copy"]["steps"];
            using (var clients = new Clients())
            {
                var store = new MemoryStore();
                StoreSeeds.Authority(store);
                Started(store, clients);

                foreach (var step in steps)
                {
                    AssertShape(step, store, () => Played(store, step, clients));
                }
            }
        }

        [Test]
        public void Two_copies_that_each_recreate_synchronized_and_kept_write_what_the_model_wrote()
        {
            var steps = (JArray)Vectors()["sequences"]["two copies"]["steps"];
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                var second = first.Copy();

                AssertShape(steps[0], first, () => Service(first).Recreate());
                AssertShape(steps[1], second, () => Service(second).Recreate());
                AssertShape(steps[2], first, () =>
                {
                    first.Synchronize(second);
                    return AuthorityChange.Done();
                });

                var standing = first.Read().Answering.Select(record => record.Form.Member("id")).ToList();
                Assume.That(standing, Has.Count.EqualTo(2));
                AssertShape(steps[3], first, () => Service(first).Keep(standing[0], standing));

                Assert.That(StoreAuthorities.Served(first.Read()), Is.EqualTo(standing[0]), "the answer the model read: the authority kept is served");
            }
        }

        [Test]
        public void Two_copies_one_revoking_a_client_the_other_renews_synchronized_admit_nobody_of_that_client()
        {
            // `STORE.md` §615: the revocation names the client key, so the certificate renewed in a
            // copy that had not read it is refused once the copies meet, in each of them.
            var steps = (JArray)Vectors()["sequences"]["two copies, revoked in the first and renewed in the second"]["steps"];
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                var second = first.Copy();

                AssertShape(steps[0], first, () => Played(first, steps[0], clients));
                AssertShape(steps[1], second, () => Played(second, steps[1], clients));
                AssertShape(steps[2], first, () =>
                {
                    first.Synchronize(second);
                    return AuthorityChange.Done();
                });
                AssertShape(steps[3], second, () =>
                {
                    second.Synchronize(first);
                    return AuthorityChange.Done();
                });

                foreach (var copy in new[] { first, second })
                {
                    using (var reading = copy.Read())
                    {
                        Assert.That(StoreAdmissions.Admitted(reading, clients.Admission("c0.1").AdmissionKey), Is.Null,
                            "the certificate renewed in the copy that had not read the revocation is refused");
                        Assert.That(StoreAdmissions.Admitted(reading, clients.Admission("c0").AdmissionKey), Is.Null);
                    }
                }

                Assert.That(Held(first), Is.EquivalentTo(Held(second)), "and both copies hold the same records");
            }
        }

        [Test]
        public void Two_copies_that_add_and_remove_names_apart_synchronized_name_what_the_model_named()
        {
            // `STORE.md` §616 and §644 item 32: a name removed in each copy and added again in the
            // first is off the list once the copies meet, a name added in each is on it by two
            // additions, and the name added again after both removals is on it in both copies,
            // which then hold the same records.
            var steps = (JArray)Vectors()["sequences"]["two copies, a name removed in each and added again in the first, another added in each"]["steps"];
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                StartedNamed(first, "pf");
                var second = first.Copy();
                var copies = new[] { first, second };

                for (var at = 0; at < steps.Count; at++)
                {
                    var step = steps[at];
                    var copy = copies[(int)step["copy"]];
                    var other = copies[1 - (int)step["copy"]];
                    AssertShape(step, copy, () => PlayedNamed(copy, other, step), NameKinds, (string)step["command"] == "Synchronize");

                    if (at == 6)
                    {
                        foreach (var met in copies)
                        {
                            Assert.That(NamesOf(met), Is.EqualTo(new[] { "pg" }), "once the copies meet: pf off the list, pg on it");
                            using (var reading = met.Read())
                            {
                                Assert.That(
                                    reading.InForce.Additions.Count(addition => addition.Form.Member(StoreKinds.NameMember) == "pg"),
                                    Is.EqualTo(2),
                                    "pg by two additions");
                            }
                        }
                    }
                }

                foreach (var met in copies)
                {
                    Assert.That(NamesOf(met), Is.EqualTo(new[] { "pf", "pg" }), "pf added again after both removals, in both copies");
                }

                Assert.That(Held(first), Is.EquivalentTo(Held(second)), "and both copies hold the same records");
            }
        }

        [TestCaseSource(nameof(RuleSequences))]
        public void A_sequence_of_rules_at_single_nodes_writes_and_reads_what_the_model_did(string name)
        {
            var lost = name.StartsWith("lost, ", StringComparison.Ordinal);
            var first = StoreFor((JObject)Vectors()["sequences"][name]);
            var sequence = lost ? Renamed((JObject)Vectors()["sequences"][name], first) : (JObject)Vectors()["sequences"][name];
            if (lost)
            {
                // The model reads a deletion record at every node of its tree, a node no version
                // names among them, as a gate reads the database's tree.
                first.Tree.AddRange(new[] { (string)sequence["tree"]["root"] }
                    .Concat(((JObject)sequence["tree"]["groups"]).Properties().Select(group => group.Name))
                    .Select(StoreComposer.BytesOf));
            }

            var mapped = StartedRules(first, sequence);
            var copies = new[] { first, first.Copy() };
            var gives = new[] { (JObject)sequence["start"]["answer"]["rules"]["gives"], (JObject)sequence["start"]["answer"]["rules"]["gives"] };
            var seenModel = new HashSet<string>(ModelVersions((JObject)sequence["start"]["file"]).Keys, StringComparer.Ordinal);
            var seenProduct = new HashSet<string>(mapped.Values, StringComparer.Ordinal);

            foreach (var step in (JArray)sequence["steps"])
            {
                var command = (string)step["command"];
                var copy = (int)step["copy"];
                var store = copies[copy];
                var where = name.Substring(0, name.IndexOf(':')) + ", step " + ((JArray)sequence["steps"]).IndexOf(step) + ", " + command;
                var before = Labels(store);

                if (command == "Synchronize")
                {
                    var act = (JArray)step["act"];
                    Assert.That((int)act[2], Is.EqualTo(copy), where + ": the copy merged into");
                    store.Synchronize(copies[(int)act[3]]);
                }
                else if (!PlayedApart(store, sequence, step, mapped, where))
                {
                    PlayedRules(store, sequence, step, gives[copy], where);
                }

                AssertRules(step, store, before, command != "Synchronize", where);
                AssertNamed(step, store, mapped, seenModel, seenProduct, where);
                gives[copy] = (JObject)step["answer"]["rules"]["gives"];
            }
        }

        [TestCaseSource(nameof(AnswerSequences))]
        public void A_sequence_of_answers_to_renewals_writes_and_reads_what_the_model_did(string name)
        {
            // `STORE.md` §618: each step's answer about each client the model answers for, by its
            // value, its cause, how many versions stand and are lost and whether it refuses at
            // each moment of the clock; how many versions of answers are present; and whether a
            // step the model played only where its copy's reading allowed it was played here. The
            // acts are the product's own, each naming what its copy was shown: a dialog's reading
            // where the sequence shows an answer, and the reading at the write otherwise; Seal
            // again is the seal's act alone, composed over the store at its write.
            var sequence = (JObject)VectorsAsText()["sequences"][name];
            var moments = ((JArray)sequence["clock"]).Select(moment => (string)moment).ToList();
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                var copies = new[] { first, first.Copy() };
                var clocks = new[] { 0, 0 };
                var shown = new[] { new Dictionary<string, RenewalStanding>(StringComparer.Ordinal), new Dictionary<string, RenewalStanding>(StringComparer.Ordinal) };
                var keys = new Dictionary<string, string>(StringComparer.Ordinal) { { (string)sequence["clients"]["c0"], clients.Admission("c0").ClientKey } };

                foreach (var step in (JArray)sequence["steps"])
                {
                    var command = (string)step["command"];
                    var copy = (int)step["copy"];
                    var store = copies[copy];
                    var act = (JArray)step["act"];
                    var where = name.Substring(0, name.IndexOf(':')) + ", step " + ((JArray)sequence["steps"]).IndexOf(step) + ", " + command;
                    var played = PlayedAnswer(store, copies, act, command, clients, moments[clocks[copy]], shown[copy], where);
                    if (command == "Set clock")
                    {
                        // A clock set where it stands moves nothing, and the model plays nothing.
                        played = clocks[copy] != (int)act[2];
                        clocks[copy] = (int)act[2];
                    }
                    else if (command == "Pair")
                    {
                        keys[(string)sequence["clients"]["c1"]] = clients.Admission("c1").ClientKey;
                    }

                    Assert.That(played, Is.EqualTo((bool)step["played"]), where + ": played where the model's copy played it");
                    AssertAnswers(step, store, keys, moments, where);
                }
            }
        }

        /// <summary>The sequences of the standing answers to a renewal, A1 to A14, by name.</summary>
        private static IEnumerable<TestCaseData> AnswerSequences()
        {
            return ((JObject)Vectors()["sequences"]).Properties()
                .Select(sequence => sequence.Name)
                .Where(name => name.StartsWith("answers, ", StringComparison.Ordinal))
                .Select(name => new TestCaseData(name).SetArgDisplayNames(name.Substring(0, name.IndexOf(':'))));
        }

        /// <summary>
        /// A step of a sequence of answers, played in this copy by the product's acts; whether it
        /// was played, which an answer and a renewal are only where the copy's reading allows them
        /// at its clock, and a lift only where it names something.
        /// </summary>
        private static bool PlayedAnswer(
            MemoryStore store, MemoryStore[] copies, JArray act, string command, Clients clients, string clock, Dictionary<string, RenewalStanding> shown, string where)
        {
            var now = DateTimeOffset.Parse(clock, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
            switch (command)
            {
                case "Synchronize":
                    store.Synchronize(copies[(int)act[3]]);
                    return true;
                case "Set clock":
                    return true;
                case "Pair":
                    StoreSeeds.Admit(store, clients.Paired(StoreSeeds.Served(store)));
                    return true;
                case "Revoke":
                    StoreSeeds.Revoke(store, clients.Admission((string)act[2]).ClientKey);
                    return true;
                case "Show answer":
                    using (var reading = store.Read())
                    {
                        var client = clients.Admission((string)act[2]).ClientKey;
                        shown[client] = StoredRenewals.Of(reading.InForce, client);
                    }

                    return true;
                case "Refuse forever":
                case "Refuse until a date":
                case "Allow renewal":
                    return Answered(store, act, command, clients, now, shown, where);
                case "Renew":
                    using (var reading = store.Read())
                    {
                        if (StoredRenewals.Of(reading.InForce, clients.Admission((string)act[2]).ClientKey).Refuses(now))
                        {
                            return false;
                        }
                    }

                    StoreSeeds.Admit(store, clients.Renewed((string)act[2], StoreSeeds.Served(store)));
                    return true;
                case "Lose version":
                    using (var reading = store.Read())
                    {
                        var key = (byte[])reading.InForce.Key.Bytes.Clone();
                        try
                        {
                            var client = clients.Admission(((string)act[2]).Substring("renew:".Length)).ClientKey;
                            store.Delete(StoreComposer.IdentifierOf(key, "version", RenewalText.ObjectOf(client), 0, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")));
                        }
                        finally
                        {
                            Array.Clear(key, 0, key.Length);
                        }
                    }

                    return true;
                case "Write text out of form":
                    OutOfForm(store, clients.Admission((string)act[2]).ClientKey, where);
                    return true;
                case "Delete by hand":
                    DeletedNewest(store, (JArray)act[2], (int)act[3] == 1, clients, where);
                    return true;
                case "Seal again":
                    return Sealed(store, where);
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, "a command this test does not play");
            }
        }

        /// <summary>
        /// A refusal or a lift, given only where the copy's reading asks or refuses at its clock,
        /// naming what a dialog showed where one was shown and what the reading holds otherwise.
        /// </summary>
        private static bool Answered(MemoryStore store, JArray act, string command, Clients clients, DateTimeOffset now, Dictionary<string, RenewalStanding> shown, string where)
        {
            var client = clients.Admission((string)act[2]).ClientKey;
            StoreAct answer;
            using (var reading = store.Read())
            {
                var standing = StoredRenewals.Of(reading.InForce, client);
                RenewalStanding seen;
                if (!shown.TryGetValue(client, out seen))
                {
                    seen = standing;
                }

                // Given only where what the dialog showed asks, and lifted only where what the
                // page showed refuses, at the copy's clock.
                shown.Remove(client);
                var lift = command == "Allow renewal";
                if (seen.Refuses(now) != lift)
                {
                    return false;
                }

                if (lift)
                {
                    answer = RenewalActs.Lift(reading, client, seen.Standing, seen.Lost);
                }
                else
                {
                    var text = (string)act[3];
                    var refusal = text == "forever" ? RenewalRefusal.Forever() : RenewalRefusal.Until(Until(text));
                    string refused;
                    answer = RenewalActs.Refuse(reading, client, refusal, seen.Standing, out refused);
                    Assert.That(answer, Is.Not.Null, where + ": " + refused);
                }
            }

            if (answer == null)
            {
                return false;
            }

            try
            {
                var done = store.Write(new[] { answer });
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
            }
            finally
            {
                answer.Forget();
            }

            return true;
        }

        /// <summary>The moment of the model's `until1` and `until2`: the earlier and the later.</summary>
        private static DateTimeOffset Until(string text)
        {
            return DateTimeOffset.Parse(
                text == "until1" ? "2026-12-14T23:00:00Z" : "2027-03-31T22:00:00Z",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal);
        }

        /// <summary>
        /// A version of the client's answer whose text is out of form, written as somebody holding
        /// the store key writes it, with a summary entry restating it.
        /// </summary>
        private static void OutOfForm(MemoryStore store, string client, string where)
        {
            StoreAct act;
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    var members = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.ObjectMember, RenewalText.ObjectOf(client) },
                        { StoreKinds.After, string.Empty },
                        { StoreKinds.Doc, RenewalText.UntilStart + "2026-12-15" },
                    };
                    act = new StoreAct("a text out of form", key, reading.InForce.Key.Name, new[] { StoreRecord.Compose(key, StoreKinds.Version, members) }, null);
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            try
            {
                var done = store.Write(new[] { act });
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
            }
            finally
            {
                act.Forget();
            }
        }

        /// <summary>
        /// The newest summary entry, or the newest version of a client's answer, deleted for good,
        /// with a deletion record or with none.
        /// </summary>
        private static void DeletedNewest(MemoryStore store, JArray what, bool recorded, Clients clients, string where)
        {
            string hex;
            using (var reading = store.Read())
            {
                if ((string)what[0] == "head")
                {
                    var top = reading.InForce.Heads.Max(head => head.Form.Number);
                    var newest = reading.InForce.Heads.Where(head => head.Form.Number == top).ToList();
                    Assert.That(newest, Has.Count.EqualTo(1), where + ": a single newest summary entry");
                    hex = newest[0].Hex;
                }
                else
                {
                    var client = clients.Admission(((string)what[1]).Substring("renew:".Length)).ClientKey;
                    var versions = StoredRenewals.Of(reading.InForce, client).Standing;
                    Assert.That(versions, Has.Count.EqualTo(1), where + ": a single standing version, which is the newest");
                    hex = versions[0];
                }
            }

            store.Drop(hex);
            if (recorded)
            {
                store.Delete(StoreComposer.BytesOf(hex));
            }
        }

        /// <summary>The answer about each client the model answers for after a step, and the versions of answers present.</summary>
        private static void AssertAnswers(JToken step, MemoryStore store, Dictionary<string, string> keys, List<string> moments, string where)
        {
            using (var reading = store.Read())
            {
                var status = (string)step["answer"]["status"];
                Assert.That(reading.State, Is.EqualTo(status == "ok" ? StoreState.Whole : StoreState.Stopped), where + ": " + reading.Reason);
                foreach (var answered in (JObject)step["answer"]["renewals"])
                {
                    var expected = (JObject)answered.Value;
                    var standing = StoredRenewals.Of(reading.InForce, keys[answered.Key]);
                    var value = standing.Answer == null ? "ask" : standing.Answer.NotBefore.HasValue ? "until:" + Moments.Wire(standing.Answer.NotBefore.Value) : "forever";
                    Assert.That(value, Is.EqualTo((string)expected["value"]), where + ": the answer");
                    Assert.That(standing.Cause.ToString().ToLowerInvariant(), Is.EqualTo((string)expected["cause"]), where + ": the cause");
                    Assert.That(standing.Standing.Count, Is.EqualTo(((JArray)expected["standing"]).Count), where + ": the versions that stand");
                    Assert.That(standing.Lost.Count, Is.EqualTo(((JArray)expected["lost"]).Count), where + ": the versions lost");
                    foreach (var moment in moments)
                    {
                        Assert.That(
                            standing.Refuses(DateTimeOffset.Parse(moment, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)),
                            Is.EqualTo((bool)expected["refuses"][moment]),
                            where + ": refused at " + moment);
                    }
                }

                var present = reading.InForce.Records.Count(record => record.Is(StoreKinds.Version)
                                                                   && record.Form.Member(StoreKinds.ObjectMember).StartsWith("renew:", StringComparison.Ordinal));
                var modelPresent = ((JArray)step["present"]).Count(item => (string)item["kind"] == "version"
                                                                         && ((string)item["object"]).StartsWith("renew:", StringComparison.Ordinal));
                Assert.That(present, Is.EqualTo(modelPresent), where + ": the versions of answers present");
            }
        }

        /// <summary>
        /// The sequences of rules, the writes above among them, and those of the lost versions a
        /// version settles, by name.
        /// </summary>
        private static IEnumerable<TestCaseData> RuleSequences()
        {
            foreach (var name in ((JObject)Vectors()["sequences"]).Properties()
                         .Select(sequence => sequence.Name)
                         .Where(name => name.StartsWith("rules, ", StringComparison.Ordinal)
                                        || name.StartsWith("form, ", StringComparison.Ordinal)
                                        || name.StartsWith("lost, ", StringComparison.Ordinal)))
            {
                yield return new TestCaseData(name).SetArgDisplayNames(name.Substring(0, name.IndexOf(':')));
            }
        }

        /// <summary>
        /// A sequence of the lost versions a version settles with names that do under this store's
        /// key what the model's searched names do under its key, `STORE.md` §609: two names whose
        /// tags meet for the two whose tags meet there, and a name whose tag is 0 for the name
        /// whose tag is 0, each meeting no other name of the sequence. Every text of the sequence
        /// holding a searched name holds the name put for it.
        /// </summary>
        private static JObject Renamed(JObject sequence, MemoryStore store)
        {
            var searched = ((JArray)sequence["profiles"]).Select(profile => (string)profile).Where(Searched).ToList();
            var others = Others(sequence);
            var model = StoreComposer.MeasuredKey("K");
            Dictionary<string, string> names;
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    var taken = new HashSet<int>(others.Select(other => StoreComposer.ProfileTag(key, other)));
                    Assert.That(taken, Does.Not.Contain(0), "no other name of the sequence has a tag of 0 under the store's key");
                    if (searched.Count == 2)
                    {
                        Assert.That(StoreComposer.ProfileTag(model, searched[0]), Is.EqualTo(StoreComposer.ProfileTag(model, searched[1])), "the model's names meet under its key");
                        var meeting = Meeting(key, taken);
                        var ordered = new[] { meeting.First, meeting.Second }.OrderBy(name => name, StringComparer.Ordinal).ToList();
                        var modelOrdered = searched.OrderBy(name => name, StringComparer.Ordinal).ToList();
                        names = new Dictionary<string, string>(StringComparer.Ordinal) { { modelOrdered[0], ordered[0] }, { modelOrdered[1], ordered[1] } };
                    }
                    else
                    {
                        Assert.That(StoreComposer.ProfileTag(model, searched.Single()), Is.EqualTo(0), "the model's name has a tag of 0 under its key");
                        names = new Dictionary<string, string>(StringComparer.Ordinal) { { searched[0], Zero(key) } };
                    }
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            var text = sequence.ToString(Newtonsoft.Json.Formatting.None);
            foreach (var pair in names)
            {
                text = text.Replace(pair.Key, pair.Value);
            }

            using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(text)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            {
                return JObject.Load(reader);
            }
        }

        /// <summary>
        /// A store with its authority under whose key the names a sequence takes as they stand
        /// have tags as the model's key gave them: none of 0, and no two alike. A store's key is
        /// random and a tag is two bytes of a keyed hash, so under some keys such a name has a
        /// tag of 0 or the tag of another name of the sequence, which the model's sequence did
        /// not play; a store with such a key is made again.
        /// </summary>
        private static MemoryStore StoreFor(JObject sequence)
        {
            while (true)
            {
                var store = new MemoryStore();
                StoreSeeds.Authority(store);
                using (var reading = store.Read())
                {
                    var key = (byte[])reading.InForce.Key.Bytes.Clone();
                    try
                    {
                        var tags = Others(sequence).Distinct(StringComparer.Ordinal).Select(other => StoreComposer.ProfileTag(key, other)).ToList();
                        if (!tags.Contains(0) && tags.Distinct().Count() == tags.Count)
                        {
                            return store;
                        }
                    }
                    finally
                    {
                        Array.Clear(key, 0, key.Length);
                    }
                }
            }
        }

        /// <summary>
        /// The names of a sequence that the model did not search for by their tags: its profiles
        /// that are no searched name, and each name a step adds.
        /// </summary>
        private static List<string> Others(JObject sequence)
        {
            return ((JArray)sequence["profiles"]).Select(profile => (string)profile).Where(profile => !Searched(profile))
                .Concat(((JArray)sequence["steps"]).Where(step => (string)step["command"] == "Add name").Select(step => (string)step["act"][2]))
                .ToList();
        }

        /// <summary>Whether a name is a name the model searched for by its tag: `q` and a counter.</summary>
        private static bool Searched(string name)
        {
            return name.Length > 1 && name[0] == 'q' && name.Skip(1).All(c => c >= '0' && c <= '9');
        }

        /// <summary>
        /// Two names whose tags meet under this key, their tag none of these and not 0, found by a
        /// search. Each begins with `q-`, so it stands where the model's name stands among the
        /// sequence's other names in a text that orders names, as a switch's does.
        /// </summary>
        private static (string First, string Second) Meeting(byte[] key, ISet<int> taken)
        {
            var seen = new Dictionary<int, string>();
            for (var at = 0; ; at++)
            {
                var name = "q-" + at.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var tag = StoreComposer.ProfileTag(key, name);
                string met;
                if (tag == 0 || taken.Contains(tag))
                {
                    continue;
                }

                if (seen.TryGetValue(tag, out met))
                {
                    return (met, name);
                }

                seen[tag] = name;
            }
        }

        /// <summary>A name whose tag is 0 under this key, found by a search, beginning with `q-` as <see cref="Meeting"/>'s do.</summary>
        private static string Zero(byte[] key)
        {
            for (var at = 0; ; at++)
            {
                var name = "q-" + at.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (StoreComposer.ProfileTag(key, name) == 0)
                {
                    return name;
                }
            }
        }

        /// <summary>
        /// A command of the sequences of the lost versions a version settles that is no press on a
        /// node's dialog, played in this copy: a name added, a version lost, an entry of the store
        /// deleted by hand, and a version of a rule's object with no slash, which the plugin never
        /// writes. False for every other command.
        /// </summary>
        private static bool PlayedApart(MemoryStore store, JObject sequence, JToken step, Dictionary<string, string> mapped, string where)
        {
            var act = (JArray)step["act"];
            switch ((string)step["command"])
            {
                case "Add name":
                    StoreSeeds.Name(store, (string)act[2]);
                    return true;
                case "Lose version":
                    Lose(store, sequence, step, (string)act[2], mapped, where);
                    return true;
                case "Delete by hand":
                    DeletedByHand(store, step, (JArray)act[2], (int)act[3] == 1, mapped, where);
                    return true;
                case "Set object with no slash":
                    SetWithNoSlash(store, (string)sequence["nodes"][(string)act[2]], (string)act[3], where);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A deletion record of a version of this object of the model's, `rule:`, a node's label,
        /// `/` and a name, or `sw:` and a node's label, whose content no file holds: at the place of
        /// the object under the store's key, the model's identifier taken for it.
        /// </summary>
        private static void Lose(MemoryStore store, JObject sequence, JToken step, string modelObject, Dictionary<string, string> mapped, string where)
        {
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    string subject;
                    var number = 0;
                    if (modelObject.StartsWith("sw:", StringComparison.Ordinal))
                    {
                        subject = StoreComposer.SwitchObject((string)sequence["nodes"][modelObject.Substring("sw:".Length)]);
                    }
                    else
                    {
                        var slash = modelObject.IndexOf('/');
                        subject = "rule:" + (string)sequence["nodes"][modelObject.Substring("rule:".Length, slash - "rule:".Length)];
                        number = StoreComposer.ProfileTag(key, modelObject.Substring(slash + 1));
                    }

                    var identifier = StoreComposer.IdentifierOf(key, "version", subject, number, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
                    store.Delete(identifier);
                    var added = ((JArray)step["deletions"]).Select(Id).Where(id => !Previous(step).Deletions.Contains(id)).ToList();
                    Assert.That(added, Has.Count.EqualTo(1), where + ": the model's deletion record of the version lost");
                    mapped[added[0]] = KeePassGate.Hex.Of(identifier);
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }
        }

        /// <summary>
        /// An entry of the store deleted for good, with a deletion record or with none: the newest
        /// summary entry, or the product's version the model's deleted version is taken for.
        /// </summary>
        private static void DeletedByHand(MemoryStore store, JToken step, JArray what, bool recorded, Dictionary<string, string> mapped, string where)
        {
            string hex;
            if ((string)what[0] == "head")
            {
                using (var reading = store.Read())
                {
                    var top = reading.InForce.Heads.Max(head => head.Form.Number);
                    var newest = reading.InForce.Heads.Where(head => head.Form.Number == top).ToList();
                    Assert.That(newest, Has.Count.EqualTo(1), where + ": a single newest summary entry");
                    hex = newest[0].Hex;
                }
            }
            else
            {
                var deleted = ((JArray)step["deleted"]).Where(item => (string)item["kind"] == "version").Select(Id).ToList();
                Assert.That(deleted, Has.Count.EqualTo(1), where + ": the model deleted a single version");
                Assert.That(mapped.ContainsKey(deleted[0]), Is.True, where + ": a version this test has seen");
                hex = mapped[deleted[0]];
            }

            store.Drop(hex);
            if (recorded)
            {
                store.Delete(StoreComposer.BytesOf(hex));
            }
        }

        /// <summary>
        /// A version of the object `rule:` and the node with no slash holding these letters, which
        /// is no node's rule and lies at the place of the rows of a name whose tag is 0, written as
        /// an act: naming in `after` the versions of that object that stand and what they name
        /// there, and in `lost` its lost versions and what those name there, as `STORE.md` §617
        /// has every version name.
        /// </summary>
        private static void SetWithNoSlash(MemoryStore store, string node, string letters, string where)
        {
            var objectText = "rule:" + node;
            StoreAct act;
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    var standing = reading.InForce.VersionsOf(objectText);
                    var after = standing.SelectMany(version => new[] { version.Id }.Concat(Split(version.After)))
                        .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
                    var lost = reading.InForce.LostOf(objectText).Concat(standing.SelectMany(version => Split(LostOf(version.Members))))
                        .Distinct(StringComparer.Ordinal).Except(after, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
                    var members = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.ObjectMember, objectText },
                        { StoreKinds.After, string.Join(" ", after) },
                        { StoreKinds.Doc, KeePassGate.Acl.RuleText.Row(ModelLetters(letters)) },
                    };
                    if (lost.Count > 0)
                    {
                        members["lost"] = string.Join(" ", lost);
                    }

                    act = new StoreAct("a version of a rule's object with no slash", key, reading.InForce.Key.Name, new[] { StoreRecord.Compose(key, StoreKinds.Version, members) }, null);
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            try
            {
                var done = store.Write(new[] { act });
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
            }
            finally
            {
                act.Forget();
            }
        }

        /// <summary>The text of a version's `lost`, empty where its members hold none.</summary>
        private static string LostOf(IReadOnlyDictionary<string, string> members)
        {
            string lost;
            return members != null && members.TryGetValue("lost", out lost) ? lost : string.Empty;
        }

        /// <summary>
        /// The rows of the model's start, put into the store with a first summary entry restating
        /// them in place of the first summary entry Create wrote, as entries and not by a write;
        /// answers the product's identifier for each of the model's versions.
        /// </summary>
        private static Dictionary<string, string> StartedRules(MemoryStore store, JObject sequence)
        {
            var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    var head = reading.InForce.Heads.Single();
                    var items = head.Form.Items
                        .Select(item => StoreComposer.Item(item.Members.Select(member => (member.Key, member.Value)).Concat(new[] { ("kind", item.Kind) }).ToArray()))
                        .ToList();
                    foreach (var version in ModelVersions((JObject)sequence["start"]["file"]))
                    {
                        var composed = StoreComposer.RuleVersion(key, version.Value.Object, version.Value.Doc);
                        store.Put(composed);
                        items.Add(StoreComposer.RuleVersionItem(composed, version.Value.Object, version.Value.Doc));
                        mapped[version.Key] = composed.Hex;
                    }

                    store.Drop(head.Hex);
                    store.Put(StoreComposer.Record(key, "head", "store", StoreComposer.Content(("after", string.Empty), ("height", "1")), 1, items));
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            using (var reading = store.Read())
            {
                Assert.That(reading.State, Is.EqualTo(StoreState.Whole), "the start: " + reading.Reason);
            }

            return mapped;
        }

        /// <summary>A command of a sequence of rules, played as an OK on the node's dialog in this copy.</summary>
        private static void PlayedRules(IGateStore store, JObject sequence, JToken step, JObject gives, string where)
        {
            var command = (string)step["command"];
            var act = (JArray)step["act"];
            var node = (string)sequence["nodes"][(string)act[2]];
            var profiles = ((JArray)sequence["profiles"]).Select(profile => (string)profile).ToList();

            StoreAct written;
            IReadOnlyList<KeePassGate.Acl.RuleLeft> left;
            using (var reading = store.Read())
            {
                // The model composes every act at its write: the tab's reading is the write's own,
                // so what arrives now is what the press showed arriving, and the versions below the
                // press kept are every version the reading holds there.
                var rules = KeePassGate.Acl.StoredRules.Of(reading);
                var shown = rules.At(node);
                var tree = RuledTree((JObject)sequence["tree"]);
                var below = rules.Below(node, tree);
                var arriving = Arriving(sequence, gives, node);
                KeePassGate.Acl.RuleChanges changes;
                switch (command)
                {
                    case "Set row":
                        changes = Pressed(Change(KeePassGate.Acl.RowChangeKind.Set, (string)act[3], ModelLetters((string)act[4]), shown, below));
                        break;
                    case "Set rows":
                        var spec = (string)act[3] == "none" ? string.Empty : (string)act[3];
                        var rows = spec.Contains("=")
                            ? spec.Split(',').Select(pair => (pair.Split('=')[0], pair.Split('=')[1]))
                            : profiles.Select(profile => (profile, spec));
                        changes = Pressed(rows.Select(row => Change(KeePassGate.Acl.RowChangeKind.Set, row.Item1, ModelLetters(row.Item2), shown, below)).ToArray());
                        break;
                    case "Remove row":
                        changes = Pressed(Change(KeePassGate.Acl.RowChangeKind.TakeOff, (string)act[3], Letters(arriving, (string)act[3]), shown, below));
                        break;
                    case "Settle limit":
                        changes = Pressed(Change(KeePassGate.Acl.RowChangeKind.Settle, (string)act[3], Letters(arriving, (string)act[3]), shown, below));
                        break;
                    case "Set switch":
                        var refuses = (string)act[3] == "deny";
                        changes = new KeePassGate.Acl.RuleChanges(null, new KeePassGate.Acl.SwitchChange(
                            refuses, shown, refuses ? null : arriving.Where(pair => pair.Value.Count > 0), below));
                        break;
                    case "Remove version":
                        changes = Pressed(Removed(sequence, step, shown, (string)act[3], (int)act[4], below));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(step), command, "a command this test does not play");
                }

                // The node's parent with the rules of the same reading.
                var parent = (string)sequence["tree"]["groups"][node];
                var above = parent == null ? null : Tree((JObject)sequence["tree"], rules)[parent];
                written = KeePassGate.Acl.RuleActs.Of(reading, node, _ => above, _ => tree, changes, out left);
            }

            Assert.That(left, Is.Empty, where + ": every version fits");
            if (!(bool)step["played"])
            {
                Assert.That(written, Is.Null, where + ": the model wrote nothing");
                return;
            }

            Assert.That(written, Is.Not.Null, where + ": the model wrote");
            var waits = false;
            try
            {
                var done = store.Write(new[] { written });
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
                waits = done.Waits;
            }
            finally
            {
                // An act held for a dialog's OK is the buffer's to let go.
                if (!waits)
                {
                    written.Forget();
                }
            }
        }

        /// <summary>
        /// The X on a version of a bound group: the version the model removed, taken by its place
        /// among the versions the model's copy held standing before the step, with its text.
        /// </summary>
        private static KeePassGate.Acl.RowChange Removed(
            JObject sequence, JToken step, KeePassGate.Acl.NodeRule shown, string profile, int index, IEnumerable<string> below)
        {
            var node = (string)sequence["nodes"][(string)step["act"][2]];
            var objectText = StoreComposer.RowObject(node, profile);
            var previous = PreviousAnswer(step);
            var text = (string)previous["rules"]["standing"][objectText][index];
            var version = shown.Row(profile).Versions.Single(held => Text(held) == text);
            return new KeePassGate.Acl.RowChange(KeePassGate.Acl.RowChangeKind.RemoveVersions, profile, null, new[] { version.Id }, shown, below);
        }

        /// <summary>What the model's copy answered before this step: its last step's answer, or the start's.</summary>
        private static JToken PreviousAnswer(JToken step)
        {
            var steps = (JArray)step.Parent;
            var copy = (int)step["copy"];
            var last = steps.Take(steps.IndexOf(step)).LastOrDefault(earlier => (int)earlier["copy"] == copy);
            return last == null ? ((JObject)steps.Parent.Parent)["start"]["answer"] : last["answer"];
        }

        private static string Text(KeePassGate.Acl.RowVersion version)
        {
            switch (version.Kind)
            {
                case KeePassGate.Acl.RowVersionKind.Grants:
                    return KeePassGate.Acl.RuleText.Row(version.Letters);
                case KeePassGate.Acl.RowVersionKind.TakenOff:
                    return KeePassGate.Acl.RuleText.TakenOff(version.Letters);
                default:
                    return KeePassGate.Acl.RuleText.TakenOffRefusing;
            }
        }

        private static KeePassGate.Acl.RuleChanges Pressed(params KeePassGate.Acl.RowChange[] rows)
        {
            return new KeePassGate.Acl.RuleChanges(rows, null);
        }

        private static KeePassGate.Acl.RowChange Change(
            KeePassGate.Acl.RowChangeKind kind, string profile, KeePassGate.Acl.AclGrant letters, KeePassGate.Acl.NodeRule shown, IEnumerable<string> below)
        {
            return new KeePassGate.Acl.RowChange(kind, profile, letters, null, shown, below);
        }

        /// <summary>The tree of a sequence, every group under the node above it, as the write above is carried down it.</summary>
        private static TestRuledTree RuledTree(JObject tree)
        {
            var root = new (string Node, string Above)[] { ((string)tree["root"], null) };
            return TestRuledTree.Of(root
                .Concat(((JObject)tree["groups"]).Properties().Select(property => (property.Name, (string)property.Value)))
                .ToArray());
        }

        /// <summary>
        /// What arrives at the node for each profile in the model's copy before the step: what the
        /// node's parent gives, and nothing above the root.
        /// </summary>
        private static Dictionary<string, KeePassGate.Acl.AclGrant> Arriving(JObject sequence, JObject gives, string node)
        {
            var parent = (string)sequence["tree"]["groups"][node];
            var arriving = new Dictionary<string, KeePassGate.Acl.AclGrant>(StringComparer.Ordinal);
            if (parent != null)
            {
                foreach (var pair in (JObject)gives[parent])
                {
                    arriving[pair.Key] = Rules.Letters((string)pair.Value);
                }
            }

            return arriving;
        }

        private static KeePassGate.Acl.AclGrant Letters(IReadOnlyDictionary<string, KeePassGate.Acl.AclGrant> arriving, string profile)
        {
            KeePassGate.Acl.AclGrant letters;
            return arriving.TryGetValue(profile, out letters) ? letters : KeePassGate.Acl.AclGrant.Nothing;
        }

        /// <summary>A mask as the model writes it, in lower case with `w` for update, as letters.</summary>
        private static KeePassGate.Acl.AclGrant ModelLetters(string mask)
        {
            return KeePassGate.Acl.AclGrant.Of(mask
                .Select(letter => letter == 'w' ? 'U' : char.ToUpperInvariant(letter))
                .Select(letter => KeePassGate.Acl.AclVerbs.Grantable[KeePassGate.Acl.VerbMask.Letters.IndexOf(letter)])
                .Distinct());
        }

        /// <summary>
        /// The records of kind `version` and the summary entries of a store, by identifier, each as
        /// the model's file is compared: a version by its object, a summary entry by its height.
        /// </summary>
        private static (Dictionary<string, string> Present, HashSet<string> Deletions) Labels(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                var present = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var record in reading.InForce.Records)
                {
                    if (record.Is(StoreKinds.Version))
                    {
                        present[record.Hex] = "version " + record.Form.Member(StoreKinds.ObjectMember);
                    }
                    else if (record.Is(StoreKinds.Head))
                    {
                        present[record.Hex] = "head " + record.Form.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                return (present, new HashSet<string>(reading.Deletions.Select(KeePassGate.Hex.Of), StringComparer.Ordinal));
            }
        }

        /// <summary>A record the model's file lists, as <see cref="Labels"/> labels it.</summary>
        private static string ModelLabel(JToken item)
        {
            var kind = (string)item["kind"];
            return kind == "version" ? "version " + (string)item["object"]
                : kind == "head" ? "head " + ((int)item["number"]).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }

        /// <summary>
        /// A step of a sequence of rules held to the model's file and answer after it: the versions
        /// and summary entries present, those a write deleted and told of, each summary entry's
        /// height, names and items, the texts that stand, and what every node gives every profile.
        /// </summary>
        private static void AssertRules(
            JToken step, MemoryStore store, (Dictionary<string, string> Present, HashSet<string> Deletions) before, bool write, string where)
        {
            var after = Labels(store);
            Assert.That(
                after.Present.Values.OrderBy(label => label, StringComparer.Ordinal),
                Is.EqualTo(((JArray)step["present"]).Select(ModelLabel).Where(label => label != null).OrderBy(label => label, StringComparer.Ordinal)),
                where + ": the versions and summary entries present");

            if (write)
            {
                var previous = Previous(step);
                var added = after.Deletions.Where(hex => !before.Deletions.Contains(hex)).ToList();
                var modelAdded = ((JArray)step["deletions"]).Where(item => !previous.Deletions.Contains((string)item["id"])).ToList();

                // What left the file, in each: the model lists as deleted what the deletion records
                // the step added name, which holds a version lost that no file held and leaves out
                // an entry deleted with no deletion record.
                var now = new HashSet<string>(((JArray)step["present"]).Select(Id), StringComparer.Ordinal);
                Assert.That(
                    before.Present.Where(pair => !after.Present.ContainsKey(pair.Key)).Select(pair => pair.Value).OrderBy(label => label, StringComparer.Ordinal),
                    Is.EqualTo(PreviousPresent(step).Where(item => !now.Contains(Id(item)))
                        .Select(ModelLabel).Where(label => label != null).OrderBy(label => label, StringComparer.Ordinal)),
                    where + ": the records deleted");
                Assert.That(
                    added.Where(before.Present.ContainsKey).Select(hex => before.Present[hex]).OrderBy(label => label, StringComparer.Ordinal),
                    Is.EqualTo(modelAdded.Where(item => previous.Present.Contains((string)item["id"]))
                        .Select(ModelLabel).Where(label => label != null).OrderBy(label => label, StringComparer.Ordinal)),
                    where + ": the deletion records added for records the file held");
                Assert.That(
                    added.Count(hex => !before.Present.ContainsKey(hex)),
                    Is.EqualTo(modelAdded.Count(item => !previous.Present.Contains((string)item["id"]))),
                    where + ": the deletion records added for records the file did not hold");
            }

            using (var reading = store.Read())
            {
                // Whole but where the model's copy stopped, as a summary entry deleted with no
                // deletion record stops it; the rules are read from what is in force either way.
                var status = (string)step["answer"]["status"];
                Assert.That(reading.State, Is.EqualTo(status == "incomplete" ? StoreState.Stopped : StoreState.Whole), where + ": " + reading.Reason);

                var heads = reading.InForce.Heads
                    .Select(head => (Height: head.Form.Number, After: Count(head.Form.Member("after")), Items: head.Form.Items.Count(item => item.Kind != "format")))
                    .OrderBy(head => head.Height).ThenBy(head => head.After).ThenBy(head => head.Items).ToList();
                var modelHeads = ((JArray)step["heads"])
                    .Select(head => (Height: (int)head["height"], After: (int)head["after"], Items: ((JArray)head["items"]).Count))
                    .OrderBy(head => head.Height).ThenBy(head => head.After).ThenBy(head => head.Items).ToList();
                Assert.That(heads, Is.EqualTo(modelHeads), where + ": each summary entry's height, names and items");

                var rules = (JObject)step["answer"]["rules"];
                var standing = reading.InForce.Objects
                    .Where(objectText => reading.InForce.VersionsOf(objectText).Count > 0)
                    .ToDictionary(
                        objectText => objectText,
                        objectText => string.Join(" | ", reading.InForce.VersionsOf(objectText).Select(version => version.Members[StoreKinds.Doc]).OrderBy(doc => doc, StringComparer.Ordinal)));
                var modelStanding = ((JObject)rules["standing"]).Properties().ToDictionary(
                    property => property.Name,
                    property => string.Join(" | ", ((JArray)property.Value).Select(doc => (string)doc).OrderBy(doc => doc, StringComparer.Ordinal)));
                Assert.That(standing, Is.EquivalentTo(modelStanding), where + ": the texts that stand");
                Assert.That(
                    reading.InForce.Objects.Where(objectText => reading.InForce.LostOf(objectText).Count > 0),
                    Is.EquivalentTo(((JArray)rules["lost"]).Select(lost => (string)lost)),
                    where + ": what is lost");

                var nodes = Tree((JObject)((JObject)step.Parent.Parent.Parent)["tree"], KeePassGate.Acl.StoredRules.Of(reading));
                foreach (var node in (JObject)rules["gives"])
                {
                    foreach (var profile in (JObject)node.Value)
                    {
                        Assert.That(
                            LettersOf(KeePassGate.Acl.AclResolver.Arrives(nodes[node.Key], profile.Key)),
                            Is.EqualTo((string)profile.Value),
                            where + ": what " + node.Key.Substring(0, 2) + " gives " + profile.Key);
                    }
                }
            }
        }

        /// <summary>
        /// What each version a step brought names, the model's identifiers taken for the product's:
        /// each new version of the model's file is the product's new version of the same object
        /// and text, and names the versions the model's names.
        /// </summary>
        private static void AssertNamed(
            JToken step, MemoryStore store, Dictionary<string, string> mapped, HashSet<string> seenModel, HashSet<string> seenProduct, string where)
        {
            var model = ModelVersions((JObject)step["file"]);
            Dictionary<string, (string Object, string Doc, string After, string Lost)> product;
            using (var reading = store.Read())
            {
                product = reading.InForce.Versions.Values.ToDictionary(
                    version => version.Id,
                    version => (version.Object, version.Members[StoreKinds.Doc], version.After ?? string.Empty, LostOf(version.Members)),
                    StringComparer.Ordinal);
            }

            var newProduct = product.Where(pair => !seenProduct.Contains(pair.Key)).ToList();
            foreach (var version in model.Where(pair => !seenModel.Contains(pair.Key)))
            {
                var same = newProduct.Where(pair => pair.Value.Object == version.Value.Object && pair.Value.Doc == version.Value.Doc).ToList();
                Assert.That(same, Has.Count.EqualTo(1), where + ": a version of " + version.Value.Object + " holding " + version.Value.Doc);
                mapped[version.Key] = same[0].Key;

                var names = Split(version.Value.After).Select(id =>
                {
                    Assert.That(mapped.ContainsKey(id), Is.True, where + ": the model names a version this test has seen");
                    return mapped[id];
                });
                Assert.That(Split(same[0].Value.After), Is.EqualTo(names.OrderBy(id => id, StringComparer.Ordinal)), where + ": what " + version.Value.Doc + " names");

                var settled = Split(version.Value.Lost).Select(id =>
                {
                    Assert.That(mapped.ContainsKey(id), Is.True, where + ": the model settles a version this test has seen");
                    return mapped[id];
                });
                Assert.That(Split(same[0].Value.Lost), Is.EqualTo(settled.OrderBy(id => id, StringComparer.Ordinal)), where + ": what " + version.Value.Doc + " settles");
            }

            seenModel.UnionWith(model.Keys);
            seenProduct.UnionWith(product.Keys);
        }

        /// <summary>Every version a file of the model holds, by record or by an item of a summary entry, by identifier.</summary>
        private static Dictionary<string, (string Object, string Doc, string After, string Lost)> ModelVersions(JObject file)
        {
            var versions = new Dictionary<string, (string Object, string Doc, string After, string Lost)>(StringComparer.Ordinal);
            foreach (var entry in (JObject)file["entries"])
            {
                var strings = (JObject)entry.Value["strings"];
                if ((string)strings["KPG.Kind"] == "version")
                {
                    var data = JObject.Parse((string)strings["KPG.Data"]);
                    versions[entry.Key] = ((string)data["object"], (string)data["doc"], (string)data["after"], (string)data["lost"]);
                }

                foreach (var members in strings.Properties()
                             .Where(property => property.Name.StartsWith("KPG.S.", StringComparison.Ordinal))
                             .Select(property => JObject.Parse((string)property.Value))
                             .Where(item => (string)item["kind"] == "version"))
                {
                    versions[(string)members["id"]] = ((string)members["object"], (string)members["doc"], (string)members["after"], (string)members["lost"]);
                }
            }

            return versions;
        }

        /// <summary>The nodes of a sequence's tree, each holding the rules of a reading.</summary>
        private static Dictionary<string, TestNode> Tree(JObject tree, KeePassGate.Acl.StoredRules rules)
        {
            var root = (string)tree["root"];
            var nodes = new Dictionary<string, TestNode>(StringComparer.Ordinal) { { root, TestNode.Root("root").Holding(rules.At(root)) } };
            var groups = ((JObject)tree["groups"]).Properties().ToDictionary(property => property.Name, property => (string)property.Value, StringComparer.Ordinal);
            while (nodes.Count < groups.Count + 1)
            {
                var placed = groups.Where(pair => !nodes.ContainsKey(pair.Key) && nodes.ContainsKey(pair.Value)).ToList();
                Assert.That(placed, Is.Not.Empty, "every group of the tree sits under the root");
                foreach (var group in placed)
                {
                    nodes[group.Key] = nodes[group.Value].Child(group.Key.Substring(0, 2)).Holding(rules.At(group.Key));
                }
            }

            return nodes;
        }

        /// <summary>The letters of a grant in the order of `CRUSDLAP`.</summary>
        private static string LettersOf(KeePassGate.Acl.AclGrant grant)
        {
            return string.Concat(Enumerable.Range(0, KeePassGate.Acl.VerbMask.Width)
                .Where(at => grant.Holds(KeePassGate.Acl.AclVerbs.Grantable[at]))
                .Select(at => KeePassGate.Acl.VerbMask.Letters[at]));
        }

        private static IEnumerable<string> Split(string after)
        {
            return string.IsNullOrEmpty(after) ? Enumerable.Empty<string>() : after.Split(' ');
        }

        /// <summary>A step of the sequence of names, played by the product in this copy.</summary>
        private static AuthorityChange PlayedNamed(MemoryStore copy, MemoryStore other, JToken step)
        {
            var act = (JArray)step["act"];
            switch ((string)step["command"])
            {
                case "Add":
                    StoreSeeds.Name(copy, (string)act[2]);
                    break;
                case "Remove":
                    StoreSeeds.Unname(copy, (string)act[2]);
                    break;
                case "Synchronize":
                    copy.Synchronize(other);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step), (string)step["command"], "a command this test does not play");
            }

            return AuthorityChange.Done();
        }

        /// <summary>
        /// The name the model's start holds, put into the store as an entry and not by a write,
        /// as <see cref="Started"/> puts the admission there.
        /// </summary>
        private static void StartedNamed(MemoryStore store, string name)
        {
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    store.Put(StoreComposer.Record(key, "profile", "profile:" + name, StoreComposer.Content(("after", string.Empty), ("name", name))));
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }
        }

        private static IReadOnlyList<string> NamesOf(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return KeePassGate.Acl.ProfileNames.Of(reading).Names;
            }
        }

        /// <summary>A command of a sequence, played by the product.</summary>
        private static AuthorityChange Played(MemoryStore store, JToken step, Clients clients)
        {
            var command = (string)step["command"];
            var act = (JArray)step["act"];
            switch (command)
            {
                case "Recreate":
                    return Service(store).Recreate();
                case "Delete":
                    return Service(store).Delete();
                case "Create":
                    return Service(store).Create();
                case "Pair":
                    StoreSeeds.Admit(store, clients.Paired(StoreSeeds.Served(store)));
                    return AuthorityChange.Done();
                case "Renew":
                    StoreSeeds.Admit(store, clients.Renewed((string)act[2], StoreSeeds.Served(store)));
                    return AuthorityChange.Done();
                case "Revoke":
                    StoreSeeds.Revoke(store, clients.Admission((string)act[2]).ClientKey);
                    return AuthorityChange.Done();
                default:
                    throw new ArgumentOutOfRangeException(nameof(step), command, "a command this test does not play");
            }
        }

        /// <summary>
        /// The admission the model's start holds beside its first summary entry, `c0`, put into the
        /// store as an entry and not by a write, since the start's summary entry is its first.
        /// </summary>
        private static void Started(MemoryStore store, Clients clients)
        {
            using (var reading = store.Read())
            {
                var admission = clients.Started(StoreAuthorities.Served(reading));
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    store.Put(StoreComposer.Record(
                        key,
                        "client",
                        "client:" + admission.ClientKey,
                        StoreComposer.Content(admission.Members().Select(member => (member.Key, member.Value)).ToArray())));
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }
        }

        private static void AssertShape(JToken step, MemoryStore store, Func<AuthorityChange> act)
        {
            AssertShape(step, store, act, Compared, false);
        }

        /// <summary>A step played, held to the model's file after it by shape.</summary>
        /// <param name="merged">
        /// Whether the step is a merge rather than a write, whose deletions are held by what is
        /// present after it and not record by record: the model derives a summary entry's
        /// identifier from its height, so a summary entry each copy wrote at the same height is a
        /// single entry to the model and two to the product, and a merge deletes them differently
        /// while leaving the same records present.
        /// </param>
        private static void AssertShape(JToken step, MemoryStore store, Func<AuthorityChange> act, string[] compared, bool merged)
        {
            var command = (string)step["command"];
            var before = store.Read();
            var kinds = Kinds(before);
            var heights = before.InForce.Heads.ToDictionary(head => head.Hex, head => head.Form.Number, StringComparer.Ordinal);
            var deletedBefore = new HashSet<string>(before.Deletions.Select(KeePassGate.Hex.Of), StringComparer.Ordinal);

            var done = act();
            Assert.That(done.Succeeded, Is.True, command + ": " + done.Message);

            var reading = store.Read();
            var after = Kinds(reading);
            if (!merged)
            {
                AssertDeleted(step, compared, kinds, heights, deletedBefore, reading, after);
            }

            AssertPresent(step, compared, reading, after);
        }

        /// <summary>What a write deleted and the deletion records it added, held to the model's.</summary>
        private static void AssertDeleted(
            JToken step,
            string[] compared,
            Dictionary<string, string> kinds,
            Dictionary<string, int> heights,
            HashSet<string> deletedBefore,
            StoreReading reading,
            Dictionary<string, string> after)
        {
            var command = (string)step["command"];
            var deleted = kinds.Where(pair => !after.ContainsKey(pair.Key)).Select(pair => pair.Value)
                .Where(compared.Contains).OrderBy(kind => kind, StringComparer.Ordinal).ToList();
            var modelDeleted = ((JArray)step["deleted"]).Select(item => (string)item["kind"])
                .Where(compared.Contains).OrderBy(kind => kind, StringComparer.Ordinal).ToList();
            Assert.That(deleted, Is.EqualTo(modelDeleted), command + ": the records deleted");

            // The deletion records the write added: for a record the file held, by its kind and
            // the number it carries, a summary entry's height; for a record it did not hold, by
            // their count.
            var added = reading.Deletions.Select(KeePassGate.Hex.Of).Where(hex => !deletedBefore.Contains(hex)).ToList();
            var held = added.Where(kinds.ContainsKey)
                .Select(hex => (Kind: kinds[hex], Number: HeightOf(heights, hex)))
                .Where(item => compared.Contains(item.Kind))
                .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Number).ToList();
            var previous = Previous(step);
            var modelAdded = ((JArray)step["deletions"]).Where(item => !previous.Deletions.Contains((string)item["id"])).ToList();
            var modelHeld = modelAdded.Where(item => previous.Present.Contains((string)item["id"]))
                .Select(item => (Kind: (string)item["kind"], Number: (int)item["number"]))
                .Where(item => compared.Contains(item.Kind))
                .OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Number).ToList();
            Assert.That(held, Is.EqualTo(modelHeld), command + ": the deletion records added for records the file held");
            Assert.That(
                added.Count(hex => !kinds.ContainsKey(hex)),
                Is.EqualTo(modelAdded.Count(item => !previous.Present.Contains((string)item["id"]) && compared.Contains((string)item["kind"]))),
                command + ": the deletion records added for records the file did not hold");
        }

        /// <summary>
        /// What is present after a step, the summary entries, the store key and the answer, held
        /// to the model's.
        /// </summary>
        private static void AssertPresent(JToken step, string[] compared, StoreReading reading, Dictionary<string, string> after)
        {
            var command = (string)step["command"];
            var present = after.Values.Where(compared.Contains).GroupBy(kind => kind).ToDictionary(group => group.Key, group => group.Count());
            var modelPresent = ((JArray)step["present"]).Select(item => (string)item["kind"]).Where(compared.Contains)
                .GroupBy(kind => kind).ToDictionary(group => group.Key, group => group.Count());
            Assert.That(present, Is.EquivalentTo(modelPresent), command + ": the records present");

            var heads = reading.InForce.Heads
                .Select(head => (Height: head.Form.Number, After: Count(head.Form.Member("after")), Items: head.Form.Items.Count(item => item.Kind != "format")))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ThenBy(head => head.Items).ToList();
            var modelHeads = ((JArray)step["heads"])
                .Select(head => (Height: (int)head["height"], After: (int)head["after"], Items: ((JArray)head["items"]).Count(item => (string)item["kind"] != "version")))
                .OrderBy(head => head.Height).ThenBy(head => head.After).ThenBy(head => head.Items).ToList();
            Assert.That(heads, Is.EqualTo(modelHeads), command + ": each summary entry's height, names and items");

            Assert.That(((JArray)step["keys"]).Count, Is.EqualTo(1), command + ": the model's file holds a single store key");
            Assert.That(reading.State, Is.EqualTo(StoreState.Whole), command + ": " + reading.Reason);

            var answer = step["answer"];
            Assert.That((string)answer["status"], Is.EqualTo("ok"), command);
            Assert.That(
                StoreAuthorities.Served(reading) != null,
                Is.EqualTo(answer["served"].Type != JTokenType.Null),
                command + ": whether an authority is served");
            Assert.That(reading.Answering.Count, Is.EqualTo(((JArray)answer["auths"]).Count), command + ": the authorities standing");
            foreach (var kind in new[] { "retired", "revoked" })
            {
                var refusals = ((JArray)answer["refusals"]).Count(refusal => (string)refusal[0] == kind);
                Assert.That(reading.InForce.Taken(kind).Count, Is.EqualTo(refusals), command + ": what is " + kind);
            }
        }

        /// <summary>The height of a summary entry, or 0 for a record of another kind, as the model numbers it.</summary>
        private static int HeightOf(IDictionary<string, int> heights, string hex)
        {
            int height;
            return heights.TryGetValue(hex, out height) ? height : 0;
        }

        /// <summary>
        /// The identifiers the model's file held and its deletion records named before this step,
        /// in the copy the step was played in: its last step, or the start, which every copy
        /// begins as.
        /// </summary>
        private static (ISet<string> Present, ISet<string> Deletions) Previous(JToken step)
        {
            var steps = (JArray)step.Parent;
            var copy = (int)step["copy"];
            var last = steps.Take(steps.IndexOf(step)).LastOrDefault(earlier => (int)earlier["copy"] == copy);
            if (last == null)
            {
                var start = ((JObject)steps.Parent.Parent)["start"];
                return (
                    new HashSet<string>(((JObject)start["file"]["entries"]).Properties().Select(entry => entry.Name), StringComparer.Ordinal),
                    new HashSet<string>(((JArray)start["deletions"]).Select(Id), StringComparer.Ordinal));
            }

            return (
                new HashSet<string>(((JArray)last["present"]).Select(Id), StringComparer.Ordinal),
                new HashSet<string>(((JArray)last["deletions"]).Select(Id), StringComparer.Ordinal));
        }

        /// <summary>What the model's copy held present before this step: its last step's, or the start's.</summary>
        private static JArray PreviousPresent(JToken step)
        {
            var steps = (JArray)step.Parent;
            var copy = (int)step["copy"];
            var last = steps.Take(steps.IndexOf(step)).LastOrDefault(earlier => (int)earlier["copy"] == copy);
            return (JArray)(last == null ? ((JObject)steps.Parent.Parent)["start"]["present"] : last["present"]);
        }

        /// <summary>The identifier of an item the model lists, or the item itself where it is the identifier.</summary>
        private static string Id(JToken item)
        {
            return item.Type == JTokenType.String ? (string)item : (string)item["id"];
        }

        private static Dictionary<string, string> Kinds(StoreReading reading)
        {
            return reading.InForce == null
                ? new Dictionary<string, string>()
                : reading.InForce.Records.ToDictionary(record => record.Hex, record => record.Form.Kind, StringComparer.Ordinal);
        }

        /// <summary>
        /// Every record present and every deletion record of a store, by identifier.
        /// </summary>
        private static IEnumerable<string> Held(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                return Kinds(reading).Keys.Concat(reading.Deletions.Select(deleted => "deleted " + KeePassGate.Hex.Of(deleted))).ToList();
            }
        }

        private static int Count(string after)
        {
            return string.IsNullOrEmpty(after) ? 0 : after.Split(' ').Length;
        }

        private static AuthorityService Service(MemoryStore store)
        {
            return new AuthorityService(store, NullGateLog.Instance);
        }

        private static JObject Vectors()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreWriteVectors.json");
                if (File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreWriteVectors.json is not above the test's directory");
        }

        /// <summary>The vectors read with every member that looks like a date kept as the text it is.</summary>
        private static JObject VectorsAsText()
        {
            for (var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); at != null; at = at.Parent)
            {
                var path = Paths.Join(at.FullName, "tests", "Shared", "StoreWriteVectors.json");
                if (File.Exists(path))
                {
                    using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                    {
                        return JObject.Load(reader);
                    }
                }
            }

            throw new FileNotFoundException("tests/Shared/StoreWriteVectors.json is not above the test's directory");
        }

        /// <summary>
        /// The model's clients as the product admits them: a certificate issued for each of the
        /// model's names, `c0` the client the start holds, `c1` and on the clients that pair in the
        /// order they pair, and a name and `.1` the certificate a renewal of that client issued.
        /// </summary>
        private sealed class Clients : IDisposable
        {
            private readonly Dictionary<string, AdmissionRecord> _admitted = new Dictionary<string, AdmissionRecord>(StringComparer.Ordinal);
            private readonly List<X509Certificate2> _issued = new List<X509Certificate2>();

            /// <summary>The admission of the model's name.</summary>
            public AdmissionRecord Admission(string name)
            {
                return _admitted[name];
            }

            /// <summary>
            /// The admission of `c0`, with the profile the model's start gives it.
            /// </summary>
            public AdmissionRecord Started(string issuer)
            {
                return Admit("c0", AdmissionRecord.For(Issue("c0"), Then, new[] { "pf" }, issuer));
            }

            /// <summary>
            /// A pairing's admission of the next client, with the profiles a model's pairing gives.
            /// </summary>
            public AdmissionRecord Paired(string issuer)
            {
                var name = "c" + _admitted.Keys.Count(known => known.IndexOf('.') < 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Last = name;
                return Admit(name, AdmissionRecord.For(Issue(name), Then, new[] { "pf", "pg" }, issuer));
            }

            /// <summary>The name of the client the last pairing admitted.</summary>
            public string Last { get; private set; }

            /// <summary>
            /// A renewal's admission of the client of this name, from its admission and with its
            /// profiles.
            /// </summary>
            public AdmissionRecord Renewed(string name, string issuer)
            {
                var from = _admitted[name];
                var renewed = name + ".1";
                return Admit(renewed, AdmissionRecord.For(Issue(renewed), Then, from.Profiles, issuer, from.ClientKey, from.AdmissionKey));
            }

            public void Dispose()
            {
                foreach (var certificate in _issued)
                {
                    certificate.Reset();
                }
            }

            private AdmissionRecord Admit(string name, AdmissionRecord admission)
            {
                _admitted[name] = admission;
                return admission;
            }

            private X509Certificate2 Issue(string name)
            {
                var certificate = Minted.Named("CN=" + name).Lasting(Then.AddDays(-1), Then.AddYears(1)).SelfSigned().Read();
                _issued.Add(certificate);
                return certificate;
            }
        }
    }
}
