using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
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
            string verb,
            OpenApiSchema body)
        {
            OperationId = operationId;
            Method = method;
            Path = path;
            NeedsIdentity = needsIdentity;
            Verb = verb;
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
        /// What the access control layer will read to decide what this operation may do. Declared
        /// in the document so that the document stays the single account of what this API permits,
        /// and read by nothing yet. TODO(step 12): that layer arrives with the access control model,
        /// which is also when a route declaring no verb stops the plugin loading.
        /// </summary>
        public string Verb { get; private set; }

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

        /// <summary>
        /// What the access control layer will read. Declared now, used later.
        /// TODO(step 12): the access control model is what reads it.
        /// </summary>
        public const string VerbExtension = "x-kpg-verb";

        /// <summary>The only body this API speaks.</summary>
        public const string MediaType = "application/json";

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
                    if (string.Equals(method, "HEAD", StringComparison.Ordinal))
                    {
                        // Served everywhere GET is, by running the GET operation, so declaring one
                        // would be a second operationId for one behavior.
                        throw new InvalidOperationException(
                            "HEAD is not declared: it follows GET. See the document's own description.");
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
                        !IsTrue(operation.Extensions, UnauthenticatedExtension),
                        Extension(operation.Extensions, VerbExtension),
                        body));
                }
            }

            if (operations.Count == 0)
            {
                throw new InvalidOperationException("The routing table declares no operations.");
            }

            return new SpecDocument(
                operations,
                document.Info == null ? string.Empty : document.Info.Title);
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

        /// <summary>The methods that carry a body, which is what the reader means by one too.</summary>
        private static bool TakesABody(string method)
        {
            return string.Equals(method, "POST", StringComparison.Ordinal)
                   || string.Equals(method, "PUT", StringComparison.Ordinal)
                   || string.Equals(method, "PATCH", StringComparison.Ordinal);
        }

        private static bool IsTrue(IDictionary<string, Microsoft.OpenApi.Interfaces.IOpenApiExtension> extensions,
            string name)
        {
            Microsoft.OpenApi.Interfaces.IOpenApiExtension value;
            if (extensions == null || !extensions.TryGetValue(name, out value))
            {
                return false;
            }

            var boolean = value as Microsoft.OpenApi.Any.OpenApiBoolean;
            return boolean != null && boolean.Value;
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
