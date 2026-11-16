# Probes for `desktop-windows`

The measurements behind `papers\ground\measured-desktop-windows.md`: what KeePass 2.62 and
KeePassXC 2.8.0 do in their own windows on a desktop. Measurement 1 is KeePass over a file that
changed underneath an open database, 2 the same in KeePassXC, 3 KeePass's entry dialog and a plugin
string on a user's entry, 4 KeePass's maintenance commands on a store made of entries, and 5 what a
plugin can observe around KeePass's own synchronize, import and save. They answer what `RESULTS.md`
7.2 lists as needing a window, and for both programs the second criterion of the list of
compatible programs: that a program merges or asks before it writes over a changed file. Written
on this branch on 2026-11-16 and recorded from it.

**This family shows windows**, as `desktop-hide-and-lock` does and no other family: every
measurement starts KeePass or KeePassXC on the desktop and drives it with UI automation, the
keyboard and the mouse.

| Path | What it is |
| --- | --- |
| `Run.ps1` | the one command: provides the programs, builds, maps `work\` as a drive, makes the databases, runs one measurement, copies its output into `out\` |
| `probe\Desk.cs`, `Desk.csproj` | `Desk.exe`, a console program over KeePassLib 2.62: makes the databases (`m1base`, `m1new`, `m3base`, `m3other`, `m4base`), dumps a file line per item (`dump`), prints a one-line verdict (`verdict`), and runs KeePassLib's own merge (`sync`, `merge`) |
| `plugin\DeskLog\` | `DeskLog.dll`, the logging plugin of measurement 5: a line per trigger event and per file event of KeePass's main window, with a fingerprint of the database in memory and of the file on disk; behind a switch it adds one marker entry at the first `FileSavingPre` inside a synchronize |
| `plugin\DeskWrite\` | `DeskWrite.dll`, the writing plugin of measurement 3: writes a string to a live entry while its dialog is open, on KeePass's own thread, and reports what the entry holds in memory |
| `target\DeskTarget.cs` | an auto-type target: a window of a given title that writes what is typed into it to a file |
| `host\DeskHost.cs` | a managed host for the scripts, kept because it settled that UI automation's client-side providers fail in a managed host as well (see "Driving the windows"); no measurement uses it |
| `scripts\Ui.ps1` | starting a program under a basic user token, reading windows through UI automation, the accessibility interface and Win32 messages, keys and clicks, pictures of one window, the replacement of a file as a synchronizer makes it |
| `scripts\Kp.ps1`, `scripts\Xc.ps1` | KeePass's and KeePassXC's windows: configuration, start, groups and entries, the entry editors, dialogs read and answered |
| `scripts\M1.ps1` to `M5.ps1`, `M2v.ps1`, `M3v.ps1` | the measurements; `M3v` is what a person sees and does in KeePass, `M2v` the same in KeePassXC |
| `scripts\Summarize.ps1`, `Collect.ps1` | a line per run from the run logs; the copy of a run's text and pictures into `out\` |
| `recorded\` | the record: every run's log, dumps, diffs, verdicts and pictures as the runs of 2026-11-16 left them |

## What it measures

- **Measurement 1** (`M1.ps1`, the report's section 2): OLD open in KeePass, the window's changes
  made through the entry dialog, NEW written beside the file and renamed over it as a synchronizer
  does, then every way KeePass saves (Ctrl+S, the toolbar, closing, locking, exiting, Save As) and
  every answer to its question, with the file's time later, equal or earlier and its length the
  same; the cases with nothing changed (focus, lock and unlock, a lock when the file arrives); and
  each of KeePass's file and synchronize options set the other way. Each file is read back with
  `Desk.exe`.
- **Measurement 2** (`M2.ps1`, section 3): the same in KeePassXC 2.8.0, each answer of its two
  "Reload database" dialogs and of "File has changed", with one saving or reloading setting changed
  at a time, and Database, Merge From Database against `keepassxc-cli merge -s` of the same two
  copies. `M2v.ps1` (section 7) is what a person sees of the store there: search, the entry preview,
  global auto-type.
- **Measurement 3** (`M3.ps1`, section 4.2): `DeskWrite` adds or changes `KPG.Ref` on a user's entry
  while its dialog is open, then OK, OK after an edit, or Cancel. `M3v.ps1` (section 4.3): the entry
  view, quick search, the Advanced tab, Duplicate Entry, moves and copies, print, auto-type and
  every export format.
- **Measurement 4** (`M4.ps1`, section 5): Database Maintenance's pages, Delete Duplicate Entries,
  Delete Empty Groups, Delete Unused Custom Icons, and the Edit and Group commands, each on a fresh
  copy of `m4base`'s store, the file then diffed against the base's dump.
- **Measurement 5** (`M5.ps1`, section 6): `DeskLog`'s lines around File, Synchronize with a file
  and with a URL, a synchronize by a trigger, File, Import with each method, a save over a changed
  file answered Synchronize, and the opening of a file KeePassLib merged; with the trigger system on
  and off, and with the marker switch.

## What a run needs

- **The desktop, to itself.** Windows appear and are driven with the keyboard and the mouse for
  minutes at a time; a person using the machine meanwhile spoils the run, and a screen lock stops
  it.
- **A script that starts a command under a basic user token and waits for it**, given as
  `-Unelevated`: the recorded runs' shell was elevated and ran `runas /trustlevel:0x20000`
  through such a script. A program started that way reported, read from its own token,
  Administrators as a deny-only group, integrity level high and `TokenIsElevated` 1. From a shell
  that is not elevated, the scripts start the programs directly.
- Windows PowerShell 5.1 as `powershell.exe`, the .NET SDK, a free drive letter (W: by default),
  KeePass 2.62 (`tools\KeePass.ps1`) and KeePassXC 2.8.0 and 2.7.12 (`tools\KeePassXC.ps1`).
- A display of 1280 by 800 or larger: the scripts click at places they read, and a row off the
  screen is a row they cannot reach.
- English as the language of Windows' own dialogs: the file dialogs are answered by their captions.

## The command

From the branch root, one measurement at a time:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\desktop-windows\Run.ps1 -Measurement M1 -Unelevated <path to the script>
```

`-Measurement` is M1, M2, M2v, M3, M3v, M4 or M5; `-Case` names cases, comma separated, `all` by
default; `-Drive` another letter. Each script also runs on its own over a work directory that
`Run.ps1` prepared, `scripts\M1.ps1 -Work W:\ -Case <cases> -Unelevated <path>`, which is how the
record's runs were made: `Run.ps1` was written last and run once end to end, for measurement 5's
case `open-merged`.

**KeePass's trigger case leaves a file behind.** At its start KeePass moves an enabled trigger out
of a non-enforced configuration into `KeePass.config.enforced.xml` beside the executable, and the
next start of that copy then synchronizes. `M5.ps1` removes that file before every run and after
the trigger case.

**The export "Windows Favorites" writes into the user profile's Favorites folder**, whatever file
the dialog names. `M3v.ps1` exports the formats named by `-ExportOnly`, which leaves both Windows
Favorites formats out unless named; the record's run named them and removed what they wrote.

## Output

Fresh output goes to `out\`: `out\runs\<measurement>\<case>-<run>\` holds `run.log` (every step,
every window's title, texts and buttons as read, every press, the file's size, time and SHA-256
before and after each replacement), the dumps of the files the run left and their diffs, and the
pictures; `out\logs\` the verdict and summary lines and the launcher's lines. `recorded\` holds the
same for the record, and `recorded\runs\<measurement>-not-counted\` the runs set apart. Every
database is made in `work\`; none is copied into `out\` or `recorded\`, and `work\` and `out\` are
not committed.

**The report's evidence is this family.** What the report names as `desktop-evidence\` beside it
is `recorded\` here, file for file and byte for byte, and its `desktop-evidence\source\` is the
family's own `Run.ps1`, `host\`, `plugin\`, `probe\`, `scripts\` and `target\`, also byte for
byte, and this `RUN.md` but for where it names the report and the evidence. Nothing of that
evidence is carried a second time.

## Runtime

Summed over the counted runs from each run's first log line to its last: measurement 1, 31 runs,
1,534 seconds (49 a run); 2, 21 runs, 1,878 seconds (89); `M2v`, 1 run, 101 seconds; 3, 10 runs,
365 seconds (37); `M3v`, 16 runs, 733 seconds (46); 4, 17 runs, 680 seconds (40); 5, 14 runs, 599
seconds (43). `Run.ps1` adds the builds, the copies of the programs and the databases: the end to
end run of one case of measurement 5 took 142 seconds.

## The figures the report quotes

The second run is `Run.ps1 -Measurement M5 -Case open-merged` on 2026-11-16; nothing else ran
twice. Each "Recorded" names the file under `recorded\`.

| Figure | Report | Recorded | Second run | Reproduced |
| --- | --- | --- | --- | --- |
| counted runs and runs not counted, per measurement | 1, 2.2, 3.1, 4.2, 4.3, 5.1, 6.1 | 31 and 12; 21 and 9; 10 and 0; 16 and 13; 17 and 32; 14 and 20; `M2v` 1 (`runs\<m>\`, `runs\<m>-not-counted\`, `logs\m1-not-counted.txt`) | not run | not run |
| KeePass's question at a save over a changed file: "Overwrite the existing file?", [Synchronize] [Overwrite] [Cancel] | 2.2, 9.1 | every case of `runs\m1\` that changed the file, `run.log` and picture | not run | not run |
| Overwrite: every item of NEW gone, in 9 runs with changes and 1 without | 2.2, 9.1 | `logs\m1-verdicts.txt` | not run | not run |
| Cancel: the file NEW byte for byte (3 runs) | 2.2 | `runs\m1\a-cancel-<n>\run.log` | not run | not run |
| Synchronize at a save, item by item | 2.3 | `logs\m1-verdicts.txt`, `a-sync-1` to `3` | not run | not run |
| Save As over the same path: "already exists. Do you want to replace it?", NEW's items gone | 2.2, 9.1 | `runs\m1\s-saveas-1\` | not run | not run |
| the file replaced by identical bytes: no question | 2.2 | `runs\m1\t-ident-1\run.log` | not run | not run |
| KeePassXC's dialogs, their texts and buttons | 3.2, 9.2 | `runs\m2\<case>-<n>\run.log` and pictures | not run | not run |
| X1 back after every merge: 10 "Reload database" merges with its record, 3 window merges without | 3.3, 9.2 | `logs\m2-verdicts.txt` | not run | not run |
| the window's merge and `keepassxc-cli merge -s` differ in database custom data only, 5 lines | 3.3 | `runs\m2\w-merge-<n>\window-vs-cli.diff.txt` | not run | not run |
| the backup `db.old.kdbx` is NEW byte for byte | 3.2, 9.2 | `runs\m2\o-backup-1\run.log` | not run | not run |
| the entry dialog's ten rows: the plugin's write undone by OK, kept by Cancel | 4.2, 9.3 | `logs\m3-summary.txt`, `runs\m3\<case>-1\` | not run | not run |
| quick search, the Advanced tab, Duplicate Entry, moves, print, auto-type, the exports | 4.3, 7 | `logs\m3v-summary.txt`, `runs\m3v\` | not run | not run |
| KeePassXC's search, preview and global auto-type | 7 | `logs\m2v-summary.txt`, `runs\m2v\look-1\` | not run | not run |
| "Deleted objects information", Delete: all 6 records gone; "3 object(s) deleted."; "2 object(s) deleted."; the file KDBX 4.0 after the last custom data key | 5.2, 9.1 | `runs\m4\<case>-1\diff-vs-base.txt` and `run.log` | not run | not run |
| the event order and the fingerprints of each case of measurement 5, and where the marker went | 6.2, 6.3 | `runs\m5\<case>-1\events.log`, `logs\m5-summary.txt` | not run | not run |
| the imports' counts at the next event: 19/1/2, 11/1/3 three times, 10/3/3 | 5.2, 6.2 | `runs\m5\import-<method>-1\events.log` | not run | not run |
| the opening of a file KeePassLib merged: `FileOpened` 10/3/3, the two trigger events, nothing else | 6.2 | `runs\m5\open-merged-1\events.log` | the same events and counts; the file's hash differs, the merge being made again | yes |
| the runtimes | "Runtime" above | each `run.log` | 142 s for the end to end run of one case | not compared |

## Driving the windows

Four things cost the record's runs time, and a re-run meets them again:

- **UI automation shows KeePass's group tree and entry list as bare panes**: the managed client's
  client-side providers fail to load (`ProxyManager.LoadDefaultProxies` throws
  `NullReferenceException`), in Windows PowerShell 5.1 and in the managed host alike. The tree
  and the list are read through the accessibility interface, and after KeePass rebuilds the tree
  (a lock and unlock, a synchronize) that interface answers with no rows at all, so the rows'
  places read at the start are used.
- **KeePass's dialogs expose no patterns**: their boxes and buttons are found as Win32 child
  windows, a box by the label on its line, and clicked; an invoke through UI automation held the
  driver whenever what it started opened another dialog, and the import dialog's OK ignored
  `BM_CLICK`.
- **KeePassXC's widgets carry Qt's object path as their automation identifier**, which is how its
  fields are found (`...passwordEdit`, `...titleEdit`). Its window is maximized so that every row
  is on the screen. `--pw-stdin` is not used: a redirected input made the launching shell wait for
  KeePassXC to exit.
- **The HTML export's file stays open, with no sharing, for as long as KeePass runs**, so it is read
  after KeePass is stopped.

## What was not run

The report's section 8 lists it: KeePassXC 2.7.12, KeePassXC's lock, close and Save As with a
changed file and its direct write setting, XML Replace, the content of the KDB export, the import
methods item by item, a synchronize with another key or over a network, a plugin write to an entry's
other carriers, and printing on paper.

## Changes made to run it here

None: the family was written on this branch and recorded from it. The scripts were corrected while
the runs were made, each correction changing how a window is found or pressed and none what is done
to a database, and a run whose flow failed was set apart and made again with the corrected script.
The last corrections came after the counted runs of measurements 1 to 4 and before measurement 5's
imports and the HTML export's last run: `Kp.ps1` presses the import dialogs' buttons by a click
(`Click-KidAt`) and waits for any new window as the key prompt, and `M3v.ps1` reads an exported file
that KeePass holds open once KeePass is stopped. `Run.ps1` and the replacements in `Collect.ps1`
came after every counted run.

## Machine paths replaced

In `recorded\`, the work directory is written `<work>` (the run's `W:` and the directory it maps),
the branch root `<branch>`, the run's temporary directory `<session>` and the user profile
`<profile>`. A search of `recorded\` for `C:\Users`, `AppData`, `scratchpad` and the machine's name
finds nothing. The pictures show `W:\runs\...` paths only; the one file dialog that showed the
profile's folders is cut to its title and file name row
(`runs\m3v\export-part3\export-7-1-cropped.png`), and so is the Save As dialog
(`runs\m1\s-saveas-1\s-saveas-1-1-cropped.png`).
