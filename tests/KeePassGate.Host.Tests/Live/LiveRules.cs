using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Automation;
using KeePassGate.Acl;
using KeePassGate.Bridge;
using KeePassGate.Diagnostics;
using KeePassGate.Gate;
using KeePassGate.Store;
using KeePassGate.Testing;
using KeePassGate.Ui;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;
using KeePassLib.Utility;
using NUnit.Framework;

namespace KeePassGate.Host.Tests.Live
{
    /// <summary>
    /// What the fixtures that drive a rule from a node's dialog to the file share: the plugin's
    /// log read for what it reports, KeePass's title and questions, the file read while KeePass
    /// runs, and the tree a session seeds.
    /// </summary>
    /// <remarks>
    /// **Nothing here sleeps for something the plugin or KeePass reports.** A write at a dialog's
    /// close is waited for by its line in the plugin's log, a save by KeePass's title, a question
    /// by its window, each with a bound, so a slow machine costs time and a broken plugin costs a
    /// bound rather than a guess.
    /// </remarks>
    internal static class LiveRules
    {
        /// <summary>What the plugin logs once the rules changed on a node's tab are written and the database is marked.</summary>
        public const string WrittenLine = " were written, and KeePass now has changes to save";

        /// <summary>What KeePass's title holds while the database holds changes nobody has saved.</summary>
        public const string UnsavedMark = "* - ";

        /// <summary>How long the write at a dialog's close is waited for, by its line in the log.</summary>
        public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);

        /// <summary>How long KeePass is given to draw a change of the mark in its title.</summary>
        public static readonly TimeSpan TitleTimeout = TimeSpan.FromSeconds(10);

        /// <summary>How long a save of the operator's, with a slow key derivation, is given to end.</summary>
        public static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(60);

        /// <summary>How long a window KeePass or the plugin raises is given to come up.</summary>
        public static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);

        /// <summary>The pointer to the bottom right corner of the screen, out of every window's middle.</summary>
        public static void PointerAway()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            TestNativeMethods.SetPhysicalCursorPos(screen.Right - 1, screen.Bottom - 1);
        }

        /// <summary>A node as the store names it: the 32 lowercase hexadecimal characters of its identifier.</summary>
        public static string Node(string uuid)
        {
            return uuid.ToLowerInvariant();
        }

        /// <summary>Whether a title of KeePass's main window marks changes nobody has saved.</summary>
        public static bool Marked(string title)
        {
            return title != null && title.IndexOf(UnsavedMark, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// KeePass's title once it shows the mark, or once it shows none, as asked, or as it last
        /// read when the time is up.
        /// </summary>
        public static string WaitForTitle(KeePassDriver driver, bool marked, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var title = Title(driver);
            while (Marked(title) != marked && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
                title = Title(driver);
            }

            return title;
        }

        /// <summary>KeePass's title, or what stands in its place where the main window has gone.</summary>
        public static string Title(KeePassDriver driver)
        {
            try
            {
                return driver.MainWindowTitle;
            }
            catch (ElementNotAvailableException)
            {
                return "(no main window)";
            }
        }

        /// <summary>Ctrl+S, KeePass's own shortcut for its save, with nothing waited for.</summary>
        public static void PressSave(KeePassDriver driver)
        {
            Keyboard.Front(driver.MainWindow);
            Keyboard.SendControl('s');
        }

        /// <summary>
        /// The operator's save, answered with KeePass's title once the mark has gone, or as it last
        /// read when the time is up.
        /// </summary>
        public static string Save(KeePassDriver driver)
        {
            PressSave(driver);
            return WaitForTitle(driver, false, SaveTimeout);
        }

        /// <summary>
        /// Whatever a read of the database's file gives, from a copy, so that KeePass's own file is
        /// never held while KeePass runs.
        /// </summary>
        public static T FromTheFile<T>(string path, Func<PwDatabase, T> read)
        {
            var copy = path + ".read";
            CopyWhenFree(path, copy);
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(copy), Key(), null);
                return read(database);
            }
            finally
            {
                database.Close();
                File.Delete(copy);
            }
        }

        /// <summary>The rules a database's store holds.</summary>
        public static StoredRules RulesOf(PwDatabase database)
        {
            using (var reading = new KdbxStore(database, GateVersion.Current, null).Read())
            {
                return StoredRules.Of(reading);
            }
        }

        /// <summary>The rules a file's store holds, read from a copy.</summary>
        public static StoredRules RulesIn(string path)
        {
            return FromTheFile(path, RulesOf);
        }

        /// <summary>
        /// The standing versions of a profile's row at a node, each as its kind and its mask, in
        /// the order of their masks: what a test compares what the store holds with.
        /// </summary>
        public static string Versions(StoredRules rules, string node, string profile)
        {
            var row = rules.At(node).Row(profile);
            var versions = row.Versions
                .Select(version => version.Kind + " " + VerbMask.Of(version.Letters))
                .OrderBy(text => text.Substring(text.IndexOf(' ') + 1), StringComparer.Ordinal)
                .ToList();
            if (row.Lost.Count > 0)
            {
                versions.Add("lost " + row.Lost.Count.ToString(CultureInfo.InvariantCulture));
            }

            return versions.Count == 0 ? "none" : string.Join(", ", versions);
        }

        /// <summary>A file copied as soon as nothing holds it, for twenty seconds at most.</summary>
        public static void CopyWhenFree(string from, string to)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (true)
            {
                try
                {
                    File.Copy(from, to, true);
                    return;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>The throwaway database's key, as anybody holding the file and its password makes it.</summary>
        public static CompositeKey Key()
        {
            var key = new CompositeKey();
            key.AddUserKey(new KcpPassword(ThrowawayKeePass.DatabasePassword));
            return key;
        }

        /// <summary>A group of a fixed identifier put under a parent, as a person makes a group.</summary>
        public static PwGroup AddGroup(PwGroup parent, string name, string uuid)
        {
            var group = new PwGroup(true, true, name, PwIcon.Folder) { Uuid = new PwUuid(MemUtil.HexStringToByteArray(uuid)), IsExpanded = true };
            parent.AddGroup(group, true);
            return group;
        }

        /// <summary>An entry of a fixed identifier put into a group, as a person makes an entry, every value invented.</summary>
        public static PwEntry AddEntry(PwGroup group, string title, string uuid)
        {
            var entry = new PwEntry(true, true) { Uuid = new PwUuid(MemUtil.HexStringToByteArray(uuid)) };
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, title.Replace(' ', '-').ToLowerInvariant() + "-invented"));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, "https://example.invalid"));
            group.AddEntry(entry, true);
            return entry;
        }

        /// <summary>
        /// The file replaced underneath a running KeePass the way a file synchronizer replaces it:
        /// the file as it stands, changed by another copy, written beside it, the file deleted, and
        /// the copy moved into its place.
        /// </summary>
        public static void ReplaceTheFile(ThrowawayKeePass keePass, Action<PwDatabase> change)
        {
            var temporary = Paths.Join(Path.GetDirectoryName(keePass.DatabasePath), ".synchronizer.throwaway.kdbx.tmp");
            CopyWhenFree(keePass.DatabasePath, temporary);
            var database = new PwDatabase();
            try
            {
                database.Open(IOConnectionInfo.FromPath(temporary), Key(), null);
                change(database);
                database.Save(null);
            }
            finally
            {
                database.Close();
            }

            File.Delete(keePass.DatabasePath);
            File.Move(temporary, keePass.DatabasePath);
        }

        /// <summary>
        /// KeePass's question in a window of its own, titled with KeePass's name alone, other than
        /// the main window, or null when none comes in time.
        /// </summary>
        public static AutomationElement Question(ThrowawayKeePass keePass, IntPtr main, TimeSpan timeout)
        {
            return Uia.WaitForOrNull(
                () =>
                {
                    var window = NativeWindows.Visible(keePass.Process)
                        .FirstOrDefault(shown => shown.Title == "KeePass" && shown.Handle != main);
                    return window == null ? null : AutomationElement.FromHandle(window.Handle);
                },
                timeout);
        }

        /// <summary>What a question of KeePass's asks: its main instruction, or every text where it shows no main instruction.</summary>
        public static string Asked(AutomationElement question)
        {
            var instruction = Uia.ById(question, ControlType.Text, "MainInstruction");
            return instruction == null ? Uia.Describe(question, ControlType.Text) : Uia.Name(instruction);
        }

        /// <summary>
        /// A question answered with the button whose caption starts with this label, and true once
        /// its window has gone.
        /// </summary>
        public static bool Answer(AutomationElement question, string label)
        {
            var window = new IntPtr(question.Current.NativeWindowHandle);
            var button = Uia.Descendant(question, ControlType.Button,
                shown => shown.Replace("&", string.Empty).StartsWith(label, StringComparison.Ordinal));
            Assert.That(button, Is.Not.Null, "no button " + label + " on the question: " + Uia.Inventory(question));

            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            return Gone(window, WindowTimeout);
        }

        /// <summary>Whether a window has left the screen within the time given.</summary>
        public static bool Gone(IntPtr window, TimeSpan timeout)
        {
            return Uia.WaitForOrNull(() => TestNativeMethods.IsWindowVisible(window) ? null : "gone", timeout) != null;
        }

        /// <summary>The window behind an element, or none for an element that has gone.</summary>
        public static IntPtr HandleOf(AutomationElement window)
        {
            try
            {
                return new IntPtr(window.Current.NativeWindowHandle);
            }
            catch (ElementNotAvailableException)
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>A dialog closed with its Cancel button, and true once it has gone.</summary>
        public static bool Cancel(KeePassDriver driver, AutomationElement dialog)
        {
            var window = HandleOf(dialog);
            return driver.PressButton(dialog, "Cancel") && Gone(window, WindowTimeout);
        }

        /// <summary>A dialog closed with Escape, as a person presses it, and true once it has gone.</summary>
        public static bool Escape(AutomationElement dialog)
        {
            var window = HandleOf(dialog);
            Keyboard.Front(dialog);
            Keyboard.SendEscape();
            return Gone(window, WindowTimeout);
        }

        /// <summary>A dialog closed with its OK, and true once it has gone.</summary>
        public static bool Accept(KeePassDriver driver, AutomationElement dialog)
        {
            var window = HandleOf(dialog);
            Keyboard.BringToFront(dialog);
            return driver.PressButton(dialog, "OK") && Gone(window, WindowTimeout);
        }

        /// <summary>A text box of a dialog given a value through its value pattern, as typing it does.</summary>
        public static void Type(AutomationElement dialog, string id, string text)
        {
            var box = Uia.WaitFor(id, () => Uia.ById(dialog, ControlType.Edit, id));
            ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
            Uia.WaitFor(id + " holding the text", () =>
                string.Equals(((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).Current.Value, text, StringComparison.Ordinal) ? text : null,
                TimeSpan.FromSeconds(10));
        }

        /// <summary>A page of a dialog's tab strip brought to the front, answered once the strip reports it chosen.</summary>
        public static void Page(AutomationElement dialog, string caption)
        {
            var page = Uia.WaitFor("the " + caption + " page", () => Uia.Descendant(dialog, ControlType.TabItem, shown => shown == caption), TimeSpan.FromSeconds(15));
            var selection = (SelectionItemPattern)page.GetCurrentPattern(SelectionItemPattern.Pattern);
            selection.Select();
            Uia.WaitFor("the " + caption + " page chosen", () => selection.Current.IsSelected ? caption : null, TimeSpan.FromSeconds(10));
        }

        /// <summary>A moment as a line of a record writes it.</summary>
        public static string Moment(DateTime? at)
        {
            return at == null ? "never" : at.Value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The plugin's own log, read for what the plugin reports, from a mark on.</summary>
    internal sealed class PluginLog
    {
        /// <summary>How the plugin's log stamps each line, in UTC, at its start.</summary>
        private const string StampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

        /// <summary>How many characters that stamp takes.</summary>
        private const int StampLength = 24;

        private readonly ThrowawayKeePass _keePass;

        public PluginLog(ThrowawayKeePass keePass)
        {
            _keePass = keePass;
        }

        /// <summary>Where the log stands now: what a later wait reads from.</summary>
        public int Mark
        {
            get { return _keePass.ReadPluginLog().Length; }
        }

        /// <summary>What the log holds from a mark on.</summary>
        public string Since(int mark)
        {
            var log = _keePass.ReadPluginLog();
            return mark >= log.Length ? string.Empty : log.Substring(mark);
        }

        /// <summary>Whether the log holds this text after the mark before the time is up.</summary>
        public bool WaitFor(int mark, string text, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                if (Since(mark).IndexOf(text, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// When the first line after the mark holding this text was written, by the time the line
        /// carries, or null where no such line stands.
        /// </summary>
        public DateTime? At(int mark, string text)
        {
            var line = Since(mark)
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(shown => shown.IndexOf(text, StringComparison.Ordinal) >= 0);
            if (line == null || line.Length <= StampLength)
            {
                return null;
            }

            DateTime at;
            if (!DateTime.TryParseExact(
                    line.Substring(0, StampLength), StampFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out at))
            {
                return null;
            }

            return at;
        }

        /// <summary>
        /// Waits for the gate to serve the database once KeePass has opened it, from a mark on, so
        /// that a later start of the same installation waits for its own lines.
        /// </summary>
        public void WaitUntilServing(int mark, ThrowawayKeePass keePass)
        {
            Assert.That(WaitFor(mark, "listening on 127.0.0.1:" + keePass.Port, TimeSpan.FromSeconds(60)), Is.True,
                "the plugin never reported a listening gate:" + Environment.NewLine + Since(mark));
            Assert.That(WaitFor(mark, "serving a database with authority", TimeSpan.FromSeconds(60)), Is.True,
                "the plugin never reported serving the database KeePass was started with:" + Environment.NewLine + Since(mark));
        }
    }

    /// <summary>
    /// A client the database's own authority issued, asking the gate of a real KeePass what it
    /// may do at a node, a fresh name for each connection.
    /// </summary>
    /// <remarks>
    /// The question the access routes answer, profile by profile in the tab's notation, which is
    /// the rule in force as the gate reads it: nothing the client asks is written, so asking
    /// changes nothing a later step reads. The gate's key is read from the file once, before a
    /// session makes every open of the file slow.
    /// </remarks>
    internal sealed class GateAsker
    {
        /// <summary>What a node that refuses this client is answered with: there is nothing there.</summary>
        public const string Refused = "refused";

        private readonly int _port;
        private readonly byte[] _key;
        private readonly X509Certificate2 _client;
        private readonly string _profile;

        public GateAsker(ThrowawayKeePass keePass, X509Certificate2 client, string profile)
        {
            _port = keePass.Port;
            _client = client;
            _profile = profile;
            using (var reading = keePass.ReadAuthority())
            {
                _key = (byte[])reading.Material.SniKey.Clone();
            }
        }

        /// <summary>A name this database's gate answers to, fresh for each connection.</summary>
        public string NewName()
        {
            return SniName.For(_key);
        }

        /// <summary>The mask the gate answers for the profile at a group, or <see cref="Refused"/>.</summary>
        public string AtGroup(string uuid)
        {
            return Ask("/groups/" + uuid + "/access");
        }

        /// <summary>The mask the gate answers for the profile at an entry, or <see cref="Refused"/>.</summary>
        public string AtEntry(string uuid)
        {
            return Ask("/entries/" + uuid + "/access");
        }

        private string Ask(string path)
        {
            var reply = LiveGateClient.Get(_port, NewName(), _client, path);
            if (!reply.Answered)
            {
                return "no answer: " + reply.Failure;
            }

            if (reply.Status == 404)
            {
                return Refused;
            }

            if (reply.Status != 200)
            {
                return reply.Status.ToString(CultureInfo.InvariantCulture) + " " + reply.Body;
            }

            var grants = Newtonsoft.Json.Linq.JObject.Parse(reply.Body)["grants"] as Newtonsoft.Json.Linq.JObject;
            var mask = grants == null ? null : grants[_profile];
            return mask == null ? "200 without the profile: " + reply.Body : (string)mask;
        }
    }

    /// <summary>A line of the rule table as UI automation reads it.</summary>
    internal sealed class TabLine
    {
        public int Index { get; set; }

        public string Profile { get; set; } = string.Empty;

        public string Mask { get; set; } = string.Empty;

        /// <summary>The word in the State column, empty where none stands.</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>The help text of the State cell, which is the cell's tooltip.</summary>
        public string StateHelp { get; set; } = string.Empty;

        /// <summary>The help text of the line itself, which tells a change made on the tab.</summary>
        public string LineHelp { get; set; } = string.Empty;

        /// <summary>Whether the line is out of the table's view.</summary>
        public bool Offscreen { get; set; }

        public AutomationElement ProfileCell { get; set; }

        public AutomationElement MaskCell { get; set; }

        public AutomationElement RemoveCell { get; set; }

        /// <summary>Whether this is the empty line a rule is started on.</summary>
        public bool Empty
        {
            get { return Profile == GrantTab.AddRowPrompt; }
        }

        public override string ToString()
        {
            return Index.ToString(CultureInfo.InvariantCulture) + " " + Profile + " " + Mask + " " +
                   (State.Length == 0 ? "-" : State) + (Offscreen ? " (out of view)" : string.Empty);
        }
    }

    /// <summary>
    /// The KPG tab of a group or entry dialog of a real KeePass, read through UI automation and
    /// pressed with the pointer and the keyboard as a person presses it.
    /// </summary>
    /// <remarks>
    /// **With a real pointer and real keystrokes**, because neither column of the table can be
    /// typed into and a list in a dropdown is not something the accessibility tree can be asked to
    /// operate: a press on a cell opens a picker, letters check what a rule grants, and Enter
    /// takes it. Each picker is waited for by its window, and each press by the table showing it.
    /// </remarks>
    internal sealed class RuleTab
    {
        private static readonly TimeSpan PickerTimeout = TimeSpan.FromSeconds(10);

        /// <summary>How long the table is given to show what a press did.</summary>
        private static readonly TimeSpan ShownTimeout = TimeSpan.FromSeconds(10);

        private const uint Wheel = 0x0800;

        private const int Notch = 120;

        private readonly KeePassDriver _driver;

        private RuleTab(KeePassDriver driver, AutomationElement dialog)
        {
            _driver = driver;
            Dialog = dialog;
        }

        public AutomationElement Dialog { get; }

        /// <summary>The dialog's KPG tab brought to the front, answered once its table is there.</summary>
        public static RuleTab Of(KeePassDriver driver, AutomationElement dialog)
        {
            LiveRules.Page(dialog, GrantTab.Text);
            Uia.WaitFor("the rule table", () => Uia.ById(dialog, GrantTab.GridControlName), TimeSpan.FromSeconds(15));
            var tab = new RuleTab(driver, dialog);
            Uia.WaitFor("the lines of the rule table", () => tab.Lines().Count > 0 ? "read" : null, TimeSpan.FromSeconds(15));
            return tab;
        }

        /// <summary>The group dialog of the group of this name, its KPG tab in front.</summary>
        public static RuleTab OfGroup(KeePassDriver driver, string name)
        {
            driver.SelectGroup(name);
            var dialog = driver.OpenGroupDialog();
            Assert.That(driver.ValueOf(dialog, "m_tbName"), Is.EqualTo(name), "the group dialog that opened is of another group");
            return Of(driver, dialog);
        }

        /// <summary>The entry dialog of the entry of this title in this group, its KPG tab in front.</summary>
        public static RuleTab OfEntry(KeePassDriver driver, string group, string title)
        {
            driver.SelectGroup(group);
            driver.SelectEntry(title);
            var dialog = driver.OpenChosenEntryDialog();
            Assert.That(driver.ValueOf(dialog, "m_tbTitle"), Is.EqualTo(title), "the entry dialog that opened is of another entry");
            return Of(driver, dialog);
        }

        /// <summary>The line above the table, which differs by dialog.</summary>
        public string Scope
        {
            get { return Uia.Name(Uia.ById(Dialog, ControlType.Text, GrantTab.ScopeLabelName)); }
        }

        /// <summary>Every line of the table, the empty line among them, in the order shown.</summary>
        public IReadOnlyList<TabLine> Lines()
        {
            var grid = Uia.ById(Dialog, GrantTab.GridControlName);
            if (grid == null)
            {
                return Array.Empty<TabLine>();
            }

            var lines = new SortedDictionary<int, TabLine>();
            foreach (var placed in grid.FindAll(TreeScope.Descendants, Condition.TrueCondition)
                         .Cast<AutomationElement>()
                         .Select(Placed.Of)
                         .Where(placed => placed != null))
            {
                TabLine line;
                if (!lines.TryGetValue(placed.Index, out line))
                {
                    line = new TabLine { Index = placed.Index };
                    lines[placed.Index] = line;
                }

                placed.Into(line);
            }

            return lines.Values.ToList();
        }

        /// <summary>The first line that answers, read again until a line does or the time is up; the test fails with every line read.</summary>
        public TabLine Line(string what, Func<TabLine, bool> which)
        {
            var found = Uia.WaitForOrNull(() => Lines().FirstOrDefault(which), ShownTimeout);
            Assert.That(found, Is.Not.Null, "no line is " + what + " among: " + Shown());
            return found;
        }

        /// <summary>Every line read, for a message.</summary>
        public string Shown()
        {
            return string.Join(" / ", Lines().Select(line => line.ToString()));
        }

        /// <summary>
        /// A rule started on the empty line: the first profile the list offers chosen with Enter,
        /// then the letters of the rule checked in the rule picker that opens after it, and Enter.
        /// </summary>
        public void AddRule(string profile, string letters, string mask)
        {
            var empty = Line("the empty line", line => line.Empty);
            Press(empty.ProfileCell);
            Picker(GrantTab.ProfilePickerName, "the profile picker");
            Keyboard.SendEnter();
            Choose(letters);
            Line(profile + " with " + mask, line => line.Profile == profile && line.Mask == mask);
        }

        /// <summary>A rule chosen on a line: its Permissions cell pressed, the letters toggled, and Enter.</summary>
        public void SetRule(TabLine line, string letters, string mask)
        {
            Press(line.MaskCell);
            Choose(letters);
            Line(line.Profile + " with " + mask, shown => shown.Profile == line.Profile && shown.Mask == mask);
        }

        /// <summary>
        /// The profile given this mask on the line the table shows for the profile, its own or
        /// inherited, by toggling each letter that differs; or on the empty line where the table
        /// shows the profile on no line.
        /// </summary>
        public void Give(string profile, string mask)
        {
            var line = Lines().FirstOrDefault(shown => shown.Profile == profile);
            if (line == null)
            {
                AddRule(profile, new string(mask.Where(letter => letter != VerbMask.Absent).ToArray()), mask);
                return;
            }

            var letters = Enumerable.Range(0, VerbMask.Width)
                .Where(at => (line.Mask.Length <= at || line.Mask[at] == VerbMask.Absent) != (mask[at] == VerbMask.Absent))
                .Select(at => VerbMask.Letters[at]);
            SetRule(line, new string(letters.ToArray()), mask);
        }

        /// <summary>The X on a line pressed with the pointer.</summary>
        public void PressX(TabLine line)
        {
            Assert.That(line.RemoveCell, Is.Not.Null, "the line " + line + " has no X");
            Keyboard.Front(Dialog);
            Mouse.Click(line.RemoveCell);
            LiveRules.PointerAway();
        }

        /// <summary>The box "Refuse any profile not named here" ticked, answered once the box reports it.</summary>
        public void TickBox()
        {
            var box = Uia.WaitFor("the box", () => Uia.ById(Dialog, ControlType.CheckBox, GrantTab.SealCheckBoxName));
            var toggle = (TogglePattern)box.GetCurrentPattern(TogglePattern.Pattern);
            toggle.Toggle();
            Uia.WaitFor("the box ticked", () => toggle.Current.ToggleState == ToggleState.On ? "ticked" : null, ShownTimeout);
        }

        /// <summary>The table scrolled down by the wheel, with the pointer over it, as a hand scrolls it.</summary>
        public void ScrollDown(int notches)
        {
            var grid = Uia.WaitFor("the rule table", () => Uia.ById(Dialog, GrantTab.GridControlName)).Current.BoundingRectangle;
            Keyboard.Front(Dialog);
            TestNativeMethods.SetPhysicalCursorPos((int)(grid.Left + (grid.Width / 2)), (int)(grid.Top + (grid.Height / 2)));
            for (var notch = 0; notch < notches; notch++)
            {
                TestNativeMethods.mouse_event(Wheel, 0, 0, unchecked((uint)-Notch), UIntPtr.Zero);
                Thread.Sleep(60);
            }

            LiveRules.PointerAway();
        }

        private void Press(AutomationElement cell)
        {
            Assert.That(cell, Is.Not.Null, "no cell to press");
            Keyboard.Front(Dialog);
            Mouse.Click(cell);
        }

        /// <summary>The rule picker waited for, the letters toggled, Enter pressed, and the picker waited out.</summary>
        private void Choose(string letters)
        {
            Picker(GrantTab.RulePickerName, "the rule picker");
            foreach (var letter in letters)
            {
                Keyboard.Send(new Keyboard.Stroke(false, letter));
            }

            Keyboard.SendEnter();
            Assert.That(_driver.WaitForWindowToClose(GrantTab.PickerName, PickerTimeout), Is.True, "the rule picker stayed open after Enter");
            LiveRules.PointerAway();
        }

        /// <summary>A picker holding the list of this name, waited for by its window.</summary>
        private void Picker(string list, string what)
        {
            var picker = Uia.WaitForOrNull(
                () =>
                {
                    var window = _driver.WaitForWindowById(GrantTab.PickerName, TimeSpan.Zero);
                    return window != null && Uia.ById(window, list) != null ? window : null;
                },
                PickerTimeout);
            Assert.That(picker, Is.Not.Null, what + " did not open");
        }

        /// <summary>An element of the table placed on a line: a line itself, or a cell of a column.</summary>
        private sealed class Placed
        {
            private const string RowWord = "Row ";

            /// <summary>What a cell of the last column holds on a line that offers its X.</summary>
            private const string RemoveMark = "X";

            private Placed(AutomationElement element, int index, string heading)
            {
                Element = element;
                Index = index;
                Heading = heading;
            }

            private AutomationElement Element { get; }

            public int Index { get; }

            /// <summary>The column's heading, or null for the line itself.</summary>
            private string Heading { get; }

            /// <summary>
            /// The element as a line or a cell of a line, by the name the table gives it: `Row n`
            /// for a line and the heading before it for a cell; null for anything else.
            /// </summary>
            public static Placed Of(AutomationElement element)
            {
                var name = Uia.Name(element);
                var line = name.StartsWith(RowWord, StringComparison.Ordinal);
                var at = line ? 0 : name.LastIndexOf(" " + RowWord, StringComparison.Ordinal) + 1;
                int index;
                if ((!line && at == 0)
                    || !int.TryParse(name.Substring(at + RowWord.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index))
                {
                    return null;
                }

                return new Placed(element, index, line ? null : name.Substring(0, at - 1));
            }

            public void Into(TabLine line)
            {
                try
                {
                    var current = Element.Current;
                    if (Heading == null)
                    {
                        line.LineHelp = current.HelpText ?? string.Empty;
                        return;
                    }

                    var value = ValueOf(Element);
                    if (Heading == GrantTab.ProfileColumnHeader)
                    {
                        line.Profile = value;
                        line.ProfileCell = Element;
                        line.Offscreen = current.IsOffscreen;
                    }
                    else if (Heading == GrantTab.MaskColumnHeader)
                    {
                        line.Mask = value;
                        line.MaskCell = Element;
                    }
                    else if (Heading == GrantTab.StateColumnHeader)
                    {
                        line.State = value;
                        line.StateHelp = current.HelpText ?? string.Empty;
                    }
                    else if (Heading == GrantTab.ForgetColumnHeader && value == RemoveMark)
                    {
                        line.RemoveCell = Element;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Gone as the table was drawn again: the line is read again by whoever
                    // waits on it.
                }
            }

            /// <summary>
            /// A cell's value, empty where the cell holds nothing: WinForms's accessible object of a
            /// grid's cell answers such a cell with the text <see cref="Nothing"/>.
            /// </summary>
            private static string ValueOf(AutomationElement element)
            {
                object pattern;
                var value = element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)
                    ? ((ValuePattern)pattern).Current.Value
                    : null;
                return value == null || value == Nothing ? string.Empty : value;
            }

            /// <summary>What a cell that holds nothing answers UI automation with as its value.</summary>
            private const string Nothing = "(null)";
        }
    }
}
