using System;
using System.Threading;
using KeePassLib;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The one thing a request writes to a database, and the only place it writes it.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` phase 5, against a real KeePass. Everything about it is a consequence of one
    /// requirement: a certificate must not be handed over until the record admitting it is on disk,
    /// because a certificate with no record is a credential nobody can see and nobody can revoke.
    ///
    /// - **Single writer.** One of these per plugin, so two pairings at once take their turn rather
    ///   than interleaving a write with a save. The wait for it is bounded, because a pairing that
    ///   cannot get in fails rather than queues.
    /// - **The write and the save happen together on the user interface thread**, in one marshaled
    ///   call, so nothing can open a dialog on the group between them.
    /// - **A group dialog open on the root group refuses the write**, checked again inside the
    ///   marshaled call rather than only before it, because that is where the answer stops being
    ///   able to change.
    /// - **Nothing is believed without reading it back.** The record has to still be there
    ///   afterwards, and the database has to have stopped saying it has unsaved changes. KeePass's
    ///   save reports nothing to its caller, so this is what "it worked" is made of.
    /// </remarks>
    public sealed class AdmissionWriter
    {
        /// <summary>How long a pairing waits for another one to finish before giving up.</summary>
        public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(20);

        /// <summary>How long it waits for the user interface thread to run the write.</summary>
        public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

        private readonly IUiThread _ui;
        private readonly Action<PwDatabase> _save;
        private readonly GroupDialogs _dialogs;
        private readonly IGateLog _log;
        private readonly object _writing = new object();

        public AdmissionWriter(IUiThread ui, Action<PwDatabase> save, GroupDialogs dialogs, IGateLog log)
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

        public AdmissionWrite Write(PwDatabase database, AdmissionRecord record)
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

            return AdmissionWrite.Done();
        }

        /// <summary>
        /// Puts the group back the way it was, and answers with why the write is being refused.
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
