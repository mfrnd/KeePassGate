using System;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Diagnostics;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The one picture this plugin puts beside its own name.
    /// </summary>
    /// <remarks>
    /// **KeePass's own icon 29, `TerminalEncrypted`**, and it comes from KeePass rather than from a
    /// resource of ours on purpose: it is then the same size, the same palette and the same style as
    /// every other icon in the window, and it follows a KeePass that changes its icons. A picture
    /// shipped here would be right until the first release that restyled them.
    ///
    /// Read through a seam because the icons live on the main window, which exists only inside a
    /// running KeePass. Everything that draws the tab is testable without one, and without an icon
    /// it simply has no icon.
    /// </remarks>
    public static class GateIcon
    {
        /// <summary>
        /// Which of KeePass's client icons. 29 is a terminal with a padlock on it, which is what
        /// this plugin is: a way in, that is guarded.
        /// </summary>
        public const int Standard = 29;

        /// <summary>
        /// Takes a copy of one of KeePass's client icons, or null when there are none to take.
        /// </summary>
        /// <remarks>
        /// A copy, because what this hands back is put into an `ImageList` that belongs to a dialog
        /// and is disposed with it. Handing out the main window's own image would mean a disposed
        /// dialog taking KeePass's icon list with it.
        /// </remarks>
        public static Image From(ImageList icons, int index)
        {
            if (icons == null || index < 0 || index >= icons.Images.Count)
            {
                return null;
            }

            try
            {
                // `Images[index]` composes a picture rather than handing one over, so it is already
                // a copy and disposing it is this method's job. Wrapping it without doing so made a
                // second bitmap and orphaned the first.
                using (var source = icons.Images[index])
                {
                    return new Bitmap(source);
                }
            }
            catch (Exception error) when (Expected.OfUserInterface(error))
            {
                return null;
            }
        }
    }
}
