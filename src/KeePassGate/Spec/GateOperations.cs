using System;
using System.Collections.Generic;
using KeePassGate.Acl;
using KeePassGate.Audit;
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
        /// The same arrangement as every other `Head` below, and the reason is written here once.
        /// Bound to the same delegate rather than to one of its own: RFC 9110 section 9.3.2 says a
        /// `head` is identical to its `get`, and one handler is how that is true by construction.
        /// The handler tells them apart by `context.Method` where it is worth the trouble; where it
        /// is not, the response writer drops the content and nothing has gone wrong.
        /// </remarks>
        public const string HeadStatus = "headStatus";

        /// <summary>`GET /gate/whoami`: who the caller is, as the database admitted it.</summary>
        public const string Identity = "getIdentity";

        public const string HeadIdentity = "headIdentity";

        public const string RevokeCertificate = "revokeCertificate";

        public const string RenewCertificate = "renewCertificate";

        public const string PairHello = "pairHello";

        public const string PairRequest = "pairRequest";

        public const string ListGroups = "listGroups";
        public const string HeadGroups = "headGroups";
        public const string ReadGroup = "readGroup";
        public const string HeadGroup = "headGroup";
        public const string ListSubgroups = "listSubgroups";
        public const string HeadSubgroups = "headSubgroups";
        public const string CreateGroup = "createGroup";
        public const string UpdateGroup = "updateGroup";
        public const string DeleteGroup = "deleteGroup";

        /// <summary>`GET /groups/{uuid}/access`: what the caller may do at that group.</summary>
        public const string GroupAccess = "getGroupAccess";
        public const string HeadGroupAccess = "headGroupAccess";

        public const string ListEntries = "listEntries";
        public const string HeadEntries = "headEntries";
        public const string ListGroupEntries = "listGroupEntries";
        public const string HeadGroupEntries = "headGroupEntries";
        public const string ReadEntry = "readEntry";
        public const string HeadEntry = "headEntry";
        public const string CreateEntry = "createEntry";
        public const string UpdateEntry = "updateEntry";
        public const string DeleteEntry = "deleteEntry";

        /// <summary>`GET /entries/{uuid}/access`: what the caller may do at that entry.</summary>
        public const string EntryAccess = "getEntryAccess";
        public const string HeadEntryAccess = "headEntryAccess";

        public const string ListFields = "listFields";
        public const string HeadFields = "headFields";
        public const string ReadField = "readField";
        public const string HeadField = "headField";
        public const string CreateField = "createField";
        public const string UpdateField = "updateField";
        public const string StripField = "stripField";

        public const string ListAttachments = "listAttachments";
        public const string HeadAttachments = "headAttachments";
        public const string ReadAttachment = "readAttachment";
        public const string HeadAttachment = "headAttachment";
        public const string CreateAttachment = "createAttachment";
        public const string UpdateAttachment = "updateAttachment";
        public const string StripAttachment = "stripAttachment";

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

        /// <summary>
        /// The same, authorizing but recording nothing.
        /// </summary>
        /// <remarks>
        /// Every route that touches the database is still decided: what is missing is only where
        /// the decisions are written down, which is what a test rarely wants and what the plugin
        /// always supplies. A router that could not decide at all would not load, and that refusal
        /// is worth keeping for the case where somebody assembles one by hand.
        /// </remarks>
        public static SpecRouter Router(string version, PairingHandler pairing)
        {
            return Router(version, pairing, new AccessCheck(NullGateAudit.Instance));
        }

        /// <summary>
        /// The same, for a build that carries no revision and renews nothing, which is what a
        /// router assembled outside the plugin is.
        /// </summary>
        public static SpecRouter Router(string version, PairingHandler pairing, AccessCheck access)
        {
            return Router(version, pairing, null, access);
        }

        /// <summary>The same, renewing as well as pairing.</summary>
        /// <param name="renewal">
        /// The renewal route's handler, or null for a build that renews nothing, which answers it as
        /// a route it does not serve.
        /// </param>
        public static SpecRouter Router(
            string version, PairingHandler pairing, RenewalHandler renewal, AccessCheck access)
        {
            return Router(version, string.Empty, pairing, renewal, access);
        }

        /// <summary>The router this plugin runs.</summary>
        /// <param name="revision">
        /// The commit the build was made from, which the status route answers beside the version,
        /// or empty for a build that carries none.
        /// </param>
        /// <param name="access">
        /// The record and the rules, combined. Every route that touches the database asks it,
        /// through `Nodes()`. The status route, the caller's identity, the revocation, the renewal
        /// and the two pairing routes do not: the first two read nothing a rule is about, a client
        /// giving up its own admission touches no group or entry a rule is about, and pairing and
        /// renewal are authorized by a person reading a dialog rather than by a grant, since a
        /// client that is pairing holds no profiles yet.
        /// </param>
        public static SpecRouter Router(
            string version,
            string revision,
            PairingHandler pairing,
            RenewalHandler renewal,
            AccessCheck access)
        {
            return new SpecRouter(
                Document,
                Handlers(version, revision, pairing, renewal),
                Guards(pairing),
                Nodes(),
                access);
        }

        /// <summary>
        /// Which node each operation is about, which is the only part of authorization a route
        /// supplies for itself.
        /// </summary>
        /// <remarks>
        /// The router does the rest, so this list and the verbs in the document are between them
        /// the whole of what says who may reach what. An operation that declares a verb and is
        /// missing from here stops the plugin loading.
        ///
        /// A `head` names the node its own `get` names, because it is the same request with the
        /// content left off: anything else would answer whether a node is there to a caller the
        /// `get` would have refused.
        ///
        /// A listing is anchored at the node it starts from: the root for the two that sweep the
        /// database, the named group for the two that do not, and the entry itself for the fields
        /// and the attachments inside one. What the caller may see inside it
        /// is asked per item, by the handler, and is not a refusal.
        ///
        /// The two that ask what the caller may do are about the node they name, as every other
        /// question about a group or an entry is.
        /// </remarks>
        public static IReadOnlyDictionary<string, NodeSelector> Nodes()
        {
            return new Dictionary<string, NodeSelector>(StringComparer.Ordinal)
            {
                { ListGroups, VaultHandlers.RootNode },
                { HeadGroups, VaultHandlers.RootNode },
                { ReadGroup, VaultHandlers.GroupNode },
                { HeadGroup, VaultHandlers.GroupNode },
                { ListSubgroups, VaultHandlers.GroupNode },
                { HeadSubgroups, VaultHandlers.GroupNode },
                { CreateGroup, VaultHandlers.GroupNode },
                { UpdateGroup, VaultHandlers.GroupNode },
                { DeleteGroup, VaultHandlers.GroupNode },
                { GroupAccess, VaultHandlers.GroupNode },
                { HeadGroupAccess, VaultHandlers.GroupNode },

                { ListEntries, VaultHandlers.RootNode },
                { HeadEntries, VaultHandlers.RootNode },
                { ListGroupEntries, VaultHandlers.GroupNode },
                { HeadGroupEntries, VaultHandlers.GroupNode },
                { ReadEntry, VaultHandlers.EntryNode },
                { HeadEntry, VaultHandlers.EntryNode },
                { CreateEntry, VaultHandlers.GroupNode },
                { UpdateEntry, VaultHandlers.EntryNode },
                { DeleteEntry, VaultHandlers.EntryNode },
                { EntryAccess, VaultHandlers.EntryNode },
                { HeadEntryAccess, VaultHandlers.EntryNode },

                { ListFields, VaultHandlers.EntryNode },
                { HeadFields, VaultHandlers.EntryNode },
                { ReadField, VaultHandlers.EntryNode },
                { HeadField, VaultHandlers.EntryNode },
                { CreateField, VaultHandlers.EntryNode },
                { UpdateField, VaultHandlers.EntryNode },
                { StripField, VaultHandlers.EntryNode },

                { ListAttachments, VaultHandlers.EntryNode },
                { HeadAttachments, VaultHandlers.EntryNode },
                { ReadAttachment, VaultHandlers.EntryNode },
                { HeadAttachment, VaultHandlers.EntryNode },
                { CreateAttachment, VaultHandlers.EntryNode },
                { UpdateAttachment, VaultHandlers.EntryNode },
                { StripAttachment, VaultHandlers.EntryNode },
            };
        }

        /// <summary>
        /// The bindings, without the document, because a test that wants one rarely wants both.
        /// </summary>
        /// <param name="revision">The commit the build was made from, or empty for none.</param>
        public static IReadOnlyDictionary<string, OperationHandler> Handlers(
            string version, string revision, PairingHandler pairing)
        {
            return Handlers(version, revision, pairing, null);
        }

        /// <summary>The same, with the renewal route's handler.</summary>
        public static IReadOnlyDictionary<string, OperationHandler> Handlers(
            string version, string revision, PairingHandler pairing, RenewalHandler renewal)
        {
            var status = new StatusOperation(version, revision);

            return new Dictionary<string, OperationHandler>(StringComparer.Ordinal)
            {
                { Status, status.Handle },
                { HeadStatus, status.Handle },
                { Identity, IdentityOperation.Handle },
                { HeadIdentity, IdentityOperation.Handle },
                { RevokeCertificate, RevocationOperation.Handle },
                { RenewCertificate, renewal == null ? NotServed : (OperationHandler)renewal.Renew },
                { PairHello, pairing == null ? NotServed : (OperationHandler)pairing.Hello },
                { PairRequest, pairing == null ? NotServed : (OperationHandler)pairing.Request },

                { ListGroups, VaultHandlers.ListGroups },
                { HeadGroups, VaultHandlers.ListGroups },
                { ReadGroup, VaultHandlers.ReadGroup },
                { HeadGroup, VaultHandlers.ReadGroup },
                { ListSubgroups, VaultHandlers.ListSubgroups },
                { HeadSubgroups, VaultHandlers.ListSubgroups },
                { CreateGroup, VaultHandlers.CreateGroup },
                { UpdateGroup, VaultHandlers.UpdateGroup },
                { DeleteGroup, VaultHandlers.DeleteGroup },
                { GroupAccess, AccessOperation.Handle },
                { HeadGroupAccess, AccessOperation.Handle },

                { ListEntries, VaultHandlers.ListEntries },
                { HeadEntries, VaultHandlers.ListEntries },
                { ListGroupEntries, VaultHandlers.ListGroupEntries },
                { HeadGroupEntries, VaultHandlers.ListGroupEntries },
                { ReadEntry, VaultHandlers.ReadEntry },
                { HeadEntry, VaultHandlers.ReadEntry },
                { CreateEntry, VaultHandlers.CreateEntry },
                { UpdateEntry, VaultHandlers.UpdateEntry },
                { DeleteEntry, VaultHandlers.DeleteEntry },
                { EntryAccess, AccessOperation.Handle },
                { HeadEntryAccess, AccessOperation.Handle },

                { ListFields, VaultHandlers.ListFields },
                { HeadFields, VaultHandlers.ListFields },
                { ReadField, VaultHandlers.ReadField },
                { HeadField, VaultHandlers.ReadField },
                { CreateField, VaultHandlers.CreateField },
                { UpdateField, VaultHandlers.UpdateField },
                { StripField, VaultHandlers.StripField },

                { ListAttachments, VaultHandlers.ListAttachments },
                { HeadAttachments, VaultHandlers.ListAttachments },
                { ReadAttachment, VaultHandlers.ReadAttachment },
                { HeadAttachment, VaultHandlers.ReadAttachment },
                { CreateAttachment, VaultHandlers.CreateAttachment },
                { UpdateAttachment, VaultHandlers.UpdateAttachment },
                { StripAttachment, VaultHandlers.StripAttachment },
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
