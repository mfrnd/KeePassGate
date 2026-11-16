# Critique of option `rules-linked`: security, KeePass's own behavior, the operator, and the cost

Key `rules-linked-security-and-fit`. Written 2026-11-15 against `option-rules-linked.md` as it
stands, KeePassGate master `a42e38aa` (the product at `a42e38aa`) and KeePass 2.62. This
attacks the design; it does not edit it.

## How statements are marked

- **[M here]**: measured for this critique, today, by the console probe described in "What I ran".
- **[M probe]**: measured by the design's own probe, as `option-rules-linked.md` reports it.
- **[M keepass]**, **[M keepassxc]**, **[M android]**, **[M spike]**: measured in that ground
  report, or in `spikes/revocation-merge` as the ground reports digest it.
- **[R plugin]**: read in the product at `a42e38aa`, file and line given.
- **[R 2.62]**: read in the KeePass 2.62 source the `keepass` ground report verified, extracted
  under `spikes\storage\probes\ground-keepass\src`, file and line given.
- **[R keepass]**, **[R android]**, **[R inventory]**: read from source in that ground report,
  which names the tag, file and line.
- **[A]**: assumed. Neither run nor read.

## Verdict

**Viable with changes.** Under this lens there are five serious problems and five minor ones. The
reference back is a real gain and does what the design claims for the two cases the one-way critics
called unfixable: a copy of a narrowed node stays narrowed, and a rule planted for a single entry
grants nothing on its own. But the second reference is itself an entry living inside a user's group,
and three of the five serious problems are new, and measured here, precisely because of that: an
ordinary KeePass tidy-up empties the governed group and suspends its grant, an ordinary copy-paste
drags a seal into an open group and closes it, and the one piece of evidence the tab offers the
operator for the **Confirm** press is exactly what a merged file can forge. None of the five is
fatal: for every one, the narrowing direction still holds and the failure is a suspended grant, a
refusal, or a plant that needs the change the design already names (the MAC of its section 13). So
the option can meet the survival requirement, but not as written.

What the design gets right under this lens, briefly:

- **A copy of a narrowed or sealed node stays closed**, in KeePass's Duplicate Entry, Duplicate
  Group, paste and Create new IDs, which is the `rules-central` critic's unfixable S4
  [M probe 5.1, 09; M here, paste and dedup]. The one-way designs could not do this.
- **A rule planted for one entry grants nothing** until the node refers back [M probe 5.5], where
  the central design's planted entry rule was in force.
- **Through the API alone a client cannot read, write, strip or plant a back reference or a rule**,
  given the guard of section 9 (Lens (a) below), which is new code.
- **Nothing rides a node's modification time**: the value on the node never changes, so the
  rollback the `rules-on-node` design calls ordinary cannot happen to a rule [M probe 04].

## What I ran

A console probe over KeePass 2.62's KeePassLib, in .NET Framework 4.8, no window, invented content,
loaded from a copy of `.keepass\2.62\KeePass.exe` whose SHA-256 matched the
original (`AFB89B74…C89C3F3`). It reuses the design's own model verbatim: `probe\Model.cs` is the
design's probe (`spikes\storage\probes\rules-linked\design\probe\Program.cs`) with only `partial` and a renamed
`Main`, so the read path, the states and the resolve walk it measures are the design as its author
implemented them. On top of it, `probe\Mine.cs` adds six cases. Sources and output:
`spikes\storage\probes\rules-linked\security-and-fit\probe\{Model.cs,Mine.cs}`,
`...\out\{probe.txt,dedup.txt}`. `keepassxc-cli` 2.8.0 and 2.4.2 were the only other programs
started, from the copies the `keepassxc` ground report extracted; both carried a valid Authenticode
signature from DroidMonkey Apps, LLC, checked again before the run.

The six cases: a foreign file that plants a rule for an entry and supplies the history item the tab
would present as confirming it (`plant-confirm`); a group rule change applied from inside a save's
progress callback (`midsave`); Ctrl+A, copy, paste of a sealed group's entries into an open group
(`paste`); a ruled templates group whose link is picked from the template menu (`template`); a
ruled entry and a ruled group copied into a second KeePassGate database (`otherdb`); a ruled group
removed the way the API's permanent path removes a group, then synchronized (`delgroup`); and
Duplicate Group followed by Delete Duplicate Entries (`dedup`).

## Problems, most severe first

### S1. Delete Duplicate Entries, after a group is duplicated, deletes the governed group's own link and empties it (serious)

The design concedes this as an assumption drawn from scenario h16: "a group's grant suspended by
... Delete Duplicate Entries after a group was copied by a drag, which can delete the original
link [A, from h16: the same call, not run for a link]" (section 12). I ran it [M here, `dedup.txt`].
It is worse than suspended.

1. The operator has group `U` opened to `agent` (read, list), holding its link and entries `X`,
   `Y`, `Z`. They duplicate `U` to start a second copy, which `PwGroup.Duplicate` makes beside it
   under the same parent, with new identifiers throughout and the link's `KPG.Anchor` still `U`'s
   [M probe 5.1; R 2.62 `MainForm.cs:2313-2342`].
2. Later they run **Delete Duplicate Entries** (Tools, one of the operations CLAUDE.md lists under
   `UIBlockInteraction`). It compares entries by their standard and custom strings and attachments,
   ignoring identifier, group and times, and deletes the older of any pair, preferring one in the
   bin [R 2.62 `PwDatabase.cs:1969-2092`; R keepass 9].
3. `U`'s link and `U - Copy`'s link carry the same strings (`KPG.Kind` `link`, the same
   `KPG.Anchor`), so they are a duplicate pair and the older, `U`'s own link, is deleted. `X`, `Y`,
   `Z` and their copies are duplicate pairs too, and the originals go. **`U` is left holding zero
   entries, its link gone** [M here: "Delete Duplicate Entries deleted 4; U still holds 0 entries,
   its own link gone"].
4. `U` now refers to nothing, while the rule version still names `U` as its home: state 4. `U`'s
   grant is a limit, so it grants nothing. Every entry that was in `U` is in `U - Copy`, where the
   copied link makes `U - Copy` a copy and limits it too. From the next request `agent` reaches
   nothing it used to reach in `U` [M here: "U:unconfirmed", `Y@U - Copy=-`, `Z@U - Copy=-`].

The entry loss in step 3 is KeePass's own and would happen to any design; what this option adds is
the deletion of the link, which turns an ordinary tidy-up into a suspended grant with the cause
(the identical-looking link entries) invisible on any node. And it runs on the plugin's own save as
well, since the writer saves through `MainForm.SaveDatabase` and the duplicate-UUID and
duplicate-entry machinery is KeePass's, not the plugin's [R inventory P11, P13].

**Change.** Give the link a string no other link can share so it is never a duplicate of another
link: its own anchor already differs per group, and it is already a custom string, but Delete
Duplicate Entries compares custom strings by value, so two links of two groups still differ (their
anchors differ) while a link and its own copy do not. Nothing inside the option stops a link being
deleted as a duplicate of its own copy, because a copy is defined to carry the same anchor. The
honest fix is the one the central critic reached for in its S4: the plugin follows KeePass's own
copy commands and writes a fresh anchor and version for each copied group at once, so the copy's
link is no longer identical to the original's. That is the reflection-on-menu-items cost section 13
of the central critique priced, and the design's section 3.3 row 5 deliberately avoids it.

### S2. The one evidence the tab offers for Confirm is exactly what a merged file can plant (serious)

The design knows history cannot be trusted and says so: "a merged file can put an older version of
an entry into the open entry's history ... so a foreign file could supply both a rule and the
history item that confirms it, knowing nothing but the entry's identifier" (section 2.2). It then
builds the **Confirm** press around showing the operator that very thing: the tab shows "whether the
entry's history holds the reference" beside the rule, with Confirm underneath (section 4.3, first
row). A foreign file controls both sides of that display.

1. Someone outside builds a `.kdbx` under a password of their own holding, for entry `Q` (which
   sits under a sealed group and has no rule) and for entry `Y`: an intact rule version granting
   `agent` read, list, protected, a matching record, and an **older** copy of the entry itself
   carrying the planted anchor as its `KPG.Anchor`. `Q`'s and `Y`'s identifiers are what the API
   returns; the two plugin group identifiers are fixed and public.
2. The operator merges it, by Synchronize, Keep existing or Overwrite if newer, or either KeePassXC
   merge. The victim's current entry is newer and wins whole, so the planted anchor is **not** on
   the current entry; it lands in the entry's **history** [M here: "Q's history carries it True" in
   5 of 6 methods and both KeePassXC merges]. `Q` and `Y` read as state 4 (not confirmed): the
   planted rule is a limit and grants nothing [M here: "Q - [unconfirmed]", "Y list,read
   [unconfirmed]"]. So far the reference back has held.
3. The operator opens `Q`'s KPG tab. It shows a rule granting read, list, protected, says the rule
   is not confirmed by the entry, and says the entry's history holds the reference. That last line
   reads as corroboration: the entry itself once pointed here. They press **Confirm**.
4. Confirm writes the planted anchor onto the entry. `Q` is now state 1, confirmed, granting read,
   list and protected over every entry under the seal, with the victim's own password intact
   underneath [M here: "after Confirm: Q list,protected,read [agree] (password the victim's True)"].

The merge step is what the `rules-central` critic called path B, closed here for a rule alone; the
new surface is the **history as decision support**. The design offers no other evidence for Confirm,
and the only honest one it could offer (the record, the version) is what the attacker planted too.
Measured in 5 of 6 KeePassLib methods and both KeePassXC releases; under Overwrite existing the
planted entry is taken whole instead and the rule is confirmed with no press at all (M4 below).

**Change.** Confirm must not present the history as evidence, and nothing the attacker can place in
the file may stand in for the operator's knowledge. The press has to mean "I, the operator, assert
this node's rule," started from a rule the operator reads and not from the planted text: the same
footing as the central critic's S2 change. Short of that, the MAC of section 13 closes it, since a
planted version then does not even limit and is not shown as a candidate at all.

### S3. An ordinary copy or duplicate of a sealed group's contents closes the destination group (serious)

A group's reference is an entry that lives among the group's own entries (section 2.3). KeePass's
Copy and Paste Entries, and a copying drag, carry whichever entries the operator selected, with new
identifiers [R 2.62 `EntryUtil.cs:258-279`; `KdbxFile.WriteEntries`/`ReadEntries`]. A link selected
with Ctrl+A travels with them.

1. The operator has group `U` opened to `agent` (read, list) and a sealed group `S`. They select
   everything in `S` (its link and its entries), copy, and paste into `U`, a plain reorganizing
   gesture.
2. The pasted link gets a new identifier and keeps `S`'s anchor, so its identifier is no longer its
   anchor. The resolver reads a link whose identifier is not its anchor as a copy, which limits the
   group it is in (section 3.3 row 5; the "moved link, ignored" rule needs identifier equal to
   anchor, which a paste breaks). `S`'s rule is a seal, `default` deny, so the limit denies.
3. **`U` grants nothing from the next request on** [M here, `paste`: `Y@U=-`, `Z@U=-`, where before
   the paste they were `list,read`]. Nothing on any node says why; the cause is one of the entries
   the operator pasted, which every listing hides.

This is per the design's own read path, run against the design's own model. It is not a plant: it is
the most ordinary thing a person does with a tree, and it suspends a grant with no visible cause.
The same shape fires through KeePass's Add Entry from a template (M1). Fail-closed, so no client
reaches more, but the operator loses a grant by moving entries around.

**Change.** A link must be told from an ordinary entry by more than its identifier matching its
anchor, since copying breaks that match. The link could carry the governing group's identifier
(`KPG.Node`) as the record does, and a link whose `KPG.Node` is not the group it sits in is a copy
that is ignored where it landed rather than a limit, which matches the intent of row 9 and extends
it to a copied link. Even then, a paste that lands a link in a genuinely different group that the
link does claim would still limit; the real fix is the central critic's, hooking KeePass's copy
commands so the plugin writes a correct reference for the copy.

### S4. The rule writes run outside the one guard the entry path has, and a write inside a save makes the file disagree with memory (serious)

The design applies a rule write "on KeePass's thread, with no save" at `FormClosed`, and for Add
Entry "before KeePass puts the entry into its group" (section 4.1). Only the entry path is given a
busy test, through `EntrySaving` (section 4.5). A rule change adds a version to the rules group and
a record to the store group and deletes the replaced version, and a first rule also adds the link to
a user's group. Those are writes to two or three `PwObjectList`s that the group dialog's own OK
does not cover and that nothing in the design routes through the writer's idle check
[R plugin `KeePassGateExt.cs:439-453` is where that check lives today].

I applied a group rule change from inside the save's `SetProgress` callback, which is where a
dialog's OK handled during `MainForm.SaveDatabase` lands, since the save pumps messages through
`UIUtil.DoEventsByTime` on every entry written [R 2.62 `KdbxFile.Write.cs:404-417`,
`StatusBarLogger.cs:97-113`]:

- With the plugin's groups written last in the tree, the new version reached the file and memory and
  file agreed [M here, `midsave-last`].
- With the plugin's groups written first, the serializer had already written the old version by the
  time the change ran, so **the file holds the old, wider rule while memory holds the new, narrower
  one** [M here, `midsave-first`: "the file reopened: Y list,read" against "in memory: Y -"]. The
  save completed without error in both, and cleared `Modified`, so the write believes it landed.

So the outcome depends on tree order, and in one ordering a narrowing is in memory and not on disk.
A second gate on a synchronized copy, or this gate after a crash before the next save, then serves
the wider rule. The marker critique rated the same class serious as its S2; here it is widened,
because a rule write now touches more collections, so a write landing while `KdbxFile` enumerates
one of them can also throw the save (not reproduced, since my trigger fired between collections;
assumed from `List<T>`'s versioned enumerator, as the marker critique assumed).

**Change.** Every rule write (entry, group, and the Add Entry post) goes through the writer's idle
check and is saved and read back like any other write, never applied inside a serialization. For Add
Entry, write the version and record before KeePass adds the entry, into the group `GroupDialogs`
records as the parent, as the marker critique's S2 change says.

### S5. A group's rule can still be planted from both sides (serious; the design concedes it; closable only with the section 13 MAC)

The design states this: "A group's rule can still be planted from both sides by a foreign file that
the operator merges with a method that matches identifiers, because both of a group's references are
entries and entries are what merges add" (section 1), measured in its 5.5. A foreign file holding a
rule version, a record and a link entry inside a group carrying the victim group's identifier opens
that group, including a group below a seal that has no rule of its own, under Synchronize, Keep
existing and Overwrite existing [M probe 5.5]. The identifiers it needs are the victim group's UUID
and the two fixed plugin group UUIDs, all public. This is the `rules-central` critic's S1 for the
group case, not closed by the reference back, because both of the group's references are forgeable
entries. It widens only a node that has no rule of its own, which under nearest-wins is every node
below a seal.

**Change.** Section 13's MAC over every version under a secret the database holds once, which a
foreign file cannot supply. A version that fails the MAC does not limit and is not shown. The price
is one more secret, a rule for its loss, and re-signing at Recreate; the design prices it and keeps
it separable. Without it, the operator's rule that "a malicious `.kdbx` vouches only for itself"
(CLAUDE.md, E9 [R inventory]) does not hold for a group's rule.

### M1. The plugin's link entries appear in KeePass's template menu, and an entry made from one is hidden and closes its group (minor)

If a ruled group is also the templates group (the operator points `EntryTemplatesGroup` at it), its
link entry is listed in the Add Entry template drop-down beside the real templates, because that
menu lists every entry of the templates group [R 2.62 `EntryTemplates.cs:231-241`]. Picking it makes
a new entry by `PwEntry.Duplicate` [R 2.62 `EntryTemplates.cs:263-267`], so the new entry carries
`KPG.Kind` `link` and a `KPG.Anchor`. I ran it [M here, `template`]: the new entry "bank login",
which the operator fills with a real password, carries `KPG.Kind` `link`, so the design's guard
leaves it out of every route (a real credential now invisible to the gate), and because it is a link
whose identifier is not its anchor it limits the group it is made in, dropping that group's grant
[M here: `Y@U=-`, `Z@U=-` after the entry is made in `U`]. Minor because it needs the templates
group to be a ruled group. **Change:** the template menu is KeePass's; the plugin cannot filter it,
so the defense is S3's, a link told apart by more than its identifier, plus the KPG tab warning on a
ruled group set as the templates group.

### M2. Copying a ruled node into another KeePassGate database makes it refuse there (minor)

The rule version lives in the source database's rules group. Copy a ruled entry, or a ruled group,
into a second KeePassGate database and the version does not come with it, so the node refers to a
rule that is not in that database: state 2, refuse, and for a copied group, refuse everything under
it [M here, `otherdb`: the copied entry and everything under the copied group `REFUSE`]. Fail-closed,
so no widening, but an operator who reorganizes across two of their own databases closes whole
subtrees for every client in the destination with nothing to say why but the node's own tab. The
one-way `rules-on-node` design, where the rule is on the node, would carry it across. **Change:**
the destination's KPG tab lists such a node as a copy with no rule here, offering Adopt, which the
design already has for copies within one database; it has to reach copies from outside too.

### M3. deleteGroup must record the link's deletion too, or a synchronize brings the group back with its grant (minor)

The design makes `deleteGroup` treat a group holding only its link as empty and says "the link goes
where the group goes" (section 9 point 4). The current permanent-delete path writes one deletion
record, for the group [R plugin `KdbxVault.cs:555-557`]. I removed a ruled group that way and
synchronized [M here, `delgroup`]: with only the group's record, the group **comes back holding its
link** in both directions, because a tombstone is dropped for a non-empty group and the other copy's
group still holds the link [R 2.62 `PwDatabase.cs:1101-1108`]; with the link's record written too,
the group is gone. A granting group that comes back brings its grant back with it, confirmed by its
returning link. Fail-closed is not the question here: a deleted granting group resurrecting is a
re-widening. And deletions do not travel through KeePassXC or the Android merges at all
[R keepassxc, R android 3.1], so a granting group deleted in KeePass comes back the next time a copy
that still holds it is merged in, whatever the plugin records. **Change:** write the link's deletion
record in `deleteGroup`; accept, and state, that cross-program merges can still resurrect a deleted
granting group, as they can today.

### M4. Overwrite existing confirms a planted entry rule with no press, by taking the foreign entry whole (minor)

The design assumes this case and does not run it: "bringing that means bringing the entry whole and
newer, which replaces its content and exposes nothing of the operator's; that case was not run [A]"
(section 5.5). I ran it [M here, `plant-confirm`, Overwrite existing]: the foreign entry is taken
whole, so the planted anchor is on the current entry at once and the rule is confirmed with no
Confirm press, granting read, list, protected; the entry's content is the foreign file's, so the
victim's own password is replaced rather than exposed [M here: "Q carries the planted anchor True",
"Q's password is the victim's False"]. The design's claim holds: nothing of the operator's secret
leaks. Minor, because Overwrite existing is not a default merge method and the replacement is
visible as a changed entry. Worth recording as the measurement the design marked assumed.

### M5. The guard is substantial new code on every route, and the store is the most visible of the rule designs (minor)

No route touches custom data today, and no `KPG.` guard exists in the vault [M here: zero matches in
`KdbxVault.cs`]. The design adds, as new code tested against the pinned KeePass: a plugin entry
absent on every listing, the flat sweep, `FindEntry`, every count and tag; `KPG.` strings hidden and
refused on `listFields`, `readField`, `createField`, `updateField` and `stripField`, in KeePass's
own collation; and `deleteGroup`'s empty-but-for-the-link rule with the link's deletion record (M3).
The entry tag must exclude `KPG.Anchor` or an If-Match holder learns when a rule is first set; the
design's section 9 says it is not measured by any read, which has to include the tag
[R plugin `NodeTags.cs:58-66` hashes every string with its name]. And the store is a field on every
ruled entry and an entry in every ruled group, visible in every program, which the design lists as
its own cost (section 8). This is more guard surface than the central design, which hides whole
entries in one group rather than a reserved string on user entries.

## Lens (a): the caller

Given the guard of section 9 as designed, the API's own surface holds. Checked against the routes
[R plugin `Spec/openapi.yaml`]:

| attempt | through | outcome | basis |
| --- | --- | --- | --- |
| write a back reference onto an entry | `updateEntry` takes standard fields only; `updateField`/`createField` refuse `KPG.` | not possible | design 9.2; [R plugin `openapi.yaml:1216-1232`, `:1336-1401`] |
| strip a back reference to widen a node | `stripField` on a `KPG.` name is `404`, the name is hidden | not possible | design 9.2 |
| read the rule or the reference | entry, field, attachment routes; plugin entries absent, `KPG.` strings hidden | not reachable | design 9.1, 9.2 |
| read either through a field reference | the gate compiles none, and a reference returns only a standard field | not reachable | [R 2.62 `SprEngine.cs:586-606`] |
| make an entry that reads as a rule or a link | `createEntry`/`createField` draw fresh identifiers and refuse `KPG.` names | not possible | design 9.3; [R plugin `KdbxVault.cs:385`, `:603`] |
| move a node away from its rule, or into a plugin group | no move route exists | not possible | [R plugin `openapi.yaml`, every `operationId`] |
| learn node identifiers for S2 or S5 | every identifier the API returns, and references in readable fields | the input to S2, S5 | [R plugin `openapi.yaml`] |
| remove a governed group it holds `delete` on | `deleteGroup`; if the link is not recorded, the group resurrects with its grant on a later merge | M3 | [M here] |

So a prompt-injected client with some rights cannot read, write, strip or plant a reference through
the routes, which is the design's main security claim and it holds. The caller's way in is a file
the operator merges (S2, S5) or a gesture the operator makes (S1, S3, M1, M2). The guard is the
load-bearing new code; a single route that forgets it (the flat sweep, a count, the tag) reopens the
surface.

## Lens (b): secrets

A version, a record and a link hold a policy and fixed or empty standard fields, no secret. The
authority's private key stays where `option-store-entries.md` puts it, in the store group; this
design adds the records to that group, so the operator sent to inspect rules in the store group is
sent to the group holding the key, the central critic's M8, unchanged here. The policy map (profiles
and node identifiers) is in plain custom strings, in every export and on a phone's screen, which the
design lists (section 8); whoever reads an export has the passwords anyway, so this is minor.

## Lens (c): KeePass's own functions and the present guards

| function | effect | basis |
| --- | --- | --- |
| entry and group dialogs, Edit Entries, history restore | cannot change a rule's text; a real gain over custom-data storage; a first rule is written into `EntryStrings`, so KeePass asks on Escape for it | design 5.1, 4.1; [R 2.62 `PwEntryForm.cs:1074-1152`] |
| the entry dialog's close | a first rule is in `EntryStrings`, so `HasModifiedEntryEx` sees it and the question is asked correctly; a later rule change is in the tab's buffer and no longer makes KeePass ask, so Escape on a buffered rule change loses it silently unless the plugin marks the entry modified | [R 2.62 `PwEntryForm.cs:2128-2150`]; [A] for the buffered case |
| Duplicate Entry, Duplicate Group, paste, template | the copy carries the reference and the original's rule limits it (S1's gain), but the link travels too and limits or hides (S1, S3, M1) | [M here]; [M probe 5.1] |
| Delete Duplicate Entries | deletes the governed group's own link and empties it | **S1** [M here] |
| Copy and Paste Entries | drags a seal into the destination and closes it | **S3** [M here] |
| File then Synchronize or Import with a foreign file | plants a group's rule (S5); plants the history that backs Confirm (S2) | [M probe 5.5]; [M here] |
| the save that writes a rule | a rule write landing inside it diverges the file from memory | **S4** [M here] |
| Database Maintenance, Remove deleted objects | clears the deletion records a lost version relies on; a returning version then reads again, refused by `KPG.After` of the newer version, so fail-closed | design 2.5; [R keepass 5] |
| templates group pointed at a ruled group | the link is offered as a template | M1 [M here] |

**The guards.** `GroupDialogs` and the single writer stay for node writes and are unchanged for
rules, which is where S4 bites: the rule writes are new writes that need the same idle check and the
same save-and-read-back and do not have them as written. `GroupDialogs` tracks the entry dialog
already [R plugin `GroupDialogs.cs:125`], so a first rule written into an open entry is covered; a
version added to the rules group while some unrelated entry dialog is open is not a write to that
entry and is not covered, which is correct, but it is also not covered against a save, which S4 is
about.

## Lens (d): the operator

- **In KeePass**: a KPG tab with five more states and four presses, held in 449 by 314; a refusal or
  a suspended grant shown only on the node's own tab and as a count on the Profiles page; a planted
  rule (S2) shown as the node's own with history offered as evidence. The link entry and the rule
  entries show in every list, in Find, in the large-entries and last-modified reports, in print and
  in exports.
- **In other programs**: a "KeePassGate link" entry in every ruled group and a `KPG.Anchor` field on
  every ruled entry, searchable as other strings in Keepass2Android and KeePassDX [R android 4.2];
  nothing on a group says how it is ruled; one deletion or one duplicate is one gesture from a
  suspended grant.
- **What they can do wrong, in order of likelihood**: run Delete Duplicate Entries after duplicating
  a group (S1), copy entries out of a sealed group (S3), press Confirm on a node the tab says the
  history corroborates (S2), merge a backup or a colleague's file (S2, S5), delete a group of
  unfamiliar-looking entries, reorganize across two databases (M2).

## Lens (e): the cost, against the one-way designs

The reference back removes two problems the one-way critics raised and the design's section 7 lists
them fairly: a copied narrowed node stays narrowed (central S4), and a rule planted for one entry
grants nothing (central S1 for the entry case). It does **not** remove: the group plant (S5, central
S1 for the group case), which both designs share and only the MAC closes; the visibility of plugin
entries in user groups (marker's own concession), which it makes worse by adding a field to every
ruled entry as well; and the operator-facing recovery, which it enlarges from the central design's
one repair into four presses, one of them (Confirm) resting on forgeable evidence (S2). It adds, over
both one-way designs, three measured traps that come straight from the second entry living in a user
group: S1, S3 and M1. So the reference back buys the copy case and the single-entry plant at the
price of more surface in the user's own tree, and it moves the hardest problem (the group plant) not
at all. Against the custom-data store it replaces, it is strictly more code and more visible, and its
security rests on a guard that does not exist yet and must be complete on every route.

**Tests.** 27 host-test files name the rule's custom-data key or the dialog's copy [M here, a search
of `tests` of the product at `a42e38aa`], led by `GrantEditorInRealDialogsTests` (40 references),
`TagOfTheCallersViewTests` (18) and `SkippedOnlyForAReaderTests` (7), and `PendingChangesTests` (63
in the unit tier); all are rebuilt. New hazards needing tests: S1 through S5 each as measured here,
the guard on every route in KeePass's collation, the tag excluding `KPG.Anchor`, the link's deletion
record, and the four presses with an OK that did not follow a press writing nothing.

## Claims in the design this critique leans on that nobody has measured

1. The Android merges over this shape (the design's section 16 item 1, still open; `measured-android.md`
   did not exist when I read the folder).
2. That a rule write landing while `KdbxFile` enumerates the mutated group throws the save (S4),
   from `List<T>`'s versioned enumerator; my trigger fired between collections, so the throw itself
   is [A].
3. That a buffered rule change lost on Escape is silent unless the plugin marks the entry modified
   (Lens (c)); read of KeePass's close path, not driven.
4. That KeePassXC's Clone and the Android programs' copies carry `KPG.Anchor` and the link, which
   sizes S1 and S3 beyond KeePass; [A], as the design's section 16 also marks it.
5. That no program trims or normalizes a custom string's value, so a one-byte change makes a version
   displaced and a node refuse; read for KeePass and the Android writers, not run for every program.
