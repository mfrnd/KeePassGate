using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KeePassGate.Store
{
    internal sealed partial class StoreReading
    {
        /// <summary>
        /// The state of the store, worked out from the checked forms and the deletion records:
        /// which entries are records under each key found, which key stands, whether the store is
        /// of the plugin's format, and whether it is whole, `STORE.md` §621 to §624 and §649 to
        /// §651, in the order of §656.
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
        /// fourth, about a million steps at the ceiling of 32, a few milliseconds. Whether the
        /// store is whole: every record and every item once, through sets. So the reading grows
        /// with the store's entries and their history, and with no power of them.
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

                // Each entry's record under each key found: the first version, in order, that
                // passes under it, `STORE.md` §621 and §622.
                var records = new List<Record>[keys.Count];
                var damage = new List<string>[keys.Count];
                var passed = 0;
                for (var key = 0; key < keys.Count; key++)
                {
                    records[key] = new List<Record>();
                    damage[key] = new List<string>();
                }

                var known = keys
                    .Select(key => (ISet<string>)new HashSet<string>(
                        StoreKinds.All.Select(kind => Hex.Of(StoreFormat.Tag(key.Bytes, kind.Name))), StringComparer.Ordinal))
                    .ToList();

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
                                records[key].Add(new Record(candidate.Hex, forms[at][index], index > 0));
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

                var deleted = Deleted(deletions, known);
                var standing = Standing(keys, records, deleted);
                if (standing.Count > 1)
                {
                    return new Judgment(
                        StoreState.Split,
                        "records under " + standing.Count.ToString(CultureInfo.InvariantCulture)
                        + " store keys stand, and the operator has kept no store")
                    {
                        Passed = passed,
                    };
                }

                var store = standing[0];
                var judged = Store(keys[store], records[store], damage[store], deleted);
                judged.Damage = damage[store].Count;
                judged.Passed = passed;
                judged.ByHistory = records[store].Count(record => record.ByHistory);
                return judged;
            }

            /// <summary>
            /// The state of the store under the single key that stands: its format, and whether it
            /// is whole, `STORE.md` §651, §623 and §624.
            /// </summary>
            private static Judgment Store(FoundKey key, IReadOnlyList<Record> records, IReadOnlyList<string> damage, ISet<string> deleted)
            {
                var heads = records.Where(record => record.Is(StoreKinds.Head)).ToList();

                // A record of a kind the plugin does not know, an item of such a kind, or a
                // version of an object of such a type: outdated, with or without a version
                // record, `STORE.md` §621 and §658.
                var unknown = records.FirstOrDefault(record => record.Form.Unknown || record.Form.UnknownItem);
                if (unknown != null)
                {
                    return new Judgment(
                        StoreState.Outdated,
                        "the store holds a record of a kind this plugin does not know: " + Short(unknown.Hex));
                }

                var highest = Highest(key.Bytes, records, heads, deleted);
                if (highest > StoreFormat.Number)
                {
                    return new Judgment(
                        StoreState.Outdated,
                        "the store is of format " + highest.ToString(CultureInfo.InvariantCulture)
                        + ", and this plugin reads format " + StoreFormat.Number.ToString(CultureInfo.InvariantCulture));
                }

                var inForce = heads.Where(head => head.InForce(deleted)).ToList();
                if (inForce.Count == 0)
                {
                    return new Judgment(StoreState.Stopped, "no summary entry of the store is in force");
                }

                var replaced = new HashSet<string>(StringComparer.Ordinal);
                foreach (var head in inForce)
                {
                    replaced.Add(head.Hex);
                    replaced.UnionWith(After(head.Form.Member(StoreKinds.After)));
                }

                var summaryStart = Hex.Of(StoreFormat.Tag(key.Bytes, StoreKinds.Head))
                                   + Hex.Of(StoreFormat.Subject(key.Bytes, StoreKinds.StoreSubject));
                var lost = deleted.FirstOrDefault(identifier =>
                    identifier.StartsWith(summaryStart, StringComparison.Ordinal) && !replaced.Contains(identifier));
                if (lost != null)
                {
                    return new Judgment(
                        StoreState.Stopped, "a summary entry was deleted that nothing in force replaces: " + Short(lost));
                }

                if (damage.Count > 0)
                {
                    return new Judgment(
                        StoreState.Stopped,
                        damage.Count.ToString(CultureInfo.InvariantCulture)
                        + " entries of the store have no version that passes, the first " + Short(damage[0]));
                }

                var notRestated = NotRestated(records, heads, inForce, deleted);
                if (notRestated != null)
                {
                    return new Judgment(
                        StoreState.Stopped, "a record no summary entry restates: " + Short(notRestated.Hex));
                }

                return new Judgment(StoreState.Whole, null);
            }

            /// <summary>
            /// The first record that takes something away, or holds a plugin back, and that no
            /// summary entry restates as its row of `STORE.md` §658 has it, or null.
            /// </summary>
            /// <remarks>
            /// Every item is put into a set once, by what restates a record, so each record is a
            /// lookup whatever number of items the summary entries hold.
            /// </remarks>
            private static Record NotRestated(
                IReadOnlyList<Record> records, IReadOnlyList<Record> heads, IReadOnlyList<Record> inForce, ISet<string> deleted)
            {
                var restating = new HashSet<(string Kind, string Value, string Id)>();
                var formats = 0;
                foreach (var item in heads.SelectMany(head => head.Form.Items))
                {
                    var row = StoreKinds.Find(item.Kind);
                    switch (row.Restated)
                    {
                        case StoreRestated.BySameMember:
                            // A removal is restated by its name and its identifier both.
                            restating.Add(Restating(row.Name, item.Member(row.RestatedBy), row.Name == StoreKinds.Removed ? item.Member(StoreKinds.Id) : null));
                            break;
                        case StoreRestated.ById:
                            restating.Add(Restating(row.Name, item.Member(StoreKinds.Id), null));
                            break;
                        case StoreRestated.ByFormat:
                            formats = Math.Max(formats, StoreChecked.NumberOf(item.Member(StoreKinds.FormatMember)));
                            break;
                    }
                }

                // What a version in force names as replaced: a version record in force, and an item
                // of a summary entry in force.
                var replacedVersions = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in records.Where(record => record.Is(StoreKinds.Version) && record.InForce(deleted)))
                {
                    replacedVersions.UnionWith(After(record.Form.Member(StoreKinds.After)));
                }

                foreach (var item in inForce.SelectMany(head => head.Form.Items).Where(item => item.Kind == StoreKinds.Version))
                {
                    replacedVersions.UnionWith(After(item.Member(StoreKinds.After)));
                }

                foreach (var record in records.Where(record => record.Form.ReadsAsKind))
                {
                    var row = record.Form.Row;
                    switch (row.Restated)
                    {
                        case StoreRestated.BySameMember:
                            var restated = Restating(
                                row.Name, record.Form.Member(row.RestatedBy), row.Name == StoreKinds.Removed ? record.Hex : null);
                            if (!restating.Contains(restated))
                            {
                                return record;
                            }

                            break;

                        case StoreRestated.ById:
                            // A version out of force, or one a version in force replaces, is no
                            // standing version, and no summary entry carries it.
                            if (!row.CountsBesideDeletion
                                && (!record.InForce(deleted) || replacedVersions.Contains(record.Hex)))
                            {
                                break;
                            }

                            if (!restating.Contains(Restating(row.Name, record.Hex, null)))
                            {
                                return record;
                            }

                            break;

                        case StoreRestated.ByFormat:
                            if (formats < record.Form.Number)
                            {
                                return record;
                            }

                            break;
                    }
                }

                return null;
            }

            /// <summary>
            /// What restates a record, its members held apart: a member's value may hold any
            /// character, a line feed among them, so no text joined from them could tell two apart.
            /// </summary>
            private static (string Kind, string Value, string Id) Restating(string kind, string value, string id)
            {
                return (kind, value, id);
            }

            /// <summary>
            /// The highest format number the store states: a version record, the item `format` of
            /// a summary entry that is a record, or a deletion record that tells of a version
            /// record by its tag, a number from 1 and the subject hash of that number, `STORE.md`
            /// §623.
            /// </summary>
            private static int Highest(byte[] key, IReadOnlyList<Record> records, IReadOnlyList<Record> heads, ISet<string> deleted)
            {
                var highest = 0;
                foreach (var record in records.Where(record => record.Is(StoreKinds.Format)))
                {
                    highest = Math.Max(highest, record.Form.Number);
                }

                foreach (var item in heads.SelectMany(head => head.Form.Items).Where(item => item.Kind == StoreKinds.Format))
                {
                    highest = Math.Max(highest, StoreChecked.NumberOf(item.Member(StoreKinds.FormatMember)));
                }

                var tag = Hex.Of(StoreFormat.Tag(key, StoreKinds.Format));
                var numberAt = (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2;
                foreach (var identifier in deleted.Where(identifier => identifier.StartsWith(tag, StringComparison.Ordinal)))
                {
                    var number = int.Parse(identifier.Substring(numberAt, StoreFormat.HeightLength * 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (number < 1)
                    {
                        continue;
                    }

                    var subject = Hex.Of(StoreFormat.Subject(key, "format:" + number.ToString(CultureInfo.InvariantCulture)));
                    if (string.Equals(identifier.Substring(StoreFormat.TagLength * 2, StoreFormat.SubjectLength * 2), subject, StringComparison.Ordinal))
                    {
                        highest = Math.Max(highest, number);
                    }
                }

                return highest;
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
            private static IReadOnlyList<int> Standing(IReadOnlyList<FoundKey> keys, IReadOnlyList<Record>[] records, ISet<string> deleted)
            {
                if (keys.Count == 1)
                {
                    return new[] { 0 };
                }

                var relations = KeptRelations(keys, records).Concat(DeletedRelations(keys, deleted)).ToList();
                var named = new HashSet<string>(relations.SelectMany(relation => relation.After), StringComparer.Ordinal);
                var names = new bool[keys.Count, keys.Count];
                foreach (var relation in relations.Where(relation => relation.Target >= 0 && !named.Contains(relation.Id)))
                {
                    names[relation.Source, relation.Target] = true;
                }

                var marks = Marked(names);
                return Enumerable.Range(0, keys.Count).Where(key => marks[key] != Mark.Aside).ToList();
            }

            /// <summary>The keep records under each key found, and the items that restate them.</summary>
            private static IEnumerable<Relation> KeptRelations(IReadOnlyList<FoundKey> keys, IReadOnlyList<Record>[] records)
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

                for (var source = 0; source < keys.Count; source++)
                {
                    var tag = Hex.Of(StoreFormat.Tag(keys[source].Bytes, StoreKinds.Kept));
                    for (var target = 0; target < keys.Count; target++)
                    {
                        List<string> starting;
                        if (target == source
                            || !byStart.TryGetValue(tag + Hex.Of(StoreFormat.Subject(keys[source].Bytes, "key:" + keys[target].Name)), out starting))
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
            private static ISet<string> Deleted(IReadOnlyList<byte[]> deletions, IReadOnlyList<ISet<string>> known)
            {
                var tags = new HashSet<int>(known.SelectMany(set => set).Select(tag => Convert.ToInt32(tag, 16)));
                var deleted = new HashSet<string>(StringComparer.Ordinal);
                foreach (var identifier in deletions)
                {
                    if (identifier == null || identifier.Length != StoreFormat.TagLength + StoreFormat.SubjectLength + StoreFormat.HeightLength + StoreFormat.CheckLength)
                    {
                        continue;
                    }

                    if (tags.Contains((identifier[0] << 16) | (identifier[1] << 8) | identifier[2]))
                    {
                        deleted.Add(Hex.Of(identifier));
                    }
                }

                return deleted;
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

        /// <summary>An entry's record under a key: the version that passes, and whether it is from its history.</summary>
        private sealed class Record
        {
            public Record(string hex, StoreChecked form, bool byHistory)
            {
                Hex = hex;
                Form = form;
                ByHistory = byHistory;
            }

            public string Hex { get; private set; }

            public StoreChecked Form { get; private set; }

            public bool ByHistory { get; private set; }

            /// <summary>Whether it reads as a record of this kind.</summary>
            public bool Is(string kind)
            {
                return Form.ReadsAsKind && string.Equals(Form.Kind, kind, StringComparison.Ordinal);
            }

            /// <summary>
            /// Whether it is in force: a record of a kind that counts beside a deletion record
            /// naming it always, and any other only where no deletion record names it, as its row
            /// of `STORE.md` §658 has it.
            /// </summary>
            public bool InForce(ISet<string> deleted)
            {
                return (Form.Row != null && Form.Row.CountsBesideDeletion) || !deleted.Contains(Hex);
            }
        }
    }
}
