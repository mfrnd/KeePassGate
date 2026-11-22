# Measured: the standing answers to a renewal, on the chain of their texts

A renewal refused forever or until a date, and lifted by **Allow renewal** (`PAIRING.md` §312), is
kept in the store as a version of an object of its own, as a rule is (`STORE.md` §618). S7 of
§646 leaves open the object, the text of each answer and the answer until a date. This report
measures the design proposed for them before anything of it is built in the product, in the model,
over the files `store.py` writes of the model, and on real files. It was run on 2026-11-22 in the
family `spikes\storage\probes\store-as-a-whole\` on the branch `spikes/storage-survival`, as
`run.ps1 -Answers`, with its record in `recorded-answers\`; the family's `RUN.md`, section "The
standing answers to a renewal", has the commands, both runs' times and the comparisons. Nothing ran
in a window or on a phone: the files of the parts on files were made and changed by KeePassLib 2.62
and merged by KeePassLib or `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0.

**How to read it.** An answer is written `ask`, `until-1`, `until-2` or `forever`: no refusal, a
refusal until the earlier of two moments (2026-12-14T23:00:00Z), until the later
(2027-03-31T22:00:00Z), and a refusal forever. Each moment is the first midnight of a day where the
operator is, written in UTC. The **clock** is a gate's own and has three positions, 0 before both
moments (2026-11-22T12:00:00Z), 1 between them (2027-01-15T12:00:00Z) and 2 after both
(2027-06-01T12:00:00Z); beside an answer, `012`, `01`, `0` or `-` are the positions at which it
refuses. The four answers form a **chain**, forever above the later moment above the earlier above
`ask`, and every answer refuses at least wherever each answer below it refuses, so an answer
**below** another on the chain refuses at fewer positions. A copy's **ideal** is the same file had
every record it ever received arrived by a perfect transport. It is taken under two **oracles**:
with a lift's `lost` counted as seen, where a lift that settled a lost version was shown that
version lost and replaces it; and with `after` alone, where it does not. A merge is **beyond both
copies** where the merged copy's answer is below both copies' answers just before it. A
**counterexample** is a history in which a copy answers below its ideal, or a renewal is issued
where its gate's ideal refuses, of none of the classes that `STORE.md` §639 and the evaluation's
decision 9 accept, and not an answer that KeePass ending before a save discarded.

## 1. The design measured

- **The object** is `renew:` and the client key, 64 lowercase hexadecimal characters, the key of
  the client's first certificate, so an answer holds for every certificate of the client. An object
  `renew:` that holds anything else is the object of nothing, and no answer is read from it.
- **The text** of a version is `refuse:forever`, `refuse:until:` and a moment, RFC 3339 in UTC to
  the second in 20 characters, or `ask`, the lift. A text is in form only where composing what was
  read gives the same bytes.
- **What the versions give together.** The strictest standing version is the answer: forever, then
  the later moment, then the earlier, then `ask`; no version and nothing lost, the operator is asked.
  A lost version refuses forever, and so does a text out of form, whatever else stands, stopping
  nothing. Two moments are compared as texts the operators chose; no merge and no reading of what
  stands reads a clock or a time of a write.
- **A deletion record tells of an answer** for a client key the file knows: the client of a record
  of kind `client`, a revocation in a record or a summary item, and the object of a version present
  or restated.
- **What a version names.** An answer names in `after` the standing versions its writer was shown
  that still stand, with what those name in `after`. A refusal is given only where the reading asks
  at the gate's clock, so it settles nothing. A lift is pressed only where the reading refuses at the
  gate's clock, names in `lost` the lost versions it was shown that are still lost, with what the
  versions it replaces name in `lost`, and comes to nothing where it names nothing. What a writer was
  shown is fixed by the dialog: the renewal dialog's reading before it opened, and the settings
  dialog's page at the press.
- **The clock** is read where a renewal is answered and where a dialog shows, and nowhere else: an
  answer until a moment refuses while the gate's clock is before the moment, and a renewal is issued
  only where the gate's reading does not refuse at its clock.
- **The write.** An answer and a lift are written into the open database, as a rule set on a node's
  dialog is (`STORE.md` §629), and kept in the file by its next save, whoever starts it. KeePass
  ending before that save discards them with every other unsaved change.

## 2. What was implemented

**In the model** (`crdt_model.py`, each line marked "ADDED (renewal answers)"; with the switch at
its default every output prints as before). `ANSWERS`, the switch; a version's value holds the
product's text, read by a strict reader of the model's own (`answer_read`), and two versions meet
as the stricter text. The fold tells a deletion record of an answer only for a client key the file
knows and reads the answer from the chain with its cause (`stands`, `lost` or `unreadable`). Each
gate has a clock, set by the act `clock`; the act `see` fixes what a dialog shows; the answer act
names what was shown; `renewcert` is refused where the gate's reading refuses at its clock. An
answer and a lift wait in memory for the next save under `pending`, which every earlier
configuration ran for a rule's act alone, with the acts `save` and `quit`. `lose` makes a deletion
record of a refusal whose content no file holds, and `unreadable` writes, as somebody holding the
store key, an answer whose text is out of form. The properties compare answers on the chain, and
`run` holds a renewal issued to its gate's ideal. `IDEAL_LOST` selects the oracle. **A defect of
the model is closed under the switch**: the completeness check held a revocation by its
certificate, where the summary entry restates the client's key, so revoking a client whose
certificate was renewed stopped the store in the model alone; `read.py` and the product read it
whole. No earlier output depends on it.

**On files.** `store.py` writes an answer's text as it stands and, under `HEX`, every certificate
and client key in hexadecimal. `read.py` reads the answers with a strict reader of its own, the
client keys it knows, and an object that holds no client key as the object of nothing. `bridge.py`
compares every answer that is not `ask`, with its cause, between the model and the file at every
step, and plays `lose` and `unreadable` on files. `vectors.py --answers` prints the vectors of
section 9.

## 3. What was run

| Part | What it is | Size |
| --- | --- | --- |
| `cells` | A1 to A14 on real files, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases, the merged file held to both copies just before the merge, to its own ideal and to every decision any gate made, then the acts that follow | 14 sequences, 252 cells |
| `losses` | an answer until a moment, forever and a lift, lost at copy 0 in six ways, Seal again, a lift, then six merges with the copy from before, on real files | 18 sequences, 108 cells |
| `random` | random histories of answers, lifts, dialogs, clocks, renewals, a pairing and a revocation on real files, Synchronize, Overwrite if newer and KeePassXC 2.8.0 | 300 histories, 1,686 steps |
| `model-hand`, `model-losses` | the same sequences in the model under both oracles, Create new IDs among the merges, A2, A3 and A11 with their writes in either order, every merged copy read again with every time set to 0 | 136 and 144 cells |
| `laws` | the meet over every sequence of one to three of eight texts in every order, and random pairs of files under a perfect transport | 584 sequences, 20,000 pairs |
| `exhaustive-4`, `exhaustive-5` | every sequence of the answers' alphabet over two copies, with the deletions by hand and Seal again to 4 steps, without them to 5 | 1,679,616 and 11,881,376 histories |
| `answers-two`, `-three`, `-hand`, `-long` | random histories of the answers' acts alone, each under both oracles | 200,000 each |
| `search-2`, `search-3` | the design's twelve configurations over two and three files with the answers' acts in the alphabet, the design's seeds | 20,000 a configuration, 480,000 |
| `bridge-two`, `bridge-hand` | every file of random histories written by the Encoder and read by `read.py`, held to the model | 2,000 histories each |

**The alphabet of the answers.** In each copy: each of the four answers about the clients `c0`
and `c1`; a dialog that shows an answer (`see`); the clock set to each of its positions; a renewal
of each client; a pairing; a revocation of `c0`; a save; and KeePass ending before a save
(`quit`). `hand` adds, in each copy, the deletion of an answer's newest version and of the newest
summary entry, each with a deletion record or with none, Seal again, every deletion record
cleared, a lost answer of each client (`lose`) and a text out of form (`unreadable`). Six merges:
Synchronize, Overwrite if newer and KeePassXC, either way. An act is drawn with 0.6, a merge with
0.4. The design's search draws from the design's alphabet, its answers replaced by these, as
`store_runs.py search` draws, with its probabilities and its seeds.

Everything ran twice, the second run printing every output of the first line for line but the
running times, and the three files of vectors byte for byte but the line ends.

## 4. The sequences, side by side

What the copy merged into answers about the client after the merge, by every merge but Create new
IDs, both ways, on real files; the model gives the same in every cell, and the file agreed with the
model at every step of every cell, entry by entry and in the read.

| Sequence | The merged copy | Note |
| --- | --- | --- |
| **A1** forever at copy 0, nothing at copy 1 | forever (`012`) both ways; a renewal refused | the refusal holds wherever a merge carries it |
| **A2** forever at copy 0, until the later moment at copy 1, apart | forever | the stricter |
| **A3** until the earlier moment at copy 0, until the later at copy 1, apart | until-2 (`01`); a renewal refused at positions 0 and 1 and issued at 2 | the later moment |
| **A4** forever, synchronized, lifted at copy 1, which saw it | ask; a renewal issued | under KeePassXC into copy 1 the replaced refusal comes back beside its deletion record and stays out of force |
| **A5** until the earlier moment, synchronized; copy 0, its clock past the moment, refuses forever, which copy 1 never sees; copy 1 lifts | forever | the lift named the earlier answer alone |
| **A6** copy 0's renewal dialog shows ask; a Synchronize brings forever while it stands; copy 0 refuses until the later moment | forever | the refusal written under the dialog names nothing, and the forever stands beside it |
| **A7** forever, synchronized; at copy 0 its version and the summary entry deleted with deletion records, Seal again, the lift; copy 1 from before | ask into copy 0 and by Synchronize into copy 1; into copy 1 by Overwrite if newer, Overwrite existing, Keep existing and every KeePassXC release, forever, narrower | Seal again writes `refuse:forever` naming the loss in `lost`, and the lift carries it on; into copy 1 the version settled in `lost` turns up with its content and stands beside the lift |
| **A8** forever, synchronized, lifted at copy 0; the lift's version and the summary entry deleted at copy 0 with deletion records | copy 0's store stops in every cell, and Synchronize stops copy 1; into copy 1 by the other methods and every KeePassXC release, forever, narrower | refusing or narrower, never wider |
| **A9** c0 revoked at copy 0 and refused until the later moment at copy 1; c1 refused forever at copy 0 and renewed at copy 1, which had not seen it | c0 until-2 and revoked, c1 forever; the renewed certificate's renewal refused | an answer about the client covers its renewed certificate |
| **A10** c0 forever, c1 until the earlier moment, the clock past both moments; renewals of c0 and c1, then c1 forever | c0 forever, c1 forever; at the clock past both, c0's renewal refused, c1's issued before its forever and refused after | the answer whose moment passed stands, refuses nothing, and the next refusal names it |
| **A11** forever at copy 0 and until the later moment at copy 1, apart; copy 0's settings page shows forever, a Synchronize brings the other refusal, and the press lifts what it showed | until-2 | the refusal made apart stands |
| **A12** forever, synchronized; copy 0's page shows it; copy 1 lifts it and refuses until the later moment; a Synchronize brings both; copy 0's press | until-2 | the press, whose version was replaced since, comes to nothing and writes nothing |
| **A13** a deletion record of a refusal whose content no file holds reaches copy 0; copy 0 lifts | ask | the lift names it in `lost` |
| **A14** a text out of form beside until the later moment; copy 0 lifts both | ask | before the lift, forever with the cause `unreadable` |

**The cells.** Of 252 cells on files, **none is below its own ideal and none beyond both copies**.
Against every decision any gate made, every import cell (Create new IDs) that is wider is the
importing copy staying stale (§640): 7 cells, of A1, A2, A3, A5, A9 and A10. A7 is narrower in 8
cells and A8 narrower in 8 and refusing in 10, each a version settled in `lost` turning up with
its content, or the store stopped by a deletion record of a summary entry. In the model, the 136
cells of A1 to A14 and of A2, A3 and A11 with their writes in the other order are none below the
ideal with a lift's `lost` counted as seen; with `after` alone, the 5 cells of A7 in which copy 0's
lift arrives are below it, which section 8 explains.

## 5. Lost versions

An answer at copy 0, synchronized into copy 1, which keeps it from before; at copy 0 its newest
version deleted for good with a deletion record or with none, and the newest summary entry kept,
deleted with a deletion record or with none; Seal again where the store stops; the dialog, and a
lift where the answer refuses. Then six merges both ways with copy 1, on real files.

| What was deleted at copy 0 | Copy 0 after the losses | After the merges with copy 1 |
| --- | --- | --- |
| the version, either way; the summary entry kept | nothing is lost: the item restates the text, the answer stands, Seal again does not apply, the lift names it | ask in every cell |
| the version and the summary entry, each with a deletion record | the store stops; Seal again writes `refuse:forever` naming the version in `lost`; the lift names that refusal and carries the loss on: ask | Synchronize both ways ask; into copy 1 by Overwrite if newer and KeePassXC 2.8.0 the version from before stands beside the lift: until-2 or forever, narrower |
| the version with a deletion record, the summary entry with none | as above | every merge brings back the version and its summary entry from before: refused, narrower |
| the version with no deletion record, the summary entry with a deletion record | the store stops; Seal again finds nothing lost and the answer is asked again (§639 row 5); no lift is offered | the version comes back: refused; Synchronize leaves the store stopped, the version not restated by copy 0's summary entry |
| the version and the summary entry with no deletion record | as above, §639 row 5 | the version and its summary entry come back: as decided |
| a lift, in each of the six ways | ask, its versions lost and named by Seal again's refusal and the lift that follows, or never lost | ask in every cell; Synchronize stops the store where copy 0's summary entry was deleted with a record and the version with none |

Of the 108 cells, **none is below its own ideal and none beyond both copies**; against every
decision any gate made, 83 are as decided, 16 narrower and 9 refusing. In the model, the same
sequences with Create new IDs among the merges give 4 cells below the copy's own ideal: the version
deleted with no deletion record, the store sealed again so that the answer is asked again, and an
import that brings nothing back, which is §639 row 5. With `after` alone, 12 more cells are below
it: Seal again's refusal names the lost version in `lost`, and the lift after it gives `ask`.

**What the lost versions sharpen.** A version deleted while a summary entry in force restates it is
not lost: the item holds its text, it stands, and a lift names it in `after`. A version is lost only
where nothing restates it, and then the store stops where the summary entry was deleted with a
deletion record, and Seal again writes `refuse:forever` for it, naming it in `lost`; the lift that
follows names that refusal in `after` and carries the loss on. A lift names a lost version in its
own `lost` where a deletion record arrives whose content no file holds (A13), the store staying
whole.

## 6. No merge decides by a time

- **The clock was read** by the dialog, the answer, the renewal and the clock's own act, 50,919,549
  times over every part of the model, and by the oracle where a renewal issued is held to the
  gate's ideal, 5,847,908 times; **never inside a fold, a merge, a write of a summary entry or any
  other act**, in any part.
- **The order of two writes made apart** decides nothing: A2, A3 and A11 played with their two
  writes in either order, so that either version is the later by its modification time, give the
  same answer in 24 of 24 cells.
- **The modification times** decide nothing in the reading: every merged copy of `model-hand` and
  `model-losses` read again with every modification time and every deletion record's time set to 0
  gives the same answer and the same store, 280 of 280.
- **Two moments are compared as texts**, the later the stricter: in A3 the later moment stands in
  every cell, whichever copy wrote last.

What times do decide is KeePass's transport, as before: a deletion record dated before an entry
leaves the entry in KeePass's Synchronize (configuration D of the search, a clock behind), and
which content of an entry wins where both copies hold the same identifier. Neither decides between
two answers, which are two entries.

## 7. The search

**The answers' acts alone**, 200,000 histories each, every history played under both oracles:

| Search | Failing a property that ends a history | KeePass ending before a save discards an answer (accepted) | A lift discarded so (the stricter way) | A renewal issued at a clock ahead |
| --- | --- | --- | --- | --- |
| two copies, 4 to 12 steps | **none** | 4,750 | 61 | 228 |
| three copies, 4 to 12 steps | **none** | 3,873 | 14 | 131 |
| two copies, 8 to 16 steps | **none** | 8,334 | 187 | 650 |
| two copies, with deletions by hand, Seal again, every deletion record cleared, a lost answer, a text out of form | 49 properties failing, every history of §639 rows 2 to 5 | 1,965 | 54 | 61 |

Both oracles count the same in all four. **The failures of the search with deletions by hand**,
each shrunk and classed by what it holds: `safety` 21, of which 20 are an answer's version and the
summary entry restating it deleted and a merge that carries no deletion record (§639 rows 2 and 3)
and 1 every deletion record removed while a loss stands (row 4); `one-loss` 27, a single deletion
that leaves the copy asking where it refused just before, of which 24 are rows 2 and 3, 1 is row 3
alone (every deletion with no deletion record) and 2 are row 4; and `sealed-after-a-loss` 1, Seal
again after a loss in both carriers with no deletion record (row 5). A history can fail two
properties at the same step. The shortest of rows 2 and 3: `renew 0 c0 until2 ; merge kp_other 0
1 ; del 0 (ver renew:c0 new) 1 ; merge kp_other 1 0 ; del 1 (head new) 0`: copy 0 deletes the
version with a deletion record while its summary entry still restates it, Overwrite if newer
delivers the summary entry alone into copy 1, and copy 1 deletes it with no deletion record, so
copy 1 is its older self, which row 3 names. **No history is of another class.**

**Every sequence** of the answers' alphabet about c0 over two copies (the four answers in each
copy, the dialog, the clock to positions 1 and 2, a renewal, a save, KeePass ending before a save,
and six merges): to 4 steps with the deletions by hand of the answer's newest version and of the
newest summary entry, with a deletion record or with none, and Seal again, 1,679,616 histories,
36 steps wide, **no failure**; to 5 steps without them, 11,881,376 histories, 26 steps wide, **no
failure**. The convergence was checked at the end of each.

**The design's search**, its twelve configurations over two and three files, 20,000 histories
each with its seeds, the answers' acts in its alphabet, 480,000 histories:

| Files | Failing a property that ends a history, by class | Discarded answers (accepted) | Renewals at a clock ahead |
| --- | --- | --- | --- |
| two | `safety` 4: about a renewal answer 3, each a store key lost (the authority's entry deleted or edited, then Create, decision 9); about a retirement 1, §639 rows 2 and 3. `rollback-by-overwrite` 104, row 1 | 684 | 3 |
| three | `safety` 1, about a retirement, §639 rows 2 and 3. `rollback-by-overwrite` 163, row 1 | 466 | 6 |

The shortest renewal failure: `renew 0 c0 forever ; del 1 (auth) 1 ; create 1 ; merge kp_sync 0 1`:
copy 1 loses its authority's entry, starts a store under a new key, and Synchronize into copy 0
brings that store, under which copy 0's answer, written under the earlier key, is passed over, as
decision 9 accepts; the oracle reads under every key of the lineage. **No history of the search
is of another class.**

**The laws of the meet.** Every sequence of one to three of eight texts (the four of the answers,
a third moment, and three texts out of form) in every order gives a single meet, the stricter, which
refuses wherever any of the texts refuses: 584 sequences, 0 violations; refusing is monotone in the
chain at every position of the clock, 0 of 64 pairs. In 20,000 random pairs of files under a
perfect transport, no joined file answers below any of the 21,824 versions standing in a copy that
the other copy's versions do not name.

**The bridge.** In 4,000 random histories of the answers' alphabet, with and without deletions by
hand, every file written by `store.py`'s Encoder with every client key in hexadecimal and read by
`read.py` gave the model's answer and cause and the same store state: 21,315 steps, 0 differing.
On real files, `cells`, `losses` and `random` disagreed with the model at no step.

## 8. The two oracles, and an answer that waits for a save

**A lift's `lost` counted as seen.** The ideal with `after` alone holds every refusal no version
names in `after`, so a lift that settles a lost refusal leaves the refusal standing in that ideal,
and every copy holding the lift answers `ask` below it: the 5 cells of A7 and the 12 of the lost
versions, and none in the four searches of the answers alone, whose deletions by hand reach a lift
after Seal again in no history drawn. That is the design and not a defect of it: the operator was
shown the client refused with the answer lost, and **Allow renewal** lifts it. So the property is
asked with a version that a version of the same answer names in `lost` counted as seen, and both
oracles are printed side by side in every part that runs both: they differ in those 17 cells and
nowhere else. The design's search and the exhaustive parts ran under the first oracle alone. The
store itself never reads `lost` as replacing: a version settled in `lost` that turns up with its
content stands beside the lift and refuses (A7, and the lost versions into copy 1), which is
narrower than both oracles.

**An answer and a lift wait for a save.** In the searches, as for a rule set on a node's dialog, an
answer and a lift are written into the open database and kept by the next save (`pending`): the
gate's own reading has them at once, a renewal at that gate is refused by them, and another copy
merges the file on disk without them. Any later write of the plugin's own that saves keeps them, a
renewal or a pairing at that gate among them, and so does KeePass's own Synchronize there, which
saves. **KeePass ending before that save** (`quit`) discards them with every other unsaved change:
the file keeps what it held, and the copy's ideal loses the discarded record with it, since no file
ever received it. A discarded refusal leaves the copy answering below what it answered before the
quit, the wider state; the runs count it as `accepted:unsaved-answer-lost`, the class a save left
to KeePass accepts, and not as a counterexample: 18,922 histories in the searches of the answers
alone, 770,678 sequences of the exhaustive parts and 1,150 histories of the design's search. A
discarded lift leaves the refusal standing, the stricter way, counted as `info:unsaved-lift-lost`:
316, 2,070 and none in the same parts. **The property is stated around it in two parts**: after
every step, a quit among them, the copy is held to its ideal, which has lost the discarded record
too, and that holds in every history; the comparison of the copy after a quit with the copy just
before it is the class, and only the class. No discarded answer reached another copy in any
history: a merge from a copy whose answer waits reads the file on disk.

**A clock ahead.** A gate whose clock is ahead of another copy's reads an answer until a moment as
passed sooner, raises the renewal dialog, and issues the renewal on the operator's Approve; the
property is asked at the gate's own clock, and such a renewal is counted apart as
`info:renewal-at-a-clock-ahead`: 1,070, 29,556 and 9 in the same parts. No clock issues a renewal
without the dialog, since `renewcert` is the approved renewal, and a clock behind refuses for
longer.

## 9. The vectors

`vectors.py --answers` prints the three files as `--lost` prints them and adds the vectors of the
answers under names of their own; `--lost` prints `recorded-lost\vectors\` byte for byte but the
line ends, and those are the product's files but their line ends.

| File | Held today | Printed | Added | Changed or removed |
| --- | --- | --- | --- | --- |
| `StoreRecordVectors.json` | 36 records | 47 records, and `renewalTexts` | 11 records, `renewalTexts` with 36 texts | none |
| `StoreMeaningVectors.json` | 84 stores | 112 stores | 28 stores, their names beginning `renewals:` | none |
| `StoreWriteVectors.json` | 49 sequences | 63 sequences | 14 sequences, A1 to A14, their names beginning `answers,` | none |

**What is added.** To the records: a version of an answer in each of the three texts, a refusal
until the later moment made apart, a lift naming the refusal it replaces, a refusal forever whose
content no store holds, a lift settling it in `lost`, a lift naming two refusals made apart, a
refusal naming the lift it replaces and what the lift replaced, a lift replacing a refusal and
settling a lost answer, an answer whose text is out of form, and a summary entry restating four of
them; and `renewalTexts`, 36 texts of an answer with what `read.py`'s reader makes of each, 6 in
form. To the meanings: stores of answers about the admission's client and what `read.py` answers
for it at each position of the clock, its cause, the texts that stand, the lost versions and
`notBefore`: no answer, each text alone, each pair made apart, a lift naming a refusal and naming
a single refusal of two, a lost answer, settled and settled beside a refusal the lift did not see,
a deletion record of a replaced version, a version beside its own deletion record, alone and named
in `after` and in `lost`, a version carried only by an item, with and without its deletion record,
a text out of form, alone, beside a moment and named by a lift, a client key the file does not
know, a key a revocation makes known, a key only the answer makes known, and two objects that hold
no client key. To the writes: A1 to A14, each step with the copy's clock and its answers at each
position, and each Synchronize with the answers below both copies, of which there is none.

**Nothing held today changed.** Every member of the three files as they stand prints the same, and
the line diff of each file against `recorded-lost\vectors\` adds 317, 2,421 and 35,143 lines and
replaces one: the line of `about`, whose text is the text held today with 1,713, 1,884 and 2,009
characters added after it, describing what is added. The files are 81,880, 614,885 and 7,509,683
bytes.

## 10. Where the model sharpens the design

1. **A version deleted while a summary entry restates it is not lost** (section 5). A lift after
   such a deletion names the version in `after`, from its item; only where nothing restates it is
   the version lost, and then the store stops or the deletion record arrived with its content
   nowhere. The lift that "names the loss in `lost`" after a deletion by hand with a deletion record
   is, in two copies, a lift after Seal again, carrying Seal again's `lost` on (A7), or a lift after
   a deletion record whose content no file holds arrives (A13).
2. **The property needs a lift that settled a lost refusal to count as having seen it** (section
   8). With `after` alone the oracle is above every copy that holds such a lift; the store is right,
   and it is the oracle that has to count `lost`.
3. **A version settled in `lost` that turns up with its content refuses again**, beside the lift
   that settled it (A7, section 5). Narrower, never wider; the operator presses **Allow renewal**
   again, and that lift names it in `after`.
4. **A press whose versions were all replaced since comes to nothing** (A12), and a refusal that
   arrives under an open page stands beside the lift (A11), as the settings tab's buffer has it.
5. **A refusal is given only where the reading asks**, so a gate that holds a refusal raises no
   dialog for it and writes no further refusal; a refusal given after the clock passed a moment
   names the passed answer and replaces it (A10, and A5 at a clock between the moments).
6. **The model's completeness check read a revocation by its certificate** (section 2), so in the
   model alone revoking a client whose certificate was renewed stopped the store. It is closed under
   the switch; the product's reading holds the revocation by the client's key already.
7. **An object of type `renew` that holds no client key** is the object of nothing in `read.py`, as
   §658 has it, and the vectors hold two such stores. A deletion record of an answer is told only
   for a client key the file knows; a key known again later is told from then on.
8. **A clock ahead lets a renewal through after the dialog** (section 8): the answer until a moment
   is read at the gate's own clock, and the measurement counts each such renewal.

None of these changes the design: each is what the design gives, stated more exactly than before,
and no history of any part ended below its ideal outside the accepted classes.

## 11. The property, as it stands

For every copy whose store is whole and serves, **after every step of any history of answers,
lifts, dialogs, clocks, renewals, pairings, revocations, deletions, Seal again, saves, KeePass
ending before a save and merges, the copy's answer about each client is not below its ideal on the
chain**, where the ideal is the same file had every record the copy ever received arrived by a
perfect transport, and a version that a version of the same answer names in `lost` counts as seen.
The chain being total and refusing monotone in it, that holds at every position of the clock at
once. **No renewal is issued at a gate whose ideal refuses at the gate's clock**: a renewal is
never issued where a copy had refused it and no lift that saw that refusal, in `after` or in
`lost`, exists. **Merges alone converge**, and the meet over the chain is the stricter in any
order. An answer and a lift wait in the open database for the next save, and KeePass ending before
it discards them, the file keeping what it held: the ideal after that step has lost the discarded
record too and the property holds against it, while the copy answering below what it answered
just before is the accepted class `accepted:unsaved-answer-lost`, counted and never a
counterexample. Outside the property, and counted: a store key lost (decision 9); and the classes
of §639, rows 1 to 5, each a deletion by hand of an answer's version and the summary entry that
restates it with a merge that carries no deletion record, Seal again after a loss with no deletion
record, every deletion record removed while a loss stands, or a whole file written over another.

## 12. What the product has to satisfy

- `RenewalText` takes exactly the texts `renewalTexts` holds as in form, with their answer and
  moment, and refuses every other, nothing throwing; a version of an answer whose text is out of
  form refuses forever with its cause and stops nothing.
- The object is `renew:` and 64 lowercase hexadecimal characters; any other object of type `renew`
  is the object of nothing.
- The answer is the strictest standing version, the later moment the stricter; a lost version
  refuses forever; no version and nothing lost asks. Whether it refuses is read against the clock
  where a renewal is answered and where the clients page draws a row, and nowhere else.
- A deletion record of an answer is told for the client keys the file knows, from a record of kind
  `client`, a revocation in a record or a summary item, and the object of a version present or
  restated. `StoreInForce` tells nothing at the place of an object of type `renew` today, so the
  stores of `StoreMeaningVectors.json` that hold a lost answer read it as forever only once it does.
- An answer names in `after` the standing versions its writer was shown that still stand, with
  what those name in `after`; a lift names in `lost` the lost versions it was shown that are still
  lost, with what the versions it replaces name in `lost`, no identifier in both, and is composed to
  nothing where it names nothing; a refusal holds no `lost`. `lost` replaces nothing.
- A renewal approved is refused at its write where the answer read there refuses at the clock.
- Seal again writes `refuse:forever` for an answer that refuses for a lost version, naming it in
  `lost`.
- **The answer records are records of kind `version`** in `StoreRecordVectors.json`, under names
  beginning `version, an answer`, `version, a lift` and `version, a refusal`. The product's
  `RuleTextTests.Versions` takes every record of kind `version` as a rule's, and has to take the
  objects of rules alone once these vectors are its; the texts of the answers are under
  `renewalTexts`, so that the texts of rules under `texts` stay as every test reads them.

## 13. What was not run, and why

- **The product**: the design is measured here before anything of it is built.
- **A window, a phone, the slow key derivation**, and KeePassXC releases other than 2.8.0 in the
  random part on files: the merges of `cells` hold all four releases.
- **Rules per profile beside the answers**: the answers are objects of their own, and the store as
  decided with a node's whole rule is the store their search ran in before.
- **An answer held in the plugin's memory, and an answer that could not be written**, have no
  record and are not in a model that holds files.
- **The settings tab's buffer as a buffer**: a press and its write at OK are one act after a
  `see`, so what a merge brings between the press and OK is what a merge brings between `see` and
  the act.
