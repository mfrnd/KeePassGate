using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// One approval the operator gave that has not been written yet, and what it was given against.
    /// </summary>
    /// <remarks>
    /// Built by <see cref="ApprovalBook.Hold"/> and nowhere else. What the operator decided, the
    /// profiles and the lifetime, and the state they decided it against: the authority being
    /// served, which of the names the decision rested on the database's list held as the dialog
    /// showed it, and for a renewal the record being renewed. <see cref="Stale"/> compares that
    /// state with the database's own under the writer's lock, at the moment of writing.
    /// </remarks>
    public sealed class HeldApproval
    {
        private readonly HashSet<string> _asked;
        private readonly HashSet<string> _listed;

        internal HeldApproval(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            IEnumerable<string> asked,
            IEnumerable<string> listed,
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
        /// The digest of the record a renewal renews, as it was when the operator approved, or null
        /// for a pairing.
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
        /// Why the database no longer holds what this was approved against, or null when it still
        /// does. Asked under the writer's lock, on KeePass's thread, as part of the write.
        /// </summary>
        /// <remarks>
        /// **The authority** has to be the one served and not retired: a record written under any other
        /// is one that chains to nothing the gate serves.
        ///
        /// **The profiles**, name by name: each name the decision rested on has to be on the list
        /// exactly when the dialog showed it there. A name the operator took off the list since
        /// would be put back by the write, with every rule naming it and every client holding it,
        /// and a name put on it since is not the "(new)" name the dialog showed. A name the
        /// decision did not rest on changes nothing it decided, so a pairing landing in between and
        /// naming something else voids nothing.
        ///
        /// **This is the read a record's names are written from**, `DESIGN.md` §124: what the
        /// handler read on its connection thread built the dialog and nothing more. A renewal's
        /// decision rests on every name its record carries, those the dialog offered and those it
        /// showed as dropped, so a read that missed the list while the dialog was built, and
        /// offered nothing, is caught here rather than written as a record with no profiles.
        ///
        /// **The record**, for a renewal: still there, unrevoked and unchanged. A revocation that
        /// landed after the operator answered, or after the handler last read it, is what this
        /// catches, under the same lock the revocation is written under.
        /// </remarks>
        public HeldStaleness Stale(IKeyValueStore store, string admissionKey)
        {
            if (store == null)
            {
                return HeldStaleness.Unpaired;
            }

            if (string.IsNullOrEmpty(store.Get(AuthorityKeys.AuthorityKey(DatabaseId)))
                || store.Get(AuthorityKeys.RetirementKey(DatabaseId)) != null)
            {
                return HeldStaleness.Authority;
            }

            if (Record != null)
            {
                var record = new AdmissionStore(store, NullGateLog.Instance).Find(admissionKey);
                if (record == null || !string.Equals(ApprovalBook.Digest(record.Save()), Record, StringComparison.Ordinal))
                {
                    return HeldStaleness.Unpaired;
                }
            }

            var stored = store.Get(ProfileNames.CustomDataKey);
            if (!ProfileNames.Readable(stored))
            {
                return HeldStaleness.Profiles;
            }

            var names = ProfileNames.Read(stored);
            foreach (var profile in _asked)
            {
                if (names.Holds(profile) != _listed.Contains(profile))
                {
                    return HeldStaleness.Profiles;
                }
            }

            return HeldStaleness.None;
        }
    }

    /// <summary>What has changed under a held approval since the operator gave it.</summary>
    public enum HeldStaleness
    {
        /// <summary>Nothing: it can be written.</summary>
        None,

        /// <summary>The authority it was given under is no longer the one served.</summary>
        Authority,

        /// <summary>The record a renewal renews has gone, been revoked or changed.</summary>
        Unpaired,

        /// <summary>An approved profile's place on the list is not what it was.</summary>
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
        /// Holds what the operator approved, until <paramref name="until"/>, replacing anything
        /// held under the same key, unless its database was locked or closed since
        /// <paramref name="mark"/>.
        /// </summary>
        /// <param name="listed">
        /// Which of the names the decision rested on the list held as the dialog showed it.
        /// </param>
        /// <param name="mark">
        /// What <see cref="Mark"/> answered before the operator was asked. An approval whose
        /// database a lock or a close let go of since then is answered as an approval that clear
        /// took away: <see cref="HeldApproval.Cleared"/>, and not held.
        /// </param>
        /// <param name="asked">
        /// The names the decision rested on beyond the approved ones, which the write compares as
        /// well, or null for none: a renewal's record carries names the operator was shown as
        /// dropped and did not approve.
        /// </param>
        public HeldApproval Hold(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            IEnumerable<string> listed,
            string record,
            PairingMoment until,
            long mark,
            IEnumerable<string> asked = null)
        {
            var held = new HeldApproval(key, databaseId, profiles, lifetime, asked, listed, record, until);
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
