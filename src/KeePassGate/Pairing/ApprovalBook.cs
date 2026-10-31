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
    /// served, which of the approved names the database's list held, and for a renewal the record
    /// being renewed. <see cref="Stale"/> compares that state with the database's own under the
    /// writer's lock, at the moment of writing.
    /// </remarks>
    public sealed class HeldApproval
    {
        private readonly HashSet<string> _listed;

        internal HeldApproval(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            IEnumerable<string> listed,
            string record,
            DateTimeOffset until)
        {
            Key = key;
            DatabaseId = databaseId;
            Profiles = profiles ?? Array.Empty<string>();
            Lifetime = lifetime;
            _listed = new HashSet<string>(listed ?? Array.Empty<string>(), StringComparer.Ordinal);
            Record = record;
            Until = until;
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

        /// <summary>When it stops being held, which is the end of the pairing budget it was given in.</summary>
        public DateTimeOffset Until { get; private set; }

        /// <summary>
        /// Whether a lock, a close or KeePass exiting took it away, rather than its time running out
        /// or a write taking it.
        /// </summary>
        public bool Cleared { get; internal set; }

        /// <summary>
        /// Why the database no longer holds what this was approved against, or null when it still
        /// does. Asked under the writer's lock, on KeePass's thread, as part of the write.
        /// </summary>
        /// <remarks>
        /// **The authority** has to be the one served and not retired: a record written under any other
        /// is one that chains to nothing the gate serves.
        ///
        /// **The profiles**, name by name: each approved name has to be on the list exactly when it
        /// was. A name the operator took off the list since would be put back by the write, with
        /// every rule naming it and every client holding it, and one put on it since is not the
        /// "(new)" name the dialog said it was. A name the approval does not carry changes nothing
        /// it decided, so a pairing landing in between and naming something else voids nothing.
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
            foreach (var profile in Profiles)
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
    /// changes anything. **Bounded by the pairing budget** the request arrived in, and **cleared by
    /// a lock, a close and KeePass exiting**, so an approval never outlives the session of the
    /// database it was given in, and one still settling when KeePass locks is never written after
    /// the unlock for a client whose connection the unlock closed.
    ///
    /// **Every approval passes through here**, the ones written at once included, because the
    /// same three things decide whether any of them may still be written: the entry is still
    /// held, the database still holds what it was approved against, and, for a pairing, the
    /// invitation is still unspent.
    /// </remarks>
    public sealed class ApprovalBook : IApprovalScreen
    {
        private readonly Func<DateTimeOffset> _clock;
        private readonly object _lock = new object();
        private readonly Dictionary<string, HeldApproval> _held =
            new Dictionary<string, HeldApproval>(StringComparer.Ordinal);

        public ApprovalBook()
            : this(null)
        {
        }

        public ApprovalBook(Func<DateTimeOffset> clock)
        {
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
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
        /// Holds what the operator approved, until <paramref name="until"/>, replacing anything
        /// held under the same key.
        /// </summary>
        public HeldApproval Hold(
            string key,
            string databaseId,
            IReadOnlyList<string> profiles,
            TimeSpan lifetime,
            IEnumerable<string> listed,
            string record,
            DateTimeOffset until)
        {
            var held = new HeldApproval(key, databaseId, profiles, lifetime, listed, record, until);
            lock (_lock)
            {
                _held[key] = held;
            }

            return held;
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

                _held.Remove(held.Key);
                return true;
            }
        }

        /// <summary>Lets go of every approval held, which a lock, a close and an exit do.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                foreach (var held in _held.Values)
                {
                    held.Cleared = true;
                }

                _held.Clear();
            }
        }

        /// <summary>A lock: nothing held survives it.</summary>
        void IApprovalScreen.Locked()
        {
            Clear();
        }

        /// <summary>A database closed for real, which takes every approval with it as a lock does.</summary>
        void IApprovalScreen.Closed()
        {
            Clear();
        }

        /// <summary>Drops whatever has run out. Called with the lock held.</summary>
        private void Sweep()
        {
            var now = _clock();
            foreach (var key in _held.Where(pair => pair.Value.Until <= now).Select(pair => pair.Key).ToList())
            {
                _held.Remove(key);
            }
        }
    }
}
