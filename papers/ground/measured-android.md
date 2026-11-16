# Measured: what Keepass2Android, KeePassDroid and KeePassDX do to KPG data in ordinary entries

Run 2026-11-15 on an Android emulator, for the storage rethink. The question: which KDBX features
that are candidates for KPG data come through Keepass2Android and KeePassDroid unchanged, under
everything a phone does to a database (a plain save, an edit, an accidental edit of a carrier, a
group edit, a delete, a move, a merge), and what is left after the result is synchronized back into
KeePass. KeePassDX was run third, after the first two were complete.

**Everything in the tables is measured**: a file a program saved on the emulator, pulled back and
read with KeePassLib 2.62. Where something is read from source it is said so, in prose, and marked
**read**; nothing read is in a table. "Kept" means the read-back line is byte for byte the base's
line (values, protection flags, attachment hashes, tags, times).

## 1. How the runs were made

### 1.1 Programs

Each APK the project publishes on GitHub, the files the spike `spikes/revocation-merge` pinned and
cached, copied into a directory of this measurement's own inside WSL and held again to the pins by
`verify-apks.sh` (`apksigner` from build-tools 37.0.0): the file's SHA-256, a verifying signature,
and exactly one signer whose certificate digest is the pinned one. All three matched.

| program | release | file | SHA-256 of the file | signer certificate SHA-256 |
| --- | --- | --- | --- | --- |
| Keepass2Android | 1.15-r6 (versionCode 254) | `keepass2android.keepass2android-Signed-x64.apk` | `5c446a4fff3ac3fe29b07724dff59e756738eb1fd6611281cd594e421817891d` | `0bf8056d3a394ed7c77173c4b42c800565b4f0a42977569cf3472c271c562939` |
| KeePassDroid | 2.7 (versionCode 223) | `app-general-release.apk` | `513b2555dbebf8324d81455642f1b709c3692e7558110b576f90f715035ca762` | `692ed2f00824264edf569615b448a96f0220fd57137e03e146df3647ef6e4cf5` |
| KeePassDX | 4.5.8 libre (versionCode 45800) | `KeePassDX-4.5.8-libre.apk` | `8aaac6a0c55a9de760147990856726e197efbbcdc5ce2f6bfb4d4ab2a3d65f74` | `7d55b8af210381aabf960f07e17cf7857b6d2a642ca2da6bf0bdf1b200362f04` |

### 1.2 The emulator

Android 11, API 30, `google_apis` x86_64 system image revision 16, device `kpg30` (pixel_5, 4 cores,
3072 MB, 6 GB data, hardware keyboard, 1080 x 2340 at 440 dpi), Android emulator 37.2.13.0,
platform-tools 37.0.1, started with **its user data wiped**, headless (`-no-window -no-audio
-no-boot-anim -no-snapshot -gpu swiftshader_indirect`), under Linux 6.18.58.1 in WSL 2
(`Ubuntu-24.04`, KVM loaded by `root-setup.sh`), driven over `adb` as root with animations off. No
window was shown on the desktop. A second emulator started beside it to run KeePassDroid in
parallel exhausted WSL's memory (91 MB free) and raised "System UI isn't responding"; it was
stopped before anything was measured on it, and every run below is on `kpg30`, one at a time.

### 1.3 The databases

Made by a console probe over **KeePassLib 2.62** (`KeePass.exe` from `.keepass\2.62`,
`StoreProbe.exe`, no window), invented content only, password
`dummy-password-for-a-throwaway-file`. Every node has a **fixed identifier** (`KPG` and an ASCII
alias), so the read-back names a node by identity and never by title; every time is explicit and
whole seconds from T0 = 2026-11-01T10:00:00Z. The main base `S-argon` (Argon2d, 2 iterations,
1 MiB, 1 lane; KDBX 4.1 as KeePass 2.62 writes it) holds:

| node | what it carries |
| --- | --- |
| group `KPG` (the plugin's own group), under the root | notes (text), searching off, auto-type off, collapsed |
| `R1` "KPG record 1" in `KPG` | standard fields, Notes holding JSON; custom strings `KPG.Record` (plain, 2861 characters of JSON), `KPG.Secret` (protected JSON), `KPG.20261115T101500Z.a1b2` (plain JSON), `KPG.20261115T101501Z.c3d4` (protected JSON); attachment `authority.p12`, 3000 random bytes, protected; tags `kpg;record`; **three history items** at +1 h, +2 h, +3 h whose custom fields differ (only `KPG.Record` v1; then v2 and the first timestamp field; then the long JSON, the second timestamp field, `KPG.Secret` v0 and the attachment); current at +4 h; entry custom data `KPG.cd` (control) |
| `R2` "KPG record 2" in `KPG` | custom string `KPG.20261115T101502Z.e5f6` (plain JSON); attachment `copy.p12` with **the same bytes** as `authority.p12`; tag `kpg` |
| `M1`, `M2` in `KPG` | `KPG.base` = `{"v":0}`; the merge pair changes them |
| group `G`, under the root | Notes holding JSON; group tags `kpg.reader;team` (a KDBX 4.1 feature); group custom data `KPG` (control) |
| `Gm` "KPG marker" in `G` | custom string `KPG.Marker` (plain JSON) |
| `E1` in `G` | standard fields; extra custom string `Extra`; attachment `e1.txt` (600 bytes); tag `work`; **three history items** (no `Extra`; `extra-v1`; `extra-v2` and a field `Old`); entry custom data `KPG` (control) |
| `E2`, `X1`, `X2`, `Y1`, `Y2`, `Y3` in `G` | ordinary entries, for deleting and for the merge cells |
| group `N` under `G`, holding `N1` | for the moves |
| group `Recycle Bin`, the database's recycle bin | empty; `RecycleBinUUID` points at it |
| deletion records | `D1` at +6 min, `D2` at +7 min, naming entries the file does not hold |
| database custom data (control) | `KPG.Profiles` with a per-key time (a KDBX 4.1 field), `KPG.Untimed` without |
| meta | `HistoryMaxItems` 8, `HistoryMaxSize` 5 MiB, `MaintenanceHistoryDays` 200 (none of them KeePassLib's defaults) |

Three AES-KDF variants (60000 rounds), for the format choice the spike found to depend on the KDF:
`S-aes` (the same content), `S-aes-nocd` (no custom data on any group or entry, group tags kept),
`S-aes-plain` (no node custom data, no group tags, no per-key time; KDBX 4.0 as KeePass writes it).

**The merge pair C and P** are `S-argon` opened, changed and saved by KeePassLib 2.62, each change
made as KeePass's entry dialog makes one (the old version into history first, then the change, then
an explicit time):

| cell | C | P |
| --- | --- | --- |
| same entry, different fields, different times (`M1`) | adds `KPG.20261115T120000Z.c` at +5 h | adds `KPG.20261115T140000Z.p` at +6 h |
| same entry, different fields, **equal** times (`M2`) | adds `KPG.20261115T130000Z.c` at +7 h | adds `KPG.20261115T130000Z.p` at +7 h |
| a new entry in the same group (`KPG`) | `NC` at +5 h | `NP` at +6 h |
| deleted on one side, unchanged on the other (`X1`) | unchanged | deleted, record at +6 h |
| deleted on one side, changed later on the other (`X2`) | deleted, record at +5 h | user name changed at +6 h |
| attachment added on one side (`Y1`) | `added-c.bin`, 1500 bytes, at +5 h | unchanged |
| tag added on one side (`Y2`) | unchanged | tag `added-p` at +6 h |
| G's notes changed on one side | changed at +5 h | unchanged |
| entry moved on one side (`Y3`) | unchanged | moved from `G` to `N` at +6 h, previous parent recorded |

A measured side fact, not about Android: **KeePassLib 2.62 resets `MemoryProtection` to its
defaults at every load** (read: `KeePassLib/Serialization/KdbxFile.Read.cs:246-248`) and writes the
standard fields' protection flags from it, so a base first written with "protect user name" on came
back from KeePassLib's own open and save with every `UserName` unprotected (measured: 42 lines,
`android-evidence\keepasslib-memprot-reset.txt`). The bases were therefore written with KeePass's
default protection, so that what a phone does to a flag is not hidden behind what KeePass does.

### 1.4 The flows

The spike's scripts, copied and changed (`scripts\` in the evidence): `run-op.sh` runs one
operation from cleared data: `pm clear` for the program, the base alone in `/sdcard/Download`, a
flow file run by `k.py` (which reads the screen through `uiautomator` and presses what the flow
names), and every file left in `Download` pulled back. Files the flow pulls in the middle (after
the first half of a delete or a move) are pulled by the flow itself. Each file is read by the probe
into a sorted line-per-item dump (one line per string with its name, protection flag and value, per
attachment with its length and hash, per tag list, per time, per history item keyed by its
modification time, per deletion record, per meta field) and diffed against the dump of the file the
program was given.

| step | Keepass2Android flow | KeePassDroid flow |
| --- | --- | --- |
| 1 plain save | `k2a-A.flow`: open, unlock, add `droid-added` at the root, save | `kpd-A.flow`: the same |
| 2 edit on the phone | `k2a-edit-e1.flow`: G, E1, Edit, append `-phone` to the password, Save | `kpd-edit-e1.flow`: G, E1, EDIT, append `-phone` to the password and its confirmation, SAVE |
| 3 edit a carrier | `k2a-edit-carriers.flow`: KPG, "KPG record 1", Edit, append `-phone` to the title, Save; then G, "KPG marker", the same | `kpd-edit-carriers.flow`: the same |
| 4 group edit | `k2a-rename-g.flow`: long-press G, Edit, append `-phone` to the name, OK | `kpd-menus.flow`: long-press G and list what is offered |
| 5 delete | `k2a-delete.flow`: G, long-press E2, Delete, **No** ("Press No to recycle"); pull; Recycle Bin, long-press E2, Delete, Yes | `kpd-delete.flow`: G, long-press E2, Delete; pull; Recycle Bin, long-press E2, Delete |
| 6 move | `k2a-move.flow`: G, long-press E1, Move to another group, N, Insert here; pull; up to G, long-press N, Move, up to the root, Insert here | `kpd-menus.flow`: long-press E1 and list what is offered |
| 7 merge | `k2a-merge.flow`: open the target, push the arriving copy over its file, add `droid-merge`, Save, "Merge changes?" answered "Yes, merge" | `kpd-menus.flow`: the group view's menu listed |

Step 8 is `sync.sh`: KeePassLib 2.62, `PwDatabase.MergeIn(source, PwMergeMethod.Synchronize)`, the
result saved and read back, in both directions: **into KeePass** (KeePass's copy open, the phone's
file arriving) and **into the phone's file** (the phone's file open, KeePass's copy arriving).

**The evidence** is in `android-evidence\` beside this report: `base\` the dumps of the files the
programs were given; `runs\<program>\<run>\` each pulled file's dump and its diff against the base
(the merge runs' diffs are against the open copy, C or P); `sync\<label>\` step 8's dumps and diffs
(against the base, the phone's file and KeePass's copy); `logs\runs\<program>\` every step a run
pressed, every dialog listed, the hashes pushed and pulled, the uncounted attempts under
`not-counted\`, and the batch summaries; `flows\`, `scripts\` and `probe\` as they stood at the
end (flows were corrected between attempts; each log records what its run pressed);
`keepasslib-memprot-reset.txt`. Machine paths in the logs are written `<work>` and `<dl>`. No
database is copied.

### 1.5 What was counted

Only runs whose flow ended without a failure, as in the spike, and for step 2 only a run whose
read-back shows the change it was meant to make. Repeated from cleared data, not counted:

- Keepass2Android, step 1: the first attempt, which stopped in the file picker (a wiped device
  shows folders as a grid, and the picker walk only went down; the walk was made a snake).
- Keepass2Android, steps 4 and 5: the first attempts, which stopped at unlock with 34 and 32 of the
  password's 35 characters in the field, keystrokes lost while the second emulator was booting;
  the password is now typed by `typepwck`, which checks the field's length.
- KeePassDroid, step 3: the first attempt, which stopped before saving: the pattern `entry_title`
  also matches the label `entry_title_label`, which comes first, so the flow tapped the label. The
  fields are now named by id (`entry_title@.*`).
- KeePassDroid, step 2: the first attempt ended without a failure, but for the same reason the text
  went to the password's label, and the file came back with the password unchanged. It is not
  counted as step 2; what it shows is in section 3.3.

- KeePassDX: the attempts listed in section 6, all stopped before saving anything.

Counted: Keepass2Android 15 runs (4 saves, 5 operations, 6 merges), KeePassDroid 8 runs (4 saves,
3 operations, 1 menu listing), KeePassDX 15 runs (4 saves, 5 operations, 6 merges).

## 2. Keepass2Android 1.15-r6

### 2.1 Carrier by operation

One run each; in every column, a carrier not named as changed read back exactly as given.

| carrier | 1 plain save | 2 edit E1 (password) | 3 edit R1 and the marker (title) | 4 rename G | 5 delete E2 | 6 move E1 into N, N to the root |
| --- | --- | --- | --- | --- | --- | --- |
| group KPG: notes, searching off, auto-type off | kept | kept | kept | kept | kept | kept |
| R1 standard fields, Notes JSON | kept | kept | title changed as asked; the rest kept | kept | kept | kept |
| plain custom strings with JSON, the 2861-character value, timestamp names | kept | kept | kept | kept | kept | kept |
| protected custom strings, their flag | kept | kept | kept | kept | kept | kept |
| attachment `authority.p12` (protected) and the same bytes as `copy.p12` on R2 | kept, two attachments | kept | kept | kept | kept | kept |
| entry tags | kept | kept | kept | kept | kept | kept |
| R1's three history items | kept | kept | kept, **one item added**: the whole version before the edit (every string with its flag, the attachment, tags, Notes, custom data) | kept | kept | kept |
| marker entry, `KPG.Marker` | kept | kept | title changed; field kept; one item added | kept | kept | kept |
| G's notes (JSON) | kept | kept | kept | **kept** (the dialog has no notes) | kept | kept |
| G's tags | kept | kept | kept | kept | kept | kept |
| E1: `Extra`, tag, `e1.txt` | kept | **kept**; password changed | kept | kept | kept | kept, in N |
| E1's three history items | kept | **kept, one item added** holding the version before the edit, carriers included | kept | kept | kept | kept |
| N and N1 | kept | kept | kept | kept | kept | N under the root, N1 in it |
| deletion records D1, D2 | kept | kept | kept | kept | kept, and **E2's added** | kept |
| modification times of nodes not edited | kept | kept; G and the root: access time only | kept; KPG, G, the root: access time only | G and **the root** moved to the time of the edit | E2's **unchanged** in the bin; location time moved | E1's and N's unchanged; location times moved |
| previous parent | (none in the base) | | | | **not recorded** for the recycled E2 | **not recorded** for E1 or N |
| format | **4.1** | 4.1 | 4.1 | 4.1 | 4.1 | 4.1 |
| control: entry custom data (E1, R1), group custom data (G) | kept | kept | kept | kept | kept | kept |
| control: database custom data | values kept, **the per-key time dropped** | the same | the same | the same | the same | the same |

The AES-KDF variants, step 1: `S-aes` and `S-aes-nocd` came back **4.1** with the same result as
`S-argon` (only the per-key time gone). **`S-aes-plain` came back as KDBX 3.1 with no
`SettingsChanged`** (it read back as the moment of reading), and with every carrier it held kept:
entries, plain and protected custom strings, attachments, entry tags, history, the KPG group's
flags, G's notes and the deletion records. So group tags, or any node custom data, kept the file at
4.x; without them the AES-KDF file dropped to 3.1, which loses nothing these carriers use.

Step 5: K2A asked "Delete permanently? ... Press No to recycle." (Cancel, No, Yes); No moved E2 into
the database's own `Recycle Bin` (the one `RecycleBinUUID` names). Inside the bin the question had
no recycle option, and Yes deleted E2 and wrote its deletion record. K2A has no "empty the recycle
bin": its group menu lists Advanced Search, Settings, Synchronize database, Work on internal cache
only, Sort by, Open another database.

### 2.2 The merge

Three runs a cell; **within each cell the three runs read back identically** (once the entry the run
added is set aside), and the two directions differ in exactly one line, `M2`'s field. Every result
is KDBX 4.1, every carrier outside the cells is as in the base, and the database's per-key time is
gone, as in a save.

| cell | C open, P arriving (CP, 3 runs) | P open, C arriving (PC, 3 runs) |
| --- | --- | --- |
| **`M1`, different fields, different times** | **P's version whole** (P's field only, at P's time +6 h); history: the base version (+50 min) and **C's version whole, holding C's field** (+5 h) | the same |
| **`M2`, different fields, equal times** | **P's version whole** (the arriving copy's); history: the base version only; **C's field exists nowhere** | **C's version whole** (the arriving copy's); **P's field exists nowhere** |
| `NC` and `NP`, new entries in `KPG` | both present | both present |
| `X1` deleted in P, unchanged in C | gone; P's record kept | gone; record kept |
| `X2` deleted in C (+5 h), changed in P later (+6 h) | **present with P's change**; C's record removed from the list | present with P's change; no record |
| deletion records | D1, D2, X1 | D1, D2, X1 |
| `Y1` attachment added in C | present | present |
| `Y2` tag added in P | present | present |
| G's notes changed in C | C's notes (G at C's time) | C's notes |
| `Y3` moved in P | in N, previous parent `G` kept | in N, previous parent kept |

The question an option turns on: **a Keepass2Android merge does not hold both fields.** The newer
entry becomes current whole; the other copy's field survives only inside a history item that is
that copy's whole version. At equal times there is no history item for the loser at all, and which
field survives depends on which copy was open.

## 3. KeePassDroid 2.7

### 3.1 What it offers

Measured on its screens (`kpd-menus.flow`): a long press on a group offers **Open** only; on an entry,
**Open** and **Delete**; the group view's menu holds Donate, Settings, Change master key, DB sort
order. So **step 4 (group edit), step 6 (move) and step 7 (merge) do not exist in KeePassDroid**;
nothing was saved by that run. Delete asks nothing.

### 3.2 Carrier by operation

| carrier | 1 plain save | 2 edit E1 (password) | 3 edit R1 and the marker (title) | 5 delete E2 |
| --- | --- | --- | --- | --- |
| group KPG: notes, searching off, auto-type off | kept | kept | kept; **its modification time moved** | kept |
| R1 standard fields, Notes JSON | kept | kept | title changed as asked; the rest kept | kept |
| plain custom strings with JSON, timestamp names | kept | kept | kept | kept |
| protected custom strings, their flag | kept | kept | kept | kept |
| attachments, the shared bytes | kept, two attachments | kept | kept | kept |
| entry tags | kept | kept | kept | kept |
| R1's three history items | kept | kept | kept, **one item added**: the whole version before the edit (strings, flags, attachment, tags, Notes; no custom data, which the save deletes) | kept |
| marker entry, `KPG.Marker` | kept | kept | title changed; field kept; one item added | kept |
| G's notes (JSON) | kept | kept | kept | kept |
| G's tags | kept | kept | kept | kept |
| E1: `Extra`, tag, `e1.txt` | kept | **kept**; password changed | kept | kept |
| E1's three history items | kept | **kept, one item added** holding the version before the edit | kept | kept |
| deletion records D1, D2 | kept | kept | kept | kept, and **E2's added** |
| modification times | kept | E1 moved; **G's and the root's moved** too | R1's and the marker's moved; **KPG's, G's and the root's moved** | E2's unchanged in the bin; location time moved; previous parent not recorded |
| format | **4.1**, as given | 4.1 | 4.1 | 4.1 |
| `SettingsChanged` | **not written** (reads back as the moment of reading) | not written | not written | not written |
| control: entry custom data (E1, R1) | **deleted** | deleted | deleted | deleted |
| control: group custom data (G) | **deleted** | deleted | deleted | deleted |
| control: database custom data | kept, **per-key time kept** | kept | kept | kept |

The AES-KDF variants, step 1: all three came back at the version they were given (`S-aes` and
`S-aes-nocd` 4.1, `S-aes-plain` **4.0**), with the same result: carriers kept, node custom data
deleted where there was any, no `SettingsChanged`.

Step 5: Delete in G moved E2 into the database's own `Recycle Bin`, its modification time
unchanged; Delete inside the bin deleted it and wrote its record.

### 3.3 An editor save with nothing changed

The uncounted first attempt at step 2 (section 1.5) opened E1's editor, typed nothing into any field
and pressed SAVE. The file came back with **E1's modification time moved, one history item added
(the unchanged version), and G's and the root's modification times moved**, every field as it was.
So KeePassDroid's Save makes a new version whether or not anything changed.

## 4. KeePassDX 4.5.8

Run third, after the two programs above were complete, with the spike's open and add flows and its
"Merge from ..." flow (`kpdx-B-from.flow`: the navigation drawer's Merge from, the user
verification with the first characters of the open database's password, the arriving copy picked
beside the open file, its password, OK; autosave writes the result).

### 4.1 Plain save

All four bases (`S-argon`, `S-aes`, `S-aes-nocd`, `S-aes-plain`) came back with **every line as
given** but the root group's modification and access times, which took the time of the save: every
carrier, every control (entry, group and database custom data, the per-key time), `SettingsChanged`
and the format version (4.1, and 4.0 for `S-aes-plain`).

### 4.2 Carrier by operation

Steps 2 to 6 are in section 4.4, after the merge.

### 4.3 The merge ("Merge from ...")

Three runs a cell (the first attempts at CP runs 2 and 3 stopped at unlock and were repeated,
section 6); within each cell the three runs read back identically but for the root group's access
time. Every result is KDBX 4.1, and every carrier outside the cells is as in the base. The
arriving copy, beside the open file, came back byte for byte as given in every run.

| cell | C open, P arriving (CP, 3 runs) | P open, C arriving (PC, 3 runs) |
| --- | --- | --- |
| **`M1`, different fields, different times** | **P's version whole**; history: the base version and **C's version whole** (+5 h) | the same |
| **`M2`, different fields, equal times** | **C's version whole** (the open copy's); **P's field exists nowhere** | **P's version whole** (the open copy's); **C's field exists nowhere** |
| `NC` and `NP` | both present | both present |
| `X1` deleted in P, unchanged in C | **gone, and no deletion record for it** | gone; P's record kept |
| `X2` deleted in C (+5 h), changed in P later (+6 h) | present with P's change; **C's record for it kept** beside it | present with P's change; no record |
| deletion records | D1, D2, **X2** | D1, D2, X1 |
| `Y1` attachment, `Y2` tag | both present | both present |
| G's notes changed in C | C's notes | C's notes |
| `Y3` moved in P | in N, **previous parent dropped, location time not moved** | **back in G** (C's place), P's previous parent and location time kept |

So KeePassDX takes the newer of two versions whole, as Keepass2Android does, but at equal times it
keeps **the open copy's** version, where Keepass2Android keeps the arriving copy's. A deletion that
arrives removes the entry **without recording it**, and a move on one side **follows the arriving
copy** at equal modification times, in both directions.

### 4.4 Operations on the phone

Flows `kpdx-edit-e1.flow`, `kpdx-edit-carriers.flow`, `kpdx-rename-g.flow`, `kpdx-edit-g-notes.flow`,
`kpdx-delete.flow`, `kpdx-move.flow`. KeePassDX opens nothing on a tap on an entry's row in these
runs and ignores presses for a while after a group opens, so every flow selects a node with a 2 s
press on the middle of its row, repeated until the selection bar shows, and edits through the
selection's menu (Edit, Move, Delete); an edit first asks the first characters of the password.
Step 2 changed E1's **title** and not its password: the tap on the password field left the focus
on the title, the check passed on the title, and the run counts as the title edit the task allows.

| carrier | 2 edit E1 (title) | 3 edit R1 and the marker (title) | 4 rename G | 5 delete E2, empty the bin | 6 move E1 into N, N to the root |
| --- | --- | --- | --- | --- | --- |
| group KPG and its flags | kept | kept; **its modification time moved** | kept | kept | kept |
| R1 standard fields, Notes JSON | kept | title changed as asked; the rest kept | kept | kept | kept |
| plain and protected custom strings, flags, long JSON, timestamp names | kept | kept | kept | kept | kept |
| attachments, the shared bytes | kept | kept | kept | kept | kept |
| entry tags | kept | kept | kept | kept | kept |
| R1's history | kept | kept, **one item added**: the whole version before the edit, custom data included | kept | kept | kept |
| marker entry | kept | title changed; field kept; one item added | kept | kept | kept |
| G's notes and tags | kept | kept | **kept** (the notes edit was not made, section 6) | kept | kept |
| E1: `Extra`, tag, `e1.txt`, custom data | **kept**; title changed | kept | kept | kept | kept, in N |
| E1's history | **kept, one item added** holding the version before the edit, custom data included | kept | kept | kept | kept |
| deletion records | kept | kept | kept | kept, and **E2's added** when the bin is emptied | kept |
| expiry time of an edited entry or group | **rewritten** to 2999-11-28T23:59:59Z (expiry still off) | **rewritten** the same, R1 and the marker | **G's rewritten** the same | | |
| modification times | E1's moved; **G's and the root's moved** | R1's, the marker's, **KPG's, G's and the root's moved** | **G's and the root's moved** | **E2's moved** by the recycle; G's, the bin's and the root's moved | **E1's and N's moved**; G's and the root's moved |
| previous parent | | | | **recorded** (G) for the recycled E2 | **recorded** (G) for E1 and for N |
| controls: entry, group and database custom data | kept | kept | kept | kept | kept |
| format | 4.1 | 4.1 | 4.1 | 4.1 | 4.1 |

Every KeePassDX file from steps 2 to 5 went back into KeePass **exactly as the phone's file**, in
both directions (section 5.3).

## 5. Back into KeePass (KeePassLib 2.62, Synchronize)

Each phone-saved file synchronized with the copy KeePass would hold: for steps 1 to 3 (and
KeePassDroid's delete, KeePassDX's rename and delete), the base the phone was given; for the
merges, run 1 of each cell with C and with P. **Into KeePass** is KeePass's copy open and the phone's file arriving; **into the phone's
file** the reverse. Every result was saved, read back and diffed against the phone's file, KeePass's
copy and the base.

### 5.1 Keepass2Android

| phone file | into KeePass | into the phone's file |
| --- | --- | --- |
| step 1 save | the phone's file exactly, **the per-key time restored** from KeePass's copy | the same |
| step 2 edit E1 | the phone's file exactly (the new password, the four history items, every carrier), per-key time restored | the same, group access times from the arriving copy |
| step 3 edit R1, marker | the phone's file exactly, per-key time restored | the same, group access times from the arriving copy |
| merge CP with C | the phone's merge result exactly, per-key time restored | the same, but **`M2` becomes C's version** (the arriving copy's) |
| merge CP with P | the phone's merge result exactly, per-key time restored | the same |
| merge PC with C | the phone's merge result exactly, per-key time restored | the same |
| merge PC with P | the phone's merge result exactly, per-key time restored | the same, but **`M2` becomes P's version** |

So nothing Keepass2Android wrote is lost on the way back, and what the phone dropped (the per-key
time) comes back from KeePass's copy, which holds it. The one thing a further synchronization changes
is the equal-time entry `M2`: KeePass, like Keepass2Android, takes the arriving copy whole at equal
times, so its version flips with the direction and one of the two fields is always gone.

### 5.2 KeePassDroid

| phone file | into KeePass | into the phone's file |
| --- | --- | --- |
| step 1 save | carriers kept; **E1's and R1's entry custom data and G's group custom data lost** (equal times: the arriving phone versions win whole); `SettingsChanged` becomes the phone's "now" | carriers kept; **all custom data restored** from KeePass's copy |
| step 2 edit E1 | carriers kept; the new password; **G's custom data lost** (KeePassDroid moved G's time), **R1's lost** (equal times); **E1's current custom data lost**, kept only in E1's history item at +4 h (KeePass's own version, backed up) | carriers kept; **G's and E1's custom data lost**, E1's history item at +4 h is the phone's, without it; R1's restored |
| step 3 edit R1, marker | carriers kept; **G's and E1's custom data lost**; R1's current lost, kept only in its history item | carriers kept; **G's and R1's lost**; E1's restored |
| step 5 delete | E2 gone, its record kept, D1 and D2 kept; custom data lost as in step 1 | E2 gone, record kept; custom data restored |

So every string, attachment, tag, note, history item and deletion record KeePassDroid wrote comes
back into KeePass whole. What it deleted (node custom data) comes back only where KeePassDroid did
not move that node's modification time and only when the phone's file is the one open; once
KeePassDroid has saved an edit inside a group, the group's and the edited entry's custom data are
gone in both directions.

### 5.3 KeePassDX

| phone file | into KeePass | into the phone's file |
| --- | --- | --- |
| step 1 save | the phone's file exactly | the same |
| steps 2, 3 (edits), 4 (rename), 5 (delete) | the phone's file exactly | the same |
| merge CP with C | **X1 back** (KeePassDX removed it without a record, and C still holds it); C's stale record for X2 dropped (X2 is newer); **Y3 back in G** | X1 back; X2's record dropped |
| merge CP with P | P's record for X1 restored; X2's record dropped; Y3's previous parent and location time restored from P | the same, and **`M2` becomes P's version** |
| merge PC with C | the phone's merge result exactly | the same, but **`M2` becomes C's version** |
| merge PC with P | **Y3 back in N** (P's place) | the phone's merge result exactly |

## 6. Not made, and why

- **KeePassDroid steps 4, 6 and 7**: the program offers no group edit, no move and no merge
  (measured on its menus, section 3.1). Nothing to run.
- **Keepass2Android, editing G's notes in step 4**: its group dialog holds a name and an icon only
  (measured: the dialog listed `group_name`, `icon_button`, OK, Cancel).
- **Keepass2Android and KeePassDroid, emptying the recycle bin**: neither offers it (measured on
  their menus); the permanent delete was made from inside the bin instead.
- **Keepass2Android's other merge paths**: "Synchronize database..." in its menu (for a file it
  caches from a remote) and the setting that merges without asking were not run; only the save's
  "Merge changes?" was.
- **KeePassDX's other merge path**: the external-change dialog and "Merge data" (the spike's `ext`)
  was not run here; only "Merge from ...". KeePassDX 3.x was not run.
- **Repeated runs of steps 1 to 6**: one counted run each, by design; only the merges were run three
  times a cell.
- **A KeePassDX attachment collision** (two different bodies whose 32-bit hashes agree): not made;
  the base's two attachments carry the same bytes on purpose.
- **History at its limits**: no entry reached `HistoryMaxItems` (8) in any run, so no program's
  pruning was exercised.
- **Any device but this emulator**, any Android but 11, any image but x86_64.

Runs started and not counted, each repeated from cleared data once its flow was corrected (logs in
`android-evidence\logs\runs\*\not-counted\`): Keepass2Android save (picker), rename and delete
(password keystrokes lost); KeePassDroid step 2 (password typed into a label; the save happened
with nothing changed, section 3.3) and step 3 (title never typed, nothing saved). KeePassDX, 17
attempts: the password lost from the field at unlock (the first attempt at CP merge run 2, and
three attempts at step 4); `typepwck`, which cannot read KeePassDX's password field (the first
attempt at CP merge run 3); taps and long presses its list ignored, on an entry's title or row and in the seconds
after a group opened (four attempts at step 2, one at step 3, one at step 4); the selection's
overflow not yet open when its item was looked for (one at step 4); the pattern
`group_edit_note` matching its container first (one at step 4); and four runs stopped by hand when
the batch was restarted with corrected flows. Every attempt that reached the pull left the file it
was given byte for byte as it was, except KeePassDroid's step 2 attempt, whose file is described in
section 3.3; the runs stopped by hand pulled nothing. The first Keepass2Android save attempt's log
was overwritten by its repeat.

- **KeePassDX, editing G's notes in step 4**: its group dialog holds the notes (measured: the dialog
  listed `group_edit_note` holding G's JSON), but no run of the notes edit ended without a failure
  (the last two: the field pattern, then the password at unlock), so whether a notes edit keeps G's
  tags and the rest is not measured. The rename alone was.

## 7. What survives on a phone

Measured on Keepass2Android 1.15-r6 and KeePassDroid 2.7 as above; "every operation" means every
operation the program offers, then KeePass's Synchronize in both directions.

### Keepass2Android 1.15-r6

**Came through every operation untouched** (save at every KDF tried, edit, accidental edit of a
carrier, group rename, delete, move, its merge, and back into KeePass):

- the dedicated `KPG` group and its flags (searching off, auto-type off);
- entries in it with their identifiers; standard fields; Notes holding JSON;
- custom string fields, plain and protected, with their protection flags, JSON values of
  kilobytes, timestamp-built names;
- attachments, including two entries holding the same bytes;
- entry tags;
- history items, which an edit only adds to (one complete earlier version per edit), keeping the
  three older ones;
- the marker entry's JSON field; E1's own extra field, tag and attachment;
- G's notes and G's tags;
- deletion records already in the file, and every record K2A wrote;
- and, as controls, entry and group custom data and the database's custom data values.

**Did not, and under what:**

- the database custom data's **per-key time** is dropped by every save and every merge (KeePass's
  synchronize puts it back from its own copy);
- the format drops to **KDBX 3.1 with no `SettingsChanged`** in an AES-KDF file with no group tags
  and no node custom data (nothing the measured carriers use was lost there);
- **one entry changed on both sides: the merge keeps one version whole.** With different times the
  other side's field survives only inside a history item; **with equal times it is lost outright**,
  and which side survives depends on which copy was open (a later KeePass synchronize flips it
  again);
- an entry deleted on one side and changed later on the other **comes back** (with the later change);
- a rename moves G's **and the root's** modification times; a recycle or a move records **no
  previous parent**.

### KeePassDroid 2.7

It has **no group edit, no move and no merge** (measured on its menus), so the operations are a
save, an entry edit and a delete.

**Came through every operation untouched** (save at every KDF tried, edit, accidental edit of a
carrier, delete, and back into KeePass): the `KPG` group and its flags; entries and their
identifiers; standard fields and JSON Notes; custom string fields, plain and protected, with their
flags, long JSON values and timestamp names; attachments, including the shared bytes; entry tags;
history items (an edit adds one complete earlier version, the three older ones stay); the marker
entry's field; E1's extra field, tag and attachment; G's notes and G's tags; deletion records; the
database's custom data **with its per-key time**; the format version as given.

**Did not, and under what:**

- **entry custom data and group custom data are deleted by every save** (the controls); after a
  plain save KeePass's synchronize brings them back only with the phone's file open, and after an
  edit, which moves the edited entry's and its groups' modification times, the group's and the
  edited entry's custom data are gone from the result in both directions (the entry's survives
  only inside a history item, and only with KeePass's copy open);
- **`SettingsChanged` is not written**, so the file reads as "settings changed now" wherever it is
  next read;
- an edit, **even one that changes nothing**, makes a new version and **moves every parent group's
  modification time** (KPG, G, the root): a group's notes and tags then go into a KeePass merge as
  the newer group, whole.

### KeePassDX 4.5.8, for comparison (third, not in scope)

Every carrier, the controls included, came through a save, the edits, a rename, a delete with the
bin emptied and the moves, and went back into KeePass exactly. What did not: at a merge, **equal
times keep the open copy's version** and lose the other's field outright; a deletion that arrives
**leaves no record**, so a later synchronize with a copy that still holds the entry brings it back;
a move on one side **follows the arriving copy** at equal times; and every edit **rewrites the
expiry time** of the edited entry or group and moves the parents' modification times.
