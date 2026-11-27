using System;
using System.Collections.Generic;
using System.Linq;
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
    /// to refuse a write or throw, as a writer and a database that went do.
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

        /// <summary>
        /// How many readings were asked for, the reading a write plans over apart.
        /// </summary>
        public int Reads { get; private set; }

        /// <summary>Why every write is refused, as a writer refuses a write, or null for writes that are made.</summary>
        public string RefuseWrites { get; set; }

        /// <summary>
        /// Why no write of the operator's is made in this session, as where the dialogs that would
        /// take a write back are not watched, or null; a write is refused with it as well.
        /// </summary>
        public string Unwritable { get; set; }

        /// <summary>What a read throws, or null for reads that work.</summary>
        public Exception ThrowOnRead { get; set; }

        /// <summary>
        /// What the next read throws, once, or null: a defect in one step of something that reads
        /// the store several times.
        /// </summary>
        public Exception NextReadThrows { get; set; }

        /// <summary>What a write throws, or null for writes that answer.</summary>
        public Exception ThrowOnWrite { get; set; }

        /// <summary>
        /// What changes the store as a write begins, before it reads the store it plans over, or
        /// null: a merge landing between a caller's last reading and its write.
        /// </summary>
        public Action BeforeWrite { get; set; }

        /// <summary>The acts of every write made, in order.</summary>
        public List<IReadOnlyList<StoreAct>> Written { get; } = new List<IReadOnlyList<StoreAct>>();

        /// <summary>Every act handed to a write, whatever came of it, in order.</summary>
        public List<StoreAct> Offered { get; } = new List<StoreAct>();

        /// <summary>The plan of the last write taken, or null before a write is taken.</summary>
        public StoreWrite LastPlan { get; private set; }

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

        /// <summary>
        /// The identifier of every group and entry of the database's tree, which a reading walks
        /// where a deletion record is left that no object the file names explains; none, the
        /// default, for a reading with no tree. Shared by every copy made of this store.
        /// </summary>
        public List<byte[]> Tree { get; private set; } = new List<byte[]>();

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
                Tree = Tree,
            };
        }

        /// <summary>
        /// Another copy brought into this store as KeePass's Synchronize brings it: every entry of
        /// both and every deletion record of both, less every entry a deletion record names.
        /// </summary>
        /// <remarks>
        /// **Not KeePass's where both copies hold an entry apart**: this copy's entry is taken,
        /// where KeePass takes the entry modified last, since the store holds no times; a sequence
        /// that edits an entry in the other copy puts that copy's entry in its place itself. Every
        /// entry a deletion record names goes, as KeePass's does. No sequence played through it
        /// passes an entry changed apart in both copies at a merge where the real merge would end
        /// wider; the real merges are run on files by the host tier's merge tests, `STORE.md` §625
        /// and §632.
        /// </remarks>
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

        /// <summary>
        /// Another copy brought into this store as a merge that carries no deletion record brings
        /// it, as KeePassXC's does: every entry of both, and the deletion records this copy held.
        /// </summary>
        public void MergeWithoutDeletions(MemoryStore other)
        {
            _candidates = _candidates.Concat(other._candidates)
                .GroupBy(candidate => candidate.Hex)
                .Select(group => group.First())
                .ToList();
        }

        /// <summary>
        /// Another copy brought into this store as KeePass's Overwrite if newer brings it: every
        /// entry of both, and the deletion records this copy held. An entry both copies hold is
        /// taken as this copy holds it, which is the newer only where the two are the same entry:
        /// the store holds no times, so an entry the two copies changed apart is refused.
        /// </summary>
        /// <exception cref="InvalidOperationException">An entry the two copies hold apart under a single identifier.</exception>
        public void OverwriteIfNewer(MemoryStore other)
        {
            var held = _candidates.ToDictionary(candidate => candidate.Hex, StringComparer.Ordinal);
            StoreCandidate mine;
            if (other._candidates.Any(candidate => held.TryGetValue(candidate.Hex, out mine) && !ReferenceEquals(mine, candidate)))
            {
                throw new InvalidOperationException("Both copies changed an entry apart, and the test's store holds no times to tell the newer by.");
            }

            MergeWithoutDeletions(other);
        }

        /// <summary>Every deletion record taken out, as KeePass's Database Maintenance takes them out.</summary>
        public void ClearDeletions()
        {
            _deletions = new List<byte[]>();
        }

        public StoreReading Read()
        {
            Reads++;
            return Reading();
        }

        /// <summary>
        /// The store as it holds it now, over lists of its own, as `KdbxStore` builds every
        /// reading afresh: a put, a drop, a deletion record or a write after it changes nothing a
        /// reading already handed out holds.
        /// </summary>
        private StoreReading Reading()
        {
            if (ThrowOnRead != null)
            {
                throw ThrowOnRead;
            }

            var once = NextReadThrows;
            if (once != null)
            {
                NextReadThrows = null;
                throw once;
            }

            if (Tree.Count == 0)
            {
                return StoreReading.Afresh(_candidates.ToList(), _deletions.ToList());
            }

            var nodes = Tree.ToList();
            return StoreReading.AfreshInTree(_candidates.ToList(), _deletions.ToList(), null, () => nodes);
        }

        public StoreWritten Write(IReadOnlyList<StoreAct> acts)
        {
            Offered.AddRange(acts);
            BeforeWrite?.Invoke();
            if (ThrowOnWrite != null)
            {
                throw ThrowOnWrite;
            }

            if (RefuseWrites != null || Unwritable != null)
            {
                return StoreWritten.Refused(RefuseWrites ?? Unwritable);
            }

            StoreWrite plan;
            using (var reading = Reading())
            {
                plan = StoreWrite.Plan(reading, acts, Plugin);
            }

            if (plan.Refused != null)
            {
                return StoreWritten.Refused(plan.Refused, plan.LeftOut);
            }

            Written.Add(acts);
            LastPlan = plan;
            Writes++;
            var applied = plan.Applied(_candidates, _deletions);
            _candidates = applied.Key.ToList();
            _deletions = applied.Value.ToList();

            return StoreWritten.Done(plan.Taken, plan.LeftOut, !plan.Empty, plan.Sealed);
        }
    }
}
