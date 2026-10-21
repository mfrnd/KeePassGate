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
    /// <see cref="Profiles"/> comes from that record and never from the certificate's claims, less
    /// every name the database's list no longer holds. Those names are <see cref="Removed"/>,
    /// which grants nothing and is kept only so a refusal they would have permitted can say so.
    ///
    /// **A refused certificate this authority issued is not anonymous**, and is kept apart from
    /// one that proved nothing. It admits nobody, exactly as a stranger does, but it carries its
    /// admission key and why it was refused, so the router can write the refusal down against the
    /// key the Clients page revoked. A certificate from somewhere else carries neither: it names
    /// nobody the operator issued, and recording it would let anybody who reaches the port grow the
    /// audit.
    /// </remarks>
    public sealed class ClientIdentity
    {
        private static readonly string[] Nothing = Array.Empty<string>();

        private readonly bool _isPaired;
        private readonly bool _isLocked;
        private readonly string _subject;
        private readonly string _admissionKey;
        private readonly string _databaseId;
        private readonly IReadOnlyList<string> _profiles;
        private readonly IReadOnlyList<string> _removed;
        private readonly string _refusal;
        private readonly string _unavailable;
        private readonly DateTimeOffset? _expires;
        private readonly string _clientKey;

        private ClientIdentity(
            bool isPaired,
            bool isLocked,
            string subject,
            string admissionKey,
            string databaseId,
            IReadOnlyList<string> profiles,
            string refusal,
            string unavailable = null,
            IReadOnlyList<string> removed = null,
            DateTimeOffset? expires = null,
            string clientKey = null)
        {
            _removed = removed ?? Nothing;
            _expires = expires;
            _clientKey = clientKey;
            _isPaired = isPaired;
            _isLocked = isLocked;
            _unavailable = unavailable ?? (isLocked ? Gate.LockedTarget.Error : null);
            _subject = subject;
            _admissionKey = admissionKey;
            _databaseId = databaseId;
            _profiles = profiles ?? Nothing;
            _refusal = refusal;
        }

        /// <summary>False for a stranger, which only the pairing path will ever serve.</summary>
        public bool IsPaired
        {
            get { return _isPaired; }
        }

        /// <summary>
        /// The database this connection reached is locked, so nothing about the caller could be
        /// established: the authority that would have validated its certificate is not loaded.
        /// </summary>
        public bool IsLocked
        {
            get { return _isLocked; }
        }

        /// <summary>The certificate's subject, answered by `/gate/whoami`. Null for a stranger.</summary>
        public string Subject
        {
            get { return _subject; }
        }

        /// <summary>
        /// SHA-256 over the certificate's DER, lowercase hex. Null for a stranger, and set for a
        /// certificate this authority issued that was refused.
        /// </summary>
        public string AdmissionKey
        {
            get { return _admissionKey; }
        }

        /// <summary>
        /// Which client this is: the client key its admission record carries, the admission key
        /// of the certificate it paired with, which a renewal keeps. Null for anybody who is not
        /// paired.
        /// </summary>
        /// <remarks>
        /// The key the operator knows a client by, `CA.md` §223, since every count and naming of
        /// clients on the KPG tab goes by it. <see cref="AdmissionKey"/> is the certificate on this
        /// connection, which is the one the audit names and a revocation takes away.
        /// </remarks>
        public string ClientKey
        {
            get { return _clientKey; }
        }

        /// <summary>Which database the name resolved to.</summary>
        public string DatabaseId
        {
            get { return _databaseId; }
        }

        /// <summary>
        /// From the admission record, never from the certificate, and only the names the database
        /// still lists.
        /// </summary>
        public IReadOnlyList<string> Profiles
        {
            get { return _profiles; }
        }

        /// <summary>
        /// The names the admission record holds that the database no longer lists, in the
        /// record's order. Empty for anybody who is not paired.
        /// </summary>
        /// <remarks>
        /// **Never handed to the rules as a grant.** It is here for one question only, asked
        /// after a refusal: whether the record's whole list would have been permitted, which is
        /// what lets the audit say a refusal was the operator removing a profile rather than a
        /// rule saying no. See `AccessCheck.Decide`.
        /// </remarks>
        public IReadOnlyList<string> Removed
        {
            get { return _removed; }
        }

        /// <summary>
        /// Why a certificate this authority issued admits nobody, in the plugin's own words, or
        /// null for anybody else.
        /// </summary>
        public string Refusal
        {
            get { return _refusal; }
        }

        /// <summary>
        /// When the certificate this client presented stops being admitted, in UTC, or null for
        /// anybody who is not paired.
        /// </summary>
        /// <remarks>
        /// Read off the certificate on the connection, which is the one the record admits: the
        /// record is keyed by its digest. A client certificate ends no later than its authority,
        /// which is issued for a hundred years, so the date reads back exactly here, where one in
        /// the year 9999 would not: `CLAUDE.md` says why under "Measured facts".
        /// </remarks>
        public DateTimeOffset? Expires
        {
            get { return _expires; }
        }

        /// <summary>
        /// Somebody who reached a database and proved nothing. Present rather than null, because
        /// pairing has to be served and a null identity is a null check somebody will forget.
        /// </summary>
        public static ClientIdentity Stranger(string databaseId)
        {
            return new ClientIdentity(false, false, null, null, databaseId, Nothing, null);
        }

        /// <summary>
        /// A certificate this database's authority issued, refused: no admission record, the wrong
        /// usage, or outside its dates. Served as a stranger, and written down as one it can name.
        /// </summary>
        public static ClientIdentity Refused(string databaseId, X509Certificate2 certificate, string reason)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return new ClientIdentity(
                false, false, null, CertificateDigest.Of(certificate), databaseId, Nothing, reason);
        }

        /// <summary>
        /// Somebody who reached a locked database. Not a stranger: a stranger can pair, and this
        /// caller cannot do anything at all until somebody unlocks KeePass.
        /// </summary>
        public static ClientIdentity Locked(string databaseId)
        {
            return new ClientIdentity(false, true, null, null, databaseId, Nothing, null);
        }

        /// <summary>
        /// Somebody who reached a database that is served by nobody for now, for the reason the
        /// word gives. A locked database is one; one holding several authorities is the other.
        /// </summary>
        public static ClientIdentity Unavailable(string databaseId, string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                throw new ArgumentException("An unavailable database says why.", nameof(error));
            }

            return error == Gate.LockedTarget.Error
                ? Locked(databaseId)
                : new ClientIdentity(false, false, null, null, databaseId, Nothing, null, error);
        }

        /// <summary>
        /// The one word this connection is answered with under `503` before anything it asked is
        /// read, or null when the database it reached is served.
        /// </summary>
        public string UnavailableError
        {
            get { return _unavailable; }
        }

        /// <summary>
        /// A client whose certificate this database's authority issued, holding these profiles,
        /// and holding on its record these others that the database no longer lists.
        /// </summary>
        /// <param name="clientKey">
        /// The client key the record carries, or null for a client that has not renewed, whose
        /// client key is its own certificate's admission key.
        /// </param>
        public static ClientIdentity Paired(
            string databaseId,
            X509Certificate2 certificate,
            IReadOnlyList<string> profiles,
            IReadOnlyList<string> removed = null,
            string clientKey = null)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            var admissionKey = CertificateDigest.Of(certificate);
            return new ClientIdentity(
                true,
                false,
                certificate.Subject,
                admissionKey,
                databaseId,
                profiles,
                null,
                null,
                removed,
                new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero),
                clientKey ?? admissionKey);
        }
    }
}
