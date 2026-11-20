using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// One approval the operator gave that has not been written yet, and what it was given against.
    /// </summary>
    /// <remarks>
    /// Built by <see cref="ApprovalBook.Hold"/> and <see cref="ApprovalBook.HoldRenewal"/> and
    /// nowhere else. What the operator decided, the profiles and the lifetime, and the state they
    /// decided it against: the authority being served, which the write's plan holds the admission
    /// to, which of the names the decision rested on the database's list held as the dialog showed
    /// it, the removals of each approved name off the list that the dialog's reading held, and
    /// for a renewal the record being renewed.
    /// <see cref="Stale(IWriteGround, string, out string)"/> compares the last three with the
    /// database's own under the writer's lock, at the moment of writing.
    /// </remarks>
    public sealed class HeldApproval
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Nothing =
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

        private readonly HashSet<string> _asked;
        private readonly HashSet<string> _listed;
        private readonly AskedProfiles _shown;

        internal HeldApproval(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            IEnumerable<string> asked,
            IEnumerable<string> listed,
            AskedProfiles shown,
            string record,
            PairingMoment until)
        {
            Key = key;
            DatabaseId = databaseId;
            Profiles = profiles ?? Array.Empty<string>();
            Lifetime = lifetime;
            _asked = new HashSet<string>(asked ?? Array.Empty<string>(), StringComparer.Ordinal);
            _asked.UnionWith(Profiles);
            _listed = new HashSet<string>(listed ?? Array.Empty<string>(), StringComparer.Ordinal);
            _shown = shown ?? AskedProfiles.None;
            Record = record;
            Ends = until;
        }

        /// <summary>The digest of the request it approved.</summary>
        public string Key { get; private set; }

        /// <summary>The authority it was approved under.</summary>
        public string DatabaseId { get; private set; }

        /// <summary>The profiles the operator approved, once each.</summary>
        public IReadOnlyList<string> Profiles { get; private set; }

        /// <summary>The lifetime the operator picked.</summary>
        public TimeSpan Lifetime { get; private set; }

        /// <summary>
        /// What the dialog of a pairing's approval was built from: the names on the list, how
        /// each name asked for was marked, and the removals it held. None for a renewal's.
        /// </summary>
        internal AskedProfiles Shown
        {
            get { return _shown; }
        }

        /// <summary>
        /// The identifier of the entry of the admission a renewal renews, as it was when the
        /// operator approved, or null for a pairing: a record is written once, so the same entry
        /// under the same store key is the same content, `STORE.md` §627.
        /// </summary>
        public string Record { get; private set; }

        /// <summary>
        /// When it stops being held, which is the end of the pairing budget it was given in, as the
        /// wall clock read it then: what a `503 busy` sends as `heldUntil`.
        /// </summary>
        public DateTimeOffset Until
        {
            get { return Ends.Wall; }
        }

        /// <summary>The same end on both clocks, of which elapsed time decides.</summary>
        internal PairingMoment Ends { get; private set; }

        /// <summary>
        /// Whether a lock, a close or KeePass exiting took it away, rather than its time running out
        /// or a write taking it.
        /// </summary>
        public bool Cleared { get; internal set; }

        /// <summary>
        /// Whether another request asking for the same thing took it: a write took it out to be
        /// written, or the same request approved again took its place.
        /// </summary>
        public bool Taken { get; internal set; }

        /// <summary>
        /// Whether the database still holds what this was approved against, and where it does not,
        /// which parts it no longer holds, with the profiles they are about. Asked under the
        /// writer's lock, on KeePass's thread, as part of the write.
        /// </summary>
        /// <param name="reason">
        /// Where it no longer holds what this was approved against, every part, for the log: the
        /// record a renewal renews, or each profile that moved on the list and each removal of an
        /// approved profile that the reading taken before the dialog did not hold. Null where it
        /// still does.
        /// </param>
        /// <remarks>
        /// **The authority is not asked here**: the write's plan holds the admission to the
        /// authority that issued its certificate, which is the authority this was approved under,
        /// standing alone, before this is asked, and answers a write it leaves out for that.
        ///
        /// **The profiles**, name by name: each name the decision rested on has to be on the list
        /// exactly when the dialog showed it there. A name the operator took off the list since
        /// would be put back by the write, with every rule naming it and every client holding it,
        /// and a name put on it since is not the new name the dialog showed. A name the
        /// decision did not rest on changes nothing it decided, so a pairing landing in between and
        /// naming something else voids nothing.
        ///
        /// **The removals of each approved name off the list**: the write puts such a name back
        /// over every removal of it the store holds then, `StoreInForce.RemovalsOnceKnown`, and
        /// each has to be a removal the dialog held: its reading's, or a removal an earlier write
        /// of the same request found, `AskedProfiles.With`. A removal beyond those, which a
        /// synchronize can bring at any time while the dialog stands or while this is held, however
        /// long ago it was made, or which only a deletion record tells of and the dialog's reading
        /// therefore could not tie to the name, is a removal the operator was not shown, and a
        /// removal wins over an addition that did not see it, `STORE.md` §627. An approved name on
        /// the list is held to its place there alone.
        ///
        /// **Every part in a single pass**: a name that moved on the list does not stop the
        /// removals of the others being compared, so that the dialog shown again shows all of what
        /// the write found and its approval is not stale over a part it was never shown.
        ///
        /// **This is the read a record's names are written from**, `DESIGN.md` §124: what the
        /// handler read on its connection thread built the dialog and nothing more. A renewal's
        /// decision rests on every name its record carries, those the dialog offered and those it
        /// showed as dropped, so a dialog built from a reading the list changed after, which
        /// offered fewer names than the write's reading holds, is caught here rather than written
        /// as a record with fewer profiles.
        ///
        /// **The admission**, for a renewal: still admitting, and still the entry the operator
        /// approved against. A revocation or a certificate taken away that landed after the
        /// operator answered, or after the handler last read it, is what this catches, read from
        /// the store in the write's own turn, under the lock every such write is made under. A
        /// retirement of the authority it was issued under is answered by the write before this
        /// is asked, in its plan, or before it plans anything where several authorities stand.
        /// </remarks>
        public HeldStaleness Stale(IWriteGround ground, string admissionKey, out string reason)
        {
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> unseen;
            return Stale(ground, admissionKey, out reason, out unseen);
        }

        /// <summary>
        /// The same, with the removals the write found beside the reason: by name, the removals
        /// the store holds of each approved name off the list at the write beyond those the dialog
        /// held.
        /// </summary>
        /// <remarks>
        /// What the write knows that the dialog's reading may not: a deletion record that tells
        /// of a removal of a name the file does not know otherwise is tied to its name by a hash
        /// under the store key, which the reading of a connection does not take. **That holds of a
        /// name the dialog showed on the list as well**: a synchronize that takes it off the list
        /// can leave nothing naming it but such a deletion record, and a later reading would show
        /// it as new. The request that was approved holds these for the dialog it puts to the
        /// operator again, and for every dialog after it, `PairingHandler`.
        /// </remarks>
        public HeldStaleness Stale(
            IWriteGround ground,
            string admissionKey,
            out string reason,
            out IReadOnlyDictionary<string, IReadOnlyCollection<string>> unseen)
        {
            reason = null;
            unseen = Nothing;
            if (Record != null)
            {
                var admission = ground.Admission(admissionKey);
                if (admission == null || !string.Equals(admission.Entry, Record, StringComparison.Ordinal))
                {
                    reason = "the record it renews went, or changed, after the operator answered";
                    return HeldStaleness.Unpaired;
                }
            }

            var causes = new List<string>();
            var named = new HashSet<string>(StringComparer.Ordinal);
            var moving = new List<string>();
            foreach (var profile in _asked.OrderBy(profile => profile, StringComparer.Ordinal))
            {
                var listed = _listed.Contains(profile);
                if (ground.Named(profile))
                {
                    named.Add(profile);
                }

                if (named.Contains(profile) != listed)
                {
                    moving.Add(profile);
                    causes.Add("the profile " + profile + (listed
                        ? " is off the list, and the dialog showed it on the list"
                        : " is on the list, and the dialog showed it off the list"));
                }
            }

            // Each approved name off the list as the write reads it, however the dialog showed it:
            // of a name it showed on the list, no removal was held. Not for a renewal, which adds
            // no name and approves only names its dialog offered from the list, so that a name of
            // it off the list now has moved, which is its cause.
            var found = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            foreach (var profile in Profiles.Where(profile => Record == null && !named.Contains(profile)))
            {
                var beyond = ground.Removals(profile).Where(removal => !_shown.Held(profile, removal)).ToList();
                if (beyond.Count > 0)
                {
                    found[profile] = beyond;
                }
            }

            unseen = found;

            // For the log, a name that left the list is named as having moved, and its removals are
            // kept all the same; the removals of a name the dialog showed off the list are a cause
            // of their own.
            var removed = found.Keys.Except(moving, StringComparer.Ordinal).OrderBy(profile => profile, StringComparer.Ordinal).ToList();
            if (removed.Count > 0)
            {
                causes.Add((removed.Count == 1
                               ? "the store holds a removal of the profile " + removed[0]
                               : "the store holds removals of the profiles " + string.Join(", ", removed))
                           + " that the reading taken before the dialog did not hold");
            }

            if (causes.Count == 0)
            {
                return HeldStaleness.None;
            }

            reason = string.Join("; ", causes);
            return HeldStaleness.Profiles;
        }
    }

    /// <summary>What has changed under a held approval since the operator gave it.</summary>
    public enum HeldStaleness
    {
        /// <summary>Nothing: it can be written.</summary>
        None,

        /// <summary>The record a renewal renews has gone, been revoked or changed.</summary>
        Unpaired,

        /// <summary>
        /// An approved profile's place on the list is not what it was, or a removal of an approved
        /// profile off the list stands that the dialog's reading did not hold.
        /// </summary>
        Profiles,
    }

    /// <summary>
    /// The approvals the operator gave that could not be written when they were given, in plugin
    /// memory and nowhere else, beside <see cref="InvitationBook"/>.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-10-14. An approval used to be answered `503 busy` and thrown
    /// away when KeePass was busy a moment after it, so the client asked again and the operator
    /// was shown the dialog they had just answered. Now the approval is kept here and the client is
    /// told until when, and a retry asking for exactly the same thing is written without the
    /// dialog.
    ///
    /// **Keyed by a digest of exactly the approved request**: for a pairing the invitation, the
    /// name, the profiles asked for and the lifetime as written, and for a renewal the admission
    /// key and the lifetime as written. A retry that differs in anything is a different request,
    /// and the dialog comes up for it. The gate makes a client's key pair itself, so no key is on
    /// the wire to be part of it.
    ///
    /// **Single use**: the write that lands takes the entry, under the writer's lock, before it
    /// changes anything. **Bounded by the pairing budget** the request arrived in, by elapsed time
    /// rather than the system clock, `PAIRING.md` §306, and **cleared by a lock, a close of its
    /// database and KeePass exiting**, so an approval never outlives the session of the database
    /// it was given in, and an approval still settling when KeePass locks is never written after
    /// the unlock for a client whose connection the unlock closed.
    ///
    /// **An answer given across a clear is cleared with it**, which is what <see cref="Mark"/> is
    /// for. The handler takes a mark before the operator is asked and hands it to
    /// <see cref="Hold"/>, and an approval whose database was locked or closed in between is let
    /// go as it arrives rather than held. Otherwise an answer a lock or a close overtook between
    /// the dialog and the hold went into the book the clear had already emptied, and the same
    /// request asked again once the database was open again was written without the dialog. An
    /// answer given on a dialog left standing over a closed database went the same way, until the
    /// operator's ruling of 2026-11-08 had a close take the dialog with it, as a lock does.
    ///
    /// **Every approval passes through here**, the ones written at once included, because the
    /// same three things decide whether any of them may still be written: the entry is still
    /// held, the database still holds what it was approved against, and, for a pairing, the
    /// invitation is still unspent.
    ///
    /// **An approval its write finds stale is used up here like any other**, and what that write
    /// found is no part of the book: the request that was approved puts the dialog to the operator
    /// again itself, and what it carries from one dialog to the next is its own,
    /// `PairingHandler`. A held approval is the only approval a later request can find stale, and
    /// that request is then the request putting the dialog to the operator again.
    /// </remarks>
    public sealed class ApprovalBook : IApprovalScreen
    {
        private readonly PairingClock _clock;
        private readonly object _lock = new object();
        private readonly Dictionary<string, HeldApproval> _held =
            new Dictionary<string, HeldApproval>(StringComparer.Ordinal);

        /// <summary>
        /// For each database a close has let go of, the clear that did it last, which grows by
        /// one entry for each database closed in the session.
        /// </summary>
        private readonly Dictionary<string, long> _closed = new Dictionary<string, long>(StringComparer.Ordinal);

        /// <summary>How many clears there have been, which is what a mark counts.</summary>
        private long _clears;

        /// <summary>The last clear that let go of every database's approvals.</summary>
        private long _clearedAll;

        public ApprovalBook()
            : this(PairingClock.Default)
        {
        }

        /// <summary>
        /// A book letting approvals go by this wall clock, or by the system's for null.
        /// </summary>
        public ApprovalBook(Func<DateTimeOffset> clock)
            : this(PairingClock.For(clock))
        {
        }

        /// <summary>The same, by a clock whose two readings a test sets apart.</summary>
        internal ApprovalBook(PairingClock clock)
        {
            _clock = clock ?? PairingClock.Default;
        }

        /// <summary>How many approvals are held now.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    Sweep();
                    return _held.Count;
                }
            }
        }

        /// <summary>The key a pairing request is held under.</summary>
        public static string Pairing(
            string invitationId, string name, IReadOnlyList<string> profiles, string lifetime)
        {
            var parts = new List<string> { "pairing", invitationId, name, lifetime ?? string.Empty };
            parts.AddRange(profiles ?? Array.Empty<string>());
            return Of(parts);
        }

        /// <summary>The key a renewal request is held under.</summary>
        public static string Renewal(string admissionKey, string lifetime)
        {
            return Of(new[] { "renewal", admissionKey, lifetime ?? string.Empty });
        }

        /// <summary>Lowercase hexadecimal SHA-256 of a text's UTF-8 bytes.</summary>
        internal static string Digest(string text)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return string.Concat(hash.Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// Each part with its length before it, so that no two lists of parts make the same text.
        /// </summary>
        private static string Of(IEnumerable<string> parts)
        {
            var text = new StringBuilder();
            foreach (var value in parts.Select(part => part ?? string.Empty))
            {
                text.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(value)
                    .Append(';');
            }

            return Digest(text.ToString());
        }

        /// <summary>
        /// Where the book stands, taken before the operator is asked and handed back to
        /// <see cref="Hold"/> with their answer.
        /// </summary>
        public long Mark()
        {
            lock (_lock)
            {
                return _clears;
            }
        }

        /// <summary>
        /// Holds what the operator approved for a pairing, until <paramref name="until"/>,
        /// replacing anything held under the same key, unless its database was locked or closed
        /// since <paramref name="mark"/>.
        /// </summary>
        /// <param name="shown">
        /// What the dialog was built from: which of the approved names the list held, and the
        /// removals of each approved name off it that the reading held.
        /// </param>
        /// <param name="mark">
        /// What <see cref="Mark"/> answered before the operator was asked. An approval whose
        /// database a lock or a close let go of since then is answered as an approval that clear
        /// took away: <see cref="HeldApproval.Cleared"/>, and not held.
        /// </param>
        public HeldApproval Hold(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            AskedProfiles shown,
            PairingMoment until,
            long mark)
        {
            shown = shown ?? AskedProfiles.None;
            var listed = (profiles ?? Array.Empty<string>()).Where(shown.Listed.Contains);
            return Held(new HeldApproval(key, databaseId, profiles, lifetime, null, listed, shown, null, until), mark);
        }

        /// <summary>
        /// Holds what the operator approved for a renewal, as <see cref="Hold"/> holds a pairing's,
        /// with the entry of the admission it renews, which the write compares.
        /// </summary>
        /// <param name="record">
        /// The identifier of the admission's entry, `STORE.md` §627. Required: an approval held
        /// without it would be compared as a pairing's, with no admission asked about.
        /// </param>
        /// <param name="asked">
        /// The names the decision rested on beyond the approved ones, which the write compares as
        /// well: a renewal's record carries names the operator was shown as dropped and did not
        /// approve.
        /// </param>
        /// <exception cref="ArgumentException">No entry was given.</exception>
        public HeldApproval HoldRenewal(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            IEnumerable<string> listed,
            string record,
            PairingMoment until,
            long mark,
            IEnumerable<string> asked)
        {
            if (string.IsNullOrEmpty(record))
            {
                throw new ArgumentException("A renewal is held with the entry of the admission it renews.", nameof(record));
            }

            return Held(new HeldApproval(key, databaseId, profiles, lifetime, asked, listed, null, record, until), mark);
        }

        /// <summary>
        /// Holds an approval, unless its database was locked or closed since the mark.
        /// </summary>
        private HeldApproval Held(HeldApproval held, long mark)
        {
            var databaseId = held.DatabaseId;
            var key = held.Key;
            lock (_lock)
            {
                if (ClearedSince(databaseId, mark))
                {
                    held.Cleared = true;
                    return held;
                }

                HeldApproval replaced;
                if (_held.TryGetValue(key, out replaced))
                {
                    replaced.Taken = true;
                }

                _held[key] = held;
            }

            return held;
        }

        /// <summary>
        /// Whether KeePass locked, or closed this database, since <paramref name="mark"/>: what a
        /// pairing request asks before it puts the dialog to the operator again, so that a lock or
        /// a close between two of its dialogs is answered as a lock is.
        /// </summary>
        public bool Cleared(string databaseId, long mark)
        {
            lock (_lock)
            {
                return ClearedSince(databaseId, mark);
            }
        }

        /// <summary>
        /// Whether a clear since this mark let go of this database's approvals. Called with the
        /// lock held.
        /// </summary>
        private bool ClearedSince(string databaseId, long mark)
        {
            long closed;
            return _clearedAll > mark
                   || (_closed.TryGetValue(databaseId ?? string.Empty, out closed) && closed > mark);
        }

        /// <summary>
        /// The approval held for this request on this database, or null when there is none or its
        /// time is up.
        /// </summary>
        public HeldApproval Find(string key, string databaseId)
        {
            lock (_lock)
            {
                Sweep();

                HeldApproval held;
                return _held.TryGetValue(key ?? string.Empty, out held)
                       && string.Equals(held.DatabaseId, databaseId, StringComparison.Ordinal)
                    ? held
                    : null;
            }
        }

        /// <summary>Whether this approval is still held.</summary>
        public bool Holds(HeldApproval held)
        {
            lock (_lock)
            {
                Sweep();

                HeldApproval found;
                return held != null && _held.TryGetValue(held.Key, out found) && ReferenceEquals(found, held);
            }
        }

        /// <summary>
        /// Takes this approval out to be written, and says whether it was still there to take. A
        /// write that takes it uses it up, whatever comes of the write.
        /// </summary>
        public bool Take(HeldApproval held)
        {
            lock (_lock)
            {
                if (!Holds(held))
                {
                    return false;
                }

                held.Taken = true;
                _held.Remove(held.Key);
                return true;
            }
        }

        /// <summary>Lets go of every approval held, which a lock and an exit do.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _clearedAll = ++_clears;
                Clear(held => true);
            }
        }

        /// <summary>A lock: nothing held survives it.</summary>
        void IApprovalScreen.Locked()
        {
            Clear();
        }

        /// <summary>
        /// A database closed for real, which takes its own approvals with it as a lock does, and
        /// leaves every other database's, the operator's ruling R1.
        /// </summary>
        /// <remarks>
        /// It took every approval, so closing a database told a pairing approved for another
        /// database that its database was locked, and the operator had to answer it again. Null
        /// is a close whose database nothing could name, and lets every approval go, as a close
        /// used to.
        /// </remarks>
        void IApprovalScreen.Closed(IReadOnlyCollection<string> databaseIds)
        {
            if (databaseIds == null)
            {
                Clear();
                return;
            }

            lock (_lock)
            {
                var clear = ++_clears;
                foreach (var databaseId in databaseIds.Where(databaseId => databaseId != null))
                {
                    _closed[databaseId] = clear;
                }

                Clear(held => databaseIds.Contains(held.DatabaseId, StringComparer.Ordinal));
            }
        }

        /// <summary>Lets go of every approval held that this chooses.</summary>
        private void Clear(Func<HeldApproval, bool> chosen)
        {
            lock (_lock)
            {
                foreach (var pair in _held.Where(pair => chosen(pair.Value)).ToList())
                {
                    pair.Value.Cleared = true;
                    _held.Remove(pair.Key);
                }
            }
        }

        /// <summary>Drops whatever has run out. Called with the lock held.</summary>
        private void Sweep()
        {
            foreach (var key in _held.Where(pair => _clock.HasReached(pair.Value.Ends)).Select(pair => pair.Key).ToList())
            {
                _held.Remove(key);
            }
        }
    }
}
