using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The document this build ships, held to what the router assumes about it.
    /// </summary>
    /// <remarks>
    /// A routing table that is also a file people edit needs a test that reads it, or the first
    /// thing anybody learns about a mistake is that the plugin will not load.
    /// </remarks>
    [TestFixture]
    public sealed class EmbeddedDocumentTests
    {
        [Test]
        public void The_build_carries_a_routing_table()
        {
            var document = SpecDocument.Embedded();

            Assert.That(document.Title, Is.EqualTo("KeePassGate"));
            Assert.That(document.Operations, Is.Not.Empty);
        }

        [Test]
        public void Only_the_two_pairing_routes_are_reachable_without_a_certificate()
        {
            var open = SpecDocument.Embedded().Operations
                .Where(o => !o.NeedsIdentity)
                .Select(o => o.OperationId)
                .OrderBy(name => name, StringComparer.Ordinal);

            // Named as the document names them, because that is what this is asserting about.
            Assert.That(open, Is.EqualTo(new[] { "pairHello", "pairRequest" }));
        }

        [Test]
        public void Nothing_is_declared_at_HEAD_because_HEAD_follows_GET()
        {
            Assert.That(
                SpecDocument.Embedded().Operations.Select(o => o.Method),
                Has.None.EqualTo("HEAD"));
        }

        [Test]
        public void Every_body_it_declares_is_one_this_can_check()
        {
            // `SpecDocument.Parse` refuses a schema the checker does not implement, so this is
            // really asserting that the refusal has nothing to say about our own document.
            Assert.DoesNotThrow(() => SpecDocument.Embedded());
        }
    }

    /// <summary>Reading a document, and refusing the ones that would route badly.</summary>
    [TestFixture]
    public sealed class SpecDocumentTests
    {
        private const string Head = "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{";

        [Test]
        public void An_operation_carries_its_name_its_method_and_its_path()
        {
            var document = SpecDocument.Parse(Document(
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\",\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}],\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            var operation = document.Operations.Single();

            Assert.That(operation.OperationId, Is.EqualTo("readA"));
            Assert.That(operation.Method, Is.EqualTo("GET"));
            Assert.That(operation.Path.Text, Is.EqualTo("/a/{id}"));
            Assert.That(operation.Path.Parameters, Is.EqualTo(new[] { "id" }));
        }

        [Test]
        public void An_operation_needs_a_certificate_unless_it_says_otherwise()
        {
            // Default deny, in the document as well as in the router: saying nothing means needing
            // one, so a route added later is guarded by not having been thought about.
            var document = SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            Assert.That(document.Operations.Single().NeedsIdentity, Is.True);
        }

        [Test]
        public void The_verb_the_access_control_layer_will_read_is_carried_across()
        {
            var document = SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            Assert.That(document.Operations.Single().Verb, Is.EqualTo("read"));
        }

        [Test]
        public void An_operation_with_no_name_cannot_be_bound_to_and_is_refused()
        {
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("operationId"));
        }

        [Test]
        public void Two_operations_with_one_name_are_refused()
        {
            // One of them would be unreachable, and which one would depend on the order the
            // document happened to be written in.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"same\",\"responses\":{\"200\":{\"description\":\"o\"}}}}," +
                "\"/b\":{\"get\":{\"operationId\":\"same\",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("twice"));
        }

        [Test]
        public void Declaring_HEAD_is_refused_because_it_follows_GET()
        {
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"head\":{\"operationId\":\"headA\",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("HEAD"));
        }

        [Test]
        public void A_body_this_cannot_check_stops_the_document_being_read_at_all()
        {
            // The difference between a subset and a gap. A schema nobody implemented would be a
            // body nobody checks, so the plugin does not load rather than serving one.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"requestBody\":{\"required\":true," +
                "\"content\":{\"application/json\":{\"schema\":{\"type\":\"object\"," +
                "\"additionalProperties\":false,\"properties\":{\"n\":{\"type\":\"integer\"}}}}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("cannot check"));
            Assert.That(raised.Message, Does.Contain("integer"));
        }

        [Test]
        public void A_body_that_is_not_json_is_refused()
        {
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"requestBody\":{\"required\":true," +
                "\"content\":{\"text/plain\":{\"schema\":{\"type\":\"string\"}}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("application/json"));
        }

        [Test]
        public void A_document_with_no_operations_is_not_a_routing_table()
        {
            var raised = Assert.Throws<InvalidOperationException>(
                () => SpecDocument.Parse("{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{}}"));

            Assert.That(raised.Message, Does.Contain("no operations"));
        }

        [Test]
        public void Something_that_is_not_a_document_is_refused_rather_than_read_as_an_empty_one()
        {
            Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse("not a document"));
        }

        [Test]
        public void A_document_that_is_not_openapi_three_is_refused()
        {
            // Swagger 2.0 reads without complaint and means something else by half of this, and
            // the reader up-converts enough of it that the difference is invisible from here.
            var two = "{\"swagger\":\"2.0\",\"info\":{\"title\":\"t\",\"version\":\"1\"}," +
                "\"paths\":{\"/a\":{\"get\":{\"operationId\":\"readA\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}}}";

            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(two));

            Assert.That(refused.Message, Does.Contain("not OpenAPI 3"));
        }

        [Test]
        public void Two_paths_that_could_match_one_request_are_refused()
        {
            // The same argument the duplicate operationId refusal makes: one of them would be
            // unreachable, and which one would depend on the order the document is in.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\"," +
                "\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}," +
                "\"/a/search\":{\"get\":{\"operationId\":\"searchA\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("unreachable"));
        }

        [Test]
        public void A_captured_name_the_operation_never_declares_is_refused()
        {
            // The document is what a client is generated from, so a segment it does not say is a
            // value is a route it does not describe.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("declares no path parameter"));
        }

        [Test]
        public void A_body_that_is_declared_and_not_required_is_refused()
        {
            // This wire has no optional bodies. Silence would leave the schema's own `required`
            // enforcing nothing, which is the only default here that would point at allow.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\"," +
                "\"requestBody\":{\"content\":{\"application/json\":{" +
                "\"schema\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"a\":{\"type\":\"string\"}}}}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("does not require it"));
        }

        [Test]
        public void A_method_that_carries_a_body_and_declares_none_is_refused()
        {
            // The fail-open half of the startup assertion: an unsupported schema stops the plugin
            // loading, and no schema at all used to disable checking silently.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("declares no requestBody"));
        }

        [Test]
        public void A_path_template_that_will_not_parse_is_refused_like_everything_else()
        {
            // One exception type carrying one account of what is wrong with the document.
            Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{}\":{\"get\":{\"operationId\":\"readA\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));
        }

        [Test]
        public void What_it_hands_back_cannot_be_emptied_by_whoever_reads_it()
        {
            // Read on every connection. `IReadOnlyList` only promises a caller will not write.
            var operations = SpecDocument.Embedded().Operations;
            var writable = operations as System.Collections.Generic.ICollection<SpecOperation>;

            Assert.That(writable, Is.Not.Null, "it is still castable; what matters is what that buys");
            Assert.Throws<NotSupportedException>(() => writable.Clear());
        }

        private static string Document(string paths)
        {
            return Head + paths + "}}";
        }
    }
}
