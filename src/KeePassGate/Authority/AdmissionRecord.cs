using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// One client, admitted. This is what a certificate is worth: without a record, a certificate
    /// that chains and carries the right usage opens nothing at all.
    /// </summary>
    /// <remarks>
    /// It carries the certificate rather than only its digest, which is what makes expiry knowable
    /// and what lets the tab say something useful about a client nobody remembers issuing.
    ///
    /// **The profiles here are the ones that count.** A certificate carries the same names as
    /// claims, but only as a convenience for a human reading it. `CA.md` §209 says why: a
    /// record is what a lost snapshot takes away, so reading a ceiling from the certificate would
    /// leave a credential nobody can see and nobody can revoke.
    ///
    /// **A record names the client as well as the certificate**, by its <see cref="ClientKey"/>:
    /// the admission key of the first certificate the client was issued, the one it paired with.
    /// A renewal writes a second record for the same client and leaves the first valid until the
    /// client gives it up, and a client chooses its own name, so neither the name nor the
    /// certificate tells one client from another; the client key does, and every count and naming
    /// of clients goes by it, which is the operator's ruling of 2026-10-16.
    /// </remarks>
    public sealed class AdmissionRecord
    {
        /// <summary>The format marker. A record that does not say this is refused, never guessed at.</summary>
        public const string Version = "1";

        /// <summary>Every field name a record may carry, and nothing else is one.</summary>
        private static readonly HashSet<string> Fields =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "v", "subject", "client", "admitted", "profiles", "certificate"
            };

        private static readonly IReadOnlyList<string> Nothing =
            new ReadOnlyCollection<string>(Array.Empty<string>());

        private readonly string _admissionKey;
        private readonly string _subject;
        private readonly string _clientKey;
        private readonly DateTimeOffset _admitted;
        private readonly IReadOnlyList<string> _profiles;
        private readonly byte[] _certificateDer;

        /// <summary>
        /// A record is a certificate, the digest it is filed under, and what was approved.
        /// </summary>
        /// <remarks>
        /// **The key has to be the digest of the certificate, and that is checked here.** Only
        /// <see cref="Load"/> checked it before, so a record whose two halves disagreed could be
        /// written, saved and never read back: pairing would report success and the client it
        /// admitted would be refused forever, with nothing but a warning in the log to say why.
        /// Refusing at construction makes that unwritable rather than unreadable.
        ///
        /// The client key is held to the shape of a digest for the same reason: one that is not
        /// would be written, and then read back as no record at all.
        /// </remarks>
        public AdmissionRecord(
            string admissionKey,
            string subject,
            string clientKey,
            DateTimeOffset admitted,
            IEnumerable<string> profiles,
            byte[] certificateDer)
        {
            KeyedBy(admissionKey, certificateDer);

            if (!CertificateDigest.IsDigest(clientKey))
            {
                throw new ArgumentException(
                    "A record names its client by the digest of the certificate it paired with.",
                    nameof(clientKey));
            }

            _admissionKey = admissionKey;
            _subject = Storable(subject);
            _clientKey = clientKey;
            _admitted = admitted;
            _profiles = Clean(profiles);
            _certificateDer = (byte[])certificateDer.Clone();
        }

        /// <summary>SHA-256 over the certificate's DER, lowercase hex.</summary>
        public string AdmissionKey
        {
            get { return _admissionKey; }
        }

        /// <summary>The subject as issued, which is what names this client to the operator.</summary>
        /// <remarks>
        /// Stored beside the certificate rather than read off it, and not compared with it when a
        /// record is read. Only whoever can write the file can make the two disagree, and whoever
        /// can do that can write a whole record for a certificate of their own, since the authority
        /// that signs one is kept in the same file, so a comparison would stop nobody.
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
        /// record that asked, so every certificate a client has held carries the same one. Two
        /// records with one client key are one client, however many certificates it holds; two
        /// with one name and different client keys are two, which a name chosen by the client
        /// cannot tell apart. Written once with the record and never changed, so a merge, which
        /// never reads a value, has nothing to decide about it.
        ///
        /// Not checked against anything when a record is read, for the reason the subject is not:
        /// whoever can write the file can write a whole record of their own. It names a client and
        /// grants nothing.
        /// </remarks>
        public string ClientKey
        {
            get { return _clientKey; }
        }

        public DateTimeOffset Admitted
        {
            get { return _admitted; }
        }

        /// <summary>What the operator approved. The only place a ceiling comes from.</summary>
        public IReadOnlyList<string> Profiles
        {
            get { return _profiles; }
        }

        public byte[] CertificateDer
        {
            get { return (byte[])_certificateDer.Clone(); }
        }

        /// <summary>The custom data key this record lives under.</summary>
        public string StorageKey
        {
            get { return AuthorityKeys.ClientRecordKey(_admissionKey); }
        }

        /// <summary>
        /// The certificate, for reading dates and names off. The caller disposes it.
        /// </summary>
        /// <remarks>
        /// `EphemeralKeySet`, for the reason `CertificateBlob` gives: no key this plugin loads ever
        /// reaches the disk. These bytes are a bare certificate and carry no key, which `Load` is
        /// what makes true, and this is what keeps it true for a record built through the public
        /// constructor instead.
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
        /// nothing, since the gate refuses it before any record is read, and the next write the
        /// plugin makes prunes its record.
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
        /// The record a pairing writes: a new client, whose client key is this certificate's own
        /// admission key.
        /// </summary>
        public static AdmissionRecord For(
            X509Certificate2 certificate, DateTimeOffset admitted, IEnumerable<string> profiles)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            return For(certificate, admitted, profiles, CertificateDigest.Of(certificate));
        }

        /// <summary>
        /// The record a renewal writes: the same client under a new certificate, its client key
        /// copied from the record that asked.
        /// </summary>
        public static AdmissionRecord For(
            X509Certificate2 certificate, DateTimeOffset admitted, IEnumerable<string> profiles, string clientKey)
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
                certificate.RawData);
        }

        /// <summary>
        /// The stored form: one field per line, name and value either side of the first equals
        /// sign.
        /// </summary>
        /// <remarks>
        /// Small and strict rather than general. A record is written by this plugin and read by
        /// this plugin, and everything in it has already been through issuance, which refuses a
        /// subject or a profile that is not a plain name. That leaves a reader that can be total in
        /// eighty lines, which is a smaller liability than a parser that has to be right about
        /// somebody else's grammar. A JSON dependency has since arrived for the access control
        /// document, so this could move to it, and deliberately has not: a record read before
        /// anything is trusted is worth a reader with no third-party grammar underneath it.
        /// </remarks>
        public string Save()
        {
            var text = new StringBuilder();
            text.Append("v=" + Version).Append('\n');
            text.Append("subject=").Append(_subject).Append('\n');
            text.Append("client=").Append(_clientKey).Append('\n');
            text.Append("admitted=").Append(Moments.Wire(_admitted)).Append('\n');
            text.Append("profiles=").Append(string.Join(",", _profiles)).Append('\n');
            text.Append("certificate=").Append(Convert.ToBase64String(_certificateDer)).Append('\n');
            return text.ToString();
        }

        /// <summary>
        /// Reads one back. Null for anything that is not exactly a record: a missing field, an
        /// unknown version, a repeated field, a subject with a line ending in it, a client key that
        /// is not a digest, a date that will not parse, a certificate that will not decode, and one
        /// that decodes to something other than the bytes it was stored as. A record that cannot be read admits nobody, which is
        /// the whole point of keeping this strict.
        /// </summary>
        public static AdmissionRecord Load(string admissionKey, string stored)
        {
            if (string.IsNullOrEmpty(admissionKey) || string.IsNullOrEmpty(stored))
            {
                return null;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var line in stored.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var equals = trimmed.IndexOf('=');
                if (equals <= 0)
                {
                    return null;
                }

                var name = trimmed.Substring(0, equals);
                if (fields.ContainsKey(name))
                {
                    return null;
                }

                fields[name] = trimmed.Substring(equals + 1);
            }

            // A name this reader does not know is refused rather than skipped. The version marker
            // exists so that a later writer can add a field which narrows what a record grants, and
            // a reader that walked past one would go on admitting on the strength of the fields it
            // did recognize. Refusing here is also what makes `Save(Load(x))` give back `x`.
            foreach (var name in fields.Keys)
            {
                if (!Fields.Contains(name))
                {
                    return null;
                }
            }

            string version;
            if (!fields.TryGetValue("v", out version) || !string.Equals(version, Version, StringComparison.Ordinal))
            {
                return null;
            }

            string subject;
            string client;
            string admitted;
            string profiles;
            string certificate;
            if (!fields.TryGetValue("subject", out subject) ||
                !fields.TryGetValue("client", out client) ||
                !fields.TryGetValue("admitted", out admitted) ||
                !fields.TryGetValue("profiles", out profiles) ||
                !fields.TryGetValue("certificate", out certificate))
            {
                return null;
            }

            // The stored form is one field per line, so a subject carrying a line ending is one
            // this could never have written. Refused rather than read, for the reason the
            // constructor gives.
            if (!IsStorableSubject(subject))
            {
                return null;
            }

            // A record that does not say which client it is cannot be counted as one, and is
            // refused like any other that is not exactly a record.
            if (!CertificateDigest.IsDigest(client))
            {
                return null;
            }

            // `Moments.WireFormat` is RFC 3339 with a `Z` offset and no fractional seconds:
            // https://www.rfc-editor.org/rfc/rfc3339#section-5.6. One spelling of it for the whole
            // plugin, because a record written on one machine is read on another, and because the
            // separators have to stay quoted: a bare `:` in a custom format string means whatever
            // the current culture uses for one.
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

            byte[] der;
            try
            {
                der = Convert.FromBase64String(certificate);
            }
            catch (FormatException)
            {
                return null;
            }

            if (der.Length == 0)
            {
                return null;
            }

            try
            {
                if (!IsKeyedBareCertificate(admissionKey, der))
                {
                    return null;
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                // A record whose certificate will not decode admits nobody, which is the same
                // answer as no record at all.
                return null;
            }

            // Checked once more by the constructor, which is the price of there being one place
            // that decides what a record is. It is asked here as well so that a stored value which
            // is not a record is refused rather than thrown about.
            return new AdmissionRecord(admissionKey, subject, client, when, Split(profiles), der);
        }

        /// <summary>
        /// A subject the stored form can hold, which is any that carries no line ending.
        /// </summary>
        /// <remarks>
        /// The format leans on issuance refusing a subject that is not a plain name, and
        /// `AuthorityIssuer.IsUsableSubjectName` does refuse one. The invariant the format depends
        /// on lived in that other class, though, so a record built from any other certificate could
        /// write a `subject=` line with a newline in it and read back as a different record or as
        /// none at all. This is that invariant, at the boundary that needs it.
        /// </remarks>
        public static bool IsStorableSubject(string subject)
        {
            return subject != null && subject.IndexOf('\n') < 0 && subject.IndexOf('\r') < 0;
        }

        /// <summary>
        /// Whether these bytes are a bare certificate whose digest is this key.
        /// </summary>
        /// <remarks>
        /// **A bare certificate and nothing else.** Measured on .NET Framework 4.8:
        /// `X509Certificate2(byte[])` sniffs the blob, so a password-less PKCS#12 and a PEM both
        /// load through it and come back re-encoded. The certificate a record carries need
        /// therefore not be the bytes it is keyed on, and a digest check alone cannot see that,
        /// because the key is a digest of whatever was stored.
        ///
        /// `EphemeralKeySet` for the reason `CertificateBlob` uses it: a PKCS#12 reaching here
        /// would otherwise write its private key into the operator's key store, from bytes a
        /// `.kdbx` chose, on whichever thread happened to read the record.
        ///
        /// Here rather than in `Load` alone, because the constructor is what decides what a record
        /// is and it was deciding less: a record built from a PKCS#12 and its digest passed every
        /// check the constructor made, stored, and then read back as nothing for ever after.
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
        /// The three checks that make a record one, and the reason the constructor is where they
        /// live: a record that admits nobody should be impossible to write rather than merely
        /// impossible to read.
        /// </summary>
        private static void KeyedBy(string admissionKey, byte[] certificateDer)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                throw new ArgumentException("A record is keyed on the certificate's digest.", nameof(admissionKey));
            }

            if (certificateDer == null || certificateDer.Length == 0)
            {
                throw new ArgumentException("A record carries the certificate it admits.", nameof(certificateDer));
            }

            if (!IsKeyedBareCertificate(admissionKey, certificateDer))
            {
                throw new ArgumentException(
                    "A record's key is the digest of the bare certificate it carries.",
                    nameof(admissionKey));
            }
        }

        private static string Storable(string subject)
        {
            if (subject == null)
            {
                return string.Empty;
            }

            if (!IsStorableSubject(subject))
            {
                throw new ArgumentException(
                    "A subject cannot carry a line ending: the stored form is one field per line.",
                    nameof(subject));
            }

            return subject;
        }

        private static IEnumerable<string> Split(string profiles)
        {
            if (string.IsNullOrEmpty(profiles))
            {
                return Nothing;
            }

            return profiles.Split(',');
        }

        /// <summary>
        /// Only names that could have been issued as claims survive, so a record edited by hand
        /// into something strange grants nothing strange.
        /// </summary>
        private static IReadOnlyList<string> Clean(IEnumerable<string> profiles)
        {
            if (profiles == null)
            {
                return Nothing;
            }

            var kept = new List<string>();
            foreach (var profile in profiles)
            {
                if (!AuthorityIssuer.IsUsableProfileName(profile) || kept.Contains(profile))
                {
                    continue;
                }

                kept.Add(profile);
            }

            // Wrapped rather than merely typed as read only. `IReadOnlyList<T>` is a view of the
            // same list, so a caster could raise a client's ceiling for the life of a connection,
            // and this is the one field in a record that grants anything.
            return new ReadOnlyCollection<string>(kept);
        }
    }
}
