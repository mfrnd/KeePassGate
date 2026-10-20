using System;
using System.Collections.Generic;
using System.Net;
using KeePassGate.Pipeline;
using KeePassGate.Transport;

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
    /// The document, dispatching. Handlers bind to `operationId` and never to a path.
    /// </summary>
    /// <remarks>
    /// **The startup assertion is the point of the whole arrangement.** Every declared operation
    /// must have a handler and every handler must answer to a declared operation, checked when this
    /// is constructed, which happens as the plugin loads. So the two cannot disagree: a route added
    /// to the document with nothing behind it, or a handler for something the document does not
    /// declare, stops the plugin loading rather than becoming a 404 discovered by whoever tried it.
    ///
    /// **The order of refusals is deliberate and is the shape step 9 left.** A locked database is
    /// refused before anything else, because a locked database has nothing to answer with and
    /// saying so is all it may say. Then the document decides which operation this is, so an
    /// undeclared path is refused before anything has parsed a body. The transport has read the
    /// bytes by then, within `GateOptions.MaxBodyBytes`, because it reads a request whole before
    /// routing it. Then identity, in one place, so a route added later is denied by default rather
    /// than by somebody remembering to guard it.
    /// Then the operation's own guard, for a precondition the document cannot state. Then the
    /// body, against the declared schema, so nothing is reported about a body on a route the
    /// caller may not reach.
    /// TODO(step 14): authorization, the admission record intersected with the rules, goes between
    /// the guard and the body. `x-kpg-verb` is declared for it, and decides no request yet.
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
        /// <summary>Where the router leaves what a path template captured.</summary>
        public const string ParametersItem = "kpg.parameters";

        private readonly SpecDocument _document;
        private readonly Dictionary<string, OperationHandler> _handlers;
        private readonly Dictionary<string, OperationGuard> _guards;

        public SpecRouter(SpecDocument document, IReadOnlyDictionary<string, OperationHandler> handlers)
            : this(document, handlers, null)
        {
        }

        public SpecRouter(
            SpecDocument document,
            IReadOnlyDictionary<string, OperationHandler> handlers,
            IReadOnlyDictionary<string, OperationGuard> guards)
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
                _handlers[pair.Key] = pair.Value;
            }

            if (guards != null)
            {
                foreach (var pair in guards)
                {
                    _guards[pair.Key] = pair.Value;
                }
            }

            Assert();
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

        public void Handle(IGateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var wanted = context.Method;

            SpecOperation operation = null;
            IReadOnlyDictionary<string, string> captured = null;
            var pathExists = false;
            var pathIsPublic = false;

            // What the path does serve, gathered while looking for what was asked for. The loop
            // below stops at a match, so this is only ever complete when there was none. That is
            // the one case that has to say what the methods are.
            var served = new List<string>();

            foreach (var candidate in _document.Operations)
            {
                IReadOnlyDictionary<string, string> found;
                if (!candidate.Path.TryMatch(context.Path, out found))
                {
                    continue;
                }

                pathExists = true;
                pathIsPublic = pathIsPublic || !candidate.NeedsIdentity;
                served.Add(candidate.Method);

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

                if (!pathIsPublic && !context.Client.IsPaired)
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

            if (operation.NeedsIdentity && !context.Client.IsPaired)
            {
                Undeclared.Refuse(context);
                return;
            }

            // Before the guard, not after. A guard on a parameterized route is exactly the
            // guard that needs what the path captured, and the obvious way to read them is
            // `context.Items[ParametersItem]`, which throws `KeyNotFoundException` on a plain
            // dictionary, so the first one written would have answered 500 rather than the
            // refusal it was written to give. They cost a caller nothing that reaching this
            // line did not already cost it: the path has already matched.
            context.Items[ParametersItem] = captured;

            OperationGuard guard;
            if (_guards.TryGetValue(operation.OperationId, out guard) && !guard(context))
            {
                return;
            }

            if (operation.Body != null && !Typed(context))
            {
                return;
            }

            if (operation.Body != null && !Checked(context, operation))
            {
                return;
            }

            var handler = _handlers[operation.OperationId];
            // The handler runs as it would for the GET, and what it wrote is dropped by the
            // writer rather than here: a body blanked at this layer still gets measured, which
            // is how `Content-Length: 0` went out where a GET would have said forty-five.
            handler(context);
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
