# Digest: `rules-linked`

SURV and SEC are the two critics, AND is `measured-android.md`. The design answered neither critic.
[M] measured, [R] read, [A] assumed.

## 1. What it is

Each rule is a plugin entry, a *version*, in a `KeePassGate rules` group, written once; a change adds
a new version (with a nonce) naming those it replaces, and a *record* entry in the store group
witnesses each one. Every ruled node has a fixed anchor: an entry carries it as the string
`KPG.Anchor`, a group in a *link* entry inside it. A rule whose node refers back is in force; one the
node does not refer back to (lost reference, copy, plant) is only a *limit*, taking away and never
granting; a reference to a missing or damaged rule refuses there and below. Nothing reads times,
history, custom data, deletion records or group fields, nothing is written at open or merge, and
four presses on the KPG tab settle what the gate finds wrong. **Covers the rules only**; the
database-level data stays as `option-store-entries.md` has it.

## 2. The hard requirement

- **KeePass 2.62: fails open [M].** Save, edit, 96 merge cells: nothing wider [M design]; copies
  limited at once [M design]. Fails open by S4, C1b, C1c, C2, C3c (section 3) [M critics]; no fix run.
- **KeePassXC 2.4.2 to 2.8.0: fails open [M].** Save, `edit -u`, 96 merge cells: nothing wider
  [M design]. It applies no deletion, so C3 rolls a rule back [M SURV]. Clone not run [R].
- **Keepass2Android 1.15-r6: fails closed [mixed].** Save, ordinary edit, title edit of a plugin
  entry, rename, delete, move kept every custom string, entry and plugin group [M AND 2.1, same
  shapes]; a title edit leaves a version intact, its identifier being over `KPG.` strings only
  [R probe]. Merge: newer entry whole, arriving copy at a tie, one-sided entries arrive, deletions and
  records carried [M AND 2.2]; an anchor lost to a later phone edit becomes a limit, so the grant
  waits. Back into KeePass exactly [M AND 5.1]. Not measured: copy and an entry whose group is missing
  [R], a group delete, the "overwrite" choice [R].
- **KeePassDroid 2.7: fails open [mixed], only by the shared overwrite.** Save, edit, delete kept
  everything, all carried back by Synchronize [M AND 3.2, 5.2]; an unchanged editor Save still makes a
  newer version [M AND 3.3]. No group edit or delete, move, copy or merge [M AND 3.1]. Every save over
  a changed file overwrites it [R]: older rules, or silence for nodes ruled since.
- **KeePassDX 4.5.8: fails closed [mixed].** Save, edit, rename, delete with the bin emptied, move:
  all kept [M AND 4]. Merge: newer whole, open copy at a tie, arriving deletion applied with no record,
  unchanged entries moved to the arriving copy's group at equal times [M AND 4.3]; a removed version
  is refused by its record [R design], a moved link suspends a grant. Back into KeePass exact [M AND
  5.3]. Copy not run [R]; 10 s silent overwrite [M spike], shared. 3.x not measured.

**The measurement** settles the edits the design marked [A] (kept) and confirms every Android merge
rule the design read. It **contradicts SEC M3** ("deletions do not travel through the Android
merges"): Keepass2Android's carries them, KeePassDX's applies them. It **weakens SURV C5's phone
step**: KeePassDroid cannot delete a group, and neither of the Android programs in scope empties the
bin.

## 3. Sequences that end wider, shortest first

1. **S4**: one OK of a group narrowing while KeePass's save pumps messages, plugin groups first in the
   tree: the file keeps the older, wider rule, `Modified` cleared [M SEC, console]. Fix: rule writes
   through the writer's idle check, saved and read back. Not run.
2. **Shared overwrite**: one phone save over a changed file, KeePass reopens it: older rule or silence
   [R; M spike]. No fix inside the file; the anchor adds nothing.
3. **C2, C1c**: duplicate a narrowed entry or a sealed group, delete the copy's `KPG.Anchor` or link:
   the copy gets its place's grant [M SURV]. Fix: a forward `copy` record. Not run.
4. **C1b**: drag-copy a sealed group into an open one, put a secret in the copy, Delete Duplicate
   Entries: the copy's link goes, the secret is open, nothing shown [M SURV]. Three ordinary commands.
   Fixes: forward record, or a fresh anchor at KeePass's copy commands (SEC). Not run.
5. **M3**: `deleteGroup`, then Synchronize with a copy still holding the group: it returns with its
   grant [M SEC]. Fix (record the link's deletion) **measured to work** in KeePass; KeePassXC and
   KeePassDX merges still restore it [M].
6. **C3, C3b, C3c**: the newest version and record, or both plugin groups, deleted for good, then
   `keepassxc-cli` merges an older copy, or KeePass clears deletion information and synchronizes: the
   older rules are in force [M SURV; M design 5.6]. Fix: the node names its newest version. Not run.
7. **h10**: version, record and reference all removed: the node inherits [M design]. No fix.
8. **Plants**, from a foreign file the operator merges: a group rule planted from both sides
   [M design]; forged history offered as Confirm's evidence (S2) [M SEC]; Overwrite existing confirming
   a planted entry rule, nothing exposed (M4) [M SEC]. Fix: a MAC over versions. Not run.
9. **Shared move**: a narrowing by moving a node undone by a KeePassDX merge at equal times [M AND
   4.3] or a KeePassXC merge [M]. Nothing done about it.

No revocation sequence of its own.

## 4. Refusals in ordinary use

All are handled on the node's KPG tab in KeePass.

- **Grant waits**: a first opening rule, then the entry edited later elsewhere (6 of 8 KeePass cells,
  every KeePassXC cell [M design], Android by the same merge rule [M AND]): Confirm, once per entry;
  with the phone's clock ahead, at every sync until KeePass's clock passes it [M SURV C4].
- **Group grant suspended**: link deleted or moved, or Delete Duplicate Entries after Duplicate Group,
  which also empties the group [M SEC S1]: Put back or Confirm.
- **Group closed, no cause shown**: a pasted link from a sealed group (S3), or an entry made from a
  link as a template, which also hides a real credential (M1) [M SEC]: Adopt or remove.
- **Copies**: an opened node's copy opens nothing until Adopt; a copy into another KeePassGate
  database refuses, no press reaching it (M2) [M SEC].
- **Subtrees refuse**: rules group deleted plus one Synchronize (C5), or a Create new IDs import plus
  Delete Duplicate Entries (C6) [M SURV]: Settle every node from nothing, retyping every rule.
- Lost version or record: Settle. Two anchors on one node: one limits for good, no press (P6).

## 5. Costs

- **Seen**: `KPG.Anchor` on every ruled entry in every program, searched on Android [R], exported;
  a "KeePassGate link" entry in every ruled group; two plugin groups.
- **Authority key**: as `store-entries`; the records share its group.
- **Growth**: two entries per rule change; records kept for good, about 250 characters each.
- **Per request**: one walk over every entry, 1.4 to 3.2 ms per 10,000 in the one-way designs [M].
- **Classes**: `AclDocument.CustomDataKey` and the `m_sdCustomData` reflection go; `RuleVersion`,
  `RuleRecord`, `RuleIndex`, `RuleStore`, resolver limits and a new `KPG.` guard on every route
  arrive. 27 host-test files are rebuilt.
- **Tabs**: five more states, four presses, in 449 by 314; Escape may lose a buffered change [A SEC].

## 6. Settled decisions it reverses

Grants in node custom data; the editor writing `m_sdCustomData`; "the plugin writes no rule of its
own"; a missing rule as silence; a restored revision bringing its rule back; a copy carrying its rules
(now its limits only); no route hiding inside an entry; `deleteGroup` refusing a non-empty group.

## 7. Verdict

The core holds: rule text sits in entries nothing changes and the node's one value never changes, so
every save, and every edit, delete and move of ordinary entries measured, keeps every datum, and every
ordinary merge ends in the rule, the narrower, a limit or a refusal. As written it does **not** meet the hard
requirement: KeePass alone opens a copy with three ordinary commands (C1b) and can file a wider rule
(S4); KeePassXC rolls rules back after a tidy-up (C3); KeePassDroid's overwrite defeats it as it does
every in-file option. Only M3's fix was run. Its ordinary price is grants that wait after phone edits
and the most visible footprint. Measure first: the forward record against C1b, C1c and C2; the
newest-version witness against C3; rule writes during a save in KeePass's window; the Android merges
and copies with real versions and links; how often grants wait in phone use.
