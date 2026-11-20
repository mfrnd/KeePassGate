using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KeePassGate.Acl;
using KeePassGate.Authority;

namespace KeePassGate.Testing
{
    /// <summary>
    /// A node's rules as a test means them, composed the way an OK on the grant editor's dialog
    /// leaves them in the store.
    /// </summary>
    /// <remarks>
    /// **Through the plugin's own texts rather than spelled by hand**, because a version spelled
    /// by hand fails in the safe direction: a letter misplaced is a text out of form, and that
    /// refuses where it is found, so a test expecting a refusal passes even when the rule it meant
    /// would have allowed the request. Built here, a mask that is not exactly a mask throws as the
    /// test sets up, and so does a profile name the editor refuses or names twice, so what the
    /// node holds is what the reading is held to and an OK could have left.
    ///
    /// **No rules is `NodeRule.None`**, which is what the store answers for a node it holds
    /// nothing for, so a node granting nothing and letting everything through is refused here: a
    /// test meaning a node with no rules puts nothing on it. Sealing with no rules is a rule, the
    /// switch refusing, and is put there.
    ///
    /// A rule is a `CRUSDLAP` mask, as the editor shows it, in either of two forms: all eight
    /// places, a dot for each verb it does not hold, as `.R...L..`, or only the letters it holds,
    /// as `RL`, since counting dots is where a mask written by hand goes wrong. Either way the
    /// letters keep their order and each is its own, so `LR` and `.L...R..` both throw rather than
    /// being read as the rule they would have to be guessed into, and so does a short form with a
    /// dot in it, which is a long form miscounted. An empty mask is a rule granting nothing.
    ///
    /// Versions as written, <see cref="Stored"/>, stay where the text is what is being tested: the
    /// reading, a text out of form, a lost version, versions made apart.
    /// </remarks>
    public static class Rules
    {
        /// <summary>
        /// A node's rules granting each profile its mask, every other profile let through to the
        /// group above.
        /// </summary>
        /// <remarks>
        /// A pair per profile rather than profiles and masks in turn, so a profile cannot lose its
        /// mask to a miscount, nor take the next profile's.
        /// </remarks>
        public static NodeRule Granting(params (string Profile, string Mask)[] rules)
        {
            return Node(false, rules);
        }

        /// <summary>
        /// A node's rules with the switch refusing, so a profile they do not name is refused here
        /// whatever is granted above, with the profiles and masks they do name.
        /// </summary>
        public static NodeRule Sealing(params (string Profile, string Mask)[] rules)
        {
            return Node(true, rules);
        }

        /// <summary>
        /// A node's rules granting a single mask to each of as many profiles as asked for, named
        /// `profile-00` onwards, which is how a test fills a table past what it can show.
        /// </summary>
        public static NodeRule Numbered(int count, string mask)
        {
            var rules = new List<(string Profile, string Mask)>();
            for (var i = 0; i < count; i++)
            {
                rules.Add(("profile-" + i.ToString("00", CultureInfo.InvariantCulture), mask));
            }

            return Node(false, rules.ToArray());
        }

        /// <summary>
        /// A node's rules as an OK on its dialog leaves them: a single version granting each
        /// profile its mask, and the switch in a single version refusing where
        /// <paramref name="refuses"/>, with no version otherwise.
        /// </summary>
        /// <remarks>
        /// Held to what the editor could write: a profile named twice, a name the issuer could not
        /// issue and a text that is a mask in neither form throw as the test sets up. A node with
        /// no rule and a switch that lets the walk go on holds nothing at all, which is
        /// `NodeRule.None`, so asking for such a node here throws too.
        /// </remarks>
        public static NodeRule Node(bool refuses, params (string Profile, string Mask)[] rules)
        {
            var versions = new List<(string Profile, string Text)>();
            var named = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules ?? Array.Empty<(string Profile, string Mask)>())
            {
                if (rule.Profile == null)
                {
                    throw new ArgumentException("A rule names no profile.", nameof(rules));
                }

                if (!AuthorityIssuer.IsUsableProfileName(rule.Profile))
                {
                    throw new ArgumentException(
                        "\"" + rule.Profile + "\" is not a profile name the editor takes.", nameof(rules));
                }

                if (!named.Add(rule.Profile))
                {
                    throw new ArgumentException(rule.Profile + " is named twice.", nameof(rules));
                }

                versions.Add((rule.Profile, RuleText.Row(Letters(rule.Mask))));
            }

            if (refuses)
            {
                versions.Add((null, RuleText.SwitchRefusing));
            }

            if (versions.Count == 0)
            {
                throw new ArgumentException(
                    "A node with no rules holds nothing in the store, which is NodeRule.None.", nameof(rules));
            }

            return Stored(versions.ToArray());
        }

        /// <summary>
        /// A node's rules as the store could hold them: each pair a standing version of the
        /// profile's row, or of the switch where the profile is null, holding the text as written,
        /// in form or not, or a lost version where the text is null; each version under an
        /// identifier of its own, the same for the same arguments, naming nothing in `after`.
        /// </summary>
        /// <remarks>
        /// The identifier is made of the place of the pair, its profile and its text, so that two
        /// versions given alike are still two versions, as two copies of a database make them.
        /// </remarks>
        public static NodeRule Stored(params (string Profile, string Text)[] versions)
        {
            var objects = new List<RuleObject>();
            var given = versions ?? Array.Empty<(string Profile, string Text)>();
            for (var at = 0; at < given.Length; at++)
            {
                var version = given[at];
                var id = Identifier(at, version.Profile, version.Text);
                objects.Add(version.Text == null
                    ? new RuleObject(version.Profile, null, new[] { id })
                    : new RuleObject(version.Profile, new[] { new RuleVersion(id, version.Text, null) }, null));
            }

            return new NodeRule(objects);
        }

        /// <summary>The letters a mask holds, written in either form these rules take; anything else throws.</summary>
        public static AclGrant Letters(string mask)
        {
            var verbs = Verbs(mask);
            if (verbs == null)
            {
                throw new ArgumentException(
                    "\"" + mask + "\" is not a mask: either all " + VerbMask.Width + " places of " +
                    VerbMask.Letters + ", a dot for each verb it does not hold, or only the letters it " +
                    "holds, in that order.", nameof(mask));
            }

            return AclGrant.Of(verbs);
        }

        /// <summary>32 lowercase hexadecimal characters made of a version's place and what it holds.</summary>
        private static string Identifier(int at, string profile, string text)
        {
            using (var sha = SHA256.Create())
            {
                var made = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    at.ToString(CultureInfo.InvariantCulture) + "\n" + (profile == null ? "-" : "+" + profile) + "\n"
                    + (text == null ? "-" : "+" + text)));
                return string.Concat(made.Take(16).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
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

            // Searched for after the last one found, so a letter out of order is not found.
            foreach (var at in mask.Select(letter => letters.IndexOf(letter, from)))
            {
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
