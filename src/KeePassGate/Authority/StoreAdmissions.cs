using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Who a database's store admits, `STORE.md` §615: the admissions, read by their meaning from
    /// what is in force under the store key.
    /// </summary>
    /// <remarks>
    /// **Read from the reading of the hold, and kept nowhere**, `STORE.md` §620: the gate asks it
    /// once for each connection, from the reading every question of that hold is answered from,
    /// and a decision that is in the gate's copy when the hold begins holds in that hold.
    ///
    /// **A certificate is admitted** by a single admission in force that reads, whose client key
    /// no revocation names, whose certificate was not taken away, whose issuer no retirement names
    /// and is the authority served, in a store that is whole. **What does not read admits nobody**,
    /// and is counted among no admissions of a certificate, and neither is an admission a decision
    /// refuses (S24).
    ///
    /// **Total, and no certificate is parsed but those asked about**: a hold parses the admission
    /// of the certificate on the connection, and a listing every admission's, for a page or a
    /// dialog.
    /// </remarks>
    internal static class StoreAdmissions
    {
        /// <summary>
        /// The admission of this certificate where it counts, else null: no admission, several,
        /// an admission refused by a decision, or an admission under an authority that is not
        /// served.
        /// </summary>
        /// <remarks>
        /// **Every admission in force is compared**, the whole key and in fixed time, so nothing
        /// tells how much of a key a certificate somebody else chose matched.
        /// </remarks>
        public static AdmissionRecord Admitted(StoreReading reading, string admissionKey)
        {
            if (reading == null || reading.InForce == null || string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            var served = StoreAuthorities.Served(reading);
            if (served == null)
            {
                return null;
            }

            AdmissionRecord found = null;
            var count = 0;
            foreach (var record in reading.InForce.Admissions.Where(record => record.InForce))
            {
                var certificate = record.Form.Member(StoreKinds.Certificate);
                if (!FixedTime.Equals(certificate ?? string.Empty, admissionKey))
                {
                    continue;
                }

                // A record that does not read is no admission anywhere, and an admission that a
                // decision refuses is not counted among several, so the answer is the same before
                // the write that deletes it and after, `STORE.md` §615 and §627.
                var admission = AdmissionRecord.Read(record.Hex, record.Form.Members);
                if (admission == null || Refused(reading.InForce, admission))
                {
                    continue;
                }

                count++;
                found = admission;
            }

            return count == 1 && string.Equals(found.Issuer, served, StringComparison.Ordinal)
                ? found
                : null;
        }

        /// <summary>
        /// Every admission in force that reads and that no decision refuses, newest first, each
        /// with what it comes to; the records in force that do not read; and how many clients a
        /// decision refuses whose admissions wait to be deleted.
        /// </summary>
        /// <remarks>
        /// **A store that cannot be judged lists nobody and reports it could not**: under several
        /// keys, a form the plugin does not read, past the ceiling on keys. A database whose file
        /// holds no store key lists nobody and states so, since nobody can be admitted there.
        /// </remarks>
        public static AdmissionListing Listing(StoreReading reading)
        {
            if (reading == null)
            {
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), false);
            }

            var inForce = reading.InForce;
            if (inForce == null)
            {
                return new AdmissionListing(Array.Empty<AdmissionRecord>(), reading.State == StoreState.None);
            }

            var served = StoreAuthorities.Served(reading);
            var read = new List<AdmissionRecord>();
            var unreadable = new List<UnreadAdmission>();
            var waiting = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in inForce.Admissions.Where(record => record.InForce))
            {
                var admission = AdmissionRecord.Read(record.Hex, record.Form.Members);
                if (admission == null)
                {
                    var client = record.Form.Member(StoreKinds.ClientMember);
                    var certificate = record.Form.Member(StoreKinds.Certificate);
                    if (RefusedByText(inForce, client, certificate, record.Form.Member(StoreKinds.Issuer)))
                    {
                        waiting.Add(CertificateDigest.IsDigest(client) ? client : record.Hex);
                        continue;
                    }

                    unreadable.Add(new UnreadAdmission(record.Hex, client, certificate));
                    continue;
                }

                if (Refused(inForce, admission))
                {
                    waiting.Add(admission.ClientKey);
                    continue;
                }

                read.Add(admission);
            }

            var named = read.GroupBy(admission => admission.AdmissionKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var standing = new Dictionary<AdmissionRecord, AdmissionStanding>();
            foreach (var admission in read)
            {
                if (named[admission.AdmissionKey] > 1)
                {
                    standing[admission] = AdmissionStanding.Twice;
                }
                else if (!string.Equals(admission.Issuer, served, StringComparison.Ordinal))
                {
                    standing[admission] = AdmissionStanding.NotServed;
                }
            }

            var ordered = read.OrderByDescending(admission => admission.Admitted)
                .ThenBy(admission => admission.Entry, StringComparer.Ordinal)
                .ToList();
            return new AdmissionListing(ordered, standing, unreadable, waiting.Count, true);
        }

        /// <summary>
        /// The certificates the records of kind `client` in force name, by the texts the core
        /// keeps, with what a write deletes taken out and what it adds put in: what the standing
        /// answers to a renewal are kept for once the write lands, `STORE.md` §618.
        /// </summary>
        public static ISet<string> Named(StoreReading reading, StoreWrite write)
        {
            var named = new HashSet<string>(StringComparer.Ordinal);
            if (reading != null && reading.InForce != null)
            {
                var deleted = new HashSet<string>(write == null ? Enumerable.Empty<string>() : write.Delete, StringComparer.Ordinal);
                named.UnionWith(reading.InForce.Admissions
                    .Where(record => record.InForce && !deleted.Contains(record.Hex))
                    .Select(record => record.Form.Member(StoreKinds.Certificate))
                    .Where(certificate => certificate != null));
            }

            if (write != null)
            {
                named.UnionWith(write.Add
                    .Where(record => record.Kind == StoreKinds.Client)
                    .Select(record => record.Member(StoreKinds.Certificate))
                    .Where(certificate => certificate != null));
            }

            return named;
        }

        /// <summary>
        /// Whether a decision refuses an admission: a revocation of its client key, its certificate
        /// taken away, or a retirement of its issuer, by record, item or deletion record.
        /// </summary>
        private static bool Refused(StoreInForce inForce, AdmissionRecord admission)
        {
            return RefusedByText(inForce, admission.ClientKey, admission.AdmissionKey, admission.Issuer);
        }

        private static bool RefusedByText(StoreInForce inForce, string client, string certificate, string issuer)
        {
            if (client != null && inForce.Taken(StoreKinds.Revoked).Contains(client))
            {
                return true;
            }

            if (certificate != null && inForce.Taken(StoreKinds.Withdrawn).Contains(certificate))
            {
                return true;
            }

            return issuer != null && inForce.Taken(StoreKinds.Retired).Contains(issuer);
        }
    }
}
