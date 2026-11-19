using System.Collections.Generic;
using System.Linq;
using KeePassGate.Authority;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// Listings of admissions a test composes for a page, without a store behind them.
    /// </summary>
    internal static class Listings
    {
        /// <summary>
        /// These admissions, each admitting; these records that do not read, each holding the
        /// client key and the certificate given; and this many clients refused and waiting to be
        /// deleted.
        /// </summary>
        public static AdmissionListing Of(
            IEnumerable<AdmissionRecord> records,
            IEnumerable<UnreadAdmission> unreadable,
            int waiting,
            bool complete)
        {
            return new AdmissionListing(
                records.ToList(),
                new Dictionary<AdmissionRecord, AdmissionStanding>(),
                unreadable.ToList(),
                waiting,
                complete);
        }

        /// <summary>
        /// These admissions, and this many clients refused and waiting to be deleted.
        /// </summary>
        public static AdmissionListing Of(IEnumerable<AdmissionRecord> records, int waiting, bool complete)
        {
            return Of(records, Enumerable.Empty<UnreadAdmission>(), waiting, complete);
        }
    }
}
