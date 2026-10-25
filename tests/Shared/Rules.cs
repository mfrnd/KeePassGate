using System;
using System.Collections.Generic;
using System.Globalization;
using KeePassGate.Acl;

namespace KeePassGate.Testing
{
    /// <summary>
    /// A node's rules as a test means them, written the way the grant editor writes them.
    /// </summary>
    /// <remarks>
    /// **Through the plugin's own writer rather than spelled by hand**, because a document spelled
    /// by hand fails in the safe direction: a verb misspelled, or a quote dropped, is a document
    /// that will not read, and that refuses where it is found, so a test expecting a refusal passes
    /// even when the rule it meant would have allowed the request. Built here, a mask that is not
    /// exactly one throws as the test sets up, and what the node holds is a document
    /// <see cref="AclDocument.Read"/> is held to.
    ///
    /// A rule is a `CRUSDLAP` mask, as the editor shows it, in one of two forms: all eight places,
    /// a dot for each verb it does not hold, as `.R...L..`, or only the letters it holds, as `RL`,
    /// since counting dots is where a mask written by hand goes wrong. Either way the letters keep
    /// their order and each is its own, so `LR` and `.L...R..` both throw rather than being read
    /// as the rule they would have to be guessed into, and so does a short form with a dot in it,
    /// which is a long one miscounted. An empty mask is a rule granting nothing.
    ///
    /// Raw text stays where the text is what is being tested: the reader, and a document no
    /// editor would write.
    /// </remarks>
    public static class Rules
    {
        /// <summary>
        /// A document granting each profile its mask, the rest asking the group above.
        /// </summary>
        /// <remarks>
        /// A pair per profile rather than profiles and masks in turn, so a profile cannot lose its
        /// mask to a miscount, nor take the next profile's.
        /// </remarks>
        public static string Granting(params (string Profile, string Mask)[] rules)
        {
            return Written(AclDefault.Inherit, rules);
        }

        /// <summary>
        /// A document sealing the node, so a profile it does not name is refused here whatever is
        /// granted above, with the profiles and masks it does name.
        /// </summary>
        public static string Sealing(params (string Profile, string Mask)[] rules)
        {
            return Written(AclDefault.Deny, rules);
        }

        /// <summary>
        /// A document granting one mask to each of as many profiles as asked for, named
        /// `profile-00` onwards, which is how a test fills a table past what it can show.
        /// </summary>
        public static string Numbered(int count, string mask)
        {
            var rules = new List<(string Profile, string Mask)>();
            for (var i = 0; i < count; i++)
            {
                rules.Add(("profile-" + i.ToString("00", CultureInfo.InvariantCulture), mask));
            }

            return Written(AclDefault.Inherit, rules);
        }

        private static string Written(AclDefault fallback, IEnumerable<(string Profile, string Mask)> rules)
        {
            var grants = new Dictionary<string, IEnumerable<AclVerb>>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                if (rule.Profile == null)
                {
                    throw new ArgumentException("A rule names no profile.", nameof(rules));
                }

                var verbs = Verbs(rule.Mask);
                if (verbs == null)
                {
                    throw new ArgumentException(
                        "\"" + rule.Mask + "\" for " + rule.Profile + " is not a mask: either all " +
                        VerbMask.Width + " places of " + VerbMask.Letters + ", a dot for each verb it " +
                        "does not hold, or only the letters it holds, in that order.", nameof(rules));
                }

                if (grants.ContainsKey(rule.Profile))
                {
                    throw new ArgumentException(rule.Profile + " is named twice.", nameof(rules));
                }

                grants.Add(rule.Profile, verbs);
            }

            return AclDocument.Of(fallback, grants).Write();
        }

        /// <summary>What a mask in either form holds, or null for anything that is not one.</summary>
        private static IList<AclVerb> Verbs(string mask)
        {
            if (mask == null)
            {
                return null;
            }

            // Upper case only, as the editor shows a mask, which its own reader does not insist on.
            foreach (var letter in mask)
            {
                if (char.IsLower(letter))
                {
                    return null;
                }
            }

            if (mask.Length == VerbMask.Width)
            {
                // Every place is there, so every place is held to its own letter, which is what
                // the editor's own reader does.
                return mask.Trim() == mask ? VerbMask.Read(mask) : null;
            }

            var letters = VerbMask.Letters;
            var held = new List<AclVerb>();
            var from = 0;

            foreach (var letter in mask)
            {
                // Searched for after the last one found, so a letter out of order is not found.
                var at = letters.IndexOf(letter, from);
                if (at < 0)
                {
                    return null;
                }

                held.Add(AclVerbs.Grantable[at]);
                from = at + 1;
            }

            return held;
        }
    }
}
