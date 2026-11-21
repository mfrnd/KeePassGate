using System.Drawing;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The backgrounds that mark a row of a table, or a control beside a table, beyond what the row
    /// tells in words: a change that waits for OK, and rows bound together.
    /// </summary>
    /// <remarks>
    /// **The light yellow of a change is defined here and nowhere else**, so that every page of
    /// this plugin that marks a change waiting for OK marks the change in the same color:
    /// <see cref="ChangedColor"/>, #FFF3B0.
    ///
    /// **Never the only mark.** A row that changed tells so in its tooltip and its accessible text,
    /// and rows bound together are bound by a bracket and named by the word in their State cell,
    /// so a reader who cannot tell the colors apart, or who sees none, loses nothing a color tells.
    ///
    /// **Light enough for the text drawn on the colors.** On the light yellow, the window's text
    /// keeps a contrast of about 18.7 to 1 and the gray of an inherited rule about 4.6 to 1; on
    /// the light red, about 18.3 and 4.5 to 1, measured against the black and the gray (#6D6D6D)
    /// the window draws with. The red is held that light for the gray, since an inherited rule
    /// bound to its limit is drawn in gray on the red.
    ///
    /// **No fixed color under a high contrast setting**: the colors of that setting are the reader's
    /// own choice, and a light background under the setting's light text is a row nobody reads.
    /// There the rows keep the setting's own background, and what the colors mark is told by the
    /// words, the bracket and the letters in bold alone.
    /// </remarks>
    internal sealed class RowTints
    {
        /// <summary>The light yellow behind a change that waits for OK.</summary>
        public static readonly Color ChangedColor = Color.FromArgb(0xFF, 0xF3, 0xB0);

        /// <summary>The light red behind rows bound together, and behind a box in the mixed mark.</summary>
        public static readonly Color BoundColor = Color.FromArgb(0xFF, 0xEB, 0xEB);

        /// <summary>The colors of a display in its ordinary colors.</summary>
        public static readonly RowTints Standard = new RowTints(ChangedColor, BoundColor);

        /// <summary>No color at all: every row keeps the background the table gives it.</summary>
        public static readonly RowTints None = new RowTints(Color.Empty, Color.Empty);

        private RowTints(Color changed, Color bound)
        {
            Changed = changed;
            Bound = bound;
        }

        /// <summary>Behind a change that waits for OK, or empty for none.</summary>
        public Color Changed { get; }

        /// <summary>Behind rows bound together, or empty for none.</summary>
        public Color Bound { get; }

        /// <summary>The colors for a display set as given: none under a high contrast setting.</summary>
        /// <param name="highContrast">
        /// Whether the display is under a high contrast setting, which the caller reads where the
        /// colors are chosen: `SystemInformation.HighContrast`, asked again whenever the display's
        /// colors change.
        /// </param>
        public static RowTints For(bool highContrast)
        {
            return highContrast ? None : Standard;
        }
    }
}
