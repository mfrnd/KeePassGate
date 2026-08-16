using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeePassGate.Bridge
{
    /// <summary>
    /// Database paths compared the way KeePass compares them when it decides whether a database is
    /// already open: trimmed, and lowered in the culture KeePass runs in.
    /// </summary>
    /// <remarks>
    /// **KeePass's rule, and not the one Windows keeps for file names.** `MainForm` matches a path
    /// against the documents it has by `Path.Trim().ToLower()`, and that is what decides whether
    /// two spellings are one database or two. A comparison without case agreed with it under most
    /// cultures and not under Turkish, where a capital `I` lowers to an `i` without its dot:
    /// KeePass kept `Ideas.kdbx` and `ideas.kdbx` as two documents while the plugin took them for
    /// one, so the lock of one could take the other's cached target and the close of one release
    /// the other's lock. A path can be a URL as well, which the rule for file names was never
    /// about.
    ///
    /// **The culture is taken once, when this is made**, which is on KeePass's own thread, rather
    /// than read at every comparison: a connection thread compares too, and a key has to hash the
    /// same wherever it is looked up.
    /// </remarks>
    internal sealed class KeePassPaths : IEqualityComparer<string>
    {
        private readonly CultureInfo _culture;

        /// <summary>In the culture of the thread that makes it.</summary>
        public KeePassPaths()
            : this(CultureInfo.CurrentCulture)
        {
        }

        public KeePassPaths(CultureInfo culture)
        {
            if (culture == null)
            {
                throw new ArgumentNullException(nameof(culture));
            }

            _culture = culture;
        }

        public bool Equals(string x, string y)
        {
            return string.Equals(Normal(x), Normal(y), StringComparison.Ordinal);
        }

        public int GetHashCode(string obj)
        {
            var normal = Normal(obj);
            return normal == null ? 0 : StringComparer.Ordinal.GetHashCode(normal);
        }

        private string Normal(string path)
        {
            return path == null ? null : path.Trim().ToLower(_culture);
        }
    }
}
