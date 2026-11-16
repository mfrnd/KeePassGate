# Measured: the store as decided

The store as the project decided it on 2026-11-16, run on real files the same day: the design of
the option `crdt` as `EVALUATION.md` section 4 holds it, with no repair after a Synchronize and with
the history rule as the reading of an edited entry. The run of the store as a whole
(`measured-store-as-a-whole.md`) had the repair switched on and stopped on any damaged entry; this
report runs the configuration that was decided instead. Every figure here comes from the family
`spikes\storage\probes\store-as-a-whole\` of the branch `spikes/storage-survival`, run with its
switch `-Decided`: the record is `recorded-decided\` there, and its `RUN.md`, section "The store as
decided", has the command, both runs' times and a second run's value of every figure. Nothing ran in
a window, on a phone or in an emulator; every file was made and changed by KeePassLib 2.62 and
merged by KeePassLib or `keepassxc-cli`.

**How to read it.** The words are those of `measured-store-as-a-whole.md`. **Wider** is a client
reaching more than the last decision allowed, and a sequence that ends wider is named
**fail-open**; **refuses** is the plugin refusing what the last decision allowed, with its scope
(the whole database, or nothing served); **narrower** is a file granting less than the last decision
without refusing as a whole. The oracle is every decision any gate made, as there. **The repair** is
the plugin writing back, inside the save of a Synchronize in its own KeePass, whatever took access
away before the merge and no longer did after it; the decided store has none. **The history rule**
is the reading decided for an edited entry: every record carries a check, a keyed digest of its
own `KPG.` strings; a record counts in its current version when that version passes the check, or
else in a version kept in the entry's history that passes; only an entry of the store with no
passing version anywhere, or a missing summary entry, stops the whole database. **The plain stop**
is the reading of the first run: an entry under an identifier of the store whose current version
fails stops the whole database. A **summary entry** (the papers' head) restates everything in force
that takes access away, with every write of the plugin.

## 1. What was run

| Part | What it is | Cells |
| --- | --- | --- |
| A, on files | every part of the first run's real files but `b-slow`, the same histories and merges, with no repair after any Synchronize and every file read under the history rule | `b-merges` 234 merge cells and 90 cells of a user node, `b-deletions` 144, `b-clear` 210, `b-dedupe`, `b-dupes`, `b-repair` 96 cells in its modes with no repair, `b-random` 300 histories, `b-growth` |
| A, in the model | the first run's search, its search with no hand loss of the authority's entry and the extended model, the repair switched off | 24, 24 and 20 cells of 20,000 or 8,000 histories |
| B | `b-history`, new: ten kinds of store entry, each edited at copy 0 by ten operations of KeePass's own, then read and merged with an unedited copy, under the history rule and beside it under the plain stop | 590 cells |
| C | `b-growth`, the growth of the store at 1,000 writes, read under the history rule | 4 sizes |

Everything ran twice; the second run printed every output of the first line for line, but the
running times (`RUN.md`). The configuration is as decided: no repair, several gates, no
deletion record that names no entry (the papers' witness), nothing on the operator's own entries
or groups, a revocation naming the client's key, the store key carried over a Recreate and kept in
the retirement of an authority deleted with none after it, and nothing kept on the gate's machine.
With the switch off, the family prints what it printed before: six parts were run again after the
switch was added and came out the same as the first record.

**The history rule as implemented**, in `read.py`, and the narrowest reading where the decisions
leave a point open:

- A version of an entry is its current strings or a history item's; history items are tried newest
  first, and the first version that passes is the record.
- An entry is a candidate for the store when any of its versions carries a `KPG.` string.
- The store key is taken from any version of an authority or of a retirement that holds it, which
  passes under that key.
- An entry with no passing version is damage, and stops the whole database, only when its
  identifier carries the store's keyed tag under a key the file holds; with no key held, nothing
  is recognized as the store's, and the read is `nostore`, as in the first run.
- **Open, read narrowly:** the plugin writes nothing again for a record that counts by its history
  (`EVALUATION.md` section 8 leaves open whether it restates such a record). What the other reading
  would change is in section 6.

**Cut, to keep the two runs within the time set for them** (both took under 15 minutes each here):
`b-slow`, whose 32 KeePassXC cells under the slow derivation came out as the fast cells in the
first run, and whose question, the seconds of two loads, touches nothing this store keeps;
`store-ablations`, `store-scenarios` and `store-section6`, whose first records already hold a
column with the repair undone; `store-repair-closes`, which is about the repair; and the copy check
of the design's model.

## 2. A. No repair, on real files

### 2.1 Cell by cell against the first run

Every cell of every part was held to the same cell of the first record. The table lists every cell
that differs; every other cell is unchanged.

| Part, history | Cell | With the repair (first run) | No repair (decided) |
| --- | --- | --- | --- |
| `b-merges` H13, a copy from before a Recreate and a copy after it | Synchronize into the older copy | narrower: N lacks read (the repair took N's later rule for a loss) | **as decided** |
| `b-merges` H7, a rule set, widened, narrowed and emptied at N, narrowed at the root | Create new IDs into the stale copy | **wider**: N gives read | **wider**: the root gives read and write (N's read as well) |
| `b-merges`, a user node N under its own rule and a granting root | the copy of N, by Duplicate Entry in all 18 cells of T2 and by Create new IDs into copy 1 in T1, T3, T4 and T5 (22 cells) | the copy gives read: the repair at the history's Synchronize had written the root's earlier read beside its read and write | the copy gives read and write, what the root gives (accepted by decisions 9 and 10) |
| `b-clear` P5, renewals refused, then allowed again | Synchronize into the copy taken before | narrower: the renewal refused again by the repair | **as decided** |
| `b-dedupe` | the third copy synchronized | refuses, nothing served; 11 entries passed over | the same outcome; 9 entries passed over (the repair had written 2 entries under a key the file no longer held) |

**Tallies.** `b-merges`: 221 as decided, 3 narrower, **10 wider** (first run: 220, 4, 10); every
wider cell is still Create new IDs into the copy that lacks the decisions, row 6 of
`EVALUATION.md` section 6. N under its own rule gives nothing in all 90 cells of a user node, as
before. `b-deletions`: 126 as decided, 12 narrower, 6 refusing, **none wider**, as before.
`b-clear`: 210 as decided of 210 (first run: 209 and 1 narrower), **none wider**. `b-dedupe` and
`b-dupes`: every outcome as before. `b-repair`: all 96 cells of the modes with no repair are the
same cell for cell as the first run's same modes.

**No cell on files ended wider with the repair off that was not wider with it on**, and no cell
ended refusing that did not refuse before. Two cells ended as decided where the repair had made
them narrower (H13 and P5); in the H7 cell, wider either way, the root that the repair had narrowed
in the stale copy now gives read and write there too; and 22 readings of a copied node give what
the place they land in gives, which decision 10 accepts.

### 2.2 Row 7: a file made by somebody holding the store key

`EVALUATION.md` section 6, row 7, expects such a file to end wider at the gate and in both files
once it is synchronized in. **Confirmed on files.** `b-repair` R8 (N narrowed at gate 0, copy 1
synchronized, a version granting read and write at N made with the store key in copy 1, then
KeePass's Synchronize at gate 0 writing both files): **wider, N gives read and write, in file 0 and
in file 1**. The same merge as File, Import with Synchronize's method, Overwrite if newer,
Overwrite existing or Keep existing: wider in file 0, the gate. Through Import with Create new IDs:
file 0 as decided, since nothing arriving under a new identifier is read. **Fail-open.** The repair
had closed this sequence where it ran and reached both files; with no repair nothing in the store
covers it, as row 7 states.

### 2.3 The random histories

`b-random`, the bridge over 300 random histories of two and three copies: 0 failures of the model's
safety check (no file wider than what was delivered to it), as before. With the repair off and the
plain stop, a check run beside the record outside the family, the 300 histories printed the first
run's output line for line: **the repair changed nothing in them**. Under the history rule, 1,419
steps applied (first run 1,416: an edited entry no longer stops the store, so three more gate acts
apply), and the 724 files at the end were as decided in 406, narrower in 93, refusing in 54 and
wider than every decision in 171 (first run: 397, 91, 73, 163). Against every decision any gate
made, "wider" includes a copy that a decision made elsewhere has not yet reached. By the safety
check no file is wider than what was delivered to it, so the 8 more are, by reasoning, files that
read on where the plain stop refused and lack a decision made at another gate. They were not read
singly, and the tally is not a measure of the store.

### 2.4 The model with no repair

| Search | `safety` histories with the repair | Without it | Difference |
| --- | --- | --- | --- |
| The first run's search, 24 cells of 20,000 | as in its record | the same in every cell but F over two files: **6 against 5** | 1 history in 480,000 |
| No hand loss of the authority's entry, 24 cells | 1 in D and E over two files, 1 in H over three | the same | none |
| The extended model, 20 cells of 8,000 | P1 2, P2 1, F 1 and 1, F2 1, DX 1 | the same | none |
| Exhaustive, after the design's divergence | 11,110, 47,988, 40,494, 27,930 histories, no failure | the same | none |

That history is the history the repair closed in the first run, replayed in the model with and
without the repair: `del 0 (authority) with a record; rule 1 root r; del 1 (authority) with a
record; renew 1 c0 forever; create 0; Synchronize 1 from 0` gives file 1 a renewal of c0 asked,
whose refusal was written under the store key both deletions lost. **Fail-open**, row 12 of
section 6 (the store key's carrier deleted by hand, then Create). Without the repair, the
merges-only histories that converged narrower (35 over two files, 28 over three) and every file the
repair made narrower (8 to 85 a cell) are gone; the accepted rule lost before a save is unchanged in
every cell (86 to 106 over two files, 60 to 89 over three).

## 3. B. The history rule with the summary entries

### 3.1 The cells

The base, both copies equal: `revoke 0 c0; recreate 0; pair 0 (c1); padd 0 pg; prem 0 pf; rule 0
N none; renew 0 c1 forever; Synchronize 1 from 0`. A tenth kind, the retirement that holds the store
key, has `delauth 0` before the Synchronize, so that no authority is served and every read of it is
followed by Create at that copy. A single entry of copy 0 is changed by a single operation, as
KeePass's own commands change it (KeePassLib's `CreateBackup` with the database, which trims the
history to the database's limits, then the change, then the modification time):

| Operation | What it does |
| --- | --- |
| dialog | the entry dialog: the first character of the record's subject in `KPG.Data` changed |
| field | the entry dialog: `KPG.Data` deleted |
| quick | a quick edit, Expires: Now (the check does not cover it) |
| move, bin, delete | moved to another group; to the recycle bin; deleted for good with a deletion record |
| edits10, edits11 | the dialog edit, then 9 or 10 dialog edits of the title: 10 or 11 edits under KeePass's default limit of 10 history items |
| quick10 | the dialog edit, then 10 quick edits |
| off | the database's limit of history items set to 0 in both copies, then the dialog edit |

Columns: copy 0 as it is; Synchronize of copy 0 into copy 1 and of copy 1 into copy 0; the same by
KeePassXC 2.8.0; copy 0 after the gate writes again (a profile name added). Each read under the
history rule, and the first five under the plain stop.

### 3.2 What each kind and operation does

Letters per column, in the order above: **A** as decided, **n** narrower, **R** the whole database
refuses with the store key held (sealing the store again applies), **K** the whole database refuses
with no store key held (sealing again does not apply), **x** nothing served. For the retirement
that holds the key, the letters are after Create.

| Kind | dialog, field, edits10 | quick, move, bin | delete | edits11, quick10, off |
| --- | --- | --- | --- | --- |
| the authority | AAAAAA (plain stop: KKKKK) | AAAAAA | KKKAxK | KKKKKK |
| a retirement (from Recreate) | AAAAAA (plain stop: RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| an admission record (c1) | AAAAAA (RRRRR) | AAAAAA | nnnAnn | RRRRRR |
| a revocation (c0) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| a profile addition (pg) | AAAAAA (RRRRR) | AAAAAA | nnnAnn | RRRRRR |
| a profile removal (pf) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| a rule version (N, nothing) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| a renewal answer (c1, refused forever) | AAAAAA (RRRRR) | AAAAAA | AAAAAA | RRRRRR |
| the summary entry | AAAAAA (RRRRR) | AAAAAA | RRRARR | RRRRRR |
| the retirement that holds the store key, then Create | AAAAA | AAAAA | nnnAA | nnnnn |

**Counts.** Under the history rule, 590 cells: **390 as decided, 28 narrower, 172 refusing, none
wider.** Under the plain stop, the 500 reads before any write: 214 as decided, 8 narrower, 278
refusing, none wider. The two differ in exactly the dialog, field and edits10 rows of the nine kinds
an authority is served for (135 reads): the plain stop refuses, the history rule reads as decided.
**In no cell did the history rule give more than the plain stop, and in no cell did access end
wider than before the edit.** Every merge cell gave what copy 0 gave, except where the operation was
a deletion for good: KeePassXC 2.8.0 into the unedited copy carries no deletion record, so that copy
keeps the entry and reads as decided, and into copy 0 it brings the authority back beside its
record, where the key is read and no authority stands.

### 3.3 Where the rule holds, and where it stops holding

- **It holds while the written version is in the history.** After a single dialog edit, a deleted
  `KPG.Data`, or ten edits in all under KeePass's default limit of 10 items, every kind reads as
  decided, in copy 0, after Synchronize and after KeePassXC 2.8.0 in both directions, and after the
  gate's next write.
- **Under KeePass's default history settings an entry survives 10 edits; the 11th takes the written
  version out**, whatever the later edits are: ten dialog edits of the title or ten quick edits,
  which the check does not cover, count alike. From then on the whole database refuses, in that copy
  and in every copy the edited entry reaches by either merge. A merge with an unedited copy did not
  bring the written version back in any of these cells: KeePassLib trims the merged history to the
  limit, and KeePassXC 2.8.0 kept the newer entry whole.
- **A database that keeps no history** (0 items) loses the written version at the first edit: the
  first dialog edit of any entry of the store refuses the whole database, as the plain stop does.
- **A move, the recycle bin and a quick edit alone** leave every kind as decided under both
  readings; a quick edit adds a history item and so moves the written version toward the limit.
- **A deletion for good with a deletion record** is no edit and the history rule does not reach it:
  a revocation, a retirement, a removal, a rule version and a renewal answer read as decided by the
  summary entry; an admission record and a profile addition end narrower (that client refused, that
  name off the list); the summary entry refuses the whole database (missing summary entry); the
  authority leaves no key (section 3.5).

### 3.4 An edit of the summary entry

Run under the rule as written, with nothing new decided about it: an edited summary entry counts in
the version its history holds, and restates what it restated before. After a dialog edit, a deleted
`KPG.Data` or ten edits it reads as decided in every cell, and **the gate's next write replaces it**
with a new summary entry and deletes the edited entry with a deletion record, so it no longer counts
by its history after that write. Past the limit, or with no history kept, the whole database refuses
with the store key held, which is the stop sealing the store again was run for after a deleted
summary entry in the first run; it was not pressed here.

### 3.5 The authority and the retirement that holds the store key

These two entries carry the store key in turn. **Where the entry that carries it has no passing
version, the file holds no key**: nothing is recognized as the store's, the read is `nostore` and
nothing is served. That is not the stop the history rule names: sealing the store again does not
apply, and the next Create makes a new key under which every rule, name and revocation written
before is passed over.

- The authority, past the limit or with no history kept: **K** in every cell, the gate cannot
  write. Deleted with a record: K in copy 0 and after Synchronize either way; after KeePassXC into
  copy 1, which carries no record, as decided; after KeePassXC into copy 0, the entry back beside
  its record, the key is held and no authority stands, so nothing is served.
- The retirement that holds the key, past the limit or with no history kept: after Create, **every
  cell narrower** (the root's rule passed over); deleted with a record: narrower after Synchronize,
  as decided after KeePassXC either way: into copy 1 no record arrives, and into copy 0 the
  retirement brought back beside its own record still gives the key. A rule set again after such
  a Create ends wider (the first run's section 5, item 8; not run again here).

## 4. C. Growth

**Unchanged, and why.** Neither change writes anything per operation: the repair never ran in
`b-growth`, whose single gate never synchronizes, and the history rule only reads. Run again under
the history rule, `b-growth` printed the first record's sizes: after 1,000 writes a summary entry of
81,551 bytes in 272 strings naming 1,144 earlier summary entries, 285 entries in the store, 2,008
deletion records; only the file's bytes vary by some bytes from save to save, as in the first run.
With no repair the store loses the writes the repair made after a Synchronize, which `b-growth`
never had. An edited entry grows by the history KeePass keeps, up to its limit; the plugin adds
nothing.

## 5. Every fail-open sequence in this run

1. **Row 6, Create new IDs of a newer copy into a stale copy**: 10 of the 26 Create new IDs cells of
   `b-merges`, every cell into the copy that lacks the decisions, as in the first run.
2. **Row 7, a file made by somebody holding the store key, synchronized in**: wider at the gate and
   in both files (section 2.2); the repair no longer closes it anywhere.
3. **Row 4, records cleared after a loss of both carriers, then the stale copy synchronized**
   (`b-repair` R1): wider at gate 0 and in file 1 through Synchronize and every import method but
   Create new IDs, as before.
4. **Row 3, a narrowed rule's new version and summary entry removed by script after a KeePassXC
   merge** (`b-repair` R2): wider in the copy that removed them, as before; gate 0 as decided.
5. **Row 12, the store key's carrier deleted by hand, then Create**: a single history more in the
   model's search than with the repair (section 2.4).
6. **The model's other `safety` histories**, unchanged from the first run's search: rows 2, 3 and
   12 by their shortest forms in `measured-store-as-a-whole.md` section 2.1.

Nothing in `b-history`, `b-deletions` or `b-clear` ended wider.

## 6. Where the decided design does not hold as described

- **An entry with no passing version is not always a stop that sealing again lifts.** For the
  authority and the retirement that holds the store key, no passing version means no key: nothing
  served, sealing again not applicable, and Create starting a new key (section 3.5). This is row 12
  and row 19's cost, reached by edits as well as by a deletion.
- **The stop that the history rule keeps** comes at the 11th edit of an entry under KeePass's
  default limit, counting quick edits that the check does not cover, and at the first edit in a
  database that keeps no history.
- **The open point of section 1, read the other way**: if the plugin restated a record that counts
  by its history at its next write, the edits10, edits11 and quick10 rows would not move toward the
  stop through the plugin's writes; the edits themselves would still reach it, since each edit
  after the restatement is of the entry it edits. Not run.

## 7. Two ranges of the first report, from its record

`measured-store-as-a-whole.md` gives two ranges in several forms. From its `recorded\`:

- **Files the repair made narrower** (`info:repair-narrower`): "8 to 74" is `store-search`, over
  two files 16 to 74 and over three 8 to 47 (24 cells); "9 to 85" is the store's own rows of
  `store-ablations`, over two files 20 to 85 and over three 9 to 18 (10 cells), and equally
  `store-search-noauth`, 20 to 85 and 9 to 40 (24 cells); "8 to 85" is both searches together
  (48 cells).
- **A rule lost before a save** (`accepted:unsaved-rule-lost`): "71 to 111" is the store's own rows
  of `store-ablations`, over two files 80 to 111 and over three 71 to 90 (10 cells); "60 to 115" is
  both searches together, `store-search` 60 to 106 (86 to 106 and 60 to 89) and
  `store-search-noauth` 66 to 115 (80 to 115 and 66 to 90), 48 cells.

## 8. Not measured

A window, a phone, an emulator or a second KeePass with the plugin; sealing the store again after a
stop of `b-history`; an edit in both copies, by pykeepass, by a phone program or of a history item
by script on this store (run in `edited-refusals` on a store without summary entries); Database
Maintenance's history page; the other KeePassLib methods and KeePassXC releases for the edited
entries; a rule set again after the key carrier's loss; the model with an entry's history; and
everything the first run lists as not run.
