# Probes of `grant-only`

The option in which access rules can only add: a grant is an entry written once in the plugin's
store group, a take-away is a withdrawal entry with a second witness in the database's custom data,
and nothing of the plugin's sits on a user's node. Three papers quote programs that ran for it, all
under `papers\options\grant-only\`: the design (`design.md`, written as `option-grant-only.md`), its
survival critique (`critique-survival.md`) and its security critique
(`critique-security-and-fit.md`). The digest (`digest.md`) repeats their numbers. In the tables
below "design 4.3" means section 4.3 of `design.md`, "survival P2" and "security S4" name a
critique's problem, and "digest 3.13" is line 13 of the digest's table in its section 3.

Every probe here ran again from the branch on 2026-11-15, on the machine the papers were measured
on. Every outcome the papers quote came out the same in every run, with one exception: one cell of
the design's extra KeePassXC run, which depends on timing and came out differently in one of six
runs (section 1.3). What else differs is timing, the time stamp each output starts with, and three
counts the output does not bear out as written: "10 of 10 cells" for an import, "7 of 30" tests,
and "4.0 to 4.8 ms", whose lower end comes from a run whose file was overwritten. Section 6 lists
what the papers claim was measured that no output on the branch shows.

| Directory | Paper | What runs | Command, from the branch root |
| --- | --- | --- | --- |
| `design\` | the design | `probe\Program.cs`, built as `Go.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\design\run.ps1` |
| `design\scripts\` | the design, its counts over the product's tests | `search.ps1` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\design\scripts\search.ps1` |
| `survival\` | the survival critique | `probe\Program.cs`, built as `Gos.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\survival\run.ps1` |
| `security-and-fit\` | the security critique | `probe\Mine.cs`, built as `Probe.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\security-and-fit\run.ps1` |
| `security-and-fit\scripts\` | the security critique, its counts over the product's tests | `search.ps1` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\grant-only\security-and-fit\scripts\search.ps1` |

Each writes into `out\` beside it, which is not committed, and each file it writes has its recorded
copy under the same name in `recorded\`. The databases go to `work\`, which each command empties
first and which is not committed either; none is kept. What a run needs: Windows PowerShell 5.1,
the .NET SDK (the projects target `net48` through `Microsoft.NETFramework.ReferenceAssemblies`
1.0.3, and two of them take `Newtonsoft.Json` 13.0.4 from NuGet), and git with the product's commit
in this repository's history. KeePass 2.62, the KeePassXC releases and the product's files are
provided and checked by `tools\KeePass.ps1`, `tools\KeePassXC.ps1` and `tools\Product.ps1`, which
the commands call.

The directories are named `design\`, `survival\` and `security-and-fit\`, as `probes\README.md`
lays out and as the papers' paths already name them. No critique of this option has a model of its
own, so there is no `critic-formal`.

## What the agents ran, and that the branch runs the same programs

- **KeePass.** Each agent loaded a copy of KeePass.exe from its own `bin\`. All three copies have
  the SHA-256 of `.keepass\2.62\KeePass.exe`, which every run here loads:
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`.
- **KeePassXC.** The design's probe ran `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, the survival
  probe the same four, and the security probe 2.8.0, all out of the `keepassxc` ground report's
  downloads. Each of those four executables is byte for byte the `keepassxc-cli.exe` that
  `.keepassxc\<version>\` holds here (SHA-256 compared).
- **The product.** The design's and the security critique's probes compile eight of the product's
  files: `Acl\AclDocument.cs`, `AclDocuments.cs`, `AclGrant.cs`, `AclInheritance.cs`,
  `AclResolver.cs`, `AclVerb.cs`, `VerbMask.cs` and `Transport\JsonBody.cs`. The agents' copies,
  with their carriage returns taken out, hash to the blob identifiers those files have at
  `a42e38aa`, so the copies are the commit's files. Here `tools\Product.ps1` exports them from the
  commit into `.product\` and the projects compile them from there.
- **Newtonsoft.Json.** `JsonBody.cs` needs it. Both agents linked the product's own build output,
  whose SHA-256 is `F0C07AF0E84D4DD4DA4BD7823BA4535BC0481B3BF623ED40B659B68147A6BB75`. The projects
  here reference `Newtonsoft.Json` 13.0.4, the version the product pins at that commit, whose
  `lib\net45\Newtonsoft.Json.dll` is that same file and is what the builds copy beside the probes.
- **How the C# probes were built.** No agent kept a project file. The design's and the security
  critique's agents compiled with the .NET SDK's Roslyn `csc` (`-langversion:7.3 -platform:x64`,
  without `-optimize` or `-debug`); their executables are 64-bit and carry the
  `DebuggableAttribute` value 263, which turns the JIT's optimizations off. The survival agent
  compiled with the .NET Framework's own `csc` with nothing but `/target:exe`; its `Gos.exe` is
  AnyCPU, runs as a 64-bit process, and carries no `DebuggableAttribute`, so the JIT optimized it.
  The projects added here build the same sources the same way: `Go.exe` and `Probe.exe` as 64-bit
  executables with `Optimize` off, which gives the same attribute value 263 (read back from the
  branch's builds), and `Gos.exe` as an AnyCPU Release build at C# 5, which the JIT optimizes too.
  That matters for one number only, the design's cost of the fold.

## Changes made to the probes, and why

Every source file is carried unchanged; every change is a file added beside it.

| Probe | Change | Why |
| --- | --- | --- |
| design | `probe\Go.csproj` added | the agent compiled with a command line and kept no project; the project holds only what `Go.csproj`'s comment lists |
| design | `run.ps1` added | the agent ran its steps by hand from Bash and kept no script; it runs them in the agent's order with the agent's arguments |
| design | the extra KeePassXC run, `xc-touched.txt`, written as part of `run.ps1` | the agent typed it as a Bash loop, which the session transcript holds; `run.ps1` does the same steps and appends the bytes the loop appended (an LF line, then the state file after its first line, whose lines end in CRLF) |
| design | `ver.txt` and `kdbx-versions.txt`, written as part of `run.ps1` | the agent wrote `ver.txt` with `Go.exe state` into its `work\` and printed the version tally with `od`, saving neither; both are rebuilt from that Bash line |
| design | `ver-all.txt`, written as part of `run.ps1` | added on the branch, with no record beside it: `Go.exe state` over every database the KeePassLib cells leave, to read the version claim off every file |
| design | `run.ps1 -Only <step>` | added on the branch: runs one step over the databases a whole run left, used to repeat `touched` (section 1.3) |
| design | `scripts\search.ps1` added | the agent counted with `grep` in a checkout of the product and kept the console output only; this asks the same patterns of the commit through `git grep`, and adds one labeled check line |
| design | not carried: the agent's `work\patch.py` and `work\patch2.py` | both edited `Program.cs` in place before the recorded runs (scenarios 16 and 17 and the slow key derivation; the `state` mode), so the carried source already holds what they did |
| survival | `probe\Gos.csproj` and `run.ps1` added | as for the design: no project, no script; the agent's one command line is what `run.ps1` runs |
| security | `probe\Probe.csproj` and `run.ps1` added | as above |
| security | databases in `work\` rather than `work2\` | the agent's recorded run used `work2\` after an earlier run in `work\`; `.gitignore` excludes `work\` only, and nothing the probe prints names the directory |
| security | `scripts\search.ps1` added | as for the design, with `Select-String`'s semantics: without case, lines counted, .NET patterns through `git grep -P` |

A defect in a probe that changes a result: none found. Two things a reader should know, neither of
which changes a quoted number. The design's `Effective` prints one mask per entry, a union of
letters that overstates a switch when the verb beside it comes from another row; the security
critique's M1 states this and uses it. The design's verdicts are read off that mask, but each
verdict it quotes is about a verb or a whole row, where the union is exact, and the critiques' yes
or no is the resolver's own answer. The security probe's withdrawal keys carry a fourth field, the
letters the row kept, and its reader skips a key without that field; every key it reads is a key
it wrote, so none is skipped.

**What was scrubbed.** Nothing the probes wrote names a machine path, a user or a session: every
recorded output is the agents' file byte for byte. The two `recorded\search.txt` and
`design\recorded\kdbx-versions.txt` are console output the session transcripts hold, with the
command lines kept: there the leading `cd` into the checkout was taken out and the checkout's path
became `<product>`. The sources needed no change; the design's comment "the review4 worktree" is
kept as the papers keep it, a worktree's name and no path.

## 1. `design\`: the design's probe

**What it measures.** Two parts. The model: 400 generated trees of 30 nodes, three profiles, rules
on 3 nodes in 10, a seal on 15 rules in 100 and an empty mask on 12 rows in 100, seed 20261115,
answered by the plugin's own `AclResolver` and by the grant-only resolver, under a lost rule, a
rollback, a copy, a displacement and the middle form's marks. The store: grant and withdrawal
entries over KeePassLib 2.62 databases, 15 scenarios merged under five methods in both directions
(150 cells) and 17 scenarios by `keepassxc-cli merge -s` in four releases (136 cells), every result
saved and read back; copies in memory; the cost of the fold over 10,070 entries; and the extra
KeePassXC run in which the stale copy was saved by KeePassXC first. Cited by design 1.2 to 5.5, 10
and 11, by the survival critique as `[M design]`, by the security critique's M1, and by the digest.

**Command.** `run.ps1` above. It exports the product's eight files, empties `work\`, builds
`probe\Go.csproj` into `bin\`, runs `Go.exe` as `model`, `keepass` and `xc` for each release, then
the extra KeePassXC run and the version reads.

**Output.** `out\model.txt`, `out\keepass.txt`, `out\xc-2.4.2.txt`, `out\xc-2.6.6.txt`,
`out\xc-2.7.12.txt`, `out\xc-2.8.0.txt`, `out\xc-touched.txt`, `out\ver.txt`,
`out\kdbx-versions.txt`; the recorded copies in `recorded\`. `out\ver-all.txt` has no recorded
copy.

**How long.** 277 and 282 seconds for two whole runs: the build 1 to 2 s, `model` 9.3 and 9.2 s,
`keepass` 5.1 and 5.7 s, each `xc` 53.9 to 60.6 s, the extra run 28.5 to 30.8 s, the version reads
under 2 s. The record holds no durations; the agent's transcript has `model` at 150 trees in 4.7 s
and the `keepass` and `xc 2.8.0` runs together in 51.6 s.

**Compared.** Twice whole, and the extra KeePassXC run four more times alone. `model.txt`,
`keepass.txt`, `ver.txt` and `kdbx-versions.txt` equal the record but for the first line, which
holds the time of the run (`kdbx-versions.txt` has none); `keepass.txt` besides in its last line,
the cost of the fold, in the second run. The four `xc-*.txt` differ besides only in the four "(the
merge took ... s)" lines of the slow key derivation. `xc-touched.txt` equals the
record line for line in five of six runs; section 1.3 has the sixth. A whole run leaves 696
databases in `work\`, as many as the agent's run left: 198 of the KeePassLib cells and the copies,
482 of the KeePassXC runs and 16 of the extra run.

### 1.1 The model and the KeePassLib cells

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| On rule sets with no narrowing the two models answer alike: 400 trees, 3,131 rules, 648,000 answers, 0 different | design 1.2 | `3131 rules`, `648000, different: 0` | the same | yes | |
| Today's documents read as grants: 13,442 answers permitted that today refuses, in 291 of 400 trees, none the other way | design 1.4 | model section 5 | the same | yes | the record adds that 387 of the trees hold a narrowing |
| One rule lost: wider in 1,025 of 3,175 today, 0 of 3,136 today with no narrowing, 0 of 3,175 grant-only | design 2, 4.1, 10; survival 1, 2, 5; digest 7 | model section 2 | the same | yes | |
| A rollback widened in 2,280, none without the older row holding a letter the newer lacks | design 4.1 | `2802 trials, wider in 2280`, unexplained `0` | the same | yes | 2,280 of 2,802 trials |
| A copy beside its original: wider in 1,552 of 4,428 today, 0 of 4,428 grant-only | design 4.1, 10; survival 2, 5; digest 2, 7 | model section 3 | the same | yes | |
| A node displaced to the root: wider in 494 of 8,576 today, 0 of 8,576 grant-only | design 4.1, 10; survival 2, 5; digest 7 | model section 6 | the same | yes | |
| The middle form, 1,723 marks: grants lost 0 of 3,158; record lost, node refers 0 of 1,723; copy with the reference 0 of 1,723; displaced, no mark above 0 of 975; both lost 847 of 1,723; copy without the reference 541 of 1,723; displaced out of a marked group 91 of 300 | design 1.5; survival P2; digest 3.1 | model section 7, all seven rows | the same | yes | |
| The generator: 30 nodes, three profiles, rules on about 3 in 10 nodes, a seal on 15 in 100, an empty mask in 12 in 100 rows, seed 20261115 | design 11 | the header prints trees, nodes, profiles and seed | the same | yes | the fractions are `Rule` in `Program.cs` |
| 15 scenarios, five methods, both directions: 150 cells | design 5.5 | 150 lines | 150 | yes | |
| 01, 03, 04: the union; neither letter; nothing from G, in 30 of 30 cells | design 4.1, 5.2, 5.5 | as claimed | the same | yes | |
| 02, 05, 15: the withdrawn grant stays away, the new grant only, G closed, every cell | design 4.2, 4.3, 5.5 | as claimed | the same | yes | 15 is the "10 of 10" of design 4.2 |
| 06, the withdrawal's entry deleted: with the key, cut in 9 of 10, the tenth Keep existing into B; without the key, back in 5 of 10 | design 4.3; survival 2 | as claimed | the same | yes | the five: Overwrite if newer, Overwrite existing and Keep existing into B, Create new IDs both ways |
| 07, a foreign file's grant on H: Q closed in 10 of 10 | design 4.1, 5.3, 5.5 | as claimed | the same | yes | 18 of 18 with KeePassXC, section 1.2 |
| 08, a foreign file's withdrawal: G closed in every cell | design 4.1, 5.5, 9; digest 4 | as claimed | the same | yes | |
| 09 to 14, moves and a deletion: the KeePassLib columns of design 4.2's table, 48 cells, and 12 more under Create new IDs with each original where the open copy had it | design 4.2; survival P2, 5; digest 3.3, 3.5, 3.11 | as claimed | the same | yes | |
| An import with new identifiers: copies with no grant, and "the copies of grant records name the original nodes and change nothing: 10 of 10 cells" | design 4.1 | every copied entry `........` and every original as the target or Synchronize has it, in all 30 Create new IDs cells | the same | differs | what is claimed holds in every cell; no set of ten is marked in the output (30 cells, 18 of the store scenarios, 12 of the moves) |
| Then Delete Duplicate Entries deleted the originals and left the copies, with no grant | design 4.1; survival 4 | copies section, last line | the same | yes | |
| A copy of Y, which has a grant of its own, had only G's; a copy of G under the root had nothing; a copy of Q put into G was readable | design 4.1; survival 2, 4 | copies section | the same | yes | |
| Two copies of a grant changed nothing, and a copy of a withdrawn grant stayed cut | design 5.2 | copies section | the same | yes | |
| A grant moved into the recycle bin by hand was out of force | design 5.4; survival P3 | `in bin 1` | the same | yes | |
| The results were KDBX 4.0 without a withdrawal key and 4.1 with a key | design 5.4 | `KDBX versions of the results: 4.0, 4.1`; `ver.txt`; the tally | the same | yes | over all 198 databases (`ver-all.txt`): 62 with a key, all 4.1; 136 without, all 4.0 |
| The fold: 4.0 to 4.8 ms for 10,070 entries with 60 grants and 10 withdrawals | design 5.4; digest 5 | 4.84 ms | 4.84 and 3.80 ms | differs | timing. The record holds 4.84; the 4.02 behind "4.0" is in the transcript only, from the agent's first `keepass` run, whose file the recorded run overwrote |
| The probe prints one mask per state, `G/Y=CRU..L.P` and `.RU....P` in "A alone" lines | security M1 | lines 7 and 37 | the same | yes | |

### 1.2 The KeePassXC cells

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| 17 scenarios, both directions, four releases: 136 cells, and the four releases gave the same verdict in every cell | design 5.5 | 34 cells in each file, all exit 0; the files equal but for the release, the time stamp, the slow merges' durations and the KDBX line | the same | yes | 2.4.2 and 2.6.6 wrote only KDBX 4.0 targets, 2.7.12 and 2.8.0 4.0 and 4.1; no paper quotes that |
| A plain save by each release kept every record's identifier, strings, flags and times | design 5.4; survival 4; digest 2 | `unchanged: yes`, four times | the same | yes | |
| 07: Q closed in 8 of 8; 18 of 18 with KeePassLib | design 4.1, 5.3; survival 2 | as claimed | the same | yes | |
| 08: G closed in every cell | design 4.1, 5.5 | as claimed | the same | yes | |
| 01, 03, 04 in 24 of 24 | design 4.1, 5.2 | as claimed | the same | yes | |
| 02, 05, 15, 17: the grant stays away; KeePassXC brought the deleted grant entry back and it stayed cut | design 4.2, 4.3, 5.5; survival 2, 5 | `cut 1` beside the withdrawal | the same | yes | 15 is the "8 of 8" of design 4.2 |
| 06 and 16: with the key, cut in 16 of 16; without it, back in all 16 | design 4.3 | as claimed | the same | yes | |
| 09 to 14: the KeePassXC column of design 4.2's table, 48 cells; 14 brings X back in both directions | design 4.2; survival 4, 5; digest 3.3 | as claimed | the same | yes | |
| With Argon2d at 128 MiB and 14 passes, "a merge took 2.4 s" | design 11 | 2.4 to 2.5 s for 2.8.0, 2.7 to 3.0 s for the others | 2.5 to 2.9 s for 2.8.0, 2.8 to 3.4 s for the others, two runs | differs | timing; the key derivation's parameters are `NewDb` in `Program.cs` |
| The extra run, 8 cells: 2.8.0 slow into A removed the key and the grant was in force again; 2.8.0 fast into B, the key did not reach B; the other six stayed cut | design 4.3; survival P5; digest 3.13 | as claimed | as claimed in five of six runs; in the sixth, 2.8.0 slow into B stayed as it was too | differs | timing in KeePassXC's merge of custom data, section 1.3 |

### 1.3 The cell that depends on timing

In the extra run, B is first saved by `keepassxc-cli add`, which gives B KeePassXC's own
`_LAST_MODIFIED` value in its custom data; A, written by KeePassLib, has none. The cell is 2.8.0
with the slow key derivation, A merged into B. In the record and in five runs here the merge
carried `KPG.Withdrawn.<id>` into B and the grant stayed cut; in the first whole run here it did
not, and the line reads:

```text
2.8.0-16/intoB.kdbx (KDBX 4.0): G/S/S1=.R...L..  G/X=.R...L..  G/Y=.RU..L.P  H/Bonly=........  H/Q=........  H/xc-touch*=........  [grants live 2, cut 0; withdrawals 0 entries, 0 keys]
```

where the record has

```text
2.8.0-16/intoB.kdbx (KDBX 4.0): G/S/S1=.R...L..  G/X=.R...L..  G/Y=.R...L..  H/Bonly=........  H/Q=........  H/xc-touch*=........  [grants live 1, cut 1; withdrawals 0 entries, 1 keys]
```

The extra run was made six times here: in two whole runs and four times alone over a whole run's
databases (`run.ps1 -Only touched`). Five of the six equal the record byte for byte; the first
whole run gave two cells as they were and five cut. The claim's two named cells, 2.8.0 slow into A
in force again and 2.8.0 fast into B as it was, came out so in all six.

The databases of the odd run, read with KeePassLib, show what happened: B's `_LAST_MODIFIED` read
`22:35:51` before the merge and the merged B still held that value and no key, so KeePassXC left
B's custom data alone; in the agent's databases the merged B has a `_LAST_MODIFIED` five seconds
later than B's and holds the key. The value is kept to the second. The probe, its inputs and the
release were the same in every run, so what decided the odd run is when its steps ran: the
machine's timing, not a defect in the probe and not a claim that fails. Which comparison of times
KeePassXC makes was not read here. The design lists the conditions behind this residue as a
measurement still needed (its 12.5), and survival P5 reads it as KeePassXC's merge of custom data
turning on whether two loads fall in one second.

## 2. `survival\`: the survival critique's probe

**What it measures.** Six sequences over databases made as the design's store makes them, each
merged under KeePassLib's five methods in both directions and by `keepassxc-cli merge -s` in four
releases in both directions: S1 and S1b, two grants on one node from two copies; S2, a place from
one copy and a grant from the other; S3, KeePassXC's two-way merge of a move, then KeePassLib's
Synchronize; S4 and S4b, a grant entry deleted or recycled by hand. Each result answers one
question with yes or no. Cited by survival 1, P1 to P3, 2, 4, 5, 6 and 8, and by the digest (3).

**Command.** `run.ps1` above. It empties `work\`, builds `probe\Gos.csproj` into `bin\`, and runs
`Gos.exe` with the work directory, the output file and each release's `keepassxc-cli`.

**Output.** `out\survival.txt`; the recorded copy `recorded\survival.txt`.

**How long.** 11.0 s for the probe, about 14 s with the build.

**Compared.** Every line equal to the record but the first, which holds the time of the run. A run
leaves 180 databases in `work\`, as many as the agent's run left.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| S1: `agent` reads Y's password in 18 of 18 cells; S1b: creates a protected value in 18 of 18; 36 of 36 | survival 1, P1, 5, 6; digest 3.8 | `YES` in all 36 | the same | yes | neither copy alone: `no` |
| S2: `agent` reads X in 14 of 18: KeePassLib 6 of 10 (Synchronize both ways, the other methods and Create new IDs into the copy that moved X), KeePassXC 8 of 8 | survival 1, P2, 5, 6; digest 3.10 | as claimed | the same | yes | |
| S3: A <- B keeps X in H with B's location time; B <- that puts X back in G at equal times and `agent` reads X, 4 of 4 releases; B <- A directly is closed in every release; Synchronize then closed into the first result, open into B | survival 1, P2, 4, 5, 6; digest 3.12 | `X location H @ +0h`, `G @ +0h`, as claimed | the same | yes | |
| S4: KeePassXC brings the deleted grant back in both directions, 8 of 8; Create new IDs open both ways; Synchronize closed both ways; the three other methods closed into A, open into B | survival 1, P3, 4, 5; digest 3.2 | as claimed | the same | yes | |
| S4b: KeePassXC into B open, 4 of 4, into A closed; Create new IDs open both ways; Synchronize closed | survival 1, P3, 5, 6; digest 3.2 | as claimed | the same | yes | |
| The design's fold is `rows[profile] = was \| m`, at `Program.cs:659` | survival P1 | line 659 of `design\probe\Program.cs` | the same | yes | read off the carried source |
| The whole run took 12 seconds | survival 8 | 12.2 s in the transcript, for the run that wrote the record; not in the file | 11.0 s | differs | timing |

## 3. `security-and-fit\`: the security critique's probe

**What it measures.** Four parts. The store: cases N1 to N3, D03, D04 and ADD, one row of `agent`
on G edited apart in two copies, merged under KeePassLib's five methods in both directions and by
`keepassxc-cli` 2.8.0 in both directions, each result read under the design's fold and under a
"cap" fold. The strip guard over the grant-only resolver before and after a grant record is lost.
A database in 0.1.0's shape read by today's `AclResolver` and by the grant-only fold. And a worked
example, an invented tree under four arrangements of one policy and five events. Cited by the
security critique's verdict, "What I ran", S1, S2, S4, M2 and lenses (a) and (d), and by the digest
(3).

**Command.** `run.ps1` above. It exports the product's eight files, empties `work\`, builds
`probe\Probe.csproj` into `bin\`, and runs `Probe.exe` with the work directory, the output file and
`keepassxc-cli` 2.8.0.

**Output.** `out\probe.txt`; the recorded copy `recorded\probe.txt`.

**How long.** 9.0 s for the probe, about 12 s with the build and the export.

**Compared.** Every line equal to the record but the first, which holds the time of the run. A run
leaves 103 databases in `work\`, as many as the agent's recorded run left in `work2\`.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| N1 and N2 give `.R.S.L..` and N3 `CRU..L..` under the design's fold in every cell, the meet under the cap fold; 10 KeePassLib and 2 `keepassxc-cli` cells each, 36 | security verdict 4, S4; digest 3.9 | as claimed | the same | yes | |
| Under the cap fold D03 and D04 give the design's verdicts in all 24 cells, ADD unites in 12, and S granted again after the merge holds | security S4 | as claimed | the same | yes | |
| The strip guard: refused before, permitted after the root's grant record is deleted | security M2 | as claimed | the same | yes | |
| The 0.1.0 database in the README's shape: today X and Z read, Q refused, list at the root; grant-only nothing; after the root is granted again everything, Q included; after the push-down X and Z, not Q, no list at the root | security verdict 2, S2; digest 3.14 | as claimed | the same | yes | |
| The worked example: four arrangements, 0 answers differing from the policy on 26 entries; 11 rule documents, 13 grants, 9 grants with 5 moves and 3 new groups, 4 grants and 7 marks | security S1, lens (a) | as claimed | the same | yes | |
| E1 to E5 under the four arrangements, the table of S1 | security verdict 1, S1, S3, lenses (a) and (d); digest 3.1 | as claimed | the same | yes | E4 today: read by `assistant`, not by `ci` |
| `out\probe.txt` has a SHA-256 beginning `5C82C042DF5E9305` | security, "What I ran" | `5C82C042DF5E9305...` | the fresh file differs in its first line | yes | the record's hash; no fresh run can have it |
| The eight product files are byte-identical to the worktree at `a42e38aa` | security, "What I ran"; design 11 | | blob identifiers equal | yes | see above |
| An earlier identical run under `work\` differs only in one label | security, "What I ran" | the earlier run's output, in the transcript: it differs from the record in line 41 alone, N3's meet `.R...L..` where the record has `CR...L..`, and in the time stamp | not run | yes | from the transcript only. The agent corrected that label in `Mine.cs` between the two runs, so the carried source prints the record's line |
| One run, 15 seconds | security, "What I ran" | 14.9 s, the earlier run's, in the transcript; the recorded run's was not printed | 9.0 s | differs | timing |

## 4. The counts over the product's tests

**What it measures.** How often the product's tests at `a42e38aa` use a narrowing, by the patterns
the two agents searched for. Cited by design 3.2 and by the security critique's lens (c).

**Command.** The two `search.ps1` above. Each asks the product's commit through `git grep` and
writes `out\search.txt`; `recorded\search.txt` holds the agent's commands and their console output
as the transcript has them, so the two files differ in form and are compared by their numbers.

**How long.** Under 2 s each.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| 18 uses of `Rules.Sealing(` in 8 files | design 3.2; security lens (c) | 18, 8 | 18, 8 | yes | both agents |
| 22 rules with an empty mask in 8 files | design 3.2 | 22, 8 | 22, 8 | yes | |
| 274 uses of `Rules.Granting(` in 32 files | design 3.2; security lens (c) | 274, 32 | 274, 32 | yes | both agents |
| 66 test methods in 28 files name a narrowing | design 3.2 | 66, 28 | 66, 28 | yes | |
| About 4,200 test attributes | design 3.2 | 4210 | 4210 | yes | |
| In `AclResolverTests`, 7 of 30 tests pin a narrowing | design 3.2 | 30 methods listed | 30; the design's own name pattern picks 5 | differs | the 30 reproduces; the 7 is the agent's reading of the list, which the paper calls rough, and no rule for it was kept |
| 17 lines name `AclDefault.Deny` in 9 files | security lens (c) | 17, 9, each line | the same | yes | |
| 16 lines in 4 client test files read `Access` as one mask per profile | security lens (c) | 16, 4 | 16, 4 | yes | |
| `GrantSheet`'s inherited mask and its test | security lens (c) | `InheritedMask`: `GrantSheet.cs` 6, `GrantPanel.cs` 2, `GrantSheetTests.cs` 1 | the same | yes | |

## 5. What was not carried, and why

- **Every database**: 696 in the design's working directory, 180 in the survival critique's, 206 in
  the security critique's (`work\` and `work2\`). Each run makes its own.
- **Build output**: each agent's `bin\` with `Go.exe`, `Gos.exe` and `Probe.exe`, the copy of
  KeePass.exe and `Newtonsoft.Json.dll`. The builds here make the first three, `tools\KeePass.ps1`
  provides KeePass, and NuGet the DLL.
- **The product's files** in the design's and the security critique's `src\`: exported from the
  commit instead, so no file of the product is on this branch.
- **The design's `work\patch.py` and `work\patch2.py`**: already applied to the carried source
  (section "Changes").
- **The design's `work\touched\state.txt`**: the last of the extra run's intermediate files, whose
  content is the record's last section.
- **The `cli-*.txt` files** the survival probe leaves beside each KeePassXC cell: `keepassxc-cli`'s
  own messages, which name the database's path and no paper quotes.

## 6. What the papers call measured and no output on the branch shows

- **Design 5.4, the lower end of "4.0 to 4.8 ms"**: the 4.02 ms is in the transcript only (section
  1.1).
- **Security, "What I ran"**: the earlier run "differs only in one label". It does, but only the
  transcript holds that run's output (section 3); no file on the branch does, and the carried
  source cannot print it again.
- **Marks that point elsewhere**: `[M spike]`, `[M android]`, `[M central]`, `[M linked]`,
  `[M keepassxc]` and `[M critic rules-...]` in these papers are measurements of
  `spikes/revocation-merge`, of `spikes\storage\android\`, of the other options' families and of
  the ground reports, and are traced in their own `RUN.md`, not here.
- **What the critiques propose and did not run**, which they mark `[A]` themselves: the switch
  bound to its grant record (survival P1), the take-away by any refusal-only trace (survival P3,
  "Measure S4 and S4b with the changes"), re-signing at Recreate (security S5), the check over the
  `KPG.` strings (security M6). Of the changes the papers name, only the cap fold was run (security
  S4), as the digest states.
