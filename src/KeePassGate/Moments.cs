using System;
using System.Globalization;

namespace KeePassGate
{
    /// <summary>
    /// The two ways this plugin writes a moment down, in one place.
    /// </summary>
    /// <remarks>
    /// **Three formats, and no fourth.** <see cref="WireFormat"/> is RFC 3339 and goes anywhere a
    /// machine reads it: the wire, an admission record, the audit. <see cref="OnScreenFormat"/> drops
    /// the `T` and the `Z` for a person reading a table, where a column of them is already headed by
    /// what they are and the separators are noise. <see cref="LogFormat"/> is the wire's with
    /// milliseconds, because two log lines written in the same second still happened in an order and
    /// a reader looking for what went wrong needs to see which came first.
    ///
    /// Here because there were four copies of those two, and one of them had spelled the separators
    /// without quoting them. That version was correct only because every caller happened to pass
    /// `InvariantCulture`: in a custom format string a bare `-` means "the date separator of the
    /// current culture" and a bare `:` means "the time separator", so the same code against a
    /// culture that uses dots writes `2026.08.26 11.04.07`. Every format string here quotes its
    /// separators, and there is now one of each to get right.
    ///
    /// Always in UTC, because a record that says when a client was admitted has to mean the same
    /// thing on the machine that wrote it and the machine reading it back.
    /// </remarks>
    internal static class Moments
    {
        /// <summary>RFC 3339, which is what a machine is given.</summary>
        public const string WireFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'";

        /// <summary>The same instant for a person reading a table.</summary>
        public const string OnScreenFormat = "yyyy'-'MM'-'dd HH':'mm':'ss";

        /// <summary>The wire's, with milliseconds, which is what a log line carries.</summary>
        public const string LogFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'";

        public static string Wire(DateTimeOffset when)
        {
            return when.ToUniversalTime().ToString(WireFormat, CultureInfo.InvariantCulture);
        }

        public static string Log(DateTime when)
        {
            return when.ToUniversalTime().ToString(LogFormat, CultureInfo.InvariantCulture);
        }

        public static string OnScreen(DateTimeOffset when)
        {
            return when.ToUniversalTime().ToString(OnScreenFormat, CultureInfo.InvariantCulture);
        }

        public static string OnScreen(DateTime when)
        {
            return when.ToUniversalTime().ToString(OnScreenFormat, CultureInfo.InvariantCulture);
        }
    }
}
