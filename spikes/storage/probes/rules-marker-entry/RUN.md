# Probes of `rules-marker-entry`

The option keeps every access rule as an ordinary entry, a rule entry, inside the group it governs,
written once and never changed, with two add-only records beside it. Three papers measured it, and
each has its probe here, in the directory `tools/Copy-Papers.ps1` gave its paper:

| Directory | Paper | Probe |
| --- | --- | --- |
| `design\` | `papers/options/rules-marker-entry/design.md`, called "design" below | `Rme.exe` and `scripts\Run.ps1` |
| `survival\` | `papers/options/rules-marker-entry/critique-survival.md`, "survival" | `Rms.exe` and `scripts\Run.ps1` |
| `security-and-fit\` | `papers/options/rules-marker-entry/critique-security-and-fit.md`, "security-and-fit" | `Rmc.exe` |

`papers/options/rules-marker-entry/digest.md`, "digest", quotes the other three and measured
nothing of its own. No formal critic wrote a probe for this option. Paths below are relative to
this file's directory unless they start at the branch root.

**Every probe ran again for this file, and every number the papers quote from it came out the same,
except the timings, two sentences of the design that its own record contradicts, and one block of a
survival output that an earlier version of its probe wrote.** The table near the end has each.

## Before running

From the branch root, in PowerShell 7 (`pwsh`):

- KeePass 2.62 in `.keepass\2.62\`: `. .\tools\KeePass.ps1; Install-KeePass`. The build stops with
  that command when it is missing.
- KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0: each `Run.ps1` asks `tools\KeePassXC.ps1` for them,
  which provides a missing release from `keepassxc.sha256` and checks its signature every time.
- The .NET SDK, for `dotnet build`, and `uv`, for the Python steps. The NuGet package
  `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 is restored by the build.

**PowerShell 7, not Windows PowerShell 5.1.** Both `Run.ps1` scripts set
`ProcessStartInfo.StandardInputEncoding`, which .NET Framework does not have, so under 5.1 they stop
at their first call to `keepassxc-cli`. The recorded outputs carry no byte order mark where
`Set-Content -Encoding utf8` wrote them, which 5.1 always writes, so they were made under
PowerShell 7 as well. These runs used PowerShell 7.7.0, the .NET SDK 10.0.401 and `uv` 0.12.23,
on Windows 11 with the regional format `de-DE`. That format matters in one place, which "Defects and
dependencies found" names.

**Where the files go.** A probe's build output is its project's `probe\out\`. A run's outputs go to
the paper directory's `out\`, beside the `recorded\` they are compared with, and its databases to
`work\`. All three are gitignored, and nothing here keeps a `.kdbx`: every run makes its own.

## The design's probe, `design\`

**What it measures.** `Rme.exe make` builds one base database with invented content (Argon2d, 2
iterations, 1 MiB) and two copies, A and B: the root and 16 groups, nine of them ruled at the base
and three holding a ruled entry, each group a different way two copies come to disagree about a
rule. `Run.ps1` merges the copies with KeePassLib 2.62 under all five methods in both directions and
with `keepassxc-cli merge -s` of four releases in both directions, chains the two, runs KeePassXC
back and forth, saves with KeePassXC, runs KeePass's Delete Duplicate Entries, edits and deletes
rule entries by hand, and reads every result back with the design's read path. `check.py` holds
what a client with the profile `reader` gets at each of the 20 nodes against the last decision
made there; `summarize.py` and `compact.py` fold the results by scenario; `Rme.exe cost` times the
read path on an invented database of 10,402 entries in memory. Cited by design 1, 2, 3, 6, 7, 9, 11
and 14, and through it by both critiques and the digest.

**Command**, from the branch root:

```powershell
pwsh -NoProfile -File spikes\storage\probes\rules-marker-entry\design\scripts\Reproduce.ps1
```

**Output.** `out\merges.txt`, `cost.txt` and `dump-A.txt` from `Run.ps1`; `out\summary.txt`,
`compact.txt` and `check.txt` from the three Python scripts; `out\dump-A-body.txt` and
`dump-xcsave-<release>.txt` from `Rme.exe dump`. The record of each is in `recorded\` under the
same name.

**How long.** 25 s here, the build included. The record does not state how long it took.

**Changes made to run it here.**

1. `probe\Rme.csproj`: the reference to KeePass named `.keepass\2.62\KeePass.exe` inside the
   product's worktree by its full path; it names `$(KeePassExe)`, which
   `spikes\storage\Directory.Build.props` defines as the same file, of the same hash
   (`AFB89B74...C89C3F3`).
2. `scripts\Run.ps1`: `$Root` was a full path into the author's scratch directory, and is now the
   directory above the script. The four paths into the KeePassXC ground report's downloads are now
   `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1`, which provides the same four
   releases, pinned by archive hash and checked for the publisher's signature. Dot-sourcing that
   file sets strict mode in the script's scope, and the script ran clean under it.
3. Added, not changed: `scripts\Reproduce.ps1`, which runs in order the steps the author typed by
   hand, the build, `Run.ps1`, the three Python scripts and the five dumps, as the timestamps of the
   recorded outputs order them. `Program.cs`, `check.py`, `summarize.py` and `compact.py` are as the
   author left them.

## The survival critique's probe, `survival\`

**What it measures.** The design's write path and read path, copied into `Rms.exe` unchanged, under
the sequences the critique walks: a rule repeated after it was lost or replaced (`s1` to `s4`), two
rule entries in force and one of them deleted by hand (`s5`), the rule entry and its record deleted
with KeePassLib, with pykeepass and by a simulated KeePassDX merge (`s6`), an import with new
identifiers and then the tab's Remove (`s7`), the root's rule entry binned (`s8`), and the bin
pointer decided by a time (`s9`). `Run.ps1` adds what needs `keepassxc-cli` 2.4.2 and 2.8.0 and
pykeepass. Cited by survival P1 to P6, P10 and sections 3 to 5, and by the digest.

**Command**, from the branch root:

```powershell
pwsh -NoProfile -File spikes\storage\probes\rules-marker-entry\survival\scripts\Reproduce.ps1
```

**Output.** `out\s1-s3.txt`, `s5-s9.txt` and `s8.txt` from `Rms.exe`, then `out\run.txt` from
`Run.ps1`, which reads the databases `s1` left in `work\s1`. The record of each is in `recorded\`.

**How long.** 12 s here, the build included. The record gives no time.

**Changes made to run it here.**

1. `probe\Rms.csproj`: the reference to KeePass, as for the design.
2. `scripts\Run.ps1`: `$Root` and the two KeePassXC paths, as for the design.
3. `probe\Program.cs`, line 6, a comment: the path of the design's probe in the author's scratch
   directory is now its path on this branch. Nothing that runs changed.
4. Added, not changed: `scripts\Reproduce.ps1`, which runs the build and the three runs of `Rms.exe`
   the author typed by hand, before `Run.ps1`, which needs the first of them.

`Run.ps1` runs `scripts\pk_delete.py` through `uv run --no-project --with pykeepass`, with no
version. The survival paper names pykeepass 4.2.0 and the record does not print it; this run's `uv`
resolved 4.2.0 as well.

## The security and fit critique's probe, `security-and-fit\`

**What it measures.** The design's identifier derivation, write step and read path, copied into
`Rmc.exe`, under the four ordinary ways a rule entry is lost, each followed by the tab's Save:
moved to the bin, with the bin before and after the group in the tree, then KeePass's own repair of
duplicate identifiers by reflection; deleted for good, then three merge methods in both
directions; duplicated, then Delete Duplicate Entries and Remove; edited by hand, then Remove. And
the root's rule entry deleted for good. Cited by security-and-fit S1 and M2, and by the digest.

**Command**, from the branch root:

```powershell
pwsh -NoProfile -File spikes\storage\probes\rules-marker-entry\security-and-fit\scripts\Reproduce.ps1
```

**Output.** `out\collision.txt`; the record is `recorded\collision.txt`.

**How long.** 4 s here, the build included. The record gives no time.

**Changes made to run it here.**

1. `probe\Rmc.csproj`: the reference to KeePass, as for the design.
2. Added, not changed: `scripts\Reproduce.ps1`, which builds the probe and runs it with `work\` as
   its argument and its output into `out\collision.txt`, as the author did by hand.

The comment at the top of `Program.cs` names the design's probe as
`rules-marker-entry\probe\Program.cs`, the scratch layout; on this branch that file is
`design\probe\Program.cs`. It names no machine and is left as written.

## Comparing a run with the record

From the branch root, once the three commands above have run:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\rules-marker-entry\compare.py
```

`compare.py` was written for this branch and measures nothing. It compares each file of
`recorded\` with the file of the same name in `out\`, first as written, then with three things
masked that a run cannot repeat: identifiers of 32 hex digits and their 8-digit prefixes, because
every probe gives its groups random identifiers and a rule entry's identifier is derived from its
group's; timings, which it prints side by side instead; and the path in the first line of
`dump-A.txt`. It also checks the one claim that is an equality of files, the dumps of copy A before
and after each KeePassXC save.

What it printed for this run:

| Output | Against the record |
| --- | --- |
| `design\merges.txt`, `check.txt`, `summary.txt`, `compact.txt` | identical, byte for byte |
| `design\dump-*.txt` | the same once identifiers are masked; each KeePassXC save left every KPG entry of A as it was, in the record and in the run |
| `design\cost.txt` | the same counts; the timings differ, see D17 to D21 |
| `survival\s1-s3.txt`, `s8.txt`, `run.txt` | the same once identifiers are masked |
| `survival\s5-s9.txt` | the same once identifiers are masked, except the `s8` block, see V15 |
| `security-and-fit\collision.txt` | the same once identifiers are masked |

The counts the table below quotes from `merges.txt` that `check.txt` and `compact.txt` do not print
were taken with `Select-String` over `design\out\merges.txt`, with the pattern given in the row.

## What the papers quote, and whether it came out the same

"Recorded" is the value in `recorded\`, "this run" the value in `out\` after the commands above. A
result of 28 is one of the 28 results of merges that match identifiers: 8 KeePassLib, 8
`keepassxc-cli`, 4 chains, 4 files of 2 back and forth cells, 4 hand deletions after a merge.

| # | Claim | Quoted in | Probe and output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | 16 groups and the root, nine ruled at the base, three holding a ruled entry; 20 nodes judged | design 1, 14 | `Rme`: `Program.cs` `Scenarios`, `WithBaseRule`, `EntryRuled`; `merges.txt` "inputs" | 16, 9, 3, 20 | same | yes | read off the source and the inputs cell |
| D2 | 560 verdicts over merges that match identifiers, none wider than the last decision | design 1, 14; security-and-fit "What was checked" 5; digest 2 | `Rme`: `check.txt` | 26 cells, 560 verdicts, wider 0 | identical | yes | 26 cells are 28 results because each back and forth cell reads two files |
| D3 | Section 14 table: KeePassLib 8 results, 160 verdicts; `keepassxc-cli` 8, 160; chains 4, 80; back and forth 4, 80; hand deletion 4, 80; all 0 wider; Create new IDs 2, 76, 4 wider | design 14; digest 2 | `Rme`: `check.txt` "by kind" | KeePassLib `[10, 236]` less the 2 cells and 76 verdicts of new identifiers; the rest as quoted | identical | yes | |
| D4 | Create new IDs: 4 of 76 verdicts wider, each the open copy's own rule (G02, G03, G14x, G15, A into B) | design 6, 11, 14; digest 2, 3 | `Rme`: `check.txt` | 4 | 4 | yes | |
| D5 | A KeePassXC save added nothing to a rule entry: every KPG entry identical in identifier, group, time, auto-type switch, icon, strings and flags, four releases | design 2.1, 6 | `Rme`: `dump-A-body.txt` against `dump-xcsave-<release>.txt`; no `:strings` suffix in the `add` cells of `merges.txt` | equal, 4 of 4 | equal, 4 of 4 | yes | checked by `compare.py`. The dump prints no history |
| D6 | The same rule made in two copies is one entry after every merge (G04, G05), with one history item when the times differ (G04) and none at a tie (G05) | design 2.1, 6 | `Rme`: `merges.txt`, pattern `^  G0[45]-` in the 28 results | G04 `[X-G04; rule:narrow:hist1]` 28 of 28; G05 `[X-G05; rule:narrow]` 28 of 28 | same | yes | |
| D7 | A rule entry copied by hand, and every rule entry an import with new identifiers copied, fails its check and closes its group | design 2.1, 2.5, 7 | `Rme`: `merges.txt` cells `A-handcopy`, `CreateNewUuids` | each `UNBOUND` copy's group `REFUSE (an entry here claims to be a rule and is not one)` | same | yes | |
| D8 | G02's rule text widened by hand: G02 refuses; G11's title and notes changed: no effect | design 2.1, 7 | `Rme`: `merges.txt` "hand edit" | G02 `REFUSE`, G11 `narrow` | same | yes | |
| D9 | G12: A's narrowed entry rule in force in all 28 results, beside B's password in 26 and A's in 2 | design 2.2 | `Rme`: `compact.txt` G12x | 8 and 18 with `pw:changed`; `KP>A:Keep` and `KP>B:Overwrite` with `pw:invented` | identical | yes | the two are KeepExisting into A and OverwriteExisting into B, as the design states |
| D10 | An import with new identifiers puts the arriving root's entries in the open root and its groups beside the originals; the root refuses, and so does every copy | design 2.3, 6, 11 | `Rme`: `merges.txt` both `CreateNewUuids` cells | as quoted | same | yes | the open copy's own nodes that inherit from the root refuse as well |
| D11 | KeePassXC applies no deletion: a replaced rule entry stays or comes back beside the rule entry in force in 8 of 8 merges and after three merges back and forth | design 2.4, 6 | `Rme`: `compact.txt` G01, `sealed +old` | 8 of 8 merges, 4 of 4 back and forth files | identical | yes | |
| D12 | After each of four KeePassXC merges, deleting the rule entry in force for good leaves the group refusing | design 2.4, 7 | `Rme`: `merges.txt` "deleted for good by hand" | G01 `REFUSE` 4 of 4 | same | yes | |
| D13 | G08: a group moved in one copy while the other changed its rule ends "in every merge, at the new place with the new rule" | design 2.5 | `Rme`: `compact.txt` G08 | new rule 28 of 28; new place 25 of 28 | identical | **no** | in KeePassLib OverwriteIfNewer, OverwriteExisting and KeepExisting of A into B, G08 stays at the root, its old place, with the new rule (`compact.txt` G08 row `narrow`). The record shows the same: the sentence is wrong, not the run. Design 7's "the rule moves with it" holds in all 28 |
| D14 | G14: reading the whole tree gives the narrower of the two rules in all 28 results | design 3.1 | `Rme`: `compact.txt` G14x | 25 in H and 3 in G14, all the narrower | identical | yes | that the group alone "would have given one of them" was not run, see the list below |
| D15 | G02, G03, G15: every merge gives `{"default":"inherit","ops":[],"reader":["read"]}` | design 3.4 | `Rme`: `compact.txt` | 28, 28, 28 | identical | yes | |
| D16 | Cost database: 10,402 entries, 501 groups, 5,000 deletion records, 101 ruled groups, 100 ruled entries; one check is SHA-256 over 123 bytes | design 3.5 | `Rme cost`: `cost.txt` | as quoted | same | yes | |
| D17 | The pass alone: 1.6 to 1.8 ms | design 3.5 | `Rme cost`: `cost.txt` | 1.798 ms | 1.755 to 2.386 ms over six runs | differs | environment: the machine was running other measurements, at about 42 percent processor load. The record holds one run; the quoted range came from runs not kept |
| D18 | Building the view: 3.0 to 3.2 ms | design 3.5; security-and-fit M4; digest 2, 5 | `Rme cost`: `cost.txt` | 3.066 ms | 3.703 to 6.109 ms | differs | as D17 |
| D19 | Checking one rule entry: 1.6 microseconds | design 3.5 | `Rme cost`: `cost.txt` | 1.63 | 1.65 to 2.59 | differs | as D17 |
| D20 | A listing of every entry with each group's document remembered: 23 to 24 ms | design 3.5 | `Rme cost`: `cost.txt` | 24.300 ms | 26.656 to 33.739 ms | differs | as D17. The one recorded value is above the quoted range |
| D21 | About 0.3 microseconds an entry | design 3.5 | derived from D18 over 10,402 entries | 0.29 | 0.36 to 0.59 | differs | follows D18 |
| D22 | KeePass Synchronize: replaced rule entries gone from both copies, a binned rule entry in the bin in both, both directions alike | design 6 | `Rme`: `compact.txt`, cells `KP>A:Sync` and `KP>B:Sync` | in the same row for every scenario, never `+old` | identical | yes | |
| D23 | Overwrite if newer, Overwrite existing, Keep existing: into the copy that replaced a rule as Synchronize; into the stale copy the replaced rule entry stays beside the new rule entry | design 6 | `Rme`: `compact.txt` G01, G08, G11 | `+old` only in the stale copy | identical | yes | |
| D24 | G11: the group's time and an entry moved later in the other copy have no effect on the rule entry | design 6 | `Rme`: `compact.txt` G11 | `narrow` in 28 of 28 | identical | yes | |
| D25 | G10: a group deleted for good in one copy while the other changes its rule comes back with the new rule entry and none of its older entries | design 6 | `Rme`: `merges.txt`, pattern `^  G10-` | KeePassLib: `[rule:narrow]` in the 9 results the deletion reached; the 3 merges into B other than Synchronize never removed the group; `keepassxc-cli`: the older entry comes back in 16 of 16 | same | yes | the row stands among KeePass's and holds there; KeePassXC applies no deletion, as its own row states |
| D26 | KeePassXC merges: the rule or the narrower in all 8 merges and both round trips; a rule entry deleted for good comes back and stays dead (G07); a rule entry arriving in an ordinary group named like the other copy's bin counts, with the old rule (G06) | design 6 | `Rme`: `check.txt`; `compact.txt` G06, G07 | as quoted | identical | yes | |
| D27 | A KeePassXC merge, then KeePass Synchronize with the other copy: what KeePassXC brought back is deleted again, 4 chains | design 6 | `Rme`: `compact.txt`, cells `+KPsync` | 4 of 4, never `+old` | identical | yes | |
| D28 | A rule entry moved to the bin: the node refuses, in KeePassLib and with `keepassxc-cli` 2.4.2 and 2.8.0 | design 7 | `Rme`: `merges.txt` G06 on A; `rm` cells, G11 | `REFUSE` | same | yes | "moving it back out restores the rule" was not run |
| D29 | A rule entry deleted for good: the node refuses by its `ruled` record, same programs | design 7 | `Rme`: `merges.txt` G07 on A; second `rm` cells | `REFUSE (ruled, and no rule entry is left)` | same | yes | the 2.8.0 cell depends on the machine's language, see below. Emptying the bin ran in the survival probe, V6 |
| D30 | A rule entry moved to another group stays in force, `keepassxc-cli` 2.8.0 | design 7 | `Rme`: `merges.txt` `mv` cell | G01 `wide`, its rule entry in General | same | yes | |
| D31 | A ruled entry moved: its rule stays in force and the rule entry stays behind (G13) | design 7 | `Rme`: `compact.txt` G13x | as quoted | identical | yes | |
| D32 | Delete Duplicate Entries: 1 deleted with a hand copy, refusal before and after; 0 of 60 on B, although five groups held the same rule, two with the same `KPG.After` | design 7 | `Rme`: `merges.txt` "DeleteDuplicateEntries" | 1; 0 of 60; `narrow` at G04, G05, G08, G09, G10 of B | same | yes | `KPG.After` is not printed; that G04 and G05 share it (`none`, both first rules) is read off the source |
| D33 | 34 of 46 files are KDBX 4.0 and 12 are 4.1, all 12 having passed through KeePassXC 2.7.12 or 2.8.0 | design 9 | `Rme`: `merges.txt`, pattern `^# (\S+) kdbx (\d\.\d)` | 34, 12 | same | yes | the 46 are evaluations of 43 files, since the hand steps on B read the same file again |
| D34 | "Every file KeePassLib 2.62 ... wrote in this run is 4.0" | design 9 | `Rme`: `merges.txt` | 4 KeePassLib files at 4.1 | same | **no** | the two chains after a 2.8.0 merge and the two hand deletions after such a merge were written by KeePassLib, as 4.1. The record shows the same |
| D35 | Each `keepassxc-cli` validly signed by DroidMonkey Apps, LLC | design 14; survival 5 | `Rme`: `merges.txt` "signatures"; `Rms`: `run.txt` | Valid, 4 and 2 | same | yes | `tools\KeePassXC.ps1` checks the same before every run |
| V1 | s4: A's first narrowing and B's later narrowing get one identifier, and A's second rule names it | survival P1a | `Rms`: `run.txt` s4 | `True`; `667B0E12`, `B0D38130` | `True`; other identifiers | yes | the identifiers are derived from a random group identifier and change on every run; the relation does not |
| V2 | P1a: in all ten results N grants `reader` read and list, wider than the narrower of the two | survival P1a; digest 2, 3 | `Rms`: `run.txt` s4 | 10 of 10 | 10 of 10 | yes | the digest's "6 merges" in KeePass are the 6 KeePassLib results of the 10 |
| V3 | P1a, P10: Synchronize keeps the replaced entry and drops its deletion record | survival P1a, P10 | `Rms`: `run.txt` s4 | `deletionRecords=0`, the entry there | same | yes | so do OverwriteIfNewer and KeepExisting |
| V4 | P1b s2: the save writes the deleted identifier and N refuses; another text grants read and list | survival P1b | `Rms`: `s1-s3.txt` | as quoted | same | yes | |
| V5 | P1b s3: the save writes the replaced first identifier and N refuses | survival P1b | `Rms`: `s1-s3.txt` | as quoted | same | yes | |
| V6 | P1c s1: one identifier twice; a KeePassLib save and reopen reads N as narrow; KeePass's repair renames the copy in the bin when N comes first and N's when the bin comes first, closing N; Empty Recycle Bin then makes N refuse | survival P1c, 3 | `Rms`: `s1-s3.txt` | as quoted | same | yes | the repair was called by reflection, not through KeePass's window |
| V7 | P1c: `keepassxc-cli` 2.4.2 and 2.8.0 keep one of the two, where the later entry was: N reads in one order and refuses in the other | survival P1c | `Rms`: `run.txt` s1 | as quoted, both releases | same | yes | 2.4.2's `ls -R -f` exits 1 in the record and the run; nothing quotes it |
| V8 | P2 s5a: two rule entries met to read; the narrow rule entry binned or deleted: read and list | survival P2 | `Rms`: `s5-s9.txt` | as quoted | same | yes | |
| V9 | P2 s5b: met to read in both directions; the rule entry left in G binned: read and list | survival P2 | `Rms`: `s5-s9.txt` | as quoted | same | yes | |
| V10 | P3 s7: after the import the root and both copied groups refuse, and F with them; after Remove the copies of E and s1 grant read and list while the originals stay sealed | survival P3; digest 3, 4 | `Rms`: `s5-s9.txt` | as quoted | same | yes | |
| V11 | P4 s6: refused; read and list with pykeepass; read and list after a KeePassDX-style merge; read after a `keepassxc-cli` merge | survival P4; digest 3 | `Rms`: `run.txt` s6 | as quoted | same | yes | the KeePassDX row is the probe's simulation, as the paper states; pykeepass 4.2.0 resolved here |
| V12 | P5 s8: the root, Q and Q's entry refuse; P keeps its rule | survival P5; digest 4 | `Rms`: `s8.txt` | as quoted | same | yes | |
| V13 | P6 s9: A refuses at N; Synchronize either way: N grants read, the old bin an ordinary group that clients reach | survival P6 | `Rms`: `s5-s9.txt` | as quoted | same | yes | |
| V14 | Section 3: Empty Recycle Bin removes with records and, with P1c, kills the live twin | survival 3 | `Rms`: `s1-s3.txt` | as quoted | same | yes | |
| V15 | The `s8` block inside `s5-s9.txt` | quoted nowhere | `Rms`: `s5-s9.txt` | a root holding the design's "says-nothing" entry | a root granting read and list | differs | the probe changed after that file was written (`Program.cs` at 17:23:00, `s5-s9.txt` at 17:22:34): the recorded block came from an earlier `S8` that is not on the branch. The current `S8` is what `s8.txt` holds, and P5 quotes that |
| F1 | S1, rule entry binned: Save writes the binned entry's identifier, duplicates; narrow until KeePass's repair, which closes the group when the bin comes first and keeps the rule when it comes after | security-and-fit S1 | `Rmc`: `collision.txt` | as quoted | same | yes | |
| F2 | S1, deleted for good: Save writes the deleted identifier and the group still refuses; Synchronize, Overwrite if newer and Keep existing, both directions, drop the record and the rule comes back | security-and-fit S1 | `Rmc`: `collision.txt` | 6 of 6 | 6 of 6 | yes | |
| F3 | S1, copied then Delete Duplicate Entries: the original deleted, the copy kept; after Remove, Save writes the original's identifier and the group still refuses | security-and-fit S1 | `Rmc`: `collision.txt` | as quoted | same | yes | the original is the older by a second, and it is the entry deleted: the paper's "because it prefers the older" means prefers to delete it |
| F4 | S1, edited by hand then Remove: Save writes the removed identifier and the group still refuses | security-and-fit S1 | `Rmc`: `collision.txt` | as quoted | same | yes | |
| F5 | M2: the root's rule entry deleted for good; Save writes the deleted identifier and the root still refuses | security-and-fit M2 | `Rmc`: `collision.txt` | as quoted | same | yes | |

## Claims no probe here can vouch for

Quoted in the papers as measured, or as resting on a run, with no probe or output on this branch
that holds them:

- **security-and-fit, "What it costs", and digest 5: "19 host test files hold 101 references to the
  rule's custom data key or the dialog's copy", 39 of them in `GrantEditorInRealDialogsTests`, 18 in
  `TagOfTheCallersViewTests` and 7 in `SkippedOnlyForAReaderTests`.** A search of the product, of
  which neither the pattern nor the output was kept. The closest search found, run from the branch
  root at the product's commit, gives the three named counts exactly but **18 files and 99
  references**, all under `KeePassGate.Host.Tests`:

  ```powershell
  git grep -c -E 'AclDocument\.CustomDataKey|m_sdCustomData|NodeFormCustomDataField' a42e38aa -- tests
  ```

- **design 7: a rule entry moved to the bin and "moving it back out restores the rule".** No run
  moves a binned rule entry back out. It follows from the read path.
- **design 7: a copied rule entry, "removing the copy reopens it".** Every run that removes a copy
  had lost the original first. It follows from the read path.
- **design 7: "Duplicates a ruled group ... the copy refuses".** No run duplicates a group; only the
  import with new identifiers ran, D10. Design 13 lists Duplicate Group as still to be measured.
- **design 3.1: reading only the node's group "would have given one of them" at G14.** The probe
  reads the whole tree and nothing else.
- **design 13: "a 4,223 character `KPG.After`".** Arithmetic, 128 identifiers of 32 digits and 127
  commas, not a measurement.

## Defects and dependencies found

None of these was fixed, since each probe stays as its numbers were measured.

- **The design's `Run.ps1` depends on the machine's language.** KeePassXC 2.8.0 names the recycle
  bin it creates in the language of the regional format, `Papierkorb` on the machine that recorded
  the outputs and on this machine, and the script names that bin literally for the second `rm` of
  G11's rule entry. Elsewhere that `rm` would find no entry, and the two cells after it, D29's 2.8.0
  half and D30's file, would read differently. No merge cell depends on it, so `check.txt` would
  not change. 2.4.2 named its bin `Recycle Bin` here.
- **Both `Run.ps1` scripts need PowerShell 7**, as noted above, which the papers do not state.
- **The survival `Run.ps1` cannot run on its own on a fresh copy:** it writes `out\run.txt` without
  making `out\`, and it reads `work\s1`, which only `Rms.exe s1` makes. `Reproduce.ps1` runs that
  first.
- **`pk_delete.py` runs whatever pykeepass `uv` resolves**, and nothing records which. It was 4.2.0
  here, the version the paper names.
- **Two sentences of the design are contradicted by its own record**, D13 and D34, and its quoted
  timings come from runs that were not kept, D17 to D20.

## What was not carried, and why

From the design's working directory:

- `scripts\patch.py`, `patch2.py` and `patch3.py`: edits the author applied to `Program.cs`,
  `Run.ps1` and `check.py` before the recorded run. The carried files are their result, `check.py`
  edited once more by hand after `patch3.py` rewrote it, and running a patch again would change the
  probe.
- `scripts\Hand28.ps1`: a second removal by KeePassXC 2.8.0, run once and appended to `merges.txt`
  at 16:51. `Run.ps1` makes the same step itself through `$binName`, and rewrote `merges.txt` at
  16:55 without that cell, so no recorded output comes from this script.
- `out\compact2.txt`: an earlier fold, made at 16:54 by a version of `compact.py` that is not the
  carried version, from a `merges.txt` that was later rewritten. No paper cites it.
- `work\`: 51 databases, and the eight logs of `keepassxc-cli merge`, which no paper cites and
  every run writes again.

From all three: `probe\out\` and `probe\obj\`, the build output, which held a copy of `KeePass.exe`
and its two companion files, and the survival and security and fit `work\` directories, 54 and 2
databases. And from outside them: the KeePassXC releases the scripts ran, in the KeePassXC ground
report's downloads, which `tools\KeePassXC.ps1` now provides from the same pinned archives.

## Machine paths replaced

In the sources, as listed under each probe: the full path of the product worktree's KeePass in the
three projects, the scratch directory in both `Run.ps1` scripts, and the scratch path in one comment
of the survival `Program.cs`. In the recorded outputs, one line: the first of
`design\recorded\dump-A.txt` named the dumped file by its full path in the author's scratch
directory, which held the user name and the session identifier, and now names it by its path on
this branch, `spikes\storage\probes\rules-marker-entry\design\work\base\A.kdbx`. No other recorded
output held a machine path, a user name or a session identifier. Three things such a search
turns up are none of them: `kpg=` in `merges.txt` is the probe's count of KPG entries,
`kpg-rme-never-saved.kdbx` in the design's `Program.cs` is a name the cost run gives a database it
never writes, and the signature lines name the publisher's certificate.
