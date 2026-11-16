# Probes of `rules-linked`

The option that keeps each version of a node's access rule as an entry of its own in the plugin's
rules group, witnessed by a record in the store group, with a reference in both directions: an
anchor on the node (the string `KPG.Anchor` on an entry, a link entry inside a group) and the
anchor and node in every version. Three papers quote programs that ran for it, all under
`papers\options\rules-linked\`: the design (`design.md`, written as `option-rules-linked.md`), its
survival critique (`critique-survival.md`) and its security critique
(`critique-security-and-fit.md`). The digest (`digest.md`) repeats many of their numbers. In the
tables below "design 5.2" means section 5.2 of `design.md`, "survival P1" the problem P1 of the
survival critique, "security S2" the problem S2 of the security critique, and so on.

Every probe here ran again from the branch on 2026-11-15, on the machine the papers were measured
on, three times for the design's and more for the critics', and every line of every recorded
output came out the same but for the moment of the run and one path (section 5). Every outcome the
papers quote from them holds. What differs is the wording of three claims about what was run or
counted: how many merge methods carried a planted history item (security S2), how many cases the
security probe adds, and how many host test files the security critique counted, the last through
a search written here, since the critic kept none. Those are marked "differs" in the tables.
Section 8 lists the claims no output on the branch shows.

| Directory | Paper | What runs | Command, from the branch root |
| --- | --- | --- | --- |
| `design\` | the design | `probe\Program.cs`, built as `Rl.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\design\run.ps1` |
| `survival\` | the survival critique | `probe\Program.cs`, built as `Rls.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\survival\run.ps1` |
| `security-and-fit\` | the security critique | `probe\Model.cs` and `probe\Mine.cs`, built as `Rlsf.exe` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\security-and-fit\run.ps1` |

Each writes into `out\` beside it, which is not committed, and each file it writes has its recorded
copy under the same name in `recorded\`. The databases go to `work\` beside it, which is not
committed either and which each command empties first; none is kept. After the three commands, one
more command compares every fresh output with its record:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\rules-linked\compare.py
```

It masks the moment of the run on the first line of an output, and in
`survival\recorded\signatures.txt` the path of the KeePass.exe whose signature was checked (section
5), prints every other difference as a diff, checks that the four KeePassXC outputs of the design
are alike once the release is taken out, and exits 0 when nothing differs. What a run needs:
Windows PowerShell 5.1, the .NET SDK (the projects target `net48` through
`Microsoft.NETFramework.ReferenceAssemblies` 1.0.3), git for the check of section 4, and `uv` for
the comparison. KeePass 2.62 and the KeePassXC releases are provided and checked by
`tools\KeePass.ps1` and `tools\KeePassXC.ps1`, which every command above calls.

The critics' directories are named `survival\` and `security-and-fit\`, as `probes\README.md` lays
out and as the papers' paths already name them.

## What the agents ran, and that the branch runs the same programs

- **KeePass.** Each agent loaded a copy of KeePass.exe from its own `bin\`. All three copies have
  the SHA-256 of `.keepass\2.62\KeePass.exe`, which every run here loads:
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`.
- **KeePassXC.** The design's probe ran `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, and both
  critics ran 2.4.2 and 2.8.0, all out of the `keepassxc` ground report's downloads. Each of those
  four executables is byte for byte the `keepassxc-cli.exe` that `.keepassxc\<version>\` holds here
  (SHA-256 compared).
- **How the C# probes were built.** All three agents compiled with the .NET Framework's own `csc`,
  with no project file. The executables they left carry no `DebuggableAttribute`. The design's
  `Rl.exe` is 64-bit (machine `0x8664`, PE32+); the critics' `Rls.exe` and `Rlsf.exe` are IL only
  for any processor, neither requiring nor preferring 32 bits. The projects added here build the
  same sources, unchanged, as executables of the same names and kinds, at C# 5, which is what that
  `csc` compiles, and the commands build them as Release. For the one result that could depend on
  the build, a running time, the survival agent's own `Rls.exe` was run beside the branch's
  (section 2).
- **Which source wrote which record.** Every recorded output of a probe was written after the
  probe's source was last changed, with one exception: `security-and-fit\recorded\probe.txt` was
  written four minutes before the agent's last change to `Mine.cs`, which added the `dedup` case
  and the branch in `Main` that runs it. The source as it stood for `probe.txt` was not kept. The
  final `Mine.cs` gives `probe.txt` again line for line, so whatever changed did not touch the
  cases it prints.

## 1. `design\`: the design's probe

**What it measures.** One base database (group U opened to `agent` for read and list, entry X in U
narrowed to nothing, entries Y and Z in U, group S sealed holding group C with entry Q, group W with
entry V, the store group and the rules group), the design's write path (a first rule on an entry as
an edit with a revision, a first rule on a group as a link entry, a rule change with the replaced
version deleted and recorded) and its read path, the walk of design 3.4 for the one profile
`agent`. Then: twelve scenarios over two copies of the base, each merged in both directions under
KeePassLib 2.62's Synchronize, Overwrite if newer, Overwrite existing and Keep existing (96 cells),
and by `keepassxc-cli merge -s` in four releases (96 cells), every result saved, reopened and read;
seventeen acts by hand in one file (h01 to h17); what `PwEntry.Duplicate`, `PwGroup.Duplicate`, the
clipboard's `KdbxFile.WriteEntries` and `ReadEntries`, and an import of the base into itself with
Create new IDs carry; a foreign file that plants a rule, under four methods; and the newest version
lost, then a stale copy merged in, by KeePassLib Synchronize and by each KeePassXC release. For
each KeePassXC release also a plain save (`add`) and an edit (`edit -u`). Cited by design 1, 2.2,
2.3, 2.5, 2.6, 3.2, 3.3, 5, 6, 7, 12 and 15, by the survival critique as `[M design]` (sections 1,
2, 4, 5, 6), by the security critique as `[M probe]` (verdict, S5, lens (c)), and by the digest
(sections 2, 3, 4).

**Command.** `run.ps1` above. It empties `work\`, builds `probe\Rl.csproj` into `bin\`, runs
`Rl.exe` as `keepass` into `work\kp` and as `xc` into `work\xc` for each release, and writes
`signatures.txt`.

**Output.** `out\keepass.txt`, `out\xc-2.4.2.txt`, `out\xc-2.6.6.txt`, `out\xc-2.7.12.txt`,
`out\xc-2.8.0.txt`, `out\signatures.txt`; the recorded copies in `recorded\`. After it,
`scripts\kdbx-versions.ps1` writes `out\kdbx-versions.txt` from the databases in `work\` (section
4).

**How long.** About 30 seconds here, three times: the build about 2 s, `keepass` 5.1 to 7.1 s, `xc`
2.4.2 and 2.6.6 6.8 to 7.5 s each, 2.7.12 and 2.8.0 3.5 to 4.5 s each. The record holds no
durations; its first lines and the files' times put `keepass` at about 4 s and the four KeePassXC
runs at about 6, 6, 3 and 3 s.

**Compared.** Three times through `run.ps1`, and `keepass` once more on its own: every line of
`keepass.txt`, the four `xc-*.txt` and `signatures.txt` equal to the record but the first, which
holds the moment of the run. A run leaves 540 databases in `work\`, as many as the agent's run
left in its own.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| 96 KeePassLib cells: 12 scenarios, four methods that match identifiers, both directions, each saved, reopened and read | design 1, 5.2, 15; digest 2 | 12 scenarios, 8 cells each | the same | yes | |
| 96 `keepassxc-cli merge -s` cells, four releases, the four outputs the same line for line once the release's name is taken out | design 1, 5.3, 15; digest 2 | 12 scenarios, 2 cells each, four files alike | the same | yes | `compare.py` checks the likeness in the record and in the run |
| 17 acts by hand in one file | design 1, 5.4, 15 | h01 to h17 | the same | yes | |
| No merge cell and no single act by hand gives `agent` more than the operator's last decision, or more than the narrower of two | design 1, 5.3, 6; digest 2 | every cell of 01 to 12 and every act but h10 | the same | yes | a reading of all 192 cells; h10 is three acts, and the plant and the lost version are not among the twelve scenarios |
| 01, a first rule that narrows Y, B edits Y later: the reference gone in 6 KeePassLib cells, kept in 2; `agent` nothing in all 8 | design 2.2, 5.2; survival 2, 5 | `Y back ref no` in 6, `yes` in 2; `Y@U=-` in all 8 | the same | yes | |
| 02, a first grant on V, B edits V later: the reference gone in 6, kept in 2; nothing in the 6, read in the 2 | design 1, 2.2, 3.3 row 4, 5.2; digest 4 | `V@W=-[unconfirmed]` in 6, `V@W=read[agree]` in 2 | the same | yes | |
| 03, as 01 in the same second: gone in 4, kept in 4 by direction; nothing in all 8 | design 2.2, 5.2 | as claimed | the same | yes | |
| 04, a rule change in A, an edit of X in B: the new rule in all 8, the reference untouched | design 5.2, 7; survival 2; security verdict | `X@U=list[agree]` in all 8 | the same | yes | |
| 05, X changed to list in A and to read in B: the narrower, nothing, in all 8 | design 3.2, 5.2; survival 2 | `X@U=-[agree]` in all 8 | the same | yes | |
| 06, two first rules on Y: one rule confirmed, the other a limit; nothing in all 8 | design 5.2; survival P6 | `Y@U=-[agree+unconfirmed]` in all 8 | the same | yes | |
| 07, a first grant on group W in A, B adds N to W and moves W's time: link and rule arrive, the grant in force for N too, in all 8 KeePassLib cells and all 8 KeePassXC cells | design 2.3, 5.2; survival 2 | `N@W=list,read`, `V@W=list,read` in 8 and in 2 per release | the same | yes | |
| 08, two first rules on W: both links confirmed, the narrower, read, in all 8 | design 5.2 | `V@W=read`, `W:agree+agree` in all 8 | the same | yes | |
| 09, X, S and U duplicated: the copy of X limited to nothing, the copy of S sealed, the copy of U opens nothing, the originals unchanged, in all 16 cells | design 2.3, 5.2, 7 | as claimed in 8 and in 2 per release | the same | yes | the copy of S sits at the root, which grants nothing, so its `Q@C=-` alone does not show the seal; section 7 |
| 10, X's rule entry deleted for good in A: X refuses in 5 cells, B keeps the rule in the 3 where the deletion does not arrive | design 5.2, 7 | as claimed | the same | yes | |
| 11, U's link moved into W in A: U's grant suspended and W "a moved link, ignored" in 5 cells, unchanged in 3 | design 5.2 | as claimed | the same | yes | |
| 12, X's reference deleted by hand in A: gone in 6, kept in 2; nothing in all 8 | design 5.2 | as claimed | the same | yes | |
| KeePassXC: the later entry whole both ways (01, 02, 12 lose the reference in both directions); the target at a tie (03 kept into A, gone into B); no deletion applied (10 back and in force both ways) | design 5.3 | as claimed, four releases | the same | yes | |
| A plain KeePassXC save left every plugin entry unchanged and X's `KPG.Anchor` kept, X otherwise unchanged but for the three empty standard fields KeePassXC adds | design 2.2, 5.3, 6 | the probe's verdict line reads `NO`; its listing differs in X's line only, by `Notes=`, `URL=` and `UserName=` | the same | yes | the verdict compares X as well as the plugin entries, which is why it prints `NO`; the design's wording accounts for it |
| `edit -u` on X kept the reference and made a revision that carries it | design 2.2, 5.3 | `KPG.Anchor kept True`, `history items 1`, `carries KPG.Anchor True`, four releases | the same | yes | |
| h01 X's reference made text that is no anchor: refuses | design 3.3 row 7, 5.4 | `REFUSE(itself: its reference will not read)` | the same | yes | |
| h02 an anchor nothing carries: refuses | design 3.3 rows 2 and 7, 5.4 | `REFUSE(... not in this database)` | the same | yes | |
| h03 U's anchor: nothing, limited by both rules | design 3.3 row 7, 5.4 | `X@U=-[copy+unconfirmed]` | the same | yes | |
| h04 the reference deleted: nothing, X's rule as a limit | design 3.3 row 4, 5.4 | `X@U=-[unconfirmed]` | the same | yes | |
| h05 Y given X's anchor: nothing, limited by X's rule | design 3.3 row 5, 5.4 | `Y@U=-[copy]` | the same | yes | |
| h06 V given U's anchor, a grant's: nothing | design 3.3 row 7, 5.4 | `V@W=-[copy]` | the same | yes | |
| h07 X's rule entry edited, `KPG.Data` widened: refuses | design 3.3 row 3, 5.4 | `REFUSE(itself: a recorded rule version is missing)` | the same | yes | the edit changes the derived identifier, so the reason printed is the record's |
| h08 the rule entry removed with no trace, its record kept: refuses | design 3.3 rows 3 and 10, 5.4, 7; survival 2, 5 | as claimed | the same | yes | |
| h09 rule entry and record removed: refuses | design 3.3 row 2, 5.4, 7 | `REFUSE(... not in this database)[dangling]` | the same | yes | |
| h10 as h09 and the reference too: X inherits read and list | design 3.3 row 11, 5.4, 12; survival 5; digest 3 | `X@U=list,read` | the same | yes | |
| h11 the rules group removed: U, S and X refuse, with everything under U and S | design 5.4, 7 | `Q`, `X`, `Y`, `Z` refuse, `V@W=-` | the same | yes | |
| h12 both plugin groups removed: the same | design 3.3 row 2, 5.4, 7 | as claimed | the same | yes | |
| h13 U's link deleted for good: nothing under U | design 5.4, 7 | `Y@U=-`, `Z@U=-`, `U:unconfirmed` | the same | yes | |
| h14 S's link deleted for good: S still sealed | design 5.4, 7 | `S:unconfirmed`, `Q@C=-` | the same | yes | S sits at the root, which grants nothing, so `Q@C=-` alone does not show the seal; the state shows the limit applies. Section 7 |
| h15 X's rule entry moved to the root, its record into U: unchanged | design 2.6, 5.4; survival 2 | as claimed | the same | yes | |
| h16 X copied, then Delete Duplicate Entries: it deleted the original and kept the copy, still limited to nothing | design 5.4, 7 | `deleted 1; the original X is gone`, `X@U=-[copy]` | the same | yes | |
| h17 X restored to its revision from before its first rule: nothing, the rule as a limit | design 4.1, 5.4 | `X@U=-[unconfirmed]` | the same | yes | |
| `PwEntry.Duplicate` keeps `KPG.Anchor` under a new identifier | design 2.2, 5.1 | `new identifier True, KPG.Anchor kept True` | the same | yes | |
| `PwGroup.Duplicate` copies the link under a new identifier, its anchor still the original's | design 2.3, 5.1 | all four `True` | the same | yes | |
| `KdbxFile.WriteEntries` then `ReadEntries` keeps `KPG.Anchor` under a new identifier | design 2.2, 5.1 | `1 entry, new identifier True, KPG.Anchor kept True` | the same | yes | |
| Create new IDs into itself: the copy of X limited, the copy of S sealed, the copy of U opens nothing | design 5.1, 7 | `X@U=-[copy]`, `S:copy`, `Y@U=-`, `Z@U=-` for the copies | the same | yes | the copy of S as in 09 |
| A foreign file with rule entries alone: C and V not confirmed, nothing granted, under Synchronize, Keep existing, Overwrite existing and Create new IDs | design 1, 5.5, 7 | `C:unconfirmed`, `V@W=-[unconfirmed]`, four times | the same | yes | |
| With a link inside a group carrying C's identifier: Q read, list, protected under Synchronize, Keep existing and Overwrite existing; V still nothing | design 1, 5.5; security S5; digest 3 | `Q@C=list,protected,read` three times | the same | yes | |
| With the link, Create new IDs: nothing granted, the arriving group refuses | design 5.5 | `Q@C=-`, `C:copy-broken` | the same | yes | no entry sits in the arriving group, so the refusal shows only as its state |
| The newest version lost: refuses before any merge and after the stale copy, by KeePassLib Synchronize and four KeePassXC releases | design 2.5, 5.6; survival 2, 5 | `REFUSE(... a recorded rule version is missing)` five times | the same | yes | |
| The newest version and its record lost: refuses before; KeePassLib Synchronize refuses; KeePassXC, four releases, the older wider rule in force | design 1, 5.6, 12; survival P2 | `X@U=list,protected,read[agree]` four times | the same | yes | |
| What KeePass writes is KDBX 4.0 | design 6 | not printed for KeePassLib's files | every KeePassLib file 4.0 (section 4) | yes | KeePassXC 2.7.12 and 2.8.0 wrote 4.1 for scenario 11 into B, which the design does not claim either way; the agent's databases show the same two |
| Each `keepassxc-cli` carries a valid signature from DroidMonkey Apps, LLC | design 15 | four lines, `Valid` | the same, byte for byte | yes | the agent's command for this file was not kept; `run.ps1` writes it in the same form |
| KeePass.exe loaded from a copy with SHA-256 `AFB89B74...C89C3F3` | design 15 | | the agent's copy has that hash; this run loads the pinned file itself | yes | |

## 2. `survival\`: the survival critique's probe

**What it measures.** The design's own model, its first 494 lines and the helpers `E`, `G`,
`AnchorOfX`, `InstsOf`, `RecordOf` and `Cli` copied unchanged (compared here line for line), over a
base of its own (U opened to `agent`, X in U narrowed to nothing, Y in U, W holding S sealed with Q,
and V). Six chains: C1a to C1d, copies of S by a drag and by Duplicate Group, Delete Duplicate
Entries and a hand deletion of the copy's link; C2, the copy of X with its reference deleted; C3,
the newest version and record deleted with deletion records, then a stale copy by four KeePassLib
methods and by `keepassxc-cli` 2.4.2 and 2.8.0; C3b and C3c, both plugin groups deleted, then the
stale copy by Synchronize, by Synchronize after clearing the deletion records, and by KeePassXC;
C4, a phone clock a day ahead against Confirm; C5, the rules group deleted for good in one copy,
then Synchronize both ways and a KeePassXC merge; C6, an import with Create new IDs, then Delete
Duplicate Entries. Cited throughout the survival critique as `[M here]` and by the digest (sections
2, 3, 4).

**Command.** `run.ps1` above. It empties `work\`, builds `probe\Rls.csproj` into `bin\`, writes
`signatures.txt`, and runs `Rls.exe` with the work directory, the output file and `keepassxc-cli`
2.4.2 and 2.8.0.

**Output.** `out\survival.txt` and `out\signatures.txt`; the recorded copies in `recorded\`.

**How long.** 5.9 s the first time, cold, then 1.7 to 2.1 s in four runs; the agent's own
`Rls.exe`, run alternately with the branch's three times, 1.8 to 1.9 s. About 4 s with the build.
The record's first line and its file's time put the agent's run at about 1 s.

**Compared.** Eight times, twice through `run.ps1`, three more with the branch's `Rls.exe` and
three with the agent's own: every line of `survival.txt` equal to the record but the first, which
holds the moment of the run. A run leaves 41 databases in `work\`, as many as the agent's run left
in its `work2\`. `signatures.txt` differs in one path, section 5.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| C1a, a copying drag of S into U: the copy's link identifier is not its anchor, both links at `T0`, titles equal | survival P1 | `uuid==anchor False`, both `2026-01-01T00:00:00`, `titles equal True` | the same | yes | only the title of the strings is printed; that all strings are equal shows in C1b's deletion |
| C1a: the copy in U stays sealed | survival P1 step 1 | `Q@S=-` twice, `S:copy` | the same | yes | this copy sits in a granting group, so `-` does show the seal |
| C1b, the drag, a new secret in the copy, Delete Duplicate Entries: one entry deleted, the copy's link; the copy's Q2 then `list,read`, nothing refusing | survival 1, P1, 4, 5, 7; digest 2, 3 | `deleted 1: KeePassGate link@S(copy link)`, `Q2@S=list,read`, no `copy` state left | the same | yes | |
| C1c, the drag, then the copy's link deleted for good: the copy of Q `list,read` | survival 1, P1, 7; digest 2, 3 | `Q@S=list,read` for the copy | the same | yes | |
| C1d, Duplicate Group appending `- Copy` to the titles, moved into U, Delete Duplicate Entries: nothing deleted, the copy sealed | survival 2, P1 | `deleted 0`, `Q - Copy@S=-` | the same | yes | |
| C2, Duplicate Entry of X: limited to nothing; its `KPG.Anchor` deleted by hand: `X - Copy@U=list,read` | survival 1, P1, 7; digest 2, 3 | `X - Copy is - [copy]`, then `X - Copy@U=list,read` | the same | yes | |
| C3, version and record deleted with deletion records: X refuses; after the stale copy by four KeePassLib methods still refuses; by `keepassxc-cli` 2.4.2 and 2.8.0 `list,protected,read`, with three deletion records in the result | survival 1, P2, 4, 5, 7; digest 2, 3 | as claimed, `deletion records in the result 3` twice | the same | yes | |
| C3b, X narrowed and Y given a first rule since the stale copy, both plugin groups deleted with records: every ruled node refuses; Synchronize with the stale copy still refuses everywhere | survival P2 | as claimed | the same | yes | that the authority stays gone is not shown, section 8 |
| C3b by `keepassxc-cli` 2.4.2 and 2.8.0: X `list,protected,read`, Y refuses | survival P2, 4, 5; digest 3 | as claimed | the same | yes | |
| C3c, deletion records cleared, then Synchronize: the same result as KeePassXC | survival P2, 4, 7; digest 2, 3 | `X@U=list,protected,read[agree]`, Y refuses | the same | yes | |
| C4, the phone's clock a day ahead: after Synchronize V is `-` unconfirmed; Confirm at +20 opens V; the next Synchronize either way closes it; Confirm after +86401 holds | survival P5, 5, 6; digest 4 | as claimed | the same | yes | |
| C5, the rules group deleted for good in one copy: every ruled node refuses; one Synchronize either way leaves 0 rule texts in both and every ruled node refusing; a `keepassxc-cli` 2.8.0 merge of the intact copy heals it | survival 1, 2, P3, 4, 5; digest 4 | `rule texts left in the file: 0` twice; the KeePassXC line as the base | the same | yes | |
| C6, Create new IDs then Delete Duplicate Entries: all twelve originals deleted, 3 displaced versions and no intact version left, every ruled node and everything below it refusing | survival 1, P3, 4, 5; digest 4 | `deleted 12`: 3 records, 2 links, 3 versions, Q, V, X, Y; `intact 0, displaced 3` | the same | yes | V, under W with no rule, answers `-` |
| `keepassxc-cli` 2.4.2 and 2.8.0 and KeePass.exe carry valid signatures, DroidMonkey Apps, LLC and Dominik Reichl | survival 9 | three lines, `Valid` | the same | yes | the KeePass.exe line names another path, section 5 |
| Lines 1 to 494 and six helpers copied unchanged from the design's probe | survival 9 | | compared here line for line | yes | |
| The whole run took under 3 seconds | survival 9 | about 1 s by the record's own times | 1.7 to 2.1 s warm, 5.9 s cold | yes | timing |

## 3. `security-and-fit\`: the security critique's probe

**What it measures.** The design's own model, `Model.cs`, which is the design's `Program.cs` with
`partial` added and its `Main` renamed `DesignMain` (compared here: those are the only two lines
that differ), under seven cases in `Mine.cs`: `plant-confirm`, a foreign file that plants a rule
and an older copy of each entry carrying the planted anchor, merged by four KeePassLib methods and
by `keepassxc-cli` 2.8.0 and 2.4.2, then Confirm simulated; `midsave`, U's rule narrowed from
inside a save's progress callback, with the plugin's groups first and last in the tree; `paste`, a
sealed group's entries pasted into U through the clipboard's path; `template`, a link picked from
the template menu; `otherdb`, a ruled entry and group copied into a second database; `delgroup`, a
ruled group holding only its link removed with and without the link's deletion record, then
Synchronize; and `dedup`, U duplicated beside itself, then Delete Duplicate Entries. Cited by the
security critique's verdict, "What I ran", S1 to S4, M1 to M4 and lenses (a) and (c), and by the
digest (sections 2, 3, 4).

**Command.** `run.ps1` above. It empties `work\`, builds `probe\Rlsf.csproj` into `bin\`, and runs
`Rlsf.exe` twice: with the work directory, `out\probe.txt` and the pairs `2.8.0` and `2.4.2` with
their `keepassxc-cli.exe`, in the order the record prints them; then with the work directory,
`out\dedup.txt` and `dedup`.

**Output.** `out\probe.txt` and `out\dedup.txt`; the recorded copies in `recorded\`.

**How long.** 10.7 s for `probe` the first time, cold, then 1.3 to 1.6 s in three runs; `dedup`
0.2 s. About 4 s with the build. The record's first line and its file's time put the agent's
`probe` run at about 1.5 s.

**Compared.** Four times, every line of `probe.txt` equal to the record but the first, which holds
the moment of the run, and `dedup.txt` equal entirely. The first line of `probe.txt` names
`rules-linked\probe\Program.cs`, the agent's path for the design's probe, which `Mine.cs` prints as
a literal; on the branch that file is `design\probe\Program.cs`. A run leaves 34 databases in
`work\`, as many as the agent's.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| `plant-confirm`: after the merge Q and Y are not confirmed, Q `-`, Y `list,read`, under Synchronize, Keep existing, Overwrite if newer and both KeePassXC merges | security S2 | `Q - [unconfirmed]`, `Y list,read [unconfirmed]`, five times | the same | yes | |
| The planted anchor lands in the entry's history "in 5 of 6 methods and both KeePassXC merges", and S2 is "measured in 5 of 6 KeePassLib methods and both KeePassXC releases" | security S2 | `Q's history carries it True` under Synchronize, Keep existing, Overwrite if newer and both KeePassXC merges; `False` under Overwrite existing | the same | differs | six merges ran, four KeePassLib methods and two KeePassXC releases, and the history carries the anchor in 5 of the 6: 3 of 4 KeePassLib methods and both KeePassXC merges. No KeePassLib method beyond those four was run |
| After Confirm: Q `list,protected,read`, confirmed, the victim's password intact | security S2; digest 3 | `Q list,protected,read [agree] (password the victim's True)` five times | the same | yes | |
| Overwrite existing takes the foreign entry whole: the planted anchor on Q, the rule confirmed with no press, Q's password not the victim's | security M4, S2; digest 3 | `Q carries the planted anchor True`, `Q's password is the victim's False` | the same | yes | |
| `midsave`, the plugin's groups written first: the file holds the older, wider rule (`Y list,read`) while memory holds the narrower (`Y -`); the save completes and clears `Modified` | security S4; digest 3 | as claimed, `trigger after entry 13 of 13` | the same | yes | |
| `midsave`, the plugin's groups written last: memory and file agree | security S4 | `the file reopened: Y -`, `trigger after entry 7 of 13` | the same | yes | |
| `paste`: Y and Z in U go from `list,read` to `-` | security S3, lens (c); digest 4 | `Y@U=-`, `Z@U=-`, `U:agree+copy` | the same | yes | |
| `template`: the link is listed among the templates; the new entry carries `KPG.Kind` `link`; Y and Z in U `-` | security M1; digest 4 | as claimed | the same | yes | the menu's list is the probe's own reading of the group's entries, not KeePass's menu |
| `otherdb`: the copied entry and everything under the copied group refuse | security M2; digest 4 | `REFUSE(... not in this database)` four times | the same | yes | |
| `delgroup`: with the group's deletion record only, R comes back holding its link both ways; with the link's record too, R is gone | security M3, lens (a); digest 3 | as claimed | the same | yes | |
| `dedup`: Delete Duplicate Entries deleted 4; U holds 0 entries, its own link gone; U unconfirmed; the copies' Y and Z `-` | security S1, lens (c); digest 4 | as claimed | the same | yes | |
| `Model.cs` is the design's probe with only `partial` and a renamed `Main` | security "What I ran" | | two lines differ, those two | yes | |
| "`probe\Mine.cs` adds six cases" | security "What I ran" | seven cases in the outputs | the same | differs | the critique's next paragraph begins "The six cases" and names seven; the header comment of `Mine.cs` gives five |
| KeePass.exe loaded from a copy with SHA-256 `AFB89B74...C89C3F3` | security "What I ran" | | the agent's copy has that hash; this run loads the pinned file itself | yes | |

## 4. Two checks written on the branch, not probes of the papers

**`design\scripts\kdbx-versions.ps1`.** The design's probe prints a KDBX version only once per
KeePassXC release, after its plain save. This reads the version out of the header of every database
`run.ps1` left in `work\`, as the probe's own `KdbxVersion` does, and writes
`design\out\kdbx-versions.txt`. Run it after `design\run.ps1`, under a second:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\design\scripts\kdbx-versions.ps1
```

Three runs gave `work\kp`: 184 files, all 4.0; `work\xc`: 354 files 4.0 and 2 files 4.1, both
`xc<release>-11\intoB\target.kdbx` for 2.7.12 and 2.8.0, the merge that moved the link. The agent's
540 databases, still in its working directory, show the same two files at 4.1 and every other at
4.0. There is nothing recorded to compare with; the result is in the design's table above.

**`security-and-fit\scripts\count-tests.ps1`.** The security critique's lens (e) quotes a search
of the test tree, and M5 a search of `KdbxVault.cs`, "[M here]", and kept neither command nor
output. This repeats both with `git grep` over the repository's own history at the commit
`tools\Product.ps1` names, which reads that commit and changes nothing, and writes
`security-and-fit\out\count-tests.txt`. Under a second:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\rules-linked\security-and-fit\scripts\count-tests.ps1
```

The test pattern is a reconstruction: `CustomDataKey|NodeFormCustomDataField|m_sdCustomData`,
every occurrence counted, is the pattern tried here that gives all four per-file counts the critique
quotes. It was chosen for that, so the per-file counts agreeing with it shows they are consistent
with such a search, not that they were checked independently. It also matches
`ProfileNames.CustomDataKey`, the profile list's key, so the script searches a second time with the
rule's key alone, `AclDocument.CustomDataKey`.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| `GrantEditorInRealDialogsTests` 40, `TagOfTheCallersViewTests` 18, `SkippedOnlyForAReaderTests` 7, `PendingChangesTests` 63 in the unit tier | security lens (e) | none kept | 40, 18, 7, 63 with the reconstructed pattern | yes | with the rule's key alone: 39, 18, 7 and 0; each of `PendingChangesTests`' 63 is the profile list's key |
| "27 host-test files name the rule's custom-data key or the dialog's copy" | security lens (e); digest 5 | none kept | 27 files in both tiers: 23 host test files and 4 unit test files; with the rule's key alone, 18 files, all host | differs | 27 counts the unit tier's files as host files, and counts files that name only the profile list's key |
| No `KPG.` guard in the vault: "zero matches in `KdbxVault.cs`" | security M5 | none kept | 0 occurrences of `KPG.`; 1 of `KPG`, in a comment | yes | |

## 5. What was replaced in what was copied

The sources and every recorded output were searched for a user's directory, `AppData`, the scratch
directory, the session identifiers, the machine's name and the product's worktree. One file held
any: `survival\recorded\signatures.txt`, which names the three programs whose signatures the agent
checked by their full paths. There, the two `keepassxc-cli.exe` inside the `keepassxc` ground
report's downloads became `.keepassxc\2.4.2\keepassxc-cli.exe` and
`.keepassxc\2.8.0\keepassxc-cli.exe`, where this branch provides the same files, and the agent's
copy of KeePass.exe became `spikes\storage\probes\rules-linked\survival\bin\KeePass.exe`, its place
on the branch. Nothing else in any file was replaced. A run writes that file with
`.keepass\2.62\KeePass.exe`, the file it checks, which is why `compare.py` masks that one path.
`design\recorded\signatures.txt` and `survival\recorded\signatures.txt` hold the publishers'
addresses as the certificates carry them, which is public.

## 6. Every change made to run the probes here

- `design\probe\Program.cs`, `survival\probe\Program.cs`, `security-and-fit\probe\Model.cs` and
  `security-and-fit\probe\Mine.cs`: unchanged, byte for byte (SHA-256 compared with the agents'
  files). So are the recorded outputs, but for the one file section 5 names.
- `design\probe\Rl.csproj`, `survival\probe\Rls.csproj` and `security-and-fit\probe\Rlsf.csproj`:
  added. There was no project file; the agents compiled with `csc`. Each references KeePass by
  `$(KeePassExe)` and builds the agent's executable name at C# 5: 64-bit for `Rl.exe`, any
  processor without a preference for 32 bits for the other two, as the executables the agents left
  are.
- `design\run.ps1`, `survival\run.ps1` and `security-and-fit\run.ps1`: added, the commands the
  agents typed and did not keep, each step as the probe's `Main` takes its arguments. The survival
  probe's databases go to `work\` where the agent's went to `work2\`, because the branch ignores
  `work\` and the probe takes the directory as an argument.
- `compare.py`: added, section 1's comparison.
- `design\scripts\kdbx-versions.ps1` and `security-and-fit\scripts\count-tests.ps1`: added,
  section 4.
- `cspell.yaml` at the branch root: eight identifiers of the carried sources added to its words.

## 7. Defects and weaknesses found in the probes

Reported, not fixed; none changes an outcome the papers draw.

- **A sealed group's copy is tested where nothing grants.** Scenario 09 and the import with Create
  new IDs put the copy of S at the root, and h14 deletes S's link where S sits at the root, which
  grants nothing. Q's answer, `-`, is then the same whether the seal limits the copy or not; what
  shows the seal applying is the group's state, `S - Copy:copy` and `S:unconfirmed`. The design's
  "the copy of S sealed" and "S still sealed" therefore rest on the state rather than on an answer.
  The survival critique's C1a, which drags S into the granting group U, is the run in which a copied
  seal holds where its place grants, and it does (section 2).
- **The design's plain-save verdict reads `NO`.** It compares X as well as the plugin entries, and
  KeePassXC adds three empty standard fields to X; the listing it prints beside the verdict shows
  that this is the whole difference (section 1).
- **`Mine.cs` counts its cases wrongly in its own header**, five where it runs seven. Cosmetic.
- **The source that wrote `security-and-fit\recorded\probe.txt` was not kept**: the agent changed
  `Mine.cs` after that run. The final source gives the same output, so this costs nothing here.

## 8. Claims no probe or output on the branch vouches for

Each is quoted as measured, or rests on a number, but no output here shows it.

- **Survival P2, C3b: "with the store group gone the gate has no authority", and after Synchronize
  "the authority stays gone".** The probe models no authority and prints no group's presence; it
  shows only every ruled node refusing.
- **Digest 3, item 5: after M3's fix, "KeePassXC and KeePassDX merges still restore it [M]".** The
  security probe ran `delgroup` under KeePassLib Synchronize only; the critique itself marks the
  KeePassXC and Android part as read.
- **Digest 3, item 9: a narrowing by moving a node undone "or a KeePassXC merge [M]".** No probe in
  this family moves a node; scenario 11 moves a link. The nearest run is scenario 10 of
  `rules-central`'s design probe.
- **Design 8 and digest 5: records "about 250 characters each".** Not marked as measured. The
  design's recorded listing (`xc-*.txt`, the plain save) gives, counting names and values, 272
  characters for the record of a first version and 290 to 317 for a first version with a short
  rule.
- **Security "What I ran": both `keepassxc-cli` "carried a valid Authenticode signature ...
  checked again before the run".** Nothing was recorded. `tools\KeePassXC.ps1` checks it on every
  run here, and the executables are the agents' (see the section on what the agents ran).
- **Security lens (e): the test counts.** No command or output was kept; section 4 reconstructs
  them.

The papers also quote measurements of other families as `[M keepass]`, `[M keepassxc]`,
`[M others]`, `[M spike]`, `[M central]`, `[M marker]`, `[M crit, ...]`, `[M android, interim]` and
the digest's `AND`. Those are traced where their probes are, not here: among them the cost of the
pass, "1.4 to 3.2 ms for about 10,000 entries" (design 3.5, digest 5), under `rules-central` and
`rules-marker-entry`, and everything about Keepass2Android, KeePassDroid and KeePassDX under
`spikes\storage\android\`.

## 9. What was not carried, and why

The sources and every recorded output were carried, the outputs from each agent's `out\` into
`recorded\`. Nothing else was:

- `rules-linked\bin\`, `rules-linked-survival\bin\`, `rules-linked-security-and-fit\bin\`: a copy of
  KeePass.exe each, with the pinned file's hash, and the agents' `Rl.exe`, `Rls.exe` and `Rlsf.exe`.
  Build output, and a program the branch provides.
- `rules-linked\work\` (540 databases), `rules-linked-survival\work2\` (41) and
  `rules-linked-security-and-fit\work\` (34): databases, which the probes make again every run.
- `rules-linked-survival\work\` (37 databases): an earlier run of the survival probe, three minutes
  before the recorded run, whose output was not kept and which has no `c6`.
- `rules-linked-survival\probe\Mine.cs.part`: a draft of the part of `Program.cs` after line 530,
  written before it. It builds C1d's copy another way and has neither C3c nor C6, so it is not what
  wrote `survival.txt`, and nothing compiles it.
- None of the three directories held a clone, a download or a log.
