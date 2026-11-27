# Measured: a store started again, and the keys it sets aside

**Start store again** (`STORE.md` §632, §641) abandons the store of a database and makes a store
under a new key, setting every other key the reading finds aside by the keep records of §649. This
report measures that command, with the keys set aside it rests on, before anything of it is built in
the product, in the model, over the files `store.py` writes of the model, and on real files. It was
run on 2026-11-26 and 2026-11-27 in the family `spikes\storage\probes\store-as-a-whole\` on the
branch `spikes/storage-start-again`, as `run.ps1 -Starts`, its output in `out\starts\`. Nothing ran
in KeePass's window or on a phone: the files of the parts on files were made and changed by
KeePassLib 2.62 and merged by KeePassLib or `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0. The
vectors of the product's tests and the family's record of these runs are not part of this report.

**How to read it.** `K` is the key of the store every history starts with, whose first authority is
`a0` and whose client is `c0`; `K4`, `K8` and so on are keys a start again or a Create made,
numbered as the model numbers them, each with the authority of the same number. A copy is printed as
its state (`ok`, a whole store; `incomplete`, a stopped store; `split`; `outdated`; `nostore`, no
key found), the authority it serves, the key that stands, the keys set aside and the clients
admitted. A copy's **own ideal** is the same file had every record it ever received arrived by a
perfect transport, read under every key of the database's lineage that no keep record it holds sets
aside; the **oracle** is the same over every record any gate ever wrote. A copy **serves under a key
set aside** where it serves and the key that stands in it is a key its own ideal sets aside. A merge
gives **what neither copy gave** where the merged copy serves an authority, admits a profile of a
client, gives a word at a node or answers a renewal below the chain in a way neither copy that
served did just before it. A **counterexample** is a history in which a copy is wider than its own
ideal, or serves under a key its own ideal sets aside, of none of the classes that `STORE.md` §639
and the evaluation's decision 9 accept.

## 1. The design measured

- **A key is found** in a version of an authority, or of a retirement that carries it, that passes
  under that very key (§621, §649 step 1), whatever its lineage: a key a file from outside brings is
  found as the database's own is.
- **A keep record** is a record of kind `kept` under a key, its content the name of the key it sets
  aside, the HMAC-SHA256 under that key over `key`, and `after`. It counts as a record, as an item
  of a summary entry present, and as a deletion record under the tag of `kept` and the subject hash
  of `key:` and a name, under a key found and naming another key found, which names nothing in
  `after` (§649, §623).
- **Which keys stand** is worked out as the product's reading works it out
  (`StoreReading.Judgment.Standing`): a keep record that any keep record names in `after` counts for
  nothing; a key that no counting record names, or only records under keys set aside, stands; a key
  that a counting record under a standing key names is set aside; the keys left unmarked that a
  chain of counting records leads back to stand, a circle, and the two rules run again, until a
  round marks nothing.
- **A single standing key is the store key**: only what passes under it counts, and damage only
  under its tags. **Several standing keys are a split store**, which serves nothing.
- **A version record** states the store's format, and a summary entry restates the highest. A store
  whose format is above the plugin's is outdated and serves nothing (§651).
- **Start again** is offered wherever a key is found and Create has nothing to do: a whole store
  with an authority standing, a stopped store, a split store and an outdated store; not a whole
  store with no authority, which is Create's, nor with several standing, which is the chooser's. Its
  single write: a new key, an authority holding it, a keep record under the new key for each other
  key found, each naming in `after` every keep record the reading read under any key found, as a
  record, an item or a deletion record, a version record of the plugin's format, and a summary entry
  of height 1 with an empty `after` restating the keep records and the format. It deletes nothing
  (§632).
- **After it**, a pairing, a renewal and every write of the plugin's own are made under the key that
  stands, and a write looks at nothing under any other key: no summary entry, deletion record or
  damaged entry of another key is named, deleted or counted.
- **The ideal of a store started again begins empty under the new key**, and every decision under a
  key a keep record sets aside is out of it.

## 2. What was implemented

**In the model** (`crdt_model.py`, each line marked "ADDED (started again)"; with the switch at its
default every output prints as before). `STARTS`, the switch; `marked`, the marking of the keys,
written from the product's; `keys_of`, the keys found, the relations read and the keys that stand;
the fold under the store key alone, the states `split` and `outdated`, and the completeness check of
a keep record and a version record; `write_head` under the store key alone, carrying the keep
records and the format into every summary entry; the acts `restart` and `newer`, a plugin of a later
format that holds the store key and raises the format to 2; the property `aside-counts`, that a copy
serves under no key its own ideal sets aside, which ends a history. **Two defects of the model are
closed under the switch**: `pair` and `renewcert` wrote under the fixed key label `K`, so that a
client paired after a start again would have been under no key that stands, and a file in which no
key is found read `incomplete` where it held a deletion record of a summary entry, where `read.py`
and the product read no store. No earlier output depends on either.

**On files.** `store.py` writes the keep record, the version record and their items in a summary
entry; `read.py` reads them with a marking of its own, written apart from the model's, reads the
split and outdated states, and measures the room of the next summary entry against the product's
ceilings (`StoreRoom`: a content of at most 262,144 characters and fewer than 9,999 items, the item
`format` among them); `bridge.py` compares the keys that stand and the keys set aside between the
model and the file at every step. `probe/restart.py` holds the sequences and plays them on files;
`model/restart_runs.py` plays them and the searches in the model.

With the switches at their defaults, `store_runs.py scenarios` and `section6`, `answer_runs.py`
`hand`, `losses`, `laws` and `bridge two`, `answers.py cells` and `losses` on real files and
`lost_runs.py` `tags` and `hand` each printed its record line for line, and `vectors.py --answers`
printed the three files of `recorded-answers\vectors\` byte for byte but the line ends.

## 3. What was run

| Part | What it is | Size |
| --- | --- | --- |
| `cells` | T1 to T12 on real files, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases, the merged file held to both copies just before the merge, to its own ideal, to the keys its ideal sets aside and to the oracle, then the acts that follow | 12 sequences, 216 cells |
| `room` | a store at the ceiling of a summary entry's items and a store at the ceiling of its content, each read, started again and read again, on real files | 2 stores |
| `random` | random histories of a start again, pairings, revocations, names, rules, answers, Delete, Create, a later format, deletions by hand of the first and the newest authority and of a keep record, on real files, by Synchronize, Overwrite if newer and KeePassXC 2.8.0 | 300 histories, 1,769 steps |
| `model-hand` | T1 to T12 in the model, Create new IDs among the merges | 96 cells |
| `laws` | the marking of §649 step 4 over 15 hand-made sets of relations and random sets, held to its laws and to `read.py`'s marking | 20,000 sets, 80,134 relations |
| `exhaustive-4`, `exhaustive-4-noauth`, `exhaustive-5` | every sequence of a small alphabet over two copies: to 4 steps with the deletions by hand of the first and the newest authority and of a keep record, to 4 with the keep record's alone, to 5 without | 614,656, 331,776 and 3,200,000 histories |
| `starts-two`, `-three`, `-hand`, `-hand-noauth`, `-long` | random histories of the acts of a store started again; `hand` adds deletions and edits by hand of every entry that can carry a key, of a keep record and of the newest summary entry, Seal again, deletion records cleared and Create new IDs; `hand-noauth` the same with no entry that can carry a key deleted or edited; `long` 8 to 16 steps | 200,000 each |
| `search-2`, `search-3` | the design's twelve configurations over two and three files with `restart` and `newer` in the alphabet and a keep record and the first authority among what a person deletes and edits, the design's seeds | 20,000 a configuration, 480,000 |
| `search-noauth-2`, `search-noauth-3` | the same with no deletion or edit by hand of an entry that can carry a key, so that the classes behind section 9's are reached | 480,000 |
| `bridge-two`, `bridge-hand` | every file of random histories written by the Encoder, an edited entry with its content changed, read by `read.py` and held to the model's state, authority served, key that stands, keys set aside and clients admitted | 2,000 histories each |

The store is the store of the answers' runs (the store as decided, the standing answers to a
renewal, a rule or an answer waiting for a save, the reading of lost versions), with the switch on.
Each part ran twice (section 14).

## 4. The sequences, side by side

What the copy merged into reads after the merge, on real files, by every merge but Create new IDs,
both ways; the model gives the same in every cell.

| Sequence | The merged copy | Note |
| --- | --- | --- |
| **T1** a start again on a whole store at copy 0, then a name, a pairing and a rule under the new key; copy 1 from before | served `a4` under `K4`, `K` aside, `c1` admitted, in both copies; a pairing lands, `c0`'s renewal refused | the earlier client is admitted nowhere the keep record reaches |
| **T2** the same after the store stopped at copy 0, its newest summary entry deleted with a deletion record | as T1 | the stop is no obstacle, and the start deletes nothing |
| **T3** two first authorities made apart (copy 1 lost `a0` with no deletion record and made `K4` by Create), synchronized both ways, so both copies are split; copy 0 starts again | served `a6` under `K6`, `K` and `K4` aside, into either copy | a split store is ended by a start again |
| **T4** copy 0 starts again and pairs `c1`; copy 1, stale, still serves the earlier store and pairs `c2` | `K4` stands; `c2` admitted in neither copy, its renewal refused; `c1`'s issued | a stale gate's pairing is admitted nowhere the keep record reaches |
| **T5** copy 0 starts again; `a0`'s entry deleted at copy 0 with a deletion record; copy 1 holds it | `K4` stands; by Synchronize `a0` goes in both copies and `K` is found nowhere; by every other method and every release `a0` comes back and `K` is set aside | the earlier key brought back counts for nothing |
| **T6** both copies start again apart | split, `K4` and `K8` standing, `K` aside, nothing served; a start again on the merged file then serves `a12` under `K12` with all three aside | two keys stand, and a later start settles them |
| **T7** a start again, synchronized, then a second start at copy 0 | served `a11` under `K11`, `K` and `K4` aside; `c1`'s renewal refused | the second keep records name the first in `after` |
| **T8** a plugin of a later format raises copy 0's format, synchronized, so both copies are outdated; copy 0 starts again | served `a6` under `K6`, `K` aside, also into the outdated copy | the earlier records hold the plugin back no more, also once merged again |
| **T9** a start again, then Delete of the new authority with none after it, then Create | served `a10` under `K4`, `K` aside | the retirement carries the new key, and the earlier key stays aside |
| **T10** a start again; at copy 1 `a0`'s entry edited 11 times | `K4` stands in both copies; where the edited entry wins `K` is found nowhere, where it does not `K` is set aside | nothing changes |
| **T11** a start again; the new authority's entry deleted at copy 0 for good with a deletion record | **served `a0` under `K`, `c0` admitted**, in copy 0 before any merge and in every cell | section 9 |
| **T12** a start again; the new authority's entry edited 11 times at copy 0 | as T11 | section 9 |

**The cells.** Of 216 cells on files, **none gives what neither copy gave**. **34 are wider than
their own ideal and serve under a key their own ideal sets aside, each of them a cell of T11 or
T12**: the 16 cells of each by the merges but Create new IDs, and the import into copy 0 of each.
Against the oracle, the import cells (Create new IDs) into the stale copy 1 are wider where that
copy stays stale (§640): T1, T2, T4, T5, T7 and T9, and the stale copy of T11 and T12. The file and
the model disagreed in 2 cells, entry by entry, and nowhere in the read: T10 by Overwrite existing
into copy 1 and Keep existing into copy 0, where the model merges both methods as Overwrite if
newer, and so keeps the edited `a0` that the file replaces with the unedited entry, or keeps the
unedited entry that the file replaces; both read `K4` standing. In the model the 96 cells give the
same, 14 of them flagged, each a cell of T11 or T12.

## 5. No room on files

| Store | Before the start | After the start, and after a name and a pairing |
| --- | --- | --- |
| 9,997 revocations, so that the next summary entry would hold 9,999 items | stopped: 9,999 items, no room | whole, served under the new key, `K` aside; the next summary entry holds 2 items |
| 7,940 summary entries deleted with deletion records, so that the summary entry over them names 7,941 and the next would name 7,942 in 262,161 characters | stopped: no room | whole, served; the next summary entry names 3 summary entries in 174 characters |

The start applies where the store has no room, writes nothing under the earlier key, and the new
store has room. The model holds no ceiling, so it reads both stores whole before the start; that is
the only step at which it disagreed with the file.

## 6. The searches

**The design's search**, its twelve configurations, 20,000 histories each, the failures of every
property that ends a history, each history shrunk and classed by what it holds:

| Files | Section 9's class (`safety`, `aside-counts`, `one-loss`) | Accepted classes | Discarded answers and rules (accepted) |
| --- | --- | --- | --- |
| two | 300, 305, 299; and 1 `sealed-after-a-loss` | a store key lost then Create, decision 9: 9; §639 rows 2 and 3: 1; `rollback-by-overwrite`, row 1: 101; outdated: 1,323; split by starts made apart: 12 | 598 and 575 |
| three | 195, 196, 195 | decision 9: 6; row 1: 200; outdated: 1,277; split: 30 | 414 and 434 |

**The same with no deletion or edit by hand of an entry that can carry a key**:

| Files | Failing a property that ends a history, by class | Discarded answers and rules |
| --- | --- | --- |
| two | §639 rows 2 and 3: 2 `safety`, 1 `one-loss`; row 1: 141; outdated: 1,323; split: 12 | 593 and 564 |
| three | row 1: 229; outdated: 1,277; split: 30 | 419 and 412 |

`convergence-to-ideal`, the property of configuration A that the copies converge on their ideal,
counts the two states of the design that serve nothing on purpose: a store raised to a later format
(`newer` alone, shrunk to a single step) is outdated until it is started again, and two copies that
start again apart are split until a later start settles them (`restart 0 ; restart 1`), while the
oracle reads the union of what stands. Neither is wider.

**The acts of a store started again**, 200,000 histories each:

| Search | Failing a property that ends a history | Discarded answers and rules (accepted) |
| --- | --- | --- |
| two copies, 4 to 12 steps | **none** | 1,256 and 1,422 |
| three copies, 4 to 12 steps | **none** | 1,174 and 1,271 |
| two copies, 8 to 16 steps | **none** | 1,835 and 2,224 |
| two copies, deletions and edits by hand, Seal again, deletion records cleared, Create new IDs | section 9's class: 2,468 `safety`, 2,525 `aside-counts`, 2,436 `one-loss`, 10 `sealed-after-a-loss`; decision 9: 77, and 3 where the earlier authority's entry was lost before a start (below); §639 rows 2 and 3: 18 and 7; row 4: 2 and 2 | 462 and 465 |
| the same, no entry that can carry a key deleted or edited | **none** | 755 and 833 |

**Every sequence**: to 4 steps with the deletions by hand, 614,656 histories, 28 steps wide, fail
only where an authority's entry is deleted: 7,954 `aside-counts`, 7,768 `safety` and 7,764
`one-loss`, the first shortest each `restart ; restart ; pair ; del ('auth',)`; classed as they
stand, unshrunk, 6,748 of the `aside-counts` hold the deletion after a start and no Create, and
1,206 a Create as well. To 4 steps with a keep record deleted with a deletion record or with none,
331,776 histories, 24 steps wide, **no failure**; to 5 steps without deletions, 3,200,000 histories,
20 steps wide, **no failure**. The convergence was checked at the end of each.

**No history of any part is of another class.** The three histories where the earlier authority's
entry was lost before a start (`edit 1 ('auth', 'a0') ; renew 0 c0 forever ; restart 0 ; merge xc 0
1 ; restart 0`) are decision 9's: the key `K` is lost at copy 0 by the edited entry the merge
brings, the second start cannot name a key it does not find, and the oracle, which still reads `K`,
refuses the renewal the copy asks.

**On files**, the 300 random histories disagreed with the model at no step, entry by entry and in
the read. 10 steps read wider than their own ideal and served under a key the ideal sets aside, each
after the new authority's entry was deleted (section 9). Of 979 merges held to both copies just
before, 2 gave what neither copy gave: a merge in section 9's class, and a merge that gave the union
of the two copies' decisions where a copy served no authority (a rule set in a copy with no
authority, merged into a copy that serves), which the ideal gives as well.

## 7. The marking of the keys

The 15 hand-made sets read as stated: no keep record, both keys stand; a key named is set aside; a
chain, whose middle key, set aside, drops its own record, so the last stands; a circle, all stand; a
circle settled by a later record naming both earlier records in `after`; a later record under
another key replacing an earlier record; a circle that names a key outside it, which is set aside; a
key that names itself, which stands; a record naming a key not found, which marks nothing; a
deletion record; a three-circle; starts in a chain; two starts apart; and two circles, the first
naming a key of the other. Over 20,000 random sets of 1 to 6 keys and 0 to 8 relations, **no law is
violated** (some key stands; every key set aside is named by a counting record under a key that
stands), the model's marking and `read.py`'s, written apart, **agree in every set**, and neither the
order of the keys, the order of the relations nor a relation read twice changes the marking.

**Circles marked in a single round.** In 255 of the 20,000 sets a key stands that a standing key
names with no circle leading back: two circles, the first naming a key of the second, are found in
the same round and all their keys stand. That is the product's marking, and it is the narrower
answer: more keys stand, so the store is split and serves nothing until a store is kept. §649's
words ("keys whose keep records name each other in a circle set none of them aside") leave open
which reading is meant; section 10 sharpens them.

## 8. The bridge

In 4,000 random histories of the acts of a store started again, with and without deletions and edits
by hand, every file written by `store.py`'s Encoder and read by `read.py` gave the model's state,
authority served, key that stands, keys set aside and clients admitted: 22,222 steps, of which
17,324 whole, 671 stopped, 2,454 with no key found, 1,740 outdated and 33 split; **0 differing**. On
real files, `cells`, `room` and `random` disagreed with the model only where sections 4 and 5 name
it.

## 9. A store started again whose key is lost brings the earlier store back

**The class**, found by every search whose alphabet holds a deletion or an edit by hand of an
authority's entry, shrunk to two steps: `restart 0 ; del 0 ('auth',) 0`, and the same with a
deletion record, with a record dated before the entry, and with an edit. Copy 0 starts the store
again; the new authority's entry, the single carrier of the new key, is then deleted for good or
edited past KeePass's history limit. The new key is found no more, so no keep record under it is
read, and the earlier key, whose authority, admissions, rules, names and answers the start left in
place, is the only key found: it stands, and the earlier store is served whole, its clients admitted
as before the start. On real files T11, the deletion with a deletion record, and T12, eleven edits,
give it in the copy itself before any merge and in every merge both ways by every method and
release: the copy serves `a0` under `K` and admits `c0`, which its own ideal and the oracle set
aside.

**Why no accepted class holds it.** §641 accepts the loss of the entry that holds the store key
because "nothing is granted by the loss": with no key found, nothing is served until Create starts a
store, and every earlier decision is passed over. A store started again breaks the premise: the
earlier store is still in the file and still whole, its key still found, and only the keep records
under the new key kept it aside. The loss of the new key's carrier therefore grants, at once and
with nobody asked, every client, rule and name abandoned by the start, the narrowing rules among
them. It takes a single deletion or eleven edits of a single entry, the causes §641 lists, and a
Synchronize carries the deletion into every copy. The searches reach it again through Seal again
(`sealed-after-a-loss`, 11 histories) and through every merge of a copy that lost the key. Delete of
the new authority is not this class: the retirement carries the new key (T9), and Create under it
follows.

**What would close it**, none of it measured here:

- the start writes, under every key it sets aside that it can write under, a retirement of each
  authority standing there, so that the earlier store, should its key stand alone again, serves no
  authority; what the earlier store held would then wait for a Create under the earlier key, which
  would count its rules and names again;
- the start writes, under every key it sets aside, a record no summary entry under that key
  restates, so that the earlier store, standing alone again, is stopped; Seal again would lift that;
- §641 accepts the class by name, and the warning of the start names it.

The searches with no deletion or edit by hand of an entry that can carry a key were run so that the
classes behind this class are reached; they find no other class (section 6).

## 10. Where the model sharpens the design

1. **The loss of the new key after a start again brings the earlier store back** (section 9). §641's
   "nothing is granted by the loss" holds for a store that was never started again, and not after a
   start: the start sets the earlier store aside only for as long as its own key is found.
2. **A key the start could not find, it cannot name** (§632), and the oracle shows what that costs:
   where the earlier key was lost before the start, its decisions are passed over as decision 9 has
   them, and a merge that later brings its carrier back makes that key stand beside the new key: the
   store is split, not served.
3. **Circles marked in a single round** (section 7). The product marks every circle found in a round
   at once, so a circle that names a key of another circle found in the same round sets nothing
   aside: all stand, the store splits. Stated so in §649, the reading is the narrower of the two the
   words allow.
4. **Which keep records a start names in `after`** matters only where a later keep record brings a
   key set aside back: a keep record the start named is replaced, so the key it had set aside stands
   again, and a later choice of the earlier store meets that key standing and is asked again. The
   product's reading reads no keep relation where a single key is found, so a start over such a
   store names nothing in `after` there; since every keep record under that key counts only while
   the key stands, which after the start it does not, the two give the same in every history run.
5. **Damage is judged under the key that stands alone**, and a write deletes damage only there: an
   entry of the earlier store edited past the limit stops nothing after a start (T10).
6. **A foreign key found makes the store split.** Under the switch an authority from outside under a
   key of its own is a key found, so the store splits and serves nothing, where the model without
   the switch passed the foreign key over and served nothing for two authorities standing. Both
   serve nothing, and a start sets the foreign key aside with the rest.

None of these but the first changes the design: each is what the design gives, stated more exactly.

## 11. The property, as it stands

For every copy whose store is whole and serves, **after every step of any history of starts again,
pairings, revocations, names, rules, answers, Delete, Create, Recreate, a plugin of a later format,
deletions by hand, Seal again, saves, KeePass ending before a save and merges, the copy is not wider
than its own ideal, and serves under no key its own ideal sets aside**, where the ideal begins empty
under the key of every start and holds out every decision under a key a keep record it holds sets
aside. **Merges alone converge**, and two starts made apart converge on a split store, which serves
nothing until a later start. **The laws of §649 step 4 hold**: a later keep record replaces an
earlier, circles stand, some key always stands, and the marking depends on nothing but the
relations. **Outside the property, and counted**: the loss of the entry that carries the key of a
store started again, section 9, which no class accepts; a store key lost before a start or before a
Create (decision 9); and the classes of §639 rows 1 to 4.

## 12. What the product has to satisfy

- A keep record names a key by the HMAC-SHA256 under that key over `key`, as 64 lowercase
  hexadecimal characters, and its subject is `key:` and that name; it counts as a record, as an item
  of a summary entry present and as a deletion record under a key found naming another key found.
- The keys that stand are marked as `StoreReading.Judgment.Standing` marks them; `read.py`'s
  marking, written apart, agrees with it over every set run, and the product's tests can hold it to
  the same sets.
- A start again is offered in a whole store with an authority standing, a stopped store, a split
  store within the ceiling on keys and an outdated store; its write is the write of section 1, made
  over every key found, deleting nothing, and it applies where the store has no room.
- After a start a pairing and a renewal are written under the key that stands, and every summary
  entry carries every keep record under that key forward, from an item, else from its record, else
  from a deletion record that tells of it, with an empty `after`.
- After a start the earlier store's damage, format and records hold nothing back (T8, T10), and the
  room is measured under the new key alone (section 5).
- **What the design has not named**: what keeps the earlier store aside once the new key's carrier
  is lost (section 9). Until a form of it is measured, the operator's documentation and the warning
  of the start describe the loss of the new authority's entry as bringing back the store the start
  set aside.

## 13. What was not run, and why

- **The product**: the design is measured here before anything of it is built.
- **The vectors**: a keep record, the `keys:` family and T1 to T10 are added to
  `StoreRecordVectors.json`, `StoreMeaningVectors.json` and `StoreWriteVectors.json` once printed
  from the model these runs used; nothing of them was printed here.
- **The dialog that keeps a store among several** (§649): circles arise from it and from no start,
  so they were run in the marking alone (section 7).
- **A key past the ceiling on keys**, **the group of a store started again** and **a second gate
  still serving the earlier store** over a socket: the model holds neither groups nor connections,
  and T4 is the stale gate's pairing as a file holds it.
- **A window, a phone and the slow key derivation.**

## 14. The two runs

`run.ps1 -Starts -Parallel 4`, from the branch root in Windows PowerShell 5.1 under an ordinary user
token, ran every part twice, in turn: run 1 on 2026-11-26 and run 2 on 2026-11-27. **Every output of
run 2 is the same as run 1's line for line, with the masks of `compare.py`**: 19 files. Neither run
showed a window. How long, in seconds, four parts at once on twelve logical processors, each model
part on four processes of its own:

| Part | Run 1 | Run 2 |
| --- | --- | --- |
| `exhaustive-5` | 1,896 | 1,629 |
| `starts-hand`, `starts-three`, `starts-long` | 532, 351, 336 | 316, 236, 241 |
| `exhaustive-4`, `search-3`, `starts-hand-noauth`, `starts-two` | 271, 266, 256, 241 | 236, 251, 130, 131 |
| `search-noauth-3`, `random`, `search-2`, `exhaustive-4-noauth`, `search-noauth-2` | 236, 206, 185, 176, 166 | 216, 160, 175, 150, 160 |
| `cells`, `room` | 115, 15 | 110, 25 |
| `bridge-two`, `bridge-hand`, `model-hand`, `laws` | 5 each | 5 each |
