using System;
using System.Collections.Generic;
using KeePassGate.Spec;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Which path is which route, which is the one decision a router makes before anything else.
    /// </summary>
    /// <remarks>
    /// Most of these are about what does **not** match. A template that matched one path too many
    /// would send a request to an operation nobody meant it to reach, and the operation would have
    /// no way to tell.
    /// </remarks>
    [TestFixture]
    public sealed class PathTemplateTests
    {
        [Test]
        public void A_literal_path_matches_itself_and_nothing_else()
        {
            var template = PathTemplate.Parse("/gate/status");

            Assert.That(Matches(template, "/gate/status"), Is.True);
            Assert.That(Matches(template, "/gate/stat"), Is.False);
            Assert.That(Matches(template, "/gate/status/"), Is.False);
            Assert.That(Matches(template, "/gate/status/more"), Is.False);
            Assert.That(Matches(template, "/Gate/Status"), Is.False, "paths are compared as sent");
        }

        [Test]
        public void A_parameter_captures_one_segment_and_reports_what_it_captured()
        {
            var template = PathTemplate.Parse("/entries/{uuid}");

            IReadOnlyDictionary<string, string> captured;

            Assert.That(template.TryMatch("/entries/abcdef01", out captured), Is.True);
            Assert.That(captured["uuid"], Is.EqualTo("abcdef01"));
        }

        [Test]
        public void A_parameter_never_spans_a_separator()
        {
            // Otherwise `/entries/{uuid}` would reach the operation that reads one entry with a
            // path that names two things.
            var template = PathTemplate.Parse("/entries/{uuid}");

            Assert.That(Matches(template, "/entries/a/b"), Is.False);
        }

        [Test]
        public void A_parameter_is_never_empty()
        {
            // `/entries/` is not a request for an entry called nothing.
            Assert.That(Matches(PathTemplate.Parse("/entries/{uuid}"), "/entries/"), Is.False);
        }

        [Test]
        public void Several_parameters_are_all_captured()
        {
            var template = PathTemplate.Parse("/groups/{group}/entries/{uuid}");

            IReadOnlyDictionary<string, string> captured;
            Assert.That(template.TryMatch("/groups/g1/entries/e2", out captured), Is.True);
            Assert.That(captured["group"], Is.EqualTo("g1"));
            Assert.That(captured["uuid"], Is.EqualTo("e2"));
            Assert.That(template.Parameters, Is.EqualTo(new[] { "group", "uuid" }));
        }

        [Test]
        public void A_path_that_captures_nothing_captures_an_empty_set_rather_than_null()
        {
            IReadOnlyDictionary<string, string> captured;

            Assert.That(PathTemplate.Parse("/gate/status").TryMatch("/gate/status", out captured), Is.True);
            Assert.That(captured, Is.Empty);
        }

        [TestCase("gate/status", TestName = "not absolute")]
        [TestCase("", TestName = "empty")]
        [TestCase(null, TestName = "null")]
        [TestCase("/entries/{}", TestName = "a parameter with no name")]
        [TestCase("/entries/a{uuid}", TestName = "a brace inside a literal")]
        [TestCase("/entries/{uuid}b", TestName = "a brace at the end of a literal")]
        [TestCase("/entries/{uuid}/more/{uuid}", TestName = "the same name twice")]
        public void A_template_that_would_route_badly_is_refused_when_it_is_read(string template)
        {
            // At startup, over the document this build ships, so a template nobody can route by is
            // a plugin that does not load rather than a route that silently matches nothing.
            Assert.Throws<ArgumentException>(() => PathTemplate.Parse(template));
        }

        [Test]
        public void Something_that_is_not_a_path_matches_nothing()
        {
            var template = PathTemplate.Parse("/gate/status");

            Assert.That(Matches(template, null), Is.False);
            Assert.That(Matches(template, ""), Is.False);
            Assert.That(Matches(template, "gate/status"), Is.False);
        }

        [Test]
        public void A_template_says_what_the_document_wrote()
        {
            // What a log or a failure names, so that it is the document's own text rather than
            // something reconstructed from the parts.
            Assert.That(PathTemplate.Parse("/entries/{uuid}").ToString(), Is.EqualTo("/entries/{uuid}"));
        }

        [TestCase("/entries/..%2F..%2F", TestName = "an escaped slash")]
        [TestCase("/entries/..%5C..%5C", TestName = "an escaped backslash, which climbs on Windows")]
        [TestCase("/entries/%2E%2E%2F%2E%2E%2F", TestName = "the dots escaped as well")]
        [TestCase("/entries/..%2f..%2f", TestName = "lowercase hex")]
        [TestCase("/entries/a%2F..%2Fb", TestName = "a climb in the middle of a name")]
        public void A_capture_that_decodes_into_something_that_climbs_is_refused(string path)
        {
            // The reader cannot have caught these. It refuses a segment that *is* `..`, and it
            // splits before it decodes so that an escaped separator stays inside one name,
            // which is the whole reason an attachment can be called `a/b`. So each of these
            // arrives here as one token and decodes into a path that climbs.
            var template = PathTemplate.Parse("/entries/{uuid}");

            Assert.That(Matches(template, path), Is.False, path);
        }

        [Test]
        public void A_name_with_a_separator_in_it_is_still_a_name()
        {
            // The other side of the same rule: escaping a separator is how an attachment called
            // `a/b` is addressed at all, and refusing a climb must not refuse that too.
            var template = PathTemplate.Parse("/entries/{uuid}");

            IReadOnlyDictionary<string, string> captured;
            Assert.That(template.TryMatch("/entries/a%2Fb", out captured), Is.True);
            Assert.That(captured["uuid"], Is.EqualTo("a/b"));
        }

        [TestCase("", TestName = "nothing at all")]
        [TestCase("a\rb", TestName = "a carriage return")]
        [TestCase("a\nb", TestName = "a line feed")]
        [TestCase("a\tb", TestName = "a tab")]
        [TestCase("a\u007Fb", TestName = "delete")]
        [TestCase("a\u0085b", TestName = "a C1 control")]
        [TestCase("..", TestName = "a climb")]
        [TestCase("a/../b", TestName = "a climb inside a name")]
        [TestCase("..\\..\\", TestName = "a climb spelled with backslashes")]
        public void A_value_no_path_could_capture_is_not_a_capture(string value)
        {
            // The question asked of a name a body is about to make, so it has to be the one the
            // match asks of a name a path carries, and not a copy of it.
            Assert.That(PathTemplate.IsCapture(value), Is.False);
            Assert.That(PathTemplate.IsCapture(null), Is.False);
        }

        [TestCase("a/b")]
        [TestCase("notes..txt")]
        [TestCase("Deploy key")]
        [TestCase("été")]
        public void A_value_a_path_can_capture_is_one(string value)
        {
            Assert.That(PathTemplate.IsCapture(value), Is.True);
        }

        [Test]
        public void What_a_template_with_no_captures_hands_back_cannot_be_written_to()
        {
            // One object, shared by every capture-less match in the process, and published into
            // per-request state. A caller that downcast it would be writing into what every
            // other request is reading.
            IReadOnlyDictionary<string, string> captured;
            PathTemplate.Parse("/gate/status").TryMatch("/gate/status", out captured);

            var writable = captured as IDictionary<string, string>;
            Assert.That(writable, Is.Not.Null, "it is still castable; what matters is what that buys");
            Assert.Throws<NotSupportedException>(() => writable["poisoned"] = "yes");
        }

        private static bool Matches(PathTemplate template, string path)
        {
            IReadOnlyDictionary<string, string> captured;
            return template.TryMatch(path, out captured);
        }
    }
}
