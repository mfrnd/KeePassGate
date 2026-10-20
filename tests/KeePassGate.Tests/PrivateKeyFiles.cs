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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in new[] { @"Microsoft\Crypto\Keys", @"Microsoft\Crypto\RSA" })
            {
                var path = Path.Combine(appData, directory);
                if (!Directory.Exists(path))
                {
                    continue;
                }

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
    }
}
