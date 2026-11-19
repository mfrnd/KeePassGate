using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The authorities a database's store holds, `STORE.md` §614: which stand, which is served,
    /// and each read whole or damaged.
    /// </summary>
    /// <remarks>
    /// **Which authority is served is decided by the retirements**, by record, by item and by
    /// deletion record, among the records under the single store key that stands, and never by
    /// counting authorities, `StoreInForce.Authorities`. A single standing authority, in a store
    /// that is whole, is served. No standing authority serves nothing, and several serve nothing
    /// until the operator keeps an authority.
    ///
    /// **An authority's document is parsed only where it is asked for**: by the tab, the chooser
    /// and a write, and by the gate once for each authority it builds what it serves with. Which
    /// authority stands is answered from the reading alone.
    /// </remarks>
    internal static class StoreAuthorities
    {
        /// <summary>
        /// What the store holds, as the tab and the chooser read it: none, retired, damaged, whole,
        /// or several. The caller owns the result and disposes it.
        /// </summary>
        /// <param name="clientRecords">How many admission records the database holds.</param>
        public static AuthorityReading Read(StoreReading reading, int clientRecords)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            var standing = reading.Answering;
            if (standing.Count == 0)
            {
                // A store whose every authority is retired, by Delete or by copies that each kept
                // another authority: no authority, and not a store whose authority was never made.
                return reading.InForce != null && reading.InForce.Taken(StoreKinds.Retired).Count > 0
                    ? AuthorityReading.Retired(clientRecords)
                    : AuthorityReading.Absent(clientRecords);
            }

            if (standing.Count == 1)
            {
                var problems = new List<string>();
                var material = Material(standing[0], problems);
                return material == null
                    ? AuthorityReading.Damaged(problems, clientRecords)
                    : AuthorityReading.Whole(material, clientRecords);
            }

            // In the order of their fingerprints, ordinal, which is the order the operator reads
            // them in on the chooser and on the tab alike, and not the order the store works them
            // out in, which follows the identifiers of their entries.
            var candidates = new List<AuthorityCandidate>();
            foreach (var record in standing.OrderBy(record => record.Form.Member(StoreKinds.Id), StringComparer.Ordinal))
            {
                var problems = new List<string>();
                var material = Material(record, problems);
                var id = record.Form.Member(StoreKinds.Id);
                candidates.Add(material == null
                    ? AuthorityCandidate.Damaged(id, problems)
                    : AuthorityCandidate.Whole(id, material));
            }

            return AuthorityReading.Undecided(candidates, clientRecords);
        }

        /// <summary>
        /// The material an authority's record holds, read from the version that passed, or null
        /// with what was wrong in <paramref name="problems"/>.
        /// </summary>
        public static AuthorityMaterial Material(FoundRecord record, List<string> problems)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            return AuthorityRecord.Read(record.Form.Member(StoreKinds.Id), record.Form.Content(), problems);
        }

        /// <summary>
        /// The `id` of the authority this store serves, or null: a store that is whole with a
        /// single authority standing. Its document is not read here, so a damaged authority is
        /// answered by its `id`, and nothing is served from it.
        /// </summary>
        public static string Served(StoreReading reading)
        {
            return reading != null && reading.State == StoreState.Whole && reading.Answering.Count == 1
                ? reading.Answering[0].Form.Member(StoreKinds.Id)
                : null;
        }

    }
}
