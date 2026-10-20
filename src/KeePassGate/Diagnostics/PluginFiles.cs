using System;
using System.IO;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// Where the plugin keeps a file it writes for itself: the one the environment names, or one
    /// in a directory of its own under this account's application data.
    /// </summary>
    /// <remarks>
    /// **One answer for every such file**, so that whoever has found one has found them all, and
    /// so that no two can come to disagree about where the plugin keeps what it writes.
    /// </remarks>
    internal static class PluginFiles
    {
        /// <summary>The directory under this account's local application data.</summary>
        public const string DirectoryName = "KeePassGate";

        /// <summary>
        /// The file <paramref name="variable"/> names, made absolute, or
        /// <paramref name="fileName"/> in the plugin's own directory, or null when neither can be
        /// had.
        /// </summary>
        /// <remarks>
        /// **A name that is not a full path is made into one here, once.** Left as it was set, it
        /// was resolved on every write against whatever KeePass's working directory was at that
        /// moment, which is wherever it was started from and can change while it runs, so a file
        /// could be split across two with nothing lost and nothing said. And the one line written
        /// to tell the operator where the file is named a bare file name.
        /// </remarks>
        public static string Resolve(string variable, string fileName)
        {
            try
            {
                // Inside the guard with the rest of it. Reading an environment variable is a
                // permission a policy can withhold, and this runs from a constructor on the way
                // into KeePass, where an exception is not a missing file but a plugin that did not
                // load. A name `GetFullPath` cannot make sense of is refused by the same guard, and
                // whoever asked says so rather than writing nowhere quietly.
                var named = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(named))
                {
                    return Path.GetFullPath(named);
                }

                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return string.IsNullOrEmpty(local)
                    ? null
                    : Path.Combine(Path.Combine(local, DirectoryName), fileName);
            }
            catch (Exception error) when (Expected.OfStorage(error))
            {
                return null;
            }
        }
    }
}
