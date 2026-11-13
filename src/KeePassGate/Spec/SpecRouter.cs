using System;
using System.Collections.Generic;
using System.Net;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Transport;
using KeePassGate.Vault;

namespace KeePassGate.Spec
{
    /// <summary>What an operation does, once the router has decided it is the one.</summary>
    public delegate void OperationHandler(IGateContext context);

    /// <summary>
    /// Whether this connection may reach this operation at all. False when it has already answered.
    /// </summary>
    /// <remarks>
    /// For the precondition only an operation's own subsystem can evaluate. Pairing has one: its
    /// routes are open to a connection that arrived on an invitation and to nothing else, which is
    /// not a fact about identity and not a fact the document can state.
    ///
    /// It runs **before the body is checked**, and that ordering is the whole reason it exists.
    /// Checking a body first would let a caller that may not reach a route learn whether its body
    /// would have been accepted, which is a difference the 404 there exists to deny.
    /// </remarks>
    public delegate bool OperationGuard(IGateContext context);

    /// <summary>
    /// Which group or entry a request is about, or null when it is about nothing that exists.
    /// </summary>
    /// <remarks>
    /// **The router authorizes, not the handler.** An operation that touches the database declares
    /// a verb, and the only thing it can be asked about is a node, so the piece a handler has to
    /// supply is which node. Everything after that is the same for every route: intersect the
    /// admission record with the rules, write the decision down, and refuse if it went against.
    ///
    /// A handler that asked the question itself would be a handler that could forget to, and a
    /// route added later would then be a route with no rules on it and nothing to say so. This way
    /// an operation with a verb and no selector stops the plugin loading.
    ///
    /// It runs on the connection's thread, reading the database through the vault, like everything
    /// else that only reads, with KeePass's thread held still for the read. So it meets a thread
    /// that cannot be held exactly as a handler does, and `VaultUnsettled` out of one is answered
    /// `503 busy` by the same catch: the selector runs inside it rather than a hop ahead of it.
    /// </remarks>
    public delegate IAclNode NodeSelector(IGateContext context);

    /// <summary>
    /// The document, dispatching. Handlers bind to `operationId` and never to a path.
    /// </summary>
    /// <remarks>
    /// **The startup assertion is the point of the whole arrangement.** Every declared operation
    /// must have a handler and every handler must answer to a declared operation, checked when this
    /// is constructed, which happens as the plugin loads. So the two cannot disagree: a route added
    /// to the document with nothing behind it, or a handler for something the document does not
    /// declare, stops the plugin loading rather than becoming a 404 discovered by whoever tried it.
    ///
    /// **The order of refusals is deliberate.** A locked database is refused before anything
    /// else, because a locked database has nothing to answer with and the lock is all it may
    /// answer. Then the document decides which operation this is, so an undeclared path is refused
    /// before anything has parsed a body. The transport has read the bytes by then, within
    /// `GateOptions.MaxBodyBytes`, because it reads a request whole before routing it. Then
    /// identity, in one place, so a route added later is denied by default rather than by somebody
    /// remembering to guard it. Then a query name or a body the operation does not declare,
    /// answered as the undeclared target the request then is. Then the operation's own guard, for
    /// a precondition the document cannot state. Then authorization, the admission record
    /// intersected with the rules. Then the body's media type, and then the body against the
    /// declared schema, so nothing is reported about a body on a route the caller may not reach.
    /// A method none of the path's operations has goes the same way as far as it can: a caller
    /// that may not be told the path is there, or a query name or a body nothing on the path
    /// declares, is answered as an undeclared target, and anything else is a `405`.
    ///
    /// **HEAD follows GET.** It is declared beside every `get` and bound to the same handler, and
    /// served by running that handler and discarding what it wrote. RFC 9110 section 9.3.2 asks for the same header
    /// fields as GET and permits omitting the ones determined while generating the content, which
    /// is why the answer carries no length. https://www.rfc-editor.org/rfc/rfc9110#section-9.3.2
    /// `HttpResponse.Write` is where that is done, because it is the one place that decides both
    /// the length and whether any bytes follow, and every refusal in this class goes through it.
    /// </remarks>
    public sealed class SpecRouter : IGateHandler
    {
        /// <summary>
        /// What a caller is told about a node it may not reach, and about one that is not there.
        /// </summary>
        /// <remarks>
        /// **The same thing, deliberately.** Telling them apart would let a client that may reach
        /// nothing map the database by asking, one identifier at a time, which is exactly what
        /// `list` exists to be able to withhold. The operator is told the difference, in the audit,
        /// where a decision says which it was and why.
        /// </remarks>
        public const string UnreachableError = "no such route";

        /// <summary>Where the router leaves what a path template captured.</summary>
        public const string ParametersItem = "kpg.parameters";

        /// <summary>Where the router leaves the protected-scope question, ready to be recorded.</summary>
        public const string ReachItem = "kpg.reach";

        /// <summary>
        /// Where the router leaves what records a protected value a read's view withheld.
        /// </summary>
        public const string WithheldItem = "kpg.withheld";

        /// <summary>
        /// Where the router leaves a <see cref="ServedAfter"/> for a request it decides inside
        /// a hold: what takes the part of an entry's answer that is made of the entry's values,
        /// to be run once the hold has ended.
        /// </summary>
        public const string AfterItem = "kpg.after";

        /// <summary>
        /// Where the router leaves what `If-Match` asks, for an operation that declares it and a
        /// request that carried one: a <see cref="VaultPrecondition"/>, absent otherwise.
        /// </summary>
        public const string PreconditionItem = "kpg.precondition";

        /// <summary>
        /// Where the router leaves what each profile is granted at the node, for an operation
        /// declaring `any`, which is decided in the same walk.
        /// </summary>
        public const string GrantsItem = "kpg.grants";

        /// <summary>
        /// Where the router leaves a <see cref="DecidedInside"/> for an operation whose verb
        /// changes the database and whose request it did not refuse: the question the change
        /// asks again from inside its own turn, which is the answer acted on.
        /// </summary>
        public const string InsideItem = "kpg.inside";

        private readonly SpecDocument _document;
        private readonly Dictionary<string, OperationHandler> _handlers;
        private readonly Dictionary<string, OperationGuard> _guards;
        private readonly Dictionary<string, NodeSelector> _nodes;
        private readonly AccessCheck _access;

        public SpecRouter(SpecDocument document, IReadOnlyDictionary<string, OperationHandler> handlers)
            : this(document, handlers, null)
        {
        }

        public SpecRouter(
            SpecDocument document,
            IReadOnlyDictionary<string, OperationHandler> handlers,
            IReadOnlyDictionary<string, OperationGuard> guards)
            : this(document, handlers, guards, null, null)
        {
        }

        /// <summary>
        /// Binds each handler, guard and node to its operation, and throws at the first one that
        /// does not fit the document, which is as the plugin loads.
        /// </summary>
        /// <param name="nodes">
        /// Which node each operation is about, for the operations that touch the database. One is
        /// required for every operation declaring a grantable verb, and refused for one declaring
        /// `none`, both asserted here as the plugin loads.
        /// </param>
        /// <param name="access">
        /// The record and the rules, combined and recorded. Absent, no operation may declare a
        /// verb: a router that could not authorize would otherwise serve as though everything were
        /// permitted.
        /// </param>
        public SpecRouter(
            SpecDocument document,
            IReadOnlyDictionary<string, OperationHandler> handlers,
            IReadOnlyDictionary<string, OperationGuard> guards,
            IReadOnlyDictionary<string, NodeSelector> nodes,
            AccessCheck access)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (handlers == null)
            {
                throw new ArgumentNullException(nameof(handlers));
            }

            _document = document;
            _handlers = new Dictionary<string, OperationHandler>(StringComparer.Ordinal);
            _guards = new Dictionary<string, OperationGuard>(StringComparer.Ordinal);

            foreach (var pair in handlers)
            {
                _handlers[pair.Key] = Bound(pair.Key, pair.Value, "handler");
            }

            _nodes = new Dictionary<string, NodeSelector>(StringComparer.Ordinal);
            _access = access;

            if (guards != null)
            {
                foreach (var pair in guards)
                {
                    _guards[pair.Key] = Bound(pair.Key, pair.Value, "guard");
                }
            }

            if (nodes != null)
            {
                foreach (var pair in nodes)
                {
                    _nodes[pair.Key] = Bound(pair.Key, pair.Value, "node selector");
                }
            }

            Assert();
            AssertAuthorized();
        }

        /// <summary>The delegate bound under a name, refusing a binding to nothing.</summary>
        /// <remarks>
        /// A name bound to null counted as bound: every assertion below asks which names are
        /// bound, and for a pair whether the two delegates are the same, so a null handler, guard
        /// or selector loaded, and its first request threw where the router calls it and was
        /// answered `500` as a defect. That is the operation with nothing behind it, found on a
        /// connection, which the assertions exist to find as the plugin loads.
        /// </remarks>
        private static T Bound<T>(string name, T bound, string kind) where T : class
        {
            if (bound == null)
            {
                throw new InvalidOperationException(
                    name + " is bound to no " + kind + ", so its first request would be a defect" +
                    " rather than an answer.");
            }

            return bound;
        }

        /// <summary>
        /// The document and the handlers, held to each other.
        /// </summary>
        /// <remarks>
        /// Private, because asking it means having a router: the startup check is the only caller
        /// there is, and a second one would be a second place deciding what binding means.
        /// </remarks>
        private static void Assert(SpecDocument document, IEnumerable<string> handlers)
        {
            var bound = new HashSet<string>(handlers, StringComparer.Ordinal);
            var declared = new HashSet<string>(StringComparer.Ordinal);

            foreach (var operation in document.Operations)
            {
                declared.Add(operation.OperationId);

                if (!bound.Contains(operation.OperationId))
                {
                    throw new InvalidOperationException(
                        "The routing table declares " + operation.OperationId + " and nothing handles it.");
                }
            }

            foreach (var name in bound)
            {
                if (!declared.Contains(name))
                {
                    throw new InvalidOperationException(
                        name + " is handled and the routing table does not declare it, so nothing can reach it.");
                }
            }
        }

        /// <summary>Holds every `head` to the handler its own `get` reaches, and to its guard.</summary>
        /// <remarks>
        /// The document requires the two to declare the same thing; this requires them to be
        /// the same thing. RFC 9110 section 9.3.2 says a `head` is identical to its `get`
        /// except that no content is sent, and one handler is how that is true by
        /// construction rather than by two implementations agreeing. That is only worth
        /// anything if nobody can bind the two apart.
        ///
        /// **The guard as well, because it is bound by name too.** It is looked up under the
        /// operation that matched, so a guard on a `get` and none on its `head` let the `head`
        /// run the shared handler on a connection the `get` would have refused before anything
        /// was looked at, which is the oracle a pair is held together to deny.
        /// </remarks>
        private void AssertTwinned()
        {
            foreach (var operation in _document.Operations)
            {
                if (!string.Equals(operation.Method, "HEAD", StringComparison.Ordinal))
                {
                    continue;
                }

                var get = _document.Twin(operation);
                if (get == null)
                {
                    continue;
                }

                if (_handlers[operation.OperationId] != _handlers[get.OperationId])
                {
                    throw new InvalidOperationException(
                        operation.OperationId + " and " + get.OperationId +
                        " reach different handlers, so the head could answer something its own" +
                        " get would not.");
                }

                OperationGuard mine, its;
                _guards.TryGetValue(operation.OperationId, out mine);
                _guards.TryGetValue(get.OperationId, out its);

                if (mine != its)
                {
                    throw new InvalidOperationException(
                        operation.OperationId + " and " + get.OperationId +
                        " are guarded differently, so the head could answer on a connection its" +
                        " own get would refuse.");
                }
            }
        }

        /// <summary>
        /// Every operation that touches the database can be authorized, and nothing else pretends
        /// to be.
        /// </summary>
        /// <remarks>
        /// The same argument as the verb itself, one level along. A verb that nothing consults
        /// would be an operation nobody had thought about the authorization of, and it would serve
        /// exactly as though everything were permitted. So it does not load.
        /// </remarks>
        private void AssertAuthorized()
        {
            foreach (var operation in _document.Operations)
            {
                var governed = AclVerbs.IsGoverned(operation.Verb);

                if (governed && _access == null)
                {
                    throw new InvalidOperationException(
                        operation.OperationId + " declares " + AclVerbs.Write(operation.Verb) +
                        " and this router has nothing to authorize with.");
                }

                if (governed && !_nodes.ContainsKey(operation.OperationId))
                {
                    throw new InvalidOperationException(
                        operation.OperationId + " declares " + AclVerbs.Write(operation.Verb) +
                        " and nothing says which node it is about, so no rule could be applied to it.");
                }

                if (!governed && _nodes.ContainsKey(operation.OperationId))
                {
                    // A selector nothing consults reads as authorization that is happening and is
                    // not, which is worse than none.
                    throw new InvalidOperationException(
                        operation.OperationId + " declares " + AclVerbs.Write(operation.Verb) +
                        " and names a node, which nothing would ever ask about.");
                }
            }

            foreach (var name in _nodes.Keys)
            {
                if (!_handlers.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        name + " names a node and the routing table does not declare it.");
                }
            }

            // Last, so a build that named a node for nothing is told about that before it is
            // told about the half of a pair it left behind.
            foreach (var operation in _document.Operations)
            {
                if (!string.Equals(operation.Method, "HEAD", StringComparison.Ordinal))
                {
                    continue;
                }

                var get = _document.Twin(operation);
                if (get == null)
                {
                    continue;
                }

                NodeSelector mine, its;
                _nodes.TryGetValue(operation.OperationId, out mine);
                _nodes.TryGetValue(get.OperationId, out its);

                if (mine != its)
                {
                    // A decision is made of three things. The document holds the verb and the
                    // switch of a pair together and `Assert` holds its handler; what is left is
                    // the node. A head asking about another one would put this one's rule to
                    // that one's existence.
                    throw new InvalidOperationException(
                        operation.OperationId + " asks about a different node from " +
                        get.OperationId + ", so the head would be authorized against something" +
                        " its own get is not.");
                }
            }
        }

        /// <summary>Writes the lines a request's decisions came to.</summary>
        /// <remarks>
        /// Only an operation the rules govern gathers lines, and no router is built holding such
        /// an operation and nothing to authorize with, which <see cref="AssertAuthorized"/>
        /// refuses. So the access check is there whenever there is a line to write.
        /// </remarks>
        private void Record(DecisionLines lines)
        {
            _access.Write(lines);
        }

        public void Handle(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var wanted = context.Method;

            // **A connection that arrived on an invitation reaches the pairing routes and nothing
            // else**, which is what `PAIRING.md` §302 says the pairing path is. Nothing enforced
            // it: a client the database already admits can compute the name from an invitation it
            // also holds, and its certificate is judged the same way there as anywhere, so every
            // ordinary route answered on a connection whose deadline is the pairing budget. It
            // gains no permission by that, because the same certificate reaches the same routes on
            // its own name, but it does get six minutes on the thread where the ordinary budget
            // gives ninety seconds, and the pairing budget is that long only because a person is
            // being waited for.
            //
            // Asked here rather than by a guard, because a guard says what one operation needs and
            // this says what a whole connection may ask for. It is the mirror of the identity rule
            // below and is written beside it for that reason: an operation that needs a certificate
            // is not reachable by a caller without one, nor by a caller that named a token instead
            // of a database.
            var admitted = context.Client.IsPaired && PairingConnection.Of(context) == null;

            SpecOperation operation = null;
            IReadOnlyDictionary<string, string> captured = null;
            var pathExists = false;
            var pathIsPublic = false;

            // What the path does serve, gathered while looking for what was asked for. The loop
            // below stops at a match, so this is only ever complete when there was none. That is
            // the one case that has to say what the methods are, and the query names any of them
            // declares, and whether any of them takes a body, are gathered for the same case.
            var served = new List<string>();
            var named = new HashSet<string>(StringComparer.Ordinal);
            var bodied = false;

            foreach (var candidate in _document.Operations)
            {
                // A segment its parameter's schema refuses, a uuid of the wrong length, is a path
                // the document does not declare, and it is answered as one: before a lookup
                // could be asked about it, and without a `405` naming what the path serves.
                IReadOnlyDictionary<string, string> found;
                if (!candidate.Path.TryMatch(context.Path, out found) || !candidate.Admits(found))
                {
                    continue;
                }

                pathExists = true;
                pathIsPublic = pathIsPublic || !candidate.NeedsIdentity;
                served.Add(candidate.Method);
                named.UnionWith(candidate.Query);
                bodied = bodied || candidate.Body != null;

                if (string.Equals(candidate.Method, wanted, StringComparison.Ordinal))
                {
                    operation = candidate;
                    captured = found;
                    break;
                }
            }

            if (operation == null)
            {
                // **A caller with no certificate is told the same thing everywhere.** Answering 404
                // here and 401 on the routes that exist would let anybody who reaches the port map
                // the API by asking. So an undeclared path answers exactly what a declared one
                // answers, and only a client the database already admitted is told the difference.
                //
                // The exception is a path whose operations need no certificate, because those are
                // declared in a document that ships inside the plugin and so are public already.
                if (!pathExists)
                {
                    Undeclared.Refuse(context);
                    return;
                }

                if (!pathIsPublic && !admitted)
                {
                    Undeclared.Refuse(context);
                    return;
                }

                // A query name that nothing on this path declares makes the target one the
                // document does not declare, whichever method asked, and `openapi.yaml` §507
                // answers that exactly as an undeclared path. Asked only by `Asked` once a method
                // had matched, `DELETE /gate/status?x=1` was a `405` naming what `/gate/status`
                // serves where `GET /gate/status?x=1` and `DELETE /nothing?x=1` were both `404`.
                if (!Declared(context, named))
                {
                    return;
                }

                // The body is the other half of that target, as `Bodied` holds once a method has
                // matched. Asked only there, `DELETE /gate/status` carrying `{"x":"1"}` was a `405`
                // naming what the path serves, where the same words sent as a query were `404`.
                if (!bodied && !string.IsNullOrEmpty(context.Body))
                {
                    Undeclared.Refuse(context);
                    return;
                }

                // RFC 9110 section 15.5.6: a 405 lists the methods the target does serve. Read
                // off the document rather than assembled by a handler, so it says what this
                // server actually routes and cannot fall out of step with it. In the document's
                // own order, which is the order a reader of it meets them in.
                //
                // Nothing is given away by it: this is reached only for a path that exists and
                // is either public or asked for by a client the database already admitted, and
                // the document that lists these ships inside the plugin.
                var allowed = new WebHeaderCollection();
                allowed.Add("Allow", string.Join(", ", served.ToArray()));

                context.Respond(405, HttpResponse.ErrorBody("method not allowed"), allowed);
                return;
            }

            if (operation.NeedsIdentity && !admitted)
            {
                // Written down when the certificate is one this database's authority issued, which
                // is a revoked or expired client still trying and exactly what an operator opens the
                // audit to see, or an admitted client asking on an invitation's connection for what
                // its own name gives it. Here and not at the two refusals above: those name no
                // operation, so nobody refused there is written down, an admitted client included.
                if (_access != null && context.Client.IsPaired)
                {
                    _access.RefuseOnInvitation(context.Client, operation.OperationId);
                }
                else if (_access != null)
                {
                    _access.Refuse(context.Client, operation.OperationId);
                }

                Undeclared.Refuse(context);
                return;
            }

            if (!Asked(context, operation))
            {
                return;
            }

            if (!Bodied(context, operation))
            {
                return;
            }

            // Before the guard, not after. A guard on a parameterized route is exactly the
            // guard that needs what the path captured, and the obvious way to read them is
            // `context.Items[ParametersItem]`, which throws `KeyNotFoundException` on a plain
            // dictionary, so the first one written would have answered 500 rather than the
            // refusal it was written to give. They cost a caller nothing that reaching this
            // line did not already cost it: the path has already matched.
            context.Items[ParametersItem] = captured;

            // **Everything the route does, not the handler alone.** The router reads the
            // database itself, before dispatch: the node selector an operation declares asks
            // the vault which group or entry a request is about, and that read meets a tree
            // moving under it exactly as a handler's does. Catching around the handler alone
            // answered one moving database two ways, and the answer on the earlier hop was the
            // wrong one.
            //
            // **The decisions are kept and written last.** A read is decided inside its
            // hold, and a change is decided twice, here and again inside its own turn
            // on KeePass's thread. A line written in either would put the audit's pacing on the
            // thread the operator is typing into. So every line the request's decisions come to
            // gathers in one place, the later decision in place of the earlier, and is written
            // once nothing of the request is left to decide: after the handler, before the answer
            // goes out, on the caller's own connection.
            var lines = AclVerbs.IsGoverned(operation.Verb) ? new DecisionLines() : null;

            try
            {
                OperationGuard guard;
                if (_guards.TryGetValue(operation.OperationId, out guard) && !guard(context))
                {
                    return;
                }

                if (lines != null && !AclVerbs.Changes(operation.Verb))
                {
                    // A read, a listing, or a question about what the caller may do at a node:
                    // nothing about it is decided out here. `Held` has the reason.
                    Held(context, operation, lines);
                    return;
                }

                if (!Authorized(context, operation, lines))
                {
                    return;
                }

                Served(context, operation);
            }
            catch (VaultUnsettled)
            {
                // KeePass's thread could not be held still for a read, or not for long enough.
                // That is KeePass busy, not a fault here, and letting it travel makes it one: the
                // catch-all around this counts anything escaping the router as a defect in this
                // plugin, answers 500, and puts an incident on the plugin's own window for it.
                context.Respond(503, HttpResponse.ErrorBody("busy"));
            }
            finally
            {
                if (lines != null)
                {
                    Record(lines);
                }
            }
        }

        /// <summary>
        /// A request that changes nothing, decided while KeePass's thread is held still once
        /// and answered with the database as it stood in that hold.
        /// </summary>
        /// <remarks>
        /// **One hold for the client, the rules and the answer.** The client's record and the
        /// list of profiles are read again, the node is found, the rules are asked and the
        /// handler reads what it serves, all of the database as it stands at one moment. Each of
        /// those was a hold of its own, so an operator's edit could land between any two of them:
        /// a rule taken away after the router asked still let the read through, and what the
        /// read served was the database as it stood after the edit. Nothing about a read is
        /// decided before this hold, because an answer from before it is an answer about another
        /// moment, which is what a change's first answer is and why a change asks again.
        ///
        /// **An entry's values are read once the hold has ended, and are still of that
        /// moment.** What the handler takes of an entry inside the hold is the caller's view,
        /// which keeps the objects the entry's values were held in and reads none of them. The
        /// handler leaves the rest with <see cref="AfterItem"/>: measuring the values, reading
        /// them, taking the tag and asking the condition of it. That part grows with the entry
        /// and no write is refused for an entry's size, so it runs on the connection's thread
        /// with KeePass's own free, <see cref="ServedAfter"/>.
        ///
        /// **The answer is handed over once the hold has ended**, for the reason
        /// <see cref="DecidedContext"/> gives, and a hold that could not be taken or did not
        /// last leaves the request to the `503 busy` every unsettled read gets. The lines its
        /// decisions came to are written all the same: they are what was decided, and a change
        /// that queued and was never made leaves its line too.
        ///
        /// **Nothing in here waits**, which <see cref="Ui.IReadHold"/> asks of everything it
        /// holds KeePass's thread for: the audit's lines are kept and written once the hold is
        /// over, and the answer is written to the connection after that.
        /// </remarks>
        private void Held(IGateContext context, SpecOperation operation, DecisionLines lines)
        {
            // With no vault on the request there is no thread to hold, and nothing of a database
            // for the steps to read: they run where they are.
            var vault = ServedDatabase.Of(context);
            var read = vault == null
                ? Read(context, operation, lines)
                : vault.Held(() => Read(context, operation, lines));

            read.Deliver();
        }

        /// <summary>
        /// Everything a held request does, against the client as the database holds it now, with
        /// the answer kept on what is returned.
        /// </summary>
        private DecidedContext Read(IGateContext context, SpecOperation operation, DecisionLines lines)
        {
            var client = ClientCredentials.Again(
                context.Client, ServedDatabase.TargetOf(context), ServedDatabase.LogOf(context));
            var read = new DecidedContext(context, client, true);
            read.Items[AfterItem] = (ServedAfter)read.After;

            if (!client.IsPaired)
            {
                TurnAway(read, operation, lines);
                return read;
            }

            if (Authorized(read, operation, lines))
            {
                Served(read, operation);
            }

            return read;
        }

        /// <summary>
        /// Answers a client the database stopped admitting between its connection and the moment
        /// its request is decided, as its next connection is answered.
        /// </summary>
        /// <remarks>
        /// A database that locked, or that nobody is served from for now, is the `503` the
        /// listener gives before routing, in the same word, and leaves no line, because nothing
        /// was decided. A certificate whose record has gone is refused as a certificate with no
        /// record is refused on arrival: the line a refused certificate gets, and the `401` every
        /// route gives a caller the database does not admit.
        ///
        /// A read comes here inside its hold and a change inside its turn, so the two are turned
        /// away alike.
        /// </remarks>
        private void TurnAway(IGateContext decided, SpecOperation operation, DecisionLines lines)
        {
            if (decided.Client.UnavailableError != null)
            {
                decided.Respond(503, HttpResponse.ErrorBody(decided.Client.UnavailableError));
                return;
            }

            _access.Refuse(decided.Client, operation.OperationId, lines);
            Undeclared.Refuse(decided);
        }

        /// <summary>The body, the condition and the handler, for a request the rules let through.</summary>
        private void Served(IGateContext context, SpecOperation operation)
        {
            if (operation.Body != null && !Typed(context))
            {
                return;
            }

            if (operation.Body != null && !Checked(context, operation))
            {
                return;
            }

            // After everything that refuses, so that a condition reveals nothing about a node the
            // caller may not reach: a refused node is the same `404` whatever the tag. The
            // handler asks it of the node, inside the read's hold or under the writer's lock.
            if (operation.TakesIfMatch)
            {
                var precondition = VaultPrecondition.Read(context.GetHeader(SpecDocument.IfMatchHeader));
                if (precondition != null)
                {
                    context.Items[PreconditionItem] = precondition;
                }
            }

            // The handler runs as it would for the GET, and what it wrote is dropped by the
            // writer rather than here: a body blanked at this layer still gets measured, which
            // is how `Content-Length: 0` went out where a GET would have sent forty-five.
            _handlers[operation.OperationId](context);
        }

        /// <summary>Whether every query parameter sent is one the operation declares.</summary>
        /// <remarks>
        /// **A request target is more than its path.** The transport splits the query off and
        /// parses it, and nothing here ever read the result, so `GET /entries?group=x` was served
        /// as `GET /entries`: the whole database came back and the caller was told nothing about
        /// the word it sent. `API.md` §407 is where scoping by query was cut, and it names the
        /// list routes as where filtering and paging would arrive, so this asks the document
        /// rather than refusing every query for all time.
        ///
        /// Refused as an undeclared target, which is what it is, so it answers exactly what an
        /// undeclared path answers: `404` to a client the database admitted, and to everybody
        /// else the same `401` every path there is gives a caller with no certificate. No new
        /// status arrives with this, and nothing has to be declared per operation to describe it.
        /// </remarks>
        private static bool Asked(IGateContext context, SpecOperation operation)
        {
            var sent = context.Query;
            if (sent == null || sent.Count == 0)
            {
                return true;
            }

            foreach (var name in sent.Keys)
            {
                if (!operation.DeclaresQuery(name))
                {
                    Undeclared.Refuse(context);
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether a body was sent only to an operation that declares one.</summary>
        /// <remarks>
        /// **The other half of the request `Asked` is about.** A `get`, a `head` or a `delete`
        /// declares no body, and the transport reads one whole before routing whatever the method,
        /// so a body sent with one was read and then ignored: `GET /entries` with `{"group":"x"}`
        /// in it answered every entry, and `DELETE` of a group with `{"recursive":"true"}` was a
        /// plain delete, with nothing to say the words went unread. That is what `Asked` refuses a
        /// query for, and this is refused the same way, as the undeclared target the request is,
        /// so no new status arrives with it and nothing has to be declared per operation to say
        /// so. Asked where the query is, before the guard and the rules, because what a caller is
        /// told here is what it is told about any path it may not have.
        /// </remarks>
        private static bool Bodied(IGateContext context, SpecOperation operation)
        {
            if (operation.Body != null || string.IsNullOrEmpty(context.Body))
            {
                return true;
            }

            Undeclared.Refuse(context);
            return false;
        }

        /// <summary>
        /// The same question for a path none of whose operations was asked for, of every name any
        /// of them declares.
        /// </summary>
        /// <remarks>
        /// A name one of them declares is a target the document does declare, at another method, so
        /// that is still a `405`. A name none of them declares is no target at all.
        /// </remarks>
        private static bool Declared(IGateContext context, ISet<string> named)
        {
            var sent = context.Query;
            if (sent == null || sent.Count == 0)
            {
                return true;
            }

            foreach (var name in sent.Keys)
            {
                if (!named.Contains(name))
                {
                    Undeclared.Refuse(context);
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether the body is the media type this API speaks, or claims nothing.</summary>
        /// <remarks>
        /// `SpecDocument.MediaType` is already what the *document* is held to at startup: an
        /// operation declaring a body under any other type stops the plugin loading. This is
        /// the same rule applied to the other side of the wire, which nothing applied at all.
        ///
        /// A body with no type declared is taken as the one type there is, because a caller
        /// that claimed nothing has not claimed something else. And this is asked after the
        /// guard for the reason everything here is ordered that way: a caller that may not
        /// reach a route learns nothing about what that route would have accepted.
        /// </remarks>
        private static bool Typed(IGateContext context)
        {
            var declared = context.GetHeader("Content-Type");
            if (string.IsNullOrEmpty(declared))
            {
                return true;
            }

            if (string.Equals(
                    declared.Split(';')[0].Trim(),
                    SpecDocument.MediaType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            context.Respond(415, HttpResponse.ErrorBody("media type"));
            return false;
        }

        /// <summary>
        /// The rules, before the body.
        /// </summary>
        /// <remarks>
        /// **Before**, and for the same reason the guard is: checking a body first would let a
        /// caller that may not reach a node learn whether its body would have been accepted, which
        /// is a difference the refusal exists to deny.
        ///
        /// **For a change this can refuse and cannot permit.** What it permits is a place in the
        /// queue for KeePass's thread, and the change asks again from inside its turn, through
        /// the <see cref="DecidedInside"/> left for it here. Asked here all the same, so that a
        /// request the rules refuse is turned away before its body is read and before it costs
        /// KeePass's thread anything.
        ///
        /// **For anything else this is the only asking**, and <see cref="Held"/> runs it inside
        /// the hold the answer is read in.
        /// </remarks>
        /// <param name="lines">Where the request's decisions are kept until they are written.</param>
        private bool Authorized(IGateContext context, SpecOperation operation, DecisionLines lines)
        {
            if (!AclVerbs.IsGoverned(operation.Verb))
            {
                // An operation that touches no data. The status route reads nothing and pairing is
                // authorized by a person reading a dialog, so there is no question to ask and
                // nothing to write down about one.
                return true;
            }

            var node = _nodes[operation.OperationId](context);

            if (node == null && ServedDatabase.TryAnswerLocked(context))
            {
                // The database locked or closed after the gate reached it and before this lookup,
                // so there was nothing to decide about rather than a node that is not there. A
                // refusal written down here would say the rules turned away a request that a lock
                // turned away, which the audit does not record before routing either.
                return false;
            }

            // Left for the handler, which asks the one question the router cannot: whether this
            // caller may have the protected values inside an answer it is otherwise allowed. It
            // goes through the same check, which decides it and writes a refusal down in one walk,
            // so that the answer the handler acts on is the answer recorded.
            context.Items[ReachItem] = Reach(context.Client, operation, lines);

            // And for a read, whose view decides what it serves: what writes down a value the
            // view withheld, as the walk that withheld it and about this client and operation.
            context.Items[WithheldItem] = Withheld(context.Client, operation, lines);

            // The identifier asked for goes on the line when it found nothing, so the operator can
            // see which reference a request was following; the check keeps only a well-formed one.
            var decision = _access.Decide(
                context.Client, operation.OperationId, operation.Verb, operation.Reach, node,
                node == null ? Captured(context, "uuid") : null, lines);

            if (decision.Permitted)
            {
                if (decision.Grants != null)
                {
                    // An operation declaring `any` answers with what was found deciding it, so the
                    // answer and the line written down are one walk and cannot disagree.
                    context.Items[GrantsItem] = decision.Grants;
                }

                if (AclVerbs.Changes(operation.Verb))
                {
                    context.Items[InsideItem] =
                        (DecidedInside)(found => Inside(context, operation, found, lines));
                }

                return true;
            }

            context.Respond(404, HttpResponse.ErrorBody(UnreachableError));
            return false;
        }

        /// <summary>
        /// The protected-scope question for one client, deciding and keeping its refusals in one
        /// walk.
        /// </summary>
        private ProtectedReach Reach(ClientIdentity client, SpecOperation operation, DecisionLines lines)
        {
            var access = _access;
            var operationId = operation.OperationId;

            return (node, verb, profiles) =>
                access.Reaches(client, operationId, verb, AclReach.Protected, node, profiles, lines);
        }

        /// <summary>
        /// What records, for one client, a protected value a read's view withheld: the line is
        /// composed when this is asked, and kept with the request's other lines by what comes
        /// back.
        /// </summary>
        /// <remarks>
        /// Composed and added apart, for the reason <see cref="ProtectedWithheld"/> gives: the
        /// line is composed inside the read's hold, from the database, and a read that goes on
        /// to serve nothing never adds it.
        /// </remarks>
        private ProtectedWithheld Withheld(ClientIdentity client, SpecOperation operation, DecisionLines lines)
        {
            var access = _access;
            var operationId = operation.OperationId;

            if (lines == null)
            {
                // An operation no rule governs keeps no lines and serves no entry. A line asked
                // for here all the same is written where it is asked, as every line is that has
                // nowhere to be kept.
                return (node, verb, decision, listed) =>
                {
                    access.Withheld(client, operationId, verb, AclReach.Protected, node, decision, listed, null);
                    return () => { };
                };
            }

            return (node, verb, decision, listed) =>
            {
                var composed = new DecisionLines();
                access.Withheld(client, operationId, verb, AclReach.Protected, node, decision, listed, composed);

                return () => lines.Asked(composed);
            };
        }

        /// <summary>
        /// A change decided again, inside its own turn and about the node it found there: what
        /// <see cref="DecidedInside"/> describes.
        /// </summary>
        /// <remarks>
        /// **The client first, then the rules about that client.** The record and the list of
        /// profiles are read in the same turn the node was found in, so the three cannot have
        /// moved against each other, and the profiles the resolver is handed are the ones the
        /// database holds for this certificate now.
        ///
        /// **Both answers replace the line the router kept before dispatch**, a refused
        /// certificate as the certificate's own refusal and anything else as the rules decided
        /// it, so the audit holds the answer the change was given and not the one it queued on.
        ///
        /// **A client the database stopped admitting while the change waited is answered here**,
        /// by <see cref="TurnAway"/> and on the request itself, before null goes back to refuse
        /// the change. Left to the handler, a refusal is the `404` of a node that is not there,
        /// which told a revoked client to look for another node and told a client whose database
        /// had locked, or had come to hold two authorities, that what it was changing had gone.
        /// The first answer on a request stands, so the handler's `404` comes second and changes
        /// nothing. A database that is not there replaces no line: the line kept before dispatch
        /// is the only answer the rules gave, as it is for a change that never reached its turn.
        /// </remarks>
        private IGateContext Inside(
            IGateContext context, SpecOperation operation, IAclNode node, DecisionLines lines)
        {
            var client = ClientCredentials.Again(
                context.Client, ServedDatabase.TargetOf(context), ServedDatabase.LogOf(context));

            if (!client.IsPaired)
            {
                TurnAway(new DecidedContext(context, client), operation, lines);
                return null;
            }

            var decision = _access.Decide(
                client, operation.OperationId, operation.Verb, operation.Reach, node, null, lines);

            if (!decision.Permitted)
            {
                return null;
            }

            var inside = new DecidedContext(context, client);
            inside.Items[ReachItem] = Reach(client, operation, lines);
            return inside;
        }

        /// <summary>A path parameter as the router captured it, or null.</summary>
        private static string Captured(IGateContext context, string name)
        {
            object found;
            var captured = context.Items.TryGetValue(ParametersItem, out found)
                ? found as IReadOnlyDictionary<string, string>
                : null;

            string value;
            return captured != null && captured.TryGetValue(name, out value) ? value : null;
        }

        private static bool Checked(IGateContext context, SpecOperation operation)
        {
            var body = JsonBody.Read(context.Body);

            var refusal = BodyCheck.Validate(operation.Body, body);
            if (refusal == null)
            {
                return true;
            }

            context.Respond(400, HttpResponse.ErrorBody(refusal));
            return false;
        }

        private void Assert()
        {
            Assert(_document, _handlers.Keys);
            AssertTwinned();

            foreach (var name in _guards.Keys)
            {
                if (!_handlers.ContainsKey(name))
                {
                    // A guard over nothing would be a precondition nobody ever evaluates.
                    throw new InvalidOperationException(
                        name + " is guarded and not handled, so the guard would never run.");
                }
            }
        }
    }
}
