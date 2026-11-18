namespace KeePassGate.Store
{
    /// <summary>
    /// What the reading makes of the store as a whole, in the order `STORE.md` §656 answers them:
    /// a store in two of these states is in the earlier.
    /// </summary>
    public enum StoreState
    {
        /// <summary>No store key is found, so nothing is recognized as the store's, `STORE.md` §632.</summary>
        None,

        /// <summary>Records under more than a single store key stand, `STORE.md` §649.</summary>
        Split,

        /// <summary>
        /// The store states a format above the plugin's, or holds a record, an item or an object
        /// of a kind the plugin does not know, `STORE.md` §651 and §621.
        /// </summary>
        Outdated,

        /// <summary>The store is not whole, `STORE.md` §624.</summary>
        Stopped,

        /// <summary>A single store key stands, the store is of the plugin's format, and it is whole.</summary>
        Whole,
    }

    /// <summary>
    /// The words a client is answered with under `503` while the store holds the gate back,
    /// `STORE.md` §656, each sent in the body's `error` and in `KPG-Unavailable` as `locked` and
    /// `undecided` are.
    /// </summary>
    /// <remarks>
    /// **Part of the contract**: the routing document declares each, and the reference client has
    /// a class for each. A word added here alone fails the test that holds the document's words to
    /// the words the code can send.
    /// </remarks>
    public static class StoreWords
    {
        /// <summary>Several store keys stand, until the operator keeps a store.</summary>
        public const string Split = "split";

        /// <summary>
        /// The store was raised above the plugin, or holds a record of a kind it does not know,
        /// until the plugin is updated or the store is started again.
        /// </summary>
        public const string Outdated = "outdated";

        /// <summary>The store is stopped, until it is sealed again.</summary>
        public const string Stopped = "stopped";

        /// <summary>
        /// The file holds no store key, answered only by a gate that was serving the database when
        /// the key went, from what it served with, until the database is locked or closed.
        /// </summary>
        public const string Lost = "lost";

        /// <summary>
        /// The word the gate answers a store in this state with, or null where it holds nothing
        /// back: <see cref="Of"/>, and <see cref="Lost"/> for a file with no store key.
        /// </summary>
        /// <remarks>
        /// **Apart from <see cref="Of"/>, because a write and the tab read the same state as no
        /// store yet**, which a Create starts and the tab offers to create: nothing in a file
        /// tells a key lost from a store never made. The gate answers it only from what it last
        /// served a database with, so the word reaches only a client that was being served when
        /// the key went.
        /// </remarks>
        public static string Answered(StoreState state)
        {
            return state == StoreState.None ? Lost : Of(state);
        }

        /// <summary>
        /// The word a database whose store is in this state is answered with, or null where the
        /// store holds nothing back: a store that is whole, and a database with no store key.
        /// </summary>
        public static string Of(StoreState state)
        {
            switch (state)
            {
                case StoreState.Split:
                    return Split;
                case StoreState.Outdated:
                    return Outdated;
                case StoreState.Stopped:
                    return Stopped;
                case StoreState.None:
                case StoreState.Whole:
                    return null;
                default:
                    // A state nothing here names holds the gate back, in the strictest word.
                    return Stopped;
            }
        }
    }
}
