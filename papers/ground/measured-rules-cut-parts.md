# Measured: the parts cut from the run of rules per profile

The run of a rule stored as a record for each node and profile (`measured-rules-per-profile.md`)
cut parts of the earlier runs to keep within its time, and left two things about the model's
searches untraced. `STORE.md` §644 item 24 holds both as owed before the rules are built. This
report is that run, made on 2026-11-20 in the family `spikes\storage\probes\store-as-a-whole\` on
the branch `measure/rules-cut-parts`, taken from `spikes/storage-survival`, as `run-cut.ps1`, with
its record in `recorded-cut\`. The family's `RUN.md`, section "The parts cut from rules per
profile", has the commands and both runs' times. Nothing ran in a window, on a phone or in an
emulator; every file was made and changed by KeePassLib 2.62 and merged by KeePassLib or by
`keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0.

**How to read it.** The words are those of `measured-rules-per-profile.md`. A **row** is a
profile's rule at a node, the **switch** is the node's "Refuse any profile not named here".
**Wider** is a client reaching more than the last decision allowed, and a sequence that ends wider
is **fail-open**; on files the oracle is every decision any gate made, and in the model's searches
the property `safety` holds each file to what was delivered to that file. **Narrower** is less
than the last decision without refusing as a whole, and **refuses** is the plugin refusing what
the last decision allowed. **Per profile** is the store as decided with a rule as a record for
each node and profile and a removed row written as decided (`run.ps1 -Rows`); the **whole-rule
form** is the store as decided with a version of a node's whole rule (`run.ps1 -Decided`). Two
profiles, `pf` and `pg`, and two letters, read and write; `pf:rw pg:-` is read and write for `pf`
and nothing for `pg`.

## 1. What was run

| Part | What it is | Cells |
| --- | --- | --- |
| `b-deletions` | nine kinds of store entry deleted four ways at copy 0, the read, Seal again, a stale copy merged both ways by Synchronize and by KeePassXC; with each of the four releases, in both forms | 216 for each release and form, 72 of them merged by KeePassXC |
| `b-clear` | the same deletions and six prunes, every deletion record removed, a stale copy merged; with each release, in both forms | 252, 84 of them by KeePassXC |
| `b-history` | each kind of store entry edited at copy 0 by ten operations of KeePass's own, merged both ways; with each release, in both forms; per profile the switch as an eleventh kind | 660 per profile and 600 whole-rule, 220 and 200 by KeePassXC |
| `b-random-rows`, `-off` | the same 300 random histories of acts on a row and on the switch with each release in every merge of KeePassXC; each merge held to both copies just before it | 300 histories and 712 files for each release, removal as decided and taken off |
| `b-random` | nothing run: its histories already draw every merge of KeePassXC from the four releases | 578 merges drawn: 2.4.2 79, 2.6.6 59, 2.7.12 74, 2.8.0 74 |
| `b-slow` | the KeePassXC cells of H3, H6, H13 and T2 under the slow key derivation, each history cell also in the whole-rule form | 32 |
| `b-dedupe`, `b-dupes`, `b-repair` | the parts of the store as decided, per profile | 4, 15 and 96 outcomes |
| `b-growth` | one gate, 10 clients, 5 names, 20 ruled nodes, 1,000 writes, per profile | 4 sizes |
| `store-ext` | the extended model of the survival critique, both forms | 16 configurations of 8,000 histories |
| `store-scenarios`, `store-section6` | the design's 20 replays and the 43 sequences of `EVALUATION.md` section 6, in the column of the store as decided, both forms | 20 and 43 |
| the searches, traced | the model's searches history by history: per profile `search`, `acts` and `noauth`, whole-rule `search` and `noauth`, each over two and three files; every history that fails `safety` shrunk | 24 configurations of 20,000 histories in each of five searches |

**Every part ran twice**, and the second run printed every output of the first line for line but
the running times: 52 files, the comparisons of this report among them, the 10 traces of the
searches byte for byte, and the 70 files of the single histories played. Before the comparisons,
the run was held to the records it can be held to: with KeePassXC 2.8.0, `b-deletions`, `b-clear` and `b-history` per profile printed
`recorded-rows\` line for line (274, 316 and 924 lines) and `b-random-rows` and `-off` its 3
lines; `store-ext` in the whole-rule form printed `recorded-decided\` line for line (53 lines);
the replays in the whole-rule form gave the first record's column with the repair undone in 20 of
20 replays and 43 of 43 sequences; and every traced search counted what its record counts, in 24
configurations of 24 for each of the five searches, and shrank the same shortest history in every
configuration that fails `safety`.

## 2. Item 1: the other KeePassXC releases in the edits, deletions and random histories

| Part | Cells merged by KeePassXC, in every release, per profile | The same, whole-rule | Cells whose class differs between the four releases | Cells whose class differs between the forms |
| --- | --- | --- | --- | --- |
| `b-deletions` | 72: as decided 66, narrower 4, refuses 2 | the same | 0 of 216 in either form | 0 of 216 in each release |
| `b-clear` | 84: as decided 84 | the same | 0 of 252 in either form | 0 of 252 in each release |
| `b-history` | 220: as decided 156, narrower 2, refuses 62 | 200: as decided 142, narrower 2, refuses 56 | 0 of 660 per profile, 0 of 600 whole-rule | 0 of 600 in each release; the 60 cells of the switch, per profile only: as decided 42, refuses 18 |
| `b-random-rows` | merges beyond both copies 0; files at the end WIDER 234, as decided 383, narrower 95 | not run: the acts are on rows | 0 of 300 histories end with a file in another class | |
| `b-random-rows-off` | merges beyond both copies 0; files at the end WIDER 234, as decided 383, narrower 95 | not run | 0 of 300 histories | |

**Nothing differs between KeePassXC releases, and nothing is wider in a merge by any of them.**
Each cell of the three parts, 1,128 per profile and 1,068 in the whole-rule form, holds the same
class with 2.4.2, 2.6.6, 2.7.12 and 2.8.0, and each of the 1,068 cells both forms hold has the
same class in both with every release. In every release every whole part read the same:
`b-deletions` 216 cells, as decided 153, narrower 18, refuses 12, Seal again not needed 30 and not applicable 3; `b-clear` 252 cells,
as decided 240, narrower 6, refuses 6; `b-history` per profile 660 cells, as decided 450, narrower
10, refuses 190, and 10 rows "the Create above". No cell of the three parts ends wider in any
release or form.

**The random histories of rows** played the same 300 histories with each release in every merge
of KeePassXC: 1,724 steps (1,725 with the removal taken off), no entry disagreement and no read
difference, and no merge that gave a letter neither copy gave just before it, in each of the
eight runs. Every file of every history ends in the same class under each release. The 234 files
"WIDER" against every decision are the same files in every release and as in
`measured-rules-per-profile.md`: a copy that has not met a narrowing made at another copy, or that
widened a row apart, gives more than all decisions together allow; the test of a merge is the
test beyond both copies.

**`b-random` was not cut.** Its alphabet draws every merge of KeePassXC from the four releases in
the record of the store as decided and in that of rules per profile alike: of its 578 merges,
286 are by KeePassXC, 79 by 2.4.2, 59 by 2.6.6, 74 by 2.7.12 and 74 by 2.8.0. The list of section
12 of `measured-rules-per-profile.md` names it among the parts with 2.8.0 alone, which it is not.

## 3. Item 1: the other parts cut

**`b-slow`.** The 32 KeePassXC cells under the slow derivation (Argon2d, 200 iterations, 64 MiB, 2
lanes): the 24 cells of H3, H6 and H13 are as decided in both forms, and each of the 32 cells
reads as the same cell of `recorded-rows\b-merges.txt` under the fast derivation, in both
directions, with each release. No figure depends on the two loads of a merge falling in
different seconds.

**`b-dedupe`, `b-dupes`, `b-repair`**, each outcome per profile against the same outcome of
`recorded-decided\`:

| Part | Outcomes | Whole-rule to per profile |
| --- | --- | --- |
| `b-dedupe` | 4 | refuses to refuses 4 |
| `b-dupes` | 15 | as decided 10, narrower 2, refuses 3, each the same |
| `b-repair` | 96 | as decided 55, narrower 3, refuses 11, WIDER 16, each the same; **WIDER to refuses 11** |

The 11 outcomes that change go from wider to refusing: R1, records cleared after an unsettled
loss (6 outcomes: both files of the Synchronize, and copy 0 after the import into it by each of
the four methods that keep identifiers), and R2, a narrowed rule's new version and summary entry
removed by script in copy 1 (5: copy 1 beside the import into copy 0 by each of the five
methods). Both histories delete "the newest rule version" by hand, which per profile takes a single row's version, `pg`'s; `pf`'s
new version stays, no summary entry in force restates it, and the store stops, where in the
whole-rule form nothing of the narrowing is left and the stale copy's rule is back. Of the 16
outcomes wider in both forms, R1's 5 (the stale copy of each import into copy 0) print the same
text, and R8's 11 (a version made with the store key, §639 row 7) give `pf:rw pg:-` per profile
where the whole-rule form gives read and write to both.

**`b-growth`**, the same 1,000 writes per profile, against `recorded-decided\b-growth.txt`:

| After | Summary entry: bytes, strings, items restated | Store: entries, bytes of `KPG.` strings | Deletion records | Longest version `after` list |
| --- | --- | --- | --- | --- |
| the start | 7,346, 45, 42 (whole-rule 4,280, 24, 21) | 59, 19,900 (38, 12,130) | 33 (33) | 0 (0) |
| 10 writes | 8,349, 48, 45 (5,064, 27, 24) | 60, 21,503 (39, 13,295) | 61 (54) | 1 (1) |
| 100 writes | 17,361, 73, 70 (11,951, 44, 41) | 85, 38,770 (57, 25,838) | 280 (222) | 8 (7) |
| 1,000 writes | 103,745, 339, 336 (81,551, 272, 269) | 352, 208,394 (285, 157,631) | 2,527 (2,008) | 33 (36) |

The read answers `ok` at every size in both forms. Per profile the store holds a version for each
row a write changes, so after 1,000 writes it holds 1.24 times the entries and 1.32 times the bytes
of the whole-rule form, and its summary entry restates 336 items against 269. The longest `after`
list of a version is 33 identifiers against 36: a row's versions name only the row's own.

**`store-ext`.** Per profile, every line of the extended model's 16 configurations is the line of
the whole-rule form (53 lines but the header), which is `recorded-decided\store-ext.txt`.

**The replays**, in the column of the store as decided, per profile against the whole-rule form:

| Sequence | Whole-rule | Per profile |
| --- | --- | --- |
| replay 9, the newest rule version and the head deleted with records, deletion records cleared, Synchronize with the stale copy | fails: `safety`, N grants read and write | holds, the store incomplete: refuses as a whole |
| replay 10, the same with Seal again before the stale copy | `sealed-after-a-loss`, N grants read | `sealed-after-a-loss`, N grants `pf:- pg:r` |
| section 6, row 3: a narrowed rule's new entries removed by script after KeePassXC brought them | WIDER, N grants read and write | refuses, the whole database (incomplete) |
| section 6, row 4: records cleared while a loss stands, then a stale copy synchronized | WIDER, N grants read and write | refuses, the whole database (incomplete) |
| section 6, row 4: the same with Seal again before the stale copy | WIDER, N grants read and write | WIDER, N grants `pf:- pg:rw` |
| section 6, row 9: a file made with the store key from a current copy | WIDER, N grants read and write | WIDER, N grants `pf:rw pg:-` |
| section 6, row 14: a first narrowing, its version and head deleted with no record, Seal again; and the same then KeePassXC into a copy that never had the rule | WIDER, N grants read | WIDER, N grants `pf:- pg:r` |

In the other 18 replays and 37 sequences the file asked gives the same answer in both forms, and
the model's checks fail the same property at the same step. Their printed views differ in three
ways only: where a file other than the file asked is wider in both forms, per profile with fewer
letters (row 9 from a stale copy, file 1 gives `pf:rw pg:r` against read and write); in row 6,
where the accepted copy's words print as letters per profile; and in the names the model gives
authorities after a Create, which count its elements (`a10` per profile for `a8`). Against every
decision, the 43 sequences are wider in 13 in both forms and in 2 in the whole-rule form alone
(rows 3 and 4 above), as decided in 18, narrower in 1 and refusing in 9 in both. Each
difference is the hand deletion of "the newest rule version", which per profile takes `pg`'s row
alone: where it leaves `pf`'s new version with no summary entry to carry it the store stops, and
where it does not, the row deleted is wider and the other row is as decided.

## 4. Item 2: the four histories in which the counts differ

The search of the store as decided, traced history by history in both forms: the same 20,000
histories for each configuration, drawn from the same seeds. Over two files, 1,507 histories
count a property in the whole-rule form and 1,504 per profile; over three files, 1,261 and 1,262.
**Exactly four histories fail `safety` in a form alone**, all over two files, which are the four
differing counts of the record; no history fails it in both forms at another step or file.

| Configuration, history | Whole-rule | Per profile | Where the forms part | The parting act taken out |
| --- | --- | --- | --- | --- |
| C #5506 | nothing | `safety`, file 1: the root grants read and write | step 0, `rule 1 N inherit` | both: `safety`, the root grants read and write |
| E #8455 | nothing | `safety`, file 1: client c0 admitted | step 3, `rule 0 root r` | both: `safety`, c0 admitted |
| F #17746 | `safety`, file 1: renewal of c0 asked | nothing | step 1, `rule 1 root r` | both: nothing |
| G #17957 | nothing | `safety`, file 0: renewal of c0 asked | step 2, `rule 0 N inherit` | both: `safety`, renewal of c0 asked |

**What differs is a rule act that changes nothing.** In each history the forms part at a rule act
that changes no row: `inherit` at N, which holds no row, or `r` at the root, whose rows grant read
already. Per profile such an act writes nothing; in the whole-rule form it writes a version of the
node's rule. Under change 4 that version waits in memory for a save, and while a rule waits in a
copy the model lets no other program change that copy. So the hand deletions that follow
(`del ('auth',)` in C, F and G; the summary entry and the retirement in E) are refused in the
whole-rule copy and done in the per-profile copy, and from that step the two forms play different
histories. Taken out, the parting act leaves both forms with the same answer in all four. The
same holds for every other history whose properties differ between the forms: of the 14 (11 over
two files, 3 over three: `accepted:copy-wider` 8, `rollback-by-overwrite` 5,
`accepted:unsaved-rule-lost` 1), each parts at a rule act that writes a version in the whole-rule
form alone.

**Three of them end wider per profile where the whole-rule form did not**, against what was
delivered to that file and against every decision any gate made alike. None holds an act on a
row, and none is wider by a rule: each is a failure of a kind the whole-rule form has too.

- **C #5506**, a store key lost: copy 1's authority entry deleted with no record, Create, then the
  root set to read and write. The new authority admits no client, and the rule the file gives is
  the last decision; the oracle reads under every store key of the lineage and still holds the
  root's earlier read. A store started again (§641). Shortest form: `del 1 ('auth',) 0 ; create 1 ;
  rule 1 root rw`.
- **G #17957**, a store key lost: renewals of c0 refused forever, then copy 0's authority entry
  deleted with a record and Create there; the refusal, written under the key that is lost, is
  passed over, so a renewal of c0 is asked again. Shortest form: `renew 1 c0 forever ; del 0
  ('auth',) 1 ; create 0 ; merge dx 1 0`.
- **E #8455**, a retirement deleted with its summary entry: copy 0 deletes its authority with
  none after it, then its newest summary entry with no record and the retirement with a record;
  an import into copy 1 by Overwrite if newer, which carries no deletion record, leaves copy 1
  admitting c0. §639 row 2. Shortest form: `delauth 0 ; del 0 ('head', 'new') 0 ; del 0 ('ref',
  'retired') 1 ; merge kp_other 1 0`.

F #17746, wider in the whole-rule form alone, is the store key lost the other way: there the hand
deletion of copy 1's authority entry is refused and the renewal refusal written, and per profile
it is done and the refusal never written. Shortest form, whole-rule: `del 0 ('auth',) 1 ; renew 1
c0 forever ; merge kp_sync 1 0 ; create 0 ; merge xc 0 1`.

## 5. Item 3: every failure of `safety` in the searches, by kind

Each history that fails `safety` was shrunk as `search` shrinks its shortest, and named by what it
deletes: **a store key lost** where the authority's entry is deleted or edited and Create follows,
and **a refusal deleted with a summary entry** where the history deletes both.

| Search | Failures counted, histories | A store key lost: the root grants, a renewal asked | A refusal deleted with a summary entry | Holds an act on a row or the switch |
| --- | --- | --- | --- | --- |
| per profile, `search` | 36, 36 | 9, 20 | revocation 1, retirement 3, name removal 2, renewal answer 1 | 0 |
| per profile, `acts` | 20, 20 | 12, 5 | name removal 1, renewal answer 2 | 6, each a store key lost |
| per profile, `noauth` | 5, 5 | 0, 0 | revocation 2, retirement 1, name removal 1, **a row's version 1** | 1 |
| whole-rule, `search` | 34, 34 | 8, 20 | revocation 1, retirement 2, name removal 2, renewal answer 1 | |
| whole-rule, `noauth` | 3, 3 | 0, 0 | revocation 1, name removal 2 | |

Each history fails once, so failures and histories are the same count. **Two kinds appear that
no shortest history of their search shows**, both of the second kind, a refusal deleted with a
summary entry:

- **a name removal deleted with a summary entry**, in the store's `search` over two and three
  files (C #7280, C #10803), in both forms alike; the shortest histories of `acts` and `noauth`
  and of the whole-rule `noauth` show it;
- **a row's narrowing deleted with a summary entry**, in the per-profile `noauth` over two files,
  C #12934, shortest form `row 1 root pg  ; merge kp_other 1 0 ; del 1 ('ver', 'rule:root',
  'new') 0 ; merge xc 0 1 ; del 0 ('head', 'new') 0`: copy 1 empties `pg`'s row at the root and
  deletes that version with no record, KeePassXC brings only the new summary entry into copy 0,
  and copy 0 deletes it with no record, so copy 0 is its older self and gives `pg` read at the
  root. This is §639 row 3, with a row in place of the node's rule, and it is the only failing
  history beyond the store key lost that holds an act on a row. The same history with the act on
  the node's whole rule (`rule 1 root none`) fails the same way at the same step in the
  whole-rule form.

The six histories of `acts` that hold an act on a row are a store key lost with a row set after
Create (`del 0 ('auth',) 1 ; create 0 ; row 0 root pf rw`): the same as a whole rule set after
Create. No failing history holds an act on the switch.

## 6. What decides

**Does anything end wider per profile that did not in the whole-rule form?** On files, no: in no
cell of any part, with any release, did the per-profile form end wider where the whole-rule form
did not, and where both end wider the per-profile form never grants a letter the whole-rule form
does not grant (`b-repair` R8, section 6 rows 4, 9, 14). Every change of class between the forms,
on files and in the replays, goes from wider to refusing: `b-repair` 11 outcomes, replay 9, and
section 6 rows 3 and 4. In the model's searches, three histories of the search fail `safety` per
profile alone (C #5506, E #8455, G #17957), each listed in section 4 with its shortest form; each
parts from the whole-rule form at a rule act that changes nothing and, with that act taken out,
fails the same in both forms. None of them is wider by a rule: two are a store key lost and the
third a retirement deleted with its summary entry.

**Does any result differ between KeePassXC releases?** No: 0 of 1,128 cells of `b-deletions`,
`b-clear` and `b-history` per profile and 0 of 1,068 whole-rule, and 0 of 300 histories of each
random part of rows, hold another class with another release; the 32 cells of `b-slow` are the
cells of the fast derivation with each release.

**Does anything contradict `STORE.md` §617?** No measured answer does. Four statements rest on
measurements narrower than their wording, and the text could carry them:

- §617, "no cell and no file ended wider in that form that did not end wider in the whole-rule
  form": it holds for the parts run here as well, with every release of KeePassXC; but the
  hand deletions of `b-repair`, the replays and section 6 delete a single row's version per
  profile, so they measure the loss of a single row, never of every row of a node at once.
- §644 item 24, "their counts ... differ by a single failure in each of the other four, in
  histories that were not traced": traced; the cause is the model's change 4 meeting a rule act
  that changes nothing, which writes per profile nothing and in the whole-rule form a version
  that waits for a save (section 4).
- §644 item 24, "no shortest failing history holds a row or a switch": true of the shortest;
  beyond them a single failing history holds an act on a row, §639 row 3 with a row's narrowing,
  which fails the same in the whole-rule form (section 5).
- §617, "In the whole-rule form the node refused for every profile; with an object for each
  profile the refusal is that profile's alone": in `b-repair` R1 and R2 and section 6 rows 3 and
  4 the store refuses as a whole per profile; that is not a row refusing but a row's version
  that no summary entry present carries, which stops the whole database as §624 states for any
  record ("every decision present is restated").

## 7. Every fail-open sequence in this run

1. **A store key lost, Create after it** (§641): in every search, per profile and whole-rule, 29
   of 36 and 28 of 34 failures of the store's `search`, 17 of 20 of `acts`. Shortest forms:
   `del 1 ('auth',) 1 ; create 1 ; rule 1 root rw` and `renew 0 c0 forever ; del 0 ('auth',) 0 ;
   create 0`. Item 2's C #5506 and G #17957 per profile and F #17746 whole-rule are of this kind.
2. **A refusal deleted with a summary entry, a merge then carrying no record** (§639 rows 2 and 3):
   a revocation, a retirement, a name removal, a renewal answer, and per profile a row's
   narrowing; 7 of 36 failures of `search` per profile and 6 of 34 whole-rule, 3 of 20 of `acts`,
   5 of 5 of `noauth` per profile and 3 of 3 whole-rule. Shortest forms: `revoke 0 c0 ; del 0
   ('head', 'old') 2 ; del 0 ('ref', 'revoked') 2 ; merge xc 1 0`, `delauth 0 ; del 0 ('head',
   'new') 0 ; del 0 ('ref', 'retired') 1 ; merge kp_other 1 0`, and the row's form in section 5.
   Item 2's E #8455 is of this kind.
3. **On files, the same cells as the earlier records**: `b-repair` R1 into the stale copy (5
   outcomes, §639 row 4) and R8 (11, a version made with the store key, §639 row 7), wider in both
   forms; in the model, 13 sequences of `EVALUATION.md` section 6 wider against every decision in
   both forms (its rows 2, 3, 4, 7, 9, 10 and 14, as `store_runs.py` labels them, and the root
   ruled again after a Delete), each wider in the earlier records too.
4. **None in the merges of rows**: no merge of the 300 random histories of rows gave a letter
   neither copy gave, with any release, the removal written as decided or taken off.

## 8. What was not run, and why

- **The replays with a decision undone, per profile.** Those columns of the first record hold the
  repair after a Synchronize, which reads no row and is not part of the store as decided.
- **`store-ablations`, `store-repair-closes` and `design-full`**, which the store as decided did
  not run either: the cost of each decision against the store with the repair, the repair itself,
  and the copy of the design's model.
- **The forms `void`, `limit` and `split` beyond the parts that name them, and more than 300
  random histories on files**, which section 12 of `measured-rules-per-profile.md` lists as cut
  beside the parts of this brief; neither was asked here.
- **The loss of every row of a node at once by hand**: the harness's selection of "the newest rule
  version" takes a single row, and changing it is a change to the model.
- **Everything `measured-rules-per-profile.md` lists as not measured**: the grant editor and
  KeePass's window, a second KeePass with the plugin, a phone, more than two profiles and two
  letters, a rule between the root and N, a name off the list, a collision of tags, the read in
  the plugin's C#, the growth of the `after` lists under repeated edits of a row.

## 9. The harness

New files only: `probe\cut.py`, `model\cut_runs.py`, `model\search_trace.py`,
`model\search_replay.py`, `run-cut.ps1`, `compare_cut.py`; no file of the family was changed. The
parts that take a release are copies of the functions of `cases.py` with the release a parameter,
and with 2.8.0 print what `run.ps1 -Rows` printed. `RUN.md` has the commands, the times and the
defects of this configuration.
