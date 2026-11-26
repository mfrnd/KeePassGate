namespace KeePassGate.Authority
{
    /// <summary>What an authority is shown as, to the operator and in the log.</summary>
    public static class AuthorityShown
    {
        /// <summary>What an authority whose record names no digest at all is shown as.</summary>
        public const string NoDigest = "(no digest)";

        /// <summary>
        /// The digest an authority is filed under as the operator and the log see it: shortened,
        /// or <see cref="NoDigest"/> for a key that names none, which a list would show as nothing.
        /// </summary>
        public static string Of(string fingerprint)
        {
            return string.IsNullOrEmpty(fingerprint) ? NoDigest : CertificateDigest.Shorten(fingerprint);
        }
    }
}
