using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Tells the operator, as KeePass exits, about standing answers to renewals that no database
    /// took, which go with it.
    /// </summary>
    /// <remarks>
    /// The operator's ruling of 2026-10-14: an answer forever or until a date given while KeePass
    /// was busy holds from memory and is written by the next save of that database, and an answer
    /// still unwritten is told to the operator rather than only to the log. Exit is when that
    /// becomes true for good: a lock or a close keeps it, and the next save after an unlock or an
    /// open writes it. Told once, on the way out, naming each certificate, so that the operator
    /// knows which programs may be asked about again; `OwedAnswerNotice` has the words, and why
    /// they give no cause.
    ///
    /// **A Windows logoff or shutdown gets the log line and no box**, the operator's ruling of
    /// 2026-11-08. KeePass unloads its plugins from its main window's `FormClosed`, which WinForms
    /// raises inside the session's end, and a modal box there holds the logoff up with nobody at
    /// the desk to answer it until Windows gives up on KeePass, unread either way. So the main
    /// window's `FormClosing` is watched for the reason WinForms gives it, which at a session's
    /// end is `CloseReason.WindowsShutDown` (measured, and the reason KeePass's own handler reads
    /// to skip minimizing to the tray). An exit the operator starts still shows the box, since
    /// somebody is there to read it.
    ///
    /// **Once**, because `Dispose` on the plugin runs `Terminate` as well and nothing orders the
    /// two: an answer lost once is not lost again.
    /// </remarks>
    public sealed class OwedAnswersAtExit : IDisposable
    {
        private readonly Func<IReadOnlyList<OwedAnswer>> _owed;
        private readonly IOperatorPrompt _prompt;
        private Form _window;
        private bool _told;

        /// <summary>
        /// A notice that reads what is owed when it is told, and tells it through
        /// <paramref name="prompt"/>.
        /// </summary>
        /// <param name="owed">The answers no database has taken yet.</param>
        /// <param name="prompt">How the operator is told.</param>
        public OwedAnswersAtExit(Func<IReadOnlyList<OwedAnswer>> owed, IOperatorPrompt prompt)
        {
            if (owed == null)
            {
                throw new ArgumentNullException(nameof(owed));
            }

            if (prompt == null)
            {
                throw new ArgumentNullException(nameof(prompt));
            }

            _owed = owed;
            _prompt = prompt;
        }

        /// <summary>
        /// Whether the last close KeePass's main window was asked for is Windows ending the
        /// session.
        /// </summary>
        public bool SessionEnding { get; private set; }

        /// <summary>
        /// Watches <paramref name="window"/>, KeePass's main window, for why it is closing.
        /// </summary>
        public void Watch(Form window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            Dispose();
            _window = window;
            _window.FormClosing += OnClosing;
        }

        /// <summary>
        /// Writes the answers still owed to the log and, unless Windows is ending the session, puts
        /// them in front of the operator. Nothing at all when nothing is owed, or when this has
        /// told it already.
        /// </summary>
        public void Tell(IGateLog log)
        {
            if (_told)
            {
                return;
            }

            var owed = _owed();
            if (owed.Count == 0)
            {
                return;
            }

            _told = true;

            if (log != null)
            {
                log.Warn(OwedAnswerNotice.LogLine(owed));
            }

            if (!SessionEnding)
            {
                _prompt.Tell(GateName.Full, OwedAnswerNotice.Message(owed));
            }
        }

        public void Dispose()
        {
            if (_window != null)
            {
                _window.FormClosing -= OnClosing;
                _window = null;
            }
        }

        /// <summary>
        /// What the window's `FormClosing` hands over, taken as it comes.
        /// </summary>
        /// <remarks>
        /// The latest close decides, so a session end something else refused, with an exit the
        /// operator asks for afterwards, still shows the box. A handler of KeePass's own window
        /// event, and kept total rather than wrapped: it reads one value and stores one flag.
        /// </remarks>
        internal void OnClosing(object sender, FormClosingEventArgs e)
        {
            SessionEnding = e != null && e.CloseReason == CloseReason.WindowsShutDown;
        }
    }
}
