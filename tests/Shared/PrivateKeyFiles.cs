using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KeePassGate.Diagnostics;

namespace KeePassGate.Testing
{
    /// <summary>
    /// The private key files this account holds, for the tests that assert none appears.
    /// </summary>
    /// <remarks>
    /// **Names rather than a count, and both directories.** These are shared by the whole logon
    /// session, so a count changes when anything else on the machine creates or removes a key, and
    /// a test that compared counts failed while nothing was wrong. Comparing names and asking only
    /// whether one appeared leaves the far commoner case, something else tidying up its own key,
    /// unable to fail anything. And `Crypto\Keys` is CNG only: a regression that wrote through a
    /// legacy provider would land in `Crypto\RSA` and go unseen, so both are read.
    ///
    /// Here rather than on one fixture because more than one of them asks the question, and two
    /// copies of it would be two answers to how it is asked. In `tests/Shared` rather than in the
    /// unit tier because the host tier asks it too and cannot reference that assembly, which is
    /// what this folder is for: the file is compiled into each of them from one source.
    ///
    /// Every fixture that issues a client loads a key that lands here, so in a tier that runs
    /// fixtures in parallel a test asking this question is `NonParallelizable`, and the fixtures
    /// writing keys are not.
    ///
    /// **And a lock for the processes beside this one**, which no mark reaches. Two test runs at
    /// once, two gates or a second `dotnet test`, write their keys into the same two directories,
    /// because the directories belong to the account and not to the process: pointing `APPDATA`
    /// elsewhere was measured to change nothing. So a test watching takes <see cref="Lock"/> for
    /// as long as it watches, and a helper writing takes it for the moment of the load that writes,
    /// and a key one run writes never lands inside another run's window. A key written before the
    /// window and removed during it is no trouble, since only what appears is counted.
    /// </remarks>
    internal static class PrivateKeyFiles
    {
        /// <summary>
        /// Held while a key is written here or while the directories are watched for one, by
        /// every test process this account runs.
        /// </summary>
        /// <remarks>
        /// A named mutex, so a process that dies holding it gives it up rather than keeping it,
        /// which reaches the next holder as an abandoned wait and is taken as acquired. A mutex
        /// another process cannot open, or one held for longer than the patience here, leaves
        /// the caller unlocked rather than stuck: a watcher still tells a busy machine from a
        /// regression on its own, which is what it did before there was a lock.
        /// </remarks>
        public static IDisposable Lock()
        {
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, LockName);
            }
            catch (UnauthorizedAccessException)
            {
                return Unlocked.Instance;
            }

            try
            {
                if (!mutex.WaitOne(LockPatience))
                {
                    mutex.Dispose();
                    return Unlocked.Instance;
                }
            }
            catch (AbandonedMutexException)
            {
                // Its holder ended without letting go, and the wait was granted all the same.
            }

            return new Held(mutex);
        }

        private const string LockName = @"Local\KeePassGate.Tests.PrivateKeyFiles";

        private static readonly TimeSpan LockPatience = TimeSpan.FromMinutes(2);

        public static HashSet<string> Names()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directories())
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    names.Add(file);
                }
            }

            return names;
        }

        /// <summary>What appeared while something was being done, which is the only direction that means anything.</summary>
        public static string[] Appeared(HashSet<string> before)
        {
            var after = Names();
            after.ExceptWith(before);
            return after.ToArray();
        }

        /// <summary>
        /// Every key file created while something ran, one it deleted again before returning
        /// included, which comparing <see cref="Names"/> before and after cannot see.
        /// </summary>
        /// <remarks>
        /// A certificate loaded without `EphemeralKeySet` writes its key into one of these
        /// directories and deletes it when the certificate is disposed, so a load inside a
        /// `using` leaves no name behind to find. A watcher sees the file arrive. Measured, its
        /// report comes before the load has returned; the wait afterwards is for a loaded machine,
        /// not a guess at how late the report is.
        /// </remarks>
        public static string[] CreatedDuring(Action action)
        {
            var created = new List<string>();
            var watchers = new List<FileSystemWatcher>();

            try
            {
                foreach (var watcher in Directories().Select(path => new FileSystemWatcher(path)
                         {
                             IncludeSubdirectories = true,
                             NotifyFilter = NotifyFilters.FileName,
                         }))
                {
                    watcher.Created += (sender, e) =>
                    {
                        lock (created)
                        {
                            created.Add(e.FullPath);
                        }
                    };

                    watcher.EnableRaisingEvents = true;
                    watchers.Add(watcher);
                }

                action();
                System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(250));
            }
            finally
            {
                foreach (var watcher in watchers)
                {
                    watcher.Dispose();
                }
            }

            lock (created)
            {
                return created.ToArray();
            }
        }

        /// <summary>Where this account's private keys are written, those of the two that exist.</summary>
        private static IEnumerable<string> Directories()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            foreach (var path in new[] { @"Microsoft\Crypto\Keys", @"Microsoft\Crypto\RSA" }
                         .Select(directory => Paths.Join(appData, directory))
                         .Where(Directory.Exists))
            {
                yield return path;
            }
        }

        /// <summary>The lock, let go on the thread that took it.</summary>
        private sealed class Held : IDisposable
        {
            private Mutex _mutex;

            public Held(Mutex mutex)
            {
                _mutex = mutex;
            }

            public void Dispose()
            {
                if (_mutex == null)
                {
                    return;
                }

                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
            }
        }

        /// <summary>What a caller holds when there was no lock to be had.</summary>
        private sealed class Unlocked : IDisposable
        {
            public static readonly Unlocked Instance = new Unlocked();

            public void Dispose()
            {
            }
        }
    }
}
