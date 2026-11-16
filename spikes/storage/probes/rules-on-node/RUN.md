# rules-on-node: the probes and what they reproduce

The family behind `papers/options/rules-on-node/`: the design (`design.md`), its two critiques
(`critique-survival.md` and `critique-security-and-fit.md`) and the digest (`digest.md`), which
quotes the same numbers. Three probes ran for those papers, one each, and the security critic also
ran a short collation script. Each lives in the directory of the paper that ran it, as
`probes/README.md` lays out, keeping the layout its agent gave it:

| Directory | Probe | What it measures | Cited by |
| --- | --- | --- | --- |
| `design\` | `probe\Program.cs` | the design's store and read rule under KeePass 2.62's merges, four methods both ways, and `keepassxc-cli merge -s` of four releases both ways; Create new IDs, the history limit, Delete Duplicate Entries, a KeePassXC save, and the read path's cost | `design.md` §0, §1 to §6, §12; `critique-survival.md` §1, §3, §4; `critique-security-and-fit.md` P1, P2, P4; `digest.md` §2, §4, §5 |
| `survival\` | `probe\Program.cs` | seven cases C1 to C7 attacking the design under KeePass 2.62's merges, read by the design's rule ("D") and a stricter rule ("V") | `critique-survival.md` §0 to §4, §6; `digest.md` §2 to §4 |
| `security-and-fit\` | `probe\Attack.cs` | four scenarios S1 to S4: a recycled head seal, the prune under a KeePassXC 2.8.0 merge, a rule entry moved to the root, and names KeePass cannot tell from the reserved names | `critique-security-and-fit.md` §0, P1, P2, P4, P5, §5; `digest.md` §2 to §4 |
| `security-and-fit\` | `collation.ps1` | how .NET Framework 4.8 collates U+034F in four cultures | `critique-security-and-fit.md` P5, §5 |

All four ran in this run, and each gives its recorded output again. The three probes match
line for line apart from the timestamp on their first line, the collation script matches exactly,
and the only numbers that came out differently are the timings of the read path in the design's
`cost.txt`, which move with the load on the machine. The claims are in the tables under each
probe; the claims these papers present as measured that no probe here backs are listed apart at
the end.

## Running them

From the branch root, in Windows PowerShell 5.1, which is what the probes were measured with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-on-node\design\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-on-node\survival\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-on-node\security-and-fit\run.ps1
```

Each `run.ps1` provides and checks KeePass 2.62 through `tools\KeePass.ps1` and, where a probe needs
it, each KeePassXC release through `tools\KeePassXC.ps1`; builds the probe with `dotnet build`,
which needs the .NET SDK and the package `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3; clears
the probe's `work\`, where it makes its databases; and runs the probe into `out\`. Both are
ignored by the branch. The recorded output each run is compared with is in `recorded\` beside it,
under the same file names. Nothing shows a window: KeePassLib is used as a library and
`keepassxc-cli` runs as a console child process.

How long each ran here, in two runs of all three, with the machine busy with other measurements
(about two thirds of the processor in use during the first). The second run's output is in `out\`.

| Run | Here | Recorded |
| --- | --- | --- |
| `design`: `keepass` | 5.6 s, 3.3 s | not stated; the file times give about 3 s |
| `design`: `cost` | 0.4 s, 0.4 s | not stated |
| `design`: `xc` for 2.4.2, 2.6.6, 2.7.12 and 2.8.0 | 5.6 s, 5.8 s, 2.9 s and 3.3 s; 5.5 s, 5.8 s, 2.4 s and 2.9 s | not stated; the file times give 3 s to 6 s each |
| `survival` | 4.8 s, 3.1 s | 3.3 s, `critique-survival.md` §6 |
| `security-and-fit`: `Attack` | 1.9 s, 0.9 s | not stated |
| `security-and-fit`: `collation.ps1` | 0.1 s, under 0.1 s | not stated |

A build takes one to two seconds before each.

**How a result is judged.** "Yes" is that this run gives what the paper quotes. "Differs" is that
it gives something else, with the note stating whether that is noise, the environment or the claim.
"No" is that neither the record nor this run gives what the paper states. Where the record and this
run agree and the paper's conclusion holds, but its sentence is broader than the output or passes
over a cell, the result is "yes" and the note names the cell or the part that is not in the
output.

## The design's probe, `design\`

`probe\Program.cs` builds every database itself from one base: a group U with a rule entry, an
entry X ruled R0, entries Y and Z unruled, a second ruled group V and a group KeePassGate holding
the seals. Each of 11 scenarios makes copies A and B, merges in both directions, saves, reopens and
applies the read rule of `design.md` §3, which the probe implements itself. It takes a work
directory, a mode (`keepass`, `cost` or `xc`) and an output file, and for `xc` a release and its
`keepassxc-cli.exe`. Output: `out\keepass.txt`, `out\cost.txt`, `out\xc-2.4.2.txt`,
`out\xc-2.6.6.txt`, `out\xc-2.7.12.txt` and `out\xc-2.8.0.txt`.

All paths below are under `papers/options/rules-on-node/`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | every merge cell gives the head rule, the sealed silence or a refusal; 88 KeePass cells (11 scenarios, 4 methods, 2 directions) and 88 KeePassXC cells (11 scenarios, 2 directions, 4 releases) | `design.md` §0 item 5, §12; `critique-survival.md` §1, §4 item 1; `digest.md` §2 | `keepass.txt`, `xc-*.txt`, scenarios 01 to 11 | 176 cells, no rule in force other than the head's | the same 176 lines | yes | checked cell by cell against each scenario's head |
| D2 | the four KeePassXC releases gave identical output | `design.md` §12 | `xc-*.txt` | identical apart from the release named in the headings | the same | yes | |
| D3 | the cells that refused: the tie where the rule's version lost, two heads, a hand edit, the rule entry or seal deleted for good "after a KeePass synchronize", Keep existing where a seal came without its text, copies under new identifiers | `design.md` §12 | `keepass.txt`, `xc-*.txt` | refusals only of those kinds | the same | yes | in 09 and 10 the copy that deleted refuses under every method, not only after a synchronize; the other copy refuses only under Synchronize |
| D4 | a KeePassXC save (`keepassxc-cli add`) leaves every rule and seal reading as before; the rule entry keeps its time and gains `Notes`, `Password`, `URL`, `UserName` | `design.md` §1.1, §5.1; `critique-survival.md` §3 | `xc-*.txt`, "a plain save" | states equal before and after, exit 0; time `2026-01-01T00:00:00Z`; keys `KPG.Group,KPG.Node,Notes,Password,Title,URL,UserName` | the same, each release | yes | the probe prints the names of the strings, not their values, so "empty" is not in the output; the seals' strings are not printed, so `critique-survival.md` §3's "and seals" has no output behind it |
| D5 | 01 and 05: the sealed rule in force, "found in history, all methods and directions" | `design.md` §1.3, §5.2 row 1; `critique-survival.md` §1; `critique-security-and-fit.md` P1 step 4 | `keepass.txt`, `xc-*.txt`, 01 and 05 | R1 in force in every cell: from history in 6 of 8 KeePass cells and both KeePassXC cells, on the node in Overwrite existing into B and Keep existing into A | the same | yes | in those two cells A's version stays current, so the text is on the node rather than in history |
| D6 | 02, a tie: in force when the rule's version won, refuses when it lost | `design.md` §5.2 row 2; `digest.md` §4 | `keepass.txt`, `xc-*.txt`, 02 | refuses in Synchronize into A, Overwrite if newer into A, Keep existing into B and KeePassXC into B; R1 in force elsewhere | the same | yes | Overwrite existing into A is a cell the rule's version lost (B's password is current) where R1 is served from history instead of refusing |
| D7 | 03, the rule changed in both copies: two heads, refuses | `design.md` §5.2 row 3; `digest.md` §4 | 03 | `REFUSE (2 heads)` in all 8 KeePass and both KeePassXC cells | the same | yes | |
| D8 | 04, the rule taken away in one copy: silence, as sealed | `design.md` §5.2 row 4 | 04 | `silence (sealed as no rule)` everywhere | the same | yes | |
| D9 | 11, a first rule in each copy: both in force; Keep existing refuses one of them | `design.md` §5.2 rows 5 and 6 | 11 | both in force in 6 of 8 KeePass cells and both KeePassXC cells; under Keep existing Z refuses into A and Y into B | the same | yes | |
| D10 | 08, the rule of U changed in A while B edits Z, adds N and moves U's time: the new rule in force; Keep existing refuses | `design.md` §1.2, §5.2 row 6, §5.3 row 1; `critique-survival.md` §1; `digest.md` §4 | 08 | RG1 on the node in 6 of 8 KeePass cells, from history in Overwrite existing into A, `REFUSE (the sealed text is nowhere)` in Keep existing into B; RG1 on the node in both KeePassXC cells | the same | yes | `design.md` §5.3 also states that both copies' other entries were kept, which the probe does not print |
| D11 | 06, the rule text edited by hand: refuses | `design.md` §6.2; `critique-survival.md` §2.3; `digest.md` §4 | 06 | refuses in 6 of 8 KeePass cells and both KeePassXC cells; R0, the sealed rule, where the hand edit lost (Overwrite existing into B, Keep existing into A) | the same | yes | |
| D12 | 07, the `KPG` field deleted by hand: the sealed rule stays in force from history | `design.md` §6.2; `critique-survival.md` §2.1 sequence C | 07 | R0 from history in 6 of 8 KeePass cells and both KeePassXC cells, on the node in the other two | the same | yes | |
| D13 | 09, the rule entry deleted for good: refuses after KeePass, in force after KeePassXC | `design.md` §6.2; `digest.md` §4 | 09 | refuses under Synchronize both ways and under every method into B; RG0 into A under the other three; RG0 in both KeePassXC cells | the same | yes | see D3 |
| D14 | 10, the only seal of X deleted for good: refuses (a text and no seal) after KeePass, in force after KeePassXC | `design.md` §6.2; `critique-security-and-fit.md` P1; `digest.md` §4 | 10 | the same pattern as D13, for X | the same | yes | a node's only seal; `critique-survival.md` §4 item 8 and `critique-security-and-fit.md` P1 attack it for a later seal |
| D15 | Create new IDs: the originals unchanged, each copy that carries a rule refuses | `design.md` §5.2 row 7, §6.2; `digest.md` §4 | `keepass.txt`, "CreateNewUuids" | originals as the base; copies of U and X `REFUSE (a text and no seal)` | the same | yes | |
| D16 | without the repair the text is gone after 10 later edits and X refuses; with the repair in the first edit R1 stays in force | `design.md` §4.3; `critique-survival.md` §2.3 item 2; `digest.md` §4 | `keepass.txt`, "How long a rolled back rule can be found in history" | R1 from history after 1 to 9 edits (10 history items from the 8th), refuses from the 10th; with the repair R1 on the node after 1 and 12 | the same | yes | |
| D17 | Delete Duplicate Entries: 0 deleted with `KPG.Node`, 1 without it, and U then refuses | `design.md` §2.2, §6.2; `critique-survival.md` §4 item 14; `critique-security-and-fit.md` P4 | `keepass.txt`, "Delete Duplicate Entries" | `0 deleted of 8`; `1 deleted; U: REFUSE (the sealed text is nowhere) \| V: RULE RG0` | the same | yes | |
| D18 | a seal holds 144 characters of strings | `design.md` §2.3; `critique-security-and-fit.md` P2; `digest.md` §5 | `cost.txt` | 144 | 144 | yes | |
| D19 | 2000 seals read into an index by node: 1.4 ms | `design.md` §3; `digest.md` §5 | `cost.txt` | 1.38 ms | 2.13 ms; 1.33 ms to 2.46 ms over seven runs | differs | timing, noise: 20 rounds in memory on a machine busy with other work |
| D20 | each of 20000 entries in 200 groups asked for `KPG.Group`: 2.6 ms | `design.md` §3; `digest.md` §5 | `cost.txt` | 2.59 ms | 6.87 ms; 3.16 ms to 6.87 ms over seven runs | differs | timing, as D19; the same order of magnitude, and never as low as recorded here |
| D21 | one digest of a rule text: 3 microseconds | `design.md` §3 | `cost.txt`, 1000 digests | 3.09 ms for 1000 | 2.83 ms for 1000; 2.39 ms to 9.89 ms over seven runs | differs | timing, as D19; the run in `out\` agrees, the others spread over a factor of four |

`cost.txt` also times asking 20000 entries for `KPG` (2.13 ms recorded, 2.67 ms in `out\`), which
no paper quotes. The seven runs of `cost` are the two of `run.ps1` and five more made with the
built `Rules.exe` into a directory outside the branch, only to see the spread.

## The survival critic's probe, `survival\`

`probe\Program.cs` models the store exactly as the design's probe does, adds an unruled group W to
the base, and reads every case with both rules: "D", the design's as its probe implements it, and
"V", which also looks at the carriers' history in steps 3 and 5. It takes a work directory and an
output file. Output: `out\survival.txt`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S1 | C1, the newest seal lost after scenario 01 (removed for good, binned, moved to the root): R0 back in force in 6 of 8 cells for each way, the other two refuse; V refuses in all 8 | `critique-survival.md` §0 item 1, §2.1 sequence A, §4 items 6, 8 and 12, §6; `digest.md` §2, §3 item 1 | C1 | D: R0 on the node in 6 of 8 for (a), (b) and (c), `REFUSE (a text no seal names)` in Overwrite existing into B and Keep existing into A; V refuses in all 8 | the same | yes | |
| S2 | C2, a first rule's only seal lost: silence in 6 of 8 cells; V refuses in all 8 | `critique-survival.md` §0 item 1, §2.1 sequence B, §4 item 8; `digest.md` §3 item 1 | C2 | D: `SILENCE (no seal, no text)` in 6 of 8, refuses in the two where Y keeps `KPG`; V refuses in 8 | the same | yes | |
| S3 | C3, the field deleted and then the KeePassGate group deleted: X silence under D, refuses under V; U refuses | `critique-survival.md` §2.1 sequence C, §4 item 9; `digest.md` §3 item 6 | C3 | as quoted, Synchronize both ways | the same | yes | |
| S4 | C4, U deleted for good, its seals pruned with deletion records, N added in B: 2 seals pruned; silence in 5 of 8 cells under both rules, into the deleting copy under all four methods and into the other under Synchronize | `critique-survival.md` §0 item 2, §2.2, §3, §4 items 7 and 10; `digest.md` §3 item 4 | C4, "prune leaves deletion records" | `seals pruned in A: 2`; silence in the 5 cells named; RG0 in force into B under the other three methods | the same | yes | `critique-survival.md` §3 gives "silence, 4 of 4" on a row naming three methods; the 4 cells into A include Synchronize |
| S5 | C4 with a prune that leaves no deletion records: refuses in the same 5 cells | `critique-survival.md` §2.2; `digest.md` §3 item 4 | C4, "prune leaves no deletion records" | `REFUSE (the sealed text is nowhere)` in those 5 | the same | yes | |
| S6 | C5, a seal identifier derived from content: B's seal is A's first; silence in 8 of 8 cells under both rules | `critique-survival.md` §0 item 3, §2.4, §4 items 11 and 12; `digest.md` §3 item 3 | C5 | `one identifier: True`; `SILENCE (sealed as no rule)` in all 8 | the same | yes | |
| S7 | C6, a 1.5 MiB attachment under a 1 MiB history limit: X refuses in both directions with an empty history | `critique-survival.md` §2.7, §3; `digest.md` §4 | C6 | `REFUSE (the sealed text is nowhere)`, `hist 0`, both ways | the same | yes | |
| S8 | C7, the design's recoveries 01, 05 and 07: the same under D and V, and the same answers as the design's own run | `critique-survival.md` §1, §2.1 | C7 | R1, R1 and R0 from history under both rules, both ways | the same | yes | equal to the design's Synchronize rows for 01, 05 and 07 (D5, D12) |
| S9 | the probe ran in 3.3 seconds | `critique-survival.md` §6 | console | 3.3 s | 3.1 s; 4.8 s in the first run | yes | timing, which the load moves |
| S10 | it ran against `KeePass.exe` with SHA-256 `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3` | `critique-survival.md` §6 | the program itself | that hash, for the copy in the probe's directory | the same hash for `.keepass\2.62\KeePass.exe` | yes | the same file for all three probes |

## The security critic's probe and script, `security-and-fit\`

`probe\Attack.cs` models the design's store and read rule as the design's probe does, adds the
check of a seal's identifier against its strings, and keeps the seals in a subgroup "Rule seals" as
`design.md` §2.3 describes. It takes a work directory and an output file, and for S2 a KeePassXC
release and its `keepassxc-cli.exe`; the run used 2.8.0. Output: `out\attack.txt`, and each
`keepassxc-cli` merge's console output in `work\s2\<cell>\cli.txt`, recorded as
`recorded\s2\<cell>\cli.txt`. `collation.ps1` prints to the console, and `run.ps1` writes what it
prints to `out\collation.txt`.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F1 | S1, the ordinary rollback: R1 in force from history | `critique-security-and-fit.md` P1 step 4 | `attack.txt` S1 | `RULE R1(narrow) from history`, 3 seals | the same | yes | |
| F2 | S1, the head seal recycled: R0 in force from the node, in that copy and after a KeePass Synchronize of it with the merged copy | `critique-security-and-fit.md` §0 item 1, P1 step 6; `digest.md` §3 item 1 | `attack.txt` S1 | `RULE R0(wide) on the node`, both lines | the same | yes | |
| F3 | S1, the head seal deleted for good with a deletion record: the same | `critique-security-and-fit.md` P1 step 6 | `attack.txt` S1 | `RULE R0(wide) on the node`, both lines | the same | yes | |
| F4 | S1, the same recycle after the repair: refuses | `critique-security-and-fit.md` P1 step 7 | `attack.txt` S1 | `REFUSE (a text no seal names)` | the same | yes | |
| F5 | S2, X narrowed, deleted, its seals pruned, then `keepassxc-cli 2.8.0 merge -s` of a stale copy: R0 in force, with or without deletion records | `critique-security-and-fit.md` §0 item 2, P2 step 4; `digest.md` §3 item 5 | `attack.txt` S2; `cli.txt` | `RULE R0(wide) on the node`, exit 0, for both prunes; the log shows X and the seal S0 added from the stale copy | the same, and the same logs | yes | |
| F6 | S2 without the prune: refuses; KeePass Synchronize deletes X again in all three variants | `critique-security-and-fit.md` P2 step 5 | `attack.txt` S2 | `REFUSE (the sealed text is nowhere)`; `X: absent` three times | the same | yes | |
| F7 | S3, U's rule entry moved to the root: the root and U both refuse | `critique-security-and-fit.md` §0 item 4, P4 step 3; `digest.md` §4 | `attack.txt` S3 | `root: REFUSE (a text and no seal) \| U: REFUSE (the sealed text is nowhere)` | the same | yes | |
| F8 | S4, U+034F is category Mn | `critique-security-and-fit.md` P5 step 1 | `attack.txt` S4; `collation.txt` | `NonSpacingMark` | the same | yes | |
| F9 | S4, an entry holding only a look-alike of `KPG.Group` answers `Strings.Exists("KPG.Group")` true, and an ordinal reserved test does not catch it | `critique-security-and-fit.md` P5 step 2 | `attack.txt` S4 | `Exists("KPG.Group")` true for all four names that look like `KPG.Group`, `Exists("KPG")` true for both that look like `KPG`, and the ordinal test catches none of the four names it is given; culture `de-DE` | the same, culture `de-DE` | yes | depends on the current culture, see the caveats |
| F10 | S4, setting `KPG<U+034F>` on an entry holding `KPG` replaces the value of `KPG` and keeps its key | `critique-security-and-fit.md` P5 step 2 | `attack.txt` S4 | `keys [3 chars]`, the second value read back | the same | yes | |
| F11 | S4, culture `IsPrefix(name, "KPG.")` true for the look-alike prefixes | `critique-security-and-fit.md` P5, "Why minor" | `attack.txt` S4; `collation.txt` | true for the three names that begin with a look-alike of `KPG.` | the same | yes | |
| F12 | S4, an entry carrying `K<U+034F>PG.Group` added at the root: the root refuses, U and X unchanged | `critique-security-and-fit.md` P5 step 4; `digest.md` §4 | `attack.txt` S4 | `root REFUSE (a text and no seal) \| U RULE RG on the node \| X RULE R0(wide) on the node` | the same | yes | |
| F13 | .NET Framework 4.8 collates U+034F as nothing in en-US, de-DE, tr-TR and lt-LT | `critique-security-and-fit.md` P5 step 2 | `collation.txt` | `cmpToKPG=0` for `KP<U+034F>G` and `KPG<U+034F>` in all four cultures | the same 27 lines | yes | the script compares with the culture's own `CompareInfo`, so it does not depend on the machine's culture |
| F14 | run in Windows PowerShell 5.1 on .NET Framework 4.8 | `critique-security-and-fit.md` §5 | `collation.txt` | `runtime: 4.0.30319.42000` | the same | yes | |

## Claims no probe here backs

**Presented as measured, with no probe or output behind them.** These are what the branch cannot
yet vouch for from this family.

| Claim | Quoted in | What was found |
| --- | --- | --- |
| "Each has a change that closes it, the first three measured here" | `critique-survival.md` §0 | The third change, a random nonce in each seal, has no variant in the survival probe, so only the first two are measured (V in C1 to C3, the prune without records in C4). `digest.md` §3 item 3 states this too. Not run here either |
| "Tests: 37 files under the product at `a42e38aa` name a `CustomDataKey` [M here, a search]" | `critique-security-and-fit.md` §2 (e) | No search output was recorded. Checked here, read only: `git grep -l CustomDataKey a42e38aa019d572c5a10b489e6aadeea91ef91c1` names 37 files, 12 under `src/` and 25 under `tests/`. The count holds for the whole tree; for the tests the paper lists it under, it is 25 |
| The KeePassXC save "adds empty standard strings to rule entries and seals [M design]" | `critique-survival.md` §3 | The design's probe prints the names of the rule entry's strings only (D4), not their values and nothing of the seals |
| A KeePass and KeePassXC merge of scenario 08 "took the newer rule entry and both copies' other entries [M probe 08]" | `design.md` §5.3 row 1 | The rule in force is printed (D10); which entries U holds afterwards is not |
| "KeePassDX treats two attachments as one on 32 bits of a hash [M android §5.1]" | `design.md` §1.1 | Section 5.1 of `papers/ground/ground-android.md` is one of its "Hazards found while reading", a reading of the source, and `measured-android.md` does not measure it. The mark should be `[R android §5.1]` |

**A count read from source, checked the same way.** "The 44 `operationId`s" of
`Spec/openapi.yaml` (`critique-security-and-fit.md` §2 (a)) is marked as read, not measured.
`git grep -E "^\s+operationId:" a42e38aa019d572c5a10b489e6aadeea91ef91c1 --
src/KeePassGate/Spec/openapi.yaml` gives 44.

**Measured by another family, and not run here.** The papers also quote measurements of the
ground reports, which their own families carry, all under `spikes\storage\probes\`:
`[M keepass §n]` in `ground-keepass\`, `[M keepassxc §n]` in `ground-keepassxc\`,
`[M others §n]` in `ground-others\`, `[M spike]` and `[M measured §n]` (`ground-measured.md`) in
`ground-measured\`, and `[M inventory §8]` (31 profiles, 4482 characters) in `ground-inventory\`.
The Android measurements the digest calls `M android` (`papers/ground/measured-android.md`) are in
`spikes\storage\android\`. Claims marked `[R ...]` or `[A]` are reading and assumption, and no
probe is owed for them.

## Changes made to run them here

- **No probe source was changed.** `design\probe\Program.cs`, `survival\probe\Program.cs`,
  `security-and-fit\probe\Attack.cs` and `security-and-fit\collation.ps1` are byte for byte the
  files that produced the recorded output. `Attack.cs` holds a literal U+034F inside a string, and
  the copy keeps it.
- **A project file for each probe**, `Rules.csproj`, `Survival.csproj` and `Attack.csproj`, beside
  its source. The probes had none: each was compiled with the .NET Framework's own compiler
  against a copy of `KeePass.exe` in its directory. The projects build the same source for
  `net48`, under the assembly names the programs had, and reference `$(KeePassExe)` from
  `spikes\storage\Directory.Build.props`, with `Microsoft.NETFramework.ReferenceAssemblies`
  1.0.3. The compiler is therefore the SDK's, at C# 7.3, in Release, where the recorded runs used
  the Framework's compiler; the sources need nothing beyond C# 5, and the outputs are identical.
- **A `run.ps1` for each paper's directory**, which provides KeePass and KeePassXC through the
  tools, builds, clears `work\`, and passes the probe the arguments the recorded runs used, with
  full paths: for the design `work\kp`, `work\cost` and `work\xc`, as its recorded databases were
  laid out; for the security probe `work\` in place of the `work2\` its paper names, since
  `work\` is the directory the branch ignores.

**Recorded output.** Copied as the runs left it. The six design outputs, `survival.txt`,
`attack.txt` and `collation.txt` hold no machine path, user name or session identifier, and are
byte for byte the originals. The three `cli.txt` files held the full path of the agent's working
directory, a session scratch directory under a user's profile; it is replaced by the branch path
`spikes\storage\probes\rules-on-node\security-and-fit`, and nothing else in them changed. They are
in German, as `keepassxc-cli` answered on the machine that ran them, and so are this run's.

## Not carried

- **Every database**: 441 `.kdbx` files of the design's run, 114 of the survival run and 49 of the
  security runs, all made by the probes, which make them again.
- **The build output** in each `bin\`: the compiled `Rules.exe`, `Survival.exe` and `Attack.exe`,
  rebuilt from the sources, and the copies of `KeePass.exe` beside them, which the branch provides
  with the same hash.
- **The security critic's first run**, in its `work\`: its `attack.txt` was overwritten by the
  second run in `work2\`, which is the run the paper quotes. Only its three `cli.txt` logs were
  left, the same as the second run's apart from the directory, so they add nothing.

## Caveats and defects

- **No defect was found that changes a quoted result.** The design's probe does not check a seal's
  identifier against its strings, which `design.md` §2.3 makes part of the read; no scenario it
  runs changes a seal's strings, so no cell depends on it. The design's probe keeps the seals in
  the KeePassGate group itself rather than in a subgroup, as `design.md` §2.3 describes; the
  security probe uses the subgroup and agrees with it wherever the two overlap.
- **S4 depends on the culture.** KeePass's string dictionary compares names with the current
  culture, and S4 prints the culture it ran under: `de-DE` in the recorded run and in this run.
  `collation.txt` shows the same collation in en-US, tr-TR and lt-LT, but a run under another
  culture is not covered by either.
- **The timings of D19 to D21** are single runs of 20 rounds in memory with no gate around them.
  Under the load of these runs they came out between about the recorded value and three times
  it, so they are an order of magnitude and not a number to compare designs by.
