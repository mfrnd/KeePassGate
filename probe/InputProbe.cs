using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

// Drives a KeePass started on the same desktop as this process with nothing but messages posted or
// sent to its windows and UI automation, and records, route by route, what reached KeePass. Meant
// for a desktop that is not the input desktop, and run on the visible desktop as well for comparison.
//   InputProbe.exe <KeePass.exe> <database> <password> <output directory>
public static class InputProbe
{
    const uint WM_CLOSE = 0x0010, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102,
        WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_SYSCHAR = 0x0106, WM_GETTEXT = 0x000D,
        WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B, BM_CLICK = 0x00F5;
    const byte VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_MENU = 0x12,
        VK_LMENU = 0xA4, VK_SHIFT = 0x10, VK_LSHIFT = 0xA0, VK_F10 = 0x79;

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct GUITHREADINFO
    {
        public int cbSize; public uint flags; public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret; public RECT rcCaret;
    }
    delegate bool EnumProc(IntPtr hwnd, IntPtr l);

    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, StringBuilder l);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern bool GetKeyboardState(byte[] keys);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetKeyboardState(byte[] keys);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetActiveWindow(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr GetMenu(IntPtr h);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr h, uint id, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object acc);

    static string outDir;
    static StreamWriter report;
    static Process keePass;
    static uint uiThread;
    static IntPtr main;

    static void Note(string text)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + text;
        Console.WriteLine(line);
        report.WriteLine(line);
        report.Flush();
    }

    static void Result(string id, string route, string outcome)
    {
        Note("RESULT | " + id + " | " + route + " | " + outcome);
    }

    [MTAThread]
    public static int Main(string[] args)
    {
        outDir = args[3];
        Directory.CreateDirectory(outDir);
        report = new StreamWriter(Path.Combine(outDir, "input-probe.txt"), false, new UTF8Encoding(false));
        try
        {
            Run(args[0], args[1], args[2]);
            return 0;
        }
        catch (Exception e)
        {
            Note("probe stopped: " + e);
            return 1;
        }
        finally
        {
            ShutDown();
            report.Dispose();
        }
    }

    static void Run(string exe, string database, string password)
    {
        Note("probe thread desktop: " + DesktopName());
        var info = new ProcessStartInfo(exe, "\"" + database + "\" -pw:" + password) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
        info.EnvironmentVariables["KEEPASSGATE_LOG_FILE"] = Path.Combine(outDir, "plugin.log");
        info.EnvironmentVariables["KEEPASSGATE_AUDIT_FILE"] = Path.Combine(outDir, "audit.jsonl");
        info.EnvironmentVariables["KEEPASSGATE_PORT"] = FreePort().ToString();
        keePass = Process.Start(info);
        main = WaitFor(() => TopWindows().FirstOrDefault(w => Title(w).Contains("KeePass") && Class(w).StartsWith("WindowsForms10.Window")), 60);
        if (main == IntPtr.Zero) throw new Exception("no main window; windows: " + Describe(TopWindows()));
        uint pid; uiThread = GetWindowThreadProcessId(main, out pid);
        Thread.Sleep(2500);
        Note("main window " + Hex(main) + " '" + Title(main) + "', Win32 menu " + Hex(GetMenu(main)));
        Note("A0 after start: " + Gui() + "; GetForegroundWindow " + Hex(GetForegroundWindow()));

        var root = AutomationElement.FromHandle(main);
        var list = ById(root, "m_lvEntries");
        var tree = ById(root, "m_tvGroups");
        var listHwnd = Hwnd(list);
        Note("list " + Hex(listHwnd) + ", tree " + Hex(Hwnd(tree)));

        // Activation and focus, which the drivers' check reads as the foreground.
        Result("A1", "SetForegroundWindow(main) from another process", "returned " + SetForegroundWindow(main) + "; " + Gui() + "; foreground " + Hex(GetForegroundWindow()));
        Result("A2", "AttachThreadInput, SetActiveWindow(main), SetFocus(list)", Attached(() =>
            "SetActiveWindow " + Hex(SetActiveWindow(main)) + " error " + Marshal.GetLastWin32Error() + ", SetFocus " + Hex(SetFocus(listHwnd)) + " error " + Marshal.GetLastWin32Error()) + "; " + Gui());
        Result("A3", "UI automation SetFocus on the entry list", Try(() => { list.SetFocus(); return "no exception"; }) + "; " + Gui());

        // Text and Enter, posted.
        // The quick search box is a ToolStripComboBox: an item of the toolbar, which has no window,
        // hosting a combo box that does, with an edit inside it.
        var combo = Children(main, null).FirstOrDefault(h => Class(h).IndexOf("COMBOBOX", StringComparison.OrdinalIgnoreCase) >= 0);
        var quickEdit = Children(combo, "Edit").FirstOrDefault();
        Note("quick search combo " + Hex(combo) + " " + Class(combo) + ", edit " + Hex(quickEdit));
        int before = Rows(list);
        foreach (var c in "Beta") PostMessage(quickEdit, WM_CHAR, (IntPtr)c, (IntPtr)1);
        Thread.Sleep(400);
        Result("T1", "WM_CHAR posted to the quick search box", "box reads '" + Text(quickEdit) + "'");
        PostKey(quickEdit, VK_RETURN);
        Thread.Sleep(1200);
        Result("T2", "Enter posted to the quick search box", "list rows " + before + " -> " + Rows(list));

        // A shortcut with a modifier: Ctrl+I opens Add Entry.
        SelectFirst(tree);
        Thread.Sleep(500);
        PostKey(listHwnd, (byte)'I');
        var add = WaitWindow("Add Entry", 3);
        Result("K1", "Ctrl+I posted, Ctrl not held", add == IntPtr.Zero ? "no dialog" : "dialog opened");
        if (add != IntPtr.Zero) { CloseWithEscape(add); }
        add = OpenAddEntry(listHwnd);
        Result("K2", "Ctrl+I posted with Ctrl set by AttachThreadInput and SetKeyboardState", add == IntPtr.Zero ? "no dialog" : "dialog opened; " + Gui());
        if (add != IntPtr.Zero)
        {
            Capture("R2-add-entry", add);
            var dialog = AutomationElement.FromHandle(add);
            var title = Hwnd(ById(dialog, "m_tbTitle"));
            foreach (var c in "Posted") PostMessage(title, WM_CHAR, (IntPtr)c, (IntPtr)1);
            Thread.Sleep(400);
            Result("D1", "WM_CHAR posted to the title box", "box reads '" + Text(title) + "'");
            PostKey(title, VK_RETURN);
            var gone = WaitGone(add, 4);
            Thread.Sleep(600);
            Result("D2", "Enter posted to the title box (the dialog's accept button)", (gone ? "dialog closed" : "dialog still open") + "; entry 'Posted' listed: " + Listed(list, "Posted"));
            if (!gone) CloseWithEscape(add);
        }

        add = OpenAddEntry(listHwnd);
        if (add != IntPtr.Zero)
        {
            var g = GuiInfo(); var focus = g.hwndFocus != IntPtr.Zero ? g.hwndFocus : add;
            PostKey(focus, VK_ESCAPE);
            Result("K3", "Escape posted to the dialog's focused control", WaitGone(add, 4) ? "dialog closed" : "dialog still open");
        }

        add = OpenAddEntry(listHwnd);
        if (add != IntPtr.Zero)
        {
            var cancel = Hwnd(ById(AutomationElement.FromHandle(add), "m_btnCancel"));
            IntPtr r; var sent = SendMessageTimeout(cancel, BM_CLICK, IntPtr.Zero, IntPtr.Zero, 0x2, 5000, out r);
            Result("B1", "BM_CLICK sent to Cancel", (sent != IntPtr.Zero ? "sent" : "timed out") + "; " + (WaitGone(add, 4) ? "dialog closed" : "dialog still open"));
            if (IsWindow(add) && IsWindowVisible(add)) CloseWithEscape(add);
        }

        add = OpenAddEntry(listHwnd);
        if (add != IntPtr.Zero)
        {
            var cancelEl = ById(AutomationElement.FromHandle(add), "m_btnCancel");
            PostClick(Hwnd(cancelEl), cancelEl, WM_LBUTTONDOWN, WM_LBUTTONUP);
            Result("B2", "WM_LBUTTONDOWN and WM_LBUTTONUP posted to Cancel", WaitGone(add, 4) ? "dialog closed" : "dialog still open");
            if (IsWindow(add) && IsWindowVisible(add)) CloseWithEscape(add);
        }

        add = OpenAddEntry(listHwnd);
        if (add != IntPtr.Zero)
        {
            var cancelEl = ById(AutomationElement.FromHandle(add), "m_btnCancel");
            Result("B3", "UI automation Invoke on Cancel", Try(() => { ((InvokePattern)cancelEl.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); return "returned"; }) + "; " + (WaitGone(add, 4) ? "dialog closed" : "dialog still open"));
            if (IsWindow(add) && IsWindowVisible(add)) CloseWithEscape(add);
        }

        add = OpenAddEntry(listHwnd);
        if (add != IntPtr.Zero)
        {
            var cancel = Hwnd(ById(AutomationElement.FromHandle(add), "m_btnCancel"));
            Result("B4", "MSAA accDoDefaultAction on Cancel (LegacyIAccessible default action)", Try(() =>
            {
                var iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71"); object o;
                AccessibleObjectFromWindow(cancel, 0xFFFFFFFC, ref iid, out o);
                ((Accessibility.IAccessible)o).accDoDefaultAction(0);
                return "returned";
            }) + "; " + (WaitGone(add, 4) ? "dialog closed" : "dialog still open"));
            if (IsWindow(add) && IsWindowVisible(add)) CloseWithEscape(add);
        }

        add = OpenAddEntry(listHwnd);
        if (add != IntPtr.Zero)
        {
            var dialog = AutomationElement.FromHandle(add);
            var title = ById(dialog, "m_tbTitle");
            Result("V1", "UI automation ValuePattern.SetValue on the title box", Try(() => { ((ValuePattern)title.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Automated"); return "box reads '" + Text(Hwnd(title)) + "'"; }));
            var expires = ById(dialog, "m_cbExpires");
            Result("V2", "UI automation TogglePattern.Toggle on the Expires box", Try(() => { var t = (TogglePattern)expires.GetCurrentPattern(TogglePattern.Pattern); var was = t.Current.ToggleState; t.Toggle(); Thread.Sleep(300); return was + " -> " + t.Current.ToggleState; }));
            var ok = ById(dialog, "m_btnOK");
            Result("V3", "UI automation Invoke on OK", Try(() => { ((InvokePattern)ok.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); return "returned"; }) + "; " + (WaitGone(add, 4) ? "dialog closed" : "dialog still open") + "; entry 'Automated' listed: " + Listed(list, "Automated"));
            if (IsWindow(add) && IsWindowVisible(add)) CloseWithEscape(add);
        }

        // Save with Ctrl+S, which the drivers press.
        Thread.Sleep(500);
        var dirty = Title(main);
        PostKey(listHwnd, (byte)'S');
        Thread.Sleep(2000);
        Result("S1", "Ctrl+S posted, Ctrl not held", "title '" + dirty + "' -> '" + Title(main) + "'");
        WithKeys(new[] { VK_CONTROL, VK_LCONTROL }, () => PostKey(listHwnd, (byte)'S'));
        Thread.Sleep(2500);
        Result("S2", "Ctrl+S posted with Ctrl set", "title -> '" + Title(main) + "'");

        // Menus: KeePass's are ToolStrip menus, so no Win32 menu and no WM_COMMAND.
        var menu = ById(root, "m_menuMain");
        var file = menu.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
        var fileKey = file.Current.AccessKey;
        var fileRect = file.Current.BoundingRectangle;
        Try(() => { ((ExpandCollapsePattern)file.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand(); return ""; });
        Thread.Sleep(600);
        var settingsItem = AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.ProcessIdProperty, keePass.Id),
            new PropertyCondition(AutomationElement.NameProperty, "Database Settings...")));
        if (settingsItem == null) settingsItem = FindMenuItem("Database Settings");
        var itemKey = settingsItem == null ? "" : settingsItem.Current.AccessKey;
        var itemRect = settingsItem == null ? System.Windows.Rect.Empty : settingsItem.Current.BoundingRectangle;
        Note("File access key '" + fileKey + "', Database Settings access key '" + itemKey + "', rect " + itemRect);
        Try(() => { ((ExpandCollapsePattern)file.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse(); return ""; });
        Thread.Sleep(600);
        var fileChar = Last(fileKey); var itemChar = Last(itemKey);
        int popupsBefore = Popups().Count;

        PostAlt(main, fileChar, false);
        Thread.Sleep(800);
        Result("M1", "Alt+" + fileChar + " posted as WM_SYSKEYDOWN, WM_SYSCHAR, WM_SYSKEYUP, Alt not held", "menu popups " + popupsBefore + " -> " + Popups().Count);
        PostKey(main, VK_ESCAPE); PostKey(main, VK_ESCAPE); Thread.Sleep(500);

        PostAlt(main, fileChar, true);
        Thread.Sleep(800);
        var opened = Popups().Count;
        Result("M2", "Alt+" + fileChar + " posted with Alt set", "menu popups " + popupsBefore + " -> " + opened);
        PostKey(main, (byte)char.ToUpperInvariant(itemChar));
        var settings = WaitWindow("Database Settings", 6);
        Result("M3", "then '" + itemChar + "' posted for the item", settings == IntPtr.Zero ? "no dialog; popups " + Popups().Count : "Database Settings opened");
        if (settings != IntPtr.Zero)
        {
            Capture("R3-database-settings", settings);
            CloseByButton(settings);
        }
        else { PostKey(main, VK_ESCAPE); PostKey(main, VK_ESCAPE); Thread.Sleep(500); }

        // The same item chosen through the tree, on a thread of its own, with a second call made
        // while the first is pending.
        Try(() => { ((ExpandCollapsePattern)file.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand(); return ""; });
        Thread.Sleep(600);
        var item = FindMenuItem("Database Settings");
        string invoked = "pending";
        var worker = new Thread(() => { invoked = Try(() => { ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); return "returned"; }); });
        worker.Start();
        settings = WaitWindow("Database Settings", 6);
        var started = DateTime.Now;
        var second = Try(() => { var n = ById(root, "m_lvEntries"); return n == null ? "not found" : "found"; });
        var secondTook = (DateTime.Now - started).TotalSeconds;
        Result("M4", "UI automation Expand on File, Invoke on the item, on a thread of its own", (settings == IntPtr.Zero ? "no dialog" : "dialog opened") + "; Invoke " + invoked + " while the dialog is open; a second automation call " + second + " after " + secondTook.ToString("0.0") + " s");
        if (settings != IntPtr.Zero) CloseByButton(settings);
        worker.Join(20000);
        Result("M4b", "the pending Invoke after the dialog closed", invoked);

        // A click posted on the menu bar's own window, at the item's place.
        var menuHwnd = Hwnd(menu);
        PostClickAt(menuHwnd, fileRect, WM_LBUTTONDOWN, WM_LBUTTONUP);
        Thread.Sleep(800);
        var popups = Popups();
        Result("M5", "a click posted to the menu bar at File", "menu popups " + popupsBefore + " -> " + popups.Count);
        if (popups.Count > popupsBefore && !itemRect.IsEmpty)
        {
            var drop = popups.Last();
            PostClickAt(drop, itemRect, WM_LBUTTONDOWN, WM_LBUTTONUP);
            settings = WaitWindow("Database Settings", 6);
            Result("M6", "then a click posted to the open menu at the item", settings == IntPtr.Zero ? "no dialog" : "Database Settings opened");
            if (settings != IntPtr.Zero) CloseByButton(settings); else { PostKey(main, VK_ESCAPE); PostKey(main, VK_ESCAPE); }
        }
        Thread.Sleep(500);

        // Selection in the group tree and the entry list.
        var nodes = tree.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)).Cast<AutomationElement>().ToList();
        var probeGroup = nodes.FirstOrDefault(n => n.Current.Name == "Probe group");
        var rootGroup = nodes.FirstOrDefault();
        Result("L1", "UI automation SelectionItem.Select on a group", Try(() => { ((SelectionItemPattern)probeGroup.GetCurrentPattern(SelectionItemPattern.Pattern)).Select(); Thread.Sleep(500); return "selected " + Selected(probeGroup); }));
        PostClickAt(Hwnd(tree), rootGroup.Current.BoundingRectangle, WM_LBUTTONDOWN, WM_LBUTTONUP);
        Thread.Sleep(800);
        Result("L2", "a click posted to the tree at the root group", "root selected " + Selected(rootGroup) + ", Probe group selected " + Selected(probeGroup));
        var rows = RowElements(list);
        if (rows.Count > 1)
        {
            PostClickAt(listHwnd, rows[1].Current.BoundingRectangle, WM_LBUTTONDOWN, WM_LBUTTONUP);
            Thread.Sleep(800);
            Result("L3", "a click posted to the list at its second row", "second row selected " + Selected(rows[1]) + ", first " + Selected(rows[0]));
            Result("L4", "UI automation Select on the first row", Try(() => { ((SelectionItemPattern)rows[0].GetCurrentPattern(SelectionItemPattern.Pattern)).Select(); Thread.Sleep(400); return "first selected " + Selected(rows[0]) + ", second " + Selected(rows[1]); }));
        }

        // Context menus.
        popupsBefore = Popups().Count;
        PostMessage(listHwnd, WM_CONTEXTMENU, listHwnd, (IntPtr)(-1));
        Thread.Sleep(900);
        Result("C1", "WM_CONTEXTMENU posted to the list, as from the keyboard", "menu popups " + popupsBefore + " -> " + Popups().Count);
        DismissPopups(popupsBefore);
        rows = RowElements(list);
        if (rows.Count > 0)
        {
            PostClickAt(listHwnd, rows[0].Current.BoundingRectangle, WM_RBUTTONDOWN, WM_RBUTTONUP);
            Thread.Sleep(900);
            Result("C2", "WM_RBUTTONDOWN and WM_RBUTTONUP posted to the list at a row", "menu popups " + popupsBefore + " -> " + Popups().Count);
            DismissPopups(popupsBefore);
        }
        WithKeys(new[] { VK_SHIFT, VK_LSHIFT }, () => PostKey(listHwnd, VK_F10));
        Thread.Sleep(900);
        var shiftPopups = Popups();
        Result("C3", "Shift+F10 posted with Shift set", "menu popups " + popupsBefore + " -> " + shiftPopups.Count);
        if (shiftPopups.Count > popupsBefore)
        {
            PostKey(shiftPopups.Last(), VK_ESCAPE);
            Thread.Sleep(600);
            Result("C4", "Escape posted to the open context menu", "menu popups -> " + Popups().Count);
        }
        DismissPopups(popupsBefore);

        // A tooltip raised by hovering, posted.
        var toolbar = ById(root, "m_toolMain");
        var button = toolbar.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>().FirstOrDefault(b => !b.Current.BoundingRectangle.IsEmpty);
        int tipsBefore = Tooltips().Count;
        for (int i = 0; i < 6; i++) { PostMoveAt(Hwnd(toolbar), button.Current.BoundingRectangle, i); Thread.Sleep(150); }
        Thread.Sleep(2500);
        Result("H1", "WM_MOUSEMOVE posted over a toolbar button ('" + button.Current.Name + "')", "visible tooltips " + tipsBefore + " -> " + Tooltips().Count);
        PostMoveAt(Hwnd(toolbar), button.Current.BoundingRectangle, 0);
        var hover = button.Current.BoundingRectangle; var hp = new POINT { X = (int)(hover.Left + hover.Width / 2), Y = (int)(hover.Top + hover.Height / 2) };
        ScreenToClient(Hwnd(toolbar), ref hp);
        PostMessage(Hwnd(toolbar), 0x02A1, IntPtr.Zero, Lparam(hp.X, hp.Y));
        Thread.Sleep(2500);
        Result("H2", "then WM_MOUSEHOVER posted to the toolbar at the button", "visible tooltips -> " + Tooltips().Count);
        rows = RowElements(list);
        if (rows.Count > 0) { for (int i = 0; i < 6; i++) { PostMoveAt(listHwnd, rows[0].Current.BoundingRectangle, i); Thread.Sleep(150); } }
        Thread.Sleep(2500);
        Result("H3", "WM_MOUSEMOVE posted over the first entry row", "visible tooltips -> " + Tooltips().Count);

        // Reading the screen.
        Capture("R1-main", main);
    }

    // ---- helpers ----

    static IntPtr OpenAddEntry(IntPtr target)
    {
        WithKeys(new[] { VK_CONTROL, VK_LCONTROL }, () => PostKey(target, (byte)'I'));
        return WaitWindow("Add Entry", 5);
    }

    static void CloseWithEscape(IntPtr dialog)
    {
        var g = GuiInfo(); PostKey(g.hwndFocus != IntPtr.Zero ? g.hwndFocus : dialog, VK_ESCAPE);
        if (!WaitGone(dialog, 3)) PostMessage(dialog, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        WaitGone(dialog, 3);
        Thread.Sleep(300);
    }

    static void CloseByButton(IntPtr dialog)
    {
        var cancel = Children(dialog, null).FirstOrDefault(c => Class(c).Contains("BUTTON") && Text(c) == "Cancel");
        IntPtr r;
        if (cancel != IntPtr.Zero) SendMessageTimeout(cancel, BM_CLICK, IntPtr.Zero, IntPtr.Zero, 0x2, 5000, out r);
        if (!WaitGone(dialog, 4)) CloseWithEscape(dialog);
        Thread.Sleep(400);
    }

    static void DismissPopups(int keep)
    {
        for (int i = 0; i < 3 && Popups().Count > keep; i++) { PostKey(Popups().Last(), VK_ESCAPE); Thread.Sleep(400); }
    }

    static string Attached(Func<string> action)
    {
        var me = GetCurrentThreadId();
        var attached = AttachThreadInput(me, uiThread, true);
        try { return "attached " + attached + ": " + action(); }
        finally { if (attached) AttachThreadInput(me, uiThread, false); }
    }

    // Holds the keys down in KeePass's input state while the action posts, then lets them go.
    static void WithKeys(byte[] keys, Action action)
    {
        Attached(() =>
        {
            var state = new byte[256];
            GetKeyboardState(state);
            foreach (var k in keys) state[k] = 0x80;
            SetKeyboardState(state);
            action();
            Thread.Sleep(700);
            foreach (var k in keys) state[k] = 0;
            SetKeyboardState(state);
            return "";
        });
    }

    static IntPtr KeyParam(byte vk, bool up, bool alt)
    {
        uint scan = MapVirtualKey(vk, 0);
        uint l = 1 | (scan << 16) | (alt ? 1u << 29 : 0) | (up ? (1u << 30) | (1u << 31) : 0);
        return (IntPtr)unchecked((int)l);
    }

    static void PostKey(IntPtr h, byte vk)
    {
        PostMessage(h, WM_KEYDOWN, (IntPtr)vk, KeyParam(vk, false, false));
        PostMessage(h, WM_KEYUP, (IntPtr)vk, KeyParam(vk, true, false));
    }

    static void PostAlt(IntPtr h, char c, bool holdAlt)
    {
        byte vk = (byte)char.ToUpperInvariant(c);
        Action post = () =>
        {
            PostMessage(h, WM_SYSKEYDOWN, (IntPtr)vk, KeyParam(vk, false, true));
            PostMessage(h, WM_SYSCHAR, (IntPtr)char.ToLowerInvariant(c), KeyParam(vk, false, true));
            PostMessage(h, WM_SYSKEYUP, (IntPtr)vk, KeyParam(vk, true, true));
        };
        if (holdAlt) WithKeys(new[] { VK_MENU, VK_LMENU }, post); else post();
    }

    static IntPtr Lparam(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }

    static void PostClickAt(IntPtr h, System.Windows.Rect screen, uint down, uint up)
    {
        var p = new POINT { X = (int)(screen.Left + screen.Width / 2), Y = (int)(screen.Top + screen.Height / 2) };
        ScreenToClient(h, ref p);
        PostMessage(h, WM_MOUSEMOVE, IntPtr.Zero, Lparam(p.X, p.Y));
        PostMessage(h, down, (IntPtr)(down == WM_LBUTTONDOWN ? 1 : 2), Lparam(p.X, p.Y));
        Thread.Sleep(60);
        PostMessage(h, up, IntPtr.Zero, Lparam(p.X, p.Y));
    }

    static void PostClick(IntPtr h, AutomationElement e, uint down, uint up) { PostClickAt(h, e.Current.BoundingRectangle, down, up); }

    static void PostMoveAt(IntPtr h, System.Windows.Rect screen, int jitter)
    {
        var p = new POINT { X = (int)(screen.Left + screen.Width / 2) + jitter, Y = (int)(screen.Top + screen.Height / 2) };
        ScreenToClient(h, ref p);
        PostMessage(h, WM_MOUSEMOVE, IntPtr.Zero, Lparam(p.X, p.Y));
    }

    static string Gui()
    {
        var g = GuiInfo();
        return "active " + Hex(g.hwndActive) + " '" + Title(g.hwndActive) + "', focus " + Hex(g.hwndFocus) + " " + Class(g.hwndFocus) + ", flags 0x" + g.flags.ToString("X");
    }

    static GUITHREADINFO GuiInfo()
    {
        var g = new GUITHREADINFO(); g.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        GetGUIThreadInfo(uiThread, ref g);
        return g;
    }

    static List<IntPtr> TopWindows()
    {
        var found = new List<IntPtr>();
        EnumWindows((h, l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (pid == keePass.Id && IsWindowVisible(h)) found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }

    // Visible top-level windows of KeePass that are menus: WinForms drop-downs carry WS_EX_TOOLWINDOW
    // and WS_POPUP and no title.
    static List<IntPtr> Popups()
    {
        return TopWindows().Where(h => (GetWindowLong(h, -16) & unchecked((int)0x80000000)) != 0 && Title(h).Length == 0 && Class(h).StartsWith("WindowsForms10.Window")).ToList();
    }

    static List<IntPtr> Tooltips()
    {
        return TopWindows().Where(h => Class(h).IndexOf("tooltips_class32", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
    }

    static List<IntPtr> Children(IntPtr parent, string cls)
    {
        var found = new List<IntPtr>();
        if (parent == IntPtr.Zero) return found;
        EnumChildWindows(parent, (h, l) => { if (cls == null || Class(h) == cls) found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }

    static IntPtr WaitWindow(string titlePart, int seconds)
    {
        return WaitFor(() => TopWindows().FirstOrDefault(w => Title(w).IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0), seconds);
    }

    static bool WaitGone(IntPtr h, int seconds)
    {
        var until = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < until) { if (!IsWindow(h) || !IsWindowVisible(h)) return true; Thread.Sleep(100); }
        return false;
    }

    static IntPtr WaitFor(Func<IntPtr> find, int seconds)
    {
        var until = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < until) { var h = find(); if (h != IntPtr.Zero) return h; Thread.Sleep(150); }
        return IntPtr.Zero;
    }

    static AutomationElement ById(AutomationElement within, string id)
    {
        return within.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
    }

    static AutomationElement FindMenuItem(string prefix)
    {
        var items = AutomationElement.RootElement.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.ProcessIdProperty, keePass.Id)));
        return items.Cast<AutomationElement>().FirstOrDefault(i => i.Current.Name.Replace("&", "").StartsWith(prefix));
    }

    static IntPtr Hwnd(AutomationElement e) { return e == null ? IntPtr.Zero : new IntPtr(e.Current.NativeWindowHandle); }

    static List<AutomationElement> RowElements(AutomationElement list)
    {
        return list.FindAll(TreeScope.Children, new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))).Cast<AutomationElement>().ToList();
    }

    static int Rows(AutomationElement list) { return RowElements(list).Count; }

    static bool Listed(AutomationElement list, string name) { return RowElements(list).Any(r => r.Current.Name == name); }

    static void SelectFirst(AutomationElement tree)
    {
        var first = tree.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem));
        if (first != null) Try(() => { ((SelectionItemPattern)first.GetCurrentPattern(SelectionItemPattern.Pattern)).Select(); return ""; });
    }

    static string Selected(AutomationElement e)
    {
        object p;
        return e != null && e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out p) ? ((SelectionItemPattern)p).Current.IsSelected.ToString() : "?";
    }

    static char Last(string accessKey) { return string.IsNullOrEmpty(accessKey) ? '?' : accessKey.Trim()[accessKey.Trim().Length - 1]; }

    static string Try(Func<string> f)
    {
        try { return f(); } catch (Exception e) { return "threw " + e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
    }

    static string Title(IntPtr h) { var s = new StringBuilder(256); if (h != IntPtr.Zero) GetWindowText(h, s, 256); return s.ToString(); }
    static string Class(IntPtr h) { var s = new StringBuilder(256); if (h != IntPtr.Zero) GetClassName(h, s, 256); return s.ToString(); }
    static string Text(IntPtr h) { var s = new StringBuilder(512); if (h != IntPtr.Zero) SendMessage(h, WM_GETTEXT, (IntPtr)512, s); return s.ToString(); }
    static string Hex(IntPtr h) { return "0x" + h.ToInt64().ToString("X"); }
    static string Describe(IEnumerable<IntPtr> windows) { return string.Join("; ", windows.Select(w => "'" + Title(w) + "' " + Class(w))); }

    static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return port;
    }

    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(uint tid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformationW(IntPtr o, int i, StringBuilder s, int n, out int needed);
    static string DesktopName() { var s = new StringBuilder(256); int n; GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, s, 512, out n); return s.ToString(); }

    static int Distinct(Bitmap b)
    {
        var seen = new HashSet<int>();
        for (int y = 0; y < b.Height; y += 2) for (int x = 0; x < b.Width; x += 2) seen.Add(b.GetPixel(x, y).ToArgb());
        return seen.Count;
    }

    static void Capture(string name, IntPtr h)
    {
        RECT r; GetWindowRect(h, out r);
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        foreach (var flags in new uint[] { 2, 0 })
        {
            using (var b = new Bitmap(w, ht, PixelFormat.Format32bppArgb))
            {
                bool ok;
                using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); ok = PrintWindow(h, dc, flags); g.ReleaseHdc(dc); }
                var file = name + "-printwindow-" + flags + ".png";
                b.Save(Path.Combine(outDir, file), ImageFormat.Png);
                Result("R", "PrintWindow(flags " + flags + ") of " + name, ok + ", " + w + " by " + ht + ", distinct colors " + Distinct(b) + ", " + file);
            }
        }
        using (var b = new Bitmap(w, ht, PixelFormat.Format32bppArgb))
        {
            var src = GetWindowDC(h);
            using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); BitBlt(dc, 0, 0, w, ht, src, 0, 0, 0x00CC0020); g.ReleaseHdc(dc); }
            ReleaseDC(h, src);
            var file = name + "-bitblt.png";
            b.Save(Path.Combine(outDir, file), ImageFormat.Png);
            Result("R", "BitBlt from GetWindowDC of " + name, "distinct colors " + Distinct(b) + ", " + file);
        }
        Result("R", "Graphics.CopyFromScreen over " + name, Try(() =>
        {
            using (var b = new Bitmap(w, ht)) { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, ht)); return "distinct colors " + Distinct(b); }
        }));
    }

    static void ShutDown()
    {
        if (keePass == null || keePass.HasExited) return;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                foreach (var w in TopWindows().Where(w => w != main)) { PostMessage(w, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                Thread.Sleep(500);
            }
            PostMessage(main, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (!keePass.WaitForExit(15000))
            {
                Note("KeePass still open after WM_CLOSE; windows " + Describe(TopWindows()) + "; ending it");
                keePass.Kill();
            }
            Note("KeePass exited with code " + keePass.ExitCode);
        }
        catch (Exception e) { Note("shutdown: " + e.Message); }
    }
}
