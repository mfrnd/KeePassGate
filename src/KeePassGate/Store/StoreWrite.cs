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
    /// is measured from, its versions as `StoreReplaced` settles them with the acts' own versions,
    /// and the acts' own items beside them. A version of the acts takes the place of what it
    /// replaces or keeps it, so a store judged to have room is never refused a write that adds an
    /// item at most, and a store judged to have none is stopped before a plan is asked.
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

        /// <summary>
        /// Whether the write is refused for the plugin's own ceilings, `STORE.md` §643: the store
        /// has no room for it, though it may have room for a write that adds less.
        /// </summary>
        public bool NoRoom { get; private set; }

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

        /// <summary>
        /// What the write lifts where it seals a stopped store again, from the store it is planned
        /// over, or null for any other write, `STORE.md` §625.
        /// </summary>
        public StoreSealed Sealed { get; private set; }

        /// <summary>
        /// What the write sets aside where it starts the store again over keys found, from the
        /// store it is planned over, or null for any other write, `STORE.md` §632.
        /// </summary>
        public StoreStarted Started { get; private set; }

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

            // Step 1, the state, `STORE.md` §627: a store started where no key stands, or started
            // again over keys found and judged, whatever its state, with room or without, §632; a
            // whole store; and a stopped store whose write is sealed again, the seal first. Every
            // other state is refused with its word, Create among them. A store with no room is
            // stopped and the seal does not lift it, §643.
            var inForce = reading.InForce;
            var startsAgain = acts.Any(act => act.Starts) && StartsOver(reading);
            var starts = reading.State == StoreState.None || startsAgain;
            var seals = !starts && acts[0].Seals && reading.State == StoreState.Stopped && inForce != null;
            if (seals && !inForce.Room.HasRoom)
            {
                return Full("the store has no room for the seal's write: " + inForce.Room.Reason, null);
            }

            if (!starts && !seals && (reading.State != StoreState.Whole || inForce == null))
            {
                var word = StoreWords.Of(reading.State) ?? StoreWords.Stopped;
                return Refusal("the store is " + word + ": " + reading.Reason);
            }

            // Step 2, the ground of each act, against the store with the acts before it taken.
            var ground = new StoreGround(
                starts ? null : inForce.Key.Name,
                starts ? Enumerable.Empty<string>() : inForce.Authorities.Select(record => record.Form.Member(StoreKinds.Id)));
            var taken = new List<StoreAct>();
            var leftOut = new List<string>();
            foreach (var act in acts)
            {
                // A seal is written first, over a stopped store, and nowhere else: over a store
                // that is whole it has nothing to seal, and after another act it would be planned
                // over a store that act was composed for, `STORE.md` §625.
                var why = act.Starts ? StartRefused(reading, act, ground)
                    : !act.Seals ? act.Why(ground)
                    : starts ? "the store is started again, which sets the store sealed aside"
                    : !ReferenceEquals(act, acts[0]) ? "a seal is written first, before every other act"
                    : !seals ? "the store is not stopped, so there is nothing to seal"
                    : null;

                // The key the act was composed against, held to the key that stands in both
                // directions: a Create composed under a key that went since would start the store
                // again under it, with every record still in the file under that key.
                if (why == null && !string.Equals(act.Against, ground.KeyName, StringComparison.Ordinal))
                {
                    why = act.Against == null
                        ? "it was composed where no store key stood, and a key stands now"
                        : "it was composed under a store key that does not stand";
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

            // Over a stopped store nothing is written but behind its seal.
            if (seals && !taken[0].Seals)
            {
                return Refusal(leftOut[0], leftOut);
            }

            // Over a store started again nothing is written but behind the start.
            if (startsAgain && !taken[0].Starts)
            {
                return Refusal(leftOut.FirstOrDefault() ?? "a store started again is written first", leftOut);
            }

            var key = starts ? taken[0].Key() : (byte[])inForce.Key.Bytes.Clone();
            try
            {
                return Planned(reading, inForce, starts, seals, key, taken, leftOut, plugin);
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
                    next.Add(candidate.WithVersions(versions));
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
        /// <param name="seals">Whether the write seals a stopped store again, its first act the seal.</param>
        private static StoreWrite Planned(
            StoreReading reading,
            StoreInForce inForce,
            bool starts,
            bool seals,
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
            var keyDropped = added.Any(record => record.Kind == StoreKinds.Retired
                                                 && unmade.Contains(record.Member(StoreKinds.AuthorityMember))
                                                 && record.Protected);
            added.RemoveAll(record =>
                (record.Kind == StoreKinds.Authority && unmade.Contains(record.Member(StoreKinds.Id)))
                || (record.Kind == StoreKinds.Retired && unmade.Contains(record.Member(StoreKinds.AuthorityMember))));

            // Where such a retirement held the store key, as a Delete pressed after a Recreate on
            // the same dialog holds it, the retirements left in the write hold it in its place:
            // the write is then the Delete of what stood, and never a write that leaves no entry
            // holding the key.
            if (keyDropped)
            {
                for (var at = 0; at < added.Count; at++)
                {
                    if (added[at].Kind == StoreKinds.Retired && !added[at].Protected)
                    {
                        added[at] = Holding(key, added[at]);
                    }
                }
            }

            // A record is written once: a record whose identifier an entry of the tree holds, a
            // record a deletion record names, and a record an act before it in this write adds,
            // are not added again. The identifier is the record's own, and a second entry under it
            // would be two entries the tree tells apart by nothing.
            var present = reading.Present;
            var held = reading.Deleted;
            var once = new HashSet<StoreId>();
            added.RemoveAll(record =>
            {
                StoreId identifier;
                return StoreId.TryParse(record.Hex, out identifier)
                    && (present.Contains(identifier) || held.Contains(identifier) || !once.Add(identifier));
            });

            // A seal writes its summary entry and its deletions whatever records its acts hold.
            if (added.Count == 0 && !seals)
            {
                return new StoreWrite { Empty = true, Taken = taken, LeftOut = leftOut };
            }

            // A decision the store holds with no subject is carried as it is, a record of this
            // write naming its subject among them, `STORE.md` §611: the write after it reads the
            // subject named and restates the decision by it.
            // What the retirements, the revocations and the certificates taken away of the store
            // and of this write take away, `STORE.md` §614, §615 and §648: a write deletes in the
            // same write what it refuses.
            var retired = Taking(starts ? null : inForce, added, StoreKinds.Retired, StoreKinds.AuthorityMember);
            var revoked = Taking(starts ? null : inForce, added, StoreKinds.Revoked, StoreKinds.ClientMember);
            var withdrawn = Taking(starts ? null : inForce, added, StoreKinds.Withdrawn, StoreKinds.Certificate);

            // The versions replaced, `STORE.md` §617 and §627 step 3: what a version in force or a
            // version of this write names of its own object, deleted only where no answer changes
            // for it. A version that only a version out of force names stands, §623 step 3, so it
            // is not among them.
            var replaced = StoreReplaced.Of(key, starts ? null : inForce, added, present, held);
            if (replaced.NamesItsOwn)
            {
                return Refusal("a version of the write names a version the same write adds", leftOut);
            }

            // Step 3, the version record, `STORE.md` §650: with the store's first write, and where
            // the plugin's version is above the highest the store states.
            var format = starts ? 0 : inForce.Format;
            var stated = starts ? null : inForce.Plugin;
            // A store started again writes its stops first, each under a key it sets aside, and
            // then its own records under the new key, the version record first among them.
            var keyName = StoreFormat.KeyName(key);
            var stops = starts ? added.TakeWhile(record => Stops(record, keyName)).Count() : 0;
            if (starts || StoreInForce.Above(plugin, stated))
            {
                added.Insert(stops, StoreRecord.Compose(key, StoreKinds.Format, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.FormatMember, StoreFormat.Number.ToString(CultureInfo.InvariantCulture) },
                    { StoreKinds.PluginMember, plugin },
                }));
                format = Math.Max(format, StoreFormat.Number);
                stated = plugin;
            }

            // Step 4, what is deleted with a deletion record: every authority a retirement names,
            // every summary entry present, and every record of kind `client` whose client key is
            // revoked, whose issuer is retired or whose certificate was taken away, `STORE.md`
            // §627 step 3. Those are told by the texts the core keeps, in force or not and whether
            // or not the record reads as an admission, so no certificate is read here, and no
            // other record of that kind is deleted, since an admission that does not count now,
            // among several admissions of a certificate, may count once a merge brings a deletion
            // record of the others. And every addition that a removal of its name defeats, and
            // every record of kind `version` replaced that the write deletes.
            var delete = new List<string>();
            if (!starts)
            {
                delete.AddRange(inForce.Records
                    .Where(record => (record.Is(StoreKinds.Authority) && retired.Contains(record.Form.Member(StoreKinds.Id)))
                                     || record.Is(StoreKinds.Head))
                    .Select(record => record.Hex));
                delete.AddRange(replaced.Deleted);
                delete.AddRange(inForce.Admissions
                    .Where(admission => revoked.Contains(admission.Form.Member(StoreKinds.ClientMember))
                                        || retired.Contains(admission.Form.Member(StoreKinds.Issuer))
                                        || withdrawn.Contains(admission.Form.Member(StoreKinds.Certificate)))
                    .Select(admission => admission.Hex));
                delete.AddRange(Defeated(inForce, added));
            }

            // A seal deletes every entry that is damage, whatever its tag, each with a deletion
            // record, `STORE.md` §625: what a damaged entry held stands where a summary entry
            // carries it, and refuses where nothing does, which is narrower.
            var damagedHeads = new List<string>();
            if (seals)
            {
                delete.AddRange(reading.Damaged.Select(damaged => damaged.Hex));
                damagedHeads.AddRange(reading.Damaged.Where(damaged => damaged.Kind == StoreKinds.Head).Select(damaged => damaged.Hex));
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

            // Step 5, a deletion record for every summary entry named and every version replaced
            // that a version staying names, each where it is not in the file, once, so that a
            // merge bringing it back finds it out of force. Not in the file is no entry of the
            // tree under that identifier, a record or not: an entry with every string of the store
            // gone stays where it is, and a deletion record for it would have a merge delete it.
            var deletions = (starts ? Enumerable.Empty<string>() : inForce.After)
                .Where(identifier => !Holds(present, identifier) && !Holds(held, identifier))
                .Concat(replaced.Told)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(identifier => identifier, StringComparer.Ordinal)
                .ToList();

            // Step 6, the summary entry, `STORE.md` §611.
            var items = new List<IReadOnlyDictionary<string, string>>
            {
                Item(StoreKinds.Format, StoreKinds.FormatMember, format.ToString(CultureInfo.InvariantCulture), StoreKinds.PluginMember, stated),
            };

            // The versions as the write settles them: every standing version, and every version
            // replaced that stays and that no record in force carries, `StoreReplaced`.
            if (!starts)
            {
                items.AddRange(inForce.Restated()
                    .Where(item => item.Kind != StoreKinds.Format && item.Kind != StoreKinds.Version)
                    .Select(Members));
                items.AddRange(replaced.Restated.Select(id => Members(new StoreItem(StoreKinds.Version, inForce.Versions[id].Members))));
            }

            // A stop is restated by no summary entry, under its key or any other, so that it stops
            // the store it sets aside once the new key is lost, `STORE.md` §632.
            items.AddRange(added.Where(record => !(starts && Stops(record, keyName))).Select(Restating).Where(item => item != null));

            // An item restating what is restated already is the same item, and a summary entry
            // holds it once.
            items = items.GroupBy(Canonical, StringComparer.Ordinal).Select(group => group.First()).ToList();

            // Step 7, the plugin's own ceilings, `STORE.md` §643: refused whole, before anything
            // is added.
            if (items.Count > StoreFormat.MaxItems)
            {
                return Full(string.Format(
                    CultureInfo.InvariantCulture,
                    "the write needs {0} items in its summary entry, and a summary entry the plugin writes has names for {1}",
                    items.Count,
                    StoreFormat.MaxItems), leftOut);
            }

            // A damaged summary entry the seal deletes is named as well: its deletion record would
            // otherwise tell of a summary entry nothing in force names, and stop the store again.
            var height = starts ? 1 : Math.Min(StoreFormat.MaxNumber, damagedHeads.Select(hex => NumberOf(hex) + 1).Concat(new[] { inForce.Room.Height }).Max());
            var after = starts
                ? string.Empty
                : string.Join(" ", inForce.After.Concat(damagedHeads).Distinct(StringComparer.Ordinal).OrderBy(identifier => identifier, StringComparer.Ordinal));
            // A summary entry that does not read back holds more than the reader takes, which a
            // store with room never reaches, and is refused here; anything else Compose raises is
            // a defect, and travels to the catch-all above the write, which counts it.
            string unread;
            var head = StoreRecord.TryCompose(key, StoreKinds.Head, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.After, after },
                { StoreKinds.Height, height.ToString(CultureInfo.InvariantCulture) },
            }, items, out unread);
            if (head == null)
            {
                return Full(unread, leftOut);
            }

            added.Add(head);

            var longest = added.SelectMany(record => record.Strings.Values).Max(value => value.Length);
            if (longest > StoreFormat.MaxWritten)
            {
                return Full(string.Format(
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
                Sealed = seals
                    ? new StoreSealed(reading.Reason, reading.Damaged.Select(damaged => damaged.Hex).ToList(), inForce.RefusesAfter)
                    : null,
                Started = taken[0].Starts
                    ? new StoreStarted(keyName, reading.Found.Select(found => found.Name).ToList(), taken[0].Admitted == null ? 0 : taken[0].Admitted(reading))
                    : null,
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

        /// <summary>
        /// The values a kind restated by a member takes away in the store, by record, item and
        /// deletion record, `STORE.md` §623 step 2, with the values the records of this write of
        /// that kind name.
        /// </summary>
        /// <param name="inForce">What is in force in the store, or null for a write that starts a store.</param>
        private static HashSet<string> Taking(StoreInForce inForce, IEnumerable<StoreRecord> added, string kind, string member)
        {
            var taken = new HashSet<string>(inForce == null ? Enumerable.Empty<string>() : inForce.Taken(kind), StringComparer.Ordinal);
            taken.UnionWith(added.Where(record => record.Kind == kind).Select(record => record.Member(member)));
            return taken;
        }

        /// <summary>
        /// The additions present under the key that a removal defeats, `STORE.md` §616 and §627
        /// step 3: each record of kind `profile`, in force or not and whatever its name, whose
        /// `after` does not hold every removal of its name that the store or this write's own
        /// records know.
        /// </summary>
        /// <remarks>
        /// **Deleting an addition of these changes no answer.** A removal is never deleted, so an
        /// addition that did not see it stays defeated in every copy whether or not its deletion
        /// record reaches that copy. No other addition is deleted, a second addition of a name on
        /// the list among them, and no addition this write adds: what a write adds is what its
        /// acts were composed to add.
        /// </remarks>
        private static IEnumerable<string> Defeated(StoreInForce inForce, IReadOnlyList<StoreRecord> added)
        {
            var written = added
                .Where(record => record.Kind == StoreKinds.Removed)
                .ToLookup(record => record.Member(StoreKinds.NameMember), record => record.Hex, StringComparer.Ordinal);
            foreach (var addition in inForce.Additions)
            {
                var name = addition.Form.Member(StoreKinds.NameMember);
                var seen = StoreFormat.Identifiers(addition.Form.Member(StoreKinds.After)) ?? Array.Empty<string>();
                if (inForce.RemovalsOf(name).Concat(written[name]).Except(seen, StringComparer.Ordinal).Any())
                {
                    yield return addition.Hex;
                }
            }
        }

        /// <summary>
        /// Whether a store started again is written over this reading, `STORE.md` §632: keys are
        /// found and judged, within `StoreFormat.MaxKeys`, the store whole, stopped, split or
        /// outdated, with room or without.
        /// </summary>
        private static bool StartsOver(StoreReading reading)
        {
            return reading.Judged
                   && reading.Found.Count > 0
                   && (reading.State == StoreState.Whole
                       || reading.State == StoreState.Stopped
                       || reading.State == StoreState.Split
                       || reading.State == StoreState.Outdated);
        }

        /// <summary>
        /// Why a store started again does not apply to the store as this reading found it, or
        /// null where it does: a key found that the act names no keep record for would stand
        /// beside the new key, `STORE.md` §632; and the act is the first act of its write.
        /// </summary>
        private static string StartRefused(StoreReading reading, StoreAct act, StoreGround ground)
        {
            if (ground.KeyName != null)
            {
                return "a store started again is written first, before every other act";
            }

            var named = new HashSet<string>(
                act.Records.Where(record => record.Kind == StoreKinds.Kept).Select(record => record.Member(StoreKinds.Key)),
                StringComparer.Ordinal);
            var missing = reading.Found.Select(found => found.Name).FirstOrDefault(name => !named.Contains(name));
            return missing == null
                ? null
                : "a store key is found that it sets nothing aside for: " + missing.Substring(0, 8);
        }

        /// <summary>
        /// Whether a record is a stop a store started again writes: a keep record naming the key
        /// the write starts the store under, under a key it sets aside, `STORE.md` §632.
        /// </summary>
        private static bool Stops(StoreRecord record, string keyName)
        {
            return record.Kind == StoreKinds.Kept && string.Equals(record.Member(StoreKinds.Key), keyName, StringComparison.Ordinal);
        }

        /// <summary>Whether these identifiers hold this identifier, written as text.</summary>
        private static bool Holds(ISet<StoreId> identifiers, string identifier)
        {
            StoreId read;
            return StoreId.TryParse(identifier, out read) && identifiers.Contains(read);
        }

        /// <summary>A retirement composed again with the store key among its members.</summary>
        internal static StoreRecord Holding(byte[] key, StoreRecord retirement)
        {
            var members = retirement.Members
                .Where(member => member.Key != StoreKinds.Nonce)
                .ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            members[StoreKinds.StoreKey] = Hex.Of(key);
            return StoreRecord.Compose(key, StoreKinds.Retired, members);
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

        /// <summary>A refusal for the plugin's own ceilings, `STORE.md` §643.</summary>
        private static StoreWrite Full(string reason, IReadOnlyList<string> leftOut)
        {
            var refused = Refusal(reason, leftOut);
            refused.NoRoom = true;
            return refused;
        }

        /// <summary>Bytes 9 and 10 of an identifier written as text, a summary entry's height.</summary>
        private static int NumberOf(string hex)
        {
            return int.Parse(
                hex.Substring((StoreFormat.TagLength + StoreFormat.SubjectLength) * 2, StoreFormat.HeightLength * 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
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
