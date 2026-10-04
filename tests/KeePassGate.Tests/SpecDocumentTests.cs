using System;
using System.Linq;
using KeePassGate.Acl;
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
        public void What_the_gate_hands_out_is_what_it_will_take_back()
        {
            // Two numbers that have to agree: the ceiling `ReadAttachment` refuses above, and the
            // longest base64 the document accepts on a write. An attachment that can be read and
            // never restored is what they buy when they drift.
            var longest = 4 * ((VaultHandlers.MaxAttachmentBytes + 2) / 3);

            var writes = SpecDocument.Embedded().Operations
                .Where(o => o.Body != null && o.Body.Properties.ContainsKey("content"))
                .ToList();

            Assert.That(writes, Is.Not.Empty, "the attachment writes are what this is about");

            foreach (var write in writes)
            {
                Assert.That(write.Body.Properties["content"].MaxLength, Is.EqualTo(longest),
                    write.OperationId + " accepts a different size from the one a read hands out");
            }
        }

        [Test]
        public void The_largest_attachment_write_fits_the_body_the_transport_reads()
        {
            // The third number in the round trip. The transport refuses a body over its cap by
            // `Content-Length` before the document is consulted, so a ceiling the document accepts
            // and the transport does not is one more attachment that can be read and never put
            // back, and nothing but this relates the two. Counted at its worst: base64 is ASCII,
            // and any other string may be every character escaped as six bytes of `\uXXXX`.
            const int EscapedCharacter = 6;
            const int Punctuation = 6;

            var writes = SpecDocument.Embedded().Operations
                .Where(o => o.Body != null && o.Body.Properties.ContainsKey("content"))
                .ToList();

            Assert.That(writes, Is.Not.Empty, "the attachment writes are what this is about");

            foreach (var write in writes)
            {
                var largest = 2;
                foreach (var property in write.Body.Properties)
                {
                    var perCharacter = property.Key == "content" ? 1 : EscapedCharacter;
                    largest += property.Key.Length + Punctuation + property.Value.MaxLength.Value * perCharacter;
                }

                Assert.That(largest, Is.LessThanOrEqualTo(new GateOptions().MaxBodyBytes),
                    write.OperationId + " declares a body the transport refuses before reading it");
            }
        }

        [Test]
        public void The_waits_before_busy_are_the_ones_the_writer_keeps()
        {
            // The document gives a client the two numbers to set its own timeout against, and a
            // number the writer no longer waits for is a client giving up on a write that may
            // still land. Read off the text, with its line breaks taken out.
            string text;
            using (var stream = typeof(SpecDocument).Assembly.GetManifestResourceStream("KeePassGate.Spec.openapi.yaml"))
            using (var reader = new System.IO.StreamReader(stream))
            {
                text = System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), @"\s+", " ");
            }

            Assert.That(text, Does.Contain(
                "up to " + KeePassGate.Bridge.DatabaseWriter.LockTimeout.TotalSeconds + " seconds for the one ahead of it"));
            Assert.That(text, Does.Contain(
                "up to " + KeePassGate.Bridge.DatabaseWriter.WriteTimeout.TotalSeconds + " more for KeePass's own thread"));
        }

        [Test]
        public void Every_shared_response_says_what_its_body_is()
        {
            // A client generated from the document reads a response without content as one with
            // no body. Every answer given under these carries one, and `Internal` was the one that
            // declared none, so the words telling `not saved` from `internal error` were nowhere.
            Microsoft.OpenApi.Models.OpenApiDocument document;
            using (var stream = typeof(SpecDocument).Assembly.GetManifestResourceStream("KeePassGate.Spec.openapi.yaml"))
            {
                Microsoft.OpenApi.Readers.OpenApiDiagnostic diagnostic;
                document = new Microsoft.OpenApi.Readers.OpenApiStreamReader().Read(stream, out diagnostic);
            }

            Assert.That(document.Components.Responses, Is.Not.Empty);
            foreach (var response in document.Components.Responses)
            {
                Assert.That(response.Value.Content, Is.Not.Empty, response.Key + " declares no body");
            }
        }

        [Test]
        public void The_build_carries_a_routing_table()
        {
            var document = SpecDocument.Embedded();

            Assert.That(document.Title, Is.EqualTo("KeePassGate"));
            Assert.That(document.Operations, Is.Not.Empty);
        }

        [Test]
        public void The_document_carries_the_version_the_plugin_is_built_as()
        {
            // One version for the plugin, the client and this document, written once in
            // `Directory.Build.props` and moved together. The build stamps the plugin and the
            // client reads its own from what was installed, but a YAML file reads nothing, so this
            // is what holds the document to the number: a version moved there and not here fails
            // before anything ships. The client's tests hold the same document to the client.
            Microsoft.OpenApi.Models.OpenApiDocument document;
            using (var stream = typeof(SpecDocument).Assembly.GetManifestResourceStream("KeePassGate.Spec.openapi.yaml"))
            {
                Microsoft.OpenApi.Readers.OpenApiDiagnostic diagnostic;
                document = new Microsoft.OpenApi.Readers.OpenApiStreamReader().Read(stream, out diagnostic);
            }

            Assert.That(document.Info.Version, Is.EqualTo(GateVersion.Current));
        }

        [Test]
        public void The_table_is_the_size_the_design_argues_from()
        {
            // `DESIGN.md` §128 answers "spec-first routing is slower than a switch statement" by
            // saying how small the table is, so the answer is only as good as the number in it.
            // The number was written when the API was pairing and a status route, said "fewer
            // than a dozen" ever after, and was wrong by a factor of three by the time the
            // routes landed. A count nothing reads cannot notice that.
            //
            // A route added here fails this test on purpose: the sentence in §128 is the thing
            // that has to be brought along, and there is nowhere else that number is kept.
            var declared = SpecDocument.Embedded().Operations;

            Assert.That(declared.Count, Is.EqualTo(38),
                "§128 says thirty-eight operations");
            Assert.That(declared.Select(o => o.Path.Text).Distinct().Count(), Is.EqualTo(15),
                "§128 says fifteen paths");
            Assert.That(declared.Count(o => o.Method == "HEAD"), Is.EqualTo(11),
                "§128 says eleven of them are the HEAD that answers for a GET");
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
        public void Every_get_it_declares_has_a_head_beside_it()
        {
            // RFC 9110 section 9.1 expects a server to answer HEAD, and section 9.3.2 says
            // it answers what its GET answers without the content. Declared rather than
            // derived so that this document is the whole account of what the API answers.
            var declared = SpecDocument.Embedded().Operations;

            var gets = declared.Where(o => o.Method == "GET").Select(o => o.Path.Text);
            var heads = declared.Where(o => o.Method == "HEAD").Select(o => o.Path.Text);

            Assert.That(heads, Is.EquivalentTo(gets));
        }

        [Test]
        public void Nothing_this_serves_is_ungoverned_by_accident()
        {
            // `none` is a decision and it is written down. What this asserts is that the only
            // operations making it are the ones that were argued for: the status route, which
            // reads nothing, a client's revocation of itself, which touches no group or entry a
            // rule is about, and pairing and renewal, which a person authorizes in the moment.
            // The heads are here as well, and for the same reason their gets are: a head
            // declares what its own get declares, which the document is refused for
            // getting wrong.
            var ungoverned = SpecDocument.Embedded().Operations
                .Where(o => o.Verb == AclVerb.None)
                .Select(o => o.OperationId)
                .OrderBy(name => name, StringComparer.Ordinal);

            Assert.That(ungoverned, Is.EqualTo(new[]
            {
                "getStatus", "headStatus", "pairHello", "pairRequest", "renewCertificate", "revokeCertificate"
            }));
        }

        [Test]
        public void The_operations_reaching_an_attachment_are_the_ones_about_attachments()
        {
            // The switch is not a verb, so an operation reaching that far says so separately. What
            // this asserts is that nothing else quietly does: a route added to the entry or the
            // group side cannot pick up the attachment scope by being near one. The two heads
            // are here because their gets are: the switch is carried over with the verb.
            var reaching = SpecDocument.Embedded().Operations
                .Where(o => o.Reach == AclReach.Attachments)
                .Select(o => o.OperationId)
                .OrderBy(name => name, StringComparer.Ordinal);

            Assert.That(reaching, Is.EqualTo(new[]
            {
                "createAttachment", "headAttachment", "headAttachments",
                "listAttachments", "readAttachment", "stripAttachment", "updateAttachment",
            }));
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
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}],\"responses\":{\"200\":{\"description\":\"o\"}}},\"head\":{\"operationId\":\"headReadA\",\"x-kpg-verb\":\"read\",\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}],\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            // Two operations on this path now, since every get declares a head.
            var operation = document.Operations.Single(o => o.Method == "GET");

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
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}},\"head\":{\"operationId\":\"headReadA\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            Assert.That(document.Operations.Single(o => o.Method == "GET").NeedsIdentity, Is.True);
        }

        [Test]
        public void An_operation_declares_what_it_does_to_the_database()
        {
            var document = SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            Assert.That(document.Operations.Single(o => o.Method == "GET").Verb,
                Is.EqualTo(AclVerb.Read));
        }

        [Test]
        public void An_operation_that_declares_no_verb_stops_the_document_being_read()
        {
            // The one refusal that makes authorization something a route cannot be added without.
            // A missing verb read as `none` would be a route nothing governs, arrived at by
            // forgetting rather than by deciding.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("x-kpg-verb"));
            Assert.That(raised.Message, Does.Contain("readA"));
        }

        [Test]
        public void A_verb_nobody_recognizes_stops_it_too()
        {
            // Including a spelling of one that exists, because a document is a file people edit.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"Read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("recognizes"));
        }

        [Test]
        public void An_operation_says_separately_whether_it_reaches_attachments()
        {
            var fields = SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            var inside = SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"x-kpg-attachments\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"x-kpg-attachments\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            Assert.That(fields.Operations.Single(o => o.Method == "GET").Reach,
                Is.EqualTo(AclReach.Fields),
                "saying nothing reaches no further than the fields");
            Assert.That(inside.Operations.Single(o => o.Method == "GET").Reach,
                Is.EqualTo(AclReach.Attachments));
        }

        [TestCase("\"true\"", TestName = "as the string true")]
        [TestCase("1", TestName = "as a number")]
        [TestCase("null", TestName = "as null")]
        public void An_attachment_switch_that_is_not_a_boolean_stops_the_document_being_read(string written)
        {
            // It read as false, which is the direction that costs something: an operation
            // reaching an attachment and not saying so is answered for by the entry alone, so a
            // rule holding read and not `A` reaches an attachment it was written to be kept out
            // of. `x-kpg-attachments: "true"` in a hand-edited document is exactly that, and it
            // looks right on the page.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"x-kpg-attachments\":" + written + ",\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"x-kpg-attachments\":" + written + ",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("x-kpg-attachments"));
            Assert.That(raised.Message, Does.Contain("readA"));
        }

        [Test]
        public void An_unauthenticated_switch_that_is_not_a_boolean_stops_it_too()
        {
            // The same helper reads both, and this one fails closed when it is misread: a route
            // meant to be public becomes one nobody can reach. Refused all the same, because a
            // document nobody can read the same way twice is not a routing table.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"x-kpg-verb\":\"none\"," +
                "\"x-kpg-unauthenticated\":\"yes\"," +
                "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{" +
                "\"schema\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"a\":{\"type\":\"string\"}}}}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"},\"415\":{\"description\":\"m\"}}}}")));

            Assert.That(raised.Message, Does.Contain("x-kpg-unauthenticated"));
        }

        [Test]
        public void The_attachment_switch_cannot_be_declared_as_a_verb()
        {
            // It says how far a rule reaches, not what an operation does, and an operation that
            // named it would be one nobody could work out the verb for.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"attachments\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("x-kpg-attachments"));
        }

        [Test]
        public void The_protected_switch_cannot_be_declared_as_a_verb_either()
        {
            // The other scope, which had no test and is the one the model calls the mistake
            // worth naming. It is refused for the same reason and without the same advice:
            // `attachments` has an extension to point at and `protected` has none, because a
            // protected value is asked about by the handler writing the answer.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"protected\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("how far a rule reaches"));
            Assert.That(raised.Message, Does.Not.Contain("x-kpg-attachments"),
                "there is no extension to send anybody to for this one");
        }

        [Test]
        public void An_operation_that_touches_no_data_cannot_reach_into_an_entry()
        {
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"none\"," +
                "\"x-kpg-attachments\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("touches no data"));
        }

        [Test]
        public void An_operation_needing_no_certificate_cannot_be_governed_by_a_grant()
        {
            // A grant is resolved from an admission record, and a caller with no certificate has
            // none, so the document would be saying a route is open that nobody it opens to could use.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"listA\",\"x-kpg-verb\":\"list\"," +
                "\"x-kpg-unauthenticated\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"list\"," +
                "\"x-kpg-unauthenticated\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("x-kpg-unauthenticated"));
            Assert.That(raised.Message, Does.Contain("can be granted"));
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
                "\"/a\":{\"get\":{\"operationId\":\"same\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}},\"head\":{\"operationId\":\"headSame\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}}}," +
                "\"/b\":{\"get\":{\"operationId\":\"same\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}},\"head\":{\"operationId\":\"headSame\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("twice"));
        }

        [Test]
        public void A_get_with_no_head_stops_the_plugin_loading()
        {
            // Not left to whoever remembers. A get with no head is a route this server
            // answers 405 on for a method RFC 9110 section 9.1 expects of it.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("no head"));
        }

        [Test]
        public void A_head_governed_differently_from_its_own_get_stops_it_too()
        {
            // The two share a handler, so they cannot disagree about whether something is
            // there. They are two declarations even so, and authorization reads what each one
            // declares. A head carrying a different verb would answer that question to a
            // caller its own get would have refused.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"none\",\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("governed differently"));
        }

        [Test]
        public void A_head_needing_no_certificate_beside_a_get_that_does_stops_it_as_well()
        {
            // The worst of the family, because it does not merely answer differently: the
            // pair shares one handler, so a head nobody has to be admitted for is the get's
            // own handler run for a stranger. It makes the path public as well, since a path
            // is public if any operation on it is. Governed by nothing, because a head needing
            // no certificate and governed by a grant is refused before the pair is looked at.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"none\",\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"none\",\"x-kpg-unauthenticated\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("needing a certificate"));
        }

        [Test]
        public void A_head_carrying_a_body_its_own_get_does_not_stops_it_too()
        {
            // Not a way in, because a body a caller cannot send is a body the check refuses.
            // A head that answers nothing to anybody is still worth failing to load over.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\",\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"a\":{\"type\":\"string\"}}}}}},\"responses\":{\"200\":{\"description\":\"o\"},\"415\":{\"description\":\"m\"}}}}")));

            Assert.That(raised.Message, Does.Contain("carrying a body"));
        }

        [Test]
        public void A_head_scoped_differently_from_its_own_get_stops_it_as_well()
        {
            // The same argument one switch along. A head that did not reach the attachment
            // its get reaches would be answered for by the entry alone, so a caller holding
            // read and not `A` would learn whether the attachment is there.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"x-kpg-attachments\":true," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("scoped differently"));
        }

        [Test]
        public void A_body_this_cannot_check_stops_the_document_being_read_at_all()
        {
            // The difference between a subset and a gap. A schema nobody implemented would be a
            // body nobody checks, so the plugin does not load rather than serving one.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"x-kpg-verb\":\"update\",\"requestBody\":{\"required\":true," +
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
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"x-kpg-verb\":\"update\",\"requestBody\":{\"required\":true," +
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
                "\"paths\":{\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headReadA\",\"x-kpg-verb\":\"read\"," +
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
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\",\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}," +
                "\"/a/search\":{\"get\":{\"operationId\":\"searchA\",\"x-kpg-verb\":\"list\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("unreachable"));
        }

        [Test]
        public void An_operation_carries_the_query_parameters_it_declares()
        {
            // Read off the document rather than assumed empty, which is what lets the router
            // refuse a name nobody declared without refusing every query for all time. Declared
            // on the path item as well as on the operation, because OpenAPI allows either and
            // every operation under a path item inherits its parameters.
            var document = SpecDocument.Parse(Document(
                "\"/a\":{\"parameters\":[{\"name\":\"since\",\"in\":\"query\"," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"parameters\":[{\"name\":\"limit\",\"in\":\"query\"," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"parameters\":[{\"name\":\"limit\",\"in\":\"query\"," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}"));

            Assert.That(
                document.Operations.Single(o => o.Method == "GET").Query,
                Is.EquivalentTo(new[] { "since", "limit" }));
            Assert.That(
                document.Operations.Single(o => o.Method == "HEAD").Query,
                Is.EquivalentTo(new[] { "since", "limit" }),
                "the path item's own parameter inherited, and the operation's own declared");
        }

        [Test]
        public void A_head_taking_other_query_parameters_than_its_own_get_stops_it_too()
        {
            // One request with the content left off, so one query. A name the get declares and
            // the head does not is a target the router serves at the one and refuses as
            // undeclared at the other, and the head would say nothing is there where the get
            // answers with the entries.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"parameters\":[{\"name\":\"limit\",\"in\":\"query\"," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("disagree about the query parameters"));
        }

        [TestCase("{\"type\":\"string\",\"pattern\":\"^[a-f0-9]+$\"}", "pattern")]
        [TestCase("{\"type\":\"string\",\"enum\":[\"root\"]}", "enum")]
        [TestCase("{\"type\":\"integer\"}", "integer")]
        public void A_path_parameter_schema_nothing_would_keep_is_refused(string schema, string named)
        {
            // What a body schema nothing enforces is refused for. A path parameter may be a string
            // and say how long it is, and the router holds a segment to that; anything else the
            // document said about a parameter loaded clean and was kept by nobody.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"none\",\"parameters\":" +
                "[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":" + schema + "}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"none\",\"parameters\":" +
                "[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":" + schema + "}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("path parameter id"));
            Assert.That(raised.Message, Does.Contain(named));
        }

        [TestCase("{\"type\":\"string\",\"maxLength\":3}", "maxLength")]
        [TestCase("{\"type\":\"integer\"}", "integer")]
        public void A_query_parameter_schema_nothing_would_keep_is_refused(string schema, string named)
        {
            // The router asks only whether a query name is declared, so a bound on its value was a
            // bound the document stated and nothing held a request to.
            var raised = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"parameters\":[{\"name\":\"limit\",\"in\":\"query\",\"schema\":" + schema + "}]," +
                "\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"none\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"none\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(raised.Message, Does.Contain("query parameter limit"));
            Assert.That(raised.Message, Does.Contain(named));
        }

        [Test]
        public void A_captured_name_the_operation_never_declares_is_refused()
        {
            // The document is what a client is generated from, so a segment it does not say is a
            // value is a route it does not describe.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{id}\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("declares no path parameter"));
        }

        [Test]
        public void A_body_that_is_declared_and_not_required_is_refused()
        {
            // This wire has no optional bodies. Silence would leave the schema's own `required`
            // enforcing nothing, which is the only default here that would point at allow.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"x-kpg-verb\":\"create\"," +
                "\"requestBody\":{\"content\":{\"application/json\":{" +
                "\"schema\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"a\":{\"type\":\"string\"}}}}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("does not require it"));
        }

        [Test]
        public void An_operation_that_changes_the_database_and_declares_no_500_is_refused()
        {
            // Every one of them answers `500 not saved` for a change that was made and did not
            // reach the disk, and none of them said so. A client generated from a document that
            // leaves it out meets the one outcome it must not retry as an unknown status.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{id}\":{\"delete\":{\"operationId\":\"stripA\",\"x-kpg-verb\":\"strip\"," +
                "\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true," +
                "\"schema\":{\"type\":\"string\"}}]," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("500"));
            Assert.That(refused.Message, Does.Contain("stripA"));
        }

        [Test]
        public void A_read_is_not_asked_to_declare_a_save_that_could_fail()
        {
            // The other side of it, because a rule that asked every operation would be a rule
            // about nothing. A read leaves the database as it found it, so there is no save to
            // report on.
            Assert.DoesNotThrow(() => SpecDocument.Parse(Document(
                "\"/a\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"read\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));
        }

        [Test]
        public void A_body_declared_without_the_refusal_a_wrong_media_type_gets_is_refused()
        {
            // The router answers 415 from the one media type this API declares, so an operation
            // taking a body and saying nothing about that answer is a document that has stopped
            // being the whole account of what the API answers. It went undeclared everywhere
            // until this ran, which is how the status came to have no reason phrase either.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"x-kpg-verb\":\"create\"," +
                "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{" +
                "\"schema\":{\"type\":\"object\",\"additionalProperties\":false," +
                "\"properties\":{\"a\":{\"type\":\"string\"}}}}}}," +
                "\"responses\":{\"201\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("415"));
            Assert.That(refused.Message, Does.Contain("writeA"));
        }

        [Test]
        public void A_method_that_carries_a_body_and_declares_none_is_refused()
        {
            // The fail-open half of the startup assertion: an unsupported schema stops the plugin
            // loading, and no schema at all used to disable checking silently.
            var refused = Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"x-kpg-verb\":\"create\"," +
                "\"responses\":{\"200\":{\"description\":\"o\"}}}}")));

            Assert.That(refused.Message, Does.Contain("declares no requestBody"));
        }

        [Test]
        public void A_path_template_that_will_not_parse_is_refused_like_everything_else()
        {
            // One exception type carrying one account of what is wrong with the document.
            Assert.Throws<InvalidOperationException>(() => SpecDocument.Parse(Document(
                "\"/a/{}\":{\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"read\"," +
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

        [Test]
        public void The_query_names_it_hands_back_cannot_be_added_to_either()
        {
            // What the router refuses a query for, read on every connection too. Handed out as a
            // set, a name added to it by anything holding the document was a name every later
            // request could send.
            var operation = SpecDocument.Embedded().Operations[0];
            var writable = operation.Query as System.Collections.Generic.ICollection<string>;

            Assert.That(operation.Query, Is.Not.InstanceOf<System.Collections.Generic.ISet<string>>());
            Assert.That(writable, Is.Not.Null, "it is still castable; what matters is what that buys");
            Assert.Throws<NotSupportedException>(() => writable.Add("limit"));
            Assert.That(operation.DeclaresQuery("limit"), Is.False);
        }

        private static string Document(string paths)
        {
            return Head + paths + "}}";
        }
    }
}
