using System;
using System.Threading;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassGate.Vault;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The one place a request changes a database, whatever the change is.
    /// </summary>
    /// <remarks>
    /// It began as pairing's writer and the rules it was built to keep are the API's rules too, so
    /// there is one of these rather than two contending for the same database. `PAIRING.md` §303,
    /// phase 5, is where they came from. Everything about it is a consequence of one requirement: a
    /// certificate must not be handed over until the record admitting it is on disk, because a
    /// certificate with no record is a credential nobody can see and nobody can revoke. An entry
    /// written through the API is the same requirement with a different subject: a client told that
    /// something was saved has to be able to believe it.
    ///
    /// - **Single writer.** One of these per plugin, so two changes at once take their turn rather
    ///   than interleaving a write with a save. The wait for it is bounded, because a request that
    ///   cannot get in fails rather than queues.
    /// - **The write and the save happen together on the user interface thread**, in one marshaled
    ///   call, so nothing can open a dialog on the group between them.
    /// - **A dialog open on what is being written refuses the write**, checked again inside the
    ///   marshaled call rather than only before it, because that is where the answer stops being
    ///   able to change. A dialog holds its own copy of what it shows and writes that copy back when
    ///   it is accepted, so a change made underneath one is a change the operator's OK destroys.
    /// - **Nothing is believed without asking.** KeePass's save reports nothing to its caller,
    ///   so what "it worked" is made of is `PwDatabase.Modified` having gone false. Pairing asks
    ///   for more, and reads its record back as well, because a lost admission record is a
    ///   certificate that works and cannot be revoked.
    /// </remarks>
    public sealed class DatabaseWriter
    {
        /// <summary>How long one write waits for another to finish before giving up.</summary>
        public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(20);

        /// <summary>How long it waits for the user interface thread to run the write.</summary>
        public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

        private readonly IUiThread _ui;
        private readonly Action<PwDatabase> _save;
        private readonly GroupDialogs _dialogs;
        private readonly IGateLog _log;
        private readonly Action _admitted;

        private readonly object _writing = new object();

        public DatabaseWriter(IUiThread ui, Action<PwDatabase> save, GroupDialogs dialogs, IGateLog log)
            : this(ui, save, null, dialogs, log)
        {
        }

        /// <summary>
        /// The same, and told what to say when a record lands.
        /// </summary>
        /// <remarks>
        /// An action rather than an event: what this class needs to know about anything drawn
        /// from the records is that something wants telling, and `AdmissionNotice` is what
        /// turns that into listeners.
        /// </remarks>
        public DatabaseWriter(
            IUiThread ui,
            Action<PwDatabase> save,
            Action admitted,
            GroupDialogs dialogs,
            IGateLog log)
        {
            if (ui == null)
            {
                throw new ArgumentNullException(nameof(ui));
            }

            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            _ui = ui;
            _save = save;
            _admitted = admitted;
            _dialogs = dialogs;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>
        /// Why a record cannot be written to this database right now, or null when one can. Asked
        /// before an invitation is spent, so that a dialog left open costs an attempt rather than
        /// the invitation behind it.
        /// </summary>
        public string Blocked(PwDatabase database)
        {
            if (database == null || !database.IsOpen)
            {
                return "the database is not open";
            }

            var root = database.RootGroup;
            if (root == null)
            {
                return "the database has no root group";
            }

            if (_dialogs == null)
            {
                return null;
            }

            // Two dictionaries, two dialogs. Everything this plugin stores about the database
            // itself is in the database's own custom data, which the maintenance dialog copies;
            // a node's rules are on the node, which the group dialog copies. A write here can
            // reach either, so both are asked.
            return _dialogs.Blocking(root) ?? _dialogs.BlockingRecords(database);
        }

        public AdmissionWrite WriteAdmission(PwDatabase database, AdmissionRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (database == null)
            {
                return AdmissionWrite.Failed("there is no database to write to");
            }

            if (!Monitor.TryEnter(_writing, LockTimeout))
            {
                return AdmissionWrite.Busy("another pairing is already writing");
            }

            try
            {
                AdmissionWrite outcome = null;

                bool ran;
                try
                {
                    ran = _ui.Run(() => outcome = OnTheUiThread(database, record), WriteTimeout);
                }
                catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfCustomData(error))
                {
                    _log.Error("the admission record could not be written", error);
                    return AdmissionWrite.Failed(error.Message);
                }

                if (!ran)
                {
                    // `IUiThread.Run` withdraws work it never started and waits for work it did,
                    // so by this line the write has either not happened at all or has finished.
                    // That is what makes releasing the lock below safe, and it is what stops an
                    // abandoned write saving a record after the client was already turned away.
                    if (outcome != null)
                    {
                        // It ran, just not in time to be waited for. A late answer is still the
                        // truth about the database and is worth more than a guess.
                        return outcome;
                    }

                    _log.Warn("the user interface did not run the pairing write in time");
                    return AdmissionWrite.Busy("the user interface did not answer in time");
                }

                return outcome ?? AdmissionWrite.Failed("nothing was written");
            }
            finally
            {
                Monitor.Exit(_writing);
            }
        }

        /// <summary>
        /// Makes a change on the thread that owns the database, saves, and does not come back until
        /// the database says it has nothing unsaved.
        /// </summary>
        /// <param name="change">
        /// Runs on the user interface thread, under the writer's lock. It returns what it decided:
        /// a refusal stops the save from happening at all, so a conflict costs nothing.
        /// </param>
        public VaultWrite Change(PwDatabase database, Func<VaultChange> change)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }

            if (database == null || !database.IsOpen)
            {
                return VaultWrite.Failed("there is no database to write to");
            }

            if (!Monitor.TryEnter(_writing, LockTimeout))
            {
                return VaultWrite.Busy("another change is already being written");
            }

            try
            {
                VaultWrite outcome = null;

                bool ran;
                try
                {
                    ran = _ui.Run(() => outcome = ChangeOnTheUiThread(database, change), WriteTimeout);
                }
                catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfCustomData(error))
                {
                    _log.Error("a change could not be written", error);
                    return VaultWrite.Failed(error.Message);
                }

                if (!ran)
                {
                    if (outcome != null)
                    {
                        // It ran, just not in time to be waited for, which `Run` reports the same
                        // way as never having started. A late answer is still the truth about the
                        // database, and the alternative is telling a client to retry a create that
                        // already landed. That retry duplicates the entry and loses the identifier
                        // it was never given. `WriteAdmission` above says the same thing.
                        return outcome;
                    }

                    _log.Warn("the user interface did not run the change in time");
                    return VaultWrite.Busy("the user interface did not answer in time");
                }

                return outcome ?? VaultWrite.Failed("nothing was written");
            }
            finally
            {
                Monitor.Exit(_writing);
            }
        }

        private VaultWrite ChangeOnTheUiThread(PwDatabase database, Func<VaultChange> change)
        {
            VaultChange decided;

            try
            {
                decided = change();
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a change could not be made", error);
                return VaultWrite.Failed(error.Message);
            }

            if (decided == null || decided.Outcome == null || !decided.Outcome.Ok)
            {
                // Refused before anything was touched, so there is nothing to save and nothing to
                // put back.
                return decided == null || decided.Outcome == null
                    ? VaultWrite.Failed("nothing was written")
                    : decided.Outcome;
            }

            // Whether this call is the one that made the database dirty. A database that already
            // had unsaved changes when the request arrived still has them afterwards, and clearing
            // that on the way out would hide somebody else's work rather than this one's.
            var wasModified = database.Modified;
            database.Modified = true;

            try
            {
                _save(database);
            }
            catch (Exception error) when (Expected.OfCustomData(error) || error is System.IO.IOException)
            {
                return PutBack(database, decided, wasModified, VaultWrite.Unsaved(error.Message), error);
            }

            if (database.Modified)
            {
                // KeePass tells a caller nothing about whether a save worked. This is what it does
                // say: a database it has saved has no unsaved changes left. This is the live path,
                // because `SaveDatabase` shows its own warning and returns rather than throwing.
                return PutBack(
                    database,
                    decided,
                    wasModified,
                    VaultWrite.Unsaved("the database still has unsaved changes, so nothing is on disk"),
                    null);
            }

            return decided.Outcome;
        }

        /// <summary>Takes a change back out when the save it was made for never reached the disk.</summary>
        /// <remarks>
        /// The caller has been told the write did not land, and a change left in the database
        /// KeePass is holding makes that untrue on the operator's next save: it commits something
        /// its author was told had not happened, under a `uuid` the client was never given, and a
        /// retry that already made a second copy. `WriteAdmission` has put its record back since it
        /// was written, for the same reason and in the same words; this is the rest of the writes
        /// catching up with it.
        ///
        /// **A failed undo is reported and not thrown.** By this point the request is already being
        /// answered as unsaved, and the honest thing left to say is that the database is holding a
        /// change nobody can now take out. That belongs in the log rather than in a second
        /// failure on top of the first.
        /// </remarks>
        private VaultWrite PutBack(
            PwDatabase database, VaultChange change, bool wasModified, VaultWrite answer, Exception cause)
        {
            if (change.Undo == null)
            {
                // A change that did not say how to undo it. Nothing to take back is not the same as
                // nothing to say, so the caller is still told the write did not land.
                _log.Error("a change was made and could not be saved, and it carries no way back, " +
                           "so the database has an unsaved change the operator has to decide about", cause);
                return answer;
            }

            _log.Error("a change was made and could not be saved, so it is being taken back out", cause);

            try
            {
                change.Undo();

                if (!wasModified)
                {
                    database.Modified = false;
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a change could not be taken back out either, so the database is " +
                           "holding one the caller was told did not happen", error);
            }

            return answer;
        }

        private AdmissionWrite OnTheUiThread(PwDatabase database, AdmissionRecord record)
        {
            var blocked = Blocked(database);
            if (blocked != null)
            {
                // Asked again here, because between the first check and this moment a dialog can
                // have opened, and this is the last instant at which that can still be true.
                return AdmissionWrite.Busy(blocked);
            }

            var store = new CustomDataStore(database);
            var written = record.Save();

            // What was there before, so a write that never reaches the disk can be taken back out
            // again. A record left behind by a pairing that failed is a client on the tab that was
            // never issued anything, and the operator's next save is what would make it permanent.
            var before = store.Get(record.StorageKey);
            var wasModified = database.Modified;

            try
            {
                store.Set(record.StorageKey, written);
                database.Modified = true;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("an admission record could not be put in the database", error);
                return Undo(store, database, record, before, wasModified, error.Message);
            }

            try
            {
                _save(database);
            }
            catch (Exception error) when (Expected.OfCustomData(error) || error is System.IO.IOException)
            {
                // KeePass's own save, which reaches the file system and can fail for every reason
                // a file can. What it must not do is take the connection thread with it.
                _log.Error("the database could not be saved after pairing", error);
                return Undo(store, database, record, before, wasModified, error.Message);
            }

            if (!string.Equals(store.Get(record.StorageKey), written, StringComparison.Ordinal))
            {
                return Undo(
                    store, database, record, before, wasModified, "the record did not survive being written");
            }

            if (database.Modified)
            {
                // KeePass tells a caller nothing about whether a save worked. This is what it does
                // say: a database it has saved has no unsaved changes left.
                return Undo(
                    store, database, record, before, wasModified,
                    "the database still has unsaved changes, so nothing is on disk");
            }

            Admitted();
            return AdmissionWrite.Done();
        }

        /// <summary>Says a record landed, to whatever is drawn from the records.</summary>
        /// <remarks>
        /// Here and nowhere earlier: every path above this either put its change back or is still
        /// deciding, and a page told about a record that was taken out again would list a client
        /// the next request refuses.
        ///
        /// **A page that fails to redraw never fails the write.** By the time this runs the record
        /// is on the disk and the client is about to be handed its certificate, so a page that
        /// failed to redraw is a page an operator can bring forward again; failing the pairing
        /// over it would spend the invitation and give nothing back. What is caught is what
        /// drawing can throw, the list `Expected.OfUserInterface` keeps. Anything else is a
        /// defect in the listener and is not caught here, because this is not one of the places
        /// that catch everything: it reaches the connection thread's, which counts it, and the
        /// pairing ends with its record saved and no certificate handed over.
        /// </remarks>
        private void Admitted()
        {
            if (_admitted == null)
            {
                return;
            }

            try
            {
                _admitted();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("a record landed and the tab showing the clients was not told, so it " +
                          "is listing them as they were: " + error.Message);
            }
        }

        /// <summary>
        /// Puts the database's custom data back the way it was, and answers with why the write is
        /// being refused.
        /// </summary>
        /// <remarks>
        /// The caller is about to be told the record did not land, and the certificate it was for
        /// is about to be destroyed unsent. Leaving the record in the database anyway would make
        /// that a lie the operator finds later: a client on the clients page that never existed,
        /// admitted by a certificate nobody holds, written to disk by whatever they save next.
        ///
        /// The modified flag goes back only if this write is what set it. Clearing one that was
        /// already there would tell KeePass it has nothing to save while somebody else's change is
        /// still waiting.
        /// </remarks>
        private AdmissionWrite Undo(
            CustomDataStore store,
            PwDatabase database,
            AdmissionRecord record,
            string before,
            bool wasModified,
            string reason)
        {
            try
            {
                if (before == null)
                {
                    store.Remove(record.StorageKey);
                }
                else
                {
                    store.Set(record.StorageKey, before);
                }

                if (!wasModified)
                {
                    database.Modified = false;
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a record that did not land could not be taken back out again", error);
            }

            return AdmissionWrite.Failed(reason);
        }
    }
}
