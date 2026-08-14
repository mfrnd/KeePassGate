// The remaining pieces the vendored sources expect from the layer this port replaces.
//
// Upstream, HttpListener owns prefixes, authentication, and the SSL stream construction the
// managed implementation never actually reaches. Here it is a placeholder: the type has to exist
// because the vendored context refers to it, and the one method on it is only called from the
// original socket constructor, which this port does not use.

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Text;

namespace KeePassGate.Vendor.Http
{
    internal sealed class HttpListener
    {
        internal AuthenticationSchemes AuthenticationSchemes { get; set; } = AuthenticationSchemes.Anonymous;

        internal AuthenticationSchemeSelector? AuthenticationSchemeSelectorDelegate { get; set; }

        internal string? Realm { get; set; }

        internal bool IgnoreWriteExceptions { get; set; }

        internal void AddConnection(HttpConnection connection) { }

        internal void RemoveConnection(HttpConnection connection) { }

        internal void RegisterContext(HttpListenerContext context) { }

        /// <summary>
        /// Only reached from the socket constructor this port replaces. Upstream's own comment
        /// records that the managed implementation never negotiates TLS, so this is unreachable
        /// here by construction, and saying so loudly beats returning something half-working.
        /// </summary>
        internal static SslStream CreateSslStream(Stream innerStream, bool ownsStream,
                                                  RemoteCertificateValidationCallback callback)
        {
            throw new InvalidOperationException(
                "TLS is terminated before the vendored listener sees the stream. " +
                "Use the constructor that takes an already-authenticated stream.");
        }
    }

    /// <summary>
    /// Request cookie parsing, which leans on a BCL internal that .NET Framework does not expose.
    ///
    /// This listener serves an mTLS-authenticated API and carries no cookie-based state, so rather
    /// than reimplementing a parser for a feature the design does not offer, no cookies are
    /// surfaced. <c>Request.Cookies</c> is therefore always empty. That is a real behavioral
    /// divergence from upstream and is recorded here rather than hidden.
    /// </summary>
    internal sealed class CookieParser
    {
        public CookieParser(string cookieString)
        {
        }

        public Cookie? Get() { return null; }

        public Cookie? GetServer() { return null; }
    }
}
