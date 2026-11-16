# Measured: Keepass2Android and KeePassDX, run from their own source on Windows

Run 2026-11-16 on Windows, for the storage rethink. The question: what Keepass2Android 1.15-r6's
and KeePassDX 4.5.8's own database code does to a KDBX file on save and on merge, measured in bulk
from their source with no emulator, after each harness was held to the cells the emulator
measurements recorded. A harness is a filter, not a proof: when the code drops something, no run of
the app keeps it on that path, which section 6 lists as exclusions; when the code keeps something,
the emulator still confirms it, and section 6 lists those as candidates. When the app reads, writes
or asks (buttons, dialogs, settings, caching, background work, the storage provider) is not
measured here.

**Everything in a table was run**: a file a harness wrote, read with KeePassLib 2.62 or with the
kdbx-places family's reader of the raw file. What was read from a program's source and not run is
marked **read**. The family is `spikes\storage\probes\source-harness` on the branch
`spikes/storage-survival`; its `RUN.md` has the commands, the downloads and their hashes, the
runtime, and every figure quoted here.

## 1. What was built, and what stands in for what

### 1.1 Keepass2Android 1.15-r6

Source: `https://github.com/PhilippC/keepass2android`, tag `v1.15-r6`, commit
`bbcf3787c75bd5b8a9769576191129b5dd9e8bc0`. The app's own copy of KeePassLib,
`src\KeePassLib2Android`, compiled unchanged for .NET 10 on Windows, every file of it but two:
`Kp2aLog.cs` (the app's logger, replaced by a stand-in that writes nothing) and
`Cryptography\SelfTest.cs` (the start-up self test, which nothing on the load or save path calls).
Beside it the app's `Kp2aBusinessLogic\database\KdbxDatabaseFormat.cs`, unchanged, which is the
format object the app's loader and its save use. No edit to any file of the app.

Stand-ins, all written for the harness:

| Original | Stand-in | What it can change |
| --- | --- | --- |
| `keepass2android.Kp2aLog` (Android logging) | writes nothing | nothing written to a file |
| the native `libargon2` the copy calls through `LibraryImport("argon2")`, and `Java.Lang.JavaSystem.LoadLibrary` | the reference Argon2 for Windows x64 from the NuGet package `Argon2.Bindings` 1.21.0, handed to the runtime by a DLL import resolver; `LoadLibrary` does nothing | key derivation only |
| the native AES-KDF, `Com.Keepassdroid.Crypto.Finalkey.NativeFinalKey` | throws, so the copy takes its own managed fallback, the same transform | key derivation only |
| `Android.Graphics.Bitmap`, `BitmapFactory` | empty objects: a custom icon is never decoded | nothing: the icon's bytes are kept and written as read |
| `Java.IO.FileNotFoundException`; the namespaces `Org.BouncyCastle.*`, `Java.Text`, `Android.Webkit` | an exception type; empty namespaces (named only in `using` lines above code that is compiled out) | nothing |

The driver (`k2a\Driver.cs`) does what the app's code does around the database, step by step,
each step copied in substance from the app's source: the load is `Database.LoadData` (a
`MemoryStream` of the file, `PwDatabase.Open(stream, name, ioc, key, null,
KdbxDatabaseFormat(Default))`); a save is `SaveDb.PerformSaveWithoutCheck`
(`KdbxDatabaseFormat.Save`, which is `PwDatabase.Save(stream, null)`); a merge is
`SaveDb.MergeAndFinish`, which runs `SaveDb.MergeIn` line for line (a new `PwDatabase` given the
open database's key and memory protection, the file now at the location read into it through the
same format, then `PwDatabase.MergeIn(that, PwMergeMethod.Synchronize, null)`) and then saves. The
same merge stands for the answer "Yes, merge", for "Always merge on conflict" and for
`SynchronizeCachedDatabase` with changes on both sides, which all reach `MergeIn` (read). The edits
are those of `EntryEditActivity.SaveEntry` and `UpdateEntryFromUi`, `EntryActivity` (an entry shown
is touched), `AddEntry`, `EditGroup`, `DeleteRunnable.DoDeleteEntry` and `MoveElements`.

**Which version it writes, and why (read, then run in section 5).** The save makes a new
`KdbxFile`, whose version is the largest of what the keys need, of the version that `KdbxFile`
has read, which is none on a save, and of `GetMinKdbxVersionOrig`: 4.1 for a group's tags, an
entry's QualityCheck off, a custom icon's name or time, or a time on a database custom data item;
4.0 for node custom data, ChaCha20, a key derivation other than AES-KDF, or public custom data;
3.1 otherwise. An entry's or group's previous parent group is not among the 4.1 reasons, unlike
KeePassLib 2.62.

### 1.2 KeePassDX 4.5.8

Source: `https://github.com/Kunzisoft/KeePassDX`, tag `4.5.8`, commit
`77ecfa8654a5bd0d79d512f8fa71c0af8a29b554`. It is also the newest release on 2026-11-16 (the
KeePassDX emulator measurement's notes, from GitHub's release list), so a single tag stands for
both. The `database` module's main sources and the Kotlin and Java sources of the `crypto` module,
159 files, compiled for a desktop JVM by kotlinc 2.0.0, the compiler the project pins, with the
parcelize plugin the project applies, targeting Java 17, against jars of the module's own
dependencies (joda-time 2.14.1, commons-io 2.21.0, Bouncy Castle 1.84) and kxml2 2.3.0. No Gradle,
no Android SDK and no Robolectric. Two files are left out of the compilation and replaced by
stand-ins of the same name; no file is edited.

| Original | Stand-in | What it can change |
| --- | --- | --- |
| `NativeArgon2KeyTransformer.java` (JNI into the app's native Argon2) | Argon2 by Bouncy Castle 1.84's `Argon2BytesGenerator`, the same parameters and a 32-byte output | key derivation only |
| `NativeAESKeyTransformer.java` (JNI into the app's native AES) | throws, so the module's own JVM fallback `AESTransformer.transformKeyInJVM` runs | key derivation only |
| `Parcel`, `Parcelable`, `ParcelUuid`, `Bundle`, `Intent` | enough to compile; `Parcel` refuses every call, and nothing is parceled | nothing |
| `android.util.Xml.newSerializer()` | kxml2 2.3.0's `KXmlSerializer`; Android 11 returns libcore's modified copy of it | the two differ on a character XML 1.0 cannot hold, which libcore refuses and kxml2 writes as a character reference (read); no base holds one |
| `XmlPullParserFactory` | finds kxml2's `KXmlParser` | none seen: every calibrated cell agrees |
| `Base64` and its streams | written to Android's documented flags (wrap at 76, trailing newline, padding, URL-safe, lenient decoding) | a binary or key written in Base64, if the flags were wrong; every calibrated cell agrees |
| `Build` (API 30, the emulator's), `Log`, `Uri`, `Color`, `Resources`, `Context`, `ContentResolver`, `pm.*`, `SigningInfo`, `AndroidException`, `SuppressLint` | enough to compile `UriHelper.kt` and `Signature.kt`, which nothing on the paths run here calls | nothing |

The driver (`kpdx\Driver.kt`) restates, over the database module's own code, the bodies of the app
module's runnables, which are not compiled: the load is `LoadDatabaseRunnable` (`loadData` with
`fixDuplicateUUID` off and limits that refuse nothing); a save is `SaveDatabaseRunnable`
(`checkVersion`, then `saveData` through a cache file); a merge is `MergeDatabaseRunnable`
(`mergeData` from the arriving file with its own credentials, then the save), which serves both
"Merge from ..." and "Merge data" after the external-change dialog (read, and in the second record
alike, section 2); an edit is the entry screen's `EntryInfo` with one field changed, then
`UpdateEntryRunnable`; a new entry is `buildNewEntryInfo`, then `AddEntryRunnable`; and showing an
entry or a group is `TouchEntryRunnable` or `TouchGroupRunnable`, which the screens start.

## 2. Calibration against the emulator's record

**How a cell is held to the record.** The bases are made again by the emulator measurement's own
probe (`spikes\storage\android\probe\overwrite\Store.cs`, built unchanged): every base's dump is the
record's line for line but the first, which names the file's hash. A cell is the steps of its
emulator flow that change the object model or write the file, in the harness's script language,
taken from the flow and its step log; the harness writes the files, the same probe dumps them, and
each dump is compared with the recorded dump of the file the program left. Three things are set
aside, and nothing else: the first line; an identifier the program drew at random (an entry it
added); and a time of the run's own moment (any time from 2026-11-15T12:00:00Z on that is not
inside a quoted value, which every base time precedes). The comparison discriminates: the harness's
save of `S-argon` held to the record's save of `S-aes` differs in its KDF line, and a swapped
history in measurement 2's analysis shows as losses (`RUN.md` section 7).

**A variant** is a cell's steps with one step of the app's surrounding code placed otherwise. It is
used for one thing only: Keepass2Android shows an entry again when its editor returns
(`EntryActivity`, which touches the entry and its groups) on the screen's thread while the save
runs on a worker thread, so whether the file holds that touch is a race. A cell that agrees only as
its variant is counted apart.

### 2.1 Keepass2Android

60 cells, 94 recorded dumps: every Keepass2Android run of the first measurement, of the overwrite
measurement and of the synchronize measurement that writes a file through the database code.

| Kind | Cells | Agree | Agree as the variant |
| --- | --- | --- | --- |
| save (an entry added, saved) | 4 | 4 | |
| edit (E1's password; R1's and Gm's titles; G renamed) | 3 | 3 | |
| delete (recycled, then deleted for good), with the copy pulled between | 1 | 1 | |
| move (E1 into N, N to the root), with the copy pulled between | 1 | 1 | |
| merge (C with P arriving, P with C, three runs each) | 6 | 6 | |
| overwrite record: "No, overwrite", no question, the check off | 21 | 21 | |
| overwrite record: "Yes, merge" | 3 | 3 | |
| overwrite record: reopened or reloaded, then an entry added | 5 | 5 | |
| synchronize record: the phone's change only, uploaded | 4 | 4 | |
| synchronize record: both changed, merged at the synchronize | 4 | 4 | |
| synchronize record: both changed, written over at the synchronize | 1 | 1 | |
| synchronize record: a save that merges the server's file | 5 | 3 | 2 |
| synchronize record: a save that writes over it | 2 | 1 | 1 |
| **all** | **60** | **57** | **3** |

**The three that agree only as the variant** (cases 5B, 6 and 7 of the synchronize measurement):
the file holds P2's usage count 3 and, in case 7, its groups' counts 2, where the harness's save
before the touch holds 2 and 1. The same steps in cases 5 and 9 agree without the variant. So the
emulator's file holds the touch in some runs and not in others: the race above, the app's
surrounding code and not its database code. No other line differs.

**One harness defect was found and fixed on the way**: the first script of the synchronize cases 3a
to 3c saved again after the touch, where the run uploaded the edit's own save.

**Not cells, and why**: the overwrite record's two "look" runs and its "Cancel" run write nothing,
so the file is the arriving copy; the synchronize record's cases 1, 2 and 8 reload or copy the
server's file into the cache and write nothing of the database code's own. Those are the storage
layer's and the screen's, which no harness runs.

**Source against the released app**: nothing in this record disagrees with the source at the tag.

### 2.2 KeePassDX

34 cells: the 15 KeePassDX runs of the first measurement, and 19 runs of the second KeePassDX
emulator measurement of 2026-11-16 that write a file. That second record was not yet carried on the
branch; its bases, dumps and step logs were copied from that measurement's working directory when
the calibration ran, and its own probe dumps these cells' files, since its dump names its bases'
nodes by its own aliases.

| Kind | Cells | Agree |
| --- | --- | --- |
| first record: save, edit (E1, R1 and Gm, G renamed), delete with the copy pulled between, move with the copy pulled between | 9 | 9 |
| first record: "Merge from ..." C with P and P with C, three runs each | 6 | 6 |
| second record: "Merge from ..." and "Merge data", the phone's copy with the desktop's both ways, and the bases with attachments, history, history limits and nested groups | 13 | 12 |
| second record: "Save data" after the external-change dialog | 2 | 2 |
| second record: "Reload data", then an entry added | 2 | 2 |
| second record: the merge that never ended on the device (section 2.3) | 2 | 2 |
| **all** | **34** | **33** |

Every merge cell whose merge ended in the harness agrees, among them the six ways section 3.2 of
the KeePassDX emulator report lists in which KeePassDX's merge differs from KeePass's: arriving
deletion records applied and not kept, a record kept beside an entry that came back newer, the open
copy winning at equal times, a move without a new modification time following the arriving copy,
a group deleted on one side taking a later-changed entry with it, and deleted groups coming back
empty. **The one cell that does not agree** is `mfPD-r2`: the device's merge ended, and the
harness's merge of the same copies did not end in the recorded calibration. The two runs that
never ended on the device, `mfPD-r4` and `extmPD-r3`, did not end in the harness either. Which of
these merges ends varied between the three calibration runs made here (section 2.3), and in an
earlier run `extmPD-r3` ended in the harness and wrote the record's `extmPD-r2` line for line.

**The first record's `edit-e1`** edits E1's title: its flow meant to append to the password, and
its step log records the text landing in the title, which the record's file holds.

**Source against the released app**: nothing in either record disagrees with the source at the
tag.

### 2.3 A KeePassDX merge that does not end

**Two runs of the second KeePassDX record never ended on the device**: "Merge from ..." of the
desktop's copy into the phone's (`mfPD-r4`) and "Merge data" in the same direction (`extmPD-r3`)
left the progress dialog up for more than 90 s, and the files were the copies as given (that
measurement's notes); other runs of the same two copies ended in about a second. **The harness
reproduces it from the source**: its job for the same merge, given the same two files, did not end
in some runs and ended in others, and a thread dump of a job that did not end shows it in
`DatabaseKDBXMerger.firstNotDeletedParent` (line 347), called by `deleteGroup` (line 389) from
`merge` (line 308). **Read** at the tag: the loop in `firstNotDeletedParent` assigns
`parent = node.parent` on every pass, so the parent it tests never changes, and once that parent is
itself in the arriving deletion records the loop never ends. It is reached when a group the arriving
copy deleted still holds an entry or a group to move, and its parent group was deleted as well: the
desktop copy's deletion of the nested groups `NA`, `NB`, `NC` is such a case.

**How often** (`RUN.md` section 7.3, a job stopped after 30 s of its own processor time, where a
merge that ends takes under 2 s): the merge with PHONE open, run 30 times, each as the first job of
a fresh JVM, did not end 30 times; the reverse, DESK open, ended 30 times, each file the record's
`mfDP-r1` line for line. In the calibration runs, where the same merge followed other jobs in the
same JVM, it ended in some runs and not in others: in the recorded calibration, `mfPD-r1` and
`extmPD-r2` ended and `mfPD-r2`, `mfPD-r4` and `extmPD-r3` did not. Why the order of earlier jobs
matters was not found in the source; the device's record shows the same mixture.

## 3. The places of the format

**The bases** are the kdbx-places family's phone bases (its `android\make-bases.py`, password
`probe-only`, KDBX 4.1, Argon2d at 16 MiB): `clean-base`, which holds a recognizable value in every
place KeePassLib 2.62 writes; one file per raw place (a header comment, an undefined outer and inner
header field, the end-of-header content, bytes after the last block, an unreferenced binary,
unknown XML elements, attributes and a comment, a time on node custom data); `all-raw`; and two
pairs for merging, `clean-A-diff` with `clean-B-diff` (other values, B's times newer) and
`clean-A-tie` with `clean-B-tie` (B at A's times). Each base went through each harness as a save
(an entry added at the root), an unrelated edit (another group renamed), and an edit of the entry
holding the entry places (its password); each pair was merged both ways, the first file open, and
KeePassLib 2.62's Synchronize of the same pairs ran beside them. The family's `android\check.py`
read every file written, both headers and the decrypted XML, and printed a line per place. The
family's scripts were those of 14:47 on 2026-11-16, while that family was still being written;
`RUN.md` section 8 names their hashes. `recorded\places-table.md` has every place.

**Keepass2Android opened every base. KeePassDX refused six of the twenty**, with the reason its own
code gives: a header comment (field 1) and an undefined outer header field (13), both "Invalid
header type" from `DatabaseHeaderKDBX.readHeaderField`; an unknown element with text in `Meta` and
in a group's `Times` (`XmlPullParserException: Wrong event type`); an empty unknown element in
`MemoryProtection` ("Invalid end element", KeePassDX's own reader); and `all-raw`, which holds the
header comment. So for KeePassDX these are not places that lose data: **a file holding any of them
does not open at all**.

**What each program does to a place, where the two differ from KeePassLib or from each other** (a
save, an unrelated edit and the entry edit wrote the same but where named; every other place was
kept by every save and taken from one side by every merge exactly as KeePassLib's Synchronize took
it):

| Place | Keepass2Android | KeePassDX | KeePassLib's merge |
| --- | --- | --- | --- |
| outer header comment; undefined outer header field | dropped | the file is not opened | |
| undefined inner header field; bytes after the last block | dropped | dropped | |
| an inner header binary no entry refers to | dropped | kept | |
| unknown XML elements, unknown attributes, an XML comment | dropped | unknown elements: the file is not opened; attributes and the comment dropped | |
| `Generator` | written as Keepass2Android writes it (empty) | written as `KeePassDX` | |
| `MasterKeyChangeForceOnce` | kept | dropped; a merge takes it from either side | the open copy's |
| `MemoryProtection` | one of the five flags written `False` | kept; a merge keeps the open copy's | combined |
| a time on a database custom data item (4.1) | **dropped, in every save and merge** | kept | the newer side's |
| a time on a group's or an entry's custom data item | dropped | kept | (not a merge place) |
| group `EnableAutoType`, `EnableSearching` | written in lower case (`true`, `null`), the same value; a merge takes the same side as KeePassLib | kept; a merge at equal times keeps the open copy's | the arriving copy's at equal times |
| an entry's QualityCheck off (4.1) | kept | **dropped by the entry editor**; a merge keeps the open copy's | the newer side's |
| public custom data, a key only one copy holds | both kept | **the open copy's only** | both kept |
| the KDF parameters, an extra key only one copy holds among them | the open copy's | **the arriving copy's** | the open copy's |
| the master key's time and change settings | the open copy's | the arriving copy's when newer | the open copy's |
| group and entry custom data, a key both hold | the newer side's | **the arriving copy's**, newer or not | the newer side's |
| group and entry custom data, a key only one copy holds | as KeePassLib | both kept where KeePassLib takes one side | the newer side's |
| a deletion record both copies hold, its time | the newer side's | the open copy's | the newer side's |
| a deletion record only one copy holds | both kept | **the open copy's only** | both kept |
| an attachment's binary, merged (in `check.py`'s words, for A with B, B with A, and the two ties) | as KeePassLib | `new+open` in all four | `arriving+new`, `new+open`, `arriving`, `arriving` |
| the edited entry | a history item, new modification and access times, usage count up by one, the attachment's flag byte rewritten | the same, and its group's modification and access times moved | |
| `Meta/HeaderHash` | not written | **written by every save and merge**, though KDBX 4 has no use for it | not written |
| `Generator` | written empty, as KeePassLib does when it runs without KeePass's own name | `KeePassDX` | |
| public custom data, a key both copies hold, merged | the newer side's | **the open copy's** in all four merges | the newer side's |
| a custom icon's data, merged | as KeePassLib | the open copy's in all four merges | the newer side's |
| `HistoryMaxItems`, `HistoryMaxSize`, a group's `IsExpanded`, merged | the open copy's | the arriving copy's when it is newer, else the open copy's | the open copy's |
| group and entry fields at equal times (icon, colors, notes, tags, auto-type and the rest) | as KeePassLib | **the open copy's** | the arriving copy's |

Every other place of the inventory, the header's KDF and public custom data, `Meta`'s names,
descriptions, colors, custom icons with names and times, recycle bin and templates settings, history
limits, every group and entry element, tags, auto-type with its associations, override URL, colors,
custom strings and their protection, attachments, history and deletion records, came through every
save of both programs as KeePassLib 2.62 wrote it.

**What the kdbx-places report asks of the phone programs** (its closing section), answered from the
harnesses:

- **Unused custom icons** survive every save and every merge of both programs, and an icon one copy
  alone holds is in every merge result. Neither program wrote a deletion record for an icon in any
  run: a merge added only the arriving copy's own records, and KeePassDX not even those.
- **KeePassDX refuses** the header comment, an undefined outer header field and the unknown XML
  elements measured, as its source suggested: the file is not opened, with the reasons above.
- **KeePassDX's own rules for merging the metadata**, against KeePassLib's: the name, description
  and default user name with their times, the settings time, `LastSelectedGroup`,
  `LastTopVisibleGroup` and `MaintenanceHistoryDays` as KeePassLib; the KDF parameters, its extra
  keys included, **the arriving copy's** in all four merges, where KeePassLib keeps the open copy's;
  the master key's time and its change settings, the history limits and `IsExpanded` the arriving
  copy's when it is newer, where KeePassLib keeps the open copy's; public custom data, a custom
  icon's data and `MemoryProtection` the open copy's, where KeePassLib takes the newer or combines;
  and a group's or an entry's custom data **the arriving copy's in all four**, where KeePassLib
  takes the newer side.
- **Generator, header hash, version**: as the table above; both kept the version of these 4.1 bases
  (section 5 has when they do not).
- **The per-copy places** `LastSelectedGroup`, `LastTopVisibleGroup`, a group's `IsExpanded`,
  `MaintenanceHistoryDays` and the history limits: kept by every save of both, merged as above.
- **The name, description and default user name with their times**: kept by every save of both and
  taken from the newer side by every merge of both, as by KeePassLib.
- **Public custom data**: kept by every save of both; with KeePassDX a key only the arriving copy
  holds is lost in a merge.

## 4. The recommended store's records

**The store** is the one `EVALUATION.md` sections 1 and 4 recommend, as far as a program sees it:
a group of the plugin's own, found by the prefix of its identifier; every datum an ordinary entry
written once, with plain and protected custom strings named `KPG.` (`KPG.Kind`, `KPG.Data`, the
protected `KPG.Mac`, and the authority's protected `KPG.Key`), notes stating that the entry is the
plugin's, auto-type off, no custom data, nothing on any other entry; an identifier that begins with
the kind's three-letter tag; and a head that restates what is in force, written with every write of
the plugin, the head it replaces deleted with a deletion record. The MAC is a hash standing in for
the real one: nothing here verifies it, since what is measured is what a program does to the
entries, not what the plugin concludes from them. The files are KDBX 4.0 as KeePassLib 2.62 writes
them, with AES-KDF at 1,000 rounds so that thousands open quickly (the key derivation does not
change what a program does to an entry; it does change the version Keepass2Android and KeePassDX
write, section 5).

**The histories** (`kplib\StoreLab.cs`, seed 20261116): 2,000 of two copies and 1,000 of three.
Every history starts from an ancestor holding the authority, two profile additions, an admission,
a rule for the group General and a head, and three ordinary entries. Each copy then takes one to
four steps, interleaved on one clock, one step in six at the time of the step before it. A step is
a write of the plugin's own (an admission; a revocation of an admitted client; a rule for another
group; a rule narrowed, the earlier version deleted with a record; a profile name removed) or an
act of a person in KeePass (an ordinary entry's password changed, with a history item; an ordinary
entry added; a store entry moved to the recycle bin, as KeePass's Delete does; a store entry deleted
for good, with a record; deleted with no record, as a script or another program can; the bin
emptied, a record for each entry). A third copy forks from the ancestor or from the first copy
after some of its steps.

**What each program was given**: every copy opened, an entry added and saved; every copy opened,
an ordinary entry edited and saved; and every order of merging the copies: both orders of two, and
for three copies the six orders of all three, each written after the second copy and after the
third. Keepass2Android's merge is the save that finds the file changed, answered "Yes, merge", so an
entry is added before it as the emulator flows did; KeePassDX's is "Merge from ...". KeePassLib
2.62's own Synchronize ran over the same copies in the same orders.

**What was held to what.** For a save, every store entry of the copy against the file written: its
presence, its strings and their protection, its parent group, its history, its times and its usage
count, and every deletion record of a store entry. For a merge, an entry some copy holds and no copy
holds a record for is expected in the result; an entry is held to every version the copies hold
and reported only when it matches none; and every result is also held to KeePassLib's result of the
same copies in the same order. The analysis was checked by swapping files between histories, which
showed as losses and as differences from KeePassLib in every swapped file.

**KeePassLib 2.62's own Synchronize**, 10,000 merges of two and 6,000 of three, kept every store
entry some copy held and no copy recorded as deleted, unchanged, in its place, with its times and
its history, and applied every deletion record, with one exception that is KeePassLib's own: **an
entry whose deletion record bears the same second as its last modification is kept, and the record
dropped** (30 merges of two and 90 of three, in 15 histories of three copies). Every one is a head
written by a write of the plugin in one copy, a third copy forked from it, and a write in that third
copy in the same second that replaced the head with a record: KeePassLib deletes only when the
record is later than the entry. In the store's terms a replaced head survives beside its successor,
which restates everything the older one did.

**Keepass2Android**: in every one of the 7,000 saves and 7,000 edits, every store entry and every
deletion record of a store entry came back as the copy held it: strings, protection, parent, times,
usage, history; no entry lost, changed, duplicated or moved. Its merges, through the same 16,000
orders, **held every store entry and every record exactly as KeePassLib's Synchronize of the same
copies in the same order**, the 120 ties above included: not one merge differs from KeePassLib's.
The one thing every save, edit and merge changed is the version: these AES-KDF files went from
KDBX 4.0 to 3.1, which section 5 explains.

**KeePassDX**: in every one of the 7,000 saves and 7,000 edits, every store entry and every
deletion record of a store entry came back as the copy held it, but for the version (4.0 to 3.1, as
Keepass2Android). **Its merges differ from KeePassLib's in 6,791 of the 10,000 merges of two**, in
these ways and no other:

| What, against KeePassLib's Synchronize of the same copies | Merges of two (10,000) | Merges of three (6,000) |
| --- | --- | --- |
| a store entry's deletion record KeePassLib keeps is missing: an arriving record is applied and not kept | 10,140 records, in 2,751 of 3,000 histories | 10,160 records |
| **a store entry KeePassLib removes is present**: deleted with a record in one copy, the record lost in the first merge, the entry brought back by the third copy | none | **1,660 entries, in 1,462 merges of 691 of the 1,000 histories of three**: rule versions 680, heads 386, profile additions 297, authorities 152, admission records 141, removals 3, revocations 1 |
| a store entry in another group than KeePassLib's result puts it, with another location time | 1,208 entries in 1,150 merges | 1,266 entries |
| at the tie of section 4's KeePassLib paragraph, the entry removed and the record kept where KeePassLib keeps the entry and drops the record | 15 | 30 |

Every entry KeePassDX put elsewhere than KeePassLib in a sample of 8 was in the plugin's group in
KeePassDX's result and in the recycle bin in KeePassLib's: a move to the bin made by a person in
one copy, undone by the merge, which section 3.2 of the KeePassDX emulator report finds as "a move
made without a new modification time follows the arriving copy". No entry's strings, protection,
history or modification time differed from every copy's, and no entry was duplicated, in any merge
of either program.

**What the three-copy results mean for the store.** A copy that deleted a store entry with a
record, merged into another copy through KeePassDX, leaves that other copy without the record; a
later merge with a third copy that still holds the entry then brings the entry back. Among the
entries that came back are **admission records and profile additions a person had deleted** (141
and 297), which by the store's reading admit a client and put a name back on the list, and
**authorities deleted for good** (152). A head or a rule version that a write of the plugin
replaced is named by its successor; one a person deleted is not. What the store's read makes of
each entry that came back is the design's question, and was not run here.

### 4.1 Long strings

A head grows with every write of the plugin; the run of the store as a whole found one of 37,830
characters after 1,000 writes, where the longest string any emulator run carried had 2,861. So a
store was made (`StoreLab long`) whose copy A holds eight more store entries, each with one long
string of 4,096, 16,384, 65,536 or 262,144 characters, plain (`KPG.Data`) and protected
(`KPG.Secret`), the text ASCII with a two-byte and a three-byte UTF-8 character every 997 and 1,999
characters; copy B is A after a write of the plugin and with a ninth entry of its own, 262,144
characters plain. The files are 632 KB and 838 KB. Each program loaded, saved and edited both
copies and merged them both ways; KeePassLib's Synchronize ran beside.

**No limit, truncation or failure in either program**: every long string, plain and protected, came
back through every load, save, edit and merge of both with the same text (its SHA-256) and the same
protection flag, and every merge held both copies' long entries. The findings are the ones of
section 4 alone: the version 4.0 to 3.1 in every save and edit of both, and in KeePassDX's merge of
B into A the arriving record of the replaced head not kept. Time, not measured as a limit: one load
and save of A took about 1 s in the Keepass2Android harness and about 3 s in the KeePassDX
harness's JVM. **Read**: the KeePassDX harness passes limits that refuse nothing for the memory
check the app makes before a load (`Limits.isMemorySufficient`); on a device that check is the
app's, and it is not run here.

## 5. Format version and key derivation on save

**The inputs** are KeePassLib 2.62 files (`kplib\Formats.cs`): KDBX 3.1 with AES-KDF, made as
KeePass's export to 3.1 makes it; 4.0 with AES-KDF and no database custom data, alone and with
ChaCha20; 4.0 with an untimed database custom data item and AES-KDF, Argon2d, Argon2id, or
Argon2d with ChaCha20; 4.1 with every 4.1 item and AES-KDF, Argon2d or Argon2id; and 4.1 with each
4.1 item alone and AES-KDF, the previous parent group alone also with Argon2d. The 4.1 items are a
time on a database custom data item, a group's tags, an entry's QualityCheck off, a custom icon's
name and time, and a previous parent group on an entry and a group. Each went through each
harness as a save (an entry added), an edit of its one entry, and a merge with a copy of itself,
and through KeePassLib 2.62's own open and save; every file written was read with KeePassLib 2.62.
`recorded\formats.txt` holds every line.

**The key derivation and the cipher were kept in every file of both programs**: AES-KDF with its
rounds, Argon2d and Argon2id with their parameters, AES and ChaCha20.

**The version written**, for a save; the edit and the merge wrote the same version but where a
cell names them:

| Input | KeePassLib 2.62 | Keepass2Android | KeePassDX |
| --- | --- | --- | --- |
| 3.1, AES-KDF | 4.0 | 3.1 | 3.1 |
| 4.0, AES-KDF, nothing above 3.1 | 4.0 | **3.1** | **3.1** |
| 4.0, AES-KDF and ChaCha20, nothing else above 3.1 | 4.0 | 4.0 | **3.1**, ChaCha20 kept |
| 4.0, AES-KDF, an untimed database custom data item | 4.0 | **3.1**, the item kept | 4.0 |
| 4.0, Argon2d, Argon2id, or Argon2d and ChaCha20, the same item | 4.0 | 4.0 | 4.0 |
| 4.1, every 4.1 item, any key derivation | 4.1 | 4.1, **the custom data time gone** | 4.1, every item kept (the edit: QualityCheck back on) |
| 4.1, the custom data time alone, AES-KDF | 4.1 | **3.1, the time gone** | 4.1 |
| 4.1, a group's tags alone; a custom icon's name and time alone | 4.1 | 4.1 | 4.1 |
| 4.1, QualityCheck off alone, AES-KDF | 4.1 | 4.1 | 4.1; **the edit: 3.1, QualityCheck back on** |
| 4.1, the previous parent groups alone (forced; KeePass does not write it so) | **4.0, both gone** | **3.1 with AES-KDF, 4.0 with Argon2d, both gone** | the same as Keepass2Android |

**Why, read at the tags.** Keepass2Android's save takes the largest of what the key needs and of
`GetMinKdbxVersionOrig`, which asks 4.1 for a group's tags, QualityCheck off, a custom icon's name
or time and a database custom data time, 4.0 for node custom data, ChaCha20, a key derivation other
than AES-KDF or public custom data, and 3.1 otherwise; and its reader does not keep a database
custom data item's time, so that reason never arises. KeePassDX's `DatabaseKDBX.getMinKdbxVersion`,
as the table shows it, counts database custom data and its times and not the cipher. Neither phone
program counts a previous parent group, and neither does KeePassLib 2.62, whose save of a file with
only that item writes 4.0 without it. KeePassDX's `EntryInfo`, which its entry editor fills, has
no QualityCheck field, so an edit writes it on (read).

**What this means for the store**: a store that is entries only needs nothing above 3.1, and
neither phone program drops an entry's strings or protection when it writes 3.1 (section 4). A
file with AES-KDF and no 4.0 reason comes back from either phone program as 3.1, which KeePass 2.62
opens and writes back as 4.0.

## 6. Exclusions, and candidates for the emulator

**How to read this section.** An exclusion is a result where a program's own reader drops a place,
its writer never writes it, or its merge removes or changes something, on every path the harness
took; since the app runs the same code, no run of the app keeps that place on that path, and the
result stands without the emulator. A candidate is a result where the code keeps something: the
app may still lose it on a path the harness does not take, so the emulator confirms it before it is
relied on.

### 6.1 Exclusions: reliable from the source alone

**Keepass2Android 1.15-r6**

1. A time on a database custom data item is dropped by every save and every merge; a time on a
   group's or an entry's custom data item is dropped by every save (section 3).
2. A header comment, an undefined outer or inner header field, bytes after the last block, an
   unreferenced inner header binary, unknown XML elements and attributes, and an XML comment are
   dropped by every save (section 3).
3. A file with AES-KDF and no 4.0 reason in Keepass2Android's own count (node custom data, ChaCha20,
   another key derivation, public custom data) is written as KDBX 3.1, database custom data
   included; a previous parent group is never a reason for 4.1 and is lost when nothing else in
   the file asks for 4.1 (section 5).
4. Its merge is KeePassLib's Synchronize in its effect on the store's entries and records, in all
   16,000 merges run, the tie included (section 4); and the record of section 2.1 shows the merge
   path is reached as read.

**KeePassDX 4.5.8**

5. A file holding a header comment, an undefined outer header field, or an unknown element in the
   ones measured (`Meta`, a group's `Times`, `MemoryProtection`) is not opened (section 3).
6. A merge applies the arriving copy's deletion records and keeps none of them: 10,140 records of
   store entries that KeePassLib's merges of two keep were missing from KeePassDX's (section 4),
   and the second record agrees (section 2.2).
7. With three copies, an entry deleted with a record in one copy comes back from a third copy after
   a first merge through KeePassDX: admission records, profile additions, authorities, heads and
   rule versions among them (section 4).
8. A merge of two copies, where the arriving copy deleted a group whose parent group it also
   deleted while the open copy's group still holds an entry or a group, can loop for ever in
   `DatabaseKDBXMerger.firstNotDeletedParent` (section 2.3); the source has no path out of the loop
   once it is entered.
9. An entry's edit turns QualityCheck back on; a merge keeps the open copy's QualityCheck, public
   custom data keys and deletion records only; `MasterKeyChangeForceOnce` is dropped by every save
   (section 3).
10. A file with AES-KDF and no database custom data is written as KDBX 3.1, whatever the cipher; a
    previous parent group is lost when nothing else asks for 4.1 (section 5).

**Both, and KeePassLib 2.62 as well**: an entry whose deletion record has the same second as its
last modification survives a merge, with the record dropped by KeePassLib and Keepass2Android and
the entry dropped by KeePassDX when the record is on the open side (section 4).

### 6.2 Candidates: what the code keeps, for the emulator to confirm

Each line names what to run and what the source predicts.

1. **The store's records through each program's save, edit and merge** (section 4): two copies of
   a composed store, one changed by a write of the plugin, merged both ways on each phone. Predicted:
   Keepass2Android exactly as KeePassLib's Synchronize; KeePassDX the same entries but without the
   arriving records. Then a third copy merged after: KeePassDX brings back an entry the second copy
   deleted with a record (pick an admission record).
2. **A store entry moved to the recycle bin in one copy**, merged through KeePassDX with the copy
   open that did not move it and with the one that did: predicted back in the plugin's group either
   way where KeePassLib has it in the bin (sample of 8, section 4).
3. **The KeePassDX merge that does not end**: the second record's PHONE and DESK copies, "Merge
   from ..." with PHONE open, repeated; predicted to hang in some runs and to end in others
   (section 2.3: in the harness, 30 of 30 runs as the first job of a fresh process did not end,
   and 2 of 5 runs after other jobs ended), and to end with DESK open (30 of 30). That the run
   depends on what ran before in the same process is unexplained by the read.
4. **`B41unk`** of the KeePassDX emulator measurement, pending there: predicted not to open
   (section 3), and so for every place in an unknown element.
5. **Every place kept by a save in section 3**, on a phone save of `all-raw` for Keepass2Android and
   of `clean-base` for KeePassDX: the harnesses predict them kept; the emulator's own file is what
   confirms that nothing around the database code (a cache, a background write, the storage
   provider) changes them.
6. **The version written**: a KDBX 4.0 file with AES-KDF and no database custom data saved by each
   phone: predicted 3.1 (section 5); and a 4.1 file whose only 4.1 item is a time on database custom
   data saved by Keepass2Android: predicted 3.1 with the time gone.
7. **The long strings of section 4.1 on a phone**: copy A loaded, saved and merged with B by each
   program; predicted kept whole, with the app's own memory check and the device's time the open
   questions.
8. **Keepass2Android's touch of a shown entry racing its save** (section 2.1): which file holds the
   usage count depends on timing; nothing in the store reads it, so this is for completeness.
