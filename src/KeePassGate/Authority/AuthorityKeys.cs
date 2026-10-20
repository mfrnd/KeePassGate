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
        /// sweeping the prefix, for that reason.
        /// </remarks>
        public const string Prefix = "KPG.";

        /// <summary>
        /// The authority: its certificate and private key, the host certificate and key, and the
        /// gate key, all three inside one value.
        /// </summary>
        /// <remarks>
        /// One key rather than three, so that a kdbx merge cannot take the parts from different
        /// replicas and leave a host certificate that does not chain to the authority beside it.
        /// `AuthorityDocument` is the shape inside it.
        /// </remarks>
        public const string Authority = "KPG.Authority";

        /// <summary>One admission record per paired client, keyed by its certificate digest.</summary>
        public const string ClientPrefix = "KPG.Client.";

        public static bool IsClientRecord(string key)
        {
            return key != null && key.StartsWith(ClientPrefix, StringComparison.Ordinal);
        }

        public static bool IsOurs(string key)
        {
            return key != null && key.StartsWith(Prefix, StringComparison.Ordinal);
        }
    }
}
