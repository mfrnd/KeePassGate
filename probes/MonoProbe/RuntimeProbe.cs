using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace MonoProbe
{
    /// <summary>Which runtime this is, so that every other file of lines says what produced it.</summary>
    internal static class RuntimeProbe
    {
        internal static void Run()
        {
            var mono = Type.GetType("Mono.Runtime");
            Report.Line("runtime.mono", mono != null);
            if (mono != null)
            {
                Report.Measure("runtime.mono-version", () =>
                    mono.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
            }

            Report.Line("runtime.clr-version", Environment.Version);
            Report.Line("runtime.os", Environment.OSVersion.VersionString);
            Report.Line("runtime.platform", Environment.OSVersion.Platform);
            Report.Line("runtime.pointer-size", IntPtr.Size);
            Report.Line("runtime.culture", CultureInfo.CurrentCulture.Name);
            Report.Line("runtime.ui-culture", CultureInfo.CurrentUICulture.Name);
            Report.Line("runtime.directory-separator", Path.DirectorySeparatorChar);

            // Where `PluginFiles` puts the plugin's log and audit by default.
            Report.Measure("runtime.local-application-data", () =>
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
    }
}
