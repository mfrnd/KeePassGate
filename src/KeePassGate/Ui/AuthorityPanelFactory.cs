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
    /// A dialog with no database behind it still gets a panel, saying so. Refusing to attach would
    /// leave the operator looking for a tab that is missing for reasons they cannot see.
    /// </remarks>
    public sealed class AuthorityPanelFactory : IGatePanelFactory
    {
        private readonly IGateDatabaseSource _databases;
        private readonly IOperatorPrompt _prompt;
        private readonly IGateLog _log;
        private readonly string _version;
        private readonly IGateStatus _gate;
        private readonly IInvitations _invitations;
        private readonly GateIncidents _incidents;

        private readonly IAdmissionNotice _admissions;

        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IGateLog log,
            IGateStatus gate,
            IInvitations invitations,
            GateIncidents incidents)
            : this(databases, new MessageBoxPrompt(), log, GateVersion.Current, gate, invitations, incidents)
        {
        }

        /// <summary>The same, and handed the notice every panel it builds should listen to.</summary>
        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IGateLog log,
            IGateStatus gate,
            IInvitations invitations,
            GateIncidents incidents,
            IAdmissionNotice admissions)
            : this(databases, new MessageBoxPrompt(), log, GateVersion.Current, gate, invitations,
                   incidents, admissions)
        {
        }

        public AuthorityPanelFactory(IGateDatabaseSource databases, IOperatorPrompt prompt, IGateLog log, string version)
            : this(databases, prompt, log, version, null, null)
        {
        }

        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            IInvitations invitations)
            : this(databases, prompt, log, version, gate, invitations, null)
        {
        }

        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            IInvitations invitations,
            GateIncidents incidents)
            : this(databases, prompt, log, version, gate, invitations, incidents, null)
        {
        }

        /// <summary>
        /// The same, and telling every panel it builds what to listen to for a record landing.
        /// </summary>
        /// <remarks>
        /// One notice for the whole plugin, handed to each panel as it is built. A panel drops it
        /// when it is disposed, so the dialog being opened and closed all afternoon leaves nothing
        /// behind.
        /// </remarks>
        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            IInvitations invitations,
            GateIncidents incidents,
            IAdmissionNotice admissions)
        {
            if (databases == null)
            {
                throw new ArgumentNullException(nameof(databases));
            }

            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            _databases = databases;
            _prompt = prompt;
            _log = log ?? NullGateLog.Instance;
            _version = version;
            _gate = gate;
            _invitations = invitations;
            _incidents = incidents;
            _admissions = admissions;
        }

        public Control CreatePanel(Form dialog)
        {
            var found = _databases.ForDialog(dialog);

            // Held back until the dialog says so. Everything the page does goes through this, so it
            // does not have to know, and the dialog's own OK and Cancel come to mean what they say
            // on a tab that would otherwise write the moment a button was pressed.
            var database = found == null ? null : new PendingDatabase(found, _log);

            // And so are the invitations made on it, which are the plugin's memory rather than the
            // database's: good the moment they are made, and taken back unless the dialog's answer
            // is OK.
            var invitations = _invitations == null ? null : new PendingInvitations(_invitations);

            var panel = new AuthorityPanel(
                database, _prompt, _log, _version, _gate, invitations, _incidents, _admissions);
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
        /// this runs: read off 2.61.1. Nothing here needs the panel: what is applied or discarded
        /// is the buffer the panel wrote into, which outlives it. Subscribed while the panel is
        /// built only because that is when the buffer exists, and before the injector's own
        /// handler, which finds nothing left to take away.
        ///
        /// **The invitations made on the dialog go first when it is not accepted**, before the
        /// buffer is discarded, because a code still out is the one thing here a stranger can act
        /// on. Every answer that is not OK withdraws them: Cancel, the close box and Escape alike.
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

                        database.Apply();
                        return;
                    }

                    if (invitations != null && invitations.Discard() > 0)
                    {
                        _log.Info("the invitation made on the " + GateTab.Text
                            + " tab was withdrawn: the dialog closed without OK");
                    }

                    database.Discard();
                }
                catch (Exception error)
                {
                    // A catch-all, because KeePass raises `FormClosed` itself and anything escaping
                    // here ends the password manager. A narrow filter left every unpredicted type
                    // to do just that: a null reference, or a `TypeLoadException` after a KeePass
                    // upgrade. Reported at the level that means there is a defect here.
                    _log.Unexpected(
                        "what the " + GateTab.Text + " tab was holding could not be written", error);
                }
            };

            try
            {
                dialog.FormClosed += closed;
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                // A dialog already gone. Nothing was written, which is the safe half of the two.
                _log.Warn("the " + GateTab.Text + " tab could not watch for the dialog's result: "
                    + error.Message);
            }
        }
    }
}
