using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// A standing answer to renewing one certificate that its database could not take when the
    /// operator gave it.
    /// </summary>
    /// <remarks>
    /// **With what the database held when it was given**, because that is what tells a later
    /// answer from an earlier answer. Nothing else can: KeePass keeps a time per key and offers
    /// no public way to read it, and this answer has no moment the database could compare. So it
    /// is written only while its key still holds that, and an answer written there since, the
    /// operator's own on a later dialog or an answer a synchronize brought in, is the later word.
    /// </remarks>
    public sealed class OwedAnswer
    {
        public OwedAnswer(string databaseId, string admissionKey, RenewalRefusal refusal, RenewalRefusal baseline)
        {
            DatabaseId = databaseId;
            AdmissionKey = admissionKey;
            Refusal = refusal;
            Baseline = baseline;
        }

        /// <summary>The authority whose database it belongs in.</summary>
        public string DatabaseId { get; private set; }

        /// <summary>The certificate it is about.</summary>
        public string AdmissionKey { get; private set; }

        /// <summary>Forever, or until a date: an answer the database keeps.</summary>
        public RenewalRefusal Refusal { get; private set; }

        /// <summary>
        /// The answer the database held about this certificate when this answer was given, or null
        /// for none.
        /// </summary>
        public RenewalRefusal Baseline { get; private set; }
    }

    /// <summary>
    /// The standing answers owed to databases, which the plugin's next write to each that lands
    /// writes as well.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-10-14: a standing answer given while KeePass was busy takes
    /// effect at once, from memory, and is written at the plugin's next successful write.
    /// `DatabaseWriter` asks this inside every write it makes and tells it what landed.
    /// </remarks>
    public interface IOwedAnswers
    {
        /// <summary>Every answer still owed, to any database.</summary>
        IReadOnlyList<OwedAnswer> Owed();

        /// <summary>Says this answer is in its database now.</summary>
        void Kept(OwedAnswer answer);
    }
}
