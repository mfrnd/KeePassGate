using KeePassGate.Spec;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// What a percent escape means, and where it is allowed to mean it.
    /// </summary>
    /// <remarks>
    /// The whole subject is one rule: a path is split into segments and then each segment is
    /// decoded, never the other way round. Decoding first makes an escaped separator into a real
    /// one, which silently turns one name into two and leaves any name holding a slash impossible
    /// to address at all.
    /// </remarks>
    [TestFixture]
    public sealed class PathEncodingTests
    {
        /// <summary>A separator, spelled the way a client has to spell one inside a name.</summary>
        private const string Escaped = "%2F";

        [Test]
        public void An_escaped_separator_stays_inside_the_segment_it_was_escaped_in()
        {
            // The case the rule exists for. An attachment called `a/b` is a name a database can
            // hold, so it has to be a name this API can address.
            var template = PathTemplate.Parse("/entries/{uuid}/attachments/{name}");

            System.Collections.Generic.IReadOnlyDictionary<string, string> captured;

            Assert.That(template.TryMatch("/entries/7b1c/attachments/a" + Escaped + "b", out captured), Is.True);
            Assert.That(captured["name"], Is.EqualTo("a/b"));
        }

        [Test]
        public void A_literal_is_what_a_caller_meant_rather_than_how_it_spelled_it()
        {
            var template = PathTemplate.Parse("/gate/status");

            System.Collections.Generic.IReadOnlyDictionary<string, string> captured;

            Assert.That(template.TryMatch("/g%61te/status", out captured), Is.True);
            Assert.That(template.TryMatch("/gate" + Escaped + "status", out captured), Is.False,
                "one escaped segment is one segment, whatever it decodes to");
        }

        [Test]
        public void A_plus_is_a_plus_in_a_path()
        {
            // It means a space in a query string and nowhere else. While it meant one in both,
            // `a+b` and `a b` were the same request.
            var template = PathTemplate.Parse("/entries/{uuid}/fields/{name}");

            System.Collections.Generic.IReadOnlyDictionary<string, string> captured;

            template.TryMatch("/entries/7b1c/fields/a+b", out captured);
            Assert.That(captured["name"], Is.EqualTo("a+b"));

            template.TryMatch("/entries/7b1c/fields/a%20b", out captured);
            Assert.That(captured["name"], Is.EqualTo("a b"));
        }

        [Test]
        public void A_plus_is_still_a_space_in_a_query()
        {
            string decoded;

            Assert.That(PercentEncoding.TryDecodeQuery("a+b", out decoded), Is.True);
            Assert.That(decoded, Is.EqualTo("a b"));
        }

        [Test]
        public void A_control_character_is_refused_however_it_arrived()
        {
            // A parser that accepts `%0A` is a parser that puts a line break into a log, and the
            // audit is a file of one record per line.
            string decoded;

            Assert.That(PercentEncoding.TryDecodeSegment("a%0Ab", out decoded), Is.False);
            Assert.That(PercentEncoding.TryDecodeSegment("a%7Fb", out decoded), Is.False);
            Assert.That(PercentEncoding.TryDecodeQuery("a%00b", out decoded), Is.False);
        }

        [Test]
        public void An_escape_that_is_not_one_is_refused_rather_than_repaired()
        {
            string decoded;

            Assert.That(PercentEncoding.TryDecodeSegment("a%zzb", out decoded), Is.False);
            Assert.That(PercentEncoding.TryDecodeSegment("a%4", out decoded), Is.False);
            Assert.That(PercentEncoding.TryDecodeSegment("a%", out decoded), Is.False);
        }

        [TestCase("a|b")]
        [TestCase("a#b")]
        [TestCase("a{b}")]
        [TestCase("a\\b")]
        [TestCase("a^b")]
        [TestCase("a`b")]
        [TestCase("a\"b")]
        [TestCase("a<b>")]
        [TestCase("a[b]")]
        [TestCase("a b")]
        public void A_character_the_grammar_says_to_escape_is_refused_when_it_arrives_bare(string text)
        {
            // RFC 3986 section 3.3. A field so named was served, and would be refused by any server
            // that follows the grammar, so a client that forgot to escape worked against this alone.
            string decoded;

            Assert.That(PercentEncoding.TryDecodeSegment(text, out decoded), Is.False, text);
            Assert.That(PercentEncoding.TryDecodeQuery(text, out decoded), Is.False, text);
        }

        [Test]
        public void What_the_grammar_lets_stand_for_itself_still_does()
        {
            string decoded;

            Assert.That(PercentEncoding.TryDecodeSegment("a-b.c_d~e!f$g&h'i(j)k*l+m,n;o=p:q@r", out decoded), Is.True);
            Assert.That(PercentEncoding.TryDecodeQuery("a/b?c", out decoded), Is.True,
                "a query may carry a slash and a question mark");
            Assert.That(PercentEncoding.TryDecodeSegment("a%7Cb", out decoded), Is.True);
            Assert.That(decoded, Is.EqualTo("a|b"), "escaped, the same character is an ordinary one");
        }

        [Test]
        public void An_escape_is_an_octet_of_utf8_and_not_a_character()
        {
            // `%C3%A9` is one letter, not two. Read one byte at a time it becomes two characters
            // that name nothing and match nothing, and the same two reach the audit.
            string decoded;

            Assert.That(PercentEncoding.TryDecodeSegment("caf%C3%A9", out decoded), Is.True);
            Assert.That(decoded, Is.EqualTo("café"));

            Assert.That(PercentEncoding.TryDecodeQuery("%E6%97%A5", out decoded), Is.True);
            Assert.That(decoded.Length, Is.EqualTo(1), "three octets are one character");
        }

        [Test]
        public void A_sequence_that_is_not_utf8_is_refused_rather_than_repaired()
        {
            string decoded;

            // A lead byte with no continuation, a continuation with no lead, and an overlong
            // spelling of a character that already has a shorter one.
            Assert.That(PercentEncoding.TryDecodeSegment("%C3", out decoded), Is.False);
            Assert.That(PercentEncoding.TryDecodeSegment("%A9", out decoded), Is.False);
            Assert.That(PercentEncoding.TryDecodeSegment("%C0%AF", out decoded), Is.False);
        }

        [Test]
        public void A_control_character_spelled_as_utf8_is_refused_too()
        {
            // The C1 controls have no one byte spelling, so the octet check never sees them.
            string decoded;

            Assert.That(PercentEncoding.TryDecodeSegment("a%C2%85b", out decoded), Is.False);
        }

        [Test]
        public void Only_a_whole_segment_navigates()
        {
            // `notes..txt` is a perfectly good attachment name, and it used to match no route at
            // all because the check looked for two dots anywhere in the path.
            Assert.That(PercentEncoding.IsTraversal(".."), Is.True);
            Assert.That(PercentEncoding.IsTraversal("."), Is.True);
            Assert.That(PercentEncoding.IsTraversal("notes..txt"), Is.False);
            Assert.That(PercentEncoding.IsTraversal("...."), Is.False);
        }
    }
}
