using System;
using System.Collections.Generic;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Gate
{
    /// <summary>
    /// The admission of a certificate as the gate reads it, with the profiles the database names
    /// and the standing answers about its client's renewals in the same reading.
    /// </summary>
    /// <remarks>
    /// **A single reading for all of it**, so that the names a client is granted and the names it
    /// is refused for are two halves of a single list, and nothing lands between the admission and
    /// the names. Read in two holds, a lock landing between them would meet a database already
    /// shut, whose list names nothing, and the client would be told that every profile it holds
    /// had been taken away; and a hold of its own for the names would hold KeePass's thread once
    /// more at every connection. A renewal is answered from the same reading, so the record it
    /// renews and the answer that refuses it are of a single store, `PAIRING.md` §312.
    ///
    /// **The hold a connection is judged in parses no answer**: the versions of the client's
    /// answer are taken from the reading there, and read only where <see cref="Renewal"/> is
    /// asked, which is where a renewal is answered.
    /// </remarks>
    public sealed class Admitted
    {
        private static readonly IReadOnlyList<StoreNaming> NoVersions = Array.Empty<StoreNaming>();
        private static readonly IReadOnlyList<string> NoIds = Array.Empty<string>();

        private readonly Lazy<RenewalStanding> _renewal;

        /// <summary>An admission and what the database names in the reading that found it, with no answer about its client.</summary>
        /// <param name="named">The profiles the database names, none where it names none.</param>
        public Admitted(AdmissionRecord record, IReadOnlyList<string> named)
            : this(record, named, null)
        {
        }

        /// <summary>
        /// The same, with the versions of the standing answers about the admission's client that
        /// this reading of the store holds.
        /// </summary>
        internal Admitted(AdmissionRecord record, IReadOnlyList<string> named, StoreInForce inForce)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            Record = record;
            Named = named ?? Array.Empty<string>();

            var client = record.ClientKey;
            var objectText = client == null ? null : RenewalText.ObjectOf(client);
            var standing = inForce == null || objectText == null ? NoVersions : inForce.VersionsOf(objectText);
            var lost = inForce == null || objectText == null ? NoIds : inForce.LostOf(objectText);
            _renewal = new Lazy<RenewalStanding>(() => StoredRenewals.Of(client, standing, lost));
        }

        /// <summary>The admission of the certificate.</summary>
        public AdmissionRecord Record { get; private set; }

        /// <summary>
        /// The profiles the database names, read in the reading that found the admission: a
        /// client reaches no profile the reading left out.
        /// </summary>
        public IReadOnlyList<string> Named { get; private set; }

        /// <summary>
        /// What the reading that found the admission holds of the standing answers about its
        /// client's renewals, `STORE.md` §618, worked out at the first time of asking.
        /// </summary>
        internal RenewalStanding Renewal
        {
            get { return _renewal.Value; }
        }
    }
}
