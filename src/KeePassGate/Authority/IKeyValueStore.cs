using System.Collections.Generic;

namespace KeePassGate.Authority
{
    /// <summary>
    /// The seam over KeePass's custom data: a flat dictionary of strings, living in one
    /// database's own `PwDatabase.CustomData`.
    /// </summary>
    /// <remarks>
    /// An interface rather than the KeePass type, for the usual reason and one particular one. The
    /// usual reason is that everything above it becomes testable without KeePass. The particular
    /// one is that this store has a hazard the type does not express: a dialog can be holding a
    /// copy of it, and writing here while that is true is a write that a later OK destroys. Naming
    /// the seam is what lets that be reasoned about in one place.
    /// </remarks>
    public interface IKeyValueStore
    {
        /// <summary>The value, or null when absent. Never throws for a missing key.</summary>
        string Get(string key);

        void Set(string key, string value);

        /// <summary>Removes the key if it is there. A no-op otherwise.</summary>
        void Remove(string key);

        /// <summary>Every key currently held, in no particular order.</summary>
        IEnumerable<string> Keys { get; }
    }
}
