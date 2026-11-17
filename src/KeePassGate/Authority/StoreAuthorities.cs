using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
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
        /// <param name="clientsComplete">Whether those could be listed at all.</param>
        public static AuthorityReading Read(StoreReading reading, int clientRecords, bool clientsComplete)
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
                    ? AuthorityReading.Retired(clientRecords, clientsComplete)
                    : AuthorityReading.Absent(clientRecords, clientsComplete);
            }

            if (standing.Count == 1)
            {
                var problems = new List<string>();
                var material = Material(standing[0], problems);
                return material == null
                    ? AuthorityReading.Damaged(problems, clientRecords, clientsComplete)
                    : AuthorityReading.Whole(material, clientRecords, clientsComplete);
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

            return AuthorityReading.Undecided(candidates, clientRecords, clientsComplete);
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

        /// <summary>
        /// The `id` of every authority standing in a store that is whole, or null where the store
        /// does not tell: no key, several keys, a form this plugin does not read, or a store that
        /// is not whole. What the prune of revocations naming an authority gone is held to.
        /// </summary>
        /// <remarks>
        /// **Whole, and not only read**, because what a revocation is taken away for has to be
        /// final. In a stopped store an authority can be missing for its entry having been deleted
        /// by hand, and a copy merged in can bring that entry back, which would leave the
        /// authority standing again with the revocations of its clients gone. In a whole store an
        /// authority is missing only where the plugin retired it and deleted its entry.
        /// </remarks>
        public static IReadOnlyCollection<string> Standing(StoreReading reading)
        {
            return reading == null || reading.State != StoreState.Whole || reading.InForce == null
                ? null
                : reading.InForce.Authorities.Select(record => record.Form.Member(StoreKinds.Id)).ToList();
        }

        /// <summary>
        /// The `id` of the authority among those standing whose certificate issued this certificate, by
        /// the anchor pin, or null where none did or none could be read.
        /// </summary>
        public static string IssuerOf(StoreReading reading, X509Certificate2 certificate)
        {
            if (reading == null || reading.InForce == null || certificate == null)
            {
                return null;
            }

            foreach (var record in reading.InForce.Authorities)
            {
                using (var material = Material(record, new List<string>()))
                {
                    if (material != null && AnchorPin.Chains(certificate, material.Authority))
                    {
                        return record.Form.Member(StoreKinds.Id);
                    }
                }
            }

            return null;
        }
    }
}
