# Digest: `rules-marker-entry`

**design**: the option; **SC**, **SF**: its survival and security critiques; **MA**:
`measured-android.md`. **M** measured (whose probe), **R** read, **A** assumed.

## 1. What it is

Each rule becomes an ordinary entry, a rule entry, placed in the group it governs; an entry's own
rule sits beside that entry and names it. The rule text is today's document in four custom
strings, and the identifier is a hash of them, so a copied or hand-edited rule entry stops counting
and closes its group. A rule entry is never changed: a new rule adds an entry naming those it
replaces. Two add-only records, `ruled` (per node ever ruled) and `replaced` (per superseded rule
entry), make a missing rule refuse and a superseded one stay dead, and two rule entries that never
saw each other meet to the narrower. The fold runs per request and writes nothing at load.

**Covers the rules only.** Data 1 to 6 and the records' home are assumed to be `store-entries`;
database custom data cannot hold the records [M spike, per SF].

## 2. The hard requirement

For rule entries, the two records and the deletion records the fold reads. No critic's change was
run, so none is applied.

- **KeePass 2.62.** Save keeps everything, KDBX 4.0 [M design]. Ordinary edits and restored
  revisions touch no rule [R]. Deleting a rule entry refuses the node; moves keep rules in force;
  Duplicate Group's copy refuses [M design]. Duplicate Entry's copy has no rule [design; R].
  Identifier-matching merges: 160 verdicts, none wider [M design], yet P1a widens 6 merges [M SC];
  Create new IDs: 4 of 76 verdicts wider [M design]. **Fails open**; shortest: Duplicate Entry of a
  sealed entry.
- **KeePassXC 2.4.2 to 2.8.0.** Save leaves every KPG entry identical; `rm` refuses; `mv` stays in
  force [M design]. Its merge applies no deletion, so replaced rule entries return, dead by their
  record, 8 of 8 none wider, and KeePass Synchronize removes them again [M design]. P1a widens in
  2.4.2 and 2.8.0 [M SC]. Window merge, KeeShare, clone: not measured. **Fails open** (P1a).
- **Keepass2Android 1.15-r6.** MA settles every read: an entry in a user group with a JSON custom
  string (the rule entry's shape), identifiers and deletion records survived save at every KDF,
  edits (a title edit of that entry included), rename, delete, move and its merge; saves, edits and
  merges went back into KeePass exactly [M MA]. Its KDBX 3.1 output kept all of it, settling the
  design's [A] in §9; its merge adds one-sided entries, joins deletion records and kept a move;
  recycling moves the location time [M MA]. Not measured: a 4,223-character `KPG.After` (2,861
  kept), an entry's auto-type switch, an arriving entry whose group is missing. No contradiction.
  **Fails open only by P2**, which needs two plugin copies first.
- **KeePassDroid 2.7.** Offers save, entry edit and delete; no move, merge or copy [M MA]. All of
  this option's data came through those and back into KeePass [M MA]; the node custom data it
  deletes is no longer used. An unchanged Save adds history and moves times [M MA], which the fold
  ignores. **Fails open only by P2.**
- **KeePassDX 4.5.8** (not in scope). Save, edits, rename and delete went back into KeePass
  exactly; its merge applies arriving deletions without recording them and at equal times moves an
  entry to the arriving copy's group [M MA], confirming SC's P7 read (harmless) and P4's simulated
  mechanism. Recycling moves both times [M MA], closing SC's open KeePassDX branch of P2. **Fails
  open** (P4, the move undo).

## 3. Sequences that end wider, shortest first

No revocation lives here.

1. **A copy of a sealed entry** (Duplicate Entry, a KeePassXC clone, a KeePassDX copy) has no rule
   and inherits the open grant. One act [design; R]. Today the copy keeps the rule [R SF]. No change
   named.
2. **Add Entry, sealed, with auto-save after an entry edit on**: the posted rule write runs inside
   the save's message pumping and the file can lack the rule entry [R SF; A timing]. On
   rules-linked this mechanism left the file wider with `Modified` cleared [M there]. Change: rule
   writes through the writer's idle check, before KeePass adds the entry [SF]. Not run.
3. **Create new IDs**, File then Import's default, of a copy that narrowed a node: the open copy
   keeps its older rule [M design, 4 verdicts]. No change named.
4. **That import, or Duplicate Group, then the tab's Remove** on the copies: duplicates of sealed
   entries open to the root's grant [M SC]. Change: Adopt [SC]. Not run.
5. **Two deletions reach silence**, not three: the rule entry and its `ruled` record, by pykeepass
   [M SC], or in KeePass then a KeePassDX merge into an intact copy [M MA mechanism; fold simulated
   by SC]. Change: every record naming the node witnesses it [A SC].
6. **P2**: two plugin copies change one rule; the merge leaves two rule entries met to the
   narrower; deleting the narrower look-alike widens [M SC]. Each phone's recycle moves the location
   time [M MA] and KeePass Synchronize takes it [M ground-keepass]. Change: `issued` records [SC].
   Not run.
7. **P1a**: copy B repeats copy A's first narrowing after A replaced it; same identifier, so B's
   later narrowing arrives dead; 10 of 10 merges wider [M SC]. Change: a nonce [SC] or re-derive
   [SF]. Neither run.
8. **A hostile file** merged by Synchronize can replace a rule [R SF], as today; **0.1.0 rules**
   are unread, so seals stay open until re-entered [A SF].

- Shared, **a whole file written over a newer one** (K2A "No, overwrite", KeePassDX within 10 s,
  every KeePassDroid save over a changed file): one copy wins [R]. Nothing here.
- Shared, **a move undone**: KeePassDX returns an equal-time move to the arriving copy's place,
  also in KeePass [M MA]; KeePassXC regresses a move's location time [M ground-keepassxc]. A node's
  own rule survives anywhere; an unruled node moved under a seal inherits openly again. Nothing here.

## 4. Refusals in ordinary use

- **Deleting, recycling or editing a rule entry's `KPG` strings, anywhere**, closes the node or
  group [M design]; phones list and search rule entries [R], so a phone tidy-up is the likely cause.
  Remedy: Save on the node's KPG tab. **It fails for a first rule** (S1): Save rewrites the lost
  identifier, dead under its deletion record, or a duplicate that KeePass's pre-save repair answers
  with a "corrupted by a plugin" box and, with the bin earlier in the tree, renames, closing the
  group again [M SF, SC; window R]. Workaround: save another text first.
- **A copied rule entry**: refuses until removed; Delete Duplicate Entries removes the original
  [M SF].
- **Create new IDs or Duplicate Group**: copies and the root refuse, so one import closes the gate
  [M SC]. Remedy: close without saving.
- **The root's rule entry recycled**: everything inheriting refuses [M SC].
- **An entry moved, then its old group deleted** (also by `deleteGroup`): the entry refuses [R,
  fold]; Save meets S1.

## 5. Costs

- **Seen**: "KeePassGate rule (this group)" in each ruled group and at the root, "(entry …)" beside
  ruled entries, in KeePass's Find, reports and exports [R], KeePassXC's search and HTML export [M],
  phone lists and searches [R]. A client with `create` can plant a look-alike [R SF].
- **Authority key**: none here; `store-entries` holds it, rated serious there.
- **Growth and speed**: records never pruned; KeePassXC brings replaced rule entries back into user
  groups; a pass over every entry per request, 3.0 to 3.2 ms at 10,402 entries in a console [M
  design], not on KeePass's thread.
- **Classes**: go: `NodeDialog`'s copy and reflection, `AclDocument.CustomDataKey`. Arrive:
  `RuleEntries`, an editor buffer applied at `FormClosed`, the narrower document, a `KdbxVault`
  guard hiding `KPG.` entries. 19 host test files hold 101 references to what goes [M SF].
- **Tabs**: four new states in 449 by 314; a refusing-node count. A rule-only edit escapes KeePass's
  auto-save [R SF].

## 6. Settled decisions reversed

Grants under node `KPG` keys; absence as silence; the editor writing `m_sdCustomData`;
`NodeDialog` removing the key; step 13's reason; the `GroupForm` trap for rules; restored revisions
bringing rules back; the bin as the only thing no route reaches; a group removable only when empty.
"The plugin writes no rule of its own" is strained by the root's unasked rule entry [SF].

## 7. Verdict

The fold is sound: no program's own save or merge widens a rule by itself, and the emulator
confirmed every Android read it leaned on. It still fails the hard requirement: measured widenings
end in ordinary acts (a phone delete in P2, any merge in P1a, Create new IDs, Remove after an
import), copies escape, and all four named changes are unrun. Its critics refuted "an identifier
never comes back", "deleting is never a widening", "three deletions" and "the fold reads no time"
(the bin pointer does). And the tab cannot repair a phone's tidy-up of a first rule. Measure
first: the critics' probes rerun with the four changes; Add Entry with auto-save, and S1, in
KeePass's window; P2 end to end on the emulator; a long `KPG.After` on phones; the pass on
KeePass's thread.
