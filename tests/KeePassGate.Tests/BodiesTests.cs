using System;
using KeePassGate.Testing;
using KeePassGate.Transport;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The helper the tests write their request bodies with, held to what the gate reads back.
    /// </summary>
    /// <remarks>
    /// A helper that pasted a value in unescaped would hand the gate a body its test never meant,
    /// which is the failure it exists to take away, so every value is read back by the gate's own
    /// reader and compared with what went in.
    /// </remarks>
    [TestFixture]
    public sealed class BodiesTests
    {
        [TestCase("plain", TestName = "a plain value")]
        [TestCase("a \"quoted\" word", TestName = "a quote")]
        [TestCase("C:\\a\\b", TestName = "a backslash")]
        [TestCase("first\r\nsecond\tthird", TestName = "a line break and a tab")]
        [TestCase("\u00fc\u20ac\U0001F600", TestName = "letters beyond ASCII")]
        [TestCase("{REF:P@I:0123456789ABCDEF}", TestName = "a field reference")]
        public void A_value_arrives_as_it_was_given(string value)
        {
            var body = JsonBody.Read(Bodies.Of(("name", value), ("profiles", new[] { value, "second" })));

            Assert.That(body, Is.Not.Null, "the gate reads it as JSON");
            Assert.That(body.Text("name"), Is.EqualTo(value));
            Assert.That(body.Array("profiles"), Is.EqualTo(new[] { value, "second" }));
        }

        [Test]
        public void Names_and_values_are_written_in_the_order_given()
        {
            Assert.That(Bodies.Of(("b", "1"), ("a", Array.Empty<string>())), Is.EqualTo("{\"b\":\"1\",\"a\":[]}"));
            Assert.That(Bodies.Of(), Is.EqualTo("{}"));
        }

        [Test]
        public void A_name_given_twice_or_not_at_all_or_a_value_of_another_kind_is_refused()
        {
            // The gate refuses a body naming a member twice, so writing one would make a test
            // expecting a refusal pass for that reason rather than the one it is about.
            Assert.That(() => Bodies.Of(("name", "a"), ("name", "b")), Throws.ArgumentException);
            Assert.That(() => Bodies.Of((null, "a")), Throws.ArgumentException);
            Assert.That(() => Bodies.Of(("count", 3)), Throws.ArgumentException);
            Assert.That(() => Bodies.Of(("nothing", null)), Throws.ArgumentException);
        }

        [Test]
        public void A_length_counts_bytes_rather_than_characters()
        {
            Assert.That(Bodies.LengthOf("{}"), Is.EqualTo(2));
            Assert.That(Bodies.LengthOf("\u00fc\U0001F600"), Is.EqualTo(6), "two bytes and four");
            Assert.That(Bodies.LengthOf(null), Is.EqualTo(0));
        }
    }
}
