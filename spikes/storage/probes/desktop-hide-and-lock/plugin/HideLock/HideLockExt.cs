// The hiding and locking probe plugin: a probe for a throwaway KeePass, not part of the product.
// It tries, one switch at a time, each way a plugin has to hide the store (the group KeePassGate,
// its subgroups, and every entry carrying a KPG.Kind string) from the person at KeePass's window
// and to keep the store from being changed, and it logs what it saw and did, one line per event.
//
// Switches, read at load from HideLock.ini beside the DLL, one key=value per line:
//   log=<path>          the log file (required)
//   hide=off|ui|idle    remove the store's group node from the group tree and the store's entries
//                       from the entry list: at MainForm.UIStateUpdated (ui), or at every
//                       Application.Idle (idle)
//   dialogs=off|close|cancelsave|disableok|readonly|lockall
//                       what to do when a dialog about a store node is added to
//                       GlobalWindowManager: close it at once; cancel PwEntryForm.EntrySaving;
//                       disable its accept button and add a line of text; set the entry dialog's
//                       private edit mode to ViewReadOnlyEntry; make every control read-only
//   menus=off|disable|hide|hideonly  disable (hide: and hide; hideonly: hide without disabling)
//                       the menu items, context menu items and toolbar buttons that change a node
//                       while a store node is selected
//   keyfilter=off|on    an IMessageFilter swallowing the list's and tree's command keys while a
//                       store node is selected
//   drag=off|block      DragOver on the tree and the list answered with no effect when a store node
//                       is dragged or is the target
//   dragsel=off|on      with drag=block, a drag judged by the main window's selection as well
//                       (KeePass drags entries as text, so the drag's data names none)
//   guardbin=off|on     with menus, Empty Recycle Bin disabled while the bin holds a store entry
//   defaultaction=off|cancel  MainForm.DefaultEntryAction cancelled for a store entry
//   watch=on|off        a fingerprint of the store at every UIStateUpdated, save and 300 ms poll,
//                       logged when it changes (on by default)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;

namespace HideLock
{
    public sealed class HideLockExt : Plugin, IMessageFilter
    {
        private IPluginHost _host;
        private MainForm _mf;
        private string _log;
        private readonly Dictionary<string, string> _o = new Dictionary<string, string>();
        private TreeView _tv;
        private ListView _lv;
        private int _busy;
        private int _uiCount;
        private TreeNode _lastRoot;
        private ListViewItem _lastFirst;
        private string _lastFp;
        private string _lastUiLine;
        private int _exceptions;
        private Timer _poll;
        private readonly List<ToolStripItem> _guarded = new List<ToolStripItem>();

        private string Opt(string k, string d) { string v; return _o.TryGetValue(k, out v) ? v : d; }

        private static readonly string[] EntryItems = {
            "m_menuEntryEdit", "m_menuEntryEditQuick", "m_menuEntryIcon", "m_menuEntryColor",
            "m_menuEntryTagAdd", "m_menuEntryTagRemove", "m_menuEntryTagNew", "m_menuEntryExpiresNow",
            "m_menuEntryExpiresNever", "m_menuEntryDuplicate", "m_menuEntryDelete", "m_menuEntryRearrange",
            "m_menuEntryMoveToGroup", "m_menuEntryMoveToTop", "m_menuEntryMoveOneUp", "m_menuEntryMoveOneDown",
            "m_menuEntryMoveToBottom", "m_menuEntryMoveToPreviousParent", "m_menuEntryOtpGenSettings" };
        private static readonly string[] GroupItems = {
            "m_menuGroupEdit", "m_menuGroupDuplicate", "m_menuGroupDelete", "m_menuGroupRearrange",
            "m_menuGroupMoveToTop", "m_menuGroupMoveOneUp", "m_menuGroupMoveOneDown", "m_menuGroupMoveToBottom",
            "m_menuGroupMoveToPreviousParent", "m_menuGroupSort", "m_menuGroupSortRec", "m_menuGroupAdd" };
        private static readonly string[] AddItems = { "m_menuEntryAdd", "m_tbAddEntry" };

        public override bool Initialize(IPluginHost host)
        {
            _host = host;
            var ini = Path.Combine(Path.GetDirectoryName(typeof(HideLockExt).Assembly.Location), "HideLock.ini");
            if (File.Exists(ini))
                foreach (var line in File.ReadAllLines(ini))
                {
                    var i = line.IndexOf('=');
                    if (i > 0) _o[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            _log = Opt("log", null);
            if (_log == null) return false;
            _mf = host.MainWindow;

            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                if (_exceptions >= 400) return;
                var st = new System.Diagnostics.StackTrace(1, false).ToString().Replace("\r", "").Split('\n');
                // The driver reads the tree and the list through the accessibility interface, whose
                // probing of absent children throws and is caught inside WinForms: not KeePass's.
                if (st.Any(x => x.Contains("Accessibility.IAccessible") || x.Contains("IAccessibleInternal"))) return;
                _exceptions++;
                var top = string.Join(" | ", st.Take(8).Select(x => x.Trim()));
                Write("FirstChance", e.Exception.GetType().Name + ": " + Clean(e.Exception.Message) + " @ " + top);
            };
            // No Application.ThreadException handler: WinForms keeps one handler per thread, and one
            // added here would replace the dialog a person sees when KeePass's thread throws (KeePass
            // 2.62 adds none of its own). The first-chance line above is the plugin's record.

            _tv = Find<TreeView>("m_tvGroups");
            _lv = Find<ListView>("m_lvEntries");
            Write("Initialize", "options=" + string.Join(",", _o.Where(kv => kv.Key != "log").Select(kv => kv.Key + "=" + kv.Value))
                + " tree=" + (_tv != null) + " list=" + (_lv != null));

            _mf.UIStateUpdated += (s, e) => Guard("UIStateUpdated", () => OnUi("UIStateUpdated"));
            _mf.FileOpened += (s, e) => Guard("FileOpened", () => Fp("FileOpened"));
            _mf.FileSavingPre += (s, e) => Guard("FileSavingPre", () => Fp("FileSavingPre"));
            _mf.FileSaved += (s, e) => Guard("FileSaved", () => Fp("FileSaved"));
            _mf.FileClosingPre += (s, e) => Guard("FileClosingPre", () => Fp("FileClosingPre"));
            if (Opt("hide", "off") == "idle") Application.Idle += (s, e) => Guard("Idle", () => Hide("Idle"));
            GlobalWindowManager.WindowAdded += (s, e) => Guard("WindowAdded", () => OnWindowAdded(e.Form));
            GlobalWindowManager.WindowRemoved += (s, e) => Guard("WindowRemoved", () =>
                Write("WindowRemoved", e.Form.GetType().Name + " <<" + e.Form.Text + ">> result=" + e.Form.DialogResult));
            if (Opt("defaultaction", "off") == "cancel")
                _mf.DefaultEntryAction += (s, e) => Guard("DefaultEntryAction", () =>
                {
                    bool st = IsStoreEntry(e.Entry);
                    if (st) e.Cancel = true;
                    Write("DefaultEntryAction", "entry=" + Title(e.Entry) + " column=" + e.ColumnId + " cancelled=" + st);
                });
            if (Opt("keyfilter", "off") == "on") Application.AddMessageFilter(this);
            if (Opt("drag", "off") == "block")
            {
                if (_tv != null) _tv.DragOver += (s, e) => Guard("TreeDragOver", () => BlockDrag(e, true));
                if (_lv != null) _lv.DragOver += (s, e) => Guard("ListDragOver", () => BlockDrag(e, false));
            }
            if (Opt("menus", "off") != "off")
            {
                foreach (var name in new[] { "m_menuEntry", "m_menuGroup" })
                {
                    var top = FindItem(name) as ToolStripMenuItem;
                    if (top != null) top.DropDownOpening += (s, e) => Guard("DropDownOpening", () => ApplyMenus("DropDownOpening " + name));
                }
                if (_tv != null && _tv.ContextMenuStrip != null)
                    _tv.ContextMenuStrip.Opening += (s, e) => Guard("CtxGroupOpening", () => ApplyMenus("CtxGroupOpening"));
                if (_lv != null && _lv.ContextMenuStrip != null)
                    _lv.ContextMenuStrip.Opening += (s, e) => Guard("CtxEntryOpening", () => ApplyMenus("CtxEntryOpening"));
            }
            if (Opt("watch", "on") == "on")
            {
                _poll = new Timer { Interval = 300 };
                _poll.Tick += (s, e) => Guard("Poll", () => Fp("Poll"));
                _poll.Start();
            }
            return true;
        }

        // ------------------------------------------------------------ what the store is

        private static bool IsStoreGroup(PwGroup g)
        {
            for (var p = g; p != null; p = p.ParentGroup) if (p.Name == "KeePassGate") return true;
            return false;
        }

        private static bool IsStoreEntry(PwEntry e)
        {
            if (e == null) return false;
            return e.Strings.Exists("KPG.Kind") || IsStoreGroup(e.ParentGroup);
        }

        private bool StoreSelected(out string what)
        {
            var sel = _mf.GetSelectedEntries() ?? new PwEntry[0];
            var g = _mf.GetSelectedGroup();
            bool entries = sel.Any(IsStoreEntry);
            bool group = g != null && IsStoreGroup(g);
            what = "entries=" + sel.Length + (entries ? "(store)" : "") + " group=" + (g != null ? g.Name : "-") + (group ? "(store)" : "");
            return entries || group;
        }

        // ------------------------------------------------------------ the window's update

        private void OnUi(string ev)
        {
            _uiCount++;
            bool treeRebuilt = _tv != null && _tv.Nodes.Count > 0 && _tv.Nodes[0] != _lastRoot;
            if (_tv != null && _tv.Nodes.Count > 0) _lastRoot = _tv.Nodes[0];
            bool listRebuilt = _lv != null && _lv.Items.Count > 0 && _lv.Items[0] != _lastFirst;
            int storeNodes = _tv == null ? -1 : StoreNodes(_tv.Nodes).Count;
            int storeItems = _lv == null ? -1 : StoreItems().Count;
            string what;
            bool sel = StoreSelected(out what);
            var line = "treeRebuilt=" + treeRebuilt + " listRebuilt=" + listRebuilt + " storeNodes=" + storeNodes
                + " storeItems=" + storeItems + " listItems=" + (_lv != null ? _lv.Items.Count : -1) + " " + what + " storeSelected=" + sel;
            if (line != _lastUiLine) { Write(ev + "#" + _uiCount, line); _lastUiLine = line; }
            if (Opt("hide", "off") == "ui") Hide(ev);
            if (Opt("menus", "off") != "off") ApplyMenus(ev);
            Fp(ev);
            if (_lv != null && _lv.Items.Count > 0) _lastFirst = _lv.Items[0];
        }

        private List<TreeNode> StoreNodes(TreeNodeCollection c)
        {
            var l = new List<TreeNode>();
            foreach (TreeNode n in c)
            {
                var g = n.Tag as PwGroup;
                if (g != null && g.Name == "KeePassGate") l.Add(n);
                else l.AddRange(StoreNodes(n.Nodes));
            }
            return l;
        }

        private List<ListViewItem> StoreItems()
        {
            var l = new List<ListViewItem>();
            foreach (ListViewItem it in _lv.Items)
            {
                var pli = it.Tag as PwListItem;
                if (pli != null && IsStoreEntry(pli.Entry)) l.Add(it);
            }
            return l;
        }

        private void Hide(string ev)
        {
            if (_busy > 0) return;
            _busy++;
            try
            {
                if (_tv != null)
                {
                    foreach (var n in StoreNodes(_tv.Nodes))
                    {
                        bool selected = _tv.SelectedNode != null && (_tv.SelectedNode == n || IsUnder(_tv.SelectedNode, n));
                        n.Remove();
                        Write("HideNode", "at=" + ev + " node=" + n.Text + " wasSelected=" + selected + " nowSelected=" + (_tv.SelectedNode != null ? _tv.SelectedNode.Text : "-"));
                    }
                }
                if (_lv != null)
                {
                    var items = StoreItems();
                    if (items.Count > 0)
                    {
                        _lv.BeginUpdate();
                        foreach (var it in items) _lv.Items.Remove(it);
                        _lv.EndUpdate();
                        Write("HideItems", "at=" + ev + " removed=" + items.Count + " left=" + _lv.Items.Count);
                    }
                }
            }
            finally { _busy--; }
        }

        private static bool IsUnder(TreeNode n, TreeNode top)
        {
            for (var p = n; p != null; p = p.Parent) if (p == top) return true;
            return false;
        }

        // ------------------------------------------------------------ dialogs

        private void OnWindowAdded(Form f)
        {
            string about = "";
            bool store = false;
            var pef = f as PwEntryForm;
            if (pef != null)
            {
                var mvec = pef.MultipleValuesEntryContext;
                var entries = mvec != null ? mvec.Entries : new[] { pef.EntryRef };
                bool adding = pef.EditModeEx == PwEditMode.AddNewEntry;
                var g = _mf.GetSelectedGroup();
                store = entries.Any(IsStoreEntry) || (adding && g != null && IsStoreGroup(g));
                about = "mode=" + pef.EditModeEx + " entries=" + entries.Length + " first=" + Title(entries.FirstOrDefault())
                    + " multi=" + (mvec != null) + " parent=" + (pef.EntryRef != null && pef.EntryRef.ParentGroup != null ? pef.EntryRef.ParentGroup.Name : "-")
                    + " selectedGroup=" + (g != null ? g.Name : "-");
            }
            else if (f is GroupForm)
            {
                var fi = typeof(GroupForm).GetField("m_pwGroup", BindingFlags.Instance | BindingFlags.NonPublic);
                var g = fi != null ? fi.GetValue(f) as PwGroup : null;
                store = g != null && IsStoreGroup(g);
                about = "m_pwGroup=" + (fi == null ? "field-missing" : (g != null ? g.Name : "null")) + " selectedGroup=" + (_mf.GetSelectedGroup() != null ? _mf.GetSelectedGroup().Name : "-");
            }
            else if (f is DuplicationForm)
            {
                string what;
                store = StoreSelected(out what);
                about = what;
            }
            else
            {
                string what;
                bool sel = StoreSelected(out what);
                about = what + " storeSelected=" + sel;
            }
            var mode = Opt("dialogs", "off");
            Write("WindowAdded", f.GetType().Name + " <<" + f.Text + ">> " + about + " store=" + store + " action=" + (store ? mode : "none"));
            if (!store || mode == "off") return;
            if (!(f is PwEntryForm || f is GroupForm || f is DuplicationForm)) return;

            switch (mode)
            {
                case "close":
                    f.BeginInvoke(new MethodInvoker(() =>
                    {
                        Guard("CloseDialog", () =>
                        {
                            f.DialogResult = DialogResult.Cancel;
                            f.Close();
                            Write("ClosedDialog", f.GetType().Name + " visible=" + f.Visible + " disposed=" + f.IsDisposed);
                        });
                    }));
                    break;
                case "cancelsave":
                    if (pef != null)
                        pef.EntrySaving += (s, e) => Guard("EntrySaving", () => { e.Cancel = true; Write("EntrySaving", "cancelled"); });
                    AddLine(f);
                    break;
                case "disableok":
                    DisableOk(f, "WindowAdded");
                    AddLine(f);
                    var t = new Timer { Interval = 250 };
                    t.Tick += (s, e) => Guard("OkWatch", () =>
                    {
                        if (f.IsDisposed) { t.Stop(); return; }
                        var b = f.AcceptButton as Button;
                        if (b != null && b.Enabled) { Write("OkReenabled", f.GetType().Name + " by KeePass; disabled again"); b.Enabled = false; }
                    });
                    t.Start();
                    break;
                case "readonly":
                    if (pef != null)
                    {
                        var fm = typeof(PwEntryForm).GetField("m_pwEditMode", BindingFlags.Instance | BindingFlags.NonPublic);
                        Write("ReadOnly", "m_pwEditMode " + (fm == null ? "field-missing" : "set from " + fm.GetValue(f)));
                        if (fm != null) fm.SetValue(f, PwEditMode.ViewReadOnlyEntry);
                    }
                    else LockAll(f);
                    break;
                case "lockall":
                    f.BeginInvoke(new MethodInvoker(() => Guard("LockAll", () => LockAll(f))));
                    break;
            }
        }

        private void DisableOk(Form f, string ev)
        {
            var b = f.AcceptButton as Button;
            if (b != null) b.Enabled = false;
            Write("DisableOk", f.GetType().Name + " at=" + ev + " button=" + (b != null ? b.Text : "none"));
        }

        private void AddLine(Form f)
        {
            var l = new Label
            {
                Text = "Probe: this is a KeePassGate store entry and is read-only.",
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = Color.DarkRed,
                TextAlign = ContentAlignment.MiddleCenter,
                Name = "m_lblProbeReadOnly"
            };
            f.Controls.Add(l);
            l.BringToFront();
        }

        private void LockAll(Form f)
        {
            int n = 0;
            foreach (var c in All(f))
            {
                if (c == f.CancelButton) continue;
                var tb = c as TextBoxBase;
                if (tb != null) { tb.ReadOnly = true; n++; continue; }
                if (c is ButtonBase || c is ListControl || c is DateTimePicker || c is NumericUpDown || c is ListView || c is TreeView)
                { c.Enabled = false; n++; }
            }
            AddLine(f);
            Write("LockAll", f.GetType().Name + " controls=" + n);
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var d in All(c)) yield return d;
            }
        }

        // ------------------------------------------------------------ menus, keys, drag

        private void ApplyMenus(string ev)
        {
            string what;
            var sel = _mf.GetSelectedEntries() ?? new PwEntry[0];
            var g = _mf.GetSelectedGroup();
            bool entryStore = sel.Any(IsStoreEntry);
            bool groupStore = g != null && IsStoreGroup(g);
            StoreSelected(out what);
            var menuMode = Opt("menus", "off");
            bool hide = menuMode == "hide" || menuMode == "hideonly";
            bool disable = menuMode == "disable" || menuMode == "hide";
            int reenabled = 0, changed = 0;
            var names = new List<string>();
            if (entryStore) names.AddRange(EntryItems);
            if (groupStore) { names.AddRange(GroupItems); names.AddRange(AddItems); }
            var pdb = _mf.ActiveDatabase;
            if (Opt("guardbin", "off") == "on" && pdb != null && pdb.IsOpen && pdb.RecycleBinEnabled)
            {
                var bin = pdb.RootGroup.FindGroup(pdb.RecycleBinUuid, true);
                if (bin != null && bin.GetEntries(true).Any(IsStoreEntry)) names.Add("m_menuGroupEmptyRB");
            }
            var texts = new HashSet<string>();
            foreach (var name in names)
            {
                var it = FindItem(name);
                if (it == null) continue;
                texts.Add(it.Text);
                if (disable && it.Enabled) { reenabled++; it.Enabled = false; changed++; }
                if (hide && it.Available) { it.Available = false; changed++; }
                if (!_guarded.Contains(it)) _guarded.Add(it);
            }
            // Items guarded earlier, for a selection that is no longer the store's, are given back.
            foreach (var it in _guarded.ToList())
            {
                if (names.Contains(it.Name)) continue;
                if (hide && !it.Available) it.Available = true;
                _guarded.Remove(it);
            }
            foreach (var cms in new[] { _tv != null ? _tv.ContextMenuStrip : null, _lv != null ? _lv.ContextMenuStrip : null })
            {
                if (cms == null) continue;
                foreach (ToolStripItem it in cms.Items)
                    if (texts.Contains(it.Text) && (it.Enabled || (hide && it.Available)))
                    {
                        if (disable && it.Enabled) { it.Enabled = false; reenabled++; }
                        if (hide) it.Available = false;
                        changed++;
                    }
            }
            if (changed > 0) Write("Menus", "at=" + ev + " " + what + " foundEnabled=" + reenabled + " changed=" + changed);
        }

        private ToolStripItem FindItem(string name)
        {
            var r = _mf.MainMenuStrip != null ? _mf.MainMenuStrip.Items.Find(name, true) : new ToolStripItem[0];
            if (r.Length > 0) return r[0];
            var tool = Find<ToolStrip>("m_toolMain");
            if (tool != null) { r = tool.Items.Find(name, true); if (r.Length > 0) return r[0]; }
            return null;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x0100 && m.Msg != 0x0104) return false;
            bool onList = _lv != null && _lv.IsHandleCreated && m.HWnd == _lv.Handle;
            bool onTree = _tv != null && _tv.IsHandleCreated && m.HWnd == _tv.Handle;
            if (!onList && !onTree) return false;
            var key = (Keys)(int)m.WParam;
            var mods = Control.ModifierKeys;
            bool block = false;
            try
            {
                string what;
                bool store = StoreSelected(out what);
                if (!store) return false;
                if (key == Keys.Delete || key == Keys.F2 || key == Keys.Insert || key == Keys.Return) block = true;
                if ((mods & Keys.Alt) != 0 && (key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End)) block = true;
                if ((mods & Keys.Control) != 0 && key == Keys.V) block = true;
                if (block) Write("KeySwallowed", (onList ? "list" : "tree") + " key=" + key + " mods=" + mods + " " + what);
            }
            catch (Exception ex) { Write("FilterFailed", ex.ToString()); }
            return block;
        }

        private void BlockDrag(DragEventArgs e, bool tree)
        {
            bool block = false;
            string why = "";
            var entries = e.Data.GetData(typeof(PwEntry[])) as PwEntry[];
            if (entries != null && entries.Any(IsStoreEntry)) { block = true; why = "store entries dragged"; }
            // KeePass drags entries and groups as text (the formats it offers are String,
            // UnicodeText and Text), so what is dragged is read from the main window's selection.
            if (Opt("dragsel", "off") == "on")
            {
                var sel = _mf.GetSelectedEntries() ?? new PwEntry[0];
                if (sel.Any(IsStoreEntry)) { block = true; why = "store entries selected while dragging"; }
            }
            var grp = e.Data.GetData(typeof(PwGroup)) as PwGroup;
            if (grp != null && IsStoreGroup(grp)) { block = true; why = "store group dragged"; }
            if (tree)
            {
                var n = _tv.GetNodeAt(_tv.PointToClient(new Point(e.X, e.Y)));
                var g = n != null ? n.Tag as PwGroup : null;
                if (g != null && IsStoreGroup(g)) { block = true; why = "onto the store group"; }
            }
            if (block && e.Effect != DragDropEffects.None)
            {
                Write("DragBlocked", (tree ? "tree" : "list") + " effect was " + e.Effect + "; " + why + " formats=" + string.Join(",", e.Data.GetFormats()));
                e.Effect = DragDropEffects.None;
            }
        }

        // ------------------------------------------------------------ the store's fingerprint

        private void Fp(string ev)
        {
            if (Opt("watch", "on") != "on") return;
            var pd = _mf.ActiveDatabase;
            if (pd == null || !pd.IsOpen) return;
            var sb = new StringBuilder();
            var all = pd.RootGroup.GetEntries(true).Where(IsStoreEntry).OrderBy(x => x.Uuid.ToHexString()).ToList();
            sb.Append("store=").Append(all.Count).Append(" groups=")
                .Append(pd.RootGroup.GetGroups(true).Count(IsStoreGroup)).Append(" records=").Append(pd.DeletedObjects.UCount)
                .Append(" modified=").Append(pd.Modified ? 1 : 0).Append(" [");
            foreach (var x in all)
                sb.Append(Title(x)).Append('@').Append(x.ParentGroup != null ? x.ParentGroup.Name : "-")
                    .Append(" t=").Append(x.LastModificationTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture))
                    .Append(" h=").Append(x.History.UCount).Append(" icon=").Append((int)x.IconId)
                    .Append(x.BackgroundColor.IsEmpty ? "" : " bg").Append(x.Expires ? " exp" : "").Append("; ");
            sb.Append(']');
            var fp = sb.ToString();
            if (fp == _lastFp) return;
            _lastFp = fp;
            Write("Store", "at=" + ev + " " + fp);
        }

        // ------------------------------------------------------------ helpers

        private T Find<T>(string name) where T : Control
        {
            var r = _mf.Controls.Find(name, true);
            return r.Length > 0 ? r[0] as T : null;
        }

        private static string Title(PwEntry e) { return e == null ? "-" : e.Strings.ReadSafe(PwDefs.TitleField); }
        private static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        private void Guard(string where, Action a)
        {
            try { a(); }
            catch (Exception ex) { Write("ProbeFailed", where + ": " + ex); }
        }

        private void Write(string what, string text)
        {
            // The driver appends its DRIVER lines to the same file, so a write can meet its handle;
            // retried rather than lost.
            var line = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + what + " " + Clean(text) + Environment.NewLine;
            for (int i = 0; i < 20; i++)
            {
                try { File.AppendAllText(_log, line); return; }
                catch (IOException) { System.Threading.Thread.Sleep(15); }
            }
        }

        public override void Terminate()
        {
            Write("Terminate", "uiStateUpdated=" + _uiCount + " firstChance=" + _exceptions);
            if (Opt("keyfilter", "off") == "on") Application.RemoveMessageFilter(this);
        }
    }
}
