using System.Collections.Generic;
using KeePassGate.Acl;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Which verb a letter typed into the rule picker toggles.
    /// </summary>
    /// <remarks>
    /// **The letter is the verb's own first letter**, which is also the letter it occupies in a
    /// mask, so the keyboard and the column agree by construction rather than by a table somebody
    /// has to keep in step. `C` is create wherever it appears.
    ///
    /// **First declared wins a collision.** Nothing declared today collides, and the rule matters
    /// for what is declared tomorrow: a verb added later that starts with a letter already spoken
    /// for gets no key rather than taking one away from a verb operators have been pressing for
    /// years. The same order decides the mask, so a collision would be visible there first.
    ///
    /// It is derived from `AclVerbs.Grantable` every time rather than written down, because a
    /// written-down list is a list that disagrees with the verbs after somebody adds one.
    /// </remarks>
    public static class VerbKeys
    {
        /// <summary>
        /// Every letter that toggles something, and what it toggles.
        /// </summary>
        public static IDictionary<char, AclVerb> All()
        {
            var keys = new Dictionary<char, AclVerb>();

            foreach (var verb in AclVerbs.Grantable)
            {
                var letter = VerbMask.LetterOf(verb);
                if (!keys.ContainsKey(letter))
                {
                    keys[letter] = verb;
                }
            }

            return keys;
        }

        /// <summary>
        /// Which position in the picker a letter toggles, or -1 when the letter means nothing.
        /// </summary>
        /// <remarks>
        /// A position rather than a verb, because the picker is a list of check boxes in the order
        /// the verbs are declared and what it needs is which box to check.
        /// </remarks>
        public static int PositionOf(char typed)
        {
            var wanted = char.ToUpperInvariant(typed);

            for (var i = 0; i < AclVerbs.Grantable.Count; i++)
            {
                if (VerbMask.LetterOf(AclVerbs.Grantable[i]) == wanted)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
