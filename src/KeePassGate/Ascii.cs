namespace KeePassGate
{
    /// <summary>
    /// The ASCII classes of character the grammars on this wire are written in, in one place.
    /// </summary>
    /// <remarks>
    /// **ASCII and nothing wider**, which is the point of not asking `char`: `char.IsLetterOrDigit`
    /// answers for all of Unicode, so it takes an Arabic digit or a letter with a diacritic for one
    /// of these, and no grammar here allows either. A token, a profile name, a server name and an
    /// identifier each spelled the three ranges out where they were checked, which made each check
    /// a condition long enough to misread, and four copies of one fact.
    /// </remarks>
    internal static class Ascii
    {
        /// <summary>A to Z, in either case.</summary>
        public static bool IsLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>0 to 9.</summary>
        public static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        /// <summary>A letter or a digit, as the two above have them.</summary>
        public static bool IsLetterOrDigit(char c)
        {
            return IsLetter(c) || IsDigit(c);
        }

        /// <summary>A digit, or a to f in either case.</summary>
        public static bool IsHexDigit(char c)
        {
            return IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
