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
    /// **No two verbs share a letter**, which `VerbKeysTests` holds: a verb added later that
    /// starts with a letter already spoken for would leave one of the two with no key to type and
    /// the mask with two positions spelled the same, so it has to be given a letter of its own.
    ///
    /// It is derived from `AclVerbs.Grantable` every time rather than written down, because a
    /// written-down list is a list that disagrees with the verbs after somebody adds one.
    /// </remarks>
    public static class VerbKeys
    {
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
