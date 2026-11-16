# Ground report: the Android programs (key `android`)

What Keepass2Android, KeePassDX and KeePassDroid do to each feature a new KPG storage could use, read
from their source at the releases `spikes/revocation-merge` measured, with what that spike already
measured beside it. Written 2026-11-15 for the storage rethink.

## 0. Basis, sources and shorthands

Every statement carries one of three marks:

- **measured**: run here, or shown by the spike's recorded output (marked "measured (spike)", with
  the file);
- **read**: from source at the tag named below, cited as `path:line` relative to that tag's root;
- **assumed**: reasoning or expectation that nothing here ran or read directly.

Sources, each a shallow clone into its own directory under the scratch directory
`spikes\storage\probes\ground-android\src\`:

| shorthand | program, tag | commit | where the database code is |
| --- | --- | --- | --- |
| K2A | Keepass2Android `v1.15-r6` | `bbcf3787c75b` | `src/KeePassLib2Android` (a KeePassLib port reporting 2.35), `src/Kp2aBusinessLogic`, `src/keepass2android-app` |
| DX 4.5.8 | KeePassDX `4.5.8` | `77ecfa8654a5` | `database/src/main/java/com/kunzisoft/keepass/database/...` (cited as `database/...` below) and `app/src/main/java/...` |
| DX 3.2.0 | KeePassDX `3.2.0` | `5baf91dc779d` | `app/src/main/java/com/kunzisoft/keepass/database/...` (cited as `app/.../database/...`) |
| KPD | KeePassDroid `v2.7` | `f4212ee4cd08` | `app/src/main/java/com/keepassdroid/database/...` (cited as `keepassdroid/database/...`) |

KeePassDX 3.3.0 was not cloned; the spike ran it for a save only.

**What was measured in this task:** one thing, the cost of finding two attachment bodies that
KeePassDX would treat as one (section 5.1). No emulator was started and no Android program was run,
as instructed. None of the three programs has a merge that runs from a console, so **every
statement below about an Android merge is read, except where the spike's recorded output is
cited.** Section 7 lists the emulator runs that would turn the most important of them into
measurements.

## 1. The shared question: does a merge keep both fields?

The setting: the same entry exists in both copies, and each copy has added a different custom
string field to it (or changed a different field).

**No Android program merges an entry field by field.** In every program that merges at all, one
version of the entry becomes current whole, and the other version survives, if at all, only as an
item in that entry's history (read; the spike measured the same shape for an entry that differed in
one place, section 1.2).

### 1.1 Per program

| program | the two versions carry different modification times | the two versions carry the same modification time | basis |
| --- | --- | --- | --- |
| K2A | The newer one is current, whole: strings, attachments, tags, custom data. The older one is added to history, unless that history already holds an item equal to it. Which copy is open does not matter. | **The arriving copy is current, whole.** The open copy's version is lost: no history item is made for it. | read: `PwDatabase.cs:799-824` (both backups need a strictly newer other side, `:805-813`), `PwEntry.cs:551-595` (`AssignProperties` returns only when the arriving copy is strictly older, `:556-558`, and copies everything, custom data at `:594`) |
| DX 4.5.8 | The newer one is current, whole (`updateWith` replaces every field, `EntryKDBX.kt:121-145`). The older one is added to history if no history item has the same modification time to the second. The entry is moved to the arriving copy's group when the arriving copy is newer. | **The open copy is current, whole; the arriving copy's version is lost**, no history item. The entry is moved to the arriving copy's group. | read: `database/merge/DatabaseKDBXMerger.kt:505-527`, `:532-552` |
| DX 3.2.0 | As 4.5.8, but the entry is moved only when the arriving copy is newer, and if the arriving copy's group does not exist in the open copy, **the entry is removed and not put back**. | **Nothing happens**: the open copy's version stays, the arriving one is lost, no history item, no move. | read: `app/.../database/merge/DatabaseKDBXMerger.kt:376-398` (no equal branch; removal without re-adding at `:388-391`) |
| KPD | has no merge (spike: menus listed, strings read) | | measured (spike) `output/android-screens.txt`; read (spike) `app/src/main/res/values/strings.xml` |

**Custom data on the entry is the exception in KeePassDX**, and it is not a field-by-field merge of
strings either: before comparing times, DX 4.5.8 and 3.2.0 put every arriving custom data item into
the open entry, overwriting items both copies hold whenever either item lacks a time
(`DatabaseKDBXMerger.kt:415-432`, called at `:507`; 3.2.0 `:306-324`, `:378`) (read). An entry's
custom data item carries no time when KeePass writes it (read (spike), RESULTS.md 541-543; K2A's
port makes the entry's dictionary without times, `PwEntry.cs:70`), so the arriving value wins for
every key both hold. If the arriving entry is also newer, `updateWith` then replaces the custom data wholesale with
the arriving copy's (`EntryKDBX.kt:129`) (read). Because the open entry's custom data is overwritten
before that entry is copied into history, **the history item does not keep the losing version's
custom data** (read; the `measured` ground report shows this in the spike's output). Custom
**strings** are not touched by that step, so a history item keeps the losing version's strings
intact (read).

### 1.2 What the spike already measured on this point

- In all 18 K2A merges and all 18 KeePassDX merges, E1 (the same entry, differing only in its rule
  and its time) came out with the newer time and **one older version in its history**
  (measured (spike), `output/android-merge.txt`; RESULTS.md 511-536).
- No measured run had two versions that differ in **strings**, two versions with the **same**
  time, an entry with more than one history item, or an entry that only the **arriving** copy
  held (measured (spike), absence in `output/android-merge.txt`; the `measured` ground report says
  the same).

### 1.3 What is in the history afterwards, and for how long

| program | how two histories combine in a merge | when history is pruned | limits | basis |
| --- | --- | --- | --- | --- |
| K2A | union keyed by exact `LastModificationTime`; on a tie the open copy's item stays | **at every load** (without marking the database modified), **after every merge**, and when an entry is backed up during an edit | `HistoryMaxItems` and `HistoryMaxSize` from the file, defaults 10 and 6 MiB; oldest by modification time removed first | read: `PwDatabase.cs:1547-1599`, `:866`, `:1601-1614`; `Serialization/KdbxFile.Read.cs:278-283`; `PwEntry.cs:721-780`; defaults `PwDatabase.cs:49-50` |
| DX 4.5.8 | the losing version and every history item of the other side whose time (to the second) is not already present | **only when that entry is edited in KeePassDX** (`UpdateEntryRunnable.kt:83-87`), or for every entry when the user changes the max-items setting (`DatabaseMaxHistoryItemsPreferenceDialogFragmentCompat.kt:64`); never at load, save or merge | from the file, defaults 10 and 6 MiB (`database/element/database/DatabaseKDBX.kt:898-899`) | read: `database/element/Database.kt:1294-1345`; merge `DatabaseKDBXMerger.kt:532-552` |
| DX 3.2.0 | same shape (`addHistory`, `DatabaseKDBXMerger.kt:406-426`) | on edit only (`UpdateEntryRunnable.kt:61`) and on the setting change | same defaults (`DatabaseKDBX.kt:929-930`) | read |
| KPD | no merge | on edit only (`EntryEditActivityV4.java:145`, `keepassdroid/database/PwEntryV4.java:381-418`) | defaults 10 and 6 MiB (`PwDatabaseV4.java:63-64`) | read |

- **No program prunes history by age.** `MaintenanceHistoryDays` is read and written by all four
  and acted on by none (read: every use found by search is the reader, the writer or the field).
- History is **searched by none of them by default** (K2A `SearchInHistory` false,
  `PwDefs.cs:359-365`; DX searches current fields only, `database/search/SearchHelper.kt:160-268`)
  (read). It is visible in each program's history view (assumed).
- So a version that lost a merge **stays as long as fewer than 10 newer versions of that entry
  pile up** (and the history stays under 6 MiB), counted afresh at every K2A load; in KeePassDX it
  stays indefinitely until somebody edits that entry there (read). Anything that relies on finding
  a lost version later must read history, and must expect it to be gone after ten changes (assumed).

### 1.4 When times are the same, dropped or rewritten

- **None of the three programs drops node times.** Entries, history items and groups are written
  with their `Times` element by all four (read: K2A `Serialization/KdbxFile.Write.cs:588`, DX
  `database/file/output/DatabaseOutputKDBX.kt:694-706`, KPD `keepassdroid/database/save/PwDbV4Output.java:499`).
  A plain save moved no entry's or group's time, except that DX gave the root group the time of the
  save (measured (spike), RESULTS.md 447-448, 450-461).
- **They do rewrite times as a side effect of their own edits**, which matters for whole-node
  "newer wins": DX touches every parent group's modification time when an entry is added, edited,
  moved or deleted (`NodeVersioned.kt:134-146`; `AddEntryRunnable.kt:65-67`,
  `UpdateEntryRunnable.kt:83`, `MoveNodesRunnable.kt:74`, `DeleteNodesRunnable.kt:58`) (read).
  K2A's `Touch(true)` also touches parents (`PwEntry.cs:603-632`) (read). A move or a deletion to the
  recycle bin in DX **changes the entry's own modification time**; in K2A it does not
  (`DeleteRunnable.cs:206-218` uses `Touch(false)`) (read).
- **Equal times** are not exotic: KDBX 4 stores whole seconds (read: DX `readDateInstant`,
  `DatabaseInputKDBX.kt:852-873`), so two edits in one second, or a writer that stamps a fixed time,
  tie. The outcomes are opposite between K2A (arriving wins) and DX 4.5.8 (open wins), and both
  lose one side without history (read, section 1.1).
- DX compares times with `isBefore`/`isAfter`/`isEquals` at the instant's precision, but matches
  history items with `==`, which compares to the second (`database/element/DateInstant.kt:202-236`)
  (read). Times read from files are whole seconds, so the two agree in a merge of two files
  (assumed).
- **A device clock decides.** Every merge above compares the modification times the editing device
  stamped; an Android clock that is behind makes its edits lose (assumed, from the read rules).

### 1.5 Two different entries added to the same group, one in each copy

**Both arrive, in every merging program**, unless the open copy holds a deletion record for the
arriving entry's identifier with a later deletion time (read):

- K2A: an arriving entry the open copy lacks is inserted into the open copy's group of the same
  identifier, or the root if that group is missing; entries only the open copy holds are never
  visited (`PwDatabase.cs:769-794`) (read).
- DX 4.5.8: added to its group; **a missing group is recreated** from the arriving copy
  (`getAttachedParent`, `DatabaseKDBXMerger.kt:439-458`, used at `:470`; added at `:498-504`)
  (read). The recreation ignores deletion records and runs before the entry itself is judged
  (`:470` comes before the check at `:501-502`), so a group the open copy deleted comes back,
  possibly empty, whenever the arriving copy still holds any entry in it (read: `getAttachedParent`
  never consults `deletedObjects`).
- DX 3.2.0: added only if its group exists in the open copy; otherwise **silently dropped**
  (`app/.../DatabaseKDBXMerger.kt:367-375`) (read). Groups are merged before their entries
  (`GroupVersionedInterface.kt:52-72`, the traversal), so a group new on the arriving side is
  normally there in time (read), unless the open copy's deletion record kept it out (`:446-453`).
- Not measured by the spike (section 1.2).

### 1.6 Verdict on the timestamp-named-field proposal

"JSON in an entry field whose name is derived from a timestamp, so that a merge preserves both":

- **Within one entry, it does not hold in K2A or KeePassDX** (read): the newer entry wins whole,
  the other copy's timestamped field survives only in history, history is pruned to 10 versions
  (K2A at every load), and on equal times one side's field is gone without a trace.
- **Across entries, the idea does hold**: one entry per record, each with a fresh identifier,
  merges as a union in both programs, because an entry only one side holds is always added
  (read, 1.5). The timestamp then belongs in the entry (its fields or title), and the record's
  identity in the entry's UUID.
- **KeePassDroid has no merge**, so it neither helps nor hurts a union; what it must do is keep
  the entry intact through a save, and for custom strings it does (read, section 2).

## 2. Each candidate feature: read, kept in memory, written back

`kept` means read into the program's model and written back unchanged by a plain save (read unless
marked). "4.1 only" means written only when the program decides the file is KDBX 4.1.

| feature | K2A 1.15-r6 | DX 4.5.8 | DX 3.2.0 | KPD 2.7 |
| --- | --- | --- | --- | --- |
| entry standard strings (Title, UserName, Password, URL, Notes) | kept | kept | kept | kept |
| entry custom strings, names and values | kept | kept | kept | kept |
| protection flag of a **custom** string | kept | kept | kept | kept |
| protection of **standard** strings | **reset to defaults at every load** (section 5.6) | follows the file's `MemoryProtection` | same as 4.5.8 (assumed) | follows the file's `MemoryProtection` |
| attachments | kept; deduplicated on save by full content | kept, but **deduplicated by a 32-bit hash** (section 5.1) | same as 4.5.8 | kept; deduplicated only when identical (section 5.1) |
| entry tags | kept, normalized | kept, normalized | kept | kept |
| entry icon, custom icon reference | kept | kept | kept | kept |
| expiry (`Expires`, `ExpiryTime`) | kept | kept | kept | kept |
| history items | kept (pruned at load, 1.3) | kept | kept | kept |
| **entry custom data** | kept, without item times | kept, item times kept | kept, **item times dropped** | **deleted at save** |
| group notes | kept | kept | kept | kept |
| group tags | kept; forces 4.1 | kept; forces 4.1 | kept; forces 4.1 | kept; forces 4.1 |
| **group custom data** | kept | kept | **deleted at save** | **deleted at save** |
| database custom data | kept, **per-key times dropped** | kept with times | kept with times | kept with times |
| `DeletedObjects` | kept | kept | kept | kept |
| recycle bin pointer (`RecycleBinUUID`, `RecycleBinChanged`) | kept | kept | kept | kept |
| templates pointer (`EntryTemplatesGroup`) | kept | kept | kept | kept |
| unknown XML elements | **dropped** | **dropped** | **dropped** | **dropped** |
| previous parent group | 4.1 only, and nothing it counts makes a file 4.1 except group tags, quality-check off, a named or timed icon, a timed database key | 4.1 only, same list | 4.1 only, same list | 4.1 only; keeps the version it read |
| node times | kept | kept; root touched on save | kept; root touched on save | kept |
| `SettingsChanged` | kept | kept | kept | **not written** |

Citations and detail per row:

- **Strings and their protection.** K2A writes the custom string's own flag and the database's
  `MemoryProtection` for the five standard names (`Serialization/KdbxFile.Write.cs:863-899`).
  DX 4.5.8 the same (`database/file/output/DatabaseOutputKDBX.kt:560-591`), reading the flag from
  the `Protected` attribute (`DatabaseInputKDBX.kt:641-647`, `:1082`). KPD the same
  (`keepassdroid/database/save/PwDbV4Output.java:673-702`, reader `ImporterV4.java:1154`). Measured
  (spike): every entry and string survived every save (the `measured` ground report, 4.1). (read)
- **Names of custom strings** are written through each writer's XML-safe filter (K2A
  `KdbxFile.Write.cs:870`, DX `DatabaseOutputKDBX.kt:566`) (read). A name built from a timestamp
  and hex (`KPG.2026-11-15T10:00:00Z.ab12`) contains nothing those filters change (assumed). DX
  writes unprotected **values** with no filter (`:586`) (read), so a value must already be valid XML
  text, which KPG's own JSON policy guarantees (assumed, from CLAUDE.md's `JsonBody` rules).
- **Attachments.** K2A reads into a non-deduplicating set (`Serialization/KdbxFile.Read.cs:95-96`)
  and writes through KeePassLib's content-equal pool (assumed: the write side was not read line by
  line). DX: section 5.1. KPD: pool entries match only the same object or the same backing file
  (`keepassdroid/database/BinaryPool.java:86-107`, `security/ProtectedBinary.java:170-178`) (read).
- **Tags.** K2A splits on `,` and `;`, trims, and turns separators inside a tag into `.`
  (`Utility/StrUtil.cs:1399-1410`, `:1496-1507`); DX splits, trims and drops duplicates
  (`database/element/Tags.kt:37-45`) (read). Tags are a lossy channel for data (assumed).
- **Entry custom data.** K2A: written at `KdbxFile.Write.cs:594-595`, its dictionary has no times
  (`PwEntry.cs:70`). DX 4.5.8: read with times (`DatabaseInputKDBX.kt:614-619`, `:825-834`), written
  with them (`DatabaseOutputKDBX.kt:660-676`). DX 3.2.0: read without the time
  (`app/.../file/input/DatabaseInputKDBX.kt:827`). KPD: read (`load/ImporterV4.java:927`) and
  **never written** (`save/PwDbV4Output.java:472-512` has no custom data); measured (spike): E1's
  rule deleted in all three KPD files that had one, its time unchanged (RESULTS.md 456-461).
- **Group custom data.** DX 3.2.0's group writer has no custom data at all
  (`app/.../file/output/DatabaseOutputKDBX.kt:343-362`, against `:394` for entries and `:265` for the
  database): **this is the source line the spike did not cite** for "KeePassDX 3.2.0 and 3.3.0
  delete a group's custom data" (read; the deletion itself measured (spike), RESULTS.md 450-454).
  KPD: not written (`PwDbV4Output.java:438-466`), and read with the key in place of the value
  (`ImporterV4.java:880`, as the spike cited) (read).
- **Group tags and previous parent.** K2A writes both only at 4.1 (`KdbxFile.Write.cs:544-552`) and
  counts group tags as 4.1 (`Serialization/KdbxFile.cs:401-411`), but never a previous parent
  (`:393-455`) (read). DX 3.2.0 counts group tags as 4.1 too
  (`app/.../element/database/DatabaseKDBX.kt:233-264`), and so does KPD
  (`keepassdroid/database/PwDatabaseV4.java:563-582`) (read). DX 4.5.8 decides the version with `getMinKdbxVersion`
  (`database/element/database/DatabaseKDBX.kt:284-322`, applied at `file/DatabaseHeaderKDBX.kt:80`),
  which counts group tags, quality-check off, named or timed custom icons and timed database keys,
  and not a previous parent or an entry custom data time; a previous parent is written only at 4.1
  (`DatabaseOutputKDBX.kt:686-691`) (read). KPD starts from the version it read
  (`PwDatabaseV4.java:606-607`) (read), which is why it kept 4.1 (measured (spike)).
- **Unknown elements.** K2A `ReadUnknown` skips the subtree and keeps nothing
  (`Serialization/KdbxFile.Read.Streamed.cs:1033-1059`); DX `readUnknown`
  (`DatabaseInputKDBX.kt:883-893`); KPD `ReadUnknown` (`load/ImporterV4.java:987-1000`). None of the
  models has anywhere to keep one, and no writer emits one (read).
- **`SettingsChanged`** absent from KPD's writer, measured (spike) RESULTS.md 463-468.

## 3. Merges, feature by feature

KeePassDroid has no merge and is left out. "Open" is the copy the program has open; "arriving" is
the file merged into it. The program then saves the open copy.

### 3.1 Entries

| case | K2A (KeePassLib `Synchronize`) | DX 4.5.8 | DX 3.2.0 |
| --- | --- | --- | --- |
| only added, on one side | added (both directions) | added; missing group recreated | added only if its group exists |
| changed on one side | newer whole; older into history | newer whole; older into history; custom data: arriving wins per key | same as 4.5.8 |
| changed on both, different times | newer whole; older into history | same | same |
| changed on both, same time | **arriving whole**, open lost | **open whole**, arriving lost; moved to arriving's group | **open whole**, arriving lost |
| deleted on one side (deletion record present) | removed if its time is before the deletion; the record is merged into the open copy's list | removed if its time is before the deletion; **the record is not added to the open copy's list** | same as 4.5.8, but a deleted group goes with everything in it |
| deleted on one side (no record, e.g. only in the recycle bin) | stays where the newer location says | stays; location as below | stays |
| moved, not modified | relocated by the newer `LocationChanged` | **moved to the arriving copy's group whenever the times are equal**, so an unchanged arriving copy undoes the open copy's move | moved only when the arriving copy is newer |

Citations (all read):

- K2A entry handler `PwDatabase.cs:769-828`; relocation by `LocationChanged` `:837-847`,
  `:1116-1159`; deletions: the arriving list is merged into the open one (`MergeInDeletionInfo`,
  `:973-992`), applied to entries older than the deletion (`:994-1026`); a record whose object is
  newer than the deletion is removed from the list (`:1014-1023`); a group is deleted only when it
  is empty after relocation (`:1028-1035`).
- DX 4.5.8 `DatabaseKDBXMerger.kt:463-527` (entries), `:304-323` (deletions: `deleteEntry` removes the
  node; nothing in `merge()` calls `addDeletedObject`, so the arriving list is applied and dropped),
  `:519-524` (equal times move the entry to the arriving copy's group).
- DX 3.2.0 `app/.../DatabaseKDBXMerger.kt:274-300` (deletions; a group is removed from its parent
  with its subtree, `:286-290`), `:329-400` (entries).

### 3.2 Groups (notes, tags, custom data travel with the group)

| case | K2A | DX 4.5.8 | DX 3.2.0 |
| --- | --- | --- | --- |
| both hold it, different times | newer whole: name, notes, tags, custom data | newer whole if the arriving one is newer; open kept otherwise; custom data: arriving wins per key first | same as 4.5.8, but group custom data does not survive the save anyway |
| same time | arriving whole | open kept, moved to arriving's parent | open kept |
| only one side | added | added; missing parent recreated | added if its parent exists |

Read: K2A `PwDatabase.cs:710-767`, `PwGroup.cs:550-587` (whole copy, `bOnlyIfNewer` returns only when
strictly older); DX 4.5.8 `DatabaseKDBXMerger.kt:557-593` (the "open is newer" branch compares the
open group's time with itself, `:584`, so it never fires, and the fall-through keeps the open
group); DX 3.2.0 `:431-472`.

Because DX and K2A touch a group's time whenever any entry in it is added, edited, moved or deleted
(section 1.4), **a group's notes, tags or custom data are reverted by a merge whenever the other
copy merely changed an entry inside that group later** (assumed, from the read rules). This is the
same failure the spike measured for group rules, with a second way in.

### 3.3 Database-level items

- Custom data, `SettingsChanged`, and what K2A and DX do to them: measured (spike), RESULTS.md
  section 6; DX `DatabaseKDBXMerger.kt:192-228`, `:268`; K2A `PwDatabase.cs:1469-1545` (read).
- Recycle bin and templates pointers: newer change time wins, and K2A then checks that the group
  exists (`PwDatabase.cs:1503-1528`); DX by change time alone (`DatabaseKDBXMerger.kt:211-219`)
  (read).

## 4. The programs' own functions that reach into such entries

### 4.1 KeePassDX templates, the precedent in full

KeePassDX keeps a whole feature in ordinary entries in an ordinary group, which is the shape a KPG
store in entries would take (read throughout, DX 4.5.8):

- **Where.** The group is named by the standard `Meta` field `EntryTemplatesGroup` (a UUID) and
  stamped by `EntryTemplatesGroupChanged` (`database/element/database/DatabaseKDBX.kt:143-144`,
  `:468-496`; read and written by every program in section 2). A new database gets the group at the
  root with icon `BUILD_ID`, auto-type off, searching off, collapsed (`template/TemplateEngine.kt:62-78`),
  filled with seven default templates (`:216-226`). Turning templates on in an existing database
  **adopts the first group whose title equals the localized name** rather than creating one
  (`DatabaseKDBX.kt:472-484`).
- **What.** Each template is an ordinary entry whose definition is ordinary custom strings in the
  KPEntryTemplates format: `_etm_template` = `1`, and per field `_etm_position_<label>`,
  `_etm_title_<label>`, `_etm_type_<label>` (`Inline`, `Multiline`, `Listbox`, `Date`, `Divider`,
  with a `Protected` prefix), `_etm_options_<label>`, and the default value under the label itself
  (`template/TemplateEngineCompatible.kt:292-396`, names at `:413-430`).
- **Reference.** An entry made from a template carries a custom string `_etm_template_uuid` holding
  the template entry's UUID in hex (`TemplateEngineCompatible.kt:37-74`); the copy decoded for
  display has it removed and the copy encoded for saving has it put back (`:44-50`, `:60-74`,
  called from `DatabaseKDBX.kt:509-530`).
- **Keeping it out of the way.** Template entries are excluded from search unless the user asks
  for them (`database/search/SearchHelper.kt:83-84`, `:128-129`; default off, `SearchParameters.kt:57`)
  and so from autofill and the keyboard, which pass `searchInTemplates = false`
  (`app/.../database/helper/SearchHelper.kt:103-125`).
- **Merge.** The pointer merges by its change time (`DatabaseKDBXMerger.kt:216-219`); the template
  entries merge as any entry (section 3.1).
- **Keepass2Android uses the same format** (`Kp2aBusinessLogic/database/edit/AddTemplateEntries.cs`,
  `keepass2android-app/KpEntryTemplatedEdit.cs:56`, `:84`, `:190`; `EntryEditActivity.cs:1614` hides
  `_etm_` names) and adds two things worth copying: its template entries have **fixed, hard-coded
  UUIDs** (`AddTemplateEntries.cs:149-278`), so independently created copies are the same entries to
  a merge, and they can also be recognized by a `KP2A_TemplateId` string (`:298-312`). Deleting the
  templates group resets the pointer (`DeleteRunnable.cs:284-288`). K2A offers to add templates when
  the first entry is added ("Add templates?", seen in the spike's flows, `k2a-add.flow`) (measured
  (spike), the prompt).

What the precedent shows: a feature stored as ordinary strings in ordinary entries, in a group a
standard `Meta` field points at, survives every program in section 2 that keeps entries, and is
found by identifier rather than by name (read). What it does not show: anything about concurrent
changes, since templates rarely change (assumed).

### 4.2 Other functions

| function | K2A | DX 4.5.8 / 3.2.0 | KPD | basis |
| --- | --- | --- | --- | --- |
| search | title, user name, URL, notes, **other strings**, tags; skips entries whose group (inherited) has searching off | title, user name, URL, notes, app ids, **other strings**; skips the recycle bin, the templates group and groups with searching off | skips entries whose group has searching off | read: K2A `PwDefs.cs:271-396`, `PwGroup.Search.cs:178`, `:212`, `:232`; DX `database/search/SearchHelper.kt:115-136`, `SearchParameters.kt:31-57`, `Group.kt:399-401`; KPD `EntrySearchHandler.java:51` |
| autofill, keyboard | match by URL host or `androidapp://` URL, skipping groups with searching off | match by URL domain, app id, OTP or passkey fields, skipping groups with searching off, the bin and templates | (not read) | read: K2A `Kp2aBusinessLogic/SearchDbHelper.cs:71-149`; DX `app/.../database/helper/SearchHelper.kt:91-128` |
| OTP | fields `otp`, `TOTP Seed`, `TOTP Settings` (names configurable), KeeOtp format | fields `otp`, `TOTP Seed`/`TOTP Settings`, `HmacOtp-*`, `TimeOtp-*`, values starting `otpauth://` | (not read) | read: K2A `keepass2android-app/Totp/*.cs`; DX `database/.../otp/OtpEntryFields.kt:44-94` |
| other special names | `AndroidApp`, `KP2A_URL_*`, `_etm_*`, `KP2A_TemplateId`; a group named **`AutoOpen`** holds child-database definitions | `AndroidApp`, `AndroidApp Signature`, `KPEX_PASSKEY_*`, `_etm_*`; a group named **`Backup`** under the root counts as the recycle bin | | read: K2A `keepass2android-app/Utils/Util.cs:943`, `KeeAutoExec.cs:104`; DX `model/AppOriginEntryField.kt:29-31`, `model/PasskeyEntryFields.kt:13-24`, `DatabaseKDBX.kt:679-697` with `DatabaseKDB.kt:249` |
| recycle bin | delete moves to the bin, no time change, `LocationChanged` updated; permanent delete adds deletion records | delete moves to the bin **and touches the entry's time**; emptying adds records; with no bin set, the first group titled with the localized bin name is adopted | moves to a group named `RecycleBin`, no time change, location time updated; permanent delete adds a record | read: K2A `DeleteRunnable.cs:182-220`, `:276-317`; DX `Database.kt:1165-1258`, `DeleteNodesRunnable.kt:58`, `DatabaseKDBX.kt:703-725`; KPD `PwDatabaseV4.java:426-524` (`recycle` at `:480-492`, `deleteEntry` at `:504-508`) |
| history pruning | load, merge, edit | edit, setting change | edit | section 1.3 |
| attachment limits | in memory (assumed) | over 10 MB, or over a fifth of free memory, kept in a cache file instead of memory, nothing refused (`database/element/binary/BinaryData.kt:189`, `crypto/kdf/Limits.kt:50-61`); a 5 MiB warning when the user attaches a file (`FileTooBigDialogFragment.kt:66`) | (not read) | read |
| compression | KeePassLib's (assumed) | GZip of the whole inner stream for KDBX 4; binaries decompressed into the inner header (`DatabaseOutputKDBX.kt:100-151`) | | read |
| field references | compiled for search and URL matching (`SearchDbHelper.cs:134`) | compiled when shown or searched (`EntryKDBX.kt:171-181`) | | read; a value containing `{REF:` would be compiled (assumed) |
| duplicate UUIDs on load | (not read) | refused, unless the user turns on the fix, which gives the duplicate a new UUID (`DatabaseVersioned.kt:173-229`) | | read |
| a changed file at save | asks: synchronize, **overwrite**, or cancel (on by default); a setting merges without asking | reported only when the file's time moved on by more than 10 s; otherwise **overwritten** | **never checked: always overwritten** | read: K2A `SaveDB.cs:138-198`, `App.cs:512-513`; DX (spike) `DatabaseTaskNotificationService.kt:219`; KPD `edit/SaveDB.java:54-75`. Measured (spike): DX `keepPC` overwrote the arriving copy without a dialog |

## 5. Hazards found while reading

### 5.1 KeePassDX treats two attachments as one when 32 bits of their MD5 agree

- **Read (DX 4.5.8 and 3.2.0).** An attachment's identity is `binaryHash()`, the first four bytes
  of the MD5 of its content as an `Int` (`database/element/binary/BinaryData.kt:147-181`, MD5 at
  `:152`, `ByteBuffer.wrap(bytes).int` at `:180`; 3.2.0 `:149`, `:177`). `BinaryData.equals` compares
  the flags, the length and that integer, never the bytes (`:121-132`).
  - **At load**, each entry's attachment is put into the pool with `put(binaryData)`, which looks the
    binary up by that equality and, on a match, **puts it under the first matching key, replacing
    what that key held** (`BinaryPool.kt:69-76`, `:106-118`; called from
    `DatabaseInputKDBX.kt:806-809` through `EntryKDBX.kt:361-366`). Two entries whose attachments
    collide then both point at one body.
  - **At save**, the binaries are written deduplicated by the same integer
    (`BinaryPool.kt:160-192`, used by `DatabaseOutputKDBX.kt:131-151`, entries' references mapped
    through `AttachmentPool.kt:37-43`).
- **Measured here:** two different 48-byte bodies of equal length sharing those four bytes took
  **72,707 random tries and 0.16 s** to find (`logs\md5pair.log` in the scratch directory; the pair is
  in `out\md5pair\`). By the birthday bound, a database of n attachments of one length holds a
  colliding pair by accident with probability about n²/2³³ (assumed arithmetic): about 1% at 10,000
  attachments.
- **Consequence (assumed):** KPG records kept as attachments can have their content silently
  replaced by another attachment's after a KeePassDX load and save, and anybody who can write an
  attachment and guess a KPG attachment's content can arrange it. Not measured on a device: run R4.

### 5.2 KeePassDX merges lose attachments inside arriving history

Read (DX 4.5.8): a merged entry's **current** attachments are copied into the open copy's pool
(`DatabaseKDBXMerger.kt:475-496`, `getAttachments(pool)` with `inHistory = false`,
`EntryKDBX.kt:337-353`), but its history items keep pool indexes from the **arriving** copy
(`updateWith(..., copyHistory = true)`, `:472`), which then index the open copy's pool (assumed
consequence: wrong or missing attachments in history after a merge). Not measured.

### 5.3 KeePassDX can loop forever deleting nested groups in a merge

Read (DX 4.5.8): `firstNotDeletedParent` sets `parent = node.parent` inside its loop instead of
walking up (`DatabaseKDBXMerger.kt:342-351`). It is reached from `deleteGroup` only when the group
being deleted still has a child to move (`:358-395`): a child group, or an entry not itself
deleted. If that group's parent is also in the arriving deletion list, the loop never ends
(assumed consequence: KeePassDX hangs on "Merge data" or "Merge from"). Deletion records come in
file order, children before parents in KeePassLib (`PwGroup.cs:1424-1443`), so the usual case
deletes the leaves first and never loops (read); a child that the open copy changed after the
deletion stays and triggers it (assumed). Not measured: run R7. A KPG design that nests its own
groups inside a group that may be deleted as a tree is exposed (assumed).

### 5.4 KeePassDX undoes a move on equal times

Read: section 3.1. An entry moved in the open copy and not otherwise changed, merged with a copy
whose entry has the same modification time, goes back to the arriving copy's group
(`DatabaseKDBXMerger.kt:519-524`). A design that gives meaning to where an entry is (for example a
"retired" group) is undone by merging an older copy into KeePassDX (assumed). Not measured: run R6.

### 5.5 Overwrites lose everything, whatever the storage

K2A's "No, overwrite", KeePassDX's save over a file whose time did not move on by 10 s (measured
(spike) `keepPC`), and every KeePassDroid save over a file changed underneath it (read) write the
open copy over the other copy entirely (read, 4.2). No storage design inside the file survives
those; only a later merge from a surviving copy elsewhere could bring the records back (assumed).

### 5.6 Keepass2Android resets memory protection at every load

Read (K2A): the reader fills `MemoryProtection` from the file
(`Serialization/KdbxFile.Read.Streamed.cs:279-287`) and then, after the XML is parsed, replaces it
with defaults (`Serialization/KdbxFile.Read.cs:274-276`, in `CommonCleanUpRead`, called at `:253`
after `ReadXmlStreamed` at `:238`); the writer then writes those defaults into `Meta`
(`KdbxFile.Write.cs:503`) and protects the standard strings by them (`:874-890`). The defaults
protect the password only (`PwDefs.cs:469-475`) (read). **Why it matters beyond storage (assumed):** CLAUDE.md
says what counts as protected is KeePass's own `MemoryProtection`; if KeePass honors the file's
setting, a K2A save would make an operator's protected Notes or user name unprotected to the gate.
Whether KeePass desktop honors it was not checked here. Cheap to measure: run R8.

## 6. What a designer most needs from this

1. **Store a record as its own entry with its own UUID, never as a field added to a shared
   entry.** Entries only one side holds are always added by K2A and KeePassDX merges; fields in a
   shared entry are decided whole by the entry's time (read, sections 1.1, 1.5).
2. **Put record data in ordinary strings (custom or standard), not in entry or group custom data,
   attachments, tags or unknown elements.** Strings survive all four programs (read; measured
   (spike) for entries as wholes); entry custom data is deleted by KeePassDroid (measured (spike));
   group custom data by KeePassDX 3.2.0 and 3.3.0 and KeePassDroid (measured (spike)); attachments
   are deduplicated by 32 bits in KeePassDX (read, 5.1); unknown elements are dropped by all (read).
3. **Keep a record's entry unchanged once written**, so that its time never decides anything.
   A change made on two sides loses one side (read, 1.1); an unchanged entry has nothing to lose.
   History keeps a loser for at most ten later changes (read, 1.3).
4. **Group-level rules need a home outside the group's own fields.** Group notes, tags and custom
   data travel with the group's time, which every entry edit inside it bumps in K2A and KeePassDX
   (read, 1.4, 3.2). An entry per rule naming the group by UUID, in a KPG group, keeps the rule out
   of the group's own merge (assumed design consequence).
5. **A dedicated KPG group with searching off** is excluded from search, autofill and the keyboard
   in all three programs (read, 4.2). A "meta entry inside each user group" is not: its strings
   would be searched as "other" fields in K2A and KeePassDX (read), unless that user group's
   searching were turned off, which would hide the user's own entries (assumed).
6. **Find the KPG group by a fixed UUID** (the K2A template precedent), not by name: independent
   copies then hold the same group to a merge, and names collide with `Backup`, `AutoOpen`, the
   localized recycle bin and templates names that the programs adopt (read, 4.1, 4.2).
7. **Expect records in the recycle bin.** A deletion on Android moves the entry to the bin first;
   KeePassDX touches its time, K2A and KeePassDroid do not (read, 4.2). Fail-closed reading must decide what a
   revocation or retirement in the bin means (assumed).
8. **Deletion records do not travel through KeePassDX merges** (read, 3.1); a design must not rely on
   a deletion propagating, which is the existing rule ("a removal travels nowhere") holding for
   Android too (assumed, CLAUDE.md).

## 7. Emulator runs that would turn the reads into measurements, most valuable first

All of them reuse `spikes/revocation/android` (`run-A.sh`, `run-B.sh`, `k.py`, the pinned APKs in
`android.sha256`). Most need **no new flow**: they need new base databases and a richer reader. The
base databases are a new command in a `Kpg.cs`-style probe over KeePassLib (invented content, the
throwaway password), here called the **storage set**; the reader must print, per entry, every string
(name, protected flag, value), every attachment (name, length, SHA-256), every history item (time,
string names), the entry's group and `LocationChanged`, and per file the deletion records,
`MemoryProtection`, `HistoryMaxItems` and `HistoryMaxSize`.

The storage set, built once, every time explicit and in whole seconds:

- **group `KPG`**, fixed UUID, searching off, under the root;
- **entry `R`** in `KPG`, fixed UUID, strings `Title=R`, `KPG.base=1`, time `T0`;
- `S-left`: `R` gains `KPG.2026-03-01T10:00:00Z.left = {"side":"left"}` at time `T1`, and a new entry
  `L` in `KPG`;
- `S-right`: `R` gains `KPG.2026-03-01T11:00:00Z.right = {"side":"right"}` at time `T2 > T1`, and a
  new entry `Rn` in `KPG`;
- `S-left-eq`, `S-right-eq`: the same with `T1 = T2`;
- `S-hist`: `R` with 15 history items and `HistoryMaxItems = 10`, and an entry `Rs` whose history is
  over a `HistoryMaxSize` of 4096 bytes;
- `S-attach`: entries `A1` and `A2`, each with one attachment, `out\md5pair\a.bin` and `b.bin`;
- `S-mprot`: `ProtectNotes` and `ProtectUserName` on, an entry with notes and a user name;
- `S-del-left`: `R` deleted permanently (a deletion record) and a deletion record for a UUID that
  names nothing; `S-del-right`: `R` unchanged;
- `S-move-right`: `R` moved to a second group `KPG2` with a later `LocationChanged`, its time
  unchanged; `S-move-left`: `R` in `KPG`;
- `S-nest-left`: groups `P > S > T`, with `P` and `S` deleted (records for `P`, `S`, their entries);
  `S-nest-right`: the same tree, `T` changed after the deletion time.

The rows are in order of value; the run numbers are names only.

| run | reads it measures | programs and cells | flows (existing unless shown) |
| --- | --- | --- | --- |
| **R1** concurrent fields, different times, and two new entries | 1.1 left column, 1.3 merge column, 1.5 | K2A 1.15-r6 PC/CP; DX 4.5.8 ext and from, PC/CP; DX 3.2.0 ext PC/CP; target `S-left`, source `S-right` and the reverse | `k2a-B.flow`, `kpdx-B-ext.flow`, `kpdx-B-from.flow`, `kpdx320-B-ext.flow` |
| **R2** concurrent fields, equal times | 1.1 right column (K2A arriving wins, DX 4.5.8 open wins and moves, DX 3.2.0 nothing) | as R1 with `S-left-eq`, `S-right-eq` | as R1 |
| **R3** history limits | 1.3: K2A prunes at load and merge, DX and KPD only on edit | K2A, DX 4.5.8, DX 3.2.0, KPD, plain save of `S-hist`; K2A merge of `S-hist` with itself plus one change | `k2a-A.flow`, `kpdx-A.flow`, `kpdx320-A.flow`, `kpd-A.flow`; `k2a-B.flow` |
| **R4** attachment collision | 5.1: DX makes `A1` and `A2` one body at load and save; K2A and KPD keep both | plain save of `S-attach` in all four | the A flows; add `kpdx-attach-view.flow` below to see the load-time effect on screen |
| **R5** deletion records through a merge | 3.1: K2A merges both records into its list and drops `R`; DX drops `R` and keeps neither record; the record naming nothing survives K2A | target `S-del-right`, source `S-del-left` and the reverse, K2A and DX 4.5.8 | B flows |
| **R8** memory protection reset | 5.6 | plain save of `S-mprot` in K2A (and DX, KPD as controls) | A flows |
| **R6** equal-time move undone | 5.4 | DX 4.5.8 ext with `S-move-right` open and `S-move-left` arriving; K2A as control | `kpdx-B-ext.flow`, `k2a-B.flow` |
| **R7** nested deletion loop | 5.3 | DX 4.5.8 with `S-nest-right` open, `S-nest-left` arriving | `kpdx-B-ext-hang.flow` below |
| **R9** delete to the recycle bin | 4.2: DX touches the time, K2A does not; KPD's `RecycleBin` | each program deletes `L` from `S-left` | new delete flows, ids to be read with `list` first |
| **R10** fidelity of a rich entry in KPD 2.7 and DX 3.3.0 | section 2 rows for strings, protection, tags, history, timestamp names | plain save of a set with one of each feature | A flows |

New flows, in the style of `spikes/revocation/android/flows` (the ids marked "confirm" were not read
from source and must be checked with `list` before the first run):

```text
# kpdx-B-ext-hang.flow
# R7, KeePassDX: as kpdx-B-ext.flow, then wait up to 120 s for the group view to come back after
# "Merge data"; if it never does, record the process state and the log before failing.
include kpdx-open.flow
sleep 11
adb push ${SRC} /sdcard/Download/${NAME}.kdbx
sh touch /sdcard/Download/${NAME}.kdbx
sh ls -l --full-time /sdcard/Download/${NAME}.kdbx
key HOME
sleep 2
sh monkey -p ${PKG} -c android.intent.category.LAUNCHER 1
waitfor modified.outside 40
tap ^OK$
sleep 3
tapuntil More.options ^Merge.data$ 30
tap ^Merge.data$
log merge asked; waiting for the group view
sh dumpsys activity processes ${PKG}
sleep 60
sh dumpsys activity processes ${PKG}
sh logcat -d -t 400 *:W
waitfor add_button 60          # the group view's add button; absent after 120 s means it hung
sh ls -l --full-time /sdcard/Download/${NAME}.kdbx
list
```

```text
# kpdx-attach-view.flow
# R4, KeePassDX: open S-attach, open entry A1 and list its attachment as KeePassDX shows it, then
# the same for A2; both show the same size, so the screen alone cannot tell them apart: the save
# that follows, read back, is the measurement, and this records what the screen showed.
include kpdx-open.flow
tap ^KPG$
tap ^A1$
sleep 3
list                            # attachment row: confirm its id
key BACK
tap ^A2$
sleep 3
list
key BACK
key BACK
include kpdx-add.flow
```

```text
# kpdx-delete.flow, k2a-delete.flow, kpd-delete.flow (R9): open, go into KPG, long-press entry L,
# choose the program's delete, confirm, save where saving is separate. Each program's ids for the
# long-press menu and its confirm button are to be read with list first (confirm).
include kpdx-open.flow
tap ^KPG$
sh input swipe 540 800 540 800 1200      # long-press where L is listed (confirm the position)
list
tap ^Delete$                             # confirm the text
maybe ^(OK|Delete)$ 5
sleep 5
list
```

For every run, the measurement is the file pulled back and read with the richer reader; the
screen output is a record of what was pressed, as in the spike.

## 8. Still to measure

- Every merge row in sections 1 and 3 for strings, equal times, deletions, moves and history
  pruning (runs R1, R2, R3, R5, R6): the spike measured only entries differing in custom data at
  different times.
- The KeePassDX attachment collision on a device (R4), the merge history-attachment loss (5.2), the
  nested deletion loop (R7).
- Keepass2Android's memory protection reset (R8), and whether KeePass desktop honors the file's
  `MemoryProtection` at all, which decides whether it matters to the gate (not checked here; a
  console probe over KeePassLib from `.keepass\2.62\KeePass.exe` would answer it).
- KeePassDX 3.3.0 to 4.5.7 in a merge (the spike's list), and Keepass2Android's synchronization
  through its cache (`SynchronizeCachedDatabase.cs:90`, read by the spike).
- KeePassDroid's search and autofill (not read here), and Keepass2Android's attachment write path,
  which was assumed to be KeePassLib's content-equal pool.
