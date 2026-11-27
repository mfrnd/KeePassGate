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
    /// which is the only caller of `FoundKey.Subject`, `FoundKey.NameTag` and `FoundKey.TagOf`,
    /// and which alone walks the tree it is handed; what is handed out is read and asks the key's
    /// cache for no subject, <see cref="RemovalsOnceKnown"/> hashing the name an addition is
    /// composed for under the key's bytes, for whoever composes it. What stands and what is lost
    /// of each object is worked out as it is made, so <see cref="Objects"/>,
    /// <see cref="VersionsOf"/> and <see cref="LostOf"/> take no key and hash nothing.
    /// <see cref="Key"/> holds bytes a writer composes under only where the reading was made
    /// afresh in the writer's own turn, `StoreReading.Afresh`: a gate's reading may hand out
    /// forms whose keys were overwritten as it ended, which is why no connection thread takes a
    /// key from a gate's reading.
    ///
    /// **What takes access away is a union**, `STORE.md` §623 step 2: the records present, the
    /// items of every summary entry present, in force or not, and the deletion records that tell
    /// of a record of a subject the file knows. **A deletion record tells only under its own
    /// kind's tag**, so the deletion record of an authority, which shares a subject with its
    /// retirement, retires nothing. For a certificate taken away the file knows an admission key
    /// from the `certificate` and the `from` of every record of kind `client` present, in force
    /// or not, and from the `certificate`, `from` and `renewed` of the records and the items of
    /// that kind: the admission of a certificate taken away goes with the write that takes it
    /// away, and where the record and its item are lost as well, a renewal's `from` is the text
    /// left that names the certificate. Knowing a key only lets a deletion record refuse.
    ///
    /// **A decision whose subject the file names nowhere is held by what tells of it**, `STORE.md`
    /// §611: a revocation or a retirement a deletion record tells of, by that record's keyed tag
    /// and subject hash, and a removal of a name by its identifier, and so is an item with no
    /// subject that holds the same. Such an item tells its decision of every subject the file names
    /// whose keyed tag and subject hash it holds, as a deletion record does, and restates every
    /// record of its kind whose identifier begins with its hash, or the removal whose identifier
    /// it holds; what tells of a subject the file names nowhere is restated by an item with no
    /// subject, which every write carries until the subject is named.
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
    /// under the key once, compared as its bytes, `StoreId`, with no text made of it unless it is
    /// handed out. Every list is read once, with the checked form of the version that holds it, and
    /// kept with that form between two holds, `StoreNames`, so a hold reads no name of a list
    /// nothing changed: what stands is worked out from the versions of each object, and a deletion
    /// record at the place of a version is answered by a search of the lists in force of what it
    /// tells of. A store after many renewals still costs each hold a few lookups for each renewal
    /// ever made, for good, since KeePass keeps every deletion record, `STORE.md` §643. A subject
    /// is hashed only where a deletion record under the key carries the tag of a kind about that
    /// subject, or an item with no subject of such a kind is present, and the hash is kept with the
    /// key, so a hold of a store whose deletion records tell of nothing hashes nothing,
    /// `FoundKey.Subject`. Everything
    /// is worked out as the reading is made, but the hash of the name an addition is composed
    /// for: what is handed out is read and never changed, by whichever thread holds the reading.
    ///
    /// **What the rules cost in hashes**, `STORE.md` §620. A deletion record under the tag of kind
    /// `version` under which a version is present costs nothing. Every other is placed by its
    /// first ten bytes where a version present begins with them, with no subject hashed and no
    /// tree walked, which places every deletion record of the plugin's own deletions and
    /// renewals, since the version of the same object naming it is at its place. A deletion
    /// record placed so nowhere costs a subject hash of `rule:` for each node the objects of the
    /// versions present name, and of `switch:` for each where such a record holds 0 in bytes 9
    /// and 10; then, only where its subject meets none of those, the same for each group and entry
    /// of the tree not yet tried, the tree being walked once in that reading. Whether a version
    /// of another object names it changes none of this. Once a deletion record lies at a node's
    /// rows, the tag of each name the file knows is wanted: read off the identifier of a row
    /// record where one shows the name, and hashed otherwise. So a store whose deletion records
    /// are all the plugin's own, and whose every name has a row record, hashes nothing for the
    /// rules at any hold, and a name with no row record has its tag hashed once. Each hash is kept
    /// with the key, so a later hold under the same key hashes only what is new, and walks the tree
    /// again only while a deletion record placed nowhere is left.
    ///
    /// **What the answers to renewals cost in hashes**, `STORE.md` §618 and §620. An answer's
    /// subject is its whole object, `renew:` and the client key, with 0 in bytes 9 and 10, so a
    /// deletion record at the place of an answer present is that answer's, with nothing hashed. A
    /// deletion record placed nowhere that holds 0 costs, after the nodes the objects name and
    /// before the tree, a subject hash of `renew:` and each client key the file knows; a client
    /// key the file does not know tells of nothing, until the file knows it.
    /// </remarks>
    internal sealed class StoreInForce
    {
        private static readonly IReadOnlyCollection<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

        private readonly Dictionary<string, HashSet<string>> _taken = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>What a deletion record alone takes away, by kind, <see cref="ByDeletion"/>.</summary>
        private readonly Dictionary<string, HashSet<string>> _byDeletion = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>
        /// The removals of names, each by its name and the identifier of its record, its item's
        /// `id` or its deletion record: a summary entry restates each apart, `STORE.md` §611.
        /// </summary>
        private readonly HashSet<(string Name, string Id)> _removals = new HashSet<(string Name, string Id)>();

        /// <summary>The identifiers of <see cref="_removals"/>, by name.</summary>
        private readonly Dictionary<string, HashSet<string>> _removalsOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>
        /// The deletion records under the tag of kind `removed`, by the subject hash each carries:
        /// what tells of a removal of a name once the name is known, <see cref="RemovalsOnceKnown"/>.
        /// </summary>
        private readonly Dictionary<long, List<string>> _removalDeletions = new Dictionary<long, List<string>>();

        /// <summary>
        /// The decisions whose subject the file names nowhere, by kind, `STORE.md` §611: of a
        /// revocation and a retirement each keyed tag and subject hash, and of a removal each
        /// identifier, that a deletion record tells of or an item with no subject holds. What a
        /// summary entry restates with no subject.
        /// </summary>
        private readonly Dictionary<string, SortedSet<string>> _unnamed = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        /// <summary>
        /// What the items with no subject of the summary entries present restate, by kind: each
        /// keyed tag and subject hash, and each identifier of a removal.
        /// </summary>
        private readonly HashSet<(string Kind, string Carried)> _restatingUnnamed = new HashSet<(string Kind, string Carried)>();

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

        /// <summary>The statements, by the certificate each took away.</summary>
        private readonly Dictionary<string, List<StoreStatement>> _statementsOf =
            new Dictionary<string, List<StoreStatement>>(StringComparer.Ordinal);

        /// <summary>The records of kind `client`, by the text of their `certificate`.</summary>
        private readonly Dictionary<string, List<FoundRecord>> _admissionsOf =
            new Dictionary<string, List<FoundRecord>>(StringComparer.Ordinal);

        /// <summary>What a write that adds no version does with the versions present: what the room counts.</summary>
        private StoreReplaced _settled;

        /// <summary>The standing versions of each object, ascending by identifier, `STORE.md` §617.</summary>
        private readonly Dictionary<string, List<StoreNaming>> _standing = new Dictionary<string, List<StoreNaming>>(StringComparer.Ordinal);

        /// <summary>The identifiers of the lost versions of each object, ascending, `STORE.md` §617.</summary>
        private readonly Dictionary<string, IReadOnlyList<string>> _lost = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        /// <summary>
        /// The versions whose record and an item in force, or two items in force, restate them
        /// otherwise, each with every object those name: lost to each.
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> _differing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>Every object with a standing or a lost version, ascending.</summary>
        private IReadOnlyCollection<string> _objects = Nothing;

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

        /// <summary>
        /// Every record of kind `client` under the key, in force or not, whether or not it reads
        /// as an admission, `STORE.md` §615: each with the texts the core keeps of it, §658.
        /// </summary>
        public IReadOnlyList<FoundRecord> Admissions { get; private set; }

        /// <summary>
        /// Every record of kind `profile` under the key, in force or not and whatever its name,
        /// `STORE.md` §616: each with the texts the core keeps of it, §658.
        /// </summary>
        public IReadOnlyList<FoundRecord> Additions { get; private set; }

        /// <summary>
        /// The identifiers of the removals of this name that the reading holds, `STORE.md` §616:
        /// a record of kind `removed` by its identifier, an item `removed` by its `id`, and a
        /// deletion record that tells of a removal by its own identifier, a removal known in
        /// several of these ways counted once. None for a name no removal names; names are
        /// compared exactly.
        /// </summary>
        /// <remarks>
        /// Worked out as the reading is made, so that no caller builds an index in a hold.
        /// </remarks>
        public IReadOnlyCollection<string> RemovalsOf(string name)
        {
            HashSet<string> removals;
            return name != null && _removalsOf.TryGetValue(name, out removals) ? removals : Nothing;
        }

        /// <summary>
        /// The identifiers of the removals of this name that an addition of it names, `STORE.md`
        /// §616: those <see cref="RemovalsOf"/> holds, and every removal that a deletion record
        /// tells of once the name counts as known. None for a name nothing removes; names are
        /// compared exactly.
        /// </summary>
        /// <remarks>
        /// **The addition is what makes the name known.** A deletion record tells of a removal
        /// only of a name the file knows, `STORE.md` §623 step 2, so where the removal's record
        /// and every item restating it are gone, the reading holds no removal of a name that
        /// nothing else names, and the file the addition is written into tells of the removal by
        /// its deletion record, or by an item with no subject holding its identifier, `STORE.md`
        /// §611. It rules out an addition naming only what <see cref="RemovalsOf"/> holds, which
        /// its own write would put out of force.
        ///
        /// **Asked by whoever composes an addition**, over a reading whose key it composes under:
        /// the name is hashed under the key's bytes, as composing the addition hashes it, and not
        /// through the key's cache, which only the thread making the reading asks. Nothing is
        /// hashed where no deletion record and no item with no subject carries the tag of kind
        /// `removed`, and where the key's
        /// bytes were overwritten, as letting the reading go overwrites them, the call throws
        /// rather than answer with fewer removals.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The key's bytes were overwritten.</exception>
        public IReadOnlyCollection<string> RemovalsOnceKnown(string name)
        {
            var held = RemovalsOf(name);
            if (name == null || _removalDeletions.Count == 0)
            {
                return held;
            }

            // Bytes that no longer give the key's name were overwritten as the reading was let
            // go, and a name hashed under them finds nothing: refused, as an act composed under
            // them is, `StoreAct`.
            if (!string.Equals(StoreFormat.KeyName(Key.Bytes), Key.Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The removals an addition names were asked of a reading whose store key was overwritten.");
            }

            List<string> told;
            var subject = StoreId.SubjectOf(StoreFormat.Subject(Key.Bytes, StoreKinds.Find(StoreKinds.Removed).SubjectPrefix + name));
            if (!_removalDeletions.TryGetValue(subject, out told))
            {
                return held;
            }

            var removals = new HashSet<string>(held, StringComparer.Ordinal);
            removals.UnionWith(told);
            return removals;
        }

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
        /// Every summary entry that a deletion record tells of and that nothing in force replaces,
        /// ascending: summary entries deleted that nothing present stands in for, `STORE.md` §624.
        /// </summary>
        public IReadOnlyList<string> LostHeads { get; private set; }

        /// <summary>
        /// The values a kind restated by a member takes away by a deletion record alone, `STORE.md`
        /// §623 step 2: a client revoked or an authority retired that no record present and no item
        /// of a summary entry present names. None for a removal, which
        /// <see cref="RemovalsByDeletion"/> holds, and for any other kind.
        /// </summary>
        public IReadOnlyCollection<string> ByDeletion(string kind)
        {
            HashSet<string> values;
            return kind != null && _byDeletion.TryGetValue(kind, out values) ? values : Nothing;
        }

        /// <summary>
        /// The removals of names known by a deletion record alone, each its name and the deletion
        /// record's identifier: no record present and no item carries that identifier.
        /// </summary>
        public IReadOnlyCollection<KeyValuePair<string, string>> RemovalsByDeletion { get; private set; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        /// The revocations, the retirements and the removals of names a deletion record alone
        /// tells of whose subject the file names nowhere, each with every deletion record that
        /// tells of it, by kind and then by what the summary entry's item holds, `STORE.md` §611:
        /// none that an item with no subject carries.
        /// </summary>
        public IReadOnlyList<StoreUnnamed> UnnamedByDeletion { get; private set; } = Array.Empty<StoreUnnamed>();

        /// <summary>
        /// What a summary entry written over this reading restates with no subject for a kind,
        /// ascending, `STORE.md` §611: of a revocation and a retirement each keyed tag and subject
        /// hash, and of a removal each identifier, whose subject the file names nowhere. None for
        /// any other kind.
        /// </summary>
        public IReadOnlyCollection<string> Unnamed(string kind)
        {
            SortedSet<string> carried;
            return kind != null && _unnamed.TryGetValue(kind, out carried) ? carried : Nothing;
        }

        /// <summary>
        /// The certificates taken away by a deletion record alone, each the deletion record's
        /// identifier and the admission key it tells of, `STORE.md` §648: statements of their own
        /// whose content is nowhere.
        /// </summary>
        public IReadOnlyCollection<KeyValuePair<string, string>> WithdrawnByDeletion { get; private set; } = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        /// Every object that the deletion record of a damaged version would make refuse, once the
        /// seal has deleted the version with one, and that no lost version of the reading makes refuse,
        /// ascending, `STORE.md` §625: none where no version is damaged.
        /// </summary>
        public IReadOnlyList<string> RefusesAfter { get; private set; } = Array.Empty<string>();

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
        /// Every version present, a record of kind `version` or an item `version` of a summary
        /// entry present, by its identifier, `STORE.md` §623 step 3.
        /// </summary>
        public IReadOnlyDictionary<string, StoreNaming> Versions { get; private set; }

        /// <summary>The identifiers of the records present of a kind other than `version`.</summary>
        public ISet<string> Others { get; private set; }

        /// <summary>
        /// The versions of <see cref="Versions"/> whose `id` is an identifier, `StoreId`, by that
        /// identifier: every version a list or a deletion record can name, since an item's `id` is
        /// a text.
        /// </summary>
        internal IReadOnlyDictionary<StoreId, StoreNaming> VersionsById { get; private set; }

        /// <summary><see cref="Others"/>, as identifiers.</summary>
        internal ISet<StoreId> OthersById { get; private set; }

        /// <summary>
        /// The text of every object, of every type, that has a standing version or a lost version
        /// under the key, each once: what the rules of a reading are worked out from, `STORE.md`
        /// §617.
        /// </summary>
        public IReadOnlyCollection<string> Objects
        {
            get { return _objects; }
        }

        /// <summary>
        /// The standing versions of an object, `STORE.md` §617: in force, by a record in force or
        /// by an item of a summary entry in force, and named in `after` by no version in force of
        /// the same object; ascending by identifier, and none for an object nothing stands for.
        /// </summary>
        public IReadOnlyList<StoreNaming> VersionsOf(string objectText)
        {
            List<StoreNaming> standing;
            return objectText != null && _standing.TryGetValue(objectText, out standing)
                ? standing
                : (IReadOnlyList<StoreNaming>)Array.Empty<StoreNaming>();
        }

        /// <summary>
        /// The identifiers of the lost versions of an object, `STORE.md` §617, ascending: each
        /// deletion record telling of a version of the object that nothing in force names or
        /// carries, and each version present beside its own deletion record that nothing in force
        /// names or restates; none where nothing of the object is lost.
        /// </summary>
        public IReadOnlyList<string> LostOf(string objectText)
        {
            IReadOnlyList<string> lost;
            return objectText != null && _lost.TryGetValue(objectText, out lost) ? lost : Array.Empty<string>();
        }

        /// <summary>
        /// The versions present that a version in force names of its own object in `after`:
        /// replaced, so that none stands, `STORE.md` §617 and §658; what a write settles
        /// beside the versions its own versions name, `StoreReplaced`.
        /// </summary>
        internal ISet<string> ReplacedVersions { get; private set; }

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
        /// The statements that took this certificate away, `STORE.md` §648, each with the renewals
        /// it names and the certificate of origin it holds; none for a certificate no statement
        /// names. Worked out as the reading is made, so that following a chain of origins builds
        /// no index in a hold.
        /// </summary>
        public IReadOnlyList<StoreStatement> StatementsOf(string certificate)
        {
            List<StoreStatement> statements;
            return certificate != null && _statementsOf.TryGetValue(certificate, out statements)
                ? statements
                : (IReadOnlyList<StoreStatement>)Array.Empty<StoreStatement>();
        }

        /// <summary>
        /// The records of kind `client` under the key whose `certificate` is this text, in force or
        /// not and whether or not they read as admissions, in the order of
        /// <see cref="Admissions"/>; none where no record names it.
        /// </summary>
        public IReadOnlyList<FoundRecord> AdmissionsOf(string certificate)
        {
            List<FoundRecord> records;
            return certificate != null && _admissionsOf.TryGetValue(certificate, out records)
                ? records
                : (IReadOnlyList<FoundRecord>)Array.Empty<FoundRecord>();
        }

        /// <summary>
        /// Every record that takes something away, or holds a plugin back, and that no summary
        /// entry restates as its row of `STORE.md` §658 has it, in the order of
        /// <see cref="Records"/>, `STORE.md` §624: none in a store whose decisions are restated.
        /// </summary>
        /// <remarks>
        /// Every record is asked, also after the first that fails: a store that is whole asks every
        /// record either way, and a stopped store's dialog lists each.
        /// </remarks>
        public IReadOnlyList<FoundRecord> NotRestated()
        {
            var notRestated = new List<FoundRecord>();
            foreach (var record in Records.Where(record => record.Form.ReadsAsKind))
            {
                var row = record.Form.Row;
                switch (row.Restated)
                {
                    case StoreRestated.BySameMember:
                        var restated = (row.Name, record.Form.Member(row.RestatedBy), row.Name == StoreKinds.Removed ? record.Hex : null);
                        if (!Restating.Contains(restated) && !_restatingUnnamed.Contains((row.Name, CarriedOf(row, record.Hex))))
                        {
                            notRestated.Add(record);
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
                            notRestated.Add(record);
                        }

                        break;

                    case StoreRestated.ByFormat:
                        if (ItemFormat < record.Form.Number)
                        {
                            notRestated.Add(record);
                        }

                        break;

                    case StoreRestated.ByStatement:
                        // Only by an item of its identifier holding the same of every member.
                        if (!_itemStatements.Contains(StatementOf(record.Hex, record.Form.Members)))
                        {
                            notRestated.Add(record);
                        }

                        break;
                }
            }

            return notRestated;
        }

        /// <summary>
        /// What is in force under this key, from the records found under it and the identifiers
        /// the deletion records name.
        /// </summary>
        /// <param name="byTag">
        /// The identifiers the deletion records name that begin with a tag under a key found, by
        /// that tag as a number.
        /// </param>
        /// <param name="keptByDeletion">
        /// The deletion records that tell of a keep record under this key, each with the name of
        /// the key it sets aside, `STORE.md` §623 step 7: what a summary entry carries on for
        /// them, since none of them is a record present.
        /// </param>
        /// <param name="nodes">
        /// The identifier of every group and entry of the tree, walked only where a deletion record
        /// of a version is left that no node the objects name explains; null where there is none.
        /// </param>
        /// <param name="damagedVersions">
        /// The identifiers of the entries that are damage under the tag of kind `version`, each
        /// told of as the deletion record the seal leaves for it would be, for
        /// <see cref="RefusesAfter"/>; none, or null, for a store with no such damage.
        /// </param>
        public static StoreInForce Of(
            FoundKey key,
            IReadOnlyList<FoundRecord> records,
            IReadOnlyDictionary<int, List<StoreId>> byTag,
            IReadOnlyCollection<KeyValuePair<string, string>> keptByDeletion,
            Func<IEnumerable<byte[]>> nodes,
            IReadOnlyList<StoreId> damagedVersions = null)
        {
            var made = new StoreInForce { Key = key, Records = records };
            made.Heads = records.Where(record => record.Is(StoreKinds.Head)).ToList();
            made.HeadsInForce = made.Heads.Where(head => head.InForce).ToList();
            made.Items = made.Heads.SelectMany(head => head.Form.Items).ToList();
            made.Admissions = records.Where(record => record.Is(StoreKinds.Client)).ToList();
            made.Additions = records.Where(record => record.Is(StoreKinds.Profile)).ToList();
            foreach (var admission in made.Admissions)
            {
                var certificate = admission.Form.Member(StoreKinds.Certificate);
                if (certificate == null)
                {
                    continue;
                }

                List<FoundRecord> named;
                if (!made._admissionsOf.TryGetValue(certificate, out named))
                {
                    named = new List<FoundRecord>();
                    made._admissionsOf[certificate] = named;
                }

                named.Add(admission);
            }

            var byKind = ByKind(key, byTag);
            made.Told = Telling(key, byKind, StoreKinds.Head, StoreFormat.SubjectOf(StoreKinds.Find(StoreKinds.Head), null, out _)).ToList();
            made.Highest(key, byKind);
            made.Take(key, byKind);
            made.Restate();
            made.Index(key, byKind[StoreKinds.Version], nodes, damagedVersions ?? Array.Empty<StoreId>());

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
        private static Dictionary<string, List<StoreId>> ByKind(FoundKey key, IReadOnlyDictionary<int, List<StoreId>> byTag)
        {
            var byKind = new Dictionary<string, List<StoreId>>(StringComparer.Ordinal);
            foreach (var kind in StoreKinds.All)
            {
                List<StoreId> tagged;
                byKind[kind.Name] = byTag.TryGetValue(key.TagOf(kind), out tagged) ? tagged : new List<StoreId>();
            }

            return byKind;
        }

        /// <summary>
        /// The authorities that stand under a key, and nothing else of what is in force: what a
        /// word is answered under where several keys stand, `STORE.md` §656. The authority
        /// records in force, less those a retirement names by record, by an item of any summary
        /// entry present, or by a deletion record of its tag or an item with no subject holding
        /// that tag whose subject hash is that of an authority present: the `id` of an authority
        /// present is the only value hashed.
        /// </summary>
        public static IReadOnlyList<FoundRecord> Standing(
            FoundKey key, IReadOnlyList<FoundRecord> records, IReadOnlyDictionary<int, List<StoreId>> byTag)
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
        /// record, by an item of any summary entry present, or by a deletion record of its tag or
        /// an item with no subject holding that tag, `STORE.md` §611, whose subject hash is that of
        /// an `id` given, which are the only values hashed.
        /// </summary>
        public static ISet<string> Retired(
            FoundKey key, IReadOnlyList<FoundRecord> records, IReadOnlyDictionary<int, List<StoreId>> byTag, IEnumerable<string> ids)
        {
            var retired = new HashSet<string>(
                records.Where(record => record.Is(StoreKinds.Retired)).Select(record => record.Form.Member(StoreKinds.AuthorityMember)),
                StringComparer.Ordinal);
            var items = records
                .Where(record => record.Is(StoreKinds.Head))
                .SelectMany(head => head.Form.Items)
                .Where(item => item.Kind == StoreKinds.Retired)
                .ToList();
            retired.UnionWith(items.Select(item => item.Member(StoreKinds.AuthorityMember)).Where(authority => authority != null));

            // A deletion record of the kind's tag, and an item with no subject holding that tag,
            // each by its subject hash.
            List<StoreId> told;
            var retirement = StoreKinds.Find(StoreKinds.Retired);
            var tag = key.TagOf(retirement);
            var subjects = new HashSet<long>(byTag.TryGetValue(tag, out told) ? told.Select(identifier => identifier.Subject) : Enumerable.Empty<long>());
            foreach (var item in items.Where(item => item.Member(StoreKinds.AuthorityMember) == null))
            {
                StoreId carried;
                if (StoreId.TryParseTagAndSubject(item.Member(StoreKinds.Hash), out carried) && carried.Tag == tag)
                {
                    subjects.Add(carried.Subject);
                }
            }

            if (subjects.Count > 0)
            {
                retired.UnionWith(ids.Where(id => subjects.Contains(key.SubjectHash(StoreFormat.SubjectOf(retirement, id, out _)))));
            }

            return retired;
        }

        /// <summary>
        /// The deletion records of a kind that tell of this subject, each once: the subject's hash
        /// follows the tag. The subject is hashed only where there is a deletion record of the
        /// kind.
        /// </summary>
        private static IEnumerable<string> Telling(FoundKey key, IReadOnlyDictionary<string, List<StoreId>> byKind, string kind, string subject)
        {
            var identifiers = byKind[kind];
            if (identifiers.Count == 0)
            {
                return Array.Empty<string>();
            }

            var hash = key.SubjectHash(subject);
            return identifiers.Where(identifier => identifier.Subject == hash).Distinct().Select(identifier => identifier.ToString());
        }

        /// <summary>
        /// The highest format number and plugin version: of a version record, of the item `format`
        /// of a summary entry present, and, for the number alone, of a deletion record that tells
        /// of a version record by its tag, a number from 1 and the subject hash of that number,
        /// `STORE.md` §623 step 6.
        /// </summary>
        private void Highest(FoundKey key, IReadOnlyDictionary<string, List<StoreId>> byKind)
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
                var number = identifier.Number;
                var told = StoreFormat.SubjectOf(StoreKinds.Find(StoreKinds.Format), number.ToString(CultureInfo.InvariantCulture), out _);
                if (number >= 1 && identifier.Subject == key.SubjectHash(told))
                {
                    Format = Math.Max(Format, number);
                }
            }
        }

        /// <summary>
        /// What each kind a summary entry restates by a member takes away: its records, its items,
        /// and the deletion records and the items with no subject that tell of it about a subject
        /// the file knows, `STORE.md` §623 step 2; what tells of a subject the file names nowhere,
        /// `STORE.md` §611; the identifiers of the removals of each name; and the deletion records
        /// and the items with no subject under the tag of kind `removed`, by subject hash.
        /// </summary>
        private void Take(FoundKey key, IReadOnlyDictionary<string, List<StoreId>> byKind)
        {
            var unnamedDecisions = new List<StoreUnnamed>();
            foreach (var row in StoreKinds.All.Where(row => row.Restated == StoreRestated.BySameMember))
            {
                var values = new HashSet<string>(StringComparer.Ordinal);
                var removal = row.Name == StoreKinds.Removed;
                var held = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in Records.Where(record => record.Is(row.Name)))
                {
                    values.Add(record.Form.Member(row.RestatedBy));
                    held.Add(removal ? record.Hex : record.Form.Member(row.RestatedBy));
                    if (removal)
                    {
                        _removals.Add((record.Form.Member(row.RestatedBy), record.Hex));
                    }
                }

                var unnamedItems = new List<StoreItem>();
                foreach (var item in Items.Where(item => item.Kind == row.Name))
                {
                    if (item.Member(row.RestatedBy) == null)
                    {
                        unnamedItems.Add(item);
                        continue;
                    }

                    values.Add(item.Member(row.RestatedBy));
                    held.Add(removal ? item.Member(StoreKinds.Id) : item.Member(row.RestatedBy));
                    if (removal)
                    {
                        _removals.Add((item.Member(row.RestatedBy), item.Member(StoreKinds.Id)));
                    }
                }

                // The values of every subject the file knows by their keyed subject hash, made
                // only where a deletion record of the kind's tag or an item with no subject asks.
                var identifiers = byKind[row.Name];
                var known = identifiers.Count > 0 || unnamedItems.Count > 0
                    ? Hashed(key, row, KnownValues(row))
                    : new Dictionary<long, List<string>>();
                var unnamed = new SortedSet<string>(StringComparer.Ordinal);

                // An item with no subject tells of every subject the file knows whose keyed tag and
                // subject hash it holds, as a deletion record does, and is carried as it is where
                // it tells of none.
                var tag = unnamedItems.Count > 0 ? key.TagOf(row) : 0;
                foreach (var item in unnamedItems)
                {
                    var carried = item.Member(removal ? StoreKinds.Id : StoreKinds.Hash);
                    StoreId told;
                    List<string> named = null;
                    var tagged = (removal ? StoreId.TryParse(carried, out told) : StoreId.TryParseTagAndSubject(carried, out told)) && told.Tag == tag;
                    if (tagged && removal)
                    {
                        ListAt(_removalDeletions, told.Subject).Add(carried);
                    }

                    if (!tagged || !known.TryGetValue(told.Subject, out named))
                    {
                        unnamed.Add(carried);
                        continue;
                    }

                    foreach (var value in named)
                    {
                        values.Add(value);
                        held.Add(removal ? carried : value);
                        if (removal)
                        {
                            _removals.Add((value, carried));
                        }
                    }
                }

                // What a deletion record alone states: a removal by its identifier, which a record
                // and an item carry as well, and any other kind by its value; and of a subject the
                // file names nowhere, what an item with no subject is to hold.
                var byDeletion = new HashSet<string>(StringComparer.Ordinal);
                var removalsByDeletion = new List<KeyValuePair<string, string>>();
                var carriedByItem = new HashSet<string>(unnamed, StringComparer.Ordinal);
                var unnamedByDeletion = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                foreach (var identifier in identifiers.Distinct())
                {
                    var text = identifier.ToString();
                    List<string> named;
                    if (!known.TryGetValue(identifier.Subject, out named))
                    {
                        var carried = removal ? text : identifier.TagAndSubject;
                        unnamed.Add(carried);
                        if (!carriedByItem.Contains(carried) && !held.Contains(carried))
                        {
                            SortedSet<string> telling;
                            if (!unnamedByDeletion.TryGetValue(carried, out telling))
                            {
                                telling = new SortedSet<string>(StringComparer.Ordinal);
                                unnamedByDeletion[carried] = telling;
                            }

                            telling.Add(text);
                        }

                        continue;
                    }

                    foreach (var value in named)
                    {
                        values.Add(value);
                        if (removal)
                        {
                            _removals.Add((value, text));
                            if (!held.Contains(text))
                            {
                                removalsByDeletion.Add(new KeyValuePair<string, string>(value, text));
                            }
                        }
                        else if (!held.Contains(value))
                        {
                            byDeletion.Add(value);
                        }
                    }
                }

                _taken[row.Name] = values;
                _byDeletion[row.Name] = byDeletion;
                _unnamed[row.Name] = unnamed;
                unnamedDecisions.AddRange(unnamedByDeletion.Select(pair => new StoreUnnamed(row.Name, pair.Value.ToList())));
                if (removal)
                {
                    RemovalsByDeletion = removalsByDeletion.Distinct().ToList();
                }
            }

            UnnamedByDeletion = unnamedDecisions;

            foreach (var removal in _removals.Where(removal => removal.Name != null && removal.Id != null))
            {
                HashSet<string> of;
                if (!_removalsOf.TryGetValue(removal.Name, out of))
                {
                    of = new HashSet<string>(StringComparer.Ordinal);
                    _removalsOf[removal.Name] = of;
                }

                of.Add(removal.Id);
            }

            foreach (var identifier in byKind[StoreKinds.Removed].Distinct())
            {
                List<string> sharing;
                if (!_removalDeletions.TryGetValue(identifier.Subject, out sharing))
                {
                    sharing = new List<string>();
                    _removalDeletions[identifier.Subject] = sharing;
                }

                sharing.Add(identifier.ToString());
            }

            TakeStatements(key, byKind);
        }

        /// <summary>
        /// The statements that took certificates away, `STORE.md` §623 step 2 and §648, and the
        /// certificates they take away.
        /// </summary>
        private void TakeStatements(FoundKey key, IReadOnlyDictionary<string, List<StoreId>> byKind)
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
            var byDeletion = new List<KeyValuePair<string, string>>();
            foreach (var told in ToldValues(key, byKind[row.Name], row, () => KnownAdmissionKeys(row))
                         .Where(told => !answered.Contains(told.Identifier)))
            {
                _statements.Add((told.Identifier, told.Value, string.Empty, null));
                byDeletion.Add(new KeyValuePair<string, string>(told.Identifier, told.Value));
            }

            WithdrawnByDeletion = byDeletion.Distinct().ToList();

            _taken[row.Name] = new HashSet<string>(_statements.Select(statement => statement.Certificate), StringComparer.Ordinal);

            foreach (var statement in _statements.Where(statement => statement.Certificate != null))
            {
                List<StoreStatement> of;
                if (!_statementsOf.TryGetValue(statement.Certificate, out of))
                {
                    of = new List<StoreStatement>();
                    _statementsOf[statement.Certificate] = of;
                }

                of.Add(new StoreStatement(statement.Renewed, statement.From));
            }
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
                    // An item with no subject names none.
                    foreach (var item in Items.Where(item => item.Kind == about.Name && item.Member(about.RestatedBy) != null))
                    {
                        yield return item.Member(about.RestatedBy);
                    }
                }
            }
        }

        /// <summary>
        /// The admission keys the file knows for a certificate taken away, `STORE.md` §623 step
        /// 2: the `certificate` and the `from` of every record of kind `client`, and the
        /// `certificate`, the `from` and each key in `renewed` of every record and item of the
        /// kind, a text that is no admission key adding none.
        /// </summary>
        private IEnumerable<string> KnownAdmissionKeys(StoreKind row)
        {
            var named = Admissions
                .SelectMany(admission => new[] { admission.Form.Member(StoreKinds.Certificate), admission.Form.Member(StoreKinds.From) })
                .Where(value => value != null && StoreChecked.InForm(value, StoreForm.Hex));
            foreach (var value in named)
            {
                yield return value;
            }

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
            FoundKey key, IReadOnlyList<StoreId> identifiers, StoreKind row, Func<IEnumerable<string>> knownValues)
        {
            if (identifiers.Count == 0)
            {
                yield break;
            }

            var hashes = Hashed(key, row, knownValues());
            foreach (var identifier in identifiers.Distinct())
            {
                List<string> values;
                if (hashes.TryGetValue(identifier.Subject, out values))
                {
                    var text = identifier.ToString();
                    foreach (var value in values)
                    {
                        yield return (value, text);
                    }
                }
            }
        }

        /// <summary>
        /// The values of a subject the file knows for a kind, by the hash of their subject under
        /// the key, each value once: what a deletion record and an item with no subject are told by.
        /// </summary>
        private static Dictionary<long, List<string>> Hashed(FoundKey key, StoreKind row, IEnumerable<string> knownValues)
        {
            var hashes = new Dictionary<long, List<string>>();
            foreach (var value in new HashSet<string>(knownValues.Where(value => value != null), StringComparer.Ordinal))
            {
                ListAt(hashes, key.SubjectHash(StoreFormat.SubjectOf(row, value, out _))).Add(value);
            }

            return hashes;
        }

        /// <summary>
        /// What an item with no subject of this kind would hold to restate a record of it under
        /// this identifier: of a removal the identifier, and of any other kind its first 16
        /// characters, its keyed tag and subject hash, `STORE.md` §611.
        /// </summary>
        private static string CarriedOf(StoreKind row, string hex)
        {
            const int Carried = (StoreFormat.TagLength + StoreFormat.SubjectLength) * 2;
            return row.Name == StoreKinds.Removed || hex == null || hex.Length < Carried ? hex : hex.Substring(0, Carried);
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
                        // A removal is restated by its name and its identifier both, and an item
                        // with no subject restates by what it holds alone, `STORE.md` §611.
                        if (item.Member(row.RestatedBy) == null)
                        {
                            _restatingUnnamed.Add((row.Name, item.Member(row.Name == StoreKinds.Removed ? StoreKinds.Id : StoreKinds.Hash)));
                            break;
                        }

                        Restating.Add((row.Name, item.Member(row.RestatedBy), row.Name == StoreKinds.Removed ? item.Member(StoreKinds.Id) : null));
                        break;
                    case StoreRestated.ById:
                        Restating.Add((row.Name, item.Member(StoreKinds.Id), null));
                        break;
                }
            }

            // Every version present: what its record holds, or where no record carries it what an
            // item holds; whether its record is in force, and whether an item of a summary entry
            // in force carries it. Its text is its record's, the content its identifier was made
            // of, wherever a record carries it; an item in force that restates it otherwise, or a
            // second item in force that differs from the first, leaves no single text to read, and
            // the version is lost to every object they name, whatever order the entries are in.
            var versions = new Dictionary<string, StoreNaming>(StringComparer.Ordinal);
            foreach (var record in Records.Where(record => record.Is(StoreKinds.Version) && !versions.ContainsKey(record.Hex)))
            {
                versions[record.Hex] = new StoreNaming(
                    record.Hex,
                    record.Form.Member(StoreKinds.ObjectMember),
                    record.Form.Member(StoreKinds.After),
                    record.Form.Named,
                    record.Form.Member(StoreKinds.Lost),
                    record.Form.Settles,
                    true,
                    record.InForce,
                    WithId(record.Hex, record.Form.Members));
            }

            foreach (var head in Heads)
            {
                foreach (var item in head.Form.Items.Where(item => item.Kind == StoreKinds.Version))
                {
                    var id = item.Member(StoreKinds.Id);
                    var members = WithId(id, item.Members);
                    StoreNaming version;
                    if (!versions.TryGetValue(id, out version))
                    {
                        version = new StoreNaming(
                            id,
                            item.Member(StoreKinds.ObjectMember),
                            item.Member(StoreKinds.After),
                            item.Named,
                            item.Member(StoreKinds.Lost),
                            item.Settles,
                            false,
                            false,
                            members);
                        versions[id] = version;
                    }

                    if (!head.InForce)
                    {
                        continue;
                    }

                    if ((version.Record || version.ItemInForce) && !Alike(version.Members, members))
                    {
                        var refusing = SetAt(_differing, id);
                        refusing.Add(version.Object);
                        refusing.Add(item.Member(StoreKinds.ObjectMember));
                    }

                    version.CarriedInForce(members);
                }
            }

            Versions = versions;
            VersionsById = versions.Values
                .Where(version => version.Identified)
                .ToDictionary(version => version.Identifier, version => version);
            Others = new HashSet<string>(Records.Where(record => !record.Is(StoreKinds.Version)).Select(record => record.Hex), StringComparer.Ordinal);
            var othersById = new HashSet<StoreId>();
            foreach (var hex in Others)
            {
                StoreId other;
                if (StoreId.TryParse(hex, out other))
                {
                    othersById.Add(other);
                }
            }

            OthersById = othersById;

            ReplacedVersions = Replaced(versions.Values);
            _settled = StoreReplaced.Settle(this, Array.Empty<StoreRecord>());
        }

        /// <summary>
        /// What a version in force names of its own object is replaced, `STORE.md` §617: of each
        /// object, every version present that a version in force of that object names.
        /// </summary>
        /// <remarks>
        /// **Asked of the object, and from whichever side is shorter**: each version of the object
        /// asked of each list in force of it, a search of the list for each, or each name of those
        /// lists looked up among the versions present. A store after many renewals holds a list
        /// that grows with every renewal beside a version or two of each object, so the first asks
        /// a question for each version present and none for each name; a store a merge brought
        /// many versions of an object in force is asked the second, so nothing here costs more
        /// than a lookup for each name of each list in force.
        /// </remarks>
        private ISet<string> Replaced(IEnumerable<StoreNaming> versions)
        {
            var replaced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var same in versions.GroupBy(version => version.Object, StringComparer.Ordinal))
            {
                var inForce = same.Where(version => version.InForce).ToList();
                if (inForce.Count == 0)
                {
                    continue;
                }

                var present = same.Where(version => version.Identified).ToList();
                var names = inForce.Sum(version => (long)version.Named.Count);
                if ((long)present.Count * inForce.Count <= names)
                {
                    replaced.UnionWith(present
                        .Where(version => inForce.Any(naming => naming.Named.Names(version.Identifier)))
                        .Select(version => version.Id));
                    continue;
                }

                replaced.UnionWith(inForce
                    .SelectMany(naming => naming.Named.All)
                    .Select(VersionOf)
                    .Where(named => named != null && string.Equals(named.Object, same.Key, StringComparison.Ordinal))
                    .Select(named => named.Id));
            }

            return replaced;
        }

        /// <summary>The version present under this identifier, or null.</summary>
        private StoreNaming VersionOf(StoreId identifier)
        {
            StoreNaming named;
            return VersionsById.TryGetValue(identifier, out named) ? named : null;
        }

        /// <summary>
        /// What stands and what is lost of each object, whatever its type, `STORE.md` §617: the
        /// versions in force that no version in force of the same object names; each version
        /// present beside its own deletion record that nothing in force names of its object or
        /// restates; each version whose record and items restate it otherwise; and each object a
        /// deletion record under the tag of kind `version` tells of, where no version in force of
        /// that object names it.
        /// </summary>
        /// <remarks>
        /// **A version names what it replaces apart from what it settles**, `STORE.md` §617: in
        /// `after` the versions of its own object that stood with their content, and in `lost` the
        /// lost versions at its place it settles for its own object alone. So a deletion record a
        /// version present carries is that version's, and refuses its object unless a version in
        /// force of that object names it in either list; any other tells of every object at its
        /// place, the rows of every name of its tag and with 0 the object with no `/`, and refuses
        /// none of them where a version in force of any of them names it in `after`, since it is
        /// known whose version it was, and otherwise each of them but those a version in force of
        /// which names it in `lost`. A loss settled for a single name therefore leaves every other
        /// row of the tag refusing until a version of that row settles it too, and an edit of a row
        /// refuses no other row of its tag. A version at another place naming it answers nothing.
        /// </remarks>
        /// <param name="deleted">The deletion records under the tag of kind `version`.</param>
        /// <param name="nodes">The identifier of every group and entry of the tree, or null where there is none.</param>
        /// <param name="damaged">
        /// The entries that are damage under the tag of kind `version`, told of beside the deletion
        /// records as the deletion record the seal would leave for each, and lost to nothing in this reading.
        /// </param>
        private void Index(FoundKey key, IReadOnlyList<StoreId> deleted, Func<IEnumerable<byte[]>> nodes, IReadOnlyList<StoreId> damaged)
        {
            var losing = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            // A deletion record under which a version is present is that version's, and its object
            // judges it below; every other is told of at its place, `Deletions`. A damaged version
            // the file holds no deletion record of is told of in the same pass, so that the tree is
            // walked once at most, and kept apart afterwards.
            var held = new HashSet<StoreId>(deleted);
            var apart = damaged.Where(identifier => !held.Contains(identifier)).Distinct().ToList();
            var beside = new HashSet<StoreId>();
            var told = deleted.Count + apart.Count > 0 ? new Deletions(this, key) : null;
            if (told != null)
            {
                told.Tell(apart.Count == 0 ? deleted : deleted.Concat(apart).ToList(), nodes, beside, losing);
            }

            var afterDamage = new HashSet<string>(StringComparer.Ordinal);
            if (apart.Count > 0)
            {
                var damagedHex = new HashSet<string>(apart.Select(identifier => identifier.ToString()), StringComparer.Ordinal);
                foreach (var pair in losing.ToList())
                {
                    if (pair.Value.RemoveWhere(damagedHex.Contains) > 0)
                    {
                        afterDamage.Add(pair.Key);
                    }

                    if (pair.Value.Count == 0)
                    {
                        losing.Remove(pair.Key);
                    }
                }
            }

            foreach (var version in Versions.Values.Where(version => version.Object != null))
            {
                HashSet<string> refusing;
                if (_differing.TryGetValue(version.Id, out refusing))
                {
                    foreach (var objectText in refusing.Where(objectText => objectText != null))
                    {
                        Losing(losing, objectText).Add(version.Id);
                    }

                    continue;
                }

                if (ReplacedVersions.Contains(version.Id))
                {
                    continue;
                }

                if (version.InForce)
                {
                    ListAt(_standing, version.Object).Add(version);
                }
                else if (version.Identified && beside.Contains(version.Identifier) && (told == null || !told.Settles(version.Object, version.Identifier)))
                {
                    // Present beside its own deletion record: told of by its own object, with
                    // nothing hashed, and answered by a version in force of that object naming it
                    // in `after`, which makes it replaced above, or in `lost`.
                    Losing(losing, version.Object).Add(version.Id);
                }
            }

            foreach (var standing in _standing.Values)
            {
                standing.Sort((first, second) => string.CompareOrdinal(first.Id, second.Id));
            }

            foreach (var pair in losing)
            {
                _lost[pair.Key] = pair.Value.ToList();
            }

            _objects = _standing.Keys.Union(_lost.Keys, StringComparer.Ordinal).OrderBy(objectText => objectText, StringComparer.Ordinal).ToList();
            RefusesAfter = afterDamage.Where(objectText => !_lost.ContainsKey(objectText)).OrderBy(objectText => objectText, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The rows and the switches that deletion records whose content is nowhere tell of,
        /// `STORE.md` §617 and §620, each lost to every object it tells of where no version in
        /// force of those objects names it in `after`, but to an object a version in force of
        /// which names it in `lost`: a deletion record at the place of a version present first,
        /// then the nodes of the objects the file names, and the tree's only where a deletion
        /// record is still not told of.
        /// </summary>
        /// <remarks>
        /// **A deletion record tells of a row** by the subject hash of `rule:` and a node and the
        /// tag of a name in bytes 9 and 10, and so of the row of every name of that tag the file
        /// knows; with 0 there, of the object of that node with no `/` as well, where a version
        /// names it, since the ten keyed bytes tell neither apart, `STORE.md` §609. **It tells of a
        /// switch** by the subject hash of `switch:` and a node, with 0. A tag that meets no name
        /// the file knows tells of nothing, until the name is known. **Its place**, those ten
        /// bytes, is that of a version present of an object of the same node, type and tag
        /// wherever a version present begins with them, which is how every deletion record of the
        /// plugin's own writes is placed: the version that names it is at its place.
        ///
        /// **What it hashes, each kept with the key**, `FoundKey`: nothing for a deletion record at
        /// the place of a version present; for the rest a subject for each node tried, `rule:` for
        /// each and `switch:` only where such a deletion record holds 0. Once a deletion record
        /// lies at a node's rows, the tag of each name the file knows, read off the identifier of a
        /// row record where one shows the name and hashed otherwise. Whether a version of another
        /// object names a deletion record changes none of this.
        ///
        /// **What a deletion record costs**: a lookup of its identifier among the versions present,
        /// a lookup of its place, and a search of the lists in force of the objects it tells of
        /// until a list names it, `after` first and `lost` only where no `after` does, with no
        /// text made of it unless it is lost. A version holds a `lost` only where it settles a
        /// loss, so a list of `lost` is searched only where a version holds it. What a place
        /// tells of, each object and the lists in force of it, is worked out once for the place,
        /// the first time a deletion record lies there. So a store after many renewals, each a
        /// deletion record at the place of the version that names it, costs a hold a few lookups
        /// for each renewal ever made, for good, and builds nothing as large as the names its lists
        /// hold.
        ///
        /// **What it adds is bounded**: each deletion record is placed once, and added once to the
        /// set of each object it tells of, so K deletion records at a node's rows add at most K for
        /// each of the M names of their tag, M being 1 but where tags meet; the sets are built once
        /// for the reading.
        /// </remarks>
        private sealed class Deletions
        {
            private static readonly string Rules = StoreKinds.RuleType + StoreKinds.ObjectTypeEnd;
            private static readonly string Switches = StoreKinds.SwitchType + StoreKinds.ObjectTypeEnd;
            private static readonly string Renewals = StoreKinds.RenewType + StoreKinds.ObjectTypeEnd;
            private static readonly IReadOnlyList<StoreNames> NoLists = Array.Empty<StoreNames>();

            private readonly StoreInForce _inForce;
            private readonly FoundKey _key;

            /// <summary>The objects of the versions present, each once.</summary>
            private readonly HashSet<string> _objects;

            /// <summary>
            /// The lists in `after` of the versions in force of each object, which answer for every
            /// object at the place of what they name. A version restated otherwise answers for none.
            /// </summary>
            private readonly Dictionary<string, List<StoreNames>> _answering = new Dictionary<string, List<StoreNames>>(StringComparer.Ordinal);

            /// <summary>
            /// The lists in `lost` of the versions in force of each object, which settle for that
            /// object alone; only lists that name something are held.
            /// </summary>
            private readonly Dictionary<string, List<StoreNames>> _settling = new Dictionary<string, List<StoreNames>>(StringComparer.Ordinal);

            /// <summary>What a deletion record at a node's rows tells of, by the node and its number, once asked.</summary>
            private readonly Dictionary<(string Node, int Number), IReadOnlyList<ObjectTold>> _atRows = new Dictionary<(string Node, int Number), IReadOnlyList<ObjectTold>>();

            /// <summary>The names the file knows by their tags, worked out once a deletion record lies at a node's rows.</summary>
            private Dictionary<int, List<string>> _names;

            public Deletions(StoreInForce inForce, FoundKey key)
            {
                _inForce = inForce;
                _key = key;
               _objects = new HashSet<string>(
                    inForce.Versions.Values.Select(version => version.Object).Where(objectText => objectText != null), StringComparer.Ordinal);
                foreach (var version in inForce.Versions.Values.Where(version =>
                             version.Object != null && version.InForce && !inForce._differing.ContainsKey(version.Id)))
                {
                    ListAt(_answering, version.Object).Add(version.Named);
                    if (version.Settles.Count > 0)
                    {
                        ListAt(_settling, version.Object).Add(version.Settles);
                    }
                }
            }

            /// <summary>Whether a version in force of this object names this identifier in `lost`.</summary>
            public bool Settles(string objectText, StoreId identifier)
            {
                return Of(objectText).Settles(identifier);
            }

            /// <summary>
            /// Tells of each of these deletion records under the tag of kind `version`, putting
            /// into <paramref name="beside"/> each under which a version is present, which is
            /// that version's to judge, and into <paramref name="losing"/> each other by the
            /// objects it is lost to.
            /// </summary>
            public void Tell(IReadOnlyList<StoreId> deleted, Func<IEnumerable<byte[]>> nodes, ISet<StoreId> beside, Dictionary<string, SortedSet<string>> losing)
            {
                // Placed by a version present at its place, with nothing hashed: the first version,
                // in the order of the versions, whose identifier begins with the place's bytes.
                var places = new Dictionary<StoreId, Place>();
                foreach (var version in _inForce.Versions.Values.Where(version => version.Object != null))
                {
                    var place = StoreId.PlaceOf(version.Id);
                    if (place.HasValue && !places.ContainsKey(place.Value))
                    {
                        places[place.Value] = new Place(version.Object);
                    }
                }

                var unplaced = new List<StoreId>();
                foreach (var identifier in deleted)
                {
                    Place place;
                    if (_inForce.VersionsById.ContainsKey(identifier))
                    {
                        beside.Add(identifier);
                    }
                    else if (places.TryGetValue(identifier.Place, out place))
                    {
                        TellAt(place.Objects ?? (place.Objects = ToldAt(place.Object, identifier.Number)), identifier, losing);
                    }
                    else
                    {
                        unplaced.Add(identifier);
                    }
                }

                if (unplaced.Count > 0)
                {
                    TellUnplaced(unplaced, nodes, losing);
                }
            }

            /// <summary>
            /// The deletion records no version present places: each told of at the node of an
            /// object the file names whose subject hash it holds, then as an answer about a client
            /// key the file knows, and only where a deletion record is still not told of, at each
            /// node of the tree.
            /// </summary>
            private void TellUnplaced(IReadOnlyList<StoreId> unplaced, Func<IEnumerable<byte[]>> nodes, Dictionary<string, SortedSet<string>> losing)
            {
                var bySubject = new Dictionary<long, List<StoreId>>();
                foreach (var identifier in unplaced)
                {
                    ListAt(bySubject, identifier.Subject).Add(identifier);
                }

                var zero = unplaced.Any(identifier => identifier.Number == 0);
                var unmatched = new HashSet<long>(bySubject.Keys);
                var tried = new HashSet<string>(StringComparer.Ordinal);

                void Try(string node)
                {
                    if (!tried.Add(node))
                    {
                        return;
                    }

                    List<StoreId> telling;
                    var rows = _key.SubjectHash(Rules + node);
                    if (bySubject.TryGetValue(rows, out telling))
                    {
                        foreach (var identifier in telling)
                        {
                            TellAt(AtRows(node, identifier.Number), identifier, losing);
                        }

                        unmatched.Remove(rows);
                    }

                    if (!zero)
                    {
                        return;
                    }

                    var turned = _key.SubjectHash(Switches + node);
                    if (bySubject.TryGetValue(turned, out telling))
                    {
                        var told = new[] { Of(Switches + node) };
                        foreach (var identifier in telling.Where(identifier => identifier.Number == 0))
                        {
                            TellAt(told, identifier, losing);
                        }

                        unmatched.Remove(turned);
                    }
                }

                foreach (var objectText in _objects)
                {
                    if (objectText.StartsWith(Rules, StringComparison.Ordinal))
                    {
                        Try(NodeOf(objectText, Rules));
                    }
                    else if (objectText.StartsWith(Switches, StringComparison.Ordinal))
                    {
                        Try(objectText.Substring(Switches.Length));
                    }
                }

                // The answers to renewals about each client key the file knows, before the tree:
                // only a deletion record that holds 0 tells of one, `STORE.md` §618.
                if (zero && unmatched.Count > 0)
                {
                    foreach (var client in _inForce.KnownClients())
                    {
                        List<StoreId> telling;
                        var answered = Renewals + client;
                        var hash = _key.SubjectHash(answered);
                        if (bySubject.TryGetValue(hash, out telling))
                        {
                            var told = new[] { Of(answered) };
                            foreach (var identifier in telling.Where(identifier => identifier.Number == 0))
                            {
                                TellAt(told, identifier, losing);
                            }

                            unmatched.Remove(hash);
                        }
                    }
                }

                // The whole tree, once, where a deletion record's subject meets no node the
                // objects name: the nodes already tried are hashed no further.
                if (unmatched.Count > 0 && nodes != null)
                {
                    foreach (var node in nodes())
                    {
                        StoreId identifier;
                        Try(StoreId.TryOf(node, out identifier) ? identifier.ToString() : Hex.Of(node));
                    }
                }
            }

            /// <summary>
            /// What a deletion record at the place of a version of this object tells of: at a
            /// rule's place the rows of its node, and at a switch's or an answer's that object,
            /// whose subject is the whole object.
            /// </summary>
            private IReadOnlyList<ObjectTold> ToldAt(string objectText, int number)
            {
                if (objectText.StartsWith(Rules, StringComparison.Ordinal))
                {
                    return AtRows(NodeOf(objectText, Rules), number);
                }

                return objectText.StartsWith(Switches, StringComparison.Ordinal) || objectText.StartsWith(Renewals, StringComparison.Ordinal)
                    ? new[] { Of(objectText) }
                    : Array.Empty<ObjectTold>();
            }

            /// <summary>
            /// What a deletion record at a node's rows holding this number tells of: the row of
            /// every name the file knows whose tag the number is, and with 0 the object of the
            /// node with no `/` where a version names it.
            /// </summary>
            private IReadOnlyList<ObjectTold> AtRows(string node, int number)
            {
                IReadOnlyList<ObjectTold> told;
                if (_atRows.TryGetValue((node, number), out told))
                {
                    return told;
                }

                if (_names == null)
                {
                    _names = _inForce.NamesByTag(_key, _objects, Rules);
                }

                var objects = new List<ObjectTold>();
                List<string> tagged;
                if (_names.TryGetValue(number, out tagged))
                {
                    objects.AddRange(tagged.Select(name => Of(Rules + node + StoreFormat.ProfileStart + name)));
                }

                if (number == 0 && _objects.Contains(Rules + node))
                {
                    objects.Add(Of(Rules + node));
                }

                _atRows[(node, number)] = objects;
                return objects;
            }

            /// <summary>An object a deletion record tells of, with the lists in force of it.</summary>
            private ObjectTold Of(string objectText)
            {
                List<StoreNames> answering;
                List<StoreNames> settling;
                return new ObjectTold(
                    objectText,
                    _answering.TryGetValue(objectText, out answering) ? answering : NoLists,
                    _settling.TryGetValue(objectText, out settling) ? settling : NoLists);
            }

            /// <summary>
            /// Lost to each of these objects, the objects at a place: to none where a version in
            /// force of any of them names the record in `after`, and otherwise to each but those a
            /// version in force of which names it in `lost`.
            /// </summary>
            private static void TellAt(IReadOnlyList<ObjectTold> told, StoreId identifier, Dictionary<string, SortedSet<string>> losing)
            {
                for (var at = 0; at < told.Count; at++)
                {
                    if (told[at].Replaces(identifier))
                    {
                        return;
                    }
                }

                for (var at = 0; at < told.Count; at++)
                {
                    if (!told[at].Settles(identifier))
                    {
                        Losing(losing, told[at].Object).Add(identifier.ToString());
                    }
                }
            }

            /// <summary>The place of a version present, and what a deletion record there tells of, once asked.</summary>
            private sealed class Place
            {
                public Place(string objectText)
                {
                    Object = objectText;
                }

                public string Object { get; private set; }

                public IReadOnlyList<ObjectTold> Objects { get; set; }
            }
        }

        /// <summary>
        /// An object a deletion record tells of, and the lists in force of it: those in `after`,
        /// which answer for every object at their place, and those in `lost`, which settle for it.
        /// </summary>
        private sealed class ObjectTold
        {
            private readonly IReadOnlyList<StoreNames> _answering;
            private readonly IReadOnlyList<StoreNames> _settling;

            public ObjectTold(string objectText, IReadOnlyList<StoreNames> answering, IReadOnlyList<StoreNames> settling)
            {
                Object = objectText;
                _answering = answering;
                _settling = settling;
            }

            public string Object { get; private set; }

            /// <summary>Whether a version in force of the object names this identifier in `after`.</summary>
            public bool Replaces(StoreId identifier)
            {
                return Names(_answering, identifier);
            }

            /// <summary>Whether a version in force of the object names this identifier in `lost`.</summary>
            public bool Settles(StoreId identifier)
            {
                return Names(_settling, identifier);
            }

            private static bool Names(IReadOnlyList<StoreNames> lists, StoreId identifier)
            {
                for (var at = 0; at < lists.Count; at++)
                {
                    if (lists[at].Names(identifier))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// The names the file knows, `STORE.md` §617, by their tags: from every record of kind
        /// `profile` or `removed` present, in force or not, every item `removed` of a summary entry
        /// present, and every object of type `rule` that a version present names, each once. A
        /// tag is read off the identifier of a row record that shows the name, which its check
        /// holds to its content, and hashed under the key otherwise.
        /// </summary>
        private Dictionary<int, List<string>> NamesByTag(FoundKey key, IEnumerable<string> objects, string rules)
        {
            var known = new HashSet<string>(KnownValues(StoreKinds.Find(StoreKinds.Removed)).Where(name => name != null), StringComparer.Ordinal);
            foreach (var objectText in objects.Where(objectText => objectText.StartsWith(rules, StringComparison.Ordinal)))
            {
                var start = objectText.IndexOf(StoreFormat.ProfileStart, rules.Length);
                if (start >= 0)
                {
                    known.Add(objectText.Substring(start + 1));
                }
            }

            var shown = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var record in Records.Where(record => record.Is(StoreKinds.Version) && record.Form.Tagged != null))
            {
                shown[record.Form.Tagged] = NumberOf(record.Hex);
            }

            var names = new Dictionary<int, List<string>>();
            foreach (var name in known)
            {
                int tag;
                ListAt(names, shown.TryGetValue(name, out tag) ? tag : key.NameTag(name)).Add(name);
            }

            return names;
        }

        /// <summary>
        /// The client keys the file knows, `STORE.md` §618, each once and in the order first met:
        /// the `client` of every record of kind `client` present, in force or not, of every record
        /// and every item `revoked`, and the key in the object of type `renew` of every version
        /// present as a record or carried by an item of a summary entry in force. A text that is
        /// no client key, 64 lowercase hexadecimal characters, adds none.
        /// </summary>
        private IEnumerable<string> KnownClients()
        {
            var answers = StoreKinds.RenewType + StoreKinds.ObjectTypeEnd;
            var named = Admissions.Select(admission => admission.Form.Member(StoreKinds.ClientMember))
                .Concat(Records.Where(record => record.Is(StoreKinds.Revoked)).Select(record => record.Form.Member(StoreKinds.ClientMember)))
                .Concat(Items.Where(item => item.Kind == StoreKinds.Revoked).Select(item => item.Member(StoreKinds.ClientMember)))
                .Concat(Versions.Values
                    .Where(version => (version.Record || version.ItemInForce)
                                      && version.Object != null
                                      && version.Object.StartsWith(answers, StringComparison.Ordinal))
                    .Select(version => version.Object.Substring(answers.Length)));
            return named.Where(client => client != null && StoreChecked.InForm(client, StoreForm.Hex)).Distinct(StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether an item restates a version as these members hold it: its object, `after` and
        /// text alike, and `lost` alike or absent from both.
        /// </summary>
        private static bool Alike(IReadOnlyDictionary<string, string> held, IReadOnlyDictionary<string, string> restated)
        {
            string firstLost;
            string secondLost;
            held.TryGetValue(StoreKinds.Lost, out firstLost);
            restated.TryGetValue(StoreKinds.Lost, out secondLost);
            return string.Equals(firstLost, secondLost, StringComparison.Ordinal)
                   && new[] { StoreKinds.ObjectMember, StoreKinds.After, StoreKinds.Doc }.All(member =>
                   {
                       string first;
                       string second;
                       return held.TryGetValue(member, out first) && restated.TryGetValue(member, out second)
                              && string.Equals(first, second, StringComparison.Ordinal);
                   });
        }

        /// <summary>The node of an object of type `rule`: its text after `rule:` and before the first `/`.</summary>
        private static string NodeOf(string objectText, string rules)
        {
            var end = objectText.IndexOf(StoreFormat.ProfileStart, rules.Length);
            return end < 0 ? objectText.Substring(rules.Length) : objectText.Substring(rules.Length, end - rules.Length);
        }

        /// <summary>The list under a key, made the first time it is asked for.</summary>
        private static List<TValue> ListAt<TKey, TValue>(Dictionary<TKey, List<TValue>> lists, TKey key)
        {
            List<TValue> list;
            if (!lists.TryGetValue(key, out list))
            {
                list = new List<TValue>();
                lists[key] = list;
            }

            return list;
        }

        /// <summary>The set under a key, made the first time it is asked for.</summary>
        private static HashSet<string> SetAt(Dictionary<string, HashSet<string>> sets, string key)
        {
            HashSet<string> set;
            if (!sets.TryGetValue(key, out set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                sets[key] = set;
            }

            return set;
        }

        /// <summary>The identifiers lost to an object, ascending, made the first time it is asked for.</summary>
        private static SortedSet<string> Losing(Dictionary<string, SortedSet<string>> losing, string objectText)
        {
            SortedSet<string> set;
            if (!losing.TryGetValue(objectText, out set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                losing[objectText] = set;
            }

            return set;
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

            LostHeads = Told.Where(identifier => !replaced.Contains(identifier)).Distinct(StringComparer.Ordinal).OrderBy(identifier => identifier, StringComparer.Ordinal).ToList();

            var items = 1;
            foreach (var row in StoreKinds.All.Where(row => row.Restated == StoreRestated.BySameMember && row.Name != StoreKinds.Removed))
            {
                items += _taken[row.Name].Count;
            }

            items += _removals.Count;
            items += _statements.Count;

            // A version and a keep record are carried whole, each by its identifier, with the
            // members its item holds: from an item where a summary entry in force holds an item,
            // and from its record where none does. The versions carried are those a write that
            // adds no version restates, `StoreReplaced`: every standing version, and every version
            // replaced that only an item carries and that alone names a version.
            items += _settled.Restated.Count;

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

            items.AddRange(_settled.Restated.Select(id => Item(StoreKinds.Version, Versions[id].Members)));
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

            carried[id] = WithId(id, members);
        }

        /// <summary>An item's members: these members and the identifier as `id`.</summary>
        private static IReadOnlyDictionary<string, string> WithId(string id, IReadOnlyDictionary<string, string> members)
        {
            var item = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                item[member.Key] = member.Value;
            }

            item[StoreKinds.Id] = id;
            return item;
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

    /// <summary>
    /// A version present, `STORE.md` §623 step 3: what its record holds, or where no record
    /// carries it what an item of a summary entry present holds, and where it is in force.
    /// </summary>
    internal sealed class StoreNaming
    {
        /// <summary>A version of this identifier and object, naming what this `after` and this `lost` hold.</summary>
        /// <param name="named">
        /// The identifiers <paramref name="after"/> names, as the checked form that held it read
        /// them; read from <paramref name="after"/> where none is given, and none where it names
        /// none in form.
        /// </param>
        /// <param name="lost">The text of its `lost`, or null where it holds none.</param>
        /// <param name="settles">
        /// The identifiers <paramref name="lost"/> names, as the checked form that held it read
        /// them; read from <paramref name="lost"/> where none is given, and none where it names none.
        /// </param>
        public StoreNaming(
            string id,
            string objectText,
            string after,
            StoreNames named,
            string lost,
            StoreNames settles,
            bool record,
            bool recordInForce,
            IReadOnlyDictionary<string, string> members)
        {
            Id = id;
            StoreId identifier;
            Identified = StoreId.TryParse(id, out identifier);
            Identifier = identifier;
            Object = objectText;
            After = after;
            Named = named ?? StoreNames.Read(after) ?? StoreNames.None;
            Lost = lost;
            Settles = settles ?? StoreNames.Read(lost) ?? StoreNames.None;
            Record = record;
            RecordInForce = recordInForce;
            Members = members;
        }

        /// <summary>The version's identifier: its record's, or its item's `id`.</summary>
        public string Id { get; private set; }

        /// <summary>
        /// Whether <see cref="Id"/> is an identifier, 32 lowercase hexadecimal characters, which
        /// an item's `id` need not be: only such a version can be named or told of.
        /// </summary>
        internal bool Identified { get; private set; }

        /// <summary><see cref="Id"/> as an identifier, where <see cref="Identified"/>.</summary>
        internal StoreId Identifier { get; private set; }

        /// <summary>What it is a version of.</summary>
        public string Object { get; private set; }

        /// <summary>The identifiers it names, as its `after` holds them.</summary>
        public string After { get; private set; }

        /// <summary>The identifiers <see cref="After"/> names, read once with the form that holds it.</summary>
        internal StoreNames Named { get; private set; }

        /// <summary>
        /// The lost versions at its place it settles for its own object, as its `lost` holds them,
        /// or null where it holds none, `STORE.md` §617.
        /// </summary>
        public string Lost { get; private set; }

        /// <summary>The identifiers <see cref="Lost"/> names, read once with the form that holds it.</summary>
        internal StoreNames Settles { get; private set; }

        /// <summary>Whether a record of kind `version` present carries it, in force or not.</summary>
        public bool Record { get; private set; }

        /// <summary>Whether a record of kind `version` in force carries it.</summary>
        public bool RecordInForce { get; private set; }

        /// <summary>Whether an item of a summary entry in force carries it.</summary>
        public bool ItemInForce { get; private set; }

        /// <summary>Whether it is in force, by its record or by an item of a summary entry in force.</summary>
        public bool InForce
        {
            get { return RecordInForce || ItemInForce; }
        }

        /// <summary>
        /// Its members, `id` among them, as an item restating it holds them: its record's where a
        /// record carries it, and otherwise those of the first item of a summary entry in force
        /// that carries it, or of the first item present where none in force does.
        /// </summary>
        public IReadOnlyDictionary<string, string> Members { get; private set; }

        /// <summary>
        /// Marks it carried by an item of a summary entry in force, taking that item's members
        /// where neither a record nor an earlier item in force gave them.
        /// </summary>
        internal void CarriedInForce(IReadOnlyDictionary<string, string> members)
        {
            if (!Record && !ItemInForce)
            {
                Members = members;
            }

            ItemInForce = true;
        }
    }
}
