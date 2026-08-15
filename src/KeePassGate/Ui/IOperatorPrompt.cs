using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Asking the person in front of the screen to confirm something they cannot undo.
    /// </summary>
    /// <remarks>
    /// An interface so that a test can answer without a human, and so that the one place a dialog
    /// is raised stays visible. Note the rule this does not break: no API request ever prompts.
    /// These prompts are only ever reached from a button the operator has just pressed.
    /// </remarks>
    public interface IOperatorPrompt
    {
        /// <summary>True when the operator agrees. Anything else, including closing the box, is no.</summary>
        bool Confirm(string title, string question);
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
    }
}
