# Critique of `rules-marker-entry`: survival across programs

Written 2026-11-15 against `option-rules-marker-entry.md` (master `a42e38aa`, KeePass 2.62). Lens:
survival. I walked every save and merge in the ground reports through the design, datum by datum,
looking for a sequence that loses, rolls back, duplicates or resurrects something. Data 1 to 6 are
outside this design (it assumes the `store-entries` option for them). What is walked here is datum
7 as the design stores it, rule entries in the user's groups, and the two records it adds to the
KPG group, `ruled` and `replaced`. The design does not use datum 8, and I found no sequence that
needs it. Nothing in this design is a revocation, so "a revocation undone" does not arise here; the
equivalent for rules is a narrowing undone, which is what I looked for.

## How statements are marked

- **[M here]**: run for this critique, from a console. Probe, scripts and raw output are under
  `spikes\storage\probes\rules-marker-entry\survival\`:
  `probe\Program.cs` (the design's own write path `Save` and read path `View`, `Meet`, `Reader`,
  copied unchanged from the design's probe, plus my scenarios), `scripts\Run.ps1`,
  `scripts\pk_delete.py`, and the outputs `out\s1-s3.txt`, `out\s5-s9.txt`, `out\s8.txt`,
  `out\run.txt`. Section 5 lists the runs.
- **[M design]**: the design's own recorded runs (`spikes\storage\probes\rules-marker-entry\design\out\check.txt`
  and its text). I did not re-run them.
- **[M ground: name]**: measured in that ground report.
- **[R file:line]**: read from source. KeePass 2.62 paths are relative to the archive the KeePass
  ground report extracted (`spikes\storage\probes\ground-keepass\src`); KeePassXC at tag 2.8.0
  (`spikes\storage\probes\ground-keepassxc\src\v2.8.0`); Keepass2Android `v1.15-r6` and KeePassDX `4.5.8` as the
  Android ground report cloned them.
- **[A]**: assumed. Neither run nor read.
- **"simulated"**: a behavior another program was read to have, reproduced by my probe over
  KeePassLib. It is a measurement of the design's fold, not of that program.

"Wider" below means what the design's own checker means (`check.py`): more than the last rule the
operator set at the node, or more than the narrower of two rules that never saw each other.

## 1. The answer first

**Viable with changes.** The core holds against programs. Rule entries are only ever added, never
changed, and checked against an identifier derived from their content; the two add-only records
and the fold that takes the narrower of several rule entries mean that no save or merge by another
program, measured or read, widens a rule on its own. My runs reproduce the design's mechanics:
a copy under a new identifier closes its group, a lost rule entry refuses by its `ruled` record,
two concurrent rule entries meet to the narrower, and keepassxc-cli's merge, which applies no
deletion, brings back what a person deleted.

What breaks it is three sequences, each **serious**, each fixable with a change I name:

1. **The derived identifier comes back.** `KPG.After` names only rule entries that count, so after
   a node's rule entry is lost, or for a node's first rule, a rule with a text the node had before
   gets that old identifier again. Measured: a later narrowing made in a second copy is swallowed
   by every merge tried, ten results out of ten wider (P1a); the tab's own "Save putting the shown
   rule back" leaves the node refusing (P1b); and one database can hold two entries with one
   identifier, which KeePass's next save repairs with a warning dialog and, in one tree order, by
   closing the group (P1c).
2. **Two concurrent rule entries, and one deleted by hand.** The design says deleting a rule entry
   "is never a widening". Measured: after a merge leaves two rule entries in force, deleting the
   narrower one, which looks like a duplicate or an orphan, widens the node (P2).
3. **An import with new identifiers, then the tab's Remove.** The import closes the copies, as
   designed; pressing Remove on each copy, which the tab offers, opens every duplicate to the
   root's grant, where today's store keeps the rule on the duplicate (P3).

The rest is minor, most of it a claim in the design that is stated more strongly than its basis.
Nothing is fatal: the one loss no change can address, a whole file written over another, the
design already names (its section 11) and it affects every store inside the file.

## 2. The problems

Each is a sequence, its outcome, and what the gate then does. Rated fatal, serious or minor as the
task defines them.

### P1 (serious). The identifier is derived from content, so a later decision can be born dead

**The mechanism.** A rule entry's identifier is the first 16 bytes of SHA-256 over `KPG.Node`,
`KPG.After` and `KPG.Rule` (design 2.1). `KPG.After` names "every rule entry of the node that counts
and everything those name" (design 4.1 step 3; the design probe's `Save`, `Program.cs:180-188`). When
nothing counts at the moment of saving, because it is the node's first rule or because its rule
entry has been moved to the bin, deleted or dropped, `KPG.After` is `none`. A rule whose text equals
the node's first rule then has the first rule's identifier, which may already be replaced, deleted
or sitting in the bin. The design's sentence "An identifier never comes back: a later rule entry of
the same node always names more predecessors" (2.1) holds only while a rule entry counts.

**P1a. A later narrowing in a second copy is swallowed. Widening.** [M here, `out\run.txt`, s4]

1. Base: group N with no rule, under a root that grants the profile `reader` read and list.
2. Copy A (KeePass with the plugin), at +1 h: N narrowed to `reader` read. Rule entry `667B...`,
   `KPG.After` `none`.
3. Copy A, at +2 h: N changed to `other` (`reader` read and list). Rule entry `B0D3...`, naming
   `667B`; a `replaced` record and a deletion record for `667B`.
4. Copy B (another KeePass with the plugin, from the base), at +3 h: N narrowed to `reader` read.
   Its `KPG.After` is `none`, so its identifier is `667B`, the entry A replaced.
5. Merged: KeePassLib Synchronize, OverwriteIfNewer and KeepExisting, both directions; keepassxc-cli
   2.4.2 and 2.8.0 `merge -s`, both directions.

In all ten results N grants `reader` read and list. B's narrowing, the last decision at N and one
A's widening never saw, arrives dead: the `replaced` record and `B0D3`'s `KPG.After` name it. By the
design's checker N is bounded at the narrower of the two, read: every result is wider. (In the
Synchronize results the deletion record for `667B` is gone and the entry stayed, because B's copy
is newer than the record; KeePass keeps an object that is not older than its deletion record and
drops the record, `KeePassLib\PwDatabase.cs:1083-1096` [R]. The entry is dead by the records.)

**P1b. The tab's own repair leaves the node refusing. Wrongly refused.** [M here, `out\s1-s3.txt`]

- s2: N's first rule is narrow. A person deletes its rule entry for good, leaving a deletion record;
  N refuses, as designed. The operator opens N's KPG tab and saves narrow again, which is what
  "Save putting the shown rule back" (design 4.3) offers. The new entry has the deleted entry's
  identifier and is dead by its deletion record: after OK, N still refuses. Saving a different text
  works.
- s3: N narrow, then `other` (replacing narrow). A person moves the rule entry in force to the bin.
  The operator saves narrow again: the identifier is the first, replaced one, dead by a `replaced`
  record that is never pruned. N refuses after OK. Only another text, saved first, gets narrow back.

**P1c. Two entries with one identifier in one database.** [M here, s1; R for KeePass's window]

- s1: N's first rule is narrow; a person moves its rule entry to the bin; N refuses. The operator
  saves narrow again: the new entry in N has the identifier of the one in the bin. KeePassLib saves
  and reopens that file, and the fold reads N as narrow.
- Every save KeePass makes through its window, which includes every save the plugin's writer asks
  for (`MainForm.SaveDatabase` calls `PreSavingEx`, `KeePass\Forms\MainForm.cs:759`), runs
  `FixDuplicateUuids` (`KeePass\Forms\MainForm_Functions.cs:3697-3701`). That shows a warning naming
  the database as corrupted by another program, with one Repair button, and then gives every later
  occurrence of an identifier a new one in pre-order, a group's entries before its subgroups
  (`MainForm_Functions.cs:6548-6574`; `KeePassLib\PwDatabase.cs:1881-1917`;
  `KeePassLib\PwGroup.cs:641-663`) [R].
- Measured with that repair called directly: when N comes before the bin among the root's groups,
  the copy in the bin is renamed and N keeps its rule. When the bin comes first in the tree's order,
  as it does for any group added at the root after the database's first deletion, N's new entry is
  renamed, no longer matches its content, and **closes N**.
- Emptying the bin before the next save writes a deletion record for the shared identifier, and
  N's new entry is dead: N refuses [M here].
- The warning is modal and comes up inside whatever save is next, a pairing's included; the writer
  waits out a call that has started (CLAUDE.md, "This used to claim a floor for both") [R], so that
  save waits for whoever answers the dialog [A, the combination not run].
- In passing: keepassxc-cli 2.4.2 and 2.8.0 collapse two same-identifier entries into one, placed
  where the later one was, so N refused after a KeePassXC save in one order and read in the other
  [M here; R `src\format\KdbxXmlReader.cpp:800-811`, `:1202-1217`]. Keepass2Android and KeePassDX
  refuse such a file by default [R `Kp2aBusinessLogic\database\Database.cs:259-266`,
  `keepass2android-app\app\App.cs:1103-1108`; KeePassDX `DatabaseVersioned.kt:215-229`]. Both are
  moot while only KeePass writes the file, since its save repairs first.

**Fix.** Put a value drawn at random into the derivation, as a `KPG.Nonce` string that the
identifier covers, new at every save. The same rule made in two copies is then two entries, which
the fold already meets to the same document; G04 and G05 lose their single entry and nothing else
changes. As a second line, the write step refuses to add an entry whose identifier is present
anywhere in the tree or named by a deletion record or a `replaced` record. [A: by the fold; not
run.]

### P2 (serious). Two concurrent rule entries: deleting one of the two by hand widens to the other

The design's own concurrency cases (G02, G03, G14, G15) end with two rule entries that both count,
met to the narrower. Nothing records that both must stay, and the design says (11): "A person
tidying up can close a group to clients by deleting or editing the rule entry. That is never a
widening."

- s5a [M here, `out\s5-s9.txt`]: N wide. A narrows it (+1 h), B sets `other` (+2 h). KeePassLib
  Synchronize: two entries titled `KeePassGate rule (this group)` in N, met to `reader` read. A
  person moves the narrow one to the bin, or deletes it for good: N grants read and list.
- s5b [M here]: the shape of G14. Entry E in G has its own rule, wide. A narrows E's rule (rule
  entry in G); B moves E to H and sets `other` there (rule entry in H). Synchronize either direction:
  met to read. A person deletes the rule entry left in G, which names an entry that is no longer in
  G and looks orphaned: E grants read and list.

**A three-way chain**: KeePass synchronizes the two copies (both rule entries arrive [M design,
M here]); the file goes to a phone; Keepass2Android and KeePassDX list the rule entries among the
user's entries and find them by search [R ground android 4.2]; a person deletes one of the two
look-alikes in Keepass2Android, which moves it to the bin and updates its location time [R ground
android 4.2]; KeePass synchronizes the phone's file back and takes the newer location [M ground
keepass 2.2, "E recycled on A" rows], so the narrower one is in the bin in KeePass too. The gate
grants the wider rule [fold]. KeePassDX also moves it to the bin, touching its modification time;
whether it updates the location time was not read, so the chain through KeePassDX is open.

The same widening follows when one copy's rule entry was moved to the bin by hand and the other
copy held a different one [fold: the binned entry is neither counted nor named].

**Fix.** An `issued` record per rule entry, written with it in the KPG group and only ever added.
The fold refuses a node for which an issued rule entry that no `replaced` record names does not
count (in the bin, deleted, or absent). The write step replaces every issued, unreplaced rule entry
of the node, wherever it is and whether or not it is still there, and names all of them in
`KPG.After`. A hand deletion of either rule entry then refuses, and the operator's next save on
the tab resolves it. The record also does the `ruled` record's job. [A: not run.]

### P3 (serious). An import with new identifiers, then the tab's Remove, drops the duplicates' rules

s7 [M here]: the root grants `reader` read and list. Group O has no rule and holds E, sealed by its
own rule (rule entry in O), and F. Group S is sealed. The operator imports an older copy of the
same database with File then Import, whose preselected method is Create new IDs [R ground keepass
2.4].

- After the import: the root, the duplicate O and the duplicate S are closed, as the design says.
  The open copy's own entry F refuses too, since it inherits from the closed root: one import
  closes the whole gate.
- The tab offers Remove on each entry that claims to be a rule and is not one (design 4.3). After
  Remove on the copies: the duplicate E and the duplicate s1 grant read and list, the root's grant.
  Their originals stay sealed.

Why: the copies' `KPG.Node`, and the copied `ruled` records, name the original nodes, so the
duplicates are never ruled and inherit. Today's store copies a node's custom data into its
duplicate (`AssignProperties` at `KeePassLib\PwDatabase.cs:811-813` and `:860-862`, which copies
custom data at `KeePassLib\PwGroup.cs:544` and `KeePassLib\PwEntry.cs:528` [R]), so a
duplicate keeps its rule; on this path the design is weaker than today once the operator follows
the tab. Duplicate Group in KeePass gives the same result through the same button. The design's
section 11 names "closes without saving or removes the copied rule entry from the root" as the
remedy without saying that the second one widens every duplicate below.

**Fix.** Offer Adopt in place of Remove for a copy of a group's rule entry: write a bound rule
entry for the group the copy sits in, with the copy's document, and its record. Keep a group closed
while it holds a copy of an entry's rule entry, until every entry in that group has a rule of its
own or the operator seals the group. Never offer a plain Remove for a copy whose `KPG.Node` names a
node of this database. [A: not run.]

### P4 (minor, understated). Two hand deletions, not three, reach silence

The design (7, 11) says silence needs three deliberate acts: the rule entry deleted for good, its
`ruled` record deleted for good, and the deleted objects information removed. The second line of
defense is the deletion record, and deletion records do not travel everywhere.

s6 [M here, `out\run.txt`]: N narrow under a wide root. N's rule entry and its `ruled` record are
deleted.

| How they are deleted, and what follows | N grants `reader` |
| --- | --- |
| in KeePassLib, each with a deletion record | refused (by the record) |
| with pykeepass 4.2.0, which records no deletion [R ground others] | read and list |
| as above in KeePassLib, then merged into an intact copy the way KeePassDX 4.5.8 merges: arriving deletion records applied, none kept [R `DatabaseKDBXMerger.kt:304-323`; simulated] | read and list |
| as above in KeePassLib, then that copy merged into an intact one by keepassxc-cli 2.4.2 or 2.8.0 | read (both come back) |

So two deletions suffice, in a program that records none, or followed by one ordinary KeePassDX
merge that becomes the file KeePass opens next. Fix: say so, and let every record that names a
node witness it (`replaced` records naming their node, and P2's `issued` records), so that silence
needs every record ever written about the node gone. [A]

### P5 (minor). The root's rule entry is one entry away from closing the gate

s8 [M here, `out\s8.txt`]: the root grants read and list; group P is narrow, group Q has no rule. A
person moves the entry at the root, `KeePassGate rule (this group)`, to the bin. The root, Q and
Q's entry refuse; P keeps its own rule. For every profile, every node whose walk reaches the root
refuses. The entry sits at the top of the user's root in every program, and an import closes the
root as well (P3). The design says a person "can close a group to clients"; for this entry it is
the whole tree under the root. Wrongly refused. Fix: name it in the docs and on the tab's count of
refusing nodes, and put the root's state in the plugin's window under Tools, since it is the one
refusal that takes everything. [A]

### P6 (minor). The fold reads the bin pointer, which merges by a time

The design (1.5) says nothing in the fold reads a modification time. A rule entry counts only
outside the bin, and which group is the bin is `RecycleBinUuid`: KeePass takes it from the copy
with the later `RecycleBinChanged` (`KeePassLib\PwDatabase.cs:1576-1588` [R]), Keepass2Android and
KeePassDX by the same kind of change time [R ground android 3.3], and KeePassXC and keepass-rs do
not merge it at all [M ground keepassxc; R ground others].

s9 [M here]: copy A moves N's rule entry to the bin by hand at +1 h, making bin X: N refuses. Copy B
moves a user entry to the bin at +2 h, making bin Y. KeePassLib Synchronize, either direction: X is
an ordinary group, N's rule entry counts again, N grants read. Not wider than the last decision,
but a hand deletion is undone by which copy's clock stamped its bin later, and two copies holding
the same tree can answer differently. The whole old bin also becomes reachable to clients, which is
`DESIGN.md` §138's concern and not this design's. Fix: correct the claim; with P2's fix the binned
entry refuses wherever the bin pointer goes.

### P7 (minor). A KeePassDX merge moves every rule entry to where the arriving copy has it

Rule entries never change, so every copy holds them at the same modification time, and KeePassDX
4.5.8 moves an entry to the arriving copy's group whenever the two times are equal
(`DatabaseKDBXMerger.kt:519-524` [R ground android 3.1, 5.4]). Every KeePassDX merge therefore
relocates every shared rule entry: a stale arriving copy undoes the tab's Put back and brings a
hand-binned rule entry back out; an arriving copy in which a rule entry was binned bins it in the
open copy. By the fold the outcome is the last rule or a refusal, never wider (with P2's fix, a
refusal or the last rule) [R; not measured].

### P8 (minor). An entry's rule entry left in a group the entry has left

The design keeps a moved entry's rule entry in the old group (2.2). When that group is moved to the
bin, the rule entry goes with it and the moved entry refuses (by the fold: in the bin it does not
count, and the `ruled` record stands). A KeePassDX 3.2.0 merge of a deletion record for that group
removes it with its whole subtree (`app\...\DatabaseKDBXMerger.kt:286-290` [R ground android 3.1]),
with the same result. Wrongly refused, until the operator saves the entry's rule again. Fix: show
it on the entry's tab, which the design's "Put back" state already covers, and say in the docs that
deleting a group can close entries that once lived in it.

### P9 (minor). Other ordinary operations that close a group

- KeePass's entry templates: if the templates group is a ruled group, its rule entry is offered as
  a template, and an entry made from it carries the four `KPG` strings under a new identifier. It
  closes the group it is made in, and the API guard (design 8.1) hides it, with the user's own
  password in it [R ground keepass 9; fold].
- XML Replace over string values rewrites `KPG.Rule` in every rule entry it matches, and each
  closes its group [R ground keepass 9].

All fail closed.

### P10 (note). A sentence in the design's section 6

"Replaced rule entries are removed in both copies by their deletion records" (KeePass Synchronize
row) is not true when the other copy's identical entry is newer than the deletion record: s4 kept
`667B` and dropped its record [M here]. Harmless, since the records keep it dead, but the tidying
of 4.2 has to expect to meet such entries after every Synchronize.

## 3. The walk, program by program

What each save and merge does to the design's three kinds of entry, and the gate's answer. "Union"
means an entry only one copy holds arrives. "As the fold" means the outcome follows from the
design's read path and the program's behavior as marked, with nothing new found beyond section 2.

| Program, operation | Rule entries | `ruled`, `replaced` | Deletion records | Bin pointer | Gate | Basis |
| --- | --- | --- | --- | --- | --- | --- |
| KeePass 2.62, save | kept; duplicates repaired with a warning (P1c) | kept | kept | kept | the rule | M design; R for the repair |
| KeePass, Synchronize | union; a replaced entry's twin newer than its deletion record survives (P10) | union | joined; a record dropped when its object is not older | the later change time | the rule, or the narrower; P1a widens; P6 revives a binned entry | M design, M here |
| KeePass, OverwriteIfNewer, OverwriteExisting, KeepExisting | union | union | own applied, none copied | by method | as Synchronize; P1a widens in the two I ran | M design, M here |
| KeePass, Create new IDs | copies under new identifiers close their groups and the root | copies count by what they name | | | refuses; after the tab's Remove, duplicates widen (P3) | M design, M here |
| KeePass, Empty Recycle Bin | removed with records; with P1c, kills the live twin | | added | | refusal | M here |
| KeePass, Database Maintenance, remove deleted objects information | | | all gone | | witnesses that were only records go (P4) | R ground keepass 5 |
| KeePassXC 2.4.2 to 2.8.0, save | kept, identical | kept | kept | kept | the rule | M design |
| KeePassXC, merge | union, nothing removed | union | none applied or copied | not merged | the rule, the narrower, or a person's deletion undone; P1a widens | M design, M here |
| Keepass2Android, save | kept | kept | kept | kept | the rule | R ground android 2; KDBX 3.1 output A |
| Keepass2Android, merge | union; an entry whose group is missing goes to the root | union | joined | by change time, checked | as KeePass Synchronize | R ground android 1.5, 3.1, 3.3 |
| KeePassDX 4.5.8, save | kept | kept | kept | kept | the rule | R ground android 2 |
| KeePassDX 4.5.8, merge | union; every shared one moved to the arriving copy's group (P7) | union | arriving applied, none kept (P4) | by change time | the rule or refusal; with P4's deletions, silence | R ground android 1.5, 3.1, 5.4 |
| KeePassDX 3.2.0, 3.3.0, save | kept | kept | kept | kept | the rule | R ground android 2 (3.2.0); 3.3.0 not read |
| KeePassDX 3.2.0, merge | dropped if its group is missing; a deleted group removed with its subtree (P8) | as rule entries | arriving applied | | refusal where an entry is dropped | R ground android 1.5, 3.1 |
| KeePassDroid 2.7, save | kept | kept | kept | kept | the rule | R ground android 2 |
| kdbxweb 2.1.1, save and merge | kept; union; anything a deletion record names removed, whatever its time | union | joined | by change time | the rule; with P1, a born-dead entry stays dead | M ground others for strings and union; R for deletions |
| pykeepass 4.2.0 | kept; a deletion leaves no record (P4); an edit moves no time | | none written | | silence after two deletions | M here, R ground others |
| Strongbox, KeePassKit, keepass-rs | union | union | Strongbox and KeePassKit joined; keepass-rs metadata not merged | keepass-rs not merged | as the fold | R ground others |
| KeePassium, gokeepasslib | kept; no merge | | | | the rule | R ground others |
| A whole file written over another | one copy whole | | | | that copy's rules (design 11) | R ground android 5.5, others |

**Clocks that disagree.** Apart from the bin pointer (P6), a clock decides only whether KeePass
applies a deletion record to an arriving object. A replaced entry that survives because its twin
is newer is dead by its records; a hand-deleted rule entry that survives counts again, which is the
last rule. Neither widens [M here, s4; fold].

**History pruned.** Rule entries carry no history except one item holding the same content when the
same rule was made in two copies at different times (design G04), and nothing reads history. P1's
fix removes even that.

## 4. Claims of survival and their basis

Claims in the design that rest on reading or assumption, or that this critique found wrong. Each
needs a measurement, or a change, before the design is chosen.

**Found wrong by measurement here:**

1. "An identifier never comes back" (2.1): s1 to s4.
2. Deleting or editing a rule entry "is never a widening" (11): s5a, s5b.
3. Silence needs "three deliberate deletions" (7, 11): two, with pykeepass (measured) or before a
   KeePassDX merge (read, simulated).
4. "Nothing in the fold reads a modification time ... or a group's own fields" (1.5): the bin
   pointer is decided by `RecycleBinChanged` (s9).
5. "Replaced rule entries are removed in both copies by their deletion records" under Synchronize
   (6): not when the twin is newer (s4).
6. The 560 verdicts with none wider (1, 14) hold for the design's 16 scenarios; they include no
   repeated rule text, no hand deletion of one of two concurrent rule entries, no remedy after an
   import, and no deletion record lost.

**Resting on reading, to be measured** (in the order I would measure them):

7. Every Android cell of section 6: custom strings, the auto-type switch, a `KPG.After` of 4,223
   characters and the records through saves by Keepass2Android, KeePassDX 3.2.0, 3.3.0 and 4.5.8 and
   KeePassDroid; the merges of Keepass2Android and KeePassDX in both directions, including where
   Keepass2Android puts an entry whose group is missing, KeePassDX's equal-time move (P7) and
   KeePassDX 3.2.0 dropping an entry or removing a subtree (P8). KeePassDX 3.3.0 was not read at
   all.
8. That Keepass2Android's KDBX 3.1 output carries entries and custom strings intact (design 9 marks
   it [A]).
9. KeePass's own window: `PreSavingEx` repairing duplicate identifiers inside a save the plugin's
   writer asked for (P1c); a real File then Synchronize and File then Import; Empty Recycle Bin,
   Delete Duplicate Entries, Duplicate Group and the templates path (P9) through the window.
10. kdbxweb's removal of whatever a deletion record names, and Strongbox's, KeePassKit's and
    keepass-rs's merges, for this shape (read only).
11. KeePassXC's window merge, its merge on reloading a changed file, and a KeeShare import, which
    applies deletions [R ground keepassxc 3].
12. The narrower-of-several document against the real resolver, with several profiles, `default`,
    switches and inheritance (design 13.8). My runs, like the design's, use one profile.
13. That each fix named here (a nonce, `issued` records, Adopt) does what the fold says it does. None
    was run.

## 5. What was run for this critique

A console probe over KeePassLib 2.62 loaded from `.keepass\2.62\KeePass.exe`,
built with `dotnet build` as `Rms.exe`, made every database itself with invented content (Argon2d,
2 iterations, 1 MiB; password `probe-only`). keepassxc-cli 2.4.2 and 2.8.0 were the copies the
KeePassXC ground report downloaded; I checked their archives again against the spike's
`keepassxc.sha256` and both executables' Authenticode signatures (valid, DroidMonkey Apps, LLC).
pykeepass 4.2.0 ran through `uv run --no-project --with pykeepass python -I -X utf8`. No window was
opened, no KeePass, KeePassXC or Android program with a window was started, and no test tier or
`build.ps1` was run.

| Run | What | Output |
| --- | --- | --- |
| s1 | first rule, rule entry to the bin, the same rule saved again; both orders of N and the bin; KeePassLib save and reopen; `FixDuplicateUuids` by reflection; Empty Recycle Bin; then keepassxc-cli 2.4.2 and 2.8.0 `ls` and `add` over the two files | `out\s1-s3.txt`, `out\run.txt` |
| s2 | first rule deleted for good, saved again; then another text | `out\s1-s3.txt` |
| s3 | narrow, then other, the newer binned, narrow saved again | `out\s1-s3.txt` |
| s4 | the same first rule text decided later in another copy; KeePassLib Synchronize, OverwriteIfNewer, KeepExisting and keepassxc-cli 2.4.2 and 2.8.0, both directions | `out\run.txt` |
| s5 | two concurrent rule entries after KeePassLib Synchronize, then one binned or deleted by hand; the G14 shape, both directions | `out\s5-s9.txt` |
| s6 | the rule entry and the `ruled` record deleted: in KeePassLib, with pykeepass, then a KeePassDX-style merge (simulated) and keepassxc-cli 2.4.2 and 2.8.0 merges | `out\run.txt` |
| s7 | Create new IDs import of an older copy, then Remove on every copy | `out\s5-s9.txt` |
| s8 | the root's rule entry moved to the bin under a granting root | `out\s8.txt` |
| s9 | a rule entry binned in one copy, a later bin made in the other, Synchronize both ways | `out\s5-s9.txt` |
