using System;

namespace KeePassGate.Ui
{
    /// <summary>Which database, by the file it came from.</summary>
    public sealed class GateDatabaseEventArgs : EventArgs
    {
        private readonly string _path;
        private readonly bool _locking;

        public GateDatabaseEventArgs(string path, bool locking)
        {
            _path = path ?? string.Empty;
            _locking = locking;
        }

        /// <summary>
        /// The file, which is the only thing both halves of a lock have in common.
        /// </summary>
        /// <remarks>
        /// Not the database object. By the time KeePass says a file has closed, the database it
        /// closed has been cleared, and the path is what is left to recognize it by when the same
        /// file is opened again.
        /// </remarks>
        public string Path
        {
            get { return _path; }
        }

        /// <summary>
        /// True when this close is a lock, false when the database is really being closed.
        /// </summary>
        /// <remarks>
        /// The difference is everything here. A lock keeps the gate key and the responder so the
        /// gate can answer, and a close drops both because there is nothing left to answer for.
        /// </remarks>
        public bool IsLocking
        {
            get { return _locking; }
        }
    }

    /// <summary>
    /// KeePass opening, locking and closing databases, without KeePass in the signature.
    /// </summary>
    /// <remarks>
    /// The seam that lets lock and unlock be tested without a password manager, and the same shape
    /// as <see cref="IWindowEventSource"/>. All three events arrive on the user interface thread.
    /// </remarks>
    public interface IDatabaseEvents
    {
        /// <summary>A database was opened, which is also what an unlock is.</summary>
        event EventHandler<GateDatabaseEventArgs> Opened;

        /// <summary>
        /// A database is about to be closed or locked, and can still be read.
        /// </summary>
        /// <remarks>
        /// Carries nothing, because what it is for is reading every open database once more, and
        /// which of them is going does not change that. It may be followed by nothing at all:
        /// KeePass lets the close be canceled after this.
        /// </remarks>
        event EventHandler Closing;

        /// <summary>A database was closed, which is also what a lock is.</summary>
        event EventHandler<GateDatabaseEventArgs> Closed;
    }
}
