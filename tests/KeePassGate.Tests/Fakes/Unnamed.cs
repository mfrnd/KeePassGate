using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// The controls on a window that carry no Name, which CONVENTIONS.md requires of every control
    /// the plugin builds, so that the desktop tests can point at it from outside the process.
    /// </summary>
    /// <remarks>
    /// The parts WinForms builds inside a control of its own are not the plugin's, and are left
    /// out: the edit box and the arrows inside a `NumericUpDown`.
    /// </remarks>
    internal static class Unnamed
    {
        public static IReadOnlyList<string> On(Control root)
        {
            return ControlTree.Descendants(root)
                .Where(control => !(control.Parent is UpDownBase))
                .Where(control => string.IsNullOrEmpty(control.Name))
                .Select(control => control.GetType().Name + " \"" + control.Text + "\"")
                .ToList();
        }
    }
}
