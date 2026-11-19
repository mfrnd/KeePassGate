using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The acts that write admissions, revocations and certificates taken away into a database's
    /// store, `STORE.md` §615 and §648, each composed under the key of the reading it is composed
    /// over: a reading made in the turn of the write, or a read of the KPG tab's buffer, which
    /// composes a revocation and a certificate taken away again over the store as it stands then,
    /// `StoreAct.Again`; a certificate with nothing left of it to end is composed to nothing.
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
        /// Whether nothing of this certificate is left for a statement to end: a statement took it
        /// away, or no record of kind `client` in force names it under a client the store does not
        /// take away, whether or not the record reads.
        /// </summary>
        /// <remarks>
        /// **The second case is a client a revocation took away, and a record deleted by hand**,
        /// also where the deletion is merged in while the settings dialog is open: an act on such
        /// a certificate is refused at a press, and writes nothing at OK, where the log names it. A
        /// record of the certificate that a later merge brings back is admitted again, as any
        /// record deleted by hand is, `STORE.md` §639.
        /// </remarks>
        private static bool Ended(StoreInForce inForce, string certificate)
        {
            if (inForce.Taken(StoreKinds.Withdrawn).Contains(certificate))
            {
                return true;
            }

            var revoked = inForce.Taken(StoreKinds.Revoked);
            return !inForce.Admissions.Any(record => record.InForce
                && string.Equals(record.Form.Member(StoreKinds.Certificate), certificate, StringComparison.Ordinal)
                && !revoked.Contains(record.Form.Member(StoreKinds.ClientMember)));
        }

        /// <summary>
        /// An act of the record of kind `client` holding this admission and, after it, an addition
        /// for each profile it holds that the reading does not have on the list, held to its issuer
        /// standing alone, or null where the reading holds no store key.
        /// </summary>
        /// <remarks>
        /// **The names are records of the admission's own act**, `STORE.md` §616: the same decision
        /// as the admission and the same write, so that the names and the admission are planned,
        /// saved and taken back together, and left out together where the issuer does not stand
        /// alone. Each addition names every removal of its name the reading holds, so a name the
        /// operator approved after its removal is back on the list, and it is written whatever the
        /// list already holds, since the list keeps no ceiling. A renewal's admission holds only
        /// names the list holds, and adds none.
        /// </remarks>
        public static StoreAct Admit(StoreReading reading, AdmissionRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var issuer = record.Issuer;
            var named = ProfileNames.Of(reading);
            var adding = (record.Profiles ?? Array.Empty<string>())
                .Where(profile => AuthorityIssuer.IsUsableProfileName(profile) && !named.Holds(profile))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(profile => profile, StringComparer.Ordinal)
                .ToList();
            return Act(
                reading,
                AdmitName,
                key => new[] { StoreRecord.Compose(key, StoreKinds.Client, record.Members()) }
                    .Concat(adding.Select(profile => ProfileActs.Addition(key, profile, reading.InForce.RemovalsOf(profile))))
                    .ToList(),
                ground => ground.Standing.Count == 1 && ground.Standing.Contains(issuer)
                    ? null
                    : "the authority that issued its certificate does not stand alone",
                null);
        }

        /// <summary>
        /// An act of a single record of kind `revoked` holding the client key alone, with no
        /// ground, `STORE.md` §615 (S5), or null where the reading holds no store key. Composed
        /// again under the key of each later reading.
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
            }, null, again => Revoke(again, clientKey));
        }

        /// <summary>
        /// An act of the records of kind `withdrawn` that take this certificate away, with no
        /// ground, `STORE.md` §648 (S29), or null where the reading holds no store key: a record
        /// for each certificate of origin its records of kind `client` in force hold, an absent
        /// origin among them, each naming in `renewed` every certificate the reading holds as
        /// renewed from it where an admission in force of this certificate reads, and nothing
        /// where none does.
        /// </summary>
        /// <remarks>
        /// **Renewed from it** is the certificate of every admission in force that reads and holds
        /// it in `from`, whatever else refuses that admission, and of every statement that took a
        /// certificate away and holds it in `from`, a record of kind `withdrawn` present or an
        /// item of that kind. A deletion record holds no origin, so nothing it tells of is named.
        /// A certificate between that was taken away first is named so, and what was renewed from
        /// it keeps counting; a renewal another copy made after this statement is named by no
        /// statement written before it reached this copy, and stays refused.
        ///
        /// **A certificate no admission in force of which reads names nothing**: it admitted
        /// nobody, so taking it away ends nothing renewed from it, which stays refused. Its
        /// statement holds the origins its records name all the same, whether or not they read,
        /// so that the chain is asked onward from them.
        /// </remarks>
        public static StoreAct TakeAway(StoreReading reading, string admissionKey)
        {
            if (!CertificateDigest.IsDigest(admissionKey))
            {
                throw new ArgumentException("A certificate is taken away by its admission key.", nameof(admissionKey));
            }

            var admissions = Readable(reading);
            var inForce = reading == null ? null : reading.InForce;
            var statements = inForce == null
                ? Enumerable.Empty<string>()
                : inForce.Taken(StoreKinds.Withdrawn).Where(certificate => inForce.StatementsOf(certificate)
                    .Any(statement => string.Equals(statement.From, admissionKey, StringComparison.Ordinal)));
            var renewed = string.Join(" ", admissions
                .Where(admission => string.Equals(admission.From, admissionKey, StringComparison.Ordinal))
                .Select(admission => admission.AdmissionKey)
                .Concat(statements)
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
                .ToList(), null, again => Ended(again.InForce, admissionKey) ? null : TakeAway(again, admissionKey));
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
        /// <param name="again">The act composed again against a later reading, or null for an act composed once.</param>
        private static StoreAct Act(
            StoreReading reading,
            string name,
            Func<byte[], IReadOnlyList<StoreRecord>> records,
            Func<StoreGround, string> ground,
            Func<StoreReading, StoreAct> again)
        {
            if (reading == null || reading.InForce == null)
            {
                return null;
            }

            var key = (byte[])reading.InForce.Key.Bytes.Clone();
            try
            {
                return new StoreAct(name, key, reading.InForce.Key.Name, records(key), ground, again);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
