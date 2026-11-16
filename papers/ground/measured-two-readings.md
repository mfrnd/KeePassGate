# Measured: two readings of the store as decided

The run of the store as decided (`measured-decided-store.md`) left two readings open. **Reading 1**
has since been decided and is measured here before it is built: whenever the plugin writes to its
store anyway, it also writes again every record that counts only by its history, so that the
written version is current again; never after a merge, never on a read. **Reading 2** is to be
decided from this measurement: a record that takes access away counts from any entry whose content
passes the check, whatever the entry's identifier, while a record that gives access never counts
from a copy. Both ran on real files on 2026-11-16, each as a configuration of its own on top of the
store as decided, in the family `spikes\storage\probes\store-as-a-whole\` of the branch
`spikes/storage-survival`: `run.ps1 -Rewrite`, recorded in `recorded-rewrite\`, and
`run.ps1 -Copies`, recorded in `recorded-copies\`. The family's `RUN.md`, section "The two
readings", has the commands, both runs' times and a second run's value of every figure. Nothing ran
in a window, on a phone or in an emulator; every file was made and changed by KeePassLib 2.62 and
merged by KeePassLib or `keepassxc-cli` 2.8.0.

**How to read it.** The words are those of `measured-decided-store.md`. **Wider** is a client
reaching more than the last decision allowed, and a sequence that ends wider is named
**fail-open**; **refuses** is the plugin refusing what the last decision allowed, with its scope
(the whole database, or nothing served); **narrower** is a file granting less than the last decision
without refusing as a whole; the oracle is every decision any gate made. **The history rule**: every
record carries a check, a keyed digest of its own `KPG.` strings; a record counts in its current
version when that version passes, or else in a version kept in the entry's history that passes;
only an entry of the store with no passing version, or a missing summary entry, stops the whole
database. A **summary entry** (the papers' head) restates, with every write of the plugin,
everything in force that takes access away. A **copy** is an entry holding a record's strings under
an identifier the record's content does not give: what KeePass's Duplicate Entry, Duplicate Group,
copy and paste, and its import with "Create new IDs" make. **Row 6** is the sequence of
`EVALUATION.md` section 6 that ends wider on real files most often: a stale copy imports the newer
file with Create new IDs, so the newer decisions arrive only as copies and are not read.

## 1. What was run

| Configuration | Part | Cells |
| --- | --- | --- |
| Reading 1, two forms of writing again (section 2.1) | `one`: ten kinds of store entry by the ten operations of `b-history`, then one write of the gate's own, further edits until the store refuses, and four merges with the unedited copy | 100 cases, each with a write, up to 12 further edits and 4 merges |
| | `alternate`: edits and the gate's writes alternating past KeePass's default limit of 10 history items | 10 kinds by 5 patterns, up to 22 cycles |
| | `two`: two gates writing the same edited record again, and one copy writing it again while the other holds a later edit, both orders; 12 merges each | 324 merge cells, and 5 rounds of merges for each of 9 kinds |
| | `prune`: a later write at the other gate that takes the record out, before and after the rewrite; 12 merges each | 120 merge cells |
| | `b-random`: 300 random histories with the rewrite after every gate act | 300 histories |
| | the control: `two` and `prune` with nothing written again | 444 merge cells |
| Reading 2, two variants of the completeness check (section 3.1) | the parts of the store as decided: `b-merges`, `b-deletions`, `b-clear`, `b-dedupe`, `b-dupes`, `b-repair`, `b-random` | 234 and 90, 144, 210, 2 orders, 13 cases, 96 cells, 300 histories |
| | `copies`: copies that are not an import, and the combination with writing such a record again under its own identifier | 9 kinds by 8 cases, 2 replaced versions by 6 merges, 10 histories |

Every part ran twice; the second run printed every output of the first line for line, but the
running times. Each configuration took about five minutes with six parts at once. With the new
switches off, six parts of the first record and seven of the store as decided were run again after
the addition and printed their records line for line.

**Cut.** Writing again as a new entry beside the edited entry (section 2.1); KeePassXC releases
other than 2.8.0 and the slow key derivation; the reading of copies in the model's searches; the
plugin's own Seal again after a stop caused by a copy (the model's Seal again stands for it in
section 4).

## 2. Reading 1: an edited record written again at the plugin's next own write

### 2.1 What writing again is, in what was measured

The store's entries are write-once, and their identifiers are made from their content: three bytes
of the kind's keyed tag, five of a keyed hash of what the record is about, the height, and six bytes
of the check. A record written again with the same content therefore has the same identifier, and
**the same entry's current version set back** is the only form that writes *the written version*
current again, which is what the decision's wording asks. Two variants of that form were run:

- **backup**: the edited version is put into the entry's history first, as an edit through
  KeePass's own path does (`CreateBackup` with the database, which trims the history to the
  database's limits); then every `KPG.` string of the history item that passes, with its
  protection, becomes current, and every other `KPG.` string goes. The identifier, the place, the
  title and every string the check does not cover stay; the modification time becomes the write's.
- **cleared**: the same, with the entry's history emptied instead.

**The other form, a new entry beside the old with the old replaced through the summary entry, was
not run**, and is not cheap here: a new entry needs new content (a new nonce) to pass its identifier
check, so it is a new record under a new identifier, and every record that names the old record
would have to be pointed at the new record. A profile addition names every removal of its name, a
rule version names every version it replaces, and a summary entry names each version it restates.
What that form would change, by reasoning, not measured: no history item consumed by the rewrite,
the old entry deleted with a deletion record (one more record per rewrite), and in a merge with a
copy that never saw the rewrite, two records for the same decision, which the read already tolerates
for a refusal and would have to reconcile for an addition or a version. The probe's two variants
differ only in the entry's history; every outcome below is the same under both.

### 2.2 One gate

After the edit, one write of the gate's own (a profile name added; Create for the retirement that
holds the store key, where no authority stands):

| Operation | After the write | History of the entry after the write (backup) | Further edits until the store refuses | The 4 merges with the unedited copy |
| --- | --- | --- | --- | --- |
| dialog, field (the record's payload changed or deleted) | as decided, the record written again, in all 10 kinds | 2 items: the written version current, then the edited version, then the written version | the 11th; 10 further edits read as decided | as decided, 40 of 40 |
| edits10 (10 edits) | as decided, written again | 10 items, the written version current and none of the 10 items passing | the 11th | as decided, 40 of 40 |
| quick, move, bin | as decided; nothing to write again, since the current version passes | 1, 0, 0 | the 11th | as decided, 40 of 40 |
| edits11, quick10, off (no passing version left) | the gate cannot write: the store has stopped already (the authority: no store key held; the key carrier: section 2.4) | unchanged | not run | as in `measured-decided-store.md` |
| delete (for good, with a record) | as in the store as decided: nothing to write again | gone | not run | as in `measured-decided-store.md` |

The summary entry is never written again: the gate's write replaces it with a new summary entry and
deletes the edited entry with a record, as in the store as decided. Tally of the reads after the
write and after each merge, 500 in all, in both variants: 337 as decided, 20 narrower, 124 the whole
database refusing with the store key held, 18 refusing with no key held, 1 nothing served,
**none wider**. Every outcome that is not as decided comes from the operations after which no
version passes or the entry is gone, where nothing can be written again.

**What the rewrite buys and what it does not.** It takes the written version back to the front of
the history at every write, so a record survives any number of edits as long as fewer than 11 fall
between two writes of the plugin's own. It does not touch the edits themselves: the 11th edit
between two writes still takes the written version out, and a database that keeps no history still
loses it at the first edit.

### 2.3 Edits and writes alternating

| Edits between writes | Cycles | Every kind but the summary entry, backup | The same, cleared |
| --- | --- | --- | --- |
| 1 | 22 (22 edits) | as decided at every cycle; history 2, 4, 6, 8, then 10 from the 5th cycle on, the written version current after every write | as decided; history 0 after every write |
| 3 | 8 (24 edits) | as decided; history 4, 8, then 10 | as decided; 0 |
| 10 | 3 (30 edits) | as decided; history 10 | as decided; 0 |
| 11 | 2 | the 11th edit stops the store before the write (no key held for the authority); the gate cannot write; for the key carrier, Create starts a new key, narrower | the same |
| 1 edit, then 12 writes alone | 12 | as decided; the first write writes the record again, the other 11 write nothing again; history 2 throughout | as decided; 0 |

The summary entry reads as decided in every cycle of every pattern but the 11-edit pattern, since
every write replaces it. **An entry written again every 10 or fewer edits never reached the stop**,
over 30 edits in the longest sequence.

**The plugin's own rewriting consumes history, but never the passing version.** Under `backup`, a
rewrite adds one history item (the edited version), and the edit before it added another (the
written version): two items a cycle until KeePass's limit of 10, after which KeePass drops the
oldest. The version that passes is current after every write, and the newest history item after the
next edit, so the trimming always falls on older items. **Writes alone write nothing again**: once
the record is current again it counts by its current version, and the next 11 writes left its
history at 2. Ten rewrites in a row cannot therefore occur without ten edits between them. What the
rewriting does consume is the operator's own history of the entry: after five edit and write cycles
the 10 items are rewrites and edits of the payload, and nothing older. Under `cleared` the history
is 0 after every write, so nothing of an edit survives the next write.

### 2.4 The two entries that carry the store key

- **The authority.** Written again like any record (dialog, field, edits10: as decided, history as
  above), and it survives 10 further edits; at the 11th the file holds no store key and the whole
  database refuses with no key held. Past the limit before a write (edits11, quick10, off) the gate
  cannot write, so nothing is written again and the loss is the key's, as in the store as decided.
- **The retirement that holds the key after a Delete with no new authority.** No authority is
  served, so the gate's next own write is Create, and that Create writes the retirement again
  (dialog, field, edits10: as decided after Create, the rewritten retirement passing). Past the
  limit, Create finds no key and starts a new key: narrower, the root's rule passed over, as in the
  store as decided. After Create the authority holds the same carried key, so a later loss of the
  retirement no longer loses the key.

### 2.5 Two gates

**The same edited record written again in both copies** (each gate writing apart, at different
times), then 12 merges: as decided in every cell for every kind, under both variants. KeePassLib's
Synchronize of two copies that each wrote the record again keeps one current version and merges the
histories to 3 items (the two equal written versions in history count once); the imports with
Overwrite if newer, Overwrite existing and Keep existing gave 3 as well, KeePassXC 2.8.0 3, Create
new IDs 2 (the target's own entry; the other arrives as a copy). The gate's next write at the target
wrote nothing again.

**One copy writing the record again while the other holds a later edit of it, both orders**: as
decided in every cell. Where the later edit wins the merge (Synchronize, Overwrite if newer,
KeePassXC 2.8.0 and some imports), the current version fails and the record counts by its history,
the written version among the merged items; the target gate's next write writes it again (history 4,
the written version current). No cell refused, narrowed or widened.

**Across merges the rewrites pile up history items, and stop at KeePass's limit.** Five rounds of an
edit at copy 0, a Synchronize into copy 1, both gates writing, and a Synchronize each way: under
`backup` the record's history was 3, 6, 9, 10, 10 in both copies, the written version current after
every round; under `cleared` 1 in every round; in the control with nothing written again 1, 2, 3, 4,
5 with the current version failing, so that the control reaches the stop at the 11th round's edit
while the rewrite never does. 324 merge cells, both variants and the control: as decided in all.

**A rewrite does not undo a later decision of the other gate.** Gate 1 makes a decision that takes
the record out of its copy with a deletion record (it revokes c1, recreates the authority, removes
pg, sets N to read and write, or allows renewals of c1 again); gate 0 writes the record again, after
that decision and before it. 120 merge cells: 110 as decided, 4 narrower, 6 wider, and **every
cell has the same outcome as the control with nothing written again**. The 6 wider cells are Create
new IDs into copy 0, row 6: gate 1's decision arrives only as a copy. The 4 narrower are Create new
IDs into copy 0 for the rule and the renewal answer, the widening decision arriving only as a copy.
Where the rewrite is later than the other gate's deletion record, KeePassLib's Synchronize keeps the
entry and drops the record, so the written-again record comes back into gate 1's copy; the decision
that took it out (the revocation, the retirement, the removal, the newer version) still holds, and
gate 1's next write takes the entry out again. Of the 120 cells, the record was present after 85
merges; after the target gate's next write it was gone in all but the 10 Create new IDs cells into
copy 0, which never received gate 1's decision.

**Random histories.** `b-random` with the rewrite after every gate act printed the record of the
store as decided under both variants: 1,419 steps, no failure of the model's safety check, the same
outcomes, and no record written again, since none of its random edits was followed by a write at
that file while the edited record counted by its history.

### 2.6 What follows for reading 1

**It holds, with one gate and with two.** No sequence ended wider than the store as decided, and no
write again undid a later write of the plugin's at the other gate. It moves the stop from the 11th
edit since the record was written to the 11th edit since the plugin last wrote anything. Its costs
are the history: under `backup` up to 10 history items of each edited store entry are the plugin's
own rewrites and the edits between them, and two gates pile them up to the limit within four
rounds; under `cleared` the evidence of an edit is gone at the next write. **The form the wording
settles is the same entry's current version set back**; whether the edited version goes into the
history or the history is emptied is not settled by the wording and changes no outcome measured.

## 3. Reading 2: a record that takes access away read from a copy

### 3.1 The classification as run

The narrowest reading: a record counts from a copy only where it can only take away.

| Record | Counts from a copy |
| --- | --- |
| a revocation | yes |
| a retirement | yes, its refusal of the authority; never as a source of the store key |
| a profile removal | yes |
| a rule version that refuses everything (a rule of nothing), and a renewal answer that refuses forever | yes |
| any other rule version or renewal answer | no (section 3.4) |
| an admission, a profile addition, an authority, a summary entry | no |

A counted copy is read as the record under the identifier its content gives, so a deletion record,
a later version naming it, or the record itself present under its own identifier all apply to it as
to the original. Whether such a record is held to the completeness check, which stops the store
where no summary entry restates a record, is not settled by the reading; both were run: **held**,
held to it like any record, and **exempt**.

### 3.2 The 234 merge cells

Cell by cell against the store as decided, every cell that differs:

| History | Create new IDs into the stale copy 1 | Store as decided | Held | Exempt |
| --- | --- | --- | --- | --- |
| H2 a revocation | | wider: c0 admitted | refuses, the whole database | as decided |
| H3 a revocation; the key renewed at the other gate | | wider: c0 admitted | refuses, the whole database | as decided |
| H4 a name added, removed and added again | | as decided | **refuses, the whole database** | as decided |
| H5 a name removed | | wider: c0 holds pf | refuses, the whole database | as decided |
| H6 the authority recreated, deleted, created again, a client paired | | wider: c0 admitted | refuses, the whole database | refuses, nothing served |
| H7 a rule widened, narrowed and emptied at N, the root narrowed to read | | wider: root read and write | refuses, the whole database | **wider: root read and write** |
| H8 a rule at N removed, the root emptied | | wider: root read | refuses, the whole database | **wider: N read** |
| H9 the same rule changed apart, read and read and write | | wider: N read and write | **wider: N read and write** | **wider: N read and write** |
| H10 a rule narrowed at one gate, widened at the other | | wider: N read and write | refuses, the whole database | as decided |
| H12 renewals refused forever | | wider: renewal asked | refuses, the whole database | as decided |
| H13 a copy from before a Recreate | | wider: c0 admitted | refuses, the whole database | refuses, nothing served |

Tallies: store as decided 221 as decided, 3 narrower, **10 wider**; held 220, 3, 10 refusing,
**1 wider**; exempt 226, 3, 2 refusing, **3 wider**. Every other cell, the other 224 and all 90 of
a user node, is the same in all three.

**Of the ten wider cells, held closes 9 and exempt closes 7.** Held turns each into a whole-database
refusal, which lasts until the store is sealed again (section 4), and newly refuses H4, which the
store as decided read as decided: the removal counts from its copy, and the summary entry that
restates it arrived only as a copy. Exempt closes H2, H3, H5, H10 and H12 as decided and H6 and H13
as nothing served (the retirement counts from its copy and the new authority arrives only as a
copy); H8 stays wider at another node, since the root's rule of nothing now counts while N's rule
removal does not; H7 and H9 stay wider, since a rule narrowed to read only does not count from a
copy (section 3.4).

### 3.3 The other parts, and copies that are not an import

- **`b-deletions` (144 cells), `b-clear` (210), `b-repair` (96 in the modes with no repair)**: every
  cell the same as in the store as decided, under both variants. The imports with Create new IDs in
  `b-repair` already ended as decided at the gate.
- **`b-dedupe`, `b-dupes`**: every outcome the same. After Duplicate Entry and Delete Duplicate
  Entries, which deletes the original with a record and keeps the copy, a revocation, a removal, a
  rule of nothing and a refused renewal count from the copy (one element more) and read as decided,
  as they did before through the original's deletion record.
- **`b-random`, 300 histories**: store as decided 171 files wider, 406 as decided, 93 narrower, 54
  refusing; held 169, 406, 93, 56, with one gate act refused because the reading stopped the store;
  exempt 169, 407, 93, 55. The model's safety check failed in none.

The part `copies`, for the five kinds that count and four that never do, every case read as decided
for the five but where the table notes otherwise, under both variants:

| Case | What the reading does |
| --- | --- |
| Duplicate Entry; the duplicate in another group (copy and paste stood in for); in the recycle bin | nothing: the original is present, so the copy is the same record once |
| Duplicate Entry, then Delete Duplicate Entries; the original deleted for good, with and with no record | the copy counts, as decided |
| the original deleted with a record, the copy carried into the other copy and deleted there, then KeePassXC 2.8.0 brings it back beside its own deletion record | the copy counts: its deletion record names the copy's identifier, not the record's, so nothing reads it |
| the same, Synchronize instead | the copy is gone; the original's deletion record still refuses, as decided |
| a narrowing duplicated, then replaced at copy 0 by a widening version, then every merge both ways | **the replaced narrowing does not come back**: the widening version names the original's identifier, under which the copy is read, so the copy is dead wherever the widening version is read. As decided in copy 0 and after Synchronize and KeePassXC 2.8.0 both ways and Create new IDs into 0; narrower after Create new IDs into 1, where the widening version itself arrives only as a copy and the read there counts no copy |
| the same cases for an admission, an addition, the authority and the summary entry | the copy never counts: once the original is gone the read is narrower, nothing served or refusing, as in the store as decided |

**Cost in narrowing beyond the store as decided**: H4 under held, and the two nothing-served cells
under exempt, which were wider before. No copy that is not an import narrowed anything.

### 3.4 Where the classification is not clean

Named, not decided. The run counts what the first column states; the second states what the other
reading would change.

| Case | The other reading |
| --- | --- |
| **A rule narrowed to something other than nothing** (read only): a value that ignores the parent, so it can give where the parent gives less, and narrows only relative to what it replaces | counted, by reasoning (not run), it would close H7 and H9, each reading read only; whether it can only take away depends on the versions it names, which a copy does not carry |
| **A rule removed** (back to inheriting): takes away where the parent gives less, gives where it gives more | counted, by reasoning, it would close H8 at N; in general it can widen |
| **A retirement that settles which authority is served**: where two authorities stand and nothing is served, retiring an authority serves the other | not counted, by reasoning, H6 and H13 would stay wider, as in the store as decided; no cell here had two standing authorities at the import |
| **A retirement that holds the store key**: its refusal takes away, its key gives everything the key opens | counted as a key source, a copy of the key carrier would make every record under that key readable from a copy; not run |
| **A summary entry**: its refusal items take away; its version items and its presence (it completes the store) can give | its refusal items counted alone would add refusals restated for records deleted by hand; not run |
| **A version that narrows one profile and widens another** | not expressible in this probe, whose rule holds one value per node; under the narrowest reading it does not count |
| **The completeness check** | both run (section 3.2) |

### 3.5 The combination with reading 1

A record counted only from a copy does not count by its history, so the decided rewrite does not
reach it: as decided, the plugin writes nothing again for it, and under held the store stays stopped
until it is sealed again. What would have to be decided is whether the plugin writes such a record
under the identifier its content gives. That was run on the ten row-6 histories: after the import,
the gate's write; then each record counted from a copy written as a new entry under its own
identifier, where the file holds neither an entry nor a deletion record under it; then a summary
entry restating them (the model's Seal again standing for the summary entry such a write carries);
then Synchronize with the newer copy both ways.

| History | Held: after the import, then after the write | Exempt: after the import, then after the write |
| --- | --- | --- |
| H2, H3, H5, H10, H12 | refuses the whole database, then as decided | as decided, as decided |
| H6 | refuses the whole database, then nothing served | nothing served, nothing served |
| H7 | refuses the whole database, then **wider: root read and write** | wider, wider |
| H8 | refuses the whole database, then **wider: N read** | wider, wider |
| H9 | wider, nothing to write | wider, wider |
| H13 | refuses the whole database, then **no store key held** | nothing served, then no store key held |

Synchronize with the newer copy afterwards gave as decided in every history, both ways, both
variants. **H13 is a cost of counting a retirement from a copy**: written under its own identifier,
the retirement of the authority the stale copy served made the next write take that authority's
entry out as retired, and it was the stale copy's only carrier of the store key; the new authority
had arrived only as a copy. The store starts again. A plugin whose writes take out what its read
finds retired would do the same at its next write even without writing the copy under its own
identifier; the probe's write path does not prune by the reading of copies, so that was not run.

## 4. Every fail-open sequence in this run

1. **Row 6 under reading 2**: H9 (held), and H7, H8 and H9 (exempt), Create new IDs into the stale
   copy; under held, H7 and H8 again once the store is sealed with the copied records written under
   their own identifiers.
2. **Row 6 in the two-gate parts of reading 1**: Create new IDs into copy 0, 6 cells of `prune`, the
   same in the control; the rewrite neither causes nor closes them.
3. Every fail-open sequence of `measured-decided-store.md` section 5 not run again here stands.

Nothing in `one`, `alternate`, `two`, the rewrite's `b-random`, `b-deletions`, `b-clear`, `b-repair`
or `copies` ended wider than the store as decided.

## 5. Not measured

A window, a phone, an emulator or a second KeePass with the plugin; writing again as a new entry
beside the edited entry; KeePassXC releases other than 2.8.0; the slow key derivation; a history
limit other than 10 and 0 under the rewrite; KeePass's own copy and paste; a version narrowing one
profile and widening another; two standing authorities settled by a copied retirement; a copy of the
key carrier read as a key source; a summary entry's refusal items read from a copy; the plugin's own
Seal again after a stop caused by a copy; a write that prunes by the reading of copies; the model's
searches under either reading; and everything `measured-decided-store.md` lists as not measured.
