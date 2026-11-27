# Measured: Seal again, the press that lifts a stopped store

Seal again is the single press for a stopped store (`STORE.md` §625). For every object that refuses
because a deletion record tells of a version whose text is nowhere, it writes a version granting
nothing; then a write of §627 deletes every entry that is damage with a deletion record and adds a
summary entry over everything that passes and is in force. The pull request that builds it designs
it in its plan, section 3.1, and names in section 4.1 what is measured first. This report measures
that design before anything of it is built in the product, in the model, over the files `store.py`
writes of the model, and on real files. It was run on 2026-11-26 in the family
`spikes\storage\probes\store-as-a-whole\` on the branch `spikes/storage-survival`, as `run.ps1
-Seal`, with its record in `recorded-seal\`; the family's `RUN.md`, section "Seal again", has the
commands, both runs' times and the comparisons. Nothing ran in a window or on a phone: the files of
the parts on files were made and changed by KeePassLib 2.62 and merged by KeePassLib or
`keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0.

**How to read it.** A copy's **ideal** is the same file had every record it ever received arrived
by a perfect transport, as in every report of this family; **every decision** is the file holding
every record any gate ever made. A file is **beyond** an ideal where it grants what the ideal does
not: a client admitted, a profile held, a letter at a node, or a renewal answer below the ideal's on
the chain. With the seal on, both count a version that a version of the same object names in `lost`
as seen and replaced, for every object, as the renewal report counted it for answers alone (section
8, item 3). A **counterexample** is a history in which a copy ends beyond its ideal, a seal leaves
its store stopped, or an object that refused for a lost version grants after the seal, of none of
the classes that `STORE.md` §639 and the evaluation's decision 9 accept.

## 1. The design measured

- **Where the press applies**: a store stopped with its key held (§624). Create is refused there,
  as the product refuses it; where no key stands there is no stop, and Create starts a store.
- **The versions**: for each object a lost version makes refuse, a version granting nothing for its
  type, an empty rule for a row, the switch refusing, `refuse:forever` for an answer, naming in
  `after` every version of the object in force and what those name in `after`, and in `lost` the
  lost versions and what the versions in force name in `lost`. Nothing for a revocation, a
  retirement or a removal that a deletion record alone states: the union of §623 step 2 keeps it in
  force and the new summary entry restates it. Nothing for a deletion record of a version of no
  object the file knows.
- **The write**: every damaged entry deleted with a deletion record, whatever its tag; deletion
  records for every summary entry told of; a summary entry whose `after` names every summary entry
  present, everything those name, every summary entry a deletion record tells of and every damaged
  summary entry the write deletes; everything in force restated. The seal does not compact.
- **Composed at the write**, over the reading of that moment, never over what the dialog showed; a
  seal over a store that became whole writes nothing.
- **Beside other acts**: the seal first and the acts composed over the sealed store, in one write
  with one summary entry, at the settings dialog's OK, held in the open database and kept by the
  next save.

## 2. What was implemented

**In the model** (`crdt_model.py`, each line marked "ADDED (seal again)"; with the switch at its
default every output prints as before). `SEAL`, the switch: Create refused on a stopped store while
a key stands; a damaged summary entry the write deletes named in the new summary entry's `after`
(`SEAL_NAME_DAMAGED` off is the control); the seal played first and put back where it would leave
no entry holding the store key, the guard `StoreWrite.Plan` holds for every write; the seal and an
act written with it waiting for a save under `pending`; the act `sealsee`, what the seal's dialog
shows; `reseal+`, the seal and an act in a single write; `SEAL_COMPOSE` `shown`, the control that
composes the seal over what `sealsee` showed. The press's write moved unchanged into a method of its
own. A node's whole rule set after the seal names in `lost` what it settles, as a row's version
already does. `run` holds every seal that applied to the store being whole after it
(`seal-left-stopped`), every object that refused for a lost version granting nothing after it
(`seal-granted-a-lost-object`), and a single write reading as the seal and the act written apart
(`seal-with-reads-otherwise`); and counts a single write that keeps other records than the two
writes apart, an object refusing by a damaged version's deletion record, and a seal refused for the
store key.

**On files.** `read.py` with `SEAL` answers `losses`: every condition of §624 that holds, not only
the first; the damaged entries by identifier, the kind their tag names and their title; the summary
entries told of that nothing in force names; the records no summary entry restates; the
revocations, retirements and removals a deletion record alone states; the lost objects with their
deletion records; the objects a damaged version's deletion record would make refuse once deleted;
and the versions the seal writes, composed from that reading alone. `bridge.seal()` is the
configuration; `probe/seal.py` plays R1 to R21 and the edits past the limit. `vectors.py --seal`
prints the vectors of section 7.

## 3. What was run

| Part | What it is | Size |
| --- | --- | --- |
| `edits` | each kind of store entry edited past KeePass's history limit (eleven dialog edits; a dialog edit and ten quick edits) and with no history kept, and a rule version granting read alone and with the summary entry; Seal again; each merge of KeePassLib's five methods and KeePassXC 2.8.0 both ways with the unedited copy, on real files | 12 kinds by 3 edits, 360 merge cells |
| `cells` | R1 to R21 on real files, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases, each held to every decision, its own ideal and both copies just before | 21 sequences, 378 cells |
| `hand` | R1 to R21 in the model, Create new IDs among the merges; R15 with the switch off; R11 and R12 with the damaged summary entry left unnamed; R16 to R18 as a single write and apart; every sequence composed as shown | 168 cells |
| `seal-two`, `-three`, `-long` | random histories of the seal's alphabet: every act of the store, the dialog, the seal and the seal beside eight acts, every deletion by hand and edit of each kind, every deletion record cleared, a version whose content no file holds, four merges | 200,000, 100,000 and 100,000 histories |
| `exhaustive-4` | every sequence of 4 steps of a narrow alphabet of 40 over two copies after a fixed divergence | 2,560,000 histories |
| `damage` | configurations F, I and K over two files with the design's seeds: a damaged version sealed | 20,000 each |
| `search-2`, `search-3` | the design's twelve configurations over two and three files with its seeds, the seal's two acts in the alphabet | 480,000 histories |
| `bridge` | the Encoder's files read by read.py, held to the model's fold; read.py's planned seal held to the model's | 2,000 histories |

Everything ran twice, under an ordinary user token, the second run printing every output of the
first line for line but the running times, and the three files of vectors byte for byte but the
line ends. Seven parts of `-Answers` run again with the switch off printed their records, `cells`
and `losses` on real files among them.

## 4. The sequences, side by side

Copy 0 holds c0 revoked, the authority recreated, c1 paired, pg added, pf removed, N's rule empty and
c1's renewals refused forever, synchronized into copy 1, the copy from before; then the losses of
each sequence at copy 0, the dialog, the seal and every merge. On files and in the model alike:

| Sequence | Before the seal, read.py's listing | After the seal, copy 0 | The merges |
| --- | --- | --- | --- |
| R1 the only summary entry deleted with a record | no summary entry in force | as decided | as decided in every cell |
| R2 one of two summary entries made apart deleted with a record | a summary entry deleted that nothing replaces | as decided | as decided |
| R3 N's newest version and the summary entry deleted with records | no summary entry; N lost by 2 deletion records, the version's and that of the version it replaced | an empty rule naming both in `lost`; as decided | as decided |
| R4 c1's refusal and the summary entry | the answer lost | `refuse:forever` naming it in `lost`; renewal refused | as decided |
| R5, R6, R7 the revocation, the retirement, the removal, each with the summary entry | each known by its deletion record alone | restated; as decided | as decided |
| R8 the revocation edited past the limit | damage | deleted with a record; c0 still refused | as decided |
| R9 c1's admission edited | damage | deleted with a record; c1 refused, narrower | narrower, or as decided into the unedited copy by a method that carries no record |
| R10 N's version granting read edited | damage; the summary entry carries its text | N gives read: it stands | as decided, narrower in one import |
| R11 the summary entry edited | no summary entry; damage | as decided | as decided; with the damaged summary entry left unnamed the store stops again (`hand`) |
| R12 N's version and the summary entry both edited | damage; N lost by the deletion record of the version it replaced | N refuses: the empty rule for the lost predecessor, and the damaged version's own deletion record | as decided by the oracle, which holds the seal's refusal |
| R13 a revocation no summary entry restates, by KeePassXC | a record no summary entry restates | restated; as decided | as decided; one import into copy 1 stops that copy |
| R14 a lost version and the revocation edited | two conditions | as decided | as decided |
| R15 Delete, then the summary entry deleted | stopped with the key in the retirement, no authority | Create refused before the seal; after it Create under the key held, and a pairing | as decided; one import into the stale copy stays stale (§640) |
| R16, R17, R18 the seal with a revocation, with Create, with N set to RU | as R3, R15, R3 | the same as the seal and the act written apart but for the summary entries | as decided; one stale import each in R16 and R17; R18 narrower into copy 1 by the methods that carry no record |
| R19 a merge under the dialog brings the lost version's text back | at the dialog N lost; at the write nothing lost, the admission damaged | no version for N: N gives read; c1 refused | narrower (c1) or as decided |
| R20 a merge under the dialog brings a further loss | at the dialog nothing lost; at the write N lost | an empty rule naming the new loss | as decided |
| R21 the revocation and the summary entry deleted with no record, sealed | no summary entry; nothing lost | as decided | into the stale copy by every method that carries no record, c0 admitted: §639 row 5, 8 cells |

**The cells.** Of 378 cells on files: 337 as decided, 29 narrower, 1 refusing and 11 wider than
every decision, each accepted: 8 of R21, row 5, and 3 imports into the copy that stays stale
(§640). **None is beyond both copies and none is beyond its own ideal outside those classes.** The
file and the model disagreed at one step, an entry of R19 after Create new IDs, their readings the
same. In the model the 168 cells give the same classes; 5 cells of R18 are beyond the ideal by
`after` alone, which section 8 item 3 explains.

## 5. A seal after a stop by an edited entry

Each kind of entry of copy 0, edited past KeePass's history limit (the eleventh edit, of the dialog
or quick) or with no history kept, stops the store: an entry of the store with no version that
passes. 30 of the 36 cases stop with the key held, and Seal again applies in all 30 and leaves the
store whole in all 30. The other 6 are the authority and the retirement that holds the store key:
their edit leaves no key, the store reads as none, the press does not apply, and Create starts a new
store (decision 9).

| What was edited | After the seal | After every merge both ways |
| --- | --- | --- |
| a retirement, a revocation, a removal, a rule version (nothing; read), a renewal answer, the summary entry | as decided: a summary entry carried the text, or the union keeps it | as decided |
| an admission (c1) | c1 refused, narrower | narrower |
| a profile addition (pg) | pg off the list, narrower | narrower |
| a rule version granting read and the summary entry, both | N refuses: no summary entry held its text | as decided by the oracle, which holds the seal's refusal; N refuses wherever it reaches |

Of the 360 merge cells, 315 as decided, 42 narrower and 3 wider than every decision, each an import
by Create new IDs into the unedited copy, which stays stale and within its own ideal (§640). **No
file grants beyond its ideal.** What the damaged entry held stands where a summary entry carried its
text, and refuses where none did.

## 6. The search

**The seal's alphabet**, every history held to every property:

| Search | Failing a property that ends a history, by class | Counted |
| --- | --- | --- |
| two copies, 200,000, 4 to 12 steps | `safety` 27: rows 2 and 3 14, decision 9 13. `one-loss` 168 | an object refusing by a damaged version 239; a seal refused for the store key 28 |
| three copies, 100,000 | `safety` 2: rows 2 and 3 1, decision 9 1. `one-loss` 70 | 66; 2 |
| two copies, 100,000, 8 to 16 steps | `safety` 47: row 3 1, rows 2 and 3 14, decision 9 32. `sealed-after-a-loss` 11: row 5 10, rows 2 and 3 with the seal not involved 1. `one-loss` 131 | 278; 43; a single write keeping other records 2 |

**No history is of another class**, and none failed `seal-left-stopped`,
`seal-granted-a-lost-object` or `seal-with-reads-otherwise`. The `one-loss` failures are, but for 11
of rows 2 and 3 and row 3, a class not of the seal: a deletion record of an answer whose content no
file holds, then the client's admission deleted by hand, so that the file no longer knows the client
key and the deletion record tells of nothing (§618), the client admitted nowhere in that copy: 358
histories, the shortest `lose 0 renew:c0 ; del 0 (rec) 0`.

**Every sequence** of 4 steps of the narrow alphabet, 2,560,000 histories: no failure but row 5,
1,938 sequences, each holding a loss that left no deletion record, the shortest `del 0 (ver rule:N
new) 0 ; del 0 (ver rule:N new) 0 ; del 0 (head new) 0 ; reseal+ 0 (rule 0 N rw)`; an object
refusing by a damaged version after the seal in 6,720.

**The design's search**, twelve configurations, 20,000 histories each: over two files `safety` 2 (E,
rows 2 and 3; K, decision 9), `sealed-after-a-loss` 1 (C, rows 2 and 3, the seal not involved),
`rollback-by-overwrite` 73 and 19 (H and I, row 1); over three files `safety` 1 (H, decision 9) and
`rollback-by-overwrite` 103 and 31. **No history is of another class.**

**A damaged version whose text no summary entry holds.** In F, I and K over two files, of 1,223,
473 and 489 histories with an edit and a seal, a seal applied in 172, 57 and 56, and an object
refused afterwards by the deletion record of a damaged version the seal deleted in 1, 0 and 0: the
class point 3 under §625 of the plan rests on is met about once in 20,000 histories of the design's
configurations, and in 239, 66 and 278 histories of the seal's own searches and 6,720 sequences of
the exhaustive part. Every such object refuses: narrower, never wider.

**The bridge.** In 2,000 histories, 9,700 files read by read.py gave the model's whole or stopped
and its lost objects, and at 1,305 stopped copies read.py's planned seal named the same versions,
text, `after` and `lost`, as the model's seal wrote. A script apart, not in the record, found none
of 447 versions written by a seal naming an identifier in both `after` and `lost`.

## 7. The vectors

`vectors.py --seal` prints the three files as `--answers` prints them and adds the seal's under
names of their own; `--answers` prints `recorded-answers\vectors\` byte for byte but the line ends,
and those are the product's `tests\Shared` on `origin/master` (`133a08b4`) byte for byte.

| File | Held today | Printed | Added | Changed or removed |
| --- | --- | --- | --- | --- |
| `StoreRecordVectors.json` | 47 records | 51 | 4: the seal's empty rule of a row naming a standing version and a lost one, its switch refusing, its refusal of renewals forever, and a summary entry restating them | none |
| `StoreMeaningVectors.json` | 112 stores | 130 | 18, `stopped:`: each condition of §624, each kind of damage, a record and a version not restated, a lost row, switch and answer, a revocation, a retirement and a removal by deletion record alone, two at once, a version of a node the file does not know; each with `losses` | none |
| `StoreWriteVectors.json` | 63 sequences | 85 | 22: R1 to R21 and R3s with rows, every step with `damaged` and `losses`, R21 holding `accepted` | none |

**Nothing held today changed**, checked by `compare_seal.py vectors`: each file's line diff adds
lines and replaces one, the line of `about`, whose text is the text held today with 657, 1,702 and
2,060 characters added after it. A7 of the answers, played again with the seal on, gives the
sequence already in the file, so the product's skip of A7 at the seal can go with nothing added. No
vector holds a certificate taken away: neither the model nor read.py holds the kind.

## 8. Where the model sharpens the design

1. **The seal must name the damaged summary entry it deletes.** With it left unnamed, R11 and R12
   stop again at once: the deletion record the seal leaves tells of a summary entry nothing in force
   names. The plan's step 6 has it; the model wrote no such `after` before the switch.
2. **The seal is refused where its write would leave no entry holding the store key**, the guard
   every write of the product has. It is met where the entry that holds the key is damaged and a
   retired authority's entry, which the write prunes, held it until then: after Delete, the
   retirement edited and merged by KeePassXC; after Recreate, the authority edited. 28, 2 and 43
   histories. Without the guard the model's seal left a store with no key. The plan withholds the
   button where the seal's plan does not fit the room; it has to be withheld where the plan is
   refused for the key as well, the way out being Start again.
3. **A rule or an answer set after the seal carries its `lost` on**, so that the losses the seal
   settled stay settled; and the oracle counts `lost` as seen for every object, as for answers. By
   `after` alone, the operator's rule set over the seal's refusal (R18) reads beyond an ideal that
   still holds the lost version; the store is right and it is the oracle that has to count `lost`.
4. **The damaged version's own deletion record often finds its object refusing already**: the
   version it replaced, pruned with a deletion record, is lost once no summary entry names it, and
   the seal writes its empty rule there (R12). The dialog's "refuses until set again" is the rest: a
   damaged first version of its object, whose deletion record the seal itself writes.
5. **A single write keeps, in 2 of 100,000 histories, records the second of two writes would
   prune**: an authority and an admission out of force beside their deletion records, where the
   damaged retirement naming them is deleted in the same write. The reading is the same.
6. **Composed at the write** (R19, R20). Over what the dialog showed, the control writes a refusal
   for a version a merge brought back, so N refuses where the design gives read, and settles nothing
   of a further loss; composed at the write, neither.
7. **Create refused on a stopped store** (R15). With the switch off the measured Create writes over
   the stopped store and serves an authority, every damaged entry deleted and no version granting
   nothing written; with it on, the seal first and Create after it, alone or in the single write.
8. **A history after a seal is not row 5 for having a seal**: one of the 11 of `seal-long` and the
   one of the design's search fail the same with every seal left out, a merge reading the file from
   before a seal that waited unsaved.
9. **A deletion record of an answer tells of nothing once the client key is unknown** (§618): a
   class of `one-loss` found here, not of the seal.

None of these changes the design but item 2, which adds a refusal the product has already.

## 9. The property, as it stands

For every copy whose store is whole and serves, **after every step of any history of the store's
acts, the seal's dialog, the seal alone and beside an act, deletions by hand, edits, cleared
deletion records, lost versions, saves, KeePass ending before a save and merges, the copy grants
nothing beyond its ideal**, where a version a version of the same object names in `lost` counts as
seen. **A store stopped with its key held is whole after every seal that applied**, and the seal
applies wherever its write leaves an entry holding the key. **Every object that refused for a lost
version grants nothing after the seal.** **A single write reads as the seal and the act written
apart.** **Merges alone converge.** Outside it, and counted: a store key lost (decision 9); §639 rows
1 to 5; KeePass ending before a save; a copy of a node wider than its original (decision 16); and a
client's admission deleted so that a lost answer's deletion record tells of nothing.

## 10. What the product has to satisfy

- `StoreLosses` lists what read.py's `losses` lists for every store of the `stopped:` family: each
  condition that holds, the damaged entries, the summary entries told of, the records not restated,
  the decisions a deletion record alone states, the lost objects, the objects a damaged version's
  deletion record would make refuse, and the seal's versions with `after` and `lost`. The product's
  reading gives the first reason alone today.
- The seal's versions are the records of `StoreRecordVectors.json` whose names hold the seal's, text
  for text; `after` and `lost` hold no identifier in common.
- The seal's write deletes every damaged entry, whatever its tag, with a deletion record, and its
  summary entry's `after` names each damaged summary entry it deletes; the store is whole after it.
  Today's step 5 writes deletion records only for what is not in the file, so a damaged entry stays.
- The seal is refused, and its button withheld, where its write would leave no entry holding the
  store key, beside the room the plan names.
- Create stays refused on a stopped store alone, and is planned over the seal in one write.
- An act composed over the sealed store names the seal's version in `after` and carries its `lost`.
- R1 to R21 and R3s of `StoreWriteVectors.json` by shape and by answer; R21 held as the accepted class
  of §639 row 5, never as decided; A7 played past the seal.

## 11. What was not run, and why

- **Start again** (plan 4.2): a later run measures it.
- **The page, the dialog and the press in a window** (plan 4.3): the product's host and end to end
  tiers.
- **A store with no room or at a ceiling**: neither the model nor read.py holds them.
- **A certificate taken away**: neither the model nor read.py holds the kind.
- **Three copies on files, the slow key derivation, a phone, the product.**
