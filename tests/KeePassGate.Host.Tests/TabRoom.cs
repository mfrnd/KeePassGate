using System;
using System.Drawing;
using System.Windows.Forms;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests
{
    /// <summary>
    /// How much room one of KeePass's tabbed dialogs leaves this plugin's page, icon and all.
    /// </summary>
    /// <remarks>
    /// **Decorated by `TabPageInjector` itself**, because the icon is what changes the room: a tab
    /// strip shows pictures only once the whole control has an image list, and the strip is as
    /// tall as the pictures in it. The picture is the size KeePass would hand one over at the
    /// dialog's scaling, since its icons grow with the display, 16 pixels at 100 percent and 32 at
    /// 200. A list of sixteen at every scaling, or no list at all, left the icon's pixels out.
    ///
    /// **The smaller of two measurements**, in each direction. The injected page has no padding
    /// of its own, so it is laid out to the whole of the tab control's display area, which is the
    /// first. The second is KeePass's own first page as the dialog built it, which is what these
    /// fixtures have always held a page to: never shown, a dialog does not lay that page out again
    /// when the strip changes, so it does not see the icon, but its padding usually leaves it the
    /// smaller anyway. Measured on 2.61.1 it is smaller everywhere but on the group dialog at 100
    /// percent, which has pages with no padding and lost a pixel to the icon: 356 by 171 rather
    /// than the 356 by 172 that was asserted.
    /// </remarks>
    internal static class TabRoom
    {
        /// <summary>The size KeePass's icons are designed at, before any scaling.</summary>
        private const int DesignedIcon = 16;

        /// <summary>
        /// The line height KeePass's dialogs are designed for, as `AutoScaleDimensions` measures it,
        /// which is what tells how far a dialog built at a larger font was scaled.
        /// </summary>
        private const float DesignedLine = 13F;

        /// <summary>The room this plugin's page gets on the dialog, with its tab and icon added.</summary>
        public static Size Of(Form dialog, string tabControlName)
        {
            // The handle, so that the tab control has laid its pages out. Without it a page reports
            // the size it was designed at rather than the size it will have.
            var handle = dialog.Handle;
            Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero));

            var tabs = ControlTree.FindByName<TabControl>(dialog, tabControlName);
            Assert.That(tabs, Is.Not.Null, dialog.GetType().Name + " no longer names its tab control that.");
            Assert.That(tabs.TabPages.Count, Is.GreaterThan(0));

            var built = tabs.TabPages[0].DisplayRectangle.Size;
            var icon = (int)Math.Round(DesignedIcon * dialog.CurrentAutoScaleDimensions.Height / DesignedLine);

            var injector = new TabPageInjector(
                new EmptyPanels(), null, tabControlName, "KPG", "KpgRoomProbe", () => new Bitmap(icon, icon));
            Assert.That(injector.Attach(dialog), Is.True, "the tab could not be put on " + dialog.GetType().Name);

            try
            {
                Assert.That(tabs.ImageList, Is.Not.Null, "the injector put no picture on the strip");
                Assert.That(tabs.ImageList.ImageSize, Is.EqualTo(new Size(icon, icon)));

                var given = tabs.DisplayRectangle.Size;
                return new Size(Math.Min(built.Width, given.Width), Math.Min(built.Height, given.Height));
            }
            finally
            {
                injector.Detach(dialog);
            }
        }

        /// <summary>A page with nothing on it, since only the strip above it is being measured.</summary>
        private sealed class EmptyPanels : IGatePanelFactory
        {
            public Control CreatePanel(Form dialog)
            {
                return new Panel();
            }
        }
    }
}
