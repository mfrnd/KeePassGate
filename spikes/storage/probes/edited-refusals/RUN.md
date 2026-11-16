# Probes for `edited-refusals`

What a hand edit of a record of the recommended store does once the edited copy is merged with
another copy: four kinds of record that refuse and a record that grants, ten kinds of edit, the five
merge methods of KeePassLib 2.62 and `keepassxc-cli merge` of 2.4.2, 2.6.6, 2.7.12 and 2.8.0 in both
directions, and every result read under five reading rules. Written on this branch on 2026-11-16
for the report `papers\ground\measured-edited-refusals.md`, which quotes every figure of the table
below. The store's shapes are those of `EVALUATION.md` section 4 as that report lists them, field
by field, taken from the working tree's file on 2026-11-16 at 08:27 UTC, whose SHA-256 was
`980ce06c99ede2bde5fccd08d60043d10754f983dff55b70e3059edee5bcb803`.

| Path | What it is |
| --- | --- |
| `probe\Program.cs`, `probe\Edr.csproj` | `Edr.exe`, a console program over KeePassLib 2.62: `make` builds every scenario's copies A and B, `reads` dumps both as they are, `kp` merges them with KeePassLib's five methods and `xc` with `keepassxc-cli merge -s`, each in both directions, and every result is dumped as a JSON line |
| `rules.py` | the five reading rules, and nothing else |
| `scripts\condense.py` | folds the dumps of a run into the files `recorded\` holds |
| `scripts\verdicts.py` | applies every rule `rules.py` names to every cell and writes the tables |
| `scripts\compare.py` | holds a fresh `out\` to `recorded\`, file by file |
| `run.ps1` | the command |
| `recorded\` | the output of the run the report quotes |

**Everything ran twice**, on 2026-11-16, on a machine that other measurements were sharing.
**Every output of the second run is the same as the record line for line**, apart from the times.
Nothing here opens a window: KeePassLib runs inside `Edr.exe`, and `keepassxc-cli` is started with
`CreateNoWindow` and the password on standard input.

## What it measures

**The copies.** Each scenario starts from the same database, made by `Edr.exe` with invented content
at 2026-01-01T00:00:00Z (T0) and times in whole seconds: a group `KeePassGate` holding authority a0,
authority a1, the retirement of a0, the admission records of c1 and c3, the revocation of c3, the
additions of `reader`, `writer`, `auditor` and `legacy`, the removal of `legacy` and a recycle bin
marker; the recycle bin holding a deleted user entry and c2's admission record; and two deletion
records that name no entry, the witnesses of the retirement and of the revocation. Every record
carries a check under a store key. Copy B then takes **a single edit of a single record** at
T0+7200, made as KeePass's entry dialog makes it, the old version into history first and the
modification time moved: a character of the target in the payload, the payload string renamed, the
payload string deleted, every string emptied, the title alone, a move to `General`, a move to the
recycle bin, a deletion with a deletion record, a deletion without, and a duplicate with the
original then edited.
The record is the retirement, the revocation, the removal, the marker, or as a control c1's
admission record. Copy A is untouched, or edits the same entry's title 1800 seconds after B's edit,
1800 seconds before it, or in the same second. A scenario with no edit in B is the last control.

**The merges.** Each scenario's copies are merged B into A and A into B, with KeePassLib's
Synchronize, Overwrite if newer, Overwrite existing, Keep existing and Create new IDs over copies
under the fast key derivation of `store-entries` (Argon2d, 2 iterations, 1 MiB), and with
`keepassxc-cli merge -s` of the four releases over copies under **the slow derivation of
`ground-keepassxc`** (Argon2d, 200 iterations, 64 MiB, 2 lanes), so that the two loads of a merge
fall in different seconds. Every result, and each copy as it is, is dumped.

**The verdicts.** `rules.py` reads each dump under five rules, and `scripts\verdicts.py` holds each
result against what the unedited copy decides: wider, unchanged, or refusing. The report's section 2
states each rule exactly.

Cited by the report throughout; the report's sections name the file of `recorded\` each table comes
from.

## What a run needs

- Windows PowerShell 5.1 as `powershell.exe`, which runs `run.ps1`.
- The .NET SDK for `dotnet build`, and uv, which runs the three scripts with the standard library
  alone.
- KeePass 2.62, which `. .\tools\KeePass.ps1; Install-KeePass` provides from the branch root, and
  KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, which `tools\KeePassXC.ps1` provides when the run asks
  for a release.

## The command

From the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\edited-refusals\run.ps1
uv run --no-project python -I -X utf8 spikes\storage\probes\edited-refusals\scripts\compare.py spikes\storage\probes\edited-refusals\recorded spikes\storage\probes\edited-refusals\out
```

`run.ps1 -Degree <n>` sets how many cells run at once, 6 by default and in both runs. A rule added
to `rules.py` is read over the record with no database at all:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\edited-refusals\scripts\verdicts.py spikes\storage\probes\edited-refusals\rules.py <a copy of recorded\>
```

which writes the tables into the directory it is given, so a copy keeps the record as it is.

## Output

Fresh output goes to `out\`, the raw dumps to `out\raw\`, and every database to `work\`; none of
them is committed. `recorded\` holds the first run's `out\` without `raw\`:

| File | What it holds |
| --- | --- |
| `versions.jsonl`, `rows.jsonl`, `frames.jsonl`, `states.jsonl`, `cells.tsv` | every dump of the run, folded: each distinct entry version, row, group list and file once, and each cell's file; `scripts\verdicts.py` rebuilds every dump exactly from them |
| `verdicts.tsv` | each cell's verdict under each rule, with what it widens or refuses |
| `counts.md` | per rule, the counts by edit and record, the totals, and the edited copy's own verdicts |
| `contents.md` | per record, what each result holds of it: current version, history items, deletion record, copies |
| `meanings.md` | per record, each holding of `contents.md` with its number of cells and its verdict under each rule |
| `shortest.md` | per rule, every sequence that ends wider, by its number of steps |
| `boundary.md` | every verdict that differs between A's title edit in the same second as B's edit and 1800 seconds either side |
| `checks.txt` | what holds over every file: the decided state under each rule, the witnesses, the recycle bin, the controls |
| `xc-times.tsv` | each `keepassxc-cli` merge: exit code, whether it wrote the target, seconds; and a single load per release |
| `phases.txt`, `tools.txt` | each phase's seconds; KeePass's hash and each `keepassxc-cli` signature |

`phases.txt` prints seconds with the machine's `de-DE` group separator: `7.041 s` is 7,041 seconds.

## Runtime

7,955 seconds in the first run and 12,487 in the second, from the command's start to its end,
nearly all of it the slow derivation: making the slow copies took 888 and 862 seconds and the
`keepassxc-cli` merges 7,041 and 11,558, while the KeePassLib merges took 16 seconds in each run.
Other work shared the machine during both runs, more of it during the second.

## The figures the report quotes

"Same" in the last two columns means the second run's file holds the same line.

| Figure | Report | Recorded | Second run | Reproduced |
| --- | --- | --- | --- | --- |
| cells, merge cells, distinct files | 3 | 4,120, 3,708, 926 (`checks.txt`) | 4,120, 3,708, 926 | yes |
| scenarios, and copies read as they are | 3 | 206, 412 (`cells.tsv`) | 206, 412 | yes |
| every rule reads the decided state from the unedited copy | 1.3 | 5 of 5 (`checks.txt`) | 5 of 5 | yes |
| every file holds both witnesses, and `g:bin` is the recycle bin in every file | 1.3, 4 | 4,120 of 4,120 each; 0 other bins | 4,120 of 4,120 each; 0 | yes |
| the content tables of the five records are the same cell for cell; the four releases differ in no cell | 4 | 5 of 5; 0 | 5 of 5; 0 | yes |
| every holding in the report's section 4 tables and its number of cells | 4 | `contents.md`, `meanings.md` | the same files | yes |
| sums of those files that sections 4.1 and 7 quote: B's edited content current in 14, 14, 2 and 8 of 18 merges by time condition; 920 refusal cells and 24 control cells in which R4 refuses the store beyond the other rules; the 1,440 merge cells of the revocation's and the retirement's edits | 4.1, 7 | as listed | the same | yes |
| R1: wider / unchanged / refusing, refusal cells, control cells, no-edit cells | 5 | 486/2232/162, 0/558/162, 0/108/0 (`counts.md`) | the same | yes |
| R2, the same | 5 | 30/2840/10, 0/558/162, 0/108/0 | the same | yes |
| R3, the same | 5 | 486/2232/162, 0/558/162, 0/108/0 | the same | yes |
| R4, the same | 5 | 30/1920/930, 0/534/186, 0/108/0 | the same | yes |
| R5, the same | 5 | 324/2556/0, 0/558/162, 0/108/0 | the same | yes |
| every count by edit and record, and every verdict of the edited copy alone | 5 | `counts.md` | the same file | yes |
| R1 and R3 give the same verdict in every cell | 5 | 4,120 of 4,120 (`verdicts.tsv`) | 4,120 of 4,120 | yes |
| every sequence that ends wider, and the shortest for each rule | 6 | `shortest.md` | the same file | yes |
| verdicts that depend on the same second; in each, the same-second verdict is a neighbor's | this file | 1,075 rows (`boundary.md`) | 1,075 rows, the same file | yes |
| `keepassxc-cli` merges: number, exit codes, targets left as they were | 3 | 1,648, all 0, 568 (142 for each release) | 1,648, all 0, 568 (142 for each release) | yes |
| a single `keepassxc-cli` load of a slow copy | 3 | 3.59, 3.59, 3.95 and 3.34 s for 2.4.2, 2.6.6, 2.7.12, 2.8.0 | 3.73, 3.60, 4.27 and 3.12 s | differs: a time; every load over 3 seconds in both runs |
| a `keepassxc-cli` merge of slow copies | 3 | 5.50 to 26.06 s, median 15.9 | 9.08 to 98.15 s, median 21.61 | differs: a time, under heavier load from other work |
| no-edit merge cells that are not unchanged; control cells that are wider | 5 | 0; 0 (`checks.txt`) | 0; 0 | yes |
| the run's time, from the command's start to its end | this file | 7,955 s | 12,487 s | differs: a time |

## The cells that depend on a second boundary

**The same second.** `boundary.md` lists, per rule, every edit and merge whose verdict with A's
title edit in the same second as B's edit differs from its verdict with A's edit 1800 seconds later
or earlier: 1,075 rows over the five rules. In every row the same-second verdict is the verdict of
a neighbor, never a third verdict. Which neighbor it follows is the merge's rule at equal times:
KeePassLib's Synchronize and Overwrite if newer take the arriving copy's version, so B's edit wins
into A and loses into B; `keepassxc-cli` keeps the target's version; Overwrite existing and Keep
existing do not compare times. Every `same` cell of `contents.md` is therefore a cell that
a shift of a single second could change.

**The two loads.** The slow derivation puts the loads of a `keepassxc-cli merge` at least 3.1
seconds apart (`xc-times.tsv`, a single load per release); every `keepassxc-cli` cell depends on
that. What a load in the same second changes, the database's custom data, carries nothing of this
store, and the fast derivation was not run for `keepassxc-cli` here.

## What was not run

- **A KeePass or KeePassXC window**: every edit is KeePassLib standing for the entry dialog, a move
  standing for KeePass's move and Delete as `crdt` and `grant-only` stand for them, and every
  KeePass merge is `PwDatabase.MergeIn`.
- **A phone program**, kdbxweb and pykeepass. "Deleted without a record" stands for a program that
  writes none; no such program ran.
- **An edit that leaves the target intact**: the changed character is the last character of the
  target, so every edit of the payload breaks both the check and R1's recognition, and R1 and R3
  come out alike. A change elsewhere in the payload, or in `KPG.Kind`, was not run.
- **More than a single edit**, and so a history trimmed past the edit: every edited entry holds a
  single history item before the merge.
- **The authority, a profile addition, a rule version, a renewal answer and a reference**: none was
  edited, and the last three are not built.
- **A group made the recycle bin by a merge**: the marked group is the database's recycle bin in
  every file, so the marker's verdict is the marker's own (the report's section 1.3).
- **Database Maintenance**, Delete Duplicate Entries and Import.
- **Three copies**, chains of merges, and the fast derivation for `keepassxc-cli`.

## Changes made to run it here

None: the family was written on this branch and recorded from it. Between the first run and the
record, `scripts\verdicts.py` gained `meanings.md`, the edited copy's table in `counts.md` and two
lines of `checks.txt`, and `verdicts.tsv` lost four columns that repeat the cell's name; it was run
again over the first run's folded dumps, which it does not change, and the second run ran it as it
is.

## Machine paths replaced

None in `recorded\`: the dumps label every entry and group, `xc-times.tsv` names scenarios, and
`tools.txt` names the signer. A search of `recorded\` for `C:\`, `Users\`, `AppData` and
`scratchpad` finds nothing. The raw dumps under `out\raw\` and each cell's `merge.txt` under
`work\`, which name the run's paths, are not carried.

## The history extension: R6 and the limits of history

Added on 2026-11-16 for the report's section 9. Everything above stays as recorded; this part has a
command, an output and a record of its own.

**What it measures.** Rule R6, in `rules.py` beside R1 to R5: every record carries the check; a
record counts in its current version when that version passes, or else in its newest history item
that passes; a store entry with no passing version anywhere makes the whole store refuse. It holds
for what grants as for what refuses, and counts no witness. Every rule is read over five parts,
each on all five records, merged B into A and A into B by KeePassLib's five methods and the four
`keepassxc-cli` releases, with A untouched unless the part names A:

1. **History limits**: B's dialog edit of a character of the payload, then N-1 dialog edits of the
   title, N in 1, 2, 3, 10, 11 and 12, under the database's limits at KeePass's defaults (10 items,
   6 MiB), a single item, no item, none at all (-1) and a single byte.
2. **Database Maintenance**: KeePassLib standing for its "Delete history entries older than (days)",
   which removes every history item that many days old or older, in B after the edit (30 days,
   which takes the written version, and 365, which keeps it), or in the result after the merge (30).
3. **An edit that leaves no history**: the four payload edits by KeePassLib with the backup left
   out, the modification time moved; and a character changed or `KPG.Data` deleted by pykeepass
   4.2.0 through `set_custom_property` and `delete_custom_property` (`scripts\pk_edit.py`).
4. **An edit in both copies**: B's dialog edit of the target's last character, and A's edit of its
   first character with no history item, 1800 seconds after B's, 1800 before, and in the same
   second.
5. **A history item tampered with**: after B's dialog edit, a script changes the first character of
   the target in the history item, or removes the history item, leaving the entry's times as they
   are.

**The derivation.** Every copy here uses the fast derivation, `keepassxc-cli` included. What the
two loads' seconds decide is the database's custom data, and no record of this store lives there,
so no cell of the extension can depend on them; the slow derivation was therefore run for none.

### The command

From the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\edited-refusals\run-history.ps1
uv run --no-project python -I -X utf8 spikes\storage\probes\edited-refusals\scripts\compare.py spikes\storage\probes\edited-refusals\recorded\history spikes\storage\probes\edited-refusals\out\history
uv run --no-project python -I -X utf8 spikes\storage\probes\edited-refusals\scripts\compare.py spikes\storage\probes\edited-refusals\recorded\history\part0 spikes\storage\probes\edited-refusals\out\history\part0
```

`run-history.ps1` makes every case (`Edr.exe history`, then `scripts\pk_edit.py`), dumps and merges
them as `run.ps1` does, folds the dumps, and judges them with the single script
`scripts\history.py`, which also judges part 0's record again under all six rules. It sets
`UV_EXCLUDE_NEWER` to `2026-11-16T00:00:00Z`, so that pykeepass resolves to 4.2.0 again.

### Output

Fresh into `out\history\`, recorded in `recorded\history\`, the raw dumps left out:

| File | What it holds |
| --- | --- |
| `versions.jsonl`, `rows.jsonl`, `frames.jsonl`, `states.jsonl`, `cells.tsv` | the extension's dumps, folded as part 0's are |
| `verdicts.tsv` | each cell's verdict under each of the six rules |
| `counts.md` | per part and rule, wider / unchanged / refusing over the refusal cells, the control cells and the edited copy alone; then per edit |
| `limits.md` | part 1's edited copy alone: per limit and number of edits, the history items, whether the written version is among them, and each rule's verdicts |
| `holdings.md` | per part and edit, what each result holds of the revocation; the other records hold the same (`checks.txt`) |
| `shortest.md` | per part and rule, the shortest sequence that ends wider |
| `checks.txt` | the decided state under each rule, the records alike, the releases alike, what merges bring back where B kept no written version |
| `pykeepass.txt`, `xc-times.tsv`, `phases.txt` | pykeepass's release and the history it left; each merge's exit code; each phase's seconds |
| `part0\` | part 0's tables again under all six rules: `counts.md`, `meanings.md`, `shortest.md`, `boundary.md`, `checks.txt` |

Part 0's `verdicts.tsv` under six rules and its `contents.md`, which no rule changes, are rebuilt by
the command and left out of the record.

### Runtime

452 seconds in the first run and 521 in the second, the build included.

### The figures the report quotes

| Figure | Report | Recorded | Second run | Reproduced |
| --- | --- | --- | --- | --- |
| cells, merge cells, distinct files | 9.2 | 4,400, 3,960, 711 (`checks.txt`) | the same | yes |
| every rule reads the decided state from the unedited copy; the five records hold the same; the releases agree | 9.2 | 6 of 6; yes; 0 differ | the same | yes |
| R6 in part 0: refusal, control and no-edit cells | 9.3 | 30/2840/10, 0/710/10, 0/108/0 (`part0\counts.md`) | the same | yes |
| part 1, R1 to R6: refusal, control and edited-copy counts | 9.3 | `counts.md`, part 1 | the same | yes |
| part 2, the same | 9.3 | `counts.md`, part 2 | the same | yes |
| part 3, the same | 9.3 | `counts.md`, part 3 | the same | yes |
| part 4, the same | 9.3 | `counts.md`, part 4 | the same | yes |
| part 5, the same | 9.3 | `counts.md`, part 5 | the same | yes |
| the edit at which the written version leaves the history, per limit | 9.4 | `limits.md` | the same | yes |
| where B kept no written version: of 504 revocation merges, the written version current in 96, a history item in 89, a copy under a new identifier in 28, gone in 291 | 9.5 | `checks.txt` | the same | yes |
| every holding the report's section 9 cites | 9.4, 9.5 | `holdings.md` | the same | yes |
| no sequence ends wider under R6 in any part; part 0's shortest under R6 | 9.6 | `shortest.md`, `part0\shortest.md` | the same | yes |
| pykeepass 4.2.0: no history item, modification time kept | 9.2 | `pykeepass.txt`; the dumps | the same | yes |
| `keepassxc-cli` merges: number and exit codes | 9.2 | 1,760, all 0 (`xc-times.tsv`) | the same | yes |

### The cells that depend on a second boundary

Part 4's `same` cells, and every part 3 cell of pykeepass, whose edit keeps the entry's
modification time and so meets copy A's version at the same second: there KeePassLib's Synchronize
and Overwrite if newer take the arriving copy's version and keep the other nowhere, and
`keepassxc-cli` keeps the target's (`holdings.md`). No cell depends on the loads' seconds (above).

### What was not run

- A KeePass, KeePassXC or phone window; KeePassXC's own `edit`; a phone program's edit.
- KeePass's Database Maintenance itself: the probe stands for it with the measured behavior of the
  desktop measurement, and the history items' ages come from the run's clock (the written version
  is 319 days old on 2026-11-16).
- More than a single edit in A, maintenance in A before a merge, and three copies.
- The slow derivation, for the reason given above.

### Changes to the family

All additive. `rules.py` gained R6. `scripts\verdicts.py` gained an output directory and `--only`,
and `run.ps1` passes `--only R1,R2,R3,R4,R5`, so that part 0's tables come out as recorded: run
over `recorded\` they matched it byte for byte. `probe\Program.cs` gained the command `history`, a
switch that leaves the backup out (on by default), and a step after a merge for scenarios that name
such a step; part 0's scenarios name none, and part 0 was not run again after the change. Both runs
of the extension called that command by an earlier name, changed afterwards for the spelling check.
`scripts\history.py` writes `-` rather than an empty cell in `shortest.md` since both runs, and was
run again over both runs' folded dumps, which it does not change; the two still agree in every
file. `run-history.ps1`, `scripts\pk_edit.py` and `scripts\history.py` are new.
