using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Testing;
using KeePassGate.Tests.Fakes;
using KeePassGate.Transport;
using KeePassGate.Vault;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// `getGroupAccess` and `getEntryAccess`, through the real router against a database in a
    /// dictionary: what a client may do at a node, one mask per profile it holds.
    /// </summary>
    /// <remarks>
    /// Everything is invented: the groups, the entries, the profiles.
    /// </remarks>
    [TestFixture]
    public sealed class AccessOperationTests
    {
        private FakeVault _vault;
        private RecordingAudit _audit;
        private SpecRouter _router;
        private string _group;
        private string _entry;

        [SetUp]
        public void SetUp()
        {
            _vault = new FakeVault();
            _group = _vault.Add(_vault.RootAlias, "Fictitious");
            _entry = _vault.AddEntry(_group, "an invented service");
            _audit = new RecordingAudit();
            _router = GateOperations.Router("9.9.9", null, new AccessCheck(_audit));
        }

        [Test]
        public void A_group_answers_with_a_mask_for_each_profile_the_client_holds()
        {
            _vault.Granting(
                _vault.RootAlias,
                Rules.Granting("reader", "RLP", "uploader", "CA"));

            var answer = Send("GET", "/groups/" + _group + "/access", "reader", "uploader");

            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);
            Assert.That(answer.Json, Is.EqualTo("{\"grants\":{\"reader\":\".R...L.P\",\"uploader\":\"C.....A.\"}}"));
        }

        [Test]
        public void Reached_without_the_routers_answer_it_answers_as_an_absent_node()
        {
            // Only an operation the router decided as `any` carries the grants, so the handler
            // reached without them is a defect, answered as every absence is.
            var context = Context("GET", "/groups/" + _group + "/access", "reader");

            AccessOperation.Handle(context);

            Assert.That(context.Status, Is.EqualTo(404));
            Assert.That(context.Json, Is.EqualTo(HttpResponse.ErrorBody(SpecRouter.UnreachableError)));
        }

        [Test]
        public void An_entry_that_is_not_there_is_written_down_by_the_identifier_asked_for()
        {
            // The 404 a client is told says nothing about which; the line is the operator's, and
            // without the identifier it could not say which reference a resolve was following.
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));
            var absent = new string('c', 32);

            var answer = Send("GET", "/entries/" + absent, "reader");

            Assert.That(answer.Status, Is.EqualTo(404));
            var line = _audit.Entries.Single();
            Assert.That(line.Node, Is.Null);
            Assert.That(line.Requested, Is.EqualTo(absent));
            Assert.That(line.Line(), Does.Contain("\"requested\":\"" + absent + "\""));
        }

        [Test]
        public void An_entry_that_is_there_is_written_down_by_its_node_alone()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));

            Assert.That(Send("GET", "/entries/" + _entry, "reader").Status, Is.EqualTo(200));
            Assert.That(_audit.Entries.Single().Requested, Is.Null);
            Assert.That(_audit.Entries.Single().Line(), Does.Not.Contain("requested"));
        }

        [TestCase("root", "root")]
        [TestCase("0123456789abcdefABCDEF0123456789", "0123456789abcdefABCDEF0123456789")]
        [TestCase("0123456789abcdefABCDEF012345678", null)]
        [TestCase("0123456789abcdefABCDEF012345678g", null)]
        [TestCase("\"},\"decision\":\"permitted\",\"x\":\"", null)]
        [TestCase(null, null)]
        public void Only_an_identifier_as_the_routes_write_one_reaches_the_line(string requested, string kept)
        {
            Assert.That(AccessCheck.Identifier(requested), Is.EqualTo(kept));
        }

        [Test]
        public void The_root_group_is_asked_by_its_alias()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "L"));

            Assert.That(Grants(Send("GET", "/groups/root/access", "reader"))["reader"], Is.EqualTo(".....L.."));
        }

        [Test]
        public void An_entry_answers_with_the_rule_nearest_to_it()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("editor", "RL"));

            Assert.That(Grants(Send("GET", "/entries/" + _entry + "/access", "editor"))["editor"],
                Is.EqualTo(".R...L.."), "what the root grants reaches down to it");

            _vault.Granting(_entry, Rules.Granting("editor", "RUP"));

            Assert.That(Grants(Send("GET", "/entries/" + _entry + "/access", "editor"))["editor"],
                Is.EqualTo(".RU....P"), "and a rule of its own wins, as for every other question");
        }

        [Test]
        public void One_profile_s_switch_does_not_reach_another_profile_s_verb()
        {
            // The reason for one mask per profile, and the test that fails if they are ever
            // combined: `R` under one profile and `P` under another are not a protected read.
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R", "auditor", "LP"));
            Assume.That(_vault.CreateField(_entry, "Password", "an invented secret", true, VaultTagging.None).Ok, Is.True);

            var grants = Grants(Send("GET", "/entries/" + _entry + "/access", "reader", "auditor"));

            Assert.That(grants["reader"], Is.EqualTo(".R......"));
            Assert.That(grants["auditor"], Is.EqualTo(".....L.P"));
            Assert.That(Send("GET", "/entries/" + _entry, "reader", "auditor").Json,
                Does.Contain("\"Password\":null"), "and a read agrees: the protected value is withheld");
        }

        [Test]
        public void A_profile_granted_nothing_there_is_eight_dots()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));

            Assert.That(Grants(Send("GET", "/entries/" + _entry + "/access", "reader", "editor"))["editor"],
                Is.EqualTo("........"));
        }

        [Test]
        public void A_node_where_no_profile_may_do_anything_is_answered_as_one_that_is_not_there()
        {
            // Absent and refused answer the same `404`, which is what keeps a client that may reach
            // nothing from mapping the database by asking what it may do.
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));
            _vault.Granting(_entry, Rules.Granting("reader", ""));

            var refused = Send("GET", "/entries/" + _entry + "/access", "reader");
            var absent = Send("GET", "/entries/" + new string('0', 32) + "/access", "reader");

            Assert.That(refused.Status, Is.EqualTo(404));
            Assert.That(absent.Status, Is.EqualTo(404));
            Assert.That(refused.Json, Is.EqualTo(absent.Json));
        }

        [Test]
        public void Every_question_is_written_down_once_whichever_way_it_goes()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));

            Send("GET", "/entries/" + _entry + "/access", "reader");
            Send("GET", "/entries/" + _entry + "/access", "nobody");

            Assert.That(_audit.Entries.Select(line => line.OperationId),
                Is.EqualTo(new[] { GateOperations.EntryAccess, GateOperations.EntryAccess }));
            Assert.That(_audit.Entries.Select(line => line.Permitted), Is.EqualTo(new[] { true, false }));
            Assert.That(_audit.Entries.Select(line => line.Node).Distinct(),
                Is.EqualTo(new[] { _vault.NodeOf(_entry).Name }), "both about the entry asked about");
        }

        [Test]
        public void A_HEAD_is_answered_as_its_GET_is()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "L"));

            Assert.That(Send("HEAD", "/groups/" + _group + "/access", "reader").Status, Is.EqualTo(200));
            Assert.That(Send("HEAD", "/groups/" + _group + "/access", "nobody").Status, Is.EqualTo(404));
        }

        [Test]
        public void A_database_that_locks_under_the_question_is_answered_as_locked()
        {
            // Not a node that has gone, so not the `404` one gets: the nodes are all still there.
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));
            _vault.After(0, _vault.Lock);

            var answer = Send("GET", "/entries/" + _entry + "/access", "reader");

            Assert.That(answer.Status, Is.EqualTo(503));
            Assert.That(answer.Json, Is.EqualTo(HttpResponse.ErrorBody("locked")));
            Assert.That(_audit.Entries, Is.Empty, "a lock is not a decision");
        }

        [Test]
        public void Any_other_method_is_told_the_two_this_path_serves()
        {
            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "R"));

            var context = Context("PUT", "/entries/" + _entry + "/access", "reader");
            _router.Handle(context);

            Assert.That(context.Status, Is.EqualTo(405));
            Assert.That(context.Headers["Allow"], Is.EqualTo("GET, HEAD"));
        }

        private static IDictionary<string, string> Grants(Answer answer)
        {
            Assert.That(answer.Status, Is.EqualTo(200), answer.Json);

            using (var reader = new JsonTextReader(new StringReader(answer.Json)) { DateParseHandling = DateParseHandling.None })
            {
                return JObject.Load(reader)["grants"].Children<JProperty>()
                    .ToDictionary(member => member.Name, member => (string)member.Value);
            }
        }

        [Test]
        public void Every_mask_the_answer_can_carry_is_one_the_document_declares()
        {
            // No response schema is enforced, so the pattern the document gives each mask is held
            // here: read from the shipped document rather than copied, against every combination
            // of the eight letters, and against a mask the real router answered with.
            Microsoft.OpenApi.Models.OpenApiDocument document;
            var shipped = typeof(SpecDocument).Assembly;
            using (var stream = shipped.GetManifestResourceStream("KeePassGate.Spec.openapi.yaml"))
            {
                Microsoft.OpenApi.Readers.OpenApiDiagnostic diagnostic;
                document = new Microsoft.OpenApi.Readers.OpenApiStreamReader().Read(stream, out diagnostic);
            }

            var grants = document.Components.Schemas["Access"].Properties["grants"];
            var pattern = grants.AdditionalProperties.Pattern;
            var mask = new System.Text.RegularExpressions.Regex(BodyCheck.Ecma(pattern));
            var verbs = KeePassGate.Acl.AclVerbs.Grantable;

            for (var held = 0; held < 1 << verbs.Count; held++)
            {
                var granted = verbs.Where((verb, at) => (held & (1 << at)) != 0);
                var written = KeePassGate.Acl.VerbMask.Of(granted);

                Assert.That(mask.IsMatch(written), Is.True, written);
            }

            _vault.Granting(_vault.RootAlias, Rules.Granting("reader", "RLP"));
            var answered = Grants(Send("GET", "/groups/root/access", "reader", "other"));
            Assert.That(mask.IsMatch(answered["reader"]), Is.True, answered["reader"]);
            Assert.That(mask.IsMatch(answered["other"]), Is.True, answered["other"]);
        }

        private Answer Send(string method, string path, params string[] profiles)
        {
            var context = Context(method, path, profiles);
            _router.Handle(context);

            return new Answer(context.Status, context.Json);
        }

        private GateContext Context(string method, string path, params string[] profiles)
        {
            var head = new HttpRequestHead(
                method,
                path,
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                0);

            var context = new GateContext(head, string.Empty, Paired(profiles));
            new ServedDatabase(_vault).PutOn(context);
            return context;
        }

        private static ClientIdentity Paired(string[] profiles)
        {
            using (var authority = TestAuthority.New())
            using (var certificate = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("d1e2f3", certificate, profiles);
            }
        }

        private sealed class Answer
        {
            public Answer(int status, string json)
            {
                Status = status;
                Json = json ?? string.Empty;
            }

            public int Status { get; private set; }

            public string Json { get; private set; }
        }
    }
}
