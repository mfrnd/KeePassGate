using System;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Tells the KPG tab, on KeePass's own thread, of an answer to a renewal that the plugin's
    /// memory holds rather than the database.
    /// </summary>
    /// <remarks>
    /// **The clients page draws a client's name red from both**: the standing answers the database
    /// keeps, and those `SessionRefusals` holds, an answer for this session and an answer the
    /// database could not take when it was given. A standing answer that lands is told to the page
    /// by the writer, through the admission notice. Nothing writes an answer for this session, and
    /// an owed answer is written only later, so a renewal refused that way while the settings
    /// dialog showed the page left the client's name black until something else drew the page.
    ///
    /// **Posted, and waited for by nobody.** The renewal handler gives the answer on a connection
    /// thread, and the notice is raised on KeePass's, where every page listening to it draws. The
    /// connection has a client to answer and nothing to learn from a redraw, so it does not wait
    /// for the redraw: `Unattended` follows the work across the post. A window that has gone
    /// refuses the post on the connection thread, where it is logged and the answer stands.
    /// </remarks>
    internal sealed class SessionAnswerNotice : IDisposable
    {
        private readonly SessionRefusals _refusals;
        private readonly Control _window;
        private readonly Action _tell;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;

        /// <summary>Listens to the answers held in memory from now until disposed.</summary>
        /// <param name="refusals">The plugin's one set of answers held in memory.</param>
        /// <param name="window">
        /// A window on KeePass's thread, which is where the notice is raised.
        /// </param>
        /// <param name="tell">Raises the admission notice, which the KPG tab listens to.</param>
        /// <param name="incidents">
        /// Where a defect in what is told is counted, or null for nowhere.
        /// </param>
        /// <param name="log">
        /// Where a window that has gone is written down, or null for nowhere.
        /// </param>
        public SessionAnswerNotice(
            SessionRefusals refusals,
            Control window,
            Action tell,
            GateIncidents incidents,
            IGateLog log)
        {
            if (refusals == null)
            {
                throw new ArgumentNullException(nameof(refusals));
            }

            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (tell == null)
            {
                throw new ArgumentNullException(nameof(tell));
            }

            _refusals = refusals;
            _window = window;
            _tell = tell;
            _incidents = incidents;
            _log = log ?? NullGateLog.Instance;

            _refusals.Answered += OnAnswered;
        }

        /// <summary>Stops telling, which the plugin does as it unloads.</summary>
        public void Dispose()
        {
            _refusals.Answered -= OnAnswered;
        }

        private void OnAnswered(object sender, EventArgs e)
        {
            try
            {
                Unattended.Post(_window, _tell, Failed);
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Warn("the KPG tab was not told of an answer to a renewal held in memory, " +
                          "so it shows the client as it was: " + error.Message);
            }
        }

        private void Failed(Exception error)
        {
            if (_incidents != null)
            {
                _incidents.Record(
                    "the KPG tab could not be told of an answer to a renewal held in memory", error);
            }
        }
    }
}
