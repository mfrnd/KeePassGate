using System;
using System.Collections.Generic;
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
    /// The order of operations here is measured rather than chosen, and `FINDINGS.md`, under "Loading a plugin, and testing one",
    /// records why. Reading the accessibility tree while a menu is open dismisses the menu, and
    /// choosing from a menu through the tree blocks every other automation call until the dialog it
    /// opened is closed. So keys are discovered first, the menu is then driven from synthesized
    /// keystrokes with nothing watching, and results are read once nothing is modal.
    ///
    /// Discovery is what makes a menu appear to open twice, and it is remembered per item so that
    /// it happens once a session rather than once a call. `KeysFor` has the whole of it.
    /// </remarks>
    internal sealed class KeePassDriver
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// What Windows' own file dialog calls its file name box. The platform's identifier, not
        /// KeePass's and not ours, and the same since the dialog was redesigned for Vista.
        /// </summary>
        private const string FileNameBox = "1148";

        /// <summary>
        /// What KeePass names its main form, which WinForms reports to UI automation as the
        /// window's identifier. KeePass's own name, set by its designer.
        /// </summary>
        /// <remarks>
        /// **By identifier rather than by title**, because a title is a sentence for the operator
        /// and more than one window of this process carries the product's name in it: the box
        /// the plugin raises when the gate cannot take its port is titled `KeePassGate (KPG)` and
        /// sits on top of the main window, and the authority chooser's title names the product
        /// as well. Taken for the main window, either one was sent every menu key that followed,
        /// and the fixture failed later on a menu or a dialog rather than on what the box said.
        /// </remarks>
        private const string MainFormName = "MainForm";

        private readonly Process _process;
        private readonly AutomationElement _mainWindow;
        private readonly StringBuilder _diagnostics = new StringBuilder();

        // Access keys belong to the KeePass build rather than to the moment, so they are learned
        // once. See `KeysFor`, which is where the cost of learning them is explained.
        private readonly Dictionary<string, Keyboard.Stroke[]> _menuKeys =
            new Dictionary<string, Keyboard.Stroke[]>(StringComparer.Ordinal);

        private KeePassDriver(Process process, AutomationElement mainWindow)
        {
            _process = process;
            _mainWindow = mainWindow;
        }

        public string Diagnostics
        {
            get { return _diagnostics.ToString(); }
        }

        /// <summary>
        /// What KeePass's main window says in its title bar, which is where it marks a database
        /// holding changes nobody has saved.
        /// </summary>
        public string MainWindowTitle
        {
            get { return Uia.Name(_mainWindow); }
        }

        /// <summary>Starts KeePass and waits for the main window. Fails the test if it never comes.</summary>
        public static KeePassDriver Start(ThrowawayKeePass keePass)
        {
            var process = keePass.Start();

            var mainWindow = Uia.WaitForOrNull(() => WindowById(process, MainFormName), StartTimeout);

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
            return OpenFromMenu("File", "Database Settings", "Database Settings");
        }

        /// <summary>
        /// File, Synchronize, Synchronize with File, and another copy of the database named in the
        /// dialog that opens, which is how an operator merges two copies of one database.
        /// </summary>
        /// <remarks>
        /// **The dialog is the platform's rather than KeePass's**, so its file name box is found by
        /// the identifier Windows gives it, and the path is put there through the box's value
        /// pattern: an edit control reads its own window text, so what is written there is what
        /// the dialog opens. Enter is then its Open, pressed as a keystroke for the reason every
        /// menu here is chosen by one.
        ///
        /// **Answers once the dialog has gone**, which is when KeePass has the file and is merging
        /// it. The merge and the save it ends with run on KeePass's own thread, so whatever that
        /// save raises arrives after this returns, and a caller waits for it.
        /// </remarks>
        public void Synchronize(string path)
        {
            // What KeePass titles the dialog, which is the name of the command.
            const string title = "Synchronize";

            var dialog = OpenWith(title, KeysFor("File", "Synchronize", "Synchronize with File"));

            var box = Uia.WaitForOrNull(
                () => Uia.ById(dialog, ControlType.Edit, FileNameBox),
                TimeSpan.FromSeconds(10));
            if (box == null)
            {
                throw new TimeoutException(
                    "The Synchronize dialog shows no file name box. Boxes: " + Uia.Describe(dialog, ControlType.Edit));
            }

            object pattern;
            if (!box.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                throw new InvalidOperationException("The Synchronize dialog's file name box takes no text.");
            }

            ((ValuePattern)pattern).SetValue(path);

            Keyboard.Front(dialog);
            Keyboard.SendEnter();

            var closed = Uia.WaitForOrNull(
                () => NativeWindows.Find(_process, shown => shown.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) == null
                    ? path
                    : null,
                TimeSpan.FromSeconds(15));
            if (closed == null)
            {
                throw new TimeoutException(
                    "The Synchronize dialog was still open after Enter. Windows: " + NativeWindows.Titles(_process));
            }

            _diagnostics.AppendLine("synchronized with " + System.IO.Path.GetFileName(path));
        }

        /// <summary>Tools, and the plugin's own item, which opens the status window.</summary>
        /// <remarks>
        /// KeePass places a plugin's item under Tools itself, from `GetMenuItem`, so this is a
        /// menu like any other and is driven the same way: keys discovered once, then sent with
        /// nothing watching.
        /// </remarks>
        public AutomationElement OpenStatusWindow()
        {
            return OpenFromMenu("Tools", GateTab.MenuItemText, GateStatusForm.Caption);
        }

        /// <summary>Group, Edit Group, on whichever group the tree has selected.</summary>
        public AutomationElement OpenGroupDialog()
        {
            return OpenFromMenu("Group", "Edit Group", "Edit Group");
        }

        private AutomationElement OpenFromMenu(string menuName, string itemPrefix, string windowTitlePart)
        {
            return OpenWith(windowTitlePart, KeysFor(menuName, itemPrefix));
        }

        /// <summary>
        /// Sends the keys that reach a menu item, one after the other, and waits for the window the
        /// item opens.
        /// </summary>
        private AutomationElement OpenWith(string windowTitlePart, Keyboard.Stroke[] keys)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                Keyboard.Front(_mainWindow);
                _diagnostics.AppendLine("attempt " + attempt + ": KeePass in front");

                foreach (var key in keys)
                {
                    Keyboard.Send(key);
                }

                var dialog = Uia.WaitForOrNull(
                    () => Window(_process, title =>
                        title.IndexOf(windowTitlePart, StringComparison.OrdinalIgnoreCase) >= 0),
                    TimeSpan.FromSeconds(15));

                if (dialog != null)
                {
                    return dialog;
                }

                _diagnostics.AppendLine("  no dialog. Windows: " + NativeWindows.Titles(_process));
                Keyboard.SendEscape();
            }

            throw new TimeoutException(
                "The " + windowTitlePart + " dialog never opened." + Environment.NewLine + _diagnostics);
        }

        /// <summary>
        /// The access keys that reach a menu item, one for each menu on the way down to it,
        /// discovered once per item and remembered.
        /// </summary>
        /// <remarks>
        /// **This is why a menu appears to open twice**, and it opens twice for a reason rather than
        /// by accident. A menu item's access key is only in the accessibility tree while its menu is
        /// open, so the menu has to be opened to be read; and reading the tree while a menu is open
        /// is what dismisses it, which `FINDINGS.md` records under "Driving KeePass through the
        /// desktop". So the menu is opened through the tree to learn the key, closed, and then
        /// opened again by keystroke with nothing watching, which is the only order that both
        /// discovers and works.
        ///
        /// **An item inside a submenu is read a level at a time**, File then Synchronize then the
        /// item, because the items of a submenu are only in the tree while it is open too. Each
        /// menu on the way down is opened through the tree before the level below it is looked
        /// for, and all of them are closed again, innermost first, before anything is sent.
        ///
        /// What was wasteful was doing it on every call, since the keys belong to the KeePass build
        /// rather than to the moment. Remembered here, an item costs its double open once per
        /// session and every later call opens the menu exactly once, which is also what somebody
        /// watching the desktop expects to see.
        /// </remarks>
        private Keyboard.Stroke[] KeysFor(params string[] path)
        {
            var remembered = string.Join("\0", path);

            Keyboard.Stroke[] keys;
            if (_menuKeys.TryGetValue(remembered, out keys))
            {
                return keys;
            }

            var menuName = path[0];
            var menu = Uia.WaitFor(
                "the " + menuName + " menu",
                () => Uia.Descendant(_mainWindow, ControlType.MenuItem, name => Strip(name) == menuName));

            var found = new Keyboard.Stroke?[path.Length];
            found[0] = Keyboard.ParseAccessKey(AccessKeyOf(menu));
            Assert.That(found[0], Is.Not.Null, "The " + menuName + " menu reports no access key.");

            var opened = new List<AutomationElement>();
            var current = menu;
            for (var level = 1; level < path.Length; level++)
            {
                Uia.Expand(current);
                opened.Add(current);

                var itemPrefix = path[level];
                current = Uia.WaitFor(
                    "the " + itemPrefix + " menu item",
                    () => MenuItemStartingWith(itemPrefix));
                found[level] = Keyboard.ParseAccessKey(AccessKeyOf(current));
            }

            for (var level = opened.Count - 1; level >= 0; level--)
            {
                Uia.Collapse(opened[level]);
            }

            keys = new Keyboard.Stroke[path.Length];
            keys[0] = found[0].Value;
            for (var level = 1; level < path.Length; level++)
            {
                Assert.That(found[level], Is.Not.Null, "The " + path[level] + " item reports no access key.");
                keys[level] = found[level].Value;
            }

            _diagnostics.AppendLine("keys for " + string.Join(" / ", path) + ": " + string.Join(" then ", keys));
            _menuKeys[remembered] = keys;
            return keys;
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
            // By identifier rather than by name. A box reports its `AccessibleName` as its name, and
            // that is a phrase for somebody listening rather than a handle for a test; the
            // identifier is `Control.Name`, which is ours and does not change when the wording does.
            var box = Uia.WaitForOrNull(
                () => Uia.ById(dialog, ControlType.Edit, controlName),
                TimeSpan.FromSeconds(10));

            if (box == null)
            {
                _diagnostics.AppendLine("no box with the identifier '" + controlName + "'. Boxes: " +
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
        /// A window of this KeePass by the identifier its form carries, or null if none appears in
        /// time. The authority chooser arrives on its own, posted after whatever raised it.
        /// </summary>
        /// <remarks>
        /// By identifier rather than by title, because a title is a sentence for the operator: the
        /// chooser's names the product, and so does the main window's.
        /// </remarks>
        public AutomationElement WaitForWindowById(string automationId, TimeSpan timeout)
        {
            return Uia.WaitForOrNull(() => WindowById(_process, automationId), timeout);
        }

        /// <summary>Whether the window with this identifier has gone within the time given.</summary>
        public bool WaitForWindowToClose(string automationId, TimeSpan timeout)
        {
            return Uia.WaitForOrNull(
                () => WindowById(_process, automationId) == null ? automationId : null,
                timeout) != null;
        }

        /// <summary>
        /// Selects an authority in the chooser by the short fingerprint its row shows, as a click on
        /// the row does, and answers with the full fingerprint the dialog then describes.
        /// </summary>
        /// <remarks>
        /// **The list is found by its identifier alone**, because what it reports itself as is the
        /// provider's business: a `ListView` in details view is a `DataGrid`, with `Selection`,
        /// `Grid` and `Table` patterns, and looked for as a `List` it was not found at all. That was
        /// measured on this dialog, shown off screen and read by a .NET Framework client, after the
        /// first live run stopped here.
        ///
        /// The row is looked for as more than one control type for the same reason: under that
        /// provider a row is a `DataItem` named by its first column, and under another it is a
        /// `ListItem`. What comes back is read off the box below the list rather than trusted from
        /// the selection, because that box is what an operator reads before pressing Keep, and Keep
        /// keeps what it describes.
        /// </remarks>
        public string SelectAuthority(AutomationElement chooser, string shortFingerprint)
        {
            var list = Uia.WaitForOrNull(
                () => Uia.ById(chooser, GateTab.ChooserAuthorityListName),
                TimeSpan.FromSeconds(10));
            if (list == null)
            {
                throw new TimeoutException(
                    "The chooser shows no list of authorities. What it shows: " + Uia.Inventory(chooser));
            }

            AutomationElement row = null;
            foreach (var type in new[] { ControlType.DataItem, ControlType.ListItem })
            {
                row = Uia.Descendant(list, type, name => name == shortFingerprint);
                if (row != null)
                {
                    break;
                }
            }

            if (row == null)
            {
                throw new InvalidOperationException(
                    "The chooser lists no authority " + shortFingerprint + ". What the list holds: " +
                    Uia.Inventory(list));
            }

            object pattern;
            if (row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
            {
                ((SelectionItemPattern)pattern).Select();
                System.Threading.Thread.Sleep(400);
            }

            _diagnostics.AppendLine("selected the authority " + shortFingerprint);
            return ValueOf(chooser, GateTab.ChooserFingerprintBoxName);
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

        private static AutomationElement WindowById(Process process, string automationId)
        {
            foreach (var window in NativeWindows.Visible(process))
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

                if (string.Equals(Uia.Identifier(element), automationId, StringComparison.Ordinal))
                {
                    return element;
                }
            }

            return null;
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
