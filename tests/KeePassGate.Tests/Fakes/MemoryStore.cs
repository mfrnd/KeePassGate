using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A database's store without KeePass: its entries and deletion records in memory, read as
    /// the gate reads a database and written by the product's own plan, applied as
    /// `StoreWrite.Applied` applies it.
    /// </summary>
    /// <remarks>
    /// The entries a test puts here directly are composed by `tests/Shared/StoreComposer.cs`, so
    /// that a test can hold the product's writes to records it did not make. It can also be told
    /// to lose a write or to damage what a write holds, which no database does, for the read backs
    /// that are there in case a database ever does, and to refuse a write or throw, as a writer and
    /// a database that went do.
    /// </remarks>
    internal sealed class MemoryStore : IGateStore
    {
        private readonly StoreVersions _versions = new StoreVersions();
        private List<StoreCandidate> _candidates = new List<StoreCandidate>();
        private List<byte[]> _deletions = new List<byte[]>();

        /// <summary>The plugin's version a write states.</summary>
        public string Plugin { get; set; } = "0.3.0";

        /// <summary>How many writes were made.</summary>
        public int Writes { get; private set; }

        /// <summary>When true, a write is planned and answered as made, and nothing is kept.</summary>
        public bool SwallowWrites { get; set; }

        /// <summary>
        /// When true, every authority a write holds lands with certificates and a gate key that
        /// will not read: a write that landed and cannot be used, which needs another message
        /// than a write that was lost.
        /// </summary>
        public bool DamageAuthorities { get; set; }

        /// <summary>Why every write is refused, as a writer refuses a write, or null for writes that are made.</summary>
        public string RefuseWrites { get; set; }

        /// <summary>
        /// Why no write of the operator's is made in this session, as where the dialogs that would
        /// take a write back are not watched, or null; a write is refused with it as well.
        /// </summary>
        public string Unwritable { get; set; }

        /// <summary>What a read throws, or null for reads that work.</summary>
        public Exception ThrowOnRead { get; set; }

        /// <summary>What a write throws, or null for writes that answer.</summary>
        public Exception ThrowOnWrite { get; set; }

        /// <summary>The acts of every write made, in order.</summary>
        public List<IReadOnlyList<StoreAct>> Written { get; } = new List<IReadOnlyList<StoreAct>>();

        /// <summary>Every act handed to a write, whatever came of it, in order.</summary>
        public List<StoreAct> Offered { get; } = new List<StoreAct>();

        /// <summary>The entries the store holds, each its identifier and its versions.</summary>
        public IReadOnlyList<StoreCandidate> Candidates
        {
            get { return _candidates; }
        }

        /// <summary>The identifiers the deletion records name.</summary>
        public IReadOnlyList<byte[]> Deletions
        {
            get { return _deletions; }
        }

        /// <summary>A record composed by the test, put into the store as it is.</summary>
        public void Put(StoreComposer.Composed record)
        {
            _candidates.Add(_versions.Candidate(record));
        }

        /// <summary>An entry under an identifier, with versions the test made.</summary>
        public void Put(byte[] identifier, params IReadOnlyDictionary<string, string>[] versions)
        {
            _candidates.Add(_versions.Candidate(identifier, versions));
        }

        /// <summary>Takes an entry out with no deletion record, as a program that keeps none does.</summary>
        public void Drop(string hex)
        {
            _candidates.RemoveAll(candidate => candidate.Hex == hex);
        }

        /// <summary>A deletion record naming an identifier.</summary>
        public void Delete(byte[] identifier)
        {
            _deletions.Add(identifier);
        }

        /// <summary>This store as another copy of it holds it, from here on apart.</summary>
        public MemoryStore Copy()
        {
            return new MemoryStore
            {
                Plugin = Plugin,
                _candidates = new List<StoreCandidate>(_candidates),
                _deletions = new List<byte[]>(_deletions),
            };
        }

        /// <summary>
        /// Another copy brought into this store as KeePass's Synchronize brings it: every entry of
        /// both and every deletion record of both, less every entry a deletion record names.
        /// </summary>
        public void Synchronize(MemoryStore other)
        {
            var deletions = _deletions.Concat(other._deletions).GroupBy(KeePassGate.Hex.Of).Select(group => group.First()).ToList();
            var deleted = new HashSet<string>(deletions.Select(KeePassGate.Hex.Of));
            _candidates = _candidates.Concat(other._candidates)
                .GroupBy(candidate => candidate.Hex)
                .Select(group => group.First())
                .Where(candidate => !deleted.Contains(candidate.Hex))
                .ToList();
            _deletions = deletions;
        }

        public StoreReading Read()
        {
            if (ThrowOnRead != null)
            {
                throw ThrowOnRead;
            }

            return StoreReading.Afresh(_candidates, _deletions);
        }

        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            Offered.AddRange(acts);
            if (ThrowOnWrite != null)
            {
                throw ThrowOnWrite;
            }

            if (RefuseWrites != null || Unwritable != null)
            {
                return StoreWritten.Refused(RefuseWrites ?? Unwritable);
            }

            if (DamageAuthorities)
            {
                acts = acts.Select(Damaged).ToList();
            }

            StoreWrite plan;
            using (var reading = Read())
            {
                plan = StoreWrite.Plan(reading, acts, Plugin);
            }

            if (plan.Refused != null)
            {
                return StoreWritten.Refused(plan.Refused, plan.LeftOut);
            }

            Written.Add(acts);
            Writes++;
            if (!SwallowWrites)
            {
                var applied = plan.Applied(_candidates, _deletions);
                _candidates = applied.Key.ToList();
                _deletions = applied.Value.ToList();
            }

            return StoreWritten.Done(plan.Taken, plan.LeftOut, !plan.Empty);
        }

        /// <summary>The same act, with every authority it holds composed again with parts no reader takes.</summary>
        private static StoreAct Damaged(StoreAct act)
        {
            var key = act.Key();
            try
            {
                var records = act.Records.Select(record =>
                {
                    if (record.Kind != StoreKinds.Authority)
                    {
                        return record;
                    }

                    var members = record.Members
                        .Where(member => member.Key != StoreKinds.Nonce)
                        .ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
                    members[AuthorityRecord.AuthorityMember] = "rubbish";
                    members[AuthorityRecord.HostMember] = "rubbish";
                    members[AuthorityRecord.SniKeyMember] = "rubbish";
                    return StoreRecord.Compose(key, StoreKinds.Authority, members);
                }).ToList();

                return new StoreAct(act.Name, key, act.Against, records, act.Why, act.EndsAdmissions);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
