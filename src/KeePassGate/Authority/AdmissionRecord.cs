using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Diagnostics;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// One certificate, admitted: the content of a record of kind `client` in the plugin's store,
    /// `STORE.md` §615. This is what a certificate is worth: without an admission, a certificate
    /// that chains and carries the right usage opens nothing at all.
    /// </summary>
    /// <remarks>
    /// It carries the certificate rather than only its digest, which is what makes expiry knowable
    /// and what lets the tab say something useful about a client nobody remembers issuing.
    ///
    /// **The profiles here are the ones that count.** A certificate carries the same names as
    /// claims, but only as a convenience for a human reading it. `CA.md` §209 explains: an
    /// admission is what a lost record takes away, so reading a ceiling from the certificate
    /// would leave a credential nobody can see and nobody can revoke.
    ///
    /// **An admission names the client as well as the certificate**, by its
    /// <see cref="ClientKey"/>: the admission key of the first certificate the client was issued,
    /// the one it paired with. A renewal writes a second admission for the same client, naming the
    /// certificate it was renewed from in <see cref="From"/>, and a client chooses its own name, so
    /// neither the name nor the certificate tells one client from another; the client key does, and
    /// every count and naming of clients goes by it.
    ///
    /// **A member for each field**, `STORE.md` §615 (S4): <see cref="Members"/> writes them and
    /// <see cref="Read"/> reads them, and a content that is not exactly an admission reads as none.
    /// </remarks>
    public sealed class AdmissionRecord
    {
        private static readonly IReadOnlyList<string> Nothing =
            new ReadOnlyCollection<string>(Array.Empty<string>());

        /// <summary>What separates the names of `profiles`, `STORE.md` §615.</summary>
        private static readonly char[] Space = { ' ' };

        private readonly string _admissionKey;
        private readonly string _subject;
        private readonly string _clientKey;
        private readonly DateTimeOffset _admitted;
        private readonly IReadOnlyList<string> _profiles;
        private readonly byte[] _certificateDer;
        private readonly string _issuer;
        private readonly string _from;
        private readonly string _entry;

        /// <summary>
        /// An admission is a certificate, the digest it is filed under, what was approved, the
        /// authority that issued it, and for a renewal the certificate it was renewed from.
        /// </summary>
        /// <remarks>
        /// **The key has to be the digest of the certificate, and that is checked here**, so an
        /// admission whose two halves disagree cannot be composed, written and then read back as
        /// none: pairing would report success and the client it admitted would be refused for good.
        ///
        /// The client key, the issuer and the certificate renewed from are held to the shape of a
        /// digest for the same reason: one that is not would be written, and then read back as no
        /// admission at all.
        /// </remarks>
        /// <param name="entry">
        /// The identifier of the entry it was read from, or null for an admission composed.
        /// </param>
        public AdmissionRecord(
            string admissionKey,
            string subject,
            string clientKey,
            DateTimeOffset admitted,
            IEnumerable<string> profiles,
            byte[] certificateDer,
            string issuer,
            string from = null,
            string entry = null)
        {
            KeyedBy(admissionKey, certificateDer);

            if (!CertificateDigest.IsDigest(clientKey))
            {
                throw new ArgumentException(
                    "An admission names its client by the digest of the certificate it paired with.",
                    nameof(clientKey));
            }

            if (!CertificateDigest.IsDigest(issuer))
            {
                throw new ArgumentException(
                    "An admission names the authority that issued its certificate by that authority's id.",
                    nameof(issuer));
            }

            if (from != null && !CertificateDigest.IsDigest(from))
            {
                throw new ArgumentException(
                    "A renewal names the certificate it was renewed from by its admission key.",
                    nameof(from));
            }

            _admissionKey = admissionKey;
            _subject = subject ?? string.Empty;
            _clientKey = clientKey;
            _admitted = admitted;
            _profiles = Clean(profiles);
            _certificateDer = (byte[])certificateDer.Clone();
            _issuer = issuer;
            _from = from;
            _entry = entry;
        }

        /// <summary>SHA-256 over the certificate's DER, lowercase hex.</summary>
        public string AdmissionKey
        {
            get { return _admissionKey; }
        }

        /// <summary>The subject as issued, which is what names this client to the operator.</summary>
        /// <remarks>
        /// Stored beside the certificate rather than read off it, and not compared with it when an
        /// admission is read. Only whoever holds the store key can make the two disagree, and
        /// whoever can do that can write a whole admission for a certificate of their own, since
        /// the authority that signs such a certificate is kept in the same file, so a comparison
        /// would stop nobody.
        /// </remarks>
        public string Subject
        {
            get { return _subject; }
        }

        /// <summary>
        /// Which client this is: the admission key of the first certificate it was issued, the
        /// one it paired with, lowercase hex.
        /// </summary>
        /// <remarks>
        /// Pairing writes the new certificate's own key here, and a renewal copies it from the
        /// admission that asked, so every certificate a client has held carries the same one. Two
        /// admissions with one client key are one client, however many certificates it holds; two
        /// with one name and different client keys are two. A revocation names it, `STORE.md` §615.
        /// </remarks>
        public string ClientKey
        {
            get { return _clientKey; }
        }

        public DateTimeOffset Admitted
        {
            get { return _admitted; }
        }

        /// <summary>
        /// What the operator approved, each name once and in ordinal order. The only place a
        /// ceiling comes from.
        /// </summary>
        public IReadOnlyList<string> Profiles
        {
            get { return _profiles; }
        }

        /// <summary>
        /// The `id` of the authority that issued the certificate, `STORE.md` §614.
        /// </summary>
        public string Issuer
        {
            get { return _issuer; }
        }

        /// <summary>
        /// For an admission a renewal wrote, the admission key of the certificate it was renewed
        /// from, `STORE.md` §648; null for a pairing's.
        /// </summary>
        public string From
        {
            get { return _from; }
        }

        /// <summary>
        /// The identifier of the entry it was read from, as lowercase hexadecimal, or null for an
        /// admission composed and not read: what an approval held for a renewal is compared with,
        /// since a record is written once and the same entry is the same content, `STORE.md` §627.
        /// </summary>
        public string Entry
        {
            get { return _entry; }
        }

        /// <summary>
        /// The certificate, for reading dates and names off. The caller disposes it.
        /// </summary>
        /// <remarks>
        /// `EphemeralKeySet`, kept so that no key this plugin loads could reach the disk, though
        /// these bytes cannot carry one: the constructor refuses anything but a bare certificate,
        /// however an admission is built.
        /// </remarks>
        public X509Certificate2 Certificate()
        {
            return new X509Certificate2(_certificateDer, (string)null, X509KeyStorageFlags.EphemeralKeySet);
        }

        /// <summary>
        /// Whether the certificate this admits has expired by <paramref name="now"/>, and false when
        /// it will not read, which says nothing either way.
        /// </summary>
        /// <remarks>
        /// For the screens that say who regains a removed profile: an expired certificate regains
        /// nothing, since the gate refuses it on its dates before any admission is read.
        /// </remarks>
        public bool HasExpired(DateTimeOffset now)
        {
            try
            {
                using (var certificate = Certificate())
                {
                    return CertificateValidity.HasExpired(certificate, now);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                return false;
            }
        }

        /// <summary>
        /// The admission a pairing writes: a new client, whose client key is this certificate's own
        /// admission key.
        /// </summary>
        /// <param name="issuer">The `id` of the authority that issued the certificate.</param>
        public static AdmissionRecord For(
            X509Certificate2 certificate, DateTimeOffset admitted, IEnumerable<string> profiles, string issuer)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return For(certificate, admitted, profiles, issuer, CertificateDigest.Of(certificate), null);
        }

        /// <summary>
        /// The admission a renewal writes: the same client under a new certificate, its client key
        /// copied from the admission that asked, naming the certificate it was renewed from.
        /// </summary>
        /// <param name="issuer">The `id` of the authority that issued the certificate.</param>
        /// <param name="from">
        /// The admission key of the certificate renewed from, or null for a pairing's.
        /// </param>
        public static AdmissionRecord For(
            X509Certificate2 certificate,
            DateTimeOffset admitted,
            IEnumerable<string> profiles,
            string issuer,
            string clientKey,
            string from)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return new AdmissionRecord(
                CertificateDigest.Of(certificate),
                certificate.Subject,
                clientKey,
                admitted,
                profiles,
                certificate.RawData,
                issuer,
                from);
        }

        /// <summary>
        /// The members of the admission's content, `STORE.md` §615, without the nonce, which the
        /// record is composed with: `profiles` in ordinal order and separated by single spaces,
        /// `admitted` as `Moments.Wire` writes a moment, and `from` only for a renewal's.
        /// </summary>
        public IReadOnlyDictionary<string, string> Members()
        {
            var members = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.Certificate, _admissionKey },
                { StoreKinds.Der, Convert.ToBase64String(_certificateDer) },
                { StoreKinds.ClientMember, _clientKey },
                { StoreKinds.Issuer, _issuer },
                { StoreKinds.Profiles, string.Join(" ", _profiles) },
                { StoreKinds.IssuedSubject, _subject },
                { StoreKinds.Admitted, Moments.Wire(_admitted) },
            };

            if (_from != null)
            {
                members[StoreKinds.From] = _from;
            }

            return members;
        }

        /// <summary>
        /// The admission a record of kind `client` holds, or null for whatever is no admission,
        /// `STORE.md` §615: a member but `from` absent; `certificate`, `client`, `issuer` or `from`
        /// not 64 lowercase hexadecimal characters; a `der` that is no base64 of a bare certificate
        /// whose digest is `certificate`; a moment that does not parse in the form it is written
        /// in. A member it does not name is passed over, `STORE.md` §607.
        /// </summary>
        /// <remarks>
        /// **Total.** Nothing a content holds makes it throw: an admission that does not read
        /// admits nobody, which is the answer for no admission at all.
        /// </remarks>
        /// <param name="entry">The identifier of the entry the record was read from.</param>
        /// <param name="members">The members the core kept of the record's content.</param>
        public static AdmissionRecord Read(string entry, IReadOnlyDictionary<string, string> members)
        {
            if (members == null)
            {
                return null;
            }

            var certificate = Text(members, StoreKinds.Certificate);
            var der = Text(members, StoreKinds.Der);
            var client = Text(members, StoreKinds.ClientMember);
            var issuer = Text(members, StoreKinds.Issuer);
            var profiles = Text(members, StoreKinds.Profiles);
            var subject = Text(members, StoreKinds.IssuedSubject);
            var admitted = Text(members, StoreKinds.Admitted);
            var from = Text(members, StoreKinds.From);

            if (der == null || profiles == null || subject == null || admitted == null)
            {
                return null;
            }

            if (!CertificateDigest.IsDigest(certificate) || !CertificateDigest.IsDigest(client)
                || !CertificateDigest.IsDigest(issuer) || (from != null && !CertificateDigest.IsDigest(from)))
            {
                return null;
            }

            // `Moments.WireFormat` alone, RFC 3339 with a `Z` and no fractional seconds: the single
            // form an admission is written in, `STORE.md` §615.
            DateTimeOffset when;
            if (!DateTimeOffset.TryParseExact(
                    admitted,
                    Moments.WireFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out when))
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(der);
            }
            catch (FormatException)
            {
                return null;
            }

            if (bytes.Length == 0 || !IsKeyedBareCertificate(certificate, bytes))
            {
                return null;
            }

            return new AdmissionRecord(
                certificate,
                subject,
                client,
                when,
                profiles.Split(Space, StringSplitOptions.RemoveEmptyEntries),
                bytes,
                issuer,
                from,
                entry);
        }

        /// <summary>A member's text, or null where the content does not hold it.</summary>
        private static string Text(IReadOnlyDictionary<string, string> members, string name)
        {
            string value;
            return members.TryGetValue(name, out value) ? value : null;
        }

        /// <summary>
        /// Whether these bytes are a bare certificate whose digest is this key.
        /// </summary>
        /// <remarks>
        /// **A bare certificate and nothing else.** Measured on .NET Framework 4.8:
        /// `X509Certificate2(byte[])` sniffs the blob, so a password-less PKCS#12 and a PEM both
        /// load through it and come back re-encoded. The certificate an admission carries need
        /// therefore not be the bytes it is keyed on, and a digest check alone cannot see that,
        /// because the key is a digest of whatever was stored.
        ///
        /// `EphemeralKeySet`, because a PKCS#12 reaching here would otherwise write its private
        /// key into the operator's key store, from bytes a `.kdbx` chose, on whichever thread
        /// happened to read the admission.
        /// </remarks>
        private static bool IsKeyedBareCertificate(string admissionKey, byte[] certificateDer)
        {
            if (!FixedTime.Equals(CertificateDigest.Of(certificateDer), admissionKey))
            {
                return false;
            }

            try
            {
                using (var parsed = new X509Certificate2(
                    certificateDer, (string)null, X509KeyStorageFlags.EphemeralKeySet))
                {
                    return FixedTime.Equals(parsed.RawData, certificateDer);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                // Total on purpose: bytes that are not a certificate are not a bare certificate,
                // and answering that with an exception would make the constructor throw one kind
                // of failure for a malformed key and another for malformed bytes.
                return false;
            }
        }

        /// <summary>
        /// The checks that make a record an admission, and the reason the constructor is where they
        /// live: an admission that admits nobody should be impossible to compose rather than
        /// merely impossible to read.
        /// </summary>
        private static void KeyedBy(string admissionKey, byte[] certificateDer)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                throw new ArgumentException("An admission is keyed on the certificate's digest.", nameof(admissionKey));
            }

            if (certificateDer == null || certificateDer.Length == 0)
            {
                throw new ArgumentException("An admission carries the certificate it admits.", nameof(certificateDer));
            }

            if (!IsKeyedBareCertificate(admissionKey, certificateDer))
            {
                throw new ArgumentException(
                    "An admission's key is the digest of the bare certificate it carries.",
                    nameof(admissionKey));
            }
        }

        /// <summary>
        /// Only names that could have been issued as claims survive, each once, in ordinal order,
        /// so an admission edited by hand into something strange grants nothing strange.
        /// </summary>
        private static IReadOnlyList<string> Clean(IEnumerable<string> profiles)
        {
            if (profiles == null)
            {
                return Nothing;
            }

            var kept = profiles.Where(AuthorityIssuer.IsUsableProfileName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(profile => profile, StringComparer.Ordinal)
                .ToList();

            // Wrapped rather than merely typed as read only. `IReadOnlyList<T>` is a view of the
            // same list, so a caster could raise a client's ceiling for the life of a connection,
            // and this is the one field in an admission that grants anything.
            return new ReadOnlyCollection<string>(kept);
        }
    }
}
