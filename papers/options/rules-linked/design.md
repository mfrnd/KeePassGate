# Option `rules-linked`: rules in entries of their own, with a reference in both directions

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. One design, the
strongest form of this option that I could build and measure. It covers datum 7, the access rules,
and section 10 has how the same pair of references serves other data about a node and how the
database's own data (`option-store-entries.md`) sit beside it.

`rules-linked` takes up the idea that when rules or meta data sit outside the node, the references
between these nodes should be bidirectional. Two designs already keep a rule outside its node with
a reference in one direction (`option-rules-central.md`, `option-rules-marker-entry.md`). This one
builds on what held in them and adds the reference back.

## How statements are marked

- **[M probe]**: measured for this report, today, from a console: KeePass 2.62's KeePassLib and
  `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 over invented databases. Section 15 describes the
  runs and names the files.
- **[M android, interim]**: read by me at 18:18 from the working files of the agent that is
  measuring the Android programs on an emulator (its result files for Keepass2Android 1.15-r6).
  `measured-android.md` did not exist when this was written, so these are its raw results, not its
  report, and only the three I name.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in the ground report
  of that key, or in `spikes/revocation-merge` as `ground-measured.md` digests it.
  **[M central]**, **[M marker]**: the probes of the two one-way designs. **[M crit]**: a probe of
  one of their four critiques, named where used.
- **[R 2.62]**: read by me in `KeePass-2.62-Source.zip` (the copy `ground-keepass.md` verified),
  file and line given. **[R xc]**: read by me in the KeePassXC clones the `keepassxc` ground report
  left, tag given. **[R droid]**: read by me in the Android clones the `android` ground report left.
  **[R plugin]**: read in the review4 worktree at `a42e38aa` by the design or critique named
  beside it; I did not read those lines again.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names tag, file and line.
- **[A]**: assumed. Neither run nor read. Every assumed statement about another program is on the
  list in section 16.

## 1. The design on one page

1. **A rule is an ordinary entry of the plugin's own, outside the node it governs**, written once
   and never changed. A change of the rule adds a new entry, a *version*, that names what it
   replaces. The rule text is today's document, byte for byte.
2. **Every ruled node has an anchor**: 16 random bytes, made once, when the node first gets a
   rule. Every version of that node's rule carries the anchor and the node's identifier. That is
   the reference from the rule to the node.
3. **The node carries the anchor back.** An entry carries it in a custom string of its own,
   `KPG.Anchor`. A group carries it in a *link entry* directly inside the group, a small entry of
   the plugin's whose own identifier is the anchor. Because the anchor never changes, the node is
   written once in its life for a rule, and never again when the rule changes.
4. **What each direction is trusted for.** A rule that names a node which names it back is the
   node's rule. A reference in one direction only **can take away and can never give**:
   - a rule that names a node which does not refer back (the reference was lost in a merge,
     deleted by hand, or the rule was planted) is applied as a *limit*: it caps what the node
     would otherwise get, and grants nothing;
   - a node that refers to a rule which names another node (the node is a copy) gets the
     original's rule as a limit;
   - a node that refers to a rule which is not there, or which is damaged, **refuses**, there and
     below, as a document that will not read refuses today.
5. **Every version has a second witness**, a small *record* in the store group, written with it.
   A record whose version is missing refuses the node. So losing the newest version does not
   bring an older one back.
6. **Nothing reads a modification time, a history item, custom data, a deletion record, a group's
   own fields or the recycle bin pointer.** Nothing is written when a database is opened or
   merged.
7. **Nothing the gate found wrong is settled by a dialog's OK alone.** Confirm, Adopt, Settle and
   Put back are presses on the KPG tab.
8. **The API sees none of it**: the plugin's entries are absent on every route, and `KPG.Anchor`
   on a user's entry is hidden and refused like every name beginning `KPG.`.

**What was measured for it** [M probe]: 12 scenarios merged in both directions under KeePassLib
2.62's four methods that match identifiers (96 cells) and by `keepassxc-cli` in four releases (96
cells, the four releases alike); 17 things a person can do by hand; KeePass's own ways of copying;
a foreign file that plants a rule, under four import methods; a lost newest version followed by a
stale copy. In no merge cell and after no single act by hand was the profile granted more than the
operator's last decision at that node, or more than the narrower of two decisions that never saw
each other.

**The honest weaknesses, first.**

- **A grant can be suspended with nobody at fault.** A first rule that *opens* an entry, made in
  one copy while another copy edits that entry later, loses its back reference in the merge, and
  the grant waits for a Confirm press: 6 of 8 KeePass cells and every KeePassXC cell where the
  other copy's edit was later [M probe, scenario 02]. A narrowing made the same way stays in
  force in every cell [M probe, 01, 03].
- **A copy inherits the original's narrowing and not its grant.** Today a copy carries the whole
  rule. Here a copied seal still seals, and a copied opening opens nothing until the operator
  adopts it.
- **A group's rule can still be planted from both sides** by a foreign file that the operator
  merges with a method that matches identifiers, because both of a group's references are entries
  and entries are what merges add [M probe, section 5.5]. Today the same file replaces the group
  whole. Section 13 has the change that closes it and what it costs.
- **Two targeted losses and a stale copy roll one rule back**: the newest version and its record
  both removed with no trace, then a merge that applies no deletions with a copy that still holds
  the older version [M probe, section 5.6].
- **It is the most visible of the rule designs**: a field on every ruled entry and an entry in
  every ruled group, in every program, beside the plugin's two groups.

## 2. The pieces

### 2.1 The anchor

| Property | Value | Why |
| --- | --- | --- |
| What | 16 random bytes, written as 32 hex digits | names "this node, as the plugin knows it", apart from the node's own identifier |
| Made | once, with the node's first rule. For an entry KeePass draws it; for a group it is the identifier KeePass draws for the link entry | a copy of the node gets a new identifier and keeps the anchor, which is exactly how a copy is told from its original |
| Changes | never, while the node keeps its rule, whatever the rule becomes. A rule taken away is a version holding `{"default":"inherit"}`; the anchor stays | so the node is not written again, and no merge of the node can roll a reference back to an older value |
| Served | by no route, in no listing, tag or count | a file that has not read this database cannot attach a version to an existing anchor |

**Considered and rejected: a back reference that names the version.** It would let the node itself
witness which version is the newest. It has to be rewritten at every rule change, which is a write
to the user's entry each time (a revision, a new time, the dialog's copy), and it does not hold:
one copy changes the rule, another copy edits the entry later, every merge takes the later entry
whole [M keepass 1a; M keepassxc 4], and the reference is the old one again. The record of 2.5
does that job without touching the node.

### 2.2 The back reference on an entry

A custom string on the user's entry: `KPG.Anchor`, not protected, holding the anchor.

```text
Entry "Router admin"            (the user's own, in any group)
  Title       Router admin
  Password    ********
  KPG.Anchor  5B81CC0361F024B85BEE69608DCB16ED
```

**Why a custom string.** It is the one carrier on an entry that every program keeps:

| Program | A save | An edit of another field of that entry | A copy of the entry | Basis |
| --- | --- | --- | --- | --- |
| KeePass 2.62 | kept | kept: the entry dialog copies the strings and writes them back | `PwEntry.Duplicate` kept it under a new identifier; the clipboard path (`KdbxFile.WriteEntries`, `ReadEntries`) kept it under a new identifier | [M keepass 2.1]; [M probe]; [R 2.62 `KeePassLib/PwEntry.cs:321-356`, `:856-861`; `KeePass/Util/EntryUtil.cs:258-279`; `KeePassLib/Serialization/KdbxFile.Read.cs:519-560`] |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | kept, the entry otherwise unchanged but for the three empty standard fields KeePassXC adds to an entry that lacks them | `edit -u`: kept, and the revision it made carries it too | Clone copies the attributes in all four; 2.7.12 and 2.8.0 can also clone a whole group with its entries (no such command found in 2.4.2 or 2.6.6) | [M probe, four releases alike]; [R xc `src/core/Entry.cpp:1036-1096` at 2.8.0, `:721-732` at 2.4.2, `:760-771` at 2.6.6; `src/core/Group.cpp:927-971` and `src/gui/DatabaseWidget.cpp:1090-1101` at 2.8.0]; the clone itself not run |
| Keepass2Android 1.15-r6 | every custom string of every entry kept | the password of one entry and the titles of two others changed on the phone: every custom string kept, protected ones and a 2861 character one among them, and the revision made carries them | "copy entry" is `CloneDeep` and a new identifier | [M android, interim: `k2a save-aes`, `edit-e1`, `edit-carriers`]; [R droid `src/Kp2aBusinessLogic/database/edit/CopyEntry.cs:39-47`]; the copy not run |
| KeePassDX 4.5.8, 3.2.0 | custom strings read and written back | [A] | 4.5.8 copies an entry, and a group with every entry in it, under new identifiers | [R android 2]; [R droid `database/.../element/Database.kt:1113-1149` at 4.5.8]; that the copy carries custom strings is [A] |
| KeePassDroid 2.7 | custom strings read and written back | [A] | not read | [R android 2] |
| kdbxweb, pykeepass | kept | | | [M others] |

**What a merge does to it.** Every merge measured takes an entry whole. So a back reference added
in one copy while the other copy edited the entry later is not on the current entry afterwards:
measured in 6 of 8 KeePass cells and in every KeePassXC cell where the other edit was later
[M probe, 01, 02], and decided by the direction at a tie [M probe, 03]. This happens once in a
node's life, at its first rule. Section 3.3 has what the gate does then.

**Considered and rejected: taking the reference from the entry's history.** After that merge the
losing version, with the reference, is in the entry's history [M keepass 1a; M keepassxc 4;
M android, interim, for the revision an edit makes]. Reading it there would restore most first
rules with no press. It cannot be trusted: a merged file can put an *older* version of an entry
into the open entry's history under Synchronize and under Keep existing [M keepass 1a, "the
arriving version still lands in its history"], so a foreign file could supply both a rule and the
history item that confirms it, knowing nothing but the entry's identifier. History is shown to the
operator on the tab and decides nothing.

### 2.3 The back reference of a group: a link entry

A group has no field that survives: its notes, tags and custom data are taken whole by whichever
copy's group is newer, other programs move that time for their own reasons, KeePassDroid and
KeePassDX 3.2 and 3.3 delete its custom data, and it has no history [M spike; M keepass 3;
R android 1.4]. So the group's reference is the one thing every program keeps and every merge
adds: an entry.

```text
Group  Finance
  Entry  KeePassGate link         UUID 95CE46ADAABF7D9FB78DCB29C218918A   (the anchor)
           KPG.Kind    link
           KPG.Anchor  95CE46ADAABF7D9FB78DCB29C218918A
           User name, password, URL empty; auto-type off; icon 29
           Notes       Links this group to its KeePassGate access rule. Managed on the group's KPG tab in KeePass.
  Entry  payroll                  (the user's own)
```

- **Written once, never changed**, so it never has two versions and no merge decides anything
  about it by a time. A group given its first rule in one copy while the other copy added an entry
  to it and moved the group's time later ended with the link in place and the rule in force in all
  8 KeePass cells and all 8 KeePassXC cells [M probe, 07].
- **It holds no rule.** The rule is in the plugin's group. So the entry in the user's group never
  changes when the rule does, and an edit or deletion of it by hand cannot change a rule's text.
- **Its own identifier is the anchor, and it also carries the anchor as a string.** A copy of the
  group copies the link under a new identifier with the string unchanged
  [M probe: `PwGroup.Duplicate`; R 2.62 `KeePassLib/PwGroup.cs:1437-1447`, `:1024-1043`], so a link
  whose identifier is not its anchor is a copy. A link whose identifier is its anchor and that sits
  in a group its rule does not name was *moved*, and is ignored where it landed. That difference
  keeps one stray entry from limiting the group it lands in: Keepass2Android puts an arriving entry
  at the root when its group is missing [R android 1.5], and the critics of two earlier designs
  measured a rule entry out of place closing the root.
- **The root gets a link like any group**, directly under the root, when the root gets a rule. No
  link is written for a root without a rule.

**The other answer for groups, argued and not taken: a reference in one direction only.** It would
keep the plugin out of the user's groups. It loses the two things the reference back is for, on
the node where they matter most. A duplicated sealed group would hold every entry of the original
and no seal, which the critic of the central design judged unfixable there; with the link, the
copy of a sealed group stayed sealed and the copy of an opened group opened nothing, in all 16
cells [M probe, 09]. And a rule planted for a group that has none would be in force, where with
the link it only limits [M probe, section 5.5].

### 2.4 A rule version

One entry per version, in the plugin's rules group, written once.

| Part | Content |
| --- | --- |
| UUID | the first 16 bytes of SHA-256 over `KPG/1/rule/` and the six strings below, each on a line: the *version id* |
| `Title` | `KPG rule ` and the first 8 hex digits of the anchor. Fixed words and hex only |
| `UserName`, `Password`, `URL` | present and empty, for the reasons `option-store-entries.md` section 3 gives |
| `Notes` | `KeePassGate data. Managed on the KPG tab in KeePass.` |
| `KPG.Kind` | `rule` |
| `KPG.Anchor` | the node's anchor |
| `KPG.Node` | `entry/` or `group/` and the node's identifier |
| `KPG.After` | the ids of every version of this anchor the writer had read, one space between them; `none` for a first version |
| `KPG.Lost` | the ids of records this version answers (section 4.3, Settle); `none` otherwise |
| `KPG.Data` | the rule document exactly as `AclDocument.Write` produces it today |
| `KPG.Nonce` | 16 fresh random bytes as hex |

- **The identifier is derived from the content, and the content holds a nonce.** A version edited
  by hand, and a copy of one under a new identifier, is not the entry the plugin wrote, which the
  read path sees (3.2). The nonce is the fix both survival critics asked for: without it a rule set
  back to an earlier text got that earlier version's identifier again and arrived dead
  [M crit, `rules-marker-entry-survival` P1; `rules-on-node-survival` 2.4]. With it, the same
  decision made in two copies is two versions, which the fold meets to the same document.
- **The version the plugin replaces is deleted in the same write**, for good, with the deletion
  record KeePass's own delete leaves. So the rules group shows one entry per ruled node, not ten
  that look alike, which is what invited the deletions the central design's critics describe. A
  replaced version that a merge brings back is dead by `KPG.After`, in the newer version and in its
  record. No other pruning exists: the versions of a node that is gone stay, inert, and there is no
  Remove.

### 2.5 The record: a second witness for every version

One small entry per version, in the store group of `option-store-entries.md`, written in the same
turn and never changed or removed:

| Part | Content |
| --- | --- |
| UUID | the first 16 bytes of SHA-256 over `KPG/1/issued/` and the version id |
| `Title` | `KPG rule record `, 8 hex digits of the anchor, 8 of the version id (so no two look alike) |
| `KPG.Kind` | `issued` |
| `KPG.Version`, `KPG.Anchor`, `KPG.Node`, `KPG.After` | the version's own, without its text |

It is a refusing record in that design's sense: it counts wherever its entry is found, by what it
names. It records two facts. *This version was written*: if it is not replaced and its entry is
nowhere, the node refuses. *These versions are replaced*: so a returning older version stays dead
when the newer one's entry is gone. Measured: the newest version removed with no trace refused,
and still refused after a stale copy holding the older version was merged in by KeePassLib and by
four KeePassXC releases [M probe, section 5.6].

Why the rule entry and its record sit in two groups: one deletion of a group, which KeePass
records entry by entry and a KeePassDX merge then applies without keeping the records
[M crit, `rules-central-survival` P1, C4], takes one kind and leaves the other.

### 2.6 The two groups

- **The store group** is `option-store-entries.md`'s (`FE4040A9…`, searching and auto-type off).
  It gains the kind `issued`.
- **The rules group**, `KeePassGate rules`, holds the versions: searching and auto-type off, the
  same line of notes, no subgroups, beside the store group and not inside it. A group of its own,
  so that an operator looking at rules is not sent into the group that holds the authority's
  private key (the central design's critic, M8).
- **No decision reads either group.** Rule entries, records and links are read wherever they are:
  in the recycle bin, at the root, in a user's group. So which group is the bin, which merges by a
  time [M crit, `rules-marker-entry-survival` P6], decides nothing. A version moved to the root
  and its record moved into a user's group were still in force [M probe, h15].

## 3. Reading

### 3.1 One pass per request

Inside the hold a request already runs in, one walk over every entry of the tree collects: the
versions (`KPG.Kind` `rule`), the records (`issued`), the links (`link`, by the group they sit in)
and nothing else. A user's entry is asked for `KPG.Anchor` when a walk meets it. Deletion records
are not read.

### 3.2 The rule of an anchor

For an anchor A, from the versions and records that carry it:

1. A version *reads* when its strings are well formed. Its id is what its content derives. It is
   *intact* when its entry's identifier is that id, and *displaced* otherwise (edited by hand, or
   a copy under a new identifier).
2. *Dead* ids: every id named in `KPG.After` of a version or record of A.
3. *Live* versions: those that read and are not dead.
4. A is **broken** when any of these holds:
   - a record of A names a version that is not dead, not answered by `KPG.Lost` of an intact
     version, and not present (**a version was lost**);
   - no live version is left;
   - a live version has no intact entry (**changed outside the plugin**);
   - the versions and records of A name more than one node, or a live document will not read.
5. Otherwise A's document is the live version's, and for several live versions the **narrower of
   them**, as both one-way designs define it and the security critics checked against the
   resolver: `deny` when any denies; a profile all name keeps the words all give it; a profile
   only some name keeps nothing. Measured: one copy set `agent` to list, the other to read; every
   cell gave nothing [M probe, 05].
6. A's *home* is the node its versions and records name.

### 3.3 The states of the pair

For a node N: *back(N)* is the anchors N carries (an entry's `KPG.Anchor`; the links directly in a
group), and *forward(N)* the anchors whose home is N.

| # | State | What the gate answers | Why it can never be wider than the operator meant | Measured |
| --- | --- | --- | --- | --- |
| 1 | **Both agree**: N carries A and A's home is N | A's document is N's rule, nearest wins per profile, as today | it is the rule the operator set, or the narrower of two the operator set in two copies | [M probe, 04, 05, 07] |
| 2 | **N refers to a rule that is not there**: N carries A and nothing in the file carries A | N **refuses**, there and below | the operator set some rule here and the gate cannot know which. Inheriting would be wider than any narrowing it held. This is the node itself witnessing a lost rule | [M probe, h02, h09, h12] |
| 3 | **N refers to a rule that is broken** (3.2 point 4) | N **refuses**, there and below | the same; a text changed outside the plugin is not the operator's decision | [M probe, 10, h07, h08, h11] |
| 4 | **A rule names N and N does not refer back** (lost in a whole-entry merge, deleted by hand, restored from an old revision, a moved or deleted link, or the rule was planted) | A's document is a **limit** at N (3.4): it takes away what it would take away and gives nothing | if the rule is the operator's, the answer is at most that rule. If it was planted, the answer is at most what N had without it. The limit is at most both | [M probe, 01, 02, 03, 11, 12, h04, h13, h14, h17, plant] |
| 5 | **N refers to a rule whose home is another node**: N is a copy | the original's document is a **limit** at N. If the original's rule is broken, N refuses | the operator decided nothing about the copy. It gets at most what the original's rule allows and at most what its place allows | [M probe, 09, h05, h06, h16, Create new IDs] |
| 6 | **Two nodes carry the same anchor** | the home node is state 1; every other is state 5. When the home node is gone, all are state 5 | as 5. Delete Duplicate Entries deleted the original and kept the copy; the copy stayed narrowed | [M probe, 09, h16] |
| 7 | **The reference edited by hand** | text that is no anchor: refuses. A well formed anchor nothing carries: state 2. Another node's anchor: state 5 for that rule, and state 4 for N's own. Deleted: state 4 | every case is a refusal or a limit. A reference to a *granting* rule, typed onto an entry outside every grant, gave nothing | [M probe, h01, h02, h03, h04, h05, h06] |
| 8 | **N carries two anchors that both name it** (a group given a first rule in each of two copies) | the narrower of the two documents | as two versions that never saw each other | [M probe, 08] |
| 9 | **A group holds a link that was moved in** (its identifier is its anchor, its rule names another group) | ignored there and shown; its own group is state 4 | nothing is given anywhere; the group it left keeps its narrowings | [M probe, 11] |
| 10 | **A record names N and its version is missing**, whether or not N refers back | N **refuses** | a record can only refuse | [M probe, h08] |
| 11 | **No reference either way** | silence: N inherits, as today | nothing was ever decided here, or three things were removed in three places (section 12) | [M probe, h10] |

Two rows deserve their argument in full.

**Row 4, why a limit and not a refusal.** A refusal would be as safe. It would also close a node
after the most ordinary sequence there is: a first rule in one copy, an edit of the same entry in
another, a merge. With a limit, a seal or a narrowing made that way stays fully in force with no
press, because taking away is all it does; only an opening waits. In the two cells of scenario 02
where the reference survived, the grant was in force; in the six where it did not, the entry was
closed to the profile, never open to more [M probe].

**Row 5, whether the original's rule governs the copy.** It does, as a limit. The two other
answers are worse. "The copy has no rule" is the central design's weakness: a copy of a sealed
group is open. "The copy refuses until the operator has looked" closes every entry made from a
template and every duplicated group, for every profile. With a limit, the copy of a sealed or
narrowed node is sealed or narrowed at once, in every program that copies custom strings and
entries, and the copy of an opened node is open to nothing more than its new place allows.

### 3.4 Limits: the one thing the resolver learns

Today a node hands the resolver one document or none (`IAclNode.Document`). Here a node hands it a
document or none, a list of limits, or a refusal. The walk for one profile, from the node up:

- at each node, a refusal ends the walk in a refusal, as an unreadable document does today
  (`Acl/AclResolver.cs:484-491` [R plugin, as the central design cites it]);
- each limit met caps the answer: a limit that names the profile intersects the cap with its
  words; a limit that does not name it and has `default` `deny` ends the walk with nothing;
- the first confirmed document that names the profile answers with its words, cut to the cap; one
  that does not name it and denies ends the walk with nothing;
- at the top, nothing.

A cap only ever removes words, so an answer with limits is at most the answer without them. And a
limit applied where the full rule would have applied gives at most what the full rule gives: where
the rule names the profile, the limit is the rule's words cut by what arrives from above; where the
rule denies, both give nothing; where the rule is silent, both let the walk go on. `AclDocument`,
nearest wins, the empty array, `default`, the verbs and switches do not change.

### 3.5 Cost

Not measured for this design. The pass is the one both one-way designs make: 1.4 to 3.2 ms for
about 10,000 entries in a console process, nearly all of it the test of each entry for one string
[M central; M marker]. This design asks a user's entry for a second string only when a walk meets
it. A full listing therefore pays one more string lookup per entry [A: about the cost of the
first]. If `option-store-entries.md` is taken, its pass and this one are the same walk.

## 4. Writing

Rules are written where they are today: on KeePass's thread, by the grant editor, at a dialog's
OK. No request writes a rule, a record, a link or a back reference.

### 4.1 The entry dialog

KeePass's entry dialog copies the entry's strings when it opens and writes that copy back on OK,
and the copy is public: `PwEntryForm.EntryStrings`, `UpdateEntryStrings`, and the events
`EntrySaving` (which can cancel) and `EntrySaved` [R 2.62 `KeePass/Forms/PwEntryForm.cs:117-118`,
`:141`, `:231`, `:358`, `:1074-1077`, `:1148-1149`].

- **A first rule.** When the operator sets one on the tab, the plugin draws the anchor and writes
  `KPG.Anchor` into `EntryStrings` at once. KeePass's own "was anything changed" answer then
  includes it, so its question on Escape covers a first rule without the plugin asking.
- **OK.** KeePass saves the entry with the reference in it (one revision, the time moves, once in
  the node's life). The plugin then adds the version and its record. For Add Entry they are added
  before KeePass puts the entry into its group, which the security critic of the marker design
  showed is the order that keeps KeePass's save after the dialog from writing a ruled entry
  without its rule. A version whose node is not in the tree yet is ignored.
- **A later rule change** touches no string of the entry. The tab keeps the change in a buffer of
  its own and applies it when the dialog closes with OK, as `AuthorityPanelFactory` applies the
  settings tab (`Ui/AuthorityPanelFactory.cs:198-271` [R plugin, as the central design cites
  it]): one new version, its record,
  the replaced version deleted. The plugin marks the database modified, since KeePass does not
  for an unchanged entry (`MainForm_Functions.cs:5800-5812` [R 2.62, as the central design cites
  it]), and saves when KeePass's "save after modifying an entry" option is on.
- **While KeePass is busy** (a save is pumping messages), `EntrySaving` cancels the OK with one
  line that names the reason. The rule write then never lands inside a serialization.
- **Cancel** discards KeePass's copy of the strings and the tab's buffer. Nothing was written.
- **Several entries edited together and a revision opened for viewing** stay read only on the
  tab, as today. KeePass's own Advanced list can still add or remove `KPG.Anchor` on all of them;
  every outcome is a row of 3.3.
- **Restoring a revision** no longer brings an old rule back. A revision from before the first
  rule has no reference, which is state 4: the rule stayed in force as a limit [M probe, h17].

### 4.2 The group dialog

The group dialog holds no copy of the group's entries, so the tab keeps its own buffer and applies
it at `FormClosed` with OK: for a first rule, the link entry inside the group, the first version
and its record; later, a version and a record only. Add Group puts its group into the tree before
the dialog opens and takes it out on Cancel, so a link is added only at OK. Adding an entry moves
no time on the group (`KeePassLib/PwGroup.cs:1342-1354` [R 2.62, as the marker design cites it]).

### 4.3 Four presses, and nothing without them

Beside local, inherited and sealed off, the tab shows the states of 3.3 in its warning band, each
with one press that is applied at the dialog's OK like any edit. **An OK that did not follow the
press writes nothing for them.** Both critics of both one-way designs measured what the other
choice does: an OK pressed to change a password sealed whatever the node carried.

| The tab shows | The press | What it writes |
| --- | --- | --- |
| "This rule is not confirmed by the entry (or group). It only takes away." with the rule, and whether the entry's history holds the reference | **Confirm** | entry: `KPG.Anchor` back into `EntryStrings`. Group: the link put back if it is found elsewhere; otherwise a new link under a new anchor and a version that replaces the old anchor's versions |
| "A copy of (path). The original's rule only takes away here." | **Adopt**, or **Remove the reference** (with the sentence that the node will then inherit) | Adopt: a new anchor for this node, a first version holding the rule shown, the reference rewritten |
| "Refusing: a rule version is missing" or "changed outside KeePassGate" or "the rule is not in this database" | **Settle** | a version under the node's anchor. The table starts from a rule that grants nothing, never from an older version. `KPG.After` names only versions the plugin read (a changed entry among them, so it is dead wherever it comes back); `KPG.Lost` names the records it answers. A copy of the database that still holds the lost version then has two live versions and applies the narrower |
| "Two rules, changed in two copies. The narrower applies." with both | **Settle** | one version replacing both |
| "A link from (path) was moved here." | **Put back** | the link moved to its group |

### 4.4 What the plugin writes unasked, and what it never does

Unasked, only inside its own rule write: the deletion of the version that write replaces. It never
repairs at open, after a merge or on somebody else's save; never moves a link or adds a reference
on the evidence of the other direction alone (that is exactly what a planted rule wants); never
removes a record; never removes the versions of a node that is gone.

### 4.5 What changes in the plugin

| Class | Change |
| --- | --- |
| `AclDocument` | `CustomDataKey` goes; format, reader, ceilings stay; gains the narrower of several |
| `AclResolver`, `IAclNode` | a node answers a document or none, limits, or a refusal (3.4). Nearest wins and everything else unchanged |
| new `Acl/RuleVersion`, `Acl/RuleRecord` | the strings read strictly, the derivation |
| new `Bridge/RuleIndex` | the pass and the fold of 3.2 and 3.3, once per hold |
| new `Bridge/RuleStore` | the only writer: first rule, change, the four presses |
| `KdbxNodes` | asks the index; reads `KPG.Anchor` of the entry at hand |
| `NodeDialog`, `IGrantStore`, `GrantPanelFactory` | `m_sdCustomData` and its reflection go; `EntryStrings`, `EntrySaving`, `EntrySaved` for entries; a buffer applied at `FormClosed` otherwise |
| `GrantPanel`, `GrantSheet`, `GrantTab` | the states and presses of 4.3 |
| `KdbxVault`, `VaultHandlers` | the guard of section 9 |
| `IRuledNodes`, `DatabaseNodes`, the Profiles page | count through the index; three more counts (refusing, not confirmed, copies) |
| `GroupDialogs`, `DatabaseWriter` | unchanged for rules; the writer's busy test is what `EntrySaving` asks |
| Tests | `SnapshotHazardTests` and the rule cells of `MergeInRealDatabaseTests` give way to pins, against the pinned KeePass, of what section 5 rests on |

## 5. What was measured

All [M probe]. "agent" is the one profile read. The base: group U opened to agent (read, list)
under a root that grants nothing; entry X in U narrowed to nothing; entries Y and Z in U with no
rule; group S sealed, holding group C with entry Q; group W with entry V and no rule.

### 5.1 KeePass's own copying

| Path | What it does | Basis |
| --- | --- | --- |
| Duplicate Entry; a copying drag of entries; Add Entry from a template | `PwEntry.Duplicate`: a deep clone, strings included, under a new identifier. The Duplicate dialog then changes only the title, the user name and password (to references, when asked) and the history. **`KPG.Anchor` kept** | [M probe]; [R 2.62 `KeePassLib/PwEntry.cs:321-356`, `:856-861`; `KeePass/Forms/MainForm.cs:933-963`; `Forms/DuplicationForm.cs:94-120`; `Forms/MainForm_Functions.cs:6478-6488`; `Util/EntryTemplates.cs:265-266`] |
| Duplicate Group; a copying drag of a group | `PwGroup.Duplicate`: a deep clone with new identifiers for the group and everything in it. **The link entry copied, its identifier new, its `KPG.Anchor` still the original's** | [M probe]; [R 2.62 `KeePassLib/PwGroup.cs:358-396`, `:1437-1447`; `Forms/MainForm.cs:2313-2342`, `:1318`] |
| Copy and paste of entries | written and read back as KDBX, with new identifiers. **`KPG.Anchor` kept** | [M probe, `KdbxFile.WriteEntries` then `ReadEntries`]; [R 2.62 `Util/EntryUtil.cs:258-279`] |
| File then Import with Create new IDs, a copy of the database into itself | every node again under a new identifier, back references and links with them; the originals and their rules untouched | [M probe]: the copy of X limited, the copy of S sealed, the copy of U opened to nothing |

That these are the calls KeePass's commands make is read, not driven: no window was shown.

### 5.2 Merges: KeePassLib 2.62, four methods, both directions (96 cells)

| # | Scenario | Result |
| --- | --- | --- |
| 01 | A gives Y a first rule, a narrowing; B changes Y's password later | Y's reference is gone in 6 cells (every cell where B's version wins) and kept in 2. **agent: nothing in all 8** (a limit in 6, the rule in 2) |
| 02 | A gives V a first rule, a grant (read); B changes V's password later | reference gone in 6, kept in 2. agent: nothing in the 6 (**the grant waits for Confirm**), read in the 2 |
| 03 | as 01, in the same second | gone in 4, kept in 4, by direction. agent: nothing in all 8 |
| 04 | X ruled in the base; A changes the rule to list; B changes X's password later | **the new rule in all 8**, the reference untouched: a rule change never writes the entry |
| 05 | A changes X's rule to list, B to read | both versions arrive; the narrower (nothing) in all 8 |
| 06 | A and B each give Y a first rule: two anchors | one confirmed, the other a limit; nothing in all 8 |
| 07 | A gives group W a first rule (a grant); B adds an entry to W and moves W's time later | link and rule arrive; the grant in force in all 8, for B's new entry too |
| 08 | A and B each give group W a first rule | two links, two anchors, both confirmed; the narrower (read) in all 8 |
| 09 | A duplicates entry X, group S and group U | in all 8: the copy of X limited to nothing; the copy of S sealed; the copy of U opens nothing; the originals unchanged |
| 10 | A deletes X's rule entry for good by hand | X **refuses** in 5 cells (wherever the deletion applies); in the 3 where B never learns of it, B keeps the rule |
| 11 | A moves U's link entry into W | in the 5 cells that carry the move: U's grant suspended, U's entries get nothing, W unaffected ("a moved link, ignored"); in the other 3 unchanged |
| 12 | A deletes X's `KPG.Anchor` by hand | gone in 6, kept in 2; agent: nothing in all 8 |

### 5.3 Merges: keepassxc-cli 2.4.2, 2.6.6, 2.7.12, 2.8.0 (96 cells, the four releases alike)

The same scenarios, `merge -s`, both directions. Results as 5.2 with KeePassXC's own rules: the
later entry whole in both directions (01, 02, 12: the reference gone in both); the target at a tie
(03: kept into A, gone into B); no deletion applied, so the rule entry deleted by hand in 10 came
back into the deleting copy and the rule was in force again in both. No cell wider. A plain save
(`add`) left every plugin entry and X's `KPG.Anchor` unchanged; `edit -u` on X kept the reference
and made a revision that carries it.

### 5.4 By hand, in one file

| # | Act | agent at the node |
| --- | --- | --- |
| h01 | X's reference changed to text that is no anchor | refuses |
| h02 | changed to a well formed anchor nothing carries | refuses |
| h03 | changed to U's anchor | nothing (limited by both rules) |
| h04 | deleted | nothing (X's own rule as a limit) |
| h05 | Y, unruled, given X's anchor | nothing (limited by X's rule) |
| h06 | V, outside every grant, given U's anchor (a grant's) | nothing: a reference to a grant gives nothing |
| h07 | X's rule entry edited: `KPG.Data` widened | refuses |
| h08 | X's rule entry removed with no trace, its record kept | refuses |
| h09 | rule entry and record both removed with no trace | refuses: the reference on X has nothing to name |
| h10 | as h09, and X's reference removed too | **read, list: X inherits.** Three acts in three places |
| h11 | the rules group removed with no trace | U, S and X refuse, with everything under U and S |
| h12 | the rules group and the store group removed with no trace | the same: every reference has nothing to name |
| h13 | U's link deleted for good | U's grant suspended: nothing under U |
| h14 | S's link deleted for good | S still sealed (the seal as a limit) |
| h15 | X's rule entry moved to the root, its record into U | unchanged: place means nothing |
| h16 | X copied by a drag, then KeePass's Delete Duplicate Entries | it deleted the original and kept the copy; the copy is still limited to nothing |
| h17 | X restored to its revision from before its first rule | nothing (the rule as a limit) |

### 5.5 A foreign file that plants a rule

A database under no relation to the victim but public identifiers (the two plugin groups' fixed
identifiers, group C's and entry V's), holding intact rule versions and records that grant agent
read, list and protected at C (which sits under the seal and has no rule) and at V:

| The foreign file also holds | Synchronize, Keep existing, Overwrite existing | Create new IDs |
| --- | --- | --- |
| nothing else | C and V: not confirmed, **nothing granted** | the same |
| a link entry inside a group that carries C's identifier | **the planted rule is in force at C: Q is read, protected values included.** V still nothing | nothing granted; the arriving group refuses |

So the reference back closes the plant for an entry and for a rule alone, under every method. It
does not close it for a group when the file brings both references under a method that matches
identifiers. For an entry the second reference is a string on the entry, and bringing that means
bringing the entry whole and newer, which replaces its content and exposes nothing of the
operator's; that case was not run [A].

### 5.6 The newest version lost, then a stale copy

The stale copy holds X opened wide; the current copy narrowed X to nothing.

| Lost in the current copy, with no trace | Before any merge | After the stale copy is merged in |
| --- | --- | --- |
| the newest version's entry | refuses | refuses, by KeePassLib Synchronize and by four KeePassXC releases: the older version came back with KeePassXC and stayed dead |
| the newest version's entry and its record | refuses (the older version's record now asks for the older version, which the plugin had deleted) | KeePassLib Synchronize: refuses, since the current copy's deletion record removes the returning version again. **KeePassXC, four releases: the older, wider rule is in force**, with nothing shown |

## 6. Program by program

| Program | The back reference on an entry | Links, versions, records | A merge | What the gate then does |
| --- | --- | --- | --- | --- |
| KeePass 2.62 | kept by save, edit and every copy [M probe; M keepass] | entries: kept, added by every method, KDBX 4.0 [M probe; M keepass X1, X7] | the later entry whole; entries only one copy holds arrive; a deletion travels by Synchronize only [M probe; M keepass] | section 5.2: the rule, the narrower, a limit or a refusal |
| KeePassXC 2.4.2 to 2.8.0 | kept by save and edit [M probe]; Clone copies it [R xc] | kept unchanged by a save; added by every merge [M probe] | the later entry whole, the target at a tie; no deletion applied or copied [M probe; M keepassxc] | section 5.3; and the one rollback of 5.6 |
| Keepass2Android 1.15-r6 | kept by a save and by an edit of another field, in the revision too [M android, interim]; its copy is a deep clone [R droid] | custom strings of every entry kept by a save [M android, interim] | the later entry whole, the arriving copy at a tie; an arriving entry added to its group, or to the root when the group is missing; deletion records joined [R android 1.1, 1.5, 3.1]. Not measured for this shape | by the reads: as KeePass's Synchronize. A link that lands at the root is a moved link and is ignored there |
| KeePassDX 4.5.8 | custom strings read and written back [R android 2]; copies entries and whole groups [R droid] | the same [R android 2] | the later entry whole, the open copy at a tie; arriving entries added, missing groups recreated; deletion records applied and not kept; an unchanged entry moved to the arriving copy's group [R android 1.1, 1.5, 3.1, 5.4]. Not measured | by the reads: the union. A deletion it applied without a record leaves the node refusing by its record, or by its own reference. A link it moves is state 9 or back in place |
| KeePassDX 3.2.0, 3.3.0 | as 4.5.8 [R android 2 for 3.2.0; 3.3.0 not read] | as 4.5.8. They delete group custom data [M spike], which holds nothing of ours | 3.2.0 drops an arriving entry whose group the open copy lacks [R android 1.5]. Not measured | a dropped version beside an arrived record, or under a reference, refuses |
| KeePassDroid 2.7 | custom strings read and written back [R android 2]. It deletes every node's custom data [M spike], which holds nothing of ours | kept every entry it was given [M spike] | has no merge [M spike] | unchanged |
| kdbxweb, pykeepass | kept [M others] | kept [M others] | kdbxweb: union, and it removes whatever a deletion record names [R others]. pykeepass deletes with no record [R others] | a removed version refuses by its record or the node's reference |
| A whole file written over another | | | nothing merges [R android 5.5; R others] | not solved, section 12 |

**Against the survival requirement** (a rule must not be lost because the database was edited in
KeePass, KeePassXC, Keepass2Android or KeePassDroid). Editing an entry, adding one, saving, and
merging in KeePass and KeePassXC lost no rule and widened none in any cell measured here: a rule's
text is in an entry no other program has a reason to change, and the one value on the node never
changes. For Keepass2Android, that a save and an edit of another field keep an entry's custom
strings is measured, in the other agent's working files; its merge is read. For KeePassDroid
everything is read: it writes custom strings back and has no merge. What an edit elsewhere can
cost is a first grant that waits for Confirm, never a rule. So the option passes for KeePass and
KeePassXC as measured, and passes for the two phone programs as far as reading and one interim
measurement go; item 1 of section 16 is what turns that into a measurement.

## 7. What it buys over each one-way design

### Over `rules-central` (rules in the plugin's group, naming the node)

| Problem its critics found | Here | Basis |
| --- | --- | --- |
| A copy of a governed node carries no rule; a duplicated sealed group is open (security S4, "the residual is the project's call") | the copy carries the reference; the original's rule limits it. Sealed stays sealed in KeePass's Duplicate Entry, Duplicate Group, paste and Create new IDs | [M probe 5.1, 09]; for KeePassXC's Clone, Keepass2Android's and KeePassDX's copies [R], not run |
| Delete Duplicate Entries keeps the copy and deletes the governed original (survival P6) | the surviving copy is still limited | [M probe, h16] |
| A file the operator merges can rule any node whose identifier it knows (security S1) | a rule alone only limits, under every method. Not closed for a group when the file brings the link too | [M probe 5.5] |
| Create new IDs, then Remove copies, promotes a foreign copy (S1 path A) | no such step exists. A copy of a rule entry is the same version seen twice when the original is there, and never a grant when it is not | by 3.2; [M probe, Create new IDs] |
| Copies of rule entries stop the whole gate with `503 undecided` (security M3) | nothing stops; no second meaning for that word | [M probe, Create new IDs] |
| A lost newest version leaves no trace where KeePassDX consumed the deletion record, or pykeepass deleted (survival P1: C1, C2, C4) | the record refuses; with the record gone too, the node's own reference refuses; a deleted rules group or both groups refuse everywhere they were used | [M probe, h08, h09, h11, h12, 5.6]; the KeePassDX step itself is read |
| The plugin's own Remove deletes rule entries (P1, C1) | there is no Remove | by design |
| The repair writes the older rule on any OK and carries it to copies that held the narrowing (security S2, survival P2) | Settle is a press, starts from a rule that grants nothing, and never names a version the plugin did not read | by 4.3; not run |
| Ten look-alike entries invite a tidy-up that silently reverts (security S3) | one rule entry per node; a deleted one refuses rather than reverting | [M probe, 10, h08] |
| A hand-changed entry the repair deleted comes back through KeePassXC and refuses again (survival P3) | the settling version names it in `KPG.After`; it is dead wherever it returns | by 3.2; not run |
| KeePassDX 3.2.0 keeps rules out of a copy whose store group was deleted; nodes ruled since then inherit (P4) | such a node carries its reference and refuses | by 3.3, row 2; not run |
| Section 3's "a stale copy can only lack versions" was false (P7) | still false in one chain, stated in 5.6 | [M probe 5.6] |

### Over `rules-marker-entry` (rule entries inside the governed group)

| Problem its critics found | Here | Basis |
| --- | --- | --- |
| The derived identifier comes back: a later narrowing swallowed, a restore that stays dead, two entries with one identifier (survival P1, security S1) | a nonce in every version | by 2.4; not run against their chains |
| Two concurrent rule entries, one deleted by hand, widen to the other (survival P2) | the deleted one's record refuses the node | [M probe, 10, h08, for one version]; the two-version chain not run |
| Two deletions reach silence, with pykeepass or before a KeePassDX merge (P4) | the node's reference is the third witness and is not in the plugin's groups | [M probe, h09, h10] |
| An import with new identifiers, then the tab's Remove, opens every duplicate (P3) | the duplicates are limited from the start; the tab offers Adopt first and names what Remove does | [M probe, Create new IDs] |
| An entry's rule entry stays in the group the entry left; deleting that group closes the entry (security S3, survival P8) | an entry's reference is on the entry and its rule is in the plugin's group. Nothing of an entry's rule is in a user's group | by design |
| The rule entry in a user's group is one gesture from a refusal, and at the root that closes the gate (survival P5) | the link holds no rule. Deleting it suspends the group's grant and keeps its narrowings; a seal held | [M probe, h13, h14] |
| The fold reads the recycle bin pointer, which merges by a time (P6) | nothing reads the bin | by 3.1 |
| The rule write runs outside the writer's guards and lands inside KeePass's save (security S2) | the order of 4.1 and `EntrySaving`'s cancel | [R 2.62]; not run |
| A planted `replaced` record plus the prune deletes a rule entry for good (security M1) | version ids hold a nonce and cannot be derived from the rule text; the only deletion is of the version the same write replaces | by 2.4 |
| The empty root rule entry (security M2) | not written | by 2.3 |

### Over `rules-on-node` (the text on the node)

The text never rides the node's clock, so the rollback that design calls ordinary cannot happen to
a rule [M probe, 04]. What does ride the node is a value that never changes.

## 8. What it costs

- **A field on user entries, visible in every program**: `KPG.Anchor` and 32 hex digits on every
  ruled entry, in each program's field list, in Keepass2Android's and KeePassDX's search of other
  strings [R android 4.2], in KeePassXC's HTML export and KeePass's print [M keepassxc 8;
  R keepass 9]. It shows that the entry is ruled, and not how.
- **An entry in every ruled group and at a ruled root.** A person can delete or move it. The cost
  is then a suspended grant, shown on the tab, and never a widening.
- **A write to the governed entry**, once, at its first rule: a revision in its history, a new
  modification time (which can beat an older edit of that entry in another copy, as any edit
  does), and the dialog's OK and Cancel. Later rule changes write nothing on the node.
- **One more thing the API must hide inside entries it serves** (section 9), where the central
  design hides whole entries only.
- **Two entries per rule change**, and the record stays for ever: about 250 characters each. A
  database whose rules changed 500 times holds 500 records in the store group.
- **A new notion in the resolver**, the limit, and the tests that pin it against every verb and
  switch.
- **Grants that wait**: the first weakness of section 1.
- **A changed promise**: `docs/access-control.md` promises that a copy of a node carries its rules,
  templates included. It becomes: a copy carries what its original's rule takes away.
- **Tab states and presses**: five more states in a tab held to 449 by 314.
- **The pass over every entry on every request**, as in both one-way designs.

## 9. Keeping the API and the reference client out

1. **An entry anywhere that carries `KPG.Kind` is absent on every route**, as the recycle bin is
   (`Bridge/KdbxVault.cs:2123-2154` [R plugin, as the central design cites it]): versions, records
   and links, wherever they sit.
   The two plugin groups are absent too.
2. **On every other entry, a string whose name begins `KPG.` is hidden and refused**: not named,
   counted, tagged or measured by any read; `404` for a field route that names one; `400` for a
   create or update that would make one, decided from the request alone. `updateEntry` carries
   `KPG.Anchor` over untouched. The test is made with KeePass's own collation in KeePass's
   culture, since names that differ only by a character it gives no weight would otherwise pass
   [M crit, `rules-on-node-security-and-fit` P5].
3. **A client cannot make a copy**: no route copies or moves a node, and a new entry starts with
   no reserved string.
4. **`deleteGroup` treats a group that holds only its link as empty**, and the link goes where the
   group goes. A plugin entry that a person dragged into a user's group gets the sentence the
   recycle bin has, so the refusal does not reveal it.
5. The routing document gains nothing. `pykeepassgate.refs` refuses `{S:KPG.Anchor}` as it refuses
   every placeholder it cannot be sure of, and `ReferenceVectors.json` gains that case from
   KeePass. A field reference cannot return a custom string
   ([R 2.62 `KeePass/Util/Spr/SprEngine.cs:586-606`, as the central design's critic read it]).

## 10. Other data about a node, and the database's own data

**The same pair serves every datum about a node.** The anchor names the node, not the rule, so
any record about that node carries `KPG.Anchor` and `KPG.Node`, and the node needs one reference
however many kinds of record there are. The rule of thumb from 3.3 carries over: **a record that
only takes away needs the reference from the record alone; a record that gives needs both.** The
recycle bin markers of the open T049 design mean "this group was a bin": they only hide, so they
would be one more kind in the store group, counted wherever found, and no link has to be put into
a bin. A datum that would widen what a client reaches at a node (none exists today) would wait for
the node's reference as a rule does.

**The database's own data stay as `option-store-entries.md` has them**: the authority, retirements,
admission records, revocations, standing answers and the profile list are about the database, and
the database has no node to refer back from. They gain nothing from a second direction. The two
designs share the store group, the pass, the guard's first rule and the notion "refusing records
count wherever they are", of which the rule record is one more. This design depends on that one
for the records: kept as database custom data they would be removed whole by a KeePassXC merge
[M spike].

## 11. Settled decisions: reversed and kept

**Reversed, each to be recorded.** Grants live under a `KPG` key of the node's custom data: now a
version in the plugin's group, tied to the node from both sides. The grant editor writes into
`m_sdCustomData`: now `EntryStrings` for a first rule and the tab's buffer otherwise. "The plugin
writes no rule of its own": it writes versions, records and links, at the operator's OK. A node
with no rule is silence: only where neither direction holds a reference. A restored revision
brings its rule back: no longer. A copy of a node carries its rules: it carries what they take
away. No route hides anything inside an entry it serves: a reserved name space. `deleteGroup`
refuses a group that holds something: the link is not something.

**Kept.** One document format; nearest wins, per profile; an empty array is a rule; `default`; a
document that will not read refuses; no per-subject opt-in; the profile list decides what a client
holds; OK keeps a rule and Cancel discards it; opening or reading a database changes nothing in
it; nothing that grants is cached across requests; the router asks before any handler; a refused
node and an absent one answer the same `404`; one JSON grammar and one escaper (the document is
unchanged, everything else is hex and fixed words); `DatabaseWriter` as the single writer for
requests; no host registry, nothing outside the file.

## 12. What it does not solve

- **A whole file written over another, and a stale copy on its own.** No store inside the file
  can tell.
- **Three removals in three places**: the version, its record, and the node's reference
  [M probe, h10]. The node then inherits.
- **The rollback of 5.6**: version and record removed with no trace, then a merge that applies no
  deletions with a copy that holds the older version.
- **A group's rule planted from both sides** (5.5), unless section 13 is taken.
- **Grants suspended with nobody at fault** (scenario 02), and a group's grant suspended by one
  deletion or move of its link (h13, 11), or by Delete Duplicate Entries after a group was copied
  by a drag, which can delete the original link [A, from h16: the same call, not run for a link].
- **An unruled entry in a copied group gets the copy group's limit, not a rule of its own**, so it
  is as closed as the original group's rule makes it and no more.
- **A copy made by a program that drops custom strings or does not copy a group's entries** would
  carry no reference. None is known; none was measured on Android.
- **Visibility and clutter**, section 8.
- **The Android merges and the Android copies are read, not measured**, for this shape.

## 13. A change that closes the two-sided plant, and its price

Authenticate every version: a MAC over its strings under a secret the database holds once and a
foreign file cannot supply. `option-wildcard.md` makes such a key once for its store; the critic of
the central design proposed the served authority's gate key. A version that fails the MAC would
not even limit, which also takes away the narrowing a planted rule can cause. The price: one more
secret to keep, a rule for what happens when it is lost or two arrive, and, if it is derived from
the authority, every version signed again at Recreate. It is separable: nothing in sections 2 to 4
changes with it.

## 14. A migration, in one paragraph

None is designed. Carrying a 0.1.0 database over would be a press on the KPG tab, never an act at
open: for every node whose custom data holds a `KPG` rule that reads, mint an anchor, write the
reference (the string on an entry, which is a revision of that entry; a link in a group), the
first version and its record, save, and only then remove the old item. Until that press, and for
any node a stale copy brings back with the old item, a node that still carries the old item has to
refuse, because not reading it would be silence, and silence inherits; that is a check, not
support for the old shape. Whether any 0.1.0 database is in use is a question for the project.

## 15. The probe behind the [M probe] marks

- Source:
  `spikes\storage\probes\rules-linked\design\probe\Program.cs`,
  compiled with the .NET Framework's own `csc` against a copy of
  `.keepass\2.62\KeePass.exe` (SHA-256 `AFB89B74…C89C3F3`, equal to the
  original's). No window. `keepassxc-cli` was the only other program started, from the copies the
  `keepassxc` ground report extracted; each carried a valid Authenticode signature from
  DroidMonkey Apps, LLC, checked again (`out\signatures.txt`).
- Outputs, under `rules-linked\out\`: `keepass.txt`, `xc-2.4.2.txt`, `xc-2.6.6.txt`,
  `xc-2.7.12.txt`, `xc-2.8.0.txt`. Databases under `rules-linked\work\`. Invented content,
  Argon2d with 2 iterations and 1 MiB, password `probe-only`.
- The probe implements the write path of section 4 (first rule on an entry as an edit with a
  revision, first rule on a group, a rule change with the replaced version deleted and recorded)
  and the read path of section 3 with the walk of 3.4, for one profile, and prints for every
  user entry what the profile is granted and the state of the node.
- KeePassLib: 12 scenarios under Synchronize, Overwrite if newer, Overwrite existing and Keep
  existing in both directions, each result saved and reopened; 17 acts by hand in one file;
  `PwEntry.Duplicate`, `PwGroup.Duplicate`, `KdbxFile.WriteEntries` and `ReadEntries`,
  `DeleteDuplicateEntries`, and Create new IDs; the foreign file under four methods; the lost
  version followed by Synchronize.
- KeePassXC: a plain save, `edit -u` on a ruled entry, the 12 scenarios with `merge -s` in both
  directions, and the lost version followed by a merge, in each of four releases. After the
  release's name is taken out, the four outputs are the same line for line.
- What the probe is not: the plugin's code, KeePass's window, KeePassXC's window, any Android
  program. Settle, Confirm and Adopt were not implemented. The cost of the pass was not measured.

## 16. Measurements still needed, most important first

1. **The Android programs over this shape**, on the emulator the other agent is using, once it is
   free: that Keepass2Android, KeePassDroid 2.7 and KeePassDX 3.2.0, 3.3.0 and 4.5.8 keep
   `KPG.Anchor` on a user's entry through a save and through an edit of another field of that
   entry (measured so far for Keepass2Android only, and only in that agent's working files); that
   Keepass2Android's copy of an entry and KeePassDX's copy of an entry and of a group carry the
   string and the link; the 12 scenarios through Keepass2Android's and KeePassDX's merges in both
   directions, the tie among them; where a link lands when its group is missing; KeePassDX's move
   of unchanged entries at equal times, for links.
2. **KeePass's own window with the plugin** (the Live tier): Duplicate Entry, Duplicate Group, a
   copying drag, paste and Add Entry from a template really carry the reference; a first rule
   written into `EntryStrings` is kept by OK, dropped by Cancel and asked about on Escape;
   `EntrySaving` cancels cleanly while a save is pumping; Add Entry with "save after modifying an
   entry" on writes the entry, its version and its record into the same file; a rule-only change
   marks the database modified; Edit Entries; a restored revision.
3. **The presses**: Settle after each loss of 5.4 and 5.6, then a merge with a copy that still
   holds the lost version (the critics' chains C3 and C5); Confirm and Adopt; that an OK without a
   press writes nothing.
4. **The limit against the real resolver**: for every profile, verb and switch, with `default`,
   seals and several limits on one walk, the answer is at most the answer without the limit and at
   most the answer with the limit as the rule.
5. **The chains the critics measured against the one-way designs**, run against this one:
   `rules-central-survival` C1 to C5 with a model of KeePassDX's merge, `rules-marker-entry-survival`
   s4 to s7, and three copies merged in every order.
6. **How often a first grant on an entry meets a later edit elsewhere**, which is this design's
   falsifier: if opening single entries is the common use and copies are edited apart for days,
   grants will wait often.
7. **KeePassXC's window**: Clone of an entry and of a group, its window merge, its merge on
   reloading a changed file, and a KeeShare import (the one path that applies deletions, and a way
   for a share's writer to bring a link and a rule into a shared group).
8. **The pass inside the gate**, on KeePass's thread, for 1,000, 10,000 and 50,000 entries, with a
   full listing, beside today's read.
9. **Every route against the guard**: a plugin entry in a user's group and in the bin; the
   reserved prefix in each letter case and with characters KeePass's collation ignores;
   `deleteGroup` over a group that holds only its link.
10. **A hostile file that brings an entry whole and newer with a reference of its own** (5.5's
    assumed case), and Delete Duplicate Entries after a group was copied by a drag.
11. **The lower-depth programs**: kdbxweb's merge, Strongbox and KeePassKit, a KeePassium save.
