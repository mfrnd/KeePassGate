using System;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The custom data keys this plugin owns for its admissions, all under one prefix so that what
    /// belongs to KeePassGate can be listed, and deleted, without guessing.
    /// </summary>
    public static class AuthorityKeys
    {
        /// <summary>
        /// Every key the admissions are written under starts with this.
        /// </summary>
        /// <remarks>
        /// Not everything the plugin writes. The access control document is `KPG`, with no dot, so
        /// a prefix test on this finds the admission records, the revocations, the standing
        /// answers and the profile list, and misses the grants entirely. Deleting names its keys
        /// rather than sweeping the prefix, for that reason.
        /// </remarks>
        public const string Prefix = "KPG.";

        /// <summary>What a key naming an authority by no digest at all is shown as.</summary>
        public const string NoDigest = "(no digest)";

        /// <summary>
        /// The digest an authority is filed under as the operator and the log see it: shortened,
        /// or <see cref="NoDigest"/> for a key that names none, which a list would show as nothing.
        /// </summary>
        public static string Shown(string fingerprint)
        {
            return string.IsNullOrEmpty(fingerprint) ? NoDigest : CertificateDigest.Shorten(fingerprint);
        }

        /// <summary>
        /// One admission record per certificate admitted, keyed by its digest, so a renewed client
        /// holds one for each certificate it still has.
        /// </summary>
        public const string ClientPrefix = "KPG.Client.";

        public static bool IsClientRecord(string key)
        {
            return key != null && key.StartsWith(ClientPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The key one admission record lives under.
        /// </summary>
        /// <remarks>
        /// Here rather than at each site, because the inverse is here too. A record is written
        /// under one of these and revoked by name, and the one operation that must never fail
        /// quietly is the deletion: a prefix composed by hand in two places is a prefix that can
        /// disagree with itself and leave a live credential admitted.
        /// </remarks>
        public static string ClientRecordKey(string admissionKey)
        {
            return ClientPrefix + admissionKey;
        }

        /// <summary>The digest a record key is filed under, or null when the key is not one.</summary>
        public static string AdmissionKeyIn(string key)
        {
            return IsClientRecord(key) ? key.Substring(ClientPrefix.Length) : null;
        }

        /// <summary>
        /// One revocation per revoked certificate, keyed by the same digest as its record, whose
        /// value is the certificate's `notAfter` and the digest of the authority that issued it.
        /// </summary>
        /// <remarks>
        /// **A key of its own, only ever added and never changed**, because that is the one shape a
        /// KeePass merge cannot undo: it never removes a key the target holds and never reads a
        /// value, `CA.md` §204. The emptied record beside it is still written, and is still the
        /// losing kind of mark: a stale copy can bring the record back, and this is what refuses it
        /// when it does.
        ///
        /// **The value is chosen once, as the key is written**, and is what lets the key go: past
        /// `notAfter` the gate refuses the certificate on its dates, and once the authority named
        /// is retired or gone, the retirement refuses everything that authority issued. So both are
        /// written together, the end first and the authority's digest after a space. A revocation
        /// written before revocations named their authority holds the date alone, and so does one
        /// for a certificate no authority this database holds issued; one for a record that would
        /// not read holds nothing at all.
        /// </remarks>
        public const string RevokedPrefix = "KPG.Revoked.";

        public static bool IsRevocation(string key)
        {
            return key != null && key.StartsWith(RevokedPrefix, StringComparison.Ordinal);
        }

        /// <summary>The key a client's revocation lives under.</summary>
        public static string RevocationKey(string admissionKey)
        {
            return RevokedPrefix + admissionKey;
        }

        /// <summary>The digest a revocation key is filed under, or null when the key is not one.</summary>
        public static string AdmissionKeyInRevocation(string key)
        {
            return IsRevocation(key) ? key.Substring(RevokedPrefix.Length) : null;
        }

        /// <summary>
        /// What a revocation is written with: the certificate's end as the wire writes a moment,
        /// and the digest of the authority that issued it, or empty when there is no end to give.
        /// </summary>
        public static string RevocationValue(string notAfter, string authority)
        {
            if (string.IsNullOrEmpty(notAfter))
            {
                return string.Empty;
            }

            return string.IsNullOrEmpty(authority) ? notAfter : notAfter + " " + authority;
        }

        /// <summary>The end a revocation holds, as written, or null when it holds none.</summary>
        public static string NotAfterInRevocation(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var space = value.IndexOf(' ');
            return space < 0 ? value : value.Substring(0, space);
        }

        /// <summary>
        /// The authority a revocation names, or null when it names none or names something that is
        /// not a digest, which is then kept as though it named none.
        /// </summary>
        public static string AuthorityInRevocation(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var space = value.IndexOf(' ');
            if (space < 0)
            {
                return null;
            }

            var named = value.Substring(space + 1);
            return CertificateDigest.IsDigest(named) ? named : null;
        }

        /// <summary>
        /// The operator's standing answer to renewing one certificate, keyed by the same digest as
        /// its record, holding `forever` or the moment renewals are asked about again.
        /// </summary>
        /// <remarks>
        /// **Changed rather than only added**, unlike a revocation, because a later answer about
        /// the same certificate replaces an earlier one: the one the dialog takes once an
        /// until-date has passed, since a standing answer keeps the dialog shut until then and
        /// forever keeps it shut for good, or one a merge brings in. So a merge can decide it
        /// either way: by modification time, which a slow clock or a copy saved without the times
        /// turns around, or whatever the time under Overwrite existing. It never admits anybody,
        /// since a renewal is still a person approving one, but losing a newer until-date to an
        /// older forever answers every renewal `409 forever` until the operator lifts it, `CA.md`
        /// §204. **Allow renewal** on the clients page, accepted with the settings dialog, is what
        /// lifts it, and removes the key; so does the prune of a key no record is left to ask.
        ///
        /// **A refusal in this session has no key**: it is held in plugin memory, so that it goes
        /// with the session it names.
        /// </remarks>
        public const string RenewalRefusedPrefix = "KPG.RenewalRefused.";

        public static bool IsRenewalRefusal(string key)
        {
            return key != null && key.StartsWith(RenewalRefusedPrefix, StringComparison.Ordinal);
        }

        /// <summary>The key a standing answer to renewing this certificate lives under.</summary>
        public static string RenewalRefusalKey(string admissionKey)
        {
            return RenewalRefusedPrefix + admissionKey;
        }

        /// <summary>The digest a standing answer is filed under, or null when the key is not one.</summary>
        public static string AdmissionKeyInRenewalRefusal(string key)
        {
            return IsRenewalRefusal(key) ? key.Substring(RenewalRefusedPrefix.Length) : null;
        }

        public static bool IsOurs(string key)
        {
            return key != null && key.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
