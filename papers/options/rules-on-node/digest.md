# Digest: rules-on-node

Checked against the design, both critiques, the three probes' outputs and `measured-android.md`.
M is measured, R read from source, A assumed. "D" is the read rule as written; "V" is the survival
critic's change, which also checks a carrier's history for a text no seal names.

## 1. What it is

An entry's rule is a plain custom string `KPG` on the entry. A group's rule is a string `KPG.Group`
on a "KeePassGate rule" entry inside the group. Every rule change adds a seal entry to a seal group,
naming the node, the SHA-256 of the text (or `none`) and the seals it replaces. The rule in force is
the text the newest seal names, on the node or in the carrier's history: no seal and no text is
silence, and any other mismatch refuses at the node and below. The API hides and refuses every
`KPG` and `KPG.*` name and every rule entry. **It covers the rules only**; the database-level data
is left to a companion design.

## 2. The hard requirement

All three phone programs kept the carriers (custom strings with `KPG.` names, long JSON, entries,
history items, deletion records) through every operation they offer and back into KeePass (M
android). Not measured: the bare name `KPG`, an empty custom string (the first seal's `KPG.After`),
auto-type off on an entry, history at its limits, Keepass2Android's root landing. Two changes were
shown to work and are applied below: V (M survival C1 to C3) and never pruning seals (M survival
C4, M security S2). Items 2, 3 and 7 of section 3 stay open after both, in every program.

- **KeePass 2.62: fails open.** With seals intact, all 88 merge cells gave the head rule or a
  refusal (M design). Item 1 below widens in 6 of 8 cells (M survival C1, security S1); V closes
  it (M).
- **KeePassXC 2.4.2 to 2.8.0: fails open.** Saves and 11 merge scenarios gave the head rule or a
  refusal (M design). Item 1 applies, and so does item 5: a deleted entry whose seals were pruned
  came back with its wide rule (M security S2). Never pruning closes item 5 (M).
- **Keepass2Android 1.15-r6: fails open.** Its merge keeps the loser in history with its strings
  (M), so the recovery works. Delete asks "Delete permanently? ... Press No to recycle" (M);
  either answer gives item 1 under D. A tie loses one side outright, the arriving copy winning
  (M), and the refusal that follows is exposed to item 2.
- **KeePassDroid 2.7: fails open.** It offers save, edit and delete only (M). Delete bins with no
  question (M), giving item 1 under D at the next KeePass sync. Every editor Save adds a revision,
  even with nothing changed (M), which hastens item 7. A save over a changed file overwrites (R).
- **KeePassDX 4.5.8: fails open.** As Keepass2Android, but the open copy wins a tie (M), and an
  arriving deletion leaves no record (M), so a deleted seal can return at a later sync (M, X1),
  harmlessly (A).

**What the Android runs settled.** Both merging phone programs keep the loser's custom strings
in history (M, cell M1), which was the design's first open question. Both tie directions and the
arrival of new entries from both sides are confirmed (M). The security critic's "Keepass2Android
recycles" is corrected: it asks first, and a permanent delete leaves a record its merge carries
(M, X1). Nothing measured contradicts an Android claim of the design.

## 3. Sequences that end wider, shortest first

1. **An ordinary rollback, then one Delete of the newest seal**: the older rule, or silence for a
   first rule, in 6 of 8 cells each (M survival C1, C2, security S1; the delete M android). A seal
   landing outside the seal group does the same (root landing R; KeePassXC's foreign bin M). V
   closes it (M). Reading seals anywhere: proposed, not run.
2. **A refusing node, then any OK of its entry dialog**: the found text is sealed, whether a hand
   edit, a template, a lost tie or one of two heads (R design §4.1). Fix: seal only on an explicit
   act on the KPG tab. Not run.
3. **A copy sets the same text again after another removed it, then any merge**: the later
   narrowing is dropped, 8 of 8 (M survival C5). Fix: a nonce per seal. Not run: the critic's
   verdict calls it measured, but its output has no nonce variant.
4. **A group deleted for good, its seals pruned with records, an entry added on a phone, a
   KeePass merge**: the group returns unruled, 5 of 8 (M survival C4). Pruning without records, or
   never, closes it (M).
5. **An entry narrowed, deleted, its seals pruned, then a KeePassXC merge of a stale copy**: the
   wide rule is back (M security S2). Never pruning closes it (M).
6. **`KPG` deleted on a phone, then the seal group deleted**: silence (M survival C3). V closes
   it (M).
7. **Residual after V**: a lost newest seal, with the sealed text gone from history (10 edits,
   trimming, Maintenance, a limit of 0): the older rule (A, composed from M). A repair at
   `FileSavingPre` would narrow the window. Not run.
8. **A 0.1.0 database under 0.2.0**: every rule reads as silence (R security P6). Fix: read an old
   item as a refusal. Not run.
9. **Planted seals in a file synchronized in** rule an unruled group (A security P8). Not designed.
10. **Shared: a whole file over a newer one** (Keepass2Android "No, overwrite", KeePassDX within
    10 s, any KeePassDroid save over a changed file) restores old rules and seals (R android §5.5).
    This option does nothing.
11. **Shared: a move undone by a merge**: KeePassDX put a moved entry back, and KeePass kept that
    (M android, Y3); KeePassXC too (M keepassxc §4). This option does nothing.

## 4. Refusals in ordinary use

A refusal covers the node and its subtree, and at the root every root-anchored listing. The
operator repairs it on that node's KPG tab in KeePass, which shows the reason. Frequency is not
measured; the design calls it its own falsifier.

- A tie in the losing direction (M design 02; M android).
- A rule changed in two copies between syncs (M design 03).
- The sealed text aged out of history (M design), or never kept (large attachments, M survival C6).
- A Keep existing import (M design 08).
- Every copy of a ruled node: Duplicate, templates, paste, Create new IDs (M, same state).
- A hand edit of the text (M design 06).
- A rule entry binned, deleted (M design 09) or moved. At the root it refuses the whole root (M
  security S3), as a look-alike name from a client holding `create` does (M S4).
- The only seal deleted (M design 10), or the seal group deleted (under V, M).

## 5. Costs

- **Visible**: a "KeePassGate rule" entry in every ruled group, a `KPG` line on every ruled entry,
  and one "Rule seal" per change, with titles that look like duplicates. Every program shows them,
  and Android search finds them (R android §4.2); KeePassXC's HTML export prints them (M).
- **Authority key**: none placed; the seal log sits beside it unless given its own group (P9).
- **Growth**: 144 characters per seal (M), never pruned.
- **Per request**: a seal index per hold (1.4 ms for 2000 seals, M), a carrier scan (2.6 ms for
  20000 entries, M) and history walks, more under V. Today: one lookup per node.
- **Classes**: `NodeFormCustomDataField` and `CustomDataKey` go; `RuleSeal` and `RuleSeals`
  arrive; `KdbxNodes`, `AclResolver`, `KdbxVault`, `VaultHandlers`, `DatabaseWriter`, the grant
  editor and `pykeepassgate.refs` change.
- **Editor and tabs**: entries write through `EntryStrings` and its two events (R, in 2.62); the
  group tab buffers until `FormClosed`. A fourth state, "refusing", arrives; "seal" already means
  something there.

## 6. Settled decisions it reverses

Grants under a `KPG` custom data key; the editor writing into `m_sdCustomData`; "the plugin writes
no rule of its own"; absence as the only answer besides a rule; a restored revision bringing its
rule back; no route hiding anything inside an entry; a copy carrying its rule; `deleteGroup`
refusing any group that holds something; and the `GroupForm` trap as the reason rules need care.
The `FileSavingPre` repair would extend O4, and P6's fix departs from O7.

## 7. Verdict

Not as written, and not yet with the changes. The carriers pass every Android program
in scope (M), the real gain over today. But the fail-closed property lives in the seals, which people
delete in every program, so one Delete after an ordinary sync widens a rule. V and never pruning
close the measured widenings; three remain whose fixes nobody ran (sealing on any OK, derived seal
identities, the residual). The text on the node adds little to the seals, so the text kept beside
the seal dominates. Before trusting it I would measure V with a nonce over C1 to C5 through both
phone merges, the explicit keep in a real dialog, the `FileSavingPre` repair in a real
synchronize, an empty custom string and the name `KPG` on each phone, and a month of refusals.
