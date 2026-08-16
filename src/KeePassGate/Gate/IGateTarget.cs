using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace KeePassGate.Gate
{
    /// <summary>
    /// One database as the transport sees it: an identifier, a gate key to recognize names by, and
    /// the certificates to serve and validate with.
    /// </summary>
    /// <remarks>
    /// Deliberately not a KeePass database. The transport has no business reaching entries, and the
    /// seam is what lets every one of these tests run without a password manager.
    /// </remarks>
    public interface IGateTarget
    {
        /// <summary>
        /// The authority's digest, lowercase hex. Stable for the life of the authority, derived
        /// from public bytes, and meaningless to anyone who has not already been let in.
        /// </summary>
        string DatabaseId { get; }

        /// <summary>A copy of the gate key, which is what connection names are built from.</summary>
        byte[] GateKey { get; }

        /// <summary>
        /// The certificate to present and the key to prove it with, in memory and nowhere else.
        /// </summary>
        HostKeyMaterial Host { get; }

        /// <summary>The authority every client certificate has to chain to.</summary>
        X509Certificate2 Authority { get; }
    }

    /// <summary>
    /// The databases that can be served right now. Read afresh per connection, because a database
    /// can be opened, locked or closed between any two of them.
    /// </summary>
    public interface IGateTargets
    {
        IReadOnlyList<IGateTarget> Current();
    }
}
