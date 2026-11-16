# Critique of `rules-linked`: survival across programs

Written 2026-11-15 against `option-rules-linked.md` as it stands at 18:25, KeePassGate master
`a42e38aa`, KeePass 2.62. The lens is survival: every save and every merge of every program in the
ground reports, walked through the design's pair of references (the rule version and its record in
the plugin's groups; `KPG.Anchor` on an entry or a link entry in a group), looking for a sequence
that loses, rolls back, duplicates or resurrects one end of a pair, or both, and for what the gate
then answers. Security of the limit itself and the planted rule (the design's 5.5 and 13) belong to
the security critic and are named here only where a survival sequence ends in them.
`measured-android.md` did not exist when this was written (checked at 18:43); I read the other
agent's working files and started no emulator.

## How statements are marked

- **[M here]**: run for this critique from a console, over databases I made with invented content.
  `...` below is
  `spikes\storage\probes`.
  Probe `spikes\storage\probes\rules-linked\survival\probe\Program.cs`, output `spikes\storage\probes\rules-linked\survival\out\survival.txt`,
  databases under `spikes\storage\probes\rules-linked\survival\work2`. Its read and write path is the design's own probe
  code, copied unchanged; section 9 has the runs.
- **[M design]**: the design's own probe results as its text and `spikes\storage\probes\rules-linked\design\out\*.txt` record
  them. I read them; I did not re-run them.
- **[M android, interim]**: the other agent's working files under `spikes\storage\android\work\runs\k2a`
  (dumps and diffs of Keepass2Android 1.15-r6 saves and edits), read at 18:43.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M crit]**: measured in that
  ground report, in `spikes/revocation-merge` as `ground-measured.md` digests it, or in one of the
  earlier critiques, named where used.
- **[R here]**: read by me in the source a ground report left under `...`, file and line given.
  **[R android]**, **[R keepass]**, **[R keepassxc]**, **[R others]**: read in that ground report.
- **[A]**: assumed. Neither run nor read.

The setup in every run of mine. The root grants nothing. Group U opens `agent` to read and list (a
link in U); entry X in U is narrowed to nothing by its own rule (`KPG.Anchor` on X); entry Y in U has
no rule. Group W has no rule and holds group S, sealed (a link in S), with entry Q, and entry V.
"Wider" means more than the operator's last decision at that node, or more than the design itself
promises for the node (row 5 of its 3.3 for a copy).

## 1. Verdict

**Viable with changes.** The core holds where the design says it is measured, and it holds in two
places the design left open. The rule text sits in entries no other program has a reason to change,
the node's reference never changes after it is written, and nothing the read path decides depends on
a modification time, history, custom data or a per-key time. So what broke today's store does not
reach this one, and edits on the Android programs in scope keep the reference: measured for
Keepass2Android [M android, interim], and read here for KeePassDroid 2.7 and KeePassDX 4.5.8, whose
entry editors the design marked [A] (section 2). Every sequence that loses a back reference through
an ordinary edit or merge ends in a limit or a refusal, never a widening.

What fails is what the second direction was brought in to fix, and what concentrating every rule in
two deletable groups costs:

1. **P1, serious.** A copy is protected by exactly one carried reference, and KeePass's own Delete
   Duplicate Entries removes it. After a copying drag of a sealed group into a granting group, a new
   secret put into the copy, and Delete Duplicate Entries, the copy is open to the granting group
   with nothing refusing and nothing shown [M here, C1b]. One hand deletion of the copy's link entry,
   or of a copied entry's `KPG.Anchor`, does the same [M here, C1c, C2].
2. **P2, serious.** Losing the newest version and its record, or both plugin groups, and then bringing
   back any older copy, puts every older rule back in force with nothing shown. The design names this
   for a loss "with no trace"; the trace does not matter. A KeePassXC merge rolled back with three
   deletion records still in the file, and KeePass's own route back (Remove deleted objects
   information, then Synchronize) rolls back as well [M here, C3, C3b, C3c].
3. **P3, serious for availability.** One deletion of the rules group with the bin emptied, in any
   copy, plus one Synchronize, removes every rule text from both copies and refuses every ruled node
   and everything below it, with nothing to settle from [M here, C5]. An import with Create new IDs
   followed by Delete Duplicate Entries refuses every ruled node too [M here, C6]. Nothing widens; the
   repair is typing every rule again.
4. **P4, serious and shared.** A whole file written over another (KeePassDroid's every save over a
   changed file, Keepass2Android's "overwrite", KeePassDX inside its 10 second window) brings back
   older rules or silence. The anchor is version independent by design, so the reference back adds
   nothing here. No store inside the file can meet this; the design's section 6 verdict for the phone
   programs has to say so.

Each of P1 to P3 has a change named below. Five minor problems follow. Nothing is fatal in the sense
of the task: P4 is the one loss no change inside the file can address, and every in-file option has
it.

## 2. What survived the attack

| Claim | Result | Basis |
| --- | --- | --- |
| A phone edit of a ruled entry keeps `KPG.Anchor` | held. Keepass2Android: an edit of E1's password kept its custom string and the revision carries it. KeePassDroid 2.7's editor removes every custom string and puts back every one its editor list holds, which it filled from the entry (`EntryEditActivityV4.java:117-138`, `:142-177`). KeePassDX 4.5.8 lists every non-standard field (`EntryKDBX.kt:285-298`, `DatabaseInfo.kt:116`), shows each as a view (`TemplateAbstractView.kt:425-453`), reads them back (`:587-592`) and writes them after clearing (`DatabaseInfo.kt:538`, `:557`) | [M android, interim, `edit-e1`, `edit-carriers`]; [R here, kpd27 and kpdx458]. The design marks both [A] |
| A rule change made in one copy while the node is edited in another | the new rule, in all cells | [M design, 04] |
| A rule changed in two copies | both versions arrive, the narrower applies | [M design, 05] |
| A first rule that narrows, losing its reference to a later edit elsewhere | still narrow, as a limit | [M design, 01, 03, 12] |
| A group's first rule while the other copy edits the group | the link is an entry, so it arrives; the rule in force | [M design, 07] |
| A lost version, its deletion record consumed by KeePassDX or never written by pykeepass | the record refuses, wherever the record is | [M design, h08, 5.6 first row]; KeePassDX consuming records [R android 3.1] |
| KeePassDX 3.2.0 dropping arriving entries whose group the open copy lacks | a node ruled since then carries its reference and refuses. In the central design it inherited (`critique-rules-central-survival.md` P4) | [R android 1.5] and the design's row 2; not run |
| Place of a version or record (bin, root, a user's group, KeePassDX's relocation at equal times, Keepass2Android's root placement) | decides nothing | [M design, h15]; [R android 3.1, 5.4] |
| KeePassDroid deleting node custom data, KeePassDX 3.2 and 3.3 deleting group custom data, a KeePassDX merge putting a stale rule under a newer time | do not reach this store: nothing is in custom data | [M spike] for the deletions; by design |
| A rules group emptied out in one copy, merged by KeePassXC with an intact copy | healed: KeePassXC applies no deletions | [M here, C5] |
| Copies made by KeePassDX | carry the link and the string; KeePassDX appends ` (~)` to every copied title (`Database.kt:1113-1149`), so Delete Duplicate Entries never pairs a KeePassDX copy with its original | [R here, kpdx458]; the copy not run |
| KeePass's Duplicate Group with its dialog's default, "Append - Copy" on (`DuplicationForm.cs:43`, `:99-105`) | the copy's link title differs, Delete Duplicate Entries deleted nothing, the copy stayed sealed | [M here, C1d] |

## 3. Problems

### P1, serious: a copy is one reference deep, and KeePass's own Delete Duplicate Entries removes that reference

**What the design rests on.** Row 5 of 3.3: a node that refers to a rule whose home is another node
is a copy, limited by the original's rule. Section 7: "Sealed stays sealed in KeePass's Duplicate
Entry, Duplicate Group, paste and Create new IDs." Section 12 names, as [A], that Delete Duplicate
Entries "can delete the original link", which only suspends a grant.

**Why that does not hold.** Nothing ties a copy to its original but what the copy carries: no
version or record names the copy. Remove that one reference and the copy is a node with no reference
in either direction, which is row 11, silence, and it inherits. Delete Duplicate Entries compares
standard and custom strings and attachments and nothing else, deletes the first of two equal entries
in tree order when its time is older or equal, and adds a deletion record
(`KeePassLib/PwDatabase.cs:1995-2029`, `:2039-2092` [R here]). A copying drag of a group runs
`PwGroup.Duplicate` with no dialog and appends the copy to the drop target (`MainForm.cs:1317-1321`
[R here]); `Duplicate` keeps every modification time (`PwGroup.cs:1437-1447`, `:358-399`, only
creation and access times are set [R here]). So the copy's link has the original's strings and time
exactly [M here, C1a: "titles equal True", both times `T0`], and whichever of the two comes first in
tree order is deleted.

**Sequence C1b** [M here]:

1. S is sealed; Q in it is a secret. The operator copies S by a drag into U, which opens `agent`.
   The copy is limited by S's seal: `Q@S=-` [copy].
2. The operator uses the copy as a start for a similar group and puts a new secret into it (Q's copy
   gets a new title and password). Still sealed: `Q2@S=-`.
3. Later, Tools then Database Tools then Delete Duplicate Entries. It deletes one entry: **the copy's
   link** (U comes before W, so the copy's link is first; the times are equal).
4. **agent at Q2: `list,read`**, U's grant. The copy holds no reference and no rule names it, so
   nothing refuses, the tab counts nothing and the audit records a grant.

Beside it [M here]:

- **C1c**: the copy's link deleted for good by hand (or moved to the bin, the bin then emptied): the
  copy of Q is `list,read`.
- **C2**: Duplicate Entry of X (narrowed to nothing) gives a copy limited to nothing; the copy's
  `KPG.Anchor` deleted by hand: **`X - Copy@U=list,read`**.
- **C1d** (control): Duplicate Group with "Append - Copy" on, moved into U, Delete Duplicate Entries:
  nothing deleted, the copy sealed.

**What the gate does.** It serves the copy at what its place grants, which is wider than the
original's rule and wider than the design's own promise for a copy, and, for a copy of a sealed
group, wider than today's store, which copies the seal into the duplicate's custom data. E6 fails for
the copy: its protection is lost without a trace. When the order is the other way (the copy after the
original), the original's link is deleted instead and the original's grant waits, as section 12
expects.

**How often.** It needs a copy plus one of: Delete Duplicate Entries with the copy first in tree order
and equal strings (a copying drag, a paste of entries that includes the link, or Duplicate Group with
"Append - Copy" turned off, a choice KeePass remembers, `DuplicationForm.cs:63-67` [R here]); or one
deletion of the copy's link or string in any program. A person tidying a copy made by KeePassDX sees
an entry titled `KeePassGate link (~)` in it [R here, `Database.kt:1135`]. That either act is common
is [A]; that the first takes two ordinary KeePass commands and no act on plugin data is [M here].

**Change.** Give the copy a reference that does not travel on the copy:

- **A forward pin for every copy.** When the plugin sees a copy (row 5) in a database KeePass holds,
  it writes, inside the operator's next save of that database (as owed answers ride a save,
  ground-inventory P18), a record in the store group of kind `copy`, naming the copy node by
  identifier and the original's anchor. It only takes away: at the copy it is the original's rule as
  a limit, as the carried reference is now. Losing the carried reference then leaves state 4 for the
  copy, a limit, never silence. Price: it reverses 4.4's "writes nothing unasked"; a copy whose
  reference is stripped in another program before KeePass ever saved it is not covered; one record
  per copy; a planted reference then causes a planted narrowing, which a planted reference can cause
  already. Measure C1b and C1c with it.
- Short of that, the tab of the original should show its copies and the docs should say that a
  copy's seal is one entry deep until Adopt. Adopt does close it: the copy then has its own anchor,
  version and record, and its link no longer equals the original's.
- Correct section 7's two rows and section 12's [A] with C1b.

### P2, serious: the newest version and its record lost, then any older copy, rolls the rule back with nothing shown; the trace does not help

**What the design says.** Section 1: "Two targeted losses and a stale copy roll one rule back: the
newest version and its record both removed with no trace, then a merge that applies no deletions."
Section 12 repeats it. Section 3.1: "Deletion records are not read."

**Why it is wider than stated.** KeePassXC applies no deletion in any merge [M keepassxc 2], and this
read path reads no deletion record, so a loss recorded properly is as good as one without a trace
whenever the older copy comes in through KeePassXC. And the plugin's two groups are one gesture in
any program (select both, delete), so "two targeted losses" is one tidy-up with the bin emptied.

**Sequence C3** [M here]:

1. The stale copy holds X opened wide (`list,protected,read`). The current copy narrowed X to
   nothing; the old version was deleted by the plugin with its deletion record.
2. In the current copy, the newest version and its record are deleted for good, **with** deletion
   records, as Empty Recycle Bin writes them. X refuses: "a recorded rule version is missing".
3. The stale copy merged into the current one:
   - KeePassLib Synchronize, Overwrite if newer, Overwrite existing, Keep existing: X still refuses
     (the open copy's own deletion records keep the stale version out, as ground-keepass 2.2 reads).
   - **keepassxc-cli 2.4.2 and 2.8.0: X `list,protected,read`**, with three deletion records still
     in the result. The anchor on X agrees with the returned version, so X is state 1.

**Sequence C3b, the tidy-up** [M here]:

1. Since the stale copy, the current copy narrowed X and gave Y a first rule that narrows.
2. Both plugin groups deleted for good with deletion records. Every ruled node refuses (every
   reference names a rule the file lacks), and with the store group gone the gate has no authority:
   the operator notices.
3. The two ways back to the lost groups both use an older copy:
   - keepassxc-cli 2.4.2 or 2.8.0 merging the older copy in: **X `list,protected,read`** (rolled
     back, nothing shown), Y refuses (its anchor names a rule the older copy never had).
   - **C3c**, KeePass alone: Database Maintenance's "Remove deleted objects information"
     (`DatabaseOperationsForm.cs:159-170` [R keepass 5]) and then Synchronize: **the same result**.
   - Synchronize without clearing the records: every node still refuses, and the authority stays
     gone, because KeePass applies the open copy's records to every arriving object under every
     method [M keepass X3, X4]. This is the route that does not recover.

**What the gate does.** Every rule changed since the older copy is back at its older text, in force,
with no band and no count. Every node first ruled since then refuses, which is the reference back
doing its job. The narrowings made since the older copy are undone: E5 fails.

**How often.** It needs the store group gone as well as the rules group (with only the rules group
gone, the records refuse and an older copy brings nothing back into force, [M here, C5 and the fold]).
The store group holds the authority, so a tidy-up that takes both stops the gate at once, and the
operator's natural repair is a merge of a backup or another device's copy. That such a tidy-up
happens is [A]; that every available repair then rolls back is [M here].

**Change.** One of:

- **Let the node witness its newest version, for refusal only.** This is the alternative 2.1
  rejected, re-argued: `KPG.Anchor` carries the newest version's id beside the anchor, rewritten at
  each rule change; a group gets a new link per change, the replaced link deleted (and dead by the
  version chain wherever it returns). The read rule: a node naming a version that is neither present
  nor dead refuses; a node naming a dead version (an older copy of the entry won a merge) changes
  nothing, the live version applies. In C3, C3b and C3c the node's own witness lives in the user's
  entry or group, which neither the tidy-up nor the stale merge touches, so all three refuse
  (follows from the fold [A]; not run). Price: an entry revision and a time change at every rule
  change, the cost section 2.1 names; a whole file written over another is still not detected (P4).
- **Attribute deletion records to anchors.** Derive the first bytes of a version's and a record's
  identifier from its anchor, and let a deletion record of that anchor that no present version or
  record answers refuse it. KeePassXC keeps the target's records (three in C3's result [M here]), so
  C3 into the deleting copy refuses. It does not help after KeePassDX consumed the records, after
  Remove deleted objects information (C3c), or in the other direction.
- Either way, correct section 1, section 5.6 and section 12: "with no trace" is not a condition.

### P3, serious for availability: one tidy-up and one Synchronize leave no rule text anywhere, and Settle starts from nothing

**What the design says.** h11 and h12 measure the rules group removed and every node using it
refusing. Settle "starts from a rule that grants nothing, never from an older version" (4.3). The
records carry "the version's own, without its text" (2.5).

**Sequence C5** [M here]:

1. On the phone, the `KeePassGate rules` group is deleted and the bin emptied (deletion records for
   the group and every version). Every ruled node refuses, U, S and everything under them included.
2. One KeePassLib Synchronize, in either direction: the intact copy's versions are older than the
   deletion records and are deleted there too. **Rule texts left in the file: 0, in both copies.**
   Every ruled node refuses in both. Keepass2Android's merge is a port of this Synchronize
   [R android 1.1, 3.1], so the phone's own merge spreads it the same way [A, not run].
3. A KeePassXC merge of the intact copy into the phone's copy heals it [M here], because it applies
   no deletion. Neither KeePass nor Keepass2Android offers that.

**Sequence C6** [M here]: File then Import of a copy of the database with its preselected Create new
IDs (`ImportMethodForm.cs:37`, `:69` [R keepass 2.4]), then Delete Duplicate Entries to clean up the
duplicates. It deletes all twelve originals: every user entry, every link, every intact version and
every record, keeping the copies (the originals come first in tree order, the times are equal). What
is left: three displaced versions and no intact one. **Every ruled node and everything below it
refuses.**

**What the gate does.** It refuses, which is closed. The repair is a Settle at every ruled node, and
in C5 the operator has no text to start from: the texts are in no copy. In C6 they are in the
displaced copies, which the tab shows but Settle does not start from. That is requirement A9 (the
store survives Database Maintenance's commands, export and import), not E5.

**Change.**

- **Carry the rule text in the record.** A record is the witness that a version was written; with the
  text in it, Settle can offer the text of every recorded version that is missing and not dead, which
  is the operator's last decision by construction and never an older one. The record and the version
  are in two groups, which is exactly why one tidy-up then loses neither text. Still a press, never
  applied unasked. The security critic should check that a record's text cannot be made to apply
  without the press.
- **A displaced entry whose derived id a record names is that version.** A copy under a new
  identifier with unchanged content is the same decision; a hand edit changes the derived id, which no
  record names, so it stays "changed outside the plugin". C6 then leaves limits and Adopt presses,
  not refusals.
- Give the rules group a new identifier each time it is made (P8), so kdbxweb does not drop it.

### P4, serious and shared: a whole file written over another

**The sequences** [R android 4.2, 5.5]: KeePassDroid never checks whether the file changed and always
writes its open copy (`edit/SaveDB.java:54-75`); Keepass2Android offers "overwrite" beside
"synchronize"; KeePassDX overwrites when the file's time moved less than 10 seconds [M spike,
`keepPC`]. Then:

- a rule changed since the phone loaded the file: the older version is back, its record present, the
  node's anchor agreeing. **The older rule is in force, nothing shown.**
- a node first ruled since then: version, record and reference all gone. **Silence, it inherits.**

**What the reference back adds.** Nothing: the anchor is the same for every version (2.1), so a stale
whole file is consistent with itself. P2's first change does not help either, because the stale file
brings the stale node with it.

**When it happens.** If KeePass still holds the database, its next save finds the file changed and
offers Synchronize (`MainForm_Functions.cs:4664-4689` [R here]); the plugin's entries in memory then
survive the union. The rules are lost only if KeePass closed after saving the change and reopened the
overwritten file. That is a property of the arrangement, not something the plugin sees [A].

**Change.** None inside the file. The design's section 6 row for KeePassDroid ("has no merge;
unchanged") and its verdict ("passes for the two phone programs as far as reading ... go") have to
name this, because KeePassDroid is one of the Android programs in scope and its ordinary save is
this overwrite whenever the file changed underneath it. The requirement can be met for the phone only
with an operating rule (the phone saves only a copy it opened after KeePass's last save, or uses
Keepass2Android's merge), and the project decides whether that is acceptable.

### P5, minor: grants that wait, and a phone clock ahead that undoes every Confirm

The design's first weakness: a first rule that opens an entry, made while another copy edits that
entry later, waits for Confirm [M design, 02]. Confirm is itself an edit of the entry and meets the
same rule. **C4** [M here]: the phone's clock is a day ahead; the phone edits V (stamped +86400) before
KeePass's first grant at +10 reaches it. After Synchronize V is `-` (unconfirmed). Confirm at
KeePass's +20 opens V; the next Synchronize in either direction takes the phone's +86400 version
whole and V is `-` again. Confirm sticks only once KeePass's clock passes the phone's stamp. The
narrowings stay in force throughout; only grants wait. Change: the tab should show the competing
version's time when it is in the future, so the operator knows why Confirm did not hold.

### P6, minor: two anchors on one node, and no press that retires one

Scenarios 06 and 08 leave two anchors naming one node [M design]. An entry carries one `KPG.Anchor`,
so one is confirmed and the other is a limit forever: every later change of the confirmed rule is
capped by the other's rule, and Confirm on the other only swaps which one caps (by 3.2 and 3.4 as
written [A]; not run). A version of the losing anchor holding `{"default":"inherit"}` would neutralize
it, but section 4.3 has no press that writes one. Change: a press, "Merge into this rule", that writes
that version.

### P7, minor: links are ordinary entries in the user's groups

Selecting every entry of a ruled group and moving or deleting them takes the link with them; a phone
tidy-up deletes an entry titled `KeePassGate link`; KeePassDX moves every unchanged entry to where the
arriving copy has it at equal times, which carries a hand move from a stale arriving copy into the
open one (`DatabaseKDBXMerger.kt:519-524` [R android 5.4]). Each suspends the group's grant and keeps
its narrowings [M design, h13, h14, 11]. Closed, occasional, shown on the tab.

### P8, minor: program-specific losses that close

- kdbxweb removes whatever a deletion record names, whatever its time [R others], so a rules group
  remade under its fixed identifier after a deletion is dropped with every version written since
  [M crit, `critique-store-entries-survival.md` P3 for the drop]. The store group's identifier is
  fixed by `option-store-entries.md`, and the design's probe fixes the rules group's (`RulesUuid`);
  section 2.6 does not say which it is. Nodes then refuse. Change: a new identifier each time the
  rules group is made, which costs nothing, since the read path finds versions wherever they are.
- KeePassDX 3.2.0 rebuilds a moved, newer group empty and leaves its unchanged entries off
  (`critique-rules-central-survival.md` P4 [R]): a moved rules group loses every version in that
  file (records refuse), a moved user group loses its link (grant waits) and, with it, every
  unchanged user entry, which is that release's own defect [A, as there].
- Keepass2Android puts an arriving entry at the root when its group is missing [R android 1.5]. A link
  there is "a moved link" and is ignored, as designed. A user entry there takes the root's rules,
  which no store can see; it needs the user to have deleted the group on one side while the entry was
  edited on the other [A].

### P9, minor: a copy follows its original's later decisions

Row 5 limits a copy by the original's rule as it is now. Widening the original later (in this copy or
in another one, through any merge) widens every copy up to what its place grants. Today a copy keeps
the rule it was copied with. Not a loss, but a change of promise that `docs/access-control.md` must
state, beside the one section 8 names.

## 4. Every program, save, edit, copy and merge

| Program | Save | Edit of a ruled entry | Copy | Merge | What the gate then does | Basis |
| --- | --- | --- | --- | --- | --- | --- |
| KeePass 2.62, Synchronize | kept, KDBX 4.0 | kept | carries the reference (P1) | union; deletion records carried; an entry whole by time, so a first-rule reference can lose (a limit) | the rule, the narrower, a limit, a refusal; spreads a deleted rules group to both copies (P3) | [M design]; [M here, C3, C5] |
| KeePass, Overwrite if newer, Keep existing | | | | union; records not carried, the open copy's applied | as Synchronize into the deleting copy | [M design]; [M here, C3] |
| KeePass, Overwrite existing | | | | the arriving entry whole regardless of time: an older backup takes the reference off every entry it predates | limits (grants wait) | [M keepass 1a]; by the design's 01 |
| KeePass, Create new IDs | | | every node and plugin entry twice | copies limited; with Delete Duplicate Entries after, every ruled node refuses (P3) | [M design]; [M here, C6] |
| KeePass, Delete Duplicate Entries | | | after a copying drag, deletes the copy's link (P1) | | the copy at its place's grant | [M here, C1b] |
| KeePass, Remove deleted objects information | | | | opens the rollback route (P2) | the older rule | [M here, C3c] |
| KeePass, Empty Recycle Bin, delete for good | records | | | | a record or the node's reference refuses | [M design, h08 to h12] |
| KeePassXC 2.4.2 to 2.8.0 | kept | `edit -u` kept, revision carries it | Clone copies attributes [R xc] | union; never applies a deletion; brings replaced versions back, dead by `KPG.After` | the rule, the narrower; heals a deleted rules group (C5); rolls back after the loss of P2 | [M design]; [M here, C3, C3b, C5] |
| KeePassXC KeeShare import | | | | Synchronize mode | as KeePass Synchronize | [R keepassxc 3] |
| Keepass2Android 1.15-r6 | every custom string kept | kept, revision carries it | deep clone, new identifier | KeePassLib's Synchronize ported; the arriving entry at a tie; missing group, the root; offers "overwrite" | as Synchronize; P3 spreads; P4 | [M android, interim]; [R android 1.1, 1.5, 3.1, 4.2] |
| KeePassDX 4.5.8 | kept [R android 2] | kept [R here] | ` (~)` titles, fields copied [R here] | arriving records applied and dropped; the open copy's own keep older arrivals out; equal-time relocation; missing groups recreated | records refuse where versions were consumed; links relocated (P7); P4 within 10 s | [R android 3.1, 5.4]; [R here] |
| KeePassDX 3.2.0, 3.3.0 | kept [R android 2 for 3.2.0] | as 4.5.8 [A] | | 3.2.0: drops arrivals whose group is missing; a moved, newer group rebuilt empty | refuses (improvement on the central design); P8 | [R android 1.5]; [R crit] |
| KeePassDroid 2.7 | kept [R android 2] | kept [R here, `EntryEditActivityV4.java:117-177`] | not read | none; every save over a changed file overwrites | P4 | [R android 4.2] |
| kdbxweb 2.1.1 | kept [M others] | | | union; removes whatever a record names; drops a remade group | refuses (P8) | [R others]; [M crit] |
| pykeepass 4.2.0 | kept [M others] | moves no time [R others] | | none; deletes without records | the record or the reference refuses | [M design, h08, h09] |
| Strongbox, KeePassKit, keepass-rs | in their models | | | union, KeePass-like | as KeePass | [R others] |
| KeePassium, gokeepasslib | in their models | | | none; KeePassium overwrites on conflict | P4 | [R others] |
| A whole file written over another | | | | nothing merges | P4 | [R android 5.5]; [R others] |

## 5. The sequences the task names

| Sequence | Outcome | Widened or wrongly refused | Basis |
| --- | --- | --- | --- |
| An entry edited on the phone in one copy while its back reference was written in the other | the later entry whole; the reference lost; the rule a limit | narrowings hold; a first grant waits for Confirm; with the phone's clock ahead, until the clock passes (P5) | [M design, 01 to 03]; [M here, C4] |
| A stale copy merged in, either direction | union; returning older versions dead by the newer version's or record's `KPG.After` | neither | [M design, 04, 05, 5.6 first row] |
| The same, after the newest version and its record were lost | the older rule (P2) | **widened**, nothing shown | [M here, C3, C3b, C3c] |
| A rule entry deleted by hand | the record refuses; with the record, the node's reference refuses; with the reference too, silence | closed; silence needs three acts in three places | [M design, h08 to h10] |
| A referenced node deleted by hand | its versions are inert; back from another copy, its own rule | neither | by 3.2 [A]; not run |
| The recycle bin emptied | a version in the bin counted until then; afterwards as a hand deletion, and Synchronize carries it to every copy (P3) | refused, every rule retyped | [M here, C5] |
| History pruned or restored | nothing reads history; a revision from before the first rule is a limit | a grant waits | [M design, h17]; by 3.1 |
| A copy made in each program | KeePass, KeePassXC, Keepass2Android, KeePassDX carry the reference; KeePassDroid's copying, if it has any, not read | limited at the copy; **widened** after one deletion of the copy's reference or Delete Duplicate Entries (P1) | [M design, 5.1]; [M here, C1]; [R] for the others |
| An import with new identifiers | copies limited; with Delete Duplicate Entries after, every ruled node refuses | refused (P3) | [M design]; [M here, C6] |
| KeePass, a phone, KeePass | Keepass2Android: as Synchronize. KeePassDX: records consumed, versions refused by their records; a later KeePassXC merge brings older versions back dead. KeePassDroid over a changed file: P4 | as the rows above | [R android]; [M design, 5.6]; [M here] for the KeePass and KeePassXC steps |

## 6. How often an ordinary day produces a refusal the operator repairs by hand

- **A day with no rule change**: nothing in any save, edit or merge measured or read produces one.
  The rule text is in plugin entries and the node's reference is untouched by edits of its other
  fields [M design; M android, interim; R here].
- **A day with a first rule on an entry**: one Confirm, only if that entry is also edited in another
  copy before the next merge [M design, 02]; every merge until the phone's stamp passes, if that
  copy's clock is ahead [M here, C4]. How often both happen is the design's falsifier (16.6) and is
  [A].
- **A link or `KPG.Anchor` moved or deleted by a person**: one Put back or Confirm per node [M design].
  Frequency [A].
- **The large repairs** (P3: every rule retyped; C6: every node settled or adopted) need a deliberate
  deletion of a plugin group or a cleanup command after an import. Not daily [A]; when they happen,
  the cost is the whole rule set.

## 7. Claims of survival and their basis

**Found wrong or incomplete by measurement here:**

1. Section 1 and 12, "removed with no trace": the trace does not matter under KeePassXC, and
   KeePass's own recovery route rolls back too (C3, C3c).
2. Sections 2.3 and 7, "the copy of a sealed group stayed sealed": true when the copy is made, not
   after Delete Duplicate Entries (C1b) or one deletion of the copy's reference (C1c, C2).
3. Section 7, Delete Duplicate Entries "the surviving copy is still limited": measured for an entry
   (h16); for a group's link it deleted the copy's link and kept the original's (C1b).
4. Section 12's [A], Delete Duplicate Entries after a drag "can delete the original link": it deletes
   whichever link comes first, and when that is the copy's, access widens rather than waits.
5. Section 6, "passes for the two phone programs as far as reading and one interim measurement go":
   omits KeePassDroid's overwrite (P4) and a phone tidy-up spread by Synchronize (P3).

**Resting on reading, to be measured** (most important first):

6. Every Android merge of this shape: Keepass2Android's (as KeePassLib's Synchronize, the tie, the
   root placement of a link), KeePassDX 4.5.8's (records consumed, equal-time relocation of links,
   recreated groups), KeePassDX 3.2.0's (dropped arrivals, a moved group rebuilt). None was run by
   the design or here.
7. KeePassDroid 2.7's and KeePassDX's edits keeping `KPG.Anchor`: read here, not run. KeePassDX
   3.3.0 not read at all.
8. Keepass2Android's merge spreading a deleted rules group as KeePassLib does (P3).
9. Copies outside KeePass: KeePassXC's Clone, Keepass2Android's copy, KeePassDX's copy carrying the
   string and the link; whether KeePassDroid copies at all (not read).
10. KeePass's window: that a copying drag and Duplicate Group call what was read; Delete Duplicate
    Entries through the menu; Database Maintenance clearing records.
11. kdbxweb dropping a remade rules group (P8), and Strongbox's, KeePassKit's and keepass-rs's merges.

**Resting on assumption:**

12. That phone users rarely delete plugin entries or groups, and that the operator's repair after
    losing the store group is a merge of an older copy (P2, P3).
13. How often a first grant meets a later edit elsewhere (P5; the design's 16.6).
14. That the changes named here do what the fold says: the forward pin (P1), the version witness and
    record attribution (P2), the text in the record and the displaced-version rule (P3). None was run.

## 8. What the design should change in its text

- Section 1, the third and fourth weaknesses: P1 and P2 as measured, without "with no trace".
- Section 2.2's table: KeePassDroid's and KeePassDX's edits are read, not assumed (section 2 here).
- Section 3.3 row 5 and section 7: a copy's limit lasts as long as its carried reference (P1).
- Section 4.3: no press retires a second anchor (P6); Settle has nothing to start from after C5 (P3).
- Section 6: the KeePassDroid row and the verdict (P4, P3).
- Section 12: C1b, C3, C3c, C5 and C6 in place of the [A] and the "no trace" condition.

## 9. What was run for this critique

- **The probe.** `Program.cs` under `spikes\storage\probes\rules-linked\survival\probe`: lines 1 to 494 and the helpers
  `E`, `G`, `AnchorOfX`, `InstsOf`, `RecordOf` and `Cli` copied unchanged from the design's probe
  (`spikes\storage\probes\rules-linked\design\probe\Program.cs`), so its write path, fold, limit and resolver are the
  design's; my base and runs C1 to C6 added after them. Compiled with the .NET Framework's own `csc`
  against a copy of `.keepass\2.62\KeePass.exe` (SHA-256 `AFB89B74…89C3F3`, equal
  to the original's; Authenticode valid, Dominik Reichl). Databases with invented content, Argon2d
  with 2 iterations and 1 MiB, password `probe-only`; every result saved and reopened. The whole run
  took under 3 seconds.
- **KeePass's own code where it acts**: `PwGroup.Duplicate` appended to the drop target, as the
  copying drag does; `PwEntry.Duplicate` with the dialog's title; `PwDatabase.DeleteDuplicateEntries`;
  `PwDatabase.MergeIn` under four methods and Create new IDs; a permanent deletion as a record plus a
  removal; Remove deleted objects information as clearing `DeletedObjects`.
- **keepassxc-cli 2.4.2 and 2.8.0**, the copies the KeePassXC ground report extracted, both with a
  valid Authenticode signature from DroidMonkey Apps, LLC, checked again
  (`spikes\storage\probes\rules-linked\survival\out\signatures.txt`); `merge -s`, password on standard input.
- **Read here**: KeePass 2.62 source (`spikes\storage\probes\ground-keepass\src`): `PwDatabase.cs:1969-2092`,
  `PwGroup.cs:358-399`, `:1437-1447`, `MainForm.cs:1317-1321`, `:2313-2342`,
  `DuplicationForm.cs:43-120`, `MainForm_Functions.cs:4664-4689`. KeePassDroid `v2.7`
  (`spikes\storage\probes\ground-android\src\kpd27`): `EntryEditActivityV4.java:117-209`. KeePassDX `4.5.8`
  (`spikes\storage\probes\ground-android\src\kpdx458`): `DatabaseInfo.kt:86-135`, `:512-570`, `TemplateAbstractView.kt:410-610`,
  `EntryKDBX.kt:121-131`, `:285-298`, `Database.kt:1113-1149`.
- **Not run.** No window, no KeePass process, no Android program or emulator, no test tier, no
  `build.ps1`. Nothing was written to either worktree or to the main checkout.
