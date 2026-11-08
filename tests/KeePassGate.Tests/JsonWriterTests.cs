using System;
using KeePassGate.Transport;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// The writer every answer goes through, which promises that whatever a handler leaves open is
    /// closed, so that nothing it answers is text that is not JSON.
    /// </summary>
    [TestFixture]
    public sealed class JsonWriterTests
    {
        [Test]
        public void What_a_handler_closes_itself_is_written_as_it_closed_it()
        {
            var written = new JsonWriter()
                .Text("a", "b")
                .Array("items").Item().Text("c", "d").End().Item("e").End()
                .Done();

            Assert.That(written, Is.EqualTo("{\"a\":\"b\",\"items\":[{\"c\":\"d\"},\"e\"]}"));
        }

        [Test]
        public void An_object_left_open_inside_an_array_is_closed_with_everything_around_it()
        {
            // Json.NET ends only as far as the nearest object when asked to end an object, so
            // closing the root that way closed this object and left the array and the root open.
            var written = new JsonWriter().Array("items").Item().Text("a", "b").Done();

            Assert.That(written, Is.EqualTo("{\"items\":[{\"a\":\"b\"}]}"));
            Assert.DoesNotThrow(() => JObject.Parse(written));
        }

        [Test]
        public void Objects_left_open_inside_objects_are_closed_to_the_root()
        {
            var written = new JsonWriter().Object("o").Object("p").Text("a", "b").Done();

            Assert.That(written, Is.EqualTo("{\"o\":{\"p\":{\"a\":\"b\"}}}"));
        }

        [Test]
        public void An_array_left_open_is_closed_with_the_root()
        {
            var written = new JsonWriter().Object("o").Array("x").Done();

            Assert.That(written, Is.EqualTo("{\"o\":{\"x\":[]}}"));
        }

        [Test]
        public void Asking_twice_is_asking_for_the_same_string()
        {
            var writer = new JsonWriter().Array("items").Item();

            Assert.That(writer.Done(), Is.SameAs(writer.Done()));
        }

        [Test]
        public void Done_after_dispose_throws_when_nothing_was_made()
        {
            // What the class remark holds: dispose closes the writer, so `Done` comes first.
            var writer = new JsonWriter().Text("a", "b");
            writer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => writer.Done());
        }
    }
}
