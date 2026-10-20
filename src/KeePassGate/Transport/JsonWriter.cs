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
    /// </remarks>
    internal sealed class JsonWriter : IDisposable
    {
        private readonly StringWriter _text;
        private readonly JsonTextWriter _writer;

        private string _done;

        public JsonWriter()
        {
            _text = new StringWriter(CultureInfo.InvariantCulture);
            _writer = new JsonTextWriter(_text) { Formatting = Formatting.None };
            _writer.WriteStartObject();
        }

        /// <summary>The document, closed. Asking twice is asking for the same string.</summary>
        public string Done()
        {
            if (_done != null)
            {
                return _done;
            }

            // Whatever is still open is closed here, so a handler that describes something and
            // stops does not have to count its own brackets.
            _writer.WriteEndObject();
            _writer.Flush();

            _done = _text.ToString();
            return _done;
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
        /// Lets go of the writer and the buffer under it.
        /// </summary>
        /// <remarks>
        /// Neither holds anything a handler has to release in time, so this exists because they are
        /// disposable rather than because a caller is expected to remember, and no handler calls
        /// it. **`Done` comes first.** This closes the writer, so `Done` called afterwards answers
        /// with the string it had already made, and throws when it had made none.
        /// </remarks>
        public void Dispose()
        {
            ((IDisposable)_writer).Dispose();
            _text.Dispose();
        }
    }
}
