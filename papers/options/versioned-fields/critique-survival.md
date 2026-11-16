# Critique of `versioned-fields`: survival across programs

Written 2026-11-15 against `option-versioned-fields.md` as it stands, KeePassGate master `a42e38aa`,
KeePass 2.62. The lens is survival: every save and merge in the ground reports, walked through the
design one datum at a time, looking for a sequence that loses, rolls back, duplicates or resurrects
something. The design's own list of what it does not solve (its section 12) is repeated only where
it understates.

## How statements are marked

- **[M here]**: run for this critique, from a console, over databases made with invented content.
  Everything is under
  `spikes\storage\probes\versioned-fields\survival\`:
  `probe\Program.cs` (a copy of the design's own probe with additions, section 8), `scripts\Run.ps1`,
  and the output in `out\h5.txt`, `out\newids.txt`, `out\gone.txt`, `out\chain.txt` and
  `out\refined.txt`.
- **[M design]**: measured by the design's own probe, whose output under
  `spikes\storage\probes\versioned-fields\design\out\` I read, or rerun here with the same code.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M inventory]**: measured in
  that ground report, cited by its section or experiment.
- **[R ...]**: read from source, with the ground report that names tag, file and line, or read here
  in the file named. **[R plugin]** is the review4 worktree at `a42e38aa`.
- **[A]**: assumed. Neither run nor read.

## 1. Verdict

**Viable with changes.** The core holds where the design claims a measurement: an entry only one
copy holds arrives in every KeePassLib merge method and every KeePassXC release run, nothing the
gate decides depends on a modification time, a per-key time, custom data or a group's fields, and a
closing version counts from every place a merge can leave it among the plugin's own entries.
Section 2 lists what survived.

Four problems are serious. Three fail toward access; the fourth is unbounded growth under the most
common desktop program after KeePass. None needs a hand deletion.

1. **P1.** KeePass's own File then Import, with the "Create new IDs" it preselects, carries no
   revocation, retirement, removal or narrowing from the copy that made it into a stale copy,
   because the copies arrive without the write mark and unmarked entries are not read. Measured: a
   revoked client admitted and a retired authority serving. Today's store carries both through the
   same import, measured here on 2.62.
2. **P2.** A KeePassXC merge never applies a deletion record, so every state pair the plugin ever
   wrote comes back and stays, in both files. Every renewal ends in a revocation, which this design
   writes as a full state pair, so the store grows by a full copy of the state at each renewal.
   Measured: 8 renewals took the store from 3 entries and 6,481 characters to 19 entries and 62,297,
   against 3 and 11,475 under KeePass's Synchronize.
3. **P3.** The design's rule for reading a node's history is narrower than the rule its probe ran.
   Measured with the design's rule on its own pair H5: a KeePassXC merge of the stale copy into the
   copy that narrowed an entry **widens** that entry again. The design reports the narrowing kept in
   all 16 cells.
4. **P4.** The Data page's "Remove rules of nodes that are gone" undoes the very protection that
   section 6.3 keeps those rules for. Measured: after the press, a KeePassXC merge brings the
   deleted entry back under its older, wider rule; without the press the same merge keeps it
   narrow.

Each has a change that fixes it. Seven minor problems follow, P5 to P11.

## 2. What survived the attack

| Claim | Result | Basis |
| --- | --- | --- |
| Write entries added in two copies both arrive | held in every KeePassLib method (but Create new IDs, P1) and every KeePassXC release run, both directions | [M design, 160 cells]; [M here, `h5.txt`, `gone.txt`, `chain.txt`] |
| A revocation reaches a stale copy through a KeePassXC merge and refuses the record that comes back with it | held | [M design, R1, R2]; [M here, `chain.txt`: c1 refused after 8 rounds] |
| The plugin's own deletion records, one second after the time in the identifier, remove superseded entries under KeePass's Synchronize in both copies | held: the store stayed at 3 entries over 8 renewals | [M design, `sentinel.txt`, R2]; [M here, `chain.txt`] |
| Superseded entries that come back count for nothing | held: every verdict right after 8 KeePassXC rounds, 19 entries present | [M here, `chain.txt`] |
| A version edited by hand is found whole in the entry's history | held | [M design, F2] |
| Two copies' rule edits read as what both allow; a removal wins over an addition made without knowledge of it | held | [M design, C, P] |
| Versions of another store planted in a marked entry change nothing | held | [M design, F1] |
| A KeePassXC plain save keeps every write entry, field, flag, marker and deletion record | held | [M design, `dumps.txt`] |
| KeePass, then Keepass2Android, then KeePass | the union; the plugin's records joined and applied to entries older than them | [R android 1.5, 3.1] |
| KeePass, then KeePassDX, then KeePass | the union; KeePassDX applies the records and keeps none; the newest pair still refuses | [R android 3.1] |
| The deletions of node custom data by KeePassDroid and KeePassDX 3.2 and 3.3, and KeePassXC's replacement of the database's custom data | do not reach the store, which uses neither | [M spike]; design section 3.2 |

## 3. Problems

### P1, serious: an import with new identifiers carries no closing version

**What the design rests on.** Section 2 point 6: "What closes counts wherever it is found ... A
revocation, a retirement, a removal or a rule counts from any entry". Section 5.1 step 1 and the
File then Import row of section 7.2 narrow that to entries whose identifier carries the write mark:
"entries without the mark are not read, so the copies of the store count for nothing". The design
measured that import in one direction only, a stale copy imported into the fresh one
(`newids-detail.txt`, `R-base` into `R-A2`) [M design].

**The other direction, measured** [M here, `newids.txt`]:

1. Copy A revokes client c1 (the design's `R-A1`), or revokes and tidies (`R-A2`).
2. Copy B, stale, still holds c1's pairing entry.
3. On B, the operator brings A in with File then Import of a KeePass KDBX file, leaving the
   preselected "Create new IDs" [R keepass 2.4, `ImportMethodForm.cs:37`, `:69`]. A trigger "Import
   into active database" does the same by default [R keepass 2.4,
   `EcasDefaultActionProvider.cs:463-467`]. The probe runs KeePassLib's
   `MergeIn(..., CreateNewUuids)`.
4. A's entries arrive under new random identifiers, without the mark, and no deletion record comes
   with them.

**What the gate does** on B afterwards:

| A brought in | Create new IDs | Synchronize, for comparison |
| --- | --- | --- |
| `R-A1`, c1 revoked | **c1 admitted**; X keeps B's wider rule | c1 refused; X narrowed |
| `R-A2`, revoked and tidied | **c1 admitted**; X keeps B's wider rule | c1 refused; X narrowed |
| `R-A2` after Recreate (retirement of fpA, new authority fpB, client c9 paired under it) | **fpA serves**, every client B paired under it admitted, c1 included; fpB and c9 count for nothing | fpA retired, fpB serves, c9 admitted |

**Today's store through the same import** [M here, `cdcontrol` in `newids.txt`]: the stale copy
afterwards holds `KPG.Revoked.c1` and `KPG.RetiredAuthority.fpA`, under Create new IDs as under
Synchronize. That is the spike's 2.61.1 result ("a key added once and never changed survives every
KeePass merge and import method") [M spike §2; M inventory P2], measured again on 2.62. So this is a
regression, and it needs no hand deletion: the operator did merge, in the way KeePass offers first.

**Why it is worse than "a stale copy that a merge told nothing"** (section 12). The copies stay in
B, hidden from the API (section 9 item 2) and read by nothing. A later state pair written in B
neither deletes them nor carries them, because they are not marked. An operator who then retires A's
file believes B is current.

**Other ways a mark is lost** [R keepass 9; R android 4.2]: KeePass's repair of duplicate
identifiers at open, and KeePassDX's fix for duplicate identifiers. How often either happens is [A].

**Change.** Count a closing version of the home store (`retire`, `revoke`, a `name` removal, `rule`,
`bin`) from every entry that holds a version field, mark or no mark, as section 2 point 6 already
states. Keep the opening versions to marked entries. Measured with the copies read as well, B
refuses c1, holds fpA retired and keeps X narrowed [M here, `foldcopies` in `newids.txt`]; with only
the closing versions read there, fpB serves nothing in B until a Synchronize, which fails closed.
(The probe's `foldcopies` read the opening versions from the copies too, so it also lists fpB in
service; and its client verdicts do not apply the authority, so it lists fpA's clients as admitted
where the gate would refuse them for chaining to a retired authority.) The store identifier already
keeps another file's versions out, and section 9 item 2 already hides every entry carrying
`KPG.Write`, so reading them adds nothing a client can reach or plant.

### P2, serious: under KeePassXC every state pair ever written stays, and every renewal writes one

**What the design rests on.** Section 6.1: "A write that closes something adds a state pair ...
each holding every version in force ... Then it deletes every other write entry". Section 6.3, "How
much": "A busy database, ten clients that each renew weekly, makes 520 small entries a year and a
tidy every eleven days; nothing accumulates in the tree".

**Two facts the sizing misses.**

- **A renewal ends in a revocation.** The client gives the old certificate up by presenting it,
  once the new identity is in place (`docs\pykeepassgate.md:280-286`; `CA.md:975`) [R plugin], and
  section 6.2 writes `DELETE /gate/certificate` as a state pair. So each renewal is a small entry and
  a state pair, not a small entry alone. A client may ask for a certificate of a day
  (`CA.md:976`) [R plugin].
- **KeePassXC never applies or copies a deletion record** [M keepassxc 2]. The design measured the
  consequence (pair R2: "in the other 11 cells 10 were left, the four returned ones counting for
  nothing") [M design] and did not carry it into its sizing.

**Sequence, measured** [M here, `chain.txt`]. Machine 1 runs KeePass with the plugin; machine 2 runs
KeePassXC and merges both ways with `keepassxc-cli merge` 2.8.0, which uses the same `Merger` as
KeePassXC's window and its merge on reload [R keepassxc 3]. Each round, one client renews: a small
entry for the new certificate, then a state pair carrying every version in force with the old
certificate's revocation added, every other write entry deleted with the plugin's record.

| After round | KeePassXC merging: write entries, version fields (distinct), characters | KeePass Synchronize merging: the same |
| --- | --- | --- |
| start | 3, 11 (6), 6,481 | 3, 11 (6), 6,481 |
| 1 | 5, 25 (8), 12,023 | 3, 15 (8), 8,605 |
| 4 | 11, 79 (14), 31,109 | 3, 21 (11), 9,835 |
| 8 | **19, 179 (22), 62,297** | 3, 29 (15), 11,475 |

Each KeePassXC round adds more than the last (5,542 characters in round 1, 8,412 in round 8),
because each state pair is a full copy of a state that grows, and none ever leaves. The deletion
records in machine 1's file went from 10 to 90, because each closing write deletes every returned
entry again. The compressed files grew less, 6,885 to 15,013 bytes, since the copies repeat each
other; KeePass holds them uncompressed [A].

**What the gate does.** Every verdict stayed right: c1 and every revoked renewal refused, c2
admitted [M here]. No access changes. What changes is cost and clutter:

- With the design's own state entry of about 17,000 characters, ten clients renewing weekly make
  520 state pairs a year, about 17.7 million characters kept in both files [A, arithmetic on the
  design's numbers]; daily certificates make seven times that.
- Every request compares every version field's object with the last request's: 0.15 ms with the
  walk against 0.13 ms without, for 142 fields in 10,030 entries [M design, `walk.txt`], so about
  0.14 µs a field. At some 62,000 fields after such a year that is of the order of 10 ms a request
  with KeePass's thread held, and a read that parses every field from nothing after each merge,
  at the measured 20 µs a version, of the order of a second [A, linear extrapolation].
- In KeePass alone the design's "about 585 deletion records a year" is about 1,560 for weekly
  renewals, three a renewal [M here, `chain.txt`: 13 to 34 in 7 rounds; A for the year].
- The clutter invites hand cleanup, which section 5.5 answers with `503 damaged` (P6).

This fails requirement A5 (bounded growth) and the survival requirement that the other programs
merge the data sanely, and it presses on A6 (cheap reads on KeePass's thread).

**Change.** Stop copying the state. A closing write adds only its own versions, written twice in two
small entries, and the plugin deletes only entries whose every version is about an expired
certificate. What KeePassXC then brings back is small and refused on its dates, and growth is
bounded by what is in force, as today. `after` is already cumulative, so nothing else in the reading
rule depends on a full state. The cost: the tidy goes, and a pair covers one act rather than all of
them. If the state pair stays, section 6.3 must state that a KeePassXC-merged pair of files keeps
every state ever written, and the reader must cache parsed versions by name across requests, since
a version never changes.

### P3, serious: the design's history rule rolls back a narrowing in the copy that made it

**What the design rests on.** Section 5.3: "It is the newest version and no write entry holds it:
the node's history items are read for a newer copy." Section 7.1, pair H5: "X keeps A's narrowing in
all 16, read from the entry's history". Section 8: "Deletes both state entries: ... rules are read
from the nodes". Section 14 concedes that the probe "reads a node's history always", and the probe
does so at `Program.cs:644-651` [R design probe].

**Sequence, measured with the design's own rule** [M here, `h5.txt`, the design's pair H5 rerun]:

1. Copy A narrows entry X (`reader` from read, list, update to read) and later tidies. X carries the
   new copy, its history the old.
2. Copy B, stale, changed X's password later, so B's X is newer and carries the old rule copy, and B
   holds the old rule's write entries.
3. Both tidy entries in A are deleted without a deletion record: in a program built on pykeepass
   [R others 3], or by KeePassDX applying a record and keeping none [R android 3.1]. The design
   concedes that this loses clients (section 12, "two are not seen"); it claims rules survive it.
4. `keepassxc-cli` 2.4.2 or 2.8.0 merges B into A. X becomes B's version, A's version goes into X's
   history, and B's old rule entries come back, since KeePassXC applies no record.
5. X's own copy is now the old version, and B's old rule entries hold it, so the design's rule reads
   no history.

**What the gate does.**

| Cell | The probe's rule (history always) | The design's rule |
| --- | --- | --- |
| into A, KeePassLib, four methods | reader read | reader read (A's own records remove B's old rule entries, so history is read) |
| **into A, keepassxc-cli 2.4.2 and 2.8.0** | reader read | **reader read, list, update: A's narrowing undone** |
| into B, Synchronize, Overwrite existing | reader read | reader read |
| into B, Overwrite if newer, Keep existing, keepassxc-cli 2.4.2 and 2.8.0 | reader read | reader read, list, update (B as it was) |

Before the merge, A served `reader read` from X's own copy [M design, H5 input]. So the cells into
A are a rollback in the copy that narrowed, not a stale copy left stale. Section 7.1, section 8 and
section 12 all understate this.

**Change.** Two options, both cheap to state:

- **Do not count a write entry that a deletion record names as holding the node's copy.** Measured:
  it reads history in both KeePassXC cells into A and gives `reader read`, and leaves the cells into
  B as they were [M here, `refined.txt`]. It fails where the records were cleared (Database
  Maintenance [R keepass 5]) or never written (pykeepass).
- **Read the history of every node on a request's walk, always**, and measure the cost on a full
  listing (section 15 item 5).

Either way the history itself is short-lived: KeePass keeps 10 items [M keepass 1b],
Keepass2Android trims at every load [R android 1.3], Strongbox at every save [R others 3]. Section 8
should state that rules are read from the nodes only while the nodes' history holds them.

### P4, serious: "Remove rules of nodes that are gone" reopens what keeping them prevents

**What the design rests on.** Section 6.3: "Never left out: ... the newest `rule` version of every
node, a taken-away rule included, even after the node is gone, because the node can come back from
a stale copy with an older copy on it. The tab offers to remove the rules of nodes that are gone, on
the operator's press." Section 8: "Deletes a ruled node: its rule stays in the state pair until
removed on the Data page".

**Sequence, measured** [M here, `gone.txt`]:

1. Copy A has X narrowed (the design's `R-A2`). A deletes X for good, as KeePass deletes: a deletion
   record dated now.
2. The operator presses "Remove rules of nodes that are gone". A state pair without X's rule versions
   is written and the old pair deleted.
3. `keepassxc-cli` 2.4.2 or 2.8.0 merges the stale copy B into A. X comes back with B's older rule
   copy, because KeePassXC applies no record [M keepassxc 2], and B's old rule entries come back
   with it.

**What the gate does.** X reads `reader read, list, update`: the narrowing is gone, and nothing
shows. Without step 2 the same merge leaves X at `reader read`, because the pair still holds the
newer version, which replaces the older [M here]. KeePassLib's four methods into A keep X deleted,
because A's own record applies under every method [M here; M keepass 5]. History does not help: B's
X never held the narrowing [M here, `refined.txt`].

**Change.** Drop the press. Or let it write, per node, one compact closing version whose `after`
names every version that node's rule ever had, with the document `{"default":"deny"}`: a node that
comes back is then refused until settled on its tab, as a copied node is, and the cost is 17
characters per old version.

### P5, minor: one witness for long stretches

The design writes a refusal twice and counts on the second entry to cover a lost first. Three ordinary paths leave one
witness for a long time:

- **Accept.** Section 6.2 writes an accepted deletion as a small entry. The deleted entry's twin is
  then the only witness until the next closing write.
- **A deletion that leaves no record** (pykeepass [R others 3]; KeePassDX applying another copy's
  record [R android 3.1]). Section 8: "nothing is lost and nothing shows; the next state pair makes
  two again". Only a closing write or a tidy makes one, which in a quiet database is weeks [A].
- **The titles.** "KPG ... state" beside "KPG ... state (second copy)" reads to a person as a
  duplicate to remove [A].

**Sequence** [A, made of reads]: a person deletes the "(second copy)" in a program built on
pykeepass; weeks later another copy's hand deletion of the first reaches this copy through a
KeePassDX merge, which applies it and keeps no record [R android 3.1]; a stale pairing entry that
KeePassXC returned (P2) then admits the revoked client.

**Change.** Every write the plugin makes, the opening ones and Accept included, checks that the
newest pair is whole and writes a fresh pair when it is not; and neither entry is titled as a copy.

### P6, minor: superseded entries that another program touched, and hand cleanup

- **The plugin's deletion record loses to a touched entry.** KeePassDX moves an entry's modification
  time when it moves or recycles it [R android 1.4]; KeePassXC 2.7.12 and later do on `rm`
  [M keepassxc 6]. The plugin's record is dated one second after the time in the identifier, so
  KeePass's Synchronize then keeps the entry and drops the record [M keepass X3, "E back with B's
  change, tombstone gone"]. The entry returns at every Synchronize and no record names it again, so
  its opening versions count again; the newest pair still refuses them. Section 5.4's "a write entry
  exists in one version only" is false for such entries.
- **Hand cleanup closes the gate.** A person deleting the clutter of P2 writes records dated now,
  which section 5.5 answers with `503 damaged` in every copy a Synchronize or kdbxweb reaches,
  although the newest pair held everything they deleted.

**Change.** Each state entry lists the identifiers of the write entries it supersedes. A deletion
record naming a listed entry is benign whatever its time, and the plugin dates its own deletion one
second after the later of the identifier's time and the entry's modification time.

### P7, minor: the prune reads a clock

Section 5.4: "no clock takes part". Section 6.3 leaves out "an `admit` whose certificate has
expired, a `revoke` or `answer` about one", expired by the writing machine's clock.

**Sequence** [A, made of the design's rules]: two machines run KeePass with the plugin; machine L's
clock runs a month ahead. L makes any closing write and leaves out the revocation of c7, whose
certificate has two weeks left. A Synchronize carries L's records, so the pair that held the
revocation goes everywhere. c7's pairing entry comes back from a copy KeePassXC kept (P2). Machine D,
whose clock is right, admits c7 until its end.

Today's prune reads the same clock [R inventory D4], so this is no regression; the claim in section
5.4 is what is wrong. **Change**: keep a revocation until its `notAfter` plus a margin, 30 days for
example, at 205 characters each.

### P8, minor: removing a version field is silent

Section 5.5 closes the gate on a field that does not read and on a deletion record. A version field
that is removed, rather than edited, leaves neither. Where the edit leaves a history item the
version is found there, which held [M design, F2]. Without history it is gone: pykeepass adds no
history item on an edit [R others 3]; KeePass with a history limit of 0 keeps none [M keepass 1b];
Strongbox trims at every save [R others 3] and Keepass2Android at every load [R android 1.3].

**Sequence** [A, made of reads]: a person removes the `revoke` field from both state entries in a
program built on pykeepass; no record, no unreadable field, no `503 damaged`; a stale pairing entry
present (P2) then admits the client. **Change**: each write entry holds a manifest of its version
names, a count and a hash in `KPG.Write`, and a write entry whose fields do not match it is damaged.

### P9, minor: two sentences that would widen if built as written

- Section 5.3: "a profile only some name keeps nothing". The probe gives such a profile an empty
  array (`Meet`, `Program.cs:587`) [R design probe], which seals it. Silence would inherit from
  above, wider than the version that named it. The text should state "an empty array".
- Section 6.3: "Never left out: ... the newest `rule` version of every node". After a merge a node
  can have two versions neither replaces. A state pair that carries one and deletes the pair holding
  the other drops that copy's narrowing for good, in the copy that made it. The probe never wrote a
  pair after holding two (pair C stops at the merge) [R design, section 14]. The text should state
  "every version of a node's rule that no other replaces".

### P10, minor: a retirement in one store does not reach another

Section 5.2: "Versions of any other store count for nothing, whatever they are." Two copies of a
database copied before its first authority each create one, so the file holds two stores and
answers `503 undecided` [design 5.2]. If "Keep this authority" writes fpB's retirement in fpA's
store, and home later moves to fpB's store (after fpA is deleted, "the operator names the store on
the Data page"), the retirement counts for nothing and fpB serves again with its old records. If it
writes the retirement in fpB's store, the plugin writes into a store that is not home. The design
does not state which [R design]. **Change**: a `retire` counts for the authority it names in whatever
store it is found; a planted one can only refuse.

### P11, minor: the marker's identifier is derived from the group's

A marker deleted by hand leaves a record for its identifier; ruling the group again makes the marker
under the same identifier; kdbxweb removes a node a record names whatever its time [R others 3],
the mechanism `critique-store-entries-survival.md` P3 measured for a group. The marker then drops in
every KeeWeb merge with a copy holding the record. The pair still holds the rule, so the gate is
unchanged; only the node's copy goes. **Change**: a random identifier, with the group's identifier
in a field, which section 5.1 step 3 already reads for copied markers.

## 4. Datum by datum

| # | Datum | Survives | Fails | Gate then |
| --- | --- | --- | --- | --- |
| 1 | Authority | every save and merge run; one entry, loss fails closed [M design] | not carried by an import with new identifiers (P1); retired elsewhere and served again across stores (P10) | serves a stale or retired authority (P1, P10) |
| 2 | Retirement | the pair, every merge run [M design for the shape] | not carried by an import with new identifiers (P1, measured); a store change (P10) | a retired authority serves (P1, measured) |
| 3 | Admission records | union; a stale record returned is refused by the revocation [M design; M here] | superseded records return and accumulate under KeePassXC (P2, measured); opening versions of touched entries count again (P6) | refused while the closing versions hold |
| 4 | Revocations | the pair, every merge run [M design; M here] | not carried by an import with new identifiers (P1, measured); one witness for weeks (P5); pruned by a fast clock (P7); field removed silently (P8) | a revoked client admitted (P1 measured; P5, P7, P8 assumed) |
| 5 | Standing answers | the strictest unreplaced answer [design 5.3] | as revocations, with nothing admitted | a dialog where `409 forever` was meant |
| 6 | Profile list | removal wins, measured in pair P [M design] | removal not carried by an import with new identifiers (P1) | a removed profile grants in the importing copy |
| 7 | Access rules | what both allow; the pair and the node [M design] | import with new identifiers (P1, measured); history rule (P3, measured); the press (P4, measured); wording (P9) | a narrowing undone (P1, P3, P4) |
| 8 | Bin markers (optional) | as a retirement | as P1 | |
| | Accept | | leaves one witness (P5) | |

## 5. Program by program

| Program and act | What the store meets | Problems found here |
| --- | --- | --- |
| KeePass 2.62 Synchronize (File then Synchronize, the save that finds the file changed) | union, the plugin's records carried and applied; a store that does not grow [M design; M here] | P6 (a touched entry's record dropped) |
| KeePass Overwrite if newer, Overwrite existing, Keep existing | into the acting copy its own records apply; into a stale copy nothing is learned and superseded entries stay there [M design] | P3 (into B, as stale as before) |
| KeePass File then Import, Create new IDs (preselected), and the import trigger | copies without the mark, no records | **P1** |
| KeePass Delete Duplicate Entries after such an import | originals deleted, `503 damaged` [M design] | |
| KeePass Database Maintenance: deleted objects cleared, history deleted by age | superseded entries return for good; node history goes [R keepass 1b, 5] | P3 (the refined condition), P6 |
| KeePass Empty Recycle Bin, Shift+Delete, history limit 0 | hand deletions with records, `503 damaged`; field removal without history | P8 |
| KeePassXC 2.4.2 to 2.8.0, save | kept whole [M design] | |
| KeePassXC merge, command line, window, reload; KeeShare | union, records never applied or copied [M keepassxc 2; R keepassxc 3] | **P2**, **P3**, **P4** |
| KeePassXC 2.7.12 and later, `rm` and `mv` | the entry's modification time moves [M keepassxc 6] | P6 |
| Keepass2Android 1.15-r6, 1.08d-r5 | a port of Synchronize: records joined and applied [R android 3.1]; history trimmed at every load [R android 1.3]; KDBX 3.1 output [M spike] holding all of it [A] | P3 (history), P8 |
| KeePassDX 4.5.8 | union, records applied and dropped, entries moved on equal times [R android 3.1]; a move or recycle moves the entry's time [R android 1.4] | P5, P6 |
| KeePassDX 3.2.0, 3.3.0 | arriving entries added when their group exists [R android 1.5]; strings kept on save [R android 2] | |
| KeePassDroid 2.7 | no merge; strings kept [R android 2]; every save over a changed file overwrites it [R android 5.5] | section 12 |
| kdbxweb, KeeWeb | a record removes what it names whatever its time [R others 3] | P6 (hand cleanup reaches every copy), P11 |
| Strongbox, KeePassKit, keepass-rs | union, records merged by time [R others 3, 4]; Strongbox trims history at every save | P3 (history), P8 |
| KeePassium | no merge; overwrite on conflict [R others 3] | section 12 |
| pykeepass and programs on it | deletions without records, edits without history [R others 3; M others 3] | P3, P5, P8 |
| A file synchronizer's conflict copy | one file whole [R others 3] | section 12 |

**Three-way chains walked.** KeePass, then Keepass2Android, then KeePass: the union, records joined,
nothing lost [R android]. KeePass, then KeePassDX, then KeePass: the union, records dropped by
KeePassDX, the newest pair refusing what returns [R android]. KeePass, then KeePassXC, then KeePass:
every verdict right and the store growing (P2, measured); with a press on the Data page in between,
a narrowing undone (P4, measured). KeePass, then an import with new identifiers into an older copy:
a revocation and a retirement lost (P1, measured).

## 6. Survival claims that rest on reading or assumption

Each has to be measured before the design can rely on it.

1. **Keepass2Android's merge of this shape**: arriving write entries added, and the plugin's records,
   dated one second after the identifier's time, applied to an entry that is strictly older
   [R android 1.5, 3.1]; never run for this shape [M spike ran only an entry the open copy held].
2. **KeePassDX 4.5.8 and 3.2.0 merges**: arriving entries added, records applied and dropped, 3.2.0's
   need for the parent group [R android]; none run.
3. **Every Android save of the store**: a state entry of some 60 custom strings and 17,000 characters,
   a protected custom string of 3,063, dotted names of 40 characters, the marker entry and the
   records with their times [R android 2]; entries as wholes were measured [M spike], custom strings
   never were. Keepass2Android's KDBX 3.1 output holding all of it is [A].
4. **That no program moves a write entry's modification time**: section 5.4 needs it, and KeePassDX
   and KeePassXC 2.7.12 do on a move or a recycle [R android 1.4; M keepassxc 6] (P6).
5. **That no program ages deletion records out by their date**: the plugin's records are dated at the
   entry's creation, often months back, so any such program drops them at once [A]; not read for
   any program.
6. **"Nothing accumulates in the tree"** (section 6.3): contradicted under KeePassXC [M here, P2].
7. **"X keeps A's narrowing in all 16"** (section 7.1, H5): holds only for the probe's rule, not the
   design's [M here, P3].
8. **The cost of reads** (section 5.6) was measured on a compact store of 142 versions; at the size
   P2 reaches it is [A].
9. **That the two entries of a pair are not lost together in ordinary use** [A], and that a person
   leaves the "(second copy)" alone (P5).
10. **kdbxweb and KeeWeb over this shape** [R others 3]; strings kept on a save [M others 3].
11. **Strongbox, KeePassKit, keepass-rs and KeePassium** [R others 3, 4].
12. **KeePass's window**: File then Synchronize, File then Import in both directions, Delete
    Duplicate Entries, Empty Recycle Bin, Database Maintenance's clearing of deleted objects
    [R keepass]; only KeePassLib calls were run.
13. **KeePassXC's window merge, its merge on reload, and KeeShare** [R keepassxc 3].
14. **The entry dialog path**: `EntryStrings`, the `FormClosed` test of the entry, the Escape
    question [R 2.62 as the design cites; A for the question].
15. **That "no KeePass dialog copies a group's list of entries and puts it back"** [A, from another
    critique's checked item].
16. **That a version cannot come to carry another store's identifier except by an edit that also
    fixes its `sum`** [A].
17. **That no program normalizes string values** (Unicode forms, line ends): every copy of a version
    would then fail its `sum` and close the gate [A].

## 7. What the design should change in its own text

- Section 2 point 6 against sections 5.1 and 7.2: one of them is wrong (P1).
- Section 6.3, "How much": a renewal ends in a revocation; KeePassXC keeps every state (P2).
- Sections 5.3, 7.1, 8 and 12 on node history (P3).
- Section 6.3 and the Data page on removing rules of nodes that are gone (P4).
- Section 5.4: a clock takes part in the prune (P7), and a write entry can exist in two versions (P6).
- Sections 5.3 and 6.3: the two sentences of P9.

## 8. What was run for this critique

**The probe.** `probe\Program.cs` is a copy of the design's probe
(`spikes\storage\probes\versioned-fields\design\probe\Program.cs`), built as `Vfs.exe` with the .NET SDK against
`.keepass\2.62\KeePass.exe`, used as a library, with no window. Its `make` builds
the design's own sets, so the pairs are the design's. Added:

- `fold2`: the design's own history rule of section 5.3, beside the probe's `fold`, which reads
  history always; `fold3`: the same with the refined condition of P3's change; `foldcopies`: the
  probe's own option to read entries without the mark.
- `recreate`: a state pair with the retirement of fpA, fpA's entry deleted, a new authority fpB and a
  client c9 paired under it.
- `gone`: X deleted for good with a record dated now, then the press as a state pair without X's
  rule versions.
- `close`: one renewal as the design writes it, a small entry for the new certificate and a state
  pair carrying every version in force with the old certificate's revocation, every other write
  entry deleted with the plugin's record (an approximation: it prunes nothing by date).
- `count`, and `cdcontrol`, which puts today's keys in a database's custom data and imports the
  revoking copy into the stale one by Create new IDs and by Synchronize.

**The other programs.** `keepassxc-cli` 2.4.2 and 2.8.0, the copies the KeePassXC ground report
downloaded. Their archives hash to the values the spike pins in `keepassxc.sha256`
(`f2f0b870...981264` and `48ff4f08...fc828`), and both executables carry a valid Authenticode
signature from DroidMonkey Apps, LLC, checked again here. They ran `merge -s` only, with no window.
KeePassLib 2.62 merged under all five methods.

**Limits.** The probe's rules use its own short syntax, not `AclDocument`'s. Its client verdicts do
not apply the authority a certificate chains to (P1 names where that matters). Its store identifier
is fixed. Two KeePassXC releases were run, not four. No Android program, no KeePass window and no
test tier were run, and nothing was written to either worktree.
