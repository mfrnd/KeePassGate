using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Store;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A database that is holding changes until the dialog they were made on is accepted.
    /// </summary>
    /// <remarks>
    /// Asked by the one screen that has to care. Everything else on the tab reads and writes as
    /// though the database were the database, which is the point of the decorator; the clients page
    /// is different because what it invites somebody into is answered by the gate, out of the live
    /// file, and the gate cannot see anything that is still waiting here.
    /// </remarks>
    public interface IPendingChanges
    {
        /// <summary>Whether anything at all is waiting for the dialog's OK.</summary>
        bool Waiting { get; }

        /// <summary>Whether an act about a profile name is waiting: a name added or removed.</summary>
        bool ProfilesWaiting { get; }

        /// <summary>
        /// Whether an act of the authority's is waiting on the plugin's store: an authority made,
        /// replaced or deleted. A client revoked is not such an act, since it leaves the authority
        /// a pairing is answered by as it is.
        /// </summary>
        bool AuthorityWaiting { get; }
    }

    /// <summary>
    /// A database whose changes are held until the dialog they were made on is accepted.
    /// </summary>
    /// <remarks>
    /// **This reverses a decision, and the reason is what an operator expects a dialog to mean.**
    /// Every button on the KPG tab used to write at once and mark the database as having
    /// something to save. That was safe, because KeePass's database settings dialog keeps no copy
    /// of anything this plugin writes and therefore never undid any of it, and it was wrong anyway: a
    /// dialog with OK and Cancel on it says that nothing has happened yet, and a tab inside one that
    /// has already happened is a tab lying about the buttons around it. Cancel now means Cancel.
    ///
    /// **A decorator rather than a change to the pages**, so that nothing above this knows. Each
    /// page reads and writes exactly as it did, through the same seams; what it is holding is a
    /// buffer, and the dialog's result decides whether it is applied or dropped. Nothing on the tab
    /// writes to a group or an entry, so the rules on nodes are read straight through.
    ///
    /// **Everything the tab writes is the plugin's store's**, `STORE.md` §615 to §618: the
    /// authority, the admissions, the revocations, the profile names and the lifts of a standing
    /// answer to a renewal, written by the acts the buffer holds. Each act is composed again at OK
    /// against the store as it stands, so a pairing that landed while the dialog was open, a name
    /// it added and an answer the renewal dialog wrote under the dialog are each kept, and a
    /// Recreate, a Delete and a revocation delete in their own write every admission they refuse,
    /// `STORE.md` §627. A lift names what the page showed at its press and nothing else, so a
    /// refusal that arrived under the dialog stands at OK.
    /// </remarks>
    public sealed class PendingDatabase : IGateDatabase, IPendingChanges
    {
        private readonly IGateDatabase _database;
        private readonly PendingRecords _records;
        private readonly IGateLog _log;

        public PendingDatabase(IGateDatabase database, IGateLog log)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            _database = database;
            _log = log ?? NullGateLog.Instance;

            // The store is asked for on every access rather than taken once here. A database that
            // is locked and opened again while this dialog is up comes back with a different root
            // group behind the same handle, and a store captured in a constructor would go on
            // reading and writing the group that was replaced. `OpenDatabaseTargets` rebuilds its
            // own per read for the same reason.
            _records = new PendingRecords(() => database.Store);
        }

        public bool IsOpen
        {
            get { return _database.IsOpen; }
        }

        public string Name
        {
            get { return _database.Name; }
        }

        /// <summary>The plugin's store with the acts pressed on the dialog beside it.</summary>
        public IGateStore Store
        {
            get { return _records; }
        }

        /// <summary>The live tree, read through: nothing on the tab writes to it.</summary>
        public IRuledNodes Nodes
        {
            get { return _database.Nodes; }
        }

        /// <summary>Whether anything is waiting for the dialog's OK.</summary>
        public bool Waiting
        {
            get { return _records.Waiting; }
        }

        /// <summary>Whether an act about a profile name is waiting on the plugin's store.</summary>
        public bool ProfilesWaiting
        {
            get { return _records.Acts.Any(act => ProfileActs.About(act) != null); }
        }


        /// <summary>
        /// Whether an act of the authority's is waiting on the plugin's store: an act holding an
        /// authority or a retirement among its records.
        /// </summary>
        public bool AuthorityWaiting
        {
            get
            {
                return _records.Acts.Any(act => act.Records.Any(record =>
                    record.Kind == StoreKinds.Authority || record.Kind == StoreKinds.Retired));
            }
        }

        /// <summary>Whether an act of this name is waiting on the plugin's store.</summary>
        public bool ActWaiting(string name)
        {
            return _records.Acts.Any(act => string.Equals(act.Name, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// The acts the last <see cref="Apply"/> wrote into the plugin's store, in the order they
        /// were pressed; none where it wrote nothing: what the audit of the dialog's OK is
        /// written from.
        /// </summary>
        public IReadOnlyList<StoreAct> Landed { get; private set; } = Array.Empty<StoreAct>();

        /// <summary>
        /// The names of the Adds held that OK would leave out now, because a removal of the name
        /// that the press did not see stands in the database, `STORE.md` §627, in the order
        /// pressed; none where the database is gone. Found as <see cref="Apply"/> finds them, by
        /// the acts composed against the store as it stands, and by reading alone.
        /// </summary>
        /// <remarks>
        /// What the Profiles page's line names as why a name the last press added does not show,
        /// `DatabaseProfiles.Overtaken`: an addition never overrules a removal made apart.
        /// </remarks>
        public IReadOnlyList<string> OvertakenNow()
        {
            if (!_database.IsOpen || !_records.Waiting)
            {
                return Array.Empty<string>();
            }

            var held = _records.Acts;
            var composed = _records.Composed();
            var found = new List<string>();
            using (var live = _database.Store.Read())
            {
                for (var at = 0; at < composed.Count; at++)
                {
                    if (composed[at] == null && ProfileActs.Overtaken(live, held[at]))
                    {
                        found.Add(ProfileActs.About(held[at]));
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Ignored rather than deferred. What marks the database is a change actually reaching it,
        /// which is <see cref="Apply"/>, because a database marked for a change that was then
        /// canceled is a database asking to be saved for nothing.
        /// </summary>
        /// <remarks>
        /// **What this buys is a Cancel that leaves nothing to save.** On OK, KeePass's own
        /// handler for the settings dialog marks the database changed whatever the tab did, after
        /// the tab has applied what it held, so an OK that changed nothing still asks for a save.
        /// </remarks>
        public void MarkModified()
        {
            // Deliberately nothing. What reaches the database is decided by what `Apply`
            // actually writes, not by who asked for a mark.
        }

        /// <summary>
        /// Writes everything that was waiting, and marks the database if anything landed.
        /// </summary>
        /// <param name="landed">
        /// Run once the plugin's store holds what this wrote into it, and only where the store's
        /// write wrote anything: the audit lines of what the clients page ended are written there.
        /// What it throws reaches the caller once the buffer is let go. Null for nothing.
        /// </param>
        /// <returns>True when the database now holds something it did not before.</returns>
        public bool Apply(Action landed = null)
        {
            Landed = Array.Empty<StoreAct>();
            if (!_database.IsOpen)
            {
                // The database went away while the dialog was open, which is a locked or closed
                // KeePass. There is nothing to write to and nothing to be done about it.
                _log.Warn("the " + GateName.Product + " tab had changes to apply and the database was gone");
                Discard();
                return false;
            }

            // The plugin's store first, through the writer, `STORE.md` §627: each act held to the
            // store as it stands now. An act left out is named by the store's writer, and a write
            // refused is named here with the acts it held, a write the writer's guard refuses
            // among them, and adds nothing. A save of the plugin's own is never what the guard
            // meets here: an OK pressed while such a save pumps under the dialog is handled inside
            // that pump, and the dialog's close, which runs this, is raised by the dialog's own
            // loop once the save has returned.
            var changed = false;
            var wrote = false;
            try
            {
                if (_records.Waiting)
                {
                    // Composed against the store as it stands now, on KeePass's thread with no
                    // other write between. An act with nothing left to write writes nothing, and
                    // the log names it: a certificate taken away by then, by its client or in a
                    // copy merged in, or with no record left in force under a client the store
                    // does not take away, as after a revocation held before it or a record deleted
                    // by hand; a name already where the press would put it; an Add that a
                    // removal of its name the press did not see overtook; and a lift every
                    // version of whose press was replaced since.
                    var held = _records.Acts;
                    var composed = _records.Composed();
                    if (composed.Contains(null))
                    {
                        using (var live = _database.Store.Read())
                        {
                            for (var at = 0; at < composed.Count; at++)
                            {
                                if (composed[at] != null)
                                {
                                    continue;
                                }

                                if (ProfileActs.Overtaken(live, held[at]))
                                {
                                    _log.Warn(Described(held[at]) + ": not written at OK, since a removal of the name "
                                              + "that the press did not see stands in the database");
                                }
                                else if (RenewalActs.About(held[at]) != null)
                                {
                                    _log.Warn(Described(held[at]) + ": not written at OK, since every answer to the "
                                              + "client's renewals that the press was shown was replaced since");
                                }
                                else
                                {
                                    _log.Info(Described(held[at]) + ": nothing was left to write at OK");
                                }
                            }
                        }
                    }

                    var acts = composed.Where(act => act != null).ToList();
                    var written = acts.Count == 0 ? null : _database.Store.Write(acts);

                    // The line telling what the write came to as soon as that is known, before the
                    // audit lines and letting the buffer go, so that a defect in either leaves a
                    // line telling what landed.
                    if (written != null && written.Taken)
                    {
                        wrote = written.Wrote;
                        changed = wrote;
                        Landed = wrote ? written.Acts : Array.Empty<StoreAct>();
                        if (wrote)
                        {
                            _log.Info("the plugin's store holds what the " + GateName.Product + " tab held: "
                                      + string.Join(", ", Landed.Select(Described)));
                            StillRefused(Landed);
                        }
                    }
                    else if (written != null)
                    {
                        _log.Warn("what the " + GateName.Product + " tab held for the plugin's store was not written ("
                                  + string.Join(", ", acts.Select(act => act.Name)) + "): " + written.Reason
                                  + (written.LeftOut.Count == 0 ? string.Empty : "; " + string.Join("; ", written.LeftOut)));
                    }
                }

                // On what was written, not on what was asked for. A create and a delete on the same
                // tab cancel out, and nothing is marked for them here. KeePass marks the database
                // on the settings dialog's OK in any case, `MarkModified` gives the reason that is
                // not undone here, so what `changed` decides beyond this is the factory's log line
                // about what landed.
                if (changed)
                {
                    _database.MarkModified();
                }

                // The lines as soon as the store holds what was written. A defect writing them
                // reaches the catch-all above once the buffer is let go, and the caller's own steps
                // after this run whatever the lines came to.
                if (wrote && landed != null)
                {
                    landed();
                }
            }
            finally
            {
                // The buffer last, whatever the write and the lines came to,
                // a write that throws among it: the acts held and composed, the store key each
                // holds overwritten, and the reading they leave.
                _records.Discard();
            }

            return changed;
        }

        /// <summary>
        /// Warns of each lift written beside which a refusal of its client still stands: an answer
        /// the renewal dialog wrote under this dialog, or a copy merged in brought, which the press
        /// was not shown and so did not name, `STORE.md` §627. Read with no clock: whether the
        /// refusal stands is asked, and not whether it refuses at this moment.
        /// </summary>
        private void StillRefused(IEnumerable<StoreAct> landed)
        {
            var lifts = landed.Select(RenewalActs.About).Where(client => client != null).ToList();
            if (lifts.Count == 0)
            {
                return;
            }

            using (var live = _database.Store.Read())
            {
                foreach (var client in lifts.Where(lifted => StoredRenewals.AnswerOf(live.InForce, lifted) != null))
                {
                    _log.Warn(RenewalActs.LiftName + " " + CertificateDigest.Shorten(client) + ": written at OK, "
                              + "and an answer the press was not shown still stands");
                }
            }
        }

        /// <summary>
        /// An act as the log names it: its name, and the profile name, or the clients and
        /// certificates its records are about, each by the first characters of its key.
        /// </summary>
        internal static string Described(StoreAct act)
        {
            var name = ProfileActs.About(act);
            if (name != null)
            {
                return act.Name + " " + name;
            }

            var lifted = RenewalActs.About(act);
            if (lifted != null)
            {
                return act.Name + " " + CertificateDigest.Shorten(lifted);
            }

            var about = act.Records
                .Select(record => record.Member(StoreKinds.Certificate) ?? record.Member(StoreKinds.ClientMember))
                .Where(CertificateDigest.IsDigest)
                .Distinct(StringComparer.Ordinal)
                .Select(CertificateDigest.Shorten)
                .ToList();
            return about.Count == 0 ? act.Name : act.Name + " " + string.Join(" ", about);
        }

        /// <summary>Throws away everything that was waiting, which is what Cancel means.</summary>
        public void Discard()
        {
            _records.Discard();
        }
    }

    /// <summary>
    /// The renewals refused in this session as one settings dialog sees them, by client, with the
    /// answers its clients page lifted held back until the dialog is accepted.
    /// </summary>
    /// <remarks>
    /// **Beside the buffer rather than in it**, because an answer for this session is no record of
    /// any database: it is the plugin's memory, `PAIRING.md` §312. What it shares with the buffer
    /// is the rule the whole tab follows, that nothing happens until OK. So the page reads through
    /// this, and an answer lifted here stops showing at once, and the plugin's own set is told
    /// only by <see cref="Apply"/>. Cancel is <see cref="Discard"/>, and the gate goes on refusing.
    ///
    /// One per dialog, for the reason the buffer is: two settings dialogs open at once each hold
    /// their own pending changes, and each one's OK applies what was pressed on it.
    /// </remarks>
    public sealed class PendingSessionRefusals
    {
        private readonly SessionRefusals _session;

        /// <summary>Every client whose answer was lifted on the page.</summary>
        private readonly HashSet<string> _lifted = new HashSet<string>(StringComparer.Ordinal);

        public PendingSessionRefusals(SessionRefusals session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _session = session;
        }

        /// <summary>
        /// The answer the plugin's memory holds about this client's renewals, as OK would leave
        /// it: an answer for this session, or null.
        /// </summary>
        public RenewalRefusal Answer(string clientKey)
        {
            return Holds(clientKey) ? RenewalRefusal.ThisSession() : null;
        }

        /// <summary>
        /// Lifts the plugin's memory of an answer about a client when the dialog is accepted,
        /// answering with whether there was an answer.
        /// </summary>
        public bool Lift(string clientKey)
        {
            if (!Holds(clientKey))
            {
                return false;
            }

            _lifted.Add(clientKey);
            return true;
        }

        /// <summary>
        /// Lifts every answer that was lifted on the page, which is what OK means, and answers
        /// with the clients it lifted.
        /// </summary>
        public IReadOnlyList<string> Apply()
        {
            var lifted = new List<string>(_lifted);
            foreach (var clientKey in lifted)
            {
                _session.Lift(clientKey);
            }

            _lifted.Clear();
            return lifted;
        }

        /// <summary>Forgets what was lifted on the page, which is what Cancel means.</summary>
        public void Discard()
        {
            _lifted.Clear();
        }

        /// <summary>
        /// Whether the plugin's memory holds an answer about this client's renewals, as OK would
        /// leave it.
        /// </summary>
        private bool Holds(string clientKey)
        {
            return !string.IsNullOrEmpty(clientKey) && !_lifted.Contains(clientKey) && _session.Refuses(clientKey);
        }
    }
}
