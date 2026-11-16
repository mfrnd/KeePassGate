# KeePassXC against the candidate storage features

Ground report, key `keepassxc`, 2026-11-15. What KeePassXC does, when it saves or merges a file, to
each place KPG data could live: ordinary entries in a dedicated group (standard and custom strings,
protected strings, attachments, tags, notes, history), group notes and tags, entry custom data,
`DeletedObjects`, the recycle bin, the binary pool and XML no reader knows. And which of KeePassXC's
own functions reach into such entries without being asked.

Every statement carries its basis:

- **[M]** measured: I ran it here on this machine, and the output is in my scratch directory (paths
  in section 1), or it is the spike's recorded output, which is then named.
- **[R]** read: source at a named tag, file and line.
- **[A]** assumed: neither run nor read; reasoning only.

## Contents

1. How this was measured
2. The short answers
3. The merge, read from source
4. The merge, measured
5. The question every ground report answers: one entry changed in both copies, two entries added
6. A plain save and the command line edits, measured
7. Each candidate feature in one table
8. KeePassXC functions that reach into entries without being asked
9. What a later KeePass synchronize makes of a KeePassXC result (measured in passing)
10. Not run, and what is still to measure

## 1. How this was measured

**Programs.** KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, each the Windows archive its release
publishes, downloaded into its own new directory and verified against the SHA-256 pinned in the
spike's `keepassxc.sha256`; `KeePassXC.exe` reported its own version and both `KeePassXC.exe` and
`keepassxc-cli.exe` carried a valid Authenticode signature from DroidMonkey Apps, LLC
(`logs\download.log`) [M]. **Only `keepassxc-cli.exe` was ever started**, with no window, the
password on standard input, a working directory of my own and nothing else running. The machine's
locale is de-DE, so the command line answered in German.

**Source.** Shallow clones at `2.7.12` (commit `860e51d8`) and `2.8.0` (`1c2f23ef`), and, to explain
measured differences, `2.4.2` (`a775031f`), `2.6.6` (`9c108b95`) and `2.7.0` (`d7a9ef49`, `src/core`
only). Every [R] below names the tag. `Merger.cpp` is identical between 2.7.12 and 2.8.0 but for one
line inside `mergeDeletions`, so its line numbers hold for both [R].

**Databases.** All invented, made by a console probe over KeePassLib 2.62 loaded from
`.keepass\2.62\KeePass.exe` (no KeePass window), plus `pykeepass` through
`uv run --no-project --with pykeepass python -I -X utf8` for what KeePassLib cannot write or would
hide: unknown XML, raw history counts and raw tag text.

- **Set 1** (`work\base-fast`, and `work\base-slow` with a slow KDF): a base file, KDBX 4.0, Argon2d;
  `Root/General` with three ordinary entries; `Root/KPG`, the dedicated group, with 21 entries and
  five subgroups. The database's history limits are 10 items and **1 MiB** (lowered from KeePass's
  6 MiB so that the size limit could be reached with small files). Copy **A** changes things at
  T0+1h, copy **B** at T0+2h, T0 being 2026-01-01 00:00 UTC; every change is made the way KeePass
  makes one, the current state put into history first. A is KDBX 4.1 (it holds group tags and
  previous parents), B is KDBX 4.0.
- **Set 2** (`work\base-extra`): group flags, group custom data, an exclusion from reports, and two
  "meta" entries given one custom string field per change, named after the time of the change, the
  shape the timestamp-named-field proposal would take. Limits 10 items and 6 MiB.
- **Fast** is Argon2d 2 iterations, 1 MiB, 1 lane; **slow** is 200 iterations, 64 MiB, 2 lanes,
  under which one merge took 7.3 to 10.7 seconds (`work\merge-slow`), so the two loads always fell
  in different seconds.

**Reading back.** A dump through KeePassLib 2.62 (`Xcp.exe dump`), and `pykeepass` on the raw XML
wherever history counts matter. **This matters**: KeePassLib 2.62's `PwDatabase.Open` itself trims
each entry's history to the file's limits. A file `pykeepass` read as holding 10 history items, with
the limit set to 3, came back from KeePassLib with 3 [M] (`work\histmax`). Every history count in
this report is therefore the raw count `pykeepass` read, never KeePassLib's.

**Where everything is.** Scripts: `spikes\storage\probes\ground-keepassxc\scripts`
(`probe\Program.cs`, `Run-Merge.ps1`, `Run-Save.ps1`, `unknown_xml.py`, `count_hist.py`,
`tags_raw.py`, `set_history_max.py`). Results: the `work` directory beside it: `merge-fast`,
`merge-slow`, `merge-extra` (one directory per release and direction, holding `result.txt`, the
command's own report in `merge.txt` and, from 2.6.6, the dry run in `dry.txt`), `save-fast`,
`unknown`, `tags`, `histmax`, `roundtrip`, `export`, `kpsync`.

## 2. The short answers

1. **No KeePassXC merge on the command line or in the window ever applies a deletion.** The merge
   applies tombstones only in the `Synchronize` mode, and the mode every group falls back to is
   `KeepNewer`; nothing outside KeePassXC's tests ever sets another, at any of the five tags read
   [R]. Measured: entries A deleted (with tombstones) came back into A from B, a deleted group too,
   and A kept its tombstones beside the resurrected entries; B never received a tombstone [M, all
   four releases, both KDFs]. Only a KeeShare import forces `Synchronize` [R].
2. **An entry or group added in one copy always arrives, and a merge never removes anything from
   the target** [R, M]. Two different entries added to the same group, one in each copy, were both
   in both results, in all four releases, both directions, fast and slow KDF [M].
3. **An entry is merged whole, never field by field.** The copy whose entry has the later
   modification time (compared to the second) wins entirely: its strings, attachments, tags, notes
   and custom data. The loser's version is put into the winner's history at its own modification
   time [R, M].
4. **On a tie, the target wins and the source's version is lost without trace**: neither in the
   entry nor in its history, and nothing printed [M]. Three merges back and forth left the two
   copies holding different versions for good [M].
5. **The loser's version is not kept if the winner already has as many newer history items as the
   history limit allows** (10 by default). The merge then sees no change in the newest items and
   throws the merged history away [R]. Measured with the proposal's shape: one copy added a
   timestamped field once, the other eleven times; the first copy's field was in neither result, in
   any release [M].
6. **2.7.12 and 2.8.0 do not truncate history when the source's entry wins**, so a merge can leave
   more history than the file's limits allow (11 items under a limit of 10, 1.2 MiB under 1 MiB);
   2.4.2 and 2.6.6 truncate [M]. KeePassLib trims that excess when KeePass opens the file [M].
7. **A plain KeePassXC save keeps every string, protected flag, attachment, note, entry tag, entry
   custom data item, history item and tombstone**, and never prunes history [M]. It **drops all
   unknown XML** at every position tried [M], adds empty standard fields to every entry, flags
   every attachment protected, adds `_LAST_MODIFIED` to every custom data dictionary that has items
   [M]. **2.4.2 and 2.6.6 also drop group tags, previous parents and the exclusion from reports** and
   write KDBX 4.0 [M].
8. **Of a group, a merge carries only name, notes, icon, expiry and modification time, and only
   when the source group is newer** [R]. Group tags, group custom data and the searching and
   auto-type flags are never merged, yet the target group takes the newer modification time [M],
   which later makes the stale values look current to KeePass [M, section 9].
9. **A move survives only while no newer content arrives from the other copy**: when it does, the
   entry keeps its place but takes the other copy's location time, and a later merge into the other
   copy no longer moves it there; the copies then disagree for good [M].
10. **The recycle bin is not merged.** A bin the other copy created arrives as an ordinary group
    named "Recycle Bin", with the target's own bin setting unchanged [M].
11. **Unasked readers**: CSV export carries the five standard fields, including entries in the
    recycle bin; HTML export every custom string, protected ones in clear; XML export everything
    [M]. Reports and the online HIBP check read only the `Password` field and skip empty ones [R].
    Auto-type matches an entry's title against window titles by default [R]. An associated
    browser extension can ask for any entry by UUID or by path [R].

## 3. The merge, read from source

**Who calls it.** Every merge path uses the same `Merger` class [R]:

- `keepassxc-cli merge [-s] <target> <source>`: `Merger merger(db2, database)` and a save of the
  target when the change list is not empty (`src/cli/Merge.cpp:89-101`, 2.7.12; `:88`, 2.8.0).
- Database, then Merge in the window: a dry run for the change list, then the merge
  (`src/gui/MergeDialog.cpp:159` and `:185`, 2.8.0).
- The file changing on disk while the open copy has unsaved changes: the user is offered Merge, and
  the copy in memory is merged into the copy just loaded (`src/gui/DatabaseWidget.cpp:2256-2280`
  and `:2214`, 2.8.0).
- Import into a new database: custom data of the database skipped
  (`src/gui/DatabaseTabWidget.cpp:304-305`, 2.8.0).
- KeeShare import: `setForcedMergeMode(Group::Synchronize)` and the database's custom data skipped
  (`src/keeshare/ShareImport.cpp:95-97`, 2.7.12 and 2.8.0).

**The modes.** `Default`, `KeepNewer` and `Synchronize` (`src/core/Group.h:39-44`, 2.7.12);
2.4.2 and 2.6.6 also have `Duplicate`, `KeepLocal` and `KeepRemote` (`src/core/Merger.cpp:381-399`,
2.4.2). A group's mode is its own, else its parent's, else **`KeepNewer`**
(`src/core/Group.cpp:207-216`, 2.7.12; `:201-208`, 2.8.0; `:250`, 2.4.2; `:232`, 2.6.6), although
the comment in `Group.h:41` says the fallback is `Synchronize`. `setMergeMode` has no caller outside
`tests/` at any of the five tags, and the file format holds no merge mode [R]. So every merge but a
KeeShare import runs `KeepNewer`.

**The order** is groups and entries, then deletions, then metadata (`Merger.cpp:168-184`, 2.7.12).

**Entries** (`Merger.cpp:186-262`):

- Found by UUID anywhere in the target (`:192`). Absent: cloned with its history into the target's
  group of the same UUID (`:194-199`). Present: moved if the source's location time is later and
  the parents differ (`:202-210`), with time updates switched off (`moveEntry`, `:331-362`), so the
  target's own location time does not change; then the conflict is resolved (`:211`).
- `resolveEntryConflict_MergeHistories` (`:439-479`) compares the two modification times ignoring
  milliseconds (`:447-449`). Source later: the source entry is cloned with its history, the target's
  history merged into the clone, the target erased and the clone put in the target's group
  (`:453-465`). Otherwise: the source's history is merged into the target (`:466-477`). The mode is
  not used (`:499`). **There is no merge of individual fields anywhere.**
- `mergeHistory` (`:494-605`): both histories go into one map keyed by modification time, to the
  second (`:508-538`), the target's item kept on a collision unless the source entry is the newer
  (`:531-534`). The older of the two current versions is added as a history item when their times
  differ (`:550-566`). **When the times are equal and the content differs, a warning is logged and
  neither version is added** (`:542-548`). Then only the newest `maxItems` items of the old and the
  new history are compared (`:568-581`), and if they are equal the merged list is discarded
  (`:582-585`); otherwise it replaces the history and `truncateHistory` runs (`:593-598`).
- `truncateHistory` (`src/core/Entry.cpp:871-923`, 2.7.12; `:886-936`, 2.8.0) cuts from the oldest,
  first by count and then by size, the size of an item being its strings, auto-type associations,
  attachments, custom data and tags (`Entry.cpp:463-475`, 2.7.12). **It returns at once when the
  entry belongs to no database** (`:875-877`). In 2.7.12 and 2.8.0 the history is merged into the
  clone before the clone is put into the database (`Merger.cpp:463` before `:465`), so the branch
  where the source wins never truncates. In 2.4.2, 2.6.6 and 2.7.0 the clone was moved in first
  (`Merger.cpp:355-356`, 2.4.2; `:354-355`, 2.6.6; `:351-352`, 2.7.0) [R]. Which release in between
  changed the order is not bracketed.

**Groups.** Absent from the target: cloned without entries (`:226`), with all its own fields
(`Group.cpp:931-932`, 2.7.12, copies the group's data and custom data), and its entries follow by the
recursion. Present: moved if its location time is later (`:235-246`), and
`resolveGroupConflict` (`:264-329`) acts **only when the source group is strictly newer** (`:274`),
and then copies name, notes, icon, expiry and the modification time (`:289-325`). Tags, custom
data, the auto-type and searching flags and the default auto-type sequence are never copied.

**Deletions** (`:607-698`): only when the effective mode is `Synchronize` (`:610-614`). When that
runs, in a KeeShare import, the tombstones of both copies are joined, the earliest time kept
(`:640-642`), an entry deleted only if not modified after its tombstone (`:648-651`), a group only
if nothing undeleted is left in it (`:675-678`), and a tombstone whose entry survives is dropped from
the list (`:648-652`).

**Metadata** (`:700-765`): custom icons are added; the database's custom data as the spike
measured (`:724-762`); the comment at `:702-704` lists the recycle bin among what is not merged.

## 4. The merge, measured

`keepassxc-cli merge -s <target> <source>`. **Into A** is B merged into A (A loaded first, A
written); **into B** the reverse. Unless a cell names releases, all four releases gave the same
result, and the slow KDF gave exactly the fast KDF's result for every group and entry, in every
release and both directions (the only differing line was the recycle bin's UUID, which the two sets
were made with) [M]. Every run exited 0.

### Set 1

| case | A did (T0+1h) | B did (T0+2h) | into A | into B |
| --- | --- | --- | --- | --- |
| `added-A`, `added-B` | added an entry in `KPG` | added another in `KPG` | both entries | both entries |
| `in-newA` | added a group `KPG/NewA` and an entry in it | nothing | kept | group and entry arrive |
| `one-A` | changed custom string `Data` | nothing | A's | A's, B's version in history |
| `prot-A` | changed a protected custom string | nothing | A's | A's, still protected |
| `att-A` | changed an attachment's content | nothing | A's | A's, old attachment in history |
| `notes-A` | changed Notes | nothing | A's | A's |
| `tag-A` | added a tag | nothing | A's | A's |
| `both-diff` | added field `FieldA` | added field `FieldB` | **B's whole: `FieldB` only**; `FieldA` only in the history item at +1h | the same |
| `both-samefield` | set `Shared=A` | set `Shared=B` | B's; A's in history | B's; A's in history |
| `att-both` | added `a.bin` | added `b.bin` | B's (`base.bin`, `b.bin`); `a.bin` only in history | the same |
| `tag-both` | added `tagA` | added `tagB` | B's (`t0`, `tagB`); `tagA` only in history | the same |
| `cd-both` (entry custom data) | added item `cdA` | added item `cdB` | B's (`cd0`, `cdB`); `cdA` only in history | the same |
| `both-tie` | added `FieldA` at +1h | added `FieldB` **also at +1h** | **A's; B's version nowhere** | **B's; A's version nowhere** |
| `rewound-B` | nothing | changed `Data` with its time set to **T0-1h**, before the base | the base version; B's change only in history | the same |
| `hist` (2 base history items) | changed `Data` | changed `Data` | B's, history of 4: -2h, -1h, T0, A's +1h | the same |
| `hist-many` (9 base items) | changed | changed | B's; history **10** in 2.4.2 and 2.6.6, **11** in 2.7.12 and 2.8.0 (limit 10) | B's; history 10, oldest dropped |
| `hist-big` (400 KiB attachment per version, limit 1 MiB) | new attachment | nothing | A's, 2 items | A's; **2 items** in 2.4.2 and 2.6.6, **3 items, 1.2 MiB** in 2.7.12 and 2.8.0 |
| `move-A` | moved `KPG` to `KPG/Sub` | nothing | stays | moved; B's location time stays T0; previous parent set by 2.7.12 and 2.8.0 only |
| `move-A-edit-B` | moved to `KPG/Sub` | changed `Data` | B's content in `Sub`, **with B's location time (T0) and no previous parent** | B's content, moved to `Sub` |
| `del-A-unch` | deleted, tombstone at +1h | nothing | **comes back**; A's tombstone stays beside it | stays; B gets no tombstone |
| `del-A-chB-after` | deleted | changed after the deletion | comes back | stays |
| `del-A-chB-before` | deleted | changed before the deletion | **comes back** | stays |
| `GDelA` (empty group) | deleted, tombstone | nothing | comes back | stays |
| `bin-A` | moved to a recycle bin A created, as KeePass does | nothing | stays in A's bin | a group "Recycle Bin" arrives as an **ordinary group** (B's bin setting still empty), searching and auto-type off, the entry in it |
| `GNotesBoth` | notes `nA` | notes `nB` | B's | B's |
| `GNotesA` | notes `nA` | nothing | A's | A's |
| `GTagsA` | group tag `gtA` | nothing | A's in 2.7.12 and 2.8.0; **none** in 2.4.2 and 2.6.6 | **none**, under A's modification time |

History counts are raw, read by `pykeepass` (`scripts\count_hist.py` over `merge-fast`) [M]. The
other cells are from the KeePassLib dump, normalized for what every KeePassXC save adds (section 6)
[M].

### Set 2

| case | A did | B did | into A | into B |
| --- | --- | --- | --- | --- |
| `meta` | added `kpg-20260101T010000` once, at +1h | added one `kpg-<time>` field per change, 11 changes from +2h to +12h, history kept at 10 | B's entry, B's 10 history items; **A's field nowhere** | the same |
| `meta2` | added `kpg-...T010000` at +1h and `kpg-...T030000` at +3h | added `kpg-...T020000` at +2h | A's entry (both A fields); B's version as the history item at +2h | the same |
| `GFlags` | turned searching and auto-type off | nothing | A's | **B's (inherit)**, under A's modification time |
| `GCustom` | set group custom data `KPG=rule-A` | nothing | A's, with `_LAST_MODIFIED` added | **B's (none)** |
| `excl` | turned off the quality check (exclusion from reports) | nothing | kept in 2.7.12 and 2.8.0; **lost** in 2.4.2 and 2.6.6 | the same |

`meta` was read raw: ten history items in all eight results, none holding A's field [M].

### Back and forth

`work\roundtrip`, 2.4.2 and 2.8.0: A ← B, then B ← A, then A ← B. The two copies still differed
afterwards [M]:

- `both-tie`: A holds `FieldA`, B holds `FieldB`, and neither has the other's anywhere.
- `move-A-edit-B`: A has it in `KPG/Sub`, B in `KPG`. B never learned of the move, because the
  version that came back to it carried B's own location time.
- Only A holds the tombstones; only A has a designated recycle bin.
- 2.8.0: only A has the group tag.
- 2.8.0 reported a history change to `hist-big` on the third merge, and wrote the file again,
  although that entry came out the same in both copies.

## 5. The question every ground report answers

**The same entry in both copies, each copy having added a different custom string field (or
changed a different field).** KeePassXC keeps **one copy's entry whole**, the one with the later
modification time to the second, and puts the other copy's version into the entry's history at
that version's modification time. Both fields are never on the current entry. Measured in all four
releases, in both directions and with both KDFs (`both-diff`, `att-both`, `tag-both`, `cd-both`,
`both-samefield`) [M]; read at `Merger.cpp:439-479` and `:494-605` (2.7.12) [R].

**What is then in the history.** Both histories joined, items with the same modification time
counted as one, plus the losing version as one more item, sorted by time [R]; measured on `hist`,
with four items after the merge, every version of both copies present [M]. So the union of the two
copies' fields can be recovered from the entry plus its history, **except in three cases**:

1. **A tie.** Equal modification times to the second and different content: the target's version
   stays, the source's is dropped, history included, and the command printed nothing [M]
   (`both-tie`, both directions); the source says it only logs a warning (`Merger.cpp:542-548`) [R].
   KDBX 4 stores times in whole seconds, so two programs changing the same entry within one second
   produce exactly this [A].
2. **The winner already has `maxItems` newer history items** than the losing version. The merge then
   sees no difference among the newest `maxItems` items and discards the joined history
   (`Merger.cpp:568-585`) [R]: the losing version never enters it. Measured with `meta`, eleven
   changes on one side against one on the other [M].
3. **Truncation afterwards** (below).

**How long it stays.** The limits are the database's own `HistoryMaxItems` and `HistoryMaxSize`;
a database KeePassXC creates gets 10 items and 6 MiB (`src/core/Metadata.cpp:29-30`, 2.7.12;
`:27-28`, 2.8.0) [R], the same values KeePass uses [A]. KeePassXC truncates one entry's history:

- **whenever that entry is edited**, by count and by size [M] (`keepassxc-cli edit` dropped the
  oldest of `hist-many`'s items and one of `hist-big`'s by size, raw counts) and [R]
  (`Entry::endUpdate`, `Entry.cpp:1104-1116`, 2.7.12);
- **in a merge**, when the joined history differs, always where the target's entry wins, and where
  the source's entry wins only up to 2.7.0 [M, R] (section 3);
- **for every entry when the history limits are changed** in Database Settings
  (`src/gui/dbsettings/DatabaseSettingsWidgetGeneral.cpp:169-174`, 2.8.0) [R];
- when the user deletes history items in the entry editor (`src/gui/entry/EditEntryWidget.cpp:1286`
  and `:1741`, 2.8.0) [R].

KeePassXC does **not** truncate on load or on a plain save: with the file's limit set to 3, all ten
items of `hist-many` were still in the file after a `keepassxc-cli add`, in all four releases [M].
KeePass does: KeePassLib 2.62 returned 3 of those 10 items on `PwDatabase.Open` [M], and its
`PwDatabase.Save` wrote 2 of the 4 history items the probe created under the 1 MiB limit [M]. That
is KeePass's behavior, recorded here because it bounds what history can carry through KeePassXC
too.

**When the two versions carry the same modification time**: case 1 above [M]. **When a program has
dropped or rewritten times**: KeePassXC compares only `LastModificationTime` (`Merger.cpp:447-449`)
[R], so a version whose time was rewritten earlier loses whole, whatever it holds. Measured with
`rewound-B`, whose real change carried a time an hour before the base's: it lost in both
directions and survived only as a history item at its rewritten time [M]. If such a time is older
than the newest `maxItems` items, case 2 applies and it is not kept at all [R]. KeePassXC itself
left every unchanged entry's times alone on save [M]; its own edits stamp the local clock [A], so a
machine whose clock is behind loses its edits to older ones made elsewhere [A].

**Two different entries added to the same group, one in each copy.** Both arrive, always: in all
four releases, both directions, both KDFs [M] (`added-A`, `added-B`), and the source never names an
entry the target lacks except to add it (`Merger.cpp:192-199`) [R]. A group added on one side
arrives with its entries (`in-newA`) [M]. **No merge removes an entry** the target holds [R, M].
What removes entries is the user deleting them, emptying the recycle bin, a KeeShare import in
`Synchronize` mode, and choosing Discard when the file changed on disk [R].

## 6. A plain save and the command line edits, measured

**A plain save** is `keepassxc-cli add <file> General/xc-added`, which touches nothing else,
on a copy of A, a KDBX 4.1 file holding every candidate feature (`work\save-fast\<release>\after-add.txt`
against `A-orig.txt`) [M].

| feature | 2.4.2 | 2.6.6 | 2.7.12 | 2.8.0 |
| --- | --- | --- | --- | --- |
| format written | 4.0 | 4.0 | 4.1 | 4.1 |
| standard and custom strings, values | kept | kept | kept | kept |
| protected custom string and its flag | kept | kept | kept | kept |
| empty standard fields added to every entry, `Password` flagged protected | yes | yes | yes | yes |
| attachments, bytes | kept | kept | kept | kept |
| every attachment flagged protected in the binary pool | yes | yes | yes | yes |
| entry notes | kept | kept | kept | kept |
| entry tags, raw text `zeta; Alpha;alpha, b:c,  spaced tag ;zeta` | verbatim | verbatim | `Alpha,alpha,b:c,spaced tag,zeta` | the same |
| entry history | kept, not truncated | kept | kept | kept |
| entry custom data | kept, `_LAST_MODIFIED` added to the entry and to each history item | the same | the same | the same |
| entry previous parent | **dropped** | **dropped** | kept | kept |
| exclusion from reports (`QualityCheck` false) | **dropped** | **dropped** | kept | kept |
| group notes | kept | kept | kept | kept |
| group tags, raw text | **dropped** | **dropped** | verbatim | verbatim |
| group custom data | kept, marker added | kept | kept | kept |
| group searching and auto-type flags | kept | kept | kept | kept |
| `DeletedObjects` | all kept | all kept | all kept | all kept |
| recycle bin setting and group | kept | kept | kept | kept |
| unknown XML at 8 positions | **all dropped** | **all dropped** | **all dropped** | **all dropped** |
| times of entries and groups not touched | unchanged | unchanged | unchanged | unchanged |

Notes on the table:

- The tag row and the unknown XML row are from `pykeepass` on the raw XML (`work\tags`,
  `work\unknown`) [M]. The eight positions were in `Meta`, in `Root`, in a group, in an entry, inside
  a `String`, inside a custom data `Item`, inside `Times`, and in a history entry. Read at 2.7.12:
  every reader routine ends in `skipCurrentElement` (`src/format/KdbxXmlReader.cpp:1208-1211`), and
  nothing keeps what it skipped [R].
- Entry tags from 2.7.12 are split on comma, semicolon and tab, trimmed, deduplicated, sorted and
  joined with a comma (`src/core/Entry.cpp:42` and `:717-727`, 2.7.12) [R].
- Group tags make the file 4.1 (`src/format/KeePass2Writer.cpp:72-74`, 2.7.12) [R].
- The binary flag is written as `0x01` for every attachment (`src/format/Kdbx4Writer.cpp:228`,
  2.7.12; `:224`, 2.8.0), and identical attachments are stored once (`:243-248`, 2.7.12) [R].
- Custom data item times are written only in a 4.1 file (`KdbxXmlWriter.cpp:287`, 2.7.12) [R]; the
  spike measured them dropped in 4.0 files (spike `RESULTS.md` section 5).
- A value containing a character XML 1.0 cannot carry has it removed on write
  (`KdbxXmlWriter.cpp:452`, 2.7.12) [R].
- The only time a plain save changed was the modification time of `General`, the group the new
  entry went into [M].

**Edits** (`after-all.txt` against `after-add.txt`) [M]:

| command | 2.4.2 | 2.6.6 | 2.7.12 | 2.8.0 |
| --- | --- | --- | --- | --- |
| `edit -u` on a `KPG` entry | pre-edit state into history, time now, history truncated | the same | the same | the same |
| the history item an edit makes | **no entry custom data** | **no entry custom data** | **no entry custom data** | with custom data |
| `rm` of an entry outside the bin | moved to the bin, location time now, modification time unchanged | the same | moved, modification time and previous parent set | the same |
| `rm` of an entry in the bin | deleted, tombstone at now | the same | the same | the same |
| `mv` to another group | not available | location time only | history item, modification time, location time, previous parent | the same |

The history item an edit makes is built without the custom data up to 2.7.12
(`Entry::beginUpdate`, `Entry.cpp:1089-1102`, 2.7.12, copies data, strings, attachments and
auto-type only); 2.8.0 copies everything (`Entry.cpp:1100-1110`) [R]. The command line moves an entry
inside `beginUpdate`/`endUpdate` from 2.7.12 (`src/cli/Move.cpp:62-64`, 2.8.0), and `rm` recycles
or deletes (`src/cli/Remove.cpp:50-53`, 2.8.0) [R].

## 7. Each candidate feature in one table

| feature | survives a KeePassXC save | what a KeePassXC merge does to it | other KeePassXC hazards |
| --- | --- | --- | --- |
| ordinary entry in a dedicated group, added once and never changed | yes [M] | always arrives, never removed [M] | user deletion is undone by the next merge into the deleting copy [M]; user edits whole-entry [M] |
| the same entry changed in both copies | yes | newest whole; the other in history, except on a tie, behind `maxItems` newer items, or after truncation [M] | |
| standard strings (Title, UserName, Password, URL, Notes) | yes [M] | whole entry [M] | read by reports, HIBP, auto-type, search, browser, CSV export (section 8) |
| custom strings | yes [M] | whole entry [M] | HTML and XML export in clear; reserved names (section 8) |
| protected custom strings | yes, flag kept [M] | whole entry, flag kept [M] | HTML export prints them in clear [M] |
| attachments | yes, flagged protected [M] | whole entry [M] | none found |
| entry tags | 2.4.2, 2.6.6 verbatim; 2.7.12, 2.8.0 normalized [M] | whole entry [M] | searched by default [R] |
| entry notes | yes [M] | whole entry [M] | CSV, HTML export; searched [M, R] |
| history | yes, never pruned on save [M] | joined, with the three losses of section 5 [M] | truncated on edit, merge, limit change [M, R] |
| group notes | yes [M] | newer group's, only if strictly newer [M] | HTML export [M] |
| group tags | dropped by 2.4.2, 2.6.6 [M] | never merged, time raised anyway [M] | |
| group custom data | yes, marker added [M] | never merged [M] (spike: likewise) | Properties page removes it [R] |
| group searching and auto-type flags | yes [M] | never merged after creation; travel with a new group [M] | |
| entry custom data | yes, marker added, item times only in 4.1 [M, R] | whole entry [M] | edit history lacks it up to 2.7.12 [M]; Properties page removes it [R] |
| `DeletedObjects` | yes [M] | **never applied, never copied** [M] | only the deleting copy keeps them |
| recycle bin | yes [M] | not merged; a foreign bin arrives as an ordinary group [M] | emptying deletes with tombstones [R] |
| binary pool | contents kept, deduplicated, all flagged protected [M, R] | follows the entries [M] | |
| unknown XML | **dropped everywhere** [M] | gone before any merge [A] | |
| database custom data | yes, times only in 4.1 (spike) | replaced whole by the later load (spike, 2.4.2 on) | |

## 8. KeePassXC functions that reach into entries without being asked

| function | what it reads or writes | basis |
| --- | --- | --- |
| Health check report | every entry not in the recycle bin with a non-empty `Password`; skips entries excluded from reports | [R] `src/gui/reports/ReportsWidgetHealthcheck.cpp:107-117`, 2.8.0 |
| HIBP report | the `Password` of every entry not recycled and not empty; sends the first five hex digits of its SHA-1 to `api.pwnedpasswords.com`, only when the user runs it | [R] `ReportsWidgetHibp.cpp:256-257`; `src/networking/HibpDownloader.cpp:97-100`, 2.8.0 |
| Passkeys report | entries with `KPEX_PASSKEY_PRIVATE_KEY_PEM` | [R] `ReportsWidgetPasskeys.cpp:79`, 2.8.0 |
| Browser statistics report | entries with browser settings; its menu deletes those settings from selected entries | [R] `ReportsWidgetBrowserStatistics.cpp:369-374`, 2.8.0 |
| Statistics report | counts; no write path seen | [R], not read in full |
| Browser integration, matching | entries outside the recycle bin whose `URL` or `KP2A_URL*` fields match, in groups not hidden by the group custom data `BrowserHideEntry`; **also `keepassxc://by-uuid/<uuid>` and `keepassxc://by-path/<path>`, so an associated extension can name any entry**; it then needs the user's permission | [R] `src/browser/BrowserService.cpp:1015-1067`, `:1369-1395`, 2.8.0 |
| Browser integration, what it returns | user name and password with placeholders resolved, and custom strings whose names start with `KPH: ` when that option is on | [R] `BrowserService.cpp:1190-1223`, 2.8.0 |
| Browser integration, writes | the entry custom data item `KeePassXC-Browser Settings` (allowed and denied hosts) when the user answers; new credentials into a group `KeePassXC-Browser Passwords`; a password update into an entry the user picks | [R] `src/browser/BrowserEntryConfig.cpp:29`, `:107`; `BrowserService.cpp:1264`, 2.8.0 |
| Auto-type | entries in groups whose auto-type is enabled (inherited by default), matching the window title against the entry's title and URL, both matches on by default | [R] `src/autotype/AutoType.cpp:476-479`; `src/core/Entry.cpp:355`, `:360`; `src/core/Config.cpp:80-81`, 2.8.0 |
| Search | by default title, user name, URL, tags and notes; custom strings only when named (`_name:`); groups with searching disabled are skipped; read only | [R] `src/core/EntrySearcher.cpp:74`, `:235-239`, `:318-323`, 2.8.0 |
| Reserved string names | `otp`, `TOTP Seed`, `TOTP Settings`, `TimeOtp-*` make an entry a TOTP entry; `KP2A_URL*` add browser URLs; `KPEX_PASSKEY_*` make a passkey; `_EXEC_CMD`; `KPH: ` prefix | [R] `src/core/Totp.h:70-78`; `src/core/EntryAttributes.cpp:42-59`, 2.8.0 |
| KeeShare | only groups that carry a KeeShare reference; imports in `Synchronize`, so deletions are applied there | [R] `ShareImport.cpp:95-97`, 2.8.0 |
| Recycle bin | delete moves to the bin when enabled, creating one when the database names none; emptying deletes the bin's direct children, each with a tombstone | [R] `src/core/Database.cpp:1031-1069`, 2.8.0; [M] for `rm` |
| History limits | section 5 | [M, R] |
| "Plugin data" removal | the Properties page of the entry editor and of the group editor lists every custom data item with a Remove button, behind a confirmation | [R] `src/gui/EditWidgetProperties.cpp:69-99`; `EditEntryWidget.cpp:180`; `src/gui/group/EditGroupWidget.cpp:181`, 2.8.0 |
| "Plugin data" in database settings | the Browser page removes `KPXC_BROWSER_*` keys from the database's custom data, and "remove stored permissions" removes only `KeePassXC-Browser Settings` from every entry | [R] `src/gui/dbsettings/DatabaseSettingsWidgetBrowser.cpp:107-108`, `:184`, `:218`; `BrowserService.cpp:1006-1010`, 2.8.0 |
| Attachment and attribute size | the window asks before adding an attachment over 5 MB; no limit on a string's length found | [R] `src/gui/entry/EntryAttachmentsWidget.cpp:526-539`, 2.8.0; length not measured |
| CSV export | Group, Title, Username, Password, URL, Notes, TOTP, Icon, Last Modified, Created; **recycled entries included**; no custom strings, attachments, custom data, history, tags or group notes | [M] `work\export`; [R] `src/format/CsvExporter.cpp:60-97`, 2.8.0 |
| HTML export | the standard fields, **every custom string including protected ones, in clear**, entry and group notes; not the recycle bin, attachments, custom data or history; not in the 2.6.6 command line | [M]; [R] `src/format/HtmlExporter.cpp:73-92`, `:220-221`, 2.8.0 |
| XML export | everything, unencrypted | [M] |

## 9. What a later KeePass synchronize makes of a KeePassXC result

Measured in passing, because the spike listed it as not run: KeePassLib 2.62's
`PwDatabase.MergeIn(source, PwMergeMethod.Synchronize)` in the console probe, no KeePass window
(`work\kpsync`) [M]. KeePass is another report's subject; these are only the interactions.

- **The tombstones KeePassXC left in its target still work for KeePass.** Synchronizing A after
  KeePassXC merged B into it (entries resurrected beside their tombstones) with the original B gave
  the same entries as synchronizing the original A and B: `del-A-unch` and `del-A-chB-before` gone,
  `del-A-chB-after` kept because it changed after the deletion, the empty group gone [M].
- **The copy KeePassXC wrote into never holds those tombstones**, so the deletion survives only
  while the deleting copy, or one KeePass synchronized with it, takes part [M, A].
- **A group time KeePassXC raised without copying the field makes the stale field win.**
  Synchronizing the original A with B after KeePassXC merged A into it lost A's group tag: B carried
  A's modification time and no tag, and KeePassLib took B's on equal times [M]. The same is
  expected for the group flags and group custom data, which KeePassXC also leaves behind under a
  raised time [A].
- The location time KeePassXC regressed on `move-A-edit-B` came through KeePass's synchronize too
  [M].
- With A as the target and B as the source, KeePassLib's synchronize resolved the tie in
  `both-tie` to B's version, with no trace of A's [M].

## 10. Not run, and what is still to measure

- **The window's merge** (MergeDialog, from 2.7.11), **the merge on reloading a file changed on
  disk**, **KeeShare** and **2.8.0's remote sync**: read only; they call the same `Merger`.
- **Browser integration, auto-type, the reports and the HIBP check**: read only.
- **Releases** 2.5.x and 2.7.1 to 2.7.11: where history stopped being truncated on a winning source
  lies between 2.7.0 (read) and 2.7.12 (read and measured).
- A **string value of a megabyte or more** through a save and a merge.
- **KDBX 3.1 files** and AES-KDF in a merge.
- **Two machines with skewed clocks** editing the same entry: follows from the time comparison [A],
  not run.
- Whether the **repeated history change** on `hist-big`, reported and saved on every merge in 2.8.0,
  goes on for ever.
- **KeePass 2.62's own window** merging KeePassXC's output, rather than KeePassLib in a console.
- The **entry custom data marker** `_LAST_MODIFIED`, rewritten at every load, makes two otherwise
  equal entries compare unequal (`CustomData::operator==` compares every item, marker included,
  `src/core/CustomData.cpp:210-213`, 2.8.0) [R]; whether that ever causes more than spurious change
  reports was not measured.
