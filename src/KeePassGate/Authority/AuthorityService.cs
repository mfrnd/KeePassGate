using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using KeePassGate.Diagnostics;
using KeePassGate.Store;

namespace KeePassGate.Authority
{
    /// <summary>
    /// What an operator can do to a database's authority: create it, recreate it, delete it, and
    /// keep an authority where several stand, each a write into the plugin's store, `STORE.md` §614;
    /// seal the store again where it stopped, §625; and start the store again under a new key,
    /// §632.
    /// </summary>
    /// <remarks>
    /// Nothing is destroyed implicitly: creating where an authority stands is refused rather than
    /// replacing it, because replacing it revokes every client at once and that has to be asked
    /// for.
    ///
    /// **A command is answered by its store's write, and nothing is read back after it**,
    /// `STORE.md` §614. On the KPG tab the store is the dialog's buffer, which holds an act only
    /// where the plan of every act held with it takes the act, and reads the store as that same
    /// plan leaves it: a read back there finds what the plan made, so it refuses nothing the write
    /// took, and a refusal after the write would leave the act held for OK all the same. Keeping
    /// an authority from KeePass's main window writes through `KdbxStore`, which applies to the
    /// database the plan it made of a reading taken in the same turn, as the tab's OK does.
    ///
    /// **Each command is an act composed now and held to its ground at the write**, `STORE.md`
    /// §627: on the KPG tab the store is the dialog's buffer, the write lands at its OK, and the
    /// act is left out there if what it was composed against went meanwhile.
    ///
    /// **The admissions go with the authority that issued them**: the write of a Recreate or a
    /// Delete deletes every admission its retirements refuse, `STORE.md` §627, so nothing here
    /// takes them away apart from the act, and an act left out takes nothing.
    /// </remarks>
    public sealed class AuthorityService
    {
        /// <summary>
        /// Why a Create or a Recreate is refused where its write would leave the store without
        /// room for the write after it, `STORE.md` §643.
        /// </summary>
        public const string Full = "the store would be left full, and nothing is served then";

        /// <summary>
        /// What can be done about a store a Create or a Recreate would leave full, where the page
        /// offers Start store again: a start writes a store of its own under a new key, `STORE.md`
        /// §632, whose summary entry names no earlier summary entry, so it leaves room, §643.
        /// </summary>
        public const string StartMakesRoom = "Start store again is the way out.";

        /// <summary>
        /// What can be done about a store a Create or a Recreate would leave full, where the page
        /// offers no start: nothing the page holds makes room. The tab as a whole is not
        /// answered for, since a write that takes the last of the room leaves a stopped store, and
        /// the page offers a start over that.
        /// </summary>
        public const string NothingMakesRoom = "Nothing on this page makes room.";

        private readonly IGateStore _store;
        private readonly AuthorityIssuer _issuer;
        private readonly IGateLog _log;

        public AuthorityService(IGateStore store, IGateLog log)
            : this(store, new AuthorityIssuer(), log)
        {
        }

        public AuthorityService(IGateStore store, AuthorityIssuer issuer, IGateLog log)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            _store = store;
            _issuer = issuer;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>What the database holds. The caller disposes what comes back.</summary>
        /// <remarks>
        /// **A store that cannot be read is damage, never absence**: an absent authority is
        /// created, and a store the plugin could not read may hold an authority that stands.
        /// </remarks>
        public AuthorityReading Read()
        {
            StoreReading stored;
            var reading = Read(out stored);
            if (stored != null)
            {
                stored.Dispose();
            }

            return reading;
        }

        /// <summary>
        /// What the database holds, with the store read for it, for a caller that asks more of
        /// that reading and lets it go; the reading is null where the store could not be read.
        /// </summary>
        internal AuthorityReading Read(out StoreReading stored)
        {
            try
            {
                stored = _store.Read();
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                stored = null;
                _log.Warn("the plugin's store could not be read: " + error.Message);
                return AuthorityReading.Damaged(new[] { AuthorityRecord.CouldNotReadProblem + error.Message }, 0);
            }

            return Read(stored);
        }

        /// <summary>
        /// Creates an authority, and refuses if any authority stands. A damaged authority is refused too:
        /// recreating is the answer to that, and it is a different button because it has
        /// different consequences. Where no store key stands, the authority starts a store under
        /// a new key, `STORE.md` §632.
        /// </summary>
        public AuthorityChange Create()
        {
            using (var reading = _store.Read())
            {
                var held = HeldBack(reading);
                if (held != null)
                {
                    return held;
                }

                using (var existing = Read(reading))
                {
                    if (existing.Presence == AuthorityPresence.Present)
                    {
                        return AuthorityChange.Refused("This database already has an authority. Use Recreate to replace it.");
                    }

                    if (existing.Presence == AuthorityPresence.Damaged)
                    {
                        return AuthorityChange.Refused(
                            "This database holds an authority that cannot be read. Recreate it to replace it.");
                    }

                    if (existing.Presence == AuthorityPresence.Undecided)
                    {
                        return AuthorityChange.Refused(
                            "This database holds more than one authority. Choose the authority to keep, or " +
                            "recreate to replace them all.");
                    }
                }

                return Issue("created", reading, Array.Empty<string>());
            }
        }

        /// <summary>
        /// Replaces whatever stands: a retirement of each authority that stands and a new
        /// authority under the same store key, in a single write, which deletes every admission
        /// those retirements refuse. The tab counts the clients in the question it asks before this
        /// runs.
        /// </summary>
        public AuthorityChange Recreate()
        {
            using (var reading = _store.Read())
            {
                var held = HeldBack(reading);
                if (held != null)
                {
                    return held;
                }

                return Issue("recreated", reading, Standing(reading));
            }
        }

        /// <summary>
        /// Retires every authority that stands, each retirement holding the store key, in a write
        /// that deletes every admission they refuse. The plugin serves nothing for this database
        /// afterwards, which is the point: this is how a database is taken out of service.
        /// </summary>
        public AuthorityChange Delete()
        {
            var waits = false;
            using (var reading = _store.Read())
            {
                var held = HeldBack(reading);
                if (held != null)
                {
                    return held;
                }

                var standing = Standing(reading);
                if (standing.Count > 0)
                {
                    var written = Write(Retirements(reading, "Delete", standing, null), out waits);
                    if (written != null)
                    {
                        _log.Warn("the authority could not be deleted: " + written);
                        return AuthorityChange.Refused("The authority could not be deleted: " + written);
                    }
                }
            }

            // A press on the tab only waits for the dialog's OK, which a Cancel drops, so nothing
            // is logged as done there.
            if (!waits)
            {
                _log.Info("authority deleted");
            }

            return AuthorityChange.Done();
        }

        /// <summary>
        /// Keeps one of several authorities and retires the rest, each retirement holding the
        /// store key, which is the operator's answer to a database that met another copy of
        /// itself holding an authority of its own, `STORE.md` §614.
        /// </summary>
        /// <remarks>
        /// **An answer is about the authorities it was given among**, and the write is refused
        /// once any other set stands, as <see cref="AuthorityChange.IsOvertaken"/>, for a caller
        /// that puts the question again: the database is as a synchronize left it.
        /// </remarks>
        /// <param name="fingerprint">The `id` of the authority to keep.</param>
        /// <param name="offered">The `id` of every authority the question listed.</param>
        public AuthorityChange Keep(string fingerprint, IEnumerable<string> offered)
        {
            if (offered == null)
            {
                throw new ArgumentNullException(nameof(offered));
            }

            List<string> others;
            var waits = false;
            using (var reading = _store.Read())
            {
                var held = HeldBack(reading);
                if (held != null)
                {
                    return held;
                }

                var listed = new HashSet<string>(offered, StringComparer.Ordinal);
                var standing = Standing(reading);
                if (!listed.SetEquals(standing))
                {
                    _log.Info("an authority was not kept, because the database no longer holds the " +
                              "authorities the question listed");
                    return AuthorityChange.Overtaken(
                        "This database no longer holds the authorities that were listed, so none of them was kept.");
                }

                // Among the authorities listed and whole, or a refusal the log names as an error:
                // the chooser offers nothing else, so an answer outside them is a defect upstream.
                var kept = reading.Answering.FirstOrDefault(record => record.Form.Member(StoreKinds.Id) == fingerprint);
                var refused = string.IsNullOrEmpty(fingerprint) || kept == null
                    ? "That authority is not among the authorities this database can keep."
                    : Unreadable(kept);
                if (refused != null)
                {
                    _log.Error("an authority could not be kept: " + refused, null);
                    return AuthorityChange.Refused("The authority could not be kept. " + refused);
                }

                // None to retire is the authority already standing alone, which is kept as it is.
                others = standing.Where(id => id != fingerprint).ToList();
                var written = others.Count == 0 ? null : Write(Retirements(reading, "keeping an authority", others, listed), out waits);
                if (written != null)
                {
                    if (written.Contains(Overtaken))
                    {
                        return AuthorityChange.Overtaken(
                            "This database no longer holds the authorities that were listed, so none of them was kept.");
                    }

                    _log.Error("an authority could not be kept: " + written, null);
                    return AuthorityChange.Refused("The authority could not be kept: " + written);
                }
            }

            // A choice on the tab only waits for the dialog's OK, which a Cancel drops, so nothing
            // is logged as done there.
            if (!waits)
            {
                _log.Info("authority " + CertificateDigest.Shorten(fingerprint) + " kept, others retired: " + others.Count);
            }

            return AuthorityChange.Done();
        }

        /// <summary>
        /// Seals a stopped store again, `STORE.md` §625: a version granting nothing for every
        /// object a lost version makes refuse, every entry that is damage deleted with a deletion
        /// record, and a summary entry over what passes and is in force, in a single write. The
        /// only command a stopped store takes, and refused on a store in any other state and on
        /// a store with no room, which the seal does not lift.
        /// </summary>
        /// <remarks>
        /// **Composed over the store as it stands at the write**, `SealActs`: on the KPG tab the
        /// act is composed again over the store at OK, so what the dialog showed decides nothing
        /// of what is written.
        /// </remarks>
        public AuthorityChange Seal()
        {
            var waits = false;
            using (var reading = _store.Read())
            {
                if (reading.State != StoreState.Stopped)
                {
                    return HeldBack(reading)
                           ?? AuthorityChange.Refused("The store of this database is not stopped, so there is nothing to seal again.");
                }

                string refused;
                var act = SealActs.Seal(reading, out refused);
                if (act == null)
                {
                    return AuthorityChange.Refused("The store of this database cannot be sealed again, since " + refused + ".");
                }

                // Asked of the store as the seal's write would leave it, before the act is written
                // or held: a seal whose own write stops the store again is refused with nothing
                // waiting for OK. The write plans the same act over the same store in the same
                // turn, so nothing is read back after it.
                var unsealed = SealActs.Unsealed(reading, act);
                if (unsealed != null)
                {
                    act.Forget();
                    _log.Warn("the store was not sealed again: " + unsealed);
                    return AuthorityChange.Refused("The store of this database cannot be sealed again, since " + unsealed + ".");
                }

                var written = Write(act, out waits);
                if (written != null)
                {
                    _log.Warn("the store could not be sealed again: " + written);
                    return AuthorityChange.Refused("The store could not be sealed again: " + written);
                }
            }

            // A press on the tab only waits for the dialog's OK, which a Cancel drops, so nothing
            // is logged as done there.
            if (!waits)
            {
                _log.Info("the store was sealed again");
            }

            return AuthorityChange.Done();
        }

        /// <summary>
        /// Starts the store again under a new key, `STORE.md` §632: under each key found a stop,
        /// a keep record naming the new key that no summary entry restates; then an authority
        /// holding the new key with its host certificate, and a keep record naming each key found
        /// and every keep record read with every stop in `after`; and the version record, which the
        /// write puts first under the new key, and a summary entry of height 1, in a single write
        /// that deletes nothing. Taken whatever the store's state,
        /// with room or without, wherever keys are found and judged; refused where no key is found,
        /// which Create starts, and past `StoreFormat.MaxKeys`, where no key can be named.
        /// </summary>
        /// <remarks>
        /// **Composed once, at the press**, as every command on the authority is, and held at the
        /// write to the keys found then: a key found that it names no keep record for leaves it
        /// out, `StoreWrite`, since that key would stand beside the new key.
        /// </remarks>
        public AuthorityChange StartAgain()
        {
            using (var reading = _store.Read())
            {
                if (reading.State == StoreState.None)
                {
                    return AuthorityChange.Refused("This database holds no store key. Create starts the store.");
                }

                if (!reading.Judged || reading.Found.Count == 0)
                {
                    return AuthorityChange.Refused(
                        "The store of this database cannot be started again, since no store key can be set aside: " + reading.Reason + ".");
                }

                return Issue(StartedAgain, reading, Array.Empty<string>());
            }
        }

        /// <summary>
        /// How many clients a reading of the store admits, as the clients page counts them and
        /// the question Start store again asks counts them, `ClientReaches.Admitted`, so that the
        /// audit of a store started again counts what the operator was told the start costs; none
        /// where nothing is in force.
        /// </summary>
        private static int ClientsAdmitted(StoreReading reading)
        {
            return Ui.ClientReaches.Admitted(reading, DateTimeOffset.UtcNow);
        }

        /// <summary>The verb of a store started again, as the log and a refusal name it.</summary>
        private const string StartedAgain = "started again";

        /// <summary>
        /// The records of a store started again under this new key, `STORE.md` §632: the stop under
        /// each key found, the authority, and a keep record naming each key found, in that order.
        /// </summary>
        private static List<StoreRecord> Started(StoreReading reading, byte[] key, StoreRecord authority)
        {
            var name = StoreFormat.KeyName(key);
            var stops = reading.Found
                .Select(found => StoreRecord.Compose(found.Bytes, StoreKinds.Kept, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { StoreKinds.Key, name },
                    { StoreKinds.After, string.Empty },
                }))
                .ToList();
            var after = string.Join(" ", reading.KeptRead.Concat(stops.Select(stop => stop.Hex))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal));
            var records = new List<StoreRecord>(stops) { authority };
            records.AddRange(reading.Found.Select(found => StoreRecord.Compose(key, StoreKinds.Kept, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StoreKinds.Key, found.Name },
                { StoreKinds.After, after },
            })));
            return records;
        }

        /// <summary>Why an authority standing cannot be kept, or null where it reads whole.</summary>
        private static string Unreadable(FoundRecord record)
        {
            using (var material = StoreAuthorities.Material(record, new List<string>()))
            {
                return material == null ? "That authority cannot be read, so it cannot be kept." : null;
            }
        }

        /// <summary>What a ground that went answers, for a caller that tells a choice overtaken apart.</summary>
        private const string Overtaken = "the authorities it was asked about no longer stand as they did";

        /// <summary>What a store reading made already holds, with its admissions counted.</summary>
        internal static AuthorityReading Read(StoreReading reading)
        {
            var listing = StoreAdmissions.Listing(reading);
            return StoreAuthorities.Read(reading, listing.Records.Count + listing.Unreadable.Count);
        }

        /// <summary>
        /// Refuses a command while the store holds the gate back, with the word and why, or null
        /// where a command may write: a store that is whole, and a database with no store key.
        /// </summary>
        private static AuthorityChange HeldBack(StoreReading reading)
        {
            var word = StoreWords.Of(reading.State);
            return word == null
                ? null
                : AuthorityChange.Refused(
                    "The store of this database is " + word + ", so nothing is written into it: " + reading.Reason + ".");
        }

        /// <summary>The `id` of every authority standing, a damaged authority among them.</summary>
        private static IReadOnlyList<string> Standing(StoreReading reading)
        {
            return reading.Answering.Select(record => record.Form.Member(StoreKinds.Id)).ToList();
        }

        /// <summary>
        /// An act retiring these authorities, each retirement holding the store key, held to the
        /// authorities that stood when it was composed, or to those listed.
        /// </summary>
        private static StoreAct Retirements(
            StoreReading reading, string name, IReadOnlyList<string> retired, ISet<string> listed)
        {
            var key = (byte[])reading.InForce.Key.Bytes.Clone();
            try
            {
                var stood = listed ?? new HashSet<string>(Standing(reading), StringComparer.Ordinal);
                var records = retired.Select(id => Retirement(key, id, true)).ToList();
                return new StoreAct(
                    name, key, reading.InForce.Key.Name, records, ground => ground.Standing.SetEquals(stood) ? null : Overtaken);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private static StoreRecord Retirement(byte[] key, string authority, bool holdingKey)
        {
            var members = new Dictionary<string, string>(StringComparer.Ordinal) { { StoreKinds.AuthorityMember, authority } };
            if (holdingKey)
            {
                members[StoreKinds.StoreKey] = Hex.Of(key);
            }

            return StoreRecord.Compose(key, StoreKinds.Retired, members);
        }

        /// <summary>Writes an act, answering why it was not written, or null where it was.</summary>
        /// <remarks>
        /// **The act's copy of the store key is overwritten here** once the store is done with it,
        /// written, refused or thrown, `CA.md` §208, unless the store holds the act for a dialog's
        /// OK: that buffer overwrites it when it lets the act go.
        /// </remarks>
        /// <param name="waits">Whether the store holds the act for a dialog's OK.</param>
        private string Write(StoreAct act, out bool waits)
        {
            waits = false;
            try
            {
                var written = _store.Write(new[] { act });
                waits = written.Taken && written.Waits;
                return written.Taken ? null : written.Reason;
            }
            finally
            {
                if (!waits)
                {
                    act.Forget();
                }
            }
        }

        /// <summary>
        /// Mints an authority and its host certificate and writes it with a retirement of each
        /// authority it replaces, which is what creating and recreating have in common.
        /// </summary>
        private AuthorityChange Issue(string verb, StoreReading reading, IReadOnlyList<string> replacing)
        {
            byte[] sniKey = null;
            byte[] key = null;
            string minted;
            var waits = false;

            try
            {
                // Asked at the press, before anything is minted, written or held: a write that
                // would leave the store held back is refused with nothing waiting for OK, and the
                // page offers no such press. Inside the catch below, since it draws random bytes.
                // A start is not asked: it writes under a new key, whatever the store it sets aside.
                var issueRefusal = verb == StartedAgain ? null : IssueRefusal(reading);
                if (issueRefusal != null)
                {
                    _log.Warn("the authority was not " + verb + ": " + issueRefusal);
                    return AuthorityChange.Refused(
                        "The authority could not be " + verb + ": " + issueRefusal + "."
                        + (issueRefusal == Full ? " " + (StartOffered(verb, reading) ? StartMakesRoom : NothingMakesRoom) : string.Empty));
                }

                var authority = _issuer.CreateAuthority();
                var host = _issuer.IssueHost(authority);
                sniKey = NewRandom(AuthorityMaterial.SniKeyLength);

                // Under the store key that stands, or a new key where none does or the store is
                // started again, `STORE.md` §632: a Recreate carries the key, and a Create after a
                // Delete keeps it.
                var again = verb == StartedAgain;
                key = again || reading.State == StoreState.None ? StoreFormat.NewKey() : (byte[])reading.InForce.Key.Bytes.Clone();

                using (var material = new AuthorityMaterial(authority, host, sniKey))
                {
                    minted = material.AuthorityDigest;
                    var issued = StoreRecord.Compose(key, StoreKinds.Authority, AuthorityRecord.Members(material, key));
                    StoreAct act;
                    if (again)
                    {
                        act = new StoreAct("Start again", key, null, Started(reading, key, issued), null, starts: true, admitted: ClientsAdmitted);
                    }
                    else
                    {
                        var records = replacing.Select(id => Retirement(key, id, false)).ToList();
                        records.Add(issued);
                        var stood = new HashSet<string>(Standing(reading), StringComparer.Ordinal);
                        act = new StoreAct(
                            verb == "created" ? "Create" : "Recreate",
                            key,
                            reading.State == StoreState.None ? null : reading.InForce.Key.Name,
                            records,
                            ground => ground.Standing.SetEquals(stood) ? null : Overtaken);
                    }

                    var written = Write(act, out waits);
                    if (written != null)
                    {
                        _log.Warn(Subject(verb) + " could not be " + verb + ": " + written);
                        return AuthorityChange.Refused(Named(verb) + " could not be " + verb + ": " + written);
                    }
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                _log.Error(Subject(verb) + " could not be " + verb, error);
                return AuthorityChange.Refused(Named(verb) + " could not be " + verb + ": " + error.Message);
            }
            finally
            {
                if (sniKey != null)
                {
                    Array.Clear(sniKey, 0, sniKey.Length);
                }

                if (key != null)
                {
                    Array.Clear(key, 0, key.Length);
                }
            }

            // Logged as done only where it is done, as a Delete is.
            if (!waits)
            {
                _log.Info((verb == StartedAgain ? "store started again, authority" : "authority " + verb) + ", digest " + minted);
            }

            return AuthorityChange.Done();
        }

        /// <summary>
        /// Why a Create or a Recreate over this reading would not be taken, or null where its write
        /// would be taken and leave the store whole, `STORE.md` §614 and §643: the records the
        /// write holds, planned over the reading and applied in memory, and the store they leave
        /// judged. Asked by the press before anything is minted, written or held, and by the page
        /// that offers the press; a Create is asked only where no authority stands, so the
        /// retirements are those of a Recreate.
        /// </summary>
        /// <remarks>
        /// **Asked of the store as the page reads it.** On the KPG tab that is the store as OK
        /// would leave it with the presses held, and the press is planned as a write after them,
        /// as a press on the next dialog is: OK writes every press in a single write, which names
        /// a summary entry fewer, so a press refused here would have left room for a single write
        /// more at the most, and the page counts the room of a write after OK as well. **Asked at
        /// the press and not at OK**: a write landing under the dialog, as a pairing, can still
        /// leave the store full at OK, and OK writes the press all the same.
        ///
        /// **An authority stands in for the authority not yet minted**: a record of kind
        /// `authority` holding the store key and an `id` of the same length. No item restates an
        /// authority and nothing a summary entry holds is drawn from its content, so the store a
        /// minted authority's write leaves differs only in that record's own strings, far below
        /// the plugin's ceiling for a string.
        /// </remarks>
        internal static string IssueRefusal(StoreReading reading)
        {
            if (reading == null)
            {
                throw new ArgumentNullException(nameof(reading));
            }

            var starts = reading.State == StoreState.None;
            var key = starts ? StoreFormat.NewKey() : (byte[])reading.InForce.Key.Bytes.Clone();
            StoreAct act = null;
            try
            {
                var records = Standing(reading).Select(id => Retirement(key, id, false)).ToList();
                records.Add(StoreRecord.Compose(key, StoreKinds.Authority, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    // Random, so that no retirement a file holds names it: as long as a digest.
                    { StoreKinds.Id, Hex.Of(NewRandom(StandInLength)) },
                    { StoreKinds.StoreKey, Hex.Of(key) },
                }));
                act = new StoreAct("Create", key, starts ? null : reading.InForce.Key.Name, records, null);

                var write = StoreWrite.Plan(reading, new[] { act }, GateVersion.Current);
                if (write.Refused != null)
                {
                    return write.NoRoom ? Full : write.Refused;
                }

                var applied = write.Applied(reading.Candidates, reading.Deletions);
                using (var after = StoreReading.Afresh(applied.Key, applied.Value))
                {
                    if (after.State == StoreState.Whole)
                    {
                        return null;
                    }

                    return after.Room != null && !after.Room.HasRoom ? Full : after.Reason;
                }
            }
            finally
            {
                if (act != null)
                {
                    act.Forget();
                }

                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>
        /// Whether the Certificate Authority page offers Start store again beside the press of this
        /// verb over this reading, as `AuthorityDescription.Under` decides it, `STORE.md` §632:
        /// beside a Recreate where a single authority stands or is damaged and a key is found and
        /// judged, and never beside a Create, which holds the place a start takes, nor beside
        /// the chooser, which takes it where several authorities stand.
        /// </summary>
        /// <remarks>
        /// A start already waiting for the dialog's OK is not asked about: the page offers no
        /// second start then, and the store it reads is the store the start leaves, a summary
        /// entry of height 1, which no single Create or Recreate leaves full.
        /// </remarks>
        private static bool StartOffered(string verb, StoreReading reading)
        {
            return verb == "recreated" && reading.Judged && reading.Found.Count > 0 && Standing(reading).Count <= 1;
        }

        /// <summary>What a command that mints an authority writes, as the log names it: the store where it is started again.</summary>
        private static string Subject(string verb)
        {
            return verb == StartedAgain ? "the store" : "the authority";
        }

        /// <summary><see cref="Subject"/> at the start of a sentence.</summary>
        private static string Named(string verb)
        {
            return verb == StartedAgain ? "The store" : "The authority";
        }

        /// <summary>The bytes of the `id` that stands in for an authority not yet minted: a SHA-256 digest's.</summary>
        private const int StandInLength = 32;

        private static byte[] NewRandom(int length)
        {
            var bytes = new byte[length];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return bytes;
        }
    }
}
