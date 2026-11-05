using System;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using KeePassGate.Ui;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// Where the plugin keeps a file it writes for itself: the one the environment names, or one
    /// in a directory of its own under this account's application data. And how such a file is
    /// set aside once it has grown.
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
                    : Paths.Join(local, DirectoryName, fileName);
            }
            catch (Exception error) when (Expected.OfStorage(error))
            {
                return null;
            }
        }

        /// <summary>
        /// Renames <paramref name="path"/> to the one previous file kept, <c>path.1</c>, in place of
        /// any file already there, or throws when the files or their directory do not let this
        /// account.
        /// </summary>
        /// <remarks>
        /// **What the access control lists allow is what happens, whatever privileges the account
        /// holds.** `File.Delete` and `File.Move`, which did this, open what they change for backup,
        /// so in a process with the backup and restore privileges enabled they pass any ACL, and
        /// Git Bash, run elevated, enables both in every process it starts. In a directory sealed for an
        /// append-only audit, which takes away removing and adding files there, the previous file
        /// was deleted and this file moved, and then the next could not be made, so nothing more
        /// was written to it. A handle opened for the right to delete and no other, without asking
        /// for backup, is given only what the ACLs give, and Windows holds the rename through it
        /// to the directory's ACL the same way. `FINDINGS.md` has the measurement.
        ///
        /// **One rename, never a removal and then a rename**, so the previous file goes only when
        /// this file takes its place. A directory that lets files be removed and not added lost the
        /// previous file to the removal, and then refused the rename.
        ///
        /// A reader holding either file holds the rename off, whatever it shares, and each caller
        /// answers that by letting the file grow past its bound until the next attempt.
        /// </remarks>
        public static void SetAside(string path)
        {
            using (var file = new FileStream(
                       path, FileMode.Open, FileSystemRights.Delete, FileShare.ReadWrite | FileShare.Delete, 1,
                       FileOptions.None))
            {
                var error = NativeMethods.SetFileInformationByHandle(file.SafeFileHandle, path + ".1");
                if (error == 0)
                {
                    return;
                }

                // Thrown as what `Expected.OfStorage` names, as `File.Move` threw it, so that the
                // callers' filters read this as they read any other file that would not move.
                var reason = path + " could not be set aside: " + new Win32Exception(error).Message;
                if (error == AccessDenied)
                {
                    throw new UnauthorizedAccessException(reason);
                }

                throw new IOException(reason, unchecked((int)0x80070000) | error);
            }
        }

        /// <summary>`ERROR_ACCESS_DENIED`, which a refusing ACL and a reader holding a file both answer.</summary>
        private const int AccessDenied = 5;
    }
}
