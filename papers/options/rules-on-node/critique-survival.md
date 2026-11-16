# Critique: rules-on-node, survival across programs

Key: `rules-on-node-survival`. Written 2026-11-15 against `option-rules-on-node.md` as it stood at
16:30, KeePassGate master `a42e38aa` and KeePass 2.62. The lens is survival across programs: every
save and merge in the ground reports, walked through this design for datum 7 (the access rules)
and the two things the design adds to carry it, the rule entries and the seals. I attack the
design. I do not repeat what its section 10 already says it does not solve, except where it
understates it.

**How each statement is marked.**

- **[M mine]**: measured for this critique by the probe in section 6, over KeePass 2.62's
  KeePassLib, console only, invented databases. The probe applies the design's read rule exactly
  as the design's own probe implements it ("D" below) and a stricter variant ("V", section 2.1).
- **[M design]**: measured by the design's own probe, whose output I read
  (`storage\rules-on-node\out\keepass.txt`, `xc-*.txt`).
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in that ground report,
  section named.
- **[R ...]**: read from source, in the ground report named, or by me in the KeePass 2.62 source
  the `keepass` report verified (`storage\keepass\src`), with file and line.
- **[A]**: assumed. Every assumed statement about another program is repeated in section 5.

## 0. Verdict

**Viable with changes.** The mechanism at the center holds: a seal outside the node decides, the
text is authenticated by its digest, and no time is compared anywhere. That makes the design
immune to the time failures that break today's store (dropped per-key times, KeePassDX's stale
rule under a newer time, KeePassXC's raised group time) and it gave the head rule or a refusal in
every merge the design ran, where the set of seals was intact [M design].

It does not hold when the set of seals is damaged, and the read rule as written turns four kinds
of damage into an open answer instead of a refusal. Measured here under KeePass 2.62's own merges:

1. **One seal lost after the merge the design calls ordinary** rolls the rule back to the older
   text, or to silence for a first rule: 6 of 8 cells in each case [M mine C1, C2].
2. **The plugin's own prune**, if it leaves deletion records as the plugin's removals do today,
   brings a deleted ruled group back unruled when another copy put an entry in it: 5 of 8 cells
   [M mine C4].
3. **Seal identifiers derived from content** make a later narrowing in another copy the same entry
   as an earlier seal that was since superseded, so the narrowing is dropped: 8 of 8 cells
   [M mine C5].
4. **The editor seals whatever text a refusing node carries at the next OK**, which hands every
   refusal the design relies on to the text it was refusing [R design §4.1].

Each has a change that closes it, the first three measured here. What stays open after the changes
is a seal lost *and* the sealed text aged out of history before anybody acted. Any store whose
only record of a decision is an entry a person can delete shares that.

## 1. What holds, so that the rest is read in proportion

- With the seals intact, the design's 11 scenarios gave the head rule, the sealed silence or a
  refusal in every cell of KeePass's four methods and four KeePassXC releases [M design]. My C7
  re-ran its scenarios 01, 05 and 07 and got the same answers [M mine].
- No time is compared, so a program that drops, rewrites or skews times changes only which version
  of an entry is current, never which rule is in force [M design 01, 02; R design §3].
- A group's own time decides nothing: a rule changed in one copy survived the other copy editing,
  adding entries and moving the group's time later [M design 08].
- A rule entry and a seal are ordinary entries with ordinary strings, which every save measured or
  read keeps [M keepassxc §6; M others §3; R android §2]. The Android half is read, not measured
  (section 4).

## 2. Problems, most severe first

### 2.1 Losing one seal fails open (serious)

The read rule uses a carrier's history only to *find* the head's text (step 7). It never checks
history against the seals, and its "no seal" test (step 3) looks at current strings only. After the
merge the design calls "ordinary, not exotic" (§1.3), the node's current text is the older one and
the sealed text is only in history. From that state the newest seal is the only evidence that a
newer rule exists, and losing it reads as if that rule never existed.

**Sequence A, a rollback. KeePass, then a phone, then KeePass.** [M mine C1]

1. KeePass with the plugin narrows entry X from R0 (`reader` read and list) to R1 (`reader`
   nothing) at +10. Seal S1 names S0.
2. A phone holding the stale copy changes X's password at +20.
3. Any merge, either direction. X's current version is the phone's, carrying R0; R1 is in X's
   history. The gate serves R1, as designed [M design 01; M mine C1 "merged"].
4. S1 alone is lost: removed for good, moved into the recycle bin, or moved out of the seal group
   (for example to the root, where Keepass2Android puts an arriving entry whose group the open copy
   lacks [R android §1.5]).
5. The gate: S0 is now the only head, R0 is on the node and S0 names it. **R0 is in force: the
   narrowing is undone, silently.** 6 of 8 cells, for all three ways of losing S1 [M mine C1 (a),
   (b), (c)]. The other two cells are those where R1 won the merge and sits on the node; they
   refuse.

**Sequence B, a first rule.** [M mine C2] As A, but entry Y had no rule. KeePass gives Y its first
rule R1 and seals it; the phone changes Y's password later; after the merge Y's current version
carries no `KPG` and R1 is in history. Y's one seal is lost. **No seal and no current text:
silence, and Y inherits what its group grants.** 6 of 8 cells.

**Sequence C, a field and then the seals.** [M mine C3] Someone deletes X's `KPG` field on a phone.
After the merge the sealed rule stays in force from history, as the design says [M design 07]. Later
the KeePassGate group is deleted. **X: silence.** Group U, whose text is on its rule entry,
refuses. The design's §6.2 says "every ruled node refuses" for that act; that holds only for nodes
whose text is on the carrier at the moment.

**How a seal is lost without anybody setting out to remove a rule and its seals**, which is what
§6.2 and §10 concede:

- **Seals of one node look like duplicates.** Every seal of a node has the same title, "Rule seal"
  and the node's first eight hex characters (§2.3; the design's probe writes exactly that). After a
  rule's first change, every program lists two entries of one name in the seal group, which a
  person tidying up on a phone or in KeePassXC reads as a duplicate [A]. Deleting the newer one is
  sequence A.
- **Deleting is moving into the recycle bin** in KeePass, KeePassXC, Keepass2Android, KeePassDX and
  KeePassDroid [R keepass §9; R keepassxc §8; R android §4.2], and the design reads seals from the
  seal group only (§3 step 2). It never says whether a seal in the bin counts; under the rule as
  written it does not, so the bin alone is enough [M mine C1 (b)]. A KeePassXC merge carries a
  phone's bin into a copy without one as an ordinary group named "Recycle Bin" [M keepassxc §4,
  `bin-A`], so a seal binned on a phone arrives outside the seal group there too [A, from that
  measurement and the rule].
- **Merges that misplace or drop an arriving entry.** Keepass2Android puts it at the root when its
  group is missing in the open copy; KeePassDX 3.2.0 drops it [R android §1.5]. The design's §5.2
  says this "leaves its node with a text its seals do not name: a refusal, not a widening [A]". That
  is true only of a first seal whose text is on the node. For any later seal the node keeps its
  older seals, and the result is sequence A [M mine C1 (c)].
- **pykeepass removes an entry without a deletion record** [R others §3], so a script that removes
  a seal leaves that copy rolled back until a merge brings the seal back.

What the gate then does: it serves the older, wider rule (A) or the group's grant (B, C), and
nothing in the audit says a rule was lost, because the read rule found nothing wrong.

**Change, measured.** Apply steps 3 and 5 to the carriers' history as well as their current
strings: a node whose carriers or their history hold a rule text and which has no seal refuses,
and a text in a carrier's history that no seal of the node names refuses. Under that rule (V), C1,
C2 and C3 refuse in every cell, and the design's recoveries 01, 05 and 07 are unchanged
[M mine C1 to C3, C7]. Two more pieces belong with it:

- **Read a seal wherever it is**: the bin, the root, a second seal group, a group a merge made out
  of another copy's bin. Recognize it by its strings and its identifier, as §2.3 already checks, and
  make every entry that carries `KPG.Rule` unaddressable on every route, as §7 does for an entry
  carrying `KPG.Group`. Reading seals anywhere makes the API's refusal of reserved names load
  bearing for seals, since a client able to plant a seal could make itself a head. It is already
  load bearing for `KPG` (§7).
- **Titles that differ within a node**, so that no program shows two seals as one entry twice.

What V costs: a hand-edited text that ends up in history after the operator repairs the node keeps
the node refusing until that history item ages out, unless the repair names it in a superseded
seal. What V leaves open: once history no longer holds the text, the lost seal is invisible again
and sequence B is silence. History goes after ten later edits in any program, at every Strongbox
save, at every kdbxweb merge, by Database Maintenance's age limit, by `kp-purge-history`, or at once
under a limit of 0 [M keepass §1b; R others §3, §4]. The design keeps that window open for as long
as nobody writes the entry through the plugin or opens its dialog (§0 item 6, §4.3). To close most
of it, repair at `FileSavingPre` without moving the entry's time, as owed standing answers already
ride every save (inventory P18, O4): the sealed text then goes back onto the node in the file a
synchronize writes to both sides [R keepass §4 for where that hook runs; not run].

### 2.2 The prune brings a deleted ruled group back unruled (serious)

The design prunes "the seals of a node that is no longer in the database" on the plugin's own
writes (§4.5) and does not say whether the prune leaves deletion records. The plugin's removals
today stamp what KeePass's own delete stamps (inventory P16, `RemovalMergeTests`), so a prune
written in that habit leaves them.

**Sequence. KeePass, then a phone, then KeePass.** [M mine C4]

1. On the desktop the operator deletes group U for good (Shift+Delete, or emptying the bin). U
   carried a narrowing rule. KeePass records a deletion for U and for every entry in it, the rule
   entry included [R `KeePassLib/PwGroup.cs:1367-1386`; M keepass §2.2].
2. Any write of the plugin's in that database (a pairing, a renewal, an API write) prunes U's seals.
3. A phone holding the stale copy adds a new entry N to U. On the phone, U is still the narrowed
   group it always was.
4. Synchronize. U survives because it is not empty and holds only N; the rule entry is older than
   its deletion record and is deleted; the seals are older than theirs and are deleted
   [M keepass §2.2; R `KeePassLib/PwDatabase.cs:1083-1108`].
5. The gate: U has no seal and no carrier. **Silence: N, put into a group its author saw as
   narrowed, is served under U's parent's grant.** 5 of 8 cells: into the deleting copy under all
   four methods, since its own deletion records apply under every method
   (`PwDatabase.cs:929`), and into the other copy under Synchronize [M mine C4].

The same prune without deletion records gives **refuse** in the same five cells, because the seals
come back from the copy that kept them [M mine C4]. Today's store keeps the rule here, since U
comes back as the other copy's group object with its custom data (inventory P5). Keepass2Android
applies deletion records by time as KeePassLib does [R android §3.1], so it is assumed to give the
same result; KeePassXC applies none [M keepassxc §2], so there U returns with its rule.

The design's §4.5 says a node that comes back "returns with a text ... where it returns without
them it refuses". A group that comes back because of a new entry returns without its rule entry,
and §5.3's "the group refuses [A]" holds only when no prune ran in between.

**Change, measured**: the prune removes seals without deletion records, or seals are never
pruned.

### 2.3 Any OK seals whatever a refusing node carries (serious)

§4.1: "At `EntrySaving` the tab writes its answer into `EntryStrings` once more"; "At `EntrySaved`
the plugin adds the seal, if the text now on the entry is not the head's"; for a refusing node "the
operator can keep the found rule (OK then seals it)", and "for two heads, any OK seals the rule
shown after both". Nothing requires the operator to have looked at the KPG tab, so every refusal
the design counts on is resolved by the next OK of that dialog, in favor of the text the node
carries.

1. **A hand edit.** On a phone someone changes X's `KPG` from `{"reader":[]}` to
   `{"reader":["read","list","protected"]}`. After the merge X refuses [M design 06]. Days later
   the operator opens X in KeePass to change its password and presses OK. EntrySaved finds a text
   that is not the head's and seals it: **the hand-typed wider rule is in force.**
2. **A tie lost, Keep existing, history aged out, an entry too big for its history.** X carries the
   older R0 and the sealed R1 is nowhere: refuse [M design 02, 08, "history"; M mine C6].
   EntrySaving cannot put back a text it cannot find, so R0 stays; EntrySaved finds a text that is
   not the head's and seals it: **R0 is in force.** The rollback the seal exists to stop arrives
   through the operator's next unrelated OK.
3. **Two heads.** Two KeePass installations with the plugin each changed X's rule [M design 03].
   "The rule shown" is not defined. If it is the text on the node, it is the one from the copy whose
   entry won the merge by time, which is the choice by time that the list of predecessors was
   introduced to avoid (§2.3: it "never drops a concurrent change without the operator seeing it").

The group dialog's apply at `FormClosed` (§4.2) leaves the same question open for a refusing group.

**Change**: a refusing node keeps refusing through OK. EntrySaving writes nothing for it and
EntrySaved adds no seal unless the operator chose a rule on the KPG tab in that dialog. The tab
offers every candidate (each head's text, the current text, the texts in history) with none chosen
in advance.

### 2.4 A seal identity derived from content drops a later decision (serious)

A seal's identifier is a digest of its node, its rule's digest and its predecessors (§2.3), "so the
same seal made in two copies is the same entry". The same choice makes a later, independent
decision with the same content as an earlier one *the* earlier one, which may already be
superseded.

**Sequence.** [M mine C5]

1. Copy A: group W gets its first rule `{"default":"deny"}` at +10, seal S. At +20 the operator
   takes it away: the rule entry is deleted for good and a seal `none` names S.
2. Copy B, without A's changes: the operator gives W the same first rule at +30. Its seal has S's
   identifier [M mine C5: "one identifier: True"].
3. Any merge, any method, either direction: seals S and `none`, head `none`. B's rule entry carries
   a text S names, so step 6 ignores it. **Silence: the later narrowing is dropped.** 8 of 8 cells
   [M mine C5].

Copy B needs no second installation. One KeePass with the plugin whose file was overwritten whole
by a phone's stale copy (Keepass2Android "No, overwrite" [R android §5.5]), in which the operator
sets the rule again, and a third copy that had received the removal and is synchronized later,
give the same sequence [A, composed of the measured merge and the read overwrite]. The rule writer
makes such collisions likely: one canonical text per meaning (§2.1), so `{"default":"deny"}` is the
same bytes wherever it is set.

A smaller effect of the same choice: a seal made again after its deletion (the operator keeping
the found rule on a node whose seals were lost) has the deleted seal's identifier, and kdbxweb
removes anything named in `DeletedObjects` whatever its time [R others §3]. In KeeWeb the repair is
then undone at every merge with a copy holding that record. That refuses (closed), and it is the
reason §2.2 gives for not deriving the rule entry's identifier.

**Change**: a fourth string on each seal, a random nonce, with the identifier derived from all
four. The same change made in two copies then gives two heads and refuses, which is the closed
direction.

### 2.5 The set of seals must be found the same way in every copy, and that is deferred (serious)

Every result above and in the design assumes one seal group found the same way in every copy. The
design leaves that to the KPG group design (§2.4), and under the read rule as written:

- **Two seal groups**, made independently in two copies before they first met, split a node's
  seals if the group is not found by a fixed identifier; the half in the group that is not read
  gives sequence A or B of 2.1 [A, from the rule].
- **File then Import with Create new IDs**, which KeePass preselects [R keepass §2.4], adds a second
  KeePassGate group with every seal copied under a new identifier. The identifier check correctly
  ignores the copies. But that group is not the KPG group, so the API serves its entries
  [A, from §2.4 and §7], and once seals are read wherever they are (2.1), a client with `delete`
  there must not be able to remove any of them.
- **The KPG group moved into the bin** by a person: read or not, unspecified.

**Change**: a fixed identifier for the KPG group and the seal group, seals read by content wherever
they are, every entry carrying a seal's reserved strings hidden from every route.

### 2.6 After the ordinary merge, the text every other program shows is the stale one (minor)

The design's reason for keeping the text on the node is that "the rule is visible and searchable on
the node in every program" (§0, "The honest weakness"; §6.1). That holds until the first merge in
which the other copy's version wins, which §1.3 calls ordinary. From then on every program shows,
and every search finds, the *older* rule on the node, while the rule in force is in history, which
no Android program searches by default [R android §1.3]. "An operator can use any program's search
to find every node that names a profile" (§6.1) then finds nodes by rules that are not in force and
misses the ones that are. This state lasts until the plugin writes the entry or the operator opens
it, and its length is the window of 2.1. The repair at `FileSavingPre` proposed there shortens it.

### 2.7 An entry whose history cannot hold a revision refuses on every rollback (minor)

An entry with a 1.5 MiB attachment under a 1 MiB history size limit: after the design's scenario 01,
X refuses in both directions, its history empty [M mine C6]. At the default 6 MiB limit the same is
expected of any entry whose attachments exceed it [A, from M keepass §1b: the size limit binds
before the count]. That is closed, but such an entry can never be recovered from history, so every
ordinary merge in which the other copy's version wins refuses it. §10 names a history limit of 0,
not this.

### 2.8 What a client reaches still depends on where a node sits, which the programs merge badly (minor)

Per node, §6.2's closing claim ("the gate's answer is either the rule the operator last set through
the editor or a refusal") holds. For what a client reaches it does not, because an unruled entry
inherits from wherever it is:

- KeePassXC: a move survives only while no newer content arrives, and the copies then disagree for
  good [M keepassxc §4, `move-A-edit-B`].
- KeePassDX 4.5.8: an unchanged arriving copy undoes a move at equal times [R android §5.4]; a merge
  that deletes a group still holding an entry not itself deleted moves that entry to the first
  parent not deleted [R android §5.3].
- KeePassXC: a phone's recycle bin arrives as an ordinary group under the root, its entries
  reachable and inheriting the root's rule [M keepassxc §4, `bin-A`].

All of this is true of today's store as well. It is listed because §10's "the recycle bin markers of
T049 are not covered and not needed" understates the last item: datum 7's inheritance needs a bin
made by a merge to be recognized, whatever the store.

### 2.9 Every first seal carries an empty string (minor)

`KPG.After` is empty for a first rule (§2.3). That every program keeps an empty-valued custom string
through a save and a merge is not measured for any Android program, Strongbox, KeePassKit or kdbxweb
[A]. kdbxweb drops empty custom data values on save [M others §3]; strings were not tried. If a
program drops it, the strict reader ("a seal that will not read is no seal", §3) loses every first
seal: a text on the node refuses, and a text only in history is sequence B of 2.1. Write a
non-empty token such as `-`.

### 2.10 A rule entry where it does not belong refuses the whole database from the root (minor)

A carrier whose text no seal of its group names refuses that group (§3 step 5). Under the root that
is the whole database. A person moving a rule entry there is in §6.2; Keepass2Android also puts an
arriving entry at the root when its group is missing in the open copy [R android §1.5], which turns
a merge into a refusal of everything. Closed, but cheap to blunt: a rule entry whose `KPG.Node`
names another group could be ignored as a carrier and only reported, since its text would refuse
nothing it governs.

## 3. Program by program, for the rules, the rule entries and the seals

"Outcome" is the gate's answer under the rule as written. Only sequences beyond the design's own
§5 and §6 are listed.

| Program | Save | Merge | Sequence that hurts | Outcome | Basis |
| --- | --- | --- | --- | --- | --- |
| KeePass 2.62, Synchronize | keeps strings, entries, history [M keepass §2.1] | entries a union; newer entry whole, loser into history; both copies' deletion records applied by time | 2.1 A and B after a seal is binned or deleted; 2.2 with the prune's records; 2.4 | R0 in force; silence; silence | [M mine C1, C2, C4, C5] |
| KeePass, Overwrite if newer, Overwrite existing, Keep existing | | the open copy's own deletion records applied under every method | 2.2 into the deleting copy | silence, 4 of 4 | [M mine C4; R `PwDatabase.cs:929`] |
| KeePass, Create new IDs | | every node twice under new identifiers | the copied KeePassGate group is not the KPG group | copies refuse [M design]; the copy's entries served by the API [A] | 2.5 |
| KeePass's own tools | | | history deleted by age, limit 0, a large attachment | refuse while the seals hold; silence under V once a seal is also lost | [R keepass §1b; M mine C6] |
| KeePassXC 2.4.2 to 2.8.0 | keeps strings; adds empty standard strings to rule entries and seals [M design] | never applies deletions; tie keeps the target; a loser behind `maxItems` newer items is dropped; a foreign bin arrives as an ordinary group | a seal binned on a phone arrives outside the seal group | R0 in force or silence | [M keepassxc §2, §4, §5; A for the outcome] |
| Keepass2Android 1.15-r6 | keeps custom strings and flags; prunes history at every load | arriving copy wins a tie; arriving entry at the root when its group is missing; deletion records applied by time | 2.1 A by root landing; 2.2; shorter 01 windows | R0 in force; silence | [R android §1.1, §1.3, §1.5, §3.1]; outcome [M mine] under KeePassLib, of which its merge is a port [A] |
| KeePassDX 4.5.8 | keeps custom strings; prunes history only on edit | open copy wins a tie; missing group recreated; arriving deletion records applied, not kept; a delete moves into the bin and touches the time | a seal binned on the phone; 2.2 when it merges the deleting copy in; 2.8 entries moved out from under their group | R0 in force; silence | [R android §1.1, §1.4, §3.1, §5.3] |
| KeePassDX 3.2.0, 3.3.0 | keeps custom strings (3.2.0 read, 3.3.0 not read) | drops an arriving entry whose group is missing; no tie branch | a seal dropped | R0 in force or silence | [R android §1.5] |
| KeePassDroid 2.7 | keeps custom strings | no merge; overwrites a changed file | conceded in §10 | | [R android §2, §4.2] |
| kdbxweb 2.1.1, KeeWeb | keeps strings, flags, history [M others §3] | tie keeps the open copy; history items the arriving copy lacks are dropped; deletion records applied whatever the time | 01 recoveries become refusals; a remade seal is deleted again (2.4) | refuse | [M others §3, §4; R others §3] |
| pykeepass 4.2.0 | keeps everything [M others §3] | no merge; deletes without records; edits without moving times | a seal removed by a script | R0 in force or silence in that copy until a merge | [R others §3] |
| Strongbox 1.65.0 | trims history at every save | tie keeps both versions; deletions combined | shorter windows for 2.1 | as 2.1 | [R others §3] |
| KeePassKit 3.3.2+ | keeps strings | arriving copy lost at a tie | | refuse | [R others §3] |
| keepass-rs 0.17.0 | keeps strings | tie keeps both; `kp-purge-history` removes all history | every rule served from history refuses; silence under V with a lost seal | refuse | [R others §3] |
| KeePassium, gokeepasslib | keep strings | no merge | | | [R others §3] |
| Whole-file synchronizers | one file wins whole | | conceded in §10 | | [R others §3] |

## 4. The design's claims of survival, against their basis

1. **§0 item 5 and §12, "no cell had a rule in force other than the head's".** Measured, by the
   design's own implementation of the rule (not the plugin), over KeePassLib and `keepassxc-cli`
   only, two copies, every step saved and reopened (so no unsaved sub-second times inside one
   KeePass session, R keepass §1c), at most one seal beyond the base per node, no seal deleted but a
   node's only one, and no prune. Every open answer in 2.1 to 2.4 lies outside that set.
2. **§0, "rules survive KeePassDroid and KeePassDX saves".** Read only [R android §2]; no Android
   program has saved a custom string under measurement [M measured §4.1].
3. **§5.1, Keepass2Android, KeePassDX and KeePassDroid keep `KPG`, `KPG.Group`, the seal strings
   (one of them empty), a value of 4482 characters and auto-type off.** Read or assumed.
4. **§5.2, the Android merges leave the losing version in history with its strings.** Read; the
   KeePassDX order of steps is read at one tag.
5. **§5.2, "both seals arrive" in Android merges.** Read. The spike never had an Android merge add
   an entry only the arriving copy held, and never in a group other than the root [M measured §4.6].
6. **§5.2, "a seal that lands outside it ... a refusal, not a widening [A]".** False for any seal
   after a node's first [M mine C1 (c)].
7. **§5.3, "the group deleted in one copy, an entry added to it in the other: the group refuses
   [A]".** True only when no prune with deletion records ran between [M mine C4].
8. **§6.2, "delete a seal for good: refuses".** Measured for a node's only seal with its text on
   the node [M design 10]. False for a later seal after the ordinary merge, and for a first rule
   served from history [M mine C1, C2].
9. **§6.2, "delete the seal subgroup or the KPG group: every ruled node refuses [A]".** False for
   every node served from history [M mine C3].
10. **§4.5, "where it returns without them it refuses".** False for a group that returns because of
    a new entry [M mine C4].
11. **§2.3, the list of predecessors "never drops a concurrent change without the operator seeing
    it".** False [M mine C5].
12. **§0 and §1.3, "a stale rule can no longer win silently in any program including KeePass
    itself".** False after one lost seal [M mine C1] and with no loss at all [M mine C5].
13. **§2.3, "superseded seals cannot be pruned earlier: one that came back from a stale copy would
    be named by nobody and look like a second head".** The reasoning does not hold: a superseded
    seal is named by its successor whether it was pruned and came back or not [R the rule]. What
    pruning it would cost is the recovery from history, since its text would become unnamed and
    refuse. The conclusion stands for that other reason.
14. **§6.2, "Delete Duplicate Entries: nothing deleted".** Measured for two rule entries [M design];
    seals differ in every string by construction.
15. **§4.1, the dialog's events and their order, Cancel, an unchanged OK.** Read only; the design
    lists it.
16. **§5.2, "the winner already has as many newer revisions as the history limit: refuses [A]".**
    Follows from the rule, over KeePassXC's measured behavior [M keepassxc §5].

## 5. Measurements to add to the design's section 13

1. **C1 to C5 on the Android programs**, with the seal deleted on the phone into its bin, the
   Keepass2Android root landing, and Keepass2Android and KeePassDX merging in the copy that deleted
   a group (`ground-android.md` §7 runs R1, R5, R9 over this base).
2. **Chains of more than one seal per node** in every program that merges, with each seal missing
   in turn.
3. **KeePassXC's merge with a phone's bin**: whether a binned seal arrives in an ordinary "Recycle
   Bin" group outside the seal group.
4. **An empty-valued custom string** through a save and a merge in each program.
5. **A real File then Synchronize in KeePass with the plugin**, with the prune and with a repair at
   `FileSavingPre`, including unsaved sub-second times in the open copy.
6. **What each program shows of the seal group**: whether two seals of one node appear as a
   duplicate and whether a person is invited to delete one.

Assumed statements about other programs in this critique, collected: that a person reads two seals
of one title as a duplicate (2.1); that a seal binned on a phone arrives outside the seal group in a
KeePassXC merge (2.1, section 3); that Keepass2Android and KeePassDX give 2.2's result as KeePassLib
does (2.2, section 3); that a single installation plus an overwrite and a third copy gives 2.4's
sequence (2.4); that every program keeps an empty-valued custom string (2.9); that entries with
attachments above 6 MiB keep no history (2.7).

## 6. The probe behind the [M mine] marks

- Source: `spikes\storage\probes\rules-on-node\survival\probe\Program.cs`,
  compiled with the .NET Framework's own `csc` against a copy of
  `.keepass\2.62\KeePass.exe` (SHA-256
  `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`, the same file the design's
  probe used). No window, no other program started.
- Output: `spikes\storage\probes\rules-on-node\survival\out\survival.txt`; databases under
  `spikes\storage\probes\rules-on-node\survival\work\`. Ran in 3.3 seconds.
- Base: the design's probe's base (group U with a rule entry, X ruled R0, Y and Z unruled, a
  KeePassGate group holding the seals) plus an unruled group W. Each case makes copies A and B,
  merges with Synchronize, Overwrite if newer, Overwrite existing and Keep existing in both
  directions (C3, C6 and C7: Synchronize only), saves, reopens, and reads with both rules.
- Cases: **C1** scenario 01, then S1 removed for good, binned, or moved to the root; **C2** scenario
  05, then Y's seal removed; **C3** scenario 07, then the KeePassGate group deleted for good;
  **C4** U deleted for good in A with KeePass's deletion records, the design's prune at +21 with and
  without records, N added to U in B at +30; **C5** W ruled and unruled in A, ruled the same in B
  later; **C6** scenario 01 with a 1.5 MiB attachment and a 1 MiB history limit; **C7** the design's
  01, 05 and 07 under both rules.
- Results: C1 D gives R0 in 6 of 8 cells, V refuses in 8; C2 D gives silence in 6 of 8, V refuses
  in 8; C3 D gives silence for X, V refuses; C4 with records gives silence in 5 of 8 under both
  rules, without records refuses in those 5; C5 gives silence in 8 of 8 under both; C6 refuses with
  an empty history; C7 is unchanged between D and V.
