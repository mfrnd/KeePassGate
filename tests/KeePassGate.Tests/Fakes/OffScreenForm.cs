using System.Drawing;
using System.Windows.Forms;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>A real window, off where nobody sees it, and a way to let it finish.</summary>
    /// <remarks>
    /// The editor's pickers are dropdowns, and a dropdown that has never been shown behaves
    /// differently from one that has: a button on a form nobody drew does nothing when it is
    /// pressed, and a `ToolStripDropDown` posts work it only does when a message loop runs. So the
    /// tests about it use a shown window and pump it, and both halves of that were copied between
    /// two fixtures before they lived here.
    /// </remarks>
    public static class OffScreenForm
    {
        /// <summary>A window that is real and shown, and far enough away to bother nobody.</summary>
        public static Form New()
        {
            return new Form
            {
                Width = 640,
                Height = 480,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000)
            };
        }

        /// <summary>
        /// Runs the message loop for long enough that whatever was posted has happened.
        /// </summary>
        /// <remarks>
        /// Several passes rather than one, because the panel defers its own teardown with
        /// `BeginInvoke` and that work can post more of its own: disposing a dropdown inside the
        /// handler that closed it is what takes KeePass down, so it is deliberately not done in one
        /// go and cannot be waited for in one either.
        /// </remarks>
        public static void Pump()
        {
            for (var i = 0; i < 8; i++)
            {
                Application.DoEvents();
            }
        }
    }
}
