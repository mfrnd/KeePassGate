using System;
using System.IO;
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

        [Test]
        public void A_document_written_into_another_writer_is_the_document_a_kept_writer_hands_back()
        {
            // The answer to a read of an entry is written this way, a member at a time into the
            // transport's own writer. The same description has to come to the same text either
            // way, or an answer written as it is sent would be a dialect of its own.
            Action<JsonWriter> describe = json => json
                .Text("a", "b")
                .Text("absent", null)
                .Number("n", 7)
                .Flag("f", true)
                .Object("o").Text("quoted", "a \"b\" \\ c").End()
                .Array("items").Item().Text("c", "d").End().Item("e").End();

            var kept = new JsonWriter();
            describe(kept);

            using (var text = new StringWriter())
            {
                JsonWriter.Into(text, describe);

                Assert.That(text.ToString(), Is.EqualTo(kept.Done()));
                Assert.DoesNotThrow(() => JObject.Parse(text.ToString()));
            }
        }

        [Test]
        public void Whatever_a_description_leaves_open_is_closed_in_the_other_writer_too()
        {
            using (var text = new StringWriter())
            {
                JsonWriter.Into(text, json => json.Object("o").Array("items").Item().Text("a", "b"));

                Assert.That(text.ToString(), Is.EqualTo("{\"o\":{\"items\":[{\"a\":\"b\"}]}}"));
            }
        }

        [Test]
        public void A_description_of_nothing_is_an_empty_object()
        {
            using (var text = new StringWriter())
            {
                JsonWriter.Into(text, json => { });

                Assert.That(text.ToString(), Is.EqualTo("{}"));
            }
        }

        [Test]
        public void Each_member_reaches_the_other_writer_as_it_is_described()
        {
            // Nothing of the document is kept here, which is the point of writing it this way: a
            // value described is with the writer underneath before the member after it is read.
            using (var text = new StringWriter())
            {
                var seen = string.Empty;

                JsonWriter.Into(text, json =>
                {
                    json.Text("first", "an invented value");
                    seen = text.ToString();
                    json.Text("second", "another");
                });

                Assert.That(seen, Is.EqualTo("{\"first\":\"an invented value\""));
                Assert.That(text.ToString(), Is.EqualTo("{\"first\":\"an invented value\",\"second\":\"another\"}"));
            }
        }

        [Test]
        public void A_description_that_throws_leaves_text_that_is_not_a_finished_document()
        {
            // A read that fails part way must not leave half a document that reads as a whole.
            // Json.NET closes whatever is open when its writer is let go, which would finish it,
            // so that is switched off, and what is left is open where the description stopped.
            using (var text = new StringWriter())
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => JsonWriter.Into(text, json =>
                {
                    json.Text("a", "b").Object("fields").Text("Title", "an invented service");
                    throw new InvalidOperationException("an invented read that fails");
                }));

                Assert.That(thrown.Message, Is.EqualTo("an invented read that fails"));
                Assert.That(text.ToString(), Is.EqualTo("{\"a\":\"b\",\"fields\":{\"Title\":\"an invented service\""));
                Assert.That(
                    () => JObject.Parse(text.ToString()),
                    Throws.InstanceOf<Newtonsoft.Json.JsonException>(),
                    "what is left reads as no document at all");
            }
        }

        [Test]
        public void A_document_written_into_another_writer_has_no_text_to_hand_back()
        {
            // `Done` hands back the document a writer kept. A writer that kept none has nothing
            // true to answer with, during the description or after it.
            using (var text = new StringWriter())
            {
                JsonWriter inside = null;

                JsonWriter.Into(text, json =>
                {
                    inside = json;
                    json.Text("a", "b");
                    Assert.Throws<InvalidOperationException>(() => json.Done());
                });

                Assert.Throws<InvalidOperationException>(() => inside.Done());
                Assert.That(text.ToString(), Is.EqualTo("{\"a\":\"b\"}"), "asking changed what was written");
            }
        }

        [Test]
        public void The_other_writer_is_left_open_for_whoever_it_belongs_to()
        {
            using (var text = new StringWriter())
            {
                JsonWriter.Into(text, json => json.Text("a", "b"));

                Assert.DoesNotThrow(() => text.Write("\r\n"));
                Assert.That(text.ToString(), Is.EqualTo("{\"a\":\"b\"}\r\n"));
            }
        }

        [Test]
        public void Writing_into_another_writer_needs_the_writer_and_a_description()
        {
            using (var text = new StringWriter())
            {
                Assert.Throws<ArgumentNullException>(() => JsonWriter.Into(null, json => { }));
                Assert.Throws<ArgumentNullException>(() => JsonWriter.Into(text, null));
                Assert.That(text.ToString(), Is.Empty);
            }
        }
    }
}
