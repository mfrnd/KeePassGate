# Critique of option `single-entry`: security, KeePass's own behavior, the operator, the cost

Written 2026-11-15 against KeePassGate master `a42e38aa` (the product at `a42e38aa`) and
KeePass 2.62. Key: `single-entry-security-and-fit`. It attacks `option-single-entry.md` ("the whole
of KeePassGate's state in one entry, changed in place") through four lenses: the caller in the
threat model and a file the operator merges; where the authority's private key now shows up;
KeePass's own functions and the present guards against one entry holding everything; what one
deletion or one hand edit does; and what the change costs. It does not repeat the design's own
sections 10 and 12 except where they understate a problem. This is a defensive review: each
weakness is paired with the change that closes it.

## How statements are marked

- **[M here]**: measured for this critique, today, by the console probes under "What I ran".
- **[M probe]**: measured by the design's own probe, as `option-single-entry.md` reports it.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M store-entries]**,
  **[M rules-central]**: measured in that ground report, in `spikes/revocation-merge` as the ground
  reports digest it, or by the named option's probe or critique.
- **[R 2.62 file:line]**: read in `KeePass-2.62-Source.zip` (the copy `ground-keepass.md` verified,
  SHA-256 `9C07B053…63AC0`), extracted under
  `spikes\storage\probes\ground-keepass\src`, path from the archive root.
- **[R android file:line]**: read in the Android clones the `android` ground report pinned, under
  `spikes\storage\probes\ground-android\src`, path from the clone root.
- **[R plugin file:line]**: read in the product at `a42e38aa`.
- **[R inventory Pn]**, **[R keepass …]**, **[R android …]**: read from source in that ground report,
  which names the tag, file and line.
- **[A]**: assumed. Neither run nor read.

**Nothing about an Android program is measured by me**; the emulator run had written no
`measured-android.md` when this was finished. Every Android statement here is [R android], [M spike]
or [A].

## What I ran

Windows PowerShell 5.1 over KeePass 2.62's KeePassLib, loaded from a copy of
`.keepass\2.62\KeePass.exe` whose SHA-256 matched the original
(`AFB89B74…C89C3F3`). No window, in memory, invented content only. I reused the design's own
`Lib.ps1` (its document, its witness, its reconciling step and its datum-by-datum merge), so the
probes exercise the design's code rather than a re-implementation of it. Scripts and output:
`spikes\storage\probes\single-entry\security-and-fit\`,
`scripts\Probe.ps1` with `out\probe.txt`, `scripts\Plant.ps1` with `out\plant.txt`.

- **Plant** (`out\plant.txt`): a victim database holding an authority, a client with profile
  `agent`, a seal (`rule.G` = deny) on a group, and a child `C` under it with **no rule of its
  own**. A foreign file carrying **no authority**, only a crafted document that adds
  `rule.C = read protected list`, under the same public fixed store-group and state-entry
  identifiers, with its own witness and a random lineage. Merged into the victim with
  `PwDatabase.MergeIn`, Synchronize, OverwriteIfNewer and KeepExisting, foreign earlier and later.
- **Walk / deletion** (`out\probe.txt`): a copy that wrote nothing merged after the other copy made
  more versions than `seen` holds (the 256-ancestor ceiling, run at a scaled window of 64); and
  KeePass's File-then-Import default (Create new IDs) of a copy of the same database, then Delete
  Duplicate Entries.

## Verdict

**Viable with changes.** Under this lens there are four serious problems and five minor ones. Each
serious problem has a named change, so none is fatal on its own: no essential requirement of
`ground-inventory.md` is beyond this option because of what it is. But two of the serious problems
are forced by the design's own central mechanism rather than incidental to it, and the operator
should read them as the price of the shape, not as defects a tidy-up removes.

| # | Rating | Problem |
| --- | --- | --- |
| S1 | serious | A foreign file merged under the fixed identifiers **adds a widening rule on a node that had none**, and can force `503` by planting a bare witness. The design's "its rules can only narrow" is false, measured |
| S2 | serious | The authority's private key is a protected custom string of an ordinary entry; KeePassDroid, one of the Android programs in scope, shows it in clear, and print, export and the clipboard reach it. Unavoidable while it is one entry |
| S3 | serious | One deletion or one damaged string stops the whole gate for that database; on a phone the recovery can need a backup |
| S4 | serious | The tie-refusal is reachable: a bare witness, or a same-second concurrent edit merged without the plugin, denies the gate until an operator press, and at a true tie one side's rules are lost |
| M1 | minor | `503 undecided` now has two unrelated causes with two different remedies |
| M2 | minor | Every request runs the reconciling step; a changed document reparses 20 to 65 KB, unmeasured in the plugin |
| M3 | minor | Survival of an ordinary concurrent edit depends on history being present; the many-entries designs do not |
| M4 | minor | The closed-group child list refuses phone-made nodes until confirmed, and misses a copy that lands elsewhere |
| M5 | minor | Migration undesigned; the fail-closed direction is right but worth stating |

## What was checked and holds

So the design gets credit for what it got right, and these are not re-raised as problems.

1. **No API route reads, writes, plants or removes the state.** The store group and everything in
   it is absent as the recycle bin is, any entry carrying a `KPG.` string is absent, no route names
   a `KPG.` string, and no route lets a caller choose an identifier (section 6.5). I checked the
   44 operations: there is no move route and no copy route, `createEntry` and `createGroup` draw a
   random UUID [R plugin `Bridge/KdbxVault.cs:385`, `:603` as `rules-central` read it], and
   `updateEntry`/`updateGroup` declare no `parent` member [R plugin `Spec/openapi.yaml:811-834`,
   `:1216-1252`]. So a caller cannot name the fixed identifier or a derived one, and cannot move a
   node into or out of the store.
2. **A field reference cannot read `KPG.State`.** `{REF:}`'s wanted field is only `T U A P N I`,
   never a custom string, and `O` is a field to search in, not to return
   [R 2.62 `KeePass/Util/Spr/SprEngine.cs:586-606`]. The document is a custom string, so it is
   unreachable through a reference even though the reference ignores the searching switch.
3. **A caller cannot gain from the tree through the API.** With no move and no copy route, the only
   growth a caller can make is `create`, and a created node has no rule and inherits the nearest
   rule, which includes any seal above it: creating under a sealed group is refused for an unnamed
   profile, as today. The copy-widening of section 5.3 is reachable only through another program's
   copy command, never the API.
4. **Delete Duplicate Entries does not collapse the store.** It compares standard and custom
   strings and attachments [R 2.62 `KeePassLib/PwDatabase.cs:2039-2092`]; the state entry's
   `KPG.State`, each witness's `KPG.Version` and each small record differ, so no two are equal and
   none is deleted. The design's claim in section 6.3 holds.
5. **An import with Create new IDs cannot change the state** [M here, `out\probe.txt`, part D]:
   the arriving state entry took a new identifier, so two state entries existed, one under the
   fixed identifier unchanged, and the step read the fixed one and did nothing. This matches the
   design's section 5.4.

## Problems

### S1. A foreign file adds a widening rule, and the design says it cannot (serious; the design's claim is false)

The design's section 3.4 asserts: "A foreign file cannot take the state over ... Merged in under
the fixed identifier with a later time, it is at most a second head: **its rules can only narrow**,
a second authority ends in `503 undecided` ... What a foreign file can still do is narrow, and
plant a revocation entry (it denies)." The "can only narrow" is true of a node that already has a
rule, where the merge takes the meet (section 3.4). It is false of a node that has **no** rule of
its own, which under nearest-wins is every node governed only by a seal above it.

**Measured** [M here, `out\plant.txt`], with the design's own document, merge and reconcile:

- The victim holds an authority, a client with profile `agent`, a seal `rule.G = DENY` on a group,
  and a child `C` with no rule, so `agent` reaches nothing under `G`.
- A foreign file carries **no authority**, only a one-line document that adds
  `rule.C = read protected list`, under the public fixed store-group and state-entry identifiers,
  with its own witness and a random lineage.
- A Synchronize (either order), an OverwriteIfNewer (either order), and even a KeepExisting where
  the foreign copy is the earlier, all end with the reconciling step producing
  `M(F0+V0)` holding **`PLANTED rule.C = read protected list`** beside the kept `rule.G = DENY`. On
  the next request `agent` reads `C` and every protected value in it.

Because the foreign file brought no authority, there is no second authority and therefore no
`503 undecided` to stop it: the merge succeeds and the planted rule takes effect silently. The only
foreign-file case that did **not** plant was KeepExisting with the foreign copy the later, where the
victim's document was kept and the foreign document was lost; there the foreign **witness** still
arrived and the step answered `REFUSE version missing: F0` [M here] — a denial, which is S4.

**Why the design cannot wish this away.** The plant is not an oversight the merge rule can patch,
because it is the merge rule. The design added the datum-by-datum merge of two heads (section 3.4)
precisely to meet the survival requirement: two copies that legitimately wrote
concurrently must both keep their changes, so a datum only one head holds, that the other has not
seen, is taken as an addition. A foreign document is, to that rule, indistinguishable from the
other copy's legitimate concurrent write: random lineage, unseen stamps, a datum the victim lacks.
"All or nothing" does not help, because the whole point of the reconcile is that it is **not** all
or nothing when both sides wrote.

**The carrier.** The plant needs a merge that keeps identifiers: KeePass Synchronize and
OverwriteIfNewer [M here], every KeePassXC merge (entries are added) [M keepassxc 4], the Android
merges [R android 1.5], and KeeShare [A, as `rules-central` assumes]. The Create-new-IDs import path
is **not** a carrier [M here, part D], which is the one mercy. The inputs are a node UUID (every
identifier the API returns is the node's UUID) and the caller's own profile name (from
`GET /gate/whoami`), both available to a prompt-injected client, plus one file the operator merges.
That is exactly the actor `ground-inventory.md` E9 and the task name ("a file the operator merges in
brings a document of its own with a later time").

**Why it matters.** "A malicious `.kdbx` therefore vouches only for itself" (CLAUDE.md, first
decided bullet); E9 [R inventory]. Here a foreign file that holds none of this database's secrets
grants one of this database's clients a new reach. It is the single-entry twin of
`rules-central` S1 path B [M rules-central critique], and single-entry does not escape it by being
one document.

**The second half: a bare witness forces `503`.** The witness mechanism (section 3.5) is what makes
a lost version noticed: a witness entry naming a version that is no candidate and that no head has
seen means "a version is missing", and the gate answers `503 undecided` until an operator press.
A witness counts under its derived identifier anywhere in the tree, and that identifier is a public
derivation of a version id the attacker chooses (`Witnesses` checks only
`Uuid == SHA256('KPG/1/witness/'+vid)[0..15]`, `Lib.ps1:158-168`). So a foreign file holding one
crafted witness entry and nothing else, merged in, names a version that never existed and that no
head has seen, and the gate refuses for that database until the operator presses "continue" on the
tab. Measured as the by-product of KeepExisting above [M here]: the arriving witness alone produced
`REFUSE version missing: F0`. This is a denial, not a disclosure, so it is the closed direction, but
it is a gate outage a foreign file can cause with one tiny entry.

**Rating.** Serious. It fails closed only in the witness-DoS half; the rule-plant half **widens**,
against E9 and E5, with the actor inside the threat model, and the design states the opposite.

**Change.** Authenticate the document (and the witnesses) with a secret a foreign file cannot
supply. The served authority already holds one, the gate key inside `AuthorityDocument`
[R inventory D1], and a foreign file bringing a second authority already ends in `503 undecided`.
A MAC over the document keyed by the served authority's gate key makes a head count only if it was
written by somebody holding that key, which is somebody who can already open the database; a foreign
file holding no authority, or a different one, cannot forge it. A witness carries the same MAC, so a
crafted witness is ignored rather than refusing the gate. The cost: Recreate re-signs the whole
document (it already rewrites every record), and a head signed by a retired authority's key counts
only as a replaced version. This reverses `CA.md`'s "nothing in a record is authenticated beyond the
file" (inventory P23), a reversal to record, and it is the same change `rules-central` S1's strong
variant and `store-entries` S1's strong variant need; the three options converge on it. Short of a
MAC there is no structural fix, because the fixed identifier that makes two copies one entry to a
merge is the same thing that lets a foreign document land as a second head.

### S2. The authority's private key is in an entry, and a phone shows it in clear (serious; unavoidable while it is one entry)

**Today** the authority document is a value of `PwDatabase.CustomData`; no ordinary screen in any
program shows it, and only KeePass's XML export writes it [R inventory P26, D1].

**In this design** the same key material (about 2,903 characters of base64 PKCS#12, the authority's
private key among it) is a **protected custom string** of an entry in a group every program lists
(section 2.1). Protected is not hidden from the operator's own tools, and on a
phone it is not hidden at all:

- **KeePassDroid 2.7, one of the Android programs in scope, ignores the protected flag on display.** Its entry
  view prints every custom string's value directly, with no check of whether it is protected
  [R android `kpd27/app/src/main/java/com/keepassdroid/EntryActivityV4.java:60-70`, `fillData`
  renders `pair.getValue().toString()` with no `isProtected` branch]. So opening the `KeePassGate
  data` entry on KeePassDroid puts the authority's private key on the screen in clear. Keepass2Android,
  the other Android program in scope, masks it behind a reveal and a copy control
  [R android `k2a/.../EntryActivity.cs:735`, `:766-780`], which is better but is still one tap to the
  clipboard, and an Android clipboard copy passes through the keyboard's history [A].
- **KeePass's print, and the HTML export built on it, write every custom string, protected ones in
  clear, when "custom strings" is ticked** [R 2.62 `KeePass/Forms/PrintForm.cs:735-738`, and
  `:900-920` as `store-entries` read it]; the box is one click, and the print path never consults
  the searching switch. KeePassXC's HTML export prints every custom string in clear by default
  [M keepassxc 8]. KeePass's KDB 1.x export folds every custom string into the Notes in clear
  [R store-entries critique S3]. KeePass's own Copy on a custom string is one click to the clipboard.

**Why it is worse than the master key.** The design's section 11 concedes "more ways to leak it by
accident" and reasons that whoever holds the master key holds everything anyway. That understates it:
the authority's private key is worth **more** than the entries beside it. The master key reads the
database once; the authority's private key is the standing power to mint client certificates and
admission records for that database and, while the operator still trusts the gate, to be admitted
with any profile, offline, until the authority is recreated and every client re-paired
[R plugin `CA.md:372-380`, `:922-923`]. The present store keeps that key in the one place the format
hides from casual view; this option moves it to the most-printed, most-exported, most-screen-visible
part of the file, and onto a phone that shows it in clear.

**Why the option cannot avoid it.** The authority is written once and is identical in every copy, so
it already survives every KeePass merge and every save as an add-only custom-data key [M spike;
inventory P2] and gains nothing from the single-entry treatment. The only thing that breaks it is a
**KeePassXC merge replacing the whole custom-data dictionary** [M spike §5], which is the survival
requirement firing. So the hybrid `store-entries` S3 proposes (keep the authority in custom
data, move only the fragile data) is exactly what the survival requirement forbids for this
database: to survive a KeePassXC merge the authority must leave custom data, and the only place left
that survives is an entry. A design whose thesis is "one entry, all or nothing" cannot both keep its
promise and keep the key out of the entry.

**Rating.** Serious, against E10's spirit (secrets protected by the database's encryption and not put
somewhere more exposed). It is the price of meeting the hard requirement for the authority, and the
operator should decide whether the exposure is acceptable before choosing the shape.

**Change.** None keeps it one entry and off the screen. Two partial reliefs: the MAC of S1 does not
help here; marking the entry so KeePass excludes it from print and export has no mechanism, because
the print path reads no such flag [R 2.62 `PrintForm.cs`]. The honest options are to accept the
exposure, or to split the authority back into custom data and accept that this database must then
not be merged by KeePassXC, which is the hard requirement reversed. State which, on the record.

### S3. One deletion or one damaged string stops the whole gate, and on a phone the way back can be a backup (serious; the steepest cliff but `wildcard`)

The design concedes this (section 1 "the cliff", section 6.4, section 12). Two things sharpen it
under this lens.

- **The blast radius is everything at once.** One entry holds the authority, every admission record,
  every rule, the profile list and the answers. Moving it to the recycle bin, deleting it for good,
  or damaging the one protected string (a hand edit, a truncation, a program that rewrites the value)
  takes the whole gate for that database to a clean refusal (section 6.4). The present store spreads
  the same loss across a key family, where deleting one key costs one record; here deleting one entry
  costs all of them.
- **On a phone the recovery is weaker.** Every program moves a deleted entry to a bin
  first, so a mistaken delete is usually recoverable [R android 4.2; R keepass 9]. But KeePassDroid
  has no merge and **writes its copy over the file at every save over a file that changed underneath
  it** [R android `kpd27/.../edit/SaveDB.java:54-75`; M spike §5], so a stale phone copy saved after
  the entry was deleted elsewhere, or a deletion on the phone saved back, loses the entry from every
  copy that syncs through that file, with the recovery being a backup or another surviving copy
  (section 6.4 paths 3 and 4). The design's own measurement shows that a plain synchronize with a
  backup does **not** bring a hard-deleted entry back [M probe], so the recovery is the plugin's
  "Restore from a file", which needs the operator to still have a copy that holds it.

Measured corner [M here, part D]: after the Create-new-IDs import, Delete Duplicate Entries deleted
the fixed-identifier state entry (it kept the import's copy, which shares the state content), leaving
the gate at `REFUSE no readable version` and the fixed identifier gone. So an ordinary tidy-up
(import a backup "to be safe", then Delete Duplicate Entries) can itself remove the live state entry
and leave only a copy that counts for nothing.

**Rating.** Serious, and mitigated by the four recovery paths the design lists; no other shape has a
cliff this steep except `wildcard`. The operator should weigh it against the many-entries designs,
where one deletion costs one record.

**Change.** The design's recovery paths are the mitigation; beyond them, document in the store's
notes and in `docs/` that the one entry is load-bearing and that only the KPG tab removes a client,
and keep the Restore-from-a-file remedy. Nothing makes the single entry as cheap to lose-a-piece-of
as a key family.

### S4. The tie-refusal is reachable, and at a true tie rules are lost (serious; the cost of the witness defense)

The witness defense turns a lost version into a refusal rather than a silent widening, which is the
right direction. But the refusal is reachable and the loss behind it is real.

- **Reachable as a denial.** A foreign file holding one crafted witness entry (S1, second half)
  puts the gate at `503 undecided` for that database until an operator press [M here]. So does a
  same-second concurrent edit merged by a program without the plugin, and a Keep-existing import
  where the witness arrives and the document does not (section 3.3, the ten and sixteen refusals the
  design measured) [M probe].
- **A true tie loses rules.** At a genuine same-second tie merged without the plugin, one copy's
  version is kept nowhere (section 3.1, S3 row; KeePass and KeePassXC keep opposite sides), the
  witness makes the loss a refusal, and the operator's only answer is "continue with what is here",
  after which what the lost version changed, rules among them, is gone (section 3.5, section 4). The
  design is honest that this is the scenario the survival requirement names,
  narrowed to a same-second tie, a history limit of 0, or a
  document over the history size limit.

**Rating.** Serious. It is fail-closed (a denial, and a loss the operator is told about), so it does
not disclose; but it is a gate outage a foreign file can cause and a rule loss an ordinary
same-second edit can cause, where the many-entries designs keep both copies' records with no tie and
no refusal (section 10).

**Change.** The MAC of S1 removes the forged-witness denial. The true-tie loss is inherent to a
single document chosen by one modification time and cannot be removed inside this shape; the design's
section 10 names the middle form (a witness carrying the whole document, so a lost version is
recovered rather than only noticed) and does not design it. That middle form, or `wildcard`'s
one-entry-per-version, is the only way to keep both sides at a tie.

### M1. `503 undecided` now carries two unrelated causes (minor)

Today the word means "two authorities, choose one", answered by a prompt over KeePass's main window
at open, unlock and the save that ends a synchronize [R inventory P19]. The design adds "a version
is missing", answered on the KPG tab (section 3.5). One word, two states, two remedies, one of them
reachable by a foreign file (S1). Shared with `rules-central` M3. **Change**: a word of its own for
the missing-version state, with the same over-the-window prompt the authority choice gets, so the
operator is not sent to a tab for one cause and a dialog for the other.

### M2. Every request runs the reconciling step (minor)

The step runs on every request on KeePass's thread (section 3.3). The common case is cheap: the
entry is found by identifier and its `ProtectedString` object is compared by reference with the one
seen last time [M probe, the same object is handed back]. But when the object changed, the document
is read and its `sum` checked, and on a merge two heads are merged datum by datum. The design
measures the look-up and the hash but states "Parsing 65 KB of JSON in the plugin was not measured"
(section 6.1) and lists the plugin-side cost as item 5 still to measure (section 15). A1 (cheap
reads) is a stated requirement. **Change**: measure the parse and the two-head merge under the gate
before choosing; nothing suggests it is expensive, but it is unmeasured.

### M3. Survival of an ordinary concurrent edit depends on history (minor)

The many-entries designs and `wildcard` keep both copies' concurrent writes with no dependency on
times or history. Single-entry keeps the loser only in the entry's history, and recovers it only
when the two times differ, the history limit is at least 1, the loser is not a same-second collision,
and nothing cut it first (section 3.2). The design measures that a history limit of 0 or a document
over the history size limit loses the loser before the plugin can look [M probe]. So survival of an
ordinary edit made a few seconds apart is conditional where the other designs' is not. **Change**:
the KPG tab warns when it sees a history limit of 0 or a document near the size limit, as the design
proposes (section 12); the dependency itself is inherent to reading history.

### M4. The closed-group child list has a cost and a gap (minor)

To close the copy hole (section 5.3), narrowing a node writes `closed.<parent>` listing the group's
known children, and a child the list does not hold is refused until the operator confirms it. That
refuses anything created on a phone in a closed group until it is confirmed in KeePass (an
availability cost the design states), adds a member per child (about 32 KB for 2,000 nodes with the
root closed), and still misses a copy that lands **outside** a closed group (section 5.3's own "does
not close" list). **Change**: none beyond what the design has; the residual is the copy that lands
elsewhere, shared with every option that keeps rules off the node, and closable only by the
back-reference of `option-rules-linked.md`, which writes on governed nodes and defeats the point of
the option.

### M5. Migration is undesigned, and the residue should be stated (minor)

Section 13 leaves the conversion to a ruling. The fail-closed direction is right: a machine still
running 0.1.0 on a converted file finds no authority and serves nothing, and a node still carrying
the old `KPG` custom-data item refuses rather than inheriting. Worth stating so the format and
exposure claims are read as "for a database born or converted under this option", and so the
project's O7 question (whether a 0.1.0 database is carried over at all) is answered before, not
after, the shape is chosen.

## Lens (a): the caller and the file the operator merges

| attempt | through | outcome | basis |
| --- | --- | --- | --- |
| read, change or remove the one entry | entry, field, attachment routes | the entry is absent, as the bin is | design 6.5; [R plugin `openapi.yaml`] |
| read `KPG.State` via a reference | the gate compiles none; wanted fields are `T U A P N I` | not reachable | [R 2.62 `SprEngine.cs:586-606`] |
| name the fixed or a derived identifier | `createEntry`/`createGroup` | random UUID, not chooseable | [R plugin `KdbxVault.cs:385`, `:603`] |
| gain from the tree | `create` under a sealed group | inherits the seal: refused | nearest-wins; design 5.1 |
| move a node out of a seal | there is no move route | not possible | [R plugin `openapi.yaml`] |
| **add a widening rule** | a foreign file the operator merges | **succeeds** (S1) | [M here] |
| **stop the gate** | a foreign file holding one witness | **`503 undecided`** (S1, S4) | [M here] |
| plant a revocation (denies) | a foreign file | fail-closed, as the design intends | design §4 |

So the API's own surface holds; the caller's way in is a file the operator merges (S1), which the
datum-merge admits.

## Lens (b): secrets

The authority's private key is in a protected custom string of an ordinary entry (S2), shown in
clear on KeePassDroid and reachable by print, export and the clipboard. The rules and records are a
policy and a certificate map, also in that entry; an export or a phone screen that reveals the key
reveals them too. The design's at-rest point is fair (a protected string is kept encrypted in
KeePass's process memory, better than today's plain custom-data string), and it does not offset the
new display, print, export and clipboard surfaces (S2).

## Lens (c): KeePass's own functions and the present guards

| function | what it does to one entry holding everything | basis |
| --- | --- | --- |
| the entry dialog's OK | the `GroupForm` trap, now on the one entry: an OK that changed nothing restores the old modification time and removes its own backup, so a plugin write made underneath it is undone silently with the old time, and with no history item the witness and the plugin's memory are the only trace | [R 2.62 `KeePass/Forms/PwEntryForm.cs:1118-1135`]; design 7.5 |
| the guard against it | the writer refuses while an entry dialog is open on the state entry, through `GroupDialogs`, which already tracks entry dialogs by node | [R plugin `Bridge/GroupDialogs.cs:581-617`]; design 7.5 |
| Edit Entries | refuses writes to every entry of the database while open; the guard already covers it | [R plugin `GroupDialogs.cs:619-630`] |
| Database Maintenance | **stops mattering**: it copies the database's custom data, which the store leaves, so moving the store into an entry takes it out of that dialog's write-back. A genuine gain | [R 2.62 `DatabaseOperationsForm.cs:73`]; [R inventory P11] |
| history restore of the state entry | KeePass backs up the current version first, so the newer is in history and the lineage puts it back | [R 2.62 `KeePassLib/PwEntry.cs:602-615`]; design 6.4 |
| Delete Duplicate Entries | deletes nothing of the store (unique strings), but after a Create-new-IDs import it deleted the live fixed-identifier entry and kept the copy, stopping the gate | [M here, part D] |
| search, reports, auto-type, expiry | skipped by searching-off and auto-type-off, except the large-entries and last-modified reports, which list the group | [R keepass 9] |

**The guards.** The single writer is unchanged. `GroupDialogs` gains the entry-dialog-on-the-state-
entry watch, which it is already shaped for (it tracks entry dialogs by node). The save read back on
two things is unchanged and now covers the one entry's write. What changes for the better: Database
Maintenance's write-back, the one dialog the present trap section fears most, stops touching the
store. What changes for the worse: the state entry is now exposed to the entry-dialog trap and to
Edit Entries, both of which the guard covers, and to the history view, which the lineage covers.

## Lens (d): the operator

- **In KeePass**: one group with a handful of entries; a fourth page on the KPG tab for "continue
  with what is here" and "put it back"; a `503 undecided` that now also means "a version is missing"
  (M1). A planted rule (S1) shows on no node's tab, because the node that gained it had no rule and
  the plant added one that reads as the operator's own.
- **On the phone**: the authority's private key in clear on KeePassDroid (S2); one group the
  operator is told not to touch, whose deletion stops the gate (S3); a same-second edit that loses a
  side (S4).
- **What they can do wrong, in order of likelihood**: open the state entry on a phone and see the
  key (S2); delete or tidy the group (S3); import a backup and run Delete Duplicate Entries (S3);
  merge a colleague's or an attacker's file (S1, S4); edit the database from the phone at the same
  second as the desktop (S4).

## Lens (e): the cost

- **Classes.** The design's section 9 table is close. It understates: `OpenDatabaseTargets` gains
  the missing-version cause and a second prompt (M1); the MAC of S1, if taken, means `StateDocument`
  signs and verifies, Recreate re-signs, and the witnesses carry a MAC; `GroupDialogs` keeps its
  entry-dialog watch (it does not go). `CustomDataStore` and `IKeyValueStore`-over-custom-data go;
  `AuthorityStore`, `AdmissionStore`, `RenewalRefusal`, `ProfileNames`, `AclResolver`, `KdbxNodes`,
  `NodeDialog`, `PendingChanges`/`PendingDatabase`/`PendingStore`, `DatabaseWriter`,
  `OpenDatabaseTargets`, `OwedAnswerSaves`, `KdbxVault`/`VaultHandlers` all change.
- **Documents reversed** beyond section 11's list: the whole "database's own `CustomData`" decided
  bullet, "the authority takes the file to KDBX 4.1", "one document per node under a `KPG` key",
  "the grant editor writes into the dialog's own copy", "a copy of a node carries its rules"
  (`docs/access-control.md`), and "nothing in a record is authenticated beyond the file" if the MAC
  is taken.
- **Tests.** `CustomDataMergeRuleTests`, the rule cells of `MergeInRealDatabaseTests` and the
  `m_sdCustomData` pin fall away; new host tests are needed for section 3.1 against the pinned
  KeePass, for the entry-dialog trap on the state entry, for the closed-group list, and for the
  foreign-plant (S1) and the forged-witness denial (S4) this critique raises, several needing the
  emulator the design's section 15 defers.
- **Write size.** Every pairing, renewal, rule edit and profile edit rewrites 20 to 65 KB (section
  6.2), against one small entry in the many-entries designs, and the entry's history holds that much
  per retained revision until trimmed.

## Problems the design states that I confirm and do not re-raise

A whole file written over another (KeePassDroid, Keepass2Android overwrite, a synchronizer's conflict
copy) takes everything in it, witnesses included (section 1, section 12) — true, and shared with
every in-file store. A stale copy on its own serves what it has. A copy of a narrowed node outside a
closed group is wider (section 5.3). A history limit of 0 or an over-size document refuses every
merge where the arriving version is the earlier (section 12). All real, all the design's own, none
made worse by my findings except that S1 shows a foreign file can **widen** and not only narrow,
which the design's section 3.4 denies and the MAC closes.
