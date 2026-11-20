# Measured: the forms rules per profile left owed

The run of a rule stored as a record for each node and profile (`measured-rules-per-profile.md`)
left owed, in `STORE.md` §644 item 24, the forms it did not hold: more than two profiles and two
letters, and the switches of a rule; a rule on a group between the root and the node; a row of a
name off the list whose records and items are all gone; two profiles whose tags meet in the form
proposed for S8; and how the `after` lists grow. This report is that run, made on 2026-11-20 in
the family `spikes\storage\probes\store-as-a-whole\` on the branch `measure/rules-model`, cut
from `spikes/storage-survival` at `b9d0ab03`, as `run.ps1 -Owed`, with its record in
`recorded-owed\`. The family's `RUN.md`, section "The forms rules per profile left owed", has the
commands, both runs' times and a second run's value of every figure. Nothing ran in a window, on
a phone or in an emulator; every file was made and changed by KeePassLib 2.62 and merged by
KeePassLib or `keepassxc-cli`.

**How to read it.** As in `measured-rules-per-profile.md`: a **row** is a profile's rule at a
node, `N/pf`; the **switch** is the node's "Refuse any profile not named here"; a row reads as
an **empty rule** (E), the **rule taken off** (O), or **never made** (X). **Wider** is a client
reaching more than the last decision allowed, and a sequence that ends wider is **fail-open**;
**narrower** is less without refusing as a whole. The **oracle** is every decision any gate made,
read in the same form; since it combines versions made apart as the store does, it cannot see a
merge that adds up to more than either copy gave, so each merged file is also held to **what both
copies gave just before the merge**. Here that is done twice: **by letters**, as before, and **by
capabilities**, since a switch of a rule (attachments `a`, protected values `p`) means "and those
too" for the verbs of the same rule. A capability is a verb alone (`pf r`), or a verb that reads or
writes with a switch of the same rule (`pf r+a`); strip and delete reach attachments and protected
values with no switch; a switch without a verb grants nothing. A merge **beyond both copies by
capabilities only** gives a combination neither copy gave while each of its letters came from a
copy. Letters are written per profile in the order of the mask `CRUSDLAP`, `pf:rla`. An **import
cell** is "Create new IDs": the importing copy stays stale (§640).

## 1. What was implemented

**On top of rules per profile as run**, with a removed row written as decided (an empty rule
where the gate reads the switch as refusing, nothing written where the row is an empty rule
already). Every addition is a switch whose default is that run; each line carries "ADDED (owed
forms)".

**More profiles and the full mask.** Four profiles, `pf`, `pg`, `ph` and `pi`, and the eight
letters of `CRUSDLAP`: create, read, update, strip, delete, list, attachments and protected
values. A row's text is `row:` and its letters as before, now any of the eight (the probe writes
them in alphabetical order; the order is a matter of the product's text). The reading of a
node is unchanged: versions that grant give the letters all of them hold, a version that takes
the rule off beside them cuts what arrives to those letters where the switch inherits, and a
switch letter is a letter there like any other, as §617 has it.

**Capabilities.** `crdt_model.caps` turns a profile's letters into capabilities as defined above;
every merge in the parts of this run is held to both copies by letters and by capabilities.

**A group between the root and N.** With three levels the model has a node `G` under the root and
N under `G`, and what arrives at a node is what its parent gives. On files `G` is the group
General, which held N in every earlier run as well; the bridge maps it and compares and judges it
as it does the root and N.

**The names the file knows.** The model read every deletion record by the element it names. Here
it tells a deletion record of a removal of a name only for a name the file knows from the list's
records and items, and of a row's version only for a name the file knows from those or from any
row object a version present or a summary item names, as `read.py` does; so Seal again and every
write compose over the reading the file gives. The root's rule can name some profiles only, so
that a profile can be named at N alone. How an addition names the removals of its name (§616) has
two forms: `text`, the removals the reading before the write holds, as §616 words it; `name`,
also those a deletion record tells once the name being added is counted as known.

**Tags that meet, in the form `split`.** `read.py` read a deletion record of a row's version in
that form through a dictionary from a tag to a single name, filled by iterating a set of names, so
it kept the name the set yielded last, and the order of a set of strings changes with the
process's hash seed. The names are now sorted and kept in a list for each tag, and three readings
are run: `first` and `last`, the two answers the reading as run can give for two names; and
`each`, the reading proposed (S8, and the implementing plan's R4 and R5): a deletion record is
told of every name of its tag that the file knows, and only where no version present or restated
carries its identifier. The model follows each reading, so that Seal again writes what the
reading tells.

## 2. What was run

| Part | What it is | Cells |
| --- | --- | --- |
| `combine` | every pair of versions of a row (each of the 256 sets of the eight letters, and the rule taken off), every set arriving from above, the switch refusing and inheriting, held to what both versions allow by capabilities; random triples | 16,974,336 pairs, 200,000 triples |
| `merges4` | eight histories with four profiles and the full mask over two copies, KeePassLib's five methods and four `keepassxc-cli` releases, both directions | 144 |
| `random4` | random histories of acts on rows and switches, four profiles and the full mask, at the root and N, with Synchronize, Overwrite if newer and KeePassXC 2.8.0 | 300 histories, 781 merges |
| `levels` | ten sequences at the root, a group G and N under it, every merge both ways | 180 |
| `random3` | `random4` at the root, G and N | 300 histories, 866 merges |
| `beyond-2-rw`, `-4-rw`, `-2-mask`, `-4-mask`, `-4-mask-3` | the model's `beyond` with two or four profiles, read and write or the full mask, two or three levels; each history that gives more shrunk to its shortest form | 200,000 histories each |
| `offlist` | a row of a name off the list, its records and items lost in four ways in two trees, Seal again, the name added again in two forms, a further write, each merge with a copy that holds the row; and a control with the name on the list | 44 sequences, 300 reads |
| `tags` | two names whose keyed tags meet, under three readings: row acts and every merge both ways, and four deletions each followed by Seal again and merges; a name whose tag is 0000 | 54 merge cells, 68 reads |
| `afters` | a row edited 400 times in a copy, 200 times alternately in two copies, and 200 times apart in two copies, with merges between | 1,200 edits |

Every part ran twice; the second run printed every output of the first line for line, but the
running times, the read's milliseconds and the file's bytes. `random4` and `random3` were then
given the count of merges they hold and ran twice more, the third run being their record and the
fourth printing it line for line; every other line of theirs is the same in all four runs. A run
took about 15 minutes with four parts at once, `beyond-4-mask-3` the longest. With the new
switches off, 15 earlier outputs were run again after the addition and printed their records line
for line: `beyond`, `beyond-whole`, `beyond-again`, `b-merges`, `switch`, `same`,
`b-random-rows`, `apart`, `states`, `dels` and `growth` of `recorded-rows\`, `b-merges` and
`b-random` of `recorded-decided\`, and `store-scenarios` and `store-section6` of `recorded\`.
`beyond-2-rw` draws what `beyond` drew and found the same 67 histories.

## 3. Item 1: more profiles, the full mask and the switches of a rule

**Versions of a row made apart stay right with switches in some versions and not in others.**
Over every pair of versions, every set arriving from above and either switch of the node, the row
gave exactly what both versions allow, by capabilities, in 16,974,336 of 16,974,336 combinations,
neither more nor less; and in 200,000 of 200,000 random triples. A switch without a verb (`a`,
`p`, `ap`) grants nothing. The reason holds for every combination: the capabilities of a set of
letters are the pairs of its verbs and of its switches, so meeting the letters meets the
capabilities. In O2 on files, `ra` at copy 0 and `ua` at copy 1 met as `a`, which grants pg
nothing at N, in 16 of 16 cells.

**On files, at a single node, no merge gave a verb or a switch beyond both copies.**

| History, four profiles and the full mask | Every merge but imports (16) | Against both copies |
| --- | --- | --- |
| O1 attachments dropped at copy 0, protected values added at copy 1 (`rla` to `rl` and `rlap`) | as decided, pf `rl` | within, 16 |
| O2 `ra` at copy 0, `ua` at copy 1 | as decided, pg `a`: nothing | within, 16 |
| O3 the switches crossed, `rla` and `rlp` | as decided, ph `rl` | within, 16 |
| O4 four profiles at N: pf widened with both switches and ph taken off at copy 0, pg emptied and a switch added to pi at copy 1 | as decided, both changes for all four | within, 16 |
| O5 a switch dropped at copy 0, verbs widened at copy 1 | as decided, pf `rl` | within, 16 |
| O6 across nodes: the root gives pf `ua`, N `r`; N's row taken off at copy 0; the root set to `ra` at copy 1 | as decided | **beyond by capabilities only, 16: N gives pf `r+a`**, where copy 0 gave `ua` and copy 1 `r` |
| O7 across nodes: N refuses pf, unnamed; copy 0 turns the switch off under a root giving `ua`; copy 1 sets the root to `ra` | as decided | **beyond by letters, 16: N gives pf `r`** |
| O8 S34 with switches: under a refusing switch pf's row `rla` removed at copy 0; the switch turned off at copy 1 | as decided, pf nothing | within, 16 |

The 16 import cells are within what both copies gave, and against the oracle 10 wider, 2 as
decided and 4 narrower, each the importing copy staying stale. Every cell of model and file
agreed, entry by entry and in the read. **The random histories on files** (`random4`): 300
histories, 1,967 steps, no disagreement between model and file; of 781 merges none gave
a letter or a capability beyond both copies. The files at the end, held to every decision any gate
made, were 328 wider, 227 as decided and 167 narrower: a file is wider there when it has not
received a narrowing another copy made, while no merge gave more than both copies, as in the
earlier run (234, 383 and 95 of 712 there).

**The counts in the model are of the same order as with two letters.** Every history that gave
more was, at its shortest, a history across nodes; none was at a single node.

| `beyond`, 200,000 histories each | Histories beyond both copies | By letters | By capabilities only |
| --- | --- | --- | --- |
| two profiles, read and write (the earlier run's draws) | 67 | 67 | 0 |
| four profiles, read and write | 33 | 33 | 0 |
| two profiles, the full mask | 129 | 121 | 8 |
| four profiles, the full mask | 44 | 43 | 1 |
| four profiles, the full mask, root, G and N | 44 (56 merges: 20 at G, 36 at N) | 43 | 1 |

More profiles spread the same number of acts over more rows, and the count falls; the full mask
gives a row act more values, and the count rises. The shortest by capabilities only, four
profiles: `row 0 N pg cula ; merge kp_other 1 0 ; row 1 N pg off ; row 0 root pg rla ; merge xc
1 0`, after which N gives pg `r+a`, where copy 0 gave `cula` at N and copy 1 gave the root's `r`.
A check by letters alone passes 1,365,238 of the 16,777,216 triples of letter sets (copy 0, copy
1, merged) whose merged capabilities neither copy gave, the smallest `c`, `a` and `ca`.

## 4. Item 2: a rule on a group between the root and the node

| Sequence, the root, G and N | Every merge but imports (16) | Against both copies |
| --- | --- | --- |
| L1 nearest wins, apart at two levels: pf narrowed at N at copy 0, widened at G at copy 1 | as decided: N `r`, G `crudla` | within, 16 |
| L2 two profiles at two levels: pg widened at G at copy 0, pf widened at N at copy 1 | as decided | within, 16 |
| L3 pf's rule at N taken off at copy 0 and narrowed to `r` at copy 2, met in copy 0; G widened at copy 1 | as decided: N gives pf what arrives cut to `r`, `r` | within, 16 |
| L4 L3 with G narrowed to `l` at copy 1 | as decided: N gives pf nothing | within, 16 |
| L5 G's switch turned on at copy 0, N's inheriting; pg's row at N taken off at copy 1 | as decided: pg nothing at G and N | within, 16, less than either copy |
| L6 G's switch on at copy 0, on then off at copy 1 | as decided: refuses at G, as the copy with it on | within, 16 |
| L7 S34 at G: under G's refusing switch pf's row at G removed at copy 0; G's switch turned off at copy 1 | as decided: pf nothing at G and N | within, 16 |
| L8 across levels: pf's narrow rule at G taken off at copy 0; the root widened for pf at copy 1 | as decided | **beyond by letters, 16: G and N give pf `cusdap`** |
| L9 across levels, the node between: pf's narrow rule at N taken off at copy 0; G widened for pf at copy 1 | as decided | **beyond by letters, 16: N gives pf `cud`** |
| L10 G refuses pf, unnamed, N's switch inherits and N names pf; pf's row at N taken off at copy 0; G's switch turned off at copy 1 | as decided | **beyond by letters, 16: N gives pf `cusdap`** |

**Nearest wins per profile through versions made apart at different levels** (L1, L2), a rule
taken off at the node beside a narrowing version while the group's rule changes in the other copy
gives what arrives from the group's new rule cut to the narrowing letters (L3, L4), and the switch
at the group refusing while the node's inherits refuses the profiles the group does not name in
the merged file (L5, L6), each in 16 of 16 cells. **Ended beyond both copies**: L8, L9 and L10,
the class of S34 that is left, now with the group as the node taken off or widened, and with the
group's switch in the place of a row (L10). The 20 import cells: 8 wider, 1 as decided, 11
narrower, each the stale copy. `random3`: 300 histories, 1,973 steps, no disagreement; of
866 merges none gave more than both copies; the files at the end 312 wider, 248 as
decided, 150 narrower. In the model, with three levels: 44 of 200,000 histories, 56 such merges,
each across levels; the shortest at G is `row 0 G pf (empty) ; merge kp_other 1 0 ; row 0 G pf
off ; row 1 root pf cula ; merge kp_other 0 1`, and the shortest by capabilities only takes N's
row off while copy 1 widens G.

## 5. Item 3: a row of a name off the list whose records and items are gone

**The name can only be unknown to the file where nothing grants it.** A name the file knows from
any row object is known, so a deletion record of its row's version is told. The case the reading
cannot tell is a name with no record, no item and no row left anywhere, and such a name is named by
no rule, so it gets nothing at any node. The run built both trees: pg named at the root and at N
(`root`), and pg named at N alone. pg had read at N, was removed from the list, and copy 1 took
everything; then at copy 0 were lost the versions of N/pg and every summary entry (`row`), with
pg's additions and removal (`row and name`), and with every row of pg as well (`every row and
name`), each in four ways; Seal again; pg added again, a further write, pg added a second time;
each merge with copy 1.

| Tree, what was lost | With deletion records (deleted for good; to the bin, emptied) | With none (no record; records cleared) |
| --- | --- | --- |
| root, row | Seal again writes E: pg nothing at N, narrower | **X: pg inherits `rw` from the root, wider than `r`, 6 reads in each way** |
| root, row and name | E, narrower; the name stays off the list at the first Add in the form `text` | **X: wider, 6 reads in each way** |
| root, every row and name | pg unknown, X, nothing; once pg is added, N/pg refuses | X, nothing anywhere |
| N alone, row | E, narrower | X, nothing anywhere |
| N alone, row and name | pg unknown, X, nothing; once pg is added, N/pg refuses | X, nothing anywhere |
| control: root, row, pg never removed | E, narrower | **X: wider, 1 read in each way** |

**26 reads of 300 ended wider, each a loss with no trace of a row whose profile a rule above
still names**, and the control with the name on the list ends wider by the same loss in the same
reads. That is §617's "a profile whose rule at a node is lost in every carrier, with no trace, is a
profile the node does not name", which the list does not change. **No read was wider where a
deletion record stayed**, and no read was wider where pg was unknown: the deletion record of the
row was told as soon as the name was known again, and the row refused. None of the 176 reads after
a merge with copy 1 ended wider: without a deletion record a merge brings the row `r` back from
copy 1, and where a deletion record stayed it reaches copy 1 by Synchronize, or KeePassXC brings
the version back into copy 0 beside its deletion record, which refuses.

**What the row reads as after the name is back**: E where Seal again saw it lost, refusing where
the name was unknown at Seal again, and X where nothing told of it. **Adding the name back**: in
the form `text`, where pg's removal is known only from a deletion record that the reading tells
once pg is known, the addition does not name it, is not in force and is pruned by its own write; pg
stayed off the list in 6 of 20 sequences, and was on it after the second Add in all 6. In the form
`name` the first Add put it back in 20 of 20. Narrower, never wider.

## 6. Item 4: two profiles whose tags meet, in the form `split`

**Found by search** under the store key: `q00067` and `q00401` share the tag `6b11`, after 402
names; `q00238` is the first name whose tag is 0000.

**The reading as run is not stable.** It kept, for the tag, the name a set of strings yielded
last. Over the hash seeds 0 to 39 of the same Python, the set kept `q00067` in 17 and `q00401` in
23; the same file reads differently from process to process. Both of its answers were run:

| Reading | A's version and the summary entries deleted (G2) | B's version and the summary entries deleted (G3) | A version deleted, the summary entry kept (G4, G5) | Row acts and every merge (G1) |
| --- | --- | --- | --- | --- |
| `first` (keeps `q00067`) | both rows E after Seal again, narrower for `q00067` | **`q00401` never made: inherits `rw` where its rule was empty; wider in 4 of 6 reads**, `q00067` E | G4 as decided; G5 `q00067` refuses, narrower | as decided 16 of 16, within both copies 18 of 18 |
| `last` (keeps `q00401`) | **`q00067` never made: inherits `rw` where its rule was `r`; wider in 4 of 6 reads**, `q00401` E | `q00401` E | G4 `q00401` refuses where its rule was empty; G5 as decided | the same |
| `each` (proposed) | both refuse, then E: `q00067` narrower | both refuse, then E: `q00067` narrower | nothing told while the summary entry restates the version: as decided | the same |

The 4 wider reads under `first` (G3) and under `last` (G2) are the read after the deletion, while
the store stopped for the lost summary entry, and Seal again and Synchronize into either copy,
with the store whole and serving.
The KeePassXC merges ended within, the deleted version coming back beside its deletion record.
**No profile got the other profile's rule**: a version and a summary item carry their object, so
only a deletion record is read by the tag. **Either profile could get a wider rule only under the
reading as run**: the name the dictionary does not keep has its lost version read as never made.
**Under `each`** both rows refuse and Seal again writes both empty, narrower, and no read of G2 to
G5 was wider, 22 of 22. **The reading can tell that it happened**: it computes the tag of every
name it knows, and every read here listed `q00067/q00401` as tags that meet, so the plugin can
log them.

**A name whose tag is 0000** (G6): `read.py`'s form `split` takes 0000 in bytes 9 and 10 as a
version of a node's whole rule, so the deletion record of `q00238`'s version told of no row, and
the row read as never made, inheriting `rw` where its rule was `r`, while the store was stopped
for the lost summary entry; Seal again, composed by the model, wrote it empty. The product has no
version of a node's whole rule; a reading that skipped 0000 there would leave such a row untold.

**How likely a meeting is.** Two given names meet with chance 1/65,536. Among n names the file
knows: n = 4, 0.009%; 8, 0.043%; 16, 0.18%; 32, 0.75%; 64, 3.0%; 100, 7.3%; 256, 39%; 1,000,
99.95%. Under `each` a deletion record at a node refuses, at that node, the row of every name of
the same tag that the file knows, ruled there or not, so the names that count are all names the
file knows, and for a given lost row the chance is 1/65,536 for each other name the file knows. A
meeting costs something only where a version is lost by hand with no summary entry restating it.

## 7. Item 5: how the `after` lists grow

| Edits of N/pf | Copy | Standing versions | Identifiers in `after` | KPG.Data of the version | Its summary item | Summary entry's own `after` | Deletion records | History items |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | A, a single copy | 1 | 0 | 130 | 136 | 1 | 1 | 0 |
| 10 | A | 1 | 9 | 425 | 431 | 10 | 19 | 0 |
| 100 | A | 1 | 99 | 3,395 | 3,401 | 100 | 199 | 0 |
| 400 | A | 1 | 399 | 13,295 | 13,301 | 400 | 799 | 0 |
| 200 | B, alternately in two copies, Synchronize between | 1 | 199 | 6,695 | 6,701 | 200 | 399 | 0 |
| 200 | C, apart in two copies, Synchronize both ways each round | 2 | 198 | 6,663 | 6,669 | 199 | 397 | 0 |

**Linear, 33 characters an edit, and not bounded by what the store prunes.** A version names in
`after` every version its writer read and everything those name, so each edit adds an identifier;
the store deletes the replaced version and keeps its identifier in every later `after` and in a
deletion record, two records an edit with the replaced summary entry, and nothing shortens a list
(decision 21). A version is a new entry, so no entry's history grows. The file grew from 3,047 to
19,575 bytes over 400 edits, and the read took 0.4 to 1.5 ms.

**Where a limit is met**, from `store.py`'s strings: a version's KPG.Data passes the plugin's
ceiling of 262,144 characters (§643) at 7,940 identifiers, so at the 7,941st edit of a single row
in a single copy, and its summary item at 7,940 identifiers as well; the reader's ceiling of
1,048,576 (§607) at 31,772 and 31,771. Apart in two copies, a round of two edits adds two
identifiers. The summary entry's own content passes 262,144 at 7,942 named, and in every pattern
run the row's `after` was shorter than the summary entry's by 1 or 2, so **the edit that takes
the item past the ceiling comes while the summary entry's content still fits**: a room judged on
the summary entry's content alone lets that write through (the item is 24 characters longer than
the summary entry's content at 400 edits here), which is §644 item 31. The 32 members of a
content or an item are not approached (a version holds five), and the 9,999 item names grow by
none, a row holding a single item however often it is edited.

## 8. What decides

**Item 1.** Nothing ended wider at a single node: with four profiles and the full mask, every
history and every random merge that gave more than both copies was across nodes. Combining
versions made apart stays right with switch letters in some versions and not in others, and no
merge gave a verb or a switch beyond both copies at a node whose own rows changed. The counts are of
the same order as with two letters (33 to 129 against 67 in 200,000). **New**: a merge across
nodes can give a combination of a verb and a switch that neither copy gave while each letter came
from a copy (O6 on files, 16 of 16; 1 to 8 histories in 200,000 in the model), which a check by
letters does not see.

**Item 2.** Nearest wins per profile through versions made apart at different levels, and the cut
of a rule taken off beside a narrowing version follows the group's new rule, within both copies.
Wider: the across-nodes class of S34, now also with the group between and with the group's switch
in the place of a row (L8, L9, L10, 16 of 16 each).

**Item 3.** Nothing wider that the list causes: with a deletion record the row refuses once the
name is known; an unknown name grants nothing anywhere. A loss with no trace of a row whose profile
a rule above names is wider, the same with the name on the list (§617 accepts it).

**Item 4.** **The form of S8 is supported, and the reading as run is overturned.** With the
reading proposed, tags that meet only ever refuse more (narrower), the reading sees the meeting,
and the chance is small for the names a database holds. The reading as run is fail-open (a lost
version of the name not kept reads as never made) and unstable from process to process. A tag of
0000 has to be read as a tag like any other.

**Item 5.** Unbounded, linear, and nothing pruned; a single row edited about 7,940 times passes the
plugin's ceiling for a string in its own version and summary item before the summary entry's
content does.

**S34.** What is left of it stays what it was, rules of different nodes that do not meet, and no
run here found it at a single node. It is wider in two ways the text does not name: a combination
of a verb and a switch that neither copy gave, with every letter from a copy; and through a group,
with the group's switch in a row's place. The counts stay of the same order. Whether it can stay
accepted is the project's; the measurement leaves it the same class and asks that it be accepted in
capabilities and not in letters.

**§617.** Nothing contradicts it: two copies changing different profiles at a node do not meet
(O4), versions of a profile's rule made apart give what all of them allow with a switch counted as
a letter (16,974,336 combinations), a version that takes the rule off beside a version that grants
cuts what arrives from above (L3, L4), versions of the switch made apart refuse (L6), and a row
removed under a refusing switch is written as an empty rule and holds against the switch turned off
in the other copy (O8, L7).

## 9. Every fail-open sequence in this run

1. **Across nodes, by capabilities only** (O6 on files, 16 of 18 cells; model, 1 to 8 histories
   in 200,000): the root gives pf `ua`, N's row gives `r`; copy 0 takes N's row off; copy 1 sets
   the root to `ra`; merge: N gives pf `r+a`, which neither copy gave. Shortest in the model:
   `row 0 N pg cula ; merge kp_other 1 0 ; row 1 N pg off ; row 0 root pg rla ; merge xc 1 0`.
2. **Across nodes, by letters** (O7, L8, L9, L10 on files, 16 of 18 cells each; the model, 33 to
   129 histories in 200,000): a narrow rule taken off, or a switch turned off, at a node or a group
   in a copy while another copy widens the rule above it. Shortest with three levels: `row 0 G pf
   (empty) ; merge kp_other 1 0 ; row 0 G pf off ; row 1 root pf cula ; merge kp_other 0 1`.
3. **Tags that meet, under the reading as run** (G2 under `last`, G3 under `first`, 4 of 6 reads
   each): two names whose tags meet each have a row at N; the version of the row of the name the
   reading does not keep is deleted for good with a deletion record, with every summary entry;
   Seal again; that row reads as never made and inherits. Closed by the reading `each`.
4. **A name whose tag is 0000 under `read.py`'s form `split`** (G6, 1 of 2 reads, the store not
   serving): its row's version and every summary entry deleted with deletion records; the row reads
   as never made.
5. **A loss with no trace of a row whose profile a rule above names** (`offlist`, 26 of 300 reads,
   the control among them): the row's versions and every summary entry deleted for good with no
   deletion record, or with the records cleared; Seal again; the row reads as never made. Accepted
   in §617, and the list plays no part.
6. Every import cell that ends wider against the oracle (10 of `merges4`, 8 of `levels`): a copy
   that stays stale.

## 10. Where `STORE.md`'s text does not match the files

- **S8, "such a deletion record would refuse both rows, which is narrower (reasoning)"**: true of
  the reading proposed (`each`), measured here; false of the reading the form was run with, which
  kept a single name for a tag, chosen by the order of a set, and was fail-open for the other name.
  S8 and §644 item 24 cite that run's "same lines in 4 of 4 sequences"; those sequences hold no
  meeting tags. The plan's R4 and R5 are the reading measured here as `each`.
- **S8 and §609**: bytes 9 and 10 are 0 for every version but a row's; a row whose name's tag is
  0000 is a row all the same, and the probe's reading took it for a version of a node's whole rule
  (G6). Nothing in `STORE.md` names that value.
- **S34 and §639, "add up to more than either copy's table gave"**: measured in letters; with the
  switches of a rule a merge across nodes also gives a combination of a verb and a switch that
  neither table gave while each letter is in a table (O6), and the class reaches a group between
  the root and the node and the group's switch (L8 to L10).
- **§616, "an addition names every removal of its name that the reading it is composed over
  holds"**: where the removal is known only from a deletion record that tells of it once the name
  is known, the reading before the write does not hold it, the addition is not in force and the
  plugin's write prunes it; the name stays off the list at the first Add and comes back at the
  second (6 of 20 sequences). Narrower, but the operator's Add does not take.
- **§643 and §644 item 31**: §643 lists the `after` of each standing version among what grows;
  the run gives the count, 7,940 identifiers for the plugin's ceiling, and shows that the edit
  taking a row's summary item past it comes while the summary entry's content still fits, as item
  31 reasons.
- **§644 item 24, "a row of a name off the list whose records and items are all gone, which the
  reading that was run cannot tell from a deletion record"**: the reading cannot tell it only while
  the name is named by no row, record or item, and then the name is granted nothing anywhere; once
  the name is added back the deletion record is told and the row refuses.

## 11. What was not run, and why

- **The model's searches** (`store-search` and its variants) with four profiles, the full mask
  or three levels: each takes about twelve minutes with two profiles, and the question of these
  items, merges beyond both copies, is the question `beyond` asks.
- **The forms `void` and `limit`** of removing a single version, and a removed row written
  `again` or `off`, with four profiles: the decided forms only.
- **KeePassXC releases other than 2.8.0** in `random4`, `random3`, `offlist` and `tags` (the merge
  cells of `merges4`, `levels` and G1 hold all four releases); the slow key derivation.
- **A row set again after it refuses** (the plan's R16): the model's row act does not name a
  lost version, so a row that refuses after a name comes back stays refusing in the model; not
  measured.
- **The read in the plugin's C#**, the grant editor, a second KeePass with the plugin, a phone.
- **More than 400 edits on files**: the ceilings of item 5 are computed from `store.py`'s strings,
  not reached in a file.
- **More than four profiles, and the product's texts** (the mask's order, the object's form): the
  probe writes its own texts, as before.
