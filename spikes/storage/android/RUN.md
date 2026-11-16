# The Android measurements

On 2026-11-15 an emulator ran Keepass2Android 1.15-r6, KeePassDroid 2.7 and KeePassDX 4.5.8
against databases holding every candidate carrier for KPG data, and KeePassLib 2.62 read back
every file they saved and synchronized it into KeePass again. `papers\ground\measured-android.md`
reports it, naming its evidence `android-evidence\`. `papers\ground\ground-android.md` read the
same programs' source and measured one thing on the console, the MD5 pair of its section 5.1.
Everything behind both is here: the database builder and reader, the emulator's scripts and
flows, the MD5 probe, and what they recorded. In the tables below "measured 2.1" means section 2.1
of `measured-android.md` and "ground 5.1" section 5.1 of `ground-android.md`.

Two more measurements followed that night on the same emulator, from a working directory of their
own, and are carried here as well: `papers\ground\measured-overwrite.md`, when Keepass2Android and
KeePassDroid write an older copy over a newer file, and `papers\ground\measured-k2a-synchronize.md`,
Keepass2Android's own synchronization of a database on a WebDAV server. Sections 11 to 14 are
theirs; there "overwrite 2.1" means section 2.1 of the first and "synchronize 2" section 2 of the
second.

**What ran again from the branch on 2026-11-16, and what came out:**

- **The probe builds against the pinned KeePass and makes the six bases again**, and every base's
  dump is the record line for line but the first, which names the file's SHA-256: the salts and
  IVs of every save change it. Even the sizes matched on the first run; on each later run one or
  two bases came out 16 bytes off, which is the same randomness.
- **Step 8, KeePassLib's Synchronize of each phone's file back into KeePass, ran again here** from
  the files the phones saved, which the branch does not carry: all 200 files are the record's, in
  KeePassDroid's labels with `M settingsChanged` masked, because KeePassDroid writes none and every
  read of its file answers the moment of reading.
- **The MD5 pair came out the same**: 72,707 tries, the same two bodies, 0.20 s against 0.16 s.
- **The emulator runs were recorded on 2026-11-15 and are not re-run**: the emulator was in use by
  another measurement. What they left is checked instead. `checks\tables.py` states 388 claims of
  `measured-android.md` over the recorded read-backs, diffs and step logs, every cell of every
  table among them, and 386 hold. The two that do not are one wording in table 5.3, section 8.1.
- **The later probe builds beside the first and makes the later bases again**: OLD, NEW-diff,
  NEW-ident, SOLD and SNEW-fixed are the record line for line but the first, and so is NEW-same but
  its padding, which a search against a size the random IVs move decides: 1309 characters in the
  first of three runs here, as recorded, then 1311 and 1312. NEW-diff came out 16 bytes short in
  the third run, the same randomness (section 11.5).
- **KeePass's own Synchronize of synchronize 2 ran again here**, through the measurement's
  `expect.sh`, from copies of the files the phone left, which the branch does not carry: 15 of 15
  dumps are the record's.
- **The later emulator runs are not re-run either.** `checks\overwrite.py` states 117 claims of
  `measured-overwrite.md`, every cell of every table among them, and 115 hold;
  `checks\k2a_synchronize.py` states 204 of `measured-k2a-synchronize.md`, and 203 hold. The three
  that do not are in sections 11.5 and 12.5.

Nothing here contradicts a finding of any of the three papers. Section 8 lists the five points
where a number or a word of `measured-android.md` differs from what the record shows, section 9
its claims no output here shows, and sections 11.5 and 12.5 the same for the later two.

## 1. Where the papers' paths are

| The papers write | Here |
| --- | --- |
| `android-evidence\base\` | `recorded\base\` |
| `android-evidence\runs\<program>\<run>\` | `recorded\runs\<program>\<run>\` |
| `android-evidence\sync\<label>\` | `recorded\sync\<label>\` |
| `android-evidence\logs\`, with `logs\runs\<program>\` and `not-counted\` | `recorded\logs\` |
| `android-evidence\keepasslib-memprot-reset.txt`, and `logs\keepasslib-memprot-reset.txt` in a comment of `Store.cs` | `recorded\keepasslib-memprot-reset.txt` |
| `android-evidence\flows\` | `scripts\flows\` |
| `android-evidence\scripts\`, and `scripts\` in measured 1.4 | `scripts\` |
| `android-evidence\probe\` | `probe\` |
| `spikes\storage\android\work\runs\` and `work\base\` in the draft evaluation and in `papers\options\rules-linked\critique-survival.md` | `recorded\runs\` and `recorded\base\`; a fresh run writes `work\` |
| `spikes\storage\probes\ground-android\src\` in ground 0 | not carried: the clones, listed in section 10 |
| `logs\md5pair.log` and `out\md5pair\` in ground 5.1 | `md5pair\recorded\md5pair.log` and `md5pair\recorded\md5pair\` |
| `<work>` in the recorded step logs | this directory, which holds `work\` and `out\` |
| `<dl>` | the APK directory inside WSL, `$HOME/storage-measure/dl` |
| `spikes\storage\android\recorded\overwrite\` and `recorded\k2a-synchronize\` in the two later papers, which `tools\Copy-Papers.ps1` wrote for their `android-evidence\overwrite\` and `android-evidence\k2a-synchronize\`, with `base\`, `runs\`, `logs\` and `server\` below as the papers name them | `recorded\overwrite\` and `recorded\k2a-synchronize\` |
| `server\` in synchronize 1.3, "the rclone log, the fetch script and the published `SHA256SUMS`" | the log and the checksums in `recorded\k2a-synchronize\server\`; the fetch script `server\fetch-rclone.sh` (section 14) |
| `spikes\storage\android\scripts\`, `scripts\flows\` and `probe\overwrite\` in the two later papers | the same places: the later scripts beside the first measurement's, the later state of the probe in `probe\overwrite\` (section 13) |
| `<wsl-home>` in `recorded\k2a-synchronize\server\rclone.log`, `<home>` in the emulator logs | the WSL user's home directory |
| `<invented>` | the WebDAV password, invented for the runs (section 14) |

The layout is the measurement's own, because the scripts depend on it: `env.sh` finds the flows
in `scripts\flows\`, and `collect.sh` and `sync.sh` find the probe in `probe\` and the bases in
`work\base\` beside them. The earlier spike keeps its scripts and `flows\` together in
`spikes/revocation/android` in the same way.

## 2. The runs

| Run | Command, from the branch root | Writes | Recorded copy | Here | Recorded |
| --- | --- | --- | --- | --- | --- |
| R1, the console part | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\android\run.ps1` | `work\base\`, `sync\base\`, `md5pair\out\`, `out\tables.txt`, `out\tables-bases.txt`, `out\overwrite.txt`, `out\k2a-synchronize.txt` | `recorded\base\`, `recorded\overwrite\base\`, `recorded\k2a-synchronize\base\`, `md5pair\recorded\` | 34 s | none |
| R2, step 8 again, Git Bash | `sh spikes/storage/android/scripts/sync-all.sh [<runs> <bases>]` | `work\sync\` | `recorded\sync\` | 39 s | none |
| R3, the comparison | `uv run --no-project python -I -X utf8 spikes\storage\android\compare.py` | prints | | under 1 s | |
| R4, the record against the tables | `uv run --no-project python -I -X utf8 spikes\storage\android\checks\tables.py` | prints | | under 1 s | |
| R5, the emulator flows | under Linux, section 5 | `work\runs\`, `out\runs\`, `out\batch-*.txt` | `recorded\runs\`, `recorded\logs\` | not run | 17:01 to 19:48 UTC |
| R6, the overwrite flows | under Linux, section 11.2 | `work\runs\`, `work\not-counted\`, `out\runs\`, `out\batch-*.txt` | `recorded\overwrite\` | not run | 21:07 to 01:05 UTC |
| R7, the synchronize flows | under Linux, section 12.2 | `work\sync\`, `out\sync\`, `server\root\` | `recorded\k2a-synchronize\` | not run | 22:59 to 01:09 UTC |
| R8, KeePass's own Synchronize again, Git Bash | `sh spikes/storage/android/scripts/sverdict.sh <dir>` and `sh spikes/storage/android/scripts/expect.sh <dir>`, for each of the five runs of section 12.2 | `<dir>\keepass-sync\` | `recorded\k2a-synchronize\runs\<run>\keepass-sync\` | 50 s | none |
| R9, the later records against their tables | `uv run --no-project python -I -X utf8 spikes\storage\android\checks\overwrite.py`, and the same with `checks\k2a_synchronize.py` | prints | | under 1 s each | |

`run.ps1` builds the probe, makes and dumps the bases, runs the MD5 probe, runs `checks\tables.py`
twice (on the record, and its base claims on the fresh bases), builds the later state of the probe
and makes and dumps the later bases with it, runs `checks\overwrite.py` and
`checks\k2a_synchronize.py` on the record, and ends with `compare.py`, which exits 0 when nothing
differs. What a run needs: Windows PowerShell 5.1, the .NET SDK, `uv`, and KeePass 2.62, which
`tools\KeePass.ps1` provides. Step 8 and R8 need Git Bash and the files the phones saved, which
only a run of the emulator flows makes; here both were given copies of the files the measurements
pulled. Nothing shows a window.

`out\` and `work\` are gitignored, and so are `sync\`, `server\rclone-dl\` and `server\root\`, by
this directory's `.gitignore`. Every database a run makes is in `work\` or `sync\`: the ten bases,
the two synchronize bases, and after step 8 and R8 the merged results beside their dumps.

## 3. Every claim, and whether it reproduced

"Reproduced" records whether running the probe again gave the claim again. For what only the
emulator can make, it is "not run", and "the record" answers whether the recorded output shows the
claim, as `checks\tables.py` (R4) states it.

| Claims | Quoted in | Probe | Command | Reproduced | The record | Note |
| --- | --- | --- | --- | --- | --- | --- |
| the three APKs: file hashes, signer digests, version codes; `apksigner` of build-tools 37.0.0 | measured 1.1 | `scripts\verify-apks.sh` | R5 | not run | holds | `recorded\logs\apksigner.txt`, three `SIGNER MATCH`; section 5 |
| the emulator and the device | measured 1.2 | `scripts\emu-start.sh`, `wait-boot.sh` | R5 | not run | in part | section 9 |
| the bases, the AES variants, the merge pair: 3 claims | measured 1.3 | `probe\`, `make` | R1 | yes | holds | every base dump the record but its first line; section 4 |
| the side fact: "protect user name" lost by KeePassLib's open and save, 42 lines | measured 1.3 | an earlier `Store.cs`, not kept | none | not run | holds | section 8.4 |
| "Merge changes?" answered "Yes, merge" | measured 1.4 | the flows | R5 | not run | holds | 1 claim |
| the runs counted, 15, 8 and 15; 34 and 32 of 35 characters | measured 1.5 | the step logs | R5 | not run | holds | 2 claims |
| Keepass2Android, carrier by operation, the AES paragraph, the delete dialog | measured 2.1 | the flows, `collect.sh` | R5 | not run | 112 of 112 hold | |
| Keepass2Android's merge, every cell, the runs alike, the two directions one line apart | measured 2.2 | the flows, `collect.sh`, `runs-alike.sh` | R5 | not run | 21 of 21 hold | |
| KeePassDroid: the menus, every cell, the AES paragraph, step 5, the unchanged edit | measured 3.1 to 3.3 | the flows, `collect.sh` | R5 | not run | 88 of 88 hold | |
| KeePassDX: the plain saves, every merge cell, the runs alike but the root's access time, every operation cell | measured 4.1 to 4.4 | the flows, `collect.sh`, `runs-alike.sh` | R5 | not run | 115 of 115 hold | |
| back into KeePass: the inputs, every cell of 5.1, 5.2 and 5.3 | measured 5 | `probe\`, `sync`, through `sync.sh` | R2 | yes | 39 of 41 hold | the two are section 8.1, in the record and in the run here alike |
| not made: 17 KeePassDX attempts, the two group dialogs, no entry at the history limit | measured 6 | the step logs, the dumps | R5 | not run | 4 of 4 hold | |
| what survives on a phone | measured 7 | | | | holds | restates the tables |
| 72,707 tries; two bodies of 48 bytes whose MD5 digests share four bytes | ground 5.1 | `md5pair\scripts\md5prefix_pair.py` | R1 | yes | holds | the running time differs, 0.20 s against 0.16 s; section 6 |
| "about 1% at 10,000 attachments" | ground 5.1 | arithmetic | | | | marked assumed: `10000**2 / 2**33` is 1.16% |
| the claims marked "measured (spike)" | ground 1.1, 1.2, 1.4, 2, 3.3, 4.1, 4.2, 5.5, 6 | `spikes/revocation-merge` | | not run | | section 9 |

Every other statement of `ground-android.md` is marked read or assumed: a source read at the
releases in section 10, which no probe here makes.

`checks\tables.py` states each claim as the lines of the dumps that decide it: a carrier is "kept"
when its lines in the read-back are its lines in the base, the entry the run added set aside, and a
`parent` line is compared without the parent's name, which a rename of G changes. It was itself
checked by running it on a copy of the record with dumps swapped between programs, where every
swapped cell failed. Pointed at the fresh step 8 with `--root spikes\storage\android\work`, where
only `sync\` and `base\` exist and every other claim fails for want of its files, the 40 claims of
tables 5.1 to 5.3 come out as on the record: 38 hold, and the same two do not.

## 4. `probe\`: the database builder and reader

`StoreProbe.exe`, over KeePassLib 2.62. `make <dir>` writes the bases `S-argon`, `S-aes`,
`S-aes-nocd` and `S-aes-plain` and the merge pair `C` and `P` (measured 1.3); `dump` and `dumpdir`
read a file into the sorted line-per-item dump every comparison uses (measured 1.4); `sync <target>
<source> <out>` is KeePass's own Synchronize, `PwDatabase.MergeIn` with
`PwMergeMethod.Synchronize`, which `sync.sh` runs in both directions for step 8 (measured 5).

**Which source wrote which record.** `Store.cs` last changed at 16:55:46 UTC, the exe was built at
16:55:54, and the bases and their dumps were written at 16:55:55, before the first emulator run at
17:01:36; every dump of a pulled file and every step-8 file is later. So everything under
`recorded\base\`, `recorded\runs\` and `recorded\sync\` was written by the source here, which is
byte for byte the measurement's. The one exception is `recorded\base-memprot\`, written at
16:55:38 by the version before, which turned "protect user name" on; that version was replaced
eight seconds later and not kept. The comment in `Store.cs` where the protection is set names
this record as the reason the bases keep KeePassLib's defaults.

| Number | Quoted in | Recorded | Here | Reproduced |
| --- | --- | --- | --- | --- |
| `KPG.Record` of 2861 characters | measured 1.3 | `len=2861` | `len=2861` | yes |
| `authority.p12`, 3000 bytes, protected, and the same bytes as `copy.p12` | measured 1.3 | `len=3000 sha=86f51de7a0078da7` on both | the same | yes |
| `e1.txt`, 600 bytes; `added-c.bin`, 1500 bytes | measured 1.3 | 600, 1500 | 600, 1500 | yes |
| three history items at +1 h, +2 h, +3 h, current at +4 h, on R1 and on E1 | measured 1.3 | 11:00, 12:00, 13:00; 14:00 | the same | yes |
| Argon2d, 2 iterations, 1 MiB, 1 lane; KDBX 4.1 | measured 1.3 | `it=2 mem=1024KiB par=1`, 4.1 | the same | yes |
| AES-KDF, 60000 rounds; `S-aes-plain` KDBX 4.0 | measured 1.3 | `rounds=60000`, 4.0 | the same | yes |
| `HistoryMaxItems` 8, `HistoryMaxSize` 5 MiB, `MaintenanceHistoryDays` 200 | measured 1.3 | 8, 5242880, 200 | the same | yes |
| D1 at +6 min, D2 at +7 min | measured 1.3 | 10:06, 10:07 | the same | yes |
| every cell of the merge pair: +5 h, +6 h, +7 h, `NC`, `NP`, `X1`, `X2`, `Y1`, `Y2`, G's notes, `Y3` | measured 1.3 | as the table gives | the same | yes |
| every line of the six base dumps after the first | measured 1.4 | 422, 422, 419, 419, 474, 488 lines | the same | yes |
| a base with "protect user name" on came back from KeePassLib's open and save with every `UserName` unprotected, 42 lines | measured 1.3 | 42 lines | not run | not run, section 8.4 |
| step 8: all 20 labels, 200 files | measured 5 | `recorded\sync\` | the same, masked as above | yes |

`checks\tables.py` names these claims `1.3-bases`, `1.3-aes`, `1.3-pair` and `1.3-memprot`, and
`run.ps1` runs the first three against the fresh bases too.

**Changes to run it here.** `Probe.csproj`: the `HintPath` of `KeePass` from the product
worktree's `.keepass\2.62\KeePass.exe` to `$(KeePassExe)`, which the branch's
`Directory.Build.props` defines as the same pinned file, and its comment from "the KeePass.exe the
review4 worktree provides" to "the KeePass.exe the branch provides", since the comment named a
local worktree. `Store.cs`: none. The `KeePass.exe` beside the built probe has the SHA-256 the
reports quote, `AFB89B74...C3F3`, and so did the `KeePass.exe` the measurement's build used. And
one change made when the later measurements came: an `ItemGroup` that removes `overwrite\**` from
the project's sources, so that the later state of the probe in `probe\overwrite\` (section 13),
which is a project of its own, is not compiled into this project.

## 5. The emulator flows: recorded on 2026-11-15, not re-run

The Linux side drives the programs; the Windows side makes and reads the databases. Each run
starts from cleared data, puts one base alone in `/sdcard/Download`, runs a flow with `k.py`,
which reads the screen through `uiautomator` and presses what the flow names, and pulls back every
file left there. `scripts\` holds:

- `env.sh`, sourced by every Linux script: this directory as the work directory, the copy of the
  pins beside it, and the SDK; `root-setup.sh`, `emu-start.sh`, `wait-boot.sh`, `teardown.sh` for
  the emulator; `stage-apks.sh`, `verify-apks.sh` and `install.sh` for the programs; `run-op.sh`
  for one operation, `batch.sh` for a file of them under `batches\`, `run-A.sh` and `run-B.sh` as
  the spike had them, `prep.sh` and `k.sh` for working out a screen by hand;
- `flows\`, the 37 flow files, as the runs left them (each log records what its run pressed);
- `collect.sh`, `sync.sh`, `runs-alike.sh` and `evidence.sh` for the Windows side, in Git Bash,
  and `sync-all.sh`, added here (section 7);
- `sdk-setup.sh`, `avd-create.sh` and `fetch.sh`, the earlier spike's, which the measurement
  borrowed from that branch and which are carried here now (section 14).

The later measurements' scripts and flows are beside these, sections 11 to 13.

**How it is run from the branch.** The bases come first, from `run.ps1` on the Windows side. Then,
under Linux, from this directory, since the batch files name the bases relative to it: once, the
SDK, the device `kpg30` and the three APKs; then the runs.

```sh
cd spikes/storage/android
sudo sh scripts/root-setup.sh "$USER" install
sh scripts/sdk-setup.sh
sh scripts/avd-create.sh
sh scripts/fetch.sh
sh scripts/verify-apks.sh
sh scripts/emu-start.sh kpg30 5554 wipe &
sh scripts/wait-boot.sh
sh scripts/install.sh k2a-1.15-r6
sh scripts/run-op.sh k2a k2a-A.flow keepass2android.keepass2android S-argon save-argon ENTRY=droid-added
sh scripts/batch.sh scripts/batches/k2a-save-aes.txt
sh scripts/batch.sh scripts/batches/k2a-edit.txt
sh scripts/batch.sh scripts/batches/k2a-ops.txt
sh scripts/install.sh kpd-2.7
sh scripts/batch.sh scripts/batches/kpd-all.txt
sh scripts/install.sh kpdx-4.5.8
sh scripts/batch.sh scripts/batches/kpdx-save-merge.txt
sh scripts/batch.sh scripts/batches/kpdx-ops.txt
sh scripts/teardown.sh
```

The `*-redo*.txt` batches are the runs that were repeated after a failure, as the summaries under
`recorded\logs\batch-*.txt` show. Then, in Git Bash, `sh scripts/collect.sh
work/runs/<program>/<run> <base>` for every run directory, with the base it was given (`S-argon`,
an AES variant, or `C` or `P` for a merge); `sh scripts/sync-all.sh`; and `sh scripts/evidence.sh`,
which writes a fresh evidence directory to `out\evidence\` for comparison with `recorded\`.

The measurement itself did not fetch: `stage-apks.sh` copied the APKs from the download directory
of the earlier measurement, `sh scripts/stage-apks.sh <that directory>`, and `fetch.sh` takes its
place for anybody without that directory. Both leave the same files in the same place.

**The pins.** `scripts\android.sha256` is the measurement's copy, holding the three programs it ran
and the SDK; its four lines are lines of the root `android.sha256`, character for character, and
`fetch.sh`, `verify-apks.sh` and `install.sh` read it, so `fetch.sh` fetches the three releases
this branch measures and no other. `recorded\logs\apksigner.txt` is what `verify-apks.sh`
printed: each file's hash, a verifying signature, exactly one signer, and that signer's digest,
all three `SIGNER MATCH`.

**How long it ran.** From the first step log to the last, 17:01:36 to 19:47:48 UTC, the emulator
having started at 16:48:16. One counted run took 72 to 392 s: Keepass2Android's 15 runs 30 min,
KeePassDroid's 8 runs 11 min, KeePassDX's 16 logs 56 min, the uncounted attempts on top.

**Changes to run it here.** The evidence copy wrote each of these paths as a placeholder, which no
shell can run:

- `env.sh`: `D=<work>` to `D=$(cd "$P/.." && pwd)`, this directory, whose `work\` and `out\` are
  gitignored. `collect.sh` and `sync.sh`: `M=<work>` to `M=$(cd "$(dirname "$0")/.." && pwd)`,
  the same directory.
- `evidence.sh`: `M` the same; its target from the review's evidence directory to `$M/out/evidence`,
  so that a re-run writes beside the record rather than over it; the three patterns that matched
  the session's scratch directory and the earlier measurement's replaced by two that match this
  directory as Git Bash and WSL name it, `$M` and `/mnt$M`, and the pattern for `<spike-work>`
  dropped, since `stage-apks.sh` names no path any more. Its line copying
  `logs\keepasslib-memprot-reset.txt` names a directory nothing here writes and fails harmlessly;
  the record is in `recorded\`.
- `stage-apks.sh`: `SRC=<spike-work>/dl` to the first argument, the earlier measurement's download
  directory, which lived in a session's scratch directory.
- `batches\kpdx-ops.txt` and `batches\kpdx-save-merge.txt`: `SRC=<work>/work/base/` to
  `SRC=work/base/`, relative to this directory. `batch.sh` hands a line to `run-op.sh` without
  expanding anything in it, so no absolute path written there holds on another machine.

And one file added: `.gitattributes`, which keeps `scripts\android.sha256` and
`scripts\batches\*.txt` at LF, since they are read under Linux and the root rules reach only
`*.sh`, `*.flow`, `*.py` and the root's `android.sha256`, and marks `*.bin` binary. `k.py`, the
flows, the batch files' other lines and every other script are unchanged.

## 6. `md5pair\`: KeePassDX's 32-bit attachment identity

`scripts\md5prefix_pair.py` looks for two attachment bodies of equal length whose MD5 digests share
the first four bytes, which is what KeePassDX compares an attachment by (ground 5.1), and writes the
pair. `run.ps1` runs it; by hand:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\android\md5pair\scripts\md5prefix_pair.py spikes\storage\android\md5pair\out\md5pair > spikes\storage\android\md5pair\out\md5pair.log
```

| Number | Quoted in | Recorded | Here | Reproduced |
| --- | --- | --- | --- | --- |
| tries to find the pair | ground 5.1 | 72,707 | 72,707 | yes |
| seconds | ground 5.1 | 0.16 | 0.20 | differs: a running time |
| length of each body | ground 5.1 | 48 | 48 | yes |
| the two digests | ground 5.1, by `out\md5pair\` | `d71a4c0feb19...`, `d71a4c0f6eb8...` | the same | yes |
| the bodies | ground 5.1, `out\md5pair\` | `a.bin`, `b.bin` | byte for byte | yes |

**`recorded\md5pair\b.bin` is the fresh run's.** The original was overwritten in the scratch
directory before it was copied, so the copy here is the file this run wrote, held to what the
recorded log prints for it: the MD5 `d71a4c0f6eb8866fa44bd26df5b08b4e` and the body for the nonce
72707. `a.bin` is the original, and the fresh `a.bin` is byte for byte the same.

Python 3.14.8, from `uv`. Nothing in the script depends on the version: `json.dumps` and `hashlib`
give the same bytes. No change to the script.

## 7. What was added on the branch

- `run.ps1`, the console part in one command, R1.
- `compare.py`: the fresh bases against the record, the first line masked; every recorded diff,
  49 under `runs\` and 120 under `sync\`, against the difference of the two dumps it names, and
  each run diff again against the fresh base; step 8 again where `work\sync\` exists, with
  KeePassDroid's `M settingsChanged` masked; and the MD5 pair. It exits 0 when nothing differs.
- `checks\tables.py`, the claims of `measured-android.md` over the record, section 3. It exits 0
  when exactly the claims it lists as known not to hold do not; `-v` prints the lines behind every
  claim, and `--root` points it at another record of the same layout.
- `scripts\sync-all.sh`, step 8 for every label the record holds: the 20 labels, each with the
  phone file it used (run 1 of a merge cell) and KeePass's copy, read off the first lines of the
  recorded dumps, since the measurement ran them one by one and kept no list.

And for the later two measurements:

- `checks\overwrite.py` and `checks\k2a_synchronize.py`, the claims of the two later papers over
  their records, sections 11.4 and 12.4, written and checked as `checks\tables.py` was: each exits
  0 when exactly the claims it lists as known not to hold do not, `-v` prints the lines behind
  every claim, and `--root` points it at another record of the same layout. Run on copies of the
  records with dumps, logs and verdict lines swapped between cases, every swapped cell failed: in
  `recorded\overwrite\` the dumps of a "No, overwrite" run and a "Yes, merge" run, the logs of a run
  on screen and a run in the background, the logs of the control and of a replacement of OLD's
  length, the verdicts of a reopened run and an overwritten run, the dumps of KeePassDroid's
  baseline and its lock, and the dumps of NEW-same and NEW-diff; in
  `recorded\k2a-synchronize\` the server's dumps of a save answered "Yes, merge" and of a save
  answered "No, overwrite", and of a merge and an overwrite at a synchronize, the logs of cases 6
  and 7, the caches of cases 2 and 8, and KeePass's two results of a run.
- In `run.ps1`, the later steps of R1; in `compare.py`, its items 5 to 7: the later bases, the
  later recorded diffs against their dumps (342 diffs), and R8's dumps against the record.
- `.gitignore`, for what the later scripts write outside `work\` and `out\`: `sync\`,
  `server\rclone-dl\` and `server\root\`.
- In `.gitattributes`, `scripts/batches-ow/*.txt` at LF, since `batch-ow.sh` and `batch-sy.sh`
  read them under Linux, and the record's `SHA256SUMS` kept from normalization, since it is a
  signed text whose bytes stay as rclone published them.

## 8. Where the record and the papers differ

1. **Table 5.3, two cells of "into the phone's file".** "merge PC with C: the same, but `M2`
   becomes C's version" and "merge PC with P: the phone's merge result exactly" both also take the
   root group's access time from KeePass's copy: `G Root t.accessed 2026-11-15T18:21:51Z` becomes
   `2026-11-01T10:00:00Z`, in the record and in the run here alike. The CP rows into the phone's
   file do the same; their cells name only what changed, so they still hold. Nothing a carrier
   holds is touched. `checks\tables.py` names the two `5.3-merge-fromPC-with-C-phone` and
   `5.3-merge-fromPC-with-P-phone`.
2. **KeePassDroid's step 8 counts move by two lines.** `sync.sh` prints 120 line counts. Five of
   them, all of a result against the phone's file, come out two lines different here: for
   `kpd-save-argon`, 4 against 2 recorded into KeePass and 7 against 5 into the phone's file; for
   `kpd-edit-e1` and `kpd-edit-carriers`, 9 against 7 into KeePass; for `kpd-delete`, 2 against 4
   into KeePass. In every case the two lines are `M settingsChanged`: KeePassDroid writes none,
   each read answers its own second, and whether the result and the phone's file were read in the
   same second decides the pair.
3. **"72,707 random tries"** in ground 5.1. The probe draws nothing at random: it tries the nonces
   1, 2, 3 and so on in order, which is why the count comes out the same every time.
4. **The record of the side fact in measured 1.3 cannot be made again from here.** The record is
   42 lines, one heading and 41 lines of diff, and those are exactly the `UserName` lines of the
   diff of the two dumps in `recorded\base-memprot\`: all 19 in that `S-argon` are protected and
   none of the 22 in `C`. The source that made them was replaced eight seconds later and not kept,
   and the probe here, run unchanged, cannot make a base with "protect user name" on. So the
   finding stands on the record alone. "42 lines" counts the file, heading included.
5. **The base sizes.** They are not quoted, and are reported by `compare.py` rather than compared:
   a save's random IVs change the compressed size by a 16-byte block now and then.

## 9. Claims with no probe or output here

What the papers state as measured that nothing under this directory shows:

| Claim | Quoted in | What there is |
| --- | --- | --- |
| system image revision 16, platform-tools 37.0.1, Linux 6.18.58.1 in WSL 2 | measured 1.2 | nothing recorded; the emulator logs name the image `android-30/google_apis/x86_64` and the emulator 37.2.13.0, and `apksigner.txt` build-tools 37.0.0 |
| `kpg30` is a pixel_5 with 4 cores, 3072 MB, 6 GB of data and a hardware keyboard | measured 1.2 | the device was made by `avd-create.sh` of `spikes/revocation-merge`, carried here as `scripts\avd-create.sh`, which sets exactly these; its configuration was not recorded here. The emulator log shows `kpg30`, 1080 x 2340 at 440 dpi |
| started with its user data wiped, driven as root with animations off | measured 1.2 | what `emu-start.sh ... wipe` and `wait-boot.sh` do; their output was not recorded, and the emulator log shows a full startup |
| the second emulator exhausted WSL's memory (91 MB free) and raised "System UI isn't responding" | measured 1.2 | its emulator log, `recorded\logs\emu-kpg30b-*.log`, shows a boot of 194 s and nothing about memory |
| Keepass2Android's group menu lists Advanced Search, Settings, Synchronize database, Work on internal cache only, Sort by, Open another database, and offers no "empty the recycle bin" | measured 2.1, 6 | no log of this measurement lists it; KeePassDroid's menus are recorded, `recorded\logs\runs\kpd\menus.log`. The later records do list it, for the same release on the same device: `recorded\overwrite\logs\explore\k2a-menu.log` and the menu before "Work on internal cache only" in `recorded\k2a-synchronize\logs\sync\s3c-phone-cacheonly-r1.log` hold exactly these six items |
| Keepass2Android's first save attempt stopped in the file picker | measured 1.5, 6 | its log was overwritten by the repeat, as the paper states; `k.py` holds the snake walk that followed |
| the claims `ground-android.md` marks "measured (spike)" | ground 1.1, 1.2, 1.4, 2, 3.3, 4.1, 4.2, 5.5, 6 | they rest on the branch `spikes/revocation-merge`: its `RESULTS.md` holds at the lines cited what is quoted, and its `output\` holds the files named. Not carried here |

## 10. What was not carried, and why

From the measurement's working directory, which became this directory:

- **every `.kdbx`**: the six bases, the two of `base-memprot`, the 51 files pulled from the
  emulator, and the step-8 results. A database is never carried; its dump is, and a run makes the
  bases again;
- `probe\bin\` and `probe\obj\`, build output;
- `out\shots\kpd-entry-view.png`, a screenshot `k.py` took while a KeePassDroid screen was worked
  out by hand with `kpd-explore-entry.flow`, which no paper quotes.

The emulator logs `out\emu-*.log`, which name the machine's paths as a comment of `emu-start.sh`
notes, are carried to `recorded\logs\` with those paths replaced (last paragraph). From the report
directory's `android-evidence\`: everything, as listed in section 1; its directories `overwrite\`
and `k2a-synchronize\`, which the later measurements wrote, as sections 11 to 13 describe; and not
its directory `keepassdx\`, which a measurement still in progress writes. From the working
directory of `ground-android.md`:
the MD5 probe and its output, and not the four shallow clones of other projects' sources under
`src\`, which the paper cites by path and line at these releases:

| Program | Repository | Tag | Commit |
| --- | --- | --- | --- |
| Keepass2Android | `https://github.com/PhilippC/keepass2android` | `v1.15-r6` | `bbcf3787c75bd5b8a9769576191129b5dd9e8bc0` |
| KeePassDX | `https://github.com/Kunzisoft/KeePassDX` | `4.5.8` | `77ecfa8654a5bd0d79d512f8fa71c0af8a29b554` |
| KeePassDX | `https://github.com/Kunzisoft/KeePassDX` | `3.2.0` | `5baf91dc779dfafc7936e78d69c29ef09e968c3e` |
| KeePassDroid | `https://github.com/bpellin/keepassdroid` | `v2.7` | `f4212ee4cd08704b94c0004254e54ad5bffa45e0` |

KeePassDX 3.3.0 was not cloned, as ground 0 states.

**What was replaced in what was carried.** `recorded\` but for the emulator logs, `base-memprot\`
and the later records `overwrite\` and `k2a-synchronize\` (section 13.4) is `android-evidence\`
byte for byte, as the measurement's `evidence.sh` left it:
the working directory written `<work>`, the APK directory inside WSL `<dl>`, the earlier
measurement's directory `<spike-work>`. In the two emulator logs, the WSL user's home is written
`<home>` and the crash directory named after that user `/tmp/android-<user>`, in 8 and 9 lines.
The `base-memprot\` dumps hold no path. The scripts' paths are as section 5 describes.

## 11. The overwrite measurement

### 11.1 What it measures

A phone program holds a copy it opened earlier; the file on the device's storage is replaced by a
newer copy, as a synchronizer bringing KeePass's later save would replace it; the program then
saves. `papers\ground\measured-overwrite.md` reports when Keepass2Android 1.15-r6 and KeePassDroid
2.7 write the older copy over the newer file and when they notice, and
`papers\ground\measured-k2a-synchronize.md` cites its sections 1.1, 2.1, 2.2, 2.5, 2.6 and 3. The
four given files are OLD, the first measurement's `S-argon` with X1's notes padded, and three
arriving copies made by the later probe's `makeow`. A run clears the program's data, pushes OLD
alone, runs a flow that opens it, has `replace.sh` put an arriving copy over it at a time the case
names, and does what the case names on the device; every file left in `Download` is pulled and
read back by `verdict.sh` on the Windows side.

### 11.2 Its commands

The bases come from R1, which writes them into `work\base\` beside the first measurement's. The
emulator runs, recorded on 2026-11-15 and 16 and **not run again here**, the emulator being in use,
go under Linux from this directory, after the setup of section 5 (the device was started without
wiping, with the three APKs already installed):

```sh
cd spikes/storage/android
sh scripts/emu-start.sh kpg30 5554 &
sh scripts/wait-boot.sh
sh scripts/check.sh
sh scripts/batch-ow.sh scripts/batches-ow/k2a-first.txt
sh scripts/batch-ow.sh scripts/batches-ow/k2a-main.txt
sh scripts/batch-ow.sh scripts/batches-ow/kpd-main.txt
sh scripts/batch-ow.sh scripts/batches-ow/k2a-window.txt
sh scripts/batch-ow.sh scripts/batches-ow/k2a-nocheck3.txt
sh scripts/pin.sh set
sh scripts/batch-ow.sh scripts/batches-ow/k2a-qu.txt
sh scripts/pin.sh clear
sh scripts/teardown.sh
```

Those are the counted runs. As they ran, `recorded\overwrite\logs\batch-*.txt` shows the order:
`k2a-first.txt`; `k2a-cancel.txt`, whose run failed and was repeated within `k2a-main.txt`;
`after-main.txt`, which is `kpd-main.txt` and `k2a-window.txt` followed by the two lines of
`k2a-nocheck.txt`, both of which failed; `k2a-nocheck2.txt`, which failed again;
`k2a-nocheck3.txt`; `k2a-qu.txt`, whose first two runs failed and which ran again under
`run-pin.sh` with the PIN set. `run-rest.sh` is an earlier sequence of the last two steps. Then, in
Git Bash, the read-back and the evidence:

```sh
sh scripts/verdict.sh work/runs/k2a/* work/runs/kpd/* > out/verdicts.txt
sh scripts/verdict.sh work/not-counted/* > out/verdicts-not-counted.txt
sh scripts/evidence-ow.sh
```

`evidence-ow.sh` writes `out\evidence\overwrite\` for comparison with `recorded\overwrite\`. The
explorations that worked out the screens, `explore.sh` with the `x-*.flow` files, left
`recorded\overwrite\logs\explore\`; `kk.sh` runs `k.py` by hand, `ls-k2a.sh` lists the program's
files, `kill-batch.sh` stops a batch, `interop-test.sh` checks that WSL can run the probe, and
`wsl-tools.sh` and `wsl-tidy.sh` look at WSL before and after. The checks, as run here:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\android\checks\overwrite.py
```

### 11.3 How long it ran

The emulator started at 21:01:55 UTC without wiping (its log, `recorded\overwrite\logs\emu-*.log`:
a full startup, boot completed in 48.7 s). The 36 counted runs ran from 21:07:53 to 01:05:23 UTC,
88 to 264 s each and 86 minutes in all, Keepass2Android's 32 runs 79 minutes and KeePassDroid's 4
runs 7, the uncounted attempts and the synchronize runs of section 12 between them. The checks take
under a second; the later bases of R1, 27 s.

### 11.4 Every number, and whether it reproduced

"The record" is what `checks\overwrite.py` finds the recorded output to show.

| Number | Quoted in | Recorded | Here | Reproduced | The record |
| --- | --- | --- | --- | --- | --- |
| the APKs' hashes `5c446a4f...`, `513b2555...`, `8aaac6a0...`, a verifying signature, one signer, the pinned signer | overwrite 1.1 | `logs\apksigner.txt`, `logs\verify.txt`: three `SIGNER MATCH` | not run | not run | holds |
| the APK each installed package runs from hashes to the pinned value | overwrite 1.1 | not recorded | not run | not run | no, 11.5 |
| file times at +0100; every run from cleared data | overwrite 1.1 | every replacement line; `pm clear` answering `Success` in every counted log | not run | not run | holds |
| OLD: S-argon's lines but X1's notes, 1500 characters; Argon2d, KDBX 4.1; 9301 bytes | overwrite 1.2 | `base\OLD.kdbx.dump.txt` | the same lines, 9301 bytes | yes | holds |
| NEW-diff: RV, M1 at `v1` with its old version in history, X1 deleted, all at 2026-11-01T18:00:00Z; RV's notes 4000 characters | overwrite 1.2 | `base\NEW-diff.kdbx.dump.txt` | the same lines | yes | holds |
| NEW-diff: 11621 bytes | overwrite 1.2 | 11621 | 11621, 11621 and 11605 in three runs | differs, 11.5 | holds |
| NEW-same: the same changes, RV's notes 1309 characters, 9301 bytes, one line from NEW-diff's | overwrite 1.2 | 1309, 9301 | 1309, 1311 and 1312 in three runs; 9301 each time | differs, 11.5 | holds |
| NEW-ident: OLD's bytes, 9301 bytes | overwrite 1.2 | OLD's SHA-256 | the same | yes | holds |
| the opened file given the clock less 600 s; "later" at least 600 s on, "equal" to the nanosecond, "earlier" 3600 s back; each logged before, after and at the pull | overwrite 1.3 | every `given`, `replace:` and `device` line | not run | not run | holds |
| counted: Keepass2Android 32 (21, 7, 2 and 2), KeePassDroid 4 | overwrite 1.4 | 36 logs and read-backs | not run | not run | holds |
| seven attempts not counted, each leaving NEW-diff byte for byte | overwrite 1.4 | `logs\not-counted\`, `logs\verdicts-not-counted.txt` | not run | not run | holds |
| table 2.1, every cell of its 11 rows: 3, 3, 1, 3, 1, 1, 1, 1, 3, 3 and 1 runs | overwrite 2.1 | | not run | not run | 55 of 55 hold |
| the dialog in 18 runs of the table, every run but 2f's three, its text exactly | overwrite 2.1 | | not run | not run | holds |
| "No, overwrite": OLD with the phone's entry and nothing else, in all 18 runs with that answer; "Yes, merge": NEW with it, 3 runs | overwrite 2.1 | | not run | not run | holds |
| OLD's counts KPG 4 entries and G 9; NEW's KPG 5 and G 8 | overwrite 2.1, 2.4, 2.6 | the group view's lines | not run | not run | holds |
| tables 2.2 to 2.4: 1, 3, 2 and 1 runs | overwrite 2.2 to 2.4 | | not run | not run | 17 of 18 hold |
| the lock screen's text in case 3b | overwrite 2.2 | not recorded | not run | not run | no, 11.5 |
| table 2.5: stored `false`, the dialog all the same; four runs stored it false; six runs showed the screen and stored five preferences with nothing pressed | overwrite 2.5 | | not run | not run | 14 of 14 hold |
| table 2.6: the QuickUnlock screen, OLD's counts, the dialog | overwrite 2.6 | | not run | not run | 5 of 5 hold |
| table 3: 3 runs lost, custom data deleted, SettingsChanged the moment of reading; 1 run kept | overwrite 3 | | not run | not run | 10 of 10 hold |
| 18 of 18; 3 of 3, 2 of 2, 4 of 4, 1 of 1; KeePassDroid 3 of 3 and 1 run | overwrite 5 | | not run | not run | holds |

Every other statement of the paper is marked read, a source at `v1.15-r6` or `v2.7`, which no
probe here makes.

### 11.5 Where the record and the paper differ

1. **The installed APKs, overwrite 1.1.** "The APK each installed package runs from hashed to the
   pinned value" rests on what `check.sh` printed to its console; its file `verify.txt` holds the
   downloaded files' verification and not the installed files' hashes, and no other carried file
   holds them. The downloaded files' hashes are in `logs\apksigner.txt`, which `verify-apks.sh`
   writes and which is carried from the working directory for that reason.
2. **The lock screen of case 3b, overwrite 2.2.** The paper quotes "Password-based QuickUnlock not
   available" and "QuickUnlock using a part of your password is blocked because screen lock is not
   activated on your device". The run's log records the screen's buttons only, "Enable screen
   lock", "Close database" and "QuickUnlock!", and no text; the run pressed Close database, as the
   paper reports. The quoted text is in no carried file.
3. **NEW-same's 1309 characters and NEW-diff's 11621 bytes, overwrite 1.2.** `makeow` searches
   for the padding that makes NEW-same exactly OLD's length, and the compressed size moves with the
   random IVs of a save, by a 16-byte block now and then, as section 8.5 found for the first
   bases: made three times here, the search found 1309, 1311 and 1312, and NEW-diff came out 11621,
   11621 and 11605 bytes. NEW-same's length, OLD's, and every other line hold each time.
4. **A reading, not a difference.** "At Save, before anything was written" in table 2.1 rests on
   the Cancel run, after which the file was NEW-diff byte for byte: the logs of the other runs show
   the dialog after the press on Save and nothing about the file at that moment.

## 12. The synchronization measurement

### 12.1 What it measures

Keepass2Android 1.15-r6 opens a database from a WebDAV server through its own "HTTP (WebDav)"
storage, which keeps a cached copy; a second copy, the desktop's later save, is put on the server
while the program holds the first, and the server's file and the cache are read back after the
program's "Synchronize database...", "Synchronize cache copy with source", a plain save or a
reopen. `papers\ground\measured-k2a-synchronize.md` reports it. The server is rclone 1.68.2,
section 14; the two given copies, SOLD and SNEW-fixed, are the later probe's `makesync` and
`newsync`, and a run that changes both sides builds its own SNEW with `mksnew.sh`, from the phone's
cached copy as it is then.

### 12.2 Its commands

The bases come from R1, which writes them into `sync\base\`. The server needs rclone, fetched once
with `sh server/fetch-rclone.sh` (section 14), and the WebDAV password of `server.sh` and
`k2a-sy-open.flow`. The emulator runs, recorded on 2026-11-15 and 16 and **not run again here**,
go under Linux from this directory:

```sh
cd spikes/storage/android
sh server/fetch-rclone.sh
sh scripts/server.sh start
sh scripts/batch-sy.sh scripts/batches-ow/sy-main.txt
sh scripts/batch-sy.sh scripts/batches-ow/sy-main3.txt
sh scripts/batch-sy.sh scripts/batches-ow/sy-main4.txt
sh scripts/batch-sy.sh scripts/batches-ow/sy-main5.txt
sh scripts/run-pin.sh
sh scripts/server.sh stop
```

As they ran, `recorded\k2a-synchronize\logs\batch-sy-*.txt` shows what each batch counted:
`sy-main.txt` cases 1, 2 and 3a, and stopped at the first 3c attempt; `sy-main2.txt` the second
3c attempt; `sy-main3.txt` cases 3c and 3b, and stopped at the first case 4; `sy-main4.txt` the
case 4 runs and cases 5 to 8, the first case 5 in B failing; `sy-main5.txt` case 5 in B; and
`run-pin.sh`, which sets the PIN, runs `k2a-qu.txt` of section 11 and `sy-pin.txt`, case 9, and
takes the PIN away. `run-last.sh` is the last two steps in one. `setprefs.sh` writes the
preferences each batch line names, `srv-put.sh` and `srv-stat.sh` put and log the server's file,
`events.sh` the accessibility events, and `pull-cache.sh` the cache. Then, in Git Bash, the
read-back, KeePass's own Synchronize of the five runs where both sides changed, and the evidence:

```sh
sh scripts/sverdict.sh work/sync/* > out/sverdicts.txt
sh scripts/sverdict.sh work/sync-not-counted/* > out/sverdicts-not-counted.txt
for r in s4-both-cacheonly-merge-r1 s4-both-cacheonly-merge-r2 s4-both-cacheonly-overwrite-r1 \
         s4b-both-serverstopped-merge-r1 s4B-both-cacheonly-bgsync-merge-r1; do
  sh scripts/expect.sh work/sync/$r
done
sh scripts/evidence-sy.sh
```

**R8, run here.** The five `expect.sh` runs need the phone's copy before the synchronize, the run's
SNEW and the server's file, which the branch does not carry. Copies of the three from the
measurement's working directory were put in `work\keepass-sync\<run>\`, and from the branch root,
in Git Bash:

```sh
for r in s4-both-cacheonly-merge-r1 s4-both-cacheonly-merge-r2 s4-both-cacheonly-overwrite-r1 \
         s4b-both-serverstopped-merge-r1 s4B-both-cacheonly-bgsync-merge-r1; do
  sh spikes/storage/android/scripts/sverdict.sh spikes/storage/android/work/keepass-sync/$r
  sh spikes/storage/android/scripts/expect.sh spikes/storage/android/work/keepass-sync/$r
done
```

`compare.py` then holds the 15 dumps of `keepass-sync\` to the record. The checks, as run here:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\android\checks\k2a_synchronize.py
```

### 12.3 How long it ran

The 21 counted runs ran from 22:59:29 to 01:09:34 UTC, between the overwrite runs on the same
emulator, 168 to 558 s each and 103 minutes in all, the uncounted attempts on top. The server's
log, `recorded\k2a-synchronize\server\rclone.log`, shows it started four times. R8 took 50 s here;
the checks, under a second.

### 12.4 Every number, and whether it reproduced

"The record" is what `checks\k2a_synchronize.py` finds the recorded output to show.

| Number | Quoted in | Recorded | Here | Reproduced | The record |
| --- | --- | --- | --- | --- | --- |
| rclone v1.68.2 for linux-amd64, `0e6fa180...a13c`, matched against the published `SHA256SUMS`; the server on port 8080 | synchronize 1.1 | `server\SHA256SUMS`, `server\rclone.log` | the archive the measurement fetched, hashed here: `0e6fa180...a13c` | yes | holds |
| every run opened over "HTTP (WebDav)" at `10.0.2.2:8080/`; the cache, its version files and the local backup copy | synchronize 1.1 | the 21 step logs | not run | not run | holds |
| preferences A in 19 runs, B in 2, each as listed | synchronize 1.1 | each log's `prefs set` line | not run | not run | holds |
| table 1.2, every cell of its 10 rows | synchronize 1.2 | `base\`, the runs' SNEW and the phone's copies | SOLD and SNEW-fixed made again, line for line but the first | yes | 30 of 30 hold |
| SNEW-fixed: Q2 at 2027-01-01, Q3 at 18:00Z; a run's own SNEW: Q2 an hour after the phone's edit, Q3 at the same second | synchronize 1.2 | | SNEW-fixed made again | yes for SNEW-fixed, not run for the runs' | holds |
| counted 21: 12, 6 and 3; ten logs not counted, the exploration among them | synchronize 1.3 | | not run | not run | holds |
| the 20 s of accessibility events after a press, the window's own only | synchronize 2 | | not run | not run | holds |
| table 2, every cell of its 9 rows | synchronize 2 | | not run | not run | 45 of 45 hold |
| in every case 4 and 4b run, the cache the server's file | synchronize 2 | | not run | not run | holds |
| KeePass's own Synchronize of the same two, both directions: everything the program kept, and three differences only | synchronize 2 | `runs\<run>\keepass-sync\` | made again, R8: 15 of 15 dumps | yes | holds |
| table 3, every cell of its 5 rows | synchronize 3 | | not run | not run | 25 of 25 hold |
| table 4, every cell of its 2 rows | synchronize 4 | | not run | not run | 9 of 10 hold |
| table 5, every cell: 10 rows of 8 columns, and the column heads' run counts | synchronize 5 | | not run | not run | 81 of 81 hold |
| KG1 and KG2 in every file of every run | synchronize 5 | 128 read-backs | not run | not run | holds |
| 3 runs of 3 lost the desktop's items; the cache older in 2 of 2; every write KDBX 4.0 without per-key times | synchronize 7 | | not run | not run | holds |

Every other statement of the paper is marked read, a source at `v1.15-r6`, which no probe here
makes.

### 12.5 Where the record and the paper differ

1. **The QuickUnlock screen of case 9, synchronize 4.** The paper quotes the screen as "Enter last
   3 characters of your password:". The run's log shows the screen by its field and its button
   only, `waitfor QuickUnlock_password` and the press on "QuickUnlock!", and lists none of its
   text. The quoted label is in the overwrite record's QuickUnlock runs, on the same release and
   device, `recorded\overwrite\logs\runs\k2a\qu-later-diff-overwrite-r1.log`.
2. **"Every Keepass2Android write ... writes KDBX 4.0", synchronize 7.** It holds for the 32 files
   Keepass2Android wrote in this record. The overwrite record's Keepass2Android saves read back as
   KDBX 4.1 in every verdict line, with the per-key time dropped
   (`recorded\overwrite\logs\verdicts.txt`): the version follows the file, and only the dropped
   per-key time is common to both records.
3. **Two readings, not differences.** "SOLD with ..." and "the phone's copy whole" are compared
   with the access times and usage counts every open changes set aside, and "none" of the phone's
   own changes with the per-key time every Keepass2Android save drops set aside, as the checker's
   comment states.

## 13. The later scripts and probe, and which record came from which

The two later measurements ran in one working directory, which started at 20:57 UTC as a copy of
the first measurement's scripts and flows, so they share every script, flow and the probe. Both
reports name `flows\`, `scripts\` and `probe\` "as they stood at the end"; what is carried is the
working directory's last state, beside the first measurement's.

**What is in two versions, and why.**

- `scripts\k.py` and `scripts\overwrite\k.py`. The later file is the first with five commands
  added, `scrollto`, `tapb`, `checkstate`, `host` and `dialog`, and nothing else changed, so the
  first measurement's flows run the same under either. The first measurement's record came from
  `scripts\k.py`. The later records came from successive states of the later file, whose last
  state is kept: it last changed at 23:15:45 UTC, so the overwrite runs before that time and the
  synchronize runs of cases 1, 2 and 3a ran an earlier state, not kept, which lacked some of what
  the last state has.
- `probe\Store.cs` and `probe\overwrite\Store.cs`, each with its `Probe.csproj`. The later source
  is the first with three commands added, `makeow`, `makesync` and `newsync`, and nothing else
  changed. The first measurement's record came from `probe\`. The later source last changed at
  21:38:45 UTC and was built at 21:39:02: the synchronize bases (21:39:03), every SNEW `mksnew.sh`
  made and every read-back of both later records, all dumped between 01:13 and 01:18, came from
  it; the overwrite bases (20:59:29 to
  20:59:52) came from an earlier state, which had `makeow` and not yet the other two and was not
  kept. Its `makeow` made the bases again here line for line but NEW-same's padding (section 11.5).

**What is in one version.** `env.sh` of the later directory is the family's but for its work
directory, which the first carry changed already; `android.sha256`, `batch.sh`, `emu-start.sh`,
`install.sh`, `k.sh`, `prep.sh`, `root-setup.sh`, `run-A.sh`, `run-B.sh`, `run-op.sh`,
`runs-alike.sh`, `teardown.sh`, `verify-apks.sh` and `wait-boot.sh` are the family's byte for
byte; `collect.sh`, `evidence.sh`, `stage-apks.sh` and `sync.sh`, which only the first measurement
ran, are the family's but for the paths the first carry changed. The 37 flows of the first
measurement are byte for byte the same in the later directory. Added: 31 scripts in `scripts\`, the
16 batch files in `scripts\batches-ow\`, 62 flows in `scripts\flows\`, and `server\fetch-rclone.sh`
(section 14). Some flows changed between attempts, as the not-counted logs show (an earlier
`k2a-ow-setting.flow` pressed the setting with `tap`, the last with `tapb`); each log records what
its run pressed.

### 13.1 Changes made to carry them

The working directory wrote each path in full; each is now this directory, as section 5's are:

- `evidence-ow.sh` and `evidence-sy.sh`: `M` from the working directory to
  `M=$(cd "$(dirname "$0")/.." && pwd)`; `E` from the review's evidence directory to
  `$M/out/evidence/overwrite` and `$M/out/evidence/k2a-synchronize`, so that a run writes beside
  the record rather than over it; the copies of `k.py` and of the probe's source from
  `scripts\overwrite\` and `probe\overwrite\`; and the three patterns that matched the session's
  scratch directory replaced by three that match this directory as Git Bash, WSL and Windows name
  it, `$M`, `/mnt$M` and `Mw`, its Windows form with each backslash doubled.
- `verdict.sh`, `sverdict.sh` and `expect.sh`: `M` the same; the probe from `probe\bin\...` to
  `probe\overwrite\bin\...`. `mksnew.sh`: the probe the same. `interop-test.sh`: its two full
  paths to `$M`, defined the same way, and the probe in `probe\overwrite\`.
- `run-ow.sh`, `run-sy.sh`, `explore.sh` and `kk.sh`: `$P/k.py` to `$P/overwrite/k.py`.
- `probe\overwrite\Probe.csproj`: the `HintPath` of `KeePass` to `$(KeePassExe)`, and its comment
  from "the review4 worktree provides" to "the branch provides", as section 4 did for the first.
- `server.sh` and `flows\k2a-sy-open.flow`: the WebDAV password to `<invented>` (section 14).
- `server\fetch-rclone.sh`: the pin check of section 14.

`Store.cs`, `k.py`, the batch files, every other flow and every other script are unchanged.

### 13.2 What the record holds, and what was replaced

| Directory | From | Files |
| --- | --- | --- |
| `recorded\overwrite\base\` | `android-evidence\overwrite\base\` | 4 |
| `recorded\overwrite\runs\` | `android-evidence\overwrite\runs\`, `not-counted\` among them | 129 |
| `recorded\overwrite\logs\` | `android-evidence\overwrite\logs\`, and two files of the working directory's `out\` | 59 |
| `recorded\k2a-synchronize\base\` | `android-evidence\k2a-synchronize\base\` | 2 |
| `recorded\k2a-synchronize\runs\` | `android-evidence\k2a-synchronize\runs\`, `sync-not-counted\` among them | 384 |
| `recorded\k2a-synchronize\logs\` | `android-evidence\k2a-synchronize\logs\` | 40 |
| `recorded\k2a-synchronize\server\` | `rclone.log` and `SHA256SUMS` of `android-evidence\k2a-synchronize\server\` | 2 |

The two from `out\` are `apksigner.txt`, which `verify-apks.sh` wrote under `check.sh` and which
holds the APK files' hashes that `verify.txt` leaves out, and `emu-kpg30-20261115T210155Z.log`,
the emulator's log for both later measurements.

### 13.3 What was not carried

- **every `.kdbx`**: the four overwrite bases, the two synchronize bases, the 36 files pulled for
  the overwrite runs and the seven of the attempts, every server file, cache, local backup copy,
  SNEW and KeePass result of the synchronize runs, and the file left in the server's directory. A
  database is never carried; its dump is;
- the rclone archive and binary in `server\rclone-dl\`, somebody else's program, which
  `server\fetch-rclone.sh` fetches and the pin holds; its `SHA256SUMS` is in the record;
- `probe\bin\` and `probe\obj\`, build output;
- `out\rclone.pid`, the server's process number;
- `draft-*.md`, the measurement's notes toward its two reports, which no paper quotes;
- from the evidence, its copies of `flows\`, `scripts\` and `probe\` and the fetch script, which
  the working directory's last state replaces: they differ from it in the paths the evidence wrote
  `<work>` and in `wsl-tidy.sh`, written after the copy;
- `android-evidence\keepassdx\` and `measured-keepassdx.md`, a measurement still in progress.

### 13.4 What was replaced in what was carried

`recorded\overwrite\` and `recorded\k2a-synchronize\` are the evidence byte for byte, as
`evidence-ow.sh` and `evidence-sy.sh` left it, the working directory written `<work>` and the APK
directory inside WSL `<dl>`, and the WSL user's home in `server\rclone.log` written `<wsl-home>` by
the measurement after the copy, with one replacement here: the WebDAV password, invented for these
runs, written `<invented>` in the 28 step logs that typed it, `logs\explore\k2a-webdav.log` of the
overwrite record and 21 counted and 6 uncounted logs of the synchronize record. In the emulator log
the WSL user's home is written `<home>` and the crash directory named after that user
`/tmp/android-<user>`, in 6 lines and 1; `apksigner.txt` holds no path. Searched afterwards, no
carried file holds a path of this machine, a user's directory, a session's identifier, the WebDAV
password or a device serial but the emulator's own, `EMULATOR37X2X13X0`.

## 14. Setup, the server, and the pins

**What the family borrowed and now carries.** The first measurement left the emulator's setup to
`spikes/revocation-merge`. Its `sdk-setup.sh` and `avd-create.sh` are carried byte for byte, and
its `fetch.sh` with the download directory moved from `$D/dl/<name>` to `$DL/<name>`, where
`install.sh` and `verify-apks.sh` read, with `mkdir -p` for it since `env.sh` does not make `DL`,
and with its comment naming the place. It reads `scripts\android.sha256` through `env.sh`, which
pins the three releases this branch measures and the SDK, so it fetches those three and no other.
`root-setup.sh`, `wait-boot.sh` and `teardown.sh` were here already, byte for byte the spike's, and
so were `emu-start.sh`, with its `wipe` argument added, and `install.sh` and `verify-apks.sh`,
reading from `$DL`. Nothing a run of this family calls is on that branch any more.
`sdk-setup.sh` installs the newest build-tools `sdkmanager` lists; the measurements ran 37.0.0, as
`apksigner.txt` records, and that version is not pinned.

**The server.** `server\fetch-rclone.sh` fetches rclone v1.68.2 for linux-amd64 into
`server\rclone-dl\`, which `server.sh` serves `server\root\` with. Its archive is pinned in the
root `android.sha256`, in that file's format: the SHA-256
`0e6fa18051e67fc600d803a2dcb10ddedb092247fc6eee61be97f64ec080a13c`, the name and file
`rclone-1.68.2/rclone-v1.68.2-linux-amd64.zip`, `-` for a ZIP that has no signer, and the URL.
That is the hash the record's `SHA256SUMS` publishes for the file and the hash of the archive the
measurement fetched, hashed here. The one change to the script: it refuses an archive whose hash
is not that line's, as well as an archive whose hash is not the published hash, which is all it
checked before.
The binary inside is not pinned; the measurement's hashed here to
`3f08f96554f1902378c0a73b1bdd8cd963bae65db382ce0a41de290be0d45425`.

**The WebDAV password.** `server.sh` passes it to rclone and `curl`, and `k2a-sy-open.flow` types
it into the program's login dialog. It was an invented password, written `<invented>` here as in
the record and the paper, so `server.sh start` and that flow need a password of their own in those
three places before a run: as written, the shell reads `<invented>` as a redirection. The user,
`kpg`, is the project's abbreviation, as in the device's name `kpg30`, and is kept. The screen lock
PIN `pin.sh` sets for the QuickUnlock runs is the emulator's, invented, and kept.
