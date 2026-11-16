# Probes for `versioned-fields`

The probes behind the option that writes every change as a JSON version in a string field named
from its time, each write in an entry of its own, reconciled when read: its design,
`papers\options\versioned-fields\design.md`, its two critiques, `critique-survival.md` and
`critique-security-and-fit.md` beside it, and the numbers `digest.md` takes from them. The option
has no formal critique. One directory per paper, as `spikes\storage\probes\README.md` lays out:

| Directory | Paper | What runs |
| --- | --- | --- |
| `design\` | `design.md` | `probe\Program.cs` built as `Vf.exe` over KeePassLib 2.62, and `scripts\Run.ps1` driving it and four `keepassxc-cli` releases |
| `survival\` | `critique-survival.md` | `probe\Program.cs`, the critic's copy of the design's probe with additions, built as `Vfs.exe`, and `scripts\Run.ps1` driving it and `keepassxc-cli` 2.4.2 and 2.8.0 |
| `security-and-fit\` | `critique-security-and-fit.md` | `scripts\copies.ps1`, which loads KeePass 2.62 as a library and duplicates a ruled group and entry |

Every probe was run again from this branch on 2026-11-15, twice, on the machine the record was made
on and under the same `de-DE` locale. **Every output came out the same as its record** once what
differs between two runs by design is taken away, with one exception that is the probe's and not
the run's: the last section of the design's `dumps.txt`, which the record made with an earlier
version of the probe (see "Defects and deviations found"). **Every number the papers quote from
these probes reproduced**, the timings at the fast end of the machine's spread and one of them
below it, and a file size that moves between any two runs. One sentence is wider than the probe's
own input and is marked "no"; three more are worded more broadly than their output, and each is
noted where it stands. What no probe here covers is listed at the end.

Each row of a table below belongs to the probe and the command of the section it is in.

## What a run needs

- Windows PowerShell 5.1 as `powershell.exe`, which runs every command below, and PowerShell 7 as
  `pwsh.exe`, which runs the two `scripts\Run.ps1` (see "PowerShell 7" below).
- The .NET SDK for `dotnet build`, and uv.
- KeePass 2.62, which `. .\tools\KeePass.ps1; Install-KeePass` provides from the branch root, and
  KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, which `tools\KeePassXC.ps1` provides when a probe asks
  for a release.

Each `Reproduce.ps1` writes only into gitignored places beside it: `out\`, `work\`, `probe\out\`
and `probe\obj\`, and through the tools into `.keepassxc\` when a release is not there yet. **Every
database is made by the run, in `work\`, and none is kept here.**

## Comparing a run with the record

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\versioned-fields\compare.py spikes\storage\probes\versioned-fields\design
```

The same with `survival` or `security-and-fit` as the last argument. `compare.py` was written on
this branch, after the `compare.py` that `store-entries` carries. It takes away only what differs
between any two runs of the same code: identifiers a run draws at random, times in milliseconds,
the clock at the moment of a run, the size of a database file, which moves by whole cipher blocks
between two saves of the same content, and line endings. An identifier both files share is kept,
so a derived identifier that moved would still show. What remained after that, file by file:

| File | What remained | Why |
| --- | --- | --- |
| `design\out\dumps.txt` | the last section, the import with new identifiers: the record shows the file before Delete Duplicate Entries, this run after it | the record was made by an earlier version of the probe; see "Defects and deviations found" |
| `design\out\merges.txt`, `out\newids-detail.txt`, `out\summary.txt` | the order of the `rule on` lines of copied nodes, in the sections on Duplicate Group and the import with new identifiers | the fold sorts nodes by identifier, and a copy's identifier is drawn at random; the lines are the same, in another place each run |
| `survival\out\newids.txt` | the same order of the copied nodes' `rule on` lines; and in the second run the value of `KPG.Client.c1` on the last line, `(empty)` where the record and the first run have `record of c1` | the order as above; the value is a race with the clock, see the survival table |

Every other recorded file was the same after normalizing in both runs: `marks.txt`,
`sentinel.txt` and `walk.txt` of the design, `h5.txt`, `gone.txt`, `chain.txt` and `refined.txt`
of the survival critique, and `copies.txt`, which was the same as its record byte for byte.

## `design\`: the design's merges, saves, copies, deletion times and walk

**What it measures.** `Vf.exe make` builds sixteen databases with invented content (Argon2d,
2 iterations, 1 MiB) as the design writes its store: a base, a copy `R-A1` that revokes client `c1`
and narrows entry X's rule, `R-A2` that tidies on top of it, the stale copy `R-B` that pairs `c4`,
changes X's password and group U's rule, the pairs `C` and `P` of concurrent rule and profile
changes, the hand damage `H1` to `H6`, and the single files `F1`, versions of another store planted
in a user group, and `F2`, a revocation edited by hand. `scripts\Run.ps1` merges ten pairs with
KeePassLib's four identifier-matching methods and four `keepassxc-cli` releases in both directions
(160 cells), saves `R-A2` with each release, runs one chain, a Duplicate Group and an import with
new identifiers followed by Delete Duplicate Entries, reads every result with the design's reading
rule (`fold`), dumps the files the design quotes, and times the walk. `Vf.exe marks` prints the
derived marks, `Vf.exe newids` and `Vf.exe dump` the import in detail, and `Vf.exe sentinel` which
deletion time removes an unchanged entry in another copy (24 cells). Cited by `design.md` sections
1, 3.1, 3.2, 4.1, 5.1 to 5.3, 5.6, 6.3, 7.1, 7.2, 8, 10, 12 and 14, by `critique-survival.md` as
`[M design]`, by `critique-security-and-fit.md` as `[M vf]`, and by `digest.md` sections 2 to 5.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\versioned-fields\design\Reproduce.ps1
```

**Output.** Fresh: `design\out\merges.txt`, `summary.txt`, `dumps.txt`, `walk.txt`, `marks.txt`,
`newids-detail.txt` and `sentinel.txt`. Recorded: the same names under `design\recorded\`. The
timing alone is `spikes\storage\probes\versioned-fields\design\probe\out\Vf.exe walk` once the
command has built it.

**Runtime here**: 86 and 84 seconds in two runs, the build included. The record does not state how
long its run took; its first database and its `summary.txt` are stamped 67 seconds apart.

**Changes made to run it here:**

1. `probe\Vf.csproj`: the `HintPath` to KeePass became `$(KeePassExe)`, which
   `spikes\storage\Directory.Build.props` defines as the pinned `.keepass\2.62\KeePass.exe`, the
   same file by hash as the file the record was built against.
2. `scripts\Run.ps1`: `$Root`, a fixed path into the agent's scratch directory, became
   `Split-Path -Parent $PSScriptRoot`; `$XcRoot` and the four paths into the KeePassXC ground
   report's downloads became `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1`,
   which provides the same pinned archives and checks their signatures.
3. `Reproduce.ps1` was added. It measures nothing. It holds a Release build into `probe\out\`,
   which the record's `probe\obj\Release` and `probe\out\` imply and which nothing recorded;
   `scripts\Run.ps1`; `scripts\summarize.py` over `out\merges.txt` into
   `out\summary.txt`; and three steps the agent ran by hand and recorded without the command,
   reconstructed from the outputs and the directories the run left: `Vf.exe marks` into
   `out\marks.txt`; `Vf.exe make` into `work\recheck\`, `Vf.exe newids` of `R-base` into `R-A2`
   there and `Vf.exe dump` of the result under the heading `## the file afterwards`, into
   `out\newids-detail.txt`; and `Vf.exe sentinel work\sentinel` into `out\sentinel.txt`. The record
   joined the two parts of `newids-detail.txt` in a shell that ended the heading's lines with a bare
   line feed; `compare.py` reads line endings alike.
4. `scripts\Run.ps1` is started with `pwsh.exe`. See "PowerShell 7".

**What the papers quote, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| The write mark is `367B11D1`, the marker mark `2B82E624`, the group mark `A1B1D236`: the first four bytes of SHA-256 over `KPG/1/write`, `KPG/1/marker` and `KPG/1/group` | `design.md` §3.1, §4.1 | `marks.txt`: those values | the same | yes | |
| A version name `KPG.20261115T101502Z.D110E16556F73DB5.01` and its entry `367B11D1 6AF98626 D110E16556F73DB5` | `design.md` §3.1, §3.2 example | `marks.txt`: `367B11D16AF98626D110E16556F73DB5` | the same | yes | |
| Version sizes: an authority 3,063 characters in a protected string, an admission record 857, a revocation 205, a profile addition 175, a rule 230 to 242 | `design.md` §3.2 rows 1, 3, 4, 6 and 7; `critique-security-and-fit.md` S2 | `dumps.txt`: `[P](3063)`, `(857)`, `(205)`, `(175)`, and `(230)`, `(237)`, `(242)` for rules | the same | yes | the stand-ins are the probe's own; the table's other sizes are estimates or `ground-inventory.md`'s, see the last section |
| An entry only one copy holds arrives under every method; two entries added in two copies both arrive in each of the 160 merges | `design.md` §1, §7.2; `critique-survival.md` §2; `critique-security-and-fit.md` S1 | `c4`, which only `R-B` pairs, admitted in all 128 cells of R1, R2 and H1 to H6; `c5` in all 16 of C; both copies' profile versions found in all 16 of P | the same | yes | |
| 80 KeePassLib cells, 80 `keepassxc-cli` merges and four saves; all 84 exited 0 | `design.md` §7.1, §14 | 80, 80 and 4, every exit 0 | the same | yes | |
| Each `keepassxc-cli` carries a valid Authenticode signature from DroidMonkey Apps, LLC | `design.md` §14 | four `Valid` | the same | yes | `tools\KeePassXC.ps1` checks the same before the run |
| `F2`: both entries of a revocation edited by hand; the whole version is found in their history and the client stays refused | `design.md` §5.1 item 4, §8; `critique-survival.md` §2 | `F2` input: `c1` refused, no `DAMAGED` | the same | yes | |
| `F1`: a marked entry in a user group with a retirement of the authority and a wide rule for an unruled entry, under another store identifier, changes nothing | `design.md` §5.2; `critique-survival.md` §2; `critique-security-and-fit.md` "What was checked" 1 | `F1` reads as `R-A1` does, with `versions of another database 2`, the authority in service and no rule on Y | the same | yes | |
| Pair C: both rule versions held, X reads `default=deny`, `ops` nothing, `reader` read, in all 16 | `design.md` §5.3, §7.1; `critique-survival.md` §2; `digest.md` §4 | 16 of 16 `two versions, what both allow: default=deny;ops=;reader=read` | the same | yes | |
| Pair C: `reader` off, `ops` on, in all 16 | `design.md` §5.3, §7.1 | 16 of 16 | the same | yes | |
| Pair P: `reader` off in all 16 | `design.md` §5.3, §7.1; `critique-survival.md` §2 | 16 of 16 | the same | yes | |
| Duplicate Group: the copied group and the copied entry inside it both refuse | `design.md` §5.3, §8; `critique-security-and-fit.md` "What was checked" 6; `digest.md` §4 | both copies `REFUSES: copied from a ruled ...` | the same, the two lines in another order | yes | `PwGroup.Duplicate` called as KeePass's command calls it, not the command |
| File then Import with new identifiers: the store arrives again in a second group under new identifiers, every user node is duplicated, and every copied ruled node refuses | `design.md` §5.3, §7.2 | `newids-detail.txt` and the `merges.txt` section: two `KeePassGate` groups, two `General`, two `U`; both copies refuse | the same | yes | KeePassLib's `CreateNewUuids` merge, not KeePass's window |
| Copies of write entries without the mark are not read | `design.md` §7.2, §8 | `copies of write entries under other identifiers 7`, none of their versions counted | the same | yes | the copies were made by the import; a write entry duplicated on its own was not run |
| Delete Duplicate Entries after that import deleted four entries and neither state entry; the four were originals, the authority's entry and the group's marker among them; the gate then serves nothing | `design.md` §4.1, §7.2, §8; `critique-security-and-fit.md` S3; `digest.md` §4 | `newids-detail.txt`: `deleted 4`, `CLOSED`; the records the command wrote name the original marker, authority entry, user entry and Y; both tidy entries kept | the same | yes | |
| The import leaves a second copy of the authority's key | `design.md` §12; `digest.md` §5 | `newids-detail.txt`: the copied group holds an authority entry with `[P](3063)` | the same | yes | |
| R1, all 16: `c1` refused, its revocation in two entries; `c2` and `c4` admitted; X keeps A's narrowing; U carries B's newer rule | `design.md` §7.1 | `summary.txt`, R1 | the same | yes | |
| R2, all 16 the same verdicts; 6 write entries where the plugin's records applied (KeePassLib into A under all four methods, Synchronize into B), 10 in the other 11 cells | `design.md` §6.3, §7.1 | 5 cells `W6`, 11 cells `W10` | the same | yes | |
| `keepassxc-cli` brought four deleted entries back beside the ten records, in four releases, every verdict as it was; a KeePass Synchronize of that result removed them again | `design.md` §6.3 | R2 into A under every release: `W10`, ten own records; the chain: `W6` | the same | yes | |
| KeePassXC never applies or copies a deletion record | `design.md` §7.2 | merging into `R-B` every release leaves `R-B`'s own 2 records | the same | yes | |
| H1, all 16: `c1` refused | `design.md` §7.1, §8 | 16 of 16, the revocation in one entry | the same | yes | |
| H3, all 16: every verdict as in R2 | `design.md` §7.1, §8 | 16 of 16 | the same | yes | |
| H4, all 16: `c1` refused; `503 damaged` in 9, every merge into A and Synchronize into B | `design.md` §7.1, §8 | 16 refused; `CLOSED` in those 9 | the same | yes | the probe prints `CLOSED` where the design answers `503 damaged` |
| H2: 9 closed, A's file holding no record of `c1` and a Synchronize removing B's; 7 open, `c1` admitted | `design.md` §7.1 | the same 9 and 7 | the same | yes | |
| H5: into A, all 8: `c1` and `c2` not admitted, `reader` off; into B the same after Synchronize, the other 7 leaving B's clients as they were; X keeps A's narrowing in all 16, read from the entry's history | `design.md` §5.3, §7.1, §8; `critique-security-and-fit.md` "What was checked" 3; `digest.md` §4 | as stated | the same | yes | **under the probe's rule, which reads a node's history always**, as `design.md` §14 states; under the design's own rule of §5.3 the narrowing is lost in the cells `critique-survival.md` P3 measured, see the survival table |
| The file that lost both state entries admits nobody | `design.md` §8 | `H5` input: no client and no profile | the same | yes | |
| H6: `503 damaged` in 9; the other 7 leave B as it was | `design.md` §7.1, §8 | `CLOSED` in those 9 | the same | yes | |
| The 21 open cells are H2, H5 and H6 merged into B by a method that carries no deletion records | `design.md` §7.1, §8, §12; `critique-security-and-fit.md` "Problems the design states"; `digest.md` §3 item 3 | `c1` admitted in 7 cells of each, and nowhere else it was revoked | the same | yes | |
| With the deletion record at the entry's own time or a second before, the entry stayed and the record was dropped in all 16 cells; at one second after, the entry was removed by every method in the copy that deleted it and by Synchronize in the other | `design.md` §6.3; `critique-survival.md` §2 | `sentinel.txt`, 24 lines | the same | yes | |
| A plain save by `keepassxc-cli add`, four releases, kept every write entry, field, the authority's protection flag, the marker, `KPG.Rule`, every time and all ten deletion records with their times; the file stayed KDBX 4.0 | `design.md` §7.1, §10; `critique-survival.md` §2; `digest.md` §2 | `dumps.txt`: the store group, its flags, the marker, X and its history and the ten records identical to the input in all four | the same | yes | |
| KeePassXC adds keys of its own to the database's custom data | `design.md` §7.2, row "KeePassXC 2.4.2 to 2.8.0" | 2.7.12 and 2.8.0 add `_LAST_MODIFIED` and `KPXC_RANDOM_SLUG`; 2.4.2 and 2.6.6 add none | the same | yes | the row names four releases; two of them add keys |
| Every file made and each of the 164 results is KDBX 4.0, and KeePassXC 2.4.2 and 2.6.6 kept all of it | `design.md` §10 | 182 fold headers, all `kdbx 4.0`: 16 inputs, 160 merges, 4 saves, the chain and the duplicate | the same | yes | |
| The walk's database: 10,030 and 50,030 entries, 5,000 deletion records, 30 write entries holding 142 versions | `design.md` §5.6 | the same | the same | yes | |
| Finding the plugin's entries and marked records by identifier: 0.13 ms at 10,030 entries | `design.md` §5.6; `digest.md` §5 | 0.133 ms | 0.137 and 0.140 ms in two runs of `Reproduce.ps1`; 0.131 to 0.146 ms in five runs of `Vf.exe walk` on their own | yes, within the spread | |
| The same and the comparison of every version field: 0.15 ms at 10,030 | `design.md` §5.6; `critique-survival.md` P2 | 0.151 ms | 0.155 and 0.197 ms; 0.138 to 0.167 ms | yes, within the spread | |
| Reading and checking all 142 versions from nothing: 2.9 ms at 10,030 | `design.md` §5.6; `critique-survival.md` P2 | 2.884 ms | 3.825 and 4.258 ms; 2.903 to 4.258 ms | yes, at the fast end | the record is below every rerun by under 1 percent |
| One lookup of `KPG.Rule` on every user entry: 1.5 ms at 10,030 | `design.md` §5.6; `critique-security-and-fit.md` M4; `digest.md` §5 | 1.513 ms | 2.061 and 4.205 ms; 1.538 to 1.849 ms | yes, at the fast end | |
| Finding by identifier: 0.96 ms at 50,030 | `design.md` §5.6; `digest.md` §5 | 0.955 ms | 1.130 and 2.663 ms; 0.968 to 1.232 ms | yes, at the fast end | |
| The same and the comparison: 0.95 ms at 50,030 | `design.md` §5.6; `digest.md` §5 | 0.947 ms | 1.110 and 2.459 ms; 0.947 to 1.251 ms | yes, at the fast end | |
| Reading all 142 versions: 2.5 ms at 50,030 | `design.md` §5.6 | 2.488 ms | 3.579 and 4.152 ms; 2.549 to 2.948 ms | yes, at the fast end | the record is below every rerun by 2 percent |
| One lookup of `KPG.Rule` on every user entry: 12.0 ms at 50,030 | `design.md` §5.6; `critique-security-and-fit.md` M4; `digest.md` §5 | 12.043 ms | 14.993 and 16.895 ms; 13.323 to 15.557 ms | differs | **the record is 10 percent below the fastest of seven reruns.** The second run of `Reproduce.ps1` was slower in every row, on a machine other measurements were sharing; the record looks like a quiet moment of the same machine. Noise, not a claim that fails, and if anything the claim is low |

## `survival\`: the survival critique's sequences

**What it measures.** `Vfs.exe` is the design's probe with additions: `fold2`, the design's own
history rule of §5.3 beside the probe's `fold`; `fold3`, the same with the refined condition of
P3's change; `foldcopies`, which reads entries without the mark too; `recreate`, a Recreate of the
authority; `gone`, entry X deleted for good and then the Data page's press; `close`, one renewal
as the design writes it; `count`; and `cdcontrol`, today's store in database custom data through the
same import. `scripts\Run.ps1` runs pair H5 under both rules (KeePassLib's four methods and
`keepassxc-cli` 2.4.2 and 2.8.0, both directions), imports the revoking copy into the stale copy
with new identifiers and by Synchronize, runs P4's press and merge, and eight renewals with
KeePassXC and with KeePass's Synchronize doing the merging. Cited by `critique-survival.md`
sections 1, 2, 3 (P1 to P4) and 8, and by `digest.md` sections 2, 3 and 5.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\versioned-fields\survival\Reproduce.ps1
```

**Output.** Fresh: `survival\out\h5.txt`, `newids.txt`, `gone.txt`, `chain.txt` and `refined.txt`.
Recorded: the same names under `survival\recorded\`.

**Runtime here**: 36 seconds in each of two runs, the build included.

**Changes made to run it here:**

1. `probe\Vfs.csproj`: the `HintPath` to KeePass became `$(KeePassExe)`, as for the design.
2. `scripts\Run.ps1`: `$Root` became `Split-Path -Parent $PSScriptRoot`; `$XcRoot` and the two
   paths into the KeePassXC ground report's downloads became `Get-KeePassXCCli`.
3. `Reproduce.ps1` was added. It builds `probe\Vfs.csproj` into `probe\out\`, runs
   `scripts\Run.ps1` with `pwsh.exe`, and then makes `out\refined.txt`, which the critic recorded
   without the command. That step is reconstructed from the record and the cells the run left: for
   the twelve `work\h5-*` cells in ordinal order and the two
   `work\gone\xc<release>-B-into-A-pressed` cells, a heading `## <cell>, refined condition` and the
   lines of `Vfs.exe fold3` over the cell's result that name the design's history rule or the rule
   on entry X. It gives the record line for line.

**What the paper quotes, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| P3, the probe's rule (history always read): `reader` read in all 12 cells of H5 | `critique-survival.md` §3 P3 table | `h5.txt` | the same | yes | |
| P3, the design's rule: into A, KeePassLib's four methods, `reader` read; into A, `keepassxc-cli` 2.4.2 and 2.8.0, `reader` read, list, update, A's narrowing undone; into B, Synchronize and Overwrite existing, read; into B, Overwrite if newer, Keep existing and both releases, read, list, update | `critique-survival.md` §1, §3 P3, §6 item 7; `digest.md` §2, §3 item 5 | `h5.txt` | the same | yes | refutes `design.md` §7.1's "X keeps A's narrowing in all 16" for the design's text, which the design's own probe measured with the other rule |
| Before the merge, A served `reader` read from X's own copy | `critique-survival.md` §3 P3 | the design's `merges.txt`, `H5` input | the same | yes | |
| The refined condition reads history in both KeePassXC cells into A and gives `reader` read, and leaves the cells into B as they were | `critique-survival.md` §3 P3 change; `digest.md` §2, §3 item 5 | `refined.txt` | the same | yes | |
| None of P1 to P4 needs a hand deletion | `critique-survival.md` §1 | P3's cells start from `H5`, whose two tidy entries were deleted without a record | the same | no | **the sentence is wider than the probe's own input**: P3 needs both tidy entries deleted without a record, which `critique-survival.md` §3 P3 step 3 itself describes; `digest.md` §3 item 5 states the same |
| P4: after the press, `keepassxc-cli` 2.4.2 and 2.8.0 merging the stale copy into A give X `reader` read, list, update; without the press the same merge keeps X at read; KeePassLib's four methods into A keep X deleted | `critique-survival.md` §1, §3 P4; `digest.md` §2, §3 item 4 | `gone.txt` | the same | yes | |
| History does not help P4: B's X never held the narrowing | `critique-survival.md` §3 P4 | `refined.txt`, the two pressed cells: history read, X still read, list, update | the same | yes | |
| P1, `R-A1` or `R-A2` imported into the stale copy with Create new IDs: `c1` admitted, X keeps B's wider rule; by Synchronize: `c1` refused, X narrowed | `critique-survival.md` §1, §3 P1; `digest.md` §2, §3 item 1 | `newids.txt` | the same | yes | KeePassLib's `CreateNewUuids` merge, not KeePass's window |
| P1, the Recreated copy imported with Create new IDs: `fpA` serves, every client B paired under it admitted, `c1` included, `fpB` and `c9` count for nothing; by Synchronize: `fpA` retired, `fpB` serves, `c9` admitted | `critique-survival.md` §3 P1 | `newids.txt` | the same | yes | under Synchronize the output prints no line for `fpA` at all, its entry removed by the plugin's deletion record, rather than `retired`; it lists B's own `c4` admitted, since the probe does not apply the authority a certificate chains to, as §8 states |
| Today's store through the same import: the stale copy holds `KPG.Revoked.c1` and `KPG.RetiredAuthority.fpA` under Create new IDs as under Synchronize | `critique-survival.md` §3 P1 | `cdcontrol` in `newids.txt` | the same in both runs | yes | in the second run `KPG.Client.c1` came out `(empty)` under Create new IDs where the record and the first run have `record of c1`: the probe writes the base and the revoking copy within moments, and whether the copy's value is newer depends on whether the clock passed a second between the two. The keys the claim names were there each time |
| With closing versions read from the copies too, B refuses `c1`, holds `fpA` retired and keeps X narrowed; the probe's `foldcopies` also lists `fpB` in service and `fpA`'s clients admitted | `critique-survival.md` §3 P1 change; `digest.md` §2 | `foldcopies` in `newids.txt` | the same | yes | `c1` is refused in the `R-A1` and `R-A2` imports; in the Recreated import it is listed admitted, which is the parenthesis of the paper |
| P2: the start, 3 write entries, 11 version fields (6 distinct), 6,481 characters, under both | `critique-survival.md` §1, §3 P2 | `chain.txt` | the same | yes | |
| P2 round 1: KeePassXC 5, 25 (8), 12,023; Synchronize 3, 15 (8), 8,605 | `critique-survival.md` §3 P2 | `chain.txt` | the same | yes | |
| P2 round 4: KeePassXC 11, 79 (14), 31,109; Synchronize 3, 21 (11), 9,835 | `critique-survival.md` §3 P2 | `chain.txt` | the same | yes | |
| P2 round 8: KeePassXC 19, 179 (22), 62,297; Synchronize 3, 29 (15), 11,475 | `critique-survival.md` §1, §2, §3 P2, §6 item 6; `digest.md` §5 | `chain.txt` | the same | yes | |
| Each KeePassXC round adds more than the last: 5,542 characters in round 1, 8,412 in round 8 | `critique-survival.md` §3 P2 | rising every round, 5,542 to 8,412 | the same | yes | differences of the counts above |
| The deletion records in machine 1's file went from 10 to 90 | `critique-survival.md` §3 P2 | 10 to 90 | the same | yes | |
| The compressed files grew from 6,885 to 15,013 bytes | `critique-survival.md` §3 P2 | 6,885 to 15,013 | 6,885 to 14,837 and 6,885 to 15,173 | differs | noise: a database file's size moves by whole cipher blocks between saves of the same content. The growth the sentence is about holds in every run |
| Every verdict stayed right after 8 KeePassXC rounds: `c1` and every revoked renewal refused, `c2` admitted, 19 entries present | `critique-survival.md` §2, §3 P2 | `chain.txt`, the last fold | the same | yes | |
| Under KeePass's Synchronize the store stayed at 3 entries over 8 renewals | `critique-survival.md` §2 | `chain.txt` | the same | yes | |
| In KeePass alone, 13 to 34 deletion records in 7 rounds, three a renewal | `critique-survival.md` §3 P2 | `chain.txt` | the same | yes | the 1,560 a year beside it is arithmetic, marked so by the critic |
| Write entries added in two copies both arrive in every cell run here | `critique-survival.md` §2 | `h5.txt`, `gone.txt`, `chain.txt` | the same | yes | |
| The two `keepassxc-cli` archives hash to `f2f0b870...981264` and `48ff4f08...fc828`, and both executables carry a valid signature from DroidMonkey Apps, LLC | `critique-survival.md` §8 | nothing recorded | both hashes are this branch's pins in `keepassxc.sha256`, which `tools\KeePassXC.ps1` checks when it provides a release, and it checks the signature at every call | yes, by the tool | the critic's run wrote neither check down |

## `security-and-fit\`: the security and fit critique's duplication probe

**What it measures.** `scripts\copies.ps1` loads KeePass 2.62 as a library and builds an in-memory
database with a group G ruled twice over, by today's custom data and by the design's marker entry,
and an entry inside it. It duplicates G as KeePass's Duplicate Group does, once with KeePass's copy
suffix appended to each copied title and once without, runs Delete Duplicate Entries after each,
and duplicates an entry ruled by `KPG.Rule`. Cited by `critique-security-and-fit.md` "What was
checked and holds" item 6 and M1.

**Command**, from the branch root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\versioned-fields\security-and-fit\Reproduce.ps1
```

**Output.** Fresh: `security-and-fit\out\copies.txt`. Recorded:
`security-and-fit\recorded\copies.txt`.

**Runtime here**: about one second in each of two runs.

**Changes made to run it here:**

1. None to `scripts\copies.ps1`. It takes the KeePass it loads as `-KeePassExe` and the file it
   writes as `-OutFile`, and `Reproduce.ps1` passes `Get-KeePassExe`, the pinned KeePass 2.62, and
   `out\copies.txt`; the record names `KeePassLib 2.62` and not the path it was given.
2. `Reproduce.ps1` was added, to hold those two arguments.

**What the paper quotes, and this run:**

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| After a Duplicate Group the copy's marker `KPG.GroupRule` still names the original group, and the duplicated entry's `KPG.Rule` still names the original entry | `critique-security-and-fit.md` "What was checked and holds" 6 | `names original=True` for both | the same, byte for byte | yes | `PwGroup.Duplicate` and `PwEntry.Duplicate`, with the title extension done as the probe does it, not KeePass's command |
| With the title extension on, Delete Duplicate Entries deleted nothing | `critique-security-and-fit.md` M1 | `deleted 0` | the same | yes | |
| With it off, Delete Duplicate Entries deleted the pair and the original marker was the marker removed; the original and the copy tie on modification time | `critique-security-and-fit.md` M1 | `deleted 2; original marker kept=False; copy marker kept=True`, `lastmod equals original=True` | the same | yes | the probe prints the count and which marker survived, not which other entry went; that KeePass deletes the earlier of a tie is read from `PwDatabase.cs`, not run |
| The design's measurements the critique credits: 160 merges, `F1`, H5 in all 16, Delete Duplicate Entries after an import, 1.5 and 12.0 ms for a lookup on every entry | `critique-security-and-fit.md` S1, S3, M4, "What was checked" 1 and 3 | | | see the design's table | quoted as `[M vf]`, the design's probe |

## PowerShell 7

`spikes\storage\probes\README.md` states that the PowerShell probes were measured with Windows
PowerShell 5.1. **The two `scripts\Run.ps1` here were not**, and cannot have been: their
`Invoke-Cli` sets `ProcessStartInfo.StandardInputEncoding`, which exists in .NET from Core 2.1 on
and not in .NET Framework 4.8, so Windows PowerShell stops at that line, as
`spikes\storage\probes\store-entries\RUN.md` records for the same function. The record agrees:
`design\recorded\walk.txt` carries no byte order mark, which `Set-Content -Encoding utf8` writes in
5.1 and not in 7. So each `Reproduce.ps1` starts its `scripts\Run.ps1` with `pwsh.exe`. Nothing
measured runs in that shell: the KeePassLib work runs in `Vf.exe` and `Vfs.exe`, .NET Framework
4.8 programs, and the merges in `keepassxc-cli`. `scripts\copies.ps1` names Windows PowerShell 5.1
in its header and runs in it here.

## Claims nothing here can vouch for

Each of these is quoted as a number or a measurement, and no probe or output of this family makes
it. Where the papers mark it as assumed or as somebody else's measurement, that is noted.

- **The sizes in `design.md` §3.2 that are not the probe's**: about 200 characters for a
  retirement, about 230 for a standing answer, about 170 for an accept, about 200 for a bin
  marker. The probe never wrote those kinds, as `design.md` §14 states; the survival probe's
  `recreate` writes a retirement and prints no size. About 990 for a real admission record and
  about 300 and 5,200 for rules are `[M inventory 8]`, `ground-inventory.md`'s.
- **"How much" in `design.md` §6.3**: about 17,000 characters a state entry, a store of about
  38,000 against some 14,000 today, 520 small entries a year, a tidy every eleven days, about 585
  deletion records a year. Arithmetic on sizes, half of them `ground-inventory.md`'s, with no
  probe behind it. The state entry checks as 10 records of 990, 3 revocations of 205, 5 names of
  175 and 20 rules of 300, which is 17,390; the rest was not rebuilt. The 17 characters a version
  adds to `after` is the format: 16 hex digits and a space.
- **The arithmetic in `critique-survival.md` P2**: about 17.7 million characters a year, about
  0.14 microseconds a field and 10 ms a request, a second for a full parse, about 1,560 deletion
  records a year. The critic marks each `[A]`. Their inputs reproduce: the walk's 0.133, 0.151 and
  2.884 ms over 142 versions, and the chain's three records a renewal.
- **`critique-security-and-fit.md` M3 and S3**: about 34 KB a closing write and 520 small entries
  a year, arithmetic on the design's numbers; the save time of a whole database is cited from
  elsewhere.
- **Two rows of `design.md` §8 cited `[M here]` that the probe ran only in part**: "Duplicates a
  write entry" (the copies here were all made by the import with new identifiers, none by
  duplicating one write entry) and "makes from a template a ruled entry or group" (no template was
  used; Duplicate Group and the import were).
- **Everything else the four papers mark `[M keepass]`, `[M keepassxc]`, `[M others]`,
  `[M spike]`, `[M inventory]`, `[M store-entries]`, `[M rules-central]` or `[M android]`** is
  another family's, and its own `RUN.md` answers for it.

## Defects and deviations found

- **The recorded design run saved the import before Delete Duplicate Entries, and the command's
  result over its input.** The last section of `design\recorded\dumps.txt` is titled "R-base
  imported into R-A2 with new identifiers, then Delete Duplicate Entries" and shows the file before
  that command: all four originals present, none of the four deletion records it writes. The
  agent's `work\base\R-A2.kdbx`, read here without changing it, holds the command's result
  instead, with those four records stamped at the moment of the recorded run. So the `Vf.exe` of
  the record wrote the imported database to the output path before the command and the command's
  result to the target's own file. The carried `Program.cs` saves the output as the primary file
  and writes the command's result there, which is what `design\recorded\newids-detail.txt` shows,
  made 4 minutes later; it is the only version of the probe the agent left. This run's
  `dumps.txt` section therefore differs from the record and is the same, after normalizing, as the
  dump in the recorded `newids-detail.txt`. **No claim rests on the mislabeled section**: the
  claims about the import rest on the folds in `merges.txt`, which are read from memory and
  reproduce, and on `newids-detail.txt`.
- **Both probes' `Program.cs` were edited after most of their record was made.** The design's was
  last written after `merges.txt`, `dumps.txt`, `walk.txt`, `summary.txt`, `marks.txt` and
  `newids-detail.txt` and before `sentinel.txt`; the survival critique's after its four main
  outputs and before `refined.txt`, which uses the `fold3` that edit added. Apart from the section
  above, every output reproduces from the code as left, so the edits changed nothing else.
- **`design.md` §7.1, H5, "X keeps A's narrowing in all 16"** holds for the probe's rule only;
  `critique-survival.md` P3 measured the design's own rule and found the narrowing lost in two
  KeePassXC cells into A. `design.md` §14 states the difference between the two rules.
- **`design.md` §7.2 states that KeePassXC adds keys of its own to the database's custom data** in
  a row for 2.4.2 to 2.8.0; 2.7.12 and 2.8.0 do and 2.4.2 and 2.6.6 do not.
- **`critique-survival.md` §1 states that none of P1 to P4 needs a hand deletion**; P3's input
  comes from a hand deletion.
- **`critique-survival.md` P1 states that `fpA` is retired after a Synchronize**; the output shows
  it gone rather than printed as retired, which is the same verdict.
- **Two outputs vary by design between runs** without a claim changing: the order of the copied
  nodes' `rule on` lines, which the fold sorts by identifier and a copy's identifier is random,
  and the `KPG.Client.c1` value of the survival probe's `cdcontrol` under Create new IDs, a race
  with the clock's second.
- **The design's `scripts\Run.ps1` states in its first line that it merges with five KeePassLib
  methods**; it runs four, Synchronize, Overwrite if newer, Overwrite existing and Keep existing,
  which is what `design.md` §7.1 counts. The comment is left as it was.
- **The probes' stated limits**, not defects: the rule documents use a short syntax of the probe's
  own; the store identifier is fixed; the kinds `retire`, `answer`, `accept` and `bin` are not
  exercised by the design's probe; the client verdicts do not apply the authority a certificate
  chains to; the walk runs in a console process; KeePass's commands are reached through
  KeePassLib's calls and not its window. Each paper states them.

## Not carried

- **Every database**: 286 `.kdbx` files under the design's `work\` and 66 under the survival
  critique's. The security critique's probe never saves a `.kdbx`: its database lives in memory
  under a path that does not exist. Each run makes them again.
- **Build output**: both `probe\obj\` and `probe\out\`, which hold `Vf.exe` and `Vfs.exe` and
  copies of KeePass's `KeePass.exe`, `KeePass.exe.config` and `KeePass.XmlSerializers.dll`.
- **The 80 `merge.txt` files** under the design's `work\`: what `keepassxc-cli merge` printed in
  each cell, localized and naming the scratch paths of each file, and quoted by no paper.
- **The design's `scripts\patch_make.py` and `scripts\patch_final.py`**: two scripts that rewrote
  an earlier `Program.cs` into the carried version, whose result the carried file already holds; and
  **`scripts\fix_doc_1.py` and `scripts\fix_doc_2.py`**, which edited the wording of the paper
  itself. None is needed to run a probe or to check a claim.
- **The security critique's `work\`**, which was empty.

## Machine paths replaced

The recorded outputs held no machine path, user name or session identifier, and nothing in them was
changed. In the sources, the full paths to the agents' scratch directories, to the KeePassXC
downloads and to the product worktree's KeePass, in the two `scripts\Run.ps1` and the two project
files, became the paths relative to the branch and the tools listed under each probe's changes. A
search of this directory for `C:\`, `Users\`, `AppData`, `scratchpad`, `Temp\`, the session
identifiers and the user name finds only this sentence, the invented path
`C:\nonexistent\probe-copies.kdbx` that `scripts\copies.ps1` names for a database it never saves,
and the user name only as the project's abbreviation: the local `kpg` in both `Program.cs` and the
name `kpg-vf-walk-never-saved.kdbx` the walk gives a database it never saves. The runs' databases
and build output were removed after the runs; their fresh output stays in each `out\`, which is not
committed.
