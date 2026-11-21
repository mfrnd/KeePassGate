using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The tree of groups and entries a write above is carried down, every node known by its
    /// identifier, `STORE.md` §617.
    /// </summary>
    /// <remarks>
    /// A seam because the tree is KeePass's, and which versions below a node an OK writes anew is
    /// worth testing without KeePass. What is behind it is the tree as it stands in the turn the
    /// write is made in, below the node it was walked from: that node, every node below it at
    /// which a version of the rules it was made for lets a profile through, and every group
    /// between, which is all that a write above or a tab asks of it. What is above it reads the
    /// rules of each node from the plugin's store.
    ///
    /// **The recycle bin is in it**, where KeePass puts it, so a node lies there below the bin
    /// and the groups above the bin. Left out, a node in the bin kept what it recorded through
    /// every change above while it lay there, and once restored and merged with a copy that had
    /// changed the node's new place apart, gave what neither copy gave, `STORE.md` §639. So a
    /// write above carries a node in the bin as it carries any node, and a node restored gives
    /// what arrives at its new place cut to what arrived by the bin's path. What that costs: a
    /// renewal, a version and a deletion record, for each node in the bin that follows, though
    /// the vault reaches nothing there, `DESIGN.md` §138; the bin's nodes walked at each write
    /// above them; and a node restored under a group that gives more than arrived by the bin's
    /// path coming back cut, shown on its tab as a limit, until the limit is settled there.
    ///
    /// **Not the plugin's store.** No rule of the operator's is in it, and a rule a merge put
    /// there reaches no client, `STORE.md` §633, so a node in it is no node below anything and a
    /// write above leaves its versions as they stand.
    /// </remarks>
    public interface IRuledTree
    {
        /// <summary>
        /// The node directly above this node, as the 32 lowercase hexadecimal characters of its
        /// identifier: null at the node the tree was walked from, and for a node the tree does not
        /// hold.
        /// </summary>
        string Above(string node);

        /// <summary>
        /// Every node below this node that the tree holds, each once, as the 32 lowercase
        /// hexadecimal characters of its identifier, in no order a caller may rely on: none for an
        /// entry, and none for a node the tree does not hold.
        /// </summary>
        IEnumerable<string> Below(string node);
    }
}
