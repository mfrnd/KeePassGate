using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KeePassLib;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Store;
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
    /// - **And that call waits for KeePass to be idle, which keeps it out of KeePass's own
    ///   operations.** Being on that thread is not being alone on it: the operations KeePass takes
    ///   a while over all pump the message queue while they run, and a write posted as a message
    ///   was dispatched inside them, changing the tree they were walking and saving underneath
    ///   them. A turn from `ControlUiThread` starts only on `Application.Idle`, which the
    ///   `DoEvents` they pump with never raises, so a write waits for them instead. A dialog one
    ///   of them shows is a loop of WinForms' own and does raise Idle, so the third question
    ///   beside the dialogs and the save's own three stays, asked in the marshaled call because
    ///   that is where the answer stops being able to change: is KeePass part way through
    ///   something of its own. <see cref="Interrupting"/> is that question. **And a dialog of
    ///   Windows' own is asked about before the call is queued**, through `IUiThread.Stalled`,
    ///   since a turn never comes round under one and the write would only find that out from
    ///   its timeout.
    /// - **A dialog open on what is being written refuses the write**, checked again inside the
    ///   marshaled call rather than only before it, because that is where the answer stops being
    ///   able to change. A dialog holds its own copy of what it shows and writes that copy back when
    ///   it is accepted, or as it closes in the case of KeePass's attachment editor, so a change
    ///   made underneath it is a change the operator destroys without being told.
    /// - **A save that would ask the operator something is refused instead**, which
    ///   `DESIGN.md` §119 states as "no API request raises a dialog of KeePass's own that could
    ///   have been foreseen". `MainForm.SaveDatabase` has three questions that can be seen
    ///   coming, and <see cref="Asking"/> is those three.
    /// - **Nothing is believed without asking.** KeePass's save reports nothing to its caller,
    ///   so what "it worked" is made of is `PwDatabase.Modified` having gone false. **That is the
    ///   answer after an exception as well as after a return**, because `MainForm.SaveDatabase`
    ///   catches everything the save itself throws and runs the rest outside that try: its window
    ///   redrawn, which raises `UIStateUpdated` to every other plugin, its `FileSaved` event and
    ///   the triggers. An exception arriving from there is something failing over a file this
    ///   change is already in. **And the flag is not the whole of it
    ///   either**, because KeePass clears it as soon as it has committed the bytes and only then
    ///   hashes the file back: when that disagrees it warns the operator and changes nothing, so
    ///   the file is asked about as well. Pairing asks for more again, and reads its record back,
    ///   because a lost admission record is a certificate that works and cannot be revoked.
    /// - **A change that landed says so to the window as well as to the client.** KeePass's save
    ///   ends in `UpdateUIState`, which redraws the title bar and the menus and rebuilds neither
    ///   the group tree nor the entry list, so without this an operator watching the window sees
    ///   the database as it was before the request. `DESIGN.md` §119 has the argument and the
    ///   seven arguments the redraw is made with. **And so does a change taken back out**, whose
    ///   failed save either redrew the title bar while the change was still there or, abandoned
    ///   before writing, drew nothing: either way the window does not show the flag the put back
    ///   leaves, cleared or left up.
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

        /// <summary>The database this writer's own save is saving, on KeePass's thread, or null.</summary>
        private PwDatabase _ownSave;

        /// <summary>
        /// Whether KeePass reported, through `FileSaved`, that a save of <see cref="_ownSave"/>
        /// landed while this writer's last save ran.
        /// </summary>
        /// <remarks>
        /// Asked only of a database that closed inside that save, where nothing else is left to
        /// ask: <see cref="Concluded"/> explains why. Any save of the database counts, the save the
        /// writer began and a save KeePass's close made of it on the operator's answer alike,
        /// since either wrote what memory held.
        /// </remarks>
        private bool _ownSaveLanded;

        /// <summary>
        /// What this writer's last save carried of the standing answers owed, written by
        /// <see cref="BeforeSave"/> while it ran.
        /// </summary>
        private Owing _carried;

        /// <summary>
        /// What each save KeePass is making for somebody else carries, from its start to its end.
        /// </summary>
        private readonly Dictionary<PwDatabase, Owing> _carrying = new Dictionary<PwDatabase, Owing>();

        /// <summary>
        /// A writer that makes each change and its save in one call marshaled to the user
        /// interface thread.
        /// </summary>
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
                // One dialog for the custom data: the standing answers to a renewal a write prunes
                // go into the database's own custom data, which only the maintenance dialog copies.
                // The admission and the profile names a pairing adds are entries of the plugin's
                // group, asked about once the write's plan exists, `STORE.md` §626. No
                // group dialog is asked: a group dialog's OK puts back no entry. The TAN wizard is
                // asked about the save rather than the write, since its OK adds entries inside a
                // save of its database, and this write ends in a save.
                var open = _dialogs.BlockingRecords(database) ?? _dialogs.BlockingSaves(database);
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
        /// inside its own: a dialog is a turn on KeePass's thread like a write, and meets whatever
        /// a write meets there. Asked after <see cref="Saving"/>, since this writer's own save
        /// blocks KeePass's interaction as KeePass's own does.
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
        /// <remarks>
        /// **And noted as this writer's**, so that what <see cref="BeforeSave"/> writes into it is
        /// the writer's to settle once the file has been read back: <see cref="_carried"/>.
        /// </remarks>
        private void Save(PwDatabase database)
        {
            Interlocked.Increment(ref _saves);
            _ownSave = database;
            _ownSaveLanded = false;
            _carried = null;
            try
            {
                _save(database);
            }
            finally
            {
                _ownSave = null;
                Interlocked.Increment(ref _saves);
            }
        }

        /// <summary>
        /// Writes into a database KeePass is about to save the standing answers owed to it, so that
        /// the save carries them, whoever asked for it. KeePass's `FileSavingPre`, on its thread.
        /// </summary>
        /// <remarks>
        /// **Every save, which is the operator's choice of 2026-11-04.** The answers rode on this
        /// writer's own writes alone, so a database the operator saved after keeping an authority
        /// or pressing the KPG tab's OK went to its file without them, and an answer that no write
        /// of the plugin's reached before KeePass exited was lost though the database had been
        /// saved since.
        ///
        /// **Not a copy**, which leaves the database's own file as it was, and **not while Database
        /// Maintenance is open on it**: that dialog writes its copy of the custom data back when it
        /// closes whenever the number of keys differs, and an answer written underneath it was gone
        /// from the database after memory had let it go. Both leave the answers owed for the next
        /// save.
        ///
        /// **This writer's own save settles what it carried itself**, once the file has been read
        /// back, and takes it back out with the change on every path that takes the change back
        /// out, so an answer is let go from memory only by a save known to have landed. A save
        /// whose file did not read back as written leaves it owed, since whether the file holds it
        /// is exactly what nothing knows.
        /// </remarks>
        public void BeforeSave(PwDatabase database, bool copy)
        {
            if (database == null || copy || !database.IsOpen)
            {
                return;
            }

            if (_dialogs != null && _dialogs.BlockingRecords(database) != null)
            {
                return;
            }

            var owing = Owe(database);
            if (ReferenceEquals(database, _ownSave))
            {
                _carried = owing;
                return;
            }

            if (owing != null)
            {
                _carrying[database] = owing;
            }
        }

        /// <summary>
        /// Settles what a save KeePass made for somebody else carried, or takes it back out when
        /// the save did not land. KeePass's `FileSaved`, on its thread.
        /// </summary>
        /// <remarks>
        /// Believed on KeePass's word, as an operator's own save is believed: only this writer's
        /// saves read the file back, and those settle their own. **What this writer keeps of
        /// KeePass's word about its own save** is whether a save of that database landed, for the
        /// one ending the file cannot be asked about: <see cref="_ownSaveLanded"/>.
        /// </remarks>
        public void AfterSave(PwDatabase database, bool success)
        {
            if (success && database != null && ReferenceEquals(database, _ownSave))
            {
                _ownSaveLanded = true;
            }

            Owing owing;
            if (database == null || ReferenceEquals(database, _ownSave) || !_carrying.TryGetValue(database, out owing))
            {
                return;
            }

            _carrying.Remove(database);
            if (success)
            {
                Owing.Settle(owing, Owed);
            }
            else
            {
                TakeBackOwed(owing);
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
        /// **A posted call is a message, and a message is delivered by whatever is pumping.** The
        /// rest of this class was written as though running on the user interface thread meant
        /// KeePass was between operations, and it does not: everything KeePass takes a noticeable
        /// time over pumps that thread's queue while it runs, so a write handed to `BeginInvoke`
        /// by a connection thread was dispatched in the middle of it, with what follows. **Queued
        /// rather than posted, a write no longer starts there**: a turn starts on
        /// `Application.Idle`, and every one of these pumps through `Application.DoEvents`, which
        /// never raises it, so the write waits for the operation to end. This is still asked,
        /// and still refuses, for what can raise Idle part way through one of them: a dialog of
        /// WinForms' own that one shows, whose loop raises it as KeePass's main loop does.
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
        ///   A message box's loop is Windows' own and raises no Idle either, so a turn never starts
        ///   under one; the write asks `IUiThread.Stalled` before it is queued and is refused at
        ///   once rather than at the end of `WriteTimeout`.
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
        /// **And a merge another plugin runs announces itself in none of them**, so the thread's
        /// own stack is asked first, <see cref="Merging"/>. A queued write never meets a merge,
        /// since a turn starts outside it. The write that takes no turn does: the operator's
        /// answer to which authority to keep, <see cref="Keep"/>, written from a post or a
        /// timer's tick on KeePass's own thread, which the pump of a merge's status logger
        /// delivers inside the merge, and the chooser's question, which waits on this answer.
        ///
        /// **A question that cannot be answered refuses**, like every other guard in this class.
        /// A write is worth losing; a client told its write landed when the operator's import
        /// took it back is not.
        /// </remarks>
        private string Interrupting()
        {
            var merging = Merging;
            if (merging != null && merging())
            {
                return "KeePass is merging another database into this database";
            }

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
        /// file this refuses to save over is the file KeePass would stop at, whether to ask or to
        /// merge, as the file stands when this reads it. It costs one more read of the file per
        /// write, which is what having the answer before the save rather than during it costs, and
        /// the save that follows reads the file again anyway.
        ///
        /// **Earlier than `PreSaveValidate` reads it, and the gap is not closed.** Between the two
        /// reads `MainForm.SaveDatabase` pumps messages as it raises its block and raises
        /// `FileSavingPre` to every plugin, this one's `OwedAnswerSaves` among them, and another
        /// process needs no pump at all. A file a sync client rewrites in that gap still puts
        /// KeePass's question, or its merge under `SaveForceSync`, inside the request, holding the
        /// writer's lock until it is answered. `DESIGN.md` §119 names the gap
        /// beside the two warnings that remain.
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
        /// other. Outside it there is no save either. A database closed after its save has no hash
        /// either, since `PwDatabase.Close` clears it, and that case is asked before this is.
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
            Func<IWriteGround, string> condition = null)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (database == null)
            {
                return AdmissionWrite.Failed("there is no database to write to");
            }

            // Before the lock, so that the answer is immediate: a write cannot start under a dialog
            // of Windows' own, and queued it would wait the whole of `WriteTimeout` to be told so.
            var stalled = _ui.Stalled;
            if (stalled != null)
            {
                return AdmissionWrite.Busy(stalled);
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
            return Change(database, change, true, null);
        }

        /// <summary>
        /// Takes a single certificate away at its own client's request, `STORE.md` §648: a record
        /// of kind `withdrawn` written into the plugin's store, saved and verified like every other
        /// change.
        /// </summary>
        /// <remarks>
        /// **Made here because nobody is going to press OK on it.** Through
        /// <see cref="Change(PwDatabase, Func{VaultChange})"/>, so it waits its turn, is refused
        /// inside an operation KeePass is running and before a save that would ask, is taken back
        /// out when the save does not reach the disk, and is left where it is, with the database
        /// marked modified, when the file does not hold what was written.
        ///
        /// **In the turn**: the store read with every entry, the certificate still admitted, else
        /// missing, the act of `AdmissionActs.TakeAway`, whose record names the renewals in force
        /// and the certificate's own origin, and its plan, which deletes the admission and restates
        /// the record by an item of its own. A second certificate of the same client keeps
        /// working, and so does the certificate a renewal issued, since the record names it.
        ///
        /// **And refused while Database Maintenance is open on the database**, which `Change` does
        /// not ask on its own: the write prunes the standing answers to a renewal no admission
        /// names any more, which are the database's own custom data, and that dialog writes its
        /// copy back whenever the number of keys differs.
        ///
        /// **What is taken back out is everything it touched**: the entries and their deletion
        /// records, and the standing answers the prune took away.
        ///
        /// A certificate given up that lands is written to the audit, tells the Clients page, and
        /// has KeePass's group tree and entry list rebuilt, as every change that adds an entry
        /// does.
        /// </remarks>
        /// <param name="authority">
        /// The `id` of the authority the connection giving the certificate up arrived under, which
        /// is the issuer of the admission that admitted it.
        /// </param>
        /// <param name="audit">
        /// The audit line for the certificate given up, written here once it stands.
        /// <see cref="Landed"/> gives when and why here. Null for no line.
        /// </param>
        public VaultWrite GiveUp(PwDatabase database, string admissionKey, string authority, Action audit = null)
        {
            if (string.IsNullOrEmpty(admissionKey))
            {
                return VaultWrite.Missing();
            }

            return Change(database, () => GiveUpOnTheUiThread(database, admissionKey, authority), true, () => Landed(audit));
        }

        private VaultChange GiveUpOnTheUiThread(PwDatabase database, string admissionKey, string authority)
        {
            if (_dialogs != null)
            {
                var open = _dialogs.BlockingRecords(database);
                if (open != null)
                {
                    return VaultWrite.Busy(open);
                }
            }

            var kdbx = new KdbxStore(database, GateVersion.Current, _log, Holding);
            using (var reading = kdbx.ForWrite())
            {
                // The store's word where it holds the gate back, and `undecided` where several
                // authorities stand, the connection's own among them, `STORE.md` §656, as for a
                // pairing.
                var word = StoreReadings.WordOf(reading, authority);
                if (word != null)
                {
                    _log.Warn("a certificate given up was not written, since " + HeldBack(word, reading));
                    return VaultWrite.Unavailable(word);
                }

                if (StoreAdmissions.Admitted(reading, admissionKey) == null)
                {
                    // Gone since the connection was admitted: taken away, its client revoked, or
                    // its authority retired in between, beside several others standing as well,
                    // where no authority is served. Nothing is changed, and the certificate is
                    // refused from here on, which is what was asked for.
                    return VaultWrite.Missing();
                }

                var act = AdmissionActs.TakeAway(reading, admissionKey);
                try
                {
                    var staged = kdbx.Stage(reading, new[] { act });
                    if (staged.Refused != null)
                    {
                        // A statement for each origin the certificate's admissions hold, so a
                        // store with room for a further item may have none for this write: the
                        // word of a store with no room for its next write, `STORE.md` §656, with
                        // nothing written.
                        _log.Warn("a certificate given up was not written into the plugin's store: " + staged.Refused);
                        return staged.Write.NoRoom ? VaultWrite.Unavailable(StoreWords.Stopped) : VaultWrite.Failed(staged.Refused);
                    }

                    if (staged.Held != null)
                    {
                        return VaultWrite.Busy(staged.Held);
                    }

                    var store = new CustomDataStore(database);

                    // The mark is `Change`'s to decide, from what the database held before and
                    // from the file, as for every change: the application raises it, and it is
                    // put back here so that a write taken back out over an unchanged file leaves
                    // nothing to save.
                    var modified = database.Modified;
                    var undo = staged.Apply();
                    database.Modified = modified;
                    var pruned = new RenewalAnswers(store, _log).Prune(StoreAdmissions.Named(reading, staged.Write));
                    return new VaultChange(VaultWrite.Done(), () =>
                    {
                        PutBackAnswers(store, pruned);
                        undo();
                    });
                }
                finally
                {
                    act.Forget();
                }
            }
        }

        /// <summary>
        /// Writes the operator's standing answer to a client renewing one certificate, saved and
        /// verified like every other change.
        /// </summary>
        /// <remarks>
        /// Refused while Database Maintenance is open on the database: the key is in the
        /// database's own custom data, which that dialog writes back whenever the number of keys
        /// differs. Taken back out, and the answer that was there before put back, when the save
        /// does not land. The Clients page draws the row's name red from it, so the page is told
        /// when it lands, as it is when an admission does; the tree and the entry list show none
        /// of it, so they are not rebuilt.
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

            return Change(database, () => RefuseRenewalOnTheUiThread(database, admissionKey, refusal), false, Admitted);
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

            new RenewalAnswers(store, _log).RefuseRenewal(admissionKey, refusal);

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
        /// Puts back the standing answers to a renewal a write's prune took away, where nothing
        /// holds the key again since.
        /// </summary>
        /// <remarks>
        /// Each comes back with a modification time of now rather than the one it had, which
        /// nothing public sets; harmless, since each is about a certificate no admission names, and
        /// the next write that lands prunes it again.
        /// </remarks>
        private static void PutBackAnswers(IKeyValueStore store, IEnumerable<KeyValuePair<string, string>> pruned)
        {
            if (pruned == null)
            {
                return;
            }

            foreach (var pair in pruned.Where(pair => store.Get(pair.Key) == null))
            {
                store.Set(pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// The change <see cref="Change(PwDatabase, Func{VaultChange})"/> makes, with something to
        /// run once it stands.
        /// </summary>
        /// <param name="lists">
        /// Whether a change that lands puts the group tree and the entry list out of date, which
        /// <see cref="Redraw"/> rebuilds for the database in front. False for a change to the
        /// database's own custom data alone, which neither shows: the rebuild replaces the
        /// operator's search results with the selected group's entries.
        /// </param>
        /// <param name="landed">
        /// Run on the user interface thread once the change stands: saved and verified, or saved
        /// and left in place because the file could not be verified, which undoes nothing, and so
        /// too when the save ended in an exception nothing expects, before that exception goes on.
        /// Not on any path that took the change back out, nor for a database that closed inside
        /// its save before KeePass reported a save of it that landed. Null when nothing is drawn
        /// from what the change wrote.
        /// </param>
        private VaultWrite Change(PwDatabase database, Func<VaultChange> change, bool lists, Action landed)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }

            if (database == null || !database.IsOpen)
            {
                return VaultWrite.Failed("there is no database to write to");
            }

            // Before the lock, as `WriteAdmission` asks it and for its reason.
            var stalled = _ui.Stalled;
            if (stalled != null)
            {
                return VaultWrite.Busy(stalled);
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
                    ran = _ui.Run(() => outcome = ChangeOnTheUiThread(database, change, lists, landed), WriteTimeout);
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
        /// Keeps one of several authorities, a retirement of each of the others written into the
        /// plugin's store, and marks the database modified without saving it, `STORE.md` §614.
        /// </summary>
        /// <remarks>
        /// **The operator's answer in the authority chooser, and the one write here nobody waits on
        /// from a connection.** It still goes through the same lock as every other write, and is
        /// refused while an entry dialog holds an entry the write deletes, sets back or protects,
        /// the summary entry among them, and while KeePass is part way through something of its
        /// own, and the operator is told why. It writes no custom data and saves nothing, so
        /// Database Maintenance and the TAN wizard refuse it nothing, `STORE.md` §626.
        ///
        /// **Not saved, which is operator answer 5**: the database is marked, the window says so,
        /// and the operator saves as they would any other change. Serving starts at once all the
        /// same, because the gate reads the database in memory.
        ///
        /// **About the authorities the question listed, and no others**, which the answer carries
        /// to `AuthorityService.Keep`: asked there, inside the turn that writes, so a synchronize
        /// that ended while the question stood or between two tries refuses the answer rather than
        /// having it retire an authority nobody was shown.
        /// </remarks>
        public AuthorityChange Keep(PwDatabase database, string fingerprint, IReadOnlyCollection<string> offered)
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
                return AuthorityChange.BehindAnotherWrite("another change is being written");
            }

            try
            {
                AuthorityChange outcome = null;

                bool ran;
                try
                {
                    ran = _ui.Run(() => outcome = KeepOnTheUiThread(database, fingerprint, offered), WriteTimeout);
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

        private AuthorityChange KeepOnTheUiThread(
            PwDatabase database, string fingerprint, IReadOnlyCollection<string> offered)
        {
            var interrupting = Interrupting();
            if (interrupting != null)
            {
                return AuthorityChange.Refused(interrupting);
            }

            // No window is asked about the database as a whole: a keep writes no custom data, so
            // Database Maintenance's copy of it takes nothing back, and it saves nothing, so a
            // save the TAN wizard is inside is not met. What a window can take back is an entry
            // the keep deletes, sets back or protects, asked of each once the plan exists,
            // `STORE.md` §626.
            var kept = new AuthorityService(
                new KdbxStore(database, GateVersion.Current, _log, Holding), _log).Keep(fingerprint, offered);
            if (kept.Succeeded)
            {
                // The tree and the list too: a choice writes entries into the plugin's group.
                database.Modified = true;
                Redraw(database, true);
            }

            return kept;
        }

        /// <summary>
        /// A write of the operator's into the plugin's store, made now on KeePass's own thread,
        /// after the questions every write asks there, `STORE.md` §627: the tab's OK, applied in
        /// memory, the database marked, and saved by the operator's own save.
        /// </summary>
        /// <remarks>
        /// **No lock is taken.** The writer's lock orders the connection threads' writes, each of
        /// which takes a turn on this very thread to make its change; what keeps two writes apart
        /// on this thread is the thread, and a turn never returns into a dialog's own loop before
        /// it ends. A connection's write waiting for its turn waits for this write to end.
        ///
        /// **Refused where KeePass is part way through something of its own**, a merge or a save
        /// among it, a save of this writer's included, or a window holds what the write touches:
        /// the same questions `Keep` asks. Nothing is added then, and the caller names what was not
        /// written. The settings dialog's OK does not meet a save of this writer's: an OK pressed
        /// while such a save pumps under the dialog is handled inside that pump, and the dialog's
        /// close, which makes this write, is raised by the dialog's own loop once the save has
        /// returned (`ModalDialogCloseTests`, `TabOkBesideOwnSaveTests`).
        /// </remarks>
        /// <param name="write">The write, planned and applied against the store as it stands then.</param>
        public StoreWritten ByOperator(PwDatabase database, Func<StoreWritten> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            if (database == null || !database.IsOpen)
            {
                return StoreWritten.Refused("the database is not open");
            }

            var interrupting = Interrupting();
            if (interrupting != null)
            {
                return StoreWritten.Refused(interrupting);
            }

            if (_dialogs != null)
            {
                var open = _dialogs.BlockingRecords(database) ?? _dialogs.BlockingSaves(database);
                if (open != null)
                {
                    return StoreWritten.Refused(open);
                }
            }

            var written = write();
            if (written.Wrote)
            {
                Redraw(database, true);
            }

            return written;
        }

        /// <summary>
        /// Why an entry cannot be written now, a window holding a copy of it, or null, `STORE.md`
        /// §626: what every write into the store asks of each entry it deletes, sets back or
        /// protects. Where no watch was given, which is a test, nothing holds.
        /// </summary>
        internal string Holding(PwEntry entry)
        {
            return _dialogs == null ? null : _dialogs.Blocking(entry);
        }

        /// <summary>
        /// Why every write of the operator's into a store is refused in this session, or null:
        /// the dialogs that would take a write back were not watched from the plugin's load.
        /// </summary>
        /// <remarks>
        /// Asked by the KPG tab before it offers a command, so that a press is never taken that
        /// every OK refuses. Null where this writer was given no watch, which is a test.
        /// </remarks>
        public string Unwritable
        {
            get { return _dialogs != null && !_dialogs.Watching ? GroupDialogs.NotWatched : null; }
        }

        private VaultWrite ChangeOnTheUiThread(
            PwDatabase database, Func<VaultChange> change, bool lists, Action landed)
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

            var saving = _dialogs == null ? null : _dialogs.BlockingSaves(database);
            if (saving != null)
            {
                // Whatever the change is about, because what is refused is the save it ends in:
                // an OK pressed in a dialog that adds to the tree from inside itself is handled
                // part way through that save.
                return VaultWrite.Busy(saving);
            }

            var asking = Asking(database);
            if (asking != null)
            {
                // Before the change is made rather than between the change and the save, so there
                // is nothing to take back out and the refusal costs the database nothing. Nothing
                // between here and `_save` pumps a message, so the two instants are equally close
                // to the save; what is *not* true, and what the guard above is for, is that
                // nothing has pumped to get here. Nor is the save the same instant: `Asking`
                // names what happens inside it before KeePass reads the file again.
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

            Exception failed = null;

            // What the save carried of the standing answers owed to this database, which
            // `BeforeSave` wrote as KeePass began it: kept by the same save, and taken back out with
            // the change when the save does not land.
            Owing owing = null;

            // True until the save ends in a way this method expects: returning, or throwing what
            // `Expected.OfSaving` names.
            var escaped = true;

            try
            {
                Save(database);
                escaped = false;
            }
            catch (Exception error) when (Expected.OfSaving(error))
            {
                // Kept rather than answered, because an exception does not say whether the file
                // was written. The modified flag below is what says that, and it is asked of
                // everything a save and the handlers around it can throw: a filter naming the
                // file's exceptions alone let another plugin's refused access through with the
                // change still made, answered `500` as though nothing had been left behind.
                failed = error;
                escaped = false;
            }
            finally
            {
                owing = _carried;
                if (escaped)
                {
                    // **A defect in the save is settled as an expected failure is, and then goes
                    // on.** `MainForm.SaveDatabase` runs every other plugin's handlers on either
                    // side of writing the file, and KeePass's own window work after it, so an
                    // exception no filter here names leaves this method from inside the save. Left
                    // at that, a change the file never received stayed in the database KeePass
                    // holds, in force and kept by the operator's next save, and a change the file
                    // did receive was never written down: a certificate its client gave up lost its
                    // audit line either way, and a create answered `500` was made again by the
                    // retry. The modified flag decides, as it does for every other ending, and
                    // nothing here catches the exception, which goes on to the connection thread's
                    // catch-all and is counted there.
                    _log.Error("the save ended in an exception nothing here expects, so the change " +
                               "is settled as the database leaves it, and the exception goes on", null);
                    Concluded(database, decided, wasModified, owing, lists, landed, null);
                }
            }

            return Concluded(database, decided, wasModified, owing, lists, landed, failed);
        }

        /// <summary>
        /// What a change comes to once its save has ended: taken back out when nothing reached the
        /// disk, and otherwise left in force, written down and drawn.
        /// </summary>
        /// <remarks>
        /// Asked of the database rather than of how the save ended, so a save that returned, a save
        /// that threw what `Expected.OfSaving` names and a save that threw anything else are
        /// settled alike. <paramref name="failed"/> is the exception of the second, and null for
        /// the other two.
        /// </remarks>
        private VaultWrite Concluded(
            PwDatabase database,
            VaultChange decided,
            bool wasModified,
            Owing owing,
            bool lists,
            Action landed,
            Exception failed)
        {
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

            if (!database.IsOpen)
            {
                // **Closed inside its save**, which an operator's trigger on Saving database file
                // or Saved database file does with Close active database, and another plugin's
                // `FileSavingPre`, `FileSaving` or `FileSaved` handler can do. Never a lock KeePass
                // handles of its own accord: `MainForm.SaveDatabase` pumps only while its block is
                // up, and refuses a lock arriving then. `PwDatabase.Close` clears the flag, the
                // file's hashes and the connection, so the flag above read as a save that worked,
                // whether it had or not, and the hash below would read as verified on its null.
                // Nothing knows whether the file holds the change, which is what this answer is:
                // not taken back out, there being no database left in memory to take it out of,
                // and the standing answers the save carried kept owed.
                //
                // **Written down as a change left in place only when KeePass reported a save of it
                // that landed**, which a close after the file was written follows and a close
                // before it does not: a close that discards the changes leaves no database holding
                // the change and a file without it, and KeePass's save then fails on the closed
                // database and raises `FileSaved` as a failure. A line written then stated a
                // revocation that nothing held, over a certificate the next open admits again.
                _log.Error("the database closed as its save ended, so whether the file holds the " +
                           "change is not known", failed);

                if (landed != null && _ownSaveLanded)
                {
                    landed();
                }

                return VaultWrite.Unverified("the database closed as its save ended");
            }

            if (failed != null)
            {
                // **An exception out of `_save` with nothing unsaved left is not a save that
                // failed.** `PwDatabase.Save` clears that flag as the last thing it does, and
                // `MainForm.SaveDatabase` catches everything the save itself throws; what is left
                // to throw is what runs after it, outside that try, with the file already written:
                // its window redrawn, which raises `UIStateUpdated` to every other plugin loaded,
                // its `FileSaved` event and the triggers. So an exception here means one of those
                // failed after this change reached the disk, and taking the change back out over
                // it would leave the database in
                // memory disagreeing with the file KeePass just wrote: the client is told `500 not
                // saved` and retries, the next unlock reads the file and brings the first copy
                // back beside the second, and the operator's next save writes the undone state
                // over the top instead. Decided by the flag rather than by the exception's type
                // for the same reason every other question here is asked of the database: the type
                // says which code threw, and the flag says what happened to the file.
                _log.Error("the change was saved and something KeePass runs after its save " +
                           "failed, another plugin's handler, a trigger or KeePass's own window: " +
                           "the change is on the disk and is being left there", failed);
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
                // stored; the clients page is about who is refused, which this decides. **The
                // standing answers the save carried stay owed**: whether the file holds them is what
                // this path does not know, and the next save that lands writes them from memory.
                if (landed != null)
                {
                    landed();
                }

                return VaultWrite.Unverified(
                    "the file on disk does not hold what KeePass wrote to it");
            }

            // The line before the window. `Redraw` is KeePass's `UpdateUI`, which raises
            // `UIStateUpdated` to every other plugin loaded, and while the window was redrawn
            // first, a defect there reached the connection thread with a client's revocation of
            // itself saved, verified and never written down. A listener's defect here leaves the
            // window as it was, which is a repaint an operator can ask for again.
            Owing.Settle(owing, Owed);
            if (landed != null)
            {
                landed();
            }

            // Last, and the only place the tree and the list are asked for, when the change is one
            // they show. Every path above either changed nothing or has just put its change back,
            // and in both cases they already show the database as it now is: neither is rebuilt
            // between the change and the undo, since nothing on this thread reads a `PwGroup` again
            // until something asks it to. What a put back does leave wrong is the mark that there
            // is something to save, which `PutBack` redraws on its own, from the flag it leaves.
            Redraw(database, lists);

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
                    }
                    else
                    {
                        LeftUnsaved(what, cause);
                    }

                    // Drawn from the flag as it now stands, whichever branch set it. A save that
                    // got as far as writing ended in `UpdateUIState` with the change still in,
                    // and a window left showing that sends the operator looking for a change the
                    // client was told did not happen, or saving to keep it. A save KeePass
                    // abandoned before writing, at its own look at the file or at a cancel from a
                    // `FileSavingPre` or `FileSaving` handler, returned without drawing at all,
                    // and a flag left up then shows nothing to save over what may be the only
                    // undamaged copy.
                    Redraw(database, false);
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
        /// The standing answers owed to databases, written by every save KeePass makes of the
        /// database each belongs to, or null for none.
        /// </summary>
        /// <remarks>
        /// Settable rather than a constructor argument because one plugin builds one of each and
        /// tests build writers with nothing owed. `SessionRefusals` is the one there is.
        /// </remarks>
        public IOwedAnswers Owed { get; set; }

        /// <summary>
        /// Whether the calling thread is inside a merge KeePass is running, which is
        /// `MergeWatch.Over` for KeePass's own merge method, or null where there is nothing to ask.
        /// </summary>
        /// <remarks>
        /// Settable for the reason <see cref="Owed"/> is. It is the question `ControlUiThread`
        /// asks before it serves a turn, asked here for the write that takes none.
        /// </remarks>
        public Func<bool> Merging { get; set; }

        /// <summary>
        /// Writes every standing answer owed to this database into it, and says what it wrote and
        /// what was there before, so that a write that does not land takes them back out.
        /// </summary>
        /// <remarks>
        /// **Only where the answer belongs**: the database in which the authority it was given
        /// under stands, and while a record of kind `client` in force names the certificate it is
        /// about, whether or not that admission admits now, as `RenewalAnswers.Prune` keeps an
        /// answer. An answer no such record names is settled without being written, since nothing
        /// is left for it to refuse, and so is an answer whose authority this database has retired.
        ///
        /// **And only while it is still the later word**: while the key holds what it held when the
        /// answer was given, `OwedAnswer.Baseline`, and while the answer still refuses. Written
        /// whatever the key held, an owed answer went over the operator's newer answer, in the very
        /// write that had just put that answer there, and over an answer a synchronize brought in;
        /// and an answer until a date whose day had begun was written to refuse nothing. Both are
        /// let go unwritten. **A failure here fails nothing**: the save it rides on goes ahead, the
        /// answers stay owed, and the next save tries again.
        /// </remarks>
        private Owing Owe(PwDatabase database)
        {
            var owed = Owed == null ? null : Owed.Owed();
            if (owed == null || owed.Count == 0)
            {
                return null;
            }

            var owing = new Owing(new CustomDataStore(database));

            // Which authorities the store retired and which stand, and which certificates it
            // admits, from a reading let go at the end of this; no answer is written where the
            // store does not tell.
            using (var reading = new KdbxStore(database, GateVersion.Current, _log).Read())
            {
                ISet<string> retired = null;
                ISet<string> standing = null;
                var named = StoreAdmissions.Named(reading, null);
                if (reading.InForce != null)
                {
                    retired = new HashSet<string>(reading.InForce.Taken(StoreKinds.Retired), StringComparer.Ordinal);
                    standing = new HashSet<string>(
                        reading.InForce.Authorities.Select(record => record.Form.Member(StoreKinds.Id)), StringComparer.Ordinal);
                }

                try
                {
                    var answers = new RenewalAnswers(owing.Store, _log);
                    foreach (var answer in owed)
                    {
                        if (retired != null && retired.Contains(answer.DatabaseId))
                        {
                            // Given under an authority this database has since retired. A recreate
                            // or a delete deletes every admission it issued, so nothing is left for
                            // the answer to refuse, and a certificate that authority issued is
                            // refused before any renewal can ask about it. Skipped, it stayed owed
                            // for the session and was counted at exit as an answer whose client
                            // would be asked about again.
                            owing.Settled.Add(answer);
                            continue;
                        }

                        if (standing == null || !standing.Contains(answer.DatabaseId))
                        {
                            // Owed to another database, which never held the authority, or to a
                            // database whose store does not tell which authorities stand: kept
                            // owed.
                            continue;
                        }

                        if (!named.Contains(answer.AdmissionKey))
                        {
                            // No record of kind `client` in force names its certificate, which is
                            // when the prune takes an answer away: an admission that admits nobody
                            // for now, in a store held back, under several authorities or beside
                            // a second admission of its certificate, keeps its answer.
                            owing.Settled.Add(answer);
                            continue;
                        }

                        if (!RenewalRefusal.Same(answers.StandingRefusal(answer.AdmissionKey), answer.Baseline)
                            || !answer.Refusal.Refuses(DateTimeOffset.UtcNow))
                        {
                            owing.Settled.Add(answer);
                            continue;
                        }

                        var key = AuthorityKeys.RenewalRefusalKey(answer.AdmissionKey);
                        if (!owing.Before.ContainsKey(key))
                        {
                            owing.Before[key] = owing.Store.Get(key);
                        }

                        answers.RefuseRenewal(answer.AdmissionKey, answer.Refusal);
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

        /// <summary>What an admission's write is held to, read in its own turn.</summary>
        private sealed class Ground : IWriteGround
        {
            private readonly StoreReading _reading;
            private ProfileNames _named;

            public Ground(StoreReading reading)
            {
                _reading = reading;
            }

            public AdmissionRecord Admission(string admissionKey)
            {
                return StoreAdmissions.Admitted(_reading, admissionKey);
            }

            /// <summary>Whether a name is on the list of this turn's reading, worked out once for the condition.</summary>
            public bool Named(string profile)
            {
                if (_named == null)
                {
                    _named = ProfileNames.Of(_reading);
                }

                return _named.Holds(profile);
            }
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
        /// The certificates the admissions in force name once this write lands, against which the
        /// standing answers to a renewal are pruned, `RenewalAnswers.Prune`.
        /// </summary>
        /// <remarks>
        /// **Here, inside the guarded write, and never at open**: opening a database must not mark
        /// it modified, and a prune made while Database Maintenance is open is undone by that
        /// dialog's write-back. Nothing depends on it, since no standing answer admits anybody, so
        /// one that fails is a warning and the write goes on.
        /// </remarks>
        private IReadOnlyList<KeyValuePair<string, string>> PruneAnswers(
            CustomDataStore store, StoreReading reading, StoreWrite write)
        {
            return new RenewalAnswers(store, _log).Prune(StoreAdmissions.Named(reading, write));
        }

        /// <summary>
        /// Why a write meets this word, for the log: several authorities standing, or the store
        /// holding the gate back with the reading's own reason, which a store that is whole has
        /// none of.
        /// </summary>
        private static string HeldBack(string word, StoreReading reading)
        {
            return HeldBackTarget.Why(word) + (reading.Reason == null ? string.Empty : " (" + reading.Reason + ")");
        }

        private AdmissionWrite OnTheUiThread(
            PwDatabase database, AdmissionRecord record, Action audit, Func<IWriteGround, string> condition)
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
            var kdbx = new KdbxStore(database, GateVersion.Current, _log, Holding);

            // The store read afresh with every entry, held for the whole turn and let go at its
            // end, which overwrites the key, `STORE.md` §627.
            using (var reading = kdbx.ForWrite())
            {
                // The store's word where it holds the gate back, `STORE.md` §656, as a connection
                // is answered, `lost` among them for a file whose key went while this database was
                // served, and `undecided` where several authorities stand, the record's issuer
                // among them: before anything is planned, asked or spent.
                var word = StoreReadings.WordOf(reading, record.Issuer);
                if (word != null)
                {
                    _log.Warn("an admission was not written, since " + HeldBack(word, reading));
                    return AdmissionWrite.Unavailable(word);
                }

                var act = AdmissionActs.Admit(reading, record);
                if (act == null)
                {
                    return AdmissionWrite.Failed("the database holds no store to write the admission into");
                }

                StagedWrite staged;
                try
                {
                    staged = kdbx.Stage(reading, new[] { act });
                }
                finally
                {
                    act.Forget();
                }

                if (staged.Held != null)
                {
                    // A window holds an entry this write deletes, sets back or protects again,
                    // `STORE.md` §626, so this is the window's for as long as it stands, and worth
                    // asking again. A plan that is refused asks no window.
                    return AdmissionWrite.Busy(staged.Held);
                }

                if (!AdmissionActs.AddsAdmission(staged.Write))
                {
                    // Refused before the condition spends anything: a write that adds no admission
                    // is never believed, whatever its save then reports. **An act the plan leaves
                    // out because its issuer is not the authority the store serves is told apart**,
                    // `PAIRING.md` §311: with a store holding the gate back and several authorities
                    // standing, the issuer among them, answered above, the issuer was retired since
                    // the operator answered, by a Recreate, a Delete or a copy merged in, and no
                    // retry under it can be written, whatever stands beside it.
                    var why = staged.Refused ?? "the write adds no admission";
                    _log.Warn("an admission was not written into the plugin's store: " + why);
                    return string.Equals(StoreAuthorities.Served(reading), record.Issuer, StringComparison.Ordinal)
                        ? AdmissionWrite.Failed(why)
                        : AdmissionWrite.Unserved(why);
                }

                if (condition != null)
                {
                    // Last of the questions and first of the changes: everything above can still be
                    // busy, which keeps an approval held, and nothing below can. So what the
                    // condition spends, an approval or an invitation, is spent only on a write that
                    // is going ahead, and what it compares, read from the store in this very turn,
                    // cannot move before the write that relies on it. A refusal is answered as a
                    // failure, since the caller that gave the condition knows which part refused.
                    var withdrawn = condition(new Ground(reading));
                    if (withdrawn != null)
                    {
                        return AdmissionWrite.Failed(withdrawn);
                    }
                }

                var wasModified = database.Modified;

                // What the application and the prune took, so a write that does not land puts it
                // back and the database in memory goes on agreeing with the file: the admission and
                // the profile names it adds are entries of the plan, and its undo takes them back
                // out. The entries first: an application that throws has taken itself back, so the
                // standing answers are pruned only beside entries that are there.
                var undone = staged.Apply();
                IReadOnlyList<KeyValuePair<string, string>> pruned = null;
                Owing owing = null;

                try
                {
                    pruned = PruneAnswers(store, reading, staged.Write);
                    database.Modified = true;
                }
                catch (Exception error) when (Expected.OfCustomData(error))
                {
                    _log.Error("an admission could not be put in the database", error);
                    return Undo(store, database, undone, pruned, owing, wasModified, error.Message);
                }

                // Everything this write touched, put back as it was, for every ending below that
                // leaves the admission out of the database. `raised` keeps the flag up whatever the
                // file then reads as, for the one path that raises it on purpose.
                Func<string, bool, AdmissionWrite> undo = (reason, raised) => Undo(
                    store, database, undone, pruned, owing, wasModified || raised, reason);
                var added = staged.Write.Add;

                // True until the save ends in a way this method expects, as in
                // `ChangeOnTheUiThread`.
                var escaped = true;

                try
                {
                    Save(database);
                    escaped = false;
                }
                catch (Exception error) when (Expected.OfSaving(error))
                {
                    escaped = false;
                    owing = _carried;

                    // KeePass's own save and every other plugin's handlers around it, which can
                    // fail for every reason a file can and every reason a window can:
                    // `Expected.OfSaving` is that list. What it must not do is take the connection
                    // thread with it.
                    //
                    // **Which of those it was is the database's answer rather than the
                    // exception's**, for the reason `ChangeOnTheUiThread` gives: the save clears
                    // the modified flag as the last thing it does, and `MainForm.SaveDatabase`
                    // catches everything the save itself throws, so an exception arriving with
                    // nothing unsaved left is something KeePass runs after its save failing after
                    // the admission reached the disk. Undoing there would refuse the client its
                    // certificate while the admission stays in the file, which is the client on the
                    // clients page that never existed that `Undo` exists to prevent, arriving by
                    // the other road.
                    if (database.IsOpen && database.Modified)
                    {
                        _log.Error("the database could not be saved after an admission was written", error);
                        return undo(error.Message, false);
                    }

                    _log.Error("the admission was saved and something KeePass runs after its save " +
                               "failed, another plugin's handler, a trigger or KeePass's own window: " +
                               "the admission is on the disk and is being left there", error);
                }
                finally
                {
                    owing = _carried;
                    if (escaped)
                    {
                        // A defect in the save, settled as `ChangeOnTheUiThread` settles a defect
                        // in its own save and for its reason: an admission the file never received
                        // is taken back out, and an admission the file did receive is written down
                        // before the exception goes on, to end the pairing or the renewal with no
                        // certificate handed over.
                        _log.Error("the save ended in an exception nothing here expects, so the admission " +
                                   "is settled as the database leaves it, and the exception goes on", null);
                        RecordConcluded(database, added, owing, audit, undo);
                    }
                }

                return RecordConcluded(database, added, owing, audit, undo);
            }
        }

        /// <summary>
        /// What an admission comes to once its save has ended: taken back out unless it is in the
        /// database and in the file, and written down when it is.
        /// </summary>
        /// <remarks>
        /// Asked of the database rather than of how the save ended, for the reason `Concluded`
        /// gives. **Read back by its own entries**, `STORE.md` §627: each entry the write added is
        /// in the tree under its identifier with the strings it was composed with. A whole reading
        /// would add a decision that arrived inside the save, which the client's next request is
        /// answered by, and would walk every entry on KeePass's thread a second time.
        /// </remarks>
        private AdmissionWrite RecordConcluded(
            PwDatabase database,
            IReadOnlyList<StoreRecord> added,
            Owing owing,
            Action audit,
            Func<string, bool, AdmissionWrite> undo)
        {
            if (!database.IsOpen)
            {
                // Closed inside its save: nothing is left in memory to take the admission back out
                // of, and nothing knows whether the file holds it, so no certificate is handed
                // over. The standing answers the save carried stay owed.
                _log.Error("the database closed as its save ended, so whether the file holds the " +
                           "admission is not known, and no certificate is handed over", null);
                return AdmissionWrite.Failed("the database closed as its save ended");
            }

            if (!StoreEntries.Holds(database, added))
            {
                return undo("the admission did not survive being written", false);
            }

            if (database.Modified)
            {
                // KeePass tells a caller nothing about whether a save worked. This is what it does
                // say: a database it has saved has no unsaved changes left.
                return undo("the database still has unsaved changes, so nothing is on disk", false);
            }

            if (!TheFileHoldsWhatWasWritten(database))
            {
                // The read back above is of memory. This is the file, and it is the last thing
                // between the admission and a certificate: an admission that may not be on the disk
                // is a credential nobody can see and nobody can revoke, which is the one thing this
                // whole class exists to prevent. Undone rather than left where the API's write
                // leaves it, because refusing a pairing costs an invitation and admitting one that
                // was never stored costs a client nobody can take away.
                //
                // The flag goes up whatever it was before, and stays up, which is more than `Undo`
                // does on its own: a database that arrived with unsaved edits has just had them
                // written to a file that did not keep them either, so this is the one path where
                // leaving it clear would be wrong about somebody else's work as well as about this
                // admission.
                database.Modified = true;

                return undo("the file on disk does not hold what KeePass wrote to it", true);
            }

            Owing.Settle(owing, Owed);
            Landed(audit);

            // Last, as for every change that lands: the admission and its summary entry are
            // entries of the plugin's group, so the tree and the list are out of date, `DESIGN.md`
            // §119.
            Redraw(database, true);
            return AdmissionWrite.Done();
        }

        /// <summary>
        /// Writes the audit line for an admission or a certificate given up that landed, and then
        /// tells whatever is drawn from the store that it landed.
        /// </summary>
        /// <remarks>
        /// **The line first, and here, so that nothing after it can lose it.** An approved
        /// pairing, an approved renewal and a certificate its own client gives up are each written
        /// to the audit once their change is in force. The operation used to write its line once
        /// this write had returned, and a defect in a listener below went past every filter on the
        /// way to the connection thread's catch-all, so a change that was on the disk was never
        /// written down. Written here, on the path that leaves the change in force and before any
        /// listener runs, the line is in the audit however the listener ends, and nothing new
        /// catches everything to keep it there. Before KeePass's window is redrawn too, which
        /// raises an event to every other plugin, and on the path a defect in the save itself
        /// takes, which a `finally` settles as the database leaves it: a change the file received
        /// is written down there, and a change it never received is taken back out.
        ///
        /// **Once at most.** Each path that leaves a change in force runs this once, and a path
        /// that took the change back out or never made it does not run it at all, so an operation
        /// writes no line of its own about a change it handed here. A database closed inside its
        /// save leaves the change in force nowhere unless KeePass reported a save of it that
        /// landed, and runs this only then.
        ///
        /// **And never waiting**, because this is KeePass's own thread inside the writer's lock.
        /// The line is written without waiting for the client's turn, and the operation takes that
        /// turn once the write returns, on the client's own connection, as `AdmissionAudit` says.
        /// Nor for the file: the audit queues a line recorded on this thread for a writer of its
        /// own, `JsonLinesAudit.Record`. An audit swallows what it cannot write, so a full disk
        /// fails nothing here either.
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
        /// Tells whatever is drawn from the store that an admission landed, or a certificate its
        /// own client gave up, or a standing answer to a renewal.
        /// </summary>
        /// <remarks>
        /// Here and nowhere earlier: every path above this either put its change back or is still
        /// deciding, and a page told about an admission that was taken out again would list a
        /// client the next request refuses. A certificate given up reaches it from the end of a
        /// change, on the two paths that leave the change in force: saved and verified, and saved
        /// without the file being verified, which takes nothing back out.
        ///
        /// **A page that fails to redraw never fails the write.** By the time this runs the
        /// admission is on the disk and the client is about to be handed its certificate, so a
        /// page that failed to redraw is a page an operator can bring forward again; failing the
        /// pairing over it would spend the invitation and give nothing back. What is caught is what
        /// drawing can throw, the list `Expected.OfUserInterface` keeps. Anything else is a defect
        /// in the listener and is not caught here, because this is no kind of catch-all
        /// `DESIGN.md` §141 lists: it reaches the connection thread's, which counts it, and the
        /// pairing ends with its admission saved and no certificate handed over. A renewal ends the
        /// same way, and a certificate given up ends in force and answered `500`. In all three the
        /// audit line is already written, by <see cref="Landed"/> before this ran, so the change on
        /// the disk is in the audit as well, and the incident the catch-all counts records why its
        /// answer was a failure. The tab listens only once it is whole, so its listener cannot be
        /// told about a page half built.
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
        /// Takes an admission's write back out of the database, and answers with why the write is
        /// being refused.
        /// </summary>
        /// <remarks>
        /// The caller is about to be told the admission did not land, and the certificate it was
        /// for is about to be destroyed unsent. Leaving the admission in the database anyway would
        /// make that a lie the operator finds later: a client on the clients page that never
        /// existed, admitted by a certificate nobody holds, written to disk by whatever they save
        /// next.
        ///
        /// **Everything the write applied goes, in this order**: the standing answers owed that the
        /// save carried; the entries, by the undo the application handed back, which removes what
        /// it added with no deletion record, the admission and the profile names it adds among
        /// them, puts back what it deleted and removes those deletion records, and removes the
        /// deletion records it added and a group it made; and the standing answers the prune took
        /// away.
        ///
        /// The modified flag goes back only if this write is what set it. Clearing one that was
        /// already there would tell KeePass it has nothing to save while somebody else's change is
        /// still waiting, and clearing one over a file the failed save damaged would tell it there
        /// is nothing to save while the only undamaged copy is the one in memory.
        /// <see cref="TheFileIsAsKeePassLeftIt"/> is that second question, in the same words as
        /// `PutBack` asks it. A file that does not hold what was written is undone with the flag
        /// kept up whatever it was, for the reason `RecordConcluded` gives there.
        /// </remarks>
        private AdmissionWrite Undo(
            CustomDataStore store,
            PwDatabase database,
            Action undone,
            IReadOnlyList<KeyValuePair<string, string>> pruned,
            Owing owing,
            bool wasModified,
            string reason)
        {
            try
            {
                TakeBackOwed(owing);

                if (undone != null)
                {
                    undone();
                }

                PutBackAnswers(store, pruned);

                if (!wasModified)
                {
                    string what;
                    if (TheFileIsAsKeePassLeftIt(database, out what))
                    {
                        database.Modified = false;
                    }
                    else
                    {
                        // Logged, as `PutBack` logs it: the database is left marked unsaved with
                        // no change in it, and the operator is otherwise told only that the write
                        // failed.
                        LeftUnsaved(what, null);
                    }

                    // The title bar and the save button only, from the flag as it now stands, for
                    // the reason `PutBack` gives: a save that failed may have drawn the database
                    // with the admission still in, or not drawn it at all.
                    Redraw(database, false);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("an admission that did not land could not be taken back out again", error);
            }

            return AdmissionWrite.Failed(reason);
        }
    }
}
