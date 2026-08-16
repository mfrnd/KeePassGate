using System;
using KeePass.Forms;
using KeePassLib;
using KeePassGate.Ui;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// The one adapter that knows how KeePass announces a database opening, locking and closing.
    /// </summary>
    /// <remarks>
    /// As thin as <see cref="GlobalWindowManagerWindows"/> and for the same reason: nothing above it
    /// can be unit tested against a real KeePass, so it translates and forwards and does nothing
    /// else.
    ///
    /// **A lock arrives as a close.** KeePass raises `FileClosed` with `FileEventFlags.Locking` when
    /// the workspace is locked and without it when a file is really closed, and `FileOpened` for
    /// both an open and an unlock. That is the whole of the difference the plugin gets, and it is
    /// enough: `PAIRING.md` §308 wants a locked database to keep answering and a closed one to
    /// disappear.
    ///
    /// **The path is what identifies a database across the transition**, not the `PwDatabase`.
    /// `PwDatabase.Close` clears the object, including its `IOConnectionInfo`, so by the time this
    /// event arrives the database that closed can no longer say which file it was. The event
    /// carries the path for exactly that reason.
    ///
    /// **And a document that is already locked has been through that once already**, so its
    /// `FileClosingPre` carries nothing either. KeePass keeps what it was locked from on the
    /// document rather than on the database, which is what `ClosingPathOf` reads.
    /// </remarks>
    public sealed class MainWindowDatabases : IDatabaseEvents, IDisposable
    {
        private readonly MainForm _mainWindow;
        private readonly object _stateLock = new object();
        private bool _disposed;

        // The path of whatever is closing, kept only between the two events.
        private string _closing;

        public MainWindowDatabases(MainForm mainWindow)
        {
            if (mainWindow == null)
            {
                throw new ArgumentNullException(nameof(mainWindow));
            }

            _mainWindow = mainWindow;
            _mainWindow.FileOpened += OnFileOpened;

            // `File > New` raises this and never `FileOpened`, so without it a database created
            // this session is never served, never has a responder minted, and answers nothing at
            // all once it is locked. Verified against KeePass 2.61.1: `OnFileNew` is the only
            // method that raises `FileCreated`, and `OpenDatabase` the only one that raises the
            // other.
            _mainWindow.FileCreated += OnFileCreated;
            _mainWindow.FileClosingPre += OnFileClosing;
            _mainWindow.FileClosed += OnFileClosed;
        }

        public event EventHandler<GateDatabaseEventArgs> Opened;

        public event EventHandler Closing;

        public event EventHandler<GateDatabaseEventArgs> Closed;

        public void Dispose()
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _mainWindow.FileOpened -= OnFileOpened;
            _mainWindow.FileCreated -= OnFileCreated;
            _mainWindow.FileClosingPre -= OnFileClosing;
            _mainWindow.FileClosed -= OnFileClosed;

            Opened = null;
            Closing = null;
            Closed = null;
        }

        private void OnFileCreated(object sender, FileCreatedEventArgs e)
        {
            var handler = Opened;
            if (handler == null || e == null || e.Database == null)
            {
                return;
            }

            var connection = e.Database.IOConnectionInfo;
            handler(this, new GateDatabaseEventArgs(connection == null ? null : connection.Path, false));
        }

        private void OnFileOpened(object sender, FileOpenedEventArgs e)
        {
            var handler = Opened;
            if (handler == null || e == null || e.Database == null)
            {
                return;
            }

            handler(this, new GateDatabaseEventArgs(PathOf(e), false));
        }

        /// <summary>
        /// Remembers what is about to close, because the close itself may not be able to say.
        /// </summary>
        /// <remarks>
        /// **A close that follows a lock carries no path.** Measured on KeePass 2.61.1: after
        /// `PwDatabase.Close()` the `IOConnectionInfo.Path` is the empty string, and a document that
        /// was locked has already been through that, so `FileClosed` announces it with nothing to
        /// name it by. Everything keyed on the path then does nothing, and what was answering for
        /// that database is held until KeePass exits.
        ///
        /// `FileClosingPre` is the last moment the database is still identifiable. Only the path is
        /// kept, and only until the matching close arrives, because this may be canceled: the
        /// event carries a `Cancel` flag and acting on it would drop material for a close that
        /// never happens.
        ///
        /// **Written on every one of these, including when the answer is nothing.** It used to be
        /// written only when a path was found, and cleared only when one was used, which made it
        /// the path of whatever last closed with a name rather than of the document closing now.
        /// The document lookup below now names a locked database by itself; what this still
        /// decides is a close that names no file anywhere, a database never saved. Under the old
        /// rule such a close, after a lock, was announced as an ordinary close of the locked
        /// database, which then stopped answering `503 locked`. KeePass raises this and the
        /// matching close one after the other for one document, so a value written every time and
        /// taken away every time names that document and no other.
        ///
        /// **And it is announced**, because being the last moment a database can be read is worth
        /// more than its path. What a lock answers with is built from what the gate last read, and
        /// before this nothing read it between a connection and the lock: a Save As moved the path
        /// under the entry, an authority created on the KPG tab had no entry at all, and one
        /// deleted there still had one, so the lock that followed matched nothing or answered for
        /// an authority that was gone.
        /// </remarks>
        private void OnFileClosing(object sender, FileClosingEventArgs e)
        {
            if (e == null || e.Database == null)
            {
                return;
            }

            var path = ClosingPathOf(e.Database);

            lock (_stateLock)
            {
                _closing = path;
            }

            var handler = Closing;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// What file this database is, at the last moment anything can say.
        /// </summary>
        /// <remarks>
        /// Two places to look, because a lock has already cleared the first. `PwDatabase.Close`
        /// resets the connection to an empty one, so a document locked earlier in the session
        /// arrives here with nothing on the database; KeePass moves the file it was locked from to
        /// the document, which is where the second look goes. A database that is neither open nor
        /// a document this window knows answers null, and the close it belongs to then reaches
        /// nobody, which is the same as it did before and is the safe direction.
        /// </remarks>
        private string ClosingPathOf(PwDatabase database)
        {
            var connection = database.IOConnectionInfo;
            var path = connection == null ? null : connection.Path;
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            var documents = _mainWindow.DocumentManager;
            var document = documents == null ? null : documents.FindDocument(database);
            var locked = document == null ? null : document.LockedIoc;
            return locked == null ? null : locked.Path;
        }

        private void OnFileClosed(object sender, FileClosedEventArgs e)
        {
            var handler = Closed;
            if (handler == null || e == null)
            {
                return;
            }

            var locking = (e.Flags & FileEventFlags.Locking) != FileEventFlags.None;
            var path = e.IOConnectionInfo == null ? null : e.IOConnectionInfo.Path;

            string closing;
            lock (_stateLock)
            {
                // Taken away whether or not it is wanted here, so that nothing of one document's
                // close can be read as another's.
                closing = _closing;
                _closing = null;
            }

            if (string.IsNullOrEmpty(path))
            {
                path = closing;
            }

            handler(this, new GateDatabaseEventArgs(path, locking));
        }

        private static string PathOf(FileOpenedEventArgs e)
        {
            var connection = e.Database.IOConnectionInfo;
            return connection == null ? null : connection.Path;
        }
    }
}
