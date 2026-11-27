# Measured: a store started again, and the keys it sets aside

**Start store again** (`STORE.md` §632, §641) abandons the store of a database and makes a store
under a new key, setting every other key the reading finds aside by the keep records of §649, and
stopping the store it sets aside. This report measures that command, with the keys set aside it
rests on and Seal again beside it, before anything of it is built in the product, in the model, over
the files `store.py` writes of the model, and on real files. It was run on 2026-11-27 in the family
`spikes\storage\probes\store-as-a-whole\` on the branch `spikes/storage-start-again`, over the
commits of Seal again, as `run.ps1 -Starts`, with its record in `recorded-starts\`; the family's
`RUN.md`, section "A store started again", has the commands, both runs' times and the comparisons.
Nothing ran in KeePass's window or on a phone: the files of the parts on files were made and changed
by KeePassLib 2.62 and merged by KeePassLib or `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0.

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
  aside, the HMAC-SHA256 under that key over `key`, `after` and a nonce; its subject is `key:` and
  that name. It counts as a record, as an item of a summary entry present, and as a deletion record
  under the tag of `kept` and the subject hash of `key:` and a name, under a key found and naming
  another key found, which names nothing in `after` (§649, §623).
- **Which keys stand** is worked out as the product's reading works it out
  (`StoreReading.Judgment.Standing`): a keep record that any keep record names in `after` counts for
  nothing; a key that no counting record names, or only records under keys set aside, stands; a key
  that a counting record under a standing key names is set aside; the keys left unmarked that a
  chain of counting records leads back to stand, a circle, and the two rules run again, until a
  round marks nothing.
- **A single standing key is the store key**: only what passes under it counts, and damage only
  under its tags. **Several standing keys are a split store**, which serves nothing.
- **A version record** states the store's format (§650: `format`, `plugin`, a nonce, the subject
  `format:` and the number, the number in the identifier), and a summary entry restates the highest.
  A store whose format is above the plugin's is outdated and serves nothing (§651).
- **Start again** is offered wherever a key is found and Create has nothing to do: a whole store
  with an authority standing, a stopped store, a split store and an outdated store; not a whole
  store with no authority, which is Create's, nor with several standing, which is the chooser's. Its
  single write, deleting nothing (§632):
  - **under each key found, the stop**: a keep record naming the new key, with an empty `after`,
    which no summary entry under that key restates;
  - under the new key: an authority holding it; a keep record naming each other key found, naming in
    `after` every keep record the reading read under any key found, as a record, an item or a
    deletion record, and every stop the write makes; a version record of the plugin's format; and a
    summary entry of height 1 with an empty `after` restating them.
- **While the new key is found**, the stop is named in `after` by the keep record that sets its key
  aside, so it counts for nothing, and its key is not judged at all. **Once the new key is lost**,
  the earlier key stands alone, and the stop is a record no summary entry restates: the earlier
  store is stopped (§624) and serves nothing.
- **Seal again over a store a start stopped** keeps the earlier store: besides what §625 writes, a
  keep record under its key naming the key the stop names, with the stop in `after`, and the summary
  entry restating both. Should the new key come back, the two keys name each other: a circle, the
  store split, the operator asked (§649).
- **After a start**, a pairing, a renewal and every write of the plugin's own are made under the key
  that stands, and a write looks at nothing under any other key.
- **The ideal of a store started again begins empty under the new key**, and every decision under a
  key a keep record sets aside is out of it.

## 2. What was implemented

**In the model** (`crdt_model.py`, each line marked "ADDED (started again)"; with `STARTS` at its
default every output prints as before, Seal again's among them). `STARTS`, the switch, and
`START_STOPS`, the stop, on by default, off the start as first measured; `marked`, the marking of
the keys, written from the product's; `keys_of`, the keys found, the relations read and the keys
that stand; the fold under the store key alone, the states `split` and `outdated`, and the
completeness check of a keep record and a version record; `write_head` under the store key alone,
carrying the keep records and the format into every summary entry; the acts `restart`, with the
stops, and `newer`, a plugin of a later format that holds the store key and raises the format to 2;
in Seal again's write, the keep record over a stop; the property `aside-counts`, that a copy serves
under no key its own ideal sets aside, which ends a history. **Two defects of the model are closed
under the switch**: `pair` and `renewcert` wrote under the fixed key label `K`, so that a client
paired after a start again would have been under no key that stands, and a file in which no key is
found read `incomplete` where it held a deletion record of a summary entry, where `read.py` and the
product read no store.

**On files.** `store.py` writes the keep record, the version record in the product's form and their
items in a summary entry; `read.py` reads them with a marking of its own, written apart from the
model's, reads the split and outdated states, measures the room of the next summary entry against
the product's ceilings (`StoreRoom`), and lists, among what a stopped store lost, each stop a start
wrote by the key it names (`setAsideBy`) and the keep record the seal writes for it; `bridge.py`
compares the keys that stand and are set aside between the model and the file at every step.
`probe/restart.py` holds the sequences and plays them on files; `model/restart_runs.py` plays them
and the searches in the model; `vectors.py --starts` prints the vectors.

## 3. What was run

| Part | What it is | Size |
| --- | --- | --- |
| `cells` | T1 to T16 on real files, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases, the merged file held to both copies just before the merge, to its own ideal, to the keys its ideal sets aside and to the oracle, then the acts that follow | 16 sequences, 288 cells |
| `dialog` | what Seal again's reading lists of a store a start stopped once the new key is lost, by a deletion, by eleven edits, over a store already stopped and over a split store, and the seal pressed over it, held to what the listing plans, on real files | 4 stores |
| `room` | a store at the ceiling of a summary entry's items and a store at the ceiling of its content, read, started again and read again, on real files | 2 stores |
| `random` | random histories of starts, pairings, revocations, names, rules, answers, Delete, Create, a later format, Seal again, deletions by hand of the first and the newest authority and of a keep record, on real files, by Synchronize, Overwrite if newer and KeePassXC 2.8.0 | 300 histories, 1,767 steps |
| `model-hand` | T1 to T16 in the model, Create new IDs among the merges | 128 cells |
| `laws` | the marking of §649 step 4 over 15 hand-made sets of relations and random sets, held to its laws and to `read.py`'s marking | 20,000 sets, 80,134 relations |
| `exhaustive-4`, `exhaustive-4-noauth`, `exhaustive-5` | every sequence of a small alphabet over two copies: to 4 steps with the first and the newest authority deleted, Seal again and a keep record deleted with a deletion record and with none; to 4 with the keep record's deletions alone; to 5 with none | 810,000, 331,776 and 3,200,000 histories |
| `starts-two`, `-three`, `-hand`, `-hand-noauth`, `-long` | random histories of the acts of a store started again; `hand` adds deletions and edits by hand of every entry that can carry a key, of a keep record and of the newest summary entry, Seal again, deletion records cleared and Create new IDs; `hand-noauth` the same with no entry that can carry a key deleted or edited; `long` 8 to 16 steps | 200,000 each |
| `search-2`, `search-3` | the design's twelve configurations over two and three files, Seal again's acts and `restart` and `newer` in the alphabet, a keep record and the first authority among what a person deletes and edits, the design's seeds | 20,000 a configuration, 480,000 |
| `search-noauth-2`, `search-noauth-3` | the same with no deletion or edit by hand of an entry that can carry a key | 480,000 |
| `bridge-two`, `bridge-hand` | every file of random histories written by the Encoder, an edited entry with its content changed, read by `read.py` and held to the model's state, authority served, key that stands, keys set aside and clients admitted | 2,000 histories each |

The store is the store of Seal again's runs (the store as decided, the standing answers to a
renewal, a rule or an answer waiting for a save, the reading of lost versions, Seal again as
designed), with `STARTS` and `START_STOPS` on. **Every part ran twice** (section 15).

## 4. The sequences, side by side

What the copy merged into reads after the merge, on real files, by every merge but Create new IDs,
both ways; the model gives the same in every cell.

| Sequence | The merged copy | Note |
| --- | --- | --- |
| **T1** a start on a whole store at copy 0, then a name, a pairing and a rule under the new key; copy 1 from before | served `a4` under `K4`, `K` aside, `c1` admitted, in both copies; a pairing lands, `c0`'s renewal refused | the earlier client is admitted nowhere the keep record reaches |
| **T2** the same after the store stopped at copy 0, its newest summary entry deleted with a deletion record | as T1 | a stopped store can be started again |
| **T3** two first authorities made apart (copy 1 lost `a0` with no deletion record and made `K4` by Create), synchronized both ways, so both copies are split; copy 0 starts again | served `a6` under `K6`, `K` and `K4` aside, into either copy | a split store is ended by a start |
| **T4** copy 0 starts again and pairs `c1`; copy 1, stale, still serves the earlier store and pairs `c2` | `K4` stands; `c2` admitted in neither copy, its renewal refused; `c1`'s issued | a stale gate's pairing is admitted nowhere the keep record reaches |
| **T5** copy 0 starts again; `a0`'s entry deleted at copy 0 with a deletion record; copy 1 holds it | `K4` stands; by Synchronize `a0` goes in both copies and `K` is found nowhere; by every other method and release `a0` comes back and `K` is set aside | the earlier key brought back counts for nothing |
| **T6** both copies start again apart | split, `K4` and `K8` standing, `K` aside, nothing served; a start on the merged file then serves `a12` under `K12`, all three aside | two keys stand, and a later start settles them |
| **T7** a start, synchronized, then a second start at copy 0 | served `a11` under `K11`, `K` and `K4` aside; `c1`'s renewal refused | the second keep records name the first records and stops in `after` |
| **T8** a plugin of a later format raises copy 0's format, synchronized, so both copies are outdated; copy 0 starts again | served `a6` under `K6`, `K` aside, also into the outdated copy | the earlier records hold the plugin back no more, also once merged again |
| **T9** a start, then Delete of the new authority with none after it, then Create | served `a10` under `K4`, `K` aside | the retirement carries the new key, and the earlier key stays aside |
| **T10** a start; at copy 1 `a0`'s entry edited 11 times | `K4` stands in both copies; where the edited entry wins `K` is found nowhere, where it does not `K` is set aside | nothing changes |
| **T11** a start; the new authority's entry deleted at copy 0 for good with a deletion record | **stopped, served nothing**, in copy 0 before any merge and in every cell, `K` alone standing | the stop holds the earlier store |
| **T12** a start; the new authority's entry edited 11 times at copy 0 | as T11 | the stop holds the earlier store |
| **T13** T11, then Seal again at copy 0 | served `a0` under `K`, `c0` admitted, in every cell; the oracle as decided | Seal again keeps the earlier store |
| **T14** a start, synchronized into copy 1; at copy 0 the new authority's entry deleted with a deletion record and Seal again; copy 1 holds the started store | by Synchronize both ways `a0` under `K`, the deletion record carried; by Overwrite if newer, Overwrite existing and Keep existing into copy 1, and by every KeePassXC release both ways, **split**, `K` and `K4` standing, nothing served | the new key brought back meets the keep record of the seal: a circle, and the operator is asked |
| **T15** the store stopped before the start; a start; the new key lost by 11 edits; Seal again | served `a0` under `K` in every cell | the seal lifts both stops at once |
| **T16** T11, then the stop deleted for good with a deletion record | **served `a0` under `K`, `c0` admitted**, in copy 0 before any merge and in every cell | section 9 |

**The cells.** Of 288 cells on files, **none gives what neither copy gave**. **17 are wider than
their own ideal and serve under a key their own ideal sets aside, each of them a cell of T16**: its
16 cells by the merges but Create new IDs, and its import into copy 0. Against the oracle, the
import cells (Create new IDs) into the stale copy 1 are wider where that copy stays stale (§640):
T1, T2, T4, T5, T7, T9, T11, T12 and T16. The file and the model disagreed in 2 cells, entry by
entry, and nowhere in the read: T10 by Overwrite existing into copy 1 and Keep existing into copy 0,
where the model merges both methods as Overwrite if newer; both read `K4` standing. In the model the
128 cells give the same, 7 of them flagged, each a cell of T16.

## 5. What Seal again shows, and what it does

What `read.py`'s listing of a stopped store holds of a store a start stopped, once the new key is
lost, and the seal pressed over it, on real files:

| Store | Stopped because | Set aside by a start | The seal writes | After the seal |
| --- | --- | --- | --- | --- |
| the new key lost by a deletion with a deletion record | a record no summary entry restates | the stop, naming `K4` | a keep record under `K` naming `K4`, the stop in `after` | whole, `a0` served under `K`, `c0` admitted |
| the new key lost by eleven edits | as above | as above | as above | as above |
| the earlier store stopped before the start (its summary entry deleted), then the new key lost | no summary entry of the store is in force; a record no summary entry restates | the stop, naming `K4` | as above | whole, as above |
| a split store started again, then the new key lost | split: `K` and `K4` both stand, each with a stop naming the new key | both stops, naming `K6` | nothing: a split store is not sealed | split, nothing served |

In every store the keep record the seal wrote is the record the listing planned. **The dialog can
therefore show** the stop as a row of its own: its kind, a store set aside by a start, and the name
of the key the start made, which is found nowhere; and after sealing, the earlier store kept again,
the new key set aside should it come back as a circle with it. **Sealing brings the earlier store
back** with its own key standing, its authority served and its clients admitted as before the start,
its rules, names and answers in force; what was decided under the new key is out of it. **Should the
new key come back** (T14, by every merge that carries no deletion record), the seal's keep record
and the start's name each other: both keys stand and the store is split until the operator keeps a
store.

## 6. No room on files

| Store | Before the start | After the start, and after a name and a pairing |
| --- | --- | --- |
| 9,997 revocations, so that the next summary entry would hold 9,999 items | stopped: 9,999 items, no room | whole, served under the new key, `K` aside; the next summary entry holds 2 items |
| 7,940 summary entries deleted with deletion records, so that the summary entry over them names 7,941 and the next would name 7,942 in 262,161 characters | stopped: no room | whole, served; the next summary entry names 3 summary entries in 174 characters |

The start applies where the store has no room, writes under the earlier key the stop alone, and the
new store has room. The model holds no ceiling, so it reads both stores whole before the start; that
is the only step at which it disagreed with the file.

## 7. The searches

**The design's search**, its twelve configurations, 20,000 histories each, the failures of every
property that ends a history, each history shrunk and classed by what it holds:

| Files | Section 9's class (`safety`, `aside-counts`, `one-loss`) | Accepted classes | Discarded answers and rules (accepted) |
| --- | --- | --- | --- |
| two | 7, 7, 4 | decision 9: 6; `rollback-by-overwrite`, row 1: 85; outdated: 1,323; split by starts made apart: 12 | 619 and 557 |
| three | 7, 7, 2 | decision 9: 3; row 1: 151; outdated: 1,277; split: 30 | 399 and 403 |

**The same with no deletion or edit by hand of an entry that can carry a key**:

| Files | Failing a property that ends a history, by class | Discarded answers and rules |
| --- | --- | --- |
| two | §639 rows 2 and 3: 1; row 1: 103; outdated: 1,323; split: 12 | 628 and 572 |
| three | rows 2 and 3: 2; row 1: 162; outdated: 1,277; split: 30 | 407 and 426 |

`convergence-to-ideal`, the property of configuration A that the copies converge on their ideal,
counts the two states of the design that serve nothing on purpose: a store raised to a later format
(`newer` alone) is outdated until it is started again, and two copies that start again apart are
split until a later start settles them (`restart 0 ; restart 1`), while the oracle reads the union
of what stands. Neither is wider.

**The acts of a store started again**, 200,000 histories each:

| Search | Failing a property that ends a history | Discarded answers and rules (accepted) |
| --- | --- | --- |
| two copies, 4 to 12 steps | **none** | 1,256 and 1,422 |
| three copies, 4 to 12 steps | **none** | 1,174 and 1,271 |
| two copies, 8 to 16 steps | **none** | 1,835 and 2,224 |
| two copies, deletions and edits by hand, Seal again, deletion records cleared, Create new IDs | section 9's class: 218 `safety`, 223 `aside-counts`, 89 `one-loss`, 1 `sealed-after-a-loss`; Seal again keeping the earlier store: 1; the start's keep record and its summary entry deleted beside the stop: 1 each; decision 9: 76, and 3 where the earlier authority's entry was lost before a start; §639 rows 2 and 3: 16 and 6; row 4: 3 and 3 | 462 and 464 |
| the same, no entry that can carry a key deleted or edited | the start's keep record and its summary entry deleted beside the stop: 3 | 754 and 831 |

**Every sequence**: to 4 steps with the deletions by hand and Seal again, 810,000 histories, 30
steps wide, fail only where the new authority's entry and a keep record are both deleted: 812
`aside-counts`, 804 `safety` and 404 `one-loss` in section 9's class, and 64, 64 and 32 holding a
Create as well (decision 9), the first shortest `restart ; pair ; del ('auth',) ; del ('kept',)`; to
4 steps with a keep record deleted with a deletion record or with none, 331,776 histories, 24 steps
wide, **no failure**; to 5 steps without deletions, 3,200,000 histories, 20 steps wide, **no
failure**. The convergence was checked at the end of each.

**The classes beside section 9's, each accepted.**

- **Seal again keeping the earlier store** (`restart 0 ; rule 0 N none ; newer 0 ; del 0 ('auth',) 0
  ; reseal 0`): the new key lost, the earlier store sealed back. The copy gives at `N` what the
  earlier store gave there, while the oracle, which reads both keys of the circle the seal's keep
  record makes, holds the empty rule written under the new key. It is what sealing does (section 5):
  what was decided under the new key is out of the earlier store.
- **The start's keep record and the summary entry restating it deleted beside the stop** (`restart 0
  ; del 0 ('head', 'new') 1 ; del 0 ('kept',) 1 ; del 0 ('kept',) 0`): three deletions by hand, the
  record that sets the earlier key aside lost with every carrier, so that the stop's deletion record
  sets the new key aside instead; §639 rows 2 and 3.
- **The earlier authority's entry lost before a start** (`edit 1 ('auth', 'a0') ; renew 0 c0 forever
  ; restart 0 ; merge xc 0 1 ; restart 0`): the second start cannot name a key it does not find, and
  the oracle, which still reads `K`, refuses the renewal the copy asks; decision 9.

**On files**, the 300 random histories disagreed with the model at no step, entry by entry and in
the read; **no step read wider than its own ideal or served under a key the ideal sets aside**. Of
970 merges held to both copies just before, 5 gave what neither copy gave, none in a history with a
start: each the union of the two copies' decisions where a copy served no authority, which the ideal
gives as well.

## 8. The marking of the keys, and the bridge

The 15 hand-made sets read as stated: no keep record, both keys stand; a key named is set aside; a
chain, whose middle key, set aside, drops its own record, so the last stands; a circle, all stand; a
circle settled by a later record naming both earlier records in `after`; a later record under
another key replacing an earlier record; a circle that names a key outside it, which is set aside; a
key that names itself, which stands; a record naming a key not found, which marks nothing; a
deletion record; a three-circle; starts in a chain; two starts apart; and two circles, the first
naming a key of the second. Over 20,000 random sets of 1 to 6 keys and 0 to 8 relations, **no law is
violated** (some key stands; every key set aside is named by a counting record under a key that
stands), the model's marking and `read.py`'s, written apart, **agree in every set**, and neither the
order of the keys, the order of the relations nor a relation read twice changes the marking. In 255
sets two circles, the first naming a key of the second, are found in the same round and all their
keys stand, which is the product's marking and the narrower reading (section 10).

In 4,000 random histories of the acts of a store started again, with and without deletions and edits
by hand, every file written by `store.py`'s Encoder and read by `read.py` gave the model's state,
authority served, key that stands, keys set aside and clients admitted: 22,217 steps, of which
17,255 whole, 737 stopped, 2,454 with no key found, 1,740 outdated and 31 split; **0 differing**.

## 9. The stop deleted by hand beside the new key's carrier

**The class**, the only class of any part outside those accepted, shrunk to three steps: `restart 0
; del 0 ('auth',) 1 ; del 0 ('kept',) 1`, and the same with the deletions in either order, with a
deletion record or with none, and with an edit of the authority's entry; on real files T16. The new
authority's entry and the stop are both deleted by hand: the new key is lost, and nothing is left
that stops the earlier store, since the stop has a single carrier, its own entry, by design (no
summary entry restates it), and a deletion record of it tells of a key the earlier store cannot
name, the new key being found no more. The earlier store is served whole, its clients admitted.

**What it takes**: two deletions or edits by hand of entries of the plugin's own, each of which the
guard keeps from being deleted on the tab (§635), in the same copy or in two copies a merge brings
together; a single loss of either serves nothing more than it did (T11, T12, and a stop deleted
while the new key is found, which changes nothing). It is the shape of §639 row 3, a decision
deleted with the only carrier it has. The first form of the start, with no stop, served the earlier
store on the first of the two deletions alone; the stop closes that class in every part, and leaves
this class.

**What would close it**, not measured: a second carrier of the stop that an entry's deletion does
not take away, for instance a deletion record under the earlier key of a summary entry no summary
entry names, which stops that store as §624 names (a summary entry deleted that nothing in force
replaces) and which only clearing every deletion record removes (§639 row 4).

## 10. Where the model sharpens the design

1. **The stop holds the earlier store once the new key is lost** (T11, T12): stopped, nothing
   served, and Seal again the only way back. The first form of the start served it whole.
2. **Seal again over a stop is a choice of a store** (section 5): its keep record names the new key,
   so that a new key brought back by a merge makes a circle and the store splits, rather than either
   store standing over the other's later word.
3. **A split store started again whose new key is lost is split again**, each earlier key holding a
   stop naming a key found nowhere; Seal again does not apply to a split store, and a further start
   settles it.
4. **The start's keep record names the stops in `after`**, written in the same write before it: the
   stop's identifier does not depend on the keep record's, so the order of the write is the only
   order that can be.
5. **A key the start could not find, it cannot name** (§632): where the earlier key was lost before
   the start, its decisions are passed over as decision 9 has them.
6. **Circles marked in a single round** (section 8). The product marks every circle found in a round
   at once, so a circle that names a key of another circle found in the same round sets nothing
   aside: all stand, the store splits. Stated so in §649, the reading is the narrower of the two the
   words allow.
7. **Which keep records a start names in `after`** matters only where a later keep record brings a
   key set aside back. The product's reading reads no keep relation where a single key is found; a
   start over such a store names nothing there, and since every keep record under that key counts
   only while the key stands, which after the start it does not, the two give the same in every
   history run.
8. **Damage is judged under the key that stands alone**, and a write deletes damage only there: an
   entry of the earlier store edited past the limit stops nothing after a start (T10).
9. **A foreign key found makes the store split**: an authority from outside under a key of its own
   is a key found, so the store serves nothing, and a start sets the foreign key aside with the
   rest.

## 11. The property, as it stands

For every copy whose store is whole and serves, **after every step of any history of starts again,
pairings, revocations, names, rules, answers, Delete, Create, Recreate, a plugin of a later format,
deletions by hand, Seal again, saves, KeePass ending before a save and merges, the copy is not wider
than its own ideal, and serves under no key its own ideal sets aside**, where the ideal begins empty
under the key of every start and holds out every decision under a key a keep record it holds sets
aside. **A lost new key serves nothing**, and only Seal again brings the earlier store back.
**Merges alone converge**, and two starts made apart converge on a split store, which serves nothing
until a later start. **The laws of §649 step 4 hold**: a later keep record replaces an earlier,
circles stand, some key always stands, and the marking depends on nothing but the relations.
**Outside the property, and counted**: the stop deleted by hand beside the new key's carrier
(section 9); Seal again keeping the earlier store, as designed; a store key lost before a start or
before a Create (decision 9); and the classes of §639 rows 1 to 4.

## 12. What the product has to satisfy

- A keep record names a key by the HMAC-SHA256 under that key over `key`, as 64 lowercase
  hexadecimal characters, and its subject is `key:` and that name; it counts as a record, as an item
  of a summary entry present and as a deletion record under a key found naming another key found.
- The keys that stand are marked as `StoreReading.Judgment.Standing` marks them; `read.py`'s
  marking, written apart, agrees with it over every set run.
- A start is offered in a whole store with an authority standing, a stopped store, a split store
  within the ceiling on keys and an outdated store, and applies where the store has no room. Its
  write is the write of section 1: under each key found the stop first, then under the new key the
  authority, the keep records naming every stop and every keep record read in `after`, the version
  record and the summary entry; it deletes nothing.
- The reading reads a stop as a record no summary entry restates, so that a store a start stopped is
  stopped wherever its key stands alone; and lists it apart, as a keep record under the store key
  that names a key found nowhere, with the name, so that the seal's dialog can show it.
- Seal again writes, for each stop it lifts, a keep record under the store key naming the key the
  stop names, with the stop in `after`, restated by its summary entry with the stop.
- After a start, a pairing and a renewal are written under the key that stands, and every summary
  entry carries every keep record under that key forward, from an item, else from its record, else
  from a deletion record that tells of it, with an empty `after`.
- **What the design leaves open**: a second carrier of the stop (section 9).

## 13. The vectors

`vectors.py --starts` prints the three files of `--seal` with a store started again added under
names of their own; `--seal` prints `recorded-seal\vectors\` byte for byte but the line ends.

| File | Held today | Printed | Added | Changed or removed |
| --- | --- | --- | --- | --- |
| `StoreRecordVectors.json` | 51 records | 56 records | 5 records, their names beginning `kept,`, `format,` and `head of height 1 under the new key` | none |
| `StoreMeaningVectors.json` | 130 stores | 139 stores | 9 stores, their names beginning `keys:` | none |
| `StoreWriteVectors.json` | 85 sequences | 101 sequences | 16 sequences, T1 to T16, their names beginning `starts,` | none |

**Nothing held today changed**: the line diff of each file against `recorded-seal\vectors\` adds
111, 1,237 and 57,742 lines and replaces a single line, the line of `about`, whose text is the text
held today with 764, 863 and 1,268 characters added after it. The records are the stop under the key
`one` naming the key `two`, the start's keep record under `two` naming `one` and the stop, the keep
record Seal again writes under `one` over the stop, the version record and the summary entry under
`two`. The meanings hold, for each store, read.py's answer, its keys (found, standing, aside, and
every keep record read) and its listing of what a stopped store lost; each step of the sequences
adds `keysRead`.

## 14. What was not run, and why

- **The product**: the design is measured here before anything of it is built.
- **The dialog that keeps a store among several** (§649): circles arise from it and from Seal again
  over a stop the new key comes back to, so they were run in the marking (section 8) and in T14.
- **A key past the ceiling on keys**, **the group of a store started again** and **a second gate
  still serving the earlier store** over a socket: the model holds neither groups nor connections,
  and T4 is the stale gate's pairing as a file holds it.
- **A window, a phone and the slow key derivation.**

## 15. The two runs

`run.ps1 -Starts -Parallel 4`, from the branch root in Windows PowerShell 5.1 under an ordinary user
token, ran every part twice, in turn; in run 1 the five parts that class a deletion of a keep record
were run again once the classes of section 7 were told apart. **Every output of run 2 is the same as
`recorded-starts\` line for line**, with the masks of `compare.py`: 22 files, the two listings of
the vectors among them, and the three files of vectors the same bytes but the line ends. With
`STARTS` off, every earlier part run again, Seal again's among them, printed its record line for
line, and `vectors.py --seal` the files of `recorded-seal\vectors\` byte for byte but the line ends.
The times of both runs are in `RUN.md`; the longest part, `exhaustive-5`, took 1,704 and 1,705
seconds.
