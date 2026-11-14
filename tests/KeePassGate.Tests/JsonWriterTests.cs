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

        [Test]
        public void Bytes_handed_over_in_pieces_are_written_as_the_base64_of_the_whole(
            [Values(1, 2, 3, 4, 5, 6, 12 * 1024, (12 * 1024) + 1, 48 * 1024, (48 * 1024) + 2, 100000, 150001)] int size,
            [Values(3, 21, 12 * 1024, 48 * 1024)] int piece)
        {
            // An attachment is written this way, a piece at a time as it is read. Base64 turns
            // three bytes into four characters, so the text of pieces that are each a whole
            // number of threes joins into the text of the whole, with padding only where the
            // last piece ends. The same text from a writer that keeps its document and from a
            // writer that writes into another, as every other member is.
            var bytes = Invented(size);
            var expected = "{\"content\":\"" + Convert.ToBase64String(bytes) + "\"}";
            Action<JsonWriter> describe = json => json.Base64("content", InPieces(bytes, piece));

            var kept = new JsonWriter();
            describe(kept);
            Assert.That(
                kept.Done() == expected,
                Is.True,
                "a kept writer wrote other text than the base64 of the whole");

            using (var text = new StringWriter())
            {
                JsonWriter.Into(text, describe);

                Assert.That(text.ToString() == expected, Is.True, "written into another writer, the text differs");
                Assert.That(
                    Convert.FromBase64String((string)JObject.Parse(text.ToString())["content"]),
                    Is.EqualTo(bytes));
            }
        }

        [Test]
        public void No_bytes_are_written_as_an_empty_string()
        {
            // An attachment of no bytes hands out no piece, and a reader may as well hand over
            // a piece holding nothing: either is the empty string, quoted, and still a value.
            Action<Action<byte[], int, int>> nothing = piece => { };
            Action<Action<byte[], int, int>> empty = piece => piece(new byte[] { 1, 2, 3 }, 3, 0);

            foreach (var read in new[] { nothing, empty })
            {
                Assert.That(new JsonWriter().Base64("content", read).Done(), Is.EqualTo("{\"content\":\"\"}"));

                using (var text = new StringWriter())
                {
                    JsonWriter.Into(text, json => json.Base64("content", read));

                    Assert.That(text.ToString(), Is.EqualTo("{\"content\":\"\"}"));
                    Assert.That((string)JObject.Parse(text.ToString())["content"], Is.Empty);
                }
            }
        }

        [Test]
        public void A_member_written_after_the_bytes_is_separated_from_them_as_from_any_value()
        {
            // The quotes and the text between them go straight to the writer underneath, past
            // Json.NET, which still has to know a value was written: the member after it needs
            // its comma, and whatever is left open its closing.
            var bytes = Invented(100);
            var base64 = Convert.ToBase64String(bytes);
            Action<JsonWriter> describe = json => json
                .Text("name", "notes.txt")
                .Number("size", bytes.Length)
                .Base64("content", InPieces(bytes, 33))
                .Text("after", "an invented value")
                .Base64("again", InPieces(bytes, 99))
                .Object("o").Base64("inside", InPieces(bytes, 3)).Flag("f", true);

            var expected = "{\"name\":\"notes.txt\",\"size\":100,\"content\":\"" + base64
                + "\",\"after\":\"an invented value\",\"again\":\"" + base64
                + "\",\"o\":{\"inside\":\"" + base64 + "\",\"f\":true}}";

            var kept = new JsonWriter();
            describe(kept);
            Assert.That(kept.Done(), Is.EqualTo(expected));

            using (var text = new StringWriter())
            {
                JsonWriter.Into(text, describe);
                Assert.That(text.ToString(), Is.EqualTo(expected));

                var parsed = JObject.Parse(text.ToString());
                Assert.That(Convert.FromBase64String((string)parsed["content"]), Is.EqualTo(bytes));
                Assert.That(Convert.FromBase64String((string)parsed["again"]), Is.EqualTo(bytes));
                Assert.That(Convert.FromBase64String((string)parsed["o"]["inside"]), Is.EqualTo(bytes));
                Assert.That((string)parsed["after"], Is.EqualTo("an invented value"));
                Assert.That((bool)parsed["o"]["f"], Is.True);
            }
        }

        [Test]
        public void Bytes_as_the_first_member_follow_no_comma()
        {
            var written = new JsonWriter().Base64("content", InPieces(new byte[] { 1, 2, 3 }, 3)).Text("a", "b").Done();

            Assert.That(
                written,
                Is.EqualTo("{\"content\":\"" + Convert.ToBase64String(new byte[] { 1, 2, 3 }) + "\",\"a\":\"b\"}"));
        }

        [Test]
        public void A_piece_larger_than_a_step_of_the_writer_is_written_whole()
        {
            // The writer makes base64 of a few kilobytes at a time, whatever it is handed, so a
            // piece of many steps is cut into them, and only where the piece itself ends short of
            // a three is anything padded.
            var bytes = Invented(100000);
            Assume.That(bytes.Length % 3, Is.Not.Zero, "a last piece that ends inside a three is what this hands over");

            var written = new JsonWriter().Base64("content", piece => piece(bytes, 0, bytes.Length)).Done();

            Assert.That(written == "{\"content\":\"" + Convert.ToBase64String(bytes) + "\"}", Is.True);
        }

        [Test]
        public void A_piece_is_read_from_its_offset_for_its_count_and_no_further()
        {
            // An attachment held in the open is handed over as KeePass's own buffer at an offset,
            // and a protected attachment as a copy with a block ahead of the piece, so what is
            // written is the count from the offset and nothing either side of it.
            var held = Invented(64 * 1024);
            var expected = new byte[30000];
            Buffer.BlockCopy(held, 16, expected, 0, 30000);

            var written = new JsonWriter()
                .Base64("content", piece =>
                {
                    piece(held, 16, 12000);
                    piece(held, 12016, 18000);
                })
                .Done();

            Assert.That(written == "{\"content\":\"" + Convert.ToBase64String(expected) + "\"}", Is.True);
        }

        [Test]
        public void A_piece_after_the_piece_that_ended_the_bytes_is_refused()
        {
            // A piece that is not a whole number of threes is padded, and base64 pads only at
            // its end. Bytes written after it would be text no reader takes for what was handed
            // over, so the piece after it is refused and the document is left unfinished.
            using (var text = new StringWriter())
            {
                Action<Action<byte[], int, int>> past = piece =>
                {
                    piece(new byte[] { 1, 2, 3 }, 0, 3);
                    piece(new byte[] { 4, 5, 6, 7 }, 0, 4);
                    piece(new byte[] { 8, 9, 10 }, 0, 3);
                };

                Assert.Throws<InvalidOperationException>(
                    () => JsonWriter.Into(text, json => json.Base64("content", past)));

                Assert.That(
                    text.ToString(),
                    Is.EqualTo("{\"content\":\"" + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7 })),
                    "nothing follows the padding");
                Assert.That(text.ToString(), Does.EndWith("=="));
                Assert.That(
                    () => JObject.Parse(text.ToString()),
                    Throws.InstanceOf<Newtonsoft.Json.JsonException>(),
                    "what is left reads as no document at all");
            }

            Assert.Throws<InvalidOperationException>(() => new JsonWriter().Base64("content", piece =>
            {
                piece(new byte[] { 1 }, 0, 1);
                piece(new byte[] { 2 }, 0, 1);
            }));
        }

        [Test]
        public void Each_piece_reaches_the_other_writer_as_it_is_handed_over()
        {
            // Nothing of the bytes is kept here, which is the point of writing them this way: a
            // piece is with the writer underneath before the piece after it is read.
            using (var text = new StringWriter())
            {
                var seen = new System.Collections.Generic.List<string>();

                JsonWriter.Into(text, json => json.Text("name", "notes.txt").Base64("content", piece =>
                {
                    seen.Add(text.ToString());
                    piece(new byte[] { 1, 2, 3 }, 0, 3);
                    seen.Add(text.ToString());
                    piece(new byte[] { 4, 5, 6 }, 0, 3);
                    seen.Add(text.ToString());
                }));

                const string Opened = "{\"name\":\"notes.txt\",\"content\":\"";
                var first = Convert.ToBase64String(new byte[] { 1, 2, 3 });
                var both = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6 });

                Assert.That(seen, Is.EqualTo(new[] { Opened, Opened + first, Opened + both }));
                Assert.That(text.ToString(), Is.EqualTo(Opened + both + "\"}"));
            }
        }

        [Test]
        public void A_read_of_the_bytes_that_throws_leaves_text_that_is_not_a_finished_document()
        {
            // An attachment the store refuses part way is refused where its pieces are being
            // written, with the status long gone. What is left is open inside the string, so no
            // reader takes it for the attachment.
            using (var text = new StringWriter())
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => JsonWriter.Into(text, json => json
                    .Number("size", 6)
                    .Base64("content", piece =>
                    {
                        piece(new byte[] { 1, 2, 3 }, 0, 3);
                        throw new InvalidOperationException("an invented read that fails");
                    })));

                Assert.That(thrown.Message, Is.EqualTo("an invented read that fails"));
                Assert.That(
                    text.ToString(),
                    Is.EqualTo("{\"size\":6,\"content\":\"" + Convert.ToBase64String(new byte[] { 1, 2, 3 })));
                Assert.That(
                    () => JObject.Parse(text.ToString()),
                    Throws.InstanceOf<Newtonsoft.Json.JsonException>());
            }
        }

        [Test]
        public void Writing_bytes_needs_what_hands_them_over()
        {
            var writer = new JsonWriter();

            Assert.Throws<ArgumentNullException>(() => writer.Base64("content", null));
            Assert.That(writer.Done(), Is.EqualTo("{}"), "and nothing was written for the member");
        }

        /// <summary>Invented bytes, the same for the same size.</summary>
        private static byte[] Invented(int size)
        {
            var bytes = new byte[size];
            new Random(size).NextBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// What hands bytes over in order, in pieces of this many, the last holding what is
        /// left.
        /// </summary>
        private static Action<Action<byte[], int, int>> InPieces(byte[] bytes, int size)
        {
            return piece =>
            {
                for (var at = 0; at < bytes.Length; at += size)
                {
                    piece(bytes, at, Math.Min(size, bytes.Length - at));
                }
            };
        }
    }
}
