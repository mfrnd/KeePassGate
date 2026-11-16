# Critique of option `single-entry`, lens: survival across programs

Written 2026-11-15 against `option-single-entry.md` ("the whole of KeePassGate's state in one entry,
changed in place") and the six ground reports, on KeePassGate master `a42e38aa`. The question asked
of the design: walk every save and every merge of every program the ground reports cover, and
every way the tree can change under the document, and look for a sequence in which the wrong
version of the one entry wins and the right one is nowhere, or the reconciling step decides
wrongly; then say what the gate answers. `measured-android.md` did not exist when this was written,
so nothing about an Android program is measured here either.

## 0. How statements are marked

- **[M here]**: measured for this critique, today, from a console, over invented databases. Script:
  `spikes\storage\probes\single-entry\survival\scripts\Survival.ps1`,
  on the design's own probe library `Lib.ps1` copied unchanged beside it. Outputs: `w1.txt`,
  `w2.txt`, `c1.txt`, `k1.txt`, `d1.txt` in the `out` directory beside `scripts`. Section 9
  describes the runs.
- **[M design]**: measured by the design's own probe, as its `out\cells.txt` and `out\rounds.txt`
  record it.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M wildcard-critique]**: measured in that
  ground report or critique.
- **[R android]**, **[R keepass]**, **[R keepassxc]**, **[R others]**: read from source in that
  ground report, which names the tag, file and line. **[R 2.62]**: read by me in the KeePass 2.62
  source the `keepass` ground report verified, file and line given.
- **[A]**: assumed. Neither run nor read.

"The design" is `option-single-entry.md`, cited by line.

## 1. Verdict in short

**Viable with changes.** Under this lens the option does what it says on the one point the evaluation
asks about: every merge measured or read takes the state entry whole, so no merge leaves a mixture
[M design, 128 cells; R android 1.1; M keepassxc 5]. And it removes the failures that started the
rethink: nothing of the plugin's sits on a group or an entry, so KeePassDroid deleting every node's
custom data and KeePassDX 3.2 and 3.3 deleting every group's no longer touch a rule
[M inventory 3; design line 98]. For a setup of one KeePass with the plugin and the
Android programs in scope, the state entry is only ever written by that one KeePass, the phone's copy is
always an ancestor of it, and the merges walked in section 3 end with the newer state in force,
on read evidence for both phones.

What does not hold as written, most severe first:

1. **A whole file written over a newer one rolls everything back with no sign, and KeePassDroid,
   one of the Android programs in scope, does that at every save over a changed file**
   [R android 4.2]. Followed by KeePass's lock and unlock, which reads the file again, the gate
   serves the old state: a revoked client admitted again. The design lists this as not solved
   (line 814). It is fatal to the requirement as worded for every store inside the file, so it does
   not decide between options; what can be changed is that the gate refuses instead of serving
   (section 2.1).
2. **The lineage is cut at 256 versions, and past that the step judges wrongly in every reading of
   the design.** A copy older than the window brings a profile the operator took off back onto the
   list (a widening), or refuses nodes the operator widened, or, read the other way, a concurrent
   copy's narrowing is dropped without a refusal. Measured (section 2.2).
3. **KeePassXC never applies deletions, so the witnesses the plugin deletes pile up in every file
   KeePassXC merged into, and past the window the gate refuses for a version nobody lost.**
   Measured: `503 undecided` on a laptop's file with nothing missing (section 2.3).
4. **"Continue with what is here" names the missing version as seen, so when that version does turn
   up, its narrowing and its pairings are thrown away for good.** Measured; a variant that only
   acknowledges the missing version restores both (section 2.4).
5. **A node with no rule of its own that another program's merge puts back outside a sealed group is
   served by where it lands.** Not a regression (today is the same), but the closed groups do not
   catch it, because they record membership and not position (section 2.5).

Then four minor ones: KeePass's own Delete Duplicate Entries removes the state entry after an
import with Create new IDs, contrary to design line 569 (measured, 2.6); the fixed identifier and a
deletion record make every kdbxweb merge drop a restored state entry (measured, 2.7); the root is
closed by any top-level narrowing, so the copy hole is smaller and the refusals larger than the
design says (2.8); a future time is followed, so two plugin hosts tie predictably (2.9); deletion
records grow by one per version for ever (measured, 2.10).

## 2. Problems, most severe first

### 2.1 Serious, shared by every store in the file: a whole file written over another

**Sequence, the programs in scope** [R android 4.2, 5.5; CLAUDE.md on locking; A for the
synchronizer]:

1. The phone opens the database in KeePassDroid at 9:59, holding version `v1`, in which client `X`
   is admitted.
2. The desktop's KeePass revokes `X` at 10:00: version `v2`, the revocation entry `R(X)`, the witness
   `W(v2)`, saved. The synchronizer delivers that file to the phone at 10:00:30.
3. On the phone the operator changes a password and saves at 10:02. KeePassDroid "never checked:
   always overwritten" [R android 4.2]: the file is `v1` plus the password, with no `R(X)` and no
   `W(v2)`. The synchronizer sees a local change on top of the version it delivered and sends it
   back as the newest; no conflict copy [A].
4. KeePass locks at 10:05 (idle or Windows lock). A lock closes the database and an unlock reads the
   file again (CLAUDE.md, "A locked document's close carries no path").
5. At 10:30 the operator unlocks. The plugin reads `v1`. Nothing in the file names `v2`: the witness
   went with the overwrite, and so did the revocation entry. **`X` is admitted.** No refusal, nothing
   on the tab.

The same follows from Keepass2Android's "No, overwrite" and from KeePassDX's save over a file whose
time moved by less than ten seconds [R android 4.2], from a synchronizer that resolves a conflict by
keeping the later copy, and from opening a backup [A]. If KeePass had not locked, its next save
would have found the file changed and offered Synchronize, which keeps `v2` and `R(X)`
[R keepass 4, `MainForm_Functions.cs:4664-4689`]; the loss needs the reload.

**Outcome.** E3 and E5 broken silently. No arrangement of data inside the file can survive a newer
file being replaced by an older one, so this is fatal to the survival requirement
for every in-file option alike. It is listed first so that a
comparison of options does not read the design's "not solved" line as a detail.

**Change (needs a ruling).** A rollback marker outside the file, on the gate's machine: per authority
fingerprint, the newest version identifier the plugin served and the digests of the certificates
it revoked, in KeePass's own configuration. A file whose document has not seen that version is
answered `503 undecided` with "this file lost changes KeePassGate made here", and a remembered
revocation refuses whatever the file says. It only ever refuses, and losing it returns to today's
behavior, so it gives an attacker-supplied file nothing; but it is literally the "sidecar" E9
forbids, so it is the project's call. Even with it, the rules lost are refused rather than kept:
the operator must restore or redo them.

### 2.2 Serious: the bounded `seen` makes the step judge wrongly once a copy is 256 versions behind

`seen` holds "the newest 256" ancestors (line 184); a head is "a candidate no other candidate has
seen" (line 284); the merge's last row treats "a stamp older than what seen still holds" the narrow
way (line 337) without saying how a stamp's age is known, since a stamp is a version identifier
and carries no generation. The design's probe used a window of 64 and never passed 25 generations
[M design, `Lib.ps1:9`, `rounds.txt` "generation 25"], so the window was never reached.

A version is made at every pairing, renewal, revocation, rule and profile edit, every settings OK,
every confirmation of a child in a closed group and every entry added through Add Entry in one
(lines 478-480). Ten clients renewing weekly make 520 a year on their own [A, arithmetic].

**Sequence W1, a stale ancestor** [M here, `w1.txt`], window 256, KeePass Synchronize with the
plugin's memory:

1. `D0`: profiles `P` and `Q`; rule `N1` `list`; rule `N2` `read list`; rule `N3` `list`; client
   `c0`; `c9` refused forever. Copy B keeps `D0` (a laptop, a backup, a phone that has not synced).
2. Copy A: `v1` takes `P` off the list, `v2` widens `N1` to `read list`, `v3` removes `N3`'s rule,
   `v4` lifts `c9`'s refusal, then 296 more versions.
3. B synchronized into A: `v300` current, `D0` in history, as every cell of the design predicts.
4. The step, three ways:

| Reading of the design | The step | What the gate serves | |
| --- | --- | --- | --- |
| as written, with the design's own probe merge | `D0` is a head, merged | **`P` back on the list**, `N1` `list`, `N3`'s rule back, `c9` refused | a removed profile returns: **widening** (E5) |
| as written, the last row read the most favorable way (a stamp no window holds, or older than the other's window, is old) | merged, "old stamp" at four data | `N1` and `N3` **refuse**, `c9` refused | the operator's widening and removal turned into refusals, silently |
| a candidate below the other's window taken as its ancestor | `v300` alone | correct | but see W1b |

At 20 versions all three readings serve `v300` correctly [M here], which is the control.

**Sequence W1b, a concurrent copy that forked long ago** [M here]: B forks at `D0` and writes `B1`,
narrowing `N2` to `list` and pairing `cB`; A writes 300 versions; B synchronized into A.

| Reading | What the gate serves |
| --- | --- |
| as written, probe merge | `N2` `list`, `cB` admitted: right |
| last row, favorable | `N2` `list`; **`cB` dropped**: the client must pair again |
| ancestor by generation | **`N2` `read list`, no `cB`, and no refusal**: B's narrowing lost silently (E5, E6) |

**Outcome.** No reading is right in both sequences: one widens, one refuses what it should serve,
one loses a narrowing without a word. Nothing about it needs an unusual program; a KeePass
Synchronize with a copy that is a few months stale is enough.

**Fix.** Make the lineage exact and bounded by the number of writers, not of versions: a version
vector. Each KeePass installation that writes gets a random writer identifier (kept in KeePass's
configuration; it decides nothing about trust), a version carries one counter per writer, a stamp
is a writer and a counter, and "has seen" is a comparison of counters that never ages. Witnesses
carry the writer and counter too, which also fixes 2.3.

### 2.3 Serious: KeePassXC brings every deleted witness back, and past the window the gate refuses

The plugin deletes the witnesses of the versions it replaces, with deletion records (line 661);
witnesses a merge without deletions brought back are "recognized (the current version has seen
them) and removed again" (lines 670-671). KeePassXC never applies a deletion in any merge
[R keepassxc 3; M keepassxc 4]. Recognition needs the window.

**Sequence W2** [M here, `w2.txt`]: the desktop writes 300 versions as the plugin does; every 25,
the laptop merges the desktop's file into its own with `keepassxc-cli` 2.8.0 `merge -s` (12 merges,
all exit 0). KeePass with the plugin then opens the laptop's file, as it does wherever the laptop's
save becomes the shared file:

| File | History after KeePassLib's load | Witnesses | Deletion records | The step |
| --- | --- | --- | --- | --- |
| desktop | 0 | 1 | 300 | serves `v300` |
| laptop | 10 (KeePassLib trimmed it at load) | **13** | 0 | **`503 undecided`, "version missing: v25, D0"** |

Nothing was missing: `v25` and `D0` are ancestors of `v300` that fell out of the window and out of
history. Continue cleared it in this run.

**Outcome.** Every database for the gate refuses until the operator presses a button that says a
change made elsewhere did not arrive, which is false. An operator who learns that the button is
harmless will press it the day it is not (2.4). It recurs whenever a KeePassXC-written file reaches
the plugin after the window has moved on [A, from the mechanism].

**Fix.** As 2.2: with a version vector a witness of writer `w`, counter `c` is seen exactly when the
head's counter for `w` is at least `c`, whatever the age.

### 2.4 Serious: Continue forecloses the remedy it names

Lines 376-383: when a version is missing the operator may "Synchronize with that copy" or "Continue
with what is here", which "writes a version that names the missing one as seen". A version named as
seen is not a head when it arrives later (line 284), so the remedy is gone the moment Continue is
pressed, and the page does not say that.

**Sequence C1** [M here, `c1.txt`]:

1. `D0`: rule `N` `read list`, client `c0`. A writes `A1` (narrows `N` to `list`, pairs `cA`); B writes
   `B1` (pairs `cB`), in the same second.
2. B merged into A by a KeePassLib Synchronize with no plugin at it (a phone's merge has this shape:
   Keepass2Android lets the arriving copy win a tie [R android 1.1]): `B1` current, `A1` nowhere,
   `W(A1)` present. The plugin: `503 undecided`, "version missing: A1".
3. The operator presses Continue. The gate serves `N` `read list`, `c0`, `cB`.
4. Later, a copy of A from before the merge (the laptop's own file, a backup) is synchronized in.
   `A1` arrives in history.

| Continue | After step 4 the gate serves |
| --- | --- |
| as designed, `A1` named as seen | `N` **`read list`**, no `cA`: the narrowing and the pairing are discarded for good |
| proposed, `A1` recorded as acknowledged, not seen | merged: `N` `list`, `cA`, `cB`: everything both copies decided |

**Outcome.** E5 broken by an ordinary synchronize after the documented remedy. With one plugin host
this needs a lost version, which needs a tie, Keep existing or a history limit of 0 (lines 302-304,
825-827); with two plugin hosts a tie is enough.

**Fix.** Continue writes an acknowledgment of the missing version and not an ancestry: the refusal
stops, and if the version turns up it is a second head merged by kind, which can only narrow
(measured above). The page then says "if that copy turns up later, what it changed will be added".

### 2.5 Serious, shared and not a regression: a merge that puts a node back outside a sealed group

The design's row for a moved node: "no more than today: a node with no rule of its own, moved out of
a sealed group, is governed by where it lands" (line 432). True, and it is the operator's
requirement that today fails here too, without a person moving anything:

1. **KeePassDX 4.5.8**, open copy newer: entry `E` was moved into the sealed group `S` on the
   desktop; a move moves no modification time, so `E` carries the same time in both copies
   [M inventory 4.3, `FINDINGS.md:417`]. The phone opens the shared file and merges an older copy that has `E` in the
   open group `G`: at equal times the entry is moved to the arriving copy's group [R android 3.1,
   5.4]. `E` is in `G` again.
2. **KeePassXC, then KeePass**: `E` moved into `S` on the desktop; the laptop changes `E`'s content
   later; a KeePassXC merge into the desktop's file keeps `E` in `S` but with the laptop's older
   location time; a later KeePass Synchronize with the laptop's file then has `E` at equal location
   times, and that regression measured through KeePass's synchronize [M keepassxc 4,
   `move-A-edit-B`; M keepassxc 9].

The gate then serves `E` with `G`'s grant: **widened**. The closed groups (lines 474-490) do not
catch it, because they record which nodes are confirmed inside a group, not where: `E` stays on the
closed ancestor's list wherever it lands under it, and outside every closed group nothing looks.

**Change.** Let a closed list pin each listed node's parent, and refuse a listed node found under
any other parent, or outside the closed group, until the operator confirms it (KeePass's own moves
in its window, which the plugin can see, would confirm themselves). This closes "moved out of a
sealed group by another program" (line 819) as well, at the price of a press after a person's own
move on a phone.

### 2.6 Minor: Delete Duplicate Entries removes the state entry after an import with Create new IDs

Line 569: "Delete Duplicate Entries compares strings, and no two of these entries have equal
strings." True inside one database. The phone never writes the state entry, so a phone's copy
always holds the same state entry as the desktop, string for string, and File then Import
preselects Create new IDs [R keepass 2.4].

**Sequence D1** [M here, `d1.txt`]: B, a copy at the same version, imported into A with Create new
IDs: two state entries and two witnesses, the step still serves `v1`. Tools, Database Tools, Delete
Duplicate Entries: three entries removed, **the state entry under the fixed identifier and its
witness among them**, a deletion record naming the fixed identifier. The step: no readable version,
the gate serves nothing for that database.

Why: equal times delete the first of the pair in tree order (`bDeleteA = CompareLastMod(...) <= 0`,
[R 2.62 `KeePassLib/PwDatabase.cs:2002`]), and the imported copies are added after the originals
[R keepass 2.4].

**Outcome.** Fails closed, at the cliff (line 777), with the document intact in the copy beside it.
And the deletion record that it leaves is the trigger for 2.7.

**Fix.** When the fixed entry is gone and a copy holds a readable document of this database's
lineage, the KPG tab offers to put it back under the fixed identifier; correct line 569; give the
state entry and each witness a string that makes it unequal to a copy (the import gives the copy a
new identifier, so a string naming the entry's own identifier differs).

### 2.7 Minor: the fixed identifier and a deletion record, through kdbxweb

kdbxweb skips every group and entry its united deletion records name, whatever the times
[M wildcard-critique 2.1, for a group; R there for entries]. The design restores a deleted state
entry under the same fixed identifier with a later time (lines 605-610), measured through KeePass
only.

**Sequence K1** [M here, `k1.txt`]: A's state entry deleted for good; B takes that file; A restores
the entry under the fixed identifier at a later time and drops the record. kdbxweb 2.1.1, either
direction: **state entry absent, the gate serves nothing**. KeePass Synchronize, either direction:
restored and served.

**Outcome.** The restore holds until a KeeWeb merge with any copy still carrying the record. The
wildcard critique's fix, a fresh identifier, is not open to this design: the fixed identifier is
what makes two copies hold "the same entry to every merge" (lines 151-154). KeeWeb is not one of the
programs in scope.

**Fix.** Either state KeeWeb merges unsupported, or make the identifier carry an epoch
(`KPG/1/state/<epoch>`), raised at each restore, with the document naming its epoch and the step
reading every epoch's entry as a candidate.

### 2.8 Minor: closing the parent closes the root more often than the design says

The grant editor closes "the node's parent" whenever a rule narrows (line 476). For a group or entry
directly under the root, the most common place for a seal [A], the parent is the root, so the case
the design presents as an option ("the operator may close any group by hand, the root included",
line 481; "with the root closed", line 487) is the ordinary one. Two consequences. The copy hole is
smaller than lines 485-486 say: under a closed root every copy anywhere is refused. And the cost is
larger: every entry added on a phone anywhere in the database is refused by the gate until it is
confirmed in KeePass; a new group made on a phone, or through KeePass's Add Group dialog, which the
design does not list as confirming (lines 478-480), refuses itself and every confirmed entry moved
into it ("there and below"). All fail closed. The design should say so where it prices the closed
groups, and name Add Group.

### 2.9 Minor: the time rule follows a future time, so two hosts tie predictably

Line 657: "The time is the later of now and one second past the entry's own and its newest history
item's." One hand edit of the state entry on a device whose clock is a day ahead puts that day on
the entry. From then on two plugin hosts that both start from it write at exactly that time plus
one second, then plus two, and every pair of concurrent writes is a tie: Keepass2Android and KeePass
keep the arriving copy, KeePassDX, KeePassXC and kdbxweb the open one, the loser in no history
[R android 1.1; M keepassxc 5; M others 4], and each is a `503 undecided` [A, from the rule; not
run]. One host alone is unaffected. **Fix:** when the entry's own time is ahead of the clock, add a
random number of seconds to it, or ignore a time more than a minute ahead; the lineage, not the
time, decides.

### 2.10 Minor: one deletion record per version, for ever

Every write deletes the previous witness with a record (line 661), and KeePass never ages records
[R keepass 5]. Measured: 300 deletion records after 300 versions [M here, `w2.txt`], about 110 bytes
of XML each [A]. That is growth without bound (A5). Database Maintenance's "Remove deleted objects
information" clears them all [R keepass 5], after which a KeePass merge no longer deletes a
resurrected witness either, which brings 2.3 forward. **Fix:** with the exact lineage of 2.2, a
witness that comes back is recognized as seen whatever its age, so the plugin can remove superseded
witnesses without writing a deletion record at all; the records then stop growing, and a returning
witness costs one removal at the next write.

## 3. Saves and merges, program by program

"The state" is the state entry with its history; "witness", "revocation" the small entries beside
it. "Gate" is what the plugin serves at its next read.

| Program | A save | A merge of the state | Witnesses and revocation entries | Ties | The gate afterwards | Basis |
| --- | --- | --- | --- | --- | --- | --- |
| KeePass 2.62, Synchronize, Overwrite if newer | keeps all | later whole, earlier in history | arrive; Synchronize carries and applies records | arriving wins, open copy nowhere; the plugin's memory holds the open one | complete; refuses a tie merged without the plugin; wrong past the window (2.2) | [M design]; [M here W1] |
| KeePass 2.62, Overwrite existing | keeps all | arriving current, open in history | arrive; no records copied | as its normal case | complete | [M design] |
| KeePass 2.62, Keep existing | keeps all | open current; arriving nowhere when it is later | arrive | | refuses until a synchronize | [M design] |
| KeePass 2.62, Create new IDs | keeps all | a copy, counts nothing | copies count nothing | | unchanged; Delete Duplicate Entries then removes the state entry (2.6) | [M design]; [M here D1] |
| KeePassXC 2.4.2 to 2.8.0 | keeps all | later whole, earlier in history; no history kept for a loser behind ten newer items | arrive; **no deletion applied or copied**: deleted witnesses, revocation entries and a deleted state entry all come back | target wins, source nowhere | complete; refuses a tie; false refusal past the window (2.3) | [M design]; [M keepassxc]; [M here W2] |
| Keepass2Android 1.15-r6 | custom strings and flags kept | KeePassLib's Synchronize: later whole, earlier in history; history cut to the limits at every load | arrive; records merged and applied by time | **arriving wins, open nowhere** | complete, or refuses at a tie (two plugin hosts, or a hand edit in the same second) | [R android 1.1, 1.3, 3.1]; 18 of 18 merges of a changed entry [M spike]; for this entry [A] |
| KeePassDX 4.5.8 | custom strings kept | later whole, earlier in history; equal times move the entry to the arriving copy's group | arrive; arriving records applied by time and **not kept** | open wins, arriving nowhere | complete, or refuses at a tie; its output carries no records to the next copy | [R android 1.1, 3.1]; [A] for this entry |
| KeePassDX 3.2.0 | custom strings kept; group custom data deleted, which holds nothing here | as 4.5.8; an arriving entry whose group is missing is dropped | as 4.5.8 | nothing happens; arriving lost | as 4.5.8 | [R android 1.5, 3.1]; [A] |
| KeePassDroid 2.7 | custom strings kept; **node custom data deleted, which holds nothing here** | no merge; every save over a changed file is a whole-file write | as the file it writes | | **2.1: silent rollback** | [R android 2, 4.2]; small strings [M spike] |
| kdbxweb 2.1.1 (KeeWeb) | keeps all | later whole; a history item the arriving copy lacks and that is older than the arriving entry is dropped | arrive; **records skip a named entry whatever its time** | open wins, arriving nowhere | complete, or refuses (tie, a head dropped from history); a restored state entry dropped (2.7) | [R others 4]; [M here K1] |
| Strongbox 1.65.0 | history trimmed at every save | later whole | records by time, childless only | open current, **arriving filed in history** | complete | [R others] |
| KeePassKit (MacPass) | | later whole | records by time | **neither kept, arriving lost** | refuses at a tie | [R others] |
| keepass-rs | | later whole; `kp-purge-history` removes all history | | **both kept** | complete; refuses if history was purged before KeePass read it | [R others] |
| KeePassium | | no merge; "overwrite remote" | | | 2.1 | [R others] |
| A synchronizer | the file as written | none: one file wins, the other is a conflict copy | | | 2.1, unless the operator synchronizes the conflict copy in KeePass | [R others]; [A] |

Equal times are not exotic: whole seconds [R android 1.4; M keepass 1c], and a phone's clock
decides its edits [A]. With one plugin host, though, the state entry is written in only one place,
so a tie needs a hand edit of it on another device in the same second as a plugin write [A, from
the design's write path].

## 4. The tree changing under the document

The tree is merged node by node by programs that know nothing of the plugin. Each row: what can
happen, what the gate then answers, whether access widens.

| Change | By whom | The gate | Widened? | Basis |
| --- | --- | --- | --- | --- |
| A node with a rule of its own moved | any program, any merge | its rule follows its identifier | no | [M rules-central, scenario 10, as the design cites] |
| A node with no rule moved out of a sealed group by a person | a person on a phone | governed by where it lands, as today | yes, by the person's act | design line 432 |
| The same, done by a merge with nobody moving anything | KeePassDX 4.5.8 at equal times; KeePassXC then KeePass | governed by where it lands; the closed list does not see it | **yes** (2.5) | [R android 3.1]; [M keepassxc 4, 9] |
| A node moved into a closed group | any | refused until confirmed | no | design line 432 |
| Renamed | any | nothing changes | no | design line 433 |
| Deleted into the bin | any; KeePassDX moves its time | not served; the rule waits | no | [R inventory P15] |
| Deleted for good | any | the rule waits; the record travels by KeePass Synchronize and Keepass2Android, not by KeePassXC or KeePassDX | no | [R android 3.1]; [M keepassxc 4] |
| Brought back | a KeePassXC merge; KeePass when the other copy changed it later; KeePassDX recreating a missing parent | its rule applies; a node on the `gone` list refuses | no | [M keepassxc 4]; [M keepass 2.2]; [R android 1.5] |
| A group deleted in one copy while the other added an entry to it | KeePass keeps the group with the new entry; KeePassDX 4.5.8 recreates it; KeePassDX 3.2.0 drops the entry; kdbxweb drops the group with everything in it | where the group survives, its rule survives with it, keyed by identifier: better than today, where the rule rode in the group's own custom data | no | [M keepass 2.2]; [R android 1.5]; [M wildcard-critique 2.1] |
| Created on a phone in a group that is not closed | any | inherits, as today | no | |
| Created anywhere under a closed group, which after any top-level narrowing is the whole tree | any but KeePass's Add Entry and the API | refused until confirmed; a new group refuses everything moved into it | no; costs presses (2.8) | design lines 478-490 |
| A copy of a node whose own rule narrows | KeePass Duplicate, a Ctrl drag, KeePassXC Clone, a phone copy | refused where it lands under the closed parent; served by its place elsewhere | **yes** outside the closed parent (design 5.3) | design lines 459-466; landing places for KeePassXC and the phones [A] |
| Import with Create new IDs | KeePass | arriving nodes refused only under a closed root; then Delete Duplicate Entries removes the state entry | yes for arriving copies of narrowed nodes unless the root is closed; then the cliff (2.6) | [M design]; [M here D1] |
| Tree newer than the document: phone edits, document untouched | any | each node as its row | as the rows | |
| Document older than the tree, from a merge | any merge | the step puts the newer one back, merges, or refuses through the witness | no, within the window; past it, 2.2 | [M design]; [M here W1] |
| Document and tree both older, from a whole-file write | KeePassDroid, an overwrite, a synchronizer | the old state, served | **yes**, and a revocation undone (2.1) | [R android 4.2] |
| Document newer than the tree | any | rules for absent nodes wait | no | design line 441 |

The design's own claim for this half, "moves, renames and deletions are handled better than today;
copies are the hole" (line 60), holds for what a person does. What it leaves out is that merges move
nodes too (row three), and that the closed groups were built for copies and do nothing for that.

## 5. Chains through a phone and back

| Chain | What happens | The gate at the end | Basis |
| --- | --- | --- | --- |
| KeePass revokes `X`; Keepass2Android, holding the older copy, saves over the changed file and the operator chooses Synchronize; KeePass reads the result | the phone's state entry is older, so `v2` is current and `v1` in history; `R(X)` and `W(v2)` arrive; `W(v1)` deleted by the arriving record | `v2`, `X` refused | [R android 1.1, 3.1, 4.2] |
| The same with KeePassDroid | the phone's file written whole over the desktop's | **`v1`, `X` admitted**, after KeePass's next reload | 2.1 |
| The same with KeePassDX, Merge data | as Keepass2Android, but no record kept in the phone's file | `v2`, `X` refused; a third copy still holding `W(v1)` later brings it back, recognized within the window | [R android 3.1] |
| A hand edit of the state entry on the phone (title, notes), or KeePassDX moving it to the bin and back, which moves its time | the stale entry wins every later merge by time; the newer version is in history | the step puts the newer one back, served at once, written at the next save | [M design S4]; [R android 1.4] |
| KeePass, then KeePassXC on a laptop over many months, then KeePass opens the laptop's file | witnesses pile up in the laptop's file | false `503 undecided` once 256 versions have passed (2.3) | [M here W2] |
| Two KeePass hosts write in one second; the phone merges both files | Keepass2Android lets the arriving copy win; one version nowhere, its witness arrives | `503 undecided`; Continue; when the lost version turns up its narrowing is discarded (2.4) | [M here C1]; [R android 1.1] |
| A copy months behind is synchronized into the desktop | its version is a second head | widening, wrong refusals or a lost narrowing, by reading (2.2) | [M here W1, W1b] |

## 6. Claims of survival in the design and their basis

| Claim (design line) | Basis given | Holds? |
| --- | --- | --- |
| After every merge the entry held one copy's entry whole, never parts of two (34-36) | M, 128 cells; R android | yes |
| "In every measured cell the gate ended with the complete state of both copies or refused. It never served a stale or partial state" (54-56) | M, 128 cells, 10 chains of 12 rounds | for at most 25 generations, under a window of 64 where the text says 256; past the window it serves a removed profile back or refuses what it should serve (2.2) [M here] |
| "Over all eight scenarios, every one of the 149 output lines that carries a document carries the complete one" (352) | M | the probe's merge has no row for old stamps (`Lib.ps1:128-146`), so line 337 was never run |
| Witnesses brought back by a merge without deletions are recognized and removed (670-672) | M, 12 rounds | not past the window [M here W2] |
| A lost version's witness "still arrives" on Android (370-371) | R android 1.5 | read only |
| Keepass2Android behaves as KeePass without the plugin (721) | R; M spike for a different entry | assumed for this entry |
| KeePassDX: complete, or refuses at a tie (722) | R | assumed for this entry; its output keeps no record |
| KeePassDroid keeps the state entry through a save (723) | R; M spike for small strings | a 20 to 65 KB protected string through its save and its entry screen not run |
| The other programs behave "as KeePassXC" (724) | M others for kdbxweb; R | not kdbxweb with a record (2.7), not KeePassKit or kdbxweb at a tie, not keepass-rs after a purge (section 3) |
| "A merge version is a function of its parents" (322-324) | design | the probe makes merge versions with random identifiers (`Lib.ps1:41-45`, `:87-88`); not measured |
| `FileSavingPre` runs after `MergeIn` and before either file is written (315-317) | R | not run; if it does not, the tie the plugin's memory rescues is lost at the next lock |
| "Synchronize with that copy is the remedy" (378-379) | design | not after Continue (2.4) [M here C1] |
| "A revoked client comes back only if the entry is removed with no trace and a stale document wins and its loser is lost and the operator presses continue" (414-416) | design | a whole-file write is enough (2.1) |
| Delete Duplicate Entries finds no two equal entries (569) | M keepass X10 | not after an import with Create new IDs [M here D1] |
| A state entry put back from a file survives a synchronize with the backup (607-610) | M for KeePass | not through kdbxweb [M here K1] |
| A moved node: "no more than today" (432) | M rules-central | holds; today fails E5 there too (2.5) |
| Copies land in the original's group in KeePassXC, Keepass2Android and KeePassDX (465-466) | A | to measure |
| The entry dialog's undo on the state entry is guarded (707-712) | R | not run |
| A history limit of 0 or a document over the size limit refuses (825-827) | M | holds |

## 7. What would have to be measured, in order

1. **On the emulator, with the state entry**: a Keepass2Android merge of a file whose state entry
   is newer, and of one whose entry was hand-edited later (is the newer version in history, and
   still after its next load); KeePassDroid's save of a 65 KB protected custom string and what its
   entry screen does to it; a KeePassDroid save over a file changed underneath, to put a number on
   2.1 in a setup of one KeePass and phones.
2. **A version vector in the probe**, over W1, W1b, W2 and C1, beyond 1000 versions, with three
   writers and every merge order.
3. **KeePass's lock and unlock** over a file another program replaced while locked: that the plugin
   reads the replacement and serves it, which is what makes 2.1 silent.
4. **KeePassDX 4.5.8 "Merge data"** with an entry moved on one side at equal times (2.5), and with
   the state entry.
5. **The determinism of a merge version** (line 322) across two hosts reconciling the same pair.
6. **The time rule** after a future-dated edit with two hosts (2.9).
7. **KeePass's window**: File then Import with Create new IDs, then Delete Duplicate Entries, as
   D1 does with the same KeePassLib calls.

## 8. What the design should record if it is taken

- That a whole-file write, which KeePassDroid makes at every save over a changed file, rolls back
  revocations silently, and that the gate cannot know it without something outside the file.
- That "seen" must not be cut, or must be a version vector.
- That Continue acknowledges and does not subsume.
- That merges move nodes, and that the closed groups do not see it unless they pin positions.
- That the state entry's fixed identifier cannot be changed, so a deletion record naming it is
  permanent trouble for kdbxweb.

## 9. What was run for this critique

All from a console in Windows PowerShell 5.1, no window, invented databases, about twenty seconds in
all. Nothing under the worktrees or the main checkout was written.

- KeePassLib from a copy of `.keepass\2.62\KeePass.exe` (SHA-256
  `AFB89B74…88C89C3F3`, equal to the original), with the design's `Lib.ps1` unchanged for the
  document, the witnesses and the plugin's write. `Survival.ps1` adds the step with a choice of
  merge (the probe's, and the design's last row read the most favorable way) and of how heads are
  judged, Continue as designed and as an acknowledgment, and the window set to 256.
- W1, W1b: KeePassLib `MergeIn(..., Synchronize)` with the plugin's memory, 20 and 300 versions.
- W2: 300 versions, `keepassxc-cli` 2.8.0 `merge -s` every 25 (the archive the `keepassxc` ground
  report verified), then a KeePassLib load of each file.
- C1: two writes in one second, a KeePassLib Synchronize with no plugin, Continue both ways, then a
  Synchronize with a copy from before.
- K1: AES-KDF at one round; kdbxweb 2.1.1 through the `kw.js` the wildcard critique wrote, both
  directions, and KeePassLib Synchronize as the control.
- D1: KeePassLib `MergeIn(..., CreateNewUuids)` and `PwDatabase.DeleteDuplicateEntries`.
