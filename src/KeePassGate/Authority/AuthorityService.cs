using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The three things an operator can do to a database's authority: create it, recreate it, and
    /// delete it. Everything else about an authority happens without them.
    /// </summary>
    /// <remarks>
    /// Two rules run through this class. Nothing is destroyed implicitly: creating over an existing
    /// authority is refused rather than silently replacing one, because replacing it revokes every
    /// client at once and that has to be asked for. And nothing is believed without reading it
    /// back, because the store this writes to can have a dialog holding a stale copy of it, and a
    /// write that vanishes must be reported rather than assumed. What the read back found is then
    /// what the message says: a refusal claiming the database was left alone, made after something
    /// in it was already taken away, is a worse answer than the failure it is reporting, because
    /// the operator acts on it.
    /// </remarks>
    public sealed class AuthorityService
    {
        private readonly AuthorityStore _store;
        private readonly AuthorityIssuer _issuer;
        private readonly IGateLog _log;

        public AuthorityService(IKeyValueStore store, IGateLog log)
            : this(store, new AuthorityIssuer(), log)
        {
        }

        public AuthorityService(IKeyValueStore store, AuthorityIssuer issuer, IGateLog log)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            _store = new AuthorityStore(store);
            _issuer = issuer;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>What the database holds. The caller disposes what comes back.</summary>
        public AuthorityReading Read()
        {
            try
            {
                return _store.Read();
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                _log.Error("the authority could not be read", error);
                return AuthorityReading.Damaged(new[] { "The authority could not be read: " + error.Message }, 0);
            }
        }

        /// <summary>
        /// Creates one, and refuses if there is anything there already. A damaged authority is
        /// refused too: recreating is the answer to that, and it is a different button because it
        /// has different consequences.
        /// </summary>
        public AuthorityChange Create()
        {
            using (var existing = Read())
            {
                if (existing.Presence == AuthorityPresence.Present)
                {
                    return AuthorityChange.Refused(
                        "This database already has an authority. Use Recreate to replace it.");
                }

                if (existing.Presence == AuthorityPresence.Damaged)
                {
                    return AuthorityChange.Refused(
                        "This database holds an authority that cannot be read. Recreate it to replace it.");
                }
            }

            return Issue("created", null);
        }

        /// <summary>
        /// Replaces whatever is there. Every client admitted under the old authority loses its
        /// admission, and the message says how many, because that is the number the operator needs
        /// before pressing it and the only one they cannot work out themselves.
        /// </summary>
        public AuthorityChange Recreate()
        {
            // Removed only once the replacement exists. Removing first and issuing after meant a
            // mint that failed left the old authority and every admission record already gone,
            // under a message saying the recreation had not happened.
            var removed = 0;
            var cleared = false;
            var result = Issue("recreated", () =>
            {
                try
                {
                    removed = _store.DeleteEverything();
                    cleared = true;
                    return null;
                }
                catch (Exception error) when (Expected.OfCustomData(error))
                {
                    _log.Error("the old authority could not be removed", error);
                    return AuthorityChange.Refused(
                        "The old authority could not be removed: " + error.Message);
                }
            });
            if (!result.Succeeded)
            {
                // Once the clearing has run, a refusal is not a database that was left alone, and
                // the message has to say so or the operator reads "it did not happen" and goes
                // back to whatever they were doing with a database that now serves nobody. The
                // sentence describes what recreation does rather than asserting what landed,
                // because the store that swallowed the replacement swallowed the removal too and
                // nothing here can tell those apart.
                return cleared ? AuthorityChange.Refused(result.Message + ClearingNote()) : result;
            }

            return AuthorityChange.Done(result.Message + RevocationNote(removed), removed);
        }

        /// <summary>
        /// Removes the authority and every admission record with it. The plugin serves nothing for
        /// this database afterwards, which is the point: this is how a database is taken out of
        /// service.
        /// </summary>
        public AuthorityChange Delete()
        {
            int removed;
            try
            {
                removed = _store.DeleteEverything();
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the authority could not be deleted", error);
                return AuthorityChange.Refused("The authority could not be deleted: " + error.Message);
            }

            using (var check = Read())
            {
                if (check.Presence != AuthorityPresence.None)
                {
                    // Not "nothing was changed on disk", which this cannot know and which was
                    // wrong the moment the admission records went first: the removals are already
                    // asked for by the time the authority is read back, so a delete that comes
                    // apart halfway is the case being reported and reporting it as a no-op sends
                    // the operator away from a database that needs looking at.
                    _log.Warn("the authority survived a delete");
                    return AuthorityChange.Refused(
                        "The authority is still in the database after being deleted, so some of the " +
                        "deletion may have landed. Read the page again before pressing anything else.");
                }
            }

            _log.Info("authority deleted, admission records removed: " + removed);
            return AuthorityChange.Done("The authority was deleted." + RevocationNote(removed), removed);
        }

        /// <summary>
        /// What a recreation that failed after the clearing step has to add, whatever it failed
        /// at. The old authority and the records are asked for before the replacement is written,
        /// so every refusal from that point on is about a database that has already been changed.
        /// </summary>
        private static string ClearingNote()
        {
            return " Recreating removes the old authority and every admission record before writing " +
                   "the replacement, so what was there before may be gone as well.";
        }

        private static string RevocationNote(int removed)
        {
            if (removed <= 0)
            {
                return string.Empty;
            }

            return removed == 1
                ? " One client lost its admission."
                : " " + removed + " clients lost their admission.";
        }

        private static byte[] NewSniKey()
        {
            var key = new byte[AuthorityMaterial.SniKeyLength];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(key);
            }

            return key;
        }

        /// <param name="clearFirst">
        /// Runs after the new material is minted and before anything is written, or null when
        /// there is nothing to clear. `Recreate` removes the old authority here rather than
        /// before calling, so that a mint which fails destroys nothing: what it returns, if
        /// anything, is the refusal to report.
        /// </param>
        private AuthorityChange Issue(string verb, Func<AuthorityChange> clearFirst)
        {
            X509Certificate2 authority = null;
            X509Certificate2 host = null;
            byte[] sniKey = null;
            string minted = null;

            try
            {
                authority = _issuer.CreateAuthority();
                host = _issuer.IssueHost(authority);
                sniKey = NewSniKey();

                if (clearFirst != null)
                {
                    var refused = clearFirst();
                    if (refused != null)
                    {
                        return refused;
                    }
                }

                using (var material = new AuthorityMaterial(authority, host, sniKey))
                {
                    authority = null;
                    host = null;
                    minted = material.AuthorityDigest;
                    _store.Write(material);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                _log.Error("the authority could not be " + verb, error);
                return AuthorityChange.Refused("The authority could not be " + verb + ": " + error.Message);
            }
            finally
            {
                if (sniKey != null)
                {
                    Array.Clear(sniKey, 0, sniKey.Length);
                }

                Reset(authority);
                Reset(host);
            }

            // Believe it only after reading it back out of the store.
            using (var check = Read())
            {
                // Present is not enough, and recreation is what makes that matter. A write the
                // store swallows leaves the authority that was already there, which reads back as
                // present: the operator was told the recreation had worked while nothing had
                // changed and every client it claimed to revoke was still admitted. The digest of
                // what was just minted is the only thing that separates the two.
                if (!check.IsPresent ||
                    !string.Equals(check.Material.AuthorityDigest, minted, StringComparison.Ordinal))
                {
                    _log.Warn("the authority was written and could not be read back");
                    return AuthorityChange.Refused(NotReadBack(check.Presence));
                }

                _log.Info("authority " + verb + ", digest " + check.Material.AuthorityDigest);
                return AuthorityChange.Done(
                    "The authority was " + verb + ". Fingerprint " +
                    CertificateDigest.Shorten(check.Material.AuthorityDigest) + ".");
            }
        }

        /// <summary>
        /// What to say when the material was written and the read back did not find it whole.
        /// </summary>
        /// <remarks>
        /// Two states rather than one, because the operator's next move differs and the message
        /// used to give them neither. Nothing under the key is the write being swallowed, which is
        /// what a dialog holding a stale copy does and is the case this read back was written for.
        /// Something unreadable under the key is the write landing: the database now carries an
        /// authority this plugin refuses, `Create` answers "recreate it" from here on, and a
        /// message saying nothing had changed leaves the operator arguing with that.
        /// </remarks>
        private static string NotReadBack(AuthorityPresence presence)
        {
            return presence == AuthorityPresence.Damaged
                ? "The authority was written and could not be read back, so this database now holds " +
                  "one that cannot be used. Recreate it to replace it."
                : "The authority was written and could not be read back, so nothing was stored.";
        }

        private static void Reset(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return;
            }

            try
            {
                certificate.Reset();
            }
            catch (CryptographicException)
            {
                // Releasing a key handle that has already gone.
            }
        }
    }
}
