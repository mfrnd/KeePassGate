using System;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

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
        private readonly GateIncidents _incidents;

        public AuthorityPanelFactory(IGateDatabaseSource databases, IGateLog log)
            : this(databases, new MessageBoxPrompt(), log, GateVersion.Current, null)
        {
        }

        public AuthorityPanelFactory(
            IGateDatabaseSource databases, IGateLog log, IGateStatus gate, GateIncidents incidents)
            : this(databases, new MessageBoxPrompt(), log, GateVersion.Current, gate, incidents)
        {
        }

        public AuthorityPanelFactory(IGateDatabaseSource databases, IGateLog log, IGateStatus gate)
            : this(databases, new MessageBoxPrompt(), log, GateVersion.Current, gate)
        {
        }

        public AuthorityPanelFactory(IGateDatabaseSource databases, IOperatorPrompt prompt, IGateLog log, string version)
            : this(databases, prompt, log, version, null)
        {
        }

        public AuthorityPanelFactory(
            IGateDatabaseSource databases, IOperatorPrompt prompt, IGateLog log, string version, IGateStatus gate)
            : this(databases, prompt, log, version, gate, null)
        {
        }

        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            string version,
            IGateStatus gate,
            GateIncidents incidents)
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
            _incidents = incidents;
        }

        public Control CreatePanel(Form dialog)
        {
            var found = _databases.ForDialog(dialog);

            // Held back until the dialog says so. Everything the page does goes through this, so it
            // does not have to know, and the dialog's own OK and Cancel come to mean what they say
            // on a tab that would otherwise write the moment a button was pressed.
            var database = found == null ? null : new PendingDatabase(found, _log);

            var panel = new AuthorityPanel(database, _prompt, _log, _version, _gate, _incidents);
            Commit(dialog, database);
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
        /// </remarks>
        private void Commit(Form dialog, PendingDatabase database)
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
                        database.Apply();
                        return;
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
