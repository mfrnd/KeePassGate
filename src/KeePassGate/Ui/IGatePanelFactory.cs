using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Builds the contents of the plugin's tab. Separate from the code that attaches the tab
    /// because the two change for different reasons: attaching is finished, whereas the contents
    /// grow with every later step of the build order.
    /// </summary>
    public interface IGatePanelFactory
    {
        /// <summary>
        /// A fresh control for this dialog, one per dialog. The caller owns it and disposes it with
        /// the tab page.
        /// </summary>
        /// <param name="dialog">
        /// The host dialog, so that a panel can find what it is about. What that means is the
        /// factory's business: the code that attaches the tab knows nothing of databases.
        /// </param>
        Control CreatePanel(Form dialog);
    }
}
