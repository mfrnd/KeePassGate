using System.Windows.Forms;
using KeePassGate.Authority;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The one database a dialog is about, as the plugin's own screens see it.
    /// </summary>
    public interface IGateDatabase
    {
        /// <summary>False when the dialog has no database behind it, which is refused rather than guessed at.</summary>
        bool IsOpen { get; }

        /// <summary>
        /// The database's own custom data, where everything this plugin owns that belongs to the
        /// database lives. A node's access rule is the exception, and lives on that node.
        /// </summary>
        IKeyValueStore PluginCustomData { get; }

        /// <summary>
        /// Tells the host there is something to save. It does not save: KeePass decides when, and
        /// a plugin that forces a write from inside a modal dialog is a plugin fighting its host.
        /// </summary>
        void MarkModified();
    }

    /// <summary>
    /// Finds the database behind a dialog. The only implementation that matters knows KeePass;
    /// everything above this interface does not.
    /// </summary>
    public interface IGateDatabaseSource
    {
        /// <summary>The database the given dialog is editing, or null when there is none.</summary>
        IGateDatabase ForDialog(Form dialog);
    }
}
