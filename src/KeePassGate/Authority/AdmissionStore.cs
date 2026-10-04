using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The admitted clients of one database: one custom data key each, under `KPG.Client.`.
    /// </summary>
    /// <remarks>
    /// One key per client rather than one document, because a replicated database merges per key.
    /// Two hosts pairing different clients then keep both, where a single blob would be
    /// last-writer-wins and would lose a pairing without saying so. A revocation is a key of its
    /// own as well, under `KPG.Revoked.`, for the reason `Remove` gives.
    ///
    /// Lookups compare every stored key against the one asked for without stopping at the first
    /// difference, rather than asking the dictionary. The key is a digest of a certificate somebody
    /// else chose, so the comparison must not report how much of it matched.
    ///
    /// **A failure to read is not an absence, and where that matters the answer says so.** For
    /// `Find` it does not: both are null, because the safe answer to "can this certificate be
    /// admitted" is no either way, and a store that would not answer says so with an `Error` of
    /// its own, immediately before the caller's `refused a certificate with no admission record`.
    /// `All` carries the difference in an `AdmissionListing`, because a screen that states how
    /// many clients are admitted would otherwise turn "could not tell" into "nobody".
    /// </remarks>
    public sealed class AdmissionStore
    {
        private readonly IKeyValueStore _store;
        private readonly IGateLog _log;

        public AdmissionStore(IKeyValueStore store, IGateLog log)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _store = store;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>
        /// The record admitting this certificate, or null. Absent is the answer to everything that
        /// is not exactly one readable record with no revocation beside it: no key, an unreadable
        /// value, a revocation, anything at all.
        /// </summary>
        public AdmissionRecord Find(string admissionKey)
        {
            bool listed;
            return Find(admissionKey, out listed);
        }

        /// <summary>
        /// The same, saying whether the records could be listed at all, since a listing that
        /// failed finds nothing and that is not the same as nothing being there.
        /// </summary>
        public AdmissionRecord Find(string admissionKey, out bool listed)
        {
            listed = true;
            if (string.IsNullOrEmpty(admissionKey))
            {
                return null;
            }

            AdmissionRecord found = null;
            var revoked = false;

            foreach (var pair in Stored(out listed))
            {
                var key = pair.Key;

                // Every key is compared, both ways, and neither comparison stops at the first
                // difference. A dictionary lookup would be quicker and would leak how much of a
                // digest was right.
                var isRecord = FixedTime.Equals(AuthorityKeys.AdmissionKeyIn(key), admissionKey);
                var isRevocation = FixedTime.Equals(AuthorityKeys.AdmissionKeyInRevocation(key), admissionKey);

                // No first-match-wins here: a store keys by string, so at most one of each can
                // match. The loop runs to the end anyway, which is the whole point of it.
                if (isRecord)
                {
                    found = Read(key);
                }

                if (isRevocation)
                {
                    revoked = true;
                }
            }

            if (revoked && found != null)
            {
                // Only a merge puts a record back beside its revocation, so this is the moment the
                // operator's file is known to have taken in a copy older than the revocation.
                _log.Warn("an admission record came back beside its revocation and admits nobody: " +
                          CertificateDigest.Shorten(admissionKey));
            }

            return revoked ? null : found;
        }

        /// <summary>
        /// Every record that can be read, the keys of those that cannot, and whether the listing
        /// that found them was itself read. A record that cannot be read is reported, admits
        /// nobody, and is named by its key; a listing that cannot be made is reported and
        /// answered, which is not the same as answering that there are none. A record with a
        /// revocation beside it is not listed, only counted, as one that came back.
        /// </summary>
        public AdmissionListing All()
        {
            bool listed;
            var stored = Stored(out listed);
            var revoked = Revoked(stored);
            var records = new List<AdmissionRecord>();
            var unreadable = new List<string>();
            var cameBack = 0;

            foreach (var key in RecordKeys(stored))
            {
                var admissionKey = AuthorityKeys.AdmissionKeyIn(key);
                if (revoked.Contains(admissionKey))
                {
                    cameBack++;
                    continue;
                }

                var record = Read(key);
                if (record != null)
                {
                    records.Add(record);
                    continue;
                }

                // Listed with something stored, and not a record: damaged by a merge, edited by
                // hand, or written by a later format. Named, so that it can be taken away.
                unreadable.Add(admissionKey);
            }

            return new AdmissionListing(records, unreadable, cameBack, listed);
        }

        /// <summary>
        /// How many records are stored and not revoked, readable or not, and whether they could
        /// be listed at all.
        /// </summary>
        /// <remarks>
        /// Keys rather than readable records, which is the count the authority's own summary has
        /// always meant: a record that will not parse is still a record somebody has to be told
        /// about, and reporting it as absent would hide the damage rather than show it. A record
        /// beside its revocation is not counted, because it admits nobody and the clients page
        /// counts it apart, as one that came back.
        /// </remarks>
        public int Count(out bool listed)
        {
            var stored = Stored(out listed);
            var revoked = Revoked(stored);

            return RecordKeys(stored).Count(key => !revoked.Contains(AuthorityKeys.AdmissionKeyIn(key)));
        }

        public void Write(AdmissionRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            _store.Set(record.StorageKey, record.Save());
        }

        /// <summary>
        /// The operator's standing answer to renewing this certificate, or null when there is none.
        /// </summary>
        /// <remarks>
        /// **A value that will not read is answered as forever**, which is the refusing direction:
        /// only a hand at Database Maintenance or a later version of this plugin writes one, and
        /// answering it as no answer at all would raise a dialog the operator had said not to. It
        /// is said in the log, and it goes when the certificate stops being admitted.
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
        /// The clients page's Allow renewal, and nothing else: the answer otherwise goes only with
        /// the certificate it is about. A value that will not read is taken away like any other,
        /// since it is answered as forever and lifting forever is what the operator asked for.
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
        /// Takes every admission record away, and every revocation and standing answer with them,
        /// answering with how many clients went.
        /// </summary>
        /// <remarks>
        /// **Refuses rather than reporting nothing when the listing cannot be made.** The authority is
        /// retired and removed before this runs, so a delete that could not enumerate would leave
        /// records behind pointing at an authority that is gone, and report that there were none to
        /// remove. That is the one answer a caller cannot recover from, so it throws instead.
        ///
        /// **The revocations go too, and so do the records they emptied.** Every certificate they
        /// name was issued by the authority retired a moment ago, and that retirement is only ever
        /// added, so it refuses each of those certificates, revoked or not, whatever a stale copy
        /// merged in afterwards brings back. A revocation beside it would refuse nothing more, and
        /// would sit in the file until its certificate expired and another revocation happened to
        /// prune it. The count is of the records that were not empty, as it always was.
        /// </remarks>
        public int RemoveAll()
        {
            bool listed;
            var stored = Stored(out listed);

            if (!listed)
            {
                throw new AuthorityFailure(
                    "The admission records could not be listed, so none of them was removed.");
            }

            var clients = RecordKeys(stored).Count;

            foreach (var pair in stored)
            {
                _store.Remove(pair.Key);
            }

            return clients;
        }

        /// <summary>
        /// Revocation: a revocation added beside the record, and the record emptied.
        /// </summary>
        /// <remarks>
        /// Whatever calls this decides when it lands; the revocation is written when the change it
        /// belongs to is accepted. From the next connection after that the client is refused, because
        /// records are read from the live database and never cached.
        ///
        /// **Two writes, and the one that holds is the second key.** The revocation is added under
        /// `KPG.Revoked.`, beside the record, and is never changed afterwards; the record is
        /// written empty, so that it carries no profiles whatever happens to the revocation. A
        /// client is admitted only by a record that is not empty and has no revocation beside it.
        ///
        /// **Why a key of its own rather than the emptied record alone.** KeePass merges this
        /// dictionary key by key on modification time, and a mark decided that way loses whenever
        /// its time does: a file saved without the times, which KeePassXC and KeePass before 2.48
        /// both do, a clock behind the pairing host's, or an import with Overwrite existing from a
        /// copy still holding the record. The record comes back in every one of those. The merge
        /// never removes a key the target holds and never reads a value, though, so a key only
        /// ever added survives all of them. That is measured against the KeePass this builds with,
        /// in the host tests, so a release that changes the rule fails the gate.
        ///
        /// **Its value is the certificate's `notAfter`**, which is what lets it be pruned: past
        /// that moment the gate refuses the certificate on its dates, so the revocation has nothing
        /// left to refuse. A record that cannot be read gives no date, and its revocation is
        /// written with an empty value that is never pruned.
        ///
        /// **Pruning expired revocations rides on this write and on nothing else.** Revocations
        /// are only ever added here, so pruning here keeps their number at the unexpired ones and
        /// the one just written, and opening a database, or reading it, changes nothing in it.
        /// Records that came back beside their revocations go in the same write, and so do the
        /// records of expired certificates: see <see cref="PruneCameBack"/> and
        /// <see cref="PruneExpired"/>.
        /// </remarks>
        public bool Remove(string admissionKey)
        {
            return Remove(admissionKey, DateTimeOffset.UtcNow);
        }

        /// <summary>The same, at a moment the caller chose, which is what the prune is measured against.</summary>
        public bool Remove(string admissionKey, DateTimeOffset now)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return false;
            }

            var key = AuthorityKeys.ClientRecordKey(admissionKey);
            var stored = _store.Get(key);
            if (Gone(stored))
            {
                return false;
            }

            var revocation = AuthorityKeys.RevocationKey(admissionKey);

            // Only when absent. A revocation already there is never changed, because a changed one
            // has a modification time a merge can lose to.
            if (_store.Get(revocation) == null)
            {
                _store.Set(revocation, Expiry(admissionKey, stored));
            }

            _store.Set(key, string.Empty);

            // Records first, because a record is known to have come back by the revocation beside
            // it, and the prune after this can take an expired revocation away. The standing
            // answers last, because what they ask is whether a record is still there.
            PruneCameBack();
            Prune(now, revocation);
            PruneExpired(now);
            PruneRefusals();

            // Read back rather than assumed, the way every other mutation here is believed. The
            // answer this returns is what the operator is shown, and telling somebody a client is
            // revoked when the write did not land is the one wrong answer that cannot be noticed
            // until that client connects again.
            return Gone(_store.Get(key)) && _store.Get(revocation) != null;
        }

        /// <summary>
        /// Takes away every revocation whose certificate has expired, except the one named, with
        /// the emptied record beside it, and leaves every other one where it is.
        /// </summary>
        /// <remarks>
        /// One whose record has come back goes like any other: past its certificate's end the
        /// gate refuses that certificate on its dates, and the record goes in the same write.
        ///
        /// **The emptied record goes with it**, because past that end it has nothing left to
        /// refuse either, and kept it would be a key for every client ever revoked: a client given
        /// a week at a time, which gives up its older certificate each time it renews, makes that
        /// a number with no bound. A stale copy that brings it back brings back an empty value, or a
        /// record of a certificate that has expired, which the next write takes away again.
        ///
        /// A failure here is logged and nothing else, because the revocation this rides on has
        /// already been written and is what the caller is answered about. A revocation left
        /// behind costs one key until the next one.
        /// </remarks>
        private void Prune(DateTimeOffset now, string except)
        {
            try
            {
                var stored = Stored(out _);

                foreach (var pair in stored.Where(pair => AuthorityKeys.IsRevocation(pair.Key)))
                {
                    var key = pair.Key;
                    if (string.Equals(key, except, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    DateTimeOffset notAfter;
                    if (!DateTimeOffset.TryParseExact(
                            pair.Value,
                            Moments.WireFormat,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out notAfter))
                    {
                        continue;
                    }

                    if (!CertificateValidity.HasExpired(notAfter, now))
                    {
                        continue;
                    }

                    _store.Remove(key);
                    Ended("the revocation", AuthorityKeys.AdmissionKeyInRevocation(key), notAfter, now);

                    // Only an emptied one. A record that came back beside it went before this ran,
                    // and anything else under the key is a record of an expired certificate, which
                    // `PruneExpired` takes away when it can read it and keeps when it cannot.
                    var record = AuthorityKeys.ClientRecordKey(AuthorityKeys.AdmissionKeyInRevocation(key));
                    if (string.Equals(_store.Get(record), string.Empty, StringComparison.Ordinal))
                    {
                        _store.Remove(record);
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the revocations of expired certificates could not be pruned: " + error.Message);
            }
        }

        /// <summary>
        /// Takes away every record whose certificate has expired, answering with what went so that
        /// a write that does not land can put it back.
        /// </summary>
        /// <remarks>
        /// **Only as part of a write this plugin makes anyway**, which is the rule every prune
        /// here follows: a revocation, and every write <c>AuthorityStore.Prune</c> rides on, which
        /// is pairing, renewing, keeping one authority, and recreating or deleting it. Opening or
        /// reading a database changes nothing in it, so an expired record stays until then, and
        /// the clients page shows it as expired meanwhile.
        ///
        /// Past its certificate's end the gate refuses that certificate on its dates, so the
        /// record admits nobody and has nothing left to say. Kept, records of expired certificates
        /// would be a key for every certificate ever issued, and a client given a week at a time,
        /// renewing as each runs out, makes that a number with no bound. A stale copy that
        /// brings one back does no harm, since its certificate is refused on its dates, and the
        /// next write takes it away again.
        ///
        /// **A record that cannot be read gives no date, and is kept**, named on the clients page
        /// so that the operator can take it away. So is the emptied record of a revoked client,
        /// which goes with its revocation instead, in <see cref="Prune"/>.
        ///
        /// A failure is logged and answered with what went before it, because the write this
        /// rides on is what the caller is answered about, and an expired record admits nobody.
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, string>> PruneExpired(DateTimeOffset now)
        {
            var removed = new List<KeyValuePair<string, string>>();

            try
            {
                var stored = Stored(out _);

                foreach (var pair in stored.Where(pair => AuthorityKeys.IsClientRecord(pair.Key) && !Gone(pair.Value)))
                {
                    var record = AdmissionRecord.Load(AuthorityKeys.AdmissionKeyIn(pair.Key), pair.Value);
                    DateTimeOffset ended;
                    if (record == null || !HasExpired(record, now, out ended))
                    {
                        continue;
                    }

                    _store.Remove(pair.Key);
                    removed.Add(pair);
                    Ended("the record", record.AdmissionKey, ended, now);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the records of expired certificates could not be taken away: " + error.Message);
            }

            return removed;
        }

        /// <summary>
        /// Takes away every standing answer about a certificate no record admits any more, answering
        /// with what went so that a write that does not land can put it back.
        /// </summary>
        /// <remarks>
        /// **On the writes every prune here rides on, and after the others**, because what it asks
        /// is whether the record is still there: a revocation empties it, an expired one is taken
        /// away, one that came back beside its revocation goes. A record that cannot be read is
        /// still there, so its answer stays with it. Past that, an answer refuses a renewal nothing
        /// could ask for, since a renewal is asked with the certificate the record admits.
        ///
        /// A failure is logged and answered with what went before it, as the other prunes are.
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, string>> PruneRefusals()
        {
            var removed = new List<KeyValuePair<string, string>>();

            try
            {
                var stored = Stored(out _);
                var revoked = Revoked(stored);

                foreach (var pair in stored.Where(pair => AuthorityKeys.IsRenewalRefusal(pair.Key)))
                {
                    var admissionKey = AuthorityKeys.AdmissionKeyInRenewalRefusal(pair.Key);
                    if (!Gone(_store.Get(AuthorityKeys.ClientRecordKey(admissionKey))) && !revoked.Contains(admissionKey))
                    {
                        continue;
                    }

                    _store.Remove(pair.Key);
                    removed.Add(pair);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the standing answers about clients no longer admitted could not be taken away: " +
                          error.Message);
            }

            return removed;
        }

        /// <summary>
        /// Whether the certificate a readable record admits has expired, which a certificate that
        /// will not decode answers no to, since it gives no date.
        /// </summary>
        private static bool HasExpired(AdmissionRecord record, DateTimeOffset now, out DateTimeOffset ended)
        {
            ended = default(DateTimeOffset);

            try
            {
                using (var certificate = record.Certificate())
                {
                    ended = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                    return CertificateValidity.HasExpired(certificate, now);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                return false;
            }
        }

        /// <summary>
        /// Says in the log that a prune took something away on this machine's clock, and when the
        /// certificate it was about ended.
        /// </summary>
        /// <remarks>
        /// A refusal on dates is undone by setting a wrong clock right; a key taken out of the
        /// file is not, so each one is named, with both moments, for whoever finds a client gone
        /// and a clock that was wrong when it went.
        /// </remarks>
        private void Ended(string what, string admissionKey, DateTimeOffset ended, DateTimeOffset now)
        {
            _log.Info(what + " of a certificate that ended at " + Moments.Wire(ended) + " goes with this write, " +
                      "by this machine's clock, which reads " + Moments.Wire(now) + ": " +
                      CertificateDigest.Shorten(admissionKey));
        }

        /// <summary>
        /// Takes away every record that came back beside its revocation, answering with what went
        /// so that a write that does not land can put it back.
        /// </summary>
        /// <remarks>
        /// **Only as part of a write this plugin makes anyway**, which is the rule every prune
        /// here follows: a revocation, and every write <c>AuthorityStore.Prune</c> rides on, which
        /// is pairing, renewing, keeping one authority, and recreating or deleting it. Opening or
        /// reading a database changes nothing in it, so a record a merge brought back stays until
        /// then. It admits nobody meanwhile, because <see cref="Find(string)"/> reads the revocation rather
        /// than waiting for this, and the clients page counts it under the list rather than
        /// listing it as a client.
        ///
        /// Deleted rather than emptied. The revocation beside it is what refuses it and is never
        /// changed, so the record has nothing left to say, and a stale copy that brings it back
        /// again only brings it back to be taken away again.
        ///
        /// A failure is logged and answered with what went before it, because the write this
        /// rides on is what the caller is answered about, and a record left behind admits nobody.
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, string>> PruneCameBack()
        {
            var removed = new List<KeyValuePair<string, string>>();

            try
            {
                var stored = Stored(out _);
                var revoked = Revoked(stored);

                foreach (var pair in stored.Where(pair => AuthorityKeys.IsClientRecord(pair.Key) && !Gone(pair.Value)))
                {
                    if (!revoked.Contains(AuthorityKeys.AdmissionKeyIn(pair.Key)))
                    {
                        continue;
                    }

                    _store.Remove(pair.Key);
                    removed.Add(pair);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the records that came back beside their revocations could not be taken away: " +
                          error.Message);
            }

            return removed;
        }

        /// <summary>
        /// When the certificate a record admits stops being valid, as a revocation stores it, or
        /// empty when the record will not say.
        /// </summary>
        private static string Expiry(string admissionKey, string stored)
        {
            var record = AdmissionRecord.Load(admissionKey, stored);
            if (record == null)
            {
                return string.Empty;
            }

            try
            {
                using (var certificate = record.Certificate())
                {
                    return Moments.Wire(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Whether a stored value means no record: absent, or the mark a revocation leaves.
        /// </summary>
        /// <remarks>
        /// A revocation writes the key empty rather than removing it, because a removal is the one
        /// change a kdbx merge cannot carry: it has no modification time, so the copy in another
        /// replica comes back. `CustomDataStore.Remove` says why. Here the two look alike on
        /// purpose, so that nothing above this asks which kind of gone a client is. What keeps a
        /// record that came back from admitting anybody is the revocation beside it, not this.
        /// </remarks>
        private static bool Gone(string stored)
        {
            return string.IsNullOrEmpty(stored);
        }

        private AdmissionRecord Read(string key)
        {
            var admissionKey = AuthorityKeys.AdmissionKeyIn(key);

            string stored;
            try
            {
                stored = _store.Get(key);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                // Guarded the way the listing is, and for the same race: the gate reads custom data
                // on a connection thread while the user interface thread can be writing it.
                _log.Error("an admission record could not be read", error);
                return null;
            }

            if (string.IsNullOrEmpty(stored))
            {
                return null;
            }

            var record = AdmissionRecord.Load(admissionKey, stored);
            if (record == null)
            {
                // Damage in one record is not damage in the database, so it is reported and the
                // rest are served. The client it belonged to is refused, which is the safe half.
                _log.Warn("an admission record could not be read and admits nobody: " +
                          CertificateDigest.Shorten(admissionKey));
            }

            return record;
        }

        /// <summary>
        /// The records, revocations and standing answers stored, with their values, and whether they
        /// could be listed at all. The enumeration itself is what throws in the race this guards, so
        /// `listed` is set from inside the `try` rather than before it, and the values are read
        /// inside it too.
        /// </summary>
        private List<KeyValuePair<string, string>> Stored(out bool listed)
        {
            try
            {
                var keys = _store.Keys;
                var found = keys == null
                    ? new List<KeyValuePair<string, string>>()
                    : keys.Where(key => AuthorityKeys.IsClientRecord(key)
                                        || AuthorityKeys.IsRevocation(key)
                                        || AuthorityKeys.IsRenewalRefusal(key))
                          .Select(key => new KeyValuePair<string, string>(key, _store.Get(key)))
                          .ToList();

                listed = true;
                return found;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the admission records could not be listed", error);
                listed = false;
                return new List<KeyValuePair<string, string>>();
            }
        }

        /// <summary>The keys of the records that are not gone, in the order they were listed.</summary>
        private static List<string> RecordKeys(IEnumerable<KeyValuePair<string, string>> stored)
        {
            return stored.Where(pair => AuthorityKeys.IsClientRecord(pair.Key) && !Gone(pair.Value))
                         .Select(pair => pair.Key)
                         .ToList();
        }

        /// <summary>
        /// The admission keys that carry a revocation, whatever its value: the key being there is
        /// the revocation, and the value only says when it may be pruned.
        /// </summary>
        private static HashSet<string> Revoked(IEnumerable<KeyValuePair<string, string>> stored)
        {
            return new HashSet<string>(
                stored.Where(pair => AuthorityKeys.IsRevocation(pair.Key))
                      .Select(pair => AuthorityKeys.AdmissionKeyInRevocation(pair.Key)),
                StringComparer.Ordinal);
        }
    }
}
