using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// What is in force under a store key, `STORE.md` §623: the records and the deletion records
    /// that name them, the summary entries, what is taken away and by what, the highest format
    /// number and plugin version the store states, the authorities that stand, and the room the
    /// store has for its next write, `STORE.md` §627.
    /// </summary>
    /// <remarks>
    /// **Made by the judgment, from what the reading found under a single key**, and handed out
    /// for a store that is whole or stopped, which is the store a write is made into or sealed
    /// again: of a form the plugin does not read, nothing but the authorities a word is answered
    /// under is taken from it, and of a store under several keys it is not made at all, since
    /// <see cref="Standing"/> works out those authorities alone.
    ///
    /// **Where it may be asked from.** It is made inside the reading, on the thread making it,
    /// which is the only caller of `FoundKey.Subject` and `FoundKey.TagOf`; what is handed out is
    /// read and never asked for a subject. <see cref="Key"/> holds bytes a writer composes under
    /// only where the reading was made afresh in the writer's own turn, `StoreReading.Afresh`: a
    /// gate's reading may hand out forms whose keys were overwritten as it ended, which is why no
    /// connection thread takes a key from one.
    ///
    /// **What takes access away is a union**, `STORE.md` §623 step 2: the records present, the
    /// items of every summary entry present, in force or not, and the deletion records that tell
    /// of a record of a subject the file knows. **A deletion record tells only under its own
    /// kind's tag**, so the deletion record of an authority, which shares a subject with its
    /// retirement, retires nothing. For a certificate taken away the file knows an admission key
    /// from the records and the items of that kind alone, their `certificate`, `from` and
    /// `renewed`: an admission's own `certificate` and `from` are read with the admissions, which
    /// this build does not read, `STORE.md` §620.
    ///
    /// **A certificate taken away is a set of statements**, `STORE.md` §648: each record of the
    /// kind by its identifier, each item of the kind as it is, and each deletion record of the
    /// kind that no record present and no item answers for, as a record whose content is
    /// nowhere, with no renewal. Each statement is restated by an item of its own.
    ///
    /// **No step follows a list**, as in the judgment: `after` is read as the identifiers it
    /// names, and nothing is looked up through them.
    ///
    /// **What it costs.** Every record and every item once, through sets, and each deletion record
    /// under the key once. A subject is hashed only where a deletion record under the key carries
    /// the tag of a kind about that subject, and the hash is kept with the key, so a hold of a
    /// store whose deletion records tell of nothing hashes nothing, `FoundKey.Subject`. Everything
    /// is worked out as the reading is made, and nothing after: what is handed out is read and
    /// never changed, by whichever thread holds the reading.
    /// </remarks>
    internal sealed class StoreInForce
    {
        private static readonly IReadOnlyCollection<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

        private readonly Dictionary<string, HashSet<string>> _taken = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>
        /// The removals of names, each by its name and the identifier of its record, its item's
        /// `id` or its deletion record: a summary entry restates each apart, `STORE.md` §611.
        /// </summary>
        private readonly HashSet<(string Name, string Id)> _removals = new HashSet<(string Name, string Id)>();

        /// <summary>The versions a summary entry carries, by identifier, each with its item's members.</summary>
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _versions =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>The keep records a summary entry carries, by identifier, each with its item's members.</summary>
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _kept =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>
        /// The statements that took certificates away, each whole, `STORE.md` §648: what the
        /// room counts and a write restates.
        /// </summary>
        private readonly HashSet<(string Id, string Certificate, string Renewed, string From)> _statements =
            new HashSet<(string Id, string Certificate, string Renewed, string From)>();

        /// <summary>The statements the items of the summary entries present make: what restates a record.</summary>
        private readonly HashSet<(string Id, string Certificate, string Renewed, string From)> _itemStatements =
            new HashSet<(string Id, string Certificate, string Renewed, string From)>();

        private HashSet<string> _after = new HashSet<string>(StringComparer.Ordinal);

        private StoreInForce()
        {
        }

        /// <summary>
        /// The store key, as the form that gave it holds it: what a write composes its records
        /// under, read from a reading made afresh in the writer's own turn. Overwritten when the
        /// forms are let go, which a gate's reading made beside a lapsed hold does before it is
        /// handed out.
        /// </summary>
        public FoundKey Key { get; private set; }

        /// <summary>
        /// What a summary entry written now names in `after`, `STORE.md` §611: every summary
        /// entry present, everything those name, and every summary entry a deletion record tells
        /// of.
        /// </summary>
        public IReadOnlyCollection<string> After
        {
            get { return _after; }
        }

        /// <summary>
        /// Every record under the key, in the order the entries were walked: each with the version
        /// that passes, whether it counts by its history and whether a deletion record names it.
        /// </summary>
        public IReadOnlyList<FoundRecord> Records { get; private set; }

        /// <summary>Every item of every summary entry present, of the kinds the plugin knows.</summary>
        public IReadOnlyList<StoreItem> Items { get; private set; }

        /// <summary>The summary entries present, `STORE.md` §611.</summary>
        public IReadOnlyList<FoundRecord> Heads { get; private set; }

        /// <summary>The summary entries in force: present, and named by no deletion record, `STORE.md` §623 step 4.</summary>
        public IReadOnlyList<FoundRecord> HeadsInForce { get; private set; }

        /// <summary>
        /// The identifiers of the summary entries that deletion records tell of, `STORE.md` §623
        /// step 7.
        /// </summary>
        public IReadOnlyList<string> Told { get; private set; }

        /// <summary>
        /// A summary entry that a deletion record tells of and that nothing in force replaces, or
        /// null: a summary entry deleted that nothing present stands in for, `STORE.md` §624.
        /// </summary>
        public string Lost { get; private set; }

        /// <summary>
        /// The authorities that stand under the key: in force, and named by no retirement, by
        /// record, item or deletion record, `STORE.md` §614. In the order of their identifiers.
        /// </summary>
        public IReadOnlyList<FoundRecord> Authorities { get; private set; }

        /// <summary>
        /// The highest format number the store states, `STORE.md` §623 step 6, or 0 where it
        /// states none.
        /// </summary>
        public int Format { get; private set; }

        /// <summary>
        /// The highest plugin version the store states, as three whole numbers joined by periods,
        /// or null where it states none: a deletion record states no plugin version.
        /// </summary>
        public string Plugin { get; private set; }

        /// <summary>The room the store has for its next write, `STORE.md` §627.</summary>
        public StoreRoom Room { get; private set; }

        /// <summary>
        /// The versions that a version in force names in `after`: replaced, so that no summary
        /// entry carries them, `STORE.md` §658.
        /// </summary>
        private ISet<string> ReplacedVersions { get; set; }

        /// <summary>What the items of the summary entries present restate, by kind, `STORE.md` §658.</summary>
        private HashSet<(string Kind, string Value, string Id)> Restating { get; set; }

        /// <summary>The highest format number the items of the summary entries present state.</summary>
        private int ItemFormat { get; set; }

        /// <summary>
        /// The values that records, items and deletion records take away for a kind a summary
        /// entry restates by a member, `STORE.md` §623 step 2: the authorities retired, the
        /// clients revoked, the certificates taken away, and every name a removal names. Nothing
        /// for any other kind.
        /// </summary>
        /// <remarks>
        /// **Every name a removal names is not the names off the list**: a name an addition has
        /// put back on the list after its removal is among them, and which names are off the list
        /// is the profile names' meaning, `STORE.md` §616, which this does not work out.
        /// </remarks>
        public IReadOnlyCollection<string> Taken(string kind)
        {
            HashSet<string> values;
            return kind != null && _taken.TryGetValue(kind, out values) ? values : Nothing;
        }

        /// <summary>
        /// The first record that takes something away, or holds a plugin back, and that no
        /// summary entry restates as its row of `STORE.md` §658 has it, or null, `STORE.md` §624.
        /// </summary>
        public FoundRecord NotRestated()
        {
            foreach (var record in Records.Where(record => record.Form.ReadsAsKind))
            {
                var row = record.Form.Row;
                switch (row.Restated)
                {
                    case StoreRestated.BySameMember:
                        var restated = (row.Name, record.Form.Member(row.RestatedBy), row.Name == StoreKinds.Removed ? record.Hex : null);
                        if (!Restating.Contains(restated))
                        {
                            return record;
                        }

                        break;

                    case StoreRestated.ById:
                        // A version out of force, or a version that a version in force
                        // replaces, is no standing version, and no summary entry carries it.
                        if (!row.CountsBesideDeletion && (record.BesideDeletion || ReplacedVersions.Contains(record.Hex)))
                        {
                            break;
                        }

                        if (!Restating.Contains((row.Name, record.Hex, null)))
                        {
                            return record;
                        }

                        break;

                    case StoreRestated.ByFormat:
                        if (ItemFormat < record.Form.Number)
                        {
                            return record;
                        }

                        break;

                    case StoreRestated.ByStatement:
                        // Only by an item of its identifier holding the same of every member.
                        if (!_itemStatements.Contains(StatementOf(record.Hex, record.Form.Members)))
                        {
                            return record;
                        }

                        break;
                }
            }

            return null;
        }

        /// <summary>
        /// What is in force under this key, from the records found under it and the identifiers
        /// the deletion records name.
        /// </summary>
        /// <param name="byTag">
        /// The identifiers the deletion records name that begin with a tag under a key found, as
        /// lowercase hexadecimal, by that tag.
        /// </param>
        /// <param name="keptByDeletion">
        /// The deletion records that tell of a keep record under this key, each with the name of
        /// the key it sets aside, `STORE.md` §623 step 7: what a summary entry carries on for
        /// them, since none of them is a record present.
        /// </param>
        public static StoreInForce Of(
            FoundKey key,
            IReadOnlyList<FoundRecord> records,
            IReadOnlyDictionary<string, List<string>> byTag,
            IReadOnlyCollection<KeyValuePair<string, string>> keptByDeletion)
        {
            var made = new StoreInForce { Key = key, Records = records };
            made.Heads = records.Where(record => record.Is(StoreKinds.Head)).ToList();
            made.HeadsInForce = made.Heads.Where(head => head.InForce).ToList();
            made.Items = made.Heads.SelectMany(head => head.Form.Items).ToList();

            var byKind = ByKind(key, byTag);
            made.Told = Telling(key, byKind, StoreKinds.Head, StoreKinds.StoreSubject).ToList();
            made.Highest(key, byKind);
            made.Take(key, byKind);
            made.Restate();

            var retired = made._taken[StoreKinds.Retired];
            made.Authorities = records
                .Where(record => record.Is(StoreKinds.Authority) && record.InForce
                                 && !retired.Contains(record.Form.Member(StoreKinds.Id)))
                .OrderBy(record => record.Hex, StringComparer.Ordinal)
                .ToList();

            made.Room = made.Measured(keptByDeletion ?? Array.Empty<KeyValuePair<string, string>>());
            return made;
        }

        /// <summary>
        /// The deletion records under this key, by the kind whose tag they begin with: looked up
        /// by tag, so this key costs a lookup a kind and none a deletion record. Two kinds whose
        /// tags meet under a key each take the deletion records of both, which tells of more and
        /// never of less.
        /// </summary>
        private static Dictionary<string, List<string>> ByKind(FoundKey key, IReadOnlyDictionary<string, List<string>> byTag)
        {
            var byKind = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kind in StoreKinds.All)
            {
                List<string> tagged;
                byKind[kind.Name] = byTag.TryGetValue(key.TagOf(kind), out tagged) ? tagged : new List<string>();
            }

            return byKind;
        }

        /// <summary>
        /// The authorities that stand under a key, and nothing else of what is in force: what a
        /// word is answered under where several keys stand, `STORE.md` §656. The authority
        /// records in force, less those a retirement names by record, by an item of any summary
        /// entry present, or by a deletion record of its tag whose subject hash is that of an
        /// authority present: the `id` of an authority present is the only value hashed.
        /// </summary>
        public static IReadOnlyList<FoundRecord> Standing(
            FoundKey key, IReadOnlyList<FoundRecord> records, IReadOnlyDictionary<string, List<string>> byTag)
        {
            var authorities = records.Where(record => record.Is(StoreKinds.Authority) && record.InForce).ToList();
            if (authorities.Count == 0)
            {
                return authorities;
            }

            var retired = Retired(key, records, byTag, authorities.Select(record => record.Form.Member(StoreKinds.Id)));
            return authorities
                .Where(record => !retired.Contains(record.Form.Member(StoreKinds.Id)))
                .OrderBy(record => record.Hex, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The authorities a retirement under a key names, of those whose `id` is given: by
        /// record, by an item of any summary entry present, or by a deletion record of its tag
        /// whose subject hash is that of an `id` given, which are the only values hashed.
        /// </summary>
        public static ISet<string> Retired(
            FoundKey key, IReadOnlyList<FoundRecord> records, IReadOnlyDictionary<string, List<string>> byTag, IEnumerable<string> ids)
        {
            var retired = new HashSet<string>(
                records.Where(record => record.Is(StoreKinds.Retired)).Select(record => record.Form.Member(StoreKinds.AuthorityMember)),
                StringComparer.Ordinal);
            retired.UnionWith(records
                .Where(record => record.Is(StoreKinds.Head))
                .SelectMany(head => head.Form.Items)
                .Where(item => item.Kind == StoreKinds.Retired)
                .Select(item => item.Member(StoreKinds.AuthorityMember)));

            List<string> told;
            var retirement = StoreKinds.Find(StoreKinds.Retired);
            if (byTag.TryGetValue(key.TagOf(retirement), out told) && told.Count > 0)
            {
                var subjects = new HashSet<string>(told.Select(SubjectOf), StringComparer.Ordinal);
                retired.UnionWith(ids.Where(id => subjects.Contains(key.Subject(retirement.SubjectPrefix + id))));
            }

            return retired;
        }

        /// <summary>
        /// The deletion records of a kind that tell of this subject: the subject's hash follows
        /// the tag. The subject is hashed only where there is a deletion record of the kind.
        /// </summary>
        private static IEnumerable<string> Telling(FoundKey key, IReadOnlyDictionary<string, List<string>> byKind, string kind, string subject)
        {
            var identifiers = byKind[kind];
            if (identifiers.Count == 0)
            {
                return Array.Empty<string>();
            }

            var hash = key.Subject(subject);
            return identifiers.Where(identifier => SubjectOf(identifier) == hash);
        }

        /// <summary>
        /// The highest format number and plugin version: of a version record, of the item `format`
        /// of a summary entry present, and, for the number alone, of a deletion record that tells
        /// of a version record by its tag, a number from 1 and the subject hash of that number,
        /// `STORE.md` §623 step 6.
        /// </summary>
        private void Highest(FoundKey key, IReadOnlyDictionary<string, List<string>> byKind)
        {
            foreach (var record in Records.Where(record => record.Is(StoreKinds.Format)))
            {
                Format = Math.Max(Format, record.Form.Number);
                Plugin = Higher(Plugin, record.Form.Member(StoreKinds.PluginMember));
            }

            foreach (var item in Items.Where(item => item.Kind == StoreKinds.Format))
            {
                var number = StoreChecked.NumberOf(item.Member(StoreKinds.FormatMember));
                ItemFormat = Math.Max(ItemFormat, number);
                Format = Math.Max(Format, number);
                Plugin = Higher(Plugin, item.Member(StoreKinds.PluginMember));
            }

            foreach (var identifier in byKind[StoreKinds.Format])
            {
                var number = NumberOf(identifier);
                if (number >= 1 && SubjectOf(identifier) == key.Subject("format:" + number.ToString(CultureInfo.InvariantCulture)))
                {
                    Format = Math.Max(Format, number);
                }
            }
        }

        /// <summary>
        /// What each kind a summary entry restates by a member takes away: its records, its items,
        /// and the deletion records that tell of it about a subject the file knows, `STORE.md`
        /// §623 step 2.
        /// </summary>
        private void Take(FoundKey key, IReadOnlyDictionary<string, List<string>> byKind)
        {
            foreach (var row in StoreKinds.All.Where(row => row.Restated == StoreRestated.BySameMember))
            {
                var values = new HashSet<string>(StringComparer.Ordinal);
                var removal = row.Name == StoreKinds.Removed;
                foreach (var record in Records.Where(record => record.Is(row.Name)))
                {
                    values.Add(record.Form.Member(row.RestatedBy));
                    if (removal)
                    {
                        _removals.Add((record.Form.Member(row.RestatedBy), record.Hex));
                    }
                }

                foreach (var item in Items.Where(item => item.Kind == row.Name))
                {
                    values.Add(item.Member(row.RestatedBy));
                    if (removal)
                    {
                        _removals.Add((item.Member(row.RestatedBy), item.Member(StoreKinds.Id)));
                    }
                }

                foreach (var told in ToldValues(key, byKind[row.Name], row, () => KnownValues(row)))
                {
                    values.Add(told.Value);
                    if (removal)
                    {
                        _removals.Add(told);
                    }
                }

                _taken[row.Name] = values;
            }

            TakeStatements(key, byKind);
        }

        /// <summary>
        /// The statements that took certificates away, `STORE.md` §623 step 2 and §648, and the
        /// certificates they take away.
        /// </summary>
        private void TakeStatements(FoundKey key, IReadOnlyDictionary<string, List<string>> byKind)
        {
            var row = StoreKinds.Find(StoreKinds.Withdrawn);
            var answered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Items.Where(item => item.Kind == row.Name))
            {
                var statement = StatementOf(item.Member(StoreKinds.Id), item.Members);
                _itemStatements.Add(statement);
                _statements.Add(statement);
                answered.Add(statement.Id);
            }

            foreach (var record in Records.Where(record => record.Is(row.Name)))
            {
                _statements.Add(StatementOf(record.Hex, record.Form.Members));
                answered.Add(record.Hex);
            }

            // A deletion record that no record present and no item answers for stands for a
            // record whose content is nowhere: a statement of its own, which names no renewal.
            foreach (var told in ToldValues(key, byKind[row.Name], row, () => KnownAdmissionKeys(row))
                         .Where(told => !answered.Contains(told.Identifier)))
            {
                _statements.Add((told.Identifier, told.Value, string.Empty, null));
            }

            _taken[row.Name] = new HashSet<string>(_statements.Select(statement => statement.Certificate), StringComparer.Ordinal);
        }

        /// <summary>A statement of what a record or an item holds, a member absent kept as null.</summary>
        private static (string Id, string Certificate, string Renewed, string From) StatementOf(
            string id, IReadOnlyDictionary<string, string> members)
        {
            string certificate;
            string renewed;
            string from;
            members.TryGetValue(StoreKinds.Certificate, out certificate);
            members.TryGetValue(StoreKinds.Renewed, out renewed);
            members.TryGetValue(StoreKinds.From, out from);
            return (id, certificate, renewed, from);
        }

        /// <summary>
        /// The values of a subject the file knows for a kind restated by a member: from the
        /// records of every kind about the same subject and from the items that restate them.
        /// </summary>
        private IEnumerable<string> KnownValues(StoreKind row)
        {
            foreach (var about in StoreKinds.All.Where(about => about.SubjectPrefix == row.SubjectPrefix && about.SubjectMember != null))
            {
                foreach (var record in Records.Where(record => record.Is(about.Name)))
                {
                    yield return record.Form.Member(about.SubjectMember);
                }

                if (about.Restated == StoreRestated.BySameMember)
                {
                    foreach (var item in Items.Where(item => item.Kind == about.Name))
                    {
                        yield return item.Member(about.RestatedBy);
                    }
                }
            }
        }

        /// <summary>
        /// The admission keys the file knows for a certificate taken away, `STORE.md` §623 step
        /// 2: the `certificate`, the `from` and each key in `renewed` of every record and item of
        /// the kind, a text that is no admission key adding none.
        /// </summary>
        private IEnumerable<string> KnownAdmissionKeys(StoreKind row)
        {
            var stated = Records.Where(record => record.Is(row.Name)).Select(record => record.Form.Members)
                .Concat(Items.Where(item => item.Kind == row.Name).Select(item => item.Members));
            foreach (var members in stated)
            {
                string value;
                if (members.TryGetValue(StoreKinds.Certificate, out value))
                {
                    yield return value;
                }

                if (members.TryGetValue(StoreKinds.From, out value) && StoreChecked.InForm(value, StoreForm.Hex))
                {
                    yield return value;
                }

                if (members.TryGetValue(StoreKinds.Renewed, out value))
                {
                    foreach (var renewal in value.Split(' ').Where(renewal => StoreChecked.InForm(renewal, StoreForm.Hex)))
                    {
                        yield return renewal;
                    }
                }
            }
        }

        /// <summary>
        /// The values that deletion records of a kind tell of, each with the deletion record: by
        /// the subject hash of every value of its subject that the file knows.
        /// </summary>
        /// <param name="knownValues">The values the file knows, asked only where a deletion record carries the tag.</param>
        private static IEnumerable<(string Value, string Identifier)> ToldValues(
            FoundKey key, IReadOnlyList<string> identifiers, StoreKind row, Func<IEnumerable<string>> knownValues)
        {
            if (identifiers.Count == 0)
            {
                yield break;
            }

            var known = new HashSet<string>(knownValues(), StringComparer.Ordinal);
            var hashes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var value in known)
            {
                List<string> sharing;
                var hash = key.Subject(row.SubjectPrefix + value);
                if (!hashes.TryGetValue(hash, out sharing))
                {
                    sharing = new List<string>();
                    hashes[hash] = sharing;
                }

                sharing.Add(value);
            }

            foreach (var identifier in identifiers)
            {
                List<string> values;
                if (hashes.TryGetValue(SubjectOf(identifier), out values))
                {
                    foreach (var value in values)
                    {
                        yield return (value, identifier);
                    }
                }
            }
        }

        /// <summary>
        /// What the summary entries present restate, and what the versions in force replace, for
        /// the question of `STORE.md` §624 whether every decision present is restated.
        /// </summary>
        private void Restate()
        {
            Restating = new HashSet<(string Kind, string Value, string Id)>();
            foreach (var item in Items)
            {
                var row = StoreKinds.Find(item.Kind);
                switch (row.Restated)
                {
                    case StoreRestated.BySameMember:
                        // A removal is restated by its name and its identifier both.
                        Restating.Add((row.Name, item.Member(row.RestatedBy), row.Name == StoreKinds.Removed ? item.Member(StoreKinds.Id) : null));
                        break;
                    case StoreRestated.ById:
                        Restating.Add((row.Name, item.Member(StoreKinds.Id), null));
                        break;
                }
            }

            // What a version in force names as replaced: a version record in force, and an item
            // of a summary entry in force.
            ReplacedVersions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in Records.Where(record => record.Is(StoreKinds.Version) && record.InForce))
            {
                ReplacedVersions.UnionWith(Listed(record.Form.Member(StoreKinds.After)));
            }

            foreach (var item in HeadsInForce.SelectMany(head => head.Form.Items).Where(item => item.Kind == StoreKinds.Version))
            {
                ReplacedVersions.UnionWith(Listed(item.Member(StoreKinds.After)));
            }
        }

        /// <summary>
        /// The room the store has: the summary entry a write that adds nothing would write,
        /// `STORE.md` §611 and §627. It names every summary entry present, everything those name
        /// and every summary entry a deletion record tells of; it is higher by a step than any of
        /// them; and it holds the item `format`, an item for each value taken away, and an item
        /// for each version and each keep record that a summary entry carries.
        /// </summary>
        /// <remarks>
        /// A version is carried by the summary entries in force, and by its record where it
        /// stands, `STORE.md` §623 step 3. A keep record counts as an item of any summary entry
        /// present and by its deletion record, `STORE.md` §649, so each of those is carried,
        /// and no relation that sets a key aside is dropped by a write.
        /// </remarks>
        private StoreRoom Measured(IReadOnlyCollection<KeyValuePair<string, string>> keptByDeletion)
        {
            // Every summary entry's list is read once, for what the next names and for what the
            // summary entries in force replace.
            var named = new HashSet<string>(Told, StringComparer.Ordinal);
            _after = named;
            var replaced = new HashSet<string>(StringComparer.Ordinal);
            var height = Told.Select(NumberOf).DefaultIfEmpty(0).Max();
            foreach (var head in Heads)
            {
                var after = Listed(head.Form.Member(StoreKinds.After));
                named.Add(head.Hex);
                named.UnionWith(after);
                height = Math.Max(height, head.Form.Number);
                if (head.InForce)
                {
                    replaced.Add(head.Hex);
                    replaced.UnionWith(after);
                }
            }

            Lost = Told.FirstOrDefault(identifier => !replaced.Contains(identifier));

            var items = 1;
            foreach (var row in StoreKinds.All.Where(row => row.Restated == StoreRestated.BySameMember && row.Name != StoreKinds.Removed))
            {
                items += _taken[row.Name].Count;
            }

            items += _removals.Count;
            items += _statements.Count;

            // A version and a keep record are carried whole, each by its identifier, with the
            // members its item holds: from an item where a summary entry holds an item, and from its
            // record where none does.
            // A standing version only, `STORE.md` §611: an item of a version that a version in
            // force replaces is no more carried than its record is, whichever summary entry
            // holds it.
            foreach (var item in HeadsInForce.SelectMany(head => head.Form.Items)
                         .Where(item => item.Kind == StoreKinds.Version && !ReplacedVersions.Contains(item.Member(StoreKinds.Id))))
            {
                Carry(_versions, item.Member(StoreKinds.Id), item.Members);
            }

            foreach (var record in Records
                         .Where(record => record.Is(StoreKinds.Version) && record.InForce && !ReplacedVersions.Contains(record.Hex)))
            {
                Carry(_versions, record.Hex, record.Form.Members);
            }

            items += _versions.Count;

            foreach (var item in Items.Where(item => item.Kind == StoreKinds.Kept))
            {
                Carry(_kept, item.Member(StoreKinds.Id), item.Members);
            }

            foreach (var record in Records.Where(record => record.Is(StoreKinds.Kept)))
            {
                Carry(_kept, record.Hex, record.Form.Members);
            }

            foreach (var told in keptByDeletion)
            {
                Carry(_kept, told.Key, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.Key, told.Value },
                    { StoreKinds.After, string.Empty },
                });
            }

            items += _kept.Count;

            return new StoreRoom(named.Count, Math.Min(StoreFormat.MaxNumber, height + 1), items);
        }

        /// <summary>
        /// The items a summary entry written now restates, `STORE.md` §611: the item `format`
        /// with the highest format number and plugin version the store states, an item for each
        /// value taken away, and an item for each version and each keep record carried. The very
        /// sets <see cref="Room"/> counts, so what a write restates and the room it was judged to
        /// have cannot disagree.
        /// </summary>
        /// <remarks>
        /// Made when it is asked for, by a write, and not at every reading: the sets are worked
        /// out with the reading, and nothing here hashes or reads a string.
        /// </remarks>
        public IReadOnlyList<StoreItem> Restated()
        {
            var items = new List<StoreItem>();
            if (Format > 0 && Plugin != null)
            {
                items.Add(Item(StoreKinds.Format, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.FormatMember, Format.ToString(CultureInfo.InvariantCulture) },
                    { StoreKinds.PluginMember, Plugin },
                }));
            }

            foreach (var row in StoreKinds.All.Where(row => row.Restated == StoreRestated.BySameMember && row.Name != StoreKinds.Removed))
            {
                foreach (var value in _taken[row.Name])
                {
                    items.Add(Item(row.Name, new Dictionary<string, string>(StringComparer.Ordinal) { { row.RestatedBy, value } }));
                }
            }

            foreach (var removal in _removals)
            {
                items.Add(Item(StoreKinds.Removed, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.NameMember, removal.Name },
                    { StoreKinds.Id, removal.Id },
                }));
            }

            // A statement with every member it holds and none it lacks, `STORE.md` §648.
            foreach (var statement in _statements)
            {
                var members = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.Id, statement.Id },
                    { StoreKinds.Certificate, statement.Certificate },
                };
                if (statement.Renewed != null)
                {
                    members[StoreKinds.Renewed] = statement.Renewed;
                }

                if (statement.From != null)
                {
                    members[StoreKinds.From] = statement.From;
                }

                items.Add(Item(StoreKinds.Withdrawn, members));
            }

            items.AddRange(_versions.Select(version => Item(StoreKinds.Version, version.Value)));
            items.AddRange(_kept.Select(kept => Item(StoreKinds.Kept, kept.Value)));
            return items;
        }

        /// <summary>
        /// Whether a plugin version is above another, each three whole numbers joined by
        /// periods, compared as numbers; nothing is above a version not stated.
        /// </summary>
        internal static bool Above(string version, string held)
        {
            return version != null && (held == null || Order(version) > Order(held));
        }

        private static StoreItem Item(string kind, IReadOnlyDictionary<string, string> members)
        {
            return new StoreItem(kind, members);
        }

        /// <summary>An item carried by its identifier, with the item's members and that identifier.</summary>
        private static void Carry(
            Dictionary<string, IReadOnlyDictionary<string, string>> carried, string id, IReadOnlyDictionary<string, string> members)
        {
            if (id == null || carried.ContainsKey(id))
            {
                return;
            }

            var item = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                item[member.Key] = member.Value;
            }

            item[StoreKinds.Id] = id;
            carried[id] = item;
        }

        /// <summary>The higher of two plugin versions, each three whole numbers joined by periods, either may be null.</summary>
        private static string Higher(string held, string stated)
        {
            if (held == null)
            {
                return stated;
            }

            return stated != null && Order(stated) > Order(held) ? stated : held;
        }

        private static long Order(string version)
        {
            var parts = version.Split('.');
            return (long.Parse(parts[0], CultureInfo.InvariantCulture) << 32)
                   | (long.Parse(parts[1], CultureInfo.InvariantCulture) << 16)
                   | long.Parse(parts[2], CultureInfo.InvariantCulture);
        }

        /// <summary>Bytes 4 to 8 of an identifier, the subject's hash, as hexadecimal.</summary>
        private static string SubjectOf(string identifier)
        {
            return identifier.Substring(StoreFormat.TagLength * 2, StoreFormat.SubjectLength * 2);
        }

        /// <summary>Bytes 9 and 10 of an identifier, a height or a format number.</summary>
        private static int NumberOf(string identifier)
        {
            return int.Parse(
                identifier.Substring((StoreFormat.TagLength + StoreFormat.SubjectLength) * 2, StoreFormat.HeightLength * 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }

        private static IReadOnlyList<string> Listed(string list)
        {
            return StoreFormat.Identifiers(list) ?? Array.Empty<string>();
        }
    }
}
