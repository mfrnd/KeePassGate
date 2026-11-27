# Measured: what a summary entry restates of a decision a deletion record alone tells of

**A revocation, a retirement or a removal of a name** whose record and summary entry are deleted
with a deletion record each lives on in the file as that deletion record alone, and Seal again
(`STORE.md` §624, §625) writes a summary entry over what the reading still holds. The product's
reading tells such a deletion record only for a subject that a record or an item of the file still
names, and its seal restates only what it tells; the model the seal was measured in told every such
deletion record by the element it names. This report measures what the product's form leaves, and
a form that closes it, before anything of it is built in the product, in the model, over the files
`store.py` writes of the model, and on real files. It was run on 2026-11-27 in the family
`spikes\storage\probes\store-as-a-whole\` on the branch `spikes/storage-start-again`, as
`run.ps1 -Restates`, with its record in `recorded-restates\`; the family's `RUN.md`, section "What a
summary entry restates of a decision told by deletion alone", has the commands, both runs' times and
the comparisons. Nothing ran in KeePass's window or on a phone: the files of the parts on files were
made and changed by KeePassLib 2.62 and merged by KeePassLib or `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12
and 2.8.0.

**How to read it.** `c0` is the client every history starts with, admitted by the first authority
`a0`; `pf` is the profile name it holds. Copy 0 is the copy sealed; **the copy from before** is a
copy that never held the decision and still holds what the decision took away (`c0`'s admission,
`a0` standing, `pf`'s addition). A copy's **own ideal** is the same file had every record it ever
received arrived by a perfect transport; the **oracle** is the same over every record any gate ever
wrote; a copy is **wider** where it grants what its own ideal does not. **The control** is the
model's form before this measurement, the form every part of Seal again and of a store started
again was run in: it reads a deletion record by the element it names, which the product cannot, so
the control is the ideal of what a seal could restate, and every form is held to it history by
history.

## 1. The forms measured

`crdt_model.SEAL_RESTATES`, read with Seal again on, holds the form. In every form, a decision a
record or an item names by its subject is restated by its subject, as before.

| Form | A deletion record of a revocation, a retirement or a removal of a name | What a summary entry restates of it |
| --- | --- | --- |
| `all`, the control | told by the element it names, whatever the file names | the decision, by its subject |
| `named`, the product's | told only for a subject the file names: a client of a record of kind `client` or of a revocation in a record or an item; an authority of a record of kind `authority` or of a retirement in a record or an item; a name of a record of kind `profile` or `removed` or of a removal in an item | what is told, by its subject; nothing of the rest |
| `ids`, the form proposed | as `named` | as `named`, and a decision whose subject nothing names by an item holding the identifier of its deletion record and the kind, `{"id", "kind"}`, carried by every summary entry after it while nothing names the subject |
| `hashes` | as `named` | as `ids`, but a revocation or a retirement by the identifier's keyed tag and subject hash alone, `{"hash", "kind"}`; a removal of a name by its identifier, as in `ids` |

**An item with no subject** restates the record whose identifier it holds (with `hash`, every
record of its kind whose identifier begins with it), and tells its decision of every subject the
file names whose keyed hash under the store key is the hash it holds, so that a subject named again
later, an admission merged back in, is refused by computing its hash. Once a subject is named, the
next summary entry restates its decision by the subject, and the item is not carried further. An
addition of a name names, in `after`, every removal of it that a deletion record or such an item
tells of, counting the name being added as named (`STORE.md` §627, as the product's addition does).

## 2. What was implemented

**In the model** (`crdt_model.py`, each line marked "ADDED (seal restates)"). The switch, with the
product's form as its default; with it set to `all`, or Seal again off, every output prints as
before, and every earlier configuration's setup keeps `all` unless the environment names another
form in `SEAL_RESTATES`, so that a part of Seal again or of a store started again can be run under a
form unchanged. In the fold, under a form other than `all`: the subjects the file names, read as
`read.py` reads them; a deletion record of a decision told only for a subject among them; the items
with no subject read, telling a decision of a subject named and restating their records in the
completeness check. `seal_carried` composes the items a summary entry holds, written only where a
decision needs them.

**On files.** `store.py` writes the two items; `read.py` reads them (`subject_item`), in the
completeness check and in the listing's records no summary entry restates. `probe/restates.py`
plays the sequences on files under a form; `model/restates_runs.py` plays every history under every
form beside the control; `compare_restates.py` holds the forms to the control and to the records;
`vectors.py --restates` prints the vectors.

## 3. What was run

| Part | What it is | Size |
| --- | --- | --- |
| `hand` | R1 to R21 of Seal again and F5 to F7r below, in the model under every form, every merge both ways by Synchronize, Overwrite if newer, KeePassXC and Create new IDs, with nothing before it and with every deletion record of copy 0 cleared before it | 27 sequences, 432 cells a form |
| `cells-<form>` | the same sequences on real files under each form, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases, with and without the deletion records cleared, each merged file held to the oracle, to its own ideal and to both copies just before | 972 cells a form |
| `seal-two`, `-three`, `-long` | the random histories of Seal again's parts of those names, the same counts and seeds, each under every form beside the control | 200,000, 100,000, 100,000 |
| `search-2`, `search-3` | the histories of Seal again's search, the design's twelve configurations with their seeds, each under every form | 240,000 each |
| `exhaustive-seal-4` | every sequence of 4 steps of Seal again's exhaustive alphabet after its divergence, under every form | 2,560,000 |
| `exhaustive-told-4` | after each of a revocation, a retirement and a removal at copy 0 whose record and summary entry are deleted with a deletion record each, copy 1 the copy from before, every sequence of 4 steps of the seal, every deletion record cleared, a summary entry deleted with a deletion record or with none, a save, a pairing, the same decision made apart at copy 1, and every merge both ways | 390,625 after each, 1,171,875 |
| `bridge`, `bridge-hand` | every file of 2,000 random histories and of every cell of `hand` written by the Encoder, read by `read.py` and held to the model's fold, every revocation and retirement among it, under every form | 9,700 and 5,732 steps a form |
| the parts of Seal again under `named`, `ids` and `hashes` | `edits` (on files), `hand`, `damage`, `bridge`, held to `recorded-seal\` | 4 parts a form |
| the parts of a store started again under `ids` and `hashes` | every part of `run.ps1 -Starts`, on files and in the model, held to `recorded-starts\` | 20 parts a form |

The store is the store of Seal again's runs (the store as decided, the standing answers to a
renewal, a rule or an answer waiting for a save, the reading of lost versions, Seal again as
designed). A history **differs from the control** where under the form it fails a property of
`crdt_model.run` that ends a history and under the control does not, or ends with a copy stopped,
wider than its own ideal, or granting less than the same copy under the control. **Every part ran
twice** (section 10).

**The sequences F5 to F7r**, added for this measurement. In R5, R6 and R7 of Seal again the copy
merged after the seal was synchronized after the decision; here copy 1 is the copy from before.

| Sequence | At copy 0 | Copy 1 |
| --- | --- | --- |
| F5 | `c0` revoked; the revocation and the summary entry deleted with a deletion record each; Seal again | from before: `c0` admitted |
| F6 | the authority recreated; the retirement and the summary entry deleted the same way; Seal again | from before: `a0` standing |
| F7 | `pf` removed; the removal and the summary entry deleted the same way; Seal again | from before: `pf` added |
| F5r, F6r, F7r | as F5, F6, F7 | the same decision made on its own: a record of it under another identifier |

## 4. What the product's form leaves

**It widens.** In F5 and F7, once copy 0 is sealed in the form `named`, its summary entry restates
nothing of the revocation of `c0` or of the removal of `pf`, which live on as a deletion record
alone. Two ways bring the copy from before's admission or addition back:

- **into the copy from before, by a merge that carries no deletion record**: KeePass's Overwrite if
  newer, Overwrite existing and Keep existing, and every KeePassXC release. Copy 1 takes copy 0's
  summary entry and none of its deletion records, nothing it holds names a decision about `c0` or
  `pf`, and `c0` is admitted, or holds `pf` once more. With nothing cleared: 7 cells of each
  sequence on files.
- **into either copy once copy 0's deletion records are cleared** (Database Maintenance, or a
  program that keeps none): by every method but Create new IDs, Synchronize among them. 16 cells of
  each.

Under the control, the same cells are within their own ideal: the seal's summary entry restates
`revoked c0` and `removed pf` by subject, and the admission or the addition that comes back is
refused. **F6 grants less rather than more**: the retirement of `a0` is lost, the copy holds `a0`
and the new authority standing, and serves nothing until the chooser settles it.

| Part | Form `named` against the control |
| --- | --- |
| `cells-named`, 972 cells | 69 differ: 46 wider than their own ideal and than the oracle (23 of F5, 23 of F7), 23 serving nothing where the control serves (F6); none beyond both copies |
| `hand`, 432 cells | 24 differ: 16 wider (12 with the deletion records cleared), 8 granting less (F6) |
| `exhaustive-told-4`, after a revocation | 815 histories fail where the control does not (`sealed-after-a-loss`); 765 end wider; 9 end stopped |
| `exhaustive-told-4`, after a retirement | 815 end granting less; 9 end stopped |
| `exhaustive-told-4`, after a removal | 791 fail (`sealed-after-a-loss`); 733 end wider |

**The shortest**, three steps after the decision and its two deletions: `reseal 0 ; save 0 ; merge
kp_other 1 0` and the same by KeePassXC, the sealed copy merged into the copy from before.

**Seal again's own searches could not have shown it.** Every history the form widens holds a merge
that carries no deletion record, or deletion records cleared, and Seal again's classes put each in
§639 row 5, "Seal again after a loss in both carriers with no deletion record", which they accept.
It is not that class: nothing was lost without a deletion record before the seal; the seal itself
left the decision with a single carrier. Run under `named`, Seal again's random histories, its
search, its exhaustive alphabet and its parts `edits`, `hand`, `damage` and `bridge` differ from the
control in no history and print their records line for line: none of their alphabets reaches a
decision whose subject nothing names, then the copy from before. F5 to F7r and `exhaustive-told-4`
are made for it.

**It also stops a store, more narrowly than first thought.** A revocation record that comes back
from a copy whose own summary entry restates it stops nothing: that summary entry, present in the
merged copy, restates it, as every summary entry present counts for the completeness check. So R5 to
R7, merged both ways by every method and with the deletion records cleared, stay whole under every
form. **A record of the decision with nothing restating it stops the store**: the same decision made
apart at copy 1 (another identifier) and copy 1's summary entry deleted, then merged into the sealed
copy, by Synchronize as well as by a method that carries no record: 9 histories after a revocation
and 9 after a retirement in `exhaustive-told-4`, the shortest `revoke 1 c0 ; reseal 0 ; del 1 (head
new) 0 ; merge kp_sync 0 1`. The store is stopped, narrower and never wider, and Seal again lifts
it again. The control restates the revocation by subject and stays whole.

## 5. The form proposed, `ids`

**Never wider than its own ideal**, in every part: on files 972 cells and in the model 432, each the
same as the control's cell by cell; the random histories, the search and both exhaustive alphabets;
with every merge method both ways, every KeePassXC release, and the deletion records cleared. In F5
the seal's summary entry holds `{"id": <the revocation's identifier>, "kind": "revoked"}`; the
admission of `c0` that comes back is named, its hash matches, and `c0` is refused; the next summary
entry restates `revoked c0` by subject and carries the item no further. The removal of `pf` and the
retirement of `a0` are held the same way, and an addition of `pf` made after the seal names the
removal in `after`, the deletion records cleared or not, where under `named` with them cleared it
names nothing.

**It differs from the control in 18 histories, each narrower**: the 9 and 9 of section 4 in which
copy 1 makes the same decision on its own and loses its summary entry. The record that arrives has
another identifier, so the item does not restate it, and the store stops as under `named`, granting
less; Seal again lifts it.

**The parts of a store started again**, all twenty, run under `ids` print their records line for
line, on files and in the model; so do Seal again's parts.

## 6. A form that does better, `hashes`

**`hashes` never differs from the control**, in any part, and is never wider than its own ideal. It
holds a revocation or a retirement by the identifier's keyed tag and subject hash alone (16
hexadecimal characters under the store key): that is what the reading matches a subject by in any
case, and it restates every record of the decision about the subject, under whatever identifier it
arrives, which is exactly what restating it by its subject does. The 18 histories `ids` stops stay
whole. A removal of a name needs its identifier: an addition counts only once it names every removal
of its name, by identifier, in `after`, so a removal is held as in `ids`; after a removal,
`exhaustive-told-4` finds no history in which either form differs from the control, the removal
made apart at copy 1 among them, since the control too holds a removal by its identifier.

**Its cost**: an item of 44 characters for a revocation or a retirement, against 58 for `ids`' and
94 for a revocation item naming its client; one item per decision, as the item by subject it stands
in for. **No other form does better** among those that hold nothing more: the subject itself is
what the product cannot recover from a deletion record, and the tag and the hash are all of the
identifier that is about the subject; the rest, the height and the check, tell the records of a
decision apart, which is what makes `ids` narrower.

**The parts of a store started again and of Seal again run under `hashes`** print their records
line for line as well (section 10).

## 7. What it costs elsewhere

**Nothing changes where no decision is told by deletion alone with its subject unnamed.** A summary
entry holding an item with no subject was written in 19 of 200,000 histories of `seal-two`, 1 of
100,000 of `seal-three`, 35 of 100,000 of `seal-long`, 2 of 240,000 of `search-2`, 1 of `search-3`,
6,888 of 2,560,000 of `exhaustive-seal-4` and about 55,000 of each 390,625 of `exhaustive-told-4`,
and in none of them outside `exhaustive-told-4` did any form differ from the control; every other
summary entry is the same as before, item for item. The bridge reads every file under every form as
the model does: 0 steps differ of 9,700 and 5,732 under `named`, `ids` and `hashes`, and every
seal read.py plans is the seal the model writes. **Under the control**, read.py differs from the
model in 1 step of 9,700 and 212 of 5,732: the steps where the model reads a revocation or a
retirement a deletion record tells of whose subject nothing names, which read.py cannot, and which
the earlier bridge, comparing whether the store is whole and what a lost version refuses, did not
compare.

## 8. What the product has to satisfy

- The seal's summary entry, and every summary entry after it while nothing names the subject,
  restates a revocation or a retirement a deletion record under the store key tells of and whose
  subject no record or item names, by an item holding the kind and the deletion record's keyed tag
  and subject hash, `{"hash": <16 hexadecimal characters>, "kind": "revoked"}` or `"retired"`; and a
  removal of a name by an item holding the kind and the deletion record's identifier,
  `{"id": <32 hexadecimal characters>, "kind": "removed"}`. Neither names a subject.
- The reading reads such an item as stating its decision for every subject the file names whose
  hash under the key matches, as a deletion record does; holds a record of the kind whose identifier
  begins with the hash, or a removal with the identifier, as restated; and counts the item as a
  carrier of the decision wherever a deletion record counts.
- Once the subject is named, the summary entry restates the decision by its subject and the item is
  carried no further.
- An addition of a name names in `after` a removal of it that such an item tells of, as it names a
  removal a deletion record tells of.
- With `ids` in place of `hashes` for a revocation and a retirement, the same, narrower in the case
  of section 5.

## 9. The vectors

`vectors.py --restates` prints the three files of `--starts` with the sequences of this
measurement added under names of their own, played under `ids`, the form proposed when the runs
were made; `--starts` prints `recorded-starts\vectors\` byte for byte but the line ends.

| File | Held today | Printed | Added | Changed or removed |
| --- | --- | --- | --- | --- |
| `StoreRecordVectors.json` | 56 records | 56 records | none | none |
| `StoreMeaningVectors.json` | 139 stores | 139 stores | none | none |
| `StoreWriteVectors.json` | 101 sequences | 146 sequences | 45 sequences, their names beginning `restates,` | none |

The sequences are R5, R6, R7 and F5 to F7r, each followed by five merges: Overwrite if newer and
KeePassXC into copy 1 as sealed, and Overwrite if newer, KeePassXC and Synchronize with copy 0's
deletion records cleared. **Nothing held today changed**: the line diff against
`recorded-starts\vectors\` adds 165,424 lines and replaces a single line, the line of `about`, whose
text is the text held today with 1,455 characters added after it. Every step holds each summary
entry's items, the items with no subject among them. **In the form `hashes`** the sequences hold the
same answers; their revocation and retirement items would hold the hash in place of the identifier.

## 10. The two runs

`run.ps1 -Restates -Parallel 4`, from the branch root in Windows PowerShell 5.1 under an ordinary
user token, ran every part twice, in turn. **Every output of run 2 is the same as
`recorded-restates\` line for line**, with the masks of `compare.py`: 70 files, the listings of the
cells, of the parts held to their records and of the vectors among them, and the three files of
vectors the same bytes but the line ends. The parts of Seal again and of a store started again
under `hashes` were added once run 2 had ended, and ran twice after it with `-Parts`, the second
time the same as the first line for line. With the
switch unset, every earlier part run again, Seal again's and a store started again's among them,
printed its record line for line, and `vectors.py --starts` the files of `recorded-starts\vectors\`
byte for byte but the line ends. The times of both runs are in `RUN.md`; the longest part,
`exhaustive-seal-4`, took 5,956 and 5,730 seconds.

## 11. What was not run, and why

- **The product**: the forms are measured here before anything of them is built.
- **The dialog over a store that holds an item with no subject**: what Seal again shows of such a
  store is the listing's, which reads the item as a carrier of the decision; the dialog was not
  drawn.
- **A key past the ceiling on keys**, a second gate over a socket, a window, a phone and the slow
  key derivation.
