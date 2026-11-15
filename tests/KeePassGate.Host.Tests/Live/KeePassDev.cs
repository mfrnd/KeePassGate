using System;
using System.IO;
using System.Linq;
using System.Reflection;
using KeePassGate.Diagnostics;
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
            get { return Paths.Join(Directory, "KeePass.exe"); }
        }

        /// <summary>
        /// Names a KeePass for the end to end tier to copy and start that is meant to be another
        /// release than the tests were built against.
        /// </summary>
        /// <remarks>
        /// For the question a new KeePass release asks of a plugin already shipped: whether the
        /// plugin as it was built, against the release before, loads and serves in the new
        /// release. The plugin a session installs is the build beside the tests, so with this set
        /// the tests and the plugin are compiled against the KeePass of <see cref="Directory"/>
        /// and the plugin runs in the KeePass named here. `build.ps1 -RunInKeePassDir` sets it.
        ///
        /// **A variable of its own, not a second meaning for <see cref="DirectoryVariable"/>.**
        /// That variable moves where the same KeePass is found, and a release found there that the
        /// tests were not built against is a mistake the tier ignores itself over. Here the
        /// difference is what was asked for, so nothing compares the two.
        /// </remarks>
        public const string RunInVariable = "KPG_RUN_IN_KEEPASS_DIR";

        /// <summary>
        /// The directory of the KeePass named to run in, or null when the tier runs in the KeePass
        /// it was built against.
        /// </summary>
        public static string RunInDirectory
        {
            get
            {
                var named = Environment.GetEnvironmentVariable(RunInVariable);
                return string.IsNullOrEmpty(named) ? null : named;
            }
        }

        /// <summary>The directory a session copies its throwaway KeePass from.</summary>
        public static string SourceDirectory
        {
            get { return RunInDirectory ?? Directory; }
        }

        /// <summary>
        /// True when the KeePass named to run in is there to be copied, with the two releases in
        /// <paramref name="report"/>, and false with what is missing.
        /// </summary>
        /// <remarks>
        /// Any release is taken, the release the tests were built against among them, since which
        /// release to run in is the operator's choice here. What is not taken is a directory with
        /// no KeePass in it, which a session fails over, not ignores itself over: a tier asked to
        /// run in a KeePass that is not there has measured nothing, and green would read as an
        /// answer.
        /// </remarks>
        public static bool NamedToRunInIsThere(out string report)
        {
            var exe = Paths.Join(RunInDirectory, "KeePass.exe");
            if (!File.Exists(exe))
            {
                report = RunInVariable + " names " + RunInDirectory + ", and no KeePass.exe is there.";
                return false;
            }

            var file = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            var named = new Version(file.FileMajorPart, file.FileMinorPart, file.FileBuildPart, file.FilePrivatePart);
            report = "The plugin and the tests were built against KeePass " +
                     typeof(PwDatabase).Assembly.GetName().Version + ", and the plugin runs in KeePass " + named +
                     " from " + RunInDirectory + ".";
            return true;
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
