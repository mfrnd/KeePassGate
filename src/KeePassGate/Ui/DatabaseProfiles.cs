using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Store;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The profiles page's answers, read from and written to one open database.
    /// </summary>
    /// <remarks>
    /// Two stores rather than a single store, and they are not the same kind of thing. The list
    /// of names is the plugin's store's, `STORE.md` §616: an addition and a removal are records
    /// of it. The rules are a document for each node, all over the tree. A profile is added and
    /// removed by touching only the first, because a client holds only the names its admission
    /// carries that the list still names: taking a name off the list is what makes every rule for
    /// it grant nothing, and putting it back is what makes them grant again. The rules are read
    /// here and never written.
    ///
    /// **A press is an act handed to the database's store**, as the clients page hands a
    /// revocation: the KPG tab's buffer holds it until the dialog's OK, and the page reads the
    /// store with every act held planned over it. Nothing here saves.
    /// </remarks>
    public sealed class DatabaseProfiles : IProfiles
    {
        /// <summary>
        /// What an Add answers in a database whose store has no key: only an authority starts a
        /// store, `STORE.md` §627.
        /// </summary>
        public const string NoAuthorityRefusal =
            "This database has no certificate authority yet. Create it on the Certificate Authority page first.";

        private readonly IGateDatabase _database;
        private readonly IGateLog _log;

        private string _lastRemoval;

        public DatabaseProfiles(IGateDatabase database, IGateLog log)
        {
            _database = database;
            _log = log ?? NullGateLog.Instance;
        }

        public string LastRemoval
        {
            get { return _lastRemoval; }
        }

        /// <summary>
        /// Whether OK would leave out the Add of this name held on the dialog because a removal of
        /// the name that the press did not see stands in the database, `STORE.md` §627: found as
        /// the dialog's OK finds it, by reading alone. False over a database holding nothing for
        /// OK, and where the store could not be read.
        /// </summary>
        public bool Overtaken(string name)
        {
            var pending = _database as PendingDatabase;
            if (pending == null)
            {
                return false;
            }

            try
            {
                return pending.OvertakenNow().Contains(name, StringComparer.Ordinal);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the profiles held for OK could not be read against the store: " + error.Message);
                return false;
            }
        }

        public IReadOnlyList<string> Names()
        {
            return Read().Names;
        }

        /// <summary>
        /// How many groups and entries have a rule for each profile, whatever the rule grants: an
        /// empty one, which grants nothing from its node down, is a rule too. Counted rather than
        /// remembered, because the rules are edited on other dialogs entirely and a number kept here
        /// would be stale the moment somebody pressed OK on one of them.
        /// </summary>
        /// <remarks>
        /// **Every profile in one walk**, because the walk is the cost: every group and every
        /// entry. Asked one profile at a time, a page of thirty two profiles over twenty thousand
        /// entries, when the walk still took in each entry's history, walked two hundred thousand
        /// nodes thirty two times on KeePass's own thread, which held the settings dialog shut for
        /// twelve seconds before it was drawn.
        /// </remarks>
        public IReadOnlyDictionary<string, int> Rules()
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);

            if (_database == null || !_database.IsOpen || _database.Nodes == null)
            {
                return found;
            }

            try
            {
                foreach (var stored in _database.Nodes.All().Select(
                    node => node == null ? null : node.Read()))
                {
                    if (string.IsNullOrEmpty(stored))
                    {
                        continue;
                    }

                    var document = AclDocument.Read(stored);
                    if (document == null)
                    {
                        continue;
                    }

                    foreach (var name in document.Profiles)
                    {
                        int counted;
                        found.TryGetValue(name, out counted);
                        found[name] = counted + 1;
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the rules could not be counted: " + error.Message);
            }

            return found;
        }

        /// <summary>
        /// How many admitted clients hold each profile.
        /// </summary>
        /// <remarks>
        /// Counted rather than remembered, for the reason the rules are: a pairing writes an
        /// admission from somewhere else entirely while this dialog is open, so a number kept here
        /// would be wrong about the client the operator approved a moment ago.
        ///
        /// **The admissions are what is read, not the certificates.** What a client may reach is
        /// read from its admission rather than from its certificate everywhere else in this
        /// plugin, and a count that disagreed with that would be a count of something else. **And
        /// each client is counted once**, by the client key all its admissions carry:
        /// a client that renewed holds two admissions until it gives the older up, and is one
        /// client holding the profile if either of them does, while two programs that paired under
        /// one name are two clients.
        ///
        /// **Every profile from one reading of the admissions**, for the reason the rules are
        /// counted in one walk: each reading parses every admission's certificate, and the page
        /// asks for a count on every row.
        ///
        /// **Only clients that get in**, which <see cref="GettingIn()"/> gives the reason for.
        /// </remarks>
        public IReadOnlyDictionary<string, int> Clients()
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);

            if (_database == null || !_database.IsOpen)
            {
                return found;
            }

            try
            {
                // A listing that could not be made counts nobody, which shows this profile as
                // held by no client rather than claiming it is held by a client. The tab's own
                // line is where a failed read is reported.
                var holders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                foreach (var record in GettingIn())
                {
                    if (record.Profiles == null)
                    {
                        continue;
                    }

                    foreach (var held in record.Profiles)
                    {
                        if (string.IsNullOrEmpty(held))
                        {
                            continue;
                        }

                        HashSet<string> clients;
                        if (!holders.TryGetValue(held, out clients))
                        {
                            clients = new HashSet<string>(StringComparer.Ordinal);
                            holders[held] = clients;
                        }

                        // A set of client keys, because an admission naming a profile twice is
                        // still a single client, and so is a client holding it through two.
                        clients.Add(record.ClientKey);
                    }
                }

                foreach (var pair in holders)
                {
                    found[pair.Key] = pair.Value.Count;
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the clients could not be counted: " + error.Message);
            }

            return found;
        }

        /// <summary>
        /// The admissions whose clients get in now, judged by `ClientReaches` against the authority
        /// this dialog would serve, as the clients page judges its rows.
        /// </summary>
        /// <remarks>
        /// An admission the gate refuses, of another authority, not for client use, not yet valid
        /// or expired, holds profiles it cannot use, and is not counted as a client holding them.
        /// </remarks>
        private List<AdmissionRecord> GettingIn()
        {
            StoreReading stored;
            using (var reading = new AuthorityService(_database.Store, _log).Read(out stored))
            using (stored)
            {
                return GettingIn(reading, stored);
            }
        }

        /// <summary>
        /// The same, from a reading of the store made already and the authority it holds.
        /// </summary>
        private static List<AdmissionRecord> GettingIn(AuthorityReading reading, StoreReading stored)
        {
            var authority = reading.IsPresent ? reading.Material.Authority : null;
            var now = DateTimeOffset.UtcNow;
            var listing = StoreAdmissions.Listing(stored);

            return listing.Records
                .Where(record => listing.StandingOf(record) == AdmissionStanding.Admitted
                                 && ClientReaches.GetsIn(record, authority, now))
                .ToList();
        }

        /// <summary>
        /// Adds a name, as an act the KPG tab's buffer holds until the dialog's OK, or answers why
        /// not: a text no claim could carry, a name the list already holds, a database whose store
        /// has no key, or a write the store refuses.
        /// </summary>
        /// <remarks>
        /// **The addition names the removals of the name that the page's reading held at the
        /// press**, a removal that only a deletion record tells of among them, `STORE.md` §616
        /// and §627, and that is what OK writes: a removal that reaches the database after the
        /// press is not overruled by it. The list keeps no ceiling.
        /// </remarks>
        public string Add(string name)
        {
            if (_database == null || !_database.IsOpen)
            {
                return "No database is open on this dialog.";
            }

            var refused = ProfileNames.Refusal(name);
            if (refused != null)
            {
                return refused;
            }

            return Press(
                names => names.Holds(name) ? "That profile is already named here." : null,
                reading => ProfileActs.Add(reading, name));
        }

        /// <summary>
        /// What adding this name would put back in force, or null when nothing would.
        /// </summary>
        /// <remarks>
        /// Only a name the list does not hold can come back, and it brings back whatever still
        /// names it: a removal leaves the rules where they were and the admissions as they were
        /// approved, so every client holding the name regains it. Counted now rather than
        /// remembered from the removal, because a rule can be written by hand, or an admission or
        /// a rule arrive in a merge, long after the name went.
        ///
        /// **The list and the clients holding the name from a single reading of the store.** Each
        /// client is named once, by its client key, so a client that renewed and still holds the
        /// admission it renewed from is named once, and two that share a name are named apart. It
        /// regains the name through whichever of its certificates holds it, which while it holds
        /// two can be the older alone. A listing that cannot be made names nobody, as the count
        /// does; the tab's own line is where a failed read is reported, and a warning that could
        /// not name the clients still states what comes back of the rules.
        /// </remarks>
        public string Returning(string name)
        {
            if (_database == null || !_database.IsOpen || string.IsNullOrEmpty(name))
            {
                return null;
            }

            var found = new List<AdmissionRecord>();
            StoreReading stored;
            using (var reading = new AuthorityService(_database.Store, _log).Read(out stored))
            using (stored)
            {
                if (ProfileNames.Of(stored).Holds(name))
                {
                    return null;
                }

                try
                {
                    // Only an admission that gets in: an expired admission regains nothing and
                    // goes with the plugin's next write, and an admission of another authority,
                    // not for client use or not yet valid regains nothing either.
                    found.AddRange(GettingIn(reading, stored).Where(record => record.Profiles != null
                                                                            && record.Profiles.Contains(name, StringComparer.Ordinal)));
                }
                catch (Exception error) when (Expected.OfCustomData(error))
                {
                    _log.Warn("the clients holding " + name + " could not be listed: " + error.Message);
                }
            }

            var rules = Counted(Rules(), name);
            var holders = ClientList.ClientNames(found);
            if (rules == 0 && holders.Count == 0)
            {
                return null;
            }

            var said = new List<string>();

            // Every client by name, because which clients regain a name is the whole of what the
            // operator is being asked to agree to, and a count would leave them to guess who.
            if (holders.Count == 1)
            {
                said.Add("One client still holds " + name + " from before and will regain it: "
                    + holders[0] + ".");
            }
            else if (holders.Count > 1)
            {
                said.Add(holders.Count + " clients still hold " + name + " from before and will regain it: "
                    + string.Join(", ", holders) + ".");
            }

            if (rules == 1)
            {
                said.Add("Adding " + name + " again puts the rule naming it back in force.");
            }
            else if (rules > 1)
            {
                said.Add("Adding " + name + " again puts the " + rules + " rules naming it back in force.");
            }

            return string.Join(" ", said);
        }

        /// <summary>
        /// Removes a name, as an act the KPG tab's buffer holds until the dialog's OK, or answers
        /// why not: a name the list does not hold, or a write the store refuses.
        /// </summary>
        public string Remove(string name)
        {
            _lastRemoval = null;

            if (_database == null || !_database.IsOpen)
            {
                return "No database is open on this dialog.";
            }

            var refused = Press(
                names => names.Holds(name) ? null : "That profile is not named here.",
                reading => ProfileActs.Remove(reading, name));
            if (refused != null)
            {
                return refused;
            }

            _lastRemoval = Said(name, Counted(Rules(), name), Counted(Clients(), name));
            return null;
        }

        /// <summary>
        /// What a removal did, in the terms the operator cares about: what the name does now, and
        /// what is left holding it.
        /// </summary>
        /// <remarks>
        /// The rules are stated to stay because they do, and so are the clients, and both are
        /// news: an operator who removed a name and later adds it back gets every rule in force
        /// again, for every client whose admission still holds it then. A client that renewed in
        /// between was written without the name, since a renewal carries over only the names the
        /// list holds. Counted from what could be read, like the columns, which are the only
        /// counts there are.
        ///
        /// Internal rather than private so that `GateTabFitsTests` can measure the longest sentence
        /// against the page it is shown on.
        /// </remarks>
        internal static string Said(string name, int rules, int clients)
        {
            var said = "The profile " + name + " is gone, so it grants nothing.";

            if (rules == 1)
            {
                said += " The rule naming it stays, inert.";
            }
            else if (rules > 1)
            {
                said += " The " + rules + " rules naming it stay, inert.";
            }

            if (clients == 1)
            {
                said += " One client still holds it.";
            }
            else if (clients > 1)
            {
                said += " " + clients + " clients still hold it.";
            }

            return said;
        }

        /// <summary>One profile's count, which is none when the count does not name it.</summary>
        private static int Counted(IReadOnlyDictionary<string, int> counts, string name)
        {
            int found;
            return counts != null && counts.TryGetValue(name, out found) ? found : 0;
        }

        /// <summary>
        /// The names on the list as the page shows them: the store read through the tab's buffer,
        /// with every act held planned over it.
        /// </summary>
        private ProfileNames Read()
        {
            try
            {
                using (var reading = _database.Store.Read())
                {
                    return ProfileNames.Of(reading);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the profiles could not be read: " + error.Message);
                return ProfileNames.Of(null);
            }
        }

        /// <summary>
        /// Composes an act about a name over the store as the page shows it and hands it to the
        /// store, answering why it was not taken, or null where it was.
        /// </summary>
        /// <param name="refusal">Why the press is refused against the list the page shows, or null.</param>
        /// <param name="compose">The act, composed over that reading, or null where no store key stands.</param>
        private string Press(Func<ProfileNames, string> refusal, Func<StoreReading, StoreAct> compose)
        {
            StoreAct act;
            try
            {
                using (var reading = _database.Store.Read())
                {
                    var refused = refusal(ProfileNames.Of(reading));
                    if (refused != null)
                    {
                        return refused;
                    }

                    act = compose(reading);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the profiles could not be read", error);
                return "That could not be written: " + Clipped.Message(error.Message);
            }

            if (act == null)
            {
                return NoAuthorityRefusal;
            }

            StoreWritten written = null;
            try
            {
                written = _database.Store.Write(new[] { act });
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                // Cut, because the message is somebody else's and lands on a page that does not
                // scroll, under the page's own sentence, which is measured whole.
                _log.Error("the profiles could not be written", error);
                return "That could not be written: " + Clipped.Message(error.Message);
            }
            finally
            {
                if (written == null || !written.Taken || !written.Waits)
                {
                    act.Forget();
                }
            }

            return written.Taken ? null : "That could not be written: " + Clipped.Message(written.Reason);
        }
    }
}
