using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeePassGate.Tests
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
    /// copies of it would be two answers to how it is asked. `OpenDatabaseTargetsTests` keeps a
    /// third, which is unavoidable: the host tier cannot reference this assembly.
    /// </remarks>
    internal static class PrivateKeyFiles
    {
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
                foreach (var path in Directories())
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName,
                    };

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

            foreach (var directory in new[] { @"Microsoft\Crypto\Keys", @"Microsoft\Crypto\RSA" })
            {
                var path = Path.Combine(appData, directory);
                if (Directory.Exists(path))
                {
                    yield return path;
                }
            }
        }
    }
}
