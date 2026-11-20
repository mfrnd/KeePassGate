using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KeePassGate.Authority;
using KeePassGate.Store;

namespace KeePassGate.Acl
{
    /// <summary>
    /// The texts of the versions of a node's rules and the objects they are versions of,
    /// `STORE.md` §617: composed and read in this single place, strictly, and total.
    /// </summary>
    /// <remarks>
    /// **Strict, because a text read generously is a rule nobody wrote.** A reader that took a
    /// letter out of order, a letter twice, a lowercase letter or a name the issuer could not issue
    /// would read a version the plugin never composed as a version it composed, and a second
    /// spelling of a rule is a rule that means what somebody guessed. So a text is in form only
    /// where composing what was read gives back the same bytes, and anything else is out of form,
    /// which the walk refuses where it is found.
    ///
    /// **Total, because the store holds texts nobody of the plugin wrote.** Nothing here throws on
    /// any text, a null among them: a text out of form is told as out of form, and an object out of
    /// form is the rule of no node.
    ///
    /// The letters are those of `VerbMask`, in the order of `AclVerbs.Grantable`, so a version
    /// spells a mask as the editor shows a mask, with the dots left out.
    /// </remarks>
    internal static class RuleText
    {
        /// <summary>What the text of a version of a row that grants begins with, the letters granted following it.</summary>
        public const string RowStart = "row:";

        /// <summary>What the text of a version of a row that takes the rule off begins with, the letters that arrived following it.</summary>
        public const string TakenOffStart = "off:";

        /// <summary>The whole text of a version of a row taken off while the node's own switch refused.</summary>
        public const string TakenOffRefusing = "off:deny";

        /// <summary>The whole text of a version of the switch that refuses every profile the node does not name.</summary>
        public const string SwitchRefusing = "sw:deny";

        /// <summary>
        /// What the text of a version of the switch that inherits begins with, an item for each
        /// profile that letters arrived for following it.
        /// </summary>
        public const string SwitchInheriting = "sw:inherit";

        /// <summary>What comes before each item of a switch's text.</summary>
        private const char ItemStart = ' ';

        /// <summary>What separates the name of an item from its letters.</summary>
        private const char LettersStart = '=';

        /// <summary>The length of a node's identifier as an object writes it.</summary>
        private const int NodeLength = 32;

        /// <summary>`CRUSDLAP`, the letter of each verb and switch at its position in `AclVerbs.Grantable`.</summary>
        private static readonly string Alphabet = VerbMask.Letters;

        private static readonly IReadOnlyDictionary<string, AclGrant> NoItem =
            new Dictionary<string, AclGrant>(StringComparer.Ordinal);

        /// <summary>
        /// The text of a version of a row that grants these letters: `row:` and each letter
        /// granted, in the order of `CRUSDLAP`.
        /// </summary>
        public static string Row(AclGrant letters)
        {
            return RowStart + Letters(letters);
        }

        /// <summary>
        /// The text of a version of a row that takes the rule off, recording these letters as the
        /// letters that arrived: `off:` and each letter, in the order of `CRUSDLAP`.
        /// </summary>
        public static string TakenOff(AclGrant arrived)
        {
            return TakenOffStart + Letters(arrived);
        }

        /// <summary>
        /// The text of a version of the switch that inherits, recording the letters that arrived
        /// for each profile: `sw:inherit`, then for each profile holding at least a single letter,
        /// ascending by ordinal, a space, the name, `=` and its letters; a profile whose name the
        /// issuer could not issue, or that holds no letter, is left out.
        /// </summary>
        /// <remarks>
        /// A profile given twice is written once with the letters both hold, since a name twice is
        /// out of form and the wider of the two would let through letters the narrower left out.
        /// Leaving a profile out lets nothing through for it, so every way a profile is left out
        /// is the narrow way.
        /// </remarks>
        public static string Inheriting(IEnumerable<KeyValuePair<string, AclGrant>> arrived)
        {
            var held = new SortedDictionary<string, AclGrant>(StringComparer.Ordinal);
            foreach (var pair in arrived ?? Array.Empty<KeyValuePair<string, AclGrant>>())
            {
                if (pair.Value == null || !AuthorityIssuer.IsUsableProfileName(pair.Key))
                {
                    continue;
                }

                AclGrant already;
                held[pair.Key] = held.TryGetValue(pair.Key, out already) ? already.Cut(pair.Value) : pair.Value;
            }

            var text = new StringBuilder(SwitchInheriting);
            foreach (var pair in held.Where(pair => pair.Value.Count > 0))
            {
                text.Append(ItemStart).Append(pair.Key).Append(LettersStart).Append(Letters(pair.Value));
            }

            return text.ToString();
        }

        /// <summary>
        /// What the text of a version of a row reads as, with the letters it grants or records in
        /// <paramref name="letters"/>, no letter for `off:deny` and for a text out of form; total.
        /// </summary>
        public static RowVersionKind ReadRow(string text, out AclGrant letters)
        {
            letters = AclGrant.Nothing;
            if (text == null)
            {
                return RowVersionKind.OutOfForm;
            }

            if (string.Equals(text, TakenOffRefusing, StringComparison.Ordinal))
            {
                return RowVersionKind.TakenOffRefusing;
            }

            AclGrant read;
            if (text.StartsWith(RowStart, StringComparison.Ordinal) && ReadLetters(text, RowStart.Length, text.Length, out read))
            {
                letters = read;
                return RowVersionKind.Grants;
            }

            if (text.StartsWith(TakenOffStart, StringComparison.Ordinal) && ReadLetters(text, TakenOffStart.Length, text.Length, out read))
            {
                letters = read;
                return RowVersionKind.TakenOff;
            }

            return RowVersionKind.OutOfForm;
        }

        /// <summary>
        /// What the text of a version of the switch reads as, with the letters it records for each
        /// profile it names in <paramref name="letters"/>, none for `sw:deny` and for a text out of
        /// form; total.
        /// </summary>
        public static SwitchVersionKind ReadSwitch(string text, out IReadOnlyDictionary<string, AclGrant> letters)
        {
            letters = NoItem;
            if (text == null)
            {
                return SwitchVersionKind.OutOfForm;
            }

            if (string.Equals(text, SwitchRefusing, StringComparison.Ordinal))
            {
                return SwitchVersionKind.Refuses;
            }

            if (!text.StartsWith(SwitchInheriting, StringComparison.Ordinal))
            {
                return SwitchVersionKind.OutOfForm;
            }

            var items = new Dictionary<string, AclGrant>(StringComparer.Ordinal);
            string previous = null;
            var at = SwitchInheriting.Length;
            while (at < text.Length)
            {
                // A single space before each item, and nothing after the last item: a second space
                // or a space at the end is an item with nothing in it.
                if (text[at] != ItemStart)
                {
                    return SwitchVersionKind.OutOfForm;
                }

                var start = at + 1;
                var end = text.IndexOf(ItemStart, start);
                if (end < 0)
                {
                    end = text.Length;
                }

                var equals = text.IndexOf(LettersStart, start, end - start);
                if (equals <= start)
                {
                    return SwitchVersionKind.OutOfForm;
                }

                var name = text.Substring(start, equals - start);
                AclGrant read;

                // Names strictly ascending by ordinal, so each stands once, and an item with no
                // letter is out of form: the composer leaves such a profile out.
                if (!AuthorityIssuer.IsUsableProfileName(name)
                    || (previous != null && string.CompareOrdinal(previous, name) >= 0)
                    || equals + 1 == end
                    || !ReadLetters(text, equals + 1, end, out read))
                {
                    return SwitchVersionKind.OutOfForm;
                }

                items.Add(name, read);
                previous = name;
                at = end;
            }

            letters = items;
            return SwitchVersionKind.Inherits;
        }

        /// <summary>
        /// The object of a profile's row at a node: `rule:`, the node as 32 lowercase hexadecimal
        /// characters, `/` and the name.
        /// </summary>
        public static string RowObject(string node, string profile)
        {
            return StoreKinds.RuleType + StoreKinds.ObjectTypeEnd + node + StoreFormat.ProfileStart + profile;
        }

        /// <summary>The object of a node's switch: `switch:` and the node as 32 lowercase hexadecimal characters.</summary>
        public static string SwitchObject(string node)
        {
            return StoreKinds.SwitchType + StoreKinds.ObjectTypeEnd + node;
        }

        /// <summary>
        /// Whether a text is the object of a profile's row at a node, `rule:`, 32 lowercase
        /// hexadecimal characters, `/` and a name the issuer could issue, or of a node's switch,
        /// `switch:` and 32 lowercase hexadecimal characters, with <paramref name="profile"/> null:
        /// false for any other text, which is the rule of no node; total.
        /// </summary>
        public static bool ReadObject(string objectText, out string node, out string profile)
        {
            node = null;
            profile = null;
            if (objectText == null)
            {
                return false;
            }

            var rule = StoreKinds.RuleType + StoreKinds.ObjectTypeEnd;
            if (objectText.StartsWith(rule, StringComparison.Ordinal))
            {
                var slash = rule.Length + NodeLength;
                if (objectText.Length <= slash
                    || objectText[slash] != StoreFormat.ProfileStart
                    || !IsNode(objectText, rule.Length)
                    || !AuthorityIssuer.IsUsableProfileName(objectText.Substring(slash + 1)))
                {
                    return false;
                }

                node = objectText.Substring(rule.Length, NodeLength);
                profile = objectText.Substring(slash + 1);
                return true;
            }

            var sw = StoreKinds.SwitchType + StoreKinds.ObjectTypeEnd;
            if (objectText.StartsWith(sw, StringComparison.Ordinal)
                && objectText.Length == sw.Length + NodeLength
                && IsNode(objectText, sw.Length))
            {
                node = objectText.Substring(sw.Length);
                return true;
            }

            return false;
        }

        /// <summary>The letters a grant holds, in the order of `CRUSDLAP`; none for no grant.</summary>
        private static string Letters(AclGrant letters)
        {
            var text = new StringBuilder(Alphabet.Length);
            if (letters != null)
            {
                for (var at = 0; at < Alphabet.Length; at++)
                {
                    if (letters.Holds(AclVerbs.Grantable[at]))
                    {
                        text.Append(Alphabet[at]);
                    }
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Whether the characters from <paramref name="start"/> to <paramref name="end"/> are
        /// letters of `CRUSDLAP` in its order, each at most once, none at all among them.
        /// </summary>
        private static bool ReadLetters(string text, int start, int end, out AclGrant letters)
        {
            letters = AclGrant.Nothing;
            var held = new List<AclVerb>(Alphabet.Length);
            var last = -1;
            for (var at = start; at < end; at++)
            {
                var position = Alphabet.IndexOf(text[at]);
                if (position <= last)
                {
                    return false;
                }

                held.Add(AclVerbs.Grantable[position]);
                last = position;
            }

            letters = AclGrant.Of(held);
            return true;
        }

        /// <summary>Whether 32 lowercase hexadecimal characters begin at <paramref name="start"/>.</summary>
        private static bool IsNode(string text, int start)
        {
            if (text.Length < start + NodeLength)
            {
                return false;
            }

            for (var at = start; at < start + NodeLength; at++)
            {
                var c = text[at];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
