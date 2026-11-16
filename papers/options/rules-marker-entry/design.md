# Option `rules-marker-entry`: access rules as rule entries inside the group they govern

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. One design, the strongest
form of this option that I could build and measure. It covers datum 7, the access rules. Data 1 to
6 are assumed to be entries in a dedicated group, here called the KPG group, as the option
`store-entries` describes it; section 2.4 names the two small records this design adds there.

## How statements are marked

- **[M here]**: measured for this design, today, from a console. Probe, scripts and raw output are
  under
  `spikes\storage\probes\rules-marker-entry\design\`
  (`probe\Program.cs`, `scripts\Run.ps1`, `scripts\check.py`, `out\merges.txt`, `out\compact.txt`,
  `out\check.txt`, `out\cost.txt`). Section 14 describes the runs.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M android]**, **[M spike]**: measured in
  that ground report (`ground-keepass.md`, `ground-keepassxc.md`, `ground-others.md`,
  `ground-android.md`) or recorded by `spikes/revocation-merge` as `ground-measured.md` and
  `ground-inventory.md` digest it.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[R here]**: read by me, with file and line, from the review4 worktree at `a42e38aa` or from
  `KeePass-2.62-Source.zip` as the KeePass ground report extracted it.
- **[A]**: assumed. Neither run nor read. Every assumed statement about another program is on the
  list in section 13.

## 1. The design in one page

**The timestamp-named-field idea, with its unit changed.** The idea was a meta entry inside the group, holding
JSON in fields named after a timestamp, plus a step at load that repairs what a merge left. Every
merge anybody measured takes an entry whole and never field by field [M keepass, 120 cells;
M keepassxc; M others; R android], so two fields added to one meta entry in two copies do not both
survive. Two entries added to one group in two copies do, in every merge measured [M keepass X1;
M keepassxc; M others; M here] and read [R android]. So:

1. **A rule is an ordinary entry, the *rule entry*, kept in the group it governs.** A group's rule
   entry sits directly inside that group. An entry's rule entry sits beside that entry, in the same
   group, and names it. The rule text is today's rule document, unchanged, in a custom string.
2. **A rule entry is written once and never changed.** Changing a rule adds a new rule entry that
   names the ones it replaces and removes those. Its identifier is derived from its content, so
   the same decision made in two copies is the same entry, and an entry whose content was changed
   by hand or copied under a new identifier is not a rule entry any more.
3. **Three facts decide what is in force at a node, and each is something only ever added**: the
   rule entries that name the node; a `replaced` record for every rule entry a later rule
   replaced; a `ruled` record for every node that ever carried a rule. The two records are entries
   in the KPG group.
4. **The fold at read, with nothing written at load or after a merge:**
   - exactly one rule entry of the node counts: its document is the rule;
   - several count, because two copies each changed the rule without seeing the other's change:
     the **narrower of them** is the rule, until the operator saves the rule again;
   - none counts and the node was never ruled: silence, which inherits, as today;
   - none counts and the node is recorded as ruled: the node **refuses**, exactly as a document
     that will not read refuses today;
   - an entry that claims to be a rule and is not one **closes the group it sits in**.
5. **Nothing in the fold reads a modification time, a history item, a custom data item or a group's
   own fields.** Those are what the ground reports show other programs dropping, rewriting or
   deciding by.
6. **The plugin never writes on a user's group or entry for a rule.** No user entry gains a field,
   no node's time moves, the entry dialog's comparison sees nothing, and a restored revision of an
   entry no longer brings an old rule back.

**Measured for this report**: 16 groups and the root, each a different way two copies can come to
disagree about a rule, three of them about an entry's rule. In every result of a merge that matches
identifiers (KeePassLib 2.62 in four methods and both directions, `keepassxc-cli` 2.4.2, 2.6.6,
2.7.12 and 2.8.0 in both directions, four chains of the two, two round trips, and four results in
which a person then deleted the rule entry in force), what a client would be granted was the last
rule the operator set, the narrower of two rules that never saw each other, or nothing: 560
verdicts, none wider [M here, `out\check.txt`].

**The honest weakness, first.** The closure comes from the two records and the identifier check,
not from where the rule entry sits. What sitting in the group buys is narrower and named in
section 2.5. And the rule entry is in plain sight in every program, inside the user's own groups.

## 2. Where each datum lives

### 2.1 A group's rule

One rule entry per rule version, directly inside the group. In the normal state a ruled group
holds exactly one.

| Part of the entry | Content |
| --- | --- |
| UUID | the first 16 bytes of SHA-256 over the UTF-8 text `KPG/1/rule/<KPG.Node>`, a line feed, `<KPG.After>`, a line feed, `<KPG.Rule>` |
| `Title` | `KeePassGate rule (this group)`. Fixed words, never text a client or the operator supplied and never a brace, because KeePass compiles a title as placeholders when it matches auto-type [R keepass 9] |
| `UserName`, `Password`, `URL` | present and empty. Empty password: the quality and duplicate reports of KeePass and the health and HIBP checks of KeePassXC skip the entry [R keepass 9; R keepassxc 8]. Empty URL: nothing for a browser or autofill to match [R keepassxc 8; R android 4.2]. Present: a KeePassXC save adds the standard fields an entry lacks, and it added nothing to a rule entry [M here, four releases] |
| `Notes` | one fixed line: `Access rule for KeePassGate. Change it in KeePass, on the KPG tab of this group. Deleting or editing this entry closes the group to KeePassGate clients.` |
| `KPG.Kind` | `rule`. It marks the entry as the plugin's for the API's guard (section 8) |
| `KPG.Node` | `group/` and the group's identifier as 32 hex digits |
| `KPG.After` | the identifiers of every rule entry this one replaces, the newest first, separated by commas, at most 128; `none` for a node's first rule. Never empty, since some programs treat an empty value as absent [M others, for custom data in kdbxweb] |
| `KPG.Rule` | the rule document as `AclDocument` writes it today: `{"default":"deny","ops":["read","list"]}`. Not protected: a rule is no secret inside the database |
| auto-type | off for the entry |
| icon | KeePass's standard icon 29, the plugin's own |
| expiry, tags, attachments, entry custom data, history | none |

As a person sees it in any program:

```text
Group  Servers
  Entry  KeePassGate rule (this group)
           KPG.Kind   rule
           KPG.Node   group/4F0C91AA0B7E4D5F9C3A6E1D2B8F7A60
           KPG.After  none
           KPG.Rule   {"default":"deny","ops":["read","list"]}
  Entry  db-primary            (the user's own)
  Entry  db-replica            (the user's own)
```

**Why custom strings.** Custom strings are read and written back by Keepass2Android, KeePassDX
4.5.8 and 3.2.0 and KeePassDroid [R android 2], by KeePassXC 2.4.2 to 2.8.0 [M keepassxc 6;
M here], by kdbxweb and pykeepass [M others] and are carried by KeePassium, Strongbox, KeePassKit
and gokeepasslib [R others]. Entry custom data is deleted by KeePassDroid and replaced per key by a
KeePassDX merge [M spike]; group custom data is deleted by KeePassDroid and KeePassDX 3.2.0 and
3.3.0 and never merged by KeePassXC [M spike; M keepassxc 4]; attachments are treated as the same
attachment by KeePassDX when 32 bits of a hash agree [R android 5.1; the cost of a collision
M android]; tags are rewritten [R keepass 2.3; M keepassxc 6].

**Why the identifier comes from the content.** Three things follow, and each was measured:

- The same rule made in two copies is one entry after a merge, in KeePassLib's four
  identifier-matching methods and in four KeePassXC releases, both directions [M here, G04 and
  G05]. When the two copies made it at different times the entry comes out with one history item
  holding the same content [M here, G04]; nothing reads it.
- A copy under a new identifier is not a rule entry. A rule entry duplicated by hand, and every
  rule entry that an import with new identifiers duplicated, failed the check and closed the group
  it sat in [M here].
- A rule entry edited by hand is not a rule entry. The rule text of G02 changed by hand to a wider
  rule: G02 refused. The title and notes of G11's rule entry changed by hand: no effect, since the
  identifier covers only the three `KPG` strings [M here, `hand edit` cell].

An identifier never comes back: a later rule entry of the same node always names more
predecessors, so its text differs even when the rule is set back to an earlier one. That matters
because kdbxweb removes whatever a deletion record names, whatever the times [R others].

### 2.2 An entry's own rule: a rule entry beside the entry

The task leaves three answers open. This design takes the second, and here is the argument.

| Answer | What happens | Verdict |
| --- | --- | --- |
| A field on the entry | The rule shares the entry's clock with the password. One copy narrows the rule; another copy, not yet synchronized, changes the password a minute later; every merge takes the later entry whole [M keepass 1a; M keepassxc 4; M others 4; R android 1.1], so the wide rule is current again and the narrowing is one history item, which is trimmed after ten later changes and lost at once at a tie [M keepass 1b; M keepassxc 5]. This is how today's rule in entry custom data already behaves under KeePass's own synchronize. A restored revision brings the old rule back. The field also sits inside what the field routes serve | Rejected. It cannot be made to fail closed without a second record per version elsewhere, at which point the field is the weak copy |
| No entry-level rules | Simplest and closed by construction: one kind of rule entry, no entry that can be moved away from its rule. But the narrowest slice the product gives, one entry opened to one profile or one entry sealed inside an open group, would need a group of its own, so the operator would have to arrange the database around its clients | Rejected, for what it takes away. It is the fallback if the cost in section 11 is judged too high: everything in this design then holds with `entry/` nodes removed |
| **A rule entry beside the entry, naming it** | The rule merges on its own, apart from the entry's edits. Measured: copy A narrowed an entry's rule, copy B changed that entry's password an hour later; A's narrowed rule was in force in all 28 results, beside B's password in 26 of them and beside A's in the two where the method keeps the open entry or takes the arriving one whatever the times [M here, G12] | **Chosen** |

The entry's rule entry is the same shape with three differences: `KPG.Node` is `entry/` and the
entry's identifier; the title is `KeePassGate rule (entry 4F0C91AA)`, the first eight digits of
that identifier; the notes line names the entry's KPG tab. It is made in the group that holds the
entry at the moment the rule is saved.

**What this costs, named here and again in section 11.** A copy of a ruled entry made with
Duplicate Entry is a new entry with no rule, where today the copy carries the rule. And a ruled
entry that a person moves to another group leaves its rule entry behind; the rule stays in force,
because rule entries are looked for everywhere (section 3), and the next save of that entry's rule
puts a new rule entry beside it.

### 2.3 The root always carries a rule entry

When the editor writes the first rule entry of a database, and the root group has none, it also
writes a rule entry for the root holding the empty document (`AclDocument.Empty`,
`Acl\AclDocument.cs:100-103` [R here], default inherit and no profile), and the root's `ruled`
record. At the root that document changes nothing: there is nothing above to inherit.

It is there for one measured reason. An import with new identifiers, which File then Import
preselects [R keepass 2.4], puts the arriving root's own entries **into the open root** and the
arriving top-level groups beside the originals [M here; M keepass X1]. The arriving root's rule
entry arrives as a copy under a new identifier, so the open root holds an entry that claims to be
a rule and is not one, and the root closes. Every duplicated group that carries no rule entry of
its own then ends its walk at a refusing root instead of inheriting [M here, both imports with new
identifiers: the root refused, and so did every duplicate].

### 2.4 The two records in the KPG group

Both have the shape of every record in that group (a fixed title, empty standard fields, `KPG.Kind`
and `KPG.Data`), are written once, never changed, and never removed by the plugin. Both are
*refusing* records in the sense the `store-entries` option uses: each counts wherever its entry is
found, the recycle bin included, by what its `KPG.Data` names, and still counts when only a
deletion record naming its derived identifier is left.

| Kind | Identifier derived from | `KPG.Data` | Written | Meaning |
| --- | --- | --- | --- | --- |
| `ruled` | `KPG/1/ruled/<node>` | `{"v":"1","node":"group/4F0C…"}` | with a node's first rule entry | this node must have a rule entry that counts; without one it refuses |
| `replaced` | `KPG/1/replaced/<rule entry identifier>` | `{"v":"1","entry":"9B1D…"}` | with the rule entry that replaces it | that rule entry is dead, wherever a merge brings it back |

**Why `ruled`.** An absent rule is silence and inherits (`Acl\AclDocument.cs:110-120`
[R inventory]). Nothing inside a group can witness that its rule entry is gone, because whatever
removes the rule entry can remove its neighbor too. A record elsewhere that merges as a union can.

**Why `replaced`, when the new rule entry already names what it replaces.** `KPG.After` lives in
the rule entry in force, so it is lost with that entry. KeePassXC never applies a deletion, so a
replaced rule entry stays or comes back beside the one in force in every KeePassXC merge
[M keepassxc 2; M here, 8 of 8 merges]. A person who then deletes one of two entries with the same
title may delete the rule entry in force, and without a record the older, wider rule would be the
only one left and would come back into force. With the record it stays dead: after each of four
KeePassXC merges, deleting the rule entry in force for good left the group refusing, in the copy
that held deletion records and in the copy that held none [M here, `head deleted` cells].

**Why not a counter or a time.** A counter lets the copy with more edits win over a narrowing it
never saw; a time trusts a clock. Naming exactly which rule entry is dead does neither.

**Growth.** One `ruled` record per node ever ruled and one `replaced` record per rule change, 50 to
60 characters of data each. They are never pruned: a rule entry they keep dead can come back from
a stale copy at any time.

**Datum 8.** The recycle bin markers of the open T049 design are not needed here. They would fit
as one more kind of record in the KPG group, added once and never changed.

### 2.5 What sitting in the group buys, and what it does not

It buys four things. The rule entries move with their group, with no write [M here, G08: a group
moved in one copy while the other copy changed its rule ended, in every merge, at the new place
with the new rule]. They go to the recycle bin with their group and come back with it. Every copy
of a ruled group made by an import with new identifiers, or by duplicating the group, carries a
copy of the rule entry that closes the copy [M here]. And a person looking at a group in any
program sees that it is ruled.

It does not make the rule safe; the records do. And it puts the plugin's entries among the
user's.

## 3. What is in force at a node

### 3.1 One pass per request

Inside the hold a request already runs in, one pass over every entry of the tree and over the
deletion records builds the request's view:

- every entry carrying `KPG.Kind` equal to `rule`, split into those outside the recycle bin and
  those inside it;
- every `ruled` and `replaced` record, wherever it is;
- the deletion records, as a set of identifiers.

Rule entries are looked for **in the whole tree, not only in the node's group**. Looking only in
the group would be cheaper and would miss one case: an entry moved in one copy while both copies
changed its rule leaves the two new rule entries in two groups, and the one left behind may be the
narrower. Measured: with the whole tree read, that case gave the narrower of the two in all 28
results; the group alone would have given one of them [M here, G14].

### 3.2 When a rule entry counts

A rule entry **reads** when its three `KPG` strings are well formed, its document reads as
`AclDocument` reads one today, and its identifier is the one its content derives. A rule entry
that reads **counts** when all of these hold:

- it is outside the recycle bin;
- no `replaced` record names it;
- no deletion record names it or its `replaced` record;
- no other rule entry of the same node that reads names it in `KPG.After`, a rule entry in the
  recycle bin included.

### 3.3 The document a node has

`KdbxNodes` hands the resolver one text per node, as today (`IAclNode.Document`,
`Acl\AclResolver.cs:15-36` [R here]). What changes is where the text comes from:

| What the pass found for node N | The document | As the resolver treats it |
| --- | --- | --- |
| an entry directly in N (a group), outside the recycle bin, that carries `KPG.Kind` `rule` and does not read | a text that will not read | N refuses, there and below |
| exactly one rule entry of N counts | its `KPG.Rule` | the rule, nearest wins per profile, as today |
| several count | the narrower of them (3.4) | a rule |
| none counts, and N has a `ruled` record, or any rule entry of N that does not count | a text that will not read | N refuses |
| none counts, no record, no rule entry | none | silence: N inherits, as today |

The resolver, the verbs, the switches, "nearest wins per profile" and "a document that will not
read refuses where it is found" do not change.

**Missing or unreadable, in one sentence each.** A rule entry that is missing where a rule once
was refuses the node. A rule entry that is there and will not read, or does not match its
identifier, closes the group it sits in. A rule entry that reads and names a node that is not in
the tree is ignored and shown.

### 3.4 Two rule entries that never saw each other

The narrower of several documents is one document, computed, never stored:

- `default` is `deny` if any of them denies;
- a profile every one of them names is granted the words every one of them grants;
- a profile only some of them name is granted nothing.

Each of those is at most what each document grants, so nothing either copy's operator took away
comes back. Measured: one copy set `reader` to read, the other set `reader` to read and list and
`ops` to read; every merge gave `{"default":"inherit","ops":[],"reader":["read"]}` [M here, G02,
G03, G15, 28 results each]. The KPG tab of the node shows both rules and that the narrower is in
force; saving the rule there writes one rule entry that names both.

### 3.5 Cost

On an invented database of 10,402 entries in 501 groups with 5,000 deletion records, 101 ruled
groups and 100 ruled entries, in a console process [M here, `out\cost.txt`]:

| Step | Time |
| --- | --- |
| the pass alone, testing each entry for the `KPG.Kind` string | 1.6 to 1.8 ms |
| building the whole view: the pass, every rule entry checked against its identifier, the deletion records into a set | 3.0 to 3.2 ms |
| checking one rule entry against its identifier (SHA-256 over 123 bytes) | 1.6 microseconds |
| a listing of all 10,402 entries, each asking for its own document and for each group's above it, with each group's document remembered for the request | 23 to 24 ms more |

So about 0.3 microseconds an entry on every request: 0.3 ms for a database of 1,000 entries. The
probe keys its lookups by text; an implementation keyed by identifier does less. The `store-entries`
option makes the same pass for its own records, so the two share it. Nothing that grants is kept
across requests.

## 4. Writing

### 4.1 The grant editor: its own buffer, applied at the dialog's OK

Today the editor writes into the dialog's own copy of the node's custom data, `m_sdCustomData`,
because `GroupForm` and `PwEntryForm` write that copy back on OK (`Bridge\NodeDialog.cs:17-22`
[R here]). A rule entry is in no copy either dialog holds: `GroupForm` copies the group's name,
notes, icon, tags and custom data [R keepass 3] and its source names no list of entries at all
(`KeePass\Forms\GroupForm.cs`, searched [R here]), and `PwEntryForm` copies the one entry it is
about [R keepass 4]. So the trap goes, and the mechanism the settings tab already uses takes its
place:

1. `NodeDialog.Store()` returns a buffer that implements `IGrantStore`. Reading it gives the
   document in force at the node from the view of section 3; writing it keeps the edited document
   in the tab and touches nothing.
2. `GrantPanelFactory` subscribes to the dialog's `FormClosed`, as `AuthorityPanelFactory` does.
   With `DialogResult.OK` it applies the buffer; otherwise it drops it. So OK keeps a rule and
   Cancel discards it, as today.
3. Applying, on KeePass's thread, with no save, as a rule change is not saved today either:
   - nothing at all when the document is the one in force, so a dialog that was only looked at
     writes nothing;
   - nothing when the node was never ruled and the document is the empty document;
   - otherwise one new rule entry in the node's group (the group itself for a group, the entry's
     parent for an entry), with `KPG.After` naming every rule entry of the node that counts and
     everything those name, cut to the newest 128;
   - a `replaced` record for every rule entry of the node found anywhere outside the bin, and
     those entries removed, each with a deletion record;
   - the `ruled` record of the node if it has none, and the root's rule entry of 2.3 if the root
     has none;
   - the database marked modified through `MainForm.UpdateUI`. The entry dialog reports no change
     when only a rule changed (`bMod = (bOK && dlg.HasModifiedEntry)`,
     `KeePass\Forms\MainForm_Functions.cs:5800` [R here]), so KeePass would not mark it.
4. **Taking every rule off a node writes a rule entry holding the empty document.** A node that
   was ruled once keeps a rule entry for good. Removing it instead would be indistinguishable from
   losing it.

Three details. Add Group puts its group into the tree before the dialog opens and takes it out on
Cancel (`KeePass\Forms\MainForm.cs:1358-1379` [R here]), so the rule entry is added only at OK.
Add Entry adds the entry to its group after the dialog has closed with OK
(`KeePass\Forms\MainForm_Functions.cs:6807-6812` [R here]), so the rule entry is added to that
group in a call posted with `BeginInvoke`, after KeePass has put the entry in. A history revision
and several entries edited together stay read only, as today.

Adding or removing an entry moves no time on the group: `PwGroup.AddEntry` appends and sets the
parent (`KeePassLib\PwGroup.cs:1342-1354` [R here]). So a rule change no longer makes the group
the newer copy in anybody's merge, where the group dialog's OK today always moves the group's time
[R keepass 3].

### 4.2 What the plugin writes without being asked

Only tidying, riding on its own writes as the prune does today, never at open and never on
somebody else's save:

- a rule entry that no longer counts because it was replaced is removed, with a deletion record,
  unless an entry dialog holds it (`GroupDialogs.Blocking(PwEntry)`, `Bridge\GroupDialogs.cs:581`
  [R here]); the next write takes it;
- nothing else. A rule entry that counts, one that does not read, one in the recycle bin and one
  whose node is gone are left where they are and shown on the tab. "The plugin writes no rule of
  its own" stays true.

### 4.3 What the tabs show

The KPG tab of a group or entry shows, above the table and inside the room the tab has: that two
rules are in force as the narrower of them, with both; that the node refuses because its rule
entry is gone, with **Save** putting the shown rule back; that an entry in this group claims to be
a rule and is not one, with **Remove**; that the node's rule entry sits in another group, with
**Put back**. Each is buffered until OK like any edit. The entry dialog of a rule entry itself
carries a KPG tab that names the node it rules and offers no editor. The settings tab gains one
line under Profiles: how many nodes refuse for a reason above, since an operator must be able to
find them without opening every group.

## 5. What changes in the plugin

| Class | Change |
| --- | --- |
| `Bridge\KdbxNodes` | `DocumentOf` reads the request's view instead of `CustomData`. The seam `IAclNode` and its remarks on the hold and the culture stay |
| new `Bridge\RuleEntries` | the one class that knows the strings, the derivation, the pass and the three write steps. Behind a seam, so the fold is tested without KeePass |
| `Acl\AclDocument` | format and reader unchanged; `CustomDataKey` goes; gains the narrower of several documents |
| `Acl\AclDocuments` | keeps the request's view beside the parsed documents |
| `Acl\AclResolver`, `AclGrant`, `AclVerb`, `AclInheritance`, `VerbMask` | unchanged |
| `Acl\IRuledNodes`, `Bridge\DatabaseNodes` | the Profiles page counts through the same view |
| `Bridge\NodeDialog` | `Working`, `DialogStore`, the reflection on `m_sdCustomData` and `KeePassContract.NodeFormCustomDataField` go; `Store()` returns the buffer. The other reflected fields stay |
| `Ui\IGrantStore` | unchanged |
| `Ui\GrantPanelFactory` | applies on `FormClosed` with OK, drops otherwise |
| `Ui\GrantPanel`, `GrantSheet`, `GrantTab` | the four states of 4.3; the table, the pickers and the keys are unchanged |
| `Bridge\GroupDialogs` | the watch is unchanged: it still guards the API's writes against the dialogs. It is asked before a replaced rule entry is removed |
| `Bridge\DatabaseWriter` | no rule passes through it, as today; its prune gains 4.2 |
| `PendingDatabase`, `PendingStore`, `AuthorityStore`, `AdmissionStore` | untouched by this option. As the `store-entries` option rebuilds them they leave the kinds `ruled` and `replaced` alone, including at Recreate and Delete, which are about the authority |
| `Bridge\KdbxVault`, `Pipeline\VaultHandlers` | the guard of section 8 |
| `Bridge\MergeWatch` | unchanged, and it matters more: a read inside a merge would see rule entries half arrived |
| Tests | host tests pinning, against the pinned KeePass, what section 6 rests on: two entries added to one group in two copies both arrive under each method; one identifier is one entry; an import with new identifiers puts the arriving root's entries in the open root; `PwGroup.AddEntry` moves no time. `SnapshotHazardTests` no longer bears on rules |

## 6. What a save and a merge do to it

Verdicts are from `out\merges.txt`, read by the design's own read path. "The rule" means the last
rule the operator set at that node.

| Program | A save | A merge | What the gate then enforces |
| --- | --- | --- | --- |
| KeePass 2.62, Synchronize | keeps everything; a file holding only this is KDBX 4.0 [M keepass X7; M here] | a rule entry only one copy holds arrives; replaced rule entries are removed in both copies by their deletion records; a rule entry moved to the bin is in the bin in both; both directions alike [M here] | the rule; the narrower where both copies changed it; refusal where a person had removed the rule entry |
| KeePass 2.62, Overwrite if newer, Overwrite existing, Keep existing | | additions as Synchronize. Into the copy that replaced a rule: as Synchronize, since a database applies its own deletion records under every method [M keepass X4; M here]. Into the stale copy: the replaced rule entry stays beside the new one, with no deletion record [M here] | the rule; the replaced rule entry is dead by its `replaced` record |
| KeePass 2.62, Create new IDs | | every arriving group and entry again under a new identifier; the arriving root's entries in the open root [M here; M keepass X1] | the root refuses; every duplicated group and entry refuses, by a copied rule entry in its group or at the root; the open copy's own nodes keep their own rule, which in 4 verdicts is wider than a merge would have given, since the other copy's change to that node arrives only as a copy that closes its duplicate; a node whose rule entry the arriving copy's `replaced` record names refuses [M here] |
| The same rule made in both copies | | one entry; one history item of the same content when the two times differ, none at a tie [M here, G04, G05] | the rule |
| A group's time moved later in the other copy, an entry in it edited there | | no effect on the rule entry [M here, G11] | the rule. Today the group's rule would go to the copy with the later group time [M keepass 3] |
| A group deleted for good in one copy while the other changes its rule | | the group comes back holding the new rule entry and none of its older entries [M here, G10; M keepass X3] | the new rule |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | every `KPG` entry identical before and after: identifier, group, time, auto-type switch, icon, every string and flag [M here, dump compared, four releases]. From 2.4.2 its merge replaces the database's custom data whole [M spike], which holds nothing of the rules | additions arrive in both directions; no deletion is applied or copied, so a replaced rule entry stays or comes back beside the rule entry in force in 8 of 8 merges and after three merges back and forth; a recycle bin arrives as an ordinary group [M here; M keepassxc 2, 4] | the rule or the narrower, in all 8 merges and both round trips. A rule entry a person had deleted for good comes back into the deleting copy and stays dead there by its deletion record. A rule entry that arrives inside an ordinary group named after the other copy's bin counts, and the old rule is in force there [M here, G06, G07] |
| KeePassXC merge, then KeePass Synchronize with the other copy | | what KeePassXC brought back is deleted again [M here, 4 chains; M keepassxc 9] | as Synchronize |
| Keepass2Android 1.15-r6, 1.08d-r5 | keeps entries, custom strings and a custom string's own flag [R android 2; entries as wholes M spike] | an arriving entry is added to the group of the same identifier, or to the root when that group is missing; the two copies' deletion records are joined [R android 1.5, 3.1]. Not measured for this shape | by the reads: as KeePass's Synchronize. A rule entry that landed at the root still counts, since the whole tree is read |
| KeePassDX 4.5.8 | keeps entries and custom strings; writes 4.1 [R android 2; M spike] | an arriving entry is added and a missing group recreated; arriving deletion records are applied and not kept [R android 1.5, 3.1]. It gives the root the time of every save and moves a group's time with every edit inside it [M spike; R android 1.4], which no longer decides anything. Not measured | by the reads: the rule; a replaced rule entry it leaves is dead by its record |
| KeePassDX 3.2.0, 3.3.0 | as 4.5.8 for entries and strings [R android 2]. They delete every group's custom data [M spike], which is where today's rule is and where this design keeps nothing | 3.2.0 drops an arriving entry whose group the open copy lacks [R android 1.5]. Not measured | by the reads: the rule. A dropped rule entry beside an arrived `ruled` record refuses |
| KeePassDroid 2.7 | keeps entries and custom strings [R android 2; entries as wholes M spike]. It deletes every node's custom data [M spike], today's rule with it. It has no merge [M spike] | none | unchanged |
| kdbxweb 2.1.1 (KeeWeb) | keeps strings and flags [M others] | two entries added in two copies both arrive; it removes whatever a deletion record names [M others; R others]; it never takes the other copy's entry or group custom data [M others], which no longer matters | by the reads: the rule; its stricter deletions fall on replaced rule entries |
| Strongbox, KeePassKit, keepass-rs | carry strings [R others] | add a node the open copy lacks [R others] | by the reads: the rule |
| A file written over: Keepass2Android "No, overwrite", KeePassDX within ten seconds, any KeePassDroid save, a file synchronizer | | nothing merges [R android 5.5; M spike; R others] | not solved, section 11 |

History limits, trimming, equal times, rewritten times and the root group's time, which decide so
much in the ground reports, are not in the table because no rule entry ever has two versions.

## 7. What a person sees, what they can break, and what the gate does

**What they see.** In every program: one entry titled `KeePassGate rule (this group)` in each ruled
group and at the root, one titled `KeePassGate rule (entry …)` beside each ruled entry, each with
an empty user name and password, one line of notes and four extra fields. In KeePass they carry
the plugin's icon, are offered by no auto-type, and are in no password report [R keepass 9]; the
last modified and large entries reports list them [R keepass 9]. Keepass2Android and KeePassDX
search custom strings, so a search for `read` or `KeePassGate` finds rule entries among the
user's entries [R android 4.2]; searching cannot be turned off for them without turning it off for
the user's group. KeePassXC's default search covers titles and notes and finds them by name
[R keepassxc 8]. Exports carry them: the rule text is in KeePassXC's HTML export and KeePass's
XML export [M keepassxc 8; R keepass 9]. A ruled group is never empty to KeePass's Delete Empty
Groups, which today removes a group that holds only notes and custom data, its rule with it
[M keepass X10].

**What they can break.**

| By hand, in any program | What the gate does | Basis |
| --- | --- | --- |
| Changes a rule entry's title, notes, icon or user name | nothing | M here |
| Changes `KPG.Rule`, `KPG.Node` or `KPG.After` | the group that entry sits in refuses, and so does the node | M here |
| Deletes the rule entry (to the bin) | the node refuses; moving it back out restores the rule | M here, in KeePassLib and with `keepassxc-cli` 2.4.2 and 2.8.0 |
| Deletes it for good, or empties the bin | the node refuses, by its `ruled` record | M here, the same |
| Then removes the deleted objects information in Database Maintenance | still refuses: the `ruled` record is an entry | by the fold |
| Deletes the node's `ruled` record | still counts in the bin, and after the bin is emptied by the deletion record | by the fold |
| Deletes the rule entry for good, the `ruled` record for good, and the deleted objects information | silence, and the node inherits. Three deliberate acts by the holder of the master key | section 11 |
| Deletes the rule entry in force while a replaced one is still beside it | the node refuses; the replaced one stays dead | M here, 4 results |
| Moves a rule entry to another group | nothing: still in force | M here, `keepassxc-cli` 2.8.0 |
| Moves a ruled group | the rule moves with it | M here, G08 |
| Moves a ruled entry | its rule stays in force; the rule entry stays behind until the rule is saved again | M here, G13 |
| Deletes a ruled group | the rule entries go to the bin with it and come back with it | by the fold |
| Duplicates a rule entry | the copy closes the group it sits in; removing the copy reopens it | M here |
| Then runs Delete Duplicate Entries | KeePass deletes one of the two, and the group still refuses, since the copy may be the entry that stays | M here: 1 deleted, refusal before and after. With no copy made, 0 of 60 entries deleted, although five groups carried the same rule, two of them with the same `KPG.After` as well: `KPG.Node` makes the strings differ |
| Duplicates a ruled group, or imports with new identifiers | the copy refuses; after an import the root refuses too. Close without saving is the remedy | M here |
| Duplicates a ruled entry | the copy has no rule and inherits | section 11 |
| Restores a history revision of a ruled entry | nothing: the rule is not in the entry | by the design |

## 8. Keeping the API and the reference client out

Today no route reads or writes custom data, so a rule is out of reach by construction
[R inventory P15]. A rule entry sits inside the very group a client may be allowed to create and
update entries in, so the guard is explicit, in the vault and not in any rule.

1. **An entry carrying a string whose name begins `KPG.` is absent on every route**, exactly as
   the recycle bin is (`KdbxVault.Discarded`, `Bridge\KdbxVault.cs:2123-2155`, `DESIGN.md` §138
   [R here]): `FindEntry` answers null, so reading, updating, deleting, the field and attachment
   routes and both access routes answer the `404` of a node the rules do not reach.
2. **Listings and counts leave it out**: `listGroupEntries`, the flat `listEntries`, and whatever
   a group's read counts. A tag is made from what its caller is shown, so no tag holds a rule
   entry and a rule change moves no tag.
3. **No route makes or names such a string.** `createField` refuses a name beginning `KPG.`,
   compared without regard to ASCII case, with the `400 malformed` it gives a name its routes
   could not name again. `createEntry` and `updateEntry` take the five standard names and
   `expires` and nothing else (`EntryFields`, `additionalProperties: false`,
   `Spec\openapi.yaml:1982-1984` [R here]).
4. **A second line behind the first.** Even an entry that carried the four strings would not be a
   rule entry: `createEntry` gives a new entry a fresh identifier, and a rule entry counts only
   under the identifier its content derives. An entry that claims to be a rule and is not one
   closes its group. So the worst a client could reach by planting is a refusal, never a grant.
5. **A client cannot change, move or delete a rule entry**: it cannot address one, and no route
   moves anything.
6. **`deleteGroup` treats a group holding only rule entries as empty** and takes them with the
   group, to the recycle bin, or written down as deleted where the database keeps none. The check
   today is on raw counts (`Bridge\KdbxVault.cs:489` [R here]). Without this a client told the
   group is empty could never remove it.
7. The routing document gains no route and no schema. `API.md` gains the reserved prefix and the
   absent entries.

**The reference client changes nothing.** `pykeepassgate.api` has no method that could reach a
rule entry; `pykeepassgate.refs` follows references by identifier under the caller's rules and
meets the same `404`; the stand-in gate models no rules. `pykeepassgate.model` shows a group's
entries as the gate lists them, so without rule entries.

## 9. KDBX version

**4.0, and nothing here needs more.** Entries, custom strings, the entry's auto-type switch and
deletion records are written at 4.0 by KeePass 2.62 [M keepass X7]. Every file KeePassLib 2.62
and KeePassXC 2.4.2 and 2.6.6 wrote in this run is 4.0, 34 of 46 files; the twelve that are 4.1
all passed through a merge, a move or a removal in KeePassXC 2.7.12 or 2.8.0, where those releases
record a previous parent group [M here; M keepassxc 6]. Keepass2Android writes KDBX 3.1 for a file with
AES-KDF and no node custom data [M spike]; with the rules out of custom data a ruled database can
now be such a file, and that entries and custom strings pass through 3.1 is [A].

## 10. Settled decisions: reversed and kept

**Reversed, each to be recorded as a reversal:**

- "The access control model is one document per node … Grants live under a `KPG` key": the
  document and the model stay; the place is a rule entry, not the node's custom data.
- "An empty array is a rule … a profile the document does not name is silence": stays, and gains
  two things: a node recorded as ruled with no rule entry that counts refuses, and several rule
  entries that never saw each other give the narrower of them.
- "The grant editor writes into the dialog's own copy, `m_sdCustomData`": gone. The editor keeps
  its own buffer and applies it at the dialog's OK. "A KeePass that renames it leaves the editor
  showing the rules and refusing to change them" goes with it.
- `NodeDialog`'s "the key is removed rather than set to nothing … so that a node whose grants were
  all taken away is indistinguishable from one that never had any": reversed on purpose. A node
  that was ruled keeps a rule entry, because indistinguishable is exactly what a lost rule must
  not be.
- Step 13's "the editor never writes the same rules in other words", argued from the entry
  dialog's comparison: the behavior stays, the reason changes to "a new rule entry is a new
  version".
- "The trap that shaped the design", for rules: `GroupForm`'s copy no longer holds a rule. The
  admission record's half of that section is the store's subject.
- `DESIGN.md` on a restored revision bringing its rule back, and `CA.md` on a rule travelling with
  its node by the node's time: both stop being true.
- `DESIGN.md` §138 and `API.md` §401 and §407: the recycle bin is no longer the only thing in the
  tree that no route reaches.

**Kept:** nearest wins, per profile; a document that will not read refuses where it is found;
`protected` and `attachments` as scopes; six verbs and two switches; no per-subject opt-in; the
profile list decides what a client holds; OK keeps a rule and Cancel discards it; opening or
reading a database changes nothing in it; nothing that grants is cached across requests; the JSON
grammar and the one escaper (the document is unchanged and the two records are flat objects of
strings); refused and absent answer the same `404`; spec-first routing; every database is its own
trust domain, with nothing outside the file; "nothing on a tab scrolls".

## 11. What it does not solve

- **A file written over, and a stale copy on its own.** One file wins whole [R android 5.5;
  M spike; R others]. No store inside the file survives that.
- **A copy of a ruled entry has no rule.** Duplicate Entry in KeePass, a clone in KeePassXC, a
  copy on Android: the new entry inherits. For an entry opened to a profile that is the closed
  direction. For an entry sealed inside an open group it is a widening, by an act of the operator,
  and today's rule in custom data would have been copied along. The only cure found is a field on
  the user's entry, which this design does not write.
- **An import with new identifiers of a copy made before the database's first rule** brings
  duplicates that carry no rule entry and no root rule entry, and they inherit from the open root.
  Today's store has the same exposure. With a root rule entry in the arriving copy, every
  duplicate refuses, and so does every node of the open copy that inherits from the root, until
  the operator closes without saving or removes the copied rule entry from the root [M here].
- **Under that import the open copy's own rule stays** where the arriving copy had changed the
  same node's rule: that method merges no node [M here, 4 of 76 verdicts].
- **A hostile file merged in by the operator** can carry rule entries that name the open
  database's nodes and its rule entries. Nothing in a rule entry is authenticated beyond the file.
  Today a hostile file's group with a later time replaces a rule the same way.
- **Three deliberate deletions**: the rule entry for good, its `ruled` record for good, and the
  deleted objects information.
- **A group deleted in one copy and brought back by a KeePassXC merge from a stale copy** comes
  back with the stale copy's rule entry. If a later rule had replaced it, it is dead and the group
  refuses; if it was the last rule, it is in force.
- **The plugin's entries are in the user's groups**, in every program's lists, in Android
  searches, in exports and in the last modified report. A person tidying up can close a group to
  clients by deleting or editing the rule entry. That is never a widening, and it is an outage
  they did not ask for.
- **A rule cannot be removed in another program.** Deleting the rule entry there refuses the
  node. Removing a rule is done on the KPG tab.
- **The cost of the pass** on every request (3.5), and **two records that only grow** (2.4).
- **The Android merges and saves are read, not measured**, for this shape.

## 12. A migration, in one paragraph

None is designed. Carrying a 0.1.0 database over would be a press on the KPG tab, never an act at
open: for every node whose custom data holds a `KPG` rule that reads, write a rule entry and a
`ruled` record, save, and only then remove the key. A stale copy merged in later could bring a
`KPG` key back, and the gate would have to go on reading that key as a refusal for as long as such
copies exist, which is the legacy code the project's rule against carrying an older stored shape
forbids. Within that rule the alternative is that a rule in custom data is not read at all and the
operator sets the rules again, with a database that grants nothing until they do.

## 13. Measurements to make before this design could be chosen

Most important first. Items 1 and 2 need the emulator or a window and wait for the desktop.

1. **The Android programs over this report's files** (`work\base\A.kdbx` and `B.kdbx`, read back
   with `Rme.exe eval`): merges by Keepass2Android 1.15-r6 and KeePassDX 4.5.8 and 3.2.0 in both
   directions; saves by KeePassDroid 2.7, KeePassDX 3.2.0, 3.3.0 and 4.5.8 and Keepass2Android,
   its KDBX 3.1 output included. Does a rule entry only the arriving copy holds arrive in a user's
   group; where does Keepass2Android put a rule entry whose group it lacks; does KeePassDX 3.2.0
   drop one; are the four strings, a 4,223 character `KPG.After` and the entry's auto-type switch
   kept. Every Android cell of section 6 is a read until this runs.
2. **KeePass's own window with the plugin**: the editor applying at `FormClosed` on Edit Group,
   Add Group, Edit Entry and Add Entry, with OK and with Cancel; that neither dialog's OK touches
   a group's list of entries; the modified flag and Auto-save after an entry dialog in which only
   a rule changed; a real File then Synchronize and File then Import over A and B; Duplicate
   Entry and Duplicate Group; Delete Duplicate Entries, Delete Empty Groups and Empty Recycle Bin
   through the window.
3. **The pass in the real plugin, on KeePass's thread**: for 1,000, 10,000 and 50,000 entries,
   beside what a request costs today; whether a listing builds the view once; a listing of 20,000
   entries.
4. **Every route against a rule entry**, as host and end to end tests: the `404`, the listings,
   the counts, the tags, `createField` with the reserved prefix in each letter case, `deleteGroup`
   of a group holding only rule entries.
5. **What each program shows and offers**: Keepass2Android and KeePassDX search and autofill with
   rule entries in a user's group; KeePassXC's search, reports, auto-type with the entry's switch
   off, and a browser extension asking by identifier.
6. **KeePassXC's window merge, its merge on reloading a changed file, and a KeeShare import**,
   which applies deletions [R keepassxc 3], over A and B.
7. **kdbxweb and KeeWeb** over A and B, for its rule that a deletion record removes whatever it
   names; **Strongbox, KeePassKit and KeePassium**, which need another platform.
8. **The narrower document against the resolver**: for every profile, verb and switch, at most
   what each document grants, with `default` and inheritance in play.
9. **A hostile file** carrying rule entries and `replaced` records for the open database, under
   each import method, in this design and in today's.
10. **Long values through each program**: the largest rule the reader accepts, 4,482 characters
    [M inventory], beside 128 predecessors.

## 14. What was run for this report

A console probe over KeePassLib 2.62 loaded from `.keepass\2.62\KeePass.exe`
made one base database with invented content (Argon2d, 2 iterations, 1 MiB) and two copies. The
base holds the KPG group, the root and 16 groups, nine of them ruled, three holding a ruled entry.
Copy A changes rules at one hour, copy B at two hours or in the same second, each the way the
editor of 4.1 would, plus what a person does by hand in A: a rule entry moved to the bin, one
deleted for good, one removed with no deletion record, a group moved, a group moved to the bin, a
group deleted for good, a ruled entry moved. The probe then merged the copies with
`PwDatabase.MergeIn` under all five methods in both directions, and `keepassxc-cli` 2.4.2, 2.6.6,
2.7.12 and 2.8.0, the copies the KeePassXC ground report downloaded and held to the spike's pinned
hashes, each with a valid Authenticode signature checked again here, ran `merge -s` in both
directions, `add` as a plain save, and its own removal and move of a rule entry. Every result was
read back by the read path of section 3, which the probe implements, and `check.py` compared what
a client holding the profile `reader` would be granted at each of the 20 nodes with the last
decision made there.

| Cells | Results | Verdicts | Wider than the last decision |
| --- | --- | --- | --- |
| KeePassLib 2.62, four methods that match identifiers, both directions | 8 | 160 | 0 |
| `keepassxc-cli`, four releases, both directions | 8 | 160 | 0 |
| a `keepassxc-cli` merge (2.4.2, 2.8.0), then KeePassLib Synchronize with the other copy | 4 | 80 | 0 |
| `keepassxc-cli` (2.4.2, 2.8.0), three merges back and forth, both files | 4 | 80 | 0 |
| a `keepassxc-cli` merge, then the rule entry in force deleted for good | 4 | 80 | 0 |
| KeePassLib 2.62, Create new IDs, both directions | 2 | 76 | 4, the open copy's own rule on its own node |

Also run: four plain KeePassXC saves with every `KPG` entry compared before and after;
`PwDatabase.DeleteDuplicateEntries` on copy B and on a copy holding a hand-made duplicate of a
rule entry; a hand edit of a rule entry's rule text and of another's title and notes; and the
cost of the pass on an in-memory database. No window was opened, no KeePass or KeePassXC window
process was started, and no test tier was run.
