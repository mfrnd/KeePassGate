using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Automation;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Covers the few controls whose contents are new on every run and cannot be settled at the
    /// source, so a regenerated picture differs only where the screen did.
    /// </summary>
    /// <remarks>
    /// **A last resort, and the smallest one available.** Everywhere else the value itself was made
    /// to hold still: the authority and the admitted client are written into the database, and the
    /// invitation code is put into its box. What is left cannot be reached either way. An
    /// invitation identifier is derived from a token minted in the plugin's memory, whose only seam
    /// is a clock, and the port a client connected from is chosen by the operating system. Both are
    /// shown in labels, and a label paints from its managed text and never reads the window's, so
    /// nothing outside the process can write to one.
    ///
    /// **The rectangle is measured, not written down.** It comes from the control's own bounding
    /// rectangle at the moment of the screenshot, so it moves when the layout moves. A rectangle in
    /// coordinates would be the same mistake as a corner radius asserted here: right until the
    /// dialog reflows, and wrong quietly afterwards.
    ///
    /// **It runs to the edge of what holds it**, rather than stopping where the text stops. These
    /// labels size themselves to their contents, so a shorter value gives a narrower control and a
    /// narrower bar: two runs then differed by the six pixels of bar that one of them did not have.
    /// The parent's right edge is measured too, and does not move with the text.
    ///
    /// **It is a visible bar rather than a plausible value.** A number painted in would be a
    /// picture claiming the program produced something it did not, and a reader could not tell. A
    /// bar says a value is not being shown, which is true and ordinary in documentation.
    ///
    /// **What this costs.** Inside the bar the picture can no longer report anything, including a
    /// label that has moved or text that has outgrown its room. That is the reason to cover as
    /// little as possible, and the reason nothing is covered that could be settled instead.
    /// </remarks>
    internal static class Redacted
    {
        /// <summary>
        /// The controls whose contents are new every run, by the identifier they carry.
        /// </summary>
        /// <remarks>
        /// The two on the approval dialog are a fact's value and nothing else, so the bar is
        /// exactly the value. The one on the clients page is a whole sentence with the identifier
        /// inside it, so the bar covers the sentence: that is the smallest thing this can measure,
        /// because the identifier is not a control of its own.
        /// </remarks>
        private static readonly string[] Volatile =
        {
            // Derived, the way the dialog derives them. Spelling `KpgFactInvitationIdValue`
            // out here would be a second copy of a name nobody writes: `Fact.ControlName` builds
            // it from the caption, so the two agree until somebody rewords the caption, and then
            // they disagree without a word.
            new KeePassGate.Ui.Fact(KeePassGate.Ui.GateTab.InvitationIdFactName, null).ControlName(true),
            new KeePassGate.Ui.Fact(KeePassGate.Ui.GateTab.PeerFactName, null).ControlName(true),
            KeePassGate.Ui.GateTab.InvitationLabelName,

            // The renewal dialog's dates: the client it is about is issued when the picture is
            // taken, so when it was admitted and when its certificate ends are this minute's.
            new KeePassGate.Ui.Fact(KeePassGate.Ui.GateTab.RenewalAdmittedFactName, null).ControlName(true),
            new KeePassGate.Ui.Fact(KeePassGate.Ui.GateTab.RenewalExpiresFactName, null).ControlName(true),

            // KeePass's own, on the group dialog's first page, which the moving picture opens: a
            // group that never expires shows today's date there, whatever the group holds, so no
            // value written into the database can settle it.
            "m_dtExpires",

            // And the same on the entry dialog's, for an entry that never expires.
            "m_dtExpireDateTime",
        };

        /// <summary>
        /// What one of those controls says when nothing on it is new, which is left uncovered.
        /// </summary>
        /// <remarks>
        /// **Covered only when there is something to hide**, the rule `Steadied` keeps for the box.
        /// The invitation line names an identifier and a count of minutes while a code is out, and
        /// one fixed sentence once there is none, which is what the clients page says after the
        /// refused pairing has spent the only invitation. A bar over that sentence hid a line that
        /// reads the same on every run, and the pages explain the bar as hiding what is new each
        /// time.
        /// </remarks>
        private static readonly string[] Settled =
        {
            KeePassGate.Ui.InvitationText.None,
        };

        /// <summary>Gray rather than black, so a bar reads as a bar and not as a hole.</summary>
        private static readonly Color Bar = Color.FromArgb(255, 108, 108, 108);

        /// <summary>
        /// Paints over the cells of one client's row on the clients page that hold a moment, given
        /// the picture and where on screen it was taken from, and leaving alone whatever lies
        /// inside `spared`.
        /// </summary>
        /// <remarks>
        /// **For the moving picture, whose client is paired for real**, so when it was admitted and
        /// when its certificate ends are this minute's. The client in the still pictures is written
        /// into the database instead, which is why nothing else needs this.
        ///
        /// **Measured from the column headers and the row's own cell**: a cell is drawn by the grid
        /// rather than being a window, but UI automation still reports where each header and each
        /// cell is, and a column's header spans exactly the column's width. So the bar is the
        /// column's width by the row's height, which does not move with the value under it.
        ///
        /// **`spared` is a menu open over the row**, which the picture is of, and a bar over the
        /// cell must not be painted over the menu as well.
        /// </remarks>
        public static void Moments(
            Bitmap picture, Rectangle area, AutomationElement window, string client, Rectangle spared)
        {
            var grid = Uia.ById(window, KeePassGate.Ui.ClientList.ListName);
            var cell = KeePassDriver.ClientCell(window, client);
            if (grid == null || cell == null)
            {
                throw new InvalidOperationException(
                    "The clients page lists no " + client + ", so nothing on its row can be covered.");
            }

            var row = cell.Current.BoundingRectangle;

            using (var brush = new SolidBrush(Bar))
            using (var graphics = Graphics.FromImage(picture))
            {
                if (!spared.IsEmpty)
                {
                    graphics.ExcludeClip(new Rectangle(
                        spared.X - area.X, spared.Y - area.Y, spared.Width, spared.Height));
                }

                foreach (var header in MomentHeaders)
                {
                    var column = HeaderOf(grid, header);
                    if (column.IsEmpty)
                    {
                        throw new InvalidOperationException(
                            "The clients page has no column headed " + header + ": " + Uia.Inventory(grid));
                    }

                    graphics.FillRectangle(brush, new Rectangle(
                        (int)Math.Floor(column.X) - area.X,
                        (int)Math.Floor(row.Y) - area.Y,
                        (int)Math.Ceiling(column.Width),
                        (int)Math.Ceiling(row.Height)));
                }
            }
        }

        /// <summary>The headers of the two columns on the clients page that hold a moment.</summary>
        private static readonly string[] MomentHeaders = { "Admitted", "Until" };

        /// <summary>Where the header of a column with this caption is, or an empty rectangle.</summary>
        private static System.Windows.Rect HeaderOf(AutomationElement grid, string caption)
        {
            foreach (AutomationElement element in grid.FindAll(
                TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, caption)))
            {
                try
                {
                    var bounds = element.Current.BoundingRectangle;
                    if (!bounds.IsEmpty && bounds.Width >= 1 && bounds.Height >= 1)
                    {
                        return bounds;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Gone between being found and being measured. Another element with this
                    // caption may still answer, and if none does, the caller throws for the column.
                }
            }

            return System.Windows.Rect.Empty;
        }

        /// <summary>
        /// Paints over each of them, given the picture and where on screen it was taken from.
        /// </summary>
        public static void Over(Bitmap picture, Rectangle area, AutomationElement window)
        {
            Over(picture, area, window, Volatile);
        }

        /// <summary>
        /// The same over controls only some pictures need covered, which the caller names.
        /// </summary>
        /// <remarks>
        /// KeePass's entry view under the main window's list is one: it says when the chosen entry
        /// was last changed, and once a client has changed one in the picture that is this
        /// minute. Every other time it shows times written into the database, which read the same
        /// on every run and are worth showing, so it is not on the list every picture is held to.
        /// </remarks>
        public static void Over(Bitmap picture, Rectangle area, AutomationElement window, params string[] identifiers)
        {
            if (picture == null || window == null)
            {
                return;
            }

            using (var brush = new SolidBrush(Bar))
            using (var graphics = Graphics.FromImage(picture))
            {
                foreach (var identifier in identifiers)
                {
                    foreach (var where in RectanglesOf(window, identifier))
                    {
                        var inside = Rectangle.Intersect(
                            new Rectangle(
                                where.X - area.X, where.Y - area.Y, where.Width, where.Height),
                            new Rectangle(Point.Empty, picture.Size));

                        if (inside.Width > 0 && inside.Height > 0)
                        {
                            graphics.FillRectangle(brush, inside);
                        }
                    }
                }
            }
        }

        /// <summary>Where a control is on screen, if it is here at all.</summary>
        private static IEnumerable<Rectangle> RectanglesOf(AutomationElement window, string identifier)
        {
            var found = new List<Rectangle>();

            foreach (AutomationElement element in window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, identifier)))
            {
                try
                {
                    // A label reports its text as its name, which is what is compared here.
                    if (Array.IndexOf(Settled, element.Current.Name) >= 0)
                    {
                        continue;
                    }

                    var bounds = element.Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
                    {
                        continue;
                    }

                    // To the edge of whatever holds it. The label is only as wide as its text, so a
                    // bar that stopped there would be a different width for a different value.
                    var right = bounds.Right;
                    var parent = TreeWalker.RawViewWalker.GetParent(element);
                    if (parent != null)
                    {
                        var around = parent.Current.BoundingRectangle;
                        if (!around.IsEmpty && around.Right > right)
                        {
                            right = around.Right;
                        }
                    }

                    found.Add(new Rectangle(
                        (int)Math.Floor(bounds.X),
                        (int)Math.Floor(bounds.Y),
                        (int)Math.Ceiling(right - bounds.X),
                        (int)Math.Ceiling(bounds.Height)));
                }
                catch (ElementNotAvailableException)
                {
                    // Gone between being found and being measured, so there is no rectangle left
                    // to give for it.
                }
            }

            return found;
        }
    }
}
