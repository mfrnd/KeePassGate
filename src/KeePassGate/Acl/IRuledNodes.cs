using System.Collections.Generic;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>
    /// Every group and entry in a database a rule can be in force at, one node at a time.
    /// </summary>
    /// <remarks>
    /// A seam because reaching every group and entry means KeePass, and counting the rules in
    /// force at them is worth testing without KeePass. What is behind it walks the tree; what is
    /// above it knows each node by its identifier and reads the rules from the plugin's store,
    /// `StoredRules`.
    ///
    /// **Read, and never written.** Removing a profile takes it off the database's list and
    /// touches no rule: a rule for a name the list does not hold grants nothing, because a
    /// client's profiles are its record's less every name missing from the list. So the one
    /// thing this plugin does across the whole tree is count.
    /// </remarks>
    public interface IRuledNodes
    {
        /// <summary>
        /// Every group and entry. Not the revisions an entry keeps, which are no node of their
        /// own; not the recycle bin or anything in it, whose rules are in force for nobody until
        /// somebody moves the node back out; and not a node of the plugin's store, which takes no
        /// rule, its entries told as <paramref name="reading"/> tells them.
        /// </summary>
        IEnumerable<IRuledNode> All(StoreReading reading);

        /// <summary>
        /// Every group and entry the tree holds, the recycle bin and everything in it among them,
        /// and not a node of the plugin's store: what the dialog that seals the store again names
        /// a rule's node by, `STORE.md` §625, since a rule a store lost may be about a node
        /// removed into the bin.
        /// </summary>
        IEnumerable<IRuledNode> Held(StoreReading reading);
    }

    /// <summary>One group or entry, as a count sees it.</summary>
    public interface IRuledNode
    {
        /// <summary>
        /// The node's identifier as a rule names it: the 32 lowercase hexadecimal characters of
        /// its bytes.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// What the node is called, a group's name or an entry's title, read when it is asked
        /// for: what the dialog that seals the store again names a rule's node by, `STORE.md` §625,
        /// as <see cref="IRuledNodes.Held"/> hands the node over.
        /// </summary>
        string Title { get; }
    }
}
