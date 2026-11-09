using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using KeePassGate.Host.Tests.Live;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// The encoder the moving picture is written with, read back by GDI+'s own GIF decoder.
    /// </summary>
    /// <remarks>
    /// **Decoded by somebody else's code**, because an encoder checked against a decoder written
    /// beside it could agree with itself about a format neither follows. GDI+ is what Windows
    /// shows a GIF with, and it lays each frame over the one before, so what it gives back for a
    /// frame is the picture a viewer sees at that moment.
    ///
    /// Here rather than with the screenshots, which only run when asked for: this needs no KeePass
    /// and no desktop, and a broken encoder found only on the day the pictures are retaken is a
    /// broken picture found late.
    /// </remarks>
    [TestFixture]
    public sealed class AnimatedGifTests
    {
        private const int Width = 160;
        private const int Height = 96;

        [Test]
        public void Every_frame_reads_back_as_it_was_drawn()
        {
            var frames = Frames();
            try
            {
                using (var encoded = new MemoryStream(Encode(frames)))
                using (var decoded = Image.FromStream(encoded))
                {
                    var dimension = new FrameDimension(decoded.FrameDimensionsList[0]);

                    // The second frame repeats the first, so it is not written: four remain.
                    Assert.That(decoded.GetFrameCount(dimension), Is.EqualTo(4));

                    var expected = new[] { frames[0], frames[2], frames[3], frames[4] };
                    for (var i = 0; i < expected.Length; i++)
                    {
                        decoded.SelectActiveFrame(dimension, i);
                        using (var shown = new Bitmap(decoded))
                        {
                            // The gradient has more colors than a palette holds, so it comes back
                            // near rather than exact, and the frame after it keeps it as it was
                            // shown; everything else is exact, the one pixel that frame changes
                            // among it.
                            var tolerance = i >= 2 ? 24 : 0;
                            AssertAlike(expected[i], shown, tolerance, "frame " + i);
                            if (i == 3)
                            {
                                Assert.That(shown.GetPixel(100, 50).ToArgb(), Is.EqualTo(Color.Black.ToArgb()));
                            }
                        }
                    }
                }
            }
            finally
            {
                // codeql[cs/missed-using-statement] one of a list the `finally` disposes, which no `using` holds
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }
        }

        [Test]
        public void Each_frame_is_held_for_its_own_time_and_the_picture_loops_forever()
        {
            var frames = Frames();
            try
            {
                using (var encoded = new MemoryStream(Encode(frames)))
                using (var decoded = Image.FromStream(encoded))
                {
                    // Hundredths of a second, four bytes a frame: the repeated frame's time is
                    // added to the one it repeats.
                    var delays = decoded.GetPropertyItem(0x5100).Value;
                    Assert.That(BitConverter.ToInt32(delays, 0), Is.EqualTo(30 + 20));
                    Assert.That(BitConverter.ToInt32(delays, 4), Is.EqualTo(10));
                    Assert.That(BitConverter.ToInt32(delays, 8), Is.EqualTo(40));
                    Assert.That(BitConverter.ToInt32(delays, 12), Is.EqualTo(250));

                    var loops = decoded.GetPropertyItem(0x5101).Value;
                    Assert.That(BitConverter.ToInt16(loops, 0), Is.Zero, "zero is forever");
                }
            }
            finally
            {
                // codeql[cs/missed-using-statement] one of a list the `finally` disposes, which no `using` holds
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }
        }

        [Test]
        public void Each_frame_after_the_first_holds_only_the_rectangle_that_changed()
        {
            // Read off the file's own image descriptors, because GDI+ lays each frame over the one
            // before: a frame written whole reads back exactly as one written as its change, so
            // only the file can tell them apart, and the file's size is what the change is for.
            var frames = Frames();
            try
            {
                var written = Descriptors(Encode(frames));

                Assert.That(written, Has.Length.EqualTo(4), "the repeated frame is folded into the one before");
                Assert.That(written[0], Is.EqualTo(new Rectangle(0, 0, Width, Height)), "the first frame, whole");
                Assert.That(written[1], Is.EqualTo(new Rectangle(40, 30, 12, 9)), "the square, and nothing round it");
                Assert.That(written[3], Is.EqualTo(new Rectangle(100, 50, 1, 1)), "the one pixel the dot changed");
            }
            finally
            {
                // codeql[cs/missed-using-statement] one of a list the `finally` disposes, which no `using` holds
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }
        }

        [Test]
        public void The_same_frames_make_the_same_file()
        {
            var frames = Frames();
            try
            {
                Assert.That(Encode(frames), Is.EqualTo(Encode(frames)));
            }
            finally
            {
                // codeql[cs/missed-using-statement] one of a list the `finally` disposes, which no `using` holds
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }
            }
        }

        private static byte[] Encode(Bitmap[] frames)
        {
            var delays = new[] { 30, 20, 10, 40, 250 };
            using (var stream = new MemoryStream())
            {
                using (var gif = new AnimatedGif(stream, Width, Height))
                {
                    for (var i = 0; i < frames.Length; i++)
                    {
                        gif.Add(frames[i], delays[i]);
                    }
                }

                return stream.ToArray();
            }
        }

        /// <summary>
        /// Five frames: noise in two hundred colors, which fills the coder's table and makes it start
        /// again; the same again; a small square changed; a gradient with thousands of colors; and
        /// one pixel changed.
        /// </summary>
        private static Bitmap[] Frames()
        {
            var noise = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            var seed = 12345u;
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    seed = (seed * 1103515245u) + 12345u;
                    var pick = (int)((seed >> 16) % 200);
                    noise.SetPixel(x, y, Color.FromArgb(pick, 255 - pick, (pick * 7) % 256));
                }
            }

            var again = new Bitmap(noise);

            var square = new Bitmap(noise);
            using (var graphics = Graphics.FromImage(square))
            using (var brush = new SolidBrush(Color.FromArgb(0x0B, 0x5E, 0xD7)))
            {
                graphics.FillRectangle(brush, 40, 30, 12, 9);
            }

            var gradient = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    gradient.SetPixel(x, y, Color.FromArgb(x * 255 / Width, y * 255 / Height, (x + y) % 256));
                }
            }

            var dot = new Bitmap(gradient);
            dot.SetPixel(100, 50, Color.Black);

            return new[] { noise, again, square, gradient, dot };
        }

        /// <summary>
        /// Where each frame in a GIF is drawn and how large it is, from its image descriptor, in
        /// the order the file holds them.
        /// </summary>
        /// <remarks>
        /// The file's blocks walked as GIF89a lays them out: the header and the screen's
        /// descriptor, the screen's color table when it has one, then extensions and images until
        /// the trailer, every one of them a run of sub-blocks after its own fixed part.
        /// </remarks>
        private static Rectangle[] Descriptors(byte[] gif)
        {
            var found = new List<Rectangle>();
            var at = 6 + 7;
            if ((gif[10] & 0x80) != 0)
            {
                at += 3 << ((gif[10] & 0x07) + 1);
            }

            while (gif[at] != 0x3B)
            {
                if (gif[at] == 0x21)
                {
                    at = PastSubBlocks(gif, at + 2);
                    continue;
                }

                Assert.That(gif[at], Is.EqualTo(0x2C), "a block that is neither an extension nor an image, at " + at);
                found.Add(new Rectangle(
                    BitConverter.ToUInt16(gif, at + 1),
                    BitConverter.ToUInt16(gif, at + 3),
                    BitConverter.ToUInt16(gif, at + 5),
                    BitConverter.ToUInt16(gif, at + 7)));

                var packed = gif[at + 9];
                at += 10;
                if ((packed & 0x80) != 0)
                {
                    at += 3 << ((packed & 0x07) + 1);
                }

                // The coder's starting width, then the coded pixels.
                at = PastSubBlocks(gif, at + 1);
            }

            return found.ToArray();
        }

        /// <summary>Past a run of sub-blocks, each its length then that many bytes, and the empty one.</summary>
        private static int PastSubBlocks(byte[] gif, int at)
        {
            while (gif[at] != 0)
            {
                at += gif[at] + 1;
            }

            return at + 1;
        }

        private static void AssertAlike(Bitmap expected, Bitmap actual, int tolerance, string what)
        {
            Assert.That(actual.Size, Is.EqualTo(expected.Size), what);
            for (var y = 0; y < expected.Height; y++)
            {
                for (var x = 0; x < expected.Width; x++)
                {
                    var wanted = expected.GetPixel(x, y);
                    var found = actual.GetPixel(x, y);
                    var off = Math.Max(
                        Math.Abs(wanted.R - found.R), Math.Max(Math.Abs(wanted.G - found.G), Math.Abs(wanted.B - found.B)));
                    if (off > tolerance)
                    {
                        Assert.Fail(what + " differs at " + x + "," + y + ": drawn " + wanted + ", read back " + found);
                    }
                }
            }
        }
    }
}
