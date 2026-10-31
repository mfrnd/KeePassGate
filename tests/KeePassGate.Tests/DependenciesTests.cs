using System;
using System.IO;
using System.Linq;
using KeePassGate.Diagnostics;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    [TestFixture]
    public sealed class DependenciesTests
    {
        /// <summary>
        /// What measuring coverage adds: a reference in the plugin it measures, and the assembly
        /// itself beside it. Nothing ships it.
        /// </summary>
        private const string CoverageShim = "Microsoft.VisualStudio.CodeCoverage.Shim";

        [Test]
        public void Loading_them_loads_every_assembly_the_plugin_names_and_ships_beside_itself()
        {
            // Asked of the plugin's own references rather than listed a second time here, so a
            // dependency added later fails this until it is loaded at the start as well. What is
            // not beside the plugin is the framework or KeePass, which the host already has.
            //
            // By full name, version included, because a reference binds only to the version it was
            // built against, and that is what makes a replaced one refuse the start too.
            var plugin = typeof(Dependencies).Assembly;
            var beside = Path.GetDirectoryName(new Uri(plugin.CodeBase).LocalPath);

            var shipped = plugin.GetReferencedAssemblies()
                .Where(name => File.Exists(Paths.Join(beside, name.Name + ".dll")))
                .Where(name => name.Name != CoverageShim)
                .Select(name => name.FullName);

            var loaded = Dependencies.Load().Select(assembly => assembly.FullName);

            Assert.That(loaded, Is.EquivalentTo(shipped));
        }
    }
}
