using System;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A message of the plugin's own, put in front of the operator once whatever just closed has
    /// closed, and once KeePass is not part way through work of its own.
    /// </summary>
    /// <remarks>
    /// **Posted, never shown where it is asked for**, so that the window that has just closed, a
    /// node's dialog or the renewal dialog, has been handled first, and so that nothing waits for
    /// the operator to read it: a connection thread asking goes on to answer its client. Posted
    /// through `Unattended`, which follows the work across the post.
    ///
    /// **A message shown inside KeePass's own save stands inside that save.** The post runs at
    /// the first pump after it is made, and KeePass saves right after an entry's dialog where it
    /// saves after every edit, pumping as it saves. So KeePass is asked, as the writer asks it
    /// before a write, and while it is busy the message waits for an Idle: WinForms raises none
    /// inside `DoEvents`, where KeePass's own operations pump, and work posted again from posted
    /// work would run again in the same delivery. The handler lets go of Idle before it asks
    /// anything, so a failure records once rather than at every Idle after.
    /// </remarks>
    internal sealed class FreeMessage
    {
        private readonly Control _window;
        private readonly IOperatorPrompt _prompt;
        private readonly Func<string> _busy;
        private readonly GateIncidents _incidents;
        private readonly IGateLog _log;

        /// <summary>Tells through this prompt, over this window, once KeePass is free.</summary>
        /// <param name="window">
        /// A window on KeePass's thread that outlives whatever the message is about, where the
        /// message is posted to; null for a KeePass that names no main window, where the log alone
        /// tells.
        /// </param>
        /// <param name="prompt">What puts the message in front of the operator.</param>
        /// <param name="busy">
        /// Why KeePass is part way through work of its own, or null where it is not, asked on its
        /// thread; null where nobody can be asked, which tells at once.
        /// </param>
        /// <param name="incidents">Where a defect in telling is counted, or a count of its own.</param>
        public FreeMessage(Control window, IOperatorPrompt prompt, Func<string> busy, GateIncidents incidents, IGateLog log)
        {
            if (prompt == null)
            {
                throw new ArgumentNullException(nameof(prompt));
            }

            _window = window;
            _prompt = prompt;
            _busy = busy;
            _log = log ?? NullGateLog.Instance;
            _incidents = incidents ?? new GateIncidents(_log);
        }

        /// <summary>Tells the operator, from any thread, under this title.</summary>
        /// <param name="untold">What the log and the incident state where it could not be told.</param>
        public void Tell(string title, string news, string untold)
        {
            if (_window == null)
            {
                _log.Warn(untold + ": KeePass names no main window");
                return;
            }

            try
            {
                Unattended.Post(_window, () => WhenFree(title, news, untold), error => _incidents.Record(untold, error));
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A window that has gone, which is KeePass going: the log has what happened already.
                _log.Warn(untold + ": " + error.Message);
            }
        }

        /// <summary>
        /// Tells at once where KeePass is not part way through work of its own, and otherwise at the
        /// first `Application.Idle` at which KeePass is not.
        /// </summary>
        private void WhenFree(string title, string news, string untold)
        {
            if (Asked() == null)
            {
                _prompt.Tell(title, news);
                return;
            }

            EventHandler idle = null;
            idle = (sender, e) =>
            {
                Application.Idle -= idle;
                WindowHandlers.Run(untold, () => WhenFree(title, news, untold));
            };
            Application.Idle += idle;
        }

        /// <summary>Why KeePass is busy, or null where it is not or nobody can be asked.</summary>
        private string Asked()
        {
            if (_busy == null)
            {
                return null;
            }

            try
            {
                return _busy();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // Unanswered is busy, as the writer takes it: the message waits for an answer.
                _log.Error("KeePass could not be asked whether it is busy", error);
                return error.Message;
            }
        }
    }
}
