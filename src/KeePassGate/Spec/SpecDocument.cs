using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace KeePassGate.Spec
{
    /// <summary>One operation the document declares, as the router needs it.</summary>
    public sealed class SpecOperation
    {
        internal SpecOperation(
            string operationId,
            string method,
            PathTemplate path,
            bool needsIdentity,
            AclVerb verb,
            AclReach reach,
            OpenApiSchema body)
        {
            OperationId = operationId;
            Method = method;
            Path = path;
            NeedsIdentity = needsIdentity;
            Verb = verb;
            Reach = reach;
            Body = body;
        }

        /// <summary>What a handler binds to. Never a path.</summary>
        public string OperationId { get; private set; }

        /// <summary>Upper case, as the wire sends it.</summary>
        public string Method { get; private set; }

        public PathTemplate Path { get; private set; }

        /// <summary>
        /// False only for the two routes an invitee may reach before it has a certificate. Default
        /// deny: an operation that says nothing needs one.
        /// </summary>
        public bool NeedsIdentity { get; private set; }

        /// <summary>
        /// What this operation does to the database, which is what the access control layer asks a
        /// grant about. Declared in the document so that the document stays the single account of
        /// what this API permits, and never absent, because an operation that declares no verb
        /// stops the plugin loading.
        /// </summary>
        public AclVerb Verb { get; private set; }

        /// <summary>
        /// How far into an entry this reaches. `Fields` unless the operation says otherwise, which
        /// is the same default deny as everything else in here: an operation that opens an
        /// attachment has to say so, and one that forgot to reaches no further than the fields.
        /// </summary>
        public AclReach Reach { get; private set; }

        /// <summary>The schema a body is checked against, or null when the operation takes none.</summary>
        public OpenApiSchema Body { get; private set; }
    }

    /// <summary>
    /// The routing table, read from the document that ships inside the plugin.
    /// </summary>
    /// <remarks>
    /// **The document is the routing table rather than a description of one.** It is an embedded
    /// resource, so it cannot be edited apart from the assembly that serves it, and it is read once
    /// at startup: a document that will not parse, or that declares something the body checker
    /// cannot enforce, stops the plugin loading. A path it does not declare has nowhere to go,
    /// which is what makes routing default deny by construction rather than by remembering.
    ///
    /// `Microsoft.OpenApi` reads it. That is a real dependency and it was measured before it was
    /// taken: three assemblies, and it works on .NET Framework 4.8, which was the doubt.
    ///
    /// **It is YAML because one of those three assemblies is a YAML reader.** `SharpYaml` arrives
    /// with `Microsoft.OpenApi.Readers` whether anything reads YAML or not, so writing the document
    /// in JSON would have shipped it unused and spent the readability for nothing. What is bought
    /// is block scalars: the reasons in here are paragraphs, and in JSON they were one long line
    /// each with the newlines escaped.
    /// </remarks>
    public sealed class SpecDocument
    {
        /// <summary>Marks the routes an invitee reaches before it has a certificate.</summary>
        public const string UnauthenticatedExtension = "x-kpg-unauthenticated";

        /// <summary>What an operation does to the database, which every one of them declares.</summary>
        public const string VerbExtension = "x-kpg-verb";

        /// <summary>Marks an operation that reaches the attachments inside an entry.</summary>
        public const string AttachmentsExtension = "x-kpg-attachments";

        /// <summary>The only body this API speaks.</summary>
        public const string MediaType = "application/json";

        /// <summary>What the router answers a body sent under any other media type with.</summary>
        public const string UnsupportedMediaTypeStatus = "415";

        /// <summary>What a change that was made and not saved is answered with.</summary>
        public const string NotSavedStatus = "500";

        private const string ResourceName = "KeePassGate.Spec.openapi.yaml";

        private readonly IReadOnlyList<SpecOperation> _operations;

        private SpecDocument(List<SpecOperation> operations, string title)
        {
            // Wrapped rather than kept as the list it was built from. This is read on every
            // connection, and `IReadOnlyList` only promises a caller will not write to it.
            _operations = new ReadOnlyCollection<SpecOperation>(operations);
            Title = title;
        }

        public string Title { get; private set; }

        public IReadOnlyList<SpecOperation> Operations
        {
            get { return _operations; }
        }

        /// <summary>The document this build ships.</summary>
        public static SpecDocument Embedded()
        {
            var assembly = typeof(SpecDocument).Assembly;
            using (var stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "This build carries no " + ResourceName + ", so it has no routing table.");
                }

                using (var reader = new StreamReader(stream))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
        }

        /// <summary>
        /// Reads a document, or refuses it. Everything that could be wrong with one is wrong here,
        /// at startup, rather than later on a connection.
        /// </summary>
        public static SpecDocument Parse(string text)
        {
            OpenApiDocument document;
            OpenApiDiagnostic diagnostic;

            try
            {
                document = new OpenApiStringReader().Read(text, out diagnostic);
            }
            catch (Exception error) when (Expected.OfDocument(error))
            {
                throw new InvalidOperationException("The routing table does not parse: " + error.Message, error);
            }

            if (diagnostic.Errors.Count > 0 || document.Paths == null)
            {
                throw new InvalidOperationException(
                    "The routing table does not parse: " +
                    (diagnostic.Errors.Count > 0 ? diagnostic.Errors[0].Message : "it declares no paths"));
            }

            if (diagnostic.SpecificationVersion != OpenApiSpecVersion.OpenApi3_0)
            {
                // Asserted rather than inferred from what survived the read. A Swagger 2.0
                // document reads without complaint and means something else by half of this,
                // and this reader up-converts enough of it that the difference is not
                // otherwise visible from here.
                throw new InvalidOperationException(
                    "The routing table is not OpenAPI 3: it reads as " +
                    diagnostic.SpecificationVersion + ".");
            }

            var operations = new List<SpecOperation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var templates = new List<PathTemplate>();

            foreach (var path in document.Paths)
            {
                PathTemplate template;
                try
                {
                    template = PathTemplate.Parse(path.Key);
                }
                catch (ArgumentException error)
                {
                    // Inside the same sentence as every other refusal here. `Parse` promises
                    // one exception carrying one account of what is wrong with the document,
                    // and a template that will not parse is exactly that.
                    throw new InvalidOperationException(
                        "The routing table does not parse: " + error.Message, error);
                }

                foreach (var earlier in templates)
                {
                    if (earlier.Overlaps(template))
                    {
                        // The same argument the duplicate operationId refusal makes below:
                        // one of them would be unreachable, and which one would depend on
                        // the order the document happened to be in. Matching stops at the
                        // first template that fits, so `/entries/search` declared after
                        // `/entries/{uuid}` is a route nothing can reach and nothing says so.
                        throw new InvalidOperationException(
                            path.Key + " and " + earlier +
                            " can both match one request, so one of them is unreachable.");
                    }
                }

                templates.Add(template);

                foreach (var pair in path.Value.Operations)
                {
                    var operation = pair.Value;
                    if (string.IsNullOrEmpty(operation.OperationId))
                    {
                        throw new InvalidOperationException(
                            "An operation on " + path.Key + " has no operationId, so nothing can bind to it.");
                    }

                    if (!seen.Add(operation.OperationId))
                    {
                        // Two operations with one name would mean one of them unreachable, and
                        // which one would depend on the order the document happened to be in.
                        throw new InvalidOperationException(
                            "The routing table declares " + operation.OperationId + " twice.");
                    }

                    var method = pair.Key.ToString().ToUpperInvariant();

                    var written = Extension(operation.Extensions, VerbExtension);
                    if (written == null)
                    {
                        // The whole of what makes authorization something a route cannot be added
                        // without: not declaring a verb is not a permissive default, it is a plugin
                        // that does not load.
                        throw new InvalidOperationException(
                            operation.OperationId + " declares no " + VerbExtension +
                            ", so nothing knows what it does to the database.");
                    }

                    var verb = AclVerbs.Read(written);
                    if (verb == null)
                    {
                        throw new InvalidOperationException(
                            operation.OperationId + " declares a " + VerbExtension +
                            " nobody recognizes: " + written);
                    }

                    if (AclVerbs.IsScope(verb.Value))
                    {
                        // No operation does attachments. One reads or replaces or removes, and
                        // whether the thing it reaches is an attachment is the other extension.
                        // Only one of the two scopes has an extension, so only one of them can
                        // be pointed at: `protected` is asked about by the handler on the way
                        // out, and there is nothing to declare here in its place.
                        throw new InvalidOperationException(
                            operation.OperationId + " declares " + written + " as its " +
                            VerbExtension + ", which says how far a rule reaches rather than what " +
                            "an operation does." +
                            (verb.Value == AclVerb.Attachments ? " Use " + AttachmentsExtension + "." : string.Empty));
                    }

                    var reach = IsTrue(operation.Extensions, AttachmentsExtension, operation.OperationId)
                        ? AclReach.Attachments
                        : AclReach.Fields;

                    if (reach == AclReach.Attachments && !AclVerbs.IsGrantable(verb.Value))
                    {
                        // An operation that touches no data cannot reach into an entry, and one
                        // saying both is one whose author meant something this cannot express.
                        throw new InvalidOperationException(
                            operation.OperationId + " declares " + AttachmentsExtension +
                            " with a verb of " + written + ", which touches no data at all.");
                    }

                    if (AclVerbs.IsGrantable(verb.Value)
                        && IsTrue(operation.Extensions, UnauthenticatedExtension, operation.OperationId))
                    {
                        // The same kind of disagreement. A grant is resolved from the profiles an
                        // admission record carries, and a caller with no certificate has no record,
                        // so this would publish a route as reachable without a certificate that no
                        // caller without one could ever be allowed.
                        throw new InvalidOperationException(
                            operation.OperationId + " declares " + UnauthenticatedExtension +
                            " with a verb of " + written + ", which only a caller with a certificate" +
                            " can be granted.");
                    }

                    OpenApiSchema body = null;

                    if (operation.RequestBody != null)
                    {
                        OpenApiMediaType content;
                        if (!operation.RequestBody.Content.TryGetValue(MediaType, out content))
                        {
                            throw new InvalidOperationException(
                                operation.OperationId + " declares a body that is not " + MediaType + ".");
                        }

                        var unsupported = BodyCheck.Supports(content.Schema);
                        if (unsupported != null)
                        {
                            // A schema nobody can enforce would be a body nobody checks, so the
                            // plugin does not load rather than serving one.
                            throw new InvalidOperationException(
                                "The routing table declares a body this cannot check: " + unsupported);
                        }

                        if (!operation.RequestBody.Required)
                        {
                            // This wire has no optional bodies. Silence here would leave the
                            // schema's own `required` enforcing nothing, which is the only
                            // default in this document that would point at allow.
                            throw new InvalidOperationException(
                                operation.OperationId +
                                " declares a body and does not require it.");
                        }

                        if (!Answers(operation, UnsupportedMediaTypeStatus))
                        {
                            // The router refuses a body sent under any other media type, and it
                            // decides that from `MediaType` rather than from anything an
                            // operation says. An operation declaring a body and not declaring
                            // that refusal is therefore a document that has stopped being the
                            // whole account of what this API answers, which is the one thing the
                            // document is for.
                            throw new InvalidOperationException(
                                operation.OperationId + " declares a body and no " +
                                UnsupportedMediaTypeStatus + ", so the document does not say what" +
                                " a body sent under another media type is answered with.");
                        }

                        body = content.Schema;
                    }
                    else if (TakesABody(method))
                    {
                        // The other direction, and the one that matters: a method that carries
                        // a body, declared with none, is a body nothing checks. The startup
                        // assertion holds the document and the handlers to each other; without
                        // this it would say nothing about a `requestBody` block somebody
                        // forgot or mis-indented on a route that writes to the database.
                        throw new InvalidOperationException(
                            operation.OperationId + " is a " + method +
                            " and declares no requestBody, so nothing would check one.");
                    }

                    if (AclVerbs.Changes(verb.Value) && !Answers(operation, NotSavedStatus))
                    {
                        // Every one of these goes through `VaultHandlers.Answer`, which answers
                        // `500 not saved` for a change that was made in the database KeePass is
                        // holding and did not reach the disk. A route that changes something and
                        // does not declare that answer is a client generated from this document
                        // treating the one outcome it must not retry as an unknown.
                        throw new InvalidOperationException(
                            operation.OperationId + " changes the database and declares no " +
                            NotSavedStatus + ", so the document does not say what a change that" +
                            " was not saved is answered with.");
                    }

                    foreach (var name in template.Parameters)
                    {
                        if (!Declares(path.Value.Parameters, operation.Parameters, name))
                        {
                            // The document says what this API is, so a route whose parameter
                            // it never declares is a route it does not describe. Checked here
                            // because a client is generated from this and would have no way
                            // to know the segment is a value.
                            throw new InvalidOperationException(
                                path.Key + " captures " + name + " and " +
                                operation.OperationId +
                                " declares no path parameter for it.");
                        }
                    }

                    operations.Add(new SpecOperation(
                        operation.OperationId,
                        method,
                        template,
                        !IsTrue(operation.Extensions, UnauthenticatedExtension, operation.OperationId),
                        verb.Value,
                        reach,
                        body));
                }
            }

            if (operations.Count == 0)
            {
                throw new InvalidOperationException("The routing table declares no operations.");
            }

            // Built before it is checked, because what makes two declarations a pair is a
            // question about the whole document and is answered in one place.
            var parsed = new SpecDocument(
                operations,
                document.Info == null ? string.Empty : document.Info.Title);

            parsed.AssertPaired();
            return parsed;
        }

        /// <summary>The other half of a `get` and `head` pair, or null if there is none.</summary>
        /// <remarks>
        /// The one place that decides which two declarations are a pair, so everything holding the
        /// two to each other is asking the same question, this document's own refusals and the
        /// router's alike. Anything else answers null, and so does a `head` on a path with no `get`:
        /// what this document requires is that a `get` has a `head`, not the reverse.
        /// </remarks>
        public SpecOperation Twin(SpecOperation operation)
        {
            if (operation == null)
            {
                return null;
            }

            string wanted;
            if (string.Equals(operation.Method, "GET", StringComparison.Ordinal))
            {
                wanted = "HEAD";
            }
            else if (string.Equals(operation.Method, "HEAD", StringComparison.Ordinal))
            {
                wanted = "GET";
            }
            else
            {
                return null;
            }

            foreach (var candidate in Operations)
            {
                if (string.Equals(candidate.Method, wanted, StringComparison.Ordinal) &&
                    string.Equals(candidate.Path.Text, operation.Path.Text, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Holds every `get` to the `head` that answers for it.</summary>
        /// <remarks>
        /// RFC 9110 section 9.3.2: a `head` is identical to its `get` except that no content is
        /// sent. They share a handler, so they cannot disagree about whether something is there.
        /// They are still two declarations, and authorization is keyed on what each one declares.
        /// A `head` carrying a different verb would answer whether a node exists to a caller its
        /// `get` would have refused, which is an oracle rather than a route.
        ///
        /// Missing is refused too. A `get` with no `head` is a route this server answers `405` on
        /// for a method RFC 9110 section 9.1 expects of it, and leaving that to whoever remembers
        /// is the permissive default this design does not have anywhere else.
        /// </remarks>
        private void AssertPaired()
        {
            foreach (var operation in Operations)
            {
                if (!string.Equals(operation.Method, "GET", StringComparison.Ordinal))
                {
                    continue;
                }

                var head = Twin(operation);
                if (head == null)
                {
                    throw new InvalidOperationException(
                        operation.Path.Text +
                        " declares a get and no head, so a HEAD there would be refused.");
                }

                if (head.Verb != operation.Verb)
                {
                    throw new InvalidOperationException(
                        head.OperationId + " declares " + head.Verb + " and " +
                        operation.OperationId + " declares " + operation.Verb +
                        ", so the head would be governed differently from its own get.");
                }

                // The verb decides what a rule must grant; this decides whether anything is
                // asked for at all, and it is the more dangerous of the two to get wrong. A
                // head needing no certificate beside a get that needs one is not a laxer
                // route, it is the get's own handler reachable by a stranger. They share
                // one, and `SpecRouter` reads this off whichever of the pair was matched.
                // It also makes the whole path public, since a path is public if any
                // operation on it is, so every unpaired caller stops getting the uniform
                // refusal there.
                if (head.NeedsIdentity != operation.NeedsIdentity)
                {
                    throw new InvalidOperationException(
                        head.OperationId + " and " + operation.OperationId +
                        " disagree about needing a certificate, so the head would be reachable" +
                        " by a caller its own get would refuse.");
                }

                // Not a way in, because a body a caller cannot send is a body the check
                // refuses. It is here because the pair is meant to be one request with the
                // content left off. A head demanding a body is a route that answers nothing
                // to anybody, which is worth failing to load rather than serving.
                if ((head.Body == null) != (operation.Body == null))
                {
                    throw new InvalidOperationException(
                        head.OperationId + " and " + operation.OperationId +
                        " disagree about carrying a body, so they are not one request with" +
                        " the content left off.");
                }

                // The switch is not a verb, so it is asked about separately here as it is
                // everywhere else. A head reaching an attachment its own get does not would
                // answer whether that attachment is there to a caller holding no `A`.
                if (head.Reach != operation.Reach)
                {
                    throw new InvalidOperationException(
                        head.OperationId + " reaches " + head.Reach + " and " +
                        operation.OperationId + " reaches " + operation.Reach +
                        ", so the head would be scoped differently from its own get.");
                }
            }
        }

        /// <summary>Whether either list declares this name as a path parameter.</summary>
        /// <remarks>
        /// Both lists, because OpenAPI allows a path parameter on the path item, where every
        /// operation under it inherits one, or on the operation itself.
        /// </remarks>
        private static bool Declares(
            IList<OpenApiParameter> onThePath, IList<OpenApiParameter> onTheOperation, string name)
        {
            return Declares(onThePath, name) || Declares(onTheOperation, name);
        }

        private static bool Declares(IList<OpenApiParameter> parameters, string name)
        {
            if (parameters == null)
            {
                return false;
            }

            foreach (var parameter in parameters)
            {
                if (parameter.In == ParameterLocation.Path
                    && string.Equals(parameter.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the operation declares an answer under this status.</summary>
        private static bool Answers(OpenApiOperation operation, string status)
        {
            return operation.Responses != null && operation.Responses.ContainsKey(status);
        }

        /// <summary>The methods that carry a body, which is what the reader means by one too.</summary>
        private static bool TakesABody(string method)
        {
            return string.Equals(method, "POST", StringComparison.Ordinal)
                   || string.Equals(method, "PUT", StringComparison.Ordinal)
                   || string.Equals(method, "PATCH", StringComparison.Ordinal);
        }

        /// <summary>Whether this operation declares the switch, refusing anything that is not one.</summary>
        /// <remarks>
        /// **Absent is false and unreadable is a plugin that does not load**, which are two
        /// different answers and were one. A value the reader hands back as anything but a
        /// boolean is a line somebody wrote and meant something by: `x-kpg-attachments: "true"`
        /// is a string, reads as false, and lowers what a rule has to grant to reach an
        /// attachment. Silence is the only thing that may be taken as no.
        /// </remarks>
        private static bool IsTrue(IDictionary<string, Microsoft.OpenApi.Interfaces.IOpenApiExtension> extensions,
            string name, string operationId)
        {
            Microsoft.OpenApi.Interfaces.IOpenApiExtension value;
            if (extensions == null || !extensions.TryGetValue(name, out value))
            {
                return false;
            }

            var boolean = value as Microsoft.OpenApi.Any.OpenApiBoolean;
            if (boolean == null)
            {
                throw new InvalidOperationException(
                    operationId + " declares " + name +
                    " as something that is not true or false, and a switch that will not read is" +
                    " a switch this would have to guess at. Only leaving it out means no.");
            }

            return boolean.Value;
        }

        private static string Extension(IDictionary<string, Microsoft.OpenApi.Interfaces.IOpenApiExtension> extensions,
            string name)
        {
            Microsoft.OpenApi.Interfaces.IOpenApiExtension value;
            if (extensions == null || !extensions.TryGetValue(name, out value))
            {
                return null;
            }

            var text = value as Microsoft.OpenApi.Any.OpenApiString;
            return text == null ? null : text.Value;
        }
    }
}
