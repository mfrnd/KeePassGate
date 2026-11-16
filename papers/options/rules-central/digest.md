# Digest: `rules-central`

Sources: `option-rules-central.md` ("design"), its survival and security critiques ("survival
critic", "security critic"), `measured-android.md` ("emulator"), `ground-inventory.md`,
`ground-android.md`. **M** measured (whose probe named), **R** read from source, **A** assumed.

## 1. What it is

Nothing is written on a governed node. Each version of a node's rule is an ordinary entry in the
plugin's group `KeePassGate`, naming the node by UUID in four custom strings, written once and
never changed. Its UUID is derived from the node and the content, so a hand edit or a deletion
record is recognizable. The newest version is in force, and two newest versions give their meet.
A deleted or hand-changed newest version makes the node refuse, and a copy under a new UUID makes
the whole database answer `503 undecided`. **Rules only** (datum 7).

## 2. The hard requirement

| Program | Verdict | Basis |
| --- | --- | --- |
| KeePass 2.62 | **fails open** | Save, edits elsewhere, moves, and all four merge methods (112 cells) keep the rule, never an older one [M design probe]. Widens through the repair on any OK (section 3, item 3), through copies (Duplicate Entry or Group, drag copy, paste, template) [R 2.62], and through Maintenance's "Remove deleted objects information" [R]. |
| KeePassXC 2.4.2 to 2.8.0 | **fails open, through copies only** | Save keeps every rule entry. A merge is a union and never applies a deletion, so a deleted rule comes back, which is safe [M design probe]. It also brings back an entry the repair deleted, so the node refuses again [M survival critic]. A clone carries no rule [A]. |
| Keepass2Android 1.15-r6 | **fails closed** | Every operation it offers was run (save at every KDF, edit, title edit, rename, delete, move, merge, back into KeePass). UUIDs, custom strings (2,861 characters of JSON), flags and deletion records came back byte for byte. A merge adds entries that one copy holds and joins both copies' deletion records [M emulator]. Refuses only after a rule entry is deleted for good or a `KPG.` string is edited. |
| KeePassDroid 2.7 | **fails closed** | Offers save, edit and delete only, and keeps everything this option uses [M emulator]. Refuses only after a permanent delete inside the bin or an edit of a `KPG.` string. |
| KeePassDX 4.5.8 | **fails open** | Save, edits, rename, delete and move keep everything. A merge adds arriving entries, but **an arriving deletion removes the entry and keeps no record** [M emulator], so the older rule is served (item 4). Its copy carries no rule [R]. |

**What the emulator changed.**

- **Confirms** the survival critic's main premise, which was read and modeled: KeePassDX drops
  arriving deletion records.
- **Settles** two open points. Keepass2Android and KeePassDX merges add an entry only the arriving
  copy holds. Keepass2Android's KDBX 3.1 output keeps everything.
- **Contradicts** the design twice. A KeePassDX save left a 4.0 file at 4.0, not 4.1, which does
  not matter for rules. The design's section 13 says a loss without trace first needs a deletion
  that no program makes on its own. That is true only to the letter: a KeePassDX merge strips the
  record from a deletion a person made elsewhere.
- **Not run on Android**: the exact rule-entry shape, KeePassDX 3.x, the phones' copy commands.

## 3. Sequences that end wider, shortest first

1. **Copy a narrowed or sealed node in KeePass.** The copy falls under its parent's grant,
   passwords included [R 2.62; M security critic]. The same holds for a KeePassDX copy [R] and a
   KeePassXC clone [A]. **Change**: hook KeePass's copy commands, which is new reflection and
   leaves other programs open; or "grant low" (no narrowing below a grant). Neither run.
2. **pykeepass deletes the newest version.** No record is kept [M survival critic]. **Change**: a
   second witness. Not run.
3. **The newest version is deleted for good or retyped, and later the operator presses OK on that
   node's dialog.** The repair writes the older rule and names the lost version, so every merge
   carries the rollback into copies that kept the narrowing: four KeePassLib methods,
   `keepassxc-cli` 2.4.2 and 2.8.0 [M survival critic; the dialog step is from the design's text].
   **Change**: repair only on a Settle press, start from a seal, record a lost version in
   `KPG.Lost`. Not run.
4. **The newest version is deleted for good in copy P, and KeePassDX merges P into Q.** The older
   rule applies and nothing refuses [mechanism M emulator; chain M survival critic plus model].
   **Change**: a second witness, either an add-only database custom data key or a twin entry. Not
   run. With the key, one sequence stays open: a KeePassDX merge, then a KeePassXC merge from an
   older copy [M spike].
5. **A permanent delete, then "Remove deleted objects information".** The older version becomes
   the newest [R]. **Change**: the witness, or a warning. Not run.
6. **The operator merges a crafted file.** A Synchronize plants a version for any node whose UUID
   is known, with nothing visible [M security critic for the landing]. **Change**: a MAC keyed by
   the gate key. Not run.
7. **C1**: the Rules page's Remove, and a KeePassDX merge into a copy that changed the node later
   [mechanism M emulator; chain M survival critic plus model]. **Change**: drop Remove. Not run.
8. **C4**: the `KeePassGate` group is deleted for good, and a KeePassDX merge follows [M survival
   critic plus model]. **Change**: the witness. Not run.
9. **0.1.0**: an upgraded database, or a second machine still on 0.1.0, reads every rule as
   silence [R]. **Change**: a leftover node `KPG` item refuses. Not run.
10. **Delete Duplicate Entries after a paste** keeps the copy and deletes the original [R; A].
    **Change**: documentation only.

**Shared by every in-file option.** A whole file written over a newer one (Keepass2Android's "No,
overwrite", a KeePassDX save within 10 seconds, any KeePassDroid save over a changed file) leaves
stale rules [R; M spike]. A narrowing made by a move is undone when KeePassDX follows the arriving
copy at equal times [M emulator]. This option does nothing about either.

## 4. How it refuses in ordinary use

- **A version deleted for good, or a `KPG.` string edited.** The node and everything below it
  refuse until the operator settles them on that node's KPG tab.
- **Two versions.** The meet applies, which is narrower than either, until the operator settles
  it.
- **A rule entry copied under a new UUID** (KeePass's preselected import, a duplicate, a clone).
  The whole database answers `503` until the operator presses Remove copies on the Rules page, or
  closes without saving.
- **A node comes back after its rules were removed**, or **the store group is deleted for good**:
  the node refuses.

Ordinary edits, moves, saves and merges of user entries trigger none of these [M emulator; M
design probe].

## 5. What it costs

- **What people see.** In every program, `KPG rule <8 hex>` entries, one per version, holding
  plain custom strings. The governed nodes show nothing.
- **The authority key.** None here. If combined with `store-entries`, the same group holds the
  private key.
- **Growth.** One entry per rule change, up to about 4.7 KB, and nothing is pruned.
- **Per request.** A pass over the whole tree: 1.43 ms at 10,060 entries [M design probe,
  console].
- **Classes.** `NodeFormCustomDataField` and `NodeDialog.Working` go. `RuleVersion`, `RuleIndex`
  and `RuleStore` arrive. The vault gains a guard on every route. 174 host tests write rules as
  node custom data.
- **The grant editor** keeps a buffer and applies it at OK. Escape needs reflection (security
  critic M1).
- **The settings tab** gains a fourth page, Rules.

## 6. Settled decisions it reverses

- One document per node, under a node `KPG` key.
- The editor writing into `m_sdCustomData`.
- "The plugin writes no rule of its own."
- The trap that shaped the design, for rules.
- A copy or a template carrying its rule.
- `503 undecided` having one cause.
- Three pages on the KPG tab.
- At risk until the MAC exists: "a malicious `.kdbx` vouches only for itself."

## 7. Verdict

The rules come through every operation in the Android programs in scope [M emulator]. A deletion by
hand fails closed. The option fails open in KeePass itself and through KeePassDX merges. Even with
every named change, none of them run, copies made outside KeePass and whole-file overwrites stay
open. **Viable only with those changes, and only if the operator accepts that a copy is wider or
adopts "grant low".** Measure first:

- the emulator over the exact rule entries and chains C1, C2 and C4;
- the phones' copy commands;
- the dialogs with a Settle press in the Live tier;
- the witness through KeePassXC and KeePassDX merges;
- the pass inside the gate.
