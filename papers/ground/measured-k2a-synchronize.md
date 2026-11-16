# Measured: Keepass2Android's synchronization of a remote database

Run 2026-11-15 and 16 on an Android emulator, after the project's decision of 2026-11-15 that
there is to be **one synchronized `.kdbx` with several
writers**. The question: when Keepass2Android works on a database that another device (the desktop,
KeePass with the plugin) changes meanwhile, what does the file on the server hold afterwards, for
the plugin's own records as the evaluation recommends storing them (`EVALUATION-final.md` 6) and
for ordinary passwords?

**Everything in the tables is measured**: the server's file after the run, and Keepass2Android's
cached copy pulled as root from the emulator, each read with KeePassLib 2.62. What is read from
its source (`v1.15-r6`, commit `bbcf3787c75b`) is said so in prose and marked **read**.

## 1. How the runs were made

### 1.1 Program, emulator, server

Keepass2Android 1.15-r6 as in `measured-overwrite.md` 1.1 (the same pinned APK, the same emulator).
The server is **WebDAV over HTTP**: rclone v1.68.2 for linux-amd64, fetched from
`downloads.rclone.org` into the measurement's own directory and held to the `SHA256SUMS` published
beside it (`0e6fa18051e67fc600d803a2dcb10ddedb092247fc6eee61be97f64ec080a13c`, matched), run inside
WSL as `rclone serve webdav <dir> --addr 0.0.0.0:8080 --user kpg --pass <invented>
--dir-cache-time 0s --poll-interval 0` (`spikes\storage\android\scripts\server.sh`). The emulator reaches it as
`http://10.0.2.2:8080/`. Directory listings are not cached by the server, because the runs change
its files on disk: a first attempt with rclone's default five-minute listing cache showed the
program a stale listing and is not counted.

**How the file was opened** (`spikes\storage\android\scripts\flows\k2a-sy-open.flow`, every run from cleared data): Open file...,
storage type **"HTTP (WebDav)"**, the dialog "Enter WebDav login data:" filled with
`10.0.2.2:8080/` (the program adds `http://`), the user and the password, OK; the program's own file
browser then listed the server's directory ("Root", the file with its size and time), the file
pressed; the password screen named it `http://10.0.2.2:8080/kpg-<run>.kdbx`; unlocked with the
password. What the program then stored, found on the device: the **cache**
`no_backup/OfflineCache/<SHA-256 of the URL>.cache`, beside it `.cache.version` and
`.cache.baseversion` (each the SHA-256 of a file, in hex), and a **local backup copy**
`no_backup/http10.0.2.28080kpg-<run>.kdbx` (Settings, File handling, "Local backups", "Create a local
backup copy after successfully loading a database", on by default). The cache and the local backup
are pulled at the end of every run.

**Cleartext HTTP.** The program refuses `http://` with "Cannot connect to file provider service
Cleartext HTTP is disabled by user preference. Go to app settings/File handling if you really want to
use HTTP." (measured). So every run starts from a written preferences file (`spikes\storage\android\scripts\setprefs.sh`,
as root after `pm clear`, before the program starts), in one of two states:

- **A** (the main runs): only "Allow cleartext HTTP" (`permit_cleartext_traffic`) stored true; every
  other preference unstored, so the program reads its code defaults: "Synchronize in background"
  false, "Check for modifications" true, "Always merge on conflict" false, "Database caching" true.
  This is what an operator of an HTTPS server has who never opened the File handling screen.
- **B**: what showing the File handling screen stores, measured in `measured-overwrite.md` 2.5,
  with cleartext on: "Database caching", **"Synchronize in background"**, "Check for
  modifications" and file transactions true, "Always merge on conflict" false. Any operator who has
  once opened that screen, which an HTTP user must, is in B.

### 1.2 The two copies

Made by the probe's `makesync` and `newsync` commands (`spikes\storage\android\probe\overwrite\Store.cs`), KeePassLib 2.62, Argon2d,
invented content, the invented password:

| item | SOLD (the server's file when the phone opens it) | SNEW (the desktop's later save) | the phone's own changes |
| --- | --- | --- | --- |
| group `KeePassGate`, searching and auto-type off | `KG1` "KPG grant 1" (`KPG.Grant`), `KG2` "KPG admission 1" (`KPG.Admission`, protected `KPG.Secret`) | the same, and **`KG3`** "KPG revocation" (`KPG.Revoked.c2`), a write-once entry standing for a revocation | none |
| database custom data | `KPG.Profiles` | and **`KPG.NewKey`**, a key SOLD lacks | none |
| deletion records | `D1` | and **`DZ`**, naming no entry in either copy, and **`X1`**, an entry SOLD holds unchanged | none |
| `P1` password | `p1-old` | `p1-desktop` | none |
| `P2` password | `p2-old` | unchanged | `p2-old-phone` |
| `Q1` password, both sides, **the desktop's older** | `q1-old` | `q1-desktop` at 2026-11-01T18:00Z | `q1-old-phone` at the phone's clock |
| `Q2` password, both sides, **the desktop's newer** | `q2-old` | `q2-desktop` one hour after the phone's edit | `q2-old-phone` |
| `Q3` password, both sides, **equal times** | `q3-old` | `q3-desktop` at the phone's edit's time exactly | `q3-old-phone` |
| `NP`, a new ordinary entry | absent | absent | `phone-new` at the root |
| `K` | untouched | untouched | untouched |

Every desktop change is made as KeePass's dialog makes one (the old version into history first).
Where the phone changes nothing, SNEW is `SNEW-fixed` (Q2 at 2027-01-01, Q3 at 18:00Z). Where both
sides change, the run builds its own SNEW after the phone's edits: `mksnew.sh` pulls the phone's
cached copy, reads Q2's and Q3's modification times from it, and writes SNEW with Q2 an hour later
and Q3 at the same second; then puts it on the server, written beside the served file and renamed
over it, as a synchronizer does.

The read-back (`sverdict.sh`) prints, for the server's file and for the cache: KG1, KG2, **KG3**,
**NewKey**, **DZ**, **X1** (there or gone, with or without its record), each password's current
value and the other sides' values found in its history, and **NP**.

### 1.3 Runs, evidence, what was counted

`run-sy.sh` runs one case from cleared data: the program's data cleared, the preferences written,
the server's directory emptied and SOLD put there, the flow run by `k.py`, then the server's file
copied and the cache and the local backup pulled. During a flow, `srv-put.sh` changes the server's
file, `server.sh` stops and starts the server, `events.sh` records the accessibility events of the
next seconds (a toast is only there; none carried text in any run), and `dialog` logs a dialog's
title, message and buttons. Counted: runs whose flow ended without a failure, **21 runs** (12 in
section 2, 6 in section 3, 3 in section 4). Not counted, each case repeated with its flow corrected,
kept in `runs\sync-not-counted\` and `logs\sync-not-counted\`: the first case 1, which met the
server's stale listing; four runs stopped by hand part way when a batch was stopped to correct a
flow (cases 2, 3c, 3c and 4); two attempts at case 3c, one whose flow looked for a checkbox the menu
item does not have, one that looked for "Work on internal cache only" in the mode where the menu
shows "Synchronize cache copy with source" instead; the first case 4, where the question came at
"Synchronize cache copy with source" and the flow was not ready to answer it (its files: the server
still SNEW, the cache the phone's copy); and the first case 5 in B, whose flow expected no question
(section 3). The exploration that worked out the opening flow (`x-webdav`) is kept beside them.

**The evidence** is in `spikes\storage\android\recorded\k2a-synchronize\`: `base\` the dumps
of SOLD and SNEW-fixed; `runs\<run>\` the dumps of the server's file, the cache, the local backup,
the phone's cached copy just before a synchronize (`phone-before-sync`), the run's SNEW, and
KeePass's own Synchronize of the same two (`keepass-sync\`), each with its diffs against SOLD and
SNEW; `logs\sync\<run>.log` every step, dialog, event, preference and file hash;
`logs\sverdicts.txt` the read-back line of every file; `server\` the rclone log, the fetch script and
the published `SHA256SUMS`; `spikes\storage\android\scripts\flows\`, `spikes\storage\android\scripts\`, `spikes\storage\android\probe\overwrite\`. Machine paths are written `<work>`. No
database is copied.

## 2. "Synchronize database..." in each state

Preferences A unless said. Every run opened SOLD from the server, as in 1.1. "Server" is the file
on the server afterwards, "cache" the program's cached copy afterwards, both read back.

| case | counted runs | sequence | what the program showed | server afterwards | cache afterwards |
| --- | --- | --- | --- | --- | --- |
| 1, nothing changed | 1 | open; look; More options, **Synchronize database...**; look | no dialog, no toast (the 20 s of accessibility events after the press hold only the window's own) | SOLD, byte for byte, its time unchanged | SOLD |
| 2, only the server changed | 2 | open; SNEW-fixed put on the server; Synchronize database...; look | no dialog, no toast; afterwards **KeePassGate listed KPG revocation** (the server's) | SNEW-fixed byte for byte, unchanged | **SOLD**, and its `.version` and `.baseversion` still SOLD's hash; the local backup copy was rewritten as SNEW |
| 3a, only the phone changed, server reachable | 1 | open; P2's password edited and saved; Synchronize database... | no dialog at the save, none at Synchronize | SOLD with P2 `p2-old-phone`, **written by the save itself** (before Synchronize was pressed); Synchronize changed nothing | the same file |
| 3b, only the phone changed, **server stopped** | 1 | open; server stopped; P2 edited and saved; server started; Synchronize database... | no dialog and no toast at the save (only a notification "P2: Entry is available through KP2A Keyboard"); none at Synchronize | SOLD while stopped; after Synchronize, **the phone's file** (P2 `p2-old-phone`) | the same |
| 3c, only the phone changed, **"Work on internal cache only"** | 2 | open; More options, Work on internal cache only; P2 edited and saved; More options, **"Synchronize cache copy with source"** (in that mode the menu shows this item in place of both "Synchronize database..." and "Work on internal cache only"); Synchronize database... | no dialog; nothing at the save | SOLD after the save (nothing sent); after "Synchronize cache copy with source", the phone's file | the same |
| 4, **both changed**, the phone's changes made in "Work on internal cache only", answer **"Yes, merge"** | 2 | open; Work on internal cache only; the passwords of P2, Q1, Q2 and Q3 edited and saved, and `phone-new` added, each saved with no question (to the cache only: the server still SOLD); this run's SNEW put on the server; More options, **"Synchronize cache copy with source"**; then Synchronize database...; look | at "Synchronize cache copy with source": **the "Merge changes?" dialog** (the text of `measured-overwrite.md` 2.1, buttons Cancel, No, overwrite, Yes, merge); answered Yes, merge; nothing at the Synchronize database... after it | **a merge**: KG3, NewKey, DZ and X1's deletion all there; P1 the desktop's, P2 the phone's, `phone-new` there; Q1 the phone's (newer) with the desktop's in its history; Q2 the desktop's (newer) with the phone's in its history; **Q3 (equal times) the phone's, with the desktop's in its history**; KDBX 4.0, per-key times dropped | the same file |
| 4, answer **"No, overwrite"** | 1 | as above, the dialog answered No, overwrite | the same dialog at "Synchronize cache copy with source"; nothing at Synchronize database... after it | **the phone's copy whole**: no line differs from the phone's cached copy before the synchronize but access times; **KG3, NewKey, DZ, X1's deletion, P1's, Q1's, Q2's and Q3's desktop values all gone**, from the current entries and from every history | the same file |
| 4b, both changed, the phone's changes saved **with the server stopped**, answer "Yes, merge" | 1 | open; server stopped; the same four password edits and `phone-new`, each saved with no question and no warning (only the notification "<entry>: Entry is available through KP2A Keyboard"), to the cache only; SNEW put on the server; server started; **Synchronize database...** | **the "Merge changes?" dialog** at Synchronize database...; answered Yes, merge | the same merge as case 4, line for line in what it holds: every desktop item and every phone item, Q1 and Q2 by time with the other in history, Q3 the phone's with the desktop's in history | the same file |
| 4, **preferences B** ("Synchronize in background" on), "Yes, merge" | 1 | as case 4 | the same dialog at "Synchronize cache copy with source" | the same merge as case 4 | the same file |

In every case 4 and 4b run, the cache read back as the same file as the server.

**What "Synchronize" is, then.** With nothing changed on the phone, it reads the server's file into
what the program holds and writes nothing anywhere (1, 2); with nothing changed on the server, it
uploads the cache (3b, 3c); with both changed, it asks the same **"Merge changes?"** question a
save asks, and the answer decides: **"Yes, merge"** made a real merge (4, 4b), **"No, overwrite"**
put the phone's copy over the desktop's (4). **Read**, consistent with this:
`src/Kp2aBusinessLogic/database/SynchronizeCachedDatabase.cs:68-160` downloads the server's file
and compares its hash with the cache's base version; server unchanged and the cache changed:
upload; server changed and no local change: reload from the downloaded data; both changed: a save
(`SaveDb`) with the downloaded data as the original file, which takes the path of
`SaveDB.cs:149-198` straight to the question (the check-on-save setting is consulted only when no
original file is handed in, `:138-146`). In the cache-only mode the menu offers "Synchronize cache
copy with source" in place of both "Synchronize database..." and "Work on internal cache only",
and it behaved the same (3c, 4).

**Is it a real merge?** The probe ran KeePass 2.62's own Synchronize on the same two copies (the
phone's cached copy just before the synchronize, and the run's SNEW), in both directions
(`spikes\storage\android\scripts\expect.sh`, `runs\<run>\keepass-sync\`). Keepass2Android's result held everything
KeePass's held: the desktop's write-once entry, its database custom data key, both its deletion
records (the one naming no entry kept in the list), its password on P1, the phone's on P2 and its
new entry, and for Q1 and Q2 the newer value current with the other in history. It differed in
three ways only: **at equal times (Q3) KeePass keeps one side whole and the other nowhere** (the
arriving copy's, whichever direction), while Keepass2Android kept the phone's current and **the
desktop's in history**, in every merge run; the file is KDBX 4.0 with the database custom data's
per-key times dropped (as every Keepass2Android save, `measured-android.md` 2.1); and the usage
count. So Keepass2Android's merge lost nothing that KeePass's own Synchronize keeps, and kept one
thing KeePass's would drop.

## 3. A plain save while the server's file changed

No Synchronize pressed: open; SNEW-fixed put on the server (the desktop's later save); P2's
password edited and saved.

| case | prefs | counted runs | what the program showed | server afterwards | cache afterwards |
| --- | --- | --- | --- | --- | --- |
| 5, answer **"Yes, merge"** | A | 2 | at Save: **"Merge changes?"**, the same text and buttons | **a merge**: KG3, NewKey, DZ, X1's deletion, P1, Q1, Q2, Q3 the desktop's, P2 the phone's | the same file |
| 5, answer **"No, overwrite"** | A | 1 | the same dialog | **SOLD with the phone's P2 and nothing of the desktop's**: KG3, NewKey, DZ, X1's deletion and the four desktop passwords gone | the same file |
| 5, **preferences B** ("Synchronize in background" on), answer "Yes, merge" | B | 1 | **the same dialog**, raised a few seconds after the save; in a first attempt (not counted, its flow expected no question) the cache already held the phone's P2 while the dialog stood and the server still held SNEW, so the save wrote the cache first and the question came from the synchronization that followed it | a merge, as above | the same file |
| 6, **"Always merge on conflict"** turned on on its screen (More options, Settings, App, File handling; checkbox false, pressed, true; stored `AlwaysMergeOnConflict` true) | A, and then what showing the screen stores (B) | 1 | **no dialog** at Save | **a merge**, the same as the "Yes, merge" rows | the same file |
| 7, **"Check for modifications"** turned off on the same screen (checkbox true, pressed, false; stored `CheckForFileChangesOnSave` false); answer "No, overwrite" | A, then B by the screen | 1 | **the "Merge changes?" dialog all the same** | by the answer: SOLD with the phone's P2, the desktop's items gone | the same file |

## 4. Opening the database again after the server's file changed

| case | counted runs | sequence | what the program showed | what it loaded | server afterwards |
| --- | --- | --- | --- | --- | --- |
| 8, closed and reopened | 2 | open; lock; **Close database** (the lock screen offers it; with no screen lock set QuickUnlock is refused, `measured-overwrite.md` 2.2); SNEW-fixed put on the server; the program started again, the password screen for the same URL, unlocked; look; Synchronize database...; look | no dialog at the unlock or at Synchronize | **the server's file**: KeePassGate listed KPG revocation, and the cache read back right after the unlock as **SNEW-fixed** byte for byte, its version and base version SNEW's hash | SNEW-fixed, unchanged |
| 9, **QuickUnlock** (a screen lock PIN set on the device for this run, so the program offers it) | 1 | open; the lock button; the QuickUnlock screen ("Enter last 3 characters of your password:"); SNEW-fixed put on the server; the last three characters, QuickUnlock!; look; P2 edited and saved | after QuickUnlock the program left for the home screen and was brought back by its launcher (as in `measured-overwrite.md` 2.6); then **the copy held in memory**: KeePassGate without KPG revocation, X1 still listed; at Save, **"Merge changes?"**, answered Yes, merge | **the copy in memory**, not the server's file; nothing was read at the unlock | a merge, as in case 5 |

## 5. What the desktop decided, item by item, in the server's file

Every run in which both sides had changed something, read from the server's file afterwards.
"yes" is the desktop's item there as SNEW holds it; "history" is the desktop's value kept only as
an earlier version of the entry; "no" is gone without a trace. The phone's own changes are in the
last two rows.

| item | 4 sync, "Yes, merge" (4 runs: 4 ×2, 4b, 4 B) | 4 sync, "No, overwrite" (1) | 5 save, "Yes, merge" (2, and 1 in B) | 5 save, "No, overwrite" (1) | 6 "Always merge on conflict" (1) | 7 check off, "No, overwrite" (1) | 8 reopened (2) | 9 QuickUnlock, then a save, "Yes, merge" (1) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| KG3, a write-once `KPG.` entry in `KeePassGate` (a revocation) | yes | **no** | yes | **no** | yes | **no** | yes | yes |
| `KPG.NewKey`, a database custom data key SOLD lacks | yes (its per-key time dropped) | **no** | yes | **no** | yes | **no** | yes | yes |
| DZ, a deletion record naming no entry | yes | **no** | yes | **no** | yes | **no** | yes | yes |
| X1 deleted, with its record (the phone left X1 alone) | yes | **no**: X1 back, no record | yes | **no** | yes | **no** | yes | yes |
| P1, a password only the desktop changed | yes | **no** | yes | **no** | yes | **no** | yes | yes |
| Q1, both changed, the desktop's older | history | **no** | yes (the phone left Q1 alone in case 5) | **no** | yes | **no** | yes | yes (the phone left Q1 alone) |
| Q2, both changed, the desktop's newer | yes, current | **no** | yes | **no** | yes | **no** | yes | yes |
| Q3, both changed, equal times | history | **no** | yes | **no** | yes | **no** | yes | yes |
| P2, the phone's password | yes | yes | yes | yes | yes | yes | (not changed) | yes |
| `phone-new`, the phone's new entry | yes | yes | (not made) | (not made) | (not made) | (not made) | (not made) | (not made) |

KG1 and KG2, the write-once entries both copies held, were in every file of every run.

## 6. Not run, and why

- **"Cancel" at the question during a synchronize**: the overwrite report measured Cancel at a save
  (nothing written, the edit left unsaved); at a synchronize it was not run.
- **Background synchronization on its own** (the periodic background synchronization and
  "Synchronize in background" finishing with the program closed): only what follows a save in the
  foreground was seen (case 5 in B: the question came while the program stood open). What it does
  when the question has nobody to answer it was not measured.
- **Any other remote**: HTTPS, Nextcloud or ownCloud, Google Drive, Dropbox, OneDrive, SFTP, FTP,
  Samba. Read: they all go through the same cache (`CachingFileStorage`) when "Database caching" is
  on, so the cases here should carry over; not run.
- **A lock with QuickUnlock** is case 9; the application timeout was not run (read: it locks
  through the same `App.Lock`).
- **The case 6 setting without the screen's side effect**: turning "Always merge on conflict" on
  needs the File handling screen, which also stores "Synchronize in background" true; with the two
  apart, "Synchronize in background" alone (case 5 in B) still asked, so the merge without asking
  is the setting's.
- **Keepass2Android's own trimming of history at its limits**: no entry reached the limit.
- **KeePass on the desktop** reading the merged file: the files are read back with KeePassLib 2.62,
  and KeePass's own Synchronize was run headless on the same copies (section 2); KeePass's window was
  not started.
- **Any device but this emulator**, any Android but 11.

## 7. What a phone that synchronizes keeps and loses

Measured on Keepass2Android 1.15-r6 with the file on a WebDAV server, reached through the program's
own "HTTP (WebDav)" storage, which keeps a cached copy.

**Everything the desktop decided survived every sequence except one answer.** Its write-once
`KPG.` entry, its database custom data key, its deletion record for an identifier nobody holds, its
deletion of an entry the phone had left alone, and its passwords were in the server's file after
every synchronize and every merge answered "Yes, merge", after every save while the server was
reachable, and after reopening; ordinary passwords changed on both sides kept the newer value
current and the other in the entry's history, and at equal times the phone's current with the
desktop's in history. The phone's own changes (a password, a new entry) were never lost either.

**The one sequence that loses**: the server's file has changed since the phone last loaded it, the
phone has an unsent change (a save while the server was reachable, or one waiting in the cache), and
the answer to **"Merge changes?"** is **"No, overwrite"**. Then the server's file is the phone's
copy, and everything the desktop wrote since the phone's last load is gone, from the entries and
from their histories (3 runs of 3: at a save, at "Synchronize cache copy with source", and with
"Check for modifications" off). The question is the same dialog at a save and at a synchronize:

> **Merge changes?** The database file was modified externally. Do you want to load and merge the
> changes before saving? Select No if you want to overwrite the external changes.
> [Cancel] [No, overwrite] [Yes, merge]

**The safe sequence of presses**, measured:

1. Open the database through Keepass2Android's own access to the server (Open file..., the storage
   type, the address, the file), not through the system file picker.
2. Before editing: More options, **Synchronize database...** (or close the database and open it
   again with the password). It loads the server's file and writes nothing (cases 2 and 8). A
   QuickUnlock does not: it keeps the copy held before the lock, and the next save asks (case 9).
3. Edit and save. With the server reachable the save goes straight to the server; if the server's
   file changed meanwhile, the save asks "Merge changes?": **Yes, merge** (case 5).
4. Away from the server (no network, the server down, or "Work on internal cache only"), saves go to
   the cache with no question and no warning. When the server is back: **Synchronize database...**,
   or "Synchronize cache copy with source" in the cache-only mode; if both sides changed it asks
   "Merge changes?": **Yes, merge** (cases 4, 4b).
5. **Never "No, overwrite".** Turning on **"Always merge on conflict"** (Settings, App, File handling)
   takes the question away and merges every time (case 6); opening that screen also turns on
   "Synchronize in background", which changed no result measured here. **"Check for
   modifications" off does not take the question away** (case 7, and `measured-overwrite.md` 2.5),
   so it neither protects nor endangers.

**Not a loss, but worth knowing**: after a synchronize that only loaded the server's changes, the
program's cache kept the older file (case 2, 2 of 2), while what the program showed was the
server's version; a later merge was not run from that state. Every Keepass2Android write drops the database custom
data's per-key modification times and writes KDBX 4.0 (as in `measured-android.md` 2.1).

### KeePassDroid: incompatible with a synchronized or merged database (the evidence)

Not run here, since KeePassDroid is incompatible.
The evidence is in `measured-overwrite.md` section 3: KeePassDroid 2.7 has no
synchronization and no merge; its save writes over a file that changed underneath it without any
question (3 runs of 3), losing every change the desktop made meanwhile; and every save deletes the
custom data of every entry and group (`measured-android.md` 3.2).
