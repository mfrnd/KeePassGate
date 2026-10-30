using System;
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
