# Option: access rules in standard fields of the node they govern

Key: `rules-on-node`. Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62.

One design, the strongest form of this option that I could find. It covers datum 7, the access
rules. Data 1 to 6 and 8 are assumed to be entries in a dedicated group, here called the KPG group,
and section 2.4 names the one thing this design adds to that group.

**How each statement is marked.**

- **[M probe]**: measured for this report, by the probe in section 12, over KeePass 2.62's
  KeePassLib and `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, console only, invented databases.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in the ground report
  of that key (`ground-keepass.md`, `ground-keepassxc.md`, `ground-others.md`) or in the spike as
  `ground-measured.md` and `ground-inventory.md` record it, with the section.
- **[R ...]**: read from source, in the ground report named or, for KeePass 2.62 and the plugin, by
  me, with file and line.
- **[A]**: assumed. Every assumed statement about another program is repeated in section 13 as a
  measurement to make.

## 0. The design on one page

1. **An entry's rule is a custom string on that entry**, named `KPG`, holding today's rule document
   byte for byte: `{"default":"inherit","reader":["read","list"]}`.
2. **A group's rule is a custom string on an entry inside that group**, the group's *rule entry*:
   string `KPG.Group` holding the same document, string `KPG.Node` naming the group. A group has no
   strings and no history, and everything a group does carry (notes, tags, flags, custom data)
   merges with the group's own time, which other programs move for reasons that have nothing to do
   with the rule. An entry inside the group moves with the group, is deleted with it, and merges
   only when the rule itself changed.
3. **Every rule version has a seal**: one small entry, added once and never changed, in the KPG
   group. A seal names the node, the SHA-256 of the rule text (or `none` for "the rule was taken
   away"), and the seals it replaces. The newest seal of a node is its *head*.
4. **The rule in force at a node is the text its head seal names, wherever that text is found**: on
   the node, or failing that in the history of the entry that carries it. A node whose head text is
   nowhere, a node with two heads, a node carrying a text no seal of its own names, and a node
   carrying a text with no seal at all **refuse**, at that node and below, exactly as a document
   that will not read refuses today. A node with no seal and no text is silence, as today.
5. So **another program can roll a rule back, delete it, copy it or edit it by hand, and the gate
   either still enforces the sealed rule or refuses. It never enforces the stale, copied or edited
   text.** In every merge cell run for this report, KeePass 2.62 in four methods and both
   directions and four KeePassXC releases in both directions, the result was the head rule, the
   sealed silence, or a refusal [M probe, section 12].
6. **Nothing is written when a database is opened or merged.** Recovery from history is a read.
   The text is put back onto the node by the operator's next OK on that node's dialog and by the
   plugin's next write of that entry.
7. **The API hides and refuses the names**: `KPG` and every name beginning `KPG.` are reserved on
   every field route, and a rule entry is as unaddressable as the recycle bin.

What this buys over today: rules survive KeePassDroid and KeePassDX saves, a stale rule can no
longer win silently in any program including KeePass itself, and a lost rule refuses instead of
inheriting. What it costs: one visible entry in each ruled group, one visible field on each ruled
entry, one seal per rule change, refusals where today there is silent widening, and a copy of a
ruled entry that is refused until the operator accepts its rule.

**The honest weakness, said first.** The fail-closed property comes from the seal, not from where
the rule text sits. With the seal in place, the text on the node is the part that other programs
and people can damage, and what keeping it on the node still buys is narrow: the rule is visible
and searchable on the node in every program, it is deleted with its node, and a rule changed in
one copy is found again in the entry's own history after the usual merge. A design that kept the
text in the seal's entry would need none of the recovery in section 3. That comparison belongs to
whoever weighs the options; this report builds the on-node form as well as it can be built.

## 1. The variants, worked through

### 1.1 A rule on an entry

| Variant | What a save does | What a merge does | Verdict |
| --- | --- | --- | --- |
| **Custom string `KPG`** | Kept by KeePass 2.62, by KeePassXC in four releases with its flag [M keepassxc §6; M probe], by kdbxweb and pykeepass [M others §3]; kept by Keepass2Android, KeePassDX 4.5.8 and 3.2.0 and KeePassDroid [R android §2]. No Android program has been measured with a custom string [M spike, `ground-measured.md` §4.1] | The newer entry whole, the older version into history with its strings intact: KeePass [M keepass §1a], KeePassXC [M keepassxc §4], kdbxweb [M others §4], Keepass2Android and KeePassDX [R android §1.1] | **Chosen** |
| Entry tags | Rewritten: KeePass trims, replaces `,` and `;`, removes duplicates and sorts on every read [R keepass §2.3]; KeePassXC 2.7.12 and 2.8.0 split, sort and rejoin [M keepassxc §6] | As the string: whole entry | Rejected. Same merge as a string, a smaller alphabet, shown in every program's tag list, and the API is to serve tags (`API.md` §407, "Tags are not here yet") |
| Entry custom data (today) | Deleted by KeePassDroid [M spike] | KeePassDX puts the arriving copy's under the newer time [M spike]; kdbxweb never takes it [M others §4] | Ruled out by the survival requirement |
| Notes, an attachment | Kept | Whole entry | Rejected: Notes are the user's text; KeePassDX treats two attachments as one on 32 bits of a hash [M android §5.1] |

A custom string moves with the entry, is in every revision, and is copied by Duplicate Entry
(`PwEntry.Duplicate` is a deep clone with a new identifier [R `KeePassLib/PwEntry.cs:856-860`, 2.62]).
It is **not protected**: a rule is not a secret inside the database, an unprotected string can be
read and searched by the operator in every program, and KeePassXC's HTML export prints protected
strings in clear anyway [M keepassxc §8].

### 1.2 A rule on a group

A group carries a name, notes, an icon, an expiry, tags (KDBX 4.1), three flags, a default
auto-type sequence and custom data. None of them has a history, and all of them travel with the
group's modification time.

| Variant | Why it fails |
| --- | --- |
| Group notes | A stale copy's notes win whenever that copy's group is newer, and other programs make it newer for unrelated reasons: Keepass2Android and KeePassDX move a group's time whenever an entry in it is added, edited, moved or deleted [R android §1.4]; KeePassXC and KeePassDX give the root group the time of every save [M spike, `ground-measured.md` §3.7]; KeePass's own group dialog moves the time on every OK, changed or not [R keepass §3]. A group has no history to recover from, so with a seal every such merge would refuse the whole subtree until the operator repaired it, and at the root that is the whole database. Notes are also the user's text, edited in every program, and KeePassium reads the first line of an empty group's notes as a search query [R others §5] |
| Group tags | KDBX 4.1 [M keepass §2.1]; dropped on save by KeePassXC 2.4.2 and 2.6.6 and never merged by any KeePassXC release although the time is raised, after which a KeePass synchronize took the copy without the tag [M keepassxc §4, §9] |
| Searching and auto-type flags, expiry, icon | No room for a rule, and the flags are never merged by KeePassXC [M keepassxc §4] |
| A custom icon as a marker | Icons merge as a union [M keepass §8], but the group's pointer to one travels with the group, and "Delete unused custom icons" removes it [R keepass §8] |
| Group custom data (today) | Deleted by KeePassDroid and KeePassDX 3.2 and 3.3, never merged by KeePassXC, replaced by the arriving copy in a KeePassDX merge [M spike] |
| **A rule entry inside the group** | **Chosen.** An entry is the one unit every program keeps on save and merges as a union [M keepass §1d; M keepassxc §4; M others §4; R android §1.5]. Nothing but a change of the rule changes it, so the group's own time stops deciding anything: a rule changed in one copy survived the other copy editing an entry in the group, adding one and moving the group's time later, in KeePass and in four KeePassXC releases [M probe, scenario 08] |

One rule entry per group, edited in place, not one entry per rule version. An entry per version
would merge as a pure union, but KeePassXC never applies a deletion and brings deleted entries
back from the other copy [M keepassxc §2], so superseded versions would return to the user's group
at every KeePassXC merge. With the seal deciding which text is in force, the one edited entry is
enough, and its history is where an older copy's win is recovered from.

### 1.3 Why a seal at all

Without it this option is today's design with a better carrier: a rule in a string survives the
saves that delete custom data, and nothing more. Two failures stay:

- **Rollback is ordinary, not exotic.** One copy narrows an entry's rule; another copy, not yet
  synchronized, changes that entry's password a minute later. Every merge takes the later entry
  whole, so the wide rule is current again and the narrowing is one history item. That is true of
  KeePass's own synchronize today [M keepass §1a] and of every other merge surveyed.
- **A rule that is gone looks like a rule that never was**, and absence inherits
  (`Acl/AclDocument.cs:110-120`).

Both need something outside the node that is only ever added. That is what a revocation already
is, so the seal is the same kind of thing: an entry added once in the KPG group.

## 2. Where each datum lives

### 2.1 The rule on an entry

- Custom string **`KPG`** on the entry, not protected.
- Value: the document `AclDocument.Write` produces today, unchanged: `default` first, the profiles
  sorted, the verbs in declared order. The reader and its ceilings (31 profiles, 4482 characters
  at most [M inventory §8]) do not change.
- An entry whose rule is taken away has the string removed and a seal of `none` added (2.3).

```text
Entry "Router admin" (any group)
  Title     Router admin
  Password  ********
  KPG       {"default":"inherit","reader":["read","list"],"ops":["read","update","list","protected"]}
```

### 2.2 The rule on a group

One entry inside the group, made by the plugin when the group gets its first rule:

```text
Entry inside group "Servers"
  Title      KeePassGate rule
  Notes      Access rule of this group for KeePassGate. Change it in KeePass, on the group's KPG
             tab. Do not edit, move or delete this entry.
  KPG.Group  {"default":"deny","ops":["read","list"]}
  KPG.Node   4F0C...  (the group's identifier, 32 hex characters)
  no user name, password or URL; auto-type off for the entry; no expiry; a standard icon
```

- **Recognized by carrying a `KPG.Group` string**, not by its title or identifier. The identifier is
  random: a derived one would let two copies make "the same" entry, but a rule entry deleted and
  made again would then meet its own deletion record, which kdbxweb applies whatever the times
  [R others §3].
- **`KPG.Node` is there for two reasons.** It makes the strings of two groups' rule entries differ,
  without which KeePass's Delete Duplicate Entries deletes one of two rule entries that hold the
  same rule [M probe: 0 deleted with it, 1 deleted without it, and the group then refuses;
  M keepass §9]. And it lets the editor tell the operator where a displaced rule entry belongs.
- No password, so no password report reads it; no URL, so no browser or autofill match; auto-type
  off, so a window title cannot select it [R keepass §9; R keepassxc §8; R android §4.2]. That each
  program keeps the entry's auto-type switch through a save is [A].
- The root group's rule entry sits directly under the root.

### 2.3 The seal

One entry per rule version, in a subgroup of the KPG group that holds nothing else, so that
reading every seal is one pass over one list.

```text
Entry in KPG group / Rule seals
  Title      Rule seal 4F0C91AA
  KPG.Node   4F0C...            the group or entry it is about, 32 hex characters
  KPG.Rule   9b1d...            SHA-256 of the rule text, 64 hex characters, or the word none
  KPG.After  0A77...,C3E1...    the seals this one replaces, by identifier; empty for a first rule
```

- **Added once, never changed, never removed while its node exists.** Its identifier is the first
  16 bytes of the SHA-256 of its three strings, so the same seal made in two copies is the same
  entry, and a seal whose strings were changed afterwards no longer matches its identifier and is
  not read as a seal. That check costs a digest, so it is made for the seals of the nodes a request
  walks and not for every seal in the index.
- **Heads.** A node's heads are its seals that no other seal of that node names in `KPG.After`.
  One head is the normal state. Two heads mean two copies each changed that node's rule without
  having seen the other's change; the node refuses until the operator saves a rule there again,
  which adds a seal naming both.
- **Why a list of predecessors and not a counter or a time.** A counter would let the copy with
  more edits win over a narrowing it never saw. A time would trust a clock. Naming predecessors
  needs neither and never drops a concurrent change without the operator seeing it.
- **Size and growth.** A seal holds 144 characters of strings [M probe]. Seals grow by one per rule
  change and are pruned only with their node (4.5). Superseded seals cannot be pruned earlier: one
  that came back from a stale copy would be named by nobody and look like a second head.

### 2.4 What this needs from the KPG group

- A subgroup for seals, found the way that design finds its own groups.
- The KPG group, its subgroups and everything in them out of reach of every route, which that
  design needs for the authority anyway.
- Nothing else. Data 1 to 6 and 8 are untouched by this option.

## 3. The read path

For a node N, inside the hold the request already runs in:

1. **Carriers.** For an entry: the entry itself, string `KPG`. For a group: every direct child
   entry that carries a `KPG.Group` string.
2. **Seals** of N, from an index of the seal subgroup built once per hold.
3. **No seal.** No carrier text: silence, N inherits, as today. A carrier text: **refuse**. This is
   a copied entry, an imported tree with new identifiers, a rule typed by hand, or a node whose
   seals were deleted.
4. **Heads.** Not exactly one: **refuse**.
5. **A carrier text that no seal of N names: refuse.** Somebody edited the rule outside the editor.
6. **Head is `none`:** silence. A leftover older text of N's own is ignored.
7. **Head is a digest:** the rule in force is the text with that digest, taken from a carrier's
   current strings, or failing that from the history of a carrier. Found nowhere: **refuse**.

"Refuse" is the answer an unreadable document gives today: every profile is refused at N and under
it, the router answers the same 404 as for an absent node, and the audit line carries which of the
reasons it was. A rule that reads but names a profile the list no longer holds still grants
nothing, as today.

Three properties of this rule:

- **The digest authenticates the text**, so it does not matter where the text was found. History
  is used as a cache and never as the store: when it no longer holds the text, the node refuses.
- **No time is compared anywhere.** Not the entry's, not the group's, not a clock's.
- **Nothing is cached across requests.** The index and each group's carriers are found once per
  hold, during which KeePass's thread stands still (`Ui/IReadHold.cs`), and dropped with it.

**Cost** [M probe, in memory, KeePass 2.62, no gate around it]: reading 2000 seals into an index
by node took 1.4 ms; asking each of 20000 entries in 200 groups whether it carries `KPG.Group`,
which a full listing does once, took 2.6 ms; a digest of a rule text took 3 microseconds. Today's
read is one dictionary lookup per node, so this is dearer, and it is paid per request.

**What changes in the code.**

- `Bridge/KdbxNodes.cs`: `DocumentOf` no longer reads `CustomData`. A node answers one of three
  things: a text in force, silence, or a refusal with its reason. `IAclNode.Document` grows into
  that answer.
- `Acl/AclResolver.cs`: a refusal from the node ends the walk the way an unreadable document does.
  Nearest wins, per profile, the empty array and `default` are untouched.
- New `Acl/RuleSeal.cs` (the three strings, read strictly: a seal that will not read is no seal,
  which leaves its node with a broken chain or an unsealed text, and both refuse) and
  `Bridge/RuleSeals.cs` (the index for one hold).
- `Acl/AclDocument.cs`: `CustomDataKey` goes; the format stays.
- `Acl/IRuledNodes.cs` and the Profiles page's counts read through the same answer, and count
  refusing nodes separately.

## 4. The write path

Rules are written where they are today, on KeePass's thread, by the grant editor at a dialog's OK.
The API never writes a rule. `DatabaseWriter` stays the only place a request writes.

### 4.1 The entry dialog

KeePass's entry dialog copies the entry's strings when it opens and writes that copy back on OK
[R `KeePass/Forms/PwEntryForm.cs:231`, `:1088`, 2.62]. That copy is **public**:
`PwEntryForm.EntryStrings`, with `UpdateEntryStrings` marked "Public for plugins" and the events
`EntrySaving` and `EntrySaved` raised around the save of the entry itself [R `PwEntryForm.cs:117-118`,
`:141`, `:357-359`, `:1074-1078`, `:1148-1149`].

- **Reading**: the tab shows the rule in force (section 3), and says so when it came from history
  or when the node refuses and why.
- **Editing**: a change on the tab is written into `EntryStrings` at once, as it is written into
  `m_sdCustomData` today, so KeePass's own "was anything changed" answer includes it, and the
  Advanced tab's list is refreshed through `UpdateEntryStrings`.
- **At `EntrySaving`**: the tab writes its answer into `EntryStrings` once more. That puts back a
  rule found only in history (the repair), and it means a hand edit of the `KPG` line on the
  Advanced tab of the same dialog does not survive OK.
- **OK**: KeePass makes one revision and moves the time, as for any edit. **At `EntrySaved`** the
  plugin adds the seal, if the text now on the entry is not the head's: its `KPG.After` names the
  heads there were. Both are in memory and reach the file in the same save.
- **Cancel**: KeePass discards its copy; `EntrySaved` never fires; no seal.
- **A refusing node** is shown with what was found. For a text with no seal or a text no seal
  names, the operator can keep the found rule (OK then seals it) or change it; for two heads, any
  OK seals the rule shown after both.
- **Restore of a revision** closes the dialog without the save [R `PwEntryForm.cs:1495-1499`], after
  backing up the current entry [R `KeePassLib/PwEntry.cs:613`]. So a restored revision's rule is
  not in force unless it is the sealed one, and the sealed text is in the backup just made. Today
  a restore brings the old rule back into force (`DESIGN.md:972-973`).
- Several entries edited together and a revision opened for viewing stay read only, as today.

`Bridge/NodeDialog.cs` loses `NodeFormCustomDataField` and the reflection on `m_sdCustomData` for
entries; its store writes to `EntryStrings`. A KeePass without those public members leaves the
editor read only, as a renamed field does today.

### 4.2 The group dialog

The group dialog has no copy of the group's entries, so the tab keeps its own: what the operator
changes stays in the panel until the dialog closes.

- **OK** (`FormClosed` with `DialogResult.OK`, the way `AuthorityPanelFactory` applies the settings
  tab): the plugin edits the rule entry as KeePass edits an entry (revision first, then the string,
  then the time), or makes it for a first rule, or deletes it for good when the rule was taken
  away, and adds the seal. KeePass marks the database modified after every group dialog OK
  [R `KeePass/Forms/MainForm_Functions.cs:5830-5831`, 2.62].
- **Cancel**: the panel's copy is dropped. Nothing was written.
- Add Group puts its group into the tree before the dialog opens and takes it out on Cancel, so a
  rule for a new group is applied at OK like any other.
- **`GroupForm`'s own write-back no longer matters to a rule**: it puts back notes, tags and custom
  data, not entries [R `KeePass/Forms/GroupForm.cs:209-231`]. Its OK still moves the group's time,
  which decides nothing here.

### 4.3 The API's own writes

- Every route that changes an entry already makes a revision. In the same turn, under the writer,
  it now also puts the entry's `KPG` right when the head text was found only in history, or removes
  a leftover text when the head is `none`. This is the repair riding the plugin's own write, and it
  is what keeps a client with `update` from pushing the sealed text out of history by writing ten
  times: after 10 later edits without the repair the text was gone and the entry refused; with the
  repair in the first of them it stayed in force [M probe, "history"].
- A write that does not land is taken back out with its repair, as it is with its prunes today.
- Removing a group through the API treats a group that holds nothing but its rule entry as empty,
  and the rule entry goes where the group goes.

### 4.4 What each named class becomes

| Class | Change |
| --- | --- |
| `DatabaseWriter` | Unchanged as the single writer for requests. Gains the repair of 4.3 and the seal prune of 4.5 on its landed path. Writes no rule and no seal for a rule |
| `PendingDatabase`, `PendingStore`, `PendingChanges` | Not touched by rules. The settings tab does not edit rules. The Profiles page gains a count of refusing nodes, read only |
| `AuthorityStore` | Not touched |
| Grant editor (`GrantPanel`, `GrantPanelFactory`, `NodeDialog`, `IGrantStore`) | Entry: writes into `EntryStrings`, hooks `EntrySaving` and `EntrySaved`. Group: own buffer, applied at `FormClosed` with OK. Shows a fourth state beside local, inherited and sealed off: refusing, with the reason. `m_sdCustomData` is no longer used |
| The prune | Gains seals (4.5). Rides the plugin's own writes only, as today |
| `GroupDialogs` | Stops mattering to rules: a rule is written only from KeePass's thread as a dialog closes, and KeePass's dialogs are modal. It stays for the API's writes (an entry whose dialog is open is refused today) and for whatever the KPG group's design needs |
| `KdbxVault` | Hides the reserved strings and the rule entries (section 7); treats a rule entry as nothing when asked whether a group is empty |
| `KdbxNodes`, `AclResolver`, `AclDocument`, `IRuledNodes` | Section 3 |

### 4.5 Pruning

On the plugin's own writes, never at open: the seals of a node that is no longer in the database,
the recycle bin counted as in it. A node that comes back from a stale copy after that returns
with a text and, where the merge does not apply deletions, with its seals too; where it returns
without them it refuses, which is the closed direction.

## 5. What a save and a merge do to it, program by program

"Head or refuse" below means the outcome was the head rule in force, the sealed silence, or a
refusal, and never a stale, copied or edited text in force.

### 5.1 Saves

| Program | The `KPG` string, the rule entry, the seals | Basis |
| --- | --- | --- |
| KeePass 2.62 | Kept. KDBX 4.0 is enough for strings, entries and history | [M keepass §2.1] |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | Kept: after `keepassxc-cli add` every rule and seal read as before; the rule entry kept its time and gained empty `Notes`, `Password`, `URL`, `UserName` strings | [M probe, "a plain save"; M keepassxc §6] |
| Keepass2Android 1.15-r6 | Custom strings, their names and flags are read and written back. It prunes history to the file's limits at every load | [R android §2, §1.3]; no custom string measured |
| KeePassDX 4.5.8, 3.2.0 | Custom strings read and written back; history pruned only when that entry is edited there | [R android §2, §1.3]; not measured. 3.3.0 not read |
| KeePassDroid 2.7 | Custom strings read and written back; it kept every entry and standard string it was given | [R android §2]; [M spike, `ground-measured.md` §4.1] for entries |
| kdbxweb 2.1.1, pykeepass 4.2.0 | Strings, flags and history kept | [M others §3] |
| Strongbox, KeePassium, KeePassKit | Strings and history in their models; Strongbox trims history at every save | [R others §3] |

### 5.2 Merges of an entry that carries a rule

| Case | KeePass 2.62 | KeePassXC, four releases | Keepass2Android, KeePassDX | The gate |
| --- | --- | --- | --- | --- |
| Rule changed in one copy, an unrelated field changed later in the other | Later entry whole, the rule's version in history [M keepass §1a; M probe 01] | The same [M keepassxc §4; M probe 01] | The same, the history item keeping its strings [R android §1.1] | The sealed rule, found in history [M probe 01, 05, all methods and directions] |
| The same within one second | The arriving copy wins, the other version is nowhere [M keepass §1a] | The target wins, the other version is nowhere [M keepassxc §4] | Arriving wins (Keepass2Android), open wins (KeePassDX 4.5.8) [R android §1.1] | In force when the rule's version won, **refuses** when it lost [M probe 02] |
| Rule changed in both copies | Later entry whole; both seals arrive | The same | Both seals arrive [R android §1.5] | **Refuses**, two heads [M probe 03] |
| Rule taken away in one copy, a field changed later in the other | The old text is current again | The same | The same [R] | Silence, as sealed [M probe 04] |
| A first rule in each of two entries, one per copy | Both entries, both seals | The same | [R android §1.5] | Both in force [M probe 11] |
| KeePass import with Keep existing, when the rule's version arrives | The open entry kept, the arriving version lost [M keepass §1a] | n/a | n/a | **Refuses**: the seal arrived and the text did not [M probe 08, 11] |
| KeePass import with Create new IDs | Every node twice, the copies under new identifiers [M keepass §2.4] | n/a | n/a | The originals unchanged; each copy that carries a rule **refuses** [M probe, "CreateNewUuids"] |
| The winner already has as many newer revisions as the history limit | n/a | The losing version is never added [M keepassxc §5] | [A] | **Refuses** once the text is nowhere [A, from the rule in section 3] |

Under KeePassDX the old design lost one side's rule in both places, because the arriving custom
data was put before the history item was made [M spike, `ground-measured.md` §4.2]. Custom strings
are not touched by that step [R android §1.1], so the history item should hold the text. This is
the first thing to measure (section 13).

Two reads about where an arriving entry lands bear on the seals. Keepass2Android puts an arriving
entry whose group the open copy lacks at the root, and KeePassDX 3.2.0 drops it [R android §1.5].
Groups are merged before their entries in both [R android §1.5], so the seal subgroup is normally
there in time. A seal that lands outside it, or does not arrive, is not read, which leaves its
node with a text its seals do not name: a refusal, not a widening [A].

### 5.3 Merges of a group that carries a rule

| Case | Programs | The gate |
| --- | --- | --- |
| Rule changed in one copy; the other copy edited and added entries in the group and its group time is later | KeePass and KeePassXC took the newer rule entry and both copies' other entries [M probe 08]. Keepass2Android and KeePassDX move the group's time on such edits [R android §1.4], and merge the rule entry as any entry [R android §3.1] | The new rule in force [M probe 08] |
| The group's notes, tags, flags or custom data lost, kept from the wrong side or deleted | Any of what section 1.2 lists | Not the rule's concern any more |
| The group moved, renamed or recycled | The rule entry is inside it | Unchanged |
| The group deleted in one copy, an entry added to it in the other | KeePass keeps the group holding only the new entry and deletes the older entries [M keepass §2.2] | The rule entry is deleted with them; the group **refuses** [A, from that measurement and section 3] |

## 6. What a person sees, what they can break, and what the gate then does

### 6.1 What is visible

| Program | A ruled entry | A ruled group | Basis |
| --- | --- | --- | --- |
| KeePass | A line `KPG` among the string fields on the Advanced tab; found by Find, which searches other strings by default | An entry "KeePassGate rule" in the group's list | [R `PwEntryForm.cs:395-414`; `KeePassLib/PwDefs.cs:355`, 2.62] |
| KeePassXC | An attribute `KPG` under additional attributes; not searched unless named; printed by the HTML export | The entry in the group; its title is searched | [R keepassxc §8]; [M keepassxc §8] for the exports |
| Keepass2Android, KeePassDX | An extra field; searched as "other strings" | The entry in the group, found by search | [R android §4.2] |
| KeePassDroid | An extra field | The entry in the group | [A] |

The rule entry's Notes tell a person what it is. An operator can use any program's search to find
every node that names a profile, which custom data never allowed.

### 6.2 What a person can do by hand, and the gate's answer

| Act, in any program | State afterwards | The gate | Basis |
| --- | --- | --- | --- |
| Edit the `KPG` or `KPG.Group` text | A text no seal of that node names | **Refuses** there and below; the editor shows the found rule and offers to keep it | [M probe 06] |
| Delete the `KPG` field | Absent on the entry, the sealed text in the revision the edit made | The sealed rule stays in force from history; refuses once history has lost it. A rule is not taken away by deleting its field | [M probe 07] |
| Delete the rule entry (recycle bin) | The group has a seal and no carrier | **Refuses** at the group; moving the entry back out of the bin restores it | [A, from section 3] |
| Delete the rule entry for good | KeePass synchronize deletes it in the other copy too; a KeePassXC merge brings it back | **Refuses** after KeePass; in force after KeePassXC | [M probe 09] |
| Delete a seal for good | The same two outcomes | **Refuses** (a text and no seal) after KeePass; in force after KeePassXC | [M probe 10] |
| Delete the seal subgroup or the KPG group | Every ruled node has a text and no seal | Every ruled node **refuses**; unruled nodes inherit as before | [A, from section 3] |
| Duplicate or copy a ruled entry; make an entry from a ruled template; clone a ruled group | A text under a new identifier with no seal | The copy **refuses** until the operator opens it and keeps its rule | [M probe, "CreateNewUuids", for the same state] |
| Move a rule entry into another group | Its own group has a seal and no carrier; the other group holds a `KPG.Group` text that is not its own | Both groups **refuse**: the first for a sealed text that is nowhere, the second for a text with no seal or a text no seal of its own names. Moving the entry back restores both | section 3, steps 3, 5 and 7 |
| Restore an old revision of a ruled entry | The old text current, the sealed text in the backup | The sealed rule stays in force | [R `PwEntry.cs:613`] |
| KeePass Delete Duplicate Entries | Rule entries differ in `KPG.Node`, seals in every string | Nothing deleted | [M probe; M keepass §9] |
| KeePass Delete Empty Groups | A ruled group is never empty | The group stays | [R keepass §3] |
| Database Maintenance, delete history older than N days; history limits lowered to 0 | A rule being served from history is gone | **Refuses** | [R keepass §1b] |
| Delete both a rule and every seal of its node | No text, no seal | Silence: the node inherits. Two deliberate acts by a holder of the master key, conceded | section 3 |

In every row the gate's answer is either the rule the operator last set through the editor or a
refusal. The refusals are shown on the node's own KPG tab with their reason, counted on the
Profiles page, logged, and named in the audit line of every request they refuse.

## 7. Keeping the API and the reference client out

**The names.** `KPG` and every name beginning `KPG.` are reserved, on entries anywhere in the
database.

| Route | Behavior |
| --- | --- |
| `listFields`, `readEntry`, the entry listings, and every head beside them | A reserved string is not named, not counted, not in the size a read ceiling counts, and not in what a node's tag is made from |
| `readField`, `updateField`, `stripField` on a reserved name | `404`, the answer an absent name gets, whether or not the entry carries one |
| `createField` with a reserved name; `createEntry` or `updateEntry` whose map holds one | `400 malformed`, decided from the request alone before the entry is looked at, so the answer is the same on a ruled and an unruled entry and nothing can be learned from it |
| `updateEntry` | Carries the entry's `KPG` over untouched, and repairs it (4.3) |
| Rule entries: every listing, sweep and count; `readEntry`, `updateEntry`, `deleteEntry`, the field and attachment routes, `getEntryAccess` by their identifier | Not listed; `404`. `KdbxVault.FindEntry` already answers null for anything in the recycle bin (`Bridge/KdbxVault.cs:2191-2202`); an entry carrying `KPG.Group`, and anything in the KPG group, joins it there |
| `deleteGroup` | A group holding only its rule entry is empty |

- The reserved test is made byte for byte and with the comparer KeePass's string dictionary uses,
  for the reason `KdbxVault.Holds` and `Clashes` exist: that dictionary gives some characters no
  weight (`Bridge/KdbxVault.cs:1620-1640`). The schema already refuses every format character in a
  name.
- An entry that an ordinary user gave a field named `KPG.something` loses that field from the
  API's view. That is the price of a reserved prefix.
- **No route reads history**, so a rule in a revision is out of reach already (`API.md` §407).
- **The reference client.** `pykeepassgate.api` and `model.py` see nothing new: the fields are not
  there, which is the "absent rather than empty" rule. `pykeepassgate.refs` compiles `{S:<name>}`
  from the entry's own strings; `{S:KPG}` and any `{S:KPG.<name>}` name strings the client cannot have, so
  it refuses them, as it refuses every placeholder it cannot be sure of, and
  `ReferenceVectors.json` gains the case from KeePass, which would substitute the rule text.
  `pykeepassgate.testing` needs no model of rules, as now.
- **What the API cannot stop.** A reference by search in a user's entry, `{REF:P@O:text}`, searches
  other strings and ignores the searching switch [M keepass §9], so in KeePass it can resolve to an
  entry because of what its rule text holds. That is KeePass compiling a placeholder for the
  operator, not the gate; the client refuses searches other than by identifier. KeePass's own
  exports and print include custom strings [R keepass §9], as the XML export includes custom data
  today.

## 8. The KDBX version it needs

None beyond what KeePass writes anyway. Custom strings, entries, history and an entry's auto-type
switch are KDBX 4.0 [M keepass §2.1], and they exist in KDBX 3.1 as well [A: not measured in a
3.1 file]. Rules no longer need KDBX 4.1 or any per-key time. Whether the file is still taken to
4.1 is the KPG group design's question.

## 9. Settled decisions: reversed and kept

**Reversed, to be recorded as reversals.**

1. "Grants live under a `KPG` key" of a node's custom data: a custom string `KPG` on an entry, and
   `KPG.Group` on a rule entry inside a group.
2. "The grant editor writes into the dialog's own copy, `m_sdCustomData`": the entry dialog's
   public `EntryStrings` with `EntrySaving` and `EntrySaved`; for a group, the tab's own buffer
   applied at the dialog's OK.
3. "The plugin writes no rule of its own" (`CA.md:127-138`): it writes the group's rule entry, the
   seals, and the repair of an entry's rule inside its own write of that entry.
4. Absence is silence (`AclDocument.cs:110-120`): only where no seal exists. A third answer joins
   rule and silence, the refusal for a rule that does not match its seal.
5. A restored revision brings its rule back into force (`DESIGN.md:972-973`): it does not.
6. No route hides anything inside an entry it serves: a reserved name space, hidden and refused.
7. A copy of an entry or a group carries its rule: the copy refuses until the operator keeps it.
8. "A group that still holds something is refused" by `deleteGroup`: the rule entry is not something.
9. The `GroupForm` trap as the reason rules need care: `GroupForm` no longer holds a rule. The
   entry dialog's copy of the strings takes its place, and the editor writes into it.

**Kept.**

- One document per node, the format, nearest wins per profile, the empty array, `default`, a
  document that will not read refuses, no per-subject opt-in, `CRUSDLAP`, the verbs and switches.
- The profile list decides what a client holds; a rule naming a removed profile grants nothing.
- OK keeps a rule and Cancel discards it, on both dialogs. The editor never writes the same rules
  in other words. Nothing on a tab scrolls.
- Opening or reading a database changes nothing in it; repairs and prunes ride the plugin's own
  writes and the operator's OK.
- The router asks before any handler runs; refused and absent answer the same 404; the audit says
  which it was.
- One JSON grammar and one escaper: the rule document is unchanged, and a seal is three strings of
  hexadecimal read by a strict reader, with no JSON in it.
- `DatabaseWriter` as the single writer for requests, the save rules, the watch on dialogs for the
  API's writes.
- Local code execution as the user is conceded, and so is a holder of the master key who sets out
  to remove a rule and its seals.

## 10. What it does not solve

- **A whole-file overwrite.** Keepass2Android's "No, overwrite", KeePassDX's save over a file whose
  time moved by less than ten seconds, every KeePassDroid save over a changed file, and a file
  synchronizer keeping one of two files [R android §5.5; R others §3] put a stale copy in place
  whole, rules and seals together. Nothing inside the file can tell. This is true of every design.
- **A rule and all its seals removed together** read as never ruled.
- **Refusals where the operator did nothing wrong.** A tie within one second in the losing
  direction, a rule changed in two copies between synchronizations, the sealed text aged out of
  history before anything repaired it, a Keep existing import, a duplicated entry. Each is closed
  and each needs the operator at the node. A refusal at a group closes everything under it, and at
  the root that is the whole database. Whether that is rare enough is a question for use, not for
  a probe.
- **Clutter and exposure.** One entry per ruled group and one field per ruled entry in every
  program's view, in search results on Android, in KeePassXC's HTML export and in KeePass's print.
  A person who does not know what the entry is can delete it, and the group then refuses.
- **Cost per request**: an index of all seals and a look at each group's entries, where today there
  is one lookup per node.
- **Growth**: one seal per rule change for as long as the node lives.
- **History is not under the plugin's control.** How long a rolled back rule can be served before
  it is repaired depends on the file's history limits and on each program's trimming (Strongbox at
  every save, kdbxweb at every merge [R others §3]).
- **The entry's time still moves when its rule changes**, so a rule change in one copy can still
  win over a password change made earlier in another, putting the password change into history.
  That is KeePass's rule for any two edits and is as today.
- **The recycle bin markers of T049** are not covered and not needed.
- **Everything about data 1 to 6** is the KPG group design's.

## 11. What a migration would take

Not designed here. In outline: on the operator's press and never at open, for each node carrying
the old `KPG` custom data item, write the string or the rule entry, add a first seal and remove
the item, in one save. From then on node custom data is not read at all, so a copy that was not
converted and is merged in later brings back items that mean nothing; its nodes that were ruled
only there arrive unruled and inherit, which the operator has to be told before pressing. Whether
databases made with 0.1.0 are carried over at all is a question for the project (O7 in
`ground-inventory.md`).

## 12. The probe behind the [M probe] marks

- Source: `spikes\storage\probes\rules-on-node\design\probe\Program.cs`,
  compiled with the .NET Framework's own `csc` against a copy of
  `.keepass\2.62\KeePass.exe`. No window; `keepassxc-cli` was the only other
  program started, from the archives the `keepassxc` ground report verified, each with a valid
  signature from DroidMonkey Apps, LLC.
- Outputs, beside it under `rules-on-node\out\`: `keepass.txt`, `cost.txt`, `xc-2.4.2.txt`,
  `xc-2.6.6.txt`, `xc-2.7.12.txt`, `xc-2.8.0.txt`. Databases under `rules-on-node\work\`.
- Each scenario starts from one base file (a group U with a rule entry, entries X ruled, Y and Z
  unruled, a KPG group with the seals), makes copy A and copy B, merges in both directions, saves,
  reopens and applies the read rule of section 3, which the probe implements itself.
- KeePass: `PwDatabase.MergeIn` with Synchronize, Overwrite if newer, Overwrite existing and Keep
  existing, 11 scenarios, 88 cells, plus Create new IDs, twelve later edits against the history
  limit, and `DeleteDuplicateEntries`. KeePassXC: `merge -s`, 11 scenarios, both directions, four
  releases, 88 cells, plus a plain save in each. The four releases gave identical output.
- **Result: no cell had a rule in force other than the head's.** The cells that refused: the tie in
  the direction where the rule's version lost; two heads; a hand-edited text; the rule entry or the
  seal deleted for good, after a KeePass synchronize; Keep existing where a seal arrived without
  its text; the copies under new identifiers.
- What the probe is not: the plugin's code, KeePass's window (File then Synchronize runs the same
  `MergeIn` [R keepass, "What was run"]), or KeePassXC's window.

## 13. Measurements to make before this design could be chosen

Most important first.

1. **The Android programs, which is where the present store fails and where every statement here
   is read and none measured.** With the emulator runs `ground-android.md` §7 lays out (R1, R2, R3,
   R5, R10), over this report's base and scenarios: that KeePassDroid 2.7, KeePassDX 3.2.0, 3.3.0
   and 4.5.8 and Keepass2Android keep a custom string named `KPG` and `KPG.Group`, a value of 4482
   characters and the entry's auto-type switch through a save; that a Keepass2Android and a
   KeePassDX merge leave the losing version in history **with its strings**; what each does at a
   tie; that both copies' seals arrive.
2. **The dialogs in a real KeePass 2.62** (the Live tier): a rule written into `EntryStrings` is
   kept by OK and dropped by Cancel; `EntrySaving` and `EntrySaved` fire once per OK and not for
   the "was anything changed" check; the Advanced list refreshes; the group tab's buffer is applied
   at OK and dropped at Cancel, for Edit Group and Add Group; a restore of a revision; several
   entries edited together; Escape with only a rule changed.
3. **A real File then Synchronize in KeePass with the plugin loaded**, and a save that finds the
   file changed: that requests during and after the merge read head or refuse, and that reading an
   entry's history inside a hold is safe against `MergeWatch`.
4. **The read path inside the gate**: a listing of 20000 entries with 2000 seals and a few hundred
   ruled groups, against today's, with the hold's limit in view.
5. **Three copies and longer chains**: seals merged in every order never give a false second head;
   a node deleted, its seals pruned, and the node brought back by a KeePassXC merge.
6. **KeePassXC's window paths**: Database then Merge, the reload of a file changed on disk,
   KeeShare (the one path that applies deletions) and 2.8.0's remote sync.
7. **The lower-depth programs** over the same scenarios: Strongbox and KeePassKit merges and
   Strongbox's history trim at save, kdbxweb's merge (its history rule drops items), KeePassium's
   save.
8. **How often ordinary use refuses**: a month of two or three copies in daily use with rule
   changes, counting refusals by reason. This is the design's falsifier: if merges make refusals a
   routine event, the text does not belong on the node.
9. **What each program shows**: the rule entry and the `KPG` field on each program's screens, in
   search and in autofill, and whether a person is led to delete them.
10. **KeePass's own tools in its window**: Delete Duplicate Entries, Delete Empty Groups, Database
    Maintenance's history deletion, a template carrying a rule, copy and paste of entries.
11. **Placeholders**: `{S:KPG}` and a search reference meeting a rule text, in KeePass, and the
    client's refusal against the vector KeePass makes.
