using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The request body reader, which is strict because it can afford to be.
    /// </summary>
    /// <remarks>
    /// Most of this fixture is refusals, and that is the point. The bodies it reads arrive from an
    /// unauthenticated caller, so anything it accepts is something the rest of the plugin has to
    /// cope with, and everything it refuses is a shape nobody downstream ever sees.
    /// </remarks>
    [TestFixture]
    public sealed class JsonBodyTests
    {
        [Test]
        public void A_flat_object_of_strings_reads()
        {
            var body = JsonBody.Read("{\"name\":\"agent-fictitious\",\"proof\":\"AAAA\"}");

            Assert.That(body, Is.Not.Null);
            Assert.That(body.Text("name"), Is.EqualTo("agent-fictitious"));
            Assert.That(body.Text("proof"), Is.EqualTo("AAAA"));
            Assert.That(body.Count, Is.EqualTo(2));
        }

        [Test]
        public void An_array_of_strings_reads()
        {
            var body = JsonBody.Read("{\"profiles\":[\"ops\",\"readonly\"]}");

            Assert.That(body.Array("profiles"), Is.EqualTo(new[] { "ops", "readonly" }));
        }

        [Test]
        public void An_empty_object_reads_and_holds_nothing()
        {
            var body = JsonBody.Read("{}");

            Assert.That(body, Is.Not.Null);
            Assert.That(body.Count, Is.Zero);
            Assert.That(body.Text("anything"), Is.Null);
        }

        [Test]
        public void An_empty_array_reads()
        {
            Assert.That(JsonBody.Read("{\"profiles\":[]}").Array("profiles"), Is.Empty);
        }

        [Test]
        public void Whitespace_between_everything_reads()
        {
            var body = JsonBody.Read("  {\n \"a\" : \"1\" ,\r\n \"b\" : [ \"2\" ] \t}  ");

            Assert.That(body, Is.Not.Null);
            Assert.That(body.Text("a"), Is.EqualTo("1"));
            Assert.That(body.Array("b"), Is.EqualTo(new[] { "2" }));
        }

        [Test]
        public void A_string_is_not_an_array_and_an_array_is_not_a_string()
        {
            var body = JsonBody.Read("{\"one\":\"x\",\"many\":[\"y\"]}");

            Assert.That(body.Array("one"), Is.Null, "a string must not read as a one element array");
            Assert.That(body.Text("many"), Is.Null);
            Assert.That(body.Has("one"), Is.True);
            Assert.That(body.Has("many"), Is.True);
            Assert.That(body.Has("neither"), Is.False);
        }

        [Test]
        public void Escapes_read_the_way_JSON_says()
        {
            // `\b` and `\f` are the two JSON escapes missing from this list, and they are missing
            // because the policy refuses them rather than because the grammar does. See
            // `A_character_the_database_could_not_hold_is_refused` below.
            var body = JsonBody.Read("{\"a\":\"q\\\"q\\\\q\\/q\\nq\\rq\\tq\\u0041\"}");

            Assert.That(body.Text("a"), Is.EqualTo("q\"q\\q/q\nq\rq\tqA"));
        }

        // The control characters, other than the three a `.kdbx` can carry.
        [TestCase("{\"a\":\"one\\u0000two\"}")]
        [TestCase("{\"a\":\"one\\btwo\"}")]
        [TestCase("{\"a\":\"one\\ftwo\"}")]
        [TestCase("{\"a\":\"one\\u000Btwo\"}")]
        [TestCase("{\"a\":\"one\\u001Ftwo\"}")]

        // The two code points that are never characters.
        [TestCase("{\"a\":\"one\\uFFFEtwo\"}")]
        [TestCase("{\"a\":\"one\\uFFFFtwo\"}")]

        // And a name, because a field is addressed by one and stored under one.
        [TestCase("{\"one\\u0007two\":\"a\"}")]
        public void A_character_the_database_could_not_hold_is_refused(string text)
        {
            // A `.kdbx` is XML, and KeePass writes a string into one through
            // `StrUtil.SafeXmlString`, which silently removes everything XML 1.0 cannot represent.
            // Accepting these meant answering that a value had been stored and storing a shorter
            // one, which on a password is the difference between a client believing it rotated a
            // credential and its not having done so.
            Assert.That(JsonBody.Read(text), Is.Null);
        }

        [Test]
        public void A_surrogate_with_no_pair_is_refused_where_one_can_still_arrive()
        {
            // Not from the wire, and that is worth writing down rather than discovering twice. A
            // body arrives as UTF-8 decoded strictly, and UTF-8 cannot carry an unpaired surrogate
            // at all; written as an escape it never reaches this policy either, because Json.NET
            // substitutes U+FFFD for one while it is reading, measured against the version this
            // builds against. What can carry one is the reader's other caller: an access control
            // document, a profile list or an authority read back out of a `.kdbx` this plugin did
            // not write. Refusing there is the fail-closed direction, which is where the check
            // earns its place.
            // Both ends of both halves of the surrogate range.
            foreach (var half in new[] { '\uD800', '\uDBFF', '\uDC00', '\uDFFF' })
            {
                Assert.That(JsonBody.Read("{\"a\":\"one" + half + "two\"}"), Is.Null,
                    "half of a character is not a character");
            }

            Assert.That(JsonBody.Read("{\"one\uD800two\":\"a\"}"), Is.Null, "in a name as well");
            Assert.That(JsonBody.Read("{\"a\":\"one\uD800\"}"), Is.Null, "and at the end of one");
        }

        [Test]
        public void A_surrogate_pair_is_one_character_and_survives()
        {
            // The refusal above is about halves. A pair is a code point the database stores like
            // any other, including the ones whose low half looks like `U+FFFE`.
            Assert.That(JsonBody.Read("{\"a\":\"one\\uD83D\\uDE00two\"}").Text("a"),
                Is.EqualTo("one" + char.ConvertFromUtf32(0x1F600) + "two"));
            Assert.That(JsonBody.Read("{\"a\":\"\\uD83F\\uDFFE\"}"), Is.Not.Null,
                "U+1FFFE is a character, whatever the two halves look like on their own");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("\"a string\"")]
        [TestCase("{")]
        [TestCase("}")]
        [TestCase("{\"a\"}")]
        [TestCase("{\"a\":}")]
        [TestCase("{:\"a\"}")]
        [TestCase("{,}")]
        [TestCase("{\"a\":\"b\"} trailing")]
        [TestCase("{\"a\":\"b\"}{\"c\":\"d\"}")]
        [TestCase("{\"a\":\"b\"")]
        [TestCase("{\"a\":\"b}")]
        public void Anything_that_is_not_one_flat_object_is_refused(string text)
        {
            Assert.That(JsonBody.Read(text), Is.Null);
        }

        [TestCase("{\"a\":1}")]
        [TestCase("{\"a\":true}")]
        [TestCase("{\"a\":false}")]
        [TestCase("{\"a\":null}")]
        [TestCase("{\"a\":{\"b\":\"c\"}}")]
        [TestCase("{\"a\":[[\"b\"]]}")]
        [TestCase("{\"a\":[1]}")]
        [TestCase("{\"a\":[{\"b\":\"c\"}]}")]
        public void Every_value_that_is_not_a_string_or_an_array_of_strings_is_refused(string text)
        {
            // Nesting and numbers are refused rather than ignored, because a body this cannot read
            // is a body nothing downstream has to have an opinion about.
            Assert.That(JsonBody.Read(text), Is.Null);
        }

        [Test]
        public void A_name_that_appears_twice_is_refused()
        {
            // Which one wins is the sort of question that turns into a parser difference somewhere,
            // so there is no answer to give: it is not a body.
            Assert.That(JsonBody.Read("{\"a\":\"1\",\"a\":\"2\"}"), Is.Null);
            Assert.That(JsonBody.Read("{\"a\":\"1\",\"a\":[\"2\"]}"), Is.Null);
        }

        [Test]
        public void A_trailing_comma_is_read_as_the_object_it_obviously_is()
        {
            // Json.NET accepts it, and refusing it would mean scanning for one character that
            // changes nothing about what the object says. Pinned so that the leniency is a decision
            // rather than something nobody noticed.
            var body = JsonBody.Read("{\"a\":\"b\",}");

            Assert.That(body, Is.Not.Null);
            Assert.That(body.Text("a"), Is.EqualTo("b"));
        }

        [Test]
        public void A_value_that_looks_like_a_date_stays_the_text_that_was_sent()
        {
            // Json.NET turns anything date shaped into a `DateTime` unless it is told not to, and a
            // value would then come back reformatted rather than as it was sent. This is what would
            // notice if `DateParseHandling.None` were ever dropped.
            Assert.That(
                JsonBody.Read("{\"a\":\"2026-03-04T05:06:07Z\"}").Text("a"),
                Is.EqualTo("2026-03-04T05:06:07Z"));
        }

        [Test]
        public void A_comment_inside_the_object_reaches_nothing_and_is_discarded()
        {
            // The same leniency in another shape. A comment holds no member and reaches nothing,
            // so a body with one says exactly what the same body without one says.
            var inside = JsonBody.Read("{\"a\":\"b\" /* invented */}");

            Assert.That(inside, Is.Not.Null);
            Assert.That(inside.Text("a"), Is.EqualTo("b"));
            Assert.That(inside.Count, Is.EqualTo(1), "and it is not a member");
        }

        [Test]
        public void A_comment_after_the_object_is_refused_with_everything_else_that_follows_one()
        {
            // Measured, and worth pinning because the leniency above does not extend here: the
            // reader reports a comment after the closing brace as a token like any other, and what
            // refuses it is the same check that refuses a second document. Nothing distinguishes
            // them without reading past the object, which is the thing that check exists to
            // prevent.
            Assert.That(JsonBody.Read("{\"a\":\"b\"} /* invented */"), Is.Null);
        }

        [TestCase("{\"a\":\"b\"}\0")]
        [TestCase("{\"a\":\"b\"}\0x")]
        [TestCase("{\"a\":\"b\"}\0{\"c\":\"d\"}")]
        public void A_NUL_after_the_object_is_refused_with_whatever_follows_it(string text)
        {
            // Measured: Json.NET's reader reports a NUL after the object as the end of its input,
            // so one on its own passed the check that refuses anything after the closing brace.
            // What follows a NUL was refused already, and is pinned beside it. The body reader
            // decodes strictly and lets a NUL through, so these are bytes a caller can send.
            Assert.That(JsonBody.Read(text), Is.Null);
        }

        [TestCase("{'a':'b'}", "b")]
        [TestCase("{a:\"b\"}", "b")]
        [TestCase("{\"a\":\"b\\'c\"}", "b'c")]
        public void Json_NET_s_other_leniencies_are_read_as_the_object_they_obviously_are(string text, string value)
        {
            // Single quotes, a name with none, and an escaped apostrophe: Json.NET's grammar, like
            // the trailing comma and the comment above, and pinned for the same reason. None of
            // them carries a member or changes what the object says.
            var body = JsonBody.Read(text);

            Assert.That(body, Is.Not.Null, text);
            Assert.That(body.Text("a"), Is.EqualTo(value));
            Assert.That(body.Count, Is.EqualTo(1));
        }

        [Test]
        public void Something_nested_is_refused()
        {
            // Two different refusals, and the difference is worth knowing. One level of nesting is
            // parsed and then refused by the value check, because a member's value here is a
            // string or an array of strings. Only the second level is refused by the reader's own
            // depth limit, which is what keeps a deep body from ever becoming a tree.
            Assert.That(JsonBody.Read("{\"a\":{\"b\":\"c\"}}"), Is.Null);
            Assert.That(JsonBody.Read("{\"a\":[[\"b\"]]}"), Is.Null);
        }

        [Test]
        public void A_line_break_in_a_value_is_a_value_however_it_was_written()
        {
            // An entry's notes are a field like any other and people write paragraphs in them, so a
            // line break has to survive. It reads the same whether it arrived escaped or raw, which
            // is Json.NET's grammar rather than a decision made here, and it is harmless because
            // what keeps a line break out of a log is escaping on the way out.
            Assert.That(JsonBody.Read("{\"a\":\"one\\ntwo\"}").Text("a"), Is.EqualTo("one\ntwo"));
            Assert.That(JsonBody.Read("{\"a\":\"one\ntwo\"}").Text("a"), Is.EqualTo("one\ntwo"));
        }

        [TestCase("{\"a\":\" \"}")]
        [TestCase("{\"a\":\"   \"}")]
        [TestCase("{\"a\":\"\\n\\t \\r\"}")]
        [TestCase("{\" \":\"a\"}")]
        [TestCase("{\"a\":[\"b\",\" \"]}")]
        public void A_string_of_whitespace_alone_is_refused(string text)
        {
            // KeePass drops an element's text when it is only whitespace, so a value like this
            // came back empty from the file and a name like this came back as no name at all.
            // Measured against KeePass 2.61.1.
            Assert.That(JsonBody.Read(text), Is.Null, text);
        }

        [Test]
        public void Whitespace_beside_anything_else_is_kept_and_an_empty_string_is_still_one()
        {
            Assert.That(JsonBody.Read("{\"a\":\" b \"}").Text("a"), Is.EqualTo(" b "));
            Assert.That(JsonBody.Read("{\"a\":\"\"}").Text("a"), Is.Empty);
            Assert.That(JsonBody.Read("{\"a\":\"\\u00A0\"}").Text("a"), Is.EqualTo("\u00A0"),
                "a space XML does not count as whitespace survives the file");
        }

        [TestCase("{\"a\":\"\\x\"}")]
        [TestCase("{\"a\":\"\\\"}")]
        [TestCase("{\"a\":\"\\u00\"}")]
        [TestCase("{\"a\":\"\\u12g4\"}")]
        public void A_broken_escape_is_refused(string text)
        {
            Assert.That(JsonBody.Read(text), Is.Null);
        }

        [Test]
        public void There_is_a_ceiling_on_how_many_members_a_body_may_have()
        {
            Assert.That(JsonBody.Read(Object(JsonBody.MaxMembers)), Is.Not.Null);
            Assert.That(JsonBody.Read(Object(JsonBody.MaxMembers + 1)), Is.Null);
        }

        [Test]
        public void There_is_a_ceiling_on_how_long_an_array_may_be()
        {
            Assert.That(JsonBody.Read(Array(JsonBody.MaxItems)), Is.Not.Null);
            Assert.That(JsonBody.Read(Array(JsonBody.MaxItems + 1)), Is.Null);
        }

        [Test]
        public void There_is_a_ceiling_on_how_long_a_string_may_be()
        {
            Assert.That(
                JsonBody.Read("{\"a\":\"" + new string('x', JsonBody.MaxStringLength + 10) + "\"}"), Is.Null);
        }

        [Test]
        public void Reading_a_name_out_of_nothing_is_not_an_error()
        {
            var body = JsonBody.Read("{}");

            Assert.That(body.Text(null), Is.Null);
            Assert.That(body.Array(null), Is.Null);
            Assert.That(body.Has(null), Is.False);
        }

        private static string Object(int members)
        {
            var text = new System.Text.StringBuilder("{");
            for (var i = 0; i < members; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append("\"k").Append(i).Append("\":\"v\"");
            }

            return text.Append('}').ToString();
        }

        private static string Array(int items)
        {
            var text = new System.Text.StringBuilder("{\"a\":[");
            for (var i = 0; i < items; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append("\"v\"");
            }

            return text.Append("]}").ToString();
        }
    }
}
