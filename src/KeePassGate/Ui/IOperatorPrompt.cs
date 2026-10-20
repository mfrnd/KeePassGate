using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Asking the person in front of the screen to confirm something they cannot undo.
    /// </summary>
    /// <remarks>
    /// An interface so that a test can answer without a human, and so that the one place a dialog
    /// is raised stays visible. Note the rule this does not break: no API request ever prompts.
    /// These prompts are reached from something the operator has just done, or once at startup
    /// when the gate could not take its port.
    /// </remarks>
    public interface IOperatorPrompt
    {
        /// <summary>True when the operator agrees. Anything else, including closing the box, is no.</summary>
        bool Confirm(string title, string question);

        /// <summary>
        /// Tells the operator something there is no answer to, and waits for them to acknowledge it.
        /// </summary>
        /// <remarks>
        /// Three callers: a gate that could not take its port, a port the operator has just changed
        /// on the window under Tools, and an authority they chose that could not be kept. None is a
        /// question, because nothing the operator can say here would change it, and none is a log
        /// line either, because the consequence lasts and the person it affects is the person in
        /// front of the screen.
        /// </remarks>
        void Tell(string title, string news);
    }

    /// <summary>The real one.</summary>
    public sealed class MessageBoxPrompt : IOperatorPrompt
    {
        public bool Confirm(string title, string question)
        {
            // Owned by whatever is in front, the way KeePass's own `MessageService` does it. An
            // ownerless box raised from inside a modal dialog can be ordered behind it, leaving
            // the operator with a dialog that will not respond and nothing saying why.
            return MessageBox.Show(
                Form.ActiveForm,
                question,
                title,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public void Tell(string title, string news)
        {
            // Owned for the reason `Confirm` gives. With nothing in front, which is a KeePass that
            // is minimized or in the tray, the owner is null and the box is a window of its own.
            MessageBox.Show(Form.ActiveForm, news, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
