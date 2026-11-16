# Digest: option `crdt`

Sources: the design, its survival, security and formal critics, `measured-android.md`,
`ground-inventory.md`. M measured, R read, A assumed; "model" measures no program. "Surv": the
survival critic's real files under KeePassLib 2.62 and `keepassxc-cli` 2.4.2 to 2.8.0.

## 1. What it is

Every datum is an ordinary entry written once, in one plugin group found by a UUID prefix, its
identifier a keyed digest of content holding a nonce, HMAC'd under a store key kept in the
authority's document. Authorities and records form a two-phase set with final retirements and
revocations, profile names a remove-wins set, and rules, renewal answers and node meta registers
whose concurrent versions meet as the narrower; "later" is an `after` list, never a clock. Each gate
write adds a *head* carrying every refusal, removal and standing version, and each revocation and
retirement a *witness* deletion record. A missing head, damage or an unexplained record makes the
store *incomplete*, serving nothing until **Seal again**. A ruled node carries a mark that only takes
away. **It covers all data.**

## 2. The hard requirement

Fixes shown to work: **P1**, count a verifying element wherever it sits, only a deletion record
taking it out of force (bin: R1 to R4, R9 pass on real files [M surv], 48,000 histories clean [model
surv]; Move [model formal]); **P2**, drop the 64-height window [model formal]. Nothing uses node
custom data or times, which the phones destroy; entries, protected custom strings and records naming
nothing came through every phone operation and back [M android 2 to 5].

| Program | Verdict | Why |
| --- | --- | --- |
| KeePass 2.62 | fails open | As written, Delete plus Synchronize widens (R3, R4 [M surv]; CE1 [model formal]). With P1, save, edit, all five merges hold [M surv; model surv]. Left: items 1 to 3, 8, 9 |
| KeePassXC | fails open, by shared and never-told sequences only | Saves keep strings, flags, records [M keepassxc 6]; `rm` bins, merges carry no record [M]. As written R1, R2, R9 [M surv]; with P1 all pass [M surv]. Left: 5, 12 |
| Keepass2Android 1.15-r6 | fails open, by "No, overwrite" only [R android 4.2] | Save, edits, rename, Delete (bin, no record), delete in bin, Move, merge, way back measured [M android 2, 5.1]. As written a binned head stops every copy and feeds R2; with P1 neither [M surv; model surv] |
| KeePassDroid 2.7 | fails open, by its save over a changed file [R android 4.2, not run] | Offers save, edit, delete [M 3.1]; all kept, back whole [M 3.2, 5.2]. Its unchanged Save moves the time [M 3.3], so KeePass drops the gate's records [M surv F2]: R3; with P1 it passes [M surv] |
| KeePassDX 4.5.8 | fails open | All kept, back exactly [M 4, 5.3]; its merge keeps no arriving record, follows the arriving place at equal times [M 4.3]; with P1 clean beside Synchronize [model surv]. Left: 5, 11, 12; overwrite within 10 s [R; M spike] |

**The measurement contradicts the design** twice. Phone Delete bins with no record [M 2.1, 3.2]: the
design cites this as T4 yet says only a script removes without trace, and its read ignores binned
heads and versions, whose move travels by merge [M surv F3; M android `Y3`]. And T3 "harmless" is
false [M 3.3; M surv F2]. It confirms the design's read KeePassDX statements (T1, T7, T13, relocation)
and that KeePassDX's bin moves the time [M 4.3, 4.4]. New, unmodeled: KeePassDX keeps its own record
beside a newer arriving entry (`X2`), refusing direction. It contradicts no critic. Open: phone
screens (S2 [A]), a fifty-string head, group deletion on a phone (S4 [A]), Keepass2Android's
unchanged Save, a one-copy witness through its merge (an arriving record is carried [M `X1`]; one
naming nothing [R]).

## 3. Sequences that end wider, shortest first

1. **Shared, overwrite**: revoke; KeePassDroid saves its older copy over the file; KeePass locks and
   unlocks: admitted, no trace [R android 4.2; R CLAUDE.md lock; A reload]. No in-file fix [model
   formal P3]; a host mark or replica needs a ruling against E9, not run.
2. **Refused rule write**: narrow an entry's rule, OK, the writer refuses; the wider rule stands [R
   design 10, critic S5]. Fix (add in memory, ride the next save) not run.
3. **Copy stripped**: copy a ruled node, remove the copy's mark before KeePass next saves: governed
   by place, 3 steps [model design]. The pin needs a ruling.
4. **One deletion widens** (CE10, 4 steps); a revocation undone by two Moves and a KeePassXC merge
   (CE3), a retirement likewise (CE4) [model formal]; the design's model refutes its one-loss claim
   [model design, surv]. P1 closes all [model formal].
5. **Never told**: both carriers deleted where made, then a merge carrying no records, 4 steps
   [model design]. The making copy refuses. No fix.
6. **R1**, 5 steps: revoke; Delete its entry; Empty Recycle Bin; KeePassXC merge into a stale copy;
   Delete the new head there: revoked client admitted [M surv]. Also R2 (4), R9 (5), R4 (5,
   Synchronize only), R3 (6) [M surv]. P1 closes all [M surv].
7. **Seal again after a double loss** forgets a forever refusal of renewals [model design]; after P1
   only by script.
8. **Create new IDs import, then Delete Duplicate Entries**: originals gone in every synchronized
   copy [M surv F5]; after a new authority, sealed copies go by place [model surv]. Fix not run.
9. **R6**: records cleared (Database Maintenance) after an unsealed loss, then a stale copy
   synchronized [M surv]; P1 does not close it.
10. **CE5**: a copy 65 writes behind, 70 steps [model formal]; P2 closes it.
11. **Script removal** beside the older state (CE6) [model formal], or records applied by a KeePassDX
    merge that keeps none [model design; M android 4.3]. Not closed.
12. **Shared, a narrowing by move undone by a merge** [M keepassxc 4, 9; M android 4.3, 5.3].
    Nothing here addresses it.
13. Only if S1's new key at Recreate is taken: CE11, CE12 [model formal]; fix not run.

## 4. Refusals in ordinary use

- **Whole database, until Seal again** on the KPG tab: the head deleted for good (Shift+Delete, Empty
  Recycle Bin, delete inside a phone's bin), or a `KPG.` string edited by hand, in every copy it
  reaches; as written also a binned head or a duplicated element (S3). Once per gesture [A].
- **Nothing served, rules and clients gone**: the plugin group deleted for good (Keepass2Android
  asks twice [M entry, A group]), or item 8. Marks keep ruled nodes refusing; the operator creates,
  pairs and rules again.
- **A node refuses below**: marked with no rule; item 2 on a first rule. `503 undecided`: a second
  authority. A copy is limited until Adopt; two gates' versions meet until Settle. Writes refused
  while `KPG head` is open in a dialog [A].

## 5. What it costs

- **Seen** everywhere: up to about 140 `KPG …` entries [A], a private key among them; a head of
  dozens of fields; `KPG.Node` on ruled entries, a link entry in ruled groups [M kept; A display].
- **Authority key**, now with the store key, in a protected string: printed and exported by KeePass
  [R], in clear by KeePassXC's HTML export [M keepassxc 2], kept on phones [M]. One leak forges elements;
  Recreate keeps the store key (S1).
- **Growth**: records never aged, 100 to 750 a year; head 3 to 20 KB; P2 adds 16 bytes a write [A].
- **Per request**: 0.26 ms for a 10,054-entry lookup [M store-entries], not this shape; P1 walks every
  entry per change [A]. A rule edit becomes a whole save (80.6 s at 201 MiB [M `DESIGN.md`]).
- **Classes**: `CustomDataStore`, key names, reconcilers, `m_sdCustomData` reflection go;
  `AuthorityStore`, `AdmissionStore`, `AclResolver`, `DatabaseWriter`, `OpenDatabaseTargets`,
  `GroupDialogs`, `KdbxVault` (API guard) change; codec, MAC, fold, head, witnesses, marks, pins,
  four presses, a `503` word arrive; 76 test files rebuilt.
- **Editor and tabs**: rules go through the writer at the node dialog's OK (S5); new states in 449 by
  314: incomplete, Seal again, Settle, Adopt, rule gone.

## 6. Reverses

The database's own `CustomData` bullet; "the authority takes the file to KDBX 4.1"; "replayed key by
key" and the profile and renewal reconciles; "the grant editor writes into the dialog's own copy";
one document per node under `KPG`; `CA.md`'s unauthenticated records; the maintenance trap; "the
plugin writes no rule of its own". A host mark or replica would reverse the first decided bullet.

## 7. Verdict

The types are sound: the lattice laws hold over 200,000 triples, and nonce identity survives restored
backups [model formal]. The read was not: counting by place and the 64-height window widen; both
fixes were run (P1 on real files, P2 in a model). With them every phone operation keeps this
option's carriers [M android]. Left: the shared overwrite and move, measured widenings (8, 9), a rule
edit lost after OK, a private key in a printable entry, outages from one edit, and more parts than
any option, mostly idle with one gate (security M5). Not as written; viable with P1, P2, S3's copy
rule and S5, given a ruling on a host replica. Measure first: KeePassDroid's save over a changed
file, then a KeePass lock; P1's read at 10,000 entries on KeePass's thread; R1 to R9 and the twenty
scenarios through Keepass2Android and KeePassDX merges; a fifty-string head and group deletion on
phones; `EntrySaving` cancelling OK.
