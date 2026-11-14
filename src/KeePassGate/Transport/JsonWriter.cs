using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace KeePassGate.Transport
{
    /// <summary>
    /// Writes the answers this API sends: objects, arrays, strings, numbers, booleans and null.
    /// </summary>
    /// <remarks>
    /// **Wider than what `JsonBody` reads, and deliberately so.** A request body is bytes from a
    /// caller that has proved nothing yet, so the reader is one flat object of strings and refuses
    /// everything else. A response is this plugin's own output, and a list of entries is an array of
    /// objects however strictly the other direction is bounded. The status route already answered
    /// with a boolean before any of this existed.
    ///
    /// Json.NET does the writing, including every escape, so nothing here can produce something
    /// that is not JSON. The base64 of an attachment is the exception to who writes, and none to
    /// who escapes: <see cref="Base64"/> writes that string itself, a piece at a time, between
    /// quotes of its own, and base64 holds nothing JSON escapes. A value handed over in pieces
    /// is written between quotes of this writer's own as well, each piece escaped by Json.NET,
    /// <see cref="TextInPieces"/>. What this adds is the shape a
    /// handler wants to write in: a fluent call per member, with the punctuation and the closing
    /// of whatever is still open left to the writer rather than to whoever is describing an
    /// entry.
    ///
    /// **A document is kept here whole, or written into a writer that is somebody else's.**
    /// Kept, it is handed back by <see cref="Done"/> as text. Written by <see cref="Into"/>,
    /// nothing of it is kept: each member goes to the writer underneath as it is described,
    /// which is how an answer made of an entry's values is written as it is sent,
    /// `HttpResponse.Write`.
    /// </remarks>
    internal sealed class JsonWriter : IDisposable
    {
        /// <summary>
        /// How many bytes are made into base64 at a time: a multiple of three, so that each
        /// step's text ends where a step of base64 does.
        /// </summary>
        private const int Base64Step = 12 * 1024;

        /// <summary>
        /// How many characters of a piece of text are escaped at a time. A piece no longer
        /// than this is escaped as it is, with nothing cut out of it, which is every piece a
        /// value read from its bytes comes in; a step and what it is escaped to are each
        /// under the size the runtime keeps apart for large objects, for text that is not
        /// mostly characters JSON escapes.
        /// </summary>
        private const int TextStep = 20 * 1024;

        /// <summary>
        /// How many characters of an escaped step are handed to the writer underneath at a
        /// time.
        /// </summary>
        private const int EscapedStep = 8 * 1024;

        private readonly StringWriter _text;
        private readonly TextWriter _into;
        private readonly JsonTextWriter _writer;

        private string _done;
        private bool _disposed;
        private char[] _escaped;

        public JsonWriter()
        {
            _text = new StringWriter(CultureInfo.InvariantCulture);
            _into = _text;
            _writer = new JsonTextWriter(_text) { Formatting = Formatting.None };
            _writer.WriteStartObject();
        }

        /// <summary>A document written into <paramref name="into"/> and kept nowhere.</summary>
        /// <remarks>
        /// Json.NET closes whatever is open when its writer is let go, which finishes a
        /// document nobody finished: turned off, so a document whose description failed part
        /// way is left as far as it got. The writer underneath is not closed with it.
        /// </remarks>
        private JsonWriter(TextWriter into)
        {
            _into = into;
            _writer = new JsonTextWriter(into)
            {
                Formatting = Formatting.None,
                CloseOutput = false,
                AutoCompleteOnClose = false,
            };
            _writer.WriteStartObject();
        }

        /// <summary>
        /// Writes a document into a writer that is somebody else's, as it is described, and
        /// closes whatever the description left open.
        /// </summary>
        /// <remarks>
        /// The document is closed and flushed only once <paramref name="describe"/> has
        /// returned. A description that throws leaves the document open where it stopped, so
        /// nothing downstream takes half a document for a whole.
        /// </remarks>
        public static void Into(TextWriter text, Action<JsonWriter> describe)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (describe == null)
            {
                throw new ArgumentNullException(nameof(describe));
            }

            using (var json = new JsonWriter(text))
            {
                describe(json);
                json.Close();
            }
        }

        /// <summary>The document, closed. Asking twice is asking for the same string.</summary>
        public string Done()
        {
            if (_done != null)
            {
                return _done;
            }

            if (_text == null)
            {
                throw new InvalidOperationException("a document written into another writer is kept nowhere");
            }

            if (_disposed)
            {
                // Asked rather than left to Json.NET, whose own disposal closes everything that is
                // open, so the loop below would find nothing to close and hand back a document
                // nobody finished.
                throw new ObjectDisposedException(nameof(JsonWriter), "Done comes before Dispose.");
            }

            Close();

            _done = _text.ToString();
            return _done;
        }

        /// <summary>Closes whatever is still open, and hands on everything written.</summary>
        private void Close()
        {
            // Whatever is still open is closed here, innermost first, so a handler that describes
            // something and stops does not have to count its own brackets. One at a time, because
            // Json.NET closes only as far as the nearest object when asked to end an object: an
            // object left open inside an array was closed and the array and the root were not, and
            // the answer was text that is not JSON.
            while (_writer.WriteState != WriteState.Start)
            {
                _writer.WriteEnd();
            }

            _writer.Flush();
        }

        public JsonWriter Text(string name, string value)
        {
            _writer.WritePropertyName(name);
            _writer.WriteValue(value);
            return this;
        }

        public JsonWriter Number(string name, long value)
        {
            _writer.WritePropertyName(name);
            _writer.WriteValue(value);
            return this;
        }

        /// <summary>
        /// Writes bytes as a base64 string, a piece at a time, as they are handed over.
        /// </summary>
        /// <remarks>
        /// **For an attachment, which is read a piece at a time and never whole.** Json.NET
        /// writes a string it is handed whole, and a byte array likewise, so there is nothing
        /// of it to write a string in pieces with. The quotes and the base64 between them
        /// are therefore written here, straight to the writer underneath.
        ///
        /// **That is no second way of escaping a string**, which is what keeping every value
        /// to Json.NET is for: the text between the quotes is what `Convert` makes of bytes,
        /// letters, digits, `+`, `/` and `=`, and JSON escapes none of those whatever the
        /// bytes are.
        /// </remarks>
        /// <param name="read">
        /// What hands the bytes over in order, each piece an array, an offset and a count. A
        /// piece that is not a whole number of threes ends the bytes: base64 pads there, so
        /// nothing may follow it.
        /// </param>
        public JsonWriter Base64(string name, Action<Action<byte[], int, int>> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            _writer.WritePropertyName(name);

            // The opening quote goes through Json.NET as the value it expects here, so that it
            // goes on to write the comma and the name of whatever follows.
            _writer.WriteRawValue("\"");

            var text = new char[(Base64Step / 3) * 4];
            var ended = false;
            try
            {
                read((bytes, offset, count) =>
                {
                    if (ended)
                    {
                        throw new InvalidOperationException("bytes were handed over after the piece that ended them");
                    }

                    ended = count % 3 != 0;
                    for (var at = 0; at < count; at += Base64Step)
                    {
                        var written = Convert.ToBase64CharArray(
                            bytes, offset + at, Math.Min(Base64Step, count - at), text, 0);
                        _into.Write(text, 0, written);
                    }
                });
            }
            finally
            {
                // What the bytes were made into is the bytes, to whoever reads memory that
                // left the process, and this is the only copy of that text here that can be
                // overwritten: the writer underneath keeps its own for as long as it lives.
                System.Array.Clear(text, 0, text.Length);
            }

            _into.Write('"');
            return this;
        }

        /// <summary>
        /// Writes a string from the text handed over, a piece at a time.
        /// </summary>
        /// <remarks>
        /// **For a field's value, which is read a piece at a time from where KeePass keeps
        /// it.** Json.NET writes a string it is handed whole, quotes included, so a string in
        /// pieces has its quotes written here, straight to the writer underneath, as
        /// <see cref="Base64"/> writes its own.
        ///
        /// **Json.NET escapes every piece all the same**, through `JsonConvert.ToString`, which
        /// escapes as its writer does and a character at a time, so what a piece is escaped to
        /// is what the same text is escaped to inside a longer string. It puts each piece
        /// between quotes of its own, and those are left out.
        ///
        /// **A piece of any length is escaped a step at a time.** The string KeePass keeps
        /// for a value held in the open is handed over whole, whatever its length, and
        /// Json.NET's own writer copies each run of a string between two characters it
        /// escapes into a buffer it keeps, as long as the run: megabytes, for a long value
        /// that ends in a line break. So what is alive of a value while it is written is a
        /// step of it and what that step was escaped to, whether it came in pieces or whole.
        /// </remarks>
        /// <param name="read">
        /// What hands the text over in order, and nothing for a string of no characters.
        /// </param>
        public JsonWriter TextInPieces(string name, Action<Action<string>> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            _writer.WritePropertyName(name);

            // The opening quote goes through Json.NET as the value it expects here, as the
            // opening quote of base64 does.
            _writer.WriteRawValue("\"");

            try
            {
                read(piece =>
                {
                    if (piece == null)
                    {
                        throw new ArgumentNullException(nameof(piece));
                    }

                    Escaped(piece);
                });
            }
            finally
            {
                if (_escaped != null)
                {
                    // The only copy of the escaped text here that can be overwritten.
                    System.Array.Clear(_escaped, 0, _escaped.Length);
                }
            }

            _into.Write('"');
            return this;
        }

        /// <summary>
        /// Writes a piece of text as Json.NET escapes it, a step at a time, without the quotes
        /// Json.NET puts around each step.
        /// </summary>
        private void Escaped(string piece)
        {
            for (var from = 0; from < piece.Length;)
            {
                var taken = Math.Min(TextStep, piece.Length - from);
                var quoted = JsonConvert.ToString(taken == piece.Length ? piece : piece.Substring(from, taken));
                from += taken;

                if (_escaped == null)
                {
                    _escaped = new char[EscapedStep];
                }

                for (var at = 1; at < quoted.Length - 1;)
                {
                    var count = Math.Min(_escaped.Length, quoted.Length - 1 - at);
                    quoted.CopyTo(at, _escaped, 0, count);
                    _into.Write(_escaped, 0, count);
                    at += count;
                }
            }
        }

        public JsonWriter Flag(string name, bool value)
        {
            _writer.WritePropertyName(name);
            _writer.WriteValue(value);
            return this;
        }

        /// <summary>Opens an object under a name. Closed by <see cref="End"/>.</summary>
        public JsonWriter Object(string name)
        {
            _writer.WritePropertyName(name);
            _writer.WriteStartObject();
            return this;
        }

        /// <summary>Opens an array under a name. Closed by <see cref="End"/>.</summary>
        public JsonWriter Array(string name)
        {
            _writer.WritePropertyName(name);
            _writer.WriteStartArray();
            return this;
        }

        /// <summary>Opens an object inside an array.</summary>
        public JsonWriter Item()
        {
            _writer.WriteStartObject();
            return this;
        }

        /// <summary>A string inside an array.</summary>
        public JsonWriter Item(string value)
        {
            _writer.WriteValue(value ?? string.Empty);
            return this;
        }

        /// <summary>
        /// Closes whatever was opened last, with the bracket it was opened with.
        /// </summary>
        /// <remarks>
        /// Which one that is, is the writer's own business rather than something a caller has to
        /// keep track of and could get wrong.
        /// </remarks>
        public JsonWriter End()
        {
            _writer.WriteEnd();
            return this;
        }

        /// <summary>
        /// Lets go of the writer and the buffer under it, where the document is kept here.
        /// </summary>
        /// <remarks>
        /// Neither holds anything a handler has to release in time, so this exists because they are
        /// disposable rather than because a caller is expected to remember, and no handler calls
        /// it. **`Done` comes first.** This closes the writer, so `Done` called afterwards answers
        /// with the string it had already made, and throws when it had made none.
        /// </remarks>
        public void Dispose()
        {
            _disposed = true;
            ((IDisposable)_writer).Dispose();
            if (_text != null)
            {
                _text.Dispose();
            }
        }
    }
}
