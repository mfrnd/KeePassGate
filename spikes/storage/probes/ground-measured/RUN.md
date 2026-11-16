# ground-measured: the probe and what it reproduces

The probe behind `papers/ground/ground-measured.md`, which digests what `spikes/revocation-merge`
measured and re-reads the result files its runs left; its section 7 names this directory as the
place of the re-read. In the tables below "measured 3.4" means section 3.4 of
`ground-measured.md`.

| Probe | What it measures | Cited by |
| --- | --- | --- |
| `scripts\reread.ps1` | the result databases of the spike's Android run and of an earlier KeePassXC run, opened with KeePassLib 2.62: every string of every entry, every history item's time, rule and strings, and each database's history limits, `SettingsChanged`, recycle bin and deletions | `ground-measured.md` 1, 2, 3.4 to 3.8, 4.1 to 4.7, 6.3, 7 and 9 |
| `run.ps1`, added here | the agent's console steps around the re-read, rebuilt from the session transcript: the copies, the lists, the counts, the cells, the tally, the hashes and the WSL state | the same sections |
| `checks.py`, added here | the claims the report reads off the re-read, over `out\` and over `recorded\` alike | the same sections |

Everything ran again from the branch on 2026-11-16, on the machine the paper was measured on.
**Every number and outcome the paper quotes from the re-read and the agent's commands came out the
same.** What differs is noise, the moment a database that holds no `SettingsChanged` was read, and
the environment: the WSL distribution that was stopped is running now. The section on claims near
the end lists what the paper presents as measured that no output here shows.

## Running it

From the branch root, in PowerShell 7, with the inputs the sections below describe. On the
measuring machine the commands were:

```powershell
$s = '<scratch of the session that measured spikes/revocation-merge>'
pwsh -NoProfile -File spikes\storage\probes\ground-measured\run.ps1 `
    -DroidWork "$s\droid-measure\work" -XcWork "$s\xc-measure\work" `
    -Downloads "$s\xc-measure\dl,$s\droid-measure\dl"
uv run --no-project python -I -X utf8 spikes/storage/probes/ground-measured/checks.py `
    spikes/storage/probes/ground-measured/out
```

`run.ps1` runs the steps its header lists. Each writes into `out\`, which is not committed, under
the name its recorded copy has in `recorded\`; a step the header marks as added on the branch
writes a file with no recorded copy. `-Only` takes a list of steps and runs them over what an
earlier run left. The databases are copied into `work\data\`, which is not committed either; none
is kept. `checks.py` reads the re-read in the directory it is given, `out\` or `recorded\`, and
prints a line for each claim.

What a run needs: PowerShell 7 for `run.ps1`, which starts Windows PowerShell 5.1 for
`reread.ps1`; `uv` (0.12.23) with a Python it can find; git, with `spikes/revocation-merge` in this
repository, unless `-SpikeOutput` names a copy of its outputs; `wsl.exe` for the `wsl` step.
KeePass 2.62 is provided and checked by `tools\KeePass.ps1`, which `run.ps1` calls. A clone of this
branch alone lacks the other branch, and this fetches it:

```powershell
git fetch origin spikes/revocation-merge:refs/remotes/origin/spikes/revocation-merge
```

About 85 seconds: the re-read 59 (Android 26, fast KeePassXC 1, slow KeePassXC 28 to 32, which the
agent recorded as 23.6), `slow-bases` 22, the rest under 5. `checks.py` takes a second.

## What the agent ran, and that the branch runs the same programs

- **KeePass.** The agent loaded the product worktree's `.keepass\2.62\KeePass.exe`. That file and
  the branch's `.keepass\2.62\KeePass.exe`, which every run here loads, have the same SHA-256,
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`.
- **What it re-reads.** The result files of two runs, which exist only on the measuring machine,
  under the scratch directory of the session that measured `spikes/revocation-merge`: the Android
  run's work directory and an earlier KeePassXC run's. No branch holds them, since no `.kdbx` is
  committed anywhere. The next section describes what that leaves a reader without them.
- **Which PowerShell.** The agent's console commands ran in PowerShell 7, the session's shell, and
  `reread.ps1` in Windows PowerShell 5.1, started by those commands. `run.ps1` runs in PowerShell
  7, because its lists are sorted with `Sort-Object`, which orders paths differently in the two: in
  Windows PowerShell 5.1 the lists and the Android re-read hold the same lines in another order
  (measured here: sorting the fresh lists in PowerShell 7 gives the recorded order).

## What it reads, and what that leaves a reader without the machine

The work directories given as `-DroidWork` and `-XcWork`, and the downloads given as
`-Downloads`:

- **The Android run's work directory**, `A`, `B`, `base` and `base2`, which is what the Android
  scripts of `spikes/revocation-merge` leave in `$KPG_ANDROID_WORK/work`. A new run of that
  measurement, which needs the emulator, writes the same layout, with other times and hashes.
- **An earlier KeePassXC run's work directory**, `A`, `A247`, `base` and `C\<release>\<cell>-r<n>`,
  whose bases a `Kpg.cs` of the measuring session made with the password
  `dummy-password-for-a-throwaway-file`. The KeePassXC script committed on `spikes/revocation-merge`
  writes another layout (`work\save`, `work\merge`) with the password `probe-only`, so a new run of
  it does not feed these lists: the KeePassXC half of the re-read can be repeated only over the
  files that earlier run left.
- **The downloads**, the six KeePassXC archives, KeePass 2.47 and six APKs, which
  `tools\KeePassXC.ps1`, `tools\KeePass.ps1` and the Android scripts provide from their pins.

## The re-read, `scripts\reread.ps1`, and the steps around it

`reread.ps1` opens result databases with KeePassLib 2.62 and prints what the spike's recorded
outputs never printed: every string of every entry, every history item's time, rule and strings,
and the database's history limits, `SettingsChanged`, recycle bin and deletions. The agent ran it
over 95 Android files and 77 KeePassXC and KeePass 2.47 files, and then counted, grouped and hashed
with console commands that `run.ps1` rebuilds, step for step.

**How the fresh run compares with the record.**

- `copy.txt`, `files.txt`, `counts.txt`, `cells-android.txt`, `cells-keepassxc.txt`, `tally.txt`,
  `hash-results.txt`, `hash-downloads.txt`, `xc-reread-fast.txt`, `xc-reread-slow.txt`: identical
  bytes.
- `droid.lst`, `xc.lst`, `xc-fast.lst`, `xc-slow.lst`: identical once `<data>\` stands for the
  copy's directory.
- `droid-reread.txt`: identical but for seven lines, the `SettingsChanged` of the seven files that
  hold none, Keepass2Android's two KDBX 3.1 files and KeePassDroid's five saves, which KeePassLib
  reads back as the moment of reading: `2026-11-15T14:38:49Z` then, `2026-11-15T23:15:18Z` now.
  That is noise by design, and it is the claim of `ground-measured.md` 3.8 coming out again.
- `wsl.txt`: `Stopped` then, `Running` now, and two empty lines more. The environment: the
  emulator measurement of `spikes/storage/android` runs in that distribution today.
- `checks.py` prints the same over `recorded\` as over `out\`.
- `limits.txt` and `xc-reread-slow-bases.txt` have no record: 172 files read, none with other
  limits; the five slow bases carry `SettingsChanged` `2026-11-10T10:23:44Z` (`C-slow`,
  `P-moved-slow`, `P-plain-slow`) and `2026-11-10T10:27:24Z` (`C-slowm`, `P-plain-slowm`).

**The numbers and outcomes `ground-measured.md` quotes from the re-read and the agent's commands.**

| Claim | Quoted in | Recorded | This run | Reproduced |
| --- | --- | --- | --- | --- |
| 95 Android files and 77 KeePassXC and KeePass 2.47 files re-read: 36 fast merge targets, 6 slow merge targets, 21 saves and the bases | measured 2 | `copy.txt` 95 and 220 copied; lists of 95, 71 and 6; `counts.txt` 36 targets, 35 saves and bases; `checks.py`: A 18, A247 3, base 14, C 42 | same | yes |
| The earlier KeePassXC run left 627 files | measured 2 | `files.txt`: 627, and 98 in the Android directory | same | yes |
| The 216 lines of `keepassxc-merge.txt`, grouped by cell: one distinct result per cell | measured 2 | `cells-keepassxc.txt`: 72 cells, each `[variants: 1]` | same | yes |
| All 47 files whose hash the recorded Android outputs print match the kept files | measured 2, 7 | `hash-results.txt`: `match 47` | same | yes |
| All thirteen archives and APKs match their pins | measured 7 | `hash-downloads.txt`: 13 `MATCH` against the pins of `spikes/revocation-merge` | 13 `MATCH` against the branch's pins | yes |
| KeePassXC, every release, both directions: E1 newer and P's, exactly one history item, C's rule, C's time, C's strings: 42 merge results, 36 fast and 6 slow | measured 1 table, 3.4, 4.2 | `checks.py` over `recorded\`: 42 of 42 | 42 of 42 | yes |
| From 2.4.2 KeePassXC's `_LAST_MODIFIED` is inside the history item too | measured 3.4 | `checks.py`: 2.4.1 0 of 4, every later release all of its targets | same | yes |
| Keepass2Android, all 18 merge results: P's rule at P's time, one item with C's rule at C's time, the strings unchanged | measured 1 table, 3.4, 4.2 | `cells-android.txt`, six cells x3 | same | yes |
| KeePassDX 3.2.0 and 4.5.8, all 18 merge results: P's time, the arriving copy's rule; one item at C's time with the current rule; C's rule nowhere with C open, P's nowhere with P open | measured 1, 3.4, 4.2, 9 | `cells-android.txt`: `extCP`, `fromCP` E1 P and item P; `extPC`, `fromPC` E1 C and item C; six cells x3 | same | yes |
| No file has more than one history item | measured 1, 3.6, 4.4 | `tally.txt`: 0 second items | 0 | yes |
| The limits read back as 10 items, 6291456 bytes and 365 days in every file | measured 1, 3.6 | read by eye | `limits.txt`: 172 files, 0 otherwise | yes, no record |
| The title and the 17-character protected password of E1 and E2 in every file | measured 4.1 | `tally.txt`: 0 entries without | 0 | yes |
| Every file has zero deleted objects | measured 4.7 | `tally.txt`: 0 | 0 | yes |
| No attachment and no tag in any file, so neither was ever measured | measured 8 | `tally.txt`: 0 | 0 | yes |
| KeePassXC adds empty `Notes`, `URL` and `UserName` to every existing entry and history item, its time unmoved | measured 3.5, 4.1 | `checks.py`: 120 of 120 E1 and E2 in 60 files, 0 moved | same | yes |
| E1 and E2 keep their modification times in every save and merge, with the same identifiers and parents | measured 3.5, 3.7, 4.1 | `checks.py`: 0 at another time | same | yes for the times; identifiers and parents are in every dump line and were not counted |
| The Android programs leave existing entries' strings alone and give their own entry all five standard fields | measured 3.5 | `checks.py`: 190 of 190, 46 of 46 | same | yes |
| KeePassDroid 2.7's own entry carries one history item, an empty entry about 15 s older, in all five files | measured 3.5 | `checks.py`: 5 of 5, 15 to 16 s | same | yes |
| KeePass 2.47 adds nothing to existing entries | measured 3.5 | `checks.py`: 6 of 6 | same | yes |
| Keepass2Android keeps `droid-merge`, the entry only the open copy held, in all 18 | measured 3.5, 4.6 | `checks.py`: 18 of 18 | same | yes |
| KeePass 2.47, KeePassXC and KeePassDX write back the `SettingsChanged` they were given | measured 3.8 | `checks.py`: no KeePassDX value outside the Android bases; two KeePassXC values outside the bases the lists name | same | yes: the two are the slow bases' own, `xc-reread-slow-bases.txt` |
| Keepass2Android's KDBX 3.1 files, 1.15-r6 as well as 1.08d-r5, and KeePassDroid's saves hold no `SettingsChanged` | measured 3.8, 9 | `checks.py`: those seven read back later than every run | same, at the moment of this reading | yes |
| The WSL distribution `Ubuntu-24.04` exists and is stopped | measured 6.3 | `wsl.txt`: `Stopped` | `Running` | differs: the environment |

**The rest of `ground-measured.md` is the spike's record, not this family's.** Every cell marked
"measured (recorded)" quotes an output under `spikes/revocation/output` of the branch
`spikes/revocation-merge`, with the line of its `RESULTS.md`: the merge rule's 10 cases by 5
methods, the 70 cells of the designs and of the layout, the 35 KeePassXC and KeePass 2.47 saves,
the 4 KDF files, the 216 KeePassXC merge runs, the 27 Android saves and the 37 Android merge runs,
and every outcome the matrix of section 3 draws from them. That branch carries its own probes and
describes how to run them; nothing here re-runs them. `xc-merge` and `hashes` read some of its
outputs, which is how this family checks that they and the kept files agree.

## Claims for which no probe or output can be found

These are the findings the branch cannot yet vouch for. Claims the paper labels "read" or
"assumed" are not listed: their labels already record that nothing ran. The limits come first,
since every claim of the re-read rests on them: the KeePassXC result files of the earlier run exist
only on the measuring machine, and the Android result files can be remade only by a new emulator
run, whose times and hashes differ from the 47 the spike recorded.

| Claim | Quoted in | Why nothing here shows it |
| --- | --- | --- |
| The earlier KeePassXC run's bases were made with the same keys, values, times and rules as `Paired.cs` | measured 2 | a comparison of two sources the agent made by reading; the `Kpg.cs` it names is the measuring session's, not on any branch |

## Changes made to run it here

| Probe | Change | Why |
| --- | --- | --- |
| `scripts\reread.ps1` | none; the command adds `-ExecutionPolicy Bypass` | the agent's call relied on the machine's execution policy |
| `run.ps1` | added: the agent's console commands, each a step, with only the paths turned into parameters | the agent typed them; the transcript holds them |
| `run.ps1`, `copy` | the copies go to `work\data\` rather than `data\` | `work\` is where the contract puts databases; the names the re-read prints are relative to it and come out the same |
| `run.ps1`, `files` | the agent's first two counts of that command only | the rest listed the measuring session's own directories |
| `run.ps1`, `counts` | the agent's command without its last part, a search of the report's prose for dashes | that part read the report, not the results |
| `run.ps1`, `xc-merge`, `hashes` | the spike's recorded outputs are read with `git show spikes/revocation-merge:...` rather than from its worktree | the branch's history holds them; `-SpikeOutput` takes a directory instead |
| `run.ps1`, `downloads` | the pins are the branch's `keepass.sha256`, `keepassxc.sha256` and `android.sha256` rather than those of `spikes/revocation-merge` | the branch carries its own; they hold the same hash for each of the thirteen files, and the output is the same with either |
| `run.ps1`, `wsl` | the agent's `wsl.exe -l -v` without the `Get-Content` of `k.py` the same command line printed first | that part read a source file |
| `run.ps1`, `limits` | added | the history limits of every re-read file, which the report states and the agent read by eye |
| `run.ps1`, `slow-bases` | added: `reread.ps1` over the five slow bases | no list of the agent's names them, and the `SettingsChanged` of the slow merge targets is traced to them |
| `checks.py` | added | the claims the report reads off the re-read, checked over `out\` and over `recorded\` alike |

A defect in a probe that changes a result: none found. **The tally's `FAILED` count of 1 is a match
on a path**, the directory `extPC-r1.failed-overflow` of a run the Android measurement set aside,
not a file that failed to open: every file the lists name opened. The report does not quote that
count.

**What was scrubbed.** The recorded lists named every database by its full path under the session's
scratch directory; that prefix became `<data>\`, which is `work\data\` in a run here. Everything
else in `recorded\` is either the agent's file byte for byte (the re-reads `droid-reread.txt`,
`xc-reread-fast.txt` and `xc-reread-slow.txt`) or console output the session transcripts hold,
kept as printed (`copy.txt`, `files.txt`, `counts.txt`, `cells-android.txt`, `cells-keepassxc.txt`,
`tally.txt`, `hash-results.txt`, `hash-downloads.txt` and `wsl.txt`), none of which names a path of
the machine.

## Not carried

- **Downloads**: the KeePassXC archives, KeePass 2.47 and the APKs `downloads` hashes, which the
  branch's tools and the Android scripts provide from the pins.
- **Databases**: the 315 copies under the agent's `data\`, which a run here copies again into
  `work\data\` from the directories it is given.
