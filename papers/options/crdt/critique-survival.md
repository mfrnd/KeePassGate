# Critique of option `crdt`: survival across the real programs

Key: `crdt-survival`. Written 2026-11-15 against `option-crdt.md` as it stood at 20:03 (1073 lines)
and its model `crdt-model\crdt_model.py` (SHA-256 `ED5F93CD…4707F`), which every run below used
unchanged. The lens is survival: whether the model of what each program's save and merge do to
entries is true, what it leaves out, and what the phone programs must do for the design to hold.

## 0. How statements are marked

- **[M here, F1 to F5]**: measured by me on real `.kdbx` files in the design's shape, changed only by
  KeePassLib 2.62 (loaded from `.keepass\2.62\KeePass.exe`, no window) and by
  `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0. Output: `out\facts.txt` (section 9).
- **[M here, R1 to R9]**: a history replayed on real files through a bridge. Every gate act is the
  design's own write path (the model's code, unchanged), applied to the file as exactly the entries
  and deletion records it adds and removes. Every other step (merges, deletions, the recycle bin,
  KeePassDroid's unchanged Save, Empty Recycle Bin, cleared records, imports) is done on the file by
  KeePassLib or `keepassxc-cli`. The design's read rule (the model's fold) is then applied to what
  the file holds. Output: `out\bridge-replays.txt`.
- **[M report]**: measured in the named report: `android` is `measured-android.md` as it stands now
  (its KeePassDX section is filled); `keepass`, `keepassxc` the ground reports; `crit rules-linked`
  is `critique-rules-linked-survival.md`.
- **[R file:line]**: read. KeePass 2.62 source as the keepass ground report extracted it
  (`spikes\storage\probes\ground-keepass\src`); the design by its line numbers.
- **[model]**: the design's model, unchanged. **[model+]**: the same model with what it leaves out
  added: where an entry sits, the recycle bin, KeePassDroid's unchanged Save, and KeePassLib's rule
  for a deletion record (`model\model_ext.py`). Neither is a measurement of any program.
- **[A]**: assumed, neither run nor read.

Nothing here started a window, an emulator or a test tier. Every database was made by my probe with
invented content.

## 1. Verdict in short

**Viable with changes.** The transport the design rests on mostly holds, and I checked it on real
files rather than by reading: over 2,000 random histories (two and three copies, 9,602 real steps
through KeePassLib's five methods, the four KeePassXC releases, deletions with and without a record,
the bin, KeePassDroid's unchanged Save, Empty Recycle Bin, cleared records, imports with new
identifiers), each file agreed with the model's prediction, once where an entry sits was added to the
model, at every step but 19, and those 19 change nothing the read rule decides (P6). Union, whole
entries, a witness that only Synchronize carries, imported copies ignored: all hold.

What breaks is what the model leaves out (`option-crdt.md:806`): **where an entry sits**. The read
rule counts an element only as a direct child of the store group, refusals and removals in the bin
excepted (`:171`, `:373`). But **Delete in KeePass, KeePassXC, Keepass2Android and KeePassDroid moves
the entry into the recycle bin and writes no deletion record**, and **KeePass's Synchronize and every
KeePassXC merge carry that move into the other copy** [M here F3; M android 2.1, 3.2]. So the "removal
without a trace" that the design says only a pykeepass script makes (`:104-112`) is what the Delete
key does in every program in scope, and a merge spreads it instead of healing it. On real
files that gives: a revoked client admitted again after one Delete in a copy KeePassXC wrote, the
revocation's entry having been deleted for good where it was made (R1); a rule widened by one Delete
in a copy KeePassXC wrote (R2), and in a third copy that only ever synchronized (R9); a rule widened
using nothing but KeePassDroid and KeePass (R3); a standing refusal of renewals lost (R4). The design's
own unchanged model already fails its two summary claims (`:90-92`) on a four-step history its search
did not reach (P2).

**One change closes every one of these**, in the model and on the real files: count an element
wherever it sits in the tree, the bin included, and let only a deletion record take it away (P1).
With it, the extended model found no failure in 48,000 random histories over every configuration
tried, and R1 to R4 and R9 pass on real files. What is then left is what the design already states as
its limits, though with a wider way in than it says (P3).

Two more serious problems need their own changes: KeePass's own Import plus Delete Duplicate
Entries takes the whole store out of every copy a Synchronize reaches and leaves copies of sealed
nodes governed by their place once an authority is made again (P4); and KeePassDroid's save over a
changed file, read to happen at every save, rolls the file back, which KeePass does not repair across
a lock (P5). P5 is shared by every option inside the file. **Nothing here is fatal to this option
alone**; P5 becomes fatal to every in-file option if an arrangement lets KeePassDroid's
save land over a newer file, which the design's own measurement 16.8 decides.

## 2. What held

| Claim of the design | What was done | Result |
| --- | --- | --- |
| T1: an entry only one copy holds arrives | bridge, 9,602 real steps; F2 to F5 | held in every KeePassLib method and every KeePassXC release [M here] |
| T2: an entry both hold is replaced whole by the newer, never field by field | bridge | held for Synchronize, OverwriteIfNewer and KeePassXC; OverwriteExisting and KeepExisting choose by method, not time (P6) [M here] |
| T5, T6: records travel through Synchronize only; a copy applies its own records under every KeePass method; KeePassXC applies none and keeps the target's beside what comes back | F2, F4; bridge | held [M here], with the strict rule of F1 |
| T9: a record naming nothing survives every save and merge measured | F4: through all five KeePassLib methods both ways and four KeePassXC releases, kept by the copy that holds it; carried only by Synchronize | held [M here] |
| T11: imported copies under new identifiers are ignored | F4, F5, R8: Create new IDs also gives the store group a new identifier, so the copies land in a group without the prefix, outside the read | held [M here] |
| The head's MAC "over every `KPG.S` string in order" | a head of 12 strings written by KeePassLib and rewritten by each KeePassXC release | every program writes the strings in name order [M here, `out\order.txt`]; define the MAC over names in ordinal order and nothing depends on a writer |
| The design's scenarios 7, 9, 18, 20 on real files | R5 to R8 | as the design says: 7 incomplete, 9 widens (its stated limit 6.4 item 4), 18 and 20 hold [M here] |

## 3. Problems, most severe first

### P1, serious: Delete is a move into the bin in every program, the move travels, and the read rule takes it for a loss without trace

**What the design rests on.** "An element counts only as a direct child of such a group" (`:171`);
refusals and removals also count "in the recycle bin" (`:373`); "Not relied on: where an entry sits
… which group is the recycle bin" (`:152-154`); the model leaves out "where an entry sits and the
recycle bin" (`:806`); a removal without a deletion record is made by "a script over pykeepass … and
KeePass, KeePassXC, Keepass2Android and KeePassDroid do not (T4)" (`:104-112`).

**What the programs do.**

- Delete moves the entry into the bin, moves `LocationChanged`, leaves the modification time and
  writes no record: KeePass [R keepass 2.2, `MainForm_Functions.cs:5159-5166`]; KeePassXC `rm`
  [M keepassxc 6]; Keepass2Android, after "Press No to recycle" [M android 2.1 step 5]; KeePassDroid,
  asking nothing [M android 3.2 step 5].
- **The move travels** [M here F3]. A head binned in one copy (location time 400): KeePassLib's
  Synchronize put it into the bin of the other copy as well, in both directions; OverwriteIfNewer,
  OverwriteExisting and KeepExisting left the other copy's placement alone; every KeePassXC release
  moved it into the other copy's bin, keeping that copy's own location time. Keepass2Android's merge
  relocates by `LocationChanged` as KeePassLib does [R android 3.1, `PwDatabase.cs:837-847`; M android
  2.2 for a move, `Y3`]; KeePassDX moves an entry to the arriving copy's group at equal modification
  times [M android 4.3, `Y3`], which is always the case for an unchanged element.

So a binned element is, to the read rule, a removal without a record, and unlike one, a merge
carries it everywhere.

**Sequences, on real files** (KeePassXC was 2.6.6 in these replays; F2 to F4 show the four releases
alike on every step used) [M here]:

- **R2, one Delete widens a copy KeePassXC wrote.** (1) The gate in copy A takes the root's rule from
  `read` to a document that inherits, which at the root grants nothing. (2) In A somebody presses
  Delete on `KPG head …`: A is incomplete, correctly. (3) A is merged into a stale copy B with `keepassxc-cli merge`. B now holds the old head and rule
  and the new rule; the new head arrives in B's bin. B is incomplete, since the new rule is not
  carried by a head in the group. (4) In B somebody presses Delete on the new rule's entry.
  **Outcome: B is whole and the root grants `read` again.** The last step alone made B wider, which
  is the design's own `one-loss` property.
- **R9, the same reaches a copy that never met KeePassXC.** (1) A narrows the root to nothing.
  (2) KeePassXC merges A into stale B. (3) KeePass synchronizes B into stale C: C holds old and new,
  and no record (B had none to give). (4), (5) Delete on the new head and on the new rule in C.
  **Outcome: C grants `read` at the root.**
- R3 (KeePassDroid and KeePass only) and R4 (a standing refusal of renewals) are in P3, and R1 (a
  revocation) in P2; each ends with a Delete.

**What it costs where nothing widens.**

- One Delete of `KPG head` on the phone, followed by any Synchronize, puts the head into the bin of
  every copy [M here F3]. Every copy is then incomplete and serves nothing until Seal again.
  Section 6.2 says a merge "that brings a head covering what is present" heals it, and 6.3 that "a
  KeePassXC merge with a healthy copy heals everything" (`:478`): for a binned element both are
  false, because both merges move the healthy copy's head into the bin instead [M here F3].
- **Seal again treats intact binned decisions as lost, and the mark does not hold them.** A group's
  first rule `nothing`; Delete on its rule entry, on the head and on the group's link entry (the
  mark is an ordinary entry in the user's group, and Delete takes it the same way); Seal again.
  **Outcome: the group grants what the root grants**, with all three entries intact in the bin
  [model+]. Section 6.4 item 5 says "a rule [is kept] by its node's mark" (`:515-516`); the mark is
  as deletable as the rule.

**Change, tried.** Count an element wherever it sits in the tree, the bin and any other group
included, and let only a deletion record take it out of force; then the only removals without a
record are those of a script or a hand-made file. The cost is a walk over every entry per read;
`store-entries` measured 0.26 ms for a lookup by identifier over a tree of 10,054 entries, the figure
the design itself cites (`:674-677`), and a full walk is assumed to cost about the same [A]. With
the change, the extended model found **no failure** in any configuration (Synchronize and
Keepass2Android with the bin, KeePassDroid's Save, Empty Recycle Bin and restore;
the same with KeePassXC and the import methods; with KeePassDX; 8,000 histories each for two and
three copies) [model+, `out\ext-results.txt`, `out\ext-results-dx.txt`], and **R1 to R4 and R9 pass on
real files** [M here]. The gate's prune then also has to delete what it replaces wherever it sits,
which section 10 does not say. A narrower change, reading only the bin's subtree for every kind,
closes the Delete key but not a move to another group (Keepass2Android's "Move to another group"
[M android 2.1 step 6], KeePass's drag) nor KeePassXC's foreign "Recycle Bin" group, which arrives as
an ordinary group in a copy without a bin [M keepassxc 2 item 10, 4 `bin-A`].

### P2, serious: after a KeePassXC merge a revocation can have one carrier, and the design's own model refutes its two summary claims

**What the design claims.** "No single deletion or edit of a store entry made a copy wider than it
was" and "a revocation was undone only by a whole-file overwrite, or where the copy had never been
given it" (`:90-92`); "deleting a revocation for good, in any program that leaves a deletion record,
changes nothing" (5.2 point 3); "every decision whose loss would widen has two carriers and the
refusals a third" (point 5 of section 1).

**The design's own model, unchanged** [model]: `revoke 0 c0; del 0 (the revocation) with a record;
merge xc 1 0; del 1 (the newest head) without a record`. After step 3, file 1 refuses `c0` (its new
head carries the revocation). After step 4, **file 1 admits `c0`**: the model reports `safety` and
`one-loss` both. File 1 was given the revocation, and one deletion made it wider. The search missed
it because its exhaustive runs start from a divergence where other decisions follow the revocation,
whose entries make the store incomplete, and none of its exhaustive alphabets mixes deletions with
and without a record; the random search, which does mix them, did not draw it.

**On real files (R1)** [M here]: (1) the gate in A revokes `c0`; (2) in A somebody deletes the entry
`KPG revoked client …` (Delete) and (3) empties the recycle bin, which writes its record; A still
refuses `c0`, by the record, as 5.2 says; (4) A is merged into a stale copy B by `keepassxc-cli`: B
holds its old head, the new head carrying the revocation, and `c0`'s old admission record, and no
record of any deletion, because KeePassXC carries none; (5) in B, Delete on the new head. **Outcome: B
is whole and admits the revoked `c0`.** That is requirement E3's worst case, reached with two Deletes,
one Empty Recycle Bin and one KeePassXC merge.

**Why.** In B the revocation had one carrier. Its entry was gone before the merge, its witness and its
entry's deletion record live in A's records, which KeePassXC does not carry (T6), and B's stale head
counts as in force because nothing in B names it as replaced once the new head is out of the group.

**Change.** P1's change closes this sequence (R1 passes on real files, the binned head still
counts). With a true removal without a record (a script) it remains, which is 6.4 item 3; the
summary claims in section 1 have to say so. And the gate should write a fresh refusal entry, with a
new nonce, at its next write in any copy where a refusal in force has no entry, which shortens the
time a refusal has one carrier [A, not modeled].

### P3, serious: KeePassLib and Keepass2Android drop the gate's records whenever the entry is not strictly older, so the stale state reaches the programs in scope

**What the design rests on.** T3, KeePassDroid's Save with nothing changed: "Harmless here: the
content is the same, and identity is by content" (`:138`). 5.2 point 2: the gate's pruning leaves
records, "so an older element that a KeePassXC merge brings back stays out". 6.4 item 3: the stale
state exists in "a copy that … took the newer one from a KeePassXC, KeePassDX or import merge". The
model deletes an entry whose time equals the record's (`crdt_model.py:609`, `<=`).

**What KeePassLib does.** A record removes its object only when the object is strictly older, to the
second, and otherwise **the record is dropped** [R `PwDatabase.cs:1083-1096`], under every method,
since every method applies the open copy's records [R `:926-929`]. Keepass2Android's merge is the same
port [R android 3.1, `PwDatabase.cs:994-1023`; M android 2.2, `X2`].

- **F1** [M here]: an entry at second 100 and its record at second 100: all four non-import methods,
  both directions, **kept the entry and dropped the record**; at 101 the entry went.
- **F2** [M here]: the gate pruned an old head and an old rule version (records at 200); on the stale
  phone copy both were saved unchanged at 300, as KeePassDroid's editor Save does [M android 3.3].
  Synchronize, OverwriteIfNewer, OverwriteExisting and KeepExisting, both directions: **both old
  elements kept, both records gone**, in the gate's copy too. KeePassXC brought them back and kept
  the records beside them.

So the state 6.4 item 3 needs (old elements beside their successors, no record) is made without
KeePassXC by: KeePassDroid's Save with nothing changed, then a Synchronize; two writes of the store
in one second, with a copy taken between them [A for the timing; the rule is F1]; and two gate
hosts whose clocks differ, the slower one pruning what the faster one wrote [A for the hosts; the rule
is F1].

**R3, the programs in scope** [M here]: (1) the gate in A takes the root's rule from `read` to
nothing; (2), (3) on a stale phone copy P, KeePassDroid's editor Save, nothing changed, on `KPG head`
and on the root's rule entry; (4) KeePass synchronizes P into A: A holds old and new and no record of
the old; (5), (6) Delete on the new head and on the new rule (in KeePass on the computer, or on the
phone before the next synchronization). **Outcome: the gate's own copy grants `read` at the root.**
The design's claim that Synchronize and Keepass2Android with "deletions of any kind" never failed
(`:86-87`, configurations J2 and K) does not survive these two behaviors.

**R4, two gate copies and Synchronize only** [M here; model+ found it unprompted]: (1) gate copy B
refuses `c0`'s renewals forever; (2) Delete on B's head; (3) gate copy A, concurrently, changes the
root's rule; (4) Delete on B's refusal entry; (5) B synchronizes with A. **Outcome: B asks again about
`c0`'s renewals**, the answer intact in B's bin. Before step 5, B was incomplete; A's head made it
whole.

**Change.** P1's change closes R3 and R4 (both pass on real files). What is left is 6.4 item 3, and
its precondition should name these ways in. T3 should not be called harmless.

### P4, serious: Import with new identifiers and Delete Duplicate Entries take the store out of every copy a Synchronize reaches

**What the design says.** "The copies do not verify under their identifiers, so the store is gone in
that file: nothing served. … Closing without saving is the better remedy. Not modeled" (`:996-1000`).

**F5** [M here]: a stale backup imported into the gate's copy with Create new IDs (KeePass's
preselected method [R keepass 2.4]), then `PwDatabase.DeleteDuplicateEntries`: it deleted four
entries, **the original authority, the original profile addition, the original rule version and the
original user entry**, each with a record dated now, and kept the imported copies, because the
originals come first in tree order and the times are equal [R `PwDatabase.cs:2002`]. A third copy
synchronized with the result **lost the same originals**. The `rules-linked` critic measured the
same on its shape, links in the user's groups and marked user entries included [M crit rules-linked
C6].

**Outcome.** No copy that synchronizes has an authority, so none serves anything (closed). But:

- the store key is still in the file, in the imported copy of the authority's entry, and every
  imported element verifies under it; the design reads none of them, and offers no way back;
- the originals' marks went with them (a group's link and a marked entry are duplicates of their
  imported copies [M crit rules-linked C6]), so the original nodes are unmarked, and the copies carry
  marks naming nodes that have no rule. Section 8 gives such a copy "the original's rule as a limit"
  (`:604`), and with no rule known that is no limit: the model returns the place's grant
  [model, `crdt_model.py:276-280`]. Once the operator makes an authority again, the only way
  forward, **content that was sealed is governed by its place** until each rule is entered again.

**Change.** A press that re-mints, as new elements, what verifies under the key found in a copy of
the authority's entry; and a copy of a node whose rule cannot be found refuses. Requirement A9 (the
store survives Database Maintenance's commands, export and import) is not met without the first.

### P5, serious and shared: KeePassDroid writes over a changed file at every save, and KeePass's lock ends the repair the design counts on

**What the design says.** "While KeePass still holds the database open its next save meets the
changed file and offers Synchronize, which puts the union back" (`:100-101`).

**What is read.** KeePassDroid never checks the file before writing [R android 4.2, KeePassDroid
`edit/SaveDB.java:54-75`]. KeePass's lock closes the database and the unlock opens it again from the
file (CLAUDE.md, measured facts, "A locked document's close carries no path…", pinned by
`MainWindowDatabasesTests`) [R].

**Sequence** [A for the synchronizer and for KeePass's lock settings; the parts read]: (1) KeePassDroid opens the database on the
phone; (2) on the computer the operator revokes a client; (3) KeePass locks, as it does when idle;
(4) on the phone the operator changes a password and saves, writing the phone's older copy over the
file; (5) KeePass is unlocked and reads the file. **Outcome: the revoked client is admitted, and
nothing in the file shows that a revocation ever existed.** Every option inside the file shares this.
The design's high-water mark outside the file (6.4 item 1) would detect it and refuse, and would
not bring the revocation back.

**Rating.** Serious and shared. If a synchronizer lets KeePassDroid's save replace a
newer file rather than keep both, the hard requirement fails for every option inside the file, this
one included; measurement 16.8 is the one that decides it, and it should be run before any option is
chosen.

### P6, minor: the model's transport against the real programs

- **Equal seconds**: the model deletes at `<=`; KeePassLib keeps the entry and drops the record (F1).
- **The import methods**: the model's `kp_other` applies OverwriteIfNewer's rule to OverwriteExisting
  (the arriving entry whatever its time) and KeepExisting (the open entry whatever its time). The
  bridge saw 18 steps where the file and the model differed for this reason (8 OverwriteExisting, 10
  KeepExisting), an element's time or a hand-edited head taken or not; since elements never change,
  none altered what the read rule decides [M here, `out\bridge-random-2.txt`].
- **Create new IDs also applies the open copy's own records** to entries already there, as every
  method does [R `PwDatabase.cs:926-929`]: one step, which removed elements KeePassXC had brought back
  beside their records; benign [M here].
- **Location**: KeePassXC relocates by the later location time and keeps the target's own; KeePassDX
  follows the arriving copy at equal times; Synchronize takes the later [M here F3; M android 4.3].
  Not in the model; P1.

### P7, minor: bases to correct

- T7 (KeePassDX applies arriving records and keeps none) is now measured [M android 4.3, `X1`, `X2`].
- T9's Keepass2Android evidence: `D1` and `D2` were in both copies of the merge pair, so a witness that
  only one copy holds traveling through Keepass2Android's merge is read
  [R android 3.1, `MergeInDeletionInfo`], not measured.
- T3 rests on KeePassDroid. Whether Keepass2Android's editor Save with nothing changed moves the time
  is not measured; its edit path touches the entry [R android 1.4].
- "Not relied on: where an entry sits … which group is the recycle bin" (`:152-154`) contradicts 3.1
  and 5.2 point 3.

## 4. Program by program

| Program and operation | What it does to store entries | Basis | In the design's model | Effect on this design |
| --- | --- | --- | --- | --- |
| KeePass Synchronize (File, a save that meets a changed file) | union; newer whole; records joined, applied to strictly older, dropped otherwise; relocation by the later location time | M here F1 to F4, bridge | yes, but `<=` and no location | P1, P3 |
| KeePass OverwriteIfNewer, OverwriteExisting, KeepExisting | union; own records applied (strictly older); none carried; no relocation; the last two choose by method | M here F1, F2, F4, bridge | as one method | P6 |
| KeePass Create new IDs | copies under new identifiers in a new group; own records applied | M here F4, F5, bridge | yes, without the records | holds (ignored); P4 with Delete Duplicate Entries |
| KeePass Delete, Empty Recycle Bin | bin without record; then records dated now | R keepass 2.2, 5; M here (R1) | no bin | P1 |
| KeePass Delete Duplicate Entries after an import | deletes every original, records dated now | M here F5; M crit rules-linked C6 | no | P4 |
| KeePass Remove deleted objects information | clears every record | R keepass 5; M here (R6) | yes | the design's 6.4 item 4 |
| KeePass lock, unlock | closes the database, reopens from the file | R CLAUDE.md measured fact | no | P5 |
| `keepassxc-cli merge`, 2.4.2 to 2.8.0 | union; newer whole; no record applied or carried; target keeps its own; relocation by the later location time, target's time kept | M here F2 to F4, bridge | yes, without location | P1, P2 |
| KeePassXC `rm` | bin without record; in the bin, a record | M keepassxc 6 | no bin | P1 |
| Keepass2Android merge ("Merge changes?") | as KeePassLib Synchronize | M android 2.2; R android 3.1 | yes | P1, P3 |
| Keepass2Android Delete; delete in the bin | bin without record, time unchanged; record | M android 2.1 | no bin | P1 |
| Keepass2Android "No, overwrite" | whole file over the other | R android 4.2 | yes (`over`) | the design's 6.4 item 1 |
| KeePassDroid save | every carrier kept, records kept, node custom data deleted (unused here) | M android 3.2 | yes | holds |
| KeePassDroid Delete; delete in the bin | bin without record, time unchanged; record | M android 3.2 | no bin | P1 |
| KeePassDroid editor Save, nothing changed | new time, one history item | M android 3.3; its effect on records M here F2 | no | P3 |
| KeePassDroid save over a changed file | whole file over the other, never checked | R android 4.2 | yes (`over`) | P5 |
| KeePassDX 4.5.8 merge | arriving records applied, not kept; moves to the arriving copy's group at equal times | M android 4.3 | records yes, location no | P1 |

## 5. The phone: what the design needs from Keepass2Android and KeePassDroid

| The design needs | Keepass2Android 1.15-r6 | KeePassDroid 2.7 |
| --- | --- | --- |
| entries in the store group kept with their identifiers, custom strings byte for byte, protection flags | measured, every operation [M android 2.1] | measured [M android 3.2] |
| the store group's identifier, whatever its bytes, kept | measured for a group with a chosen identifier [M android 1.3, 2.1] | measured [M android 3.2] |
| a head of some fifty custom strings kept and editable without loss | not measured (the design's 16.6) | not measured |
| a record naming nothing (the witness) kept by a save | measured, `D1`, `D2` [M android 2.1] | measured [M android 3.2] |
| a witness only one copy holds carried by the merge, both ways | read only (P7) | no merge [M android 3.1] |
| the merge: union, newer whole, records applied and carried | measured [M android 2.2] | no merge |
| Delete leaves an element where the read rule finds it | **no**: the bin, no record [M android 2.1]; fails P1 as written | **no**: the bin, no record [M android 3.2]; fails P1 as written |
| no element's time moved without its content changing | not measured for an unchanged Save (P7) | **no**: the editor's Save moves it [M android 3.3]; P3 |
| a save never written over a changed file | asks; "No, overwrite" overwrites [R android 4.2] | **no**: never checks [R android 4.2]; P5 |
| a user entry's `KPG.Node` string kept through an edit of that entry | measured, `E1`'s extra string [M android 2.1] | measured [M android 3.2] |
| a group's link entry left alone | kept unless deleted; Delete bins it [M android 2.1] | the same [M android 3.2] |

With P1's change the two "no" rows about Delete become harmless. The KeePassDroid editor row is
harmless after P1 except as a way into 6.4 item 3 (P3). The overwrite row is P5 and has no answer
inside the file.

## 6. The sequences the task names

| Sequence | As written | With P1's change | Basis |
| --- | --- | --- | --- |
| a hand Delete (to the bin) of store entries, after a KeePassXC merge | widens (R2), reaches a third copy by Synchronize (R9) | holds | M here |
| a hand deletion for good (a record) | detected, incomplete: the same histories with records in place of the bin did not fail | holds | model+; M here R5 |
| a hand deletion without any record (a script) | the design's 6.4 item 3; and P2's history | the same | model |
| the recycle bin emptied | writes records, so what was binned then counts as deleted with a record: a refusal still refuses (R1, step 3), a head is detected | holds | M here R1; model+ |
| records cleared, then a stale copy synchronized | widens (R6), as 6.4 item 4 says | widens | M here R6 |
| an import with new identifiers | holds (R8) | holds | M here R8, F4 |
| Delete Duplicate Entries after that import | the store gone in every copy a Synchronize reaches; copies of sealed nodes open later (P4) | the same | M here F5; M crit rules-linked C6 |
| a three-way chain through KeePassXC and Synchronize | widens after two Deletes (R9) | holds | M here R9 |
| KeePassDroid's unchanged Save, Synchronize, two Deletes | widens in the gate's own copy (R3) | holds | M here R3 |
| a whole file written over another | rolls back, as stated; across a KeePass lock not repaired (P5) | the same | the design's model; R |

## 7. Claims of the design and their basis after this critique

1. `:90` "no single deletion … made a copy wider": false in the design's own model (P2) and on real
   files with the bin (R1, R2, R9).
2. `:91-92` "a revocation was undone only by a whole-file overwrite, or where the copy had never been
   given it": false (P2, R1).
3. `:104-112` the removal without a trace is a pykeepass script's: Delete makes it in all four
   programs (P1).
4. `:86-87` Synchronize and Keepass2Android with every kind of deletion never failed: false once the
   bin is in (R4), and with KeePassDroid's Save (R3).
5. `:138` T3 harmless: it drops the gate's records under every KeePass method (F2).
6. `:152-154` location not relied on: it is (P7).
7. 6.2 and `:478`, a merge heals a lost head: not for a binned one; it spreads (F3).
8. `:515-516` a rule kept by its node's mark: the mark is deleted as easily (P1, model+).
9. `:996-1000` Delete Duplicate Entries loses the store "in that file": in every copy a Synchronize
   reaches, with the key still in the file (F5).
10. `:100-101` KeePass's next save repairs an overwrite: not across a lock (P5).
11. T7 is now measured; T9's Keepass2Android merge row is read for a witness only one copy holds (P7).

## 8. What would have to be measured next

1. **The design's 16.8 first**: how KeePassDroid's save meets a changed file through a
   synchronizer, and what KeePass reads at its next unlock. It decides P5 for every option.
2. P1's change in KeePass's window: Delete and Empty Recycle Bin on store entries, Synchronize, the read
   over the whole tree, on KeePass's thread with 10,000 entries.
3. Keepass2Android: its editor Save with nothing changed; a witness only one copy holds through its
   merge in both directions; its "Synchronize database…" path.
4. Both phone programs with a head of fifty strings (the design's 16.6).
5. KeePassDX 3.2.0 merges over this shape (the design's 16.1), and KeePassDX's bin, which moves the
   entry's own time [R android 1.4].

## 9. What was run

All under
`spikes\storage\probes\crdt\survival\`:

- `probe\Crdt.cs` and `probe\host.ps1`: KeePassLib 2.62 in Windows PowerShell 5.1, compiled by CodeDom,
  driven over a pipe: make a database in the design's shape (a store group whose identifier begins
  `KPG/STOR`, a recycle bin, AES-KDF one round, password `test`), add elements and records, merge by
  any method, bin, KeePassDroid-like Save, hand edit, permanent delete, Empty Recycle Bin, clear
  records, Delete Duplicate Entries, dump. `probe\kp.py` drives it and `keepassxc-cli` (the releases
  the keepassxc ground report downloaded, under `storage\keepassxc\dl`).
- `probe\facts.py`: F1 to F5, output `out\facts.txt`.
- `model\crdt_model.py`: the design's model, copied unchanged. `model\model_ext.py`: location, the bin,
  KeePassDroid's Save, KeePassLib's record rule, KeePassXC's and KeePassDX's relocation, and the
  change of P1 as a switch; searches in `out\ext-results.txt` (16 searches of 8,000 histories) and
  `out\ext-results-dx.txt` (4 more with KeePassDX).
- `probe\bridge.py`: the design's gate over real files. `replay`: R1 to R9, each as written and with
  P1's change, output `out\bridge-replays.txt`. `random 2000 2`: 2,000 histories, 9,602 real steps,
  19 disagreements (P6), no failure of the design's checks in random histories, output
  `out\bridge-random-2.txt`.
- `probe\order.py` (pykeepass): the order of a head's strings as each program writes them,
  `out\order.txt`.
- The design's model, unchanged, on P2's history and on every single deletion after its step 3: one
  command, quoted in P2.

Total machine time about 12 minutes, one process at a time.
