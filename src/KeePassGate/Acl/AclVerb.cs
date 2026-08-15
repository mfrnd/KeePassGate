using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KeePassGate.Acl
{
    /// <summary>
    /// What a rule grants: six verbs, and two switches that say how far they reach.
    /// </summary>
    /// <remarks>
    /// **Six verbs, and no more without a reason.** A verb exists so that a rule can be written
    /// about it, so every one has to be something an operator would think to allow or refuse
    /// separately. Splitting them finer would make the editor longer without making anybody safer,
    /// and lumping them together would make "may read" and "may delete" the same permission.
    ///
    /// **Neither switch is a seventh verb.** `Attachments` says that whichever of the six a rule
    /// grants reach that entry's attachments as well as its fields; without it they reach the
    /// fields only, so an attachment is not listed, read, added or replaced no matter what else the
    /// rule says. That is the right way round because an attachment is usually the highest risk
    /// thing a database holds, and because "may read this entry" and "may take the file out of it"
    /// are two decisions an operator makes separately. `Protected` says the same about the values
    /// the database keeps encrypted, so a rule can hand over everything about an entry except what
    /// somebody could sign in with. Neither is asked about for a removal, because removing shows
    /// nobody anything.
    ///
    /// They are stored alongside the verbs rather than as members of their own, because a rule is
    /// one list of what it grants and a reader that has to look in three places for one answer is a
    /// reader that will look in one. What they mean is a scope, and the editor shows them that way.
    ///
    /// **Declared in the order they are shown**, which is `CRUSDLAP`, so a stored rule lists what
    /// it grants in the same order the editor does. `Strip` sits beside `Delete` because the two are
    /// the sizes of one act, and a mask that allows the smaller and not the larger differs where the
    /// eye already is.
    ///
    /// `List` is below `Read` on purpose: knowing that an entry exists is not knowing what is in it,
    /// and an agent that may search a database without opening it is a useful thing to describe.
    ///
    /// The operation declares which verb it needs, in the routing table, as `x-kpg-verb`, and says
    /// separately whether it reaches attachment contents. Both are declared there rather than as
    /// attributes in code so that the document stays the single account of what this API permits,
    /// and **an operation that declares no verb does not load**, so a route added without thinking
    /// about authorization is a route that will not serve.
    ///
    /// `None` is not a weaker verb, and it has no letter. It is for an operation that no rule
    /// governs: the status route, which reads nothing, and the two pairing routes, which are
    /// authorized by a person reading a dialog rather than by a grant, because a client that is
    /// pairing holds no profiles yet. It is written down rather than left absent, because absent
    /// would be indistinguishable from forgetting.
    /// </remarks>
    public enum AclVerb
    {
        /// <summary>Touches no data. Not a permission and not grantable.</summary>
        None = 0,

        /// <summary>Adds an entry to a group.</summary>
        Create,

        /// <summary>Reads what an entry holds, except the values the database protects, which
        /// the <see cref="Protected"/> switch adds.</summary>
        Read,

        /// <summary>Changes an entry that already exists.</summary>
        Update,

        /// <summary>
        /// Removes the parts inside a node: an entry's fields and its attachments.
        /// </summary>
        Strip,

        /// <summary>
        /// Removals are allowed here. Removing a node asks for this on both sides of the move,
        /// from the group the node is leaving and from the node itself.
        /// </summary>
        Delete,

        /// <summary>
        /// Sees that an entry exists, and what it is called, without reading what it holds.
        /// </summary>
        List,

        /// <summary>
        /// Not a verb. The switch that extends whichever verbs a rule grants to the attachments
        /// inside an entry as well as to its fields.
        /// </summary>
        Attachments,

        /// <summary>
        /// Not a verb either. The switch that extends whichever verbs a rule grants to the values
        /// the database protects, which is the password and any field an operator marked.
        /// </summary>
        Protected,
    }

    /// <summary>Reading a verb from text, which is how it arrives from a document.</summary>
    public static class AclVerbs
    {
        /// <summary>
        /// Everything a rule can hold, in the order it is declared, written and shown: the six
        /// verbs and then the two switches.
        /// </summary>
        /// <remarks>
        /// One list, so the editor's boxes, a document's ordering and anything that enumerates what
        /// can be granted cannot disagree about what there is or what order it comes in.
        ///
        /// **Read only, and not merely a field nobody reassigns.** A position in this list is a
        /// letter of every mask and the place of a verb in every document written, for the life of
        /// the process, so code able to sort it or write into it would change both everywhere at
        /// once. `readonly` on an array keeps the reference and nothing in it.
        /// </remarks>
        public static readonly IReadOnlyList<AclVerb> Grantable = new ReadOnlyCollection<AclVerb>(new[]
        {
            AclVerb.Create,
            AclVerb.Read,
            AclVerb.Update,
            AclVerb.Strip,
            AclVerb.Delete,
            AclVerb.List,
            AclVerb.Attachments,
            AclVerb.Protected,
        });

        /// <summary>
        /// Where a verb sits in <see cref="Grantable"/>, or -1 for one no rule can hold.
        /// </summary>
        public static int PositionOf(AclVerb verb)
        {
            for (var i = 0; i < Grantable.Count; i++)
            {
                if (Grantable[i] == verb)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether this says how far a rule reaches rather than what it may do.
        /// </summary>
        /// <remarks>
        /// True for `Attachments` and `Protected`, and neither can be declared as the verb an
        /// operation needs. No operation does attachments: one reads or replaces or removes, and
        /// whether the thing reached is an attachment is a separate question. Nor does any
        /// operation do protected values: one arrives inside the answer to a read, and whether
        /// the caller may see it is asked of the same rule at the same moment.
        /// </remarks>
        public static bool IsScope(AclVerb verb)
        {
            return verb == AclVerb.Attachments || verb == AclVerb.Protected;
        }

        /// <summary>
        /// The verb this text names, or null for anything else.
        /// </summary>
        /// <remarks>
        /// Fail closed. A verb nobody recognizes is not a verb that means nothing, it is a rule
        /// somebody wrote and this cannot honor, so it denies rather than being skipped. The
        /// alternative would be a typo in a grant silently widening what a client may do, because
        /// the rule it belonged to would have been dropped instead of applied.
        ///
        /// Case sensitive, and the document writes them in lower case. A comparison that ignored
        /// case would mean two spellings of one rule, and an editor that has to choose one.
        /// </remarks>
        public static AclVerb? Read(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            switch (text)
            {
                case "none": return AclVerb.None;
                case "create": return AclVerb.Create;
                case "read": return AclVerb.Read;
                case "update": return AclVerb.Update;
                case "strip": return AclVerb.Strip;
                case "delete": return AclVerb.Delete;
                case "list": return AclVerb.List;
                case "attachments": return AclVerb.Attachments;
                case "protected": return AclVerb.Protected;
                default: return null;
            }
        }

        /// <summary>How a verb is written, which is how a document and an editor both spell it.</summary>
        public static string Write(AclVerb verb)
        {
            switch (verb)
            {
                case AclVerb.None: return "none";
                case AclVerb.Create: return "create";
                case AclVerb.Read: return "read";
                case AclVerb.Update: return "update";
                case AclVerb.Strip: return "strip";
                case AclVerb.Delete: return "delete";
                case AclVerb.List: return "list";
                case AclVerb.Attachments: return "attachments";
                case AclVerb.Protected: return "protected";
                default:
                    throw new ArgumentOutOfRangeException(nameof(verb));
            }
        }

        /// <summary>
        /// Whether this verb is one a rule can be written about.
        /// </summary>
        /// <remarks>
        /// `None` is not. An operation that touches no data needs no grant, and a grant of `none`
        /// would read as permission to do something rather than as permission to do nothing.
        /// </remarks>
        public static bool IsGrantable(AclVerb verb)
        {
            return verb != AclVerb.None;
        }
    }
}
