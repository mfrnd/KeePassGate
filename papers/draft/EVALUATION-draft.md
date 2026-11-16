# Where KeePassGate keeps its data: the evaluation

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62, for the project's
decision. It rests on the reports in this directory: six ground reports (`ground-*.md`), ten
designs (`option-*.md`) and their critiques (`critique-*.md`).

## How to read this

Every statement about what another program does carries its basis:

- **[M]** measured. The source is named: `spike` is the recorded output of
  `spikes/revocation-merge`; `ground` is a ground report's own console run; `option` and `critic`
  are the console probes of a design or of a critique (KeePassLib 2.62, `keepassxc-cli` 2.4.2,
  2.6.6, 2.7.12, 2.8.0, kdbxweb 2.1.1, pykeepass 4.2.0, invented databases).
- **[M, interim]** measured on the Android emulator by another agent of this workflow, whose
  report (`measured-android.md`) did not exist when this was written. I read its raw result files
  between 18:58 and 19:03 under `spikes\storage\android\work\runs\` (dumps and
  diffs of Keepass2Android 1.15-r6 and KeePassDroid 2.7 results). Treat these as results still to
  be confirmed by that report.
- **[R]** read from source at a named tag, in the ground report or critique cited.
- **[A]** assumed: neither run nor read.

I ran no program for this evaluation. My own contribution is the reading, the cross-checking of
designs against critiques and ground reports, and the judgment.

**State of the inputs.** Ten options exist, two more than the task's digest lists
(`rules-linked` and `single-entry`, both added later). Every option has two
critiques. The two for `single-entry` were finished at 19:03 and 19:04, after the rest of this
evaluation was drafted; section 3.4 was rewritten from them, and of those two I read the
verdicts, the problem tables and the first two problems of each in full, not every line. The
store-entries option has both critiques although the digest lists one.

## 1. The requirements, essential first

From `ground-inventory.md` section 5, with the survival requirement for this rethink put first.

| # | Requirement | Today's store |
| --- | --- | --- |
| C0 | **The survival requirement.** The store rests only on features that no other program breaks when it opens, saves, edits or merges the file. The other programs need not use the data; they must leave it intact and merge it sanely | fails |
| E1 | Fail closed: a datum that is missing, damaged or unreadable admits nobody and grants nothing | met |
| E2 | A certificate admits only through a record in the database; a lost record leaves it inert | met |
| E3 | A revocation survives every save and every merge, by KeePass and by the other programs, in both directions, whatever the clocks | fails under a KeePassXC merge from 2.4.2 [M spike] |
| E4 | A retirement survives the same way; a retired authority never serves again | fails the same way [M spike] |
| E5 | A narrowing is never undone by another program's save or merge: a rule narrowed or sealed is not lost or replaced by an older, wider one; a profile taken off the list does not come back | fails: rules deleted by KeePassDroid and KeePassDX 3.2/3.3 saves, stale rules under newer times after KeePassDX, KeePassXC and kdbxweb merges, the list taken whole [M spike, M ground-others] |
| E6 | The loss of a rule is detectable, or refuses. An absent rule must not read like a rule another program deleted | fails: absence is silence and inherits |
| E7 | No credential before its record is in the file, saved and verified | met |
| E8 | The API reaches none of it and writes none of it | met by construction: no route touches custom data |
| E9 | The trust domain is the database: everything inside the encrypted file, no host registry, no sidecar, and a file the operator merges vouches only for itself | met |
| E10 | Secrets are protected by the database's encryption only and are not put anywhere more exposed | met |
| E11 | A KeePass dialog's stale write-back never silently undoes a revocation, a retirement or a narrowing | met by refusing writes |
| E12 | No silent choice between authorities | met |
| E13 | The plugin's own writes move no node time a KeePass edit would not | met |
| E14 | Live reads: a revocation or a rule change bites from the next request | met |

Availability and correctness (A), and rulings (O), that the options touch:

| # | Requirement or ruling |
| --- | --- |
| A1 | Pairings made on two copies both survive a merge |
| A3 | A profile added in either copy survives a merge |
| A5 | Bounded growth |
| A6 | Cheap reads on KeePass's thread |
| A8 | Damage is shown and named on the KPG tab; the operator's remedy is there |
| A9 | The store survives ordinary use of KeePass and of the other programs |
| O1 | Opening or reading a database changes nothing in it; prunes ride the plugin's own writes |
| O2, O3 | The settings tab waits for OK; on the group and entry dialogs OK keeps a rule and Cancel discards it |
| O7 | An older stored shape is not read or migrated (0.1.0 is released, so this is open again) |

Two properties of today's store that the critiques showed to be at stake, though no requirement
names them:

- **A copy of a ruled node carries its rule.** KeePass's Duplicate Entry and Duplicate Group copy
  node custom data [R critic `wildcard-security-and-fit` 2.3, `PwEntry.cs:321-354`,
  `PwGroup.cs:1437-1446`], and `docs/access-control.md` promises it.
- **A refusal is hard to remove by hand.** Today a revocation key can be deleted only on Database
  Maintenance's Plugin Data tab in KeePass [R ground-inventory A9].

## 2. The survival matrix

What each program does to each feature a store could rest on. A cell in bold is a break.

### 2.1 A save (the program opens the file, changes something unrelated, saves)

| Feature | KeePass 2.62 | KeePassXC 2.4.2 to 2.8.0 | Keepass2Android 1.15-r6 | KeePassDX 4.5.8, 3.2.0, 3.3.0 | KeePassDroid 2.7 | kdbxweb 2.1.1 (KeeWeb), pykeepass 4.2.0 | Read only: Strongbox, KeePassKit, KeePassium, keepass-rs, gokeepasslib |
| --- | --- | --- | --- | --- | --- | --- | --- |
| An ordinary entry as a whole (identifier, group, times, standard strings) | kept [M ground-keepass X7] | kept; adds empty Notes, URL, UserName to an entry that lacks them, time unmoved [M ground-keepassxc 6; M spike] | kept [M spike; M, interim] | kept [M spike, all three] | kept [M spike; M, interim] | kept [M ground-others] | kept [R] |
| A custom string and its protection flag | kept [M X7] | kept, flag kept [M ground-keepassxc 6; M every option probe] | kept, protected strings and a 2,861 character value included, in four saves and two edits [M, interim] | kept [R 4.5.8, 3.2.0; 3.3.0 not read]. **Not measured** | kept in four saves and one edit [M, interim; R] | kept [M] | kept [R]; KeePassium drops a field whose name and value are both empty; gokeepasslib before 3.7.1 can corrupt protected values in some layouts [R] |
| An attachment | kept [M] | kept, every attachment flagged protected [M] | kept [M, interim] | kept, **but two attachments of one length are one body when 32 bits of their MD5 agree** [R; a colliding pair found in 0.16 s, M ground-android] | kept [M, interim] | kept [M] | kept [R] |
| Entry history | **trimmed to the file's limits (10 items, 6 MiB) at every load, save and merge** [M X5] | kept, never trimmed on a save [M] | **trimmed at every load** [R]; three items under a limit of ten kept [M, interim] | trimmed only when that entry is edited there [R] | kept [M, interim]; trimmed on an edit [R] | kept [M] | **Strongbox trims at every save** [R] |
| Entry tags | normalized on read [R] | 2.4.2, 2.6.6 verbatim; **2.7.12, 2.8.0 normalized** [M] | normalized [R] | normalized [R] | kept [R] | kept [M] | kept [R] |
| Entry custom data | kept [M] | kept; `_LAST_MODIFIED` added, time unmoved [M] | kept [M spike] | kept; 3.2.0 drops item times [M spike; R] | **deleted, the entry's time unmoved** [M spike; M, interim] | kept [M] | kept [R] |
| Group custom data | kept [M] | kept; marker added [M] | kept [M spike] | 4.5.8 kept; **3.2.0 and 3.3.0 deleted, the group's time unmoved** [M spike] | **deleted, time unmoved** [M spike; M, interim] | kept [M] | gokeepasslib before 3.7.0 has no field for it [R] |
| Group notes, searching and auto-type switches | kept [M] | kept [M] | kept [R] | kept [R] | kept [R] | kept [M] | kept [R] |
| Group tags | kept, forces KDBX 4.1 [M X7] | **dropped by 2.4.2 and 2.6.6** [M] | kept [R] | kept [R] | kept [R] | | |
| Database custom data, key and value | kept [M] | kept; adds keys of its own [M spike] | kept [M spike; M, interim] | kept [M spike] | kept [M spike; M, interim] | kdbxweb **drops a key whose value is empty**; pykeepass keeps all [M] | kept [R] |
| Per-key times of database custom data (KDBX 4.1) | kept (KeePass 2.47 **dropped**) [M spike] | **dropped** in a 4.0 file; kept from 2.7.0 in a file that is 4.1 for another reason [M spike]; 2.8.0 stamps time-less keys of such a file [M critic `hardened-custom-data-survival`] | **dropped at load** [M spike; M, interim] | kept [M spike] | kept [M spike] | **kdbxweb writes them as text, and KeePass 2.62 cannot open the file** [M ground-others]; pykeepass keeps them [M] | kept [R] |
| Deletion records | kept [M X4] | kept [M] | kept [M, interim] | kept [R] | kept [M, interim] | kept; **pykeepass writes none for its own deletions** [M critic] | kept [R] |
| Modification times of nodes the program did not touch | unmoved | unmoved; **the root group takes the time of every save** [M spike] | unmoved [M spike]; an entry edit moved no group's modification time [M, interim] | unmoved; **the root group takes the time of every save** [M spike]; an entry edit touches every parent group [R] | unmoved on a plain save [M spike]; **an entry edit moved its group's and the root's time** [M, interim] | pykeepass moves no time even on its own edits [R] | |
| XML no program knows | **dropped** [M] | **dropped** at eight positions [M] | **dropped** [R] | **dropped** [R] | **dropped** [R] | kdbxweb **drops**; pykeepass keeps [M] | dropped; **KeePassium refuses the file** [R] |

### 2.2 A merge (two copies that both changed)

KeePassDroid, KeePassium, pykeepass and gokeepasslib have no merge. "Open" is the copy the
program holds, "arriving" the copy merged in.

| Feature | KeePass 2.62 | KeePassXC 2.4.2 to 2.8.0 | Keepass2Android 1.15-r6 | KeePassDX 4.5.8, 3.2.0 | kdbxweb 2.1.1 (KeeWeb) | Read only: Strongbox, KeePassKit, keepass-rs |
| --- | --- | --- | --- | --- | --- | --- |
| An entry only one copy holds | arrives, all five methods, both directions [M ground-keepass X1; M every option probe] | arrives; a merge never removes an entry [M ground-keepassxc 4, four releases] | arrives in both directions, in a dedicated group, three runs each [M, interim]; an entry only the open copy held stayed in 18 of 18 [M spike]; lands at the root when its group is missing [R] | 4.5.8: added, a missing group recreated [R]. 3.2.0: added only if its group exists, **otherwise dropped** [R]. **Not run** | arrives [M ground-others], **unless a deletion record names it or its group** [R; M critics] | added [R] |
| A group made in both copies under one identifier | one group holding the union [M option store-entries, four methods] | one group, the union [M, four releases] | one group [R]; not run | [R]; not run | not run | |
| The same entry changed in both copies, different seconds (two fields added, one in each copy) | **the newer entry whole; the other copy's field only in one history item**. 120 of 120 cells, the same for strings, protected strings, notes, attachments, tags, custom data [M X2]. Overwrite existing takes the arriving entry whatever its time; Keep existing loses a newer arriving version [M X2] | **the newer whole, the older in history** [M, `both-diff` and four more cases] | **the newer whole; the older copy's timestamp-named field only in history** [M, interim, entry M1; M spike] | newer whole [R]; the history item it makes carries the merged custom data, not the loser's [M spike, re-read] | newer fields; **the other copy's custom data never taken** [M] | newer whole, older in history [R] |
| The same, changed in the same second | **the arriving copy's version; the open copy's nowhere** [M X2] | **the target's version; the source's nowhere, nothing printed** [M `both-tie`] | **the arriving copy's; the open copy's nowhere** [M, interim, entry M2] | 4.5.8: the open copy's, and the entry moves to the arriving copy's group; 3.2.0: nothing happens [R] | **the open copy's; the other nowhere** [M] | KeePassKit loses the arriving one; Strongbox and keepass-rs file it in history [R] |
| How long the loser stays in history | until ten later versions or 6 MiB; none at a limit of 0; Database Maintenance deletes by age [M X5, X13; R] | **never added when the winner already holds ten newer items** [M `meta`] | trimmed at the next load [R] | until that entry is edited there [R] | trimmed by count at every merge; a trimmed item travels as a deletion [R] | Strongbox trims at every save [R] |
| Deletion records | Synchronize carries them; the other four methods carry none; the open copy's own are applied under every method; a record is dropped when its object is newer, or is a group that is not empty [M X3, X4] | **never applied and never copied: a deleted entry comes back** into the deleting copy, beside its record [M, four releases] | both lists joined and applied to older objects [M, interim: an entry deleted in the arriving copy was removed and its record kept; a record whose entry was changed later was dropped] | arriving records applied to older entries, **and none kept** [R]. Not run | lists united; **every named node removed whatever its time, a group with everything in it** [R; M critics `store-entries-survival`, `wildcard-survival`] | merged by time [R] |
| Database custom data, a key only ever added | never removed, never changed [M spike on 2.61.1, 70 of 70 cells; M critic `versioned-fields-survival` on 2.62, Create new IDs included] | **from 2.4.2 the copy loaded later replaces the whole dictionary: added keys are removed** (96 of 96 slow-KDF runs) [M spike]; nothing is merged when both loads fall in one second [M spike]; 2.4.1 merges none | kept [M spike, 18 of 18] | kept, by per-key time [M spike, 18 of 18] | a key the open copy lacks is added; with no times the open copy's value stays [R] | keepass-rs merges no metadata [R] |
| Database custom data, a value changed in place | by per-key time, then `SettingsChanged` [M spike] | **the other copy's whole dictionary** [M spike] | **a stale value came back in 9 of 18** [M spike] | by per-key time [M spike] | open copy's without times [R] | |
| Entry custom data | with its entry, whole [M X2] | with its entry [M] | with its entry [M spike] | **the arriving copy's item replaces the open copy's, whichever is newer** [M spike] | **never taken from the other copy** [M] | with the entry [R] |
| A group's own fields: notes, tags, switches, custom data | the newer group whole; the arriving copy at a tie; no history [M X6] | **custom data, tags and switches never merged, yet the group takes the newer time**; notes only when the source is strictly newer [M] | the newer group whole [M spike; M, interim for notes] | 4.5.8: **the arriving copy's custom data under the newer time** [M spike] | no custom data [M] | newer whole [R] |
| Where a node sits: moves, the recycle bin | Synchronize only, by `LocationChanged` [M X3] | the bin is not merged: another copy's bin arrives as an ordinary group [M] | by `LocationChanged` [M, interim] | **an entry with equal times moves to the arriving copy's group** [R] | | |

Two operations that are not a merge of two versions:

| Operation | What happens | Basis |
| --- | --- | --- |
| KeePass, File then Import with "Create new IDs", **the preselected method** | the whole arriving tree is added again under new identifiers at the open root; no deletion record travels; database custom data keys are still merged | [M ground-keepass X1, X3, X4; R `ImportMethodForm.cs:37`]; [M critic `versioned-fields-survival`] |
| A whole file written over another: Keepass2Android "No, overwrite", a KeePassDX save within 10 seconds of the other change, **every KeePassDroid save over a file that changed underneath it**, KeePassium on a conflict, a file synchronizer's conflict copy | one file wins whole; nothing merges | [R ground-android 4.2, 5.5]; [M spike for KeePassDX]; [R ground-others] |

### 2.3 What the matrix leaves standing

**Does not break in any save measured, and is added by every merge measured or read**: an
ordinary entry that is written once and never changed, with its content in custom strings
(protection flag included), in a group of the plugin's own. That is the one unit every program
keeps whole and every merge unites. Its limits, all in the cells above: a deletion (by hand, or
by a deletion record where one travels), KeePassDX 3.2.0 dropping an arriving entry whose group
it lacks [R], kdbxweb removing whatever a deletion record names, the duplicates of an import with
new identifiers, and a whole file written over another.

**Breaks somewhere, and cannot carry the store**: a field added to an entry both copies change
(the timestamp-named-field idea as worded); entry history; entry and group custom data; a group's notes,
tags and switches; per-key times; a value of database custom data that is changed in place;
deletion records as the only witness; tags; attachments under KeePassDX; unknown XML.

**In between**: a key of database custom data that is only ever added survives every save
measured and every merge except a KeePassXC merge from 2.4.2, which removes it [M spike]. It can
be a second witness. It cannot be the only one.

**Nothing inside the file survives a whole-file overwrite.** KeePassDroid does that at every save
over a changed file [R ground-android 4.2, `SaveDB.java:54-75`]. No option that keeps the data
in the `.kdbx` meets C0 against it, and this evaluation does not recommend around it: it is
question 1 for the project.

## 3. The options

Verdicts are the critics' unless marked as mine. "Changes" means changes each critic named and,
in most cases, measured.

### 3.1 `store-entries`: the database-level data as ordinary entries in a group of the plugin's own

**What it is.** One record is one entry, written once and never changed, in a group found by a
fixed identifier. A change of decision is another record beside the first (a revocation beside a
client's record, a retirement beside an authority, a removal beside a profile's addition). What
the gate serves is a fold over the records at every read; nothing is written at open or after a
merge. Granting records count only in place; refusing records count wherever they are found.
Covers data 1 to 6, not the rules.

**Survives.** Union of both copies' records in every KeePassLib 2.62 method that matches
identifiers and in four `keepassxc-cli` releases, both directions, 59 results, all KDBX 4.0
[M option]. A revocation reached a stale copy through a KeePassXC merge and refused the record
that came back with it [M option; M critic]. Nothing reads a time, history or custom data, so
none of the spike's failures reaches it.

**Breaks** (survival: viable with changes; security and fit: viable with changes).

- A refusal often has one witness, and one deletion by hand takes it. After a KeePassXC merge
  into a stale copy, deleting the entry `KPG revoked client c1` with pykeepass admitted the
  revoked client [M critic, chain A]. A KeePassDX merge does the same to a recorded deletion [R].
- Profile identifiers name a generation, not an act: a removal made in one copy is undone by a
  re-addition made in another copy that never saw it (follows from the design's rule and the
  measured union).
- The fixed group identifier is reused after a deletion, and kdbxweb then drops the remade store
  whole; carried through to a revoked client admitted [M critic, chain C].
- A refusing record is believed by its content wherever found, and the prune then deletes what
  it refuses: a Bitwarden JSON import carrying two strings retired the authority for good
  [M critic `store-entries-security-and-fit`].
- A granting record in the recycle bin returns with no person acting through a KeePassDX
  equal-time move [R].
- The authority's private key is one copy, print or export away in every program [M
  ground-keepassxc 8 for KeePassXC's HTML export; R for KeePass's print and copy].

**My judgment where design and critic disagree.** The design's "every way to damage the store
lands on the refusing side" is contradicted by chain A, and the ground supports the critic:
KeePassXC copies no deletion record [M ground-keepassxc 2] and pykeepass writes none [M]. But
the act that completes the chain is a deletion by hand, not a save or a merge, so E3 as worded
holds; what fails is "a refusal is hard to remove by hand". On the authority, both security
critics propose keeping it in database custom data and call the price nothing. That is not what
the ground shows: a KeePassXC merge of a copy from before Create or Recreate removes the
authority's key (the spike's "new authority removed"; 8 of 8 in the `outside-file` probe). The
outcome is closed, and costly when no other copy holds the key.

**Reverses.** Custom data as the store; KDBX 4.1; key by key replay; the Database Maintenance
paragraph of the trap section; `CA.md` 204 and 208.

**Costs.** The storage seam and the classes above it (42 source files name them [R critic]) and
a rebuild of the tests that name the store (49 files, about 26,000 lines [M critic]); a pass
over the tree on every request (1.9 ms for 10,054 entries in a console [M option]); visible
entries that only the KPG tab may remove.

### 3.2 `versioned-fields`: the timestamp-named-field idea in its strongest form

**What it is.** Every write is a JSON document in a string field named `KPG.<time>.<origin>.<n>`.
The idea as worded (fields in one shared entry) does not hold in any program (section 2.2, third
row), so the design gives every write an entry of its own and keeps the field name as the
version's identity. Closing writes are written as a pair of entries that carry the whole state
and replace the older ones. A rule also has a copy on its node (a field on the entry, a marker
entry in the group). Order is by an `after` list, never a clock. Every version carries a store
identifier no file made elsewhere knows. Covers all eight data.

**Survives.** 160 merges (KeePassLib four methods, four KeePassXC releases, ten pairs, both
directions): every verdict held except 21 cells that are one case, the stale copy that a merge
told nothing [M option]. Concurrent rule edits read as what both allow; a removal won over a
concurrent re-addition; planted versions of another store counted for nothing [M option]. An API
client cannot plant a version or reach `503 damaged` [checked by the security critic].

**Breaks** (both critics: viable with changes).

- An import with new identifiers carries no revocation, retirement or narrowing into a stale
  copy, because unmarked copies are not read: a revoked client admitted and a retired authority
  serving, where today's store carries both through the same import [M critic].
- Under KeePassXC every state pair ever written stays in both files. Eight renewals took the
  store from 3 entries and 6,481 characters to 19 and 62,297 [M critic]. That fails A5.
- The design's rule for reading a node's history is narrower than the rule its probe ran, and a
  KeePassXC merge then widens a narrowing in the copy that made it [M critic, pair H5].
- "Remove rules of nodes that are gone" lets a KeePassXC merge bring the entry back under its
  older rule [M critic].
- One hand deletion in a group of entries that look like junk stops the whole gate
  (`503 damaged`); the authority key is exposed as in 3.1.

**Reverses.** As 3.1, and: one document per node, the grant editor's `m_sdCustomData`, "the
plugin writes no rule of its own", three tab pages become four, a new `503` word.

**Costs.** Among the largest rewrites of the in-file options: a reconciler that decides
authorization from several sources on every request, a fourth page, about 38,000 characters of
store for the inventory's example against 14,000 today.

### 3.3 `wildcard`: one snapshot of the whole state per write

**What it is.** Everything is one document. Each write adds a new entry holding the whole new
state and deletes the entries it replaces; two copies that both wrote leave two snapshots, and
what is in force is their join. Deletion records are the witnesses of replaced and lost
snapshots.

**Survives.** The same joined state in both directions in all 40 KeePass and all 40 KeePassXC
cells of five sets; a 1 MiB document through KeePassLib, four KeePassXC saves and a KeePass 2.47
save as KDBX 3.1 [M option].

**Breaks** (both critics: viable with changes).

- kdbxweb drops the store group with every snapshot when a deletion record names its fixed
  identifier, and the design makes that record itself (Delete Empty Groups while a snapshot sits
  in the bin). In one shape the result is a silent widening read as in force [M critic, e4].
- The witness is absent exactly in the copies that got the snapshot through KeePassXC, KeePass's
  three other methods or KeePassDX. A hand deletion there leads to "Use what is here"
  re-admitting a revoked client [M critic, e3].
- The profile list joined by the higher number undoes a concurrent removal.
- "Use what is here" folds an imported foreign store into this one [by the design's rule, steps
  measured].
- A duplicate of a ruled node carries no rule; a change pressed OK can be refused after its
  dialog has closed.
- The cliff: one deleted or damaged entry stops the gate for the database.

**Reverses.** As 3.2. **Costs.** About twenty classes; every write rewrites 5 to 40 KB.

### 3.4 `single-entry`: the whole state in one entry, changed in place

**What it is.** The evaluation's later question: one entry, so that a merge is "one or the other".
The design adds a lineage inside the document, a reconciling step that reads the entry and its
history, a small "version witness" entry per version, and revocations and retirements as
entries of their own.

**The answer to the question.** "One or the other" is true in every program [M option, 128
cells]. But the winner is chosen by the entry's modification time, so a stale copy touched later
wins, and at a tie the other version is kept nowhere, with KeePass and KeePassXC keeping
opposite sides [M option; M ground-keepass 1a; M ground-keepassxc 4]. With the additions the
gate never served a stale state in the cells run, at the price of refusing: 16 of 64 KeePassXC
cells and 17 of 64 KeePass cells without the plugin's memory ended in `503 undecided` [M option].
It is the only design that leans on history, which the matrix shows to be the weakest carrier.

**Breaks** (both critics: viable with changes).

- The lineage is cut at 256 versions, and past that the step judges wrongly in every reading of
  the design: a copy a few months stale brought a removed profile back onto the list, or refused
  nodes the operator had widened, or lost a concurrent narrowing without a refusal [M critic,
  W1, W1b]. The fix is an exact lineage (a counter per writing installation).
- KeePassXC brings every deleted witness back, and past the window the gate refuses for a
  version nobody lost: `503 undecided` on a laptop's file with nothing missing [M critic, W2].
- A foreign file merged under the fixed identifiers adds a widening rule on a node that had
  none (every node below a seal), with no second authority and so no `503`; the design states
  the opposite [M critic `single-entry-security-and-fit`, S1]. The fix is an authentication of
  the document, the change three options converge on.
- "Continue with what is here" names the missing version as seen, so when that version turns
  up its narrowing and its pairings are thrown away [M critic].
- The authority's key in the one entry; one deletion or one damaged string stops the gate; at a
  true tie merged elsewhere one side's rules are lost.

The design itself calls `wildcard` "the stronger way to get" one state taken whole.

### 3.5 `rules-on-node`: rule text in standard fields of the node, with seals elsewhere

**What it is.** An entry's rule is a custom string on the entry; a group's rule is a string on a
rule entry inside the group. A "seal" entry per rule version in the plugin's group decides which
text is in force; the text is found on the node or in the carrying entry's history.

**Survives.** With the seals intact, 176 merge cells gave the head rule or a refusal [M option].

**Breaks** (both critics: viable with changes). Losing one seal fails open: after an ordinary
rollback, one deletion of the newest seal entry puts the older, wider rule in force, in 6 of 8
cells [M critic]. The design's own prune turns a refusal into a widening under a KeePassXC merge
[M critic]. Any OK of the entry dialog seals whatever text it finds. A displaced rule entry at
the root refuses every root-anchored answer.

**My judgment.** The design names its own weakness correctly: the fail-closed property comes
from the seal, and the text on the node is the part other programs and people can damage. It is
dominated by keeping the text beside the seal (3.7, 3.8).

### 3.6 `rules-marker-entry`: rule entries inside the group they govern

**What it is.** A rule is an entry written once, sitting in the governed group (an entry's rule
beside the entry). Add-only `ruled` and `replaced` records in the plugin's group make a missing
rule refuse.

**Survives.** 560 verdicts over 26 merge results, none wider than the last rule set [M option].

**Breaks** (both critics: viable with changes). The content-derived identifier comes back: a
later narrowing made in a second copy came out dead in all 10 merges tried [M critic], and the
tab's own restore writes the identifier of the entry that was lost. Deleting the narrower of two
concurrent rule entries widens to the other [M critic]. An import with new identifiers followed
by the tab's Remove opens every duplicate [M critic]. An entry's rule entry stays in the group
the entry left. The rule write can land inside KeePass's save [R].

**My judgment.** It puts the text, not only a reference, into the user's groups, where a tidy-up
on a phone closes a group. 3.8 keeps what this buys (the rule's mark travels with the group and
its copies) without the text there.

### 3.7 `rules-central`: rule versions as entries in the plugin's group, naming the node

**What it is.** Nothing is written on a governed node. Each version of a node's rule is one
entry, written once; the newest is in force; two newest give their meet (what both allow); a
deleted or hand-changed version refuses.

**Survives.** 224 cells, none with an older rule in force: the newest rule, the meet or a
refusal [M option]. Another copy editing the governed entry or group, even in the same second,
never touched the rule. No KeePass dialog holds a rule any more.

**Breaks** (both critics: viable with changes, with one condition).

- A lost newest version leaves no trace in the copy a KeePassDX merge writes, and the plugin's
  own Remove makes such losses [M critic for the KeePassLib and pykeepass steps; KeePassDX step
  modeled from source].
- The repair writes the older rule on any OK and carries the rollback into copies that still
  held the narrowing: all four KeePassLib methods and two KeePassXC releases [M critic].
- A file the operator merges can rule any node whose identifier it knows [M critic]. Node
  identifiers are what the API returns.
- **A copy carries no rule**, Duplicate Group of a sealed subtree included, and after KeePass's
  preselected import the copies of ruled nodes carry none [M option]. The security critic's
  condition: if a narrowed secret must stay narrowed when a program copies it, no change inside
  this option does that.

**Reverses.** One document per node; the grant editor's `m_sdCustomData`; "the plugin writes no
rule of its own"; the documented promise that a copy carries its rule.

### 3.8 `rules-linked`: rules-central with a reference back from the node

**What it is.** Written for the idea that references between a rule and its node
should run both ways. Rule versions as in 3.7, each with a random nonce and a second witness (a
record in the store group). The node refers back through an anchor that never changes: a custom
string `KPG.Anchor` on a ruled entry, a small link entry inside a ruled group. A rule the node
does not refer back to only takes away (a "limit"); a node that refers to a rule which is not
there refuses; a copy, which carries the anchor under a new identifier, gets the original's rule
as a limit.

**Survives.** 192 merge cells and 17 acts by hand: never more than the operator's last decision
[M option]. A copy of a sealed group stayed sealed through Duplicate Entry, Duplicate Group,
paste and an import with new identifiers [M option]. A rule planted for a single entry granted
nothing [M option]. A phone edit of a ruled entry keeps the anchor: Keepass2Android [M,
interim], KeePassDroid and KeePassDX [R critic].

**Breaks** (both critics: viable with changes).

- A copy is protected by one carried reference. After a copying drag of a sealed group, Delete
  Duplicate Entries deleted the copy's link and the copy was open [M critic, C1b]. That equals
  3.7's starting point; it is not worse.
- The newest version and its record lost together, then a KeePassXC merge of an older copy: the
  older rule in force with nothing shown [M critic, C3].
- One deletion of the rules group plus a Synchronize leaves no rule text in either copy [M
  critic, C5]. Closed; every rule typed again.
- A group's rule can be planted from both sides by a merged file [M option 5.5]; the evidence
  the tab shows for Confirm is what a merged file can forge [M critic]. Both close only with an
  authentication of versions, which the design prices and leaves out.
- A pasted or duplicated link closes the group it lands in; a rule write can land inside a save
  [M critic].
- Grants that wait: a first grant on an entry edited later in another copy waits for a Confirm
  press in 6 of 8 cells [M option].

**My judgment.** The two critics measured opposite outcomes for Delete Duplicate Entries (one
saw the copy's link deleted, the other the original's). Both are right: KeePass deletes
whichever of two equal entries comes first in tree order [R `PwDatabase.cs:1995-2029`]. Most of
this design's remaining trouble (suspended grants, Confirm, the forged evidence, the group
plant) comes from treating a rule the node does not confirm as suspect. A store identifier in
every version, as 3.2 has it, removes that reason; section 5 builds on it.

### 3.9 `outside-file`: what changes kept in a sealed folder beside the database

**What it is.** The authority and a 32-byte store key stay in database custom data; records,
revocations, retirements, answers, names and rules move to a folder beside the file, one sealed
file per plugin installation, joined at load.

**Survives.** No KDBX program reaches data 2 to 7 at all. It is the only option in which a
whole-file overwrite by another program does not take revocations and rules with it.

**Breaks** (both critics: viable with changes; the security critic adds that one problem cannot
be closed). It reverses the first settled decision (E9: no sidecar, the trust domain is the
file). Two gate computers that share the file through KeePass's own Synchronize share no store,
and revocations stop travelling that way. A narrowed node can arrive in the file before its rule
arrives in the folder, and that gap cannot be closed in this architecture. A copy of a node
carries no rule. A backup of the `.kdbx` alone is a closed gate. A KeePassXC merge from a copy
that lacks the two file keys removes them, 8 of 8 [M option]. It adds a cipher of the plugin's
own and rests on about seven unmeasured assumptions about real file synchronizers.

### 3.10 `hardened-custom-data`: stay in custom data, mirrored in the root group

**What it is.** The baseline. Every database-level key also in the root group's custom data;
keys written once; rules stay on the node with a generation and a ledger.

**Breaks. Survival critic: not viable; security critic: viable with changes.** I side with the
survival critic. Its chain is measured: after a KeePassXC merge the fresh state lives only in
the root group's custom data, and the next merge (KeePass's own Synchronize with the plugin
installed included) takes the root group whole from one copy; 4 of 5 cells ended with 7 of 14
keys, the revocation, the retirement and the profile removal gone [M critic]. The ground agrees
on both halves: KeePassXC replaces the dictionary [M spike] and KeePass merges a group whole by
its time [M ground-keepass X6]. The security critic did not test merges; within its own lens it
found four more widenings. And the rules stay in node custom data, which KeePassDroid and
KeePassDX 3.2/3.3 delete at every save [M spike; M, interim]: after each such save every grant
is off until a person restores it. This option does not meet C0, E3 or E4 and is not
recommended in any form.

## 4. The options against the essential requirements

"Changes" means: with the changes the critics named. Rules-only options are judged for datum 7.

| Option | C0: only features nothing breaks | E3, E4: revocation and retirement through saves and merges | E5: narrowing kept | E6: lost rule shows or refuses | E9: a merged file vouches only for itself | E10: the authority key's exposure | A copy of a narrowed node | Refusals with nobody at fault | Cost |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `store-entries` | yes | yes in every merge run; one hand deletion can undo where the entry is the only witness; changes | profile list: changes; rules not covered | not covered | changes (refusals believed by content) | worse: key in an entry | not covered | few | large |
| `versioned-fields` | yes | changes (import with new identifiers) | changes | yes, three witnesses | **yes** (store identifier) | worse | refuses until settled | whole gate on one hand deletion | largest but one |
| `wildcard` | yes | changes (add-only refusal entries) | changes | changes | changes | worse | **no** | the cliff: whole gate | large |
| `single-entry` | **no**: leans on history | by separate entries | changes (exact lineage) | by refusing | **no** as written (planted rule measured); changes | worse | partly (closed groups) | at every tie merged elsewhere | largest |
| `rules-on-node` | partly: text on the node | | changes | changes | changes | | refuses | subtree | large |
| `rules-marker-entry` | yes | | changes | changes | changes | | changes | a tidy-up closes a group | large |
| `rules-central` | yes | | changes | changes | changes (authentication) | | **no, and not fixable inside it** | node and subtree | medium |
| `rules-linked` | yes | | changes | yes, node as third witness | entries yes; groups only with authentication | | **limited by the original's rule** (one reference deep) | first grants wait; subtree | medium to large |
| `outside-file` | data 2 to 7 leave the file | yes, overwrite included | yes in the file; gap between file and folder | yes | **reverses E9** | same as today, plus a store key | **no** | gate closed on file-management events | large, own cipher |
| `hardened-custom-data` | **no** | **no** (measured) | no | partly | as today | one more place | as today | every grant off after a KeePassDroid save | large |

## 5. Recommendation

### 5.1 The design to adopt

**Records as entries: `store-entries` as the base for the database's own data, `rules-linked`
for the rules, both with their critics' changes and with the store identifier of
`versioned-fields`.** It is the timestamp-named-field idea with its unit changed from the field to the
entry, which is the one change the measurements force, and it keeps both things the evaluation
asks for: a global place for all KPG data, and a small meta entry inside a ruled group.

Why this family and not another: the matrix has exactly one unit that every save keeps and every
merge unites, the entry that is added once and never changed. `store-entries`, `versioned-fields`
and `rules-linked` are built on it; their critics' changes converge (act identities with a random
part, no fixed group identifier, no repair on a plain OK, refusals with more than one witness,
no Remove for rules of absent nodes). `wildcard` and `single-entry` make the unit the whole
state, and then losing or misjudging the newest state rolls everything back or stops the whole
gate. Small records fail one datum at a time, and each failure is closed: a client pairs again,
a node refuses.

**What the three ideas become.**

- "JSON in a field named from a timestamp, so a merge keeps both": JSON in a custom string of an
  entry of its own. The time may be in the title for the person reading it; identity is a random
  part or a digest, never the time, because two copies write in the same second and clocks
  disagree.
- "A meta entry inside the group, or a global meta entry": both. A global group holds every
  record. A ruled group holds one small link entry that carries no rule, only a reference.
- "A preprocessing step when loading": a fold at every read that writes nothing, so opening
  still changes nothing (O1). Repairs are presses on the KPG tab.

**Datum by datum.**

| # | Datum | Where | Why there |
| --- | --- | --- | --- |
| 1 | Authority: certificate, private key, host PKCS#12, gate key | One entry in the plugin's store group, written once, identifier derived from the certificate's digest, today's `AuthorityDocument` unchanged in a protected custom string | C0. In database custom data it is removed by a KeePassXC merge of any copy from before Create or Recreate [M spike; M option `outside-file`], and if no other copy holds it every client pairs again. As an entry it is united by every merge run. The price is exposure (below) |
| 2 | Retirement | One entry, added once, never removed, **and** today's key `KPG.RetiredAuthority.<fp>` beside it. A retirement counts when either is found, wherever the entry is | E4 is absolute. The two carriers fail under different programs: entries go by deletion, the key goes only in a KeePassXC merge. The key is already written today |
| 3 | Admission record | One entry per certificate, identifier derived from its digest, today's record text unchanged. Counts only inside a store group, outside the recycle bin, with the database's store identifier, and with no revocation. Deleted by the plugin at revocation and at expiry | A1: two copies' pairings unite. Loss fails closed (E2) |
| 4 | Revocation | One entry per certificate, added once, kept until the certificate has expired, **and** today's key `KPG.Revoked.<key>` beside it. Counts when either is found, wherever the entry is, the recycle bin included | E3 is absolute; as for 2. It answers `store-entries` P1 (one hand deletion) in the way its critic proposed |
| 5 | Standing answer to renewals | One entry per answer, with an identity of its own and the list of answers it replaces; the strictest unreplaced answer is in force; a lift is an answer `allowed` | A lift then travels, which it does not today. An answer never admits, so every loss costs a dialog |
| 6 | Profile list | One entry per act (addition or removal), each with an identity of its own; an addition lists the removals it saw. A name is on the list only when an addition counts and every removal found is one that addition saw | A removal wins over a re-addition made without knowledge of it (E5); names added in two copies both stay (A3). This is the fix for the generation flaw found in `store-entries`, `wildcard` and `hardened-custom-data` alike |
| 7 | Access rules | Rule versions as entries in a second group of the plugin's own, written once, each naming its node, the versions it replaces, a random nonce and the store identifier; a record of every version in the store group, carrying the rule text. On the node only a reference that never changes: a custom string on a ruled entry, a link entry inside a ruled group | The text is where no other program has a reason to edit it and no merge decides by a time [M option `rules-central`, `rules-linked`]. The reference lets a copy be recognized in every program that copies strings and entries, and lets the node itself witness a rule that is gone (E6) |
| 8 | Recycle bin markers (T049, if taken) | One more kind of refusing record in the store group | A marker only hides, so it needs no reference from the node |

**The rules that make it fail closed** (each is a critic's change or a design's own measured
rule):

1. Nothing reads a modification time, a history item, custom data on a node, a group's own
   fields or the recycle bin pointer. Nothing is written at open or after a merge.
2. What grants counts only in place and only with this database's store identifier; what refuses
   counts wherever it is found. A granting record found in the recycle bin is not counted and is
   turned into a revocation by the plugin's next write, so that it cannot come back silently.
3. Every record carries the database's store identifier, a random value that no route serves and
   no file made elsewhere knows. Records of another store count for nothing and are listed on
   the tab. This closes the planted retirement, the planted rule and the forged Confirm evidence
   in one step [M option `versioned-fields`, file F1, for the mechanism]. The critics of
   `store-entries`, `rules-central`, `rules-linked` and `single-entry` converge on the same
   need and propose a MAC under the served authority's gate key; either form does it, and the
   MAC also shuts out the holder of an old copy once the authority has been recreated, at the
   price of signing every record again at Recreate.
4. Two decisions that never saw each other resolve to the closed side: a removal wins, the
   stricter answer wins, two rule versions give what both allow, two unretired authorities
   answer `503 undecided` as today.
5. A node refuses, there and below, when a recorded rule version is missing, when a version was
   changed by hand, or when the node refers to a rule the file does not hold.
6. A copy of a ruled node (it carries the reference under another identifier) gets the
   original's rule as a limit: it can take away and cannot give.
7. Nothing the gate found wrong is settled by a dialog's plain OK. Settle starts from a rule
   that grants nothing and never names a version the plugin did not read. There is no Remove for
   the rules of absent nodes.
8. The plugin's groups get a fresh identifier each time they are made, so that no deletion
   record can name a group that is made again (the kdbxweb drop).
9. The API guard is explicit and complete: the plugin's groups and every entry carrying
   `KPG.Kind` are absent on every route, as the recycle bin is; a string whose name begins
   `KPG.` is hidden and refused on every field route, compared with KeePass's own collation; an
   entry's tag is computed without it.

**How it sits in KeePass.** No KeePass dialog copies a group's list of entries and puts it back
[R critic `store-entries-security-and-fit`, checked item 4], so no OK undoes an added record,
and adding an entry moves no time on its group [R `PwGroup.cs:1342-1354`, as the marker design
cites it], which keeps E11 and E13. Database Maintenance's write-back can only cost a
second-witness key, which the entry covers, so the writer's refusal while that dialog is open
goes. A pairing still hands over its certificate only after the record's entry is saved and
verified (E7). The entry dialog matters in two places: the plugin skips deleting a record that
is open in a dialog, and a first rule on an entry is written into the dialog's public
`EntryStrings` [R option `rules-linked` 4.1; not driven in a window].

**One amendment of mine, to be probed before it is trusted.** `rules-linked` treats a rule that
its node does not refer back to as a limit, because such a rule could have been planted. With
the store identifier in every version, a planted rule counts for nothing anyway, so a rule that
names a node can be in force whether or not the node refers back. That removes the grants that
wait for Confirm, the Confirm press itself, and the suspended grant after a link is deleted or
deduplicated; the reference then has two jobs only, marking copies and witnessing a lost rule.
No probe has run this combination. It is item 2 of section 6.

**What it reverses of the settled decisions, each to be recorded as a reversal.** The database's
own `CustomData` as the store, and the whole bullet about per-key times and one key per
authority (its finding, that what must survive a merge is something only ever added, stays and
widens to every datum). One rule document under a `KPG` key of the node, the grant editor
writing into `m_sdCustomData`, and "the plugin writes no rule of its own". "A copy of a node
carries its rule" becomes "a copy carries what its original's rule takes away". Key by key
replay and `KPG.Profiles` as a difference. "Nothing in a record is authenticated beyond the
file" gains the store identifier. No route hid anything inside an entry it serves; now a name
prefix is reserved. Three pages on the KPG tab become four. The Database Maintenance paragraph
of the trap section stops being about the store.

**What it keeps.** Every database its own authority inside itself, no host registry, no sidecar;
the admission record load bearing; no passphrase on the authority key; tokens and held approvals
in memory only; one writer, saved and verified before a certificate is handed over; live reads;
the settings tab waits for OK; OK keeps a rule and Cancel discards it; nearest wins per profile,
the rule document and `CRUSDLAP`; one JSON grammar; the same `404` for refused and absent;
opening changes nothing.

**What it costs.**

- A rewrite of the storage seam and of the grant editor's write path, and a rebuild of the tests
  that name the store: 49 test files touch the database-level store [M critic] and 27 the rule's
  key or the dialog's copy [M critic]. A fourth page on the KPG tab.
- Visible data: a `KeePassGate` group and a rules group in every program, a field on every ruled
  entry, a link entry in every ruled group. People can delete them; every such loss is closed,
  and the tab is the only safe place to remove a client or a rule.
- The authority's key in an entry: one reveal or copy away, printed by KeePassXC's HTML export
  [M ground-keepassxc 8] and by KeePass's print when custom strings are ticked [R], and shown in
  clear on KeePassDroid's entry screen, which ignores the protection flag on display [R critic
  `single-entry-security-and-fit`, `EntryActivityV4.java:60-70`]. What bounds it: any channel
  that leaks it leaks every password in the file as well, and the key alone admits nobody, since
  a certificate needs a record in the database (E2). The critics weigh this differently among
  themselves: two propose leaving the authority in custom data, a third observes that this is
  exactly what C0 forbids. Carrying the document as an attachment of that entry would keep it
  out of prints, the HTML export, the one-click copy and KeePassDroid's field list; that is a
  measurement (section 6, item 1) and a question (section 7, question 4).
- A pass over every entry on every request: 1.4 to 3.2 ms for about 10,000 entries in a console
  [M options]. Not measured on KeePass's thread.
- While a retirement or an unexpired revocation exists, the second-witness keys keep the file at
  KDBX 4.1 as today, and with it the KeeWeb defect (a 4.1 file with timed custom data saved by
  KeeWeb cannot be opened by KeePass [M ground-others]). Without the keys the file is 4.0.
- One record per rule version for as long as the node lives.

**What it does not solve, plainly.**

- **A whole file written over another.** KeePassDroid at every save over a changed file,
  Keepass2Android's "No, overwrite", KeePassDX within ten seconds, a synchronizer's conflict
  copy. Everything written since the winning copy was taken is gone, revocations included, with
  no sign in the file. No design inside the file can tell. While KeePass still holds the
  database, its next save finds the file changed and offers Synchronize, which puts the union
  back [R ground-keepass 4]; the loss needs KeePass to have closed or locked in between, and a
  lock reads the file again at unlock. One partial answer exists and needs a ruling: a memory on
  the gate's own machine, in KeePass's configuration, of the certificates it revoked, the
  authorities it retired and the newest record it wrote. It could only ever refuse (a remembered
  revocation holds whatever the file holds; a file that has lost the newest record answers
  `503`), so it gives a file made elsewhere nothing, but it is data about a database outside
  that database, which E9 forbids as written (proposed by the critic `single-entry-survival`,
  2.1; not built or measured).
- **A stale copy on its own** admits whoever it admitted.
- **Several witnesses removed by hand**, then a merge that carries no deletions with an older
  copy: for a revocation, the entry and the key; for a rule, the version, its record and the
  node's reference.
- **A KeeWeb merge** can drop a group a deletion record names, with what is in it. With fresh
  group identifiers and two witnesses that costs availability, not access.
- **The Android merges.** Keepass2Android is measured only by the interim run (entries unite in
  both directions, a tie takes the arriving copy, deletion records are joined). **KeePassDX's
  merge has not been run for any entry-based shape**, and its source reads as dropping arriving
  entries whose group is missing (3.2.0) and keeping no arriving deletion record. Until item 1 of
  section 6 has run, the recommendation meets C0 for KeePassDX on reading only.

### 5.2 The runner-up

**`wildcard` with its critics' changes**: one snapshot entry per write, joined, with revocations
and retirements as add-only entries of their own and rules named by node identifier. It is the
strongest form of "one thing, taken whole", which is what the `single-entry` question
asks for, and it leaves nothing in the user's groups and almost nothing to tidy. It loses to the
recommendation on three counts: a copy of a narrowed node carries no rule, one deleted or
damaged entry stops the gate for the whole database, and the detection of a lost newest snapshot
rests on deletion records, which KeePassXC never copies and KeePassDX never keeps.

Within the recommended family there is one fallback worth naming: if the operator does not want
anything of the plugin's on a user's entry or in a user's group, take `rules-central` for datum 7
with the same changes. Copies of narrowed nodes then inherit from where they land.

### 5.3 What would change my mind

1. **The Android runs.** If KeePassDX 4.5.8 or 3.2.0 drops or mangles a record entry in a merge
   or a save, or Keepass2Android's final report contradicts the interim results, no in-file
   design meets C0 for that program, and the honest outcome is to name that program unsupported
   for merging, not to pick another in-file layout.
2. **A decision of the project that a whole-file overwrite must be survived.** Then only
   `outside-file` qualifies, with E9 reversed and its uncloseable gap accepted.
3. **A widening in the probe of the combined design**, above all in my amendment. Then the rules
   fall back to `rules-linked` as written (a rule the node does not confirm only limits), with
   its waiting grants.
4. **The pass on KeePass's thread** costing more than the gate can afford at the size of an
   existing database, with no cheap pre-test of entries.
5. **The operator weighing the authority key's exposure above a dark gate.** Then datum 1 stays
   in database custom data, which is a change to one class, at the price of section 3.1's
   judgment.

## 6. What has to be measured before the decision is safe

Most important first. Items 1 and 7 need the emulator or a window and wait for the desktop.

1. **Android, on the emulator, with the spike's flows** (`spikes/revocation/android`:
   `run-A.sh` for a save, `run-B.sh` for a merge, three runs a cell) over the storage set of
   `ground-android.md` section 7, extended with the recommended shapes (a store group with
   record entries, a 2,903 character protected string, a `KPG.` string on a user entry, a link
   entry in a user group, deletion records):
   - finish and report the run in progress (Keepass2Android 1.15-r6 and KeePassDroid 2.7 saves,
     edits, deletes; Keepass2Android merges in both directions);
   - **KeePassDX 4.5.8 "Merge data" and "Merge from", and KeePassDX 3.2.0, both directions**: does
     an entry only the arriving copy holds arrive in the store group; what happens when the open
     copy lacks that group (3.2.0); are arriving deletion records applied and none kept; where do
     entries with equal times go; is a group made in both copies one group;
   - saves by KeePassDX 3.2.0, 3.3.0 and 4.5.8 of the same file, and Keepass2Android's KDBX 3.1
     output for an AES-KDF file;
   - KeePassDroid saving over a file changed underneath it, to put a number on the loss nothing
     covers;
   - the copy commands of Keepass2Android and KeePassDX: do the copy of a ruled entry and of a
     ruled group carry the reference string and the link entry;
   - the authority as an attachment against a protected string: what each program's entry view
     shows and offers to copy.
2. **A KeePassLib 2.62 console probe of the combined design**, which no report has built: the
   fold of section 5.1 with the store identifier, the profile acts, the second-witness keys and
   my amendment. Against it, in all five merge methods and both directions: the critics' chains
   that broke the parent designs (`store-entries-survival` A, B, C; `rules-central-survival` C1
   to C5 with KeePassDX's merge modeled from its source; `rules-linked-survival` C1 to C6;
   `rules-linked-security-and-fit` plant, paste, template and dedup cases;
   `versioned-fields-survival` P1 in both directions of an import with new identifiers), two
   copies each removing and re-adding a profile, three copies merged in every order, and hand
   deletions with and without deletion records (pykeepass for the latter).
3. **`keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0** over the same sets, both directions, with
   a slow and a fast KDF (the fast KDF is the case in which KeePassXC merges no custom data, so
   the second-witness keys do not arrive), and `rm` and `mv` of record entries before a merge.
4. **kdbxweb 2.1.1** (the Node script of `ground-others.md`) over the same sets: its rule that a
   deletion record removes whatever it names, with plugin groups under fresh identifiers.
5. **The cost of the pass inside the gate**, on KeePass's thread, for 1,000, 10,000 and 50,000
   entries, a full listing included, beside today's read.
6. **The API guard as host tests on every route**: a plugin entry in a user group and in the
   bin, the reserved prefix in each letter case and with characters KeePass's collation ignores,
   `deleteGroup` over a group that holds only its link, the tag of a ruled entry.
7. **KeePass's own window with the plugin** (the Live tier, when the desktop is free): a real
   File then Synchronize and File then Import with the plugin reading in between; a first rule
   written into the entry dialog's public `EntryStrings`, then OK, Cancel and Escape; a rule
   change applied at `FormClosed`; Add Entry with "save after modifying an entry" on; Duplicate
   Entry, Duplicate Group, a copying drag and paste; Delete Duplicate Entries after each; Empty
   Recycle Bin and Database Maintenance's clearing of deletion records; a rule write while a
   save is running.
8. **KeePassXC's window**: its merge from the menu, its merge on reloading a changed file, a
   KeeShare import (the one path that applies deletions), and Clone of an entry and of a group.
9. **How often the costly cases occur in use with one desktop and phones**: copies of narrowed nodes, a
   first grant on an entry that is edited on the phone before the next merge.

## 7. Questions that are the project's to decide

1. **The whole-file overwrite.** KeePassDroid writes its open copy over a changed file at every
   save, and nothing inside the file survives that. Is an operating rule acceptable (a program
   merges before it saves, or saves only a copy it opened after KeePass's last
   save), or must the store survive it? The second answer means a store outside the file and a
   reversal of "no sidecar". A third answer lies between: a memory on the gate's machine that
   can only refuse (revoked certificates, retired authorities, the newest record written). Is
   that a sidecar in the sense of the settled decision?
2. **Copies of narrowed nodes.** Must a copy of a sealed group or a narrowed entry stay narrowed
   in every program? If yes, the plugin puts a field on every ruled entry and a link entry into
   every ruled group. If a copy may be governed by where it lands, nothing of the plugin's sits
   in the user's groups.
3. **Visible plugin data.** Are a `KeePassGate` group, a rules group, a `KPG.` field on ruled
   entries and a link entry in ruled groups acceptable on the phone and in KeePassXC, given that
   deleting them there closes access until it is settled in KeePass?
4. **The authority's private key.** In an entry it survives every merge and is one reveal, print
   or export away, and KeePassDroid shows a protected string in clear [R]. In database custom
   data it is out of sight, and a KeePassXC merge of an old
   copy can remove it, after which every client pairs again unless another copy still holds it.
   Which? And if in an entry: as a protected string, or as an attachment once measured?
5. **A second witness in database custom data** for revocations and retirements. It makes one
   hand deletion harmless, keeps the file at KDBX 4.1 while such a key exists, and keeps the
   KeeWeb defect as today. Keep the keys, write them without a time through KeePassLib's
   internal setter (one more pinned private member), or drop them?
6. **Databases made with 0.1.0.** Carry them over by a press on the KPG tab, or let them read as
   having no authority (the O7 precedent)? Either way a node that still carries the old rule key
   must refuse and not inherit; is that check allowed under the rule against reading an older
   shape?
7. **Unasked writes.** May the plugin write a repair or a pin for a copy inside the operator's
   next save, as owed answers ride a save today (O4), or only on an explicit press?
8. **Two decisions made apart.** Until the operator settles them: two rule versions give what
   both allow, and a profile removed in one copy stays off although another copy added it back.
   Acceptable?
9. **How a refusal is announced.** A node that refuses closes its subtree until settled on its
   KPG tab. Is `503 undecided` reused for store-wide damage, or is a new word wanted?
10. **Which programs are listed as compatible.** KeePassXC's merge, KeePassDX and its releases,
    KeeWeb: which of them are to be listed as compatible and measured further? That sizes every
    residue above, and KeeWeb merges may
    be simplest to name unsupported.
11. **A fourth page on the KPG tab**, for rules that refuse, copies and other stores' data,
    within the tab's fixed room.

## Sources

- Ground: `ground-inventory.md`, `ground-measured.md`, `ground-keepass.md`,
  `ground-keepassxc.md`, `ground-android.md`, `ground-others.md`.
- Designs: `option-store-entries.md`, `option-versioned-fields.md`, `option-wildcard.md`,
  `option-single-entry.md`, `option-rules-on-node.md`, `option-rules-marker-entry.md`,
  `option-rules-central.md`, `option-rules-linked.md`, `option-outside-file.md`,
  `option-hardened-custom-data.md`.
- Critiques: `critique-<option>-survival.md` and `critique-<option>-security-and-fit.md` for
  each (for `single-entry` see "State of the inputs").
- Interim Android results:
  `spikes\storage\android\work\runs\`
  (`k2a\merge-CP-r1` to `merge-PC-r3`, `k2a\save-*`, `k2a\edit-*`, `kpd\save-*`, `kpd\edit-e1`),
  each with a `.diff.txt` against its base under `spikes\storage\android\work\base\`.
