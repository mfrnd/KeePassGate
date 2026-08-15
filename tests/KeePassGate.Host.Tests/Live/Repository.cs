using System.IO;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The checkout these tests were built from, found by walking up from them.
    /// </summary>
    /// <remarks>
    /// Walked rather than written as a path relative to the test assembly, which would be four
    /// directories of build layout written down in a test and wrong the day one of them changes.
    /// The solution marks the top.
    /// </remarks>
    internal static class Repository
    {
        /// <summary>The directory holding the solution.</summary>
        public static string Root
        {
            get
            {
                var at = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (at != null)
                {
                    if (File.Exists(Path.Combine(at.FullName, "KeePassGate.sln")))
                    {
                        return at.FullName;
                    }

                    at = at.Parent;
                }

                throw new DirectoryNotFoundException(
                    "No KeePassGate.sln above " + TestContext.CurrentContext.TestDirectory +
                    ": the end to end tier runs from a build of this repository.");
            }
        }
    }
}
