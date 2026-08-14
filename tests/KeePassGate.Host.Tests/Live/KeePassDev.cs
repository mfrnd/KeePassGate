using System;
using System.IO;
using System.Linq;
using System.Reflection;
using KeePassLib;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Finds the KeePass the tests were built against, on disk and whole. The compiling reference
    /// is one file; running KeePass needs the directory around it.
    /// </summary>
    internal static class KeePassDev
    {
        /// <summary>Overrides the path compiled in, for a machine that keeps KeePass elsewhere.</summary>
        public const string DirectoryVariable = "KEEPASSGATE_KEEPASS_DIR";

        public static string Directory
        {
            get
            {
                var fromEnvironment = Environment.GetEnvironmentVariable(DirectoryVariable);
                if (!string.IsNullOrEmpty(fromEnvironment))
                {
                    return fromEnvironment;
                }

                var metadata = typeof(KeePassDev).Assembly
                    .GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == "KeePassDevDir");

                if (metadata == null || string.IsNullOrEmpty(metadata.Value))
                {
                    throw new InvalidOperationException(
                        "This build does not record where KeePass lives. Set " + DirectoryVariable + ".");
                }

                return metadata.Value;
            }
        }

        public static string ExePath
        {
            get { return Path.Combine(Directory, "KeePass.exe"); }
        }

        /// <summary>
        /// True when the directory holds the same KeePass the tests compiled against. A mismatch
        /// would mean the live test measures a different program from the rest of the suite.
        /// </summary>
        /// <remarks>
        /// **Every part of the version, compared as numbers.** This compared the text of the file's
        /// version with the first two parts of the compiled one, so a 2.61.0 passed as the 2.61.1
        /// the tests were built against, and so would a 2.610. KeePass ships patch releases, and
        /// `KEEPASSGATE_KEEPASS_DIR` is how a directory holding one of them gets pointed at.
        /// </remarks>
        public static bool MatchesTheAssemblyUnderTest(out string reason)
        {
            if (!File.Exists(ExePath))
            {
                reason = "No KeePass.exe at " + ExePath + ".";
                return false;
            }

            var file = System.Diagnostics.FileVersionInfo.GetVersionInfo(ExePath);
            var onDisk = new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart);
            var compiledAgainst = typeof(PwDatabase).Assembly.GetName().Version;

            if (!onDisk.Equals(compiledAgainst))
            {
                reason = "KeePass at " + ExePath + " is " + onDisk +
                         " but the tests were built against " + compiledAgainst + ".";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
