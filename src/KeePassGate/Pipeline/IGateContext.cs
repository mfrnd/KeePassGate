using System.Collections.Generic;
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
    /// about framing.
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

        bool Answered { get; }
    }
}
