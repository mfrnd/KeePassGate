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
    /// left out and named, as a Recreate of an authority that no longer stands. An act on a client,
    /// on a certificate or about a profile name holds what is to be done, <see cref="Again"/>: it
    /// is composed again over the store as it stands at each read of the buffer and at OK, and has
    /// no ground: a revocation is always written, a certificate taken away is written unless
    /// nothing of it is left to end by then, and a name is written where the store still calls
    /// for it.
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
        /// <param name="again">
        /// The act composed again against a reading that holds a store key, or null where nothing
        /// is left of it to write there; null for an act composed once, at its press.
        /// </param>
        /// <param name="seals">
        /// Whether the act seals a stopped store again, `STORE.md` §625: written first in its
        /// write, which deletes every entry that is damage, and the only act that may hold no
        /// record, since its write's summary entry is what it writes.
        /// </param>
        /// <param name="leftOut">What the act left out as it was composed, each with why, for the log; none for null.</param>
        internal StoreAct(
            string name,
            byte[] key,
            string against,
            IReadOnlyList<StoreRecord> records,
            Func<StoreGround, string> ground,
            Func<StoreReading, StoreAct> again = null,
            bool seals = false,
            IReadOnlyList<string> leftOut = null)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (records == null || (records.Count == 0 && !seals))
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
            Again = again;
            Seals = seals;
            LeftOut = (leftOut ?? Array.Empty<string>()).ToList();
        }

        /// <summary>
        /// Whether the act seals a stopped store again, `STORE.md` §625: the only act a write takes
        /// over a stopped store, and only as its first.
        /// </summary>
        internal bool Seals { get; private set; }

        /// <summary>What the act left out as it was composed, each with why: what the log names.</summary>
        internal IReadOnlyList<string> LeftOut { get; private set; }

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

        /// <summary>
        /// What is to be done, composed again: the act as a reading that holds a store key calls
        /// for it, under that key, or null where nothing is left of it to write there. Null for an
        /// act composed once, at its press, as an act of the authority's is.
        /// </summary>
        /// <remarks>
        /// The act's composer gives it, so that whoever holds an act composes it again without
        /// knowing the family it comes from. A reading that holds no store key is not asked: an act
        /// held there stays as it was pressed, and the write leaves it out for its key.
        /// </remarks>
        internal Func<StoreReading, StoreAct> Again { get; private set; }

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

    /// <summary>
    /// What a write that sealed a stopped store again lifted, `STORE.md` §625, as the store it was
    /// planned over held it: why the store was stopped, the damaged entries the write deleted, and
    /// the objects their deletion records leave refusing.
    /// </summary>
    public sealed class StoreSealed
    {
        internal StoreSealed(string stopped, IReadOnlyList<string> deleted, IReadOnlyList<string> refusing)
        {
            Stopped = stopped;
            Deleted = deleted;
            Refusing = refusing;
        }

        /// <summary>Why the store the write was planned over was stopped.</summary>
        public string Stopped { get; private set; }

        /// <summary>The identifiers of the damaged entries the write deleted, each with a deletion record.</summary>
        public IReadOnlyList<string> Deleted { get; private set; }

        /// <summary>The objects a damaged version's deletion record leaves refusing until set again.</summary>
        public IReadOnlyList<string> Refusing { get; private set; }
    }

    /// <summary>What a write into the store came to: taken, or refused and why, and the acts left out.</summary>
    public sealed class StoreWritten
    {
        private StoreWritten(bool taken, string reason, IReadOnlyList<StoreAct> acts, IReadOnlyList<string> leftOut, bool wrote, bool waits, StoreSealed sealedAgain = null)
        {
            Taken = taken;
            Reason = reason;
            Acts = acts ?? Array.Empty<StoreAct>();
            LeftOut = leftOut ?? Array.Empty<string>();
            Wrote = wrote;
            Waits = waits;
            Sealed = sealedAgain;
        }

        /// <summary>
        /// What the write lifted where it sealed a stopped store again and was made, or null: told
        /// from the write itself, not from a reading before it.
        /// </summary>
        public StoreSealed Sealed { get; private set; }

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

        internal static StoreWritten Done(IReadOnlyList<StoreAct> taken, IReadOnlyList<string> leftOut, bool wrote, StoreSealed sealedAgain = null)
        {
            return new StoreWritten(true, null, taken, leftOut, wrote, false, sealedAgain);
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
