# Digest: `grant-only`

M measured, R read, A assumed; "design", "critic S" (survival), "critic F" (security and fit) name the
probe, checked against its output files.

## 1. What it is

A rule is a row (node, profile, `CRUSDLAP` mask), and rows only add: a verb is permitted when some
node on the path to the root holds it, a switch only in the same row; no `default`, seal, empty mask
or nearest wins. A grant is an entry written once in the plugin's store group, checked under a store
key kept in the authority's document; a take-away is a withdrawal entry, counted wherever found,
witnessed again by a key `KPG.Withdrawn.<id>` in database custom data. Nothing sits on user nodes, so
a lost grant only narrows; only a lost withdrawal widens. A node is kept from a profile only by where
it sits, so a move is a policy change. An optional middle form adds a "withheld mark" that travels
with its node.

**Covers the rules only (datum 7).** Data 1 to 6 are `store-entries` unchanged; the store key rides in
its authority entry.

## 2. The hard requirement

Applied: critic F's cap fold, the only change anyone ran (line 9).

| Program | Comes through | Widens | Verdict |
| --- | --- | --- | --- |
| KeePass 2.62 | save [M design]; edits touch no record; Duplicate never wider, 0 of 4,428 [M design] | merges: lines 2, 5, 8, 10, 11; a drag (line 1) | **fails open** |
| KeePassXC 2.4.2 to 2.8.0 | save keeps every record [M design] | merge applies no deletion: lines 2, 3, 8, 10 to 13 | **fails open** |
| Keepass2Android 1.15-r6 | save, edits (a record's title too), rename, delete, Move keep every carrier the records use; an AES-KDF file returns as KDBX 3.1, losing none [M android 2.1]. Merge unites entries, applies deletion records [M android 2.2]. Back into KeePass exactly [M android 5.1]. Exact record shapes: not run | Move (line 1) [M android 2.1]; its merge carries moves [M android 2.2], so lines 8, 10, 11 [A]; "No, overwrite" (line 6) [R] | **fails open** |
| KeePassDroid 2.7 | save, edit, delete only [M android 3.1]; every carrier kept; the node custom data it deletes holds nothing here [M android 3.2]; back into KeePass whole [M android 5.2] | overwrites a changed file at every save (line 6) [R]; a no-change edit adds a version [M android 3.3], harmless if the check covers only `KPG.` strings [unmeasured] | **fails open**, overwrite only |
| KeePassDX 4.5.8 | save, edits, rename, delete, move keep every carrier; all but the move synchronized back exactly [M android 4.4, 5.3] | Merge from: lines 4, 7; deleted group recreated [R]; recycled grant put back [A] | **fails open** |

A copy, in any program, gets what its place gives, never more than its original beside it [M design,
model]; no phone copy was run.

**Where the Android measurement contradicts the sources.**

- The design's "nothing a phone program saves or merges can lose a rule in the widening direction"
  holds for records, not the tree: KeePassDX undoes a move and keeps no record of an arriving deletion
  [M android 4.3, 5.3].
- So critic S's P3 (a), a deletion record read as a withdrawal, cannot reach KeePassDX.
- Critic F cites [M android 5.1] for a Keepass2Android Move reaching KeePass: 5.1 has no move row, and
  the move its merge carried was KeePassLib's [M android 2.2]. Inference only.

## 3. Sequences that end wider

Shortest first.

| # | Sequence | Basis | Change | Run |
| --- | --- | --- | --- | --- |
| 1 | KeePass drag or Keepass2Android Move of an ungranted folder into a granted group; nothing shows it | M critic F; M android 2.1 | mark, or a rule never to narrow by a move | mark in a model only; 847 and 541 of 1,723 still wider [M design] |
| 2 | Grant deleted by hand, then a KeePassXC merge (8 of 8) or File then Import (4 of 4) of an older copy; or recycled, KeePassXC into the other copy (4 of 4) | M critic S | P3: a deletion record, a hand edit or a place outside the group withdraws | no |
| 3 | Node deleted to take it away, KeePassXC merge of an older copy | M design 14 | none | |
| 4 | Move out in the copy KeePassDX holds open, Merge from an older copy; a move it carries loses its time, and Synchronize into a copy without the move undoes it | M android 4.3, 5.3 | mark | no |
| 5 | A move, then a merge into the other copy by three of KeePass's five methods | M design 09, 10, 13 | mark | no |
| 6 | Withdrawal or move-out in KeePass, then KeePassDroid saves over that file (also Keepass2Android "No, overwrite", KeePassDX within 10 s) | R; M spike for KeePassDX | none inside the file | |
| 7 | Grant deleted in one copy, KeePassDX merge, Synchronize with a copy holding it | M android 5.3, generic entry | P3 misses it | no |
| 8 | Two copies each add one letter to one row, one a switch; any merge | M critic S, 36 of 36 | bind a switch to its grant record | no |
| 9 | One copy withdraws letters, another adds one; any merge (`U` without `P`) | M critic F, 36 cells | cap fold | yes, every cell |
| 10 | A files X under ungranted P, B grants P, merge | M critic S, 14 of 18 | mark | no |
| 11 | Two moves of one node: the later stamp wins, clock ahead included | M design 11; A clocks | mark | no |
| 12 | A moves X out, B edits X, KeePassXC merge both ways | M critic S, 4 of 4 | mark | no |
| 13 | Withdrawal entry deleted by hand, KeePassXC 2.8.0 slow-KDF merge of an older copy removes the key | M design, 1 of 8 | none; as for revocations | |
| 14 | After the upgrade, the root granted again by habit; the old seal unread | M critic F | one-off script listing narrowings | no |
| 15 | Leaked export, Recreate, one merged file: forged grants count | R | check under the served authority's key, re-signed at Recreate | no |
| 16 | KeePassDX puts a recycled grant back; a reused identifier takes a grant | A | record kind and title | no |

**Shared.** A whole file over a newer one is line 6; nothing here addresses it. A narrowing by a move
undone by a merge: here every narrowing is a move (lines 1, 4, 5, 10 to 12), so this option is worse.

## 4. Where it refuses in ordinary use

- **No sweep from the root** (critic F S3): with grants low, `listGroups`, `listEntries` and the
  client's `ROOT` defaults refuse every program, always; the operator hands each its group identifier
  until `whoami` lists granted nodes (not built).
- **Upgrade**: nothing is reached until the operator grants again, node by node. Once.
- **Grant again on the tab** after: a planted withdrawal in a merged file [M design 08]; a grant edited
  by hand (a phone title edit, if the check covers titles); Delete authority then Create; the plugin's
  group deleted; an import then Delete Duplicate Entries [M design].
- The conversion refuses a node still carrying a 0.1.0 item; the mark, a node whose mark is gone.

## 5. What it costs

- **Seen everywhere**: a `KeePassGate` group of entries titled like `KPG grant agent .R...L..
  4F0C91AA 3b9e1c`; withdrawals pile up. Nothing on user nodes unless the mark is adopted.
- **Authority key**: a protected string as in `store-entries`, shown in clear by KeePassDroid
  [R critic]; the store key beside it is load bearing (line 15).
- **Growth**: a record per tab change, unbounded; the withdrawal key keeps KDBX 4.1 and the KeeWeb defect.
- **Per request**: a fold over every entry, 4.0 to 4.8 ms for 10,070 entries in a console [M design].
- **Classes**: go `AclDocument`, `AclDefault`, `AclDocuments`, `NodeDialog`, `IGrantStore`; arrive
  `GrantRecord`, `RuleIndex`, `RuleStore`; `AclResolver` and the client's `Access` rewritten.
- **Editor**: a gray line per inherited row plus this node's own mask (critic F M1); the refuse-unnamed
  box, struck-through seal and "Discard the unreadable rules" go; writes apply at the tab buffer's OK.
  The README story, `lifecycle.gif` and four sections of `docs/access-control.md` are redone.

## 6. Settled decisions it reverses

- "Nearest wins, per profile", the empty array, `default`, "a document that will not read refuses".
- "One document per node ... Grants live under a `KPG` key"; the editor writing `m_sdCustomData`; step
  13's seal state.
- In practice, "a flat sweep is an operation on the root group"; the client's one mask per profile.
- Via `store-entries`: `CustomData` as the store, key-by-key replay. Not in CLAUDE.md: the conversion
  breaks the "no legacy handling" ruling.

## 7. Verdict

For records this is the strongest rule design measured: a lost, uncopied or displaced grant never
widens (0 of 3,175, 4,428, 8,576 [M design]). It fails the hard requirement anyway: the narrowing
moved into the tree, the weakest carrier in the file, which a Keepass2Android Move, KeePassDX and
KeePassXC merges and KeePass's own Synchronize change with nothing shown; and a hand take-away on the
phone is undone by KeePassXC and KeePass's import. Only the cap fold was run. The switch binding and P3
are cheap and unrun; for the tree only the unbuilt mark helps, and it puts references back on user
nodes. Lines 1, 8 and 10 reach even an operator who never narrows. Measure first: the count of narrowing
rules in an existing database; line 8 with switches bound; P3; a Keepass2Android Move synchronized back; the record
shapes and real HMAC through phone edits; re-signing at Recreate; the mark with a store.
