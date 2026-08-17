using System.Threading;
using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The adapter the tab writes through, and the part of it that is easy to leave out.
    /// </summary>
    /// <remarks>
    /// Marking a database is two things, and only one of them is obvious. The flag records that
    /// there is something to save; telling KeePass is what makes it say so. Without the second, the
    /// title bar goes on claiming everything is on disk, and an operator who believes it closes
    /// KeePass having lost the change they just made.
    ///
    /// That is what these ask, because it is invisible from anywhere else: nothing fails, nothing
    /// is logged, and the only symptom is a window that quietly disagrees with the database behind
    /// it.
    /// </remarks>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class OpenDatabaseTests
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
        public void Marking_a_database_records_it_and_tells_the_host()
        {
            var told = 0;
            var open = new OpenDatabase(_database.Database, () => told++);
            _database.Database.Modified = false;

            open.MarkModified();

            Assert.That(_database.Database.Modified, Is.True, "the database has to know");
            Assert.That(told, Is.EqualTo(1), "and so does the window showing it");
        }

        [Test]
        public void A_database_with_nobody_listening_still_records_the_change()
        {
            // Everything above this interface is testable without a window to repaint, so the
            // notification is optional. What must not be optional is the flag.
            var open = new OpenDatabase(_database.Database);
            _database.Database.Modified = false;

            open.MarkModified();

            Assert.That(_database.Database.Modified, Is.True);
        }

        [Test]
        public void A_change_from_the_tab_is_in_the_database_and_not_yet_in_the_file()
        {
            // Asked of a real button and a real file, one layer below the tab: a panel on the
            // live database changes it as soon as it is asked, and the file is written when the
            // database is saved. What the tab's notice promises on top of that is that nothing
            // reaches even this far until OK. If this
            // ever stops being true in either direction, that sentence is a lie in a place an
            // operator relies on it.
            _database.Save();
            Assume.That(_database.Database.Modified, Is.False);

            PendingDatabase pending;
            using (var panel = NewPanel(out pending))
            {
                panel.CreateAuthority();

                Assert.That(Authority(), Is.False,
                    "nothing reaches the database before the dialog is accepted");
            }

            pending.Apply();

            Assert.That(_database.Database.Modified, Is.True, "the change is real now");
            Assert.That(Authority(), Is.True, "and it is in the database KeePass is holding");

            _database.CloseAndReopenWithoutSaving();

            Assert.That(Authority(), Is.False, "but nothing reached the file, because nobody saved");
        }

        [Test]
        public void Canceling_the_dialog_leaves_the_database_as_it_was()
        {
            // The other half of the notice on the tab, and the half nothing asserted. `Discard` is
            // what `AuthorityPanelFactory` calls for any result that is not OK.
            _database.Save();
            Assume.That(_database.Database.Modified, Is.False);

            PendingDatabase pending;
            using (var panel = NewPanel(out pending))
            {
                panel.CreateAuthority();
            }

            pending.Discard();

            Assert.That(Authority(), Is.False, "nothing was written");
            Assert.That(_database.Database.Modified, Is.False, "and nothing is waiting to be saved");
        }

        [Test]
        public void The_same_change_is_in_the_file_once_it_is_saved()
        {
            PendingDatabase pending;
            using (var panel = NewPanel(out pending))
            {
                panel.CreateAuthority();
            }

            pending.Apply();
            _database.SaveCloseAndReopen();

            Assert.That(Authority(), Is.True);
            Assert.That(_database.Database.Modified, Is.False,
                "a saved database has nothing left to save, which is what the tab is telling them to do");
        }

        /// <summary>
        /// The panel as the plugin actually builds it, wrapped in the decorator that holds its
        /// writes back until the dialog is accepted.
        /// </summary>
        /// <remarks>
        /// Wrapped here because `AuthorityPanelFactory` always wraps, and a fixture that skipped it
        /// tested a panel this plugin never constructs: the whole hold-until-OK reversal could have
        /// been deleted and every test here would still have passed. `pending` stands for the
        /// dialog's answer, `Apply` for OK and `Discard` for Cancel.
        /// </remarks>
        private AuthorityPanel NewPanel(out PendingDatabase pending)
        {
            pending = new PendingDatabase(
                new OpenDatabase(_database.Database, delegate { }), NullGateLog.Instance);

            return new AuthorityPanel(pending, new NeverAsked(), NullGateLog.Instance);
        }

        private bool Authority()
        {
            using (var reading = new AuthorityService(new CustomDataStore(_database.Database), null).Read())
            {
                return reading.IsPresent;
            }
        }
    }
}
