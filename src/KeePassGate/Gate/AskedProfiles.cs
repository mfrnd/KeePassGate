using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Gate
{
    /// <summary>
    /// What a pairing's approval dialog is built from, read in a single hold over a single reading
    /// of the plugin's store, `STORE.md` §616: the names on the list, and of each name asked for that
    /// the list does not hold, the admitted clients whose admissions hold it and the removals of it.
    /// </summary>
    /// <remarks>
    /// **The removals are those the reading tells of the name**, `StoreInForce.RemovalsOf`: by the
    /// removal's record, by an item restating it, or by a deletion record of a name the file knows
    /// otherwise, tied to the name as the reading was made. No connection thread takes a key from a
    /// reading, so a removal that only a deletion record tells of, of a name the file does not know
    /// otherwise, is a removal of no name here. The write counts with the key,
    /// `StoreInForce.RemovalsOnceKnown`, finds it, and the approval is stale, `PAIRING.md` §311.
    /// The request puts the dialog to the operator again, and that dialog and every dialog of the
    /// request after it hold what its writes found as well, <see cref="With"/>, so that their
    /// approvals are not stale over the same removals.
    ///
    /// **Read and never changed**, so that the approval holding it compares with what the dialog
    /// was built from, whichever thread asks.
    ///
    /// **Read before the dialog is shown**, and true of the store when it was read and no later: a
    /// first dialog waits for the screen as long as another pairing or a renewal stands in front of
    /// the operator, and every dialog stands as long again, so a synchronize in that time still
    /// leaves a dialog over a store no longer served, its names as this reading found them, and the
    /// write answers its approval with the store's word, `PAIRING.md` §303.
    /// </remarks>
    public sealed class AskedProfiles
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _removals;

        private AskedProfiles(
            bool served,
            string word,
            IReadOnlyList<string> listed,
            IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> returning,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> removals)
        {
            Served = served;
            Word = word;
            Listed = listed ?? Array.Empty<string>();
            Returning = returning ?? new Dictionary<string, IReadOnlyList<AdmissionRecord>>(StringComparer.Ordinal);
            _removals = removals ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            Removed = _removals.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Nothing read, which an approval held without a dialog keeps: not served, no name on the
        /// list, nobody holding a name, and no removal.
        /// </summary>
        public static AskedProfiles None
        {
            get { return new AskedProfiles(false, null, null, null, null); }
        }

        /// <summary>
        /// A reading that did not serve the authority the pairing is asked under, which holds
        /// nothing a dialog may show, and the word its request is answered with.
        /// </summary>
        /// <param name="word">
        /// The word under `503`, as the write answers the same store: the store's word, `undecided`
        /// or `locked`; null where the authority asked under is no longer the authority served,
        /// which is answered `409 no invitation`.
        /// </param>
        public static AskedProfiles Unserved(string word)
        {
            return new AskedProfiles(false, word, null, null, null);
        }

        /// <summary>
        /// Whether the reading served the authority the pairing is asked under. A reading that did
        /// not holds nothing else, <see cref="Unserved"/>, and no dialog is built from it.
        /// </summary>
        /// <remarks>
        /// **Asked again here, after its connection was judged on it**: a connection to a store
        /// that holds the gate back is answered the store's word before any handler runs, and a
        /// synchronize bringing a second authority or a retirement of the authority, or the
        /// database being locked or closed, can come between that and this reading, or between a
        /// write that found an approval stale and the reading of the dialog shown again. A dialog
        /// built from a reading that does not serve would mark untruly every name it showed: a name
        /// on the list as new, as the reading holds no list, or as removed where an admission holds
        /// it, `PAIRING.md` §303.
        /// </remarks>
        public bool Served { get; private set; }

        /// <summary>
        /// Where the reading did not serve, the word under `503` that the write answers the same
        /// store with, and null where the authority asked under went, <see cref="Unserved"/>; null
        /// where it served.
        /// </summary>
        public string Word { get; private set; }

        /// <summary>The names on the list, in ordinal order.</summary>
        public IReadOnlyList<string> Listed { get; private set; }

        /// <summary>
        /// By name, every removal this holds of a name asked for off the list: what a dialog built
        /// from it holds, and what the dialog after it holds as well.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Removals
        {
            get { return _removals; }
        }

        /// <summary>
        /// How a dialog built from this marks a name asked for: on the list, removed where a
        /// removal of it is held or a client still holds it, and otherwise new.
        /// </summary>
        public ProfileMark MarkOf(string profile)
        {
            if (Listed.Contains(profile, StringComparer.Ordinal))
            {
                return ProfileMark.Listed;
            }

            return Removed.Contains(profile, StringComparer.Ordinal) || Returning.ContainsKey(profile ?? string.Empty)
                ? ProfileMark.Removed
                : ProfileMark.New;
        }

        /// <summary>
        /// Of each name asked for off the list that an admitted client holds, the clients holding
        /// it: approving the name gives it back to all of them.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> Returning { get; private set; }

        /// <summary>
        /// The names asked for off the list whose removal the dialog holds, in ordinal order: as
        /// the reading tells it, made in this copy or brought in by a synchronize, by its record,
        /// by an item of a summary entry or by a deletion record of a name the file knows
        /// otherwise, or as a write of the same request found it, <see cref="With"/>. What the
        /// dialog marks as removed whether or not a client holds them.
        /// </summary>
        /// <remarks>
        /// **Not a name whose removal only a deletion record tells of, of a name the file does not
        /// know otherwise**, until a write of the request found it: such a deletion record is tied
        /// to its name by a hash under the store key, which this reading does not take.
        /// </remarks>
        public IReadOnlyList<string> Removed { get; private set; }

        /// <summary>
        /// What a single reading holds of these profiles where the store serves the authority
        /// asked under: the names on the list, and of each name off it, the clients holding it
        /// from the admissions the reading lists and the removals of it.
        /// </summary>
        /// <param name="reading">
        /// The reading of a single hold, from which no key is taken, of a store that serves the
        /// authority the pairing is asked under; a reading that does not is <see cref="Unserved"/>.
        /// </param>
        /// <param name="profiles">The profiles asked for.</param>
        /// <param name="now">The moment a client's certificate is held to, as the gate holds it.</param>
        public static AskedProfiles Of(StoreReading reading, IReadOnlyCollection<string> profiles, DateTimeOffset now)
        {
            if (reading == null || reading.InForce == null)
            {
                throw new ArgumentException("A reading that serves an authority holds a store in force.", nameof(reading));
            }

            var listed = ProfileNames.Of(reading).Names;
            var named = new HashSet<string>(listed, StringComparer.Ordinal);
            var off = new HashSet<string>(
                (profiles ?? Array.Empty<string>()).Where(profile => !string.IsNullOrEmpty(profile) && !named.Contains(profile)),
                StringComparer.Ordinal);

            // The admissions the reading lists that admit their certificate, and not an admission
            // whose certificate has expired: it regains nothing, being refused on its dates before
            // its admission is read. Every admission once, for every name.
            var listing = StoreAdmissions.Listing(reading);
            var returning = new Dictionary<string, List<AdmissionRecord>>(StringComparer.Ordinal);
            foreach (var record in listing.Records.Where(record => listing.StandingOf(record) == AdmissionStanding.Admitted
                                                                   && !record.HasExpired(now)))
            {
                foreach (var profile in (record.Profiles ?? Array.Empty<string>()).Where(off.Contains).Distinct(StringComparer.Ordinal))
                {
                    List<AdmissionRecord> holding;
                    if (!returning.TryGetValue(profile, out holding))
                    {
                        holding = new List<AdmissionRecord>();
                        returning[profile] = holding;
                    }

                    holding.Add(record);
                }
            }

            // The removals each name has as the reading tells them, without the store key, which
            // no connection thread takes from a reading. Copied, as the names are, so that
            // nothing of the reading is held past its hold while an approval holds this.
            var removals = off.ToDictionary(
                profile => profile,
                profile => (IReadOnlyCollection<string>)new HashSet<string>(reading.InForce.RemovalsOf(profile), StringComparer.Ordinal),
                StringComparer.Ordinal);

            return new AskedProfiles(
                true,
                null,
                listed.ToList(),
                returning.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AdmissionRecord>)pair.Value, StringComparer.Ordinal),
                removals);
        }

        /// <summary>
        /// The same, holding as well the removals that the request's writes found with the store
        /// key, of each name asked for off the list: what the dialog shown again is built from, and
        /// what its approval is compared with. A name on the list takes none of them, since a
        /// dialog shows it on the list whatever was found of it.
        /// </summary>
        /// <param name="found">By name, the identifiers of the removals found.</param>
        public AskedProfiles With(IReadOnlyDictionary<string, IReadOnlyCollection<string>> found)
        {
            if (found.Count == 0)
            {
                return this;
            }

            var removals = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var pair in _removals)
            {
                IReadOnlyCollection<string> more;
                if (!found.TryGetValue(pair.Key, out more))
                {
                    removals[pair.Key] = pair.Value;
                    continue;
                }

                var held = new HashSet<string>(pair.Value, StringComparer.Ordinal);
                held.UnionWith(more);
                removals[pair.Key] = held;
            }

            return new AskedProfiles(Served, Word, Listed, Returning, removals);
        }

        /// <summary>
        /// Whether the dialog held this removal of a name asked for off the list: as its reading
        /// told it, or as a write of the same request found it. False for a name the reading did
        /// not ask about.
        /// </summary>
        public bool Held(string profile, string removal)
        {
            IReadOnlyCollection<string> of;
            return profile != null && removal != null && _removals.TryGetValue(profile, out of) && of.Contains(removal);
        }
    }

    /// <summary>
    /// The state a pairing's approval dialog shows of a profile asked for, in its column of the
    /// table of profiles.
    /// </summary>
    public enum ProfileMark
    {
        /// <summary>On the list, with no state shown.</summary>
        Listed,

        /// <summary>Off the list, and approving it adds it: `PairingApprovalForm.NewState`.</summary>
        New,

        /// <summary>Off the list, and approving it puts it back: `PairingApprovalForm.RemovedState`.</summary>
        Removed,
    }
}
