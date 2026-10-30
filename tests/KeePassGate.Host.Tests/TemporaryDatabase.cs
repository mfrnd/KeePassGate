using System;
using System.IO;
using KeePassGate.Diagnostics;
using KeePassGate.Testing;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// A real KDBX4 database in a temporary directory, created with KeePass's own library.
    /// </summary>
    /// <remarks>
    /// Invented and empty, and deleted when the test finishes. The author's own database is never
    /// touched by anything in this repository, and a `.kdbx` is never committed.
    /// </remarks>
    internal sealed class TemporaryDatabase : IDisposable
    {
        private const string Password = "not-a-real-password-4b21";

        private readonly string _directory;
        private PwDatabase _database;

        private TemporaryDatabase(string directory, PwDatabase database)
        {
            _directory = directory;
            _database = database;
        }

        public PwDatabase Database
        {
            get { return _database; }
        }

        public string Path
        {
            get { return Paths.Join(_directory, "test.kdbx"); }
        }

        public PwGroup RootGroup
        {
            get { return _database.RootGroup; }
        }

        public static TemporaryDatabase New()
        {
            var directory = Paths.Join(
                System.IO.Path.GetTempPath(), "KeePassGateTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            var database = new PwDatabase();
            database.New(IOConnectionInfo.FromPath(Paths.Join(directory, "test.kdbx")), KeyFor());
            database.RootGroup.Name = "Invented";

            return new TemporaryDatabase(directory, database);
        }

        /// <summary>Writes it to disk, as KeePass would.</summary>
        public void Save()
        {
            _database.Save(null);
        }

        /// <summary>
        /// Saves, closes and opens it again, which is the only way to believe that something is
        /// really stored rather than merely present in memory.
        /// </summary>
        public void SaveCloseAndReopen()
        {
            Save();
            _database.Close();
            Reopen();
        }

        /// <summary>
        /// Opens the file again without saving first, which is what an unlock is: KeePass closed
        /// the database and is now reading the same file back.
        /// </summary>
        public void Reopen()
        {
            var reopened = new PwDatabase();
            reopened.Open(IOConnectionInfo.FromPath(Path), KeyFor(), null);
            _database = reopened;
        }

        /// <summary>
        /// Throws the database in memory away and opens the file again, which is what happens to a
        /// change nobody saved.
        /// </summary>
        /// <remarks>
        /// The counterpart to `SaveCloseAndReopen`, and the only way to tell a change that reached
        /// the file from one that merely reached the object graph. A test that never asks this
        /// cannot notice a plugin that has quietly stopped saving.
        /// </remarks>
        public void CloseAndReopenWithoutSaving()
        {
            _database.Close();

            var reopened = new PwDatabase();
            reopened.Open(IOConnectionInfo.FromPath(Path), KeyFor(), null);
            _database = reopened;
        }

        /// <summary>
        /// Saves, closes and opens the same `PwDatabase` again.
        /// </summary>
        /// <remarks>
        /// What KeePass does when a workspace is locked and unlocked, which is not what
        /// `SaveCloseAndReopen` does: `MainForm` keeps the document's database object and calls
        /// `Open` on it, so the instance is the same and the root group behind it is a new one.
        /// Anything holding the old group is then holding a group nothing writes to any more, and
        /// only a reopen of this shape can show it.
        /// </remarks>
        public void LockAndUnlock()
        {
            Lock();
            Unlock();
        }

        /// <summary>The first half of <see cref="LockAndUnlock"/>: saved, and closed.</summary>
        /// <remarks>
        /// On its own for a test that asks something while the database is locked, which is when
        /// everything holding the old root group is reading a tree nothing points at any more.
        /// </remarks>
        public void Lock()
        {
            Save();
            _database.Close();
        }

        /// <summary>The second half: the same `PwDatabase`, opened again.</summary>
        public void Unlock()
        {
            _database.Open(IOConnectionInfo.FromPath(Path), KeyFor(), null);
        }

        public void Dispose()
        {
            // Whatever closing throws, the directory still goes below, and a cleanup that threw
            // would hide what the test itself found.
            _ = Thrown.By(() =>
            {
                if (_database != null && _database.IsOpen)
                {
                    _database.Close();
                }
            });

            Delete(_directory);
        }

        /// <summary>
        /// Deleting can lose a race with whatever last held the file, so it is tried a few times
        /// before being given up on. Left alone it accumulates a directory in the temporary folder
        /// for every run that ends badly.
        /// </summary>
        private static void Delete(string directory)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Directory.Exists(directory))
                    {
                        return;
                    }

                    Directory.Delete(directory, true);
                    return;
                }
                catch (IOException)
                {
                    // The race lost: a file is still held by whatever had it last, so this waits
                    // and tries again.
                }
                catch (UnauthorizedAccessException)
                {
                    // What Windows answers instead for a file that is still being let go. Tried
                    // again as well.
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        private static CompositeKey KeyFor()
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(Password));
            return key;
        }
    }
}
