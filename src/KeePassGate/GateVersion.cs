using System;
using System.Reflection;
using KeePassGate.Diagnostics;

namespace KeePassGate
{
    /// <summary>
    /// The plugin's own version, and the commit it was built from, as the log and the status route
    /// report them.
    /// </summary>
    /// <remarks>
    /// Not shown on any surface of this plugin's own. KeePass's plugins dialog lists every plugin
    /// with its version, which is where somebody checking one looks and where the answer is right
    /// for all of them at once; repeating it here would be a row that window already owns.
    ///
    /// **Both come from one attribute.** The SDK writes the version into the informational version
    /// and appends the commit the build was made from after a plus sign, which is SemVer's build
    /// metadata; it finds the commit through Source Link, in the repository the build ran in.
    /// </remarks>
    public static class GateVersion
    {
        private static readonly string Carried = Read();
        private static readonly string Value = VersionOf(Carried);
        private static readonly string Commit = RevisionOf(Carried);

        /// <summary>Never null and never empty, because it goes on the wire and into the log.</summary>
        public static string Current
        {
            get { return Value; }
        }

        /// <summary>
        /// The commit this build was made from, or empty when it carries none.
        /// </summary>
        /// <remarks>
        /// What tells two builds of one version apart, which a version alone cannot, since every
        /// commit between two releases carries the same one. Empty rather than null, because it
        /// goes on the wire: a build from a tree that is not a repository carries none, and saying
        /// so is an answer. A build made with uncommitted changes carries the commit they were made
        /// on, because that is all the build can know.
        /// </remarks>
        public static string Revision
        {
            get { return Commit; }
        }

        private static string VersionOf(string carried)
        {
            var plus = carried.IndexOf('+');
            return plus < 0 ? carried : carried.Substring(0, plus);
        }

        private static string RevisionOf(string carried)
        {
            var plus = carried.IndexOf('+');
            return plus < 0 ? string.Empty : carried.Substring(plus + 1);
        }

        private static string Read()
        {
            try
            {
                var assembly = typeof(GateVersion).Assembly;

                var informational = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    assembly, typeof(AssemblyInformationalVersionAttribute));
                if (informational != null && !string.IsNullOrEmpty(informational.InformationalVersion))
                {
                    return informational.InformationalVersion;
                }

                var name = assembly.GetName();
                if (name != null && name.Version != null)
                {
                    return name.Version.ToString();
                }
            }
            catch (Exception error) when (Expected.OfReflection(error))
            {
                // No version attribute, or an assembly that will not say. The fallback stands.
            }

            return "unknown";
        }
    }
}
