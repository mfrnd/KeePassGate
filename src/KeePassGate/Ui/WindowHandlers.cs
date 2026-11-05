using System;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// How a handler on a window of this plugin's own runs: whatever it throws that it did not name
    /// is caught here and recorded.
    /// </summary>
    /// <remarks>
    /// A kind of catch-all `DESIGN.md` §141 lists. WinForms runs these handlers from its own
    /// dispatch on KeePass's thread, with nothing of this plugin's above them, and it dispatches
    /// them wherever KeePass pumps messages, which includes the middle of KeePass's own save and
    /// its lock: a click, a timer's tick or a repaint is handled there as readily as anywhere. An
    /// exception escaping one reaches WinForms' own dialog, which inside such work holds every lock
    /// off while the gate goes on serving reads. So every handler on the Tools window, the approval
    /// dialogs, the question of a day, the authority chooser and the KPG pages with their pickers
    /// and menus runs through here.
    ///
    /// What a handler names it still catches itself and puts on its window. What reaches here is a
    /// defect, and the handler's work stops where the defect was, with the window left standing.
    ///
    /// **Recorded where the plugin says**, which it sets as it starts and puts back as it ends, the
    /// way `CaptureGuard.Wanted` is. With nothing set, as in a test that asks nothing about it,
    /// nothing is caught, so the failure arrives where it was thrown.
    /// </remarks>
    internal static class WindowHandlers
    {
        /// <summary>Where a failure is recorded, or null to catch nothing.</summary>
        internal static GateIncidents Incidents { get; set; }

        /// <summary>
        /// Runs <paramref name="work"/>, recording what it throws as <paramref name="what"/>, the
        /// sentence the log and the incident carry.
        /// </summary>
        public static void Run(string what, Action work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            try
            {
                work();
            }
            catch (Exception error) when (Incidents is GateIncidents incidents)
            {
                incidents.Record(what, error);
            }
        }

        /// <summary>
        /// The same for a handler that answers, which answers <paramref name="failed"/> when what
        /// it ran threw.
        /// </summary>
        public static T Run<T>(string what, Func<T> work, T failed)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            var answer = failed;
            Run(what, () => answer = work());
            return answer;
        }
    }
}
