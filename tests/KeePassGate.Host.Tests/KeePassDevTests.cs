using System;
using System.CodeDom.Compiler;
using System.IO;
using KeePassGate.Diagnostics;
using KeePassGate.Host.Tests.Live;
using Microsoft.CSharp;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// Whether the KeePass the end to end tier would start is the one these tests were built
    /// against, which decides whether that tier runs at all.
    /// </summary>
    /// <remarks>
    /// Asked of programs compiled here that carry nothing but a version, because the question is
    /// about the version and no second KeePass release is ever provided. `KEEPASSGATE_KEEPASS_DIR`
    /// points the check at them, which is the path a machine keeping its own KeePass takes.
    /// </remarks>
    [TestFixture]
    [NonParallelizable]
    public sealed class KeePassDevTests
    {
        private static readonly Version CompiledAgainst = typeof(KeePassLib.PwDatabase).Assembly.GetName().Version;

        [Test]
        public void The_release_the_tests_were_built_against_is_accepted()
        {
            string reason;

            Assert.That(MatchesAt(CompiledAgainst, out reason), Is.True, reason);
        }

        [Test]
        public void Another_patch_release_of_the_same_minor_version_is_refused()
        {
            string reason;
            var patch = new Version(
                CompiledAgainst.Major, CompiledAgainst.Minor, CompiledAgainst.Build + 1, CompiledAgainst.Revision);

            Assert.That(MatchesAt(patch, out reason), Is.False, "a different KeePass would be driven");
            Assert.That(reason, Does.Contain(patch.ToString()), "and the refusal names what was found");
        }

        [Test]
        public void A_minor_version_that_only_begins_with_the_same_digits_is_refused()
        {
            string reason;
            var longer = new Version(
                CompiledAgainst.Major, CompiledAgainst.Minor * 10, CompiledAgainst.Build, CompiledAgainst.Revision);

            Assert.That(MatchesAt(longer, out reason), Is.False);
        }

        /// <summary>
        /// Asks the check about a program carrying this version, in a directory of its own.
        /// </summary>
        private static bool MatchesAt(Version version, out string reason)
        {
            var directory = Paths.Join(Path.GetTempPath(), "kpg-release-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var was = Environment.GetEnvironmentVariable(KeePassDev.DirectoryVariable);

            try
            {
                Compile(Paths.Join(directory, "KeePass.exe"), version);
                Environment.SetEnvironmentVariable(KeePassDev.DirectoryVariable, directory);

                return KeePassDev.MatchesTheAssemblyUnderTest(out reason);
            }
            finally
            {
                Environment.SetEnvironmentVariable(KeePassDev.DirectoryVariable, was);
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// An empty program whose file version is this one, which is all the check reads.
        /// </summary>
        private static void Compile(string path, Version version)
        {
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = path };
                var results = compiler.CompileAssemblyFromSource(
                    options,
                    "[assembly: System.Reflection.AssemblyFileVersion(\"" + version + "\")]\n" +
                    "internal static class Program { private static void Main() { } }\n");

                Assert.That(results.Errors.HasErrors, Is.False, "the stand-in did not compile");
            }
        }
    }
}
