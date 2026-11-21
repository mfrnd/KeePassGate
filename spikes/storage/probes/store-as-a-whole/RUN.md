# Probes of `store-as-a-whole`

The recommended store of `EVALUATION.md` section 4, run as a whole: the design of the option `crdt`
with the changes of section 4.1, in the design's executable model and over real files. Written on
this branch on 2026-11-16 for the report `papers\ground\measured-store-as-a-whole.md`, which quotes
every figure of the table at the end. The store is configured
from `EVALUATION.md` as it stood on 2026-11-16 at 12:50 UTC, SHA-256
`39f58d3095acce27f3850f16eb6c825854a5bd0700d10e582a742a10aebffd47`.

**A second configuration, the store as decided**, was added the same day for the report
`papers\ground\measured-decided-store.md`: no repair after a Synchronize, and every file read under
the history rule. `run.ps1 -Decided` selects it, its output goes to `out\decided\` and its record is
`recorded-decided\`. The section "The store as decided" at the end holds what it runs, its figures
and what was added for it. With the switch off, every part prints what it printed before: six
parts were run again after the addition and came out the same as `recorded\`.

**Two further configurations, the two readings of the store as decided**, were added the same
night for the report `papers\ground\measured-two-readings.md`: `run.ps1 -Rewrite`, an edited record
written again at the plugin's next own write, with its record in `recorded-rewrite\`, and
`run.ps1 -Copies`, a record that can only take access away read from a copy under another
identifier, with its record in `recorded-copies\`. The section "The two readings" at the end holds
both.

**Five further configurations, the wider readings of a copy**, were added on 2026-11-17 for the
report `papers\ground\measured-copies-extended.md`: `run.ps1 -Reading <name>`, the store as decided
read with a copy of a record applied as far as it can only take access away, with rules per profile
in the model and in the probe. Each has a record of its own, `recorded-reading-<name>\`. The section
"The wider readings of a copy" at the end holds them.

**A further configuration, rules per profile**, was added on 2026-11-17 for the report
`papers\ground\measured-rules-per-profile.md`: `run.ps1 -Rows`, the store as decided with a rule
stored as a record for each node and profile and the node's switch as a record of its own. Its
record is `recorded-rows\`. The section "Rules per profile" at the end holds it.

**A further configuration, the forms rules per profile left owed**, was added on 2026-11-20 for
the report `measured-rules-owed-forms.md`: `run.ps1 -Owed`, rules per profile with four profiles
and the full mask, a group between the root and N, a name off the list, two names whose tags meet,
and a row edited many times. Its record is `recorded-owed\`. The section "The forms rules per
profile left owed" at the end holds it.

**A further configuration, the two forms of a design for S34**, was added on 2026-11-20 for the
report `measured-rules-across-levels.md`: `run.ps1 -S34`, the forms of the design `S34-DESIGN.md`
(form B, a version that lets through records what arrived; form A, it names what stood above),
each played beside the store as decided. Its record is `recorded-s34\`. The section "The two forms
of S34" at the end holds it.

**A further configuration, the lost versions a version settles**, was added on 2026-11-21 for the
report `measured-lost-settled.md`: `model\lost_runs.py all`, three ways of answering a deletion
record of a rule's version played side by side on the same acts, in the model and in `read.py`
over the files `store.py` writes of the model, with no KeePass. Its record is `recorded-lost\`,
with the vectors it prints in `recorded-lost\vectors\`. The section "The lost versions a version
settles" at the end holds it.

| Path | What it is |
| --- | --- |
| `model\crdt_model.py` | a copy of `spikes\storage\model\crdt_model.py` with the switches of section "What was added" below; every new switch at its default is the design's model |
| `model\model_ext.py` | a copy of `spikes\storage\probes\crdt\survival\model\model_ext.py`, over the copy above |
| `model\store_runs.py` | the store in the model: the search, the replays, the cost of each decision, the sequences of `EVALUATION.md` section 6 |
| `model\repair_closes.py` | histories the repair of change 7 closes, and histories it leaves |
| `probe\Store.cs`, `probe\host.ps1` | a copy of the survival critique's `Crdt.cs` and `host.ps1`, extended: KeePassLib 2.62 makes and changes the files |
| `probe\kp.py` | a copy of the critique's `kp.py`: the host's driver and `keepassxc-cli merge -s` |
| `probe\store.py` | the store's entries: `KPG.Kind`, `KPG.Data`, `KPG.Mac` under a store key, the identifier of design 3.2 |
| `probe\read.py` | the store's read rule, once, over a dump of a real file |
| `probe\bridge.py` | the model's gate over real files, with the prediction compared at every step |
| `probe\cases.py` | the sequences of B over real files, and the bridge over random histories |
| `probe\growth.py` | the head and the store as the plugin writes, and the read's time |
| `run.ps1`, `compare.py` | the command, and the comparison of a fresh `out\` with `recorded\` |
| `recorded\` | the output of the run the report quotes |
| `recorded-decided\` | the output of the run of the store as decided, which `measured-decided-store.md` quotes |
| `probe\readings.py` | the parts of the two readings of the store as decided (section "The two readings") |
| `recorded-rewrite\`, `recorded-copies\` | the output of the runs of the two readings, which `measured-two-readings.md` quotes |
| `recorded-reading-nothing\`, `-profile\`, `-verb\`, `-cap\`, `-cap-items\` | the output of the runs of the wider readings of a copy, which `measured-copies-extended.md` quotes |
| `probe\rows.py` | the parts of rules per profile (section "Rules per profile") |
| `recorded-rows\` | the output of the runs of rules per profile, which `measured-rules-per-profile.md` quotes |
| `probe\owed.py` | the parts on real files of the forms rules per profile left owed (section "The forms rules per profile left owed") |
| `recorded-owed\` | the output of the runs of those forms, which `measured-rules-owed-forms.md` quotes |
| `probe\s34.py` | the parts on real files of the two forms of S34 (section "The two forms of S34") |
| `recorded-s34\` | the output of the runs of those forms, which `measured-rules-across-levels.md` quotes |
| `model\lost_runs.py`, `compare_lost.py` | the parts of the lost versions a version settles (section "The lost versions a version settles"), and the comparisons of their outputs and of the vectors |
| `recorded-lost\` | the output of those parts, which `measured-lost-settled.md` quotes; `recorded-lost\vectors\` the three files of vectors `vectors.py --lost` prints |

**Everything ran twice**, on 2026-11-16, on a machine other measurements were sharing. Every
output of the second run is the same as the record line for line, apart from what `compare.py`
masks (section "Running it").

## What it measures

**A. The model.** The design's model, copied, configured to the store as decided: no witness, no
mark (and with it no link, no pin, no Adopt and no refusal written by Seal again for a marked node),
a revocation that refuses the client's key, the store key carried over a Recreate (the design) and
held by the retirement after a Delete with no Create (change 6), a rule from a node's dialog held in
memory until the next save (change 4), the repair after a Synchronize (change 7), and every file a
gate that writes. Changes 1 to 3 are what the design's model already does: it holds no place, no
limit of 64, and counts as damage only an entry under its own element's identifier. The store adds
acts to the alphabet: a renewal or a pairing again under a key already admitted, Delete of the
authority, Create, a save, and KeePass ending before a save. Then:

- the design's random search, its twelve configurations, 20,000 histories each over two and three
  files with the design's seeds, its exhaustive part and its laws (`store-search`), and the same
  search with no hand deletion or edit of the authority's entry (`store-search-noauth`), which
  separates the losses of the store key that decision 9 accepts;
- the extended model of the survival critique, where an entry sits, the bin and KeePassDroid's
  unchanged Save, every entry counted wherever it sits, its configurations and seeds, and two
  configurations with KeePassDX's merge (`store-ext`);
- the design's 20 replays, under the store and under the store with each decision undone
  (`store-scenarios`);
- each change taken by decision undone in turn, over the same histories as the store, configurations
  B, C, D, E, F, H, I and J2 with two files and C and I with three, with no hand loss of the
  authority's entry (`store-ablations`);
- the sequences of `EVALUATION.md` section 6, under the store and with each decision undone, each
  held to two oracles: what was delivered to that file, and every decision any gate made
  (`store-section6`);
- histories that end wider without the repair, replayed with it (`store-repair-closes`).

**B. Real files.** Every gate act is the model's write path, written into a `.kdbx` by KeePassLib
2.62 as entries with a check keyed by the store key (SHA-256); every other step is KeePassLib's or
`keepassxc-cli`'s. Each file is read by `read.py` and held to every decision any gate made: as
decided, narrower, refuses (and for what scope), or wider.

- `b-merges` (B.1, B.3, B.4): 13 histories over two copies, each ending in KeePassLib's five merge
  methods and four `keepassxc-cli` releases, both directions, 234 cells; and five histories of a
  user node under a rule (moved, duplicated, deleted to the bin, restored, deleted for good), 90
  cells, read on real files only.
- `b-slow`: the KeePassXC cells of `b-merges` for the histories H3, H6, H13 and the copy of a user
  node, 32 cells, both copies saved under the slow derivation of `ground-keepassxc` (Argon2d, 200
  iterations, 64 MiB, 2 lanes) just before the merge, so that the two loads fall in different
  seconds.
- `b-dedupe` (B.2): Create new IDs, then Delete Duplicate Entries, both orders, then a third copy
  synchronized with the result.
- `b-deletions` (B.5): nine kinds of entry deleted at a copy in four ways (to the bin, for good with
  a record, for good with none, to the bin with the bin emptied), the read, Seal again where the
  store is incomplete, then a stale copy merged both ways by Synchronize and by KeePassXC 2.8.0.
- `b-clear`, **added from the desktop measurement**: the same deletions and six prunes, then every
  deletion record removed, the read, and a stale copy merged.
- `b-dupes`, **added from the desktop measurement**: Delete Empty Groups and Delete Duplicate
  Entries on the store as it sits, after two gates revoke the same client, after Duplicate Group of
  the plugin's group with and without " - Copy", and after Duplicate Entry of each kind.
- `b-repair` (B.6): eight histories ending in a Synchronize at gate 0, with the repair written
  inside the save and the merged database then written to the other file (as KeePass writes both),
  with the other file written before the repair, and with no repair; and, **added from the desktop
  measurement**, the same merge as File, Import with each of KeePass's methods, with no repair and
  with the repair at the next save.
- `b-growth` (B.7): one gate, 10 clients, 5 names, 20 ruled nodes, 1,000 writes; the head, the store
  and the read at 10, 100 and 1,000.

**C. The bridge.** At every step of B the model's prediction for the file is compared with the
file, entry by entry and record by record, and the model's read of what the file holds with
`read.py`'s read of the file. `b-random` adds 300 random histories over two and three copies.

## The command

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py
```

`run.ps1 -Parts b-merges,b-slow` runs some parts; `-Parallel n` sets how many run at once, 6 by
default and 8 in both runs here. What a run needs: Windows PowerShell 5.1; `uv`, which provides
Python with the standard library alone; KeePass 2.62 and KeePassXC 2.4.2, 2.6.6, 2.7.12 and 2.8.0,
which `run.ps1` provides and checks through `tools\KeePass.ps1` and `tools\KeePassXC.ps1`. No .NET
SDK: `host.ps1` compiles `Store.cs` with the .NET Framework's own compiler. Nothing shows a window.
Each part writes `out\<part>.txt`; the databases go to `work\`, and none is kept.

**How long**, in seconds, on a machine other measurements were sharing, eight parts at once:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `design-full` | 1,446 | 1,569 |
| `store-search` | 1,000 | 1,153 |
| `store-search-noauth` | 889 | 1,042 |
| `store-ablations` | 2,554 | 2,818 |
| `store-ext` | 297 | 322 |
| `store-scenarios`, `store-section6` | 5, 5 | 20, 267 |
| `store-repair-closes` | 171 | 181 |
| `b-growth` | 10 | 25 |
| `b-merges` (alone) | 814 | 797 |
| `b-slow` (alone) | 3,751 | 2,369 |
| `b-dedupe`, `b-dupes` | 5, 15 | 70, 40 |
| `b-deletions` | 221 | 544 |
| `b-clear` | 362 | 931 |
| `b-repair` | 106 | 423 |
| `b-random` | 281 | 763 |

A run of `run.ps1` with every part takes about as long as `b-slow`, an hour here, beside which
the others end in 45 to 50 minutes: the slow derivation is paid at every load of every cell, by
KeePassLib and by `keepassxc-cli`.

## Running it

**How the two runs were made.** Run 1 was `run.ps1 -Parallel 8`; its outputs are `recorded\`. Two
parts were run again on their own, with the same command line `run.ps1` gives them: `b-slow`, whose
first form took every history and was stopped after two histories in about 25 minutes, then
narrowed to the four histories it now holds; and `b-merges`, after the listing of a user node's
copies was sorted by what it reads, since a copy's identifier is random and the first listing
followed it. Run 2 was `run.ps1 -Parallel 8` with every part but those two, which ran on their own
again. `run.ps1 -Parts` empties only the outputs of the parts it is given.

`compare.py` masks the running times, the read's milliseconds in `b-growth.txt` and the time
KeePassLib's Delete Duplicate Entries gives its records, which is the moment it runs; it holds
`design-full.txt` to the design's own record but for the last line.

**Compared.** Every output of run 2 is the same as its record, line for line, with the masks above:
17 files, and `design-full.txt` the same as the design's record but its last line.

**A second boundary.** No figure here depends on the two loads of a KeePassXC merge falling in
different seconds: the store keeps nothing in custom data, and the 32 KeePassXC cells run under the
slow derivation in `b-slow` came out as the same cells under the fast derivation in `b-merges` (24
cells as decided, and 8 of the copy of a user node reading N nothing and the copy read, cell for
cell as in `b-merges`). The column "Second boundary" names the figures that would; none does.

| Figure | Quoted in the report | Recorded | Second run | Reproduced | Second boundary |
| --- | --- | --- | --- | --- | --- |
| The copy with every switch at its default prints the design's record but the time | 2 | 187 lines, the same but the last | the same | yes | no |
| `store-search`: `safety` per cell A to K, two files | 2.1 | 0, 3, 2, 1, 1, 5, 2, 2, 2, 2, 2, 2 | the same | yes | no |
| the same, three files | 2.1 | 0, 2, 2, 0, 0, 0, 0, 1, 1, 1, 0, 2 | the same | yes | no |
| `one-loss` | 2.1 | 1, in C over two files | the same | yes | no |
| accepted copy wider, A to K, two files; three files | 2.1, 5 | 17, 16, 7, 11, 7, 6, 8, 9, 10, 13, 12, 17; 6, 6, 0, 6, 7, 3, 5, 3, 2, 6, 6, 5 | the same | yes | no |
| accepted rule lost before a save, two files; three files | 2.1, 5 | 104, 86, 99, 100, 106, 86, 101, 93, 88, 104, 92, 91; 67, 71, 89, 82, 74, 60, 71, 76, 77, 84, 64, 72 | the same | yes | no |
| repair narrower, two files; three files | 2.1, 2.6 | 49, 23, 16, 31, 29, 22, 20, 27, 25, 49, 74, 70; 32, 16, 21, 19, 16, 16, 12, 17, 8, 43, 43, 47 | the same | yes | no |
| rollback by overwrite, H and I, two files; three files | 2.1, 5 | 167, 34; 244, 66 | the same | yes | no |
| merges only converging narrower, two files; three files | 2.1, 7 | 35; 28 | the same | yes | no |
| repair closed a widening | 2.6, 6 | 1, F over two files | the same | yes | no |
| exhaustive histories; failures; repair writes | 2.1 | 11,110, 47,988, 40,494, 27,930; none; 208 and 172 | the same | yes | no |
| laws of the join | 2.1 | 0 violations in 10,000 | the same | yes | no |
| `store-search-noauth`: `safety` | 2.1, 7 | 1 in D and E over two files, 1 in H over three, none elsewhere, no `one-loss` | the same | yes | no |
| the same: rollback by overwrite H and I; repair narrower | 5, 7 | 170, 44; 267, 76; 20 to 85 and 9 to 40 | the same | yes | no |
| `store-ext`: `safety` | 2.2 | P1 2, P2 1, F 1 and 1, F2 1, DX 1 (two files unless the second F, three) | the same | yes | no |
| `store-scenarios`: fail under the store; hold with a part kept | 2.3 | 3, 9, 10, 15, 16, 18; 10 and 15 with the mark, 18 with the witness; 5 and 6 fail with one gate | the same | yes | no |
| `store-section6`: every outcome of the report's table | 2.4, 5 | as the report's table | the same | yes | no |
| `store-ablations`: every cell of the report's table | 2.5, 6 | as the report's table | the same | yes | no |
| `store-repair-closes`: wider without the repair, C, D, E, F, I, J2; closed | 2.6 | 2, 1, 3, 1, 0, 0; 0 | the same | yes | no |
| `b-merges`: as decided, narrower, wider; cells with a disagreement | 3.1, 4 | 220, 4, 10; 0 | the same | yes | no |
| `b-merges`: N under its own rule in every cell of a user node | 3.2 | gives nothing, 90 cells | the same | yes | no |
| `b-slow`: the KeePassXC cells under the slow derivation | 3.1 | 32 cells, the same as under the fast derivation | the same | yes | the figure itself, and none differs |
| `b-dedupe`: both orders, and the third copy | 3.3 | refuses, nothing served | the same | yes | no |
| `b-deletions`: merges as decided, narrower, refusing, wider | 3.4 | 126, 12, 6, 0 of 144 | the same | yes | no |
| `b-clear`: merges as decided, narrower, wider | 3.5 | 209, 1, 0 of 210 | the same | yes | no |
| `b-dupes`: groups Delete Empty Groups deleted; entries Delete Duplicate Entries deleted per case | 3.6 | 2; 0, 0, 0, 9, 1, 0, 1, 1, 1, 1, 1, 1, 1 | the same | yes | no |
| `b-repair`: every cell of the report's table | 3.7 | as the report's table | the same | yes | no |
| `b-growth`: head bytes, strings, list of replaced heads | 3.8 | 4,280, 24, 33; 5,064, 27, 44; 11,951, 44, 137; 81,551, 272, 1,144 | the same | yes | no |
| `b-growth`: entries, bytes of strings, longest string, longest version list, records | 3.8 | 38, 12,130, 1,165, 0, 33; 39, 13,295, 1,528, 1, 54; 57, 25,838, 4,598, 7, 222; 285, 157,631, 37,830, 36, 2,008 | the same | yes | no |
| `b-growth`: file bytes | 3.8 | 8,055; 8,871; 16,039; 90,167 | 8,055; 8,887; 16,039; 90,183 | differs by some bytes | no |
| `b-growth`: the read, ms | 3.8 | 1.8; 1.0; 1.3; 13.9 | 3.5; 1.1; 1.9; 16.3 | differs, a time | no |
| `b-random`: steps; entry disagreements; read differences; `safety` | 4 | 1,416; 3; 16; 0 | the same | yes | no |
| `b-random`: files at the end against every decision | 4 | 397, 91, 73, 163 | the same | yes | no |
| the bridge's disagreements per part (`b-dedupe`, `b-deletions`, `b-clear`, `b-dupes`, `b-repair`) | 4 | 2 and 6, 0 and 18, 0 and 15, 9 and 2, 0 and 0 | the same | yes | no |

## What was added to the copied model

Each line added or changed carries "ADDED (store-as-a-whole)". With every new switch at its default
the copy prints the design's record (`design-full.txt` against
`spikes\storage\model\recorded\results-full.txt`, every line but the time).

`crdt_model.py`:

1. **Switches** in `Flags`, each defaulting to the design: `witness` (the gate writes the witnesses;
   `T` reads them), `mark` (the mark, the pin, Adopt, and the refusal Seal again writes for a marked
   node), `revkey` (a revocation refuses the client's key), `keyretire` (a retirement with no
   authority after it carries the store key), `pending` (a rule from a node's dialog waits in memory
   for the next save), `repair` (change 7), `gates` (how many files write; 0 is every file),
   `newacts` (the store's acts in the alphabet), `idealkeys` (the oracle reads under every key of
   the lineage). `Flags.but` copies a preset with changes. Presets: `IDEAL`, the oracle, which under
   the design's alphabet reads exactly as `FULL`; `STORE`, the store as decided.
2. **Clients and keys.** A client is a key and its certificates are `c0`, `c0.1`, `c0.2`;
   `rtuple` gives what a revocation refuses. Revoke writes a revocation for each certificate of the
   key admitted at that gate, which under the design's alphabet is the single certificate.
3. **Store keys.** An element has a key label; the store keys a file holds are those of its own
   authorities and, with `keyretire`, the key a retirement carries (`El.carry`). An element verifies
   under a key held; a head's or a decision's deletion record is read by its keyed tag, so only
   under a key held. Every element is written under the key the gate holds (`wkey`); under the
   design's alphabet that is always `K`. Recreate carries the served authority's key, as the design
   does.
4. **New acts**: `renewcert` (a renewal, or a pairing again, under a key admitted at that gate,
   unless its renewals are refused forever), `delauth` (Delete of the authority with no Create; with
   `keyretire` the retirement carries the key), `create` (Create when no authority stands: under the
   key held, or under a new key when none is held), `quit` (KeePass ends before a save: what waits
   in memory is gone), `plantk` (a file made by somebody holding the store key: a version granting
   read and write at N that names every version present, and a head), `dedupe` (Delete Duplicate
   Entries after an import with new identifiers: of an element and its copy, the original goes, with
   a record).
5. **Change 4.** With `pending`, a rule is written to the gate's copy in memory and the file on
   disk is kept beside it (`Rep.disk`); another program reads the file on disk; a save, any write of
   the plugin's own, and KeePass's own merges and imports there write it; while a rule waits, no
   other program changes that copy. `quit` restores the file on disk.
6. **Change 7.** `merge_step`: after a Synchronize at a gate, `repair` writes, inside that save, a
   refusal in force before and not after, a removal of a name a removal held off the list before,
   and for a register whose value gives more after than before, a version holding the earlier value
   met with what stands, naming nothing, then a head. Not decisions, so the oracle does not hold
   them. Counted as `info:repair-writes`, `info:repair-closed` (a file wider before the repair and
   not after it), `info:repair-left-wider` and `info:repair-narrower` (a file narrower than the
   oracle after the repair and not before it). A first form wrote the earlier value itself; when
   nothing stood after the merge it granted alone, in the history `rule 2 root inherit; revoke 2 c0;
   del 2 (auth) 1; create 2; merge kp_sync 1 2` of the extended model with KeePassDX, where file 1
   gave read at the root; no output of that form was kept.
7. **Without the mark** a node reads no mark, a rule sets none, a save writes no pin, Adopt does
   not apply, and Seal again writes no refusal for a marked node; a copy of N made while N has a
   rule in the store is counted, and a copy wider than N is `accepted:copy-wider` (decision 16).
8. **The checks.** `narrower_than`, the other direction of `narrower`; a property beginning
   `accepted:` (a residue a decision accepts) or `info:` is counted once a history and ends no
   history; KeePass ending before a save with a narrowing waiting is `accepted:unsaved-rule-lost`
   (decision 13); every copy saves before the convergence check; a convergence that differs from
   the oracle under the store's acts is labeled wider or not wider.
9. **Alphabets and searches** take the flags; `maxclients` (3 unless the growth probe raises it);
   the exhaustive part saves each rule of its divergence under `pending`.

`model_ext.py`: the file on disk carried by its clone, read by its merges and its `over`; no bin,
restore, touch or empty while a rule waits in that copy; its alphabet and search take the flags. Its
`run` ignores `accepted:` and `info:`, which it never produces.

## What was changed in the copied probe

- `Store.cs`, from `Crdt.cs`: an element carries any `KPG.` strings it is given, `KPG.Data`
  protected for an authority and for a retirement that carries the store key; the check and the
  identifier are made by `store.py`, with a key, where `Crdt.cs` used a SHA-256 with no key; the
  dump carries every `KPG.` string, each entry's parent, and every group; the user tree has fixed
  identifiers and an entry N and a group Other; added commands: `kdf` (the slow derivation), `move`,
  `dup`, `restore`, `dupgroup`, `deg`; `bin` and `delrec` take a group too, and `empty` empties
  groups in the bin.
- `host.ps1`: compiles `Store.cs`, into `bin\` under a name made from the source's hash, so that a
  host still running keeps its build.
- `kp.py`: this family's paths; the host started without `PSModulePath`, so that a Windows
  PowerShell started from PowerShell 7 loads its own modules; the dump read for `Store.cs`; `uid`
  removed; the merge's timeout 600 s for the slow derivation.

## Defects and limits

Reported, not fixed.

- **The design's model holds no place.** Change 1 is run only in the extended model, where an entry
  sits in the store group or the bin, and on real files; an entry moved to another group is run on
  real files alone.
- **The oracle reads under every store key of the lineage.** After the authority's entry is deleted
  or edited by hand and Create starts a new key, the store passes over what was written under the
  lost key, as decision 9 and `EVALUATION.md` 4.3 accept, while the oracle still holds it: a rule
  set again after that reads as wider than the old rule. `store-search-noauth` and the ablations
  leave these losses out; `store-search` and `store-ext` keep them.
- **The model's renewal check** counts a renewal asked of a client whose certificate chains to an
  authority no longer served, which no client can reach.
- **Change 4 in the model**: while a rule waits in a copy, no other program changes that copy, and a
  copy of N is not made there; so the counts of `accepted:copy-wider` differ between the store and
  the store with change 4 undone for that reason.
- **The repair in `b-merges`, `b-deletions` and `b-clear`** runs after KeePassLib's Synchronize into
  one file; only `b-repair` writes the merged file to both files, as KeePass does. That KeePass's
  save events fall where the repair needs them is the desktop measurement's, not run here.
- **The probe's imitations**: the bin, the unchanged save, Duplicate Entry and Duplicate Group, the
  restore from the bin and the move are done through KeePassLib's object model, not by the commands
  in KeePass's window; Delete Duplicate Entries, Delete Empty Groups and the merges are KeePassLib's
  own methods. The restore puts N into the root group rather than the group it left: the store needs
  nothing above KDBX 4.0, and a previous parent with no other 4.1 feature is dropped when the file
  is written (`RESULTS.md` 2.1), so the probe finds none. N's rule names N and follows it wherever
  it is.
- **`read.py` and the model differ in two readings, by construction**: with no store key held,
  `read.py` cannot tell a head's deletion record from any other and answers `nostore`, where the
  model reads the tag without the key and answers `incomplete`; both serve nothing. A foreign
  authority verifies in `read.py` under the key its own document holds, so its key is held; in the
  model nothing foreign verifies. Either way two authorities stand and nothing is served.
- **Delete Duplicate Entries dates its records at the moment it runs**, where the files' times are
  invented from 2026-01-01; the bridge's record times disagree there, and a later merge meets
  records dated after every entry.
- **`read.py` lists a renewal answer only for a client the file still names**, where the model
  lists every client it paired; seven steps of `b-random` differ in that listing alone.
- **The repair in the model writes under the key it last held** even where the merge left the file
  holding none; a plugin would hold the key in memory from before the merge. Such a file serves
  nothing either way.
- **The read is timed in Python**, in a console, not in the plugin's C# on KeePass's thread.
- **Every file is under the fast derivation** (AES-KDF, one round) but in the cells of `b-slow`.

## Not run

- Nothing in a window, nothing on a phone, no second KeePass instance with the plugin: several gates
  are files that each write through the model's write path.
- The plugin's API (`EVALUATION.md` section 9, run 1).
- Rows 5, 12, 13 and 22 of `EVALUATION.md` section 6: a node's place decided by a merge, a former
  bin becoming a group, a rule narrowed while a save runs, and two files with the same entries
  answering differently, which the formal critique's model measures; that model was not run on the
  store.
- A renewed store key at Recreate; kdbxweb, pykeepass, Keepass2Android and KeePassDX as programs
  (KeePassDX's merge is run only as the extended model's reading of it); XML Replace.
- A clock behind on real files beyond a deletion record dated at the start of the times.
- The limit of 64 as a switch: the design's model holds none, so dropping it is what the model
  already runs; the formal critique's CE5 is where the limit was measured.

## The store as decided

Added on 2026-11-16 for `papers\ground\measured-decided-store.md`, after two decisions changed what
the store is: no repair after a Synchronize (the plugin puts nothing back after a merge), and the
history rule (a record counts in its current version when that version passes its check, or else in
the newest item of the entry's history that passes; only an entry of the store with no passing
version, or a missing summary entry, stops the whole database). Everything else is the store of the
first record: no witness, no mark, several gates, a revocation naming the client's key, the store
key carried over a Recreate and kept in the retirement after a Delete, no memory on the gate's
machine.

### What it runs

`run.ps1 -Decided` runs these parts, each into `out\decided\<part>.txt`, held to
`recorded-decided\` by `compare.py --decided`:

| Part | What it is |
| --- | --- |
| `store-search`, `store-search-noauth`, `store-ext` | the model's searches of the first record, the repair switched off. The model holds no history of an entry, so a hand edit is still damage there; the history rule is run on files |
| `b-merges`, `b-dedupe`, `b-deletions`, `b-clear`, `b-dupes`, `b-random` | the same cells as the first record, with no repair after any Synchronize and every file read under the history rule |
| `b-repair` | the same histories, in the modes with no repair only: the Synchronize at gate 0 with both files written, and each File, Import method with no repair |
| `b-growth` | B.7 again, read under the history rule |
| `b-history` | new: each kind of store entry edited at copy 0 by an operation of KeePass's own, then copy 0 read, each copy merged with the other by Synchronize and by KeePassXC 2.8.0, and copy 0 after the gate writes again; each read under the history rule and under the plain stop of the first record |

**`b-history` in full.** The base is `revoke 0 c0; recreate 0; pair 0; padd 0 pg; prem 0 pf; rule 0
N none; renew 0 c1 forever; Synchronize 1 from 0`, so that both copies hold an authority, a
retirement, an admission record (c1), a revocation (c0), a profile addition (pg), a removal (pf), a
rule version (N, nothing), a renewal answer (c1, refused forever) and a summary entry. A tenth kind,
the retirement that holds the store key, has `delauth 0` before the Synchronize; no authority is
served there, so each of its reads is followed by Create at that copy and read again. The
operations, each made by KeePassLib in copy 0 as KeePass's own commands make them
(`CreateBackup` with the database, which trims the history to the database's limits):

| Operation | What `Store.cs` does |
| --- | --- |
| `dialog` | the entry dialog: the first character of the record's subject in `KPG.Data` changed |
| `field` | the entry dialog: `KPG.Data` deleted |
| `quick` | a quick edit, Expires: Now, which the check does not cover |
| `move`, `bin`, `delete` | moved to the group General; deleted to the recycle bin; deleted for good with a deletion record |
| `edits10`, `edits11` | the dialog edit and then 9 or 10 dialog edits of the title: 10 or 11 edits under KeePass's default limit of 10 history items |
| `quick10` | the dialog edit and then 10 quick edits |
| `off` | the database's limit of history items set to 0 in both copies, then the dialog edit |

Ten kinds by ten operations by six columns, less the gate's own write for the tenth kind, are 590
cells.

**Left out, to keep both runs within the time set for them**: `b-slow`, whose 32 cells came out the
same as the fast cells in the first record and whose question, the seconds of two loads, touches
nothing this store keeps; `store-ablations`, `store-scenarios` and `store-section6`, whose records
already hold a column with the repair undone; `store-repair-closes`, which is about the repair; and
`design-full`, which checks the copy of the design's model and is unchanged.

### The command

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Decided -Parallel 8
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --decided
```

A run of every part empties `out\decided\` and the databases of this configuration, `work\d-*`,
and nothing of the first record's. Both runs here were made under an ordinary user token, eight
parts at once.

**How long**, in seconds:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `store-search` | 456 | 426 |
| `store-search-noauth` | 401 | 381 |
| `store-ext` | 136 | 131 |
| `b-merges` | 91 | 96 |
| `b-random` | 95 | 100 |
| `b-clear` | 90 | 95 |
| `b-history` | 75 | 95 |
| `b-deletions` | 60 | 65 |
| `b-repair`, `b-dupes`, `b-growth`, `b-dedupe` | 20, 10, 10, 5 | 20, 10, 5, 5 |

**Compared.** Every output of run 2 is the same as `recorded-decided\` line for line, with the masks
of "Running it": 12 files.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `store-search`: `safety` per cell A to K, two files; three files | 0, 3, 2, 1, 1, 6, 2, 2, 2, 2, 2, 2; 0, 2, 2, 0, 0, 0, 0, 1, 1, 1, 0, 2 | the same | yes |
| `store-search`: accepted rule lost before a save, two files; three files | 86 to 106; 60 to 89, each cell as in `recorded\` | the same | yes |
| `store-search`: exhaustive histories; failures | 11,110, 47,988, 40,494, 27,930; none | the same | yes |
| `store-search-noauth`: `safety` | 1 in D and E over two files, 1 in H over three, as in `recorded\` | the same | yes |
| `store-ext`: every line | as in `recorded\` but the first line | the same | yes |
| `b-merges`: as decided, narrower, wider | 221, 3, 10 | the same | yes |
| `b-merges`: N under its own rule in every cell of a user node | gives nothing, 90 cells | the same | yes |
| `b-deletions`: as decided, narrower, refusing, wider | 126, 12, 6, 0 of 144 | the same | yes |
| `b-clear`: as decided, narrower, wider | 210, 0, 0 of 210 | the same | yes |
| `b-dedupe`, `b-dupes` | every outcome as in `recorded\` | the same | yes |
| `b-repair`: the 96 cells of the modes with no repair | as in `recorded\`, cell for cell; R1 and R8 wider at gate 0 and in file 1 | the same | yes |
| `b-random`: steps; `safety`; files at the end against every decision | 1,419; 0; 171, 406, 93, 54 | the same | yes |
| `b-growth`: every size | as in `recorded\` | the same | yes |
| `b-history`: under the history rule, as decided, narrower, refusing, wider | 390, 28, 172, 0 of 590 | the same | yes |
| `b-history`: the 500 reads before any write, under the plain stop | as decided 214, narrower 8, refusing 278, wider 0 | the same | yes |

### What was added for it

Each line added or changed carries "ADDED (decided store)". With the switch off, every part prints
what it printed before: `b-dedupe`, `b-dupes`, `b-repair`, `b-merges`, `b-growth` and
`store-scenarios` were run again after the addition and came out the same as `recorded\` line for
line, with the masks of "Running it".

- `Store.cs`: `dump <path> h`, the dump with a line for each history item of each entry;
  `history-edit`, the four edits of the table above; `limits`, the database's limit of history
  items.
- `kp.py`: `dump(path, history=True)` reads those lines.
- `read.py`: `read(dump, history=True)`, the history rule: an entry is the store's when any of its
  versions carries a `KPG.` string; a store key is found in any version of an authority or a
  retirement; an entry counts in its current version when that version passes, or else in its
  newest history item that passes; only an entry with no passing version is damage. With `history`
  off it reads as before and counts nothing new.
- `bridge.py`: `decide()`: no repair unless a mode asks for it, dumps with history, every read under
  the history rule, and the model adopting an entry as intact when any of its versions passes. Its
  databases go to `work\d-<tag>`.
- `cases.py`: `--decided`, which calls `decide()`; the part `history`; and in `repair`, with the
  switch on, only the modes with no repair.
- `growth.py`: `--decided`. `store_runs.py`: `--decided` for `search`, `search-noauth` and `ext`,
  with the repair off.
- `run.ps1`: `-Decided`. `compare.py`: `--decided`.

### Defects and limits of this configuration

Reported, not fixed.

- **The model holds no history of an entry.** In the model's searches an edit is damage, as in the
  first record; the history rule is run on files only (`b-history`, `b-random`).
- **The plugin writes nothing again for a record that counts by its history** in this
  configuration: the next write of the plugin leaves the edited entry as it is, and the summary
  entry it writes restates what is in force as before. Writing such a record again at the plugin's
  next own write is the configuration of "The two readings" below.
- **A store entry is recognized as damaged only under a key the file holds.** An authority, or a
  retirement that holds the store key, with no passing version leaves no key in the file, so the
  read answers `nostore` and the model `incomplete`, as in the first record's defect of the same
  kind; both serve nothing.
- **`read.py` lists a renewal answer only for a client the file still names** (the first record's
  defect): 93 of the 121 read differences of `b-history` are this, each in a copy where c1's record
  is gone or names a retired authority; the other 28 are `nostore` against `incomplete`.
- **KeePassLib stands for KeePass's window**: the entry dialog and the quick edit are `CreateBackup`
  with the database, then the change, then the modification time, as `edited-refusals` stands for
  them.
- **The first run after `Store.cs` changes compiles it in every part at once**, and parts that
  start together can fail to write the same `bin\Store-<hash>.dll` (CS0016, the file in use). Such a
  part ends at once with that error in its `.err`; running it again uses the file the others wrote.
  It happened once here: after the record, local names of the probe were renamed for the spelling
  check, and of the five parts run again to confirm the record, three failed so; run again, all
  five printed `recorded-decided\` line for line.
- **Only Synchronize and KeePassXC 2.8.0** merge the edited copy in `b-history`; the other methods
  of KeePassLib and releases of KeePassXC were run on edited records in `edited-refusals`, on a
  store without summary entries.

### Not run in this configuration

- Seal again after a stop of `b-history`; an edit in both copies; an edit by pykeepass or by a phone
  program; Database Maintenance's history page; a history item edited by script. Each was run on a
  store without summary entries in `edited-refusals`.
- Everything the first record does not run.

## The two readings

Added on 2026-11-16 for `papers\ground\measured-two-readings.md`. The run of the store as decided
left two readings open. **Reading 1**, since decided: whenever the plugin writes to the store
anyway, it also writes again every record that counts only by its history, so that the written
version is current again; never after a merge and never on a read. **Reading 2**, to be decided from
this run: a record that can only take access away counts from any entry whose content passes the
check, whatever the entry's identifier; a record that gives access never counts from a copy. Each
reading is a configuration of its own on top of the store as decided: `run.ps1 -Rewrite` and
`run.ps1 -Copies`.

### What they run

**`-Rewrite`**, into `out\rewrite\`, held to `recorded-rewrite\` by `compare.py --rewrite`. After
every gate act (never after a merge, an import or a read), `bridge.py` reads the file under the
history rule and writes again every record that counts by a history item, from that item, through
`Store.cs rewrite`. Two forms, each a set of parts; the parts ending in `-cleared` are the second:

| Form | What `Store.cs rewrite` does to the entry |
| --- | --- |
| `backup` | the edited version put into the history first, as an edit through KeePass's own path does (`CreateBackup` with the database, which trims the history to the database's limits); then every `KPG.` string of the history item that passes, with its protection, becomes current, and every other `KPG.` string of the current version goes |
| `cleared` | the same, with the entry's history emptied instead of the edited version put into it |

Both keep the entry's identifier, place, title and every string the check does not cover, and set
the modification time to the time of the gate's act.

| Part | What it is |
| --- | --- |
| `one` | each kind of store entry of `b-history` and each of its ten operations at copy 0, then one write of the gate's own (a profile name added; Create for the retirement that holds the store key); the read, the entry's versions, further edits until the store refuses (a dialog edit of the payload, then of the title, at most 12), and the write's file merged with the unedited copy 1 by Synchronize and by KeePassXC 2.8.0 both ways. 100 cells, each with a write and four merges |
| `alternate` | for each of the ten kinds, edits and the gate's writes alternating: 1 edit then a write for 22 cycles, 3 for 8, 10 for 3, 11 for 2; and 1 edit then 12 writes alone |
| `two` | two gates, for the nine kinds with an authority: the record edited at copy 0 and synchronized into copy 1, then both gates writing (`same`), gate 0 writing and then copy 1 editing again (`later`), and the reverse (`earlier`); each followed by Synchronize, Import with Overwrite if newer, Overwrite existing, Keep existing and Create new IDs, and KeePassXC 2.8.0, each into 0 and into 1 (12 merges), and the gate at the target writing again; 324 merge cells. Then five rounds of: an edit at 0, Synchronize into 1, both gates write, Synchronize into 0 and into 1 |
| `prune` | two gates: the record edited at copy 0; gate 1 makes a decision that takes the record out of its copy with a deletion record (revokes c1, recreates the authority, removes pg, sets N to read and write, allows renewals of c1); gate 0's write writes the record again, after gate 1's decision and before it; then the 12 merges; 120 merge cells |
| `b-random` | `cases.py random 300 7`, which holds the model's edit among its faults, with the rewrite after every gate act |
| `two-none`, `prune-none` | the control: `two` and `prune` with nothing written again (`--rewrite none`) |

**`-Copies`**, into `out\copies\`, held to `recorded-copies\` by `compare.py --copies`. Every read
by `read.py` with `copies`: an entry whose identifier fails but whose content passes the check under
a store key the file holds is read as the record under the identifier its content gives, when the
record is a revocation, a retirement (its refusal; a store key is never taken from a copy), a
profile removal, or a version whose value refuses everything (a rule of nothing, renewals refused
forever). An admission, a profile addition, an authority, a version of any other value and a summary
entry never count from a copy. Two variants: `held`, where such a record is held to the completeness
check like any record, so that a record no summary entry restates stops the store; and `exempt`, in
the parts ending in `-exempt`, where it is not. A gate act is refused where the reading stops the
store and the model, which holds no reading of copies, would write.

| Part | What it is |
| --- | --- |
| `b-merges`, `b-dedupe`, `b-deletions`, `b-clear`, `b-dupes`, `b-repair`, `b-random` | the parts of the store as decided, the same cells, read with copies |
| `copies` | copies that are not an import, for five kinds that count from a copy and four that never do: Duplicate Entry; the duplicate moved to another group (KeePass's copy and paste stood in for); in the recycle bin; then Delete Duplicate Entries; the original deleted for good with and with no record; the original deleted with a record, the copy carried into copy 1 and deleted at copy 0, then KeePassXC 2.8.0 and Synchronize into copy 0; for the rule and the renewal answer, the original replaced by a widening version and then every merge both ways. Then the combination with writing again: the ten histories whose Create new IDs cell into copy 1 is wider in the store as decided, each import followed by the gate's write, the records counted from copies written under the identifiers their content gives (`Store.cs recanon`), a summary entry restating them, and Synchronize both ways |

### The command

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Rewrite -Parallel 6
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --rewrite
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Copies -Parallel 6
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --copies
```

A run of every part of a configuration empties its own output and databases (`work\wb-*`,
`work\wc-*` and `work\wn-*`, or `work\ch-*` and `work\ce-*`) and nothing of the other
configurations'. Both runs here were made under an ordinary user token, the two configurations at
once, six parts each.

**How long**, in seconds:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `one`, `one-cleared` | 131, 130 | 136, 136 |
| `alternate`, `alternate-cleared` | 55, 55 | 70, 75 |
| `two`, `two-cleared`, `two-none` | 90, 80, 50 | 95, 90, 55 |
| `prune`, `prune-cleared`, `prune-none` | 25, 25, 20 | 30, 30, 25 |
| `b-random`, `b-random-cleared` (rewrite) | 115, 85 | 131, 100 |
| `b-merges`, `b-merges-exempt` | 156, 146 | 156, 141 |
| `b-clear`, `b-clear-exempt` | 141, 110 | 136, 100 |
| `b-random`, `b-random-exempt` (copies) | 141, 90 | 130, 80 |
| `b-deletions`, `b-deletions-exempt` | 85, 85 | 85, 85 |
| `copies`, `copies-exempt` | 55, 55 | 50, 30 |
| `b-repair`, `b-dupes`, `b-dedupe`, each variant | 25 and 20, 10, 5 | 25 and 20, 10, 5 |

**How the two runs were made.** Run 1 of `-Rewrite` was made twice: the first run picked the entry
to edit afresh at each cycle of `alternate` and each round of `two`, which for a profile addition
picked the newest addition, the addition the gate's own write had just made; the pick was fixed to
the same entry (the newest summary entry for the summary entry, since every write replaces it), the
control parts were added, and the whole configuration ran again as run 1. In run 1 of `-Copies`, the
part `copies` printed the identifier an import drew for each copy and was run again on its own with
the identifier left out. Run 2 was every part of both configurations.

**Compared.** Every output of run 2 is the same as its record line for line, with the masks of
"Running it": 12 files of `recorded-rewrite\` and 16 of `recorded-copies\`.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `one`, both forms: the reads after the write and after each merge | A 337, K 18, R 124, n 20, x 1; none wider | the same | yes |
| `one`: dialog, field, edits10, each kind with an authority and the key carrier | written again 1, as decided after the write and in all 4 merges; then 10 further edits as decided, the 11th refuses | the same | yes |
| `alternate`, both forms: every kind, 1, 3 or 10 edits between writes | as decided through every cycle (22, 8, 3), never a stop | the same | yes |
| `alternate`: 11 edits between writes | the store refuses at the 11th edit; nothing is written again | the same | yes |
| `alternate`, `backup`: history after each write | grows by 2 a cycle to the limit of 10, the passing version current | the same | yes |
| `alternate`, `cleared`: history after each write | 0 | the same | yes |
| `two`, both forms and the control: 324 merge cells | as decided in all 324 | the same | yes |
| `two`, five rounds: history of the record after each round, `backup`; `cleared`; the control | 3, 6, 9, 10, 10; 1 in every round; 1, 2, 3, 4, 5 with the current version failing | the same | yes |
| `prune`, both forms and the control: 120 merge cells | as decided 110, narrower 4, wider 6, each the same cell in the control; every wider cell Create new IDs into copy 0 | the same | yes |
| `b-random` (rewrite), both forms | as in `recorded-decided\`: 1,419 steps, 0 failures of `safety`, files 171 wider, 406, 93, 54; no record written again (the random edits fall on summary entries, revocations and retirements, and none was followed by a write at that file while it counted by its history) | the same | yes |
| `b-merges` held; exempt | as decided 220, narrower 3, refuses 10, wider 1; as decided 226, narrower 3, refuses 2, wider 3 | the same | yes |
| `b-deletions`, `b-clear`, `b-repair`, both variants | every cell as in `recorded-decided\` | the same | yes |
| `b-dedupe`, `b-dupes`, both variants | every outcome as in `recorded-decided\` | the same | yes |
| `b-random` (copies), held; exempt | 1,418 steps, files 169 wider, 406, 93, 56, 1 gate act refused; 1,419 steps, 169, 407, 93, 55, none refused | the same | yes |
| `copies`: the combination, held; exempt | as in the report's section 3.5 | the same | yes |

### What was added for them

Each line added or changed carries "ADDED (two readings)". With the new switches off, every part
prints what it printed before: `b-dedupe`, `b-dupes`, `b-repair`, `b-merges`, `b-growth` and
`store-scenarios` of the first record and `b-history`, `b-merges`, `b-dedupe`, `b-dupes`,
`b-repair`, `b-random` and `b-growth` of the store as decided were run again after the addition and
came out the same as their records line for line, with the masks of "Running it".

- `Store.cs`: `rewrite <path> <uuid> <t> <form> <index>`, the record written again from the history
  item `index`, in the form `backup` or `cleared`; `recanon <path> <copy> <uuid> <t>`, a record
  counted from a copy written as a new entry under the identifier its content gives.
- `read.py`: `read(dump, history, copies)`; the answer also names each record that counts by a
  history item (`byhist`), the store keys held (`held`) and each record counted from a copy
  (`fromcopy`); with `copies`, the count `fromcopy`.
- `bridge.py`: `rewrite(form)` and `copies(variant)`, each the store as decided as well; `rd`, the
  read of the configuration; `rewrite_again`, run after every gate act under `rewrite`; `recanon`;
  a gate act refused under `copies` where the reading stops the store; `outcome` reads a dump
  already made when given a dump; the databases of a configuration under `work\<prefix><tag>`, the
  prefix of the store as decided still `d-`.
- `cases.py`: `--rewrite FORM` and `--copies VARIANT`; `random` counts the records written again
  or the gate acts refused.
- `readings.py`: new, the parts `one`, `alternate`, `two`, `prune` and `copies`.
- `run.ps1`: `-Rewrite` and `-Copies`. `compare.py`: `--rewrite` and `--copies`.

### Defects and limits of these configurations

Reported, not fixed.

- **The model holds neither reading.** Under `-Rewrite` it holds the written version and its time,
  and KeePassLib keeps the target's time where both current versions are the written version, so
  `two` lists entry disagreements of the modification time alone. Under `-Copies` its read differs
  from `read.py`'s wherever a copy counts; those differences are listed in the parts of the store
  as decided and not in `copies`.
- **The model's write path does not prune by the reading of copies.** A plugin whose next write
  takes out what its read finds retired or revoked would, after a retirement counted from a copy,
  take out the authority that retirement names. The combination in `copies` shows it once the
  record is written under its own identifier (H13 then holds no store key); without that, the probe
  writes as the model does and does not show it.
- **Seal again** stands for the summary entry a write would carry in the combination; the plugin's
  own Seal again after a stop by a copy was not run.
- **A retirement that settles which authority is served**, where two stand, gives access; the
  reading counts it as a refusal. No cell here holds two standing authorities at the import.
- **The rule model holds one value per node**, so a version that narrows for one profile and widens
  for another cannot be written here.
- **KeePass's copy and paste** is stood in for by Duplicate Entry and a move; the window's own paste
  was not run.
- **KeePassLib stands for KeePass's window** in every edit, as in the store as decided.

### Not run in these configurations

- Writing again as a new entry beside the edited entry, its old entry replaced through the summary
  entry: the identifiers of this store are made from the content, so such a record is a new record
  under a new identifier, and every later record that names the old record would have to name the
  new record too.
- KeePassXC releases other than 2.8.0, the slow derivation, and a history limit other than 10 and 0
  under the rewrite; a merge of copies under KeePassDX's reading.
- The reading of copies in the model's searches.
- Everything the store as decided does not run.

## The wider readings of a copy

Added on 2026-11-17 for `papers\ground\measured-copies-extended.md`. The reading of a copy that
`-Copies` runs counts a rule version only where it refuses everything, and left open what a copy of
any other version, of a removed rule, of a retirement where several authorities stand, of the key
carrier and of a summary entry may do. These configurations define that for every kind of record
and measure each definition: `run.ps1 -Reading <name>`, each the store as decided as well.

### What they run

**What every reading here has in common.** A copy is an entry whose content passes the check under a
store key the file holds through its own records, under an identifier its content does not give. A
revocation, a retirement and a profile removal count from a copy as under `-Copies`, with a
difference: a retirement read from a copy refuses the authority it names and never leaves another
authority served; which authority is served is decided by the file's own records. A copied rule
version or renewal answer never joins its object's versions: it replaces no version of the file's
own and grants nothing. It is applied as a limit, a document combined with the file's own rule of
the object word by word, each word given only where both give it. It limits unless a version the
file reads names it as replaced, or another passing copy does, or the file holds the version itself.
A rule version or renewal answer the gate writes names, as replaced, the copies of that object's
versions the file reads. A store key is never taken from a copy, and an admission, a profile
addition, an authority and a summary entry's presence never count from a copy. Held to the
completeness check, a copy that takes away what no summary entry in force restates stops the store;
exempt, it does not.

| `-Reading` | How far a copied rule version or renewal answer limits |
| --- | --- |
| `nothing` | only a copy that refuses everything: the reading `-Copies` runs, with the retirement as above |
| `profile` | per profile the copy names: where the file's own rule of the node names the profile and the copy grants within what that rule grants, the profile gets what the copy grants; where the file's own rule does not name the profile, or the file holds no rule of the node, only a copy that grants the profile nothing limits |
| `verb` | per profile the copy names: every word the copy does not grant is taken away. A profile the copy does not name is left alone |
| `cap` | the same, and a profile the copy does not name gets at most what arrives from above: the copy combined with the file's own rule as two versions made apart are |
| `cap-items` | `cap`, and a copy of a summary entry gives its items: a restated revocation, retirement or removal as that refusal, a restated version as a copy of that version; never its presence |

Each reading writes into `out\reading-<name>\` and is held to `recorded-reading-<name>\` by
`compare.py --reading-<name>`. The parts ending in `-exempt` are the variant exempt from the
completeness check; the others are held to it.

| Part | What it is |
| --- | --- |
| `b-merges` | the 13 histories of the store as decided and nine further histories, each ending in KeePassLib's five merge methods and four `keepassxc-cli` releases, both directions: 396 cells, and the 90 cells of a user node. The further histories: H14 a version that narrows a profile and widens the other; H15 a rule that grants nothing, then widened for a profile; H16 a narrowing on a node below a wider rule, where the stale copy holds no rule of the node; H17 a rule removed where the node above gives less; H18 a rule removed where the node above gives more; H19 a rule changed to other words, neither within the other; H20 a rule narrowed for a profile at a gate and widened for the other profile at the other gate; H21 a rule that names a profile less, which inherits again below a narrower rule; H22 two authorities standing after both gates recreate apart, and gate 0 keeping its own |
| `b-dedupe`, `b-deletions`, `b-clear`, `b-dupes`, `b-repair` | the parts of the store as decided, the same cells |
| `b-random` | `cases.py random 300 7`, its alphabet with five rules per profile for the root and for N and the act `keep` added |
| `copies` | the part of `-Copies`, with a rule per profile among its kinds and the gate's own write beside a copy; a replaced narrowing and what clears a limit a copy sets (a second import, a Synchronize, the rule set again with and without the gate naming the copies it read, Seal again, the copies deleted by hand); the ten histories whose import ends wider in the store as decided and six of the further histories, each import followed by Seal again where the reading stops the store, an edit of the authority's entry, the gate's write with the decided rewrite (`backup`) and nothing written from a copy, then Synchronize both ways and every copy deleted by hand; and, as the control, the combination of `-Copies` that writes a copied record under its own identifier |
| `kinds` | three kinds read from a copy or not over the same file: a retirement that settles which authority is served (three files: copy 2 taken after gate 0 keeps its authority, then imported into the copy that holds two standing); a summary entry's items, after a refusal's entry was deleted by hand with no record, and an older summary entry copied before a later widening; a store key taken from a copy of the authority's entry after the entry was deleted and the store started again |

**How a rule per profile is written.** A rule act takes `pf=rw,pg=r`: each profile it names with its
words, a profile left out inheriting. Two profiles, `pf` and `pg`, and two words, read and write.
The entry's `doc` string is `pp:pf=rw,pg=r`; the documents of the recorded configurations
(`const:rw`, `cap:rw`) are written as before and read, where rules per profile are on, as naming
every profile alike. Words print as before where every profile holds the same (`rw`), and
otherwise per profile (`pf:rw pg:r`, `-` for none).

**What every part prints at its end.** Each file's answer is also read as the store as decided
reads the same file; the part counts the answers of the same class and each change of class (`WIDER
-> as decided` and so on), and marks in the cell an answer that differs with the other answer in
braces. It also holds each answer to the test of the report's section 1.1: an answer that gives
more than the file's own records give is marked in the cell and counted. `kinds` reads the same
file with a switch off and on and prints no such tally.

**Seal again after a stop that a copy caused** (`bridge.seal_copies`): a summary entry over the
file's own records, as any write of the plugin's own writes, with an item of kind `copy` for each
counted copy, holding the identifier the copy's content gives. A copy named so in a summary entry
in force is not held to the completeness check, and later summary entries carry the items on.
Nothing is written from the copy.

### The command

From the branch root, in Windows PowerShell 5.1, for each of `nothing`, `profile`, `verb`, `cap`
and `cap-items`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Reading cap -Parallel 8
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --reading-cap
```

A run of every part of a reading empties its own output and databases (`work\x<letter>h-*` and
`work\x<letter>e-*`, the letter the first of the reading's name, `i` for `cap-items`) and nothing of
the other configurations'. Both runs here were made under an ordinary user token, eight parts at
once, the five readings in turn.

**How long**, in seconds; each cell is run 1, held and exempt, then run 2, held and exempt:

| Part, held and exempt | `nothing` | `profile` | `verb` | `cap` | `cap-items` |
| --- | --- | --- | --- | --- | --- |
| `b-merges` | 136 and 131; 182 and 181 | 146 and 140; 187 and 181 | 176 and 176; 181 and 181 | 196 and 196; 182 and 181 | 181 and 176; 191 and 176 |
| `b-clear` | 101 and 90; 126 and 121 | 101 and 100; 131 and 120 | 116 and 120; 126 and 116 | 126 and 125; 126 and 121 | 121 and 116; 131 and 126 |
| `b-random` | 106 and 80; 131 and 105 | 106 and 90; 131 and 105 | 116 and 115; 126 and 110 | 131 and 125; 126 and 105 | 126 and 105; 136 and 100 |
| `b-deletions` | 65 and 65; 81 and 85 | 60 and 65; 91 and 85 | 76 and 75; 86 and 80 | 86 and 80; 81 and 80 | 81 and 80; 90 and 85 |
| `copies` | 65 and 50; 85 and 70 | 60 and 55; 91 and 70 | 75 and 75; 86 and 70 | 91 and 85; 86 and 70 | 85 and 70; 90 and 70 |
| `b-repair` | 25 and 15; 30 and 30 | 15 and 25; 30 and 30 | 25 and 30; 30 and 30 | 35 and 30; 30 and 30 | 30 and 25; 35 and 30 |
| `b-dupes` | 10 and 10; 10 and 15 | 10 and 10; 10 and 10 | 10 and 10; 15 and 10 | 15 and 10; 10 and 10 | 10 and 10; 20 and 10 |
| `b-dedupe` | 5 and 5; 6 and 5 | 5 and 5; 6 and 5 | 5 and 5; 10 and 5 | 5 and 5; 6 and 5 | 5 and 5; 5 and 5 |
| `kinds` | 5 and 5; 10 and 10 | 5 and 10; 10 and 10 | 5 and 10; 10 and 10 | 10 and 10; 10 and 10 | 10 and 10; 15 and 10 |

**How the two runs were made.** Each reading ran once with every part before the record: the first
form of `kinds` paired a client after the store was started again under a new key, and the copied
model writes such an admission under the first key's name, so the cell was changed to read the
rules and the names instead; the row "then the rule of N set again" after Seal again was added to
`copies`; and the control of `copies` was changed to leave a copy of a summary entry out, so that it
writes the same records under every reading. Then run 1 of every reading, kept as the record, and
run 2 of every reading. After run 2 the description of H22 that `b-merges` prints and some comments
were reworded, and `b-merges` of every reading, both variants, ran twice more, the five readings
at once: the first of those runs is the record of `b-merges`, and the second printed it line for
line. The times of `b-merges` in the table are those of the two whole runs.

**Compared.** Every output of run 2 is the same as its record line for line, with the masks of
"Running it": 18 files in each of the five records.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `b-merges`, the 396 cells, exempt: as decided, narrower, nothing served, wider | `nothing` 379, 5, 3, 9; `profile` 382, 6, 3, 5; `verb` 383, 7, 3, 3; `cap` and `cap-items` 386, 7, 3, 0 | the same | yes |
| the same cells as the store as decided reads the same files | 374, 5, 1, 16 | the same | yes |
| `b-merges`, held: as decided, narrower, the whole database refusing, wider | `nothing` 373, 5, 11, 7; `profile` 373, 5, 15, 3; `verb` 373, 5, 17, 1; `cap` and `cap-items` 373, 4, 19, 0 | the same | yes |
| the ten wider cells, exempt: closed as decided and as nothing served; still wider | `nothing` 5 and 2; H7, H8, H9. `profile` and `verb` 7 and 2; H8. `cap` and `cap-items` 8 and 2; none | the same | yes |
| the ten wider cells, held | each a refusal of the whole database, but H9 under `nothing`, wider; H4 refuses in every reading | the same | yes |
| H14 to H21, exempt: wider | `nothing` H14, H16, H17, H19, H20, H21; `profile` H16, H17, H19, H21; `verb` H17, H21; `cap` and `cap-items` none | the same | yes |
| cells other than Create new IDs into copy 1 that differ from the store as decided | none, in every reading and variant | the same | yes |
| `b-deletions`, `b-clear`, `b-repair`, `b-dedupe`, `b-dupes`: answers of the same class as the store as decided | 300 of 300, 252 of 252, 96 of 96, 4 of 4, 15 of 15, in every reading and variant | the same | yes |
| `b-random`, exempt; held | 1,436 steps, 0 failures of `safety`, 720 of 721 answers of the same class, 1 wider file as decided; 1,435 steps, 719 of 721, 1 as decided, 1 refusing, 1 gate act refused; the same in every reading | the same | yes |
| `copies`: the rows of copies that are not an import | 131, each as in the store as decided, in every reading and variant | the same | yes |
| `copies`: a limit that outlasts its replacement | as the report's section 4.2 | the same | yes |
| `copies`: combined with the decided rewrite, nothing written from a copy | 16 histories in every reading and variant: the authority's entry written again, the key held, the entry present; as decided after 32 of 32 Synchronizes; wider after the copies are deleted by hand in 16 | the same | yes |
| `copies`: the control | H13 holds no store key under every reading and variant; under `cap`, the other 15 as decided or, H6, nothing served | the same | yes |
| `kinds`: a retirement that settles | not counted, nothing served; counted, wider (client c1 admitted) and more than the file's own records give | the same | yes |
| `kinds`: a summary entry's items, four records | not counted, wider in 4; counted, as decided in 4 (exempt), the whole database refusing in 4 (held); an older summary entry copied, as decided in 3 either way | the same | yes |
| `kinds`: a store key from a copy, after the store is started again | not taken: 1 key, the name pf, the root and N give nothing; taken: 2 keys, pf and pg, the root read, N read and write | the same | yes |
| the test of the report's section 1.1 | 21,399 answers over the five readings and two variants, none giving more than the file's own records | the same | yes |

### What was added for them

Each line added or changed carries "ADDED (copies extended)". With the new switches off, every part
prints what it printed before: after the addition, `-Copies` and `-Rewrite` were run again whole,
`-Decided` in the parts `b-history`, `b-merges`, `b-dedupe`, `b-dupes`, `b-repair`, `b-random`,
`b-growth`, `b-deletions` and `b-clear`, and the first record in the parts `design-full`,
`b-dedupe`, `b-dupes`, `b-repair`, `b-merges`, `b-growth`, `store-scenarios` and `store-section6`;
each came out the same as its record line for line, with the masks of "Running it".

- `crdt_model.py`: `PP`, rules per profile on; `PROFS`; a document `('pp', C, P)`, the letters it
  grants and the letters it passes on from above, each letter a profile, a dot and a word; `lift`
  and `gen`, a document of the two older forms as naming every profile alike; `meet`, `limit` and
  `app` letter by letter where a document of the new form takes part, and as before otherwise;
  `doc_of`, a rule act's document; `words_text`, the words as text; with `PP`, `fold` gives every
  word as a profile's letter; the act `keep`, which no alphabet of the model holds: where several
  authorities stand and none is served, a retirement for each but the first created; `write_head`
  carries the acknowledged copies of earlier summary entries into the next, and adds nothing where
  there is none.
- `store.py`: `doc` and `undoc` for `pp:` documents; a summary entry's items of kind `copy`.
- `read.py`: `read(dump, history, copies, ext)`: with `ext`, the wider reading (`rules`, `settle`,
  `heads`, `key`, `among`); `limit_of`, what a copied document may take away at each level;
  `wider_than`, the test of an answer against the file's own records; the answer also names the
  objects a copy limits (`limited`) and the copies that pass and are not replaced (`live_copies`);
  with `PP`, every document read in the form with profiles. With `ext` unset it reads as before.
- `bridge.py`: `extended(level, variant)`; `rd` passes `ext`; `outcome` reads the same file as the
  store as decided does and counts the pair in `TRANS` and the failures of the test in `MONO`;
  `judge`, the oracle's verdict taken out of `outcome` unchanged; `cat` and `moved`; a gate act
  refused where the reading serves no authority and the act needs an authority; a version the gate
  writes names the copies read, with a deletion record for each identifier named; `seal_copies`;
  `recanon` leaves a copy of a summary entry out.
- `cases.py`: `--reading LEVEL VARIANT`; `PROFILE_HISTORIES` and `PP_SPECS`; the tallies at the
  end of a part. `readings.py`: `--reading LEVEL VARIANT`; `replaced`, `combine_never` and the part
  `kinds`; the kind with a rule per profile and the row of the gate's own write in `copies`.
- `run.ps1`: `-Reading`. `compare.py`: `--reading-<name>`.

### Defects and limits of these configurations

Reported, not fixed.

- **The model holds no reading of copies**, as under `-Copies`: its read differs from `read.py`'s
  wherever a copy counts, and those differences are listed in the parts of the store as decided.
- **Two profiles and two words.** The switches of a rule (attachments, protected values) are not
  written; in this form each would be a further word of its profile. A document's `default` is not
  written either: a sealed document reads here as naming every other profile with nothing.
- **The oracle compares what a node gives each profile**, not what a client reaches through the
  profiles it holds: a word more for a profile no admitted client holds is counted as wider.
- **A document of the older forms names every profile alike** where rules per profile are on, so
  the 13 histories of the store as decided keep their meaning, and their cells print as before.
- **The copied model writes an admission under the first key's name** (`El('rec:...')` takes the
  default key), also after Create has started a new key. Found while building `kinds`; no part of
  the recorded configurations pairs after a new key, and `kinds` does not either.
- **`kinds` holds its third cell to the two listings it prints**: the oracle reads under every key
  of the lineage and holds two authorities there, so it serves nothing and the class of the answer
  is empty; the marked line and the listings are the measurement.
- **Held to the check, a gate act is refused where the reading stops the store**, so a file's
  history can differ from the history the store as decided would have written; the comparison with
  the store as decided is of the same file read both ways, not of two runs.
- **Seal again acknowledges every counted copy at once**, and stands for the plugin's own press; no
  window was run.
- **KeePass's copy and paste** is stood in for by Duplicate Entry and a move, and **KeePassLib
  stands for KeePass's window** in every edit, as in the configurations before.

### Not run in these configurations

- Candidate d of the report on its own (a removal counted only where the removed rule granted more
  than the nearest rule above it): `cap` applies it word by word.
- A copy of an authority counted as standing without being served, which could only take away: the
  retirement beside it does that in every cell here.
- The readings in the model's searches; KeePassXC releases other than 2.8.0 outside `b-merges`; the
  slow derivation; a merge under KeePassDX's reading.
- A copy under a store key the file does not hold, which every reading passes over.
- Create at a file where a copied retirement leaves nothing served; two KeePass instances.
- Everything the store as decided and the two readings do not run.

## Rules per profile

Added on 2026-11-17 for `papers\ground\measured-rules-per-profile.md`. Every configuration before
this section stored an access rule as a version of a node's whole rule, for every profile at once,
and the configurations of "The wider readings of a copy" wrote such a version so that it names each
profile with its words. The project has since decided that a rule is a record for each node and
profile, a **row** of the grant editor, with versions of its own, and that the node's switch "Refuse
any profile not named here" is a record of its own. `run.ps1 -Rows` runs the store as decided in
that form. A row's version holds the letters it grants, no letter (an **empty rule**, which grants
nothing from there down), or the rule taken off (the profile is then not named at the node). With
the switch refusing, a removed row is written as an empty rule (the latest decision); the parts that
name `off` write the rule taken off instead, as the control. Its record is `recorded-rows\`.

### What it runs

`run.ps1 -Rows` runs these parts, each into `out\rows\<part>.txt`, held to `recorded-rows\` by
`compare.py --rows`; after them `compare.py --rows-against-decided` writes
`against-decided.txt`, every cell of `b-deletions`, `b-clear` and `b-history` against the same
cell of `recorded-decided\`.

| Part | What it is |
| --- | --- |
| `store-search` | the model's search of the store as decided (`store_runs.py search`, the same alphabet and seeds), a rule act writing a version for each row it changes |
| `store-search-acts`, `store-search-noauth` | the same with the acts on a single row and on the switch in the alphabet; and that alphabet with no hand deletion or edit of the authority's entry |
| `beyond`, `beyond-again`, `beyond-off`, `beyond-whole` | 200,000 random histories in the model over two copies, acts on rules and merges only: how often a merge gives at a node a letter that neither copy gave just before it. The removed row written as decided, in the form `again` (below), taken off, and the whole-rule form. Its header names the whole-rule form "written as None" |
| `b-merges` | the 13 histories of the store as decided and the eight of `PROFILE_HISTORIES` with H22, every merge both ways; each cell also played in the whole-rule form, and a class that differs printed in braces: 396 cells, and the 90 cells of a user node |
| `b-deletions`, `b-clear`, `b-history` | the parts of the store as decided, per profile; `b-history` adds the switch as an eleventh kind of record |
| `b-random` | `random 300 7`, the histories of the store as decided's record, each also played in the whole-rule form; at each merge, the merged file held to both copies just before it |
| `b-random-rows`, `b-random-rows-off` | `random-rows 300 11`: histories of acts on a single row and on the switch at the root and at N, with Synchronize, Overwrite if newer and KeePassXC 2.8.0, each merge held to both copies; the removal written as decided and taken off |
| `apart` | item 2 of the report: seven cases of two copies that change different profiles at a node, every merge both ways, in both forms |
| `same` | item 3: the same profile's row changed apart in two and three copies, and the removal of a single version in three forms, with every merge both ways |
| `states` | item 4: an empty rule, a rule taken off and a row never made, through every merge and six operations by hand |
| `switch` | item 5: nine sequences about the switch, sequence S34 of `STORE.md` among them, under three ways of writing a removed row; each merge held to both copies just before it |
| `dels` | item 6: what a deletion record of a row's version tells, in the identifier's form run and in the form `split`, and what the reading computes to tell it |
| `growth` | item 7: the store's strings for the same rules in both forms, from `store.py`'s format, at 20, 200 and 2,000 nodes with one, four and ten rows |

**How a row is written.** The object of a row's version is `rule:<node>/<profile>`, the switch's
`switch:<node>`. A row's text is `row:<letters>`, `row:` for an empty rule, `off:` for the rule
taken off; the switch's is `sw:deny` or `sw:inherit`. A rule act names a table of rows (the
whole-rule acts of the earlier configurations read as a table that names every profile alike, and
`inherit` as every row taken off) and writes a version for each row whose standing value it
changes, naming every version of that row it read; a row the act leaves as it reads writes
nothing. Removing a row whose switch the gate reads as refusing writes an empty rule; where the row
is an empty rule already, nothing is written (`empty`, the form run). `again` writes the rule taken
off in that last case, and `off` takes the rule off always. Seal again writes, for a row whose
version was lost, an empty rule, and for a switch, `sw:deny`.

**How a node's rule is read.** For each profile the standing versions of its row are put together:
the letters all granting versions hold; with a version that takes the rule off beside them, what
arrives from above cut to those letters where the switch inherits and nothing where it refuses; a
profile with no row, or with every version taking the rule off, gets what arrives where the switch
inherits and nothing where it refuses; a row a deletion record shows lost gives nothing. Versions
of the switch that differ refuse.

**Removing a single version of a row that stands in several** (`drop` in the model). `again`, the
form proposed: each version left is written again, naming the removed version and itself. `void`:
a single version that names the removed version and holds nothing of its own. `limit`: a single
version that names the removed version and holds what the versions left allow together.

**The identifier of a row's version.** `pair`, the form run: the subject hash over
`rule:<node>/<profile>`, so that the reading tries each node of the tree with each profile name the
file knows. `split`, run in `dels` only: the subject hash over `rule:<node>`, and the two bytes of
the height, 0 for every version until now, a keyed tag of the profile's name.

### The command

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Rows -Parallel 8
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --rows
```

A run of every part empties `out\rows\` and the databases `work\pe-*` and `work\po-*` and nothing of
the other configurations'. Both runs here were made under an ordinary user token.

**How long**, in seconds:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `store-search` | 747 | 747 |
| `store-search-acts` | 752 | 742 |
| `store-search-noauth` | 586 | 667 |
| `beyond` | 115 | 130 |
| `beyond-again` | 115 | 130 |
| `beyond-off` | 115 | 130 |
| `beyond-whole` | 95 | 105 |
| `b-merges` | 281 | 211 |
| `b-random` | 236 | 221 |
| `b-clear` | 116 | 100 |
| `b-history` | 120 | 105 |
| `b-random-rows` | 126 | 115 |
| `b-random-rows-off` | 130 | 115 |
| `b-deletions` | 65 | 65 |
| `apart` | 85 | 80 |
| `same` | 156 | 150 |
| `switch` | 155 | 135 |
| `states` | 40 | 35 |
| `dels` | 20 | 20 |
| `growth` | 5 | 5 |

**How the two runs were made.** Run 1 was `run.ps1 -Rows -Parallel 8` with every part but the four
parts `beyond` and `store-search-noauth`, which were added while it ran and ran next with `-Parts`,
the four first. During run 1, local names in the code were renamed for the spelling check; the parts
that had started printed what they would have printed, since a name changes no output, and the part
`states` started after the rename and prints its label `unrecorded` from it. Run 2 was
`run.ps1 -Rows -Parallel 8` with every part.

**Compared.** Every output of run 2 is the same as `recorded-rows\` line for line, with the masks of
"Running it": 21 files.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `b-merges`, 396 cells: as decided, narrower, refusing, wider; cells whose class differs from the whole-rule form | 373, 6, 1, 16; 1, H20 Create new IDs into 0 | the same | yes |
| `b-merges`: N under its own rule in every cell of a user node | gives nothing, 90 cells | the same | yes |
| `against-decided.txt`: cells of `b-deletions`, `b-clear`, `b-history` of the same class as `recorded-decided\` | 216 of 216, 252 of 252, 600 of 600; the switch in `b-history` 42 as decided and 18 refusing | the same | yes |
| `b-random`: files of the same class in both forms; merges beyond both copies, whole-rule and per profile | 718 of 724; 1 and 1 | the same | yes |
| `b-random-rows`, `-off`: steps; files at the end; merges beyond both copies | 1,724 and 1,725; WIDER 234, as decided 383, narrower 95 in each; 0 and 0 | the same | yes |
| `store-search`: `safety` per cell A to K, two files; three files | 0, 3, 3, 1, 2, 5, 3, 2, 2, 2, 2, 2; 0, 2, 2, 0, 0, 0, 0, 1, 1, 1, 0, 2 | the same | yes |
| `store-search-acts`; `store-search-noauth`: `safety`, two files; three files | 0, 1, 0, 4, 1, 0, 1, 3, 1, 0, 3, 3; 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0. B 1, C 2, E 1; C 1 | the same | yes |
| `store-search`, `-acts`: exhaustive histories; failures | 11,110, 47,988, 40,494, 27,930; none | the same | yes |
| `beyond`, `-again`, `-off`, `-whole`: histories with a merge beyond both copies, of 200,000 | 67, 68, 69, 298 | the same | yes |
| `apart`: per profile; whole-rule | 112 as decided with nothing to settle, imports 6 wider and 8 narrower; 16 as decided and 80 narrower with two versions, imports 5 wider and 7 narrower | the same | yes |
| `same`: cells beyond both copies | 16, all D3 in the form `void` | the same | yes |
| `states`: the state read after each operation | as the report's section 6 | the same | yes |
| `switch`: cells beyond both copies | W3 `off` 16, W6 `again` 16, W8 16 in each way, W9 16 | the same | yes |
| `dels`: the same lines in both forms; keyed hashes at 20, 200, 2,000 nodes, `pair` and `split` | 4 of 4; 228, 1,308, 12,108 and 88, 448, 4,048 | the same | yes |
| `dels`: the read, ms | 1.1, 8.4, 48.1 and 0.6, 2.7, 23.0 | 1.8, 3.7, 40.0 and 0.6, 1.9, 21.5 | differs, a time |
| `growth`: every size | as the report's section 9 | the same | yes |

### What was added for it

Each line added or changed carries "ADDED (rules per profile)". With the new switches off, every
part prints what it printed before: after the addition, `-Decided` was run again in the parts
`b-merges`, `b-deletions`, `b-history`, `b-random` and `b-clear`, `-Reading cap` in `b-merges`,
`b-random-exempt` and `kinds`, and the first record in `b-merges`, `b-dedupe` and
`store-scenarios`; each came out the same as its record line for line, with the masks of "Running
it".

- `crdt_model.py`: `ROWS` and `ROW_ACTS`; a row's value `('rows', letters, off)` and the switch's
  `('sws', values)`, met in `meet`; `table_of`, `rows_node`, `switch_refuses`, `refusal_of`,
  `obj_match`, `row_text`; `World.rows_act`, the acts `row`, `switch` and `drop`; the root's rule of
  the start as a row for each profile; `pick`, `copy`, `plant` and `plantk` by row; with `ROW_ACTS`
  the alphabet holds the acts on a row and on the switch.
- `store.py`: the texts of a row's and a switch's version; the objects `rule:<node>/<profile>` and
  `switch:<node>`; `ID_FORM` and `profile_tag`, the form `split`.
- `read.py`: `ROWS` and `SHOW`; a node's rule from its rows and its switch; a deletion record of a
  row's version or a switch's told by the names the file knows, or in the form `split` by the
  profile's tag; with either switch the answer names each row's standing versions (`standing`),
  the rows lost (`lost`), and the count of keyed hashes computed (`subject_hashes`).
- `bridge.py`: `rows(variant)`, the configuration, and `use(form)`, a part's history played in the
  whole-rule form beside it.
- `cases.py`: `--rows VARIANT`; `merges` with the profile histories and each cell in both forms;
  `random` in both forms, and each merge held to both copies; the part `random-rows`; the switch as
  a kind of `history`.
- `rows.py`: new, the parts `apart`, `same`, `states`, `switch`, `dels` and `growth`.
- `store_runs.py`: `--rows VARIANT [acts]` for `search` and `search-noauth`, and the part `beyond`.
- `run.ps1`: `-Rows`. `compare.py`: `--rows`, and `--rows-against-decided`.

### Defects and limits of this configuration

Reported, not fixed.

- **Two profiles and two letters**, `pf` and `pg`, read and write, as in the configurations before.
  The switches of a rule (attachments, protected values) are not written; a profile name that is
  not on the list can have a row here, since the whole-rule acts name both profiles.
- **The oracle is every decision any gate made, read in the same form.** It answers for the form's
  own combination and so does not tell a merge that adds up rows to more than either copy gave:
  `switch`, `same`, `beyond` and the two random parts hold each merge to both copies just before it
  instead. A cell "into" a copy by Create new IDs is a copy that stays stale, since the store reads
  no copy of its own records.
- **The same random history differs between the forms** where a rule act changes no row, which
  writes nothing per profile and a version of the node's rule in the whole-rule form, and where a
  hand deletion picks a single row's version (the newest row by identifier, `pg`'s at N) rather
  than the node's rule. `b-history` names its rule version the same way.
- **The cost of a deletion record is timed in Python**, over a dump with groups added as nodes of
  the tree and a deletion record of a switch for each, not in the plugin's C#.
- **`growth` writes no file**: its sizes are of the strings `store.py` would write, the whole-rule
  document a stand-in of the same length.
- **The repair after a Synchronize does not read rows** (`World.repair`); it is off in every part
  here, as in the store as decided.
- **KeePassLib stands for KeePass's window** in every edit, as in the configurations before.

### Not run in this configuration

- The grant editor, a window, a phone, a second KeePass with the plugin.
- A row of a name off the list whose records are all gone; more than two profiles in a file; a
  rule on a group between the root and N.
- The forms `void` and `limit` and the form `split` beyond the parts that name them; the slow key
  derivation; KeePassXC releases other than 2.8.0 outside `b-merges`, `apart`, `same`, `states`
  and `switch`.
- Everything the store as decided does not run.

## The forms rules per profile left owed

Added on 2026-11-20 for the report `measured-rules-owed-forms.md`. The run of rules per profile
left owed, in `STORE.md` §644 item 24: more than two profiles and two letters, and the switches of
a rule; a rule on a group between the root and the node; a row of a name off the list whose records
and items are gone; two profiles whose tags meet in the form `split`; and how the `after` lists
grow. `run.ps1 -Owed` runs them on rules per profile with a removed row written as decided. Its
record is `recorded-owed\`.

### What it runs

| Part | What it is |
| --- | --- |
| `combine` | `store_runs.py --owed combine 200000`: every pair of versions of a row (each set of the eight letters, and the rule taken off), every set of letters arriving from above, the switch refusing and inheriting, held to what both versions allow by capabilities; 200,000 random triples; and how many triples of letter sets a check by letters would pass while a capability is in neither copy |
| `beyond-2-rw`, `beyond-4-rw`, `beyond-2-mask`, `beyond-4-mask`, `beyond-4-mask-3` | `store_runs.py --owed P L V beyond 200000 34`: `beyond` of rules per profile with two or four profiles, the letters read and write or the full mask, at the root and N or at the root, a group and N; each merge held to both copies by letters and by capabilities; each history that gives more shrunk to its shortest form and named by the nodes its acts touch. `beyond-2-rw` draws exactly what `beyond` drew |
| `merges4` | `owed.py merges4`: eight histories with four profiles and the full mask, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases, each merged file held to the oracle and to both copies just before the merge |
| `random4` | `owed.py random4 300 13`: random histories of acts on rows and switches with four profiles and the full mask, at the root and N, with Synchronize, Overwrite if newer and KeePassXC 2.8.0 |
| `levels` | `owed.py levels`: ten sequences at the root, the group General (the model's `G`) and N under it, every merge both ways |
| `random3` | `owed.py random3 300 17`: `random4` at the root, the group and N |
| `offlist` | `owed.py offlist`: a row of a name off the list, its records and summary items lost in four ways, Seal again, the name added again in two forms, a further write, and each merge with a copy that holds the row |
| `tags` | `owed.py tags`: two names whose keyed tags meet, found by search, in the form `split` under three readings, and a name whose tag is 0000 |
| `afters` | `owed.py afters`: a row edited 400 times in a copy, 200 times alternately in two copies with Synchronize between, and 200 times apart in two copies with Synchronize both ways; what a version, its summary item and the summary entry hold, at checkpoints; and the counts at which `store.py`'s strings pass the ceilings of `STORE.md` |

**How a row is written with the full mask.** As in "Rules per profile", with any of the eight
letters of `CRUSDLAP` in `row:`, in alphabetical order. A rule act names a table, `pf=crusdlap,pg=rl`.
**How a merge is held to both copies.** By letters, as before, and by capabilities
(`crdt_model.caps`): each verb alone, each verb that reads or writes with each switch of the same
rule, and strip and delete with both switches; a switch without a verb grants nothing. A merge that
gives a capability neither copy gave while each of its letters came from a copy is counted as
"capabilities only".

### The command

From the branch root, in Windows PowerShell 5.1, under an ordinary user token:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -Owed -Parallel 4
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --owed
```

A run of every part empties `out\owed\` and the databases `work\qo-*` and nothing of the other
configurations'. Started through a PowerShell 7 session, Windows PowerShell inherits PowerShell 7's
module path and `tools\KeePass.ps1` fails to load `Get-AuthenticodeSignature`; the runs here removed
`PSModulePath` before starting `run.ps1`, as `kp.py` does for the host.

**How long**, in seconds, four parts at once, on a machine another measurement was sharing:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `beyond-4-mask-3` | 338 | 331 |
| `beyond-4-mask` | 252 | 251 |
| `beyond-2-mask` | 227 | 211 |
| `beyond-4-rw` | 197 | 211 |
| `beyond-2-rw` | 166 | 171 |
| `afters` | 162 | 226 |
| `levels` | 171 | 171 |
| `merges4` | 121 | 131 |
| `offlist` | 121 | 90 |
| `tags` | 101 | 65 |
| `combine` | 30 | 30 |
| `random4`, runs 1 to 4 | 368, 286, 150, 105 | |
| `random3`, runs 1 to 4 | 312, 276, 150, 105 | |

**How the runs were made.** Run 1 was `run.ps1 -Owed -Parallel 4` with every part; its outputs
were copied aside and are `recorded-owed\`, but `random4` and `random3`. Run 2 was the same
command. Then `random4` and `random3` were given the count of merges they hold to both copies,
which run 1 did not print, and ran twice more with `-Parts random4,random3 -Parallel 2`: run 3 is
their record and run 4 printed it line for line; every other line of theirs is the same in all
four runs. Before run 1, with the new switches off, `-Rows` was run again in `beyond`,
`beyond-whole`, `beyond-again`, `b-merges`, `switch`, `same`, `b-random-rows`, `apart`, `states`,
`dels` and `growth`, `-Decided` in `b-merges` and `b-random`, and the first record in
`store-scenarios` and `store-section6`; each printed its record line for line, with the masks of
"Running it": 15 files.

**Compared.** Every output of run 2, and of run 4 for the two random parts, is the same as
`recorded-owed\` line for line, with the masks of "Running it": 13 files.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `combine`: pairs held to what both versions allow; more, less; random triples more, less | 16,974,336; 0, 0; 200,000: 0, 0 | the same | yes |
| `combine`: triples of letter sets within both copies' letters and beyond their capabilities | 1,365,238 of 16,777,216 | the same | yes |
| `beyond-2-rw`, `-4-rw`, `-2-mask`, `-4-mask`, `-4-mask-3`: histories beyond both copies; of them by capabilities only | 67, 33, 129, 44, 44; 0, 0, 8, 1, 1 | the same | yes |
| the same: histories at a single node | 0 in every configuration | the same | yes |
| `merges4`, 144 cells: as decided against the oracle but imports; against both copies, within, by letters, by capabilities only | 128 of 128; 112 (16 imports), 16 (O7), 16 (O6) | the same | yes |
| `random4`: steps; merges held; beyond both copies; files wider, as decided, narrower | 1,967; 781; 0; 328, 227, 167 | the same | yes |
| `levels`, 180 cells: as decided but imports; within, by letters | 160 of 160; 132 (20 imports), 48 (L8, L9, L10) | the same | yes |
| `random3`: steps; merges held; beyond both copies; files wider, as decided, narrower | 1,973; 866; 0; 312, 248, 150 | the same | yes |
| `offlist`: reads wider than the last decision | 26 of 300, each a loss with no deletion record of a row whose profile the root names | the same | yes |
| `offlist`: the name kept off the list by its first addition in the form `text` | 6 of 20, back at the second addition | the same | yes |
| `tags`: the names whose tags meet; the name whose tag is 0000; the name the set keeps over 40 hash seeds | `q00331`, `q00534`, `c699`; `q184933`; 20 and 20 | the same | yes |
| `tags`: reads wider, `first`, `last`, `each` | G3 4 of 6; G2 4 of 6; 0 of 22, and G6 1 of 2 | the same | yes |
| `afters`: at 400 edits in a copy, identifiers in `after`, KPG.Data, item, deletion records | 399, 13,295, 13,301, 799 | the same | yes |
| `afters`: identifiers at which a version and its item pass 262,144 and 1,048,576 characters | 7,940 and 7,940; 31,772 and 31,771 | the same | yes |
| `afters`: the file's bytes and the read's milliseconds | 3,047 to 19,575 bytes; 0.4 to 1.5 ms | differ by some bytes, and by time | masked |

**`tags` was recorded anew on 2026-11-20**, when the tag of a profile's name took the label
`name` (`store.py`, `PROFILE_TAG_LABEL`): it was keyed over `profile`, which is also the label of
the check of a record of kind `profile`. The labels keyed under a store key are now the name of
each kind for the check, `tag` for a kind's tag, `subject` for a subject's hash, `key` for a key's
name and `name` for a profile's tag. `tags` is the only record that depends on the label: it
found `q00067`, `q00401` and `6b11`, `q00238`, and 17 and 23 before, and its tallies are
unchanged. It was run twice with `-Owed -Parts tags` and printed the same lines but its time.
`dels` of rules per profile, the only other part that reads a tag, was run twice with `-Rows
-Parts dels` and printed its record with the masks of "Running it".

### What was added for it

Each line added or changed carries "ADDED (owed forms)". With the new switches off, every part
printed what it printed before, in the 15 files named under "How the runs were made".

- `crdt_model.py`: `configure`, the profiles, the letters and three levels (`PARENT`, a node `G`
  under the root and N under it, what arrives at a node being what its parent gives); `caps` and
  `caps_text`, a profile's letters as capabilities; `ROOT_ROWS`, the profiles the root names at
  the start; `KNOWN`, a deletion record of a removal or of a row's version told only for a name
  the file knows, as `read.py` tells it, and `ADD_FORM`, how an addition names the removals of its
  name under it; `TAG_OF` and `TAG_READ`, the form `split`'s tags and the readings `first`, `last`
  and `each`.
- `store_runs.py`: `--owed combine` and `--owed PROFILES LETTERS LEVELS beyond COUNT SEED`
  (`part_combine`, `part_beyond_owed`, `node_value`, `mask_text`).
- `store.py`: `LETTERS`, the letters a row's version may hold.
- `read.py`: `LETTERS`; in the form `split` the names of a tag kept sorted in a list, with
  `TAG_READ` `first`, `last` or `each`, the last telling a deletion record of every name of its tag
  only where no version present or restated carries its identifier; the answer names the tags that
  meet (`tag_meets`), the names known for a row (`row_names`) and from the list (`known_names`).
- `bridge.py`: `owed`, the configuration; `NODES` and `JUDGE`, the nodes compared and judged, the
  group General as `G` with three levels.
- `owed.py`: new, the parts on files.
- `run.ps1`: `-Owed`. `compare.py`: `--owed`.

### Defects and limits of this configuration

Reported, not fixed.

- **The reading of `split` as it was run was not stable**: its dictionary from a tag to a name
  kept the name a set of strings yielded last, an order the process's hash seed decides. No earlier
  output depends on it, since no earlier run held two names whose tags meet. Here the names are
  sorted, and `first` and `last` stand for its two answers.
- **`read.py`'s form `split` reads a tag of 0000 as a version of a node's whole rule**, so the
  deletion record of a row of a name whose tag is 0000 tells of no row (`tags`, G6). The model
  tells it, so Seal again in G6 wrote what the model read.
- **The model's row act does not name a lost version**, so a row that refuses for a lost version
  stays refusing when it is set again; nothing here sets such a row again.
- **The oracle compares what a node gives each profile**, letters and not capabilities; each merge
  is held to both copies by capabilities as well, but a file is held to the oracle by letters.
- **`afters` writes no file past 400 edits**; the ceilings are computed from `store.py`'s strings.
- **The probe's texts**: letters in alphabetical order, the measured forms of every record (`v`,
  nonces derived from the elements), as in the configurations before.
- **KeePassLib stands for KeePass's window** in every edit, as in the configurations before.

### Not run in this configuration

- The model's searches with four profiles, the full mask or three levels; the forms `void` and
  `limit`, and the removal written `again` or `off`, with four profiles.
- KeePassXC releases other than 2.8.0 in `random4`, `random3`, `offlist` and `tags`; the slow key
  derivation.
- A row set again after it refuses; more than four profiles; the product's texts.
- Everything rules per profile does not run.

## The two forms of S34

Added on 2026-11-20 for the report `measured-rules-across-levels.md`. The design
`S34-DESIGN.md` (in the session's scratchpad, not in a repository) proposes two forms that close
rules changed apart at two levels (`STORE.md` §639, S34), and its section 8 specifies this run.
**Form B, by what arrived** (`seen`): a version that takes a rule off records the letters that
arrived for the profile, `off:deny` where the node's own switch refused, and a version of the
switch that inherits records them for each profile; the reading cuts what arrives to those
letters; an act above writes anew the versions below that let through everything that arrived
before it. **Form A, by identity** (`above`): the same versions name the versions the walk read
above the node and the node's own switch, and the reading refuses the profile where what stands
is not what was named. Both are switches beside the store as decided (`DEP`, none by default),
and every part plays the store as decided beside the form on the same steps. `run.ps1 -S34` runs
them; its record is `recorded-s34\`.

### What it runs

| Part | What it is |
| --- | --- |
| `beyond-2-rw-off-`, `beyond-4-rw-`, `beyond-2-mask-`, `beyond-4-mask-`, `beyond-4-mask-3-` and the form | `store_runs.py --dep FORM off P L V 200000 34`: the draws of `beyond` in each configuration of `recorded-owed\`, a removed row taken off for real; for the store as decided and for the form, the histories with a merge beyond both copies, by letters and by capabilities only; each history beyond both under the form shrunk, printed and named by what the walk from its node holds; the cost |
| `beyond-2-rw-again-`, `beyond-2-rw-empty-` and the form; `beyond-2-rw-again-none`, `beyond-2-rw-off-none` | the same, two profiles, read and write, a removed row written as `again` and as decided, under each form; and with no form, the controls of `recorded-rows\` (68 and 69) |
| `long-2-` and the form | the same with 8 to 14 steps, the search that draws H4 |
| `moves-3-` and the form | three levels, with N moved between G and the root in the alphabet |
| `combine` | `store_runs.py --dep combine 200000`: `combine` with a version that takes the rule off holding each recorded set, and `off:deny` |
| `subtree-` and the form | a write above 20, 200 and 2,000 nodes that let through |
| `search-acts-` and the form | `store_runs.py --depform FORM --rows off acts search`: the model's search with the acts on a row and on the switch, under the form |
| `hand-`, `cells4-`, `cells3-`, `switch-`, `moves-`, `afters-` and the form | `s34.py PART FORM` on files: the design's hand histories; O1 to O8 and L1 to L10, every merge both ways; W1 to W8 with a removed row written as decided, as `again` and taken off; the moves and H16; the `after` lists under renewals |

**What the cost counts.** For each history, a copy giving a profile less at a node than the
store as decided gives on the same steps, by capabilities, counted only where the store as decided
has no merge beyond both copies in that history, at any step and at the end; of them, a profile
given nothing where the store as decided gives it something; and the form giving more.

### The command

From the branch root, in Windows PowerShell 5.1, under an ordinary user token, with
`PSModulePath` removed when started from PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run.ps1 -S34 -Parallel 4
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare.py --s34
```

A run of every part empties `out\s34\` and the databases `work\qss-*` and `work\qsa-*` and nothing
of the other configurations'.

**How long**, in seconds, four parts at once, on a machine other measurements were sharing; each
cell is run 1 and run 2:

| Part | Form B (`seen`) | Form A (`above`) |
| --- | --- | --- |
| `beyond-4-mask-3` | 1,012; 972 | 992; 987 |
| `search-acts` | 857; 822 | 842; 837 |
| `beyond-4-mask` | 736; 722 | 752; 737 |
| `long-2` | 711; 686 | 702; 702 |
| `beyond-4-rw` | 596; 591 | 606; 596 |
| `moves-3` | 581; 566 | 576; 567 |
| `beyond-2-mask` | 536; 536 | 546; 536 |
| `beyond-2-rw-off`, `-again`, `-empty` | 441, 441, 436; 446, 446, 436 | 456, 476, 486; 456, 456, 456 |
| `subtree` | 115; 115 | 140; 135 |
| `switch`, `cells3`, `cells4` | 140, 95, 75; 186, 115, 90 | 185, 125, 85; 186, 135, 95 |
| `afters`, `hand`, `moves` | 25, 10, 5; 30, 10, 5 | 45, 10, 5; 30, 10, 5 |
| `beyond-2-rw-again-none`, `-off-none`, `combine` | 421, 421, 100; 406, 396, 95 | |

**How the runs were made.** A first run 1 was stopped after its first part: its listing of a
form's failures printed the first 40 by length, which the search with moves filled with a single
class, and the time of `subtree` was not where the comparison masks it. Both were changed, and run
1 and run 2 were then `run.ps1 -S34 -Parallel 4` with every part, run 1's outputs copied aside and
kept as `recorded-s34\`. Before them, with both forms off, the 28 outputs named under "What was
added for it" printed their records.

**Compared.** Every output of run 2 is the same as `recorded-s34\` line for line, with the masks of
"Running it": 37 files.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| the store as decided beside each form, beyond both copies: 2 profiles rw, 4 rw, 2 the mask, 4 the mask, 4 the mask at 3 levels | 67, 33, 129 (8 by capabilities only), 44 (1), 44 (1) | the same | yes |
| no form, the removal as `again` and taken off | 68, 69 | the same | yes |
| form B and form A, beyond both copies, in each of the 7 configurations of 4 to 10 steps | 0 in each | the same | yes |
| form B cost, less (at the end), nothing (at the end), more, in the order of the first row | 181 (161), 23 (21), 19; 108 (96), 7 (7), 9; 327 (288), 71 (61), 3; 129 (124), 28 (28), 0; 115 (107), 25 (23), 0 | the same | yes |
| form A cost, the same | 263 (241), 263 (241), 19; 128 (117), 128 (117), 9; 424 (377), 424 (377), 3; 143 (135), 142 (135), 0; 131 (123), 131 (122), 0 | the same | yes |
| form B and form A, the removal written as decided; as `again` | 177 (157) and 259 (237); 182 (162) and 264 (242) | the same | yes |
| `long-2`: the store as decided; form B (each of the shape H4); form A | 294; 5; 0 | the same | yes |
| `moves-3`: the store as decided; form B; form A | 134; 80 (79 with no version on the walk, 1 of the shape H4); 79, the same 79 | the same | yes |
| `combine`: pairs; more, less; the recorded sets cut | 67,502,592; 0, 0; 18,275,970 | the same | yes |
| `subtree`, 20, 200, 2,000 nodes: versions, deletion records | k + 1, k + 2 under each form | the same | yes |
| `search-acts` under each form | `recorded-rows\store-search-acts.txt` line for line but the header | the same | yes |
| `cells4`, `cells3`: beyond both copies, as decided; under each form | 32 (O6, O7) and 48 (L8, L9, L10); 0 | the same | yes |
| `cells4`, `cells3`: narrower than the store as decided, form B; form A | 32 and 48; 32 and 64 (L3 as well) | the same | yes |
| `switch`: beyond both copies under each form, each way of writing a removal; wider than the store as decided | 0; W6 taken off, 16 cells and its import cell | the same | yes |
| `afters`: identifiers in the version below after 400 edits above; after 200 edits apart | 401; 101 | the same | yes |

### What was added for it

Each line added or changed carries "ADDED (S34 forms)". With both forms off, every part printed
what it printed before: after the addition, the 15 parts named under "The forms rules per profile
left owed" ("How the runs were made") and the 13 parts of `recorded-owed\` were run again and each
printed its record line for line, with the masks of "Running it": 28 files.

- `crdt_model.py`: `DEP` and `MOVES`; `is_off` and `deps_of`; `meet` keeps the records of every
  version; `dep_cut` and `rows_node`'s walk; the fold's `parent_of`, `standing_ids` and
  `walk_above`, given to the acts under a form; `dep_act`, the acts of a form with the write
  above; the act `lift`; `nmove`, a move of N; `row_text` and `rec_text` print a record.
- `store_runs.py`: `--dep` (`part_dep`, the store as decided beside a form), `--dep combine`
  (`part_combine_dep`), `--dep subtree` (`part_subtree`) and `--depform`, the model's search
  under a form.
- `store.py`: `DEP`, the texts of both forms in `doc` and `undoc`, the encoder handing `doc` the
  identifiers on the file.
- `read.py`: `deps_of`; `meet` keeps the records; `standing_ids`, `walk_above` and `dep_cut` over
  the file's tree.
- `bridge.py`: `dep`, the form and the way a removed row is written; the file side of `nmove`,
  KeePassLib moving N between the groups General and the root.
- `s34.py`: new, the parts on files.
- `run.ps1`: `-S34`. `compare.py`: `--s34`.

### Defects and limits of this configuration

Reported, not fixed.

- **The probe keeps form A's identifiers in `doc`**, where the design puts them in a member
  `above`; the summary items restate `doc` whole, so the reading has the same identifiers.
- **A dialog is not modeled**: every act composes at its write, so the design's condition that a
  write above renews only versions the tab read at the press (H6), and the dialog's own state at
  an X (H8), are not run.
- **The tree is the root, G and N**, and the subtree of `subtree` is k nodes under the root in the
  model alone; no file holds more than three levels.
- **Seal again is no act above** here: it writes an empty rule or the switch refusing, and the
  design does not state whether that renews the nodes below.
- **The oracle compares by letters**; every merge is held to both copies by letters and by
  capabilities, and every cell on files to the store as decided by capabilities.
- **KeePassLib stands for KeePass's window** in every edit and every move, as in the
  configurations before.

### Not run in this configuration

- `lift` in the alphabet of the searches, three copies, and the longer search over three levels;
  the forms in `b-merges`, `apart`, `same`, `states`, `b-deletions`, `b-clear`, `b-history`,
  `b-random` and `b-random-rows`.
- Merges other than Synchronize in `afters` and `moves`; the slow key derivation.
- The tab, a second KeePass with the plugin, a phone.

## The parts cut from rules per profile

Added on 2026-11-20 for `papers\ground\measured-rules-cut-parts.md`. The run of "Rules per
profile" cut parts of the earlier runs to keep within its time, and `STORE.md` §644 item 24 holds
them as owed: `b-slow`, `b-dedupe`, `b-dupes`, `b-repair` and `b-growth` per profile; `store-ext`
and the replays; and the KeePassXC releases other than 2.8.0 in `b-history`, `b-deletions`,
`b-clear` and the random parts. It also left untraced the histories behind the four
configurations whose counts of `safety` differ from the store as decided's record, and every
failure of `safety` beyond the shortest that `search` prints. `run-cut.ps1` runs all of them, in
the configuration of `run.ps1 -Rows` and, where no record holds it, in the whole-rule form beside
it. Its record is `recorded-cut\`.

### What it runs

`run-cut.ps1 -Run N` runs these parts, each into `out\cut-N\<part>.txt`. `compare_cut.py check N`
holds the run to the earlier records it can be held to, `compare_cut.py analyze N` writes the
comparisons the report quotes into `out\cut-N\analysis.txt`, with the single histories it plays
in `out\cut-N\replays\`, and `compare_cut.py second` holds run 2 to `recorded-cut\`.

| Part | What it is |
| --- | --- |
| `b-deletions-<release>`, `b-clear-<release>`, `b-history-<release>` | `probe\cut.py --rows empty`: the part of `run.ps1 -Rows` with `keepassxc-cli` of that release in every merge of KeePassXC, for 2.4.2, 2.6.6, 2.7.12 and 2.8.0. With 2.8.0 each prints `recorded-rows\` line for line |
| the same names ending in `-whole` | `probe\cut.py --decided`: the same in the whole-rule form, for the three releases `recorded-decided\` does not hold |
| `b-random-rows-<release>`, `b-random-rows-off-<release>` | `probe\cut.py --rows empty` or `--rows off` `random-rows <release> 300 11`: the same 300 histories with that release in every merge of KeePassXC, then the class of each file at the end of each history |
| `b-random-draws` | `probe\cut.py draws 300 7`: the merges the 300 histories of `b-random` draw, by program; no file is made |
| `b-slow`, `b-dedupe`, `b-dupes`, `b-repair`, `b-growth` | `probe\cut.py --rows empty`: the parts of `cases.py` and `growth.py`, unchanged, per profile. `b-slow` plays each cell in the whole-rule form as well, as `b-merges` does |
| `store-ext`, `store-scenarios`, `store-section6` | `model\cut_runs.py rows`: the extended model, the design's 20 replays and the sequences of `EVALUATION.md` section 6, on the store as decided per profile |
| the same names ending in `-whole` | `model\cut_runs.py whole`: the same in the whole-rule form |
| `trace-<form>-<search>-<files>` | `model\search_trace.py`: the searches of `store_runs.py`, the same histories from the same seeds, every history in which a property is counted written to `<part>.jsonl` (not recorded), every history that fails `safety` printed with the history shrunk as `search` shrinks its shortest. `rows` with `search`, `acts` and `noauth` as `run.ps1 -Rows` runs them, `whole` with `search` and `noauth` as `run.ps1 -Decided` runs them, each for 2 and 3 files |

**`b-random` has no part here.** Its alphabet draws every merge of KeePassXC from the four
releases (`cases.alphabet`, `bridge.RELEASES`), in the record of the store as decided and in that
of rules per profile alike: of the 578 merges its 300 histories draw, 79 are by 2.4.2, 59 by 2.6.6,
74 by 2.7.12 and 74 by 2.8.0 (`b-random-draws`).

**The replays run in the column of the store as decided alone.** The first record's columns with
a decision undone hold the repair after a Synchronize, which reads no row (`World.repair`). A
replay's predicate reads a node's words as letters; per profile, `cut_runs.py` gives it the words
of a node that grants every profile the same letters as those letters, and the words of any other
node as they are. The view of section 6 prints words as `words_text` does, which in the
whole-rule form is the text the first record printed. In the whole-rule form both parts print
the first record's column with the repair undone (`check`).

**What `analyze` adds for item 2.** For each history that fails `safety` in a form alone, both
forms played step by step by `model\search_replay.py`, beside the oracle of `safety` (what was
delivered to that file) and the oracle of every decision any gate made; the first step at which
an act applies in a form alone; and, where that act is a rule act, both forms played again with
it taken out. For each history in which another property is counted in a form alone, the first
such step. For item 3, each failing history shrunk is named by what it deletes (`kind_of`), and
a failing history per profile that holds an act on a row, of a kind other than a store key lost,
is played again in the whole-rule form with each act on a row made the act on the node's whole
rule with the same letters.

### The command

From the branch root, in Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File spikes\storage\probes\store-as-a-whole\run-cut.ps1 -Run 1 -Parallel 4
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare_cut.py check 1
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare_cut.py analyze 1
```

and the same with `-Run 2`, `check 2` and `analyze 2`, then `compare_cut.py second`. A run writes
its outputs and its databases under names of its own (`out\cut-N\`, `work\pe-rN-*`,
`work\po-rN-*`, `work\d-rN-*` and `work\rN\`), so that two runs can go at once; `-Parts` runs
some parts, and replaces only their outputs. **Nothing may hold `out\cut-N\times.txt` open** while
a run writes it: a reader that keeps it open, as `tail -F` does, makes the runner's write fail,
and the runner waits a minute for it and then ends, leaving the parts it started to end on their
own. Both runs here were made under an ordinary user token, four parts at once, half of what the
runs before used, on a machine another measurement was sharing.

**How long**, in seconds, each part's own time; for a part with a release, the releases in the
order 2.4.2, 2.6.6, 2.7.12, 2.8.0. The parts of run 1 that ran first shared the machine with a
search of another worktree and took longer.

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `b-slow` | 1,524 | 1,798 |
| `trace-rows-search-2`, `-3` | 259, 358 | 278, 376 |
| `trace-rows-acts-2`, `-3` | 259, 371 | 277, 402 |
| `trace-rows-noauth-2`, `-3` | 288, 414 | 277, 412 |
| `trace-whole-search-2`, `-3` | 194, 270 | 198, 247 |
| `trace-whole-noauth-2`, `-3` | 198, 312 | 181, 243 |
| `b-deletions-<release>` | 216, 151, 144, 51 | 52, 61, 73, 48 |
| `b-deletions-<release>-whole` | 214, 149, 136 | 51, 58, 53 |
| `b-clear-<release>` | 259, 199, 151, 79 | 80, 88, 77, 78 |
| `b-clear-<release>-whole` | 270, 192, 91 | 79, 85, 72 |
| `b-history-<release>` | 289, 228, 101, 87 | 115, 119, 88, 88 |
| `b-history-<release>-whole` | 250, 227, 89 | 105, 112, 79 |
| `b-random-rows-<release>` | 321, 319, 103, 91 | 143, 171, 95, 93 |
| `b-random-rows-off-<release>` | 283, 306, 96, 115 | 138, 170, 94, 86 |
| `store-ext`, `store-ext-whole` | 232, 157 | 191, 126 |
| `store-scenarios`, `-whole`, `store-section6`, `-whole` | 1, 1, 6, 6 | 1, 1, 5, 3 |
| `b-repair`, `b-dupes`, `b-growth`, `b-dedupe`, `b-random-draws` | 13, 5, 6, 1, 5 | 14, 5, 5, 1, 5 |

**How the two runs were made.** Run 1 was started with every part and its runner ended after
starting six, when a reader of this session held `times.txt` open; the four parts still running,
`b-slow` among them, ran to their end on their own. The runner was given its retry, and the parts
not yet started ran with `-Parts` in two further starts. The first of them ended the same way after
starting four parts, which ran to their end, because the reader had outlived the first start; once
that reader was stopped, the second ran the other 41.
Every part of run 1 ran once and to its end, and `check 1` held each to its record. Run 2 was
`run-cut.ps1 -Run 2` with every part.

**Compared.** Every output of run 2 is the same as `recorded-cut\` line for line, with the masks of
"Running it": 52 files, `analysis.txt` among them; the 10 traces of run 2 are the same as run 1's
byte for byte, and so are the 70 files of the replays. Both runs were held to the earlier records
by `check`, and both held.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `check`: with KeePassXC 2.8.0, `b-deletions`, `b-clear`, `b-history` per profile against `recorded-rows\`; `b-random-rows`, `-off` the lines the record holds | 274, 316, 924 lines the same; 3 and 3 | the same | yes |
| `check`: `store-ext-whole` against `recorded-decided\`; the replays in the whole-rule form against the first record's column with the repair undone | 53 lines the same; 20 of 20 replays, 43 of 43 sequences | the same | yes |
| `check`: each trace against its search's record, counts and the shortest failing history | 10 traces, 12 configurations each, every shortest `safety` history the same | the same | yes |
| `b-deletions`, `b-clear`, `b-history`: cells whose class differs between the four releases; between the forms | 0 of 216, 252, 660 per profile and 216, 252, 600 whole-rule; 0 in every release | the same | yes |
| the same, cells merged by KeePassXC in every release | 72: 66, 4, 2; 84: 84; 220: 156, 2, 62 per profile and 200: 142, 2, 56 whole-rule (as decided, narrower, refuses) | the same | yes |
| `b-random-rows`, `-off`, every release: steps; files at the end; merges beyond both copies; histories differing between releases | 1,724 and 1,725; WIDER 234, as decided 383, narrower 95; 0; 0 of 300 | the same | yes |
| `b-random-draws`: merges by KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | 79, 59, 74, 74 of 578 | the same | yes |
| `b-slow`: the 24 cells of H3, H6, H13 per profile and whole-rule; cells as under the fast derivation | as decided 24 in both; 32 of 32 | the same | yes |
| `b-dedupe`, `b-dupes`, `b-repair`: outcomes of the same class as `recorded-decided\`; the others | 4 of 4, 15 of 15, 85 of 96; 11 WIDER to refusing (R1 6, R2 5) | the same | yes |
| `b-growth` after 1,000 writes: summary entry bytes; entries; deletion records | 103,745; 352; 2,527 | the same | yes |
| `store-ext`: lines that differ from the whole-rule form | the header alone | the same | yes |
| replays and sequences of section 6 whose answer differs between the forms | 2 of 20 (9, 10); 6 of 43 | the same | yes |
| the search: histories failing `safety` in a form alone; histories differing in another property | 4 (C, E, G per profile, F whole-rule); 14; each parting at a rule act, the control the same in 4 of 4 | the same | yes |
| failures of `safety`, per profile `search`, `acts`, `noauth`; whole-rule `search`, `noauth` | 36, 20, 5; 34, 3 | the same | yes |

### What was added for it

Every file is new; no file of the family was changed. Each part prints what the part it calls
prints, and the parts with a release of KeePassXC print, with 2.8.0, what `run.ps1 -Rows` printed
(`check`).

- `probe\cut.py`: the configuration (`--rows VARIANT` or `--decided`) and `--run TAG`, which puts
  the databases under `work\<prefix>r<TAG>-`; `deletions`, `clear` and `random-rows`, copies of
  the functions of `cases.py` with the release of KeePassXC a parameter, `random-rows` printing at
  its end the class of each file of each history; `history`, `cases.part_history` with its table
  of merges replaced; `draws`; and `slow`, `dedupe`, `dupes`, `repair` and `growth`, which call
  `cases.py` and `growth.py` unchanged.
- `model\cut_runs.py`: `store_runs.part_ext`, `part_scenarios` and `part_section6` in either form,
  on the store as decided, with the predicate and the view described above.
- `model\search_trace.py`: the loop of `crdt_model.search` drawing the same histories, keeping each
  history in which a property is counted and shrinking each that fails `safety`.
- `model\search_replay.py`: a single history step by step in either form.
- `run-cut.ps1`, `compare_cut.py`: the command, and the checks and comparisons.

### Defects and limits of this configuration

Reported, not fixed.

- **A hand deletion of "the newest rule version" takes a single row's version per profile** (the
  newest row by identifier, `pg`'s at N), where the whole-rule form takes the node's whole rule.
  So in `b-repair`, the replays and the sequences of section 6, a loss per profile is the loss of a
  single row, and pf's row stays: the store stops where the whole-rule form ends wider, or gives
  pg alone what the whole-rule form gives both. The loss of every row of a node was not run.
- **A rule act that changes no row writes nothing per profile and a version in the whole-rule
  form.** Under change 4 that version waits in memory for a save, and while it waits the model
  lets no other program change that copy, so the same drawn history plays differently from that
  act on: this is every history the two forms of the search count differently (item 2).
- **The kinds of item 3 are named by rule** from the history as `search` shrinks it: a store key
  lost where the authority's entry is deleted or edited and Create follows, a refusal deleted with
  a summary entry where the history deletes both. The shrunk history is not unique: `shrink` keeps
  the first history it reaches whose removal of any further step stops the failure.
- **The model holds the defects of the configurations before**: two profiles and two letters, no
  history of an entry, the oracle that reads under every store key of the lineage, and the repair
  that reads no row.
- **KeePassLib stands for KeePass's window** in every edit, as in the configurations before.

### Not run in this configuration

- The replays with a decision undone, per profile: those columns hold the repair.
- `store-ablations`, `store-repair-closes` and `design-full`, which the store as decided did not
  run either; the forms `void`, `limit` and `split` beyond the parts that name them, and more than
  300 random histories on files, which `measured-rules-per-profile.md` lists as cut beside the
  parts run here.
- The loss of every row of a node at once, by hand.
- Everything rules per profile does not run.

## Vectors for the product

Added on 2026-11-18. `vectors.py`, beside `run.ps1`, prints values the product's tests hold its
reading of the store to: the tags, subject hashes and key names of the kinds, summary entries at
several heights, a record of each kind the family never wrote (`withdrawn`, `kept`, `format`), and
small stores with what `probe\read.py` answers for each with its history rule on. It imports
`probe\store.py` and `probe\read.py`, calls their functions over inputs written out in the script,
and starts no program and writes no file, so it runs without KeePass:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\vectors.py
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\vectors.py --readings
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\vectors.py --records
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\vectors.py --meanings
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\vectors.py --writes
```

The first prints every vector, those of the last three modes among them. The second prints the
small stores in the shape of the product's `tests\Shared\StoreReadingVectors.json`, byte for byte,
with the three words of the read's status written as the names the product gives those states;
the script's `STATES` is that table. Each of the last three prints a file the product keeps under
`tests\Shared`, opening with an `about` that names what made it and what is invented, with its
keys sorted:

- **`--records`**, `StoreRecordVectors.json`: records in the forms `STORE.md` §614, §615 and §648
  fix, composed by `store.py`'s `dumps`, `mac` and `ident` as `rec` composes them. An authority
  with `ca`, `host`, `id`, `sniKey` and `storeKey`; a retirement with and without `storeKey`; an
  admission with the members of S4, with and without `from`; a revocation holding `client` alone;
  a `withdrawn` record with `from` and `renewed`, and with an empty `renewed`; two summary entries
  of height 1; and two of height 2, each naming both of height 1 in `after`. The first of height
  2 holds the items `format`, `retired`, `revoked` and `withdrawn`, the second `format` and an
  item `withdrawn` for each `withdrawn` record of the file. Each with its inputs, its content
  text, its check, its subject and number, its identifier and every string of its entry. An item
  `withdrawn` is in the form S29 settled on 2026-11-17 for §611, §648 and §658: it holds the
  identifier of the record it restates as `id`, and that record's `certificate`, `renewed` and
  `from` text for text, with no `from` where the record holds none. Added on 2026-11-19, in the
  forms of §616 and §611: an addition of a profile name with an empty `after`, an addition whose
  `after` names a removal, the removal, and a summary entry of height 1 holding the item
  `removed` that restates it by its `name` and its identifier as `id`. Added on 2026-11-20, the
  records of a rule in the form of "The rules" below, and `profileTags`.
- **`--meanings`**, `StoreMeaningVectors.json`: small stores of those records, with what
  `read.py` answers for each with its history rule on, as `status`, `served`, `auths`, `adm`,
  `valid`, `refusals` and `names`: an authority alone, two authorities, a retirement by record,
  by item and by deletion record, an authority beside a deletion record naming it, an admission,
  a revocation by record, by item and by deletion record, an admission beside a deletion record
  naming it, under a retired issuer, and under an authority that is not served. The seven added
  on 2026-11-19 are about profile names, each the store of an admission with further records or
  deletion records: a name removed by record, by item and by deletion record, a name added again
  naming its removal, an addition naming a single removal of two, an addition beside a deletion
  record naming it, and a name added twice. `read.py` answers the name on the list for the
  addition that names its removal and for the name added twice, and off it for the other five.
  Added on 2026-11-20, the stores whose names begin `rules:`, about a profile's rule at a node
  ("The rules" below).
- **`--writes`**, `StoreWriteVectors.json`: the file after each act of sequences that
  `crdt_model.py`'s `World.step` plays under `bridge.py`'s `FLAGS`, written out by `store.py`'s
  `Encoder`, each step with its records, its deletion records, its summary entries and `read.py`'s
  answer. From the store the model's World starts with: Pair, Pair, Revoke, Recreate, Pair,
  Delete and Create on a single copy; Recreate in each of two copies, Synchronize of the
  second into the first, and keeping an authority; and in two copies, Revoke of the client `c0`
  in the first, Renew of its certificate in the second, the model's `renewcert`, Synchronize of
  the second into the first and then of the first into the second. The third sequence, added on
  2026-11-18, ends with the renewed certificate refused in both copies, since the revocation
  names the client's key. The fourth, added on 2026-11-19, plays the model's `padd` and `prem` as
  Add and Remove of a profile name on two copies: `pf` removed in the first and added again
  there, removed in the second, `pg` added in each, Synchronize of the second into the first and
  of the first into the second, `pf` added again in the first, and the two Synchronize again.
  Once the copies meet `pf` is off the list in both and `pg` on it by two additions, and at the
  end `pf` is on it in both, with both copies holding the same file. The model's pairing writes
  no addition for a name it approves, which `STORE.md` §627 has a pairing write. Added on
  2026-11-20, every sequence plays with the store's rows switched on, and the sequences whose
  names begin `rules,` play rows, switches and removed versions ("The rules" below).

### The rules

Added on 2026-11-20 for the rules kept in the store, `STORE.md` §617, and printed anew the same
day in the form by what arrived, the form `seen` of "The two forms of S34" with a removed row
taken off for real. Before any mode prints, the script switches the store's rows on in that form,
as `bridge.rows` and then `bridge.dep('seen', 'off')` switch them for the runs of "The two forms
of S34": `crdt_model.ROWS` is `off` and its `PP` on, `crdt_model.DEP` and `store.DEP` are `seen`,
`read.ROWS` and `read.PP` are on, and `store.ID_FORM` is `split`. A row's version is then
identified by the subject hash over `rule:` and the node, and in bytes 9 and 10 by its profile's
tag, the first two bytes of HMAC-SHA256 under the store key over `name`, a line feed and the
name; a switch's version by the subject hash over `switch:` and the node, and zero.

**The form.** A version that takes a rule off records the letters that arrived for its profile
when it was written, or `deny` where the node's own switch refused; a version of the switch that
inherits records them for each profile. The reading cuts what arrives at a node that lets a
profile through to the letters of each such record, `deny` holding none. A write at a node with
nodes below writes anew each version below that let everything through, the nearer node first,
with what arrives after it; a version that was cutting already, or `off:deny`, is left.

**What the script chooses, and the model does not.**

- **The texts.** In the script alone, `store.py`'s `doc` and `undoc` are replaced by the same
  functions with the model's letters written as the product writes them. A row's text is `row:`
  and its letters, `row:` alone an empty rule; a rule taken off is `off:` and the letters that
  arrived, `off:` alone where nothing arrived, or `off:deny`; a switch's text is `sw:deny`, or
  `sw:inherit` followed, for each profile at least a letter arrived for, by a space, the name, `=`
  and its letters, the names in ascending ordinal order. The model writes every mask in lower case
  and in alphabetical order (`off:alr`); the product writes it in capitals in the order of
  CRUSDLAP, each letter at most once (`off:RLA`), and each vector's `about` names the difference.
  With the model's two letters, read and write are `R` and `U`: `U` for write is the script's
  choice, since the model's write has no letter in CRUSDLAP.
- **The reader.** The script reads a product's text strictly before `store.py`'s `undoc`: a mask
  of capitals of the letters the model is configured with, in the order of CRUSDLAP and each at
  most once; `off:deny` and `sw:deny` exactly; `sw:inherit` and items, each a single space, a
  name of 1 to 64 ASCII letters, digits, `-` and `_` other than `default`, `=` and a mask of at
  least a letter, the names ascending and so each once, nothing after the last. A text it refuses
  is out of form; `store.py`'s `undoc` alone takes some of them.
- **The configurations.** `configure` sets the model, `store.py` and `read.py` to a set of
  profiles, a set of letters and two or three levels, as `bridge.owed` does. The records of
  `--records` and the stores of `--meanings` are composed with the full mask and the profiles `pf`
  and `pg`, so that a mask shows the order of CRUSDLAP; read and write are then the model's `r`
  and `u`, written `R` and `U` as before. `--writes` plays two profiles, read and write and two
  levels, but O6, O7, L8, L9 and L10, which play the profiles `pf`, `pg`, `ph` and `pi` with the
  full mask as they were measured, and R1a and the writes above, which play three levels.
- **The nodes.** The root is `aa` repeated 16 times and the model's node N `bb` repeated 16
  times; `--meanings` adds a group `cc` repeated 16 times directly under N. In `--writes` N is
  directly under the root, or, with three levels, under the model's group G, `dd` repeated 16
  times, directly under the root; each sequence gives its own under `nodes` and `tree`.
- **The keys and the tags.** The rule records of `--records` and the stores of `--meanings` are
  under `keybytes('one')`, `849b00c169130b902e318a8e483f0a4c4f1ad84821891f7f6d836b863bb9c6a8`,
  as every record of those files is; the sequences of `--writes` under the model's label `K`.
  The profile tags under the first are `pf` `bf7f`, `pg` `c5f4` and `px` `e708`, under the
  second `pf` `5348` and `pg` `cfd6`.
- **The profiles** are the model's `pf` and `pg`, `px` in two stores of `--meanings`, `Zeta`,
  `p-b` and `pa` beside `pf` and `pg` in the versions of the switch of `--records`, so that the
  ordinal order of the names shows, and `ph` and `pi` in O6, O7 and L8 to L10.
- **The nonces.** Every rule record of `--records` holds the nonce of the file's other records;
  versions made apart in `--meanings` differ in their nonce, the third derived as the invented
  values are.

**What each mode prints for the rules.**

- **`--records`**: versions of `pf` at N granting `row:RU`, taken off with `off:RLA`, with `off:`
  and with `off:deny`; an empty rule of `pg`; the switch refusing, inheriting with a mask for each
  of five profiles, and inheriting with none; a rule taken off and the switch, each renewed by a
  write above, naming the version it renews; a version of `pf` granting `row:R` that names the
  grant and the first rule taken off; a summary entry of height 1 holding the item `format` and an
  item `version` for each of those, by its identifier as `id` and its `object`, `after` and
  `doc`. For a row, the `subject` and `number` printed are those its identifier holds: `rule:`
  and the node, and the profile's tag read as a big-endian number. `profileTags` holds the tag of
  `pf`, `pg` and `px`. `texts` holds 47 texts of a row or a switch, each with the model's own
  text where the reader takes it, and null where it refuses it.
- **`--meanings`**: stores of the first authority, the root's rows of `pf` and `pg` granting
  `RU` (or the root's versions a store names), the versions the store's name gives, and a summary
  entry restating what stands. Every store of the earlier file, in the form: a rule taken off
  holds what arrived when it was taken off, `RU` at N, `R` at the group under N where N gives `R`,
  nothing at the root, and `off:deny` under N's switch refusing; each answers in letters as the
  earlier store did. Beside them, 17 stores of the form itself: a cut of what arrives now; a rule
  taken off at the group under N below a version at N made apart, which it did not see; a record
  holding more than arrives; two records made apart; `off:deny` under a switch with no version,
  beside the switch turned to inherit made apart, and renewed by that switch, each renewal naming
  the version it replaces, under a deletion record; a rule taken off before the switch was turned
  to refuse; the switch inheriting with masks beside a widening above, and naming a single
  profile; a rule and the switch both recording; a switch and no verb left at N, as O6 and O7
  end; cuts at N and at the group under N along a single walk, by a rule and by the group's
  switch; and a rule taken off and the switch inheriting, each lost by a deletion record. Each
  answer adds `rules`: what each node gives `pf` and `pg` as letters and as capabilities,
  `crdt_model.caps` of the letters, the standing texts of each row and switch, what a deletion
  record shows lost, and `read.py`'s value of each, whether it refuses, the letters all granting
  versions hold, whether a version takes the rule off, and what the standing versions record; and
  `tree`. Every store reads as `ok`.
- **`--writes`**: every answer adds `rules` for each node, capabilities among it, a version present
  or deleted adds its `object`, with its `subject` and `number` as `--records` gives them, and
  every Synchronize adds `beyond`, what the copy merged into gives beyond both copies just before
  the merge, by letters and by capabilities, as `store_runs.py`'s `part_dep` holds a merge. The
  sequences whose names begin `rules,` are those of `measured-rules-per-profile` sections 4, 5 and
  7 that run over two copies, A1 to A7, the two copies of section 5, D1 to D3 in the form `again`,
  and W1 to W8, each a base in copy 0, a Synchronize into copy 1, the acts of each copy apart, and
  a Synchronize of copy 1 into copy 0 and of copy 0 into copy 1; a rule act of the report that
  sets several rows is an act for each row, in the order of the profiles' names, and an act that
  changes no row is left out. They answer as the earlier file did, but W6 and W8, as
  `measured-rules-across-levels` section 4 has them: W8 ends with N giving `pf` `R`, what the
  first copy gave, and W6 with `RU`, by design (H9). The sequences whose names begin `form,`: O6
  and O7 from `owed.py`'s `merges4`, L8, L9 and L10 from its `levels`, played the same way, ending
  with `pf` given `A` alone at N in O6 and O7, a switch with no verb and so no capability, `RL` at
  G and N in L8, `RL` at N in L9 and nothing for `pf` at N in L10; H1, then the press that settles
  (the model's `lift`), H3, H4, H9 and H10 as `s34.py` plays them, H10 with the root's row of
  `pf` first set to no letter, so that nothing arrives for `pf`, as the design states it; R1a; and
  a sequence of writes above at three levels, which renews G and N, leaves N while it cuts, and
  renews it once it no longer cuts. Every step reads as `ok` under a single key. No Synchronize
  gives anything beyond both copies but the last of H4, which gives `pf` `R` at N, within what N's
  version recorded; R1a ends with N giving `pf` `RU` after a widening of the root made after the
  merge. The commands are Set row, Remove row, Set switch, Set rows (the model's `rule` act with a
  table, a version for each row it names in a single write), Settle limit, Remove version and
  Synchronize.

### Defects and limits

- A record of a kind the family never wrote is composed by the family's functions and read by
  nothing here: its vector holds the check and the identifier, and no reading of that kind.
- The small stores are dumps composed in the script, not files a program saved.
- It has no record of its own under `recorded\`: the product's file is its record, and a change of
  `store.py` or `read.py` that changes a value shows as a difference from that file.
- **The values of a record are invented.** An authority's `ca` and `host` are invented bytes and
  no PKCS#12, and its `id` is the digest of nothing in `ca`, so the reading of §614 finds every
  authority here not whole, and `read.py`, which judges no authority whole, serves it all the
  same. An admission's `der` is a certificate made in the script that decodes, with the base point
  of P-256 as its key and a signature nobody checks, and its `certificate` is the SHA-256 of it.
- **`read.py` reads less than `STORE.md` fixes**: it asks a summary entry for no item `format`, it
  holds no `der` to its certificate, and it applies none of §648 and none of S24. The stores of
  `--meanings` keep within that, and their `about` names it.
- **The model cannot play a Create on a database with no store and then a pairing.** Its Create
  on a file with no store writes the authority under a new key label, while its pairing writes
  every admission under the label `K`; played from emptied files, every admission after that
  Create was under a key the file does not hold and passed over, and the revocation did not
  apply. So `--writes` starts each sequence from the store the model's World starts with and
  leaves that first Create out; it raises the model's `maxclients` to 4, as `growth.py` raises it.
- **The model writes the measured forms**: `v`, short names, nonces derived from its elements, no
  version record and no item `format`, an authority and an admission without the members of S3
  and S4, a revocation that also holds `certificate`, and a retirement for keeping an authority
  without the store key. So the product holds `--writes` by shape and by answer, and the first
  two modes byte for byte.
- **The model holds a switch under an object of its own name**, `sw:` and the node, which
  `store.py` writes as `switch:` and the node's identifier, and **its acts compose a version at
  the write, with no buffer**: an act reads the copy's file as it stands when played and writes
  at once, where `STORE.md` §629 has a rule set in a node's dialog written at the dialog's OK,
  naming the versions the tab showed at the press. So no vector holds a write at a dialog's OK
  that names only what the tab showed, nor a version that reached the database under an open
  dialog and that the write above leaves (H6), nor the dialog's own order of an X and the switch
  (H8). The only write of several rows is the model's `rule` act, a table of rows at a single
  node: no write sets rows at several nodes, or a row and the switch together. The `about` of
  each of the three files names this.
- **`--writes` is about 4.3 MB**, since every step holds the whole file.

### Not printed

- **A store holding a text out of form.** `read.py` reads a version's text with `undoc`, which
  raises on a text the script's reader refuses, so the reading has no answer where the product
  refuses that profile's row or reads the switch as refusing. `--records` lists such texts in
  `texts`; no store holds such a text, and none holds an object out of form either.
- **Two names whose tags meet.** `read.py` keeps a single name for each tag, the last of a set
  whose order changes from run to run, so its answer is neither both rows refused, as the
  product reads such a deletion record, nor the same in two runs. Added on 2026-11-21: with
  `--lost` they are printed, read with every name of the tag, in the section below.
- **The sequences over three copies** of `measured-rules-per-profile` section 5 and D4 and D5,
  and the residual history over three copies, outside the two copies the write vectors play;
  **the removal forms `void` and `limit`**, and **W9**, which writes a node's rule whole.
- **The form by identity**, and the store as decided beside the form: each vector holds the form
  by what arrived alone.
- **H5, H7 and H13**, which move a node with the model's `nmove`, and **H16**, a name put back on
  the list apart from a row widened, which the reading of rules does not see.

## The lost versions a version settles

Added on 2026-11-21 for `papers\ground\measured-lost-settled.md`. A refinement of the records of
rules, measured before anything of it is built in the product: a version names in `after` the
versions of its own object it replaces, and in a new member `lost` the lost versions at its place
it settles for its own object. It is played beside two other ways of answering a deletion record of
a rule's version: the measured reading of `read.py`, which answers a deletion record for every
object once any version names it, and the product's reading as built, which answers it for an
object only where a version of that object names it. No program but Python runs: every file is
the model's, written by `store.py`'s Encoder and read by `read.py`. Its record is
`recorded-lost\`.

### What it runs

`model\lost_runs.py all OUT WORKERS` runs these parts, each into `OUT\<part>.txt`:

| Part | What it is |
| --- | --- |
| `tags` | the names: two whose tags meet under the store key of the model's label `K`, `pf`, a name whose tag is 0, and the names a write that touches no rule adds |
| `hand` | 14 sequences, each step under each of the three behaviors (`crdt_model.JUDGE` and `read.JUDGE` `union`, `object` and `lost`), held to the exact reading of the same file, with each merge held to both copies, a write that touches no rule made on a clone after each merge, and `read.py` over the file the Encoder writes held to the model's fold |
| `search-two`, `-three`, `-zero`, `-hand`, `-long` | `lost_runs.py search CONFIG 200000 SEED LO HI`: random histories over two copies, played under each behavior; `two` the root and N with `pf` and the two names, `three` with a group G between them, `zero` with `pf` and the name whose tag is 0 and the object of N's rule with no slash, `hand` `two` with a person's deletions and Seal again, each of 4 to 12 steps, and `long` `two` with 8 to 16 |
| `bridge-two`, `-three`, `-zero`, `-hand` | `lost_runs.py`'s `part_bridge`: 2,000 random histories of each search's alphabet under each behavior, every file read by `read.py` and held to the model's fold after every step |

`compare_lost.py check OUT RECORD` holds a run to the record, `compare_lost.py print VECTORS` prints
the three files of vectors into `VECTORS` and holds every file `vectors.py` prints to the same file
printed under each behavior, and `compare_lost.py vectors VECTORS SHARED` compares those files with
the files the product keeps.

**What a history is held to.** After every step that applied, the copy it changed, under the
behavior, beside the exact reading of the same file (`World.fold(exact=True)`: every deletion record
told of by the object of the element it names, which the model knows and a file does not, answered
where a version in force of that object names it in `after` or in `lost`): a capability the
behavior gives that the exact reading does not, and a row that refuses under the behavior and not
under the exact reading where no deletion record it refuses for is of a loss (an identifier the act
`lose` made, a version the act `del` deleted, or a version replaced only by versions `del`
deleted). After every merge, every `lose` and every `del`, a write that touches no rule, a profile
name of `KEPT` added or removed, on a clone: any answer it changes. Every merge is held to both
copies by capabilities, and a merge beyond both copies is classed as wider than the exact reading,
beyond both copies read exactly as well, or within what the copies give read exactly.

### The command

From the branch root, in any shell:

```powershell
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\model\lost_runs.py all spikes\storage\probes\store-as-a-whole\out\lost-1 10
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\model\lost_runs.py all spikes\storage\probes\store-as-a-whole\out\lost-2 10
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare_lost.py check spikes\storage\probes\store-as-a-whole\out\lost-2 spikes\storage\probes\store-as-a-whole\recorded-lost
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare_lost.py print spikes\storage\probes\store-as-a-whole\recorded-lost\vectors
uv run --no-project python -I -X utf8 spikes\storage\probes\store-as-a-whole\compare_lost.py vectors spikes\storage\probes\store-as-a-whole\recorded-lost\vectors <the product's tests\Shared>
```

The last argument of `all` is the number of processes the searches use. A run writes only into
its `OUT`; the searches are seeded, each chunk of 500 histories with a seed of its own, and the
processes' results are gathered in the order of the chunks.

**How long**, in seconds, ten processes; during the first of the searches of run 1 the vectors
were printed beside it:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `search-long` | 337 | 323 |
| `search-three` | 263 | 246 |
| `search-two`, `search-hand`, `search-zero` | 204, 204, 179 | 207, 192, 211 |
| `bridge-two`, `-three`, `-zero`, `-hand` | 4, 5, 4, 4 | 4, 5, 4, 4 |
| `tags`, `hand` | under 1 each | under 1 each |
| `compare_lost.py print` (12 prints of `vectors.py`) | 14 | 14 |

**How the runs were made.** Run 1 and run 2 were `lost_runs.py all` with ten processes, in turn.
Run 1's outputs were copied to `recorded-lost\`; `recorded-lost\vectors\` and its two listings,
`vectors-print.txt` and `vectors-compare.txt`, are what `compare_lost.py print` and `vectors`
wrote after run 2, into `recorded-lost\` and again into `out\lost-2\`.

**Compared.** Every output of run 2 is the same as `recorded-lost\` line for line, with the
running times masked: 13 files, the two listings of the vectors among them; and the three files
of vectors printed into `out\lost-2\vectors\` are the same bytes as those of
`recorded-lost\vectors\`, and so are those of a third print. During run 1 the files were printed
twice with the same bytes, before the wording of an `about` text changed.

| Figure | Recorded | Second run | Reproduced |
| --- | --- | --- | --- |
| `hand`: steps flagged wider, refused, beyond, changed, file differs, union; object; lost | 11, 0, 0, 0, 0; 0, 50, 3, 5, 0; 0, 0, 0, 0, 0 | the same | yes |
| searches, histories failing under union, object, lost: `two`, `three`, `zero`, `hand`, `long` | 9,847, 104,015, 0; 7,236, 80,472, 0; 336, 9,956, 0; 5,748, 77,420, 0; 19,436, 137,012, 0 | the same | yes |
| searches under lost, histories with a merge beyond both copies, the exact reading beyond as well; within the exact readings | 1,316, 1,506, 1,186, 874, 2,745; 577, 610, 277, 409, 1,243 | the same | yes |
| the same, Synchronize merges | 0, 1, 0, 0, 0; 0, 0, 0, 2, 0 | the same | yes |
| `bridge-*`: steps compared, steps that differ, each behavior | 12,444 to 13,453 a part; 0 | the same | yes |
| vectors: each file under `--judge union`, `object`, `lost` | the same bytes as without it, 9 of 9 | the same | yes |
| vectors: records, stores, sequences added; members changed or removed | 8, 14, 14; 0 | the same | yes |

### What was added for it

Each line added or changed carries "ADDED (lost settled)". With the new switches off, every output
prints as before: `vectors.py` prints its three files byte for byte as the product keeps them but
the line ends, and the same under `--judge union`, `--judge object` and `--judge lost`.

- `crdt_model.py`: an element's `lost`; `JUDGE` and `place_of`; `fold(exact=True)` and
  `judge_dels`, what each deletion record of a version is lost to under each behavior and in the
  exact reading; `names_lost`, what an act names, used by `rows_act`, `dep_act` and its renewals,
  the removal of a single version and Seal again; the acts `settle`, `plain` and `lose`; and
  `loses`, the identifiers `lose` made and the versions `del` deleted, carried by `World.clone`.
- `store.py`: the Encoder writes `lost` in a version's content and in its summary item, absent
  where empty.
- `read.py`: `JUDGE`; a version's and an item's `lost`; `judged`, the same reading as the model's
  over the file's tags, with a tag of 0 telling of the object with no slash as well.
- `vectors.py`: `--judge J` and `--lost`; `rule_record` and `restating_version` with `lost`; the
  records, the stores (`lost_stores`) and the sequences (`lost_command`) of the refinement.
- `model\lost_runs.py`, `compare_lost.py`: new.

### Defects and limits of this configuration

Reported, not fixed.

- **The model composes every act at its write**, so the lost versions an act names are those of
  the write's own reading; the product names those the tab showed at the press. A deletion record
  that lands under an open dialog is not run.
- **A loss is a deletion record whose content no file holds** (`lose`), or a person's deletion
  (`del`); a loss by a merge that drops an entry with no deletion record is not in the alphabet.
- **The exact reading reads the model's elements**, which a file does not carry; it is the oracle
  of the runs and no reading a gate could make.
- **The searches hold two copies**, two letters, three profiles at most and three levels at most,
  and KeePass's Synchronize, Overwrite if newer and KeePassXC as the model has them.
- **The files are the model's**, written by the Encoder; no file was made or merged by KeePassLib
  or `keepassxc-cli`, and the product's reading was not run.

### Not run in this configuration

- Real files and every program of the earlier configurations; three copies; the full mask and four
  profiles in a search; the product.
