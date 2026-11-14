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
    /// that is not JSON. What this adds is the shape a handler wants to write in: a fluent call per
    /// member, with the punctuation and the closing of whatever is still open left to the writer
    /// rather than to whoever is describing an entry.
    ///
    /// **A document is kept here whole, or written into a writer that is somebody else's.**
    /// Kept, it is handed back by <see cref="Done"/> as text. Written by <see cref="Into"/>,
    /// nothing of it is kept: each member goes to the writer underneath as it is described,
    /// which is how an answer made of an entry's values is written as it is sent,
    /// `HttpResponse.Write`.
    /// </remarks>
    internal sealed class JsonWriter : IDisposable
    {
        private readonly StringWriter _text;
        private readonly JsonTextWriter _writer;

        private string _done;
        private bool _disposed;

        public JsonWriter()
        {
            _text = new StringWriter(CultureInfo.InvariantCulture);
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
