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
        private static readonly object Lock = new object();

        private static ImageList _icons;

        private static Image _bin;

        /// <summary>
        /// Which of KeePass's client icons. 29 is a terminal with a padlock on it, which is what
        /// this plugin is: a way in, that is guarded.
        /// </summary>
        public const int Standard = 29;

        /// <summary>
        /// Which of KeePass's client icons heads the columns that remove things. 43 is its own
        /// waste bin, the one it draws on its recycle bin group.
        /// </summary>
        /// <remarks>
        /// **Borrowed rather than drawn, for the reason the tab icon is borrowed.** A bin drawn
        /// out of rectangles is a bin at one size on one theme, and the one this replaced read as
        /// a stick figure at sixteen pixels, which is what an outline of a lid over a tapered body
        /// looks like when the lines are all the eye has. KeePass already ships the picture, in the
        /// palette and the style of every other icon in the window, and it costs nothing to ship
        /// because it is already there.
        /// </remarks>
        public const int TrashBin = 43;

        /// <summary>
        /// How much width a column needs to show one of these with room around it.
        /// </summary>
        /// <remarks>
        /// A column sized to its content asks its cells what they need, and a heading that paints
        /// itself is asked about text it does not draw. This is what it is told instead.
        ///
        /// **Measured off the bin rather than written down**, because the bin is KeePass's and
        /// KeePass scales its icons with the display: 16 pixels at 100 percent, 24 at 150 and 32 at
        /// 200. A floor of 22 fitted only the first, so a table with no rows, whose column had
        /// nothing else to be as wide as, drew a larger bin wider than the column it is centered
        /// in.
        /// </remarks>
        public static int Room
        {
            get { return RoomFor(Bin); }
        }

        /// <summary>
        /// The size KeePass's icons are designed at, which is the bin's when there is none to
        /// measure.
        /// </summary>
        private const int DesignedSize = 16;

        /// <summary>The air either side of the bin, which is what 22 left around 16.</summary>
        private const int Air = 3;

        /// <summary>
        /// The width a column needs around a picture this wide, or around one at the designed size
        /// when there is none.
        /// </summary>
        internal static int RoomFor(Image picture)
        {
            return (picture == null ? DesignedSize : picture.Width) + (2 * Air);
        }

        /// <summary>
        /// The bin, or null where there is no KeePass to borrow one from.
        /// </summary>
        /// <remarks>
        /// One copy, kept, because this is asked for on every repaint of a column heading and a
        /// fresh bitmap per paint is a bitmap per paint to collect. It belongs to this rather than
        /// to whatever drew it, so nothing disposes it out from under the next painter, and
        /// `Return` is what ends its life when the plugin unloads.
        /// </remarks>
        public static Image Bin
        {
            get
            {
                lock (Lock)
                {
                    if (_bin == null && _icons != null)
                    {
                        _bin = From(_icons, TrashBin);
                    }

                    return _bin;
                }
            }
        }

        /// <summary>
        /// Hands this the icons to borrow from, which only a running KeePass has.
        /// </summary>
        /// <remarks>
        /// A seam rather than a reach into the main window, because everything that draws these
        /// tables is testable without a password manager in the room, and without icons it simply
        /// has no icons.
        /// </remarks>
        public static void Borrow(ImageList icons)
        {
            lock (Lock)
            {
                Release();
                _icons = icons;
            }
        }

        /// <summary>Gives back what was borrowed, which the plugin does as it unloads.</summary>
        public static void Return()
        {
            lock (Lock)
            {
                Release();
                _icons = null;
            }
        }

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

        /// <summary>Drops the kept copy. Called under the lock.</summary>
        private static void Release()
        {
            if (_bin != null)
            {
                _bin.Dispose();
                _bin = null;
            }
        }
    }
}
