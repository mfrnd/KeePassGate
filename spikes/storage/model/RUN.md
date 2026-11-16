# The executable models of `crdt`

Option `crdt` keeps every datum the plugin stores as ordinary entries written once and reads them
as conflict-free replicated data types. Its design was argued with an executable model, and its
formal critic wrote a second model from the design's text alone. Both live here:

- `crdt_model.py`, the design's model, cited as `[model]` throughout
  `papers\options\crdt\design.md` (written as `option-crdt.md`), which names it
  `crdt-model\crdt_model.py`, and by the digest (`papers\options\crdt\digest.md`) as
  `[model design]`;
- `critic\crdt_formal.py` with `critic\probe_liveness.py`, `critic\probe_restore.py` and
  `critic\probe_fixed.py`, the formal critic's model and probes, cited as `[model here]` in
  `papers\options\crdt\critique-formal.md`, which names them `crdt-model\critic\...`, and by the
  digest as `[model formal]`.

The survival critic ran the design's model too, copied unchanged, beside an extension of it and
real files. That copy and everything it ran are under `spikes\storage\probes\crdt\survival\`, in
the family `crdt`, whose `RUN.md` covers them and the security critic's counts. In the tables
below "design 12.3" means section 12.3 of `design.md`, "formal P1" problem P1 of the formal
critique, "digest 3" section 3 of the digest.

**Both models ran again from the branch on 2026-11-15 and 16, and every line of every recorded
output came out the same but for one line: the running time on the last line of
`results-full.txt`.**
Every number and every outcome the design and the formal critique quote from them holds. Two
descriptions of the programs differ, rather than anything they print: what `quick` runs (design
12) and how long `crdt_formal.py` is (formal 4.1). What cannot be run is how the design's model
found the six counterexamples of design 12.4, because the versions of the model that the search
found them against were not kept; section 3 runs the six histories against the model as it stands.
Section 7 lists the claims no output here shows.

| Run | Command, from the branch root | Writes | Recorded copy | Here | Recorded |
| --- | --- | --- | --- | --- | --- |
| the design's model, `full` | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\model\run.ps1 full` | `out\results-full.txt` | `recorded\results-full.txt` | 639 s | 596 s |
| the same, `quick` | `... run.ps1 quick` | `out\results-quick.txt` | none | 111 s | none |
| the same, `scenarios` | `... run.ps1 scenarios` | `out\results-scenarios.txt` | the first 63 lines of `recorded\results-full.txt` | 2 s | none |
| the formal critic's model and probes | `... run.ps1 critic` | `critic\out\results-critic.txt`, `results-liveness.txt`, `results-restore.txt`, `results-fixed.txt` | the same names in `critic\recorded\` | 265 s, 17 s, 7 s, under 1 s | none |
| histories the design quotes with no output (section 3) | `uv run --no-project python -I -X utf8 spikes\storage\model\checks\design_histories.py` | `out\design-histories.txt` | none | under 1 s | none |

`run.ps1` runs exactly the line each paper gives (design 12:
`uv run --no-project python -I -X utf8 spikes\storage\model\crdt_model.py full`; formal 4.1:
`... spikes\storage\model\critic\crdt_formal.py all 1500`) and writes what the program prints into
`out\`, byte for byte, as the agents redirected it into the files now under `recorded\`. The
critic's two probes take what the record shows they were given: `probe_liveness.py 1500`, and
`probe_restore.py` and `probe_fixed.py` with no argument. Then one more command compares every
fresh output with its record:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\model\compare.py
```

It masks the running time on the last line of `results-full.txt`, compares `results-scenarios.txt`
with the scenario part of `results-full.txt`, holds `design-histories.txt` to what the design states
of each history, and exits 0 when nothing differs. What a run needs: Windows PowerShell 5.1 for
`run.ps1`, and `uv`, which provides a Python. Neither model has a dependency, starts a program or
writes a file of its own; nothing shows a window, and no database is made.

The papers name these files by the directory they were written in, `crdt-model\`, which is this
directory, and name the outputs at its root and at `critic\`'s, where the agents wrote them. Here
the recorded copies are under `recorded\` and `critic\recorded\`, as `probes\README.md` lays out.

## What the agents ran, and that the branch runs the same programs

- **The sources are the agents' own, byte for byte**, compared by SHA-256 with the files in the
  report directory and in the formal critic's working directory, which hold the same bytes. The
  design's model has the SHA-256 the survival critique quotes, `ED5F93CD…4707F`.
- **Python.** The formal critic's directory kept a bytecode cache of `crdt_formal.py` made by
  CPython 3.12; the design's agent ran its model as a script, which leaves no cache, so its version
  is not known. The branch ran CPython 3.14.8, the version `uv` provides here, and ran the critic's
  three probes and the design's `scenarios` again under 3.12.15: the same output. `-I` makes Python
  ignore `PYTHONHASHSEED`, so every run had a string hash seed of its own; the outputs being equal
  shows that nothing they print depends on it. Every random choice is drawn from a
  `random.Random` with a seed written in the source.
- **Which source wrote which record.** Every record was written after its source was last changed,
  with one exception: `critic\recorded\results-liveness.txt` was written at 20:40:08 and
  `critic\probe_liveness.py` last changed at 20:40:50, together with `probe_restore.py`. The source
  as it stood for that record was not kept. The final source, given 1500, prints the record line
  for line, and under Python 3.12 as well, so whatever changed did not touch what it prints.

## 1. `crdt_model.py`: the design's model

**What it measures.** A file as a set of store entries with times and a set of deletion records,
over a small tree; the design's read rule of 5.2 and node rule of 8 as one function, with a switch
for each part; the gate's acts through the design's write path; and the other programs' merges and
people's faults as the rows of design 2 and 12.1 state them. It checks safety, rollback by
overwrite, one-loss, copy-cap and convergence against an ideal after every step. Cited by design 0,
1, 4.2, 5.4, 6.1, 6.2, 6.4, 7, 8, 11 and 12, and by the digest (sections 3, 4 and 7).

**Command and output.** `run.ps1 full`, `quick` or `scenarios`, into `out\`. The model draws 20,000
random histories per configuration under `full` and 4,000 under `quick`, and searches the
exhaustive part one step less deep under `quick`.

**How long.** `full` 639 s here, with a second probe running beside it for most of that time; the
record's last line reads 596 s. `quick` 111 s, `scenarios` 2 s.

**Compared.** `full` once, `scenarios` twice (under 3.14.8 and 3.12.15), `quick` once: every line of
`results-full.txt` the same as the record but the time, and `results-scenarios.txt` the same as the
record's scenario part. `quick` has no record; section 6 describes what it finds.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| 480,000 random histories over two and three copies | design 1, 12.3 | 12 configurations, 2 and 3 files, 20,000 each | the same | yes | |
| 127,522 exhaustive histories | design 1, 12.3 | 11,110, 47,988, 40,494 and 27,930 | the same | yes | |
| The exhaustive table: widths 10, 36, 34 and 30, depths 4, 3, 3 and 3, no failure in any | design 12.3 | as claimed | the same | yes | |
| Merges alone, by every program, in any order: no copy wider than the ideal, and all copies agreeing with it | design 1, 12.3 row A | A: no failure in 2 or 3 files; the exhaustive merges: none | the same | yes | |
| Synchronize and Keepass2Android only, deletions of any kind, hand edits, imports, planted entries: no failure | design 1, 12.3 rows J, J2, K; digest 2 | no failure in all six cells | the same | yes | |
| The random table: C safety 2 and 1; D safety 2 and none; E safety 1 and none; G copy-cap 2 and none; H rollback by overwrite 251 and safety 1, rollback 404 and safety 2; I rollback 41 and safety 1, rollback 105; A, B, F, J, J2, K no failure | design 12.3 | as claimed | the same | yes | |
| Merges that carry no deletion record, and deletions: failures of two shapes only | design 1, 12.3 | the shortest of C, D, E and H are both carriers deleted where made, then such a merge; I's is design 6.4 item 3 | the same | yes | only the shortest of each configuration is printed, as design 12.3 states |
| The shortest histories quoted: `prem 0 pf; del 0 (head) 1; del 0 (the removal) 1; merge kp_other 1 0`; `rule 0 root none; del 0 (the version) 1; renew 1 c0 forever; del 0 (head) 1; merge dx 1 0`; `renew 0 c0 forever; del 0 (the answer) 1; del 0 (head) 1; merge kp_other 2 0`; I's `rule 0 root inherit; padd 1 pg; del 0 (head) 2; merge dx 1 0; del 1 (the version) 0`; `rule 1 N none; copy 1; strip 1`; `revoke 2 c0; over 2 1` | design 6.4, 12.3; digest 3 items 3, 5 | as claimed: C 2 files, E 2 files, C 3 files, I 2 files, G 2 files, H 3 files | the same | yes | the first takes the older head, `('head', 'old')` |
| Design 6.4 item 2's `prem 0 pf; del 0 (the removal); del 0 (head); merge xc 1 0` | design 6.4 | H 2 files, shortest safety, with records | the same | yes | |
| No history failed `one-loss`, `convergence` or `sealed after a loss` in this run | design 6.1, 12.3 | none in the 24 cells of the design as written | the same | yes | one ablation does fail `one-loss`; the sentence is about the design as written |
| No single deletion or edit of a store entry made a copy wider; a revocation undone only by an overwrite or where never given | design 1 | true of every history this run drew | the same | yes | of the search; survival P2 gives a four-step history the search did not draw where the unchanged model fails both, run in `probes\crdt\survival\scripts\p2_history.py` (`probes\crdt\RUN.md`, section 1) |
| The laws of the join: 10,000 random pairs, no violation | design 12.3 | 0 violations in 10,000 | the same | yes | |
| The 20 replayed sequences, as written: holds in 1, 4 to 6, 10 to 13, 15, 18, 20; holds and incomplete in 2, 7, 8, 17, 19; holds with nothing served in 14; fails in 3, 9, 16 | design 1, 4.2, 5.4, 6.1, 6.2, 6.4, 7, 8, 11, 12.3; digest 3 | as claimed | the same | yes | every scenario the design cites by number is a row of this table |
| Fails with this part off: 1 without the head's carrying; 2 under no switch alone; 4 with generations; 8 without the record rule; 18 without the head's carrying or without the witness | design 11 rows P1, P2; 12.3 | as claimed | the same | yes | |
| Ablations, 20,000 histories each: no head snapshot safety 20, shortest `revoke 0 c0; del 0 (the revocation) 1; merge xc 1 0`, as written safety 2; no witness, no record rule, a decision without its head: no failure; a deleted head not recognized: safety 2 beside 2; a decision's record telling nothing: one-loss 1 and safety 3, shortest `rule 0 root inherit; del 0 (head) 1; merge kp_other 1 0; del 1 (the version) 1`, beside safety 2; generations: convergence to the ideal 2, shortest `prem 0 pf; prem 1 pf; padd 0 pf`, beside no failure | design 12.3 | as claimed | the same | yes | |
| "One run of `full`, 596 seconds" | design 12.3 | `== 596 s` | `== 639 s` | differs | a running time, with another probe beside it here; noise |
| "About 1,000 lines of Python with no dependency" | design 12 | | 1,008 lines, the standard library only | yes | |
| "`quick` in place of `full` for a tenth of the histories" | design 12 | | `quick` draws 4,000 histories where `full` draws 20,000, a fifth, and makes each exhaustive search one step shallower: 4,562 histories in place of 127,522 | differs | a description of the program, which prints its own counts; section 6 |

## 2. `critic\`: the formal critic's model and probes

**What it measures.** `crdt_formal.py` models a file as entries with a modification time, a
location (the store group, the recycle bin or another group) and a location time, deletion records,
marked nodes, and every element ever in its lineage; the design's read as the critic read the text,
with switches for the 64-height window, counting elements anywhere, binned heads in force, another
reading of design 5.2 point 4 and a new key at Recreate; the gate's acts; five merges and the faults
of the critique's section 4.1. It judges every file after every step against two oracles, `IK` and
`IE`. `ce` replays the critique's counterexamples with every file's value after every step, `laws`
checks the document lattice and the set types, and `search N` draws random histories.
`probe_liveness.py` asks whether merges alone ever leave a file incomplete and whether two files
holding the same elements answer differently; `probe_restore.py` restores a backup and writes on
it; `probe_fixed.py` replays every counterexample under P1's change and under P1's and P2's. Cited
by formal 1 to 4 and by the digest (sections 2, 3 and 7).

**Command and output.** `run.ps1 critic`, into `critic\out\`.

**How long.** `crdt_formal.py all 1500` 265 s, `probe_liveness.py 1500` 17 s, `probe_restore.py`
7 s, `probe_fixed.py` under a second. The record holds no durations.

**Compared.** Once under 3.14.8, and the three probes again under 3.12.15: every line of the four
outputs the same as the record.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| The meet table: 200,000 random triples of the 512 per-profile functions, with random inputs: closed, commutative, associative, idempotent, below each argument, monotone, a narrower parent never widens the child: 0 violations | formal 1, 2; digest 7 | `closed 0, comm 0, assoc 0, idem 0, below 0, mono 0, compose 0` | the same | yes | |
| The meet is exactly what both give | formal 2 | counted in `below`, 0 | the same | yes | |
| The remove-wins set and the registers are functions of the union: 20,000 random triples, 0 violations | formal 2 | 0 | the same | yes | |
| A restored backup written to with two of six acts, merged back by each of five merges both ways: 3,600 histories, no widening, no revocation undone | formal 1, 2; digest 7 | `3600 histories, 0 with a failure` | the same | yes | |
| Merges alone never stop the gate: 1,500 histories, three and four files, for each of three reads, no file incomplete | formal 2 | 0 for each read | the same | yes | |
| A removal beats a re-addition that did not see it; no failure that did not go through P1 | formal 2 | every shortest history the search as written prints, with the window unbounded, has a Delete, a Move or KeePassDX's Delete in it | the same | yes | a reading of those histories; the others were counted, not printed |
| The design's model holds no `bin`, `recycle`, `location` or `64` | formal 0 | | none of the four in `crdt_model.py`, ignoring case | yes | |
| CE1, 5 steps: both files whole and `N` giving `p` read, update and list after Synchronize | formal 1, P1 | `N/p=lru` in both files, `detected-loss-healed-wider` | the same | yes | |
| CE1 under the other reading turns into a refusal; CE1-move fails under both readings | formal P1, 4.3 | `N/p=-`, no failure; CE1-move fails | the same | yes | |
| CE3, 4 steps: file 1 admits `c0`; CE3-bin the same; CE4 the retired authority served with its clients; CE13, 6 steps, a revocation undone with one Move in each of two copies | formal 1, P1; digest 3 item 4 | as claimed | the same | yes | |
| CE10, 4 steps: one Delete of the head makes file 0 serve the older `rw` | formal 1, P1; digest 3 item 4 | `N/p=lru` in file 0, `one-loss` | the same | yes | |
| The security critic's S3 change closes CE1 (`CE1-binheads`) and not CE3 (`CE3-binheads`) | formal P1 | as claimed | the same | yes | |
| With P1's change every history of P1 holds (CE1, CE1-move, CE2, CE3, CE3-bin, CE4, CE7, CE10, CE13) | formal P1; digest 2, 3 | no failure for each | the same | yes | `results-fixed.txt` |
| With P1's and P2's changes, only CE6, CE11 and CE12 still fail | formal P1 | as claimed | the same | yes | |
| CE5, 70 steps, 65 of them gate writes: both files whole, `N` giving read, update and list; `CE5-unbounded` incomplete | formal 1, P2; digest 3 item 10 | as claimed | the same | yes | |
| CE11, 4 steps: the new client gets read, update and list at `N`; CE12, 5 steps: the name back on the list; both hold with the key carried forward | formal 1, P4; digest 3 item 13 | as claimed | the same | yes | |
| CE6, 6 steps: whole, `rw`; `CE6-no-touch` incomplete | formal 1, P5; digest 3 item 11 | as claimed | the same | yes | |
| CE8, 3 steps: both files hold the record, file 1 admits the client, file 0 does not | formal 1, P6 | as claimed | the same | yes | |
| 1,500 histories with every fault but an overwrite: 101 pairs of files with the same elements answering differently, 34 both serving; with P1's change 3, the pair printed from a record and refusing | formal P6 | 101 and 34; anywhere 3 and 1, the pair printed refusing `c1` | the same | yes | |
| 56 of 128 meets of two documents are limits no `AclDocument` states | formal 1, P7 | `56 of 128` | the same | yes | |
| The search table of formal 4.4: as written, B, C and D failing in 4 of 6 runs, E none, F one through a Move, G one through a Delete; with `K=3` 5 of 6, E none, F with a Seal again and G none; P1's change failing only in F, `del0` twice with KeePassDX merges; the S3 change failing through Moves | formal 4.4; digest 2 | as claimed, cell for cell | the same | yes | |
| In C a revocation undone in 6 steps and a narrowing healed wider through Synchronize in 5, `gate 2 rule N deny; bin 2 head(old); move 2 rule(new); gate 1 prem q; sync 2 1` | formal 4.4 | as claimed | the same | yes | |
| Each cell is 1,500 histories for three files and 1,500 for four | formal 4.4 | | | yes | |
| "About 1,100 lines of Python with no dependency" | formal 4.1 | | 1,195 lines, the standard library only | differs | nearer 1,200 |

## 3. `checks\design_histories.py`: written on the branch

The design quotes histories from its model that no recorded output holds. This replays them in the
model as it stands, as written and with each part of the read rule switched off in turn, through the
model's own checks (`crdt_model.run`). The design's words are mapped to the model's selectors, and
where the design leaves a selector open the mapping is stated in the output: "the head" is the
newest, "the older head" the lowest.

| Claim | Quoted in | Recorded | This run | Reproduced | Note |
| --- | --- | --- | --- | --- | --- |
| The six counterexamples of design 12.4 were found by the search against earlier versions of the model | design 5.2, 12.4 | none | | not run | the versions were not kept, section 7 |
| Each of the six "is closed" in the design as it stands | design 12.4 | none | no failure for any of the six as written | yes | |
| Item 6's change, a decision's kind and subject in its identifier, and item 5's, any damage in the group making the store incomplete, are what close those two | design 12.4 items 5, 6 | none | item 6 fails again with the decision tag off, item 5 with the cover off, under which the model checks damage | yes | the other four are closed by more than the change the design names for each: item 1 fails only with the decision tag off, not with the cover off; item 4 only with the head snapshot off; items 2 and 3 under no single switch. The design claims no switch per item, and later changes closing earlier items is what "each is closed and stays in the search" allows |
| Seal again after both carriers of a standing answer went without a trace: the renewal is asked again; with deletion records the same history holds | design 6.4 item 5; digest 3 item 7 | none | `sealed-after-a-loss`, renewal of `c0` asked; with records no failure | yes | with records it fails only when the decision tag is off |

## 4. What was replaced in what was copied

Nothing. The sources and the recorded outputs were searched for a user's profile directory and the
application data under it, the scratch directory, the session identifiers, the machine's name and
the product's worktree, and none held any. The scripts the formal critic left beside them that did
hold the scratch directory's full path, `debug_ce5.py` and `probe_merges.py`, were not carried
(section 8).

## 5. Every change made to run the models here

- `crdt_model.py`, `critic\crdt_formal.py`, `critic\probe_liveness.py`, `critic\probe_restore.py`
  and `critic\probe_fixed.py`: unchanged, byte for byte. So are the five recorded outputs.
- `run.ps1`: added. The papers give the lines that start the models, and the agents redirected the
  standard output into files; this does both.
- `compare.py`: added, the comparison above.
- `checks\design_histories.py`: added, section 3.

## 6. Defects and weaknesses found in the models

Reported, not fixed; none changes an outcome the papers draw.

- **`quick` is a fifth of `full`, not a tenth** (design 12), and also searches the exhaustive part
  one step less deep. It finds none of the rare failures: in its 24 random cells only rollback by
  overwrite (H and I, 55, 8, 92 and 22) and in its ablations only one `safety` with the head's
  carrying off; every `safety` and `copy-cap` cell of `full` is empty under `quick`. So `quick`
  checks that the model runs and that its replays hold, and shows nothing about the search's
  numbers.
- **The search reaches what it reaches.** The design's random search did not draw survival P2's
  four-step history, which the unchanged model fails (`probes\crdt\RUN.md`, section 1), and its
  exhaustive part starts from one fixed divergence. The design states as much of three of its parts
  (12.3, after the ablations); the summary claims of design 1 hold of the histories drawn and not
  beyond them.
- **The design's model leaves out where an entry sits, the recycle bin and the 64-height window**,
  as design 12.1 states. That is what the survival and formal critics' P1 and the formal critic's P2
  are about, and why both critics built models of their own beside it.
- **`critic\probe_liveness.py` changed after its record was written** (the section on what the
  agents ran). The final source gives the record.

## 7. Claims no probe or output on the branch vouches for

Each is quoted as a result of a model, or rests on a number, and no output here shows it.

- **Design 12.4: that the search found the six counterexamples, in that order, against the design
  as first written.** The model was changed after each, by edits the design's agent applied with
  scripts (`patch2.py` to `patch6.py` in its working directory, section 8) to a file that was not
  kept, so no earlier version exists to search. Section 3 shows each history closed now.
- **Design 6.1: "In earlier runs it failed where the step was the second loss of one decision."**
  The same earlier runs.
- **Design 12.4's seventh change**, from replaying `rules-linked` survival C3c with a press in the
  middle: scenario 10 is that replay and holds now; the run that showed the need is not kept.
- **Design 9: deletion records "about a hundred a year" and a head of "3 to 5 KB"**, and **formal P2
  and P8: "80 KB of identifiers after 5,000 writes" and "about 18 years"**: arithmetic or assumed,
  marked so by the papers, with no probe behind them.
- **Formal 4.3: "An off-by-one there made honest histories incomplete in my first run."** The first
  run was not kept; the window's bounds in the final source are the bounds 4.3 states.

The design also quotes measurements of other families as `[M keepass]`, `[M keepassxc]`,
`[M others]`, `[M measured]`, `[M spike]`, `[M android]`, `[M store-entries]`, `[M rules-linked]`,
`[M wildcard]` and `[M crit ...]`. Those are traced where their probes are, not here: among them the
cost of reading "0.26 ms ... 0.08 ms ... 0.02 ms" on 10,054 entries (design 10) under
`store-entries`, and everything about Keepass2Android, KeePassDroid and KeePassDX under
`spikes\storage\android\`.

## 8. What was not carried, and why

Everything in the report directory's `crdt-model\` was carried: the two models, the three probes
and the five outputs. From the agents' working directories:

- **The design's agent's directory** held no probe, only ten scripts that edited files in place:
  `fixdoc2.py`, `fixdoc3.py`, `fixdoc4.py`, `fixrefs.py` and `fixsay.py`, which rewrote passages and
  references of the design's text, and `patch2.py` to `patch6.py`, which rewrote passages of the
  model on its way to the version carried here. Each replaces text that it asserts occurs exactly
  once in the file it is given, so each was written against a version of the file that is gone.
  They are needed neither to run the model nor to check a claim.
- **The formal critic's directory**: `crdt_formal.py`, the three probes and the four outputs are
  the same bytes as the report directory's, carried from there. Not carried: `ce.txt`, which is
  the first 547 lines of `results-critic.txt`; `search-1500.txt`, which is its last 104;
  `liveness.txt`, the same bytes as `results-liveness.txt`; `search-200.txt`, an earlier search of
  200 histories a cell that no paper quotes; `debug_ce5.py` and `probe_merges.py`, debugging scripts
  that no paper quotes and that hold the scratch directory's full path; and `__pycache__\`.
- Neither directory held a clone, a download, a database or a log.
