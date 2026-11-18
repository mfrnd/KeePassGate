using System;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The custom data keys of the standing answers to a renewal, and what an authority is shown
    /// as.
    /// </summary>
    public static class AuthorityKeys
    {
        /// <summary>What an authority whose record names no digest at all is shown as.</summary>
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
        /// The operator's standing answer to renewing one certificate, keyed by that certificate's
        /// admission key, holding `forever` or the moment renewals are asked about again.
        /// </summary>
        /// <remarks>
        /// **Changed rather than only added**, because a later answer about the same certificate
        /// replaces an earlier one: the one the dialog takes once an until-date has passed, since a
        /// standing answer keeps the dialog shut until then and forever keeps it shut for good, or
        /// one a merge brings in. So a merge can decide it either way: by modification time, which
        /// a slow clock or a copy saved without the times turns around, or whatever the time under
        /// Overwrite existing. It never admits anybody, since a renewal is still a person approving
        /// one, but losing a newer until-date to an older forever answers every renewal
        /// `409 forever` until the operator lifts it, `CA.md` §204. **Allow renewal** on the
        /// clients page, accepted with the settings dialog, is what lifts it, and removes the key;
        /// so does the prune of a key no admission in force names, `RenewalAnswers.Prune`.
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
    }
}
