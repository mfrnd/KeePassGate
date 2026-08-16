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
    ///
    /// **And every line is marked left to right**, which <see cref="LeftToRight"/> says why.
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
        /// Marks the ends of a line as left to right, and draws as nothing.
        /// </summary>
        public const char LeftToRightMark = (char)0x200E;

        /// <summary>
        /// The same text, folded at word boundaries unless it is short enough or already broken by
        /// hand, with every line marked left to right.
        /// </summary>
        public static string Wrapped(string text)
        {
            return Wrapped(text, Width);
        }

        /// <summary>The same, at a width a caller names. For the tests, mostly.</summary>
        public static string Wrapped(string text, int width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return LeftToRight(Folded(text, width));
        }

        /// <summary>
        /// Every line of the text between two left-to-right marks, its breaks where they were.
        /// </summary>
        /// <remarks>
        /// **A tooltip is read in the direction of the window it belongs to, not of the control it
        /// describes.** `ToolTip` asks the top-level form, and has the tip read right to left
        /// whenever that form does, whatever the control itself does; under a right-to-left
        /// KeePass translation that form is KeePass's dialog, which this plugin does not own. Every
        /// tooltip here is English, and read right to left a sentence's full stop was drawn at its
        /// start, and a line starting with digits or punctuation moved them to its other end.
        ///
        /// A mark at each end puts everything on the line between two strong left-to-right
        /// characters, which is all the bidirectional algorithm needs to keep it in order; the
        /// reading direction then only decides the edge it is drawn against. At each end of each
        /// line, because every line break ends a paragraph as far as the algorithm is concerned,
        /// and a mark at the end alone leaves a leading run of digits or punctuation where it was.
        /// `TooltipsTests` measures both halves: the flag, and the drawing.
        /// </remarks>
        private static string LeftToRight(string text)
        {
            var marked = new StringBuilder(text.Length + 8);
            marked.Append(LeftToRightMark);

            for (var i = 0; i < text.Length; i++)
            {
                var character = text[i];
                if (character != '\r' && character != '\n')
                {
                    marked.Append(character);
                    continue;
                }

                // Closed before the break and opened after the whole of it, so that a `\r\n` is
                // still one break rather than two with a mark between them.
                marked.Append(LeftToRightMark).Append(character);
                if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    marked.Append('\n');
                    i++;
                }

                marked.Append(LeftToRightMark);
            }

            return marked.Append(LeftToRightMark).ToString();
        }

        private static string Folded(string text, int width)
        {
            if (width <= 0)
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
