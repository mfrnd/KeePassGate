# Digest: `hardened-custom-data`

Critiques **Surv** and **Sec**; **Android** is `measured-android.md`. **M** measured (whose probe),
**R** read, **A** assumed. No resolver exists: outcomes apply the design's states to measured storage.

## 1. What it is

All KPG data stays in custom data, and each database-level key is also copied into the root group's
custom data (the mirror), read as a union. Every key is written once, with no per-key time and no
empty value, so a change becomes a new numbered key. Rules stay on their node with a generation
member `#`, and each version is also recorded in a ledger (`KPG.Rule.<uuid>.<gen>.<d>`). A node
agrees, is *behind* (the recorded head is in force) or *disagrees* (only narrowing counts, grants
off until the operator settles it on a new Rules page). Repair rides on plugin writes and
`FileSavingPre`, never on opening. **Covers all data.**

## 2. The hard requirement

| Program | What happens | Verdict |
| --- | --- | --- |
| KeePass 2.62 | Save keeps all, file 4.0 [M design]. Synchronize from intact copies: union, 14 of 14 [M design, 6 cells]; the other four methods not run on 2.62. Two plugin copies: the one with more edits overrules a later narrowing (Sec P1, Surv S1) [M both]; a removed profile returns (Sec P8) [M Sec]. Copies (Duplicate, paste, Import with new IDs) grant by the formula as written (Sec P2) [R] | **fails open**; fixes named, none run |
| KeePassXC 2.4.2 to 2.8.0 | Save keeps all [M design]. Merge, loads in different seconds: dictionary replaced, mirror holds, 14 of 14 [M design, 4 releases]. Loads in one second, or 2.4.1: a stale target stays stale, 7 of 14 [M design]. Back into KeePass: F1, 7 of 14 [M Surv] | **fails open**, no fix |
| Keepass2Android 1.15-r6 | Save, entry edit, rename, delete, move, "Yes, merge": database values, entry and group custom data kept; back into KeePass exact both ways [M Android §2, §5.1]. The root's own custom data was not in the bases [A, from G] | **survives** (mirror A) |
| KeePassDroid 2.7 | Offers save, edit, delete only [M Android §3.1]. Every save keeps the dictionary and deletes all entry and group custom data [M Android §3.2], the root's by its writer [R]. The ledger keeps narrowings; every grant is off | **fails closed**, every save |
| KeePassDX 4.5.8 | Save, edit, rename, delete, move and "Merge from" (controls equal in both copies) keep every custom data control; root stamped at every save; back exact both ways [M Android §4, §5.3]. A stale rule under a newer time is *behind* [M spike §6] | **survives** for KPG data |

**Where Android contradicts the design or a critic.**

- Design §11: KeePassDroid moves no time, so its deletion reaches KeePass only at equal times.
  Measured: an entry edit, even an unchanged one, moves that entry's and every parent's time, root
  included [M Android §3.2, §3.3], and the deletion then wins a KeePass synchronize **both ways**
  [M §5.2].
- Surv S3 had KeePassDX undoing a move as R and A. Measured: an equal-time move follows the arriving
  copy, and a KeePass synchronize with the phone's file open keeps it undone [M Android §4.3, §5.3].
- Surv M1: every KeePassDX and KeePassDroid save writes 4.1. Measured: both kept a 4.0 input at 4.0
  [M Android §3.2, §4.1]. M1 keeps other routes (a group tag, a previous parent).
- Surv's F1 used a KeePassXC save to stand for "a phone stamps the root". Now measured: KeePassDX
  every save, KeePassDroid every entry edit, Keepass2Android a rename [M Android].
- Unsettled: design §5's "KeePassDX merges the mirror key by key" [A] against Surv's read, root
  taken whole [R]. No base carried root custom data.

## 3. Sequences ending wider or with a revocation undone

1. **One KeePassXC merge** of the fresh copy into a stale one, loads in one second, or 2.4.1, or a
   reload merge [M design; R Surv]. No fix (design case 1).
2. **One history restore** meant to undo a widening: the older text is *behind*, so the widening
   stays and the next save overwrites the node (Sec P1) [R design]. Fix: the head wins only where no
   wider; not run.
3. **Two plugin copies**, unequal edit counts, then Synchronize: the higher generation wins (P1, S1)
   [M Sec, Surv]. Fix: ledger keys name their parent; not run.
4. **Profile removed and re-added in one copy, removed in another**: `in.2` beats `out.1` (P8)
   [M Sec]. Fix: tagged removals; not run.
5. **F1**: KeePassXC merges an older copy into the fresh one; then any merge whose winning root is
   stale (KeePass Synchronize [M], other KeePass methods, KeePassDX, kdbxweb, Strongbox [R],
   Keepass2Android [A]): revocation undone, retired authority serves, profile back [M Surv, 4 of 5
   cells]. Reconciling earlier rescues one direction only [M Surv]. No fix.
6. **Narrow, delete for good, prune, KeePassXC merge** with a copy holding the node: it returns wide
   (P3) [M Sec]. Fix: keep head versions; the no-prune variant was run and refused [M Sec].
   KeePassDX's recordless deletion is a second way back [M Android; A].
7. **KeePassDroid save, synchronize, Keep all**, or OK on a disagreeing node's tab: tombstones
   outrank the narrowing everywhere (P4, M3) [M Android for the deletion; R design]. Fix: no bulk
   Keep, an explicit settle button; not run.
8. **Import with new IDs** of an older backup: an unruled duplicate of a later-sealed group inherits
   (S2) [M keepass §2.4; R]. No fix.
9. **Clock ahead** prunes a revocation; a merge re-adds the record (M2) [A]. Fix: a margin; not run.
10. **Two foreign programs in a row**: KeePassDroid deletes the mirror, KeePassXC replaces the
    dictionary [M for G, R for the root, M for KeePassXC; chain not run].

Shared, and this option does nothing about either: **a whole file over a newer one**
(Keepass2Android "No, overwrite", every KeePassDroid save over a changed file, KeePassDX within
10 s, file synchronizers) [R android §5.5; M spike]; **a narrowing made by a move**, undone by
KeePassDX [M Android] and KeePassXC [M keepassxc §4].

## 4. Refusals in ordinary use

- **Every KeePassDroid save**: every grant off; every KeePassDX 3.x save: group grants [M spike].
  The operator presses Restore all and OK on the Rules page in KeePass on Windows; the next
  KeePassDroid save deletes them again [M Android].
- Duplicates, imports, scripted rules: that node's grants off until OK on its tab (with the P2 fix).
- Two copies editing one rule at one generation: intersection until settled.
- A ledger older than eight generations, or a contested key: fails closed.
- A profile union over 32 names: every client reaches nothing until names go [R].
- KeePassXC 2.7 and later saving a 4.1 file stamps every KPG key [M Surv, Sec]; a later KeeWeb
  save makes the file unopenable in KeePass [M others]. The fix only narrows the window.
- The root's `KPG` rule deleted among ~200 unconfirmed rows: nearly every grant off (P7) [R].
- 0.1.0 databases are dark without migration.

## 5. Cost

- **Seen**: KeePass shows names and sizes only, ~200 rows [R Sec]; the design claimed values [A].
  KeePassXC's root Properties shows values, the authority PKCS#12 in clear (P6) [R]. KeePassDX
  shows no custom data [R]; the other two unchecked [A].
- **Authority key**: a second copy in the root group, two in XML exports; Import with new IDs leaves
  one in an ordinary group Delete authority misses (P5) [M Sec].
- **Growth**: ~17 KB per replica for a small database [arithmetic]; P3's fix keeps a head per
  deleted node.
- **Per request**: a ledger index over both replicas on KeePass's held thread, ~18,000 keys at 1000
  ruled nodes [A].
- **Classes**: new `RootMirrorStore`, `ReplicatedStore`, `RuleLedger`, Rules page; `PendingStore`'s
  three reconcilers go; the writer, the stores, the resolver, the editor and `GroupDialogs` change;
  `KeePassContract` pins an internal setter.
- **Editor, tabs**: still writes `m_sdCustomData`, records at `FormClosed`; `#` caps a node at 30
  profiles; a fourth KPG page.

## 6. Settled decisions reversed

"Never the root group's"; "the authority takes the file to 4.1"; revocation beside an emptied record;
`KPG.Profiles` as a difference and the standing answer's second writer; "a lift does not travel";
"the plugin writes no rule of its own"; absent rule as silence, one document per node (gains `#`),
nearest wins only where the node agrees. Needs a ruling: repair inside the operator's own saves.

## 7. Verdict

It fails the hard requirement. Against KeePassXC it fails open in one step and in two (F1), measured,
and nothing inside custom data closes either: KeePassXC replaces the dictionary whole, and every
merging program takes the root group's custom data whole. Against KeePassDroid, one of the Android programs
in scope, it fails closed at every save: narrowings and revocations hold, every grant is off
until someone restores at the desktop. Keepass2Android and KeePassDX 4.5.8 on their own survive.
Two plugin copies widen too, with no fix run. Before trusting it, measure F1 with the real programs,
the root's custom data through every Android save and merge, KeePassXC's window and reload merges,
how often default-KDF loads share a second, and a parent-linked ledger.
