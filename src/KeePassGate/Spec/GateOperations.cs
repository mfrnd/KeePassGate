using System;
using System.Collections.Generic;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;

namespace KeePassGate.Spec
{
    /// <summary>
    /// Everything this build answers to, bound to the names the document declares.
    /// </summary>
    /// <remarks>
    /// One place, so that the answer to "what does this plugin serve" is a list rather than a
    /// search. The startup assertion in <see cref="SpecRouter"/> holds this and the document to
    /// each other, so adding a route to one and forgetting the other stops the plugin loading.
    /// </remarks>
    public static class GateOperations
    {
        public const string Status = "getStatus";

        /// <summary>The `head` that answers for `getStatus`, on the same handler.</summary>
        /// <remarks>
        /// Bound to the same delegate rather than to one of its own. RFC 9110 section 9.3.2 says a
        /// `head` is identical to its `get`, and one handler is how that is true by construction.
        /// The handler tells them apart by `context.Method` where it is worth the trouble; where it
        /// is not, the response writer drops the content and nothing has gone wrong.
        /// </remarks>
        public const string HeadStatus = "headStatus";

        public const string PairHello = "pairHello";

        public const string PairRequest = "pairRequest";

        private static readonly object Reading = new object();

        private static SpecDocument _declared;

        /// <summary>The document this build ships, read once.</summary>
        /// <remarks>
        /// Read here rather than in a field initializer. A type initializer wraps whatever it
        /// caught in a `TypeInitializationException`, whose own message is the CLR's and is
        /// localized, and `GateIncidents` records the type name. The sentence this throws, which
        /// names exactly what the document and the handlers disagree about, would then be the one
        /// thing the operator never sees. The CLR also caches a failed type initializer
        /// for the life of the AppDomain, so a retry could not say anything different.
        ///
        /// `Lazy&lt;T&gt;` is the wrong tool for the same reason: its default mode caches the
        /// exception too.
        /// </remarks>
        public static SpecDocument Document
        {
            get
            {
                lock (Reading)
                {
                    return _declared ?? (_declared = SpecDocument.Embedded());
                }
            }
        }

        /// <summary>
        /// Where an operation is declared. Read from the document rather than written down a second
        /// time, so that a path cannot be right in one place and wrong in the other.
        /// </summary>
        public static string PathOf(string operationId)
        {
            foreach (var operation in Document.Operations)
            {
                if (string.Equals(operation.OperationId, operationId, StringComparison.Ordinal))
                {
                    return operation.Path.Text;
                }
            }

            throw new ArgumentException(
                "The routing table declares no " + operationId + ".", nameof(operationId));
        }

        public static string StatusPath
        {
            get { return PathOf(Status); }
        }

        /// <summary>
        /// A router with no pairing behind it, which is what a build that serves none has.
        /// </summary>
        public static SpecRouter Router(string version)
        {
            return Router(version, null);
        }

        /// <summary>The router this plugin runs.</summary>
        public static SpecRouter Router(string version, PairingHandler pairing)
        {
            return new SpecRouter(Document, Handlers(version, pairing), Guards(pairing));
        }

        /// <summary>
        /// The bindings, without the document, because a test that wants one rarely wants both.
        /// </summary>
        public static IReadOnlyDictionary<string, OperationHandler> Handlers(
            string version, PairingHandler pairing)
        {
            var status = new StatusOperation(version);

            return new Dictionary<string, OperationHandler>(StringComparer.Ordinal)
            {
                { Status, status.Handle },
                { HeadStatus, status.Handle },
                { PairHello, pairing == null ? NotServed : (OperationHandler)pairing.Hello },
                { PairRequest, pairing == null ? NotServed : (OperationHandler)pairing.Request },
            };
        }

        /// <summary>
        /// The preconditions the document cannot state, because only pairing knows them.
        /// </summary>
        /// <remarks>
        /// Reaching a pairing route needs a connection that arrived on an invitation and a client
        /// that does not already hold a certificate. That is neither identity nor a body, so the
        /// router asks pairing itself, before anything has looked at what was sent.
        /// </remarks>
        public static IReadOnlyDictionary<string, OperationGuard> Guards(PairingHandler pairing)
        {
            var guards = new Dictionary<string, OperationGuard>(StringComparer.Ordinal);

            // A build with no pairing refuses here rather than in the handler, so that the refusal
            // comes before anything has looked at what was sent. A caller learns that the route is
            // not there and nothing about its own body.
            guards[PairHello] = pairing == null ? Absent : (OperationGuard)PairingHandler.MayReach;
            guards[PairRequest] = pairing == null ? Absent : (OperationGuard)PairingHandler.MayReach;

            return guards;
        }

        /// <summary>
        /// What a build with no pairing answers on the pairing routes.
        /// </summary>
        /// <remarks>
        /// Bound rather than absent, which is the difference between a build that says no and a
        /// document the handlers disagree with. The assertion is about whether the two accounts
        /// match, not about whether every operation does something, and a plugin assembled without
        /// pairing says exactly as much about those routes as it says about any other path it does
        /// not serve.
        ///
        /// **Which means answering what that caller is told everywhere else**, rather than always
        /// `404`. An unadmitted client is refused on every path there is before its identity has
        /// been proved, so a `404` here and a `401` there would make this one prefix the only one
        /// in the API that replies differently, and asking for both is all it takes to find it.
        /// That answer is `Undeclared.Refuse`, the router's own, rather than a copy of it here.
        /// </remarks>
        private static void NotServed(IGateContext context)
        {
            Undeclared.Refuse(context);
        }

        /// <summary>
        /// The same answer, as a precondition, which is where it actually gets used.
        /// </summary>
        /// <remarks>
        /// `NotServed` stays bound beside this because the assertion is about whether the document
        /// and the handlers agree, and a handler that exists and is never reached is a better thing
        /// to have than a class that is only safe while something else guards it.
        /// </remarks>
        private static bool Absent(IGateContext context)
        {
            NotServed(context);
            return false;
        }
    }
}
