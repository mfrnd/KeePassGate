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
    /// </remarks>
    public sealed class AskedProfiles
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _removals;

        private AskedProfiles(
            bool served,
            IReadOnlyList<string> listed,
            IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> returning,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> removals)
        {
            Served = served;
            Listed = listed ?? Array.Empty<string>();
            Returning = returning ?? new Dictionary<string, IReadOnlyList<AdmissionRecord>>(StringComparer.Ordinal);
            _removals = removals ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            Removed = _removals.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Nothing read: no name on the list, nobody holding a name, and no removal, for a store
        /// that serves nothing.
        /// </summary>
        public static AskedProfiles None
        {
            get { return new AskedProfiles(false, null, null, null); }
        }

        /// <summary>
        /// Whether the reading served the authority asked under: where it did not, it holds no
        /// name and no removal, whatever the store holds.
        /// </summary>
        public bool Served { get; private set; }

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
        /// What a single reading holds of these profiles: the names on the list and the removals
        /// only where the store serves the authority asked under; the clients holding a name from
        /// the admissions the reading lists, which a store that cannot be judged lists none of.
        /// </summary>
        /// <param name="reading">The reading of a single hold, from which no key is taken.</param>
        /// <param name="served">Whether the store serves the authority the pairing is asked under.</param>
        /// <param name="profiles">The profiles asked for.</param>
        /// <param name="now">The moment a client's certificate is held to, as the gate holds it.</param>
        public static AskedProfiles Of(StoreReading reading, bool served, IReadOnlyCollection<string> profiles, DateTimeOffset now)
        {
            if (reading == null)
            {
                return None;
            }

            served = served && reading.InForce != null;
            var listed = served ? ProfileNames.Of(reading).Names : Array.Empty<string>();
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
            var removals = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            if (served)
            {
                foreach (var profile in off)
                {
                    removals[profile] = new HashSet<string>(reading.InForce.RemovalsOf(profile), StringComparer.Ordinal);
                }
            }

            return new AskedProfiles(
                served,
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

            return new AskedProfiles(Served, Listed, Returning, removals);
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

    /// <summary>How a pairing's approval dialog marks a profile asked for.</summary>
    public enum ProfileMark
    {
        /// <summary>On the list, with no mark.</summary>
        Listed,

        /// <summary>Off the list and marked "(new)": approving it adds it.</summary>
        New,

        /// <summary>Off the list and marked "(removed)": approving it puts it back.</summary>
        Removed,
    }
}
