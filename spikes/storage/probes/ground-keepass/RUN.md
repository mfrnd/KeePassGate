# ground-keepass: the probes and what they reproduce

The family behind `papers/ground/ground-keepass.md`, the ground report on what KeePass 2.62 itself
does to each place KeePassGate's data could live. One probe ran for it, in thirteen experiments
that the report numbers X1 to X13, and the report also quotes the hash of the KeePass source it
read. The inventory's probe is a family of its own, `ground-inventory\`.

| Directory | Probe | What it measures | Cited by |
| --- | --- | --- | --- |
| root | `probe\Program.cs` | thirteen experiments over KeePass 2.62's KeePassLib: entries added and changed in two copies under all five merge methods both ways, deletions and moves, tombstones, history limits, groups, the file version each feature needs, the header's public custom data, custom icons, two maintenance calls behind KeePass's menus, and search | `ground-keepass.md` §1 to §10 |
| root | `source.ps1`, added here | the SHA-256 and size of `KeePass-2.62-Source.zip` against what `keepass.info/integrity.html` publishes, and the source tree it extracts into `src\` | `ground-keepass.md`, "What was run"; and every paper that reads KeePass's source under `spikes\storage\probes\ground-keepass\src` |
| root | `checks\compare.py` and `checks\x2.py`, added here | a fresh run held to the record line by line, and the table of §1a and the claim about seven kinds of change held to X2 cell by cell | `ground-keepass.md` §1a, §1c, §4, §6 |

Everything ran in this run. The thirteen outputs match the record line for line in two runs, apart
from the start time on each first line and, in X12, the one number that is the run's own clock.
Every number and outcome the report quotes from them holds, the table of §1a in each of its cells
for each of the seven kinds of change, and the few sentences broader than their output are named
in the notes. The source archive hashes to the value the report quotes, in a fresh download and
in the copy the report's run kept, and the integrity page publishes that value today. The claims
the report marks as measured with no probe behind them are listed apart at the end.

## Running them

From the branch root, in Windows PowerShell 5.1, which is what the probe was measured with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-keepass\run.ps1
uv run --no-project python -I -X utf8 spikes\storage\probes\ground-keepass\checks\compare.py
uv run --no-project python -I -X utf8 spikes\storage\probes\ground-keepass\checks\x2.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\ground-keepass\source.ps1 -Integrity
```

`run.ps1` provides and checks KeePass 2.62 through `tools\KeePass.ps1`; builds the probe with
`dotnet build`, which needs the .NET SDK and the package
`Microsoft.NETFramework.ReferenceAssemblies` 1.0.3; clears `work\`, and so the databases of the
run before; and runs each experiment with a work directory of its own, `work\x1` to
`work\x13`, as the recorded runs did, into `out\<name>.txt`. `-Experiment x2,x5` runs only those.
`work\` and `out\` are ignored by the branch, and the record each output is compared with is
`recorded\<name>.txt`. Nothing shows a window: KeePassLib is used as a library.

`compare.py` masks two things expected to differ in every run, the start time `T0` and the group
identifiers KeePassLib draws at random and X10 prints, and prints every other line that differs.
`x2.py` checks `out\x2.txt`, or the file an argument names, `recorded\x2.txt` for the record.
`source.ps1` fetches the archive from SourceForge unless `-Archive` names an archive on disk,
checks it either way, refuses to extract an archive that is not the report's, and with
`-Integrity` fetches the integrity page into `out\` and checks what it publishes about the file. It
writes `out\source.txt`.
`src\` is ignored by the `.gitignore` beside it.

How long each ran here, in two runs, with the machine busy with other measurements (about half the
processor in use during the second). The report states no time; the recorded column is each
output's modification time less the start on its first line, which is to the second.

| Run | Here | Recorded |
| --- | --- | --- |
| X1 | 0.7 s, 0.8 s | under 1 s |
| X2 | 28.3 s, 23.6 s | about 20 s |
| X3 | 4.2 s, 3.3 s | about 3 s |
| X4 | 1.3 s, 1.0 s | about 1 s |
| X5 | 3.3 s, 2.7 s | about 2 s |
| X6 | 1.2 s, 0.9 s | about 1.5 s |
| X7 | 0.5 s, 0.4 s | about 1 s |
| X8 | 1.0 s, 1.1 s | about 1 s |
| X9 | 0.8 s, 0.7 s | under 1 s |
| X10, X11, X12 | 0.1 s each, both runs | under 1 s each |
| X13 | 3.4 s, 3.1 s | about 3 s |
| `source.ps1` with the archive on disk | 44 s, most of it the extraction | not a script there |

A build takes one to two seconds before the experiments. The download itself took 30 seconds from
one SourceForge mirror; an earlier attempt through `source.ps1` stalled on another mirror for 247
seconds and broke off with "unexpected EOF", so the run in `out\` was given the downloaded archive
with `-Archive`.

**How a result is judged.** "Yes" is that this run gives what the report quotes. "Differs" is that
it gives something else, with the note stating whether that is noise, the environment or the claim.
"No" is that neither the record nor this run gives what the report states. Where the record and this
run agree and the report's conclusion holds, but its sentence is broader than the output or passes
over a cell, the result is "yes" and the note names the part that is not in the output.

## The probe, `probe\Program.cs`

It makes every database itself, new for each case, with invented content and AES-KDF at one
round, and saves, merges, saves again and reopens them through KeePassLib alone. Each merge cell
opens the target file, opens the source file, runs `MergeIn`, saves the result to a new file and
reports what the reopened file holds. Every time is set explicitly in whole seconds and printed as
an offset from `T0`, two hours before the run started, so the outputs compare across runs. It
takes a work directory, the experiment (`x1` to `x13`, or `all`) and the output file.

All sections below are of `papers/ground/ground-keepass.md`. In X2's lines, `cur` names whose
change the current version holds and `hist` whose change some history item holds.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| K1 | KeePassLib 2.62 loaded from `KeePass.exe`, x64, .NET Framework runtime 4.0.30319.42000, no window | "What was run" | first line of every file | `KeePassLib 2.62, ... process x64, 4.0.30319.42000` | the same | yes | the `KeePass.exe` the record ran with, still in its build output, hashes to the SHA-256 of `.keepass\2.62\KeePass.exe`, `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3` |
| K2 | the seven kinds of change (a custom string, a protected string, one field to two values, entry custom data, an attachment, a tag, JSON in Notes) give identical outcomes in all 120 cells, 6 time conditions by 5 methods by 2 directions by 2 backup variants | §1a; §4 first bullet; §6 | `x2.txt`, the `X2S` lines; `checks\x2.py` | identical in 120 of 120 | the same | yes | the report's grouping script was not kept, and `x2.py` does the comparison. The probe has an eighth kind, `only-A-changes`, over three time conditions, which the report does not count |
| K3 | the table of §1a, six time conditions by six columns, Synchronize and Overwrite if newer together | §1a | `x2.txt`, the `backup` lines | every cell as the table prints it, for all seven kinds | the same | yes | 48 cells a kind with the two methods counted apart. `x2.py` also checks the times the table emphasizes, "(older)" and "(+0)". "hist stale" is read off the full line, whose history holds an item at `+30`, since `hist` marks only changes |
| K4 | Create new IDs, every row: two entries, one holding A's change and one B's, each with only its own history | §1a | `X2S` lines, `NewUuids` | two such entries in all 12 cells, for all seven kinds | the same | yes | in the last row B made no change, so the second entry is B's unchanged copy, `cur=-` |
| K5 | strictly newer wins and the loser goes to history; equal times lose a version outright; a time rewritten forward makes stale content win; Keep existing keeps the open entry and loses a newer arriving entry | §1a bullets; §1c | `x2.txt` | as the table | the same | yes | each is a row or a column of K3 |
| K6 | Overwrite existing ignores times: the arriving copy wins whole and the open copy goes to history | §1a bullet 5 | `x2.txt` | as the table | the same | yes | the open copy does not go to history in three cells the table itself marks lost: neither time moved, in both directions, and B's time not moved, into B. In each the history already holds an item at that time (§1a bullet 3, §1c) |
| K7 | without the backup, the row where B's time did not move keeps B's change in history into A | §1a bullet 3 | `nobackup`, `B-time-not-moved`, into A | `cur=A hist=B`, Synchronize and Overwrite if newer | the same | yes | |
| K8 | under Overwrite existing the loser stays in history only if no item already holds its time | §1c | `neither-time-moved`, Overwrite, both variants | lost with the backup and in history without it, both directions | the same | yes | |
| K9 | an entry with no `LastModificationTime` reads as `PwDefs.DtDefaultNow`: `lm=+7201`, equal to it | §1c | `x12.txt` | `lm=+7201 PwDefs.DtDefaultNow=+7201 now=+7201` | `lm=+7200 PwDefs.DtDefaultNow=+7200 now=+7200`, both runs | differs | noise: the number is the moment the run read the entry, two hours after `T0`, rounded to the second. That the entry's time equals `DtDefaultNow` holds in both |
| K10 | two different entries added to one group, one in each copy, always both arrive, all five methods, both directions, content differing or identical; Create new IDs duplicates the rest; only the order differs by direction | §1d; §10 row 1 | `x1.txt` | `XA` and `XB`, and both `Same`, in every cell; the order `XB XA` into A and `XA XB` into B | the same | yes | |
| K11 | §1e: twelve rounds, 24 fields written; no preprocessing, B 5 s later: 22 of 24, the entry holding 12, `A01` and `A02` lost | §1e | `x13.txt` | `written=24 inEntry=12 inEntryOrHistory=22 hist=10 lost=KPG.A01,KPG.A02` | the same | yes | |
| K12 | no preprocessing, the same second: 12 of 24, every field of the side that lost each tie | §1e | `x13.txt` | `inEntryOrHistory=12`, lost `B01`, `A02`, `B03` and so on, one a round | the same | yes | |
| K13 | with preprocessing, B 5 s later: 24 of 24, all in the entry | §1e | `x13.txt` | `inEntry=24 inEntryOrHistory=24` | the same | yes | |
| K14 | with preprocessing, the same second: 12 of 24 | §1e | `x13.txt` | `inEntryOrHistory=12`, the same fields lost as K12 | the same | yes | |
| K15 | with preprocessing and `HistoryMaxItems=0`: 4 of 8 after 4 rounds, every A field lost | §1e; §1b last row; §4 last bullet | `x13.txt` | `written=8 inEntryOrHistory=4 lost=KPG.A01,KPG.A02,KPG.A03,KPG.A04` | the same | yes | |
| K16 | with preprocessing and `HistoryMaxItems=1`: 8 of 8 | §1e | `x13.txt` | `written=8 inEntryOrHistory=8 lost=` | the same | yes | |
| K17 | three copies each adding a field, merged into the first: the newest copy's field in the entry, the other two in history | §1e | `x13.txt` | `inEntry=KPG.C2 inEntryOrHistory=KPG.C0,KPG.C1,KPG.C2` | the same | yes | |
| K18 | an identical value rewritten with a new time: 12 rounds leave 10 history items, all one value | §1b row 7 | `x13.txt` | `hist=10 distinctValuesInHistory=1` | the same | yes | |
| K19 | the history defaults: 10 items, and `size limit 6291456` | §1b row 1 | `x5.txt` | `size limit 6291456`; the count stops at 10 from round 10 | the same | yes | the defaults themselves are also read by reflection in `ground-inventory\`, I10 |
| K20 | a file saved holding 9 history items under a limit of 3 reads back with 3 | §1b row 2 | `x5.txt`, "A as saved with limit 3" | `n=3` | the same | yes | the 9 is how the probe built it, eight backups in the base and one in A, and is not printed. The probe does not tell a trim at save from a trim at load, which both call `MaintainBackups` (`KeePassLib/Serialization/KdbxFile.Write.cs:103`, `KdbxFile.Read.cs:255`, read here) |
| K21 | the oldest item goes first: round 11 drops revision 0, round 12 revision 1 | §1b row 3 | `x5.txt` | `revs=1,...,10` and `revs=2,...,11` | the same | yes | |
| K22 | the target's limit applies in a merge, and a merge does not carry it: 3 items into the copy with limit 3, 9 into the copy with limit 10 | §1b row 4 | `x5.txt` | `n=3 limitAfter=3`; `n=9 limitAfter=10` | the same | yes | |
| K23 | with a 1 MiB attachment, 10 synchronized edits leave 5 history items | §1b row 5 | `x5.txt` | `n=5 revs=5,6,7,8,9` | the same | yes | |
| K24 | history items with the same time collapse: into A keeps `a1`, into B keeps `b1`, and Overwrite existing takes the arriving item | §1b row 6 | `x5.txt`, "colliding history times" | `a1` into A and `b1` into B under Synchronize, Overwrite if newer and Keep existing; `b1` into A and `a1` into B under Overwrite existing | the same | yes | |
| K25 | KeePass 2.62 writes no file below KDBX 4.0, an empty database with AES-KDF included | §2.1 | `x7.txt` | `empty database \| 4.0` | the same | yes | "never" rests on the reading of `KdbxFile.cs:351-397`; the probe shows the cases it ran |
| K26 | the version each feature needs: 4.0 for the entry features, entry and group custom data, tombstones, the header's public custom data, an icon with neither name nor time, and a group's notes, searching, auto-type and collapsed state; 4.1 for an entry kept out of reports, a group tag, and an icon with a name or a time | §2.1; §3 row 5; §5 row 8; §7 row 2; §8 row 3 | `x7.txt` | each line as listed | the same | yes | "and all of it read back": after reopening, the line prints the strings, the attachment, the tag, the expiry and the history item; the icon, the colors, the override URL and the auto-type switch are set but not printed |
| K27 | a previous parent and nothing else of 4.1 gives 4.0 and is dropped; beside another 4.1 feature it gives 4.1 and is kept | §2.1 | `x7.txt` | `4.0 previousParentAfterReopen=False`; `4.1 previousParentAfterReopen=True` | the same | yes | the other feature in the probe is a group tag |
| K28 | the table of §2.2: deleting, recycling and moving an entry, and a deleted group, under each method, ten rows | §2.2; §5 rows 3 to 5 | `x3.txt`, `x4.txt` | every cell as quoted | the same | yes | two things the cells pass over: Keep existing into A also loses B's later change in the moved row, and the tree prints how many history items an entry has, not which versions they hold |
| K29 | Create new IDs makes the arriving root an empty group named after the arriving database, adds the arriving top-level groups again at the open root, and every group and entry is then present twice | §2.4; §9 "File then Import" | `x1.txt`, `x3.txt` | `new[]` with no entries or subgroups, and `groups=KPG/new/KPG`, `groups=KPG/G2/G3/Bin/new/KPG/G2/G3/Bin` | the same | yes | every database the probe makes has the root `new`, from the file name it creates them with, so "named after the arriving database" cannot be told here from "named like the open root"; the name rests on the reading of `PwDatabase.cs:804-805` |
| K30 | a deleted entry comes back under Create new IDs as a copy under a new identifier | §2.4 | `x4.txt`, E deleted on B, into B, `NewUuids` | `KPG[] new[] KPG[E{Rec=0}@+0h0]` | the same | yes | |
| K31 | a group merges whole from the strictly newer copy, its custom data with it | §3 row 1; §4 second bullet | `x6.txt` | every field of `KG` from the newer copy, in both directions | the same | yes | groups keep no history, which the output cannot show and the report reads |
| K32 | at equal times the arriving group wins whole: into A, B's tags and custom data; into B, A's notes | §3 row 2 | `x6.txt`, both at `+10` | as quoted | the same | yes | |
| K33 | Overwrite existing takes the arriving group whatever the times; Keep existing keeps the open group | §3 row 3 | `x6.txt` | as quoted | the same | yes | |
| K34 | `IsExpanded` is never merged: each copy keeps its own, and a group new to one copy arrives expanded | §3 row 4 | `x6.txt` | `KG` collapsed in every result into A and expanded in every result into B; `NewOnA` collapsed into A and expanded into B | the same | yes | |
| K35 | Delete Empty Groups deletes 4: a group holding only notes and custom data, an empty recycle bin, an empty child and its parent | §3 row 9; §3 last paragraph; §9 | `x10.txt` | `deleted=4`, only `KPG-with-entry` left, `tomb=4` | the same | yes | the probe calls `PwDatabase.DeleteEmptyGroups`; "with no confirmation found" is a reading of the menu's handler |
| K36 | a tombstone naming nothing survives save and reopen | §5 row 2 | `x4.txt` | `tomb=1` | the same | yes | |
| K37 | only Synchronize carries a tombstone to the other copy | §5 row 3; §2.2 | `x4.txt`, the orphan tombstone into B | `tomb=1` under Synchronize, `tomb=0` under the other four | the same | yes | |
| K38 | the open database's own tombstones apply under every method, to arriving objects too | §5 row 4; §2.2 | `x3.txt`, `x4.txt` | an arriving entry older than the tombstone gone from every result into the deleting copy, under every method | the same | yes | under Create new IDs a copy of it arrives under a new identifier, K30 |
| K39 | a tombstone goes when its object is present and newer, or is a group that is not empty | §5 row 5; §2.2 | `x3.txt`, `x4.txt` | `tomb=0` where the other copy changed the object later; G2's own tombstone gone and E2's kept, `tomb=1`, where B added N to G2 | the same | yes | |
| K40 | two entries differing only in an attachment's name are not duplicates | §6 | `x10.txt` | `deleted=0` | the same | yes | |
| K41 | the header's public custom data is read by `PwDatabase.LoadHeader` with no key | §7 row 1 | `x7.txt` | `visible` | the same | yes | |
| K42 | the header's public custom data merges as a union of keys; for a key both hold, the later `SettingsChanged` wins and at equal settings the open copy; Overwrite existing takes the arriving values and Keep existing copies nothing; a value changed under the older settings is lost even to an unchanged value; no method removes a key | §7 rows 3 to 5 | `x8.txt` | as quoted; with A's settings at `+10` and B's at `+20`, `k0=v0` | the same | yes | no copy removed a key in the probe, so what "no method removes a key" rests on here is that no result lacks a key the open copy held |
| K43 | a deleted icon leaves a tombstone that Synchronize applies on the other side and the other methods leave; a name or a time on an icon gives 4.1 | §8 rows 2 and 3 | `x9.txt`, `x7.txt` | into B, `I2,I1 tomb=1` under Synchronize and `I0,I2,I1 tomb=0` under the other four; `version A 4.1` with names and times | the same | yes | |
| K44 | Delete Duplicate Entries deletes one of two entries equal in their strings whatever their custom data or tags, and across two user groups; none when the group's identifier is in a field; it deletes the older entry, and an entry in the recycle bin | §9 row 5 | `x10.txt` | `deleted=1` three times, `deleted=0` with the identifier; the newer kept of the two in one group; of the two where one is in the bin and newer, the entry in the bin deleted | the same | yes | |
| K45 | a search skips entries in a group with searching off, by default | §9 row 10 | `x11.txt` | nothing found with `RespectEntrySearchingDisabled=True`, which the defaults print | the same | yes | |
| K46 | a field reference's search ignores the searching switch and finds the KPG entry | §9 row 11 | `x11.txt` | `first=kpg-store` | the same | yes | the probe searches with the parameters it reads `SprEngine.FindRefTarget` to set; it does not compile a `{REF:...}` through `SprEngine`, so the reference itself is the reading of `SprEngine.cs:627-665` |

## The added script, `source.ps1`

The report quotes the archive's hash and size as measured and as what the integrity page
publishes, and kept no output of that measurement, only the archive and a saved copy of the page.
`source.ps1` is written here to measure both again and to provide the source tree the papers read.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| K47 | `KeePass-2.62-Source.zip` from SourceForge has SHA-256 `9C07B0539A013EE5E22B26FD806E472C1858BC2F3F7B3B4406F4404774263AC0` and 5,339,759 bytes, which is what `keepass.info/integrity.html` publishes for it | "What was run" | `out\source.txt` | no output; the archive and the page the report's run kept give the same hash, size and listing | equal for a fresh download from a second mirror and for the archive the report's run kept; the page fetched on 2026-11-15 lists that hash and `Size: 5339759 B` for the file | yes | the tree it extracts, 1270 files, is identical to the tree the report's run extracted and the papers read |

## Claims no probe here backs

**Presented as measured, with the measuring code not kept.** Both are reproduced by scripts added
here, so they are vouched for, but by code written after the report:

| Claim | Quoted in | What was found |
| --- | --- | --- |
| "grouped by a script that compared every cell across the seven" | §1a | no such script was in the report's directory. `checks\x2.py` does the comparison, K2 |
| the source archive's hash and size, "[measured]" | "What was run" | no command or output was kept, only the downloads. `source.ps1` measures them, K47 |

**Measured elsewhere, and not run here.** "The spike measured 2.47 writing 3.1" (§2.1), "spike
section 1 for 2.61.1" (§4) and the database custom data rule "the spike measured on 2.61.1" (§4)
are measurements of the branch `spikes/revocation-merge`, which `papers/ground/ground-measured.md`
digests.

**Read, not measured.** Every `[read file:line]` is a reading of the source `source.ps1`
extracts, and no probe is owed for it. Four that the measured claims lean on were looked at
here and bear out the report: `PwDatabase.cs:47-48` (the defaults, `internal`),
`KdbxFile.Write.cs:103` and `KdbxFile.Read.cs:255` (the history trim at save and at load), and
`PwDatabase.cs:940` (the trim at the end of a merge). The rest were not checked.

## Changes made to run them here

- **`probe\Program.cs` is unchanged**, byte for byte. Its file is dated after X1 to X12 were
  written, 15:52 against 15:45 to 15:47 of the same afternoon, and only X13 is later. Both runs
  here, from the source as kept, give X1 to X12 again line for line, so whatever was changed after
  them did not touch their output.
- **`probe\Probe.csproj`** references `$(KeePassExe)` from `spikes\storage\Directory.Build.props` in
  place of the `KeePass.exe` of the product's worktree, and the comment above the reference, which
  named that worktree, records the change. Nothing else in it changed: the same `net48`, x64,
  C# 7.3, and `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3.
- **`run.ps1` is added**, to provide KeePass, build, clear `work\` and pass each experiment the
  arguments the recorded runs used, `<work directory> <experiment> <output file>`, with the work
  directories as `work\x1` to `work\x13` where the recorded runs had `work-x1` to `work-x13`.
- **`source.ps1`, `checks\compare.py`, `checks\x2.py` and `.gitignore` are added**, as above.

**Recorded output.** The thirteen files in `recorded\` are byte for byte the files the report
quotes. None holds a machine path, user name or session identifier, so nothing was replaced. The
group identifiers X10 prints are random identifiers of throwaway databases.

## Not carried

- **Every database**: 1560 `.kdbx` files in the thirteen work directories of the recorded runs, all
  made by the probe, which makes them again. Those directories held nothing else.
- **The build output** in `probe\bin\` and `probe\obj\`: `Probe.exe`, rebuilt from the source, and a
  copy of `KeePass.exe` with the hash the branch provides.
- **KeePass's source**, the extracted `src\` and the archive `src-download\KeePass-2.62-Source.zip`:
  somebody else's code, which `source.ps1` provides and checks.
- **`integrity\integrity.html`**, keepass.info's page as saved during the report's run, which
  `source.ps1 -Integrity` fetches again.

## Caveats and defects

- **No defect was found in this family's probes.** The inventory's `sizes.ps1` prints the history
  defaults empty, a defect that changes no quoted number; see `ground-inventory\RUN.md`.
- **KeePass's window is not reproduced**, as the report itself states: the merge cells stand for
  File then Synchronize because it runs the same `MergeIn`, and the dialogs, Delete Duplicate
  Entries and Delete Empty Groups are reached through the KeePassLib calls behind them.
- **Every time is set by the probe, in whole seconds.** Sub-second times, which the report names as
  not run in §1c and §11, are not covered by any cell.
- **X2's `cur` and `hist` see only a change**, so a version that differs from the base only by its
  time is not marked in them; "hist stale" in K3 is read off the full line instead.
- **`src\` is ignored by a `.gitignore` in this directory**, because the papers cite the source at
  `spikes\storage\probes\ground-keepass\src` and the root `.gitignore` ignores no `src`.
