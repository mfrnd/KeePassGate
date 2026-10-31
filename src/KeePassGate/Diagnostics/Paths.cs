using System;
using System.IO;
using System.Linq;
using System.Text;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// A path made of parts, none of which can take the place of the ones before it.
    /// </summary>
    /// <remarks>
    /// `Path.Combine` starts again at any part after the first that is rooted, and drops every part
    /// before it without a word: a directory joined with `C:\Windows` is `C:\Windows`. .NET
    /// Framework has no `Path.Join`, which is how .NET answered that later, so this is the join with
    /// the drop refused. A later part that is rooted, or that climbs with a `..` segment, throws,
    /// rather than ending up outside the directory it was meant to be inside. The first part is the
    /// base and is taken as it is. Everything else is `Path.Combine`'s join: one separator between
    /// two parts unless the first already ends in one, and an empty part adds nothing.
    ///
    /// **CodeQL reports every call to `Path.Combine`** as one that may drop its earlier parts, which
    /// is what keeps this the one way a path is joined here, in the plugin and in its tests.
    /// </remarks>
    internal static class Paths
    {
        /// <summary>
        /// <paramref name="first"/>, then each of <paramref name="rest"/> inside the one before it.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// A part after the first is rooted, or holds a `..` segment.
        /// </exception>
        public static string Join(string first, params string[] rest)
        {
            if (first == null)
            {
                throw new ArgumentNullException(nameof(first));
            }

            if (rest == null)
            {
                throw new ArgumentNullException(nameof(rest));
            }

            var joined = new StringBuilder(first);
            foreach (var part in rest)
            {
                if (part == null)
                {
                    throw new ArgumentNullException(nameof(rest));
                }

                if (part.Length == 0)
                {
                    continue;
                }

                if (Path.IsPathRooted(part))
                {
                    throw new ArgumentException(
                        "'" + part + "' is rooted, so it would replace what it is joined to.", nameof(rest));
                }

                if (Climbs(part))
                {
                    throw new ArgumentException(
                        "'" + part + "' climbs with '..', so it would leave what it is joined to.", nameof(rest));
                }

                if (joined.Length > 0 && !EndsInSeparator(joined))
                {
                    joined.Append(Path.DirectorySeparatorChar);
                }

                joined.Append(part);
            }

            return joined.ToString();
        }

        private static bool Climbs(string part)
        {
            return part.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..");
        }

        private static bool EndsInSeparator(StringBuilder joined)
        {
            var last = joined[joined.Length - 1];
            return last == Path.DirectorySeparatorChar
                || last == Path.AltDirectorySeparatorChar
                || last == Path.VolumeSeparatorChar;
        }
    }
}
