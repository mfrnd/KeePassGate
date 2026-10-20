using System;
using System.Security.Cryptography.X509Certificates;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Whether a certificate is inside its own dates, checked here rather than left to a chain
    /// build so that the allowance for a wrong clock is exactly the one `CA.md` §213 specifies.
    /// </summary>
    /// <remarks>
    /// One place, because the gate and the tab have to agree about it. They did not: the tab called
    /// a client expired at the same instant the gate was still serving it, which is a disagreement
    /// an operator can see and cannot explain.
    /// </remarks>
    public static class CertificateValidity
    {
        public static bool IsCurrent(X509Certificate2 certificate, DateTimeOffset now)
        {
            if (certificate == null)
            {
                return false;
            }

            var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);

            // **`notBefore` is honored exactly, and the allowance lives in the certificate.**
            // `AuthorityIssuer.Backdate` already sets `notBefore` five minutes before issuance, so
            // a second five minutes here made the tolerance ten where `CA.md` §213, `DESIGN.md`
            // §132 and `ClientCredentials` all say exactly five. It applied to every certificate
            // that can reach this, because the anchor pin means only this authority's own do.
            //
            // The backdate is the half worth keeping: it is written into the certificate, so the
            // reference client validating a host certificate through OpenSSL gets the same five
            // minutes this does, where slack on this side would have been ours alone.
            // TODO(step 10): the reference client arrives with step 10.
            if (now < notBefore)
            {
                return false;
            }

            return !HasExpired(certificate, now);
        }

        /// <summary>
        /// Whether the end of a certificate's life has passed, on the boundary
        /// <see cref="IsCurrent"/> uses.
        /// </summary>
        /// <remarks>
        /// Its own method so the tab cannot answer this differently from the gate. The last moment
        /// counts as current, here and there.
        /// </remarks>
        public static bool HasExpired(X509Certificate2 certificate, DateTimeOffset now)
        {
            if (certificate == null)
            {
                return true;
            }

            return HasExpired(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero), now);
        }

        /// <summary>
        /// The same question about a moment already read off a certificate.
        /// </summary>
        /// <remarks>
        /// Here so that the tab, which holds the date rather than the certificate it came from, asks
        /// the same question the gate does instead of writing the comparison again. The two had
        /// drifted apart by one boundary: `expires &lt;= now` calls a certificate expired at the
        /// instant `now &gt; notAfter` still serves it.
        /// </remarks>
        public static bool HasExpired(DateTimeOffset notAfter, DateTimeOffset now)
        {
            return now > notAfter;
        }

        /// <summary>How long is left, for a tab that has to warn before something stops working.</summary>
        public static TimeSpan RemainingOn(X509Certificate2 certificate, DateTimeOffset now)
        {
            if (certificate == null)
            {
                return TimeSpan.Zero;
            }

            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            var left = notAfter - now;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }
}
