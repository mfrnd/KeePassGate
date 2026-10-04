using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pipeline;
using KeePassGate.Transport;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KeePassGate.Pairing
{
    /// <summary>How long each half of pairing is allowed to take.</summary>
    public sealed class PairingOptions
    {
        public PairingOptions()
        {
            ChallengeLifetime = TimeSpan.FromMinutes(2);
            ApprovalTimeout = TimeSpan.FromMinutes(2);
            ApprovalSettle = TimeSpan.FromSeconds(HttpResponse.RetryAfterSeconds);
            ApprovalHeld = GateOptions.DefaultPairingBudget;
        }

        /// <summary>
        /// How long an approval waits for KeePass to stop being busy before it is held and the
        /// client is answered `503 busy` with the moment it is held until.
        /// </summary>
        /// <remarks>
        /// As long as the `Retry-After` the client would be told, so a busy that clears that fast
        /// is written on the same connection rather than on a retry. What keeps the operator's
        /// decision when a busy lasts longer is <see cref="ApprovalHeld"/>, not this.
        /// </remarks>
        public TimeSpan ApprovalSettle { get; set; }

        /// <summary>
        /// How long an approval that could not be written is held for the client's retry, counted
        /// from when its request arrived.
        /// </summary>
        /// <remarks>
        /// The pairing budget the request was measured against, so an approval is held for what is
        /// left of it: a client that waits for that long, as the reference client does, has come
        /// back by then or is not coming back.
        /// </remarks>
        public TimeSpan ApprovalHeld { get; set; }

        /// <summary>
        /// How long a client has to follow a hello with a request. Short, because the two are
        /// consecutive calls by the same program and a challenge that outlives its exchange is a
        /// value sitting in memory for no reason.
        /// </summary>
        public TimeSpan ChallengeLifetime { get; set; }

        /// <summary>
        /// How long the operator has to answer. Long enough to read the dialog and think about it,
        /// short enough that an unanswered pairing frees the connection rather than holding it. A
        /// timeout does not spend the invitation.
        /// </summary>
        public TimeSpan ApprovalTimeout { get; set; }
    }

    /// <summary>
    /// The two routes an invitee may reach, and the ordering that makes a certificate revocable.
    /// </summary>
    /// <remarks>
    /// `PAIRING.md` §303, phases 3 to 5, in one class.
    ///
    /// **The client speaks first.** `/pair/hello` carries a proof over the channel binding and the
    /// nonce from the name it arrived on, so a captured name replayed at the gate gets no further
    /// than here and raises no dialog. Only then does the server answer with a proof of its own,
    /// which is what tells a client with no trust anchor yet that it is talking to the KeePass whose
    /// operator holds the token, and not to whatever accepted the connection.
    ///
    /// **The exchange spans two connections**, because this transport serves one request per
    /// connection. The challenge is what carries the exchange across: it is held on the invitation,
    /// in memory, with the binding and nonce it was issued against, and it is spent when it is
    /// taken. So a request has to name a hello that really happened, on a connection served with
    /// the same certificate.
    ///
    /// **The order in `/pair/request` is the point of the whole design.** The blocked check comes
    /// before the operator is disturbed and again once they have answered, so a dialog left open
    /// costs a hello rather than the invitation. The challenge is already spent by then, which is
    /// what makes a second try need a fresh hello. The certificate is issued, the invitation is burned, and
    /// the record is written and saved, and only then is anything handed over. A certificate whose
    /// record never landed is a credential nobody can see and nobody can revoke, so this refuses
    /// rather than hands one over.
    /// </remarks>
    public sealed class PairingHandler
    {
        public const string HelloPath = "/pair/hello";

        public const string RequestPath = "/pair/request";

        private readonly IInvitations _invitations;
        private readonly IPairingApproval _approval;
        private readonly PairingOptions _options;
        private readonly AuthorityIssuer _issuer;
        private readonly Func<DateTimeOffset> _clock;
        private readonly IGateLog _log;
        private readonly ApprovalBook _held;

        public PairingHandler(IInvitations invitations, IPairingApproval approval, IGateLog log)
            : this(invitations, approval, new PairingOptions(), new AuthorityIssuer(), null, log)
        {
        }

        /// <param name="held">
        /// The approvals held for a retry, which a lock, a close and an exit clear, or null for a
        /// book of this handler's own.
        /// </param>
        public PairingHandler(
            IInvitations invitations, IPairingApproval approval, ApprovalBook held, IGateLog log)
            : this(invitations, approval, new PairingOptions(), new AuthorityIssuer(), null, log, held)
        {
        }

        public PairingHandler(
            IInvitations invitations,
            IPairingApproval approval,
            PairingOptions options,
            AuthorityIssuer issuer,
            Func<DateTimeOffset> clock,
            IGateLog log,
            ApprovalBook held = null)
        {
            if (invitations == null)
            {
                throw new ArgumentNullException(nameof(invitations));
            }

            _invitations = invitations;
            _approval = approval ?? NoPairingApproval.Instance;
            _options = options ?? new PairingOptions();
            _issuer = issuer ?? new AuthorityIssuer();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _log = log ?? NullGateLog.Instance;
            _held = held ?? new ApprovalBook(_clock);
        }

        /// <summary>`pairHello`, once the router has decided this is the operation.</summary>
        public void Hello(IGateContext context)
        {
            var connection = Reached(context);
            if (connection != null)
            {
                Hello(context, connection);
            }
        }

        /// <summary>`pairRequest`, once the router has decided this is the operation.</summary>
        public void Request(IGateContext context)
        {
            var connection = Reached(context);
            if (connection != null)
            {
                Request(context, connection);
            }
        }

        /// <summary>
        /// Whether this connection may be here at all, which only pairing knows.
        /// </summary>
        /// <remarks>
        /// The router asks this before it looks at a body, so that a caller which may not reach
        /// these routes never learns whether what it sent would have been accepted. It is asked
        /// again inside the operations below, because a class that is only safe when something
        /// else remembers to check first is a class waiting to be called directly.
        /// </remarks>
        public static bool MayReach(IGateContext context)
        {
            return Reached(context) != null;
        }

        /// <summary>
        /// The same question, answering with the connection when the answer is yes.
        /// </summary>
        /// <remarks>
        /// The router decided that the path and the method are declared and that the operation
        /// needs no certificate. What it cannot know is that a pairing route is not open to
        /// anybody who reaches the port: the connection has to have arrived on an invitation, and a
        /// client that already holds a certificate has no business here.
        ///
        /// Neither is answered anything of its own. Both get `Undeclared.Refuse`, which is what
        /// the router says about a path nobody declared: the `404` an undeclared route gets for an
        /// admitted client, and for anybody else the `401` every route gets before a certificate
        /// is proved. Answering `404` to both made this one prefix the only one in the API that
        /// replied differently.
        ///
        /// **That does not hide that these routes exist, and it is not meant to.** The router
        /// answers `405` for a declared path asked for with an undeclared method, and these two
        /// paths are declared in a document that ships inside the plugin, so `GET /pair/hello` says
        /// as much as reading that document says. What it keeps is the rule that a caller which may
        /// not *use* a route is refused the same way everywhere, rather than learning from the
        /// refusal whether it was close.
        /// </remarks>
        private static PairingConnection Reached(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var connection = PairingConnection.Of(context);
            if (connection != null && !context.Client.IsPaired)
            {
                return connection;
            }

            Undeclared.Refuse(context);
            return null;
        }

        /// <summary>
        /// Phase 3. Both proofs, in the order that means an impostor does no work until the caller
        /// has shown it holds the token.
        /// </summary>
        private void Hello(IGateContext context, PairingConnection connection)
        {
            var body = JsonBody.Read(context.Body);
            var offered = body == null ? null : Base64(body.Text("proof"));
            if (offered == null)
            {
                context.Respond(400, HttpResponse.ErrorBody("malformed"));
                return;
            }

            var token = connection.Invitation.Token;
            if (token == null)
            {
                // Burned or expired between the gate resolving it and here.
                context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                return;
            }

            try
            {
                var binding = connection.Binding;
                var nonce = connection.Nonce;

                if (!FixedTime.Equals(PairingProof.Client(token, binding, nonce), offered))
                {
                    // No dialog is raised and nothing is spent. `PAIRING.md` §306: a failed
                    // proof leaves the invitation exactly where it was.
                    _log.Warn("pairing proof refused on invitation " + connection.Invitation.Id +
                              " from " + connection.Peer);
                    context.Respond(401, HttpResponse.ErrorBody("proof"));
                    return;
                }

                var challenge = connection.Invitation.Challenge(
                    binding, nonce, _clock(), _options.ChallengeLifetime);

                if (challenge == null)
                {
                    context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                    return;
                }

                var proof = PairingProof.Server(token, binding, nonce, challenge.Value);

                _log.Info("pairing proof accepted on invitation " + connection.Invitation.Id +
                          " from " + connection.Peer);

                // Written by `Json.NET`, as the bundle is, rather than spliced between quotes. Every
                // value here is base64 or a timestamp and needs no escaping, which is true of these
                // three and says nothing about the next member somebody adds.
                var answer = new JObject
                {
                    ["challenge"] = Convert.ToBase64String(challenge.Value),
                    ["proof"] = Convert.ToBase64String(proof),
                    ["expires"] = Moment(challenge.Expires)
                };

                context.Respond(200, answer.ToString(Formatting.None));
            }
            finally
            {
                Array.Clear(token, 0, token.Length);
            }
        }

        /// <summary>Phases 4 and 5. The operator decides, and then the record lands before anything is given out.</summary>
        private void Request(IGateContext context, PairingConnection connection)
        {
            // When the pairing budget began, near enough: the request is off the wire by now, and
            // an approval is held for what is left of that budget.
            var arrived = _clock();

            var body = JsonBody.Read(context.Body);
            if (body == null)
            {
                context.Respond(400, HttpResponse.ErrorBody("malformed"));
                return;
            }

            var name = body.Text("name");
            var asked = body.Array("profiles") ?? Array.Empty<string>();
            var offered = Base64(body.Text("proof"));

            // How long the client asks its certificate to last, as it wrote it, which is what the
            // proof below is computed over. The document holds it to one spelling and this reads
            // it again: present and not a lifetime is the same refusal as any other body that is
            // not what the document declares.
            var lifetimeWritten = body.Text("lifetime");
            var lifetime = Lifetime.Read(lifetimeWritten);

            if (name == null || offered == null
                || (body.Names.Contains("lifetime") && lifetime == null))
            {
                context.Respond(400, HttpResponse.ErrorBody("malformed"));
                return;
            }

            if (!AuthorityIssuer.IsUsableSubjectName(name))
            {
                context.Respond(400, HttpResponse.ErrorBody("name"));
                return;
            }

            foreach (var profile in asked)
            {
                if (!AuthorityIssuer.IsUsableProfileName(profile))
                {
                    context.Respond(400, HttpResponse.ErrorBody("profile"));
                    return;
                }
            }

            var invitation = connection.Invitation;
            var token = invitation.Token;
            if (token == null)
            {
                context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                return;
            }

            PairingChallenge challenge;
            try
            {
                // Taken, so it is spent whatever happens next. A request that fails cannot be
                // retried against the same challenge, only against a fresh hello.
                challenge = invitation.TakeChallenge(_clock());
                if (challenge == null)
                {
                    context.Respond(409, HttpResponse.ErrorBody("no exchange"));
                    return;
                }

                if (!FixedTime.Equals(challenge.Binding, connection.Binding))
                {
                    // The hello and the request were served with different certificates, so they
                    // did not reach the same database, or the authority changed in between.
                    _log.Warn("pairing request refused: it did not follow its own hello");
                    context.Respond(409, HttpResponse.ErrorBody("no exchange"));
                    return;
                }

                var expected = PairingProof.Request(
                    token,
                    challenge.Binding,
                    challenge.Nonce,
                    challenge.Value,
                    PairingProof.Asked(name, asked, lifetimeWritten));

                if (!FixedTime.Equals(expected, offered))
                {
                    _log.Warn("pairing request refused on invitation " + invitation.Id + ": proof");
                    context.Respond(401, HttpResponse.ErrorBody("proof"));
                    return;
                }
            }
            finally
            {
                Array.Clear(token, 0, token.Length);
            }

            var key = ApprovalBook.Pairing(invitation.Id, name, asked, lifetimeWritten);
            var held = _held.Find(key, connection.Target.DatabaseId);
            if (held != null)
            {
                // Approved on an earlier connection and held because KeePass was busy then: the
                // operator has answered exactly this, so nothing is put in front of them again.
                _log.Info("pairing on invitation " + invitation.Id + " was approved earlier and held, " +
                          "so it is written now without asking again");
            }
            else
            {
                held = Approve(context, connection, name, asked, lifetime, key, arrived);
                if (held == null)
                {
                    return;
                }
            }

            Write(context, connection, name, held);
        }

        /// <summary>
        /// Puts the request to the operator, and holds their approval, or answers and says null
        /// when there is none.
        /// </summary>
        private HeldApproval Approve(
            IGateContext context,
            PairingConnection connection,
            string name,
            IReadOnlyList<string> asked,
            TimeSpan? lifetime,
            string key,
            DateTimeOffset arrived)
        {
            var target = connection.Target;
            var invitation = connection.Invitation;

            // Before the operator is disturbed. The challenge is already spent by now, so a
            // client turned away here needs a fresh hello rather than a second try. Database
            // Maintenance open on this database would take the record straight back off again.
            var blocked = Blocked(target);
            if (blocked != null)
            {
                _log.Warn("pairing refused for now: " + blocked);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            // A list that will not read is not offered as one naming nothing, as a renewal's is
            // not: every name would be shown as "(new)", names the damaged list holds among them,
            // and approving would write a list of only the approved names over it, dropping every
            // other client's for good and the sign that the list was damaged with them.
            var unreadable = ProfilesUnreadable(target);
            if (unreadable != null)
            {
                _log.Warn("pairing refused for now: " + unreadable);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            // Named as well as fingerprinted. The operator is being asked to let a stranger into
            // one of possibly several open databases, and sixteen hexadecimal characters is not
            // something anybody recognizes under time pressure.
            //
            // Asked for once each. A caller may send the same profile twice, and the dialog would
            // then show two identical rows: clearing one of them leaves the other checked, so a
            // profile the operator refused is granted anyway. The proof was computed over the list
            // exactly as it arrived, which is why this happens here and not before it was checked.
            var once = Once(asked);
            var known = target.KnownProfiles;
            var request = new PairingRequest(
                target.DatabaseId,
                Named(target),
                invitation.Id,
                name,
                once,
                known,
                connection.Peer,
                Returning(target, once, known),
                lifetime);

            PairingApproval decision;
            try
            {
                decision = _approval.Ask(request, _options.ApprovalTimeout) ?? PairingApproval.TimedOut();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the pairing dialog failed", error);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            switch (decision.Decision)
            {
                case PairingDecision.TimedOut:
                    // Nothing is spent. An absent operator does not cost the invitee its invitation.
                    _log.Warn("pairing on invitation " + invitation.Id + " went unanswered");
                    context.Respond(504, HttpResponse.ErrorBody("unanswered"));
                    return null;

                case PairingDecision.Unavailable:
                    _log.Warn("pairing on invitation " + invitation.Id + " could not be put to anybody: " +
                              decision.Reason);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return null;

                case PairingDecision.Locked:
                    // Unanswered, as a timeout is, so nothing is spent; told as every connection to
                    // a locked database is told, since that is what this one now is.
                    _log.Warn("pairing on invitation " + invitation.Id + " was " + decision.Reason);
                    context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                    return null;

                case PairingDecision.Refused:
                    _invitations.Burn(invitation.Id);
                    _log.Warn("pairing on invitation " + invitation.Id + " was refused, and it is spent");
                    context.Respond(403, HttpResponse.ErrorBody("refused"));
                    return null;
            }

            // Held from here until it is written, so that KeePass being busy when the operator has
            // answered costs a retry rather than the answer. Once each here too, because what comes
            // back is whatever the screen answered and a repeated profile would reach the
            // certificate as a repeated claim. The lifetime is the operator's choice, which the
            // issuer holds to `Lifetime`'s bounds once more. What the list held of the approved
            // names is kept with it, because the write compares that under the writer's lock.
            var approved = Once(decision.Profiles);
            var listed = new HashSet<string>(target.KnownProfiles ?? Array.Empty<string>(), StringComparer.Ordinal);
            return _held.Hold(
                key,
                target.DatabaseId,
                approved,
                decision.Lifetime,
                approved.Where(listed.Contains),
                null,
                arrived + _options.ApprovalHeld);
        }

        /// <summary>
        /// Phase 5 for an approval the operator has given: waits a moment for a busy KeePass, then
        /// issues, writes and hands over, or holds it for the client's retry.
        /// </summary>
        private void Write(IGateContext context, PairingConnection connection, string name, HeldApproval held)
        {
            var target = connection.Target;
            var invitation = connection.Invitation;

            // Asked again, because the answer before the dialog is as old as the dialog. Database
            // Maintenance opened while the operator was reading, or KeePass saving, is found here
            // while it can still be a `503` that spends nothing. A lock while the dialog stands
            // closes the dialog instead, `PAIRING.md` §308, and a lock after it takes the held
            // approval away, which ends this wait as the lock it is.
            var blocked = Settled(target, _options.ApprovalSettle, Blocked, () => _held.Holds(held));
            if (!_held.Holds(held))
            {
                Gone(context, held, "pairing on invitation " + invitation.Id, _log);
                return;
            }

            if (blocked != null)
            {
                Busy(context, held, "pairing on invitation " + invitation.Id, blocked, _log);
                return;
            }

            Issue(context, connection, name, held);
        }

        /// <summary>
        /// Answers an approval that is held no longer: `503 locked` when a lock or a close took it,
        /// and `503 busy` with nothing held when its time ran out, which raises the dialog again.
        /// </summary>
        internal static void Gone(IGateContext context, HeldApproval held, string what, IGateLog log)
        {
            if (held.Cleared)
            {
                log.Warn(what + " was approved, and KeePass locked or closed the database before it " +
                          "could be written, so nothing was issued");
                context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                return;
            }

            log.Warn(what + " was approved, and was held until its time ran out without being written");
            context.Respond(503, HttpResponse.ErrorBody("busy"));
        }

        /// <summary>
        /// Answers an approval that could not be written yet: `503 busy`, saying until when it is
        /// held, so that the same request asked again before then is written without the dialog.
        /// </summary>
        internal static void Busy(IGateContext context, HeldApproval held, string what, string why, IGateLog log)
        {
            var until = Moments.Wire(held.Until);
            log.Warn(what + " was approved and could not be written yet, so it is held until " + until +
                      ": " + why);
            context.Respond(503, HeldBody(until));
        }

        /// <summary>The body of a `503 busy` that holds an approval.</summary>
        internal static string HeldBody(string until)
        {
            return "{\"error\":\"busy\",\"heldUntil\":" + JsonConvert.ToString(until) + "}";
        }

        /// <summary>
        /// Phase 5. Issue, then land the record and spend the invitation together, and only then
        /// hand anything over.
        /// </summary>
        private void Issue(IGateContext context, PairingConnection connection, string name, HeldApproval held)
        {
            var target = connection.Target;
            var invitation = connection.Invitation;
            var profiles = held.Profiles;
            var lifetime = held.Lifetime;

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
                // `OfIssuance` rather than `OfCryptography`, which does not name the
                // `InvalidOperationException` an expired authority is refused with. An authority
                // is issued for a hundred years and nothing refuses ahead of that, so this is where
                // a database that outlived its own runs out, as well as a key that would not sign;
                // left unnamed it escaped the handler to the catch-all on the connection thread,
                // which counts anything a handler throws as a defect in this plugin.
                _log.Error("a client certificate could not be issued", error);
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return;
            }

            try
            {
                // A profile the operator approved that this database had never named is created by
                // their approval, and the writer does that inside the same marshaled write as the
                // record, off the profiles the record already carries.
                var record = AdmissionRecord.For(certificate, _clock(), profiles);

                // **Asked under the writer's lock, once the database is known to be writable**,
                // which is the one moment none of it can move before the record relies on it: the
                // approval is still held and is used up, the database still holds what it was
                // given against, and the invitation is spent, last, so that nothing before the
                // record can fail once it is. Everything that can still answer busy is asked
                // before this, so a `503` never follows a spent invitation.
                string refused = null;
                var spent = false;
                Func<IKeyValueStore, string> condition = store =>
                {
                    if (!_held.Take(held))
                    {
                        refused = held.Cleared ? "locked" : "expired";
                        return "the approval is no longer held";
                    }

                    var stale = held.Stale(store, null);
                    if (stale == HeldStaleness.Authority)
                    {
                        refused = "no invitation";
                        return "the authority it was approved under is no longer the one served";
                    }

                    if (stale != HeldStaleness.None)
                    {
                        refused = "stale";
                        return "a profile it approved is not on the list as it was when the operator answered";
                    }

                    if (!_invitations.Burn(invitation.Id))
                    {
                        refused = "no invitation";
                        return "the invitation was spent or ran out while the operator was deciding";
                    }

                    spent = true;
                    return null;
                };

                AdmissionWrite written;
                try
                {
                    written = target.Admit(record, condition) ?? AdmissionWrite.Failed("nothing was reported");
                }
                catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfUserInterface(error))
                {
                    _log.Error("an admission record could not be written", error);
                    written = AdmissionWrite.Failed(error.Message);
                }

                if (!written.Ok && !spent)
                {
                    Unwritten(context, held, written, refused, "pairing on invitation " + invitation.Id);
                    return;
                }

                if (!written.Ok)
                {
                    // The invitation is gone and the certificate is not handed over, so nothing was
                    // issued that the database does not know about. The operator invites again.
                    //
                    // **Never 503 here, however transient the reason was.** The invitation was
                    // spent inside the write, so a client told to try again would be turned away
                    // at the name before it reached a route at all, and dropped without a word.
                    // Whether it was worth retrying is something only the operator can act on, so
                    // that part goes in the log rather than on the wire.
                    _log.Warn("pairing abandoned after the record did not land" +
                              (written.IsBusy ? ", and it might have worked a moment later: " : ": ") +
                              written.Reason);
                    context.Respond(500, HttpResponse.ErrorBody("not recorded"));
                    return;
                }

                // Who and where, and not the profiles. Which profiles a client holds is in the
                // record already, and a log repeating them is a second copy of the thing worth
                // stealing, kept in the log's file, which nothing encrypts.
                _log.Info("paired " + certificate.Subject + " with " +
                          CertificateDigest.Shorten(target.DatabaseId));

                context.Respond(200, Bundle(target, certificate, profiles));
            }
            finally
            {
                certificate.Reset();
            }
        }

        /// <summary>
        /// Answers a pairing whose record was not written and whose invitation was not spent.
        /// </summary>
        /// <remarks>
        /// **Busy keeps the approval held**: the write found KeePass busy before it took anything,
        /// so the same request asked again before the approval runs out is written without the
        /// dialog. **Anything the write's condition refused uses the approval up**, and what the
        /// client is told is what refused it: a lock or a close, the invitation gone, or the
        /// database no longer holding what the operator approved, which asks them again.
        /// </remarks>
        private void Unwritten(
            IGateContext context, HeldApproval held, AdmissionWrite written, string refused, string what)
        {
            if (written.IsBusy)
            {
                if (_held.Holds(held))
                {
                    Busy(context, held, what, written.Reason, _log);
                    return;
                }

                Gone(context, held, what, _log);
                return;
            }

            switch (refused)
            {
                case "locked":
                case "expired":
                    Gone(context, held, what, _log);
                    return;

                case "no invitation":
                    // Gone while the operator was reading the dialog or while the approval was
                    // held: somebody else spent it, and only one of them can win, or its time ran
                    // out, and `PAIRING.md` §306 has no way back from gone.
                    _log.Warn(what + " lost the race to spend it, or it ran out while the operator was deciding: " +
                              written.Reason);
                    context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                    return;

                case "stale":
                    _log.Warn(what + " was approved and not written, so the operator is asked again: " +
                              written.Reason);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return;
            }

            // Nothing was touched and nothing spent: the database closed under the write, or the
            // write could not be attempted. The approval goes with it, and asking again asks the
            // operator again.
            _held.Take(held);
            _log.Warn(what + " was approved and not written, and nothing was spent: " + written.Reason);
            context.Respond(503, HttpResponse.ErrorBody("busy"));
        }

        /// <summary>
        /// What the client is given: its own certificate and key, the authority to pin from now on,
        /// and the gate key every connection after this one is named from. A renewal answers with
        /// the same, so that a client stores either the same way.
        /// </summary>
        internal static string Bundle(
            IGateTarget target, X509Certificate2 certificate, IReadOnlyList<string> profiles)
        {
            var gateKey = target.GateKey;

            try
            {
                // Written by `Json.NET`, which is here for the request bodies, rather than
                // assembled out of punctuation and an escaper of our own. What goes into this
                // is a certificate subject and a list of profile names, neither of which this
                // side chose, so the one thing it must not do is depend on a second answer to
                // what a JSON string looks like.
                var text = new StringWriter(CultureInfo.InvariantCulture);
                using (var json = new JsonTextWriter(text) { Formatting = Formatting.None })
                {
                    json.WriteStartObject();

                    json.WritePropertyName("identity");
                    json.WriteValue(CertificateBlob.Save(certificate));
                    json.WritePropertyName("authority");
                    json.WriteValue(Convert.ToBase64String(target.Authority.RawData));
                    json.WritePropertyName("gateKey");
                    json.WriteValue(Convert.ToBase64String(gateKey));
                    json.WritePropertyName("subject");
                    json.WriteValue(certificate.Subject);
                    json.WritePropertyName("database");
                    json.WriteValue(CertificateDigest.Shorten(target.DatabaseId));
                    json.WritePropertyName("expires");
                    json.WriteValue(
                        Moment(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)));

                    json.WritePropertyName("profiles");
                    json.WriteStartArray();
                    foreach (var profile in profiles)
                    {
                        json.WriteValue(profile);
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                return text.ToString();
            }
            finally
            {
                if (gateKey != null)
                {
                    Array.Clear(gateKey, 0, gateKey.Length);
                }
            }
        }

        /// <summary>
        /// What the operator's dialog calls this database, or empty when reading it fails.
        /// </summary>
        /// <remarks>
        /// A name is worth having and is worth nothing at all compared to the pairing itself, so a
        /// database that will not say what it is called is shown by its fingerprint alone rather
        /// than taking the pairing down with it.
        /// </remarks>
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

        /// <summary>
        /// The same list with anything repeated in it kept once, in the order it first appeared.
        /// </summary>
        private static IReadOnlyList<string> Once(IReadOnlyList<string> profiles)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var once = new List<string>();

            foreach (var profile in profiles)
            {
                if (seen.Add(profile))
                {
                    once.Add(profile);
                }
            }

            return once;
        }

        /// <summary>
        /// For each profile asked for that the database does not name, the clients whose records
        /// still hold it.
        /// </summary>
        /// <remarks>
        /// A removed profile stays in every record approved for it, granting nothing while the
        /// list does not name it. Approving it here puts the name back on the list in the same
        /// write as the new record, and every one of those clients regains it, so the dialog has
        /// to say who they are. Asked only for names off the list: a listed name is already held
        /// by whoever holds it, and approving it changes nothing for them.
        /// </remarks>
        private static IReadOnlyDictionary<string, IReadOnlyList<AdmissionRecord>> Returning(
            IGateTarget target, IReadOnlyList<string> asked, IReadOnlyList<string> known)
        {
            var named = new HashSet<string>(known ?? Array.Empty<string>(), StringComparer.Ordinal);
            var returning = new Dictionary<string, IReadOnlyList<AdmissionRecord>>(StringComparer.Ordinal);

            foreach (var profile in asked)
            {
                if (named.Contains(profile))
                {
                    continue;
                }

                var holding = target.Holding(profile);
                if (holding != null && holding.Count > 0)
                {
                    returning[profile] = holding;
                }
            }

            return returning;
        }


        /// <summary>
        /// Why a record cannot be written, asked until it can, until the wait runs out or until the
        /// approval it is for is no longer held, and the last answer, which is null once it can.
        /// </summary>
        /// <remarks>
        /// **An approval let go ends the wait at once.** A lock takes every held approval away, so
        /// one still settling when KeePass locks is answered as the lock rather than written into
        /// the database a quick unlock opens again, after that unlock has closed its connection.
        /// </remarks>
        internal static string Settled(
            IGateTarget target, TimeSpan wait, Func<IGateTarget, string> blocked, Func<bool> holding)
        {
            var answer = blocked(target);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (answer != null && waited.Elapsed < wait && holding())
            {
                System.Threading.Thread.Sleep(100);
                answer = blocked(target);
            }

            return answer;
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

        private static string Moment(DateTimeOffset when)
        {
            return Moments.Wire(when);
        }

        /// <summary>Null for anything that is not base64, which is refused rather than guessed at.</summary>
        private static byte[] Base64(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
