using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using KeePassGate.Ui;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// Drives a running KeePass the way a person would: menus by keyboard, everything read back
    /// through the accessibility tree.
    /// </summary>
    /// <remarks>
    /// The order of operations here is measured rather than chosen, and `FINDINGS.md` section 7
    /// records why. Reading the accessibility tree while a menu is open dismisses the menu, and
    /// choosing from a menu through the tree blocks every other automation call until the dialog it
    /// opened is closed. So keys are discovered first, the menu is then driven from synthesized
    /// keystrokes with nothing watching, and results are read once nothing is modal.
    /// </remarks>
    internal sealed class KeePassDriver
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

        private readonly Process _process;
        private readonly AutomationElement _mainWindow;
        private readonly StringBuilder _diagnostics = new StringBuilder();

        private KeePassDriver(Process process, AutomationElement mainWindow)
        {
            _process = process;
            _mainWindow = mainWindow;
        }

        public string Diagnostics
        {
            get { return _diagnostics.ToString(); }
        }

        /// <summary>Starts KeePass and waits for the main window. Fails the test if it never comes.</summary>
        public static KeePassDriver Start(ThrowawayKeePass keePass)
        {
            var process = keePass.Start();

            var mainWindow = Uia.WaitForOrNull(
                () => Window(process, title => title.IndexOf("KeePass", StringComparison.OrdinalIgnoreCase) >= 0),
                StartTimeout);

            if (mainWindow == null)
            {
                Assert.Fail(process.HasExited
                    ? "KeePass exited during start up with code " + process.ExitCode +
                      ". A KeePass that hands its command line to another instance does exactly this."
                    : "No KeePass main window after " + StartTimeout.TotalSeconds + "s. Windows were: " +
                      NativeWindows.Titles(process));
            }

            return new KeePassDriver(process, mainWindow);
        }

        /// <summary>Opens File, Database Settings and returns the dialog.</summary>
        public AutomationElement OpenDatabaseSettings()
        {
            var file = Uia.WaitFor(
                "the File menu",
                () => Uia.Descendant(_mainWindow, ControlType.MenuItem, name => Strip(name) == "File"));

            var openMenu = Keyboard.ParseAccessKey(AccessKeyOf(file));
            Assert.That(openMenu, Is.Not.Null, "The File menu reports no access key.");

            Uia.Expand(file);
            var settings = Uia.WaitFor(
                "the Database Settings menu item",
                () => MenuItemStartingWith("Database Settings"));
            var chooseItem = Keyboard.ParseAccessKey(AccessKeyOf(settings));
            Uia.Collapse(file);

            Assert.That(chooseItem, Is.Not.Null, "The Database Settings item reports no access key.");
            _diagnostics.AppendLine("keys: " + openMenu + " then " + chooseItem);

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                _diagnostics.AppendLine("attempt " + attempt + ": KeePass in front: " + Keyboard.BringToFront(_mainWindow));

                Keyboard.Send(openMenu.Value);
                Keyboard.Send(chooseItem.Value);

                var dialog = Uia.WaitForOrNull(
                    () => Window(_process, title =>
                        title.IndexOf("Database Settings", StringComparison.OrdinalIgnoreCase) >= 0),
                    TimeSpan.FromSeconds(15));

                if (dialog != null)
                {
                    return dialog;
                }

                _diagnostics.AppendLine("  no dialog. Windows: " + NativeWindows.Titles(_process));
                Keyboard.SendEscape();
            }

            throw new TimeoutException(
                "The database settings dialog never opened." + Environment.NewLine + _diagnostics);
        }

        /// <summary>Brings the KPG tab to the front, which is what puts its contents in the tree.</summary>
        public AutomationElement SelectGateTab(AutomationElement dialog)
        {
            return SelectTab(dialog, GateTab.Text);
        }

        /// <summary>
        /// Brings one of the KPG tab's own pages to the front, which is what an operator does
        /// before touching anything on it. A control on a page behind another is not there as far
        /// as UI automation is concerned.
        /// </summary>
        public AutomationElement SelectPage(AutomationElement dialog, string text)
        {
            return SelectTab(dialog, text);
        }

        private static AutomationElement SelectTab(AutomationElement dialog, string text)
        {
            var tab = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.TabItem, name => name == text),
                TimeSpan.FromSeconds(10));

            if (tab == null)
            {
                return null;
            }

            object pattern;
            if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                ((SelectionItemPattern)pattern).Select();
                System.Threading.Thread.Sleep(500);
            }

            return tab;
        }

        public string TextStartingWith(AutomationElement dialog, string prefix)
        {
            var element = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.Text, name => name.StartsWith(prefix, StringComparison.Ordinal)),
                TimeSpan.FromSeconds(5));

            return element == null ? null : Uia.Name(element);
        }

        /// <summary>
        /// What the tab shows beside a name, for the facts it lays out as a table.
        /// </summary>
        /// <remarks>
        /// Found by matching the value rather than by walking from the name, because the
        /// accessibility tree gives no relation between two labels that happen to share a row. The
        /// shape is what identifies it, which is enough here: nothing else on this tab looks like a
        /// fingerprint or like a date.
        /// </remarks>
        public string TextMatching(AutomationElement dialog, string pattern)
        {
            var expression = new System.Text.RegularExpressions.Regex(pattern);

            var element = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.Text, name => expression.IsMatch(name)),
                TimeSpan.FromSeconds(5));

            return element == null ? null : Uia.Name(element);
        }

        /// <summary>
        /// The contents of a text box, found by the name the plugin gives it. A label reports its
        /// text as its name and a box does not, so this reads the value pattern instead.
        /// </summary>
        public string ValueOf(AutomationElement dialog, string controlName)
        {
            var box = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.Edit, name => name == controlName),
                TimeSpan.FromSeconds(10));

            if (box == null)
            {
                _diagnostics.AppendLine("no box called '" + controlName + "'. Boxes: " +
                                        Uia.Describe(dialog, ControlType.Edit));
                return null;
            }

            object pattern;
            return box.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)
                ? ((ValuePattern)pattern).Current.Value
                : null;
        }

        /// <summary>
        /// A window of this KeePass whose title says what it is, or null if none appears in time.
        /// The pairing dialog arrives on its own while a connection thread waits for it.
        /// </summary>
        public AutomationElement WaitForWindow(string titlePart, TimeSpan timeout)
        {
            return Uia.WaitForOrNull(
                () => Window(_process, title => title.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0),
                timeout);
        }

        /// <summary>
        /// Presses a button by its caption. Safe through the accessibility tree because these
        /// buttons open no modal dialog of their own.
        /// </summary>
        public bool PressButton(AutomationElement dialog, string caption)
        {
            var button = Uia.WaitForOrNull(
                () => Uia.Descendant(dialog, ControlType.Button, name => Strip(name) == caption),
                TimeSpan.FromSeconds(10));

            if (button == null)
            {
                _diagnostics.AppendLine("no button called '" + caption + "'. Buttons: " +
                                        Uia.Describe(dialog, ControlType.Button));
                return false;
            }

            object pattern;
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                return false;
            }

            ((InvokePattern)pattern).Invoke();
            System.Threading.Thread.Sleep(1000);
            return true;
        }

        /// <summary>
        /// Escape rather than OK. KeePass writes back a snapshot of custom data on the group
        /// dialog's OK, and while the settings dialog carries no such snapshot, a test that closes
        /// dialogs with Cancel can never be the reason something was lost.
        /// </summary>
        public void CloseDialog(AutomationElement dialog)
        {
            Keyboard.Front(dialog);
            Keyboard.SendEscape();
            System.Threading.Thread.Sleep(500);
        }

        /// <summary>
        /// Closes a dialog with its OK, which is what commits anything the tab was holding.
        /// </summary>
        /// <remarks>
        /// The difference between this and <see cref="CloseDialog"/> is the whole of what the
        /// KPG tab now means by a change: Escape is Cancel, and Cancel throws it away.
        /// </remarks>
        public bool AcceptDialog(AutomationElement dialog)
        {
            Keyboard.BringToFront(dialog);

            var pressed = PressButton(dialog, "OK");
            System.Threading.Thread.Sleep(900);
            return pressed;
        }

        /// <summary>Saves the database with the shortcut KeePass gives it.</summary>
        public void Save()
        {
            Keyboard.Front(_mainWindow);
            Keyboard.SendControl('s');
            System.Threading.Thread.Sleep(1500);
        }

        /// <summary>Locks the workspace with the shortcut KeePass gives it, as an operator does.</summary>
        public void LockWorkspace()
        {
            Keyboard.Front(_mainWindow);
            Keyboard.SendControl('l');
            System.Threading.Thread.Sleep(1000);
        }

        /// <summary>Closes KeePass and waits for it to go. True when it exited by itself.</summary>
        public bool ShutDown()
        {
            _process.CloseMainWindow();
            return _process.WaitForExit((int)ShutdownTimeout.TotalMilliseconds);
        }

        public int ExitCode
        {
            get { return _process.HasExited ? _process.ExitCode : int.MinValue; }
        }

        private static AutomationElement Window(Process process, Func<string, bool> titleMatches)
        {
            var window = NativeWindows.Find(process, titleMatches);
            if (window == null)
            {
                return null;
            }

            try
            {
                return AutomationElement.FromHandle(window.Handle);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        private AutomationElement MenuItemStartingWith(string prefix)
        {
            var underMainWindow = Uia.Descendant(_mainWindow, ControlType.MenuItem,
                name => Strip(name).StartsWith(prefix, StringComparison.Ordinal));
            if (underMainWindow != null)
            {
                return underMainWindow;
            }

            foreach (var window in NativeWindows.Visible(_process))
            {
                AutomationElement element;
                try
                {
                    element = AutomationElement.FromHandle(window.Handle);
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                var item = Uia.Descendant(element, ControlType.MenuItem,
                    name => Strip(name).StartsWith(prefix, StringComparison.Ordinal));
                if (item != null)
                {
                    return item;
                }
            }

            return null;
        }

        private static string AccessKeyOf(AutomationElement element)
        {
            try
            {
                return element.Current.AccessKey;
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        private static string Strip(string menuText)
        {
            return (menuText ?? string.Empty).Replace("&", string.Empty).Trim();
        }
    }
}
