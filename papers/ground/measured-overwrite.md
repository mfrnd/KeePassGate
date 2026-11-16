# Measured: when Keepass2Android and KeePassDroid write an older copy over a newer file

Run 2026-11-15 on an Android emulator, for the storage rethink (`EVALUATION-final.md` 2.3 A and
section 8, run 1). The question: a phone program holds a copy it opened earlier, the file on the
phone's storage is meanwhile replaced by a newer one (a synchronizer bringing KeePass's later save),
and the phone program then saves. Exactly when does it write its older copy over the newer file,
losing everything KeePass wrote since, and when does it notice?

**The evidence** is in `spikes\storage\android\recorded\overwrite\`: `base\` the dumps of the
four given files; `runs\<program>\<run>\` each pulled file's dump and its diffs against NEW-diff and
OLD (`runs\not-counted\` for the attempts not counted); `logs\runs\<program>\<run>.log` every step a
run pressed, every dialog's text and buttons, the file's size, time and hash before and after the
replacement and at the pull, the stored preferences where a run logged them; `logs\verdicts*.txt`
the read-back line of every file; `spikes\storage\android\scripts\flows\`, `spikes\storage\android\scripts\` and `spikes\storage\android\probe\overwrite\` as they stood at the end. Machine
paths are written `<work>`. No database is copied.

**Everything in the tables is measured**: a file a program left on the emulator, pulled back and
read with KeePassLib 2.62. What is read from a program's source is said so in prose and marked
**read**; nothing read is in a table. Two of the project's decisions arrived while this ran and shape
it: KeePassDroid is no longer one of the Android programs in scope and is incompatible, as measured, with a
synchronized or merged database, so its part was cut to the baseline and its lock (section 3); and
the weight moved to Keepass2Android, whose own synchronization is measured in the companion report
`measured-k2a-synchronize.md`.

## 1. How the runs were made

### 1.1 Programs and emulator

The APKs of `measured-android.md` 1.1, installed on the emulator `kpg30` by that measurement and
held to their pins again before the first run: `verify-apks.sh` (each file's SHA-256, a verifying
signature, the one pinned signer) passed for all three, and the APK each installed package runs
from hashed to the pinned value: Keepass2Android 1.15-r6 `5c446a4f...`, KeePassDroid 2.7
`513b2555...`, KeePassDX 4.5.8 `8aaac6a0...` (`logs\verify.txt`; `spikes\storage\android\scripts\check.sh` prints the
installed files' hashes). Android 11, API 30, x86_64, headless, as in
`measured-android.md` 1.2; started without wiping, and every run clears the program's data
(`pm clear`) first. The device's time zone is Europe/Berlin, so the logs print file times at
+0100. No window was shown on the desktop.

### 1.2 The databases

Made by the probe of `measured-android.md` 1.3 over **KeePassLib 2.62**, with one command added
(`makeow`, `spikes\storage\android\probe\overwrite\Store.cs`), invented content, the invented password of that report, no window:

| file | what it is | size |
| --- | --- | --- |
| `OLD` | the main base `S-argon` of `measured-android.md` 1.3 (Argon2d, KDBX 4.1), X1's notes holding 1500 random characters | 9301 bytes |
| `NEW-diff` | OLD with three changes standing for what KeePass wrote later, all at 2026-11-01T18:00:00Z: **RV** "KPG revocation", a new entry in the plugin's group `KPG` with the custom string `KPG.Revoked.c2` (a revocation record); **M1**'s custom string `KPG.base` changed from `{"v":0}` to `{"v":1,"by":"KeePass"}`, the old version put into history first as KeePass's dialog does; **X1** deleted, with its deletion record. RV's notes hold 4000 random characters | 11621 bytes |
| `NEW-same` | the same three changes with RV's notes cut to 1309 characters, so that the file is **exactly OLD's length** (found by search; its dump differs from NEW-diff's in that one line) | 9301 bytes |
| `NEW-ident` | OLD's bytes exactly: a replacement that changes the file's time and nothing else | 9301 bytes |

The read-back (`verdict.sh`) dumps a pulled file with the probe and says for NEW's three changes
whether they are there: **kept** is RV present, M1 at `v1`, X1 gone with its record; **lost** is RV
absent, M1 at `v0`, X1 present with no record. It names the entry the phone added and says when the
bytes are exactly one of the given files. Each dump is diffed against NEW-diff's and OLD's
(`runs\<program>\<run>\*.vs-NEW.diff.txt`, `*.vs-OLD.diff.txt`).

### 1.3 The replacement

`replace.sh` stands for a synchronizer bringing KeePass's later save: `adb push` of the arriving
copy over the **same path** in `/sdcard/Download`, then the file's modification time set with the
device's own `touch`. The file the program opens is given the device clock less 600 s when it is
pushed. **Later** is the device clock at the replacement (at least 600 s past the opened file's
time); **equal** is the opened file's time exactly, nanoseconds included (copied with `touch -r`
through a reference file taken just before the push); **earlier** is the opened file's time less
3600 s. Every replacement logs the file's size, time and SHA-256 before and after, and the pull
logs them again; the logs show every time came out as the case names. **On screen**: the
replacement made with the program in front. **In the background**: HOME, the replacement, then the
program brought back by its launcher.

### 1.4 The flows and what was counted

`run-ow.sh` runs one case from cleared data: the program's data cleared, OLD pushed alone, a flow
run by `k.py` (the driver of `measured-android.md`, given commands to run the replacement on this
side, to log a dialog's title, message and buttons, to scroll a settings list and to read a
checkbox), and every file left in `Download` pulled. The flows are in `spikes\storage\android\scripts\flows\` (`k2a-ow-*.flow`,
`kpd-ow-*.flow`, `k2a-ans-*.flow` for the answers). Counted: runs whose flow ended without a
failure. Not counted, each repeated with its flow corrected (`logs\not-counted\`, read-backs in
`runs\not-counted\`), and every one of them leaving the file as NEW-diff byte for byte:

- the first Keepass2Android "Cancel" run, whose flow expected the group view after Cancel and found
  the entry editor still open;
- two attempts at section 2.5 whose press on the setting landed on no line (the checkbox stayed
  checked; the dialog came at the save, as with the setting on);
- two attempts at section 2.5 that did turn the setting off, whose flow then expected no question
  and stopped at the dialog it got (section 2.5 reports what they showed);
- the first two attempts at section 2.6, whose flow did not expect the program to leave for the
  home screen after QuickUnlock.

Counted: **Keepass2Android 32 runs** (21 in section 2.1, 7 in 2.2 to 2.4, 2 in 2.5 and
2 in 2.6), **KeePassDroid 4 runs**. KeePassDX was not run (section 4).

## 2. Keepass2Android 1.15-r6

### 2.1 The overwrite, and what decides whether it notices

The file opened through the system file picker (the program's list of storage types offers no
local file on this device: System file picker, Dropbox, Google Drive, OneDrive, SFTP, FTP, WebDAV,
ownCloud, Nextcloud, Samba, pCloud, MEGA; measured on its screen), unlocked, the replacement made,
then an entry `phone-<run>` added at the root and saved by the editor's Save.

| case | replacement | counted runs | what the program showed | the file afterwards | NEW's changes |
| --- | --- | --- | --- | --- | --- |
| 1a, answer **"No, overwrite"** | NEW-diff, later, on screen | 3 | at Save, before anything was written: the dialog below | OLD with the phone's entry; the database's per-key time dropped (the only other line, as in every K2A save) | **lost**, all three, in every run |
| 1b, answer **"Yes, merge"** | NEW-diff, later, on screen | 3 | the same dialog | NEW with the phone's entry; per-key time dropped | **kept**, all three, in every run |
| 1c, answer **"Cancel"** | NEW-diff, later, on screen | 1 | the same dialog; then the entry editor stayed open with the entry unsaved; its own cancel took it to the group view, writing nothing | NEW-diff byte for byte; the phone's entry nowhere | kept; the phone's entry not saved |
| 2a | NEW-same (OLD's length to the byte), **equal** time | 3 | the same dialog | answered "No, overwrite": OLD with the phone's entry | lost |
| 2b | NEW-same, **earlier** by an hour | 1 | the same dialog | "No, overwrite": the same | lost |
| 2c | NEW-diff, earlier by an hour | 1 | the same dialog | "No, overwrite": the same | lost |
| 2d | NEW-diff, equal time | 1 | the same dialog | "No, overwrite": the same | lost |
| 2e | NEW-same, later | 1 | the same dialog | "No, overwrite": the same | lost |
| 2f, the control | **OLD's own bytes**, later time | 3 | **no dialog**; the save went straight through | OLD with the phone's entry | nothing to lose: the arriving file held no change |
| 2g | NEW-diff, later, **in the background** | 3 | nothing on return (the group view still listed OLD's counts, KPG 4 entries, G 9); the dialog at Save | "No, overwrite": OLD with the phone's entry | lost |
| 2h | NEW-same, equal time, in the background | 1 | nothing on return; the dialog at Save | "No, overwrite": the same | lost |

In every run whose answer was "No, overwrite" the file read back as **OLD with the phone's entry
and nothing else** (no line differs from OLD's dump but the new entry's, the two counts it changes
and the per-key time); in every "Yes, merge" run, as **NEW with the phone's entry and nothing
else**. The dialog, in all 18 runs of this table that showed it (every run but the three of the
control 2f), exactly:

> **Merge changes?**
> The database file was modified externally. Do you want to load and merge the changes before
> saving? Select No if you want to overwrite the external changes.
> [Cancel] [No, overwrite] [Yes, merge]

**What it compares: the file's content, and only that.** A replacement of the same length with the
same time to the nanosecond was caught (2a, three runs), and so were earlier times (2b, 2c); a
replacement that changed the time and not one byte was not (2f, three runs). **Read**, consistent
with this: the save hashes the file as it is now and compares it with the hash taken when it was
loaded (`src/Kp2aBusinessLogic/database/edit/SaveDB.cs:149-155`, `:365-415`); for a file reached
through the system picker the fast check by time answers false
(`src/Kp2aBusinessLogic/Io/AndroidContentStorage.cs:61-64`), so the hash is the whole test. **When
it compares: at the save, not before.** Coming back from the background to a changed file showed
nothing (2g, 2h); the group view went on showing the copy in memory until the save asked.

### 2.2 The window: how the program was left

| case | counted runs | sequence | what the program showed | the file afterwards | NEW's changes |
| --- | --- | --- | --- | --- | --- |
| 3a, open and unlocked | (2.1) | as 2.1 | the dialog at Save | by the answer | by the answer |
| 3b, its own lock, on a device with no screen lock | 1 | open; replacement (NEW-diff, later) while open; the lock button; the lock screen then says "Password-based QuickUnlock not available" ("QuickUnlock using a part of your password is blocked because screen lock is not activated on your device"), offering **Close database**, which was pressed; the program started again; the password screen for the same file; unlocked; entry added and saved | after unlocking, KPG listed **KPG revocation** (NEW's); **no dialog** at Save | NEW with the phone's entry | **kept** |
| 3c, closed and reopened | 3 | open; lock; Close database; replacement (NEW-diff, later) while closed; started again, unlocked, entry added and saved | KPG listed KPG revocation; no dialog at Save | NEW with the phone's entry | **kept**, in every run |
| 3d, its own lock with **QuickUnlock** (a screen lock set) | see 2.6 | | | | |

So unlocking with the password after "Close database" reads the file again: what the program then
holds is NEW, and a save keeps it. **Read**: with QuickUnlock enabled (its default, a checkbox on
the password screen, checked) the lock keeps the database in memory
(`src/keepass2android-app/app/App.cs:128-147`); only when QuickUnlock cannot be used is it unloaded
(`:148-158`). On this device QuickUnlock was refused because no screen lock was set, so the lock
screen offered only Close database. Section 2.5 has the lock with a screen lock set.

### 2.3 Nothing changed on the phone

| case | counted runs | sequence | the file afterwards |
| --- | --- | --- | --- |
| 5 | 2 | open; replacement (NEW-diff, later); then only looking: G, E1 opened and shown, back, back, KPG listed, back; lock; Close database | **NEW-diff byte for byte, with the replacement's own time**: nothing was written |

Opening, browsing, viewing an entry, locking and closing write nothing. Only a save writes.

### 2.4 Its menu's "Synchronize database..." on a file from the system picker

| case | counted runs | sequence | what the program showed | the file afterwards | NEW's changes |
| --- | --- | --- | --- | --- | --- |
| 4 | 1 | open; replacement (NEW-diff, later); More options, **Synchronize database...**; then an entry added and saved | no dialog; after it the group view showed **NEW's counts** (KPG 5 entries, G 8); the file was not written by it (same bytes and time as the replacement); then **no dialog at Save** | NEW with the phone's entry | **kept** |

So on a picked file "Synchronize database..." reads the file's changes into what the program holds
and writes nothing when there is nothing of its own to add; the save after it has nothing left to
ask about. Its own synchronization of a remote file, with changes on both sides, is the subject of
`measured-k2a-synchronize.md`.

### 2.5 Its settings: "Check for modifications" turned off

The setting is on its screen under More options, Settings, App, **File handling**: "Check for
modifications", "Check whether the file was modified externally before saving changes", checked by
default. The flow opens the screen, scrolls to the line, presses it, reads the checkbox before and
after, and logs the program's stored preferences (read as root from its own SharedPreferences file)
before the screen, on it, and after the press.

| case | counted runs | sequence | stored preference | what the program showed | the file afterwards | NEW's changes |
| --- | --- | --- | --- | --- | --- | --- |
| 6a, check turned off | 1 | open; the setting pressed (checkbox true, then false); back; replacement (NEW-diff, later); entry added and saved; "No, overwrite" | `CheckForFileChangesOnSave` false | **the "Merge changes?" dialog all the same** | OLD with the phone's entry | lost (by the answer) |
| 6b, check turned off, then the database closed and opened again | 1 | as 6a, with lock, Close database, start, unlock between the press and the replacement | false, before and after the reopen | **the dialog all the same** | OLD with the phone's entry | lost (by the answer) |

Two earlier attempts of 6a ended at the dialog (their flow expected no question and stopped there,
so they are not counted); both stored false and both showed the dialog, the file left as NEW-diff.
So **on this release, turning "Check for modifications" off did not stop the question for a file
from the system picker**, in all four runs that stored it false (6a, 6b and the two attempts).
**This contradicts the read**: at `v1.15-r6`
the save skips the check when that preference is false (`SaveDB.cs:138-146`, the preference read
at `app/App.cs:512-513` with the key the screen stores). What makes the release ask anyway was not
found. The setting therefore neither helps nor hurts here; for a remote file see
`measured-k2a-synchronize.md`.

**A side effect of merely opening that screen**, measured in all six runs that showed it (6a, 6b,
the two attempts above and the two whose press missed, section 1.4): before it,
the program had stored none of these preferences; once the File handling screen had been shown,
it had stored `UseOfflineCache` true, **`SyncOfflineCacheInBackground_key` true** ("Synchronize in
background"), `CheckForFileChangesOnSave` true, `UseFileTransactions` true and
`AlwaysMergeOnConflict` false, with nothing pressed. The program's own code reads "Synchronize in
background" as false when nothing is stored (read: `app/App.cs:1331-1336`), and the screen's
declared default is true (`Resources/xml/pref_app_file_handling.xml:16-22`): **showing the screen
once turns it on**. For a file from the system picker it changed nothing measured here (the dialog
came in all six runs that had shown the screen); what it does to a remote file is in
`measured-k2a-synchronize.md`.

### 2.6 Its own lock with QuickUnlock

With a screen lock PIN set on the device for these runs (`spikes\storage\android\scripts\pin.sh`, taken away again after
them), which is what makes the program offer QuickUnlock (QuickUnlock itself is on by default:
"Enable QuickUnlock" checked on the password screen).

| case | counted runs | sequence | what the program showed | the file afterwards | NEW's changes |
| --- | --- | --- | --- | --- | --- |
| 3d | 2 | open, unlock; the lock button; the QuickUnlock screen ("Enter last 3 characters of your password:", **QuickUnlock!**, Close database); replacement (NEW-diff, later) while locked; the password's last three characters, QuickUnlock!; entry added and saved; "No, overwrite" | after QuickUnlock! the program left for the home screen and was brought back by its launcher; then the group view showed **OLD's counts** (KPG 4 entries, G 9: the copy in memory, not the file); KPG did not list KPG revocation; at Save **the "Merge changes?" dialog** | answered "No, overwrite": OLD with the phone's entry | lost (by the answer) |

So a QuickUnlock keeps the copy the program held before the lock and reads nothing from the file;
the save then asks, as in 2.1. Only Close database and a full unlock (2.2) read the file afresh.

## 3. KeePassDroid 2.7

Cut to the baseline and its lock when KeePassDroid was found incompatible with a synchronized or
merged database; what is here is the evidence for that finding. The file opened through
its Open button (the system file picker; on Android 11 the program offers no field for a path:
read, `res/layout-v30/file_selection_filename.xml`; on its screen only the Open button), unlocked,
the replacement made, an entry added at the root by ADD ENTRY and saved by the editor's SAVE.

| case | replacement | counted runs | what the program showed | the file afterwards | NEW's changes |
| --- | --- | --- | --- | --- | --- |
| 1, the baseline | NEW-diff, later, on screen | 3 | **nothing**: no dialog at the replacement, none at SAVE; the new entry listed at the root | OLD with the phone's entry; besides, as in every KeePassDroid save (`measured-android.md` 3.2), entry and group custom data deleted and `SettingsChanged` read back as the moment of reading | **lost**, all three, in all three runs |
| 3b, its own lock | NEW-diff, later, while open; then **Lock Database**; unlocked again with the password | 1 | the lock left the password screen for the same file; after unlocking, KPG listed **KPG revocation**; nothing at SAVE | NEW with the phone's entry (custom data deleted as above) | **kept** |

**Read**, consistent with both: its save writes with no check of any kind
(`app/src/main/java/com/keepassdroid/database/edit/SaveDB.java:54-75`), and its lock clears the
loaded database (`fragments/PasswordFragment.java:398-403`), every unlock loading afresh
(`:740-756`). Its application timeout (five minutes by default) goes through the same lock
(`timeout/TimeoutHelper.java`; not run). Cases 2 to 5 were not run, since KeePassDroid is incompatible.

## 4. Not run, and why

- **KeePassDX**: only if both programs were complete with time left; the time went to
  Keepass2Android's synchronization instead. What is known is the spike's: a replacement whose time
  did not move on by ten seconds was saved over without a dialog (`spikes/revocation-merge`
  `RESULTS.md` 6, keepPC; read: `DatabaseTaskNotificationService.kt:219` compares the time only).
- **KeePassDroid cases 2 to 5**: cut when KeePassDroid was found incompatible (section 3).
- **Keepass2Android, a file reached by a path** (`file://`): its storage list offers none on this
  device, so a phone cannot reach a file that way except through another program's
  intent. **Read**: such a file would be checked by its time first (more than one second later)
  and by its hash when the time has not moved (`Io/BuiltInFileStorage.cs:89-104`), and would be
  offered "Reload file?" when the program comes back to the screen (`app/App.cs:521-536`).
- **Keepass2Android's application timeout**: not run; **read**, it locks through the same
  `App.Lock` as the button.
- **KeePass holding the database open over the overwritten file** (does its next save offer
  Synchronize, and does it put the entries back?): it needs KeePass's window, which this run does
  not start.
- **Any device but this emulator**, any Android but 11.

## 5. What the operator can rely on

### Keepass2Android 1.15-r6

**The sequence that loses KeePass's later changes**: the database is open in Keepass2Android,
unlocked or locked by QuickUnlock (which keeps the copy it held, 2.6); the file changes underneath;
a change is saved on the phone; and the answer to **"Merge changes?"** is **"No, overwrite"**. Then
the file is the phone's copy plus the phone's change, and everything KeePass wrote since the phone
loaded the file is gone, without a trace in the file (18 runs of 18 with that answer). No other
sequence measured here lost anything: the question came at every save over a changed file, whatever
its time and its length, after a return from the background, after a QuickUnlock, and with "Check
for modifications" turned off (2.5).

**Safe, measured**:

- **"Yes, merge"**: the file afterwards holds NEW's changes and the phone's (3 of 3).
- **"Cancel"**: nothing is written; the phone's change stays unsaved in the editor.
- **Doing nothing but look**: opening, browsing, viewing, locking and closing write nothing (2 of 2).
- **"Close database" and a full unlock with the password**, before or after the file changes: the
  program reads the file afresh, and a save then keeps NEW (4 of 4).
- **"Synchronize database..."** before an edit, on a picked file: the program takes up the file's
  changes, and the following save asks nothing and keeps them (1 of 1).
- The question does not depend on times or sizes: any change of content is caught, whatever the
  file's time says (equal, earlier) and whatever its length. A replacement that changes nothing is,
  rightly, not asked about.

**The shortest operating rule**: *Never answer "No, overwrite": when Keepass2Android asks "Merge
changes?", answer "Yes, merge".* (Cancel is safe too, and leaves the edit unsaved.) No setting
measured here removes the risk: "Check for modifications" off still asks; "Always merge on
conflict" on removes the question by always merging (measured for a remote file,
`measured-k2a-synchronize.md` case 6), which makes the safe answer automatic.

### KeePassDroid: incompatible with a synchronized or merged database (the evidence)

KeePassDroid 2.7 saves over a file that changed underneath it **without any question**, every
time (3 of 3 runs): the file afterwards is its older copy plus its own change, and every change
KeePass wrote meanwhile is gone, with nothing shown on the phone or in the file. It compares
nothing before saving (read: no check exists). It also deletes every entry's and group's custom
data at every save (`measured-android.md` 3.2) and has no merge of its own. Unlocking after its own
lock does read the file afresh (1 run), which narrows the window to the time a database stays
unlocked, but any edit while a newer file arrives loses that file's changes. It is incompatible,
as measured, for a synchronized or merged database.
