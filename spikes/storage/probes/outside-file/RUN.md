# outside-file: the probes and what they reproduce

The family behind `papers/options/outside-file/`: the design (`design.md`), its two critiques
(`critique-survival.md` and `critique-security-and-fit.md`) and the digest (`digest.md`), which
quotes the same numbers. Two papers ran probes, each kept in the directory of the paper that ran
it, as `probes/README.md` lays out, in the layout its agent gave it:

| Directory | Probe | What it measures | Cited by |
| --- | --- | --- | --- |
| `design\` | `probe\Kp.cs` and `probe\Store.cs`, one program, `Probe.exe` | the two keys the design leaves in the file under KeePass 2.62's KeePassLib, five merge methods both ways; a prototype of the sealed store: its join, its checks and its cost | `design.md` §1, §4 to §9, §13, §16, §18, §20; `critique-survival.md` §1 to §3, §5; `critique-security-and-fit.md` credits, problems 6 and 7; `digest.md` §2 to §5 |
| `design\` | `probe\Run-Xc.ps1`, `probe\Run-Xc2.ps1` | `keepassxc-cli add` and `merge -s` of 2.4.2, 2.6.6, 2.7.12 and 2.8.0 over the same databases, read back by `Probe.exe describe` | `design.md` §1, §13, §18, §20; `critique-survival.md` §1 to §3; `digest.md` §2 |
| `design\` | `probe\Replace.ps1` | `File.Replace` while another handle holds the file, in PowerShell 7 | `design.md` §9.1, §20; `digest.md` §4 |
| `survival\` | `scripts\Survival.cs`, `Survival.exe` | the design's prototype brought to its text in three places, plus letting go at expiry, through three sequences over a modeled synchronizer | `critique-survival.md` §1, P1, P2, P8, §5; `digest.md` §3 |
| `survival\` | `scripts\Run-XcSavedStale.ps1` | a KeePassXC save of a copy with no custom data, then that copy merged into a copy that holds the two keys, four releases | `critique-survival.md` §1, §3, P6, P12, §5, §6; `digest.md` §2, §5 |

The security critic ran no probe of its own. Its working directory held copies of `Kp.cs` and
`Store.cs`, byte for byte the design's, and the three binaries to build them against, but no
program; its one measurement is the hash of `KeePass.exe`, checked below as F1.

Every probe ran here, and every outcome the papers quote from them came out as recorded: the
store's 21 checks, its size and the question of which time a write moves, the KeePassLib merges,
the KeePassXC saves and merges and the replace under a reader in the design's directory, and the
three sequences and KeePassXC's own saves in the survival critic's. Each output matches its record
line for line once the paths, timestamps and random values a run writes afresh are set aside. What
came out differently is the store's timings, which move with the load, the seconds each KeePassXC
merge took, and the lines of `xcsaved.txt` that the recorded run printed only because of the
directory it ran in. Four claims presented as measured have no run behind them, or a run that
measures something else, and one of them, that a holder of the store key cannot undo a revocation,
is contradicted by the design's own text; four sentences of the draft papers quote the KeePassXC
result without its condition. Both are listed apart at the end.

## Running them

From the branch root, in Windows PowerShell 5.1, with PowerShell 7 (`pwsh`) on the path:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\outside-file\design\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\outside-file\survival\run.ps1
```

Each `run.ps1` provides and checks KeePass 2.62 through `tools\KeePass.ps1` and each KeePassXC
release through `tools\KeePassXC.ps1`; builds its program with `dotnet build`, which needs the .NET
SDK and the packages `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3, `BouncyCastle.Cryptography`
2.7.0 and `Newtonsoft.Json` 13.0.4; clears the part of `work\` it is about to fill; and writes
`out\`. The branch ignores both. The recorded output each run is compared with is in `recorded\`
beside it, under the same file names. Nothing shows a window: KeePassLib is used as a library and
`keepassxc-cli` runs as a console child process with no window. `-Part` runs some parts only,
written with no space, as `-Part store,keepass`: `store`, `keepass`, `keepassxc` and `replace` for
`design\`, where `keepassxc` reads the databases `keepass` makes in `work\kp`, and `store` and
`keepassxc` for `survival\`.

**Two shells, as the probes were measured.** `Run-Xc.ps1`, `Run-Xc2.ps1` and
`Run-XcSavedStale.ps1` set `ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not
have, so in Windows PowerShell 5.1 they stop at their first call to `keepassxc-cli`; the agents ran
all three in PowerShell 7, and `Replace.ps1` measured .NET 11 there and names it in its first line.
The run scripts therefore start those four with `pwsh` and do everything else in Windows
PowerShell 5.1, which is what the tools are written for.

How long each part ran here, in one run of each, with the machine busy with other measurements. A
build takes one to two seconds before each. The recorded runs did not time themselves; where the
stamps KeePassXC wrote and the files' times allow, the table gives what they imply.

| Run | Here | Recorded |
| --- | --- | --- |
| `design`: `store` (`scenarios`, `timing`) | 2.0 s, 5.3 s | not stated |
| `design`: `keepass` (`make` with the slow KDF, `make` with the fast KDF, `kpmerge`) | 97.0 s, 0.3 s, 1.6 s | not stated; about 80 s for the slow `make` by the files' times |
| `design`: `keepassxc` (`Run-Xc.ps1`, `Run-Xc2.ps1`) | 506.8 s, 261.8 s | about 7 minutes and 3.5 minutes by the stamps |
| `design`: `replace` | 0.5 s | not stated |
| `survival`: `store` | 3.5 s | not stated |
| `survival`: `keepassxc` (its `make`, then `Run-XcSavedStale.ps1`) | 257.8 s: about 100 s for its `make` and 152.9 s for the script | about 2 minutes for the script by the stamps, its bases made before |

**How a result is judged.** "Yes" is that this run gives what the paper quotes. "Differs" is that
it gives something else, with the note stating whether that is noise, the environment or the claim.
"No" is that neither the record nor this run gives what the paper states. Where the record and this
run agree and the paper's conclusion holds, but its sentence is broader than the output or passes
over a cell, the result is "yes" and the note names the part that is not in the output.

## The design's probe, `design\`

`Probe.exe` takes a verb and a work directory. `make <dir> <iterations> <MiB> <lanes>` makes
`base.kdbx` with two entries and copies it into `old` (no custom data); `anchored` (both keys,
written through KeePass's public setter, so each with a time, and saved as 4.1); `anchored2`
(`anchored` with a third entry); and `anchored-notimes` (`anchored` with both times taken away
through KeePassLib's internal setter, and an entry added), and prints each with `describe`.
`kpmerge` merges eight ordered pairs of those under five methods, saves each result and reads it
back; `other` makes `old-other`, `old` with `Other.Plugin=1`; `scenarios` runs the store's 21
checks; `timing` measures its cost. The authority document is a stand-in of 2903 `A` and the store
key a fixed value, so `describe` prints `authority(2903)` and `store(same)` when both are as
written, and `@t` or `@none` for a key's time. `Run-Xc.ps1` and `Run-Xc2.ps1` run `keepassxc-cli`
over copies of `work\kp`, made with Argon2d, 200 iterations, 64 MiB and 2 lanes, the target loaded
first; that `new Argon2Kdf()` is Argon2d in KeePass 2.62 was checked here by reflection.
`kpmerge` ran over `work\kpfast`, made with 2 iterations, 1 MiB and 1 lane. Output: `out\make.txt`,
`out\make-fast.txt`, `out\kpmerge.txt`, `out\scenarios.txt`, `out\timing.txt`, `out\xc.txt`,
`out\xc2.txt` and `out\replace.txt`.

All paths below are under `papers/options/outside-file/`.

**The store's prototype**, `scenarios.txt` and `timing.txt`:

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | the store's 21 checks all hold: one writer; a revocation made while the other installation was behind; a revocation another installation asserts, which cannot be taken away; two versions of a rule refuse, are settled and are superseded; names added and taken off at once; standing answers; a file put back older, deleted, changed by a bit, renamed, and opened under another key | `design.md` §1 item 4 ("21 checks"), §20; `critique-security-and-fit.md` "What the design gets right" item 4; `critique-survival.md` §5 first row | `scenarios.txt` | 21 lines `ok` | the same 21 lines | yes | the synchronizer is the probe's `Store.Sync`, which copies the file with the higher `head` between two writers of distinct names; that it is "a correct merge" holds for that, and `critique-survival.md` P1 is the case it does not cover |
| D2 | the join: a revocation made on one installation while the other was behind held on both and could not be taken away from the other; a name added on one installation and taken off on the other, neither knowing, was off, and on again once added knowing of the removal; `forever` beat a lift made at the same time; a rule changed on both refused on both, was settled by one write and was taken over by a later change that knew of it | `design.md` §6; `critique-survival.md` §2, the rows on two versions of a rule and on a revocation another installation asserts; `critique-security-and-fit.md` "What the design gets right" item 4 | `scenarios.txt` 2 to 5 | `ok` | the same | yes | "an emptied record wins" in the security critique is not reached by these, see N2 |
| D3 | a writer's file put back as it was one write earlier, and a writer's file deleted, each closed the store, through what this installation saw and through what the other writer's file recorded; the right file back opened it again | `design.md` §1 item 6, §6; `critique-survival.md` §2, the last row | `scenarios.txt` 6 and 7 | `closed` with both reasons, then `serves` | the same | yes | the deletion was tried only on an installation that had seen the store, which prints both reasons; the installation with no seen file was given only the older file (7). The prototype keeps a count per writer and no digest (`Store.cs:113`), so check 3's digest is not measured, as both critics state. "Closes the gate (`503 undecided`) and is named on the tab": the prototype prints `closed`; no gate and no tab is in it |
| D4 | one bit changed, a file copied under another writer's name, and the right files opened under another key were each refused | `design.md` §5.2; `critique-survival.md` §2, the last row | `scenarios.txt` 8 | `ok`, each `does not open` | the same | yes | |
| D5 | 200 rules, 30 records, 30 revocations and 10 names over three writers: 81,764 bytes in three files | `design.md` §5.3, §20; `critique-security-and-fit.md` problem 6 ("~82 KB"); `digest.md` §5 | `timing.txt` | `three writer files, 270 keys, 81764 bytes in all` | the same | yes | the records and revocations are stand-ins of 819 and 85 characters, and the size rests on them |
| D6 | a whole load and join: 1.9 ms | `design.md` §8, §20; `digest.md` §5 | `timing.txt` | 1.889 ms | 2.476 ms; 2.22 ms to 2.48 ms over five runs | differs | timing, noise: 200 loads in memory on a machine busy with other work |
| D7 | the per-request look at the folder: 50 microseconds, local NTFS | `design.md` §8, §20, §21.7; `critique-security-and-fit.md` problem 7; `digest.md` §5 | `timing.txt` | 50.2 microseconds | 62.5 microseconds; 58 to 69 over five runs | differs | timing, as D6 |
| D8 | a lookup in the image: 26 ns | `design.md` §8, §20 | `timing.txt` | 26 ns | 37 ns; 25 ns to 37 ns over five runs | differs | timing, as D6 |
| D9 | a write with both loads: 18.6 ms | `design.md` §9.1, §20; `digest.md` §5 | `timing.txt` | 18.60 ms | 21.20 ms; 20.3 ms to 21.4 ms over five runs | differs | timing, as D6; this write also flushes to the disk |
| D10 | a replace moves the folder's time, a write in place moves only the file's | `design.md` §8, §20 | `timing.txt` | `True`, `False`, `True` | the same | yes | each after a pause of 1.1 s, on NTFS |
| D11 | AES-256-GCM over 1 MiB: seal 8.3 ms, open 8.8 ms | `design.md` §5.2, §20 | `timing.txt` | 8.3 ms, 8.8 ms, the same bytes back | 8.9 ms, 10.3 ms, the same bytes back; seal 8.6 ms to 10.8 ms and open 8.9 ms to 10.6 ms over five runs | differs | timing, as D6; one call each |

**The two keys under KeePassLib 2.62**, `make.txt`, `make-fast.txt` and `kpmerge.txt`:

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D12 | written through KeePass's public setter each key gets a time and the file is 4.1; with both times taken away it stays 4.0 | `design.md` §4, §13 (KeePass 2.62, "a save"), §16 | `make.txt`, `make-fast.txt` | `anchored: kdbx 4.1 ... @t`; `anchored-notimes: kdbx 4.0 ... @none` | the same 5 lines each | yes | the times are taken away through KeePassLib's internal `StringDictionaryEx.Set(string, string, DateTime?)`, by reflection, which is the call `design.md` §16 proposes the plugin make |
| D13 | eight ordered pairs, five methods: 40 cells, both keys present and unchanged in 38; the other 2 are `old` as the open copy under Keep existing, which copies nothing; never removed, never changed | `design.md` §13 (KeePass 2.62), §20; `critique-survival.md` §2, §3 (KeePass 2.62); `digest.md` §2 | `kpmerge.txt` | `cells 40, both keys present and unchanged in 38` | the same 42 lines | yes | "unchanged" is the values: `into anchored2 from anchored-notimes, OverwriteExisting` keeps both keys without their times, as a 4.0 file. The merges ran over `work\kpfast` (2 iterations, 1 MiB, 1 lane), not the slow `work\kp`; `design.md` §20 does not state which |
| D14 | in all 30 cells whose open copy held the keys both stayed; a copy that lacked them received them in 8 of 10 | `design.md` §13 (KeePass 2.62) | `kpmerge.txt` | 30 of 30; 8 of 10 | the same | yes | |
| D15 | written without times, both keys were still kept by every method in both directions | `design.md` §4, §16 | `kpmerge.txt`, the 20 `anchored-notimes` cells | kept in the 15 whose open copy held them; arrived in 4 of the 5 into `old` | the same | yes | in the cells into `old`, "kept" is that they arrived, Keep existing excepted, as in D14 |
| D16 | a KeePass synchronize brings the keys back to a copy that lacks them | `design.md` §13 (KeePass 2.62), §18.1; `critique-survival.md` §1, §2; `digest.md` §2 | `kpmerge.txt`, `into old from anchored` and `into old from anchored-notimes` | arrived under every method but Keep existing | the same | yes | measured through KeePassLib into `old`, a copy that never had custom data; not into a copy a KeePassXC merge emptied, which still holds `Other.Plugin` or KeePassXC's own keys, and not through KeePass's window, as `design.md` §21.3 states |

**The two keys under `keepassxc-cli`**, `xc.txt` and `xc2.txt`. Each line holds what KeePassXC
writes fresh every time, a random `KPXC_RANDOM_SLUG`, a `_LAST_MODIFIED` stamp and the seconds the
merge took; with those three set aside, this run's lines are the recorded lines.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D17 | a save keeps both keys, drops their times, writes 4.0 and adds `_LAST_MODIFIED`, and from 2.7.12 `KPXC_RANDOM_SLUG`: 4 of 4 releases | `design.md` §13 (KeePassXC); `critique-survival.md` §2, §3; `digest.md` §2 | `xc.txt`, the four `save` lines | as quoted | the same | yes | the save is `keepassxc-cli add` of one entry |
| D18 | one save and six merges a release, 28 cells: both keys present and unchanged in all; the per-key times dropped by every save | `design.md` §20 | `xc.txt` | 28 of 28 | the same 28 lines | yes | a merge that brought nothing new left the file reading as before, with no `_LAST_MODIFIED`: into `anchored` (4.1, with its times) and into `anchored-notimes` from `old` in every release, and into `anchored2` from `anchored` from 2.6.6 on. "Every save" is the cells that were saved |
| D19 | both copies hold the keys: kept, 12 of 12; a stale copy with empty custom data: the target's keys kept, 8 of 8; into a copy that lacks them: they arrive, 8 of 8 | `design.md` §13 (KeePassXC) | `xc.txt`, `xc2.txt` | 12, 8 and 8 | the same | yes | the 8 that arrive are 4 into `old` in `xc.txt` and 4 into `old-other` in `xc2.txt` |
| D20 | into a copy that holds them, from a stale copy that lacks them and holds another key: both removed, 8 of 8, all four releases | `design.md` §1, §13 (KeePassXC), §18.1, §20; `critique-survival.md` §2, §3; `digest.md` §2; the draft papers, Q1 to Q3 | `xc2.txt` | 8 of 8: `custom data: Other.Plugin=1@none _LAST_MODIFIED=...`, and `KPXC_RANDOM_SLUG` from 2.7.12 | the same 8, with their own slugs and stamps | yes | that the gate then closes is the design's reading (§11), not part of the probe |
| D21 | into `old-other` from `anchored`: both keys arrive and `Other.Plugin` is removed, 4 of 4 | `design.md` §20 | `xc2.txt` | 4 of 4 | the same 4 | yes | |
| D22 | the slow KDF puts the two loads of a merge in different seconds | `design.md` §20 | `xc.txt`, `xc2.txt`, the seconds of each merge | 5.2 s to 11.4 s | 5.3 s to 13.6 s | yes | the loads' own times are not printed; that they fell in different seconds is read from how long a merge took |

**A replace under a reader**, `replace.txt`:

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D23 | `File.Replace` is refused while a reader holds the file without sharing deletion (`Read`, `ReadWrite`) and goes through when it does (`Read, Delete`, `ReadWrite, Delete`); in PowerShell 7 on .NET 11, not .NET Framework | `design.md` §9.1, §20; `digest.md` §4 | `replace.txt` | as quoted, `PowerShell 7.7.0 (.NET 11.0.0)` | the same 5 lines, `PowerShell 7.7.0 (.NET 11.0.0)` | yes | that .NET Framework does the same is `[A]` in the paper and is not run here either |

## The survival critic's probe, `survival\`

`Survival.exe` is the design's prototype with the changes `critique-survival.md` §6 lists: only
files named `<16 hex>.kpgw` are read, the seen file keeps a digest beside each count and refuses a
different digest at an equal count, one `Apply` writes several keys, and a write lets go of its own
records and revocations whose certificate has ended by the writer's clock. Its synchronizer copies
a file changed on one side, and for a file changed on both keeps the newer and renames the other
`<name>.sync-conflict-20261115-120000-DEVICE.kpgw` on both sides, the rule Syncthing documents; no
synchronizer runs. It takes a work directory and prints three sequences. `Run-XcSavedStale.ps1`
saves a copy of `old.kdbx` once with each release's `keepassxc-cli add`, merges it into a copy of
`anchored.kdbx` with the target loaded first, reads both back with the design's
`Probe.exe describe`, and prints the merge's own lines that match `custom|Custom|Benutzer|KPG`.
Output: `out\store-sequences.txt` and `out\xcsaved.txt`.

**The store's three sequences**, `store-sequences.txt`:

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | P1, one writer name on two computers over a synchronized folder: after the conflict, pc1 is closed, "`a1b2c3d4e5f60718` at 2 is not the file this computer saw at that count"; pc2, a new pc3 with no seen file, and pc1 after taking a new name all admit `c1`, which pc1 had revoked; the revocation is only in the conflict copy, which nothing reads | `critique-survival.md` §1 item 1, P1 steps 4 to 7, §5 (copying between replicas, check 3, a synchronized folder); `digest.md` §3 item 5 | section 1 | as quoted | the same 8 lines | yes | the synchronizer is the model's, after the rule Syncthing documents; none was run. Without the digest pc1 would serve too: see N3 |
| S2 | P2, a fast clock: once B, whose clock is two months ahead, writes anything, A admits `c2`, which B had revoked; with B's clock right, A refuses it | `critique-survival.md` §1 item 2, P2 steps 1 to 5, §5 ("No time is read", "a record that came back"); `digest.md` §3 item 4 | section 2 | `ADMITTED`; the control `refused` | the same 3 lines | yes | "and so on every computer whose clock is right" is A alone in the model |
| S3 | P8, one file rolled back: on C, a computer with no seen file, B's previous file beside A's passes every check and C serves `reader: [read, list]`; A, which had loaded B's newer file, is closed when given the older file | `critique-survival.md` P8, §5 (what the checks cannot see); `digest.md` §3 item 8 | section 3 | as quoted | the same 3 lines | yes | |

**KeePassXC over a copy it saved itself**, `xcsaved.txt`:

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S4 | the bases: `old.kdbx` 4.0 with no custom data, `anchored.kdbx` 4.1 with both keys and their times | `critique-survival.md` §6 | the first line | as quoted | the same | yes | made fresh by `Probe.exe make`, where the critic copied the design's |
| S5 | 2.4.2 and 2.6.6 save a copy with no custom data and add nothing to it; merged into the present copy, both keys are kept | `critique-survival.md` §3 (2.4.2 and 2.6.6, save and merge), P6, §5 (the window); `digest.md` §2 | the `save` and `merge` lines of 2.4.2 and 2.6.6 | `(none)`; both keys kept, `_LAST_MODIFIED` added | the same | yes | |
| S6 | 2.7.12 and 2.8.0 add `KPXC_RANDOM_SLUG` and `_LAST_MODIFIED` even to that empty dictionary, and the merge removes both keys, 2 of 2 | `critique-survival.md` §1, §3 (2.7.12 and 2.8.0, save and merge), P6, §5 (the window); `digest.md` §2; the draft papers, Q4 | the same lines of 2.7.12 and 2.8.0 | as quoted | the same | yes | |
| S7 | the merge report prints each removed key with its value, the store key in clear, and the authority document the same way | `critique-survival.md` P12 (the aside); `digest.md` §5 | the merge's lines of 2.7.12 and 2.8.0 | the store key's line with `{"v":"1","key":"..."}`, the authority's with `<2903 x A>` | the same two lines for each release, in an order that varies between runs | yes | |
| S8 | each merge took 9.5 s to 13.6 s, so the two loads fell in different seconds | `critique-survival.md` §6 | the seconds of each merge | 9.5, 9.6, 11.4 and 13.6 s | 10.8, 10.9, 12.2 and 8.9 s | differs | timing, noise: every merge still took several seconds, so the conclusion holds; as D22, the seconds are read from the duration |

## The security critic, `security-and-fit\`

Nothing of its own was run besides a hash, and nothing is carried; the measurements it quotes are
the design's, and their rows above name it where it quotes them.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F1 | the KeePass 2.62 it read against matches the worktree's, `KeePass.exe` SHA-256 `AFB89B74...C89C3F3` | `critique-security-and-fit.md`, "How statements are marked" | none was written | `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3` for the copy in the critic's `bin\` and in the design's `probe\out\` | the same for `.keepass\2.62\KeePass.exe` | yes | `(Get-FileHash .keepass\2.62\KeePass.exe -Algorithm SHA256).Hash` from the branch root; the same file both probes ran against |

## Quoted in the draft papers

The working papers that compare the options quote this family's probes too. Their sentences are
checked against the same outputs.

| # | Quote | Quoted in | Output | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| Q1 | a KeePassXC merge of a copy from before Create or Recreate removes the authority's key, "8 of 8 in the `outside-file` probe" | `papers/draft/EVALUATION-draft.md` §3.1 | `design\recorded\xc.txt`, `xc2.txt`; `survival\recorded\xcsaved.txt` | no, as worded | the 8 of 8 (D20) are copies whose custom data holds another key. A copy from before Create whose custom data is empty kept both keys, 8 of 8 in `xc.txt` (D19) and in the two releases of `xcsaved.txt` that leave it empty (S5). The sentence holds for any such copy that KeePassXC 2.7.12 or 2.8.0 has saved (S6); a copy from before Recreate, which holds the old keys, is not run here |
| Q2 | "A KeePassXC merge from a copy that lacks the two file keys removes them, 8 of 8 [M option]" | `papers/draft/EVALUATION-draft.md` §3.9 | the same | no, as worded | as Q1: the condition that the copy holds another key is dropped |
| Q3 | "In database custom data it is removed by a KeePassXC merge of any copy from before Create or Recreate [M spike; M option `outside-file`]" | `papers/draft/EVALUATION-draft.md` §5.1, the table's first row | the same | no, as worded | as Q1 |
| Q4 | "KeePassXC 2.7.12 and later adds timed keys of its own to every file it saves [M critic outside-file-survival]" | `papers/draft/completeness-of-draft.md`, "Statements that are wrong", item 5 | `xcsaved.txt`, `xc.txt` | no, as worded | both releases added `KPXC_RANDOM_SLUG` and `_LAST_MODIFIED` in every save measured, but each with no time (`@none`): every file they wrote here is 4.0, which holds no per-key time. Whether a 4.1 save stamps them is not measured here |

## Claims no probe here backs

**Presented as measured, with no run behind them, or with a run that measures something else.**
These are what the branch cannot vouch for from this family.

| # | Claim | Quoted in | What was found |
| --- | --- | --- | --- |
| N1 | a holder of the store key who can write the folder "cannot undo a revocation, because a revocation is add-only in the join", limiting the forger "to existing clients' reach and never to resurrection"; "Leaked store key plus folder access: wider rules until Recreate, no revocation undone [M design]" | `critique-security-and-fit.md` "What the design gets right" item 4 and problem 8; `digest.md` §3 item 10 | **Not what the probe measures, and contradicted by the design.** Scenario 2 (D2) runs an installation writing through the store's own `Set` and `Remove`, which cannot take away a line another writer asserts. Nothing in the probe writes as a holder of the key can: seal the revoking writer's file again under that writer's name, with a higher `head` and without the revocation and the emptied record. Every check of `design.md` §6 accepts such a file, the digest compared only at an equal count included, and the issuer's record line then admits the client again. `design.md` §2 itself states it: whoever holds the key "can forge the store: bring a revoked client back". Not run here either |
| N2 | "an emptied record wins", cited with `out\scenarios.txt` 21 of 21 | `critique-security-and-fit.md` "What the design gets right" item 4 | No scenario reaches the branch of the join for two record lines that do not know of each other (`Store.cs:205-210`): in scenario 2 the emptying line knows the record line, so the record line is dropped before that branch. The rule is read from the code, not measured |
| N3 | "without it pc1 serves too", of the digest rule, in the sequence marked "[M here, model]" | `critique-survival.md` P1 | The model always keeps the digest; no run leaves it out. It follows from the design's text, which reads only `<16 hex>.kpgw`. The design's own prototype, which reads every `.kpgw`, would close both computers on the conflict copy instead, which does not open under its name. Not run here either |
| N4 | a KeePassXC merge removes the keys "loaded a second or more later" | `digest.md` §2 | Every KeePassXC run here uses the slow KDF so that the loads fall in different seconds (D22); none varies the gap. The condition is the spike's measurement, carried by `ground-measured` |

**Measured by another family, and not run here.** The papers also quote measurements of the
ground reports, which their own families carry, all under `spikes\storage\probes\`:
`[M keepass]` in `ground-keepass\`, `[M keepassxc]` in `ground-keepassxc\`, `[M others]` in
`ground-others\`, `[M spike]` and `ground-measured.md` in `ground-measured\`, and
`[M inventory]` (the 2903 characters of an authority document, which the probe's stand-in copies)
in `ground-inventory\`. What the digest calls `M android` is in `spikes\storage\android\`, and the
hardened-custom-data critic's measurement it quotes in §5 is in `hardened-custom-data\`. Claims
marked `[R ...]` or `[A]` are reading and assumption, and no probe is owed for them. The line
numbers the critiques read in `Store.cs` (`:69-104`, `:113`, `:171-176`, `:186-211`, `:189-193`,
`:212`, `:281-294`) point at the lines they describe in the carried file.

## Changes made to run them here

- **No probe source was changed but for paths.** `Kp.cs`, `Store.cs` and `Survival.cs` are byte for
  byte the files that produced the recorded output. A comment in `Survival.cs` names the prototype
  by its place in the agents' directory, `storage\outside-file\probe\Store.cs`, which is
  `design\probe\Store.cs` here; it is left as it was.
- **`Run-Xc.ps1` and `Run-Xc2.ps1`**: the two lines naming the agent's directory and the KeePassXC
  downloads, and the table of four `keepassxc-cli.exe` paths under them, are replaced by
  `$root = Split-Path -Parent $PSScriptRoot`, a dot-source of `tools\KeePassXC.ps1` and a table of
  the same four releases filled by `Get-KeePassXCCli`. Nothing else changed.
- **`Run-XcSavedStale.ps1`**: the same, with `$me` for `$root`, and the full path of the design's
  `Probe.exe` replaced by `..\design\probe\out\Probe.exe` from the survival directory.
  Dot-sourcing the tool sets `Set-StrictMode -Version Latest` in the three scripts' scope; they ran
  under it unchanged.
- **`Replace.ps1` is new as a file.** The design's agent ran it as one inline command in PowerShell
  7 and kept only its output. It is taken from that agent's transcript as it ran, with the paths of
  its work directory and of its output made relative to the script, its last line, which printed
  the output of another run, left out, and four lines of comment added at the top to record this.
- **A project file for each program**, `Probe.csproj` and `Survival.csproj`, beside its sources.
  Neither had a project file: each was compiled with the .NET Framework's own `csc`, `/platform:x64
  /optimize+` for `Probe.exe` and no platform for `Survival.exe`, against copies of `KeePass.exe`,
  Bouncy Castle 2.7.0 and Newtonsoft.Json 13.0.4. The projects build the same sources for `net48`
  under the names they had, `Probe.exe` for x64 into `probe\out\`, where the three scripts look for
  it, and `Survival.exe` for any CPU into `bin\`. Only `Probe.csproj` references `$(KeePassExe)`.
  The two libraries are the packages and versions the product at `a42e38aa` references; their
  `net461` and `net45` assemblies have the same SHA-256 as the copies the probes ran with. The
  compiler is therefore the SDK's, at C# 7.3, where the recorded runs used the Framework's C# 5
  compiler; the sources need nothing newer.
- **A `run.ps1` for `design\` and for `survival\`**, which provides KeePass and KeePassXC through
  the tools, builds, and runs each part with the arguments of the recorded run, taken from the
  agents' transcripts, over full paths under `work\`. For `survival\` it also makes the two bases
  with `Probe.exe make <dir> 200 64 2`, where the critic had copied them from the design's
  `work\kp`, and then applies the critic's own shortening of runs of `A`, the expression the critic
  ran over its output file after the run.

## Recorded output

Copied as the runs left it, but for machine paths. The five design outputs `Probe.exe` wrote held
the full path of the agent's working directory in their first line, a session scratch directory
under a user's profile; it is replaced by `spikes\storage\probes\outside-file\design`. `xcsaved.txt`
held the survival agent's directory in ten places, in what `keepassxc-cli` printed, replaced by
`spikes\storage\probes\outside-file\survival`. Nothing else in them changed. `xc.txt`, `xc2.txt`,
`replace.txt` and `store-sequences.txt` held no machine path, user name or session identifier and
are byte for byte the originals. `xcsaved.txt` is the file as the critic left it after shortening
each run of 40 or more `A` to `<2903 x A>`. Parts of it are in German, as `keepassxc-cli` 2.7.12
and 2.8.0 answered on the machine that ran it, and so is this run's.

## Not carried

- **Every database**: 127 `.kdbx` files of the design's runs and 10 of the survival runs, all made
  by the probes, which make them again.
- **The sealed store files** the two prototypes wrote, 10 and 20, which every run writes again.
- **The build output**: `Probe.exe` and `Survival.exe`, rebuilt from the sources, and the copies
  of `KeePass.exe`, Bouncy Castle and Newtonsoft.Json beside them, which the branch provides with
  the same hashes.
- **The security critic's directory**: copies of `Kp.cs` and `Store.cs` byte for byte the design's,
  the same three binaries, and an empty `out\` and `work\`.
- **The KeePassXC releases** the scripts ran, from the `keepassxc` ground report's downloads: the
  branch provides the same four, and their `keepassxc-cli.exe` has the same SHA-256.

## Caveats and defects

- **No defect was found that changes a quoted result.** The design's prototype differs from the
  design's text in three places that both critics name: its seen counts keep no digest, it reads
  every `.kpgw` rather than only `<16 hex>.kpgw`, and it writes one key a write. No result the
  design quotes depends on them, and the survival model is the prototype with them closed.
- **The KeePassXC cells rest on the two loads of a merge falling in different seconds**, which the
  slow KDF arranges and nothing checks (D22, S8). Here every merge took at least five seconds. On a
  machine fast enough to load both copies within one second, D20 and S6 could come out otherwise,
  and the output would not show why.
- **The filter of `Run-XcSavedStale.ps1` depended on the machine.** It keeps the merge's lines that
  match `custom|Custom|Benutzer|KPG`, and PowerShell's `-match` ignores case, so in the recorded run
  every line holding the agent's working directory was kept too, since that path matches the
  pattern once case is ignored. Here the path matches nothing, so those lines are not printed: the
  merge's lines are empty for 2.4.2 and 2.6.6, and for 2.7.12 and 2.8.0 they hold the two removed
  keys without the messages that follow them, which name the two files and ask for the password.
  The lines S7 quotes match on `KPG` itself and are printed either way.
- **The timings of D6 to D9 and D11** are single runs with no gate around them, on a machine busy
  with other measurements. They are an order of magnitude, not a number to compare designs by.
- **The culture shows.** The probes format numbers with the decimal comma and `keepassxc-cli` 2.7.12
  and 2.8.0 answer in German, because the machine they ran on uses `de-DE`, here as before. Nothing
  quoted depends on it.
