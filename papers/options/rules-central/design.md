# Option `rules-central`: access rules as entries in the plugin's own group, naming the node by UUID

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. One design, the
strongest form of this option that I could find. It covers datum 7, the access rules. Data 1 to 6
are another option's subject; this design uses the group `option-store-entries.md` defines and
needs nothing else from it. Datum 8 is one line in section 2.

## How statements are marked

- **[M probe]**: measured for this report, today, from a console: KeePass 2.62's KeePassLib and
  `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 over invented databases. Section 15 describes the
  runs and names the files.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M store-entries]**:
  measured in the ground report of that key, in `spikes/revocation-merge` as `ground-measured.md`
  and `ground-inventory.md` digest it, or in the probe of `option-store-entries.md`.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[R 2.62]**: read by me in `KeePass-2.62-Source.zip` (the copy the `keepass` ground report
  verified), file and line given. **[R plugin]**: read in the review4 worktree at `a42e38aa`.
- **[A]**: assumed. Neither run nor read. Every assumed statement about another program comes
  back in section 16 as a measurement to make.

## 1. The design on one page

1. **Nothing is written on a governed node.** No custom data, no string, no entry inside the
   governed group. A group or entry that carries a rule looks, in every program, exactly like one
   that carries none.
2. **A rule is an ordinary entry in the plugin's own group**, naming the node it governs by UUID
   in a custom string. The rule document is today's, byte for byte.
3. **One entry per version of a node's rule, written once and never changed.** Changing a rule
   adds an entry that names the versions it replaces. Taking a rule away adds an entry whose
   document grants and seals nothing. In the ordinary state a node has one newest version, and
   that is its rule.
4. **The entry's identifier is derived**: a fixed mark, the first six bytes of the node's
   identifier, and a digest of the entry's content. So a rule entry is recognized by its
   identifier alone, a deletion record of a rule entry still tells which node it was about, and an
   entry whose content was changed by hand no longer matches its identifier.
5. **What is in force at a node**: its newest version. Two newest versions, which is what two
   copies of the database changing the same node's rule between two synchronizations leave, give
   what both of them allow. A rule entry deleted or changed by hand makes the node refuse, there
   and below, exactly as a document that will not read refuses today.
6. **A rule entry counts wherever it is found**: in the group, in the recycle bin, in a user's
   group somebody dragged it to. Place means nothing, because every way of not counting a rule
   could widen what a client reaches.
7. **A copy of a rule entry under another identifier closes the database** (`503 undecided`)
   until the operator removes the copies. Only an import with new identifiers or a duplication
   makes one, and that same act copied groups and entries no rule names.
8. **No modification time, no history item, no custom data and no group field is read.** Nothing
   is written when a database is opened or merged.
9. **The grant editor keeps its OK and Cancel** by holding the change on the tab and applying it
   when the dialog closes with OK. The reflection on `m_sdCustomData` goes.
10. **The API cannot see or make any of it**: the group and every entry carrying a `KPG.` string
    are as absent as the recycle bin, and no route makes a string whose name begins `KPG.`.

**The honest weakness, first.** A rule that names a node by identifier does not cover a copy of
that node. Duplicate an entry whose rule narrows what its group grants, and the copy is governed
by the group. On the node, a rule would have been copied along. Point 7 closes the one case the
gate can see, an import that copied the rules too. A single node duplicated by hand in any program
is not seen, and section 13 does not pretend otherwise. Everything else this design buys follows
from the same fact turned around: nothing another program does to a node can touch its rule.

## 2. Where a rule lives

### 2.1 The group

The store group of `option-store-entries.md` section 2: fixed UUID
`FE4040A9E0FAE884FA8BECAEEE9FBB46` (the first 16 bytes of SHA-256 over `KPG/1/group/`, computed
again here [M probe]), named `KeePassGate`, made under the root, searching and auto-type off, one
fixed line of notes, no subgroups. Found by its UUID, never by its name. Rule entries sit directly
in it. If data 1 to 6 end up somewhere else, this design makes the same group with the first rule.

No decision here reads the group: not its place, its flags, its time or its notes. It is where the
plugin puts rule entries and where a person finds them.

### 2.2 One rule version, one entry

| Part of the entry | Content |
| --- | --- |
| UUID | 16 bytes: the mark `AED8E7D3` (the first 4 bytes of SHA-256 over `KPG/1/rule/`), then the first 6 bytes of the governed node's UUID (the node tag), then the first 6 bytes of SHA-256 over `KPG/1/rule/<KPG.Node>/<KPG.After>/<KPG.Data>` |
| `Title` | `KPG rule ` and the first 8 hex digits of the node. Fixed words and hex only, never a node's name: names are text a client may have supplied, and KeePass compiles titles as placeholders when it matches auto-type [R keepass 9] |
| `UserName`, `Password`, `URL` | present and empty, for the reasons `option-store-entries.md` section 3 gives: nothing for reports, browser matching or autofill to read, and nothing for a KeePassXC save to add |
| `Notes` | `KeePassGate data. Managed on the KPG tab in KeePass.` |
| custom string `KPG.Kind` | `rule` |
| custom string `KPG.Node` | the governed group's or entry's UUID, 32 hex digits |
| custom string `KPG.After` | the identifiers of the versions this one replaces, sorted, one space between them; the word `none` for a node's first rule |
| custom string `KPG.Data` | the rule document exactly as `AclDocument.Write` produces it today (`Acl/AclDocument.cs:248-292` [R plugin]); for a rule taken away, `{"default":"inherit"}`, which is what `AclDocument.Empty` writes |
| protection, expiry, tags, attachments, entry custom data, history | none. A rule is no secret inside the database |

```text
Group  KeePassGate                 (searching off, auto-type off)
Entry  KPG rule 4F0C91AA           UUID AED8E7D3 4F0C91AA22B7 61C09D5E13F2
  User name, password, URL   (empty)
  Notes      KeePassGate data. Managed on the KPG tab in KeePass.
  KPG.Kind   rule
  KPG.Node   4F0C91AA22B7D31E8A6640C5B9E2F7A1
  KPG.After  AED8E7D34F0C91AA22B70B7744A1C3E9
  KPG.Data   {"default":"deny","ops":["read","list"]}
```

**Why custom strings.** They are the one carrier every program keeps: KeePassXC in four releases
kept every rule entry's identifier, strings, flags and times through a plain save [M probe];
Keepass2Android, KeePassDX 4.5.8 and 3.2.0 and KeePassDroid read custom strings and write them back
[R android 2]; kdbxweb and pykeepass kept them [M others]. Entry and group custom data, where
rules live today, are what KeePassDroid and KeePassDX delete and what KeePassDX and kdbxweb merges
replace [M spike; M others].

**Why `none` and not an empty string.** KeePassXC kept an empty `KPG.After` in all four releases
[M probe], but no Android program has been measured with an empty custom string, and KeePassium
drops a field whose name and value are both empty [R others 3]. A value that is never empty takes
the question away.

**Size and growth.** A rule entry with a 50 character rule and one predecessor holds 232 characters
of strings [M probe]; with today's largest readable rule, 4482 characters [M inventory 8], about
4.7 KB. Growth is one entry per change of a rule. The plugin never removes a replaced version on
its own (section 5.4 has why), so a database whose operator has changed rules 500 times holds 500
small entries.

**The identifier's three parts, and what each is for.**

- *The mark* lets the read path find rule entries by looking at four bytes of each entry's
  identifier: 0.17 ms for 10,060 entries, against 1.13 ms when every entry is also asked for a
  string [M probe]. It also lets a deletion record be recognized as a rule entry's.
- *The node tag* makes a deletion record, which holds nothing but an identifier and a time
  [R keepass 5], still name the node whose rule entry was deleted. Without it a deleted rule
  would look like a rule that never was. Two nodes share a tag with a chance of 2^-48 a pair, and
  the cost of that is one node refusing for another's damage.
- *The digest* makes the same decision made in two copies the same entry [M probe, scenario 06],
  and makes an entry edited by hand detectable: its content no longer derives its identifier.

**Datum 8**, the recycle bin markers of the open T049 design, would be one more kind of record in
this group, added once and never changed. Nothing here needs them.

### 2.3 Why one entry per version, and not one entry per node edited in place

| Variant | What a merge does | Verdict |
| --- | --- | --- |
| One entry per node, its `KPG.Data` edited at each change | The same entry changed in two copies is taken whole by its modification time in every merge measured or read [M keepass 1a; M keepassxc 4; M others 4; R android 1.1]. At equal times one side is lost with no trace, and which side depends on the program and the direction [M keepass 1a; M keepassxc 4; R android 1.1]. A time moved forward on stale content makes the stale content win [M keepass 1a], and KeePassDX moves an entry's own time when it is recycled and restored [R android 1.4] | Rejected: a narrowing can lose to a clock |
| **One entry per version, never changed** | Entries added in two copies both arrive, in every KeePass method, in four KeePassXC releases, in both directions [M keepass 1d; M keepassxc 4; M probe] and in the Android merges as read [R android 1.5]. No two versions of one entry ever exist, so no time is compared | **Chosen** |

## 3. What is in force at a node

Read from the set of rule entries in the file, with no time in it:

1. **A version of node N** is an entry whose `KPG.Kind` is `rule`, whose `KPG.Node` is N, and whose
   four strings read strictly. Its identity is the identifier its content derives.
2. **Replaced**: a version is replaced when another version present names it in `KPG.After`.
3. **Newest**: the versions of N that are not replaced. One is the normal state, and its document
   is N's rule.
4. **No version**: silence, N inherits. As today.
5. **Several newest versions**: the document in force is their **meet**: a profile that all of them
   name keeps the words all of them give it; a profile that only some of them name keeps nothing;
   the default is `deny` when any of them denies. Example, measured: `{"default":"inherit",
   "reader":[]}` and `{"default":"deny","ops":["read","update","list"],"reader":["list"]}` give
   `{"default":"deny","ops":[],"reader":[]}` [M probe, scenario 03]. The meet is an ordinary
   document, so the resolver needs no new kind of answer, and it never grants a profile more than
   any one of the versions would: where all name the profile it is their intersection, where one
   does not it is nothing, and where none does the default only ever moves toward `deny`.
6. **Damage at N: N refuses**, there and below, as a document that will not read does today
   (`Acl/AclResolver.cs:484-491` [R plugin]). Three kinds:
   - an entry bearing the mark and N's tag, or naming N in `KPG.Node`, whose content does not
     derive its identifier or will not read (**changed by hand**);
   - a deletion record bearing the mark and N's tag that names neither a version present nor a
     version some present version replaces (**a rule entry was deleted**);
   - a newest version whose document will not read.
7. **Copies**: an entry with `KPG.Kind` `rule` under an identifier that does not bear the mark is a
   copy. Its content is read, so a copy stands in for an original that has gone. While any copy
   exists the database answers every request `503 undecided` (section 6.4).

Three properties:

- **A version takes over only by naming what it replaces.** An entry that names nothing is at
  most one more newest version, and the meet can only narrow.
- **Nothing is trusted for being in a place**, so nothing changes when a person or a program moves
  an entry.
- **A stale copy of the database can only lack versions.** Merging it in adds nothing that is
  newest, and merging into it adds what it lacked.

## 4. The read path

One pass per hold, which is once per request and once more inside a write's turn, on the calling
thread while KeePass's thread stands still (`Ui/IReadHold.cs` [R plugin]):

1. Every entry of the tree: four bytes of its identifier against the mark; an entry without the
   mark is asked for `KPG.Kind` (to find copies).
2. Every deletion record: four bytes against the mark.
3. The rule entries found are read strictly and their identifiers derived again. That is one
   SHA-256 per rule entry, kept from hold to hold against the entry's string objects, which KeePass
   hands back unchanged for an unchanged entry [M store-entries 4].
4. The index: for each node identifier, the one document text to give the resolver (the newest
   version's, the meet, or a text that will not read, with the reason for the audit line), and for
   each damaged tag the reason.

Then, for every node a walk meets, `KdbxNodes.DocumentOf` (`Bridge/KdbxNodes.cs:65-73` [R plugin])
asks the index by the node's identifier instead of asking the node's custom data. `IAclNode`,
`AclResolver`, nearest wins per profile, the empty array, `default`, `AclDocuments` and the
profile list are untouched: the resolver still sees a document text or nothing at each node.

**Cost** [M probe, in memory, a console process, no gate around it]:

| | 10,060 entries, 501 groups | 50,060 entries, 501 groups |
| --- | --- | --- |
| every identifier tested for the mark | 0.17 ms | 0.86 ms |
| the same, and every unmarked entry asked for `KPG.Kind` | 1.13 ms | 8.16 ms |
| 5000 deletion records tested for the mark | 0.03 ms | 0.03 ms |
| the whole reading: walk, string test, 60 rule entries for 20 nodes, their digests, the index | 1.43 ms | 9.38 ms |
| one index lookup for each user entry, which a full listing makes | 0.14 ms | 1.06 ms |
| today's read for comparison, `CustomData.Get("KPG")` on each | 0.55 ms | 4.54 ms |

So a listing gets cheaper per node and every request pays the pass. Today a read of one entry pays
a handful of lookups; here it pays about 1.4 ms in a database of ten thousand entries. Nearly all
of that is the string test that finds copies. If `option-store-entries.md` is taken, its own pass
makes the same test on every entry [M store-entries 4] and the two are one walk. Nothing that
grants is kept from one request to the next, as today.

## 5. The write path

### 5.1 The grant editor: OK and Cancel without the dialog's copy

Today the editor writes into `m_sdCustomData`, the dialog's private copy, found by reflection
(`Bridge/NodeDialog.cs:17-22`, `KeePassContract.cs:184` [R plugin]), and KeePass puts that copy on
the node at OK. Here the rule is not the node's, so no dialog copies it and no dialog can undo it.

- **`IGrantStore` keeps its two methods** (`Ui/IGrantStore.cs` [R plugin]). The store behind it is
  a buffer of the tab's own: `Read` answers the buffered document, or else the document in force
  for the node's own versions; `Write` changes the buffer only. `GrantPanel` and `GrantSheet` do
  not change.
- **OK**: a handler on the dialog's `FormClosed` applies the buffer when `DialogResult` is `OK`
  and drops it otherwise. That is how `AuthorityPanelFactory` already applies the settings tab
  (`Ui/AuthorityPanelFactory.cs:198-271` [R plugin]). Applying means: add one rule entry whose
  `KPG.After` names the node's newest versions, set `PwDatabase.Modified`, refresh the window.
- **Why the plugin has to mark the database modified.** After an entry dialog's OK KeePass marks
  it only when the entry itself changed (`MainForm_Functions.cs:5800-5812`, `bMod = bOK &&
  dlg.HasModifiedEntry` [R 2.62]), and a rule no longer changes the entry. After a group dialog's
  OK KeePass always marks it (`MainForm_Functions.cs:5830-5831` [R 2.62]).
- **Cancel**: the buffer is dropped; nothing was written anywhere.
- **Escape with only a rule changed.** KeePass's entry dialog asks whether to save before closing
  when its own entry changed (`PwEntryForm.cs:2146-2192` [R 2.62]), and it cannot see the tab's
  buffer. The tab asks the same question from its own `FormClosing` handler when the buffer holds
  a change and KeePass did not ask. When KeePass asked and was answered Yes, it sets
  `DialogResult.OK` itself (`PwEntryForm.cs:2179-2183` [R 2.62]) and the buffer is applied. The
  group dialog has no such question today (`GroupForm.cs:233-235` is its whole Cancel [R 2.62]).
- **Add Entry and Add Group.** A new entry has its identifier before the dialog opens
  (`MainForm_Functions.cs:6772`, added to its group at `:6812` after OK [R 2.62]), and a new group
  is in the tree before the dialog opens and is taken out on Cancel (`MainForm.cs:1367-1378`
  [R 2.62]). So a rule chosen on either is applied at OK like any other, and Cancel leaves
  nothing.
- **Several entries edited together, and a revision opened for viewing**: read only, as today. A
  revision has no rule of its own any more, so its sentence changes.
- **The same rules are never written again**: when the buffer equals the document in force and
  the node has one newest version, nothing is added.
- **What goes**: `KeePassContract.NodeFormCustomDataField`, `NodeDialog.Working`, the read-only
  state for a KeePass that renamed the field, and the test pinning that field. The dialog's node
  and database are still read as today.

Two things the entry dialog does today stop happening, both for the better. A rule change no
longer makes a revision of the entry or moves its time, so it can no longer outrank a password
change made earlier in another copy. And restoring a revision no longer brings an old rule back
(`DESIGN.md:972-973` [R inventory 1.2]).

### 5.2 Two more states on the tab

Beside local, inherited and sealed off, the tab's warning band shows:

- **Two versions**: "This rule was changed in two copies of the database. The gate applies what
  both allow. OK keeps the rules shown." The table shows the meet.
- **Refusing**, with the reason: "A rule entry of this node was deleted." or "A rule entry of this
  node was changed outside KeePassGate." The table shows the newest version that still reads.

In both, OK writes a **repair**: one version whose `KPG.After` names every newest version, every
entry changed by hand and every unanswered deletion record carrying the node's tag, after which
the entries changed by hand are deleted. One OK settles the node. Nothing is repaired without it.

### 5.3 A fourth page on the settings tab: Rules

The KPG tab on the database settings dialog gets a page of its own for what is wrong with rules,
because that is a subject of its own and the three pages are full. In the ordinary state it holds
one line, the count of rules and of nodes that carry them. Otherwise:

- **Copies of rule entries: N** and a button, **Remove copies**. Where a copy's original is gone
  the copy is first put back under its own identifier, and any deletion record naming that
  identifier is removed; then every copy is deleted. The page tells the operator that groups and
  entries the import added are governed by where they are.
- **Nodes that refuse: N**, named by path, each settled on its own dialog.
- **Rules without a node: N** and a button, **Remove**.

Both buttons wait for the settings dialog's OK like everything on that tab, through
`PendingDatabase`, and are worked out again against the live database at OK.

### 5.4 What the plugin deletes, and what it never does

It deletes a rule entry in three cases only, each on the operator's press: an entry changed by
hand, at the repair; a copy; a rule without a node. Every deletion is for good and leaves a
deletion record, which is what lets a KeePass synchronize carry it [M keepass 5].

**It never removes a replaced version of a living node, and it prunes nothing on its own.** Two
measurements are why:

- A replaced version removed in one copy comes back from another through every merge that does not
  carry deletions, which is every KeePassXC merge and three of KeePass's methods [M keepassxc 2;
  M keepass 5]. If the version that replaced it has since been replaced and removed too, the
  returning one is named by nothing present and counts as a second newest version.
- Rules removed after their node was deleted make the node refuse when another copy brings it
  back [M probe, scenarios 13 and 14]. That is the closed direction. But Remove also destroys a
  narrowing that a copy still holding the node has not received: merged into such a copy by a
  merge that carries no deletions, nothing arrives, and that copy goes on applying the older rule
  to a node it still has [M probe, scenario 14, into B]. So Remove is the operator's press, with
  that sentence beside it, and never something a pairing happens to trigger.

The API never writes a rule. `DatabaseWriter` stays the only place a request writes, and writes no
rule entry.

### 5.5 What changes in the plugin

| Class | Change |
| --- | --- |
| `AclDocument` | `CustomDataKey` goes. Format, reader, writer and ceilings stay. Gains the meet of several documents |
| `AclResolver`, `AclGrant`, `AclVerb`, `AclInheritance`, `AclDocuments`, `VerbMask` | unchanged |
| new `Acl/RuleVersion` | the four strings read strictly, the derivation, the mark |
| new `Bridge/RuleIndex` | the pass of section 4, built once per hold, carried by the hold |
| new `Bridge/RuleStore` | the only writer of rule entries: add a version, the repair, remove copies, remove rules without a node |
| `KdbxNodes` | `DocumentOf` asks the index |
| `IRuledNodes`, `DatabaseNodes` | enumerate the index's nodes; the Profiles page counts from them |
| `NodeDialog`, `IGrantStore` | section 5.1 |
| `GrantPanelFactory` | subscribes `FormClosed` and `FormClosing` |
| `GrantPanel`, `GrantSheet`, `GrantTab` | the two states of section 5.2; nothing else |
| `GroupDialogs` | stops mattering to rules: no dialog holds a copy of one. It stays for the vault's writes to nodes, the bin choice and KeePass's progress window |
| `DatabaseWriter` | no change for rules. Its turn reads the index again where it reads the record again |
| `PendingDatabase`, `PendingStore`, `PendingChanges` | the two buttons of the Rules page; nothing else. The settings tab does not edit rules |
| `AuthorityStore`, the prune of records | untouched. Recreating or deleting the authority leaves rules alone, as today |
| `KdbxVault`, `VaultHandlers` | the guard of section 9 |
| `OpenDatabaseTargets` | answers `503 undecided` while a copy exists |
| Tests | `SnapshotHazardTests` and the rule cells of `MergeInRealDatabaseTests` give way to tests pinning what section 7 rests on against the pinned KeePass: rule entries added in two copies both arrive under every method, and a deletion record does what section 3 point 6 needs |

## 6. The cases the task names

All cells [M probe]: 14 scenarios, each merged in both directions under KeePassLib 2.62's
Synchronize, Overwrite if newer, Overwrite existing and Keep existing (112 cells) and by
`keepassxc-cli merge` in four releases (112 cells), each result saved and read back. "A" and "B"
are two copies of one database.

### 6.1 Other programs editing the governed node or its group

| Scenario | Result in every cell |
| --- | --- |
| 01: A narrows entry X and seals group U; B, not knowing, changes X's password, edits and adds entries in U, and U's own time and notes move later | X and U carry A's new rules; B's password and entries are there too |
| 02: the same for X, both in the same second | A's rule |
| 04: A takes X's rule away; B changes X's password later | no rule, as A decided |
| 05: A gives Y its first rule, B gives Z its first rule | both |
| 06: A and B make the same change at different times | one entry, one rule |

This is the case rules on their node lose: there the later edit takes the entry whole and the
rule's version goes into history [M keepass 1a], and a group's rule goes with whichever copy's
group was touched last [M spike].

### 6.2 Two rule entries for one node after a merge

Scenario 03: A sets X's rule to one document, B to another, each replacing the same version. Both
entries arrive in all 16 cells and X carries the meet (section 3, point 5). The editor shows it
and one OK settles it.

### 6.3 The node moved, deleted, or brought back

| Scenario | KeePass Synchronize | KeePass, the other three methods | KeePassXC, four releases |
| --- | --- | --- | --- |
| 10: A narrows X; B moves X to another group | X in the new group with A's rule | A's rule; X where the target had it | X in the new group with A's rule |
| 11: A narrows X; B deletes X for good later | X gone in both; two rule entries left without a node | into B the same; into A, X stays with A's rule | X stays or comes back, with A's rule |
| 12: B deletes X for good; A changes X's password later | X back with its rule | the same | the same |
| 13: A deletes X and removes its rule entry (the Rules page's Remove); B changes X's password later | X back; **refuses** | into A refuses; into B, nothing arrives and B keeps X with its rule | into A, X and its rule entry both come back and the rule is in force; into B, nothing arrives |
| 14: as 13, and A had narrowed X before deleting it, which B never saw | X back; **refuses** | into A refuses; into B, nothing arrives and B keeps X with the older rule | into A **refuses** (the narrowing's entry did not come back and its deletion record is unanswered); into B, nothing arrives and B keeps X with the older rule |

**A rule whose node is gone** grants nothing: it is asked about only when a walk reaches its node.
It is counted on the Rules page and removed there.

### 6.4 The node duplicated, and programs that give new identifiers

- **KeePass's File then Import with "Create new IDs"**, which it preselects [R keepass 2.4],
  importing a copy of the database into itself: the originals and their rules are untouched; a
  second group named `KeePassGate` arrives under a new identifier holding copies of the rule
  entries; the copies of U and X arrive under new identifiers and **no rule names them**
  [M probe]. The copies of the rule entries are the one sign of it the gate can read, so while one
  exists the database answers `503 undecided`, the word a database with more than one authority
  already answers with, for the same reason: the operator has to decide in KeePass. Closing without
  saving is the better remedy; Remove copies on the Rules page is the other.
- **KeePass's Delete Duplicate Entries after such an import** deleted 7 entries, the originals
  among them: X, Y and Z were then gone under their own identifiers, only their copies were left,
  and the rule entries survived only as copies [M probe]. The copies' content still counted for
  group U, whose own identifier was untouched. The entries' rules name identifiers that no longer
  exist. This is the worst measured case for this design, and it is closed only by the `503`.
- **On the store as the plugin writes it, Delete Duplicate Entries deletes nothing**: no two rule
  entries have equal strings, even when three nodes hold the same rule text and one node holds a
  text for the second time [M probe, 0 deleted of 6].
- **A single node duplicated, copied and pasted, or made from a template**, in KeePass or any
  other program: the copy has a new identifier and no rule, and is governed by where it sits.
  The gate cannot see it. Section 13.
- **An import of a file that holds no rule entries** (another format, or a copy made before the
  plugin was used): new nodes with no rules, governed by where they land. Under Create new IDs
  they land in groups added at the root [M keepass 2.4], so they get what the root grants.
- **KeeShare**: a share is imported in Synchronize mode [R keepassxc 3]. That its entries keep
  their identifiers, and that the store group is in no share unless the operator shares that very
  group, are [A]. The rules then stay behind, which is the right outcome: a rule is this
  database's policy, and its profile names mean nothing in another database.
- **A repair of duplicate identifiers**, which KeePass and KeePassDX offer at load
  [R keepass 9; R android 4.2], gives one of two entries a new identifier. For a rule entry that
  makes a copy, and the `503`.

## 7. What a save and a merge do to it

| Program | A save | A merge | What the gate then does |
| --- | --- | --- | --- |
| KeePass 2.62 | keeps entries and strings; KDBX 4.0 is enough [M keepass 2.1]; every file the probe wrote is 4.0 [M probe] | section 6, 112 cells [M probe]. Synchronize carries a deletion to the other copy; the other methods do not [M keepass 5] | the newest version, the meet, or a refusal; never an older rule in force |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | every rule entry's identifier, strings, protection flags and times unchanged, the group's two flags and notes kept, file still 4.0 [M probe] | section 6, 112 cells; the four releases gave the same results [M probe]. It never applies a deletion, so a deleted rule entry comes back from the other copy and counts again [M probe, scenario 07; M keepassxc 2] | as KeePass. A node that refused for a deleted rule entry is in force again once that entry is back, with the rule the operator last set |
| Keepass2Android 1.15-r6, 1.08d-r5 | keeps entries, custom strings and their flags [R android 2; entries as wholes M spike] | an entry the open copy lacks is added to the group of the same identifier, or to the root when that group is missing; the two copies' deletion records are joined [R android 1.5, 3.1]. An entry only the open copy held stayed in 18 of 18 runs [M spike]. An entry only the arriving copy holds: not measured | by the reads: the union. A rule entry that landed at the root counts all the same, since place means nothing |
| KeePassDX 4.5.8 | keeps entries and custom strings [R android 2; entries as wholes M spike]; writes 4.1 [M spike] | an arriving entry is added and a missing group recreated; arriving deletion records are applied and not kept [R android 1.5, 3.1]. Not measured | by the reads: the union. A deletion it applied without keeping the record leaves a node whose newest rule entry is gone without trace in that file; section 13 |
| KeePassDX 3.2.0 | as 4.5.8 for entries and strings [R android 2]. It deletes group custom data [M spike], which holds nothing of ours | an arriving entry is added only if its group exists in the open copy, else dropped [R android 1.5]. Not measured | by the reads: the union, since the group exists in every copy that holds a rule. A dropped entry would leave that copy with its older rule, as if it had not merged |
| KeePassDroid 2.7 | kept every entry it was given [M spike]; custom strings read and written back [R android 2]. It deletes all node custom data [M spike], which holds nothing of ours | has no merge [M spike] | unchanged |
| kdbxweb 2.1.1 (KeeWeb) | keeps strings and flags [M others] | entries added in two copies both arrive [M others]; it removes whatever a deletion record names, whatever the times [R others] | the union; its stricter deletions end in a refusal |
| Strongbox, KeePassKit, keepass-rs | carry strings in their models [R others] | add a node the open copy lacks [R others] | by the reads: the union |
| A whole file written over another: Keepass2Android's "No, overwrite", a KeePassDX save within ten seconds, any KeePassDroid save over a changed file, a file synchronizer's conflict copy | | nothing merges [R android 5.5; M spike; R others] | **not solved**, section 13 |

What broke the present store does not reach this one, because the thing it broke is not used:
node custom data deleted on save by KeePassDroid and KeePassDX 3.2 and 3.3, a stale rule put under
a newer time by a KeePassDX merge, a group's rule kept from the target by a KeePassXC merge
[M spike], and a group's time moved by edits inside it [R android 1.4] or by a save
[M spike].

## 8. What a person sees, what they can break, and what the gate does

**What they see.** In every program: entries titled `KPG rule 4F0C91AA` in the group
`KeePassGate`, each with four extra fields in clear. Search, autofill, auto-type and the password
reports leave the group alone where its two flags are honored [M keepass 9; R keepassxc 8;
R android 4.2]. **On a governed group or entry they see nothing at all.** Only KeePass with the
plugin shows, on the node's KPG tab, that it carries a rule. In another program a person cannot
tell which entries are narrowed.

| By hand, in any program | What the gate does | Basis |
| --- | --- | --- |
| Edits a rule entry's title, notes or icon | nothing; none is read | section 3 |
| Edits any of its four `KPG.` strings | the node **refuses**. A KeePass synchronize and a KeePassXC merge carry the edited entry to the other copy, which then refuses too | [M probe, scenario 09] |
| Restores that entry's earlier revision | a restore puts the revision's strings back [R inventory 4.2], so the content derives the identifier again and the rule is back in force | not run |
| Deletes the newest rule entry of a node into the recycle bin | nothing: it counts where it is | [M probe, scenario 08] |
| Deletes it for good | the node **refuses**. A KeePass synchronize makes the other copy refuse too. A KeePassXC merge with a copy that still holds the entry brings it back, and the rule is in force again | [M probe, scenario 07] |
| Deletes for good a version that another replaced | nothing | section 3, point 6 |
| Deletes the whole group for good | KeePass writes a deletion record for every entry in it [R 2.62, `PwGroup.cs:1367-1386`], so every node that carried a rule **refuses** | section 3 |
| Deletes a rule entry for good and then clears the deleted objects information in Database Maintenance | no trace is left in that file: an older version of that node is the newest again, or the node inherits | not solved, section 13 |
| Moves a rule entry, or the group, anywhere | nothing | [M probe, scenario 08] |
| Duplicates a rule entry or the group; imports with Create new IDs | `503 undecided` until the copies are removed | [M probe] |
| Duplicates, copies or pastes a governed node | the copy carries no rule | [M probe]; section 13 |
| Moves a governed node | its rule follows | [M probe, scenario 10] |
| Deletes a governed node | its rule entries stay, inert, until removed on the Rules page | [M probe, scenario 11] |
| Runs Delete Duplicate Entries | nothing deleted from the store as written | [M probe] |
| Runs Delete Empty Groups | the group goes only when it holds nothing, and is made again with the next rule | [M keepass 3] |
| Writes a rule entry by hand, with the right identifier, naming the newest version | it takes effect. Whoever holds the master key can write anything in the file, as today [R inventory P23] | conceded |
| Exports, prints | rule entries go out in clear; they are not secret | [M keepassxc 8; R keepass 9] |

Every refusal is shown with its reason on the node's own KPG tab, counted and named on the Rules
page, logged by identifier, and named in the audit line of each request it refuses.

## 9. Keeping the API and the reference client out

Today no route touches custom data, so rules are out of reach by construction [R inventory P15].
Here the guard is explicit, in the vault and not in any rule:

1. **The store group and everything in it is absent**, as the recycle bin is
   (`Bridge/KdbxVault.cs:2123-2154`, `Discarded` [R plugin]): not listed, counted, tagged or
   addressable, and answered with the `404` a refused node gets.
2. **An entry anywhere that carries a string whose name begins `KPG.` is absent the same way.**
   That covers a rule entry in the recycle bin or in a user's group. The mark is not part of this
   test: an ordinary entry bears it by chance once in 2^32, and it must not vanish for that.
3. **No route makes or names a string beginning `KPG.`**, compared without regard to ASCII case:
   `400 malformed` from the request alone, before any entry is looked at.
4. **No route lets a caller choose an identifier**: a new entry and a new group get one KeePass
   draws at random (`Bridge/KdbxVault.cs:603`, `:385` [R plugin]), and `createEntry` draws again
   if what it drew bears the mark. So a client can never make an entry the read path takes for a
   rule entry, and a copy needs `KPG.Kind`, which point 3 refuses.
5. **A group that holds the store group cannot be removed.** A governed group, on the other hand,
   is empty when it is empty: no rule entry sits inside it.
6. The routing document gains nothing. The two routes asking what a client may do at a node
   answer from the same index.

The reference client changes nothing. `pykeepassgate.api` has no method that could reach a rule,
`pykeepassgate.refs` follows references by identifier and meets the same `404`, and
`pykeepassgate.testing` models no rules. `ReferenceVectors.json` is untouched, since nothing is
added to any entry a client can read. What the API cannot stop is KeePass itself: a search
reference in a user's entry can resolve to a rule entry [M keepass 9], for the operator, in
KeePass.

## 10. The KDBX version it needs

**4.0.** Entries, strings, the group's two flags and deletion records are all written at 4.0 by
KeePass 2.62 [M keepass 2.1]; every file the probe made is 4.0, and KeePassXC 2.4.2 and 2.6.6,
which only write 4.0, kept all of it [M probe]. KeePassXC 2.7.12 and 2.8.0 wrote 4.1 for the one
scenario in which a merge moved an entry and recorded its previous parent [M probe, scenario 10;
M keepassxc 4], which is theirs and not the store's. Rules no longer need 4.1 or any per-key time.

## 11. Compared with rules that travel on their node

`option-rules-on-node.md` keeps the rule text on the node and adds a seal for each version in the
plugin's group, and it names its own weakness: with the seal in place, the text on the node is the
part that can be damaged. This design is that seal with the text inside it.

| | Rules in the plugin's group (this design) | Rules on the node |
| --- | --- | --- |
| Another copy edits the governed entry later | no effect [M probe 01, 02] | the rule's version goes into history; it has to be found there, and refuses at a tie in the losing direction |
| A group's time moved by edits inside it or by a save | no effect [M probe 01] | needs a rule entry inside the group to escape it |
| The node moved | follows [M probe 10] | follows |
| The node deleted | rule entries left behind, removed on a press | goes with the node |
| **The node duplicated, or imported with new identifiers** | **the copy has no rule and inherits**; the gate sees only an import that copied the rules too | the copy carries the rule text, and with seals it refuses until the operator accepts it |
| A shared or exported subtree opened in another database | no rule goes with it | the rule text goes with it |
| What a person sees on the node in other programs | nothing | a field on each ruled entry, an entry in each ruled group |
| What a person can damage in a user's group | nothing | the field, the rule entry |
| Whether a ruled group is empty | unaffected | the rule entry has to be discounted |
| What the API has to hide | one group, and stray entries that carry a `KPG.` string | reserved names on every entry and every field route |
| History | never read | the store of last resort |
| The entry when its rule changes | untouched | a revision and a new time |
| Restoring a revision | does not touch the rule | the seal keeps the rule |
| The dialogs | a buffer on the tab; no private field | the entry dialog's public strings and two events; a buffer for groups |
| Cost of a read | one pass over the tree per request, then one lookup a node | a lookup a node, an index of seals, a look at each group's entries |
| Refusals with nobody at fault | a rule entry deleted elsewhere and synchronized; a node back after its rules were removed | a tie, a rule aged out of history, Keep existing, every copied node |

The trade is one sentence: rules on the node keep covering a copy of the node and pay for it in
everything another program can do to a node; rules in the plugin's group are out of every other
program's way and do not cover a copy.

## 12. Settled decisions: reversed and kept

**Reversed, each to be recorded as a reversal.**

1. "The access control model is one document per node ... Grants live under a `KPG` key": the
   document is kept, its place is a rule entry in the plugin's group.
2. "The grant editor writes into the dialog's own copy, `m_sdCustomData`": the tab's own buffer,
   applied on `FormClosed` with OK.
3. "The plugin writes no rule of its own" (`CA.md:127-138`): it writes rule entries, at the
   operator's OK.
4. "The trap that shaped the design", as far as rules go: `GroupForm` and the entry dialog no
   longer hold a rule, and "testing any `CustomData` work means pressing OK" stops applying to
   rules. `GroupDialogs` stays for the vault.
5. An entry's history holds each revision's rule and a restore brings it back
   (`DESIGN.md:972-973`): no longer.
6. "The editor never writes the same rules in other words", for the reason given there (the entry
   dialog would see a changed entry): the rule stays, the reason goes, since a rule no longer
   changes the entry.
7. A rule travels with its node (habit H5 in `ground-inventory.md`): given up. Section 11.
8. `503 undecided` gains a second cause, copies of rule entries.
9. Only a rule that will not read refuses: a node also refuses for a rule entry deleted or
   changed by hand, and two versions give their meet.
10. "The KPG tab has three pages of its own": four.

**Kept.** Nearest wins, per profile; the document format and its ceilings; an empty array is a
rule; `default`; no per-subject opt-in; `CRUSDLAP`, six verbs and two switches; the profile list
decides what a client holds; the router asks before any handler; a refused node and an absent one
answer the same `404`; OK keeps a rule and Cancel discards it, on both dialogs; nothing on a tab
scrolls; everything on the settings tab waits for OK; opening or reading a database changes
nothing in it; live reads on every request; one JSON grammar and one escaper (the rule document is
unchanged, and the two new strings are hex and spaces, read strictly); `DatabaseWriter` as the
single writer for requests; no rule is written by any request; local code execution and the
holder of the master key are conceded.

## 13. What it does not solve

- **A copy of a governed node carries no rule.** Duplicate Entry, copy and paste, a template, a
  clone in KeePassXC, an import under new identifiers of a file without rule entries. Where the
  node's rule narrowed what its group grants, the copy is wider. A database whose rules grant low
  in the tree and leave the root with nothing is closed against this, since a copy lands where
  nothing is granted; a database that grants at the root and narrows below is not. The documents
  would have to tell the operator so, and the grant editor could on a narrowing rule.
- **A whole-file overwrite** puts a stale file in place, rules included [R android 5.5; R others].
  No store inside the file can tell.
- **A rule entry removed with no trace**: pykeepass deletes without a deletion record [R others];
  KeePassDX applies arriving deletion records without keeping them [R android 3.1]; Database
  Maintenance clears every record at once [R keepass 5]. Afterwards an older version of that node
  is the newest again, or the node inherits. Each needs a deletion of a rule entry first, which no
  program measured does on its own.
- **A stale copy on its own** applies the rules it has.
- **A hostile file merged in by the operator** under a method that matches identifiers can bring a
  rule entry that names a node and its newest version. Today such a file brings the node itself
  with its rule [A].
- **Refusals with nobody at fault**: a rule entry deleted in another copy and synchronized; a node
  that comes back after its rules were removed [M probe, 13, 14]. A refusal at a group closes what
  is under it.
- **The meet is narrower than it has to be**: a profile only one of two versions names gets
  nothing until the operator settles it.
- **Cost**: every request walks every entry's identifier once, and asks every entry for one
  string, where a single read today walks a handful of nodes.
- **Growth**: one small entry per rule change, kept while the node lives.
- **Nothing on the node in other programs**: a person editing on a phone cannot see that an entry
  is narrowed, and cannot find a node's rule without matching hex digits.
- **The Android merges are read, not measured**, for entries with these strings.
- **Data 1 to 6** are not this option's.

## 14. What a migration would take

Not designed. In outline, on the operator's press and never at open: for each node carrying the
old `KPG` custom data item, add a first rule entry and remove the item, in one save. From then on
node custom data is not read, so an unconverted copy merged in later brings back items that mean
nothing, and nodes that were ruled only there arrive with no rule and inherit; the operator would
have to be told before pressing. Whether a database made with 0.1.0 is carried over at all is a
question for the project (O7 in `ground-inventory.md`).

## 15. The probe behind the [M probe] marks

- Source:
  `spikes\storage\probes\rules-central\design\probe\Program.cs`,
  compiled with the .NET Framework's own `csc` against a copy of
  `.keepass\2.62\KeePass.exe`. No window. `keepassxc-cli` was the only other
  program started, from the archives the `keepassxc` ground report held to the spike's pinned
  hashes; each carried a valid signature from DroidMonkey Apps, LLC, checked again
  (`out\signatures.txt`).
- Outputs, beside it under `rules-central\out\`: `keepass.txt`, `cost.txt`, `xc-2.4.2.txt`,
  `xc-2.6.6.txt`, `xc-2.7.12.txt`, `xc-2.8.0.txt`. Databases under `rules-central\work\`.
- Each scenario starts from one base file (group U ruled, entry X ruled, entries Y and Z and group
  W with no rule, the store group holding the rule entries), makes copy A and copy B, merges in
  both directions, saves, reopens and applies the read rule of section 3, which the probe
  implements itself.
- KeePass: `PwDatabase.MergeIn` under four methods, 14 scenarios, 112 cells; Create new IDs with a
  copy of the base imported into itself, then `DeleteDuplicateEntries`; `DeleteDuplicateEntries` on
  a store of six rule entries; the cost table of section 4.
- KeePassXC: `merge -s`, 14 scenarios, both directions, four releases, 112 cells, and a plain save
  (`add`) in each. 2.4.2 and 2.6.6 gave identical output, 2.7.12 and 2.8.0 gave identical output,
  and the two pairs differ in one line, the KDBX version of section 10.
- The probe wrote an empty `KPG.After` for a first rule where the design has `none`.
- What the probe is not: the plugin's code, KeePass's window, KeePassXC's window, or any Android
  program.

## 16. Measurements to make before this design could be chosen

Most important first.

1. **The Android programs.** With the emulator runs `ground-android.md` section 7 lays out, over
   this report's base and scenarios: that a Keepass2Android and a KeePassDX merge (4.5.8 and
   3.2.0) add a rule entry only the arriving copy holds, into the store group, and what they do
   with a deletion record of a rule entry; that KeePassDroid 2.7, KeePassDX 3.2.0, 3.3.0 and 4.5.8
   and Keepass2Android (its KDBX 3.1 output included) keep the four strings through a save, a
   `KPG.Data` of 4482 characters among them.
2. **How often a narrowed node is copied.** The design's falsifier. Which rules in real use narrow
   below a grant, and which paths copy such nodes: Duplicate Entry, copy and paste, templates,
   KeePassXC's clone, imports. If narrowing below a grant is the common shape, the rule belongs
   on the node.
3. **The dialogs in a real KeePass 2.62** (the Live tier): a rule changed on the tab and nothing
   else, then OK, on the entry and the group dialog, marks the database modified and is kept;
   Cancel and Escape drop it; the tab's own question on Escape; Add Entry and Add Group, accepted
   and cancelled; several entries edited together; a revision viewed; that the tab's `FormClosed`
   handler still has its buffer after KeePass's own handler has taken the page away.
4. **The pass inside the gate**, under the hold and its limit, for 1,000, 10,000 and 50,000
   entries, and whether the string test that finds copies has to run on every request.
5. **A real File then Synchronize and File then Import in KeePass's window** with the plugin
   loaded: `503 undecided` from the first request after an import with new IDs, Remove copies,
   and that no read starts inside the merge.
6. **Three copies and long chains**: versions merged in every order, three newest versions, a
   node deleted, its rules removed, and the node brought back through a third copy.
7. **KeePassXC's window merge, its reload of a file changed on disk, and a KeeShare import**, the
   one path that applies deletions, over scenarios 07, 13 and 14; and that a share of a governed
   group carries no rule entry.
8. **The lower-depth programs** over the same scenarios: kdbxweb's merge, which removes whatever a
   deletion record names; Strongbox and KeePassKit merges; a KeePassium save.
9. **What each program shows**: the rule entries on each program's screens and in search, and
   whether a person is led to delete or tidy them; Delete Duplicate Entries and Delete Empty
   Groups through KeePass's window.
10. **The guard**, as host and end to end tests: every route against the store group, against a
    rule entry in a user's group and in the recycle bin, the reserved names, and `createEntry`
    drawing again.
