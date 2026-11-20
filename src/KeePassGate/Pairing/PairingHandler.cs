using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Audit;
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
        /// <remarks>
        /// What a dialog shown at once is given. A dialog that comes up late is given what is
        /// left of the pairing budget instead, when that is less: <see cref="DialogLeft"/>.
        /// </remarks>
        public TimeSpan ApprovalTimeout { get; set; }

        /// <summary>
        /// What a pairing or a renewal still waits for once its dialog has closed and before its
        /// save begins: the grace the prompt keeps beyond the dialog, a busy KeePass settling, and
        /// the writer's lock and turn.
        /// </summary>
        /// <remarks>
        /// **The writer's two waits are set aside although the deadline no longer runs through
        /// them.** The connection's deadline is dropped where the record is handed to the writer,
        /// <see cref="Pipeline.ConnectionDeadline"/>, so the gate would wait them out anyway. The
        /// client is what still counts them: it waits as long as the gate's budget, and with them
        /// set aside everything ahead of the save ends inside that wait, leaving only the save,
        /// which no number bounds, to a longer connection timeout.
        /// </remarks>
        public TimeSpan AfterTheDialog
        {
            get
            {
                return Ui.PairingApprovalPrompt.Grace + ApprovalSettle
                    + Bridge.DatabaseWriter.LockTimeout + Bridge.DatabaseWriter.WriteTimeout;
            }
        }

        /// <summary>
        /// How long a dialog asked for now may stand, so that what follows it still ends inside
        /// the pairing budget the request arrived in.
        /// </summary>
        /// <remarks>
        /// The budget as <see cref="ApprovalHeld"/> holds it, counted from when the request
        /// arrived, less <see cref="AfterTheDialog"/>. A dialog that comes up late, behind another
        /// on the screen or behind a busy KeePass, is given this rather than the whole of
        /// <see cref="ApprovalTimeout"/> when it is less, and is not shown at all when it is less
        /// than <see cref="Ui.PairingApprovalPrompt.Shortest"/>. `DESIGN.md` §119 has the
        /// arithmetic.
        /// </remarks>
        internal TimeSpan DialogLeft(PairingMoment arrived, PairingClock clock)
        {
            return arrived.After(ApprovalHeld - AfterTheDialog).Elapsed - clock.Elapsed;
        }
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
    ///
    /// **The operator's answer is written to the audit**, by the invitation it arrived on: approved,
    /// with the key the record admits, once that record is in the database, by the write that put
    /// it there; refused; or unanswered. Nothing is written when nobody could be asked.
    /// `AdmissionAudit` says what a line holds.
    /// </remarks>
    public sealed class PairingHandler
    {
        private readonly IInvitations _invitations;
        private readonly IPairingApproval _approval;
        private readonly PairingOptions _options;
        private readonly AuthorityIssuer _issuer;
        private readonly PairingClock _clock;
        private readonly AdmissionAudit _audit;
        private readonly IGateLog _log;
        private readonly ApprovalBook _held;
        private readonly GateIncidents _incidents;

        /// <summary>With the default options, an issuer of its own and the system clocks.</summary>
        /// <param name="audit">
        /// Where each pairing the operator answers is written down, or null for nowhere.
        /// </param>
        public PairingHandler(IInvitations invitations, IPairingApproval approval, AdmissionAudit audit, IGateLog log)
            : this(invitations, approval, new PairingOptions(), new AuthorityIssuer(), null, audit, log)
        {
        }

        /// <summary>The same, with the plugin's one book of approvals waiting to be written.</summary>
        /// <param name="held">
        /// The approvals held for a retry, which a lock, a close of their database and an exit
        /// clear, or null for a book of this handler's own.
        /// </param>
        /// <param name="incidents">
        /// Where an answer from the screen that this handler does not name is counted, or null for
        /// the log alone.
        /// </param>
        public PairingHandler(
            IInvitations invitations,
            IPairingApproval approval,
            ApprovalBook held,
            AdmissionAudit audit,
            IGateLog log,
            GateIncidents incidents = null)
            : this(invitations, approval, new PairingOptions(), new AuthorityIssuer(), null, audit, log, held, incidents)
        {
        }

        public PairingHandler(
            IInvitations invitations,
            IPairingApproval approval,
            PairingOptions options,
            AuthorityIssuer issuer,
            Func<DateTimeOffset> clock,
            AdmissionAudit audit,
            IGateLog log,
            ApprovalBook held = null,
            GateIncidents incidents = null)
        {
            if (invitations == null)
            {
                throw new ArgumentNullException(nameof(invitations));
            }

            _invitations = invitations;
            _approval = approval ?? NoPairingApproval.Instance;
            _options = options ?? new PairingOptions();
            _issuer = issuer ?? new AuthorityIssuer();
            _clock = PairingClock.For(clock);
            _audit = audit ?? new AdmissionAudit(NullGateAudit.Instance);
            _log = log ?? NullGateLog.Instance;
            _held = held ?? new ApprovalBook(_clock);
            _incidents = incidents ?? new GateIncidents(_log);
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
                    binding, nonce, _options.ChallengeLifetime);

                if (challenge == null)
                {
                    context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                    return;
                }

                var proof = PairingProof.Server(token, binding, nonce, challenge.Value);

                _log.Info("pairing proof accepted on invitation " + connection.Invitation.Id +
                          " from " + connection.Peer);

                // Written by `Json.NET`, as the bundle is, rather than spliced between quotes. The
                // version is whatever the build stamped, which needs it, and the other three are
                // base64 or a timestamp, which do not.
                //
                // **The version is here so that a client can refuse before the operator is asked**,
                // `API.md` §411: a pairing connection reaches no route but the two pairing routes,
                // so the status route that tells a paired client is not a route it can ask.
                // Beside the proof rather than covered by it, since the proof binds this channel
                // to the token and the version arrives on the same channel.
                var answer = new JObject
                {
                    ["challenge"] = Convert.ToBase64String(challenge.Value),
                    ["proof"] = Convert.ToBase64String(proof),
                    ["expires"] = Moment(challenge.Expires),
                    ["version"] = GateVersion.Current
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
            var arrived = _clock.Read();

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
                challenge = invitation.TakeChallenge();
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

            // An approval is held under the request exactly as written, so that a retry with any
            // other body never uses it.
            var key = ApprovalBook.Pairing(invitation.Id, name, asked, lifetimeWritten);

            // **The request is answered once an approval is written, the operator refuses, or the
            // time is up**, and not where its write finds the approval stale: the same request
            // then puts the dialog to the operator again, from a new reading, and writes what is
            // approved there. What it carries from one dialog to the next is its own and goes with
            // it, `Rounds`, so that nothing shown for another request rests on what this dialog
            // showed or what the operator chose on it.
            using (var rounds = new Rounds(_held.Mark()))
            {
                var held = _held.Find(key, connection.Target.DatabaseId);
                if (held != null)
                {
                    // Approved on an earlier connection and held because KeePass was busy then: the
                    // operator has answered exactly this, so nothing is put in front of them again
                    // unless its write finds it stale.
                    _log.Info("pairing on invitation " + invitation.Id + " was approved earlier and held, " +
                              "so it is written now without asking again");
                }
                else
                {
                    held = Approve(context, connection, name, asked, lifetime, key, arrived, rounds);
                    if (held == null)
                    {
                        return;
                    }
                }

                // **Each further dialog is given what is left of the budget the request arrived
                // in**, `PairingOptions.DialogLeft`, as a dialog that comes up late is, and the
                // screen shows none with too little left to be read,
                // `PairingApprovalPrompt.Shortest`, which answers the request as a request too late
                // for the screen. So the loop ends within that budget, at the latest.
                while (!Write(context, connection, name, held, rounds))
                {
                    // **The connection's deadline is taken up again**, to the end of the pairing
                    // budget counted from the request, `ConnectionDeadline.Resume`: the write that
                    // found the approval stale dropped it, and the dialog that follows waits on a
                    // person, which the listener bounds as it bounds the first dialog.
                    ConnectionDeadline.Resume(context);
                    held = Approve(context, connection, name, asked, lifetime, key, arrived, rounds);
                    if (held == null)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// What a pairing request carries from a dialog whose approval its write found stale to
        /// the dialog it puts to the operator again, and nothing outside the request holds.
        /// </summary>
        /// <remarks>
        /// **The place on the screen the request keeps from its first dialog until it is
        /// answered**, so that no other request's dialog comes up between the operator's Approve
        /// and the dialog shown again, `ScreenPlace`. **What the operator approved on the dialog
        /// before, and how that dialog marked each name**, which the next dialog opens with: a row
        /// checked there and marked the same opens checked, every other row unchecked, and its
        /// lifetime chosen, so that a single Approve grants no more than the operator approved.
        /// **Every removal the dialog before held and the request's writes found** beyond it, by
        /// name, which the next reading cannot always tie to the name, a removal that only a
        /// deletion record tells of being tied to it by a hash under the store key: each dialog
        /// after the write holds them all, `AskedProfiles.With`, so that its approval is not stale
        /// over a removal a dialog before it held or was found stale over. For a held approval the
        /// dialog before is the dialog of the request that approved it. **And where the book stood
        /// when the request arrived**, so that a lock or a close of the database between two of
        /// its dialogs is answered as a lock is.
        /// </remarks>
        private sealed class Rounds : IDisposable
        {
            private readonly Dictionary<string, HashSet<string>> _found =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            /// <summary>The same, from this mark of the approvals held on.</summary>
            public Rounds(long mark)
            {
                Mark = mark;
            }

            /// <summary>Where the book of approvals stood when the request arrived.</summary>
            public long Mark { get; private set; }

            /// <summary>The place on the screen, kept from the request's first dialog on.</summary>
            public ScreenPlace Place { get; } = new ScreenPlace();

            /// <summary>
            /// What the operator approved on the dialog before, once a write found that approval
            /// stale, and null before any write has.
            /// </summary>
            public PairingApproval Earlier { get; private set; }

            /// <summary>What the dialog before was built from, beside <see cref="Earlier"/>.</summary>
            public AskedProfiles Before { get; private set; }

            /// <summary>By name, every removal the request's writes found, for the next reading.</summary>
            public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Found
            {
                get
                {
                    return _found.ToDictionary(
                        pair => pair.Key, pair => (IReadOnlyCollection<string>)pair.Value, StringComparer.Ordinal);
                }
            }

            /// <summary>
            /// A write found this approval stale, with these removals beyond what its dialog held.
            /// </summary>
            public void Stale(HeldApproval held, IReadOnlyDictionary<string, IReadOnlyCollection<string>> unseen)
            {
                Earlier = PairingApproval.Approved(held.Profiles, held.Lifetime);
                Before = held.Shown;
                Add(held.Shown.Removals);
                Add(unseen);
            }

            private void Add(IReadOnlyDictionary<string, IReadOnlyCollection<string>> found)
            {
                foreach (var pair in found)
                {
                    HashSet<string> removals;
                    if (!_found.TryGetValue(pair.Key, out removals))
                    {
                        removals = new HashSet<string>(StringComparer.Ordinal);
                        _found[pair.Key] = removals;
                    }

                    removals.UnionWith(pair.Value);
                }
            }

            public void Dispose()
            {
                Place.Dispose();
            }
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
            PairingMoment arrived,
            Rounds rounds)
        {
            var target = connection.Target;
            var invitation = connection.Invitation;
            var again = rounds.Earlier != null;

            // **Before a dialog shown again, what the request rests on is asked once more**. A lock
            // or a close of this database since the request arrived is answered as a lock is
            // answered wherever it meets a pairing, `503 locked`: the database the write would
            // find is not open, or not the database the operator answered about. An invitation
            // spent or run out since leaves nothing for an Approve on the dialog to do, and is
            // answered as a write finds it, `409 no invitation`, with no dialog raised.
            if (again && _held.Cleared(target.DatabaseId, rounds.Mark))
            {
                _log.Warn("pairing on invitation " + invitation.Id + " was not put to the operator again, because " +
                          "KeePass locked or closed the database after its write found the approval stale");
                context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                return null;
            }

            if (again && !Outstanding(invitation))
            {
                _log.Warn("pairing on invitation " + invitation.Id + " was not put to the operator again, because " +
                          "the invitation was spent or ran out after its write found the approval stale");
                context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                return null;
            }

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

            // Named as well as fingerprinted. The operator is being asked to let a stranger into
            // one of possibly several open databases, and sixteen hexadecimal characters is not
            // something anybody recognizes under time pressure.
            //
            // Asked for once each. A caller may send the same profile twice, and the dialog would
            // then show two identical rows: clearing one of them leaves the other checked, so a
            // profile the operator refused is granted anyway. The proof was computed over the list
            // exactly as it arrived, which is why this happens here and not before it was checked.
            var once = Once(asked);

            // Everything the dialog shows of the store in a single hold over a single reading,
            // taken before it is shown: the names on the list, and of each name asked for off it
            // the clients that regain it and the removals of it. The write compares an approval of
            // such a name with those removals, and a removal beyond them, which a synchronize can
            // bring while the operator reads the dialog or while the approval is held, or which
            // only a deletion record tells of and the reading cannot tie to the name, is a removal
            // the operator was not shown.
            //
            // With what this request's writes found with the store key: the reading cannot tie a
            // removal that only a deletion record tells of, of a name the file does not know
            // otherwise, to the name, and the dialog shown again marks the name removed and holds
            // that removal all the same, so that its approval is not stale over it again. The rows
            // marked removed are each name off the list whose removal the dialog holds, whether or
            // not a client holds it, and the dialog is shown again with what the operator approved
            // before where a write of this request found that approval stale.
            var reading = target.Asked(once) ?? AskedProfiles.None;

            // **A dialog shown again over a reading that does not serve this authority is not
            // raised**: such a reading holds no name and no removal, so the dialog would show every
            // row as new and its approval would be stale again, dialog after dialog until the time
            // is used up. The request ends as a blocked pairing ends, `503 busy` with nothing
            // written or spent. Which answer such a reading is given, a first dialog's included, is
            // the store's word, which is decided where a connection's is.
            if (again && !reading.Served)
            {
                _log.Warn("pairing on invitation " + invitation.Id + " was not put to the operator again, because " +
                          "the database's store no longer serves the authority it was asked under");
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            var shown = reading.With(rounds.Found);
            var request = new PairingRequest(
                target.DatabaseId,
                Named(target),
                invitation.Id,
                name,
                once,
                shown.Listed,
                connection.Peer,
                shown.Returning,
                lifetime,
                shown.Removed,
                rounds.Earlier,
                rounds.Before,
                rounds.Place);

            PairingApproval decision;
            try
            {
                var left = _options.DialogLeft(arrived, _clock);
                decision = _approval.Ask(request, _options.ApprovalTimeout, left)
                    ?? PairingApproval.TimedOut();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the pairing dialog failed", error);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return null;
            }

            switch (decision.Decision)
            {
                case PairingDecision.Approved:
                    break;

                case PairingDecision.TimedOut:
                    // Nothing is spent. An absent operator does not cost the invitee its invitation.
                    _log.Warn("pairing on invitation " + invitation.Id + " went unanswered");
                    _audit.PairingUnanswered(target.DatabaseId, invitation.Id);
                    context.Respond(504, HttpResponse.ErrorBody("unanswered"));
                    return null;

                case PairingDecision.Unavailable:
                    // Nobody was asked, so nothing was decided and the audit has nothing to say.
                    _log.Warn("pairing on invitation " + invitation.Id + " could not be put to anybody: " +
                              decision.Reason);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return null;

                case PairingDecision.Locked:
                    // Unanswered, as a timeout is, so nothing is spent; told as every connection to
                    // a locked database is told, since that is what this connection now is. A dialog
                    // its database's close took away is answered the same, its reason naming the
                    // close. Nothing is written down here: the screen recorded a dialog it took
                    // away as it took it, since at KeePass's exit nothing waits for this thread. A
                    // request the lock kept off the screen was put to nobody, and the audit records
                    // nothing of it, as of a request with no screen to ask.
                    _log.Warn("pairing on invitation " + invitation.Id + " was " + decision.Reason);
                    context.Respond(503, HttpResponse.ErrorBody(LockedTarget.Error));
                    return null;

                case PairingDecision.Refused:
                    _invitations.Burn(invitation.Id);
                    _log.Warn("pairing on invitation " + invitation.Id + " was refused, and it is spent");
                    _audit.PairingRefused(target.DatabaseId, invitation.Id);
                    context.Respond(403, HttpResponse.ErrorBody("refused"));
                    return null;

                default:
                    // An approval is the one answer that issues, so it is named rather than reached
                    // by falling past the others, which would issue a certificate for any decision
                    // added without a case of its own. An answer nothing here names is a defect
                    // rather than a yes, and nothing is spent, as for an answer nobody gave.
                    _incidents.Record(
                        "pairing on invitation " + invitation.Id + " was answered " + decision.Decision +
                        ", which this handler does not name, so it was refused for now", null);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return null;
            }

            // Held from here until it is written, so that KeePass being busy when the operator has
            // answered costs a retry rather than the answer. Once each here too, because what comes
            // back is whatever the screen answered and a repeated profile would reach the
            // certificate as a repeated claim. The lifetime is the operator's choice, which the
            // issuer holds to `Lifetime`'s bounds once more. What the list held of the approved
            // names is kept with it, because the write compares that under the writer's lock, and
            // it is the list the dialog was built from, which is what the operator answered: a
            // fresh read here took in a name put on the list while the dialog stood, which the
            // dialog had shown as "(new)", and the write then found nothing to compare. So are the
            // removals of each name the dialog showed off the list, for the same reason: a removal
            // read here could have arrived while the dialog stood, which the operator never saw.
            //
            // Held against where the book stood when the request arrived, which is before anything
            // was put to the operator, so that a lock or a close of this database between the
            // question and the answer, or between two dialogs, lets the answer go rather than
            // holding it for a database opened again since.
            var approved = Once(decision.Profiles);
            return _held.Hold(
                key,
                target.DatabaseId,
                approved,
                decision.Lifetime,
                shown,
                arrived.After(_options.ApprovalHeld),
                rounds.Mark);
        }

        /// <summary>
        /// Phase 5 for an approval the operator has given: waits a moment for a busy KeePass, then
        /// issues, writes and hands over, or holds it for the client's retry, and returns true once
        /// the client is answered.
        /// </summary>
        /// <returns>
        /// False where the write found the approval stale, which answers nothing: what it found is
        /// in <paramref name="rounds"/>, and the request puts the dialog to the operator again.
        /// </returns>
        private bool Write(IGateContext context, PairingConnection connection, string name, HeldApproval held, Rounds rounds)
        {
            var target = connection.Target;
            var invitation = connection.Invitation;

            // Asked again, because the answer before the dialog is as old as the dialog. Database
            // Maintenance opened while the operator was reading, or KeePass saving, is found here
            // while it can still be a `503` that spends nothing. A lock while the dialog stands
            // closes the dialog instead, `PAIRING.md` §308, as a close of its database does, and a
            // lock or a close after it takes the held approval away, which ends this wait as the
            // lock it is.
            var blocked = Settled(target, _options.ApprovalSettle, Blocked, () => _held.Holds(held));
            if (!_held.Holds(held))
            {
                Gone(context, invitation, held, "pairing on invitation " + invitation.Id);
                return true;
            }

            if (blocked != null)
            {
                Busy(context, held, "pairing on invitation " + invitation.Id, blocked, _log);
                return true;
            }

            return Issue(context, connection, name, held, rounds);
        }

        /// <summary>
        /// Answers an approval that is held no longer: `503 locked` when a lock or a close took it,
        /// and `503 busy` with nothing held when its time ran out or another request asking for
        /// the same thing took it, which raises the dialog again.
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

            log.Warn(what + (held.Taken
                ? " was approved, and another request asking for the same thing took the approval first"
                : " was approved, and was held until its time ran out without being written"));
            context.Respond(503, HttpResponse.ErrorBody("busy"));
        }

        /// <summary>
        /// Answers a pairing whose approval is held no longer, as the other overload does, unless
        /// its invitation is gone as well, which leaves nothing for a `503` to retry.
        /// </summary>
        /// <remarks>
        /// Two exchanges on one invitation can find the same approval held, and the write that
        /// lands first takes it and spends the invitation, so the other was told `503 busy` and its
        /// retry was turned away at the name. It is told `409 no invitation` now, as a request
        /// whose invitation went while the operator decided is. A lock or a close is still
        /// `503 locked`, since the invitation outlives both.
        /// </remarks>
        private void Gone(IGateContext context, Invitation invitation, HeldApproval held, string what)
        {
            if (!held.Cleared && !Outstanding(invitation))
            {
                _log.Warn(what + " was approved, and " +
                          (held.Taken
                              ? "another request asking for the same thing took the approval"
                              : "the approval was held until its time ran out") +
                          ", and the invitation is spent or has run out, so nothing can come of this request");
                context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                return;
            }

            Gone(context, held, what, _log);
        }

        /// <summary>Whether the invitation is still there to be spent.</summary>
        private bool Outstanding(Invitation invitation)
        {
            return _invitations.For(invitation.DatabaseId)
                .Any(live => string.Equals(live.Id, invitation.Id, StringComparison.Ordinal));
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
        /// hand anything over, returning true once the client is answered and false where the
        /// write found the approval stale, as <see cref="Write"/> has it.
        /// </summary>
        private bool Issue(IGateContext context, PairingConnection connection, string name, HeldApproval held, Rounds rounds)
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

            CertifiedKey identity;
            X509Certificate2 certificate;
            try
            {
                identity = _issuer.IssueClient(target.AuthorityKey, name, profiles, lifetime);
                certificate = new X509Certificate2(identity.CertificateDer);
            }
            catch (Exception error) when (Expected.OfIssuance(error))
            {
                // `OfIssuance` rather than `OfCryptography`, which does not name the
                // `InvalidOperationException` an expired authority is refused with. An authority
                // is issued for a hundred years and nothing refuses ahead of that, so this is where
                // a database that outlived its own runs out, as well as a key that would not sign;
                // left unnamed it escaped the handler to the catch-all on the connection thread,
                // which counts anything a handler throws as a defect in this plugin.
                //
                // **The approval goes with it**, `PAIRING.md` §311: the book holds what KeePass was
                // too busy to write, and nothing on this answer tells the client an approval is
                // held, so asking again asks the operator again.
                _held.Take(held);
                _log.Error("a client certificate could not be issued", error);
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return true;
            }

            using (certificate)
            {
                // A profile the operator approved that this database had never named is created by
                // their approval, and the writer does that inside the same marshaled write as the
                // record, off the profiles the record already carries.
                var record = AdmissionRecord.For(certificate, _clock.Now, profiles, target.DatabaseId);

                // The audit's line is the write's to write, once the record has landed and before
                // anything else hears of it, so nothing that runs after the write can lose it. The
                // audit keeps the log's rule below: the invitation and the key the record admits,
                // and nothing else.
                var databaseId = target.DatabaseId;
                var invitationId = invitation.Id;
                var issued = record.AdmissionKey;

                // **Asked under the writer's lock, once the database is known to be writable**,
                // which is the one moment none of it can move before the record relies on it: the
                // approval is still held and is used up, the database still holds what it was
                // given against, and the invitation is spent, last, so that nothing before the
                // record can fail once it is. Everything that can still answer busy is asked
                // before this, so a `503` never follows a spent invitation.
                string refused = null;
                var spent = false;
                IReadOnlyDictionary<string, IReadOnlyCollection<string>> unseen = null;
                Func<IWriteGround, string> condition = ground =>
                {
                    if (!_held.Take(held))
                    {
                        refused = held.Cleared ? "locked" : "expired";
                        return "the approval is no longer held";
                    }

                    // Every cause in a single pass, with the removals found with the store key
                    // beyond what the dialog held, which the next dialog of this request holds.
                    // The log names each cause.
                    string stale;
                    if (held.Stale(ground, null, out stale, out unseen) != HeldStaleness.None)
                    {
                        refused = "stale";
                        return stale;
                    }

                    if (!_invitations.Burn(invitationId))
                    {
                        refused = "no invitation";
                        return "the invitation was spent or ran out while the operator was deciding";
                    }

                    spent = true;
                    return null;
                };

                // **The connection's deadline goes for the write.** What follows is the writer's
                // own bounded waits and then a save that no number bounds, with the invitation spent
                // inside it, so a deadline still running could only lose the certificate of a record
                // that lands anyway. A write that finds the approval stale saves nothing, and the
                // dialog the request then puts up again takes the deadline up again, `Request`.
                ConnectionDeadline.Drop(context);

                AdmissionWrite written;
                try
                {
                    written = target.Admit(record, condition, () => _audit.Paired(databaseId, invitationId, issued))
                              ?? AdmissionWrite.Failed("nothing was reported");
                }
                catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfUserInterface(error))
                {
                    _log.Error("an admission record could not be written", error);
                    written = AdmissionWrite.Failed(error.Message);
                }

                if (!written.Ok && !spent)
                {
                    if (Unwritten(context, invitation, held, written, refused, "pairing on invitation " + invitationId))
                    {
                        return true;
                    }

                    // Stale: nothing answered, and what the write found goes to the next dialog.
                    rounds.Stale(held, unseen);
                    return false;
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
                    return true;
                }

                // Who and where, and not the profiles. Which profiles a client holds is in the
                // record already, and a log repeating them is a second copy of the thing worth
                // stealing, kept in the log's file, which nothing encrypts.
                _log.Info("paired " + certificate.Subject + " with " +
                          CertificateDigest.Shorten(target.DatabaseId));

                context.Respond(200, Bundle(target, identity, profiles));
                return true;
            }
        }

        /// <summary>
        /// Answers a pairing whose record was not written and whose invitation was not spent,
        /// returning true, or returns false with nothing answered where the database no longer
        /// holds what the operator approved.
        /// </summary>
        /// <remarks>
        /// **Busy keeps the approval held**: the write found KeePass busy before it took anything,
        /// so the same request asked again before the approval runs out is written without the
        /// dialog. **Anything the write's condition refused uses the approval up**, and what the
        /// client is told is what refused it: a lock or a close, or the invitation gone. **The
        /// database no longer holding what the operator approved is told nothing**: the same
        /// request puts the dialog to the operator again, and the log names each cause. **A store
        /// that holds the gate back where the write meets it, or holds several authorities with
        /// the authority the invitation was made under among them**, is answered its word as a
        /// connection is, `undecided` for the second, and lets the approval go and the invitation
        /// stay. **An authority retired since the operator answered**, which the write finds
        /// before the condition is asked, beside a single authority or none in its plan and beside
        /// several before it plans anything, uses the approval up as well and is told as an
        /// invitation that is gone, since the invitation was made under that authority and no
        /// decision of the operator's lets a retry under it be written; the invitation itself is
        /// not spent.
        /// </remarks>
        private bool Unwritten(
            IGateContext context,
            Invitation invitation,
            HeldApproval held,
            AdmissionWrite written,
            string refused,
            string what)
        {
            if (written.IsBusy)
            {
                if (_held.Holds(held))
                {
                    Busy(context, held, what, written.Reason, _log);
                    return true;
                }

                Gone(context, invitation, held, what);
                return true;
            }

            if (written.Word != null)
            {
                // The plugin's store holds the gate back where the write met it, or several
                // authorities stand, the authority the invitation was made under among them,
                // `STORE.md` §627: answered its word, as a connection is, `STORE.md` §656. The
                // approval goes and the invitation stays, since each word ends by an act of the
                // operator's, after which the operator is asked again.
                _held.Take(held);
                _log.Warn(what + " was approved and not written, since " + HeldBackTarget.Why(written.Word));
                context.Respond(503, HttpResponse.ErrorBody(written.Word));
                return true;
            }

            if (written.IsUnserved)
            {
                _held.Take(held);
                _log.Warn(what + " was approved under an authority that is no longer the authority served, " +
                          "so nothing was issued: " + written.Reason);
                context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                return true;
            }

            switch (refused)
            {
                case "locked":
                case "expired":
                    Gone(context, invitation, held, what);
                    return true;

                case "no invitation":
                    // Gone while the operator was reading the dialog or while the approval was
                    // held: somebody else spent it, and only one of them can win, or its time ran
                    // out, and `PAIRING.md` §306 has no way back from gone.
                    _log.Warn(what + " lost the race to spend it, or it ran out while the operator was deciding: " +
                              written.Reason);
                    context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                    return true;

                case "stale":
                    // What is known here, and no more: whether the dialog is put up again is the
                    // request's to find out, and its own line follows where it is not.
                    _log.Warn(what + " was approved and not written, since the database no longer holds what " +
                              "the dialog showed: " + written.Reason);
                    return false;
            }

            // A lock or a close of the database that let the approval go while the write waited
            // for its turn, which then found nothing to write to: answered as the lock it is, as
            // an approval a lock took away before its write is.
            if (held.Cleared)
            {
                Gone(context, invitation, held, what);
                return true;
            }

            // Nothing was touched and nothing spent: the database closed under the write, or the
            // write could not be attempted. The approval goes with it, and asking again asks the
            // operator again.
            _held.Take(held);
            _log.Warn(what + " was approved and not written, and nothing was spent: " + written.Reason);
            context.Respond(503, HttpResponse.ErrorBody("busy"));
            return true;
        }

        /// <summary>
        /// What the client is given: its own certificate and key, the authority to pin from now on,
        /// and the gate key every connection after this one is named from. A renewal answers with
        /// the same, so that a client stores either the same way.
        /// </summary>
        /// <param name="target">The database the client is admitted to.</param>
        /// <param name="identity">The certificate and key handed over, as they are stored.</param>
        /// <param name="profiles">The profiles the record holds.</param>
        /// <remarks>
        /// **The name and the dates are read off the identity itself**, so that what a client
        /// stores beside its identity can only ever describe that identity. They used to arrive as
        /// a second argument, Windows's reading of the same certificate, and nothing held the two
        /// to one certificate but both callers happening to build the second from the first.
        /// Windows's reading still, rather than Bouncy Castle's, because the subject is the one the
        /// record and the log carry, in the form Windows renders it.
        /// </remarks>
        internal static string Bundle(IGateTarget target, CertifiedKey identity, IReadOnlyList<string> profiles)
        {
            using (var certificate = new X509Certificate2(identity.CertificateDer))
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
                        json.WriteValue(CertificateBlob.Save(identity));
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
            return profiles.Distinct(StringComparer.Ordinal).ToList();
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
