using System;
using System.Collections.Generic;
using System.Linq;

namespace KeePassGate.Store
{
    /// <summary>
    /// What one act writes into the store, composed when it is asked for: its records, the key
    /// they are under, the name the log gives it, and its ground, `STORE.md` §627.
    /// </summary>
    /// <remarks>
    /// **Composed when it is asked for, and held to its ground at the write.** The tab buffers an
    /// act until its dialog's OK, and the store can change meanwhile: an act whose ground went is
    /// left out and named, as a Recreate of an authority that no longer stands. A revocation the
    /// tab holds is composed again over the store as it stands at each read of the buffer and at
    /// OK. A revocation and a certificate taken away have no ground and are always written.
    /// </remarks>
    public sealed class StoreAct
    {
        private readonly byte[] _key;
        private readonly Func<StoreGround, string> _ground;

        /// <summary>An act of these records under this key, held to this ground.</summary>
        /// <param name="name">What the log calls the act.</param>
        /// <param name="key">The store key its records are composed under; the act keeps a copy.</param>
        /// <param name="against">
        /// The name of the store key that stood where the act was composed, which is the key its
        /// records are under; null where none stood and the act brings a new key.
        /// </param>
        /// <param name="records">Its records, in the order they are written.</param>
        /// <param name="ground">
        /// Why the act no longer applies to a store as it stands, or null where it applies; null
        /// for an act with no ground.
        /// </param>
        internal StoreAct(
            string name,
            byte[] key,
            string against,
            IReadOnlyList<StoreRecord> records,
            Func<StoreGround, string> ground)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (records == null || records.Count == 0)
            {
                throw new ArgumentException("An act writes a record at least.", nameof(records));
            }

            Name = name;
            _key = (byte[])key.Clone();
            KeyName = StoreFormat.KeyName(_key);
            if (against != null && !string.Equals(against, KeyName, StringComparison.Ordinal))
            {
                throw new ArgumentException("An act composed against a store key has its records under that key.", nameof(against));
            }

            Against = against;
            Records = records.ToList();
            _ground = ground;
        }

        /// <summary>What the log calls the act.</summary>
        internal string Name { get; private set; }

        /// <summary>The records the act writes.</summary>
        internal IReadOnlyList<StoreRecord> Records { get; private set; }

        /// <summary>The name of the key its records are under, `StoreFormat.KeyName`.</summary>
        internal string KeyName { get; private set; }

        /// <summary>
        /// The name of the store key that stood where the act was composed, or null where none
        /// stood and the act brings a new key.
        /// </summary>
        internal string Against { get; private set; }

        /// <summary>A copy of the key its records are under, for a write that starts a store with it.</summary>
        internal byte[] Key()
        {
            return (byte[])_key.Clone();
        }

        /// <summary>Why the act no longer applies to the store as the ground has it, or null.</summary>
        internal string Why(StoreGround ground)
        {
            return _ground == null ? null : _ground(ground);
        }

        /// <summary>Overwrites the key the act holds, once it is dropped.</summary>
        internal void Forget()
        {
            Array.Clear(_key, 0, _key.Length);
        }
    }

    /// <summary>
    /// What an act is held to: the store key that stands and the authorities that stand, with the
    /// acts before it in the same write taken, `STORE.md` §627.
    /// </summary>
    internal sealed class StoreGround
    {
        public StoreGround(string keyName, IEnumerable<string> standing)
        {
            KeyName = keyName;
            Standing = new HashSet<string>(standing ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        }

        /// <summary>The name of the store key that stands, or null where no key stands.</summary>
        public string KeyName { get; private set; }

        /// <summary>The `id` of every authority that stands.</summary>
        public ISet<string> Standing { get; private set; }

        /// <summary>The same ground with an act taken: what it adds stands, and what it retires does not.</summary>
        public StoreGround With(StoreAct act)
        {
            var standing = new HashSet<string>(Standing, StringComparer.Ordinal);
            foreach (var record in act.Records)
            {
                if (record.Kind == StoreKinds.Authority)
                {
                    standing.Add(record.Member(StoreKinds.Id));
                }
                else if (record.Kind == StoreKinds.Retired)
                {
                    standing.Remove(record.Member(StoreKinds.AuthorityMember));
                }
            }

            return new StoreGround(KeyName ?? act.KeyName, standing);
        }
    }

    /// <summary>What a write into the store came to: taken, or refused and why, and the acts left out.</summary>
    public sealed class StoreWritten
    {
        private StoreWritten(bool taken, string reason, IReadOnlyList<StoreAct> acts, IReadOnlyList<string> leftOut, bool wrote, bool waits)
        {
            Taken = taken;
            Reason = reason;
            Acts = acts ?? Array.Empty<StoreAct>();
            LeftOut = leftOut ?? Array.Empty<string>();
            Wrote = wrote;
            Waits = waits;
        }

        /// <summary>Whether the write was made, with every act it took.</summary>
        public bool Taken { get; private set; }

        /// <summary>Why nothing was written, or null where the write was made.</summary>
        public string Reason { get; private set; }

        /// <summary>Each act left out, by its name and why its ground went.</summary>
        public IReadOnlyList<string> LeftOut { get; private set; }

        /// <summary>The acts taken, in the order they were given; none where the write was refused.</summary>
        /// <remarks>
        /// What the audit of the KPG tab's OK is written from: an act left out wrote nothing.
        /// </remarks>
        public IReadOnlyList<StoreAct> Acts { get; private set; }

        /// <summary>
        /// Whether anything was written into the database: false where the acts taken cancel out,
        /// as an authority made and retired again does, and where they are held for a dialog's OK.
        /// </summary>
        public bool Wrote { get; private set; }

        /// <summary>
        /// Whether the acts taken are held for a dialog's OK rather than written: what comes of
        /// them is decided there.
        /// </summary>
        public bool Waits { get; private set; }

        internal static StoreWritten Done(IReadOnlyList<StoreAct> taken, IReadOnlyList<string> leftOut, bool wrote)
        {
            return new StoreWritten(true, null, taken, leftOut, wrote, false);
        }

        internal static StoreWritten Waiting(IReadOnlyList<StoreAct> taken, IReadOnlyList<string> leftOut)
        {
            return new StoreWritten(true, null, taken, leftOut, false, true);
        }

        internal static StoreWritten Refused(string reason, IReadOnlyList<string> leftOut = null)
        {
            return new StoreWritten(false, reason, null, leftOut, false, false);
        }
    }

    /// <summary>
    /// A database's store as the plugin's own screens and writer reach it: read as it stands, and
    /// written by acts, `STORE.md` §627.
    /// </summary>
    public interface IGateStore
    {
        /// <summary>
        /// The store as it stands, read now, for the caller to let go once it is done with it,
        /// which overwrites the keys the reading found.
        /// </summary>
        StoreReading Read();

        /// <summary>
        /// Writes these acts in a single write, each held to its ground, or nothing, and answers
        /// what came of it.
        /// </summary>
        StoreWritten Write(IReadOnlyList<StoreAct> acts);

        /// <summary>
        /// Why no write of the operator's is made into this store in this session, whatever it
        /// holds, or null where a write may be made: the dialogs that would take a write back not
        /// watched. The KPG tab offers no command on the store then, as under a word.
        /// </summary>
        string Unwritable { get; }
    }
}
