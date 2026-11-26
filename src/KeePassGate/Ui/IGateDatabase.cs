using System.Windows.Forms;

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
        /// The plugin's store in the database, `STORE.md` §601: where everything the plugin keeps
        /// in the database is kept, read as it stands and written through the writer.
        /// </summary>
        Store.IGateStore Store { get; }

        /// <summary>
        /// What this database is called, for a screen that has to say which one it is talking
        /// about. Empty when there is nothing to call it.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Every group and entry in it a rule can be in force at. Every database KeePass opens
        /// answers with its tree, a root it cannot reach being a tree with nothing in it; null is
        /// what a test double with no tree answers, and is read the same way.
        /// </summary>
        Acl.IRuledNodes Nodes { get; }

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
