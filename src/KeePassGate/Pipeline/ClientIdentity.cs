using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Who is asking, and the ceiling they carry. Small on purpose: a certificate proves identity
    /// and grants nothing, so there is very little here.
    /// </summary>
    /// <remarks>
    /// <see cref="IsPaired"/> means all four of: a certificate this database's own authority
    /// issued, checked by pinning the anchor rather than by trusting a chain; the `clientAuth`
    /// usage; dates that are current; and an admission record in the database with no revocation
    /// beside it. Take away the record, or add the revocation, and the same certificate admits
    /// nobody from its next connection on, which is all a revocation has to do.
    ///
    /// <see cref="Profiles"/> comes from that record and never from the certificate's claims.
    /// </remarks>
    public sealed class ClientIdentity
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private readonly bool _isPaired;
        private readonly string _subject;
        private readonly string _admissionKey;
        private readonly string _databaseId;
        private readonly IReadOnlyList<string> _profiles;

        private ClientIdentity(
            bool isPaired, string subject, string admissionKey, string databaseId, IReadOnlyList<string> profiles)
        {
            _isPaired = isPaired;
            _subject = subject;
            _admissionKey = admissionKey;
            _databaseId = databaseId;
            _profiles = profiles ?? Nothing;
        }

        /// <summary>False for a stranger, which only the pairing path will ever serve.</summary>
        public bool IsPaired
        {
            get { return _isPaired; }
        }

        /// <summary>The certificate's subject, for a log line at pairing. Null for a stranger.</summary>
        public string Subject
        {
            get { return _subject; }
        }

        /// <summary>SHA-256 over the certificate's DER, lowercase hex. Null for a stranger.</summary>
        public string AdmissionKey
        {
            get { return _admissionKey; }
        }

        /// <summary>Which database the name resolved to.</summary>
        public string DatabaseId
        {
            get { return _databaseId; }
        }

        /// <summary>From the admission record, never from the certificate.</summary>
        public IReadOnlyList<string> Profiles
        {
            get { return _profiles; }
        }

        /// <summary>
        /// Somebody who reached a database and proved nothing. Present rather than null, because
        /// pairing has to be served and a null identity is a null check somebody will forget.
        /// </summary>
        public static ClientIdentity Stranger(string databaseId)
        {
            return new ClientIdentity(false, null, null, databaseId, Nothing);
        }

        /// <summary>A client whose certificate this database's authority issued.</summary>
        public static ClientIdentity Paired(
            string databaseId, X509Certificate2 certificate, IReadOnlyList<string> profiles)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return new ClientIdentity(
                true,
                certificate.Subject,
                CertificateDigest.Of(certificate),
                databaseId,
                profiles);
        }
    }
}
