# Digest: `versioned-fields`

Marks: **M** measured, **R** read, **A** assumed, with whose probe: "design", its critics
"survival" and "security", "android" (`measured-android.md`). KeePass and KeePassXC ran from a
console (KeePassLib 2.62, `keepassxc-cli`), never a window.

## 1. What it is

Every plugin write is a new entry in a `KeePassGate` group, never changed afterwards, holding JSON
versions in custom strings named `KPG.<time>.<origin>.<n>`; the name is the identity, and no clock
or modification time decides anything. A version lists in `after` what it replaces, and versions
that do not name each other are reconciled at every read, with nothing written at open: a refusal
wins, a removal wins, two rules give what both allow. A write that closes something is a "state
pair", two entries each holding everything in force, and the plugin deletes older write entries
with deletion records dated one second after the time in their identifier. A rule also has a copy
on its node (`KPG.Rule` on an entry, a marker entry in a group), so a copied node refuses until
settled, and a random store identifier makes another file's versions count for nothing. **It
covers all data**, the rules included.

## 2. The hard requirement

| Program | Found | Verdict |
| --- | --- | --- |
| KeePass 2.62 | Saves, edits, deletions, Duplicate Group and four merge methods: every verdict held except where the plugin's own entries were destroyed by hand [M design]. **File then Import with the preselected "Create new IDs" carries no closing version into a stale copy**: revoked client admitted, retired authority serving, narrowing not carried, where today's store carries both [M survival P1]. Reading closing versions from unmarked copies closed it (run in a broader form) [M survival] | **survives with named change** (P1) |
| KeePassXC 2.4.2 to 2.8.0 | Save keeps everything [M design]; a merge unites and never applies a deletion record [M design, ground-keepassxc]. Widens under P4 and P3 [M survival]; without the press P4 holds (the critic's control), and P3's refined condition holds in the measured cells [M survival]. Window merge, reload merge, KeeShare not run | **survives with named changes** (no press; P3 condition); growth unbounded (P2) |
| Keepass2Android 1.15-r6 | Every carrier the option uses (protected and plain custom strings, kilobyte JSON, deletion records) came through save at every KDF, ordinary edit, carrier edit, rename, delete, move, its merge and Synchronize back into KeePass; the merge added entries only one copy held and joined and applied deletion records [M android]. The store's own shape (17,000-character entries, the plugin's record timing) not run; history trim at load [R] | **survives** (mixed) |
| KeePassDroid 2.7 | No merge, move or group edit exist. Save, edit, carrier edit and delete kept every carrier, and all came back into KeePass whole [M android]. An editor Save with nothing changed moves the entry's time [M android], feeding P6 | **survives** (M) |
| KeePassDX 4.5.8 | Save, edits, rename, delete, move kept every carrier and went back into KeePass exactly. Its merge adds arriving entries, applies arriving deletions **without keeping a record** (a stale KeePass copy brings those entries back), and a move or recycle moves the entry's time [M android]. 3.2.0 and the external-change merge not run | **survives** (mixed) |

Copying a node on a phone was measured nowhere; the copy would refuse [A].

**What the Android run settles.** Keepass2Android and KeePassDX merges keep a shared entry changed in two copies
whole from one side, losing the other's field outright at equal times: design section 1's reason
for changing the idea, now [M]. It confirms that Keepass2Android's KDBX 3.1 output keeps strings
(design [A]), **contradicts** "KeePassDX writes 4.1" (a 4.0 file stayed 4.0), answers security
S1's Android half for the mechanism but not this store's shape, and makes the KeePassDX steps of
P3, P5 and P6 [M]. Nothing measured contradicts a survival claim.

## 3. Sequences that end wider, shortest first

1. **P1** [M survival]: revoke (or retire, or narrow) in A; File then Import A into stale B with
   the defaults. Fix above; with closing versions only, A's new authority serves nothing in B
   until a Synchronize.
2. **P9** [R survival]: as worded, "a profile only some name keeps nothing" can inherit, and a
   state pair keeping only "the newest" of two concurrent rules drops one. Text fixes, not run.
3. **The design's 21 cells** [M design]: both entries of a revocation pair, or the group, destroyed
   in A; A merged into stale B by Overwrite if newer, Overwrite existing, Keep existing or
   KeePassXC: B admits c1. No fix.
4. **P4** [M survival, 2.4.2, 2.8.0]: narrow X; delete X for good; press "Remove rules of nodes that
   are gone"; KeePassXC merges stale B in: X back, wider, silently. Fix: drop the press (control
   holds) or a compact `deny` version (not run).
5. **P3** [M survival; KeePassDX step M android]: A narrows X; both newest state entries deleted
   without a record (pykeepass [R], or a KeePassDX merge); KeePassXC merges stale B into A: X
   widened. Refutes design 7.1's "all 16"; contrary to the critic, it needs a hand deletion.
   Refined condition run, holds, fails where records were cleared.
6. **P10** [R survival]: two copies each create an authority; merge; keep fpA; delete fpA: fpB's
   retirement, in fpA's store, stops counting. Fix: count it in any store; not run.
7. **P8** [A]: `revoke` removed from both state entries without history. Fix: a manifest; not run.
8. **P5** [A; KeePassDX step M]: both pair entries lost without records, a returned pairing entry
   admits. Fix: rewrite a broken pair at every write; not run.
9. **P7** [A]: a fast clock prunes a revocation early; no regression. Fix: a margin past
   `notAfter`; not run.

**Shared.** A whole file written over a newer one (Keepass2Android "No, overwrite", KeePassDroid,
KeePassDX within 10 s, a sync conflict) [R android; M spike]: not solved. A narrowing by a move,
undone by a merge (KeePassDX follows the arriving copy at equal times [M android]): nothing done.

## 4. Refusals in ordinary use

- **Every copy of a ruled node** (Duplicate, template, Import) refuses until settled on its own KPG
  tab [M design], as often as ruled nodes are copied.
- **`503 damaged`, the whole gate**, once a permanent delete of a write entry in any program
  reaches KeePass (all three phone programs write the record [M android]); Accept on a new Data
  page. Delete Duplicate Entries after an import too (Put back) [M design]. The clutter invites it
  (security S3).
- **Both state entries gone**: that file admits nobody; clients pair again [M design].
- **Concurrent rule edits**: what both allow until settled [M design].
- **A union past 32 profile names**: every client reaches nothing.
- **Two unretired authorities**: `503 undecided`, as today.

## 5. Costs

- **Seen everywhere**: the group of `KPG <time> ...` entries, a marker in each ruled group,
  `KPG.Rule` JSON on each ruled entry.
- **Authority key** in a protected string: one click to the clipboard, print, KeePassXC's HTML
  export in clear [M ground-keepassxc], a phone's reveal, a second copy after an import. Security
  S2: keep it in database custom data, whose values Android saves keep [M android]; not run.
- **Growth**: about 38,000 characters against 14,000 [A]; under KeePassXC every state pair stays, 3
  to 19 entries in 8 renewals, refuting "nothing accumulates" [M survival P2].
- **Per request**: 0.13 to 0.95 ms at 10,000 to 50,000 entries; a listing adds two lookups an
  entry, 1.5 to 12 ms each [M design]; about 10 ms at P2's size [A].
- **Classes**: `IKeyValueStore`, `CustomDataStore`, `ProfileNames.Merge` go; a version store,
  `503 damaged`, a guard for `KPG.` names and a fourth page, Data, arrive; some twenty classes and
  49 test files change [R design].
- **Grant editor**: writes `PwEntryForm.EntryStrings` (OK, Cancel, Escape [A]) or a group buffer;
  two new states, "two versions" and "copied".

## 6. Settled decisions reversed

The `CustomData` place and its whole bullet; KDBX 4.1; grants under a `KPG` node key; the editor
writing `m_sdCustomData`; "the plugin writes no rule of its own"; key-by-key replay and the profile
difference; only an unreadable rule refuses; three pages; the trap's Maintenance paragraph; a
restored revision bringing its rule back; `CA.md` §204 and §208.

## 7. Verdict

As written it fails the hard requirement in KeePass (P1) and KeePassXC (P4), both measured. With
those two fixes no measured sequence ends wider unless the plugin's own entries were destroyed by
hand, every Android carrier it uses came through whole, and its rule survival is the strongest of
the in-file designs. It still grows without bound under KeePassXC, shows the authority key, and
turns a hand tidy into a whole-gate outage. Measure first: the store's own shape on the emulator
(both Android merges, full-size entries, KeePassDX 3.2.0); P1's closing-only form, P2's fix and
P4's `deny` version; the dialogs in a real KeePass; the read cost on KeePass's thread.
