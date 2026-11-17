# Measured: what a copy of a store record may take away

The run of the two readings (`measured-two-readings.md`) measured the narrowest reading of a copy:
a revocation, a retirement's refusal, a profile removal, and a rule or renewal answer that refuses
everything count from an entry under another identifier, and nothing else does. It closed 7 or 9 of
the ten merge cells that end wider in the store as decided, and named what it left undecided. This
report is the next step: for every kind of store record and every field of it, what a copy may do
whatever else the file holds; the readings that follow from that; and each of those readings
measured on real files on 2026-11-17, in the family `spikes\storage\probes\store-as-a-whole\` of the
branch `spikes/storage-survival`, as `run.ps1 -Reading <name>`, each recorded in a directory of its
own, `recorded-reading-<name>\`. The family's `RUN.md`, section "The wider readings of a copy", has
the commands, both runs' times and a second run's value of every figure. Nothing ran in a window, on
a phone or in an emulator; every file was made and changed by KeePassLib 2.62 and merged by
KeePassLib or `keepassxc-cli`.

**How to read it.** The words are those of `measured-two-readings.md`. **Wider** is a client
reaching more than the last decision allowed, and a sequence that ends wider is named **fail-open**;
**refuses** is the plugin refusing what the last decision allowed, with its scope (the whole
database, or nothing served); **narrower** is a file granting less than the last decision without
refusing as a whole; the oracle is every decision any gate made. A **summary entry** (the papers'
head) restates, with every write of the plugin, everything in force that takes access away. A
**copy** is an entry holding a record's strings under an identifier the record's content does not
give: what KeePass's Duplicate Entry, Duplicate Group, copy and paste, and its import with "Create
new IDs" make. **The file's own records** are the entries that pass the check under the identifier
their content gives, in their current version or in their history: the only records the store as
decided reads. **Row 6** is the sequence of `EVALUATION.md` section 6 that ends wider on real files
most often: a stale copy imports the newer file with Create new IDs, so the newer decisions arrive
only as copies. **The ten wider cells** are the ten of the 234 merge cells that end wider in the
store as decided, each such an import. A **limit** is a rule document applied so that it can remove
words from what a node gives and never add any: the document is combined with the file's own rule of
the node as the design combines two versions made apart, word by word, each word given only where
both give it. **Held** and **exempt** are the two variants of `measured-two-readings.md`: a record
read from a copy is held to the completeness check, which stops the store where no summary entry in
force restates a record, or is exempt from it. A rule is written `pf=rw,pg=r`: each profile it
names with its words, read and write, and a profile left out inherits.

## 1. What a copy of each kind of record may do

### 1.1 The test

A statement about a copy is used by a reading only where it is true whatever else the file holds.
The test that follows is on the file alone: **a file read with its copies never gives more than the
same file read from its own records**. No client is admitted that the file's own records do not
admit, no client holds a profile more, no node gives a profile a word more, no renewal is asked that
the own records refuse, and no authority is served where the own records serve none. A reading that
passes can leave a wider cell wider; it cannot make a cell wider. A reading under which a single
file gives more with a copy than without is out. The measurement holds every answer of every part to
this test (section 3.4), beside the oracle.

### 1.2 Every kind and every field

| Record | Field or effect | What a copy of it can do | In the readings |
| --- | --- | --- | --- |
| A revocation | the client's key it refuses | only take away: a revocation is never withdrawn and never replaced | counts |
| A retirement | the authority it refuses, and with it every admission that authority issued | only take away: a retirement is never withdrawn | counts |
| | which authority is served, where several stand | give: with all but a single standing authority retired, that authority is served (section 6.1) | never; the file's own records decide which authority is served |
| | the store key it holds after a Delete with no new authority | give: everything written under that key counts again (section 6.3) | never |
| A profile removal | the name it takes off the list | only take away: an addition counts where it names every removal of its name, and a copy adds a removal | counts |
| A rule version | per profile it names, each word it does not grant | only take away, applied as a limit | counts in the readings `verb` and `cap`; in `profile` under a condition; in `nothing` only where the version grants nothing to anybody |
| | per profile it names, each word it grants | give | never |
| | a profile it does not name, which inherits | either: the node then gives that profile what arrives from above, less than a rule of the node that grants more, more than a rule that grants less. As a limit, only take away | counts as a limit in the reading `cap`; in no other |
| | the versions it names as replaced | either: replacing a narrower version gives, replacing a wider version takes away | a copy never replaces a version of the file's own; it can mark another copy as replaced, which only lifts that copy's limit |
| A renewal answer | refused forever | only take away | counts |
| | allowed again | give | never: a copy does not replace the file's own refusal |
| An admission record | the client, its certificate, its profiles | give | never |
| A profile addition | the name | give | never |
| An authority | that it stands and is served | give | never |
| | the store key | give (section 6.3) | never |
| A summary entry | its presence, which completes the store, and the earlier summary entries it names | give: a store stopped for a missing summary entry reads again | never |
| | its items that restate a revocation, a retirement or a removal | only take away | counts in the reading `cap-items`; in no other |
| | its items that restate a rule version or a renewal answer | as a copy of that version | the same |
| | its items that acknowledge copies (section 5) | give: an acknowledged copy is exempt from the check | never |

A copy is read as the record under the identifier its content gives. Where the file holds the record
itself under that identifier, the copy is the same record once and changes nothing, under every
reading. A copy passes only under a store key the file holds through its own records.

### 1.3 A rule version

A version of a node's rule is a document. For each profile and each word it makes a statement of
three kinds: the word is granted to the profile at this node, whatever arrives from above; the word
is refused, the profile being named and the word absent; or the profile is not named, and gets at
this node what arrives from above. The version also names the versions it replaces.

**What is true whatever else the file holds.** A grant is not: it gives. A refusal is, as a limit:
whatever the file holds, taking the word from the profile at that node takes away. That the profile
inherits is, as a limit as well: holding the node to what arrives from above takes away, whatever
arrives. That the version replaces another is not: a version that narrows relative to the version it
replaces still widens where the file holds a narrower version, or holds none below a narrower rule.
A removed rule is a version that names no profile: as a statement about the node it lets through
what the node above gives, which is wider or narrower than the rule removed; as a limit it can only
take away.

**The candidates.** Each applies a copy as a limit and differs in how much of the document it
applies. None lets a copy replace a version of the file's own, and none lets a copy grant.

| Reading | What a copied version does |
| --- | --- |
| `nothing` (measured before) | limits only where it grants nothing to anybody: the node then gives nothing |
| `profile` (candidate a) | per profile the copy names: where the file's own rule of the node names the profile and the copy grants within what that rule grants, the profile gets what the copy grants; where the file's own rule does not name the profile, or the file holds no rule of the node, only a copy that grants the profile nothing limits. Otherwise the copy does nothing for that profile |
| `verb` (candidate b) | per profile the copy names: every word the copy does not grant is taken from the profile at the node, whatever the file's own rule holds. A profile the copy does not name is left alone |
| `cap` (candidate c, which holds d) | the copy is combined with the file's own rule as two versions made apart are: `verb`, and a profile the copy does not name gets at the node at most what arrives from above |

Candidate d, a removal counted only where the removed rule granted more than the nearest rule above
it, is `cap` applied to a document that names no profile: where the removed rule granted more than
arrives from above, the node is held to what arrives, and where it granted less, nothing changes.
`cap` decides that word by word, with no comparison of whole rules, and again at every read, so a
node moved under another group is held to what arrives there. Candidate d was not run on its own.

**The copy older than the file's own version.** Every version names every version its writer had
read of the node, the replaced versions included. A copy of an older version is therefore named by
the file's own newer version and limits nothing, under every reading: a stale narrowing does not
come back where the store's own version was written by a gate that had read it. Where the file's
own version was made apart, at a gate that had not read the copy's original, the copy limits, and
the node gives what both allow, which is what the design gives two versions made apart in any case.
Such a limit lasts until a version that names the copy's original arrives (a Synchronize, or a
further import whose copies name it), until the rule of the node is set again at that file by a
plugin that names the copies it read (section 1.6), or until the copy is deleted. It does not end
with another write of the plugin, and not with Seal again (section 4.2).

**Two copies that disagree.** Each limits, and the node gives what both allow and the file's own
rule allows. Where a passing copy names another copy as replaced, the replaced copy does not limit:
that lifts a limit a copy set and never gives more than the file's own records give.

**Can it end wider than the decisions allow?** Not where the file's own records do not: each
candidate passes the test of section 1.1 by construction, and in every answer measured (section
3.4). Each leaves wider what it does not apply (section 3.2): `nothing` every narrowing that leaves
a word; `profile` a narrowing of a profile the file's own rule does not name, and a change to other
words; `profile` and `verb` a removed rule and a profile dropped from a rule.

### 1.4 The retirement that settles, the key carrier, the summary entry

- **A retirement that settles which authority is served** gives. Where several authorities stand,
  nothing is served; a retirement of all but a single authority serves that authority. Read from a
  copy, it served an authority that the file's own records did not serve and that a later decision
  had retired, and admitted its client (section 6.1). The readings here count a copied retirement
  as the refusal of the authority it names and of that authority's clients, and decide which
  authority is served from the file's own records alone. In the stale copy of the ten wider cells
  that leaves nothing served where the newer authority arrived only as a copy (H6, H13), as under
  the reading measured before.
- **The key carrier as a key source** gives: a store key taken from a copy opens every record
  written under it, grants included (section 6.3). No reading here takes a key from a copy. What
  that keeps: a file whose only key carrier is lost reads as holding no store, with a copy of the
  carrier in it or not.
- **A summary entry's refusal items** take away, and its version items are copies of versions. The
  reading `cap-items` counts both. It closes a sequence no other reading closes: a refusal's entry
  deleted by hand with no deletion record in the newer copy, so that only the summary entry
  restates it, then the import with Create new IDs (section 6.2). The summary entry's presence and
  the earlier summary entries it names never count: they would complete a store that is stopped.

**The rule that prevents the loss of the key in H13.** The combined run of
`measured-two-readings.md` wrote a copied retirement under its own identifier, and the next write
of the plugin then deleted the retired authority's entry, the stale copy's only key carrier. The
rule: **the plugin never writes a record again from a copy, and nothing the plugin deletes,
replaces or restates at a write of its own is decided by a copy.** A write of the plugin's own is
computed from the file's own records. Measured: with the rule the key is held in every history, and
without it H13 loses the key under every reading (section 5).

### 1.5 Held to the completeness check, or exempt, kind by kind

| Kind read from a copy | Held | Exempt |
| --- | --- | --- |
| A revocation | The gate stops for the whole database until the operator seals the store again, since no summary entry of the file restates the revocation | The client is refused, and everything else is served as before |
| A retirement | The gate stops for the whole database until Seal again; after it nothing is served where that authority was the only authority standing | The authority and its clients are refused; where it was the only authority standing, nothing is served, and nothing else changes |
| A profile removal | The gate stops for the whole database until Seal again | The name is off the list for every client, and everything else is served |
| A rule version or renewal answer that limits | The gate stops for the whole database until Seal again, also where the limit removes a single word at a single node | The node gives less, or the renewal is refused, and everything else is served |
| A summary entry's items (`cap-items`) | As the record each item restates | As the record each item restates |

Held is what the store as decided does with a record of its own that no summary entry restates.
After Seal again, which acknowledges the copies (section 5), a held reading gives what the exempt
reading gives. The two differ in what happens before the operator acts: a stop of the whole
database, or the file served with the copies' limits and nothing shown unless the tab shows it.

### 1.6 What a reading needs the plugin to do, and never to do

1. Never write a record again from a copy, in any form: not under the identifier its content gives,
   not as an item of a summary entry.
2. Compute every write of its own from the file's own records: what it deletes as replaced, retired
   or revoked, and what its summary entry restates. A copy never causes a deletion.
3. Decide which authority is served, and which store keys the file holds, from the file's own
   records.
4. For `profile`, `verb` and `cap`: when it writes a version of a node's rule or of a renewal
   answer, name as replaced the copies of that object's versions it read, by the identifier each
   copy's content gives. That is what lets the operator's next decision at that file end a limit.
5. At Seal again after a stop that a copy caused: write a summary entry over the file's own records
   that names the copies it acknowledges, and carry those names into later summary entries.
6. List on its tab the copies that take something away, with what each takes, and state there that
   deleting such a copy gives back what it took.

## 2. What was run

| Configuration | Part | Cells |
| --- | --- | --- |
| Each of the readings `nothing`, `profile`, `verb`, `cap`, `cap-items`, held and exempt | `b-merges`: the 13 histories of the store as decided and nine further histories, each ending in KeePassLib's five merge methods and four `keepassxc-cli` releases, both directions; and five histories of a user node | 396 and 90 |
| | `b-deletions`, `b-clear`, `b-repair`, `b-dedupe`, `b-dupes`: the parts of the store as decided | 144, 210 and 96 merge cells, 2 orders, 13 cases |
| | `b-random`: random histories over two and three copies, with rules per profile and the keeping of an authority among the acts | 300 histories, 721 files read at their end |
| | `copies`: copies that are not an import, ten kinds; a replaced narrowing and what clears a limit; the reading combined with the decided rewrite, the plugin writing nothing from a copy, with Seal again; and the control that writes from a copy | 131 rows; 22 rows; 16 histories, each with 6 or 7 reads; 16 histories |
| | `kinds`: a retirement that settles, a summary entry's items and a store key, each read from a copy or not over the same file | 2, 7 and 2 files, each read both ways |

The nine further histories are new to the merge part: H14 a version that narrows a profile and
widens the other; H15 a rule that grants nothing, then widened for a profile; H16 a narrowing on a
node below a wider rule, where the stale copy holds no rule of the node; H17 a rule removed where
the node above gives less; H18 a rule removed where the node above gives more; H19 a rule changed to
other words, neither within the other; H20 a rule narrowed for a profile at a gate and widened for
the other profile at the other gate, apart; H21 a rule that names a profile less, which inherits
again below a narrower rule; H22 two authorities standing after both gates recreate apart, and gate
0 keeping its own. For these the probe was extended to rules per profile, two profiles and two
words, in the model, in the entries and in the read; the documents of the recorded configurations
read as naming every profile alike, and with the new switches off every recorded configuration was
run again and printed its record line for line (`RUN.md`).

Every part ran twice; the second run printed every output of the first line for line, but the
running times. A reading took about three minutes with eight parts at once.

**Cut.** Candidate d on its own; the readings in the model's searches; the slow key derivation; the
retirement that settles, the key source and, but for `cap-items`, the summary entry's items as
configurations of their own (each is switched on and off over single files in `kinds`); a copy of
an authority counted as standing without being served; Create at a file where a copied retirement
leaves nothing served; KeePass's own window for Seal again, copy and paste, and every edit.

## 3. The readings on the merge cells and the other parts

### 3.1 The ten wider cells, exempt from the completeness check

Each row is the cell "Create new IDs into the stale copy 1" of its history.

| History | Store as decided | `nothing` | `profile` | `verb` | `cap`, `cap-items` |
| --- | --- | --- | --- | --- | --- |
| H2 a revocation | wider: c0 admitted | as decided | as decided | as decided | as decided |
| H3 a revocation; the key renewed at the other gate | wider: c0 admitted | as decided | as decided | as decided | as decided |
| H5 a name removed | wider: c0 holds pf | as decided | as decided | as decided | as decided |
| H6 the authority recreated, deleted, created again, a client paired | wider: c0 admitted | refuses, nothing served | refuses, nothing served | refuses, nothing served | refuses, nothing served |
| H7 a rule widened, narrowed and emptied at N, the root narrowed to read | wider: root read and write | **wider: root read and write** | as decided | as decided | as decided |
| H8 a rule at N removed, the root emptied | wider: root read | **wider: N read** | **wider: N read** | **wider: N read** | as decided |
| H9 the same rule changed apart, read and read and write | wider: N read and write | **wider: N read and write** | as decided | as decided | as decided |
| H10 a rule narrowed at a gate, widened at the other | wider: N read and write | as decided | as decided | as decided | as decided |
| H12 renewals refused forever | wider: renewal asked | as decided | as decided | as decided | as decided |
| H13 a copy from before a Recreate | wider: c0 admitted | refuses, nothing served | refuses, nothing served | refuses, nothing served | refuses, nothing served |
| Closed of the ten | | 7: 5 as decided, 2 nothing served | 9: 7 as decided, 2 nothing served | 9: 7 as decided, 2 nothing served | **10: 8 as decided, 2 nothing served** |

H6 and H13 end with nothing served under every reading: the retirement counts, and the newer
authority arrived only as a copy. H4, as decided in the store as decided, stays as decided.

**Held to the check**, every reading turns each of the ten cells into a refusal of the whole
database, but H9 under `nothing`, which stays wider; and H4 refuses as well, in every reading.

### 3.2 The nine further histories

The same cell of each, exempt from the check.

| History | Store as decided | `nothing` | `profile` | `verb` | `cap`, `cap-items` |
| --- | --- | --- | --- | --- | --- |
| H14 narrows pf to read, widens pg to read and write | wider: N gives pf write | wider | narrower: pg lacks write | narrower: pg lacks write | narrower: pg lacks write |
| H15 a rule of nothing widened for pf | narrower: pf lacks read and write | the same | the same | the same | the same |
| H16 pf narrowed to read below a wider root, no rule of N in the stale copy | wider: N read and write | wider | **wider** | as decided | as decided |
| H17 a rule removed where the root gives less | wider: N read and write | wider | **wider** | **wider** | as decided |
| H18 a rule removed where the root gives more | narrower: pf lacks write, pg lacks read and write | the same | the same | the same | the same |
| H19 pf changed from write to read | wider: N gives pf write | wider | **wider** | narrower: pf lacks read | narrower: pf lacks read |
| H20 pf narrowed at gate 0, pg widened at gate 1, apart | wider: N read and write | wider | as decided | as decided | as decided |
| H21 pf dropped from the rule below a narrower root, pg emptied | wider: N read and write | wider | **wider: N gives pf read and write** | **wider: N gives pf read and write** | as decided |
| H22 two authorities stand; gate 0 keeps its own | refuses, nothing served, 2 stand | refuses, nothing served, 1 stands | the same | the same | the same |
| Wider of the six | 6 | 6 | 4 | 2 | **0** |

Where a version narrows a profile and widens the other (H14, H20), the narrowing counts and the
widening does not: as decided where the stale copy already held the wider words (H20), narrower
where it did not (H14). Where the narrowing sits on a node below a wider rule and the file holds no
rule of the node (H16), `profile` does not apply it. Where the rule was removed (H17, and H8 of the
ten), only `cap` holds the node to what arrives from above; where the removal widens (H18), no
reading widens with it. Where the copy is the older version (every cell "into copy 0", the newer
copy importing the stale copy, 22 cells for each reading and variant), every reading left every
cell as in the store as decided: the file's own version names the copy.

Held to the check, each cell in which a copy takes something away is a refusal of the whole
database: 1, 4, 6, 8 and 8 of the nine under `nothing`, `profile`, `verb`, `cap` and `cap-items`.
H18 is among them under `cap`: the removal changes the rule of N to what arrives from above, which
there removes no word.

### 3.3 The tallies

The 396 merge cells of the 22 histories:

| Reading | Exempt: as decided, narrower, nothing served, wider | Held: as decided, narrower, refusing, wider |
| --- | --- | --- |
| the store as decided | 374, 5, 1, **16** | |
| `nothing` | 379, 5, 3, **9** | 373, 5, 11, **7** |
| `profile` | 382, 6, 3, **5** | 373, 5, 15, **3** |
| `verb` | 383, 7, 3, **3** | 373, 5, 17, **1** |
| `cap` | 386, 7, 3, **0** | 373, 4, 19, **0** |
| `cap-items` | 386, 7, 3, **0** | 373, 4, 19, **0** |

In every reading and variant, no cell but "Create new IDs into copy 1" differs from the store as
decided: every Synchronize, every other import method and every KeePassXC cell reads the same, and
so do the 90 cells of a user node.

- **`b-deletions` (144 merge cells), `b-clear` (210), `b-repair` (96), `b-dedupe`, `b-dupes`**:
  every answer of the same class as in the store as decided on the same file, in every reading and
  variant.
- **`b-random`, 300 histories, 721 files read at their end**: exempt, 720 answers of the same class
  as the store as decided and a single file that was wider and reads as decided; held, 719, that
  file, a second file that was wider and refuses, and a single gate act refused because the reading
  stopped the store. The same in every reading. The histories are not those of the store as
  decided's record, since they draw rules per profile; files at the end, exempt under `cap`: 223
  wider, 357 as decided, 84 narrower, 57 refusing. The wider files there are files that have not
  received a decision at all, which no reading of a copy touches. The model's safety check failed
  in none.

### 3.4 The test of section 1.1

Every answer was also held to the same file read from its own records. **No answer of any part
under any reading gave more than the file's own records give**: 21,399 answers over the five
readings and two variants, the answers of `kinds` aside. The answers that fail the test are those
of sections 6.1 and 6.3, where a switch that no reading here holds was turned on.

## 4. What the readings cost

### 4.1 Copies that are not an import

For ten kinds of store entry (the nine of `measured-two-readings.md` and a rule version with a rule
per profile): Duplicate Entry; the duplicate moved to another group, which stands for copy and
paste; the duplicate in the recycle bin; Duplicate Entry and Delete Duplicate Entries; the original
deleted for good with and with no record; the original deleted with a record, the copy carried into
the other copy and deleted, then brought back by KeePassXC 2.8.0 beside its own deletion record, and
the same with Synchronize; the original replaced by a widening version and then six merges; and the
gate's own write beside the copy. **131 rows for each reading and variant, every row the same as in
the store as decided.** The gate's own write left the copy where it was and wrote nothing for it.
A copy of a narrowing whose original the gate later replaced did not come back: the widening
version names the original.

### 4.2 A limit that outlasts its replacement

The cost a limit has is a narrowing that stays after the decision behind it was replaced where the
file cannot read the replacement. It was built on purpose, since no merge cell and no random
history showed it: N narrowed at gate 0 and widened at gate 1, apart; copy 0 imported into copy 1
with Create new IDs, where the copied narrowing limits (as decided: both were made apart); then N
widened at gate 0, which copy 1 has not received. Exempt, copy 1 is then narrower than decided (N
lacks write) under `profile`, `verb`, `cap` and `cap-items`; held, copy 1 has refused as a whole
since the import. What ended it, and what did not:

| At copy 1 | Exempt | Held |
| --- | --- | --- |
| a write of the gate's own (a profile name added) | still narrower | refused: the store is stopped |
| the rule of N set again, the gate naming the copies it read | as decided | refused until Seal again; after Seal again, as decided |
| the rule of N set again, the gate naming no copy | still narrower | the same after Seal again |
| Seal again | still narrower | narrower, the store served again |
| copy 0 imported again | as decided: the newer copy names the older | as decided |
| Synchronize from copy 0 | as decided | as decided |
| the copy deleted by hand | as decided | as decided |

Two imports in a row (the narrowing, then its widening) read as decided after the second: the
second copy names the first. With that rule off, under `verb`, `cap` and `cap-items`, the first
copy went on limiting and the file was narrower.

### 4.3 Refusals of the whole database, held to the check

Held to the check, every cell in which a copy takes away what the file's summary entries do not
restate is a refusal of the whole database until Seal again: 10 of the ten wider cells and H4 in
every reading but `nothing` (9 and H4), and up to 8 of the nine further cells. No such stop came
from a copy beside its original, from Delete Duplicate Entries or from a copy brought back by
KeePassXC (section 4.1).

## 5. Seal again after a stop that a copy caused, and the decided rewrite

**What Seal again is here.** The plugin writes a summary entry over the file's own records, as any
write of its own does, and names in it the identifier each counted copy's content gives. A copy
named in a summary entry in force is exempt from the check. Nothing is written from the copy.

Run on the ten wider cells and six of the further cells (H14, H16, H17, H19, H20, H21), after the
import into copy 1, under `cap`:

| Step | Exempt | Held |
| --- | --- | --- |
| after the import | section 3.1 and 3.2 | the whole database refuses |
| Seal again | not needed | acknowledges 1 or 2 copies; the file then reads as under exempt |
| the authority's entry edited in the entry dialog | no change; the entry counts by its history | the same |
| the gate's write, with the decided rewrite | writes; the authority's entry written again; nothing written for a copy; the store key held and the authority's entry present in all 16 | the same |
| Synchronize with the newer copy, both ways | as decided, all 16 | as decided, all 16 |
| every copy deleted by hand instead | **wider, as in the store as decided, all 16** | **the same** |

**Seal again does not end wider**: it gives what the exempt reading gives. **Deleting the copies
does**: it gives back what the copies took, which is the store as decided's answer for that file.
That is the operator's act on entries the tab can name (section 1.6, item 6); nothing the plugin
writes does it.

**The H13 loss does not occur with the rule of section 1.4.** In H13 and H6 the copied retirement
leaves nothing served, the stale authority's entry stays, the key is held, the rewrite writes that
entry again after an edit, and a Synchronize with the newer copy reads as decided both ways. Under
every reading and variant alike: the key held and the authority's entry present after the write in
all 16 histories, and as decided after each of the 32 Synchronizes.

**The control**, the combination of `measured-two-readings.md` that writes each record counted from
a copy under its own identifier and seals: under `cap`, 14 histories as decided, H6 nothing served,
and **H13 with no store key held**, under held and exempt alike. Under `nothing` it is the outcome
measured before (H7 and H8 wider again, H13 with no key). Under `cap-items` the control writes the
copied refusals, and H13 holds no key after it as well; a copied version is read there through the
copied summary entry and is not written, so the rule histories stay as after the import. Written
under its own identifier, a copied version is a version: in H14 under `cap` the widening counted as
well. Synchronize with the newer copy gave as decided in every history, both ways.

## 6. The three kinds, read from a copy and not

Each over the same file, the switch off and on, under `cap`; the other readings print the same.

### 6.1 A retirement that settles which authority is served

Both gates recreate the authority apart and synchronize, so two authorities stand in both copies
and nothing is served. Gate 0 keeps its own authority, under which a client c1 was paired; a third
copy is taken; gate 0 recreates the authority again, which retires the authority it kept. Copy 1,
which holds both standing, then imports the third copy with Create new IDs.

| The retirement | Copy 1 |
| --- | --- |
| refuses only | nothing served, a single authority standing |
| also settles | **wider: client c1 admitted**, and the authority served where the file's own records serve nothing |

The table is the variant exempt from the check; held to it, copy 1 refuses as a whole either way,
since no summary entry of copy 1 restates the copied retirement. With the newest copy imported
instead, both retirements arrive and nothing is served either way. The reading measured before let
a retirement settle and held no such cell.

### 6.2 A summary entry's items

At copy 0 the entry of a record that takes access away is deleted by hand with no deletion record;
copy 0 still reads as decided, since its summary entry restates the record. Copy 1, which never
held the record, imports copy 0 with Create new IDs.

| Record deleted at copy 0 | Items not counted | Items counted, exempt | Items counted, held |
| --- | --- | --- | --- |
| a revocation | **wider: c0 admitted** | as decided | the whole database refuses |
| a rule of nothing at N | **wider: N read** | as decided | the whole database refuses |
| a rule at N for a profile, below a wider root | **wider: N read and write** | as decided | the whole database refuses |
| a profile removal | **wider: c0 holds pf** | as decided | the whole database refuses |

This sequence is fail-open under `nothing`, `profile`, `verb` and `cap`, and closed under
`cap-items`. An older summary entry copied by Duplicate Entry before a later decision that widens (a
rule of nothing then read and write; renewals refused then allowed; a name removed then added
again) read as decided with the items counted, in all three: the later record names what the older
summary entry restates. Over the whole of `cap-items`, every part printed what `cap` prints but the
count of copies read and the control of `copies` (section 5).

### 6.3 A store key taken from a copy

The authority's entry is duplicated and the original deleted for good with a record. With no key
taken from the copy the file holds no store, as in the store as decided; with it, the store reads
again with nothing served. After Create, which starts a new key, and a name added:

| The key from the copy | Keys held | Names | The root gives | N gives |
| --- | --- | --- | --- | --- |
| not taken | 1 | pf | nothing | nothing |
| taken | 2 | pf, pg | **read** | **read and write** |

Every rule and name written under the earlier key counts again beside the store that was started
again: more than the file's own records give.

## 7. Every fail-open sequence in this run

1. **Row 6 under a reading that does not apply the narrowing**: H7, H8, H9 under `nothing`; H8
   under `profile` and `verb`; of the further histories H14, H16, H17, H19, H20 and H21 under
   `nothing`, H16, H17, H19 and H21 under `profile`, H17 and H21 under `verb`. None under `cap` or
   `cap-items`. Held to the check, those cells refuse where any copy counts, and H9 under `nothing`,
   H16, H17 and H19 under `profile` and H17 under `verb` stay wider.
2. **A refusal whose entry was deleted by hand with no record in the newer copy, then row 6**
   (section 6.2): under every reading but `cap-items`.
3. **The copies deleted by hand after they took something away** (section 5): the file reads as
   the store as decided reads it, under every reading.
4. **A retirement read from a copy that settles which authority is served** (section 6.1), and **a
   store key taken from a copy** (section 6.3): in no reading here; each on record as the reason.
5. **The control that writes a record from a copy**: H13 holds no store key afterward, which is a
   store started again, not access widening by itself.
6. Every fail-open sequence of `measured-decided-store.md` section 5 and of
   `measured-two-readings.md` section 4 that this run does not close stands. A file that has not
   received a decision in any form is wider against that decision under every reading.

## 8. Not measured

A window, a phone, an emulator or a second KeePass with the plugin; the plugin's own Seal again and
tab; KeePass's own copy and paste; candidate d on its own; the switches of a rule (attachments,
protected values), which would be further words of a profile, and a document's `default`; more than
two profiles; a rule on a group between the root and the node; a copy under a store key the file
does not hold, which every reading passes over; a copy of an authority counted as standing without
being served; Create at a file where a copied retirement leaves nothing served, and two authorities
standing after it; the summary entry's items under `profile` and `verb`; the readings in the model's
searches; the slow key derivation; a merge of copies under KeePassDX's reading; the growth of the
list of acknowledged copies in a summary entry; and everything `measured-decided-store.md` and
`measured-two-readings.md` list as not measured.
