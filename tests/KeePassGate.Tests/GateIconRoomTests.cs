using System.Drawing;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// How wide a column headed by KeePass's bin has to be, at every size KeePass draws the bin.
    /// </summary>
    /// <remarks>
    /// The bin is borrowed from KeePass, which scales its icons with the display, and it is painted
    /// at the size it arrives, centered on the heading. The room is asked of the picture rather
    /// than of KeePass, so it is measured here without one.
    /// </remarks>
    [TestFixture]
    public sealed class GateIconRoomTests
    {
        [Test]
        [TestCase(16)]
        [TestCase(20)]
        [TestCase(24)]
        [TestCase(32)]
        public void The_bin_fits_its_column_at_every_size_KeePass_draws_it(int size)
        {
            // 16 pixels at 100 percent, 20 at 125, 24 at 150 and 32 at 200. The floor was 22
            // whatever the bin was, and a table with no rows has nothing else to widen that column,
            // so from 150 percent the bin was drawn wider than the column it is centered in.
            using (var bin = new Bitmap(size, size))
            {
                var room = GateIcon.RoomFor(bin);

                Assert.That(room - size, Is.EqualTo(6), "three pixels either side, at every size");
            }
        }

        [Test]
        public void The_room_at_100_percent_is_what_it_always_was()
        {
            using (var bin = new Bitmap(16, 16))
            {
                Assert.That(GateIcon.RoomFor(bin), Is.EqualTo(22));
            }

            Assert.That(GateIcon.RoomFor(null), Is.EqualTo(22), "and without KeePass to borrow from");
        }
    }
}
