# Measured: a rule stored as a record for each node and profile

Every run of the store so far stored an access rule as a single version of a node's whole rule,
for every profile at once. The project has since decided that a rule is a record for each node and
profile: each row of the grant editor (a profile's rule at a node) is a record with versions of its
own, and the node's switch "Refuse any profile not named here" (the rule document's `default`) is a
record of its own. A removed row under a refusing switch is written as an empty rule. That form
was in no run. This report is the run owed by `STORE.md` §644 item 24, made on 2026-11-17 in the
family `spikes\storage\probes\store-as-a-whole\` of the branch `spikes/storage-survival` as
`run.ps1 -Rows`, with its record in `recorded-rows\`. The family's `RUN.md`, section "Rules per
profile", has the commands, both runs' times and a second run's value of every figure. Nothing ran
in a window, on a phone or in an emulator; every file was made and changed by KeePassLib 2.62 and
merged by KeePassLib or `keepassxc-cli`.

**How to read it.** A **row** is a profile's rule at a node, written `N/pf`; the **switch** is the
node's "Refuse any profile not named here". A row reads in a state of three: an **empty rule**
(E), which grants the profile nothing from there down; the **rule taken off** (O), after which the
node does not name the profile and the switch answers; and **never made** (X), no version at all.
**Wider** is a client reaching more than the last decision allowed, and a sequence that ends wider
is named **fail-open**; **refuses** is the plugin refusing what the last decision allowed;
**narrower** is less than the last decision without refusing as a whole. The **oracle** is every
decision any gate made, read in the same form. Since the oracle combines versions made apart as
the store does, it cannot see a merge whose rows add up to more than either copy gave: where that
is the question, a merged file is held instead to what **both copies gave just before the merge**,
and a letter that neither gave is named **beyond both copies**. An **import cell** is "Create new
IDs": the store reads no copy of its own records, so the importing copy stays stale, in either
form. The **whole-rule form** is the store as decided as it was run before (`recorded-decided\`);
**per profile** is this run. A rule is written `pf=rw,pg=r`; two profiles, `pf` and `pg`, and two
letters, read and write.

## 1. What was implemented

**The records.** A row's version is a record of kind `version` whose object is
`rule:<node>/<profile>`; the switch's object is `switch:<node>`. A row's text is `row:<letters>`,
`row:` for an empty rule, and `off:` for the rule taken off; the switch's is `sw:deny` or
`sw:inherit`. Each names, in `after`, every version of the same object its writer had read, as
every version does. Nothing else of the store changed: the check, the identifier, the summary
entry (an item for each standing version of each row and switch), the history rule, no repair
after a synchronize, copies under another identifier ignored.

**What a rule act writes.** A version for each row whose standing value the act changes, and a
version of the switch where the switch changes; a row the act leaves as it reads writes nothing.
Removing a row where the gate reads the switch as refusing writes an empty rule (as decided).

**How a node's rule is read**, for each profile, from the standing versions of its row and of the
switch: the letters every granting version holds; beside a version that takes the rule off, what
arrives from above cut to those letters where the switch inherits, and nothing where it refuses;
no row, or only versions taking the rule off, is a profile the node does not name (what arrives
where the switch inherits, nothing where it refuses); a row shown lost by a deletion record gives
nothing. Versions of the switch that differ refuse. Then nearest wins per profile, profiles combined
with or, as today. Seal again writes, for a row whose version was lost, an empty rule, and for a
switch, `sw:deny`.

**A point the decisions leave open, and the narrowest reading run.** Removing a row that is an
empty rule already, while the switch still refuses: written as an empty rule, it changes nothing,
so the reading run writes nothing, and the row stays an empty rule until the switch is turned off
(the form `empty`). The other reading takes the rule off at that second removal (the form `again`,
run beside it in section 6). The two give the same answer at that node while the switch refuses,
and differ once a copy turns the switch off apart (section 6, W6).

**What removing a single version writes** (`STORE.md` S9), three forms run in section 4:
`again`, each version left written again, naming the removed version and itself; `void`, a single
version naming the removed version and holding nothing of its own; `limit`, a single version naming
the removed version and holding what the versions left allow together.

**The identifier of a row's version** (`STORE.md` S8): `pair`, the form run everywhere, the subject
hash over `rule:<node>/<profile>`; `split`, run in section 7, the subject hash over `rule:<node>`
and a keyed tag of the profile's name in the two bytes of the height, which are 0 for every version
until now.

## 2. What was run

| Part | What it is | Cells |
| --- | --- | --- |
| `b-merges` | the 13 histories of the store as decided and H14 to H22 of `measured-copies-extended.md`, KeePassLib's five methods and four `keepassxc-cli` releases, both directions, each cell also in the whole-rule form; and five histories of a user node | 396 and 90 |
| `b-deletions`, `b-clear` | the parts of the store as decided, per profile | 216 and 252 |
| `b-history` | each kind of store entry edited at copy 0 by ten operations of KeePass's own, read, merged both ways by Synchronize and KeePassXC 2.8.0, and the gate's write; the switch added as a kind | 660 |
| `b-random` | the 300 random histories of the store as decided's record, each also in the whole-rule form; each merge held to both copies | 724 files |
| `b-random-rows`, `-off` | 300 random histories of acts on a single row and on the switch, with Synchronize, Overwrite if newer and KeePassXC 2.8.0; each merge held to both copies; a removed row written as decided and taken off | 300 each |
| `store-search`, `-acts`, `-noauth` | the model's search of the store as decided, its configurations and seeds; the same with the acts on a row and on the switch; and those acts with no hand loss of the authority's entry | 480,000 histories each, and the exhaustive part in the first two |
| `beyond`, `-again`, `-off`, `-whole` | 200,000 random histories in the model over two copies, rules and merges only: how often a merge gives a letter neither copy gave | 200,000 each |
| `apart`, `same`, `states`, `switch`, `dels`, `growth` | items 2 to 7 below | sections 3 to 8 |

Every part ran twice; the second run printed every output of the first line for line, but the
running times. A run took about 13 minutes with eight parts at once, the model's searches the
longest. With the new switches off, the store as decided, `-Reading cap` and the first record were
run again in eleven parts and printed their records line for line (`RUN.md`).

## 3. Item 1: the same cells, per profile

| Part | Whole-rule form: as decided, narrower, refusing, wider | Per profile | Cells whose class differs |
| --- | --- | --- | --- |
| `b-merges`, 396 cells | 374, 5, 1, 16 | 373, 6, 1, 16 | 1: H20, Create new IDs into 0, as decided to narrower |
| `b-merges`, user node, 90 cells | N under its own rule gives nothing in every cell | the same | none |
| `b-deletions`, 216 cells | as `recorded-decided\` | the same | none |
| `b-clear`, 252 cells | as `recorded-decided\` | the same | none |
| `b-history`, 600 cells of `recorded-decided\` | 408, 10, 172, 0, and 10 rows "the Create above" | the same | none; the switch as a kind: 42 as decided, 18 refusing, 0 wider |
| `b-random`, 724 files | 406, 93, 54, 171 | 406, 96, 54, 168 | 6 (below) |

**No cell is newly wider.** Each of the 16 wider merge cells is the same cell in both forms,
each an import cell or a stale copy, and in `b-random` no file ended wider per profile that was not
wider in the whole-rule form. The criterion of `STORE.md` §644 item 24 holds in every part.

**Where the class differs.** H20, Create new IDs into copy 0: gate 0 narrowed pf and gate 1 widened
pg, apart; per profile the oracle holds both changes, so copy 0, which stays stale, lacks pg's
write, where the whole-rule oracle had met the two versions and read copy 0 as decided. In
`b-random`, three files went from wider to as decided and three from as decided to narrower, in
four histories (110, 133, 156, 249). Each holds a rule act that changes no row, which writes
nothing per profile and a version of the node's rule in the whole-rule form that then meets the
other copy's version, or a hand deletion or a move to the bin of "the newest rule version", which
picks a single row's version per profile, or both; the oracle differs with what was written. None
of the six files grants more per profile than in the whole-rule form.

**The model's search** with the same alphabet and seeds (`store-search`): every configuration
fails `safety` in the same kinds as the store as decided, by the shortest history of each, which is
all the search prints: a store key lost with the authority's entry and a Create after it, or a
refusal and its summary entry deleted. The counts are the record's in 20 configurations of 24 and
differ by a single failure in four (C, E and G over two files a failure more, F a failure fewer);
the same drawn acts write differently wherever a rule act changes no row and so writes nothing,
and which histories differ was not traced. With the acts on a row and on the switch, and no loss
of the authority's entry (`store-search-noauth`): `safety` in B, C, E over two files and C over
three, each shortest history a revocation, retirement or removal deleted with its summary entry;
no shortest history holds a row or a switch. The exhaustive part: no failure in each of its four
sets, as before.

## 4. Item 2: different profiles changed apart at a node

Seven cases, every merge both ways, in both forms (A7 per profile only, since it holds a switch).

| Case | Per profile, outside import cells | Whole-rule form, outside import cells |
| --- | --- | --- |
| A1 pf narrowed at copy 0, pg narrowed at copy 1 | as decided 16 of 16, nothing to settle | as decided 16, the node's rule in 2 versions |
| A2 pf widened, pg widened | as decided 16 | narrower 16: N lacks write for both |
| A3 pf widened, pg narrowed | as decided 16 | narrower 16: pf lacks write |
| A4 pf taken off, pg widened | as decided 16 | narrower 16: N lacks write for both |
| A5 pf emptied, pg taken off | as decided 16 | narrower 16: pg lacks write |
| A6 pf widened, a row for pg made where none was | as decided 16 | narrower 16: pf lacks write |
| A7 under a refusing switch, pf widened, pg removed | as decided 16 | not run |

**Per profile: 112 of 112 cells as decided in both copies, with no row left to settle**, by every
KeePassLib method and every KeePassXC release, both directions. The 14 import cells end as the
importing copy's own changes (6 wider, 8 narrower), as the 12 of the whole-rule form do. The
whole-rule form left the node's rule in two versions in all 96 of its cells and ended narrower in
80; in A1 the meet of two narrowings happens to equal both changes.

## 5. Item 3: the same profile changed apart

**Two copies**, pf set to read at copy 0 and to read and write at copy 1, apart: two versions stand
in 16 of 18 cells by every method, and N gives pf what both allow, read; the import cells hold the
importing copy's own version. **Three copies**, pf set to read, to read and write, and taken off,
apart, then merged into copy 0: three versions stand in 8 of 9 cells, and N gives pf read (what
arrives, read and write, cut to the letters both granting versions hold); under a refusing switch
copy 2's removal wrote nothing (the row was an empty rule already, section 1), so two versions
stand and N gives pf read.

**Removing a single version**, five sequences, each in the three forms, then every merge both ways,
and Synchronize both ways to see whether the copies converge:

| Sequence | `again` | `void` | `limit` |
| --- | --- | --- | --- |
| D1 both copies hold r and rw; copy 0 removes r; copy 1 still holds both | r stays removed in 17 of 18 cells, pf rw; the import into copy 1 stays stale | the same | the same; two equal rows shown |
| D2 as D1, and copy 1 sets pf to write, apart | pf write in both copies after Synchronize both ways: converge | the same | the same |
| D3 the root reads; both copies hold empty and w; copy 0 removes w, copy 1 removes empty, apart | both versions stand again: pf nothing, narrower than copy 1 | **beyond both copies in 16 of 18 cells: no version stands but the two removals, pf gets read from the root, which neither copy gave** | both stand again: pf nothing |
| D4 three copies hold r, rw and w; copy 0 removes r | two versions stand, rw and w: pf write | the same | the same, a third row shown |
| D5 as D4, copy 0 removes r and copy 1 removes w, apart | all three values stand again: pf nothing | rw stands alone: pf rw, what both removals meant | all three stand again: pf nothing |

**What stands and what removal needs.** `again` is the narrowest form that works: it writes only
records of the kind that exists, every version it writes restates a version that stood, a removed
version does not come back by any merge (D1), a removal in a copy and a change in another copy
converge (D2), and no cell of any sequence is beyond both copies. Its cost is narrower, never
wider: two removals made apart in different copies undo each other (D3, D5), since each copy
writes again the version the other copy removed. **`void` is fail-open** (D3): when two copies
each remove the version the other copy kept, nothing but the two removals stands, the profile is
not named, and the node gives what arrives from above. `limit` gives the answers of `again` and
adds a row the editor would show that nobody made. **What `again` writes, exactly:** for each
version left, a version with the same text whose `after` names the removed version, everything it
names, the version left and everything that version names; removing a version of two is the same
write as setting the row to the version kept.

## 6. Item 4: an empty rule, a rule taken off, and no rule ever made

At N, under a root that grants read and write and a switch that inherits, copy 1 holding the
earlier row r: every merge both ways, and at copy 0 the version edited in the entry dialog, edited
eleven times, deleted for good with and without a deletion record, deleted to the recycle bin, and
deleted for good with its summary entry and then Seal again; each followed by Synchronize and
KeePassXC 2.8.0 both ways (a deletion record carried by Synchronize, not by KeePassXC).

| State made | Every merge | Dialog edit, deletions with and without a record, recycle bin | Eleven edits | Version and summary entry deleted, Seal again |
| --- | --- | --- | --- | --- |
| E | E in 17 of 18 cells, pf nothing; the import cell into copy 1 holds r | E in 16 of 16 | the store refuses in 4 of 4 | E in 4 of 4 |
| O | O in 17 of 18 cells, pf read and write; the import cell holds r | O in 16 of 16 | the store refuses in 4 of 4 | **E in 4 of 4: pf nothing** |
| X | X in 18 of 18 | not run: no version | not run | not run |

**The three stay apart through every merge and every edit.** A rule taken off never read as an
empty rule after a merge, and an empty rule never read as taken off. A hand edit of the text fails
the check, and the entry counts by its history item, so it never reads as the other state. **A
loss turns O into E:** where the version and every summary entry that restates it are gone and a
deletion record tells of the version, the row refuses, and Seal again writes the refusal as an
empty rule; narrower, as decided for any lost version. **E and O made apart** (from the r both
copies held): both stand, and pf gets nothing at N in 16 of 18 cells, what both allow; the import
into copy 1 keeps copy 1's O.

## 7. Item 5: the switch as a record of its own

Each sequence under three ways of writing a removed row: as decided (`empty`), `again`, and taken
off always (`off`, the control); every merge both ways; each merged file held to both copies just
before the merge. The import cells (2 of each 18) are within what the copies gave in every
sequence.

| Sequence | Copy 0 gives at N | Copy 1 gives at N | Merged, as decided | `again` | `off` |
| --- | --- | --- | --- | --- | --- |
| W1 switch on at 0; on then off at 1 | pf r, pg nothing | pf r, pg rw | refuses pg: the same as copy 0 | the same | the same |
| W2 switch on at 0; pf widened at 1 | pf r, pg r | pf rw, pg r | pf rw, pg r | the same | the same |
| **W3 S34**: refusing switch; pf removed at 0; switch off at 1 | pf nothing, pg r | pf r, pg r | **pf nothing: within, 18 of 18** | within, 18 of 18 | **pf rw: beyond both copies in 16 of 18** |
| W4 switch on at 0; a row for pg, unnamed, made at 1 | pf r, pg nothing | pf r, pg r | pf r, pg r: the same as copy 1 | the same | the same |
| W5 inheriting switch; pf removed at 0; switch on at 1 | pf rw, pg rw | pf r, pg nothing | nothing: less than either | the same | the same |
| W6 refusing switch, pf removed and synchronized; removed again at 0; switch off at 1 | nothing | pf nothing, pg rw | pf nothing: within | **pf rw: beyond both copies in 16 of 18** | within (pf was taken off from the start) |
| W7 refusing switch; pf removed at 0; at 1 switch off, then pf removed | nothing | rw | pf nothing: less than copy 1 | the same | pf rw: the same as copy 1 |
| W8 across nodes: pf's empty rule at N taken off at 0; root widened for pf at 1 | pf r | pf nothing | **pf rw: beyond both copies in 16 of 18** | the same | the same |
| W9 W8 in the whole-rule form | pf r | pf nothing | **pf rw: beyond both copies in 16 of 18** | | |

**S34 with the decided answer: closed on files.** Written as an empty rule, the removal leaves pf
with nothing at N after the merge, in both directions, by every KeePassLib method and every
KeePassXC release; taken off (the control), pf gets read and write from the root, which neither
copy's table gave, in all 16 cells that are not imports.

**The sequences next to it.** The switch turned on in a copy while another makes a row for an
unnamed profile (W4) gives what the copy that made the row gave: the same, not wider. A row removed
under a switch that inherits while another copy turns the switch on (W5) gives less than either. An
empty rule removed again under a refusing switch while another copy turns the switch off (W6) is
**fail-open under `again`** and closed under the reading run, which writes nothing at that second
removal: **the reading of section 1 is load bearing**, and taking the rule off at the second removal
reopens S34. The switch turned off and the row then removed in the same copy, beside a removal in
the other copy (W7), gives nothing, narrower than the copy that removed after turning off. **Across
nodes, in either form** (W8, W9): a narrow rule taken off at a node in a copy while another copy
widens the rule above it ends beyond both copies, per profile and in the whole-rule form alike; the
decided answer does not touch it, since it is not a removal under a refusing switch.

**How often, in random histories.** In the model, 200,000 histories over two copies of acts on
rules and merges: a merge gave a letter neither copy gave in 67 histories per profile with the
decided answer, 68 under `again`, 69 with the rule taken off, and 298 in the whole-rule form. Every
such merge is at N. The shortest per profile is across nodes, with the switch in the place of the
row: the switch turned on at copy 0 and synchronized; copy 1 widens the root for pf; copy 0 turns
the switch off; Synchronize. On files, the 300 histories of `b-random-rows` gave no such merge
with either way of writing a removed row, and the 300 of `b-random` a single merge in each form,
after the authority's entry was deleted (history 80: a store started again, not a rule).

## 8. Item 6: what a deletion record tells

**The form run, `pair`, tells the row**: the reading tries each node of the tree with each profile
name the file knows (the names of the list's records and items, and every profile a row's version
or item names) and with `switch:`. Run on files: a version of N/pf, N/pg, the switch and root/pg,
each deleted for good with its summary entry, both with deletion records: the store stops, the
listing names the lost row alone, and Seal again writes an empty rule for that row (and `sw:deny`
for the switch); the other profile's row at the same node is untouched, and both merges both ways
read the same. The refusal is that profile's alone.

**The form `split` gives the same lines in 4 of 4 sequences** (24 of 24 lines), and costs the
reading a hash per node and a tag per name instead of a hash per node and name:

| Nodes in the tree, four names | `pair`: keyed hashes, read | `split`: keyed hashes, read |
| --- | --- | --- |
| 20 | 228, 1.1 ms | 88, 0.6 ms |
| 200 | 1,308, 8.4 ms | 448, 2.7 ms |
| 2,000 | 12,108, 48.1 ms | 4,048, 23.0 ms |

Timed in Python, median of five, in run 1 (run 2 read 1.8, 3.7 and 40.0 ms, and 0.6, 1.9 and
21.5 ms), over a dump with groups added as nodes and a deletion record of a switch for each; the cost grows with every node of the tree, ruled or not, since a deletion record
can be of any node's row. **Proposed, the cheapest form that keeps the answers:** `split`, with the
switch under the same subject as the node's rows and a fixed value of its own in those two bytes,
which takes away the switch's hash for each node as well (reasoning, not run). A tag of two bytes
meets another name's tag by chance about once in 65,536 for each pair of names; such a deletion
record would refuse both rows, narrower (reasoning).

## 9. Item 7: growth

From the strings `store.py` writes, no file: a ruled node with k rows and the switch, against a
single version of the node's whole rule naming k profiles and the switch.

| Nodes, rows each | Whole-rule: entries, bytes; summary items, bytes | Per profile: entries, bytes; summary items, bytes |
| --- | --- | --- |
| 20, 1 | 20, 4,740; 20, 3,379 | 40, 8,980; 40, 6,059 |
| 20, 4 | 20, 5,160; 20, 3,799 | 100, 22,480; 100, 14,879 |
| 20, 10 | 20, 6,000; 20, 4,639 | 220, 49,480; 220, 32,519 |
| 200, 1 | 200, 47,400; 200, 31,999 | 400, 89,800; 400, 58,799 |
| 200, 4 | 200, 51,600; 200, 36,199 | 1,000, 224,800; 1,000, 146,999 |
| 200, 10 | 200, 60,000; 200, 44,599 | 2,200, 494,800; 2,200, 323,399 |
| 2,000, 1 | 2,000, 474,000; 2,000, 318,199 | 4,000, 898,000; 4,000, 586,199 |
| 2,000, 4 | 2,000, 516,000; 2,000, 360,199 | **10,000**, 2,248,000; **10,000, over 9,999**, 1,468,200 |
| 2,000, 10 | 2,000, 600,000; 2,000, 444,199 | **22,000**, 4,948,000; **22,000, over 9,999**, 3,244,200 |

**A single edit** adds a version of 268 to 322 bytes in the whole-rule form, growing with the
profiles named, and 256 bytes per profile whatever the count, for each row it changes; changing
every row of a node of four rows adds four entries per profile against a single entry. The
summary entry is written anew with every write and changes by the size of the item replaced.
**Against the ceiling of 9,999 item names**, per profile 1,999 nodes of four rows and the switch
fit, and 2,000 do not; with ten rows each, 909 nodes fit. In the whole-rule form a node is a
single item whatever its profiles. Each item is a string of its own, about 147 bytes, far below
the reader's ceiling of 1 MiB for a string.

## 10. Where `STORE.md`'s text on rules does not match the files

- **§617, "Versions of the same profile's rule made apart give what all of them allow, until the
  operator has removed versions"**, and S9, "a removed row stops standing in every copy the write
  reaches": with the form `again`, a removed version stops standing, but two removals made apart in
  two copies each write again the version the other removed, so the removed values stand again
  after the merge (D3, D5). With `void`, removals converge, and two copies that each remove the
  version the other kept end fail-open (D3). No form run has both.
- **§617 and §644 item 24, "a rule taken off and an empty rule staying apart through every merge
  and every loss"**: through every merge and every edit, yes; through a loss of the version and of
  every summary entry that restates it, a rule taken off becomes an empty rule at Seal again
  (section 6). Narrower, and the same as any lost version, but not "apart".
- **§639 and S34**: the answer decided closes the sequence as written. The text does not state
  what removing an empty rule again writes while the switch still refuses; writing the rule taken
  off there reopens the same sequence a step later (W6). The reading run writes nothing.
- **§639, "the same holds across nodes in either form"**: measured, W8 and W9, and the same with
  the switch turned off in place of a row taken off (`beyond`); per profile it occurs less often
  than in the whole-rule form (67 against 298 in 200,000 histories).
- **§643, "a database with 2,000 ruled nodes of four rows each does not fit"**: it fits without
  the switch (8,000 items) and does not with the switch set at every node (10,000), by a single
  item.
- **§643, "Each version is smaller"**: by 12 to 66 bytes, at 256 bytes; the store grows by the
  number of rows, 2 to 11 times the entries for the same rules.
- **S8, "without the reading having to try every node with every name at every request"**: the form
  run does exactly that; the form `split` does not, with the same answers (section 8).
- **§629, "a version for each row that was changed"**: as run, a rule act that changes no row
  writes nothing, which is what made six random files differ between the forms (section 3).

## 11. Every fail-open sequence in this run

1. **S34 with the rule taken off** (W3 under `off`, the control): 16 of 18 cells. Closed by the
   decided answer in 18 of 18.
2. **An empty rule removed again under a refusing switch, written as the rule taken off, while
   another copy turns the switch off** (W6 under `again`): 16 of 18 cells. Closed by writing
   nothing at that removal, the reading run.
3. **Across nodes, in either form** (W8, W9, and in the model 67 to 298 histories in 200,000): a
   narrow rule taken off, or the switch turned off, at a node in a copy, while another copy widens
   the rule above it. Untouched by the decided answer; it is the cost of rows and of nodes that do
   not meet.
4. **Removing a single version in the form `void`** (D3): two copies each remove the version the
   other kept; 16 of 18 cells. Not in the form `again`.
5. Every import cell that ends wider (16 merge cells, 6 of `apart`, and the cells of `same`): a copy
   that stays stale, the same in both forms.

No cell of the parts of the store as decided is newly wider per profile.

## 12. What was cut, and what was not measured

**Cut**, to keep both runs within about three hours: `b-slow`, `b-dedupe`, `b-dupes`, `b-repair`
and `b-growth` per profile (none of them about rules beyond what `b-merges` and `b-deletions` hold);
`store-ext` and the replays; KeePassXC releases other than 2.8.0 in `b-history`, `b-deletions`,
`b-clear` and the random parts; the forms `void`, `limit` and `split` outside the parts that name
them; more than 300 random histories on files.

**Not measured**: the grant editor and KeePass's window, a second KeePass with the plugin, a phone;
more than two profiles and two letters in a file, and the switches of a rule (attachments, protected
values); a rule on a group between the root and N; a row of a name off the list whose records and
items are all gone, which the reading run cannot tell from a deletion record; a collision of two
profiles' tags in the form `split`; the read in the plugin's C#; every `safety` failure of the
model's searches beyond the shortest history of each configuration, which is all the search
prints; the growth of the `after` lists under repeated edits of a row; and everything
`measured-decided-store.md`, `measured-two-readings.md` and `measured-copies-extended.md` list as
not measured.
