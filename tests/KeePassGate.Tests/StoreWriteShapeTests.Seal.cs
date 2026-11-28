using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The sequences of Seal again, R1 to R21 and R3s, and those of what its summary entry restates
    /// of a decision a deletion record alone tells of, R5 to R7 and F5 to F7r with their merges,
    /// played through the product's acts and held to what the measured model wrote and read after
    /// each step, `STORE.md` §625 and §611.
    /// </summary>
    /// <remarks>
    /// **Every act is the product's**: the commands on the authority are `AuthorityService`'s, a
    /// pairing, a revocation and a refusal the store's acts, the rows and the switch the acts of a
    /// node's dialog, `RuleActs`, and the seal `SealActs`, alone or held with an act in the KPG
    /// tab's buffer and written with it at OK in a single write, `PendingRecords`. **A pairing
    /// writes its admission alone**, as the model's does: the product's pairing also adds the
    /// names it approves that the list does not hold, which the model's does not, and the names
    /// compared would part at once.
    ///
    /// **Held after each step**: the state; the records present by kind, and the versions by
    /// object; the deletion records by kind; each summary entry's height, names and items; what is
    /// taken away; whether an authority is served and how many stand; which clients are admitted
    /// with which names; the names on the list; what every node gives every profile; each client's
    /// answer to a renewal; whether the step was played where the model's copy played it; and,
    /// where the store is stopped, what it lost, `StoreLosses`, as the model's listing has it.
    /// </remarks>
    public sealed partial class StoreWriteShapeTests
    {
        /// <summary>The kinds held by count, present and as deletion records: every kind the sequences write but the version record.</summary>
        private static readonly string[] SealKinds = { "authority", "client", "head", "profile", "removed", "retired", "revoked", "version" };

        /// <summary>The model's reasons for a stop, and the conditions the product names them.</summary>
        private static readonly Dictionary<string, StoreStop> Stops = new Dictionary<string, StoreStop>(StringComparer.Ordinal)
        {
            { "no summary entry of the store is in force", StoreStop.NoSummaryEntry },
            { "a summary entry was deleted that nothing in force replaces", StoreStop.SummaryEntryDeleted },
            { "entries of the store have no version that passes", StoreStop.Damage },
            { "a record no summary entry restates", StoreStop.NotRestated },
        };

        /// <summary>A keyed tag and subject hash, the first 16 hexadecimal characters of an identifier.</summary>
        private static readonly string Carried = new string('0', (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2);

        /// <summary>
        /// The sequences of Seal again, by name, and those of what its summary entry restates of a
        /// decision a deletion record alone tells of, R5 to R7 and F5 to F7r, each followed by its
        /// merges with and without the deletion records cleared.
        /// </summary>
        private static IEnumerable<TestCaseData> SealSequences()
        {
            return ((JObject)Vectors()["sequences"]).Properties()
                .Select(sequence => sequence.Name)
                .Where(name => name.StartsWith("seal, ", StringComparison.Ordinal) || name.StartsWith("restates, ", StringComparison.Ordinal))
                .Select(name => new TestCaseData(name).SetArgDisplayNames(name.Substring(0, name.IndexOf(':'))));
        }

        [TestCaseSource(nameof(SealSequences))]
        public void A_sequence_of_Seal_again_writes_and_reads_what_the_model_did(string name)
        {
            var sequence = (JObject)VectorsAsText()["sequences"][name];
            var moment = DateTimeOffset.Parse((string)sequence["clock"][0], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                StartedNamed(first, "pf");
                StartedRules(first, sequence);

                // The model reads a deletion record at every node of its tree, as a gate reads the
                // database's tree.
                first.Tree.AddRange(new[] { (string)sequence["tree"]["root"] }
                    .Concat(((JObject)sequence["tree"]["groups"]).Properties().Select(group => group.Name))
                    .Select(StoreComposer.BytesOf));
                var copies = new[] { first, first.Copy() };
                var gives = new[] { (JObject)sequence["start"]["answer"]["rules"]["gives"], (JObject)sequence["start"]["answer"]["rules"]["gives"] };
                // The model's clients by their keys, and its authorities by their names, each taken
                // for the product's as it is paired or made.
                var labels = ((JObject)sequence["clients"]).Properties().ToDictionary(client => (string)client.Value, client => client.Name, StringComparer.Ordinal);
                var authorities = new Dictionary<string, string>(StringComparer.Ordinal);
                Authorities(sequence["start"], first, authorities);

                // The KPG tab's buffer of each copy, holding the seal from the press on its dialog
                // to the settings dialog's OK, as the seal's dialog is shown and the seal written.
                var buffers = new PendingRecords[2];
                AssertSealed(sequence["start"], sequence, first, clients, labels, authorities, name.Substring(0, name.IndexOf(':')) + ", the start");

                foreach (var step in (JArray)sequence["steps"])
                {
                    var command = (string)step["command"];
                    var copy = (int)step["copy"];
                    var store = copies[copy];
                    var where = name.Substring(0, name.IndexOf(':')) + ", step " + ((JArray)sequence["steps"]).IndexOf(step) + ", " + command;

                    var played = PlayedSealing(store, copies, buffers, sequence, step, clients, labels, gives[copy], moment, where);
                    Assert.That(played, Is.EqualTo((bool)step["played"]), where + ": played where the model's copy played it");
                    if (command == "Pair")
                    {
                        // A client the sequence's names do not hold, the model's further pairings.
                        foreach (var paired in ((JArray)step["present"]).Where(item => (string)item["kind"] == "client")
                                     .Select(item => ((string)item["subject"]).Substring("client:".Length))
                                     .Where(key => !labels.ContainsKey(key)))
                        {
                            labels[paired] = clients.Last;
                        }
                    }

                    Authorities(step, store, authorities);
                    AssertSealed(step, sequence, store, clients, labels, authorities, where);
                    gives[copy] = (JObject)step["answer"]["rules"]["gives"];
                }

                // The accepted class of `STORE.md` §639 row 5: a revocation lost in both carriers
                // with no deletion record is in force nowhere once the store is sealed again, as the
                // model's copies hold it, and never as a decision kept.
                if (sequence["accepted"] != null)
                {
                    Assert.That((string)sequence["accepted"], Does.StartWith("STORE.md 639 row 5"));
                    using (var reading = first.Read())
                    {
                        Assert.That(reading.InForce.Taken("revoked"), Does.Not.Contain(clients.Admission("c0").ClientKey), "the accepted class: the revocation lost with no record");
                    }
                }
            }
        }

        /// <summary>A step of a sequence of Seal again, played in this copy; whether it was played.</summary>
        /// <remarks>
        /// **The seal's dialog is the press**: the seal is pressed where the dialog shows, held in
        /// the copy's buffer, and written at the step that seals, composed again over the store as
        /// it stands then, as the settings dialog's OK writes what its tab held. A merge between
        /// the two is what the seal is composed after.
        /// </remarks>
        private static bool PlayedSealing(
            MemoryStore store,
            MemoryStore[] copies,
            PendingRecords[] buffers,
            JObject sequence,
            JToken step,
            Clients clients,
            Dictionary<string, string> labels,
            JObject gives,
            DateTimeOffset now,
            string where)
        {
            var act = (JArray)step["act"];
            var command = (string)step["command"];
            var copy = (int)step["copy"];
            switch (command)
            {
                case "Synchronize":
                    store.Synchronize(copies[(int)act[3]]);
                    return true;
                case "Merge by KeePassXC":
                    store.MergeWithoutDeletions(copies[(int)act[3]]);
                    return true;
                case "Overwrite if newer":
                    store.OverwriteIfNewer(copies[(int)act[3]]);
                    return true;
                case "Clear deletion records":
                    store.ClearDeletions();
                    return true;
                case "Show seal":
                    using (var reading = store.Read())
                    {
                        if (reading.State != StoreState.Stopped)
                        {
                            return false;
                        }
                    }

                    buffers[copy] = new PendingRecords(() => store);
                    var pressed = Service(buffers[copy]).Seal();
                    Assert.That(pressed.Succeeded, Is.True, where + ": " + pressed.Message);
                    return true;

                case "Seal again":
                    if (buffers[copy] == null)
                    {
                        return Sealed(store, where);
                    }

                    return Ok(store, ref buffers[copy], 1, where);
                case "Edit past the limit":
                    Edited(store, step, clients, labels, where);
                    return true;
                case "Delete by hand":
                    DeletedForGood(store, step, (int)act[3] == 1, clients, labels, where);
                    return true;
                case "Lose version":
                    LostVersion(store, sequence, (string)act[2]);
                    return true;
                default:
                    if (command.StartsWith("Seal again with ", StringComparison.Ordinal))
                    {
                        if (buffers[copy] == null)
                        {
                            buffers[copy] = new PendingRecords(() => store);
                            var sealedAgain = Service(buffers[copy]).Seal();
                            Assert.That(sealedAgain.Succeeded, Is.True, where + ": " + sealedAgain.Message);
                        }

                        // The act as a step of its own, which a node's dialog plays from its command
                        // and act, pressed over the store as the seal leaves it.
                        var inner = (JObject)step.DeepClone();
                        inner["command"] = command.Substring("Seal again with ".Length);
                        inner["act"] = act[2];
                        Assert.That(
                            PlayedOver(buffers[copy], sequence, inner, (JArray)act[2], (string)inner["command"], clients, gives, now, where),
                            Is.True,
                            where + ": the act over the sealed store");
                        return Ok(store, ref buffers[copy], 2, where);
                    }

                    return PlayedOver(store, sequence, step, act, command, clients, gives, now, where);
            }
        }

        /// <summary>A command on the store that is no seal, played over it, through the KPG tab's buffer or the store itself.</summary>
        private static bool PlayedOver(
            IGateStore store, JObject sequence, JToken step, JArray act, string command, Clients clients, JObject gives, DateTimeOffset now, string where)
        {
            switch (command)
            {
                case "Revoke":
                    StoreSeeds.Revoke(store, clients.Admission((string)act[2]).ClientKey);
                    return true;
                case "Recreate":
                    return Service(store).Recreate().Succeeded;
                case "Delete":
                    return Service(store).Delete().Succeeded;
                case "Create":
                    return Service(store).Create().Succeeded;
                case "Pair":
                    // Where no authority is served, as where two stand, there is nothing to pair
                    // under, and the model's pairing does not apply either.
                    var served = StoreSeeds.Served(store);
                    if (served == null)
                    {
                        return false;
                    }

                    Paired(store, clients.Paired(served), where);
                    return true;
                case "Add name":
                    StoreSeeds.Name(store, (string)act[2]);
                    return true;
                case "Remove name":
                    StoreSeeds.Unname(store, (string)act[2]);
                    return true;
                case "Set row":
                case "Set rows":
                case "Set switch":
                    PlayedRules(store, sequence, step, gives, where);
                    return (bool)step["played"];
                case "Refuse forever":
                    return Refused(store, clients.Admission((string)act[2]).ClientKey, now, where);
                case "Renew":
                    using (var reading = store.Read())
                    {
                        var admission = clients.Admission((string)act[2]);
                        if (StoreAdmissions.Admitted(reading, admission.AdmissionKey) == null
                            || StoredRenewals.Of(reading.InForce, admission.ClientKey).Refuses(now))
                        {
                            return false;
                        }
                    }

                    StoreSeeds.Admit(store, clients.Renewed((string)act[2], StoreSeeds.Served(store)));
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, "a command this test does not play");
            }
        }

        /// <summary>The seal's act alone, composed over the store as it stands, written; whether it was.</summary>
        private static bool Sealed(MemoryStore store, string where)
        {
            StoreAct seal;
            using (var reading = store.Read())
            {
                string refused;
                seal = SealActs.Seal(reading, out refused);
                if (seal == null)
                {
                    return false;
                }
            }

            try
            {
                var done = store.Write(new[] { seal });
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
                return true;
            }
            finally
            {
                seal.Forget();
            }
        }

        /// <summary>
        /// The settings dialog's OK over a buffer holding the seal first, and the act pressed after
        /// it where there is one: every act composed again over the store as it stands, written in
        /// a single write, and the buffer let go; whether anything was written.
        /// </summary>
        /// <param name="acts">How many acts the buffer holds, the seal among them.</param>
        private static bool Ok(MemoryStore store, ref PendingRecords buffer, int acts, string where)
        {
            try
            {
                Assert.That(buffer.Acts, Has.Count.EqualTo(acts), where + ": the acts held");
                var composed = buffer.Composed().Where(made => made != null).ToList();
                if (composed.Count == 0)
                {
                    return false;
                }

                Assert.That(composed[0].Seals, Is.True, where + ": the seal first");
                var done = store.Write(composed);
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
                Assert.That(done.Acts, Has.Count.EqualTo(acts), where + ": every act held, in a single write");
                return true;
            }
            finally
            {
                buffer.Discard();
                buffer = null;
            }
        }

        /// <summary>A pairing's admission, written alone, as the model's pairing writes it.</summary>
        private static void Paired(IGateStore store, AdmissionRecord admission, string where)
        {
            StoreAct alone;
            using (var reading = store.Read())
            {
                var act = AdmissionActs.Admit(reading, admission);
                var key = act.Key();
                try
                {
                    alone = new StoreAct(act.Name, key, act.Against, act.Records.Where(record => record.Kind == StoreKinds.Client).ToList(), act.Why);
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                    act.Forget();
                }
            }

            try
            {
                var done = store.Write(new[] { alone });
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
            }
            finally
            {
                if (!(store is PendingRecords))
                {
                    alone.Forget();
                }
            }
        }

        /// <summary>A refusal of the client's renewals forever, given where the reading asks; whether it was.</summary>
        private static bool Refused(IGateStore store, string client, DateTimeOffset now, string where)
        {
            StoreAct refusal;
            using (var reading = store.Read())
            {
                var standing = StoredRenewals.Of(reading.InForce, client);
                if (standing.Refuses(now))
                {
                    return false;
                }

                string refused;
                refusal = RenewalActs.Refuse(reading, client, RenewalRefusal.Forever(), standing.Standing, out refused);
                Assert.That(refusal, Is.Not.Null, where + ": " + refused);
            }

            var done = store.Write(new[] { refusal });
            Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
            if (!(store is PendingRecords))
            {
                refusal.Forget();
            }

            return true;
        }

        /// <summary>
        /// An entry of the store edited past the history limit: its content followed by a space and
        /// no history, so that no version of it passes, as the model writes it.
        /// </summary>
        private static void Edited(MemoryStore store, JToken step, Clients clients, Dictionary<string, string> labels, string where)
        {
            var hex = Target(store, Touched(step), clients, labels, where);
            var entry = store.Candidates.Single(candidate => candidate.Hex == hex);
            var strings = entry.Versions[0].Names
                .Select((stringName, at) => new KeyValuePair<string, string>(stringName, entry.Versions[0].Strings.Read(entry.Versions[0].Values[at])))
                .ToDictionary(pair => pair.Key, pair => pair.Key == StoreComposer.Data ? pair.Value + " " : pair.Value, StringComparer.Ordinal);
            store.Drop(hex);
            store.Put(entry.Identifier, strings);
        }

        /// <summary>An entry of the store deleted for good, with a deletion record or with none.</summary>
        private static void DeletedForGood(MemoryStore store, JToken step, bool recorded, Clients clients, Dictionary<string, string> labels, string where)
        {
            var hex = Target(store, Touched(step), clients, labels, where);
            store.Drop(hex);
            if (recorded)
            {
                store.Delete(StoreComposer.BytesOf(hex));
            }
        }

        /// <summary>
        /// The entry of the model's file a step deleted or edited past the limit, as the model
        /// lists it: the item of its kind, subject, number and, for a version, object.
        /// </summary>
        private static JObject Touched(JToken step)
        {
            if ((string)step["command"] == "Delete by hand")
            {
                // The model lists as deleted what a deletion record names, so an entry deleted
                // with none is the entry its file held before the step and does not hold after it.
                var deleted = ((JArray)step["deleted"]).Cast<JObject>().ToList();
                if (deleted.Count == 0)
                {
                    var now = new HashSet<string>(((JArray)step["present"]).Select(Id), StringComparer.Ordinal);
                    deleted = PreviousPresent(step).Cast<JObject>().Where(item => !now.Contains((string)item["id"])).ToList();
                }

                Assert.That(deleted, Has.Count.EqualTo(1), "the model deleted a single entry by hand");
                var touched = (JObject)deleted[0].DeepClone();
                if ((string)touched["kind"] == "version")
                {
                    // The object, from the entry as the model's file held it before the step.
                    var data = (string)PreviousFile(step)["entries"][(string)touched["id"]]["strings"][StoreComposer.Data];
                    touched["object"] = (string)JObject.Parse(data)["object"];
                }

                return touched;
            }

            var before = new HashSet<string>(((JArray)PreviousStep(step)?["damaged"] ?? new JArray()).Select(item => (string)item["id"]), StringComparer.Ordinal);
            return ((JArray)step["damaged"]).Cast<JObject>().Single(item => !before.Contains((string)item["id"]));
        }

        /// <summary>The step of the same copy before this one, or null where this copy's first step is it.</summary>
        private static JToken PreviousStep(JToken step)
        {
            var steps = (JArray)step.Parent;
            var copy = (int)step["copy"];
            return steps.Take(steps.IndexOf(step)).LastOrDefault(earlier => (int)earlier["copy"] == copy);
        }

        /// <summary>The model's file of the same copy before this step: its last step's, or the start's.</summary>
        private static JObject PreviousFile(JToken step)
        {
            var last = PreviousStep(step);
            return (JObject)(last == null ? ((JObject)step.Parent.Parent.Parent)["start"]["file"] : last["file"]);
        }

        /// <summary>
        /// The product's entry for an entry of the model's: a summary entry of the same height, the
        /// standing version of the same object, a revocation or an admission of the same client,
        /// the retirement, or the removal of the same name.
        /// </summary>
        private static string Target(MemoryStore store, JObject touched, Clients clients, Dictionary<string, string> labels, string where)
        {
            using (var reading = store.Read())
            {
                var inForce = reading.InForce;
                var subject = (string)touched["subject"];
                switch ((string)touched["kind"])
                {
                    case "head":
                        var heads = inForce.Heads.Where(head => head.Form.Number == (int)touched["number"]).OrderBy(head => head.Hex, StringComparer.Ordinal).ToList();
                        Assert.That(heads, Is.Not.Empty, where + ": a summary entry of the model's height");
                        return heads[0].Hex;
                    case "version":
                        var objectText = Product((string)touched["object"], clients, labels);
                        var versions = inForce.VersionsOf(objectText);
                        Assert.That(versions, Has.Count.EqualTo(1), where + ": a single standing version of " + objectText);
                        return versions[0].Id;
                    case "revoked":
                        var revoked = clients.Admission(labels[subject.Substring("client:".Length)]).ClientKey;
                        return inForce.Records.Single(record => record.Is(StoreKinds.Revoked) && record.Form.Member(StoreKinds.ClientMember) == revoked).Hex;
                    case "retired":
                        return inForce.Records.Single(record => record.Is(StoreKinds.Retired)).Hex;
                    case "removed":
                        var name = subject.Substring("profile:".Length);
                        return inForce.Records.Single(record => record.Is(StoreKinds.Removed) && record.Form.Member(StoreKinds.NameMember) == name).Hex;
                    case "client":
                        var admitted = clients.Admission(labels[subject.Substring("client:".Length)]).ClientKey;
                        return inForce.Admissions.Single(record => record.Form.Member(StoreKinds.ClientMember) == admitted).Hex;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(touched), (string)touched["kind"], "a kind this test does not touch");
                }
            }
        }

        /// <summary>
        /// A deletion record of a version of `rule:N/pf` whose content no file holds, at the row's
        /// place under the store's key.
        /// </summary>
        private static void LostVersion(MemoryStore store, JObject sequence, string modelObject)
        {
            using (var reading = store.Read())
            {
                var key = (byte[])reading.InForce.Key.Bytes.Clone();
                try
                {
                    var slash = modelObject.IndexOf('/');
                    var node = (string)sequence["nodes"][modelObject.Substring("rule:".Length, slash - "rule:".Length)];
                    var number = StoreComposer.ProfileTag(key, modelObject.Substring(slash + 1));
                    store.Delete(StoreComposer.IdentifierOf(key, "version", "rule:" + node, number, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")));
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }
        }

        /// <summary>The model's authority served after a step, taken for the product's, where both serve one.</summary>
        private static void Authorities(JToken step, MemoryStore store, Dictionary<string, string> authorities)
        {
            var served = step["answer"]["served"];
            if (served.Type == JTokenType.Null || authorities.ContainsKey((string)served))
            {
                return;
            }

            using (var reading = store.Read())
            {
                var id = StoreAuthorities.Served(reading);
                if (id != null)
                {
                    authorities[(string)served] = id;
                }
            }
        }

        /// <summary>
        /// The items of a kind that takes away in a summary entry, as the product holds them and
        /// as the model's do, its names taken for the product's: client keys, authorities and
        /// names, each by its subject, and each decision whose subject the file does not name by
        /// an item with no subject, `STORE.md` §611.
        /// </summary>
        /// <param name="named">The values the product's items hold by subject.</param>
        /// <param name="unnamed">The product's items with no subject: a `hash`, or the `id` of a removal.</param>
        /// <param name="model">The values the model's items hold by subject.</param>
        /// <param name="modelUnnamed">The values the model's items with no subject are about, as the model's deletion records name them.</param>
        private static void AssertItems(
            StoreReading reading,
            string kind,
            IEnumerable<string> named,
            IEnumerable<string> unnamed,
            IEnumerable<string> model,
            IEnumerable<string> modelUnnamed,
            string what,
            string where)
        {
            // The control's summary entries, those of R1 to R21, restate a decision by its subject
            // where the file names it nowhere, as the model's own elements hold it; the product's,
            // and the model's under the form adopted, restate such a decision by an item with no
            // subject, which holds its keyed tag and subject hash, or a removal's identifier. A
            // summary entry keeps what it was written with, so the file may name the subject since.
            var held = named.ToList();
            var expected = model.ToList();
            Assert.That(held, Is.SubsetOf(expected), where + ": " + what + ", by subject, nothing the model does not restate");
            var owed = expected.Except(held, StringComparer.Ordinal).Concat(modelUnnamed).Distinct(StringComparer.Ordinal).ToList();
            Assert.That(
                unnamed.Select(text => text.Substring(0, Carried.Length)).OrderBy(text => text, StringComparer.Ordinal),
                Is.EqualTo(owed.Select(value => CarriedOf(reading, kind, value)).OrderBy(text => text, StringComparer.Ordinal)),
                where + ": " + what + ", with no subject: an item for each decision whose subject the file does not name");
        }

        /// <summary>
        /// The values a kind takes away, as the product holds them and as the model's answer does,
        /// its names taken for the product's: a value the reading does not hold is a value the
        /// file names nowhere, whose decision a deletion record or an item with no subject still
        /// carries, `STORE.md` §623 step 2.
        /// </summary>
        private static void AssertTaken(
            StoreReading reading,
            string kind,
            IEnumerable<string> product,
            IEnumerable<string> model,
            string what,
            string where)
        {
            var held = product.ToList();
            var expected = model.ToList();
            Assert.That(held, Is.SubsetOf(expected), where + ": " + what + ", nothing the model does not take away");
            var carriers = new HashSet<string>(
                reading.Deletions.Select(deleted => StoreComposer.HexOf(deleted).Substring(0, Carried.Length))
                    .Concat(reading.InForce.Items.Where(item => item.Kind == kind)
                        .Select(item => item.Member(kind == StoreKinds.Removed ? StoreKinds.Id : "hash"))
                        .Where(text => text != null)
                        .Select(text => text.Substring(0, Carried.Length))),
                StringComparer.Ordinal);
            foreach (var missing in expected.Except(held, StringComparer.Ordinal))
            {
                Assert.That(Named(reading, missing), Is.False, where + ": " + what + ": " + missing + " is named in the file and not taken away");
                Assert.That(carriers, Does.Contain(CarriedOf(reading, kind, missing)), where + ": " + what + ": " + missing + " is carried by nothing");
            }
        }

        /// <summary>The keyed tag and subject hash a decision of a kind about a value carries, as 16 hexadecimal characters.</summary>
        private static string CarriedOf(StoreReading reading, string kind, string value)
        {
            var key = (byte[])reading.InForce.Key.Bytes.Clone();
            try
            {
                return StoreComposer.HexOf(StoreComposer.IdentifierOf(key, kind, StoreKinds.Find(kind).SubjectPrefix + value, 0, new string('0', 12)))
                    .Substring(0, Carried.Length);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// What the model's deletion records named in a sequence, at its start and at any step,
        /// by identifier: the kind and the subject, which is what an item with no subject is about.
        /// </summary>
        private static Dictionary<string, JObject> ModelTold(JObject sequence)
        {
            var told = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var deleted in new[] { sequence["start"] }.Concat((JArray)sequence["steps"])
                         .SelectMany(step => (JArray)step["deletions"] ?? new JArray())
                         .Cast<JObject>())
            {
                told[(string)deleted["id"]] = deleted;
            }

            return told;
        }

        /// <summary>
        /// The value the model's item with no subject is about, its name taken for the product's:
        /// the subject of the model's deletion record whose identifier the item's `hash` begins,
        /// or whose identifier is the item's `id`.
        /// </summary>
        private static string ModelUnnamed(
            JObject item, Dictionary<string, JObject> told, Clients clients, Dictionary<string, string> labels, Dictionary<string, string> authorities)
        {
            var kind = (string)item["kind"];
            var text = (string)item["hash"] ?? (string)item["id"];
            var deleted = told.Where(pair => pair.Key.StartsWith(text, StringComparison.Ordinal) && (string)pair.Value["kind"] == kind)
                .Select(pair => pair.Value).ToList();
            Assert.That(deleted.Select(found => (string)found["subject"]).Distinct(), Has.Exactly(1).Items, "the model's item with no subject is about a single subject: " + text);
            var subject = (string)deleted[0]["subject"];
            var value = subject.Substring(StoreKinds.Find(kind).SubjectPrefix.Length);
            switch (kind)
            {
                case StoreKinds.Revoked:
                    return ClientOf(clients, labels, value);
                case StoreKinds.Retired:
                    return AuthorityOf(authorities, value);
                default:
                    return value;
            }
        }

        /// <summary>Whether a model's item restates its decision with no subject.</summary>
        private static bool Unnamed(JObject item)
        {
            return item["hash"] != null || ((string)item["kind"] == StoreKinds.Removed && item["name"] == null);
        }

        /// <summary>Whether a product's item restates its decision with no subject.</summary>
        private static bool Unnamed(StoreItem item)
        {
            return item.Member("hash") != null || (item.Kind == StoreKinds.Removed && item.Member(StoreKinds.NameMember) == null);
        }

        /// <summary>The product's client key for a model's, or the model's where the product issued no certificate for it.</summary>
        private static string ClientOf(Clients clients, Dictionary<string, string> labels, string modelKey)
        {
            string label;
            return labels.TryGetValue(modelKey, out label) && Known(clients, label) ? clients.Admission(label).ClientKey : modelKey;
        }

        /// <summary>The product's authority for a model's name, or the name where the product served none for it.</summary>
        private static string AuthorityOf(Dictionary<string, string> authorities, string label)
        {
            string id;
            return authorities.TryGetValue(label, out id) ? id : label;
        }

        /// <summary>Whether a record or an item of the reading holds this value as a member.</summary>
        private static bool Named(StoreReading reading, string value)
        {
            return value != null
                   && (reading.InForce.Records.Any(record => record.Form.Members.Values.Contains(value))
                       || reading.InForce.Items.Any(item => item.Members.Values.Contains(value)));
        }

        /// <summary>A step of a sequence of Seal again, held to the model's file and answer after it.</summary>
        private static void AssertSealed(
            JToken step, JObject sequence, MemoryStore store, Clients clients, Dictionary<string, string> labels, Dictionary<string, string> authorities, string where)
        {
            using (var reading = store.Read())
            {
                var answer = step["answer"];
                var status = (string)answer["status"];
                Assert.That(reading.State, Is.EqualTo(status == "ok" ? StoreState.Whole : StoreState.Stopped), where + ": " + reading.Reason);
                var inForce = reading.InForce;

                // The records present by kind, and the versions by object.
                Assert.That(
                    SealKinds.ToDictionary(kind => kind, kind => inForce.Records.Count(record => record.Is(kind))),
                    Is.EquivalentTo(SealKinds.ToDictionary(kind => kind, kind => ((JArray)step["present"]).Count(item => (string)item["kind"] == kind))),
                    where + ": the records present by kind");
                Assert.That(
                    inForce.Records.Where(record => record.Is(StoreKinds.Version)).Select(record => record.Form.Member(StoreKinds.ObjectMember)).OrderBy(text => text, StringComparer.Ordinal),
                    Is.EqualTo(((JArray)step["present"]).Where(item => (string)item["kind"] == "version").Select(item => Product((string)item["object"], clients, labels)).OrderBy(text => text, StringComparer.Ordinal)),
                    where + ": the versions present by object");

                // The deletion records by the kind their tag names.
                var tags = StoreKinds.All.ToDictionary(kind => inForce.Key.TagOf(kind), kind => kind.Name);
                Assert.That(
                    SealKinds.ToDictionary(kind => kind, kind => reading.Deletions.Count(deleted => tags.TryGetValue(StoreFormat.TagNumber(deleted), out var named) && named == kind)),
                    Is.EquivalentTo(SealKinds.ToDictionary(kind => kind, kind => ((JArray)step["deletions"]).Count(item => (string)item["kind"] == kind))),
                    where + ": the deletion records by kind");

                // Each summary entry's height and names, and its items: of every kind but those that
                // take away the same number, and of those every value the reading can name.
                var heads = inForce.Heads.OrderBy(head => head.Form.Number).ThenBy(head => Count(head.Form.Member("after"))).ToList();
                var modelHeads = ((JArray)step["heads"]).OrderBy(head => (int)head["height"]).ThenBy(head => (int)head["after"]).ToList();
                Assert.That(
                    heads.Select(head => head.Form.Number + " " + Count(head.Form.Member("after"))),
                    Is.EqualTo(modelHeads.Select(head => (int)head["height"] + " " + (int)head["after"])),
                    where + ": each summary entry's height and names");
                var taking = new[] { StoreKinds.Revoked, StoreKinds.Retired, StoreKinds.Removed };
                var modelTold = ModelTold(sequence);
                for (var at = 0; at < heads.Count; at++)
                {
                    var items = heads[at].Form.Items.Where(item => item.Kind != StoreKinds.Format).ToList();
                    var modelItems = ((JArray)modelHeads[at]["items"]).Cast<JObject>().ToList();
                    Assert.That(
                        items.Where(item => !taking.Contains(item.Kind)).GroupBy(item => item.Kind).ToDictionary(group => group.Key, group => group.Count()),
                        Is.EquivalentTo(modelItems.Where(item => !taking.Contains((string)item["kind"])).GroupBy(item => (string)item["kind"]).ToDictionary(group => group.Key, group => group.Count())),
                        where + ": the items of the summary entry of height " + heads[at].Form.Number);
                    foreach (var kind in taking)
                    {
                        var member = kind == StoreKinds.Revoked ? StoreKinds.ClientMember : kind == StoreKinds.Retired ? StoreKinds.AuthorityMember : StoreKinds.NameMember;
                        var ofKind = items.Where(item => item.Kind == kind).ToList();
                        var modelOfKind = modelItems.Where(item => (string)item["kind"] == kind).ToList();
                        AssertItems(
                            reading,
                            kind,
                            ofKind.Where(item => !Unnamed(item)).Select(item => item.Member(member)),
                            ofKind.Where(Unnamed).Select(item => item.Member(kind == StoreKinds.Removed ? StoreKinds.Id : "hash")),
                            modelOfKind.Where(item => !Unnamed(item)).Select(item =>
                                kind == StoreKinds.Revoked ? ClientOf(clients, labels, (string)item[member])
                                : kind == StoreKinds.Retired ? AuthorityOf(authorities, (string)item[member])
                                : (string)item[member]),
                            modelOfKind.Where(Unnamed).Select(item => ModelUnnamed(item, modelTold, clients, labels, authorities)),
                            "its items " + kind,
                            where);
                    }
                }

                // What is taken away, what is served, and who is admitted with which names.
                AssertTaken(
                    reading,
                    StoreKinds.Revoked,
                    inForce.Taken("revoked"),
                    ((JArray)answer["refusals"]).Where(refusal => (string)refusal[0] == "revoked").Select(refusal => ClientOf(clients, labels, (string)refusal[1])),
                    "the clients revoked",
                    where);
                AssertTaken(
                    reading,
                    StoreKinds.Retired,
                    inForce.Taken("retired"),
                    ((JArray)answer["refusals"]).Where(refusal => (string)refusal[0] == "retired").Select(refusal => AuthorityOf(authorities, (string)refusal[1])),
                    "the authorities retired",
                    where);
                Assert.That(StoreAuthorities.Served(reading) != null, Is.EqualTo(answer["served"].Type != JTokenType.Null), where + ": whether an authority is served");
                Assert.That(reading.Answering.Count, Is.EqualTo(((JArray)answer["auths"]).Count), where + ": the authorities standing");
                var names = KeePassGate.Acl.ProfileNames.Of(reading).Names;
                Assert.That(names, Is.EqualTo(((JArray)answer["names"]).Select(profile => (string)profile)), where + ": the names on the list");
                var admitted = labels.Values.Where(label => label.IndexOf('.') < 0 && Known(clients, label))
                    .Select(label => (Label: label, Admitted: StoreAdmissions.Admitted(reading, clients.Admission(label).AdmissionKey)))
                    .Where(pair => pair.Admitted != null)
                    .ToDictionary(pair => clients.Admission(pair.Label).ClientKey, pair => string.Join(" ", pair.Admitted.Profiles.Where(names.Contains).OrderBy(profile => profile, StringComparer.Ordinal)));
                Assert.That(
                    admitted,
                    Is.EquivalentTo(((JObject)answer["adm"]).Properties().ToDictionary(
                        client => clients.Admission(labels[client.Name]).ClientKey,
                        client => string.Join(" ", ((JArray)client.Value).Select(profile => (string)profile).OrderBy(profile => profile, StringComparer.Ordinal)))),
                    where + ": the clients admitted, with their names");

                // What every node gives every profile, and each client's answer to a renewal.
                var nodes = Tree((JObject)sequence["tree"], KeePassGate.Acl.StoredRules.Of(reading));
                foreach (var node in (JObject)answer["rules"]["gives"])
                {
                    foreach (var profile in (JObject)node.Value)
                    {
                        Assert.That(
                            LettersOf(KeePassGate.Acl.AclResolver.Arrives(nodes[node.Key], profile.Key)),
                            Is.EqualTo((string)profile.Value),
                            where + ": what " + node.Key.Substring(0, 2) + " gives " + profile.Key);
                    }
                }

                foreach (var answered in ((JObject)answer["renewals"]).Properties())
                {
                    string label;
                    if (!labels.TryGetValue(answered.Name, out label) || !Known(clients, label))
                    {
                        continue;
                    }

                    var stored = StoredRenewals.Of(inForce, clients.Admission(label).ClientKey);
                    var value = stored.Answer == null ? "ask" : stored.Answer.NotBefore.HasValue ? "until:" + Moments.Wire(stored.Answer.NotBefore.Value) : "forever";
                    Assert.That(value, Is.EqualTo((string)answered.Value["value"]), where + ": the answer to " + label + "'s renewals");
                }

                // What a stopped store lost, as the model's listing has it.
                var losses = StoreLosses.Of(reading);
                var expected = (JObject)step["losses"];
                if (losses == null)
                {
                    Assert.That((JArray)expected["reasons"], Is.Empty, where + ": nothing lost");
                    return;
                }

                Assert.That(losses.Stops, Is.EqualTo(((JArray)expected["reasons"]).Select(reason => Stops[(string)reason])), where + ": every condition that holds");
                Assert.That(
                    losses.Damaged.Select(damaged => damaged.Kind).OrderBy(kind => kind, StringComparer.Ordinal),
                    Is.EqualTo(((JArray)expected["damaged"]).Select(damaged => (string)damaged["kind"]).OrderBy(kind => kind, StringComparer.Ordinal)),
                    where + ": the entries that are damage, by kind");
                // Where no summary entry is in force the model's listing names no summary entry,
                // and the product's names those a deletion record tells of at the greatest height
                // of any summary entry of the store, present, damaged or told of, the summary entry
                // deleted last, `StoreLosses.Heads`.
                var noneInForce = ((JArray)expected["reasons"]).Any(reason => (string)reason == "no summary entry of the store is in force");
                var told = ((JArray)step["deletions"]).Where(item => (string)item["kind"] == StoreKinds.Head).Select(item => (int)item["number"]).ToList();
                var greatest = told
                    .Concat(((JArray)step["heads"]).Select(head => (int)head["height"]))
                    .Concat(((JArray)expected["damaged"]).Where(damaged => (string)damaged["kind"] == StoreKinds.Head).Select(damaged => HeightOf((string)damaged["id"])))
                    .DefaultIfEmpty(0)
                    .Max();
                if (noneInForce)
                {
                    Assert.That((JArray)expected["heads"], Is.Empty, where + ": the model's listing names no summary entry with none in force");
                }

                Assert.That(
                    losses.Heads.Count,
                    Is.EqualTo(noneInForce ? told.Count(height => height == greatest) : ((JArray)expected["heads"]).Count),
                    where + ": the summary entries deleted that nothing replaces");
                // The version record the product's Create writes is restated by the item `format`
                // of a summary entry, and the model writes none.
                var formats = new HashSet<string>(inForce.Records.Where(record => record.Is(StoreKinds.Format)).Select(record => record.Hex), StringComparer.Ordinal);
                Assert.That(
                    losses.NotRestated.Count(hex => !formats.Contains(hex)),
                    Is.EqualTo(((JArray)expected["notRestated"]).Count),
                    where + ": the records no summary entry restates");
                Assert.That(
                    losses.Revoked,
                    Is.EquivalentTo(((JArray)expected["byDeletion"]["revoked"]).Select(client => clients.Admission(labels[(string)client]).ClientKey)),
                    where + ": the revocations a deletion record alone states");
                Assert.That(losses.Retired.Count, Is.EqualTo(((JArray)expected["byDeletion"]["retired"]).Count), where + ": the retirements a deletion record alone states");
                Assert.That(
                    losses.Removed.Select(removal => removal.Key),
                    Is.EquivalentTo(((JArray)expected["byDeletion"]["removed"]).Select(removal => ((string)removal).Split(' ')[0])),
                    where + ": the removals a deletion record alone states");
                Assert.That(
                    losses.Lost.Select(loss => loss.Object + " " + loss.Lost.Count),
                    Is.EquivalentTo(((JObject)expected["lost"]).Properties().Select(lost => Product(lost.Name, clients, labels) + " " + ((JArray)lost.Value).Count)),
                    where + ": the objects lost");
                Assert.That(
                    losses.RefusesAfter,
                    Is.EquivalentTo(((JArray)expected["refusesAfter"]).Select(objectText => Product((string)objectText, clients, labels))),
                    where + ": the objects a damaged version's deletion record would make refuse");
                Assert.That(
                    losses.Lost.Select(loss => loss.Object + " " + SealActs.Granting(loss.Object) + " " + loss.After.Count + " " + loss.Settles.Count),
                    Is.EquivalentTo(((JArray)expected["seal"]).Select(seal => Product((string)seal["object"], clients, labels) + " " + (string)seal["doc"]
                                                                       + " " + ((JArray)seal["after"]).Count + " " + ((JArray)seal["lost"]).Count)),
                    where + ": what the seal writes");
            }
        }

        /// <summary>Bytes 9 and 10 of an identifier written as text: a summary entry's height.</summary>
        private static int HeightOf(string hex)
        {
            var start = (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2;
            return int.Parse(hex.Substring(start, StoreFormat.HeightLength * 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        /// <summary>Whether the test issued a certificate for the model's name yet.</summary>
        private static bool Known(Clients clients, string label)
        {
            try
            {
                return clients.Admission(label) != null;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        /// <summary>The product's object for an object the model's file names: a client key taken for the model's.</summary>
        private static string Product(string modelObject, Clients clients, Dictionary<string, string> labels)
        {
            string client;
            return RenewalText.ReadObject(modelObject, out client) ? RenewalText.ObjectOf(clients.Admission(labels[client]).ClientKey) : modelObject;
        }

        private static AuthorityService Service(IGateStore store)
        {
            return new AuthorityService(store, NullGateLog.Instance);
        }
    }
}
