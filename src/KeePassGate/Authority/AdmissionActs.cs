using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The acts that write admissions, revocations and certificates taken away into a database's
    /// store, `STORE.md` §615 and §648, each composed under the key of the reading it is composed
    /// over: a reading made in the turn of the write, or a read of the KPG tab's buffer, which
    /// composes the acts it holds again over the store as it stands then.
    /// </summary>
    /// <remarks>
    /// **Composed, not written**: the writer plans and applies an act, and the KPG tab's buffer
    /// holds it until the dialog's OK. Each act holds a copy of the store key, which whoever holds
    /// it overwrites once it is done with it, `StoreAct.Forget`.
    ///
    /// **A store with no key is given no act**: where the reading holds nothing in force, these
    /// answer null, and the caller states what the store is.
    /// </remarks>
    internal static class AdmissionActs
    {
        /// <summary>What the log calls an admission's act.</summary>
        public const string AdmitName = "admitting a client";

        /// <summary>What the log calls a revocation's act.</summary>
        public const string RevokeName = "revoking a client";

        /// <summary>What the log calls the act of taking a certificate away.</summary>
        public const string TakeAwayName = "taking a certificate away";

        /// <summary>
        /// Whether an act of this name is one of the acts composed here, about a client rather
        /// than about the authority.
        /// </summary>
        public static bool Composes(string actName)
        {
            return string.Equals(actName, AdmitName, StringComparison.Ordinal)
                   || string.Equals(actName, RevokeName, StringComparison.Ordinal)
                   || string.Equals(actName, TakeAwayName, StringComparison.Ordinal);
        }

        /// <summary>
        /// An act on a client held for a later write, composed again against this reading: what is
        /// to be done stays, and what it is written as is what the store calls for now. The act as
        /// it was where the reading holds no store key, and for an act of any other name.
        /// </summary>
        public static StoreAct Again(StoreReading reading, StoreAct act)
        {
            if (act == null)
            {
                throw new ArgumentNullException(nameof(act));
            }

            if (string.Equals(act.Name, RevokeName, StringComparison.Ordinal))
            {
                return Revoke(reading, act.Records[0].Member(StoreKinds.ClientMember)) ?? act;
            }

            return act;
        }

        /// <summary>
        /// An act of a single record of kind `client` holding this admission, held to its issuer
        /// standing alone, or null where the reading holds no store key.
        /// </summary>
        public static StoreAct Admit(StoreReading reading, AdmissionRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var issuer = record.Issuer;
            return Act(reading, AdmitName, key => new[] { StoreRecord.Compose(key, StoreKinds.Client, record.Members()) },
                ground => ground.Standing.Count == 1 && ground.Standing.Contains(issuer)
                    ? null
                    : "the authority that issued its certificate does not stand alone");
        }

        /// <summary>
        /// An act of a single record of kind `revoked` holding the client key alone, with no
        /// ground, `STORE.md` §615 (S5), or null where the reading holds no store key.
        /// </summary>
        public static StoreAct Revoke(StoreReading reading, string clientKey)
        {
            if (!CertificateDigest.IsDigest(clientKey))
            {
                throw new ArgumentException("A revocation names a client by its client key.", nameof(clientKey));
            }

            return Act(reading, RevokeName, key => new[]
            {
                StoreRecord.Compose(key, StoreKinds.Revoked, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.ClientMember, clientKey },
                }),
            }, null);
        }

        /// <summary>
        /// An act of the records of kind `withdrawn` that take this certificate away, with no
        /// ground, `STORE.md` §648 (S29), or null where the reading holds no store key: a record
        /// for each certificate of origin the admissions in force that read hold for it, an absent
        /// origin among them, each naming in `renewed` every admission in force that reads and was
        /// renewed from it, whatever else refuses that admission.
        /// </summary>
        public static StoreAct TakeAway(StoreReading reading, string admissionKey)
        {
            if (!CertificateDigest.IsDigest(admissionKey))
            {
                throw new ArgumentException("A certificate is taken away by its admission key.", nameof(admissionKey));
            }

            var admissions = Readable(reading);
            var renewed = string.Join(" ", admissions
                .Where(admission => string.Equals(admission.From, admissionKey, StringComparison.Ordinal))
                .Select(admission => admission.AdmissionKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal));
            var origins = admissions
                .Where(admission => string.Equals(admission.AdmissionKey, admissionKey, StringComparison.Ordinal))
                .Select(admission => admission.From)
                .Concat(RecordedOrigins(reading, admissionKey))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (origins.Count == 0)
            {
                origins.Add(null);
            }

            // Where no admission in force of the certificate reads, the certificate admitted
            // nobody and ends nothing renewed from it: its statement names nothing, and what was
            // renewed from it stays refused.
            var counted = admissions.Any(admission => string.Equals(admission.AdmissionKey, admissionKey, StringComparison.Ordinal));

            return Act(reading, TakeAwayName, key => origins
                .OrderBy(from => from ?? string.Empty, StringComparer.Ordinal)
                .Select(from =>
                {
                    var members = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        { StoreKinds.Certificate, admissionKey },
                        { StoreKinds.Renewed, counted ? renewed : string.Empty },
                    };
                    if (from != null)
                    {
                        members[StoreKinds.From] = from;
                    }

                    return StoreRecord.Compose(key, StoreKinds.Withdrawn, members);
                })
                .ToList(), null);
        }

        /// <summary>
        /// Whether a write planned for an admission's act adds a record of kind `client`, which is
        /// what a pairing's and a renewal's write is believed on.
        /// </summary>
        /// <remarks>
        /// A plan adds no record the tree already holds, so a plan can come to nothing. Believed on
        /// a save of nothing, a pairing or a renewal would hand a certificate over with no
        /// admission in the file, which nobody could see and nobody could revoke.
        /// </remarks>
        public static bool AddsAdmission(StoreWrite write)
        {
            return write != null && write.Refused == null && !write.Empty
                   && write.Add.Any(record => string.Equals(record.Kind, StoreKinds.Client, StringComparison.Ordinal));
        }

        /// <summary>
        /// The `from` of every record of kind `client` in force whose `certificate` is this text
        /// and whose `from` is an admission key, whether or not the record reads as an admission.
        /// </summary>
        private static IEnumerable<string> RecordedOrigins(StoreReading reading, string admissionKey)
        {
            if (reading == null || reading.InForce == null)
            {
                return Enumerable.Empty<string>();
            }

            return reading.InForce.Admissions
                .Where(record => record.InForce && string.Equals(record.Form.Member(StoreKinds.Certificate), admissionKey, StringComparison.Ordinal))
                .Select(record => record.Form.Member(StoreKinds.From))
                .Where(CertificateDigest.IsDigest);
        }

        /// <summary>The admissions in force that read, whatever else refuses them.</summary>
        private static IReadOnlyList<AdmissionRecord> Readable(StoreReading reading)
        {
            if (reading == null || reading.InForce == null)
            {
                return Array.Empty<AdmissionRecord>();
            }

            return reading.InForce.Admissions
                .Where(record => record.InForce)
                .Select(record => AdmissionRecord.Read(record.Hex, record.Form.Members))
                .Where(admission => admission != null)
                .ToList();
        }

        /// <summary>
        /// An act of the records these compose under the key the reading holds, against that key,
        /// with the copy of the key made for it overwritten, or null where no key stands.
        /// </summary>
        private static StoreAct Act(
            StoreReading reading, string name, Func<byte[], IReadOnlyList<StoreRecord>> records, Func<StoreGround, string> ground)
        {
            if (reading == null || reading.InForce == null)
            {
                return null;
            }

            var key = (byte[])reading.InForce.Key.Bytes.Clone();
            try
            {
                return new StoreAct(name, key, reading.InForce.Key.Name, records(key), ground);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
