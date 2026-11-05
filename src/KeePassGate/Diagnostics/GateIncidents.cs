using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// The failures nobody predicted, kept where the operator will see them.
    /// </summary>
    /// <remarks>
    /// The places in this plugin that catch every exception rather than a named exception report
    /// here, and `DESIGN.md` §141 is the account of them: the kinds of place, why an exception
    /// escaping each would do more harm than the defect it carries, and the roots kept safe another
    /// way.
    ///
    /// A catch-all that only writes a log line is how an unexpected failure becomes an unnoticed
    /// failure, so they record here instead. The count reaches the plugin's own window under the
    /// Tools menu, which is the one place that can be reached with no database open, and the whole
    /// exception reaches the log. Anything caught here is a defect in this plugin or a change in
    /// KeePass, never a client behaving badly: clients are refused by name, and a refusal is an
    /// ordinary warning.
    ///
    /// Bounded, because it holds exceptions from a socket thread and an attacker chooses when
    /// those happen.
    /// </remarks>
    public sealed class GateIncidents
    {
        /// <summary>Enough to see a pattern, few enough that nothing accumulates.</summary>
        public const int Keep = 10;

        private readonly IGateLog _log;
        private readonly object _lock = new object();
        private readonly List<string> _recent = new List<string>();
        private int _count;

        public GateIncidents(IGateLog log)
        {
            _log = log ?? NullGateLog.Instance;
        }

        /// <summary>How many have happened since KeePass started.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        /// <summary>The most recent ones, newest last, for a test and for a support question.</summary>
        public IReadOnlyList<string> Recent
        {
            get
            {
                lock (_lock)
                {
                    return new List<string>(_recent);
                }
            }
        }

        /// <summary>
        /// Reports a failure. Total by construction: this is called from inside a catch-all, and a
        /// reporter that throws would defeat the whole point of that catch.
        /// </summary>
        /// <remarks>
        /// **A debugger attached is stopped here**, with the exception in hand, because every
        /// place that catches everything reports here and a debugger is otherwise told nothing:
        /// what was caught is caught, and continuing carries on as it would with no debugger.
        /// </remarks>
        public void Record(string where, Exception error)
        {
            if (Debugger.IsAttached)
            {
                Debugger.Break();
            }

            var description = (where ?? "somewhere") +
                              (error == null ? string.Empty : ": " + error.GetType().Name);

            lock (_lock)
            {
                _count++;
                _recent.Add(description);
                if (_recent.Count > Keep)
                {
                    _recent.RemoveAt(0);
                }
            }

            try
            {
                _log.Unexpected(where ?? "something failed unexpectedly", error);
            }
            // codeql[cs/catch-of-all-exceptions] the incident the catch-alls record, which may not throw
            catch (Exception)
            {
                // A log that throws while reporting an incident cannot be reported to the log.
                // This runs inside the catch-alls, so a failure here has nowhere left to go.
            }
        }

        /// <summary>
        /// One line for the window under the Tools menu, or nothing at all when nothing has
        /// gone wrong. Deliberately says nothing about what happened: the log has that, and this is
        /// a row rather than an account.
        /// </summary>
        public string Summary
        {
            get
            {
                var count = Count;
                if (count == 0)
                {
                    return string.Empty;
                }

                return count == 1
                    ? "One unexpected failure, see the log."
                    : count.ToString(CultureInfo.InvariantCulture) + " unexpected failures, see the log.";
            }
        }
    }
}
