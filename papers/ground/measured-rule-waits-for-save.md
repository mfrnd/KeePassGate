# Measured: a rule written at a node dialog's OK that waits for the operator's save

Run 2026-11-19 (UTC) on this machine's desktop, in a throwaway KeePass 2.62 with the plugin at
master `42bec0ad`, branch `measure/rules-wait-for-save`, in a worktree of that branch.
The design under test is section 7 of the plan for the rules in the store ("The write at a node
dialog's OK, step by step"), `STORE.md` §629, §627, §626 and §644 items 6, 11 (row 15 of §639)
and 14, and rows 11, 15 and 18 of part 3 of the plan's questions.

**In short.** A change the plugin writes into its store as a group or entry dialog closes with OK
stays marked once KeePass has handled the dialog's result, for both dialogs, also where KeePass
itself counts the entry as unchanged. KeePass asks before it closes, locks or exits the database
under its default options, and saves without asking under "Automatically save when
closing/locking the database". An OK pressed while a save of the plugin's own runs under the
dialog is handled inside that save, but the dialog's close, and with it the write, comes only
after that save has returned: the save's file lacks the change, the database stays marked and
the next save writes it. A save over a file another copy replaced, answered Synchronize, keeps
both copies' changes; answered Overwrite, it keeps the window's change and loses the other copy's
(the question names that loss); answered Cancel, it writes nothing and leaves the mark. **No run
lost the change without a question, and no file the runs left failed to open.** The only way found
to lose it without a question is KeePass ending without its close handling (killed, crashed, the
machine off), not run, as for every unsaved edit.

## 1. How the runs were made

- **KeePass 2.62**, the pinned `.keepass\2.62`, copied by the Live tier's `ThrowawayKeePass` into
  `%ProgramData%\KeePassGateLive\<fixture>` with the plugin under test, its own configuration
  and an invented database (`FillWithInventedContent`) holding an authority made by the product.
  Started through `unelevated.ps1` (runas `/trustlevel:0x20000`, `elevated: False` at the head
  of every log), under the shared desktop lock.
- **Fixtures**: `tests/KeePassGate.Host.Tests/Live/LiveRuleWaitsForSaveTests.cs`, four
  sessions, category `Live`, each a fixture of its own: `LiveRuleAtNodeDialogOkTests` (A),
  `LiveRuleBeforeEndingTests` (B), `LiveRuleOkInsideSaveTests` (C),
  `LiveRuleNextSaveSynchronizedTests` (E). Command:
  `rule-waits-evidence\run.ps1 -Filter 'FullyQualifiedName~LiveRule…'` through `unelevated.ps1`.
  Three runs: A alone (23:26), B with E (23:35 to 23:38), A again with C (23:40 to 23:42).
  Every fixture passed; no step failed in any run.
- **Driving**: UI automation and Win32 input from the test process, as the Live tier does: menus
  by their access keys, buttons by UI automation's invoke, boxes by their value pattern, the
  question windows found as KeePass's windows titled `KeePass` other than the main window.
- **What is read**: a line the stand-in writes at each event (below), the title bar through UI
  automation, and each file read back with KeePassLib 2.62 and the product's own reading
  (`KdbxStore.Read`, `ProfileNames.Of`, the entries of the plugin's group), from a copy.
- **Read off KeePass's IL** where a result needed its mechanism: KeePass 2.62's `MainForm`,
  `GroupForm`, `PwEntryForm` and `GlobalWindowManager`, every instruction with its branch targets
  (`rule-waits-evidence\il-full.ps1`, dumps in `il-full.txt` and `il.txt`). Marked **read** below;
  nothing read is in a table.

## 2. The stand-in

`tests/KeePassGate.Host.Tests/Live/RuleProbePlugin.cs.txt`, compiled by the fixture with
KeePass's own C# compiler (`CSharpCodeProvider`) into `plugins\KeePassGate\` beside the product
before KeePass starts. Not part of the product. It is compiled under the assembly name
`KeePassGate.Host.Tests`, the name the product lets see its internals, so that it calls the
product's own writer and store rather than a copy of them.

- **Armed per case** by a file `rules-probe.arm` beside `KeePass.exe` holding a profile name.
  The next close of a `GroupForm` or a `PwEntryForm` consumes it.
- **At the dialog's close** (`FormClosed`, subscribed at `GlobalWindowManager.WindowAdded`, so
  after KeePass's own `OnFormClosed`), where the result is OK: it removes that profile name
  through `DatabaseWriter.ByOperator`, inside which the store is read afresh with every entry
  (`KdbxStore.ForWrite`), `ProfileActs.Remove` composes the act against that reading,
  `KdbxStore.Stage` plans it and asks of every entry it touches whether a window holds it
  (§626), and the plan is applied (`StoreEntries.Apply`: the record of the removal added, the
  name's addition and the summary entry deleted with deletion records, a new summary entry
  added, the database marked). `ByOperator` then has KeePass's tree and list rebuilt
  (`UpdateUI(false, null, true, null, true, null, false)`). **Nothing is saved.** With any other
  result it writes nothing and notes the arming as consumed.
- This is the write the plan's section 7 step 5 makes, with a record of kind `removed` in the
  place of a version of a rule: the same writer, guard, plan, application, mark and redraw. A
  removal of a name is a narrowing (a name taken off grants nothing), and its plan, like a
  version's, adds a record and deletes what it replaces with a deletion record.
- **What it writes down**, in `rules-probe.log`: each node dialog opened and closed with its
  result and `PwDatabase.Modified`; the outcome of the write and the mark and title right after
  it; at the first `Application.Idle` after the close, which comes once KeePass's
  `EditSelectedGroup` or `EditSelectedEntry` has returned, the mark, `MainForm.Text` and which of
  KeePass's two images the toolbar's save button shows (`m_imgFileSaveEnabled`, shown while the
  database holds unsaved changes, or `m_imgFileSaveDisabled`); the same at every `FileSavingPre`,
  `FileSaved` (with its success) and `FileClosingPre`, and at `FileClosed`.
- Each case removes a name of its own (`n01`, `n02`, ...), seeded before KeePass starts, so the
  file tells which case's change it holds: "off the list" and "removal present" below.

## 3. What each case showed

Every row was driven in the window and read from the stand-in's lines, the title bar and the
file. "Marked" is `Modified` true, the title `throwaway.kdbx* - KeePass` and the save button on
its unsaved image, all three read after KeePass handled the dialog's result; "unmarked" is all
three the other way. "In the file" is the name off the list with its removal record present and
the store `Whole`.

### 3.1 The dialog's result (session A, run twice; B4 and E1 to E3 repeat the entry case)

| Case | Driven | Written at the close | After KeePass handled the result | After Ctrl+S |
| --- | --- | --- | --- | --- |
| A1 | group dialog (root), nothing edited, OK | taken | marked | in the file, store `Whole`, 1 summary entry |
| A2 | group dialog, name edited, OK | taken | marked | in the file, with the new group name |
| A3 | group dialog, Cancel | nothing; arming consumed | unmarked | (not saved) |
| A4 | group dialog, Escape | nothing | unmarked | |
| A5 | entry dialog (Shared Mailbox), nothing edited, OK | taken | **marked** (KeePass counted the entry unchanged) | in the file |
| A6 | entry dialog, title edited, OK | taken | marked | in the file, with the new title |
| A7 | entry dialog, Cancel | nothing | unmarked | |
| A8 | entry dialog, nothing edited, Escape held | nothing; **no question** | unmarked | |
| A9 | entry dialog, title edited, Escape held | KeePass asked "Entry 'Shared Mailbox A9'. Do you want to save the changes you have...?" [Yes] [No] [Cancel]; **Yes**; the dialog closed with OK; taken | marked | in the file, with the new title |
| A10 | the same, answered **No** | closed with Cancel; nothing | unmarked | |
| A11 | entry dialog on the store's own summary entry (`KPG head 8f341f52`), nothing edited, OK | **taken**, not refused | marked | in the file, store `Whole`, 1 summary entry |

In the first run of A9 and A10 the Escape was pressed and let go at once, and KeePass closed the
dialog with Cancel and no question: KeePass asks only where the Escape key is still down as its
Cancel runs (read, `PwEntryForm.OnBtnCancel` reads `GetAsyncKeyState(VK_ESCAPE)`) or where the
window is closed by its frame. The second run held the key 400 ms, as a person does, and the rows
above are that run's. Every other row gave the same lines in both runs.

The write at the close took 20 to 125 ms from the close to the mark in these runs (a database of
four invented entries and a store of about twenty records).

### 3.2 Ending the database with only that change pending (session B)

Each case starts KeePass afresh, writes through the entry dialog with nothing edited (so that
KeePass's own handling marks nothing), then ends the database.

| Case | Options | Driven | What KeePass showed | The file afterwards |
| --- | --- | --- | --- | --- |
| B1 | default | Ctrl+W; Cancel; Ctrl+W again; Save | "Save database changes before closing the file?" [Save] [Discard changes] [Cancel], with the box "Automatically save when closing/locking …"; after Cancel the database open and marked; the same question again | after Save: in the file; database closed |
| B2 | default | Ctrl+L; Discard changes | "Save database changes before locking the workspace?" | **not in the file** (the name on the list): the operator's answer discarded it; workspace locked |
| B3 | default | the main window closed; Save | "Save database changes before exiting KeePass?" | in the file; KeePass exited cleanly |
| B4a | both automatic saves on | entry dialog, **title edited**, OK | no question; KeePass saved right after the dialog (`saving-pre`, `saved` before the idle line) | in the file with the new title; unmarked |
| B4b | the same | entry dialog, nothing edited, OK | no save; marked | not yet in the file |
| B4 | the same | Ctrl+W | **no question**; saved, then closed | in the file |
| B5 | the same | entry dialog, nothing edited, OK; Ctrl+L | no question; saved, then locked | in the file |
| B6 | the same | entry dialog, nothing edited, OK; the main window closed | no question; saved; exited | in the file |

"Both automatic saves" are `Application.FileClosing.AutoSave` ("Automatically save when
closing/locking the database") and `Application.AutoSaveAfterEntryEdit` ("Automatically save
after modifying an entry using the entry editing dialog"), written into the throwaway's
configuration before the start. The automatic save after an entry's dialog runs only where
KeePass counts the entry as modified (B4a against B4b; read, `EditSelectedEntry` saves on
`AutoSaveAfterEntryEdit && bMod`, `bMod` being `HasModifiedEntry`), and it carries the stand-in's
change, which is in the database by then.

### 3.3 OK pressed while a save of the plugin's own runs (session C)

The database's key derivation calibrated to 4 s (AES-KDF), so that every save takes that long.
Three clients admitted before the start. For each case: the stand-in armed, a node dialog opened,
a client gives its certificate up (`DELETE /gate/certificate`), whose write and save the plugin
makes on KeePass's thread under the open dialog; the moment KeePass's transaction file appears,
the dialog's OK is pressed with the pointer, 300 ms later.

| Case | Dialog | Timeline (UTC) | Give-up answered | The file as the give-up's save left it | After the dialog | After the operator's Ctrl+S |
| --- | --- | --- | --- | --- | --- | --- |
| C1 | group, nothing edited | save seen 47.316, OK pressed 48.215, `saved` 51.240, **dialog closed 51.277, written 51.311** | `200 {"ok":true}` | opens, `Whole`; the certificate given up in it; **the change not in it** (`n01` on the list) | marked | the change and the certificate given up both in the file |
| C2 | entry, nothing edited | save seen 10.655, OK 11.523, `saved` 14.724, closed 14.754, written 14.785 | `200` | opens; given up; change not in it | marked | both in the file |
| C3 | entry, title edited | save seen 34.077, OK 34.935, `saved` 37.689, closed 37.717, written 37.737 | `200` | opens; given up; **the new title in it** (KeePass's own OK handler ran inside the save); change not in it | marked | both in the file, with the title |

So KeePass's own OK handler runs inside the plugin's save, as item 30 measured for the settings
dialog, and what it changes of the node reaches the file that save writes; the stand-in's write,
made at the close, never does. No give-up was refused for the open dialog, and no write at the
close was refused for the save before it.

### 3.4 The next save over a file another copy replaced (session E)

The stand-in writes through the entry dialog (nothing edited); then the file is replaced as a
synchronizer replaces it, by the file as KeePass last saved it with a client revoked through the
product's own act (`StoredAdmissions.Remove`) and the build agent's user name changed; then
Ctrl+S. Every Ctrl+S met KeePass's "Overwrite the existing file?" [Synchronize] [Overwrite]
[Cancel].

| Case | Answer | The file afterwards | The gate afterwards |
| --- | --- | --- | --- |
| E1 | Synchronize | the window's change (`n01` off) **and** the other copy's revocation of `o1` and its user name; store `Whole`, **2 summary entries** (two writes made apart); unmarked | `o1` refused (`401`) |
| E2 | Overwrite | the window's change (`n02` off); the other copy's **revocation of `o2` gone**, its user name gone; 1 summary entry | `o2` admitted (`200`) |
| E3 | Cancel | the other copy byte for byte as placed (`n03` on the list, `o3` revoked); the window **still marked** | `o3` admitted (`200`): the gate serves memory |
| E3 again | Ctrl+S, the same question, Synchronize | both: `n03` off and `o3` revoked; 2 summary entries | `o3` refused (`401`) |

Overwrite losing the other copy's records is KeePass's own Overwrite, measured before
(`measured-desktop-windows` 2.2) and named by the question ("Changes made by the other user will
be lost"); it loses the other copy's narrowing, never the window's.

### 3.5 A window of KeePass's over the write (item 14, as far as the cases above reach)

- **An entry dialog on an entry of the store** (A11): the summary entry's own dialog, closed
  with OK. Its OK put its copy back (nothing changed) and the write at its close then deleted
  that summary entry and added the next summary entry: taken, store `Whole`. It is not refused because
  KeePass's own close handler (`OnFormClosed`) calls `GlobalWindowManager.RemoveWindow` before
  any handler a plugin subscribes later runs (read), and the product's `GroupDialogs` lets a
  dialog go at `WindowRemoved`, so at the write no window holds that entry.
- **KeePass's question over the entry dialog** (A9): answered and gone before the dialog's
  close; the write comes after it.
- **The status of a save of the plugin's own under the dialog** (C1 to C3): the OK handled
  inside it, the write after it.

## 4. The mechanism

All **read** off KeePass 2.62's IL, and borne out where a row above names it.

- **The mark is only ever set by KeePass's handling of a node dialog, never cleared.**
  `EditSelectedGroup` calls `UpdateUI(…, bSetModified: true)` after OK and `false` otherwise;
  `EditSelectedEntry` passes `bMod = (result == OK) && HasModifiedEntry`. `UpdateUIState(bool)`
  does `if (DatabaseOpened && bSetModified) pd.Modified = true` and nothing else with the flag,
  then draws the title and the save button's image from `pd.Modified`. Since the write at the
  close runs inside `ShowDialogAndDestroy`, before either method's `UpdateUI`, the mark it sets
  is still there when KeePass has finished (A5, B4b).
- **The close of a modal dialog is raised by its own loop.** An OK pressed while a save pumps
  (`SaveDatabase` and `KdbxFile.Save` pump through `DoEventsByTime`, item 30) is handled as the
  button's click there, KeePass's `OnBtnOK` included; the loop checks the dialog's result only
  between its own messages, so `FormClosing` and `FormClosed` come once the save has returned
  (C1 to C3: closed 28 to 37 ms after `saved`). `PwDatabase.Save` clears `m_bModified` at its
  end, before the close; the write at the close sets it again.
- **What asks before the database ends** is `MainForm.CloseDocument`, used by File, Close, by
  every lock (`LockAllDocuments`) and by exit: where `pd.Modified`, it saves without asking if
  `FileClosing.AutoSave` is on and policy allows saving, and otherwise shows
  `ShowFileSaveQuestion` (closing, locking or exiting): Cancel stops the close, Save saves and
  stops the close if the database is still marked after it, Discard goes on without saving.
- **Nothing locks, and no trigger fires, while a node dialog stands.** Both dialogs register
  with `GlobalWindowManager.AddWindow(this)` with no `IGwmWindow`, which makes
  `CanCloseAllWindows` false; `PrepareLock` then answers false, so Ctrl+L, a lock on Windows'
  lock or session switch (`OnSessionLock` calls `LockAllDocuments`) and the inactivity lock all
  leave the workspace unlocked and the dialog open; and `OnTimerMainTick` raises `TimePeriodic`
  only while `WindowCount` is zero. A save of KeePass's own therefore cannot run under a node
  dialog; a save of a plugin's can (C).
- **The entry dialog's question at closing** (`HandleFormClosing`) is asked only where the
  dialog is closed without OK and `m_bForceClosing` is false, which `OnBtnCancel` sets true
  unless Escape is still down, and only where `HasModifiedEntryEx` finds the entry changed: a
  change the dialog does not hold, as the rules in the store, never raises it (A8).

## 5. What decides

- **Can a rule written at a node dialog's OK and left for the next save be lost without the
  operator being asked?** Not in any sequence run. Every run that ended the database with the
  change pending asked first (default options) or saved it (automatic saving); every save after
  the write carried it, a Synchronize included. It is lost only where the operator answers
  Discard changes (B2), and, not run, where KeePass ends without its close handling: killed,
  crashed, or the machine off. That is §642 row 21, as for every unsaved edit.
- **Does KeePass ask before it ends?** Yes, for close, lock and exit alike, under its default
  options; under "Automatically save when closing/locking the database" it saves instead.
- **Is the file ever left in a state KeePass cannot read?** Not in these runs: every file read
  back opened, with the store `Whole`, including the files a save of the plugin's own wrote while
  the dialog's OK was handled inside it. The compression case of item 30 does not arise here: a
  node dialog's OK changes the node, which the save serializes after its pump, and no header
  field the save reads on both sides of the pump.
- **A narrowing rule the operator set and that is silently not in the file**: not found. The
  states in which the file lacks the rule while memory holds it (after C's save of the plugin's
  own, after E3's Cancel, B4b before the close) are each marked, and the next save or the
  question at the end writes it. The only narrowing lost in these runs is the other copy's
  revocation under Overwrite (E2), which the question announces and which is KeePass's
  Overwrite, not the rule's wait.

## 6. The plan's section 7 against this

It holds as written, with three points it has to keep:

1. **The write is made at the dialog's close, never at the press of OK.** KeePass's own
   `OnBtnOK` runs inside a save of the plugin's own when pressed there (C3); a write made at the
   press would be made inside the save, before or between the parts it serializes, and the save
   would then clear the mark it set (`PwDatabase.Save` ends with `m_bModified = false`). Where the
   save had serialized the plugin's group already, that write would be in memory, unmarked and
   not in the file: the silent loss row 15 describes. At the close it cannot happen (C1 to C3).
   This is reasoning from the measured order and the IL, not run.
2. **The mark has to be set by the write itself.** For the entry dialog KeePass sets none where
   the entry is unchanged (A5); the plan's step 5 marks through the store's application, as the
   stand-in did, and that is what kept the change asked about.
3. **Escape with only rules changed discards them without a question** (A8), as part 2 of the
   questions has it; Escape answered Yes after a field changed writes them with the entry (A9).
   The tab can state that the rules are written only by OK.

Two notes for the plan beside them. The guard of `ByOperator` refused nothing at a node dialog's
close in any run, the store's own summary entry's dialog included (A11). And after a Synchronize
the store holds two summary entries (E1, E3), which the next write of the plugin's own folds into
a single entry (E2 began from two and left a single summary entry).

## 7. Not run, and why

- **KeePass ending without its close handling** (a kill, a crash, Windows ending the session,
  power): the change is lost as every unsaved edit is; nothing of KeePass's runs to ask.
- **A pairing's or a renewal's save under the dialog**: only a certificate given up was used;
  each goes through the same `MainForm.SaveDatabase` and the same writer turn. The settings
  dialog's case was run with a pairing (item 30).
- **An OK handled during the save's serialization** rather than its key derivation: KeePass's
  progress logger pumps for every percent of the tree, so on a large database the press can land
  between two entries. The write at the close is after the save either way (the loop argument
  above); what KeePass's own handler then puts in that file by halves was not measured.
- **A save of another plugin, or `SaveForceSync` on, under the dialog**; **File, Synchronize**
  started by the operator (rather than at the save); **Save As** over the same path, which
  `measured-desktop-windows` 2.2 found to skip KeePass's question; **Add Entry** and **Add
  Group**, whose node is not in its group at the close; **several entries edited together**;
  **a second open database**; **the inactivity lock and Windows' lock while the dialog stands**
  (read only: no lock happens, section 4).
- **The rules' own records**: the stand-in writes a removal of a name. A version of a rule goes
  through the same writer, plan and mark; its texts, its deletions of replaced versions and the
  room it takes were not exercised here.
- **Under KeePassXC, Keepass2Android, or two KeePass instances**: out of scope for this item.

## 8. Evidence and commits

- Branch `measure/rules-wait-for-save`, commit `570739a5` "Measure a rule written at a node
  dialog's OK in a real KeePass": the stand-in `RuleProbePlugin.cs.txt` and the four fixtures in
  `LiveRuleWaitsForSaveTests.cs`. Pushed nowhere.
- `rule-waits-evidence\` beside this report: `run-a1.log` (A, first run), `run-be1.log` (B and
  E), `run-ac1.log` (A again, and C), each with every step, the stand-in's lines, every question
  as UI automation read it, and each file as read back; `il-full.txt` and `il.txt`, the IL read;
  `il-full.ps1` and `run.ps1`, the scripts. No database is kept; each session's directory was
  deleted when its fixture passed.
