using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Adds the plugin's own controls to a host dialog and takes them away again. Total: an
    /// implementation reports failure rather than throwing, because it runs on the UI thread of a
    /// password manager while a dialog is opening.
    /// </summary>
    public interface IFormDecorator
    {
        /// <summary>
        /// Decorates the form. Idempotent per form, so a second call adds nothing.
        /// </summary>
        /// <returns>True when the decoration is in place after the call.</returns>
        bool Attach(Form form);

        /// <summary>Removes the decoration and disposes what was added. A no-op if not attached.</summary>
        void Detach(Form form);

        /// <summary>Detaches from every form still attached, in the order they were attached.</summary>
        void DetachAll();
    }
}
