using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using KeePassGate.Diagnostics;

namespace KeePassGate.Authority
{
    /// <summary>
    /// Reads and writes the authority in a database's custom data, under the `KPG.` keys that
    /// `CA.md` §204 lists.
    /// </summary>
    /// <remarks>
    /// Everything here fails closed. A value that will not decode makes the authority damaged
    /// rather than absent, and damage is never quietly repaired: the operator is told, and the
    /// remedy is to recreate, which is a decision with consequences that only they can take.
    ///
    /// **Which authority a database has is decided by the retirements, never by counting
    /// documents.** Each authority is a key of its own, written once, and each one replaced,
    /// deleted or not chosen has a retirement beside it that is only ever added. The authorities
    /// are the documents with no retirement. Straight after a merge a retired document can be back
    /// beside its retirement, and it serves nothing whether or not anything has taken it away yet:
    /// taking it away is <see cref="Prune()"/>, which rides on writes this plugin makes anyway and
    /// which nothing here depends on having happened.
    /// </remarks>
    public sealed class AuthorityStore
    {
        private readonly IKeyValueStore _store;
        private readonly AdmissionStore _admissions;

        /// <summary>
        /// Over one database's custom data, reaching its admission records through an
        /// `AdmissionStore` of its own.
        /// </summary>
        /// <remarks>
        /// The admission records are counted and removed through `AdmissionStore` rather than by a
        /// second copy of the same enumeration here. There were two, and only one of them was
        /// guarded against a listing that throws, which `Expected.OfCustomData` names, and that is
        /// the asymmetry that made a listing failure cost every open database its connection rather
        /// than one lookup.
        /// </remarks>
        public AuthorityStore(IKeyValueStore store, IGateLog log)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _store = store;
            _admissions = new AdmissionStore(store, log);
        }

        /// <summary>
        /// What the database holds. The caller owns the result and disposes it, which disposes the
        /// certificates it carries.
        /// </summary>
        /// <remarks>
        /// One authority is read whole or damaged, as it always was. None is absence. More than
        /// one is a reading of its own, carrying every candidate, because the answer to it is a
        /// choice only the operator can make and the one thing this must not do is make it for
        /// them by serving whichever happens to sort first.
        /// </remarks>
        public AuthorityReading Read()
        {
            bool listed;
            var records = _admissions.Count(out listed);

            List<KeyValuePair<string, string>> live;
            bool retired;
            try
            {
                var retirements = Retired();
                live = Live(retirements);
                retired = retirements.Count > 0;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                // Which authority there is can only be answered by listing, and a listing that
                // failed is not a database with none: absence invites Create.
                return AuthorityReading.Damaged(
                    new[] { CouldNotReadProblem + error.Message }, records, listed);
            }

            if (live.Count == 0)
            {
                // A retirement is never removed, so a database that held an authority is still
                // read as retired once every document is gone: deleted, or retired by copies that
                // each kept a different authority. Read as absence, it looked like a database that
                // never had an authority.
                return retired ? AuthorityReading.Retired(records, listed) : AuthorityReading.Absent(records, listed);
            }

            if (live.Count == 1)
            {
                var problems = new List<string>();
                var material = ReadOne(live[0].Key, live[0].Value, problems);
                return material == null
                    ? AuthorityReading.Damaged(problems, records, listed)
                    : AuthorityReading.Whole(material, records, listed);
            }

            var candidates = new List<AuthorityCandidate>();
            foreach (var pair in live)
            {
                var problems = new List<string>();
                var material = ReadOne(pair.Key, pair.Value, problems);
                candidates.Add(material == null
                    ? AuthorityCandidate.Damaged(pair.Key, problems)
                    : AuthorityCandidate.Whole(pair.Key, material));
            }

            return AuthorityReading.Undecided(candidates, records, listed);
        }

        /// <summary>
        /// Everything that decides which authority is served, as one string: every authority and
        /// every retirement, with their values, in a fixed order.
        /// </summary>
        /// <remarks>
        /// For a cache that has to notice any change to that decision without parsing anything.
        /// Two readings that return the same text read the same authorities, whatever order the
        /// dictionary happens to enumerate them in.
        /// </remarks>
        public string Stored()
        {
            var text = new StringBuilder();
            foreach (var key in StateKeys())
            {
                text.Append(key).Append('=').Append(_store.Get(key)).Append('\n');
            }

            return text.ToString();
        }

        /// <summary>
        /// Writes an authority, all three parts in the one value that keeps them together, under
        /// its own digest.
        /// </summary>
        /// <remarks>
        /// One write rather than three, which retires the ordering the three keys needed: there is
        /// no moment at which a reader can see a part of this, because a dictionary either has the
        /// key or does not.
        ///
        /// **Refused over a document already there**, which cannot happen for an authority minted
        /// a moment ago and is the one thing that must never happen at all. A document is written
        /// once: a merge decides between two values of one key by modification time and never by
        /// value, so a document rewritten in place is one a stale copy can put back.
        /// </remarks>
        public void Write(AuthorityMaterial material)
        {
            if (material == null)
            {
                throw new ArgumentNullException(nameof(material));
            }

            var key = AuthorityKeys.AuthorityKey(material.AuthorityDigest);
            if (!string.IsNullOrEmpty(_store.Get(key)))
            {
                throw new AuthorityFailure("An authority with this digest is already stored, and none is written twice.");
            }

            var sniKey = material.SniKey;
            try
            {
                _store.Set(
                    key,
                    AuthorityDocument.Save(
                        CertificateBlob.Save(material.AuthorityKey),
                        CertificateBlob.Save(material.HostKey),
                        Convert.ToBase64String(sniKey)));
            }
            finally
            {
                Array.Clear(sniKey, 0, sniKey.Length);
            }
        }

        /// <summary>
        /// Retires every authority, removes every document, and removes every admission record,
        /// because a record is meaningless once the authority that issued its certificate is gone.
        /// </summary>
        /// <remarks>
        /// What deletion does, and what recreation does before it writes the replacement. The two
        /// used to differ, recreation emptying the records rather than removing them, because the
        /// replacement went under the same key and an emptied record was the only mark a merge
        /// with an older copy would carry. The retirement is that mark now, and it marks the
        /// authority rather than each client: a record a stale copy brings back chains to an
        /// authority that is retired, so it admits nobody, and the clients page names it for what
        /// it is.
        ///
        /// A damaged document is retired by the digest its key is filed under, which is the one
        /// thing about it that can always be read.
        /// </remarks>
        /// <returns>How many admission records were removed.</returns>
        public int DeleteEverything()
        {
            return DeleteEverything(null);
        }

        /// <summary>The same, telling the caller once the database is about to change.</summary>
        /// <remarks>
        /// **So that a refusal after that point can say the database may have changed.** Listing
        /// the authorities is the step that fails with nothing written; every step after it writes,
        /// and the records are listed again last of all, so a failure there comes after every
        /// authority was retired and its document taken away, leaving a database that serves
        /// nothing under a refusal that read as nothing having happened.
        /// </remarks>
        /// <param name="changing">
        /// Called once the authorities are listed and before the first of them is retired, or null.
        /// </param>
        /// <returns>How many admission records were removed.</returns>
        public int DeleteEverything(Action changing)
        {
            var live = Listed("deleted");
            if (changing != null)
            {
                changing();
            }

            foreach (var pair in live)
            {
                Retire(pair.Key);
            }

            Prune();
            return _admissions.RemoveAll();
        }

        /// <summary>
        /// Keeps one of several authorities: every other one is retired, and every retired
        /// document is taken away.
        /// </summary>
        /// <remarks>
        /// **Refused for a digest that is not one of the choices**, whether it was never there,
        /// has been retired since the question was put, or will not read. Keeping it would retire
        /// every authority this database can serve and leave it serving none, under a message
        /// saying one had been kept.
        ///
        /// The records issued by the authorities retired here stay. They chain to nothing that is
        /// served, so they admit nobody, and the clients page says so on each row; the operator can
        /// take them away from there. The prune every write here rides on still runs, so the
        /// records of expired certificates go, and so does a record that came back beside its
        /// revocation, whichever authority issued it.
        /// </remarks>
        /// <returns>How many authorities were retired.</returns>
        public int Keep(string fingerprint)
        {
            var live = Listed("kept");
            if (string.IsNullOrEmpty(fingerprint) || !live.Any(pair => pair.Key == fingerprint))
            {
                throw new AuthorityFailure("That authority is not one of the ones this database can keep.");
            }

            var problems = new List<string>();
            using (var material = ReadOne(fingerprint, live.First(pair => pair.Key == fingerprint).Value, problems))
            {
                if (material == null)
                {
                    throw new AuthorityFailure("That authority cannot be read, so it cannot be kept.");
                }
            }

            var retired = 0;
            foreach (var pair in live)
            {
                if (pair.Key == fingerprint)
                {
                    continue;
                }

                Retire(pair.Key);
                retired++;
            }

            Prune();
            return retired;
        }

        /// <summary>
        /// Takes away every document whose authority is retired, every admission record that came
        /// back beside its revocation, every record of a certificate that has expired, and every
        /// standing answer to a renewal that no record is left to ask, answering with what went so
        /// that a write that does not land can put it back.
        /// </summary>
        /// <remarks>
        /// **Only ever as part of a write this plugin is making anyway**: keeping one authority,
        /// recreating or deleting it, or pairing or renewing. Opening or reading a database changes
        /// nothing in it, so a document a merge brought back stays until then, serving nothing,
        /// because the decision reads the retirement rather than the document; a record a merge
        /// brought back admits nobody, because admission reads the revocation rather than the
        /// record; and a record of an expired certificate admits nobody, because the gate reads its
        /// dates.
        /// </remarks>
        public IReadOnlyList<KeyValuePair<string, string>> Prune()
        {
            return Prune(DateTimeOffset.UtcNow);
        }

        /// <summary>The same, at a moment the caller chose, which is what a certificate's end is measured against.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Prune(DateTimeOffset now)
        {
            var retired = Retired();
            var removed = new List<KeyValuePair<string, string>>(_admissions.PruneCameBack());
            removed.AddRange(_admissions.PruneExpired(now));
            removed.AddRange(_admissions.PruneRefusals());

            foreach (var key in Keys().ToList())
            {
                var fingerprint = AuthorityKeys.FingerprintInAuthority(key);
                if (fingerprint == null || !retired.Contains(fingerprint))
                {
                    continue;
                }

                removed.Add(new KeyValuePair<string, string>(key, _store.Get(key)));
                _store.Remove(key);
            }

            return removed;
        }

        /// <summary>
        /// The authorities: every document with no retirement beside it, by digest, in the order
        /// of their digests.
        /// </summary>
        private List<KeyValuePair<string, string>> Live(HashSet<string> retired)
        {
            var live = new List<KeyValuePair<string, string>>();

            foreach (var key in Keys())
            {
                var fingerprint = AuthorityKeys.FingerprintInAuthority(key);
                if (fingerprint == null || retired.Contains(fingerprint))
                {
                    continue;
                }

                // Empty is no document, which is how the tab's own buffer answers for a key it
                // took away and how nothing this plugin writes ever looks.
                var stored = _store.Get(key);
                if (!string.IsNullOrEmpty(stored))
                {
                    live.Add(new KeyValuePair<string, string>(fingerprint, stored));
                }
            }

            live.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            return live;
        }

        /// <summary>
        /// The authorities, for a change that has to know all of them, or a refusal when they
        /// cannot be listed.
        /// </summary>
        /// <remarks>
        /// Refused rather than read as none, which is what `AdmissionStore.RemoveAll` does for the
        /// same reason: a deletion that could not see an authority would leave it in service and
        /// report that there was nothing to delete.
        /// </remarks>
        private List<KeyValuePair<string, string>> Listed(string verb)
        {
            try
            {
                return Live(Retired());
            }
            catch (Exception error) when (Expected.OfCustomData(error) && !(error is GateFailure))
            {
                throw new AuthorityFailure(
                    "The authorities could not be listed, so none of them was " + verb + ": " + error.Message);
            }
        }

        private HashSet<string> Retired()
        {
            var retired = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fingerprint in Keys().Select(AuthorityKeys.FingerprintInRetirement)
                                              .Where(fingerprint => fingerprint != null))
            {
                retired.Add(fingerprint);
            }

            return retired;
        }

        /// <summary>Every key the store holds, or none when it answers with nothing.</summary>
        private IEnumerable<string> Keys()
        {
            return _store.Keys ?? Enumerable.Empty<string>();
        }

        private List<string> StateKeys()
        {
            var keys = Keys().Where(AuthorityKeys.IsAuthorityState).ToList();
            keys.Sort(string.CompareOrdinal);
            return keys;
        }

        /// <summary>Added once and never changed, so writing it again is left alone.</summary>
        private void Retire(string fingerprint)
        {
            var key = AuthorityKeys.RetirementKey(fingerprint);
            if (_store.Get(key) == null)
            {
                _store.Set(key, AuthorityKeys.RetiredValue);
            }
        }

        /// <summary>
        /// One stored document as an authority, or null with what was wrong in `problems`.
        /// </summary>
        private static AuthorityMaterial ReadOne(string fingerprint, string stored, List<string> problems)
        {
            var document = AuthorityDocument.Read(stored, problems);
            if (document == null)
            {
                // Something is under the key and it is not an authority this plugin can use. That
                // is damage, never absence: absence invites Create, and creating over a document
                // somebody else's plugin version wrote would destroy it.
                return null;
            }

            // codeql[cs/missed-using-statement] disposed only when it is not handed back, which no `using` can do
            AuthorityMaterial material = null;
            byte[] sniKey = null;
            byte[] hostPkcs12 = null;

            try
            {
                var authority = CertificateBlob.Load(document.Authority);
                if (authority == null)
                {
                    problems.Add("The authority certificate cannot be read.");
                }

                // Decoded once, for the host to be read from and for the material to keep a copy
                // of, and overwritten below with the gate key, `CA.md` §208.
                hostPkcs12 = CertificateBlob.Bytes(document.Host);
                var host = CertifiedKey.From(hostPkcs12);
                if (host == null)
                {
                    problems.Add("The host certificate cannot be read.");
                }

                sniKey = ReadSniKey(document.SniKey, problems);

                // Asked of the authority alone, before anything needs the host as well, so that a
                // misfiled document whose host will not read is reported as both: the misfiling is
                // what points at a merge or an edit rather than at a damaged certificate.
                if (authority != null && !string.Equals(
                        CertificateDigest.Of(authority.CertificateDer), fingerprint, StringComparison.Ordinal))
                {
                    // The key is what a retirement names, so an authority filed under some other
                    // digest is one that retiring could never reach.
                    problems.Add(MisfiledProblem);
                }

                if (authority == null || host == null)
                {
                    return null;
                }

                material = new AuthorityMaterial(authority, host, sniKey, hostPkcs12);

                if (!IssuedBy(material.Host, material.Authority))
                {
                    problems.Add("The host certificate was not issued by this authority.");
                }

                if (problems.Count > 0)
                {
                    return null;
                }

                var read = material;
                material = null;
                return read;
            }
            catch (Exception error) when (Expected.OfCryptography(error) || Expected.OfCustomData(error))
            {
                problems.Add(CouldNotReadProblem + error.Message);
                return null;
            }
            finally
            {
                if (sniKey != null)
                {
                    Array.Clear(sniKey, 0, sniKey.Length);
                }

                if (hostPkcs12 != null)
                {
                    Array.Clear(hostPkcs12, 0, hostPkcs12.Length);
                }

                if (material != null)
                {
                    material.Dispose();
                }
            }
        }

        /// <summary>
        /// Whether the two certificates belong together, by the anchor pin rather than by name.
        /// Every authority this plugin issues carries the same subject, so a name comparison
        /// accepts a host from somebody else's authority: found by a test that was written to
        /// prove the opposite.
        ///
        /// Expiry is not asked about here, and the pin never asks it. An expired pair is still a
        /// pair, and expiry is reported on its own terms rather than as "this does not belong to
        /// you".
        ///
        /// **Every answer is the database's.** While a chain engine answered this, it could fail to
        /// answer at all, on a locked down machine store or a CryptoAPI service that was not
        /// running, and that was reported apart, because the remedy offered for a foreign host
        /// destroys a healthy authority. The pin asks nothing of the machine now, so there is no
        /// such case left to report.
        /// </summary>
        private static bool IssuedBy(X509Certificate2 leaf, X509Certificate2 authority)
        {
            return AnchorPin.Chains(leaf, authority);
        }

        private static byte[] ReadSniKey(string blob, ICollection<string> problems)
        {
            if (string.IsNullOrEmpty(blob))
            {
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(blob);
            }
            catch (FormatException)
            {
                problems.Add("The gate key cannot be read.");
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            if (bytes.Length != AuthorityMaterial.SniKeyLength)
            {
                problems.Add(WrongLengthProblem(bytes.Length));
                return new byte[AuthorityMaterial.SniKeyLength];
            }

            return bytes;
        }

        /// <summary>What a gate key of the wrong length is reported as.</summary>
        private static string WrongLengthProblem(int length)
        {
            return "The gate key is " + length + " bytes rather than " + AuthorityMaterial.SniKeyLength + ".";
        }

        /// <summary>What an authority filed under a digest that is not its own is reported as.</summary>
        internal const string MisfiledProblem = "The authority is filed under a digest that is not its own.";

        /// <summary>What begins a problem that is an exception, before the exception's own message.</summary>
        internal const string CouldNotReadProblem = "The authority could not be read: ";

        /// <summary>
        /// The most one authority can be reported with at once, each at its longest, in the order
        /// <see cref="ReadOne"/> reports them, with `thrown` where an exception's message would be.
        /// </summary>
        /// <remarks>
        /// For the KPG tab, which is measured against the longest detail a damaged reading can
        /// produce. Here, beside `ReadOne`, because which problems can arrive together is decided
        /// by the order it asks in, and that is the one part of the measurement nothing can
        /// derive: a certificate that will not load stops every check that needs it, and an
        /// exception stops every check after it. So the gate key, at a length ten digits wide;
        /// then the digest the authority is filed under, which needs the authority; then one about
        /// the pair, which needs both certificates: the host was not issued by this authority, or
        /// reading the pair threw. The second is the one given, because the tab cuts a problem at
        /// `Clipped.Limit` and a thrown message long enough to be cut is never shorter than the
        /// sentence it stands in for. A document with parts missing, or one that will not read at
        /// all, stops at shorter sentences than these, the host that will not load among them.
        /// </remarks>
        internal static IReadOnlyList<string> LongestProblems(string thrown)
        {
            return new[] { WrongLengthProblem(int.MaxValue), MisfiledProblem, CouldNotReadProblem + thrown };
        }
    }
}
