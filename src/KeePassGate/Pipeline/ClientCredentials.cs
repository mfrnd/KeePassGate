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
    /// **The dates are checked here**, since the anchor pin asks nothing of them, so that the
    /// allowance for a wrong clock is exactly five minutes and only on `notBefore`.
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

            // Every check below reads the certificate, and every read of one through Windows is a
            // call into CryptoAPI that can fail on a context the client chose. One filter over all
            // of them, so that the promise above holds for the last three the way `AnchorPin`
            // already makes it hold for the first: a refusal, not an exception the transport has
            // to catch.
            try
            {
                // The pin asks nothing of the dates, and they are checked below, so that the skew
                // is ours.
                if (!AnchorPin.Chains(presented, target.Authority))
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

                // The names before the record, and read once, so the names granted and the names
                // withheld are two halves of one list. Each read holds KeePass's thread on its own,
                // and a lock can land between them: read second, the list met a database already
                // shut and named nothing, and a client was told every profile it holds had been
                // taken away. Read first, a lock after it is met by the record's read, which
                // answers a shut database as one.
                var named = target.KnownProfiles;

                // Whether it read whole, asked beside it and first for the same reason, so that
                // names a damaged list lost are told apart from names the operator removed. A
                // database shut by then answers that its list reads, so nothing is called damaged
                // that was not.
                var unreadable = target.ProfilesUnreadable != null;
                var record = target.Admission(admissionKey);

                if (record == null)
                {
                    logger.Warn("refused a certificate with no admission record: " +
                                CertificateDigest.Shorten(admissionKey));
                    return ClientIdentity.Refused(databaseId, presented, "this certificate has no admission record");
                }

                return ClientIdentity.Paired(
                    databaseId,
                    presented,
                    Effective(record.Profiles, named),
                    Removed(record.Profiles, named),
                    record.ClientKey,
                    unreadable);
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
        /// The client a request arrived as, as the database holds it at the moment the request is
        /// decided: its record and the list of profiles read again, the certificate not.
        /// </summary>
        /// <remarks>
        /// **An identity is as old as its connection's first read.** `For` reads the
        /// record and the list when the request arrives, and a change then waits for its turn on
        /// KeePass's thread, in which the operator can revoke the client, take a profile off the
        /// list or accept a renewal that narrows the record. A decision made in that turn about
        /// the identity read before it would be made for a client the database no longer holds
        /// that way. So whoever decides inside the turn asks this first, in the same turn, and
        /// asks the rules about what it answers.
        ///
        /// **The certificate is not judged again.** The anchor, the usage and the dates are facts
        /// about bytes the connection already holds, and a request that began inside its dates
        /// is answered. What an operator can change under a request is the record and the list,
        /// and an authority recreated or deleted answers as no record, which
        /// <see cref="IGateTarget.Admission"/> has the argument for.
        ///
        /// **Every failure is a refusal or a database that is not there**, never the identity as
        /// it was: a record that is gone refuses, a database that shut is locked, and anything
        /// that will not read refuses with the error logged. Records that could not be listed
        /// just now throw <see cref="VaultUnsettled"/> as they do for `For`, and the
        /// caller answers it `busy`, since nothing here knows whether the change it interrupts
        /// has been made.
        ///
        /// A caller that is not paired is handed back as it is, having nothing to read again,
        /// and so is any caller where no target is served, which is a test's arrangement.
        /// </remarks>
        public static ClientIdentity Again(ClientIdentity client, IGateTarget target, IGateLog log)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (target == null || !client.IsPaired)
            {
                return client;
            }

            var logger = log ?? NullGateLog.Instance;
            var databaseId = client.DatabaseId;

            if (target.IsLocked)
            {
                return ClientIdentity.Locked(databaseId);
            }

            if (target.Unavailable != null)
            {
                return ClientIdentity.Unavailable(databaseId, target.Unavailable);
            }

            try
            {
                // In the order `For` reads them, and for its reasons.
                var named = target.KnownProfiles;
                var unreadable = target.ProfilesUnreadable != null;
                var record = target.Admission(client.AdmissionKey);

                if (record == null)
                {
                    logger.Warn("refused a certificate whose admission record went while its request waited: " +
                                CertificateDigest.Shorten(client.AdmissionKey));
                    return client.AsRefused("this certificate has no admission record");
                }

                return client.AsNow(
                    Effective(record.Profiles, named),
                    Removed(record.Profiles, named),
                    record.ClientKey,
                    unreadable);
            }
            catch (DatabaseShut error)
            {
                logger.Info("a certificate could not be checked again, because " + error.Message);
                return ClientIdentity.Locked(databaseId);
            }
            catch (VaultUnsettled)
            {
                // Named so that the filter below, which takes every failure of this plugin's own,
                // does not make a refusal of a database that only has to be asked again.
                throw;
            }
            catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfCryptography(error))
            {
                logger.Error("a certificate could not be checked against this database again", error);
                return client.AsRefused("this certificate could not be checked");
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

            foreach (var profile in held.Where(
                profile => profile != null && named.Contains(profile, StringComparer.Ordinal)))
            {
                kept.Add(profile);
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

            foreach (var profile in held.Where(
                profile => profile != null
                           && (named == null || !named.Contains(profile, StringComparer.Ordinal))))
            {
                removed.Add(profile);
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
