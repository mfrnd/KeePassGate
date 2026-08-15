using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using KeePassGate.Acl;
using KeePassGate.Authority;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Pairing;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The router's shape rather than its contents: a route that dispatches, a refusal that
    /// happens before dispatch, and a terminal 404 for anything the document does not declare. The
    /// table it is exercised against is the real one, so what is asserted here is the mechanism.
    /// </summary>
    [TestFixture]
    public sealed class SpecRouterTests
    {
        private SpecRouter _router;

        [SetUp]
        public void SetUp()
        {
            _router = GateOperations.Router("9.9.9");
        }

        [Test]
        public void A_paired_client_asking_for_the_status_gets_it()
        {
            var context = Request("GET", GateOperations.StatusPath, Paired("db-digest"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(context.Json, Does.Contain("\"ok\":true"));
            Assert.That(context.Json, Does.Contain("\"version\":\"9.9.9\""));
        }

        [Test]
        public void The_status_names_the_database_the_connection_reached()
        {
            var databaseId = new string('a', 64);
            var context = Request("GET", GateOperations.StatusPath, Paired(databaseId));

            _router.Handle(context);

            Assert.That(context.Json, Does.Contain("\"database\":\"" + CertificateDigest.Shorten(databaseId) + "\""));
        }

        [Test]
        public void An_unpaired_client_is_refused_before_anything_is_dispatched()
        {
            var context = Request("GET", GateOperations.StatusPath, ClientIdentity.Stranger("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Does.Contain("unpaired"));
        }

        [Test]
        public void An_unpaired_client_is_refused_on_every_path_including_ones_that_do_not_exist()
        {
            foreach (var path in new[] { "/", "/gate/status", "/anything", "/pairing", "/gate/pair/hello" })
            {
                var context = Request("GET", path, ClientIdentity.Stranger("db"));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(401), "on " + path);
            }
        }

        [Test]
        public void A_router_with_no_pairing_says_no_more_about_those_paths_than_any_other()
        {
            // The identity refusal cannot come first, because pairing exists to serve somebody who
            // has nothing. What replaces it is that the document says which operations need no
            // certificate, and a build with no pairing behind them answers what every other path
            // answers this caller, before anything has looked at what was sent. Saying `404` here
            // while every other path said `401` made this one prefix the only one in the API that
            // replied differently, which is all an unadmitted caller needs to find it.
            foreach (var path in new[] { "/pair/hello", "/pair/request" })
            {
                var context = Request("POST", path, ClientIdentity.Stranger("db"));

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(401), "on " + path);
            }
        }

        [Test]
        public void A_path_under_pair_that_the_document_does_not_declare_is_not_a_pairing_path()
        {
            // `/pair/` is not a prefix that means anything any more. An undeclared path is
            // undeclared, and a stranger gets what a stranger gets everywhere.
            var context = Request("POST", "/pair/anything", ClientIdentity.Stranger("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401));
        }

        [Test]
        public void A_declared_path_asked_for_with_the_wrong_method_is_a_405()
        {
            foreach (var method in new[] { "GET", "PUT", "DELETE" })
            {
                var context = Request(method, "/pair/hello", ClientIdentity.Stranger("db"));

                _router.Handle(context);

                // 405 rather than the refusal a stranger gets elsewhere, because these two routes
                // are declared in a document that ships inside the plugin. Nothing is revealed by
                // saying so that reading the document would not.
                Assert.That(context.Status, Is.EqualTo(405), "on " + method);

                // RFC 9110 section 15.5.6 requires it, and the value is the document's own
                // answer about that path rather than anything a handler assembled.
                Assert.That(context.Headers, Is.Not.Null, "on " + method);
                Assert.That(context.Headers["Allow"], Is.EqualTo("POST"), "on " + method);
            }
        }

        [Test]
        public void A_paired_client_asking_for_the_wrong_method_on_a_route_it_may_reach_gets_405()
        {
            var context = Request("DELETE", GateOperations.StatusPath, Paired("db-digest"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(405));
        }

        [TestCase("POST", "/gate/status")]
        [TestCase("PUT", "/gate/status")]
        [TestCase("DELETE", "/gate/status")]
        [TestCase("PATCH", "/gate/status")]
        [TestCase("POST", "/entries/7b1c0000000000000000000000000000")]
        [TestCase("PATCH", "/entries/7b1c0000000000000000000000000000")]
        [TestCase("DELETE", "/entries/7b1c0000000000000000000000000000/fields")]
        public void A_method_a_private_path_does_not_serve_tells_a_stranger_no_more_than_any_other(
            string method, string path)
        {
            // The other half of the refusal every path gives a caller with no certificate. A 405
            // here would name the methods the path does serve, which is the path answering that it
            // is there: the identity refusal has to come first for a method the document does not
            // declare, exactly as it does for one it does.
            var stranger = Request(method, path, ClientIdentity.Stranger("db"));

            _router.Handle(stranger);

            Assert.That(stranger.Status, Is.EqualTo(401), "on " + method + " " + path);
            Assert.That(stranger.Json, Does.Contain("unpaired"));
            Assert.That(stranger.Headers, Is.Null, "an Allow field is a list of what the path serves");

            // The same request from a client the database admitted, so that what is pinned is the
            // order the two refusals come in rather than the 405 having gone away everywhere.
            var admitted = Request(method, path, Paired("db"));

            _router.Handle(admitted);

            Assert.That(admitted.Status, Is.EqualTo(405), "on " + method + " " + path);
            Assert.That(admitted.Headers["Allow"], Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void An_operation_reaches_the_handler_bound_to_its_name_and_nothing_else_does()
        {
            var reached = new RecordingHandler();

            // Every operation the document declares, with the two this test watches replaced.
            // Naming only those would fail the startup assertion, which is the assertion working.
            var handlers = new Dictionary<string, OperationHandler>(
                GateOperations.Handlers("9.9.9", null).ToDictionary(p => p.Key, p => p.Value),
                StringComparer.Ordinal)
            {
                [GateOperations.Status] = reached.Handle,
                [GateOperations.HeadStatus] = reached.Handle,
                [GateOperations.PairHello] = reached.Handle,
            };

            // No guards, because this is about which handler a name reaches. With them, a build
            // assembled without pairing answers before the handler, which is a different test.
            var router = new SpecRouter(
                GateOperations.Document,
                handlers,
                null,
                GateOperations.Nodes(),
                new AccessCheck(null));

            router.Handle(Request("POST", "/pair/hello", ClientIdentity.Stranger("db"), Hello));

            Assert.That(reached.Paths, Is.EqualTo(new[] { "/pair/hello" }));

            // A stranger reaches the unauthenticated operations and nothing else, which is the
            // identity refusal happening in one place before dispatch rather than in each handler.
            var status = Request("GET", GateOperations.StatusPath, ClientIdentity.Stranger("db"));
            router.Handle(status);

            Assert.That(status.Status, Is.EqualTo(401));
            Assert.That(reached.Paths.Count, Is.EqualTo(1), "the status operation was never run");
        }

        [Test]
        public void Every_operation_it_declares_has_something_behind_it()
        {
            // The startup assertion, asked as a test so that a route added to the document without
            // a handler fails here rather than the first time somebody loads the plugin.
            Assert.DoesNotThrow(() => GateOperations.Router("0.0.0", null));
        }

        [Test]
        public void A_head_bound_apart_from_its_own_get_stops_the_plugin_loading()
        {
            // The document holds the two to declaring the same thing. This holds them to being
            // the same thing: one handler is what makes "identical to its get, without the
            // content" true by construction, and it is worth nothing if the two can be bound
            // apart.
            var raised = Assert.Throws<InvalidOperationException>(
                () => new SpecRouter(GateOperations.Document, new Dictionary<string, OperationHandler>(
                    GateOperations.Handlers("9.9.9", null).ToDictionary(p => p.Key, p => p.Value),
                    StringComparer.Ordinal)
                {
                    [GateOperations.HeadStatus] = Nothing,
                }));

            Assert.That(raised.Message, Does.Contain("reach different handlers"));
        }

        [Test]
        public void A_head_guarded_apart_from_its_own_get_stops_the_plugin_loading()
        {
            // A guard is looked up under the operation that matched, so one on the `get` alone
            // left the `head` running the shared handler for a connection the `get` refuses.
            OperationGuard refuses = context => false;

            var raised = Assert.Throws<InvalidOperationException>(
                () => new SpecRouter(
                    GateOperations.Document,
                    GateOperations.Handlers("9.9.9", null),
                    new Dictionary<string, OperationGuard> { { GateOperations.Status, refuses } }));

            Assert.That(raised.Message, Does.Contain("guarded differently"));
        }

        [Test]
        public void A_declared_operation_with_nothing_behind_it_stops_the_plugin_loading()
        {
            // The whole point of binding to names rather than to paths: the document and the
            // handlers are held to each other once, at startup, instead of disagreeing later on
            // somebody's connection.
            var missing = new Dictionary<string, OperationHandler>
            {
                { GateOperations.Status, Nothing },
                { GateOperations.HeadStatus, Nothing },
            };

            var raised = Assert.Throws<InvalidOperationException>(
                () => new SpecRouter(GateOperations.Document, missing));

            Assert.That(raised.Message, Does.Contain(GateOperations.PairHello));
            Assert.That(raised.Message, Does.Contain("nothing handles it"));
        }

        [Test]
        public void A_handler_the_document_does_not_declare_stops_it_too()
        {
            var extra = new Dictionary<string, OperationHandler>(
                GateOperations.Handlers("9.9.9", null).ToDictionary(p => p.Key, p => p.Value))
            {
                { "readEverything", Nothing },
            };

            var raised = Assert.Throws<InvalidOperationException>(
                () => new SpecRouter(GateOperations.Document, extra));

            Assert.That(raised.Message, Does.Contain("readEverything"));
            Assert.That(raised.Message, Does.Contain("nothing can reach it"));
        }

        private static void Nothing(IGateContext context)
        {
            context.Respond(200, "{}");
        }

        [Test]
        public void A_connection_that_arrived_on_an_invitation_reaches_nothing_but_pairing()
        {
            // `PAIRING.md` §302: the pairing path serves the pairing routes and nothing else. A
            // client the database already admits can compute the name from an invitation it also
            // holds, and its certificate is judged there the way it is judged anywhere, so every
            // ordinary route used to answer on a connection whose deadline is the pairing budget.
            // It gains no permission by that, because the same certificate reaches the same routes
            // on its own name. What it gains is the six minutes on the thread that only a
            // connection waiting on a person is supposed to have.
            using (var target = TestAuthority.New())
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, NullGateLog.Instance))
            {
                var invitation = book.Invite(target.DatabaseId);
                var context = Request("GET", GateOperations.StatusPath, Paired(target.DatabaseId));

                new PairingConnection(
                    invitation,
                    target,
                    PairingProof.Binding(target.Host.CertificateDer),
                    new byte[SniName.NonceLength],
                    "127.0.0.1:60001").PutOn(context);

                _router.Handle(context);

                // The 404 an admitted client gets for anything it may not have, which is what it
                // gets for a route that is not there. It is admitted, so it is told the difference:
                // nothing is withheld from it that asking on its own name would not answer.
                Assert.That(context.Status, Is.EqualTo(404));
            }
        }

        [Test]
        public void A_query_parameter_the_document_does_not_declare_makes_the_target_undeclared()
        {
            // The transport splits a query off and parses it, and nothing read the answer, so
            // `/gate/status?anything=1` was served as `/gate/status`: a caller scoping a request
            // with a word this API never declared got the unscoped answer and was told nothing.
            var context = Request("GET", GateOperations.StatusPath, Paired("db"), string.Empty,
                new Dictionary<string, string> { { "anything", "1" } });

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404));
            Assert.That(context.Json, Does.Contain("no such route"));
        }

        [Test]
        public void A_query_nothing_on_the_path_declares_is_undeclared_whatever_the_method()
        {
            // The query was asked about only once a method had matched, so a method the path does
            // not serve answered `405` and named what it does serve, for a target the document does
            // not declare at any method.
            var query = new Dictionary<string, string> { { "anything", "1" } };

            var admitted = Request("DELETE", GateOperations.StatusPath, Paired("db"), string.Empty, query);
            var stranger = Request("GET", "/pair/hello", ClientIdentity.Stranger("db"), string.Empty, query);

            _router.Handle(admitted);
            _router.Handle(stranger);

            Assert.That(admitted.Status, Is.EqualTo(404));
            Assert.That(admitted.Headers, Is.Null, "an Allow field is a list of what the path serves");
            Assert.That(stranger.Status, Is.EqualTo(401), "on a public path, what every caller with no certificate is told");
        }

        [Test]
        public void A_body_the_operation_does_not_declare_is_refused_as_an_undeclared_request()
        {
            // Told what a query nobody declared is told: `404` to a client the database admits
            // and the uniform `401` to anybody else, and nothing about the body either way.
            var admitted = Request("GET", GateOperations.StatusPath, Paired("db"), "{\"group\":\"x\"}");
            var stranger = Request("GET", GateOperations.StatusPath, ClientIdentity.Stranger("db"), "{\"group\":\"x\"}");

            _router.Handle(admitted);
            _router.Handle(stranger);

            Assert.That(admitted.Status, Is.EqualTo(404));
            Assert.That(admitted.Json, Does.Contain("no such route"));
            Assert.That(stranger.Status, Is.EqualTo(401));
        }

        [Test]
        public void A_query_another_method_on_the_path_declares_still_names_the_methods()
        {
            // A name the path does declare, at a method that was not asked for, is a target the
            // document does declare, so it is the `405` it always was.
            var document = SpecDocument.Parse(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
                "\"/a\":{\"parameters\":[{\"name\":\"limit\",\"in\":\"query\",\"schema\":{\"type\":\"string\"}}]," +
                "\"get\":{\"operationId\":\"readA\",\"x-kpg-verb\":\"none\",\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headA\",\"x-kpg-verb\":\"none\",\"responses\":{\"200\":{\"description\":\"o\"}}}}}}");
            OperationHandler handler = answering => answering.Respond(200, "{}");
            var router = new SpecRouter(document, new Dictionary<string, OperationHandler>
            {
                { "readA", handler },
                { "headA", handler },
            });

            var declared = Request("DELETE", "/a", Paired("db"), string.Empty,
                new Dictionary<string, string> { { "limit", "5" } });
            var undeclared = Request("DELETE", "/a", Paired("db"), string.Empty,
                new Dictionary<string, string> { { "since", "5" } });

            router.Handle(declared);
            router.Handle(undeclared);

            Assert.That(declared.Status, Is.EqualTo(405));
            Assert.That(declared.Headers["Allow"], Is.EqualTo("GET, HEAD"));
            Assert.That(undeclared.Status, Is.EqualTo(404));
        }

        [Test]
        public void A_stranger_sending_one_is_told_what_every_caller_with_no_certificate_is_told()
        {
            // Refused as the undeclared target it is, which means the refusal is the uniform one
            // rather than something that would tell a stranger the path is there.
            var context = Request("GET", GateOperations.StatusPath, ClientIdentity.Stranger("db"),
                string.Empty, new Dictionary<string, string> { { "anything", "1" } });

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401));
            Assert.That(context.Json, Does.Contain("unpaired"));
        }

        [Test]
        public void The_one_query_parameter_is_the_expiry_flag_on_the_two_entry_listings()
        {
            // `API.md` 407: scoping is a path here, and filtering and paging were cut but for this
            // one flag. The check reads the document rather than refusing every query for all
            // time, so this is what says the document has not quietly grown another: a name
            // declared is one the router lets through to a handler.
            var declaring = GateOperations.Document.Operations.Where(o => o.Query.Count > 0).ToList();

            Assert.That(
                declaring.Select(o => o.OperationId).OrderBy(name => name, StringComparer.Ordinal),
                Is.EqualTo(new[] { "headEntries", "headGroupEntries", "listEntries", "listGroupEntries" }));

            foreach (var operation in declaring)
            {
                Assert.That(operation.Query, Is.EquivalentTo(new[] { "includeExpired" }), operation.OperationId);
            }
        }

        [Test]
        public void The_same_client_on_its_own_name_still_gets_the_status()
        {
            // The other half of the test above: what is refused is the connection, not the client.
            var context = Request("GET", GateOperations.StatusPath, Paired("db-digest"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200));
        }

        [Test]
        public void A_path_that_does_not_exist_is_a_404()
        {
            var context = Request("GET", "/secrets", Paired("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404));
        }

        [TestCase("POST")]
        [TestCase("PUT")]
        [TestCase("DELETE")]
        public void The_status_route_answers_to_GET_and_HEAD_and_nothing_else(string method)
        {
            var context = Request(method, GateOperations.StatusPath, Paired("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(405));

            // RFC 9110 section 15.5.6 requires it, and a client that cannot see what the path
            // does serve has to find out by asking, one method at a time. Both halves of the
            // pair, in the order the document declares them: a `head` is a method this path
            // serves, so a client told what it may ask for is told about it.
            Assert.That(context.Headers, Is.Not.Null, "a 405 has to say what is allowed");
            Assert.That(context.Headers["Allow"], Is.EqualTo("GET, HEAD"));
        }

        [Test]
        public void Nothing_it_answers_says_anything_about_the_database_contents()
        {
            var context = Request("GET", GateOperations.StatusPath, Paired("db"));

            _router.Handle(context);

            Assert.That(context.Json, Does.Not.Contain("password").IgnoreCase);
            Assert.That(context.Json.Length, Is.LessThan(200));
        }

        [Test]
        public void A_missing_context_is_refused()
        {
            Assert.Throws<ArgumentNullException>(() => _router.Handle(null));
        }

        /// <summary>A pairing handler that only says what it was given.</summary>
        private sealed class RecordingHandler
        {
            private readonly List<string> _paths = new List<string>();

            public IReadOnlyList<string> Paths
            {
                get { return _paths; }
            }

            public void Handle(IGateContext context)
            {
                _paths.Add(context.Path);
                context.Respond(200, "{}");
            }
        }

        private static ClientIdentity Paired(string databaseId)
        {
            using (var authority = TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired(databaseId, client, new List<string>());
            }
        }

        [Test]
        public void A_HEAD_runs_the_GET_and_answers_without_its_content()
        {
            // Declared nowhere and served everywhere GET is. The operation runs rather than being
            // skipped, so a HEAD reports what a GET would report: anything else would be a second
            // implementation that could disagree about whether something exists.
            //
            // The answer it produced is left intact here. Dropping it at this layer is what made
            // the length disagree with the content; `HttpResponse.Write` is where a HEAD stops
            // carrying either, and `HttpResponseTests` is where that is asserted.
            var context = Request("HEAD", GateOperations.StatusPath, Paired("db-digest"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(200));
            Assert.That(context.Json, Is.Not.Empty, "the operation ran, exactly as the GET does");
        }

        [Test]
        public void A_HEAD_is_refused_wherever_a_GET_would_be()
        {
            var context = Request("HEAD", GateOperations.StatusPath, ClientIdentity.Stranger("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(401), "it is a GET in every respect but the content");
        }

        [Test]
        public void A_HEAD_on_a_route_that_only_takes_POST_is_a_405()
        {
            var context = Request("HEAD", "/pair/hello", ClientIdentity.Stranger("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(405));
        }

        [Test]
        public void The_methods_a_405_lists_are_in_the_order_the_document_declares_them()
        {
            // Every other list asserted here is alphabetical as well, so sorting it would have
            // passed. This path serves four, and the document declares them get, head, put, delete.
            var context = Request("PATCH", "/groups/root", Paired("db"));

            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(405));
            Assert.That(context.Headers["Allow"], Is.EqualTo("GET, HEAD, PUT, DELETE"));
        }

        [Test]
        public void A_wrong_method_on_a_private_path_over_an_invitation_is_refused_as_undeclared()
        {
            // A connection that arrived on an invitation reaches the pairing routes and nothing
            // else, and that holds for a method the path does not serve as much as for one it
            // does: the certificate is admitted, and on this connection it is still told only
            // what an undeclared path tells it, not the methods the path would have served.
            using (var target = TestAuthority.New())
            using (var book = new InvitationBook(TimeSpan.FromMinutes(10), null, NullGateLog.Instance))
            {
                var invitation = book.Invite(target.DatabaseId);
                var context = Request("DELETE", GateOperations.StatusPath, Paired(target.DatabaseId));

                new PairingConnection(
                    invitation,
                    target,
                    PairingProof.Binding(target.Host.CertificateDer),
                    new byte[SniName.NonceLength],
                    "127.0.0.1:60001").PutOn(context);

                _router.Handle(context);

                Assert.That(context.Status, Is.EqualTo(404));
                Assert.That(context.Headers, Is.Null, "an Allow field is a list of what the path serves");
            }
        }

        [Test]
        public void The_parameters_a_path_captured_are_left_where_a_handler_can_read_them()
        {
            var document = SpecDocument.Parse(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
                "\"/entries/{uuid}\":{\"get\":{\"operationId\":\"readEntry\",\"x-kpg-verb\":\"none\",\"parameters\":[{\"name\":\"uuid\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
                "\"x-kpg-unauthenticated\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headEntry\",\"x-kpg-verb\":\"none\",\"parameters\":[{\"name\":\"uuid\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}]," +
                "\"x-kpg-unauthenticated\":true,\"responses\":{\"200\":{\"description\":\"o\"}}}}}}");

            IReadOnlyDictionary<string, string> captured = null;
            OperationHandler capture = answering =>
            {
                captured = (IReadOnlyDictionary<string, string>)answering.Items[SpecRouter.ParametersItem];
                answering.Respond(200, "{}");
            };

            var router = new SpecRouter(document, new Dictionary<string, OperationHandler>
            {
                { "readEntry", capture },
                { "headEntry", capture },
            });

            router.Handle(Request("GET", "/entries/abcdef01", ClientIdentity.Stranger("db")));

            Assert.That(captured["uuid"], Is.EqualTo("abcdef01"));
        }

        [Test]
        public void A_segment_its_parameter_does_not_allow_is_a_path_the_document_does_not_declare()
        {
            // The document said a uuid is 32 characters and nothing held a request to it: three
            // characters matched the route and were left for a lookup to answer. Refused as an
            // undeclared path is, so not with a `405` naming what the path serves either.
            var document = SpecDocument.Parse(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
                "\"/entries/{uuid}\":{\"parameters\":[{\"name\":\"uuid\",\"in\":\"path\",\"required\":true," +
                "\"schema\":{\"type\":\"string\",\"minLength\":32,\"maxLength\":32}}]," +
                "\"get\":{\"operationId\":\"readEntry\",\"x-kpg-verb\":\"none\",\"responses\":{\"200\":{\"description\":\"o\"}}}," +
                "\"head\":{\"operationId\":\"headEntry\",\"x-kpg-verb\":\"none\",\"responses\":{\"200\":{\"description\":\"o\"}}}}}}");

            var reached = 0;
            OperationHandler handler = answering =>
            {
                reached++;
                answering.Respond(200, "{}");
            };

            var router = new SpecRouter(document, new Dictionary<string, OperationHandler>
            {
                { "readEntry", handler },
                { "headEntry", handler },
            });

            var shortened = Request("GET", "/entries/" + new string('a', 31), Paired("db"));
            var wrongMethod = Request("PATCH", "/entries/" + new string('a', 31), Paired("db"));
            var whole = Request("GET", "/entries/" + new string('a', 32), Paired("db"));

            router.Handle(shortened);
            router.Handle(wrongMethod);
            router.Handle(whole);

            Assert.That(shortened.Status, Is.EqualTo(404));
            Assert.That(shortened.Json, Does.Contain("no such route"));
            Assert.That(wrongMethod.Status, Is.EqualTo(404), "not a 405: there is no such path");
            Assert.That(whole.Status, Is.EqualTo(200));
            Assert.That(reached, Is.EqualTo(1), "only the segment the schema allows reached the handler");
        }

        /// <summary>A body the document accepts, so that a routing test measures routing.</summary>
        private const string Hello = "{\"proof\":\"AAAA\"}";

        private static GateContext Request(string method, string path, ClientIdentity client)
        {
            return Request(method, path, client, string.Empty);
        }

        private static GateContext Request(string method, string path, ClientIdentity client, string body)
        {
            return Request(method, path, client, body, new Dictionary<string, string>());
        }

        private static GateContext Request(
            string method,
            string path,
            ClientIdentity client,
            string body,
            IReadOnlyDictionary<string, string> query)
        {
            var head = new HttpRequestHead(
                method,
                path,
                query,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                body == null ? 0 : body.Length);

            return new GateContext(head, body ?? string.Empty, client);
        }
    }

    /// <summary>
    /// Turning a presented certificate into an identity, which is the whole of authentication:
    /// the anchor pin, the usage, the dates, and the record that grants.
    /// </summary>
    [TestFixture]
    public sealed class ClientCredentialsTests
    {
        private TestAuthority _ours;
        private TestAuthority _theirs;
        private RecordingGateLog _log;

        [SetUp]
        public void SetUp()
        {
            _ours = TestAuthority.New();
            _theirs = TestAuthority.New();
            _log = new RecordingGateLog();
        }

        [TearDown]
        public void TearDown()
        {
            _ours.Dispose();
            _theirs.Dispose();
        }

        [Test]
        public void A_certificate_from_this_database_with_a_record_is_paired()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious"))
            {
                _ours.Admit(certificate);

                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.True);
                Assert.That(identity.Subject, Is.EqualTo("CN=agent-fictitious"));
                Assert.That(identity.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)));
                Assert.That(identity.DatabaseId, Is.EqualTo(_ours.DatabaseId));
            }
        }

        [Test]
        public void A_certificate_with_no_record_is_a_stranger_however_good_it_is()
        {
            // The rule the whole design turns on. This certificate chains, carries the right usage
            // and is inside its dates, and it opens nothing.
            using (var certificate = _ours.IssueClient("agent-fictitious", "ops"))
            {
                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("no admission record")), Is.True);
                Assert.That(identity.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)),
                    "a certificate this authority issued is still one the audit can name");
                Assert.That(identity.Refusal, Is.EqualTo("this certificate has no admission record"));
            }
        }

        [Test]
        public void Revoking_the_record_refuses_the_same_certificate_at_once()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious", "ops"))
            {
                _ours.Admit(certificate, "ops");
                Assume.That(ClientCredentials.For(certificate, _ours, _log).IsPaired, Is.True);

                _ours.Revoke(certificate);

                Assert.That(ClientCredentials.For(certificate, _ours, _log).IsPaired, Is.False);
            }
        }

        [Test]
        public void A_revoked_certificate_stays_refused_when_its_record_comes_back()
        {
            // What a stale copy of the file merged in does: the record is back, profiles and all.
            // The revocation beside it is what the gate reads, and it never moves.
            using (var certificate = _ours.IssueClient("agent-fictitious", "ops"))
            {
                _ours.Admit(certificate, "ops");
                Assume.That(_ours.Revoke(certificate), Is.True);

                _ours.Admit(certificate, "ops");

                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(identity.AdmissionKey, Is.EqualTo(CertificateDigest.Of(certificate)),
                    "refused as one this authority issued, so the audit can name it");
            }
        }

        [Test]
        public void The_profiles_come_from_the_record_and_not_from_the_certificate()
        {
            // A certificate claiming more than its record grants is a certificate that grants what
            // its record says.
            using (var certificate = _ours.IssueClient("agent-fictitious", "ops", "admin"))
            {
                _ours.Admit(certificate, "readonly");

                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.Profiles, Is.EqualTo(new[] { "readonly" }));
            }
        }

        [Test]
        public void A_record_granting_nothing_is_still_an_admission()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious"))
            {
                _ours.Admit(certificate);

                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.True, "it is admitted");
                Assert.That(identity.Profiles, Is.Empty, "and it can reach nothing, which is not the same thing");
            }
        }

        [Test]
        public void A_certificate_outside_its_dates_is_refused_even_with_a_record()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious"))
            {
                _ours.Admit(certificate);
                var afterItExpires = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)
                    .AddDays(1);

                var identity = ClientCredentials.For(certificate, _ours, afterItExpires, _log);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(_log.Warnings.Any(w => w.Message.Contains("outside its dates")), Is.True);
                Assert.That(identity.Refusal, Is.EqualTo("this certificate is outside its dates"));
            }
        }

        [Test]
        public void A_clock_a_few_minutes_behind_still_admits_a_fresh_certificate()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious"))
            {
                _ours.Admit(certificate);
                // Measured from issuance, which is where the documented five minutes is measured
                // from. `notBefore` is already `AuthorityIssuer.Backdate` earlier, so taking four
                // minutes off it asked for nine minutes of tolerance and passed only while a second
                // allowance on the verifier's side was quietly doubling the number.
                var issued = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero)
                    + AuthorityIssuer.Backdate;
                var slightlyBehind = issued.AddMinutes(-4);

                Assert.That(ClientCredentials.For(certificate, _ours, slightlyBehind, _log).IsPaired, Is.True);
            }
        }

        [Test]
        public void A_clock_a_long_way_behind_does_not()
        {
            using (var certificate = _ours.IssueClient("agent-fictitious"))
            {
                _ours.Admit(certificate);
                var wayBehind = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero)
                    .AddHours(-1);

                Assert.That(ClientCredentials.For(certificate, _ours, wayBehind, _log).IsPaired, Is.False);
            }
        }

        [Test]
        public void A_record_in_another_database_admits_nobody_here()
        {
            // Records are only meaningful under the authority in the same file.
            using (var certificate = _theirs.IssueClient("agent-fictitious"))
            {
                _theirs.Admit(certificate, "ops");

                Assert.That(ClientCredentials.For(certificate, _ours, _log).IsPaired, Is.False);
            }
        }

        [Test]
        public void A_certificate_from_another_authority_is_a_stranger()
        {
            using (var certificate = _theirs.IssueClient("agent-fictitious"))
            {
                // Admitted into *this* database, so the missing record is not what refuses it
                // and the anchor pin is left alone to do the refusing. Writing the record into
                // `_theirs` left the certificate unknown here either way, and this passed with
                // the pin deleted.
                _ours.Admit(certificate, "ops");
                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.False);
                Assert.That(_log.Warnings, Is.Not.Empty);
                Assert.That(identity.AdmissionKey, Is.Null, "a foreign certificate names nobody issued here");
                Assert.That(identity.Refusal, Is.Null);
            }
        }

        [Test]
        public void A_host_certificate_presented_as_a_client_is_a_stranger()
        {
            // The same authority signs both, and hostname verification is deliberately abandoned,
            // so the extended key usage is the only thing that separates them.
            var identity = ClientCredentials.For(_ours.HostCertificateAsClient(), _ours, _log);

            Assert.That(identity.IsPaired, Is.False);
        }

        [Test]
        public void No_certificate_at_all_is_a_stranger_rather_than_an_error()
        {
            var identity = ClientCredentials.For(null, _ours, _log);

            Assert.That(identity.IsPaired, Is.False);
            Assert.That(identity.DatabaseId, Is.EqualTo(_ours.DatabaseId));
            Assert.That(identity.Subject, Is.Null);
        }

        [Test]
        public void A_certificate_from_a_foreign_authority_is_refused()
        {
            // A real client certificate, carrying `clientAuth`, issued by an authority that is not
            // this database's, and admitted into this one. The usage check would pass it and the
            // record would admit it, so the only thing that can refuse it is the anchor pin, and
            // this fails if the pin is removed. Without the record it passed with the pin deleted,
            // since a certificate nobody admitted is refused either way. Asserted with a
            // certificate the usage check accepts on purpose: shown one with no usage at all, this
            // passes whichever check runs first.
            using (var foreign = TestAuthority.New())
            using (var certificate = foreign.IssueClient("agent-elsewhere"))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(certificate), Is.True,
                    "the usage check has to accept it, or the pin is not what refused it");

                _ours.Admit(certificate, "ops");
                var identity = ClientCredentials.For(certificate, _ours, _log);

                Assert.That(identity.IsPaired, Is.False);
            }
        }

        [Test]
        public void A_certificate_with_no_extended_key_usage_is_refused()
        {
            // Unconstrained is not the same as permitted. Asked of the check itself, because a
            // certificate carrying the wrong usage and the right issuer is not something the
            // authority will issue, and routing this through a connection would only ever prove
            // that the anchor pin runs first.
            using (var unconstrained = SelfSigned(null))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(unconstrained), Is.False);
            }
        }

        [Test]
        public void A_certificate_for_serving_rather_than_connecting_is_refused()
        {
            // The other half of the same rule: an extended key usage that is present and says
            // something else is a certificate for a different job.
            using (var serving = SelfSigned("1.3.6.1.5.5.7.3.1"))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(serving), Is.False);
            }
        }

        [Test]
        public void A_certificate_that_says_client_authentication_passes_the_usage_check()
        {
            using (var connecting = SelfSigned(AuthorityIssuer.ClientAuthOid))
            {
                Assert.That(ClientCredentials.IsForClientAuthentication(connecting), Is.True);
            }
        }

        /// <summary>
        /// A certificate carrying one extended key usage, or none at all when given null.
        /// </summary>
        /// <remarks>
        /// Built here rather than issued, because the authority always writes `clientAuth` into a
        /// client certificate and there is no way to ask it for one without.
        /// </remarks>
        private static X509Certificate2 SelfSigned(string usageOid)
        {
            using (var key = System.Security.Cryptography.RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    "CN=invented",
                    key,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                if (usageOid != null)
                {
                    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                        new System.Security.Cryptography.OidCollection { new System.Security.Cryptography.Oid(usageOid) }, false));
                }

                return request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            }
        }
    }
}
