using System;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// The small part of UI automation these tests need. Everything polls rather than sleeps, so
    /// a slow machine costs time and a broken one costs a timeout rather than a flake.
    /// </summary>
    internal static class Uia
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        public static T WaitFor<T>(string what, Func<T> attempt, TimeSpan? timeout = null) where T : class
        {
            var result = WaitForOrNull(attempt, timeout);
            if (result == null)
            {
                throw new TimeoutException(
                    "Gave up after " + (timeout ?? DefaultTimeout).TotalSeconds + "s waiting for " + what + ".");
            }

            return result;
        }

        public static T WaitForOrNull<T>(Func<T> attempt, TimeSpan? timeout = null) where T : class
        {
            var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
            while (true)
            {
                try
                {
                    var result = attempt();
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (ElementNotAvailableException)
                {
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                Thread.Sleep(200);
            }
        }

        public static AutomationElement Descendant(AutomationElement root, ControlType type, Func<string, bool> nameMatches)
        {
            if (root == null)
            {
                return null;
            }

            var candidates = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, type));

            return candidates
                .Cast<AutomationElement>()
                .FirstOrDefault(e => nameMatches(Name(e)));
        }

        /// <summary>
        /// A descendant by the name the plugin gave the control, which reaches UI automation as its
        /// identifier rather than as its name. What a control is called is ours; what it reports as
        /// a name is WinForms's business and differs by control type.
        /// </summary>
        public static AutomationElement ById(AutomationElement root, ControlType type, string id)
        {
            if (root == null)
            {
                return null;
            }

            return root
                .FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, type))
                .Cast<AutomationElement>()
                .FirstOrDefault(e => string.Equals(Identifier(e), id, StringComparison.Ordinal));
        }

        public static string Identifier(AutomationElement element)
        {
            try
            {
                return element.Current.AutomationId ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                return string.Empty;
            }
        }

        public static string Name(AutomationElement element)
        {
            try
            {
                return element.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Opens a menu through the accessibility tree, which returns at once because a menu is
        /// not modal. This is for reading what is inside a menu, never for choosing from it.
        /// </summary>
        public static void Expand(AutomationElement element)
        {
            object pattern;
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
            {
                ((ExpandCollapsePattern)pattern).Expand();
            }
        }

        public static void Collapse(AutomationElement element)
        {
            object pattern;
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern))
            {
                ((ExpandCollapsePattern)pattern).Collapse();
            }
        }

        /// <summary>
        /// The captions on a dialog's own tab strip, in order, and nothing from a tab strip inside
        /// one of its pages.
        /// </summary>
        /// <remarks>
        /// Descendants would answer with both, since the KPG tab has pages of its own, and a test
        /// asking where KeePass's tabs end would be told about pages that are not on that strip at
        /// all. The outer strip is the first one found, because it contains the other.
        /// </remarks>
        public static string DescribeTabs(AutomationElement dialog)
        {
            if (dialog == null)
            {
                return "(no window)";
            }

            var strip = dialog.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));

            if (strip == null)
            {
                return "(no tabs)";
            }

            var names = strip
                .FindAll(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem))
                .Cast<AutomationElement>()
                .Select(Name)
                .Where(n => !string.IsNullOrEmpty(n));

            return string.Join(", ", names);
        }

        /// <summary>Everything visible, for a failure message that says what was actually there.</summary>
        public static string Describe(AutomationElement root, ControlType type)
        {
            if (root == null)
            {
                return "(no window)";
            }

            var names = root
                .FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, type))
                .Cast<AutomationElement>()
                .Select(Name)
                .Where(n => !string.IsNullOrEmpty(n));

            return string.Join(", ", names);
        }
    }
}
