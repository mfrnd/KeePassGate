using System;
using KeePassGate.Bridge;
using KeePassLib;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Whether the calling thread is inside a merge, asked of the real KeePass the way the plugin
    /// asks it, for a fixture that makes its own queue of turns.
    /// </summary>
    internal static class KeePassMerges
    {
        public static Func<bool> Watch()
        {
            return MergeWatch.Over(typeof(PwDatabase), KeePassContract.DatabaseMergeMethod);
        }
    }
}
