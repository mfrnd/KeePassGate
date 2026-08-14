// Authentication scheme name constants, matching the runtime's internal set. This listener
// authenticates with mutual TLS and offers none of these, but the vendored code names them.

namespace KeePassGate.Vendor.Http
{
    internal static class AuthenticationTypes
    {
        internal const string NTLM = "NTLM";
        internal const string Negotiate = "Negotiate";
        internal const string Basic = "Basic";
    }
}
