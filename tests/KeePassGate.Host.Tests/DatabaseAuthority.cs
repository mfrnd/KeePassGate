using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassLib;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The authority of a database KeePass holds, as a test reaches it: its store read and written
    /// directly, with no writer asking first, and its admissions in the database's own custom data.
    /// </summary>
    internal static class DatabaseAuthority
    {
        public static AuthorityService Of(PwDatabase database, IGateLog log)
        {
            return new AuthorityService(
                new KdbxStore(database, GateVersion.Current, null), log);
        }
    }
}
