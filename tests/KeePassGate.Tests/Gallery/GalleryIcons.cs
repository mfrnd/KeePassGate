using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests.Gallery
{
    /// <summary>
    /// Lends the gallery something to draw where KeePass would lend its own icon.
    /// </summary>
    /// <remarks>
    /// **A stand-in rather than the real picture, and deliberately not mistakable for it.** The
    /// heading of every column that removes something is painted with KeePass's waste bin, borrowed
    /// off the main window's client icons at load; this tier never loads KeePass, so there is no
    /// window to borrow from and the heading comes out blank. A blank heading is the one thing the
    /// gallery must not show, because a column drawn with nothing in it is a column whose width is
    /// being reviewed against the wrong contents.
    ///
    /// So it is lent a marker the same size the real icon is, in a flat color no icon in KeePass
    /// uses. What a reviewer reads off it is how much room the heading takes and where it sits, and
    /// nothing about what it looks like: the picture itself is settled by `GateIconTests`, which
    /// runs where there is a KeePass to ask.
    ///
    /// A `SetUpFixture` rather than a line in each fixture, because what is lent is static and
    /// four fixtures lending it in turn is four chances to forget to give it back.
    ///
    /// `NonParallelizable` because the loan is the process's: while it stands, every column
    /// painted anywhere is painted with the stand-in, and taking it back disposes a picture another
    /// fixture's paint may be holding. Marked here, the gallery's whole namespace runs with nothing
    /// beside it, the loan and its return included.
    /// </remarks>
    [SetUpFixture]
    [NonParallelizable]
    public sealed class GalleryIcons
    {
        private ImageList _standIn;

        [OneTimeSetUp]
        public void Lend()
        {
            _standIn = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

            for (var at = 0; at <= GateIcon.TrashBin; at++)
            {
                // Disposed here, because `ImageList.Images.Add` copies into the list's own handle
                // rather than taking what it is handed. Without this the loop leaves forty-four
                // bitmaps for the garbage collector to clean up after.
                using (var marker = Marker())
                {
                    _standIn.Images.Add(marker);
                }
            }

            GateIcon.Borrow(_standIn);
        }

        [OneTimeTearDown]
        public void TakeBack()
        {
            GateIcon.Return();

            if (_standIn != null)
            {
                _standIn.Dispose();
                _standIn = null;
            }
        }

        private static Bitmap Marker()
        {
            var marker = new Bitmap(16, 16);

            using (var ink = Graphics.FromImage(marker))
            using (var fill = new SolidBrush(Color.SlateGray))
            using (var edge = new Pen(Color.DarkSlateGray))
            {
                ink.FillRectangle(fill, 2, 2, 11, 11);
                ink.DrawRectangle(edge, 2, 2, 11, 11);
            }

            return marker;
        }
    }
}
