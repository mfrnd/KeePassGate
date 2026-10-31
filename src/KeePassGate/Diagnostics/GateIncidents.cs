using System;
using System.Collections.Generic;
using System.Globalization;

namespace KeePassGate.Diagnostics
{
    /// <summary>
    /// The failures nobody predicted, kept where the operator will see them.
    /// </summary>
    /// <remarks>
    /// Four places in this plugin catch every exception rather than a named one, because in those
    /// four an exception that escapes would do more harm than the defect it carries. Escaping a
    /// handler of KeePass's own events, it would stop KeePass's own work part way, a lock of every
    /// database among it, and then put WinForms' own error dialog over KeePass, offering to quit.
    /// Escaping the Tools menu item, it would bring up that dialog. Escaping the thread serving one
    /// connection, it would end the process. And escaping a step of `Terminate`, it would be thrown
    /// away by KeePass with every step after it left undone.
    ///
    /// A catch-all that only writes a log line is how an unexpected failure becomes an unnoticed
    /// one, so those four record here instead. The count reaches the plugin's own window under the
    /// Tools menu, which is the one place that can be reached with no database open, and the whole
    /// exception reaches the log. Anything caught
    /// here is a defect in this plugin or a change in KeePass, never a client behaving badly:
    /// clients are refused by name, and a refusal is an ordinary warning.
    ///
    /// Bounded, because it holds exceptions from a socket thread and an attacker chooses when
    /// those happen.
    ///
    /// **Two more roots are kept safe another way**, and nothing reaches here from them: the
    /// accept thread and the timer that ends a connection at its deadline. Each runs with nothing
    /// above it, as the connection thread does, and rather than catching everything, every call
    /// either makes is total or filtered. `GateListener.Drop` explains what keeps that true, and
    /// what a change to the accept loop has to do if it cannot.
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
        /// Reports one. Total by construction: this is called from inside a catch-all, and a
        /// reporter that throws would defeat the whole point of that catch.
        /// </summary>
        public void Record(string where, Exception error)
        {
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
