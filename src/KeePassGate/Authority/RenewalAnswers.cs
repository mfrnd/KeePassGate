using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The operator's standing answers to renewing a certificate, one custom data key for each
    /// certificate, under `KPG.RenewalRefused.`, `PAIRING.md` §312.
    /// </summary>
    /// <remarks>
    /// **Kept in the database's own custom data**, beside the profile names, until they move into
    /// the plugin's store in the pull request that keeps the standing answers to a renewal there,
    /// `STORE.md` §618. No standing answer admits anybody: an answer lost to another program asks
    /// the operator again and never ends wider.
    ///
    /// **An answer goes once no admission in force names its certificate**, with the plugin's next
    /// write, <see cref="Prune"/>. Nothing of it goes by a clock, since nothing removes an
    /// admission for its age, `STORE.md` §643.
    /// </remarks>
    public sealed class RenewalAnswers
    {
        private readonly IKeyValueStore _store;
        private readonly IGateLog _log;

        public RenewalAnswers(IKeyValueStore store, IGateLog log)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _store = store;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>
        /// The operator's standing answer to renewing this certificate, or null when there is none.
        /// </summary>
        /// <remarks>
        /// **A value that will not read is answered as forever**, which is the refusing direction:
        /// only a hand at Database Maintenance or a later version of this plugin writes such a
        /// value, and answering it as no answer at all would raise a dialog the operator had asked
        /// not to. It is stated in the log, and it goes when no admission names the certificate any
        /// more.
        /// </remarks>
        public RenewalRefusal StandingRefusal(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            var stored = _store.Get(AuthorityKeys.RenewalRefusalKey(admissionKey));
            if (stored == null)
            {
                return null;
            }

            var refusal = RenewalRefusal.Read(stored);
            if (refusal == null)
            {
                _log.Warn("a standing answer to a renewal could not be read, so it is answered as forever: " +
                          CertificateDigest.Shorten(admissionKey));
                return RenewalRefusal.Forever();
            }

            return refusal;
        }

        /// <summary>Writes the operator's standing answer to renewing this certificate.</summary>
        /// <exception cref="ArgumentException">
        /// For a refusal in this session, which is held in memory and never written here.
        /// </exception>
        public void RefuseRenewal(string admissionKey, RenewalRefusal refusal)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                throw new ArgumentException("An admission key is required.", nameof(admissionKey));
            }

            if (refusal == null)
            {
                throw new ArgumentNullException(nameof(refusal));
            }

            if (!refusal.IsWritten)
            {
                throw new ArgumentException(
                    "A renewal refused in this session is held in memory and never written to a database.",
                    nameof(refusal));
            }

            _store.Set(AuthorityKeys.RenewalRefusalKey(admissionKey), refusal.Write());
        }

        /// <summary>
        /// Takes the operator's standing answer to renewing this certificate away, so that its
        /// next renewal raises the dialog again, answering with whether there was one to take.
        /// </summary>
        /// <remarks>
        /// The clients page's Allow renewal: a value that will not read is taken away like any
        /// other, since it is answered as forever and lifting forever is what the operator asked
        /// for.
        /// </remarks>
        public bool LiftRenewalRefusal(string admissionKey)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            var key = AuthorityKeys.RenewalRefusalKey(admissionKey);
            if (_store.Get(key) == null)
            {
                return false;
            }

            _store.Remove(key);
            return true;
        }

        /// <summary>
        /// Takes away every standing answer about a certificate not among those given, answering
        /// with what went so that a write that does not land can put it back.
        /// </summary>
        /// <remarks>
        /// **On a write of the plugin's own and on nothing else**: the saved writes of a pairing, a
        /// renewal and a certificate given up, and the KPG tab's OK, each against the admissions in
        /// force as that write leaves them. Opening or reading a database changes nothing in it.
        ///
        /// A failure is logged and answered with what went before it, because the write this
        /// rides on is what the caller is answered about, and an answer left behind admits nobody.
        /// </remarks>
        /// <param name="admitted">
        /// The certificates the admissions in force name, by admission key.
        /// </param>
        public IReadOnlyList<KeyValuePair<string, string>> Prune(ICollection<string> admitted)
        {
            var removed = new List<KeyValuePair<string, string>>();
            if (admitted == null)
            {
                return removed;
            }

            try
            {
                var keys = _store.Keys;
                foreach (var key in (keys ?? Enumerable.Empty<string>()).Where(AuthorityKeys.IsRenewalRefusal).ToList())
                {
                    if (admitted.Contains(AuthorityKeys.AdmissionKeyInRenewalRefusal(key)))
                    {
                        continue;
                    }

                    var value = _store.Get(key);
                    _store.Remove(key);
                    removed.Add(new KeyValuePair<string, string>(key, value));
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the standing answers about certificates no admission names could not be taken away: " +
                          error.Message);
            }

            return removed;
        }
    }
}
