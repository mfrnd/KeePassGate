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
    /// <see cref="IsPaired"/> means the client presented a certificate that this database's own
    /// authority issued, checked by pinning the anchor rather than by trusting a chain. TODO(step 7): from step 7
    /// it will also require an admission record, which is where <see cref="Profiles"/> comes from;
    /// until then that list is empty and no route reads it.
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
