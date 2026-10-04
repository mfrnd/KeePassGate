using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// The lifetime list on an approval dialog, which changes only while it is open.
    /// </summary>
    /// <remarks>
    /// A closed drop-down list takes letters and digits for a search and the arrows, Home, End and
    /// the page keys for a choice, so typing that landed on it changed what Approve issues with the
    /// list never opened: a '3' on "30 days (asked)" chose "365 days". Closed, it takes none of
    /// them. Alt and Down or F4 open it, and open it takes every key a list takes, which is a
    /// choice somebody is looking at.
    /// </remarks>
    internal sealed class SteadyChoice : ComboBox
    {
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e != null && !DroppedDown && !e.Alt && Moves(e.KeyCode))
            {
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e != null && !DroppedDown)
            {
                e.Handled = true;
                return;
            }

            base.OnKeyPress(e);
        }

        private static bool Moves(Keys key)
        {
            switch (key)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// The profile list on an approval dialog, whose checks change only by a click or by Space.
    /// </summary>
    /// <remarks>
    /// A list of check boxes that checks on click selects the first item starting with a letter
    /// typed on it and toggles that item as it does, so typing that landed on it unchecked what
    /// Approve grants, and the list has the focus when the dialog is first activated. Letters and
    /// digits are not taken; Space toggles the selected profile and the arrows move between them,
    /// which is somebody going through the list.
    /// </remarks>
    internal sealed class SteadyChecks : CheckedListBox
    {
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e != null && e.KeyChar != ' ')
            {
                e.Handled = true;
                return;
            }

            base.OnKeyPress(e);
        }
    }
}
