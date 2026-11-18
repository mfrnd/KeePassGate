using System;
using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The admissions of a database's store as the operator's screens list them, `STORE.md` §615:
    /// each admission in force that reads, with what it comes to, the records that do not read,
    /// and whether that is all of them.
    /// </summary>
    /// <remarks>
    /// A store that will not answer and a database that has admitted nobody both produce no
    /// admissions, and telling them apart only in the log made them one answer to every caller.
    /// On the gate that costs nothing, because the safe reply to "may this certificate in" is no
    /// either way. Wherever the count is shown to a person it is not: an empty list reads as
    /// nobody being admitted, which is a sentence about who has access, and saying it when
    /// nothing could be counted tells the operator something false about their own file.
    ///
    /// **Deliberately not a list.** Being enumerable would let a caller write a `foreach` and never
    /// learn there is a question to ask. Reaching the admissions through `Records` costs one word
    /// and puts `Complete` in front of whoever reads it.
    /// </remarks>
    public sealed class AdmissionListing
    {
        private static readonly IReadOnlyDictionary<AdmissionRecord, AdmissionStanding> NoStanding =
            new Dictionary<AdmissionRecord, AdmissionStanding>();

        private readonly IReadOnlyList<AdmissionRecord> _records;
        private readonly IReadOnlyDictionary<AdmissionRecord, AdmissionStanding> _standing;
        private readonly IReadOnlyList<UnreadAdmission> _unreadable;
        private readonly int _waiting;
        private readonly bool _complete;

        /// <summary>These admissions, each admitting, and nothing else.</summary>
        public AdmissionListing(IReadOnlyList<AdmissionRecord> records, bool complete)
            : this(records, null, null, 0, complete)
        {
        }

        /// <summary>
        /// These admissions, each with what it comes to, the records that do not read, and how
        /// many clients wait for their admissions to be deleted.
        /// </summary>
        /// <param name="standing">
        /// What each admission comes to, an admission absent from it admitting.
        /// </param>
        /// <param name="unreadable">
        /// The records of kind `client` in force that do not read as admissions.
        /// </param>
        /// <param name="waiting">
        /// How many clients a decision refuses whose admissions wait to be deleted.
        /// </param>
        public AdmissionListing(
            IReadOnlyList<AdmissionRecord> records,
            IReadOnlyDictionary<AdmissionRecord, AdmissionStanding> standing,
            IReadOnlyList<UnreadAdmission> unreadable,
            int waiting,
            bool complete)
        {
            _records = records ?? Array.Empty<AdmissionRecord>();
            _standing = standing ?? NoStanding;
            _unreadable = unreadable ?? Array.Empty<UnreadAdmission>();
            _waiting = Math.Max(0, waiting);
            _complete = complete;
        }

        /// <summary>
        /// Every admission in force that reads and that no revocation, retirement or certificate
        /// taken away refuses, newest first.
        /// </summary>
        public IReadOnlyList<AdmissionRecord> Records
        {
            get { return _records; }
        }

        /// <summary>What an admission of <see cref="Records"/> comes to.</summary>
        public AdmissionStanding StandingOf(AdmissionRecord record)
        {
            AdmissionStanding standing;
            return record != null && _standing.TryGetValue(record, out standing) ? standing : AdmissionStanding.Admitted;
        }

        /// <summary>
        /// The records of kind `client` in force that do not read as admissions, which admit nobody
        /// and are named by their entry, `STORE.md` §615.
        /// </summary>
        public IReadOnlyList<UnreadAdmission> Unreadable
        {
            get { return _unreadable; }
        }

        /// <summary>
        /// How many clients a revocation, a retirement or a certificate taken away refuses while
        /// their admissions are still in the file, which the plugin's next write deletes,
        /// `STORE.md` §627. Counted by client key, and by entry for a record that names no client
        /// key.
        /// </summary>
        public int Waiting
        {
            get { return _waiting; }
        }

        /// <summary>
        /// Whether the store was read at all. False tells nothing about how many clients the
        /// database has admitted, only that this does not know.
        /// </summary>
        public bool Complete
        {
            get { return _complete; }
        }
    }

    /// <summary>What an admission in force that reads comes to, `STORE.md` §615.</summary>
    public enum AdmissionStanding
    {
        /// <summary>It admits its certificate under the authority served.</summary>
        Admitted,

        /// <summary>Its issuer is not the authority served, or no authority is.</summary>
        NotServed,

        /// <summary>
        /// Several admissions in force name its certificate, so none of them admits (S24).
        /// </summary>
        Twice,

        /// <summary>
        /// A renewal that does not count, `STORE.md` §648: along its chain of origins, a
        /// certificate taken away by a statement that does not name the certificate renewed from
        /// it, a certificate neither admitted nor taken away, or a certificate met a second time.
        /// </summary>
        Uncounted,
    }

    /// <summary>
    /// A record of kind `client` that passes its check and does not read as an admission: it
    /// admits nobody and is named by its entry, with the texts of its client key and certificate
    /// where they are keys, `STORE.md` §615.
    /// </summary>
    public sealed class UnreadAdmission
    {
        public UnreadAdmission(string entry, string clientKey, string certificate)
        {
            Entry = entry;
            ClientKey = CertificateDigest.IsDigest(clientKey) ? clientKey : null;
            Certificate = CertificateDigest.IsDigest(certificate) ? certificate : null;
        }

        /// <summary>The identifier of its entry, as lowercase hexadecimal.</summary>
        public string Entry { get; private set; }

        /// <summary>Its `client` where that is a client key, else null.</summary>
        public string ClientKey { get; private set; }

        /// <summary>Its `certificate` where that is an admission key, else null.</summary>
        public string Certificate { get; private set; }
    }
}
