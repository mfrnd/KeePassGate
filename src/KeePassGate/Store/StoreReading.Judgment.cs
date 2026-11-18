using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KeePassGate.Store
{
    public sealed partial class StoreReading
    {
        /// <summary>
        /// The state of the store, worked out from the checked forms and the deletion records:
        /// which entries are records under each key found, which key stands, whether the store is
        /// of the plugin's format, whether it is whole, and whether it has room for its next
        /// write, `STORE.md` §621 to §624, §627 and §649 to §651, in the order of §656.
        /// </summary>
        /// <remarks>
        /// **No step follows a list.** `after` is read as the identifiers it names and nothing is
        /// looked up through them, so a list that names itself, or a circle of lists, reads.
        ///
        /// **What it costs, with K keys found, at most `StoreFormat.MaxKeys`.** The records: a
        /// lookup for each version of each candidate under each key, from the checked forms. The
        /// keys that stand: a name and a tag for each key and a subject hash for each pair, every
        /// keep relation and every deletion record looked at once, and the marking over K by K
        /// marks between keys, whatever number of keep records the file holds. The marking runs
        /// at most K rounds, each settling in at most K passes of K by K and asking each unmarked
        /// key whether a circle leads back to it, a search of K by K: of the order of K to the
        /// fourth, about a million steps at the ceiling of 32, a few milliseconds. What is in
        /// force, whether the store is whole and the room it has: every record and every item
        /// once, through sets, `StoreInForce`. So the reading grows with the store's entries and
        /// their history, and with no power of them.
        /// </remarks>
        private sealed class Judgment
        {
            private Judgment(StoreState state, string reason)
            {
                State = state;
                Reason = reason;
            }

            public StoreState State { get; private set; }

            public string Reason { get; private set; }

            public int Damage { get; private set; }

            public int Passed { get; private set; }

            public int ByHistory { get; private set; }

            /// <summary>Whether anything was judged, which past the ceiling on keys nothing is.</summary>
            public bool Judged { get; private set; } = true;

            /// <summary>What is in force, for a store that is whole or stopped.</summary>
            public StoreInForce InForce { get; private set; }

            /// <summary>The authorities a word is answered under, `STORE.md` §656.</summary>
            public IReadOnlyList<FoundRecord> Answering { get; private set; } = Array.Empty<FoundRecord>();

            /// <summary>
            /// Whether a retirement under a key that stands names an authority, by its `id`: none
            /// where no key is judged.
            /// </summary>
            public Func<string, bool> Retires { get; private set; } = id => false;

            /// <summary>
            /// A store with more keys than the reading works under: split, with no key judged,
            /// `StoreFormat.MaxKeys`.
            /// </summary>
            /// <remarks>
            /// Its reason names what the operator does about it, which is not what ends an ordinary
            /// split: keeping a store judges keys, and past the ceiling none is, so the entries that
            /// brought those keys are deleted first, `STORE.md` §649.
            /// </remarks>
            public static Judgment Beyond(int keys, int hidden)
            {
                return new Judgment(
                    StoreState.Split,
                    "the database holds " + keys.ToString(CultureInfo.InvariantCulture)
                    + " store keys, more than a store holds, so nothing of the store is judged and all "
                    + hidden.ToString(CultureInfo.InvariantCulture)
                    + " entries that carry a string of the store are hidden; the entries that brought those"
                    + " keys have to be deleted before the store is judged again")
                {
                    Judged = false,
                };
            }

            public static Judgment Of(
                IReadOnlyList<StoreCandidate> candidates,
                IReadOnlyList<StoreChecked[]> forms,
                IReadOnlyList<FoundKey> keys,
                IReadOnlyList<byte[]> deletions)
            {
                if (keys.Count == 0)
                {
                    return new Judgment(StoreState.None, "the database holds no store key")
                    {
                        Passed = candidates.Count,
                    };
                }

                // The tags of every kind under each key found, which each key keeps once made.
                var known = keys
                    .Select(key => (ISet<string>)new HashSet<string>(
                        key.Tags.Select(tag => tag.ToString("x6", CultureInfo.InvariantCulture)), StringComparer.Ordinal))
                    .ToList();
                var deleted = Deleted(deletions, keys);

                // Each entry's record under each key found: the first version, in order, that
                // passes under it, `STORE.md` §621 and §622.
                var records = new List<FoundRecord>[keys.Count];
                var damage = new List<string>[keys.Count];
                var passed = 0;
                for (var key = 0; key < keys.Count; key++)
                {
                    records[key] = new List<FoundRecord>();
                    damage[key] = new List<string>();
                }

                for (var at = 0; at < candidates.Count; at++)
                {
                    var candidate = candidates[at];
                    var any = false;
                    for (var key = 0; key < keys.Count; key++)
                    {
                        for (var index = 0; index < forms[at].Length; index++)
                        {
                            if (forms[at][index].Passes(keys[key], candidate.Identifier))
                            {
                                records[key].Add(new FoundRecord(candidate.Hex, forms[at][index], index, deleted.Contains(candidate.Hex)));
                                any = true;
                                break;
                            }
                        }
                    }

                    if (any)
                    {
                        continue;
                    }

                    var tag = candidate.Hex.Substring(0, StoreFormat.TagLength * 2);
                    var tagged = false;
                    for (var key = 0; key < keys.Count; key++)
                    {
                        if (known[key].Contains(tag))
                        {
                            damage[key].Add(candidate.Hex);
                            tagged = true;
                        }
                    }

                    if (!tagged)
                    {
                        passed++;
                    }
                }

                var kept = keys.Count == 1 ? new List<Relation>() : KeptRelations(keys, records).ToList();
                var told = keys.Count == 1 ? new List<Relation>() : DeletedRelations(keys, deleted).ToList();
                var standing = Standing(keys.Count, kept.Concat(told).ToList());

                // The deletion records by their tags, once for every key, `STORE.md` §649.
                var byTag = ByTag(deleted);
                if (standing.Count > 1)
                {
                    // A word is answered under each authority that stands under each key that
                    // stands, `STORE.md` §656: the authorities that stand under each key are worked
                    // out for that, and nothing else of what is in force, so no value of a record
                    // under another key is hashed but the `id` of an authority present.
                    return new Judgment(
                        StoreState.Split,
                        "records under " + standing.Count.ToString(CultureInfo.InvariantCulture)
                        + " store keys stand, and the operator has kept no store")
                    {
                        Passed = passed,
                        Answering = standing
                            .SelectMany(key => StoreInForce.Standing(keys[key], records[key], byTag))
                            .ToList(),
                        Retires = id => standing.Any(key => StoreInForce.Retired(keys[key], records[key], byTag, new[] { id }).Contains(id)),
                    };
                }

                var store = standing[0];
                var keptByDeletion = told.Where(relation => relation.Source == store)
                    .Select(relation => new KeyValuePair<string, string>(relation.Id, keys[relation.Target].Name))
                    .ToList();
                var judged = Store(keys[store], records[store], damage[store], byTag, keptByDeletion);
                judged.Damage = damage[store].Count;
                judged.Passed = passed;
                judged.ByHistory = records[store].Count(record => record.ByHistory);
                judged.Retires = id => StoreInForce.Retired(keys[store], records[store], byTag, new[] { id }).Contains(id);
                return judged;
            }

            /// <summary>
            /// The state of the store under the single key that stands: its format, whether it is
            /// whole, and whether it has room, `STORE.md` §651, §623, §624 and §627.
            /// </summary>
            private static Judgment Store(
                FoundKey key,
                IReadOnlyList<FoundRecord> records,
                IReadOnlyList<string> damage,
                IReadOnlyDictionary<string, List<string>> byTag,
                IReadOnlyCollection<KeyValuePair<string, string>> keptByDeletion)
            {
                var inForce = StoreInForce.Of(key, records, byTag, keptByDeletion);

                // A record of a kind the plugin does not know, an item of such a kind, or a
                // version of an object of such a type: outdated, with or without a version
                // record, `STORE.md` §621 and §658.
                var unknown = records.FirstOrDefault(record => record.Form.Unknown || record.Form.UnknownItem);
                if (unknown != null)
                {
                    return Outdated(
                        "the store holds a record of a kind this plugin does not know: " + Short(unknown.Hex), inForce);
                }

                if (inForce.Format > StoreFormat.Number)
                {
                    return Outdated(
                        "the store is of format " + inForce.Format.ToString(CultureInfo.InvariantCulture)
                        + ", and this plugin reads format " + StoreFormat.Number.ToString(CultureInfo.InvariantCulture),
                        inForce);
                }

                if (inForce.HeadsInForce.Count == 0)
                {
                    return Stopped("no summary entry of the store is in force", inForce);
                }

                if (inForce.Lost != null)
                {
                    return Stopped("a summary entry was deleted that nothing in force replaces: " + Short(inForce.Lost), inForce);
                }

                if (damage.Count > 0)
                {
                    return Stopped(
                        damage.Count.ToString(CultureInfo.InvariantCulture)
                        + " entries of the store have no version that passes, the first " + Short(damage[0]),
                        inForce);
                }

                var notRestated = inForce.NotRestated();
                if (notRestated != null)
                {
                    return Stopped("a record no summary entry restates: " + Short(notRestated.Hex), inForce);
                }

                // Last, since a store with room is otherwise whole: answered as a stopped store,
                // and lifted by nothing but a store started again, `STORE.md` §624 and §643.
                if (!inForce.Room.HasRoom)
                {
                    return Stopped(inForce.Room.Reason, inForce);
                }

                return new Judgment(StoreState.Whole, null) { InForce = inForce, Answering = inForce.Authorities };
            }

            /// <summary>
            /// A store of a form the plugin does not read: answered under the authorities that
            /// stand, and nothing of it handed out as in force.
            /// </summary>
            private static Judgment Outdated(string reason, StoreInForce inForce)
            {
                return new Judgment(StoreState.Outdated, reason) { Answering = inForce.Authorities };
            }

            private static Judgment Stopped(string reason, StoreInForce inForce)
            {
                return new Judgment(StoreState.Stopped, reason) { InForce = inForce, Answering = inForce.Authorities };
            }

            /// <summary>
            /// Which of the keys found stand, `STORE.md` §649: by the keep records, their items
            /// and the deletion records that tell of one, less every one another names in `after`.
            /// </summary>
            /// <remarks>
            /// What decides is only which key names which, so the relations that count are made
            /// into K by K marks first, however many keep records the file holds, and the marking
            /// runs over those.
            /// </remarks>
            private static IReadOnlyList<int> Standing(int keys, IReadOnlyList<Relation> relations)
            {
                if (keys == 1)
                {
                    return new[] { 0 };
                }

                var named = new HashSet<string>(relations.SelectMany(relation => relation.After), StringComparer.Ordinal);
                var names = new bool[keys, keys];
                foreach (var relation in relations.Where(relation => relation.Target >= 0 && !named.Contains(relation.Id)))
                {
                    names[relation.Source, relation.Target] = true;
                }

                var marks = Marked(names);
                return Enumerable.Range(0, keys).Where(key => marks[key] != Mark.Aside).ToList();
            }

            /// <summary>The keep records under each key found, and the items that restate them.</summary>
            private static IEnumerable<Relation> KeptRelations(IReadOnlyList<FoundKey> keys, IReadOnlyList<FoundRecord>[] records)
            {
                var byName = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var key = 0; key < keys.Count; key++)
                {
                    byName[keys[key].Name] = key;
                }

                for (var source = 0; source < keys.Count; source++)
                {
                    foreach (var record in records[source].Where(record => record.Is(StoreKinds.Kept)))
                    {
                        yield return new Relation(
                            source, Target(byName, record.Form.Member(StoreKinds.Key)), record.Hex, After(record.Form.Member(StoreKinds.After)));
                    }

                    foreach (var item in records[source]
                                 .Where(record => record.Is(StoreKinds.Head))
                                 .SelectMany(head => head.Form.Items)
                                 .Where(item => item.Kind == StoreKinds.Kept))
                    {
                        yield return new Relation(
                            source, Target(byName, item.Member(StoreKinds.Key)), item.Member(StoreKinds.Id), After(item.Member(StoreKinds.After)));
                    }
                }
            }

            /// <summary>
            /// The deletion records of keep records, which name nothing in `after`, `STORE.md`
            /// §623: by the first eight bytes of each identifier, the tag under the key it was
            /// under and the subject hash of the key it names, looked up once for each pair.
            /// </summary>
            private static IEnumerable<Relation> DeletedRelations(IReadOnlyList<FoundKey> keys, ISet<string> deleted)
            {
                var byStart = deleted
                    .GroupBy(identifier => identifier.Substring(0, (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

                var keptKind = StoreKinds.Find(StoreKinds.Kept);
                for (var source = 0; source < keys.Count; source++)
                {
                    var tag = keys[source].TagOf(keptKind);
                    for (var target = 0; target < keys.Count; target++)
                    {
                        List<string> starting;
                        if (target == source
                            || !byStart.TryGetValue(tag + keys[source].Subject("key:" + keys[target].Name), out starting))
                        {
                            continue;
                        }

                        foreach (var identifier in starting)
                        {
                            yield return new Relation(source, target, identifier, Array.Empty<string>());
                        }
                    }
                }
            }

            /// <summary>
            /// The marks of the keys under these relations between them: the two rules until
            /// nothing changes, then every circle among the keys left standing, and again, until
            /// a round marks nothing.
            /// </summary>
            private static Mark[] Marked(bool[,] names)
            {
                var count = names.GetLength(0);
                var marks = new Mark[count];
                while (true)
                {
                    Settle(marks, names);
                    if (!marks.Contains(Mark.None))
                    {
                        return marks;
                    }

                    // Keys whose relations lead back to them among the keys still unmarked: a
                    // circle, in which no key sets another aside, so all of them stand.
                    var circled = Enumerable.Range(0, count)
                        .Where(key => marks[key] == Mark.None && Circles(key, marks, names))
                        .ToList();
                    if (circled.Count == 0)
                    {
                        return marks;
                    }

                    foreach (var key in circled)
                    {
                        marks[key] = Mark.Standing;
                    }
                }
            }

            private static int Target(IReadOnlyDictionary<string, int> byName, string name)
            {
                int target;
                return name != null && byName.TryGetValue(name, out target) ? target : -1;
            }

            /// <summary>
            /// The two rules of `STORE.md` §649 until nothing changes: a key that no relation
            /// names, or that only relations under keys set aside name, stands; a key that a
            /// relation under a standing key names is set aside.
            /// </summary>
            private static void Settle(Mark[] marks, bool[,] names)
            {
                var count = marks.Length;
                var changed = true;
                while (changed)
                {
                    changed = false;
                    for (var key = 0; key < count; key++)
                    {
                        if (marks[key] != Mark.None)
                        {
                            continue;
                        }

                        var aside = false;
                        var standing = true;
                        for (var source = 0; source < count; source++)
                        {
                            if (!names[source, key])
                            {
                                continue;
                            }

                            aside |= marks[source] == Mark.Standing;
                            standing &= marks[source] == Mark.Aside;
                        }

                        if (aside)
                        {
                            marks[key] = Mark.Aside;
                            changed = true;
                        }
                        else if (standing)
                        {
                            marks[key] = Mark.Standing;
                            changed = true;
                        }
                    }
                }
            }

            /// <summary>Whether a chain of relations among unmarked keys leads from this key back to it.</summary>
            private static bool Circles(int key, Mark[] marks, bool[,] names)
            {
                var count = marks.Length;
                var reached = new bool[count];
                var next = new Queue<int>();
                next.Enqueue(key);
                while (next.Count > 0)
                {
                    var from = next.Dequeue();
                    for (var target = 0; target < count; target++)
                    {
                        if (!names[from, target] || marks[target] != Mark.None)
                        {
                            continue;
                        }

                        if (target == key)
                        {
                            return true;
                        }

                        if (!reached[target])
                        {
                            reached[target] = true;
                            next.Enqueue(target);
                        }
                    }
                }

                return false;
            }

            /// <summary>
            /// The identifiers the deletion records name whose first three bytes are a tag of a
            /// kind under a key found: every deletion record that can name a record of the store,
            /// since a record's identifier begins with its tag.
            /// </summary>
            private static ISet<string> Deleted(IReadOnlyList<byte[]> deletions, IReadOnlyList<FoundKey> keys)
            {
                var tags = new HashSet<int>(keys.SelectMany(key => key.Tags));
                var deleted = new HashSet<string>(StringComparer.Ordinal);
                foreach (var identifier in deletions)
                {
                    if (identifier == null || identifier.Length != StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength + StoreFormat.CheckLength)
                    {
                        continue;
                    }

                    if (tags.Contains(StoreFormat.TagNumber(identifier)))
                    {
                        deleted.Add(Hex.Of(identifier));
                    }
                }

                return deleted;
            }

            /// <summary>
            /// The identifiers the deletion records name, by the tag each begins with, as
            /// lowercase hexadecimal: sorted once, for every key the reading judges.
            /// </summary>
            private static Dictionary<string, List<string>> ByTag(IEnumerable<string> deleted)
            {
                var byTag = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var identifier in deleted)
                {
                    List<string> tagged;
                    var tag = identifier.Substring(0, StoreFormat.TagLength * 2);
                    if (!byTag.TryGetValue(tag, out tagged))
                    {
                        tagged = new List<string>();
                        byTag[tag] = tagged;
                    }

                    tagged.Add(identifier);
                }

                return byTag;
            }

            private static IReadOnlyList<string> After(string list)
            {
                return StoreFormat.Identifiers(list) ?? Array.Empty<string>();
            }

            private static string Short(string hex)
            {
                return hex == null || hex.Length < 8 ? hex : hex.Substring(0, 8);
            }

            private enum Mark
            {
                None,
                Standing,
                Aside,
            }

            /// <summary>A keep relation: under one key, naming another, `STORE.md` §649.</summary>
            private sealed class Relation
            {
                public Relation(int source, int target, string id, IReadOnlyList<string> after)
                {
                    Source = source;
                    Target = target;
                    Id = id;
                    After = after;
                }

                public int Source { get; private set; }

                /// <summary>The key named, or -1 for a name no key found has.</summary>
                public int Target { get; private set; }

                public string Id { get; private set; }

                public IReadOnlyList<string> After { get; private set; }
            }
        }
    }
}
