using System;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;

namespace KeePassGate.Ui
{
    /// <summary>
    /// What the gate asks of an admission record's certificate, asked the way the clients page asks
    /// it, so that every page and dialog calling something a client means the same thing.
    /// </summary>
    /// <remarks>
    /// **One judgment, wherever clients are counted or listed.** The clients page marks a row the
    /// gate refuses, "not this authority", "not for client use", "not yet valid" or "expired", while
    /// the Recreate and Delete confirmations, the Profiles page's counts and the authority chooser
    /// counted every readable record as a client: an authority with expired clients looked more
    /// used than one whose clients were live, and a profile was held by a program that could not
    /// connect. The order is the gate's: the anchor, then the usage, then the dates.
    /// </remarks>
    public static class ClientReaches
    {
        /// <summary>
        /// What is asked before the dates: whether <paramref name="authority"/> issued the
        /// certificate, and for a client to present.
        /// </summary>
        /// <remarks>
        /// A matter of the two certificates alone, which is why the clients page keeps the answer
        /// for as long as the authority is the same. A null authority issued nothing.
        /// </remarks>
        public static ClientReach Issued(X509Certificate2 certificate, X509Certificate2 authority)
        {
            if (!AnchorPin.Chains(certificate, authority))
            {
                return ClientReach.Stranger;
            }

            return ClientCredentials.IsForClientAuthentication(certificate)
                ? ClientReach.Reaches
                : ClientReach.NotForClients;
        }

        /// <summary>
        /// <paramref name="issued"/>, the answer <see cref="Issued"/> gave, and whether the
        /// certificate has begun after that. Its end is left to the date the row prints.
        /// </summary>
        public static ClientReach Dated(ClientReach issued, X509Certificate2 certificate, DateTimeOffset now)
        {
            if (issued != ClientReach.Reaches)
            {
                return issued;
            }

            return !CertificateValidity.IsCurrent(certificate, now) && !CertificateValidity.HasExpired(certificate, now)
                ? ClientReach.NotYetValid
                : ClientReach.Reaches;
        }

        /// <summary>
        /// Whether the client <paramref name="record"/> admits gets in at <paramref name="now"/>:
        /// everything the clients page asks of its row, its end included. What a count of clients
        /// counts.
        /// </summary>
        /// <remarks>
        /// A record whose certificate cannot be asked about gets nobody in, as the stranger the
        /// gate answers the same certificate with and the clients page shows it as. Revocation is
        /// not asked, because a revoked record is no record in the listing this is handed.
        /// </remarks>
        public static bool GetsIn(AdmissionRecord record, X509Certificate2 authority, DateTimeOffset now)
        {
            if (record == null || authority == null)
            {
                return false;
            }

            try
            {
                using (var certificate = record.Certificate())
                {
                    return GetsIn(certificate, authority, now);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                return false;
            }
        }

        /// <summary>The same, of a certificate the caller already holds, which may throw.</summary>
        public static bool GetsIn(X509Certificate2 certificate, X509Certificate2 authority, DateTimeOffset now)
        {
            return Issued(certificate, authority) == ClientReach.Reaches
                   && CertificateValidity.IsCurrent(certificate, now);
        }
    }
}
