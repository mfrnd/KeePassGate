# Measured: the recommended store as a whole

The store `EVALUATION.md` section 4 recommends, run as a whole on 2026-11-16: the design of the
option `crdt` with the changes of its section 4.1, configured in the design's executable model and
built over real files through KeePassLib 2.62 and `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0.
Every figure here comes from the family `spikes\storage\probes\store-as-a-whole\` of the branch
`spikes/storage-survival`, whose `RUN.md` has the commands, the record of the run quoted here
(`recorded\`) and a second run's value of every figure. Nothing ran in a window, on a phone or in
an emulator.

**How to read it.** "Wider" is a client reaching more than the last decision allowed; "refuses" is
the plugin refusing what the last decision allowed, with its scope; "narrower" is a file granting
less than the last decision without refusing as a whole. The oracle is the model's: every decision
a gate made, carried on a perfect transport, and in the model's searches what was delivered to the
file in question. Three sequences were **added from the desktop measurement**
(`measured-desktop-windows.md` sections 5 and 6) after the brief, and are marked so where they
appear.

**What the configuration followed.** `EVALUATION.md` as it stood at 12:50 UTC (SHA-256
`39f58d30…aebffd47`). Its section 4.1 holds what the brief lists, and two more items the brief does
not name: change 3 (an entry that fails and is not under a plugin identifier is passed over, never
damage), which the design's model already reads that way, and the press of design 9 that removes
the versions of nodes that are gone, left out, which no part here writes.

## 1. The store as configured

| Part | In the model | In the real files |
| --- | --- | --- |
| Every datum an entry written once in the plugin's group, under a check with a store key | the design's elements, each under a key label | an entry in the group `KPG/STOR…`: `KPG.Kind`, `KPG.Data` as a flat JSON object of strings, `KPG.Mac` an HMAC-SHA256 under the store key; identifier: 3 bytes of the kind's keyed tag, 5 of a keyed hash of the subject, 2 of height, 6 of the MAC |
| An entry counting wherever it sits (change 1) | the design's model has no place; the extended model reads the bin for every kind | `read.py` reads every entry in the tree, the bin included |
| Heads restating what is in force | the design's head | a head entry with one `KPG.S.nnnn` string per refusal, removal and standing version, under the MAC |
| No limit of 64 (change 2) | the model holds none | the head names every head it replaces |
| A damaged store refusing for the whole database until Seal again | the design's four conditions | the same four in `read.py` |
| Change 3 | the design's reading | an entry failing its check counts as damage only under a plugin identifier of a key held |
| No mark, no link, no pin, no Adopt (decision 11) | switch `mark` off; Seal again writes no refusal for a marked node | nothing is written on a user's node |
| No witness (decision 15) | switches `T` and `witness` off | no deletion record names no entry |
| A revocation names the client's key (decision 20, change 5) | switch `revkey`; a renewal under the same key is an act | `revoked` carries the client's key; the identifier's hash is over it |
| The store key carried over a Recreate, and in the retirement after a Delete with no Create (decision 12, change 6) | Recreate carries the key (the design); switch `keyretire` | `retired` carries the key in a protected `KPG.Data` when no authority follows |
| A rule from a node's dialog written at the next save (decision 13, change 4) | switch `pending`, and an act for KeePass ending before a save | not on files: no file shows a rule waiting in memory |
| Several gates (decision 20) | every file is a gate that writes | every copy is written through the gate's write path |
| The repair after a Synchronize (decisions 18 and 19, change 7) | switch `repair`: after a Synchronize at a gate, what was in force before and is not after is written again, as entries that take access away, with a head | the same, written into the file before its save, and the merged file then written to the other file too (section 3.7) |

Two choices the decisions leave to an implementation, made here and named: the repair restates an
earlier rule as that rule **met with what stands after the merge**, so that it never grants (a
first form that restated the earlier rule itself granted alone where nothing stood after the merge:
`rule 2 root inherit; revoke 2 c0; del 2 (authority) with a record; create 2; Synchronize 1 from 2`
gave read at the root in file 1); and it restores a name only where a removal of that name held it
off the list before.

## 2. A. The model

**The copy is the design's model.** With every new switch at its default the copy printed the
design's `results-full.txt` line for line but the running time (187 lines). Every addition is
listed in `RUN.md`.

### 2.1 The search as the design ran it

Twelve configurations, 20,000 random histories each over two and over three files, the design's
seeds, the store's acts added to the alphabet. Counts are histories; "accepted" are residues a
decision accepts, counted apart and ending no history.

| Configuration | 2 files: `safety`, `one-loss` | 2 files: accepted copy wider, accepted rule lost before a save, repair narrower | 3 files: `safety` | 3 files: accepted, accepted, repair narrower | The design as written, `safety` (2 and 3 files) |
| --- | --- | --- | --- | --- | --- |
| A merges only | 0, 0 | 17, 104, 49 | 0 | 6, 67, 32 | none, none |
| B deletions with a record | 3, 0 | 16, 86, 23 | 2 | 6, 71, 16 | none, none |
| C and with no record | 2, 1 | 7, 99, 16 | 2 | 0, 89, 21 | 2, 1 |
| D records dated before the entry | 1, 0 | 11, 100, 31 | 0 | 6, 82, 19 | 2, none |
| E records cleared | 1, 0 | 7, 106, 29 | 0 | 7, 74, 16 | 1, none |
| F hand edits, imports, planted entries | 5, 0 | 6, 86, 22 | 0 | 3, 60, 16 | none, none |
| G a copy stripped of its reference | 2, 0 | 8, 101, 20 | 0 | 5, 71, 12 | none (`copy-cap` 2), none |
| H a whole file over another | 2, 0; overwrite 167 | 9, 93, 27 | 1; overwrite 244 | 3, 76, 17 | 1 and 2; overwrite 251 and 404 |
| I everything | 2, 0; overwrite 34 | 10, 88, 25 | 1; overwrite 66 | 2, 77, 8 | 1, none; overwrite 41 and 105 |
| J Synchronize and Keepass2Android, records | 2, 0 | 13, 104, 49 | 1 | 6, 84, 43 | none, none |
| J2 the same, every deletion | 2, 0 | 12, 92, 74 | 0 | 6, 64, 43 | none, none |
| K the same, edits, imports, planted | 2, 0 | 17, 91, 70 | 2 | 5, 72, 47 | none, none |

"Overwrite" is the design's `rollback-by-overwrite`, row 1 of section 6. Merges alone converge in
every history to one state; in A, 35 and 28 histories converge to a state narrower than the oracle,
each by the repair.

**Every `safety` and `one-loss` cell, by the shortest history the search printed for it** (the
other histories of a cell were counted, not read; the search without hand losses of the authority's
entry, below, bounds them). Two groups.

- **The authority's entry deleted or edited by hand, then Create.** B, C, E, F, H, J, J2 and K over
  two files, B, H, I, J and K over three: with no authority and no retirement holding the key, the
  store holds no key, and Create starts a new key under which nothing earlier verifies. The oracle
  still holds the old decisions, so a rule set again (`del 0 (authority) with a record; create 0;
  rule 0 root rw`) reads as wider than the old rule, and a renewal answer under the old key reads as
  lost (`renew 0 c0 forever; del 0 (authority) with a record; create 0`). This is row 17's cost,
  which decision 9 accepts and `EVALUATION.md` 4.3 states: everything under the lost key is entered
  again. The renewal is asked of a client whose certificate chains to an authority no longer served,
  which no client reaches.
- **Everything else is rows 2 and 3 of `EVALUATION.md` section 6.** Both carriers of a decision
  deleted where it was made, then a merge that carries no deletion record: a revocation
  (`revoke 0 c0; del 0 (head) with a record dated before it; del 0 (revocation) the same; KeePassXC
  1 from 0`), a renewal refused forever (G, through an import method), a name removal (D and H), a
  retirement after Delete with its key (`delauth 0; del 0 (retirement) with a record; del 0 (head)
  dated before; KeePassXC 1 from 0`, I and C over three files). And row 3's first shape (C):
  `prem 1 pf; del 1 (head) with no record; Overwrite-family import 0 from 1; del 0 (the removal)
  with no record`.

**The same search with no hand deletion or edit of the authority's entry** (`store-search-noauth`):
`safety` in three of 24 cells, one history each, D and E over two files and H over three, and
nothing else, `one-loss` included; Synchronize and Keepass2Android only (J, J2, K) end in no
failure, as in the design. All three are row 2: `prem 1 pf; del 1 (head) with a record; del 1
(the removal) with a record dated before it; Overwrite-family import 0 from 1` gives c0 the removed
name in file 0, and `revoke 0 c0; del 0 (head) with no record; del 0 (the revocation) with a
record; KeePassXC 1 from 0` admits c0 in file 1.

**The exhaustive part**, after the design's divergence, each rule saved: 11,110, 47,988, 40,494 and
27,930 histories, no failure; the repair wrote in 208 and 172 of them. **The laws of the join**: 0
violations in 10,000 pairs.

**Merges alone (A)** end in no failure; 35 histories over two files and 28 over three converge to a
state **narrower** than the oracle, each by the repair (section 2.6).

### 2.2 The extended model

The survival critique's extended model (where an entry sits, the bin, KeePassDroid's unchanged
save), every entry counted wherever it sits, its eight configurations and two with KeePassDX's
merge, 8,000 histories a cell: failures in P1 (2), P2 (1), F (1 and 1), F2 (1) and DX (1), every
shortest history an authority's entry deleted by hand and Create, as above. No other failure.

### 2.3 The design's replays

The design's 20 replays under the store (each rule saved, so that a merge after it carries it):

| Replay | The design as written | The store | Undone in turn: mark, witness, key revocation, key in retirement, rule at save, several gates, repair |
| --- | --- | --- | --- |
| 1, 2, 4, 7, 8, 11 to 14, 17, 19, 20 | holds (2, 7, 8, 17, 19 incomplete; 14 nothing served) | the same | the same |
| 3, 9, 16 | fails | fails | fails |
| 5, 6 | holds | holds | holds, but fails with one gate: the replay writes at file 1, which cannot write there |
| **10**: newest version and head deleted with records, records cleared, **Seal again**, then Synchronize with the stale copy | holds | **fails**: N gives read and write | holds with the mark |
| **15**: a copy of a sealed node, a save, the copy stripped | holds | **fails** | holds with the mark |
| **18**: both carriers of a revocation deleted with no record, Seal again, KeePassXC into a stale copy | holds | **fails**: c0 admitted | holds with the witness |

Through the model's checks after every step, the store fails `safety` in 3 and 9, and
`sealed-after-a-loss` in 10 and 18; 15 and 16 are `accepted:copy-wider`. So the store has the
design's three failures, and three more: the two parts left out by decision are each what held a
replay (10 and 15 the mark, 18 the witness).

### 2.4 The sequences of `EVALUATION.md` section 6

Each replayed under the store; the outcome against every decision any gate made.

| Row | Sequence as replayed | The store | With a decision undone, where it differs |
| --- | --- | --- | --- |
| 2 | a revocation, its entry and the head deleted with records where made; KeePassXC into the other copy | **wider**: c0 admitted | |
| 2 | the same through an import method; a name removal the same way | **wider**: c0 admitted; c0 holds the removed name | |
| 2 | the same through KeePassDX, or through Synchronize | refuses, the whole database | |
| 2 | the same through Synchronize, the deleting clock behind (records dated before the entries) | refuses, the whole database | |
| 3 | the entry deleted where made, KeePassXC delivers the head alone, the head deleted there with no record | **wider**: c0 admitted | |
| 3 | a narrowed rule's new version and head removed by script after a KeePassXC merge | **wider**: N gives read and write | |
| 3 | the same, then a Synchronize with the copy that holds the decision | as decided | |
| 3 | two deletions with no record after a KeePassDX merge | refuses, the whole database | |
| 4 | records cleared while the loss stands, then the stale copy synchronized | **wider**: N gives read and write | |
| 4 | the same with **Seal again** pressed before the stale copy arrives (run without the mark for the first time) | **wider**: N gives read after the press, read and write after the merge | as decided with the mark |
| 6 | a copy of a narrowed node beside it under a granting root | accepted: the copy gives read and write, N nothing | the mark caps the copy |
| 7 | a newer copy imported into a stale copy with Create new IDs | **wider**: c0 admitted | |
| 9 | a version made with the store key, merged in by Synchronize, from a stale copy | as decided at the gate: the grant meets the narrowing it never saw | |
| 9 | the same from a current copy | as decided at the gate, by the repair; the file it was made in is **wider** | **wider** at the gate without the repair |
| 10 | a revocation at gate 0, gate 1 not yet synchronized | **wider** at gate 1 until it synchronizes | |
| 10 | gate 1 renews the revoked key, then Synchronize either way or KeePassXC | as decided: the renewal is refused | **wider**, c0's renewed certificate admitted, with the revocation by certificate |
| 11 | a client's record deleted by hand to take it away; KeePassXC into the other copy | as decided by the store: no revocation was made, and copy 1 keeps the record, since KeePassXC carries no deletion record | |
| 14 | a revocation and the head deleted with no record; Seal again; KeePassXC into a stale copy | **wider**: c0 admitted | as decided with the witness |
| 14 | the same with records | as decided | |
| 14 | a node's first rule, nothing, and the head deleted with no record; Seal again | **wider**: N gives read, inheriting | |
| 14 | the same for a rule that had an earlier version pruned with a record | as decided: the earlier version's record makes the press write the refusal | |
| 14 | renewals refused forever, the answer and the head deleted with no record; Seal again | **wider**: c0's renewal asked | |
| 15 | the head deleted for good; a refusal's `KPG.` string edited | refuses, the whole database | |
| 16 | Create new IDs, Delete Duplicate Entries, a third copy synchronized | refuses, the whole database | |
| 17 | the authority's entry deleted for good, the deletion synchronized | refuses, nothing served (no store key) | |
| 18 | a second authority arrives | refuses, nothing served | |
| 19 | a rule narrowed at a node's dialog, KeePass ends before a save | accepted: the rule is gone | not reached with the rule saved at OK |
| 20 | read at gate 0, read and write at gate 1, apart; Synchronize | the meet, read: the oracle's value for two decisions made apart, narrower than the later of the two | |
| 21 | a name removed at a copy, removed and added again at the other | the removal wins | |
| | the authority deleted on the tab, none created, a later Create; a granting root after it | as decided: every rule kept | without the key in the retirement, narrower (every rule passed over), then **wider**: the root set again gives N read and write, its narrowing lost with the key |
| | **the retirement that holds the key deleted for good by hand**, then Create; a granting root after it | narrower (every rule, name and refusal passed over); then **wider**: N gives read and write, its narrowing lost with the key | |
| | a widening, a name added again, renewals allowed again, each at a gate that saw the earlier decision, then Synchronize at the other gate | **narrower**: the repair writes the earlier state again | as decided without the repair |
| | a copy from before a Recreate synchronized into a copy after it | narrower, by the repair | as decided without the repair |

Rows 5, 12, 13 and 22 need a tree, a recycle bin chosen by time, a save in progress or the formal
critique's model, none of which the design's model holds; they were not replayed.

### 2.5 The cost of each change taken by decision

The store and the store with one change undone, over the same 20,000 histories a cell, the design's
seeds and configurations, with no hand loss of the authority's entry. Each cell is `safety`
histories of 20,000; in brackets, where the change moves them: accepted copies wider, accepted rules
lost before a save, files the repair made narrower.

| Configuration | The store | Mark kept | Witness kept | Revocation by certificate | No key in the retirement | Rule written at OK | One gate | No repair |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| B, 2 files | 0 (13, 111, 20) | 0 (0 copies) | 0 | **27**, and 1 after a press | 4 | 0 (65, 0, 92) | 0 (10, 45, 15) | 0 (repair 0) |
| C, 2 files | 0 (12, 96, 21) | 0 (0) | 0 | **28** | 1 | 1 (58, 0, 103) | 1 | 0 (0) |
| D, 2 files | 1 (11, 102, 29) | 1 (0) | 1 | **21** | 2 | 0 (64, 0, 116) | 0 | 1 (0) |
| E, 2 files | 1 (8, 107, 38) | 1 (0) | 1 | **21** | 4 | 1, and 1 after a press (54, 0, 109) | 1 | 1 (0) |
| F, 2 files | 0 (9, 96, 21) | 0 (0) | 0 | **19** | 0 | 0 (47, 0, 83) | 0 | 0 (0) |
| H, 2 files | 0 (9, 90, 27) | 0 (0) | 0 | **16** | 2 | 2 (52, 0, 98) | 0 | 0 (0) |
| I, 2 files | 0 (11, 96, 36) | 0 (0) | 0 | **15** | 3 | 0 (58, 0, 102) | 0 | 0 (0) |
| J2, 2 files | 0 (12, 80, 85) | 0 (0) | 0 | **20** | 3 | 1 (57, 0, 282) | 0 | 0 (0) |
| C, 3 files | 0 (3, 90, 18) | 0 (0) | 0 | **25** | 0 | 1 (38, 0, 101) | 0 | 0 (0) |
| I, 3 files | 0 (4, 71, 9) | 0 (0) | 0 | **22** | 0 | 0 (37, 0, 97) | 0 | 0 (0) |

The shortest history of each undone change: **revocation by certificate**, `revoke 0 c0;
renewcert 1 c0; KeePassXC 0 from 1` admits c0's renewed certificate (3 steps, every merge program
among the shortest histories); **no key in the retirement**, `renew 0 c0 forever; delauth 0;
create 0` asks the renewal again and `delauth 1; create 1; Synchronize 0 from 1; rule 0 root rw`
gives the root read and write where the old rules are passed over (3 and 4 steps); **rule written
at OK**, row 2 and row 3 shapes the waiting rule had kept out of reach (`rule 0 root none; del 0
(the version) with a record; del 0 (head) with a record; KeePassXC 1 from 0`, 4 steps).

### 2.6 What the repair closes, and what it narrows

Of 120,000 histories over six configurations, 7 end wider without the repair, and the repair closes
**none** of them (`store-repair-closes`). The search's own counts agree: the repair closed a
widening in 1 history of the 480,000 of section 2.1 (F, two files), and made a file narrower than
the oracle in 8 to 74 histories of 20,000 a cell (`info:repair-narrower`). The narrowing has a
single shape: a later decision made at another gate that saw the earlier decision, arriving by
Synchronize, is taken for a loss and the earlier state is written again beside it: a rule widened,
a name added again, renewals allowed again (`rule 0 N none; Synchronize 1 from 0; rule 1 N rw;
Synchronize 0 from 1`: N gives nothing at gate 0, where the last decision gives read and write).
The value stays the narrower until the operator settles it. The only case found that the repair
closes is row 9 (section 3.7).

## 3. B. Real files

### 3.1 Every merge, both directions (B.1, B.3, B.4)

Thirteen histories over two copies, each ending in KeePassLib's Synchronize, Overwrite if newer,
Overwrite existing, Keep existing and Create new IDs and `keepassxc-cli merge -s` of the four
releases, into either copy: 234 cells, the file each merge wrote read by `read.py`. Copy 1 is the
copy that did not take the last decisions, unless both changed.

| History | Every cell but those named | The cells named |
| --- | --- | --- |
| H1 an admission at copy 0 | as decided | Create new IDs into 1: narrower, the new client not admitted |
| H2 a revocation by key | as decided | Create new IDs into 1: **wider**, c0 admitted |
| H3 a revocation at gate 0; at gate 1, not yet told, the revoked key renewed | as decided: the renewed certificate is refused in every other cell, both ways | Create new IDs into 1: **wider**, c0 admitted |
| H4 a name added, removed, added again | as decided | |
| H5 a name removed | as decided | Create new IDs into 1: **wider**, c0 holds the name |
| H6 the authority recreated (key carried), deleted with none after, created again, a client paired | as decided | Create new IDs into 1: **wider**, c0 admitted under the retired authority |
| H7 a rule set, widened, narrowed and emptied at N, narrowed at the root | as decided | Create new IDs into 1: **wider**, N gives read |
| H8 a rule at N removed, the root emptied | as decided | Create new IDs into 1: **wider**, the root gives read |
| H9 the same rule changed apart, read and read and write | as decided (the meet, read) | Create new IDs into 1: **wider**, N gives read and write |
| H10 a rule narrowed at one gate and widened at the other, apart | as decided (the meet) | Create new IDs into 1: **wider**, N gives read and write |
| H11 an admission at each gate, apart | as decided | Create new IDs, both ways: narrower, the other gate's client not admitted |
| H12 renewals refused forever | as decided | Create new IDs into 1: **wider**, the renewal asked |
| H13 (B.3) a copy from before a Recreate and a copy after it | as decided, both ways | Synchronize into 1: narrower, by the repair (N's later rule taken for a loss); Create new IDs into 1: **wider**, c0 admitted |

**220 as decided, 4 narrower, 10 wider, every wider cell Create new IDs into the copy that lacks the
decisions** (row 7): the arriving entries are under identifiers the gate never gave them and are
passed over, so the stale copy keeps its state. Nothing else, in any program or direction, ends
wider; KeePassXC's merges, which carry no deletion record, end as decided in every cell, because no
history here deletes a plugin entry.

**The slow derivation** (both copies saved under Argon2d with 200 iterations and 64 MiB just before
the merge, so that KeePassXC's two loads fall in different seconds): all 32 cells of the four
histories run (H3, H6, H13 and the copy of a user node, every KeePassXC release, both directions)
came out as the same cells under the fast derivation: 24 as decided, and in the 8 of the copy N
nothing and the copy read. The store keeps nothing in custom data, so no result here depends on the
two loads of a merge falling in different seconds.

### 3.2 A user's node under a rule

N carries its own rule (nothing) under a root that grants read and write. Moved to another group,
deleted to the bin, restored, or deleted for good in one copy and merged by every method both ways:
**N gives nothing in every cell** where it exists, 90 cells. A copy of N, by Duplicate Entry or by
Create new IDs, is read under its place: read and write under the root, or read where the repair
had narrowed the root (row 6, accepted by decision 16). A deletion for good comes back through
KeePassXC with its rule, since KeePassXC applies no record.

### 3.3 Create new IDs, then Delete Duplicate Entries (B.2)

In both orders the originals that had copies are deleted with records, the user's entry N among
them, the copies are passed over as entries under no plugin identifier, and the store holds no key:
**refuses, nothing served**, in the file and in a third copy synchronized with it (row 16).

### 3.4 Each kind of record deleted by hand (B.5)

Copy 0 revokes c0, recreates the authority, pairs c1, adds the name pg, removes pf, narrows N and
refuses c0's renewals forever; copy 1 synchronizes; then one entry of copy 0 is deleted. The merges
are Synchronize and KeePassXC 2.8.0, each into either copy, after Seal again where the store was
incomplete: 144 merges, 126 as decided, 12 narrower, 6 refusing, **none wider**.

| Entry deleted | To the bin | For good, with a record | For good, no record | To the bin, the bin emptied |
| --- | --- | --- | --- | --- |
| a revocation, a retirement, a removal, a rule version, a renewal answer | as decided | as decided: the head restates it | as decided | as decided |
| an admission record (c1) | as decided | narrower: c1 refused, and after Synchronize either way and KeePassXC into 0; after KeePassXC into 1, which carries no record, copy 1 still admits c1 | narrower; every merge with the stale copy brings it back | as with a record |
| a profile addition (pg) | as decided | narrower: c1 lacks pg, merge by merge as for the record | narrower; every merge brings it back | as with a record |
| the authority | as decided | **refuses, nothing served** (no store key); Seal again does not apply; Synchronize either way leaves both copies without a key; KeePassXC into 0 the same; KeePassXC into 1 as decided | refuses; every merge with the stale copy brings it back | as with a record |
| the head | as decided | refuses, the whole database; **Seal again**: as decided, 10 elements and a head; every merge after it as decided | the same | the same |

Change 1 is what keeps every deletion to the bin as decided: the entry counts in the bin.

### 3.5 Every deletion record removed at once (added from the desktop measurement)

After each deletion of section 3.4, and after six prunes (a rule narrowed, a client revoked, the
authority recreated, a name removed, renewals allowed again, the authority deleted with the key in
its retirement), every deletion record removed as Database Maintenance's "Deleted objects
information" does, then a stale copy that still holds what was deleted merged by Synchronize both
ways, by Overwrite if newer, and by KeePassXC 2.8.0 both ways: **210 merges, none wider**: 209 as
decided and 1 narrower (the repair, after renewals allowed again). Read before the merge, the
clearing changes nothing that a single deletion had not: a deleted head still leaves no head in
force, a deleted record or addition still refuses that client or name. What does widen is two losses
and then the clearing: both carriers of a narrowed rule deleted with records, the records cleared,
the stale copy synchronized (row 4), wider in every mode of section 3.7.

### 3.6 Delete Empty Groups and Delete Duplicate Entries (added from the desktop measurement)

The recommended store uses no subgroup, and holds two entries with equal strings only after a
person copies plugin entries: every element carries a nonce, so two gates revoking the same client
write two different entries.

| On the store as copy 0 holds it (c0 revoked, c1 paired, pf removed, pg added, N narrowed, c0's renewals refused) | Deleted by the command | The read | After Seal again |
| --- | --- | --- | --- |
| Delete Empty Groups | 2 groups: the empty user group Other and the empty recycle bin; the plugin's group holds entries and stays | as decided | not needed |
| Delete Duplicate Entries | nothing | as decided | not needed |
| two gates revoke c1 apart, Synchronize, Delete Duplicate Entries | nothing | as decided | not needed |
| Duplicate Group of the plugin's group with " - Copy" on each title, Delete Duplicate Entries | nothing: the titles differ | as decided; the 9 copies passed over | not needed |
| **Duplicate Group without the suffix**, Delete Duplicate Entries | **9 entries, every original** (the older of each pair) | **refuses, nothing served**: no entry of the store is left under its own identifier, so no store key | does not apply |
| Duplicate Entry without the suffix of a revocation, a removal, a rule version or a renewal answer, then Delete Duplicate Entries | the original | as decided: the head restates it, and its record names its subject | not needed |
| the same of an admission record | the original | narrower: c1 refused | not needed |
| the same of a profile addition | the original | narrower: c1 lacks pg | not needed |
| the same of the authority | the original | **refuses, nothing served** | does not apply |
| the same of the head | the original | refuses, the whole database | as decided |

On an empty database the same command also deleted the plugin's group itself, empty, beside the bin
and Other (a check beside the run, not recorded).

### 3.7 The repair after a Synchronize (B.6; the import cases added from the desktop measurement)

Each history ends with KeePass's Synchronize at gate 0, done in each of the modes below; the cell
is the file of gate 0, and for R8 the other file too. The desktop measurement found the plugin told
of a synchronize before the merge, the merged state in memory at the first `FileSavingPre` of the
synchronize's save, and an entry added there reaching both files; with KeePass's trigger system
off, no announcement; File, Import raises no event at all. So the mode "no repair" is also a
synchronize with the trigger system off, and a repair made at the save's own event writes what the
first mode writes.

| History | Repair, written to both files | Repair, the other file written before it | No repair (also: trigger system off) | File, Import with Synchronize's method or an Overwrite method or Keep existing: no repair / repair at the next save | Import with Create new IDs |
| --- | --- | --- | --- | --- | --- |
| R1 records cleared after an unsettled loss, the stale copy synchronized (row 4) | **wider**: N read and write | **wider** | **wider** | **wider** / **wider** | refuses, the whole database |
| R2 copy 1 removes a narrowed rule's new version and head by script after a KeePassXC merge | as decided | as decided | as decided | as decided / as decided | as decided |
| R3 a revocation's entry and head deleted in copy 1 with records dated before them | as decided | as decided | as decided | as decided / as decided | as decided |
| R4 a removal's entry and head deleted in copy 1 by script | as decided | as decided | as decided | as decided / as decided | as decided |
| R5 a rule widened at gate 1, which saw the narrowing | narrower | narrower | as decided | as decided / narrower | narrower (the widening is not read) |
| R6 a name added again at gate 1, which saw its removal | narrower | narrower | as decided | as decided / narrower | narrower |
| R7 renewals allowed again at gate 1, which saw the refusal | narrower | narrower | as decided | as decided / narrower | narrower |
| R8 a version granting read and write at N, made with the store key in a current copy (row 9) | as decided, **both files** | as decided; the other file **wider** | **wider**, both files | **wider** / as decided | as decided |

So the repair closed the only sequence of the eight that ends wider in its absence and that a
repair can see (R8), and only where it reaches both files; it did nothing for R1, where the store
was already incomplete before the Synchronize and held no value of N to restore; and it made three
decisions taken at another gate narrower. An import, which no event announces, is repaired only by
a comparison at the person's next save; through Create new IDs nothing arrives that the read counts.

### 3.8 The head as the store grows (B.7)

One gate with 10 clients, 5 names and 20 ruled nodes writes 1,000 times (rules changed 60 in 100,
renewal answers 15, a client revoked and another paired 10, names removed or added 15), through the
model's write path; the store is then written into a real file and read by `read.py`.

| After | The head: bytes of its strings, strings, its list of replaced heads | Entries in the store, bytes of their strings | Longest single string (characters) | Longest `after` list of a version | Deletion records | File (bytes, both runs) | `read.py` (ms, Python, both runs) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| the setup's 33 writes | 4,280; 24; 33 | 38; 12,130 | 1,165 | 0 | 33 | 8,055; 8,055 | 1.8; 3.5 |
| 10 more writes | 5,064; 27; 44 | 39; 13,295 | 1,528 | 1 | 54 | 8,871; 8,887 | 1.0; 1.1 |
| 100 | 11,951; 44; 137 | 57; 25,838 | 4,598 | 7 | 222 | 16,039; 16,039 | 1.3; 1.9 |
| 1,000 | **81,551; 272; 1,144** | 285; 157,631 | **37,830** | 36 | 2,008 | 90,167; 90,183 | 13.9; 16.3 |

The head grows by an identifier a write (change 2), by a restated refusal for every revocation
(change 5: none is pruned) and by the `after` list of every standing version, which also has no
limit. Its `KPG.Data` alone is the longest string at 1,000 writes, 13 times the longest string the
phone bases held (2,861 characters, `RESULTS.md` 7.1). The read stays in milliseconds once a
deletion record's keyed tag and hash are looked up rather than tried against every subject (the
first form of `read.py` took 1.4 s at 1,000 writes; not recorded).

## 4. C. The bridge

At every step of B, and over 300 random histories (`b-random`: 1,416 steps over two and three
copies, every program and fault of B but the window's), the model's prediction for the file was held
to the file entry by entry and record by record, and the model's read of what the file holds to
`read.py`'s read of the file.

| Part | Steps where an entry or a record disagreed | Steps where the read differed |
| --- | --- | --- |
| `b-merges` (234 cells and 90 of a user node) | 0 | 0 |
| `b-slow` (32 cells) | 0 | 0 |
| `b-dedupe` | 2 | 6 |
| `b-deletions` | 0 | 18 |
| `b-clear` | 0 | 15 |
| `b-dupes` | 9 | 2 |
| `b-repair` | 0 | 0 |
| `b-random` | 3 | 16 |

Every disagreement, by kind; none makes a file wider or changes whether a client is admitted, a
name is on the list or a node gives a word:

1. **Delete Duplicate Entries dates its records at the moment it runs** (11 steps), where the
   model dates them at the history's invented time. The records are the same.
2. **A modification time under Overwrite existing or Keep existing** (3 steps of `b-random`): the
   model's import methods take the newer version, KeePassLib's Overwrite existing takes the arriving
   copy's and Keep existing the open copy's whatever the times (`RESULTS.md` 2.1). The read reads no
   time; the survival critique's bridge had 13 such.
3. **`incomplete` in the model and `nostore` in `read.py`** where the file holds no store key (47
   steps): the model reads a head's deletion record by its tag without the key, `read.py` cannot.
   Both serve nothing.
4. **Delete Duplicate Entries deleted the user's entry N** for its imported copy (6 steps of
   `b-dedupe`, three of them also kind 3): the model holds no tree.
5. **`read.py` lists a renewal answer only for a client the file still names** (7 steps of
   `b-random`): the model lists every client it paired. The answer is the same where asked.

**The model's own checks** over the 300 random histories found no `safety` failure. Held instead to
every decision any gate made, the 724 files at the ends of those histories were as decided in 397,
narrower in 91, refusing in 73 and wider in 163; that oracle counts as wider a copy a decision made
elsewhere has not yet reached, and these 163 were not read singly, so the tally is in the record
and is not quoted as a measure of the store.

## 5. Every wider outcome

Each with its shortest form, step by step, where it was measured, and whether `EVALUATION.md`
section 6 has it.

1. **A decision deleted in both of its entries where it was made, then a merge that carries no
   deletion record.** `revoke 0 c0`; the head deleted at copy 0 with no record; the revocation
   deleted there with a record; KeePassXC merges copy 0 into copy 1: copy 1 admits c0 (model, 4
   steps). The same for a name's removal through an Overwrite-family import, for a renewal refused
   forever, and for a retirement after Delete, where the deletion also takes the store key out of
   copy 0. **Row 2.**
2. **A decision lost a second time without a trace.** `prem 1 pf`; copy 1's head deleted with no
   record; an Overwrite-family import of copy 1 into copy 0 delivers the removal alone; the removal
   deleted at copy 0 with no record: c0 holds pf at copy 0 (model, 4 steps); and a narrowed rule's
   new version and head removed by script after a KeePassXC merge (model). **Row 3.**
3. **Deletion records cleared while a loss stands, then a stale copy synchronized**, wider in every
   mode of the repair on real files (R1) and in the model (9 steps); **and with Seal again pressed
   before the stale copy arrives** (design replay 10, 10 steps): without the mark the press leaves N
   with no rule, N reads the root's read, and the stale copy's older rule then gives read and write.
   **Row 4**; the press variant is there as "not run", and is now run: wider.
4. **A newer copy imported into a stale copy with Create new IDs.** `revoke 0 c0`; KeePass imports
   copy 0 into copy 1 with Create new IDs: copy 1 admits c0 (real files, 2 steps; 10 of the 26
   Create new IDs cells of section 3.1, each into the stale copy). **Row 7.**
5. **A file made by somebody holding the store key.** N narrowed at gate 0; copy 1 synchronized;
   a version granting read and write at N, naming N's versions, written with a head in copy 1;
   copy 1 synchronized into gate 0: wider in copy 1, and at gate 0 wherever the repair does not run
   or does not reach (no repair, the trigger system off, an import without a repair, the other file
   written before the repair) (real files, 4 steps). **Row 9.** New here: the repair, where it runs
   and reaches both files, closes it.
6. **A gate that has not yet synchronized** admits a client revoked at another gate (1 step).
   **Row 10.** A renewal of the revoked key at that gate is refused once the copies meet, in every
   cell but Create new IDs into the stale copy, which is item 4.
7. **Seal again after a loss without a record.** A revocation and the head deleted with no record,
   Seal again, KeePassXC into a stale copy: c0 admitted (5 steps); a node's first rule (nothing) and
   the head deleted with no record, Seal again: N inherits read (5 steps); renewals refused forever,
   the answer and the head deleted with no record, Seal again: the renewal asked (4 steps). Held by
   the witness, by nothing, and by nothing respectively. **Row 14.**
8. **The single carrier of the store key deleted by hand, then Create and a rule set again.**
   `delauth 0` (the retirement now holds the key); the retirement deleted for good; Create, under a
   new key; the root set to read and write: N's narrowing, written under the lost key, is passed
   over, and N gives read and write (model, 7 steps with N's rule and the two saves). The same
   with the authority's entry in place of the retirement (`del 0 (authority) with a record;
   create 0; rule 0 root rw`, 3 steps). **Row 17 has the authority's entry and the plugin's group
   as the carriers whose loss loses the store; the retirement after a Delete with no Create
   (change 6) is not named there.**
   "Wider" here is against the decisions made under the lost key, which `EVALUATION.md` 4.3 has
   entered again by the operator, and which nothing in the file can show.

And three accepted by decision, measured in the searches of section 2.1: a whole file written over
another (row 1; 34 to 267 histories of 20,000 in the cells of H and I), a copy of a narrowed node
read under its place (row 6; up to 17 a cell), and a rule lost when KeePass ends before a save (row
19; 60 to 115 a cell).

**No wider outcome was found that section 6 lacks, but item 8's carrier.** The sequences the
section names as not run that were run here (row 4 with a press, without the mark; row 10 with a
renewal under a revoked key; change 7) ended as items 3, 6 and 5 state.

## 6. The cost of each change taken by decision

As measured, each against the store with that change undone (section 2.5, ten cells of 20,000
histories, and the replays of section 2.3):

| Change by decision | What leaving it out of the store costs, as measured | What keeping it out costs, as measured |
| --- | --- | --- |
| No mark (decision 11) | no `safety` history in any cell differs | replays 10 and 15 fail without it (a press before a stale copy, a copy stripped of its reference), and row 4's press variant ends wider; a copy wider than its original in 3 to 13 histories a cell, where the mark capped every copy |
| No witness (decision 15) | no history in any cell differs | replay 18 fails without it, and row 14's revocation ends wider |
| A revocation names the key (decision 20, change 5) | with a revocation by certificate, **15 to 28 histories a cell end wider**, a renewal at a gate not yet told escaping the revocation | none measured: a revocation is never pruned, so the head carries it for good (section 3.8) |
| The store key in the retirement (decision 12, change 6) | without it, 0 to 4 histories a cell end wider: everything under the key is passed over after Delete and Create | the retirement becomes the key's single carrier: deleted by hand, the store key is lost (section 5, item 8) |
| A rule written at the next save (decision 13, change 4) | with the rule written at OK, 0 to 2 histories a cell end wider (the store: 0 or 1), rows 2 and 3 reaching a rule that would have waited | 71 to 111 histories a cell lose a rule when KeePass ends first (accepted) |
| Several gates (decision 20) | with one writing gate, the counts fall with the writes: 0 or 1 `safety` a cell | with several, row 10 for a time, and the repair's narrowing (below) arises only between gates |
| The repair after a Synchronize (decisions 18 and 19, change 7) | **no history in any cell differs in `safety`**; in the whole search it closed 1 widening of 480,000 histories, and on files R8 (row 9) | **9 to 85 histories a cell made narrower** than decided, a later decision of another gate written over by the earlier state; 35 and 28 merges-only histories converging narrower; and a first form that granted, where nothing stood after the merge |

## 7. What holds and what does not

**What holds, for the store as a whole.** With merges alone, in any order, by every program
modeled, no copy ends wider than what was delivered to it (A, 40,000 histories, and the exhaustive
merges), and on real files every merge cell of KeePassLib's four methods that match identifiers and
of the four KeePassXC releases, both directions, under both derivations where run, ends as decided
or narrower (section 3.1). A single entry deleted by hand in any way, a recycled entry, a head
emptied from the bin, every deletion record removed after a single deletion, Delete Empty Groups,
and Delete Duplicate Entries after Duplicate Entry of a decision, end as decided, narrower or
refusing, never wider (sections 3.4 to 3.6). A revocation that names the key refuses a renewal
made at a gate that had not heard of it, in every merge but Create new IDs. With Synchronize and
Keepass2Android as the only merges, and no hand loss of the authority's entry, the search found no
failure (J, J2, K).

**Ends wider than decided** (section 5): a decision lost in both of its entries where it was made,
then a merge without records (row 2); a decision lost a second time without a trace (row 3);
deletion records cleared while a loss stands, with or without Seal again pressed first (row 4);
Create new IDs of a newer copy into a stale copy (row 7); a file made with the store key, where the
repair does not run or does not reach both files (row 9); a gate not yet synchronized (row 10);
Seal again after a loss without a record, for a revocation, a node's first rule and a refusal of
renewals (row 14); the store key's single carrier deleted by hand and a rule set again after
Create, the retirement after a Delete being a carrier section 6 does not name. Accepted by decision
and measured: a whole file written over another (row 1), a copy of a narrowed node (row 6), a rule
lost before a save (row 19).

**Refuses**, for the whole database: the head deleted for good or emptied from the bin, a `KPG.`
string edited (row 15), two deletions with no record after KeePassDX's merge, both carriers deleted
then a Synchronize, Create new IDs of a copy where the store was incomplete; until Seal again, which
left the store as decided wherever it was run here (a head deleted in each way, or deduplicated).
**Nothing served**, with no Seal again possible: the authority's entry deleted for good (row 17) or
replaced by its duplicate, the plugin's group duplicated without the suffix and deduplicated,
Create new IDs then Delete Duplicate Entries (row 16), a second authority (row 18). **Narrower**:
an admission record or a profile addition deleted for good or deduplicated (that client or name,
until paired or added again); two decisions made apart (the meet, row 20; the removal wins, row
21); and, new here, **every decision a gate makes that gives more than an earlier decision it saw,
once it arrives at another gate by Synchronize, is taken by the repair for a loss**, and the
earlier state is written again beside it, until the operator settles it: 8 to 85 histories of
20,000 a cell, and 35 and 28 of the merges-only histories, which then converge narrower than
decided.

**What remains not run after this**: the plugin's API against a composed store (section 9, run 1);
any window, phone, emulator or second KeePass instance with the plugin, so several gates are run
as files that each write; whether KeePass's events fall where the repair needs them, which is the
desktop measurement's and not run here; change 4 on files (a rule waiting in memory is the model's
alone); a renewed store key at Recreate; rows 5, 12, 13 and 22 of section 6; kdbxweb, pykeepass,
Keepass2Android and KeePassDX as programs; a deleting clock behind on real files beyond records
dated at the start; the read on KeePass's thread, in C#; and the slow derivation for more than the
KeePassXC cells of four histories.
