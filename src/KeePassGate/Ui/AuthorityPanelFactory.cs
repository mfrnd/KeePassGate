using System;
using System.Windows.Forms;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Builds the KPG tab for whichever database the dialog is about.
    /// </summary>
    /// <remarks>
    /// The dialog arrives rather than the database, because which database a settings dialog is
    /// editing is a KeePass question and `IGateDatabaseSource` is the only thing here that answers
    /// it. A dialog with no database behind it still gets a tab, which says so.
    /// </remarks>
    public sealed class AuthorityPanelFactory : IGatePanelFactory
    {
        private readonly IGateDatabaseSource _databases;
        private readonly IOperatorPrompt _prompt;
        private readonly IGateLog _log;
        private readonly IInvitations _invitations;

        private readonly IAdmissionNotice _admissions;
        private readonly GateIncidents _incidents;

        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations)
            : this(databases, prompt, log, invitations, null, null)
        {
        }

        /// <summary>
        /// The same, telling every panel it builds what to listen to for a record landing, and
        /// counting what escapes the dialog's close where the plugin's window shows it.
        /// </summary>
        /// <remarks>
        /// One notice for the whole plugin, handed to each panel as it is built. A panel drops it
        /// when it is disposed, so the dialog being opened and closed all afternoon leaves nothing
        /// behind.
        ///
        /// The incidents are the plugin's own rather than a tally of this factory's, because the
        /// handler that applies what the tab held is one of the KeePass window event handlers that
        /// catch everything, and every one of those reports to the count the Tools window reads. A
        /// factory given none counts on one of its own, which is what a test builds.
        /// </remarks>
        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations,
            IAdmissionNotice admissions,
            GateIncidents incidents)
        {
            // Refused rather than tolerated, because the alternative is a tab that reads "no
            // database open on this dialog" whether that is true or whether this was wired up
            // wrongly, and those want different answers from whoever is looking at it.
            if (databases == null)
            {
                throw new ArgumentNullException(nameof(databases));
            }

            _databases = databases;
            _prompt = prompt;
            _log = log;
            _invitations = invitations;
            _admissions = admissions;
            _incidents = incidents ?? new GateIncidents(Log());
        }

        public Control CreatePanel(Form dialog)
        {
            var found = _databases.ForDialog(dialog);

            // Held back until the dialog says so. Everything the pages do goes through this, so
            // none of them has to know, and the dialog's own OK and Cancel come to mean what they
            // say on a tab that used to write the moment a button was pressed.
            var database = found == null ? null : new PendingDatabase(found, _log);

            // And so are the invitations made on it, which are the plugin's memory rather than the
            // database's: good the moment they are made, and taken back unless the dialog's answer
            // is OK.
            var invitations = _invitations == null ? null : new PendingInvitations(_invitations);

            var panel = new AuthorityPanel(
                database,
                _prompt,
                _log,
                invitations,
                database == null ? null : new DatabaseProfiles(database, _log),
                _admissions);

            Commit(dialog, database, invitations);
            return panel;
        }

        /// <summary>
        /// Arranges for the dialog's result to decide what happens to what is waiting.
        /// </summary>
        /// <remarks>
        /// **On `FormClosed` and not on `FormClosing`**, because closing can be refused: another
        /// handler canceling it would leave the changes already written and the dialog still open,
        /// which is the one state neither answer covers.
        ///
        /// **After the page has gone, and that is fine.** KeePass's own dialog subscribes a
        /// `FormClosed` handler in its constructor, and that handler takes the dialog off
        /// `GlobalWindowManager`, which is what detaches the page and disposes the panel, before
        /// this runs: read off 2.61.1, and `KeePassContractTests` keeps checking it. Nothing here
        /// needs the panel: what is applied or discarded is the buffer the panel wrote into, which
        /// outlives it. Subscribed while the panel is built only because that is when the buffer
        /// exists, and before the injector's own handler, which finds nothing left to take away.
        ///
        /// **The invitations made on the dialog go first when it is not accepted**, before the
        /// buffer is discarded, because a code still out is the one thing here a stranger can act
        /// on. Every answer that is not OK withdraws them: Cancel, the close box and Escape alike.
        ///
        /// **What landed is said in the log**, which is the only place left to say it: the dialog
        /// that could have is closing.
        /// </remarks>
        private void Commit(Form dialog, PendingDatabase database, PendingInvitations invitations)
        {
            if (dialog == null || database == null)
            {
                return;
            }

            FormClosedEventHandler closed = null;
            closed = (sender, e) =>
            {
                dialog.FormClosed -= closed;

                try
                {
                    if (dialog.DialogResult == DialogResult.OK)
                    {
                        if (invitations != null)
                        {
                            invitations.Apply();
                        }

                        if (database.Apply())
                        {
                            Log().Info("what the " + GateName.Product + " tab held was put into "
                                + "the database, which KeePass now says has changes to save");
                        }

                        return;
                    }

                    if (invitations != null && invitations.Discard() > 0)
                    {
                        Log().Info("the invitation made on the " + GateName.Product
                            + " tab was withdrawn: the dialog closed without OK");
                    }

                    database.Discard();
                }
                catch (Exception error)
                {
                    // A catch-all, because KeePass raises `FormClosed` itself and anything escaping
                    // here ends the password manager. A narrow filter left every unpredicted type
                    // to do just that: a null reference, or a `TypeLoadException` after a KeePass
                    // upgrade. Counted as an incident, like every handler of KeePass's events that
                    // catches everything, so the Tools window shows it and the log has it whole.
                    _incidents.Record(
                        "what the " + GateName.Product + " tab was holding could not be written", error);
                }
            };

            try
            {
                dialog.FormClosed += closed;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A dialog already gone. Nothing was written, which is the safe half of the two.
                Log().Warn("the " + GateName.Product + " tab could not watch for the dialog's result: "
                    + error.Message);
            }
        }

        private IGateLog Log()
        {
            return _log ?? NullGateLog.Instance;
        }
    }
}
