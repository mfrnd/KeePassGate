using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Turns whatever certificate a client presented into an identity, or into a stranger.
    /// </summary>
    /// <remarks>
    /// Two checks, both explicit, both from `CA.md` §213.
    ///
    /// **The anchor is pinned** against the resolved database's authority, because the natural
    /// chain validation accepts a certificate from a completely foreign authority: measured, and it
    /// would have shipped.
    ///
    /// **The extended key usage is checked**, because the same authority signs hosts and clients,
    /// and hostname verification is deliberately abandoned here. Without this, a host certificate
    /// could be presented as a client.
    ///
    /// What is not here yet is the admission record. TODO(step 7): that is step 7. Until it lands, a client that
    /// chains and carries the right usage is paired and carries no profiles, so no route can grant
    /// anything on the strength of it.
    /// </remarks>
    public static class ClientCredentials
    {
        public static ClientIdentity For(X509Certificate2 presented, IGateTarget target, IGateLog log)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var logger = log ?? NullGateLog.Instance;
            var databaseId = target.DatabaseId;

            if (presented == null)
            {
                return ClientIdentity.Stranger(databaseId);
            }

            if (!AnchorPin.Chains(presented, target.Authority, false))
            {
                logger.Warn("refused a certificate that does not chain to " + Short(databaseId));
                return ClientIdentity.Stranger(databaseId);
            }

            if (!IsForClientAuthentication(presented))
            {
                logger.Warn("refused a certificate that is not for client authentication");
                return ClientIdentity.Stranger(databaseId);
            }

            return ClientIdentity.Paired(databaseId, presented, null);
        }

        /// <summary>
        /// True only when the certificate says `clientAuth` and says it explicitly. A certificate
        /// with no extended key usage at all is unconstrained, which is refused rather than read
        /// as permission.
        /// </summary>
        public static bool IsForClientAuthentication(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return false;
            }

            var extension = certificate.Extensions["2.5.29.37"] as X509EnhancedKeyUsageExtension;
            if (extension == null)
            {
                return false;
            }

            return extension.EnhancedKeyUsages
                .Cast<System.Security.Cryptography.Oid>()
                .Any(oid => string.Equals(oid.Value, AuthorityIssuer.ClientAuthOid, StringComparison.Ordinal));
        }

        private static string Short(string databaseId)
        {
            return CertificateDigest.Shorten(databaseId ?? string.Empty);
        }
    }
}
