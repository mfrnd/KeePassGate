using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;

namespace KeePassGate.Gate
{
    /// <summary>
    /// One database as the transport sees it: an identifier, a gate key to recognize names by, and
    /// the certificates to serve and validate with.
    /// </summary>
    /// <remarks>
    /// Deliberately not a KeePass database. The transport has no business reaching entries, and the
    /// seam is what lets every one of these tests run without a password manager.
    ///
    /// **Disposable, because a target is held rather than owned.** The cache that builds them and
    /// the connection that serves from one are two holders of the same thing, and the material
    /// goes when the last of them lets go. A caller that took one from `Current` gives it back.
    /// </remarks>
    public interface IGateTarget : IDisposable
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

        /// <summary>
        /// The record admitting this certificate, or null when there is none.
        /// </summary>
        /// <remarks>
        /// Read from the database on every call rather than cached with the certificates, because
        /// a revocation beside the record has to take effect on the next connection
        /// rather than on the next time something else changed.
        /// </remarks>
        AdmissionRecord Admission(string admissionKey);
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
