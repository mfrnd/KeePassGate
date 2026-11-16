# Critique of `grant-only`: does nothing another program does widen?

Written 2026-11-15 against `option-grant-only.md` as it stands at 20:06, KeePassGate master
`a42e38aa`, KeePass 2.62. The lens is the design's central claim: with rules that can only add, a
lost rule can only narrow, a copy that carries no rule is never wider than its original, and "a
planted or stale rule is the only way to widen". I walked every save, edit, copy, move, delete and
merge of KeePass, KeePassXC, Keepass2Android, KeePassDroid and KeePassDX that the ground reports
and `measured-android.md` describe through the model and its store, looking for a sequence that
ends with a client reaching what the operator did not mean. Security of the store key and of the
API guard belongs to the security critic and is named here only where a survival sequence ends in
it.

## How statements are marked

- **[M here]**: run for this critique from a console, over databases I made with invented content.
  `...` below is
  `spikes\storage\probes`.
  Probe `spikes\storage\probes\grant-only\survival\probe\Program.cs`, output `spikes\storage\probes\grant-only\survival\out\survival.txt`,
  databases under `spikes\storage\probes\grant-only\survival\work`. Section 8 says what it is and is not.
- **[M design]**: the design's own probe as its text and `spikes\storage\probes\grant-only\design\out\*.txt` record it. I
  read the outputs and the fold (`spikes\storage\probes\grant-only\design\probe\Program.cs`); I did not re-run them.
- **[M android]**: `measured-android.md`, by section.
- **[M keepass]**, **[M keepassxc]**, **[M spike]**: measured in that ground report, or in
  `spikes/revocation-merge` as `ground-measured.md` digests it.
- **[R here]**: read by me in the KeePass 2.62 source under `spikes\storage\probes\ground-keepass\src`, file and line.
  **[R android]**, **[R keepass]**, **[R keepassxc]**: read in that ground report.
- **[A]**: assumed. Neither run nor read.

## 1. Verdict

**Viable with changes.** The claim holds for the records. A grant that a program deletes or
fails to copy only narrows, a copy beside its original is never wider, and a node displaced to the
root is never wider unless the root is granted. Nothing I ran contradicts the design's zeros there
(0 of 3,175, 0 of 4,428, 0 of 8,576 [M design]). This also removes the failure that matters most
for a phone. KeePassDroid deleting every rule on a save no longer reaches any
rule, because nothing of the plugin's is in node custom data, and KeePassDroid has no move and no
merge [M android 3.1]. Short of a whole-file overwrite, nothing KeePassDroid does can widen under
this model. Under today's store it widens at every save.

The claim does **not** hold as the design words it. Four kinds of sequence widen with no planted
grant and no stale grant:

1. **P1, serious.** A row is the union of its grants' letters, so a switch written in one copy's
   grant applies to the verbs another copy granted on the same node. Two ordinary decisions made
   apart (read without protected values in one copy, write protected values in another) merge
   into reading the password: 18 of 18 cells, and 18 of 18 for the write form [M here, S1, S1b].
   Today's store and `rules-linked` do not do this. The change is small: bind a switch to the
   grant record, not to the row.
2. **P2, serious.** The tree is the policy, and the tree is the weakest carrier in the file. A
   merge can put a node under a grant in a copy where it was never under one: a place from one
   copy plus a grant from another, 14 of 18 cells [M here, S2]. This contradicts the design's
   "a merge invents no place". KeePassXC's two-way merge undoes a move in the second file and
   KeePass's Synchronize does not heal it, in 4 of 4 releases [M here, S3]. KeePassDX puts a moved
   entry back at equal times [M android 4.3]. The later of two moves wins by stamp, not by real
   time [M design 11; A for clocks]. The design names part of this itself (its 4.2, "worse").
   The change is the middle form's mark, or an operating rule that never narrows by a move.
3. **P3, serious.** The only take-aways a phone can make are hand acts on grant entries: delete
   one, or recycle it. A KeePassXC merge brings a deleted grant back, into the copy that deleted
   it as well (8 of 8). KeePass's preselected import (Create new IDs) brings it back in both
   directions. A recycled grant counts again in the copy KeePassXC merges into (4 of 4), because
   that copy's bin is an ordinary group [M here, S4, S4b]. The tab's withdrawal stays cut in all of
   these cells [M design], but the tab exists only in KeePass on Windows. The change: let every
   refusal-only trace of a take-away (a deletion record, an edited record, a grant moved out of
   the plugin's group) withdraw the grant.
4. **P4, serious and shared.** A whole file written over another (KeePassDroid's every save over
   a changed file) brings back every grant taken away, and every node moved out from under a
   grant, since the phone loaded the file. Every in-file option has this. It does not decide
   between them; question 1 of `EVALUATION.md` does.

Nothing is fatal in the sense of the task. P1 to P3 each have a change named below, and P4 is the
one loss no change inside the file can address. **If the project decides that KeePassDroid's
overwrite must be survived**, P4 becomes fatal, for this option and every other in-file option
alike. Five minor problems follow.

Against `rules-linked` this model is better on copies, on lost and deleted rules, and on refusals
(it has none). It is worse in P1 and in P2. A node can no longer carry its own narrowing, so
everything that keeps a node out of reach rests on where the node is. Section 5 has the
comparison sequence by sequence.

## 2. What survived the attack

| Claim | Result | Basis |
| --- | --- | --- |
| A grant lost to a deletion, a failed copy or a merge only narrows | held; I found no sequence in which a grant's absence opens anything | [M design, 0 of 3,175]; by the fold, which only adds |
| KeePassDroid's deletion of node custom data no longer touches any rule | held: nothing of the plugin's is on a node; KeePassDroid keeps entries, custom strings, deletion records and database custom data with its times | [M android 3.2]; by design |
| KeePassDroid has no move and no merge, so nothing it does short of an overwrite changes the tree or the store | held | [M android 3.1] |
| Keepass2Android saves and edits keep entries and custom strings, so they keep grant and withdrawal records | held for the carriers; these exact shapes not run on the emulator (the design's 12.3) | [M android 2.1] |
| A copy beside its original (Duplicate Entry, Duplicate Group) is never wider | held: the copy of Y with its own grant had only G's; the copy of G under the root had nothing | [M design, copies; 0 of 4,428] |
| A node displaced to the root is never wider, unless the root itself is granted | held | [M design, 0 of 8,576]; Keepass2Android's root placement [R android 1.5] |
| A withdrawal made on the tab stays cut through every merge of a stale copy | held in scenarios 02, 05, 15 and 17, all methods, four KeePassXC releases; with the entry deleted by hand, the key keeps it cut except in Keep existing into the stale copy and one KeePassXC 2.8.0 cell | [M design 4.3] |
| Two copies taking different letters from one row, or one removing a row while the other adds to it | the closed side, every cell | [M design 03, 04] |
| A grant planted from a foreign file | counted for nothing, 18 of 18 | [M design 07] |
| No KeePass dialog holds a rule; editing a user entry or group anywhere touches no rule | held by construction: the group dialog's and the entry dialog's write-back carry nothing of the plugin's | by design; [R keepass 4] for what the dialogs copy |

## 3. Problems

### P1, serious: switches leak between grants on one node, so two decisions made apart merge into one neither made

**What the design rests on.** Section 1.2: "A switch counts for the verbs of the row it is
written in", argued with `C......P` so that "a grant means what its own row holds and nothing
more". Section 5.2: "The row of a node for a profile is the union of the live letters of its
grants". Section 5.5: "In no cell ... did `agent` hold more than ... both decisions allow where
two were made apart". The design's fold does exactly that: `rows[profile] = was | m`
(`spikes\storage\probes\grant-only\design\probe\Program.cs:659`). Its scenario 01 put the two copies' grants on two
different nodes (C on G, `.R...LA.` on S), so the case below was never run.

**Why that does not hold.** The switch is bound to the row, and the row is a union across
grants. Two grants on one node from two copies therefore share their switches. The tab writes a
grant holding only the letters added (section 5.2: "letters added, one new grant holding those
letters"). An added switch then applies to every verb of the merged row, not only to the verbs
the operator saw beside it.

**Sequence S1** [M here]:

1. Entry Y in group G. `agent` holds `..U.....` on Y, so a program may update Y's plain fields.
2. On the laptop, the operator adds R on Y. The tab shows `.RU.....`: read and update, protected
   values excluded. The editor writes a grant holding `.R......`.
3. On a second copy, the operator adds P on Y, so that the program may also set Y's password. The
   tab shows `..U....P`. The editor writes a grant holding `.......P`. The second copy can be a
   second gate machine, or a copy opened from the synced folder before the laptop's save arrived.
4. Any merge of the two: the row is `.RU....P`. **`agent` reads Y's password.** KeePassLib's five
   methods in both directions and keepassxc-cli 2.4.2, 2.6.6, 2.7.12 and 2.8.0 in both
   directions: 18 of 18 cells.

**Sequence S1b** [M here]: G grants `.R...L..`. One copy adds C on G (the tab shows `CR...L..`:
programs may add entries and plain fields). The other copy adds P on G (`.R...L.P`: they may read
protected values). Merged: `CR...L.P`. **`agent` may create protected values in Y**, which the
design's own `C......P` example names as a decision that must be made explicitly. 18 of 18 cells.

**What the gate does.** It serves the union row with nothing shown. Each copy's tab showed a mask
that did not allow it, and the merged tab shows one mask that does.

**Against today and `rules-linked`.** Today a node's rule is one document taken whole by the
node's time: the result is one copy's rule, `.RU.....` or `..U....P`, and the other copy's change
is lost (here that loss narrows). `rules-linked` gives two concurrent versions their meet,
`..U.....` [M design `rules-linked` 05]. **Grant-only is worse here.** It is also the one
widening in this critique that needs no other program at all: KeePass's own Synchronize does it.

**How often.** It needs two copies in which grants on the same node change before they meet. Only
KeePass with the plugin writes grants. So it takes two gate machines, a sync conflict, or a grant
made on an older copy of the file [A]. The phone cannot cause it, but every merge carries it.

**Change.** Bind a switch to the grant record that carries it, not to the row. A grant record
holds verbs together with the switches that apply to them. A reach that needs a switch is
permitted only by one record holding both the verb and the switch. Put differently, a row's
permissions are the union, over its grants, of each grant's own (verb, reach) pairs, never the
pairs of the union of letters. The editor then writes:

- for an added verb, a grant holding the verb and the switches the row shows at that moment;
- for an added switch, a grant holding the switch and the verbs the row shows;
- for a withdrawal, as now, the letters cut from every grant of the row.

S1 then gives read and update of plain fields and update of protected values. S1b gives create
and read of plain fields, list, and read of protected values. Neither reaches what no copy
granted [A, by the rule; not run]. The access routes' answer becomes a list of grants per
profile, which section 6 of the design already concedes for rows on different nodes. Re-run S1
and S1b with it.

### P2, serious: the tree is the policy, and four kinds of sequence put a node under a grant the operator did not mean

**What the design rests on.** Section 4.2: "A node that was never under a grant in any copy is
not affected: a merge invents no place." Section 10: "nothing a phone program saves or merges can
lose a rule in the widening direction". Section 2 point 2: "Every witness, reference, refusal and
press in `rules-linked` exists to catch the 1,025." The design itself rates moves "worse" (4.2)
and names the later of two moves and KeePassDX's relocation.

**Why it is wider than stated.** Under this model a node is kept from a profile only by where it
is, and where it is is one value per node, decided by `LocationChanged` and last writer wins.
`EVALUATION.md` 2.3 found that kind of carrier unable to carry the store. Four ways in follow,
three of them not in the design.

**Sequence S2, a place from one copy and a grant from another** [M here]:

1. Group P holds nothing secret. The private group H holds X.
2. On the phone, the operator files X under P. In that copy P has no grant, so nothing is
   exposed, and no phone program shows grants anyway.
3. On the laptop, the operator grants `agent` `.R...L..` on P, whose contents in the laptop's
   copy are harmless.
4. Any merge that carries the move: X in P, under P's grant. **`agent` reads X.**
   - KeePassLib: Synchronize in both directions, and the three other methods and Create new IDs
     into the copy that holds the move. 6 of 10. In the other 4, the target is the copy without
     the move and those methods carry no move.
   - KeePassXC: 8 of 8.
   - Keepass2Android's merge is a port of KeePassLib's Synchronize [R android 1.1], and it carried
     a move in both directions [M android 2.2]. The phone's own merge should give the same [A].

In no copy was X ever under a grant. The merge combined a place from one copy with a grant from
the other. The design's sentence is wrong.

**Sequence S3, KeePassXC both ways** [M here]:

1. G is granted and X is in G.
2. On the laptop (copy A), the operator moves X out of G into H at +1h. That is the narrowing,
   under this model.
3. Copy B changes X's password at +2h.
4. `keepassxc-cli merge` of B into A: X stays in H, but takes B's location time, T0.
5. The same into B, from that result: the location times are equal, so X stays in G. **`agent`
   reads X in B**, in 4 of 4 releases.
6. KeePass's Synchronize keeps the target's place at equal location times: into the first result
   closed, into B open. The two files disagree for good, and whichever KeePass opens next decides.
   A direct merge of A into B is closed in every release, so the order of an ordinary two-way sync
   decides.

The ground report measured the regressed location time [M keepassxc 4, "Back and forth"]. This
run puts it under the model.

**The later move wins by stamp.** The design's scenario 11 [M design]: open in both directions
under Synchronize and under KeePassXC. Its "later" is the later `LocationChanged` stamp. A phone
whose clock runs ahead therefore wins with a move it made before the laptop's in real time, for
instance a tidy-up inside G a week ago against today's move out of G [A, from the read rule; not
run].

**KeePassDX 4.5.8.**

- An entry whose times are equal goes to the arriving copy's group. Measured with the copy that
  made the move open and an older copy arriving: the entry went back [M android 4.3, `Y3`], and
  KeePassDX kept the moved copy's location time. By S3's equal-time rule, a later KeePass
  Synchronize into that file then keeps it where it is [M here, S3 step 6].
- A group the open copy deleted is recreated from the arriving copy when any entry in it arrives
  [R android 1.5]. Suppose the operator empties a granted group by moving its secrets out, then
  deletes it. The group comes back with its grant, if a grant still names it (P7). The moved
  entries go back into it at equal times [A, combining a measured rule and a read rule].

**What the gate does.** It serves the node with its new place's grants, as granted, with nothing
shown. The audit records a grant.

**Against today and `rules-linked`.**

- **A node with no rule of its own:** equal. In every model it inherits from where it lands.
- **A node the operator would narrow by its own rule:**
  - Today that rule rides in node custom data, which loses in other ways. In S3, KeePassXC takes
    B's newer entry whole with B's older rule, so today loses the narrowing at the first merge
    [M spike, entry rules in KeePassXC]. Today is no better.
  - Under `rules-linked` the rule is an entry in the plugin's group and the anchor on X never
    changes, so S2 (with X narrowed), S3, scenario 11 and the KeePassDX relocation all stay
    closed [M design `rules-linked` 04, 09 to 11; by its fold].
- **So grant-only is worse than `rules-linked`** for anything that must stay out of a profile's
  reach whatever happens to the tree. Here, that is everything the operator narrows.

**How often.** S2 needs a move on one side and a grant on the other before they meet.
Keepass2Android offers Move, and its user sees no grants [M android 2.1, step 6]. KeePassDroid
offers none. That the operator files entries on the phone is [A]; that each such move is a policy
change made without seeing the policy is the model's own rule.

**Change.** One of:

- **The middle form's withheld mark** (the design's 1.5) on every node that must stay out of
  reach. It travels with the node, so no move and no merge can place a marked node under a grant
  that reaches it. Its price is the reference on the node that grant-only set out to remove, with
  the design's own residues: 847 of 1,723 wider when the mark's record and the reference are both
  lost, and 541 when a copy drops the reference [M design 1.5].
- **Short of the mark, an operating rule written into the documentation.** Take a node out of a
  profile's reach by withdrawing the grant above it on the KPG tab, which the design measured as
  robust (scenario 15), and never by moving it. The documentation must say what a move is worth:
  - carried as a single move by KeePass's Synchronize, KeePassXC and Keepass2Android;
  - not carried by the other three KeePass methods;
  - lost to a later stamp in another copy;
  - undone by KeePassDX at equal times;
  - lost in the second file of a KeePassXC two-way merge;
  - combined with another copy's grant.

Correct section 4.2's "a merge invents no place" and section 10's phone sentence.

### P3, serious: a take-away made by hand is undone by KeePassXC and by KeePass's own import, and it is the only kind a phone can make

**What the design rests on.** Section 4.3: "the one datum whose loss widens is the withdrawal".
Section 5.4: "A grant found in the recycle bin does not count, and the plugin's next write turns it
into a withdrawal", with "Recycling a grant by hand took it out of force [M here]". Section 5.1:
a grant counts "outside the recycle bin. Its place otherwise means nothing".

**Why it is wider than stated.** The gate runs only in KeePass on Windows, so the KPG tab and its
withdrawal exist only there. On a phone, there are two ways to take a profile's access away:

- move the secret (P2);
- delete or recycle the grant's entry in the plugin's group, whose title names the profile and
  the mask (section 5.1).

A withdrawal needs no check, so it could be typed by hand, but it needs the node's identifier in
hex, which neither phone program shows [A]. A deletion leaves a deletion record and a recycle
leaves an entry in the bin; neither is a witness the store reads.

**Sequence S4, the grant's entry deleted** [M here]:

1. G is granted and X is in G.
2. In copy A, the grant's entry is deleted for good, with a deletion record. Keepass2Android and
   KeePassDroid write one when deleting from inside the bin [M android 2.1, 3.2]. `agent` cannot
   read X in A.
3. B is an older copy that changed something else later.
4. The merges:
   - KeePassXC: **the grant is back**, into A as well. 4 of 4 releases, both directions.
   - KeePass's Create new IDs: the copy of the grant arrives under a new identifier. It is the
     same grant by its `KPG.Id` (section 5.2) and it is **in force in both directions, A
     included**. This is File then Import's preselected method and the default of the trigger
     "Import into active database" [R keepass 2.4, 9].
   - Synchronize: closed in both directions. The three other methods: closed into A, open into B,
     which never learns.

**Sequence S4b, the grant's entry recycled**, in a database that had no bin yet, so the delete
makes one, as KeePass and KeePassXC do [M here]:

- KeePassXC into B: the bin arrives as an ordinary group [M keepassxc 2, item 10], and the grant
  in it counts. **Open, 4 of 4.** Into A: closed.
- Create new IDs: **open in both directions.**
- Synchronize: closed, since it carries the bin pointer.

**What the gate does.** It serves the grant that the operator took away on the phone, with
nothing shown.

**Against the tab, today and `rules-linked`.**

- **The tab's withdrawal:** stayed cut in every cell of the design's scenarios 02, 05, 15 and 17
  [M design].
- **Today:** no grant can be deleted by hand on a phone. A rule removed in KeePass's own dialog
  moves the node's time and wins most merges.
- **`rules-linked`:**
  - A version deleted by hand refuses through its record, until KeePassXC brings it back, after
    which it is in force again: equal under KeePassXC.
  - Its displaced import copies are not in force: better under an import.
  - Its rule that "the place decides nothing" means a recycle there takes nothing away at all:
    grant-only is better in the copy that recycled.

So this is shared in kind. But it falsifies "a planted or stale rule is the only way to widen",
and it sits where the operator will act in an emergency: on the phone, away from the desk [A].

**Change.** Let every refusal-only trace of a take-away withdraw the grant, from any source:

- **(a)** Give a grant's entry an identifier derived from its `KPG.Id`, and read a deletion record
  naming that identifier as a withdrawal of the id. KeePass, Keepass2Android and KeePassDroid write
  such records. KeePassXC keeps the target's records beside what it brings back
  [M keepassxc 2, item 1].
- **(b)** A grant record that names an id and does not verify (edited by hand) withdraws that id.
  On a phone, "edit the grant's title" then becomes a take-away that no copy of the unedited
  record can undo.
- **(c)** Count a grant only inside the plugin's own group. `EVALUATION.md` 5.1 rule 2 requires
  it ("what grants counts only in place"), and this design dropped it ("its place otherwise means
  nothing"). A grant moved into any group, a designated bin or not, then counts for nothing.
- **(d)** The plugin's next write turns each withdrawn id into a withdrawal and its key.

With (a) to (c), S4 into A and S4b into B stay closed [A, not run]. What remains:

- KeePassXC merging into the copy that never saw the deletion record (S4 into B) keeps the grant
  until that copy meets the deleting one in KeePass.
- Database Maintenance's "Remove deleted objects information" removes the witness (a) relies on
  [R keepass 5].

Measure S4 and S4b with the changes.

### P4, serious and shared: a whole file written over another

KeePassDroid writes its open copy over the file at every save, whatever changed underneath it
[R android 4.2, `SaveDB.java:54-75`]. Keepass2Android offers "overwrite", and KeePassDX overwrites
within its ten-second window [M spike `keepPC`].

**Under this model** everything decided since the phone loaded the file is gone:

- every grant made since: narrower, harmless;
- every withdrawal made since: **wider**;
- every node moved out from under a grant since: **wider**.

The design's section 9 lists it. Under today's store and `rules-linked` it is the same in kind:
narrowings made since are lost, and so is the anchor of a node first ruled since.

No change inside the file meets it. It is `EVALUATION.md` question 1: an operating rule
(KeePassDroid saves only a copy it opened after KeePass's last save, or the phone merges with
Keepass2Android), or a store outside the file. It does not decide between the in-file options.

### P5, minor and shared: a withdrawal that lost both witnesses

The design's own residue (4.3): the withdrawal's entry deleted by hand, then a KeePassXC 2.8.0
merge of an older copy with a slow key derivation removed the key as well, and the withdrawn grant
was in force again (1 of 8 cells) [M design]. With a fast key derivation KeePassXC merges no
database custom data at all when both loads fall in one second [M spike], so the key never
reaches a copy that lacked it. It is the residue `EVALUATION.md` accepts for revocations, and it
is equal to `rules-linked`'s P2 (version and record lost, then KeePassXC). Nothing to add but the
honesty of section 4.3, which is there.

### P6, minor: a grant follows an identifier, and KeePass's repair decides which node keeps it

KeePass checks for duplicate identifiers at every save (`PreSavingEx`,
`MainForm_Functions.cs:3697-3701`) and at open (`:2612`). Its repair keeps the identifier on the
first node in pre-order and gives every later one a new identifier (`PwDatabase.cs:1881-1917`)
[R here]. KeePassDX offers a similar fix on load [R android 4.2]; which node it renumbers was not
read.

A grant names only the identifier (section 5.1). If two nodes ever share one and the other node
comes first, it takes the grant: wider there, narrower at the original. Two nodes under one
identifier need something outside KeePass, a hand-edited XML or a tool [A].

Today each node keeps its own rule in its custom data. Under `rules-linked` the renumbered node
carries the anchor and is limited. Change: record the node's kind (group or entry) and its title
at grant time, and list on the Profiles page any grant whose node no longer matches.

### P7, minor: the grants of a node that is gone should be withdrawn, not deleted

Section 5.4: "The plugin may remove the grants of a node that is gone." Done by deletion, the
grant comes back with the node in every sequence that brings the node back:

- KeePassXC brings back a deleted entry or group with everything in it [M design 14;
  M keepassxc 2];
- KeePassDX recreates a deleted group when an arriving entry needs it [R android 1.5].

The outcome equals today's, where the node's own custom data comes back with it. Done as a
withdrawal it stays cut, at the cost of one record. Change: the prune writes a withdrawal and its
key.

### P8, minor: KeePassDX puts a recycled grant back at equal times

Keepass2Android's and KeePass's recycle leaves the entry's modification time alone
[M android 2.1; R keepass 2.2]. A KeePassDX "Merge from" with an older copy arriving moves an
entry whose times are equal into the arriving copy's group [M android 4.3, for an entry; not run
for a grant]. A grant recycled by hand is then back in the plugin's group and counts, even with
P3's change (c). It counts until the plugin's next write turns a grant it saw in the bin into a
withdrawal. That write only happens if KeePass with the plugin saw the bin in between. Change: P3's
(a) does not reach it (a recycle writes no deletion record). Name it in section 9.

### P9, minor, for the security critic: the store key outlives a master key change

Section 5.3 carries the store key across Recreate, and every copy made before a change of master
key holds it. A file built from such a copy can write grants that count, if the operator merges
it. `EVALUATION.md` 5.1 rule 3 proposes the opposite: a MAC under the served authority's gate key,
signed again at Recreate, which "shuts out the holder of an old copy once the authority has been
recreated". In this model a planted grant is the one widening the design admits, so which of the
two the store uses is load bearing. The security critic should weigh it.

## 4. Every program, save, edit, copy, move and merge

"Wider" means more than the operator's last decision, or more than both decisions allow where
two were made apart.

| Program and act | What it does to the store and the tree | What the gate then answers | Basis |
| --- | --- | --- | --- |
| KeePass, save | records kept, KDBX 4.0 without a withdrawal key | unchanged | [M design] |
| KeePass, edit of a user entry or group, history restore | nothing of the plugin's on the node | unchanged | by design |
| KeePass, hand edit of a grant entry | the check fails | that grant grants nothing (narrower) | [M design 4.1] |
| KeePass, Duplicate Entry, Duplicate Group | the copy beside its original, a new identifier | never wider | [M design] |
| KeePass, copying drag or paste into a granted group | the copy where it lands | open: the operator's act, conceded by the design (section 3, "Cannot be had") | [M design, Q into G] |
| KeePass, Delete Duplicate Entries | deletes the first of two equal entries in tree order | the survivor has what its place gives; never wider | [R here `PwDatabase.cs:1995-2029`]; [M design] |
| KeePass, Synchronize | union of records; deletion records carried; moves by location time, the target's place kept at equal times | union of grants with P1's switch leak; S2 open; scenario 11 open; withdrawals hold | [M here S1, S2, S3]; [M design] |
| KeePass, Overwrite if newer, Overwrite existing, Keep existing | union of records; no moves carried, no deletions copied | P1; S2 open into the copy that moved; a move made in the other copy not carried | [M here]; [M design 09 to 13] |
| KeePass, Create new IDs (preselected import; trigger default) | copies of every node at the root, copies of every record | copies at the root closed; **a grant deleted or recycled by hand comes back** (P3); P1 | [M here S4, S4b]; [M design] |
| KeePass, Database Maintenance | clears every deletion record; deletes old history | nothing today; removes P3's witness (a) | [R keepass 5] |
| KeePassXC 2.4.2 to 2.8.0, save | every record kept | unchanged | [M design 5.4] |
| KeePassXC, merge | union; never applies a deletion; a foreign bin arrives as an ordinary group; a move survives only until newer content arrives, then takes the other copy's location time | P1; S2 open; **S3 open in the second file**; deleted grant back (P3); recycled grant counts in the other copy; withdrawals hold but for P5 | [M here S1 to S4b]; [M keepassxc 2, 4] |
| KeePassXC, Clone | a copy with a new identifier, beside | never wider | [R keepassxc] |
| Keepass2Android 1.15-r6, save, edit, delete | every carrier kept; a delete recycles, a second delete writes a deletion record | unchanged; a hand delete of a grant narrows until P3 | [M android 2.1] |
| Keepass2Android, Move | location time moved, no previous parent | a policy change made without seeing the policy (P2) | [M android 2.1, step 6] |
| Keepass2Android, merge | KeePassLib's Synchronize ported; carried a move both ways; the arriving copy whole at equal times | as KeePass's Synchronize: P1, S2, scenario 11 [A by the port] | [M android 2.2]; [R android 1.1] |
| Keepass2Android, overwrite | one file whole | P4 | [R android 4.2] |
| KeePassDroid 2.7, save, edit, delete | every carrier kept; node custom data deleted, which no longer matters | unchanged | [M android 3.2] |
| KeePassDroid, move, merge | not offered | nothing | [M android 3.1] |
| KeePassDroid, save over a changed file | overwrites always | **P4** | [R android 4.2] |
| KeePassDX 4.5.8, save | every line kept but the root's times | unchanged | [M android 4.1] |
| KeePassDX 4.5.8, merge | newer whole; at equal times the entry goes to the arriving copy's group; missing groups recreated; arriving deletions applied, none kept | a move undone (P2); a deleted granted group recreated (P2, P7); a recycled grant put back (P8); P1 | [M android 4.3]; [R android 1.5, 3.1] |
| KeePassDX 3.2.0, merge | drops an arriving entry whose group is missing | a dropped grant narrows; a dropped withdrawal keeps its key, which merges by per-key time | [R android 1.5]; [M spike, keys] |
| A whole file written over another | nothing merges | P4 | [R android 5.5] |

## 5. The sequences the task names, against today and `rules-linked`

| Sequence | Grant-only | Today (0.1.0) | `rules-linked` | Grant-only is |
| --- | --- | --- | --- | --- |
| A grant taken away on the tab, an older copy merged in | cut, every cell [M design 02, 05, 15, 17] | the newer node whole; a KeePassXC merge keeps the target's rule, a KeePassDX merge puts the arriving one [M spike] | the newest version; a record refuses a lost one [M design `rules-linked`] | better than today, equal to `rules-linked` |
| The same, its entry deleted by hand and no deletion record | cut while the key is read; open again in 1 of 8 KeePassXC cells [M design 4.3] | not applicable | the older rule after version and record are lost, then KeePassXC [M critic `rules-linked-survival` P2] | equal to `rules-linked` |
| A grant taken away by hand, the only way on a phone | **open again** under KeePassXC (8 of 8) and Create new IDs (4 of 4); a recycle undone into the other copy [M here S4, S4b] | not possible on a phone | a deleted version refuses until KeePassXC brings it back, then in force; a recycle takes nothing away | equal under KeePassXC, worse under an import, better for a recycle in its own copy |
| A node carried into a granted group: the later of two moves | open [M design 11] | open for a node with no rule; a node's own narrowing travels with it | a node's own narrowing holds wherever it lands | worse than both, for a node the operator would narrow by its own rule |
| A move undone by KeePassXC's two-way merge | **open in the second file** [M here S3] | a narrowing rule on X lost at the first merge (the newer entry whole) [M spike] | X's own rule holds | better than today, worse than `rules-linked` |
| An entry put back into the arriving copy's group at equal times (KeePassDX) | open [M android 4.3] | the arriving copy's rule put on the node [M spike] | the node's own rule holds; a link moved suspends a grant only [M critic `rules-linked-survival` P7] | worse than `rules-linked` |
| A deleted group brought back | open with its grant, as it was before the deletion [M design 14]; recreated by KeePassDX [R] | the same, with its rule in its custom data | the same, versions back | equal |
| A place from one copy, a grant from another | **open**, 14 of 18 cells [M here S2] | open for a node with no rule | open for a node with no rule; closed if it carries its own | equal, and worse for a node that needed its own narrowing |
| A group's rule naming an identifier that a program reuses or reassigns | the grant follows the identifier to whichever node keeps it [R here, P6] | each node keeps its own rule | the renumbered node carries the anchor and is limited | worse (minor) |
| Two copies changed grants on one node apart | **wider than either**: switches leak, 36 of 36 cells [M here S1, S1b] | one copy's rule whole, the other lost | what both allow [M design `rules-linked` 05] | worse than both |
| A copy beside its original; a copy in an import | never wider [M design] | carries its rule | limited, one reference deep; Delete Duplicate Entries opens it [M critic `rules-linked-survival` P1] | better |
| A rule lost (KeePassDroid's save, a hand deletion) | narrower; nothing refuses [M design] | wider wherever it narrowed [M spike; M android] | refuses [M design `rules-linked`] | better than both |
| A node displaced to the root | never wider [M design] | wider out of a sealed group | as today for a node with no rule | better |
| A whole file written over another | withdrawals and moves since then lost | narrowings since then lost | narrowings and anchors since then lost | equal |

## 6. Claims of survival and their basis

**Found wrong or incomplete by measurement here:**

1. Section 5.5, "more than both decisions allow where two were made apart": wrong for two grants
   on one node (S1, S1b). Scenario 01 put the grants on two nodes.
2. Section 4.2, "a merge invents no place": a merge combines a place from one copy with a grant
   from another (S2).
3. Section 4.2's table, scenario 10, KeePassXC "closed in both": true for one merge. In a two-way
   sync, the second merge leaves the node under the grant, and KeePass's Synchronize then keeps
   each file's place (S3).
4. The option's own summary, "a planted or stale rule is the only way to widen": P1, P2 and P3
   each widen with neither.
5. Section 5.4, "A grant found in the recycle bin does not count": only while the file's bin
   pointer names that group. KeePassXC does not merge the pointer (S4b), and an import's copy sits
   outside any bin (S4b, Create new IDs).
6. Section 10, "nothing a phone program saves or merges can lose a rule in the widening
   direction": true of records. Not of the tree, which is the policy here (P2), and not of a
   phone's own take-away (P3).

**Resting on reading, to be measured** (most important first):

7. Keepass2Android's merge giving S2 and scenario 11 as KeePassLib does, and its Move across a
   granted group's border merged with a KeePass copy that moved the same entry (the design's
   12.2).
8. KeePassDX 4.5.8 recreating a deleted granted group and moving equal-time entries back into it
   (P2), and moving a recycled grant back (P8).
9. Grant and withdrawal records through Keepass2Android's and KeePassDroid's saves, edits and
   deletions on the emulator (the design's 12.3). The carriers are measured; the shapes are not.
10. KeePassDX's duplicate identifier fix: which node it renumbers (P6).

**Resting on assumption:**

11. That an operator takes access away on the phone by deleting a grant entry (P3), and files
    entries on the phone into groups a profile may be granted (P2).
12. That two copies change grants on one node before they meet (P1): two gate machines, a sync
    conflict or an older copy.
13. That the changes named for P1, P2 and P3 do what their rules say. None was run.

## 7. What the design should change in its text

- Section 1.2 and 5.2: a switch bound to its grant record (P1), and the access routes' answer as
  grants per profile.
- Section 4.1's table: the row "Two copies changed the same node's rule" with S1 and S1b.
- Section 4.2: S2 in place of "a merge invents no place"; S3 beside scenario 10; "later" as the
  later stamp.
- Section 4.3: "the one datum whose loss widens is the withdrawal" holds for the tab's take-away.
  A take-away by hand is P3.
- Section 5.1 and 5.4: whether a grant counts outside the plugin's group (P3 (c)), and what the
  prune of a gone node writes (P7).
- Section 6, the documentation: what a move is worth (P2), and how to take access away from a
  phone (P3).
- Section 9: P1, S2, S3, P3 and P8 beside the items listed.
- Section 10: the phone sentence, and "turns three failure classes ... into cannot widen" kept
  for the records, with the tree named as the place the narrowing went.

## 8. What was run for this critique

- **The probe.** `spikes\storage\probes\grant-only\survival\probe\Program.cs`, compiled with the .NET Framework's
  own `csc` (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) against a copy of
  `.keepass\2.62\KeePass.exe` (SHA-256 `AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`,
  equal to the original's).
  - Its store and fold are the design's, written again in the same form: grant and withdrawal
    entries with the design's strings, a grant counted with this store's identifier and outside
    the bin the file's pointer names, `rows[profile] = was | m`, a withdrawal counted wherever it
    is found and through its key.
  - Its resolver is the design's `GrantOnly`: some node on the path holds the verb, and the switch
    in that same row where the reach needs one.
  - Moves are made as KeePass makes one: the location time moved, the previous parent kept, the
    modification time unmoved. Edits as KeePass's entry dialog makes one: a backup first. A
    deletion for good adds a deletion record. A first recycle makes the bin and sets the pointer.
  - Databases with invented content, Argon2d with 2 iterations and 1 MiB, password `probe-only`;
    every result saved and read back with KeePassLib 2.62.
- **Merges.** `PwDatabase.MergeIn` under Synchronize, Overwrite if newer, Overwrite existing, Keep
  existing and Create new IDs, each in both directions. `keepassxc-cli merge -s` from 2.4.2,
  2.6.6, 2.7.12 and 2.8.0, the copies the KeePassXC ground report verified, each in both
  directions, and for S3 in the two-step order. The whole run took 12 seconds.
- **Read here.** KeePass 2.62 source under `spikes\storage\probes\ground-keepass\src`: `PwDatabase.cs:1858-1917`;
  `MainForm_Functions.cs:2612`, `:3697-3701`. The design's probe source and output files.
- **What the probe is not.** It is not the plugin, KeePass's window, KeePassXC's window or any
  Android program. It writes the store's check as a plain store identifier compared for equality,
  as the design's probe does, not an HMAC. It does not model the middle form.
- **Not run.** No window, no KeePass process, no Android program or emulator, no test tier, no
  `build.ps1`. Nothing was written to either worktree or to the main checkout.
