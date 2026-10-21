using System;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Spec;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// Writes down what happens to a database's admissions: every pairing and renewal the operator
    /// answers, and every revocation, the operator's and a client's own.
    /// </summary>
    /// <remarks>
    /// **No rule decides any of these, and the audit is still where they belong.** It is the one
    /// record an operator reads to see what reached the database, and a client's history there
    /// began with the pairing that admitted it, which until this was a line in the plugin's log
    /// and nowhere else. Same file and form as the rules' decisions, with the operator's or the
    /// act's own word as the decision and a phrase from the fixed set below as the reason.
    ///
    /// **What a line names.** A pairing names the invitation it arrived on, and an approved one
    /// the admission key it issued, which is the new client's client key as well; its asker holds
    /// no certificate yet, so it names no certificate that asked, and one nobody approved names no
    /// client either. A renewal names the certificate that asked and, approved, the one issued in
    /// its place. A revocation names the certificate it takes away. Every line but a pairing's
    /// names the client by the client key on the record of the certificate it is about, which a
    /// renewal copies, so it is the same before and after. **Never a profile**, as nowhere in the file:
    /// which profiles a client holds is in its record, and a second copy of that is the thing
    /// worth stealing.
    ///
    /// **An approval is written once its record is in the database**, so `approved` always names
    /// a key that was admitted. One that admitted nobody, because the record did not land, the
    /// invitation was spent elsewhere or no certificate could be issued, was answered as a failure
    /// and said so in the plugin's log, and gets no line. **The write itself writes it**, which is
    /// `DatabaseWriter`, on its own path for a change that landed and before anything drawn from
    /// the records is told, and so does a client's revocation of itself: written there, nothing
    /// that runs after the write can lose the line about a change that is on the disk.
    ///
    /// **Paced where a client could set the pace.** A renewal's and a self-revocation's lines are
    /// counted against the same allowance as the client's decisions, kept by client key, because
    /// a renewal refused by a standing answer is answered at once and could otherwise roll the
    /// file, and an approved one must not hand the client a fresh allowance. A refused one is
    /// written on the client's own connection and waits there. An approved renewal and a
    /// self-revocation are written by the writer, on KeePass's own thread, which must not wait, so
    /// those two are written without waiting and the client's turn for each is taken by
    /// <see cref="TakeTurn"/> once the write returns, on its own connection. A pairing's line
    /// names no client and waits on a person, who is its pace. The operator's revocation is
    /// written on KeePass's own thread too.
    /// </remarks>
    public sealed class AdmissionAudit
    {
        /// <summary>Why an approved pairing or renewal was written down.</summary>
        public const string ApprovedReason = "the operator approved it";

        /// <summary>Why a refused pairing was.</summary>
        public const string RefusedReason = "the operator refused it";

        /// <summary>Why a pairing or a renewal nobody answered was.</summary>
        public const string UnansweredReason = "nobody answered";

        /// <summary>A renewal the operator refused once, which asks again next time.</summary>
        public const string RefusedOnceReason = "the operator refused it once";

        /// <summary>A renewal the operator refused until KeePass exits.</summary>
        public const string RefusedThisSessionReason = "the operator refused it in this session";

        /// <summary>A renewal the operator refused until a date of their choosing.</summary>
        public const string RefusedUntilReason = "the operator refused it until a later date";

        /// <summary>A renewal the operator refused for every later request too.</summary>
        public const string RefusedForeverReason = "the operator refused it forever";

        /// <summary>A renewal refused without the dialog, until KeePass exits.</summary>
        public const string StandingThisSessionReason = "refused in this session, without asking";

        /// <summary>A renewal refused without the dialog, because of a date the operator set.</summary>
        public const string StandingUntilReason = "refused until a later date, without asking";

        /// <summary>A renewal refused without the dialog, because the operator refused every one.</summary>
        public const string StandingForeverReason = "refused forever, without asking";

        /// <summary>A client giving up its own admission.</summary>
        public const string RevokedItselfReason = "the client revoked its own certificate";

        /// <summary>The operator taking a client's admission away on the Clients page.</summary>
        public const string RevokedByOperatorReason = "the operator revoked it";

        private readonly IGateAudit _audit;
        private readonly Func<DateTimeOffset> _clock;
        private readonly DecisionPace _pace;

        /// <summary>Writing to this audit, paced on an allowance of its own.</summary>
        public AdmissionAudit(IGateAudit audit)
            : this(audit, null, null)
        {
        }

        /// <param name="pace">
        /// The allowance the clients' decision lines are counted against, which these share: a
        /// client gets one allowance for everything it can make the audit write, not one per kind.
        /// </param>
        internal AdmissionAudit(IGateAudit audit, Func<DateTimeOffset> clock, DecisionPace pace)
        {
            _audit = audit ?? NullGateAudit.Instance;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _pace = pace ?? new DecisionPace();
        }

        /// <summary>A pairing the operator approved, once the record it issued is in the database.</summary>
        public void Paired(string databaseId, string invitationId, string issued)
        {
            Pairing(databaseId, invitationId, AuditEntry.Approved, ApprovedReason, issued);
        }

        /// <summary>A pairing the operator refused, which spent its invitation.</summary>
        public void PairingRefused(string databaseId, string invitationId)
        {
            Pairing(databaseId, invitationId, AuditEntry.Refused, RefusedReason, null);
        }

        /// <summary>A pairing nobody answered, which left its invitation as it was.</summary>
        public void PairingUnanswered(string databaseId, string invitationId)
        {
            Pairing(databaseId, invitationId, AuditEntry.Unanswered, UnansweredReason, null);
        }

        /// <summary>A renewal the operator approved, once the new record is in the database.</summary>
        /// <remarks>
        /// Written by the writer on KeePass's own thread, so it does not wait: the renewal takes
        /// the client's turn for it with <see cref="TakeTurn"/> once the write returns.
        /// </remarks>
        /// <param name="client">The certificate that asked, by admission key.</param>
        /// <param name="clientKey">
        /// The client key on that certificate's record, which the new record copies, so the line
        /// names the one client the renewal leaves unchanged.
        /// </param>
        /// <param name="issued">The certificate issued in its place, by admission key.</param>
        public void Renewed(string databaseId, string client, string clientKey, string issued)
        {
            Write(
                databaseId,
                client,
                clientKey,
                GateOperations.RenewCertificate,
                AuditEntry.Approved,
                ApprovedReason,
                issued);
        }

        /// <summary>
        /// A renewal the operator refused in the dialog, with which of the four answers they gave.
        /// </summary>
        /// <param name="standing">What they said about later renewals too, or null for once only.</param>
        public void RenewalRefused(string databaseId, string client, string clientKey, RenewalRefusal standing)
        {
            var reason = standing == null ? RefusedOnceReason
                : standing.IsThisSession ? RefusedThisSessionReason
                : standing.IsForever ? RefusedForeverReason
                : RefusedUntilReason;

            Renewal(databaseId, client, clientKey, AuditEntry.Refused, reason);
        }

        /// <summary>A renewal refused without the dialog, by an answer the operator gave before.</summary>
        /// <exception cref="ArgumentNullException">
        /// For no standing answer, which refuses nothing and has no reason to write down.
        /// </exception>
        public void RenewalRefusedWithoutAsking(
            string databaseId, string client, string clientKey, RenewalRefusal standing)
        {
            if (standing == null)
            {
                throw new ArgumentNullException(nameof(standing));
            }

            var reason = standing.IsThisSession ? StandingThisSessionReason
                : standing.IsForever ? StandingForeverReason
                : StandingUntilReason;

            Renewal(databaseId, client, clientKey, AuditEntry.Refused, reason);
        }

        /// <summary>A renewal nobody answered.</summary>
        public void RenewalUnanswered(string databaseId, string client, string clientKey)
        {
            Renewal(databaseId, client, clientKey, AuditEntry.Unanswered, UnansweredReason);
        }

        /// <summary>
        /// A client's revocation of its own certificate, once the revocation is in the database.
        /// </summary>
        /// <remarks>
        /// Written by the writer on KeePass's own thread, so it does not wait: the revocation takes
        /// the client's turn for it with <see cref="TakeTurn"/> once the write returns.
        /// </remarks>
        public void RevokedItself(string databaseId, string client, string clientKey)
        {
            Write(
                databaseId,
                client,
                clientKey,
                GateOperations.RevokeCertificate,
                AuditEntry.Revoked,
                RevokedItselfReason,
                null);
        }

        /// <summary>
        /// Returns when a client's turn has come for a line the writer wrote about it without
        /// waiting, which is <see cref="Renewed"/> and <see cref="RevokedItself"/>.
        /// </summary>
        /// <remarks>
        /// Called on the client's own connection once the write has returned, where a wait holds
        /// nobody else up. It is the turn those lines took when the operation wrote them itself,
        /// taken at the same point, before the answer, and counted against the same allowance.
        /// </remarks>
        /// <param name="clientKey">The client the line was about, by its client key.</param>
        public void TakeTurn(string clientKey)
        {
            _pace.Take(clientKey);
        }

        /// <summary>
        /// The operator's revocation, as the settings dialog's OK puts it into the database.
        /// </summary>
        /// <remarks>
        /// Never paced: it is written on KeePass's own thread, and an operator cannot revoke faster
        /// than they can press a button and accept a dialog.
        /// </remarks>
        /// <param name="clientKey">
        /// The client key on the record the revocation empties, read before OK applied it, or null
        /// when that record was gone or could not be read by then.
        /// </param>
        public void RevokedByOperator(string databaseId, string client, string clientKey)
        {
            _audit.Record(AuditEntry.Admission(
                _clock(),
                databaseId,
                client,
                clientKey,
                null,
                AuditEntry.Revoked,
                RevokedByOperatorReason,
                null,
                null));
        }

        /// <summary>A pairing as the operator answered it, or left it to close itself.</summary>
        /// <remarks>
        /// An approved pairing's client key is the key it issued, which is the client's first
        /// certificate and so what makes a client key. One refused or unanswered admitted nobody,
        /// and names no client.
        /// </remarks>
        private void Pairing(string databaseId, string invitationId, string decision, string reason, string issued)
        {
            // Not paced: there is no client yet to count it against, and the dialog it answers is
            // the pace, one at a time and none faster than somebody can answer or a timeout runs.
            _audit.Record(AuditEntry.Admission(
                _clock(),
                databaseId,
                null,
                issued,
                GateOperations.PairRequest,
                decision,
                reason,
                invitationId,
                issued));
        }

        private void Renewal(string databaseId, string client, string clientKey, string decision, string reason)
        {
            // By the client, which the renewal leaves as it was, so the certificate it ends with
            // spends the allowance the one that asked had spent.
            _pace.Take(clientKey);

            Write(databaseId, client, clientKey, GateOperations.RenewCertificate, decision, reason, null);
        }

        /// <summary>A line about a client's own admission, written without waiting for its turn.</summary>
        private void Write(
            string databaseId,
            string client,
            string clientKey,
            string operation,
            string decision,
            string reason,
            string issued)
        {
            _audit.Record(AuditEntry.Admission(
                _clock(), databaseId, client, clientKey, operation, decision, reason, null, issued));
        }
    }
}
