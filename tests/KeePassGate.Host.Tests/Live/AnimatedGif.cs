using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Writes frames, one at a time, as a GIF89a that loops forever, each held for as long as it
    /// asks.
    /// </summary>
    /// <remarks>
    /// **Written here rather than handed to WPF's `GifBitmapEncoder`**, which .NET Framework has
    /// in the box and which was the obvious choice. It writes every frame whole, at the origin, so
    /// a pointer moving across a still dialog would be the whole picture again for every step it
    /// takes. It reduces each frame to colors it chooses itself, with dithering, so a dialog's
    /// flat gray comes out speckled. It writes no loop extension, so the file plays once, and a
    /// frame's delay is reachable only through metadata it documents poorly. What is left to write
    /// is small: a header, a loop extension, and per frame a delay, a palette and the pixels
    /// compressed with LZW, the variable-width coder every GIF has used since 1989.
    ///
    /// **Each frame is only what changed.** The rectangle around every pixel that differs from the
    /// frame before is written over it, which is kept, and inside that rectangle a pixel that did
    /// not change is written as the one transparent index, so what was shown there stays exactly
    /// as it was. A caret that blinks is then a frame the size of the caret, and the part of a
    /// dialog a pointer passes over is not quantized a second time.
    ///
    /// **The palette is the frame's own colors, exactly, whenever they fit**, which for a caret, a
    /// letter being typed or a pointer moving they always do, and the table is then only as large
    /// as those colors need. A frame that changes most of the picture, which is where one side
    /// stops acting and the other starts, has more, and keeps an exact entry for every color
    /// covering a noticeable share of what it changes and a median cut over the rest. Nothing is
    /// dithered.
    ///
    /// **Nothing here is random and every tie is broken by a color's value**, so the same frames
    /// make the same file byte for byte, which is what keeps this picture as steady as the PNGs
    /// beside it. A frame identical to the one before is not written: its time is added to that
    /// one's.
    /// </remarks>
    internal sealed class AnimatedGif : IDisposable
    {
        /// <summary>The most colors a frame's palette holds, leaving one entry for transparency.</summary>
        private const int MostColors = 255;

        /// <summary>
        /// The share of what a frame changes, in parts per thousand, that a color has to cover to
        /// keep an exact palette entry when a median cut decides the rest.
        /// </summary>
        /// <remarks>
        /// Two per thousand: every background, border and line of text, and none of the shades
        /// between a letter and what is behind it.
        /// </remarks>
        private const int ExactShare = 2;

        /// <summary>How many entries those exact colors may take, leaving the rest to the cut.</summary>
        private const int MostExact = 160;

        private readonly BinaryWriter _writer;
        private readonly int _width;
        private readonly int _height;

        /// <summary>The last frame given, as it was given, which the next one is compared against.</summary>
        private int[] _shown;

        /// <summary>The last frame worked out, waiting to learn whether the next one adds to its time.</summary>
        private Pending _pending;

        public AnimatedGif(Stream stream, int width, int height)
        {
            if (width < 1 || height < 1 || width > ushort.MaxValue || height > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "a GIF is between 1 and 65535 pixels a side");
            }

            _writer = new BinaryWriter(stream, Encoding.ASCII, true);
            _width = width;
            _height = height;

            _writer.Write(Encoding.ASCII.GetBytes("GIF89a"));

            // The logical screen: its size, and no palette of its own, since every frame carries
            // one. The packed byte claims eight bits of color resolution, which is advisory.
            _writer.Write((ushort)width);
            _writer.Write((ushort)height);
            _writer.Write((byte)0x70);
            _writer.Write((byte)0);
            _writer.Write((byte)0);

            // `NETSCAPE2.0` with a count of zero, which every browser and viewer reads as play
            // again forever.
            _writer.Write((byte)0x21);
            _writer.Write((byte)0xFF);
            _writer.Write((byte)11);
            _writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            _writer.Write((byte)3);
            _writer.Write((byte)1);
            _writer.Write((ushort)0);
            _writer.Write((byte)0);
        }

        /// <summary>Adds a frame, held for this many hundredths of a second.</summary>
        public void Add(Bitmap frame, int hundredths)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (frame.Width != _width || frame.Height != _height)
            {
                throw new ArgumentException("every frame has to be the size of the picture", nameof(frame));
            }

            var pixels = Pixels(frame);
            var area = _shown == null ? new Rectangle(0, 0, _width, _height) : Changed(_shown, pixels);

            if (area.IsEmpty)
            {
                _pending.Hundredths += hundredths;
                return;
            }

            Flush();
            _pending = Work(_shown, pixels, area);
            _pending.Hundredths = hundredths;
            _shown = pixels;
        }

        public void Dispose()
        {
            Flush();
            _writer.Write((byte)0x3B);
            _writer.Flush();
            _writer.Dispose();
        }

        private void Flush()
        {
            if (_pending == null)
            {
                return;
            }

            var frame = _pending;
            _pending = null;

            // The graphic control extension: left in place for the next frame to be drawn over,
            // with the entry after the last color as the one that shows what is underneath.
            _writer.Write((byte)0x21);
            _writer.Write((byte)0xF9);
            _writer.Write((byte)4);
            _writer.Write((byte)((1 << 2) | 1));
            _writer.Write((ushort)Math.Max(0, Math.Min(ushort.MaxValue, frame.Hundredths)));
            _writer.Write((byte)frame.Palette.Count);
            _writer.Write((byte)0);

            _writer.Write((byte)0x2C);
            _writer.Write((ushort)frame.Area.X);
            _writer.Write((ushort)frame.Area.Y);
            _writer.Write((ushort)frame.Area.Width);
            _writer.Write((ushort)frame.Area.Height);

            // A local palette of 2 to the power of (bits) entries, the size field being one less.
            _writer.Write((byte)(0x80 | (frame.Bits - 1)));
            for (var entry = 0; entry < 1 << frame.Bits; entry++)
            {
                var color = entry < frame.Palette.Count ? frame.Palette[entry] : 0;
                _writer.Write((byte)((color >> 16) & 0xFF));
                _writer.Write((byte)((color >> 8) & 0xFF));
                _writer.Write((byte)(color & 0xFF));
            }

            Lzw.Write(_writer, frame.Indices, Math.Max(2, frame.Bits));
        }

        /// <summary>
        /// One frame's rectangle, palette and indices: changed pixels as their nearest color, and
        /// every other one as the transparent entry.
        /// </summary>
        private Pending Work(int[] before, int[] after, Rectangle area)
        {
            var counts = new Dictionary<int, int>();
            var stride = area.Width;
            var full = before == null;
            var imageWidth = _width;

            var indices = new byte[area.Width * area.Height];
            var transparent = new bool[indices.Length];
            for (var y = 0; y < area.Height; y++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    var at = ((area.Y + y) * imageWidth) + area.X + x;
                    var color = after[at];
                    if (!full && before[at] == color)
                    {
                        transparent[(y * stride) + x] = true;
                        continue;
                    }

                    int count;
                    counts.TryGetValue(color, out count);
                    counts[color] = count + 1;
                }
            }

            var palette = PaletteFor(counts);
            var exact = new Dictionary<int, byte>();
            for (var i = 0; i < palette.Count; i++)
            {
                if (!exact.ContainsKey(palette[i]))
                {
                    exact[palette[i]] = (byte)i;
                }
            }

            var bits = 1;
            while ((1 << bits) < palette.Count + 1)
            {
                bits++;
            }

            for (var y = 0; y < area.Height; y++)
            {
                for (var x = 0; x < area.Width; x++)
                {
                    var here = (y * stride) + x;
                    if (transparent[here])
                    {
                        indices[here] = (byte)palette.Count;
                        continue;
                    }

                    var color = after[((area.Y + y) * imageWidth) + area.X + x];
                    byte index;
                    if (!exact.TryGetValue(color, out index))
                    {
                        index = Nearest(color, palette);
                        exact[color] = index;
                    }

                    indices[here] = index;
                }
            }

            return new Pending(area, palette, indices, bits);
        }

        private Rectangle Changed(int[] before, int[] after)
        {
            int left = _width, top = _height, right = -1, bottom = -1;
            for (var y = 0; y < _height; y++)
            {
                var row = y * _width;
                for (var x = 0; x < _width; x++)
                {
                    if (before[row + x] == after[row + x])
                    {
                        continue;
                    }

                    if (x < left) { left = x; }
                    if (x > right) { right = x; }
                    if (y < top) { top = y; }
                    if (y > bottom) { bottom = y; }
                }
            }

            return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        /// <summary>Every pixel of a bitmap as `0xRRGGBB`, with whatever transparency it had dropped.</summary>
        private static int[] Pixels(Bitmap frame)
        {
            var bounds = new Rectangle(0, 0, frame.Width, frame.Height);
            var data = frame.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new int[frame.Width * frame.Height];
                for (var row = 0; row < frame.Height; row++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), pixels, row * frame.Width, frame.Width);
                }

                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] &= 0xFFFFFF;
                }

                return pixels;
            }
            finally
            {
                frame.UnlockBits(data);
            }
        }

        /// <summary>
        /// The colors a frame changes, exactly when they fit, and otherwise its common ones exactly
        /// and a median cut over the rest.
        /// </summary>
        private static List<int> PaletteFor(Dictionary<int, int> counts)
        {
            var colors = new List<KeyValuePair<int, int>>(counts);

            // By how much each covers, and by value where two cover the same, so that nothing
            // below depends on the order a dictionary happens to keep.
            colors.Sort((a, b) => a.Value != b.Value ? b.Value.CompareTo(a.Value) : a.Key.CompareTo(b.Key));

            var palette = new List<int>();
            if (colors.Count <= MostColors)
            {
                foreach (var color in colors)
                {
                    palette.Add(color.Key);
                }

                return palette;
            }

            long total = 0;
            foreach (var color in colors)
            {
                total += color.Value;
            }

            var threshold = Math.Max(1, total * ExactShare / 1000);
            var rest = new List<KeyValuePair<int, int>>();
            foreach (var color in colors)
            {
                if (palette.Count < MostExact && color.Value >= threshold)
                {
                    palette.Add(color.Key);
                }
                else
                {
                    rest.Add(color);
                }
            }

            palette.AddRange(MedianCut(rest, MostColors - palette.Count));
            return palette;
        }

        /// <summary>
        /// Heckbert's median cut: the box with the widest spread in any one channel is split at
        /// the median of that channel, weighted by how often each color occurs, until there are as
        /// many boxes as entries to fill, and each box is then represented by its weighted mean.
        /// </summary>
        private static IEnumerable<int> MedianCut(List<KeyValuePair<int, int>> colors, int wanted)
        {
            var boxes = new List<List<KeyValuePair<int, int>>> { colors };

            while (boxes.Count < wanted)
            {
                var widest = -1;
                var widestSpread = 0;
                var widestChannel = 0;
                for (var i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2)
                    {
                        continue;
                    }

                    int channel;
                    var spread = Spread(boxes[i], out channel);
                    if (spread > widestSpread)
                    {
                        widest = i;
                        widestSpread = spread;
                        widestChannel = channel;
                    }
                }

                if (widest < 0)
                {
                    break;
                }

                var box = boxes[widest];
                var shift = 16 - (8 * widestChannel);
                box.Sort((a, b) =>
                {
                    var byChannel = ((a.Key >> shift) & 0xFF).CompareTo((b.Key >> shift) & 0xFF);
                    return byChannel != 0 ? byChannel : a.Key.CompareTo(b.Key);
                });

                long total = 0;
                foreach (var color in box)
                {
                    total += color.Value;
                }

                long seen = 0;
                var split = 1;
                for (var i = 0; i < box.Count - 1; i++)
                {
                    seen += box[i].Value;
                    split = i + 1;
                    if (seen * 2 >= total)
                    {
                        break;
                    }
                }

                boxes[widest] = box.GetRange(0, split);
                boxes.Add(box.GetRange(split, box.Count - split));
            }

            var means = new List<int>();
            foreach (var box in boxes)
            {
                long red = 0, green = 0, blue = 0, weight = 0;
                foreach (var color in box)
                {
                    red += ((color.Key >> 16) & 0xFF) * (long)color.Value;
                    green += ((color.Key >> 8) & 0xFF) * (long)color.Value;
                    blue += (color.Key & 0xFF) * (long)color.Value;
                    weight += color.Value;
                }

                if (weight > 0)
                {
                    means.Add((int)(((red / weight) << 16) | ((green / weight) << 8) | (blue / weight)));
                }
            }

            return means;
        }

        /// <summary>The widest a box spreads in any one channel, and which: 0 red, 1 green, 2 blue.</summary>
        private static int Spread(List<KeyValuePair<int, int>> box, out int channel)
        {
            int[] low = { 255, 255, 255 };
            int[] high = { 0, 0, 0 };
            foreach (var color in box)
            {
                for (var c = 0; c < 3; c++)
                {
                    var value = (color.Key >> (16 - (8 * c))) & 0xFF;
                    if (value < low[c]) { low[c] = value; }
                    if (value > high[c]) { high[c] = value; }
                }
            }

            channel = 0;
            for (var c = 1; c < 3; c++)
            {
                if (high[c] - low[c] > high[channel] - low[channel])
                {
                    channel = c;
                }
            }

            return high[channel] - low[channel];
        }

        /// <summary>
        /// The entry closest to a color, weighting green over red over blue roughly as an eye does,
        /// and the first of two equally close.
        /// </summary>
        private static byte Nearest(int color, List<int> palette)
        {
            var best = 0;
            var bestDistance = long.MaxValue;
            for (var i = 0; i < palette.Count; i++)
            {
                long red = ((color >> 16) & 0xFF) - ((palette[i] >> 16) & 0xFF);
                long green = ((color >> 8) & 0xFF) - ((palette[i] >> 8) & 0xFF);
                long blue = (color & 0xFF) - (palette[i] & 0xFF);
                var distance = (3 * red * red) + (4 * green * green) + (2 * blue * blue);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return (byte)best;
        }

        /// <summary>A frame worked out and not yet written, because its time may still grow.</summary>
        private sealed class Pending
        {
            public Pending(Rectangle area, List<int> palette, byte[] indices, int bits)
            {
                Area = area;
                Palette = palette;
                Indices = indices;
                Bits = bits;
            }

            public Rectangle Area { get; }

            public List<int> Palette { get; }

            public byte[] Indices { get; }

            /// <summary>The bits an index takes, which sets the size of the palette written.</summary>
            public int Bits { get; }

            public int Hundredths { get; set; }
        }

        /// <summary>
        /// GIF's variable-width LZW, in the sub-blocks a GIF carries it in.
        /// </summary>
        /// <remarks>
        /// **The width grows when the next code to be assigned no longer fits**, checked after a
        /// code is written and before the entry it implies is added. A decoder adds that entry one
        /// code later than the encoder does, so this is the moment both agree on; it is the order
        /// every encoder since `compress` has used. At 4096 entries the table is full, and a clear
        /// code starts it again.
        /// </remarks>
        private static class Lzw
        {
            private const int MostCodes = 4096;
            private const int WidestCode = 12;

            public static void Write(BinaryWriter writer, byte[] indices, int minimum)
            {
                writer.Write((byte)minimum);

                var clear = 1 << minimum;
                var end = clear + 1;

                var output = new Bits(writer);
                var table = new Dictionary<int, int>();
                var width = minimum + 1;
                var next = end + 1;

                output.Put(clear, width);

                var prefix = (int)indices[0];
                for (var i = 1; i < indices.Length; i++)
                {
                    var symbol = indices[i];
                    var key = (prefix << 8) | symbol;

                    int found;
                    if (table.TryGetValue(key, out found))
                    {
                        prefix = found;
                        continue;
                    }

                    output.Put(prefix, width);
                    width = Widened(width, next);
                    prefix = symbol;

                    if (next < MostCodes)
                    {
                        table[key] = next;
                        next++;
                    }
                    else
                    {
                        output.Put(clear, width);
                        table.Clear();
                        width = minimum + 1;
                        next = end + 1;
                    }
                }

                output.Put(prefix, width);
                width = Widened(width, next);
                output.Put(end, width);
                output.Flush();

                writer.Write((byte)0);
            }

            /// <summary>The width the next code is written in, once `next` is the next to assign.</summary>
            private static int Widened(int width, int next)
            {
                return next > (1 << width) - 1 && width < WidestCode ? width + 1 : width;
            }

            /// <summary>Codes packed least significant bit first, out in blocks of up to 255 bytes.</summary>
            private sealed class Bits
            {
                private readonly BinaryWriter _writer;
                private readonly byte[] _block = new byte[255];
                private int _filled;
                private int _pending;
                private int _pendingBits;

                public Bits(BinaryWriter writer)
                {
                    _writer = writer;
                }

                public void Put(int code, int width)
                {
                    _pending |= code << _pendingBits;
                    _pendingBits += width;
                    while (_pendingBits >= 8)
                    {
                        Byte((byte)(_pending & 0xFF));
                        _pending >>= 8;
                        _pendingBits -= 8;
                    }
                }

                public void Flush()
                {
                    if (_pendingBits > 0)
                    {
                        Byte((byte)(_pending & 0xFF));
                        _pending = 0;
                        _pendingBits = 0;
                    }

                    Block();
                }

                private void Byte(byte value)
                {
                    _block[_filled++] = value;
                    if (_filled == _block.Length)
                    {
                        Block();
                    }
                }

                private void Block()
                {
                    if (_filled == 0)
                    {
                        return;
                    }

                    _writer.Write((byte)_filled);
                    _writer.Write(_block, 0, _filled);
                    _filled = 0;
                }
            }
        }
    }
}
