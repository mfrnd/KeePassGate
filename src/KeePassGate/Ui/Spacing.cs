using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The one amount of space this plugin puts between things.
    /// </summary>
    /// <remarks>
    /// **One number, decided once, so that no page decides again.** A panel that picks its own
    /// spacing picks a different one on the next page, and a reader crossing between pages of a
    /// single tab then reads several amounts of air and wonders what the differences are for.
    ///
    /// Eight, which is what most of a dialog's own controls sit at and what six turned out not to
    /// be. It is a spacing rather than a scale: the tab it is spent on is 449 by 314, so every
    /// pixel here is a pixel the content does not get, and there is no room to be generous with.
    ///
    /// **A panel's own inset is the same number**, so the space between a page's edge and what is
    /// on it matches the space between one thing on it and the next. Two constants of the same
    /// value would only be two things to keep equal.
    ///
    /// **A window of this plugin's own is held to it everywhere but its edge.** That is a dialog
    /// rather than a page inside somebody else's tab and is not short of room, so its inset keeps
    /// the twelve a dialog conventionally has. Between one thing on it and the next the amount is
    /// this one, so that a dialog and a tab do not disagree about how far apart two lines are.
    /// TODO(step 8): the first such window, the approval dialog, arrives with pairing.
    ///
    /// Named `Spacing` rather than `Layout`, which is what it was called for about a minute:
    /// `Control` has a `Layout` event, so inside any control on this tab the name resolves to the
    /// event and every use of it stops compiling.
    ///
    /// **Every amount here is a pixel at the size KeePass's dialogs are designed at, and grows with
    /// the dialog it lands on.** Those dialogs are `AutoScaleMode.Font` against 6 by 13, so a
    /// larger system font or a scaled display widens their own margins as they are built. A page
    /// of this plugin's is added to one afterwards, where that pass never reaches, and at 200
    /// percent it sat eight pixels in from its edge beside KeePass's own pages sitting twice as far
    /// in. So `TabPageInjector` scales every page it attaches by `ScaleOf` its dialog, the factor
    /// KeePass scaled everything else there by.
    /// </remarks>
    internal static class Spacing
    {
        /// <summary>
        /// The text KeePass's dialogs are laid out for, as `AutoScaleDimensions` measures it: an
        /// average character 6 pixels wide, on lines 13 pixels apart.
        /// </summary>
        public static readonly SizeF Designed = new SizeF(6F, 13F);

        /// <summary>The scale of a dialog drawn exactly as designed, which leaves a page as it is.</summary>
        public static readonly SizeF AsDesigned = new SizeF(1F, 1F);

        /// <summary>
        /// How much larger than it was designed a dialog is drawn, which is how much larger
        /// everything this plugin puts on it has to be.
        /// </summary>
        /// <remarks>
        /// Measured off the dialog's own font, the way its `AutoScaleMode.Font` pass measured it, so
        /// a page of ours is scaled exactly as the dialog's own pages were. A dialog that does not
        /// scale by its font, or has no font to measure, is taken as drawn as designed.
        /// </remarks>
        public static SizeF ScaleOf(Form dialog)
        {
            if (dialog == null || dialog.AutoScaleMode != AutoScaleMode.Font)
            {
                return AsDesigned;
            }

            var drawn = dialog.CurrentAutoScaleDimensions;
            if (drawn.Width <= 0F || drawn.Height <= 0F)
            {
                return AsDesigned;
            }

            return new SizeF(drawn.Width / Designed.Width, drawn.Height / Designed.Height);
        }

        /// <summary>
        /// The space between one thing and the next, and between a page and its edge.
        /// </summary>
        public const int Gap = 8;

        /// <summary>
        /// The smaller amount, between a line and the one it belongs with: a heading and its
        /// detail, a label and the table under it. Named for the same reason `Gap` is, so the
        /// tab has two amounts of space rather than however many were typed.
        /// </summary>
        public const int Tight = 2;
    }
}
