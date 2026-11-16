# Ground report: the other programs a database meets, and how others keep their own data

Key: `others`. Written 2026-11-15 against KeePassGate master `a42e38aa`. Two surveys: the programs a
`.kdbx` meets beyond KeePassXC and the Android programs, and the precedents set by other plugins and
programs that keep their own data inside a `.kdbx`. Every statement about another program carries
its basis:

- **[M]** measured here: I ran it, over databases I made with invented content, and read the result
  back with KeePassLib 2.62 (`.keepass\2.62\KeePass.exe`, loaded as a library in
  Windows PowerShell 5.1, no window). Raw output is in the appendix.
- **[M, spike]** measured by `spikes/revocation-merge`, as its `RESULTS.md` records it.
- **[R]** read from source at the commit or tag named, with file and line, or from an issue or
  document named.
- **[A]** assumed: reasoned or remembered, not checked here.

## 1. What a designer most needs from this report

1. **No merge in any program surveyed merges an entry field by field. [M] for KeePass 2.62 and
   kdbxweb 2.1.1, [R] for Strongbox, KeePassKit (MacPass) and keepass-rs, [M, spike] for KeePassXC,
   Keepass2Android and KeePassDX.** When both copies changed the same entry, the newer entry is taken
   whole, and the other copy's version survives only as a history item, if at all. A custom string
   added in the older copy is gone from the current entry after the merge.
2. **Two different entries added to the same group, one in each copy, both arrive in every merge
   surveyed. [M] KeePass 2.62 and kdbxweb, [R] Strongbox, KeePassKit, keepass-rs.** Entries are the
   unit that merges additively. Fields inside an entry are not. **A whole-file synchronizer keeps
   only one of the two files** and renames or sets aside the other ([R] Syncthing, Dropbox docs).
3. **A tie loses data in three implementations.** When both copies carry the same entry modification
   time and different content: KeePass 2.62 takes the arriving copy's entry and puts the open copy's
   version nowhere [M]; kdbxweb keeps the open copy's and puts the arriving copy's nowhere [M];
   KeePassKit does the same as kdbxweb [R]. Strongbox [R] and keepass-rs [R] file the other version
   in history. The file stores whole seconds [M, spike].
4. **History is not a durable store.** Strongbox trims every entry's history to the database's
   limits at every save [R]; kdbxweb trims by count at every merge and propagates a trimmed history
   item as a deletion to the next copy it merges [R]; keepass-rs ships `kp-purge-history`, which
   removes all history [R]; KeePassium and KeePassKit trim an entry's history when that entry is
   edited [R]; the default limits written are 10 items and 6 MiB per entry [M].
5. **kdbxweb (KeeWeb) makes a KDBX 4.1 file with timestamped database custom data unreadable to
   KeePass. [M]** It writes the custom data item's `LastModificationTime` as ISO text where KDBX 4
   needs Base64; KeePass 2.62 fails to open all seven files kdbxweb 2.1.1 saved or merged from such
   a database. Reported upstream since 2022 (KeeWeb #2001, kdbxweb #49, #50, #62, all open) [R].
   KeePassGate's database is exactly such a file today.
6. **kdbxweb's merge never takes the other copy's entry or group custom data. [M]** The newer
   copy's fields and time arrive, its custom data does not: the stale rule ends up under the newer
   time, which is the KeePassDX hazard the spike measured, by a second route. kdbxweb also drops any
   custom data item whose value is empty, on every save [M].
7. **Unknown XML is not a store.** KeePass 2.62 drops unknown elements in Meta, a group and an entry
   on save [M]; kdbxweb drops them [M]; KeePassKit, Strongbox, keepass-rs and gokeepasslib drop
   them [R]; **KeePassium refuses to open the file at all** [R]. Only pykeepass keeps them [M].
8. **Strings and attachments on entries are what every program keeps.** pykeepass and kdbxweb kept
   every custom string, its protection flag, the attachment, the tags and the history on a save [M];
   KeePassium, Strongbox, KeePassKit and gokeepasslib carry all of these in their models [R]. The
   precedents that have travelled best across programs are string fields on entries and a group
   named by UUID in the database's metadata (KPEntryTemplates and the templates of KeePassDX and
   Keepass2Android, OTP fields, passkey fields) [R]. The precedents that broke are custom data,
   group custom data most of all (KeeShare in KeePassDX, KeePassXC's own browser keys) [R].
9. **Deletions are not recorded by every program.** pykeepass removes an entry or a group without a
   `DeletedObjects` record [R], so any merge with an older copy brings it back [A]. kdbxweb, by
   contrast, deletes anything listed in `DeletedObjects` whatever its later modification time [R].
10. **Some programs edit without moving the modification time.** pykeepass changes a field without
    touching `LastModificationTime` unless the caller asks [R], which every time-based merge then
    reads as no change, or as a tie [A].

## 2. Method and versions

Nothing with a window was started. Sources were shallow clones into their own directories under
the scratch directory; downloads went into their own empty directories.

| Subject | Version read or run | How |
| --- | --- | --- |
| KeePassium | `e651df4`, "version bump to 2.5.173", 2026-05-23 | [R] sparse clone of `KeePassiumLib/db/` and the conflict alert |
| Strongbox | `c70fc7b`, "1.65.0", 2026-07-17 | [R] sparse clone of `model/` |
| MacPass's library, KeePassKit | `b5cf50e`, 4 commits past tag 3.3.2, 2026-09-18 | [R] |
| kdbxweb (KeeWeb's library) | npm 2.1.1 (latest on npm, 2021-09-06) run; source at 2.1.1 and at HEAD `7601e45`, the format files identical | [M] Node script, [R] |
| KeeWeb | master `package.json` (1.18.9, `kdbxweb ^2.0.4`), last release v1.18.7 of 2021-07-18 | [R] |
| pykeepass | 4.2.0 from PyPI, run with `uv run --no-project --with pykeepass`; source at `aec2566` | [M], [R] |
| keepass-rs | tag v0.17.0, 2026-10-20 | [R], no Rust toolchain on this machine |
| gokeepasslib | tag v3.7.1, 2026-10-23 | [R], no Go toolchain on this machine |
| KeePass 2.62 | `.keepass\2.62` of the review4 worktree | [M] as the yardstick, through KeePassLib |
| KeePassXC | tag 2.7.12 | [R] for the precedents only |
| KeePassDX | tag 4.5.8 (`77ecfa8`) | [R] templates only |
| Keepass2Android | master `a9c5114`, 2026-11-10 | [R] templates only |
| KeeAgent, KeePassRPC, KPEntryTemplates, KeeTheme | `431b32a` (v0.13.8+1), `dc0a59b` (v2.0.2+1), `4e435a3`, `b42b8f4` (v1.0.3+1) | [R] |

**The databases.** `base` has a group G with custom data `KPG=grule-base`, an entry E in it with
strings `Base`, `Secret` (protected), a protected password, an attachment, a tag, entry custom data
`KPG=rule-base` and one history item, and database custom data `KPG.Key` and `KPG.Empty` (an empty
value). From it: **A** adds string `Note-A` to E at +10 s and a new entry EA in G; **B** adds `Note-B`
to E at +20 s and a new entry EB; **Btie** is B with E at +10 s, the same second as A; **Bcd** changes
only E's custom data to `rule-B` at +20 s; **Bg** changes only G's custom data to `grule-B` and G's
time to +20 s. Each derived copy first pushes E's previous state into history, as an edit would. Two
sets: `db`, where the database custom data carries per-key times (so KDBX 4.1), and `db40`, without
them (KDBX 4.0). AES-KDF, one round. A merge "X+Y" is Y merged into an open X.

Scripts: `spikes\storage\probes\ground-others\scripts\Kp.cs` and `kp.ps1` (KeePassLib), `kw.js` (kdbxweb),
`pk.py` (pykeepass); logs under `spikes\storage\probes\ground-others\logs\`, where `...` is
`<scratch>`.

## 3. Survey 1: the programs, load and save

`yes` means the item comes back after the program opens and saves the file.

| Program | strings, protection flags | attachments | tags | history | DeletedObjects | entry custom data | group custom data | database custom data and its times | unknown XML |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| KeePassium 2.5.173 | yes; standard fields' flags reset from the database's memory protection, custom ones kept; a field with empty name and empty value dropped [R] | yes [R] | yes [R] | yes [R] | yes [R] | yes, item times kept [R] | yes [R] | yes, with times [R] | **file refused** [R] |
| Strongbox 1.65.0 | yes [R] | yes [R] | yes [R] | yes, trimmed at every save [R] | yes [R] | yes [R] | yes [R] | yes, with times [R] | dropped [R] |
| KeePassKit (MacPass) 3.3.2+ | yes [R] | yes [R] | yes [R] | yes [R] | yes [R] | yes, no item times [R] | yes, no item times [R] | yes, with times [R] | dropped [R] |
| kdbxweb 2.1.1 (KeeWeb) | **yes [M]** | **yes [M]** | **yes [M]** | **yes [M]** | yes [R] | **yes [M]** | **yes [M]** | values yes, **an empty value dropped [M]**; in 4.1 **times written as text, file unreadable to KeePass [M]** | **dropped [M]** |
| pykeepass 4.2.0 | **yes [M]** | **yes [M]** | **yes [M]** | **yes [M]** | yes, but its own deletions add none [R] | **yes [M]** | **yes [M]** | **yes, with times, empty value kept [M]** | **kept [M]** |
| keepass-rs 0.17.0 | yes [R] | yes [R] | yes [R] | yes [R] | yes [R] | yes, item times modeled [R] | yes [R] | yes [R] | dropped [A, serde default] |
| gokeepasslib 3.7.1 | yes [R] | yes [R] | yes [R] | yes [R] | yes [R] | yes [R] | yes from 3.7.0 only [R] | yes, times from 3.7.0 [R] | dropped [R] |
| KeePass 2.62 (yardstick) | yes [M] | yes [M] | yes [M] | yes [M] | yes [A] | yes [M] | yes [M] | yes [M] | **dropped [M]** |
| whole-file synchronizers | the file as written | | | | | | | | |

### KeePassium

- **No merge.** On a conflict when saving, the options offered are overwrite remote, save as and
  cancel; a `merge` case exists in the enum and is not offered
  (`KeePassium/database/merger/SyncConflictAlert.swift:24-41`) [R]. The project's own issue #438,
  "Add database merging (content-level sync)", opened 2025-03-26, is open [R].
- **A file with an element KeePassium does not know is refused.** Each streaming parser breaks on an
  unknown start tag and throws `unexpectedTag` on its end tag: Meta
  (`KeePassiumLib/KeePassiumLib/db/kp2/Meta2.swift:540-543`), group (`Group2.swift:518-521`), entry
  (`Entry2.swift:634-637`); the DOM parser throws the same way (`Meta2.swift:389-391`), and `load`
  turns that into `FormatError.parsingError` (`Database2.swift:1335-1372`) [R]. Not run, since
  KeePassium is an iOS and macOS program.
- A field whose name and value are both empty is dropped on load (`Entry2.swift:583-594`,
  `EntryField2.swift:13-15`) [R]. The five standard fields take their protection flag from the
  database's memory protection settings on every save (`Entry2.swift:505-511`,
  `EntryField2.swift:28-40`) [R]; custom fields keep theirs [A, the `switch` has no case for them].
- Custom data items keep their time and write it whenever present (`CustomData2.swift:130-149`) [R];
  the file keeps the format version it was read with (`Database2.swift:1491`) [R].
- History: an entry's history is trimmed to `HistoryMaxItems` and `HistoryMaxSize` when that entry
  is backed up before an edit (`Entry2.swift:174-200`, called at `:253`), defaults 10 and 6 MiB
  (`Meta2.swift:14-15`) [R].
- Issue #482, "Database corruption", open since 2025-11-24: after an AutoFill save, "Some entries
  refer to non-existent binaries", files shrinking [R, the issue's text]. Cause unknown.

### Strongbox

- **It merges**: on a sync conflict or on request (`model/Sync/ConflictResolutionStrategy.h` has
  ask, auto merge, force push local, force pull remote) [R]. `DatabaseMerger.m` at 1.65.0 [R]:
  - an entry in both copies: the newer by modification time, strictly later, replaces the open
    copy's entry whole, fields, custom fields, tags, attachments and custom data
    (`DatabaseMerger.m:304-333`, `NodeFields.m:491-528`); the older is added to history unless an
    equal version is already there; at a tie the open copy's stays current and the arriving copy's
    current state is filed in history (`:316-322`, then `:330`);
  - histories are combined in a dictionary keyed by modification time (`:335-365`), so two
    different history items with the same time collapse into one, the open copy's;
  - a group in both copies: the newer replaces the open copy's properties and custom data whole,
    and the older goes nowhere, groups having no history (`:294-302`);
  - a node only in the arriving copy is added (`:284-292`); deletions are combined from both copies'
    `DeletedObjects` (`:235-239`);
  - database custom data: an arriving key is taken when both have times and the arriving one is
    later, when only it has a time, or, with no times, when the open copy lacks the key or the
    arriving copy's `SettingsChanged` is later (`:196-216`). The same rule as KeePass's, never
    removing a key.
- **History is trimmed on every save** to the database's `HistoryMaxItems` and `HistoryMaxSize`
  (`DatabaseModel.m:1951-2020`, called from `Serializator.m:221` before serializing and from
  `DatabaseMerger.m:116` after a merge) [R].
- Unknown XML is kept as "unmanaged children" of the parsed objects
  (`model/keepass/BaseXmlDomainObjectHandler.m:86-120`), but a save builds a new document from
  Strongbox's own node model (`KeePassXmlModelAdaptor.m:92-98`), which does not carry them [R].
  So unknown XML is dropped on save [R], except possibly inside `DeletedObjects`, which writes its
  own (`DeletedObjects.m:44-58`) [R].
- Strongbox's own data: pinned entries and other per-database settings are kept outside the file
  (issue #282, "Store Strongbox database properties in KDBX", open since 2020) [R]. What it does
  write is in section 6.

### MacPass and KeePassKit

- **It synchronizes** with KeePass's modes (`KeePassKit/Core/KPKTree+Synchronization.m:45-79`) [R]:
  - an entry in both: in Synchronize and OverwriteIfNewer only a strictly newer arriving entry
    replaces attributes, attachments, custom data, tags and times whole
    (`KPKNode.m:402-415`, `KPKEntry.m:1004-1038`); the older is pushed to history without
    maintenance (`KPKTree+Synchronization.m:126-135`); histories are combined keyed by
    modification time (`:188-228`);
  - **at a tie neither side is pushed to history and nothing is updated**, so the arriving copy's
    different content is lost (`:126-141`, both comparisons false) [R]; not run;
  - a node only in the arriving copy is created (`:93-104`); deletions are merged by time
    (`:230-300`);
  - database custom data: a key the open copy lacks is added, then by time, then by settings
    (`KPKMetaData.m:518-545`) [R].
- Node custom data is a plain string dictionary with no item times (`KPKXmlTreeReader.m:478-490`);
  the database's carries times (`:457-476`) [R]. Unknown elements are not read, since the reader
  asks the DOM for known names (`KPKXmlTreeReader.m:122-153`), and the writer builds a new tree [R].

### KeeWeb and kdbxweb

KeeWeb's last release is v1.18.7 of 2021-07-18, and its master depends on `kdbxweb ^2.0.4` [R]; the
format code that matters here is identical at 2.0.4, 2.1.1 and HEAD [R]. KeeWeb merges on its own:
`app/scripts/models/file-model.js` `mergeOrUpdate` calls `db.merge(remoteDb)` when syncing with its
storage, and keeps kdbxweb's local edit state per file [R]. So the kdbxweb results below are what a
KeeWeb sync does [A, KeeWeb itself not run].

Measured with kdbxweb 2.1.1 [M]:

- **Resave of the 4.0 set**: every string with its protection flag, the attachment, the tag, both
  history items, the entry's and the group's custom data came back. `KPG.Empty`, the empty value,
  did not: `kdbx-custom-data.ts:30` writes an item only `if (item?.value)` [R].
- **Resave or merge of the 4.1 set**: kdbxweb read the file, and **KeePass 2.62 cannot open any of
  the seven files it wrote** ("The input is not a valid Base-64 string", thrown from
  `KdbxFile.ReadTime`). The document kdbxweb builds for a binary save holds
  `<LastModificationTime>2026-01-01T10:00:00Z</LastModificationTime>` inside the database's custom
  data item, beside node times in Base64 [M]; `kdbx-custom-data.ts:34-38` calls `XmlUtils.setDate`
  without the binary flag [R]. pykeepass opens the same file [M], and kdbxweb PR #62 says current
  KeePassXC accepts it [R, the PR's text]. Reported: KeeWeb #2001 (2022-03-23), kdbxweb #49
  (2023-03-02), PRs #50 (2023-03-03) and #62 (2026-05-08), all open [R].
- **Unknown elements** in Meta, G and E were dropped on save [M]; `Kdbx.buildXml` builds a new
  document (`kdbx.ts:606-618`) [R].

Read from source [R]:

- `Kdbx.merge` (`kdbx.ts:404-436`): `DeletedObjects` combined, then metadata, then the tree, then
  `cleanup({ historyRules: true, ... })`, which removes history beyond `HistoryMaxItems`, by count
  only (`kdbx.ts:332-365`).
- An entry (`kdbx-entry.ts:369-391`): the newer replaces the open copy through `copyFrom`, the
  open copy's state pushed to history first; if the open copy is newer and the arriving state is
  not in its history by time, it is added; **at equal times nothing happens**. `copyFrom`
  (`:336-366`) copies fields, attachments, tags, times and auto-type, **and not custom data**.
- History (`:416-459`) is a "remove-win OR-set": a history item the open copy holds, the arriving
  copy lacks, and that is older than the arriving entry is treated as deleted remotely and dropped
  (`:447-455`), unless the open copy added it in this session (`_editState`).
- A group (`kdbx-group.ts:170-198`): a newer arriving group replaces properties through `copyFrom`
  (`:290-302`), which copies no custom data. A child listed in `DeletedObjects` is removed whatever
  its modification time (`:213-216`, `:227`).
- Database custom data (`kdbx-meta.ts:604-623`): an arriving key is taken only when both carry a
  time and the arriving one is later, or when the open copy lacks the key and it is not listed as
  deleted. With no times, the open copy's value always stays.

### pykeepass

pykeepass 4.2.0 keeps the decrypted XML as an `lxml` tree and writes that tree back [R], and that
shows [M]: a save of the 4.1 set returned every item, the empty custom data value, the per-key times
and the 4.1 format; three unknown elements added in Meta, G and E were in the file it wrote, and
**KeePass 2.62 opened that file and dropped all three on its save** [M].

- **No merge** [R].
- **A deletion leaves no record**: `delete` removes the element from its parent
  (`pykeepass/baseelement.py:162-163`), and the string `DeletedObjects` appears nowhere in the
  package [R].
- **An edit moves no time**: `_set_string_field` (`entry.py:95-`) does not touch
  `LastModificationTime`; `touch(modify=True)` (`baseelement.py:179-189`) does, and nothing in the
  package calls it [R]. History is added only when the caller calls `save_history`
  (`entry.py:414-427`), and nothing trims it [R].
- Programs built on it: GNOME Secrets (formerly Password Safe) and passhole are built on pykeepass
  [A]; not read.

### keepass-rs and the programs on it

- **Saving and merging are opt-in features**: `save_kdbx4` and `_merge`, the leading underscore
  marking it unstable (`Cargo.toml:25-31`) [R].
- `Database::merge` merges custom icons and the tree, and **not the metadata, so not the database's
  custom data** (`src/db/merge.rs:78-85`) [R].
- An entry in both: a newer source replaces fields, tags, custom data, icon, colors, quality check,
  and **not attachments**: "TODO: attachments and custom_icons_id" (`merge.rs:557-584`); a newer or
  equal destination stays and the source's version is filed in history, so **a tie keeps both
  versions** (`merge.rs:526-537`, `:585-614`); histories are combined by time, keeping both items
  when two with the same time differ (`:624-700`) [R].
- A group in both: the newer replaces name, notes, tags, icon and custom data whole
  (`merge.rs:326-350`); at a tie the destination's stays (`:323-333`) [R].
- `src/bin/kp-purge-history.rs` removes every entry's history and saves [R].
- Reverse dependencies on crates.io: 23 crates, among them `keepass-diff`, `diff-kdbx`, `rkeep`,
  `kdbx`, `secretspec` [R, crates.io API, 2026-11-15]; which of them save or merge was not read.

### gokeepasslib and the programs on it

- **No merge** [R]. Unknown elements are skipped (`entry.go:151-153`, "Unknown elements are
  ignored") [R].
- **Before v3.7.0 (2026-07-28) a group had no custom data, tags or previous parent in its model, and
  custom data no times** (`CHANGELOG.md:61-68`) [R], so a save by a program built on an older
  release drops every group's custom data [A, follows from the struct having no field for it].
  Earlier releases could also corrupt protected values when entries and groups alternate inside a
  group (`CHANGELOG.md:24-37`, fixed in 3.7.1) [R].
- 79 known importing modules on pkg.go.dev, among them `chezmoi`, `mostfunkyduck/kp`, `lockbox`,
  `kpmenu`, `keepass-fuse`, passbolt's import, and a `keepass-merge` [R, pkg.go.dev, 2026-11-15];
  not read.

### Whole-file synchronizers

- **Syncthing**: a conflict is two devices changing the same file; "The file with the older
  modification time will be marked as the conflicting file and thus be renamed" to
  `<filename>.sync-conflict-<date>-<time>-<modifiedBy>.<ext>`, which then syncs like any file; no
  merge [R, docs.syncthing.net/users/syncing.html].
- **Dropbox**: a conflicted copy is named with the editor and the date, "The last version saved will
  always appear as the conflicted copy", and the advice is to merge manually [R,
  help.dropbox.com/organize/conflicted-copy].
- **OneDrive**: the copy is renamed with the computer's name, `MyFile-ComputerName.txt` [R, third
  party knowledge bases only, not Microsoft's own page].
- KeePassium issue #519 (2026) shows the result in the field: a Dropbox conflicted copy beside the
  database after KeePassXC and KeePassium both saved [R].
- So: **nothing merges; one file wins, the other survives as a separate file until a person merges
  it into the database, in a program that merges, or deletes it** [A]. Two entries added in two
  copies do not both arrive. Whatever merge the person then runs is one of those in section 4.

## 4. The question every ground report answers

**Same entry in both copies, each copy adding a different custom string, then a merge.** The
yardstick is KeePass 2.62's Synchronize, measured here because the timestamp-named-field option turns on it;
it is outside this report's subject and the KeePass report should be read for it.

| Program | Result on the current entry | The other copy's field | Same modification time | Basis |
| --- | --- | --- | --- | --- |
| KeePass 2.62 Synchronize | newer entry whole | in history, as the older version | **the arriving copy wins and the open copy's version is in no history**, both directions | [M] |
| kdbxweb 2.1.1, KeeWeb | newer entry's fields whole; **custom data stays the open copy's** | in history, **without its custom data** | **the open copy wins and the arriving version is in no history**, both directions | [M] |
| Strongbox 1.65.0 | newer entry whole | in history unless already there | open copy current, arriving in history | [R] |
| KeePassKit 3.3.2+ (MacPass) | newer entry whole | in history | **open copy kept, arriving lost** | [R] |
| keepass-rs 0.17.0 | newer entry whole, except attachments | in history | open copy current, arriving in history | [R] |
| KeePassXC 2.4.1 to 2.8.0 | "An entry takes the newer entry whole", older E1 in history, 216 runs | in history | not measured | [M, spike] |
| Keepass2Android | E1 came out P's, "with one older version in its history" | in history | not measured | [M, spike] |
| KeePassDX | an entry's rule replaced by the arriving copy's whatever the times | not measured | not measured | [M, spike] |
| KeePassium, pykeepass, gokeepasslib | no merge | | | [R] |
| Syncthing, Dropbox, OneDrive | one file whole | in a conflict copy only | Syncthing picks by device ID | [R] |

**Changed a different field rather than adding one** is the same answer: no surveyed merge compares
fields, only whole entries by modification time [M for KeePass and kdbxweb, R for the rest].

**A different entry added to the same group in each copy.** Both arrived in every KeePass and
kdbxweb merge run, in both directions and at a tie [M]; Strongbox adds a node the open copy lacks
(`DatabaseMerger.m:284-292`) [R]; KeePassKit creates it (`KPKTree+Synchronization.m:91-104`) [R];
keepass-rs adds it [R, `merge.rs:377-436`]. Two exceptions: kdbxweb removes a
node whose UUID is in either copy's `DeletedObjects` regardless of time [R], and pykeepass records
no deletion at all [R].

**What is in the history afterwards, and for how long.**

- The older copy's version, as one history item, in KeePass, kdbxweb, Strongbox, KeePassKit and
  keepass-rs, as in the table [M, R]. kdbxweb's history item has no custom data [M].
- Limits written by KeePassLib's defaults and carried by every writer measured: `HistoryMaxItems`
  10, `HistoryMaxSize` 6291456 bytes, `MaintenanceHistoryDays` 365 [M, in kdbxweb's output of the
  base file]; gokeepasslib and KeePassium use the same defaults (`meta_data.go:49-50`,
  `Meta2.swift:14-15`) [R].
- When they are applied: Strongbox on every save and after every merge [R]; kdbxweb after every
  merge, by count only, and its trimmed items then travel as deletions [R]; KeePassium and KeePassKit
  when that entry is edited [R]; KeePass when an entry is edited [A], and on the operator's
  Database Maintenance by age [A, KeePass feature request #2920 describes it]; pykeepass and
  gokeepasslib never [R]; keepass-rs only through `kp-purge-history`, which removes all of it [R].
  An entry that receives ten or more later edits loses the item anyway [A, the count limit].
- Programs that drop history entirely on save: none measured or read among this report's subjects;
  KeePassDroid's handling was not read.

**When times are equal, dropped or rewritten.** An entry's own modification time is in every format
version and no program measured dropped it [M, spike, and M here for kdbxweb and pykeepass]. What
moves it is the program: pykeepass does not on an edit [R], so its edit is either lost to a newer
change in the other copy or decided as a tie [A]. KeePassDX gives the root group the save's time
[M, spike]. Ties lose data as the table shows; a tie is likely whenever two edits fall in the same
second, since the file stores whole seconds [M, spike].

## 5. Survey 2: how others keep their own data in a `.kdbx`

| Precedent | Feature used | Known breakage | Lesson |
| --- | --- | --- | --- |
| **KeeAgent** | per-entry settings as an attachment `KeeAgent.settings`; database settings in database custom data `KeeAgent.Settings` (`KeeAgent/ExtensionMethods.cs:35-39`, `:70-96`, `:104-150`) [R] | the attachment is UTF-16 without a byte order mark, which strict XML parsers refuse (KeeAgent #433, 2025) [R]; KeePassXC writes and reads the same attachment and once added it where not needed (KeePassXC #4020, 2019; #4594, 2020) [R]; no report found of the attachment being lost [R, searched] | an attachment travels with its entry everywhere measured [M for kdbxweb, pykeepass]; it merges as part of the entry version, and a format a second implementation must read needs a specification |
| **KeePassHttp, then KeePassXC-Browser** | first a string attribute `KeePassHttp Settings` or `KeePassXC-Browser Settings` on each entry, and keys as strings on an entry named after the plugin; then entry custom data `KeePassXC-Browser Settings` and database custom data `KPXC_BROWSER_<name>`, converted by KeePassXC (`src/browser/BrowserService.cpp:57`, `:66-67`, `:1125-1160`, `:1735-1745` at 2.7.12) [R] | **KeePassXC #2991 (2019): "Database Custom Data is not merged causing configuration data loss"**, browsers disconnected after syncing through Google Drive [R]; the fix merged database custom data, and KeePassXC now never removes its own `KPXC_BROWSER_*`, `_CREATED_` and `FDO_SECRETS_EXPOSED_GROUP` keys in a merge (`src/core/CustomData.cpp:23-29`, `:190-193`) while removing every other key the source lacks [R, M, spike]; **KeePassXC #9371 (2023): Secret Service group no longer exposed after a merge** [R] | database custom data has failed even its owner across merges, and the owner protects only its own keys; a third party's keys have no such protection in KeePassXC |
| **KeeShare** (KeePassXC) | group custom data `KeeShare/Reference` (`src/keeshare/KeeShare.cpp:28`) [R] | **KeePassDX #1335 (2022): "whenever a KeePass database is edited by KeePassDX, the KeeShare settings are overwritten and gets lost"**, fixed in 3.4.5 [R]; the spike measured KeePassDX 3.2.0 and 3.3.0 deleting every group's custom data and KeePassDroid every node's [M, spike] | group custom data is the least supported place in the format |
| **KPEntryTemplates, and the templates of KeePassDX and Keepass2Android** | a templates group named by UUID in the metadata (`EntryTemplatesGroup`, with `EntryTemplatesGroupChanged`); template definitions as string fields `_etm_template`, `_etm_title_<f>`, `_etm_type_<f>`, `_etm_position_<f>`, `_etm_options_<f>` on template entries; `_etm_template_uuid` on an entry made from one (`KPEntryTemplates/EntryTemplateManager.cs:37-100`; KeePassDX `TemplateEngineCompatible.kt:405-430`; Keepass2Android `AddTemplateEntries.cs:77-123`, `:336-350`, `:371`, `KpEntryTemplatedEdit.cs:51-84`) [R] | none found; not searched in depth | three independent implementations of one convention made only of strings, entries and a UUID in the metadata; Keepass2Android creates the group when the UUID names none (`AddTemplateEntries.cs:340-346`) [R] |
| **KeePassRPC and Kee** | v1: a protected string `KPRPC JSON` on every entry; v2 (2024): entry custom data `KPRPC JSON`, database custom data `KeePassRPC.Config`, requiring KDBX 4 (`KeePassRPC/Extensions.cs:18-26`, `:127-136`, `:150-180`) [R] | the move was planned in 2018 and waited for ports to support KDBX 4 (KeePassRPC #24) [R]; v2 keeps the old string "as a useful backup" [R, #24]; #143 (2024) records that some ports could not take KDBX 4 [R]; #148 (2024): opening and closing an entry wrote the new data and asked to save every entry [R] | the string field was the version that worked everywhere; moving to custom data was a bet on ports, hedged by keeping the string |
| **OTP plugins** | string fields: KeePass 2.47+ `TimeOtp-Secret-Base32`, `TimeOtp-Period` and others; KeeTrayTOTP `TOTP Seed`, `TOTP Settings`; KeePassXC and KeePassium `otp` (KeePassXC `src/core/Totp.h:70-78` at 2.7.12, reading all three; KeePassium `db/EntryField.swift:20`) [R] | readers disagree about formats, not about the data surviving [A] | strings on an entry survive every program; the cost is a convention others must learn |
| **Passkeys** | string fields `KPEX_PASSKEY_*` on the entry, shared by KeePassXC, Strongbox and KeePassium (Strongbox `StrongBox/Constants.m:66-73`, with a migration for KeePassXC's `KPXC_PASSKEY_USERNAME`; KeePassium `db/passkey/EntryField+passkey.swift:10-16`) [R] | Strongbox migrates an older spelling on every save (`DatabaseModel.m:1961-1969`) [R] | the newest cross-program convention chose strings, not custom data |
| **KeeTheme** | **nothing in the database**: KeePass's own configuration file, `CustomConfig` (`KeeTheme/Options/KeeThemeOptions.cs:26-84`) [R] | none | correction to the premise: a per-machine setting is not database data |
| **KeePass's recycle bin and templates group** | a group named by UUID in the metadata, with a change time: `RecycleBinUUID` and `RecycleBinChanged`, `EntryTemplatesGroup` and `EntryTemplatesGroupChanged` [R, every writer read here carries them] | merges take the later change time and fall back to the other side's UUID, or none, when the group named does not exist (Strongbox `DatabaseMerger.m:151-193`; KeePassKit `KPKMetaData.m:468-517`) [R]; kdbxweb's `KdbxMeta.merge` compares the times too (`kdbx-meta.ts:554-600`) [R] | the pattern "a UUID in the metadata naming an ordinary group" is understood by every merge read here; the group itself merges like any group |
| **Strongbox's markers** | entry custom data `KPEX_DoNotSuggestForAutoFill` (`Constants.m:64`); audit exclusion shared with KeePassXC's custom data flag (Strongbox #391, 2020) [R] | per-database settings kept outside the file (#282) [R] | |
| **KeePassium's markers** | smart groups: an empty group whose notes' first line is a search query (`KeePassiumLib/db/Group.swift:39-48`); `otp:*` marks an OTP group [R] | none found | encodes a feature in ordinary fields every program keeps, at the cost of looking like an empty group with notes elsewhere |
| **AutoOpen** | a group named `AutoOpen` whose entries point at other databases [A, KeePass trigger convention]; KeePassDX #915, "Add support for AutoOpen / child database", open since 2021 [R] | | a convention by name is portable data and unportable behavior |

## 6. What this leaves the timestamp-named-field proposal

The proposal: JSON in an entry field whose name is derived from a timestamp, in a meta entry per
group or one global meta entry, with a preprocessing step at load. What the surveys say about each
part, marked as before:

- **A field added in one copy does not survive a merge with a copy that edited the same entry
  later**, in any merge surveyed [M, R]. It survives as a history item in KeePass, kdbxweb,
  Strongbox, KeePassKit and keepass-rs [M, R], so a preprocessing step that also reads the meta
  entry's history would find it [A]. That step would depend on history that Strongbox trims at
  every save, kdbxweb trims at every merge and deletes on the next merge, a tie can lose in KeePass,
  kdbxweb and KeePassKit, and ten later edits push out [M, R, A as marked in section 4].
- **One entry per record, not one field per record, is what merges additively** [M, R]: two
  entries added in two copies both arrived in every merge surveyed, both directions, ties included.
  An entry that is added once and never changed is, at the entry level, what `KPG.Revoked.<key>`
  is at the custom data level [A, by analogy with the spike's design C].
- **Strings and attachments are kept by every program surveyed** [M, R], including KeePassDroid
  and KeePassDX 3.2 and 3.3, which delete node custom data [M, spike: they kept every database
  custom data key; that they keep entry strings is A, being their core function].
- **A group's own data cannot ride on the group** [M, spike, R]: KeeShare's loss in KeePassDX is
  the precedent. A record that names the group by UUID, in an entry elsewhere, follows the
  templates precedent [R] and never touches the group [A].
- **Removal needs care everywhere**: pykeepass records no deletion [R], so a removed record can come
  back from any older copy [A]; kdbxweb deletes by `DeletedObjects` whatever the times [R]; KeePass
  decides by times [A].
- **Whatever is written must not be timestamped database custom data in a 4.1 file that KeeWeb will
  save** [M], and must not be empty-valued custom data [M].
- Whole-file synchronizers merge nothing [R]; every guarantee above holds only once somebody merges
  the conflict copy in [A].

## 7. Still to measure

- KeePassium, Strongbox and MacPass on a real device or desktop: a save of the KPG layout, and
  Strongbox's and MacPass's merges of the A, B, Btie and Bcd sets. All three need a window or
  another platform.
- keepass-rs's merge (feature `_merge`) and save (`save_kdbx4`) over the same sets; needs a Rust
  toolchain. Its untouched attachments and unmerged metadata deserve a run.
- gokeepasslib before 3.7.0, saving a file with group custom data; needs Go.
- KeeWeb itself (desktop or web build) rather than its library, including whether its sync merge is
  the one measured here.
- KeePassXC and the Android programs over the A, B, Btie sets, especially the tie, and KeePassXC's
  reading of kdbxweb's text times.
- KeePass 2.62's tie rule read from source (measured only), and its history trimming on merge.
- A chain: kdbxweb merge, then KeePass synchronize of the result, for a stale rule under a newer
  time.
- How many history items survive N edits plus a Strongbox save, for a meta entry of realistic size.
- pykeepass deleting an entry, then a KeePass synchronize with an older copy.

## Appendix: measured output

KeePassLib's dump of each file: `kdbx` version, database custom data with each key's time, G's time,
custom data and entries, E's time, strings (`[p]` protected), custom data, tags, attachment count,
and each history item.

**The 4.0 inputs (A, B, Btie, Bcd as described in section 2).**

```text
A.kdbx:  E @10:00:10Z strings Base=b,Note-A=from A,Password[p]=p,Secret[p]=s customdata KPG=rule-base  tags t1 binaries 1
         history @09:00:00Z, @10:00:00Z; G entries E,EA
B.kdbx:  E @10:00:20Z strings Base=b,Note-B=from B,... customdata KPG=rule-base; G entries E,EB
Btie:    E @10:00:10Z strings ...,Note-B=from Btie,...; G entries E,EB
Bcd:     E @10:00:20Z no new string, customdata KPG=rule-B; G entries E
Bg:      G @10:00:20Z customdata KPG=grule-B; E unchanged
```

**kdbxweb 2.1.1, 4.0 set** (`logs\kdbxweb-db40.txt`):

```text
A-resaved.kdbx: kdbx 4.0, db customdata KPG.Key=v-base@none;   (KPG.Empty gone)
  E @10:00:10Z strings Base=b,Note-A=from A,Password[p]=p,Secret[p]=s customdata KPG=rule-base; tags t1 binaries 1
A+B.kdbx:   G entries E,EB,EA
  E @10:00:20Z strings ...,Note-B=from B,... customdata KPG=rule-base
    history @09:00:00Z, @10:00:00Z, @10:00:10Z strings ...,Note-A=from A,... customdata (none)
B+A.kdbx:   G entries E,EA,EB; E @10:00:20Z Note-B; history adds @10:00:10Z Note-A, customdata (none)
A+Btie.kdbx: G entries E,EB,EA; E @10:00:10Z Note-A; history @09:00:00Z, @10:00:00Z only
Btie+A.kdbx: G entries E,EA,EB; E @10:00:10Z Note-B; history @09:00:00Z, @10:00:00Z only
A+Bcd.kdbx: E @10:00:20Z customdata KPG=rule-base   (Bcd's rule-B lost, Bcd's time taken)
Bcd+A.kdbx: E @10:00:20Z customdata KPG=rule-B
kdbxweb-A+Bg.kdbx: G @10:00:20Z customdata KPG=grule-base   (Bg's rule lost, Bg's time taken)
kdbxweb-Bg+A.kdbx: G @10:00:20Z customdata KPG=grule-B
```

**kdbxweb 2.1.1, 4.1 set** (`logs\kdbxweb-db41.txt`): all of `A-resaved`, `A+B`, `B+A`, `A+Btie`,
`Btie+A`, `A+Bcd`, `Bcd+A`: "cannot be read by KeePassLib: FormatException: The input is not a valid
Base-64 string", at `KeePassLib.Serialization.KdbxFile.ReadTime`. The document kdbxweb built:

```text
<CustomData><Item><Key>KPG.Key</Key><Value>v-base</Value><LastModificationTime>2026-01-01T10:00:00Z</LastModificationTime></Item></CustomData>
<Times><CreationTime>cbuL4g4AAAA=</CreationTime><LastModificationTime>cbuL4g4AAAA=</LastModificationTime>...
```

**KeePass 2.62 Synchronize, 4.1 set** (`logs\keepass-db41.txt`), the yardstick:

```text
A+B.kdbx:    G entries E,EB,EA; E @10:00:20Z Note-B; history @09:00, @10:00, @10:00:10Z Note-A (custom data kept)
B+A.kdbx:    G entries E,EA,EB; E @10:00:20Z Note-B; history adds @10:00:10Z Note-A
A+Btie.kdbx: G entries E,EB,EA; E @10:00:10Z Note-B (the arriving copy); history @09:00, @10:00 only
Btie+A.kdbx: G entries E,EA,EB; E @10:00:10Z Note-A (the arriving copy); history @09:00, @10:00 only
A+Bcd.kdbx:  E @10:00:20Z customdata KPG=rule-B; history adds @10:00:10Z Note-A
Bcd+A.kdbx:  E @10:00:20Z customdata KPG=rule-B; history adds @10:00:10Z Note-A
keepass-A+Bg, keepass-Bg+A: G @10:00:20Z customdata KPG=grule-B
database custom data KPG.Empty and KPG.Key kept with their times in every result
```

**pykeepass 4.2.0:**

```text
pykeepass 4.2.0 resaved version (4, 1)
A-resaved.kdbx: kdbx 4.1, db customdata KPG.Empty=@2026-01-01T10:00:00Z; KPG.Key=v-base@2026-01-01T10:00:00Z
  E strings Base=b,Note-A=from A,Password[p]=p,Secret[p]=s customdata KPG=rule-base; tags t1 binaries 1; two history items
A-unknown.kdbx unknown elements: 3            (written by pykeepass)
A-unknown-keepass.kdbx unknown elements: 0    (the same file opened and saved by KeePass 2.62)
A40-unknown-kdbxweb.kdbx unknown elements: 0  (a 4.0 copy with them, saved by kdbxweb)
```
