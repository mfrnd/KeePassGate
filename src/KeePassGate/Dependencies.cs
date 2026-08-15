using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

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
    /// for it, with the plugin loaded and saying nothing is wrong. Loaded here, a missing one, or
    /// one of another version than the plugin was built against, throws `FileNotFoundException`
    /// naming the assembly and the version, inside `Initialize`, which refuses the start and writes
    /// why to the log.
    ///
    /// **Each through a type the plugin already names in it**, so nothing is added to the
    /// references KeePass checks, and each through a type rather than by name: a type binds the
    /// way the plugin's own references do, in the plugin's own directory and at the version it
    /// was built against, where an assembly asked for by name is looked for beside `KeePass.exe`.
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
        /// <summary>Loads each one, and answers with what it loaded.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IReadOnlyList<Assembly> Load()
        {
            return new[]
            {
                typeof(Newtonsoft.Json.JsonTextReader).Assembly,
                typeof(Org.BouncyCastle.Utilities.Arrays).Assembly,
                typeof(Microsoft.OpenApi.Models.OpenApiDocument).Assembly,
                typeof(Microsoft.OpenApi.Readers.OpenApiStringReader).Assembly
            };
        }
    }
}
