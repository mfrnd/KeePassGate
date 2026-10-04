using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// A standing answer to renewing one certificate that its database could not take when the
    /// operator gave it.
    /// </summary>
    public sealed class OwedAnswer
    {
        public OwedAnswer(string databaseId, string admissionKey, RenewalRefusal refusal)
        {
            DatabaseId = databaseId;
            AdmissionKey = admissionKey;
            Refusal = refusal;
        }

        /// <summary>The authority whose database it belongs in.</summary>
        public string DatabaseId { get; private set; }

        /// <summary>The certificate it is about.</summary>
        public string AdmissionKey { get; private set; }

        /// <summary>Forever, or until a date: an answer the database keeps.</summary>
        public RenewalRefusal Refusal { get; private set; }
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
