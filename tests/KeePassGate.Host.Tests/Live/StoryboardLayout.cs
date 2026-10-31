using System;
using System.Drawing;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Where everything in the moving picture goes: the titles, the terminal, the pane showing the
    /// part of the screen the dialogs were on, the caption under both and the rail along the foot.
    /// </summary>
    /// <remarks>
    /// **Every measure of the picture is here**, and <see cref="Storyboard"/> asks this for places
    /// rather than working them out itself, so the dependency runs one way: the storyboard reads
    /// this, and this, being a class of its own, cannot reach anything private to the storyboard.
    /// </remarks>
    internal sealed class StoryboardLayout
    {
        private const int Margin = 16;
        private const int TitleHeight = 22;
        private const int PaneInset = 12;
        private const int TerminalWidth = 600;

        /// <summary>
        /// The largest the picture is drawn, beyond which the KeePass side is shrunk to fit: wide
        /// enough for a README, tall enough for a laptop's screen.
        /// </summary>
        private const int MostWidth = 1400;

        private const int MostHeight = 820;
        private const int CaptionHeight = 46;

        /// <summary>How far below the panes the caption's line begins.</summary>
        private const int CaptionDrop = 12;

        /// <summary>How tall the rail along the foot of the picture is.</summary>
        private const int RailHeight = 4;

        private readonly Rectangle _screen;

        public StoryboardLayout(Rectangle screen)
        {
            _screen = screen;

            // Shrunk to fit rather than cut to fit: the main window has to be seen whole, title
            // and all, for a reader to know it for KeePass at a glance.
            var room = new Size(
                MostWidth - TerminalWidth - (3 * Margin) - (2 * PaneInset),
                MostHeight - Margin - TitleHeight - CaptionHeight - (2 * PaneInset));
            Scale = Math.Min(1.0, Math.Min((double)room.Width / screen.Width, (double)room.Height / screen.Height));

            var shown = new Size(
                (int)Math.Ceiling(screen.Width * Scale), (int)Math.Ceiling(screen.Height * Scale));
            var paneHeight = shown.Height + (2 * PaneInset);
            Terminal = new Rectangle(Margin, Margin + TitleHeight, TerminalWidth, paneHeight);
            Desk = new Rectangle(Terminal.Right + Margin, Terminal.Y, shown.Width + (2 * PaneInset), paneHeight);
            Width = Desk.Right + Margin;
            Height = Desk.Bottom + CaptionHeight;
        }

        /// <summary>How much smaller than on the screen the KeePass side is drawn, at most 1.</summary>
        public double Scale { get; }

        /// <summary>The terminal's pane, on the left.</summary>
        public Rectangle Terminal { get; }

        /// <summary>The pane on the right, showing the part of the screen the dialogs were on.</summary>
        public Rectangle Desk { get; }

        /// <summary>How wide the whole picture is.</summary>
        public int Width { get; }

        /// <summary>How tall the whole picture is, the caption under the panes included.</summary>
        public int Height { get; }

        /// <summary>How far down from the top the titles over the two panes are drawn.</summary>
        public int TitleTop
        {
            get { return Margin; }
        }

        /// <summary>Where the caption under both panes begins, the number of its step first.</summary>
        public Point Caption
        {
            get { return new Point(Margin, Desk.Bottom + CaptionDrop); }
        }

        /// <summary>The rail along the foot of the picture, which shows how far the story has got.</summary>
        public Rectangle Rail
        {
            get { return new Rectangle(0, Height - RailHeight, Width, RailHeight); }
        }

        /// <summary>The part of a pane that is drawn in, inside the inset along its edges.</summary>
        public static Rectangle Inside(Rectangle pane)
        {
            return Rectangle.Inflate(pane, -PaneInset, -PaneInset);
        }

        /// <summary>Where a point on the screen falls in the picture.</summary>
        public Point OnDesk(Point screen)
        {
            return new Point(
                Desk.X + PaneInset + (int)Math.Round((screen.X - _screen.X) * Scale),
                Desk.Y + PaneInset + (int)Math.Round((screen.Y - _screen.Y) * Scale));
        }
    }
}
