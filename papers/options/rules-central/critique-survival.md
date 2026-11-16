# Critique of `rules-central`: survival across programs

Written 2026-11-15 against `option-rules-central.md` as it stands, KeePassGate master `a42e38aa`,
KeePass 2.62. The lens is survival: every save and merge in the ground reports, walked through the
design's datum 7 (and its one line on datum 8), looking for a sequence that loses, rolls back,
duplicates or resurrects a rule, and for what the gate then does. Data 1 to 6 are not this
option's subject. The design's own list of what it does not solve (its section 13) is repeated only
where it understates.

## How statements are marked

- **[M here]**: run for this critique, from a console, over databases made with invented content.
  `...` below is
  `spikes\storage\probes`.
  Probe `spikes\storage\probes\rules-central\survival\scripts\Rcs.cs` and `scripts\pk_del.py`; output
  `spikes\storage\probes\rules-central\survival\out\chains.txt`; databases under `spikes\storage\probes\rules-central\survival\work\c1`
  to `c5`. Section 8 describes the runs.
- **[R+model]**: KeePassDX 4.5.8's merge, which no console can run, executed as a model I wrote from
  its source (`DatabaseKDBXMerger.kt` at tag 4.5.8, lines in section 8). What such a line shows is
  what the source says, read and then executed. It is not a measurement of KeePassDX. The steps
  around it (KeePassLib, `keepassxc-cli`, pykeepass, the design's read rule) are [M here].
- **[M design]**: the design's own probe output, `spikes\storage\probes\rules-central\design\out\keepass.txt` and
  `xc-*.txt`, which I read.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M store-entries-survival]**:
  measured in that ground report or critique.
- **[R ...]**: read from source, file and line. "Read here" means I read it myself in the clone or
  archive a ground report left under `...`.
- **[A]**: assumed. Neither run nor read.

**The probe's setup in every chain.** Group U carries a rule giving the profile `agent`
`[read, update, list]`. Entry X in U carries `RX1`, `agent: [read, list]`; in chains C2 to C5 also
`RX2`, `agent: [list]`, which replaces RX1. "Wider" below always means what `agent` reaches at X.
The read rule, the meet and the repair are implemented as the design's sections 3 and 5.2 describe
them, with `none` for a first rule's `KPG.After` as the design specifies (its own probe wrote an
empty value).

## 1. Verdict

**Viable with changes.** The core holds where the design says it is measured. A rule version is an
entry that is only ever added, and every merge measured or read adds an entry the other copy lacks.
Nothing the read path decides depends on a modification time, a history item, custom data or a
per-key time. So what broke today's store does not reach this one: node custom data deleted on a
save, a stale rule under a newer time, a group's rule taken from whichever copy was touched last.
Section 2 lists what survived the attack.

What fails is everything that removes an entry, and the design's own repair. Both fail toward
access:

1. **P1, serious.** The only evidence that a node's newest version ever existed is its entry, or a
   deletion record naming it. KeePassDX's merge applies deletion records and keeps none. pykeepass
   writes none. And the plugin itself makes such deletions. The copy that results applies an older
   rule, or the group's, with nothing refusing and nothing shown. Three chains end that way: the
   Rules page's Remove (C1), one deletion by hand of a rule entry (C2), and one deletion of the
   `KeePassGate` group (C4).
2. **P2, serious.** The repair of section 5.2 is written by any OK on a refusing node's dialog. It
   holds "the newest version that still reads", which after a loss is the older, wider rule, or no
   rule. And it names the lost version in `KPG.After`, so every merge carries the rollback into
   copies that still hold the narrowing. Measured under all four KeePassLib methods and with
   `keepassxc-cli` 2.4.2 and 2.8.0 (C3, C4, C5).

Each has a change that fixes it or bounds what is left. Five minor problems follow, P3 to P7. None
is fatal: the residue after the changes is a chain of two foreign programs in one copy, beside the
whole-file overwrite every option shares.

## 2. What survived the attack

| Claim | Result | Basis |
| --- | --- | --- |
| A version only one copy holds arrives in the other | held. The repair's version arrived in a stale copy under all four KeePassLib methods that match identifiers and under `keepassxc-cli` 2.4.2 and 2.8.0 | [M here, C3]; [M design, 224 cells] |
| Edits of the governed node or its group in another copy leave its rule alone | held | [M design, scenarios 01, 02] |
| Ties, rewritten times, history limits | decide nothing for a rule entry, which never changes | [M design, 02, 06]; follows from [M keepass 1a] |
| Clocks that disagree | decide only whether a deletion lands. KeePass drops a record whose entry is newer [M keepass X3]; KeePassDX deletes only an entry older than the record (`DatabaseKDBXMerger.kt:317-323` at 4.5.8, read here). A deleting device whose clock is behind fails to delete, which keeps the rule. No case found where a clock widens | [R here]; [M keepass X3, X4] |
| `KPG.After` written as `none` | read back intact after `keepassxc-cli` 2.4.2 and 2.8.0 merges | [M here, C1, C3] |
| A deleted newest version refuses | held in every copy that keeps the record: KeePass into the deleting copy under every method, Synchronize both ways, Keepass2Android [R android 3.1], keepass-rs 0.17.0 (`src/db/merge.rs:438-462`, read here) | [M design, 07]; [M here, C1, C2, C4] |
| A KeePass Synchronize heals a loss made by KeePassDX or pykeepass | held whenever the copy merged in still holds the record or the lost version | [M here, C1, C2] |
| KeePass, then Keepass2Android, then KeePass | the union, with both copies' deletion records joined | [R android 1.5, 3.1] |
| A rule entry deleted by hand comes back through KeePassXC | held, which is the safe direction | [M design, 07] |

## 3. Problems

### P1, serious: a lost newest version leaves no trace in the copy KeePassDX writes, and the plugin makes such losses itself

**What the design rests on.** Section 3 point 6: a deletion record bearing the mark and N's tag,
naming neither a present version nor a replaced one, makes N refuse. Section 8: "Deletes it for
good: the node refuses". Section 13 lists "a rule entry removed with no trace" and adds: "Each
needs a deletion of a rule entry first, which no program measured does on its own."

**Why that does not hold.**

- **KeePassDX consumes deletion records.** 4.5.8 applies each arriving record through `deleteEntry`
  (`DatabaseKDBXMerger.kt:317-323`), which calls `removeEntryFrom`; that adds no record
  (`DatabaseKDBX.kt:800-807`), and nothing in `merge()` (`:190-312`) adds one. 3.2.0 does the same
  (`DatabaseKDBXMerger.kt:274-300` at 3.2.0). All read here. So a deletion recorded properly in one
  copy becomes a deletion without trace in every copy a KeePassDX merge carries it into.
- **pykeepass deletes without a record** [M here, C2: "deletion records 0 -> 0"].
- **The deletions do not need a person tidying a phone.** The Rules page's **Remove** (sections 5.3,
  5.4) deletes rule entries of nodes absent in this copy. KeePass's Delete Duplicate Entries
  deleted the originals after an import [M design, 6.4]. One permanent deletion of the
  `KeePassGate` group writes a record for every version at once (`PwGroup.cs:1367-1386`, called
  from `MainForm_Functions.cs:5257` at 2.62, read here).
- **The entries invite deletion by hand.** Every version of a node carries the same title,
  `KPG rule` and eight hex digits (section 2.2), with an empty user name and password. A node
  changed three times shows as three entries with one title. That a person deletes such
  "duplicates" is [A].

**Sequence C1: the plugin's own Remove** [M here; the KeePassDX step R+model]:

1. Copy A: X is deleted for good. The Rules page counts X's rule entry as a rule without a node,
   and the operator presses Remove, which deletes it with a record.
2. Copy B, on a phone, changed X's password later and never saw the deletion.
3. KeePassDX merges A into B. X stays, being newer than its record. X's rule entry goes, and no
   record is kept.
4. KeePass opens B's file. **agent at X: `[read, update, list]` from U**, where B had applied
   `[read, list]`. Nothing refuses, and the Rules page has nothing to count.

Beside it [M here]:

- KeePassLib Synchronize, B open: X refuses. Overwrite if newer and Keep existing, B open: B keeps
  RX1. This matches the design's scenario 13.
- `keepassxc-cli` 2.4.2 and 2.8.0 merging A into B's file afterwards leave the widening in place,
  because KeePassXC carries no deletion record (0 records in both results).
- KeePassLib Synchronize of B's file with A puts the refusal back.

So the widening holds for as long as KeePass serves B's file without merging the copy that holds
the record. In the ordinary arrangement of one file shared by a desktop and a phone, that is from
the next time KeePass opens the file [A].

**Sequence C2: one deletion by hand** [M here; the KeePassDX step R+model]:

1. Copy P: X's newest version, RX2, is deleted for good. This could happen in KeePass, in KeePassXC,
   which writes a record when an entry is deleted from its bin [M keepassxc 6], or in
   Keepass2Android [R android 4.2]. P refuses at X.
2. KeePassDX merges P into its own copy Q. RX2's entry is removed and no record is kept.
3. RX1 is the newest version again. **agent at X: `[read, list]`**, where it was `[list]`.

pykeepass does it in one step: its deletion of RX2's entry leaves no record, and the file serves
`[read, list]` [M here]. A KeePassLib Synchronize of either file with P refuses again; with the
base, which still holds RX2, it restores `[list]` [M here].

**Sequence C4: the whole group, and a grant made later** [M here; the KeePassDX step R+model]:

1. Copy G: the `KeePassGate` group is deleted for good with KeePass's own `DeleteAllObjects`, which
   leaves three rule-entry records and the group's. G refuses at U and at X.
2. Copy Q never saw that. A minute later its operator widens U's rule to add `create`.
3. KeePassDX merges G into Q. Every older version goes, and so does the group: `deleteGroup`
   (`:358-395`) moves the one entry no record names, Q's new version of U, to the root. No record is
   kept.
4. **agent at X: `[create, read, update, list]` from U**, where Q had `[list]`. The store group is
   absent, there are no records, and nothing refuses.

KeePassLib Synchronize of Q and G refuses at X [M here].

**What the gate does.** It serves an older rule, or the group's grant, at X from the first request
after KeePass opens that file. There is no refusal, no band on the node's tab (section 5.2 has
nothing to show) and nothing on the Rules page. The audit records the grant, not a loss.

**How section 13 understates.**

- "No program measured does on its own": the plugin's Remove does (C1), KeePass's Delete Duplicate
  Entries did in the design's own run (6.4), and one deletion of the group does it for every rule
  (C4).
- The trace is lost in the copy KeePassDX writes even when the deletion was recorded properly
  elsewhere, and that file is the one KeePass opens when the phone saved last.
- The loss is not shown anywhere. Section 8's "every refusal is shown, counted, named and logged"
  has nothing to show.
- No remedy is proposed.

**Change.**

- **Drop Remove.** A rule whose node is gone grants nothing (section 6.3), so keeping it costs one
  small entry. Removing it is the one deletion of a rule entry the plugin itself would start.
- **Give every version a second witness that a deletion of entries does not reach.** For example an
  add-only key in `PwDatabase.CustomData`, `KPG.Rule.<version id>`, written in the same turn as
  the entry and never changed or removed. A version that has a key, has no entry and is named by no
  present version refuses at its node; the identifier carries the node's tag.
  - Why the two carriers fail apart: KeePassDX keeps and merges database keys by their own times
    [M spike, 18 of 18 merges] while it consumes records; pykeepass keeps database custom data
    [M others] while it deletes entries without records; KeePass keeps an add-only key under every
    method [M spike, 70 of 70 cells]. KeePassXC from 2.4.2 replaces the whole dictionary in a merge
    that loads a second later [M spike], but it never applies a deletion to an entry [M keepassxc 2].
  - What is left: both kinds in one copy, in order. A KeePassDX merge or a pykeepass deletion, then
    a KeePassXC merge from a copy older than the version. Section 13 has to say so.
  - What it costs: KeePass stamps a time on every key it sets [R inventory P17], which takes the file
    to KDBX 4.1 [R inventory P12], and kdbxweb writes such a file so that KeePass 2.62 cannot open it
    [M others]. A key without a time needs `StringDictionaryEx`'s internal setter, which the spike's
    probe reached by reflection (`Program.cs` 21-25, as `ground-measured.md` 6.1 cites it); whether
    the plugin may do that is a decision, not a measurement. Database Maintenance's write-back on a changed key count also
    comes back (the trap section of CLAUDE.md).
  - A weaker witness that needs no 4.1: each version written as two entries in two groups, with
    different identifiers and different `KPG.Kind`, so that Delete Duplicate Entries never pairs
    them [M keepass X10 for what it compares]. One hand deletion, one KeePassDX merge or one group
    deletion then leaves the other. Two deletions do not.
- **Correct** section 13, section 8's "Deletes it for good" row, section 3's third property and
  section 7's KeePass row (section 7 of this critique).

### P2, serious: the repair writes the older rule on any OK, and its `KPG.After` carries that into every copy

**What the design says.** Section 5.2: in the refusing state "the table shows the newest version
that still reads", and "in both, OK writes a repair: one version whose `KPG.After` names every
newest version, every entry changed by hand and every unanswered deletion record carrying the
node's tag". Section 5.1: OK applies the buffer, and an untouched buffer reads "the document in
force for the node's own versions".

**Four facts, together.**

- After the newest version is deleted, the newest that still reads is the one it replaced, which
  is wider, or none at all.
- After the newest version is changed by hand, it is not a version (section 3 point 6), so the same
  holds. In C5 the hand edit was one added space, and the table shows RX1.
- The repair names the lost version in `KPG.After`. To every copy that still holds that version, it
  is now replaced.
- Any OK on the dialog writes the repair. Nothing in section 5.2 requires the tab to have been
  touched, so an operator who opens X to change its password and presses OK has repaired X.

**Sequence C3** [M here]:

1. Copy P: RX2 is deleted by hand, with a record. P refuses at X: closed, as designed.
2. The operator opens X in KeePass, changes its password and presses OK. The repair holds RX1 and
   names RX1 and RX2. agent at X in P: `[read, list]`.
3. Copy S still holds RX2 and never saw the deletion. agent at X in S: `[list]`.
4. P is merged into S: KeePassLib Synchronize, Overwrite if newer, Overwrite existing and Keep
   existing; `keepassxc-cli` 2.4.2 and 2.8.0; and KeePassDX [R+model]. **agent at X in S:
   `[read, list]` in all seven.** S held the narrowing and lost it to a merge.

**Sequence C5** [M here]: RX2's `KPG.Data` is retyped by hand with one added space. X refuses. An OK
on X's dialog repairs it to RX1, so agent at X is `[read, list]`. A KeePassLib Synchronize with the
unrepaired copy keeps `[read, list]`.

**Sequence C4, continued** [M here]: after the group's deletion, the operator gives U its grant
again on U's dialog, then presses OK on X's dialog to save a new password. No version of X reads,
so the repair writes `{"default":"inherit"}`, naming RX1 and RX2. agent at X: `[read, update, list]`
from U. A `keepassxc-cli` 2.8.0 merge of that file into the base copy, which held RX2, gives the
same.

**What the gate does.** Access at X widens in this copy at once, and in every copy merged with it
afterwards, with nothing refusing. A refusal, which is what the design relies on to meet E6, is
turned by the plugin's own write into the rollback E5 forbids. And no other sequence, in the design
or here, takes a narrowing away from a copy that still holds it.

**The other reading.** If "the newest version that still reads" means the changed entry's own text
when that text parses, then a hand widening, someone on any device editing `KPG.Data` to grant
more, becomes the repair on the next OK. Either reading widens in some case.

**Change.**

- **Write a repair only on an act aimed at it** on the KPG tab, a Settle press, never on the
  dialog's OK alone. Until then the node refuses.
- **Start the refusing table from a seal**, `{"default":"deny"}`. Show beside it every text the copy
  still has: the older versions, the changed entry's text, and that entry's own history item, which
  an edit makes first in KeePass [R keepass 1a] and in KeePassXC [M keepassxc 6]. And say that the
  lost text is not in this copy and may be in another.
- **Never name in `KPG.After` a version the plugin has not read.** Answer an unanswered deletion
  record with a list of its own, for example `KPG.Lost`, which stops the record refusing in this
  copy and replaces nothing. A copy that still holds the lost version then has two newest versions
  and applies their meet, which is narrower than either (section 3 point 5).

### P3, minor: the plugin's deletions never reach a KeePassXC copy, so a repaired node refuses again at every KeePassXC merge

**Measured** [M here, C5]: the repair deleted the hand-changed entry with a record. `keepassxc-cli`
2.8.0, merging the unrepaired copy into the repaired one, brought the changed entry back (its report
says "Hinzugefügt", added), and X refused again. Section 3 point 6 counts a changed entry as damage
even when a present version names it. KeePassLib Synchronize deleted it again and X stayed
repaired.

**The same mechanism** returns the copies that "Remove copies" deleted, and with them the
database-wide `503 undecided` [A, from M keepassxc 2: no KeePassXC merge applies a deletion].

**What the gate does**: it refuses, which is closed. But while P2 is unchanged, every return is one
more OK that writes the older rule.

**Change**: a changed entry that a present version names (or that `KPG.Lost` names) is answered and
is not damage. Copies the operator already removed should count as answered rather than close the
database again.

### P4, minor: KeePassDX 3.2.0 keeps rules out of a copy whose store group was deleted, and loses every rule entry of a moved group

Read here, `DatabaseKDBXMerger.kt` at 3.2.0:

- **A deleted store group stays out.** `mergeGroup` (`:431-472`) adds a group the open copy lacks
  only when the open copy holds no newer deletion record for it. `mergeEntry` (`:329-400`) does not
  add an arriving entry whose parent is absent. So once the `KeePassGate` group is deleted in a
  KeePassDX 3.2.0 copy, no later merge into that copy brings in a rule entry, ever. Its old nodes
  refuse, by their records; every node ruled first since then inherits. Section 7's 3.2.0 row, "the
  group exists in every copy that holds a rule", fails for that copy.
- **A moved group is rebuilt empty.** When the arriving group is newer and has another parent,
  `mergeGroup` (`:455-469`) takes the open group out with its entries and puts in a fresh copy.
  `updateWith(..., updateParents = false)` copies no children (`GroupVersioned.kt:54-63`). An entry
  whose arriving copy has the same time is then left on the detached group, because `mergeEntry`
  does nothing on equal times (`:380-398`). That it is then not written is [A]: the writer walks the
  tree. Rule entries never change, so every one has the same time in both copies.

**What the gate does.** In the first case: refusals at old nodes, and every rule written since is
missing in that file. In the second, every rule is gone: if every grant goes with them, nothing is
reached (closed); if the arriving copy had written a grant the open copy lacks, that grant survives
and the narrowings do not [A].

**Change**: nothing in the design can stop it. Section 1 point 6 ("place means nothing") and
section 7's 3.2.0 row should name it, and section 16 item 1 should run both cases.

### P5, minor: kdbxweb drops a remade store group, which closes rather than widens

The design makes the same group again, under the same identifier, with the first rule after a
deletion (section 2.1, by way of `option-store-entries.md` section 5). kdbxweb 2.1.1 drops a group
remade that way whenever a copy holding the group's deletion record takes part, children and all,
with nothing recorded for them [M store-entries-survival, P3].

For rules this closes. Every version written before the deletion is named by a record, since KeePass
records every entry of a deleted group (read here, above). So every node ruled before then refuses,
there and below, and versions written after the remake are gone with the group [A, from the measured
drop and section 3's rule]. The cost is availability: the gate refuses until a KeePass Synchronize
brings the group back.

**Change**: this design reads rule entries wherever they are (section 1 point 6), so the group needs
no fixed identifier. Make a new one each time it is made.

### P6, minor: Delete Duplicate Entries turns a copy of a governed entry into its replacement

Section 13 says a copy of a governed node carries no rule. KeePass's Delete Duplicate Entries, given
two entries with equal strings, deletes the first when its modification time is older or equal,
preferring one in the recycle bin (`PwDatabase.cs:2001-2027` at 2.62, read here). A copy pasted in
KeePass gets a new identifier (`EntryUtil.cs:271`, read here) and the same strings. Its times, and
its place after the original in the list, are [A]. So a later run deletes the governed original
with a record and keeps the copy, which is governed by its group. The original's rule entries then
count as rules without a node, which the Rules page offers to Remove (P1). The design measured
exactly this after an import (its section 6.4).

**What the gate does**: the entry's secrets are reached under the group's wider grant.

**Change**: the design's falsifier (its section 16 item 2) should count this path, and the
documents should warn against Delete Duplicate Entries on a database with narrowing rules.

### P7, minor: section 3's third property and section 7's KeePass row are false as written

- "A stale copy of the database can only lack versions. Merging it in adds nothing that is
  newest." A copy that lost a newer version (P1) makes an older one newest. A repaired copy (P2)
  makes the older rule newest in every copy it is merged into.
- "Never an older rule in force" (section 7, KeePass row). It is false after the repair (C3, C5)
  and whenever KeePass opens a file a KeePassDX merge or pykeepass wrote (C1, C2, C4).

Both are statements the design is chosen on, so they must change with P1 and P2.

## 4. Every program, save and merge, through datum 7

| Program | A save | A merge | What the gate then does | Basis |
| --- | --- | --- | --- | --- |
| KeePass 2.62, Synchronize | keeps entries and strings; KDBX 4.0 | union of versions; deletion records carried both ways | newest, meet, or a refusal; P2 turns a refusal into the older rule on the next OK | [M keepass X7]; [M design, 112 cells]; [M here, C1 to C5] |
| KeePass 2.62, Overwrite if newer, Overwrite existing, Keep existing | | union; records not carried; the open copy's own records applied | as Synchronize into the deleting copy. In the other copy nothing of a deletion arrives, which keeps the narrowing | [M design]; [M here, C1] |
| KeePass 2.62, Create new IDs (File then Import) | | copies of every version under new identifiers | `503 undecided`, as designed; P3 for its return | [M design, 6.4] |
| KeePass 2.62, Delete Duplicate Entries | nothing in the store | after an import, the originals; after a paste, a governed original (P6) | refusal, or the group's grant at the copy | [M design]; [R here] |
| KeePass 2.62, deleting the group for good | a record for every version | | refuses wherever Synchronize carries it; KeePassDX consumes it (P1, C4) | [R here]; [M here, C4] |
| KeePass 2.62, Database Maintenance, "Remove deleted objects information" | every record gone | | an older version is newest again (the design's section 13) | [R keepass 5] |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | every rule entry kept, flags and notes kept | union; never applies or copies a record. Brings back versions deleted by hand (safe), changed entries the repair deleted and removed copies (P3); does not heal a KeePassDX loss | as the union; refuses again after a repair (P3) | [M design]; [M here, C1, C3, C5]; [M keepassxc 2] |
| KeePassXC KeeShare import | | Synchronize mode: applies records and keeps them | as KeePass Synchronize | [R keepassxc 3] |
| Keepass2Android 1.15-r6, 1.08d-r5 | entries kept [M spike]; custom strings [R android 2] | adds entries the open copy lacks (to the root when the group is missing); joins both copies' records | the union; a deletion refuses | [R android 1.5, 3.1]; an entry only the arriving copy holds never measured |
| KeePassDX 4.5.8 | entries kept [M spike]; strings [R android 2]; 4.1 | adds entries, recreates missing groups, applies arriving records and keeps none | an older rule or the group's grant, nothing shown (P1) | [R here, `:304-323`, `DatabaseKDBX.kt:800-807`]; [R+model, C1, C2, C4] |
| KeePassDX 3.2.0 | as 4.5.8 for strings; deletes group custom data, which holds nothing of ours | as 4.5.8 for records; P4's two cases | P1; P4 | [R here at 3.2.0]; [M spike] for the save |
| KeePassDX 3.3.0 | kept entries [M spike] | not run, not read | | |
| KeePassDroid 2.7 | kept entries [M spike]; strings [R android 2] | none; any save over a changed file writes the whole file | not solved, as the design says | [R android 5.5] |
| kdbxweb 2.1.1 (KeeWeb) | keeps strings and flags | union; deletes whatever a record names, whatever the times; drops a remade group (P5) | the union; refusals; P5 closes | [M others]; [R others]; [M store-entries-survival] |
| pykeepass 4.2.0 | keeps strings | no merge; deletes without a record | an older rule (C2) when KeePass opens its file | [M others]; [M here, C2] |
| Strongbox 1.65.0, KeePassKit 3.3.2+ | strings in their models | union; deletion records combined or merged by time | as KeePass | [R others] |
| keepass-rs 0.17.0 | strings in its model | union; applies a source's deletion and records it in the destination | as KeePass Synchronize | [R others]; read here, `src/db/merge.rs:438-462` |
| gokeepasslib, KeePassium | strings in their models | none | unchanged; KeePassium overwrites on conflict | [R others] |
| A whole file written over another | | nothing merges | not solved, as the design says | [R android 5.5]; [R others] |

## 5. Datum by datum

### Datum 7, access rules

| Event | Survives | Fails | What the gate then does |
| --- | --- | --- | --- |
| A rule added, or changed in one copy | every merge run or read [M design; M here] | | the newest version |
| A rule changed in two copies | both versions arrive; the meet applies [M design, 03] | | the meet, narrower than either |
| A rule taken away | [M design, 04] | | silence, as decided |
| The newest version deleted: by hand, by Remove, by Delete Duplicate Entries, with the group | refuses wherever the record is kept | KeePassDX merge, pykeepass, Maintenance (P1) | an older rule or the group's grant, nothing shown |
| A refusing node's dialog OK'd for any reason | | the repair (P2) | the older rule, carried into every copy |
| A version changed by hand | refuses [M design, 09] | the repair writes the older rule (P2); KeePassXC returns the changed entry (P3) | wider, or refusals again |
| Rule entries moved or binned | count where they are [M design, 08] | KeePassDX 3.2.0, a moved and newer group (P4) | |
| The governed node moved | the rule follows [M design, 10] | | |
| The governed node deleted, then back from another copy | refuses where records are kept [M design, 13, 14] | KeePassDX into the copy that kept it (C1) | the group's grant |
| The governed node copied | the design's own weakness | Delete Duplicate Entries keeps the copy (P6) | the group's grant |
| The store group deleted for good | refuses [M here, C4] | KeePassDX (C4); the repair (C4); kdbxweb after a remake (P5) | the group's grant, or closed |

### Datum 8, the recycle bin markers

Section 2.2 makes them one more add-only kind in the same group. They share P1: a marker deleted
by hand, or with the group, and carried by a KeePassDX merge, is gone without trace. They need the
same second witness. Nothing else here touches them.

## 6. Survival claims that rest on reading or assumption

Each has to be measured before the design can rely on it.

1. **Every Android merge of this store.** In particular:
   - KeePassDX 4.5.8 and 3.2.0 consuming deletion records, over the shapes of C1, C2 and C4;
   - 3.2.0's two cases in P4;
   - Keepass2Android joining records, and adding an entry only the arriving copy holds [R android
     1.5, 3.1];
   - KeePassDX 3.3.0 to 4.5.7, never read.

   The design's section 7 and this critique's KeePassDX results are reads.
2. **Every Android save of a rule entry**: the four strings, `none`, a `KPG.Data` of 4,482
   characters, and Keepass2Android's KDBX 3.1 output [R android 2; A for 3.1].
3. **What the dialogs do in KeePass's window.** That an OK pressed for the entry writes the repair
   follows from section 5.2's text; the Live tier was not run here or in the design.
4. **kdbxweb over a remade group holding rule entries.** P5's outcome for rules is derived from the
   measured drop.
5. **KeePassXC's window merge, its merge on reloading a changed file, and KeeShare** over C1 to C5
   [R keepassxc 3]. They use the same merger.
6. **Strongbox and KeePassKit merges** [R others]; **keepass-rs's merge** [R here].
7. **Section 8's "Restores that entry's earlier revision"**: not run in the design or here.
8. **P6's paste and Delete Duplicate Entries**: read; the times a paste keeps are [A].
9. **Search, autofill and auto-type leaving the group alone on Android and in KeePassXC** [R].
10. **KeeShare not carrying rule entries** (the design's 6.4) [A in the design].
11. **Which end-user programs delete through pykeepass without a record.** The library is [M here],
    the programs are [A].
12. **If a second witness is adopted (P1)**: a KeePassXC merge and a KeePassDX merge over it, and
    kdbxweb on the KDBX 4.1 file it makes unless the key is written without a time.

## 7. What the design should change in its own text

- Section 1: "nothing another program does to a node can touch its rule" holds for the node only.
  Another program's acts on the store, or on the deletion records, do touch it (P1).
- Section 3, the third property (P7).
- Section 5.2, the repair (P2). Section 5.3 and 5.4, Remove (P1).
- Section 6.3, scenarios 13 and 14, "into B, nothing arrives and B keeps X with its rule": not under
  KeePassDX, where B loses the rule (C1).
- Section 7: the KeePass row (P7); the KeePassDX 4.5.8 row should say the loss serves an older rule
  with nothing shown; the 3.2.0 row (P4).
- Section 8: "Deletes it for good: the node refuses" holds only in copies that keep the record.
  "Every refusal is shown ... logged" says nothing about a loss without trace, which is none of
  these.
- Section 13: "which no program measured does on its own" (P1).

## 8. What was run for this critique

- **The probe.** `Rcs.cs`, compiled with the .NET Framework's own `csc` against a copy of
  `.keepass\2.62\KeePass.exe`, whose Authenticode signature (Dominik Reichl)
  checked valid. It makes the databases with invented content (Argon2d, 2 iterations, 1 MiB,
  password `probe-only`), runs chains C1 to C5, and after each step applies the design's read rule
  and resolves what `agent` reaches at X. Five chains, 40 results, 4.5 seconds.
- **KeePass's own code where it acts.** A permanent entry deletion as `MainForm_Functions.cs:5154-5158`
  does it; a permanent group deletion through `PwGroup.DeleteAllObjects` itself; merges through
  `PwDatabase.MergeIn` under four methods; every result saved and reopened.
- **The KeePassDX model**, from tag 4.5.8 as read here:
  - the merge (`DatabaseKDBXMerger.kt:190-312`), entries (`:463-527`) and groups (`:557-593`);
  - deletions (`:304-311`, `deleteEntry` `:317-323`, `deleteGroup` `:358-395`), and missing parents
    (`getAttachedParent` `:439-458`).

  Attachments, custom data and history are left out; nothing in the chains uses them.
- **`keepassxc-cli` 2.4.2 and 2.8.0**, extracted into new directories of my own from the archives
  the KeePassXC ground report downloaded. Both archives match the spike's `keepassxc.sha256`,
  checked again here, and both executables carry a valid Authenticode signature from DroidMonkey
  Apps, LLC. They ran `merge -s`, password on standard input.
- **pykeepass 4.2.0**, through `uv run --no-project --with pykeepass python -I -X utf8`, deleted one
  entry by identifier and saved.
- **Sources read here**: KeePassDX 4.5.8 and 3.2.0, KeePass 2.62 (`KeePass-2.62-Source.zip`, the
  copy the KeePass ground report verified), keepass-rs at `v0.17.0`, all under `...`.
- **What was not run.** No window was opened. No Android program, no emulator, no KeePass process,
  no test tier and no `build.ps1` was run. Nothing was written to either worktree or to
  the main checkout.
