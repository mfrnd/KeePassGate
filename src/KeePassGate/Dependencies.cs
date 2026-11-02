using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using KeePassGate.Diagnostics;

namespace KeePassGate
{
    /// <summary>
    /// The assemblies this plugin ships beside itself, loaded as it starts.
    /// </summary>
    /// <remarks>
    /// **So that a missing or replaced one refuses the start rather than its first use.** The
    /// runtime loads an assembly the first time something needs a type from it, and KeePass checks
    /// what a plugin references once for each build of it and remembers that the check passed. A
    /// dependency removed or replaced after that is otherwise met wherever the plugin first reaches
    /// for it, with the plugin loaded and nothing to show that anything is wrong. Loaded here, a
    /// missing one, or one whose assembly version is not the one the plugin was built against,
    /// throws `FileNotFoundException` naming the assembly and the version, inside `Initialize`,
    /// which writes why to the log and hands the exception on to KeePass, whose warning that the
    /// plugin cannot be loaded names it as well.
    ///
    /// **And one that is older than the copy beside the plugin refuses the start too**, which the
    /// runtime does not see to. A reference binds by identity, and the identity carries the
    /// assembly version, which Bouncy Castle keeps at 2.0.0.0 for every 2.x release and
    /// Newtonsoft.Json at 13.0.0.0 for every 13.x. So when another plugin has loaded its own copy
    /// first, from its own directory, this plugin runs on that copy whatever its directory holds,
    /// and Bouncy Castle before 2.6 lacks what the issuer calls: nothing could be issued and no
    /// open database served, with nothing in the log naming the cause. The file version is what
    /// moves, so each one loaded is held to the file version of the copy of the same name beside
    /// the plugin, and a loaded one that is older throws `FileLoadException` naming both versions
    /// and both paths, which `Initialize` reports as it reports a missing one. A newer one is let
    /// be, since a release keeps what an older one offered, and so is one with no copy beside the
    /// plugin to compare with.
    ///
    /// **Each through a type the plugin already names in it**, so nothing is added to the
    /// references KeePass checks, and each through a type rather than by name: a type binds the
    /// way the plugin's own references do, to a copy of the identity it was built against already
    /// loaded or else to the one in the plugin's own directory, where an assembly asked for by
    /// name is looked for beside `KeePass.exe`.
    ///
    /// **Never inlined**, because the runtime resolves these types as it compiles the method that
    /// names them. Written into `Initialize`, a missing assembly would fail the call before the
    /// guard inside it, where nothing is logged.
    ///
    /// `SharpYaml` is not among them, since the plugin names nothing in it:
    /// `Microsoft.OpenApi.Readers` loads it the first time a document is read.
    /// </remarks>
    internal static class Dependencies
    {
        /// <summary>
        /// Loads each one, refuses one older than its copy here, and answers with what it loaded.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IReadOnlyList<Assembly> Load()
        {
            var loaded = new[]
            {
                typeof(Newtonsoft.Json.JsonTextReader).Assembly,
                typeof(Org.BouncyCastle.Utilities.Arrays).Assembly,
                typeof(Microsoft.OpenApi.Models.OpenApiDocument).Assembly,
                typeof(Microsoft.OpenApi.Readers.OpenApiStringReader).Assembly
            };

            RefuseOlder(loaded, Beside(), FileVersionAt);
            return loaded;
        }

        /// <summary>
        /// Throws `FileLoadException` for the first assembly loaded from a file older than the file
        /// of the same name in <paramref name="beside"/>.
        /// </summary>
        /// <param name="loaded">What the runtime bound the plugin's references to.</param>
        /// <param name="beside">The directory the plugin was loaded from.</param>
        /// <param name="fileVersionAt">
        /// The file version of the file at a path, or null when there is none to read.
        /// </param>
        internal static void RefuseOlder(
            IEnumerable<Assembly> loaded, string beside, Func<string, Version> fileVersionAt)
        {
            foreach (var assembly in loaded)
            {
                var name = assembly.GetName().Name;
                var shipped = Paths.Join(beside, name + ".dll");
                var wanted = fileVersionAt(shipped);
                var running = fileVersionAt(assembly.Location);
                if (wanted == null || running == null || running >= wanted)
                {
                    continue;
                }

                throw new FileLoadException(
                    name + " " + running + " was loaded from " + assembly.Location +
                    ", older than the " + wanted + " the plugin ships at " + shipped +
                    ". Another plugin loaded its own copy first, and this plugin would run on it, so it does not start.",
                    assembly.Location);
            }
        }

        /// <summary>Where the plugin was loaded from, which is where it ships its own.</summary>
        private static string Beside()
        {
            return Path.GetDirectoryName(new Uri(typeof(Dependencies).Assembly.CodeBase).LocalPath);
        }

        /// <summary>A file's version as its resource has it, or null when there is none.</summary>
        private static Version FileVersionAt(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileVersion == null
                ? null
                : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
    }
}
