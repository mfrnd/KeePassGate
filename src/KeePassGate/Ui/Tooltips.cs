using System;
using System.Text;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Tooltip text, folded so that a long one is a paragraph rather than a line off the screen.
    /// </summary>
    /// <remarks>
    /// **WinForms does not wrap a tooltip.** It measures the whole string and draws it on one line,
    /// so a sentence worth reading becomes a band the width of the desktop with its end cut off by
    /// the edge of it. Every tooltip here explains something, which means every one of them is long
    /// enough for that to happen.
    ///
    /// **A tooltip that already has newlines in it is left alone**, because somebody chose where
    /// those went. Re-folding it would move the breaks they wrote to wherever this column happens
    /// to fall, which is the one thing a written break is for.
    ///
    /// Folded on characters rather than on measured pixels, because a tooltip is drawn in whatever
    /// font the desktop gives it and this has no window to ask. What that costs is a ragged right
    /// edge on a proportional font, which is what a paragraph looks like anyway.
    /// </remarks>
    public static class Tooltips
    {
        /// <summary>
        /// How wide a tooltip line gets before it is folded, in characters.
        /// </summary>
        /// <remarks>
        /// Wide enough that a short explanation stays on one line, and narrow enough that a long
        /// one is still a shape the eye can come back to. Prose is generally held to be readable
        /// somewhere around here, and a tooltip is prose.
        /// </remarks>
        public const int Width = 72;

        /// <summary>
        /// The same text, folded at word boundaries, or exactly the same text where it is short
        /// enough or already broken by hand.
        /// </summary>
        public static string Wrapped(string text)
        {
            return Wrapped(text, Width);
        }

        /// <summary>The same, at a width a caller names. For the tests, mostly.</summary>
        public static string Wrapped(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0)
            {
                return text;
            }

            // Somebody chose these, so they stay where they are and nothing else is added.
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
            {
                return text;
            }

            if (text.Length <= width)
            {
                return text;
            }

            var folded = new StringBuilder(text.Length + 8);
            var line = 0;

            foreach (var word in text.Split(' '))
            {
                if (word.Length == 0)
                {
                    continue;
                }

                if (line == 0)
                {
                    folded.Append(word);
                    line = word.Length;
                    continue;
                }

                // A word longer than the whole width goes on its own line rather than being cut:
                // the ones that get that long here are identifiers, and half of one is worse than
                // a line that overruns.
                if (line + 1 + word.Length > width)
                {
                    folded.Append(Environment.NewLine).Append(word);
                    line = word.Length;
                    continue;
                }

                folded.Append(' ').Append(word);
                line += 1 + word.Length;
            }

            return folded.ToString();
        }

        /// <summary>Sets a tooltip on a control, folded.</summary>
        public static void Set(ToolTip tips, Control control, string text)
        {
            if (tips == null || control == null)
            {
                return;
            }

            tips.SetToolTip(control, Wrapped(text));
        }
    }
}
