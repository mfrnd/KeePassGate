# single-entry: the probes and what they reproduce

The family behind `papers/options/single-entry/`: the design (`design.md`), its two critiques
(`critique-survival.md` and `critique-security-and-fit.md`) and the digest (`digest.md`), which
quotes the same numbers. Three directories ran probes for those papers, one for each of the first
three. Each lives in the directory of the paper that ran it, as `probes/README.md` lays out,
keeping the layout its agent gave it. All three are Windows PowerShell 5.1 scripts over KeePass
2.62's KeePassLib, built on one library, `Lib.ps1`, which the design wrote and both critics copied
unchanged (the three copies here are byte for byte the same):

| Directory | Probe | What it measures | Cited by |
| --- | --- | --- | --- |
| `design\` | `scripts\Cells.ps1` | eight scenarios S1 to S8 of two copies writing the one state entry, each merged by `PwDatabase.MergeIn` under four methods in both directions (64 cells) and by `keepassxc-cli merge -s` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 in both directions (64 cells); what each merge left and what the reconciling step makes of it, with and without the plugin's memory | `design.md` §0, §1, §2.1, §3.1 to §3.5, §6.3, §6.4, §8, §14; `critique-survival.md` §1, §3, §6; `critique-security-and-fit.md` S4; `digest.md` §2, §4 |
| `design\` | `scripts\Rounds.ps1` | ten chains of twelve rounds; documents of 100,000, 1,000,000 and 7,000,000 characters; Create new IDs; the state entry deleted for good and into the bin, with the recovery; what a read costs | `design.md` §0, §2.1, §3.2, §3.3, §3.5, §5.4, §6.1, §6.4, §7.2, §8, §12, §14; `critique-survival.md` §2.2, §6; `digest.md` §2 to §5 |
| `survival\` | `scripts\Survival.ps1` | W1 and W1b (a copy 300 versions behind under a window of 256, three readings of the step), W2 (witnesses that `keepassxc-cli` 2.8.0 keeps), C1 (Continue, then the lost version turns up), K1 (a restored state entry through kdbxweb 2.1.1), D1 (Create new IDs, then Delete Duplicate Entries) | `critique-survival.md` §1, §2, §3, §5, §6, §9; `digest.md` §2 to §5 |
| `security-and-fit\` | `scripts\Probe.ps1` | part W (a copy that wrote nothing, merged after the other wrote 10 and 70 versions, under the design's probe window of 64) and part D (Create new IDs of a copy, then Delete Duplicate Entries) | `critique-security-and-fit.md` "What I ran", "What was checked and holds" item 5, S3, lens (c); `digest.md` §4 |
| `security-and-fit\` | `scripts\Plant.ps1` | a foreign file holding only a crafted document under the fixed identifiers, merged into a victim under Synchronize, Overwrite if newer and Keep existing, foreign earlier and later | `critique-security-and-fit.md` S1, S4, lens (a); `digest.md` §3 item 1, §4 |

All five ran in this run, and every recorded file comes out again: after `compare.py` takes away
the identifiers a run draws at random, the times in milliseconds and the sizes of the database
files, every recorded file is the same as its fresh twin, and the design's and the survival
critic's files were the same in a second run as well. The numbers that came out differently are
timings. One count in the digest does not come out of the output: "ties in 16 of 64 cells" is 16
refusals of which 8 are at a tie (G1). Several sentences are broader than what their probe
printed, and the notes mark where. The claims are in the tables under each probe; those the papers
present as measured that no probe here backs are listed apart at the end.

## What a run needs

- Windows PowerShell 5.1 as `powershell.exe`, which runs every probe, as it ran them when they
  were measured.
- KeePass 2.62, which `. .\tools\KeePass.ps1; Install-KeePass` provides from the branch root, and
  KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0, which `tools\KeePassXC.ps1` provides when a probe asks
  for a release.
- Node.js with npm, for kdbxweb in `survival\`, and uv, for `compare.py`.

## Running them

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\single-entry\design\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\single-entry\survival\run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\single-entry\security-and-fit\run.ps1
```

Each `run.ps1` empties its directory's `out\` and `work\` and starts each of its probes once,
in a process of its own, with the arguments the probe's `param` line takes: the full path of
`KeePass.exe` from `Get-KeePassExe`, the work directory `work\` beside it, and the output file in
`out\` under the name its recorded twin has in `recorded\`. The survival run first installs
kdbxweb with `npm ci` into `kdbxweb\node_modules\` and passes `keepassxc-cli` 2.8.0 from
`Get-KeePassXCCli`, `scripts\kw.js` and the installed module; it starts `Survival.ps1` once for
each part, with `-Only`, in the order `w1`, `w2`, `c1`, `k1`, `d1`, since one recorded file holds
one part (`w1.txt` holds W1 and W1b). The design's two scripts ask `tools\KeePassXC.ps1` for their
releases themselves. Everything a run writes is in places the branch ignores: `out\`, `work\` and
`kdbxweb\node_modules\`, and through the tools `.keepass\` and `.keepassxc\` when a program is not
there yet. **Every database is made by the run, in `work\`, and none is kept here.** Nothing shows
a window: KeePassLib is loaded as an assembly, and `keepassxc-cli` and `node` run as console child
processes with no window.

To compare a fresh run with the record, from the branch root:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\single-entry\compare.py design
uv run --no-project python -I -X utf8 spikes\storage\probes\single-entry\compare.py survival
uv run --no-project python -I -X utf8 spikes\storage\probes\single-entry\compare.py security-and-fit
```

`compare.py` was written on this branch. Its first lines list exactly what it takes away: the order
of the labels inside a merged version's name, `M(A1+B1)` or `M(B1+A1)`, which follows the version
identifiers `Lib.ps1` draws at random; the acknowledgment's random identifier in `c1.txt`; the order
of the names after `version missing:`, which follows a hash table keyed by those identifiers; the
identifiers KeePassLib draws for the invented group and entry, renamed in the order they first
appear so that which node is which is still compared; times in milliseconds; and the sizes of the
database files, which, as `design.md` §14 states, mean nothing. It printed "the same after
normalizing" for all nine recorded files.

How long each ran here, on a machine running other measurements at the same time, each step
including the tools checking the programs' versions and signatures:

| Run | Here | Recorded |
| --- | --- | --- |
| `design`: `Cells.ps1`, `Rounds.ps1` | 17.2 s and 20.4 s; again 16.5 s and 18.3 s | not stated |
| `survival`: `npm ci`, then `w1`, `w2`, `c1`, `k1`, `d1` | 2.7 s, then 6.4 s, 4.3 s, 1.1 s, 1.1 s, 0.8 s; again 2.2 s, then 6.0 s, 4.1 s, 1.1 s, 1.3 s, 0.8 s | "about twenty seconds in all", `critique-survival.md` §9 |
| `security-and-fit`: `Probe.ps1`, `Plant.ps1` | 4.1 s, 1.3 s | not stated |

**How a result is judged.** "Yes" is that this run gives what the paper quotes. "Differs" is that
it gives something else, with the note stating whether that is noise, the environment or the claim.
"No" is that neither the record nor this run gives what the paper states. Where the record and this
run agree and the paper's conclusion holds, but its sentence is broader than the output or passes
over a cell, the result is "yes" and the note names what is not in the output.

The claims are numbered D for the design, V for the survival critique, A for the security critique
and G for the digest. Every path in the "Quoted in" columns is under `papers/options/single-entry/`.

## The design's probe, `design\`

Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
spikes\storage\probes\single-entry\design\run.ps1`, which writes `out\cells.txt` from
`scripts\Cells.ps1` and `out\rounds.txt` from `scripts\Rounds.ps1`.

`Cells.ps1` makes a base document `D0` (rules `X`, `Y`, `Z`, profile `dev`, client `c0`), and in
each scenario a copy A and a copy B written as the plugin writes: the string replaced, the time
moved, no history item. Each cell prints what the merge left (current, history, witnesses), then
the step without the plugin's memory and with it, and the KeePass cells also after a save and a
reopen. "data complete" is the step's document equal to the merge of both copies' decisions,
`client.c0=dev; client.ca=dev; client.cb=ops; profile.ops=on; rule.X=read; rule.Y=read update;
rule.Z=list`, or to A's where only A wrote. `Rounds.ps1` prints one summary line per chain and per
case.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | the store group's identifier `FE4040A9E0FAE884FA8BECAEEE9FBB46` and the state entry's `8AB03BFAEDE536C6078045089CEB0601`, the first 16 bytes of SHA-256 over `KPG/1/group/` and `KPG/1/state/` | `design.md` §2.1 | `cells.txt` line 2 | as quoted | the same | yes | |
| D2 | in 64 KeePassLib and 64 `keepassxc-cli` cells the entry was never a mixture: one copy's entry whole | `design.md` §0, §1; `critique-survival.md` §1, §6 row 1; `digest.md` §2 | `cells.txt` | every `current=` names one version, or `(unreadable)` where B's damaged document arrived whole (S6) | the same | yes | the probe prints the document, whose `sum` makes a mixture unreadable; the entry's other fields are not printed, so "the entry whole" is measured as "the document whole" |
| D3 | the table of §3.1: what each of S1 to S8 left under each method and direction | `design.md` §3.1 | `cells.txt`, the `current=` lines | as the table has it, cell by cell | the same | yes | the blank cells of the table (S7 and S8 under one method each) are in the output too, and not quoted |
| D4 | the four KeePassXC releases gave the same result in every cell | `design.md` §3.1 | `cells.txt` | the four lines of each scenario and direction agree, all 64 exit 0 | the same | yes | checked by a script over both files, not by eye |
| D5 | the stale copy wins by its time (S4); a tie keeps one version nowhere and KeePass and KeePassXC keep opposite sides (S3); with no history items of the plugin's own the loser is kept behind twelve later writes (S7), with one history item a write it is dropped (S8); Keep existing delivers the witness without the document | `design.md` §0, §1, §3.1 bullets, §12; `critique-survival.md` §3 | `cells.txt` S3, S4, S7, S8 | as quoted | the same | yes | S7's "in every cell" passes over Keep existing into B, where the later `A12` is nowhere, as the table itself shows |
| D6 | KeePass and KeePassXC keep the earlier version as one history item at different times and nothing at the same second; KeePassXC not when the winner holds ten later items | `design.md` §3.2 table, rows 1 and 2 | `cells.txt` S1, S2, S3, S8 | as quoted | the same | yes | the column "when history is cut" is the ground reports', not this probe's |
| D7 | over the 128 cells: KeePass without the plugin 47 complete, 17 refused, 0 stale; KeePass with the plugin's memory 54, 10, 0; KeePassXC at the next load 48, 16, 0 | `design.md` §3.3 table; `critique-survival.md` §6 row 2; `critique-security-and-fit.md` S4 | `cells.txt` | 47, 17, 0; 54, 10, 0; 48, 16, 0 | the same | yes | counted by a script; "stale" is any line with a document other than the complete document, of which there are none |
| D8 | the ten refusals with the plugin loaded are eight Keep existing imports and S8's two cells into A; the sixteen KeePassXC refusals are S3 and S8, all four releases, both directions | `design.md` §1, §3.3; `digest.md` §4 | `cells.txt` | as quoted | the same | yes | |
| D9 | wherever the step merged, the result was A's narrowing of `X`, B's widening of `Y`, the meet at `Z`, both clients, B's profile, A's removed profile gone | `design.md` §3.4 | `cells.txt` S1 to S3, S7, S8 | every `merged` line reads `[meet at rule.Z] data complete` | the same | yes | |
| D10 | each of the 149 output lines that carries a document carries the complete document | `design.md` §3.4 | `cells.txt` | 149 lines `data complete`, none `DATA:` | the same | yes | the probe's merge (`Lib.ps1` lines 128 to 146) has no row for a stamp older than `seen`, so the last row of §3.4's table never ran, as `critique-survival.md` §6 states |
| D11 | a lost version's witness still arrives, in all 128 cells | `design.md` §3.5 | `cells.txt` | every cell that lost a version lists its witness | the same | yes | |
| D12 | Create new IDs: two entries of kind `state`, two groups named `KeePassGate`, one copy of B's witness under another identifier; under the fixed identifiers A's version, A's witness, no refusal | `design.md` §2.1, §5.4; `critique-survival.md` §3 | `rounds.txt` "Create new IDs" | as quoted | the same | yes | that a copy does not count as a witness is the probe's own rule (`Witnesses`, `Lib.ps1` lines 160 to 169); what KeePass contributes is the new identifier |
| D13 | a damaged document recovered in all 16 cells of S6; a title edited by hand changes nothing (S4) | `design.md` §6.4 table rows 1 and 2 | `cells.txt` S4, S6 | 16 of 16 `put back A1` or `ok`, `data complete` | the same | yes | |
| D14 | the plugin's memory: twelve rounds writing in the same second and synchronizing in KeePass, 24 of 24 and no refusal; the same under a history limit of 0 when the arriving version was the later | `design.md` §3.3 | `rounds.txt` chains 3 and 4 | 24 of 24, refusals 0, both | the same | yes | |
| D15 | twelve rounds in the same second through KeePassXC or a KeePassLib merge with no plugin lose one side's twelve data, each loss refused at the next load; five seconds apart, 24 of 24 with no refusal through KeePassXC in either order and KeePassLib in one order | `design.md` §1, §3.5, §12 | `rounds.txt` chains 6 to 10 | same second: 12 of 24, refusals 23, both; apart: 24 of 24, refusals 0 | the same | yes | KeePassLib loses the open copy's `client.b*`, KeePassXC the source's `client.a*`, as in S3 |
| D16 | in ten chains of twelve rounds the state entry never held more than one history item | `design.md` §3.2 | `rounds.txt` | at most 1 in every chain | the same | yes | |
| D17 | a history limit of 0: the merge's own trim removes the loser before the plugin can look | `design.md` §3.2, §12; `digest.md` §4 | `rounds.txt` chain 5 | 12 of 24, refusals 12, `client.b1` to `client.b12` lost | the same | yes | |
| D18 | at 7,000,000 characters under the default 6 MiB the loser was gone after the save; 100,000 and 1,000,000 kept it, in KeePass and in KeePassXC 2.4.2 and 2.8.0, both directions | `design.md` §3.2, §8 row 3 | `rounds.txt` "Large documents" | 7,000,000: `history=[]`, `REFUSE version missing: A1`; 100,000 and 1,000,000: `history=[A1]`, merged, all ten cells | the same | yes | KeePass ran into A only, so "both directions" holds for KeePassXC; the 7,000,000 cell lost the open copy's earlier version and was read without the plugin's memory |
| D19 | a history limit of 0 or a document over the size limit: every merge in which the arriving version is the earlier ends in a refusal | `design.md` §12; `critique-survival.md` §6 last row; `critique-security-and-fit.md` M3 | `rounds.txt` chain 5, "Large documents" | refused in both | the same | yes | only the history limit was run with the arriving version the earlier; the size cell had it the later (D18), so for size the sentence rests on the same trim, not on a cell |
| D20 | after twelve rounds the file held one witness in every chain | `design.md` §7.2 | `rounds.txt` | `witnesses in the file: 1` in all ten | the same | yes | |
| D21 | 64 KeePass cells and five chains with the plugin; 64 KeePassXC cells and three chains | `design.md` §8 | `cells.txt`, `rounds.txt` | as quoted | the same | yes | |
| D22 | the state entry deleted for good in A: Synchronize gone from both when B is unchanged or wrote before; Overwrite if newer keeps it in B; KeePassXC 2.8.0 brings it back into A; when B wrote after, back everywhere with B's version; moved to the bin with B writing later, in the bin in both under Synchronize and in A under KeePassXC | `design.md` §6.4 second table and way 4; `critique-survival.md` §4; `digest.md` §2, §4 | `rounds.txt` "The state entry deleted" | as quoted | the same | yes | that KeePassXC merging into B left the entry in a group named "Recycle Bin" that is not B's bin is half in the output: the probe prints the group's name, not whether it is the database's bin, which `design.md` takes from the KeePassXC ground report |
| D23 | a plain synchronize with a backup does not bring a deleted entry back; an entry put back under the fixed identifier with a later time survives a synchronize with the backup in both directions, and the deletion record is dropped | `design.md` §6.4 way 3; `critique-survival.md` §6; `critique-security-and-fit.md` S3; `digest.md` §4 | `rounds.txt` "Recovery" | `state entry absent; deletion records 1`, then `current=D0`, `deletion records 0`, both ways | the same | yes | the probe puts back the document the plugin remembered rather than a document read out of a file the operator picks; the entry written is the same either way |
| D24 | a look-up by identifier 0.03 ms with the store group first among 10,002 entries and 0.12 ms for the last; SHA-256 over 50,000 bytes 0.03 ms; 50,000 characters out of the string 0.01 ms | `design.md` §6.1; `digest.md` §5 | `rounds.txt` "What a read costs" | 0.025, 0.120, 0.030, 0.007 ms | 0.009, 0.106, 0.023, 0.007 ms; again 0.007, 0.097, 0.023, 0.007 ms | differs | timing, noise: lower here in both runs; the paper calls them upper bounds, and they are |
| D25 | KeePass hands back the same string object for an unchanged entry | `design.md` §3.3, §6.1; `critique-security-and-fit.md` M2 | `rounds.txt` "What a read costs" | `True` | the same | yes | measured as two `Strings.Get` calls in a row on one open database, not across a merge, a save or a reload |
| D26 | the KeePassXC row of §6.3: every string, flag and history item kept through a KeePassXC save, "every cell is a KeePassXC save" | `design.md` §6.3, §8 | `cells.txt` | the document and its history read back after every KeePassXC merge | the same | yes | the document is unprotected in the probe and no flag is printed, so the flag is the KeePassXC ground report's; the KeePass row's "KDBX 4.0 is enough" is not printed either |
| D27 | the probe ran on a copy of `KeePass.exe` with SHA-256 `AFB89B74...C89C3F3`, equal to the original | `design.md` §14 | none recorded | | the three copies in the agents' directories and `.keepass\2.62\KeePass.exe` all hash `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3` | yes | checked here; `tools\KeePass.ps1` checks the pin and the signature on every run |

## The survival critic's probe, `survival\`

Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
spikes\storage\probes\single-entry\survival\run.ps1`, which writes `out\w1.txt`, `out\w2.txt`,
`out\c1.txt`, `out\k1.txt` and `out\d1.txt` from `scripts\Survival.ps1`.

`Survival.ps1` dot-sources the design's `Lib.ps1`, sets the window to the design's 256, and adds a
step of its own (`Step`) with a choice of merge (`probe`, the design's probe's `MergeTwo`, or
`narrow`, the last row of `design.md` §3.4 read the most favorable way) and of how heads are judged
(`anc`, as written, or `gen`, a candidate below another's window taken as its ancestor), and
Continue as designed or as an acknowledgment.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V1 | the probe runs on the design's `Lib.ps1`, copied unchanged | `critique-survival.md` §0, §9 | none | | the three `Lib.ps1` here are byte for byte the same | yes | |
| V2 | the design's probe used a window of 64 and never passed 25 generations | `critique-survival.md` §2.2, §6 row 2; `digest.md` §3 item 7 | `design\recorded\rounds.txt` | `generation 25` in every chain | the same | yes | the window is `Lib.ps1` line 9, as cited; the line numbers `critique-survival.md` cites in `Lib.ps1` (41 to 45, 87 to 88, 128 to 146) hold in the carried copy |
| V3 | W1 at 300 versions, three readings: the probe's merge brings `P` back, `N1` `list`, `N3`'s rule back, `c9` refused; the favorable last row refuses `N1` and `N3` and keeps `c9` refused; heads by generation serve `v300` | `critique-survival.md` §1 item 2, §2.2 W1 table, §5 last row; `digest.md` §3 item 7 | `w1.txt` | as quoted | the same | yes | |
| V4 | at 20 versions all three readings serve the newest version correctly, the control | `critique-survival.md` §2.2 | `w1.txt`, N = 20 | `ok, serves v20` in all three | the same | yes | the sentence gives `v300`; at N = 20 the newest is `v20`, which is what all three serve |
| V5 | W1b at 300 versions: the probe's merge serves `N2` `list` and `cB`; the favorable row drops `cB`; heads by generation serve `N2` `read list`, no `cB`, no refusal | `critique-survival.md` §2.2 W1b table; `digest.md` §3 item 7 | `w1.txt` | as quoted | the same | yes | |
| V6 | W2: 12 `keepassxc-cli` 2.8.0 merges, all exit 0; the desktop's file 0 history items, 1 witness, 300 deletion records, serves `v300`; the laptop's 10, 13, 0, `503 undecided` for `v25` and `D0`; Continue cleared it | `critique-survival.md` §1 item 3, §2.3, §5; `digest.md` §2, §4 | `w2.txt` | as quoted | the same | yes | the two missing names come in either order, as a hash table lists them (`v25,D0` recorded, `D0,v25` here) |
| V7 | 300 deletion records after 300 versions | `critique-survival.md` §1, §2.10; `digest.md` §5 | `w2.txt`, the desktop's line | `deletion records 300` | the same | yes | |
| V8 | C1: as designed, after Continue and the later synchronize, `N` `read list` and no `cA`; with Continue as an acknowledgment, merged: `N` `list`, `cA`, `cB` | `critique-survival.md` §1 item 4, §2.4, §5; `digest.md` §3 item 6 | `c1.txt` | as quoted | the same | yes | the acknowledgment's identifier is random (`ack.<16 hex digits>`) |
| V9 | D1: after Create new IDs two state entries and two witnesses, the step serves `v1`; Delete Duplicate Entries removes three entries, the state entry under the fixed identifier among them, leaves a deletion record naming it; no readable version | `critique-survival.md` §1, §2.6, §3, §4; `digest.md` §4 | `d1.txt` | as quoted | the same | yes | that the removed witness was the state entry's own, not its copy, is not printed: the probe lists the kinds left, `[state,witness]` |
| V10 | K1: kdbxweb 2.1.1 either direction leaves the state entry absent, nothing served; KeePass Synchronize either direction restores and serves it | `critique-survival.md` §1, §2.7, §3 | `k1.txt` | as quoted | the same | yes | the installed kdbxweb is 2.1.1, from the carried lock file |
| V11 | all of it ran in about twenty seconds | `critique-survival.md` §9 | none | | 13.7 s for the five parts, 2.7 s more for `npm ci`; again 13.3 s and 2.2 s | differs | timing; the record is the paper's own estimate |

## The security critic's probes, `security-and-fit\`

Command: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File
spikes\storage\probes\single-entry\security-and-fit\run.ps1`, which writes `out\probe.txt` from
`scripts\Probe.ps1` and `out\plant.txt` from `scripts\Plant.ps1`. Both dot-source the design's
`Lib.ps1` and use its document, merge and step unchanged, at its window of 64.

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A1 | the probes reuse the design's `Lib.ps1`, a copy of `KeePass.exe` whose SHA-256 matched (`AFB89B74...C89C3F3`) | `critique-security-and-fit.md` "What I ran" | none | | the `Lib.ps1` copies are the same and the hashes match (D27) | yes | |
| A2 | a foreign file with no authority, only a document adding `rule.C = read protected list`: Synchronize either order, Overwrite if newer either order and Keep existing with the foreign copy earlier all end with the step serving `PLANTED rule.C = read protected list` beside `rule.G=DENY` | `critique-security-and-fit.md` S1, lens (a); `digest.md` §3 item 1 | `plant.txt` | 5 of 6 cells as quoted | the same | yes | |
| A3 | the one cell that did not plant, Keep existing with the foreign copy later, refused: `REFUSE version missing: F0`, the arriving witness alone | `critique-security-and-fit.md` S1, S4, lens (a); `digest.md` §4 | `plant.txt` | as quoted | the same | yes | the foreign file carried a state entry as well; a file holding one crafted witness and nothing else, as S1's second half describes, was not run |
| A4 | an import with Create new IDs cannot change the state: two state entries, the state entry under the fixed identifier unchanged, the step does nothing | `critique-security-and-fit.md` "What was checked and holds" item 5 | `probe.txt` part D | `state entries 2, one under the fixed identifier: True`, `step: ok` | the same | yes | |
| A5 | after that import Delete Duplicate Entries deleted the state entry under the fixed identifier and kept the import's copy: no readable version | `critique-security-and-fit.md` S3, lens (c), lens (d); `digest.md` §4 | `probe.txt` part D | `3 deleted`, `state entries 1, one under the fixed identifier: False`, `REFUSE no readable version`, the same after a save and reopen | the same | yes | |
| A6 | part W, described under "What I ran", with no result quoted | `critique-security-and-fit.md` "What I ran" | `probe.txt` part W | at 10 versions every cell serves A's data; at 70, `D0` is out of A's `seen` and every cell merges it back as a second head, `profile.old` returning after A took it off | the same | yes | the outcome is the survival critic's W1 (V3) at the probe's window of 64; this paper quotes none of it |

## The digest

| # | Number or outcome | Quoted in | Output | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| G1 | KeePassXC: "ties in 16 of 64 cells" | `digest.md` §2, KeePassXC row | `cells.txt` | 16 of 64 refused: 8 in S3, the tie, and 8 in S8, where the loser was kept nowhere behind ten history items | the same | no | the count is the refusals', not the ties'; `design.md` §3.3 gets it right: "The sixteen KeePassXC refusals are S3 and S8" |

Every other number the digest quotes from these probes is a row above: D2, D7, D17, D22 to D24,
V2, V3, V5 to V9, A2, A3 and A5.

## Claims no probe here backs

**Presented as measured, with no output behind them.** These are what the branch cannot yet vouch
for from this family's probes:

| Claim | Quoted in | What was found |
| --- | --- | --- |
| KeePassXC merging into B "left the entry in an ordinary group named 'Recycle Bin' that is not B's bin" | `design.md` §6.4 | the output names the group only (D22); whether it is B's bin is not printed |
| 100,000 and 1,000,000 characters kept the loser "in KeePass ... both directions" | `design.md` §3.2 | KeePass ran into A only (D18) |
| a foreign file "holding one crafted witness entry and nothing else" makes the gate refuse | `critique-security-and-fit.md` S1, S4 | the refusal measured came from a file that also carried a state entry (A3) |
| the same string object for an unchanged entry, as the basis for checking it on every request | `design.md` §3.3 | measured within one open database only (D25), not after a merge or a reload |
| KDBX 4.0 is enough for the entries and strings, and every flag is kept | `design.md` §6.3 | no format version or flag is printed (D26); both are cited from the ground reports as well |

**Read, and checked the same way.** `critique-security-and-fit.md` "What was checked and holds"
item 1 counts "the 44 operations" of `Spec/openapi.yaml`, marked as read: `git cat-file -p
a42e38aa019d572c5a10b489e6aadeea91ef91c1:src/KeePassGate/Spec/openapi.yaml`, counting the lines
that declare an `operationId`, gives 44.

**Measured by another family, and not run here.** The papers also quote measurements of the
ground reports and of other options, which their own families carry, all under
`spikes\storage\probes\`: `[M keepass §n]` in `ground-keepass\`, `[M keepassxc §n]` in
`ground-keepassxc\`, `[M others §n]` in `ground-others\`, `[M spike §n]` in `ground-measured\`,
and `[M inventory §n]` in `ground-inventory\`, among them the 2903 characters of one authority
that `critique-security-and-fit.md` S2 rounds to "about 2,903" and the 14 KB that `design.md`
§2.2 starts from. `[M store-entries]`, `[M rules-central]`, `[M rules-linked]`, `[M wildcard]`
and `[M wildcard-critique]` are those options' families, and the Android measurements of
`digest.md` §2 (`papers/ground/measured-android.md`) are in `spikes\storage\android\`.
`FINDINGS.md:417` (`critique-survival.md` §2.5) is the product's own, at `a42e38aa`. Claims marked
`[R ...]` or `[A]` are reading and assumption, and no probe is owed for them; the sizes of
`design.md` §2.2 and the 520 versions a year of `critique-survival.md` §2.2 are marked by the
papers as arithmetic.

## Changes made to run them here

- **Two tables in the design's probes, and nothing else in any probe.** In
  `design\scripts\Cells.ps1` the four paths of `keepassxc-cli.exe` 2.4.2, 2.6.6, 2.7.12 and 2.8.0
  inside the KeePassXC ground report's downloads, built from the parameter `$XcRoot`, became
  `Get-KeePassXCCli -Version <release>` from `tools\KeePassXC.ps1` for the same four releases; in
  `design\scripts\Rounds.ps1` the same for its two, 2.4.2 and 2.8.0. The tool is dot-sourced in a
  child scope, `& { . <tool>; ... }`, because it sets `Set-StrictMode -Version Latest`, which would
  otherwise apply to the probe, which was measured under `Set-StrictMode -Version 2` from
  `Lib.ps1`. Each change carries a comment recording it. `$XcRoot` stays in both `param` lines and
  is no longer read.
- **`KeePass.exe` needed no change in any probe**: every script takes it as the parameter
  `$KeePassExe`, and the agents passed a copy in a `bin\` of their own. Each `run.ps1` passes
  `Get-KeePassExe` instead, `.keepass\2.62\KeePass.exe`, the same file by hash (D27).
- **`survival\scripts\Survival.ps1`, both `security-and-fit\` scripts and the three `Lib.ps1`** are
  byte for byte as the agents left them: they take every program and module as a parameter.
- **`survival\scripts\kw.js`** was copied unchanged from the wildcard survival critic's scripts,
  since `critique-survival.md` §9 names it as the driver it ran and `Survival.ps1` takes it as
  `-KwJs`. It merges two files and saves the result, with the password `Lib.ps1` uses.
- **`survival\kdbxweb\package.json` and `package-lock.json`** were copied unchanged from the npm
  install the critic's `-KwMod` pointed into, in the others ground report's working directory, so
  that `npm ci` installs the same kdbxweb 2.1.1, `@xmldom/xmldom` 0.7.13 and `fflate` 0.7.5. It is
  the same lock file `store-entries\survival\kdbxweb\` carries.
- **A `run.ps1` for each directory**, added on the branch, since the agents started the scripts by
  hand and kept no command. Each passes what the probe's `param` line takes, as described under
  "Running them".
- **`compare.py`**, added on the branch, as described under "Running them".

**Recorded output.** The nine files are copied as the runs left them, ASCII with CRLF line endings,
byte for byte: a search found no machine path, user name or session identifier in any of them, so
nothing was replaced. The probes print the leaf names of their files and never a directory. The
sources held none either, since every script takes its paths as parameters, and a search of this
directory for drive paths, `Users\`, `AppData`, `scratchpad`, `Temp\`, the session identifiers and
the host name finds nothing.

**Environment.** KeePass 2.62 is loaded from `.keepass\2.62\`, where KeePass's own
`KeePassLibN.x64.dll` and `KeePass.XmlSerializers.dll` sit beside it, rather than from a bare copy;
`node` is 24.19.0 here, and the recorded run's version is not recorded. No result moved.

## Not carried

- **Every database**: 389 `.kdbx` files of the design's runs, 36 of the survival run and 40 of the
  security runs, all made by the probes, which make them again in `work\`. The `work\` of this
  run was removed after the comparison.
- **The design's 64 `merge.txt` files** under `work\cells\`: what `keepassxc-cli merge` printed in
  each KeePassXC cell, localized and quoted by no paper. A run writes them again into `work\`.
- **`out\cells-console.txt` and `out\rounds-console.txt`** in the design's directory: byte for byte
  the same as `cells.txt` and `rounds.txt`.
- **The three `bin\KeePass.exe`**: copies of somebody else's program, the same by hash as the file
  `tools\KeePass.ps1` provides.
- **The installed kdbxweb modules** in the others ground report's working directory: the lock file
  is carried, and `npm ci` installs them into `kdbxweb\node_modules\`, which this run removed again.

## Caveats and defects

- **No defect was found that changes a quoted result.** None of the probes is the plugin, which
  `design.md` §14 states: the document is a line format of the probe's own, unprotected, in a string
  named `KPG.State`, and the step is PowerShell.
- **Where the design's probe and the design's text part**, none of which moves a result here: the
  window of `seen` is 64 in the probe (`Lib.ps1` line 9) and 256 in `design.md` §2.2, which the
  survival critic ran (V3 to V6); a version identifier is 8 random bytes in the probe and 16 in
  §2.2; a witness's identifier derives from `KPG/1/witness/<id>` in the probe and from
  `KPG/1/version/<id>` in §2.1; the probe's merge has no row for a stamp older than `seen` (D10);
  and a merged version takes a random identifier in the probe where §3.3 makes it a function of
  its parents, which is why `compare.py` sorts the labels inside `M(...)`.
- **Timings** (D24, V11) are single runs on a machine doing other work. They are not numbers to
  compare designs by.
- **Culture.** The cost lines print in the current culture's number format, `0,025 ms`, German in
  the recorded run and in this run.
