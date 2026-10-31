using System;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What the single writer answers when the save does not go through.
    /// </summary>
    /// <remarks>
    /// Two subjects here. The first is a caller being told something untrue about its own request,
    /// which is worse than being told nothing: one had a change that ran and was saved reported as
    /// "try again", and the other had a change that never reached the disk reported as a route that
    /// does not exist. A client acting on either answer makes a second copy of what it just wrote.
    /// Neither could be reached through the fakes the unit tier has. `FakeUiThread` runs the work
    /// and answers true, or answers false without running it; the case that matters is the third
    /// one, which a real user interface thread produces when the work was already running.
    ///
    /// The second is a save that would stop and ask somebody. `MainForm.SaveDatabase` has three
    /// questions that can be seen coming, `DESIGN.md` §119 says no API request may raise a dialog of
    /// KeePass's own that could have been foreseen, and these are the three refusals that keep that
    /// true. Two cases that must still go through are here beside them, because a check on the
    /// file is one wrong comparison away from refusing every write there is.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class DatabaseWriterTests
    {
        private TemporaryDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = TemporaryDatabase.New();
        }

        [TearDown]
        public void TearDown()
        {
            _database.Dispose();
        }

        [Test]
        public void A_change_that_ran_late_is_answered_rather_than_called_busy()
        {
            var saves = 0;
            var writer = Writer(new LateUiThread(), db => { saves++; db.Modified = false; });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(saves, Is.EqualTo(1), "the work ran and the database was saved");
            Assert.That(outcome.IsBusy, Is.False,
                "so telling the caller to retry would have it create the same thing twice");
            Assert.That(outcome.Ok, Is.True);
            Assert.That(outcome.Uuid, Is.EqualTo("abcd"), "and it learns what it made");
        }

        [Test]
        public void A_change_the_save_did_not_take_is_not_reported_as_a_missing_route()
        {
            // `SaveDatabase` shows its own warning and returns, so this is the live shape of a
            // failed save rather than the exception one.
            var writer = Writer(new RunsUiThread(), db => { });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.Ok, Is.False);
            Assert.That(outcome.IsUnsaved, Is.True, "the change was made and is not on the disk");
            Assert.That(outcome.IsBusy, Is.False);
            Assert.That(outcome.IsConflict, Is.False);
        }

        [Test]
        public void A_save_that_throws_says_the_same_thing()
        {
            var writer = Writer(new RunsUiThread(), db => { throw new System.IO.IOException("the share went away"); });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.Ok, Is.False);
            Assert.That(outcome.IsUnsaved, Is.True);
        }

        [Test]
        public void A_change_the_save_did_not_take_is_put_back_rather_than_left_in_the_database()
        {
            // The half that matters to the database rather than to the caller. Told the write did
            // not land, the client stops; the operator's next save would otherwise commit it
            // anyway, under a `uuid` nobody was given and beside whatever a retry already made.
            var undone = 0;
            var writer = Writer(new RunsUiThread(), db => { });

            // Stated rather than assumed: a database that arrives dirty stays dirty, which the test
            // below is about, and this one is about the other case.
            _database.Database.Modified = false;

            var outcome = writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => undone++));

            Assert.That(outcome.IsUnsaved, Is.True);
            Assert.That(undone, Is.EqualTo(1), "the change is taken back out");
            Assert.That(_database.Database.Modified, Is.False,
                "and the database is no longer holding one");
        }

        [Test]
        public void A_save_the_file_did_not_keep_is_not_reported_to_the_client_as_saved()
        {
            // `PwDatabase.Save` clears `Modified` as soon as it has committed the bytes, and
            // `PostSavingEx` only then hashes the file back and compares it with what was written.
            // When those differ KeePass warns the operator that writing has failed and does
            // nothing else: it throws nothing and it leaves the flag clear. So the flag alone says
            // a change landed over a file that may not hold it, and a client told `201` with an
            // identifier records success and never asks again. A sync client putting its own copy
            // back is what this is, and failing storage has the same shape.
            _database.Save();
            var theCopyAsItWas = System.IO.File.ReadAllBytes(_database.Path);

            var undone = 0;
            var redraws = 0;
            var writer = Writer(
                new RunsUiThread(),
                db =>
                {
                    db.Save(null);
                    System.IO.File.WriteAllBytes(_database.Path, theCopyAsItWas);
                },
                (db, lists) => redraws++);

            var outcome = writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => undone++));

            Assert.That(outcome.Ok, Is.False, "a client told this was saved would never ask again");
            Assert.That(outcome.IsUnverified, Is.True, outcome.Reason);
            Assert.That(outcome.IsUnsaved, Is.False,
                "and not that one, which says the change was taken back out");
            Assert.That(undone, Is.Zero,
                "nothing knows which of the two the file holds, so undoing is as likely to be wrong");
            Assert.That(redraws, Is.Zero, "and the window is not told about a change that may not be stored");
            Assert.That(_database.Database.Modified, Is.True,
                "so KeePass asks before a lock, a close or an exit discards the copy that does hold it");
        }

        [Test]
        public void A_save_that_wrote_the_file_and_then_threw_is_left_where_it_landed()
        {
            // `MainForm.SaveDatabase` catches everything `PwDatabase.Save` throws and then, outside
            // that try and with the file already written, raises `FileSaved` to every other plugin
            // loaded. An exception from there arrives at this call with the change on the disk and
            // `Modified` already cleared, and a sync or backup plugin failing to copy the file it
            // has just been told about throws exactly the `IOException` the filter here was reading
            // as a save that did not happen. Taking the change back out over that leaves memory
            // disagreeing with the file: the client retries, and the next unlock reads the file and
            // brings the first copy back beside the second.
            var undone = 0;
            var writer = Writer(new RunsUiThread(), db =>
            {
                db.Modified = false;
                throw new System.IO.IOException("a FileSaved handler failed after the write");
            });

            var outcome = writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => undone++));

            Assert.That(outcome.Ok, Is.True, outcome.Reason);
            Assert.That(outcome.Uuid, Is.EqualTo("abcd"), "and the client learns what it made");
            Assert.That(undone, Is.Zero, "the change is on the disk, so there is nothing to take back");
        }

        [Test]
        public void A_failed_save_that_damaged_the_file_leaves_the_database_saying_it_has_unsaved_changes()
        {
            // The put-back makes the database in memory what it was. It does not make the file on
            // disk what it was, and KeePass only promises that for a transacted write committed by
            // an atomic move: through a symbolic link, with file transactions off, on a share that
            // has gone or over ftp it writes the file directly, truncating it first. A save that
            // stops part way there leaves a shorter file that still opens, and `Modified` is the
            // one flag that makes KeePass's lock, close and exit ask before they throw away the
            // copy in memory, which is now the only undamaged one.
            FillWithEntriesAndSave();
            _database.Database.UseFileTransactions = false;

            var writer = Writer(new RunsUiThread(), db =>
            {
                // What `MainForm.SaveDatabase` does with it: a warning of its own, and a
                // return that tells its caller nothing.
                _ = Thrown.By(() => db.Save(new CancelsPartWay()));
            });

            var outcome = writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => { }));

            Assert.That(outcome.IsUnsaved, Is.True, outcome.Reason);
            Assert.That(System.IO.File.Exists(_database.Path), Is.True);
            Assume.That(
                KeePass.Util.WinUtil.HashFile(_database.Database.IOConnectionInfo),
                Is.Not.EqualTo(_database.Database.HashOfFileOnDisk).Using<byte[]>(KeePassLib.Utility.MemUtil.ArraysEqual),
                "the save has to have reached the file for this to be about anything");
            Assert.That(_database.Database.Modified, Is.True,
                "so KeePass asks before its next lock, close or exit discards the only copy that " +
                "still holds everything");
        }

        [Test]
        public void A_failed_save_that_left_the_file_alone_still_says_there_is_nothing_unsaved()
        {
            // The other half, and the one that says this is not simply refusing to clear the flag.
            // A transacted save that never touched the file, and a save that threw before it wrote
            // anything, leave a file that is exactly what KeePass read; the change has been taken
            // back out, so a database claiming unsaved work would put an asterisk in the title bar
            // and a question on every lock over nothing at all.
            _database.Save();
            _database.Database.Modified = false;

            var writer = Writer(new RunsUiThread(), db => { });

            var outcome = writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => { }));

            Assert.That(outcome.IsUnsaved, Is.True);
            Assert.That(_database.Database.Modified, Is.False);
        }

        [Test]
        public void A_database_that_was_already_dirty_stays_dirty()
        {
            // Somebody else's unsaved work is not this request's to clear. Only a call that made
            // the database dirty may make it clean again.
            _database.Database.Modified = true;
            var writer = Writer(new RunsUiThread(), db => { });

            writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => { }));

            Assert.That(_database.Database.Modified, Is.True);
        }

        [Test]
        public void A_change_that_carries_no_way_back_is_still_answered_honestly()
        {
            // A refusal converts to a change with no undo, and so does anything that did not say
            // how to reverse itself. There is nothing to take back, and the caller is told the
            // same thing either way.
            var writer = Writer(new RunsUiThread(), db => { });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.IsUnsaved, Is.True);
        }

        [Test]
        public void A_change_that_landed_tells_the_window_it_is_out_of_date()
        {
            // KeePass's own save ends in `UpdateUIState`, which redraws the title bar and the menus
            // and rebuilds neither the group tree nor the entry list. So without this the operator
            // is looking at the database as it was before the request, with no way to know it: an
            // entry created through the API is not in the list, one deleted is still in it, and one
            // renamed still reads as it did, until something they do rebuilds the window.
            var redraws = 0;
            PwDatabase told = null;
            var rebuilt = false;
            var writer = Writer(
                new RunsUiThread(),
                db => db.Modified = false,
                (db, lists) =>
                {
                    redraws++;
                    told = db;
                    rebuilt = lists;
                });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.Ok, Is.True);
            Assert.That(redraws, Is.EqualTo(1));
            Assert.That(rebuilt, Is.True, "the tree and the list are what changed");
            Assert.That(told, Is.SameAs(_database.Database),
                "and the window is told which database, since it rebuilds only the one in front: a " +
                "write to a database in another tab would take the operator's search results away " +
                "in one they are looking at");
        }

        [Test]
        public void A_change_that_never_happened_rebuilds_nothing()
        {
            // Two ways for nothing to have happened, and neither is worth a rebuild: a change the
            // vault refused, and one whose save did not land and was taken back out. In both the
            // tree and the list already show the database as it now is, and rebuilding would cost
            // an operator their entry list for a request that changed nothing.
            var redraws = 0;
            var rebuilds = 0;
            Action<PwDatabase, bool> window = (db, lists) =>
            {
                redraws++;
                rebuilds += lists ? 1 : 0;
            };

            var refused = Writer(new RunsUiThread(), db => db.Modified = false, window);
            Assert.That(
                refused.Change(_database.Database, () => VaultWrite.Conflict("no")).Ok, Is.False);
            Assert.That(redraws, Is.Zero, "a refusal touched nothing, so nothing on screen is wrong");

            var unsaved = Writer(new RunsUiThread(), db => { }, window);
            Assert.That(
                unsaved.Change(
                    _database.Database,
                    () => new VaultChange(VaultWrite.Created("abcd"), () => { })).IsUnsaved,
                Is.True);

            Assert.That(rebuilds, Is.Zero);
        }

        [Test]
        public void A_change_that_found_nothing_to_change_is_answered_without_a_save()
        {
            // An update naming only what the node already holds. Unlike a refusal it is answered as
            // having worked, so the writer cannot tell from the outcome that there is nothing to
            // save, and `VaultChange.Unchanged` is how it is told.
            var saves = 0;
            var redraws = 0;
            var writer = Writer(
                new RunsUiThread(),
                db =>
                {
                    saves++;
                    db.Modified = false;
                },
                (db, lists) => redraws++);

            _database.Database.Modified = false;
            var clean = writer.Change(_database.Database, () => VaultChange.Unchanged(VaultWrite.Done()));

            Assert.That(clean.Ok, Is.True);
            Assert.That(_database.Database.Modified, Is.False, "nothing is waiting to be saved");

            // A database holding an edit of the operator's own, which a write that landed would have
            // saved along with it because KeePass saves the whole database.
            _database.Database.Modified = true;
            var dirty = writer.Change(_database.Database, () => VaultChange.Unchanged(VaultWrite.Done()));

            Assert.That(dirty.Ok, Is.True);
            Assert.That(_database.Database.Modified, Is.True, "the operator's edit is still theirs to save");
            Assert.That(saves, Is.Zero);
            Assert.That(redraws, Is.Zero, "and nothing on screen is out of date");
        }

        [Test]
        public void A_change_taken_back_out_redraws_the_mark_saying_there_is_something_to_save()
        {
            // `MainForm.SaveDatabase` ends a failed save in `UpdateUIState` while the change is
            // still in, so its title bar and save button say the database has something to save.
            // The put back then clears the flag, and a window left as it was keeps an asterisk for
            // a change the client was told did not happen, until something else redraws it.
            _database.Database.Modified = false;

            var redraws = 0;
            var rebuilt = true;
            var writer = Writer(
                new RunsUiThread(),
                db => { },
                (db, lists) =>
                {
                    redraws++;
                    rebuilt = lists;
                });

            var outcome = writer.Change(
                _database.Database, () => new VaultChange(VaultWrite.Created("abcd"), () => { }));

            Assume.That(outcome.IsUnsaved, Is.True);
            Assume.That(_database.Database.Modified, Is.False);
            Assert.That(redraws, Is.EqualTo(1), "the flag went back, so the window is told");
            Assert.That(rebuilt, Is.False, "and only that: the tree and the list never showed the change");
        }

        [Test]
        public void A_window_that_will_not_redraw_does_not_unsay_a_write_that_landed()
        {
            // The change is made and the file is written by the time the window is told, so a
            // failure here cannot be reported as one: a client told its create failed creates a
            // second one, under an identifier it was never given. A dialog closing underneath the
            // marshaled call is how a real one arrives.
            var writer = Writer(
                new RunsUiThread(),
                db => db.Modified = false,
                (db, lists) => { throw new ObjectDisposedException("the window went away"); });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.Ok, Is.True);
            Assert.That(outcome.Uuid, Is.EqualTo("abcd"));
        }

        [Test]
        public void A_file_that_changed_underneath_is_refused_rather_than_asked_about()
        {
            // `MainForm.SaveDatabase` calls `PreSaveValidate`, which compares `WinUtil.HashFile`
            // with `PwDatabase.HashOfFileOnDisk` and, when they differ, puts a Synchronize,
            // Overwrite or Cancel question on the screen. That is a window a program asked for,
            // which `DESIGN.md` §119 forbids, and it holds this thread, the writer's lock and the
            // connection until a person answers it. The database on a shared or synced path is
            // where that happens without anybody having misconfigured anything.
            _database.Save();
            RewriteTheFileUnderneath();

            var changes = 0;
            var saves = 0;
            var writer = Writer(new RunsUiThread(), db => { saves++; db.Save(null); });

            var outcome = writer.Change(
                _database.Database,
                () => { changes++; return VaultWrite.Created("abcd"); });

            Assert.That(outcome.IsBusy, Is.True, outcome.Reason);
            Assert.That(outcome.Reason, Does.Contain("synchronize"),
                "and it says which of KeePass's questions was forestalled");
            Assert.That(changes, Is.Zero, "refused before anything was touched, so there is nothing to put back");
            Assert.That(saves, Is.Zero, "and nothing reached the save that would have asked");
        }

        [Test]
        public void A_file_nobody_touched_is_saved_as_before()
        {
            // The other half of the check, and the one that says it is not simply refusing. A
            // database whose file is the one KeePass read is the ordinary case, and every write
            // has to go through it.
            _database.Save();

            var saves = 0;
            var writer = Writer(new RunsUiThread(), db => { saves++; db.Save(null); });

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.Ok, Is.True, outcome.Reason);
            Assert.That(saves, Is.EqualTo(1));
        }

        [Test]
        public void A_database_that_has_never_been_written_is_not_refused()
        {
            // `HashOfFileOnDisk` is null until something has read or written that path, and there
            // is then no earlier state for the file to disagree with. `PreSaveValidate` passes this
            // case too, so refusing it here would refuse the first write to every new database.
            Assume.That(_database.Database.HashOfFileOnDisk, Is.Null);

            var writer = Writer(new RunsUiThread(), db => db.Save(null));

            Assert.That(writer.Change(_database.Database, () => VaultWrite.Created("abcd")).Ok, Is.True);
        }

        [Test]
        public void A_database_with_no_file_is_refused_rather_than_asked_where_to_put_it()
        {
            // `SaveDatabase` hands a database with no path to `SaveDatabaseAs`, which opens a file
            // picker: a program's write asking somebody where their passwords should be kept.
            var nowhere = new PwDatabase();
            nowhere.New(new IOConnectionInfo(), KeyForNothing());

            try
            {
                var saves = 0;
                var writer = Writer(new RunsUiThread(), db => saves++);

                var outcome = writer.Change(nowhere, () => VaultWrite.Created("abcd"));

                Assert.That(outcome.IsBusy, Is.True, outcome.Reason);
                Assert.That(saves, Is.Zero);
            }
            finally
            {
                nowhere.Close();
            }
        }

        [Test]
        public void A_save_the_policy_disallows_is_refused_rather_than_warned_about()
        {
            // `AppPolicy.Try` shows a warning box and answers false, so the save would not have
            // happened either way and the only thing the request achieved was the window.
            var was = KeePass.App.AppPolicy.Current;
            KeePass.App.AppPolicy.Current = new KeePass.App.AppPolicyFlags { SaveFile = false };

            try
            {
                var saves = 0;
                var writer = Writer(new RunsUiThread(), db => saves++);

                var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

                Assert.That(outcome.IsBusy, Is.True, outcome.Reason);
                Assert.That(saves, Is.Zero);
            }
            finally
            {
                // The setter refuses null, so where nothing was set a permissive one goes back.
                // That is what `AppPolicyFlags` starts as and what a running KeePass has unless an
                // administrator has said otherwise, so the process is left saying what it said.
                KeePass.App.AppPolicy.Current = was ?? new KeePass.App.AppPolicyFlags();
            }
        }

        /// <summary>
        /// Makes the file on disk something other than what KeePass read, which is what a second
        /// KeePass or a sync tool leaves behind.
        /// </summary>
        /// <remarks>
        /// One byte, because what both this check and `PreSaveValidate` look at is the hash of the
        /// bytes. Neither opens the file, so whether the result is still a database is not a
        /// question either of them asks.
        /// </remarks>
        private void RewriteTheFileUnderneath()
        {
            var bytes = System.IO.File.ReadAllBytes(_database.Path);
            bytes[bytes.Length - 1] ^= 0xFF;
            System.IO.File.WriteAllBytes(_database.Path, bytes);
        }

        /// <summary>
        /// Puts enough in the database that its serialization reports progress, and writes it.
        /// </summary>
        /// <remarks>
        /// `KdbxFile` asks the status logger once per entry, so an empty database is written
        /// without ever asking and a cancellation has nowhere to land. Saving afterwards is what
        /// gives the file a hash for the check to compare against, and what leaves the database
        /// saying it has nothing unsaved, which is the state this is about.
        /// </remarks>
        private void FillWithEntriesAndSave()
        {
            for (var i = 0; i < 200; i++)
            {
                var entry = new PwEntry(true, true);
                entry.Strings.Set("Title", new KeePassLib.Security.ProtectedString(false, "entry " + i));
                _database.RootGroup.AddEntry(entry, true);
            }

            _database.Save();
            _database.Database.Modified = false;
        }

        /// <summary>
        /// KeePass's own cancellation, which is what the tray menu's Cancel does to a save.
        /// </summary>
        /// <remarks>
        /// `KdbxFile.Save` throws `OperationCanceledException` the moment the logger answers false,
        /// which is part way through writing the file rather than before it. That is the shape a
        /// full disk and a dropped share have as well.
        /// </remarks>
        private sealed class CancelsPartWay : KeePassLib.Interfaces.IStatusLogger
        {
            private int _asked;

            public void StartLogging(string operation, bool writeToLog)
            {
            }

            public void EndLogging()
            {
            }

            public bool SetProgress(uint percent)
            {
                return ++_asked < 50;
            }

            public bool SetText(string text, KeePassLib.Interfaces.LogStatusType type)
            {
                return true;
            }

            public bool ContinueWork()
            {
                return ++_asked < 50;
            }
        }

        private static CompositeKey KeyForNothing()
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword("not-a-real-password-9c30"));
            return key;
        }

        [Test]
        public void A_change_is_refused_while_KeePass_is_part_way_through_something_of_its_own()
        {
            // Being on KeePass's thread is not being alone on it. Its save, its import and its
            // export all pump that thread's queue, so a marshaled write is dispatched inside
            // whichever of them is running: it changes the tree that operation is walking, and
            // the nested save either loses its own change under the outer one or commits half of
            // the operator's import with nothing left saying the rest is unsaved.
            var changes = 0;
            var saves = 0;
            var writer = new DatabaseWriter(
                new RunsUiThread(),
                db => { saves++; db.Modified = false; },
                () => "KeePass is saving",
                null,
                null,
                null,
                NullGateLog.Instance);

            var outcome = writer.Change(_database.Database, () => { changes++; return VaultWrite.Created("abcd"); });

            Assert.That(outcome.IsBusy, Is.True, "and busy is the answer worth retrying");
            Assert.That(outcome.Ok, Is.False);
            Assert.That(changes, Is.EqualTo(0), "refused before anything was touched");
            Assert.That(saves, Is.EqualTo(0), "so there is nothing to take back out");
        }

        [Test]
        public void A_record_is_refused_by_the_same_answer_before_the_invitation_is_spent()
        {
            // The same for pairing, where what lands in the middle of somebody's import is the
            // one record that has to be on disk before a certificate is handed over. `Blocked` is
            // both what pairing asks before it spends an invitation and the first thing the
            // record's own marshaled call asks, so one answer covers both instants.
            var writer = new DatabaseWriter(
                new RunsUiThread(),
                db => { db.Modified = false; },
                () => "KeePass is importing",
                null,
                null,
                null,
                NullGateLog.Instance);

            Assert.That(writer.Blocked(_database.Database), Is.EqualTo("KeePass is importing"),
                "so a busy KeePass costs an attempt rather than the invitation behind it");
        }

        [Test]
        public void The_block_this_writers_own_save_raises_is_not_KeePass_being_busy()
        {
            // `MainForm.SaveDatabase` blocks KeePass's interaction for as long as it runs, and a
            // pairing or a renewal asks `Blocked` from another connection while a write is saving.
            // Read as KeePass's own work, every one of them was turned away for a write that only
            // had to be waited for.
            var blocked = false;
            var keePassBusy = false;
            string during = "never asked";
            var savingDuring = false;
            DatabaseWriter writer = null;
            writer = new DatabaseWriter(
                new RunsUiThread(),
                db =>
                {
                    blocked = true;
                    try
                    {
                        savingDuring = writer.Saving;
                        during = System.Threading.Tasks.Task.Run(() => writer.Blocked(db)).Result;
                        db.Modified = false;
                    }
                    finally
                    {
                        blocked = false;
                    }
                },
                () => blocked || keePassBusy ? "KeePass is in the middle of an operation of its own" : null,
                null,
                null,
                null,
                NullGateLog.Instance);

            Assert.That(writer.Change(_database.Database, () => VaultWrite.Created("abcd")).Ok, Is.True);

            Assert.That(savingDuring, Is.True, "the writer knows its own save is running");
            Assert.That(during, Is.Null, "asked from another thread during that save, it is not KeePass's");
            Assert.That(writer.Saving, Is.False);

            keePassBusy = true;
            Assert.That(writer.Blocked(_database.Database), Is.EqualTo("KeePass is in the middle of an operation of its own"),
                "while the same block raised by KeePass's own work still refuses");
        }

        [Test]
        public void A_connection_waits_for_the_write_in_flight_and_no_longer_than_it_is_told()
        {
            using (var inside = new System.Threading.ManualResetEventSlim())
            using (var release = new System.Threading.ManualResetEventSlim())
            {
                var writer = Writer(new RunsUiThread(), db =>
                {
                    inside.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                    db.Modified = false;
                });

                var writing = System.Threading.Tasks.Task.Run(
                    () => writer.Change(_database.Database, () => VaultWrite.Created("abcd")));
                Assert.That(inside.Wait(TimeSpan.FromSeconds(10)), Is.True);

                Assert.That(writer.Quiet(TimeSpan.FromMilliseconds(50)), Is.False, "the write is still saving");

                release.Set();
                Assert.That(writer.Quiet(TimeSpan.FromSeconds(10)), Is.True, "and is waited for once it ends");
                Assert.That(writing.Result.Ok, Is.True);
            }
        }

        [Test]
        public void A_question_about_KeePass_that_cannot_be_answered_refuses_the_write()
        {
            // Fail closed, like every other guard here. The whole point of the question is that
            // the alternative is a client told its change was saved when the operator's next
            // operation is going to take it back.
            var writer = new DatabaseWriter(
                new RunsUiThread(),
                db => { db.Modified = false; },
                () => { throw new InvalidOperationException("the window went away"); },
                null,
                null,
                null,
                NullGateLog.Instance);

            var outcome = writer.Change(_database.Database, () => VaultWrite.Created("abcd"));

            Assert.That(outcome.IsBusy, Is.True);
        }

        [Test]
        public void A_writer_with_nothing_to_ask_about_KeePass_still_writes()
        {
            // Null is "nobody can say", and outside KeePass there is no operation of KeePass's
            // for a write to land inside. Every test above this one relies on it.
            var writer = Writer(new RunsUiThread(), db => { db.Modified = false; });

            Assert.That(writer.Change(_database.Database, () => VaultWrite.Created("abcd")).Ok, Is.True);
        }

        [Test]
        public void A_database_that_closed_while_the_change_waited_is_not_called_busy()
        {
            // A lock or a File > Close runs on KeePass's thread and can come before a change that
            // was queued there behind it. `PwDatabase.Close` leaves an empty connection behind,
            // and asked about that, the save's own questions said the database had no file:
            // `503 busy`, which tells a client to try again against a database that is gone.
            // `Change` answers the same event one instruction earlier as a failure.
            var changes = 0;
            var saves = 0;
            var writer = Writer(new ClosesFirst(_database.Database), db => { saves++; db.Modified = false; });

            var outcome = writer.Change(
                _database.Database, () => { changes++; return VaultWrite.Created("abcd"); });

            Assert.That(outcome.IsBusy, Is.False, outcome.Reason);
            Assert.That(outcome.IsFailed, Is.True, outcome.Reason);
            Assert.That(outcome.Reason, Does.Contain("closed"));
            Assert.That(changes, Is.Zero, "nothing was attempted");
            Assert.That(saves, Is.Zero);
        }

        [TestCase(typeof(UnauthorizedAccessException))]
        [TestCase(typeof(System.Security.SecurityException))]
        [TestCase(typeof(NotSupportedException))]
        [TestCase(typeof(System.ComponentModel.Win32Exception))]
        public void A_handler_that_fails_before_the_save_leaves_nothing_behind(Type thrown)
        {
            // What `MainForm.SaveDatabase` lets out is not KeePass's own save, which it catches,
            // but the handlers of every other plugin around it and the window work either side. A
            // handler refused access to a path of its own throws before anything is written, and
            // read by its type rather than by the database, that left the change in memory with
            // the flag up: the client was told `500` and the next save wrote the change anyway.
            _database.Save();
            _database.Database.Modified = false;

            var undone = 0;
            var writer = Writer(new RunsUiThread(), db => { throw (Exception)Activator.CreateInstance(thrown); });

            var outcome = writer.Change(
                _database.Database,
                () => new VaultChange(VaultWrite.Created("abcd"), () => undone++));

            Assert.That(outcome.IsUnsaved, Is.True, outcome.Reason);
            Assert.That(undone, Is.EqualTo(1), "the change is taken back out");
            Assert.That(_database.Database.Modified, Is.False,
                "and the file it never reached is still the one KeePass read");
        }

        [Test]
        public void A_standing_answer_for_this_session_is_refused_before_anything_is_touched()
        {
            var saves = 0;
            var writer = Writer(new RunsUiThread(), db => saves++);

            var outcome = writer.RefuseRenewal(
                _database.Database, new string('a', 64), RenewalRefusal.ThisSession());

            Assert.That(outcome.IsFailed, Is.True);
            Assert.That(outcome.Reason, Does.Contain("this session"));
            Assert.That(saves, Is.EqualTo(0), "nothing was saved");
            Assert.That(new AdmissionStore(new CustomDataStore(_database.Database), NullGateLog.Instance)
                .StandingRefusal(new string('a', 64)), Is.Null, "nor written");
        }

        private DatabaseWriter Writer(IUiThread ui, Action<PwDatabase> save)
        {
            return Writer(ui, save, null);
        }

        private DatabaseWriter Writer(IUiThread ui, Action<PwDatabase> save, Action<PwDatabase, bool> redraw)
        {
            return new DatabaseWriter(ui, save, redraw, null, NullGateLog.Instance);
        }

        /// <summary>Runs the work and reports that it could not be waited for.</summary>
        /// <remarks>
        /// What the real one does when the work was already running: it waits it out and answers
        /// false, having done everything. The side effects are real and the answer says they are
        /// not, which is the whole of the first test above.
        /// </remarks>
        private sealed class LateUiThread : IUiThread
        {
            public bool Run(Action work, TimeSpan timeout)
            {
                work();
                return false;
            }
        }

        private sealed class RunsUiThread : IUiThread
        {
            public bool Run(Action work, TimeSpan timeout)
            {
                work();
                return true;
            }
        }

        /// <summary>
        /// Closes the database before it runs the work, which is a lock or a File > Close that
        /// KeePass's thread took first.
        /// </summary>
        private sealed class ClosesFirst : IUiThread
        {
            private readonly PwDatabase _closing;

            public ClosesFirst(PwDatabase closing)
            {
                _closing = closing;
            }

            public bool Run(Action work, TimeSpan timeout)
            {
                _closing.Close();
                work();
                return true;
            }
        }
    }
}
