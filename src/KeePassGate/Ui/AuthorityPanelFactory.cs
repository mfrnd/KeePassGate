using System;
using System.Collections.Generic;
using System.Windows.Forms;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;

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
        private readonly AdmissionAudit _audit;
        private readonly SessionRefusals _session;

        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations)
            : this(
                databases,
                prompt,
                log,
                invitations,
                null,
                null,
                new AdmissionAudit(NullGateAudit.Instance),
                new SessionRefusals())
        {
        }

        /// <summary>
        /// The same, telling every panel it builds what to listen to for a record landing,
        /// counting what escapes the dialog's close where the plugin's window shows it, and writing
        /// down each revocation the dialog's OK lands.
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
        ///
        /// The audit is the gate's own, so that the operator's revocations sit in the file beside
        /// the client's requests they put an end to. **Required here**, because a factory given
        /// none wrote nothing down and said nothing about it, so whether the operator's revocations
        /// reached the file was decided by one argument in the plugin's wiring that no test read.
        /// The constructor above, which a test builds, writes into nothing on purpose.
        ///
        /// **The renewals refused in this session are the plugin's own too**, the set the renewal
        /// handler answers from, and required for the same reason: a tab given a set of its own
        /// would show none of the gate's answers, lift none of them, and say nothing about it. The
        /// constructor above gives it an empty one, a session in which nothing has been refused.
        /// </remarks>
        public AuthorityPanelFactory(
            IGateDatabaseSource databases,
            IOperatorPrompt prompt,
            IGateLog log,
            IInvitations invitations,
            IAdmissionNotice admissions,
            GateIncidents incidents,
            AdmissionAudit audit,
            SessionRefusals session)
        {
            // Refused rather than tolerated, because the alternative is a tab that reads "no
            // database open on this dialog" whether that is true or whether this was wired up
            // wrongly, and those want different answers from whoever is looking at it.
            if (databases == null)
            {
                throw new ArgumentNullException(nameof(databases));
            }

            if (audit == null)
            {
                throw new ArgumentNullException(nameof(audit));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _databases = databases;
            _prompt = prompt;
            _log = log;
            _invitations = invitations;
            _admissions = admissions;
            _incidents = incidents ?? new GateIncidents(Log());
            _audit = audit;
            _session = session;
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

            // And so are the renewals the clients page lets through again, which are the
            // plugin's memory rather than the database's and wait beside the buffer for the same
            // OK.
            var session = new PendingSessionRefusals(_session);

            var panel = new AuthorityPanel(
                database,
                _prompt,
                _log,
                invitations,
                database == null ? null : new DatabaseProfiles(database, _log),
                _admissions,
                session);

            Commit(dialog, found, database, invitations, session);
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
        /// that could have is closing. And each revocation that landed is written to the audit,
        /// which is the moment it takes effect, naming its client by the client key on the record
        /// it empties: read from the live database just before OK applies the buffer, since the
        /// record is gone once it has, and only when a revocation is waiting.
        ///
        /// **The answers lifted for this session go first, and whatever the buffer does.** They
        /// are the plugin's memory and not the database's, so nothing about the file decides them:
        /// a database that went away under the dialog, or a buffer with nothing else in it, still
        /// leaves the operator having allowed that renewal and pressed OK.
        /// </remarks>
        private void Commit(
            Form dialog,
            IGateDatabase live,
            PendingDatabase database,
            PendingInvitations invitations,
            PendingSessionRefusals session)
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

                        foreach (var key in session.Apply())
                        {
                            Log().Info("renewals of " + CertificateDigest.Shorten(key)
                                + " are asked about again: the operator lifted the answer for this session");
                        }

                        var clients = ClientKeys(live, database);
                        if (database.Apply())
                        {
                            Log().Info("what the " + GateName.Product + " tab held was put into "
                                + "the database, which KeePass now says has changes to save");
                            Revoked(database, clients);
                        }

                        return;
                    }

                    if (invitations != null && invitations.Discard() > 0)
                    {
                        Log().Info("the invitation made on the " + GateName.Product
                            + " tab was withdrawn: the dialog closed without OK");
                    }

                    session.Discard();
                    database.Discard();
                }
                // codeql[cs/catch-of-all-exceptions] the settings dialog's close, one of the four catch-alls
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

        /// <summary>
        /// Writes down every revocation the dialog's OK just put into the database, one line each.
        /// </summary>
        /// <remarks>
        /// **When it lands, which is now.** A revocation pressed on the Clients page waits in the
        /// dialog's buffer with everything else on the tab, and Cancel takes it away with nothing
        /// written anywhere. What is written is what the buffer stored, so a client revoked by
        /// itself while the dialog was open is not written down a second time, and a revocation an
        /// authority's recreation took away again is not written down at all.
        ///
        /// The database is named by the authority it serves now, as the gate's own lines name it,
        /// or as nothing when it serves none. That is the authority served once OK is through, not
        /// necessarily the one that issued the revoked certificate: after a recreate in the same
        /// dialog it is the new one, and in a database whose two copies each made an authority it
        /// is nothing, which `docs/audit.md` says beside the `database` member.
        /// </remarks>
        /// <param name="clients">
        /// The client key of every record the live database held before OK, by admission key, which
        /// <see cref="ClientKeys"/> read. A revoked certificate missing from it is named with no
        /// client key.
        /// </param>
        private void Revoked(PendingDatabase database, IDictionary<string, string> clients)
        {
            var revoked = new List<string>();
            foreach (var key in database.Stored)
            {
                if (AuthorityKeys.IsRevocation(key))
                {
                    revoked.Add(AuthorityKeys.AdmissionKeyInRevocation(key));
                }
            }

            if (revoked.Count == 0)
            {
                return;
            }

            string databaseId;
            using (var reading = new AuthorityService(database.PluginCustomData, Log()).Read())
            {
                databaseId = reading.IsPresent ? reading.Material.AuthorityDigest : null;
            }

            foreach (var key in revoked)
            {
                // Null when the record was not there to read, which is what a miss leaves it.
                string clientKey;
                clients.TryGetValue(key, out clientKey);
                _audit.RevokedByOperator(databaseId, key, clientKey);
            }
        }

        /// <summary>
        /// The client key of every record the live database holds, by admission key, read while a
        /// revocation is still waiting in the buffer, or nothing when none is.
        /// </summary>
        /// <remarks>
        /// **Read before OK applies the buffer, because applying it is what empties the record.**
        /// The revocation's own key holds no client key, and the line about it is the one place
        /// the operator would look for which client a certificate belonged to. Read once per OK
        /// that carries a revocation, never per request. A listing that cannot be made says so in
        /// the log, from `AdmissionStore`, and leaves the lines without a client key rather than
        /// keeping the revocation from landing.
        /// </remarks>
        private IDictionary<string, string> ClientKeys(IGateDatabase live, PendingDatabase database)
        {
            var clients = new Dictionary<string, string>(StringComparer.Ordinal);
            if (live == null || !live.IsOpen || !database.WaitingOn(AuthorityKeys.IsRevocation))
            {
                return clients;
            }

            var store = live.PluginCustomData;
            if (store == null)
            {
                return clients;
            }

            foreach (var record in new AdmissionStore(store, Log()).All().Records)
            {
                clients[record.AdmissionKey] = record.ClientKey;
            }

            return clients;
        }

        private IGateLog Log()
        {
            return _log ?? NullGateLog.Instance;
        }
    }
}
