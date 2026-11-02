using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        private const string Beside = @"C:\KeePass\Plugins\KeePassGate";
        private const string Elsewhere = @"C:\KeePass\Plugins\Another";
        private const string BouncyCastle = "BouncyCastle.Cryptography";

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

        [Test]
        public void A_copy_another_plugin_loaded_first_at_an_older_file_version_refuses_the_start()
        {
            // Bouncy Castle keeps the assembly version 2.0.0.0 for every 2.x release, so the runtime
            // binds this plugin to whichever copy another plugin loaded first, and only the file
            // version tells them apart. Both versions and both paths are named, since the log is
            // where an operator finds out which other plugin to look at.
            var versions = new Dictionary<string, Version>
            {
                [Paths.Join(Beside, BouncyCastle + ".dll")] = new Version(2, 7, 0, 16391),
                [Paths.Join(Elsewhere, BouncyCastle + ".dll")] = new Version(2, 4, 0, 1)
            };
            var loaded = new[] { new LoadedFrom(BouncyCastle, Paths.Join(Elsewhere, BouncyCastle + ".dll")) };

            var refused = Assert.Throws<FileLoadException>(() => Dependencies.RefuseOlder(loaded, Beside, VersionIn(versions)));

            Assert.That(refused.Message, Does.Contain("2.4.0.1").And.Contain("2.7.0.16391"));
            Assert.That(refused.Message, Does.Contain(Elsewhere).And.Contain(Beside));
        }

        [Test]
        public void The_copy_beside_the_plugin_starts_it()
        {
            var shipped = Paths.Join(Beside, BouncyCastle + ".dll");
            var versions = new Dictionary<string, Version> { [shipped] = new Version(2, 7, 0, 16391) };

            Assert.DoesNotThrow(() => Dependencies.RefuseOlder(
                new[] { new LoadedFrom(BouncyCastle, shipped) }, Beside, VersionIn(versions)));
        }

        [Test]
        public void A_newer_copy_another_plugin_loaded_first_is_let_be()
        {
            // A release keeps what an older one offered, so newer is not what breaks the issuer.
            var versions = new Dictionary<string, Version>
            {
                [Paths.Join(Beside, BouncyCastle + ".dll")] = new Version(2, 7, 0, 16391),
                [Paths.Join(Elsewhere, BouncyCastle + ".dll")] = new Version(2, 8, 0, 1)
            };

            Assert.DoesNotThrow(() => Dependencies.RefuseOlder(
                new[] { new LoadedFrom(BouncyCastle, Paths.Join(Elsewhere, BouncyCastle + ".dll")) },
                Beside,
                VersionIn(versions)));
        }

        [Test]
        public void A_dependency_with_no_copy_beside_the_plugin_has_nothing_to_be_held_to()
        {
            var versions = new Dictionary<string, Version>
            {
                [Paths.Join(Elsewhere, BouncyCastle + ".dll")] = new Version(2, 4, 0, 1)
            };

            Assert.DoesNotThrow(() => Dependencies.RefuseOlder(
                new[] { new LoadedFrom(BouncyCastle, Paths.Join(Elsewhere, BouncyCastle + ".dll")) },
                Beside,
                VersionIn(versions)));
        }

        /// <summary>The file version at each path given, and none anywhere else.</summary>
        private static Func<string, Version> VersionIn(IDictionary<string, Version> versions)
        {
            return path =>
            {
                Version version;
                return path != null && versions.TryGetValue(path, out version) ? version : null;
            };
        }

        /// <summary>An assembly the runtime is taken to have bound, by its name and where it came from.</summary>
        private sealed class LoadedFrom : Assembly
        {
            private readonly string _name;
            private readonly string _location;

            public LoadedFrom(string name, string location)
            {
                _name = name;
                _location = location;
            }

            public override string Location
            {
                get { return _location; }
            }

            public override AssemblyName GetName()
            {
                return new AssemblyName(_name);
            }
        }
    }
}
