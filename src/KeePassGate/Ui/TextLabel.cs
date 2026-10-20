using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// A label whose text starts where the label does.
    /// </summary>
    /// <remarks>
    /// **A stock `Label` insets its text by about four pixels and nothing else on a page does.**
    /// `TextRenderer` reserves room on both sides for glyphs that overhang their cell, and `Label`
    /// asks for it; a button's border, a grid's border and the edge of a page do not. So a
    /// paragraph placed at the same left edge as the button above it and the list below it is drawn
    /// four pixels to the right of both, and the page reads as though somebody indented the prose
    /// on purpose. Measured rather than guessed: on the clients page the labels inked at x=12 with
    /// everything else at x=8.
    ///
    /// `TextFormatFlags.NoPadding` takes it off, which means painting and measuring here rather
    /// than leaving both to the base class. Measuring is the half that is easy to forget: a label
    /// that draws without the padding and sizes with it wraps a word early and leaves a gap down
    /// the right of every paragraph.
    ///
    /// It stays a `Label` rather than becoming a drawing routine, because everything else about one
    /// is wanted: automatic sizing, the wrapping the tab depends on, and a name that the end to end
    /// tests find it by.
    ///
    /// **Padding is honored in both halves, and taking over one half is what lost it.** A stock
    /// `Label` adds its padding to the size it asks for and draws inside it; overriding the two
    /// without carrying that across meant every `Padding` written down on one of these did
    /// nothing at all, silently, which is a spacing that can be written three times and never
    /// appear.
    ///
    /// **A word too wide for the label is broken rather than cut off**, which GDI's own wrapping
    /// will not do: `TextFormatFlags.WordBreak` breaks lines between words, so a single run with no
    /// space in it is drawn to the edge and clipped there, with nothing on screen to say that
    /// anything is missing. Most text has spaces in it and never meets this. The text that does not is exactly
    /// the text that matters most here, because a name a client chose for itself is shown to an
    /// operator who is being asked to approve it: a name with no spaces and fifty characters would
    /// be shown as its first thirty, and the operator would be comparing a string the client got to
    /// choose the visible half of.
    /// </remarks>
    public sealed class TextLabel : Label
    {
        /// <summary>
        /// How the text is both drawn and measured, which has to be one answer.
        /// </summary>
        /// <remarks>
        /// `WordBreak` because these wrap; `NoPrefix` because an ampersand in a database's own text
        /// is an ampersand rather than an underline under the next letter.
        /// </remarks>
        private const TextFormatFlags Shape =
            TextFormatFlags.NoPadding
            | TextFormatFlags.WordBreak
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.Top
            | TextFormatFlags.Left;

        /// <summary>
        /// How wide this may be when it is asked how tall it needs to be.
        /// </summary>
        /// <remarks>
        /// **A docked label is asked how tall it is without being told how wide.** WinForms
        /// proposes an empty size to a control docked along an edge, because the edge is what
        /// decides the width, and a label that answers as though it had the whole line puts every
        /// word on one line and runs off the side of the tab.
        ///
        /// **The room comes from the parent, not from this control.** Its own width is whatever it
        /// was last given, which during the first layout is whatever it was constructed with, and
        /// measuring against that locks a paragraph into the width of a stale guess: the scope line
        /// on the grant tab wrapped into a column four words wide (TODO(step 13): that tab is what
        /// this was found on, and it arrives later). The parent's display area is
        /// already known, does not depend on this answer, and is what the dock is about to hand
        /// over anyway.
        ///
        /// Only where the dock imposes it. A label that sizes itself in both directions has a width
        /// that follows its text, and constraining that by anything would be asking the answer to
        /// depend on itself.
        /// </remarks>
        private int Available(Size proposedSize)
        {
            if (MaximumSize.Width > 0)
            {
                return MaximumSize.Width;
            }

            if (proposedSize.Width > 0)
            {
                return proposedSize.Width;
            }

            var imposed = Dock == DockStyle.Top || Dock == DockStyle.Bottom || Dock == DockStyle.Fill;
            if (!imposed || Parent == null)
            {
                return int.MaxValue;
            }

            var room = Parent.DisplayRectangle.Width - Margin.Horizontal;

            return room > 0 ? room : int.MaxValue;
        }

        /// <summary>The last answer <see cref="Broken"/> gave, so a repaint is not a re-measure.</summary>
        private string _asked;

        private int _in;

        private Font _with;

        private string _broken;

        /// <summary>
        /// The text with a break put inside any word that will not fit the width on its own.
        /// </summary>
        /// <remarks>
        /// Nothing is done to text whose words all fit, which is nearly all of it, so the usual
        /// cost is one measurement per word. The answer is kept because painting asks for it on
        /// every repaint and the question has the same answer until the text, the width or the font
        /// changes.
        /// </remarks>
        private string Broken(int room)
        {
            var text = Text ?? string.Empty;

            if (room <= 0 || room == int.MaxValue || text.Length == 0)
            {
                return text;
            }

            if (_broken != null && _asked == text && _in == room && Equals(_with, Font))
            {
                return _broken;
            }

            var rebuilt = new System.Text.StringBuilder(text.Length);
            var word = new System.Text.StringBuilder();

            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    Emit(rebuilt, word, room);
                    rebuilt.Append(c);
                }
                else
                {
                    word.Append(c);
                }
            }

            Emit(rebuilt, word, room);

            _asked = text;
            _in = room;
            _with = Font;
            _broken = rebuilt.ToString();

            return _broken;
        }

        /// <summary>Appends one word, split across lines if it is wider than the room on its own.</summary>
        private void Emit(System.Text.StringBuilder into, System.Text.StringBuilder word, int room)
        {
            if (word.Length == 0)
            {
                return;
            }

            var text = word.ToString();
            word.Length = 0;

            if (Wide(text) <= room)
            {
                into.Append(text);
                return;
            }

            var taken = 0;

            while (taken < text.Length)
            {
                // At least one character per line, however narrow the label is, because a loop that
                // can take none of a word it cannot fit is a loop that does not end.
                var fits = 1;

                while (taken + fits < text.Length && Wide(text.Substring(taken, fits + 1)) <= room)
                {
                    fits++;
                }

                if (taken > 0)
                {
                    into.Append(Environment.NewLine);
                }

                into.Append(text, taken, fits);
                taken += fits;
            }
        }

        private int Wide(string text)
        {
            return TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue), Shape).Width;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = Available(proposedSize);

            // What the text may use, which is not what the label may use. Taking the padding off
            // before measuring and adding it back afterwards is what makes a padded label wrap in
            // the room it will actually be drawn in.
            var room = width == int.MaxValue ? int.MaxValue : Math.Max(1, width - Padding.Horizontal);

            var measured = TextRenderer.MeasureText(
                Broken(room), Font, new Size(room, int.MaxValue), Shape);

            // A label with nothing in it is still a label. Zero height collapses the row it is on,
            // which is right for one that is hidden and wrong for one that is merely empty.
            var height = measured.Height == 0 ? Font.Height : measured.Height;

            return new Size(measured.Width + Padding.Horizontal, height + Padding.Vertical);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Deliberately not calling the base implementation: it is the thing adding the padding.
            // The background is painted by `OnPaintBackground`, which still runs.
            // **The padding is taken off here rather than asked for.** `DisplayRectangle` looks
            // like the rectangle that would already have it off, and on a `Label` it is not:
            // `Control` answers with the client rectangle and only a scrollable control subtracts
            // anything. A label that asked for it drew its text hard against its own top edge
            // while reporting a size with room for padding underneath, which is a gap in the
            // wrong place and reads as no gap at all.
            var inner = new Rectangle(
                ClientRectangle.Left + Padding.Left,
                ClientRectangle.Top + Padding.Top,
                Math.Max(0, ClientRectangle.Width - Padding.Horizontal),
                Math.Max(0, ClientRectangle.Height - Padding.Vertical));

            TextRenderer.DrawText(
                e.Graphics,
                Broken(inner.Width),
                Font,
                inner,
                Enabled ? ForeColor : SystemColors.GrayText,
                Shape);
        }
    }
}
