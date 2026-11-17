using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// The plan of a write of the plugin's own into the store, `STORE.md` §627: refused, or what
    /// to add, what to delete with a deletion record, which deletion records to add for entries
    /// the file does not hold, which records to set back, and the acts left out.
    /// </summary>
    /// <remarks>
    /// **Pure and total.** It reads the store as one reading found it and touches no database;
    /// applying it is the caller's, `StoreEntries` in a database and <see cref="Applied"/> in
    /// memory. Nothing a file holds makes it throw: what does not fit is refused.
    ///
    /// **What it restates is what the room counted.** The summary entry it writes names
    /// `StoreInForce.After` and holds `StoreInForce.Restated`, the very sets the room of a store
    /// is measured from, with the acts' own items beside them, so a store judged to have room is
    /// never refused a write that adds an item at most, and a store judged to have none is
    /// stopped before a plan is asked.
    /// </remarks>
    internal sealed class StoreWrite
    {
        private StoreWrite()
        {
            Add = Array.Empty<StoreRecord>();
            Delete = Array.Empty<string>();
            Deletions = Array.Empty<string>();
            SetBack = Array.Empty<KeyValuePair<string, int>>();
            LeftOut = Array.Empty<string>();
            Taken = Array.Empty<StoreAct>();
        }

        /// <summary>Why nothing is written, or null for a write that is made.</summary>
        public string Refused { get; private set; }

        /// <summary>The word of `STORE.md` §656 where the store's state refused the write, or null.</summary>
        public string Word { get; private set; }

        /// <summary>
        /// Whether the acts taken cancel out, and nothing is left to write: an authority made and
        /// retired again among them. Nothing is applied, and nothing is refused.
        /// </summary>
        public bool Empty { get; private set; }

        /// <summary>Whether the write starts a store, under the key its first act brings, `STORE.md` §632.</summary>
        public bool Starts { get; private set; }

        /// <summary>The records to add, the summary entry last.</summary>
        public IReadOnlyList<StoreRecord> Add { get; private set; }

        /// <summary>The entries present to delete, each with a deletion record where the file holds none.</summary>
        public IReadOnlyList<string> Delete { get; private set; }

        /// <summary>The identifiers of entries the file does not hold to add a deletion record for.</summary>
        public IReadOnlyList<string> Deletions { get; private set; }

        /// <summary>The records that count by their history, each with the version that passes, `STORE.md` §628.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> SetBack { get; private set; }

        /// <summary>The acts taken.</summary>
        public IReadOnlyList<StoreAct> Taken { get; private set; }

        /// <summary>Each act left out, by its name and why its ground went.</summary>
        public IReadOnlyList<string> LeftOut { get; private set; }

        /// <summary>The plan of writing these acts into the store as this reading found it.</summary>
        /// <param name="plugin">The plugin's own version, three whole numbers joined by periods.</param>
        public static StoreWrite Plan(StoreReading reading, IReadOnlyList<StoreAct> acts, string plugin)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            if (acts == null || acts.Count == 0)
            {
                return Refusal("there is nothing to write");
            }

            // A plugin that cannot state its version writes nothing, `STORE.md` §650.
            if (plugin == null || !StoreChecked.InForm(plugin, StoreForm.Version))
            {
                return Refusal("this plugin cannot state its version, and writes nothing into a store");
            }

            // Step 1, the state, `STORE.md` §627: a store started where no key stands, and a
            // whole store; every other state is refused with its word, Create among them.
            var inForce = reading.InForce;
            var starts = reading.State == StoreState.None;
            if (!starts && (reading.State != StoreState.Whole || inForce == null))
            {
                var word = StoreWords.Of(reading.State) ?? StoreWords.Stopped;
                return new StoreWrite
                {
                    Refused = "the store is " + word + ": " + reading.Reason,
                    Word = word,
                };
            }

            // Step 2, the ground of each act, against the store with the acts before it taken.
            var ground = new StoreGround(
                starts ? null : inForce.Key.Name,
                starts ? Enumerable.Empty<string>() : inForce.Authorities.Select(record => record.Form.Member(StoreKinds.Id)));
            var taken = new List<StoreAct>();
            var leftOut = new List<string>();
            foreach (var act in acts)
            {
                var why = act.Why(ground);
                if (why == null && ground.KeyName != null && !string.Equals(act.KeyName, ground.KeyName, StringComparison.Ordinal))
                {
                    why = "it was composed under a store key that does not stand";
                }

                if (why == null && ground.KeyName == null && !act.Records.Any(record => record.Kind == StoreKinds.Authority))
                {
                    why = "no store key stands, and only an authority starts a store";
                }

                if (why != null)
                {
                    leftOut.Add(act.Name + ": " + why);
                    continue;
                }

                taken.Add(act);
                ground = ground.With(act);
            }

            if (taken.Count == 0)
            {
                return Refusal(acts.Count == 1 ? leftOut[0] : "no act applies to the store as it stands", leftOut);
            }

            var key = starts ? taken[0].Key() : (byte[])inForce.Key.Bytes.Clone();
            try
            {
                return Planned(reading, inForce, starts, key, taken, leftOut, plugin);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// The candidates and the deletion records a database would hold with this write applied,
        /// in memory: what a write not yet made is read as, and what the tests apply.
        /// </summary>
        public KeyValuePair<IReadOnlyList<StoreCandidate>, IReadOnlyList<byte[]>> Applied(
            IReadOnlyList<StoreCandidate> candidates, IReadOnlyList<byte[]> deletions)
        {
            var deleted = new HashSet<string>(Delete, StringComparer.Ordinal);
            var setBack = SetBack.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var held = new HashSet<string>((deletions ?? Array.Empty<byte[]>()).Select(Hex.Of), StringComparer.Ordinal);
            var nextDeletions = new List<byte[]>(deletions ?? Array.Empty<byte[]>());
            var next = new List<StoreCandidate>();

            foreach (var candidate in candidates ?? Array.Empty<StoreCandidate>())
            {
                if (deleted.Contains(candidate.Hex))
                {
                    if (held.Add(candidate.Hex))
                    {
                        nextDeletions.Add(candidate.Identifier);
                    }

                    continue;
                }

                int index;
                if (setBack.TryGetValue(candidate.Hex, out index) && index > 0 && index < candidate.Versions.Count)
                {
                    // The version that passes made current again, and the edited version the
                    // newest item of the history, as KeePass's own backup puts it there.
                    var versions = new List<StoreVersion> { candidate.Versions[index] };
                    versions.AddRange(candidate.Versions);
                    next.Add(new StoreCandidate(candidate.Identifier, versions));
                    continue;
                }

                next.Add(candidate);
            }

            nextDeletions.AddRange(Deletions.Where(held.Add).Select(Bytes));

            foreach (var record in Add)
            {
                next.Add(new StoreCandidate(record.Identifier, new[] { record.Version() }));
            }

            return new KeyValuePair<IReadOnlyList<StoreCandidate>, IReadOnlyList<byte[]>>(next, nextDeletions);
        }

        /// <summary>Steps 3 to 8 of `STORE.md` §627, once the acts taken are known.</summary>
        private static StoreWrite Planned(
            StoreReading reading,
            StoreInForce inForce,
            bool starts,
            byte[] key,
            IReadOnlyList<StoreAct> taken,
            IReadOnlyList<string> leftOut,
            string plugin)
        {
            var added = taken.SelectMany(act => act.Records).ToList();

            // An authority this write adds and retires again reaches no database, and neither does
            // its retirement, `STORE.md` §627: an authority made on a dialog and retired on it.
            var made = new HashSet<string>(
                added.Where(record => record.Kind == StoreKinds.Authority).Select(record => record.Member(StoreKinds.Id)), StringComparer.Ordinal);
            var unmade = new HashSet<string>(
                added.Where(record => record.Kind == StoreKinds.Retired && made.Contains(record.Member(StoreKinds.AuthorityMember)))
                    .Select(record => record.Member(StoreKinds.AuthorityMember)),
                StringComparer.Ordinal);
            added.RemoveAll(record =>
                (record.Kind == StoreKinds.Authority && unmade.Contains(record.Member(StoreKinds.Id)))
                || (record.Kind == StoreKinds.Retired && unmade.Contains(record.Member(StoreKinds.AuthorityMember))));

            // A record is written once: a record the file holds already, a record a deletion
            // record names, and a record an act before it in this write adds, are not added again.
            // The identifier is
            // the record's own, and a second entry under it would be two entries the tree tells
            // apart by nothing.
            var once = new HashSet<string>(reading.Candidates.Select(candidate => candidate.Hex), StringComparer.Ordinal);
            once.UnionWith(reading.Deletions.Select(Hex.Of));
            added.RemoveAll(record => !once.Add(record.Hex));
            if (added.Count == 0)
            {
                return new StoreWrite { Empty = true, Taken = taken, LeftOut = leftOut };
            }

            // What the retirements of the store and of this write take away, `STORE.md` §614.
            var retired = new HashSet<string>(
                starts ? Enumerable.Empty<string>() : inForce.Taken(StoreKinds.Retired), StringComparer.Ordinal);
            retired.UnionWith(added.Where(record => record.Kind == StoreKinds.Retired).Select(record => record.Member(StoreKinds.AuthorityMember)));

            // Step 3, the version record, `STORE.md` §650: with the store's first write, and where
            // the plugin's version is above the highest the store states.
            var format = starts ? 0 : inForce.Format;
            var stated = starts ? null : inForce.Plugin;
            if (starts || StoreInForce.Above(plugin, stated))
            {
                added.Insert(0, StoreRecord.Compose(key, StoreKinds.Format, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.FormatMember, StoreFormat.Number.ToString(CultureInfo.InvariantCulture) },
                    { StoreKinds.PluginMember, plugin },
                }));
                format = Math.Max(format, StoreFormat.Number);
                stated = plugin;
            }

            // Step 4, what is deleted with a deletion record: every authority a retirement names,
            // and every summary entry present.
            var delete = new List<string>();
            if (!starts)
            {
                delete.AddRange(inForce.Records
                    .Where(record => (record.Is(StoreKinds.Authority) && retired.Contains(record.Form.Member(StoreKinds.Id)))
                                     || record.Is(StoreKinds.Head))
                    .Select(record => record.Hex));
            }

            // A write never takes the store key out of the file: an entry holding it stays, or the
            // write brings an entry holding it, as Delete's retirement holds the key and Recreate's authority does,
            // `STORE.md` §632. A write that would leave none would start the store again by
            // accident, every rule and name written under the key passed over.
            var deleting = new HashSet<string>(delete, StringComparer.Ordinal);
            var carried = added.Any(record => record.Protected)
                          || (!starts && inForce.Records.Any(record => record.Form.StoreKey != null
                                                         && record.Form.StoreKey.Name == inForce.Key.Name
                                                         && !deleting.Contains(record.Hex)));
            if (!carried)
            {
                return Refusal("the write would leave no entry holding the store key", leftOut);
            }

            // Step 5, a deletion record for every summary entry named and not in the file, so that
            // a merge bringing it back finds it out of force.
            var deletions = new List<string>();
            if (!starts)
            {
                var present = new HashSet<string>(reading.Candidates.Select(candidate => candidate.Hex), StringComparer.Ordinal);
                var held = new HashSet<string>(reading.Deletions.Select(Hex.Of), StringComparer.Ordinal);
                deletions.AddRange(inForce.After
                    .Where(identifier => !present.Contains(identifier) && !held.Contains(identifier))
                    .OrderBy(identifier => identifier, StringComparer.Ordinal));
            }

            // Step 6, the summary entry, `STORE.md` §611.
            var items = new List<IReadOnlyDictionary<string, string>>
            {
                Item(StoreKinds.Format, StoreKinds.FormatMember, format.ToString(CultureInfo.InvariantCulture), StoreKinds.PluginMember, stated),
            };

            if (!starts)
            {
                items.AddRange(inForce.Restated().Where(item => item.Kind != StoreKinds.Format).Select(Members));
            }

            items.AddRange(added.Select(Restating).Where(item => item != null));

            // An item restating what is restated already is the same item, and a summary entry
            // holds it once.
            items = items.GroupBy(Canonical, StringComparer.Ordinal).Select(group => group.First()).ToList();

            // Step 7, the plugin's own ceilings, `STORE.md` §643: refused whole, before anything
            // is added.
            if (items.Count > StoreFormat.MaxItems)
            {
                return Refusal(string.Format(
                    CultureInfo.InvariantCulture,
                    "the write needs {0} items in its summary entry, and a summary entry the plugin writes has names for {1}",
                    items.Count,
                    StoreFormat.MaxItems), leftOut);
            }

            var height = starts ? 1 : inForce.Room.Height;
            var after = starts ? string.Empty : string.Join(" ", inForce.After.OrderBy(identifier => identifier, StringComparer.Ordinal));
            StoreRecord head;
            try
            {
                head = StoreRecord.Compose(key, StoreKinds.Head, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.After, after },
                    { StoreKinds.Height, height.ToString(CultureInfo.InvariantCulture) },
                }, items);
            }
            catch (ArgumentException beyond)
            {
                // Beyond what the reader takes, which a store with room never reaches.
                return Refusal(beyond.Message, leftOut);
            }

            added.Add(head);

            var longest = added.SelectMany(record => record.Strings.Values).Max(value => value.Length);
            if (longest > StoreFormat.MaxWritten)
            {
                return Refusal(string.Format(
                    CultureInfo.InvariantCulture,
                    "the write needs a string of {0} characters, and the plugin writes none longer than {1}",
                    longest,
                    StoreFormat.MaxWritten), leftOut);
            }

            // Step 8, every record that counts only by its history is set back, `STORE.md` §628.
            var setBack = starts
                ? new List<KeyValuePair<string, int>>()
                : inForce.Records
                    .Where(record => record.ByHistory && !deleting.Contains(record.Hex))
                    .Select(record => new KeyValuePair<string, int>(record.Hex, record.Index))
                    .ToList();

            return new StoreWrite
            {
                Starts = starts,
                Add = added,
                Delete = delete,
                Deletions = deletions,
                SetBack = setBack,
                Taken = taken,
                LeftOut = leftOut,
            };
        }

        /// <summary>
        /// The item that restates a record a write adds, `STORE.md` §611 and §658, or null for a
        /// record no summary entry restates. A version record is restated by the item `format`.
        /// </summary>
        private static IReadOnlyDictionary<string, string> Restating(StoreRecord record)
        {
            var row = StoreKinds.Find(record.Kind);
            switch (row.Restated)
            {
                case StoreRestated.BySameMember:
                    var item = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.ItemKind, row.Name },
                        { row.RestatedBy, record.Member(row.RestatedBy) },
                    };
                    if (row.Name == StoreKinds.Removed)
                    {
                        item[StoreKinds.Id] = record.Hex;
                    }

                    return item;

                case StoreRestated.ById:
                case StoreRestated.ByStatement:
                    var carried = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.ItemKind, row.Name },
                        { StoreKinds.Id, record.Hex },
                    };
                    foreach (var member in row.Item.Where(member => member.Name != StoreKinds.Id))
                    {
                        var value = record.Member(member.Name);
                        if (value != null)
                        {
                            carried[member.Name] = value;
                        }
                    }

                    return carried;

                default:
                    return null;
            }
        }

        /// <summary>An item's members as one text, each name and value apart, to tell two items apart.</summary>
        private static string Canonical(IReadOnlyDictionary<string, string> item)
        {
            return string.Join("\0", item.OrderBy(member => member.Key, StringComparer.Ordinal).Select(member => member.Key + "\0" + member.Value));
        }

        private static IReadOnlyDictionary<string, string> Members(StoreItem item)
        {
            var members = new Dictionary<string, string>(StringComparer.Ordinal) { { StoreKinds.ItemKind, item.Kind } };
            foreach (var member in item.Members)
            {
                members[member.Key] = member.Value;
            }

            return members;
        }

        private static IReadOnlyDictionary<string, string> Item(string kind, string name, string value, string otherName, string otherValue)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.ItemKind, kind },
                { name, value },
                { otherName, otherValue },
            };
        }

        private static StoreWrite Refusal(string reason, IReadOnlyList<string> leftOut = null)
        {
            return new StoreWrite { Refused = reason, LeftOut = leftOut ?? Array.Empty<string>() };
        }

        private static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }
    }
}
