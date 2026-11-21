using KeePassGate.Authority;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassLib;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The authority of a database KeePass holds, as a test reaches it: its store read and written
    /// directly, with no writer asking first.
    /// </summary>
    internal static class DatabaseAuthority
    {
        public static AuthorityService Of(PwDatabase database, IGateLog log)
        {
            return new AuthorityService(Store(database), log);
        }

        /// <summary>
        /// The store of a database KeePass holds, read and written directly with no writer asking
        /// first: what a host fixture seeds an authority and rules into through `StoreSeeds`.
        /// </summary>
        public static KeePassGate.Store.IGateStore Store(PwDatabase database)
        {
            return new KdbxStore(database, GateVersion.Current, null);
        }
    }
}
