using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Store;

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

        /// <summary>
        /// Where the gate's audit is writing, handed to every panel for its authority chooser, or
        /// null where there is no audit.
        /// </summary>
        public Func<string> AuditPath { get; set; }

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
                session)
            {
                AuditPath = AuditPath
            };

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
        /// which is the moment it takes effect, a line for each certificate of the client it
        /// ended: read from the live store just before OK applies the buffer, since the write that
        /// revokes deletes the admissions it refuses, and only when a revocation is waiting.
        ///
        /// **The answers lifted for this session are lifted whatever the buffer does.** They
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
                        // The database's buffer first, revocations among it, and whatever naming
                        // the clients for the audit did, since the names only go into its lines:
                        // read inside the same step, a failure there, or in what comes after it
                        // here, dropped every revocation the clients page had already shown gone.
                        // What follows can only fail closed: a lifted answer left standing refuses,
                        // and an invitation made on the dialog is outstanding from the moment it
                        // was made, which accepting the dialog only stops being taken back.
                        ILookup<string, string> certificates = null;
                        try
                        {
                            certificates = Certificates(live, database);
                        }
                        finally
                        {
                            try
                            {
                                // The revocations' lines as soon as the store holds them, before
                                // the custom data half, so that a defect there cannot lose the line
                                // of a revocation that stands.
                                if (database.Apply(() => Revoked(live, database, certificates)))
                                {
                                    Log().Info("what the " + GateName.Product + " tab held was put into "
                                        + "the database, which KeePass now says has changes to save");
                                }
                            }
                            finally
                            {
                                // The steps after the buffer, whatever its lines came to, as its
                                // custom data half runs: the standing answers that half took out of
                                // the database told in the log, the answers lifted for this session
                                // and their lines, and the invitations kept. What was thrown above
                                // reaches the catch-all below once these have run; where a step
                                // here throws as well, its exception is the incident recorded.
                                Lifted(database);
                                foreach (var key in session.Apply())
                                {
                                    Log().Info("renewals of " + CertificateDigest.Shorten(key)
                                        + " are asked about again: the operator lifted the answer for this session");
                                }

                                if (invitations != null)
                                {
                                    invitations.Apply();
                                }
                            }
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
                // codeql[cs/catch-of-all-exceptions] a handler of KeePass's own events, a kind of catch-all DESIGN.md §141 lists
                catch (Exception error)
                {
                    // A catch-all, because `FormClosed` is raised on KeePass's own thread as its
                    // settings dialog closes, with nothing of KeePass's around it. WinForms catches
                    // what escapes there, sets the dialog's result back to none, so that the dialog
                    // stays open, and puts its own error dialog over it, offering to quit KeePass.
                    // A narrow filter left every unpredicted type to do just that: a null
                    // reference, or a `TypeLoadException` after a KeePass upgrade. Counted as an
                    // incident, like every handler of KeePass's events that catches everything, so
                    // the Tools window shows it and the log has it whole. Its text is true whatever
                    // of the buffer was written before the throw, and the lines beside it in the
                    // log tell what landed.
                    _incidents.Record(
                        (dialog.DialogResult == DialogResult.OK ? "the OK" : "the Cancel") + " of the " + GateName.Product
                        + " tab did not run to its end",
                        error);
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
        /// The authority the database is served with, from a reading let go at once, or null where
        /// none is or the store could not be read: the audit then names no database.
        /// </summary>
        private string Served(IGateDatabase live)
        {
            if (live == null)
            {
                return null;
            }

            try
            {
                using (var reading = live.Store.Read())
                {
                    return StoreAuthorities.Served(reading);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                Log().Warn("the plugin's store could not be read to name the database in the lines of what the tab ended: " + error.Message);
                return null;
            }
        }

        /// <summary>
        /// Writes down every revocation the dialog's OK just wrote into the plugin's store: a line
        /// for each certificate of that client the copy admitted, and a single line with the
        /// client key alone where it admitted none.
        /// </summary>
        /// <remarks>
        /// **When it lands, which is now.** A revocation pressed on the Clients page waits in the
        /// dialog's buffer with everything else on the tab, and Cancel takes it away with nothing
        /// written anywhere. What is written is what the store's write took, so a revocation the
        /// write refused or left out is not written down at all.
        ///
        /// The database is named by the authority it serves now, as the gate's own lines name it,
        /// or as nothing when it serves none. That is the authority served once OK is through, not
        /// necessarily the authority that issued the revoked certificate: where the same dialog
        /// kept another authority it is the authority kept, and in a database whose two copies
        /// each made an authority it is nothing, as `docs/audit.md` has it beside the `database`
        /// member.
        /// </remarks>
        /// <param name="certificates">
        /// The admission keys of every certificate the live store admitted before OK, by client
        /// key, which <see cref="Certificates"/> read, or null where it could not.
        /// </param>
        private void Revoked(IGateDatabase live, PendingDatabase database, ILookup<string, string> certificates)
        {
            var revoked = database.Landed
                .Where(act => string.Equals(act.Name, AdmissionActs.RevokeName, StringComparison.Ordinal))
                .SelectMany(act => act.Records)
                .Where(record => record.Kind == StoreKinds.Revoked)
                .Select(record => record.Member(StoreKinds.ClientMember))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (revoked.Count == 0)
            {
                return;
            }

            var databaseId = Served(live);
            foreach (var clientKey in revoked)
            {
                var held = certificates == null ? new List<string>() : certificates[clientKey].ToList();
                if (held.Count == 0)
                {
                    _audit.RevokedByOperator(databaseId, null, clientKey);
                }

                foreach (var key in held)
                {
                    _audit.RevokedByOperator(databaseId, key, clientKey);
                }

                Log().Info("the operator's revocation of the client " + CertificateDigest.Shorten(clientKey) + " reached the database");
            }
        }

        /// <summary>
        /// Writes down each standing answer to a renewal the dialog's OK took out of the database.
        /// </summary>
        /// <remarks>
        /// Here rather than at the press on the clients page, which logged a lift that a Cancel
        /// then left standing, with nothing written to take the line back.
        /// </remarks>
        private void Lifted(PendingDatabase database)
        {
            foreach (var key in database.Taken.Where(AuthorityKeys.IsRenewalRefusal))
            {
                Log().Info("renewals of " + CertificateDigest.Shorten(AuthorityKeys.AdmissionKeyInRenewalRefusal(key))
                    + " are asked about again: the operator lifted the standing answer");
            }
        }

        /// <summary>
        /// The admission key of every certificate the live store admits, by client key, read while
        /// a revocation is still waiting in the buffer, or null when none is or the store could not
        /// be read.
        /// </summary>
        /// <remarks>
        /// **Read before OK applies the buffer, because the write that revokes deletes the
        /// admissions it refuses**, `STORE.md` §627, and the lines about a revocation are the one
        /// place the operator would look for which certificates the OK ended. Read once per OK
        /// that carries a revocation, never per request: every admission the listing has admitting
        /// before OK. An admission under an authority that is not served, a certificate several
        /// admissions name and a record that does not read admitted nobody, so the OK ended none of
        /// them. A store that cannot be read logs it and leaves a single line with the client key
        /// alone rather than keeping the revocation from landing.
        /// </remarks>
        private ILookup<string, string> Certificates(IGateDatabase live, PendingDatabase database)
        {
            if (live == null || !live.IsOpen || !database.ActWaiting(AdmissionActs.RevokeName))
            {
                return null;
            }

            try
            {
                using (var reading = live.Store.Read())
                {
                    var listing = StoreAdmissions.Listing(reading);
                    return listing.Records
                        .Where(record => listing.StandingOf(record) == AdmissionStanding.Admitted)
                        .Select(record => new KeyValuePair<string, string>(record.ClientKey, record.AdmissionKey))
                        .Distinct()
                        .ToLookup(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                }
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                Log().Warn("the plugin's store could not be read for the certificates of the clients revoked: " + error.Message);
                return null;
            }
        }

        private IGateLog Log()
        {
            return _log ?? NullGateLog.Instance;
        }
    }
}
