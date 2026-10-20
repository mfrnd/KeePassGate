using System;
using System.Collections.Generic;
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
    /// Four checks, in the order `CA.md` §213 gives them, and every failure is the same
    /// answer: a stranger, which the route table refuses everywhere but the pairing path. One
    /// that fails after the anchor is pinned is a stranger this authority issued, and carries its
    /// admission key and the reason, so that the refusal can be written down against it.
    ///
    /// **The anchor is pinned** against the resolved database's authority, because the natural
    /// chain validation accepts a certificate from a completely foreign authority: measured, and it
    /// would have shipped.
    ///
    /// **The extended key usage is checked**, because the same authority signs hosts and clients
    /// and hostname verification is deliberately abandoned here. Without it a host certificate
    /// could be presented as a client.
    ///
    /// **The dates are checked here** rather than left to the chain build, so that the allowance
    /// for a wrong clock is exactly five minutes and only on `notBefore`.
    ///
    /// **The admission record is looked up, and it is what grants.** A certificate that chains,
    /// carries the right usage and is inside its dates opens nothing without one. That is what
    /// makes a record lost to a stale dialog snapshot into a client that must pair again, rather
    /// than a credential nobody can see and nobody can revoke, and it is what makes revocation take
    /// effect on the next connection. **A record admits only with no revocation beside it**, which
    /// `AdmissionStore.Find` asks in the same pass, so a record a stale copy brought back after
    /// its client was revoked is refused exactly as a missing one is.
    ///
    /// The profiles come from the record and never from the certificate's claims, and only those
    /// the database's list still names: see <see cref="Effective"/>.
    /// </remarks>
    public static class ClientCredentials
    {
        public static ClientIdentity For(X509Certificate2 presented, IGateTarget target, IGateLog log)
        {
            return For(presented, target, DateTimeOffset.UtcNow, log);
        }

        public static ClientIdentity For(
            X509Certificate2 presented, IGateTarget target, DateTimeOffset now, IGateLog log)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var logger = log ?? NullGateLog.Instance;
            var databaseId = target.DatabaseId;

            if (target.IsLocked)
            {
                // Nothing about this caller can be established: the authority that would validate
                // its certificate went with the database. Saying so is not the same as calling it a
                // stranger, because a stranger can pair and this one cannot do anything at all.
                return ClientIdentity.Locked(databaseId);
            }

            if (target.Unavailable != null)
            {
                // The same for a database holding several authorities: none is loaded to judge a
                // certificate with, because loading one would be choosing between them.
                return ClientIdentity.Unavailable(databaseId, target.Unavailable);
            }

            if (presented == null)
            {
                return ClientIdentity.Stranger(databaseId);
            }

            // Every check below reads the certificate, and every read of one is a call into
            // CryptoAPI that can fail on a context the client chose. One filter over all of them,
            // so that the promise above holds for the last three the way `AnchorPin` already makes
            // it hold for the first: a refusal, not an exception the transport has to catch.
            try
            {
                // Expiry is ignored by the chain build and checked below, so that the skew is ours.
                if (!AnchorPin.Chains(presented, target.Authority, true))
                {
                    logger.Warn("refused a certificate that does not chain to " + Short(databaseId));
                    return ClientIdentity.Stranger(databaseId);
                }

                if (!IsForClientAuthentication(presented))
                {
                    logger.Warn("refused a certificate that is not for client authentication");
                    return ClientIdentity.Refused(
                        databaseId, presented, "this certificate is not for client authentication");
                }

                // A leaf's notAfter is clamped to the authority's at issuance, so a certificate
                // inside its own dates is inside the authority's as well.
                if (!CertificateValidity.IsCurrent(presented, now))
                {
                    logger.Warn("refused a certificate that is outside its dates");
                    return ClientIdentity.Refused(databaseId, presented, "this certificate is outside its dates");
                }

                var admissionKey = CertificateDigest.Of(presented);
                var record = target.Admission(admissionKey);

                if (record == null)
                {
                    logger.Warn("refused a certificate with no admission record: " +
                                CertificateDigest.Shorten(admissionKey));
                    return ClientIdentity.Refused(databaseId, presented, "this certificate has no admission record");
                }

                // Read once, so the names granted and the names withheld are two halves of one list.
                var named = target.KnownProfiles;
                return ClientIdentity.Paired(
                    databaseId, presented, Effective(record.Profiles, named), Removed(record.Profiles, named));
            }
            catch (DatabaseShut error)
            {
                // Closed under this connection, which is KeePass locking it: told as every
                // connection to a locked database is, rather than as a certificate with no record.
                logger.Info("a certificate could not be checked, because " + error.Message);
                return ClientIdentity.Locked(databaseId);
            }
            catch (VaultUnsettled error)
            {
                // The records could not be listed just now, which says nothing about this client.
                // Busy rather than a stranger, so it is told to ask again rather than to pair again.
                logger.Warn("a certificate could not be checked against this database for now: " + error.Message);
                return ClientIdentity.Unavailable(databaseId, "busy");
            }
            catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfCryptography(error))
            {
                logger.Error("a certificate could not be checked against this database", error);
                return ClientIdentity.Stranger(databaseId);
            }
        }

        /// <summary>
        /// The profiles a record holds that the database still names, in the record's order.
        /// </summary>
        /// <remarks>
        /// **Removing a profile is taking it off the list, and this is what makes that enough.** A
        /// record keeps every name it was approved for, and a rule for a removed name can stay on a
        /// node or come back in a merge. Neither grants anything, because a name off the list never
        /// reaches the resolver: the router and the rules see this and nothing else. Adding the
        /// name back is then a full undo for every rule and every record still naming it; a record
        /// a renewal wrote while the name was off the list was written without it, `PAIRING.md`
        /// §312.
        ///
        /// Read beside the record, on every connection and never cached, for the reason the record
        /// is. A list that cannot be read is an empty one, which leaves the client admitted and
        /// reaching nothing: the direction everything here fails in.
        /// </remarks>
        public static IReadOnlyList<string> Effective(IReadOnlyList<string> held, IReadOnlyList<string> named)
        {
            var kept = new List<string>();

            if (held == null || named == null)
            {
                return kept;
            }

            foreach (var profile in held)
            {
                if (profile != null && named.Contains(profile, StringComparer.Ordinal))
                {
                    kept.Add(profile);
                }
            }

            return kept;
        }

        /// <summary>
        /// The profiles a record holds that the database no longer names, in the record's order:
        /// what <see cref="Effective"/> leaves out.
        /// </summary>
        /// <remarks>
        /// Granting nothing, and kept for the audit alone: a refusal these names would have
        /// permitted is written down as a profile removed rather than as a rule refusing. A list
        /// that cannot be read is an empty one here too, so every name the record holds counts as
        /// removed, which is how the clients page shows it as well.
        /// </remarks>
        public static IReadOnlyList<string> Removed(IReadOnlyList<string> held, IReadOnlyList<string> named)
        {
            var removed = new List<string>();

            if (held == null)
            {
                return removed;
            }

            foreach (var profile in held)
            {
                if (profile != null && (named == null || !named.Contains(profile, StringComparer.Ordinal)))
                {
                    removed.Add(profile);
                }
            }

            return removed;
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
