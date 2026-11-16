# Measured: KeePass's and KeePassXC's windows over the store made of entries

Run 2026-11-16 on this machine's desktop. The question: what KeePass 2.62 and KeePassXC 2.8.0 do
in their own windows when the file changes underneath an open database (criterion 2 of the list of
compatible programs, that a program merges or asks before it writes over a changed file, never run
for either before), what KeePass's entry dialog does to a plugin string on a user's entry, what
KeePass's maintenance commands do to a store made of entries, and what a plugin can observe around
KeePass's own synchronize, import and save.

**A second run** of the family's one command (`Run.ps1`, in the family's `RUN.md`) repeated
measurement 5's last case and gave the same events and counts; nothing else ran twice.

**Everything in the tables is measured**: a file a program left, read back with KeePassLib 2.62
by the probe, or a window read through UI automation, the accessibility interface or Win32
messages and pictured. What is read from a program's source is written in prose and marked
**read**; nothing read is in a table.

| Measurement | Program | Counted runs | Not counted |
| --- | --- | --- | --- |
| 1, the file changed underneath an open database | KeePass 2.62 | 31 | 12 |
| 2, the same | KeePassXC 2.8.0 | 21 | 9 |
| 3, the entry dialog and `KPG.Ref` (dialog half; the person's half) | KeePass 2.62 | 10; 16 | 0; 13 |
| 4, maintenance commands on the store | KeePass 2.62 | 17 | 32 |
| 5, what a plugin observes | KeePass 2.62 | 14 | 20 |
| what a person sees of the store | KeePassXC 2.8.0 | 1 | 0 |
| total | | **110** | **86** |

**The evidence** is in `desktop-evidence\` beside this report: `runs\<measurement>\<case>-<run>\`
each run's `run.log` (every step, every window's title, texts and buttons as read, every press,
the file's size, time and SHA-256 before and after each replacement), the dumps of the files it
left and their diffs, and the pictures of every distinct window; `runs\<measurement>-not-counted\`
the runs set apart; `logs\` the verdict and summary lines and the batches' console output;
`base\` the dumps of the given databases; `source\` the scripts, the probe, the two probe plugins
and the auto-type target as they stand after the runs. Machine paths are written `<work>` (the work
directory), `<branch>`, `<session>` and `<profile>`. No database is copied. The family
`spikes\storage\probes\desktop-windows` on the branch holds the same sources and the same record
under `recorded\`.

## 1. How the runs were made

### 1.1 Programs, configuration, tokens

- **KeePass 2.62**: the pinned `.keepass\2.62` of the branch, `KeePass.exe` SHA-256
  `AFB89B74...C89C3F3`, copied into the work directory three times: `kp` (no plugin) for
  measurements 1 and 4 and every look at the window, `kp-log` (the logging plugin of measurement
  5) and `kp-write` (the writing plugin of measurement 3). Before every start a fresh
  `KeePass.config.xml` is written beside the executable with `PreferUserConfiguration` false, so
  that KeePass reads and writes that file and no other: no update check, no single instance, the
  weak key derivation warning off (the databases use a fast Argon2d), Esc doing nothing, the
  window placed at 40,20 and 1100 by 720. A case that tries an option sets it in that file.
- **The token.** Every KeePass and KeePassXC was started through the session's `unelevated.ps1`,
  which runs `runas /trustlevel:0x20000`, and the started process's token was read from the
  process itself (`scripts\Ui.ps1`, `TokenReport`): **Administrators present only as a deny-only
  group** (`IsInRole('Administrators')` false), integrity level **High** (`0x3000`),
  `TokenElevationType` 2 and `TokenIsElevated` 1. The token is a restricted copy of the
  session's elevated token: it acts as no Administrator, and Windows still reports it as an
  elevated token at high integrity. No run depends on the difference: no program asked for or
  used an administrator's right, and the drives and files are the work directory's.
- **KeePassXC 2.8.0**: the branch's pinned `.keepassxc\2.8.0`, copied into the work directory;
  its configuration in section 3.1.
- **The drive `W:`.** The work directory is mapped as `W:` with `subst` for the whole session, so
  that a path a dialog shows names no user; every file is still under the work directory, and the
  pictures show `W:\runs\...`.
- **Nothing outside the work directory was written by the runs but one thing**: KeePass's export
  "Windows Favorites" writes into the user profile's Favorites folder (4.3); the run removed what
  it wrote there, and kept a copy in the evidence. `%APPDATA%\KeePass`, `%LOCALAPPDATA%\KeePass`
  and KeePassXC's two profile folders did not exist before the runs and did not exist after them.

### 1.2 Driving the windows

UI automation and keyboard and mouse input from the session, as the earlier desktop runs did.
Three things here differ from those runs and cost time:

- **KeePass's group tree and entry list are bare panes to UI automation here.** The managed
  client's client-side providers do not load (`ProxyManager.LoadDefaultProxies` throws
  `NullReferenceException`, in Windows PowerShell 5.1 and in a managed host alike), and under
  PowerShell 7 they load and still show no items. The tree and the list are read and driven
  through their accessibility interface instead (`AccessibleObjectFromWindow`, `accSelect`,
  `accLocation`).
- **KeePass's WinForms dialogs expose no patterns and no control names.** Their boxes and
  buttons are found as Win32 child windows by class and caption, a box by the label on its line,
  and pressed with a click at their center; a task dialog's command links are clicked the same
  way. An invoke through UI automation held the driver whenever what it started opened another
  dialog, and `BM_CLICK` was ignored by the import dialog's OK.
- **UI automation's focus call did not move the focus between another process's controls**: the
  first attempts typed every password into the entry's title. A box is focused by a click in it.
- **After KeePass rebuilds its group tree** (a lock and an unlock, a synchronize) the tree's
  accessibility interface answers with no rows at all, while the tree on screen is the same; the
  rows' places read at the start are used for clicks from then on.
- **Pictures** are cut from the screen at the window's frame as the desktop window manager draws
  it, so that no other window and no part of the desktop is in them; each was looked at. The one
  file dialog in the pictures (Save As, 2.2) is cut to its file name and its confirmation.

## 2. Measurement 1: KeePass 2.62, the file changed underneath an open database

### 2.1 The two copies and the replacement

Made by the probe (`probe\Desk.cs`, `m1base` and `m1new`), KeePassLib 2.62, Argon2d with 1 MiB
and 2 iterations, invented content and the invented password, after the design of
`measured-k2a-synchronize.md` 1.2 with the roles turned: the window holds the older copy and
another writer's later save arrives underneath it.

| Item | OLD, the file KeePass opens | NEW, the other writer's later save | The window's own changes ("full") |
| --- | --- | --- | --- |
| group `KeePassGate`, searching and auto-type off | `KG1` "KPG grant 1" (`KPG.Grant`), `KG2` "KPG admission 1" (`KPG.Admission`, protected `KPG.Secret`) | the same, and **`KG3`** "KPG revocation" (`KPG.Revoked.c2`), a write-once entry | none |
| database custom data | `KPG.Profiles` with a per-key time | and **`KPG.NewKey`** | none |
| deletion records | `D1` | and **`DZ`**, naming no entry in either copy, and **`X1`**, an entry OLD holds and the window leaves alone | none |
| `P1` password | `p1-old` | `p1-other` | none |
| `P2` password | `p2-old` | unchanged | `p2-window` |
| `Q1`, both, the other's **older** | `q1-old` | `q1-other` at 2026-11-01T18:00Z | `q1-window` at the window's clock |
| `Q2`, both, the other's **newer** | `q2-old` | `q2-other` at 2027-01-01 | `q2-window` |
| `Q3`, both, **the same second** | `q3-old` | `q3-other` at the second of the window's edit of Q3 | `q3-window` |
| new entries | none | `NO` "other-new" | "window-new", added through Add Entry |

The window's changes go through the real entry dialog (`scripts\Kp.ps1`, `Kp-EditPassword`): the
row double-clicked, the password typed into Password and Repeat, OK. Q3's OK is pressed in the first
150 ms of a second and the times on both sides of the dialog's closing are logged; NEW is then made
with Q3's change at that second (in every "full" run both times fell in the same second). A case
marked "P2" makes P2's change alone and uses NEW-fixed, the same NEW with Q3 at 18:00Z.

**The replacement** (`scripts\Ui.ps1`, `Replace-File`) stands for a file synchronizer: NEW is
written beside the open file under a temporary name and renamed over it with `MoveFileEx`
(replace existing, write through), and the file's time is set **later** (the clock), **equal**
(the replaced file's time exactly) or **earlier** (an hour before it). **Same length** is
NEW-same, NEW found by search with OLD's length to the byte (3,413 bytes). **The control** is
OLD's own bytes under a later time. Every replacement logs the file's size, time and SHA-256
before and after it.

**What is read back.** After the case's flow KeePass is left with nothing to decide (an exit
answered "Discard changes" where it asks), and the file is dumped by the probe; `verdict` prints
each item: there or gone, a deletion record there or not, each password's current value with the
values its history holds.

### 2.2 What KeePass showed, and what the file held afterwards

Counted runs: **31**. Not counted, 12, each case repeated with its flow corrected: five
development runs of the first case while the driver was written (two typed every password into the
title, see 1.2, two pressed the task dialog's links with a message it ignored, one stopped on a
missing option; their directories were replaced by the counted runs and their console output is in
`logs\`); the toolbar case whose press through UI automation held the driver until KeePass was
stopped by hand; the Save As case that looked for a keyboard shortcut KeePass does not have; the
synchronize case and the locked-arrival case whose menu and key prompt were not found; three
attempts at the lock and unlock case, the first two before the key prompt's password box was
found, the third before the group tree was read through a rebuild. Those with a directory of their
own are in `runs\m1-not-counted\`; no file of theirs is read in this report.

The dialog every changed-file case showed, read through UI automation, title "KeePass", a task
dialog with command links (picture `runs\m1\a-sync-1\a-sync-1-1.png`):

> **Overwrite the existing file?**
> W:\runs\m1\a-sync-1\db.kdbx
> The file on disk/server has changed since it was loaded. Probably someone else has edited and
> saved the database.
> [Synchronize] Load the file on disk/server and merge it with the current database in memory.
> [Overwrite] Save the current database to the file. Changes made by the other user will be lost.
> [Cancel] Abort the current operation.

| Case | Counted runs | Sequence | What KeePass showed | The file afterwards |
| --- | --- | --- | --- | --- |
| a, Synchronize | 3 | full changes; NEW arrives, later; Ctrl+S; **Synchronize** | the dialog above | **a merge**: every item of NEW and every change of the window (table 2.3); no further dialog; the synchronize saved the file itself |
| a, Overwrite | 3 | the same; **Overwrite** | the dialog above | **OLD with the window's changes**: KG3, `KPG.NewKey`, DZ, X1's deletion, P1's and the three Q passwords of NEW, and NO **all gone**, from the entries and from every history |
| a, Cancel | 3 | the same; **Cancel** | the dialog above; then the window still marked changed (`db.kdbx*`) | **NEW byte for byte**: nothing written; the window's changes only in memory (an exit answered "Discard changes" lost them) |
| equal time | 1 | P2; NEW-fixed under the replaced file's own time; Ctrl+S; Overwrite | the dialog | OLD with P2: NEW's items gone |
| earlier time | 1 | P2; NEW-fixed an hour earlier; Ctrl+S; Overwrite | the dialog | the same |
| same length, equal time | 1 | P2; NEW-same (OLD's length) under OLD's time; Ctrl+S; Overwrite | the dialog | the same |
| same length, later | 1 | P2; NEW-same, later; Ctrl+S; Synchronize | the dialog | a merge |
| the control | 1 | P2; OLD's own bytes, later; Ctrl+S | **no dialog** | OLD with P2; nothing to lose |
| toolbar | 1 | P2; NEW later; the toolbar's Save Database button; Synchronize | the dialog | a merge |
| close | 1 | P2; NEW later; Ctrl+W | "Save database changes before closing the file?" [Save] [Discard changes] [Cancel]; Save; then the dialog above; Synchronize | a merge |
| lock | 1 | P2; NEW later; Ctrl+L | "Save database changes before locking the workspace?"; Save; the dialog above; **Overwrite** | OLD with P2: NEW's items gone |
| exit | 1 | P2; NEW later; Alt+F4 | "Save database changes before exiting KeePass?"; Save; the dialog above; Synchronize | a merge |
| **Save As** over the same path | 1 | P2; NEW later; File, Save As, Save to File..., the same path | the file dialog "Save Database" and its "Confirm Save As": "db.kdbx already exists. Do you want to replace it?"; Yes. **No question about the changed file** | **OLD with P2: NEW's items gone** |
| nothing changed, focus | 1 | NEW later; the taskbar brought to the front for 3 s and KeePass back; 5 s | **nothing**; the window still listed OLD's entries (KeePassGate: KPG grant 1, KPG admission 1; X1 in Passwords) | NEW byte for byte |
| nothing changed, lock and unlock | 1 | NEW later; Ctrl+L; Ctrl+L, the key prompt "Open Database - db.kdbx", the password, OK; then P2 and Ctrl+S | no dialog at the lock (nothing to save), the key prompt at the unlock; afterwards the window listed **NEW's entries** (KPG revocation, other-new, no X1); **no dialog at the save** | NEW with P2; nothing lost |
| locked when NEW arrives | 1 | Ctrl+L first; NEW later; unlock as above; P2; Ctrl+S | the same | NEW with P2; nothing lost |
| nothing changed, File, Synchronize with the same file | 1 | NEW later; File, Synchronize, Synchronize with File..., the open file's own path | the file dialog "Synchronize"; then **the dialog above, raised by the synchronize's own save**; Synchronize | NEW merged into the window's copy and saved (new bytes, NEW's items all there); the window then listed NEW's entries |
| nothing changed, Ctrl+S | 1 | NEW later; Ctrl+S; Overwrite | the dialog | **OLD written over NEW**: NEW's items gone, though the window had changed nothing |

**The settings** (Tools, Options, Advanced; each set in `KeePass.config.xml` before the start, and
each tried in the state other than its default, the default state being every case above):

| Setting, as the Options dialog names it | Default | Tried | Counted runs | What KeePass showed | The file afterwards |
| --- | --- | --- | --- | --- | --- |
| "Do not ask whether to synchronize or overwrite; force synchronization" (`SaveForceSync`) | off | on; P2; NEW later; Ctrl+S | 1 | **no dialog** | a merge |
| "Automatically save when closing/locking the database" (`FileClosing.AutoSave`) | off | on; P2; NEW later; Ctrl+L | 1 | no save question; the dialog above; Synchronize | a merge |
| "Automatically save after modifying an entry using the entry editing dialog" (`AutoSaveAfterEntryEdit`) | off | on; NEW later; then P2's dialog, OK | 1 | the dialog above **right after the entry dialog's OK**; Synchronize | a merge |
| "Use file transactions for writing databases" (`UseTransactedFileWrites`) | on | off; P2; NEW later; Ctrl+S; Overwrite | 1 | the dialog | OLD with P2: NEW's items gone |
| "Extra-safe file transactions" (`FileTxExtra`) | off | on; the same; Synchronize | 1 | the dialog | a merge |
| "Use database lock files" (`UseFileLocks`) | off | on; the same; Synchronize | 1 | the dialog | a merge |
| "Verify written file after saving a database" (`VerifyWrittenFileAfterSaving`) | on | off; the same; Overwrite | 1 | the dialog | OLD with P2: NEW's items gone |

The table's runs: 3 + 3 + 3 + 1 × 15 = 24, and 7 setting runs: 31.

**Read** (KeePass 2.62 source, `KeePass\Forms\MainForm.cs:717-784` and
`MainForm_Functions.cs:4664-4726`, `:3607-3695`, `:4042-4173`): every save that goes through
`SaveDatabase` (Ctrl+S, the toolbar, Save All, the save at closing, locking and exiting, the save
after an entry edit, a trigger's save) calls `PreSaveValidate`, which hashes the file as it is on
disk and compares the hash with the one taken at the load or the last save: content, not time or
length, which the time and length variants and the control confirm. `SaveDatabaseAs` does not call
it, which the Save As run confirms. KeePass watches no file (no file system watcher in the source
but the one for its own instance messages), so nothing happens until a save; a lock closes the
database and the unlock opens the file again, which is why a lock and unlock loads the newer file.
Answering Synchronize runs `ImportUtil.Synchronize` with the file's own path, which merges with
the Synchronize method and then saves with the check forced off.

### 2.3 What KeePass's Synchronize at a save keeps, item by item

The three "a, Synchronize" runs, read from the file; all three alike.

| Item | In the file after Synchronize | Held against KeePassLib's Synchronize (`RESULTS.md` 2.1) |
| --- | --- | --- |
| KG3, a write-once `KPG.` entry in KeePassGate | there | the same |
| `KPG.NewKey` | there, its per-key time kept; the file KDBX 4.1 | the same |
| DZ, a deletion record naming no entry | there | the same |
| X1, deleted in NEW, left alone in the window | gone, its record there | the same |
| P1, only the other writer's | `p1-other`, `p1-old` in history | the same |
| P2, only the window's | `p2-window`, `p2-old` in history | the same |
| Q1, the other's older | `q1-window` current, `q1-other` in history | the strictly newer current, the other in history: the same |
| Q2, the other's newer | `q2-other` current, `q2-window` in history | the same |
| Q3, **the same second as files store it** | **`q3-window` current, `q3-other` in history** | differs: at equal times KeePassLib's Synchronize takes the arriving copy and keeps the other nowhere. In the window, the edit's time in memory carries the fraction of the second (`PwEntry.Touch` sets `DateTime.UtcNow`, read), and `TimeUtil.Compare` compares whole ticks (read), so the window's edit is the newer by that fraction; both versions are written at the same second |
| NO, the other's new entry; "window-new" | both there | the same |
| KG1, KG2 | there | the same |

## 3. Measurement 2: KeePassXC, the same

### 3.1 How the runs were made

KeePassXC 2.8.0, the branch's pinned release copied into the work directory, started with
`--config <run>\keepassxc.ini --localconfig <run>\keepassxc-local.ini --allow-screencapture` and
the database's path, so that it read and wrote no configuration of the user's profile (the
profile's `%APPDATA%\KeePassXC` and `%LOCALAPPDATA%\KeePassXC` did not exist before or after the
runs), and unlocked by typing the password into its unlock view. The configuration a run writes
holds the release's own defaults for saving and reloading (2.8.0's `Config.cpp`, **read**: save
after every change, save on exit, save non-data changes, reload on change, safe saves on, backup
off), English, no tray icon, no lock on idle; a case changes one setting. The release asks "Check
for updates on startup?" at a first start whatever the configuration holds; every run answered
No. Its window was maximized.

The two copies are those of 2.1; the window's changes ("full") go through KeePassXC's entry editor
(the row double-clicked, the password typed into the field whose automation identifier ends
`passwordEdit`, OK), and a new entry through the toolbar's New Entry. NEW replaces the file as in
2.1. KeePassXC's own file watcher saw the replacement within about a second in every run. **What is
read back** is the file as the case's flow left it (`after-flow`), before the exit, which saves by
itself under the defaults; where the exit wrote the file again, the table notes it.

Counted runs: **21** here and 1 in section 7. Not counted, nine, kept in `runs\m2-not-counted\`:
the first two attempts of the main case (an entry row off the screen of the window before it was
maximized); an attempt at an edit made at once after the replacement, whose row the reloaded view
no longer showed (replaced by the open editor case); two replacements that found the file held open
by KeePassXC (`MoveFileEx` error 5; the replacement now retries, as a synchronizer does); the first
run of the overwrite case, whose flow waited for a button the first dialog does not have; and three
attempts at the window merge whose password field was looked for by a name it does not have.

### 3.2 What KeePassXC showed, and what the file held afterwards

The dialogs, read through UI automation (pictures `runs\m2\<case>\<case>-1.png`):

> **Reload database** (when the watcher sees the change and the window holds unsaved changes):
> The database file "db.kdbx" was modified externally. How would you like to proceed?
> Merge all changes / Ignore the changes on disk until save / Discard unsaved changes
> [Cancel] [Discard] [Ignore] [Merge]

> **Reload database** (when a save finds the change): the same text with
> Merge all changes then save / Overwrite the changes on disk / Discard unsaved changes
> [Cancel] [Discard] [Overwrite] [Merge]

> **File has changed** (with "reload on change" off): The database file "db.kdbx" was modified
> externally. Do you want to load the changes? [Yes] [No]

| Case | Settings | Counted runs | Sequence | What KeePassXC showed | The file afterwards |
| --- | --- | --- | --- | --- | --- |
| nothing changed | defaults | 1 | NEW arrives; then P2 changed (saved by itself) | **no dialog**: the window reloaded the file by itself | NEW with P2; nothing lost |
| an editor open | defaults | 1 | P2's editor open with a new password typed; NEW arrives; OK three seconds later (saved by itself) | the save-triggered "Reload database"; **Merge** | a merge, saved: NEW's items with P2; X1 back (below) |
| the same | defaults | 1 | the same; **Overwrite** | the same dialog | **OLD with P2: NEW's items gone** |
| Merge | save after every change off | 3 | full changes unsaved; NEW arrives; **Merge**; Ctrl+S | "Reload database" within a second of the replacement; no dialog at Ctrl+S | a merge (table 3.3) |
| Merge, no save | the same | 3 | P2 unsaved; NEW arrives; Merge; exit | the same dialog; the window marked changed | NEW unchanged until the exit, which saved the merge by itself |
| Ignore | the same | 1 | P2 unsaved; NEW arrives; **Ignore**; Ctrl+S | the dialog; **no dialog at Ctrl+S** | **OLD with P2: NEW's items gone** |
| Discard | the same | 1 | P2 unsaved; NEW arrives; **Discard** | the dialog | NEW unchanged; the window's P2 gone |
| Cancel, then Merge | the same | 1 | P2 unsaved; NEW arrives; **Cancel**; Ctrl+S; Merge | the dialog; at Ctrl+S the save-triggered dialog | a merge |
| Cancel, then Overwrite | the same | 2 | the same; **Overwrite** | the same two dialogs | **OLD with P2: NEW's items gone** |
| Cancel, then Overwrite, backup | the same, and "backup database file before saving" on | 1 | the same | the same two dialogs | OLD with P2, NEW's items gone from it; **`db.old.kdbx` beside it holds NEW byte for byte** |
| safe saves off | save after every change off, safe saves off | 1 | P2 unsaved; NEW arrives; Merge; Ctrl+S | the dialog | a merge |
| reload on change off, Yes | reload on change off | 1 | nothing changed; NEW arrives | "File has changed"; **Yes** | NEW; the window reloaded |
| reload on change off, No | the same | 1 | nothing changed; NEW arrives; **No**; Ctrl+S; Merge | "File has changed"; the window then marked changed; at Ctrl+S the save-triggered "Reload database" | a merge, with X1 back |
| Database, Merge From Database... | defaults | 3 | full changes (saved by themselves); Database, Merge From Database..., the file dialog "Merge database", NEW as a file of its own, its password; "Database Merge Confirmation" [Merge] [Abort]; Merge | the merge dialog listing the changes | a merge, saved by itself (table 3.3) |

The table's runs: 1 + 1 + 1 + 3 + 3 + 1 + 1 + 1 + 2 + 1 + 1 + 1 + 1 + 3 = **21**.

**Read** (2.8.0, `src/core/Database.cpp` and `src/gui/DatabaseWidget.cpp`, fetched for this report):
a save compares a hash of the file's first block with the one taken at the load, refuses with
"Database file has unmerged changes." where they differ, and raises the save-triggered dialog;
the watcher raises the other one; a reload is deferred while an entry is being edited, which is
why the open editor's OK led to the save-triggered dialog; Ignore and Overwrite set the database to
ignore changes on disk until its next save, and Overwrite saves at once.

### 3.3 What KeePassXC's merges keep, item by item

| Item | After "Reload database", Merge (the 3 runs with the full changes; the 7 other merge runs alike for the items they hold, the 3 that exited without a save read after the exit) | After Database, Merge From Database (3 runs alike) | `keepassxc-cli merge -s` of the same two copies (3 runs) |
| --- | --- | --- | --- |
| KG3, a write-once `KPG.` entry | there | there | there |
| `KPG.NewKey` | there, its per-key time dropped; the file KDBX 4.0 | **there**: the other file, loaded seconds later, replaced the whole dictionary | **gone**: both loads in the same second, nothing merged (`RESULTS.md` 2.2) |
| DZ, a deletion record naming no entry | there (the file's own records) | **gone**: the window's copy never had it and the merge carries none | gone |
| X1, deleted by the other writer, left alone in the window | **back, beside its record** | **back, no record** | back, no record |
| P1, only the other writer's | the other's, the old in history | the same | the same |
| P2, only the window's | the window's | the same | the same |
| Q1, the other's older | the window's current, the other's in history | the same | the same |
| Q2, the other's newer | the other's current, the window's in history | the same | the same |
| Q3, the same second | **the other's current, the window's change kept nowhere** | **the window's current, the other's kept nowhere** | the window's; the command printed "Entry of Q3[...] contains conflicting changes - conflict resolution may lose data!" |
| NO and the window's new entry | both there | both there | both there |

The window's merge and `keepassxc-cli merge -s` of the same two files differed in the database's
custom data only (`runs\m2\w-merge-<n>\window-vs-cli.diff.txt`, 5 lines in each run): the window's
merge held `KPG.NewKey`, the command's did not. At the same second the copy merged into wins, so
in "Reload database", Merge, where the file on disk is merged into, **the person's own change is
the one lost**.

## 4. Measurement 3: KeePass's entry dialog and a plugin string on a user's entry

### 4.1 What the source holds

**Read** (KeePass 2.62, `KeePass\Forms\PwEntryForm.cs` and `MainForm_Functions.cs:5765-5816`):

- **The dialog takes its copies before it is shown.** `InitEx` clones the entry's strings,
  attachments, auto-type settings and history (`PwEntryForm.cs:231-234`), the load clones the
  whole entry as the state to compare with (`:791`), and the Properties tab clones its custom data
  (`:486`). Nothing in the dialog reads the live entry again.
- **OK assigns every copy back** (`SaveEntry`, `:1066-1152`): the history, then a backup of the
  current state, then the strings, icon, expiry, attachments, colors, tags (parsed from the tags
  box), override URL, custom data and auto-type, then the modification time. **With nothing
  changed** against the clone taken at the load, it restores that clone's modification time and
  removes the backup it made (`:1118-1135`), and the main window counts the edit as no change
  (`bMod = bOK && dlg.HasModifiedEntry`, `MainForm_Functions.cs:5800`), so the database is not
  marked modified.
- **Cancel** writes nothing to the entry.
- So a string put on the live entry while its dialog is open is taken away by OK, with nothing
  touched as with a field edited; and the entry's own modification time goes back to what it was
  when the dialog opened.

### 4.2 The dialog, run in the window with a writing plugin

`scripts\M3.ps1`, in the KeePass copy `kp-write` with the probe plugin `DeskWrite`
(`plugin\DeskWrite`, a plain DLL loaded by KeePass like any plugin, not part of the product): a
timer on KeePass's own thread, which the dialog's message loop keeps running, takes one command
from a file and writes to the live entry while its dialog is open, as a plugin putting a
reference on a user's entry would: `Strings.Set`, the entry touched as modified, the database
marked modified, the list refreshed, and **saved at once** through `MainForm.SaveDatabase`, as the
product saves every write. The file is read back right after the plugin's save. The dialog is then
closed by OK with nothing touched, by OK after the user name was changed, or by Cancel; the plugin
reports what the entry holds in memory and whether the database counts as modified; Ctrl+S; the
file is read back.

The database (probe `m3base`): Passwords holds **U1** "Mail account" with `KPG.Ref` plain, the
user's own string `Extra`, an attachment, a tag, a custom data item of another plugin and two
history items; **U2** "Bank" with `KPG.Ref` protected; **U3** "Shop" without `KPG.Ref`.
KeePassGate holds two store entries. Every value is a token no other field holds.

Counted runs: **10**, one per row; none failed.

| Case | Before the dialog | While the dialog is open | Closed by | The file right after the plugin's save | In memory after the dialog | Window title after the dialog | The file after Ctrl+S |
| --- | --- | --- | --- | --- | --- | --- | --- |
| a1 | U1 has `KPG.Ref` | nothing | OK, nothing touched | | `KPG.Ref` there; history 2; time unchanged | `db.kdbx - KeePass`, not marked | `KPG.Ref` there; **no history item added, the modification time not moved**; every other carrier as before |
| a2 | the same | nothing | OK, user name changed | | `KPG.Ref` there; history 3; time moved; database modified | `db.kdbx*`, marked | `KPG.Ref` there; one history item added holding the whole earlier version, `KPG.Ref`, attachment, tag and custom data included; time moved |
| a3 | the same | nothing | Cancel | | `KPG.Ref` there; history 2; time unchanged; not modified | not marked | unchanged |
| b1 | U3 has none | the plugin adds `KPG.Ref` and saves | OK, nothing touched | `KPG.Ref` there | **`KPG.Ref` gone**; history 0; **time back to the time before the plugin's write**; database not modified | not marked | **`KPG.Ref` gone**, no trace in the entry or its history |
| b2 | the same | the same | OK, user name changed | `KPG.Ref` there | `KPG.Ref` gone; history 1; time moved; database modified | marked | **`KPG.Ref` gone from the entry**; it survives only in the history item made at OK, which holds the live entry as it was with the plugin's string |
| b3 | the same | the same | Cancel | `KPG.Ref` there | `KPG.Ref` there; history 0; the time of the plugin's write; not modified (the plugin had saved) | not marked | `KPG.Ref` there |
| b4 | the same | the plugin adds `KPG.Ref` **without saving** | OK, nothing touched | (not saved; the title marked `db.kdbx*` from the plugin's write) | `KPG.Ref` gone; time back to before the write; the database still marked modified by the plugin's own write | marked | `KPG.Ref` never in the file |
| c1 | U1 has `KPG.Ref` | the plugin changes it and saves | OK, nothing touched | the changed value | **the value before the plugin's change**; time back to before it; database not modified | not marked | **the value before the change**; no history item |
| c2 | the same | the same | OK, user name changed | the changed value | the value before the change; history 3; time moved; database modified | marked | the value before the change in the entry; the changed value only in the history item made at OK |
| c3 | the same | the same | Cancel | the changed value | the changed value; history 2; the time of the plugin's write; not modified | not marked | the changed value |

In every row the entry's attachment, tag, other plugin's custom data and earlier history items came
out as they went in. The plugin's own save left the window unmarked in every row that saved.

**What this means for a plugin string on a user's entry**: a string the plugin puts on, or changes
on, a user's entry while that entry's dialog is open is undone by the dialog's OK, whatever the
person did in it; the database is then not even marked as changed when nothing was touched, so
the undone state reaches the file at the next save of anything, by anybody, without a question.
Only Cancel keeps the plugin's write. This is the `GroupForm` hazard of the product's own notes,
on the entry dialog, for strings as well as custom data.

### 4.3 What a person sees and does with `KPG.Ref`, run in the window

`scripts\M3v.ps1`, in the KeePass copy `kp` without any plugin, on a fresh copy of the same
database per case. Counted runs: **16**: 12 for the rows of the first table (one run reads the entry
list and the three view panes, one both Advanced tabs, one the three auto-type rows) and 4 for the
exports (`runs\m3v\export-part1` to `part4`). Not counted,
13, kept in `runs\m3v-not-counted\`: the first entry view case, which read the text of a window
other than the entry view; two export runs whose follow-up dialogs the driver did not answer
(a menu's window taken for the export dialog, the key prompt's "Confirm Export" button); four
Duplicate Entry runs (a shortcut that did not reach the list, check boxes read through a message
WinForms does not answer, and one whose loop then pressed Cancel); a Move to Group through a menu
item off the screen; two print runs (the menu's submenu, the check boxes as before); two auto-type
runs whose target window's title lost its space on the command line; and an HTML export run whose
read the file's lock refused, made again with the read moved after KeePass's stop.

| What | How it was run | What the window showed or the file held |
| --- | --- | --- |
| The entry list | Passwords shown; the column headers read through the accessibility interface | the columns Title, User Name, Password, URL, Notes; no custom string is a column by default |
| The entry view pane, U1 | the row clicked; the pane's text read | "... Extra: zqxextra-u1. **KPG.Ref: zqxref-u1 {"rule":"KG1"}.**" after the standard fields: a custom string is shown with its name and value |
| The entry view pane, U2 | the same | "**KPG.Ref: \*\*\*\*\*\*\*\*.**": a protected custom string is shown by name, its value hidden |
| The entry view pane, a store entry | KeePassGate shown, "KPG grant 1" clicked | "Group: KeePassGate. Title: KPG grant 1. ... KPG.Grant: zqxstore-grant {...}. KPG.Secret: \*\*\*\*\*\*\*\*." |
| Quick search | each token typed into the toolbar's search box, Enter, the result list read | `zqxref-u1` found Mail account; **`zqxref-u2-protected` found Bank** (a protected string's value is searched); `zqxextra-u1` found Mail account; `Mail account` found it; `zqxstore-grant`, `zqxstore-secret`, `zqxstore-adm` and the title words `KPG grant` found **nothing**: the store group's entries stay out of quick search; `KPG.Ref` (a string's name), `zqxcd-u1` (custom data) and `zqxattach-u1` (an attachment's content) found nothing |
| The Advanced tab, U1 | the dialog opened, Advanced clicked, the string list read | the strings `Extra` and `KPG.Ref` listed with their values ("Value: zqxref-u1 {...}"); with `KPG.Ref` selected the buttons Add, Edit, Delete and More enabled |
| The Advanced tab, U2 | the same | `KPG.Ref` listed, "Value: \*\*\*\*\*\*\*\*"; Edit and Delete enabled |
| Edited on the Advanced tab | `KPG.Ref` selected, Edit, the dialog "Edit Entry String", a new value typed, OK, OK, Ctrl+S | the file holds the new value, `zqxref-u1-edited-by-hand`; a history item added |
| Deleted on the Advanced tab | `KPG.Ref` selected, Delete (no question), OK, Ctrl+S | the file holds U1 **without `KPG.Ref`**; a history item added |
| Duplicate Entry, as offered | U1 selected, Entry, Duplicate Entry...; "Duplication Options" offered **Append "- Copy" to entry titles** and **Copy history** checked, **Replace user names and passwords by references** not; OK; Ctrl+S | a second entry "Mail account - Copy" under **a new identifier**, holding `KPG.Ref` with the same value and both history items |
| Duplicate Entry, every option | the same with the third box checked too | the copy's user name and password are field references (`{REF:U@I:...}`); **`KPG.Ref` copied as it is**, not as a reference |
| Moved to another group | U1 dragged onto KeePassGate in the tree | U1 in KeePassGate, `KPG.Ref` and everything else kept, same identifier |
| Copied into a second database window | a second KeePass with another database beside the first; Entry, Data Exchange, Copy Entry (Encrypted) in the first; the group Imported selected in the second, Entry, Data Exchange, Paste Entry; Ctrl+S in the second | the second file holds "Mail account" under **a new identifier**, with `KPG.Ref` and both history items |
| Dragged into a second database window | U1's row dragged onto the group Imported of the second window | **nothing arrived**: the second window listed no entry, its file held none |
| Print | File, Print, Print...; the preview pictured; the Layout tab's check boxes read | the preview lists every group and entry in the default **Tables** layout with Title, User Name, Password and Notes: **the store group and its entries are printed** (their titles), no custom string is; on the Layout tab "Custom string fields" is unchecked and **disabled** in the Tables layout (it applies to the Blocks layout only), as is "Auto-Type" |
| Auto-Type of the selected entry | a target window of the probe's own in front, then KeePass; U1 selected; Ctrl+V | the target received `alice-invented{TAB}u1-pw-invented{ENTER}`: the default sequence, no custom string |
| Global auto-type, a store entry's title | the target window titled "KPG grant 1" (a store entry's title) in front; Ctrl+Alt+A | **nothing typed and no window shown**: the store group's entries, auto-type off, match no window |
| Global auto-type, a user's title | the target titled "Mail account"; Ctrl+Alt+A | the default sequence of U1 typed (its first character lost to the hot key's modifiers) |

**The exports** (File, Export...; the dialog "Export File/Data" offers ten formats; each run on its
own: the format selected, a file named, OK; KeePass asked for the master key again in "Enter
Current Master Key (Export) - db.kdbx" with the button "Confirm Export", answered with the
password; the file then searched for every token):

| Format | The file written | `KPG.` strings in it |
| --- | --- | --- |
| KeePass CSV (1.x) | 5 columns: Account, Login Name, Password, Web Site, Comments; a row per entry, the store's entries by title | none |
| KeePass KDB (1.x) | a KeePass 1.x file, encrypted, 1,804 bytes | not read: KeePassLib 2.62 opens no 1.x file without KeePass's native library |
| KeePass KDBX (2.x) | an encrypted database, read with the probe | **every one**, the store's entries and the other plugin's custom data included |
| KeePass KDBX (2.34, Old Format) | the same, KDBX 3.1 | every string; **the entry's custom data dropped** |
| KeePass XML (2.x) | the database as unencrypted XML | **every one, protected values in clear** (`<Value ProtectInMemory="True">zqxref-u2-protected</Value>`), custom data included |
| Customizable HTML File | after the key, the dialog "Export To HTML" (the print dialog's preview, button Export); **KeePass held the written file open, with no sharing, for as long as it ran** (5 runs), so it was read after KeePass was stopped (`export-part4`) | none in the default Tables layout; the store's group and titles listed, the user's passwords in clear |
| Transform using XSL Stylesheet | with KeePass's own `KDBX_DetailsFull_HTML.xsl` | **every string, protected values in clear**; no custom data |
| Windows Favorites (Folder 'KeePass') | **into the user profile's Favorites folder**, not the file named: a folder `KeePass`, found empty after the run | none |
| Windows Favorites (Root Directory) | **into the user profile's Favorites folder**: `Passwords\Mail account.url`, `Bank.url`, `Shop.url`, each an Internet shortcut to the entry's URL | none; the store's entries, having no URL, not exported. The run removed both folders afterwards (copies kept in the evidence) |
| Mozilla Bookmarks HTML | a bookmark file of the entries with a URL | none |

## 5. Measurement 4: KeePass's maintenance commands on a store made of entries

### 5.1 The database and the runs

Made by the probe (`m4base`), KeePassLib 2.62: the group **KeePassGate** (KG, searching and
auto-type off) with the write-once entries S1 to S4, an empty subgroup **Archive** (KGA), and
**S1c**, S1's content under another identifier and a later time; **S6** in the recycle bin with
**S6c**, its content later, in KG; **S7** in KG with **S7c**, its content later, in the recycle
bin; **S5** alone in the recycle bin. Passwords holds U1 with history items 533, 107 and 6 days
before the runs and U2. "Empty group" is empty. Deletion records, each pair at 2025-01-01 and
2026-11-15: **W-old, W-new** naming identifiers that never existed (as a witness does), **R-old,
R-new** of entries deleted for real, **C-old (S3), C-new (S4)** naming entries that are present (as
after a merge that applied no record). One custom icon is used by S2, one by nothing; the database
custom data holds `KPG.Profiles`.

`scripts\M4.ps1`: each case on a fresh copy in `kp`, the command through the menus or keys a
person uses, its dialogs read, pictured and answered, Ctrl+S, the file dumped and diffed against
the base's dump (`runs\m4\<case>-1\diff-vs-base.txt`; the dump holds each node's place among its
siblings). Counted runs: **17**, one per row. Not counted, 32, kept in `runs\m4-not-counted\`: a
first batch of 17, all repeated because its dump did not yet hold the order of siblings, so that a
reordering read as no change (in it, too, the group deletes went to the main window rather than the
focused tree, the plugin data row was selected without a click, and three cases looked for menu
items by names the menu does not show); 12 runs of a second batch, 11 stopped on a cmdlet the shell
they were started from did not load and one stopped by hand with it; and the three permanent deletes
whose "Delete" button the run did not press.

### 5.2 What each command did to the store

| Command | What KeePass showed | Deletion records afterwards | Store entries and groups afterwards |
| --- | --- | --- | --- |
| Tools, Database Tools, Database Maintenance: "Delete history entries older than (days)" **365** (the database's own value), Delete, Close | the dialog; no question at Delete | the 6, unchanged | unchanged; U1's history item of 533 days deleted, those of 107 and 6 days kept |
| the same dialog: "Deleted objects information", Delete, Close | the warning on the page: "Warning! After removing this information, deleted objects (groups, entries, ...) may reappear when synchronizing the current database with another one (which still contains the objects)."; no question at Delete | **all 6 removed**, old and new, witness-like and real alike | unchanged |
| the same dialog, Plugin Data tab: `KPG.Profiles` selected, Delete, Close | the list of the database's custom data keys | unchanged | `KPG.Profiles` gone from the database's custom data; with the per-key time it carried gone, the file was written as **KDBX 4.0** and **the previous parent of the three entries in the recycle bin was dropped** |
| Tools, Database Tools, **Delete Duplicate Entries** | "3 object(s) deleted." | **3 added** (S1, S6, S7c), at the time of the command | **S1** deleted (the older of S1 and S1c); **S6** (the older, in the bin) deleted, S6c kept; **S7c** (the newer, but in the bin) deleted, S7 kept |
| Tools, Database Tools, **Delete Empty Groups** | "2 object(s) deleted." | 2 added (Archive, Empty group) | the store's **empty subgroup Archive deleted** with "Empty group"; KeePassGate itself kept, having entries |
| Tools, Database Tools, Delete Unused Custom Icons | "1 object(s) deleted." | 1 added, for the icon | the unused icon gone, S2's kept |
| KeePassGate selected in the tree, Delete key | "Are you sure you want to move the selected group to the recycle bin? Deleting a group will also delete all entries and subgroups in that group." [Yes] [No]; Yes | unchanged | **KeePassGate moved into the recycle bin** with every entry and subgroup; no record |
| KeePassGate selected, **Shift+Delete** | "Are you sure you want to permanently delete the selected group?" [Delete] [Cancel]; Delete | **9 added**: KeePassGate, Archive and its 7 entries | the store group and everything in it gone; the bin's S5, S6, S7c kept |
| "KPG grant 1" (S1) selected, Delete key | "Are you sure you want to move the selected entry to the recycle bin? - KPG grant 1" [Yes] [No], with "Do not show this dialog again; always 'Yes'."; Yes | unchanged | S1 moved into the recycle bin, its previous parent recorded |
| S1 selected, **Shift+Delete** | "Are you sure you want to permanently delete the selected entry? - KPG grant 1" [Delete] [Cancel]; Delete | 1 added (S1) | S1 gone |
| the recycle bin selected, Group, **Empty Recycle Bin** | "Are you sure you want to permanently delete the items? - KPG grant 5 ..." [Delete] [Cancel]; Delete | 3 added (S5, S6, S7c) | the store entries in the bin gone; their copies S6c and S7 in KeePassGate kept |
| KeePassGate selected, Group, Duplicate Group... | "Duplication Options"; OK | unchanged | a second group **KeePassGate** under a new identifier, **searching and auto-type off as the original**, its 7 entries copied under new identifiers with " - Copy" appended to their titles, and a copy of Archive |
| S1 selected, Entry, Duplicate Entry... | "Duplication Options" (Append "- Copy" and Copy history checked); OK | unchanged | "KPG grant 1 - Copy" under a new identifier in KeePassGate, its `KPG.` strings copied |
| the root selected, Group, Rearrange, Sort Direct Subgroups | none | unchanged | the root's subgroups sorted by name: KeePassGate moved from the first place to the second |
| Passwords selected, Group, Rearrange, Move Group One Up | none | unchanged | Passwords before KeePassGate |
| S1 selected, Entry, Rearrange, Move Entry One Down (by its access keys) | none | unchanged | S1 and S2 swapped places in KeePassGate |
| S1 dragged onto Passwords in the tree | none | unchanged | S1 in Passwords, its strings kept |

**XML Replace** (Tools, Database Tools, XML Replace...) was not run. **Read**
(`KeePass\Util\XmlUtil.Replace.cs:135-223`): it selects nodes of the database's whole XML by an
XPath the person types, removes them or replaces their text (literal or regular expression,
case-sensitive or not), loads the result as a second database and merges it back with the
Synchronize method; nothing restricts the XPath to user data, so a `KPG.` string, an entry of the
store or a deletion record can be changed or removed by it like any other node.

**File, Import and File, Synchronize** in the window are measurement 5's cases 3 and 1 (section 6).
Import, KeePass KDBX (2.x), asks the other file's key in "Open Database - other.kdbx" and then
"Import Behavior": "The file format that you have selected to import supports group and/or entry
IDs. Please choose an import behavior.", with **Create new IDs preselected** (5 runs) above Keep
existing, Overwrite existing, Overwrite if newer, and Overwrite if newer and apply deletions, each
with a line of its own text. The result is not saved by itself. In memory, by the plugin's count
at the next event (entries, deletion records, entries of the group KeePassGate): Create new IDs
19, 1, 2, the open file's 9 entries and the other file's 10; Keep existing, Overwrite existing and
Overwrite if newer 11, 1, 3 each, the two entries new to the open file (KG3 among them) added,
**no deletion record taken over** and X1 kept; Overwrite if newer and apply deletions 10, 3, 3,
the synchronize's result. Synchronize with File offers **no method**: its only window was the file
dialog "Synchronize" (the other file opened with the open database's key), and it merged, saved the
open file and copied it over the other file with no question and no mark left on the title (3
runs, section 6).

## 6. Measurement 5: what a plugin observes around KeePass's synchronize, import and save

### 6.1 The logging plugin and the runs

`plugin\DeskLog` (`DeskLog.DeskLogExt` in `DeskLog.dll`, product name "KeePass Plugin"), a probe
and not part of the product, in the KeePass copy `kp-log`. It subscribes to
`Program.TriggerSystem.RaisingEvent`, writing every trigger event by the name of its identifier in
`EcasEventIDs`, and to every file event of the main window: `FileCreated`, `FileOpened`,
`FileClosingPre`, `FileClosingPost`, `FileClosed`, `FileSavingPre`, `FileSaving`, `FileSaved`. Each
line holds the time, the event, the database's file name, `PwDatabase.Modified`, a fingerprint of
the database in memory (entries, deletion records, entries of the group KeePassGate) and the
length and SHA-256 prefix of the file on disk at that moment. **Behind a switch** it adds one entry
"KPG marker" to KeePassGate at the first `FileSavingPre` that comes while a synchronize runs
(between the trigger events `SynchronizingDatabaseFile` and `SynchronizedDatabaseFile`) or nested
in another save's `FileSavingPre`. The open file is OLD (9 entries, 1 record, 2 in KeePassGate),
the other is NEW-fixed (10, 3, 3). `scripts\M5.ps1`, each case from a fresh start; both files
dumped afterwards. Counted runs: **14**, one per case. `Trigger.TimePeriodic`, raised about once a
second in some runs, is left out of the sequences below; the logs hold it.

**KeePass moved the trigger case's trigger into an enforced configuration.** At its start KeePass
2.62 moves an enabled trigger found in a non-enforced configuration into
`KeePass.config.enforced.xml` beside the executable (`KeePass\Program.cs:656-662`,
`App\Configuration\AppConfigEx.Sections.cs`, read), and that file outlived the run: the runs of
`kp-log` that followed until it was removed synchronized at start with the trigger case's other
file. Those runs, a URL synchronize, the opening of a merged file and two import attempts, are not
counted and were repeated after the file was removed; the script now removes it before every run
and after the trigger case (the file KeePass wrote is kept in `runs\m5\trigger-sync-1\`). Not
counted, 20 in all, kept in `runs\m5-not-counted\`: those four; 11 further import attempts whose
format row or OK button the driver did not press (a format selected through the accessibility
interface, an OK pressed by `BM_CLICK`, both ignored by the dialog); and 5 that stopped on a cmdlet
the shell they were started from did not load.

### 6.2 The order of events, and the fingerprint at each

The fingerprint is written entries/records/KeePassGate, then the file on disk: **OLD** (3,413
bytes, `2ff7a751a8`), **NEW** (`74dd75d724`), or **new** for a file the save just wrote.

| Case | Events in order, with the fingerprint in memory and on disk | The marker | The files afterwards |
| --- | --- | --- | --- |
| 1, File, Synchronize, Synchronize with File (NEW as the other file) | `FileOpened` 9/1/2 OLD; `Trigger.OpenedDatabaseFile`; `Trigger.AppLoadPost`; then the command: **`Trigger.SynchronizingDatabaseFile` 9/1/2 OLD**; **`FileSavingPre` 10/3/3, modified, OLD on disk**; `FileSaving` 10/3/3 OLD; `Trigger.SavingDatabaseFile` 10/3/3 OLD; **`FileSaved` 10/3/3 new**; `Trigger.SavedDatabaseFile`; **`Trigger.SynchronizedDatabaseFile` 10/3/3 new** | off | both files the same bytes: the merge, saved into the open file and copied over the other, **no event for the copy** |
| 1, the same, marker on | the same; `MarkerAdded` at `FileSavingPre`, 11/3/4 from there on | added | **the marker in both files** |
| 2, the same with the trigger system off (`TriggerSystem/Enabled` false in the configuration, the check box of Tools, Triggers...), marker on | `FileOpened` 9/1/2 OLD; **`FileSavingPre` 10/3/3, modified, OLD on disk**; `FileSaving`; `FileSaved` 10/3/3 new. **No trigger event at all** | **not added**: nothing told the plugin a synchronize was running | both files the same bytes, the merge, no marker |
| 3, File, Import..., KeePass KDBX (2.x), NEW, its key ("Open Database - other.kdbx"), "Import Behavior" with **Create new IDs preselected** in all 5 runs, then each of the 5 methods, OK; the exit answered "Discard changes"; marker on | `FileOpened` 9/1/2 OLD; `Trigger.OpenedDatabaseFile`; `Trigger.AppLoadPost`; then **nothing** for the import: no trigger event and no file event. The next lines are the exit's `Trigger.ClosingDatabaseFilePre` and `FileClosingPre`, modified, OLD on disk, holding the import's result: Create new IDs **19/1/2**; Keep existing, Overwrite existing and Overwrite if newer **11/1/3** each; Overwrite if newer and apply deletions **10/3/3** | not added | the open file OLD, byte for byte: the import is not saved by itself (the title showed `db.kdbx*`) |
| 4, a save over a changed file: P2 edited, NEW replaces the file, Ctrl+S, Synchronize; marker off | **`FileSavingPre` 9/1/2, modified, NEW on disk** (the save's own, before the question); the dialog; `Trigger.SynchronizingDatabaseFile` 9/1/2 NEW; **a second `FileSavingPre` 10/3/3, nested**; `FileSaving`; `Trigger.SavingDatabaseFile`; `FileSaved` 10/3/3 new; `Trigger.SavedDatabaseFile`; `Trigger.SynchronizedDatabaseFile`. **No `FileSaving` and no `FileSaved` for the first save** | off | the merge with P2 |
| 4, marker on | the same; `MarkerAdded` at the nested `FileSavingPre` | added | the marker in the file |
| 4, marker on, trigger system off | `FileSavingPre` 9/1/2 NEW; the nested `FileSavingPre` 10/3/3; `MarkerAdded`; `FileSaving`; `FileSaved`; no trigger event | added, from the nesting alone | the marker in the file |
| 5, a trigger: "Application started and ready", action "Synchronize active database with a file/URL", NEW; marker on | `FileOpened`; `Trigger.OpenedDatabaseFile`; `Trigger.AppLoadPost`; then the sequence of case 1 with `MarkerAdded` | added | both files the same bytes, the marker in both |
| 5, File, Synchronize, Synchronize with URL..., "Open From URL" given `file:///` and NEW's path; marker on | the sequence of case 1 with `MarkerAdded`; the file on disk OLD until `FileSaved` | added | both files the same bytes, the marker in both: the other file written through the URL |
| 6, opening a file that KeePassLib's Synchronize had merged from OLD and NEW; marker on | `FileOpened` 10/3/3; `Trigger.OpenedDatabaseFile`; `Trigger.AppLoadPost`; nothing else | not added | unchanged |

The table's runs: 3 (case 1 and 2) + 5 (case 3) + 3 (case 4) + 2 (case 5) + 1 (case 6) = 14.

### 6.3 What a plugin can observe, and what it cannot

**Before and after a merge made in the KeePass it runs in**, a plugin sees
`SynchronizingDatabaseFile` before the merge and `SynchronizedDatabaseFile` after the save, both
trigger events, and between them the synchronize's own save. **The merged state is in memory and nothing of it is on disk at
`FileSavingPre` and at `FileSaving` of that save**: in every synchronize run the fingerprint moved
from the open copy's to the merge's between `SynchronizingDatabaseFile` and the save's
`FileSavingPre`, and the file on disk changed only at `FileSaved`. A change the plugin made at that
`FileSavingPre` reached the open file and, where the synchronize was with another file, that file
too, since KeePass copies the saved file over it. A save over a changed file answered with
Synchronize shows two `FileSavingPre` events, the outer one before the question with nothing
merged, and no `FileSaved` for it.

**What it cannot observe**: a merge made by another program (opening such a file raises
`FileOpened` and nothing that shows it was merged); an import, which raises no trigger event and no
file event of its own and is visible only as a changed fingerprint at the next event; and, with
the trigger system turned off, any synchronize as such: only the save events arrive, and a
synchronize's save then looks like any other save but for its nesting inside a save over a
changed file. **Nothing in these events lets a plugin change how KeePass merges**: they arrive
before the merge starts or after it has ended, and `PwDatabase.MergeIn` has no hook (read).

## 7. What a person sees of the store

**KeePass 2.62** (measurements 1, 3 and 4; every row measured in the window):

- **The tree and the list.** The group KeePassGate is an ordinary group in the tree, with the
  store's entries listed by title (`KPG grant 1`, `KPG admission 1`, ...); nothing marks it as the
  plugin's. Selecting an entry shows its `KPG.` strings in the entry view pane by name and value,
  a protected one as `********` (pictures `runs\m3v\view\view-*.png`).
- **Quick search** never listed a store entry, by a `KPG.` value or by a word of its title, while
  it found a user's entry by the value of its `KPG.Ref`, protected or not: the store group's
  searching flag, off, keeps its entries out.
- **Global auto-type** typed nothing and showed nothing for a window whose title is a store
  entry's title, where the same hot key typed a user's entry into a window with that entry's title:
  the store group's auto-type flag, off, keeps its entries out.
- **Print** lists the store group and its entries' titles in the default layout, no `KPG.` string.
- **The exports** are in 4.3: the XML export holds every `KPG.` string in clear, protected ones
  included.
- **A person can edit and delete a `KPG.` string** on the Advanced tab, with no question, and
  delete, move, duplicate and copy the store's entries like any other (sections 4 and 5).

**KeePassXC 2.8.0** (`scripts\M2v.ps1`, 1 run, on the same database as 4.3; pictures in
`runs\m2v\look-1\`):

- **The tree and the list.** KeePassGate is an ordinary group with its entries listed by title. The
  entry preview's Advanced tab lists the `KPG.` strings as attributes with their values, a
  protected one hidden behind a reveal button (`preview-KPGgrant1.png`).
- **Search** (the toolbar's box): `zqxref-u1`, `zqxref-u2-protected`, `zqxstore-grant`,
  `zqxstore-secret` and `KPG grant` found nothing; `Mail account` found the user's entry. A custom
  string's value is not searched by a plain term, and the store's entries stayed out of search.
- **Global auto-type**, with Ctrl+Alt+A set as its hot key in the run's configuration (2.8.0 sets
  none by default): a window titled "KPG grant 1" made KeePassXC show "Auto-Type - KeePassXC" with
  "Double click a row to perform Auto-Type or find an entry using the search:" listing the three
  user's entries and **no store entry**; a window titled "Mail account" showed the same dialog with
  that entry alone. Nothing was typed in either: the dialog was closed with Esc.

## 8. Not run, and why

- **KeePassXC 2.7.12.** Provided and copied into the work directory, and `scripts\M2.ps1` takes a
  case's release, but no case was run on it: the 2.8.0 runs and measurement 5 took the session.
  Every KeePassXC row of this report is 2.8.0's.
- **KeePassXC's lock, close and Save As with a changed file, and its "use alternative saving
  method" setting** (`UseDirectWriteSaves`). Measurement 2 ran the reload and save paths under the
  defaults and with one setting changed at a time (save after every change, reload on change, safe
  saves, backup); the lock and close questions and the direct write were not reached.
- **XML Replace** (5.2). Not run; its source was read instead, and the reading answers the
  question asked of it, whether it can touch a `KPG.` string: it can, as any node of the
  database's XML. A run would show only what a person types into its XPath box.
- **The KDB (1.x) export's content** (4.3). Written and its size recorded; not read, since
  KeePassLib 2.62 opens a 1.x file only through KeePass's native library, which the probe does not
  load.
- **What each import method kept item by item.** The imports were not saved (KeePass does not save
  them by itself and each run's exit discarded them), so what is known is the plugin's count in
  memory at the next event (5.2, 6.2), not a dump of a file.
- **Synchronize with a file whose key differs**, and Synchronize with URL over a network protocol:
  the other file always opened with the open database's key, and the URL was a `file:///` one.
- **The entry dialog with a plugin write to the attachments, the tags or the custom data** of the
  open entry. The cases wrote and changed a string; the source (4.1) clones those carriers the same
  way, and the rows' other carriers came out as they went in.
- **Anything on a printer.** Print was run to its preview; nothing was printed.

## 9. What can be relied on

### 9.1 KeePass 2.62

**Criterion 2, in one sentence**: KeePass compares the file's content with what it loaded at every
save that goes through its save command (Ctrl+S, the toolbar, saving at closing, locking and
exiting, the save after an entry edit) and, where it changed, asks "Overwrite the existing file?"
with [Synchronize] [Overwrite] [Cancel], where Synchronize merges as KeePassLib's Synchronize does
and saves, while Save As over the same path writes without asking (section 2).

**Every sequence in which a person at the window loses another writer's changes or undoes
something the store depends on**, as measured:

1. **Overwrite** in that dialog, at any save that asks (Ctrl+S, the toolbar, closing, locking,
   exiting): every change of the other writer is gone, its write-once `KPG.` entries, its
   database custom data key and its deletion records among them; 3 + 6 runs, and the same with
   nothing changed in the window (1 run). KeePass names the loss in the button's text: "Changes
   made by the other user will be lost."
2. **Save As** (File, Save As, Save to File...) over the open file's own path: the same loss with
   no question about the changed file, only the file dialog's "already exists. Do you want to
   replace it?" (1 run).
3. **The entry dialog's OK** while the plugin wrote to that entry (section 4.2): the plugin's
   string is taken away, with or without a change by the person, and reaches the file at the next
   save of anything (2 + 2 runs).
4. **Database Maintenance, "Deleted objects information", Delete**: every deletion record goes,
   with no question, witnesses included (1 run).
5. **Delete Duplicate Entries** deletes a store entry that has a copy under another identifier: the
   older of the two, or the one in the recycle bin (1 run); **Delete Empty Groups** deletes an
   empty group of the store (1 run).
6. **A person's own deletes and edits**: Shift+Delete of the store group or a store entry, Empty
   Recycle Bin, Delete of a `KPG.` string on the Advanced tab, an edit of its value; each with
   KeePass's ordinary question or none (section 5).
7. **File, Import of another copy** (5.2): with the preselected Create new IDs every entry of the
   other copy arrives a second time under a new identifier, the store's entries among them, so the
   store holds records it already had under other identifiers (1 run); Keep existing, Overwrite
   existing and Overwrite if newer take over no deletion record of the other copy (3 runs). Only
   "Overwrite if newer and apply deletions" gave the synchronize's result (1 run).

**Not in the list, since nothing of the other writer is lost**: Cancel in that dialog leaves the
file the other writer's and the window's changes in memory, which an exit answered "Discard
changes" then loses (3 runs); and Synchronize keeps the other writer's version of an entry both
changed in the same second as a history item (3 runs).

### 9.2 KeePassXC 2.8.0

**Criterion 2, in one sentence**: KeePassXC watches the file and within about a second of a change
reloads it by itself when the window holds no unsaved change, and otherwise asks "Reload database"
with [Merge] [Ignore] [Discard] [Cancel], or at a save that finds the file changed the same with
[Overwrite] in place of [Ignore], where Merge merges the window's copy into the file's and saves at
the next save (or by itself under the defaults), so it merges or asks before writing over a changed
file in every sequence run (section 3).

**Every sequence in which a person at the window loses another writer's changes or undoes
something the store depends on**, as measured:

1. **Overwrite** in the save-triggered "Reload database": every change of the other writer is gone,
   its write-once `KPG.` entry, its database custom data key and its deletion records among them
   (2 runs after Cancel and Ctrl+S, 1 with an entry editor open and the defaults); with "backup
   database file before saving" on, the overwritten file stays beside it as `db.old.kdbx` (1 run).
2. **Ignore**, then a save: the same loss, **with no further question** at the save (1 run).
3. **Merge**, in either dialog or through Database, Merge From Database: an entry the other writer
   deleted comes back, beside its deletion record after "Reload database" and without one after
   Merge From Database (10 and 3 runs); Merge From Database carries no deletion record, so the
   other writer's record of an identifier nobody holds is not in the window's file (3 runs); at the
   same second the version of the copy merged into wins and the other is kept nowhere, which in
   "Reload database" is the person's own change (3 runs).

**Not in the list, since nothing of the other writer is lost**: Discard keeps the other writer's
file and loses the window's own unsaved changes (1 run).

A store of write-once entries comes through every Merge: no `KPG.` entry of either copy was lost in
any of the 13 merge runs. What a merge loses is a deletion record carried in the other direction,
which the store uses as a second witness (`EVALUATION.md` 2.3), and a deletion, which the store
never uses to take something away.

### 9.3 What the entry dialog means for a plugin string on a user's entry

A string the plugin writes onto a user's entry is safe from KeePass's entry dialog only when no
dialog of that entry is open: the dialog takes its copy before it is shown and puts the copy back
at OK, so the plugin's write disappears from memory at OK, with no change by the person and no
mark on the window, and from the file at the next save (section 4.2). A string that is already on
the entry when the dialog opens comes through every close of the dialog. The person can see, edit
and delete it on the Advanced tab, quick search finds the entry by its value, Duplicate Entry and
the clipboard copy carry it to an entry under a new identifier, and the XML export and the
detailed XSL transformation print it in clear even when it is protected (section 4.3).
