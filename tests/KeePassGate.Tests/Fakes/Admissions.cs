using System;
using System.Linq;
using KeePassGate.Authority;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The admissions of a database a page is given, as a test arranges and reads them: written
    /// into its store as a key holder can write them, under the authority the store serves, and
    /// read as the gate reads them; and the standing answers to its clients' renewals, in its store
    /// too.
    /// </summary>
    /// <remarks>
    /// Over the database the test hands the page, or over the buffer the page writes into, in
    /// which case a revocation waits for the dialog's OK as the page's own does.
    /// </remarks>
    internal sealed class Admissions
    {
        private readonly IGateDatabase _database;

        public Admissions(IGateDatabase database)
        {
            _database = database;
        }

        /// <summary>The admission, with the authority the store serves as its issuer.</summary>
        public void Write(AdmissionRecord record)
        {
            var members = record.Members().ToDictionary(member => member.Key, member => member.Value, StringComparer.Ordinal);
            members[StoreKinds.Issuer] = StoreSeeds.Served(_database.Store) ?? record.Issuer;
            StoreSeeds.Client(_database.Store, members);
        }

        /// <summary>The listing a page shows.</summary>
        public AdmissionListing All()
        {
            return StoreSeeds.Listed(_database.Store);
        }

        /// <summary>The admission of a certificate as the gate reads it, or null.</summary>
        public AdmissionRecord Find(string admissionKey)
        {
            using (var reading = _database.Store.Read())
            {
                return StoreAdmissions.Admitted(reading, admissionKey);
            }
        }

        /// <summary>
        /// Revokes the client a certificate is admitted for, answering whether it was.
        /// </summary>
        public bool Remove(string admissionKey)
        {
            var admission = Find(admissionKey);
            if (admission == null)
            {
                return false;
            }

            StoreSeeds.Revoke(_database.Store, admission.ClientKey);
            return true;
        }

        /// <summary>A standing refusal of a client's renewals, as the renewal dialog writes it.</summary>
        public void Refuse(string clientKey, RenewalRefusal refusal)
        {
            StoreSeeds.Refuse(_database.Store, clientKey, refusal);
        }

        /// <summary>The strictest stored answer about a client's renewals, or null.</summary>
        public RenewalRefusal Answer(string clientKey)
        {
            using (var reading = _database.Store.Read())
            {
                return StoredRenewals.AnswerOf(reading.InForce, clientKey);
            }
        }
    }
}
