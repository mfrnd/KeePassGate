using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Tests
{
    /// <summary>
    /// Tooltip text: folded, and kept left to right whatever the window it is shown over reads.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class TooltipsTests
    {
        private const char Mark = Tooltips.LeftToRightMark;

        [Test]
        public void Every_folded_line_is_marked_left_to_right_at_both_ends_and_nowhere_else()
        {
            var text = string.Join(" ", Enumerable.Repeat("Takes this client's admission away when OK is pressed.", 4));

            var lines = Tooltips.Wrapped(text).Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            Assert.That(lines.Length, Is.GreaterThan(1), "long enough to be folded");
            foreach (var line in lines)
            {
                Assert.That(line[0], Is.EqualTo(Mark), line);
                Assert.That(line[line.Length - 1], Is.EqualTo(Mark), line);
                Assert.That(line.Trim(Mark).IndexOf(Mark), Is.EqualTo(-1), line);
                Assert.That(line.Trim(Mark).Length, Is.LessThanOrEqualTo(Tooltips.Width), line);
            }

            Assert.That(string.Join(" ", lines.Select(line => line.Trim(Mark))), Is.EqualTo(text),
                "the marks are all that was added");
        }

        [Test]
        public void A_line_broken_by_hand_is_marked_where_it_was_broken()
        {
            Assert.That(Tooltips.Wrapped("first\r\nsecond"),
                Is.EqualTo(Mark + "first" + Mark + "\r\n" + Mark + "second" + Mark));
            Assert.That(Tooltips.Wrapped("first\nsecond"),
                Is.EqualTo(Mark + "first" + Mark + "\n" + Mark + "second" + Mark));
        }

        [Test]
        public void Nothing_is_added_to_nothing()
        {
            Assert.That(Tooltips.Wrapped(null), Is.Null);
            Assert.That(Tooltips.Wrapped(string.Empty), Is.Empty);
        }

        [Test]
        public void A_tooltip_reads_right_to_left_when_its_window_does_whatever_its_control_does()
        {
            // J076, and the reason the marks are there: `ToolTip` asks the top-level form rather
            // than the control, so a control laid out left to right on a right-to-left dialog
            // still has its tip read right to left. Were WinForms ever to ask the control, this
            // fails, and the marks have nothing left to do.
            using (var form = new Form
            {
                RightToLeft = RightToLeft.Yes,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000)
            })
            using (var tips = new ToolTip())
            {
                var page = new Panel { Dock = DockStyle.Fill, RightToLeft = RightToLeft.No };
                var button = new Button { Text = "Copy" };
                page.Controls.Add(button);
                form.Controls.Add(page);
                form.Show();
                Application.DoEvents();

                tips.SetToolTip(button, "Takes this away when OK is pressed.");

                Assert.That(button.RightToLeft, Is.EqualTo(RightToLeft.No));
                Assert.That(ReadsRightToLeft(tips), Is.True);
            }
        }

        [TestCase("Takes this client's admission away when OK is pressed.")]
        [TestCase("2026-09-19 14:02:11")]
        [TestCase("-AbCdEfGh_12")]
        [TestCase("saved with the database (on OK).")]
        public void A_marked_line_read_right_to_left_draws_what_it_draws_left_to_right(string text)
        {
            // Measured rather than left to the bidirectional algorithm's word: a full stop, a
            // leading run of digits, and a leading or trailing separator each move under a
            // right-to-left reading, and marked they do not. Drawn left to right, the marks are
            // nothing at all.
            var plain = Ink(text, TextFormatFlags.Left);

            Assert.That(Ink(text, RightToLeftReading), Is.Not.EqualTo(plain), "unmarked, the line moves");
            Assert.That(Ink(Tooltips.Wrapped(text), RightToLeftReading), Is.EqualTo(plain), "marked, it does not");
            Assert.That(Ink(Tooltips.Wrapped(text), TextFormatFlags.Left), Is.EqualTo(plain), "and the marks draw nothing");
        }

        private const TextFormatFlags RightToLeftReading = TextFormatFlags.Left | TextFormatFlags.RightToLeft;

        /// <summary>
        /// Which columns of a drawn line carry ink, from its first to its last, which is the line
        /// as it reads with wherever it was drawn taken away.
        /// </summary>
        private static string Ink(string text, TextFormatFlags flags)
        {
            const int Wide = 900;
            const int High = 40;

            using (var bitmap = new Bitmap(Wide, High))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                TextRenderer.DrawText(
                    graphics, text, SystemFonts.StatusFont, new Rectangle(0, 0, Wide, High), Color.Black,
                    flags | TextFormatFlags.NoPrefix);

                var columns = new StringBuilder();
                var first = -1;
                var last = -1;

                for (var x = 0; x < Wide; x++)
                {
                    long column = 0;
                    for (var y = 0; y < High; y++)
                    {
                        if (bitmap.GetPixel(x, y).R < 128)
                        {
                            column |= 1L << y;
                        }
                    }

                    if (column != 0)
                    {
                        first = first < 0 ? x : first;
                        last = x;
                    }

                    columns.Append(column.ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
                }

                Assert.That(first, Is.GreaterThanOrEqualTo(0), "nothing was drawn for " + text);

                // Cut to the ink, so that two lines drawn against different edges compare as what
                // they say.
                var all = columns.ToString().Split(',');
                return string.Join(",", all.Skip(first).Take(last - first + 1));
            }
        }

        /// <summary>Whether the one tool a tooltip holds is read right to left.</summary>
        private static bool ReadsRightToLeft(ToolTip tips)
        {
            var handle = (IntPtr)typeof(ToolTip)
                .GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(tips, null);

            Assert.That((int)SendMessage(handle, GetToolCount, IntPtr.Zero, IntPtr.Zero), Is.EqualTo(1));

            var text = Marshal.AllocHGlobal(8192);
            try
            {
                var tool = new ToolInfo { Size = Marshal.SizeOf<ToolInfo>(), Text = text };
                Assert.That(SendMessage(handle, EnumerateTools, IntPtr.Zero, ref tool), Is.Not.EqualTo(IntPtr.Zero));

                return (tool.Flags & RightToLeftReadingFlag) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        private const int GetToolCount = 0x0400 + 13;
        private const int EnumerateTools = 0x0400 + 58;
        private const int RightToLeftReadingFlag = 0x0004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ToolInfo
        {
            public int Size;
            public int Flags;
            public IntPtr Window;
            public IntPtr Id;
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public IntPtr Instance;
            public IntPtr Text;
            public IntPtr Parameter;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr index, ref ToolInfo tool);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);
    }
}
