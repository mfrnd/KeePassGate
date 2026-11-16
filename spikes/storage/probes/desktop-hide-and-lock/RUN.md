# Probes for `desktop-hide-and-lock`

The measurements behind `papers\ground\measured-hide-and-lock.md`: whether a plugin can hide the
store (the group `KeePassGate` and its write-once entries) from a person at KeePass 2.62's window,
and whether it can keep the store from being changed. Written on this branch on 2026-11-16 and
recorded from it.

**This family shows windows**: every case starts KeePass on the desktop and drives it with UI
automation, the keyboard and the mouse.

| Path | What it is |
| --- | --- |
| `Run.ps1` | the one command: provides KeePass, builds, maps the work directory as a drive, runs the cases, copies their output into `out\` |
| `probe\Desk.cs`, `Desk.csproj` | a copy of the desktop-windows family's probe, made `partial` and given the command `hlbase` |
| `probe\Hl.cs` | `hlbase <path> [top\|bin\|deep] [new]`: this family's database, over KeePassLib 2.62 |
| `plugin\HideLock\` | `HideLock.dll`, the probe plugin: each technique behind a switch in `HideLock.ini`, and a log line per event |
| `scripts\Hl.ps1` | the driver: one case per run, a fresh database, the switches, the window read and pictured, the file dumped; it dot-sources the desktop-windows family's `Ui.ps1` and `Kp.ps1` and replaces one function of them |
| `scripts\Cases.ps1` | the cases, each a list of steps a person takes |
| `recorded\` | the record: every run's log, the plugin's log, dumps, diffs and pictures as the runs of 2026-11-16 left them |

## What it measures

- **Hiding** (the report's section 2): the views a person has of the store with nothing switched
  on (`a-base`); the store's group node and entries removed from the tree and the list at
  `MainForm.UIStateUpdated` (`a-hide-ui`) and at `Application.Idle` (`a-hide-idle`), each through
  the same views and the same rebuilding steps; the list's grouping and the entries of subgroups
  under the first (`a-hide-grouped`); the store under the recycle bin (`a-bin`) and deep under
  another group (`a-deep`).
- **The dialogs** (section 3): the entry dialog, several entries' dialog, Add Entry, Duplicate
  Entry and the group dialog for store nodes, with nothing switched on (`b-base-dialogs`) and with
  each way of answering them (`b-close`, `b-cancelsave`, `b-disableok`, `b-readonly`,
  `b-lockall`).
- **The commands** (section 4): delete, delete for good, drag, rearrange, duplicate, quick edits,
  copy and paste, edit, sort, move, empty the recycle bin, select all and delete, delete the group:
  watched only (`b-watch`), against disabled menu items, a key filter, a drag block and a cancelled
  default action (`b-block`), and against menu items hidden but left enabled (`b-hideonly`).

## What a run needs

- **The desktop, to itself**, for minutes at a time; a screen lock stops it.
- **A script that starts a command under a basic user token and waits for it**, given as
  `-Unelevated`, as the desktop-windows family's `RUN.md` describes.
- Windows PowerShell 5.1 as `powershell.exe`, the .NET SDK, a free drive letter (W: by default),
  KeePass 2.62 (`tools\KeePass.ps1`), a display of 1280 by 800 or larger, English as the language
  of Windows' own dialogs.

## The command

From the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\desktop-hide-and-lock\Run.ps1 -Unelevated <path to the script>
```

`-Case` names cases, comma separated, `all` by default; `-WorkDir` another work directory;
`-Drive` another letter; `-RunNumber` the number in the run directories' names. The record's runs
were made with `scripts\Hl.ps1 -Work W:\ -Case <cases> -Unelevated <path>` over a work directory
prepared the way `Run.ps1` prepares it.

## Output

Fresh output goes to `out\`: `out\runs\<case>-<run>\` holds `run.log` (every step, every window's
title, texts and buttons as read, every press), `plugin.log` (the plugin's lines, with a `DRIVER`
line for every step), `dump-before.txt`, `dump-after.txt` and `diff.txt` (the file before and
after, read back with the probe), and the pictures. No database is copied.

`recorded\` holds the same for the record: `recorded\runs\<case>-1\` for the counted runs,
`recorded\runs\not-counted\` for the runs set apart, `recorded\logs\` for the batches' console
output and the launcher's lines.

**The report's evidence is this family.** What the report names as `desktop-evidence\hide-and-lock\`
beside it is `recorded\` here, file for file and byte for byte, and its `source\` is the family's
own `Run.ps1`, `plugin\`, `probe\` and `scripts\`, also byte for byte. Nothing of that evidence is
carried a second time.

## Runtime

Summed over the counted runs from each run's first log line to its last: hiding, 6 runs, 464
seconds; the dialogs, 8 runs, 870 seconds; the commands, 4 runs, 481 seconds; 1,815 seconds in all.
`Run.ps1` adds the builds and the copy of KeePass; it was not run end to end.

## The figures the report quotes

Nothing ran twice; each "Recorded" names the file under `recorded\runs\`.

| Figure | Report | Recorded | Second run | Reproduced |
| --- | --- | --- | --- | --- |
| counted runs and runs not counted | summary, 2.2, 3.2, 4.2 | 18 and 10 (`runs\<case>-1\`, `runs\not-counted\`) | not run | not run |
| what each view showed with nothing switched on and under each hiding switch | 2.3 | `a-base-1`, `a-hide-ui-1`, `a-hide-idle-1`, `run.log` and `plugin.log` | not run | not run |
| the Find dialog's results removed at `UIStateUpdated`: `NullReferenceException` and WinForms' dialog | 2.3, 6.1 | `a-hide-ui-1\plugin.log`, `ui-09-find-after-1.png` | not run | not run |
| the status bar counting hidden entries under `hide=idle` ("15 entries found") | 2.3, 6.1 | `a-hide-idle-1\run.log` | not run | not run |
| the tree rebuilt at the open, a drag, a lock and unlock, a synchronize | 2.3 | the three cases' `plugin.log`, `treeRebuilt=True` | not run | not run |
| Empty Recycle Bin deleting the store under the bin, 14 records | 2.4 | `a-bin-1\diff.txt` | not run | not run |
| each dialog switch on the nine routes | 3.2 | `b-<switch>-1\run.log`, `plugin.log`, pictures | not run | not run |
| the save question answered Yes: saved under `disableok`, refused under `cancelsave` | 3.2, 6.2 | `b-disableok-yes-1\diff.txt`, `b-cancelsave-yes-1\plugin.log` | not run | not run |
| each command with nothing switched on and under the blocking switches | 4.2 | `b-watch-1`, `b-block-1`, `b-block2-1`, `b-hideonly-1` | not run | not run |
| guarded items found enabled again at each event | 4.2 | `b-block-1\plugin.log`, `Menus` lines | not run | not run |
| history and times after each kind of change | 4.3 | `b-watch-1\diff.txt`, `b-disableok-yes-1\diff.txt` | not run | not run |
| the runtimes | "Runtime" above | each `run.log` | not run | not compared |

## Changes made to run it here

None: the family was written on this branch and recorded from it. The scripts and the plugin were
corrected while the runs were made, and a run whose flow failed was set apart and made again:
`Show-Front` replaced so that it no longer arms the menu bar of a window already in front; the
Find dialog's button pressed by its caption, "Find"; the plugin's `Application.ThreadException`
subscription removed, since it replaced the dialog a person sees; the save question at the close of
an entry dialog answered; the plugin's and the driver's writes to the shared log retried; the
command steps reordered so that no click lands on the tree after KeePass reorders it; the switches
written from the hash table's pairs rather than its `.Keys`, with the key filter's switch renamed
`keyfilter`; the switches `dragsel` and `guardbin` added for `b-block2` after `b-block`. A
`diff.txt` is now written when the files do not differ; the two counted runs made before that change
(`b-hideonly`, `b-block2`) had theirs written by hand with the same text, `no difference`.

## Machine paths replaced

In `recorded\`, the work directory is written `<work>` (the run's `W:` and the directory it maps),
the branch root `<branch>`, the run's temporary directory `<session>` and the user profile
`<profile>`. A search of `recorded\` for `C:\Users`, `AppData`, `scratchpad` and the machine's name
finds nothing. The pictures show KeePass's windows and `db.kdbx` only; no file dialog was pictured.
