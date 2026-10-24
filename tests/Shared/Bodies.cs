using System;
using System.Collections.Generic;
using System.Text;
using KeePassGate.Transport;

namespace KeePassGate.Testing
{
    /// <summary>
    /// A request body as a test means it, written by the writer the plugin answers with.
    /// </summary>
    /// <remarks>
    /// **For a body built from values rather than spelled out**, because a value pasted between two
    /// quotes is escaped by nobody. One holding a quote, a backslash or a line break makes a body
    /// that is not JSON, or JSON about something else, and a test expecting a refusal then passes
    /// for that reason. Written here, every value is escaped as Json.NET escapes it, which is how
    /// <see cref="JsonWriter"/> escapes everything the plugin writes.
    ///
    /// Literal text stays where the bytes are what is being tested: an escape spelled one way
    /// rather than another, a body that is not JSON, and the reader's own tests.
    /// </remarks>
    public static class Bodies
    {
        /// <summary>
        /// An object of members in the order given, each value a string or a list of strings,
        /// which is everything a request body here may hold.
        /// </summary>
        /// <remarks>
        /// A pair per member rather than names and values in turn, so a name cannot lose its value
        /// to a miscount. A name given twice throws, since the gate refuses a body that holds one
        /// twice and a test expecting a refusal would pass for that reason.
        /// </remarks>
        public static string Of(params (string Name, object Value)[] members)
        {
            var named = new HashSet<string>(StringComparer.Ordinal);

            using (var writer = new JsonWriter())
            {
                foreach (var member in members)
                {
                    if (member.Name == null || !named.Add(member.Name))
                    {
                        throw new ArgumentException(
                            (member.Name ?? "A member") + " is named twice or not at all.", nameof(members));
                    }

                    switch (member.Value)
                    {
                        case string text:
                            writer.Text(member.Name, text);
                            break;

                        case IEnumerable<string> items:
                            writer.Array(member.Name);
                            foreach (var item in items)
                            {
                                writer.Item(item);
                            }

                            writer.End();
                            break;

                        default:
                            throw new ArgumentException(
                                member.Name + " is neither a string nor a list of strings.", nameof(members));
                    }
                }

                return writer.Done();
            }
        }

        /// <summary>
        /// The length a connection would announce for this body, which counts its bytes in UTF-8
        /// rather than its characters.
        /// </summary>
        public static long LengthOf(string body)
        {
            return body == null ? 0 : Encoding.UTF8.GetByteCount(body);
        }
    }
}
