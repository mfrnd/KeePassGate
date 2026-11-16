# Measured: what KeePassDX does to a KDBX 4 database that KeePass with the plugin also writes

Run 2026-11-16 on an Android emulator, for the storage rethink. The question: when KeePassDX opens,
edits, saves, merges and overwrites a database that KeePass with the KeePassGate plugin and other
programs also write, what does the file hold afterwards, for ordinary carriers, for the records of
the store `EVALUATION-final.md` section 6 recommends, and for the custom data the plugin uses today?
This report closes what `measured-android.md` section 4 left open for KeePassDX.

**What was cut**: item 4 (write-protected opening) and part of item 1 (the later entry edits, most
moves, the group icon, expiry and plugin-group rename, templates, the database settings, the master
key and "save a copy") were not run to a counted result; section 10 gives the reasons, one by one.

**Everything in the tables is measured**: a file KeePassDX (or Keepass2Android, where named) left on
the emulator, pulled back and read with KeePassLib 2.62. Where something is read from a program's
source it is in prose and marked **read**; nothing read is in a table.

## 1. How the runs were made

### 1.1 The release

The newest KeePassDX release on 2026-11-16 is **4.5.8**, the release `measured-android.md` measured
first: GitHub's release list and its "latest" release both name tag `4.5.8` (commit
`77ecfa8654a5bd0d79d512f8fa71c0af8a29b554`, published 2026-10-31T20:06:43Z); no later tag exists.
So this report measures 4.5.8 again, in full. The libre flavor's APK was downloaded again from the
release into a new directory of this measurement's own inside WSL (`scripts\fetch-kpdx.sh`) and
held to the pins by `scripts\verify-apks.sh` (`apksigner` from build-tools 37.0.0):

| program | release | file | SHA-256 of the file | signer certificate SHA-256 | versionCode |
| --- | --- | --- | --- | --- | --- |
| KeePassDX | 4.5.8 libre | `KeePassDX-4.5.8-libre.apk`, 16,786,874 bytes | `8aaac6a0c55a9de760147990856726e197efbbcdc5ce2f6bfb4d4ab2a3d65f74` | `7d55b8af210381aabf960f07e17cf7857b6d2a642ca2da6bf0bdf1b200362f04` | 45800 |
| Keepass2Android (sections 9 and 10 only) | 1.15-r6 | `keepass2android.keepass2android-Signed-x64.apk` | `5c446a4fff3ac3fe29b07724dff59e756738eb1fd6611281cd594e421817891d` | `0bf8056d3a394ed7c77173c4b42c800565b4f0a42977569cf3472c271c562939` | 254 |

The downloaded file's SHA-256 is the value the release text gives for "Build Libre", the digest
GitHub reports for the asset, and the line `measured-android.md` pinned; the signature verifies
(v1 and v2 schemes) with exactly one signer, the pinned certificate (`SIGNER MATCH`). The package
the emulator runs, `com.kunzisoft.keepass.libre` versionName 4.5.8, versionCode 45800, runs from a
`base.apk` whose SHA-256 is the same value (`logs\check.txt`). The pin file in the evidence,
`android.sha256`, is this measurement's copy, with the 4.5.8 line as before.

**Release notes between 4.5.8 and the release measured**: none, the two being the same release.
The notes of 4.5.8 itself list "Fix KeePro for Play Store", "Revert Play Store obfuscation", "Fix
Autofill menu presentation" and "Fix extension when saving new file"; nothing about merge, save,
synchronization, attachments, templates or the file-change check. For context, the notes from
4.5.0 to 4.5.7 that touch these subjects are listed in `release-notes.txt`; among them 4.5.5 "New
error message after external database file hash modification" (#2725), which **read** is the
message for a header HMAC that fails to verify at load (commit `f2c0c347`, `HeaderHmacMismatchException`
in `DatabaseInputKDBX.kt`, string `invalid_db_hmac`), not a check before saving: the file-change
check is section 4.

### 1.2 The emulator

The device of `measured-android.md` 1.2: AVD `kpg30`, Android 11, API 30, `google_apis` x86_64,
headless under WSL 2 (`-no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect`),
driven over `adb` as root with animations off; Android emulator 37.2.13.0. Started without wiping
its user data; every run clears KeePassDX's data first (`pm clear`), so every run starts with
KeePassDX's defaults: autosave on, no remembered file, no advanced unlock. The device's time zone
is Europe/Berlin and its screen lock was off (`locksettings get-disabled` true) except where a
section states otherwise. No window was shown on the desktop.

### 1.3 The databases

Made by the probe of `measured-android.md` 1.3, `StoreProbe.exe` over **KeePassLib 2.62**
(`probe\Store.cs`, and `probe\DX.cs` added for this measurement), invented content only, the
invented password `dummy-password-for-a-throwaway-file`, no window. Every node the reading names
has a fixed identifier; the store's entries have identifiers that look random (the first 16 bytes
of SHA-256 over a name, so that a reading can still name them); every time is explicit, in whole
seconds from T0 = 2026-11-01T10:00:00Z. The main base **`B41`** (Argon2d, 2 iterations, 1 MiB,
1 lane, AES-256, GZip; KDBX 4.1 as KeePassLib 2.62 writes it) holds, in one file:

| what | where | carriers |
| --- | --- | --- |
| **generic carriers** | group `Carriers` (`R1`, `R2`), group `G` (`Gm`, `E1`, `E2`, `E3`), `N` under `G` (`N1`) | as `measured-android.md` 1.3: plain and protected custom strings, a 2817-character JSON value, timestamp-named fields, a protected attachment shared by two entries, entry tags, three history items on `R1` and on `E1`, entry custom data on `R1` and `E1`, `G`'s JSON notes, group tags on `G` (4.1), group custom data on `G`, a previous parent on `N1` (4.1), quality check off on `R2` (4.1); a recycle bin group the database names; deletion records `D1`, `D2` naming entries the file does not hold |
| **the recommended store** (`EVALUATION-final.md` 6.1) | group `KeePassGate`, searching and auto-type off | write-once entries, each with `KPG.Kind` and `KPG.Record` (JSON) and, where 6.1 gives one, `KPG.Check`: admission records `AD1`, `AD2`; profile additions `PA1`, `PA2`; grants `GR1`, `GR2`; a lift `LF1`; a renewal answer `RA1` (all with a check); a retirement `RT1` (with a check); a revocation `RV1`; a profile removal `PR1`; a withdrawal `WD1`; withheld marks `MK1` (a group) and `MK2` (an entry) |
| the store's other carriers | database custom data, deletion records, marked nodes | the authority `KPG.Authority.<fp1>` (one key, 2817 characters, with a store key inside) and a retired one `KPG.Authority.<fp0>`; the retirement's key `KPG.RetiredAuthority.<fp0>` and deletion record `DRT1` naming no entry; the revocation's key `KPG.Revoked.<c1>` and record `DRV1` naming no entry; `KPG.ProfileRemoved.<id>` for `PR1`, `KPG.Withdrawn.<id>` for `WD1`, `KPG.Withheld.<id>` for `MK1` and `MK2`; the reference `KPG.Mark` on the marked entry `E3`, and a small entry `MR` holding `KPG.Mark` in the marked group `Personal/Health` |
| **today's custom data** (controls) | database custom data, `G`, `E1`, `R1` | `KPG.Authority.<fp>` (shared with the store), `KPG.Client.<c2>` (an admission record as today), `KPG.Client.<c1>` emptied beside `KPG.Revoked.<c1>`, `KPG.Profiles`; `KPG` on `G` and on `E1`, `KPG.cd` on `R1` |
| **ordinary entries for both-side edits** | group `Passwords`, and groups `GD`, `NA/NB/NC` | `P1`, `P2`, `Q1`, `Q2`, `Q3`, `X1` to `X4`, `Y3` to `Y6`, `K`; `GD1` in `GD`; `NA1`, `NB1`, `NC1` in three nested groups |
| meta | | name, description, default user name and color set; `HistoryMaxItems` 8, `HistoryMaxSize` 5 MiB, `MaintenanceHistoryDays` 200; every database custom data key but `KPG.Untimed` with a per-key time (4.1) |

The **4.0 variants** (`B40`, `B40id`, `B40aes`) hold the same with nothing KDBX 4.1 needs: no group
tags, no per-key time, no previous parent, quality check on; KeePassLib 2.62 writes them as KDBX
4.0. The **KDF variants** are Argon2d (`B41`, `B40`), Argon2id (`B41id`, `B40id`, 2 iterations,
1 MiB, 1 lane) and AES-KDF (`B41aes`, `B40aes`, 60000 rounds). `B41unk` is `B41` with three XML
elements no reader knows (in `Meta`, in the root group and in an entry), written by the probe's own
encryption of KeePassLib's XML under the file's unchanged header (`unknown`).

**The merge pair** (item 2), both `B41` changed by KeePassLib, every change made as KeePass's entry
dialog makes one (the old version into history first): **`PHONE`** stands for a phone's copy with
the phone's edits saved in it, **`DESK`** for the desktop's later save.

| item | `DESK` (the desktop's later save) | `PHONE` (the phone's copy) |
| --- | --- | --- |
| a revocation, its three carriers | entry `RV2`, key `KPG.Revoked.<c3>`, deletion record `DRV2` naming no entry, at +8 h | none |
| new write-once records | admission `AD3`, grant `GR3`, profile removal `PR2` with its key, withdrawal `WD2` with its key, at +8 h | none |
| a database custom data key the other lacks | `KPG.NewKey`, at +8 h | none |
| a deletion record naming no entry | `DZ`, at +8 h | none |
| `X1`, deleted on one side only | deleted, record at +8 h | unchanged |
| `X2`, deleted on one side, changed later on the other | deleted, record at +5 h | user name changed at +6 h |
| `X3`, the same the other way round | user name changed at +7 h | deleted, record at +6 h |
| `X4`, deleted on the phone only | unchanged | deleted, record at +6 h |
| `P1`, `P2`, a password one side changed | `P1` at +8 h | `P2` at +6 h |
| `Q1`, `Q2`, `Q3`, passwords both sides changed | at +5 h, +7 h and +6 h | all three at +6 h, so the desktop's is older, newer and **equal** |
| new ordinary entries | `ND` at +8 h | `NPh` at +6 h |
| a move | `Y3` Passwords to N (location +6 h, modification time unchanged, as KeePass moves) | `Y5` to N with the modification time moved (as KeePassDX moves); `Y6` to N without |
| a move on both sides | `Y4` to N (location +6 h) | `Y4` to G (+7 h, modification time moved) |
| a group deleted on one side, an entry inside changed on the other | `GD` deleted with `GD1`, records at +5 h (as KeePass deletes a group: everything inside recorded, entries first) | `GD1`'s password changed at +6 h |
| nested groups deleted | `NA` with `NB`, `NC` and their entries, records at +8 h, in KeePass's order (`NA1 NB1 NC1 NC NB NA`) | unchanged |

**The overwrite set** (item 3), as `measured-overwrite.md` 1.2 made it, on `B41`: `OLD` is `B41`
with `X1`'s notes padded; `NEW-diff` is `OLD` with the desktop's later save (`RV2` with its key and
record, `P1`'s password, `X1` deleted with its record, all at +8 h; another length), `NEW-same` the
same changes cut to exactly `OLD`'s length (17,365 bytes), `NEW-ident` `OLD`'s bytes.

**The hazard sets** (item 5, 6): `HATT` (`B41` with `A1` holding `a.bin`, `A2` holding `b.bin`, `A3`
holding both, two 48-byte bodies whose MD5 digests share their first four bytes, the pair of
`ground-android.md` 5.1, copied from `md5pair\recorded\`); `HATTo` and `HATTa` (`A1` with `a.bin` on
both sides, and `A4` with `b.bin` new on the arriving side); `HHISTa` (`E1` changed three times on
the arriving side so that two attachments exist only in its history, and a new entry `H1` whose
first attachment exists only in its history); `HNESTa` (the `NA` tree deleted as KeePass deletes it,
at +8 h) against `HNESTg` (`NC`, the innermost group, changed at +9 h) and `HNESTe` (`NB1` changed at
+9 h); `HL` and `HLa` (`HistoryMaxItems` 3 and `HistoryMaxSize` 6000 bytes, `HL1` at the item
limit, `HL2` near the size limit, and the arriving copy with more history on both).

### 1.4 The driver and the flows

`k.py` of the earlier measurements (reads the screen through `uiautomator`, presses what a flow file
names), with four commands added: `appendto` (types at the end of a named field and checks that the
focused field is that field, which the first measurement found was not always so), `typefocused`,
`pull` (the open file, with its size, time and hash, in the middle of a flow) and `set` (a flow
variable). `run-op.sh`, `run-B.sh` and `run-ow.sh` run one case from cleared data: KeePassDX's
data cleared, the given file alone in `/sdcard/Download`, the flow, and every file left there
pulled back. **A chain** is one run that makes several operations one after another, each saved
by autosave and the file pulled after each; every operation's result is diffed against the file
before it, so each reads as an operation on its own. `replace.sh` puts an arriving copy over the
open file at a chosen time relative to the replaced file's own (to the nanosecond), as
`measured-overwrite.md` 1.3 did. Every log records every press, every dialog's message and buttons,
and the file's size, time and hash before and after every replacement and at every pull.

### 1.5 The read-back

Each pulled file is read by the probe into a sorted line-per-item dump (`measured-android.md` 1.4),
extended here with the database's color and key-change settings, the Argon2 variant and version,
and **a reading of the raw file** that KeePassLib does not keep: the outer header's fields and KDF
parameters, the inner header (stream, binaries and their flags), the XML's `Generator`, the memory
protection flags as the file holds them, and a census of every element path in the XML with its
count, so that a per-key time, an unknown element or an element a writer adds shows as a line.
`mverdict` prints one line per item of the merge pair; `owverdict` whether the desktop's later save
survived an overwrite. **KeePass's own Synchronize** of the same two copies, in both directions, is
`PwDatabase.MergeIn(..., PwMergeMethod.Synchronize)` (`mexpect.sh`, `sync.sh`), and every
KeePassDX result is diffed line by line against it.

### 1.6 What was counted

Only runs whose flow ended without a failure; a failed run was repeated from cleared data and is
listed, with its log, under `not-counted`. One exception, stated where it applies: in a chain, a
step whose file was pulled before a later step failed is counted as that operation, since its own
part of the flow ended and its file was read against the file before it; the failed step and
everything after it are not counted. A run whose flow failed because the program refused (the base
with unknown elements, which KeePassDX cannot open) is counted as that result, with the device log
that shows the refusal. Section 11 gives the counts.

## 2. Every operation the program offers (item 1)

### 2.1 Open and save unchanged

Each base opened, unlocked, and saved by the overflow menu's **Save data** with nothing else done;
one counted run each, each written within 1.4 s of the press.

| base | KDF | format given | format written | items | raw file |
| --- | --- | --- | --- | --- | --- |
| `B41` | Argon2d | 4.1 | 4.1 | every line as given but the root group's access time | generator `KeePassDX`; a `Meta/HeaderHash` element added; empty `Tags` elements left out |
| `B41id` | Argon2id | 4.1 | 4.1 | the same | the same |
| `B41aes` | AES-KDF | 4.1 | 4.1 | the same | the same |
| `B40` | Argon2d | 4.0 | 4.0 | the same | the same |
| `B40id` | Argon2id | 4.0 | 4.0 | the same | the same |
| `B40aes` | AES-KDF | 4.0 | 4.0 | the same | the same |

"Every line as given" covers everything 1.3 lists: the KDF and its parameters (the header's KDF
dictionary read back field for field), the cipher, the compression, `SettingsChanged`, the memory
protection flags as the file holds them, every per-key time of the database's custom data (and the
key without one stayed without one), node custom data, group tags, previous parents, quality check,
deletion records, history, attachments, the store's entries and keys, today's custom data. Unlike
the first measurement's saves, which added an entry, Save data alone left the root group's
modification time as given.

**`B41unk`, the base with three unknown elements, cannot be opened.** In both runs (the second's
device log kept, `logs\logcat-B41unk.txt`) the unlock went through the key derivation and the
decryption and then failed: `XMLMalformedDatabaseException: Invalid end element: Context MetaEnd
element: KPGUnknownMeta` (`DatabaseInputKDBX.endXmlElement`); the password field was emptied and
the unlock screen stayed, with no message the screen dump caught. KeePassLib 2.62 reads the same
file and skips the three elements. Nothing was written. So an element KeePassDX does not know, in
`Meta` at least, makes the whole file unreadable to it; where KeePassLib skips it.

### 2.2 Edits of an ordinary entry

Chain E on `B41`, `E1` in `G` (one editor session and one autosave per step; every step diffed
against the step before). Every step, whatever it changed, also did the following, which the table
does not repeat: **`E1`'s modification and access times moved, its usage count rose by one, one
history item added holding the whole version before the step (custom strings with their flags,
attachments, tags, notes, entry custom data), the expiry time rewritten to 2999-11-28T23:59:59Z
with expiry still off (on the first step; it stays so), and `G`'s and the root's modification and
access times moved.** Nothing else in the file changed in any step.

| step | counted runs | the change asked | read back |
| --- | --- | --- | --- |
| s01 the editor opened and validated with **nothing changed** | 1 | none | **a new version all the same**: every common effect above, every field as it was |
| s02 title | 1 | `E1` to `E1-t` | title changed |
| s03 password | 1 | `-p` appended | password changed, still protected |
| s04 notes | 1 | `-n` appended | notes changed |
| s05 to s12: tags, a custom field added, changed and removed, an attachment added and removed, the icon, the expiry | 0 | | **not counted**: three further chains stopped at the first of these steps (the tags field out of the driver's reach, then the field dialog's OK not found); section 10 |

### 2.3 An accidental edit of the store's entries

Chain R on `B41`: the title of an admission record, of a grant, of the marked entry and of the
mark's reference entry, each edited in the entry editor and validated (one counted run, four
steps, each diffed against the step before).

| step | the change asked | read back |
| --- | --- | --- |
| s01 `AD1` "KPG client build-agent" | `-phone` appended to the title | title changed; **`KPG.Kind`, `KPG.Record`, `KPG.Check` as given**; one history item (the version before); the expiry rewritten to 2999-11-28; `KeePassGate`'s and the root's times moved |
| s02 `GR1`, a grant | the same | the same; its `KPG.` strings as given |
| s03 `E3`, the marked entry | the same | the same; **`KPG.Mark` as given**; `G`'s and the root's times moved |
| s04 `MR`, the mark's reference entry in `Personal/Health` | the same | the same; `KPG.Mark` as given; `Health`'s, `Personal`'s and the root's times moved |

Every other line of the file, the store's keys and records and today's custom data included, was
as before. So a check over the `KPG.` strings only (`EVALUATION-final.md` 6.1) still verifies after
such an edit; the entry's time, its history and its expiry do not stay as written.

### 2.4 Groups

Chain G on `B41` (group dialog: name, notes, icon, expiry, searching, auto-type, tags).

| step | counted runs | the change asked | read back |
| --- | --- | --- | --- |
| s01 rename `G` | 1 | `-r` appended to the name | name changed; **`G`'s expiry rewritten to 2999-11-28** (expiry off); `G`'s and the root's modification times moved; `G`'s notes, tags, group custom data, flags as given |
| s02 `G`'s notes | 1 | `-n` typed at the end of the field | the notes changed: the two characters typed landed before the last two of the JSON (the field wraps, and the end key went to the end of the visible line), so `G`'s JSON notes no longer parse; `G`'s and the root's times moved; tags and custom data as given |
| s04 `G`'s tags (a second chain, from the base) | 1 | `added,` typed at the end of the tags field (`kpg.reader, team, `) | the tags became `added;kpg.reader`: **the tag `team` gone** with the new one in; `G`'s expiry rewritten to 2999-11-28; `G`'s and the root's times moved. Whether a person typing a tag loses the last one the same way is not known: this is what the driver's keystrokes produced |
| icon and expiry of `G`, rename of `KeePassGate` | 0 | | **not counted**: the icon picker never opened from the group dialog, and the expiry's date picker was not found; section 10 |

### 2.5 Deletions

Chain D on `B41`, one counted run, five steps.

| step | what the program showed | read back |
| --- | --- | --- |
| s01 `E2` deleted from `G` | no question | `E2` in the recycle bin, **its modification time moved**, previous parent `G` and location time recorded; the bin's, `G`'s and the root's modification times moved |
| s02 `AD1`, an admission record, deleted | no question | `AD1` in the bin with every `KPG.` string as given; its modification time moved, previous parent `KeePassGate` recorded |
| s03 the group `GD` deleted | no question | `GD` with `GD1` in the bin, previous parent recorded, `GD`'s modification time moved |
| s04 `E2` deleted again, inside the bin | "Permanently delete selected nodes?" [Cancel] [OK]; OK | `E2` gone, **its deletion record added** |
| s05 the bin emptied (overflow menu, "Empty the recycle bin") | "Permanently delete all nodes from the recycle bin?" [Cancel] [OK]; OK | `AD1`, `GD` and `GD1` gone, **a deletion record for each** (entries and the group) |

### 2.6 Moves

Chain M on `B41` (selection, Move, the target group, Paste).

| step | counted runs | read back |
| --- | --- | --- |
| s01 `E1` from `G` into `N` | 1 | `E1` in `N`; **its modification time moved** (so a later merge takes this version as the newer), previous parent `G` and location time recorded; `G`'s, `N`'s and the root's modification times moved; every carrier of `E1` (strings, attachment, tags, history, custom data) as given |
| s02 to s05: an admission record out of `KeePassGate`, `K` into it, the group `GD` into it, the group `N` to the root | 0 | **not counted**: the chain stopped after s01 (section 10). The first measurement's move of a group (`N` to the root, `measured-android.md` 4.4) and section 9's synchronize of it stand for the group move |

### 2.7 Copies and new nodes

Chain C on `B41`.

| step | counted runs | the change asked | read back |
| --- | --- | --- | --- |
| s01 `E1` copied (selection, Copy, Paste) | 1 | meant for `N`; the paste landed in `G` itself (the flow pasted before it had entered `N`) | a new entry with a new identifier in `G`, titled `E1 (~)`: every string with its flag, the tag, the attachment and the entry custom data copied; **no history**; created time `E1`'s, modification and location times new; `G`'s and the root's times moved |
| s02 a new entry in `KeePassGate` | 1 | title `phone-entry-in-kg` | a new entry there; its user name the database's default user name; expiry 2999-11-28 (off); `KeePassGate`'s and the root's times moved; the store's entries as given |
| s03 a new group in `KeePassGate` (a second chain, from the base) | 1 | name `phone-group-in-kg` | a new group there with a new identifier; expiry 2999-11-28 (off); searching and auto-type inherited; `KeePassGate`'s and the root's times moved; the store's entries as given |
| s04 a new group at the root | 1 | name `phone-group-root` | the same, at the root |
| s05 a new entry at the root | 1 | title `phone-entry-root` | as s02, at the root |

## 3. Both merge paths (item 2)

**The two paths.** "Merge from ..." is in the navigation drawer: it asks the first characters of
the open database's password, then the arriving file through the system picker and its password,
merges and saves at once (autosave). **The external-change path** begins with a dialog that
KeePassDX shows when it notices the file changed (section 4 gives when): "The information contained
in your database has been modified outside the app.", the old and the new time and size, "Merge
the data, overwrite the external modifications by saving the database or reload it with the latest
changes.", and **one button, OK**. The three answers are then the group screen's overflow menu:
**Merge data**, **Save data** (which writes the copy in memory over the file) and **Reload data**.
**Read**: OK records the new file's time and size as the ones the program holds
(`DatabaseChangedDialogFragment.kt:65-67`, `DatabaseTaskProvider.kt:117-119`), so nothing asks again
before a later save writes over the file.

PD is the phone's copy open with the desktop's arriving; DP the reverse. Every run started from
cleared data with the open copy alone in `Download`; for "Merge from" the arriving copy was pushed
beside it, for the external-change path it was pushed **over** the open file after the unlock and
given the device's clock as its time (more than ten seconds after the opened file's), with
KeePassDX sent to the home screen and brought back by its launcher, which is where the dialog
showed in every run of this section.

### 3.1 What each answer leaves

| path, answer | direction | counted runs | what the program showed | the file afterwards |
| --- | --- | --- | --- | --- |
| Merge from ... | PD | 5 | no dialog; the drawer item, the check of the open database's first password characters, the picker, the arriving file's password | **3 runs: a merge (3.2)**, written 1.1 to 1.2 s after the answer. **2 runs: never ended** (6.3): the progress dialog "Loading database… / Decrypting database content…" for 90 and 150 s, the process busy (113 to 163 % CPU in the watched run), nothing written |
| Merge from ... | DP | 3 | the same | a merge (3.2), every run |
| external change, OK, **Merge data** | PD | 2 | the dialog above at the return; after OK the overflow menu; Merge data | **1 run: line for line the "Merge from" result** of the same direction, but the root group's access time; **1 run: never ended**, nothing written, as above |
| external change, OK, Merge data | DP | 2 | the same | the same as "Merge from" DP |
| external change, OK, **Reload data** | PD | 3 | the dialog; then "Reloading the database will delete the locally modified data." [Cancel] [OK]; OK | **nothing written**: the file stays the arriving copy byte for byte; in the run that then added an entry, the arriving copy and the new entry, nothing else |
| external change, OK, Reload data, then an entry added | DP | 1 | as above; the entry editor then opened with no further dialog | the arriving copy and the new entry, nothing else: **every change the open copy held is gone** |
| external change, OK, **Save data** | PD | 2 | the dialog; Save data wrote the file at once, no further question | **the open copy whole**: no line differs from the phone's copy but the root group's access time; **every desktop change gone**: the revocation's entry, key and record, the new records, `KPG.NewKey`, `DZ`, `X1`'s deletion, the desktop's passwords |
| external change, OK, Save data | DP | 1 | the same | the open copy whole (here the desktop's): **every phone change gone** |

**OK alone answers nothing**: the program goes on holding the open copy, and the next save, by
autosave after any edit or by Save data, writes it over the arriving file (section 4).

### 3.2 The merge, item by item, against KeePass's own Synchronize

KeePassDX's results within each direction read back alike in every run, "Merge from" and Merge
data alike, but for the root group's access time. KeePass 2.62's Synchronize of the same two
copies was run headless in both directions (`work\expect\PD\`); "same" is a line for line equal
item.

| item | KeePassDX, PD (phone open) | KeePass, phone open | KeePassDX, DP (desktop open) | KeePass, desktop open |
| --- | --- | --- | --- | --- |
| `RV2`, the desktop's revocation entry | present | present | present | present |
| `KPG.Revoked.<c3>`, its key | present | present | present | present |
| `DRV2`, its deletion record naming no entry | **absent** | present | present | present |
| `AD3`, `GR3`, `PR2`, `WD2`, new records, and the keys of `PR2`, `WD2` | present | present | present | present |
| `KPG.NewKey` | present, with its per-key time | present | present | present |
| `DZ`, a deletion record naming no entry | **absent** | present | present | present |
| `X1`, deleted on the desktop only | gone, **no record** | gone, record | gone, record | gone, record |
| `X2`, deleted on the desktop, changed later on the phone | back with the phone's change | the same | back with the phone's change, **and the desktop's record kept beside it** | back, no record |
| `X3`, deleted on the phone, changed later on the desktop | back with the desktop's change, **and the phone's record kept beside it** | back, no record | back with the desktop's change | the same |
| `X4`, deleted on the phone only | gone, record | gone, record | gone, **no record** | gone, record |
| `P1`, `P2`, one side's password | each side's, the old one in history | same | same | same |
| `Q1`, both, the desktop's older | the phone's, the desktop's in history | same | same | same |
| `Q2`, both, the desktop's newer | the desktop's, the phone's in history | same | same | same |
| **`Q3`, both, equal times** | **the phone's (the open copy's); the desktop's nowhere** | the desktop's (the arriving copy's); the phone's nowhere | **the desktop's (the open copy's); the phone's nowhere** | the phone's (the arriving copy's) |
| `ND`, `NPh`, new on each side | both present | both | both | both |
| `Y3`, moved by the desktop without a new modification time | in N, **previous parent dropped, location time not moved** | in N, previous parent kept | **back in Passwords**: the desktop's move undone | in N |
| `Y4`, moved on both sides, the phone's later | in G (the phone's) | same | same | same |
| `Y5`, moved by the phone with a new modification time | in N | same | same | same |
| `Y6`, moved by the phone without a new modification time | **back in Passwords**: the phone's move undone | in N | in N, previous parent and location time differ from KeePass's | in N |
| **`GD`, deleted on the desktop; `GD1` inside changed later on the phone** | **`GD` and `GD1` both gone, no record: the phone's later change lost** | `GD` and `GD1` kept, the phone's change current | `GD` and `GD1` back, **the desktop's two records kept beside them** | `GD` and `GD1` back, no records |
| `NA`, `NB`, `NC`, nested groups deleted on the desktop | gone, **no record** for any of the six | gone, six records | **the three groups back, empty**, their records kept; their entries gone | gone, six records |
| the base's store entries, keys, controls | all kept | same | same | same |
| deletion records afterwards | 6: the phone's own (and `X3`'s, stale) | 14 | 16: the desktop's, stale ones included | 14 |

So against KeePass's own Synchronize, KeePassDX's merge differs in exactly these ways, in both
directions where the cell arises:

1. **Deletion records that arrive are applied and not kept** (`DRV2`, `DZ`, `X1`, the `NA` tree
   in PD; `X4` in DP). The revocation's record naming no entry, which the store relies on as one of
   three carriers, is gone after a merge with the phone's copy open.
2. **A record the open copy holds is kept even when the entry comes back newer** (`X3` in PD, `X2`,
   `GD`, `GD1` and the `NA` tree in DP), where KeePass drops it.
3. **At equal modification times the open copy wins whole** (`Q3`); KeePass takes the arriving
   copy. Either way one side's change exists nowhere.
4. **A move made without a new modification time follows the arriving copy** (`Y3` undone in DP,
   `Y6` undone in PD), and a move it keeps loses its previous parent and location time (`Y3` in PD).
5. **A group deleted on one side takes with it an entry the other side changed later** (`GD1` in
   PD), with nothing left to show it; KeePass keeps both.
6. **Groups deleted on the open side come back empty** when the arriving copy still holds them
   (`NA`, `NB`, `NC` in DP); KeePass leaves them deleted.

**Read**, the reasons, at 4.5.8 (`database/.../merge/DatabaseKDBXMerger.kt`): the arriving list is
applied by `deleteEntry` and `deleteGroup` and never added to the open copy's list (`:304-311`);
equal times keep the open entry and move it to the arriving copy's group (`:519-524`); a group is
removed with every child that is itself in the arriving list still inside it, so a child newer than
the deletion goes with it (`:358-395`); an arriving entry's group is recreated before the entry is
judged against the deletion records (`getAttachedParent`, `:439-458`, `:470`).

## 4. The overwrite (item 3)

**Read** at 4.5.8, the whole of KeePassDX's check for a file that changed underneath it: at
every resume of one of its database screens (`DatabaseActivity.onResume`, `:209-212`), the service
reads the file's last-modified time in milliseconds and its size through the document provider
(`FileDatabaseInfo.kt:64-66`, `:79-81`), **stores them as the ones it holds before comparing**, and
reports a change when the file stopped or started existing, or when the new time is **more than
10,000 ms later** than the time it held (`DatabaseTaskNotificationService.kt:198-243`, the condition
at `:214-219`). The size is only required to be above zero; the content is never read. A save
(`SaveDatabaseRunnable.kt:59-86`) compares nothing and writes, and records the file's new time
after it (`DatabaseTaskNotificationService.kt:440-442`). So by the read, a replacement whose time
is earlier, equal or at most ten seconds later is never reported, and one that is reported is
forgotten the moment the dialog is shown, whatever the answer.

**How the cases were staged** (`measured-overwrite.md` 1.3, with `scripts\replace.sh` extended): `OLD`
pushed alone, its time set to the device's clock less 600 s (whole seconds); opened and unlocked;
then `NEW-diff` (the desktop's later save, another length), `NEW-same` (the same changes, `OLD`'s
length to the byte) or `NEW-ident` (`OLD`'s bytes) pushed over the same path and its time set
relative to `OLD`'s own: `plusN` is `OLD`'s time plus N seconds, nanoseconds kept, `equal` is
`OLD`'s time copied, `earlier` is less 3600 s; every log shows the times before and after. Then the
phone's change: **an entry `phone-<run>` added through the entry editor** (on screen, or after
the program was sent to the home screen and brought back), or **`E2` deleted to the recycle bin
from the group screen**, which starts no new screen. The external-change dialog, wherever it
showed, was logged and answered OK, the only answer it has. "Lost" means the file afterwards holds
none of the three desktop changes (the revocation `RV2` with its key and its record, `P1`'s new
password, `X1`'s deletion with its record); every "lost" file read back as `OLD` plus the phone's
change and nothing else but the root group's times.

### 4.1 Where the boundary is

| case | replacement, time relative to the opened file's | counted runs | what the program showed | the file afterwards | the desktop's changes |
| --- | --- | --- | --- | --- | --- |
| on screen, editor | `NEW-diff`, **equal** | 1 | nothing | `OLD` and the phone's entry | **lost** |
| on screen, editor | `NEW-diff`, +1 s | 1 | nothing | the same | **lost** |
| on screen, editor | `NEW-diff`, +5 s | 2 | nothing | the same | **lost** (2 of 2) |
| on screen, editor | `NEW-diff`, +9 s | 1 | nothing | the same | **lost** |
| on screen, editor | `NEW-diff`, **+10 s** | 2 | nothing | the same | **lost** (2 of 2) |
| on screen, editor | `NEW-diff`, **+11 s** | 2 | **the dialog twice**: when the entry editor opened, and again on the group screen after Validate; OK each time; no other question | `OLD` and the phone's entry | **lost** (2 of 2) |
| on screen, editor | `NEW-diff`, +15 s | 1 | the dialog twice, as at +11 s | the same | **lost** |
| on screen, editor | `NEW-diff`, +60 s | 2 | the dialog twice | the same | **lost** (2 of 2) |
| on screen, editor | `NEW-diff`, **earlier** by 3600 s | 1 | nothing | `OLD` and the phone's entry | **lost** |
| on screen, editor | `NEW-same` (`OLD`'s length), equal: **content changed, time not** | 1 | nothing | the same | **lost** |
| on screen, editor | `NEW-same`, +5 s | 1 | nothing | the same | **lost** |
| on screen, editor | `NEW-same`, +60 s | 1 | the dialog twice | the same | **lost** |
| on screen, editor | `NEW-ident` (`OLD`'s bytes), +60 s: **time changed, content not** | 1 | **the dialog twice** all the same | `OLD` and the phone's entry | nothing to lose: the arriving file held no change |
| from the background | `NEW-diff`, +5 s | 1 | nothing at the return | `OLD` and the phone's entry | **lost** |
| from the background | `NEW-diff`, +60 s | 2 | **the dialog at the return**; OK; the entry added and saved with no further question | `OLD` and the phone's entry | **lost** (2 of 2) |
| on screen, **no screen change** (`E2` to the bin) | `NEW-diff`, +5 s | 1 | nothing | `OLD` with `E2` in the bin | **lost** |
| on screen, no screen change | `NEW-diff`, +60 s | 1 | **nothing** | `OLD` with `E2` in the bin | **lost** |
| the program's process ended (force-stopped), started again, the file opened and unlocked | `NEW-diff`, +5 s, while it was stopped | 1 | nothing | **`NEW-diff` and the phone's entry** | **kept** |
| its own lock (the drawer's Lock), unlocked again with the password | `NEW-diff`, +5 s, while locked | 1 | nothing | **`NEW-diff` and the phone's entry** | **kept** |
| its own lock, unlocked again with the password | `NEW-diff`, +60 s, while locked | 1 | nothing | **`NEW-diff` and the phone's entry** | **kept** |
| **autosave off** (stored before the start), on screen; the entry added (not written); then the overflow menu's **Merge data** | `NEW-diff`, +60 s | 1 | the dialog twice; OK each time; the group screen marked unsaved ("Save" beside the database's color); Merge data | **`NEW-diff` byte for byte**: Merge data wrote nothing, the merged copy stayed in memory unsaved, and the phone's entry is in no file | **kept**; the phone's entry not saved |
| autosave off; then **Save data** | `NEW-diff`, +60 s | 1 | the dialog twice; OK each time; Save data wrote at once, no question | **`OLD` and the phone's entry** | **lost** |

So the boundary is where the read puts it: **a replacement is reported only when its time is more
than ten seconds later** than the opened file's (+11 s reported in 2 of 2 runs, +10 s in 0 of 2),
**and only when one of the program's screens resumes** (the editor opening, a return from the
background; a delete on the same screen at +60 s was never reported). Content, size and an earlier
or equal time play no part: `NEW-same` at an equal time (the content changed, not the time) was
not reported, and `NEW-ident` at +60 s (the time changed, not the content) was. **Being reported
protects nothing**: in every run where the dialog showed and the phone's change was then saved,
autosave or Save data wrote the phone's copy over the newer file (9 of 9 with a change to lose). Only Merge data after
the dialog (with autosave off, where it wrote nothing yet), the program's own lock and unlock, and
a new process kept the desktop's changes (4 of 4).

**Read**, why the lock and a new process read the newer file: KeePassDX's lock closes the database
and an unlock loads the file again (`DatabaseLockActivity`, `LoadDatabaseRunnable`); there is no
in-memory unlock like Keepass2Android's QuickUnlock. The advanced unlocking methods (biometric,
device credential) only supply the password to that same load; they were not run.

## 5. Write-protected opening (item 4)

**Not counted; cut.** The unlock screen's switch from "Modifiable" to "Write-protected" (a toolbar
button) took the driver's taps in one of five attempts. In that one (`not-counted\runs\ro-r1-a1.log`)
the switch read "Write-protected" before the unlock; the group screen then offered **no add
button** and a toolbar of Search, Sort and **Reload data** only (no overflow menu, no Save data, no
Merge data), and the file stayed byte for byte as given for the 15 s watched; the flow then stopped
on its own assumption of an add button, so nothing more was tried. **Read**: every task the service
runs decides whether to save from `!database.isReadOnly` (`DatabaseTaskNotificationService.kt:422`
and the task builders after it), so in this mode nothing is written. Item 4 is otherwise not
measured.

## 6. The hazards read from source (item 5)

Each hazard of `ground-android.md` section 5 was staged in a set of its own (1.3) and run on the
emulator; the KeePass column is KeePass 2.62's Synchronize of the same two copies, headless.

### 6.1 Two attachments whose first four MD5 bytes agree

`a.bin` and `b.bin`: 48 bytes each, different content (SHA-256 prefixes `a9467b6b` and `9b0f7af0`),
MD5 digests `d71a4c0f eb19...` and `d71a4c0f 6eb8...`.

| case | counted runs | the file given | the file afterwards |
| --- | --- | --- | --- |
| `HATT` opened and saved unchanged (Save data): `A1` holds `a.bin`, `A2` holds `b.bin`, `A3` holds both | 2 | four attachments, two bodies | **`A2`'s `b.bin` and `A3`'s `second-b.bin` hold `a.bin`'s bytes**, names and lengths unchanged; `b.bin`'s bytes exist nowhere in the file; nothing on screen |
| Merge from: `HATTo` open (`A1` with `a.bin`), `HATTa` arriving with a new `A4` holding `b.bin` | 2 | | `A4` added, **its `b.bin` holding `a.bin`'s bytes** |
| Merge from: `HATTa` open (`A1` `a.bin`, `A4` `b.bin`), `HATTo` arriving | 1 | | `A4`'s `b.bin` **holds `a.bin`'s bytes** (the open copy's own attachment, replaced at its load) |
| KeePass's Synchronize of `HATTo` and `HATTa`, either direction | headless | | both bodies kept |

So the read of `ground-android.md` 5.1 holds on the device: KeePassDX treats two attachments of one
length whose MD5 digests share their first four bytes as one, at load, and writes one body for
both. The bodies were found by trying 72,707 nonces in 0.2 s (`md5pair`). An attachment is
therefore no carrier for anything whose bytes matter, and a party that can write one attachment
into the database can replace another's content.

### 6.2 Attachments that exist only in arriving history

`HHISTa`: `E1` changed three times on the arriving side, gaining `hist-a.bin` (700 bytes) and then
`hist-b.bin` (800 bytes), and losing each again, so both exist only in `E1`'s history; and a new
entry `H1` whose first attachment `h1-old.bin` (900 bytes) exists only in its history. `B41` open,
`HHISTa` arriving, Merge from, 2 counted runs, both alike:

| history attachment | the arriving copy | KeePassDX's result | KeePass's result |
| --- | --- | --- | --- |
| `E1`, history at 15:00, `hist-a.bin` | 700 bytes, unprotected | **3000 bytes, protected: the bytes of `authority.p12`**, the open copy's attachment of `R1` and `R2` | as arriving |
| `E1`, history at 16:00, `hist-b.bin` | 800 bytes | **600 bytes: the bytes of `e1.txt`** | as arriving |
| `H1`, history at 15:00, `h1-old.bin` | 900 bytes | **1000 bytes: the bytes of `H1`'s current `h1-cur.bin`** | as arriving |
| current attachments (`E1`'s `e1.txt`, `H1`'s `h1-cur.bin`) | | kept | kept |
| binaries in the file | | 3 (`hist-a`, `hist-b`, `h1-old` exist nowhere) | 6 |

The read of `ground-android.md` 5.2 holds and is worse than read: a history item that arrives
points at whatever the open copy's pool holds at the same index, so an older version shows another
entry's attachment, here a protected one, and its own bytes are gone.

### 6.3 A merge that deletes nested groups

The arriving copy `HNESTa` deleted the tree `NA > NB > NC` with its entries, as KeePass deletes a
group (records for `NA1 NB1 NC1 NC NB NA`, at +8 h); the open copy changed something inside later
(+9 h). Merge from, watched for 150 s (`kpdx-B-from-hang.flow`), then force-stopped:

| case | counted runs | what the program showed | CPU of the KeePassDX process | the file afterwards | KeePass's Synchronize |
| --- | --- | --- | --- | --- | --- |
| open `HNESTg`: the innermost group `NC` changed later | 2 | the progress dialog "Loading database… / Decrypting database content… / Do not kill the app…" for the whole 150 s | **92 % and 191 %** at 15 s, still busy at 60 and 150 s | **unchanged**: nothing written; the merge never ended | `NA`, `NB`, `NC` kept (`NC` with its change), the three entries deleted |
| open `HNESTe`: the entry `NB1` changed later | 2 | no dialog; the merge ended at once | 0 % | **the whole tree gone, `NB1` with its later change included, and no record for any of the six** | `NA`, `NB` and `NB1` (with its change) kept; `NA1`, `NC1`, `NC` deleted |
| the reverse: `HNESTa` open, `HNESTg` arriving | 1 | none | | `NA`, `NB`, `NC` back (`NC` with its change), their records kept | the same groups back, records for the groups dropped |

So both reads of `ground-android.md` 5.3 hold: when a child group of a deleted group survives
(changed later), the merge **never ends**, with the CPU busy and the file never written; when an
entry of a deleted group survives, it is **removed with its group**. **Read**: `firstNotDeletedParent`
assigns `node.parent` to `parent` inside its loop and never walks up
(`DatabaseKDBXMerger.kt:342-351`), reached from `deleteGroup` when a group to delete still holds a
child to move and its own parent is in the arriving list too (`:358-395`); an entry whose own record
is in the arriving list is not moved out first (`:364-368`), so it goes with the group even when it
is newer than the record.

**The same loop on the main pair.** Three runs of section 3 with the phone's copy open and the
desktop's arriving (two "Merge from", one Merge data) **never ended** in the same way: the same
progress dialog for 90 or 150 s, the file left as the phone's copy, and in the watched run
(`mfPDhang-r1`) the process at 134 %, 163 % and 113 % CPU at 15, 60 and 150 s, its CPU time
growing from 1:53 to 4:53. Four runs of the same two copies (`mfPD-r1`, `mfPD-r2`, `extmPD-r2`,
the watched `mfPDhang-r2`) wrote their result about a second after the answer, all four alike. A
further run (`mfPD-r3`, not counted) was pulled 14 s after the answer with nothing written, so
whether it had ended is not known. Its arriving copy deletes the same three-group tree. **Read**: the arriving records
are held in a `HashSet` (`DatabaseKDBX.kt:150`) and visited in its iteration order, and KeePassDX
writes that set in the order `... NB NB1 NC NC1 ... NA ...` (measured, its own save of `DESK`),
`NB` before `NC`; why the same file loops in one run and not in another was not found.

### 6.4 A move at equal times

Measured in the main pair (3.2): `Y3`, moved by the desktop without a new modification time, went
**back** to its old group when the desktop's copy was open and the phone's arriving; `Y6`, moved on
the phone the same way, went back when the phone's copy was open. With the move as KeePassDX makes
it (a new modification time, `Y5`), it held in both directions.

### 6.5 Arriving deletion records, and a third stale copy afterwards

Section 3.2 measured the first half: a deletion that arrives in a KeePassDX merge is applied and its
record is not kept (`X1`, `DZ`, `DRV2` and the six records of the `NA` tree, with the phone's copy
open). The second half, a later KeePass Synchronize of that result with a third copy that is older
than every deletion (`B41`, standing for a device that has not synchronized since), headless,
`work\sync\stale-mfPD\`, and the same with KeePass's own merge result in place of KeePassDX's
(`stale-kpPD`):

| item | KeePassDX's merge result, then KeePass with the stale copy (both directions alike) | KeePass's merge result, then KeePass with the stale copy |
| --- | --- | --- |
| `X1`, deleted on the desktop | **back**, at its old value | stays deleted, record kept |
| the `NA` tree, three groups and three entries deleted on the desktop | **all back**, at their old values | stays deleted, six records kept |
| `GD`, `GD1` | **back, `GD1` at its old value** (the phone's later change was already lost in KeePassDX's merge) | `GD` and `GD1` with the phone's change |
| `DRV2`, `DZ`, the records naming no entry | absent | present |
| the revocation's entry `RV2` and key `KPG.Revoked.<c3>` | present | present |
| `X4`, deleted on the phone (its record the phone's own) | stays deleted | stays deleted |

The same on the device: KeePassDX's own result of the PD merge (`mfPD-r1`) open, the stale `B41`
arriving, "Merge from", 1 counted run (`mfStale-r1`): **`X1` back, the whole `NA` tree back
(groups and entries), `GD` and `GD1` back at their old values**, `DRV2` and `DZ` absent; the same
items as KeePass's headless result above.

So a deletion that reached a phone only through a KeePassDX merge is undone by the next merge with
a copy that still holds the entry, in KeePassDX or in KeePass. For the store, two of a
revocation's three carriers survive this sequence (the entry and the key); the record naming no
entry does not.

## 7. History limits (item 6)

`HL`: `HistoryMaxItems` 3, `HistoryMaxSize` 6000 bytes. `HL1` holds three history items (10:20,
10:30, 10:40; current 10:50), at the item limit; `HL2` holds one history item with 2000-character
notes and current notes of 2000 characters. **KeePassLib 2.62 prunes to these limits when it saves**
(measured: a copy made with seven versions of `HL1` read back with three), so no file the probe
writes holds more history than the limits; the merge case below is therefore two copies each at
the limit whose union is not.

| case | counted runs | what changed | history afterwards | which items went |
| --- | --- | --- | --- | --- |
| edit of `HL1`'s password in KeePassDX, one past the item limit (chain `ch-history`, s01) | 1 | the pre-edit version added | 3 items: 10:30, 10:40, 10:50 | **the oldest (10:20)** |
| the same again (s02) | 1 | the pre-edit version added | 3 items: 10:40, 10:50, and the edit's time | **the oldest (10:30)** |
| edit of `HL2`'s password, past the size limit (s03) | 1 | the pre-edit version (2000-character notes) added | 1 item: the pre-edit version | **the older item (10:30)**: by size, oldest first |
| Merge from: `HL` open, `HLa` arriving (`HL1` newer there, three later versions 15:40 to 16:00; `HL2` newer, one later version) | 2 | the arriving versions current | `HL1`: 3 items, 15:40, 15:50, 16:00; `HL2`: 1 item, 15:50 | **every version the open copy held** (10:20 to 10:50 for `HL1`, 10:30 and 10:40 for `HL2`) |
| KeePass's Synchronize, `HL` open, `HLa` arriving | headless | | the same three and one items | the same |

So KeePassDX prunes history on an edit by count and by size, oldest first, and after a merge its
result held exactly what KeePass's Synchronize holds. **Read**: KeePassDX's merge path calls no
pruning (`DatabaseKDBXMerger.kt`, `MergeDatabaseRunnable.kt`; pruning is in `UpdateEntryRunnable.kt:87`
and the max-items setting); where the open copy's versions went in the merge was not found.

## 8. The way back (item 7)

### 8.1 Into KeePass, headless

Every result file of sections 2 to 7 that differs from the file KeePassDX was given was opened and
saved by KeePassLib 2.62 alone ("open and save"), and synchronized with the untouched desktop copy
in both directions (`scripts\item7.sh`, `sync.sh`; `work\sync\back-*\`): the base for an operation,
`DESK` for a merge of the pair, the desktop's later save (`NEW-diff`, `NEW-same`, `NEW-ident`) for
an overwrite, the arriving copy for a hazard merge. Each result was read back and diffed against
KeePassDX's file and the desktop copy.

| KeePassDX's files | files | KeePassLib's open and save | synchronized, KeePass's copy open | synchronized, KeePassDX's file open |
| --- | --- | --- | --- | --- |
| the saves (2.1) | 6 | KeePassDX's file line for line | the same | the desktop copy (the two differ only in the root's access time) |
| the operation chains (2.2 to 2.7, every step's file of the first chains) | 17 | line for line | KeePassDX's file: every edit, every history item, the rewritten expiry; where KeePassDX moved an entry's modification time without a version (a delete to the bin, a move), **one history item more** (KeePass's copy's version backed up) | the same |
| the merges of the pair (3) | 12 | line for line | **KeePass puts back what KeePassDX's merge dropped**, where the desktop's copy still holds it: the records `DRV2`, `DZ`, `X1` and the `NA` tree's, `Y3`'s previous parent; it drops the stale records KeePassDX kept and the empty groups it brought back. **What KeePassDX's merge lost stays lost**: `GD1`'s phone change, the losing side of `Q3` | the same, by KeePass's rules |
| the overwrites (4) | 17 | line for line | **every desktop change back** (the revocation's entry, key and record, `P1`, `X1`'s deletion), with the phone's own change | the same |
| the attachment collision (6.1), a save | 2 | line for line | **the wrong bytes arrive in KeePass's copy**: `A2`'s and `A3`'s `b.bin` now hold `a.bin`'s bytes (equal modification times, and KeePass takes the arriving copy then) | KeePass's copy, the right bytes |
| the history attachments (6.2) | 2 | line for line | the desktop copy's right bytes kept (KeePass keeps the open copy's history item at an equal time) | **the wrong bytes stay** in `E1`'s and `H1`'s history |
| the nested deletions (6.3), `HNESTe` | 2 | line for line | the deleting copy (`HNESTa`): the tree deleted; `NB1`'s later change stays lost | the same |
| the history limits (7) | 6 | line for line | the desktop copy's history | the same |

So **KeePassLib reads every file KeePassDX wrote and loses nothing of it**, and a KeePass that
still holds its own copy and synchronizes undoes most of what KeePassDX's merges dropped and every
overwrite. What it does not undo is a loss KeePassDX's merge made between the phone's and another
copy when the desktop's copy never held the lost value (`GD1`'s phone change, `NB1`'s), and the
attachment damage of 6.1 and 6.2 travels: into KeePass's copy when KeePassDX's file arrives with
equal times (6.1), and stays when KeePassDX's file is the one open (6.2).

### 8.2 Through Keepass2Android 1.15-r6

Four KeePassDX files opened in Keepass2Android, an entry added at the root and saved (`k2a-A.flow`),
and one opened there with the desktop's `DESK` pushed over it before the save, answered "Yes,
merge" (`k2a-merge.flow`); one counted run each, from cleared data; each then synchronized with the
desktop copy as in 8.1.

| file given to Keepass2Android | what Keepass2Android wrote | into KeePass, both directions |
| --- | --- | --- |
| KeePassDX's PD merge (`mfPD-r1`) | the given file and the new entry; **every per-key time of the database's custom data dropped**; the generator emptied; format 4.1 kept | the phone's file with the per-key times restored from the desktop copy, and the records KeePassDX's merge had dropped put back from `DESK` (as in 8.1) |
| KeePassDX's save of `B41` | the same: the new entry, the per-key times dropped | the phone's file, per-key times restored |
| KeePassDX's save of `B41aes` | the same | the same |
| KeePassDX's edits of `E1` (s01 to s04) | the same; `E1`'s four new history items and the rewritten expiry kept | the same |
| KeePassDX's edits of `E1`, with `DESK` arriving, "Yes, merge" | **"Merge changes?"** at the save; the merged file: **line for line KeePass's Synchronize of the same two copies** (`E1`'s edits open, `DESK` arriving), but the per-key times and the root group's access time | the merged file, per-key times restored |

So a file KeePassDX wrote goes through Keepass2Android as any other: every item kept, the per-key
times dropped by Keepass2Android and restored by KeePass (`measured-android.md` 2.1, 5.1).

## 9. Two gaps of the first measurement (item 8)

`measured-android.md` section 7 claims that every Keepass2Android operation went back into KeePass, but
its record synchronized only the save, the two edits and the merges. The files Keepass2Android
1.15-r6 and KeePassDX 4.5.8 saved on 2026-11-15 for the rename, the delete, the move and the
AES-KDF saves were copied from that measurement's working directory (their SHA-256 in
`item8\inputs.sha256`) and synchronized here with the base each was given, in both directions,
by KeePassLib 2.62 (`scripts\item8.sh`, `sync.sh`); "KeePassLib's open and save" is that file opened
and saved by KeePassLib alone. No emulator run: these are the files the programs wrote then.

| file the phone saved (`measured-android.md` step) | base | into KeePass (KeePass's copy open) | into the phone's file | KeePassLib's open and save |
| --- | --- | --- | --- | --- |
| Keepass2Android, rename G (step 4) | `S-argon` | the phone's file exactly, the per-key time of `KPG.Profiles` restored from KeePass's copy | the same | the phone's file exactly |
| Keepass2Android, delete E2 to the bin and from it (step 5) | `S-argon` | the same: E2 gone with its record, every other line the phone's | the same, and the bin's and the root's access times and usage counts from KeePass's copy | exactly |
| Keepass2Android, move E1 into N and N to the root (step 6) | `S-argon` | the phone's file exactly, per-key time restored | the same | exactly |
| Keepass2Android, save of `S-aes` (step 1) | `S-aes` | the phone's file exactly, per-key time restored | the same | exactly |
| Keepass2Android, save of `S-aes-nocd` | `S-aes-nocd` | the same | the same | exactly |
| Keepass2Android, save of `S-aes-plain` (the phone wrote KDBX 3.1 with no `SettingsChanged`) | `S-aes-plain` | every item the phone's; written as **KDBX 4.0**; `SettingsChanged` the moment of the synchronize (a file without one reads as "now", which is later than KeePass's) | the same | KDBX 4.0, every item the phone's |
| KeePassDX 4.5.8, move E1 into N and N to the root | `S-argon` | the phone's file and **one history item more on E1**: KeePass's copy's version at 14:00, because KeePassDX's move gave E1 a new modification time | the same | exactly |
| KeePassDX 4.5.8, saves of `S-aes`, `S-aes-nocd`, `S-aes-plain` | each its base | the phone's file exactly | the same | exactly |

So the claim holds for these operations: nothing Keepass2Android or KeePassDX wrote in them was lost
on the way back, and what Keepass2Android dropped (the per-key time) came back from KeePass's copy.
`k2a-save-aes-plain` is the one file whose format changes on the way back (3.1 to 4.0). This
release's own AES-KDF saves (`B41aes`, `B40aes`, 2.1) went back the same way (8.1).

## 10. Not run, and why

Cut, from the end of the list upward as far as the time allowed, never items 2, 3 and 5:

- **Item 4, write-protected opening**: the mode switch took the driver's taps once in five
  attempts, and that attempt stopped on a flow assumption (section 5). Not counted.
- **Item 1, the entry edits after the notes** (tags, a custom field added, changed and removed, an
  attachment added and removed, the icon, the expiry): three chains stopped at the first of these
  steps. The tags field sits below the editor's bottom bar, and neither swipes nor moving the focus
  by key brought it within the driver's reach; the third chain stopped at the field dialog's OK.
- **Item 1, moves after the first** (a record out of `KeePassGate`, an entry into it, a group into
  it, a group to the root), **the group icon and expiry, the rename of `KeePassGate`**: chains
  stopped on screens the driver did not find (section 2.4, 2.6).
- **Item 1, templates, the database settings** (name, default user name, history limits, recycle
  bin on and off and its group, compression, encryption, the KDF and its parameters, color),
  **the master key change and "Save a copy to ..."**: not started; their screens were never worked
  out, and the time went to items 2, 3 and 5.
- **Item 3, the unlock methods other than the password** (biometric, device credential) and the
  application timeout: not run; **read**, they supply the password to the same load as the
  password screen.
- **Item 7, a KeePass window**: every synchronize into KeePass is KeePassLib 2.62 headless; KeePass's
  own dialog on a changed file at save was not run.
- **Why a merge of the main pair sometimes never ends** (6.3): seen in 3 of 7 runs of the same two
  copies; the cause was not found.
- **Any device but this emulator**, any Android but 11, any KeePassDX flavor but libre, any release
  but 4.5.8.

Attempts not counted (32, each repeated or cut, files and logs under `not-counted\`): the first
"Merge from" PD run pulled before its save (1); the password lost at unlock (2); in the overwrite
runs, the external-change dialog not expected in the entry editor (9), its second showing on the
group screen (1) and a title typed only in part (1); the autosave-off answers, whose taps on the
overflow opened Sort (3); the lock tap ignored (4); the write-protected switch (6, section 5); the
entry chain (the file picker with a text file beside the database, the tags field twice, the field
dialog: 4); the move chain pasting in place (1). The first chain runs of the groups, the moves and the copies stopped
part way and count for their completed steps only (1.6).

## 11. The runs counted

| item | what | counted runs |
| --- | --- | --- |
| 1 | open and save unchanged, six KDF and format variants; the base with unknown elements (refused) | 6; 2 |
| 1 | chains: entry edits (4 steps), store-entry titles (4), groups (2 + 1), deletions (5), moves (1), copies and new nodes (2 + 3) | 8 chain runs, 22 counted steps |
| 2 | Merge from PD 5 (3 merged, 2 never ended), DP 3; external change, Merge data PD 2 (1 merged, 1 never ended), DP 2; Reload data PD 3, DP 1; Save data PD 2, DP 1 | 19 |
| 3 | the boundary on screen: `NEW-diff` equal, +1, +5 (2), +9, +10 (2), +11 (2), +15, +60 (2), earlier; `NEW-same` equal, +5, +60; `NEW-ident` +60 | 17 |
| 3 | from the background (3), without a screen change (2), a new process (1), the lock (2), autosave off with Merge data (1) and Save data (1) | 10 |
| 4 | write-protected opening | 0 (cut) |
| 5 | attachment collision: save (2), merge (2), reverse (1); history attachments (2); nested deletions: group (2), entry (2), reverse (1); the main pair watched (2, counted in item 2); a stale third copy on the device (1) | 13 |
| 6 | history: edits (1 chain, 3 steps), merge (2) | 3 |
| 7 | Keepass2Android: open, add, save (4); merge on save, "Yes, merge" (1); headless synchronizations of every differing result | 5 on the emulator |
| 8 | headless synchronizations of the first measurement's files | 0 on the emulator |

Total counted emulator runs: 6 + 2 + 8 + 19 + 27 + 13 + 3 + 5 = **83**, besides the 32 attempts not
counted. Every count above was checked against its table.

## 12. What can be relied on

### (a) Each operation

| operation | the generic carriers | the recommended store's records | today's custom data |
| --- | --- | --- | --- |
| open and save unchanged (Save data), every KDF, KDBX 4.1 and 4.0 | **safe** (2.1) | **safe** | **safe**, per-key times kept |
| an entry edit (title, password, notes), and a Validate with nothing changed | **safe, with side effects**: a new version every time, the expiry rewritten to 2999-11-28, the parents' times moved | **safe**: an accidental title edit keeps every `KPG.` string (2.3) | **safe** |
| a group rename or notes edit | **safe, with side effects**: the group's expiry rewritten, its time moved, so its notes, tags and custom data go into a merge as the newer group | safe | safe (`G`'s `KPG` kept) |
| a group tags edit by typing | **losing** in the one run: the last tag was replaced (2.4) | not used by the store | not touched |
| delete to the recycle bin, delete for good, empty the bin | **safe**: deletion records written for everything deleted for good | **safe under a habit**: a recycled record keeps its strings and its time moves; emptying the bin deletes it with a record, so a merge with an older copy is decided by that record (6.5) | safe |
| a move | **safe**, the entry's time moved | not measured for the store's entries | safe |
| copy, new entry, new group | **safe**; a copy carries no history | safe | safe |
| attachments (any save, any merge) | **losing**: two attachments whose MD5 digests share four bytes become one; history attachments from a merge show other attachments (6.1, 6.2) | **safe as long as the store keeps nothing in attachments**; `R1`'s protected attachment did appear in another entry's history | not used |
| a file with an element KeePassDX does not know | **unusable**: the file cannot be opened (2.1) | the same | the same |
| "Merge from" or Merge data, nothing deleted on either side | safe for entries changed on one side, new on either side, or changed on both at different times | **safe**: every write-once record, key and new entry from either copy kept | safe |
| the same with deletions or equal times | **losing** (section 3.2, (b) below) | **losing** the revocation's record naming no entry (one of three carriers) | safe |
| a merge whose arriving copy deletes a group tree | **may never end** (3 of 7 runs of the main pair; 2 of 2 with a surviving child group) | nothing written, nothing lost by itself | the same |
| Reload data, Save data after the dialog | **losing** one side whole (3.1) | losing | losing |
| the program's own lock and unlock, a new process | **safe**: the file is read afresh (4.1) | safe | safe |

### (b) Every sequence measured that loses what another writer wrote

1. **The file is replaced underneath while KeePassDX holds it open, and any change is then saved**
   (autosave after an edit, a delete, a move, or Save data): the newer file's changes are gone,
   with nothing in the file to show it (22 runs of 22 with a change to lose: every replacement time
   tried, earlier, equal and later; with and without the dialog; on screen and from the background). The dialog shows only
   for a replacement more than ten seconds newer and only when a screen resumes, and answering OK
   protects nothing.
2. **The external-change dialog answered OK, then Save data**: the open copy over the newer file
   (3.1, 4.1). **Reload data**: every unsaved and every merged-away change of the open copy gone.
3. **A merge where both copies changed one entry at the same second**: one side's change exists
   nowhere (`Q3`).
4. **A merge where one copy deleted a group and the other later changed an entry inside it**: the
   later change is deleted with the group, no record left (`GD1`, `NB1`).
5. **A merge that brings deletions, followed by a merge with any copy older than those deletions**,
   in KeePassDX or in KeePass: the deleted entries and groups come back (6.5). For the store, the
   revocation's record naming no entry is dropped in the first merge.
6. **A merge that moves an entry back**: a move made without a new modification time is undone
   when the other copy is merged in at an equal time (`Y3`, `Y6`).
7. **Two attachments of one length whose MD5 digests share four bytes**, in one database or one
   arriving by merge: one body replaces the other at the next save; **history items that arrive
   by merge** show another attachment's bytes. KeePass's Synchronize then carries the wrong bytes
   into KeePass's copy when the KeePassDX file arrives (8.1).
8. **A merge that never ends**: nothing is lost by it, but nothing is merged either, and the
   program must be stopped.

### (c) Every difference from KeePass's own Synchronize, measured on the same two copies

Section 3.2's six (deletion records not kept; stale records kept; equal times keep the open copy,
KeePass the arriving one; equal-time moves follow the arriving copy and lose their previous
parent; a deleted group takes a later-changed entry with it; deleted groups return empty), and from
section 6: the never-ending merge (KeePass ended every one), the attachment collision and the
history attachments (KeePass kept both bodies and every history attachment), and the nested
deletion with a surviving entry (KeePass kept it). The history limits (7) and every other item of
the pair came out as KeePass's.

### (d) Differences from KeePassDX 4.5.8 as first measured

The release is the same; nothing measured here contradicts `measured-android.md` section 4, and
what it measured was found again where it was run again: equal times keep the open copy's version,
an arriving deletion leaves no record, a move without a new time follows the arriving copy, every
edit rewrites the expiry and moves the parents' times, the per-key times are kept. New against that
report: a Validate with nothing changed makes a version; the file-change check and its boundary
(4); Reload and Save after the dialog (3.1); the lost later change in a deleted group (3.2); the
merge that never ends, on the main pair too (6.3); the attachment collision and the history
attachments on the device (6.1, 6.2); a file with an unknown element cannot be opened (2.1); a
Keepass2Android pass of KeePassDX's files (8.2). Its section 7 claim that every operation of either
program went back into KeePass now has synchronize runs behind the rename, delete, move and AES-KDF
saves (9).
