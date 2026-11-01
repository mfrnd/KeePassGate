using System;
using System.Globalization;

namespace MonoProbe
{
    /// <summary>
    /// The culture facts the plugin and the client rest on, both measured on .NET Framework:
    /// KeePass telling paths apart by lowering them in the current culture, placeholders compared
    /// without case in .NET Framework's terms (where the dotless i and the long s are letters of
    /// their own), and how a culture orders the keys under `KPG`, which is what the dictionaries
    /// KeePass builds on its own thread are sorted by.
    /// </summary>
    internal static class CultureProbe
    {
        private static readonly string[] Cultures = { "en-US", "tr-TR", "de-DE" };

        internal static void Run()
        {
            foreach (var name in Cultures)
            {
                var culture = new CultureInfo(name);
                Report.Measure("culture." + name + ".lower-I", () => Code("I".ToLower(culture)));
                Report.Measure("culture." + name + ".upper-i", () => Code("i".ToUpper(culture)));
                Report.Measure("culture." + name + ".path-equal", () =>
                    "C:\\DATA\\Info.kdbx".Trim().ToLower(culture) == "c:\\data\\info.kdbx".Trim().ToLower(culture));
                Report.Measure("culture." + name + ".compare-KPG-kpg", () =>
                    Math.Sign(string.Compare("KPG", "kpg", false, culture)));
                Report.Measure("culture." + name + ".compare-dot-dash", () =>
                    Math.Sign(string.Compare("KPG.Profiles", "KPG-Profiles", false, culture)));
                Report.Measure("culture." + name + ".compare-profiles-revoked", () =>
                    Math.Sign(string.Compare("KPG.Profiles", "KPG.Revoked.ab", false, culture)));
                Report.Measure("culture." + name + ".compare-ignorable", () =>
                    Math.Sign(string.Compare("KPG.Pro\u00ADfiles", "KPG.Profiles", false, culture)));
            }

            Report.Measure("culture.invariant-upper-dotless-i", () => Code(char.ToUpperInvariant('\u0131').ToString()));
            Report.Measure("culture.invariant-upper-long-s", () => Code(char.ToUpperInvariant('\u017F').ToString()));
            Report.Measure("culture.ordinal-ignore-case-TITLE-dotless", () =>
                string.Equals("TITLE", "T\u0131TLE", StringComparison.OrdinalIgnoreCase));
            Report.Measure("culture.ordinal-ignore-case-u-umlaut", () =>
                string.Equals("\u00FC", "\u00DC", StringComparison.OrdinalIgnoreCase));
        }

        private static string Code(string text)
        {
            var parts = new string[text.Length];
            for (var i = 0; i < text.Length; i++)
            {
                parts[i] = "U+" + ((int)text[i]).ToString("X4");
            }

            return string.Join(" ", parts);
        }
    }
}
