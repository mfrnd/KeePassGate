using System;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// One line per event, so a log can be read with a text search rather than a parser.
    /// An exception is folded onto the same line for the same reason.
    /// </summary>
    public static class GateLogFormat
    {
        public const string Prefix = "KPG";

        public const string InfoLevel = "INFO";
        public const string WarnLevel = "WARN";
        public const string ErrorLevel = "ERROR";

        /// <summary>
        /// Caught by one of the catch-alls. Its own level so that a log search for what went
        /// wrong finds the surprises separately from the refusals.
        /// </summary>
        public const string UnexpectedLevel = "UNEXPECTED";

        public static string Line(DateTime timestampUtc, string level, string message, Exception error)
        {
            var line = string.Concat(
                Moments.Log(timestampUtc),
                " ", Prefix,
                " ", level ?? string.Empty,
                " ", Flatten(message));

            if (error != null)
            {
                line = string.Concat(line, " | ", Flatten(error.ToString()));
            }

            return line;
        }

        /// <summary>
        /// Collapses the line breaks an exception brings with it, so one event stays one line.
        /// </summary>
        public static string Flatten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("\r\n", " | ")
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }
    }
}
