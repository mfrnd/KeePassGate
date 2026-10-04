using System;
using System.Collections.Generic;
using System.Threading;
using KeePassLib;
using KeePassGate.Acl;
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
    /// - **And that call can itself be delivered inside something else.** Being on that thread is
    ///   not being alone on it: the operations KeePass takes a while over all pump the message
    ///   queue while they run, and a marshaled call is a message. A write dispatched inside one
    ///   of them changes the tree that operation is walking, and saves underneath it. So there
    ///   is a third question beside the dialogs and the save's own three, asked in the marshaled
    ///   call because that is where the answer stops being able to change: is KeePass part way
    ///   through something of its own. <see cref="Interrupting"/> is that question.
    /// - **A dialog open on what is being written refuses the write**, checked again inside the
    ///   marshaled call rather than only before it, because that is where the answer stops being
    ///   able to change. A dialog holds its own copy of what it shows and writes that copy back when
    ///   it is accepted, so a change made underneath one is a change the operator's OK destroys.
    /// - **A save that would ask the operator something is refused instead**, which
    ///   `DESIGN.md` §119 states as "no API request raises a dialog of KeePass's own that could
    ///   have been foreseen". `MainForm.SaveDatabase` has three questions that can be seen
    ///   coming, and <see cref="Asking"/> is those three.
    /// - **Nothing is believed without asking.** KeePass's save reports nothing to its caller,
    ///   so what "it worked" is made of is `PwDatabase.Modified` having gone false. **That is the
    ///   answer after an exception as well as after a return**, because `MainForm.SaveDatabase`
    ///   catches everything the save itself throws and then raises `FileSaved` to every other
    ///   plugin outside that try: an exception arriving from there is somebody else's handler
    ///   failing over a file this change is already in. **And the flag is not the whole of it
    ///   either**, because KeePass clears it as soon as it has committed the bytes and only then
    ///   hashes the file back: when that disagrees it warns the operator and changes nothing, so
    ///   the file is asked about as well. Pairing asks for more again, and reads its record back,
    ///   because a lost admission record is a certificate that works and cannot be revoked.
    /// - **A change that landed says so to the window as well as to the client.** KeePass's save
    ///   ends in `UpdateUIState`, which redraws the title bar and the menus and rebuilds neither
    ///   the group tree nor the entry list, so without this an operator watching the window sees
    ///   the database as it was before the request. `DESIGN.md` §119 has the argument and the
    ///   seven arguments the redraw is made with. **And so does a change taken back out**, whose
    ///   failed save redrew the title bar while the change was still there: what it drew was a
    ///   database with something to save.
    /// </remarks>
    public sealed class DatabaseWriter : IWriterState
    {
        /// <summary>How long one write waits for another to finish before giving up.</summary>
        public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(20);

        /// <summary>How long it waits for the user interface thread to run the write.</summary>
        public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

        private readonly IUiThread _ui;
        private readonly Action<PwDatabase> _save;
        private readonly Func<string> _busy;
        private readonly Action<PwDatabase, bool> _redraw;
        private readonly GroupDialogs _dialogs;
        private readonly IGateLog _log;
        private readonly Action _admitted;

        private readonly object _writing = new object();

        /// <summary>
        /// How many times this writer's own save has begun or ended, so odd while one is running.
        /// </summary>
        /// <remarks>
        /// Changed only on the user interface thread, around <c>_save</c>, and read from any
        /// thread. A count rather than a flag so that a question asked from a connection thread
        /// can tell that a save began and ended between two of its reads, which a flag read twice
        /// as false cannot: <see cref="Blocked"/> says why that matters.
        /// </remarks>
        private int _saves;

        /// <param name="redraw">
        /// What tells KeePass that its window is out of date about a database, run on the user
        /// interface thread. It is handed the database, and whether the tree and the list are out of
        /// date as well as the title bar and the menus: they are after a change that was made and
        /// saved, and they are not after one that was taken back out, where the one thing wrong is
        /// the mark saying there is something to save. Which database the window is showing is its
        /// own business. Null where there is no window to tell, which is every test that is not
        /// about this and a host with no main window.
        /// </param>
        public DatabaseWriter(
            IUiThread ui,
            Action<PwDatabase> save,
            Action<PwDatabase, bool> redraw,
            GroupDialogs dialogs,
            IGateLog log)
            : this(ui, save, redraw, null, dialogs, log)
        {
        }

        /// <summary>
        /// The same, and told what to say when a record lands.
        /// </summary>
        /// <remarks>
        /// An action rather than an event, on the same terms as `redraw` beside it: what this
        /// class needs to know about anything drawn from the database is that something wants
        /// telling, and `AdmissionNotice` is what turns that into listeners.
        /// </remarks>
        public DatabaseWriter(
            IUiThread ui,
            Action<PwDatabase> save,
            Action<PwDatabase, bool> redraw,
            Action admitted,
            GroupDialogs dialogs,
            IGateLog log)
            : this(ui, save, null, redraw, admitted, dialogs, log)
        {
        }

        /// <summary>
        /// The same, and able to ask whether KeePass is part way through something of its own.
        /// </summary>
        /// <param name="busy">
        /// Why writing now would land inside an operation KeePass is already running, or null
        /// when it would not. Asked inside the marshaled call, where it is the truth about the
        /// thread the write is about to run on, and again from <see cref="Blocked"/> before an
        /// invitation is spent, where it is a guess worth making for the same reason the dialogs
        /// are asked about there. Null where there is nothing to ask, which is a test or a host
        /// with no main window: **null is not "nothing is running"**, it is "nobody can say", and
        /// it allows, because outside KeePass there is no operation of KeePass's to land in.
        /// </param>
        public DatabaseWriter(
            IUiThread ui,
            Action<PwDatabase> save,
            Func<string> busy,
            Action<PwDatabase, bool> redraw,
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
            _busy = busy;
            _redraw = redraw;
            _admitted = admitted;
            _dialogs = dialogs;
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>
        /// Why a record cannot be written to this database right now, or null when one can. Asked
        /// before an invitation is spent, so that a dialog left open costs an attempt rather than
        /// the invitation behind it.
        /// </summary>
        /// <remarks>
        /// **This writer's own save is not KeePass being busy**, and this is the one place that has
        /// to say so. The save goes through `MainForm.SaveDatabase`, which blocks KeePass's
        /// interaction for as long as it runs, and this is asked from connection threads, outside
        /// the writer's lock, while another client's write may be saving. Read as KeePass's own
        /// work, that save turned every pairing and renewal arriving during it away with `503
        /// busy`, before the dialog at the cost of a hello and after it at the cost of the
        /// operator's approval, for a write that would simply have waited its turn. So a block
        /// this writer raised is waited for instead: by the write, which waits for the lock, and
        /// by the approval prompt, which waits for the save before it puts a dialog up.
        ///
        /// Told apart by <see cref="_saves"/>, read either side of the question: a block seen
        /// while a save of this writer's was running, or began or ended, is that save's. What is
        /// left unasked then is the file, which that save is in the middle of writing, so a
        /// comparison now could only find the two halves of it; the write asks again inside its
        /// own marshaled call, where nothing of this writer's is running.
        /// </remarks>
        public string Blocked(PwDatabase database)
        {
            if (database == null || !database.IsOpen)
            {
                return "the database is not open";
            }

            if (database.RootGroup == null)
            {
                return "the database has no root group";
            }

            var before = Volatile.Read(ref _saves);
            var interrupting = Interrupting();
            var after = Volatile.Read(ref _saves);
            var ours = (before & 1) == 1 || before != after;

            if (interrupting != null && !ours)
            {
                // First of the three, because it is the cheapest and because it is the one that
                // makes the other two unsafe to trust: `Asking` reads the file, and KeePass part
                // way through writing that file has not put it back yet, so the comparison passes
                // and says nothing about whether writing now is a good idea.
                return interrupting;
            }

            if (_dialogs != null)
            {
                // One dialog, because one dictionary. A record and the profile names it adds go
                // into the database's own custom data, which only the maintenance dialog copies. No
                // group dialog is asked, the root group's included: this write touches no group, so
                // a group dialog's OK puts back nothing it changed.
                var open = _dialogs.BlockingRecords(database);
                if (open != null)
                {
                    return open;
                }
            }

            // Last, because it is the only one of these that reads the file. Pairing asks this
            // before it spends an invitation for the same reason it asks about the dialogs there:
            // a save that would stop to ask somebody costs an attempt rather than the invitation
            // behind it. Not while this writer's own save is writing that file, as above.
            return ours ? null : Asking(database);
        }

        /// <summary>
        /// Whether this writer's own save is running, which on KeePass's thread means it is what
        /// is pumping underneath whoever asks.
        /// </summary>
        public bool Saving
        {
            get { return (Volatile.Read(ref _saves) & 1) == 1; }
        }

        /// <summary>
        /// Why KeePass is part way through something of its own, asked on its thread, or null.
        /// </summary>
        /// <remarks>
        /// The question every write asks inside its marshaled call, for the approval prompt to ask
        /// inside its own: a dialog is a marshaled call like a write, and is delivered by whatever
        /// is pumping just as a write is. Asked after <see cref="Saving"/>, since this writer's
        /// own save blocks KeePass's interaction as KeePass's own does.
        /// </remarks>
        string IWriterState.Interrupting()
        {
            return Interrupting();
        }

        /// <summary>
        /// Waits for the write in flight, if there is one, to end, and says whether it did within
        /// the wait. Asked on a connection thread, never on KeePass's, where the write being waited
        /// for is waiting for that very thread.
        /// </summary>
        public bool Quiet(TimeSpan wait)
        {
            if (!Monitor.TryEnter(_writing, wait < TimeSpan.Zero ? TimeSpan.Zero : wait))
            {
                return false;
            }

            Monitor.Exit(_writing);
            return true;
        }

        /// <summary>Saves through KeePass, counted so that its block is known for this writer's.</summary>
        private void Save(PwDatabase database)
        {
            Interlocked.Increment(ref _saves);
            try
            {
                _save(database);
            }
            finally
            {
                Interlocked.Increment(ref _saves);
            }
        }

        /// <summary>Why this group cannot be changed right now, or null when it can.</summary>
        public string Blocking(PwGroup group)
        {
            return _dialogs == null ? null : _dialogs.Blocking(group);
        }

        /// <summary>
        /// Why this group cannot be taken out of the tree right now, beyond what
        /// <see cref="Blocking(PwGroup)"/> says, or null when it can.
        /// </summary>
        public string BlockingRemoval(PwGroup group)
        {
            return _dialogs == null ? null : _dialogs.BlockingRemoval(group);
        }

        /// <summary>Why nothing may be put into this group right now, or null when something may.</summary>
        public string BlockingAdditions(PwGroup group)
        {
            return _dialogs == null ? null : _dialogs.BlockingAdditions(group);
        }

        /// <summary>Why this entry cannot be changed right now, or null when it can.</summary>
        public string Blocking(PwEntry entry)
        {
            return _dialogs == null ? null : _dialogs.Blocking(entry);
        }

        /// <summary>
        /// Why this database cannot be given its first recycle bin right now, or null when it can.
        /// </summary>
        /// <remarks>
        /// The one thing KeePass's database settings dialog holds a copy of that a write here can
        /// change. `GroupDialogs.BlockingRecycleBin` says what its OK does with it.
        /// </remarks>
        public string BlockingRecycleBin(PwDatabase database)
        {
            return _dialogs == null ? null : _dialogs.BlockingRecycleBin(database);
        }

        /// <summary>
        /// Why writing now would land inside an operation KeePass is already running, or null when
        /// it would not.
        /// </summary>
        /// <remarks>
        /// **A marshaled call is a message, and a message is delivered by whatever is pumping.**
        /// The rest of this class was written as though running on the user interface thread meant
        /// KeePass was between operations, and it does not: everything KeePass takes a noticeable
        /// time over pumps that thread's queue while it runs, so a write handed to `BeginInvoke`
        /// by a connection thread is dispatched in the middle of it.
        ///
        /// - **Its own save.** `MainForm.SaveDatabase` pumps before the save, and its progress
        ///   logger pumps again for every percent of the serialization. A write delivered there
        ///   changes the tree that is being written and then saves it, and the outer save either
        ///   throws because the list it was walking moved or commits the copy it took before the
        ///   change, over the top of a save the client was told had landed.
        /// - **An import or a synchronize.** The merge's status logger pumps, and the nested save
        ///   writes a half-merged database and clears `Modified`, so what is left of the import
        ///   lives only in memory with nothing to say it is unsaved: lock, close and exit throw it
        ///   away without asking.
        /// - **An export.** The exporter walks the live tree and its logger pumps, so a write that
        ///   adds to or removes from the list it is enumerating makes it throw, leaving a warning
        ///   on the operator's screen and a truncated file.
        /// - **A message box.** A modal loop pumps by definition, and a question KeePass is
        ///   waiting on an answer to is the clearest case of it being part way through something.
        ///
        /// **What can be asked is not one question**, which is why this is a seam rather than a
        /// call. KeePass blocks its own interaction for the save and for the merges, and does not
        /// for an export: that one raises the message count instead. The wiring in
        /// `KeePassGateExt` asks both, and here it is one string.
        ///
        /// **And one operation announces itself in neither.** XML Replace merges into the live
        /// database with KeePass's progress window as its status logger, pumping on every step,
        /// and neither blocks interaction nor raises the count. The window is the only sign of it,
        /// and the dialog watch is what sees windows, so `GroupDialogs.Working` is asked after the
        /// seam.
        ///
        /// **A question that cannot be answered refuses**, like every other guard in this class.
        /// A write is worth losing; a client told its write landed when the operator's import
        /// took it back is not.
        /// </remarks>
        private string Interrupting()
        {
            if (_busy != null)
            {
                try
                {
                    var busy = _busy();
                    if (busy != null)
                    {
                        return busy;
                    }
                }
                catch (Exception error) when (Expected.OfUserInterface(error))
                {
                    _log.Error("KeePass could not be asked whether it is busy", error);
                    return "KeePass could not be asked whether it is in the middle of something";
                }
            }

            return _dialogs == null ? null : _dialogs.Working();
        }

        /// <summary>
        /// Why saving this database would put a question of KeePass's own on the operator's screen,
        /// or run a merge of KeePass's own inside the request, or null when it would do neither.
        /// </summary>
        /// <remarks>
        /// `MainForm.SaveDatabase` answers several conditions by asking somebody, and a request
        /// arriving over the wire is the one caller that must never make it do that: `DESIGN.md`
        /// §119 says an unattended agent may not prompt somebody's screen, and a modal box also
        /// holds this thread, the writer's lock and the connection until a person answers it, which
        /// is a wait no timeout here bounds. Three of its questions can be seen coming, and these
        /// are those three, in the order that call reaches them.
        ///
        /// - **Saving is disallowed by policy.** `AppPolicy.Try` shows a warning and answers false,
        ///   so nothing would have been written either way.
        /// - **The database has no file yet.** `SaveDatabase` hands that to `SaveDatabaseAs`, which
        ///   opens a file picker and asks where a program's write should be kept.
        /// - **The file on disk is not the one KeePass read.** `PreSaveValidate` compares those two
        ///   hashes and asks whether to synchronize, overwrite or cancel, and answering synchronize
        ///   runs an import inside the request. A database on a shared or synced path meets this in
        ///   the ordinary course of things, so it is the one of the three a client is likely to
        ///   provoke without anybody having misconfigured anything.
        ///
        ///   **With `SaveForceSync` on, KeePass asks nothing and synchronizes**, and that is refused
        ///   all the same. The merge still runs inside the request and underneath the change it
        ///   was asked to save, and it can still stop for the other file's key or to warn about
        ///   what it found, which is a question on the screen by another road. So nothing here
        ///   reads that setting: what is refused is what the save would do with this file, and
        ///   asking is only one of the two things it does.
        ///
        /// **The same comparison as `PreSaveValidate` makes, through the same function**, so the
        /// file this refuses to save over is exactly the file KeePass would stop at, whether to ask
        /// or to merge. It costs one more read of the file per write, which is what having the
        /// answer before the save rather than during it costs, and the save that follows reads the
        /// file again anyway.
        ///
        /// **For a database opened from a URL that read is a download**, on this thread and under
        /// the writer's lock, bounded only by the network timeouts KeePass gives it, which for a
        /// server that has stopped answering is minutes. The save's own read, its upload and the
        /// read after it are the same, so a write to such a database holds KeePass and every other
        /// write for as long as the network does. A download that fails answers null, which is
        /// what `PreSaveValidate` makes of it as well, as below.
        ///
        /// **Two of its questions stay, and both are a save that has already gone wrong**:
        /// `MessageService.ShowSaveWarning` when `PwDatabase.Save` throws, and the verification
        /// warning `PostSavingEx` raises when the bytes on disk are not the bytes written. Neither
        /// can be known before the save, and both report damage an operator has to be told about.
        /// </remarks>
        private static string Asking(PwDatabase database)
        {
            // Null when no KeePass has set one, which is a test or anything else assembling this
            // without the application around it. Allowing there leaves the decision to whichever
            // save was handed in, since outside KeePass nothing else knows what saving means.
            var policy = KeePass.App.AppPolicy.Current;
            if (policy != null && !policy.SaveFile)
            {
                return "saving this database is disallowed by policy, and KeePass reports that in a message box";
            }

            var connection = database.IOConnectionInfo;
            if (connection == null || string.IsNullOrEmpty(connection.Path))
            {
                return "the database has no file, so saving it would ask the operator for one";
            }

            var known = database.HashOfFileOnDisk;
            if (known == null)
            {
                // Nothing has been read from or written to that path yet, so there is no earlier
                // state for the file to disagree with. `PreSaveValidate` passes this case too.
                return null;
            }

            var current = KeePass.Util.WinUtil.HashFile(connection);
            if (current == null)
            {
                // Missing, unreachable or unreadable: that function answers null for every reason a
                // file can be any of them. `PreSaveValidate` treats it as nothing to compare, so
                // this does as well, and refusing instead would stop every write to a database
                // whose file the next save is about to put back.
                return null;
            }

            return KeePassLib.Utility.MemUtil.ArraysEqual(current, known)
                ? null
                : "the file on disk is not the one KeePass read, so saving would synchronize with it " +
                  "or ask the operator whether to";
        }

        /// <summary>
        /// Whether the file on disk holds what the save just wrote.
        /// </summary>
        /// <remarks>
        /// `PostSavingEx`'s own question, asked through the same two values, because a client told
        /// that something was saved has to be able to believe it and `Modified` going false is not
        /// enough to make that true. `PwDatabase.Save` clears that flag as soon as it has committed
        /// the bytes, and KeePass then hashes the file again and compares it with what it wrote.
        /// When they differ it shows "the new file's content does not match the data that KeePass
        /// has written, i.e. writing to the file has failed and it might be corrupted now", and
        /// that is the whole of what it does: it throws nothing, and it leaves `Modified` false.
        /// So without this the request is answered `201` with an identifier for something the file
        /// may not hold, and the next lock, close or exit throws the copy that does hold it away
        /// without asking.
        ///
        /// **Asked whatever the operator has set**, rather than only when
        /// `VerifyWrittenFileAfterSaving` is on. That setting decides whether KeePass warns
        /// somebody about their own save; it is not a decision about what this gate promises a
        /// program that cannot see the screen. It costs one read of the file per write that landed,
        /// which is what having an answer rather than an assumption costs; for a database opened
        /// from a URL that read is a download, which `Asking` says more about.
        ///
        /// **A file that cannot be read is not a file that holds anything**, so unreadable answers
        /// false here, as it does in <see cref="TheFileIsAsKeePassLeftIt"/> and for the same
        /// reason. A save that recorded no hash at all is a different case and answers true: the
        /// value is written by `PwDatabase.Save` itself, one statement before it clears the flag
        /// this is reached through, so inside KeePass there is no save that cleared one and not the
        /// other. Outside it there is no save either.
        /// </remarks>
        private static bool TheFileHoldsWhatWasWritten(PwDatabase database)
        {
            var written = database.HashOfLastIO;
            if (written == null)
            {
                return true;
            }

            var connection = database.IOConnectionInfo;
            if (connection == null || string.IsNullOrEmpty(connection.Path))
            {
                return true;
            }

            var current = KeePass.Util.WinUtil.HashFile(connection);
            return current != null && KeePassLib.Utility.MemUtil.ArraysEqual(current, written);
        }

        /// <summary>
        /// Whether the file on disk is still the one KeePass last read or wrote.
        /// </summary>
        /// <remarks>
        /// Asked on the way out of a save that did not land, and the only thing that makes it safe
        /// to tell KeePass the database has nothing unsaved. `PwDatabase.Save` leaves `Modified`
        /// set when it throws, and that flag is the whole of what makes `MainForm.CloseDocument`
        /// ask before a lock, a close or an exit discards the copy in memory. Clearing it over a
        /// damaged file takes the question away: the intact copy goes without anybody being asked,
        /// and what opens next time is the damage.
        ///
        /// **A failed save does not always leave the file alone.** KeePass promises that only for
        /// a transacted write committed by an atomic move. It writes the file directly, truncating
        /// it first, when the path is a reparse point such as a symbolic link, when `File.Exists`
        /// cannot see it, as on a share that has just gone, for ftp, and when the operator has
        /// turned file transactions off; and a transacted commit without TxF, which is every UNC
        /// path and every mapped or FAT drive, deletes the old file before it renames the new one.
        /// A save that stops part way there leaves a file that is shorter than it was or is not
        /// there at all, and a truncated kdbx opens again without an error, holding whatever
        /// reached the disk. Stopping part way is a full disk, a share that drops, or the tray
        /// menu's Cancel, which KeePass offers during every save.
        ///
        /// **The same comparison `Asking` makes, and the opposite answer to its nulls.** A file
        /// that cannot be read cannot be shown to be intact, and a commit that got as far as
        /// deleting the old file is exactly that case, so unreadable refuses here where it allows
        /// there. The two are not the same question: that one asks whether KeePass would stop to
        /// ask somebody, and this one asks whether the only undamaged copy may be thrown away.
        /// A database that has never been read from or written to its path has no earlier state
        /// for the file to disagree with, and there is nothing there to protect.
        /// </remarks>
        /// <param name="database">The database whose file is read back.</param>
        /// <param name="what">
        /// When it is not, what was measured: that the file could not be read, or that it was read
        /// and is something else. Said apart, because a file another program is holding is intact
        /// and only unreadable, and calling it damaged sends the operator after damage that is not
        /// there.
        /// </param>
        private static bool TheFileIsAsKeePassLeftIt(PwDatabase database, out string what)
        {
            what = null;

            var known = database.HashOfFileOnDisk;
            if (known == null)
            {
                return true;
            }

            var connection = database.IOConnectionInfo;
            if (connection == null || string.IsNullOrEmpty(connection.Path))
            {
                return true;
            }

            var current = KeePass.Util.WinUtil.HashFile(connection);
            if (current == null)
            {
                what = "could not be read back, so it cannot be shown to be intact";
                return false;
            }

            if (!KeePassLib.Utility.MemUtil.ArraysEqual(current, known))
            {
                what = "is neither what it was nor what was written";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Says in the log that a save which failed is being left marked as unsaved, and what was
        /// measured of the file that makes it so.
        /// </summary>
        private void LeftUnsaved(string what, Exception cause)
        {
            _log.Error("after the save failed the file on disk " + what + ", so the database is being left " +
                       "saying it has unsaved changes: KeePass has to ask before its next lock, close or " +
                       "exit throws away what may be the only undamaged copy", cause);
        }

        /// <summary>
        /// Writes an admission record, saves, and reads both the record and the file back before
        /// answering that it landed.
        /// </summary>
        /// <param name="audit">
        /// The audit line for the record, written here once it has landed. <see cref="Landed"/>
        /// says when and why here. Null for no line.
        /// </param>
        /// <param name="condition">
        /// Why the record may no longer be written, or null when it may, asked on KeePass's thread
        /// under the writer's lock, once the database is known to be writable and before anything
        /// is touched. Whatever it answers is the last word on the approval the record is for: an
        /// approval held while KeePass was busy, a record a renewal renews that went in the
        /// meantime, an invitation already spent. A refusal comes back withdrawn, with nothing
        /// changed. Null for none.
        /// </param>
        public AdmissionWrite WriteAdmission(
            PwDatabase database,
            AdmissionRecord record,
            Action audit = null,
            Func<IKeyValueStore, string> condition = null)
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
                return AdmissionWrite.Busy("another admission record is already being written");
            }

            try
            {
                AdmissionWrite outcome = null;

                bool ran;
                try
                {
                    ran = _ui.Run(() => outcome = OnTheUiThread(database, record, audit, condition), WriteTimeout);
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

                    _log.Warn("the user interface did not run the admission record's write in time");
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
        /// a refusal stops the save from happening at all, so a conflict costs nothing, and so does
        /// <see cref="VaultChange.Unchanged"/>, which is answered as having worked.
        /// </param>
        public VaultWrite Change(PwDatabase database, Func<VaultChange> change)
        {
            return Change(database, change, null);
        }

        /// <summary>
        /// Revokes one client at its own request: a revocation beside its record and the record
        /// emptied, saved and verified like every other change.
        /// </summary>
        /// <remarks>
        /// **The Clients page's write, made here because nobody is going to press OK on it**, which
        /// is `AdmissionStore.Remove` and the prunes that ride on it. Through
        /// <see cref="Change(PwDatabase, Func{VaultChange})"/>, so it waits its turn, is refused
        /// inside an operation KeePass is running and before a save that would ask, is taken back
        /// out when the save does not reach the disk, and is left where it is, with the database
        /// marked modified, when the file does not hold what was written.
        ///
        /// **And refused while Database Maintenance is open on the database**, which `Change` does
        /// not ask on its own, because no change to the tree touches what that dialog copies. This
        /// one does: a revocation adds a key to the database's own custom data, and that dialog
        /// writes its copy back whenever the number of keys differs, which would put the record
        /// back without its revocation and admit the client again.
        ///
        /// **What is taken back out is everything it touched**, the prunes included, because
        /// putting back the record alone would leave an undone revocation's prunes for whatever the
        /// operator saves next. That is three kinds of key: records, revocations, and the standing
        /// answers to renewals the last prune takes away with the records they were about, the
        /// revoked client's own among them.
        ///
        /// A revocation that lands is written to the audit and tells the Clients page, as a record
        /// that lands does.
        /// </remarks>
        /// <param name="audit">
        /// The audit line for the revocation, written here once it stands. <see cref="Landed"/>
        /// says when and why here. Null for no line.
        /// </param>
        public VaultWrite Revoke(PwDatabase database, string admissionKey, Action audit = null)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return VaultWrite.Missing();
            }

            return Change(database, () => RevokeOnTheUiThread(database, admissionKey), () => Landed(audit));
        }

        private VaultChange RevokeOnTheUiThread(PwDatabase database, string admissionKey)
        {
            if (_dialogs != null)
            {
                var open = _dialogs.BlockingRecords(database);
                if (open != null)
                {
                    return VaultWrite.Busy(open);
                }
            }

            var store = new CustomDataStore(database);
            var admissions = new AdmissionStore(store, _log);
            if (admissions.Find(admissionKey) == null)
            {
                // Gone since the connection was admitted: revoked from the Clients page in
                // between, or by a merge. Nothing is changed, and the certificate is refused from
                // here on, which is what was asked for.
                return VaultWrite.Missing();
            }

            var held = Records(store);
            if (!admissions.Remove(admissionKey))
            {
                Restore(store, held);
                return VaultWrite.Failed("the revocation did not survive being written");
            }

            return new VaultChange(VaultWrite.Done(), () => Restore(store, held));
        }

        /// <summary>
        /// Writes the operator's standing answer to a client renewing one certificate, saved and
        /// verified like every other change.
        /// </summary>
        /// <remarks>
        /// Refused while Database Maintenance is open on the database, for the reason a revocation
        /// is: the key is in the database's own custom data, which that dialog writes back. Taken
        /// back out, and the answer that was there before put back, when the save does not land.
        /// Nothing is drawn from it, so nothing is told when it lands.
        /// </remarks>
        public VaultWrite RefuseRenewal(PwDatabase database, string admissionKey, RenewalRefusal refusal)
        {
            if (string.IsNullOrEmpty(admissionKey) || refusal == null)
            {
                return VaultWrite.Failed("there is no standing answer to write");
            }

            if (!refusal.IsWritten)
            {
                // A caller's mistake, refused here by name rather than thrown inside the change,
                // where the store's argument exception read as a write that failed.
                _log.Error("a standing answer for this session alone was handed to the database writer", null);
                return VaultWrite.Failed("a standing answer for this session is never written to the database");
            }

            return Change(database, () => RefuseRenewalOnTheUiThread(database, admissionKey, refusal), null);
        }

        private VaultChange RefuseRenewalOnTheUiThread(PwDatabase database, string admissionKey, RenewalRefusal refusal)
        {
            if (_dialogs != null)
            {
                var open = _dialogs.BlockingRecords(database);
                if (open != null)
                {
                    return VaultWrite.Busy(open);
                }
            }

            var store = new CustomDataStore(database);
            var key = AuthorityKeys.RenewalRefusalKey(admissionKey);
            var before = store.Get(key);

            new AdmissionStore(store, _log).RefuseRenewal(admissionKey, refusal);

            return new VaultChange(VaultWrite.Done(), () =>
            {
                if (before == null)
                {
                    store.Remove(key);
                }
                else
                {
                    store.Set(key, before);
                }
            });
        }

        /// <summary>
        /// Every admission record, revocation and standing answer to a renewal, with what it holds.
        /// </summary>
        private static Dictionary<string, string> Records(CustomDataStore store)
        {
            var held = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var key in store.Keys)
            {
                if (Revocable(key))
                {
                    held[key] = store.Get(key);
                }
            }

            return held;
        }

        /// <summary>
        /// Whether a key is one a revocation can change: a record, a revocation, or a standing
        /// answer to renewing a certificate, which the revocation's prune takes away with its record.
        /// </summary>
        /// <remarks>
        /// One test for both halves of the undo, because the snapshot and the sweep disagreeing
        /// about a kind of key is what lost the standing answers: the prune that takes them came
        /// after the two were written.
        /// </remarks>
        private static bool Revocable(string key)
        {
            return AuthorityKeys.IsClientRecord(key)
                   || AuthorityKeys.IsRevocation(key)
                   || AuthorityKeys.IsRenewalRefusal(key);
        }

        /// <summary>
        /// Puts the records, revocations and standing answers back as <see cref="Records"/> found
        /// them, touching only what differs.
        /// </summary>
        /// <remarks>
        /// Only what differs, because `StringDictionaryEx.Set` stamps a fresh modification time on
        /// a value even when it is the one already there, and a time is what a merge decides by.
        /// What does differ comes back with a time of now rather than the one it had, since nothing
        /// public sets a time; each is a key this write changed in memory and the file never saw
        /// changed.
        /// </remarks>
        private static void Restore(CustomDataStore store, Dictionary<string, string> held)
        {
            foreach (var key in store.Keys)
            {
                if (Revocable(key) && !held.ContainsKey(key))
                {
                    store.Remove(key);
                }
            }

            foreach (var pair in held)
            {
                if (!string.Equals(store.Get(pair.Key), pair.Value, StringComparison.Ordinal))
                {
                    store.Set(pair.Key, pair.Value);
                }
            }
        }

        /// <param name="landed">
        /// Run on the user interface thread once the change stands: saved and verified, or saved
        /// and left in place because the file could not be verified, which undoes nothing. Not on
        /// any path that took the change back out. Null when nothing is drawn from what the change
        /// wrote.
        /// </param>
        private VaultWrite Change(PwDatabase database, Func<VaultChange> change, Action landed)
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
                    ran = _ui.Run(() => outcome = ChangeOnTheUiThread(database, change, landed), WriteTimeout);
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

        /// <summary>
        /// Keeps one of several authorities, retiring the rest and taking their documents away,
        /// and marks the database modified without saving it.
        /// </summary>
        /// <remarks>
        /// **The operator's answer in the authority chooser, and the one write here nobody waits on
        /// from a connection.** It still goes through the same lock and the same questions as
        /// every other write, because what it writes is exactly what Database Maintenance's
        /// write-back undoes: a retirement written while that dialog is open is lost when it
        /// closes, and a prune beside it is what turns a lucky key count into a write-back. So it
        /// is refused while that dialog is open or while KeePass is part way through something of
        /// its own, and the operator is told why.
        ///
        /// **Not saved, which is operator answer 5**: the database is marked, the window says so,
        /// and the operator saves as they would any other change. Serving starts at once all the
        /// same, because the gate reads the database in memory.
        /// </remarks>
        public AuthorityChange Keep(PwDatabase database, string fingerprint)
        {
            if (database == null || !database.IsOpen)
            {
                return AuthorityChange.Refused("the database is not open");
            }

            // Not waited for. This runs on KeePass's own thread, from the chooser, and a write
            // holding the lock is waiting for exactly that thread to run its marshaled half: a
            // wait here is the two waiting on each other until the timeout, with KeePass frozen.
            if (!Monitor.TryEnter(_writing, TimeSpan.Zero))
            {
                return AuthorityChange.Refused("another change is being written, so choose again in a moment");
            }

            try
            {
                AuthorityChange outcome = null;

                bool ran;
                try
                {
                    ran = _ui.Run(() => outcome = KeepOnTheUiThread(database, fingerprint), WriteTimeout);
                }
                catch (Exception error) when (Expected.OfUserInterface(error) || Expected.OfCustomData(error))
                {
                    _log.Error("an authority could not be kept", error);
                    return AuthorityChange.Refused(error.Message);
                }

                if (!ran && outcome == null)
                {
                    return AuthorityChange.Refused("the user interface did not answer in time");
                }

                return outcome ?? AuthorityChange.Refused("nothing was written");
            }
            finally
            {
                Monitor.Exit(_writing);
            }
        }

        private AuthorityChange KeepOnTheUiThread(PwDatabase database, string fingerprint)
        {
            var interrupting = Interrupting();
            if (interrupting != null)
            {
                return AuthorityChange.Refused(interrupting);
            }

            if (_dialogs != null)
            {
                var open = _dialogs.BlockingRecords(database);
                if (open != null)
                {
                    return AuthorityChange.Refused(open);
                }
            }

            var kept = new AuthorityService(new CustomDataStore(database), _log).Keep(fingerprint);
            if (kept.Succeeded)
            {
                // The title bar and the menus only: a choice changes the database's own custom
                // data, which the tree and the list do not show.
                database.Modified = true;
                Redraw(database, false);
            }

            return kept;
        }

        private VaultWrite ChangeOnTheUiThread(PwDatabase database, Func<VaultChange> change, Action landed)
        {
            if (!database.IsOpen)
            {
                // Asked again here, because `Change` asked on the connection thread and a lock or
                // a File > Close runs on this one, where it can come first. `PwDatabase.Close`
                // leaves an empty connection behind, so the question below would have answered
                // that the database has no file and turned a database that has gone into a
                // `503 busy` worth retrying. It is the same answer `Change` gives the same event
                // one instruction earlier.
                return VaultWrite.Failed("the database closed before the change could be made");
            }

            var interrupting = Interrupting();
            if (interrupting != null)
            {
                // Here rather than before the marshal, because this is the last instant at which
                // the answer can still change and the first at which it is the truth about the
                // thread the write is going to run on. A write refused here has not touched the
                // database.
                return VaultWrite.Busy(interrupting);
            }

            var asking = Asking(database);
            if (asking != null)
            {
                // Before the change is made rather than between the change and the save, so there
                // is nothing to take back out and the refusal costs the database nothing. Nothing
                // between here and `_save` pumps a message, so the two instants are equally close
                // to the save; what is *not* true, and what the guard above is for, is that
                // nothing has pumped to get here.
                return VaultWrite.Busy(asking);
            }

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

            if (!decided.Changed)
            {
                // The node already held every value the request named, which the change found out
                // here, on this thread and under this lock, where the answer cannot move before it
                // is acted on. Nothing was touched, so nothing is saved and nothing on screen is out
                // of date: `openapi.yaml` §504 says why writing anyway was the defect.
                return decided.Outcome;
            }

            // Whether this call is the one that made the database dirty, for the paths below that
            // take the change back out. A database that already had unsaved changes when the
            // request arrived still has them after a save that failed, and clearing the flag on
            // the way out would hide somebody else's work rather than this one's. A save that
            // lands writes them to the file along with this change, because KeePass saves the whole
            // database: `DESIGN.md` §119 records that as a consequence rather than a refusal.
            var wasModified = database.Modified;
            database.Modified = true;

            // Standing answers the operator gave while a database could not take them, written
            // with this change because it is the plugin's next write to that database: kept by
            // the same save, and taken back out with the change when the save does not land.
            var owing = Owe(database);

            Exception failed = null;

            try
            {
                Save(database);
            }
            catch (Exception error) when (Expected.OfSaving(error))
            {
                // Kept rather than answered, because an exception does not say whether the file
                // was written. The modified flag below is what says that, and it is asked of
                // everything a save and the handlers around it can throw: a filter naming the
                // file's exceptions alone let another plugin's refused access through with the
                // change still made, answered `500` as though nothing had been left behind.
                failed = error;
            }

            if (database.Modified)
            {
                // KeePass tells a caller nothing about whether a save worked. This is what it does
                // say: a database it has saved has no unsaved changes left. When `_save` returned
                // rather than threw, this is the live path, because `SaveDatabase` shows its own
                // warning and returns.
                TakeBackOwed(owing);
                return PutBack(
                    database,
                    decided,
                    wasModified,
                    VaultWrite.Unsaved(failed == null
                        ? "the database still has unsaved changes, so nothing is on disk"
                        : failed.Message),
                    failed);
            }

            if (failed != null)
            {
                // **An exception out of `_save` with nothing unsaved left is not a save that
                // failed.** `PwDatabase.Save` clears that flag as the last thing it does, and
                // `MainForm.SaveDatabase` catches everything the save itself throws; what is left
                // to throw is what runs after it, outside that try. Its `FileSaved` event is
                // raised there, to every other plugin loaded, with the file already written. So
                // an exception here means somebody else's handler failed after this change reached
                // the disk, and taking the change back out over it would leave the database in
                // memory disagreeing with the file KeePass just wrote: the client is told `500 not
                // saved` and retries, the next unlock reads the file and brings the first copy
                // back beside the second, and the operator's next save writes the undone state
                // over the top instead. Decided by the flag rather than by the exception's type
                // for the same reason every other question here is asked of the database: the type
                // says which code threw, and the flag says what happened to the file.
                _log.Error("the change was saved and something after the save failed, which is " +
                           "another plugin's `FileSaved` handler: the change is on the disk and " +
                           "is being left there", failed);
            }

            if (!TheFileHoldsWhatWasWritten(database))
            {
                // Not put back, and this is the one failure here that leaves the change where it
                // is. The file disagrees with what KeePass wrote, which says nothing about which
                // of the two it holds, so undoing would be as likely to make memory wrong as to
                // make it right. What can be done is to stop claiming it landed and to put the
                // flag back up, so that KeePass asks before a lock, a close or an exit discards
                // the copy that certainly does hold the change.
                database.Modified = true;

                _log.Error("the file on disk is not what KeePass wrote to it, so a change that " +
                           "may not be stored is not being reported as stored", null);

                // Left in place, so it is in force, and it is written down and what is drawn from
                // the records is told as a change that landed is: a revocation kept here refuses
                // the client from its next request, and a page still listing that client would be
                // listing one every request refuses. The window is not redrawn, as it is not for a change that may not be
                // stored; the clients page is about who is refused, which this decides.
                Owing.Settle(owing, Owed);
                if (landed != null)
                {
                    landed();
                }

                return VaultWrite.Unverified(
                    "the file on disk does not hold what KeePass wrote to it");
            }

            // Last, and the only place the tree and the list are asked for. Every path above
            // either changed nothing or has just put its change back, and in both cases they
            // already show the database as it now is: neither is rebuilt between the change and
            // the undo, since nothing on this thread reads a `PwGroup` again until something asks
            // it to. What a put back does leave wrong is the mark saying there is something to
            // save, which `PutBack` redraws on its own.
            Redraw(database, true);

            Owing.Settle(owing, Owed);
            if (landed != null)
            {
                landed();
            }

            return decided.Outcome;
        }

        /// <summary>
        /// Tells KeePass that what its window shows of this database is out of date.
        /// </summary>
        /// <remarks>
        /// On this thread, which is the user interface one, and inside the writer's lock, so the
        /// window is redrawn from the database exactly as the change left it.
        ///
        /// <paramref name="lists"/> is whether the group tree and the entry list are out of date as
        /// well as the title bar and the menus. Whether they are shown at all is the window's
        /// question, since only the database in front has either on screen.
        ///
        /// **A window that will not redraw never fails the request.** By the time this runs the
        /// change is made and the file is written, and a client told its write failed writes again:
        /// that is a second entry under an identifier it was never given. A repaint that did not
        /// happen is a log line and an operator pressing something.
        /// </remarks>
        private void Redraw(PwDatabase database, bool lists)
        {
            if (_redraw == null)
            {
                return;
            }

            try
            {
                _redraw(database, lists);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("KeePass was not told what changed, so its window is showing the " +
                          "database as it was: " + error.Message);
            }
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
        ///
        /// **And the modified flag goes back only over a file the save left alone**, which
        /// <see cref="TheFileIsAsKeePassLeftIt"/> is. Putting the change back makes the database in
        /// memory what it was; it does not make the file on disk what it was, and a save that
        /// failed part way through writing it directly leaves it truncated. Telling KeePass there
        /// is nothing unsaved over a file like that is what lets its next lock, close or exit throw
        /// the undamaged copy away without asking anybody.
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
                    string what;
                    if (TheFileIsAsKeePassLeftIt(database, out what))
                    {
                        database.Modified = false;

                        // The save that failed ended in `UpdateUIState` while the change was still
                        // in, so the title bar and the save button say there is something to save.
                        // Nothing is, now, and a window left saying so sends the operator looking
                        // for a change the client was told did not happen, or saving to keep it.
                        Redraw(database, false);
                    }
                    else
                    {
                        LeftUnsaved(what, cause);
                    }
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a change could not be taken back out either, so the database is " +
                           "holding one the caller was told did not happen", error);
            }

            return answer;
        }

        /// <summary>
        /// Adds any profile the operator approved that this database had never named.
        /// </summary>
        /// <remarks>
        /// **Here rather than beside the approval, because it is the same decision as the record
        /// and has to be the same write**: on this thread, under this lock, kept by the save below,
        /// and refused by the same open dialog. Written anywhere else it is a change to the
        /// database's custom data while Database Maintenance may be holding its own copy, and that
        /// copy's write-back would put the old list back. It would also be a write that can succeed
        /// while the record fails, leaving a database naming a profile for a client it never
        /// admitted.
        ///
        /// **A name that cannot be added is skipped rather than refused, and what that costs is
        /// said.** The list holds <see cref="ProfileNames.Most"/> names, and the operator can
        /// approve a new one while it already holds that many. The client then carries the name in
        /// its certificate and its record, and since a client reaches only what the list still
        /// names, it reaches nothing through that one until the operator adds it on the Profiles
        /// page, which at the ceiling means taking another off first; the grant editor offers no
        /// name the list does not hold, so no rule can be written for it before then. Refusing the
        /// approved pairing would cost more: a client holding a certificate for a record nobody
        /// wrote. So the pairing lands, and the log says that a name was left off and why, without
        /// naming it, as the pairing's own line does not.
        /// </remarks>
        private void Name(CustomDataStore store, IReadOnlyList<string> profiles)
        {
            if (profiles == null || profiles.Count == 0)
            {
                return;
            }

            try
            {
                var stored = store.Get(ProfileNames.CustomDataKey);
                if (!ProfileNames.Readable(stored))
                {
                    // Never written over. Read, it is a list naming nothing, and a list of only the
                    // approved names written in its place would drop every name the damaged one
                    // held, and with it the sign that it was damaged. The pairing and the renewal
                    // refuse before this while it will not read, so this is a list damaged since.
                    _log.Warn("the profiles this database names will not read, so a profile the operator " +
                              "approved was not added to them and the list was left as it is");
                    return;
                }

                var names = ProfileNames.Read(stored);
                var added = false;

                foreach (var profile in profiles)
                {
                    if (names.Holds(profile))
                    {
                        continue;
                    }

                    var refused = names.Add(profile);
                    if (refused != null)
                    {
                        _log.Warn("a profile the operator approved was left off the list this database " +
                                  "names, so the client reaches nothing through it until it is added on " +
                                  "the Profiles page: " + refused);
                        continue;
                    }

                    added = true;
                }

                if (!added)
                {
                    return;
                }

                var document = names.Write();
                if (document != null)
                {
                    store.Set(ProfileNames.CustomDataKey, document);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a profile the operator approved could not be named", error);
            }
        }

        /// <summary>
        /// The standing answers owed to databases, written by every write that lands on the one
        /// each belongs to, or null for none.
        /// </summary>
        /// <remarks>
        /// Settable rather than a constructor argument because one plugin builds one of each and
        /// tests build writers with nothing owed. `SessionRefusals` is the one there is.
        /// </remarks>
        public IOwedAnswers Owed { get; set; }

        /// <summary>
        /// Writes every standing answer owed to this database into it, and says what it wrote and
        /// what was there before, so that a write that does not land takes them back out.
        /// </summary>
        /// <remarks>
        /// **Only where the answer belongs**: the database serving the authority it was given
        /// under, and while the record it is about is still there. One whose record has gone is
        /// settled without being written, since nothing is left for it to refuse. **A failure here
        /// fails nothing**: the write it rides on goes ahead, the answers stay owed, and the next
        /// write tries again.
        /// </remarks>
        private Owing Owe(PwDatabase database)
        {
            var owed = Owed == null ? null : Owed.Owed();
            if (owed == null || owed.Count == 0)
            {
                return null;
            }

            var owing = new Owing(new CustomDataStore(database));
            try
            {
                var admissions = new AdmissionStore(owing.Store, _log);
                foreach (var answer in owed)
                {
                    if (string.IsNullOrEmpty(owing.Store.Get(AuthorityKeys.AuthorityKey(answer.DatabaseId)))
                        || owing.Store.Get(AuthorityKeys.RetirementKey(answer.DatabaseId)) != null)
                    {
                        continue;
                    }

                    if (admissions.Find(answer.AdmissionKey) == null)
                    {
                        owing.Settled.Add(answer);
                        continue;
                    }

                    var key = AuthorityKeys.RenewalRefusalKey(answer.AdmissionKey);
                    if (!owing.Before.ContainsKey(key))
                    {
                        owing.Before[key] = owing.Store.Get(key);
                    }

                    admissions.RefuseRenewal(answer.AdmissionKey, answer.Refusal);
                    owing.Settled.Add(answer);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("a standing answer to a renewal owed to this database could not be written " +
                          "with this change, and is still owed: " + error.Message);
                TakeBackOwed(owing);
                return null;
            }

            return owing;
        }

        /// <summary>Takes back out what <see cref="Owe"/> wrote, leaving the answers owed.</summary>
        private static void TakeBackOwed(Owing owing)
        {
            if (owing == null)
            {
                return;
            }

            foreach (var pair in owing.Before)
            {
                if (pair.Value == null)
                {
                    owing.Store.Remove(pair.Key);
                }
                else
                {
                    owing.Store.Set(pair.Key, pair.Value);
                }
            }

            owing.Before.Clear();
            owing.Settled.Clear();
        }

        /// <summary>What one write wrote of the answers owed, and what it wrote over.</summary>
        private sealed class Owing
        {
            public Owing(CustomDataStore store)
            {
                Store = store;
                Before = new Dictionary<string, string>(StringComparer.Ordinal);
                Settled = new List<OwedAnswer>();
            }

            public CustomDataStore Store { get; private set; }

            public Dictionary<string, string> Before { get; private set; }

            public List<OwedAnswer> Settled { get; private set; }

            /// <summary>Says the answers it wrote are in their database, once the write has landed.</summary>
            public static void Settle(Owing owing, IOwedAnswers owed)
            {
                if (owing == null || owed == null)
                {
                    return;
                }

                foreach (var answer in owing.Settled)
                {
                    owed.Kept(answer);
                }
            }
        }

        /// <summary>
        /// Takes away the documents of retired authorities, the admission records that came back
        /// beside their revocations, the records of expired certificates and the standing answers
        /// about certificates no record admits, as part of a write this plugin is making anyway,
        /// answering with what went.
        /// </summary>
        /// <remarks>
        /// **Here, inside the guarded write, and never at open**, which is operator answer 3a:
        /// opening a database must not mark it modified, and a prune made while Database
        /// Maintenance is open is undone by that dialog's write-back. Nothing depends on it, since
        /// the decision reads the retirements and admission reads the revocations, so one that
        /// fails is a warning and the write goes on.
        /// </remarks>
        private IReadOnlyList<KeyValuePair<string, string>> Prune(CustomDataStore store)
        {
            try
            {
                return new AuthorityStore(store, _log).Prune();
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("what retired authorities, revoked clients and expired certificates left behind " +
                          "could not be taken away: " + error.Message);
                return Array.Empty<KeyValuePair<string, string>>();
            }
        }

        private AdmissionWrite OnTheUiThread(
            PwDatabase database, AdmissionRecord record, Action audit, Func<IKeyValueStore, string> condition)
        {
            if (!database.IsOpen)
            {
                // Before `Blocked`, which would say the same thing as a reason to try again. A
                // database that closed while this waited for its turn has gone rather than being
                // busy, and the pairing is abandoned either way: the log is where the difference
                // is read.
                return AdmissionWrite.Failed("the database closed before the record could be written");
            }

            var blocked = Blocked(database);
            if (blocked != null)
            {
                // Asked again here, because between the first check and this moment a dialog can
                // have opened, and this is the last instant at which that can still be true.
                return AdmissionWrite.Busy(blocked);
            }

            var store = new CustomDataStore(database);

            if (condition != null)
            {
                // Last of the questions and first of the changes: everything above can still say
                // busy, which keeps an approval held, and nothing below can. So what the condition
                // spends, an approval or an invitation, is spent only on a write that is going
                // ahead, and what it compares cannot move before the write that relies on it.
                var withdrawn = condition(store);
                if (withdrawn != null)
                {
                    return AdmissionWrite.Withdrawn(withdrawn);
                }
            }

            var written = record.Save();

            // What was there before, so a write that never reaches the disk can be taken back out
            // again. A record left behind by a pairing that failed is a client on the tab that was
            // never issued anything, and the operator's next save is what would make it permanent.
            var before = store.Get(record.StorageKey);

            // The name list too. `Name` below writes it *before* the record, so a pairing that
            // failed after that point left the profile it had just created in a database KeePass
            // believes has nothing to save. The operator's next unrelated edit then commits a
            // name no record holds.
            var namesBefore = store.Get(ProfileNames.CustomDataKey);
            var wasModified = database.Modified;

            // What the prune below takes away, so a write that does not land puts it back and
            // the database in memory goes on agreeing with the file.
            IReadOnlyList<KeyValuePair<string, string>> pruned = null;
            Owing owing = null;

            try
            {
                Name(store, record.Profiles);
                store.Set(record.StorageKey, written);
                pruned = Prune(store);
                owing = Owe(database);
                database.Modified = true;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("an admission record could not be put in the database", error);
                return Undo(store, database, record, before, namesBefore, pruned, owing, wasModified, error.Message);
            }

            try
            {
                Save(database);
            }
            catch (Exception error) when (Expected.OfSaving(error))
            {
                // KeePass's own save and every other plugin's handlers around it, which can fail
                // for every reason a file can and every reason a window can: `Expected.OfSaving`
                // is that list. What it must not do is take the connection thread with it.
                //
                // **Which of those it was is the database's answer rather than the exception's**,
                // for the reason `ChangeOnTheUiThread` gives: the save clears the modified flag as
                // the last thing it does, and `MainForm.SaveDatabase` catches everything the save
                // itself throws, so an exception arriving with nothing unsaved left is another
                // plugin's `FileSaved` handler failing after the record reached the disk. Undoing
                // there would refuse the client its certificate while the record stays in the
                // file, which is the client on the clients page that never existed that `Undo`
                // exists to prevent, arriving by the other road.
                if (database.Modified)
                {
                    _log.Error("the database could not be saved after an admission record was written", error);
                    return Undo(store, database, record, before, namesBefore, pruned, owing, wasModified, error.Message);
                }

                _log.Error("the record was saved and something after the save failed, which is " +
                           "another plugin's `FileSaved` handler: the record is on the disk and " +
                           "is being left there", error);
            }

            if (!string.Equals(store.Get(record.StorageKey), written, StringComparison.Ordinal))
            {
                return Undo(
                    store, database, record, before, namesBefore, pruned, owing, wasModified, "the record did not survive being written");
            }

            if (database.Modified)
            {
                // KeePass tells a caller nothing about whether a save worked. This is what it does
                // say: a database it has saved has no unsaved changes left.
                return Undo(
                    store, database, record, before, namesBefore, pruned, owing, wasModified,
                    "the database still has unsaved changes, so nothing is on disk");
            }

            if (!TheFileHoldsWhatWasWritten(database))
            {
                // The read-back above is of the store, which is memory. This is the file, and it
                // is the last thing between the record and a certificate: a record that may not be
                // on the disk is a credential nobody can see and nobody can revoke, which is the
                // one thing this whole class exists to prevent. Undone rather than left where the
                // API's write leaves it, because refusing a pairing costs an invitation and
                // admitting one that was never stored costs a client nobody can take away.
                //
                // The flag goes up whatever it was before, which is more than `Undo` does on its
                // own: a database that arrived with unsaved edits has just had them written to a
                // file that did not keep them either, so this is the one path where leaving it
                // clear would be wrong about somebody else's work as well as about this record.
                database.Modified = true;

                return Undo(
                    store, database, record, before, namesBefore, pruned, owing, wasModified,
                    "the file on disk does not hold what KeePass wrote to it");
            }

            Owing.Settle(owing, Owed);
            Landed(audit);
            return AdmissionWrite.Done();
        }

        /// <summary>
        /// Writes the audit line for a record or a revocation that landed, and then says it landed
        /// to whatever is drawn from the records.
        /// </summary>
        /// <remarks>
        /// **The line first, and here, so that nothing after it can lose it.** An approved
        /// pairing, an approved renewal and a client's revocation of itself are each written to
        /// the audit once their change is in force. The operation used to write its line once this
        /// write had returned, and a defect in a listener below went past every filter on the way
        /// to the connection thread's catch-all, so a change that was on the disk was never written
        /// down. Written here, on the path that leaves the change in force and before any listener
        /// runs, the line is in the audit however the listener ends, and nothing new catches
        /// everything to keep it there.
        ///
        /// **Once at most.** Each path that leaves a change in force runs this once, and a path
        /// that took the change back out or never made it does not run it at all, so an operation
        /// writes no line of its own about a change it handed here.
        ///
        /// **And never waiting**, because this is KeePass's own thread inside the writer's lock.
        /// The line is written without waiting for the client's turn, and the operation takes that
        /// turn once the write returns, on the client's own connection, as `AdmissionAudit` says.
        /// An audit swallows what it cannot write, so a full disk fails nothing here either.
        /// </remarks>
        private void Landed(Action audit)
        {
            if (audit != null)
            {
                audit();
            }

            Admitted();
        }

        /// <summary>
        /// Says a record landed, or a client's revocation of itself did, to whatever is drawn from
        /// the records.
        /// </summary>
        /// <remarks>
        /// Here and nowhere earlier: every path above this either put its change back or is still
        /// deciding, and a page told about a record that was taken out again would list a client
        /// the next request refuses. A revocation reaches it from the end of a change, on the two
        /// paths that leave the change in force: saved and verified, and saved without the file
        /// being verified, which takes nothing back out.
        ///
        /// **A page that fails to redraw never fails the write.** By the time this runs the record is
        /// on the disk and the client is about to be handed its certificate, so a page that failed
        /// to redraw is a page an operator can bring forward again; failing the pairing over it
        /// would spend the invitation and give nothing back. What is caught is what drawing can
        /// throw, the list `Expected.OfUserInterface` keeps. Anything else is a defect in the
        /// listener and is not caught here, because this is not one of the four places that catch
        /// everything: it reaches the connection thread's, which counts it, and the pairing ends
        /// with its record saved and no certificate handed over. A renewal ends the same way, and a
        /// client's revocation of itself ends in force and answered `500`. In all three the audit
        /// line is already written, by <see cref="Landed"/> before this ran, so the change on the
        /// disk is in the audit as well, and the incident the catch-all counts says why its answer
        /// was a failure. The tab listens only once it is whole, so the one listener there is
        /// cannot be told about a record half built.
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
        /// still waiting, and clearing one over a file the failed save damaged would tell it there
        /// is nothing to save while the only undamaged copy is the one in memory.
        /// <see cref="TheFileIsAsKeePassLeftIt"/> is that second question, in the same words as
        /// `PutBack` asks it.
        /// </remarks>
        private AdmissionWrite Undo(
            CustomDataStore store,
            PwDatabase database,
            AdmissionRecord record,
            string before,
            string namesBefore,
            IReadOnlyList<KeyValuePair<string, string>> pruned,
            Owing owing,
            bool wasModified,
            string reason)
        {
            try
            {
                TakeBackOwed(owing);

                if (before == null)
                {
                    store.Remove(record.StorageKey);
                }
                else
                {
                    store.Set(record.StorageKey, before);
                }

                // What the prune took away: retired documents, records that came back beside
                // their revocations, records of expired certificates and the standing answers
                // about certificates no record admits. They serve, admit and refuse nothing either
                // way, and put back they leave memory agreeing with the file this write never
                // reached. Each comes back with a modification time of now rather than the one it
                // had, which nothing public can set; harmless, since each is about something no
                // record admits, and the next landed write prunes it again.
                if (pruned != null)
                {
                    foreach (var pair in pruned)
                    {
                        store.Set(pair.Key, pair.Value);
                    }
                }

                // Only when it actually moved. `StringDictionaryEx.Set` stamps a modification time,
                // so putting an identical value back is a change of its own.
                if (!string.Equals(store.Get(ProfileNames.CustomDataKey), namesBefore, StringComparison.Ordinal))
                {
                    if (namesBefore == null)
                    {
                        store.Remove(ProfileNames.CustomDataKey);
                    }
                    else
                    {
                        store.Set(ProfileNames.CustomDataKey, namesBefore);
                    }
                }

                if (!wasModified)
                {
                    string what;
                    if (TheFileIsAsKeePassLeftIt(database, out what))
                    {
                        database.Modified = false;

                        // The title bar and the save button only, for the reason `PutBack` gives:
                        // a save that failed drew the database as having something to save.
                        Redraw(database, false);
                    }
                    else
                    {
                        // Said, as `PutBack` says it: the database is left marked unsaved with no
                        // change in it, and the operator is otherwise told only that the write
                        // failed.
                        LeftUnsaved(what, null);
                    }
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
