using System.Globalization;

namespace KeePassGate
{
    // Deliberately in the root namespace and not in `Ui`. It is implemented by the transport and
    // is to be read by the tab, and putting it under `Ui` made `GateListener` take a `using` on
    // the user interface to describe itself. The root is visible from every namespace in this
    // assembly without one.
    /// <summary>Whether the gate is listening, and where.</summary>
    public interface IGateStatus
    {
        bool IsListening { get; }

        int Port { get; }
    }

    /// <summary>
    /// One line telling an operator whether anything can reach this database. Separate from the
    /// panel because the wording has to be right and a form is a poor place to test it.
    /// </summary>
    public static class GateStatusText
    {
        /// <summary>One line, like everything else on the tab. The log is where the detail is.</summary>
        public const string NotListening =
            "The gate is not listening. Nothing can reach any database.";

        public const string Unknown = "";

        public static string For(IGateStatus status, bool isServed)
        {
            return For(status, isServed, null);
        }

        /// <summary>
        /// The same, with anything that went wrong in a way nobody predicted appended to it.
        /// </summary>
        /// <remarks>
        /// A few places in the plugin catch every exception, because in each of them an escaping
        /// one ends KeePass. They count what they caught, and this is where the count is to
        /// surface: on the line the operator already reads, without a row of its own, because the
        /// tab has no room for one. What happened is in the log; that it happened at all belongs
        /// here. TODO(step 6): the tab shows this line from step 6.
        /// </remarks>
        public static string For(IGateStatus status, bool isServed, string incidents)
        {
            var line = Listening(status, isServed);

            if (string.IsNullOrEmpty(incidents))
            {
                return line;
            }

            return string.IsNullOrEmpty(line) ? incidents : line + " " + incidents;
        }

        private static string Listening(IGateStatus status, bool isServed)
        {
            if (status == null)
            {
                return Unknown;
            }

            if (!status.IsListening)
            {
                return NotListening;
            }

            var where = "127.0.0.1:" + status.Port.ToString(CultureInfo.InvariantCulture);

            return isServed
                ? "Listening on " + where + "."
                : "Listening on " + where +
                  ". Nothing is served here until this database has an authority that can be used.";
        }
    }
}
