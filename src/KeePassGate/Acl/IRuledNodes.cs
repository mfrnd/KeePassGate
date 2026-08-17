using System.Collections.Generic;

namespace KeePassGate.Acl
{
    /// <summary>
    /// Where every rule in a database can be read, one node at a time.
    /// </summary>
    /// <remarks>
    /// A seam because reaching every group and entry means KeePass, and counting what they grant is
    /// worth testing without one. What is behind it walks the tree; what is above it only knows
    /// there are nodes with documents on them.
    ///
    /// **Read, and never written.** Removing a profile takes it off the database's list and
    /// touches no node: a rule for a name the list does not hold grants nothing, because a
    /// client's profiles are its record's less every name missing from the list. So the one
    /// thing this plugin does across the whole tree is count.
    /// </remarks>
    public interface IRuledNodes
    {
        /// <summary>
        /// Every group and entry, whether or not it carries rules. Not the revisions an entry
        /// keeps: a revision's rules are in force for nobody until somebody restores it.
        /// </summary>
        IEnumerable<IRuledNode> All();
    }

    /// <summary>One node's rules, as a count sees them.</summary>
    public interface IRuledNode
    {
        /// <summary>The document stored here, or null when there is none.</summary>
        string Read();
    }
}
