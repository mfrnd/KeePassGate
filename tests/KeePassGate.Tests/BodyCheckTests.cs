using System;
using System.Collections.Generic;
using System.Linq;
using KeePassGate.Pipeline;
using KeePassGate.Spec;
using KeePassGate.Transport;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>What a body has to be, and what it may not be.</summary>
    [TestFixture]
    public sealed class BodyCheckTests
    {
        [Test]
        public void A_body_that_is_what_the_document_declared_passes()
        {
            var schema = Schema();
            var body = JsonBody.Read("{\"name\":\"agent-invented\",\"profiles\":[\"reader\"]}");

            Assert.That(Validate(schema, body), Is.Null);
        }

        [Test]
        public void A_missing_required_member_is_refused()
        {
            Assert.That(Validate(Schema(), JsonBody.Read("{\"profiles\":[]}")), Is.Not.Null);
        }

        [Test]
        public void A_member_the_document_does_not_declare_is_refused()
        {
            // `additionalProperties: false`, which the document is made to declare. Without it a
            // member nobody declared is a member nobody looked at.
            Assert.That(
                Validate(Schema(), JsonBody.Read("{\"name\":\"a\",\"profiles\":[],\"extra\":\"x\"}")),
                Is.Not.Null);
        }

        [Test]
        public void A_string_where_an_array_was_declared_is_refused_and_the_other_way_round()
        {
            Assert.That(Validate(Schema(), JsonBody.Read("{\"name\":\"a\",\"profiles\":\"reader\"}")), Is.Not.Null);
            Assert.That(Validate(Schema(), JsonBody.Read("{\"name\":[\"a\"],\"profiles\":[]}")), Is.Not.Null);
        }

        [Test]
        public void Lengths_are_enforced_at_both_ends()
        {
            Assert.That(Validate(Schema(), JsonBody.Read("{\"name\":\"\",\"profiles\":[]}")), Is.Not.Null);
            Assert.That(
                Validate(Schema(), JsonBody.Read("{\"name\":\"" + new string('x', 65) + "\",\"profiles\":[]}")),
                Is.Not.Null);
        }

        [Test]
        public void A_length_is_counted_in_characters_as_the_document_counts_it()
        {
            // JSON Schema counts characters, and one outside the Basic Multilingual Plane is two
            // UTF-16 units. Counting those refused 33 of them under a `maxLength` of 64.
            var grinning = char.ConvertFromUtf32(0x1F600);
            var most = string.Concat(Enumerable.Repeat(grinning, 64));

            Assert.That(Validate(Schema(), JsonBody.Read("{\"name\":\"" + most + "\",\"profiles\":[]}")), Is.Null);
            Assert.That(
                Validate(Schema(), JsonBody.Read("{\"name\":\"" + most + grinning + "\",\"profiles\":[]}")),
                Is.Not.Null,
                "and one more is still one too many");
        }

        [Test]
        public void An_array_longer_than_declared_is_refused()
        {
            var many = string.Join(",", Enumerable.Repeat("\"p\"", 4));

            Assert.That(
                Validate(Schema(), JsonBody.Read("{\"name\":\"a\",\"profiles\":[" + many + "]}")),
                Is.Not.Null);
        }

        [Test]
        public void A_body_that_is_not_an_object_at_all_is_refused()
        {
            Assert.That(Validate(Schema(), null), Is.Not.Null);
        }

        [Test]
        public void A_keyword_nothing_enforces_is_refused_wherever_it_sits()
        {
            // Four keywords a list of refusals once left out. `readOnly` is
            // the one that costs something: on a request body it says the member must not be
            // sent, and letting it through accepted a member the document forbids. `default` and
            // `nullable` promise a caller something no handler here delivers, and `writeOnly` is
            // named because a document using it expects the distinction enforced somewhere.
            foreach (var member in new[] { Nullable(), ReadOnly(), WriteOnly(), Defaulted() })
            {
                var schema = new OpenApiSchema
                {
                    Type = "object",
                    AdditionalPropertiesAllowed = false,
                    Properties = { { "a", member.Value } },
                };

                Assert.That(BodyCheck.Supports(schema), Does.Contain(member.Key), member.Key);
            }
        }

        [Test]
        public void The_same_keyword_on_the_body_itself_is_refused_too()
        {
            // `Unimplemented` is asked about the object as well as about each member, so a
            // keyword written one level up is not a way past it.
            var schema = new OpenApiSchema
            {
                Type = "object",
                AdditionalPropertiesAllowed = false,
                ReadOnly = true,
                Properties = { { "a", new OpenApiSchema { Type = "string" } } },
            };

            Assert.That(BodyCheck.Supports(schema), Is.EqualTo("readOnly"));
        }

        [TestCaseSource(nameof(Keywords))]
        public void A_keyword_is_allowed_only_on_the_type_it_bounds(string keyword)
        {
            // An allow list per type, so a keyword on a type it does not apply to is refused as
            // well: `maxLength` on an array reads as a bound on it and bounds nothing.
            var text = new OpenApiSchema { Type = "string" };
            Writes[keyword](text);

            var list = new OpenApiSchema { Type = "array", Items = new OpenApiSchema { Type = "string" } };
            if (keyword != "Items")
            {
                Writes[keyword](list);
            }

            var onText = BodyCheck.Supports(Body(text));
            var onList = BodyCheck.Supports(Body(list));

            if (keyword == "MinLength" || keyword == "MaxLength")
            {
                Assert.That(onText, Is.Null, "a string says how long it is");
                Assert.That(onList, Is.Not.Null, "an array does not");
            }
            else if (keyword == "Items" || keyword == "MaxItems")
            {
                Assert.That(onText, Is.Not.Null, "a string holds no items");
                Assert.That(onList, Is.Null, "an array says what it holds and how many");
            }
            else
            {
                Assert.That(onText, Is.Not.Null, "nothing enforces " + keyword);
                Assert.That(onList, Is.Not.Null, "nothing enforces " + keyword);
            }

            // And on the items of an array, which have an allow list of their own: each item is
            // held to its lengths and to nothing else, so anything else there would load and never
            // be checked.
            var item = new OpenApiSchema { Type = "string" };
            if (keyword != "Items")
            {
                Writes[keyword](item);
            }

            var onItems = BodyCheck.Supports(Body(new OpenApiSchema { Type = "array", Items = item }));
            if (keyword == "MinLength" || keyword == "MaxLength" || keyword == "Items")
            {
                Assert.That(onItems, Is.Null, "an item says how long it is");
            }
            else
            {
                Assert.That(onItems, Is.Not.Null, "nothing enforces " + keyword + " on an item");
            }
        }

        [Test]
        public void Every_keyword_the_reader_models_is_either_one_of_those_or_an_annotation()
        {
            // The allow list is only as complete as the reader's own schema object. A reader that
            // learns a keyword fails here, before a document using it loads with nothing enforcing
            // it: give it a writer above, and the test above says whether `BodyCheck` refuses it.
            var annotations = new[]
            {
                "Type", "Title", "Description", "Example", "Deprecated", "ExternalDocs", "Xml",
                "Extensions", "Reference", "UnresolvedReference", "Annotations",
            };

            var modeled = typeof(OpenApiSchema)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Select(p => p.Name);

            Assert.That(modeled, Is.EquivalentTo(annotations.Concat(Writes.Keys)));
        }

        private static IEnumerable<string> Keywords()
        {
            return Writes.Keys;
        }

        /// <summary>
        /// Every property of the reader's schema object that says something about a value, and how
        /// to write it.
        /// </summary>
        private static readonly Dictionary<string, Action<OpenApiSchema>> Writes =
            new Dictionary<string, Action<OpenApiSchema>>(StringComparer.Ordinal)
            {
                { "Pattern", s => s.Pattern = "^[a-f0-9]+$" },
                { "Format", s => s.Format = "uuid" },
                { "Enum", s => s.Enum.Add(new OpenApiString("root")) },
                { "Default", s => s.Default = new OpenApiString("root") },
                { "Nullable", s => s.Nullable = true },
                { "ReadOnly", s => s.ReadOnly = true },
                { "WriteOnly", s => s.WriteOnly = true },
                { "MinLength", s => s.MinLength = 1 },
                { "MaxLength", s => s.MaxLength = 1 },
                { "Items", s => s.Items = new OpenApiSchema { Type = "string" } },
                { "MinItems", s => s.MinItems = 1 },
                { "MaxItems", s => s.MaxItems = 1 },
                { "UniqueItems", s => s.UniqueItems = true },
                { "Properties", s => s.Properties["a"] = new OpenApiSchema { Type = "string" } },
                { "Required", s => s.Required.Add("a") },
                { "AdditionalPropertiesAllowed", s => s.AdditionalPropertiesAllowed = false },
                { "AdditionalProperties", s => s.AdditionalProperties = new OpenApiSchema { Type = "string" } },
                { "MinProperties", s => s.MinProperties = 1 },
                { "MaxProperties", s => s.MaxProperties = 1 },
                { "AllOf", s => s.AllOf.Add(new OpenApiSchema { Type = "string" }) },
                { "AnyOf", s => s.AnyOf.Add(new OpenApiSchema { Type = "string" }) },
                { "OneOf", s => s.OneOf.Add(new OpenApiSchema { Type = "string" }) },
                { "Not", s => s.Not = new OpenApiSchema { Type = "string" } },
                { "Discriminator", s => s.Discriminator = new OpenApiDiscriminator { PropertyName = "a" } },
                { "MultipleOf", s => s.MultipleOf = 2 },
                { "Minimum", s => s.Minimum = 1 },
                { "ExclusiveMinimum", s => s.ExclusiveMinimum = true },
                { "Maximum", s => s.Maximum = 1 },
                { "ExclusiveMaximum", s => s.ExclusiveMaximum = true },
            };

        /// <summary>A body whose one member is the schema being asked about.</summary>
        private static OpenApiSchema Body(OpenApiSchema member)
        {
            return new OpenApiSchema
            {
                Type = "object",
                AdditionalPropertiesAllowed = false,
                Properties = { { "a", member } },
            };
        }

        private static KeyValuePair<string, OpenApiSchema> Nullable()
        {
            return new KeyValuePair<string, OpenApiSchema>(
                "nullable", new OpenApiSchema { Type = "string", Nullable = true });
        }

        private static KeyValuePair<string, OpenApiSchema> ReadOnly()
        {
            return new KeyValuePair<string, OpenApiSchema>(
                "readOnly", new OpenApiSchema { Type = "string", ReadOnly = true });
        }

        private static KeyValuePair<string, OpenApiSchema> WriteOnly()
        {
            return new KeyValuePair<string, OpenApiSchema>(
                "writeOnly", new OpenApiSchema { Type = "string", WriteOnly = true });
        }

        private static KeyValuePair<string, OpenApiSchema> Defaulted()
        {
            return new KeyValuePair<string, OpenApiSchema>(
                "default",
                new OpenApiSchema { Type = "string", Default = new OpenApiString("whatever") });
        }

        [Test]
        public void Every_refusal_is_the_same_word()
        {
            // A client matches on it, and a message that named the member would echo a caller's
            // own bytes back at it.
            Assert.That(Validate(Schema(), JsonBody.Read("{\"profiles\":[]}")), Is.EqualTo("malformed"));
            Assert.That(Validate(Schema(), null), Is.EqualTo("malformed"));
        }

        private static string Validate(string schema, JsonBody body)
        {
            var document = SpecDocument.Parse(
                "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"t\",\"version\":\"1\"},\"paths\":{" +
                "\"/a\":{\"post\":{\"operationId\":\"writeA\",\"requestBody\":{\"required\":true," +
                "\"content\":{\"application/json\":{\"schema\":" + schema + "}}}," +
                "\"responses\":{\"200\":{\"description\":\"o\"},\"415\":{\"description\":\"m\"}}}}}}");

            var reached = new List<string>();
            var router = new SpecRouter(document, new Dictionary<string, OperationHandler>
            {
                { "writeA", answering => { reached.Add("ran"); answering.Respond(200, "{}"); } },
            });

            var context = Context(body);
            router.Handle(context);

            return context.Status == 200 ? null : JsonBody.Read(context.Json).Text("error");
        }

        private static string Schema()
        {
            return "{\"type\":\"object\",\"required\":[\"name\",\"profiles\"],\"additionalProperties\":false," +
                   "\"properties\":{\"name\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":64}," +
                   "\"profiles\":{\"type\":\"array\",\"maxItems\":3,\"items\":{\"type\":\"string\"}}}}";
        }

        private static GateContext Context(JsonBody body)
        {
            var text = body == null ? "not an object" : Rewrite(body);
            var head = new HttpRequestHead(
                "POST",
                "/a",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                text.Length);

            return new GateContext(head, text, Paired());
        }

        /// <summary>
        /// Back to the bytes, so the router reads a body the way a connection delivers one rather
        /// than being handed an object a test built.
        /// </summary>
        private static string Rewrite(JsonBody body)
        {
            var parts = new List<string>();
            foreach (var name in body.Names)
            {
                var text = body.Text(name);
                if (text != null)
                {
                    parts.Add("\"" + name + "\":\"" + text + "\"");
                    continue;
                }

                var items = body.Array(name);
                parts.Add("\"" + name + "\":[" +
                          string.Join(",", items.Select(i => "\"" + i + "\"")) + "]");
            }

            return "{" + string.Join(",", parts) + "}";
        }

        private static ClientIdentity Paired()
        {
            using (var authority = Fakes.TestAuthority.New())
            using (var client = authority.IssueClient("agent-fictitious"))
            {
                return ClientIdentity.Paired("db", client, Array.Empty<string>());
            }
        }
    }
}
