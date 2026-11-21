# Measured: the lost versions a version settles, told apart from the versions it replaces

Rules are records in the store: a rule's record is a version of the object `rule:<node>/<profile>`,
and a version names in its member `after` the versions it replaces. The first ten bytes of an
identifier, its place, are the tag of its kind, the subject hash of `rule:<node>` and the tag of
the profile's name, so two names whose tags meet share the place of their rows at a node, and a
deletion record there cannot be told to be of the row of the first name or of the second. A
deletion record whose content is nowhere and that nothing accounts for is a lost version, and
refuses. This
report measures a refinement of the records before anything of it is built in the product: a
version tells apart what it replaces, in `after`, from the lost versions at its place it settles
for its own object, in a new member `lost`. It was run on 2026-11-21 in the family
`spikes\storage\probes\store-as-a-whole\` on the branch `spikes/storage-survival`, as
`model\lost_runs.py all`, with its record in `recorded-lost\`; the family's `RUN.md`, section "The
lost versions a version settles", has the commands, both runs' times and the comparisons. Nothing
ran in KeePass, in a window or on a phone: every file is the model's, written by `store.py`'s
Encoder and read by `read.py`.

**How to read it.** `p` and `q` are two names whose tags meet under the model's store key
(`q00331` and `q00534`, tag `c699`, the first such pair of `q00000`, `q00001`, ...), `r` is `pf`,
whose tag meets neither, and `z` is a name whose tag is 0 (`q184933`). The model's letters are
read and write, `r` and `w`, written `R` and `U` in the vectors. **The three behaviors**:

- **union**, the measured reading (`read.py`'s `deadall`): a deletion record is answered for every
  object it tells of once a version in force of any object names it in `after`;
- **object**, the product's reading as built: answered for an object only where a version in
  force of that object names it in `after`;
- **lost**, the refinement (section 6).

In union and object an act names the lost versions its row shows in `after`, as the product's acts
do; under lost it names them in `lost`. **The exact reading** of a file is the reading that knows
whose each version was: every deletion record told of by the object of the element it names, which
the model knows and a file does not, and answered where a version in force of that object names it
in `after` or in `lost`. A behavior is **wider** than the exact reading where it gives a capability
the exact reading of the same file does not. A row is **refused with no lost version of its own**
where it refuses under the behavior and not under the exact reading, while no deletion record it
refuses for is of a loss: a version whose content is nowhere, a version a person deleted, or a
version replaced only by versions a person deleted. An answer **changes on a write that touches no
rule** where a write of the plugin's own that adds or removes a profile name, made on a clone after
a merge, a loss or a deletion by hand, changes what the copy gives or which rows refuse. A merge is
**beyond both copies** where the merged copy gives a capability neither copy gave just before it.

## 1. What was implemented

**In the model** (`crdt_model.py`, each line marked "ADDED (lost settled)"; with the switch at its
default every output prints as before). `JUDGE`, the behavior, none by default. A version carries
`lost`. `judge_dels` tells each deletion record of a version: a version a summary entry in force
restates is lost to nothing; a version present beside its own deletion record is told of by its own
object under object and lost; any other deletion record tells of every object at its place that the
file knows, the row of every name of its tag, and under a tag of 0 the object `rule:` and the node
with no slash where the file holds a version of it. `fold(exact=True)` is the exact reading.
`names_lost` decides what an act names: the lost versions the gate's reading shows for the object,
in `after` under union and object, in `lost` under lost with what the replaced versions name in
`lost`. Every act that writes a version of a row or of a switch uses it: a row set or taken off, a
table of rows, the switch, a single version removed, the renewals of the write above (which carry
the renewed version's `lost`), Seal again, and two acts added here, `settle`, a row that refuses for
a lost version set again to the letters its standing versions all grant, and `plain`, a version of
the object `rule:` and the node with no slash. The act `lose` adds a deletion record of a version
whose content no file holds: what a version made in a copy, deleted there with its summary entry by
a script that leaves no record of the summary entry, leaves in a copy it never reached once a
Synchronize carries its deletion record. The world keeps the identifiers `lose` made and the
versions a person deleted, `loses`, which only the runs read.

**On files.** `store.py`'s Encoder writes `lost` in a version's content and in the summary item
that restates it, sorted and separated by a single space as `after` is, and leaves the member out
where it is empty. `read.py` has the same `JUDGE` and reads `lost` from a record and from an item.
`vectors.py --judge J` plays and reads every vector under a behavior; `vectors.py --lost` is the
refinement with the records, stores and sequences of section 5 added.

## 2. What was run

| Part | What it is | Size |
| --- | --- | --- |
| `hand` | the sequences of section 3, each step under each behavior, beside the exact reading, with `read.py` over the file the Encoder writes held to the model's fold | 14 sequences, 120 steps |
| `search-two` | random histories over two copies, the root and N, profiles r, p and q, 4 to 12 steps | 200,000 |
| `search-three` | the same over the root, a group G and N | 200,000 |
| `search-zero` | profiles r and z, with the object of N's rule with no slash set and lost | 200,000 |
| `search-hand` | `search-two` with a person deleting a row's newest version or the newest summary entry, with a deletion record or with none, and Seal again | 200,000 |
| `search-long` | `search-two` with 8 to 16 steps | 200,000 |
| `bridge-*` | random histories of each search's alphabet under each behavior, every file read by `read.py` and held to the model's fold after every step | 2,000 each, 4 parts |
| vectors | every existing vector under each behavior; the files of the refinement, held to the product's | 3 files |

**The alphabet of a search.** In either copy: a row of each profile at each node set to `rw`, `r`,
an empty rule or taken off; the switch of each node turned to refuse or to inherit; a row settled;
a loss of a version of each row or of the switch at each node; and six merges, Synchronize,
Overwrite if newer and KeePassXC, either way. An act is drawn with 0.6, a merge with 0.4. Every
history is played three times, a world for each behavior, on the same acts, in the form by what
arrived with a removed row taken off for real, as the vectors play it. After every step the copy it
changed is held to its exact reading, after every merge and every loss a write that touches no rule
is made on a clone, and every merge is held to both copies by capabilities.

Everything ran twice, the second run printing every output of the first line for line but the
running times.

## 3. The sequences, side by side

What N gives p and q (and G where named); a dash is nothing. **W** is wider than the exact reading,
**R** a row refused with no lost version of its own, **B** a merge beyond both copies, **C** an
answer changed by a write that touches no rule.

| Sequence | union | object | lost |
| --- | --- | --- | --- |
| **F1**, the fail-open sequence: the root grants p `rw`; N holds an empty rule for p made in copy 0 after the copies parted, deleted there for good with a deletion record and its summary entry with none; synchronized into copy 1, which wrote a summary entry of its own meanwhile; copy 1 sets q's row alone | both rows refuse; once q is set, p `rw` at N, **W** to the end | both refuse; once q is set, p refuses; q's root row refuses from the first act, **R** | both refuse; once q is set, p refuses, q `r` |
| **E1**, ordinary edits of q at N where p holds `rw`: q set, changed, taken off, set again, synchronized, emptied in copy 1, both ways | p `rw` throughout | p refuses from q's first change to the end, **R** at 7 steps | p `rw` throughout |
| **E2**, E1 with p and q swapped | q `rw` throughout | q refuses from p's first change, **R** | q `rw` throughout |
| **E3**, q edited at N and at G above it, where p holds a rule at both | no refusal | p refuses at G, then at N as well, **R** at 9 steps | no refusal |
| **L1**, a loss at p's place in both copies; q set first, then p | once q is set, p `r` (its old version), **W**; then p `rw`, **W** to the end | once q is set, p refuses; once p is set, q refuses, **R** | once q is set, p refuses; once p is set, both `rw` |
| **L2**, the same loss; p set first, then q | p first: q `r`; both `rw` | p first: q refuses; then p refuses, **R** | p first: q refuses; then both `rw` |
| **L3**, the same loss; both set in a single write | both `rw` | both refuse, each for the version the other replaced, **R** | both `rw` |
| **L4**, the same loss; q set in copy 0 and p in copy 1, then both ways | copy 0: p `r`, **W**; merged both `rw` | merged both `rw`, **C**: the next write refuses both | merged both `rw` |
| **T1**, the sequence of the product's test: both rows set in copy 0, synchronized into copy 1 and its next write; a loss at p's place in copy 0; p set, then q; synchronized into copy 1 | copy 0 and merged both `rw` | copy 0: p refuses after q is set; merged p `rw`, **B** for p, **C**: the next write refuses p | copy 0: both `rw`; merged both `rw` |
| **T2**, T1 with q set before p | once q is set, p `r`, then `rw`, **W** to the end | merged **B** for q, **C** | as T1 |
| **T3**, T1 with the copies exchanged | as T1 | as T1, **B** and **C** | as T1 |
| **Z1**, z (tag 0) and the object of N's rule with no slash, each set and changed | z as set | z refuses after each change of the object with no slash, **R** | z as set |
| **Z2**, a loss at z's place, which the object with no slash shares; z set again | after the loss both refuse; once z is set nothing refuses | z as set; the object with no slash refuses | z as set; the object with no slash refuses |
| **W1**, the switch edited, a version of it lost, the switch set again | the same in all three: the switch refuses for the loss until set again | | |

Under lost no step of any sequence is flagged; `read.py` over the files agreed with the model's
fold at every step of every behavior. **F1 is the measured reading's fail-open**: q's version
names the lost identifier, every object at the place takes it as answered, p's row reads as never
made and p gets what the root gives. **E1 to E3 are the product's ordinary edits**: a deletion
record of q's replaced version, named only by q, refuses p at every edit of q. **T1 is the
product's failing test**: in the merged copy the earlier summary entry of copy 1 still restates
the versions each row replaced, so the deletion records the writes in copy 0 left are beside
restated versions and refuse nothing, p gets `rw` where copy 0 gave nothing and copy 1 gave `r`,
and the next write deletes that summary entry and p refuses again.

**What the product's test expects changes under the refinement.** In
`Two_names_whose_tags_meet_both_refuse_for_a_lost_version_and_a_row_set_again_refuses_the_other_for_what_it_replaced`,
after `row 0 N b rl` the first name gives `RL` and not nothing, and after the last `sync 1 0` copy 1
gives the first name `RL` as well: the deletion record that `b`'s write leaves is named in `after`
by `b`'s version, which answers for every row at the place. The rest of the test reads as it does.

## 4. The search

| Search, 200,000 histories each | union: failing (wider) | object: failing (refused, changed) | lost: failing |
| --- | --- | --- | --- |
| two: root and N, 4 to 12 steps | 9,847 (9,847) | 104,015 (101,624; 55,708) | **0** |
| three: root, G and N | 7,236 (7,236) | 80,472 (78,413; 43,032) | **0** |
| zero: r and z, the object with no slash | 336 (336) | 9,956 (8,930; 3,529) | **0** |
| hand: a person's deletions and Seal again | 5,748 (5,748) | 77,420 (74,299; 40,264) | **0** |
| long: root and N, 8 to 16 steps | 19,436 (19,436) | 137,012 (133,031; 89,373) | **0** |

**The bounds**: two copies; two or three levels (the root and N, or the root, G and N); three
profiles, two of them names whose tags meet and the third `pf`, or `pf` and the name whose tag is
0; the letters read and write; 4 to 12 steps, or 8 to 16; 84, 126, 68 or 114 acts and six merges;
1,000,000 histories in all, 8,799,454 steps, 687,135 histories with a loss or a person's deletion
and 495,284 with a row settled. Under union every failure is wider than the exact reading, and
none refuses or changes on a write; under object none is wider, nearly every failure is a row
refused for another name's replaced version, and a third to two thirds of them also change on a
write that touches no rule. Under lost **no history fails, in any of the five searches**.

**Every merge beyond both copies under lost is one the exact reading accounts for.** Of the
histories with such a merge, the exact reading of the merged copy gives beyond both copies as well
in some, and in the others the merged copy gives no more than one of the copies gives read
exactly. By merges, of each class, by Overwrite if newer, KeePassXC and Synchronize:

| Search | lost: exact beyond as well, histories (merges by program) | lost: within the exact readings, histories (merges by program) |
| --- | --- | --- |
| two | 1,316 (785, 720, 0) | 577 (314, 360, 0) |
| three | 1,506 (1,007, 1,087, 1) | 610 (443, 417, 0) |
| zero | 1,186 (639, 699, 0) | 277 (126, 151, 0) |
| hand | 874 (506, 501, 0) | 409 (266, 253, 2) |
| long | 2,745 (1,593, 1,521, 0) | 1,243 (712, 722, 0) |

The first class is, but for a single history, §639 row 2 with a loss in the place of a decision: a
version lost in a copy, its deletion record kept there, and a merge into the other copy that
carries no deletion record; the reading that knows whose each version was ends there too, and the
counts under the three behaviors are within 5% of each other (1,267, 1,298 and 1,316 in
`search-two`), the worlds parting where their acts write differently. Its shortest: `lose 1 sw:N ;
row 1 root r rw ; merge kp_other 0 1`. **The single Synchronize** (`search-three`) is the class
§639 keeps for what the record of what arrived leaves: `row 0 G p r ; row 1 N p (empty) ; merge xc
0 1 ; row 1 N p off ; lose 1 sw:G ; merge kp_sync 0 1`. N gives p `r`, what N's own version
recorded when its rule was taken off; copy 1 gave nothing there for the loss of G's switch, copy 0
for N's empty rule. It ends so under every behavior and in the exact reading, with no tag in it.

The second class is the narrowing that tags that meet add: the copy that holds the deletion record
of q's lost version refuses p as well, the merge does not carry that record, and the merged copy
gives p what its own rules give, as the copy that held the record gives p read exactly. Its
shortest: `lose 1 N/q ; row 1 root p rw ; merge kp_other 0 1`. **The two Synchronize merges**
(`search-hand`, a single history, at the root and at N) are the same narrowing taken away by what
a Synchronize brings: `row 1 root p rw ; del 1 (head new) 0 ; row 0 root r off ; del 1 (ver
rule:root/q new) 1 ; merge kp_sync 0 1`. Copy 1 deleted q's root version with a deletion record,
and its summary entry with none, so it refused p and q at the root for a deletion record whose
content it no longer held; copy 0's summary entry still restates q's version, so the merged copy
holds its text and refuses neither, and gives p the `rw` copy 1 set. Under object, Synchronize ends
beyond both copies in 290, 238, 3, 138 and 576 merges of the five searches: the shape of T1.

## 5. The vectors

**Every vector the product holds today prints the same bytes under every behavior**: `vectors.py
--judge union`, `--judge object` and `--judge lost` print `StoreRecordVectors.json`,
`StoreMeaningVectors.json` and `StoreWriteVectors.json` byte for byte as `vectors.py` prints them,
and those are the product's files but their line ends. No existing sequence sets a row while a lost
version stands, no existing store holds two names whose tags meet, and no existing record holds
`lost`: so no vector changes, and none is listed.

**The files of the refinement** (`recorded-lost\vectors\`, printed by `vectors.py --lost`) hold
every member of the product's files unchanged, and add:

- `StoreRecordVectors.json`: 8 of 36 records. Three versions whose content no store holds, so that
  their identifiers are lost (a row and a rule taken off at the place of pf's row at N, and N's
  switch); a row replacing a version and settling a lost version; a row settling two lost versions
  and replacing none; a row replacing a version that settled a lost version, carrying it on; the
  switch replacing a version and settling a lost version; and a summary entry of height 1
  restating three of those, each item holding `lost` as its version does.
- `StoreMeaningVectors.json`: 14 of 84 stores, read under the refinement, with two names whose tags
  meet under the key `one` (`q00362` and `q00757`, tag `f586`) and a name whose tag is 0 there
  (`q04543`): a version of q replacing a version, beside its deletion record, both rows granting; a
  deletion record whose content is nowhere, both refusing; settled by p alone (q refuses), by p and
  by q (neither), and by p beside a version of q replacing a version (q refuses); the merged copy of
  the product's test before its next write and after it, both rows granting in both; a version
  beside its deletion record named in `lost` by its own row (not lost) and by the other row (its own
  row refuses); a deletion record named in `after` by a version at another node (both refuse); a
  tag of 0 beside the object with no slash, three stores; and a lost version of the switch settled
  by the switch.
- `StoreWriteVectors.json`: 14 of 49 sequences, the sequences of section 3, played by the model
  with the tags of names under the key of its label `K`.

Each file's `about` is the text the product holds, with 982, 1,512 and 1,784 characters added after
it. The writes file is about 6.1 MB, against 4.3 MB today.

## 6. The rule, as text the contract can take

**What a version names.** A version of a profile's row or of a node's switch holds, beside
`object`, `after` and `doc`, a member `lost`.

- **`after`** names the versions of its own object it replaces: each version of that object that
  stood when the write was composed, as a record in force or an item of a summary entry in force,
  which the tab showed, and every identifier those name in `after`. The writer knows each to be its
  own object's, since each stood with its content.
- **`lost`** names the lost versions at its place the write settles for its own object: each
  identifier the tab showed lost for that row or that switch, and every identifier the versions it
  replaces name in `lost`. The member is absent where it names nothing, so every record written
  where nothing is lost keeps its bytes.
- Each list holds identifiers of 32 lowercase hexadecimal characters, each once, in ascending
  ordinal order, separated by a single space. No identifier is in both lists.
- **Every later version carries both on**: a version that replaces versions names in `after` what
  they name in `after` and in `lost` what they name in `lost`. A renewal by the write above names in
  `after` the version it renews and that version's `after`, and in `lost` that version's `lost`.
  Removing a single version where several stand writes each version left naming in `after` the
  removed version, the version left and what each names in `after`, and in `lost` what each names
  in `lost` and the lost versions the row shows. Sealing the store again writes, for each object
  that refuses for a lost version, a version naming in `after` the versions of that object in force
  and what they name in `after`, and in `lost` its lost versions and what the versions of it name in
  `lost`.
- **The item `version` of a summary entry** restates `lost` as the version holds it, and holds none
  where the version holds none.

**What a deletion record under the tag of `version` refuses.**

1. Where an item of a summary entry in force carries its identifier as `id`, nothing: the
   version's text is held.
2. Otherwise, where the record of a version is present under its identifier, beside it, it is
   told of by that version's own object alone, with nothing hashed, and refuses that object unless
   a version in force of that object names it in `after` or in `lost`.
3. Otherwise it tells of every object at its place that the file knows (§609, §617): the row of
   every name of its tag at its node, and with 0 in bytes 9 and 10 the object `rule:` and the node
   with no slash where the file holds a version of it; or, under the subject of a switch, the
   switch. Where a version in force of an object it tells of names it in `after`, it refuses none
   of them: it is known whose version it was, the namer's. Otherwise it refuses each of them but
   those of which a version in force names it in `lost`: it is settled for that object alone.

A version of an object at another place that names the identifier refuses nothing and answers
nothing; no writer names so.

**What a write deletes.** Step 3 of §627 as it stands: a version that a version in force of the
same object names in `after` is deleted, with its deletion record. **`lost` replaces nothing.** An
identifier named in `lost` has no entry in the copy whose tab showed it lost, and keeps the deletion
record it has there; the write deletes nothing for it and adds no deletion record for it. A version
that later turns up under such an identifier with its content, and that no version names in
`after`, stands beside the versions that settled it, as a version made apart does, and with them it
can only narrow.

**What a record costs.** `lost` adds `,"lost":"`, the identifiers and `"` to a version's content:
33 characters for each identifier, 32 and a separator, and 9 for the member, so 33n + 9 for n
lost versions, and nothing where none is settled; its summary item grows by the same. Against the
product's records today, which name those identifiers in `after`, it is 9 characters more where
`after` names something, and 10 where it names nothing, since an empty `after` stays (the records
of section 5: 195 against 186 characters, 229 against 220, 197 against 187, 196 against 187).
Every later version of the object carries the list on, as the product's `after` carries the same
identifiers today, so the growth is today's but those 9 or 10 characters a version.

## 7. What the refinement leaves open

- **A merge that carries no deletion record** (Overwrite if newer, Overwrite existing, Keep
  existing, KeePassXC): a loss in a copy does not reach the other copy, which is §639 row 2, and
  the narrowing that tags that meet add is not carried either (section 4). Synchronize carries
  both, and takes that narrowing away only where the merged copy holds the text of the version the
  deletion record names.
- **A loss settled for a single name refuses the other name** until its row is set as well: the
  file cannot tell whose the lost version was. In L1 to L4 nothing refuses once each row at the
  place is set.
- **A lost version whose content turns up again** stands beside the version that settled it, and
  narrows the row; the refinement replaces nothing by `lost`.
- **What a dialog shows**: the model composes every act at its write, so the lost versions an act
  names are those of the write's own reading, where the product names what the tab showed at the
  press; a deletion record that lands under an open dialog was not run.
- **Three copies**, more than three profiles, the full mask in a search, a tree of more than three
  levels, and every file of KeePass or KeePassXC: the files here are the model's, written by the
  Encoder and read by `read.py`.
- **The records written by the product today** name lost identifiers in `after`. Release 0.3.0 is
  not out, so no store in use holds such a record (§645); a store that did would have each such
  identifier read as a version replaced, answered for every object at the place, as the measured
  reading answers it in F1.

## 8. What was not run, and why

- KeePassLib and `keepassxc-cli` merges of real files, the product's own reading and the grant
  editor: the refinement is measured here before anything of it is built.
- A deletion record landing under an open node dialog, and the tab's own view at the press: the
  model has no buffer between a press and its write.
- Three copies and the longer bounds beyond 8 to 16 steps: left for time.
