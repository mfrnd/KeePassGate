using System;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using KeePassGate.Vault;
using KeePassLib;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What the single writer answers when the save does not go the way it hoped.
    /// </summary>
    /// <remarks>
    /// Both of these are about a caller being told something untrue about its own request, which
    /// is worse than being told nothing: one had a change that ran and was saved reported as "try
    /// again", and the other had a change that never reached the disk reported as a route that
    /// does not exist. A client acting on either answer makes a second copy of what it just wrote.
    ///
    /// Neither could be reached through the fakes the unit tier has. `FakeUiThread` runs the work
    /// and answers true, or answers false without running it; the case that matters is the third
    /// one, which a real user interface thread produces when the work was already running.
    /// </remarks>
    [TestFixture]
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

        private DatabaseWriter Writer(IUiThread ui, Action<PwDatabase> save)
        {
            return new DatabaseWriter(ui, save, null, NullGateLog.Instance);
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
    }
}
