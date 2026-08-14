// Validation helpers the vendored WebSocket path calls. That path is refused before it runs, so
// these exist to compile rather than to run.

using System;

namespace KeePassGate.Vendor.Http
{
    internal static class WebSocketValidate
    {
        internal static void ValidateArraySegment(ArraySegment<byte> arraySegment, string parameterName)
        {
            throw new PlatformNotSupportedException("WebSocket upgrade is not offered by this listener.");
        }

        internal static void ValidateSubprotocol(string subProtocol)
        {
            throw new PlatformNotSupportedException("WebSocket upgrade is not offered by this listener.");
        }
    }
}
