# What the storage probes measured

Where the KeePassGate plugin could keep its data inside a `.kdbx`, and what each program that
saves, edits, synchronizes or merges such a file does to each place. The requirement every option
was held to: the data has to come through the file being saved, edited, synchronized and merged
by every program listed as compatible. A program is compatible when measurement shows all three
of: its save keeps every place the store uses; it merges, or asks, before it writes over a file
that changed underneath it; and its merge keeps what KeePass's Synchronize keeps, applied per place
the store relies on. The programs measured besides KeePass with the plugin are KeePassXC on a
desktop and Keepass2Android, KeePassDroid and KeePassDX on a phone. This document states what was
measured of each against the three; which programs meet them, and under which conditions, is
`EVALUATION.md` section 5.

This document states what was measured, once, with where each measurement can be run again. It
holds no recommendation and no ranking. The working papers under [`papers`](papers/README.md) are
kept as they were written; where a paper and this document differ, this document holds, and
section 6 lists every sentence of a paper that came out wider than the output it quotes.

| Question | Answer |
| --- | --- |
| Does any program merge an entry field by field? | **No** for its strings and attachments: every program that merges takes them from a version of the entry whole, and the other version is a history item or nowhere. Entry custom data goes apart in two programs: a kdbxweb merge keeps the open copy's under the arriving copy's newer version, and a KeePassDX merge puts the arriving copy's under whichever version is newer (earlier spike, and the harness of section 2.7) |
| What decides between two versions of an entry? | The modification time, which a file stores in whole seconds (read), wherever a merge compares times; KeePass's Overwrite existing and Keep existing take the arriving or the open version whatever the times. At equal times KeePass's Synchronize and Keepass2Android's merge at a save take the arriving copy's version, KeePassXC, KeePassDX and kdbxweb keep the open copy's, and the other version is kept nowhere, while Overwrite existing keeps the open copy's version as a history item where the history holds no item at that time; Keepass2Android's synchronization of a remote file kept the phone's version with the other as a history item. In KeePass's window a Synchronize at a save kept the window's edit of the same second, the edit's time in memory holding a fraction of the second (section 3.1) |
| Does an entry written once and never changed come through? | Through every save that was run, and it arrived in every merge cell that was run for it. A deletion record that a merge applies removes it, and so does Delete Duplicate Entries when a copy under another identifier exists and it is the older of the two or sits in the recycle bin |
| Does the database's custom data come through a KeePassXC merge? | Not when the two loads of the merge fall in different seconds: from 2.4.2 the copy loaded later, if it holds any key, replaces the whole dictionary. Within the same second nothing is merged |
| Does entry and group custom data come through? | Not through KeePassDroid, which deletes both at every save, and a group's not through KeePassDX 3.2.0 and 3.3.0 (earlier spike). A KeePassDX merge puts the arriving copy's whichever is newer (earlier spike), a kdbxweb merge keeps the open copy's under the arriving copy's newer time, and KeePassXC never merges a group's |
| Do deletion records travel? | Through KeePass's Synchronize, Keepass2Android's merge and kdbxweb. KeePassXC never applies or copies a record, in `keepassxc-cli` and in its window's Merge From Database; its window's merge after a reload keeps the records of the file on disk. KeePassDX applies an arriving record and keeps none, so a later merge with an older copy brings the deleted entries back (section 2.3) |
| Does a program merge or ask before it writes over a file that changed underneath it? | KeePass 2.62 asks at every save through its save command, and Save As over the same path writes without asking; KeePassXC 2.8.0 reloads or asks in every sequence run; Keepass2Android 1.15-r6 asks at every save and synchronization, or merges without asking once "Always merge on conflict" is on; KeePassDX 4.5.8 writes over the file, in 22 runs of 22 with a change to lose; KeePassDroid 2.7 in 3 of 3 (section 3.1) |
| What does no store inside the file come through? | A whole file written over a newer file, which KeePassDX and KeePassDroid do at every save over a changed file, Keepass2Android on the answer "No, overwrite", KeePass on "Overwrite" and on Save As over the same path, and KeePassXC on "Overwrite" and after "Ignore"; and a node's place decided by a merge (section 3) |
| Is there a place in the file, besides entries, that every merge unites? | No: of 117 places of a KDBX 4.1 file, none was united by every merge measured, and custom icons come nearest (section 2.6) |
| Is there an option against which no measured sequence ends wider or refusing? | No, as the options are written, and not the store composed from them either, nor the store as decided. Section 4 has each sequence, sections 4.13 to 4.15 the composed store and two of its parts, section 4.16 the store as decided, sections 4.17 and 4.18 readings of it, and section 7.1 the changes to an option that were run |
| What does the store as decided keep, with nothing written back after a merge and an edited record read from a version in its history that passes its check? | On real files no cell ended wider than with the plugin writing back after each Synchronize, and none newly refused; a file made by a holder of the store's key and synchronized in ends wider in both files. Edited by KeePass's own operations, no kind of store entry ended wider in 590 cells; an entry reads as decided through 10 edits under KeePass's default history limit, and the 11th, or the first in a database that keeps no history, stops the whole database, or for the two entries that carry the store's key leaves the file with no key (section 4.16) |
| What changes when the plugin writes an edited record again at its next own write, a reading the design took? | With a single gate and with two, no cell ended wider than the store as decided, and no record written again undid a later decision of the other gate: 500 reads after a write and its merges, 324 merge cells of two gates writing the same record, and 120 cells of a rewrite against the other gate's later decision, each as in the control that writes nothing again. The stop moves from the 11th edit since the record was written to the 11th edit since the plugin last wrote; a database that keeps no history still loses the written version at the first edit (section 4.17) |
| Can a record that takes access away count from a copy under another identifier? | Measured in five readings, each in two variants, and the design took none: it reads only the store's own records. The cells such a reading closes are imports with Create new IDs into a stale copy, which keeps the state it had before the import; the import fails to bring it up to date, which Synchronize does. No reading of section 4.18 gave a file more than its own records give, in 21,399 answers. `cap`, the reading that limits most, closes all 16 such cells, 2 of them ending narrower than decided, and leaves a limit that can outlast the decision behind it (sections 4.17 and 4.18) |

## 1. What the answer rests on, and how to read it

| What | Version |
| --- | --- |
| the product | commit `a42e38aa` |
| KeePass | 2.62, as KeePassLib inside `KeePass.exe` with SHA-256 `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`; 2.47 in the probes that name it |
| KeePassXC | 2.4.2, 2.6.6, 2.7.12 and 2.8.0, through `keepassxc-cli`, and 2.8.0 in its window as well; 2.4.1 and 2.7.0 in the earlier spike's record only |
| Keepass2Android | 1.15-r6, version code 254; 1.08d-r5 in the earlier spike's record only |
| KeePassDroid | 2.7, version code 223 |
| KeePassDX | 4.5.8 libre, version code 45800, still the newest release on 2026-11-16; 3.2.0 and 3.3.0 in the earlier spike's record only |
| kdbxweb, pykeepass | 2.1.1 and 4.2.0 |
| the phone programs' device | an emulator of Android 11, API 30, x86_64 |
| the windows | KeePass 2.62 and KeePassXC 2.8.0 on a Windows desktop, driven through UI automation, the keyboard and the mouse (`desktop-windows`, `desktop-hide-and-lock`) |
| the phone programs' own code | Keepass2Android at tag `v1.15-r6` and KeePassDX at tag `4.5.8`, compiled for a desktop runtime (`source-harness`) |

- **Every figure names the family whose `RUN.md` has it.** A family is a directory under
  [`spikes/storage/probes`](spikes/storage/probes/README.md), named in backticks, as
  `ground-keepass`; the models are `spikes\storage\model` and the phone measurement is
  `spikes\storage\android`. Each `RUN.md` gives the command that runs a probe from the branch
  root, the recorded value of every number and what a second run gave.
- **Unmarked means measured**: a file a program wrote, read back, in a probe on this branch. For the
  later phone measurements of sections 2.3 and 3.1, it is a file the program left on the emulator or
  on the server, read back as their reports describe; for the windows of sections 2.1, 2.2, 2.8 and
  3.1, a file KeePass or KeePassXC left, or a window read through UI automation and pictured.
- **Read** means read from a program's source at the release named, with nothing run.
- **Harness** means a phone program's own source at the release named, compiled for a desktop
  runtime with stand-ins for what touches the device, and driven as the program's screens drive it
  (section 2.7). It shows what the program's code does to a file; when the program reads, writes or
  asks is the device's alone, and the emulator stays the measurement of record for that.
- **Earlier spike** means recorded on the branch `spikes/revocation-merge`, in its `RESULTS.md`
  and outputs, for KeePass 2.61.1 and the releases the table names. Nothing here runs those probes
  again; `ground-measured` reads their result files a second time.
- **Model** means the output of an executable model, which runs no program but itself.
- **A cell** is a merge under a single set of conditions: a program or release, a method, a
  direction, and the time condition, key derivation or variant its family names. In the first
  phone measurement a merge cell was run three times.
- **Two families open windows**, `desktop-windows` and `desktop-hide-and-lock`, each with the
  desktop to itself. Everywhere else every KeePass merge is KeePassLib's `PwDatabase.MergeIn`, which
  File then Synchronize and File then Import reach, and every KeePassXC run is `keepassxc-cli`.
  Section 7 lists what that leaves unmeasured.
- **Every database is invented.** A probe makes its databases at every run and keeps none.

## 2. What each program does to each carrier

The carriers are the places in a `.kdbx` where data could live: the database's custom data, a
group's or an entry's custom data, an entry's strings, attachments, tags and notes, its history,
the deletion records, a node's place in the tree, and entries and groups as such.

### 2.1 KeePass 2.62

`ground-keepass`, thirteen experiments X1 to X13 over KeePassLib, every time set in whole seconds.

**An entry changed in both copies** goes by the entry's modification time, or under Overwrite
existing and Keep existing by the method alone, and never by its content. Seven kinds of change (a
custom string, a protected string, the same field set to two values, entry custom data, an
attachment, a tag, JSON in the notes) gave identical outcomes in 120 of 120 cells: 6 time
conditions, 5 methods, 2 directions, with and without a backup of the older version (X2).

| Method | The two times differ | The two times are equal |
| --- | --- | --- |
| Synchronize, Overwrite if newer | the strictly newer version is current, whole; the other version is a history item, except where the history already holds an item at that version's time | the arriving copy's version is current; the open copy's change is kept nowhere |
| Overwrite existing | the arriving version is current whatever the times; the open copy's goes to history, except where the history already holds an item at that time | the same |
| Keep existing | the open copy's version stays; a newer arriving version is lost | the open copy's version stays |
| Create new IDs | two entries, each holding its own copy's change and history | the same |

A modification time rewritten forward makes stale content win. An entry with no modification
time reads as the moment of reading (X12).

| Carrier | A save | A merge | Experiment of `ground-keepass` |
| --- | --- | --- | --- |
| Database custom data | kept with its per-key times; a per-key time makes the file KDBX 4.1 (`outside-file`) | per key, by the key's time, visiting the arriving copy's keys only; no method removes a key (earlier spike, 2.61.1: a key only ever added survived in 70 of 70 cells). On 2.62: two keys written once kept in 38 of 40 cells, the other 2 Keep existing into a copy that lacks them (`outside-file`); 14 of 14 keys under Synchronize in 6 of 6 cells (`hardened-custom-data`) | |
| Group custom data, notes, tags | kept | with the group, whole: from the strictly newer copy, and at equal times from the arriving copy; Overwrite existing takes the arriving group, Keep existing keeps the open group. `IsExpanded` is never merged | X6 |
| Entry custom data, strings plain and protected, attachments, tags, notes | kept, but a standard string's protection flag follows the database's memory protection, which a load resets (read): "protect user name" was lost by an open and save, 42 lines of the record (`spikes\storage\android`) | with the entry, whole, as the table above | X2 |
| History | trimmed to the file's limits, by default 10 items and 6,291,456 bytes: an entry saved holding 9 items under a limit of 3 reads back with 3, the oldest item going first | the open copy's limit applies and is not carried: 3 items into a copy with limit 3, 9 into a copy with limit 10. Two items with the same time collapse into the open copy's item, and under Overwrite existing into the arriving copy's. With a 1 MiB attachment, 10 synchronized edits leave 5 items | X5 |
| Deletion records | a record that names nothing survives a save and a reopen | only Synchronize carries a record to the other copy. The open copy's own records apply under every method, to arriving objects too. A record is dropped when its object is present and newer, or is a group that is not empty | X3, X4 |
| A node's place | kept | Synchronize carries a move, and of two moves of a node the later move wins; Overwrite if newer, Overwrite existing and Keep existing leave the node where the open copy had it (`grant-only`, `rules-central`) | |
| Entries as such | kept | two different entries added to the same group, in a copy each, both arrive under all five methods in both directions | X1 |
| Groups as such | kept | Create new IDs makes the arriving root an empty group and adds the arriving top-level groups again, so every group and entry is present twice; a deleted entry comes back as a copy under a new identifier | X1, X3, X4 |

**Two commands behind KeePass's menus**, called through KeePassLib (X10). Delete Empty Groups
deleted 4 groups, a group holding only notes and custom data among them. Delete Duplicate Entries
deletes an entry that equals another in its strings, whatever their custom data or tags and across
two groups; it deletes the older of the two, and of two where the newer sits in the recycle bin,
that entry; two entries that differ in a field, or only in an attachment's name, are not
duplicates.

**The format a feature needs** (X7). KeePass 2.62 wrote KDBX 4.0 for every entry feature run but
the exclusion from reports, for entry and group custom data, for deletion records and for a
group's notes and flags, and 4.1 for a group tag, an entry kept out of reports and a custom icon
with a name or a time. A previous parent with no other 4.1 feature is written as 4.0 and dropped.

**Timestamp-named fields in a shared entry** (X13). Two copies each add a field to the same
entry in every round, twelve rounds, 24 fields, then synchronize; "preprocessing" copies fields
back out of history between the merge and the save.

| Preprocessing | The two writes of a round | Fields left in the entry or its history |
| --- | --- | --- |
| none | 5 seconds apart | 22 of 24, 12 of them in the entry |
| none | the same second | 12 of 24 |
| after each merge | 5 seconds apart | 24 of 24, all in the entry |
| after each merge | the same second | 12 of 24 |
| after each merge, `HistoryMaxItems` 0 | four rounds | 4 of 8 |
| after each merge, `HistoryMaxItems` 1 | four rounds | 8 of 8 |

**In KeePass's own window**, on a store of write-once entries in a group of the plugin's own, each
command given as a person gives it, the file saved and read back (`desktop-windows`, measurement 4,
a counted run per row):

| Command | What KeePass showed | What it did |
| --- | --- | --- |
| Database Maintenance, "Delete history entries older than (days)" at 365 | the dialog; no question | a user's history item 533 days old deleted, items of 107 and 6 days kept |
| Database Maintenance, "Deleted objects information", Delete | a warning on the page that deleted objects may reappear at a synchronize; no question | **every deletion record removed**, 6 of 6, records naming no entry among them |
| Database Maintenance, Plugin Data, the last key of the database's custom data deleted | the list of keys | the key gone, and with its per-key time the file's only 4.1 feature: the file written as KDBX 4.0, the previous parents of the three entries in the recycle bin dropped |
| Delete Duplicate Entries | "3 object(s) deleted." | of each pair of store entries with equal strings the older deleted, or the entry in the recycle bin where that was the newer; a deletion record for each |
| Delete Empty Groups | "2 object(s) deleted." | the store's empty subgroup deleted with an empty user group; the plugin's group, holding entries, kept |
| Delete on the plugin's group | a question naming the group | the group, every entry and its subgroup in the recycle bin; no record |
| Shift+Delete on the plugin's group | a question | the group and everything in it gone, 9 records |
| Empty Recycle Bin | a question naming the items | the store entries in the bin gone, a record each; their copies outside it kept |
| Duplicate Group of the plugin's group | "Duplication Options" | a second group of the same name and flags under a new identifier, every entry copied under a new identifier with " - Copy" appended to its title |
| Sort Direct Subgroups, Move Group One Up, Move Entry One Down, a drag onto another group | nothing | the order or the place changed; every string kept |

**File, Import** of another copy, KeePass KDBX (2.x), preselected Create new IDs in 5 runs of 5 and
saved nothing by itself. Counted in memory by a logging plugin at the next event, Create new IDs
held every entry of the other copy a second time; Keep existing, Overwrite existing and Overwrite if
newer took over no deletion record of the other copy; only "Overwrite if newer and apply deletions"
gave the synchronize's result (`desktop-windows`, measurement 5). **File, Synchronize, Synchronize
with File** offers no method: it merged, saved the open file and copied the result over the other
file with no question (3 runs). XML Replace was not run; read, it removes or changes any node of the
XML that a person's XPath selects, a `KPG.` string, a store entry and a deletion record alike, and
merges the result back by Synchronize.

**The entry dialog takes its copy of the entry before it is shown and writes the copy back at OK**
(read, and run in 10 cases with a probe plugin writing to the live entry while its dialog is open,
`desktop-windows`, measurement 3). A string the plugin added or changed while the dialog was open
was gone after OK with nothing touched, the entry's modification time back to what it was and the
database not marked changed, so the undone state reaches the file at the next save of anything;
after OK with a change it survived only in the history item OK made; Cancel kept it. A string
already on the entry when the dialog opened came through every close of the dialog.

**What a person sees and does** (16 runs): the entry view shows a custom string by name and value, a
protected value masked; the Advanced tab edits and deletes it with no question, each change adding a
history item; quick search finds a user's entry by a custom string's value, protected or not, and
never listed an entry of a group whose searching is off; Duplicate Entry and Copy Entry carry the
string to an entry under a new identifier; print lists the plugin's group and its entries' titles;
global auto-type matched no entry of a group whose auto-type is off. The XML export and the detailed
XSL transformation write every string in clear, protected values included, and the KDBX 3.1 export
drops entry custom data.

**Read, not run**: that a group keeps no history; that the Database Maintenance dialog writes a copy
of the database's custom data back on Close when the copy's key count differs from the live
dictionary's (the earlier spike models that on KeePassLib's types).

### 2.2 KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0

`ground-keepassxc`. A merge claim of the first set was checked in 16 cells: four releases, into
either copy, under a fast and a slow key derivation; a claim of the second set (group flags and
custom data, the exclusion from reports, timestamp-named fields) in 8, under the fast derivation.
The slow derivation gave exactly the fast derivation's result in every cell of the first set (8
of 8), whose claims are about entries, groups and deletion records.

| Carrier | A save (`keepassxc-cli add`) | A merge (`keepassxc-cli merge -s`) |
| --- | --- | --- |
| Database custom data | keys and values kept; the per-key times dropped and the file written as 4.0, except from 2.7.0 in a file that holds another 4.1 feature (earlier spike: all 35 saves keep every key, 30 of them KeePassXC's and 5 KeePass 2.47's). Every release from 2.4.2 adds `_LAST_MODIFIED` to a dictionary that holds a key; 2.7.12 and 2.8.0 add it and `KPXC_RANDOM_SLUG` to an empty dictionary too (`outside-file`, `store-entries`). 2.8.0 saving a 4.1 file puts a time on every key (`hardened-custom-data`) | from 2.4.2, when the two loads fall in different seconds, the copy loaded later replaces the whole dictionary: all 96 slow runs of the earlier spike, and on this branch 8 of 8 (`hardened-custom-data`) and 8 of 8 where the later copy holds a key (`outside-file`). A later copy whose custom data is empty replaces nothing (8 of 8, `outside-file`). When both loads fall in the same second nothing is merged: all 96 fast runs of the earlier spike, 4 of 4 here (`hardened-custom-data`). 2.4.1 merges none (earlier spike, 24 runs) |
| Group custom data | kept; `_LAST_MODIFIED` added | never merged: the target keeps its own, and the group takes the newer modification time (8 of 8; earlier spike 216 of 216) |
| Group notes, tags, flags | notes and flags kept; tags dropped by 2.4.2 and 2.6.6 | the newer group's notes. Tags and flags are never merged, and the group takes the newer time: into the stale copy no tag, under the newer copy's time |
| Entry custom data | kept; `_LAST_MODIFIED` added to the entry's and to each history item's | with the entry |
| Entry strings, plain and protected | kept with their flags; the standard fields an entry lacks are added empty | with the entry: the newer entry whole, the other copy's field only in a history item (16 of 16); at equal times the target's version stays and the source's is nowhere, with nothing printed (16 of 16) |
| Attachments | kept; every attachment flagged protected | with the entry |
| Entry tags | verbatim in 2.4.2 and 2.6.6; rewritten as a sorted list without duplicates, joined by commas, in 2.7.12 and 2.8.0 | with the entry |
| History | never trimmed at a load or a plain save; an edit trims | every version of both copies kept where it fits; the loser is not kept when the winner holds 10 later items. Where the arriving copy's version wins, an entry with ten items that gains another holds 10 after 2.4.2 and 2.6.6 and 11 after 2.7.12 and 2.8.0, which KeePassLib trims to 10 when it next opens the file; where the open copy's wins, 10 in every release |
| Deletion records | kept, with the recycle bin | never applied and never copied: everything a copy deleted comes back into that copy beside its records (16 of 16 in each of four scenarios), and the other copy never receives a record (16 of 16) |
| A node's place | kept; the previous parent dropped by 2.4.2 and 2.6.6 | a move arrives. When the other copy edited the moved entry, the result holds that copy's content in the new group with that copy's older location time; into the moving copy with no previous parent, into the other copy with the previous parent from 2.7.12 |
| Entries and groups as such | kept | an entry or a group only one copy holds arrives (16 of 16); no merge removes an entry or a group the target holds (16 of 16). The source's recycle bin arrives as an ordinary group |

A plain save also drops unknown XML elements at 8 positions in every release, keeps the exclusion
from reports in 2.7.12 and 2.8.0 and loses it in 2.4.2 and 2.6.6, and writes 4.0 in 2.4.2 and
2.6.6 and 4.1 in 2.7.12 and 2.8.0 for a file that holds a 4.1 feature. `edit -u` puts the state
before the edit into history; the history item it makes has no entry custom data up to 2.7.12 and
has it in 2.8.0. The CSV export holds ten columns and no custom string; the HTML export prints
every custom string, protected strings in clear; the XML export holds everything, unencrypted.

**What a later KeePass Synchronize makes of a KeePassXC result** (1 of 1 each). The entries
KeePassXC brought back are deleted again by the deleting copy's records, but for the entry the other
copy changed after the deletion, which stays as after a Synchronize of the two copies alone. A group
whose time KeePassXC raised without copying its tag makes the stale tag win. The location time that
regressed on a moved and edited entry comes through. A tie that KeePassXC left split resolves to the
arriving copy's version, with no trace of the other.

**KeePassXC 2.8.0's window** (`desktop-windows`, measurement 2), on the two copies of section 3.1,
merging the other writer's later save that replaced the file underneath it. Section 3.1 has when the
window merges or asks; what its merges kept, item by item:

| Item | "Reload database", Merge (10 runs) | Database, Merge From Database (3 runs) | `keepassxc-cli merge -s` of the same two files (3 runs) |
| --- | --- | --- | --- |
| a write-once `KPG.` entry of the other writer | kept | kept | kept |
| a key of the database's custom data only the other writer's file holds | kept, its per-key time dropped, the file KDBX 4.0 | kept: the other file, loaded seconds later, replaced the dictionary | gone: both loads in the same second, nothing merged |
| a deletion record naming no entry, the other writer's | kept: it is the file's own | gone: the merge carries none | gone |
| an entry the other writer deleted with its record | back, beside its record | back, no record | back, no record |
| a password both changed in the same second | the file's version current, the window's change kept nowhere | the window's version, the other kept nowhere | the window's |
| passwords changed by one side, or by both at different times; new entries | as KeePassLib's Synchronize keeps them | the same | the same |

The window's merge and the command's differed in the database's custom data only. At the same second
the copy merged into wins, so in "Reload database", where the file on disk is merged into, the
person's own change is the change lost. No `KPG.` entry of either copy was lost in any of the 13
merge runs. Its search found no custom string by its value, and its global auto-type listed no entry
of a group whose auto-type is off (1 run).

### 2.3 Keepass2Android 1.15-r6, KeePassDroid 2.7 and KeePassDX 4.5.8

`spikes\storage\android`, recorded 2026-11-15 on the emulator and read back with KeePassLib 2.62;
the emulator runs were not made again (section 5). Counted runs: Keepass2Android 15, KeePassDroid
8, KeePassDX 15. Saves and operations are a single run each; each merge cell is three runs, which
read back alike. "Kept" is the read-back line equal to the base's line.

| Carrier | Keepass2Android | KeePassDroid | KeePassDX |
| --- | --- | --- | --- |
| Operations run | save of four bases; edit of a user's entry; title edit of two entries shaped like the plugin's; group rename; recycle, then delete in the bin; move of an entry and of a group; the merge its save offers | save of four bases; entry edit; the same title edits; recycle, then delete in the bin. It offers no group edit, no move and no merge | save of four bases; title edits; group rename; delete with the bin emptied; the same moves; "Merge from" |
| Database custom data | values kept; the per-key time dropped by every save and merge | kept with its per-key time | kept with its per-key time |
| Entry and group custom data | kept | **deleted at every save**, the nodes' times unmoved | kept |
| Entry strings, plain and protected, with their flags; a value of 2,861 characters; names built from a time | kept | kept | kept |
| Attachments, two entries holding the same bytes among them | kept | kept | kept |
| Entry tags; a group's notes and tags | kept | kept | kept |
| History | kept; an edit adds an item holding the whole earlier version, custom data included | kept; every editor Save adds an item, with nothing changed too, and without custom data | kept; an edit adds an item, custom data included |
| Deletion records | kept; a recycle writes none, a delete inside the bin writes a record | the same | kept; emptying the bin writes a record |
| A node's place | a move keeps everything; no previous parent recorded | no move | a move keeps everything, records the previous parent and moves the entry's modification time |
| Times of nodes not edited | a rename moves the group's and the root's modification times | an entry edit moves every parent group's modification time | every edit moves the parents' modification times and rewrites the edited node's expiry time |
| `SettingsChanged` | kept in a 4.x file | not written: the file reads back as changed at the moment of reading | kept |
| Format | 4.1 kept; an AES-KDF file with no group tag and no node custom data comes back as KDBX 3.1, every carrier it held kept | as given | as given |

**The merges.** The pair C and P are the main base changed in two ways; CP is C open and P
arriving.

| Cell | Keepass2Android, CP and PC | KeePassDX, CP and PC |
| --- | --- | --- |
| the same entry, a field added in each copy, different times | the newer version whole; the other version whole in a history item | the same |
| the same, equal times | the arriving copy's version; the other field nowhere | the open copy's version; the other field nowhere |
| a new entry in each copy | both present | both present |
| deleted in P, unchanged in C | gone, the record kept, both directions | gone. With the deletion arriving (CP), **no record is kept**; with P open, P's record stays |
| deleted in C, changed later in P | present with P's change; C's record removed from the list | present with P's change; with C open its record stays beside the entry |
| an attachment or a tag added in a copy; a group's notes changed in a copy | present | present |
| moved in P | in the new group in both directions, the previous parent kept | **the arriving copy's place**: in the new group with P arriving (previous parent dropped, location time not moved), back in the old group with P open |

The earlier spike adds, for the database's and the nodes' custom data: a Keepass2Android merge
keeps a key only one copy holds (18 of 18 results) and, for a key both hold, the open copy's value
unless the arriving copy's `SettingsChanged` is later, which left a stale value in 9 of 18 runs; a
KeePassDX merge takes the database's keys by their own times (18 of 18) and puts the arriving
copy's group or entry custom data whichever is newer (6 of 6 runs); KeePassDX 3.2.0 and 3.3.0
delete a group's custom data at a save.

**Back into KeePass**, KeePassLib 2.62 Synchronize in both directions, run again from the files the
phones saved: 20 labels, 200 files, as recorded. The files the first measurement did not
synchronize, the saves under AES-KDF, Keepass2Android's rename, delete and moves and KeePassDX's
moves, were synchronized later from the same files, in both directions
(`papers\ground\measured-keepassdx.md` section 9, headless, no emulator run): nothing either program
wrote was lost.

- **Keepass2Android**: the result is the phone's file exactly, the per-key time restored from
  KeePass's copy, after its save, its two edits, each merge, and, synchronized later, its rename,
  delete and moves. The entry merged at equal times takes the arriving copy's version again, so its
  version flips with the direction. Its save of an AES-KDF file with no custom data, written as KDBX
  3.1 with no `SettingsChanged`, came back as KDBX 4.0 with `SettingsChanged` the moment of the
  synchronize.
- **KeePassDroid**: every string, attachment, tag, note, history item and deletion record comes
  back whole. The node custom data it deleted comes back only where KeePassDroid did not move that
  node's modification time and only with the phone's file open; after an edit inside a group,
  that group's and the edited entry's custom data are gone from the result in both directions,
  the entry's surviving only in a history item and only with KeePass's copy open.
- **KeePassDX**: after its saves, edits, rename and delete the result is the phone's file exactly;
  after its move, with one history item more on the moved entry, KeePass's copy's version, since
  KeePassDX's move gives the entry a new modification time.
  After its merge of P into an open C, a Synchronize with C brings back, in both directions, the
  entry KeePassDX removed without a record, and with C open puts the moved entry back into the old
  group. After its merge of C into an open P, a Synchronize with P open puts the moved entry into
  the new group again; with the phone's file open the result is the phone's merge result.

**Keepass2Android's own synchronization of a remote file.** `spikes\storage\android` material
recorded 2026-11-16, reported in `papers\ground\measured-k2a-synchronize.md` with its evidence under
`recorded\k2a-synchronize\`: Keepass2Android 1.15-r6 opening a file on a WebDAV server through its
own storage access, which keeps a cached copy, in 21 counted runs; the server's file and the cache
were read back with KeePassLib 2.62. The desktop's later save adds a write-once entry to a group of
the plugin's own, a key to the database's custom data and a deletion record that names no entry,
deletes an entry the phone leaves alone, and changes passwords; the phone changes passwords and adds
an entry.

| The desktop's item, in the server's file afterwards | Merged at a synchronize, "Yes, merge", 4 runs | Merged at a save, 4 runs: "Yes, merge" in 3, "Always merge on conflict" in 1 | "No, overwrite", 3 runs | Closed and opened again, 2 runs |
| --- | --- | --- | --- | --- |
| a write-once entry in the plugin's group | kept | kept | **gone** | kept |
| a key of the database's custom data that the older file lacks | kept, its per-key time dropped | kept | **gone** | kept |
| a deletion record that names no entry | kept | kept | **gone** | kept |
| an entry deleted with its record | deleted, the record kept | the same | **the entry back, no record** | deleted |
| a password only the desktop changed | kept | kept | **gone** | kept |
| a password both changed, the desktop's the older | a history item under the phone's | not a case of the save runs | **gone** | kept |
| a password both changed, the desktop's the newer | current, the phone's a history item | not a case of the save runs | **gone** | kept |
| a password both changed at equal times | a history item under the phone's | not a case of the save runs | **gone** | kept |

In the save runs the phone edited a single password, which the desktop had left alone, so no
password was changed on both sides there; the desktop's values for the three passwords of those rows
were kept in the merged file, as passwords only the desktop changed. The phone's own password and
its new entry were in the server's file after every run that made them, and the two write-once
entries both copies held were in every file of every run.

- **What "Synchronize database..." does.** With nothing changed on the phone it reads the
  server's file into what the program holds and writes nothing (3 runs, in 2 of which the server
  had changed and the cache kept the older file until the next save). With nothing changed on the
  server it uploads the cache (3 runs). With the server reachable a save goes straight to the
  server (1 run). With both changed it asks "Merge changes?", as a save does, and the answer
  decides (section 3.1).
- **Held against KeePass 2.62's own Synchronize** of the same two copies, run through KeePassLib
  in both directions: Keepass2Android's merge held everything KeePass's held. It differed in three
  things: at equal times KeePass keeps the arriving copy's version whole and the other nowhere,
  where Keepass2Android kept the phone's version current and the desktop's as a history item, in
  every merge run in which both had changed the entry; the file is KDBX 4.0 with the per-key
  times of the database's custom data dropped; and the usage count.
- **Over six alternating changes** (`spikes\storage\android` material, `RUN.md` section 16, reported
  in `papers\ground\measured-k2a-background.md` with its evidence under `recorded\k2a-background\`;
  1 run, "Always merge on conflict" on, the server stopped for the phone's last two turns and
  reachable again afterwards): the phone's saves while the server was away went to the cache with no
  question, message or notification; no synchronization started in the 60 s after the server came
  back; "Synchronize database..." then merged with no question. The server's final file held
  everything KeePass's Synchronize of the same two copies holds; at the equal second it kept the
  phone's version with the desktop's as a history item, where KeePass keeps one side; and it held
  one more history item, a copy of the phone's own current version. KDBX 4.0, the per-key time
  dropped. No script under `checks\` holds the tables of `measured-k2a-background.md` against
  `recorded\k2a-background\`, so its figures are the report's own (`spikes\storage\android\RUN.md`
  section 16.4).
- **Over SFTP** (2 runs): the same as over WebDAV, case for case, the cache included.
- Not run: any server but WebDAV over HTTP and SFTP; history at its limit.

**Read, not run** (`papers\ground\ground-android.md`): Keepass2Android trims history at every load
and after every merge; KeePassDX trims every entry when its history setting is changed. That
KeePassDX trims an entry when it is edited there, and treats two attachments of the same length as a
single attachment when 32 bits of their MD5 agree, was run on the device in the full measurement
below; the pair of 48-byte bodies was found on the console in 72,707 tries
(`spikes\storage\android`, `md5pair\`).

**KeePassDX 4.5.8, measured in full.** `spikes\storage\android` material, `RUN.md` section 15,
reported in `papers\ground\measured-keepassdx.md` with its evidence under `recorded\keepassdx\`,
recorded 2026-11-16 on the same emulator: 83 counted runs, besides 32 attempts not counted. The
bases hold the generic carriers, the records of the store the final evaluation recommended at the
time (write-once entries with `KPG.` strings in a group of the plugin's own, keys of the database's
custom data, deletion records naming no entry) and today's custom data as controls; every file was
read back with KeePassLib 2.62 and with a reader of the raw file. No script under `checks\` holds
this report's tables against `recorded\keepassdx\`, as `checks\overwrite.py` holds the overwrite
measurement's, so its figures are the report's own. Its section 1.1 cites `logs\check.txt` for the
hash of the `base.apk` the installed package runs from; the evidence holds no such file, only the
downloaded APK's hash and signer in `logs\apksigner.txt` and `logs\verify.txt`, and nothing on the
branch shows the installed file's hash (`spikes\storage\android\RUN.md` section 15.6).

- **Saves.** Save data with nothing changed, six bases (KDBX 4.1 and 4.0, each under Argon2d,
  Argon2id and AES-KDF), a run each: every line as given but the root group's access time, the
  format as given, every per-key time kept; KeePassDX adds a `Meta/HeaderHash` element and writes
  its own name as the generator. A base holding three XML elements no reader knows **could not be
  opened** (2 runs): the unlock failed in the XML reader and nothing was written, where KeePassLib
  skips the elements.
- **Edits.** Every entry edit, a Validate with nothing changed included, added a history item
  holding the whole earlier version, moved the entry's and its parent groups' times, raised the
  usage count and rewrote the expiry time to 2999-11-28 with expiry still off (a chain of 4 steps).
  The title edited on four store entries kept every `KPG.` string (4 steps). A group rename rewrote
  the group's expiry time and moved its time; typing at the end of a group's tags lost the last tag,
  in the one run made, as the driver's keystrokes produced it.
- **Deletions, moves and copies.** A delete to the recycle bin asked nothing, moved the entry's
  modification time and recorded its previous parent; a delete inside the bin and emptying the bin
  asked and wrote a deletion record for every entry and group deleted (5 steps). A move moved the
  entry's modification time (1 step). A copy has a new identifier and no history.
- **History limits.** An edit pruned by count and by size, oldest first (3 steps); after a merge the
  result held what KeePass's Synchronize holds (2 runs).

**Its merge.** "Merge from ..." in the drawer, and "Merge data" after the dialog of section 3.1,
gave the same result line for line where both ended (19 runs over the main pair, every answer of
that dialog among them, and 13 over the hazard sets). Against KeePass 2.62's Synchronize of the same
two copies, headless, in both directions, it differed in six ways, in every run of the direction
where each arose:

| Difference from KeePass's Synchronize | Where it arose |
| --- | --- |
| a deletion record that arrives is applied and not kept, the revocation's record naming no entry among them | the desktop's records with the phone's copy open, the phone's with the desktop's open |
| a record the open copy holds is kept beside its entry when the entry comes back newer | both directions |
| at equal times the open copy's version is current and the other's nowhere, where KeePass takes the arriving copy's | both |
| a move made without a new modification time follows the arriving copy; a move it keeps loses its previous parent and location time | both |
| a group deleted in a copy takes with it an entry the other copy changed later, and no record is left | the phone's copy open |
| groups deleted in the open copy come back empty when the arriving copy holds them | the desktop's copy open |

Every store entry, key and new entry of either copy was kept. The deletion records afterwards: 6
against KeePass's 14 with the phone's copy open, 16 against 14 with the desktop's.

- **A merge that never ends.** With the phone's copy open and the desktop's arriving, 3 of 7 runs of
  the main pair, two "Merge from" and a "Merge data", showed the progress dialog for 90 or 150 s
  with the processor busy and wrote nothing; the other 4 wrote in about a second. A pair whose
  arriving copy deleted a nested group tree while the open copy changed the innermost group later
  never ended in 2 of 2; with an entry inside the tree changed later instead, the merge ended and
  removed the whole tree, that entry and its change included, with no record (2 of 2). Read:
  `DatabaseKDBXMerger.firstNotDeletedParent` never walks up to the next parent. Why the same two
  files loop in some runs and not in others was not found.
- **Deletions that come back.** KeePassDX's merge result merged on the device with a third copy
  older than the deletions (1 run), and KeePass's Synchronize of that result with that copy
  (headless, both directions): the deleted entry, the nested tree and the deleted group came back at
  their old values; the revocation's entry and key stayed and its record naming no entry did not.
  With KeePass's own merge result in place of KeePassDX's, all stayed deleted.
- **Attachments.** Two attachments of the same length whose MD5 digests share their first four bytes
  became a single body at a save (2 runs) and at a merge (3 runs): the second holds the first's
  bytes under its own name and length. Attachments that arrive only in history items by a merge (2
  runs) point at the open copy's attachments of the same index, a history item holding another
  entry's protected attachment among them. KeePass's Synchronize kept every body; synchronized back,
  the wrong bytes reached KeePass's copy where KeePassDX's file arrived at equal times.

**The way back.** KeePassLib read every file KeePassDX wrote line for line. A Synchronize with the
desktop's copy put back the records KeePassDX's merge dropped where that copy held them, and every
change an overwrite lost; what KeePassDX's merge lost of an entry the desktop's copy never held
stays lost. Through Keepass2Android 1.15-r6 (5 runs) every item of a KeePassDX file was kept, the
per-key times dropped and restored by KeePass; its "Yes, merge" of a KeePassDX file with the
desktop's copy was KeePass's Synchronize line for line but the per-key times and the root's access
time.

### 2.4 kdbxweb 2.1.1, pykeepass 4.2.0 and KeePass 2.47

`ground-others`, except where another family is named.

| Program | What the probes show |
| --- | --- |
| kdbxweb, save | every string with its flag, the attachment, the tag, both history items and the entry's and group's custom data kept; a custom data item whose value is empty dropped at every save; unknown XML dropped |
| kdbxweb, merge | the newer entry whole, the other copy's string only in history; at equal times the open copy's entry, the arriving copy's nowhere, in both directions; its history item carries no custom data; the other copy's entry or group custom data is never taken, so the stale value stays under the newer time; entries added in a copy each both arrive; deletion records are united and applied (`wildcard`) |
| kdbxweb, a file whose database custom data carries per-key times | KeePass 2.62 cannot open any of the seven files kdbxweb saved or merged from it: `FormatException` in `KdbxFile.ReadTime`. pykeepass opens such a file |
| pykeepass, save | every item, the empty value, the per-key times and KDBX 4.1 kept; unknown XML kept (3 of 3 elements, where KeePass 2.62 and kdbxweb keep 0) |
| pykeepass, delete | writes no deletion record (`store-entries`, `wildcard`, `rules-central`, `rules-marker-entry`) |
| KeePass 2.47, save | every key and value of the database's custom data kept without the per-key times, as KDBX 4.0, or as 3.1 with AES-KDF and no node custom data (earlier spike); an entry with its protected string kept in a 3.1 file in 3 of 3, the string of 1 MiB in a single file of them, and deletion records that name nothing kept in the two that held them (`wildcard`) |

### 2.5 The rules that follow

- **No program merges an entry's strings field by field.** KeePass 2.62: 120 of 120 cells
  (`ground-keepass`). KeePassXC: 16 of 16 for each of five kinds of change (`ground-keepassxc`).
  kdbxweb (`ground-others`). Keepass2Android and KeePassDX: every run of both merge directions
  (`spikes\storage\android`). Entry custom data goes apart in two programs: a kdbxweb merge
  keeps the open copy's under the arriving copy's newer version (`ground-others`), and a
  KeePassDX merge puts the arriving copy's under whichever version is newer (earlier spike).
- **The modification time decides between two versions wherever a merge compares times, and whole
  seconds are all a file stores** (read). The strictly newer version is current; KeePass's Overwrite
  existing and Keep existing take the arriving or the open version whatever the times. The loser is
  a history item where the program keeps a history item and the history has room. At equal times the
  loser is kept nowhere, and which side loses depends on the program: the open copy's under
  KeePass's Synchronize and Overwrite if newer and under Keepass2Android's merge at a save, the
  arriving copy's under KeePassXC, KeePassDX and kdbxweb. KeePass's Overwrite existing is an
  exception: it keeps the open copy's version as a history item at equal times too, unless the
  history already holds an item at that time (section 2.1; `single-entry`, section 4.10).
  Keepass2Android's synchronization of a remote file is another exception: at equal times it kept
  the phone's version and the other as a history item. In KeePass's window a Synchronize at a save
  kept the window's edit of the same second, since its time in memory holds a fraction of the
  second (section 3.1).
- **A group has no history** (read). A group's fields, its custom data included, go with the
  group whole in KeePass. In KeePassXC, of the fields measured, only the notes go, and the name,
  the icon and the expiry go too (read); tags, flags and custom data stay, while the newer time
  goes always.
- **An entry written once and never changed** was kept by every save that was run, in every program,
  with its strings and the protection flags of its custom strings, and arrived in every merge cell
  that was run for it. A standard string's flag follows the database's memory protection, which
  KeePassLib 2.62 resets at a load (read): a base written with "protect user name" on came back from
  its open and save with every `UserName` unprotected (`spikes\storage\android`). What removes such
  an entry: a deletion record applied by KeePass (its own under every method, an arriving record
  under Synchronize), by Keepass2Android, by KeePassDX or by kdbxweb; Delete Duplicate Entries, when
  a copy of it under another identifier exists and it is the older of the two or the entry in the
  recycle bin; and a program's own delete. What a merge does not do is change it. A KeePassDX merge
  applies the record and keeps none, so a later merge with an older copy brings the entry back
  (section 2.3); Delete Empty Groups removes a group of the plugin's only when it is empty, and
  Empty Recycle Bin whatever sits in the bin (section 2.1).
- **A key of the database's custom data that is only ever added** survives every KeePass merge
  and the Keepass2Android and KeePassDX merges that were run (earlier spike; `outside-file`). A
  KeePassXC merge from 2.4.2 removes it whenever its two loads fall in different seconds, the
  stale copy is the copy loaded later and it holds any key of its own, which a save by 2.7.12 or
  2.8.0 gives every file.
- **Deletion records.** KeePass: only Synchronize carries a record, and a record is dropped when its
  object is newer or bears the same second (section 2.7); Database Maintenance removes every record
  at once. KeePassXC: none applied, none copied, so deleted entries return; its window's merge after
  a reload keeps the file's own records. Keepass2Android: both lists joined and applied, a record
  dropped when the other copy changed its entry later. KeePassDX: an arriving record is applied and
  not kept, so a later synchronize with a copy that still holds the entry brings the entry back.
  kdbxweb: the union.
- **A moved node's place.** KeePass: Synchronize only, the later location time. KeePassXC: the move
  arrives, and loses its location time when the other copy edited the entry. Keepass2Android:
  carried in both directions. KeePassDX: a move made without a new modification time follows the
  arriving copy, and KeePassDX's own move gives the entry a new modification time.
- **Whether a program merges or asks before it writes over a changed file** (section 3.1): KeePass
  at every save through its save command, not at Save As; KeePassXC in every sequence run;
  Keepass2Android at every save and synchronization; KeePassDX and KeePassDroid never.

### 2.6 Every place of the format

`kdbx-places`. An inventory of every place of a KDBX 4.1 file, from the format's specification and
KeePassLib 2.62's reader and writer: 117 places in the outer header, the inner header, `Meta`, a
group, an entry and the deletion records, XML the format does not define at 18 positions, and the
bytes after the last block. Before this family 41 of them had a measurement in this document,
several only in part, and 76 had none. It measured 113 of the 117, all but the cipher, the
compression flag, `Binaries` and `HeaderHash`: through a save by KeePass 2.62 and 2.47,
`keepassxc-cli` of the four releases, kdbxweb and pykeepass; through KeePassLib's five methods, the
four releases and kdbxweb as merges, both ways, at differing and at equal times; and through KeePass
2.62's change of master key, change of key derivation, Save As and step down to 4.0. Every file was
read by a reader of its own bytes, both headers and the decrypted XML. No phone program and no
window ran.

- **No place is united by every merge measured.** Custom icons come nearest: an icon only a single
  copy holds was united by every KeePass method, Keep existing included, and by every KeePassXC
  release, and every save kept an icon, used or not. But kdbxweb's merge deleted every icon no node
  uses and wrote a deletion record for each, which a KeePass Synchronize would carry on (read, not
  run); KeePassXC 2.4.2 and 2.6.6 decode an icon's image and encode it again, and they and KeePass
  2.47 drop an icon's name and time. The outer header's public custom data was united by
  Synchronize, Overwrite if newer, Overwrite existing and Create new IDs, and by no other merge; a
  key of the database's custom data by every KeePass method but Keep existing, and by kdbxweb.
- **Kept by every save and never touched by a merge**, the open copy's value staying:
  `LastSelectedGroup` and `LastTopVisibleGroup` in every merge of every program;
  `MaintenanceHistoryDays`, the master key change reminders and the history limits under KeePass and
  KeePassXC, where kdbxweb took the arriving copy's; a group's `IsExpanded` under KeePass and
  KeePassXC. Read: KeePass's window rewrites the two selections and a group's expansion from what it
  shows when it saves, and KeePassXC writes a zero identifier for a group that does not exist; the
  other values are settings KeePass acts on.
- **The later time wins, field by field**: the database's name, description and default user name
  under Synchronize, Overwrite if newer, Create new IDs and kdbxweb, the open copy's at equal times,
  and under no KeePassXC release; a custom icon's name, time and data by the icon's own time, under
  every KeePass method and kdbxweb.
- **Kept by KeePass only**: a key of the plugin's own in the key derivation parameters, kept by
  KeePass 2.62 and 2.47, kdbxweb and pykeepass at every save and through a new master key, Save As
  and the step down to 4.0, dropped by every KeePassXC save, never merged. Read: lost at the next OK
  of KeePass's database settings dialog, which builds the parameters anew.
- **Dropped**: the bytes after the last block, which no hash covers, by every program; the header
  comment, an undefined header field, and unknown XML elements, attributes and comments by every
  program but pykeepass, which writes the XML back as it read it; an attachment no entry refers to
  by KeePass and KeePassXC. An undefined outer or inner header field makes kdbxweb and pykeepass
  **refuse the whole file** (16 saves of 16); section 2.7 has KeePassDX refusing a file for the
  header comment, an undefined outer header field and an unknown element.
- **Fresh at every save**, in every program: the master seed, the IV, the key derivation's salt and
  the inner stream key.
- **Capacity**: values of 1,024, 65,536 and 1,048,576 characters in 14 places. No program refused a
  file or cut a value short; 12 of the 14 places came through every save of all eight programs at
  the largest size, the other two being a custom icon's data, which KeePassXC 2.4.2 and 2.6.6 encode
  again with the value moved into another chunk, and its name, which those two releases and KeePass
  2.47 drop.
- **Changes of key and format** in KeePass 2.62: a new master key changes only `MasterKeyChanged`;
  the step down to 4.0 drops exactly the 4.1 places and the previous parents.

### 2.7 The phone programs' own code

`source-harness`. Keepass2Android 1.15-r6's own copy of KeePassLib, compiled unchanged for .NET 10
with the program's own format object, and KeePassDX 4.5.8's `database` and `crypto` modules,
compiled for a desktop JVM by the Kotlin compiler the project pins, each with stand-ins for what
touches the device (logging, the native key derivations, images, parceling, the XML serializer) and
driven as the programs' screens drive them; KeePassDX 4.5.8 was also its newest release. A harness
is a filter: where the code drops something, no run of the program keeps it on that path, and where
the code keeps something, the emulator confirms it. When a program reads, writes or asks is not
measured here.

**Held to the emulator's record first.** Keepass2Android: 60 cells of the three phone measurements,
57 line for line and 3 more once the touch of a shown entry, which the program makes on its screen's
thread while the save runs on another, is placed before the write. KeePassDX: 34 cells of its two
measurements, 33 line for line, the six differences of section 2.3 from KeePass's Synchronize among
them; the 34th is a merge of the main pair that ended on the device and did not end in the recorded
calibration. Nothing in either record disagrees with the source at the tag. The calibration held the
two device runs of the main pair that had not ended when it ran; the KeePassDX report, written
later, counts 3 runs of 7 that never ended (section 2.3).

- **The merge that never ends** reproduces. A thread dump holds the job in
  `DatabaseKDBXMerger.firstNotDeletedParent`. The merge of the main pair with the phone's copy open
  did not end in 30 runs of 30 as the first job of a fresh JVM, and with the desktop's copy open
  ended in 30 of 30; after other jobs in the same JVM it ended in some runs and not in others, as on
  the device, and why the order matters was not found.
- **The records of the store the evaluation composed** (section 4.13), 3,000 random histories (2,000
  of two copies and 1,000 of three; each step a write of the plugin's own or a person's act in
  KeePass, a delete with and without a record and a move to the recycle bin among them). In 7,000
  saves and 7,000 edits by each program every store entry and every deletion record of a store entry
  came back as the copy held it. Keepass2Android's merge, over 16,000 orders, was KeePassLib 2.62's
  Synchronize in every merge. KeePassDX's merge differed in 6,791 of the 10,000 merges of two:
  arriving records applied and not kept, 10,140 records of store entries missing. With three copies,
  1,660 store entries that KeePassLib removes were present, in 1,462 merges of 691 of the 1,000
  histories: 680 rule versions, 386 heads, 297 profile additions, 152 authorities, 141 admission
  records, 3 removals and 1 revocation. And 1,208 entries in merges of two, 1,266 in merges of
  three, sat in another group than KeePassLib's result puts them; each of 8 looked at was in the
  plugin's group where KeePassLib had it in the recycle bin.
- **KeePassLib's own tie**: an entry whose deletion record bears the same second as its last
  modification is kept and the record dropped, in 30 merges of two and 90 of three, in 15 histories;
  Keepass2Android's merge does the same, and KeePassDX's removes the entry when the record is on the
  open side.
- **Long strings** of 4,096 to 262,144 characters, plain and protected: no limit, truncation or
  failure in either program's load, save, edit and merge.
- **The places of section 2.6**, on the 20 phone bases that family makes: Keepass2Android opened
  every base; KeePassDX refused 6, for a header comment, an undefined outer header field, an unknown
  element in `Meta`, in a group's `Times` and in `MemoryProtection`, and the base holding all of
  them. Keepass2Android drops a database custom data item's time at every save and merge, a node
  custom data item's time, every place the format does not define, and an attachment no entry refers
  to. KeePassDX turns an entry's quality check back on at an edit, writes `HeaderHash` into every
  4.x file, and in a merge takes the arriving copy's key derivation parameters and node custom data
  whether newer or not, keeps only the open copy's public custom data keys and deletion records, and
  keeps the open copy's group and entry fields at equal times. Unused custom icons came through
  every save and merge of both, and neither wrote a deletion record for an icon.
- **The version written.** The key derivation and the cipher were kept in every file of both. A KDBX
  4.0 file under AES-KDF with nothing above 3.1 came back from either program as 3.1;
  Keepass2Android writes 3.1 for such a file holding a database custom data item too, and since it
  drops that item's time, a file whose only 4.1 feature is that time comes back as 3.1 as well. No
  program counts a previous parent group as a reason for 4.1, KeePassLib 2.62 included. KeePass 2.62
  opens a 3.1 file and writes it as 4.0.
- **Left for the emulator**: the composed store's records and long strings on a device, a move to
  the recycle bin undone by KeePassDX's merge on a device, and the version a phone writes.

### 2.8 A plugin inside KeePass's window

KeePass 2.62's window, with probe plugins that are not the product.

**What a plugin is told** (`desktop-windows`, measurement 5, a logging plugin, 14 runs). File,
Synchronize raises the trigger event `SynchronizingDatabaseFile` before the merge; the merged state
is in memory, and not yet on disk, at `FileSavingPre` and `FileSaving` of the synchronize's own
save; an entry the plugin added at that `FileSavingPre` reached the open file and the other file,
which KeePass overwrites with the saved file with no event of its own. With the trigger system
turned off no trigger event arrives, and the plugin added nothing, since nothing told it a
synchronize was running; a synchronize that answers a save over a changed file still shows as a save
nested inside the outer save's `FileSavingPre`. An import raises no event at all, and is seen only
as a changed database at the next event. Opening a file another program merged raises `FileOpened`
and nothing else. No public event comes before a delete, a move, a duplicate, a paste or a quick
edit; each is first seen at the next `MainForm.UIStateUpdated` (`desktop-hide-and-lock`). Read:
`PwDatabase.MergeIn` offers no hook.

**Hiding the store** (`desktop-hide-and-lock`, 6 runs). KeePass builds its group tree and entry list
with no hook for a plugin. Removing the plugin's group and the store's entries from the two controls
at `Application.Idle`, the controls found by their designer names, left nothing of the store in the
tree or the list in 19 views and rebuilds, and nothing threw; removing them at `UIStateUpdated`
instead made KeePass's own selection of a search result throw, shown to the person as WinForms'
unhandled exception dialog. The group's own flags, searching and auto-type off, keep the store out
of quick search, the expired view and global auto-type with no code running. What hiding did not
reach: Find, Last Modified Entries listed all 12 store entries; the status bar counted the hidden
entries and named the group as skipped; an ordinary entry whose fields reference a store entry
showed its user name and copied its password and user name to the clipboard; export, print and
maintenance walk the database. Under the recycle bin, Empty Recycle Bin deleted the whole store
after a single question (14 records); deep under another group, the selection the file stores opened
the path to it at the next start.

**Guarding the store** (12 runs). With public members: cancelling `PwEntryForm.EntrySaving` kept the
entry dialog from saving through OK, Enter and the answer Yes to its save question; the Group and
Entry commands disabled at every `UIStateUpdated`, menu opening and context menu opening, after
KeePass had enabled them again each time (11 to 31 items at each of 51 updates); an `IMessageFilter`
swallowing the tree's and the list's own command keys, which bypass the menu items; a drop refused
by the main window's selection, since KeePass drags entries as text; the double click's default
action cancelled; Empty Recycle Bin disabled while the bin holds a store entry. Every change tried
through the window's commands was stopped. A disabled OK button did not hold: the save question at
Escape, answered Yes, saved the entry. A menu item hidden and left enabled still fired on its
shortcut. Through a private field, `PwEntryForm.m_pwEditMode`, the entry dialog opens as KeePass's
own read-only view. Out of reach of every guard run: XML Replace, Database Maintenance, Delete
Duplicate Entries, Delete Empty Groups, Import, Synchronize, a command on a group above the store, a
paste of a copied store entry, and reading any value.

**What a change leaves in the entry**: an edit through the entry dialog and each quick edit add a
history item holding the earlier version; a move, a delete to the recycle bin, a duplicate and a
paste add none, and a duplicate or a paste is an entry under a new identifier with the original's
modification time.

## 3. What no store inside the file survives

Two things happen to the file as a whole, or to the tree, and touch every carrier of section 2
alike.

### 3.1 A whole file written over a newer file

A program holds a copy it opened earlier, the file is replaced by a newer file, and the program then
saves or synchronizes. Four measurements on the same emulator, all `spikes\storage\android`
material: a file on the phone's storage, recorded 2026-11-15 and reported in
`papers\ground\measured-overwrite.md` with its evidence under `recorded\overwrite\`; a file on a
WebDAV server reached through Keepass2Android's own storage access, recorded 2026-11-16 and reported
in `papers\ground\measured-k2a-synchronize.md` with its evidence under `recorded\k2a-synchronize\`;
the same server with nobody at the phone to answer, in `papers\ground\measured-k2a-background.md`,
evidence under `recorded\k2a-background\`; and KeePassDX on the phone's storage, in
`papers\ground\measured-keepassdx.md`, evidence under `recorded\keepassdx\`. Two more on a desktop,
KeePass 2.62's and KeePassXC 2.8.0's windows over a file a synchronizer replaced (`desktop-windows`,
measurements 1 and 2). Each file a program left was read back with KeePassLib 2.62. Scripts under
`spikes\storage\android\checks\` hold the first two reports' tables against their evidence; no
script holds those of `measured-k2a-background.md` and `measured-keepassdx.md`, whose figures below
are the reports' own (section 2.3).

**Keepass2Android 1.15-r6, a file opened through the system's file picker**, 32 counted runs.
The newer file differs from the older by three changes: a new entry holding a revocation record,
a custom string changed on an existing entry, and an entry deleted with its deletion record.

| Case | Counted runs | What the program showed | The file afterwards |
| --- | --- | --- | --- |
| the newer file arrives while the database is open; an entry is added and saved; the answer is "No, overwrite" | 18: 3 in this case and 15 in the variants below that showed the dialog | the dialog "Merge changes?", at the save and not before | the older copy with the phone's entry; **all three changes of the newer file lost**, in 18 of 18 |
| the same; the answer is "Yes, merge" | 3 | the same dialog | the newer file with the phone's entry; all three changes kept |
| the same; the answer is "Cancel" | 1 | the same dialog; the editor stays open with the entry unsaved | the newer file, byte for byte |
| the newer file has the older file's length to the byte and its time to the nanosecond | 3 | the dialog | by the answer |
| the newer file has an earlier time | 2 | the dialog | by the answer |
| the newer file has another length and the older file's time, or the older file's length and a later time | 2 | the dialog | by the answer |
| the arriving file holds the older file's own bytes under a later time | 3 | **no dialog** | the older copy with the phone's entry; nothing to lose |
| the newer file arrives while the program is in the background | 4 | nothing on return, the group view still showing the copy in memory; the dialog at the save | by the answer |
| only looking: groups and an entry opened, then the database locked and closed | 2 | nothing | the newer file, byte for byte: nothing was written |
| the database closed and unlocked again with the password, the newer file arriving before or after the close | 4 | the newer file's content; no dialog at the save | the newer file with the phone's entry |
| the database locked with QuickUnlock, the newer file arriving while locked, then unlocked with the last three characters of the password | 2 | the copy held in memory, not the file; the dialog at the save | by the answer |
| "Synchronize database..." from the menu, then an entry added and saved | 1 | the newer file's content after it; no dialog at the save | the newer file with the phone's entry; the command itself wrote nothing |
| "Check for modifications" turned off in the settings, stored as `CheckForFileChangesOnSave` false | 2, and 2 attempts not counted | **the dialog all the same**, in four runs of four | by the answer |

- **What it compares is the file's content.** A replacement of the same length and the same time
  was caught, and a replacement that changed the time and no byte was not. Read: the save hashes
  the file as it is and compares that with the hash taken at the load.
- **When it compares is at the save.** Coming back from the background to a changed file showed
  nothing.
- **A lock with QuickUnlock keeps the copy the program held** and reads nothing from the file;
  only "Close database" and a full unlock read the file afresh.
- **The setting did not stop the question** for a file from the system's picker, which contradicts
  the reading of the source at `v1.15-r6`, where the save skips the check when that preference is
  false. What makes the release ask anyway was not found.
- **Showing the File handling screen once stores "Synchronize in background" as on**, with
  nothing pressed: before the screen none of its preferences was stored, and after it
  `SyncOfflineCacheInBackground_key` was true.

**Keepass2Android 1.15-r6, a file on a WebDAV server**, 21 counted runs; section 2.3 has what
its synchronization does to each carrier. The server's file is the older file when the phone
opens it and is replaced by a desktop's later save.

| Case | Counted runs | What the program showed | The server's file afterwards |
| --- | --- | --- | --- |
| nothing changed on either side; "Synchronize database..." | 1 | no dialog | unchanged, byte for byte |
| only the server changed; "Synchronize database..." | 2 | no dialog; the server's content afterwards | the desktop's file, byte for byte; nothing was written |
| only the phone changed, the server reachable; a password saved | 1 | no dialog | the phone's file, written by the save itself |
| only the phone changed, saved while the server was stopped or in "Work on internal cache only"; then a synchronize | 3 | no question and no warning at the save; no dialog at the synchronize | unchanged after the save, which went to the cache; the phone's file after the synchronize |
| the server changed; a password edited and saved; the answer is "No, overwrite" | 1 | "Merge changes?" at the save | the older file with the phone's password; **everything the desktop wrote gone** |
| the same with "Check for modifications" turned off | 1 | the dialog all the same | the same |
| both changed, the phone's changes waiting in the cache; a synchronize; "No, overwrite" | 1 | the same dialog, at the synchronize | **the phone's copy whole**: no line differs from the phone's cached copy before the synchronize but access times, and the desktop's items are gone from the current entries and from every history |
| the server changed; a password edited and saved; "Yes, merge" | 3 | the same dialog | a merge holding what the desktop wrote and what the phone wrote |
| both changed, the phone's changes waiting in the cache; a synchronize; "Yes, merge" | 4 | the same dialog, at the synchronize | a merge holding what the desktop wrote and what the phone wrote |
| "Always merge on conflict" turned on, stored as `AlwaysMergeOnConflict` true; the server changed; a save | 1 | no dialog | a merge |
| the database closed and opened again after the server's file changed | 2 | no dialog | the desktop's file; the program loaded it |
| the database locked with QuickUnlock, the server's file changed while locked; a password saved; "Yes, merge" | 1 | the copy held in memory after the unlock; the dialog at the save | a merge |

- **The rows are the 21 counted runs**, each run in a single row. In the 5 runs with changes
  waiting in the cache the phone's saves had been made with the server stopped (1 run) or in "Work
  on internal cache only" (4 runs), with no question and no warning at any save.
- **The answer "No, overwrite" lost everything the desktop wrote since the phone's last load**,
  from the entries and from their histories, in 3 runs of 3: at a save, at a synchronize, and with
  "Check for modifications" turned off.
- **The question is the same dialog at a save and at a synchronize.** With "Synchronize in
  background" stored as on, the question came a few seconds after the save (1 run); an attempt
  that was not counted shows the save writing the cache first and the question coming from the
  synchronization that followed.
- Read: the synchronization downloads the server's file and compares its hash with the cache's
  base version, and with both changed it saves with the downloaded data as the original file,
  which leads to the question whatever the check-on-save setting holds.
- Not run in these 21 runs: any server but WebDAV over HTTP. "Cancel" at a synchronize, a
  synchronization with nobody to answer and SFTP are the next measurement's.

**Keepass2Android 1.15-r6, a background synchronization with nobody to answer**, 16 counted runs
over the same WebDAV server and over SFTP. The server's file changed, the phone saved a change with
"Synchronize in background" on, and the synchronization's first request was held until the program
was in the state named; then 45 s were watched with nothing pressed.

| State | "Always merge on conflict" | Counted runs | What the program did | The server's file and the cache |
| --- | --- | --- | --- | --- |
| in front, on its own Settings screen | off | 1 | the "Merge changes?" dialog over that screen at the first look | the desktop's file byte for byte; the cache the phone's copy |
| in the background | off | 1 | an ongoing notification, "User interaction required. Please open the app.", with no action; the dialog at once on return | the same |
| the process killed after the save | off | 1 | nothing until the next unlock; then the dialog | the same |
| the screen off | off | 1 | the notification; the dialog when woken | the same |
| each of the four states | on | 7: 1, 2, 1 and 3 | no question; a merge within 3 to 5 s, for the killed process within 5 s of the unlock | one merged file on both, every desktop item and the phone's change |

- **The question cannot be dismissed.** With the setting off, BACK left it standing in 4 runs of 4;
  HOME for 30 s wrote nothing; after a force-stop, a start and an unlock it was there again before
  the group list could be reached, so no second save could be made. Nothing of either side was lost
  while it stood, and nothing of the phone's reached the server.
- **"Cancel"**, at "Synchronize database..." (1 run) and at a background synchronization after a
  save (1 run): nothing written in the 15 s watched; the next synchronize asked again, and "Yes,
  merge" then merged.
- **A crash** in 1 of the 7 runs with the setting on: `DuplicateUuidsException` thrown on the main
  thread 0.14 s after the merged upload ended; the merged file was on the server and in the cache,
  nothing was lost, and the program was not running afterwards, with nothing shown. Read: two calls
  rebuilding the program's index of entries race. The two repeats of that state, and the later run,
  recorded no such exception.
- **Turning the setting on.** Showing the File handling screen and pressing "Always merge on
  conflict" stored "Synchronize in background" as on too, in every such run. Read: the setting
  belongs to the program's installation and is off by default; only a save, an open, the menu's
  synchronize and a periodic synchronization, itself off by default, start a synchronization, so
  nothing starts a synchronization when the server becomes reachable again (section 2.3).
- Not run: long screen-off periods under Doze, a kill during an upload, the periodic and Wi-Fi-only
  options, servers other than WebDAV and SFTP, the six alternating changes with the setting off, any
  device but the emulator.

**KeePassDroid 2.7.**

| Case | Counted runs | What the program showed | The file afterwards |
| --- | --- | --- | --- |
| the newer file arrives while the database is open; an entry is added and saved | 3 | **nothing**, at the replacement or at the save | the older copy with the phone's entry, and entry and group custom data deleted as at every save; **all three changes lost**, in 3 of 3 |
| the newer file arrives; the database is locked and unlocked with the password; an entry is added and saved | 1 | the newer file's content | the newer file with the phone's entry |

Read: its save writes with no check of any kind, and its lock clears the loaded database.

**KeePassDX 4.5.8**, 27 counted runs. The newer file is the desktop's later save: a revocation with
its key and its record naming no entry, a changed password, and an entry deleted with its record. It
replaced the opened file at a time relative to that file's own, to the nanosecond; the phone then
added an entry or deleted an entry to the bin, saved by autosave.

| Case | Counted runs | What the program showed | The desktop's changes afterwards |
| --- | --- | --- | --- |
| on screen; the replacement at the opened file's own time, 1, 5, 9 or 10 s later, or an hour earlier | 8 | nothing | **lost**, 8 of 8 |
| on screen; the same length to the byte, the content changed, at the same time or 5 s later | 2 | nothing | **lost**, 2 of 2 |
| on screen; 11, 15 or 60 s later, of another length or of the same | 6 | a dialog, "The information contained in your database has been modified outside the app.", the old and new time and size, and a single button, OK: as the entry editor opened, and again on the group screen | **lost**, 6 of 6 |
| the time 60 s later and no byte changed | 1 | the dialog twice | nothing to lose |
| from the background, 5 s or 60 s later | 3 | nothing at 5 s; the dialog on return at 60 s | **lost**, 3 of 3 |
| a delete on the same screen, 5 s or 60 s later | 2 | nothing | **lost**, 2 of 2 |
| the process ended and started again; the program's own lock and an unlock with the password | 3 | nothing | **kept**: the newer file read afresh |
| autosave off; the dialog; the menu's Merge data | 1 | the dialog twice | kept: Merge data wrote nothing, the merge stayed unsaved |
| autosave off; the dialog; the menu's Save data | 1 | the dialog twice | **lost** |

- **What it compares** (read): the file's modification time and size, at every resume of a database
  screen, reported only when the time is more than 10,000 ms later than the time it holds; a save
  compares nothing. Measured: 11 s later reported in 2 of 2, 10 s in 0 of 2; content and length
  played no part.
- **Being reported protects nothing.** OK records the new time as the program's own (read), and
  every run that saved after it wrote the older copy over the newer file: 22 runs of 22 with a
  change to lose lost it, with the dialog or without. Each lost file read back as the older file
  with the phone's change and nothing else but the root group's times.
- **The dialog's other answers**, on the main merge pair: "Save data" wrote the open copy whole over
  the arriving file, every change of the other copy gone (3 runs); "Reload data" wrote nothing and
  every unsaved change of the open copy was gone (4 runs).
- Read: KeePassDX keeps no database in memory over a lock; its lock closes the database and the
  unlock loads the file again.
- The earlier spike's single run agrees: a replacement that did not move the file's time on was not
  noticed, and the save wrote the open copy over it.

**KeePass 2.62's window**, 31 counted runs. The window held the older copy and changed it; a
replacement standing for a file synchronizer, written beside the file and renamed over it, brought
the other writer's later save: a write-once `KPG.` entry, a key of the database's custom data, a
deletion record naming no entry, an entry deleted with its record, and passwords.

| Case | Counted runs | What KeePass showed | The file afterwards |
| --- | --- | --- | --- |
| a save through its save command (Ctrl+S, the toolbar, the save at closing, locking and exiting, the save after an entry edit when that setting is on) or a synchronize's own save, answered Synchronize | 12 | "Overwrite the existing file?", with [Synchronize] [Overwrite] [Cancel], the second naming the loss | **a merge**, holding both sides' changes |
| the same, answered Overwrite: at the opened file's time, an hour earlier, at the same length, at a lock, with file transactions or the verification of written files off, and with nothing changed in the window | 10 | the same question | **the older copy with the window's changes**: every change of the other writer gone, its write-once entry, its custom data key and its deletion records among them |
| the same, answered Cancel | 3 | the same question; the window still marked changed | the newer file byte for byte; the window's changes in memory only |
| the older file's own bytes under a later time | 1 | no question | nothing to lose |
| Save As over the open file's own path | 1 | the file dialog's "already exists. Do you want to replace it?", **and no question about the changed file** | **the older copy with the window's change** |
| "Do not ask whether to synchronize or overwrite; force synchronization" on | 1 | no question | a merge |
| a lock and an unlock, the file changed before or during the lock, then a save | 2 | no question at the save | the newer file with the window's change: the unlock read the file afresh |
| nothing changed, the window brought to the front | 1 | nothing: the window still listed the older file's entries | unchanged |

- Read: every save through `SaveDatabase` hashes the file on disk and compares the hash taken at the
  load or the last save, so content decides and not time or length, which the control and the
  variants confirm; Save As does not compare; KeePass watches no file.
- **At the same second**, a Synchronize at a save kept the window's version current and the other
  writer's as a history item (3 runs), where KeePassLib's Synchronize of the two files takes the
  arriving copy's. Read: the window's edit holds a fraction of the second in memory, and the
  comparison reads whole ticks.

**KeePassXC 2.8.0's window**, 21 counted runs under the release's defaults or with a single setting
changed. Its watcher saw the replacement within about a second in every run. With no unsaved change
it reloaded the file by itself. With an unsaved change it asked "Reload database", [Merge] [Ignore]
[Discard] [Cancel], and at a save that found the file changed the same question with [Overwrite] in
place of [Ignore]; with "reload on change" off it asked "File has changed", [Yes] [No], and asked
again at the save. It merged or asked before writing over the changed file in every sequence run.

| Answer | Counted runs | The file afterwards |
| --- | --- | --- |
| no unsaved change, or "File has changed" answered Yes | 2 | the newer file, reloaded |
| Merge, in either question | 10 | a merge, saved by itself or at the next save |
| Database, Merge From Database | 3 | a merge, saved by itself |
| Overwrite | 4 | **the older copy with the window's change**, every change of the other writer gone; with "backup database file before saving" on (1 run), the newer file kept beside it as `db.old.kdbx` |
| Ignore, then a save | 1 | **the same loss, with no further question** |
| Discard | 1 | the newer file; the window's own change gone |

Section 2.2 has what each merge kept, item by item.

**What the file holds afterwards.** In every run that wrote the older copy over the newer file on
the phone's storage, the file read back as the older file with the phone's entry: no line of its
dump differs from the older file's but the new entry's, the two counts it changes, and for
Keepass2Android the per-key time. For KeePassDroid the entry and group custom data are deleted
besides and `SettingsChanged` reads back as the moment of reading, as at every save of that
program. Every other carrier is as it was when the phone opened the file, so nothing inside the
file distinguishes it from a file that was never ahead. A KeePass that still holds its own copy asks
at its next save over such a file, and its Synchronize keeps both sides' changes (above);
KeePassDX's overwritten files, synchronized headless with the desktop's copy, gave every desktop
change back with the phone's own (`papers\ground\measured-keepassdx.md` section 8.1). Not run: a
file synchronizer of any kind.

### 3.2 A node's place, decided by a merge

A node's place is a single value per node, changed in place and merged by a time. No datum of
the plugin's is touched when a merge decides it.

| Program | What was measured | Family |
| --- | --- | --- |
| KeePass 2.62 | Synchronize carries a move; of two moves of a node made in two copies the later wins, so the copy that moved the node out gets it back; the three other methods that match identifiers leave each copy's place alone | `grant-only`, scenarios 09 to 13, 40 cells |
| KeePassXC, four releases | a move arrives in both directions. When the other copy edited the moved entry, the merge into the moving copy keeps the entry in the new group with the other copy's older location time; a KeePass Synchronize then carries that regressed time, and the merge back into the other copy puts the entry into the old group, 4 of 4 releases | `ground-keepassxc`; `grant-only`, `survival\` S3 |
| Keepass2Android 1.15-r6 | its merge carries a move made in a copy, in both directions, with the previous parent kept; its own Move keeps every carrier and records no previous parent | `spikes\storage\android` |
| KeePassDX 4.5.8 | at equal modification times the entry follows the arriving copy: with the moving copy open and a copy without the move arriving, the entry is **back in the old group**. A KeePass Synchronize of that result with the copy that lacks the move keeps it undone in both directions; with the copy that holds the move it restores it with that copy open and keeps it undone with the phone's file open. In the full measurement a move made without a new modification time was undone in both directions, the desktop's with the desktop's copy open and the phone's with the phone's open; KeePassDX's own move gives the entry a new modification time and held both ways; a move it keeps loses its previous parent and location time | `spikes\storage\android`; `papers\ground\measured-keepassdx.md` |

In the options that keep a rule in the plugin's own entries and nothing on the node, a node that
is closed only by where it sits follows every row of this table: `grant-only` reads an entry filed
under an ungranted group in one copy, with that group granted in the other, in 14 of 18 cells
(section 4).

## 4. What each option's probes showed

Twelve options were designed, each attacked by a survival critique and a security and fit critique,
and `crdt` by a formal critique as well. Each of sections 4.1 to 4.12 gives what the option is, what
its probes measured of its survival, every measured sequence that ends with wider access than was
decided or with the plugin refusing, and its cost figures. Sections 4.13 to 4.15 measure the store
the evaluation composed from these options, as a whole, and two of its parts, the reading of a
record edited by hand and the rule model; section 4.16 measures that store again in the
configuration that was decided, and sections 4.17 and 4.18 readings of it that were open then: the
first of section 4.17 was taken into the design, and none of the readings of a copy was. A sequence
is listed only where a probe ran it; a sequence a paper reads or assumes is not, and where a probe
ran the steps and only the ending is a paper's reading, the entry names the reading. "Wider" is a
client reaching what the last decision took away: a revoked client admitted, a retired authority
served, a removed profile in force, a narrowed rule widened.

Three things hold for every option and are not repeated:

- **No probe is the plugin.** Each models its option's store and read rule in a program of its
  own, over KeePassLib 2.62 as a library, `keepassxc-cli` and, where named, pykeepass and kdbxweb;
  the survival model of `outside-file` runs over a modeled synchronizer and neither. A verdict
  such as "admitted" or "refuses" is that program's reading of the files a merge left.
- **The phone programs were run on generic carriers**, section 2.3, and on no option's exact
  records; the full KeePassDX measurement held records of an earlier composition of the store, and
  the harness of section 2.7 ran the records of the composed store of section 4.13. A row "the phone
  programs" below restates which measured carrier the option leans on.
- **Every timing was taken once or a few times in a console process on a machine that other
  measurements were sharing.** The later runs moved by up to a factor of 3.2 for the in-memory
  timings, so each holds to an order of magnitude and is no number to compare options by. No
  timing was taken on KeePass's own thread.

### 4.1 `store-entries`

Each database-level record (authority, client record, revocation, retirement, profile act, renewal
answer) is an ordinary entry in a group of the plugin's own, found by a fixed identifier, written
once and never changed; a later decision is another entry, and the gate folds all of them at every
read. It covers the database-level data and no rule. Probes: `store-entries`, in `design\`,
`survival\` and `security-and-fit\`.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, the four methods that match by identifier | two copies that each made the store end as one group holding both copies' entries | with the four KeePassXC releases, 16 of 16 cells |
| KeePass 2.62, Create new IDs | a second `KeePassGate` group under a new identifier beside the untouched store | 6 of 6 cells |
| KeePass 2.62, every method | an entry only one copy holds arrives in the other | with KeePassXC, 54 of 54 merge cells |
| KeePass 2.62, Synchronize | a deletion record reaches the other copy; under the other four methods it does not | set D |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, save | both group flags, every custom string and its protection flag kept; the file stays KDBX 4.0 | 4 of 4 saves; 66 of 66 files at 4.0 |
| KeePassXC, merge | no deletion record applied or copied: the three entries a copy deleted come back, and a revoked client's record is back beside its revocation entry | 4 of 4 releases, each direction |
| Keepass2Android 1.15-r6, KeePassDroid 2.7, KeePassDX 4.5.8 | the carriers of this option (a group with its flags, entries, plain and protected strings) kept by every operation that was run, section 2 | `spikes\storage\android\RUN.md` |

In all 59 results (30 KeePassLib merges, 24 `keepassxc-cli` merges, 4 saves, 1 chain) every store
entry has the strings it was written with and the protection flag where it was written. Under
every merge that matches identifiers, the same decision made in both copies at different times is
a single entry with one history item, and made in the same second, a single entry with no history.

Copy A adds a profile while copy B adds the same profile and removes it: after every merge that
matches identifiers a single entry for both additions, with a history item, stands beside B's
removal, 16 of 16 cells, so by the design's fold the name is off the list and A's addition is
gone (`design\`, set I; the probe prints the entries and applies no fold). The widening the
survival critique builds on the same identifiers, a removal undone by an addition made again in
another copy, was not run.

**Sequences that end wider.**

- **A revocation undone in two steps.** A `keepassxc-cli` merge of the revoking copy into a stale
  copy leaves the client's record, the revocation entry and no deletion record (2.4.2 and 2.8.0).
  pykeepass 4.2.0 then deletes the revocation entry and writes no deletion record (0 before, 0
  after), and the design's fold admits the client. `survival\`, sequence A.
- **A store remade under the reused identifier.** kdbxweb 2.1.1 merges a copy holding the remade
  store with a copy in which the store was deleted, in either direction, to no store group at all.
  With the revocation brought to the stale copy only as an entry by `keepassxc-cli` 2.8.0, kdbxweb
  drops the store again when that copy meets the deleting copy, and every KeePassLib merge of that
  result with the stale copy admits the client: all 8 results that match identifiers, and the
  Create new IDs import into the stale copy; the import the other way serves nothing.
  `survival\`, P3.
- **A store group deleted with no deletion record, then merged into the stale copy.** pykeepass
  deletes the store group in the revoking copy and writes no record for it, so that copy holds no
  revocation entry any more and a single deletion record, the revoked client's record's. Merged
  into the stale copy under Overwrite if newer, Overwrite existing, Keep existing or Create new
  IDs, neither arrives, and the stale copy **admits the revoked client**. Under Synchronize the
  deletion record arrives and removes the client's record, and the client is not admitted.
  `survival\`, P4, the direction into the stale copy.

**Sequences that end refusing.**

- **The same deletion, in the revoking copy.** After pykeepass deletes the store group the copy
  serves no authority and admits nobody. Merging the stale copy into it under Synchronize,
  Overwrite if newer, Overwrite existing or Keep existing brings the store back in the state that
  was decided: the authority served, the unrevoked client admitted, and the revoked client's
  record removed again by its deletion record. Under Create new IDs the store comes back as a
  group under a new identifier, and nothing is served. `survival\`, P4, the direction into the
  revoking copy.
- **A store deleted and remade.** The kdbxweb merges of the second sequence above end with no
  store group, and nothing is served. `survival\`, P3.
- **A planted refusal.** A Bitwarden JSON item carrying `KPG.Kind = retired` and a `KPG.Data`
  naming an authority, run through KeePass 2.62's own importer and a Create new IDs merge, gives an
  entry holding both strings unchanged in a user's group. `security-and-fit\`. That the fold then
  refuses the authority, and that a later prune deletes it, is the critique's reading.

**Costs.** The read pass over an invented database of 10,054 entries, 501 groups and 5,000
deletion records took 1.871 ms in the record and 1.824 to 3.720 ms in the runs made again, in a
console process and not on KeePass's thread. The 49 test files the security critique lists hold
26,434 lines and 734 lines with `[Test]` at `a42e38aa`; the list itself and the count of 42 source
files have no recorded search behind them.

### 4.2 `rules-on-node`

An entry's rule is a plain custom string `KPG` on the entry, and a group's rule a string
`KPG.Group` on a rule entry inside the group. Every rule change adds a seal entry to a group of the
plugin's own, naming the node, the digest of the text and the seals it replaces. The rule in force
is the text the newest seal names, on the node or in the carrier's history; a text with no seal, or
a seal whose text is nowhere, refuses at the node and below. It covers the rules only. Probes:
`rules-on-node`, in `design\`, `survival\` and `security-and-fit\`. "D" is the read rule as the
design's probe implements it and "V" the survival critique's stricter rule, which also looks at
the carriers' history in steps 3 and 5 of the read.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, four methods both ways, the design's 11 scenarios | every cell gives the head rule, the sealed silence or a refusal | 88 of 88 cells, 11 scenarios |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, `merge -s` both ways | the same; the four releases give identical output | 88 of 88 cells |
| KeePassXC, save | every rule and seal reads as before; the rule entry keeps its time and gains the four standard strings | each release |
| KeePass 2.62, Create new IDs | the originals unchanged; each copy that carries a rule refuses | `design\` |
| KeePass 2.62, a rolled back rule | found in history after 1 to 9 later edits, refused from the 10th; with the design's repair in the first edit, in force after 12 | `design\` |
| The phone programs | entries, custom strings and history items kept by every operation run, and deletion records by every save; a Keepass2Android merge drops a record whose entry the other copy changed later, and a KeePassDX merge keeps no arriving record, section 2.3. The bare name `KPG` and an empty custom string were not on the phone bases | `spikes\storage\android\RUN.md` |

**Sequences that end wider.**

- **An ordinary rollback, then the newest seal lost** (deleted for good, recycled, or moved to the
  root): the older, wider rule is back in force in 6 of 8 cells for each of the three ways under D,
  and the other two cells refuse; V refuses in all 8. `survival\` C1; `security-and-fit\` S1 shows
  the same for a recycled and for a deleted head seal, and a refusal after the repair.
- **A first rule's only seal lost**: silence in 6 of 8 cells under D; V refuses in all 8.
  `survival\` C2.
- **The `KPG` field deleted, then the plugin's group deleted**, under Synchronize both ways: the
  entry answers silence under D and refuses under V, and the ruled group refuses under both.
  `survival\` C3.
- **A group deleted for good and its seals pruned with deletion records, an entry added in the
  other copy**: 2 seals pruned, and the group comes back with no rule in 5 of 8 cells under both
  read rules. With a prune that leaves no deletion records the same 5 cells refuse. `survival\` C4.
- **A seal identifier derived from content**: a copy that sets the same text again after another
  copy removed it writes the first seal's identifier, and the later narrowing is silence in 8 of 8
  cells under both read rules. `survival\` C5. The nonce the critique proposes has no variant in
  the probe.
- **An entry narrowed, deleted, its seals pruned, then `keepassxc-cli` 2.8.0 merging a stale
  copy**: the wide rule is in force, with or without deletion records. Without the prune the entry
  refuses. `security-and-fit\` S2.

**Sequences that end refusing.** A tie the rule's version lost (3 of 8 KeePass cells and the
KeePassXC merge into B); a rule changed in both copies (two heads, all 8 KeePass cells and both
KeePassXC cells); a hand edit of the text (6 of 8 KeePass cells and both KeePassXC cells); the
rule entry or a node's only seal deleted for good (every method into the copy that deleted it,
and Synchronize into the copy that kept it); Keep existing where a seal arrives without its text;
a 1.5 MiB attachment under a 1 MiB history limit, which leaves an empty history; Delete Duplicate
Entries removing a rule entry that carries no `KPG.Node` (1 deleted); a rule entry moved to the
root, which refuses the root and its group; and an entry added at the root under a name that .NET
Framework 4.8 collates as `KPG.Group`, which refuses the root, run under `de-DE` (U+034F is
ignored in en-US, de-DE, tr-TR and lt-LT). `design\` scenarios 02, 03, 06, 08, 09, 10;
`survival\` C6; `security-and-fit\` S3, S4.

**Costs.** A seal holds 144 characters of strings. Reading 2000 seals into an index took 1.38 ms
in the record and 1.33 to 2.46 ms over seven later runs; asking each of 20000 entries for
`KPG.Group` 2.59 ms in the record and 3.16 to 6.87 ms later. Both are 20 rounds in memory in a
console process, and hold to an order of magnitude.

### 4.3 `rules-marker-entry`

Each rule is an ordinary entry, a rule entry, inside the group it governs, or beside the entry it
names. The rule text sits in custom strings and the identifier is a hash of them, so a copied or
edited rule entry stops counting and closes its group. A rule entry is never changed: a new rule
adds an entry naming the rule entries it replaces, and two add-only records (`ruled` and
`replaced`) make a missing rule refuse and a replaced rule stay dead. Two rule entries that never
saw each other meet to the narrower. It covers the rules only. Probes: `rules-marker-entry`, in
`design\`, `survival\` and `security-and-fit\`.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62 and KeePassXC, merges that match identifiers | no verdict wider than the last decision made at the node | 560 verdicts in 26 cells: KeePassLib 160, `keepassxc-cli` 160, chains 80, back and forth 80, hand deletion 80 |
| KeePass 2.62, Create new IDs | in the import of A into B the open copy keeps its own older rule at four nodes; the root and every copied group refuse | 4 of 76 verdicts wider, all in that import |
| KeePass 2.62, Synchronize | replaced rule entries gone from both copies | both directions |
| KeePass 2.62, the other three methods | into the stale copy the replaced rule entry stays beside the new rule entry, dead by its record | `design\` |
| KeePassXC 2.4.2 to 2.8.0, save | every KPG entry identical in identifier, group, time, strings and flags | 4 of 4 releases |
| KeePassXC, merge | no deletion applied: a replaced rule entry stays or comes back beside the rule entry in force; a later KeePass Synchronize deletes it again | 8 of 8 merges; 4 of 4 chains |
| The same rule made in two copies | a single entry after every merge that matches identifiers, with a history item when the times differ and none at a tie | 28 of 28 results each |
| The phone programs | an entry in a user's group with a JSON custom string kept by every operation run, section 2 | `spikes\storage\android\RUN.md` |

Of 46 evaluations of 43 files, 34 read KDBX 4.0 and 12 read 4.1, all 12 having passed through
KeePassXC 2.7.12 or 2.8.0; four of the 12 were written by KeePassLib after such a merge.

**Sequences that end wider.**

- **A repeated identifier.** A copy that repeats another copy's first narrowing after that copy
  replaced it writes the same identifier, so its later narrowing arrives dead: the node grants read
  and list, wider than the narrower of the two, in 10 of 10 results (6 KeePassLib, 4
  `keepassxc-cli` 2.4.2 and 2.8.0). `survival\` s4.
- **Two rule entries met to the narrower, then the narrower deleted or recycled**: read and list.
  `survival\` s5.
- **Create new IDs, then the tab's Remove on the copies**: the copies of a sealed entry and of an
  entry in a sealed group grant read and list while the originals stay sealed. `survival\` s7.
- **The rule entry and its `ruled` record both deleted**: refused when KeePassLib deletes; read
  and list with pykeepass, which writes no deletion record, and after a merge that applies an
  arriving deletion without recording it, which the probe simulates for KeePassDX. `survival\` s6.
- **The recycle bin pointer decided by a time**: after a Synchronize either way a node grants
  read, and the old bin is an ordinary group that clients reach. `survival\` s9.

**Sequences that end refusing.** A rule entry recycled or deleted for good, in KeePassLib and with
`keepassxc-cli` 2.4.2 and 2.8.0; a hand edit of the rule text; a hand copy of a rule entry; the
root's rule entry recycled, which refuses the root and everything that inherits. **The tab's Save
does not repair a lost first rule**: it writes the lost identifier again, which is dead under its
deletion record, and the group still refuses; a Synchronize, Overwrite if newer or Keep existing
merge with a copy that still holds the rule entry then drops the record in both directions, 6 of
6, and the rule comes back. Or Save duplicates a recycled entry's identifier, which KeePass's
repair of duplicate identifiers, called by reflection, resolves by renaming the entry that comes
later in the tree: the rule holds when the bin comes after the group and the group closes when
the bin comes first. With the duplicate identifiers left unrepaired, Empty Recycle Bin removes
the recycled twin with a deletion record and the group refuses; a `keepassxc-cli` 2.4.2 or 2.8.0
save keeps a single entry of the two, where the later was, so the group keeps its rule with the
bin before it and refuses with the bin after it. After Delete Duplicate Entries removed the
original of a copied rule entry, Save still refuses. Save after a hand edit and Remove, Save of
the root's rule after its rule entry was deleted for good, and Save of a first rule again after
its replacement was recycled each write a dead identifier and still refuse. `security-and-fit\`
S1, M2; `survival\` s1 to s3, s8.

**Costs.** On an invented database of 10,402 entries, 501 groups, 101 ruled groups and 100 ruled
entries, building the view took 3.066 ms in the record and 3.703 to 6.109 ms over six later runs,
and a listing of every entry 24.300 ms in the record and 26.656 to 33.739 ms later, in a console
process. A search at `a42e38aa` finds 18 host test files holding 99 references to what the option
removes.

### 4.4 `rules-central`

Nothing is written on a governed node. Each version of a node's rule is an entry in the plugin's
own group, naming the node by identifier, written once and never changed, with an identifier
derived from the node and the content. The newest version is in force, and two newest versions
give their meet. A newest version that is deleted or changed by hand makes the node refuse, and a
rule entry copied under a new identifier makes the whole database answer `503 undecided`. It covers
the rules only. Probes: `rules-central`, in `design\`, `survival\` and `security-and-fit\`.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, four methods both ways | the rule decided last, the meet of two, or a refusal, in every cell but three: in scenario 14, into the copy that never saw the narrowing, Overwrite if newer, Overwrite existing and Keep existing keep that copy's older rule | 112 cells, 14 scenarios |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, `merge -s` | the same, scenario 14 included; a merge is a union and applies no deletion, so a rule entry deleted for good comes back and the rule is in force | 112 cells |
| KeePassXC, save | every rule entry's identifier, strings, flags and times kept, an empty `KPG.After` and the group's flags and notes included; KDBX 4.0 | 4 of 4 releases |
| A rule changed in both copies | both versions arrive and the node carries their meet | 16 of 16 cells |
| The same decision in two copies | a single rule entry | 16 of 16 cells |
| KeePass 2.62, Create new IDs of a copy into itself | the originals untouched; a second group under a new identifier holding 3 entries; the copies of the ruled group and entry carry no rule | `design\` |
| The phone programs | entries with custom strings and identifiers kept by every operation run, and deletion records by every save; a Keepass2Android merge drops a record whose entry the other copy changed later, and a KeePassDX merge keeps no arriving record, section 2.3; the exact rule entry shape was not on the phone bases | `spikes\storage\android\RUN.md` |

Create new IDs, then Delete Duplicate Entries, deleted 7 entries, the originals of the three user
entries among them, and left the rule entries only as copies under new identifiers, with the
ruled group still ruled by a copy. No verdict wider than the last decision is printed; the
design's answer to such copies, `503 undecided`, is its rule, and the probe holds no gate.
`design\`.

**Sequences that end wider.**

- **A narrowing pruned with its node.** Copy A narrows an entry, then deletes the entry for good
  and prunes both versions with deletion records; copy B, which never saw the narrowing, edits
  the entry. Into B the entry stands under the older, wider rule under Overwrite if newer,
  Overwrite existing and Keep existing and under `keepassxc-cli` in all four releases;
  Synchronize refuses both ways, and every merge into A refuses. `design\`, scenario 14.
- **The design's own repair.** After the newest version is deleted for good (C3), or a space is
  added to its `KPG.Data` (C5), the repair the design runs on an OK writes the older rule's
  document naming 2 predecessors, and the copy serves read and list. In C3 a copy that still held
  the narrowing serves the wider rule after each of seven merges with the repaired copy: four
  KeePassLib methods, `keepassxc-cli` 2.8.0 and 2.4.2, and the KeePassDX model; in C5 a
  Synchronize with the unrepaired copy keeps read and list. After the store group is deleted for
  good and the group's rule is entered again, the repair writes `{"default":"inherit"}` for the
  entry naming 2 predecessors, the entry gets read, update and list from the group, and a
  `keepassxc-cli` 2.8.0 merge into the base, where the entry had list, gives the same (C4).
  `survival\` C3, C4, C5.
- **pykeepass deletes the newest version** with no deletion record (0 before, 0 after), and the
  file serves read and list where list was decided. `survival\` C2.
- **A merge that applies an arriving deletion and keeps no record.** In the probe's model of
  KeePassDX 4.5.8's merge, written from its source, the entry deleted for good in one copy and its
  version removed with the Rules page's Remove gives read, update and list from the group above
  where the other copy had read and list (C1); a permanent delete of the newest version gives read
  and list where list was decided (C2); and the store group deleted for good gives create, read,
  update and list from the group above, with no store and no record (C4). Every such line is the
  model's, not KeePassDX's. That KeePassDX 4.5.8 removes an entry on an arriving deletion and keeps
  no record is measured on the emulator for an ordinary entry (section 2.3). In C1, `keepassxc-cli`
  2.8.0 and 2.4.2 merging the first copy into the model's result leave the widening; a KeePass
  Synchronize with that copy puts the refusal back.
- **A planted version.** A foreign database under another master key, sharing only the store
  group's public identifier and a group's identifier, puts its rule entry into the store group
  under Synchronize and under Keep existing; under Synchronize the store group also takes the
  foreign name and its searching and auto-type flags go back to inherit. The probe prints where
  the planted entry lands and no verdict at the node it names. `security-and-fit\` P2.

**Sequences that end refusing.** A newest rule entry deleted for good (Synchronize carries the
refusal to the other copy); a `KPG.` string edited by hand (Synchronize and a KeePassXC merge
carry the edit, and `keepassxc-cli` 2.8.0 brings the changed entry back after a repair, so the
node refuses again); a node deleted for good with its versions pruned, then edited in a copy that
kept it (Synchronize both ways and every method into the deleting copy, and `keepassxc-cli` into
the deleting copy in scenario 14); a node deleted for good and its version removed on the Rules
page, then synchronized into a copy that kept it (`survival\` C1); the store group deleted for
good. `design\` scenarios 07, 09, 13, 14; `survival\` C1, C4, C5.

**For comparison, today's store.** `PwGroup.Duplicate` and `PwEntry.Duplicate` put the rule on
the copy. `security-and-fit\` P1.

**Costs.** A rule entry holding a 33 character rule and one predecessor holds 232 characters of
strings. The whole reading took 1.426 ms at 10,060 entries and 9.383 ms at 50,060 in the record,
and 1.500 to 2.435 ms and 8.317 to 12.426 ms over nine later runs, in a console process. The
eleven test files the security critique names hold 163 tests at `a42e38aa`.

### 4.5 `rules-linked`

Each version of a rule is a plugin entry in a rules group, written once, witnessed by a record
entry in the store group. Every ruled node carries a fixed anchor: an entry as the string
`KPG.Anchor`, a group as a link entry inside it. A rule its node refers back to is in force; a
rule the node does not refer back to (a lost reference, a copy, a plant) only limits, taking away
and never granting; a reference to a missing or damaged rule refuses there and below. It covers
the rules only. Probes: `rules-linked`, in `design\`, `survival\` and `security-and-fit\`.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, four methods both ways | no cell gives more than the last decision, or more than the narrower of two | 96 cells, 12 scenarios |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, `merge -s` | the same; the four outputs are alike line for line | 96 cells |
| KeePassXC, save and `edit -u` | every plugin entry unchanged; the entry's `KPG.Anchor` kept, and carried by the revision an edit makes | 4 of 4 releases |
| A first rule on an entry in one copy, the entry edited later in the other | the reference is gone in 6 of 8 KeePassLib cells and in both KeePassXC directions; a narrowing still holds as a limit in all 8, a first grant gives nothing in the 6 | scenarios 01, 02 |
| An entry's reference deleted by hand in one copy | gone in 6 of 8 KeePassLib cells and in both KeePassXC directions; the narrowing holds as a limit in all 8 | scenario 12 |
| The same at a tie | gone in 4 cells, kept in 4, by direction | scenario 03 |
| A rule changed in both copies | the narrower, in all 8 cells | scenario 05 |
| A first grant on a group while the other copy adds an entry to it | link and rule arrive, the grant in force for the new entry | 8 KeePassLib cells, 2 per KeePassXC release |
| `PwEntry.Duplicate`, `PwGroup.Duplicate`, the clipboard's write and read, Create new IDs | the copy carries the original's anchor under a new identifier: a copied narrowed entry is limited to nothing, a copied opened group opens nothing, and a copied sealed group stays sealed, shown by its state at the root and, in a granting group, by an answer | `design\`; `survival\` C1a |
| The phone programs | custom strings, entries and plugin groups kept by every operation run; a string on a user's entry follows the entry in a merge, section 2 | `spikes\storage\android\RUN.md` |

Every file of the KeePassLib runs is KDBX 4.0 (184 files); of the 356 files of the KeePassXC runs
two are 4.1, the merge that moved a link, in 2.7.12 and 2.8.0.

**Sequences that end wider.**

- **A narrowing pressed while a save runs.** With the plugin's groups written first, a group's
  rule narrowed from inside the save's progress callback leaves the file holding the older, wider
  rule while memory holds the narrower; the save completes and clears `Modified`. With the plugin's
  groups written last, memory and file agree. `security-and-fit\`, `midsave`, in a console process.
- **A copy stripped of its reference.** A sealed group copied by a drag into an open group, the
  copy's entry given a new secret, then Delete Duplicate Entries: 1 entry deleted, the copy's link,
  and the edited entry answers list and read with nothing refusing. The copy's link deleted by hand
  gives the same, and so does deleting `KPG.Anchor` from a duplicated narrowed entry. A copy made by
  Duplicate Group, whose titles gain `- Copy`, loses nothing to Delete Duplicate Entries (0
  deleted). `survival\` C1b, C1c, C1d, C2.
- **The newest version and its record lost.** KeePassLib Synchronize with a stale copy still
  refuses; `keepassxc-cli`, four releases in the design's run and 2.4.2 and 2.8.0 in the survival
  critique's, puts the older, wider rule in force. With both plugin groups deleted and the
  deletion records cleared, a Synchronize gives the same result as KeePassXC. `design\`;
  `survival\` C3, C3b, C3c.
- **Version, record and reference all removed**: the node inherits read and list. `design\` h10.
- **A removed group that returns.** A ruled group holding only its link, removed with the group's
  deletion record only, comes back holding its link after a Synchronize in both directions; with
  the link's record too, it stays gone. The probe prints the group's return and no verdict.
  `security-and-fit\`, `delgroup`, under KeePassLib Synchronize only.
- **A plant from a foreign file.** Rule entries alone grant nothing under four methods. With a
  link inside a group carrying the target group's identifier, an entry answers read, list and
  protected under Synchronize, Keep existing and Overwrite existing. A planted anchor lands in the
  entry's history in 5 of the 6 merges run (3 of 4 KeePassLib methods and both KeePassXC
  releases), and after a simulated Confirm the entry answers list, protected and read with the
  victim's password intact; under Overwrite existing the foreign entry arrives whole, confirmed
  with no press, and the password is not the victim's. `design\`; `security-and-fit\`,
  `plant-confirm`.

**Sequences that end refusing or with a grant waiting.** A rule entry deleted for good in one
copy refuses in 5 of 8 KeePassLib cells (scenario 10). By hand in a single file, a reference made
text that is no anchor, an anchor nothing carries, a rule entry's `KPG.Data` edited, a rule entry
removed with its record kept, a rule entry and its record removed, and the rules group or both
plugin groups removed each refuse there and below (h01, h02, h07, h08, h09, h11, h12). The newest
version lost refuses before any merge and after a stale copy under KeePassLib Synchronize and all
four KeePassXC releases (`design\`); with its record lost too, every KeePassLib method still
refuses (`survival\` C3), and with both plugin groups deleted every ruled node refuses, a
Synchronize with the stale copy included (C3b). A first grant followed by a later edit of the
entry elsewhere gives nothing until confirmed (6 of 8 cells); with a phone's clock a day ahead, a
Confirm is undone by the next Synchronize in either direction until KeePass's clock passes it
(`survival\` C4). The rules group deleted for good in one copy and one Synchronize either way
leave 0 rule texts in both copies and every ruled node refusing; a `keepassxc-cli` 2.8.0 merge of
the intact copy heals it (C5). Create new IDs then Delete Duplicate Entries deletes all twelve
originals and leaves no intact version (C6). Entries pasted from a sealed group, or an entry made
from a link as a template, close the group they land in; a ruled node copied into a second
database refuses; Delete Duplicate Entries after a group is duplicated beside itself deletes 4
entries and leaves the group with 0 entries and its grant unconfirmed (`security-and-fit\`,
`paste`, `template`, `otherdb`, `dedup`).

**Costs.** A record holds 272 characters of names and values for a first version, and a first
version with a short rule 290 to 317. At `a42e38aa`, 18 host test files name the rule's custom
data key.

### 4.6 `outside-file`

The authority document and a 32-byte store key stay in the database's own custom data, written
once and never changed. Everything that changes moves to a folder beside the file, a file per
gate installation sealed with AES-256-GCM under the store key, joined at load with no clock; a
writer's file that is missing, changed or older than a file seen before closes the store. It
covers all data. Probes: `outside-file`, in `design\` and `survival\`; the security critique ran
none.

**Survival of the two keys in the file.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, five methods, eight ordered pairs | both keys present with their values | 38 of 40 cells; the other 2 are Keep existing into a copy that lacks them, which copies nothing |
| KeePass 2.62, keys written without times | kept where the open copy held them, and arriving in a copy that lacked them under every method but Keep existing | 15 of 15; 4 of 5 |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, save | both keys kept, their times dropped, KDBX 4.0; `_LAST_MODIFIED` added, and `KPXC_RANDOM_SLUG` from 2.7.12 | 4 of 4 |
| KeePassXC, merge, both copies holding the keys | kept | 12 of 12 |
| KeePassXC, merge from a stale copy whose custom data is empty | the target's keys kept | 8 of 8 |
| KeePassXC, merge into a copy that lacks the keys | the keys arrive; a key only that copy held is removed | 8 of 8; 4 of 4 |
| KeePassXC, merge from a stale copy that lacks the keys and holds another key | **both keys removed** | 8 of 8, four releases |
| KeePassXC 2.4.2, 2.6.6, a stale copy with no custom data that the release itself saved first | nothing added by the save; both keys kept by the merge | 2 of 2 |
| KeePassXC 2.7.12, 2.8.0, the same | the save adds `KPXC_RANDOM_SLUG` and `_LAST_MODIFIED` to the empty dictionary, and the merge then **removes both keys**; the merge report prints each removed key with its value, the store key in clear | 2 of 2 |

Every KeePassXC merge here ran under a slow key derivation, so that its two loads fell in
different seconds (5.2 to 13.6 s a merge); no run varies the gap. A KeePass merge under every
method but Keep existing brings the keys back to a copy that never had custom data; a copy that a
KeePassXC merge emptied was not merged back.

**The store's prototype.** Its 21 checks hold: a revocation made while the other installation was
behind holds on both and cannot be taken away by that installation's own writes; a name added on
one installation and taken off on the other is off; a rule changed on both refuses on both until
settled; a writer's file put back older, deleted, changed by a bit, renamed or opened under
another key closes the store. The synchronizer in these checks is the probe's own, which copies
the file with the higher count between two writers of distinct names.

**Sequences that end wider**, each in the survival critique's executable model of the design's
text over a modeled synchronizer; no real synchronizer ran.

- **The same writer name on two computers.** After a conflict the first computer is closed, and
  the second, a new third computer with no seen file, and the first after taking a new name all
  admit a client the first had revoked; the revocation is only in the conflict copy, which nothing
  reads. `survival\`, sequence 1.
- **A fast clock.** Once an installation whose clock is two months ahead writes anything, the
  other installation admits a client the first had revoked; with the clock right it refuses.
  `survival\`, sequence 2.
- **A single writer's file rolled back.** On a computer with no seen file, the previous file of
  one writer beside the current file of the other passes every check and serves read and list.
  `survival\`, sequence 3.

Not run: a holder of the store key sealing a writer's file again without a revocation, which the
design's own text concedes and no probe writes.

**Sequences that end refusing.** Every case of the 21 checks that closes the store; in the
survival model, an installation that had loaded a writer's newer file, given the older file
(`survival\`, sequence 3); and by the design's reading, every KeePassXC merge that removes the
two keys.

**Costs.** 200 rules, 30 records, 30 revocations and 10 names over three writers are 81,764 bytes
in three files. A load and join took 1.889 ms in the record and 2.22 to 2.48 ms over five later
runs; the look at the folder per request 50.2 microseconds and 58 to 69; a write with both loads
18.60 ms and 20.3 to 21.4, on local NTFS. `File.Replace` is refused while a reader holds the file
without sharing deletion, measured in PowerShell 7 on .NET 11 and not on .NET Framework.

### 4.7 `hardened-custom-data`

All KPG data stays in custom data, and each database-level key is also copied into the root
group's custom data (the mirror), the two read as a union. Every key is written once, with no
per-key time and no empty value, so a change is a new numbered key. Rules stay on their node with
a generation member, and each version is also recorded in a ledger of keys; a node agrees, is
behind (the recorded head is in force) or disagrees (only narrowing counts). It covers all data.
Probes: `hardened-custom-data`, in `design\`, `survival\` and `security-and-fit\`. None is the
plugin: the first two measure the storage, and the security probe applies the design's three
states in a function of its own.

**Survival.** The stale copy holds 7 `KPG.` keys in each place and the newer copy 12; their union
is 14.

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, the layout saved | KDBX 4.0 with timeless keys in both places, 4.1 with timed keys | `design\` |
| KeePass 2.62, Synchronize | the dictionary holds all 14 keys, none removed, no key with two values | 6 of 6 cells |
| KeePass 2.62, Synchronize, the mirror | the arriving copy's at equal root times; the stale copy's in both directions when its root group is later; empty in both directions when the stale copy's custom data was deleted | 6 cells |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, merge, slow key derivation | the dictionary replaced by the source's, the mirror the target's, so the union still holds 14 of 14; the root group's time unchanged; KDBX 4.0 | 8 of 8 merges |
| KeePassXC 2.4.2, 2.8.0, merge, both loads in the same second | the `KPG.` keys of both places unchanged: a stale target holds 7 of 14 | 4 merges |
| KeePassXC, merge into the stale copy | the group keeps its wide rule under the newer copy's time; the entry takes the newer rule | 4 of 4 releases |
| KeePassXC 2.4.2, 2.8.0, save | all 12 keys kept in both places; the root group takes the save's time | 2 of 2 |
| KeePassXC 2.8.0, save or merge of a file made 4.1 by another feature | every `KPG.` key of the dictionary carries a time afterwards | `survival\` M1; `security-and-fit\` S5, S5b |
| KeePassDroid 2.7 | deletes entry and group custom data at every save, section 2.3; the root group's own custom data was not on the phone bases | `spikes\storage\android\RUN.md` |

**Sequences that end wider.**

- **A single KeePassXC merge whose two loads fall in the same second**: a stale target stays
  stale in both places (7 of 14), while the entry takes the newer rule and the group keeps the
  wider. `design\`, the fast cells.
- **A KeePassXC merge, then a KeePass Synchronize whose winning root group is stale.** After
  KeePassXC 2.8.0 merges the stale copy into the newer copy, the newer keys live only in the
  mirror. A Synchronize in which the stale mirror wins, by a later root time (the stale copy saved
  by KeePassXC, in either direction) or by arriving at equal root times (the stale copy into the
  merged copy), then leaves 7 of 14 keys in those 3 of the 5 cells run without the reconcile: the
  revoked client is admitted, the retired authority is present and not retired, and the removed
  profile is in force. Running the design's reconcile first rescues the cell with the merged copy
  open (14) and not the cell with it arriving (7). `survival\`, F1.
- **Two copies editing the same rule.** After a Synchronize either way the node holds the
  generation 2 text of the copy that edited last, and the ledger holds a generation 3 from the
  copy that edited more; by the security probe's coding of the design's states the node is behind
  and generation 3, the wider rule, is in force. `survival\` S1; `security-and-fit\` S1.
- **A ledger pruned after a delete, then a `keepassxc-cli` 2.8.0 merge**: the node comes back
  with generation 1, which agrees and is in force. With the ledger kept, generation 2 is in force.
  `security-and-fit\` S2.
- **A profile removed and added again in one copy and removed in another**: the union holds
  `in.1`, `in.2` and `out.1`, and the profile is in force. `security-and-fit\` S4.
- **Create new IDs of a copy** leaves a group under the root holding a copy of the authority,
  which Delete authority does not reach. `security-and-fit\` S3.

**Sequences that end refusing.** None was run with the gate's answer. In the Synchronize cells in
which the stale copy's root and node custom data were deleted first, the root's own rule is gone
in both directions and the ledger holds no version of it (`design\` D4). That every grant is off
after a KeePassDroid save follows from the measured deletion of node custom data and the design's
states.

**Costs.** None measured beyond how long the `keepassxc-cli` runs took under the slow key
derivation: the design's eight merges 7.9 to 11.0 s recorded and 9.9 to 14.0 s later (`design\`
D9), the security probe's six merges 7.4 to 18.2 s and 6.7 to 12.1 s (A6), and the survival
probe's two merges and a save 12.1 to 82.8 s and 6.0 to 10.1 s (V11). A 2.8.0 merge prints each
dictionary key it removes or adds with its value.

### 4.8 `versioned-fields`

Every plugin write is a new entry in a group of the plugin's own, never changed afterwards,
holding JSON versions in custom strings named from a time, an origin and a counter; the name is
the identity, and no modification time decides anything. A version lists what it replaces, and
versions that do not name each other are reconciled at every read: a refusal wins, a removal
wins, two rules give what both allow. A write that closes something is a pair of state entries,
each holding everything in force, and the plugin deletes older write entries with deletion
records. A rule also has a copy on its node, so a copied node refuses until settled. It covers all
data. Probes: `versioned-fields`, in `design\`, `survival\` and `security-and-fit\`. The design's
probe reads a node's history always; the survival critique's also applies the rule as the design's
text has it.

The option as first worded, timestamp-named fields in a single shared entry copied back out of
history, is the experiment X13 of `ground-keepass`, in section 2.1.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, four methods, and KeePassXC, four releases, both ways | an entry only one copy holds arrives; two entries added in two copies both arrive | 160 of 160 merges |
| The same, a revocation and a narrowing against a stale copy | the revoked client refused, the narrowing kept, the stale copy's newer rule carried | 16 of 16 cells, twice |
| The same, a rule or a profile changed in both copies | both versions held and the node answers what both allow; the profile is off | 16 of 16 cells each |
| KeePassXC, save | every write entry, field, protection flag, marker, time and all ten deletion records kept; KDBX 4.0 | 4 of 4 releases; 182 of 182 files at 4.0 |
| KeePassXC, merge | never applies or copies a deletion record: four entries the plugin deleted come back beside its ten records, every verdict as it was; a KeePass Synchronize removes them again | 4 releases; the chain |
| KeePass 2.62, four methods, a deletion record dated at the entry's own time or a second before | the entry stays and the record is dropped | 16 of 16 cells |
| The same, a deletion record dated a second after | the entry removed by every method in the copy that deleted, and by Synchronize in the other | 8 cells |
| Versions of another store planted in a user's group | change nothing | `design\` F1 |
| Duplicate Group, Create new IDs | every copied ruled node refuses; copies of write entries under new identifiers are not read | `design\` |

**Sequences that end wider.**

- **An import with Create new IDs.** The revoking copy imported into the stale copy: the revoked
  client is admitted and the stale copy keeps its wider rule, where a Synchronize refuses the client
  and narrows the rule. With a recreated authority imported the same way, the old authority serves
  and every client paired under it is admitted. Today's store through the same import holds the
  revocation and the retirement under Create new IDs as under Synchronize. With closing versions
  read from the unmarked copies too, the stale copy keeps the narrowing in all three imports and
  refuses the client after the imports of the revoking copy; after the recreated import it holds the
  old authority retired, and the probe, which does not apply the authority a certificate chains to,
  lists that authority's clients admitted. `survival\` P1.
- **The state entries destroyed by hand.** With both entries of a revocation pair, or the group,
  destroyed in one copy, merging it into the stale copy by a method that carries no deletion
  records leaves the client admitted: 21 cells, 7 each of three scenarios. `design\` H2, H5, H6.
- **Both newest state entries deleted without a record, then a merge of the stale copy**, under
  the design's own history rule: `keepassxc-cli` 2.4.2 and 2.8.0 into the narrowing copy give
  read, list and update where read was decided, and so do Overwrite if newer, Keep existing and
  both releases into the stale copy. The critique's refined condition gives read in both KeePassXC
  cells into the narrowing copy and leaves the cells into the stale copy as they were.
  `survival\` P3.
- **The Data page's press after a permanent delete.** A narrowed entry deleted for good, the press
  that removes the rules of nodes that are gone, then `keepassxc-cli` 2.4.2 or 2.8.0 merging the
  stale copy in: the entry is back with read, list and update. Without the press the same merge
  keeps read; KeePassLib's four methods keep the entry deleted. `survival\` P4.

**Sequences that end refusing.** A permanent delete of a single write entry with a deletion record
(H4), or of the whole group with a record for each entry (H6), closes the whole gate in 9 of 16
cells each, every merge into the deleting copy and Synchronize into the other. A file that lost both
state entries admits nobody (H5); merged with the stale copy it still refuses a client nobody
revoked, with a profile off, in every merge into it and in a Synchronize into the stale copy, 9 of
16 cells, and the file that lost both entries of the revocation (H2) leaves the profile off in the
same 9 cells. Delete Duplicate Entries after an import with new identifiers deleted four entries,
all originals, the authority's entry and the group's marker among them, and the gate then serves
nothing. Every copy of a ruled node refuses.

**Growth and costs.** Over eight renewals merged by KeePassXC the store grew from 3 write entries,
11 version fields and 6,481 characters to 19 entries, 179 fields and 62,297 characters, with the
deletion records going from 10 to 90, and every verdict stayed right; merged by KeePass's
Synchronize it stayed at 3 entries (29 fields, 11,475 characters). `survival\` P2. The stand-in
versions are 3,063 characters for an authority in a protected string, 857 for an admission record
and 205 for a revocation (`design\`, `dumps.txt`). Finding the plugin's entries by identifier took
0.133 ms at 10,030 entries and 0.955 ms at 50,030 in the record, and a lookup of `KPG.Rule` on every
user entry 1.513 ms and 12.043 ms; over seven later runs the last was 13.323 to 16.895 ms. The walk
ran in a console process (`design\`, `walk.txt`).

### 4.9 `wildcard`

All KPG data is a single JSON document, a snapshot, in a protected custom string of an entry in
a group of the plugin's own, sealed by a MAC under a key kept inside it. A snapshot is never
changed: each plugin write adds a snapshot holding the whole new state and deletes the snapshots
it replaces. After a merge the gate serves the join of the surviving snapshots. Revocations and
retirements are also written as deletion records that name nothing, the witnesses; a snapshot
lost without account makes the store behind, and no snapshot at all makes it gone. It covers all
data. Probes: `wildcard`, in `design\`, `survival\` and `security-and-fit\`.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, the four methods that match identifiers, sets U, C, R, D and K | in force, with the same joined state in both directions: every revocation, the retirement, the profile taken off and both rule documents kept | 40 of 40 cells |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, the same five sets | in force, the joined state equal to KeePass's; the replaced snapshot back beside its record | 40 of 40 cells |
| kdbxweb 2.1.1, sets U, C, H, D and K | verdict and joined state equal to KeePass's Synchronize | 10 of 10 cells |
| Deletion records | Synchronize carries a record to the other copy; KeePass's other four methods and all four KeePassXC releases copy none; kdbxweb takes the union | set U; set H |
| Two copies that each made a store | undecided, two stores | 16 of 16 cells |
| KeePass 2.62, Create new IDs | undecided in 12 of 14 cells; behind in 1; in the fourteenth the source held no snapshot | 14 cells |
| A seal over 1 MiB of protected string | holds after KeePassLib 2.62 and after `keepassxc-cli add` in four releases | 1,052,608 characters |
| KeePass 2.47 | keeps the entry, the seal on 5 KB and on 1 MiB, every record and witness, in a KDBX 3.1 file | 3 files |
| pykeepass and kdbxweb, save; the record names no pykeepass version, and the later run resolved 4.2.0 | the same kept; KeePass 2.62 opens the file kdbxweb saved | 1 file each |
| A deletion record that names nothing | survives a save by four KeePassXC releases, pykeepass and kdbxweb | `design\` |
| `keepassxc-cli` 2.6.6 and 2.8.0, the snapshot moved to another group or to the recycle bin | in force; 2.8.0's `mv` and `rm` write KDBX 4.1 | 4 of 4 |
| The phone programs | a protected JSON string, a dedicated group with its flags and deletion records that name nothing kept by every operation run, section 2; the largest value on the phone bases is 2,861 characters | `spikes\storage\android\RUN.md` |

The sets: in U a copy revokes a client and so replaces the snapshot; in C both copies write; R is U
with the new snapshot in the recycle bin; D is U with the old snapshot's text edited by hand in the
other copy; in K a copy recreates the authority while the other pairs a client; in X each copy makes
a store of its own. **Set H, which is U with the new snapshot then deleted for good, is not among
the five sets of the first two rows**: its 18 cells are below: 8 end wider, 9 end with the store
gone and 1, under Create new IDs, undecided.

The join's laws held in 40,000 checks over random histories, as the probe codes the join, on rule
numbers alone and without prunes (`design\`, `lattice.txt`).

**Sequences that end wider.**

- **The newest snapshot deleted for good, then merged into the stale copy without its record.**
  KeePass's three import methods, Create new IDs and the four KeePassXC releases into the stale
  copy leave it in force at its older snapshot, **admitting the client the other copy revoked**,
  in 8 of the 18 cells of set H: neither the newer state nor its witness arrives. `design\`, set H.
- **A deletion with no record.** pykeepass deletes the newest snapshot of a file that took it
  through Overwrite if newer: the store is in force at the older snapshot and nothing is refused.
  `design\`.
- **The witness never arrives, then the newest snapshot is deleted for good.** After a KeePassXC
  2.4.2 or 2.8.0 merge or an Overwrite if newer, the stale copy holds both snapshots and no record,
  and the client is refused by the newer snapshot; with that snapshot deleted for good the store
  is behind at the older state, the client not revoked and no witness. `survival\` e3. What the
  design's "Use what is here" button does from there is read from the design and not run.
- **Delete Empty Groups, then kdbxweb.** With a head snapshot in the recycle bin and the store
  group removed by Delete Empty Groups, a kdbxweb merge in either direction reads in force with
  one copy's state alone: the profile is on again and the entry carries that copy's rule, the
  other copy's snapshot gone with no record of its own. `survival\` e4.
- **A witness outranked.** A deletion record at +10 s and an entry under its identifier at +20 s
  in the other copy: after Synchronize in both directions the record is gone and the entry
  present. `security-and-fit\` P2.

**Sequences that end refusing.** The newest snapshot deleted for good: gone into the deleting
copy by KeePass's four methods and the four KeePassXC releases, into the other copy by
Synchronize, both ways by kdbxweb, after `keepassxc-cli` 2.6.6 or 2.8.0 removed it twice, with
and without the stale copy merged in after, and at the end of the four chains of set H; in every
gone line of the runs that had revoked a client, the revoked client is still refused by its
witness. After Delete Empty Groups and the plugin's next write, kdbxweb in either direction leaves
no snapshot (`survival\` e1); a store made again under the reused group identifier is left out by
kdbxweb (`survival\` e2). After a self-import with Create new IDs, Delete Duplicate Entries
removed 3 entries, the original snapshot under its own identifier among them, and left the copy
(`security-and-fit\` P1).

**Costs.** The base snapshot is 4,968 characters, and with 22 records and 100 rules 37,299. The
whole pass by identifier at 50,000 entries took 2.454 ms in the record and 1.5 to 2.3 ms in five
later runs; the pass that also looks for the string 29.759 ms and 20.2 to 23.3 ms, and reading
and checking a seal 0.037 ms and 0.035 to 0.042 ms, in eight later runs. `design\`, `bench.txt`;
the bench ran in a console process.

### 4.10 `single-entry`

A single ordinary entry, under a fixed identifier in a group of the plugin's own, holds the
whole state as a protected custom string. The plugin rewrites it in place with no history item of
its own, so that every merge takes it whole. Because the whole is chosen by modification time,
each version carries an identifier, a window of its ancestors and a stamp per datum; a step at
every read weighs the current value, the history and the plugin's memory, then restores the
newest version or merges two heads, narrowing where both changed. A witness entry per version
turns a lost version into `503 undecided`. It covers all data. Probes: `single-entry`, in
`design\`, `survival\` and `security-and-fit\`, all PowerShell over KeePassLib 2.62; the document
in them is unprotected and in a format of the probe's own.

**Survival.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, four methods, and KeePassXC, four releases, both ways | the document is never a mixture: a single version whole in every cell | 64 and 64 cells |
| KeePass 2.62 without the plugin's memory | the step ends complete in 47 cells and refuses in 17; never a stale document | 64 cells |
| KeePass 2.62 with the plugin's memory | complete in 54, refused in 10: the eight Keep existing imports and two cells where the loser was dropped behind later history items | 64 cells |
| KeePassXC, at the next load | complete in 48, refused in 16: 8 at a tie and 8 where the loser was kept nowhere behind ten history items | 64 cells |
| A tie | a version is kept nowhere but under Overwrite existing, which keeps the loser as a history item; KeePass and KeePassXC keep opposite sides | scenario S3 |
| A lost version's witness | still arrives, in every cell that lost a version | the 128 cells |
| Twelve rounds synchronized in KeePass with the plugin's memory, 5 s apart either way or in the same second | 24 of 24 data kept, no refusal | chains 1 to 3 |
| The same under a history limit of 0 | 24 of 24 kept when the arriving version is the later; 12 of 24 and 12 refusals when it is the earlier, the merge's own trim removing the loser | chains 4, 5 |
| Twelve rounds in the same second through `keepassxc-cli` 2.8.0, or through KeePassLib with no plugin | 12 of 24 kept, each loss refused at the next load | chains 10, 7 |
| The same 5 s apart, KeePassXC in either order and KeePassLib in a single order | 24 of 24 kept, no refusal | chains 8, 9, 6 |
| A document of 7,000,000 characters under the default 6 MiB history size | the loser gone after the save, refused; 100,000 and 1,000,000 characters keep it | `design\` |
| The phone programs | a protected string kept through every operation run; both merging programs take the newer entry whole, section 2.3; the largest value on the phone bases is 2,861 characters | `spikes\storage\android\RUN.md` |

**Sequences that end wider.**

- **A crafted file merged in.** A foreign file with no authority, holding only a document that
  adds a rule on a node with none: under Synchronize in either order, Overwrite if newer in either
  order, and Keep existing with the foreign copy earlier, the design's own step takes it as a
  concurrent head and serves the planted rule, 5 of 6 cells; the sixth refuses.
  `security-and-fit\`, `Plant.ps1`.
- **A copy more than a window behind.** At 300 versions under a window of 256, the probe's merge
  brings a removed profile back, serves the meet where the other copy had widened a rule and
  brings back a rule that copy had removed; the reading most favorable to the design's text
  refuses two nodes; judging heads by generation serves the newest version. For a concurrent
  narrowing, judging by generation serves the wider rule with no refusal. At 20 versions all three
  readings serve the newest version. The design's own probe used a window of 64 and never passed
  25 generations. `survival\` W1, W1b.
- **A copy that wrote nothing, 70 versions behind under the probe's window of 64.** Merged by
  Synchronize or Overwrite if newer in either direction, with and without the plugin's memory,
  the step takes the old version for a second head, and the profile the other copy took off is
  on again in every cell; at 10 versions every cell serves the newer data.
  `security-and-fit\`, part W.
- **Continue, then the lost version turns up.** After a tie merged without the plugin, the
  design's Continue, and a later synchronize with an older copy, a narrowing is discarded; with
  Continue written as an acknowledgment both changes are restored. `survival\` C1.

**Sequences that end refusing.** A tie merged without the plugin, in 6 of the 8 KeePass cells
(Overwrite existing keeps the loser as a history item and the step completes) and in all 8 KeePassXC
cells; a Keep existing import in the 8 of its 16 cells that lost a version; a history limit of 0
when the arriving version is the earlier. After 12 `keepassxc-cli` 2.8.0 merges over 300 versions,
the file that received them holds 13 witnesses and refuses, naming two versions as missing, while
the other file holds 300 deletion records and serves the newest version (`survival\` W2). The state
entry deleted for good is gone from both copies after a Synchronize when the other copy did not
write later, and a plain synchronize with a backup does not bring it back; kdbxweb 2.1.1 in either
direction leaves a restored state entry absent (K1). After Create new IDs, Delete Duplicate Entries
removes three entries, the state entry under the fixed identifier among them, and no readable
version is left (`survival\` D1; `security-and-fit\` part D).

**Costs.** A look-up by identifier among 10,002 entries took 0.025 ms with the store group first and
0.120 ms with it last in the record, and 0.007 to 0.009 ms and 0.097 to 0.106 ms later, in a
PowerShell process (`design\`, `rounds.txt`). The cost of parsing and merging a document in the
plugin was not measured.

### 4.11 `grant-only`

A rule is a row of node, profile and `CRUSDLAP` mask, and rows only add: a verb is permitted
when some node on the path to the root holds it, with no `default`, no seal, no empty mask and no
nearest wins. A grant is an entry written once in the plugin's store group; taking a grant away
is a withdrawal entry, counted wherever found, with a second witness as a key in the database's
custom data. Nothing sits on a user's node, so a node is kept from a profile only by where it
sits, and a move is a policy change. A middle form adds a mark that travels with its node. It
covers the rules only. Probes: `grant-only`, in `design\`, `survival\` and `security-and-fit\`;
the design's and the security critique's compile the product's own resolver at `a42e38aa`.

**The model**, 400 generated trees of 30 nodes, answered by the product's `AclResolver` and by
the grant-only resolver. `design\`.

| Event | Today's model, wider in | Grant-only, wider in |
| --- | --- | --- |
| a single rule lost | 1,025 of 3,175 | 0 of 3,175 |
| a node's document replaced by a random other document, which the probe calls a rollback (section 4.15) | 2,428 of 2,802 | 2,280 of 2,802, none without the older row holding a letter the newer lacks |
| a copy beside its original | 1,552 of 4,428 | 0 of 4,428 |
| a node displaced to the root | 494 of 8,576 | 0 of 8,576 |

On rule sets with no narrowing the two models answer alike in 648,000 answers. Today's documents
read as grants permit 13,442 answers that today's model refuses, in 291 of the 400 trees. In the
middle form, of 1,723 marks: a lost grant, a lost record whose node still refers to it, and a copy
that carries the reference widen in none; record and reference both lost widen in 847, a copy
without the reference in 541, and a node displaced out of a marked group in 91 of 300.

**Survival of the records.**

| Program | What the probes show | Count |
| --- | --- | --- |
| KeePass 2.62, five methods both ways | two grants unite; a withdrawn grant stays away; a foreign file's grant on another node opens nothing, and a foreign file's withdrawal closes the group | 150 cells, 15 scenarios |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0, `merge -s` | the same verdict from all four releases; a deleted grant entry comes back and stays cut beside its withdrawal | 136 cells, 17 scenarios |
| KeePassXC, save | every record's identifier, strings, flags and times kept | 4 of 4 releases |
| A withdrawal's entry deleted by hand, KeePass | with the key, the grant stays cut in 9 of 10 cells, the tenth Keep existing into the stale copy; without the key it is back in 5 of 10 | scenario 06 |
| The same, KeePassXC | with the key, cut in 16 of 16; without it, back in all 16 | scenarios 06, 16 |
| The same after the stale copy was saved by KeePassXC first | 2.8.0 under the slow key derivation into the withdrawing copy removes the key, and 2.8.0 under the fast derivation does not carry it to the other copy: the grant is in force again in both; the other cells stay cut | 8 cells; a third cell came out differently in 1 of 6 later runs |
| Create new IDs | copies of grant records name the original nodes and change nothing; copied nodes have no grant | 30 of 30 cells |
| The phone programs | entries and custom strings kept by every operation run, section 2.3; the exact record shapes were not on the phone bases | `spikes\storage\android\RUN.md` |

The KeePassLib results are KDBX 4.0 without a withdrawal key and 4.1 with a key: of 198
databases, 62 with a key, all 4.1, and 136 without, all 4.0, by a check written on the branch,
whose output `design\out\ver-all.txt` has no record. KeePassXC wrote merged copies holding a key
as 4.0 too: every target of 2.4.2 and 2.6.6, and copies of 2.8.0 in the extra run.

**Sequences that end wider.**

- **Two grants on the same row from two copies.** Each copy adds a letter, in one copy a switch:
  after any merge the profile reads a protected value, or creates such a value, that neither
  copy alone allowed, in 36 of 36 cells. `survival\` S1, S1b. Under the design's fold, a row
  that a copy cut back while another copy added a letter to it comes out wider than the meet of
  the two in each of 36 cells; under the security critique's cap fold it is the meet in all 36.
  `security-and-fit\` N1 to N3.
- **A place from one copy and a grant from the other.** One copy files an entry under an
  ungranted group and the other grants that group: the profile reads the entry in 14 of 18 cells,
  6 of 10 under KeePassLib and 8 of 8 under KeePassXC. `survival\` S2.
- **A move through KeePassXC, both ways.** One copy moves an entry out while the other edits it:
  KeePassXC keeps the entry in its new group with the other copy's location time, the merge back
  puts it into the old group at equal times, and the profile reads it, 4 of 4 releases.
  `survival\` S3.
- **Moves under KeePass's own methods.** Synchronize carries a move, and of two moves of the same
  node the later move wins; Overwrite if newer, Overwrite existing and Keep existing
  leave the node where the open copy had it. `design\` scenarios 09 to 13.
- **A node deleted to take it away, then a KeePassXC merge of an older copy**: the node is back,
  in both directions. `design\` scenario 14.
- **A grant deleted or recycled by hand.** After a deletion, KeePassXC brings the grant back in
  both directions (8 of 8), Create new IDs leaves it open both ways, and the three import methods
  leave it open into the stale copy; Synchronize closes it both ways. After a recycle, the three
  import methods and the four KeePassXC releases into the stale copy leave it open, Create new IDs
  leaves it open both ways and Synchronize closes it both ways, 9 of 18 cells open.
  `survival\` S4, S4b.
- **A grant record lost under the strip guard.** With `C.......` on the root and `...S....` on a
  group, the guard refuses stripping the protected password of an entry in the group; with the
  root's grant record deleted by hand, leaving no deletion record, the same strip is permitted.
  What it permits is what `...S....` grants. `security-and-fit\`, the strip guard.
- **The worked example.** On an invented tree of 26 entries under a single policy, a group
  dragged under another (E3) leaves its entry read by a profile the policy keeps from it under
  both grant-only arrangements, where today's rules and the middle form keep it closed; an entry
  moved into another project (E4) is read by a profile the policy keeps from it in the tree split
  for grant-only. `security-and-fit\`, the worked example.
- **After an upgrade, the root granted again.** A database in the shape of the 0.1.0 store, read
  by the grant-only fold, grants nothing; once the root is granted again everything is reached,
  an entry the old rules refused included. `security-and-fit\`.

**Sequences that end refusing.** None: a lost grant grants less in every answer of the resolver.
After Create new IDs, Delete Duplicate Entries deleted the originals and left copies with no
grant.

**Costs.** The fold over 10,070 entries with 60 grants and 10 withdrawals took 4.84 ms in the
record and 4.84 and 3.80 ms later, in a console process built without optimization (`design\`,
`keepass.txt`). At `a42e38aa` the product's tests hold 18 uses of `Rules.Sealing(` in 8 files, 22
rules with an empty mask in 8 files and 274 uses of `Rules.Granting(` in 32 files
(`design\scripts\search.ps1`).

### 4.12 `crdt`

Every datum is an ordinary entry written once, in a group of the plugin's own, its identifier a
keyed digest of its content, under a MAC keyed by a store key kept in the authority's document.
Authorities and records form a two-phase set with final retirements and revocations, profile
names a remove-wins set, and rules, renewal answers and node marks registers whose concurrent
versions meet as the narrower; "later" is a list of predecessors, never a clock. Each gate write
adds a head that carries every refusal, removal and standing version, and each revocation and
retirement a deletion record as a witness. A missing head, damage or an unexplained record makes
the store incomplete, serving nothing until a press. It covers all data. The design's probe is an
executable model, `spikes\storage\model\`; the formal critique wrote a second model from the
design's text, `spikes\storage\model\critic\`; the survival critique ran the design's model, an
extension of it, and a bridge that applies the design's read to real files changed through
KeePassLib 2.62 and `keepassxc-cli`, in the family `crdt`. No model runs a program other than
itself, and in the bridge the recycle, the unchanged save and the emptied bin are done through
KeePassLib's object model, not by the commands they stand for.

**What the models found.**

| Model | Finding | Count |
| --- | --- | --- |
| the design's | merges alone, by every modeled program in any order: no copy wider than the ideal, all copies agreeing | no failure in that configuration; the run holds 12 configurations, each with 20,000 random histories over two copies and 20,000 over three, 480,000 in all, and 127,522 exhaustive histories |
| the design's | Synchronize and Keepass2Android only, with deletions of any kind, hand edits, imports and planted entries: no failure | 6 of 6 cells |
| the design's | with merges that carry no deletion record, and deletions: `safety` failures of two shapes, both carriers of a decision deleted where it was made and then such a merge (C, D, E, H), and a head deleted with a record dated before it, a KeePassDX merge, then the decision's version deleted with no record (I); beside them a copy stripped of its reference fails `copy-cap` (G) and a whole file written over another rolls back (H, I) | the cells C, D, E, G, H, I of its table |
| the design's | the 20 replayed sequences: 11 hold, 5 hold with the store incomplete, 1 holds with nothing served, 3 fail | scenarios 3, 9 and 16 fail |
| the design's, unchanged, on a history its search did not draw | revoke, delete the revocation with a record, a KeePassXC merge into the other copy, delete the newest head there without a record: the revoked client is admitted. Of 72 single deletions after the third step, only that deletion widens | `crdt`, `scripts\p2_history.py`, written on the branch; no output of the critique's own run was kept |
| the formal critique's | the meet over the 512 functions a profile's rule can be is closed, commutative, associative, idempotent, below each argument and monotone, and a narrower parent never widens the child | 0 violations in 200,000 random triples |
| the formal critique's | the remove-wins set and the registers are functions of the union | 0 violations in 20,000 triples |
| the formal critique's | a restored backup written to and merged back by each of five merges: no widening, no revocation undone | 3,600 histories |
| the formal critique's | merges alone never leave a file incomplete | 1,500 histories, three reads |
| the formal critique's | as written, counting an element by where it sits: a Delete and a Synchronize leave both files whole and a node wider (5 steps); a revocation undone in 4 steps and with a Move in each of two copies in 6; a retired authority served; a single Delete of the head serving an older rule (4 steps) | CE1, CE3, CE13, CE4, CE10 |
| the formal critique's | the 64-height window: a copy 65 gate writes behind, 70 steps, both files whole and the node wider | CE5 |
| the formal critique's | with elements counted wherever they sit, every such history holds; with the window dropped as well, three histories still fail: a removal by script beside the older state, and two that fail only under the security critique's new store key at Recreate and hold with the key carried forward, as the design has it | CE6, CE11, CE12 |
| the formal critique's | two files holding the same elements answering differently: 101 pairs in 1,500 histories, 34 with both serving; with elements counted anywhere 3, 1 of them with both serving, the pair printed admitting a client in a file and refusing it in the other | P6 |
| the formal critique's | 56 of 128 meets of two documents are limits that no `AclDocument` states | P7 |
| the formal critique's | random search, 1,500 histories over three files and 1,500 over four per cell: as written, failures in 4 of the 6 runs with bins, moves, deletions with a record and unchanged saves, in a run with every fault but an overwrite and in a run with overwrites. **With elements counted wherever they sit, a single failure is left**, in the configuration with every fault but an overwrite: two deletions that leave no record and KeePassDX merges, ending wider than what the files knew | formal 4.4, configuration F |

**What the bridge against real files showed.** `crdt`, `survival\`.

- In 2,000 random histories over two and three copies, 9,602 real steps through KeePassLib's five
  methods and four KeePassXC releases, each file agreed with the extended model's prediction at
  every step but 19, and no check of the design failed. Of the 19, 13 are an element's
  modification time under Overwrite existing or Keep existing, 5 are a hand-edited head that the
  model predicted would arrive and the file did not take, where the file reads whole and the
  prediction is an incomplete store, and 1 is three entries an import removed.
- **Five transport facts on single files.** An entry and its deletion record in the same second:
  all four methods that match identifiers keep the entry and drop the record, in both directions.
  A head and a version the gate pruned, then saved unchanged at a later time by another program:
  the four methods keep both and drop both records, in the gate's copy too. A recycled head:
  Synchronize puts it into the other copy's bin, the other three methods leave the other copy's
  placement alone, and every KeePassXC release moves it into the other copy's bin. A deletion
  record that names nothing, held by one copy: kept by that copy under all five methods and four
  releases, carried only by Synchronize. Create new IDs then Delete Duplicate Entries deletes four
  originals and keeps the imported copies, and a third copy synchronized with the result loses the
  same originals.
- **The replays, as the design is written.** R1 (5 steps): revoke, delete the revocation's
  entry, empty the bin, a KeePassXC 2.6.6 merge into a stale copy, delete the new head there: that
  copy is whole and admits the revoked client. R2 (4 steps) and R9 (5 steps): a copy whole and the
  root granting read again. R3 (6 steps), with the unchanged save and KeePass only: the gate's own
  copy granting read at the root. R4 (5 steps), two gate copies and Synchronize only: a refused
  renewal asked again.
- **As written**, the extended model fails in 2 of its 12 searches of 8,000 histories, both over
  two copies: with KeePassDroid's unchanged Save added it finds R4's history, and with KeePassXC
  added R2's. **With elements counted wherever they sit**, R1 to R4 and R9 pass on real files,
  and the extended model finds no failure in 32,000 histories over Synchronize, Keepass2Android,
  KeePassXC and the import methods; the 16,000 histories with KeePassDX's merge have a record and
  no kept command, and a second search with other seeds finds no failure. R6 still widens:
  deletion records cleared after an unsealed loss, then a stale copy synchronized. The formal
  critique's model still finds a failure under the same change, in the table above.

**Sequences that end refusing.** In the design's replays, five sequences end with the store
incomplete and a sixth with nothing served; a head deleted for good or a `KPG.` string edited by
hand does the same in the models.

**Costs.** None measured for this shape. The models and the bridge time only themselves: the
design's full run took 596 s in the record and 639 s later.

### 4.13 The composed store, as a whole

`store-as-a-whole`. The store the evaluation composed on 2026-11-16, configured from `EVALUATION.md`
as it then stood: the design of `crdt` (section 4.12) with changes. Every datum is an entry written
once in the plugin's group, holding `KPG.Kind`, `KPG.Data` and a check, `KPG.Mac`, a keyed SHA-256
digest under a store key; an entry counts wherever it sits, the recycle bin included; a head
restates every refusal, removal and standing version in force and names every head it replaces, with
no limit; a damaged store refuses for the whole database until the press Seal again. Beside that: no
mark on a user's node and no witness; a revocation names the client's key and not a certificate; the
store key is carried over a Recreate and held in the retirement after a Delete with no Create; a
rule set in a node's dialog is written at the next save; several gates write; and, as a switch, a
repair after a Synchronize, which writes again, as entries that take access away, whatever was in
force before the merge and is not after it. Run in a copy of the design's model with switches, which
with every switch at its default printed the design's record line for line, and over real files
changed through KeePassLib 2.62 and `keepassxc-cli` of the four releases and read by the store's
read rule. Everything ran twice, and the second run gave the record line for line but times and file
sizes.

**The model.**

| Run | Finding | Count |
| --- | --- | --- |
| the design's search, 12 configurations | `safety` failures, each either the authority's entry deleted or edited by hand and then Create, under whose new key nothing earlier verifies, or both carriers of a decision deleted where it was made and then a merge that carries no deletion record, or a decision lost a second time without a trace | 0 to 5 histories of 20,000 a cell over two files, 0 to 2 over three |
| the same, with no hand loss of the authority's entry | `safety` in 3 of the 24 cells, a history each, all of the second kind; none with Synchronize and Keepass2Android as the only merges | 480,000 histories |
| the exhaustive part; the laws of the join | no failure; 0 violations | 127,522 histories; 10,000 pairs |
| the survival critique's extended model, with KeePassDX's merge | failures in 6 cells, every shortest history the authority's entry deleted by hand and then Create | 8,000 histories a cell |
| the design's 20 replays | 3, 9 and 16 fail, as in the design; 10 and 15, which the mark held, and 18, which the witness held, fail too | |
| each decided change undone in turn | a revocation by certificate: 15 to 28 more histories a cell end wider; no store key in the retirement: 0 to 4; the rule written at OK in place of the next save: 0 to 2; a single writing gate: 0 or 1; the mark, the witness or the repair kept: no `safety` history differs | 10 cells of 20,000 |
| the repair | closed a widening in 1 history of the 480,000, and none of the 7 that a search of its own found in 120,000; made a file narrower than decided, by taking a later decision made at another gate, a rule widened, a name added again or renewals allowed again, for a loss and writing the earlier state again: 8 to 85 histories of 20,000 a cell over the 48 cells of the design's search and its search with no hand loss of the authority's entry together; 16 to 74 over two files and 8 to 47 over three in the first, 20 to 85 and 9 to 40 in the second (`info:repair-narrower`) | |
| the residues the model counts apart, as accepted | a whole file written over another: 34 to 267 histories a cell; a copy of a narrowed node read under its place: up to 17; a rule lost when KeePass ends before the save that would write it: 60 to 115 a cell over the same 48 cells together; 86 to 106 over two files and 60 to 89 over three in the design's search, 80 to 115 and 66 to 90 in the search with no hand loss (`accepted:unsaved-rule-lost`) | 20,000 a cell |

**Real files.**

- **Every merge.** 13 histories, each merged by KeePassLib's five methods and the four
  `keepassxc-cli` releases into either copy: 234 cells, 220 as decided, 4 narrower and 10 wider,
  every wider cell Create new IDs of a newer copy into the copy that lacks the decisions, whose
  arriving entries sit under identifiers the gate never gave and are passed over. That copy keeps
  the state it had before the import, and nothing changes for it: wider there compares it with the
  newest decisions made anywhere, which the import fails to bring it and Synchronize brings. The 32
  KeePassXC cells run again under the slow key derivation came out the same.
- **A user's node under its own rule**, an empty rule under a root that grants read and write,
  moved, recycled, restored or deleted for good, then merged by every method both ways: nothing
  given in 90 cells of 90 where the node exists; a copy of it is read under its place.
- **Each kind of record deleted by hand**, to the bin, for good with and without a record, and with
  the bin emptied, then Synchronize and KeePassXC 2.8.0 into either copy: 144 merges, 126 as
  decided, 12 narrower, 6 refusing, none wider. The authority's entry deleted for good serves
  nothing; the head deleted refuses until Seal again, which restored the decided state.
- **Every deletion record removed at once**, as Database Maintenance removes them, after each of
  those deletions and after six prunes, then a stale copy merged: 210 merges, 209 as decided, 1
  narrower, none wider.
- **Delete Empty Groups and Delete Duplicate Entries** deleted nothing of the store, every record
  carrying a nonce. After Duplicate Group of the plugin's group without the suffix, Delete Duplicate
  Entries deleted every original and nothing was served; after Duplicate Entry of a single record,
  it deleted the original, read as decided for a refusal, which the head restates, narrower for an
  admission record or a profile addition, and as nothing served for the authority. Create new IDs
  then Delete Duplicate Entries: nothing served, in a third copy synchronized with the result too.
- **The repair**, in each way it could run: it closed a version made by a holder of the store key in
  a current copy only where it reached both files; it did nothing for deletion records cleared while
  a loss stood, which ends wider in every mode; and it made three decisions taken at another gate
  narrower.
- **The head as a single gate writes**: after 1,000 writes it holds 81,551 bytes of strings in 272
  strings and names 1,144 replaced heads, and its longest string has 37,830 characters, 13 times the
  longest string any emulator run of section 2.3 carried; the read took 13.9 and 16.3 ms over 285
  entries and 2,008 deletion records.
- **The model held to the files** at every step, 1,416 steps over 300 random histories besides the
  steps above: no disagreement changed whether a client is admitted, a name is on the list or a node
  gives a verb.

**Sequences that end wider**, each in its shortest form measured: a decision deleted in both of its
entries where it was made, then a merge that carries no deletion record (model, 4 steps); a decision
lost a second time without a trace (model, 4 steps); deletion records cleared while a loss stands,
then a stale copy synchronized, with or without Seal again pressed before it arrives (real files and
model); Create new IDs of a newer copy into a stale copy, which stays as it was before the import
(real files, 2 steps); a file made by a holder of the store key, wherever the repair does not run or
does not reach both files (real files, 4 steps); a gate not yet synchronized (1 step); Seal again
after a loss without a record, for a revocation, a node's first rule and a refusal of renewals (4
and 5 steps); and **the retirement that holds the store key after a Delete with no Create, deleted
for good by hand, then Create and a rule set again**: the node's narrowing, written under the lost
key, is passed over and the node gives read and write (model, 7 steps), as with the authority's
entry in its place (3 steps). The loss by itself widens nothing: nothing is served until Create, and
the store then reads narrower, every rule, name and revocation written under the lost key passed
over. Wider there means only that a narrowing written under the lost key is no longer read once a
broad rule is set again.

**Sequences that end refusing.** The head deleted for good or emptied from the bin, a `KPG.` string
edited by hand, two deletions with no record after KeePassDX's merge, Create new IDs of an
incomplete copy: the whole database, until Seal again. With no press possible, nothing served: the
authority's entry deleted for good or replaced by its duplicate, the plugin's group duplicated
without the suffix and deduplicated, Create new IDs then Delete Duplicate Entries, a second
authority.

**Not run**: the plugin's API against the store; any window, phone or second KeePass with the
plugin, so several gates are files that each write; a rule waiting in memory, on real files; a store
key renewed at Recreate; kdbxweb, pykeepass and the phone programs as programs; the read on
KeePass's thread. The repair was switched on in every run of this section, and an edited entry
stopped the whole database; section 4.16 runs the configuration that was decided, with no repair
and the history rule.

### 4.14 Records edited by hand, and the reading rules

`edited-refusals`. Four records that refuse, a retirement, a revocation, a profile removal and a
marker for the recycle bin, and an admission record as a control, each an entry written once with
`KPG.Kind`, `KPG.Data` and a check `KPG.Mac` over every `KPG.` string; the retirement and the
revocation each also leave a deletion record naming no entry, a witness. Copy B takes one of ten
hand edits (a character of the payload; the payload's string renamed or deleted; every string
emptied; the title; a move; a move to the recycle bin; a deletion with and without a record; a
duplicate with the original edited), under four time conditions against copy A, and is merged with A
by KeePassLib's five methods and by `keepassxc-cli merge -s` of the four releases under the slow key
derivation, both ways: 206 scenarios, 3,708 merge cells and the 412 copies read as they are. The
four releases agree in every cell, and `keepassxc-cli` applies no deletion record. The whole run was
made twice, alike line for line but times. Every result is read under five rules:

| Rule | How it reads | Refusal cells, wider / unchanged / refusing |
| --- | --- | --- |
| R1 | current versions; no check | 486 / 2,232 / 162 |
| R2 | current versions and history items; no check | 30 / 2,840 / 10 |
| R3 | current versions; a refusal whose check fails is ignored | 486 / 2,232 / 162 |
| R4 | as R3, and a failing refusal or an unreadable store entry stops the whole store | 30 / 1,920 / 930 |
| R5 | as R3, and a revocation or a retirement also counts by its witness | 324 / 2,556 / 0 |

- **A single changed character widens under R1 and R3, with no deletion**: B's edited version
  becomes current through KeePass's Synchronize, Overwrite if newer and Overwrite existing and
  through every `keepassxc-cli` release, and a revocation, a removal or the marker no longer reads
  as itself, in 38 of 72 cells a record (2 steps). R1 and R3 gave the same verdict in all 4,120
  cells, since every edit run changed the target as well as the check.
- **Every wider cell of R2 and R4 is a deletion with its record that a KeePass merge carried**, 10
  of 72 a record; none is a `keepassxc-cli` cell.
- **R5** has no wider cell for the two records with a witness, and is wider for the removal and the
  marker in 162 cells each: every payload edit, 38 of 72, and a deletion with its record, 10 of 72.
- **A retirement** edited or deleted is never wider: two unretired authorities serve nothing.
- **The control** is never wider under any rule; its edited record refuses its client in 162 cells,
  and in 186 under R4.
- **The edited copy itself**, read as it is, is wider in a single step under every rule.

**The history rule, R6**: R2 and R4 joined. Every record carries the check; a record counts in its
current version when that version passes, or else in its newest history item that passes; a store
entry with no passing version anywhere stops the whole store. Run over the 206 scenarios and over
five parts more, each on all five records with every merge both ways and every copy under the fast
key derivation: an edit followed by further edits under history limits of the default, a single
item, none, no limit and a single byte; Database Maintenance's age cut at 30 and at 365 days; an
edit that writes no history, by KeePassLib without its backup step and by pykeepass 4.2.0, which
also kept the modification time; an edit in both copies, apart and in the same second; and a history
item edited or removed by script. 220 scenarios and 3,960 merge cells; the extension ran twice,
alike.

| Part | R6, refusal cells, wider / unchanged / refusing | R2, the same cells | R6, control cells |
| --- | --- | --- | --- |
| 0, the 206 scenarios (2,880) | 30 / 2,840 / 10 | 30 / 2,840 / 10 | 0 / 710 / 10 |
| 1, history limits (2,160) | 0 / 944 / 1,216 | 798 / 1,096 / 266 | 0 / 236 / 304 |
| 2, Database Maintenance (216) | 0 / 144 / 72 | 42 / 160 / 14 | 0 / 36 / 18 |
| 3, no history (432) | 0 / 328 / 104 | 42 / 376 / 14 | 0 / 82 / 26 |
| 4, both copies edited (216) | 0 / 192 / 24 | 0 / 216 / 0 | 0 / 48 / 6 |
| 5, a history item tampered with (144) | 0 / 84 / 60 | 33 / 100 / 11 | 0 / 21 / 15 |

- **No cell of parts 1 to 5 ends wider under R6**, 0 of 3,960: every cell that is not unchanged is
  the whole store refusing, or two unretired authorities for the retirement. R2, the same rule
  without the stop, widens once the written version has left the history.
- **Where the written version leaves the history**: at the 11th edit under the default limit of 10
  items, at the 2nd under a single item, at the first under none or a single byte, never under no
  limit; Database Maintenance at 30 days removed it and at 365 kept it. From there on R6 stops the
  whole store.
- **A merge brings it back from the untouched copy**, except where the merged entry's history limit
  pushes it out again, where pykeepass's edit kept the time so that the copies meet in the same
  second, and where a tampered history item keeps the written version's time: of 504 revocation
  merges whose copy B held no written version, it came out current in 96, as a history item in 89,
  as a copy under a new identifier in 28 and nowhere in 291.
- **The only wider cells under R6 are part 0's 30**: a refusal deleted for good with its deletion
  record, carried into the other copy by a Synchronize (2 steps); in the copy where it was deleted,
  a single step. The control's edited admission record falls back to its history under R6 and admits
  its client in 710 of 720 cells, where the other rules refuse it in 162 or 186.

**Not run**: a window, a phone, kdbxweb; an edit that leaves the target intact; an edit of an
authority, a profile addition, a rule version or a renewal answer; a merge that makes another group
the recycle bin; three copies; heads. Section 4.16 runs R6 on the composed store, heads included,
for every kind of store entry under KeePass's own operations, and under Synchronize and KeePassXC
2.8.0 only.

### 4.15 Rule models under a rollback

`rollback-rule-models`. The product's own resolver at `a42e38aa`, today's model of nearest wins, the
empty rule and `default`, beside grant-only and its middle form as `grant-only`'s probe resolves
them (section 4.11), each expressing the same intent after every edit in its own records. Two
worlds: 1,000 histories of 20 edits on trees of 30 nodes with three profiles, and every history of 4
edits on a tree of five nodes with a single profile, 116,745. Each loss is applied after the last
edit, and the result compared with the last intent. No database is made and no program merges; the
two runs gave the same output byte for byte.

**The control** gives section 4.11's figures exactly (2,428 and 2,280 of 2,802; 1,025 and 0 of
3,175) and shows what the first of them measures: `grant-only`'s trial puts a random other document
on the node, a state the node never had, and section 4.11's row is corrected to that. **Without a
loss** no model is ever wider; what a model cannot express, it gives narrower: today's model in 9.0%
and 4.8% of the intended states (sample and exhaustive), grant-only in 42.1% and 46.6% (every node
meant to hold less than its parent), the middle form in 3.5% and 5.9%.

The share of trials wider than the last intent, in the sample and in the exhaustive world:

| Loss | Today's model | Grant-only | Middle form |
| --- | --- | --- | --- |
| (a) the whole store as it was some edits back | 78.5% / 55.1% | 77.4% / 53.0% | 78.5% / 55.0% |
| (b) a node's rule back to its state before its last change | 50.6% / 50.9% | 46.7% / 51.0% | 47.6% / 46.3% |
| (b) by a stale copy of the node winning by time | 50.6% / 50.9% | 0 / 0 | 0 / 0 |
| (c) a node's newest records lost | 50.6% / 50.9% | 10.8% / 21.9% | 8.6% / 9.1% |
| (d) a document lost | 22.5% / 38.3% | | |
| (d) a grant lost | | 0 / 0 | 0 / 0 |
| (d) a withdrawal lost | | 20.6% / 38.8% | 21.5% / 39.2% |
| (d) a mark lost with its reference; a mark lost, its reference kept | | | 97.0% / 98.4%; 0 / 0 |
| (e) two edits of a node made apart, beyond what both allow | 0.5% / 0.26% | 68.1% / 50.6% | 67.0% / 33.6% |
| (e) the same, beyond what either allows | 0 / 0 | 2.6% / 5.5% | 3.0% / 5.6% |

Today's model ended beyond what both edits allow only where a decision of grant-only's kind, a grant
of its own below an inherited rule, was in play (19 and 1,374 trials). The trial holds no change of
the tree, no third copy, no authentication and no program, and how often each loss happens is not
measured.

### 4.16 The store as decided

`store-as-a-whole`, run with its switch `-Decided`; its record is `recorded-decided\` and its
report `papers\ground\measured-decided-store.md`. The store of section 4.13 in the configuration the
project decided on 2026-11-16, which differs from that run in two things. **There is no repair**:
the plugin writes nothing back after a Synchronize, and what a merge leaves is read as it is. **An
edited entry is read by the history rule**, R6 of section 4.14: every record carries its check, a
record counts in its current version when that version passes the check, or else in the newest
version kept in the entry's history that passes, and only an entry of the store with no passing
version anywhere, or a missing head, stops the whole database. The reading of section 4.13, under
which an entry whose current version fails stops the whole database, is called **the plain stop**
below. Everything else is as in section 4.13: several gates, no witness, nothing on a user's node,
a revocation naming the client's key, the store key carried over a Recreate and kept in the
retirement of an authority deleted with none after it, and nothing kept on the gate's machine.
**As decided** is a file granting what the last decision allows, and **narrower** a file granting
less without refusing as a whole. Every file was made and changed by KeePassLib 2.62 and merged by
KeePassLib or `keepassxc-cli`; nothing ran in a window or on a phone.

**The history rule as implemented** (`read.py`), where the decisions leave a point open: an entry
belongs to the store when any of its versions carries a `KPG.` string; history items are tried
newest first; the store key is taken from any version of an authority or of a retirement that
holds it and passes under it; an entry with no passing version is damage only when its identifier
carries the store's keyed tag under a key the file holds, and with no key held nothing is
recognized as the store's, the read is `nostore` and nothing is served. In this configuration the
plugin writes nothing again for a record that counts by its history; writing such a record again at
the plugin's next own write was measured afterwards, in section 4.17.

Everything ran twice, the second run printing every output of the first line for line but the
running times. With the switch off the family prints what it printed before: six parts run again
after the switch was added came out as the first record.

**No repair, on real files.** Each cell was held to the same cell of section 4.13's record.

- **Every merge**, `b-merges`: 234 cells, 221 as decided, 3 narrower and 10 wider (with the repair:
  220, 4 and 10). Every wider cell is still Create new IDs of a newer copy into the copy that lacks
  the decisions, which keeps the state it had before the import (below). The cells that changed: a
  copy from before a Recreate synchronized with a copy after it, narrower with the repair, which
  took a later rule for a loss, is as decided (H13); in the cell of a rule set, widened, narrowed
  and emptied at a node and narrowed at the root, then Create new IDs into the stale copy, wider
  either way, the root now gives read and write in the stale copy where the repair had narrowed it
  (H7); and in 22 cells a copy of a user's node, made by Duplicate Entry or by Create new IDs, gives
  read and write, what the root it lands under gives, where the repair had written the root's
  earlier read beside it.
- **A user's node under its own rule** gives nothing in all 90 cells where it exists, as before.
- **Each kind of record deleted by hand**, `b-deletions`: 144 merges, 126 as decided, 12 narrower,
  6 refusing and none wider, as before.
- **Every deletion record removed at once**, `b-clear`: 210 of 210 as decided, where the repair had
  refused renewals again after they were allowed again in a single cell (P5).
- **Delete Duplicate Entries**, `b-dedupe` and `b-dupes`: every outcome as before. In `b-dedupe`
  the third copy synchronized still serves nothing, with 9 entries passed over instead of 11: the
  repair had written 2 entries under a key the file no longer held.
- **The modes of `b-repair` with no repair**, 96 cells: the same as before, cell for cell.
- **No cell on files ended wider with the repair off that was not wider with it on, and none ended
  refusing that did not refuse before.**
- **A file made by a holder of the store key** (`b-repair` R8): a node N narrowed at gate 0, copy 1
  synchronized, a version granting read and write at N made with the store key in copy 1, then
  KeePass's Synchronize at gate 0 writing both files: **wider, N gives read and write in file 0
  and in file 1**. The same merge as File, Import with Synchronize's method, Overwrite if newer,
  Overwrite existing or Keep existing: wider in file 0, the gate's. Through Create new IDs, as
  decided, since nothing arriving under a new identifier is read. The repair had closed this
  sequence where it ran and reached both files.
- **300 random histories**, `b-random`: no failure of the model's safety check, which holds that no
  file is wider than what was delivered to it. With no repair and the plain stop, a check run
  beside the record and outside the family printed the first run's output line for line. Under the
  history rule 1,419 steps applied (1,416 before, since an edited entry no longer stops the store),
  and of the 724 files at the end 406 were as decided, 93 narrower, 54 refusing and 171 wider than
  every decision any gate made (397, 91, 73 and 163 before). Wider here includes a copy that a
  decision made at another gate has not yet reached. The 8 more wider files were not read singly;
  that they read on where the plain stop refused and lack a decision made at another gate is
  reasoning from the safety check.

**The model with the repair off.** The model holds no history of an entry, so an edit is damage
there, as in section 4.13. Against the same searches with the repair, the design's search (24 cells
of 20,000) printed the same in every cell but F over two files, 6 `safety` histories against 5: a
single history in 480,000. The search with no hand loss of the authority's entry (24 cells), the
extended model (20 cells of 8,000) and the exhaustive part (11,110, 47,988, 40,494 and 27,930
histories, no failure) printed the same. The history more is the history the repair closed: the
authority deleted with a record in copy 0, a rule set at the root in copy 1, the authority deleted
with a record there too, c0's renewals refused for good in copy 1, Create in copy 0, then copy 1
synchronized from copy 0: in file 1 c0's refused renewal is asked again, since its refusal was
written under the store key both deletions lost. That renewal asked again is what the model counts
as wider: with the key lost nothing is served until Create, and the store then reads narrower. With
the repair off, the merges-only histories that converged narrower (35 over two files and 28 over
three) and every file the repair made narrower (8 to 85 a cell over both searches together) are
gone; a rule lost before a save is unchanged in every cell (86 to 106 over two files and 60 to 89
over three, in the design's search).

**The history rule over every kind of store entry** (`b-history`, new). The base, equal in both
copies: copy 0 revokes a client c0, recreates the authority, pairs a client c1, adds a profile name
pg and removes a name pf, sets a node N's rule to nothing and refuses c1's renewals for good, and
copy 1 is synchronized from it. Each copy then holds nine kinds of store entry: the authority, a
retirement, an admission record, a revocation, a profile addition, a profile removal, a rule
version, a renewal answer and a head. A tenth kind, the retirement that holds the store key, comes
from deleting the authority with none after it before the Synchronize; no authority is served
there, so each of its reads is followed by Create at that copy. A single entry of copy 0 is changed
by a single operation, made through KeePassLib as KeePass's own commands make it: `CreateBackup`
with the database, which trims the history to the database's limits, then the change, then the
modification time.

| Operation | What it does |
| --- | --- |
| dialog | the entry dialog: the first character of the record's subject in `KPG.Data` changed |
| field | the entry dialog: `KPG.Data` deleted |
| quick | a quick edit, Expires: Now, which the check does not cover |
| move, bin, delete | moved to another group; to the recycle bin; deleted for good with a deletion record |
| edits10, edits11 | the dialog edit, then 9 or 10 dialog edits of the title: 10 or 11 edits under KeePass's default limit of 10 history items |
| quick10 | the dialog edit, then 10 quick edits |
| off | the database's limit of history items set to 0 in both copies, then the dialog edit |

Each cell is read in six columns: copy 0 as it is; Synchronize of copy 0 into copy 1 and of copy 1
into copy 0; the same two by KeePassXC 2.8.0; and copy 0 after the gate writes again, a profile
name added. Every read is under the history rule, and the first five also under the plain stop.
Ten kinds by ten operations by six columns, less the gate's own write for the tenth kind, are 590
cells. The letters, a letter per column in that order: **A** as decided; **n** narrower; **R** the
whole database refusing with the store key held, where Seal again applies; **K** the whole
database refusing with no store key held, where Seal again does not apply; **x** nothing served.
For the retirement that holds the key, the letters are after Create.

<!-- A column's letters run together as the report prints them. cspell:ignore RRRARR -->

| Kind | dialog, field, edits10 | quick, move, bin | delete | edits11, quick10, off |
| --- | --- | --- | --- | --- |
| the authority | AAAAAA (plain stop: KKKKK) | AAAAAA | KKKAxK | KKKKKK |
| a retirement (from Recreate) | AAAAAA (plain stop: RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| an admission record (c1) | AAAAAA (RRRRR) | AAAAAA | nnnAnn | RRRRRR |
| a revocation (c0) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| a profile addition (pg) | AAAAAA (RRRRR) | AAAAAA | nnnAnn | RRRRRR |
| a profile removal (pf) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| a rule version (N, nothing) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| a renewal answer (c1, refused for good) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| the head | AAAAAA (RRRRR) | AAAAAA | RRRARR | RRRRRR |
| the retirement that holds the store key, then Create | AAAAA | AAAAA | nnnAA | nnnnn |

**Under the history rule, 590 cells: 390 as decided, 28 narrower, 172 refusing and none wider.**
Under the plain stop, the 500 reads before any write: 214 as decided, 8 narrower, 278 refusing and
none wider. The two readings differ in exactly the dialog, field and edits10 rows of the nine kinds
an authority is served for, 135 reads, which the plain stop refuses and the history rule reads as
decided. In no cell did the history rule give more than the plain stop, and in no cell did access
end wider than before the edit. Every merge cell gave what copy 0 gave, except after a deletion
for good: KeePassXC 2.8.0 carries no deletion record, so the unedited copy keeps the entry and
reads as decided, and merged into copy 0 it brings the entry back beside its record.

- **The rule holds while the written version is in the history.** After a single dialog edit, a
  deleted `KPG.Data`, or ten edits in all under KeePass's default limit of 10 items, every kind
  reads as decided, in copy 0, after Synchronize and after KeePassXC 2.8.0 in both directions, and
  after the gate's next write.
- **Under KeePass's default history settings an entry survives 10 edits, and the 11th takes the
  written version out**, whatever the later edits are: ten dialog edits of the title or ten quick
  edits, which the check does not cover, count alike. From then on the whole database refuses, in
  that copy and in every copy the edited entry reaches by either merge. No merge with the unedited
  copy brought the written version back in these cells: KeePassLib trims the merged history to the
  limit, and KeePassXC 2.8.0 kept the newer entry whole.
- **A database that keeps no history** (a limit of 0 items) loses the written version at the first
  edit: the first dialog edit of any entry of the store refuses the whole database, as the plain
  stop does.
- **A move, the recycle bin and a quick edit alone** leave every kind as decided under both
  readings; a quick edit adds a history item, and so moves the written version toward the limit.
- **A deletion for good with a deletion record** is no edit, and the history rule does not reach
  it: a revocation, a retirement, a profile removal, a rule version and a renewal answer read as
  decided, from the head; an admission record and a profile addition end narrower, that client
  refused and that name off the list; a deleted head refuses the whole database as a missing head;
  and the authority leaves the file with no key (below).
- **An edited head** counts in the version its history holds and restates what it restated before:
  after a dialog edit, a deleted `KPG.Data` or ten edits it reads as decided in every cell, and the
  gate's next write replaces it with a new head and deletes the edited entry with a deletion
  record, so it no longer counts by its history after that write. Past the limit, or with no
  history kept, the whole database refuses with the store key held. Seal again was not pressed.
- **The two entries that carry the store key**, the authority and, after the authority is deleted
  with none after it, the retirement that holds the key: **where the carrier has no passing
  version, the file holds no key.** Nothing is recognized as the store's, the read is `nostore` and
  nothing is served; Seal again does not apply, and the next Create makes a new key under which
  every rule, name and revocation written before is passed over. The authority past the limit or
  with no history kept: K in every cell, and the gate cannot write. The authority deleted with its
  record: K in copy 0 and after Synchronize either way; as decided after KeePassXC into copy 1,
  which receives no record; and after KeePassXC into copy 0, the entry back beside its record, the
  key held and no authority standing, so nothing is served. The retirement that holds the key, past
  the limit or with no history kept: every cell narrower after Create, the root's rule passed over.
  Deleted with its record: narrower after Synchronize, and as decided after KeePassXC either way,
  since into copy 1 no record arrives and into copy 0 the retirement brought back beside its own
  record still gives the key. The loss by itself widens nothing: the store reads narrower after
  Create. A broad rule set again after such a Create ends wider by section 4.13's sequence, a
  narrowing written under the lost key being no longer read; it was not run again here.
- **The model against the files**: of the 121 differences `b-history` prints between the model's
  reading and the files', 93 are a defect carried over from section 4.13's record, the read listing
  a renewal answer only for a client the file still names, and 28 are `nostore` against the model's
  `incomplete`; both serve nothing (the family's `RUN.md`).

**Growth.** Neither change writes anything per operation: the repair never ran in `b-growth`, whose
single gate never synchronizes, and the history rule only reads. Read under the history rule,
`b-growth` printed the sizes of section 4.13: after 1,000 writes a head of 81,551 bytes in 272
strings naming 1,144 replaced heads, 285 entries in the store and 2,008 deletion records; the read
took 10.1 ms in this record. An edited entry grows by the history KeePass keeps, up to its limit,
and the plugin adds nothing to it.

**Sequences that end wider**, each in its shortest form measured:

- **Create new IDs of a newer copy into a stale copy** (real files, 2 steps): 10 of the 26 Create
  new IDs cells of `b-merges`, every cell into the copy that lacks the decisions, as with the
  repair. The stale copy keeps the state it had before the import, and nothing changes for it:
  wider compares it with the newest decisions made anywhere, which the import fails to bring it and
  Synchronize brings. Sections 4.17 and 4.18 measure readings under which a copy counts.
- **A file made by a holder of the store key, synchronized in** (real files, `b-repair` R8): wider
  at the gate and in both files; nothing closes it anywhere with the repair off.
- **Deletion records cleared after a loss of both carriers of a decision, then the stale copy
  synchronized** (real files, `b-repair` R1): wider at gate 0 and in file 1 through Synchronize and
  every import method but Create new IDs, as with the repair.
- **A narrowed rule's new version and head removed by script after a KeePassXC merge** (real files,
  `b-repair` R2): wider in the copy that removed them, as with the repair; gate 0 as decided.
- **The authority's entry, carrying the store key, deleted by hand, then Create** (model, 6 steps):
  a single history more in the design's search than with the repair, wider by a renewal refused
  under the lost key and asked again.
- **The model's other `safety` histories** of section 4.13, unchanged.

Nothing in `b-history`, `b-deletions` or `b-clear` ended wider.

**Sequences that end refusing.** The whole database, with the store key held: an entry of the
store edited past KeePass's default history limit, quick edits included, or edited once in a
database that keeps no history, in that copy and in every copy the entry reaches by a merge; and
the head deleted for good. Nothing served, with no key held: the authority edited past the limit,
edited with no history kept, or deleted for good with its record. As with the repair: the 6
refusing cells of `b-deletions`, and the third copy of `b-dedupe`, which serves nothing.

**Cut, to keep the two runs within the time set for them**: `b-slow`, whose 32 KeePassXC cells
under the slow key derivation came out as the fast cells in section 4.13's run, and whose question,
the seconds of two loads, touches nothing this store keeps; `store-ablations`, `store-scenarios`
and `store-section6`, whose first records already hold a column with the repair undone;
`store-repair-closes`, which is about the repair; and `design-full`, the check of the copy of the
design's model.

**Not run**: a window, a phone, an emulator or a second KeePass with the plugin; Seal again after a
stop of `b-history`; an edit in both copies, by pykeepass, by a phone program or of a history item
by script, on this store (section 4.14 ran them on a store without heads); Database Maintenance's
history page; KeePassLib's other methods and KeePassXC's other releases on the edited entries; a
rule set again after the loss of the store key's carrier; the model with an entry's history; the
plugin writing again a record that counts by its history, which section 4.17 runs; and everything
section 4.13 lists as not run.

### 4.17 Two readings of the store as decided

`store-as-a-whole`, run with its switches `-Rewrite` and `-Copies`; its records are
`recorded-rewrite\` and `recorded-copies\` and its report `papers\ground\measured-two-readings.md`.
**Of the two readings measured here, the design took the first and not the second: the plugin
writes an edited record again at its next own write, and the store reads only its own records, so
that a copy under another identifier counts for nothing (`EVALUATION.md` section 2).** Each reading
ran as a configuration of its own on top of the store as decided of section 4.16. Every file was
made and changed by KeePassLib 2.62 and merged by KeePassLib or `keepassxc-cli` 2.8.0; nothing ran
in a window or on a phone. Every part ran twice, the second run printing every output of the first
line for line but the running times, and with the new switches off six parts of the first record
and seven of the store as decided printed their records line for line.

**Reading 1: an edited record written again.** Whenever the plugin writes to the store anyway, it
also writes again every record that counts only by a history item, so that the written version is
current again; never after a merge and never on a read. The entries of the store are written once
and their identifiers are made from their content, so a record written again with the same content
has the same identifier, and the form run sets the same entry's current version back: every `KPG.`
string of the passing history item becomes current with its protection, every other `KPG.` string
goes, and the identifier, the place, the title and every string the check does not cover stay, the
modification time becoming the write's. It ran in two variants: `backup` first puts the edited
version into the entry's history, as an edit through KeePass's own path does, and `cleared` empties
the history instead. Every outcome of access below is the same under both; only the history differs.

- **A single gate** (`one`): each of the ten kinds of store entry of section 4.16 changed by each of
  its ten operations, then a write of the gate's own (a profile name added, or Create for the
  retirement that holds the store key), further edits until the store refuses, and Synchronize and
  KeePassXC 2.8.0 both ways with the unedited copy: 100 cells and 500 reads, **337 as decided, 20
  narrower, 124 the whole database refusing with the store key held, 18 refusing with no key held, 1
  nothing served, none wider**. Every read that is not as decided follows an operation after which
  no version passes (edits11, quick10, off) or the entry is gone (delete), where there is nothing to
  write again. After dialog, field and edits10 the record is written again in every kind, reads as
  decided after the write and in all four merges, and survives 10 further edits; the 11th refuses.
  The head, which the reports call the summary entry, is never written again: the gate's write
  replaces it with a new head and deletes the edited head with a deletion record.
- **Edits and writes alternating** (`alternate`, ten kinds): 1, 3 or 10 edits between writes read
  as decided at every cycle, over 22, 8 and 3 cycles, and never reached the stop, with 30 edits in
  the longest sequence; with 11 edits between writes the 11th stops the store before the gate
  writes, and for the retirement that holds the key Create then starts a new key, narrower. Under
  `backup` the history grows by 2 items a cycle to KeePass's limit of 10, the passing version
  current after every write, so that KeePass's trimming falls on older items; under `cleared` it is
  0 after every write. Writes alone write nothing again: after 1 edit and 12 writes the history
  stayed at 2.
- **Two gates** (`two`, the nine kinds with an authority): the same edited record written again at
  both gates, and a gate writing again while the other copy holds a later edit, in both orders,
  each followed by KeePassLib's five methods and KeePassXC 2.8.0 into either copy: **324 merge
  cells, as decided in all 324**, under both variants and in the control that writes nothing again.
  Where the later edit wins a merge, the record counts by its history and the target gate's next
  write writes it again. Over five rounds of an edit, a Synchronize, both gates writing and a
  Synchronize each way, the record's history after each round was 3, 6, 9, 10 and 10 under
  `backup`, 1 under `cleared`, and 1 to 5 in the control with the current version failing: by
  reasoning from that growth, the control reaches the stop at the 11th round's edit and the rewrite
  never does.
- **Against a later decision of the other gate** (`prune`): gate 1 takes the record out with a
  deletion record (a revocation, a Recreate, a profile removed, a rule widened, renewals allowed
  again) and gate 0 writes it again, before that decision or after it: 120 merge cells, **110 as
  decided, 4 narrower and 6 wider, every cell as in the control that writes nothing again**. The 6
  wider and the 4 narrower are Create new IDs into copy 0, which receives gate 1's decision only as
  a copy and keeps the state it had before the import. Where the rewrite is later than gate 1's
  deletion record, Synchronize keeps the entry and drops the record; the decision that took it out
  still holds, and gate 1's next write takes the entry out again.
- **300 random histories** (`b-random`): the record of section 4.16 again under both variants,
  1,419 steps and no failure of the model's safety check. None of their edits was followed by a
  write at that file while the edited record counted by its history, so nothing was written again.

**Where reading 1 holds and where it stops.** It holds with a single gate and with two: no cell
ended wider than the store as decided, and no record written again undid a later decision of the
other gate. It moves the stop from the 11th edit since the record was written to the 11th edit since
the plugin last wrote anything. It does not reach the edits themselves: 11 edits between two writes
of the plugin still take the written version out, a database that keeps no history still loses it at
the first edit, and for the two entries that carry the store key either loss leaves the file with no
key, as in section 4.16. Its cost is the entry's history: under `backup` up to 10 items of each
edited entry are the plugin's rewrites and the edits between them, filled within four rounds with
two gates, and under `cleared` every trace of an edit goes at the next write. Writing the record
again as a new entry beside the edited entry was not run: it would be a new record under a new
identifier, and every record that names the old record would have to name the new record.

**Reading 2: a record that takes access away, read from a copy.** A **copy** here is an entry
holding a record's strings under an identifier the record's content does not give, as KeePass's
Duplicate Entry, Duplicate Group, copy and paste and its import with Create new IDs make it. The
narrowest reading was run: a revocation, a retirement as the refusal of its authority (never as a
source of the store key), a profile removal, a rule version that grants nothing and a renewal answer
that refuses for good count from a copy, read as the record under the identifier its content gives;
an admission record, a profile addition, an authority, a head and every other version never count.
Two variants: **held**, where a record read from a copy is held to the completeness check, which
stops the whole database where no head restates a record, and **exempt**, where it is not.

The 234 cells of `b-merges`:

| Reading | As decided | Narrower | Refusing | Wider |
| --- | --- | --- | --- | --- |
| the store as decided | 221 | 3 | 0 | 10 |
| held | 220 | 3 | 10, the whole database | 1 |
| exempt | 226 | 3 | 2, nothing served | 3 |

Every cell that differs is Create new IDs of a newer copy into the stale copy: the ten cells that
end wider in section 4.16, where the stale copy keeps the state it had before the import and the
import fails to bring it up to date. **Held closes 9 of the ten**, each as a refusal of the whole
database until Seal again, and newly refuses a name removed and added again (H4), whose removal
counts from its copy while the head that restates it arrived only as a copy. **Exempt closes 7**: a
revocation (H2, and H3 with the key renewed at the other gate), a name removed (H5), a rule narrowed
at a gate and widened at the other (H10) and renewals refused for good (H12) as decided, and the two
cells whose newer authority arrived only as a copy (H6 and H13) as nothing served, the copied
retirement counting. Left wider: the same rule changed apart at both gates (H9) in both variants,
and under exempt a rule narrowed to read (H7) and a rule removed at N with the root emptied (H8),
which the narrowest reading does not count.

- **`b-deletions`, `b-clear` and `b-repair`**: every cell as in section 4.16, in both variants;
  **`b-dedupe` and `b-dupes`**: every outcome as there.
- **`b-random`**, 300 histories: held, 169 files wider, 406 as decided, 93 narrower and 56
  refusing, with a gate act refused because the reading stopped the store; exempt, 169, 407, 93 and
  55. No failure of the model's safety check.
- **Copies that are not an import** (`copies`, nine kinds by eight cases): a copy beside its
  original is the same record once and changes nothing; once the original is gone, by Delete
  Duplicate Entries or by a deletion, the copy counts for the kinds that count; a replaced narrowing
  does not come back, since the widening version names the original's identifier, under which the
  copy is read. No such copy narrowed anything.
- **A record counted from a copy and written under its own identifier**, then a head restating it
  and Synchronize with the newer copy, on the ten histories: under held, five read as decided after
  the write, H6 serves nothing, the rule narrowed to read and the rule removed (H7, H8) are wider
  again and H9 stays wider; and in both variants **H13 holds no store key**. The retirement written
  under its own identifier made the next write take the stale authority's entry out, the stale
  copy's only carrier of the key, while the newer authority had arrived only as a copy: a store
  started again, not access widening by itself. Synchronize with the newer copy afterwards gave as
  decided in every history, both ways, in both variants.

**Where the narrowest reading is not clean**, named by the report and not run: a rule narrowed to
something other than nothing; a rule removed; a retirement that settles which authority is served
where two stand; the retirement that holds the store key read as a key source; a head's refusal
items; and a version that narrows a profile and widens another, which this probe cannot write, its
rule holding a single value a node. Section 4.18 runs each.

**Sequences that end wider** in this run: Create new IDs of a newer copy into a stale copy, which
keeps the state it had before the import, under reading 2 (H9 held; H7, H8 and H9 exempt), and
under held H7 and H8 again once the copied records are written under their own identifiers; and the
6 cells of `prune`, the same cells with the rewrite and without it. Every sequence of section 4.16
not run again here stands. Nothing in `one`, `alternate`, `two`, the rewrite's `b-random`,
`b-deletions`, `b-clear`, `b-repair` or `copies` ended wider than the store as decided.

**Cut**: writing again as a new entry beside the edited entry; KeePassXC releases other than 2.8.0
and the slow key derivation; the reading of copies in the model's searches; the plugin's own Seal
again after a stop caused by a copy, for which the model's stands.

**Not measured**: a window, a phone, an emulator or a second KeePass with the plugin; a history
limit other than 10 and 0 under the rewrite; KeePass's own copy and paste; the cases named above as
not clean; a write that prunes by the reading of copies; and everything section 4.16 lists as not
run.

### 4.18 What a copy of each record may take away

`store-as-a-whole`, run with its switch `-Reading` and each of `nothing`, `profile`, `verb`, `cap`
and `cap-items`; each reading's record is `recorded-reading-<name>\` and the report is
`papers\ground\measured-copies-extended.md`. **None of these readings was taken: the store reads
only its own records, and a copy under another identifier counts for nothing (`EVALUATION.md`
section 2).** The run defines, for every kind of store record and every field of it, what a copy
may do whatever else the file holds, and measures each reading that follows, on files made and
changed by KeePassLib 2.62 and merged by KeePassLib or `keepassxc-cli`; nothing ran in a window or
on a phone. Every part ran twice, the second run printing every output of the first line for line
but the running times.

**The test.** A reading is kept only where **a file read with its copies never gives more than the
same file read from its own records**, the entries that pass the check under the identifier their
content gives, in the current version or in history: no client admitted, no profile held, no word
given at a node, no renewal asked and no authority served that the file's own records refuse. A
reading that passes can leave a cell wider and cannot make a cell wider. Every answer of every part
was held to it: **21,399 answers over the five readings and both variants, none giving more than
the file's own records give**. The part `kinds`, which turns on switches no reading holds, is not
among them.

**Ruled out, each by a counterexample** (`kinds`, the same file read with a switch off and on):

- **A copied retirement deciding which authority is served.** Both gates recreate the authority
  apart and synchronize, so two stand and nothing is served; gate 0 keeps its own, under which a
  client c1 was paired; a third copy is taken; gate 0 recreates again; the copy holding both
  imports the third copy with Create new IDs. Counted only as a refusal, nothing is served; allowed
  to settle, **c1 is admitted** and an authority is served where the file's own records serve none.
- **A store key taken from a copy.** The authority's entry is duplicated, the original deleted for
  good with a record, and the store started again with Create and a name added. Not taken: a single
  key, the name pf, the root and N giving nothing. Taken: two keys, the names pf and pg, **the root
  giving read and N read and write**, every rule and name written under the earlier key counting
  again.
- **Anything that grants**, by the report's reading of each kind and field: an admission record, a
  profile addition, an authority, the words a rule grants, renewals allowed again, a head's
  presence, which would complete a stopped store, and its items that acknowledge copies.

**The readings.** A revocation, a retirement as a refusal and a profile removal count from a copy
in every reading. A copied rule version or renewal answer never joins the file's versions and never
grants: it is applied as a **limit**, combined with the file's own rule of the node word by word,
each word given only where both give it, and it stops limiting once a version the file reads, or
another passing copy, names it as replaced. The readings differ in how much of a copied version
limits:

| Reading | A copied rule version limits |
| --- | --- |
| `nothing` | only where it grants nothing to anybody: the reading of section 4.17, with the retirement counted only as a refusal |
| `profile` | per profile it names, where the file's own rule names that profile and the copy grants within it; elsewhere only where it grants the profile nothing |
| `verb` | per profile it names: every word it does not grant is taken from the profile |
| `cap` | as `verb`, and a profile it does not name gets at most what arrives from above |
| `cap-items` | as `cap`, and a copied head's items count: a restated refusal as that refusal, a restated version as a copy of that version |

For these the probe was extended to rules per profile, with two profiles, pf and pg, and two words,
read and write, and `b-merges` gained nine histories (H14 to H22): a version that narrows a profile
and widens the other; a rule of nothing widened for a profile; a narrowing below a wider rule where
the stale copy holds no rule of the node; a rule removed where the node above gives less, and where
it gives more; a rule changed to other words; a profile narrowed at a gate and the other widened at
the other gate, apart; a profile dropped from a rule below a narrower root; and two authorities
standing, gate 0 keeping its own. With the new switches off, every recorded configuration printed
its record line for line.

The 396 merge cells of the 22 histories:

| Reading | Exempt: as decided, narrower, nothing served, wider | Held: as decided, narrower, refusing, wider |
| --- | --- | --- |
| the store as decided | 374, 5, 1, **16** | |
| `nothing` | 379, 5, 3, **9** | 373, 5, 11, **7** |
| `profile` | 382, 6, 3, **5** | 373, 5, 15, **3** |
| `verb` | 383, 7, 3, **3** | 373, 5, 17, **1** |
| `cap`, `cap-items` | 386, 7, 3, **0** | 373, 4, 19, **0** |

In every reading and variant, every cell that differs from the store as decided is Create new IDs
into the stale copy; every Synchronize, every other import method, every KeePassXC cell and the 90
cells of a user node read the same. The 16 wider cells of the store as decided are such imports,
and in each the stale copy keeps the state it had before the import: the import fails to bring it
up to date, which Synchronize does, and a reading of copies decides how much of what arrived as a
copy the stale copy reads.

- **The ten cells of section 4.17, exempt**: `nothing` closes 7, 5 as decided and 2 as nothing
  served; `profile` and `verb` close 9, leaving the rule removed at N (H8) wider; `cap` and
  `cap-items` close all 10, 8 as decided and 2 as nothing served. H6 and H13 end with nothing
  served in every reading: the retirement counts, and the newer authority arrived only as a copy.
  **Held**, every reading turns each of the ten into a refusal of the whole database, but the rule
  changed apart (H9) under `nothing`, which stays wider, and every reading refuses H4 as well.
- **The six further wider cells, exempt** (H14, H16, H17, H19, H20, H21): `nothing` leaves 6
  wider, `profile` 4, `verb` 2, `cap` and `cap-items` none. A version that narrows a profile and
  widens the other counts for the narrowing and not for the widening: as decided where the stale
  copy already held the wider words (H20), **narrower where it did not** (H14, pg lacking write). A
  rule changed to other words ends narrower under `verb` and `cap` (H19, pf lacking read). Only
  `cap` holds a node whose rule was removed to what arrives from above (H17, and H8 of the ten).
  Held, 1, 4, 6, 8 and 8 of the nine further cells refuse the whole database under `nothing`,
  `profile`, `verb`, `cap` and `cap-items`, among them the two standing authorities (H22), which
  serve nothing in every reading and in the store as decided.
- **The other parts**: `b-deletions` (144 merge cells), `b-clear` (210), `b-repair` (96),
  `b-dedupe` and `b-dupes` gave every answer of the same class as the store as decided on the same
  file, in every reading and variant. `b-random`, 300 histories over two and three copies with
  rules per profile among their acts, 721 files read at their end: exempt, 720 answers of the same
  class and a single file wider in the store as decided that reads as decided; held, 719, that file,
  a second file wider there that refuses, and a gate act refused because the reading stopped the
  store. The same in every reading, and no failure of the model's safety check. These histories are
  not section 4.16's: under `cap`, exempt, 223 of their files are wider than every decision made,
  files a decision has not reached in any form, which no reading of copies touches.

**What the readings cost.**

- **Copies that are not an import** (`copies`, ten kinds, the nine of section 4.17 and a rule
  version per profile): 131 rows a reading and variant, every row as in the store as decided; the
  gate's own write left the copy where it was.
- **Narrower than decided**: under `cap` and `cap-items` 2 cells of the 396 more than the store as
  decided (H14, H19), where the copied version and the file's own version each grant a word the
  other does not, and the node gives only what both give.
- **A limit that outlasts its replacement**, built on purpose, since no merge cell and no random
  history showed it: N narrowed at gate 0 and widened at gate 1 apart; copy 0 imported into copy 1
  with Create new IDs, where the copied narrowing limits; then N widened again at gate 0, which copy
  1 has not received. Exempt, copy 1 is narrower than decided under `profile`, `verb`, `cap` and
  `cap-items`, and stays so through a write of the gate's own, through Seal again and through the
  rule set again by a gate that names no copy; a second import, a Synchronize, the rule set again
  by a gate that names the copies it read, or the copy deleted by hand ends it. Held, copy 1 refuses
  as a whole from the import until Seal again.
- **Held to the check**, every cell in which a copy takes away what the file's heads do not restate
  is a refusal of the whole database until Seal again, a single word at a single node included: the
  ten cells and H4 in every reading but `nothing` (nine and H4 there), and up to 8 of the nine
  further cells. No such stop came from a copy beside its original, from Delete Duplicate Entries or
  from a copy brought back by KeePassXC.
- **The copies deleted by hand** after they took something away give back the answer of the store
  as decided: wider again in all 16 histories run.

**What the plugin would have to do**, for any reading here and for the store key to be kept: never
write a record again from a copy, in any form; compute every write of its own, what it deletes as
replaced, retired or revoked and what its head restates, from the file's own records; decide from
them which authority is served and which keys the file holds; for `profile`, `verb` and `cap`, name
as replaced the copies of a node's versions it read when it writes a version of that node; at Seal
again after a stop a copy caused, name in the head the copies it acknowledges; and list on its tab
the copies that take something away. Measured on the ten cells and six of the further cells, each
import followed by Seal again where the reading stops the store, an edit of the authority's entry,
the gate's write with the rewrite of section 4.17 and nothing written from a copy, then Synchronize
both ways: **the key held and the authority's entry present after the write in all 16, and as
decided after all 32 Synchronizes**, in every reading and variant. The control, which writes each
record counted from a copy under its own identifier as section 4.17 did, left H13 with no store key
under every reading and variant: a store started again, not access widening by itself.

**A defect the family's `RUN.md` states in the copied model**: it writes an admission record under
the first key's name, also after Create has started a new key. No recorded part pairs after a new
key, and `kinds` was changed to read the rules and names instead. Two limits the same list names
beside it: the probe compares what a node gives each profile, not what a client reaches through the
profiles it holds, so a word more for a profile no admitted client holds counts as wider; and held
to the check, a gate act refused where the reading stops the store can give a file a history the
store as decided would not have written, so each comparison is of the same file read both ways.

**Sequences that end wider** in this run: Create new IDs of a newer copy into a stale copy, which
keeps the state it had before the import, under a reading that does not apply the narrowing (H7, H8,
H9, H14, H16, H17, H19, H20 and H21 under `nothing`; H8, H16, H17, H19 and H21 under `profile`; H8,
H17 and H21 under `verb`; none under `cap` or `cap-items`; held, H9 under `nothing`, H16, H17 and
H19 under `profile` and H17 under `verb`); a refusal whose entry was deleted by hand with no
deletion record in the newer copy, so that only its head restates it, then that import into a copy
that never held the refusal and stays as it was, under every reading but `cap-items` (a revocation,
a rule of nothing, a rule for a profile below a wider root and a profile removal, each wider with
the items not counted); and the counted copies deleted by hand. The settling retirement and the key
from a copy end wider in no reading here, since no reading holds them.

**Cut**: the reading the report calls candidate d, a rule removal counted only where the removed
rule granted more than the rule above it, which `cap` applies word by word; the readings in the
model's searches; the slow key derivation; the settling retirement, the key from a copy and, but
for `cap-items`, a head's items as configurations of their own, each switched over single files in
`kinds`; a copy of an authority counted as standing without being served; Create at a file where a
copied retirement leaves nothing served; KeePass's own window for Seal again, copy and paste and
every edit.

**Not measured**: a window, a phone, an emulator or a second KeePass with the plugin; the plugin's
own Seal again and tab; a rule's switches (attachments, protected values) and a document's
`default`; more than two profiles; a rule on a group between the root and the node; a copy under a
store key the file does not hold, which every reading passes over; two authorities standing after
Create at a file where a copied retirement leaves nothing served; a head's items under `profile`
and `verb`; a merge of copies under KeePassDX's reading; the growth of the list of acknowledged
copies in a head; and everything sections 4.16 and 4.17 list as not run.

## 5. Running everything again

Every probe whose source and command the branch carries was run again from the branch, except the
emulator flows of `spikes\storage\android`, the window runs of `desktop-windows` and
`desktop-hide-and-lock`, and the bulk runs of `source-harness`; the `RUN.md` files that give a date
give 2026-11-15, 2026-11-16 or 2026-11-17, and those that name the machine name the machine the
records were made on. Of the families before the later measurements, seven give neither. Each
family's `RUN.md` holds a row for every number or outcome a paper quotes from it: the recorded
value, the value of the later run, and whether it was reproduced. The seven families at the end of
the table hold the later measurements, written on the branch and recorded from it on 2026-11-16, but
for the wider readings of a copy in `store-as-a-whole`, recorded on 2026-11-17; what each needs and
how it ran again is under the table. "Differs" is a later run giving something else; "no" is neither
the record nor the later run giving what the paper states. A row marked "yes" can still carry a note
that the paper's sentence is broader than the output; section 6 has those.

The counts below are the rows of the tables in each `RUN.md` that carry a "Reproduced" column.

| Family | Rows | Yes | Differs | No | Not run | What differs |
| --- | --- | --- | --- | --- | --- | --- |
| `ground-keepass` | 47 | 46 | 1 | 0 | 0 | the moment of the run, printed as a time |
| `ground-inventory` | 10 | 9 | 1 | 0 | 0 | a certificate's encoded length, which varies with its fresh key and signature |
| `ground-keepassxc` | 61 | 60 | 1 | 0 | 0 | a timing |
| `ground-measured` | 23 | 22 | 1 | 0 | 0 | the environment: a WSL distribution that was stopped is running |
| `ground-others` | 23 | 23 | 0 | 0 | 0 | |
| `store-entries` | 53 | 51 | 1 | 0 | 1 | a claim wider than its output; the row not run is arithmetic |
| `rules-on-node` | 45 | 42 | 3 | 0 | 0 | timings |
| `rules-marker-entry` | 55 | 47 | 6 | 2 | 0 | five timings; a block of an output written by an earlier version of its probe. The two "no" are sentences the record itself contradicts |
| `rules-central` | 63 | 51 | 12 | 0 | 0 | six timings and a figure derived from them, marked assumed by the critique; a random identifier; four claims wider than or contradicted by their output |
| `rules-linked` | 81 | 78 | 3 | 0 | 0 | three claims about what was run or counted |
| `outside-file` | 36 | 26 | 6 | 4 | 0 | timings. The four "no" are sentences of the draft and of its check |
| `hardened-custom-data` | 36 | 32 | 3 | 1 | 0 | timings. The "no" is a count that does not come out of its own output |
| `versioned-fields` | 68 | 64 | 2 | 1 | 0 | a timing and a file size that moves by whole cipher blocks; a further row points at the design's table. The "no" is a sentence wider than the probe's input |
| `wildcard` | 82 | 71 | 7 | 3 | 0 | six timings, each faster than the record; a claim wider than its output; a further row is the cost table's heading. The three "no" are contradicted by the record itself |
| `single-entry` | 45 | 42 | 2 | 1 | 0 | timings. The "no" is a count in the digest |
| `grant-only` | 59 | 52 | 7 | 0 | 0 | four timings; a KeePassXC cell that fell on the other side of a second boundary in 1 of 6 runs; two counts the output does not bear out as written |
| `crdt` | 37 | 32 | 2 | 0 | 3 | a total running time; a reading of 19 disagreements. Not run: the searches with KeePassDX's merge, whose command was not kept, and a result that is a reading of the model's source |
| `spikes\storage\model` | 46 | 42 | 3 | 0 | 1 | a running time; two descriptions of the programs. Not run: the search that found six counterexamples, against versions of the model that were not kept |
| `spikes\storage\android` | 69 | 25 | 3 | 0 | 39 | a running time, and two sizes of databases made again with fresh random values. Not run: the emulator flows (9 rows, and 27 of the overwrite and synchronization measurements), the side fact of measured 1.3, whose source was not kept (2 rows), and the claims marked "measured (spike)"; 2 further rows restate tables or are arithmetic |
| `desktop-windows` | 19 | 1 | 0 | 0 | 17 | nothing: a single case of measurement 5 ran twice, alike; a further row is the runtimes, not compared |
| `desktop-hide-and-lock` | 12 | 0 | 0 | 0 | 11 | nothing: nothing ran twice; a further row is the runtimes, not compared |
| `rollback-rule-models` | 25 | 25 | 0 | 0 | 0 | |
| `edited-refusals` | 35 | 32 | 3 | 0 | 0 | three times: the loads and merges of `keepassxc-cli` and the whole run, under heavier load in the second run |
| `store-as-a-whole` | 81 | 79 | 2 | 0 | 0 | a file size moving by a few bytes, and the read's milliseconds; the 15 rows of the store as decided, the 15 of the two readings and the 17 of the wider readings of a copy all reproduced |
| `kdbx-places` | 9 | 8 | 1 | 0 | 0 | a time; within a row reproduced, 7 file sizes 16 bytes apart, a cipher block |
| `source-harness` | 12 | 4 | 1 | 0 | 7 | which KeePassDX merges of the main pair end. Not run again: the bulk runs of the store's records, the long strings, the 30 repeated merges and the sample read by hand; the histories themselves were drawn alike twice |

**What the later families need, and how they ran again.** No run of the seven from a clean clone of
the branch is recorded: each was written and recorded in the branch's working tree, and its second
run, where a family has a second run, ran there.

| Family | What a run needs | Run again |
| --- | --- | --- |
| `desktop-windows` | **a window**: the desktop to itself for minutes at a time, a display of 1280 by 800 or more, Windows' own dialogs in English, a way to start a program under a basic user token, KeePass 2.62 and KeePassXC 2.8.0 from `tools\` | a single case of measurement 5, giving the same events and counts; nothing else |
| `desktop-hide-and-lock` | **a window**, as above, with KeePass 2.62 alone | not run again |
| `rollback-rule-models` | **nothing** but the .NET SDK and the product's commit in the history | twice, byte for byte |
| `edited-refusals` | **downloads** through `tools\`: KeePass 2.62 and the four KeePassXC releases | twice, line for line but times; the extension of R6 twice, alike |
| `store-as-a-whole` | **downloads**, as above | twice, line for line but what `compare.py` masks and the file sizes; the store as decided, the two readings and each of the five wider readings of a copy twice as well, every output of the second run the same as its record line for line but the running times |
| `kdbx-places` | **downloads**: KeePass 2.62 and 2.47, the four KeePassXC releases, kdbxweb from npm, pykeepass and Python packages through `uv` | twice: of 297 comparison files 288 alike line for line, 7 a file size 16 bytes apart, 2 in the record only; no outcome differs |
| `source-harness` | **downloads**, each held to a SHA-256: both programs' sources at their tags, a JDK, the Kotlin compiler, jars, an Argon2 library; KeePass 2.62 | the calibration two or three times, its KeePassDX merges of the main pair ending in 4, 3 and 2 of 5 in three runs; the places and the version table twice; the bulk runs not again |
| `spikes\storage\android`, the KeePassDX and background measurements | **a device**: the emulator under WSL 2, the pinned APKs, a server | not run again; their figures are their reports' |

**The store as decided is a second configuration of `store-as-a-whole`.** From the branch root, in
Windows PowerShell 5.1, `run.ps1 -Decided` runs its parts into `out\decided\`, and
`compare.py --decided` holds them to `recorded-decided\`, with the masks of the first
configuration; the family's `RUN.md`, section "The store as decided", has both commands, each
part's running time in both runs and the 15 figures with their second run. It was run twice, under
an ordinary user token, and the second run printed every output of the first line for line but the
running times. A run of every part empties `out\decided\` and
the databases of this configuration, `work\d-*`, and nothing of the first record's. The first run
after the probe's C# source changes compiles it in every part at once, and parts that start
together can fail to write the same assembly; such a part ends at once with the error in its
`.err`, and running it again uses the assembly the others wrote.

**The readings of sections 4.17 and 4.18 are further configurations of the same family**, each
selected by a switch of `run.ps1` and held to its own record by `compare.py`, from the branch root
in Windows PowerShell 5.1: `run.ps1 -Rewrite` and `compare.py --rewrite` into `recorded-rewrite\`,
`run.ps1 -Copies` and `compare.py --copies` into `recorded-copies\`, and for each of `nothing`,
`profile`, `verb`, `cap` and `cap-items`, `run.ps1 -Reading <name>` and
`compare.py --reading-<name>` into `recorded-reading-<name>\`. The family's `RUN.md` has the
commands under "The two readings" and "The wider readings of a copy", each part's running time in
both runs, and every figure with its second run: 15 rows for the two readings and 17 for the wider
readings, all reproduced. A run of a configuration empties its own output and databases under
`work\` and nothing of the other configurations'. Each needs what the family needs and no more: the
downloads and a console, no network once the downloads are in place (none of the probe's sources
reaches for it), and no window, phone or emulator. Each was run twice under an ordinary user token,
and every output of the second run was the same as its record line for line, with the masks
`compare.py` applies, 12 files of `recorded-rewrite\`, 16 of `recorded-copies\` and 18 in each
`recorded-reading-<name>\`. Before the record, the first run of `-Rewrite` was made again after the
entry it edits was kept the same at every cycle and the control parts were added; the part `copies`
of `-Copies` again with an import's random identifier left out of its output; and every reading once
with an earlier form of `kinds` and `copies`. After the second run, `b-merges` of every reading ran
twice more after its description of H22 was reworded, the first of those runs being its record and
the second printing it line for line. With the new switches off, the parts that `RUN.md` names as
run again after each addition printed their records line for line.

**What the kinds of difference are.**

- **A timing.** Every timing that differs is a single run, or a few, on a machine other
  measurements were sharing. Most later values are above the record. Below the record are the six
  of `wildcard`, the look-ups of `single-entry`, two of the three of `hardened-custom-data` and a
  later run of the fold of `grant-only`; below a paper's figure that no record file holds are the
  whole runs of `grant-only` and `single-entry`, the chains of `rules-central` and the total of
  `crdt`. In `rules-marker-entry`, `wildcard` and `grant-only` a paper's range has an end that
  comes from a run whose output was not kept.
- **A moment of the day or a random draw.** A time the probe reads off the clock (`ground-keepass`),
  an identifier a library draws at random (`rules-central`), a length that depends on a fresh key
  (`ground-inventory`), a file size that moves by a cipher block (`versioned-fields`, `kdbx-places`,
  `store-as-a-whole`, and the databases `spikes\storage\android` makes again).
- **What ran before in the same process.** Which of KeePassDX's merges of the main pair end in the
  harness changed from run to run, as on the device (`source-harness`).
- **A second boundary.** KeePassXC merges the database's custom data by a marker kept to the
  second, so a cell that depends on two loads falling in the same second, or in different
  seconds, can fall the other way with nothing changed: a cell of `grant-only` did, in 1 of 6
  runs, and the fast cells of `hardened-custom-data` and the slow cells of `outside-file` rest on
  the same condition without checking it. A probe of `versioned-fields` races the clock's second
  in a value no claim names.
- **The environment.** The language of the machine names KeePassXC 2.8.0's recycle bin
  (`Papierkorb` in the record and in the later runs), which decides two cells of
  `rules-marker-entry` and a cell of `store-entries`; the current culture decides the collation
  cells of `rules-on-node`. Both were the same in the record and in the later runs.
- **A claim wider than, or contradicted by, its output.** These are section 6.

**What was run again in another way.**

- **The emulator flows of `spikes\storage\android` were not run again.** What they recorded is
  held to the paper by `checks\tables.py`: 388 claims of `measured-android.md` over the recorded
  read-backs, diffs and step logs, of which 386 hold; the two that do not are a single wording
  (section 6). The console part ran again: the six bases, KeePassLib's Synchronize of the 20 phone
  files the record synchronized back into KeePass (200 files, the record's once KeePassDroid's
  `M settingsChanged` is masked) and the MD5 pair. The record's second body of the MD5 pair is a
  later run's file, the original having been overwritten before it was copied; it is held to the
  digest and the nonce the recorded log prints.
- **The later emulator measurements** (sections 2.3 and 3.1) were not run again; their figures are
  their reports' tables. `spikes\storage\android\RUN.md` holds the overwrite and synchronization
  measurements in its sections 11 and 12: `checks\overwrite.py` holds 117 claims of
  `measured-overwrite.md` to the recorded output, and 115 hold; `checks\k2a_synchronize.py` holds
  204 claims of `measured-k2a-synchronize.md`, and 203 hold; the three that do not are in its
  sections 11.5 and 12.5. Its sections 15 and 16 hold the full KeePassDX measurement and the
  background synchronization, with their scripts, flows and probes beside the earlier measurements'
  and their evidence under `recorded\keepassdx\` and `recorded\k2a-background\`; those two sections
  add no row with a "Reproduced" column, and no script under `checks\` holds those two papers'
  tables against their record. `measured-keepassdx.md` section 1.1 cites a `logs\check.txt` for the
  installed package's hash that the evidence does not hold (its section 15.6).
- **The result files** that `ground-measured` re-reads, of the earlier spike's Android run and of
  a KeePassXC run earlier than the run the spike recorded, exist only on the measuring machine.
  Its re-read gave the recorded output again from those files, but for seven `SettingsChanged`
  lines that read as the moment of reading.
- **Outputs whose command was not kept** were made again by scripts written on the branch from
  the logs, the transcripts or the outputs themselves, and each is marked so in its `RUN.md`: the
  comparison of the seven kinds and the source archive's hash in `ground-keepass`, the hand steps
  of `ground-keepassxc`, the kdbxweb and KeePass 2.47 runs of `wildcard` and its survival
  critique's group records, the signature lines of `rules-central` and `rules-linked`, the
  registry and inventory lines, and every count over the product's source and tests. The
  KeePassDX searches of `crdt` were made again with seeds of their own.
- **Sources that changed after their record was written.** In `ground-keepass`,
  `ground-keepassxc`, `ground-others`, `versioned-fields`, `wildcard`, `rules-linked`,
  `rules-marker-entry`, `spikes\storage\android` and the formal critique's liveness probe, a
  probe's source was last changed after some of its recorded output was written. The source as
  carried reproduces those outputs, except a section of a `versioned-fields` dump and a block of a
  `rules-marker-entry` output, on which no claim rests, and the Android base with "protect user
  name" on, which the carried probe cannot make and on which the side fact of measured 1.3 rests.
- **Several probe scripts need PowerShell 7**, not the Windows PowerShell 5.1 the probes' rules
  name: most because they set a property .NET Framework lacks, `ground-measured`'s `run.ps1`
  because its lists are sorted in PowerShell 7's order. Each family's command starts them in
  PowerShell 7, and the commands of `rules-marker-entry` and `ground-measured` run in it whole.
  What is measured runs in .NET Framework programs and in `keepassxc-cli`, except `outside-file`'s
  `Replace.ps1`, which measured `File.Replace` on .NET 11 in PowerShell 7.

## 6. Corrections to the papers

Five kinds of row, by paper and section:

- a sentence that a `RUN.md` or a record shows to be wider than, or contradicted by, the output
  it quotes;
- a figure a paper presents as measured for which no probe or record exists, with what a script
  written on the branch gives where there is such a script;
- a figure that a later run did not give again, which is mostly a timing;
- a limit or a defect of a probe that its `RUN.md` reports and the paper quoting the probe does
  not state;
- a sentence that a later measurement corrects or completes, named with that measurement.

The papers are left as written; the value in the right-hand column is the value that holds. A
reference in parentheses is the row of the family's `RUN.md`. Sections 2 to 4 were written from
the outputs and the `RUN.md` tables, so no row here changes what they state; where a later
measurement corrected a sentence of theirs, the sentence was corrected and the row names it.

What is not listed: a claim a paper itself marks as read or assumed, or whose limit the paper
states itself; a statement the record bears out; a measurement a paper quotes from another
family, which that family's `RUN.md` answers for; and a path a paper names, which
[`papers/README.md`](papers/README.md) maps to its place on the branch.

### 6.1 The ground reports

**`papers\ground\ground-keepass.md`**, family `ground-keepass`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §1a bullet 5 | Overwrite existing: the arriving copy wins whole and the open copy goes to history | the open copy does not go to history in three cells the table itself marks lost: neither time moved, in both directions, and B's time not moved, into B. In each the history already holds an item at that time (K6) |
| §1b row 2 | a file saved holding 9 history items under a limit of 3 reads back with 3 | `n=3` is printed; the 9 is how the probe built the entry and is not printed, and the probe does not tell a trim at save from a trim at load (K20) |
| §1c | an entry with no modification time reads as `lm=+7201` | `+7200` in both later runs: the number is the moment of the run. That the time equals `PwDefs.DtDefaultNow` holds in each (K9) |
| §2.1 | KeePass 2.62 never writes a file below KDBX 4.0 | the cases the probe ran give 4.0; "never" rests on the reading of `KdbxFile.cs:351-397` (K25) |
| §2.1 | every feature set "and all of it read back" | the strings, the attachment, the tag, the expiry and the history item are printed after the reopen; the icon, the colors, the override URL and the auto-type switch are set and not printed (K26) |
| §2.2 | the ten rows of the deletion and move table | each cell as quoted. Two things the cells pass over: Keep existing into A also loses B's later change in the moved row, and the tree prints how many history items an entry has, not which versions (K28) |
| §2.4 | Create new IDs names the arriving root after the arriving database | every database of the probe has the root `new`, so the output cannot tell that from "named like the open root"; the name rests on the reading of `PwDatabase.cs:804-805` (K29) |
| §3 row 1 | a group keeps no history | read; no output can show it (K31) |
| §3 row 9 | Delete Empty Groups acts "with no confirmation found" | the probe calls `PwDatabase.DeleteEmptyGroups`; the menu's handler is read (K35) |
| §7 rows 3 to 5 | no method removes a key of the header's public custom data | no copy removed a key in the probe; what is shown is that no result lacks a key the open copy held (K42) |
| §9 row 11 | a field reference finds the KPG entry | the probe searches with the parameters `SprEngine.FindRefTarget` is read to set; no `{REF:...}` is compiled (K46) |
| §1a | the seven kinds of change were "grouped by a script" | no such script was kept; `checks\x2.py`, written on the branch, makes the comparison and finds 120 of 120 cells identical (K2) |
| "What was run" | the source archive's hash and size, marked measured | no command or output was kept; `source.ps1`, written on the branch, measures both and they agree (K47) |

**`papers\ground\ground-inventory.md`**, family `ground-inventory`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §1.1 D3, §8 row 2 | the client certificate is 487 bytes of DER | 487, 488 or 489 bytes from run to run (16, 25 and 13 of 54 runs); the record that carries it is 819 characters in all 54 (I2, I3) |
| §1.1 D1, §8 row 1 | the authority document is 2903 characters | 2903 in 41 of 54 runs and 2891 in 13 (I1) |
| §1.2 N1, §8 row 8 | 4482 characters is the largest rule the reader accepts | the probe shows that this rule reads, not that a larger rule is refused; "largest" rests on the limits read in `AclDocument.cs:80` and `JsonBody.cs:58-61` (I8) |
| §1 | "Sizes are measured (here) unless marked" | `sizes.ps1` prints the authority's key cut to 40 characters and no other key; the key lengths (78, 85, 75, 76, 83, 12) and the fixed values (7, 20, 85) are arithmetic on the formats, which agree with the product at `a42e38aa` |
| §1.4 | some 14 KB of KPG data in the example database | arithmetic: 13.9 KB as the paper sums it, without the keys of the ten records; about 14.7 KB with every key |
| §8 | the history defaults | `sizes.ps1` prints them empty, a defect of the probe left in place; the quoted line was appended by a command that was not kept, and `constants.ps1`, written on the branch, prints the same line (I9, I10) |

**`papers\ground\ground-keepassxc.md`**, family `ground-keepassxc`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §1 | a merge under the slow key derivation took 7.3 to 10.7 seconds | 10.6 to 14.5 and 9.2 to 13.4 seconds in the two later runs; the conclusion, two loads in different seconds, holds (C03) |
| §4 | between the fast and the slow results "the only differing line was the recycle bin's UUID" | the two sets differ in every identifier, drawn at random; with identifiers replaced by names the results are the same in every cell (C02) |
| §6 | the plain save table compares a save of A, "a KDBX 4.1 file holding every candidate feature", with A | A holds no exclusion from reports, no group custom data and no group flags but the bin's. Those rows rest on set 2 merged into A (X03, X04, X05) and on a fresh dump of the save (V09), which agree with the table |
| §6 | `_LAST_MODIFIED` added to group custom data, named for 2.4.2 | added by all four releases (X04) |
| §2 item 6, §4 | `hist-big` keeps "1.2 MiB" | three history items of the probe's 400 KiB each; the size is computed (R17) |
| §1 | both programs validly signed | the record holds the signature of `keepassxc-cli.exe` and the version of `KeePassXC.exe`; the later run checks both signatures (Z01) |
| §2 item 8 | a group time raised without copying the field makes the stale values look current to KeePass, marked measured | measured for the group tag only; §9 marks the flags and the custom data assumed (K02) |
| §1, §2, §4, §5, §6 | the raw history counts, the unknown XML, the raw tag text, the history limit, and the inputs' versions and the dedicated group's 21 entries and five subgroups | no output was kept; `replay\Replay.ps1`, written on the branch from the logs, gives each number again, the inputs in the second later run only (the rows marked "no record") |
| §4, `bin-A` | the arriving bin group has searching and auto-type off, in all 16 cells | the record's merge dumps print no group flags; the round trip's dumps print them off, and the later runs print them off in all 16 cells (R24) |

**`papers\ground\ground-others.md`**, family `ground-others`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §4 | the history limits are "carried by every writer measured" | only kdbxweb's XML export prints them; no output of pykeepass or of KeePass 2.62 prints a limit |
| §4 | the limits are read off "kdbxweb's output of the base file" | the file is kdbxweb's export of its resave of A, holding the limits KeePassLib wrote |
| §3 | gokeepasslib has 79 known importing modules | read through a fetch tool with no record; fetched again on 2026-11-16: 79 |
| §2 | kdbxweb 2.1.1 was published 2021-09-06 | the record holds no date; `npm view` on 2026-11-16 gives `2021-09-06T19:57:25.199Z` |
| §2, appendix | the inputs A, B, Btie, Bcd and Bg | no output of the record shows them; `out\inputs.txt`, which the part `inputs` added on the branch writes and which has no record, matches the appendix line for line |

**`papers\ground\ground-measured.md`**, family `ground-measured`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §2 | the earlier KeePassXC run's bases were made with the same keys, values, times and rules as `Paired.cs` | a comparison of two sources made by reading; the second source is on no branch |
| §6.3 | the WSL distribution is stopped | `Stopped` in the record, `Running` on 2026-11-16: the environment |
| §1, §3.6 | the limits read back as 10 items, 6291456 bytes and 365 days in every file | read by eye in the record; `out\limits.txt`, the output of a check added on the branch, with no record: 172 files, none otherwise |
| §3.5, §3.7, §4.1 | E1 and E2 keep their identifiers and parents | the times were counted (0 at another time); identifiers and parents are in every dump line and were not counted |
| §3.8 | KeePassXC writes back the `SettingsChanged` it was given | the record holds two KeePassXC values outside the bases its lists name; `out\xc-reread-slow-bases.txt`, the output of a check added on the branch, with no record, finds both in the five slow bases |
| whole | every statement marked measured (re-read) | rests on result files that exist only on the measuring machine (KeePassXC) or that only a new emulator run makes again, with other times and hashes (Android) |

**`papers\ground\measured-android.md` and `papers\ground\ground-android.md`**, family
`spikes\storage\android`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| measured 5.3, "merge PC with C" and "merge PC with P", into the phone's file | "the same, but `M2` becomes C's version" and "the phone's merge result exactly" | both also take the root group's access time from KeePass's copy. These are the two of 388 claims `checks\tables.py` finds not to hold |
| measured 1.3 | the side fact, 42 lines | the 42 lines are the record's file, a heading and 41 lines of diff; the source that made the base with "protect user name" on was replaced eight seconds later and not kept, so the finding stands on the record alone |
| measured 1.2 | system image revision 16, platform-tools 37.0.1, Linux 6.18.58.1 | nothing recorded; the logs name the image, the emulator 37.2.13.0 and build-tools 37.0.0 |
| measured 1.2 | the device's cores, memory, data size and keyboard; a wiped start as root with animations off | set by scripts whose output was not recorded |
| measured 1.2 | the second emulator exhausted the memory (91 MB free) | its log shows a boot of 194 s and nothing about memory |
| measured 2.1, 6 | Keepass2Android's group menu and that it offers no "empty the recycle bin" | no recorded log lists that menu; KeePassDroid's menus are recorded |
| measured 7 | every operation, then KeePass's Synchronize in both directions | no phone file of Keepass2Android's rename, delete or move, of KeePassDX's move or of any AES-KDF save was synchronized: `recorded\sync\` holds 20 labels, none of them those runs. Synchronized later from the same files, in both directions, nothing either program wrote was lost (`papers\ground\measured-keepassdx.md` section 9) |
| ground 5.1 | "72,707 random tries" | the probe tries the nonces 1, 2, 3 in order and draws nothing at random, which is why the count is the same in every run |
| ground 5.1 | the pair was found in 0.16 s | 0.20 s in the later run: a running time |

**`papers\ground\measured-store-as-a-whole.md`**, family `store-as-a-whole`. The paper gives two
counts of its record in several forms; `papers\ground\measured-decided-store.md` section 7 names the
cells each form belongs to, and a script over `recorded\` gives the same ranges.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §2.6 | the repair made a file narrower than the oracle in "8 to 74" histories of 20,000 a cell | the design's search alone, `store-search`, 24 cells: 16 to 74 over two files and 8 to 47 over three (`info:repair-narrower`) |
| §6, the repair's row | "9 to 85 histories a cell made narrower" | the store's own rows of `store-ablations`, 10 cells: 20 to 85 over two files and 9 to 18 over three; the search with no hand loss of the authority's entry, `store-search-noauth`, 24 cells, gives the same range: 20 to 85 and 9 to 40 |
| §7 | "8 to 85 histories of 20,000 a cell" made narrower | both searches together, 48 cells |
| §5 | a rule lost when KeePass ends before a save, "60 to 115 a cell" | both searches together, 48 cells: `store-search` 86 to 106 over two files and 60 to 89 over three, `store-search-noauth` 80 to 115 and 66 to 90 (`accepted:unsaved-rule-lost`) |
| §6, the row of the rule written at the next save | "71 to 111 histories a cell lose a rule" | the store's own rows of `store-ablations`, 10 cells: 80 to 111 over two files and 71 to 90 over three |

**`papers\ground\measured-decided-store.md`**, family `store-as-a-whole`, configuration `-Decided`.
The paper was written while a point was open that has since been run and decided, and the
family's `RUN.md` now names where it was run.

| Where | The paper | What holds now |
| --- | --- | --- |
| §1, "Open, read narrowly" | `EVALUATION.md` section 8 leaves open whether the plugin writes again a record that counts by its history | run as the configuration `-Rewrite` (section 4.17) and taken into the design, `EVALUATION.md` section 2, decision 18; this paper's own counts are of the store that writes nothing again and stand as that |

**`papers\ground\measured-two-readings.md`**, family `store-as-a-whole`, configuration `-Copies`.
The later report, `papers\ground\measured-copies-extended.md`, corrects and completes it on two
points.

| Where | The paper | What the output shows |
| --- | --- | --- |
| §3.1, the row of a retirement; §3.4, the row of a retirement that settles | a retirement counts from a copy as "its refusal of the authority", and its settling which authority is served, where two stand, is a case named and not decided | the reading as run also let a copied retirement settle which authority is served, a defect the family's `RUN.md` lists for the configuration; no cell of the run held two standing authorities at the import, so no figure of the paper changes. The later report rules the settling out with a file built for it: allowed to settle, the copied retirement serves an authority the file's own records do not serve and admits its client c1, more than the file's own records give; counted only as a refusal, nothing is served (`kinds`, the later report §6.1) |
| §3.5, H13 | H13 holding no store key after the write is "a cost of counting a retirement from a copy" | a cost of writing the copied retirement under its own identifier, so that the plugin's next write took out the authority's entry as retired. With the plugin writing nothing from a copy and deciding every deletion from the file's own records, the key was held in H13 and in every other history, under every reading of the later report; its control, which writes from a copy, loses the key in H13 again (the later report §1.4 and §5) |

### 6.2 The options

The paper named in the first column of a table is in the directory its heading names.

**`papers\options\store-entries`**, family `store-entries`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `critique-survival.md` §3 P4 | every KeePassLib method that merged the stale copy in brought the store back, served the authority and admitted the unrevoked client | four of five methods. Under Create new IDs the store came back as a group under a new identifier and nothing was served |
| `design.md` §7 | KeePassXC adds `_LAST_MODIFIED` and `KPXC_RANDOM_SLUG` to the database's custom data in 2.8.0 | 2.7.12 adds both too; 2.4.2 and 2.6.6 add none here, where the dictionary is empty, since this option keeps nothing in it (section 2.2 has the case of a dictionary that holds a key) |
| `design.md` §7 | the same decision made in both copies is one entry with "one history item holding the same content" | one history item; the dump prints a history item's time and not its strings |
| `design.md` §3.1 | the kind is named `removed` | the probe names it `profile-removed`, so its derived identifiers are not the design's; no merge depends on that |
| `design.md` §3.1 | the sizes of an authority, a client record and a revocation | taken from `ground-inventory.md`; the probe's stand-ins are 2,883, 693 and 80 characters. Only the profile record's 45 characters is this probe's |
| `design.md` §4 | about 0.19 microseconds an entry, so 0.1 ms for 500 entries | arithmetic on the 1.9 ms pass, not a measurement at 500 entries |
| `design.md` §4, `critique-security-and-fit.md` M1 | one pass: 1.9 ms | 1.871 ms recorded; 3.720 and 2.066 ms in two full runs and 1.824 to 2.199 ms in five runs of the walk alone |
| `critique-security-and-fit.md` S1 | File then Import merges such a format with Create new IDs | the probe shows `SupportsUuids=False` and runs the merge itself; that the import chooses the method is read from `ImportUtil.cs` |
| `critique-security-and-fit.md` M8; `digest.md` §5 | 49 test files "touch the database-level store or its merge behavior by name" | the list is recorded and the search that made it is not; the closest search tried finds 51 files, 46 of them on the list. No count of lines or methods was recorded; counted on the branch: 26,434 lines and 734 `[Test]` lines |
| `critique-security-and-fit.md`, costs; `digest.md` §5 | 42 source files | neither a list nor a search was recorded |
| `critique-security-and-fit.md` S4 | pykeepass deleted "a store entry" and wrote no deletion record | the entry deleted is store-like, made by pykeepass in a group of its own and not in the design's derived group; 0 `DeletedObject` elements |
| `design.md`, `critique-survival.md`, both `Run.ps1` | nothing about the shell | both need PowerShell 7, since their `Invoke-Cli` sets a property .NET Framework lacks; nothing measured runs in that shell |

**`papers\options\rules-on-node`**, family `rules-on-node`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `design.md` §12 | the rule entry or seal deleted for good refuses "after a KeePass synchronize" | the copy that deleted refuses under every method; the other copy refuses only under Synchronize (D3, D13) |
| `design.md` §1.3, §5.2 row 1 | the sealed rule is "found in history, all methods and directions" | from history in 6 of 8 KeePass cells and both KeePassXC cells; in Overwrite existing into B and Keep existing into A the text is on the node (D5) |
| `design.md` §5.2 row 2 | a tie refuses when the rule's version lost | Overwrite existing into A is a cell the rule's version lost where the rule is served from history and not refused (D6) |
| `design.md` §5.3 row 1 | the merge of scenario 08 took "both copies' other entries" | the rule in force is printed; which entries the group holds afterwards is not (D10) |
| `design.md` §5.1, `critique-survival.md` §3 | a KeePassXC save gives the rule entry empty standard strings, and in the critique adds them "to rule entries and seals" | the probe prints the names of the rule entry's strings, not their values, and nothing of the seals (D4) |
| `design.md` §1.1 | KeePassDX treats two attachments as the same on 32 bits of a hash, marked measured | a reading of the source in `ground-android.md` §5.1; no device run measures it |
| `design.md` §3, `digest.md` §5 | 1.4 ms for 2000 seals, 2.6 ms for 20000 entries, 3 microseconds a digest | 1.33 to 2.46 ms, 3.16 to 6.87 ms and 2.39 to 9.89 ms per 1000 digests over seven later runs (D19 to D21) |
| `critique-survival.md` §0 | each widening "has a change that closes it, the first three measured here" | two are measured (V in C1 to C3, the prune without records in C4); the nonce has no variant in the probe |
| `critique-survival.md` §3 | "silence, 4 of 4" on a row naming three methods | the 4 cells into the deleting copy include Synchronize (S4) |
| `critique-security-and-fit.md` §2 (e) | 37 test files name a `CustomDataKey`, marked measured | no search output was recorded; a search at `a42e38aa` names 37 files in the whole tree, 12 under `src/` and 25 under `tests/` |
| `critique-security-and-fit.md` P5 | the look-alike names | the result depends on the current culture; both runs were under `de-DE`, and the collation script shows the same in three more cultures |
| `design.md` §2.3 | the read checks a seal's identifier against its strings, and the seals sit in a subgroup | the design's probe does neither; no scenario it runs changes a seal's strings, and the security probe, which uses the subgroup, agrees where the two overlap |

**`papers\options\rules-marker-entry`**, family `rules-marker-entry`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `design.md` §2.5 | a group moved in one copy while the other changed its rule ends "in every merge, at the new place with the new rule" | the new rule in 28 of 28 results, the new place in 25. Under Overwrite if newer, Overwrite existing and Keep existing of A into B the group stays at its old place. The record shows the same (D13) |
| `design.md` §9 | "Every file KeePassLib 2.62 ... wrote in this run is 4.0" | four files KeePassLib wrote are 4.1: the two chains and the two hand deletions after a KeePassXC 2.8.0 merge. The record shows the same (D34) |
| `design.md` §3.5; the view also `critique-security-and-fit.md` M4 and `digest.md` §5 | the pass alone 1.6 to 1.8 ms; the view 3.0 to 3.2 ms; 1.6 microseconds a check; a listing 23 to 24 ms; about 0.3 microseconds an entry | the record holds a single run (1.798, 3.066, 1.63, 24.300; 0.29 derived); the quoted ranges came from runs not kept, and the recorded listing is above its range. Six later runs: 1.755 to 2.386, 3.703 to 6.109, 1.65 to 2.59, 26.656 to 33.739; 0.36 to 0.59 (D17 to D21) |
| `design.md` §3.1 | reading only the node's group "would have given one of them" | the probe reads the whole tree and nothing else |
| `design.md` §6 | a group deleted for good while the other copy changes its rule comes back with the new rule entry and none of its older entries | in the 9 KeePassLib results the deletion reached; the 3 merges into B other than Synchronize never removed the group, and with `keepassxc-cli` the older entry comes back in 16 of 16 (D25) |
| `design.md` §7 | moving a recycled rule entry back out restores the rule; removing a copied rule entry reopens the group; the copy of a duplicated group refuses | no run moves a recycled rule entry back, removes a copy while the original stands, or duplicates a group; only the import with new identifiers ran |
| `design.md` §7 | Delete Duplicate Entries, five groups holding the same rule, "two with the same `KPG.After`" | `KPG.After` is not printed; read off the source (D32) |
| `design.md` §9 | 34 of 46 files | 46 evaluations of 43 files (D33) |
| `critique-security-and-fit.md` S1 | Delete Duplicate Entries removes the original "because it prefers the older" | the original is the older by a second and is the entry deleted (F3) |
| `critique-security-and-fit.md`, costs; `digest.md` §5 | 19 host test files hold 101 references | neither the pattern nor the output was kept. The closest search gives the three named per-file counts exactly, and 18 files and 99 references |
| `digest.md` §2 | "P1a widens 6 merges" in KeePass | 10 of 10 results; the 6 are the KeePassLib results among them (V2) |
| a survival output, quoted nowhere | the `s8` block inside `s5-s9.txt` | written by an earlier version of the probe that is not on the branch; the later run gives a different block. `s8.txt`, which P5 quotes, is the current probe's (V15) |
| `design.md` and `critique-survival.md`, both `Run.ps1` | nothing about the shell or the language | both need PowerShell 7, and the design's names the recycle bin KeePassXC 2.8.0 creates in the machine's language (`Papierkorb`): two cells depend on it, no merge cell |
| `digest.md` §2, §3 item 4, §4 | Duplicate Group's copy refuses, and Duplicate Group then the tab's Remove opens the copies, marked measured | no run duplicates a group; only the import with new identifiers ran (D10, V10) |
| `critique-survival.md` §5 | pykeepass 4.2.0 made the deletion of s6 | the record does not print the version, and `pk_delete.py` runs whatever `uv` resolves; the later run resolved 4.2.0 |

**`papers\options\rules-central`**, family `rules-central`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `design.md` §2.2 | a rule entry "with a 50 character rule and one predecessor holds 232 characters of strings" | the entry measured holds a rule of 33 characters; no rule in the probe is 50 characters. The probe prints the wrong label, and the paper repeats it |
| `design.md` §6.1, scenario 01 | B's password is there too, in every cell | in 14 of 16 cells. Overwrite existing into B and Keep existing into A keep the base password |
| `design.md` §6.1, scenario 01 | B's entries are there too | the probe prints neither the entry B adds nor the user name B edits |
| `design.md` §10 | KeePassXC 2.7.12 and 2.8.0 wrote KDBX 4.1 "for the one scenario in which a merge moved an entry" | in two scenarios, 08 and 10, both into A, in both releases; both are merges that moved an entry |
| `design.md` §5.4 | rules removed after their node was deleted make the node refuse when another copy brings it back, scenarios 13 and 14 | under KeePassLib. Into A, a KeePassXC merge puts the rule back in force in scenario 13, as §6.3 of the same paper states, and refuses in scenario 14 |
| `design.md` §2.2, §4; `critique-security-and-fit.md` M2; `digest.md` §5 | the read pass: 0.17, 1.13, 1.43, 0.14 and 0.55 ms at 10,060 entries; 0.86, 8.16, 9.38, 1.06 and 4.54 ms at 50,060; about 0.19 microseconds an entry | every figure at 10,060 entries came out above the record in nine later runs (the whole reading 1.500 to 2.435 ms); at 50,060 the mark test came out above it as well (0.939 to 1.596 ms) and the others on both sides of it (the whole reading 8.317 to 12.426 ms); 0.16 to 0.26 microseconds an entry |
| `design.md` §8 | "Duplicates, copies or pastes a governed node: the copy carries no rule", marked measured | no duplicate, copy or paste was run; the only copy made is the import with new identifiers |
| `design.md` §8 | "Moves a rule entry, or the group, anywhere: nothing", marked measured | a single rule entry is moved into the recycle bin; no run moves the group |
| `design.md` §6.4, §8 | a duplicated rule entry or group brings `503 undecided` | the probe counts the copies an import makes (3) and holds no gate; the `503` is the design's rule |
| `critique-survival.md` P1 C2 | a Synchronize "of either file" with the deleting copy refuses again | run for the model's file; the pykeepass file was synchronized with the base only |
| `critique-survival.md` P1 C4 | "three rule-entry records and the group's", and the group refuses at the ruled group and at the entry | the probe counts marked records only and stops at the entry's refusal |
| `critique-survival.md` §8 | 4.5 seconds | not in the record; 3.8 and 2.5 s later |
| `critique-survival.md` §8 | KeePass's signature and both KeePassXC archives "checked" | nothing was recorded; the tools check both at every run |
| `critique-security-and-fit.md` S1 path A | the Create new IDs copy arrives "with its four strings intact" | two of the four strings are shown to be there; the other two are never printed |
| `critique-security-and-fit.md` lens (e) | the test counts of eleven files, marked measured | no command or output was kept; counted on the branch, each count equals the occurrences of `[Test` in its file, fixture attribute included |
| `digest.md` §5 | "174 host tests write rules as node custom data" | 174 occurrences, of which 11 are `[TestFixture]`: 163 tests |

**`papers\options\rules-linked`**, family `rules-linked`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `critique-security-and-fit.md` S2 | the planted anchor lands in history "in 5 of 6 methods and both KeePassXC merges", and S2 is "measured in 5 of 6 KeePassLib methods and both KeePassXC releases" | six merges ran, four KeePassLib methods and two KeePassXC releases; the history carries the anchor in 5 of the 6: 3 of 4 KeePassLib methods and both KeePassXC merges |
| `critique-security-and-fit.md`, "What I ran" | the probe "adds six cases" | seven cases in the outputs |
| `critique-security-and-fit.md` lens (e); `digest.md` §5 | "27 host-test files name the rule's custom-data key or the dialog's copy" | no command or output was kept. A reconstructed search gives 27 files in both tiers, 23 host and 4 unit, and counts files that name only the profile list's key; with the rule's key alone, 18 files, all host |
| `critique-security-and-fit.md` M1 | the link is listed among the templates | the list is the probe's own reading of the group's entries, not KeePass's menu |
| `critique-security-and-fit.md`, "What I ran" | both `keepassxc-cli` signatures "checked again before the run" | nothing was recorded |
| `critique-survival.md` P2, C3b | "with the store group gone the gate has no authority", and it "stays gone" | the probe models no authority and prints no group's presence; it shows every ruled node refusing |
| `critique-survival.md` §9 | the whole run took under 3 seconds | about 1 s by the record's own times; 1.7 to 2.1 s warm and 5.9 s cold later |
| `design.md` §5.1, §5.2, §5.4, §7 | "the copy of S sealed", "S still sealed" | in those runs the copy, or the group, sits at the root, which grants nothing, so the entry's answer is the same with or without the seal; the group's state shows the limit. The survival critique's C1a is the run in which a copied seal holds where its place grants |
| `design.md`, the probe | its verdict on a plain KeePassXC save | reads `NO`, because it also compares the user's entry, to which KeePassXC adds three empty standard fields; the listing shows that this is the whole difference, as `design.md` §2.2 states |
| `design.md` §6 | what KeePass writes is KDBX 4.0 | not printed by the probe; read on the branch from every database of a run: every KeePassLib file 4.0, two KeePassXC targets 4.1 |
| `design.md` §8; `digest.md` §5 | the two entries of a rule change at "about 250 characters each" | not marked measured; by the design's own listing, 272 characters for the record of a first version and 290 to 317 for a first version with a short rule |
| `design.md` §5.5 | with the link, Create new IDs: the arriving group refuses | no entry sits in the arriving group, so the refusal shows only as the group's state |
| `digest.md` §3 item 5 | after the fix, "KeePassXC and KeePassDX merges still restore it", marked measured | the probe ran the case under KeePassLib Synchronize only; the critique marks the rest as read |
| `digest.md` §3 item 9 | a narrowing by a move undone by a KeePassXC merge, marked measured | no probe of this family moves a node |
| `critique-security-and-fit.md`, the record | `probe.txt` | written four minutes before the last change to the probe's source; the final source gives it again line for line |
| `design.md` §1 | "In no merge cell and after no single act by hand was the profile granted more than the operator's last decision at that node", after a list of what was run that includes the plant and the lost version | holds for the 192 cells of the twelve scenarios and for every act by hand but h10, which is three acts. The plant of §5.5 opens an entry to read, list and protected under Synchronize, Keep existing and Overwrite existing, and after the lost version of §5.6 the four KeePassXC merges put the older, wider rule in force; neither is among the twelve scenarios |
| `critique-security-and-fit.md` M5 | no `KPG.` guard in the vault, "zero matches in `KdbxVault.cs`", marked measured | no command or output was kept; a search at `a42e38aa` written on the branch finds no `KPG.` and a single `KPG`, in a comment |
| `critique-security-and-fit.md` lens (e) | the counts per file, `GrantEditorInRealDialogsTests` 40, `TagOfTheCallersViewTests` 18, `SkippedOnlyForAReaderTests` 7 and `PendingChangesTests` 63, as names of the rule's key or the dialog's copy | no command or output was kept; a pattern reconstructed to give all four gives them. With the rule's key alone they are 39, 18, 7 and 0: each of the 63 in `PendingChangesTests` is the profile list's key |

**`papers\options\outside-file`**, family `outside-file`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `critique-security-and-fit.md`, "What the design gets right" item 4 and problem 8; `digest.md` §3 item 10 | a holder of the store key who can write the folder "cannot undo a revocation", marked measured by the design's probe | not what the probe measures, and contradicted by the design. The probe writes through the store's own calls, which cannot take away a line another writer asserts; nothing writes as a holder of the key can, sealing the revoking writer's file again with a higher count and without the revocation. `design.md` §2 states that whoever holds the key can "bring a revoked client back" |
| `critique-security-and-fit.md`, the same item | "an emptied record wins", cited with 21 of 21 | no check reaches the branch of the join for two record lines that do not know of each other; the rule is read from the code |
| `critique-survival.md` P1 | "without it pc1 serves too", of the digest rule, marked measured in the model | the model always keeps the digest; no run leaves it out |
| `critique-survival.md` §1 item 2, P2 | the revoked client is admitted "on every computer whose clock is right" | a single such computer in the model |
| `critique-survival.md` §6 | each merge took 9.5 to 13.6 s | 8.9 to 12.2 s later; that the two loads fell in different seconds is read from those durations, as in the record (S8) |
| `digest.md` §2 | a KeePassXC merge removes the keys from a stale copy "loaded a second or more later" | every KeePassXC run of this family uses the slow key derivation and none varies the gap; the condition is the earlier spike's measurement |
| `design.md` §1 item 4, §20 | 21 checks over "a correct merge" by a synchronizer | the synchronizer is the probe's own, between two writers of distinct names; the case of one name on two computers is not among the checks |
| `design.md` §1 item 6, §6 | a file put back older "closes the gate (`503 undecided`) and is named on the tab" | the prototype prints `closed`; it holds no gate and no tab. It keeps a count per writer and no digest, so the design's third check is not measured |
| `design.md` §13, §20 | 40 cells, both keys "unchanged" | the values; Overwrite existing from a copy without times leaves both keys without their times. The merges ran over the databases made with the fast key derivation, which the paper does not state |
| `design.md` §13, §18.1; `critique-survival.md` §1, §2; `digest.md` §2 | a KeePass synchronize brings the keys back after a KeePassXC merge removed them | measured through KeePassLib into a copy that never had custom data; not into a copy a KeePassXC merge emptied, which still holds another key or KeePassXC's own, and not through KeePass's window, as `design.md` §21.3 states (D16) |
| `design.md` §20 | 28 cells, the per-key times dropped "by every save" | by the cells that were saved; a merge that brought nothing new left the file as it was |
| `design.md` §20 | the slow key derivation puts the two loads in different seconds | read from how long a merge took (5.2 to 11.4 s recorded, 5.3 to 13.6 s later); the loads' own times are not printed |
| `design.md` §5.2, §8, §9.1, §20, §21.7; `critique-security-and-fit.md` problem 7; `digest.md` §5 | 1.9 ms, 50 microseconds, 26 ns, 18.6 ms; AES-256-GCM over 1 MiB 8.3 and 8.8 ms | 2.22 to 2.48 ms, 58 to 69 microseconds, 25 to 37 ns, 20.3 to 21.4 ms over five later runs; 8.6 to 10.8 and 8.9 to 10.6 ms |
| `design.md` §5.3, §20; `critique-security-and-fit.md` problem 6; `digest.md` §5 | 81,764 bytes | reproduced; the records and revocations in it are stand-ins of 819 and 85 characters |
| `critique-survival.md` P12, the aside; the record | the merge report's lines in `xcsaved.txt` | the recorded run kept more lines than the probe's filter intends, because the path of its working directory matched the filter; the lines the paper quotes match on `KPG` and print either way |
| `design.md` §6; `critique-survival.md` §2, the last row | a writer's file put back older and a writer's file deleted each closed the store through the seen file and, on an installation with no seen file, through what the other writer's file recorded | the deletion was run only on an installation that had seen the store, where both reasons print; the installation with no seen file was given only the older file (D3) |
| `design.md` §4, §16 | written without times, "every KeePass merge method still kept both keys in both directions" | in the 15 cells whose open copy held them; into the copy that never had them they arrived under four methods and not under Keep existing, which copies nothing (D15) |

**`papers\options\hardened-custom-data`**, family `hardened-custom-data`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `critique-survival.md` §0, §4; `digest.md` §3 item 5 | "I measured this in four of five cells: the result held 7 of 14 keys", "lost in 4 of 5 cells" | 3 of the 5 cells the critique's own table lists hold 7 keys. Of all 7 cells the probe prints, the two reconciled first included, 4 hold 7 (V5) |
| `critique-survival.md` §2 M1, §3, §5; `critique-security-and-fit.md` P11; `digest.md` §4 | KeePassXC puts times on the keys of a 4.1 file: "2.7 or later" in M1, §5 and the digest, any release in §3 and P11 | measured for 2.8.0 only, in a save and in a merge; the bound of 2.7 is read from KeePassXC's source (V10, A10, A11) |
| `critique-survival.md` §2 S1 | the ledger "in the union" holds `1.x 2.w 2.y 3.v` | the probe lists the ledger keys of the dictionary, not the union; that the node is behind is the design's table applied by hand, as S1 itself states (V9) |
| `critique-survival.md` §1 | three `keepassxc-cli` runs, the slowest 83 s | the probe starts four; 13.6, 12.1 and 82.8 s recorded and 10.1, 9.9 and 6.0 s later (V11) |
| `critique-security-and-fit.md` P3 step 3 | which place holds what after the merge | the probe prints only the union; the split is the design probe's measurement (A3) |
| `critique-security-and-fit.md` P3 step 5 | KeePassLib Synchronize deletes the node again in both variants | into one copy only, for each variant (A5) |
| `critique-security-and-fit.md` §5 | a slow merge took 7.4 to 18.2 s | 6.7 to 12.1 s later (A6) |
| `critique-security-and-fit.md` §2 (e) | the line counts of fourteen product files, marked measured | no output was recorded; counted on the branch at `a42e38aa`, all fourteen as quoted |
| `critique-survival.md` §1; `critique-security-and-fit.md` §5 | the signature of the `keepassxc-cli` used | no output was recorded |
| `design.md` §13 | a slow merge took 7.9 to 11.0 s | 9.9 to 14.0 s later (D9) |
| `design.md` §5, row "P into stale C", §13 row 5 | with both loads in the same second the dictionary is "C's, unchanged" | its `KPG.` keys are; KeePassXC added `_LAST_MODIFIED` to the dictionary and to the root group's custom data, and `KPXC_RANDOM_SLUG` to the dictionary in 2.8.0. The cell depends on both loads falling in the same second, which held in both runs and which nothing checks (D11) |
| `design.md` §0, §5, §13 | the mirror holds 7 and 12 keys, the dictionary 7 and 12 | as `KPG.` keys. The mirror prints 8 and 13 as made and after KeePassLib, the root group's own rule included, and 9 and 14 after a KeePassXC merge that wrote the target, `_LAST_MODIFIED` included; a dictionary KeePassXC wrote prints 8 or 9 and 13 or 14, its own keys included (D4, D7) |
| `design.md` §11 | `KPXC_RANDOM_SLUG` is fresh at every save | the probe prints the key's name, not its value (D14) |
| `design.md` §6, §10 case 10, §13 | a 2.8.0 merge prints each key it changes with its value | so do 2.6.6 and 2.7.12; 2.4.2 prints none (D13) |
| `design.md` §1, §13 | the access of two `StringDictionaryEx` members | nothing was recorded; the script printed to the console. Run on the branch: both `internal` (D2) |
| `critique-survival.md` §2 M1 step 1, §3 row "KeePassDX 4.5.8" | every KeePassDX and KeePassDroid save writes 4.1 | the emulator runs kept a 4.0 file at 4.0 in both programs (`spikes\storage\android\RUN.md`); `digest.md` §2 records the contradiction |
| `design.md` §0 item 1, §1, §13 | a KeePassXC merge "leaves the target's root group alone" and "never touches it" | its `KPG.` keys and its time are the target's; every merge that wrote the target added `_LAST_MODIFIED` to the root group's custom data, 8 of 8 slow merges and both fast merges into the stale copy |
| `design.md` §4, row "KeePassXC 2.4.2 to 2.8.0" | a save adds `_LAST_MODIFIED` and `KPXC_RANDOM_SLUG` to the dictionary | 2.4.2 adds `_LAST_MODIFIED` alone; `KPXC_RANDOM_SLUG` is in every dictionary 2.7.12 and 2.8.0 wrote and in none 2.4.2 or 2.6.6 wrote (D12, D14) |
| `critique-survival.md` §3, first row | KeePass 2.62 Synchronize and Overwrite if newer, marked measured | both probes ran Synchronize alone; the paper's own F1 table marks Overwrite if newer as read |
| `design.md` §11, the synchronize row | a phone save's deletion reaches the desktop copy at equal times | after a KeePassDroid edit the entry's and its groups' times move, and the deletion reaches KeePass in both directions (`spikes\storage\android\RUN.md`); `digest.md` §2 records the contradiction |
| `design\` and `security-and-fit\`, the probes' usage lines | the tasks | `Probe.ps1` names a task `wipe` its switch lacks, the wiped case being part of `keepass`; `Attack.ps1` names four of its six tasks. No claim rests on either |

**`papers\options\versioned-fields`**, family `versioned-fields`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `critique-survival.md` §1 | none of P1 to P4 needs a hand deletion | P3 starts from a copy whose two tidy entries were deleted without a record, as the same paper's P3 step 3 describes |
| `design.md` §5.3, §7.1 H5; `critique-security-and-fit.md` "What was checked and holds" item 3 | "X keeps A's narrowing in all 16" | under the probe's rule, which reads a node's history always. Under the design's own rule of §5.3 the narrowing is lost in the two KeePassXC cells into the narrowing copy and in four cells into the stale copy (`survival\` P3) |
| `design.md` §7.2 | KeePassXC 2.4.2 to 2.8.0 adds keys of its own to the database's custom data | 2.7.12 and 2.8.0 do; 2.4.2 and 2.6.6 add none here, where the dictionary is empty, since this option keeps nothing in it. To a dictionary that holds a key they add `_LAST_MODIFIED` (section 2.2) |
| `design.md` §7.1, §7.2, §8; `critique-security-and-fit.md` S3; `digest.md` §4 | `503 damaged` in 9 cells and after Delete Duplicate Entries, and Put back from the Data page | the probe prints `CLOSED`; it holds no gate and no Data page, so nothing is put back |
| `design.md` §5.3, §8 | Duplicate Group; File then Import | `PwGroup.Duplicate` and KeePassLib's Create new IDs merge, not KeePass's commands or window |
| `design.md` §8; `digest.md` §4 | "Duplicates a write entry", and a ruled node made from a template, marked measured | every copy of a write entry was made by the import with new identifiers; no template was used |
| `design.md` §5.6; `critique-security-and-fit.md` M4; `digest.md` §5 | a lookup of `KPG.Rule` on every user entry: 12.0 ms at 50,030 entries | 12.043 ms recorded, 10 percent below the fastest of seven later runs (13.323 to 16.895 ms). The other walk timings fall at the fast end of the later runs |
| `design.md` §3.2 | the sizes of a retirement, a standing answer, an accept and a bin marker | the probe never wrote those kinds |
| `design.md` §6.3 | about 17,000 characters a state entry, about 38,000 against some 14,000, 520 small entries a year | arithmetic, half of it on another family's sizes |
| `design.md`, the record | the last section of `dumps.txt`, titled as after Delete Duplicate Entries | shows the file before that command; an earlier version of the probe wrote it. No claim rests on it: the claims about the import rest on the folds and on `newids-detail.txt`, which reproduce |
| `design.md`, the probe's `Run.ps1` | its first line names five KeePassLib methods | it runs four, which is what the paper counts |
| `critique-survival.md` §3 P1 | after a Synchronize the old authority is retired | the output prints no line for it: its entry is removed by the plugin's deletion record |
| `critique-survival.md` §3 P1 | today's store through the same import | the keys the claim names are there in every run; the value of `KPG.Client.c1` under Create new IDs came out empty in a later run, a race with the clock's second in the probe |
| `critique-survival.md` §3 P2 | the compressed files grew from 6,885 to 15,013 bytes | to 14,837 and to 15,173 bytes in the later runs; a file's size moves by whole cipher blocks |
| `critique-survival.md` §8 | the two archive hashes and signatures | nothing was recorded; both hashes are the branch's pins |
| `design.md` §7.2, row "KeePassDX 4.5.8" | KeePassDX "writes 4.1" | the emulator run kept a 4.0 file at 4.0 (`spikes\storage\android\RUN.md`); `digest.md` §2 records the contradiction |
| `critique-security-and-fit.md` "What was checked and holds" item 6, M1 | KeePass's Duplicate Group, with and without its copy suffix | `PwGroup.Duplicate` and `PwEntry.Duplicate`, the suffix added by the probe itself; not KeePass's command |
| `critique-security-and-fit.md` M3, S3 | about 34 KB a closing write; a save of tens of seconds | arithmetic on the design's sizes; the save time is cited from the product's documents; no probe of this family measures a write or a save |
| `design.md`, `critique-survival.md`, the record | the outputs both quote | both probes' sources were last changed after most of their record: the design's after six of its seven outputs, the survival critique's after four of its five and before `refined.txt`, which uses the `fold3` that change added. The sources as carried give every output again but the section of `dumps.txt` named above |
| `design.md`, `critique-survival.md`, both `Run.ps1` | nothing about the shell | both need PowerShell 7, since their `Invoke-Cli` sets a property .NET Framework lacks; nothing measured runs in that shell |

**`papers\options\wildcard`**, family `wildcard`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `design.md` §9 | after an import with Create new IDs, Delete Duplicate Entries deleted the original and left the copy, "5 entries removed, the snapshot under its own identifier among them" | the design's run shows 4 removed, twice, and both snapshots still present: it imported the other copy, whose snapshot differs. No output anywhere shows five. The outcome stated is what the security critique measured on a self-import, with 3 removed |
| `critique-survival.md` §2.8 | the same outcome, cited as measured by the design | the design's record does not show it |
| `design.md` §11 | every file the probe, the four KeePassXC releases, pykeepass and kdbxweb wrote was KDBX 4.0, with "no previous parent" | 180 of the 182 dumps are 4.0. The other two are KeePassXC 2.8.0's `rm` and `mv`, both 4.1; after the `mv` the store group is recorded as the entry's previous parent. Every merge and `add` result is 4.0. kdbxweb's files are not dumped in the record; a check written on the branch reads its 11 files as 4.0 |
| `design.md` §8.2 | a KeePassXC result, then KeePass Synchronize with the other copy, gives "the same joined state as either merge alone", sets U, C and H | holds for U, C and the H chains into A. For the two H chains into B, KeePassXC's merge alone left the copy in force at its old snapshot, and the chain ends gone |
| `design.md` §2, §3.1 | two copies that each made the store group hold one group after a merge, 16 cells | not in the probe's output, which prints a path; a check written on the branch finds one group in all 16. Under Create new IDs the result holds a second group, which the paper does not count |
| `design.md` §3.1 | KeePassXC adds four empty fields without moving the entry's time: all 116 snapshot entries | 116 carry the fields and 112 of those kept their time; the 4 that moved are hand operations that move it by design |
| `design.md` §3.2 | the database holding the base snapshot is 7.1 KB | no output; 7,141 to 7,157 bytes by a check written on the branch |
| `design.md` §3.2 | the base snapshot is 4,968 characters | the stored string: document, a line break and the 44 characters of the seal |
| `design.md` §5.1; `digest.md` §5 | the pass looking for the string at 50,000 entries: 22 to 30 ms; 0.2 ms at 1,000 | the record holds a single run, 29.759 ms and 0.265 ms; the 22 comes from a run that left no output, and the 0.2 is below the record's 0.265. Later runs: 20.2 to 23.3 ms and 0.17 to 0.22 ms |
| `design.md` §5.1 | the other cost columns | six of fifteen values came out below the record in every later run; the record's own three values for the pass over 5,000 deletion records run from 0.060 to 0.080 ms |
| `design.md` §5.3 | 40,000 checks over random histories, none failed | the checks hold the join to its laws as the probe codes it, on rule numbers alone; its histories never prune |
| `design.md` §9 | an import with Create new IDs | the probe imports the other copy, not the database into itself |
| `design.md` §8.1, §11 | the kdbxweb and KeePass 2.47 results | the commands that made those two outputs were not kept; scripts written on the branch make them again line for line |
| `design.md`, the record | the first four recorded outputs | written before the last change to the probe's source; the source as carried reproduces all six |
| `critique-survival.md` §2.1 | the store group's deletion records per file | the command that made `group-records.txt` was not kept; a script written on the branch makes it again |
| `critique-security-and-fit.md` §5.1, §5.2 | the line counts of 27 product files and of 5 test files; 32 plugin files and 52 test files mentioning `CustomData` | nothing was recorded; counted on the branch at `a42e38aa`, all as quoted |
| both `Run.ps1`, the record | nothing about the shell or the language | both need PowerShell 7; KeePassXC 2.8.0 names its bin after the machine's language |
| `design.md` §16 | KeePass 2.47 "checked again against the spike's pin and its Authenticode signature" | nothing was recorded; the tools check both whenever they provide 2.47 |
| `design.md` §8.1, §16 | pykeepass 4.2.0 | the record names no version and the probe pins none; a run on the branch resolved 4.2.0 |
| `design.md` §16; `critique-survival.md` §9 | the matrix, and the critique's runs, each under two minutes | not in the record; 106 s for the matrix and 30 s for the critique's runs later |
| `critique-security-and-fit.md` §3.8 | the design's two runs at `summary.txt` lines 207 to 210 | line 207 is the heading; the two counts and results are lines 208 to 211 |

**`papers\options\single-entry`**, family `single-entry`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `digest.md` §2, KeePassXC row | "ties in 16 of 64 cells" | 16 of 64 cells refused, 8 at the tie and 8 where the loser was kept nowhere behind ten history items; `design.md` §3.3 has it right |
| `design.md` §0, §1; `critique-survival.md` §1, §6 row 1; `digest.md` §2 | the entry was never a mixture, "one copy's entry whole" | measured as the document whole; the entry's other fields are not printed (D2) |
| `design.md` §3.1 | the loser is kept behind twelve later writes "in every cell" | not under Keep existing into B, where the later version is nowhere, as the paper's own table shows (D5) |
| `design.md` §3.2 | 100,000 and 1,000,000 characters kept the loser in KeePass, "both directions" | KeePass ran into one copy only; both directions hold for KeePassXC (D18) |
| `design.md` §12; `critique-survival.md` §6, last row | a document over the size limit ends in a refusal whenever the arriving version is the earlier | only the history limit of 0 was run with the arriving version the earlier; the size cell had it the later (D19) |
| `design.md` §3.4 | the merge table's last row, a stamp older than the window | the probe's merge has no such row, so it never ran (D10) |
| `design.md` §6.4 | KeePassXC left the entry in an ordinary group named "Recycle Bin" that is not the copy's bin | the output names the group only (D22) |
| `design.md` §6.4; `critique-survival.md` §6 | an entry put back from a file the operator picks survives a synchronize | the probe puts back the document the plugin remembered (D23) |
| `design.md` §3.3, §6.1; `critique-security-and-fit.md` M2 | KeePass hands back the same string object for an unchanged entry | two reads in a row on one open database; not across a merge, a save or a reload (D25) |
| `design.md` §6.3, §8 | every string, flag and history item kept through a KeePassXC save; KDBX 4.0 is enough | the document is unprotected in the probe, and no flag or format version is printed (D26) |
| `design.md` §6.1; `digest.md` §5 | a look-up 0.03 and 0.12 ms, a digest 0.03 ms | 0.025, 0.120 and 0.030 ms recorded; 0.007 to 0.009, 0.097 to 0.106 and 0.023 ms later (D24) |
| `design.md` §2.2, §2.1, §3.3 | a window of 256, identifiers of 16 bytes, a witness derived from the version, a merged version's identifier a function of its parents | the design's probe uses a window of 64, 8 bytes, another derivation and a random identifier; the survival critique ran the window of 256 |
| `critique-survival.md` §2.2 | at 20 versions all three readings serve `v300` | they serve `v20`, the newest at that count (V4) |
| `critique-survival.md` §2.6 | the witness Delete Duplicate Entries removed was the state entry's own | the probe lists the kinds left, not which entry went (V9) |
| `critique-survival.md` §9 | about twenty seconds | 13.7 s and 13.3 s for the five parts later; the record is the paper's estimate (V11) |
| `critique-security-and-fit.md` S1, S4; `digest.md` §4 | a foreign file "holding one crafted witness entry and nothing else" makes the gate refuse | the refusal measured came from a file that also carried a state entry (A3) |
| `design.md` §2.1 | a copy of a witness is not a witness, marked measured under Create new IDs | the probe's own rule (`Witnesses` in `Lib.ps1`); what the import contributes is the copy's new identifier (D12) |

**`papers\options\grant-only`**, family `grant-only`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `design.md` §4.1 | after an import with new identifiers the copies of grant records change nothing, "10 of 10 cells" | holds in every Create new IDs cell, of which there are 30; no set of ten is marked in the output |
| `design.md` §5.4; `digest.md` §5 | the fold: 4.0 to 4.8 ms for 10,070 entries | the record holds 4.84 ms; the 4.02 ms behind the lower end is from a run whose file was overwritten. Later: 4.84 and 3.80 ms |
| `design.md` §11 | under the slow key derivation "a merge took 2.4 s" | 2.4 to 2.5 s for 2.8.0 and 2.7 to 3.0 s for the other releases in the record; 2.5 to 3.4 s later |
| `design.md` §4.3; `critique-survival.md` P5; `digest.md` §3 line 13 | the extra KeePassXC run of 8 cells: the key removed in one cell, not carried in another, the other six cut | as recorded in five of six later runs. In the sixth, a third cell (2.8.0, slow key derivation, into the stale copy) left the custom data alone as well; the probe, its inputs and the release were the same, so the moment the steps ran decided it, and which comparison of times KeePassXC makes was not read. The two cells the claim names came out the same in all six |
| `design.md` §3.2 | in `AclResolverTests`, 7 of 30 tests pin a narrowing | 30 methods; the 7 is a reading of the list for which no rule was kept, and the design's own name pattern picks 5 |
| `critique-survival.md` §8 | the whole run took 12 seconds | 12.2 s for the recorded run, not in the file; 11.0 s later |
| `critique-security-and-fit.md`, "What I ran" | a single run, 15 seconds | 14.9 s is an earlier run's; the recorded run's time was not printed; 9.0 s later |
| `critique-security-and-fit.md`, "What I ran" | an earlier identical run differs only in one label | so it does, but no file on the branch holds that run's output, and the carried source prints the corrected label |
| `critique-security-and-fit.md` S1, step 3 | a Keepass2Android Move reaching KeePass, cited to the Android measurement's section 5.1 | that section has no move row, and no phone file of a move had been synchronized back; `digest.md` §2 notes it as inference. Synchronized later, Keepass2Android's move reached KeePass as the phone made it (`papers\ground\measured-keepassdx.md` section 9) |
| `design.md` §4.1, the row "A rule rolled back to an older version" | "2,280 modeled rollbacks widened, none without" the older state holding a letter the newer lacks | the probe's trial puts a random other document on the node, a state the node never had; a trial of rollbacks to states the node really had is section 4.15, and this document's section 4.11 row is corrected to the trial's own terms (`rollback-rule-models`) |
| `design.md` §5.4 | the probe's results were KDBX 4.0 without a withdrawal key and 4.1 with a key, and the key keeps the file at 4.1 | true of the 198 databases KeePassLib wrote. KeePassXC 2.4.2 and 2.6.6 wrote every merge target as 4.0, targets holding the key among them, and six results of the extra run, by 2.4.2 and 2.8.0, hold the key at 4.0 |

**`papers\options\crdt`**, families `crdt` and `spikes\storage\model`.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `critique-survival.md` §1, P6 | none of the 19 steps where a file disagreed with the model altered what the read rule decides, "since elements never change" | 13 are a modification time, which the read rule does not read, and 1 is an import's three entries, already out of force. In 5 a hand-edited head did not arrive where the model predicted it would, so the file reads whole where the prediction is an incomplete store. No check failed, so nothing widened |
| `critique-survival.md` §1, P1; `digest.md` §2 | 48,000 histories clean with the change, KeePassDX's 16,000 among them | 32,000 are reproduced. The KeePassDX third has a recorded output and no kept command; a script written on the branch searched the same four configurations with other seeds and found no failure |
| `critique-survival.md` P2, §9 | the design's model on the four-step history "and on every single deletion after its step 3: one command" | no command or output was kept; a script written on the branch replays it: as claimed, and of 72 single deletions only the newest head without a record widens |
| `critique-survival.md` §9 | about 12 minutes of machine time | about eight and a half minutes for the same commands; no total was recorded |
| `critique-survival.md` §2 | a head's custom strings come out in name order from each KeePassXC release | each release rewrote a head whose strings were already in name order, so the rows show that no release reorders them; KeePassLib's row does show sorting |
| `critique-survival.md` §2 (T2) | a merge never mixes an element's fields | the bridge compares an element's time, intact flag and place with the prediction, not its fields; the flag covers the two strings the read rule reads |
| `critique-survival.md` P4; `digest.md` §3 item 8 | after a new authority, sealed copies go by place, marked as the model's result | a reading of the model's source; no history was run |
| `critique-security-and-fit.md` M6 | the recycle bin is excluded in 20 places in `KdbxVault.cs` | 20 lines name the bin; that each is an exclusion is the critique's reading |
| `critique-security-and-fit.md` S3, M6, lens (f) | the counts | the output was kept and no command; counted again on the branch at `a42e38aa`, all as quoted |
| `design.md` §1 | no single deletion or edit of a store entry made a copy wider, and a revocation was undone only by a whole-file overwrite or where the copy had never been given it | true of every history the search drew. The survival critique's four-step history, which the search did not draw, fails both in the unchanged model: a single deletion makes a copy that was given the revocation admit the revoked client |
| `design.md` §12.4, §5.2, §6.1 | the search found six counterexamples against earlier versions of the model, and "in earlier runs it failed" | those versions were not kept. Replayed in the model as it stands, each of the six holds; items 5 and 6 fail again with the change named for each switched off, and the other four are closed by more than that change, which "each is closed and stays in the search" allows |
| `design.md` §12 | `quick` runs a tenth of the histories | a fifth, 4,000 for 20,000, and searches the exhaustive part one step shallower: 4,562 histories for 127,522. It finds none of the rare failures of the full run |
| `design.md` §12.3 | a full run, 596 seconds | 639 s later |
| `design.md` §1 | failures of two shapes only | only the shortest history of each configuration is printed, as §12.3 of the same paper states |
| `critique-formal.md` §4.1 | the model is about 1,100 lines | 1,195 lines |
| `critique-formal.md` §4.3 | an off-by-one in a first run | the first run was not kept |
| `critique-formal.md`, the record | the liveness probe's output | written before the last change to its source; the final source prints it line for line |
| `critique-formal.md` §2 | no failure that did not go through the first problem | a reading of the shortest histories printed; the others were counted, not printed |
| `design.md` §12.4 | a seventh change "came from replaying `rules-linked` survival C3c with a press in the middle" | the run that showed the need was not kept; scenario 10 is that replay, and it holds in the model as it stands |
| `design.md` §6.4 item 5; `digest.md` §3 item 7 | Seal again after both carriers of a standing answer went without a trace: the renewal is asked again; with deletion records the same history holds | no output was kept; a script written on the branch replays it: as claimed |
| `critique-survival.md` P2, P3, P6 | why the design's search missed the four-step history; the model deletes an entry whose time equals its record's | readings of the model's source, not runs: line 609 holds the comparison, and `main` and `alphabet` hold the exhaustive alphabets the explanation describes |
| `design.md` §12.3 | the shortest histories of C, two files, and of I delete "(head)" | both delete the older of two heads, `('head', 'old')`; the history of E deletes the newer |

### 6.3 The draft and its check

**`papers\draft\EVALUATION-draft.md` and `papers\draft\completeness-of-draft.md`.** The rows here
are the sentences a `RUN.md` checks and the sentences that repeat a claim a row of 6.1 or 6.2
corrects, as far as those were found; the draft was not gone through sentence by sentence.

| Where | The paper | What the output shows |
| --- | --- | --- |
| `EVALUATION-draft.md` §3.1 | a KeePassXC merge of a copy from before Create or Recreate removes the authority's key, "8 of 8 in the `outside-file` probe" | the 8 of 8 are stale copies whose custom data holds another key. A stale copy whose custom data is empty kept both keys, 8 of 8, and so did such a copy saved first by 2.4.2 or 2.6.6. The sentence holds for such a copy that 2.7.12 or 2.8.0 has saved. A copy from before Recreate, which holds the old keys, was not run (`outside-file`, Q1) |
| `EVALUATION-draft.md` §3.9 | "A KeePassXC merge from a copy that lacks the two file keys removes them, 8 of 8" | the same: the condition that the copy holds another key is dropped (Q2) |
| `EVALUATION-draft.md` §5.1, first row | in database custom data the key "is removed by a KeePassXC merge of any copy from before Create or Recreate" | the same (Q3) |
| `completeness-of-draft.md`, "Statements that are wrong", item 5 | "KeePassXC 2.7.12 and later adds timed keys of its own to every file it saves" | both releases added `KPXC_RANDOM_SLUG` and `_LAST_MODIFIED` in every save measured, each with no time: every file they wrote in the `outside-file` probes is KDBX 4.0, which holds no per-key time (Q4). The earlier spike records `KPXC_RANDOM_SLUG` with a time of its own when the file is 4.1, and `_LAST_MODIFIED` with none |
| `completeness-of-draft.md`, "Statements that are wrong", item 5 | "Every KeePassDX and KeePassDroid save writes 4.1", marked measured on the emulator | both programs kept a 4.0 file at 4.0 there (`spikes\storage\android\RUN.md`) |
| `completeness-of-draft.md`, the same item | "KeePassXC 2.7 and later keeps the version it read and stamps time-less items", marked measured | measured for 2.8.0 only; the range is read from KeePassXC's source (`hardened-custom-data`, V10) |
| `completeness-of-draft.md`, "Statements that are wrong", item 9 | "a KeePass Synchronize with any copy that holds the key restores it", after a KeePassXC merge removed it | the restoring merge was run into a copy that never had custom data, not into a copy a KeePassXC merge emptied (`outside-file`, D16) |
| `EVALUATION-draft.md` §3.8 | "192 merge cells and 17 acts by hand: never more than the operator's last decision" | true of the 192 cells; of the 17 acts, h10 (version, record and reference removed) gives the node read and list where it was narrowed to nothing (`rules-linked`, h10) |
| `EVALUATION-draft.md` §3.8 | "A rule planted for a single entry granted nothing" | under Overwrite existing an older copy of the entry carrying the planted anchor is taken whole, and the planted rule answers read, list and protected with no press (`rules-linked`, the security critique's M4) |
| `EVALUATION-draft.md` §3.8 | a copy of a sealed group "stayed sealed through Duplicate Entry, Duplicate Group, paste and an import with new identifiers" | as for the design of `rules-linked` in 6.2: Duplicate Group and the import put the copy at the root, which grants nothing, so the seal shows in the group's state and not in an answer; the dragged copy of the survival critique's C1a holds where its place grants |

## 7. Not run and not measured

What no probe on the branch ran, gathered by what running it would take: a change to an option or
the composed store, a window, the emulator, or something else.

### 7.1 Option changes, and the composed store

- **The composed store was run as a whole in two configurations**, in the design's model and over
  files changed by KeePassLib and `keepassxc-cli`: with the repair and the plain stop, as the
  evaluation stood earlier on 2026-11-16 (section 4.13), and as decided, with no repair and the
  history rule (section 4.16), which sections 4.17 and 4.18 run again under further readings. Not
  run on it: the plugin's API; any window, phone or second KeePass with the plugin; a rule waiting
  in memory, on real files; a store key renewed at Recreate; kdbxweb, pykeepass and the phone
  programs as programs; the read on KeePass's thread. Not run on the store as decided: Seal again
  after a stop of the history rule; an edit in both copies, by pykeepass, by a phone program or of a
  history item by script, which section 4.14 ran on a store without heads; Database Maintenance's
  history page; the edited entries under KeePassLib's other methods and KeePassXC's other releases;
  a rule set again after the loss of the store key's carrier; and the model with an entry's history.
  A composition of other options' parts was never run.
- **The readings of the store as decided** (sections 4.17 and 4.18). Not run with the edited record
  written again at the plugin's next own write: writing it again as a new entry beside the edited
  entry; a history limit other than 10 and 0; KeePassXC releases other than 2.8.0 and the slow key
  derivation. Not run for a copy under another identifier: the reading the report calls candidate d
  on its own; a rule's switches and a document's `default`; more than two profiles; a rule on a
  group between the root and the node; a copy under a store key the file does not hold; a copy of an
  authority counted as standing without being served; Create at a file where a copied retirement
  leaves nothing served; a head's items under `profile` and `verb`; the growth of the list of
  acknowledged copies in a head; a merge of copies under KeePassDX's reading; the plugin's own Seal
  again and tab after a stop caused by a copy; KeePass's own copy and paste; and either reading in
  the model's searches.
- **Of the changes the critiques propose, these were run**: the stricter read of `rules-on-node` and
  its prune without records; the ledger left unpruned in `hardened-custom-data`; the deletion record
  for a removed group's link in `rules-linked`, under KeePassLib's Synchronize only; closing
  versions read from unmarked copies and the refined history condition in `versioned-fields`;
  Continue as an acknowledgment in `single-entry`; the cap fold in `grant-only`; for `crdt`,
  elements counted wherever they sit, on real files, and the window dropped, in a model; and for the
  composed store each decided change undone in turn (section 4.13), six reading rules for an
  edited record (section 4.14), the repair dropped with the sixth of those rules adopted (section
  4.16), the edited record written again at the plugin's next own write (section 4.17), and five
  readings of a copy under another identifier (sections 4.17 and 4.18). Every other change a paper
  names was not run, and no change was run on a phone.
- **No option's exact records were given to a phone program on the emulator.** The full KeePassDX
  measurement held the records of the store the final evaluation recommended at the time, write-once
  entries with `KPG.` strings in a group of the plugin's own, keys of the database's custom data and
  deletion records naming no entry; the harness of section 2.7 ran the composed store's records
  through both phone programs' code, strings of up to 262,144 characters among them. On a device no
  string longer than 2,861 characters, no head and no rule version was run, and the first phone
  bases hold no empty custom string, no custom data on the root group and no string named `KPG`
  alone.

### 7.2 What needs a KeePass or KeePassXC window

- **KeePass's own commands**, run in its window in section 2.1: Synchronize, Import with each
  method, Duplicate Entry and Duplicate Group, a drag, Delete, Empty Recycle Bin, Delete Duplicate
  Entries, Delete Empty Groups and Database Maintenance. Not run: the template menu; XML Replace,
  which is read; what each import method kept, item by item, in a file, since an import is not saved
  by itself and only its counts in memory were read; Synchronize with a file under another key, or
  over a network URL.
- **The entry and group dialogs.** The entry dialog against a plugin's write was run (section 2.1);
  not run: a rule written from the grant editor at OK, Cancel and Escape; a plugin's write to an
  open entry's attachments, tags or custom data; the group dialog against a plugin's write, which is
  read to take its copy as the entry dialog does; a rule write while a save is running, which
  `rules-linked` measured through the save's progress callback in a console process; KeePass's
  repair of duplicate identifiers before a save, which `rules-marker-entry` called by reflection.
- **A plugin's guards** (section 2.8) were run with a probe plugin and not the product. Not run:
  export, print and maintenance under the hiding switches, which are read to walk the database; a
  trigger naming an entry; the Find menu's list dialogs reached through KeePass's window manager;
  the answer Yes to the save question under every control locked, and for several entries under a
  cancelled save.
- **KeePassXC's window**: 2.8.0's reload and merges were run (sections 2.2 and 3.1). Not run: 2.7.12
  in its window; its lock, close and Save As over a changed file; its alternative saving method;
  KeeShare; 2.8.0's remote synchronization and its clone of an entry.
- **A timing on KeePass's own thread**, for any option or for the composed store.

### 7.3 What the emulator has not been asked

- **Keepass2Android**: a file reached by a path and not through the system's picker or its own
  storage access; its application timeout; a server other than WebDAV over HTTP and SFTP; long
  periods with the screen off under Doze, a kill during an upload, and the periodic and Wi-Fi-only
  synchronization; the six alternating changes with "Always merge on conflict" off; a group delete;
  a permanent delete outside the bin; history at its limit; a merge of a file with no
  `SettingsChanged`; the releases between 1.08d-r5 and 1.15-r6.
- **KeePassDroid**: every overwrite case but the baseline and its lock; its application timeout; its
  database settings page.
- **KeePassDX**: the write-protected opening, which took the driver's taps in 1 attempt of 5; the
  entry edits after the notes (tags, a custom field added, changed and removed, an attachment added
  and removed, the icon, the expiry); the moves after the first, the group icon and expiry and the
  rename of the plugin's group; templates, the database settings, the master key change and "Save a
  copy to ..."; the biometric and device credential unlocks and the application timeout; why a merge
  of the main pair ends in some runs and not in others; the files of section 2.6 on a device; any
  flavor but libre; 3.x, but for the saves and merges of 3.2.0 and 3.3.0 in the earlier spike's
  record.
- **Every phone program**: the composed store's records; a string longer than 2,861 characters, and
  the entry view of a long protected string; Keepass2Android's and KeePassDroid's copy command; a
  group made in both copies; the version a phone writes for a file of the store.
- **Any device but the emulator**, any Android but 11, any image but x86_64.

### 7.4 Other gaps

- **Programs read and not run**: KeePassium, Strongbox, KeePassKit, keepass-rs and gokeepasslib, and
  the KeePass plugins KeeAgent, KeePassRPC, KPEntryTemplates and KeeTheme (`ground-others` names the
  commit each was read at).
- **A real file synchronizer.** The synchronizer in `outside-file` is the probe's own model of a
  documented rule, and the replacement of `desktop-windows` a rename over the file; no Syncthing,
  Dropbox or OneDrive ran, and no conflict copy was made by any. The WebDAV and SFTP servers of
  sections 2.3 and 3.1 serve a single file and merge nothing.
- **Times below a second.** Every probe sets whole seconds; only KeePass's window kept a fraction of
  a second in memory (section 3.1).
- **Overwrite if newer, Overwrite existing and Keep existing on `hardened-custom-data`**, which ran
  Synchronize and, in its security probe's third scenario, Create new IDs.
- **KeePassXC releases other than the four**, but for 2.4.1 and 2.7.0 in the earlier spike's record,
  and a KeePassXC merge of the database's custom data with a measured gap between its two loads: the
  slow and fast key derivations stand for "different seconds" and "the same second", and no probe
  varies or prints the gap.
- **A KeePass Synchronize of a file kdbxweb merged**, which would carry the deletion records kdbxweb
  writes for unused custom icons (section 2.6); KeePass 2.47's merge; kdbxweb on KDBX 4.0 files.
- **`File.Replace` under a reader on .NET Framework**, which `outside-file` measured on .NET 11.
- **The earlier spike's probes**, which this branch quotes and does not run; the six counterexamples
  the design of `crdt` reports from versions of its model that were not kept; and the earlier runs
  of the models that were not kept: the failures of design 6.1, the replay behind design 12.4's
  seventh change and the first run of formal 4.3.
- **How often any refusal or any waiting grant occurs in ordinary use.** No probe counts frequency.
