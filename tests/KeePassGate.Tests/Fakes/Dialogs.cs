using System.Windows.Forms;
using KeePassGate.Ui;

namespace KeePassGate.Tests.Fakes
{
    /// <summary>
    /// A form that can be told to announce its own closing. A form that was never shown does not
    /// raise FormClosed from Close, so a test that needs the event raises it here instead of
    /// standing a window up on somebody's desktop.
    /// </summary>
    internal sealed class TestDialogForm : Form
    {
        public void RaiseClosed()
        {
            OnFormClosed(new FormClosedEventArgs(CloseReason.UserClosing));
        }
    }

    /// <summary>
    /// Stand-ins for the KeePass dialogs, shaped the way KeePass shapes them: a tab control
    /// several containers deep, carrying the name KeePass gives it.
    /// </summary>
    internal static class Dialogs
    {
        /// <summary>A dialog whose tab control is nested and named as KeePass names it.</summary>
        public static TestDialogForm WithNestedTabControl(params string[] existingTabs)
        {
            return WithTabControlNamed(KeePassContract.DatabaseSettingsTabControlName, existingTabs);
        }

        /// <summary>A dialog whose tab control carries some other name, as a renamed KeePass would.</summary>
        public static TestDialogForm WithTabControlNamed(string tabControlName, params string[] existingTabs)
        {
            var form = new TestDialogForm { Name = "SettingsLikeForm" };
            var outer = new Panel { Name = "m_pnlOuter" };
            var inner = new Panel { Name = "m_pnlInner" };
            var tabs = new TabControl { Name = tabControlName };

            foreach (var caption in existingTabs)
            {
                tabs.TabPages.Add(new TabPage(caption) { Name = caption });
            }

            inner.Controls.Add(tabs);
            outer.Controls.Add(inner);
            form.Controls.Add(outer);
            return form;
        }

        /// <summary>A dialog with no tab control at all, which must be refused rather than crash.</summary>
        public static TestDialogForm WithoutTabControl()
        {
            var form = new TestDialogForm { Name = "PlainForm" };
            form.Controls.Add(new Panel { Name = "m_pnlOuter" });
            return form;
        }

        public static TabControl TabsOf(Form form)
        {
            return ControlTree.FindFirst<TabControl>(form);
        }
    }
}
