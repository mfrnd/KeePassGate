using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Ui;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The sequences of a store started again, T1 to T16, played through the product's acts and
    /// held to what the measured model wrote and read after each step, `STORE.md` §632.
    /// </summary>
    /// <remarks>
    /// **Every act is the product's**: Start again is `AuthorityService.StartAgain` pressed on
    /// the KPG tab's buffer and written at the dialog's OK; the commands on the authority, the
    /// pairings, the renewals, the names and the rows are those of the sequences of Seal again,
    /// and Seal again is `SealActs` alone. **Raise the format** is a later plugin's write, which
    /// no command of this plugin makes: a version record of format 2 and a summary entry over
    /// what is in force, composed under the store key.
    ///
    /// **The keys are taken by the model's labels**: each key the product makes is the label the
    /// model gave the key that stands alone after the same step, and every step holds the keys
    /// found, those that stand and those set aside to the model's. **What is held under a key**
    /// is held under the key that stands: the records present by kind, the deletion records by
    /// kind and the summary entries, each of the model's filtered by the tags of the model's key
    /// that stands, since the product's reading reads nothing under a key set aside.
    /// </remarks>
    public sealed partial class StoreWriteShapeTests
    {
        /// <summary>The kinds held by count under the key that stands: those of Seal again, and the keep records.</summary>
        private static readonly string[] StartKinds = SealKinds.Concat(new[] { "kept" }).ToArray();

        /// <summary>The model's words for a store, and the states the product names them.</summary>
        private static readonly Dictionary<string, StoreState> StartStates = new Dictionary<string, StoreState>(StringComparer.Ordinal)
        {
            { "ok", StoreState.Whole },
            { "incomplete", StoreState.Stopped },
            { "split", StoreState.Split },
            { "outdated", StoreState.Outdated },
            { "nostore", StoreState.None },
        };

        /// <summary>The sequences of a store started again, by name.</summary>
        private static IEnumerable<TestCaseData> StartSequences()
        {
            return ((JObject)Vectors()["sequences"]).Properties()
                .Select(sequence => sequence.Name)
                .Where(name => name.StartsWith("starts, ", StringComparison.Ordinal))
                .Select(name => new TestCaseData(name).SetArgDisplayNames(name.Substring(0, name.IndexOf(':'))));
        }

        [TestCaseSource(nameof(StartSequences))]
        public void A_sequence_of_a_store_started_again_writes_and_reads_what_the_model_did(string name)
        {
            var sequence = (JObject)VectorsAsText()["sequences"][name];
            var steps = (JArray)sequence["steps"];
            var moment = DateTimeOffset.Parse((string)steps[0]["clock"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            var shortName = name.Substring(0, name.IndexOf(':'));
            using (var clients = new Clients())
            {
                var first = new MemoryStore();
                StoreSeeds.Authority(first);
                Started(first, clients);
                StartedNamed(first, "pf");
                StartedRules(first, sequence);
                first.Tree.AddRange(new[] { (string)sequence["tree"]["root"] }
                    .Concat(((JObject)sequence["tree"]["groups"]).Properties().Select(group => group.Name))
                    .Select(StoreComposer.BytesOf));
                var copies = new[] { first, first.Copy() };
                var gives = new[] { (JObject)sequence["start"]["answer"]["rules"]["gives"], (JObject)sequence["start"]["answer"]["rules"]["gives"] };
                var labels = ((JObject)sequence["clients"]).Properties().ToDictionary(client => (string)client.Value, client => client.Name, StringComparer.Ordinal);
                var authorities = new Dictionary<string, string>(StringComparer.Ordinal);
                Authorities(sequence["start"], first, authorities);

                // The model's keys by their names, and the product's names by the model's labels.
                var modelKeys = ((JObject)sequence["keys"]).Properties().ToDictionary(
                    key => key.Name, key => StoreComposer.BytesOf((string)key.Value), StringComparer.Ordinal);
                var byModelName = modelKeys.ToDictionary(key => StoreComposer.KeyName(key.Value), key => key.Key, StringComparer.Ordinal);
                var keys = new Dictionary<string, string>(StringComparer.Ordinal);
                var edited = new HashSet<string>(StringComparer.Ordinal);
                Keys(sequence["start"], first, keys);
                AssertStarted(sequence["start"], sequence, first, clients, labels, authorities, modelKeys, byModelName, keys, shortName + ", the start");

                foreach (var step in steps)
                {
                    var command = (string)step["command"];
                    var copy = (int)step["copy"];
                    var store = copies[copy];
                    var where = shortName + ", step " + steps.IndexOf(step) + ", " + command;

                    var played = PlayedStarting(store, copies, sequence, step, clients, labels, authorities, byModelName, keys, edited, gives[copy], moment, where);
                    Assert.That(played, Is.EqualTo((bool)step["played"]), where + ": played where the model's copy played it");
                    if (command == "Pair" && played)
                    {
                        foreach (var paired in ((JArray)step["present"]).Where(item => (string)item["kind"] == "client")
                                     .Select(item => ((string)item["subject"]).Substring("client:".Length))
                                     .Where(key => !labels.ContainsKey(key)))
                        {
                            labels[paired] = clients.Last;
                        }
                    }

                    if (command == "Renew" && played)
                    {
                        // The model's renewal admits a key of its own, the product's renewal the
                        // client's own key under a new certificate, named for its client.
                        foreach (var renewed in ((JObject)step["answer"]["adm"]).Properties().Select(client => client.Name).Where(key => !labels.ContainsKey(key)))
                        {
                            labels[renewed] = (string)step["act"][2] + ".1";
                        }
                    }

                    Authorities(step, store, authorities);
                    Keys(step, store, keys);
                    AssertStarted(step, sequence, store, clients, labels, authorities, modelKeys, byModelName, keys, where);
                    gives[copy] = (JObject)step["answer"]["rules"]["gives"];
                }

                // The class `STORE.md` §639 accepts and no write decides: the new authority's entry
                // and the stop both deleted by hand serve the earlier store whole, its client
                // admitted, as the model's copies hold it (`measured-started-again` 9).
                if (shortName == "starts, T16")
                {
                    using (var reading = first.Read())
                    {
                        Assert.That(reading.State, Is.EqualTo(StoreState.Whole), "the accepted class: the earlier store served again");
                        Assert.That(StoreAdmissions.Admitted(reading, clients.Admission("c0").AdmissionKey), Is.Not.Null, "the accepted class: the earlier client admitted");
                        Assert.That(reading.Found.Select(key => key.Name), Is.EqualTo(new[] { keys["K"] }), "the accepted class: the new key found nowhere");
                    }
                }
            }
        }

        /// <summary>A step of a sequence of a store started again, played in this copy; whether it was played.</summary>
        private static bool PlayedStarting(
            MemoryStore store,
            MemoryStore[] copies,
            JObject sequence,
            JToken step,
            Clients clients,
            Dictionary<string, string> labels,
            Dictionary<string, string> authorities,
            Dictionary<string, string> byModelName,
            Dictionary<string, string> keys,
            ISet<string> edited,
            JObject gives,
            DateTimeOffset now,
            string where)
        {
            var act = (JArray)step["act"];
            var command = (string)step["command"];
            switch (command)
            {
                case "Synchronize":
                    var from = copies[(int)act[3]];
                    store.Synchronize(from);

                    // The newer version of an entry both copies hold is the version a Synchronize
                    // keeps, as the model's does: an entry edited past the limit in the other copy.
                    foreach (var hex in edited.Where(hex => from.Candidates.Any(candidate => candidate.Hex == hex)).ToList())
                    {
                        var newer = from.Candidates.First(candidate => candidate.Hex == hex);
                        store.Drop(hex);
                        store.Put(newer.Identifier, StringsOf(newer.Versions[0]));
                    }

                    return true;
                case "Start again":
                    return StartedAgain(store, where);
                case "Seal again":
                    return Sealed(store, where);
                case "Raise the format":
                    Raised(store);
                    return true;
                case "Delete by hand":
                    var deleted = StartTarget(store, step, clients, labels, authorities, byModelName, keys, where);
                    store.Drop(deleted);
                    if ((int)act[3] == 1)
                    {
                        store.Delete(StoreComposer.BytesOf(deleted));
                    }

                    return true;
                case "Edit past the limit":
                    var touched = StartTarget(store, step, clients, labels, authorities, byModelName, keys, where);
                    EditedPast(store, touched);
                    edited.Add(touched);
                    return true;
                case "Pair":
                    using (var reading = store.Read())
                    {
                        if (reading.State != StoreState.Whole)
                        {
                            return false;
                        }
                    }

                    return PlayedOver(store, sequence, step, act, command, clients, gives, now, where);
                case "Renew":
                    // The renewal's admission alone, as the model's renewal writes it.
                    using (var reading = store.Read())
                    {
                        var admission = clients.Admission((string)act[2]);
                        if (reading.State != StoreState.Whole
                            || StoreAdmissions.Admitted(reading, admission.AdmissionKey) == null
                            || StoredRenewals.Of(reading.InForce, admission.ClientKey).Refuses(now))
                        {
                            return false;
                        }
                    }

                    Paired(store, clients.Renewed((string)act[2], StoreSeeds.Served(store)), where);
                    return true;
                default:
                    return PlayedOver(store, sequence, step, act, command, clients, gives, now, where);
            }
        }

        /// <summary>
        /// Start store again pressed on the KPG tab's buffer, and the settings dialog's OK writing
        /// what it held; whether it was written.
        /// </summary>
        private static bool StartedAgain(MemoryStore store, string where)
        {
            var buffer = new PendingRecords(() => store);
            try
            {
                var pressed = Service(buffer).StartAgain();
                if (!pressed.Succeeded)
                {
                    return false;
                }

                var composed = buffer.Composed().Where(made => made != null).ToList();
                Assert.That(composed.Single().Starts, Is.True, where + ": the start held");
                var done = store.Write(composed);
                Assert.That(done.Taken, Is.True, where + ": " + done.Reason);
                return true;
            }
            finally
            {
                buffer.Discard();
            }
        }

        /// <summary>
        /// A later plugin's write raising the store's format: a version record of format 2 and a
        /// summary entry restating what is in force with the item `format` of 2, replacing every
        /// summary entry present with a deletion record, as the model's `newer` writes it.
        /// </summary>
        private static void Raised(MemoryStore store)
        {
            using (var reading = store.Read())
            {
                var inForce = reading.InForce;
                var key = (byte[])inForce.Key.Bytes.Clone();
                try
                {
                    var format = new Dictionary<string, string>(StringComparer.Ordinal) { { "format", "2" }, { "plugin", "0.4.0" } };
                    var items = inForce.Restated()
                        .Where(item => item.Kind != StoreKinds.Format)
                        .Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(item.Members.ToDictionary(member => member.Key, member => member.Value), StringComparer.Ordinal) { { "kind", item.Kind } })
                        .Concat(new[] { new Dictionary<string, string>(format, StringComparer.Ordinal) { { "kind", "format" } } })
                        .ToList();
                    var record = StoreRecord.Compose(key, StoreKinds.Format, format);
                    var head = StoreRecord.Compose(key, StoreKinds.Head, new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { "after", string.Join(" ", inForce.After.OrderBy(id => id, StringComparer.Ordinal)) },
                        { "height", inForce.Room.Height.ToString(CultureInfo.InvariantCulture) },
                    }, items);
                    foreach (var present in inForce.Heads)
                    {
                        store.Drop(present.Hex);
                        store.Delete(StoreComposer.BytesOf(present.Hex));
                    }

                    store.Put(record.Identifier, record.Strings);
                    store.Put(head.Identifier, head.Strings);
                }
                finally
                {
                    Array.Clear(key, 0, key.Length);
                }
            }
        }

        /// <summary>An entry edited past the history limit: its content followed by a space and no history.</summary>
        private static void EditedPast(MemoryStore store, string hex)
        {
            var entry = store.Candidates.Single(candidate => candidate.Hex == hex);
            var strings = StringsOf(entry.Versions[0]).ToDictionary(pair => pair.Key, pair => pair.Key == StoreComposer.Data ? pair.Value + " " : pair.Value, StringComparer.Ordinal);
            store.Drop(hex);
            store.Put(entry.Identifier, strings);
        }

        /// <summary>Every string of a version, by its name.</summary>
        private static Dictionary<string, string> StringsOf(StoreVersion version)
        {
            return version.Names
                .Select((stringName, at) => new KeyValuePair<string, string>(stringName, version.Strings.Read(version.Values[at])))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// The product's entry for the entry of the model's a step deleted or edited: an authority
        /// by the model's name under any key, the newest summary entry, a keep record by the key it
        /// names, and every other kind as the sequences of Seal again find it.
        /// </summary>
        private static string StartTarget(
            MemoryStore store,
            JToken step,
            Clients clients,
            Dictionary<string, string> labels,
            Dictionary<string, string> authorities,
            Dictionary<string, string> byModelName,
            Dictionary<string, string> keys,
            string where)
        {
            var touched = Touched(step);
            var what = (JArray)step["act"][2];
            switch ((string)touched["kind"])
            {
                case "authority":
                    var id = authorities[((string)touched["subject"]).Substring("authority:".Length)];
                    var found = store.Candidates.Where(candidate =>
                    {
                        var version = candidate.Versions[0];
                        var at = version.Names.ToList().IndexOf(StoreComposer.Kind);
                        var data = version.Names.ToList().IndexOf(StoreComposer.Data);
                        return at >= 0 && data >= 0 && version.Strings.Read(version.Values[at]) == "authority"
                               && (string)JObject.Parse(version.Strings.Read(version.Values[data]))["id"] == id;
                    }).ToList();
                    Assert.That(found, Has.Count.EqualTo(1), where + ": the entry of the authority " + (string)touched["subject"]);
                    return found[0].Hex;
                case "head" when what.Count > 1 && (string)what[1] == "new":
                    using (var reading = store.Read())
                    {
                        return reading.InForce.Heads.OrderByDescending(head => head.Form.Number).First().Hex;
                    }

                case "kept":
                    var named = keys[byModelName[((string)touched["subject"]).Substring("key:".Length)]];
                    using (var reading = store.Read())
                    {
                        return reading.InForce.Records.Single(record => record.Is(StoreKinds.Kept) && record.Form.Member(StoreKinds.Key) == named).Hex;
                    }

                default:
                    return Target(store, touched, clients, labels, where);
            }
        }

        /// <summary>The product's key that stands alone after a step, taken for the model's that does, where neither is named yet.</summary>
        private static void Keys(JToken step, MemoryStore store, Dictionary<string, string> keys)
        {
            var standing = ((JArray)step["keysRead"]["standing"]).Select(label => (string)label).ToList();
            if (standing.Count != 1 || keys.ContainsKey(standing[0]))
            {
                return;
            }

            using (var reading = store.Read())
            {
                if (reading.Standing.Count == 1 && !keys.ContainsValue(reading.Standing[0]))
                {
                    keys[standing[0]] = reading.Standing[0];
                }
            }
        }

        /// <summary>The model's records of a list that lie under the model's key of this label, by the tag they begin with.</summary>
        private static IEnumerable<JToken> Under(IEnumerable<JToken> items, byte[] modelKey)
        {
            var tags = new HashSet<string>(StoreKinds.All.Select(kind => KeePassGate.Hex.Of(StoreFormat.Tag(modelKey, kind.Name))), StringComparer.Ordinal);
            return items.Where(item => tags.Contains(((string)item["id"]).Substring(0, StoreFormat.TagLength * 2)));
        }

        /// <summary>A step of a sequence of a store started again, held to the model's file and answer after it.</summary>
        private static void AssertStarted(
            JToken step,
            JObject sequence,
            MemoryStore store,
            Clients clients,
            Dictionary<string, string> labels,
            Dictionary<string, string> authorities,
            Dictionary<string, byte[]> modelKeys,
            Dictionary<string, string> byModelName,
            Dictionary<string, string> keys,
            string where)
        {
            using (var reading = store.Read())
            {
                var answer = step["answer"];
                Assert.That(reading.State, Is.EqualTo(StartStates[(string)answer["status"]]), where + ": " + reading.Reason);

                // The keys found, standing and set aside, by the model's labels.
                var label = keys.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
                var read = (JObject)step["keysRead"];
                Assert.That(reading.Found.Select(key => Label(label, key.Name)).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(read["found"].Select(name => (string)name)), where + ": the keys found");
                Assert.That(reading.Standing.Select(name => Label(label, name)).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(read["standing"].Select(name => (string)name)), where + ": the keys that stand");
                Assert.That(
                    reading.Found.Select(key => key.Name).Except(reading.Standing).Select(name => Label(label, name)).OrderBy(name => name, StringComparer.Ordinal),
                    Is.EqualTo(read["aside"].Select(name => (string)name)),
                    where + ": the keys set aside");

                Assert.That(StoreAuthorities.Served(reading) != null, Is.EqualTo(answer["served"].Type != JTokenType.Null), where + ": whether an authority is served");
                Assert.That(reading.Answering.Count, Is.EqualTo(((JArray)answer["auths"]).Count), where + ": the authorities standing");
                var inForce = reading.InForce;
                if (inForce == null)
                {
                    Assert.That((JObject)answer["adm"], Is.Empty, where + ": nobody admitted where nothing is in force");
                    return;
                }

                // Under the key that stands: the records present by kind, the deletion records by
                // kind, and the summary entries.
                var modelKey = modelKeys[read["standing"].Select(name => (string)name).Single()];
                Assert.That(
                    StartKinds.ToDictionary(kind => kind, kind => inForce.Records.Count(record => record.Is(kind))),
                    Is.EquivalentTo(StartKinds.ToDictionary(kind => kind, kind => Under((JArray)step["present"], modelKey).Count(item => (string)item["kind"] == kind))),
                    where + ": the records present by kind");
                var tags = StoreKinds.All.ToDictionary(kind => inForce.Key.TagOf(kind), kind => kind.Name);
                Assert.That(
                    StartKinds.ToDictionary(kind => kind, kind => reading.Deletions.Count(deleted => tags.TryGetValue(StoreFormat.TagNumber(deleted), out var kindOf) && kindOf == kind)),
                    Is.EquivalentTo(StartKinds.ToDictionary(kind => kind, kind => Under((JArray)step["deletions"], modelKey).Count(item => (string)item["kind"] == kind))),
                    where + ": the deletion records by kind");
                var heads = inForce.Heads.OrderBy(head => head.Form.Number).ThenBy(head => Count(head.Form.Member("after"))).ToList();
                var modelHeads = Under((JArray)step["heads"], modelKey).OrderBy(head => (int)head["height"]).ThenBy(head => (int)head["after"]).ToList();
                Assert.That(
                    heads.Select(head => head.Form.Number + " " + Count(head.Form.Member("after"))),
                    Is.EqualTo(modelHeads.Select(head => (int)head["height"] + " " + (int)head["after"])),
                    where + ": each summary entry's height and names");
                var taking = new[] { StoreKinds.Revoked, StoreKinds.Retired, StoreKinds.Removed };
                for (var at = 0; at < heads.Count; at++)
                {
                    var items = heads[at].Form.Items.Where(item => item.Kind != StoreKinds.Format && !taking.Contains(item.Kind)).ToList();
                    var modelItems = ((JArray)modelHeads[at]["items"]).Cast<JObject>().Where(item => (string)item["kind"] != "format" && !taking.Contains((string)item["kind"])).ToList();
                    Assert.That(
                        items.GroupBy(item => item.Kind).ToDictionary(group => group.Key, group => group.Count()),
                        Is.EquivalentTo(modelItems.GroupBy(item => (string)item["kind"]).ToDictionary(group => group.Key, group => group.Count())),
                        where + ": the items of the summary entry of height " + heads[at].Form.Number);

                    // And of the kinds that take away, every value the reading can name, as the
                    // sequences of Seal again hold them.
                    var allItems = heads[at].Form.Items;
                    var allModel = ((JArray)modelHeads[at]["items"]).Cast<JObject>().ToList();
                    var modelTold = ModelTold(sequence);
                    foreach (var kind in taking)
                    {
                        var member = kind == StoreKinds.Revoked ? StoreKinds.ClientMember : kind == StoreKinds.Retired ? StoreKinds.AuthorityMember : StoreKinds.NameMember;
                        var ofKind = allItems.Where(item => item.Kind == kind).ToList();
                        var modelOfKind = allModel.Where(item => (string)item["kind"] == kind).ToList();
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

                // What is taken away under the key that stands.
                AssertTaken(
                    reading,
                    StoreKinds.Revoked,
                    inForce.Taken(StoreKinds.Revoked),
                    ((JArray)answer["refusals"]).Where(refusal => (string)refusal[0] == StoreKinds.Revoked).Select(refusal => ClientOf(clients, labels, (string)refusal[1])),
                    "the clients revoked",
                    where);
                AssertTaken(
                    reading,
                    StoreKinds.Retired,
                    inForce.Taken(StoreKinds.Retired),
                    ((JArray)answer["refusals"]).Where(refusal => (string)refusal[0] == StoreKinds.Retired).Select(refusal => AuthorityOf(authorities, (string)refusal[1])),
                    "the authorities retired",
                    where);

                // Who is admitted with which names, the names, what every node gives, each answer.
                var names = KeePassGate.Acl.ProfileNames.Of(reading).Names;
                Assert.That(names, Is.EqualTo(((JArray)answer["names"]).Select(profile => (string)profile)), where + ": the names on the list");
                var admitted = labels.Values.Distinct(StringComparer.Ordinal).Where(client => Known(clients, client))
                    .Select(client => (Label: client, Admitted: StoreAdmissions.Admitted(reading, clients.Admission(client).AdmissionKey)))
                    .Where(pair => pair.Admitted != null)
                    .ToDictionary(pair => pair.Label, pair => string.Join(" ", pair.Admitted.Profiles.Where(names.Contains).OrderBy(profile => profile, StringComparer.Ordinal)));
                Assert.That(
                    admitted,
                    Is.EquivalentTo(((JObject)answer["adm"]).Properties().ToDictionary(
                        client => labels[client.Name],
                        client => string.Join(" ", ((JArray)client.Value).Select(profile => (string)profile).OrderBy(profile => profile, StringComparer.Ordinal)))),
                    where + ": the clients admitted, with their names");

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

                // What a stopped store lost, the stops a start wrote among it.
                var losses = StoreLosses.Of(reading);
                var expected = (JObject)step["losses"];
                if (losses == null)
                {
                    Assert.That((JArray)expected["reasons"], Is.Empty, where + ": nothing lost");
                    return;
                }

                Assert.That(losses.Stops, Is.EqualTo(((JArray)expected["reasons"]).Select(reason => Stops[(string)reason])), where + ": every condition that holds");
                Assert.That(
                    losses.SetAside.Select(stop => Label(label, stop.Value)),
                    Is.EqualTo(((JArray)expected["setAsideBy"]).Select(stop => byModelName[(string)stop["key"]])),
                    where + ": the stops a start wrote, by the key each names");
                // Where no summary entry is in force the model's listing names no summary entry,
                // and the product's names those a deletion record tells of at the greatest height
                // of any summary entry of the store, `StoreLosses.Heads`, as the sequences of Seal
                // again hold it; under the key that stands, as everything here is held.
                var noneInForce = ((JArray)expected["reasons"]).Any(reason => (string)reason == "no summary entry of the store is in force");
                var told = Under((JArray)step["deletions"], modelKey).Where(item => (string)item["kind"] == StoreKinds.Head).Select(item => (int)item["number"]).ToList();
                var greatest = told
                    .Concat(modelHeads.Select(head => (int)head["height"]))
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
                Assert.That(losses.Damaged.Count, Is.EqualTo(((JArray)expected["damaged"]).Count), where + ": the entries that are damage");
            }
        }

        /// <summary>The model's label of a product's key, or the name itself where no label was taken for it.</summary>
        private static string Label(Dictionary<string, string> labels, string name)
        {
            string label;
            return labels.TryGetValue(name, out label) ? label : name;
        }
    }
}
