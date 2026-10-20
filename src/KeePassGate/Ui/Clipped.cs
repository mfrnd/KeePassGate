namespace KeePassGate.Ui
{
    /// <summary>
    /// How much of somebody else's message a page of this plugin will show.
    /// </summary>
    /// <remarks>
    /// **The text is not this plugin's to choose the length of.** An exception message comes from
    /// whatever threw, is composed for a log rather than for a dialog, and can be a paragraph. The
    /// pages it lands on have `AutoScroll` off, because the KPG tab is 449 by 314 and nothing on it
    /// scrolls, so an untrimmed one wraps, grows the page past the tab, and pushes the buttons out
    /// of reach with no way to scroll to them. The whole message still reaches the log, which is
    /// where a long one belongs.
    ///
    /// **Here rather than on the one page that started with it**, because two pages had the same
    /// problem and only one of them had the answer: `AuthorityPanel` cut the messages it composed
    /// itself and showed the ones `AuthorityService` composed whole, and the tab was then bounded
    /// by whichever path an operator happened to be on.
    ///
    /// The bound is a count of characters rather than of lines, because how many lines a string
    /// takes depends on the font, the scaling and the room, and none of the three is known where a
    /// message is cut.
    /// </remarks>
    internal static class Clipped
    {
        /// <summary>
        /// The most characters of somebody else's text a page will show: about two lines at the
        /// width the tab allows.
        /// </summary>
        public const int Limit = 160;

        /// <summary>
        /// The three that stand in for the rest, so a cut message reads as cut rather than as a
        /// sentence somebody left unfinished.
        /// </summary>
        private const string Rest = "...";

        /// <summary>
        /// One message, flattened onto a single line and cut to something a page can hold.
        /// </summary>
        /// <remarks>
        /// Flattened first, because a message carrying its own line breaks is as tall as it likes
        /// however few characters it has, and a page that does not scroll cannot afford that
        /// either.
        /// </remarks>
        public static string Message(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            var flattened = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flattened.Length <= Limit ? flattened : flattened.Substring(0, Limit) + Rest;
        }
    }
}
