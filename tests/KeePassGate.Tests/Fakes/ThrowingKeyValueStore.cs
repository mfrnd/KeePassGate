using System;
using System.Collections.Generic;
using KeePassGate.Authority;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A store that will not answer at all, for the paths that have to survive one.
    /// </summary>
    /// <remarks>
    /// `InvalidOperationException` because that is what a `StringDictionaryEx` throws when a
    /// connection thread enumerates it while the user interface thread writes, which
    /// `Expected.OfCustomData` names and is what every caller here has to tolerate.
    ///
    /// Here rather than nested in a fixture: two of them had already been written, character for
    /// character, in two files of the same assembly.
    /// </remarks>
    internal sealed class ThrowingKeyValueStore : IKeyValueStore
    {
        public string Get(string key)
        {
            throw new InvalidOperationException("no");
        }

        public void Set(string key, string value)
        {
            throw new InvalidOperationException("no");
        }

        public void Remove(string key)
        {
            throw new InvalidOperationException("no");
        }

        public IEnumerable<string> Keys
        {
            get { throw new InvalidOperationException("no"); }
        }
    }
}
