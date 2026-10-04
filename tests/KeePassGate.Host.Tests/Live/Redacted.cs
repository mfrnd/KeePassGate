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
        /// Paints over each of them, given the picture and where on screen it was taken from.
        /// </summary>
        public static void Over(Bitmap picture, Rectangle area, AutomationElement window)
        {
            if (picture == null || window == null)
            {
                return;
            }

            using (var brush = new SolidBrush(Bar))
            using (var graphics = Graphics.FromImage(picture))
            {
                foreach (var identifier in Volatile)
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
                }
            }

            return found;
        }
    }
}
