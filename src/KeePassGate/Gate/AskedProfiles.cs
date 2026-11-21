using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Gate
{
    /// <summary>
    /// What a pairing's approval dialog is built from, read from a single reading of the plugin's
    /// store made afresh on KeePass's thread, `STORE.md` §616: the names on the list, and of each
    /// name asked for that the list does not hold, the admitted clients whose admissions hold it
    /// and the removals of it.
    /// </summary>
    /// <remarks>
    /// **The removals are counted with the store key**, `StoreInForce.RemovalsOnceKnown`, as the
    /// write counts them: by the removal's record, by an item restating it, or by a deletion record,
    /// a deletion record of a name the file does not know otherwise among them, tied to the name by
    /// its hash under the key. The reading is made in a single turn of KeePass's thread, as the
    /// writer's turn reads, and let go before this leaves the turn, which overwrites the bytes of
    /// the key, `CA.md` §208: only names and identifiers are kept here. So a dialog shown again
    /// holds every removal a write of its request found, for as long as the store holds it, and
    /// its approval is not stale over the same removals; no write finds a removal the store no
    /// longer holds.
    ///
    /// **Read and never changed**, so that the approval holding it compares with what the dialog
    /// showed, whichever thread asks.
    ///
    /// **Read as the dialog comes up, and again on each tick while it stands**, `PAIRING.md` §303:
    /// what the dialog shows is true of the store when the dialog last read it, and a change a
    /// tick finds waits for Refresh.
    /// </remarks>
    public sealed class AskedProfiles
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> _removals;

        private AskedProfiles(
            bool served,
            string word,
            IReadOnlyList<string> listed,
            IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> returning,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> removals,
            bool failed = false)
        {
            Served = served;
            Word = word;
            Failed = failed;
            Listed = listed ?? Array.Empty<string>();
            Returning = returning ?? new Dictionary<string, IReadOnlyList<AdmissionRecord>>(StringComparer.Ordinal);
            _removals = removals ?? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            Removed = _removals.Where(pair => pair.Value.Count > 0).Select(pair => pair.Key).OrderBy(name => name, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// A reading that failed, which a later reading may make: nothing to show, and no word,
        /// since what the store holds is not known. The dialog that a failed reading comes up for
        /// is not put up, and a dialog standing over a failed reading takes no Approve until a
        /// reading serves.
        /// </summary>
        public static AskedProfiles Unreadable()
        {
            return new AskedProfiles(false, null, null, null, null, true);
        }

        /// <summary>Whether the reading failed, <see cref="Unreadable"/>.</summary>
        public bool Failed { get; private set; }

        /// <summary>
        /// Whether a dialog standing over this reading ends: the store read and no longer serves
        /// the authority the pairing arrived under, or the database is gone, a state no later tick
        /// can be counted on to leave. Answered with the store's word,
        /// `PairingApproval.NoLongerServed`, as before a dialog.
        /// </summary>
        public bool Ends
        {
            get { return !Served && !Failed; }
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
        /// not, or that failed, holds nothing else, <see cref="Unserved"/> and
        /// <see cref="Unreadable"/>, and no dialog is built from it.
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
        /// where it served or failed.
        /// </summary>
        public string Word { get; private set; }

        /// <summary>The names on the list, in ordinal order.</summary>
        public IReadOnlyList<string> Listed { get; private set; }

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
        /// the reading tells it with the store key, made in this copy or brought in by a
        /// synchronize, by its record, by an item of a summary entry or by a deletion record. What
        /// the dialog marks as restored whether or not a client holds them.
        /// </summary>
        public IReadOnlyList<string> Removed { get; private set; }

        /// <summary>
        /// What a single reading holds of these profiles where the store serves the authority
        /// asked under: the names on the list, and of each name off it, the clients holding it
        /// from the admissions the reading lists and the removals of it.
        /// </summary>
        /// <param name="reading">
        /// A reading made afresh in this turn of KeePass's thread, whose store key is still whole,
        /// of a store that serves the authority the pairing is asked under; a reading that does not
        /// serve is <see cref="Unserved"/>. Let go by its caller once this returns.
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

            var returning = Holding(reading, off, now);

            // The removals each name has, counted with the store key as the write counts them, so
            // that a removal only a deletion record tells of is tied to its name here as there.
            // Copied, as the names are, so that nothing of the reading is held past its turn while
            // an approval holds this: its caller lets it go, which overwrites the key's bytes.
            var removals = off.ToDictionary(
                profile => profile,
                profile => (IReadOnlyCollection<string>)new HashSet<string>(reading.InForce.RemovalsOnceKnown(profile), StringComparer.Ordinal),
                StringComparer.Ordinal);

            return new AskedProfiles(true, null, listed.ToList(), returning, removals);
        }

        /// <summary>
        /// Of each of these names, the admissions that regain it were it put back on the list: the
        /// admissions the reading lists that admit their certificate and whose certificate has not
        /// expired. What a dialog shows under "Regained by", and what the write compares with it,
        /// `IWriteGround.Regaining`.
        /// </summary>
        /// <remarks>
        /// **An admission whose certificate has expired regains nothing**, being refused on its
        /// dates before its admission is read. Every admission once, for every name; a name no
        /// admission holds is absent.
        /// </remarks>
        internal static IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> Holding(
            StoreReading reading, IEnumerable<string> names, DateTimeOffset now)
        {
            var wanted = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal);
            var listing = StoreAdmissions.Listing(reading);
            var holding = new Dictionary<string, List<AdmissionRecord>>(StringComparer.Ordinal);
            foreach (var record in listing.Records.Where(record => listing.StandingOf(record) == AdmissionStanding.Admitted
                                                                   && !record.HasExpired(now)))
            {
                foreach (var profile in (record.Profiles ?? Array.Empty<string>()).Where(wanted.Contains).Distinct(StringComparer.Ordinal))
                {
                    List<AdmissionRecord> holders;
                    if (!holding.TryGetValue(profile, out holders))
                    {
                        holders = new List<AdmissionRecord>();
                        holding[profile] = holders;
                    }

                    holders.Add(record);
                }
            }

            return holding.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<AdmissionRecord>)pair.Value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Of each of these names, the client keys of the clients regaining the name were it put
        /// back on the list, from a single listing of the admissions, as <see cref="Holding"/>
        /// lists the admissions holding it: what a write compares with the clients its dialog
        /// showed, `IWriteGround.Regaining`. A name no client regains is absent.
        /// </summary>
        internal static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Regaining(
            StoreReading reading, IEnumerable<string> names, DateTimeOffset now)
        {
            return Holding(reading, names, now).ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<string>)pair.Value.Select(record => record.ClientKey).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether the dialog showed this client regaining this name asked for off the list, by
        /// the client's key, `CA.md` §223: a client is its client key, however many of its
        /// certificates hold the name. False for a name the reading did not ask about.
        /// </summary>
        public bool Regains(string profile, string clientKey)
        {
            IReadOnlyList<AdmissionRecord> holding;
            return profile != null
                   && Returning.TryGetValue(profile, out holding)
                   && holding.Any(record => string.Equals(record.ClientKey, clientKey, StringComparison.Ordinal));
        }

        /// <summary>
        /// Whether the dialog held this removal of a name asked for off the list, as its reading
        /// told it. False for a name the reading did not ask about.
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
