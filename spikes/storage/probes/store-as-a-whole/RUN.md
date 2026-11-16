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
- **The plugin writes nothing again for a record that counts by its history**, the narrowest reading
  of a point `EVALUATION.md` section 8 leaves open: the next write of the plugin leaves the edited
  entry as it is, and the summary entry it writes restates what is in force as before.
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
