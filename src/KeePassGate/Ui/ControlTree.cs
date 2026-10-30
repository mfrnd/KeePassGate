using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace KeePassGate.Ui
{
    /// <summary>
    /// Searching somebody else's form for a control. KeePass builds its dialogs in generated
    /// designer code, so a plugin either reflects over private fields or walks the control tree.
    /// Walking is preferred here: it needs no reflection permissions and it degrades to "not
    /// found" rather than to an exception when a field is renamed.
    /// </summary>
    public static class ControlTree
    {
        /// <summary>
        /// Depth first, parent before children, in child index order. The order is part of the
        /// contract because <see cref="FindFirst{T}"/> is used as a fallback and has to be
        /// predictable.
        /// </summary>
        public static IEnumerable<Control> Descendants(Control root)
        {
            if (root == null)
            {
                yield break;
            }

            var pending = new Stack<Control>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!ReferenceEquals(current, root))
                {
                    yield return current;
                }

                var children = current.Controls;
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    pending.Push(children[i]);
                }
            }
        }

        /// <summary>The first descendant of the requested type carrying the requested name.</summary>
        public static T FindByName<T>(Control root, string name) where T : Control
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            return Descendants(root).OfType<T>().FirstOrDefault(
                typed => string.Equals(typed.Name, name, StringComparison.Ordinal));
        }

        /// <summary>The first descendant of the requested type, whatever it is called.</summary>
        public static T FindFirst<T>(Control root) where T : Control
        {
            return Descendants(root).OfType<T>().FirstOrDefault();
        }
    }
}
