# Option `crdt`: the store as conflict-free replicated data types

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. The evaluation asks
whether a conflict-free replicated data type (CRDT) could help in any of the options.
This is the CRDT taken as the design, for every datum the plugin keeps in a database, with
an executable model that searches short histories for counterexamples. Three rules of this design
came out of that search and are marked where they appear.

## 0. How statements are marked

- **[M report]**: measured, in the report named: `keepass`, `keepassxc`, `others`, `measured`
  (the ground reports), `spike` (`spikes/revocation-merge`), `android` (`measured-android.md`, the
  emulator runs of today, sections 2, 3 and 5.1 as they stood at 19:40; its KeePassDX section was
  still empty), `store-entries`, `rules-linked`, `wildcard` (those designs' own probes), `crit`
  (a critique's probe, named).
- **[R report]**: read from source in that report, which names tag, file and line.
  **[R plugin]**: read by me in the product at `a42e38aa`, file and line given.
- **[model]**: a result of the executable model in `crdt-model\crdt_model.py` (section 12). A
  model result shows what this design's rules do under the transport as modeled. It is neither a
  measurement of a program nor a reading of one.
- **[A]**: assumed. Neither run nor read.

Nothing was run against KeePass, KeePassXC or an Android program for this report. The entry
shape used here (an ordinary entry with custom strings in a group of the plugin's own, written
once) is the shape `option-store-entries.md` and `option-rules-linked.md` measured through
KeePassLib 2.62 and four `keepassxc-cli` releases, and `measured-android.md` measured through
Keepass2Android and KeePassDroid. What this design adds to that shape is listed in section 16 as
still to measure.

## 1. The design on one page

**The observation.** The one merge every program gets right is the union of entries: an entry
only one copy holds arrives, in all five KeePass methods, four KeePassXC releases and
Keepass2Android in both directions [M keepass 1d; M keepassxc 2; M android 2.2], and by reading
in KeePassDX [R android 1.5]. So a `.kdbx` handled by programs that know nothing of the plugin
already is a replicated grow-only set of entries, with faults. Everything else the plugin needs
is a data type whose state is a subset of that set and whose value is computed by a pure function
when the gate reads. That is a state-based CRDT with a lossy transport, and the design below is
what the field's own rules give when loss is not assumed away.

1. **An element is one ordinary entry, written once, never changed**, in a group of the plugin's
   own. Its identifier is a keyed digest of its content, and its content holds a fresh random
   number. So every act of writing has an identity of its own, the same act is the same entry in
   every copy, and no act can ever produce an identifier that existed before.
2. **"Later" is decided by descent, never by a clock.** An element names the elements of the same
   object that its writer had read (`after`). What an element names is replaced by it. What
   nothing names stands. Two elements of which neither names the other never saw each other.
3. **Three types cover every datum.**
   - *A two-phase set* for authorities and admission records: an addition, and a refusal
     (retirement, revocation) that is final.
   - *A remove-wins set* for the profile list: an addition counts only if it has seen every
     removal of its name that the file knows.
   - *A register whose concurrent versions meet as the narrower* for access rules, standing
     answers to renewals and per-node meta data such as the recycle bin markers.
4. **Every join moves toward less access.** A refusal joins by union and nothing takes one away.
   Two versions that never saw each other give what both give. Whatever the gate cannot place
   in the order of descent it treats as concurrent, which is the narrowing direction. Widening
   takes an element that names everything it overrides, and only the gate writes one, on the
   operator's word.
5. **Every decision whose loss would widen has two carriers and the refusals a third.** The
   entry itself; the *head*, an element written with every write of the gate that carries the
   refusals, the removals and the rule versions in force, text included; and for a revocation or
   a retirement a deletion record under an identifier only the gate can derive, which no program
   offers a person a way to delete singly.
6. **Loss is detected from what remains, and answered by refusing.** A store with no head in
   force, with a deletion record of a head that nothing in force replaces, with an entry that
   does not verify, or with a decision that no head accounts for, is *incomplete*: it serves
   nothing until the operator presses **Seal again** on the KPG tab. A node that is marked as
   ruled and has no rule refuses, there and below.
7. **Every element is authenticated** under a key the database holds inside its authority's
   document. A file that has not read this database cannot make an element of it, so a foreign
   file merged in adds nothing and takes nothing away.
8. **A rule names its node, and the node carries a mark that it is ruled.** The rule's reference to the node,
   being authenticated, is what gives. The node's own mark travels with every copy of the node,
   and a mark can only take away: a copy is capped by its original's rule, and a marked node
   with no rule refuses.
9. **Nothing is written when a database is opened, read or merged.** The join is computed at
   read. Pruning, the head and the witnesses ride on the gate's own writes.

**What the model found** [model, section 12.3]. In 480,000 random histories over two and three
copies and 127,522 exhaustive ones:

- with merges alone, by every program modeled, in any order and direction: no copy granted more
  than a copy that had kept everything it was ever given, and all copies that exchanged
  everything agreed with each other and with that ideal;
- with KeePass's Synchronize and Keepass2Android as the only merges, and deletions of any kind,
  hand edits, imports and planted entries on top: no failure;
- with merges that carry no deletion record (KeePassXC, KeePassDX, KeePass's import methods)
  and deletions: failures of two shapes only, both in the list below;
- no single deletion or edit of a store entry made a copy wider than it was;
- a revocation was undone only by a whole-file overwrite, or where the copy had never been given
  it.

**The honest weaknesses, first.**

- **A whole file written over another takes everything back**, the head and the witnesses
  included. KeePassDroid does this at every save over a file that changed underneath it
  [R android 4.2], and KeePassDroid is one of the Android programs in scope. No design
  inside the file detects it, this one included [model: every history with an overwrite of a
  newer copy]. While KeePass still holds the database open its next save meets the changed
  file and offers Synchronize [R keepass 9], which puts the union back. Section 6.4 names the
  one thing that would detect the rest, which is outside the file and needs a decision of the
  project.
- **Two deletions that leave no deletion record can put a copy back to what it was.** Where a
  copy received a newer state through a merge that carries no deletion records (KeePassXC,
  KeePass's three import methods other than Synchronize, KeePassDX) and nobody's gate has
  written to it since, it still holds the older elements. Remove there both the newest head and
  the newest decision without a trace, which a script over pykeepass does [M crit
  `store-entries-survival`] and KeePass, KeePassXC, Keepass2Android and KeePassDroid do not
  (T4), and what is left is the older state, whole and self-consistent [model]. That copy is a
  stale copy again. One deletion is not enough, and a deletion that leaves a record is itself
  read (5.2).
- **Deletion records cleared, then a stale copy merged**, after a loss that was detected and not
  yet sealed, brings the older state back [model]. Database Maintenance's "Remove deleted
  objects information" is the only command that clears them [R keepass 5].
- **A copy of a ruled node that loses its mark before KeePass next saves** is governed by its
  place alone [model].
- **The whole group deleted for good, and that deletion synchronized**, removes the store from
  every copy. The gate then serves nothing anywhere and the rules have to be entered again.
  Deletions travel [M keepass X4; M android 2.2]; nothing kept in entries outlives that.
- **The authority's private key sits in an entry**, as in `option-store-entries.md`, with every
  way of leaking it that its critic lists (S3). This formulation does not change that.
- **One deletion of the head, or one hand edit of any store entry, stops the gate for that
  database** until a press, in every copy the deletion or the edit reaches. Nothing is lost by
  it and nothing widens; it is the cost of the head being the witness that the store is whole.
- **It has more parts than any other option**: three types, the head, the witness, the mark and
  the pin, four conditions for a whole store, and four presses. Section 13 counts them.

## 2. The transport, as a model with its faults

The store rests on these properties and on nothing else. Each fault is something a program or a
person was measured or read to do.

| # | Property or fault | Basis |
| --- | --- | --- |
| T1 | **Union.** An entry only one copy holds arrives in the other | KeePass, five methods, both directions [M keepass 1d]; KeePassXC, four releases [M keepassxc 2]; Keepass2Android, both directions (`NC` and `NP` both present) [M android 2.2]; kdbxweb [M others]; KeePassDX 4.5.8 adds it and recreates a missing group, 3.2.0 adds it only if its group exists [R android 1.5]; Strongbox, KeePassKit, keepass-rs [R others] |
| T2 | **An entry changed in place is replaced whole by its modification time**, the loser into history or nowhere; a tie loses one side | KeePass, 120 cells [M keepass 1a]; KeePassXC [M keepassxc 4]; Keepass2Android (`M1`, `M2`) [M android 2.2]. Consequence: no element is ever changed |
| T3 | **A program can save an entry unchanged under a new time** | KeePassDroid's editor Save [M android 3.3]. Harmless here: the content is the same, and identity is by content |
| T4 | **A person can delete an entry in any program**: to the recycle bin (no deletion record, the modification time unchanged in KeePass, Keepass2Android and KeePassDroid), for good with a deletion record, or with none | bin: [M android 2.1, 3.2; R keepass 2.2]; record: [M android 2.1 step 5, 3.2 step 5; R keepass 5]; none: pykeepass [M crit `store-entries-survival`] |
| T5 | **A deletion record travels through KeePass's Synchronize and Keepass2Android's merge and is applied where the entry is not newer; where the entry is newer the record goes** | [M keepass X3, X4]; Keepass2Android: `X1` gone with the record kept, `X2` back with the record removed, both directions [M android 2.2] |
| T6 | **It does not travel through KeePass's other four methods or a KeePassXC merge**; a copy applies its own records to what arrives under every KeePass method; KeePassXC brings deleted entries back beside the target's records | [M keepass X3, X4; M keepassxc 2; M store-entries set D] |
| T7 | **KeePassDX applies the records that arrive and keeps none of them**; it keeps out an arriving entry older than a record of its own | [R android 3.1, 1.5]; not measured |
| T8 | **Deletion records can be cleared**, all at once | Database Maintenance [R keepass 5] |
| T9 | **A deletion record that names nothing is kept** by a save and by a merge | KeePass [M keepass X4]; KeePassXC, pykeepass, kdbxweb saves [M wildcard]; Keepass2Android and KeePassDroid in every operation run, and through the Keepass2Android merge (`D1`, `D2`) [M android 2.1, 2.2, 3.2]; KeePass never drops a record whose object is absent [R wildcard, `PwDatabase.cs:1067-1096`] |
| T10 | **A whole file can replace another** | Keepass2Android "overwrite", every KeePassDroid save over a changed file [R android 4.2, 5.5]; KeePassDX inside ten seconds [M spike]; file synchronizers [R others] |
| T11 | **An import can give every arriving entry a new identifier** | KeePass Create new IDs, preselected by File then Import [M keepass 2.4, X1] |
| T12 | **A copy of a user's node gets a new identifier and keeps its strings and its entries** | KeePass Duplicate and the clipboard [M rules-linked 5.1]; KeePassXC, Keepass2Android, KeePassDX [R rules-linked 2.2; R crit `rules-linked-survival` 2] |
| T13 | **Custom strings, their values and their protection flag are kept** | KeePassXC 2.4.2 to 2.8.0 [M keepassxc 6]; Keepass2Android and KeePassDroid, a 2861 character value among them [M android 2.1, 3.2]; KeePassDX [R android 2] |
| T14 | **History is trimmed, per-key times are dropped, node times are moved** | [M keepass 1b; M spike]. Not used: nothing here reads any of them |
| T15 | **A foreign file can be merged in** and adds whatever entries and deletion records it holds | by T1 and T5 |

Not relied on: where an entry sits (KeePassDX moves an unchanged entry to the arriving copy's
group at equal times [R android 5.4], and store elements are always at equal times), which group
is the recycle bin, any modification time, any history item, any custom data.

## 3. The store

### 3.1 The group

One group, made with the first authority. Its UUID is eight fixed bytes (`4B50472F53544F52`,
the ASCII of `KPG/STOR`) followed by eight random bytes drawn when it is made. It is found by
that prefix among the groups of the tree, wherever it is, the recycle bin included; several are
read as one. Name `KeePassGate`, searching off, auto-type off, one fixed line of notes; no
decision reads any of those.

- Two copies of one database hold the same group, because the group was made once [M
  store-entries set I for one identifier merging as one group].
- A group made again after a deletion has another identifier, so no deletion record ever names
  it. That is the change the critic of `store-entries` asked for after measuring kdbxweb drop a
  store remade under a fixed identifier (its P3) [M crit `store-entries-survival`].
- An element counts only as a direct child of such a group. That keeps the read to a few dozen
  entries (section 10) and it fails closed: an element anywhere else is ignored and shown.

### 3.2 An element

```text
Entry  KPG revoked client 9b1c2d3e
  User name, password, URL   present, empty
  Notes      KeePassGate data. Managed on the KPG tab in KeePass. Deleting an entry here revokes nothing.
  KPG.Kind   revoked
  KPG.Data   {"v":"2","certificate":"9b1c…","notAfter":"2027-03-01T10:00:00Z","authority":"3f2a…","nonce":"5be1…"}
  KPG.Mac    7d0e…                       (HMAC-SHA256 under the store key over kind, a line feed, data)
  UUID       3 bytes: the kind's tag | 5 bytes: a keyed hash of what it is about (here the certificate)
             | 2 bytes: its height | the first 6 bytes of KPG.Mac
```

The tag and the hash are HMACs under the store key, cut short, so they mean something to the gate
and nothing to anybody else. They are there for one reason: **a deletion record is a UUID and a
time and nothing more** [R keepass 5], and with this layout the deletion record of an element
still tells the gate what kind of element it was, what it was about, and how high (5.2, point
3). The MAC in `KPG.Mac` is what authenticates; the six bytes in the UUID only tie the entry to
it.

`KPG.Data` is one flat JSON object of strings, which is what `JsonBody` reads today [R plugin
`Acl\AclDocument.cs:28-58`]. Custom strings because they are the carrier every program keeps
(T13); the reasons against every other carrier are in `option-store-entries.md` section 3 and
stand.

### 3.3 The identity of an element, and what a replica is

A textbook CRDT gives every write a *dot*: the replica that wrote and a counter of its own. That
needs replica identities that are never shared, and here they cannot be had:

- **A file is a replica, and a file is copied.** A copy taken to a phone, a backup, a
  synchronizer's conflict copy: each is a fork of the replica with the same history. A counter
  kept in the file is forked with it, so two forks give two different acts the same dot. This
  is exactly the profile generation the critic of `store-entries` broke (its P2), and the
  answer number of its standing answers.
- **An installation is not a replica either.** One KeePass opens two copies of one database,
  and one copy is opened by two installations. An identity kept on the host would also be state
  outside the database, which CLAUDE.md's first decided bullet rules out.
- **Only gate installations write**, and what they write is an act of the operator. So the dot
  is the act: **16 fresh random bytes in the element's content**, and the identifier is a
  digest of all of the content. No counter, no replica name, nothing to fork.

Three consequences, each a finding of the earlier critiques:

- An identifier never comes back. A rule set, taken away, and set again to the same text is a
  new element that names what it follows, so it cannot be born dead under an old deletion
  record or be swallowed as a version already replaced (`rules-marker-entry` survival P1,
  security S1; `rules-on-node` survival 2.4) [model, scenario 6].
- The identifier authenticates the content: an entry edited by hand, or brought in under another
  identifier by an import, is not the element (T11).
- The digest is keyed (section 7), so nobody outside can compute an element's identifier from
  what it is about. A file built outside therefore cannot carry a deletion record that names an
  element, which is how a hostile file would otherwise delete the authority through
  Synchronize (T5).

No element's identifier is derived from its subject alone, the authority and the admission
record included: each holds a nonce. The gate finds a client's record by the certificate digest
inside it, from an index built when the store is read.

### 3.4 The causal context

Every element that can be replaced, or that replaces something, carries `after`: the identifiers
of the elements of the same object that the writer had read, and everything those name. The list
is closed, so that an old element coming back from a stale copy is known as replaced even when
the elements in between were pruned long ago. It is cut to the 64 nearest ancestors. Beyond
that, an ancestor that returns is not named, counts as concurrent, and meets the current value
to the narrower. That is the rule for everything the order of descent cannot place: **unknown
means concurrent, and concurrent means narrower**.

A head carries the same closed list of the heads it replaces, and a *height*: one more than the
highest height it has seen. Height decides nothing between two elements. It serves one purpose:
it bounds the head's list of ancestors to those within 64 heights, and lets a deletion record of a
head far below that be taken for an ancestor (section 5.2).

## 4. The types

### 4.1 The data and their types

| Datum | Type | Elements | Value | Join direction |
| --- | --- | --- | --- | --- |
| Authority | two-phase set | `authority` (today's document plus the store key, protected); refusal `retired` | the authorities in force that no retirement names. Exactly one is served; none serves nothing; several answer `503 undecided`, as today | a retirement only ever removes; a second authority refuses until the operator keeps one |
| Admission record | two-phase set | `client` (today's record text); refusal `revoked` | the certificate is admitted when its record is in force, its issuer is the served authority and no revocation names it. Profiles: the record's, less what the list lacks, as today | a revocation is final; a lost record admits nobody |
| Profile list | remove-wins set | `profile` (name, `after`: the removals of that name it saw); `removed` (name) | a name is on the list when some addition in force names every removal of that name the file knows | a removal wins over every addition that did not see it |
| Access rule of a node | register, meet | `rule` (node, `after`, the rule document as `AclDocument.Write` makes it); `pin` (node, the node it is a copy of) | the meet of the versions nothing names (4.3) | concurrent versions give what all give |
| Standing answer to renewals | register, meet | `renewal` (certificate, `after`, `forever`, `until` and a moment, or `allowed`) | the strictest of the versions nothing names: `forever`, then the later `until`, then `allowed`. None: the operator is asked, as today | a lift made without seeing a refusal loses to it |
| Recycle bin marker (T049) and other per-node meta data | register, meet | `bin` (group, `after`, `bin` or `repurposed`, since when) | `bin` when any version nothing names holds `bin` | a group once marked stays marked until an element that saw the mark lifts it |
| The head | register of snapshots | `head` (5.1) | section 5 | refusals by union |

An answer "in this session" has no element, as today. Invitations, held approvals and owed
answers stay in memory [R inventory 1.3]; an owed answer is a `renewal` element waiting for a
save.

### 4.2 Why these joins cannot be undone by a merge

- **Two-phase set.** A revocation is a fact about a certificate and a retirement about an
  authority, and neither is ever taken back: a revoked client pairs again and gets a new
  certificate. So the refusal needs no causal context, wins over the addition in every order,
  and the join of any two states holds every refusal either holds.
- **Remove-wins set.** `store-entries` identified an addition by name and generation. Two copies
  then gave one identifier to two acts, and a removal in one copy was undone by a re-addition in
  another that never saw it (critic P2). Here the re-addition names the removals it saw. A
  removal it did not see is not among them, so the name is off the list after the merge
  [model, scenario 4]. A name added in each copy on its own survives, since neither addition has
  any removal to see [model, scenario 5]. The price is the other side: an addition made at the
  same time as another copy's removal of that name is lost, and the Profiles page shows the
  name as removed.
- **Register.** A version that names all the others replaces them, whatever it holds: that is
  the operator changing a rule, and it may widen. A version that does not name another never
  saw it, and the two meet.

### 4.3 What "narrower" is for a rule document

A document is read per profile by `AclResolver.Decide` [R plugin `Acl\AclResolver.cs:466-535`]:
walking up from the node, the first document that names the profile answers with its words; a
document that does not name it and has `default` `deny` ends the walk with nothing; a document
that does not name it and inherits lets the walk go on. So, for one profile, a document is one of
two kinds of function of *what arrives from above*:

- **a grant** `G(S)`: the profile is named with the set of words `S` (out of `create`, `read`,
  `update`, `strip`, `delete`, `list`, `attachments`, `protected`); the answer is `S` whatever
  arrives. An empty array is `G(∅)`, and so is a `deny` that does not name the profile;
- **a limit** `L(S)`: the answer is what arrives, cut to `S`. A document that does not name the
  profile and inherits is `L(all)`, which changes nothing.

The meet of two documents is the function that gives, for every input, what both give:

| | `G(T)` | `L(T)` |
| --- | --- | --- |
| **`G(S)`** | `G(S ∩ T)` | `L(S ∩ T)` |
| **`L(S)`** | `L(S ∩ T)` | `L(S ∩ T)` |

This family is closed under the meet, the meet is commutative, associative and idempotent, and
the result is at most each argument for every profile, every word and every input. Words meet as
sets; a switch left without a verb grants nothing, which `AclGrant` already reads that way. Two
consequences that the earlier designs reached by other routes:

- *One copy narrowed a rule and the other took the rule away.* The meet of `G(S)` and `L(all)`
  is `L(S)`: the node gives what its group gives, and no more than `S`. `rules-linked` and
  `rules-marker-entry` gave nothing here ("a profile only some name keeps nothing"), which is
  safe and narrower than it has to be. `wildcard` section 5.3 has the same answer as this one.
- *A limit is not a new concept bolted on.* The "limit" `rules-linked` introduced for a
  reference in one direction is the second kind of element of this lattice. A copy of a node is
  capped by `L(S)` made from the original's rule, and the resolver's walk handles it as it
  handles any meet: a limit cuts what the walk finds further up.

For a whole document: `default` is `deny` when any standing version has it; each profile gets
the meet of what each version makes of it. The resolver learns one thing, to carry a cap up the
walk; `AclDocument`, nearest wins and the words do not change.

**A refusal at a node** is `G(∅)` for every profile: it stops the walk there, as a document
that will not read does today [R plugin `Acl\AclResolver.cs:484-491`].

## 5. The head, and the rule for reading

### 5.1 The head

With every write, the gate adds one `head` element and deletes the heads it replaces.

```text
Entry  KPG head 0007
  KPG.Kind    head
  KPG.Data    {"v":"2","height":"7","after":"c41f… 90aa…","nonce":"…"}
  KPG.S.0001  {"kind":"revoked","certificate":"9b1c…","notAfter":"2027-03-01T10:00:00Z","authority":"3f2a…"}
  KPG.S.0002  {"kind":"retired","authority":"11c0…"}
  KPG.S.0003  {"kind":"removed","profile":"reader","id":"e09b…"}
  KPG.S.0004  {"kind":"rule","id":"77d2…","node":"group/4f0c…","after":"a1b2…","rule":"{\"default\":\"deny\"}"}
  KPG.S.0005  {"kind":"renewal","id":"…","certificate":"9b1c…","after":"none","answer":"forever"}
  KPG.Mac     over kind, data and every KPG.S string in order
  UUID        as every element's: the head's tag, the hash of the store itself, the height, 6 bytes of MAC
```

It carries every decision whose loss would widen: every refusal, every removal, and every
register version nothing names, with its text. It carries no authority and no admission record,
because losing one of those admits nobody. One string per decision keeps each value inside
`JsonBody`'s ceilings.

In the terms of the field, the elements are deltas and the head is the state: each alone is
enough to compute the value, and they are joined by union when read. That is why one loss costs
nothing (section 6.1).

A deletion record that names a head therefore tells two things without the head: that it was a
head of this store, and how high. Nobody without the key can make such a record. `after` is
closed, as a version's is: it names every head this one replaces, directly or through another,
within 64 heights.

### 5.2 The rule for reading

Computed from the direct children of the store group, the deletion records, and the marks on the
nodes a request walks. In brackets, the name of the switch in the model.

1. **In place and authentic.** An entry is an element when its strings read strictly, its MAC
   verifies under the store key, and its identifier is the one its MAC gives. An entry in the
   group that carries a `KPG.` string and is not an element is damage, and damage is never
   passed over (point 5). An entry with no such string is a stray: ignored, shown on the tab.
2. **A deletion record takes an element out of force** [`recrule`]: an authority, a record, an
   addition, a version, a pin or a head that is present beside a deletion record naming it does
   not count. The gate's own pruning leaves such records, so an older element that a KeePassXC
   merge brings back stays out (T6). A refusal and a removal count whether or not a record names
   them.
3. **Refusals and removals are a union** of: the entries, in the group and in the recycle bin;
   what every head present carries, whether or not that head is in force; the witnesses of 5.3;
   and **every deletion record that names a refusal or a removal** by its tag, which refuses the
   certificate or authority whose hash it carries, or removes the name, exactly as the entry
   did [`snapshot`, `T`, `dtag`]. So deleting a revocation for good, in any program that
   leaves a deletion record, changes nothing.
4. **A register's versions** are the version entries in force and the versions the heads in force
   carry. The ones nothing names stand, and the value is their meet. **The object refuses** when
   a deletion record names a version of it that nothing known replaces and no head carries (a
   version was deleted and its text is nowhere), and when none stands and a version of it is
   present beside a deletion record. A record more than 64 heights below the standing versions
   is taken for one long replaced.
5. **The store is whole** when all of these hold; otherwise it is *incomplete* and serves
   nothing:
   - some head is in force;
   - every deletion record that names a head (by its tag) names one that is in force or is among
     the ancestors of a head in force [`headtag`]. The gate deletes a head only in the write
     that replaces it, so its own records always pass; a record that does not pass means a head
     was deleted that nothing present replaces. A record more than 64 heights below the highest
     head in force is taken for an ancestor;
   - nothing in the group is damage (point 1);
   - every refusal, removal and standing version that is present as an entry is carried, or
     named as replaced, by some head present [`cover`]. A decision without its head means its
     head was lost on the way.
6. **Nodes**: section 8.

The last three conditions of point 5 are not as first written: each took its present form from a
counterexample the model's search found (section 12.4).

### 5.3 The witness

For every revocation and every retirement the gate also writes a deletion record whose UUID is
the first 16 bytes of an HMAC under the store key over the kind and the certificate's or the
authority's digest. It names no entry. The gate asks for it by computing the same identifier
from the certificate presented.

- It survives every save measured, and KeePass's and Keepass2Android's merges carry it (T9, T5).
- No program offers a way to delete one deletion record. Clearing them all is one command in
  KeePass (T8).
- It does not travel through KeePassXC or KeePassDX merges (T6, T7). The gate's next write in a
  copy puts back the witness of every refusal in force there.
- It is final by nature, which fits a two-phase set and nothing else. A profile's removal has
  none, because a name can come back.

The witness is the idea of `option-wildcard.md` section 6, kept for the two refusals that are
final.

### 5.4 Seal again

The one press for an incomplete store, on the KPG tab's first page, applied at the settings
dialog's OK like everything on that tab. It writes a head over everything that verifies and is in
force, after showing what that is: the authority, the clients admitted, the names, the nodes
ruled. For every node that carries the mark of section 8 and has no rule left it writes a version
that grants nothing, so that an older version arriving later meets it and cannot take over
[model, scenario 10]. Nothing else in the design repairs anything, and
nothing is sealed without the press.

## 6. Loss

A textbook CRDT assumes an element once delivered stays. Here a person can delete one (T4), a
deletion record can carry that to other copies (T5), a hand edit turns an element into damage
that then wins every merge by its newer time (T2), and a whole file can replace another (T10).

### 6.1 One loss: healed, nothing refused

| What is lost, alone | What the gate does | Basis |
| --- | --- | --- |
| A refusal's entry (deleted, binned, moved out of the group) | still refused: the head carries it, and the witness; deleted for good, its own deletion record does too | [model, scenario 1] |
| A removal's entry | still removed: the head carries it | [model] |
| A rule version's or a standing answer's entry | the same value, from the head, with its text. The next write of the gate puts the entry back | [model, scenario 11] |
| One of two concurrent versions | the narrower of both still applies: each is in its own head, or in the one written after the merge | [model, scenario 12] |
| An admission record | the client is not admitted and pairs again. A merge with a copy that holds the record heals it unless a deletion record names it | by the type |
| An addition of a profile | the name is off the list until added again | by the type |
| An authority's entry | nothing is served; a merge with a copy that holds it heals it unless a deletion record names it | by the type |
| A node's mark | nothing changes for the node; a copy made afterwards is not recognized (section 8) | by the rule |

The model checks this as a property of its own, `one-loss`: no single deletion or hand edit of
one store entry may make a copy wider than it was before that step. It did not fail in the final
run. In earlier runs it failed where the step was the second loss of one decision, which is what
led to the rule on a decision's own deletion record (12.4) [model, section 12.3].

### 6.2 Detected, and refused

| What happened | Detected by | Scope of the refusal | Healed by |
| --- | --- | --- | --- |
| The head deleted, binned or moved out | no head in force; or decisions no head accounts for | the database: `incomplete`, nothing served | a merge that brings a head covering what is present, or Seal again |
| Any store entry edited by hand, the head included; the edit then wins every merge by its newer time (T2) | damage in the group | the database, in every copy the edit reaches | Seal again: the head still carries what a damaged decision held [model, scenario 19] |
| The head deleted for good, and another head still present (an older one in a copy that received by KeePassXC, or another copy's concurrent one) | the deletion record names a head that no head in force replaces | the database | Seal again |
| The newest head and newest decisions deleted in a copy the gate has written to, then a stale copy merged in by KeePassXC | every older head and version is beside the deletion record the gate's pruning left | the database | Seal again; a Synchronize with a healthy copy |
| The same, by KeePass's Synchronize or Keepass2Android | the older elements do not arrive at all: the copy's own records keep them out (T5, T6) | the database | the same |
| A decision arrives without its head (its head was lost in the copy that wrote it) | a decision no head accounts for | the database | Seal again |
| A copy merges, by Synchronize or Keepass2Android, with a copy whose newest head and decisions were deleted | the damaged copy's records delete the older heads in the healthy copy's stale partner too, leaving it with no head | the database, in both copies | Seal again in the copy that still knows the decisions, then merge |
| Every version of a node's rule lost in every carrier, the node still marked | the mark with no rule | the node and everything below it | Seal again writes the refusal down; the operator sets the rule |
| A register version present beside a deletion record, nothing standing | point 4 of 5.2 | the node, or the certificate's renewals | the operator sets the rule |
| A second authority under the same store key | as today | the database, `503 undecided` | the operator keeps one |
| Elements under another store key (section 7) | they do not verify | none: they are ignored and shown | the tab's removal of them |

A refusal of database scope is steep: one deletion of the entry titled `KPG head` in any
program stops the gate for that database until a press. It is the price of the head being the
witness that the store is whole. It fails closed, it spreads only where deletions travel, and
one press in one copy heals every copy at the next merge.

### 6.3 Healed by a merge

Whatever one copy lost and another still holds comes back with the union (T1), unless a deletion
record in the receiving copy names it. KeePassXC never applies one, so a KeePassXC merge with a
healthy copy heals everything [M crit `rules-linked-survival` C5 for that mechanism]. KeePass and
Keepass2Android heal what was removed without a record and what is newer than its record.

### 6.4 Not detected

Plainly, with the shortest history the model found for each:

1. **A whole file written over a newer one.** `revoke` in one copy, then an older copy written
   over it. The older file is consistent with itself. Detecting this takes something that is not
   in the file. The smallest such thing: the gate remembers, per database (by the authority's
   digest), the highest head it has written or served, in KeePass's own configuration where
   `KPG.Port` already lives [R inventory 1.3], and a file whose heads are all lower is
   incomplete until a press. It could only ever refuse. It is still state on the host about a
   database, which the first decided bullet of CLAUDE.md rules out for an authority and a
   registry, so it is not part of this design. It is a question for the project.
2. **A decision lost in both carriers, in the only copy that held it, before any merge**, when
   the merge that follows carries no deletion records (KeePassXC, KeePassDX, KeePass's import
   methods). The other copies were never told: `prem 0 pf; del 0 (the removal); del 0 (head);
   merge xc 1 0`. The copy where it happened is incomplete, and its tab shows it. Through Synchronize or
   Keepass2Android the head's deletion record travels and the receiving copy is incomplete too.
3. **The second loss of one decision, without a trace, where the first was not seen.** Three
   shapes:
   - the head and the decision's entry both removed, leaving no deletion record, in a copy that
     still holds the older state because it took the newer one from a KeePassXC, KeePassDX or
     import merge and no gate has written to it since [model, scenario 3];
   - across two files: the head deleted where the decision was made, the lone decision
     delivered by a merge without records, and then removed there without a record. Between the
     two, the receiving file is incomplete; the second removal takes away what made it so;
   - the same two deletions made with records in another copy and applied by a KeePassDX merge,
     which keeps no record (T7).
4. **Deletion records cleared while a loss stands unsealed, then a stale copy merged in**
   [model, scenario 9].
5. **Seal again pressed after a decision was lost in both carriers without a trace.** The press
   puts in force what is left: `renew 0 c0 forever; del 0 (head) leaving no record; del 0 (the
   answer) leaving no record; reseal 0`, after which the renewal is asked again [model]. With
   deletion records the same history holds, because the press writes the refusal down. A
   revocation and a retirement are kept by their witnesses, a rule by its node's mark, and a
   removed name by the deletion record of its addition; a standing answer has no third witness.
6. **A copy of a ruled node stripped of its mark before the next save** (section 8).

In every case but the first and the fifth, the copy in question has been returned to an earlier
state of itself and everything in it agrees. That is the general shape of what cannot be seen
from inside: **a file that is exactly what it once was**.

A hardening for item 3 that is not in the design: when the gate, with a database open, sees it
incomplete, it could write that down inside the operator's next save, as a keyed deletion record,
so that removing the evidence no longer ends the refusal. It is one more write the gate would
make without a press, and it helps only where a gate looks between the two losses.

## 7. Authenticity

A CRDT accepts every addition. A foreign `.kdbx` that the operator merges adds entries (T15), so
without more a file built outside could add a rule for any node, a retirement of the authority,
or a record for a client. Three critiques found that in three designs (`store-entries` security
S1, `rules-central` security S1, `rules-linked` security S5), and each named the same answer.

**Decision: every element carries an HMAC-SHA256 under a store key, and its identifier is taken
from that MAC.**

- **The key** is 32 random bytes made when the first authority is made. It lives inside the
  authority's document, as one more member beside `sniKey`, in the same protected string. A
  recreated authority's document carries the same key forward. No new entry, no new place for a
  secret, nothing outside the database (requirements E9, E10 [R inventory 5.1]).
- **Why a MAC and not a signature by the authority's private key.** Both are held only while
  the database is open. A signature would tie every rule and every profile element to one
  authority, so Recreate would have to write them all again; it costs an ECDSA verification per
  element where an HMAC costs microseconds; and it buys nothing, because nobody verifies an
  element but the gate that holds the key.
- **Why not `sniKey` itself.** It changes with the authority, for a reason of its own.

**What it does to "a malicious `.kdbx` vouches only for itself".** It makes the sentence true of
every datum, which today it is not for rules. A file that has not read this database:

- cannot make an element that verifies, so it can add no rule, no record, no profile, no
  retirement and no revocation [model, scenario 13];
- cannot compute an element's identifier, so it cannot bring a deletion record that names one;
- cannot make a witness or a head's tag, so it cannot make the store incomplete;
- can still bring an authority of its own, whole, with its own key. Two authorities then stand
  unretired and the gate answers `503 undecided`, as today, until the operator keeps one
  [model, scenario 14]. Keeping one deletes the other's entries with deletion records; a
  retirement of an authority counts only under that authority's own store key, so the foreign
  file cannot retire this database's authority, and after the choice nothing under the other
  key is read.

What it does not do: anybody who can open the database holds the key and can write any element,
as anybody who can write the file can write a whole admission record today [R inventory P23]. The
MAC is against a file from outside, not against the master key's holder.

**Reversal to record.** `CA.md` records that nothing in a record is authenticated beyond the file. Here
every element is. And the authority's document gains a member.

**If the authority's entry is lost**, the key is lost with it in that copy and nothing verifies:
no store, nothing served, which is where a lost authority leads anyway. A merge that brings the
authority back brings the key back.

## 8. The tree

A rule names a node. A copy of a node has a new identifier (T12). `option-rules-linked.md` keeps
a random anchor on the node and requires both references to agree before a rule gives, on the
argument that "a reference in one direction only can take away and can never give". Its critics
then measured what that costs: a first grant that waits for a Confirm press whenever another
copy edited the entry later, a Confirm whose only evidence a merged file can forge, a group's
grant suspended when KeePass's Delete Duplicate Entries removes the group's link, and a copy that
is open again once its one carried reference is gone (security S1, S2; survival P1, P5).

**This design keeps the argument and changes which reference it applies to.** With every element
authenticated, the two references are no longer alike:

- **The rule's reference to the node is a statement by this database's gate**, MAC'd, naming the
  node's own identifier, which is unique in the tree. Nobody outside can make one. It needs no
  confirmation from the node, so **it gives**.
- **The node's own mark is an ordinary string anybody can copy, strip or type**, so **it only
  takes away**. That is the same principle, applied to the reference that is in fact
  untrusted.

**The mark.** An entry carries a custom string `KPG.Node` holding its own identifier, written
once with its first rule, through the entry dialog's own `EntryStrings` so that the dialog's OK
and Cancel cover it [R rules-linked 4.1]. A group carries a link entry directly inside it, with
`KPG.Kind` `link` and `KPG.Node` holding the group's identifier; the link's own UUID is derived
under the store key from that identifier. No anchor: the node's identifier is the anchor.

| The node | What the gate answers | Why it is never wider than the operator meant |
| --- | --- | --- |
| has a rule in the store | the rule, nearest wins, as today, whatever became of the mark | the rule is the operator's, and names this node |
| carries its own mark and no rule is known | refuses, there and below | a rule was set here and is gone |
| carries the mark of another node: it is a copy | the original's rule as a limit (4.3), until the operator gives the copy a rule of its own | a copy of a sealed node stays sealed; a copy of an opened node gets no more than its place gives |
| is a group holding a link whose UUID is the one derived for another group: the link was moved | ignored there, shown | a link is not a rule |
| is a group that has a rule of its own and holds a copied link | the group's own rule; the copied link ignored, shown | pasting the entries of a sealed group into an open group does what it does today |
| is a group with no rule holding a copied link | the original's rule as a limit | it may be a copy of that group |
| has neither | inherits, as today | nothing was decided here |

**The pin.** A copy is protected by the one mark it carries, and one deletion or KeePass's Delete
Duplicate Entries removes it (`rules-linked` survival P1, measured there). So when the gate sees
a copy in a database KeePass holds, it adds a `pin` element inside the operator's next save of
that database, as owed answers ride a save today [R inventory P18]: a version in the copy's own
register that reads "limited by the rule of that node". After that the copy's limit no longer
depends on its mark [model, scenario 15]. A pin only takes away. It is replaced by the first rule
the operator gives the copy (Adopt), which names it in `after`. This is the one thing the gate
writes without a press, and it needs a decision of the project since it reverses "writes nothing
unasked"; without it, section 6.4 item 6 is wider.

What the mark buys beside copies: it is the witness outside the plugin's group that a node had a
rule (5.4, 6.2). What it costs: a field on every ruled entry and an entry in every ruled group,
visible in every program, as `rules-linked` already counts.

Not solved here: a copy made in a program that drops custom strings (none is known: T13); a copy
of a group whose link was deleted before the copy was made; a copy stripped before any save. A
copy follows its original's later decisions while it has no rule of its own (`rules-linked`
survival P9); `docs/access-control.md` would have to state that.

## 9. Growth and pruning

Nobody can know that every copy has seen an element: a copy can be offline for a year. So
nothing is removed on the belief that it is no longer needed elsewhere. What is removed is what
is *replaced*, and the replacement keeps the removed thing dead wherever it returns.

| What | When the gate deletes it (with a deletion record, inside a write of its own) | What a stale copy that brings it back meets |
| --- | --- | --- |
| A version another version names | at the write that replaces it | named in `after`, or beside the record: dead. Beyond 64 ancestors and with no record: concurrent, so the narrower applies and the tab asks to settle |
| A head another head replaces | at every write | its refusals are still read (they are true), its versions are judged by `after` like any |
| An addition that some removal defeats | at the removal | defeated by the removal, which stays |
| A removal that an addition in force names | once that addition exists | nothing: the name is on the list anyway |
| A record beside its revocation, or under a retired authority | at the revocation or retirement | refused by the revocation, the witness, the retirement |
| A revocation, its witness, the standing answers of its certificate | once the certificate has expired: the natural bound | the certificate is refused on its dates [R inventory D4] |
| A retired authority's document | at the retirement, to take the private key out of the file | refused by the retirement |
| A retirement, the last removal of a name, a `bin` marker | never | |
| The versions of a node that is gone | never by itself; on a press on the Profiles page's count | the node may come back from another copy with its rule, which is right |

So the store holds, in the ordinary state: one authority, one head, one record per client, one
entry per profile name, one per ruled node, one per pinned copy, the refusals whose certificates
have not expired, and one retirement per authority ever retired. The deletion records grow by one
per replaced element, since KeePass never ages them [R keepass 5]; at a pairing a week that is
about a hundred a year. A head for twenty ruled nodes and a few refusals is 3 to 5 KB [A, from
the sizes in `ground-inventory.md` 1.4], written again at each write of the gate, never by a
request that only reads.

The head is the checkpoint the field speaks of: it names what it replaces and carries the state
that matters. The elements are kept beside it because the head alone has the cliff
`option-wildcard.md` has: lose the one entry and everything is gone.

## 10. The write path and the read path

**Reading.** On every request, inside the hold a request already runs in [R inventory P25]:

1. find the store group by the prefix of its UUID (a walk over groups, not entries);
2. take its direct entries, the recycle bin's entries (for refusals and removals only), and
   the deletion records;
3. look up each entry's parsed and verified form in a memo keyed by the entry's identifier and
   the string objects it holds; KeePass returns the same object for an unchanged string
   [M store-entries 4];
4. fold. The fold of an unchanged set is itself memoized on the set of identifiers and the
   deletion records' count and digest.

Nothing that grants is cached: the memo is of a pure function of what the file holds at this
request, checked against the file at this request, so a revocation still bites at the next
request (requirement E14). Cost [M store-entries 4, on 10,054 entries, 501 groups, 5,000
deletion records, 53 store entries, in a console]: 0.26 ms for the tree by identifier, 0.08 ms
for the deletion records, 0.02 ms for the store group's entries. This design needs the groups
only for step 1, so it should be below that; the HMAC of a few dozen short strings is paid once
per change. Not measured for this shape. No request walks every entry of the tree, which the
critics of four designs counted against them (M1, M2, M4).

**Writing.** One writer, `DatabaseWriter`, in its turn on KeePass's thread, with the idle test,
the save through `MainForm.SaveDatabase(db, null)` and the verification by `Modified` and the
file's hash, all as today [R inventory P13]. Every write is: add elements, delete what they
replace, write witnesses, add the head, save. A write that did not land is taken back out by
removing what it added, with no deletion record. No credential before its record and its head
are in the file (requirement E7).

- **Rule writes go through the writer.** Two critics measured a rule written outside it landing
  inside a save, with the file then disagreeing with memory (`rules-marker-entry` security S2,
  `rules-linked` security S4). Here a rule is an element like any other.
- **At load and after a merge: nothing.** The join is the read. Requirement O1 holds as it does
  today.
- **Inside the operator's own save** (`FileSavingPre`): owed answers, as today, and pins
  (section 8).

**The settings tab.** `PendingDatabase` buffers elements to add instead of keys. OK applies them
in one turn; Cancel drops them and withdraws the invitations made on the dialog, as ruled. The
reconcilers of today become properties of the types:

- The profile list needs none: the buffer already is the difference, and a removal defeats an
  addition a pairing wrote underneath the dialog, which is what CLAUDE.md describes today.
- **Allow renewal** is a `renewal` element whose `after` is what the page showed when the button
  was pressed. An answer the renewal dialog wrote underneath is not in it, so the two are
  concurrent and the refusal stands: "keeps whatever changed under the dialog" without a
  reconciler.
- A revocation is an addition and cannot fail in the buffer, as today.

**The grant editor.** It keeps its change in a buffer of its own and applies it when the group or
entry dialog closes with OK, through the writer; Cancel drops it. A first rule on an entry puts
`KPG.Node` into the dialog's `EntryStrings`, so KeePass's own question on Escape covers it. A
rule that is not changed writes nothing. The dialog's copy of custom data, `m_sdCustomData`, and
its reflection go. The tab gains: "two rules, changed in two copies; the narrower applies", with
**Settle**; "a copy of (path); the original's rule limits it", with **Adopt**; "refusing: the
rule is gone", which the next rule the operator sets ends. No OK settles anything the operator
did not choose on the tab, which three critics asked for after finding an OK pressed for a
password sealing whatever the node carried.

**The API.** The store group and everything in it is absent on every route, as the recycle bin
is; strings whose names begin `KPG.` are hidden and refused on every field route; a group holding
only its link counts as empty; an entry's tag leaves `KPG.Node` out. This is the guard
`option-store-entries.md` section 9 and `option-rules-linked.md` section 9 describe, and it is
new code on every route. An incomplete store answers like `503 undecided`, under a word of its
own, which is one addition to the routing document and the client.

**KDBX version.** 4.0 [M store-entries 10 for the same features].

## 11. The serious findings of the critiques, one by one

"Resolved" means by this formulation. Scenario numbers are the model's replays (section 12.3).

| Finding | Resolved | How, or why not |
| --- | --- | --- |
| **store-entries, survival P1**: a refusal has one witness in many copies; one deletion admits a revoked client | yes for one deletion; partly beyond | three carriers: entry, head, witness [model, scenarios 1, 2]. Two deletions without a record in a copy that still holds the older state leave a stale copy (6.4 item 3) [model, scenario 3]. Its sequence C, the group deleted and a KeePassDX merge, is 6.4 item 3 too |
| **store-entries, survival P2**: a profile's identifier names a generation, not an act | yes | an addition names the removals it saw [model, scenarios 4, 5; with generations switched back in, scenario 4 fails] |
| **store-entries, survival P3**: the group's fixed identifier is reused after a deletion; kdbxweb drops the remade store | yes, by construction | eight random bytes in the group's identifier; no deletion record is ever removed to make room. kdbxweb is not in the model |
| **store-entries, security S1**: a planted refusal is believed anywhere, and the prune makes it permanent | yes | a refusal counts only if it verifies; the prune follows authenticated elements only [model, scenario 13] |
| **store-entries, security S2**: a record in the recycle bin returns on its own through a KeePassDX merge | no | a deletion in another program is a loss, not a decision. A binned record does not count while it is out of the group and counts again when a merge puts it back. Only the KPG tab revokes; the notes of every element state it. Not modeled |
| **store-entries, security S3**: the authority's private key is one copy or export away | no | unchanged. Keeping the authority in custom data would fail the survival requirement under a KeePassXC merge [M spike] |
| **store-entries, security S4**: refusals are lasting clutter, and tidying them undoes a revocation | partly | the tidy-up of one entry, or of all refusal entries, undoes nothing: the head and the witnesses hold them. The clutter stays, pruned at the certificate's expiry |
| **rules-on-node, survival 2.1**: one seal lost fails open | yes for one loss | the head carries the version (6.1) |
| **rules-on-node, survival 2.2**: the prune brings a deleted ruled group back unruled | yes, by construction | a node's versions are not pruned because the node is gone. Not modeled |
| **rules-on-node, survival 2.3, security P3; rules-central, survival P2, security S2**: any OK seals or repairs whatever is there | yes | nothing is settled by a dialog's OK; a lost version is served from the head, and Seal again starts a ruled node with no rule from nothing |
| **rules-on-node, survival 2.4; rules-marker-entry, survival P1, security S1**: an identifier derived from content comes back | yes | the nonce [model, scenario 6] |
| **rules-on-node, survival 2.5**: where the seals are found is left open | yes | one group by prefix; elements authenticated; hidden from the API |
| **rules-on-node, security P1**: a recycled head seal after a rollback enforces the older rule | yes for that sequence | a binned version is served from the head; an older version beside the gate's deletion record is out of force |
| **rules-on-node, security P2**: the design's own prune turns a refusal into a widening under KeePassXC | yes | the gate prunes only what is replaced, and what returns beside its record is out of force [model, scenarios 7, 8] |
| **rules-on-node, security P4**: a rule entry out of place refuses where it lands, at the root everything | yes | a version names its node and counts only in the store group; a moved link is told from a copied one by its derived identifier |
| **rules-on-node, security P6; rules-central, security S5**: 0.1.0 rules are dropped silently and everything inherits | not by this formulation | section 15 |
| **rules-marker-entry, survival P2**: two concurrent rule entries; deleting one widens | yes | each is carried by a head [model, scenario 12] |
| **rules-marker-entry, survival P3**: an import with new identifiers, then the tab's Remove, opens the duplicates | yes | imported elements do not verify under their new identifiers [model, scenario 20]; copied nodes carry the mark and are limited; there is no Remove that widens, only Adopt |
| **rules-marker-entry, security S2; rules-linked, security S4**: the rule write runs outside the writer and can land inside a save | yes, by the write path | section 10. Not modeled |
| **rules-marker-entry, security S3**: an entry's rule stays in the group the entry left | yes | rules are not in the user's groups |
| **rules-central, survival P1**: a lost newest version leaves no trace where KeePassDX wrote, and the plugin deletes too | partly | the gate deletes only what is replaced; the newest version has two carriers. Both deleted elsewhere with records and a KeePassDX merge applying them is 6.4 item 3 |
| **rules-central, security S1; rules-linked, security S5**: a file the operator merges can rule any node, a group from both sides | yes | the MAC [model, scenario 13] |
| **rules-central, security S3**: the store shows what look like duplicates, and deleting them reverts silently | yes | replaced versions are deleted at the write, so one entry per ruled node; one deletion reverts nothing |
| **rules-central, security S4**: a copy carries no rule | yes, with the limits of section 8 | the mark, the limit, the pin [model, scenarios 15, 16] |
| **rules-linked, survival P1**: a copy is one reference deep | partly | the pin, from the next save on [model, scenario 15]; before it, 6.4 item 6 [model, scenario 16] |
| **rules-linked, survival P2**: newest version and record lost, then any older copy; the trace does not help | partly | with deletion records, with none in a copy the gate wrote, and for a whole tidy-up: incomplete [model, scenarios 7, 8, 17]. Records cleared and then a stale copy: not detected, unless sealed first [model, scenarios 9, 10] |
| **rules-linked, survival P3**: one tidy-up of the rules group and one Synchronize leave no rule text anywhere | partly | there is no rules group to lose alone, and a version's text is in two entries. The whole store deleted for good and synchronized is total, and fails closed |
| **rules-linked, survival P4**, and every design's last line: a whole file written over another | no | 6.4 item 1 |
| **rules-linked, security S1**: Delete Duplicate Entries deletes the governed group's own link and suspends its grant | yes | the grant does not depend on the link |
| **rules-linked, security S2**: the evidence the tab offers for Confirm is what a merged file can forge | yes | there is no Confirm; history decides nothing and is not shown as evidence |
| **rules-linked, security S3**: pasting a sealed group's entries closes the destination | yes where the destination has a rule; partly otherwise | section 8, rows 5 and 6. Not modeled |

## 12. The executable model

`crdt-model\crdt_model.py`, about 1,000 lines of Python with no dependency. One line runs it:

```text
uv run --no-project python -I -X utf8 spikes\storage\model\crdt_model.py full
```

(`quick` in place of `full` for a tenth of the histories; `scenarios` for the replays alone.) The
output of the run reported here is `crdt-model\results-full.txt`.

### 12.1 What is in it

- **A file** is a set of entries (identifier, the element it holds, a modification time, whether
  its content is intact), a set of deletion records with their times, and a small user tree: a
  root with a rule, a node `N`, and copies of `N`.
- **The read rule** of section 5.2 and the node rule of section 8, as one function, with a
  switch for each part so that a part can be turned off.
- **The gate's acts**: pair a client, revoke one, recreate the authority, add and remove a
  profile name, set a rule on the root or on `N` (four documents: read and write, read, nothing,
  inherit), answer renewals, save (which pins copies), adopt a copy, seal again. Each act reads
  the file through the read rule, writes its elements, prunes, writes the witnesses and the
  head.
- **What other programs and people do**, each as the section 2 row it rests on:

| Step in the model | Behavior | Basis |
| --- | --- | --- |
| `merge kp_sync` | union; a changed entry by its time; both copies' deletion records joined and applied where the entry is not newer; a record whose entry is newer dropped | measured: T1, T2, T5 |
| `merge k2a` | the same | measured: T1, T5 [M android 2.2] |
| `merge kp_other` | union; no arriving record; the copy's own records applied | measured: T6 [M keepass X3, X4] |
| `merge xc` | union; no record applied or copied; deleted entries come back beside the target's records | measured: T6 |
| `merge dx` | union, except an arriving entry older than a record of the open copy; arriving records applied and not kept | **read**: T7 |
| `del … 1`, `del … 0` | one store entry deleted with a deletion record, or with none | measured: T4 |
| `del … 2` | deleted with a record dated before the entry (a clock that is behind) | **assumed** as a fault; what KeePass does with such a record is measured [M keepass X3] |
| `edit` | a store entry's content changed; it no longer verifies and carries a newer time | measured for the merge that follows: T2 |
| `clear` | every deletion record removed | **read**: T8 |
| `over` | one file written over another | **read**, and measured for KeePassDX [M spike]: T10 |
| `import` | every entry of the other file arrives under a new identifier | measured: T11 |
| `plant` | a foreign rule, retirement, record, profile or authority arrives intact | by T1; that a foreign file cannot compute a MAC is **assumed** of HMAC-SHA256 |
| `copy`, `strip` | a copy of `N` with its mark; the copy's mark removed | measured for KeePass: T12; the strip is T4 or Delete Duplicate Entries [M crit `rules-linked-survival` C1b] |

- **Not in the model**: where an entry sits and the recycle bin; kdbxweb; KeePassDX 3.2.0
  dropping an arrival whose group is missing; several links in one group; the 64-ancestor cut;
  the 32-name ceiling; the MAC itself (an element has a key label). Statements about those are
  by reading or by construction, and are marked so.

### 12.2 The properties

For every file after every step, against **the same file had it kept everything it was ever
given** (the ideal: the read rule over every element that was delivered to that file by a write
or a merge, with no deletion and no deletion record):

- **safety**: every client admitted is one the ideal holds valid (its record known, not revoked,
  its issuer not retired) with no profile the ideal lacks; every node gives no word the ideal
  does not; no renewal is asked where the ideal answers `forever`. A revocation the file was
  ever given is never undone, as a case of this.
- **rollback by overwrite**: the same against everything ever delivered to that file's place,
  which an overwrite resets. Counted apart, since it is the stated limit.
- **sealed after a loss**: a safety failure in a history where Seal again was pressed before it.
  Counted apart: the press accepts what is left (6.4 item 5).
- **one-loss**: no single deletion or edit of a store entry makes the file's content wider than
  before that step.
- **copy-cap**: a copy of a ruled node, with no rule of its own, gets no word its original's
  rule as a limit would not give.
- **convergence**: at the end of every history the files exchange everything by Synchronize,
  twice around; all must then agree; and where the history had merges only, they must equal the
  ideal.
- **the laws of the join**, on the ideal alone: the value of a register is at most every
  version nothing names; every refusal either side holds is in the join; nobody revoked is
  admitted.

### 12.3 What it tried, and what it found

All numbers are from one run of `full`, 596 seconds, recorded in `crdt-model\results-full.txt`.

**Random histories**, 3 to 9 steps each, drawn from the gate's acts (45%), merges (30%) and the
faults of the configuration (25%), 20,000 histories for each configuration and each number of
files: 480,000 in all. Every step is followed by the checks of 12.2 on every file, and every
history that passed them ends with the convergence check. A failure is counted once per history
and the shortest one is reduced by dropping steps while it still fails.

| Configuration: the merges and faults allowed | 2 files | 3 files |
| --- | --- | --- |
| A. merges only, by every program modeled | no failure | no failure |
| B. A, and deletions by hand that leave a deletion record, and Seal again | no failure | no failure |
| C. B, and deletions that leave no record | safety 2 | safety 1 |
| D. B, and deletion records dated before the entry | safety 2 | no failure |
| E. C, and deletion records cleared | safety 1 | no failure |
| F. B, and hand edits, imports with new identifiers, planted foreign entries | no failure | no failure |
| G. B, and a copy stripped of its mark | copy-cap 2 | no failure |
| H. B, and a whole file written over another | rollback by overwrite 251, safety 1 | rollback by overwrite 404, safety 2 |
| I. everything at once | rollback by overwrite 41, safety 1 | rollback by overwrite 105 |
| J. only KeePass Synchronize and Keepass2Android; deletions with a record | no failure | no failure |
| J2. the same merges; every kind of deletion | no failure | no failure |
| K. J2, and hand edits, imports, planted entries | no failure | no failure |

No history failed `one-loss`, `convergence` or `sealed after a loss` in this run. The shortest
`safety` history of each configuration, all of two shapes:

- **6.4 item 2, never told** (C, D, E, H): for example
  `prem 0 pf; del 0 (head) 1; del 0 (the removal) 1; merge kp_other 1 0` (file 1 keeps the name),
  `rule 0 root none; del 0 (the version) 1; renew 1 c0 forever; del 0 (head) 1; merge dx 1 0`
  (file 1 keeps the root's older rule), and with three files
  `renew 0 c0 forever; del 0 (the answer) 1; del 0 (head) 1; merge kp_other 2 0`. In each, both
  carriers of a decision were deleted in the file that made it, and the merge that followed
  carries no deletion record. The oracle counts the decision as given to the other file because
  a merge took place; nothing of it was in what the merge carried.
- **6.4 item 3, second shape** (I):
  `rule 0 root inherit; padd 1 pg; del 0 (head) with a record dated before it; merge dx 1 0;
  del 1 (the version) leaving no record`.
- `copy-cap` (G): `rule 1 N none; copy 1; strip 1`, which is 6.4 item 6.
- rollback by overwrite: `revoke 2 c0; over 2 1`, which is 6.4 item 1.

I read the shortest history of each configuration; the other failing histories were counted and
not read one by one.

**Exhaustive**, every sequence of merges and faults up to a depth, after a fixed divergence:
file 0 has given `N` a rule that the stale file 1 still holds, then narrowed `N` and the root to
nothing, revoked `c0`, removed profile `pf` and refused `c0`'s renewals forever.

| Steps allowed | Width | Depth | Histories | Failures |
| --- | --- | --- | --- | --- |
| merges, five programs, both directions | 10 | 4 | 11,110 | none |
| merges, deletion of each of 12 kinds of store entry with a record in either file, Seal again | 36 | 3 | 47,988 | none |
| merges, the same deletions leaving no record | 34 | 3 | 40,494 | none |
| Synchronize and Keepass2Android only, deletions with a record, Seal again | 30 | 3 | 27,930 | none |

So from that divergence no three steps of other programs and people, in any order, made either
file grant more than it should. The random search reaches the two shapes above because there the
deletions come first, in the file that made the decision.

**The laws of the join**: 10,000 random pairs of files on the ideal transport, no violation.

**The sequences of the critiques, replayed**, and each part of the read rule switched off in
turn. "holds" is the outcome the critique asked for; "(incomplete)" means it holds because the
store refuses as a whole.

| # | Sequence | Finding replayed | As written | Fails when this part is off |
| --- | --- | --- | --- | --- |
| 1 | a revocation; a KeePassXC merge into the stale copy; the revocation's entry deleted there | store-entries survival P1 A | holds | the head's carrying of decisions |
| 2 | the same, and the head deleted there too, both with records | store-entries survival P1 B | holds (incomplete) | none of the switches alone |
| 3 | the same two deletions leaving no record | store-entries survival P1, what is left | **fails**: 6.4 item 3 | |
| 4 | a name removed in one copy; removed and added again in the other; merged | store-entries survival P2 | holds | additions seeing removals (with generations in its place) |
| 5 | a name added in each copy on its own | store-entries survival P2, other side | holds | |
| 6 | a rule set, taken away, and the same rule set again in a copy that saw neither | rules-on-node survival 2.4; rules-marker-entry survival P1 | holds | |
| 7 | the newest version and the head deleted, with records, in the copy that wrote them; then a stale copy by KeePassXC | rules-linked survival P2 C3 | holds (incomplete) | |
| 8 | the same, leaving no record | the same, no trace | holds (incomplete) | a deletion record taking an element out of force |
| 9 | the same, then deletion records cleared, then Synchronize with the stale copy | rules-linked survival P2 C3c | **fails**: 6.4 item 4 | |
| 10 | the same, but sealed again before the stale copy arrives | | holds | |
| 11 | one rule version deleted by hand, the deletion synchronized | rules-linked survival P3 C5 | holds, nothing refused | |
| 12 | two concurrent versions; both entries deleted by hand | rules-marker-entry survival P2 | holds | |
| 13 | a foreign file plants a rule, a retirement, a record and a profile | rules-central security S1; store-entries security S1; rules-linked security S5 | holds | |
| 14 | a foreign authority arrives intact | CLAUDE.md, `503 undecided` | holds: nothing served | |
| 15 | a copy of a sealed node; a save; the copy loses its mark | rules-linked survival P1 | holds | |
| 16 | the same with no save in between | the same, what is left | **fails**: 6.4 item 6 | |
| 17 | everything of the plugin deleted without a trace in the copy that wrote it; then a stale copy by KeePassXC | rules-linked survival P2 C3b | holds (incomplete) | |
| 18 | both carriers of a revocation deleted without a trace; Seal again; merged into a stale copy by KeePassXC | what the witness is for | holds | the head's carrying of decisions; the witness |
| 19 | a store entry edited by hand, the edit synchronized everywhere | store-entries survival P7 | holds (incomplete) | |
| 20 | an import with new identifiers of a stale copy | rules-linked survival P3 C6 | holds | |

**Each part switched off, random histories** (20,000, two files; the same histories with the
design as written beside it):

| Part switched off | Faults | Result | As written, same histories |
| --- | --- | --- | --- |
| the head carries no decisions | deletions with a record | safety 20; shortest `revoke 0 c0; del 0 (the revocation) 1; merge xc 1 0` | safety 2 (item 2) |
| no witness for refusals | every deletion | no failure in the search; scenario 18 fails | no failure |
| a deletion record does not take an element out of force | every deletion | no failure in the search; scenario 8 fails | no failure |
| a deleted head is not recognized | deletions with a record | safety 2 | safety 2 (item 2) |
| a decision without its head is accepted | every deletion | no failure in the search | no failure |
| the deletion record of a decision tells nothing | deletions with a record | one-loss 1, safety 3; shortest `rule 0 root inherit; del 0 (head) 1; merge kp_other 1 0; del 1 (the version) 1` | safety 2 (item 2) |
| profile additions by generation | merges only | convergence to the ideal 2; shortest `prem 0 pf; prem 1 pf; padd 0 pf` | no failure |

Three of the parts show their worth only in a replayed sequence or in the history that led to
them (12.4), not in 20,000 random histories of this length. That is a statement about the
search's reach as much as about the parts, and section 16 asks for the sequences to be replayed
with real files.

### 12.4 Counterexamples that changed the design

The design as first written had, for a whole store, only "some head is in force, and no deletion
record names a head higher than every head in force". The search found these histories against
it, in this order. Each is closed and stays in the search. `0` and `1` are the files.

1. **A decision that arrives without its head.** `prem 0 pf; del 0 (head) with a record;
   merge xc 1 0; del 1 (the removal) with a record`: the name was back on the list in file 1.
   The removal's head was deleted where it was written, a KeePassXC merge delivered the removal
   alone, and one more deletion took it. Change: a decision that no head accounts for makes the
   store incomplete, so file 1 refuses from the merge on. What is left is 6.4 item 3: deleting
   the lone decision there as well removes what made the store incomplete.
2. **A deleted head as high as the one in force.** `rule 0 root none; prem 1 pf; del 0 (the
   version) with a record; del 0 (head) with a record; merge kp_sync 1 0`: the root's older rule
   was in force in file 1. Both carriers were deleted in file 0, and file 1's own head, written
   concurrently, was exactly as high as the deleted one. Change, first: "at or above".
3. **A deleted head lower than the one in force.** `rule 0 root r; rule 1 root rw; del 0 (the
   version) with a record; rule 1 N r; merge kp_sync 1 0; del 1 (the older head) with a
   record`: the root gave read and write in file 1, where the two copies' rules meet to read.
   File 0's rule had lost its entry in file 0 and reached file 1 in file 0's head alone; that
   head was lower than file 1's own and was then deleted. Height cannot tell a head that was
   replaced from a concurrent one. Change, final: a head names all the heads it replaces, and a
   deletion record of a head must name one of those or a head in force.
4. **A head edited by hand.** `renew 1 c0 forever; del 1 (the answer) without a record;
   edit 1 (head); merge k2a 1 0`: file 1 asked for a renewal it had refused forever. The edited
   head no longer verified, the other copy's head arrived and looked like the newest. Change,
   first: a damaged head makes the store incomplete.
5. **A decision edited by hand after its head was lost.** `renew 2 c0 forever; rule 0 root none;
   del 2 (head) without a record; merge k2a 2 0; edit 2 (the answer)`: the same outcome through
   a damaged decision. Change, final: any damage in the group makes the store incomplete.

6. **A revocation undone by a deletion that left a record.** `revoke 1 c0; del 1 (head) with a
   record; merge kp_other 0 1; del 0 (the revocation) with a record`: client `c0` was admitted
   in file 0. The head was deleted where the revocation was made, an import delivered the
   revocation alone to the stale copy, and it was deleted there for good. The witness was in
   file 1's deletion records, which an import does not carry. Change: the identifier of every
   decision carries its kind and a keyed hash of what it is about, so the deletion record that
   the second deletion left still refuses the certificate (5.2 point 3). After this change the
   search finds a widening only where a removal left no record at all.

A seventh change came from replaying `rules-linked` survival C3c with a press in the middle: Seal
again now writes down the refusal of a marked node that has no rule, and of an object whose
deleted version nothing carries (5.4).

## 13. What it costs, and what it does not solve

**Not solved**: the six items of 6.4; `store-entries` security S2 and S3; the whole store deleted
for good and synchronized; KeePassDX, which is read and not measured for anything here.

**The steep refusal.** Losing the head stops the gate for that database. In `rules-linked` a lost
rule refuses one node. Here a lost rule entry costs nothing and a lost head costs everything
until a press. The press is one, and it is on the tab's first page.

**After an import with new identifiers and Delete Duplicate Entries**, KeePass can delete the
originals and keep the copies [M crit `rules-linked-survival` C6]. The copies do not verify under
their identifiers, so the store is gone in that file: nothing served. The tab would need a
"Put back what verifies" that writes the surviving contents again as new elements. Closing
without saving is the better remedy. Not modeled.

**Parts.** Three types, the head, the witness, the identifier's layout, the mark, the link, the
pin; four conditions for a whole store; presses Seal again, Settle, Adopt and keeping an authority. Classes that change are
those `option-store-entries.md` section 6 and `option-rules-linked.md` section 4.5 list, plus a
head writer, the MAC, the lattice in `AclResolver`, and a `503` word. `JsonBody` reads everything
as it stands. The tests that name the stored keys or the dialog's copy are rebuilt, which the
critics counted at 49 files for the database's data and 27 for the rules.

**What the formulation buys over the designs it draws on**: the reconcilers of the settings tab
and the fold rules of four designs become three types with one rule each; "what happens when two
copies both changed X" has one answer for every datum; and every rule of reading can be turned
off in a model to see what it was for.

## 14. Settled decisions: reversed and kept

**Reversed, each to be recorded**: the place (entries, not custom data), with the whole bullet
"The database's own `CustomData`, never the root group's"; "The authority takes the file to KDBX
4.1"; "Replayed key by key" and "`KPG.Profiles` is replayed as a difference" (the types do it);
"The grant editor writes into the dialog's own copy"; "The access control model is one document
per node" where it puts the document under a `KPG` key of the node, and the resolver gains
the limit; `CA.md` on records not being authenticated; the trap section's maintenance paragraph
stops being about the store. If the pin is taken: "writes nothing unasked" gains a second
exception beside owed answers.

**Kept**: every database its own authority, nothing on the host; the admission record load
bearing and read on every request; no `kpg://any`; no passphrase on the authority key; pairing
tokens and held approvals in memory; everything on the settings tab waits for OK, revocation
included; one writer, saved and verified; nearest wins per profile, an empty array is a rule, a
document that will not read refuses; the profile list decides what a client holds; refused and
absent answer the same 404; the JSON grammar and the one escaper; opening and reading change
nothing.

## 15. A migration, in one paragraph

None is designed, and the project's rule is that an older stored shape is not read [R inventory
O7]. If 0.1.0 databases are to be carried over, it is a press on the KPG tab, never an act at
open: read the six key families and every node's `KPG` item, write the authority with a new
store key, one element per record, refusal, name and rule, the marks, the witnesses and a head,
save and verify, and only then remove the keys and the items. Two things make that less simple
than it sounds. A rule that is not read inherits, so until the conversion a node that still
carries the legacy `KPG` item has to refuse, as a document that will not read does, or the first
request after the update reaches everything the root grants (`rules-central` security S5). And a
copy still opened by 0.1.0 has to find nothing to serve, which changing the authority's stored
shape in the same write gives. The alternative inside the rule: a database made with 0.1.0 reads
as having no authority, every ruled node refuses until its legacy item is removed on the tab,
and the operator creates, pairs and enters the rules again.

## 16. Measurements still needed, most important first

1. **KeePassDX 4.5.8 and 3.2.0 merges over this shape**, on the emulator: an entry only the
   arriving copy holds; arriving deletion records applied and not kept; an arriving entry older
   than a record of the open copy; a witness record through a save and a merge. Everything the
   model does under `merge dx` is read.
2. **The witness on the phone**: a deletion record that names nothing, under an identifier the
   plugin chose, through Keepass2Android's and KeePassDroid's saves and Keepass2Android's merge
   in both directions. `D1` and `D2` in `measured-android.md` are this for two records; run it
   with a KeePass Synchronize after, and with KeePass's Database Maintenance in between.
3. **The store group through each program**: a group whose UUID has the fixed prefix, made in one
   copy, through every merge; the same group deleted and made again; kdbxweb over that.
4. **The shape through KeePassLib 2.62 and `keepassxc-cli`**, replaying the model's scenarios
   with real files: the 20 of section 12.3 first. The probes of `store-entries` and
   `rules-linked` can be copied for it.
5. **The head's size and the read's cost** on KeePass's own thread: 20, 200 and 2,000 ruled
   nodes; the memo's hit rate; a write's time including the save.
6. **What each program shows of an entry with fifty custom strings** (a head), and whether any
   editor refuses, truncates or reorders them. Keepass2Android and KeePassDroid kept a 2861
   character value and timestamp-named strings [M android]; a head is more strings than that.
7. **KeePass's window**: the entry dialog's `EntrySaving` cancelling an OK while the writer is
   busy; a pin written inside `FileSavingPre`; Delete Duplicate Entries after an import with
   new identifiers; Database Maintenance clearing records while the store is incomplete.
8. **How often a phone saves over a changed file** in an arrangement
   with a synchronizer, which decides whether 6.4 item 1 needs the memory outside the file.
