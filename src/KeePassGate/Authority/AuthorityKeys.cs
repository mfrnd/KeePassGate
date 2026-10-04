using System;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The custom data keys this plugin owns, all under one prefix so that what belongs to
    /// KeePassGate can be listed, and deleted, without guessing.
    /// </summary>
    public static class AuthorityKeys
    {
        /// <summary>
        /// Everything the authority writes starts with this.
        /// </summary>
        /// <remarks>
        /// Not everything the plugin writes. The access control document is `KPG`, with no dot, so
        /// a prefix test on this finds the authority's own keys, the admission records and the
        /// profile list, and misses the grants entirely. Deleting names its keys rather than
        /// sweeping the prefix, for that reason. The revocations and the retirements are under it
        /// too.
        /// </remarks>
        public const string Prefix = "KPG.";

        /// <summary>
        /// One authority per key, named by the digest of its certificate: its certificate and
        /// private key, the host certificate and key, and the gate key, all three inside one value.
        /// </summary>
        /// <remarks>
        /// One key rather than three, so that a kdbx merge cannot take the parts from different
        /// replicas and leave a host certificate that does not chain to the authority beside it.
        /// `AuthorityDocument` is the shape inside it.
        ///
        /// **One key per authority rather than one key for the only authority**, because a key
        /// that is only ever added and never changed is the one shape a KeePass merge cannot undo.
        /// `PwDatabase.MergeInDbProperties` visits the source's keys and no others and never reads
        /// a value, so a replacement written over the old authority's key lost to any copy whose
        /// time for that key was later, and a stale copy merged in could put back an authority the
        /// operator had replaced. Written under its own digest, a document is written once and
        /// never rewritten; what replaces it is a retirement beside it, and two authorities made
        /// separately in two copies arrive side by side for the operator to choose between rather
        /// than one silently beating the other.
        /// </remarks>
        public const string AuthorityPrefix = "KPG.Authority.";

        /// <summary>
        /// One retirement per authority that was replaced, deleted or not chosen, named by the same
        /// digest as its document.
        /// </summary>
        /// <remarks>
        /// **Only ever added, never changed and never removed**, for the reason `RevokedPrefix`
        /// gives: a merge cannot take away a key the target holds, so an authority once retired
        /// stays retired whatever copy of the file is merged in later, and its document coming
        /// back with that copy serves nothing. The value is a constant, so there is nothing for a
        /// merge to decide between.
        /// </remarks>
        public const string RetiredPrefix = "KPG.RetiredAuthority.";

        /// <summary>What a retirement holds. Constant, so that no two copies of one ever differ.</summary>
        public const string RetiredValue = "retired";

        /// <summary>The key the authority with this digest is stored under.</summary>
        public static string AuthorityKey(string fingerprint)
        {
            return AuthorityPrefix + fingerprint;
        }

        /// <summary>The key that retires the authority with this digest.</summary>
        public static string RetirementKey(string fingerprint)
        {
            return RetiredPrefix + fingerprint;
        }

        /// <summary>The digest an authority's key is filed under, or null when the key is not one.</summary>
        public static string FingerprintInAuthority(string key)
        {
            return key != null && key.StartsWith(AuthorityPrefix, StringComparison.Ordinal)
                ? key.Substring(AuthorityPrefix.Length)
                : null;
        }

        /// <summary>The digest a retirement is filed under, or null when the key is not one.</summary>
        public static string FingerprintInRetirement(string key)
        {
            return key != null && key.StartsWith(RetiredPrefix, StringComparison.Ordinal)
                ? key.Substring(RetiredPrefix.Length)
                : null;
        }

        /// <summary>
        /// Whether a key is an authority or a retirement, which between them are everything that
        /// decides which authority a database serves.
        /// </summary>
        public static bool IsAuthorityState(string key)
        {
            return FingerprintInAuthority(key) != null || FingerprintInRetirement(key) != null;
        }

        /// <summary>One admission record per paired client, keyed by its certificate digest.</summary>
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
        /// One revocation per revoked client, keyed by the same digest as its record, whose value
        /// is the certificate's `notAfter`.
        /// </summary>
        /// <remarks>
        /// **A key of its own, only ever added and never changed**, because that is the one shape a
        /// KeePass merge cannot undo. `PwDatabase.MergeInDbProperties` visits the source's keys and
        /// no others, so it never removes a key the target holds, and it decides a key both sides
        /// hold by modification time and never by value, so a value nobody changes has nothing to
        /// lose to. The emptied record beside it is still written, and is still the losing kind of
        /// mark: a stale copy can bring the record back, and this is what refuses it when it does.
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
        /// older forever costs the client a new pairing rather than a dialog, `CA.md` §204.
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
