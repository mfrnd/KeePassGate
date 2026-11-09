using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using KeePassGate.Diagnostics;
using KeePassGate.Host.Tests.Live;
using NUnit.Framework;
using Run = KeePassGate.Host.Tests.Live.ReferenceClient.Run;
using Side = KeePassGate.Host.Tests.Live.Storyboard.Side;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// What composes every frame of the moving picture, told a short story made of pictures drawn
    /// here.
    /// </summary>
    /// <remarks>
    /// **Here rather than with the screenshots**, for the reason `AnimatedGifTests` gives: it needs
    /// no KeePass and no desktop, only bitmaps, and the fixture that runs it for real runs when the
    /// pictures are retaken and is looked at rather than asserted. A storyboard that timed itself by
    /// the clock, or a replacement that missed a line and published what the client really made,
    /// would otherwise be found by somebody reading a published picture.
    ///
    /// The story is told the way `DocumentationLifecycle` tells one: a terminal typing a command
    /// and printing what it answered, a prompt answered by a paste, a key on a cap, the pointer
    /// traveling to a control and clicking it, and the window that click leaves.
    /// </remarks>
    [TestFixture]
    public sealed class StoryboardTests
    {
        /// <summary>What the client printed, which the story is told to show as the invented value.</summary>
        private const string Actual = "admission key: 0123456789abcdef0123456789abcdef";

        private const string Invented = "admission key: 5eed5eed5eed5eed5eed5eed5eed5eed";

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Paths.Join(Path.GetTempPath(), "KeePassGateStoryboard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, true);
        }

        [Test]
        public void The_same_story_makes_the_same_file_however_long_it_took_to_tell()
        {
            // Told twice, the second time with the clock running on between every step, because
            // what the story is timed by is the pace written into it and never how long the real
            // session took.
            var once = Told(false, Actual, true);
            var again = Told(true, Actual, true);

            Assert.That(File.ReadAllBytes(again), Is.EqualTo(File.ReadAllBytes(once)));
        }

        [Test]
        public void An_invented_value_is_drawn_wherever_the_terminal_would_show_the_real_one()
        {
            // The real line, replaced, makes exactly the file the invented line printed as it is
            // makes, so nothing of the real one is left in any frame.
            var replaced = Told(false, Actual, true);
            var printed = Told(false, Invented, false);
            var unreplaced = Told(false, Actual, false);

            Assert.That(File.ReadAllBytes(replaced), Is.EqualTo(File.ReadAllBytes(printed)));
            Assert.That(File.ReadAllBytes(unreplaced), Is.Not.EqualTo(File.ReadAllBytes(printed)),
                "and the two lines draw differently, so the comparison above can tell them apart");
        }

        [Test]
        public void Every_frame_is_held_for_a_time_a_browser_shows_as_it_is()
        {
            // Browsers draw a frame held for fewer than two hundredths of a second for a tenth of
            // one instead, which would stretch every step built of short frames: typing, printing,
            // the pointer's path and a cap's fade. The published picture's shortest is six.
            var told = Told(false, Actual, true);

            using (var image = Image.FromFile(told))
            {
                var delays = image.GetPropertyItem(0x5100).Value;
                for (var at = 0; at < delays.Length; at += 4)
                {
                    Assert.That(BitConverter.ToInt32(delays, at), Is.GreaterThanOrEqualTo(2), "frame " + (at / 4));
                }
            }
        }

        /// <summary>The story, written to a file of its own, and the file's path.</summary>
        private string Told(bool slowly, string printed, bool invent)
        {
            var path = Paths.Join(_directory, Guid.NewGuid().ToString("N") + ".gif");

            using (var story = new Storyboard())
            {
                if (invent)
                {
                    story.Invent(Actual, Invented);
                }

                var main = story.Keep(Picture(320, 200, Color.SteelBlue), new Rectangle(100, 100, 320, 200));
                var dialog = story.Keep(Picture(160, 90, Color.Beige), new Rectangle(180, 150, 160, 90));
                var button = new Rectangle(290, 210, 40, 20);

                story.Step(Side.Client, "The client pairs");
                story.Show(main);
                Pause(slowly);
                story.Type("pykeepassgate pair --name agent-invented");
                story.Ask("Invitation: ");
                story.Echo("********");
                story.Keys("Ctrl+V");
                story.Keys("Enter");
                story.Answered();
                Pause(slowly);
                story.Print(new Run(0, "paired\n" + printed + "\n", string.Empty));
                story.Hold(120);

                story.Step(Side.Operator, "The operator approves");
                Pause(slowly);
                story.MoveTo(button);
                story.Click(main, dialog);
                story.Hold(80);

                story.Write(path);
            }

            return path;
        }

        private static void Pause(bool slowly)
        {
            if (slowly)
            {
                Thread.Sleep(30);
            }
        }

        /// <summary>A window's picture: a frame of one color with a band across it, as any window has.</summary>
        private static Bitmap Picture(int width, int height, Color color)
        {
            var picture = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(picture))
            using (var fill = new SolidBrush(color))
            using (var band = new SolidBrush(Color.FromArgb(0x21, 0x25, 0x29)))
            {
                graphics.FillRectangle(fill, 0, 0, width, height);
                graphics.FillRectangle(band, 0, 0, width, 18);
            }

            return picture;
        }
    }
}
