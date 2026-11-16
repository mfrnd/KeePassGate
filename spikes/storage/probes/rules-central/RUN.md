# Probes of `rules-central`

<!-- cspell:ignore Hinzugefügt -->

The option that keeps each version of a node's access rule as an entry in the plugin's own group,
naming the node by UUID. Three papers quote programs that ran for it, all under
`papers\options\rules-central\`: the design (`design.md`, written as `option-rules-central.md`),
its survival critique (`critique-survival.md`) and its security critique
(`critique-security-and-fit.md`). The digest (`digest.md`) repeats a few of their numbers. In the
tables below "design 6.3" means section 6.3 of `design.md`, and so on for the other three.

Every probe here ran again from the branch on 2026-11-15, on the machine the papers were measured
on, and every outcome the papers quote came out the same. What differs is timing, one random
identifier, the paths inside `keepassxc-cli`'s own messages, and four claims that the output itself
contradicts: the length of the rule a size was measured on, the number of scenarios in which
KeePassXC wrote KDBX 4.1, B's password "in every cell", and "174 host tests". Those are marked
"differs" in the tables; section 8 lists the claims no output on the branch shows at all.

| Directory | Paper | What runs | Command, from the branch root |
| --- | --- | --- | --- |
| `design\` | the design | `probe\Program.cs`, built as `Rc.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\design\run.ps1` |
| `survival\` | the survival critique | `scripts\Rcs.cs`, built as `Rcs.exe`, which runs `scripts\pk_del.py` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\survival\run.ps1` |
| `security-and-fit\` | the security critique | `scripts\probe.ps1` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\security-and-fit\scripts\probe.ps1` |

Each writes into `out\` beside it, which is not committed, and each file it writes has its recorded
copy under the same name in `recorded\`. The databases go to `work\` or stay in memory; none is
kept. What a run needs: Windows PowerShell 5.1, the .NET SDK (the projects target `net48` through
`Microsoft.NETFramework.ReferenceAssemblies` 1.0.3), and for the survival probe `uv` on `PATH`.
KeePass 2.62 and the KeePassXC releases are provided and checked by `tools\KeePass.ps1` and
`tools\KeePassXC.ps1`, which every command above calls.

The directories are named `design\`, `survival\` and `security-and-fit\`, as `probes\README.md`
lays out and as the papers' paths already name them.

## What the agents ran, and that the branch runs the same programs

- **KeePass.** Each agent loaded a copy of KeePass.exe from its own directory (`bin\` twice, `kp\`
  once). All three copies have the SHA-256 of `.keepass\2.62\KeePass.exe`, which every run here
  loads: `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`.
- **KeePassXC.** The design's probe ran `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 out of the
  `keepassxc` ground report's downloads, and the survival probe ran 2.4.2 and 2.8.0 out of its own
  `xc\`. Each of those six executables is byte for byte the `keepassxc-cli.exe` that
  `.keepassxc\<version>\` holds here (SHA-256 compared).
- **How the C# probes were built.** Both agents compiled their one source file with the .NET
  Framework's own `csc`, with no project file. The executables they left are 64-bit (machine
  `0x8664`) and carry no `DebuggableAttribute`, so the JIT optimized them. The projects added here
  build the same source as a 64-bit executable of the same name, at C# 5, and the commands build
  them as Release, which gives the same JIT optimization. For the one result that could depend on
  the build, the timings, the agent's own `Rc.exe` was run beside the branch's: section 1 has both.

## 1. `design\`: the design's probe

**What it measures.** Fourteen scenarios over two copies of one database (group U ruled, entry X
ruled, entries Y and Z, group W, the store group holding the rule versions), each merged in both
directions under KeePassLib 2.62's Synchronize, Overwrite if newer, Overwrite existing and Keep
existing (112 cells), and by `keepassxc-cli merge -s` in four releases (112 cells); every result
saved, reopened and read by the design's read rule, which the probe implements itself. Beside that:
an import of the database into itself with Create new IDs, then Delete Duplicate Entries; Delete
Duplicate Entries on the store as written; a plain save by each KeePassXC release; the cost of the
read pass at 10,060 and 50,060 entries. Cited by design 2.1, 2.2, 3, 4, 5.4, 6, 7, 8, 10, 11, 13
and 15, by the survival critique as `[M design]` (2, 4, 5), by the security critique's M2, and by
the digest (2, 5).

**Command.** `run.ps1` above. It empties `work\`, builds `probe\Rc.csproj` into `bin\`, runs
`Rc.exe` as `keepass`, `cost` and `xc` for each release, and writes `signatures.txt`.

**Output.** `out\keepass.txt`, `out\cost.txt`, `out\xc-2.4.2.txt`, `out\xc-2.6.6.txt`,
`out\xc-2.7.12.txt`, `out\xc-2.8.0.txt`, `out\signatures.txt`; the recorded copies in `recorded\`.
After it, `scripts\kdbx-versions.ps1` writes `out\kdbx-versions.txt` from the databases in `work\`
(section 6):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\design\scripts\kdbx-versions.ps1
```

**How long.** About 30 seconds here, three times: the build 1 to 2 s, `keepass` 4.0 to 5.7 s,
`cost` 1.7 to 2.2 s, `xc` 2.4.2 and 2.6.6 6.5 to 7.4 s each, 2.7.12 and 2.8.0 3.4 to 4.3 s each.
The record holds no durations; its first lines put the `keepass` run 51 seconds before the `cost`
run, and the four KeePassXC runs 7, 7 and 3 seconds apart.

**Compared.** Three times, every line of `keepass.txt`, the four `xc-*.txt` and `signatures.txt`
equal to the record but the first, which holds the time of the run. `cost.txt` differs in its
timings only. A run leaves 553 databases in `work\`, as many as the agent's run left.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| Store group `FE4040A9E0FAE884FA8BECAEEE9FBB46`, mark `AED8E7D3` | design 2.1, 2.2 | as claimed | the same | yes | second line of every output |
| 112 KeePassLib cells, 14 scenarios, both directions, four methods, each saved and read back | design 6, 7, 15; digest 2; survival 2 ("224 cells"), 4 | 112 lines | 112 | yes | |
| 112 `keepassxc-cli merge -s` cells, four releases | design 6, 7, 15; survival 2 ("224 cells") | 112 | 112 | yes | all exit 0 |
| The four releases gave the same results; 2.4.2 equals 2.6.6, 2.7.12 equals 2.8.0, and the pairs differ in one line, the KDBX versions | design 7, 15 | as claimed | the same | yes | apart from the version in each label |
| A plain KeePassXC save (`add`) kept every rule entry's identifier, strings, protection flags and times, in all four releases | design 2.2, 7 | `unchanged: yes` four times | the same | yes | |
| That save kept an empty `KPG.After` | design 2.2 | within the line above | the same | yes | the probe writes an empty value where the design has `none`; `none` was run by the survival probe |
| That save kept the group's two flags and notes, and the file at KDBX 4.0 | design 7 | `searching False, auto-type False, notes kept yes`, `KDBX 4.0`, four times | the same | yes | |
| A rule entry "with a 50 character rule and one predecessor holds 232 characters of strings" | design 2.2 | 232 | 232 | differs | 232 reproduces, but the entry measured holds R1, 33 characters; no rule in the probe is 50 characters. Section 7 |
| With the largest readable rule, 4,482 characters, about 4.7 KB | design 2.2; digest 5 | | 199 + 4,482 = 4,681 characters | yes | arithmetic on the row above; the 4,482 is the inventory's |
| Every identifier tested for the mark: 0.17 ms at 10,060 entries, 0.86 ms at 50,060 | design 2.2, 4 | 0.167, 0.859 | 0.171 to 0.246, 0.939 to 1.596 | differs | timing, nine runs; see below |
| The same and every unmarked entry asked for `KPG.Kind`: 1.13 ms, 8.16 ms | design 2.2, 4 | 1.131, 8.162 | 1.205 to 1.769, 6.843 to 14.586 | differs | timing |
| 5000 deletion records tested: 0.03 ms at both sizes | design 4 | 0.031, 0.030 | 0.032 to 0.040, 0.031 to 0.040 | yes | 0.03 to two places in eight of nine runs, 0.04 in the ninth |
| The whole reading: 1.43 ms, 9.38 ms | design 4; security M2; digest 5 | 1.426, 9.383 | 1.500 to 2.435, 8.317 to 12.426 | differs | timing |
| One index lookup a user entry: 0.14 ms, 1.06 ms | design 4 | 0.143, 1.061 | 0.152 to 0.233, 0.889 to 1.430 | differs | timing |
| Today's read, `CustomData.Get("KPG")` on each: 0.55 ms, 4.54 ms | design 4 | 0.553, 4.541 | 0.597 to 1.012, 4.511 to 9.088 | differs | timing |
| About 0.19 microseconds an entry, linear from the figures above | security M2 | 0.199 from the record | 0.16 to 0.26 over the nine runs here | differs | arithmetic, marked `[A]` by the critique |
| Scenario 01: X and U carry A's new rules in every cell | design 6.1, 11 | R1 and RG1 in 16 of 16 | the same | yes | |
| Scenario 01: B's password is there too, in every cell | design 6.1 | `pw-x-B` in 14 of 16 cells | the same | differs | KeePassLib Overwrite existing into B and Keep existing into A keep `pw-x-0`, as those methods do with any entry; the rule half of the row holds |
| Scenario 02, the same second: A's rule in every cell | design 6.1, 11; survival 2 | R1 in 16 of 16 | the same | yes | |
| Scenario 03: both versions arrive in all 16 cells and X carries their meet, `{"default":"deny","ops":[],"reader":[]}` | design 3 point 5, 6.2; survival 5 | 16 of 16 | the same | yes | |
| Scenario 04: no rule, as A decided, in every cell | design 6.1; survival 5 | 16 of 16 | the same | yes | |
| Scenario 05: both first rules arrive | design 6.1 | 16 of 16 | the same | yes | |
| Scenario 06: the same decision made in two copies is one entry, one rule | design 2.2, 6.1; survival 2 | 3 versions, R1, 16 of 16 | the same | yes | in every cell the merge also leaves one history item on that rule entry, since the copies wrote it at different times |
| Scenario 07: a newest rule entry deleted for good refuses; Synchronize carries it to the other copy; a KeePassXC merge brings the entry back and the rule is in force | design 7, 8; survival 2, 5 | as claimed | the same | yes | KeePassLib's other methods: into B refuses, into A R1 |
| Scenario 08: a rule entry moved into the recycle bin counts where it is | design 8; survival 5 | R1 in 16 of 16 | the same | yes | moving the group was not run; section 8 |
| Scenario 09: a `KPG.` string edited by hand refuses; Synchronize and a KeePassXC merge carry the edit and the other copy refuses | design 8; survival 5 | as claimed | the same | yes | Overwrite existing into B and Keep existing into A serve the unedited R1 |
| Scenario 10: the rule follows the moved node; Synchronize and KeePassXC move X to W; the other methods leave X where the target had it | design 6.3, 8, 11; survival 5 | as claimed | the same | yes | |
| Scenario 11: Synchronize leaves X gone with two rule entries without a node; the other methods the same into B and X with R1 into A; KeePassXC keeps X with R1 | design 6.3, 8 | as claimed | the same | yes | |
| Scenario 12: X back with its rule in every cell | design 6.3 | R0 in 16 of 16 | the same | yes | |
| Scenario 13: Synchronize refuses both ways; the other methods refuse into A and keep R0 into B; KeePassXC brings the rule entry back into A, where R0 is in force, and keeps R0 into B | design 5.4, 6.3, 13; survival 5 | as claimed | the same | yes | design 5.4's "make the node refuse" holds for KeePassLib; into A, KeePassXC puts the rule back in force instead, as design 6.3 states |
| Scenario 14: Synchronize refuses both ways; the other methods refuse into A and keep the older R0 into B; KeePassXC refuses into A and keeps R0 into B | design 5.4, 6.3, 13 | as claimed | the same | yes | |
| KDBX versions of every file KeePassLib wrote: 4.0 | design 7, 10 | `4.0` | `4.0` | yes | all 553 databases of a run were read; the only four that are not 4.0 are KeePassXC targets |
| KeePassXC 2.7.12 and 2.8.0 wrote 4.1 "for the one scenario in which a merge moved an entry", scenario 10 | design 10 | the output holds only `4.0, 4.1` | 4.1 in scenarios 08 and 10, both into A, in both releases | differs | two scenarios, not one; the recorded run's own databases, still in the agent's working directory, show the same four files. Both are merges that moved an entry, so the explanation stands and the count does not |
| Create new IDs, a copy imported into itself: the originals and their rules untouched; a second group named `KeePassGate` under a new identifier holding 3 entries; the copies of U and X carry no rule | design 6.4, 8 | as claimed, `3 copies` | the same | yes | |
| Delete Duplicate Entries after that import: 7 deleted; X, Y and Z gone under their own identifiers; the rule entries left only as copies, and U still ruled by a copy | design 6.4; survival P1, P6, 4 | `7 deleted`, `store: 0 versions, 3 copies, 3 deletion records` | the same | yes | |
| Delete Duplicate Entries on the store as written: 0 deleted of 6 | design 6.4, 8; survival 4 | as claimed | the same | yes | |
| Each `keepassxc-cli` carries a valid signature from DroidMonkey Apps, LLC | design 15 | four lines, `Valid` | the same, byte for byte | yes | the agent's command for this file was not kept; `run.ps1` writes it in the same form |

**The timings.** Nine runs of the branch's `Rc.exe cost` here: three through `run.ps1`, three on
their own, and three alternating with three runs of the agent's own `Rc.exe`. Every figure at
10,060 entries came out above the record. At 50,060 entries the string test, the whole reading, the
lookup and today's read fell on both sides of it, and the mark test and the deletion records above
it. The agent's build gave the same spread as the branch's when they alternated (the whole reading
at 10,060 entries: 1.864 to 1.903 ms for the agent's build, 1.500 to 1.909 ms for the branch's), so
the difference is the machine's load at the time, not the build. The conclusions the design draws
from the table hold in every run: nearly all of the reading is the string test that finds copies,
one index lookup a node costs less than today's `CustomData.Get`, and the deletion records cost
almost nothing.

## 2. `survival\`: the survival critique's chains

**What it measures.** Five chains, C1 to C5, over databases built as the design's section 2.2
builds them, with `none` for a first rule: the Rules page's Remove, a hand deletion, the repair of
section 5.2, the store group deleted for good, and a hand edit, each followed by KeePassLib merges,
`keepassxc-cli` 2.8.0 and 2.4.2 merges, a pykeepass deletion, and a model of KeePassDX 4.5.8's
merge that the critic wrote from its source. After every step it applies the design's read rule
and prints what the profile `agent` reaches at X. Every line from the model is labeled `DX-model`:
it shows what the source describes, executed, not what KeePassDX does. Cited throughout the survival
critique, sections 1 to 5 and 8, and by the digest (2).

**Command.** `run.ps1` above. It empties `work\`, builds `scripts\Rcs.csproj` into `bin\`, and
runs `Rcs.exe` with the work directory, the output file, `keepassxc-cli` 2.8.0 and 2.4.2 from
`tools\KeePassXC.ps1`, `uv`, and `scripts\pk_del.py`.

**Output.** `out\chains.txt`; the recorded copy `recorded\chains.txt`.

**How long.** 3.8 and 2.5 s for the chains in two runs, about 6 s with the build. The critique
gives 4.5 seconds.

**Compared.** Twice, every result equal to the record. Four lines differ, 14, 15, 37 and 51, and
only inside the text `keepassxc-cli` printed, which the probe keeps, cut at 160 characters: that
text names the database's path, so it names this run's directory where the record names the
agent's, and where the path is shorter more of the program's message shows. 2.8.0 answers in the
machine's language, German, and 2.4.2 in English. The fresh `out\chains.txt` therefore holds this
machine's path; it is not committed.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| C1: the Rules page's Remove in A, then the KeePassDX model merging A into B: `agent: [read,update,list] from U` where B had `[read,list]`, nothing refusing | survival 1, P1 C1, 3 | as claimed, line 8 | the same | yes | model, not KeePassDX |
| C1 beside it: Synchronize with B open refuses; Overwrite if newer and Keep existing keep RX1 | survival P1 C1 | lines 10 to 12 | the same | yes | |
| C1: `keepassxc-cli` 2.8.0 and 2.4.2 merging A into the KeePassDX result leave the widening, 0 deletion records | survival P1 C1, 4 | lines 14, 15 | the same | yes | |
| C1: a Synchronize of that file with A puts the refusal back | survival P1 C1, 2 | line 13 | the same | yes | |
| C2: P refuses after RX2 is deleted for good; the model merging P into Q gives `[read,list]` where Q had `[list]` | survival 1, P1 C2 | lines 19, 20, 22 | the same | yes | model |
| C2: pykeepass 4.2.0 deletes RX2 with no record (`deletion records 0 -> 0`), and the file serves `[read,list]` | survival P1, P1 C2, 4 | line 25 | the same | yes | `UV_EXCLUDE_NEWER` holds the resolution to what the recorded run could resolve |
| C2: a Synchronize with P refuses again; with the base it restores `[list]` | survival P1 C2, 2 | lines 23, 24, 26 | the same | yes | run for the model's file with P and with the base, and for the pykeepass file with the base only; section 8 |
| C3: the repair on an unrelated OK writes RX1's document naming 2 predecessors, and P serves `[read,list]` | survival P2 C3 | lines 29, 30 | the same | yes | |
| C3: S, still holding RX2, serves `[list]`, then `[read,list]` after each of seven merges with the repaired P (four KeePassLib methods, `keepassxc-cli` 2.8.0 and 2.4.2, the model) | survival 1, 2, P2 C3 | lines 31 to 38 | the same | yes | |
| C4: the group deleted with `DeleteAllObjects` leaves the store absent and 3 deletion records of rule entries, and X refuses | survival P1 C4, 5 | line 41 | the same | yes | U's refusal and the group's own record are not printed; section 8 |
| C4: Q widens U a minute later; Synchronize of Q and G refuses at X; the model gives `[create,read,update,list] from U`, store absent, no records | survival 1, P1 C4 | lines 42 to 44 | the same | yes | model |
| C4 continued: the repair writes `{"default":"inherit"}` naming 2, X gets `[read,update,list]` from U, and a `keepassxc-cli` 2.8.0 merge into the base gives the same | survival P2 | lines 45, 46 | the same | yes | |
| C5: one space added to RX2's `KPG.Data` refuses; the repair gives `[read,list]`; a Synchronize with the copy not repaired keeps `[read,list]` | survival P2 C5 | lines 49, 50, 52 | the same | yes | |
| P3: `keepassxc-cli` 2.8.0 brings the changed entry back (it reports `Hinzugefügt`) and X refuses again | survival P3, 4 | line 51 | the same | yes | |
| `KPG.After` written as `none` reads back intact after `keepassxc-cli` 2.4.2 and 2.8.0 | survival 2 | lines 14, 15, 36, 37 | the same | yes | the entries read as versions, which needs their stored strings to derive their identifiers |
| Five chains, 40 results | survival 8 | 40 result lines | 40 | yes | |
| 4.5 seconds | survival 8 | not in the record | 3.8 s and 2.5 s | differs | timing |

## 3. `security-and-fit\`: the security critique's probe

**What it measures.** Two parts, in memory, under KeePassLib 2.62. P1: what `PwGroup.Duplicate`
and `PwEntry.Duplicate` do today to a rule kept as node custom data. P2: a foreign database under
another master key, sharing only the public store group identifier and one group's identifier,
holding one rule entry built as the design's section 2.2 builds rule entries, merged into the
victim under Synchronize, Keep existing and Create new IDs. Cited by the security critique, "What I
ran", S1, S4, M5 and lens (c), and by its list of claims at the end.

**Command.** The script itself, above.

**Output.** `out\probe.txt`; the recorded copy `recorded\probe.txt`.

**How long.** 1.2 s.

**Compared.** Twice, every line equal to the record but the fifth, which prints the victim group C's
identifier: `PwGroup` draws it at random, so it is noise (`FC84A49A71E1D7428161B9A718B05612` in
the record, `1338DB86BEBB61458B7D122437F0DB2F` here).

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| A duplicated group: new identifiers throughout, the rule on the copy, the entry's password on the copy | security P1, S4, lens (c) | line 2 | the same | yes | |
| A duplicated entry: a new identifier and the rule on the copy | security P1, S4 | line 3 | the same | yes | |
| Synchronize: the planted entry lands marked in the store group; the store group takes the foreign name, `Shared passwords`, with searching and auto-type back to inherit; C's and X's times unchanged, X without history | security P2, S1 path B, M5 | line 6 | the same | yes | |
| Keep existing: marked, in the store group, which is unchanged | security P2 | line 7 | the same | yes | |
| Create new IDs: an unmarked copy in a new group named after the foreign group; the store group unchanged | security P2, S1 path A | line 8 | the same | yes | "with its four strings intact" is half printed; section 8 |
| Victim C's identifier | security P2 (not quoted) | `FC84A49A71E1D7428161B9A718B05612` | `1338DB86BEBB61458B7D122437F0DB2F` | differs | random identifier, noise |
| KeePass.exe loaded from a copy whose SHA-256 matched the original, `AFB89B74`, ending `C89C3F3` | security "What I ran" | | the agent's copy has that hash; this run loads the pinned file itself | yes | |

## 4. `security-and-fit\scripts\count-tests.ps1`: a reconstruction, not a probe of the papers

The security critique's lens (e) counts test attributes in eleven files "[M here, a search of the
test tree]", and the digest adds them up as "174 host tests". No command or output was kept. This
script, written on the branch, takes those eleven files out of the product at `a42e38aa` with
`tools\Product.ps1` and counts in each every `[Test` (which the critique's numbers equal), `[Test]`
and `[TestCase(`. Which files write rules as node custom data was the critique's judgment and is
taken from it. Command, a few seconds:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-central\security-and-fit\scripts\count-tests.ps1
```

Output `out\count-tests.txt`; there is nothing recorded to compare it with.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| Test attributes: `GrantEditorInRealDialogsTests` 36, `SkippedOnlyForAReaderTests` 29, `DecidedAtTheReadTests` 27, `TagOfTheCallersViewTests` 25, `AclInRealDatabaseTests` 12, `MergeInRealDatabaseTests` 10, `SnapshotHazardTests` 10, `RemovalMergeTests` 9, `DatabaseNodesTests` 6, `DecidedAtTheWriteTests` 5, `LiveGrantEditorTests` 5 | security lens (e) | none kept | every count equal to the occurrences of `[Test` | yes | each count includes the file's one `[TestFixture]` |
| "174 host tests write rules as node custom data" | digest 5 | the sum of the row above | 174 occurrences, of which 11 are `[TestFixture]`: 163 tests, 87 `[Test]` and 76 `[TestCase(` | differs | 174 counts the fixtures as tests |

## 5. What was replaced in what was copied

The sources were copied unchanged apart from the one file section 6 names, and every source and
recorded output was searched for a user's directory, `AppData`, the scratch directory, the session
identifiers, the machine's name and the product's worktree. One file held any:
`survival\recorded\chains.txt`, where `keepassxc-cli`'s messages name the database it opened.
There, the agent's working directory written in full, twice (lines 15 and 37), became its place on
the branch, `spikes\storage\probes\rules-central\survival`; and two paths the probe had cut short
inside the session's scratch directory (lines 14 and 51) became `<scratch>`. Nothing else in any
file was replaced. `design\recorded\signatures.txt` holds the publisher's addresses as the
certificates carry them, which is public.

## 6. Every change made to run the probes here

- `design\probe\Program.cs`, `survival\scripts\Rcs.cs` and `survival\scripts\pk_del.py`:
  unchanged, byte for byte.
- `design\probe\Rc.csproj` and `survival\scripts\Rcs.csproj`: added. There was no project file; the
  agents compiled with `csc`. Each references KeePass by `$(KeePassExe)` and builds a 64-bit
  executable of the agent's name at C# 5, as the section on what the agents ran describes.
- `design\run.ps1` and `survival\run.ps1`: added, the commands the agents typed and did not keep,
  each step as the probe's `Main` takes its arguments. `survival\run.ps1` also sets
  `UV_EXCLUDE_NEWER` to 2026-11-16, because the probe asks `uv` for `pykeepass` with no version:
  a later run then resolves what the recorded run could, and the version the output prints shows
  which ran (4.2.0 in both).
- `security-and-fit\scripts\probe.ps1`: three lines and a comment of four added after its `param`,
  nothing else changed. When no argument names them, `KeePassExe` comes from `Get-KeePassExe` in
  `tools\KeePass.ps1`, dot-sourced in a child scope so that the tool's strict mode stays out of the
  probe's scope, and `OutFile` is `out\probe.txt`, whose directory is made. The agent passed a copy
  of KeePass.exe and an output path as arguments, which still works.
- `design\scripts\kdbx-versions.ps1`: added, a check rather than a probe. It reads the KDBX version
  out of the header of every database `run.ps1` left in `work\`, as the probe's own `KdbxVersion`
  does, because the probe prints only the set of versions it met.
- `security-and-fit\scripts\count-tests.ps1`: added, section 4.

## 7. Defects found in the probes

Reported, not fixed; neither changes an outcome the papers draw.

- **The design's probe labels the entry it measures wrongly.** `RunCost` prints "one rule entry
  with a 50 character rule and one predecessor holds 232 characters of strings" for
  `store.Entries.GetAt(1)`, which is the second version of the first ruled node, R1,
  `{"default":"inherit","reader":[]}`: 33 characters. The 232 is right for that entry (199
  characters of names and fixed values, and the rule). Design 2.2 repeats the label. Its estimate
  for the largest rule does not depend on it.
- **The design's probe prints only the set of KDBX versions**, so the scenario design 10 names for
  KeePassXC's 4.1 was not read off the output, and is one of two (section 1).

## 8. Claims no probe or output on the branch vouches for

Each is quoted as measured; the probe ran but prints nothing that shows it, or nothing ran it.

- **Design 6.1, scenario 01: "B's ... entries are there too".** B adds entry N to U and edits Z's
  user name; the probe never prints either.
- **Design 8: "Duplicates, copies or pastes a governed node: the copy carries no rule [M probe]".**
  No duplicate, copy or paste was run. The only copy the probe makes is the import with new
  identifiers, whose copies of U and X carry no rule (section 1). The security probe's P1 ran
  `Duplicate` on today's store, which is the other question.
- **Design 8: "Moves a rule entry, or the group, anywhere: nothing [M probe, scenario 08]".**
  Scenario 08 moves one rule entry into the recycle bin; no run moves the group.
- **Design 8 and 6.4: a duplicated rule entry or group, and the `503 undecided` it brings.** The
  probe counts the copies an import makes (3); it duplicates no single entry or group, and holds no
  gate, so the `503` is the design's rule and not a measurement.
- **Survival P1 C2: "A KeePassLib Synchronize of either file with P refuses again".** Run for the
  KeePassDX model's file; the pykeepass file was synchronized with the base only.
- **Survival P1 C4: "three rule-entry records and the group's. G refuses at U and at X".** The probe
  walks from X and stops at X's refusal, and counts only marked records; U's refusal and the
  group's own record are not in the output.
- **Security S1 path A, step 4: "an unmarked copy with its four strings intact".** The probe finds
  the copy by `KPG.Kind` and `KPG.Node`, so those two are shown to be there; `KPG.After` and
  `KPG.Data` are never printed.
- **Survival 8: KeePass.exe's signature (Dominik Reichl) "checked valid"; both KeePassXC archives
  matching `keepassxc.sha256`, "checked again here".** Nothing was recorded. Every run here checks
  both again through `tools\KeePass.ps1` and `tools\KeePassXC.ps1`, and the section on what the
  agents ran compares the programs themselves.
- **Security lens (e): the test counts.** No command or output was kept; section 4 reconstructs
  them.

## 9. What was not carried, and why

The sources and every recorded output were carried, the outputs from each agent's `out\` into
`recorded\`. Nothing else was:

- `rules-central\bin\`, `rules-central-survival\bin\`: a copy of KeePass.exe each, and the
  agents' `Rc.exe` and `Rcs.exe`. Build output, and a program the branch provides with the same
  hash.
- `rules-central-security-and-fit\kp\KeePass.exe`: a copy of KeePass, the same hash.
- `rules-central\work\` (553 databases), `rules-central-survival\work\c1` to `c5`: databases, which
  the probes make again every run.
- `rules-central-survival\xc\2.4.2`, `xc\2.8.0`: KeePassXC releases extracted from their archives,
  which `tools\KeePassXC.ps1` provides from the pinned archives; the executables are identical.
- `rules-central\scripts\`: empty.
