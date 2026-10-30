using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Audit;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Transport;
using Newtonsoft.Json;

namespace KeePassGate.Pairing
{
    /// <summary>
    /// `renewCertificate`: an admitted client asking for a new certificate, which the operator
    /// approves or refuses on screen.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §312. **Pairing's request, with the client already known.** No invitation and
    /// no proofs, because the certificate on the connection is the proof: the router has checked it
    /// chains, is current and has a record. What is left is the order that makes pairing safe, kept
    /// exactly: refused before the operator is disturbed while a dialog would take the record back,
    /// asked again once they have answered, then issued, written and saved, and only then handed
    /// over. A certificate whose record never landed is handed to nobody.
    ///
    /// **The old record is left as it is.** The client revokes it itself, with the old certificate,
    /// once the new one is stored, so an answer lost on the way costs nothing: the old certificate
    /// still works and the client asks again. A record orphaned by a lost answer expires and is
    /// pruned, or the operator revokes it. **The new record is the same client**: it carries the
    /// client key of the record that asked, so the two count as one client while both stand.
    ///
    /// **Never wider than the record.** The new certificate carries what the operator left checked
    /// of the profiles the old record holds and the database still names, and nothing else, whatever
    /// the dialog answered.
    ///
    /// **A standing answer is answered without the dialog.** When the operator refused a renewal of
    /// this certificate forever, in this session, or until a moment that has not come, every
    /// renewal of it is answered `409` before anybody is disturbed.
    ///
    /// **One renewal per certificate at a time.** A second renewal of a certificate whose first is
    /// still being answered is told `503 busy` at once, and the first stays in flight until its
    /// answer, standing half included, has been kept. Without that, the second read the standing
    /// answer before the operator gave it, waited its turn for the screen, and raised the same
    /// dialog again the moment the first closed.
    ///
    /// **Every answer is written to the audit**, against the certificate that asked: approved, with
    /// the key issued in its place, once the new record is in the database, by the write that put
    /// it there; refused, with which of the operator's four answers it was, or by one they gave
    /// before; and unanswered. Nothing is written when nobody could be asked. `AdmissionAudit`
    /// says what a line holds.
    /// </remarks>
    public sealed class RenewalHandler
    {
        /// <summary>Where the routing table declares this operation.</summary>
        public const string Path = "/gate/renew";

        private readonly IRenewalApproval _approval;
        private readonly PairingOptions _options;
        private readonly AuthorityIssuer _issuer;
        private readonly Func<DateTimeOffset> _clock;
        private readonly SessionRefusals _session;
        private readonly HashSet<string> _inFlight = new HashSet<string>(StringComparer.Ordinal);
        private readonly AdmissionAudit _audit;
        private readonly IGateLog _log;
        private readonly ApprovalBook _held;

        /// <summary>
        /// The handler this plugin runs, with the session's refusals it shares with the KPG tab.
        /// </summary>
        /// <remarks>
        /// Built once, as the gate starts, and the plugin holds the refusals until KeePass exits,
        /// which makes them last exactly as long as the session their answers name. The clients
        /// page reads the same ones, and lifts one on the operator's word.
        /// </remarks>
        /// <param name="session">
        /// The certificates whose renewals the operator refused in this session, or null for a set
        /// of this handler's own.
        /// </param>
        /// <param name="audit">
        /// Where each renewal the operator answers, or answered before, is written down, or null
        /// for nowhere.
        /// </param>
        public RenewalHandler(
            IRenewalApproval approval, SessionRefusals session, AdmissionAudit audit, IGateLog log)
            : this(approval, new PairingOptions(), new AuthorityIssuer(), null, session, audit, log)
        {
        }

        /// <summary>The same, with the plugin's one book of approvals waiting to be written.</summary>
        /// <param name="held">
        /// The approvals held for a retry, which a lock, a close and an exit clear, shared with
        /// pairing, or null for a book of this handler's own.
        /// </param>
        public RenewalHandler(
            IRenewalApproval approval, SessionRefusals session, ApprovalBook held, AdmissionAudit audit, IGateLog log)
            : this(approval, new PairingOptions(), new AuthorityIssuer(), null, session, audit, log, held)
        {
        }

        /// <summary>The same, given its options, issuer and clock rather than the defaults.</summary>
        /// <param name="session">
        /// The certificates whose renewals the operator refused in this session, which outlast any
        /// one request.
        /// </param>
        public RenewalHandler(
            IRenewalApproval approval,
            PairingOptions options,
            AuthorityIssuer issuer,
            Func<DateTimeOffset> clock,
            SessionRefusals session,
            AdmissionAudit audit,
            IGateLog log,
            ApprovalBook held = null)
        {
            _approval = approval ?? NoRenewalApproval.Instance;
            _options = options ?? new PairingOptions();
            _issuer = issuer ?? new AuthorityIssuer();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _session = session ?? new SessionRefusals();
            _audit = audit ?? new AdmissionAudit(NullGateAudit.Instance);
            _log = log ?? NullGateLog.Instance;
            _held = held ?? new ApprovalBook(_clock);
        }

        /// <summary>`renewCertificate`, once the router has decided this is the operation.</summary>
        public void Renew(IGateContext context)
        {
            // When the pairing budget began, near enough, which an approval is held against.
            var arrived = _clock();

            var client = context.Client;
            if (!client.IsPaired || string.IsNullOrEmpty(client.AdmissionKey))
            {
                // The router refuses a caller with no admission before any handler runs.
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            var target = ServedDatabase.TargetOf(context);
            if (target == null)
            {
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return;
            }

            // The document holds the body to one object whose one member is optional, and this
            // reads it again, for the reason pairing's request does.
            var body = JsonBody.Read(context.Body);
            var lifetimeWritten = body == null ? null : body.Text("lifetime");
            var lifetime = Lifetime.Read(lifetimeWritten);
            if (body == null || (body.Names.Contains("lifetime") && lifetime == null))
            {
                context.Respond(400, HttpResponse.ErrorBody("malformed"));
                return;
            }

            var key = client.AdmissionKey;
            if (!Enter(key))
            {
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused for now: " +
                          "a renewal of the same certificate is already being answered");
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return;
            }

            try
            {
                Renew(context, target, key, lifetime, ApprovalBook.Renewal(key, lifetimeWritten), arrived);
            }
            finally
            {
                Leave(key);
            }
        }

        /// <summary>
        /// Takes the certificate's one place in flight, or says another renewal of it holds it.
        /// </summary>
        private bool Enter(string key)
        {
            lock (_inFlight)
            {
                return _inFlight.Add(key);
            }
        }

        private void Leave(string key)
        {
            lock (_inFlight)
            {
                _inFlight.Remove(key);
            }
        }

        /// <summary>The renewal itself, once it is the only one of this certificate in flight.</summary>
        private void Renew(
            IGateContext context,
            IGateTarget target,
            string key,
            TimeSpan? lifetime,
            string approval,
            DateTimeOffset arrived)
        {
            AdmissionRecord record;
            if (!Admission(context, target, key, out record))
            {
                return;
            }

            if (record == null)
            {
                // Revoked between this connection and now. What it is told is what every connection
                // with this certificate is told from here on.
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            var what = "a renewal by " + CertificateDigest.Shorten(key);
            var held = _held.Find(approval, target.DatabaseId);
            if (held != null)
            {
                // Approved on an earlier connection and held because KeePass was busy then: the
                // operator has answered exactly this, so nothing is put in front of them again.
                // **A standing answer given since outranks it**, as it outranks the dialog: a merge
                // can bring one in, and so can the dialog of a renewal that asked for another
                // lifetime, which is a request of its own. The approval is let go, so the next
                // renewal after the answer is lifted asks the operator rather than using it.
                if (Standing(context, target, key, record))
                {
                    _held.Take(held);
                    return;
                }

                _log.Info(what + " was approved earlier and held, so it is written now without asking again");
            }
            else
            {
                held = Approve(context, target, key, record, lifetime, approval, arrived);
                if (held == null)
                {
                    return;
                }
            }

            // Asked again, for the reason pairing asks again: the answer before the dialog is as
            // old as the dialog. Nothing is issued yet, so a busy KeePass costs a retry and not the
            // approval, which is held for it.
            var blocked = PairingHandler.Settled(target, _options.ApprovalSettle, Blocked, () => _held.Holds(held));
            if (!_held.Holds(held))
            {
                PairingHandler.Gone(context, held, what, _log);
                return;
            }

            if (blocked != null)
            {
                PairingHandler.Busy(context, held, what, blocked, _log);
                return;
            }

            // The record too, for pairing's reason that its invitation is spent only after the
            // dialog: a renewal renews a record, and that record can go while the operator decides,
            // by the client's own revocation on another connection, by the operator's, or with an
            // authority recreated or deleted, which the target answers as no record. What it is told
            // is then what every connection with this certificate is told, and nothing is audited,
            // since nothing was admitted. The write asks once more, under the writer's lock, for one
            // that goes after this.
            AdmissionRecord still;
            if (!Admission(context, target, key, out still))
            {
                return;
            }

            var name = still == null ? null : NameIn(still.Subject);
            if (still == null || name == null)
            {
                _held.Take(held);
                _log.Warn(what + " was approved after its record went, so nothing was issued");
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            Issue(context, target, key, name, still.ClientKey, held);
        }

        /// <summary>
        /// Puts the renewal to the operator, and holds their approval, or answers and says null
        /// when there is none.
        /// </summary>
        private HeldApproval Approve(
            IGateContext context,
            IGateTarget target,
            string key,
            AdmissionRecord record,
            TimeSpan? lifetime,
            string approval,
            DateTimeOffset arrived)
        {
            if (Standing(context, target, key, record))
            {
                return null;
            }

            var blocked = Blocked(target);
            if (blocked != null)
            {
                _log.Warn("a renewal was refused for now: " + blocked);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            // A renewal writes its record from the names on the list, so a list that will not read
            // is not offered as one naming nothing: the client would carry no profile from here on,
            // whatever the list says once it reads again.
            var unreadable = ProfilesUnreadable(target);
            if (unreadable != null)
            {
                _log.Warn("a renewal was refused for now: " + unreadable);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            var request = Request(context, target, record, lifetime);
            if (request == null)
            {
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return null;
            }

            RenewalApproval decision;
            try
            {
                decision = _approval.Ask(request, _options.ApprovalTimeout) ?? RenewalApproval.TimedOut();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the renewal dialog failed", error);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            switch (decision.Decision)
            {
                case PairingDecision.TimedOut:
                    _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " went unanswered");
                    _audit.RenewalUnanswered(target.DatabaseId, key, record.ClientKey);
                    context.Respond(504, HttpResponse.ErrorBody("unanswered"));
                    return null;

                case PairingDecision.Unavailable:
                    // Nobody was asked, so nothing was decided and the audit has nothing to say.
                    _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " could not be put to anybody: " +
                              decision.Reason);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return null;

                case PairingDecision.Locked:
                    // Unanswered, as a timeout is, so nothing changes; told as every connection to
                    // a locked database is told, since that is what this one now is.
                    _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was " + decision.Reason);
                    _audit.RenewalUnanswered(target.DatabaseId, key, record.ClientKey);
                    context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                    return null;

                case PairingDecision.Refused:
                    Refused(target, key, decision.Standing);

                    // Which of the four answers the operator gave, whether or not the standing
                    // half of it could be kept: what is written down is what they decided.
                    _audit.RenewalRefused(target.DatabaseId, key, record.ClientKey, decision.Standing);
                    context.Respond(403, HttpResponse.ErrorBody("refused"));
                    return null;
            }

            // Never wider than the record: whatever the screen answered, the new certificate holds
            // only what was offered, once each.
            var offered = new HashSet<string>(request.Profiles, StringComparer.Ordinal);
            var profiles = decision.Profiles.Where(offered.Contains).Distinct(StringComparer.Ordinal).ToList();

            // And never wider than the list as it is now. What was offered was read before the
            // dialog, and a name the operator took off the list since would be put back by the
            // write, which adds every name a record holds that the list does not: every rule
            // naming it and every client holding it would come back with nobody told. The write
            // compares the list again under the writer's lock, for a name taken off after this.
            var named = new HashSet<string>(target.KnownProfiles ?? Array.Empty<string>(), StringComparer.Ordinal);
            var gone = profiles.Count(profile => !named.Contains(profile));
            if (gone > 0)
            {
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was approved for " + gone +
                          (gone == 1 ? " profile" : " profiles") + " this database stopped naming while the " +
                          "operator decided, which the new certificate does not carry");
                profiles = profiles.Where(named.Contains).ToList();
            }

            // Held from here until it is written, with the record it renews as the operator saw it.
            return _held.Hold(
                approval,
                target.DatabaseId,
                profiles,
                decision.Lifetime,
                profiles,
                ApprovalBook.Digest(record.Save()),
                arrived + _options.ApprovalHeld);
        }

        /// <summary>
        /// Answers a renewal the operator has already answered, and says whether it did.
        /// </summary>
        /// <remarks>
        /// **The database's answer is asked first, and the session's after it.** The dialog is
        /// raised only while neither refuses, so the two stand together only when a merge has
        /// brought in an answer from another copy of the file, and then the stored one is the
        /// better thing to tell the client: forever says that only pairing again helps, which an
        /// answer about this session would leave it to find out after KeePass next starts. A
        /// stored until-date is told there too, and once its day begins the session's answer
        /// takes over, so its `notBefore` is not then a moment the operator is asked again.
        /// </remarks>
        /// <param name="record">The record being renewed, whose client key the audit line names.</param>
        private bool Standing(IGateContext context, IGateTarget target, string key, AdmissionRecord record)
        {
            RenewalRefusal standing;
            try
            {
                standing = target.StandingRefusal(key);
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Error("a standing answer to a renewal could not be read", error);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return true;
            }

            if (standing == null || !standing.Refuses(_clock()))
            {
                // The plugin's memory after the database: this session's answer, or one for longer
                // the database could not take when it was given and is owed it still.
                standing = _session.Answer(key);
                if (standing != null && !standing.Refuses(_clock()))
                {
                    standing = null;
                }
            }

            if (standing == null)
            {
                return false;
            }

            // Written down like the dialog's refusals, and paced: nobody is asked, so a client can
            // send these as fast as it likes, and every one is answered at once.
            _audit.RenewalRefusedWithoutAsking(target.DatabaseId, key, record.ClientKey, standing);

            if (standing.IsForever)
            {
                _log.Info("a renewal by " + CertificateDigest.Shorten(key) +
                          " was refused without asking: the operator refused it forever");
                context.Respond(409, HttpResponse.ErrorBody("forever"));
                return true;
            }

            if (standing.IsThisSession)
            {
                _log.Info("a renewal by " + CertificateDigest.Shorten(key) +
                          " was refused without asking: the operator refused it in this session");
                context.Respond(409, HttpResponse.ErrorBody("this session"));
                return true;
            }

            var notBefore = Moments.Wire(standing.NotBefore.Value);
            _log.Info("a renewal by " + CertificateDigest.Shorten(key) +
                      " was refused without asking: the operator said not before " + notBefore);
            context.Respond(409, TooEarly(notBefore));
            return true;
        }

        /// <summary>The body of `409 too early`, which carries the moment to ask again from.</summary>
        private static string TooEarly(string notBefore)
        {
            return "{\"error\":\"too early\",\"notBefore\":" + JsonConvert.ToString(notBefore) + "}";
        }

        /// <summary>
        /// Keeps the operator's standing answer, when they gave one, and says in the log what came
        /// of it. The refusal stands either way.
        /// </summary>
        /// <remarks>
        /// An answer for this session is kept in memory and cannot fail to be. The other two are
        /// written to the database, which can, and **one the database cannot take now is kept in
        /// memory as well**, the operator's ruling of 2026-10-14: it refuses from this moment, as
        /// an answer about this session does, and the plugin's next write to the database that
        /// lands writes it, after which the database's own copy answers.
        /// </remarks>
        private void Refused(IGateTarget target, string key, RenewalRefusal standing)
        {
            if (standing == null)
            {
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused once");
                return;
            }

            if (standing.IsThisSession)
            {
                _session.Refuse(key);
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused in this session, " +
                          "which holds until KeePass exits");
                return;
            }

            Vault.VaultWrite written;
            try
            {
                written = target.RefuseRenewal(key, standing);
            }
            catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfUserInterface(error))
            {
                _log.Error("a standing answer to a renewal could not be written", error);
                written = null;
            }

            if (written != null && written.Ok)
            {
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused " +
                          (standing.IsForever ? "forever" : "until " + Moments.Wire(standing.NotBefore.Value)));
                return;
            }

            _session.Owe(target.DatabaseId, key, standing);
            _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused " +
                      (standing.IsForever ? "forever" : "until " + Moments.Wire(standing.NotBefore.Value)) +
                      ", and the database could not take the answer now, so it holds from memory until " +
                      "the plugin's next write to the database lands with it" +
                      (written == null ? string.Empty : ": " + written.Reason));
        }

        /// <summary>
        /// What the operator is shown, read from the record and the database rather than from the
        /// request, or null when the record will not say.
        /// </summary>
        private RenewalRequest Request(
            IGateContext context, IGateTarget target, AdmissionRecord record, TimeSpan? lifetime)
        {
            var name = NameIn(record.Subject);
            if (name == null)
            {
                _log.Error("a renewal was asked for under a record whose subject is not a name this " +
                           "authority issues", null);
                return null;
            }

            DateTimeOffset expires;
            try
            {
                using (var certificate = record.Certificate())
                {
                    expires = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
                }
            }
            catch (Exception error) when (Expected.OfCryptography(error))
            {
                _log.Error("a renewal was asked for under a record whose certificate would not read", error);
                return null;
            }

            var known = new HashSet<string>(target.KnownProfiles ?? Array.Empty<string>(), StringComparer.Ordinal);
            var held = record.Profiles ?? Array.Empty<string>();

            return new RenewalRequest(
                target.DatabaseId,
                Named(target),
                name,
                record.Admitted,
                expires,
                held.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList(),
                held.Where(profile => !known.Contains(profile)).Distinct(StringComparer.Ordinal).ToList(),
                ConnectionPeer.Of(context),
                lifetime);
        }

        /// <summary>Issue, write, save, and only then hand anything over, as pairing does.</summary>
        /// <param name="clientKey">
        /// The client key of the record that asked, which the new record carries: a renewal is the
        /// same client under a new certificate.
        /// </param>
        private void Issue(
            IGateContext context, IGateTarget target, string key, string name, string clientKey, HeldApproval held)
        {
            var profiles = held.Profiles;
            var lifetime = held.Lifetime;
            var what = "a renewal by " + CertificateDigest.Shorten(key);

            // Outside the filter below, which names `ArgumentException` among the ways a key
            // fails to sign: a lifetime no dialog offers is a defect here, and it travels to the
            // connection thread's catch-all, which counts it, rather than reading as a failed
            // issuance.
            if (!Lifetime.IsAllowed(lifetime))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(held), "The approval carried a lifetime no dialog offers.");
            }

            X509Certificate2 certificate;
            try
            {
                certificate = _issuer.IssueClient(target.Authority, name, profiles, lifetime);
            }
            catch (Exception error) when (Expected.OfIssuance(error))
            {
                _log.Error("a renewed client certificate could not be issued", error);
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return;
            }

            try
            {
                var record = AdmissionRecord.For(certificate, _clock(), profiles, clientKey);

                // Written by the write, as pairing's is, once the new record has landed and before
                // anything else hears of it. It waits for nothing there, on KeePass's own thread,
                // so the client's turn for it is taken below, on this connection.
                var databaseId = target.DatabaseId;
                var issued = record.AdmissionKey;

                // Asked under the writer's lock, once the database is known to be writable, as
                // pairing's is: the approval is still held and is used up, and the database still
                // holds what it was given against. **The record renewed is part of that**, so a
                // revocation that lands after the handler last read it, by the client's own
                // `DELETE /gate/certificate` on another connection or by the operator, is found
                // here, under the lock it was written under, rather than overtaken.
                string refused = null;
                var taken = false;
                Func<IKeyValueStore, string> condition = store =>
                {
                    if (!_held.Take(held))
                    {
                        refused = held.Cleared ? "locked" : "expired";
                        return "the approval is no longer held";
                    }

                    taken = true;
                    var stale = held.Stale(store, key);
                    if (stale == HeldStaleness.Authority || stale == HeldStaleness.Unpaired)
                    {
                        refused = "unpaired";
                        return "the record it renews went, or changed, after the operator answered";
                    }

                    if (stale != HeldStaleness.None)
                    {
                        refused = "stale";
                        return "a profile it approved is not on the list as it was when the operator answered";
                    }

                    return null;
                };

                AdmissionWrite written;
                try
                {
                    written = target.Admit(record, condition, () => _audit.Renewed(databaseId, key, clientKey, issued))
                              ?? AdmissionWrite.Failed("nothing was reported");
                }
                catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfUserInterface(error))
                {
                    _log.Error("a renewed admission record could not be written", error);
                    written = AdmissionWrite.Failed(error.Message);
                }

                if (!written.Ok)
                {
                    Unwritten(context, held, written, refused, taken, what);
                    return;
                }

                _log.Info("renewed " + CertificateDigest.Shorten(key) + " as " +
                          CertificateDigest.Shorten(record.AdmissionKey) + " with " +
                          CertificateDigest.Shorten(target.DatabaseId));
                _audit.TakeTurn(clientKey);

                context.Respond(200, PairingHandler.Bundle(target, certificate, profiles));
            }
            finally
            {
                certificate.Reset();
            }
        }

        /// <summary>Answers a renewal whose new record was not written.</summary>
        /// <remarks>
        /// Busy keeps the approval held, as pairing's does. What the write's condition refused uses
        /// it up and is told for what it was: a lock or a close, the record gone, or the list no
        /// longer what the operator approved against, which asks them again. A write that failed
        /// once the approval was taken is pairing's `500 not recorded`: the operator's approval is
        /// spent, nothing is handed over that the database does not know about, and the old
        /// certificate still works.
        /// </remarks>
        private void Unwritten(
            IGateContext context, HeldApproval held, AdmissionWrite written, string refused, bool taken, string what)
        {
            if (written.IsBusy)
            {
                if (_held.Holds(held))
                {
                    PairingHandler.Busy(context, held, what, written.Reason, _log);
                    return;
                }

                PairingHandler.Gone(context, held, what, _log);
                return;
            }

            switch (refused)
            {
                case "locked":
                case "expired":
                    PairingHandler.Gone(context, held, what, _log);
                    return;

                case "unpaired":
                    _log.Warn(what + " was approved after its record went, so nothing was issued: " + written.Reason);
                    context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                    return;

                case "stale":
                    _log.Warn(what + " was approved and not written, so the operator is asked again: " +
                              written.Reason);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return;
            }

            if (!taken)
            {
                // Nothing was touched and the approval was not used: the database closed under
                // the write, or the write could not be attempted. It goes all the same, and asking
                // again asks the operator again.
                _held.Take(held);
                _log.Warn(what + " was approved and not written, and nothing changed: " + written.Reason);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return;
            }

            _log.Warn("a renewal was abandoned after the record did not land" +
                      (written.IsBusy ? ", and it might have worked a moment later: " : ": ") +
                      written.Reason);
            context.Respond(500, HttpResponse.ErrorBody("not recorded"));
        }

        /// <summary>
        /// The name a subject was issued under, or null when it is not one this authority issues.
        /// </summary>
        /// <remarks>
        /// Read off the subject the record keeps, which the issuer wrote as `CN=` and the name and
        /// nothing else; a name it issues holds no character a distinguished name would quote.
        /// </remarks>
        internal static string NameIn(string subject)
        {
            const string Prefix = "CN=";
            if (subject == null || !subject.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            var name = subject.Substring(Prefix.Length);
            return AuthorityIssuer.IsUsableSubjectName(name) ? name : null;
        }

        /// <summary>
        /// Reads the record, or answers `503 busy` and says false when the records could not be
        /// listed, which says nothing about whether this one is there.
        /// </summary>
        private bool Admission(IGateContext context, IGateTarget target, string key, out AdmissionRecord record)
        {
            try
            {
                record = target.Admission(key);
                return true;
            }
            catch (DatabaseShut error)
            {
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused: " + error.Message);
                context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                record = null;
                return false;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("a renewal by " + CertificateDigest.Shorten(key) + " was refused for now: " + error.Message);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                record = null;
                return false;
            }
        }

        private static string ProfilesUnreadable(IGateTarget target)
        {
            try
            {
                return target.ProfilesUnreadable;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                return "the profiles this database names could not be read: " + error.Message;
            }
        }

        private string Named(IGateTarget target)
        {
            try
            {
                return target.DatabaseName ?? string.Empty;
            }
            catch (Exception error) when (Expected.OfCustomData(error))
            {
                _log.Warn("a database would not say what it is called: " + error.Message);
                return string.Empty;
            }
        }

        private string Blocked(IGateTarget target)
        {
            try
            {
                return target.AdmissionBlocked;
            }
            catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfUserInterface(error))
            {
                _log.Error("whether a record could be written could not be established", error);
                return "the database could not be reached";
            }
        }
    }
}
