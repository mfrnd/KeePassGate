# Critique of `outside-file`: survival across programs

Written 2026-11-15 against `option-outside-file.md` as it stands, KeePassGate master `a42e38aa`,
KeePass 2.62. The lens is survival: every save and merge in the ground reports, walked through the
design datum by datum, looking for a sequence that loses, rolls back, duplicates or resurrects
something, and for what the gate then does. The design's own list of what it does not solve
(its section 18) is repeated only where it understates.

## How statements are marked

`...` below is
`spikes\storage\probes`.

- **[M here]**: run for this critique, from a console, over databases and stores made with invented
  content. Scripts under `spikes\storage\probes\outside-file\survival\scripts\`, outputs under
  `spikes\storage\probes\outside-file\survival\out\`. Section 6 describes the runs.
- **[M here, model]**: the store's join and checks executed as a model: the design's own prototype
  (`spikes\storage\probes\outside-file\design\probe\Store.cs`) brought to the design's text in three places (section 6),
  plus letting go at expiry, which the prototype lacks and which I wrote from the design's section
  9.6. What such a line shows is what the design's text does when executed. The synchronizer in it
  follows the conflict rule Syncthing documents [R others]; no real synchronizer was run.
- **[M design]**: the design's own probe output, `spikes\storage\probes\outside-file\design\out\kpmerge.txt`, `xc.txt`,
  `xc2.txt`, `scenarios.txt`, which I read and checked against the design's text.
- **[M spike]**, **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M inventory]**: measured in
  `spikes/revocation-merge` or in that ground report.
- **[R ...]**: read from source, file and line. **[R 2.62]** is `KeePass-2.62-Source.zip` as the
  `keepass` ground report extracted it under `spikes\storage\probes\ground-keepass\src`, read here. **[R design]** is the
  option document, by section. **[R plugin]** is the review4 worktree at `a42e38aa`.
- **[A]**: assumed. Neither run nor read.

## 1. Verdict

**Viable with changes.** The move does what it claims for the file. No program that opens a `.kdbx`
reaches the retirements, records, revocations, standing answers, profile names or rules any more,
so none of the failures the spike measured reaches them (section 2). What other programs can still
do to the two keys left in the file closes the gate rather than widening it, and a KeePass
synchronize with any copy that holds the keys opens it again [M design].

What fails is the store's own machinery and the split between the file, which carries the tree,
and the store, which carries the rules. All five serious problems run toward access, and none of
them needs a KDBX program to misbehave:

1. **P1, serious.** "No file ever has two writers" rests on a writer name kept in KeePass's
   configuration, which a portable KeePass, a roaming profile or a cloned computer carries to a
   second computer. Two computers then write one file, a synchronizer keeps one version and
   renames the other, and the plugin reads only the one it kept. A revocation made on the losing
   computer is gone from the winner and from every new computer, and the Store page's way out
   for the loser serves the same store [M here, model].
2. **P2, serious.** Letting a revocation go at expiry reads the local clock, while the issuer's
   record line stays in the issuer's file until the issuer writes again. A revoking computer whose
   clock runs ahead lets go of its revocation and its emptied record, and the issuer's line is a
   record again on every computer whose clock is right [M here, model].
3. **P3, serious.** The Store page removes rules for nodes that no longer exist. Every merge that
   brings a deleted node back (KeePassXC, which never applies a deletion; KeePass when the other
   copy changed the node later; pykeepass, which records no deletion; a restored or overwritten
   file) then brings it back without its narrowing. Today the rule comes back with the node.
4. **P4, serious.** Create after a lost key "carries" the rules and names of an older, closed
   store, and Recreate is the offered way out of states whose image did not pass its checks. Both
   write an older rule set and an older name list as fresh lines that no check can compare again.
5. **P5, serious, understated by the design.** A copy of a node no longer carries its rule, and the
   layouts `docs/access-control.md` teaches (a seal or an empty mask under a wider grant) are the
   ones a copy escapes. Today KeePass's own Duplicate Entry copies the rule with the entry.

Seven minor problems follow, P6 to P12. One of them is a new measurement: **KeePassXC 2.7.12 and
2.8.0 put two keys of their own into every file they save, even one with no custom data, and a
merge from such a copy removed both of the design's keys, 2 of 2 releases** [M here]. The design's
window ("a stale copy that ... holds any other key") is therefore every pre-Create or pre-Recreate
copy a current KeePassXC has ever saved. The outcome is still closed.

## 2. What survived the attack

| Claim | Result | Basis |
| --- | --- | --- |
| No KDBX program reaches data 2 to 6, 7's text, or 8 | held for every program in the ground reports: none of them names, opens or lists a sibling folder in what was read | [R design 13]; [A] that none does in what was not read |
| The two file keys survive every save | held: KeePass, KeePassXC 2.4.2 to 2.8.0, the Android programs, kdbxweb (ours are never empty), pykeepass; others read | [M design xc.txt]; [M spike]; [M others]; [R others] |
| The two file keys survive every KeePass merge | held: 38 of 40 cells, the other 2 being Keep existing into a copy that never had them | [M design kpmerge.txt] |
| The two keys survive Keepass2Android and KeePassDX merges | held for keys of the same shape in the spike; these two keys not run | [M spike]; the design's 21.2 |
| A KeePassXC removal closes rather than widens | held when the copy merged in predates the first Create: no key, a folder no key opens, `503 undecided` | [M design xc2.txt]; [M here]; [R design 11] |
| A KeePass synchronize restores the keys after such a removal | held, Keep existing excepted | [M design kpmerge.txt, "into old from anchored"] |
| History pruned, entries restored from history, ties, rewritten or dropped times | decide nothing for KPG data any more: no datum rides on an entry, a node time or a per-key time | [R design 7, 9.5]; follows from [M keepass 1a], [M keepassxc 5] |
| Node custom data deleted (KeePassDroid, KeePassDX 3.2, 3.3) | deletes nothing of the plugin's | [M spike]; [R design 4] |
| Whole-file overwrite of the `.kdbx` (Keepass2Android "No, overwrite", KeePassDX within 10 s, every KeePassDroid save over a changed file, a synchronizer keeping one file) | a pairing, revocation or rule made in between is no longer lost with the losing file | [R design 13]; [R android 5.5]; [M spike] |
| Two versions of one rule written without knowledge of each other | refuse until settled, on every computer | [M design scenarios.txt, 3] |
| A revocation one installation asserts cannot be taken away by another | held | [M design scenarios.txt, 2] |
| A writer's file put back older, deleted, changed, renamed, under another key | closed wherever the seen file or another file's `saw` knows better | [M design scenarios.txt, 6 to 8] |

## 3. The walk, program by program

"Keys" means `KPG.Authority.<fp>` and `KPG.Store.<id>`, the only things left in the file. "Tree"
means what the file says about which nodes exist and where, which a rule keyed by node UUID is
applied to.

| Program, operation | Keys | Store | Tree, as rules see it | Gate |
| --- | --- | --- | --- | --- |
| KeePass 2.62, Synchronize, If newer, Overwrite, Keep, New UUIDs, both directions | never removed or changed [M design] | untouched [A] | a node deleted on one side returns when the other changed it later [M keepass X3, X4]; New UUIDs duplicates the arriving tree at the root with new UUIDs [M keepass X1] | serves; P3, P5 |
| KeePass Duplicate Entry, Duplicate Group, copy and paste | untouched | untouched | new UUIDs; today the rule travels, since `Duplicate` is `CloneDeep` and `CloneDeep` copies custom data [R 2.62 `KeePassLib/PwEntry.cs:856-864`, `:353`; `KeePassLib/PwGroup.cs:1437-1446`, `:396`; `KeePass/Forms/MainForm.cs:2324`, `:1318`] | P5 |
| KeePass Database Maintenance | a key deleted by hand [R inventory A9] | untouched | "Remove deleted objects information" clears tombstones, so later merges resurrect [R keepass 5] | `503 undecided` once the key is gone; P3 |
| KeePassXC 2.4.2, 2.6.6, save | kept, times dropped, `_LAST_MODIFIED` added when the dictionary has items; **nothing added to an empty dictionary** [M here] | untouched | unchanged | serves |
| KeePassXC 2.4.2, 2.6.6, merge | removed when the copy loaded later holds any key and not these [M design xc2.txt]; **kept** when that copy was only ever saved by these releases [M here] | untouched | deletions never applied: deleted nodes return [M keepassxc 4] | `503 undecided` when removed; P3 |
| KeePassXC 2.7.12, 2.8.0, save | kept, times dropped, **`KPXC_RANDOM_SLUG` and `_LAST_MODIFIED` added even to an empty dictionary** [M here] | untouched | unchanged | serves |
| KeePassXC 2.7.12, 2.8.0, merge | **removed when merged from any copy these releases saved before the first Create** [M here, 2 of 2]; replaced by the old keys from any pre-Recreate copy [follows from M spike's rule] | untouched | as 2.4.2 | `503 undecided`, or the old store where it is not closed on this computer: P6, P9 |
| Keepass2Android save, merge | kept, times lost; a key only one copy holds is added; values equal [M spike, keys of the same shape] | untouched | merge applies deletion records [R android 3.1] | serves |
| Keepass2Android "No, overwrite"; KeePassDX save within 10 s; KeePassDroid save over a changed file | the file is whichever copy was written last [R android 5.5; M spike] | untouched | the overwritten copy's nodes come back or go | serves, or `503 undecided` if that copy predates the first Create; P3 |
| KeePassDX 3.2, 3.3, 4.5.8 save, merge | kept with times; merge by per-key time, removing nothing [M spike; R android 3.3] | untouched | applies arriving deletion records and keeps none; recreates a missing group from the arriving copy [R android 1.5, 3.1] | serves; P3 |
| KeePassDroid 2.7 save | kept [M spike] | untouched | node custom data deleted, nothing of ours | serves |
| kdbxweb 2.1.1 (KeeWeb) save, merge | values kept; with per-key times in a 4.1 file the result is unreadable to KeePass [M others] | untouched | deletes whatever `DeletedObjects` lists, whatever its time [R others] | KeePass cannot open the database at all; P10 |
| Strongbox, KeePassKit | merges remove no database key [R others] | untouched | merges combine deletions [R others] | serves |
| pykeepass, gokeepasslib, keepass-rs, KeePassium | kept [M others for pykeepass; R others] | untouched | pykeepass deletes with no record, so a merge elsewhere resurrects [R others] | serves; P3 |
| A synchronizer over the `.kdbx` | one file wins whole [R others] | untouched | as the overwrite row | as the overwrite row |
| A synchronizer over the folder | not involved | each file carried one way while every file has one writer [A]; **a conflict copy as soon as two computers write one file** [R others, Syncthing and Dropbox] | not involved | P1 |
| A restore of the computer (image, VM snapshot, profile) | the file as backed up | the folder **and the seen file** as backed up | as backed up | served as found: P7 |

Datum by datum, the outcome of the walk:

- **(1) the authority and (1a) the store key** survive every save and every merge but KeePassXC's
  replacement and a whole-file overwrite by a copy that predates them, and both of those close.
  P6 widens the window; P9 is the one sequence in which the replacement reaches an older state.
- **(2) retirements** are now in the store only. They survive every KDBX program, and are lost
  with the store key (P11).
- **(3) records and (4) revocations**: P1 and P2. The issuer's record line behaves as a stale copy
  that is always merged in, which P2 turns into a revocation undone.
- **(5) standing answers**: P1 can lose one; otherwise the more refusing answer wins, which fails
  closed.
- **(6) profile names**: P1, P4. A removal wins a join, so a removed name comes back only when its
  removal line is lost or not carried, and P1 and P4 are both ways for that to happen.
- **(7) rules**: P1, P3, P4, P5, P8. P3 and P5 come from the tree travelling in the file while the
  rule stays in the store; P1, P4 and P8 come from the store alone.
- **(8) recycle bin markers**: the store can hold them and nothing foreign removes them. The bin
  itself and the database's pointer to it travel in the file, so a marker and the bin it names can
  be at different points in time; T049's open question, recognizing a bin a foreign merge made, is
  not changed by where the markers live [R design 7; R keepassxc 2, item 10].

## 4. Problems

### P1, serious: one writer name on two computers, and a synchronizer's conflict copy

**What the design rests on.** Section 1.4: "One file for each installation of the plugin that
writes ... No file ever has two writers, so a file synchronizer never has a conflict to settle".
Section 5.1: a writer is "8 random bytes ... kept as `KPG.Writer` in KeePass's configuration beside
`KPG.Port`". Section 6: loading "reads every `<16 hex>.kpgw` in the folder and nothing else".

**Why one name reaches two computers.** KeePass keeps its configuration in `KeePass.config.xml` next
to `KeePass.exe` (`KeePass/App/Configuration/AppConfigSerializer.cs:131`), preferred unless that
file says to prefer the user's (`:293-295`), and otherwise under `ApplicationData`, which is the
roaming profile (`:143-165`) [R 2.62]. So `KPG.Writer` travels with:

- a portable KeePass copied, plugins and configuration included, to set up a second computer;
- one portable KeePass kept in a synchronized folder and run on two computers;
- a roaming profile on two domain computers, a profile migrated while the old computer is still in
  use, a cloned virtual machine or disk image.

That these happen is [A]. That each carries the name is [R 2.62]. Nothing in the design notices.

**Sequence** [M here, model, `out\store-sequences.txt` section 1]:

1. pc1 and pc2 both run as writer `a1b2c3d4e5f60718`, the folder synchronized. pc1 pairs `c1`; after
   a sync both admit it.
2. pc1 revokes `c1`. Before the next sync, pc2 narrows a group. Each writes its own version 2 of
   the one file.
3. The synchronizer meets two changes to one file: pc2's, the newer, keeps the name, and pc1's is
   renamed `a1b2c3d4e5f60718.sync-conflict-20261115-120000-DEVICE.kpgw` on both sides, as
   Syncthing documents [R others]. Dropbox keeps a conflicted copy the other way round [R others];
   either way one version keeps the name.
4. pc1: **closed**, "a1b2c3d4e5f60718 at 2 is not the file this computer saw at that count".
5. pc2: **serves; record present, revocation absent: `c1` ADMITTED.**
6. pc3, a new computer with no seen file: **`c1` ADMITTED.**
7. pc1 after a restart, taking a new name as the Store page offers: what it now holds of its own is
   what the file under its old name holds, which is pc2's. **`c1` ADMITTED.** The revocation exists
   only in the conflict copy, which nothing reads.

The reverse outcome, pc1's version keeping the name, loses pc2's narrowing instead. Either way a
revocation, a narrowing or a name removal made on one computer is gone, with nothing on the winner
to say so. Step 4 needs the digest rule of section 6's check 3; the design's prototype keeps counts
only (`Store.cs:113`, `:171-176`, `:212`) [R], so its 21 checks never covered it, and without it pc1
serves too.

**Change.** A writer name bound to the computer and the installation, kept beside the seen file in
the local (not roaming) profile and checked against both at every load; a file whose name begins
with a writer name and ends `.kpgw` but is not `<16 hex>.kpgw` closes the store and is offered for
recovery rather than ignored; "Take a new name" first joins what the conflict copy holds.

### P2, serious: a clock running ahead lets a revocation go while the issuer's record still stands

**What the design rests on.** Section 6: "No time is read anywhere in it." Section 9.6: "A writer
lets go of its own record and its own revocation once the certificate has expired ... a record that
came back would be refused on its dates. It never writes a line to take away another writer's
record."

**Why that does not hold.** The join reads no time, but letting go does: "has expired" is the
writing computer's clock. And a record's line belongs to the installation that issued it, which
keeps it in its file until it next writes. So the revoker's emptied line is what hides the issuer's
record, and the revoker alone decides by its own clock when to stop hiding it.

**Sequence** [M here, model, section 2]:

1. A pairs `c2`, whose certificate ends 2026-12-31. B revokes `c2`. After a sync A refuses `c2`.
2. A writes nothing more (a laptop rarely used, a computer switched off).
3. B's clock is two months ahead (2027-01-15). B's next write, of anything, lets go of its
   revocation and its emptied record.
4. A, clock 2026-11-15: **record present, revocation absent, certificate valid: `c2` ADMITTED**, and
   so on every computer whose clock is right, until 2026-12-31.
5. Control, B's clock right: `c2` refused.

CA.md allows a certificate for a day and describes clients renewed a week at a time [R plugin,
`CA.md:204`, `:976`], so the skew that matters is the certificate's remaining life, which can be
hours. Today the same prune exists, but the stale record has to arrive in a merge of an older copy;
here the issuer's line is that older copy, present until the issuer writes.

**Change.** A writer lets go of a revocation only when no file holds a non-empty line for the record
it emptied, and only some margin past `notAfter` (a revocation line is 85 characters, so keeping
them longer costs little).

### P3, serious: a rule removed for an absent node, and the node comes back

**What the design rests on.** Section 9.6: "Rules for nodes that no longer exist stay until the
operator removes them on the Store page, because the node may still exist in a copy that has not
been merged." Section 14: "Nothing in any row widens."

**Why it widens.** Nodes come back under other programs far more readily than they go:

- KeePassXC never applies a deletion in any merge: entries deleted with tombstones and a deleted
  group came back [M keepassxc 4, `del-A-unch`, `GDelA`].
- KeePass brings back an entry deleted on one side when the other changed it later; If newer,
  Overwrite and Keep never carry a deletion [M keepass X3, X4].
- KeePassDX 4.5.8 recreates a missing group from the arriving copy [R android 1.5]; pykeepass
  deletes with no record [R others]; Database Maintenance can clear every tombstone [R keepass 5].
- A whole-file overwrite or a restored `.kdbx` brings back whatever the older copy held [R design 12].

**Sequence** [R, from the cited measurements; the store side follows from R design 9.6]:

1. "Work" grants `reader` `.R...L..`; its subgroup "Private" carries `reader: []`, the documented
   way to close a subtree for one profile (`docs/access-control.md`, "An empty mask is a rule too").
2. The operator deletes "Private" and empties the bin. The Store page counts a rule without a
   node; the operator removes it, as the page invites.
3. A phone's copy, merged into the file by KeePassXC (or synchronized by KeePass after the phone
   edited an entry in "Private"), brings "Private" back under "Work".
4. **`reader` reads "Private".** Nothing refuses, and the Store page has nothing left to show.

Today step 3 brings the group back with its custom data, so with its rule [R inventory N1].

**Change.** Never take a rule away for an absent node. If the page must offer a tidy-up, it writes
a value that refuses (the design already makes "a text that does not read as a rule" refuse there
and below, section 6), so a node that returns is closed until the operator rules again.

### P4, serious: Create and Recreate write an older rule set and name list as new

**What the design rests on.** Section 9.4: "An old store keeps its rules and names after it is
closed. If the newer key is ever lost from every copy of the file, the older key still opens them,
and a new Create carries them." Section 11 offers Recreate as the way out of "a writer's file does
not open, is missing, or is older than another file saw".

**Two sequences** [R design]:

1. *The designed one.* Recreate at T1 (store S1 closed, S2 holds the rules). After T1 the operator
   narrows group G and takes the name `ops` off the list. A whole-file overwrite by a pre-T1 copy,
   or a KeePassXC replacement from one, carried everywhere, loses S2's key from every copy. The file
   now names only S1, which is closed: `503 undecided`, and the way out section 11 offers is
   Recreate. Recreate (or Create) then carries S1's rules and names: **G as it was before the
   narrowing, and `ops` back on the list.** A removed name coming back is a widening [R inventory D6]. Clients pair again after
   Create anyway, but they pair into the older rules, and nothing tells the operator which rules are
   older than the ones they last made.
2. *The unspecified one.* A store closed because a file is missing or older is, by the design's own
   checks, not an image to believe. If Recreate carries "every rule, every profile name and every
   retirement the old image held" from it, or from the last image this computer served, the carried
   lines are fresh lines in a new store, with nothing they could ever be checked against again.

**Change.** Carry only from a store that is open, passes every check and is not closed; show the
carried rules and names on the dialog before OK; never carry profile names, which the operator adds
back deliberately.

### P5, serious: a copy escapes its rule, and the documented layouts are the ones it escapes

The design names this (section 18, point 2) and answers it with a habit: "narrow at the group, and
grant nothing at the root". It understates it in three ways:

- **It is new.** KeePass's Duplicate Entry is `CloneDeep` with a new UUID, and `CloneDeep` copies
  custom data [R 2.62 `PwEntry.cs:856-864`, `:353`], so today the copy is as narrow as the original.
  Duplicate Group and a Ctrl-drag copy of a group are `PwGroup.Duplicate`: `CloneDeep`, which copies
  custom data, then a new UUID for the group and for every group and entry inside it
  [R 2.62 `PwGroup.cs:1437-1446`, `:396`; `MainForm.cs:2324-2330`, `:1318`]. Here every rule in the
  copied subtree is left behind at once.
- **The habit reverses the documentation.** `docs/access-control.md` teaches a rule on an entry
  beating its groups, an empty mask on a group "which is how a subtree is closed off for one
  profile while leaving the rest alone", and the seal "on a group holding things no program should
  reach" [R plugin, `:378`, `:389-390`, `:396-407`]. A duplicate of a sealed entry, or a pasted copy
  of a closed group inside the granting group, is open.
- **"Grant nothing at the root" does not cover copies made inside a granting group**, which is
  where Duplicate puts them: in the original's parent, next to it [R 2.62 `MainForm.cs:2324-2330`].

**Change** (bounds it): a marker written once on the node by the grant editor at OK, naming the UUID
the rule is stored under, and never changed. A node whose marker names another UUID is a copy and
refuses until ruled. The marker is constant, so no stale copy and no time decides it; a program that
deletes node custom data (KeePassDroid, KeePassDX 3.2, 3.3 [M spike]) loses only the copy detection,
since the original's rule is in the store.

### P6, minor: KeePassXC's own save widens the window in which its merge removes the keys

[M here, `out\xcsaved.txt`]: a copy with no custom data (`old.kdbx`, the design's base) saved once
by `keepassxc-cli add`:

| release | the save leaves the database's custom data | that copy merged into the present one, loaded later |
| --- | --- | --- |
| 2.4.2 | empty | both keys kept |
| 2.6.6 | empty | both keys kept |
| 2.7.12 | `KPXC_RANDOM_SLUG`, `_LAST_MODIFIED` | **both keys removed** |
| 2.8.0 | `KPXC_RANDOM_SLUG`, `_LAST_MODIFIED` | **both keys removed** |

So the window of section 1 and section 18 point 1 is every copy from before the first Create that a
current KeePassXC ever saved, and every copy from before the latest Recreate whatever saved it,
since such a copy holds the old keys. The outcome stays closed. The design's 18.1 also leaves out
the whole-file overwrites its own section 13 lists, which take the keys away the same way.

### P7, minor: a restore of the computer restores the seen file too

Section 12 claims "Restore both on the same computer, older than it last saw: `503 undecided` ...
Today the same restore brings revoked clients back with no question asked." The seen file is
`%LOCALAPPDATA%\KeePassGate\seen\<id>.kpgs` (section 5.1). A disk image, a VM snapshot reverted, or
a profile restored puts it back with the folder, and then the store is served as found, as today
[R design 5.1, 6; A that such restores are common]. The claim holds only for a restore of the folder
alone.

### P8, minor: one file rolled back, not only a whole folder

Section 18 point 5 limits what the checks cannot see to "a whole folder taken back to an earlier
state on a computer with no seen file". One file is enough [M here, model, section 3]:

1. B writes something; A, knowing it, opens a group (`reader: [read, list]`); B, knowing A's rule,
   closes it (`reader: []`). A writes nothing further.
2. On C, a computer with no seen file, B's file is the version before the narrowing (a restored
   version, a lagging replica). A's file names B only at the older count, so every check passes.
3. **C serves `reader: [read, list]`.** A itself, having loaded the newer file, is closed when
   given the same older file.

### P9, minor: the replaced keys can reach an authority that was recreated away

Recreate writes the file first and `KPG.Closed` into the old store after (section 9.4). The old
store stops being a candidate only where that line has arrived.

1. pc1 recreates (S1 to S2), because a copy of the unlocked database leaked. pc2's replica of S1's
   folder has not yet received pc1's `KPG.Closed` line (a paused synchronizer), or pc1 stopped
   between the save and that line.
2. A KeePassXC merge from a pre-Recreate copy replaces the file's keys with the old ones (P6).
3. pc2 opens it: S1 opens, is not closed there, is the only candidate. **The leaked authority and
   its clients are served.** [R design 9.4, 11; the replacement M spike, M here]

Section 18 point 6 mentions this only for an attacker refilling the old store; no attacker is
needed. **Change:** each computer remembers, beside its seen file, the newest store id and authority
it has served for this database and refuses an older one; and Recreate writes the old authority's
retirement into the old store before saving the file.

### P10, minor: the 4.0 file of section 16 does not stay 4.0

Section 16 recommends writing the two keys without times, so that kdbxweb's defect is avoided. A
KeePassXC merge stamps every key it copies with the merge's time [M spike], and KeePassXC from
2.7.0 writes 4.1, keeping those times, whenever the tree holds another 4.1 feature [M spike]. The
times, and kdbxweb's unreadable result [M others], come back. As today; not depended on.

### P11, minor: writers only accumulate, and a lost store key forgets every retirement

- A writer's file can never be removed while another file's `saw` names it (check 2), its records
  and revocations are let go only by itself, and more than 16 writer files are refused (section
  5.2). Every new computer, reinstall, "Take a new name", and every start of a KeePass whose
  configuration is not saved (`Application.ConfigSave` off, an enforced configuration, a crash
  before KeePass writes its configuration) adds one [R 2.62 `AppConfigSerializer.cs:385-388`;
  A for how often]. The ceiling then closes the store for good, and Recreate is the only way out.
- Retirements now live only in the store. If the store key is lost from every copy, Create starts
  with none, and an old authority a stale copy brings back is no longer known to be retired. The
  "more than one authority not retired" refusal still holds, so the operator is asked rather than
  served; but the operator then chooses between authorities with nothing to say which was retired.

### P12, minor: the per-request look can miss a change

Section 8 reloads when the names, lengths or times of the writer files differ. A change that keeps
a file's length (a rule `read` turned to `list`, a count that keeps its digits) and lands with a
time the look already saw is not seen until something else changes: a synchronizer that sets the
source's time on the copy it writes, FAT32's two-second times, an SMB client's cached directory
listing [A for each; measured only on local NTFS by the design]. **Change:** read the 12-byte nonce
at the start of each file as well; it is random per write (section 5.2).

**An aside for the security lens.** KeePassXC's merge report prints each removed key with its
value, so the run above printed the store key in clear on the console
(`Plugin-Daten KPG.Store.<id> entfernt [{"v":"1","key":"..."}]`) [M here]. The authority document is
printed the same way, today as well.

## 5. The design's claims of survival, and their basis

| Claim (section) | Design's mark | Finding | To measure |
| --- | --- | --- | --- |
| copying the newer of each file between replicas is a correct merge (1.4) | M here, 21 checks | measured with distinct writer names and the prototype's own copy-by-count; false when one name is on two computers (P1) | real synchronizers with one writer name on two computers |
| check 3 compares digests where counts are equal (6) | M here | the prototype keeps counts only [R `Store.cs:113`, `:171-176`]; unmeasured by the design; my model shows it closes the losing computer and nothing else (P1) | the prototype with the digest rule |
| "No time is read anywhere in it" (6) | | true of the join; letting go at expiry reads the local clock (P2) | |
| no program that opens a `.kdbx` touches the store (13) | A | no counterexample in any ground report; two-way synchronizers on phones carry a person's deletions [A] | a phone synchronizer and file manager over the folder |
| a KeePassXC removal never serves an older state (13) | M here | held for pre-Create copies [M design, M here]; not where a pre-Recreate copy meets a replica without `KPG.Closed` (P9) | |
| the window is a stale copy holding another key (1, 18.1) | M here | understated: any copy KeePassXC 2.7.12 or 2.8.0 saved qualifies [M here]; overwrites too | the window's merge (2.7.11+) and reload merge with these keys |
| Keepass2Android and KeePassDX rows (13) | M spike | keys of the same shape, not these keys | the spike's flows with these keys |
| a restore of both older than last seen closes (12) | | only when the seen file was not restored with them (P7) | a VM snapshot revert |
| a synchronized folder has nothing to merge (12) | A | false with a repeated writer name (P1) | |
| "Nothing in any row widens" (14) | | P1 to P5 each end in a widening or a revocation undone | |
| a record that came back is refused on its dates (9.6) | | by the reading computer's clock; with a skewed revoker it is admitted (P2) | |
| an old store's rules and names are carried by a new Create (9.4) | | a designed rollback (P4) | |
| the file stays 4.0 without per-key times (16) | M here | for KeePass; KeePassXC merges put times back (P10) | |
| a change on another installation bites on the first request after its file lands (8) | M here, local NTFS | not for a same-length change within the look's resolution (P12) | the look over Syncthing, FAT32 and SMB |
| what the checks cannot see is a whole folder on a computer with no seen file (18.5) | M here | also one file (P8), and also a computer whose seen file was restored (P7) | |
| a replace is retried and fails as not saved (9.1) | A for .NET Framework | as the design says | |

## 6. What was run

**KeePassXC over a KeePassXC-saved stale copy.** `scripts\Run-XcSavedStale.ps1`, under PowerShell 7,
no window. The bases are copies of the design's `old.kdbx` (no custom data) and `anchored.kdbx`
(both keys, with times, 4.1), Argon2d 200 iterations, 64 MiB, 2 lanes, password `probe-only`, made
by the design's probe. `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 as the `keepassxc` ground
report downloaded them and verified them against the spike's pins and DroidMonkey Apps' signature
(`spikes\storage\probes\ground-keepassxc\logs\download.log`). Per release: `add stale.kdbx xc-added`, then
`merge -s target.kdbx stale.kdbx` with the target loaded first. Read back with the design's
`Probe.exe describe`, run read-only over my copies. Each merge took 9.5 to 13.6 s, so the two loads
fell in different seconds. Output `out\xcsaved.txt`, files under `work\xcsaved\<release>\`.

**The store, three sequences.** `scripts\Survival.cs`, compiled by the framework's `csc` (C# 5)
against the design's Bouncy Castle 2.7.0 and Newtonsoft.Json 13.0.4 copied into `bin\`. The store
classes are the design's prototype with these changes, each to match the design's text: only
`<16 hex>.kpgw` is read (section 6); the seen file keeps a digest beside each count and closes on a
digest that differs at an equal count (section 6, check 3); one `Apply` writes several keys in one
file write (section 9.1); and, riding on every write, a writer lets go of its own `KPG.Revoked.` and
`KPG.Client.` lines whose certificate has ended by its own clock (section 9.6). The synchronizer
carries a file changed on one side to the other, and for a file changed on both since they last met
keeps the newer by modification time and renames the older on both sides, as Syncthing documents
[R others]. Output `out\store-sequences.txt`, folders under `work\store\`.

Not run: any window, any Android program, any real synchronizer, any test tier.

## 7. Measurements to add to the design's section 21

1. **One writer name on two computers**, with Syncthing, Dropbox and OneDrive over the folder: what
   each keeps, what it names the other version, and what the plugin then serves (P1).
2. **The let-go under a skewed clock**, on the real implementation once there is one (P2).
3. **KeePassXC's merge from its window and its reload merge, 2.7.11 to 2.8.0, from a copy it saved
   before the first Create** (P6), and a KeePass synchronize afterwards through KeePass's window.
4. **A node deleted and brought back** by KeePassXC, by KeePass Synchronize after an edit on the
   other side, and by a pykeepass deletion merged elsewhere, against a rule removed on the Store
   page (P3).
5. **A VM snapshot reverted** with the store and the seen file in it (P7).
6. **The per-request look** over a synchronizer that sets times, a FAT32 stick and an SMB share,
   with a change of equal length (P12).
7. **What Recreate and Create carry** from a closed store and from one that fails a check (P4).

## 8. A note on the timestamp-named-field idea

The design presents itself as the timestamp-named-field idea (JSON that a merge keeps on both sides, a step at
load that settles conflicts) with the unit that merges moved out of the file (its section 1). The
survival attack bears on that idea wherever it is placed: both designs decide by an identity that
must be unique (here a writer name, there a field name made from a time, which two copies can make
in the same second, and a tie loses one side in KeePass, KeePassXC and the Android programs
[M keepass 1e; M keepassxc 5; R android 1.1]), and both need their step at load never to let go of a
refusal on a local clock. Moving the unit out of the file buys survival under every KDBX program;
it does not buy uniqueness or clock independence, which have to be designed in either way.
