# Ground report: what KeePass 2.62 itself does to each place KPG data could live

Key: `keepass`. Subject: KeePass 2.62 and its KeePassLib, measured headless and read from source.
Nothing here is about another program; where a sentence touches one, it says so and marks it.

## How to read this

Every statement carries its basis:

- **[measured Xn]**: the probe below ran it against KeePass 2.62's own KeePassLib and the line
  `Xn` in its output shows it.
- **[read file:line]**: read from `KeePass-2.62-Source.zip`, paths relative to the archive root.
- **[spike]**: recorded in `RESULTS.md` of the branch `spikes/revocation-merge` (measured there against
  2.61.1); where 2.62 was re-read, that is said.
- **[assumed]**: neither run nor read; a belief, and labeled so.

### What was run

- KeePass 2.62's `KeePass.exe` from `.keepass\2.62`, loaded as an assembly by a
  console probe (`KeePassLib 2.62`, x64, .NET Framework 4.8 runtime 4.0.30319.42000). No window, no
  KeePass process, only KeePassLib types [measured, header line of every output file].
- Source: `KeePass-2.62-Source.zip` from SourceForge, SHA-256
  `9C07B0539A013EE5E22B26FD806E472C1858BC2F3F7B3B4406F4404774263AC0`, 5,339,759 bytes, which is the
  value keepass.info/integrity.html publishes for that file [measured].
- Probe source:
  `spikes\storage\probes\ground-keepass\probe\Program.cs`.
  Outputs, one file per experiment:
  `spikes\storage\probes\ground-keepass\out\x1.txt`
  to `x13.txt`. Rerun: `Probe.exe <empty work dir> x2 <output file>`.
- Every database is new, made by the probe, with invented content, AES-KDF at one round.
- **Shape of every merge cell.** A base file is made and saved; copy A and copy B are each opened
  from it, changed, saved; then for each of the five methods and both directions the target file
  is opened, the source file is opened, `target.MergeIn(source, method)` runs, the result is saved
  to a new file and reopened, and the reopened file is what is reported. "Into A" means A is the
  open database and B arrives. Times are set explicitly, in whole seconds, as offsets from a base
  time `T0` (`+20` is twenty seconds after it).
- What this does **not** reproduce: KeePass's window. File then Synchronize runs the same
  `MergeIn(..., Synchronize)` and then saves the result and copies it over the other file
  [read KeePass/DataExchange/ImportUtil.cs:188-275, :351-372], so the merge cells stand for it, but
  the dialogs (entry, group, maintenance, import method) were read, not driven.

Method names in the tables: **Sync** = Synchronize, **IfNewer** = OverwriteIfNewer, **Overwrite** =
OverwriteExisting, **Keep** = KeepExisting, **NewUuids** = CreateNewUuids.

## 1. The question every ground report answers

### 1a. One entry in both copies, each copy adds a different field

**KeePass never merges an entry field by field. It takes one version of the entry whole.**
[measured X2; read KeePassLib/PwDatabase.cs:868-899, KeePassLib/PwEntry.cs:485-529]

The probe ran this for seven kinds of change, each copy changing the same entry differently: a
different custom string on each side, a different protected custom string, the same field to two
values, a different entry custom data key, a different attachment, a different tag, and Notes
holding two different JSON documents. **All seven gave identical outcomes in all 120 cells**
(6 time conditions x 5 methods x 2 directions x 2 backup variants) [measured X2, grouped by a
script that compared every cell across the seven].

Who wins, with "A" and "B" naming whose change the result's current version holds, and "hist A"
meaning A's version survives only as a history item. The copy that changed made a history backup
first, as KeePass's entry dialog does [read KeePass/Forms/PwEntryForm.cs:1081-1085]:

| time condition | Sync and IfNewer, into A | Sync and IfNewer, into B | Overwrite, into A | Overwrite, into B | Keep, into A | Keep, into B |
| --- | --- | --- | --- | --- | --- | --- |
| A newer (+20 vs +10) | A, hist B | A, hist B | **B** (older), hist A | A, hist B | A, hist B | **B, A lost** |
| B newer (+10 vs +20) | B, hist A | B, hist A | B, hist A | **A** (older), hist B | **A, B lost** | B, hist A |
| equal times (+10, +10) | **B, A lost** | **A, B lost** | B, hist A | A, hist B | A, B lost | B, A lost |
| B changed but its time not moved (A +10, B +0) | A, **B lost** | A, hist B | B (+0), hist A | A, **B lost** | A, B lost | B, A lost |
| neither time moved (both +0) | **B, A lost** | **A, B lost** | B, A lost | A, B lost | A, B lost | B, A lost |
| B unchanged but its time rewritten later (A +10, B +30) | **stale wins**, hist A | **stale wins**, hist A | stale, hist A | A, hist stale | A | stale, hist A |

NewUuids, in every row: two separate entries, one holding A's change and one holding B's, each with
only its own history, inside a duplicated tree (section 2.4) [measured X2].

What the rows mean:

- **Strictly newer wins, the loser goes to history.** Sync and IfNewer take the entry with the later
  `LastModificationTime` and keep the other as a history item, in both directions
  [measured X2; read PwDatabase.cs:878-898]. The backup is made of whichever side is older: of the
  open entry when the arriving one is newer (`:878-882`), of the arriving one when the open one is
  newer (`:884-887`), and `MergeEntryHistory` then joins both histories (`:1636-1688`).
- **Equal times lose a version outright.** Both backup conditions require a strict difference
  (`TimeUtil.CompareLastMod(...) > 0`, `:880` and `:885`), and `AssignProperties` returns only when
  the arriving copy is strictly older (`PwEntry.cs:490-492`). So with equal times nothing is backed
  up and the arriving copy overwrites the open one: into A gives B, into B gives A, and the other
  version exists nowhere [measured X2]. KDBX 4 stores whole seconds
  [read KeePassLib/Serialization/KdbxFile.Write.cs:799], so "equal" means the same second.
- **A change that did not move the time can vanish even when the other side is newer.** History
  items are keyed by `LastModificationTime` when histories are joined (`PwDatabase.cs:1659-1679`):
  an item from the arriving copy whose time is already present is dropped, except under
  OverwriteExisting, which replaces it (`:1673-1676`). B's change at `+0` collided with the base
  version's backup, also at `+0`, and was dropped into A [measured X2, row 4 into A]. Without the
  backup (no item at `+0`) the same row keeps B in history [measured X2, `nobackup` variant].
- **A time rewritten forward makes stale content win** in Sync and IfNewer, in both directions, with
  the fresh change kept only in history [measured X2, last row].
- **OverwriteExisting ignores times**: the arriving copy wins whole and the open one goes to history
  (`:879`, `:889-890`) [measured X2].
- **KeepExisting keeps the open entry whole.** When the open one is newer, the arriving version
  still lands in its history (the arriving copy is backed up and histories are joined, which runs
  for every method, `:884-887`, `:898`). When the arriving one is newer, the open entry is backed
  up onto itself and **the arriving version is lost** [measured X2].
- **The result does not depend on what kind of field changed**: custom string, protected string,
  Notes, attachment, tag and entry custom data all behave alike [measured X2].

### 1b. What is in the history afterwards, and how long it stays

| fact | basis |
| --- | --- |
| The defaults are 10 items and 6 MiB per entry (`HistoryMaxItems`, `HistoryMaxSize`), -1 meaning unlimited, both per database settings | read PwDatabase.cs:47-48; measured X5 (`size limit 6291456`) |
| The limits are applied on **every load** (`KdbxFile.Read.cs:255`), **every save** (`KdbxFile.Write.cs:103`), at the end of **every merge** (`PwDatabase.cs:940`), on every entry dialog OK (`PwEntryForm.cs:1137`) and when the history settings change (`DatabaseSettingsForm.cs:391`) | read; measured X5: a file saved holding 9 history items under a limit of 3 read back with 3 |
| The oldest item by `LastModificationTime` goes first | read PwEntry.cs:685-701; measured X5 (round 11 drops revision 0, round 12 revision 1) |
| The **target's** limits apply in a merge, and a merge does not carry the limits across | read PwDatabase.cs:1542-1634 (no history setting in it); measured X5: into the copy with limit 3, 3 items; into the copy with limit 10, 9 |
| With a 1 MiB attachment, 10 synchronized edits left 5 history items: the size limit binds before the count | measured X5 |
| History items with the same time collapse when histories are joined: the open copy's item is kept, except under Overwrite, which takes the arriving one | read PwDatabase.cs:1665-1679; measured X5 (A's `a1` and B's `b1` both at `+10`: into A keeps `a1`, into B keeps `b1`) |
| Rewriting an identical value with a new time fills history with copies differing only in time: 12 synchronized rounds left 10 items, all one value | measured X13; read PwDatabase.cs:873-876 (the equality test compares the modification time) |
| Database Maintenance deletes, for every entry, history items older than N days, N defaulting to 365 | read KeePass/Forms/DatabaseOperationsForm.cs:123-157; PwDatabase.cs:72 |
| The entry dialog's History tab lets the operator delete items and restore one | read PwEntryForm.cs:1054-1063 |
| An operator can set `HistoryMaxItems` to 0, after which a merge keeps no loser at all | measured X13 (`HistoryMaxItems=0`: A's field lost every round, even with preprocessing) |

### 1c. When times are equal, dropped or rewritten

- **Equal (same second)**: one version is lost in Sync, IfNewer and Keep, which one depending on the
  direction [measured X2]. Under Overwrite the loser is kept in history only if no history item
  already holds its time [measured X2, `neither-time-moved` with and without backup].
- **Dropped**: KDBX has no "no time" for an entry. An entry element with no
  `LastModificationTime` reads as `PwDefs.DtDefaultNow`, the moment KeePassLib's assembly loaded,
  so it looks newer than everything written before that moment and wins every Sync and IfNewer
  [measured X12: `lm=+7201`, equal to `DtDefaultNow`; read KeePassLib/PwEntry.cs:58-64,
  KeePassLib/PwDefs.cs:110].
- **Not moved** (content changed, time left alone): that version loses to any later time and can be
  lost entirely by the history key collision above [measured X2].
- **Rewritten forward** on stale content: the stale content wins [measured X2].
- In memory, an entry KeePass touched carries sub-second ticks until it is saved, while a file
  carries whole seconds, so two edits in the same second compare as unequal inside one KeePass
  session [read PwEntry.cs:536-541 uses `DateTime.UtcNow`; KdbxFile.Write.cs:799] [assumed: that
  this changes an outcome in practice; not run].

### 1d. Two different entries added to one group, one in each copy

**Both always arrive**, in all five methods and both directions, whether their content differs or
is identical [measured X1]. NewUuids also duplicates everything else (section 2.4) [measured X1].
The entries' order inside the group differs by direction; nothing else does [measured X1].

### 1e. The proposed design, measured: a field named by a timestamp, and a preprocessing step

Each round, copy A adds a field `KPG.A<k>` to one entry and copy B adds `KPG.B<k>` to the same
entry, then one Synchronize writes the result to both files, as KeePass's synchronize does. Twelve
rounds, 24 fields written. "Preprocessing" is what a plugin could do on load and after each merge:
copy back into the entry every `KPG.` field that only a history item holds, and move the time one
second past everything seen [measured X13]:

| preprocessing | B writes | fields still in the entry or its history after round 12 | lost |
| --- | --- | --- | --- |
| none | 5 s after A | 22 of 24 (the entry itself held 12) | A01, A02, pushed out by the 10 item limit |
| none | the same second | 12 of 24 | every field of the side that lost each tie |
| on load and after merge | 5 s after A | **24 of 24**, all in the entry | none |
| on load and after merge | the same second | 12 of 24 | as without preprocessing: a tie leaves nothing to copy back |
| on load and after merge, `HistoryMaxItems=0` | 5 s after A | 4 of 8 after 4 rounds | every A field: the merge's own trim removes the loser before the plugin sees it |
| on load and after merge, `HistoryMaxItems=1` | 5 s after A | 8 of 8 | none |

Three copies each adding one field, merged into the first one after the other with no
preprocessing: the entry held only the newest copy's field and the history held the other two
[measured X13].

So the design works **only** when every conflicting write is at least a second apart, the history
limit is at least 1, the preprocessing runs between KeePass's merge and its save, and no
KeepExisting import runs. Where in KeePass that step can run is section 4.

## 2. Entries in a dedicated group

### 2.1 Which KDBX version each entry feature needs

KeePass 2.62 never writes below KDBX 4.0 on its own, even with AES-KDF and an empty database
[measured X7; read KdbxFile.cs:351-397, which returns 4.0 at minimum]. (The spike measured 2.47
writing 3.1 in that case; 2.62 does not.)

| feature | version written | basis |
| --- | --- | --- |
| standard, custom and protected strings, attachments, entry tags, Notes, standard icon, expiry, history, colors, override URL, auto-type off | 4.0, and all of it read back | measured X7 |
| entry excluded from reports (`QualityCheck` false) | **4.1** | measured X7; read KdbxFile.cs:373-374 |
| entry custom data | 4.0 | measured X7 |
| previous parent (a moved entry) and nothing else 4.1 | 4.0, and **the previous parent is dropped** | measured X7 (`previousParentAfterReopen=False`); read KdbxFile.Write.cs:545-546 |
| previous parent beside any other 4.1 feature | 4.1, kept | measured X7 |

### 2.2 Deleting, recycling, moving, and an entry in a deleted group

A acts at `+20`; "Sync" covers both directions unless split [measured X3, X4]:

| scenario | Sync | IfNewer, Overwrite, Keep into A (the copy that acted) | IfNewer, Overwrite, Keep into B |
| --- | --- | --- | --- |
| E deleted on A, B unchanged | gone in both, 1 tombstone | gone: A's own tombstone deletes the older E arriving | **E stays**, no tombstone: B never learns |
| E deleted on A, B changed it later (+30) | E back with B's change, tombstone gone | same | same |
| E deleted on A, B changed it earlier (+10) | gone in both | gone | E stays with B's change |
| E recycled on A (moved into the bin), B unchanged | in the bin in both | in the bin | stays in its group |
| E recycled on A, B changed it later (+30) | **in the bin, with B's newer content**: location and content merge separately | in the bin; Keep loses B's change | in its group; Overwrite brings A's older content back over B's |
| E moved to G2 on A, B unchanged or changed later | in G2 (with B's content if newer) | in G2 | stays where B had it |
| moved on A to G2 (+20) and on B to G3 (+30) | G3: the later move wins | target's location | target's location |
| group G2 deleted on A, B unchanged | G2 and its entry gone, 2 tombstones | gone | G2 stays |
| G2 deleted on A, B **added** entry N to it (at +30, and at +10) | **G2 survives holding only N**; the old entry is deleted, G2's own tombstone is dropped | same | G2 with both entries |
| G2 deleted on A, B changed G2's entry later (+30) | G2 and the entry survive, tombstones gone | same | same |

Why: deletions are only merged in by Synchronize (`PwDatabase.cs:926-929`), but the open database's
**own** tombstones are applied under every method (`:929`), so an arriving object older than one of
them is deleted again. A tombstone is dropped when its object is present and newer, or when the
object is a group that is not empty (`:1067-1131`, the empty test at `:1101-1108`) [read]. Moves
are only merged by Synchronize, by `LocationChanged` (`:911-921`, `:1189-1234`) [read; measured X3].
Recycling is a move that sets the previous parent and `LocationChanged` and leaves the
modification time alone (`KeePass/Forms/MainForm_Functions.cs:5159-5166`) [read].

### 2.3 Fields of an entry other programs or the operator can reach

- **Tags cannot carry structured data.** On every read of `Tags` they are trimmed, `,` and `;` are
  replaced by `.`, duplicates removed and the list sorted [read KeePassLib/Utility/StrUtil.cs:1531-1577,
  KeePassLib/PwEntry.cs:250-252].
- **Notes edited in the entry dialog are read back from a rich text box** on OK
  [read PwEntryForm.cs:376-377]; that this turns `\r\n` into `\n` is [assumed], a WinForms
  behavior not measured here. An unchanged entry keeps its old modification time because the
  comparison normalizes line breaks (`PwEntryForm.cs:86-87`, `:1119-1125`) [read], so a changed
  byte sequence could be written under an unchanged time [assumed: not run].
- **Expiry**: an entry with `Expires` set is listed at database open as expired or soon to expire,
  unless its group has searching off [read MainForm_Functions.cs:2356-2367, :2620-2625].
- **Icons, colors, override URL, auto-type settings** merge with the entry, whole [read
  PwEntry.cs:504-528].

### 2.4 CreateNewUuids, which File then Import offers first

CreateNewUuids gives every group and entry of the arriving copy a new identifier and adds them
all: the arriving root becomes an **empty group named after the arriving database**, and the
arriving copy's top-level groups are added again **at the open root**, beside the originals
[measured X1, X3; read PwDatabase.cs:768-772, :792-824, in particular `:804-805`]. Every KPG group
and entry is then present twice. A deleted entry comes back as a copy under a new identifier,
because no tombstone can name it [measured X4: "into B, NewUuids" holds a copy of E that B had
deleted]. It is the preselected choice of File then Import for a KeePass file
[read KeePass/Forms/ImportMethodForm.cs:37, :69], the method used for every format without
identifiers [read ImportUtil.cs:191], and the default of the trigger action "Import into active
database" [read KeePass/Ecas/EcasDefaultActionProvider.cs:463-467].

## 3. Groups

| fact | basis |
| --- | --- |
| A group merges **whole**: name, notes, icon, times, expiry, default auto-type sequence, `EnableAutoType`, `EnableSearching`, last top entry, tags and custom data, all from whichever side is strictly newer; there is no history for groups | read KeePassLib/PwGroup.cs:503-545; measured X6 |
| Equal times: the arriving copy wins whole, so the result depends on the direction | measured X6 (into A: B's tags and custom data; into B: A's notes) |
| Overwrite takes the arriving group whole regardless of time; Keep keeps the open one | measured X6 |
| `IsExpanded` is **never merged**: each copy keeps its own, and a group new to one copy arrives expanded | measured X6 (`NewOnA` collapsed in A, expanded in B after the merge); read PwGroup.cs:503-545 (absent) |
| Group tags force KDBX 4.1; notes, searching off, auto-type off and collapsed do not | measured X7; read KdbxFile.cs:363-364, KdbxFile.Write.cs:505-513 |
| The group dialog's OK always moves the modification time, changed or not, and writes back the notes, tags and custom data it copied when it opened | read KeePass/Forms/GroupForm.cs:161, :209-231 |
| The group dialog writes notes back through a text box that on Windows normalizes line breaks to `\r\n` | read GroupForm.cs:116, KeePass/UI/UIUtil.cs:717-726 |
| Expanding or collapsing a group in the tree does not move its time | read KeePass/Forms/MainForm.cs:1391-1395 |
| **Delete Empty Groups** deletes, with no confirmation found, every group with no entries and no subgroups, including a group holding only notes and custom data, an empty recycle bin, and a parent that becomes empty when its empty child goes | measured X10 (4 deleted); read PwDatabase.cs:2094-2112, KeePass/Forms/MainForm.cs:2274-2282 |

So a group's notes as storage has the merge of custom data on a group today (newer whole group
wins) with two more ways to lose it: any OK in the group dialog of a stale copy makes that copy's
group the newest [read], and Delete Empty Groups removes a storage group that holds no entry
[measured X10].

## 4. Entry and group custom data, for comparison, and where a plugin can run

- **Entry custom data merges with its entry, whole**, exactly like a custom string [measured X2,
  `entry-custom-data` identical to the other six kinds]. It carries no per-key times: only the
  database's dictionary is made to remember them [read KeePassLib/Collections/StringDictionaryEx.cs:49-56;
  KeePassLib/PwEntry.cs and PwGroup.cs construct it with the public constructor].
- **Group custom data merges with its group, whole** [measured X6; spike section 1 for 2.61.1].
- **Database custom data** is the per-key rule the spike measured on 2.61.1; the loop is unchanged
  in 2.62 [read PwDatabase.cs:1603-1623].
- **The dialogs that copy and write back.** The entry dialog copies strings, attachments, auto-type,
  history (`PwEntryForm.cs:231-234`) and custom data (`:486`) when it opens and writes all of them
  back on OK (`:1081-1113`); the group dialog does the same for its fields (section 3); Database
  Maintenance does it for the database's custom data, on a key count (`DatabaseOperationsForm.cs:73`,
  `:82-92`) [read]. A plugin write to an entry while its dialog is open is therefore undone by OK,
  the same trap `GroupForm` sets today [read].
- **Where a preprocessing step can run.** File then Synchronize merges, then saves through the
  normal save path (`ImportUtil.cs:204`, `:234`), which raises `FileSavingPre` and `FileSaving`
  (`MainForm.cs:736`, `:745`) before writing, and then copies the saved file over the other file
  (`ImportUtil.cs:259-261`) [read]. A step in `FileSavingPre` therefore runs after the merge and
  before either file is written [read; not run]. A save that finds the file changed on disk offers
  Synchronize instead, or runs it when `SaveForceSync` is set: that check is `PreSaveValidate`
  (`MainForm_Functions.cs:4664-4689`), which runs after the outer save's `FileSavingPre`
  (`MainForm.cs:733-740`), and the Synchronize it starts saves with the force flag, so a second,
  inner `FileSavingPre` fires after the merge (`MainForm_Functions.cs:3788-3799`) [read]. A step
  there therefore sees the database once before the merge and once after it [read; not run]. A file another program merged is trimmed to the file's own history limits by the load
  (`KdbxFile.Read.cs:255`) before `FileOpened` is raised (`MainForm_Functions.cs:2615-2616`) [read].
  Nothing raises an event inside `MergeIn`, so the merge's own history trim at `PwDatabase.cs:940`
  always runs before a plugin can look [read; measured X13 with `HistoryMaxItems=0`].

## 5. DeletedObjects (tombstones)

| fact | basis |
| --- | --- |
| A tombstone is a UUID and a deletion time and nothing else | read KeePassLib/PwDeletedObject.cs |
| One naming nothing survives save and reopen | measured X4 |
| Only Synchronize carries tombstones to the other copy; the other four methods never copy one | measured X4; read PwDatabase.cs:927-928 |
| The open database's own tombstones are applied under every method, to arriving objects too | measured X3, X4; read PwDatabase.cs:929 |
| A tombstone is dropped when its object is present and newer, or is a non-empty group | measured X3, X4; read PwDatabase.cs:1083-1096 |
| KeePass never ages them out; the only wholesale removal is Database Maintenance, "Remove deleted objects information", which clears them all | read DatabaseOperationsForm.cs:159-170; no other `Clear` in KeePassLib or KeePass |
| KeePass XML export can leave them out | read KeePass/DataExchange/Formats/KeePassXml2.cs:58-68 |
| They need only KDBX 4.0 | measured X7 |
| Delete Duplicate Entries, Delete Empty Groups, deleting with Shift or from the bin, and emptying the bin all add tombstones dated now | read PwDatabase.cs:2015, :2023, :2106; MainForm_Functions.cs:5154-5158, :5255-5261; PwGroup.cs:1367-1386 |

## 6. The binary pool

The pool is not a place of its own: it is built at every save from the attachments of every entry
and every history item, deduplicated by content, and written in the inner header
[read KdbxFile.Write.cs:91-92, KeePassLib/Collections/ProtectedBinarySet.cs:102-140]. An attachment
nothing references is not written. Attachments therefore live and merge exactly as their entry does
[measured X2, `attachment` variant identical to the others]. Delete Duplicate Entries compares
attachments by name and bytes, so two entries that differ only in an attachment's name are not
duplicates [measured X10].

## 7. The header's public custom data

| fact | basis |
| --- | --- |
| It is **not encrypted**: `PwDatabase.LoadHeader` reads it with no key at all | measured X7 (`visible`); read PwDatabase.cs:751-756 |
| It needs only KDBX 4.0 | measured X7 |
| A merge takes the union of keys; for a key both hold, the copy with the later `SettingsChanged` wins, and with equal settings times the open copy wins; Overwrite takes the arriving values; Keep copies nothing | measured X8; read PwDatabase.cs:1625-1633 |
| A value changed in the copy whose settings are older is lost, even to an unchanged value | measured X8 (A changed `k0` at settings `+10`, B's settings `+20`: the result has B's untouched `v0`) |
| No method removes a key | measured X8; read PwDatabase.cs:1625-1633 |

## 8. Custom icons

| fact | basis |
| --- | --- |
| Icons merge as a union by UUID; for a UUID both hold, the strictly newer icon time wins, and an icon with a time beats one without | read PwDatabase.cs:946-982 |
| A deleted icon leaves a tombstone, and Synchronize deletes it on the other side; other methods leave it there | measured X9; read PwDatabase.cs:1793-1810, :984-1013 |
| A name or a time on an icon forces KDBX 4.1 | measured X7, X9 |
| Tools then Database Tools, "Delete unused custom icons", deletes every icon no group, entry or history item points at | read PwDatabase.cs:2114-2143, MainForm.cs:2284-2293 |

## 9. KeePass's own functions that reach KPG data without the plugin

"Prevent" means the plugin can stop it, "detect" that it can tell afterwards, "suffer" that it can
do neither.

| function | what it does to KPG data | plugin | basis |
| --- | --- | --- | --- |
| Entry dialog OK | writes back the strings, attachments, custom data, tags and history copied at open; makes a history backup and moves the time when something changed; the operator can delete fields, custom data items and history items | prevent a lost write by refusing writes while it is open, as `GroupDialogs` does for groups; suffer the operator's own edits | read PwEntryForm.cs:231-234, :486, :1066-1152 |
| Group dialog OK | writes back notes, tags, custom data; **always** moves the time | prevent lost writes the same way; detect a moved time only by comparing with something kept elsewhere | read GroupForm.cs:161, :209-231 |
| Database Maintenance | deletes history older than N days (365) for all entries; clears every tombstone; writes back its copy of the database custom data on a key count | detect a drop only against state kept elsewhere; suffer | read DatabaseOperationsForm.cs:73-92, :123-170 |
| Database Settings, history limits | trims every entry's history at once | suffer | read DatabaseSettingsForm.cs:391 |
| Delete Duplicate Entries | deletes, permanently and with tombstones, one of any two entries whose standard and custom strings and attachments are equal, **ignoring custom data, tags, times and group**; it prefers deleting the older one, and one in the bin | **prevent** by giving every KPG entry a string no other entry can share | measured X10 (custom data only, tags only, and two meta entries in two user groups: one deleted each time; a group UUID in a field: none deleted); read PwDatabase.cs:1969-2092 |
| Delete Empty Groups | deletes groups with no entries and no subgroups, a notes-only storage group included | prevent by keeping an entry in every storage group | measured X10 |
| Delete and the recycle bin | a delete moves the entry or group into the bin (location changes, time does not), Shift or a delete inside the bin removes it with tombstones; Empty Recycle Bin removes everything in it with tombstones | detect (an entry inside the bin, a tombstone); suffer | read MainForm_Functions.cs:5138-5321, PwGroup.cs:1367-1386 |
| Search and Find | skip entries in a group with searching off, by default | prevent: put KPG entries in a group with `EnableSearching` false | measured X11; read KeePassLib/PwGroup.Search.cs:129-131 |
| Field references `{REF:...}` | search **ignoring** the searching switch and take the first match in tree order, so a search reference such as `{REF:P@O:text}` in a user's entry can resolve to a KPG entry whose field contains the text | detect only; suffer | measured X11 (the reference's parameters found the KPG entry); read KeePass/Util/Spr/SprEngine.cs:627-665 |
| Auto-type | candidates are entries with auto-type on in a group with auto-type on; by default an entry whose **title** occurs in the target window's title matches; tag, URL matching are off by default | prevent: `EnableAutoType` false on the KPG group, or auto-type off on each entry | read KeePass/Util/AutoType.cs:136-149, :279-363, App/Configuration/AceIntegration.cs:134-149, PwEntry.cs:704-712 |
| Placeholders in auto-type matching | the title and URL are compiled as placeholders before matching | prevent: fixed titles, no client-supplied text in title or URL | read AutoType.cs:312-327 |
| Password quality, duplicate and similar password reports | skip entries in groups with searching off, entries with an empty password, TANs, and (quality only) entries excluded from reports | prevent: searching off, or no password field | read KeePass/Util/EntryUtil.cs:1014, :1019-1021, :1150-1158, :1587-1596 |
| Large entries, last modified, history events reports | list every entry, searching off or not | suffer (display only) | read EntryUtil.cs:1620-1718, :1749 |
| Expiry warnings at open | list entries with `Expires` set, skipping groups with searching off | prevent: no expiry on KPG entries, or searching off | read MainForm_Functions.cs:2356-2367, :2620-2625 |
| Export and print | include KPG entries; print can include custom strings; KeePass XML includes everything | suffer (an operator act) | read KeePass/Forms/PrintForm.cs:410, :737 |
| Triggers | "Import into active database" defaults to CreateNewUuids; a synchronize action is a Synchronize | detect duplicates; suffer | read EcasDefaultActionProvider.cs:445-486 |
| File then Import | preselects "Create new IDs", which duplicates the whole tree | detect duplicates; suffer | read ImportMethodForm.cs:37, :69; measured X1, X3, X4 |
| XML Replace | XPath edits over the whole database's XML, merged back by Synchronize; edited entries are touched, removed nodes get tombstones | suffer; detect by validating the data | read KeePass/Util/XmlUtil.Replace.cs:160-223, :258-337 |
| Templates group | if the operator points it at the KPG group, KPG entries are offered as templates for new entries | suffer; detect | read (database setting `EntryTemplatesGroup`, merged at PwDatabase.cs:1590-1601) |
| Duplicate entry, copy and paste | copies carry new UUIDs | detect | read EntryUtil.cs:258-279 |
| Opening a file with duplicate UUIDs | KeePass offers a repair that gives new UUIDs | detect | read MainForm_Functions.cs:6548-6574, :2612-2613 |
| Saving when the file changed on disk | runs or offers Synchronize | the plugin can hook the save (section 4) | read MainForm_Functions.cs:4668-4683 |

## 10. What each candidate is, judged on KeePass 2.62 alone

| candidate | merge unit | survives every KeePass merge? | version | worst KeePass-only hazard |
| --- | --- | --- | --- | --- |
| one entry per record, added once and never changed | the entry | yes, both copies' records arrive in all five methods; NewUuids duplicates them | 4.0 | Delete Duplicate Entries when two records' strings are equal; deletions travel only through Synchronize |
| fields added to one shared entry | the whole entry | no: the loser lives only in history, and a tie, a key collision, Keep, or a history limit of 0 loses it | 4.0 | equal times |
| entry custom data | the whole entry | as the row above | 4.0 | as above |
| group notes, tags, flags, group custom data | the whole group, no history | no: newer whole group wins, ties go to the arriving copy | tags 4.1, rest 4.0 | the group dialog's OK moves the time; Delete Empty Groups |
| DeletedObjects | each tombstone | only Synchronize copies one | 4.0 | Database Maintenance clears them all |
| binary pool | not a place: attachments of entries | as entries | 4.0 | as entries |
| header public custom data | each key, by `SettingsChanged` | keys yes, values no | 4.0 | **unencrypted**; a value changed under older settings is lost |
| custom icons | each icon by UUID | added icons yes; deletions only by Synchronize | 4.0, or 4.1 with name or time | "Delete unused custom icons" |

## 11. Not run, and what is still to measure

- **The dialogs themselves**: the entry dialog's line break handling of Notes, the group dialog's
  time on an unchanged OK, Database Maintenance, Delete Duplicate Entries and Empty Recycle Bin
  through KeePass's window. All were read; running them needs a window, which this run could not
  show.
- **A real File then Synchronize**, with `FileSavingPre` as the hook for a preprocessing step, to
  confirm it fires after `MergeIn` and before the write, and that a change made there reaches both
  files.
- **Sub-second times**: a merge into a copy holding unsaved, KeePass-touched entries (ticks finer than
  a second) against a file.
- **More than two copies with preprocessing**, and longer chains against the history limit and the
  size limit with realistic record sizes.
- XML Replace, triggers, templates group, print and export: read only.
- **Whether other programs move `LastModificationTime` when they change an entry, keep entry
  history and keep every custom string**: not this report's subject, and the answer decides how
  often the tie and "time not moved" rows of section 1a occur.
