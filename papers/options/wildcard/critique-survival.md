# Critique of option `wildcard`, lens: survival across programs

Written 2026-11-15 against `option-wildcard.md` ("one sealed snapshot of everything, replaced whole,
with deletion records as witnesses") and the six ground reports, on KeePassGate master `a42e38aa`.
The question asked of the design: walk every save and every merge of every program the ground
reports cover, datum by datum, and look for a sequence that loses, rolls back, duplicates or
resurrects something, then say what the gate does.

## 0. How statements are marked

- **[M here]**: measured for this critique, today, from a console, over invented databases. Scripts:
  `spikes\storage\probes\wildcard\survival\scripts\`
  (`Run.ps1`, `Run-e4.ps1`, `kpops.ps1`, `kw.js`). Output: the `out\` directory beside it
  (`run.txt`, `e4.txt`, `group-records.txt`). Section 9 describes the runs.
- **[M wildcard]**: measured by the design's own probe; I re-read its recorded output,
  `spikes\storage\probes\wildcard\design\out\summary.txt`, by line.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M measured]**: measured in that ground report.
- **[R here]**: read by me from source, file and line named. KeePass 2.62 is the source archive the
  `keepass` ground report verified (`spikes\storage\probes\ground-keepass\src\`). kdbxweb is the clone the `others`
  ground report made, whose format files that report found identical to 2.1.1. Strongbox is the
  clone at `c70fc7b` (1.65.0) the same report made.
- **[R android]**, **[R keepassxc]**, **[R others]**, **[R keepass]**: read from source in that ground
  report, which names the tag, file and line.
- **[A]**: assumed. Neither run nor read.

"The design" is `option-wildcard.md`, cited by section or line.

## 1. Verdict in short

**Viable with changes.** The core holds up well under this lens: an entry written once and never
changed is the one thing every merge measured adds and every save measured keeps, and the join
makes two copies that both wrote come out the same in both directions [M wildcard, 136 merge
cells]. I found no sequence in KeePass's five merge methods or in KeePassXC 2.4.2 to 2.8.0 that
defeats the snapshot itself.

Three things do not hold as written, and each needs a change:

1. **A deletion record naming the store group makes every kdbxweb (KeeWeb) merge drop the group and
   every snapshot in it, whatever the times.** The design reaches that record itself, through
   KeePass's Delete Empty Groups while a snapshot sits in the bin (a state it keeps in force) and
   through its own Create recovery after "gone". Measured: the store gone in both directions, and in
   one shape **a silent widening read as in force** (section 2.1).
2. **The second carrier is missing from the copies that need it.** A copy that took the snapshot
   through a KeePassXC merge or KeePass's three non-synchronizing methods holds no witness
   [M wildcard], and through a KeePassDX merge no deletion record at all [R android]. A hand deletion
   there leaves "behind", whose one offered remedy, **Use what is here**, re-admits a revoked client
   with nothing on the tab able to say so (measured, section 2.2), or "none", from which a stale
   copy brings the old state back into force silently.
3. **The profile list is joined by the higher number, which is not the later or the narrower
   answer**, so two copies that changed one name concurrently can bring back a name one of them
   took off (section 2.3).

Nothing here is fatal. And every Android cell in the design is read, not run, which the design says
itself (section 17.1); this critique adds what those cells would have to show (section 8).

## 2. Problems, most severe first

### 2.1 Serious: kdbxweb deletes the store with its group, and the design makes the record that triggers it

**The mechanism** [R here]. kdbxweb's `Kdbx.merge` unites both copies' deletion records first
(`lib/format/kdbx.ts:415-421`), then `KdbxGroup.mergeCollection` skips every group and entry whose
identifier is in that union, on both sides, with no look at any time
(`lib/format/kdbx-group.ts:213-216`, `:227-228`). A skipped group takes its whole subtree with it,
because only the kept groups are visited next (`:192-194`), and nothing records the entries that
went. KeePass does the opposite: a record naming a group that is present and not empty is dropped
from the list (`KeePassLib/PwDatabase.cs:1083-1096`, with `SafeCanDeleteGroup` at `:1101-1108`).
Strongbox 1.65.0 deletes only when the record is later and the item has no children
(`model/Sync/DatabaseMerger.m:505-516`) [R here]. So among the merges read, kdbxweb alone deletes a
non-empty group.

**Why the design meets it.** The store group has one fixed identifier in every database (design
3.1, line 114), and the design keeps a snapshot in force wherever it is: in the recycle bin, in
`General`, at the root (lines 121-125). While it is there the store group is empty, and KeePass's
**Delete Empty Groups** deletes any empty group and records it (`PwDatabase.cs:2094-2112`)
[R here]. The design's own table says Delete Empty Groups does "nothing: the group holds an entry
while a store exists" (line 496). That is not so in the states the design itself serves. Then
write step 3 "makes the group if it is missing" (line 357) under the same fixed identifier, so the
record and the group live side by side. And after "gone", the documented recovery, Create, makes
the group again under that identifier while the old record stays (deletion records never age,
[R keepass 5]).

**Sequence e1** [M here, `out\run.txt`, "e1"]:

1. Copy A: the snapshot `S2` is in the recycle bin (the design's set R; a delete in any program does
   this). The gate: in force.
2. A: KeePass, Tools, Database Tools, Delete Empty Groups. It removed one group, the store group,
   with a record `4B504747...01`. The gate: still in force.
3. A: the plugin's next write. `S3` in a store group made again under `4B504747...01`, newer than the
   record; `S2` deleted with its record. The gate: in force.
4. kdbxweb 2.1.1 merge of A with the stale copy B, in either direction: **no snapshot in the
   result**. The gate: **gone**, no gate for that database; the revoked `c1` still refused by its
   witness.
5. Controls: KeePass Synchronize both ways keeps `S3` in force and **drops the group's record**
   (`out\group-records.txt`); KeePassXC 2.4.2 and 2.8.0 keep `S3` in force, and 2.8.0 leaves the
   record in its target (2.4.2's records were not listed).
6. The record travels: kdbxweb merging the KeePass-repaired copy with the first kdbxweb result
   deleted the store again, gone. A KeePass Synchronize of a kdbxweb result with a copy still
   holding `S3` brings it back in force.

**Sequence e2** [M here, "e2"]: the store group deleted for good (records for it and its entries;
gone, witness holds), then a new store made under the same group identifier, as the design's
Create would. kdbxweb merged with the stale copy in either direction: **the new snapshot is not in
the result**. KeePass Synchronize kept it. (The probe's write does not add the old store's
identifiers to `over`, which the design's Create does, so its "behind" verdict on the new store is
an artifact of the probe; the measured fact is the snapshot's presence or absence.)

**Sequence e4, the worst shape** [M here, `out\e4.txt`]. The design's set C: both copies wrote.
A paired `c3` and changed E1's rule to `{"reader":["read"]}`; B revoked `c2`, changed E1's rule to
`{"reader":["list"]}` and took `writer` off the list.

1. A: its snapshot moved into the recycle bin, then Delete Empty Groups. No plugin write yet, so A
   holds a record for the store group and its head sits outside it. The gate: in force.
2. kdbxweb merge of A and B, either direction: B's group is skipped and **B's snapshot goes with
   it, with no record of its own**. The gate reads **in force**, A's state alone: `writer` on, E1
   with A's rule only. B's revocation of `c2` still holds, through the witness kdbxweb united.
3. The control, KeePass Synchronize: both heads stand, the join is right (`writer` off, E1 has
   what both versions allow, nothing for `reader`).
4. The plugin's next write over the kdbxweb result: one snapshot of A's state, and nothing left to
   show that B's existed.

**Outcome.** e1 and e2 fail closed, at the design's steepest cliff, with no hand deletion of the
store at all. e4 **widens** access with no sign: a profile B took off is back, and a rule both
copies narrowed differently reverts to one side's (E5, E6). The witness saved the revocation only
because revocations have one; profiles and rules have none.

**Fix.** (a) Never make a store group under an identifier a deletion record names: give each store
group a fresh random identifier, made again with a fresh one when it is missing, and find the store
by its snapshot marks as the design already does; the fixed identifier buys only that set X shows
one group, and set X is undecided anyway. (b) In the plugin's own write, drop any deletion record
naming a group that holds a snapshot, which is what KeePass's own merge does. (c) Keep the store
group from ever being empty while a store exists: an anchor entry written once with the store and
never replaced, so Delete Empty Groups cannot take it. (d) Correct the row at line 496. (a) to (c)
stop the record from arising in KeePass. They do not stop kdbxweb deleting a group that a hand
deletion in another copy recorded; that remains the cliff, failing closed only when no head sits
outside that group, so the design should say that a KeeWeb merge can lose a concurrent snapshot
silently, or state KeeWeb merges unsupported.

### 2.2 Serious: the witness is absent exactly where it would be the only carrier

The design rests E3 and E4 on "two carriers that fail under different programs: the snapshot
arrives in every merge measured, and the witness outlives the snapshot" (line 552), and its answer
to a hand deletion is that "revoked certificates stay refused by their witnesses" and a stale copy
"does not bring the store back into force" (line 488). Both hold in a copy whose deletion records
came from the plugin's own write or through a merge that carries records: KeePass Synchronize
[M wildcard], Keepass2Android [R android 3.1], kdbxweb [M wildcard], Strongbox [R here]. **They do
not hold in the copies that took the snapshot any other way**:

| Merge into the copy | Deletion records it receives | Basis |
| --- | --- | --- |
| KeePassXC 2.4.2 to 2.8.0, any direction | none | [M wildcard, `summary.txt` lines 39-42: `R0`] |
| KeePass OverwriteIfNewer, OverwriteExisting, KeepExisting | none | [M wildcard, lines 35-37: `R0`] |
| KeePassDX 4.5.8 and 3.2.0 | applied to older entries, then **not kept**, and the open copy is what the program saves | [R android 3.1, `DatabaseKDBXMerger.kt:304-323`] |

**Sequence e3, KeePassXC or an import method** [M here, "e3"]:

1. Copy A, KeePass with the plugin: `c1` revoked (`S2`, `D(S1)`, witness `W(c1)`).
2. Copy Y, stale (`S1`): A merged into Y by KeePassXC 2.4.2, KeePassXC 2.8.0, or KeePass
   OverwriteIfNewer. Y holds `S1` and `S2`, no records. The gate on Y: in force, `c1` refused by
   `S2` alone.
3. Y: `S2` deleted for good, as KeePassXC's `rm` twice does with a record [M keepassxc 6]. The gate:
   **behind**, `503 undecided`, which is right. The heads' state is `S1`'s: `c1` not revoked, and
   no witness in the file.
4. The KPG tab offers **Use what is here**, which joins "every snapshot that reads" (line 270): only
   `S1`. **`c1` is admitted again.** The tab "names what is missing" (line 258), which is an
   identifier; it cannot say that what is missing held a revocation, because the text is gone.

**Sequence, KeePassDX** [R android 3.1, outcome from the design's state table, not run]:

1. A phone with KeePassDX holds `S2` only: its own earlier merge applied `D(S1)` and kept no record.
2. On a desktop the store entry is deleted for good (`D(S2)`). The phone merges that file: `S2` is
   older than `D(S2)` and is removed, and `D(S2)` is not kept. The phone's file holds no snapshot,
   no record, no witness: state **none**, indistinguishable from a database that never had a store.
3. The phone merges an older file still holding `S1` (another device, a backup): `S1` arrives, with
   no record in the phone's file to name it. State **in force**, `S1`'s state, `c1` admitted.
4. The phone saves over the shared file, or the desktop takes the phone's file as the newer one.
   The gate serves the old state. No state on the tab shows it.

A desktop that **synchronizes** with that phone file instead keeps its own records and stays gone,
which is the design's case. The loss needs the file the gate opens to be the one that went through
the phone. In a shared-file setup with KeePassDX, it is: a merge saves the open copy over the file
[R android 3, the section's opening], so every such merge drops from the shared file every record
that arrived after the phone loaded it, witnesses included, until the plugin's next write puts them
back (design 6, line 339) [A, from R android 2 and 3.1: records read at load are kept, arriving ones
are not].

**Outcome.** The deletion that starts it is a person's, which the design counts as the cliff. What
the design understates is the result: not gone with witnesses, but a rollback, blind behind an
operator's press in one case and silent in the other. Section 14.3 lists only pykeepass as making
an unrecorded deletion; a KeePassDX merge makes one from a recorded deletion.

**Fix.** (a) Give revocations and retirements a carrier that travels through every merge, which
deletion records do not and entries do: one small entry for each, written once and never changed,
kept by the plugin until the certificate has expired (a retirement for good). Then a hand
deletion of the snapshot leaves them, and they arrive with the stale copy's merge like any entry.
(b) Whatever the carrier, **Use what is here must not re-admit on a missing snapshot's
account**: when what it adopts is older than a snapshot it cannot read or find, it should keep the
authority and the rules and put every client admitted only by the older state in front of the
operator again, rather than serve them. (c) Record in the design that E3 is not met across a
KeePassDX merge followed by a hand deletion, if (a) is not taken.

### 2.3 Serious: the profile join prefers the copy that changed a name more often

Design line 199: "a number: odd means on the list, even means taken off; adding or removing takes
the next number". Line 287: the join takes "the higher number". Line 553 claims E5 because "the
join never prefers an older number".

**Sequence** (from the design's own rule; the probe implements it as written,
`spikes\storage\probes\wildcard\design\probe\Program.cs:87-91`):

1. Both copies hold `writer` at 1, on.
2. Copy A, between two synchronizations: the operator takes `writer` off. 2, off.
3. Copy B, in the same interval: `writer` taken off and then put back by a pairing that approved
   it, or by the operator. 3, on.
4. Any merge, any program: both snapshots stand, the join reads 3. **`writer` is on in every copy.**
   Every client whose record names `writer` reaches what `writer` reaches again, including the
   clients A's operator meant to cut off.

A higher number means more changes, not a later one and not a narrower one. The two changes are
concurrent, and the design resolves concurrency for rules toward "what every version allows" (line
288) but for profiles toward whichever copy toggled more. The design's lattice check (line 278)
cannot catch it: its "widened" test (`Program.cs:946-952`) asserts the join equals the maximum,
which is the rule restated.

**Outcome.** A name taken off comes back: a widening, E5. Today's store loses this case too (one
copy's whole list), so it is not a regression, but the design claims to meet it.

**Fix.** Treat each name as the design treats a rule: every change a version naming the versions
it replaced, and two versions that never saw each other resolve to off. A pairing that lands in
that state is admitted to a name that grants nothing, which is the existing rule for a removed
name.

### 2.4 Minor: `gone` refuses nodes that KeePass's own merge legitimately kept, and forgets their rule

The plugin turns a rule into `gone` at its next write when "a deletion record names the node and
the node is not in the tree" (line 207). KeePass keeps a deleted entry whose other copy changed it
later, in every method into the deleting copy, and a deleted group into which the other copy added
an entry [M keepass 2.2].

1. A: a ruled entry E deleted for good (`D(E)`). Any plugin write, a pairing say: E's rule becomes
   `gone`.
2. B: E edited after the deletion.
3. KeePass Synchronize: E comes back, `D(E)` dropped. E refuses for every client, there and below.
   For a ruled group, B's new entry inside it refuses too.

The gate fails closed, so nothing widens, but clients are wrongly refused by an outcome KeePass
considers correct, and the operator must write the rule again from memory, since "the rule's text
is not kept". **Fix:** keep the last document inside the `gone` version and show it on the grant
editor as the rule to restore with one press.

### 2.5 Minor: a join can carry the list past 32 names, and then nothing grants anywhere

Above 32 names the list "reads as a list that will not read, which grants nothing" (line 287).
Copy A fills the list to 32; copy B's pairing approves a 33rd. After any merge the join holds 33
and every client in every copy reaches nothing until the operator removes a name. Today's tab
merge leaves out a name that no longer fits. **Fix:** at the ceiling, the join keeps the 32 names
with the lowest numbers or the operator's, and the tab names the one left out, as today.

### 2.6 Minor: the members that are never dropped, and a ceiling nobody set

Rules and profile names are never dropped (lines 199-200), a `gone` version stays for every ruled
node ever deleted, and every write adds a deletion record. The document has "a ceiling of its own"
(line 130) that the design does not give. If a snapshot reaches it, no write succeeds, and that
includes a revocation and a client giving its certificate up. **Fix:** name the ceiling, prune
`gone` versions whose node identifier has not been seen for a long time and whose record is
present, and refuse a pairing well before a revocation would be refused.

### 2.7 Minor: an edit to the store text propagates by its time, and the intact text is in history

An edit to `KPG.Store` in any program moves the entry's time, so in every merge afterwards the
edited, unreadable version wins whole and the intact one is kept only as a history item, in KeePass,
KeePassXC and Keepass2Android [M keepass 1a; M keepassxc 5; R android 1.1]. One stray edit on one
device stops the gate in every copy ("gone if it was the only one", line 489), and Create then
makes every client pair again, while the authority sits intact in up to ten history items. The
design reads no history on purpose. **Fix:** let Use what is here, and only it, look in the
snapshot entry's own history for a version that reads under the same identifier and key.

### 2.8 Minor: the remedy after Delete Duplicate Entries is understated

After an import with new identifiers, Delete Duplicate Entries deleted the original snapshot and
left the copy [M wildcard]. That it does the same to every duplicated user entry is [A], from the
snapshot's case and from its rule of deleting one of two equal entries whatever their custom data
[M keepass 9]. Use what is here then adopts the copied snapshot, and
every ruled entry whose original went is now an unruled copy, governed by its group. The design
(line 495) calls the duplicated nodes "the operator's to remove"; after Delete Duplicate Entries
they are the operator's entries, and what was removed was the rule. **Fix:** before Use what is
here adopts a copy, list the rules whose node a deletion record names while a node with the same
title and content stands elsewhere, and leave the gate undecided until the operator has seen it.

### 2.9 Minor: the lattice measurement does not model what the join's safety rests on

The 40,000 checks (line 278) ran on a model whose steps never prune (`Program.cs:898-921`): no
record or revocation dropped on expiry, no authority document removed at retirement, no rule
digests and no "lower by eight" (the design says the last). With prunes the law "joining a state
with any earlier state of its own line changes nothing" is false: pruned members come back, and
the design's safety there rests on dates and retirements, which is an argument, not the measured
law. And the profile check is the rule restated (section 2.3).

## 3. Datum by datum

What each datum survives, and the first sequence that breaks it. "Snapshot" means the datum lives
and dies with its snapshot.

| # | Datum | Holds through | Breaks | Gate then |
| --- | --- | --- | --- | --- |
| 1 | Authority | every save and merge measured [M wildcard]; two texts under one digest are undecided | with the snapshot: kdbxweb and a store group record (2.1); an edit by hand (2.7); a whole stale file (design 14.4) | gone, or in force on an old file |
| 2 | Retirement | union in the join, and a witness | in a copy without witnesses (2.2): a hand deletion, then Use what is here or a stale merge, serves a retired authority and its clients again (the shape of e3, not run for a retirement) | in force, wrong |
| 3 | Admission records | union by digest; a record beside its revocation refuses [M wildcard sets C, K] | a pairing in a snapshot kdbxweb drops (2.1, e4: had B paired, that client would be refused) | the client refused; fails closed |
| 4 | Revocations | union, and a witness [M wildcard set H] | the copies of 2.2 | in force, revoked client admitted |
| 5 | Standing answers | the higher number, at a tie the more refusing | the higher number is not the later answer (A4). A lift that wins sends the next renewal to a dialog and admits nothing by itself | a dialog where a refusal stood; minor |
| 6 | Profile list | the higher number per name | concurrent changes (2.3); kdbxweb dropping the snapshot that took a name off (2.1, e4: `writer` back on); more than 32 after a join (2.5) | widened, or nothing granted |
| 7 | Access rules | per node, every standing version, what all allow [M wildcard set C] | kdbxweb dropping one side (2.1, e4); eight changes against one (design 14.6); a copy of the node (design 14.1); `gone` over a kept node (2.4) | widened, or wrongly refused |
| 8 | Recycle bin markers | not designed (line 201) | | |

## 4. Program by program

| Program | Saves | Merges | What this critique adds |
| --- | --- | --- | --- |
| KeePass 2.62, Synchronize | keeps entry, strings, flag, records [M wildcard] | carries records; drops a record naming a present, non-empty group [R here, M here] | the repair for 2.1 in the copies it writes |
| KeePass 2.62, OverwriteIfNewer, OverwriteExisting, KeepExisting | | snapshot arrives, **no records** [M wildcard] | the witness gap of 2.2 |
| KeePass 2.62, Create new IDs | | copy, undecided [M wildcard] | 2.8 |
| KeePass 2.62, Delete Empty Groups | records the empty store group [R here, M here] | | the trigger of 2.1; the design's line 496 is wrong |
| KeePassXC 2.4.2 to 2.8.0 | keeps everything [M wildcard] | snapshot arrives; records never copied; the target keeps its own, the store group's included [M here, 2.8.0] | 2.2 (measured); carries 2.1's record forward |
| kdbxweb 2.1.1 (KeeWeb) | keeps everything [M wildcard] | unites records, then **drops any named group or entry with its subtree, whatever the time** [R here, M here] | 2.1: gone (e1, e2) or a silent widening (e4) |
| Keepass2Android 1.15-r6 | [R android 2], not run | KeePassLib's Synchronize: records united and applied [R android 3.1], not run | presumably as KeePass; whether its port drops a record naming a non-empty group as 2.62 does is not read |
| KeePassDX 4.5.8 | [R android 2], not run | arriving records applied and **dropped**; the open copy is what it saves [R android 3.1] | 2.2: strips witnesses from a shared file; turns a recorded deletion into none |
| KeePassDX 3.2.0, 3.3.0 | [R android 2]; 3.3.0 save only in the spike | as 4.5.8; an arriving entry whose group the open copy lacks is dropped; a deleted group goes with its subtree [R android 1.5, 3.1] | whether a store group record that meets an old group drops a snapshot there is not read in enough detail; to measure |
| KeePassDroid 2.7 | [R android 2] | none; overwrites a changed file [R android 4.2] | nothing new: the whole-file case (design 14.4) |
| Strongbox 1.65.0 | [R others 3] | deletes only when later and childless [R here] | safe from 2.1 as read |
| KeePassKit (MacPass) | [R others 3] | deletions by time [R others 3] | not checked for groups |
| keepass-rs, gokeepasslib, KeePassium, pykeepass | [R others]; pykeepass [M wildcard] | keepass-rs merges the tree only [R others]; the rest none | pykeepass's unrecorded deletion is design 14.3 |
| Whole-file synchronizer | the file as written | none | design 14.4; and 2.2: the file that wins can be newest in every other respect and still carry a rolled-back store |

## 5. Three-way chains

1. **KeePass, then KeePassXC, then KeePass.** A (plugin) revokes `c1`. A laptop merges A into its
   own file with KeePassXC: no witness [M wildcard]. A hand deletion there makes it behind [M here,
   e3]. If the desktop then **synchronizes** with the laptop's file, the desktop's records delete
   `S1` and keep it gone with the witness (follows from [M wildcard set H] and `PwDatabase.cs`
   [R here]); safe. If the desktop **opens** the laptop's file, behind, and Use what is here
   re-admits `c1`.
2. **KeePass, then KeePassDX, then KeePass.** As 2.2: the phone's file reaches none, then the old
   state in force; the desktop that opens it serves `c1` again. Read, not run.
3. **KeePass, then Keepass2Android, then KeePass.** Records are united by K2A's port of KeePassLib's
   Synchronize [R android 3.1]; no problem found, none measured.
4. **KeePass (Delete Empty Groups), then KeeWeb, then KeePass.** e4: B's write lost in the KeeWeb
   result, read in force; the desktop's next write makes A's state the only one [M here]. A later
   KeePass Synchronize with a copy still holding B's snapshot would bring it back as a head, since
   nothing names it and no `over` lists it [A, from the reading rule].
5. **KeePass (gone, then Create), then KeeWeb, then KeePass.** e2: the new store dropped again by
   every KeeWeb merge until a KeePass merge has dropped the group's record from every copy KeeWeb
   reads [M here].

## 6. Clocks, dropped times, history

- **Clocks.** The design claims "nothing rests on a time" (line 372). I found nothing that does:
  a writer whose clock is ahead leaves a replaced snapshot newer than its record, KeePass then keeps
  it and drops the record (`PwDatabase.cs:1083-1096`) [R here], and `over` still names it. Past 32
  writes it becomes a head again and is joined with its own descendant, which the rule digests and
  "lower by eight" cover for rules, the numbers for profiles and answers, and dates and retirements
  for pruned members [A, from the design's rules; the eight-rule join is not measured].
- **Dropped times.** KeePassXC and Keepass2Android drop per-key times [M spike]; the design uses none.
- **Moved times.** KeePassDX touches an entry's time when it moves it to the bin [R android 1.4]: a
  snapshot binned on the phone after the desktop replaced it is newer than its record, so KeePass
  keeps it and drops the record; `over` covers it. Harmless.
- **History.** No snapshot ever has history unless a person edits one (2.7). Trimming, ties and
  KeePassDX's unfaithful items touch nothing here.

## 7. Claims of survival in the design and their basis

| Claim (design line) | Basis given | Holds? |
| --- | --- | --- |
| "every merge measured or read adds an entry only one side holds" (46) | M, R | not kdbxweb when a record names the entry's group (2.1, [M here]); not KeePassDX 3.2.0 when the group is missing [R android 1.5] |
| the snapshot "arrives in every merge measured" (552) | M | kdbxweb with a group record drops it [M here] |
| "the witness outlives the snapshot" (552), "revoked certificates stay refused by their witnesses" (488) | M | only in copies that received records; absent after KeePassXC and three KeePass methods [M wildcard], after KeePassDX [R android] (2.2) |
| Delete Empty Groups: "nothing" (496) | R keepass 3 | wrong while a snapshot sits outside the group [M here] |
| kdbxweb: "the same outcomes as KeePass's Synchronize in all 10 cells" (464) | M | true of the cells run; not of a group record [M here] |
| a stale copy after gone "does not bring the store back into force" (488) | M | true where the gone copy kept its records; not after a KeePassDX merge (2.2) [R android] |
| "Another program cannot ... bring back one profile" (62) | design | a program cannot; the join can (2.3), and kdbxweb can by dropping a snapshot (2.1) |
| A1, A3: pairings and names from two copies survive (562) | M sets C, K | not through kdbxweb with a group record (2.1) |
| Android saves keep the seal, the flag, the marked identifiers and the records (434-436) | R | to measure (section 8) |
| Android merges add an arriving snapshot and remove a replaced one (465-467) | R | to measure; KeePassDX's dropping of records is read and its consequence (2.2) not drawn |
| K2A writes 3.1 the way KeePass 2.47 does (544) | A | to measure |
| Strongbox, KeePassKit, keepass-rs add a node the open copy lacks (469) | R | to measure |
| KeePassium, Strongbox, MacPass saves (439) | R | to measure |
| the join's laws (278) | M, numbers only | without prunes, digests or the eight rule (2.9) |
| an import shows the progress window (234) | A | to measure; it decides when a whole pass runs |
| 14.3 for a file that also holds an older snapshot | A | follows from the reading rule; e3 shows the same shape with a record |
| identifier marks survive every program (119) | M for KeePass, KeePassXC, kdbxweb, pykeepass | Android and duplicate-identifier repairs to measure (design 17.9) |

## 8. What would have to be measured, in order

1. **KeePassDX 4.5.8 and 3.2.0 "Merge data" and "Merge from"** with the design's U-A arriving at U-B,
   and H-A arriving at a phone copy holding only `S2`: does the saved file keep any deletion
   record, the witness included; does the phone reach none; and then an older file merged in. This
   decides 2.2's second sequence.
2. **Keepass2Android merges** of the same, and of e1's `R-A-deg-write` against `R-B`: does its port
   drop a record naming a present, non-empty group, as KeePass 2.62 does.
3. **Android saves** of U-A, of the 1 MiB file, and of e1's file holding a group record: seal,
   flag, marked identifiers, records naming nothing, the group record (design 17.1).
4. **KeePassDX 3.2.0 with a store group record against an old group** holding a snapshot (2.1).
5. **KeeWeb itself**, not only its library, syncing a file with a group record, to confirm it is the
   merge measured here (the `others` report assumed so).
6. **The join with digests, prunes and the eight rule** in the lattice, with a check that no join is
   wider than every input for profiles and rules (2.3, 2.9).
7. **KeePass's window**: Delete Empty Groups with the snapshot in the bin, then the plugin's write;
   Use what is here on a behind store (design 17.3).
8. Strongbox's and MacPass's merges of e1's and e4's files (2.1).

## 9. What was run for this critique

All from a console, no window, invented databases, AES-KDF at one round so kdbxweb could open them,
under two minutes. Nothing under the worktrees or the main checkout was written.

- The design's probe, `spikes\storage\probes\wildcard\design\probe\out\Wc.exe`, run unchanged: `make` (the seven sets,
  with `WC_KDF=aes`), `write` (the plugin's next write), `kpmerge` (KeePassLib 2.62 `MergeIn`),
  `sum` (the reading the gate would do).
- `kpops.ps1`, Windows PowerShell 5.1 over KeePassLib 2.62 loaded from
  `.keepass\2.62\KeePass.exe`: `deg` (`PwDatabase.DeleteEmptyGroups`), `delgroup`
  (the store group deleted for good with records for it and its entries), `delnewest` (the newest
  snapshot deleted for good with a record), `tobin` (snapshots moved into the recycle bin),
  `records` (every deletion record and group).
- kdbxweb 2.1.1, the module the `others` ground report installed, through `kw.js` (`merge`).
- `keepassxc-cli` 2.4.2 and 2.8.0 (`merge -s`), the archives the `keepassxc` ground report verified;
  both signatures checked again in this run (`out\run.txt`, top).
- e1, e2, e3 in `Run.ps1`; e4 in `Run-e4.ps1`.
