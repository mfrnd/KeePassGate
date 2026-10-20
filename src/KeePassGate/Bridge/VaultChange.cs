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
    ///
    /// **And an update that found the node already holding what it asked for carries none either**,
    /// because it touched nothing as well, and says so with <see cref="Unchanged"/>: unlike a
    /// refusal it is answered as having worked, so the writer has to be told that there is nothing
    /// to save rather than work it out.
    /// </remarks>
    public sealed class VaultChange
    {
        public VaultChange(VaultWrite outcome, Action undo)
        {
            Outcome = outcome;
            Undo = undo;
            Changed = true;
        }

        /// <summary>What the caller is told.</summary>
        public VaultWrite Outcome { get; private set; }

        /// <summary>Puts the database back as it was, or null when nothing was changed.</summary>
        public Action Undo { get; private set; }

        /// <summary>
        /// Whether the database was changed, and so whether there is anything to save.
        /// </summary>
        /// <remarks>
        /// False only for <see cref="Unchanged"/>. A refusal changed nothing either, and is never
        /// saved because it is a refusal, so the writer does not need this to know that.
        /// </remarks>
        public bool Changed { get; private set; }

        /// <summary>An outcome that carries nothing to undo, which is what every refusal is.</summary>
        public static implicit operator VaultChange(VaultWrite outcome)
        {
            return new VaultChange(outcome, null);
        }

        /// <summary>
        /// An update every one of whose values the node already held, answered as having worked
        /// without anything being changed or saved.
        /// </summary>
        /// <remarks>
        /// `openapi.yaml` §504 argues it: writing anyway took a history revision, moved the node's
        /// modification time and usage count, and saved the whole database, for a request whose
        /// answer would have been the same without any of it.
        /// </remarks>
        public static VaultChange Unchanged(VaultWrite outcome)
        {
            return new VaultChange(outcome, null) { Changed = false };
        }
    }
}
