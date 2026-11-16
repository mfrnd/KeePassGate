# Critique of option `rules-marker-entry`: security, KeePass's own behavior, the operator, the cost

Written 2026-11-15 against `option-rules-marker-entry.md` as it stood at 17:04, KeePassGate master
`a42e38aa` (the product at `a42e38aa`) and KeePass 2.62. Lens: the caller in the threat model,
where secrets show up, what KeePass's own functions and dialogs do to the data and whether the
present guards still hold, what the operator sees and can get wrong, and what it costs here.

## How statements are marked

- **[M here]**: measured by me for this critique, from a console, over databases my probe made with
  invented content. Probe:
  `spikes\storage\probes\rules-marker-entry\security-and-fit\probe\Program.cs`;
  output: `spikes\storage\probes\rules-marker-entry\security-and-fit\out\collision.txt`. The probe copies the design's
  identifier derivation, its write step and its read path verbatim from the design's own probe
  (`spikes\storage\probes\rules-marker-entry\design\probe\Program.cs`, `RuleUuid` at :72, `Save` at :170-196,
  `View` at :378-473), so what it measures is the design as its author implemented it. KeePassLib
  2.62 loaded from `.keepass\2.62\KeePass.exe`; no window, no KeePass process.
- **[M design]**: measured in the design's own run; I read its output files (`out\check.txt`,
  `out\cost.txt`).
- **[M keepass]**, **[M keepassxc]**, **[M android]**, **[M others]**, **[M spike]**: measured in
  that ground report. **[R keepass]** and so on: read from source in that ground report.
- **[R KeePass file:line]**: read by me in `KeePass-2.62-Source.zip` as the KeePass ground report
  extracted it (`spikes\storage\probes\ground-keepass\src\`). **[R review4 file:line]**: read by me under
  `src\KeePassGate\` of the product at `a42e38aa`.
- **[A]**: assumed, neither run nor read. Collected again at the end.

**How I use the ratings.** *Fatal*: no change lets the option meet the essential requirements
(E1 to E14 of `ground-inventory.md`). *Serious*: it meets them, or stops failing the operator in an
ordinary use the design presents as handled, only with the change I name. *Minor*: worth fixing,
does not decide the choice.

## The verdict

**Viable with changes.** The fold is sound where it matters most: apart from the window in S2, I
found no path by which an API client, KeePass's own functions, or the merges measured make a client
reach more than the last rule the operator set (the section "What was checked and holds"). What
breaks is the machinery around it, and it breaks on the operator's side. The content-derived identifier repeats itself exactly
when the operator restores a lost rule, so the tab's own remedy writes an entry that is dead on
arrival, or one that KeePass's pre-save repair renames into a counterfeit that closes the group
(measured). The Add Entry write is posted into a message queue that KeePass's own save pumps. And
ordinary reorganizing in KeePass closes entries nobody touched. Each has a named change; none
touches the fold itself.

| # | Rating | Problem |
| --- | --- | --- |
| S1 | serious | Restoring a lost rule on the tab writes the identifier of the rule entry that was lost: dead on arrival under its deletion record, a duplicate KeePass "repairs" into a counterfeit beside a binned one, and no way back after Delete Duplicate Entries |
| S2 | serious | The rule write runs outside every guard the plugin's own writer needed, and the Add Entry write is delivered inside KeePass's auto-save, while the tree is being serialized |
| S3 | serious | An entry's rule entry stays in the group the entry left; deleting that group, in KeePass or by `deleteGroup` through the API, closes the entry, and restoring it can close the group it moved to |
| M1 | minor | A refusing record believed by content, plus the prune, lets an imported file delete a rule entry for good |
| M2 | minor | The empty root rule entry buys nothing the resolver does not already give, and is the one plugin entry every ruled database shows at the top of its tree |
| M3 | minor | "A person sees that a group is ruled" can be forged by any client holding `create`, and the fixed title is wrong as soon as a rule entry is moved |
| M4 | minor | Every request, a single read included, now walks every entry of the tree on KeePass's thread |
| M5 | minor | 0.1.0 rules are ignored rather than read, so re-entering grants before seals widens |
| M6 | minor | KeePass's "save after modifying an entry" no longer covers an edit that changed only a rule |

## What was checked and holds

So that the design gets credit for it, and so that none of it is raised again below.

1. **No route lets a client make, name, move or address a rule entry, given the guard of section 8.**
   `updateGroup` takes `Name` and `Notes` only, `updateEntry` the standard fields and `expires`, and
   there is no move route [R review4 Spec/openapi.yaml:811-834, :1216-1232]. `createField` already
   refuses names by several rules [R review4 Spec/openapi.yaml:1363-1379]; adding the `KPG.` prefix
   there is a small change of the same kind. A created entry gets a fresh random identifier, so a
   planted copy can never read as a rule entry (the design's point 4), and SHA-256 leaves no way to
   aim at a derived identifier.
2. **The narrower of several documents is at most each of them for every profile, verb and switch.**
   The resolver answers one profile at a time and takes a switch only from the rule that grants the
   verb [R review4 Acl/AclResolver.cs:466-535]. Intersecting the verb sets per profile, sealing a
   profile only some documents name, and taking `deny` when any document denies, are each monotone
   in that walk. I found no combination that grants more than a document it was made from.
3. **The fold reads nothing other programs were measured to drop or rewrite.** No node time, no
   history item, no custom data item. That removes the whole class of failures the spike recorded
   (a stale rule under a newer time, rules deleted with no time moved) from the rules.
4. **No KeePass dialog holds a copy of a group's list of entries** [R keepass 3, 4; and the
   `store-entries` security critique, item 4], so the `GroupForm` trap no longer reaches rules, and
   a restored revision of an entry no longer brings an old rule back.
5. **The design's merge matrix stands as recorded**: 560 verdicts over merges that match identifiers,
   none wider than the last decision [M design, `out\check.txt`].
6. **Secrets: a rule entry holds none.** The design moves only the rules, and a rule is no secret
   inside the database. A field reference returns only a standard field or an identifier, never a
   custom string [the `store-entries` security critique, item 2], and a rule entry's standard fields
   are empty or fixed. Where the authority's private key shows up is decided by the store the design
   assumes for data 1 to 6 (`store-entries`), whose own critique rates that exposure serious (its
   S3). This option cannot be taken without an entry-based store for its `ruled` and `replaced`
   records: kept as database custom data they would be removed whole by a KeePassXC merge from
   2.4.2 [M spike], and a lost `ruled` record turns a lost rule entry back into silence.

## Problems

### S1. Restoring a lost rule writes the identifier of the rule entry that was lost (serious)

**The mechanism.** A rule entry's identifier is SHA-256 over `KPG/1/rule/<node>`, `KPG.After` and
the rule text (design 2.1). At a write, `KPG.After` names "every rule entry of the node that counts
and everything those name", and `none` when nothing counts (design 4.1 step 3; the design's probe,
`Save` at :180-188). When the node's rule entry has been lost, nothing counts, so `KPG.After` is
`none` again. A node's first rule also had `KPG.After` `none`. So restoring the same rule writes the
same identifier. The design's claim that "an identifier never comes back: a later rule entry of the
same node always names more predecessors" (2.1) holds only while some rule entry of the node still
counts at the time of the write. Most nodes are ruled once [A], so most restorations hit this.

The design offers exactly this restoration as the remedy: "that the node refuses because its rule
entry is gone, with **Save** putting the shown rule back" (4.3). Measured over the four ordinary ways
a rule entry is lost [M here, `out\collision.txt`]:

| How the rule entry was lost | What Save on the tab writes | What the gate then enforces |
| --- | --- | --- |
| Moved to the recycle bin (Delete in KeePass, `rm` in KeePassXC, a phone's delete, Ctrl+A then Delete in the group) | an entry with the binned entry's identifier: the database now holds two entries with one identifier | the narrow rule, until the next save. Then KeePass's repair (below) renames one of the two. With the bin made before the group, which is the usual order for any group created after the first deletion, it renames the new one: **the group closes** ("an entry here claims to be a rule and is not one"). With the bin after the group, it renames the binned one and the rule holds |
| Deleted for good (Shift+Delete, emptying the bin) | an entry whose identifier a deletion record names | **still refuses** ("every rule entry left is a replaced or deleted one"). A later KeePass merge with any copy drops the deletion record, since the object is present and newer, and the rule comes back (Synchronize, Overwrite if newer and Keep existing, both directions) [M here]; a kdbxweb merge removes the entry instead, whatever the times [R others] |
| Duplicated by hand (Duplicate Entry, copy and paste, a template), then Delete Duplicate Entries | | `DeleteDuplicateEntries` deleted **the original** and kept the copy, because it prefers the older [M here; R keepass 9]. The copy fails its identifier, so the group refuses; after the tab's Remove of the copy, Save writes the original's identifier, which a deletion record names: **still refuses** |
| Rule text edited by hand (KeePass's Advanced tab, a phone), then the tab's Remove | the removed entry's original identifier, under its deletion record | **still refuses** |

**KeePass's repair.** Before every save, `MainForm.PreSavingEx` calls `FixDuplicateUuids`
[R KeePass Forms/MainForm.cs:759, Forms/MainForm_Functions.cs:3697-3701], which shows a warning box,
"The database contains duplicate UUIDs. Such a corruption is usually caused by a plugin or a KeePass
port. Please try to find out which plugin/port is causing it and report the issue to the
corresponding developer. When closing this dialog, KeePass will fix the problem", and then gives a
fresh identifier to whichever of the two comes later in a pre-order walk
[R KeePass Forms/MainForm_Functions.cs:6548-6574; KeePassLib/PwDatabase.cs:1881-1917;
KeePassLib/PwGroup.cs:641-663, a group's entries before its subgroups]. The same check runs at open
[R KeePass Forms/MainForm_Functions.cs:2612]. The plugin's own writer saves through
`MainForm.SaveDatabase` [CLAUDE.md], so the box appears on the plugin's own save as well. The
outcome therefore depends on where the operator's recycle bin sits in the tree, and in the common
arrangement it is the worst one: the operator pressed Save to reopen a group, is told a plugin
corrupted the database, and the group is closed again. Pressing Save again writes the same
identifier again, beside the same binned entry, and the next save renames it again.

**Steps, the most ordinary case.**

1. The operator rules group `Servers` once (its first rule, `KPG.After` `none`).
2. On a phone, they delete the unfamiliar "KeePassGate rule (this group)" entry. It goes to the bin.
3. In KeePass, the KPG tab of `Servers` says the node refuses because its rule entry is gone. They
   press Save, then OK.
4. At the next save KeePass shows the corruption box and renames the new rule entry. `Servers` now
   holds an entry that claims to be a rule and is not one, and refuses for every profile its walk
   reaches. Repeating 3 repeats 4.

**What escapes it today.** Changing the rule's text to anything else gives a different identifier;
a KeePass merge happens to drop the deletion record; Database Maintenance's "remove deleted objects
information" clears every deletion record in the file, which also weakens every `ruled` and
`replaced` record whose entry is already gone. None of these is something the tab tells the
operator, and the last costs more than the fault.

**Rating.** Serious. It fails closed (no widening in any of the cases), but the one remedy the design
offers for the most likely damage either does nothing or makes KeePass blame the plugin for
corrupting the database, and the design's §7 table presents those cases as recoverable.

**Change.** Two parts, both in `RuleEntries`:

- When a rule entry of the node sits in the recycle bin and reads, the tab offers **Put back**, which
  moves that entry out, rather than **Save**, which writes a new one. The design's Put back is
  defined only for "another group" (4.3); the bin must count as one.
- Before adding a rule entry, compute its identifier and look for it among the tree's entries, the
  bin included, and the deletion records. While it is found, add it to `KPG.After` and derive again.
  A lost rule entry is then named as replaced by the one that restores it, which is also true. Pin
  both with a host test per row of the table above.

### S2. The rule write has none of the writer's guards, and the Add Entry write lands inside KeePass's save (serious)

**What the plugin learned already.** The writer does not mutate the tree when a message happens to
be delivered: "every operation KeePass takes a noticeable time over pumps the message queue while it
runs, and a write posted as a message was delivered in the middle of one", so a write waits until
neither `UIIsInteractionBlocked` nor KeePass's message count says KeePass is busy
[R review4 KeePassGateExt.cs:398-453]. The design's rule write is not a writer write: it is applied
"on KeePass's thread, with no save" at `FormClosed` (design 4.1), and for Add Entry "in a call posted
with BeginInvoke, after KeePass has put the entry in". Neither path is said to ask that question.

**The Add Entry case, read.** After the entry dialog closes with OK, KeePass adds the entry, updates
the window and, when "Automatically save after modifying an entry using the entry editing dialog" is
on, saves at once, all before returning to the message loop
[R KeePass Forms/MainForm_Functions.cs:6810-6818; the option, App/Configuration/AceApplication.cs:210].
That save writes through a status bar logger [R KeePass Forms/MainForm.cs:752-760] whose
`SetProgress` calls `UIUtil.DoEventsByTime(true)`, which is `Application.DoEvents()`
[R KeePass UI/StatusBarLogger.cs:97-113; UI/UIUtil.cs:3997-4007], and `KdbxFile` calls `SetProgress`
after every entry it writes, from inside the pre-order walk over each group's entry list
[R KeePass KeePassLib/Serialization/KdbxFile.Write.cs:404-419; KeePassLib/PwGroup.cs:641-663]. So
the call the plugin posted at `FormClosed` is delivered during the serialization. It adds a rule
entry to a group, and the `ruled` record to the KPG group, while KeePass may be enumerating those
lists with `List<T>`'s enumerator [R KeePass KeePassLib/Collections/PwObjectList.cs:53-56].

**Steps and outcomes.**

1. The operator, with that option on, adds an entry to an open group `Shared` and seals it to
   profile `agent` on its KPG tab, then OK.
2. KeePass adds the entry and starts its save. A progress step delivers the posted call.
3. If the call lands while `Shared`'s or the KPG group's entries are being written, the next step of
   the walk throws [A, from `List<T>`'s versioned enumerator], KeePass shows its save warning and the
   file is not written. If it lands before or after, **the file holds the new entry without its rule
   entry and without its `ruled` record**: in the file the entry is silence and inherits `Shared`'s
   grant [R, by the design's fold 3.3].
4. Memory holds both, so this KeePass enforces the seal. A second KeePass running the gate on a
   synchronized copy, or this one after a crash before the next save, serves the entry to `agent`
   [A for how often a second gate or a crash coincides].

Without the option, a save started by the plugin's own writer can be pumping while an entry dialog
is open, since the writer refuses only writes to the node a dialog holds; an OK pressed during that
save then applies the rule write inside the plugin's own serialization, the save fails, and the
writer's API write is answered `500` and taken back out [A for the timing; a 201 MiB database took
80.6 s to save, CLAUDE.md]. Today's rule write cannot do this: KeePass's own OK assigns the copied
dictionary to the node and touches no list.

**Rating.** Serious. Step 3's second branch writes a seal-less sealed entry to disk, which is the
only widening I found in this option, and both branches are the class of defect the writer's
`Interrupting` check exists to prevent.

**Change.** Apply every rule write through the writer's idle check, re-posting while KeePass is busy.
For Add Entry, write the rule entry and the `ruled` record at `FormClosed`, **before** KeePass adds the
entry, into the group the dialog will fill: `GroupDialogs` already records it as the entry's parent
[R review4 Bridge/GroupDialogs.cs:862-872], and the entry's identifier exists before the dialog
opens. For the instant between, the rule entry names a node not yet in the tree, which the fold
ignores. A host test drives Add Entry with the auto-save option on and reads the saved file.

### S3. Reorganizing closes entries nobody touched, and restoring them can close the destination (serious)

**The mechanism.** An entry's rule entry is "made in the group that holds the entry at the moment
the rule is saved" and stays there when the entry is moved (design 2.2). A group that is deleted
goes to the bin with everything in it [R keepass 9]. A rule entry in the bin does not count, and a
node with a `ruled` record and no counting rule entry refuses (design 3.2, 3.3; the design's probe,
`View.Document` at :456-472).

**Steps, in KeePass.**

1. The operator seals entries `prod-db` and `prod-api` in group `Old`, then later drags both into
   group `Production`. Their rule entries, "KeePassGate rule (entry 4F0C91AA)" and another, stay in
   `Old`.
2. `Old` now shows two entries the operator did not make and does not recognize. They delete `Old`.
3. Both rule entries are in the bin with `Old`, and both sealed entries refuse for every profile,
   in a group the operator never touched.
4. The tab of `prod-db` says its rule entry is gone and offers Save. Its first rule had `KPG.After`
   `none`, so Save writes the binned entry's identifier into `Production`, and if the bin precedes
   `Production` in the tree, KeePass's pre-save repair renames the new one (S1, measured there for a
   group's rule entry; the same derivation and repair here): **`Production` refuses for every
   profile whose walk reaches it**, not just at the entry.

**Steps, through the API.** The design makes `deleteGroup` treat "a group holding only rule entries
as empty" (8, point 6), which a client must be told or it can never remove a group it emptied. A
client holding `delete` on `Old` and its parent, and nothing at `Production`, empties and deletes
`Old`. The two rule entries go to the bin, and the two entries in `Production` refuse. A refusal at a
node the client has no grant at all for, caused by an operation its grant allows, is new: today
removing a group can change nothing about access elsewhere, because a rule travels with its node.

The design records both halves separately (§7: "Moves a ruled entry: its rule stays in force; the
rule entry stays behind"; "Deletes a ruled group: the rule entries go to the bin with it") and never
puts them together.

**Rating.** Serious. Fail closed throughout, but moving entries out of a group and deleting the
group is among the most ordinary things done in KeePass, the cause is invisible where the effect
shows, and the restore path from S1 can widen the damage to a whole group.

**Change.** Three, all small: `deleteGroup` answers `409` for a group holding a rule entry of a node
outside it; the plugin's next write of its own moves a rule entry that reads and sits away from its
node into the node's group (a move, not a rewrite, so no identifier changes); and S1's Put back
covers the bin. The second also retires the "Put back" state the operator would otherwise have to
find.

### M1. A refusing record believed by content, plus the prune, deletes a rule entry for good (minor)

The `replaced` record "counts wherever its entry is found ... by what its `KPG.Data` names" (2.4),
and the plugin's own writes remove "a rule entry that no longer counts because it was replaced ...
with a deletion record" (4.2). The `store-entries` security critique measured that KeePass's own
importers carry a file's custom fields into an entry by name, verbatim (its S1: a Bitwarden JSON
file through File then Import). The rule text is canonical, profiles sorted and verbs in `CRUSDLAP`
order [R review4 Acl/AclDocument.cs:248-280], so a client whose profiles are the only ones a rule
names can rebuild that rule from `getGroupAccess` and derive its rule entry's identifier [A that such
rules are common].

1. A prompt-injected agent hands the operator a file "to import these accounts", one of whose items
   carries `KPG.Kind` `replaced` and `KPG.Data` naming the rule entry in force at `Servers`.
2. After the import the rule entry no longer counts and `Servers` refuses. The planted entry carries a
   `KPG.` string, so it is absent to every route.
3. The plugin's next write of its own prunes the rule entry for good, with a deletion record.
4. Removing the planted entry restores nothing, and Save on the tab meets S1.

Fail closed; with S1 fixed, Save recovers it. **Change:** prune only on a `replaced` record whose
own identifier is the one its content derives and which sits in the KPG group; a record found
anywhere else still refuses but deletes nothing, and the tab lists it.

### M2. The empty root rule entry buys nothing, and it is the most visible plugin entry (minor)

The design writes a rule entry holding the empty document at the root with the database's first rule
(2.3), "for one measured reason": after an import with new identifiers, duplicated groups that carry
no rule entry "end their walk at a refusing root instead of inheriting". But the resolver answers
each profile at the nearest rule naming it and refuses at an unreadable document only when the
walk reaches it [R review4 Acl/AclResolver.cs:466-535], and a root that names nothing has nothing
above it to inherit from, so its answer is "no rule anywhere grants it" (`:534`). A refusing empty
root and a silent root therefore answer every client alike. When the root carries a real rule, its
own rule entry already exists, and its copy already closes the open root on such an import. The
design's measurement was of a root carrying a real rule (the probe's base gives the root `Wide`,
`Program.cs:313`), not of the empty one.

What the empty entry does cost: it is the one plugin entry every ruled database shows at the top of
its tree, in KeePass and on every phone, and its loss runs into S1 like any other [M here, S5 in
`collision.txt`: deleted for good, the root's Save writes the deleted identifier and the root still
refuses], which by the resolver's walk changes nothing for a client and everything for the operator
reading the tab. **Change:** write no rule entry at the root until the root is given a rule.

### M3. The "ruled" signal can be forged, and the fixed title lies after a move (minor)

Section 2.5 counts "a person looking at a group in any program sees that it is ruled" as a benefit.
A client holding `create` in a group can add an ordinary entry titled "KeePassGate rule (this
group)" with the same notes line: the title and notes are standard fields `createEntry` takes
[R review4 Spec/openapi.yaml:1012-1033]. In KeePass the decoy lacks icon 29 and the `KPG` strings; on a
phone, in a list, the two look the same [A]. A decoy can make the operator believe a group is
sealed when it is not, or, beside the real one, invite deleting the wrong one. And the real title,
fixed to "(this group)", is false as soon as the entry is moved: Ctrl+A and a drag carries a group's
rule entry into another group, where it still rules the first (design §7). **Change:** read the
benefit as "a person sees the plugin left something here" and not as a statement about the group;
give the title the governed node's name at write time, or drop "(this group)".

### M4. Every request walks the whole tree on KeePass's thread (minor)

Shared with the `store-entries` option and raised there as its M1. Here it changes a single
`readEntry` from a walk up one path to a pass over every entry, inside the hold on KeePass's thread
(design 3.1). 3.0 to 3.2 ms at 10,402 entries in a console process [M design, `out\cost.txt`]; not
measured on KeePass's thread. `DecisionPace` allows a client a thousand decisions at once
[CLAUDE.md], so a burst of single reads on a 50,000 entry database holds KeePass's thread for whole
seconds in sum, interleaved with the operator's clicks [A by proportion]. The design's measurement
item 3 should include that burst.

### M5. 0.1.0 rules are ignored rather than read (minor)

The design proposes no migration: a rule in node custom data "is not read at all and the operator
sets the rules again, with a database that grants nothing until they do" (12). That holds until the
operator starts. A 0.1.0 database whose root grants `agent` read and whose entry `payroll` is sealed
grants nothing after the upgrade; the operator re-creates the root grant first, and `payroll` is open
until they also re-create its seal, while the old `KPG` key sits on `payroll` unread. The operator
said 0.1.0 is unused (memory, 2026-11-08), which is what keeps this minor. **Change:** if a 0.1.0
database is ever met, refuse every node that still carries an unread `KPG` key until the operator
clears it, which is a check, not a migration.

### M6. Auto-save after an entry edit no longer covers a rule-only edit (minor)

KeePass saves after the entry dialog only when the dialog reports a modified entry
[R KeePass Forms/MainForm_Functions.cs:5800, :5814]. Today a rule edit changes the entry's custom
data, so it counts; under the design it changes nothing in the entry (design 4.1 step 3 says so), so
an operator who relies on that option has a rule change in memory only. Fold it into S2's change: a
rule write that marks the database modified saves when that option is on.

## What the operator sees and can do wrong, beyond the problems above

In KeePass: a "KeePassGate rule" entry in every ruled group, beside every ruled entry's group,
and at the root (M2), in the entry list, in Find, in the last modified and large entries reports,
in print and in exports. On a phone: the same entries in lists and in searches of custom strings
[R android 4.2]. Every one of them is one gesture from a refusal (design §7), and with S1 unfixed,
from a refusal the tab cannot lift. A person who uninstalls the plugin keeps all of them; one who
cleans them up and later reinstalls finds every ruled node refusing, and for first rules Save
collides (S1). A KeePassXC user who deletes one sees it come back at the next KeePassXC merge into
that copy [M keepassxc 2]. KeePass's trigger action "Import into active database" imports with new
identifiers by default [R keepass 9], and a trigger set to save after it [A that operators set one]
removes the design's remedy for that case ("close without saving", §7).

## Problems the design states that I confirm, and do not re-raise

- **A copy of a ruled entry has no rule** (11). Confirmed, and it is a regression: KeePass's
  `CloneDeep` copies an entry's custom data [R KeePass KeePassLib/PwEntry.cs:321-353], so today's
  Duplicate Entry carries the seal along. The design states it.
- **A hostile file merged in** (11): confirmed for a KeePass XML file imported with Synchronize,
  which can carry a rule entry under the derived identifier naming the open rule entry in
  `KPG.After`. Under the preselected Create new IDs it only closes the group it lands in.
- **The plugin's entries are in the user's groups** (11): confirmed and widened above by S1, S3
  and M2.
- **The Android cells are read, not measured** (6, 11): confirmed.

## What it costs in this code base

**Rewritten.** `Bridge\NodeDialog` loses its reason to exist as written (the working copy, the
reflection on `m_sdCustomData`, `DialogStore`, and the reasons the editor goes read only)
[R review4 Bridge/NodeDialog.cs:17-31, :83-102, :419-455]; `Ui\GrantPanelFactory` applies on
`FormClosed`; `Bridge\KdbxNodes` reads a per-request view instead of `CustomData`
[R review4 Bridge/KdbxNodes.cs:67, :72], so the view has to be built inside the same hold as the
walk and threaded to every caller of `KdbxNodes.Of`; `Bridge\DatabaseNodes` counts through the view
[R review4 Bridge/DatabaseNodes.cs:74, :89]; `Bridge\KdbxVault` gains the absence guard in
`FindEntry`, every listing and count, `createField`'s prefix, and both `deleteGroup` rules (empty
but for rule entries, and S3's `409`) [R review4 Bridge/KdbxVault.cs:489-498, :2191-2202]; the
writer's prune gains rule entries and must put them back, deletion records removed, when a write is
taken back out; a new `RuleEntries` holds the derivation, the pass and the writes, with S1's
identifier check; and the rule write goes through the writer's idle check (S2), which today sits in
`KeePassGateExt` [R review4 KeePassGateExt.cs:439-453].

**The tab.** Four new states, one of them showing two rules beside the narrower one, inside 449 by
314 with nothing scrolling, plus a KPG tab on a rule entry's own entry dialog. `GrantTabFitsTests`
grows by every state at its longest text.

**Tests.** 19 host test files hold 101 references to the rule's custom data key or the dialog's
copy [M here, a search of `tests` of the product at `a42e38aa`], among them `GrantEditorInRealDialogsTests` (39),
which tests exactly the mechanism that goes, `TagOfTheCallersViewTests` (18) and
`SkippedOnlyForAReaderTests` (7); `KeePassContractTests` pins the field that goes; the live
`LiveGrantEditorTests` and `DocumentationScreenshots` drive the editor and the tab. The design's one
line on tests names four pins and `SnapshotHazardTests`.

**Falls away.** For rules: the `GroupForm` trap, the reflection on `m_sdCustomData` and
`KeePassContract.NodeFormCustomDataField`, "a KeePass that renames it leaves the editor read only",
and the entry dialog's comparison as the reason the editor never respells a rule.

**New hazards needing new tests.** S1's four loss paths each followed by Save, through KeePass's
pre-save repair; Add Entry with auto-save on (S2); a moved entry's old group deleted in KeePass and
through `deleteGroup` (S3); every route against a rule entry, `createField` with the prefix in every
letter case, and the counts; the prune and its take-back; a planted `replaced` record outside the KPG
group (M1); the narrower document against the resolver (the design's item 8); the pass on KeePass's
thread under a `DecisionPace` burst (M4).

**Documents.** The reversals the design lists, plus `docs\access-control.md` and every screenshot of
the main window over a ruled group, which now shows rule entries in the list; the screenshots are
retaken as a whole set.

**0.1.0.** Not carried over, by the design's choice; M5 for what that leaves.

## Assumed statements in this critique

- That a rule entry added while `KdbxFile` enumerates the same group's entries makes the save throw
  (S2), from `List<T>`'s versioned enumerator; not run, since driving it needs KeePass's window.
- How often a second gate on a synchronized copy, or a crash before the next save, meets S2's
  file without the seal.
- The timing of an OK pressed during the plugin's own long save (S2, last paragraph).
- That a client's profiles are often the only ones a rule names, which is what makes M1's identifier
  derivable.
- That a decoy and a real rule entry look alike in a phone's list (M3).
- That a `DecisionPace` burst on a large database holds KeePass's thread for seconds in sum (M4), by
  proportion from a console measurement.
- Everything in S3 and S1's repair outcome as seen through KeePass's window: the fold, the repair and
  Delete Duplicate Entries were run in KeePassLib from a console, and the window's own paths were read.
