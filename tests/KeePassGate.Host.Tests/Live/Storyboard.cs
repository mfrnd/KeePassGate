using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The moving picture, as a timeline: a terminal on the left, KeePass on the right and a line
    /// under both saying what is happening, put together from what really ran and really showed.
    /// </summary>
    /// <remarks>
    /// **Timed by the story, never by the clock.** Every frame is worked out from the pictures the
    /// fixture took, the rectangles UI automation reported for the controls the operator used, and
    /// the text the client typed and printed, at a pace written down here. How long the real
    /// session took to get anywhere is not in the picture, so two runs that took different times
    /// make the same file.
    ///
    /// **The terminal is drawn rather than photographed.** What is in it is real: the command line
    /// that was run, typed out a character at a time, and what the program wrote to its two
    /// streams, a line at a time. A console window would have added nothing to that but a title
    /// bar, a font chosen by whoever configured it and the account's own prompt, and the last of
    /// those names the account.
    ///
    /// **The KeePass side is a view of the screen** that takes in the whole main window and every
    /// dialog the story opens over it, each drawn where it really was, and shrunk to fit when that
    /// is more than the picture has room for. Shrunk rather than cut: a main window without its
    /// left edge and title is not recognizably KeePass. The pictures are
    /// the fixture's own captures, the same as a still in `docs/images`, with their values settled
    /// and covered the same way.
    ///
    /// **The pointer is drawn**, because a screen copy never holds one: an arrow that travels to
    /// the middle of each control the operator used, on an eased path, and a ring where it
    /// clicks, shown a moment before the press changes anything and fading a moment after. The
    /// middle is where `Mouse` presses, so the drawn click lands where the real one did.
    ///
    /// **So are the keys**, for the same reason: a key pressed shows as a cap with its name on it,
    /// on the side that pressed it, for as long as a reader needs to see it. A paste at a prompt
    /// that does not echo is otherwise a moment in which nothing on the screen changes.
    ///
    /// **Whichever side is not acting is dimmed**, and keeps showing what it showed last, which is
    /// what it would be showing on a real desk at that moment.
    ///
    /// **What cannot be settled at its source is replaced in the text**, by a value written down
    /// and patterned so that nobody takes it for a real one. See <see cref="Invent"/>.
    /// </remarks>
    // codeql[cs/coupled-types] `Layout` is a private part of it, nested inside it
    internal sealed class Storyboard : IDisposable
    {
        /// <summary>Which side of the picture a step happens on.</summary>
        public enum Side
        {
            Operator,
            Client
        }

        private enum Kind
        {
            Command,
            Output,
            Error
        }

        private const string Prompt = "$ ";

        // The pace, in hundredths of a second: a character typed, a line printed, a step of the
        // pointer, the ring held before a press lands and each step of it fading after, the half of
        // a caret's blink, and how long a key's cap holds the story and then stays in its corner.
        private const int Keystroke = 6;
        private const int Printing = 6;
        private const int PointerStep = 6;
        private const int PointerSteps = 12;
        private const int Pressing = 20;
        private const int RingStep = 7;
        private const int RingSteps = 6;
        private const int Blink = 50;
        private const int KeyShown = 70;
        private const int KeyLingers = 150;
        private const int KeyFades = 50;
        private const int FadeStep = 10;

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
        private const int LineHeight = 17;
        private const int CaptionHeight = 46;

        /// <summary>How much of the canvas color is laid over the side that is not acting, out of 255.</summary>
        private const int Dimming = 150;

        private static readonly Color Canvas = Color.FromArgb(0xE9, 0xEC, 0xEF);
        private static readonly Color Ink = Color.FromArgb(0x21, 0x25, 0x29);
        private static readonly Color Muted = Color.FromArgb(0x49, 0x50, 0x57);
        private static readonly Color Accent = Color.FromArgb(0x0B, 0x5E, 0xD7);
        private static readonly Color SecondButton = Color.FromArgb(0xD9, 0x48, 0x0F);
        private static readonly Color Rail = Color.FromArgb(0xCE, 0xD4, 0xDA);
        private static readonly Color TerminalBack = Color.FromArgb(0x1E, 0x1E, 0x1E);
        private static readonly Color TerminalText = Color.FromArgb(0xD4, 0xD4, 0xD4);
        private static readonly Color CommandText = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color PromptText = Color.FromArgb(0x6A, 0xC9, 0x55);
        private static readonly Color ErrorText = Color.FromArgb(0xF4, 0x87, 0x71);

        /// <summary>The standard arrow, its tip at the origin, in pixels.</summary>
        private static readonly Point[] Arrow =
        {
            new Point(0, 0), new Point(0, 17), new Point(4, 13), new Point(7, 19),
            new Point(10, 18), new Point(7, 12), new Point(12, 12)
        };

        private readonly List<KeyValuePair<Kind, string>> _lines = new List<KeyValuePair<Kind, string>>();
        private readonly List<KeyValuePair<string, string>> _invented = new List<KeyValuePair<string, string>>();
        private readonly List<Frame> _frames = new List<Frame>();
        private readonly List<Bitmap> _pictures = new List<Bitmap>();

        private Shot[] _windows = Array.Empty<Shot>();
        private Side _acting;
        private string _caption = string.Empty;
        private int _step;
        private string _typing;
        private bool _running;
        private string _question;
        private int _blinking;
        private Point? _pointer;
        private readonly List<Cap> _caps = new List<Cap>();

        /// <summary>
        /// A picture of a KeePass window and where on the screen it was taken, owned from here on
        /// by this storyboard.
        /// </summary>
        public Shot Keep(Bitmap picture, Rectangle area)
        {
            _pictures.Add(picture);
            return new Shot(picture, area);
        }

        /// <summary>
        /// Shows `invented` wherever the terminal would show `actual`, in every frame.
        /// </summary>
        /// <remarks>
        /// For what the run makes new every time and nothing outside the client can fix: the key of
        /// a certificate the client generated itself, the moment it expires, and the directory the
        /// identity was stored in, which is this session's and not a place anybody would look.
        /// </remarks>
        public void Invent(string actual, string invented)
        {
            if (!string.IsNullOrEmpty(actual))
            {
                _invented.Add(new KeyValuePair<string, string>(actual, invented));
            }
        }

        /// <summary>A new step of the story, told on one side, under a caption of its own.</summary>
        public void Step(Side acting, string caption)
        {
            _acting = acting;
            _caption = caption;
            _step++;

            // A new step lets the caps of the last one go, fading rather than vanishing.
            foreach (var cap in _caps)
            {
                cap.Left = Math.Min(cap.Left, KeyFades);
            }
        }

        /// <summary>The KeePass windows on screen from now on, bottom first, the main window among them.</summary>
        /// <remarks>
        /// The main window is a picture like any other rather than a backdrop taken once, because
        /// it changes while the story runs: a search lists every entry, choosing a group lists that
        /// group's, and a menu opens over it.
        /// </remarks>
        public void Show(params Shot[] windows)
        {
            _windows = windows ?? Array.Empty<Shot>();
        }

        /// <summary>Puts the pointer somewhere, on the screen, without moving it there.</summary>
        public void PointerAt(Point screen)
        {
            _pointer = screen;
        }

        /// <summary>
        /// Nothing moves for this many hundredths of a second, except a caret blinking and a key's
        /// cap fading.
        /// </summary>
        public void Hold(int hundredths)
        {
            while (hundredths > 0)
            {
                var left = Blink - (_blinking % Blink);
                var chunk = Math.Min(Math.Min(left, hundredths), UntilTheCapsChange());
                Emit(chunk);
                _blinking += chunk;
                hundredths -= chunk;
            }
        }

        /// <summary>A command typed at the prompt, a character at a time, and Enter.</summary>
        public void Type(string commandLine)
        {
            var typed = new StringBuilder();
            _blinking = 0;
            foreach (var character in commandLine)
            {
                typed.Append(character);
                _typing = typed.ToString();
                Emit(Keystroke);
            }

            _typing = null;
            _lines.Add(new KeyValuePair<Kind, string>(Kind.Command, commandLine));
            _running = true;
            _blinking = 0;
            Emit(Keystroke);
        }

        /// <summary>
        /// What the command wrote, a line at a time, standard output first and then standard error,
        /// and the prompt back.
        /// </summary>
        /// <remarks>
        /// One stream after the other rather than interleaved, which is exact for every command in
        /// the picture: each writes to one of them or to neither.
        /// </remarks>
        public void Print(ReferenceClient.Run run)
        {
            foreach (var line in Lines(run.Output))
            {
                _lines.Add(new KeyValuePair<Kind, string>(Kind.Output, line));
                Emit(Printing);
            }

            foreach (var line in Lines(run.Errors))
            {
                _lines.Add(new KeyValuePair<Kind, string>(Kind.Error, line));
                Emit(Printing);
            }

            _running = false;
            _question = null;
            _blinking = 0;
            Emit(Printing);
        }

        /// <summary>
        /// The running command asks for something on its own line: the caret waits after the
        /// prompt, and after whatever <see cref="Echo"/> shows of the answer, until
        /// <see cref="Answered"/>.
        /// </summary>
        /// <remarks>
        /// Held as the frame's own state rather than written into the lines until it is answered,
        /// because a line written there is read by every frame that counts it, and the stars of an
        /// answer would then appear in the frames from before it was given.
        /// </remarks>
        public void Ask(string prompt)
        {
            _question = prompt;
            _blinking = 0;
            Emit(Printing);
        }

        /// <summary>What the prompt shows of the answer so far, such as a star per character.</summary>
        public void Echo(string shown)
        {
            _question = _question + shown;
            Emit(Printing);
        }

        /// <summary>
        /// The answer is in: the prompt's line stays as it was left, and the command goes on with
        /// the caret on a line of its own.
        /// </summary>
        public void Answered()
        {
            _lines.Add(new KeyValuePair<Kind, string>(Kind.Output, _question));
            _question = null;
            _blinking = 0;
            Emit(Keystroke);
        }

        /// <summary>A key, or keys held together, pressed on the side that is acting, shown as a cap.</summary>
        /// <remarks>
        /// The story pauses on the cap only briefly and then goes on, while the cap stays in its
        /// corner a while longer and then fades: a reader is watching the pointer or the text, not
        /// the corner, and a cap gone as soon as it came was easy to miss. Keys pressed one after
        /// another stand in a row, the newest on the right, so Ctrl+V and then Enter read as the
        /// two presses they were. A key pressed on the other side clears the row, and a new step
        /// lets it fade.
        /// </remarks>
        public void Keys(string pressed)
        {
            if (_caps.Count > 0 && _caps[0].On != _acting)
            {
                _caps.Clear();
            }

            _caps.Add(new Cap(pressed, _acting, KeyLingers + KeyFades));
            Hold(KeyShown);
        }

        /// <summary>
        /// How long until a cap starts fading or takes its next step down, so that a long hold is
        /// cut into frames short enough for the fade to be seen.
        /// </summary>
        private int UntilTheCapsChange()
        {
            var soonest = int.MaxValue;
            foreach (var until in _caps.Select(cap => cap.Left > KeyFades ? cap.Left - KeyFades : FadeStep))
            {
                soonest = Math.Min(soonest, Math.Max(1, until));
            }

            return soonest;
        }

        /// <summary>The pointer travels to the middle of a control, on the screen.</summary>
        public void MoveTo(Rectangle control)
        {
            var to = new Point(control.X + (control.Width / 2), control.Y + (control.Height / 2));
            var from = _pointer ?? to;

            for (var step = 1; step <= PointerSteps; step++)
            {
                // Smoothstep: slow away, quick across, slow to arrive, as a hand moves a mouse.
                var t = (double)step / PointerSteps;
                var eased = t * t * (3 - (2 * t));
                _pointer = new Point(
                    (int)Math.Round(from.X + ((to.X - from.X) * eased)),
                    (int)Math.Round(from.Y + ((to.Y - from.Y) * eased)));
                Emit(PointerStep);
            }
        }

        /// <summary>
        /// A click where the pointer is, and the windows it leaves on screen, which appear once
        /// the ring it draws has been seen. Naming none leaves the screen as it was.
        /// </summary>
        public void Click(params Shot[] after)
        {
            Clicked(Accent, after);
        }

        /// <summary>The same with the second button, drawn in a color of its own.</summary>
        public void RightClick(params Shot[] after)
        {
            Clicked(SecondButton, after);
        }

        /// <summary>Draws every frame and writes them as one animated GIF.</summary>
        public void Write(string path)
        {
            // The main window and every dialog over it, so that the view takes all of them in.
            var screen = Rectangle.Empty;
            foreach (var frame in _frames)
            {
                foreach (var window in frame.Windows)
                {
                    screen = screen.IsEmpty ? window.Area : Rectangle.Union(screen, window.Area);
                }
            }

            var layout = new Layout(screen);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var gif = new AnimatedGif(file, layout.Width, layout.Height))
            using (var fonts = new Fonts())
            {
                foreach (var frame in _frames)
                {
                    using (var drawn = Draw(frame, layout, fonts))
                    {
                        gif.Add(drawn, frame.Hundredths);
                    }
                }
            }
        }

        public void Dispose()
        {
            foreach (var picture in _pictures)
            {
                picture.Dispose();
            }

            _pictures.Clear();
        }

        /// <summary>
        /// The ring first, held while nothing has changed yet, then what the press did, with the
        /// ring widening and fading over it.
        /// </summary>
        /// <remarks>
        /// A ring that arrived with the change was seen after the fact, if at all: the screen
        /// changed and the eye went to the change. Shown first, it says where the click is about
        /// to land, and kept a little after, it ties what changed to the place that changed it.
        /// </remarks>
        private void Clicked(Color color, Shot[] after)
        {
            Emit(Pressing, new Ring(color, 0));

            // A press that changes nothing on screen, such as one into the search box, names no
            // windows, and the ones already there stay: replacing them with none blanked the
            // whole of KeePass for as long as the ring took.
            if (after.Length > 0)
            {
                Show(after);
            }

            for (var ring = 0; ring < RingSteps; ring++)
            {
                Emit(RingStep, new Ring(color, ring));
            }
        }

        private void Emit(int hundredths, Ring ring = null)
        {
            var caret = _typing != null || (_blinking / Blink) % 2 == 0;
            _frames.Add(new Frame
            {
                Lines = _lines.Count,
                Typing = _typing,
                Running = _running,
                Question = _question,
                Caret = caret,
                Caps = _caps
                    .Select(cap => new KeyValuePair<string, int>(
                        cap.Keys, cap.Left >= KeyFades ? 255 : (255 * cap.Left) / KeyFades))
                    .ToArray(),
                CapsOn = _caps.Count > 0 ? _caps[0].On : _acting,
                Windows = _windows,
                Pointer = _pointer,
                Ring = ring,
                Acting = _acting,
                Caption = _caption,
                Step = _step,
                Hundredths = hundredths
            });

            foreach (var cap in _caps)
            {
                cap.Left -= hundredths;
            }

            _caps.RemoveAll(cap => cap.Left <= 0);
        }

        private static IEnumerable<string> Lines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<string>();
            }

            return text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        }

        private Bitmap Draw(Frame frame, Layout layout, Fonts fonts)
        {
            var canvas = new Bitmap(layout.Width, layout.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.Clear(Canvas);

                Text(graphics, "The client, in a terminal", fonts.Title, Muted, layout.Terminal.X, Margin);
                Text(graphics, "The operator, in KeePass", fonts.Title, Muted, layout.Desk.X, Margin);

                DrawTerminal(graphics, frame, layout.Terminal, fonts.Mono);
                DrawDesk(graphics, frame, layout);

                using (var veil = new SolidBrush(Color.FromArgb(Dimming, Canvas)))
                {
                    graphics.FillRectangle(veil, frame.Acting == Side.Client ? layout.Desk : layout.Terminal);
                }

                if (frame.Caps.Length > 0)
                {
                    DrawKeys(graphics, frame.Caps, frame.CapsOn == Side.Client ? layout.Terminal : layout.Desk, fonts.Title);
                }

                var line = layout.Desk.Bottom + 12;
                var counted = frame.Step.ToString(CultureInfo.InvariantCulture) + ".";
                Text(graphics, counted, fonts.Number, Accent, Margin, line);
                var after = (int)Math.Ceiling(Measure(graphics, counted + " ", fonts.Number));
                Text(graphics, frame.Caption, fonts.Caption, Ink, Margin + after, line);

                // How far through the story this step is, along the foot of the picture.
                using (var track = new SolidBrush(Rail))
                using (var done = new SolidBrush(Accent))
                {
                    var rail = new Rectangle(0, layout.Height - 4, layout.Width, 4);
                    graphics.FillRectangle(track, rail);
                    graphics.FillRectangle(
                        done, new Rectangle(0, rail.Y, layout.Width * frame.Step / Math.Max(1, _step), rail.Height));
                }
            }

            return canvas;
        }

        /// <summary>
        /// The windows on screen, the main window behind them, and the pointer over all of it, in
        /// a pane that shows one part of the screen.
        /// </summary>
        private void DrawDesk(Graphics graphics, Frame frame, Layout layout)
        {
            var state = graphics.Save();
            graphics.SetClip(layout.Desk);

            using (var empty = new SolidBrush(Rail))
            {
                graphics.FillRectangle(empty, layout.Desk);
            }

            foreach (var window in frame.Windows)
            {
                Place(graphics, window, layout);
            }

            if (frame.Pointer.HasValue)
            {
                var at = layout.OnDesk(frame.Pointer.Value);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;

                if (frame.Ring != null)
                {
                    var radius = 6 + (frame.Ring.Phase * 4);
                    var alpha = 230 - (frame.Ring.Phase * 35);
                    using (var pen = new Pen(Color.FromArgb(alpha, frame.Ring.Color), 3f))
                    {
                        graphics.DrawEllipse(pen, at.X - radius, at.Y - radius, radius * 2, radius * 2);
                    }
                }

                var arrow = Array.ConvertAll(Arrow, point => new Point(point.X + at.X, point.Y + at.Y));
                using (var fill = new SolidBrush(Color.White))
                using (var outline = new Pen(Color.Black, 1f))
                {
                    graphics.FillPolygon(fill, arrow);
                    graphics.DrawPolygon(outline, arrow);
                }

                graphics.SmoothingMode = SmoothingMode.None;
            }

            graphics.Restore(state);
        }

        private static void Place(Graphics graphics, Shot shot, Layout layout)
        {
            // Both corners mapped, rather than one corner and a scaled size, so that two windows
            // whose edges met on the screen still meet in the picture.
            var at = layout.OnDesk(shot.Area.Location);
            var to = layout.OnDesk(new Point(shot.Area.Right, shot.Area.Bottom));

            // At full size a pixel is copied as it is; smaller, it is resampled smoothly, which is
            // the only way text drawn at screen size stays readable once shrunk.
            graphics.InterpolationMode = layout.Scale < 1.0
                ? InterpolationMode.HighQualityBicubic
                : InterpolationMode.NearestNeighbor;
            graphics.DrawImage(
                shot.Picture,
                new Rectangle(at.X, at.Y, to.X - at.X, to.Y - at.Y),
                new Rectangle(0, 0, shot.Picture.Width, shot.Picture.Height),
                GraphicsUnit.Pixel);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        }

        /// <summary>
        /// The terminal as it stood: every line wrapped to the pane, the last that fit, and the
        /// line being typed, the prompt or a running command's cursor after them.
        /// </summary>
        private void DrawTerminal(Graphics graphics, Frame frame, Rectangle pane, Font mono)
        {
            using (var back = new SolidBrush(TerminalBack))
            {
                graphics.FillRectangle(back, pane);
            }

            var cell = Measure(graphics, new string('M', 20), mono) / 20f;
            var columns = (int)Math.Floor((pane.Width - (2 * PaneInset)) / cell);
            var fits = (pane.Height - (2 * PaneInset)) / LineHeight;

            var rows = new List<KeyValuePair<Kind, string>>();
            for (var i = 0; i < frame.Lines; i++)
            {
                var kind = _lines[i].Key;
                var text = Invented(_lines[i].Value);
                Wrap(rows, kind, kind == Kind.Command ? Prompt + text : text, columns);
            }

            if (frame.Typing != null)
            {
                Wrap(rows, Kind.Command, Prompt + Invented(frame.Typing), columns);
            }
            else if (frame.Running)
            {
                // Waiting at a prompt of its own, the caret stays on the prompt's line, after what
                // the prompt shows of the answer so far.
                rows.Add(new KeyValuePair<Kind, string>(Kind.Output, frame.Question ?? string.Empty));
            }
            else
            {
                rows.Add(new KeyValuePair<Kind, string>(Kind.Command, Prompt));
            }

            var first = Math.Max(0, rows.Count - fits);
            var y = pane.Y + PaneInset;
            var x = pane.X + PaneInset;
            for (var i = first; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Key == Kind.Command && row.Value.StartsWith(Prompt, StringComparison.Ordinal))
                {
                    Text(graphics, Prompt, mono, PromptText, x, y);
                    Text(graphics, row.Value.Substring(Prompt.Length), mono, CommandText, x + (Prompt.Length * cell), y);
                }
                else
                {
                    Text(graphics, row.Value, mono, Colored(row.Key), x, y);
                }

                if (i == rows.Count - 1 && frame.Caret)
                {
                    using (var caret = new SolidBrush(TerminalText))
                    {
                        graphics.FillRectangle(caret, x + (row.Value.Length * cell), y + 2, cell, LineHeight - 3);
                    }
                }

                y += LineHeight;
            }
        }

        /// <summary>
        /// The key caps in a row in the lower right corner of the side that pressed them, the
        /// newest on the right, each as opaque as how far it has faded allows.
        /// </summary>
        private static void DrawKeys(Graphics graphics, KeyValuePair<string, int>[] caps, Rectangle pane, Font font)
        {
            const int padding = 10;
            const int between = 8;
            var right = pane.Right - PaneInset;

            for (var i = caps.Length - 1; i >= 0; i--)
            {
                var keys = caps[i].Key;
                var opacity = caps[i].Value;
                var size = graphics.MeasureString(keys, font, PointF.Empty, StringFormat.GenericTypographic);
                var cap = new Rectangle(
                    0, 0, (int)Math.Ceiling(size.Width) + (2 * padding), (int)Math.Ceiling(size.Height) + padding);
                cap.Offset(right - cap.Width, pane.Bottom - PaneInset - cap.Height);
                right = cap.X - between;

                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(cap, 6))
                using (var fill = new SolidBrush(Color.FromArgb((235 * opacity) / 255, Ink)))
                using (var edge = new Pen(Color.FromArgb(opacity, Accent), 2f))
                {
                    graphics.FillPath(fill, path);
                    graphics.DrawPath(edge, path);
                }

                graphics.SmoothingMode = SmoothingMode.None;
                Text(graphics, keys, font, Color.FromArgb(opacity, Color.White), cap.X + padding, cap.Y + (padding / 2f));
            }
        }

        private static GraphicsPath Rounded(Rectangle area, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(area.X, area.Y, diameter, diameter, 180, 90);
            path.AddArc(area.Right - diameter, area.Y, diameter, diameter, 270, 90);
            path.AddArc(area.Right - diameter, area.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(area.X, area.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Colored(Kind kind)
        {
            switch (kind)
            {
                case Kind.Command:
                    return CommandText;
                case Kind.Error:
                    return ErrorText;
                default:
                    return TerminalText;
            }
        }

        private string Invented(string text)
        {
            foreach (var replacement in _invented)
            {
                text = text.Replace(replacement.Key, replacement.Value);
            }

            return text;
        }

        /// <summary>
        /// A line cut into pieces no wider than the terminal, at a space where there is one and
        /// mid word where there is not, as a terminal wraps a line it was given whole.
        /// </summary>
        private static void Wrap(List<KeyValuePair<Kind, string>> rows, Kind kind, string text, int columns)
        {
            var rest = text;
            while (rest.Length > columns)
            {
                var cut = rest.LastIndexOf(' ', columns);
                if (cut <= 0)
                {
                    cut = columns;
                }

                rows.Add(new KeyValuePair<Kind, string>(kind, rest.Substring(0, cut)));
                rest = rest.Substring(cut).TrimStart(' ');
            }

            rows.Add(new KeyValuePair<Kind, string>(kind, rest));
        }

        private static void Text(Graphics graphics, string text, Font font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color))
            {
                graphics.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic);
            }
        }

        private static float Measure(Graphics graphics, string text, Font font)
        {
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                return graphics.MeasureString(text, font, PointF.Empty, format).Width;
            }
        }

        /// <summary>A picture of one KeePass window and the rectangle of the screen it was taken from.</summary>
        internal sealed class Shot
        {
            public Shot(Bitmap picture, Rectangle area)
            {
                Picture = picture;
                Area = area;
            }

            public Bitmap Picture { get; }

            public Rectangle Area { get; }
        }

        /// <summary>A key pressed, the side that pressed it, and how much longer its cap shows.</summary>
        private sealed class Cap
        {
            public Cap(string keys, Side on, int left)
            {
                Keys = keys;
                On = on;
                Left = left;
            }

            public string Keys { get; }

            public Side On { get; }

            /// <summary>Hundredths of a second until it is gone, the last of them spent fading.</summary>
            public int Left { get; set; }
        }

        /// <summary>The ring a click leaves, and how far it has widened.</summary>
        private sealed class Ring
        {
            public Ring(Color color, int phase)
            {
                Color = color;
                Phase = phase;
            }

            public Color Color { get; }

            public int Phase { get; }
        }

        /// <summary>Everything one frame shows, recorded as the timeline reached it.</summary>
        private sealed class Frame
        {
            /// <summary>How many of the terminal's lines had been written by then.</summary>
            public int Lines { get; set; }

            /// <summary>The command being typed, as far as it has got, or null.</summary>
            public string Typing { get; set; }

            /// <summary>Whether the last command was still running, so there is no prompt yet.</summary>
            public bool Running { get; set; }

            /// <summary>
            /// The prompt the running command was waiting at, with what it showed of the answer so
            /// far, or null when it was not waiting at one.
            /// </summary>
            public string Question { get; set; }

            /// <summary>Whether the caret is on, in its blink.</summary>
            public bool Caret { get; set; }

            /// <summary>
            /// The keys on show, oldest first, each with how opaque its cap is, out of 255.
            /// </summary>
            public KeyValuePair<string, int>[] Caps { get; set; }

            /// <summary>The side that pressed them, whose corner the caps stand in.</summary>
            public Side CapsOn { get; set; }

            public Shot[] Windows { get; set; }

            /// <summary>Where the pointer is, on the screen, or nowhere yet.</summary>
            public Point? Pointer { get; set; }

            public Ring Ring { get; set; }

            public Side Acting { get; set; }

            public string Caption { get; set; }

            public int Step { get; set; }

            public int Hundredths { get; set; }
        }

        /// <summary>
        /// Where everything goes: the terminal, the pane showing the part of the screen the
        /// dialogs were on, and the caption under both.
        /// </summary>
        private sealed class Layout
        {
            private readonly Rectangle _screen;

            public Layout(Rectangle screen)
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

            public Rectangle Terminal { get; }

            public Rectangle Desk { get; }

            public int Width { get; }

            public int Height { get; }

            /// <summary>Where a point on the screen falls in the picture.</summary>
            public Point OnDesk(Point screen)
            {
                return new Point(
                    Desk.X + PaneInset + (int)Math.Round((screen.X - _screen.X) * Scale),
                    Desk.Y + PaneInset + (int)Math.Round((screen.Y - _screen.Y) * Scale));
            }
        }

        /// <summary>
        /// The fonts every frame is drawn in, in pixels rather than points, so that the picture is
        /// the same size whatever the display's scaling.
        /// </summary>
        private sealed class Fonts : IDisposable
        {
            public Fonts()
            {
                Mono = Named("Consolas", 13f, FontStyle.Regular, FontFamily.GenericMonospace);
                Title = Named("Segoe UI", 12f, FontStyle.Bold, FontFamily.GenericSansSerif);
                Caption = Named("Segoe UI", 15f, FontStyle.Regular, FontFamily.GenericSansSerif);
                Number = Named("Segoe UI", 15f, FontStyle.Bold, FontFamily.GenericSansSerif);
            }

            public Font Mono { get; }

            public Font Title { get; }

            public Font Caption { get; }

            public Font Number { get; }

            public void Dispose()
            {
                Mono.Dispose();
                Title.Dispose();
                Caption.Dispose();
                Number.Dispose();
            }

            /// <summary>A font by name, or the family given in its place where this machine lacks it.</summary>
            /// <remarks>
            /// Checked by name afterwards, because GDI+ answers a family it does not have with
            /// Microsoft Sans Serif and says nothing, the fact `AuthorityPanel.CodeFont` rests on.
            /// </remarks>
            private static Font Named(string name, float pixels, FontStyle style, FontFamily otherwise)
            {
                var font = new Font(name, pixels, style, GraphicsUnit.Pixel);
                if (string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return font;
                }

                font.Dispose();
                return new Font(otherwise, pixels, style, GraphicsUnit.Pixel);
            }
        }
    }
}
