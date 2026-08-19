using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The profiles page's answers, read from and written to one open database.
    /// </summary>
    /// <remarks>
    /// Two stores rather than one, and they are not the same kind of thing. The list of names is a
    /// single value in the database's own custom data. The rules are one document per node, all
    /// over the tree. A profile is added and removed by touching only the first, because a client
    /// holds only the names its record carries that the list still names: taking a name off the
    /// list is what makes every rule for it grant nothing, and putting it back is what makes them
    /// grant again. The rules are read here and never written.
    ///
    /// Nothing here saves. It marks the database as changed, exactly as the rest of the tab does.
    /// </remarks>
    public sealed class DatabaseProfiles : IProfiles
    {
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
                foreach (var node in _database.Nodes.All())
                {
                    var stored = node == null ? null : node.Read();
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
        /// Counted rather than remembered, for the reason the rules are: a pairing writes a record
        /// from somewhere else entirely while this dialog is open, so a number kept here would be
        /// wrong about the client the operator approved a moment ago.
        ///
        /// **The records are what is counted, not the certificates.** What a client may reach is
        /// read from its record rather than from its certificate everywhere else in this plugin,
        /// and a count that disagreed with that would be a count of something else.
        ///
        /// **Every profile from one reading of the records**, for the reason the rules are counted
        /// in one walk: each reading parses every record's certificate, and the page asks for a
        /// count on every row.
        /// </remarks>
        public IReadOnlyDictionary<string, int> Clients()
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);

            if (_database == null || !_database.IsOpen || _database.PluginCustomData == null)
            {
                return found;
            }

            try
            {
                // A listing that could not be made counts nobody, which shows this profile as
                // held by no client rather than claiming it is held by one. The tab's own line
                // is where a failed read is reported.
                foreach (var record in new AdmissionStore(_database.PluginCustomData, _log).All().Records)
                {
                    if (record == null || record.Profiles == null)
                    {
                        continue;
                    }

                    // A set, because a record naming a profile twice is still one client.
                    foreach (var held in new HashSet<string>(record.Profiles, StringComparer.Ordinal))
                    {
                        if (string.IsNullOrEmpty(held))
                        {
                            continue;
                        }

                        int counted;
                        found.TryGetValue(held, out counted);
                        found[held] = counted + 1;
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the clients could not be counted: " + error.Message);
            }

            return found;
        }

        public string Add(string name)
        {
            if (_database == null || !_database.IsOpen)
            {
                return "No database is open on this dialog.";
            }

            var names = Read();
            if (names.Holds(name))
            {
                return "That profile is already named here.";
            }

            var refused = names.Add(name);
            if (refused != null)
            {
                return refused;
            }

            return Store(names);
        }

        /// <summary>
        /// What adding this name would put back in force, or null when nothing would.
        /// </summary>
        /// <remarks>
        /// Only a name the list does not hold can come back, and it brings back whatever still
        /// names it: a removal leaves the rules where they were and the records as they were
        /// approved, so every client holding the name regains it. Counted now rather than
        /// remembered from the removal, because a rule can be written by hand, or a record or a
        /// rule arrive in a merge, long after the name went.
        /// </remarks>
        public string Returning(string name)
        {
            if (_database == null || !_database.IsOpen || string.IsNullOrEmpty(name) || Read().Holds(name))
            {
                return null;
            }

            var rules = Counted(Rules(), name);
            var holders = Holders(name);
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
        /// The admitted clients whose records hold a name, as the Clients page calls them.
        /// </summary>
        /// <remarks>
        /// A listing that cannot be made names nobody, as the count does; the tab's own line is
        /// where a failed read is reported, and a warning that could not name the clients still
        /// says what comes back of the rules.
        /// </remarks>
        private List<string> Holders(string name)
        {
            var found = new List<string>();

            if (_database.PluginCustomData == null)
            {
                return found;
            }

            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var record in new AdmissionStore(_database.PluginCustomData, _log).All().Records)
                {
                    // Not one whose certificate has expired, which regains nothing.
                    if (record != null && record.Profiles != null
                        && record.Profiles.Contains(name, StringComparer.Ordinal)
                        && !record.HasExpired(now))
                    {
                        found.Add(ClientList.Describe(record));
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the clients holding " + name + " could not be listed: " + error.Message);
            }

            found.Sort(StringComparer.Ordinal);
            return found;
        }

        public string Remove(string name)
        {
            _lastRemoval = null;

            if (_database == null || !_database.IsOpen)
            {
                return "No database is open on this dialog.";
            }

            var names = Read();
            if (!names.Remove(name))
            {
                return "That profile is not named here.";
            }

            var refused = Store(names);
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
        /// The rules are said to stay because they do, and so are the clients, and both are news:
        /// an operator who removed a name and later adds it back gets every one of those rules in
        /// force again, for every one of those clients. Counted from what could be read, like the
        /// columns, which are the only counts there are.
        ///
        /// Internal rather than private so that `GateTabFitsTests` can measure the longest one
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

        private ProfileNames Read()
        {
            try
            {
                return ProfileNames.Read(_database.PluginCustomData.Get(ProfileNames.CustomDataKey));
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("the profiles could not be read: " + error.Message);
                return ProfileNames.Empty();
            }
        }

        private string Store(ProfileNames names)
        {
            try
            {
                var document = names.Write();

                if (document == null)
                {
                    _database.PluginCustomData.Remove(ProfileNames.CustomDataKey);
                }
                else
                {
                    _database.PluginCustomData.Set(ProfileNames.CustomDataKey, document);
                }

                _database.MarkModified();
                return null;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("the profiles could not be written", error);
                return "That could not be written: " + error.Message;
            }
        }
    }
}
