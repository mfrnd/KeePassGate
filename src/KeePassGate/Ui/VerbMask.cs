using System;
using System.Collections.Generic;
using System.Text;
using KeePassGate.Acl;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A grant as eight characters: `CRUSDLAP`, with a dot for everything it does not hold.
    /// </summary>
    /// <remarks>
    /// **The whole rule fits in one cell and stays legible down a column.** A row of eight check
    /// boxes says the same thing and says it eight times as wide, and a list of words wraps. Read
    /// down a table, `.R...L..` and `.R...LA.` differ in the place the eye is already looking,
    /// which is the comparison an operator actually makes: this group and the one below it.
    ///
    /// A dot rather than a blank, and every position always present, because a mask that collapsed
    /// what it does not hold would be read as a shorter list rather than as a narrower rule, and
    /// `RL` and `LR` would then both be sayable and mean the same thing.
    ///
    /// **The letter is the first letter of the verb**, upper cased, and nothing chooses it
    /// separately. A second table mapping verbs to letters is a second place to forget one, so the
    /// only thing that could go wrong is two verbs sharing an initial, and there is a test that
    /// fails the day one does.
    ///
    /// Monospaced when it is shown. In a proportional font the dots and letters do not line up
    /// between rows, and a mask that does not line up is a mask nobody can read down.
    /// </remarks>
    public static class VerbMask
    {
        /// <summary>What an empty position looks like. Never a space.</summary>
        public const char Absent = '.';

        /// <summary>The letters, in order, which is `CRUSDLAP`.</summary>
        public static string Letters
        {
            get
            {
                var text = new StringBuilder(AclVerbs.Grantable.Count);
                foreach (var verb in AclVerbs.Grantable)
                {
                    text.Append(LetterOf(verb));
                }

                return text.ToString();
            }
        }

        /// <summary>How wide a mask always is.</summary>
        public static int Width
        {
            get { return AclVerbs.Grantable.Count; }
        }

        /// <summary>The letter that stands for a verb, which is its own first letter.</summary>
        public static char LetterOf(AclVerb verb)
        {
            return char.ToUpperInvariant(AclVerbs.Write(verb)[0]);
        }

        /// <summary>The mask for what a rule holds, or the empty mask for nothing at all.</summary>
        public static string Of(IEnumerable<AclVerb> held)
        {
            var holds = new HashSet<AclVerb>();
            if (held != null)
            {
                foreach (var verb in held)
                {
                    holds.Add(verb);
                }
            }

            var text = new StringBuilder(Width);
            foreach (var verb in AclVerbs.Grantable)
            {
                text.Append(holds.Contains(verb) ? LetterOf(verb) : Absent);
            }

            return text.ToString();
        }

        /// <summary>
        /// What a mask says a rule holds, or null for anything that is not exactly a mask.
        /// </summary>
        /// <remarks>
        /// Strict, and for the same reason the document reader is: this is what a typed cell is
        /// read back through, and a mask read generously is a rule that grants something nobody
        /// wrote. Every position has to be its own letter or a dot, in order, eight of them. A letter
        /// in the wrong column is refused rather than being taken to mean the verb it names,
        /// because somebody who typed `R` in the create column meant something, and guessing which
        /// is how an editor grants a verb its user did not choose.
        ///
        /// Case is not enforced, because a person typing a mask by hand is not choosing between two
        /// spellings of a rule the way a stored document is: there is only one rule here and the
        /// grid rewrites the cell to the canonical mask as soon as it is committed.
        /// </remarks>
        public static IList<AclVerb> Read(string mask)
        {
            if (mask == null)
            {
                return null;
            }

            var text = mask.Trim();
            if (text.Length != Width)
            {
                return null;
            }

            var held = new List<AclVerb>();
            for (var i = 0; i < AclVerbs.Grantable.Count; i++)
            {
                var verb = AclVerbs.Grantable[i];
                var typed = char.ToUpperInvariant(text[i]);

                if (typed == LetterOf(verb))
                {
                    held.Add(verb);
                    continue;
                }

                if (typed != Absent)
                {
                    return null;
                }
            }

            return held;
        }
    }
}
