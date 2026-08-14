// Stand-ins for BCL internals the vendored sources reach for and .NET Framework does not expose
// publicly. Each records how faithful it is, because these are the places where the vendored code
// stops being exactly upstream's.

using System;
using System.Net;

namespace KeePassGate.Vendor.Http
{
    internal static class CookieShim
    {
        /// <summary>
        /// Upstream calls the BCL's internal <c>CookieCollection.InternalAdd(cookie, isStrict: true)</c>,
        /// which answers 1 when the cookie was stored and 0 when an equivalent one existed and could
        /// not be replaced. The public <see cref="CookieCollection.Add(Cookie)"/> replaces silently
        /// and returns nothing, so the strict distinction is lost and 1 is the only honest answer.
        /// The visible consequence is that setting a duplicate cookie succeeds here where upstream
        /// would raise. That is a real divergence, small, and recorded rather than hidden.
        /// </summary>
        internal static int StrictAdd(CookieCollection cookies, Cookie cookie)
        {
            cookies.Add(cookie);
            return 1;
        }

        /// <summary>Cookie.Clone is internal to the BCL; the public properties are copied instead.</summary>
        internal static Cookie Clone(Cookie source)
        {
            return new Cookie(source.Name, source.Value, source.Path, source.Domain)
            {
                Comment = source.Comment,
                CommentUri = source.CommentUri,
                Discard = source.Discard,
                Expires = source.Expires,
                HttpOnly = source.HttpOnly,
                Port = source.Port,
                Secure = source.Secure,
                Version = source.Version,
            };
        }

        /// <summary>RFC 2965 is not offered, so the variant question has one answer.</summary>
        internal static bool IsRfc2965Variant(Cookie cookie) { return false; }

        /// <summary>
        /// Cookie.ToServerString is internal to the BCL. This restates the Set-Cookie form for the
        /// attributes the public surface exposes. It is the one piece of upstream behavior this
        /// port reimplements rather than carries, and it is only reachable if a handler chooses to
        /// set a cookie, which this design does not.
        /// </summary>
        internal static string ToServerString(Cookie cookie)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(cookie.Name).Append('=').Append(cookie.Value);
            if (!string.IsNullOrEmpty(cookie.Path)) sb.Append("; Path=").Append(cookie.Path);
            if (!string.IsNullOrEmpty(cookie.Domain)) sb.Append("; Domain=").Append(cookie.Domain);
            if (cookie.Expires != DateTime.MinValue)
            {
                sb.Append("; Expires=").Append(cookie.Expires.ToUniversalTime().ToString("R"));
            }
            if (cookie.Secure) sb.Append("; Secure");
            if (cookie.HttpOnly) sb.Append("; HttpOnly");
            return sb.ToString();
        }
    }

    /// <summary>
    /// WebSocket upgrade, which this project does not offer. Upstream reaches an internal helper
    /// here; the analysis calls for refusing unwanted features at the boundary rather than deleting
    /// the code that would serve them, so the calls remain and fail loudly if anything ever gets
    /// this far.
    /// </summary>
    internal static class HttpWebSocket
    {
        internal const string SecWebSocketKeyGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        internal const string WebSocketUpgradeToken = "websocket";
        internal const int DefaultReceiveBufferSize = 16 * 1024;
        internal const int DefaultClientSendBufferSize = 16 * 1024;
        internal const int MinSendBufferSize = 16;

        internal static System.Threading.Tasks.Task<System.Net.WebSockets.HttpListenerWebSocketContext> AcceptWebSocketAsyncCore(
            HttpListenerContext context, string? subProtocol, int receiveBufferSize,
            TimeSpan keepAliveInterval, System.ArraySegment<byte> internalBuffer = default)
        {
            throw new PlatformNotSupportedException("WebSocket upgrade is not offered by this listener.");
        }

        internal static bool AreWebSocketsSupported
        {
            get { return false; }
        }

        internal static void ValidateOptions(string subProtocol, int receiveBufferSize,
                                             int sendBufferSize, TimeSpan keepAliveInterval)
        {
            throw new PlatformNotSupportedException("WebSocket upgrade is not offered by this listener.");
        }
    }

    internal static class StreamArguments
    {
        /// <summary>The argument validation Stream gained in .NET 6, restated.</summary>
        internal static void ValidateBufferArguments(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > buffer.Length - offset) throw new ArgumentOutOfRangeException(nameof(count));
        }
    }
}
