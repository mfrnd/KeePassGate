using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        }

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
    public sealed class PairingHandler : IGateHandler
    {
        public const string Prefix = "/pair/";

        public const string HelloPath = "/pair/hello";

        public const string RequestPath = "/pair/request";

        private readonly IInvitations _invitations;
        private readonly IPairingApproval _approval;
        private readonly PairingOptions _options;
        private readonly AuthorityIssuer _issuer;
        private readonly Func<DateTimeOffset> _clock;
        private readonly IGateLog _log;

        public PairingHandler(IInvitations invitations, IPairingApproval approval, IGateLog log)
            : this(invitations, approval, new PairingOptions(), new AuthorityIssuer(), null, log)
        {
        }

        public PairingHandler(
            IInvitations invitations,
            IPairingApproval approval,
            PairingOptions options,
            AuthorityIssuer issuer,
            Func<DateTimeOffset> clock,
            IGateLog log)
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
        }

        /// <summary>Whether this path belongs to pairing at all.</summary>
        public static bool Owns(string path)
        {
            return path != null && path.StartsWith(Prefix, StringComparison.Ordinal);
        }

        public void Handle(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var connection = PairingConnection.Of(context);
            if (connection == null || context.Client.IsPaired)
            {
                // Not a pairing connection, or one that already has a certificate. Neither is told
                // that these routes exist, and the way to say nothing is to answer exactly what the
                // router would have answered about any other path: an admitted client gets the 404
                // an undeclared route gets, and anybody else gets the 401 that every route gets
                // before a certificate is proved. Answering 404 to both would have made this one
                // prefix the only one in the API that replies differently, which is the whole of
                // what an enumeration needs.
                Undeclared(context);
                return;
            }

            if (!string.Equals(context.Method, "POST", StringComparison.Ordinal))
            {
                context.Respond(405, HttpResponse.ErrorBody("method not allowed"));
                return;
            }

            if (string.Equals(context.Path, HelloPath, StringComparison.Ordinal))
            {
                Hello(context, connection);
                return;
            }

            if (string.Equals(context.Path, RequestPath, StringComparison.Ordinal))
            {
                Request(context, connection);
                return;
            }

            // A pairing connection asking for a path under this prefix that does not exist. It
            // already knows the prefix is here, so there is nothing left to give away.
            context.Respond(404, HttpResponse.ErrorBody("no such route"));
        }

        /// <summary>
        /// What the router says about a path nobody declared, said here so that these paths are
        /// indistinguishable from those for every caller that may not use them.
        /// </summary>
        private static void Undeclared(IGateContext context)
        {
            if (!context.Client.IsPaired)
            {
                context.Respond(401, HttpResponse.ErrorBody("unpaired"));
                return;
            }

            context.Respond(404, HttpResponse.ErrorBody("no such route"));
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
            var body = JsonBody.Read(context.Body);
            if (body == null)
            {
                context.Respond(400, HttpResponse.ErrorBody("malformed"));
                return;
            }

            var name = body.Text("name");
            var asked = body.Array("profiles") ?? Array.Empty<string>();
            var offered = Base64(body.Text("proof"));

            if (name == null || offered == null)
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
                    PairingProof.Asked(name, asked));

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

            Approve(context, connection, name, asked);
        }

        private void Approve(
            IGateContext context, PairingConnection connection, string name, IReadOnlyList<string> asked)
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
                return;
            }

            // Before the operator is disturbed as well, and for the same reason. An authority past
            // its ten years signs nothing that is valid, so a dialog here would spend somebody's
            // attention on a pairing that cannot end in a certificate.
            //
            // **Named on the wire rather than answered as an internal error.** An authority that
            // has run out is the ordinary state of an old database, and this client is one step
            // away from being handed that authority to pin, so its dates are nothing it is being
            // told for the first time. `CA.md` §219: there is no reissue, so what fixes it is
            // "Recreate authority" on the Certificate Authority page, which revokes every client
            // already admitted.
            //
            // **A `500` even though nothing is spent.** `PAIRING.md` §303 makes `503` the answer a
            // caller is invited to try again, and no retry issues anything until the operator has
            // acted, so an invitation that survives is worth less here than an answer that does not
            // send a client round a loop it cannot leave.
            if (Expired(target))
            {
                _log.Warn("pairing refused on invitation " + invitation.Id +
                          ": this database's authority has expired, so it can issue nothing that is" +
                          " valid until \"Recreate authority\" on the Certificate Authority page" +
                          " replaces it");
                context.Respond(500, HttpResponse.ErrorBody("expired authority"));
                return;
            }

            // And the host certificate, which runs out five years in where the authority runs ten.
            // Every connection a paired client makes afterwards checks it, so a pairing completed
            // over one saved a record the operator had just approved and handed over a bundle no
            // connection could ever use. `CA.md` §219 has no reissue for it either, so it is
            // refused the way an expired authority is and for the same reasons, under a word of
            // its own.
            if (HostExpired(target))
            {
                _log.Warn("pairing refused on invitation " + invitation.Id +
                          ": this database's host certificate has expired, so no client paired now" +
                          " could connect until \"Recreate authority\" on the Certificate Authority" +
                          " page replaces it");
                context.Respond(500, HttpResponse.ErrorBody("expired host"));
                return;
            }

            // Named as well as fingerprinted. The operator is being asked to let a stranger into
            // one of possibly several open databases, and sixteen hexadecimal characters is not
            // something anybody recognizes under time pressure.
            //
            // Asked for once each. A caller may send the same profile twice, and the dialog would
            // then show two identical rows: clearing one of them leaves the other checked, so a
            // profile the operator refused is granted anyway. The proof was computed over the list
            // exactly as it arrived, which is why this happens here and not before it was checked.
            var request = new PairingRequest(
                target.DatabaseId, Named(target), invitation.Id, name, Once(asked), connection.Peer);

            PairingApproval decision;
            try
            {
                decision = _approval.Ask(request, _options.ApprovalTimeout) ?? PairingApproval.TimedOut();
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                _log.Error("the pairing dialog failed", error);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return;
            }

            switch (decision.Decision)
            {
                case PairingDecision.TimedOut:
                    // Nothing is spent. An absent operator does not cost the invitee its invitation.
                    _log.Warn("pairing on invitation " + invitation.Id + " went unanswered");
                    context.Respond(504, HttpResponse.ErrorBody("unanswered"));
                    return;

                case PairingDecision.Unavailable:
                    _log.Warn("pairing on invitation " + invitation.Id + " could not be put to anybody: " +
                              decision.Reason);
                    context.Respond(503, HttpResponse.ErrorBody("busy"));
                    return;

                case PairingDecision.Refused:
                    _invitations.Burn(invitation.Id);
                    _log.Warn("pairing on invitation " + invitation.Id + " was refused, and it is spent");
                    context.Respond(403, HttpResponse.ErrorBody("refused"));
                    return;
            }

            // Asked again, because the answer before the dialog is as old as the dialog. KeePass
            // locking underneath it, or Database Maintenance opened while the operator was reading,
            // is found here while it can still be a `503` that spends nothing. Left to the write,
            // it was found after the burn and cost the invitation the approval was for.
            blocked = Blocked(target);
            if (blocked != null)
            {
                _log.Warn("pairing on invitation " + invitation.Id + " was approved and refused for now: " +
                          blocked);
                context.Respond(503, HttpResponse.ErrorBody("busy"));
                return;
            }

            // Once each here too, because what comes back is whatever the screen answered and a
            // repeated profile would reach the certificate as a repeated claim.
            Issue(context, connection, name, Once(decision.Profiles));
        }

        /// <summary>
        /// Phase 5. Issue, spend the invitation, land the record, and only then hand anything over.
        /// </summary>
        private void Issue(
            IGateContext context, PairingConnection connection, string name, IReadOnlyList<string> profiles)
        {
            var target = connection.Target;
            var invitation = connection.Invitation;

            X509Certificate2 certificate;
            try
            {
                certificate = _issuer.IssueClient(target.Authority, name, profiles);
            }
            catch (Exception error) when (Expected.OfIssuance(error))
            {
                // `OfIssuance` rather than `OfCryptography`, which does not name the
                // `InvalidOperationException` an expired authority is refused with. The check
                // before the dialog answers that case with the authority named, and this is the
                // same refusal arriving from a clock that moved in between; left unnamed it
                // escaped the handler to the catch-all on the connection thread, which counts
                // anything a handler throws as a defect in this plugin.
                _log.Error("a client certificate could not be issued", error);
                context.Respond(500, HttpResponse.ErrorBody("internal error"));
                return;
            }

            try
            {
                if (!_invitations.Burn(invitation.Id))
                {
                    // Gone while the operator was reading the dialog. Somebody else spent it, and only
                    // one of them can win, or its time ran out, and an answer given after that is too
                    // late: `PAIRING.md` §306 has no way back from gone. The book's own line says which.
                    _log.Warn("pairing on invitation " + invitation.Id +
                              " lost the race to spend it, or it ran out while the operator was deciding");
                    context.Respond(409, HttpResponse.ErrorBody("no invitation"));
                    return;
                }

                var record = AdmissionRecord.For(certificate, _clock(), profiles);

                AdmissionWrite written;
                try
                {
                    written = target.Admit(record) ?? AdmissionWrite.Failed("nothing was reported");
                }
                catch (Exception error) when (Expected.OfCustomData(error) || Expected.OfUserInterface(error))
                {
                    _log.Error("an admission record could not be written", error);
                    written = AdmissionWrite.Failed(error.Message);
                }

                if (!written.Ok)
                {
                    // The invitation is gone and the certificate is not handed over, so nothing was
                    // issued that the database does not know about. The operator invites again.
                    //
                    // **Never 503 here, however transient the reason was.** The invitation was
                    // spent on the line above, so a client told to try again would be turned away
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
                // stealing, kept in whatever file the log is sent to and never rotated.
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
        /// What the client is given: its own certificate and key, the authority to pin from now on,
        /// and the gate key every connection after this one is named from.
        /// </summary>
        private static string Bundle(
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
        /// Whether the authority behind this database has run out, in which case pairing has
        /// nothing valid to hand over however the operator answers.
        /// </summary>
        /// <remarks>
        /// The certificate is already in memory, so this reads a date rather than the database and
        /// cannot fail the way <see cref="Blocked"/> can. `CertificateValidity` is asked instead of
        /// comparing here, so the gate, the tab and the issuer all draw the boundary in one place.
        /// </remarks>
        private bool Expired(IGateTarget target)
        {
            return CertificateValidity.HasExpired(target.Authority, _clock());
        }

        /// <summary>
        /// Whether the host certificate this database answers with has run out, which leaves a
        /// pairing nothing a client could use however the operator answers.
        /// </summary>
        /// <remarks>
        /// Read off the bytes this connection was served with, which is what every later connection
        /// will be served with too.
        /// </remarks>
        private bool HostExpired(IGateTarget target)
        {
            using (var host = new X509Certificate2(target.Host.CertificateDer))
            {
                return CertificateValidity.HasExpired(host, _clock());
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
