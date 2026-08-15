using System;
using KeePassGate.Vault;

namespace KeePassGate.Bridge
{
    /// <summary>What a change did, and how to put it back if it never reaches the disk.</summary>
    /// <remarks>
    /// **The undo comes from the change itself**, because only the change knows it: the entry
    /// that was created, the string that was written over, the group a removal came out of. A
    /// writer holding a database cannot work any of that out afterwards, and a snapshot of the
    /// whole thing is not something KeePass offers.
    ///
    /// **A refusal carries none**, which is what the conversion below is for: `VaultWrite.Missing`
    /// and its neighbors are returned from a dozen places that touched nothing, and each of them
    /// stays the single line it was rather than restating that there is nothing to take back.
    /// </remarks>
    public sealed class VaultChange
    {
        public VaultChange(VaultWrite outcome, Action undo)
        {
            Outcome = outcome;
            Undo = undo;
        }

        /// <summary>What the caller is told.</summary>
        public VaultWrite Outcome { get; private set; }

        /// <summary>Puts the database back as it was, or null when nothing was changed.</summary>
        public Action Undo { get; private set; }

        /// <summary>An outcome that changed nothing, and so has nothing to undo.</summary>
        public static implicit operator VaultChange(VaultWrite outcome)
        {
            return new VaultChange(outcome, null);
        }
    }
}
