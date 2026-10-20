using System;
using System.Reflection;
using KeePassGate.Diagnostics;

namespace KeePassGate
{
    /// <summary>
    /// The plugin's own version, as the log and the status route report it.
    /// </summary>
    /// <remarks>
    /// Not shown on any surface of this plugin's own. KeePass's plugins dialog lists every plugin
    /// with its version, which is where somebody checking one looks and where the answer is right
    /// for all of them at once; repeating it here would be a row that window already owns.
    /// </remarks>
    public static class GateVersion
    {
        private static readonly string Value = Read();

        /// <summary>Never null and never empty, because it goes on the wire and into the log.</summary>
        public static string Current
        {
            get { return Value; }
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
                    // Strip the source revision the SDK appends after a plus sign.
                    var plus = informational.InformationalVersion.IndexOf('+');
                    return plus < 0
                        ? informational.InformationalVersion
                        : informational.InformationalVersion.Substring(0, plus);
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
