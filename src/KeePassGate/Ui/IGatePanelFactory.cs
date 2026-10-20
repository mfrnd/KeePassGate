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
        /// A fresh control, one per dialog. The caller owns it and disposes it with the tab page.
        /// </summary>
        Control CreatePanel();
    }
}
