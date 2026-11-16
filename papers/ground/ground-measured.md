# Ground report: what is already measured (key `measured`)

What the runs on `spikes/revocation-merge` established about every KDBX feature a new storage
design could rest on, digested for the storage rethink of 2026-11-15. The subject is the existing
measurements, not a program: nothing new was run against any program here. What is new is a
re-read, with KeePassLib 2.62, of the result files those runs left behind, which prints what the
recorded outputs never printed: every string of every entry, and the content of each history item.

**Basis labels.** Every statement carries one:

- **measured (recorded)**: the spike's recorded output under `spikes/revocation/output` shows it.
- **measured (re-read)**: I read it today out of a result file a spike run left behind, with
  KeePassLib 2.62 in Windows PowerShell 5.1, by `reread.ps1` (paths in section 7). The files are
  the programs' own output; the reading is mine.
- **read**: from source, file and line named. KeePass source is cited as RESULTS.md cites it.
  KeePassXC, Keepass2Android and KeePassDX lines are from the single source files the spike's
  measurement session fetched at the tags RESULTS.md names, kept under that session's scratch directory
  (section 7); I read them there.
- **assumed**: inferred, not run and not read.

**Names used throughout.** `C` is the stale copy and `P` the copy newer in every way the plugin
reads (RESULTS.md 251-258, `Paired.cs` 14-85). Both hold the same groups `Root`, `G`, `H` and the
same entries `E1`, `E2`, with the same identifiers. `E1` differs between the copies only in its
custom data (the rule, `{"reader":["read","list"]}` in C, `{"reader":[]}` in P) and its
modification time (C 2026-01-01 10:00Z, P 2026-02-01 07:00Z). **Its strings are identical in both
copies: a title and a password, nothing else.** `G` differs the same way. Operations:

- **S**, a plain save: the program opened P, added one entry at the root, saved.
- **M-newer**, a merge into the newer copy: P open or the target, C arriving (KeePassXC "into P",
  Android "PC").
- **M-older**, a merge into the stale copy: C open or the target, P arriving (KeePassXC "into C",
  Android "CP").

RESULTS.md means `RESULTS.md` of the branch `spikes/revocation-merge`, cited by line.

## 1. The answer first

**The question every ground report answers: the same entry in both copies, each copy adding a
different custom string field.** No run did this. In every run the two versions of `E1` had the
same strings, so **nothing measured shows whether any program merges strings field by field.**
What was measured is what each program does with one entry whose two versions differ, which bounds
the answer:

| program | which version is current after a merge | what the history holds | basis |
| --- | --- | --- | --- |
| KeePassXC 2.4.1 to 2.8.0 | the newer version, whole: P's rule, P's time, both directions | exactly one item, the older version as it was: C's rule, C's time, C's strings | current: measured (recorded), 216 runs, RESULTS.md 359-360. History content: measured (re-read), 42 merge results (36 fast-KDF, 6 slow-KDF) of an earlier run of the same measurement, every release, both directions |
| Keepass2Android 1.08d-r5, 1.15-r6 | the newer version, whole, both directions, with either copy's settings later | exactly one item, the older version as it was | current: measured (recorded), 18 runs, RESULTS.md 511-512. History content: measured (re-read), all 18 |
| KeePassDX 3.2.0, 4.5.8 | the newer modification time, but **the arriving copy's rule**, whichever is newer | one item with the older modification time and **the same rule as the current entry** | measured (re-read), all 18 merge runs. Why: read, `DatabaseKDBXMerger.kt` 4.5.8 :507-515, the open entry's custom data is overwritten before it is copied into history |

So, from what exists:

- **A field added only on the older side is never in the current entry** after a KeePassXC or
  Keepass2Android merge; it is in the one history item, intact (measured for custom data; for
  strings read from source, section 5). After a KeePassDX merge the older side's custom data can be
  in neither place: with the stale copy open, C's rule exists nowhere in the result; with the newer
  copy open, P's rule exists nowhere (measured, re-read).
- **Equal modification times lose one side outright** in all three merge implementations, which no
  run exercised: read, KeePassXC `Merger.cpp` :540-566, Keepass2Android `PwDatabase.cs` :799-821,
  KeePassDX `DatabaseKDBXMerger.kt` :519-524. History items are keyed by modification time in all
  three, so two different versions stamped in the same second collapse into one (read, section 5).
- **History has never been seen with more than one item**, so no pruning was observed. All three
  merges trim history to the database's limits afterwards (read). The limits read back as
  10 items, 6 MiB and 365 days in every file, but those are KeePassLib's defaults, so "kept" and
  "dropped and read back as the default" cannot be told apart (measured, re-read; section 4.4).
- **Two different entries added to one group, one in each copy: never run.** An entry that only one
  copy held was in the result of every merge where that happened (KeePassXC, an entry only the
  arriving copy held, all six releases; Keepass2Android, an entry only the open copy held), but
  never two at once, never in a non-root group, and never an Android merge of an entry only the
  arriving copy held (measured; section 4.6). All three merges add an entry the target lacks
  (read).
- **The spike never ran a KeePass entry or group merge.** Its KeePass 2.61.1 runs merged the
  database's custom data only (`Program.cs`, `Layout.cs`); 2.62 was not run at all. What KeePass
  does to an entry is in the plugin's own host tests (read, section 4.8), not in the spike.

## 2. What exists to build on

| run | programs | what was done | counted | where recorded |
| --- | --- | --- | --- | --- |
| merge rule | KeePassLib 2.61.1 in process | `PwDatabase.MergeIn`, five methods, one database custom data key at a time | 10 cases x 5 methods | `output/rules.txt`, RESULTS.md 27-69 |
| revocation designs | KeePassLib 2.61.1 | five designs x seven conditions x five methods x two directions | 70 cells | `output/matrix.txt`, RESULTS.md 71-168 |
| authority layout | KeePassLib 2.61.1 | one key per authority, retirements, pruning; three copies in every order | 70 cells, 48 + 192 + 12 runs | `output/layout.txt`, RESULTS.md 170-213 |
| Database Maintenance | KeePassLib 2.61.1 types | the dialog's copy and write back, modeled | 7 cases | RESULTS.md 215-237 |
| KeePassXC, KeePass 2.47 saves | six KeePassXC releases, KeePass 2.47 | `keepassxc-cli add`, or KeePassLib 2.47 Open, add, Save | 35 files | `output/keepassxc-save.txt`, RESULTS.md 260-297 |
| KeePassXC KDF change | 2.7.12, 2.8.0 | the window driven by UI automation | 4 files | `output/keepassxc-kdf.txt`, RESULTS.md 299-314 |
| KeePassXC merges | six releases | `keepassxc-cli merge -s`, both directions, fresh and saved C, slow and fast KDF | 216 runs | `output/keepassxc-merge.txt`, RESULTS.md 316-364 |
| Android saves | Keepass2Android 1.08d-r5, 1.15-r6; KeePassDX 3.2.0, 3.3.0, 4.5.8; KeePassDroid 2.7 | open, add an entry, save, on the program's screens | 27 files | `output/android-save.txt`, RESULTS.md 419-468 |
| Android merges | Keepass2Android both; KeePassDX 3.2.0, 4.5.8 | each program's own merge on its screens | 37 runs | `output/android-merge.txt`, `android-screens.txt`, RESULTS.md 470-554 |

Every run in a cell read back alike: measured (recorded) for KeePassXC (I grouped the 216 lines of
`keepassxc-merge.txt` by cell: one distinct result per cell) and for Android (RESULTS.md 475-476,
`android-merge.txt` "runs 2 and 3: the same").

**The kept result files.** The Android run's work directory survives with every result file, and
all 47 files whose hash the recorded Android outputs print match it byte for byte (measured today).
The KeePassXC run that produced the recorded outputs left no files, but an earlier run of the same
measurement by the same session did: 627 files, six releases, bases made by `Kpg.cs` with the same
keys, values, times and rules as `Paired.cs`. My re-read covers 95 Android files and 77 KeePassXC
and KeePass 2.47 files: the 36 fast-KDF merge targets (run 1 of every cell), six slow-KDF merge
targets (2.4.2 and 2.8.0), the 21 saves and the bases.

## 3. The matrix

One table per feature. A cell is what happened, and where it is recorded. "Not run" means no run
covered it. Program rows: **XC** is KeePassXC, **K2A** Keepass2Android, **DX** KeePassDX, **KPD**
KeePassDroid.

### 3.1 Database custom data: keys and values

| program, release | S | M-newer | M-older | basis, where |
| --- | --- | --- | --- | --- |
| KeePass 2.61.1 | (probe saves only) | visits the source's keys only; never removes a target key; never reads a value; a key both hold decided by the two times, the method and `SettingsChanged` | same rule | measured (recorded), `rules.txt`, `matrix.txt`, `layout.txt`; RESULTS.md 27-69; read, `KeePassLib/PwDatabase.cs:1541-1632` |
| KeePass 2.62 | not run | not run | not run | the plugin's `CustomDataMergeRuleTests` pins the 2.61.1 rule against the pinned KeePass (read, CA.md 161-167 at a42e38aa) |
| KeePass 2.47 | all 11 keys and values kept, nothing added | not run | not run | measured (recorded), `keepassxc-save.txt` 405-460; RESULTS.md 264, 296-297 |
| XC 2.4.1 | all kept, nothing added | no database custom data merged at all | same | measured (recorded), 24 runs; RESULTS.md 269, 328, 347 |
| XC 2.4.2, 2.6.6 | all kept; adds `_LAST_MODIFIED` | slow KDF: the copy loaded later (C) **replaces the whole dictionary**: revocation, retirement, new authority removed, record and profile list back to C's. Fast KDF (both loads in one second): nothing changes | slow: P's dictionary replaces C's; fast: nothing | measured (recorded), RESULTS.md 270, 326-346; read, `src/core/Merger.cpp:724-761` |
| XC 2.7.0 | as 2.4.2; with a previous parent in the file the result is 4.1 | as 2.4.2 | as 2.4.2 | measured (recorded), RESULTS.md 271, 331-332 |
| XC 2.7.12, 2.8.0 | as 2.7.0; adds `KPXC_RANDOM_SLUG`; a KDF change adds `KPXC_autosaveDelayMin` (and `KPXC_REMOTE_SYNC_SETTINGS` in 2.8.0) | as 2.4.2 | as 2.4.2 | measured (recorded), RESULTS.md 272, 293-294, 310-311 |
| K2A 1.08d-r5, 1.15-r6 | all kept, nothing added | keys only P holds kept; a key both hold keeps the open copy's value **unless the arriving copy's `SettingsChanged` is later** (PCs: record and profile list went back to C's) | keys only P holds added; shared keys keep C's (CP), or take P's with later settings (CPs) | measured (recorded), RESULTS.md 423-424, 483-499; read, `src/KeePassLib2Android/PwDatabase.cs:1476`, `:1530-1534` |
| DX 4.5.8 | all kept | P's 11 keys at P's times (by per-key time) | same | measured (recorded), RESULTS.md 523-530; read, `DatabaseKDBXMerger.kt:415-432` |
| DX 3.2.0 | all kept | P's keys at P's times (ext merge then Save data) | same | measured (recorded), RESULTS.md 526-530 |
| DX 3.3.0 | all kept | not run | not run | measured (recorded), RESULTS.md 396, 430 |
| KPD 2.7 | all kept | the program has no merge | | measured (recorded), RESULTS.md 431, 556-560 |

**No save in any program removed a database key** (measured, recorded: 35 + 27 files). **The only
removals of database keys were KeePassXC merges from 2.4.2** in a later second.

### 3.2 Per-key modification times (a KDBX 4.1 field)

| program, release | S | merges | basis, where |
| --- | --- | --- | --- |
| KeePass 2.61.1 | kept (4.1 chosen only when some key has a time) | decide the merge, section 3.1 | read, `KdbxFile.cs:351-397`; measured (recorded), `rules.txt` |
| KeePass 2.47 | **dropped** in every file (writes 4.0, or 3.1 with AES-KDF and no node custom data) | not run | measured (recorded), RESULTS.md 273, 275 |
| XC 2.4.1 to 2.6.6 | **dropped**, always 4.0 | result 4.0, no times | measured (recorded), RESULTS.md 269-270, 363 |
| XC 2.7.0 to 2.8.0 | **dropped** in `P-plain` (4.0); **kept** in `P-moved`, whose previous parent is a second 4.1 feature | results 4.0, or 4.1 in the `m` sets; keys a replacing merge brings in carry the merge's time, the stale values then the newest | measured (recorded), RESULTS.md 271-272, 277-285, 350-355, 363-364 |
| K2A both | **dropped**, lost at the load | results 4.0, no times | measured (recorded), RESULTS.md 428, 433-445, 510 |
| DX 3.2.0, 3.3.0, 4.5.8 | kept, 4.1 | kept, P's times | measured (recorded), RESULTS.md 429-430, 529-530 |
| KPD 2.7 | kept, 4.1 | no merge | measured (recorded), RESULTS.md 431 |

### 3.3 Group custom data (the rule on `G`)

| program, release | S | M-newer | M-older | basis, where |
| --- | --- | --- | --- | --- |
| KeePass 2.61.1, 2.62 | not run in the spike | not run in the spike | not run in the spike | the plugin's host tests: taken whole from the group modified last (read, section 4.8) |
| KeePass 2.47 | kept | not run | not run | measured (recorded), `keepassxc-save.txt` 411 |
| XC 2.4.1 | kept | G keeps the target's rule (P's), takes the newer time (P's) | **G keeps C's wider rule under P's newer time** | measured (recorded), all 216 merge runs, RESULTS.md 356-358 |
| XC 2.4.2 to 2.8.0 | kept; `_LAST_MODIFIED` added, G's time unchanged | as 2.4.1 | as 2.4.1 | measured (recorded), RESULTS.md 287-292, 356-358 |
| K2A both | kept | P's rule, P's time | P's rule, P's time | measured (recorded), 18 runs, RESULTS.md 485-488, 511-512 |
| DX 4.5.8 | kept | **C's older rule under P's newer time** (from, ext) | P's rule | measured (recorded), RESULTS.md 523-536 |
| DX 3.2.0 | **deleted**, G's time unchanged | none left (deleted) | none left | measured (recorded), RESULTS.md 450-454, 526-527 |
| DX 3.3.0 | **deleted**, G's time unchanged | not run | not run | measured (recorded), RESULTS.md 430, 450-454 |
| KPD 2.7 | **deleted**, G's time unchanged | no merge | | measured (recorded), RESULTS.md 456-461 |

### 3.4 Entry custom data (the rule on `E1`)

| program, release | S | M-newer | M-older | basis, where |
| --- | --- | --- | --- | --- |
| KeePass 2.47 | kept | not run | not run | measured (recorded) |
| XC 2.4.1 | kept | P's rule, P's time; history: C's version | P's rule, P's time; history: C's version | current: measured (recorded), RESULTS.md 359-360; history: measured (re-read) |
| XC 2.4.2 to 2.8.0 | kept; `_LAST_MODIFIED` added, E1's time unchanged | as 2.4.1, `_LAST_MODIFIED` also inside the history item | as 2.4.1 | measured (recorded) and (re-read) |
| K2A both | kept | P's rule, P's time; history: C's version | same | measured (recorded) and (re-read) |
| DX 4.5.8, 3.2.0 | kept | **C's rule under P's time**; history: C's time, C's rule | P's rule, P's time; history: **C's time with P's rule** | measured (re-read), all 18 runs; RESULTS.md 523-536 for the current rule |
| DX 3.3.0 | kept | not run | not run | measured (recorded) |
| KPD 2.7 | **deleted**, E1's time unchanged | no merge | | measured (recorded), RESULTS.md 456-461 |

### 3.5 Entries as wholes

| program, release | S | merges | basis, where |
| --- | --- | --- | --- |
| every program run | E1 and E2 kept, same identifiers, same parent, same modification time; title and password kept; the program's own entry added | the same identifiers in every result; one version current, section 1 | measured (re-read), every save and merge file |
| XC 2.4.1 to 2.8.0 | **adds empty `Notes`, `URL`, `UserName` strings to every existing entry** (and, in merges, to the history item), without moving its time | an entry only the arriving copy held (`xc-touch`, at the root) arrived, all six releases | measured (re-read); `xc-touch@Root` in every "saved, into P" line of `keepassxc-merge.txt` (measured, recorded) |
| XC merges into C | the replaced E1 moves after E2 in the group's order | | measured (recorded), `keepassxc-merge.txt` "entries: E2@G E1@G" |
| K2A, DX, KPD | existing entries' strings unchanged; their own new entry carries all five standard fields | K2A: an entry only the open copy held (`droid-merge`) kept in all 18 | measured (re-read); RESULTS.md 490-492 |
| KPD 2.7 | its own new entry carries **one history item, an empty entry about 15 s older** | | measured (re-read), all five files |
| KeePass 2.47 (KeePassLib API) | nothing added to existing entries | | measured (re-read) |

### 3.6 Entry history

| program, release | S | merges | basis, where |
| --- | --- | --- | --- |
| every program run | no history added to E1 or E2 (both 0 in every save) | one item added to E1 in every merge, section 1 | measured (recorded) counts; measured (re-read) content |
| history limits | `HistoryMaxItems` 10, `HistoryMaxSize` 6291456, `MaintenanceHistoryDays` 365 in every file, saved or merged | same | measured (re-read); these equal KeePassLib's defaults, which the bases were made with, so a program that drops them is not told apart (assumed limitation) |
| pruning | never exercised: no entry ever had more than one history item | | measured (re-read) |

### 3.7 Modification times of nodes

| program, release | S | merges | basis, where |
| --- | --- | --- | --- |
| every program | E1, E2, G and H keep their times, whatever else the save did to them (custom data added by XC, deleted by KPD and DX 3.x, empty fields added by XC) | | measured (recorded) and (re-read) |
| XC all, DX all three | **the root group takes the time of the save** | | measured (recorded), `keepassxc-save.txt` "group Root, modified save"; RESULTS.md 447-448 |
| KeePass 2.47, K2A, KPD | the root group's time unchanged | | measured (recorded), RESULTS.md 448 |
| XC, K2A, DX merges | | G and E1 come out with the newer (P's) time in both directions | measured (recorded), RESULTS.md 356-360, 511-512, 523-527 |

### 3.8 `SettingsChanged`

| program, release | S | merges | basis, where |
| --- | --- | --- | --- |
| KeePass 2.61.1 | | decides a key neither side has a time for | measured (recorded), `rules.txt` |
| KeePass 2.47, XC all, DX all | the value given kept | XC: both copies were given the same value, so a merge shows nothing | measured (re-read) |
| K2A 4.0 files | kept | **takes the arriving copy's value when it is later** | measured (recorded), RESULTS.md 463-466, 506-507 |
| K2A 3.1 files, both releases | **none written**: reads back as the moment of reading | | measured (recorded) for 1.08d-r5, RESULTS.md 465; **measured (re-read) for 1.15-r6**, which RESULTS.md 466 left unread |
| KPD 2.7 | **none written**: reads back as the moment of reading, later than any save | no merge | measured (recorded), RESULTS.md 463-467; read, `PwDbV4Output.java` has no such element |

### 3.9 Previous parent (a KDBX 4.1 field)

| program, release | S | merges | basis, where |
| --- | --- | --- | --- |
| KeePass 2.47 | dropped | not run | measured (recorded), RESULTS.md 273 |
| XC 2.4.1 to 2.6.6 | dropped | | measured (recorded), RESULTS.md 269-270, 284-285 |
| XC 2.7.0 to 2.8.0 | kept, and keeps the file at 4.1 with every per-key time | kept in the `m` sets | measured (recorded), RESULTS.md 271-272, `keepassxc-merge.txt` "E2@H(from G)" |
| K2A both | dropped | | measured (recorded), RESULTS.md 428, 441-445 |
| DX all three, KPD | kept | | measured (recorded), RESULTS.md 429-431 |

### 3.10 Format version written

| program, release | S | merges | basis |
| --- | --- | --- | --- |
| KeePass 2.47 | 4.0; 3.1 with AES-KDF and no node custom data | not run | measured (recorded), RESULTS.md 273 |
| XC 2.4.1 to 2.6.6 | 4.0 | 4.0 | measured (recorded) |
| XC 2.7.0 to 2.8.0 | 4.0, unless the tree holds a tag, a previous parent, an entry excluded from reports or a custom icon with a name or time: then 4.1 | same | measured (recorded); read, `KeePass2Writer.cpp:52-108`, RESULTS.md 277-285 |
| K2A both | 4.0; 3.1 with AES-KDF and no node custom data | 4.0 | measured (recorded), RESULTS.md 428, 510 |
| DX all, KPD | 4.1 | 4.1 | measured (recorded) |

## 4. Everything the runs show about features other than custom data

These are the candidates now, so each observation is collected here even where the matrix has it.

### 4.1 Entries survive every save

No save by any of the thirteen program releases run (six KeePassXC, KeePass 2.47, two
Keepass2Android, three KeePassDX, KeePassDroid) dropped an entry, changed an entry's identifier,
parent or modification time, or removed a string (measured, re-read: the title and the protected
17-character password of E1 and E2 are in every file). KeePassXC adds three empty standard fields
to entries that lack them, and to their history items (measured, re-read). Nothing run had a
custom string field, so whether every program keeps one, its protected flag included, is **not
measured** (it is the most direct question the timestamp-named-field idea raises).

### 4.2 One version of an entry wins a merge

Every merge that met two versions of E1 left one version current and put one item in its history,
in every program that has a merge (section 1). For KeePassXC and Keepass2Android that item is the
losing version, faithful in time, custom data and strings (measured, re-read). For KeePassDX it is
not: the history item has the losing version's time and the merged custom data (measured,
re-read). Why, read from the 4.5.8 source: `mergeCustomData(entry.customData,
entryToMerge.customData)` runs on the open entry first (:507), and only then is the open entry
copied into history (:511, or :518 the other way).

### 4.3 Which version wins is the newer modification time

The newer modification time decided the current version in every merge measured: P's time, in
either direction, in all three merge implementations (measured, recorded and re-read). Read from
source, all three compare to the second: KeePassXC ignores milliseconds (`Merger.cpp` :447-449),
Keepass2Android compares with `TimeUtil.CompareLastMod(..., true)` (`PwDatabase.cs` :806, :811).

### 4.4 History: what is kept and for how long

- Content: section 4.2.
- Count: one item per merge here, and never more than one in any file (measured, re-read).
- Limits: KeePassXC truncates an entry's history after merging it (`Merger.cpp` :598, the limit
  read at :452 from `historyMaxItems`); Keepass2Android calls `MaintainBackups()` at the end of
  every merge (`PwDatabase.cs` :866, :1601-1614), which trims each entry to the database's
  `HistoryMaxItems` and `HistoryMaxSize`, defaults 10 and 6 MiB (`:49-50`) (all read). What
  KeePassDX trims was not read. KeePass's Database Maintenance can delete history older than a
  number of days (assumed here; the KeePass report reads it).
- So a record that lives only in a history item is gone after about ten later edits of the same
  entry in any of these programs, or sooner under the size limit (assumed from the reads above;
  never run).
- Settings survive as far as can be told, but only defaults were ever written (section 3.6).

### 4.5 Equal modification times, and rewritten times

Never run. Read from source, an equal time with different content loses one side completely, not
even into history: KeePassXC keeps the target, warns "conflict resolution may lose data" and adds
nothing (`Merger.cpp` :466-477, :540-566); Keepass2Android makes no backup of either side and
assigns nothing (`PwDatabase.cs` :804-821); KeePassDX only relocates the entry
(`DatabaseKDBXMerger.kt` :519-524). History items with the same time are one item to all three
(`Merger.cpp` :508-538; `PwDatabase.cs` :1570-1590; `DatabaseKDBXMerger.kt` :532-551). No program
measured moved an entry's or a non-root group's time on a save (section 3.7), so the measured way
to get a stale value under a new time is a merge: KeePassDX puts the arriving rule under the newer
time (measured), and KeePassXC keeps the target group's rule under the newer time (measured).

### 4.6 Entries only one copy holds

| program | entry only the arriving copy held | entry only the open copy held | basis |
| --- | --- | --- | --- |
| XC, six releases | arrived (`xc-touch`, at the root) | kept | measured (recorded and re-read) |
| K2A both | never run | kept (`droid-merge`), 18 runs | measured (recorded and re-read) |
| DX 3.2.0, 4.5.8 | never run | never run (ext and from add no entry) | |

Read from source, all three add an entry the target lacks: KeePassXC clones it with its history
into the matching group (`Merger.cpp` :190-199); Keepass2Android adds it to the group with the same
identifier, or the root when that group is missing (`PwDatabase.cs` :776-794); KeePassDX adds it
unless the target holds a deletion record newer than the entry (`DatabaseKDBXMerger.kt` :498-504).

### 4.7 Removals

- Database keys: removed only by KeePassXC merges from 2.4.2 (section 3.1).
- Node custom data: removed by KeePassDroid saves (groups and entries) and KeePassDX 3.2.0 and 3.3.0
  saves (groups), times unchanged (section 3.3).
- Nodes and strings: **nothing was ever deleted by anyone, and every file has zero deleted
  objects** (measured, recorded and re-read). How deletions travel, whether a deleted entry comes
  back from a stale copy, and what each program does with the recycle bin, were never measured.
  KeePassXC applies deletions only in its Synchronize mode (`Merger.cpp` :607-613, read).

### 4.8 KeePass itself

The spike's KeePass runs touched only `PwDatabase.CustomData` (`Program.cs`, `Layout.cs`; read).
The plugin's own host tests drive KeePass's merge of groups and entries against the pinned KeePass:
`MergeInRealDatabaseTests.The_same_two_records_on_the_root_group_do_not_both_survive` (a group's
custom data taken whole, `tests/KeePassGate.Host.Tests/MergeInRealDatabaseTests.cs:19-24`, `:267`)
and `RemovalMergeTests.An_edit_made_on_another_copy_stays_what_the_entry_holds_after_it_is_binned_here`
(the newer entry kept, the other edit into its history, `RemovalMergeTests.cs:115-133`), read at
a42e38aa. That those pass on 2.62 is assumed from master being green after the 2.62 pin; I ran no
test tier. Keepass2Android's merge is a port of KeePassLib reporting version 2.35 (RESULTS.md
501-502), so its behavior is a hint for KeePass's, not a measurement of it (assumed).

## 5. The shared question in full, for this subject

**Same entry, a different custom string field added in each copy.** Not measured anywhere (section
1). What would happen, from the reads: KeePassXC and Keepass2Android make the newer version current
whole and put the older version, with its own field, into history; KeePassDX copies the newer
entry's strings over the open one with `updateWith` (`:512`), and copies a whole entry into history
(`:546-550`), so the older side's strings would be in history there too, but its custom data would
not (read, not run). In no program does the older side's field stay in the current entry. A design
in which two replicas write one entry must therefore either read history to reconcile, or never
let two replicas write the same entry (one entry per record, the timestamp in the entry rather than
in a field) (assumed, from the reads).

**What history holds, and for how long.** Section 4.4: one faithful item per merge in KeePassXC and
Keepass2Android (measured), an unfaithful one in KeePassDX for custom data (measured), trimmed to
about ten items and 6 MiB at every merge (read), never observed past one item.

**Equal times, dropped or rewritten times.** Section 4.5: never run; read, one side is lost
entirely at an equal time, and same-second history items collapse. Per-key times dropped by saves
do not touch node times (measured); node times stayed as written in every save measured.

**Two different entries added to one group, one in each copy.** Never run (section 4.6). What was
run: one copy's extra entry arrived or stayed in every KeePassXC and Keepass2Android merge where it
existed, at the root (measured). All three merges add an entry the target lacks (read).

## 6. How the runs were made, so that a new candidate can be measured the same way

### 6.1 KeePass merges, in process

`spikes/revocation/Probe.csproj` builds `Probe.exe` for net48 against the KeePass named in
`keepass.version` (2.61.1 on the branch), from `.keepass\<version>\`, which `tools/KeePass.ps1`
provides and checks against `keepass.sha256` and KeePass's Authenticode signature (README.md 22-42).
`Probe.exe <work> rules|layout`, or no question for the matrix. A time is written or removed
through `StringDictionaryEx`'s internal `Set(string, string, DateTime?)` by reflection, which is
how a save by a program that drops times is simulated (`Program.cs` 21-25, 130-133). Every step
saves and reopens the file (`RoundTrip`, `Program.cs` 46-52). **To measure an entry candidate this
way**, extend `Paired.cs` or write a sibling probe; it runs from a console and shows no window.
A KeePass 2.62 run uses `-p:KeePassDevDir=` pointing at a 2.62 KeePass (Probe.csproj 13).

### 6.2 KeePassXC and KeePass 2.47

- **Provided** by `tools/KeePassXC.ps1`: `Install-KeePassXC -Version <v> [-Archive <zip>]` puts a
  release into `.keepassxc\<version>\` under the checkout, after checking the archive against
  `keepassxc.sha256`, the version of `KeePassXC.exe`, and the Authenticode signature of both
  `KeePassXC.exe` and `keepassxc-cli.exe` (signer "DroidMonkey Apps, LLC"). Archive names change
  between releases (`KeePassXC-2.8.0-x64.zip`, no `-Win64`), which is why the name is in the pin.
  KeePass 2.47 the same way, `Install-KeePass -Version 2.47`.
- **Bases**: `Probe.exe <work>\base paired <set> <kdf...>` writes `C-<set>`, `P-plain-<set>` and
  `P-moved-<set>` (`Paired.cs` 20-26, 163-209). Sets: `fast`/`fastm` Argon2d 2 iterations, 1 MiB,
  1 lane; `slow`/`slowm` Argon2d 200 iterations, 64 MiB, 2 lanes; `aes` 100000 rounds; `b` no rules;
  `m` E2 moved in both copies (`Measure-KeePassXC.ps1` 185-193). Password `probe-only`.
- **Runs** (`Measure-KeePassXC.ps1`, Windows PowerShell 5.1 only): saves with
  `keepassxc-cli add <copy> xc-added`; merges with `keepassxc-cli merge -s [--dry-run] <target>
  <source>` (the target is loaded first), the `saved` state first giving C a save by the same
  release (`add C xc-touch`). The password goes on standard input, with the console's input
  encoding set to UTF-8 without a byte order mark, which KeePassLib otherwise reads as part of the
  password (`:96-103`). Each program is started just after a second begins, and the window of UTC
  time it ran in is passed to the reader, which prints a time inside a window as that window's
  label (`:133-156`; `Paired.cs` 222-232). KeePass 2.47 saves through `Save-WithKeePass.ps1`, which
  loads `KeePass.exe` 2.47 as an assembly in a process of its own. About 50 minutes, nearly all
  slow-KDF merges (README.md 64-69).
- **The KDF change** (`Measure-KeePassXCKdf.ps1`) drives KeePassXC's window by UI automation. It
  takes the desktop and must not be run while the desktop is in use.

### 6.3 The Android programs

- **Where**: an Android 11 emulator (API 30, `google_apis`, x86_64, device `kpg30` made from
  `pixel_5`, 4 cores, 3072 MB, hardware keyboard), headless (`-no-window -no-audio
  -gpu swiftshader_indirect`), under Linux with KVM in WSL 2, driven over `adb` (README.md 81-171;
  `android-programs.txt` 6-26 lists every tool version). The WSL distribution `Ubuntu-24.04` exists
  on this machine and is stopped (measured, `wsl -l -v`). KVM has to be loaded again after every WSL
  restart (`root-setup.sh`).
- **Scripts** in `spikes/revocation/android`: `root-setup.sh` (KVM, JDK 17, libraries),
  `sdk-setup.sh` (command-line tools held to `android.sha256`), `avd-create.sh`, `emu-start.sh`,
  `wait-boot.sh` (roots adb, screen on, animations off), `fetch.sh` and `verify-apks.sh` (each APK
  held to its hash and to its signer's certificate digest), `install.sh`, `run-A.sh` (a save) and
  `run-B.sh` (a merge, three runs a cell), `teardown.sh`. Each run clears the program's data
  (`pm clear`), pushes one base alone into `/sdcard/Download`, runs a flow file through `k.py`
  (which reads the screen through `uiautomator dump` and taps what a flow names; the file picker is
  walked by keyboard), and pulls back what the program left. The merge flows put the arriving copy
  over the open file (Keepass2Android: then add an entry and save, answering "Yes, merge";
  KeePassDX: wait 11 s and `touch` the file, since a change counts only when the file's time moves
  more than 10 s, then "Merge data"), or beside it ("Merge from ..."), `android-screens.txt`.
- **Windows side**: `kp.ps1` compiles `Kpg.cs` against a copy of `KeePass.exe` named `KeePass.dll`
  and runs `make` (bases), `settings` (a copy with a later `SettingsChanged`) or `dump`/`list`;
  `collect.ps1` reads a directory back. Password `dummy-password-for-a-throwaway-file`. Both sides
  share one work directory, `KPG_ANDROID_WORK` (`env.sh`), which must sit on a Windows drive WSL
  reaches.
- **Notation**: a re-run differs from the recorded Android output in every time a program wrote,
  every hash and `SettingsChanged`, by design; what is compared is each file's summary line
  (README.md 214-223).
- **Not now**: the emulator and the UI-automation script were excluded from this task, and every
  measurement of an Android program waits for the desktop to be free.

## 7. Where the cached downloads and results are

All under the spike's measurement session's scratch directory,
`<scratch>\`
(here `<s>`). I hashed every archive and APK today: **all thirteen match their pins** (measured).

| what | where | state |
| --- | --- | --- |
| KeePassXC archives, six releases | `<s>xc-measure\dl\xc-<version>\*.zip`, with each release's `.DIGEST` | match `keepassxc.sha256`; not extracted. `Install-KeePassXC -Archive` takes them |
| KeePass 2.47 | `<s>xc-measure\dl\kp-2.47\KeePass-2.47.zip` | matches `keepass.sha256` |
| APKs, six releases | `<s>droid-measure\dl\<name>\*.apk` | match `android.sha256` |
| KeePassXC source files read | `<s>xc-measure\src-2.7.12\` (nine files), `<s>xc-measure\dl\src\` (four) | single files, not a clone |
| Android source files read | `<s>droid-measure\src\<program>\`, with `*-tree.json` | single files per program |
| Android results, the recorded run | `<s>droid-measure\work\{A,B,base,base2}` | 47 recorded hashes match; two runs not counted are in directories named `.failed-overflow` and `.aborted-drawer` |
| KeePassXC results, an earlier run | `<s>xc-measure\work\{A,A247,B,base,C}` | 627 files; not the recorded run |
| SDK, emulator image, device | inside WSL, `~/android-sdk` and `~/.android/avd/kpg30.avd` by `env.sh`'s defaults | assumed; not looked at, WSL was left stopped |
| My re-read | `spikes\storage\probes\ground-measured\` : `scripts\reread.ps1`, `out\droid-reread.txt`, `out\xc-reread-fast.txt`, `out\xc-reread-slow.txt`, copies under `data\` | |

## 8. What was never run, and what to run next

**Never run, and relevant to the candidates** (from RESULTS.md 375-385 and 578-594, and from the
shape of the bases):

- Any KeePass entry or group merge in the spike; anything on KeePass 2.62; any KeePass save made
  through its window.
- A custom string field, protected or not, a long value (a JSON document of kilobytes), an
  attachment, a tag, an entry in a dedicated group, a group or entry with more than one history
  item, history at its count or size limit, Database Maintenance deleting history.
- Two copies changing different fields of one entry; two copies each adding an entry to one
  group; equal modification times; a copy whose node times another program rewrote.
- Any deletion, any recycle bin, any deletion record merged.
- A merge of a file another program had saved or merged first, in any program, KeePass included
  (RESULTS.md 385, 593).
- Keepass2Android or KeePassDX merging an entry only the arriving copy held; KeePassDX 3.3.0 in a
  merge; every KeePassDX release between 3.3.0 and 4.5.8; Keepass2Android's remote sync through
  its cache and its merge-without-asking setting; KeePassDroid's database settings page.
- KeePassXC's merge from its window, its reload of a changed file, KeeShare and 2.8.0's remote
  sync; KeePassXC 2.5.x and 2.7.1 to 2.7.11.
- Every other program that opens KDBX: none was run.
- Android other than 11 on x86_64, and any real device.

**What to run next, the same way** (assumed to be the smallest additions): extend the bases with,
on E1, a custom string field whose name carries a timestamp and differs between C and P, one of
them protected; a "meta" entry holding a JSON document in a string field, edited differently in C
and P; one new entry added to G in each copy; a pair at equal modification times; a deleted entry
in one copy; and an entry with eleven history items. Read every string and every history item
back, as `reread.ps1` does. The KeePassLib and `keepassxc-cli` halves run from a console; the
Android half needs the emulator and waits for the desktop.

## 9. Corrections to what is already written

- **KeePassDX's history is not "the older version"**: its item carries the older time and the
  merged custom data (measured, re-read). RESULTS.md 511-512 states the history count only for
  Keepass2Android, which is right; nothing in RESULTS.md claims the KeePassDX history content, but
  a design reading history would be misled by assuming it.
- **"The newer entry whole, the older in its history"** is measured for KeePassXC and
  Keepass2Android only. For KeePass itself it rests on the plugin's host tests (read) and on
  Keepass2Android being a KeePassLib port (assumed).
- **The history limits that read back unchanged prove nothing** about whether a program keeps them,
  since every base used KeePassLib's defaults (section 3.6).
- **Keepass2Android 1.15-r6's KDBX 3.1 file has no `SettingsChanged`** either (measured, re-read),
  which RESULTS.md 466 left open.
- **CA.md section 204 does not yet carry the Android results** (CA.md 304-322 at a42e38aa names
  KeePassXC and KeePass 2.47 only), which is the known documentation finding C080.
