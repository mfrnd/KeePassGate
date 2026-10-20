using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Acl;
using KeePassGate.Diagnostics;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The router authorizing, which is what makes a rule mean something to a request.
    /// </summary>
    /// <remarks>
    /// The refusals are the subject. What a permitted request does is the handler's business; what
    /// this decides is whether the handler runs at all, what a refused caller is told, and what a
    /// route that forgot about authorization costs.
    /// </remarks>
    [TestFixture]
    public sealed class RouterAuthorizationTests
    {
        private const string Document =
            "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
            "\"/entries/{uuid}\":{\"get\":{\"operationId\":\"readEntry\",\"x-kpg-verb\":\"read\",\"parameters\":[{\"name\":\"uuid\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
            "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
            "\"head\":{\"operationId\":\"headEntry\",\"x-kpg-verb\":\"read\",\"parameters\":[{\"name\":\"uuid\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
            "\"responses\":{\"200\":{\"description\":\"o\"}}}}}}";

        /// <summary>The same document with an operation that governs nothing, which is `none`.</summary>
        private const string Governs =
            "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
            "\"/gate/status\":{\"get\":{\"operationId\":\"getStatus\",\"x-kpg-verb\":\"none\"," +
            "\"responses\":{\"200\":{\"description\":\"o\"}}}," +
            "\"head\":{\"operationId\":\"headStatus\",\"x-kpg-verb\":\"none\"," +
            "\"responses\":{\"200\":{\"description\":\"o\"}}}}}}";

        private RecordingAudit _audit;
        private RecordingGateLog _log;
        private List<string> _reached;
        private IAclNode _node;

        [SetUp]
        public void SetUp()
        {
            _audit = new RecordingAudit();
            _log = new RecordingGateLog();
            _reached = new List<string>();
            _node = TestNode.Root("root").Granting("reader", AclVerb.Read).Child("an entry");
        }

        [Test]
        public void A_client_the_rules_permit_reaches_the_handler()
        {
            var context = Ask(Paired("reader"));

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(_reached, Is.EqualTo(new[] { "readEntry" }));
        }

        [Test]
        public void A_client_the_rules_refuse_never_reaches_it()
        {
            var context = Ask(Paired("nobody"));

            Assert.That(_reached, Is.Empty, "the handler did not run, so nothing was read to be discarded");
            Assert.That(context.Status, Is.EqualTo(404));
        }

        [Test]
        public void A_node_that_is_not_there_and_one_that_may_not_be_reached_answer_the_same()
        {
            // Telling them apart would let a client that may reach nothing map the database by
            // asking, one identifier at a time, which is exactly what `list` exists to withhold.
            var refused = Ask(Paired("nobody"));

            _node = null;
            var absent = Ask(Paired("reader"));

            Assert.That(absent.Status, Is.EqualTo(refused.Status));
            Assert.That(absent.Json, Is.EqualTo(refused.Json));
        }

        [Test]
        public void The_operator_is_told_the_difference_even_though_the_caller_is_not()
        {
            Ask(Paired("nobody"));

            _node = null;
            Ask(Paired("reader"));

            Assert.That(_audit.Entries.Count, Is.EqualTo(2));
            Assert.That(_audit.Entries[0].Node, Is.EqualTo("an entry"));
            Assert.That(_audit.Entries[0].Reason, Does.Contain("no rule anywhere grants it"));
            Assert.That(_audit.Entries[1].Node, Is.Null);
            Assert.That(_audit.Entries[1].Reason, Does.Contain("nothing to decide"));
        }

        [Test]
        public void A_certificate_this_authority_issued_and_refused_is_written_down_by_its_key()
        {
            // A revoked client still trying. It is answered what every caller without an
            // admission is answered, and the operator who revoked it can see it keep asking.
            ClientIdentity refused;
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                refused = ClientIdentity.Refused("d1e2f3", certificate, "this certificate has no admission record");
            }

            var context = Ask(refused);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(_reached, Is.Empty);

            var recorded = _audit.Entries.Single();
            Assert.That(recorded.Permitted, Is.False);
            Assert.That(recorded.AdmissionKey, Is.EqualTo(refused.AdmissionKey));
            Assert.That(recorded.OperationId, Is.EqualTo("readEntry"));
            Assert.That(recorded.Node, Is.Null);
            Assert.That(recorded.Reason, Is.EqualTo("this certificate has no admission record"));
        }

        [Test]
        public void A_caller_that_proved_nothing_is_not_written_down()
        {
            // Nobody the operator issued, so a line would name nobody, and anybody who reaches the
            // port could grow the file with them.
            var context = Ask(ClientIdentity.Stranger("d1e2f3"));

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(_audit.Entries, Is.Empty);
        }

        [Test]
        public void A_permitted_request_is_written_down_as_well_as_a_refused_one()
        {
            Ask(Paired("reader"));

            var recorded = _audit.Entries.Single();

            Assert.That(recorded.Permitted, Is.True);
            Assert.That(recorded.OperationId, Is.EqualTo("readEntry"));
        }

        [Test]
        public void The_rules_are_asked_before_the_body_is_read()
        {
            // The same ordering argument as the pairing guard. A body checked first would let a
            // caller that may reach nothing learn whether its body would have been accepted.
            var document =
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
                "\"/entries/{uuid}\":{\"post\":{\"operationId\":\"writeEntry\",\"x-kpg-verb\":\"update\",\"parameters\":[{\"name\":\"uuid\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
                "\"requestBody\":{\"required\":true,\"content\":{\"application/json\":{\"schema\":" +
                "{\"type\":\"object\",\"required\":[\"title\"],\"additionalProperties\":false," +
                "\"properties\":{\"title\":{\"type\":\"string\"}}}}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"},\"415\":{\"description\":\"m\"},\"500\":{\"description\":\"u\"}}}}}}";

            var router = Router(SpecDocument.Parse(document), "writeEntry");
            var context = Context("POST", "/entries/7b1c", Paired("nobody"), "{\"nonsense\":1}");

            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404), "not 400: the body was never looked at");
        }

        [Test]
        public void An_operation_that_declares_a_verb_and_names_no_node_stops_the_plugin_loading()
        {
            // The whole arrangement, one level along from the verb itself. A route that nothing can
            // authorize would serve exactly as though everything were permitted.
            var raised = Assert.Throws<InvalidOperationException>(() => new SpecRouter(
                SpecDocument.Parse(Document),
                new Dictionary<string, OperationHandler>
                {
                    { "readEntry", Nothing },
                    { "headEntry", Nothing },
                },
                null,
                new Dictionary<string, NodeSelector>(),
                new AccessCheck(_audit)));

            Assert.That(raised.Message, Does.Contain("which node it is about"));
        }

        [Test]
        public void An_operation_that_declares_a_verb_with_nothing_to_authorize_with_stops_it_too()
        {
            var raised = Assert.Throws<InvalidOperationException>(() => new SpecRouter(
                SpecDocument.Parse(Document),
                new Dictionary<string, OperationHandler>
                {
                    { "readEntry", Nothing },
                    { "headEntry", Nothing },
                }));

            Assert.That(raised.Message, Does.Contain("nothing to authorize with"));
        }

        [Test]
        public void Naming_a_node_for_an_operation_that_governs_nothing_stops_it_as_well()
        {
            // A selector nothing consults reads as authorization that is happening and is not,
            // which is worse than none at all.
            var document = Document.Replace("\"read\"", "\"none\"");

            var raised = Assert.Throws<InvalidOperationException>(() => new SpecRouter(
                SpecDocument.Parse(document),
                new Dictionary<string, OperationHandler>
                {
                    { "readEntry", Nothing },
                    { "headEntry", Nothing },
                },
                null,
                new Dictionary<string, NodeSelector> { { "readEntry", context => _node } },
                new AccessCheck(_audit)));

            Assert.That(raised.Message, Does.Contain("nothing would ever ask about"));
        }

        [Test]
        public void A_head_asking_about_another_node_than_its_own_get_stops_it_as_well()
        {
            // A decision is made of three things, and this is the third. The document holds
            // the verb and the switch together and `Assert` holds the handler, so what is
            // left is the node: a head asking about another one would put this one's rule to
            // that one's existence.
            var raised = Assert.Throws<InvalidOperationException>(() => new SpecRouter(
                SpecDocument.Parse(Document),
                new Dictionary<string, OperationHandler>
                {
                    { "readEntry", Nothing },
                    { "headEntry", Nothing },
                },
                null,
                new Dictionary<string, NodeSelector>
                {
                    { "readEntry", context => _node },
                    { "headEntry", context => _node },
                },
                new AccessCheck(_audit)));

            Assert.That(raised.Message, Does.Contain("asks about a different node"));
        }

        [Test]
        public void A_node_named_for_an_operation_nobody_declared_stops_it_as_well()
        {
            NodeSelector node = context => _node;

            var raised = Assert.Throws<InvalidOperationException>(() => new SpecRouter(
                SpecDocument.Parse(Document),
                new Dictionary<string, OperationHandler>
                {
                    { "readEntry", Nothing },
                    { "headEntry", Nothing },
                },
                null,
                new Dictionary<string, NodeSelector>
                {
                    { "readEntry", node },
                    { "headEntry", node },
                    { "readEverything", node },
                },
                new AccessCheck(_audit)));

            Assert.That(raised.Message, Does.Contain("readEverything"));
        }

        [Test]
        public void An_operation_that_governs_nothing_is_never_written_down()
        {
            // The status route reads nothing and pairing is authorized by a person reading a
            // dialog, so there is no question to ask and nothing to record about one. An audit full
            // of "nothing to decide" would bury the lines that matter.
            //
            // Through this fixture's own router rather than the plugin's, because the plugin's is
            // built with an audit of its own: asserting that this one stayed empty while the router
            // wrote to another would be an assertion that cannot fail.
            OperationHandler status = answer => { _reached.Add("getStatus"); answer.Respond(200, "{}"); };

            var router = new SpecRouter(
                SpecDocument.Parse(Governs),
                new Dictionary<string, OperationHandler>
                {
                    { "getStatus", status },
                    { "headStatus", status },
                },
                null,

                // No node selector, because an operation that governs nothing may not name one:
                // the router refuses to be built that way, which is the assertion this leans on.
                new Dictionary<string, NodeSelector>(),
                new AccessCheck(_audit));
            var context = Context("GET", "/gate/status", Paired("reader"), null);

            router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(_reached, Does.Contain("getStatus"), "the handler ran, so there was a request to record");
            Assert.That(_audit.Entries, Is.Empty);
        }

        [Test]
        public void A_database_that_moved_while_the_node_was_being_chosen_answers_busy()
        {
            // The selector reads the tree before any handler runs, so it meets the same moving
            // database a handler does. Answering that hop differently turned somebody typing in
            // KeePass into a 500 for whoever happened to be reading at the time.
            var context = Context("GET", "/entries/7b1c", Paired("reader"), null);

            Moving(whileChoosing: true, whileHandling: false).Handle(context);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Does.Contain("busy"));
            Assert.That(_reached, Is.Empty, "the node was never decided, so nothing was dispatched");
        }

        [Test]
        public void A_database_that_moved_while_the_handler_read_it_answers_busy_as_well()
        {
            // The hop the catch always covered, pinned beside the one it did not: what a client
            // is told must not depend on which read the tree happened to move under.
            var context = Context("GET", "/entries/7b1c", Paired("reader"), null);

            Moving(whileChoosing: false, whileHandling: true).Handle(context);

            Assert.That(context.Status, Is.EqualTo(503));
            Assert.That(context.Json, Does.Contain("busy"));
        }

        [Test]
        public void Neither_of_them_is_counted_against_the_plugin_as_a_defect()
        {
            // Through a whole conversation, because the incident is recorded above the router:
            // `GateConversation` counts anything escaping as this plugin's own fault and answers
            // 500. A tree moving is somebody using KeePass, so the count has to stay where it was,
            // and the caller has to get the 503 `PyKeePassGate` reports as `GateBusy` rather than
            // the refusal every other status becomes there.
            var routers = new[]
            {
                Moving(whileChoosing: true, whileHandling: false),
                Moving(whileChoosing: false, whileHandling: true),
            };

            foreach (var moving in routers)
            {
                var incidents = new GateIncidents(_log);
                var stream = new ConversationStream("GET /entries/7b1c HTTP/1.1\r\n\r\n");

                GateConversation.Serve(
                    stream, Paired("reader"), moving, new GateOptions(), _log, null, incidents);

                Assert.That(incidents.Count, Is.EqualTo(0), "nothing here is a defect in this plugin");
                Assert.That(stream.Status, Is.EqualTo(503));
                Assert.That(stream.Body, Does.Contain("busy"));
            }
        }

        /// <summary>What a read raises once its three attempts are spent.</summary>
        private static VaultUnsettled Unsettled()
        {
            return new VaultUnsettled("the database kept changing while it was being read");
        }

        /// <summary>
        /// A router over `readEntry` whose two reads of the tree can each be made to give up, so
        /// that a test can say which hop met a database that would not hold still.
        /// </summary>
        private SpecRouter Moving(bool whileChoosing, bool whileHandling)
        {
            OperationHandler handler = context =>
            {
                if (whileHandling)
                {
                    throw Unsettled();
                }

                _reached.Add("readEntry");
                context.Respond(200, "{}");
            };

            NodeSelector node = context =>
            {
                if (whileChoosing)
                {
                    throw Unsettled();
                }

                return _node;
            };

            return new SpecRouter(
                SpecDocument.Parse(Document),
                new Dictionary<string, OperationHandler>
                {
                    { "readEntry", handler },
                    { "headEntry", handler },
                },
                null,
                new Dictionary<string, NodeSelector>
                {
                    { "readEntry", node },
                    { "headEntry", node },
                },
                new AccessCheck(_audit));
        }

        private GateContext Ask(ClientIdentity client)
        {
            var router = Router(SpecDocument.Parse(Document), "readEntry", "headEntry");
            var context = Context("GET", "/entries/7b1c", client, null);

            router.Handle(context);
            return context;
        }

        /// <summary>
        /// A router over the named operations, each reaching the same handler and asking about
        /// the same node. That is what a get and its head are: two declarations, one handler,
        /// and so one answer to which node a request there is about.
        /// </summary>
        private SpecRouter Router(SpecDocument document, params string[] operationIds)
        {
            // One handler and one selector across the lot, because what is passed here is an
            // operation and the head that answers for it, and the router refuses a pair that
            // reaches two of either. The name recorded is the first, which is the get: it is
            // the only one these tests ever ask for.
            var named = operationIds[0];
            OperationHandler handler = context => { _reached.Add(named); context.Respond(200, "{}"); };
            NodeSelector node = context => _node;

            var handlers = new Dictionary<string, OperationHandler>();
            var nodes = new Dictionary<string, NodeSelector>();

            foreach (var operationId in operationIds)
            {
                handlers.Add(operationId, handler);
                nodes.Add(operationId, node);
            }

            return new SpecRouter(document, handlers, null, nodes, new AccessCheck(_audit));
        }

        private static void Nothing(IGateContext context)
        {
        }

        private static ClientIdentity Paired(params string[] profiles)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, profiles);
            }
        }

        private static GateContext Context(string method, string path, ClientIdentity client, string body)
        {
            var text = body ?? string.Empty;
            var head = new HttpRequestHead(
                method,
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                text.Length);

            return new GateContext(head, text, client);
        }
    }
}
