using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace KeePassGate.Pipeline
{
    /// <summary>
    /// One request, as everything above the transport sees it.
    /// </summary>
    /// <remarks>
    /// The seam `DESIGN.md` §103 names. The router, the authorization layer and the audit log
    /// compile against this and never against a socket, a stream or a certificate, so replacing
    /// what is underneath is a new adapter and nothing else. That is not a hypothetical: the
    /// transport under it has already been replaced once.
    ///
    /// <see cref="Body"/> is a string, read once and bounded, because the transport is what
    /// enforces the limit and refuses a framing it does not understand. Nothing above here thinks
    /// about framing, of an answer either: an answer is handed over as text, whole or as what
    /// writes it, and how its end is marked on the wire is the transport's.
    /// </remarks>
    public interface IGateContext
    {
        /// <summary>Upper case, as sent.</summary>
        string Method { get; }

        /// <summary>The absolute path, with the query already split off.</summary>
        string Path { get; }

        IReadOnlyDictionary<string, string> Query { get; }

        /// <summary>Case insensitive, null when absent.</summary>
        string GetHeader(string name);

        /// <summary>Read once, bounded, and never null.</summary>
        string Body { get; }

        /// <summary>Present always, paired only sometimes.</summary>
        ClientIdentity Client { get; }

        /// <summary>Per request, for pipeline stages to leave things for each other.</summary>
        IDictionary<string, object> Items { get; }

        /// <summary>Answers, once. A second call is ignored rather than allowed to corrupt a response.</summary>
        void Respond(int status, string json);

        /// <summary>The same, carrying header fields the writer has no way to know.</summary>
        /// <remarks>
        /// Every field the writer sends is the same on every answer, the content type and the
        /// caching and the closed connection among them, so it holds them and nothing has to pass
        /// them.
        /// `Allow` is the first that is not: RFC 9110 section 15.5.6 requires a `405` to list
        /// the methods the target does serve, and which those are is a question about the
        /// request rather than about this server.
        ///
        /// A `WebHeaderCollection` rather than a dictionary of strings, and that is the whole
        /// reason for choosing it: it refuses a name or a value carrying `CR`, `LF` or a null,
        /// which is what turns a header somebody influenced into a second response. Measured
        /// rather than read off the documentation. It does not refuse `Content-Length`, so the
        /// writer refuses the fields it owns itself.
        /// </remarks>
        void Respond(int status, string json, WebHeaderCollection headers);

        /// <summary>
        /// Answers, once, with content that is written as it is sent and kept nowhere.
        /// </summary>
        /// <remarks>
        /// **For an answer made of an entry's values**, which no write is refused for the size
        /// of. Handed over whole, such an answer is alive several times over in the password
        /// manager's own process; written as it is sent, what is alive is the value being
        /// written. `HttpResponse.Write` has the measurement and the framing.
        ///
        /// **Everything that can refuse comes before this.** Calling it decides the status,
        /// and <paramref name="content"/> runs once that status is on the wire, so what it
        /// throws cannot be answered: the connection is closed over an answer left without its
        /// end, which a client takes none of. It is not run for a HEAD, and not at all where
        /// the answer is dropped.
        ///
        /// **What it writes is the whole document and nothing else, and it reads no
        /// database.** It runs on the connection's thread once the router is done with the
        /// request, after any hold and outside any turn, so it is handed what was taken
        /// inside them, as <see cref="ServedAfter"/> holds of the work a read leaves.
        /// </remarks>
        /// <param name="content">What writes the answer's JSON, as text.</param>
        /// <param name="headers">Fields the writer has no way to know, or null.</param>
        void Respond(int status, Action<TextWriter> content, WebHeaderCollection headers);

        bool Answered { get; }
    }
}
