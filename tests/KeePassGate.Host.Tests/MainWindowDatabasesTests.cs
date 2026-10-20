using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using KeePass.Forms;
using KeePass.UI;
using KeePassGate.Bridge;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Serialization;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// How KeePass announces an open, a lock and a close, and which database each one is about.
    /// </summary>
    /// <remarks>
    /// Against the real `MainForm` and the real event arguments, driven in the order KeePass
    /// raises them. The window is never shown or initialized: `MainForm.CloseDocument` is what
    /// raises these, and reproducing the sequence is what this needs, not a running password
    /// manager.
    ///
    /// The sequence is the whole point. A close raises `FileClosingPre` and then `FileClosed`, and
    /// for a document that was locked earlier both of them carry an empty path, because
    /// `PwDatabase.Close` already cleared the connection at the lock. Everything this plugin holds
    /// for a database is keyed on the path, so getting that wrong releases one database's material
    /// and keeps another's for ever.
    /// </remarks>
    [TestFixture]
    public sealed class MainWindowDatabasesTests
    {
        private const string First = @"C:\invented\first.kdbx";
        private const string Second = @"C:\invented\second.kdbx";

        private MainForm _window;
        private DocumentManagerEx _documents;
        private MainWindowDatabases _databases;
        private List<GateDatabaseEventArgs> _closed;
        private List<GateDatabaseEventArgs> _opened;

        [SetUp]
        public void SetUp()
        {
            // No constructor, because `MainForm`'s builds a whole window. Only its events and its
            // document manager are wanted, and both are fields.
            _window = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            _documents = new DocumentManagerEx();

            var field = typeof(MainForm).GetField("m_docMgr", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null,
                "MainForm no longer keeps its documents in m_docMgr, so a locked document cannot be named");
            field.SetValue(_window, _documents);

            _databases = new MainWindowDatabases(_window);
            _closed = new List<GateDatabaseEventArgs>();
            _opened = new List<GateDatabaseEventArgs>();
            _databases.Closed += (sender, e) => _closed.Add(e);
            _databases.Opened += (sender, e) => _opened.Add(e);
        }

        [TearDown]
        public void TearDown()
        {
            _databases.Dispose();
        }

        [Test]
        public void An_opened_database_is_announced_by_its_path()
        {
            var database = Document(First).Database;

            Raise("FileOpened", new FileOpenedEventArgs(database));

            Assert.That(_opened, Has.Count.EqualTo(1));
            Assert.That(_opened[0].Path, Is.EqualTo(First));
            Assert.That(_opened[0].IsLocking, Is.False);
        }

        [Test]
        public void Locking_a_database_names_it_and_says_it_is_a_lock()
        {
            var document = Document(First);

            Lock(document);

            Assert.That(_closed, Has.Count.EqualTo(1));
            Assert.That(_closed[0].Path, Is.EqualTo(First), "a lock that names nobody holds material for ever");
            Assert.That(_closed[0].IsLocking, Is.True, "and a lock that reads as a close stops answering 503");
        }

        [Test]
        public void A_lock_is_announced_first_while_the_database_can_still_be_read()
        {
            // What a lock answers with is whatever the gate last read, so it has to be told while
            // there is still something to read: after the close the database is empty.
            var document = Document(First);
            var readable = new List<bool>();
            _databases.Closing += (sender, e) => readable.Add(document.Database.IsOpen);

            Lock(document);

            Assert.That(readable, Is.EqualTo(new[] { true }),
                "announced once, and before the database was emptied");
            Assert.That(_closed, Has.Count.EqualTo(1), "and the lock itself still follows");
        }

        [Test]
        public void Closing_a_locked_database_names_that_database_and_not_another()
        {
            // The failure this fixture exists for. Both documents are locked, so both closes carry
            // an empty path, and the second one used to be answered with the first one's.
            var first = Document(First);
            var second = Document(Second);

            Lock(first);
            Lock(second);
            _closed.Clear();

            Close(first);
            Close(second);

            Assert.That(Paths(), Is.EqualTo(new[] { First, Second }),
                "closing one locked database released another database's material and kept its own");
            Assert.That(_closed[0].IsLocking, Is.False, "a close is not a lock");
            Assert.That(_closed[1].IsLocking, Is.False);
        }

        [Test]
        public void A_close_that_follows_an_unrelated_one_is_still_named_correctly()
        {
            // The other half of the same defect: what was remembered outlived the close it was
            // remembered for, so an ordinary close of one file renamed the next locked one.
            var first = Document(First);
            var second = Document(Second);

            Close(second);
            Lock(first);
            _closed.Clear();

            Close(first);

            Assert.That(Paths(), Is.EqualTo(new[] { First }));
        }

        [Test]
        public void A_close_that_was_canceled_leaves_nothing_behind_for_the_next_one()
        {
            var first = Document(First);

            // One that names no file of its own, since only such a close has anything to take
            // from what was remembered: a next close with a path of its own passed whatever was
            // left behind.
            var unsaved = Unsaved();

            // KeePass raises the warning and the operator says no, so no close follows.
            Raise("FileClosingPre", new FileClosingEventArgs(first.Database, FileEventFlags.None));

            Close(unsaved);

            Assert.That(_closed, Has.Count.EqualTo(1));
            Assert.That(_closed[0].Path, Is.Null.Or.Empty, "the abandoned close must not name the next one");
        }

        [Test]
        public void A_close_that_names_no_file_after_a_lock_is_not_announced_as_the_locked_one()
        {
            // What remembering on every close and forgetting on every close is for, now that a
            // locked database is found through its document: the lock leaves its path behind, and
            // a database that was never saved has none to put in its place.
            var first = Document(First);
            Lock(first);
            var unsaved = Unsaved();
            _closed.Clear();

            Close(unsaved);

            Assert.That(_closed, Has.Count.EqualTo(1));
            Assert.That(_closed[0].Path, Is.Not.EqualTo(First),
                "the locked database was announced as closed, so it stops answering 503 locked");
            Assert.That(_closed[0].Path, Is.Null.Or.Empty);
        }

        [Test]
        public void An_ordinary_close_carries_its_own_path_and_needs_nothing_remembered()
        {
            var document = Document(First);

            Close(document);

            Assert.That(Paths(), Is.EqualTo(new[] { First }));
        }

        /// <summary>A document KeePass knows about, holding an open database at this path.</summary>
        private PwDocument Document(string path)
        {
            var document = _documents.CreateNewDocument(false);
            document.Database.New(IOConnectionInfo.FromPath(path), NewKey());
            return document;
        }

        /// <summary>A document KeePass knows about, holding a database made with no file at all.</summary>
        private PwDocument Unsaved()
        {
            var document = _documents.CreateNewDocument(false);
            document.Database.New(new IOConnectionInfo(), NewKey());
            return document;
        }

        /// <summary>
        /// What `MainForm.LockAllDocuments` does to one document, in its order: the close events
        /// that `CloseDocument` raises, with the database emptied between them, and only once that
        /// has returned the file kept on the document.
        /// </summary>
        /// <remarks>
        /// Read off KeePass 2.61.1's `LockAllDocuments`, which takes the connection before the
        /// close and puts it on the document after. `FileClosingPost` comes between the two
        /// events as well, and is left out because nothing here listens to it. This model used to
        /// put the file on the document first, and agreed with the adapter only because nothing
        /// looks at the document during a lock's own close.
        /// </remarks>
        private void Lock(PwDocument document)
        {
            var locked = document.Database.IOConnectionInfo;

            Raise("FileClosingPre", new FileClosingEventArgs(document.Database, FileEventFlags.Locking));

            var connection = document.Database.IOConnectionInfo.CloneDeep();
            document.Database.Close();

            Raise("FileClosed", new FileClosedEventArgs(connection, FileEventFlags.Locking));

            document.LockedIoc = locked;
        }

        /// <summary>The same thing without the locking flag, which is a real close.</summary>
        private void Close(PwDocument document)
        {
            Raise("FileClosingPre", new FileClosingEventArgs(document.Database, FileEventFlags.None));

            var connection = document.Database.IOConnectionInfo.CloneDeep();
            document.Database.Close();

            Raise("FileClosed", new FileClosedEventArgs(connection, FileEventFlags.None));
        }

        private string[] Paths()
        {
            var paths = new List<string>();
            foreach (var closed in _closed)
            {
                paths.Add(closed.Path);
            }

            return paths.ToArray();
        }

        /// <summary>
        /// Raises one of `MainForm`'s events. They are field-like, so the delegate is reachable
        /// even on a window that was never constructed.
        /// </summary>
        private void Raise(string name, EventArgs args)
        {
            var field = typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "MainForm no longer raises " + name);

            var handler = field.GetValue(_window) as Delegate;
            Assert.That(handler, Is.Not.Null, "nothing subscribed to " + name);

            handler.DynamicInvoke(_window, args);
        }

        private static KeePassLib.Keys.CompositeKey NewKey()
        {
            var key = new KeePassLib.Keys.CompositeKey();
            key.AddUserKey(new KeePassLib.Keys.KcpPassword("invented"));
            return key;
        }
    }
}
