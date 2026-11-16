# Measured: hiding the store from KeePass's window, and keeping it from being edited

Run 2026-11-16 on this machine's desktop. The question: in the KeePass that runs the plugin
(2.62), can a plugin (a) hide the store's group `KeePassGate` and its entries from a person at the
window, as a setting, and (b) make them read-only in effect? The store is the one of the next
design: ordinary entries, written once, in a group of the plugin's own, carrying custom strings
named `KPG.<something>`, some protected.

**Everything in the tables is measured**: the window read through UI automation, the accessibility
interface or Win32 messages and pictured, the probe plugin's own log of what it saw and did inside
KeePass, and each file KeePass left read back with KeePassLib 2.62. What is read from KeePass's
source (2.62, the extracted tree of the family `ground-keepass`) is written in prose and marked
**read**; nothing read is in a table.

| Part | Cases | Counted runs | Not counted |
| --- | --- | --- | --- |
| A, hiding | `a-base`, `a-hide-ui`, `a-hide-idle`, `a-hide-grouped`, `a-bin`, `a-deep` | 6 | 4 |
| B.1, the dialogs | `b-base-dialogs`, `b-close`, `b-cancelsave`, `b-disableok`, `b-readonly`, `b-lockall`, `b-disableok-yes`, `b-cancelsave-yes` | 8 | 3 |
| B.2 to B.4, the commands | `b-watch`, `b-block`, `b-hideonly`, `b-block2` | 4 | 3 |
| total | | **18** | **10** |

**In short.** Hiding: the store's node and entries can be taken out of the tree and the list at
`Application.Idle` with public members and the controls' designer names, and the group's flags keep
it out of quick search and auto-type; the Last Modified dialog, field references in ordinary
entries, the status bar, export and print still reach or betray it, and removing at
`UIStateUpdated` made KeePass throw. Read-only: cancelling `PwEntryForm.EntrySaving`, disabling the
commands at every update, a key filter and a refused drop stopped every edit tried in the window;
the private `PwEntryForm.m_pwEditMode` gives KeePass's own read-only view; a disabled OK button does
not hold, because the save question saves anyway; and every maintenance, import, synchronize and
XML Replace command remains (section 6).

**The evidence** is in `desktop-evidence\hide-and-lock\` beside this report:
`runs\<case>-1\` each run's `run.log`, the plugin's `plugin.log` with a `DRIVER` line per step,
`dump-before.txt`, `dump-after.txt` and `diff.txt`, and the pictures; `runs\not-counted\` the runs
set apart; `logs\` the batches' console output; `source\` the probe, the plugin and the scripts as
they stand after the runs. The work directory is written `<work>`. No database is copied. The
family `spikes\storage\probes\desktop-hide-and-lock` on the branch holds the same sources and the
same record under `recorded\`.

## 1. How the runs were made

- **KeePass 2.62**: the pinned `.keepass\2.62` of the branch, copied into the work directory as
  `kp-hl`, with the probe plugin `HideLock.dll` in `Plugins\HideLock`. Before every start a fresh
  `KeePass.config.xml` is written beside the executable (the desktop-windows family's
  `Write-KpConfig`): no update check, no single instance, Esc doing nothing in the main window, the
  window at 40,20 and 1100 by 720. One case sets the entry list's grouping and "Show Entries of
  Subgroups" there.
- **The token.** Every KeePass was started through the session's `unelevated.ps1`
  (`runas /trustlevel:0x20000`); the started process's token read from the process itself:
  Administrators present only as a deny-only group, integrity level High, `TokenIsElevated` 1, as
  in the earlier desktop runs.
- **The drive `W:`.** The work directory is mapped as `W:` with `subst`, so that no dialog shows a
  path that names a user.
- **The database**, made a moment before each case by the probe (`probe\Hl.cs`, `hlbase`) over
  KeePassLib 2.62, invented content only: the group **KeePassGate** under the root (searching and
  auto-type off) holding ten store entries `KPG grant 1` (S1) to `KPG profile 12` (S12), two of
  them (S9, S10) in its subgroup **Archive**, and **S11** moved into the recycle bin with
  KeePassGate as its previous parent: twelve in all, each with `KPG.Kind` and `KPG.Body` plain and
  `KPG.Secret` protected. S1 carries a user name, password and notes (`zqxstore-user1`,
  `zqxstore-pw1`, `zqxstore-notes1`); S2 expired on 2026-01-01; S6 carries the tag `kpgtag`.
  Beside them **Passwords** holds `Mail account` (tag `kpgtag`, one history item), `Bank`, and
  `Linked`, whose user name, password, URL, notes and URL override are field references to S1
  (`{REF:U@I:...}`, `{REF:P@I:...}`, `{REF:N@I:...}`); an empty group and the recycle bin. The
  file's last selected group is KeePassGate. The other side of a synchronize is the same database
  with one more store entry, S13. Variants put KeePassGate under the recycle bin (`bin`) or under
  Passwords/Old/2019/Misc (`deep`).
- **The probe plugin** `HideLock` (`plugin\HideLock\HideLockExt.cs`, `HideLock.HideLockExt` in
  `HideLock.dll`, product name "KeePass Plugin"), a probe and not product code. It reads its
  switches from `HideLock.ini` beside it, so that each technique runs alone, and logs one line per
  event: every `MainForm.UIStateUpdated` with whether the group tree and the entry list were
  rebuilt since the last one and how many store nodes and store items they held; every window
  `GlobalWindowManager` adds and removes, with the node it is about; every first-chance exception
  raised in KeePass's process (those raised inside WinForms' accessibility code by the driver's own
  reading are left out); and a fingerprint of the store (each store entry's parent, modification
  time, history count, icon, color, expiry) whenever it changes, checked at every
  `UIStateUpdated`, `FileSavingPre`, `FileSaved`, `FileClosingPre` and every 300 ms. A store entry is one
  carrying `KPG.Kind` or lying under a group named KeePassGate.
- **Driving the windows**: the desktop-windows family's `Ui.ps1` and `Kp.ps1`, dot-sourced from
  that family unchanged, with one function replaced (below). The tree and the list are read
  through their accessibility interface; after KeePass rebuilds the tree that interface answers
  with no rows (as in the earlier runs), and the plugin's log is then what tells what the tree held.
  Every step of a case is written into the plugin's log as a `DRIVER` line, so that what the
  plugin saw is read against what was done.
- **One correction to the earlier helpers.** `Show-Front` taps Alt before `SetForegroundWindow`;
  tapped while KeePass was already in front, that Alt armed KeePass's menu bar, and the letters of
  the next quick search opened Tools, Options (the first run of `a-base`, set apart). The
  replacement leaves a window already in front alone, and disarms the menu bar of the main window
  with Escape after the tap.
- **Pictures** are cut from the screen at the window's frame; each was looked at for anything of
  this machine before it was kept.

## 2. Hiding

### 2.1 What the source holds

**Read** (`KeePass\Forms\MainForm_Functions.cs`, `MainForm.cs`, `SearchForm.cs`):

- **The tree and the list are built with no filter and no hook.** `UpdateGroupList` clears the
  tree and adds a node for every group of the database (`GuiAddGroupRec`, `:1438-1531`);
  `UpdateEntryList` clears the list and adds every entry of the group shown or of a results group
  (`:1546-1640`). Nothing between those two and the screen asks a plugin. What a plugin has is
  `MainForm.UIStateUpdated`, raised at the end of `UpdateUIState` (`:729`), which `UpdateUI`
  calls after both rebuilds (`:4359-4376`) and `ShowSearchResults` calls after the results are
  listed and the first one selected (`:4378-4394`), and the controls themselves, reachable by
  their designer names through `MainForm.Controls.Find` (`m_tvGroups`, `m_lvEntries`), with each
  node's `Tag` a `PwGroup` and each item's `Tag` a `KeePass.UI.PwListItem`.
- **The group's searching flag is honored by some views and not by others.** Quick search and the
  expired-entries view pass `RespectEntrySearchingDisabled = true` (`:2309`, `:2359`, `:2376`).
  "Find, All" calls the quick search with that flag **false** (`MainForm.cs:2391`); the Find
  dialog's box "Ignore search settings of groups" sets it false (`SearchForm.cs:298`); "Tag"
  lists every entry with the tag (`FindEntriesByTag`, `:6023-6038`); "Last Modified Entries" and
  the other list dialogs of the Find menu open a dialog of their own (`ListViewForm`) over
  `EntryUtil`'s lists, of which only the duplicate and similar password lists and the password
  quality list skip entries whose searching is off (`EntryUtil.cs:1014`, `:1150`, `:1587`). Field
  references are resolved with the flag false (`SprEngine.cs:650`).
- **The tree has no in-place editing**: `m_tvGroups` and `m_lvEntries` never set `LabelEdit`, and
  F2 on either opens the group or entry dialog (`HandleGroupKeyEvent`, `HandleEntryKeyEvent`).
- **What the file keeps of the selection**: at a save and a lock, `SaveViewState` writes the group
  selected in the tree as `PwDatabase.LastSelectedGroup` (`:3990-4006`); at an open,
  `RestoreViewState` finds that group and selects it, expanding its parents (`:4008-4027`).

### 2.2 The runs

Counted runs: **6**, one per case below. Not counted, 4, kept in `runs\not-counted\`: the
exploration of the menus (`explore-1`), two runs of `a-base` (the menu bar armed by `Show-Front`,
then the Find dialog's button looked for as "OK" while it is "Find", which left the dialog
standing over every later step), and a run of `a-hide-ui` whose plugin still subscribed to
`Application.ThreadException`, which in WinForms replaces the dialog a person sees when the thread
throws; it was made again without that subscription.

| Case | Plugin switch | Place of KeePassGate | Steps | Seconds |
| --- | --- | --- | --- | --- |
| `a-base` | none (watch only) | under the root | the eleven views below, then the eight rebuilding steps | 113 |
| `a-hide-ui` | `hide=ui`: the node and the store's items removed at every `UIStateUpdated` | under the root | the same | 122 |
| `a-hide-idle` | `hide=idle`: the same at every `Application.Idle` | under the root | the same | 125 |
| `a-hide-grouped` | `hide=ui`; the list's grouping On and "Show Entries of Subgroups" on in the configuration | under the root | open, the root, Find All | 33 |
| `a-bin` | none | under the recycle bin | open, quick search, Find All, Empty Recycle Bin, save | 48 |
| `a-deep` | none | Passwords/Old/2019/Misc | open, Passwords | 23 |

### 2.3 What a person saw

The tree and the list as read through the accessibility interface after each step, the status bar,
and the plugin's count of store nodes and store items; pictures `runs\<case>-1\*.png`.

| View | Nothing switched on (`a-base`) | Removed at `UIStateUpdated` (`a-hide-ui`) | Removed at `Application.Idle` (`a-hide-idle`) |
| --- | --- | --- | --- |
| Open, KeePassGate the last selected group | KeePassGate selected, its 9 entries listed | KeePassGate absent from the tree; the selection moved to the next node, "Empty group"; the list empty | the same |
| Quick search `zqxstore` (a word in every store entry's `KPG.Body`) | nothing found; status "0 entries found, 2 groups skipped ('Recycle Bin', 'KeePassGate')." | the same: **the status bar names the hidden group** | the same |
| Quick search `KPG` | `Mail account` (by its tag `kpgtag`); the same status naming KeePassGate | the same | the same |
| Find, All | **15 entries: every store entry**, S11 in the recycle bin and S9, S10 in Archive included; "15 entries found." | 3 user entries; "3 entries found." | 3 user entries; **"15 entries found."** and "1 of 15 selected" |
| Find, Expired | nothing (S2, expired, is in a group with searching off); KeePassGate named as skipped | the same | the same |
| Find, Tag, `kpgtag` | `Mail account` and **`KPG profile 1`** | `Mail account`; "1 entry found." | `Mail account`; **"2 entries found."** |
| Find, Last Modified Entries... | a dialog "Last Modified" listing **all 12 store entries** with the user's 3 | **the same: all 12 store entries** | **the same** |
| Find dialog (Ctrl+F), `zqxstore`, "Ignore search settings of groups" checked, Find | **the 12 store entries** | the list emptied, and **KeePass's thread threw**: WinForms' dialog "Unhandled exception has occurred in your application. If you click Continue, the application will ignore this error and attempt to continue. If you click Quit, the application will close immediately. Object reference not set to an instance of an object." [Details] [Continue] [Quit]; the plugin's log: `NullReferenceException` in `ListViewItem.set_Selected`, from `UIUtil.SetFocusedItem`, `MainForm.SelectFirstEntryIfNoneSelected`, `ShowSearchResults`, `PerformSearchDialog`; Continue pressed, KeePass went on | the list empty, no exception |
| View, Show Entries of Subgroups, the root selected | all 15 entries | 3 | 3 |
| Entry view of `Linked`, whose fields reference S1 | "User Name: zqxstore-user1 - {REF:U@I:...}", the URL with S1's user name in it; the password masked | the same | the same |
| Ctrl+C on `Linked` | a question "{REF:P@I:...} Are you sure you want to evaluate/replace the placeholder?" [Yes] [No]; Yes: **S1's password `zqxstore-pw1` on the clipboard** | the same | the same |
| Ctrl+B on `Linked` | **S1's user name `zqxstore-user1`** on the clipboard, no question | the same | the same |
| The list's grouping On, Show Entries of Subgroups on, the root; then Find All (`a-hide-grouped`) | | 3 user entries under their group headers; "3 entries found." | |

**When KeePass rebuilds the tree.** The plugin compared the tree's first node and the list's first
item with those of the previous `UIStateUpdated`. In all three cases the tree was rebuilt at the
open, after the drag of an entry onto another group, after a lock and unlock, and after a
synchronize; it was **not** rebuilt by a save, a quick search, a Find view, a column sort, a
change of selection or the tree's arrow keys. The list was rebuilt at every change of group and
every search. Under both switches the plugin removed the store's node again at each of the four
rebuilds (4 `HideNode` lines per case) and the store's items at each listing that held one. Under
`hide=idle` the removal came after KeePass had written its status bar, which therefore counted the
removed entries; under `hide=ui` it came before the status line of a search and after the
selection of the first result, which is where the exception came from: the Find dialog's results
were all store entries, the plugin removed the first one inside the selection change, and KeePass
then set `Selected` on the removed item.

**The tree's keyboard.** With the root selected, eight presses of Down walked the tree to its
end; under both switches no step selected KeePassGate or a group under it (the plugin's selected
group at each `UIStateUpdated`).

**A synchronize that brings a new store entry** (File, Synchronize, Synchronize with File, the
other file holding S13): "Synchronization completed successfully."; the plugin's store count went
from 12 to 13 at its 300 ms poll and at the next `UIStateUpdated`, and the tree, rebuilt, lost
KeePassGate again under both switches.

### 2.4 Placing the group elsewhere

| Case | What the window showed | What KeePass's commands did |
| --- | --- | --- |
| `a-bin`, KeePassGate under the recycle bin | the tree expanded to KeePassGate under "Recycle Bin" (it was the last selected group) and its 9 entries listed; quick search: "1 group skipped ('Recycle Bin')"; Find All: every store entry | Recycle Bin selected, Group, Empty Recycle Bin: "Are you sure you want to permanently delete the items? - KeePassGate, Archive" [Delete] [Cancel]; Delete; saved: **KeePassGate, Archive and all 12 store entries gone, 14 deletion records added** |
| `a-deep`, KeePassGate under Passwords/Old/2019/Misc | the tree expanded down to KeePassGate, selected, its entries listed: the stored selection opens the path | nothing run on it |

The plugin's own reading found the store in both places (12 store entries at its first
fingerprint), since it looks the group up by name below the root and the entries by `KPG.Kind`.

## 3. The dialogs

### 3.1 What the source holds

**Read** (`KeePass\Forms\PwEntryForm.cs`, `GroupForm.cs`, `UI\GlobalWindowManager.cs`):

- **When a plugin hears of a dialog.** `GlobalWindowManager.WindowAdded` (public, static) is
  raised from `AddWindow`, which the entry dialog calls at the start of its `OnFormLoad`
  (`PwEntryForm.cs:789`), after `InitEx` and before it clones the entry it will compare with
  (`:791`), sets its title by its edit mode (`:813-840`) and fills its controls. The group dialog
  calls it at the start of its load as well (`GroupForm.cs:89`).
- **What the entry dialog tells a plugin, publicly**: `EntryRef` (the entry being edited; for
  several entries a temporary one), `MultipleValuesEntryContext` (the entries, when several are
  edited), `EditModeEx` (read only: AddNewEntry, EditExistingEntry or ViewReadOnlyEntry), and two
  events, **`EntrySaving` with a `Cancel` flag** and `EntrySaved` (`:117-140`). `SaveEntry` raises
  `EntrySaving` before it writes anything and returns false when it is cancelled (`:1074-1079`);
  `OnBtnOK` then sets `DialogResult.None`, so the dialog stays open (`:1163-1167`). In
  ViewReadOnlyEntry mode `SaveEntry` writes nothing and answers true (`:1068`).
- **The edit mode is a private field**, `m_pwEditMode`, set by `InitEx` and read again by
  `OnFormLoad` and by the handlers of every control (`:307-353`, `:833`, `:949`, `:985`, `:1185`,
  `:2149`, `:2539`).
- **The group dialog tells a plugin nothing of its group**: `GroupForm` has `InitEx` and nothing
  public to read back; the group is the private field `m_pwGroup` (`:42`). Its OK assigns the
  dialog's copies back to the group (the `GroupForm` trap of the product's notes).
- **The duplicate dialog** (`DuplicationForm`) only collects the options; the duplication itself
  is done by the main window after its OK.

### 3.2 The runs

Each case runs the same nine routes on a fresh database: S1's entry dialog by a double click, its
title then typed over and Enter pressed; S3's by Enter; the nine entries of KeePassGate selected
with Ctrl+A and Enter (one dialog for several entries); Add Entry (Ctrl+I) with KeePassGate
selected; Duplicate Entry (Ctrl+D) on S3; the group dialog of KeePassGate (F2 on the tree) and of
its subgroup Archive; and, as a check, a user's entry, `Bank`. Each dialog left standing was
closed with Escape, and KeePass's question "Do you want to save the changes before closing?",
when it came, was read, pictured and answered No; the database was saved at the end and read
back. Counted runs: **6**, one per case. Not counted, 3, kept in `runs\not-counted\`: the first
runs of `b-base-dialogs`, `b-close` and `b-cancelsave`, in which that question was not answered
and stood over every later step.

The plugin decides that a dialog is about the store from `EntryRef` and
`MultipleValuesEntryContext` for the entry dialog (and, when adding, from the group selected in
the main window), from the private `GroupForm.m_pwGroup` for the group dialog, and from the main
window's selection for the duplicate dialog. It found the right node in every run (the plugin's
`WindowAdded` lines).

| Switch | What it does at `WindowAdded` | Entry dialog (S1, S3) | Several entries | Add Entry in KeePassGate | Duplicate Entry | Group dialog (KeePassGate, Archive) | The user's entry |
| --- | --- | --- | --- | --- | --- | --- | --- |
| none (`b-base-dialogs`) | | "Edit Entry", OK enabled; the typed title and Enter **saved S1 as "KPG grant 1 edited by hand"** (in the file) | "Edit Entries (9)", every field "(Multiple values)" | "Add Entry" | "Duplication Options" | "Edit Group" | "Edit Entry" |
| `close`: `DialogResult.Cancel` and `Close()` through `BeginInvoke` | | **no dialog stood**: the plugin closed it about 0.1 s after it was added, while still visible; KeePass's thread then raised first-chance `ObjectDisposedException` (`SecureTextBoxEx`, `TextBox`) and `InvalidOperationException` ("Invoke or BeginInvoke cannot be called on a control until the window handle has been created"), caught inside KeePass, with nothing shown | **KeePass asked "Do you want to save the changes before closing?" [Yes] [No] [Cancel] over the half-closed dialog** | no dialog stood, no entry added | no dialog stood, no duplicate made | no dialog stood (first-chance `ObjectDisposedException` on its `TabControl`) | opened as usual |
| `cancelsave`: `EntrySaving` cancelled, a line of text added | | "Edit Entry", OK enabled; **Enter kept the dialog open with nothing saved**, the plugin's `EntrySaving cancelled` line; Escape then raised the save question | the save question at Escape | the dialog opened, OK enabled | not covered: the dialog is not an entry dialog | not covered | opened as usual |
| `disableok`: the accept button disabled, a line of text added | | "Edit Entry", **OK disabled**, the line "Probe: this is a KeePassGate store entry and is read-only." at the bottom, laid over the lower edge of the Tools and Cancel buttons; typing in the title did not enable OK again (no `OkReenabled` line); Enter did nothing; Escape raised the save question | OK disabled; the save question at Escape | OK disabled | OK disabled | OK disabled | opened as usual, OK enabled |
| `readonly`: the private `PwEntryForm.m_pwEditMode` set to `ViewReadOnlyEntry`; for the other dialogs as `lockall` | | **"View Entry (Read-Only)", "You are viewing an entry."**: the title, user name, password, URL and expiry boxes grayed and read-only, the icon and generator buttons disabled, OK disabled, Cancel enabled; typing changed nothing; Enter did nothing; Escape closed it with **no question** | "View Entry (Read-Only)", no question | **"View Entry (Read-Only)"** in place of "Add Entry" | as `lockall` | as `lockall` | opened as usual |
| `lockall`: every text box read-only, every button, list and picker disabled but Cancel, a line of text added | | "Edit Entry", boxes read-only, OK disabled; typing changed nothing; Enter did nothing | the save question at Escape | the same | the four controls disabled, OK disabled | the 17 controls disabled, OK disabled | opened as usual |

In the file after each switch case, **no store entry and no store group had changed**: the diffs
against the file before hold only access times and usage counts.

**The save question answered Yes.** Two more cases (counted, one run each) opened S1, typed a new
title and a new password, pressed Escape, and answered KeePass's "Do you want to save the changes
before closing?" with Yes, then saved:

| Switch | What followed Yes | The file |
| --- | --- | --- |
| `disableok` (`b-disableok-yes`) | **the dialog closed with `DialogResult.OK`**, the OK button never enabled | **S1 saved with the new title and password**, its earlier version in a new history item (2026-11-01T10:10:00Z), its modification time the time of the answer |
| `cancelsave` (`b-cancelsave-yes`) | the plugin's `EntrySaving cancelled`; **the dialog stayed open**; Escape again, No: closed with Cancel | S1 unchanged |

So a disabled OK button keeps nothing: the save question calls the same save the button does. The
cancelled `EntrySaving` is what stops the entry dialog's save on every path that reached it here.

## 4. The commands that open no dialog

### 4.1 What the source holds

**Read** (`KeePass\Forms\MainForm_Functions.cs:733-1046`, `:4736-4965`, `MainForm.cs`,
`MainForm.Designer.cs`):

- **The menu items' enabled state is KeePass's, set again at every update.** `UpdateUIState` calls
  `UpdateUIGroupMenuState` and `UpdateUIEntryMenuState` (`:654-655`), which set every Group and
  Entry command's `Enabled` from the selection alone (entries selected, a group other than the
  root); the Group and Entry menus' `DropDownOpening` and the two context menus' `Opening` set
  them again (`MainForm.cs:2510-2517`, `:1916`, `:2226`). The comment at `:653` gives the reason:
  "Enable/disable menu items for correct shortcut keys handling". A plugin's handler on the same
  events, added after KeePass's, runs after it.
- **The keys of the tree and the list do not go through the menu items.** `HandleEntryKeyEvent`
  calls the commands directly on Delete (`OnEntryDelete`, with Shift for a permanent delete),
  F2 and Enter (`OnEntryEdit`), Insert (`OnEntryAdd`), Shift+Insert and Ctrl+Shift+V (paste),
  Ctrl+A (select all) and Alt with Home, Up, Down or End (move) (`:4894-4965`, `:4817-4847`);
  `HandleGroupKeyEvent` does the same for groups on Delete, F2, Insert, Ctrl+Shift+V and the Alt
  keys (`:4849-4892`). The menu items' shortcuts (Ctrl+I add, Ctrl+D duplicate, Ctrl+Shift+V paste,
  and the same Delete and Alt keys) act through WinForms' `ToolStripMenuItem.ProcessCmdKey`, which
  fires an enabled item whether it is shown or not.
- **Drag and drop** starts in KeePass's own `ItemDrag` handlers and lands in its `DragDrop`
  handler on the tree; WinForms raises `DragDrop` only when the last `DragOver` left an effect, and
  a `DragOver` handler a plugin adds runs after KeePass's.
- **A delete, a move, a sort, a paste raise no event of their own.** After each, KeePass calls
  `UpdateUI` or `UpdateUIState`, so `UIStateUpdated` is the first public event after it; nothing
  public is raised before it.

### 4.2 The runs

The same sequence of commands, each on the store as a person gives it, in one KeePass per case,
saved at the end and read back: with nothing switched on (`b-watch`), and with the menu items and
toolbar buttons disabled while a store node is selected, the key filter, the drag block and the
default action cancelled (`b-block`: `menus=disable`, `keyfilter=on`, `drag=block`,
`defaultaction=cancel`). `b-hideonly` hides the same menu items without disabling them;
`b-block2` repeats the drag and Empty Recycle Bin with the drag judged by the selection
(`dragsel=on`) and Empty Recycle Bin disabled while the bin holds a store entry (`guardbin=on`).
Counted runs: **4**. Not counted, 3, kept in `runs\not-counted\`: the first `b-watch` and
`b-block`, whose later steps clicked the tree at places read before KeePass had reordered it (so
they selected the wrong group), and a `b-block` whose plugin received none of its switches: a
PowerShell hash table with a key named `keys` answers `.Keys` with that key's value, and the
driver wrote the switches from `.Keys`. The driver now enumerates the pairs and the switch is named
`keyfilter`.

Two steps of the counted `b-watch` did not run as written and are not read from it: the edit of
S1 through its dialog (the double click opened no dialog in that run; the dialog's edit is
measured in section 3, `b-base-dialogs` and `b-disableok-yes`), and "Move to Group", which the
2.62 Entry menu does not list under that name (not run).

| Command, as a person gives it | Nothing switched on: what KeePass showed, and what the store became | First event the plugin saw it at | Blocking switches (`b-block`, `b-block2`) | Menu items hidden, not disabled (`b-hideonly`) |
| --- | --- | --- | --- | --- |
| S3 selected, Delete | "Are you sure you want to move the selected entry to the recycle bin?" Yes: S3 in the recycle bin | `UIStateUpdated` right after the answer (the store's fingerprint, the database modified) | **the key swallowed** by the filter (`KeySwallowed list key=Delete`): no question, nothing changed | the question, Yes: S3 in the bin |
| S4, Shift+Delete | "Are you sure you want to permanently delete the selected entry?" Delete: S4 gone, a deletion record | `UIStateUpdated` | **swallowed** | not run |
| S5 dragged onto Passwords | no question: S5 in Passwords | `UIStateUpdated` | judged by the drag's data (`b-block`): **not stopped**, S5 in Passwords, since KeePass drags entries as text (formats String, UnicodeText, Text) and the data names no entry; judged by the selection (`b-block2`): **`DragOver` answered with no effect, nothing dropped**, S5 still in KeePassGate | not run |
| S6, Alt+Down | S6 one place down | not in the fingerprint (order); the file's index changed | **swallowed** | not run |
| S7, Ctrl+D; Entry, Duplicate Entry | "Duplication Options", OK: "KPG authority 1 - Copy" under a new identifier, twice | `UIStateUpdated` | **nothing happened**: the shortcut of a disabled item does not fire, and the menu item is disabled | **"Duplication Options" opened** from Ctrl+D on the hidden item |
| S8, Entry, Edit Entry (Quick), Expires: Now; then Color, Light Red | no question: S8 expired now, then colored | `UIStateUpdated` | **the Edit Entry (Quick) item disabled**: its submenu does not open | not run |
| S12, Ctrl+Shift+C; KeePassGate, Ctrl+Shift+V | no question: a second "KPG profile 12" under a new identifier in KeePassGate | `UIStateUpdated` (a first-chance `NotSupportedException` in `GZipStream.get_Position`, caught inside KeePassLib) | **the paste swallowed** on the tree | not run |
| S1, double click | "Edit Entry" (section 3) | | **`DefaultEntryAction` cancelled**: no dialog | not run |
| KeePassGate selected, Ctrl+I | "Add Entry" | | **nothing**: the item disabled | **"Add Entry" opened** |
| Recycle Bin, Group, Empty Recycle Bin | "Are you sure you want to permanently delete the items?" Delete: S3 and S11 gone, two records | `UIStateUpdated` | not guarded (`b-block`): **S11 deleted for good**; with `guardbin=on` (`b-block2`) **the item disabled**, read disabled through UI automation, nothing deleted | not run |
| KeePassGate, Ctrl+A, Delete | the question, Yes: every entry of KeePassGate in the bin | `UIStateUpdated` | **swallowed** | not run |
| KeePassGate in the tree, Alt+Down, then Delete | the group one place down; "Are you sure you want to move the selected group to the recycle bin?" Yes: KeePassGate with Archive in the bin | not in the fingerprint (it follows entries); the file's group parent | **both keys swallowed** on the tree | not run |
| The root, Group, Rearrange, Sort Direct Subgroups | the root's groups sorted, KeePassGate's place among them changed | not in the fingerprint | not guarded (the root is not a store node): sorted | not run |

**The menus, read through UI automation with S1 selected.** With nothing switched on, the Entry
menu, its Edit Entry (Quick) submenu, the list's context menu and the Group menu offered every
command enabled. Under `menus=disable` they read **disabled**: Edit Entry, Edit Entry (Quick),
Duplicate Entry, Delete Entry; in the context menu Add Entry, Edit Entry, Edit Entry (Quick),
Delete Entry and Rearrange (Select All and More Commands stayed enabled); in the Group menu Add
Group, Edit Group, Duplicate Group, Delete Group and Rearrange.

**KeePass enables them again at every update, and the plugin disables them after it.** The
plugin counted the guarded items it found enabled each time it acted: at every one of 51
`UIStateUpdated` events that followed a store selection, 11 to 31 of them; at each
`DropDownOpening` of the Entry menu 19, of the Group menu 9; at the context menu's `Opening` 19.
Its handlers, added after KeePass's, ran after them each time, and every read showed the items
disabled.

### 4.3 What an edit leaves in the entry

From the files the counted runs left (`dump-after.txt` against `dump-before.txt`):

| Change | History afterwards | Times |
| --- | --- | --- |
| Through the entry dialog (S1, `b-base-dialogs`, `b-disableok-yes`) | **one item added, holding the whole earlier version** (its `KPG.` strings, title, password) | the entry's modification time moved to the edit; the history item keeps the earlier one (2026-11-01T10:10:00Z) |
| Quick edits, Expires: Now and Color (S8, `b-watch`) | **one item per quick edit** (two) | modification time moved to the last one |
| Dragged onto another group (S5) | none | modification time kept; location time moved |
| Moved to the recycle bin (S3, and the whole store at Ctrl+A, Delete) | none | modification time kept; location time moved |
| Duplicated (S7) and pasted (S12) | the copy has none (S7 had none to copy) | **the copy keeps the original's modification time** (2026-11-01) under a new identifier, with a creation time of the copy |

So a write-once entry edited through the entry dialog or a quick edit keeps its earlier version in
its history, with the earlier modification time; a move keeps the entry's own modification time;
a duplicate or a paste is a second entry with a new identifier and the original's modification
time.

## 5. Not run, and why

The brief's order of cuts was followed: part A.3 and A.4 and the less common commands of B.2 were
cut first, and each cut is named here.

- **A.1, the rest of KeePass's commands under the hiding switches**: File, Export of the whole
  database, File, Print, Database Maintenance's counts, Delete Duplicate Entries, Delete Empty
  Groups. Not run under the switches, because none of them reads the tree or the list: **read**,
  each walks `PwDatabase.RootGroup` (`PerformExport`, `PrintGroup` over the root,
  `EntryUtil`'s maintenance), and the earlier desktop measurement ran each of them on the same kind
  of store (`measured-desktop-windows.md` 4.3 and 5.2): the XML export holds every `KPG.` string in
  clear, print lists the store's titles, Delete Duplicate Entries and Delete Empty Groups act on
  the store. Removing the tree's node changes none of that.
- **"Go to parent group"** (Find, Selected Entry's Group) on a store entry: under both switches no
  store entry stayed listed long enough to select it; with the selection on a store entry from the
  Last Modified dialog the command was not tried.
- **Global auto-type** under the switches: not rerun. The earlier measurement found the store's
  entries out of global auto-type by the group's auto-type flag alone (`measured-desktop-windows.md`
  4.3), and auto-type does not read the list.
- **A trigger reaching a store entry, and the entries' own "recently used" list**: KeePass 2.62
  keeps no list of recently used entries (**read**: the main window's most recently used list is of
  files); a trigger's actions that name an entry were not run.
- **A.3 beyond the two placements**: the plugin's own reading under the recycle bin was checked
  (12 store entries), not a plugin that writes there; "Deep" was opened and not searched.
- **A.4, other hooks**: none run. **Read**: neither `UpdateGroupList` nor
  `UpdateEntryList` offers a filter; the list dialogs of the Find menu (`ListViewForm`) are added to
  `GlobalWindowManager` and could be reached the same way as the entry dialog, which was not tried.
- **B.1, the entry list's cell editing and the tree's label editing**: none exists (**read**,
  section 2.1). Edit Group's own variants (Duplicate Group's dialog, the group's icon picker) and
  Edit Entries (Quick)'s Icon and Add Tag were not run.
- **B.2, the less common commands**: Move to Group (not found under that name in the 2.62 Entry
  menu), cut and paste of entries between two databases, Rearrange's other moves, Delete for good
  of a group, Sort Recursively, and every command under `menus=hideonly` but Ctrl+D, Ctrl+I and
  Delete.
- **The save question answered Yes under `lockall`**, and the several-entries dialog under
  `cancelsave` answered Yes: not run.
- **Printing on paper, and KeePassXC**: out of the question's scope.

## 6. What can be relied on in KeePass 2.62

### 6.1 Hiding

**A plugin can do reliably, with public members only:**

- keep the store out of quick search and the expired-entries view, and out of global auto-type, by
  the group's own flags (searching off, auto-type off) set on the group, with no plugin code
  running at all;
- remove the store's group node from the group tree and the store's entries from the entry list at
  `Application.Idle` (`hide=idle`): the controls reached by their designer names through
  `MainForm.Controls.Find("m_tvGroups")` and `("m_lvEntries")`, the node's `Tag` a `PwGroup`, an
  item's `Tag` a `KeePass.UI.PwListItem` (a public type). In the 19 views and rebuilding steps no store
  node or entry stayed in the tree or the list, nothing threw, and the selection moved off the hidden group by
  itself when it was the group stored in the file.

**What that costs, measured**: the status bar still counts the hidden entries after Find All and
Tag ("15 entries found", "1 of 15 selected", "2 entries found"), and every quick search and the
expired view name the group in their status line ("2 groups skipped ('Recycle Bin',
'KeePassGate')"). These are names of designer controls, not API: a KeePass that renames
`m_tvGroups` or `m_lvEntries` leaves the plugin hiding nothing, so a test has to pin both names.

**Removing at `MainForm.UIStateUpdated` instead (`hide=ui`) is not safe**: it ran inside KeePass's
own selection of the first search result, and KeePass's thread threw a `NullReferenceException`
(`MainForm.SelectFirstEntryIfNoneSelected` setting `Selected` on the removed item), which WinForms
showed to the person as "Unhandled exception has occurred in your application" with Continue and
Quit. It fires early enough to fix the status count and too early for KeePass's own code.

**What no technique here reaches** (the store stays visible to a person through these, under the
best technique):

1. **Find, Last Modified Entries...**, and by the same code the other list dialogs of the Find menu
   that do not skip groups with searching off (History, Large Entries: **read**,
   `EntryUtil.FindHistoryEvents`, `FindLargeEntries`): a dialog of its own listing
   all 12 store entries, untouched by the tree and list removal.
2. **An entry's field references**: an ordinary entry whose fields reference a store entry shows
   the store entry's user name in the entry view and the list, and copies its password with Ctrl+C
   (after "Are you sure you want to evaluate/replace the placeholder?") and its user name with
   Ctrl+B, under every switch.
3. **The file itself**: export, print, Database Maintenance and the maintenance commands walk the
   database, not the window (section 5; the earlier measurement).
4. **The status bar's counts and its line naming KeePassGate as skipped**, which tell a person that
   something is there.
5. **The moment between a rebuild and the next idle**, in which the node and the entries are on
   screen; no run caught one on a picture.

**Placing the group elsewhere does not hide it**: under the recycle bin or deep under another
group, the stored selection opened the path down to it at the next start, and under the recycle
bin, Empty Recycle Bin deleted the whole store for good with one question that names it
("- KeePassGate, Archive").

### 6.2 Read-only

**A plugin can do reliably, with public members only:**

- **cancel the entry dialog's save**, `PwEntryForm.EntrySaving` with `Cancel = true`, subscribed at
  `GlobalWindowManager.WindowAdded`: OK, Enter and Yes to "Do you want to save the changes before
  closing?" all left the entry unchanged and the dialog open; the person can only leave by Cancel
  or No. The person sees no reason unless the plugin adds one (the probe's line of text);
- **close any dialog about the store as it opens** (entry, several entries, Add Entry, Duplicate
  Entry, group): it vanished about 0.1 s after it appeared, nothing was changed, and KeePass raised
  first-chance exceptions it caught itself. For the several-entries dialog KeePass asked "Do you
  want to save the changes before closing?" over it, **and a Yes there was not tried**;
- **disable the menu items, context menu items and toolbar buttons** that change a node while a
  store node is selected, at `UIStateUpdated`, at the Entry and Group menus' `DropDownOpening` and
  at the context menus' `Opening`: KeePass enables them again at each of those events and the
  plugin, running after it, disabled them every time; a disabled item's shortcut (Ctrl+D, Ctrl+I)
  did nothing. **Hiding an item without disabling it does not stop its shortcut**;
- **swallow the tree's and the list's command keys** with an `IMessageFilter`
  (`Application.AddMessageFilter`) while a store node is selected: Delete, Shift+Delete, Alt with
  the arrows, Ctrl+Shift+V, Ctrl+A then Delete; KeePass's key handlers call the commands directly,
  so disabling the menu items alone does not stop these keys;
- **refuse a drop**: a `DragOver` handler on the tree and the list setting `Effect` to none, judged
  by the main window's selection (`GetSelectedEntries`), since KeePass's drag carries text;
- **cancel the default action of a double click**, `MainForm.DefaultEntryAction`;
- **disable Empty Recycle Bin** while the bin holds a store entry.

**Only through private members**:

- **`PwEntryForm.m_pwEditMode`** set to `PwEditMode.ViewReadOnlyEntry` at `WindowAdded`: the
  dialog opens as KeePass's own "View Entry (Read-Only)", boxes grayed, OK disabled, no save
  question at Escape, for a single entry, for several and for Add Entry. The cleanest of the
  techniques to a person; a test has to pin the field's name and type.
- **`GroupForm.m_pwGroup`**, to know which group a group dialog is about; without it the plugin
  can only guess from the main window's selection, which is what opened the dialog in every run
  here. `GlobalWindowManager`, `PwEntryForm.EntryRef`, `MultipleValuesEntryContext`, `EditModeEx`
  and `EntrySaving` are public.
- The designer names of the controls and menu items (`m_tvGroups`, `m_lvEntries`, `m_toolMain`,
  `m_menuEntry*`, `m_menuGroup*`, `m_menuGroupEmptyRB`), reached through public `Controls.Find` and
  `ToolStripItemCollection.Find`: not API, and each has to be pinned by a test.

**What does not work**: a disabled OK button (`disableok`) or every control made read-only
(`lockall`) keeps nothing, since KeePass's save question at Escape saves through the same path:
answered Yes, S1 was saved with the new title and password. Hiding menu items keeps nothing, since
their shortcuts still fire.

**What a person can still do to a store entry under the best combination** (`EntrySaving`
cancelled or the read-only edit mode, menu items disabled, the key filter, the drop refused by the
selection, the default action cancelled, Empty Recycle Bin guarded):

1. **Everything outside the main window's commands**: XML Replace, Database Maintenance's history
   and deleted-objects pages, Delete Duplicate Entries, Delete Empty Groups, Import, Synchronize
   (the earlier measurement, `measured-desktop-windows.md` 5.2 and 6), none of which raises
   anything before it acts.
2. **Commands on a selection that is not a store node but holds one**: Sort Direct Subgroups on the
   root reordered the store group among the root's groups; Delete or Duplicate Group of a group
   above KeePassGate (deep placement) takes the store with it, and a guard keyed on the selected
   node does not see it (not run, follows from the guard's rule).
3. **Paste from the clipboard with the store's own entries** (Ctrl+Shift+C on a store entry is a
   read and stays allowed): the copy is a new entry with a new identifier and the original's
   modification time, which a reading rule keyed on content will meet twice.
4. **Copy of every value**: Ctrl+C, Ctrl+B, the field references and the exports read the store;
   read-only is about changing it.
5. **A key or command the filter does not list** and any route added by another plugin.

**After the fact**: every change the window made was first seen by the plugin at the next
`MainForm.UIStateUpdated`, with the database already changed in memory and not yet saved; no
public event is raised before a delete, a move, a duplicate, a paste or a quick edit.
