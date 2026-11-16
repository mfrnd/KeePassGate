# Measured: the access rules rolled back, under today's model and under grant-only

Run 2026-11-16 for the storage rethink, in a console over invented trees, with no window, no
emulator and no database. The question: when a node's access rule returns to a state it really
had earlier, after the operator has since changed it, does some client end up with more access
than the operator's last decision, under today's rule model and under the grant-only model with its
middle form, and how often? The grant-only design's probe has a trial it calls a rollback (its
trial 4, `Program.cs` lines 355 to 371): it replaces a node's document by a random document. A
random document is a state the node never had, so that trial measures something else. This report
measures rollbacks.

The probe is `spikes\storage\probes\rollback-rule-models\` on the branch `spikes/storage-survival`,
with its `RUN.md`, its command and its recorded output. Every count below is read off
`recorded\rollback.txt`; a second run gave the same file line for line.

## 1. What ran

- **Today's model** is the product's own resolver: the probe compiles the eight files the
  grant-only design's probe compiles (`Acl\AclDocument.cs`, `AclDocuments.cs`, `AclGrant.cs`,
  `AclInheritance.cs`, `AclResolver.cs`, `AclVerb.cs`, `VerbMask.cs`, `Transport\JsonBody.cs`),
  exported from `a42e38aa` by `tools\Product.ps1`, and every answer of today's model is
  `AclResolver.Permits` over the documents the trial writes.
- **Grant-only and its middle form** are the resolution functions of the design's probe, carried
  unchanged: `GrantOnly` and `Middle` (that file's lines 205 to 237) and its fold of grant and
  withdrawal records (its `Read`, lines 652 to 659: a grant's live letters are its mask less every
  withdrawal of its row it did not see, and a row is the union of its grants' live letters). The
  design's text and its probe were not found to differ on how a row resolves. They do differ on
  what a withdrawal holds when a whole row is taken away (section 2.4), and both forms were run.
- **C# on .NET Framework 4.8**, because the product's resolver is C# for that framework and the
  design's probe is the same language. No KeePass type is needed: the resolver asks `IAclNode`,
  which the probe's own node class implements, so KeePass is not referenced.
- **Two worlds.** A seeded sample: 1,000 histories of 20 edits each, on trees of 30 nodes made by
  the design's own generator, three profiles, seed 20261116. And an exhaustive world: every history
  of 4 edits from an empty policy on a five node tree (root, group G under it, entry X and group S
  in G, entry S1 in S), one profile, over a small alphabet of edits: 116,745 histories.
- **Runtime**: 190.5 s and 193.9 s for the two runs, each with the build of about 2 s besides, on
  a machine other work was sharing. The output holds no time, and the two runs gave the same file
  byte for byte.

## 2. The design of the trial

### 2.1 The access table

A cell is a profile, a node and a question among 14: create, read, update and list, each on the
fields, with the attachments and with the protected values (12 cells), and strip and delete, which
no switch qualifies (2 cells). The design's probe counts 18 answers per node and profile, strip
and delete three times each; the controls use its 18, the trial uses the 14. A sample tree has
1,260 cells; the exhaustive tree has 70.

### 2.2 The operator's intent

The intent after each edit is a table of cells, the same for every model. It comes from a policy of
decisions, which the edits change:

- an **exact** decision at a node for a profile: from here down the profile has exactly these
  cells, until a nearer decision. This is today's rule, and the edits in today's vocabulary write,
  change and remove such decisions;
- an **adds** decision: from here down the profile has what arrives from above plus these cells.
  This is the grant-only form's own act, a grant with its own switches below an inherited rule
  (the design's `..U....P` on an entry under `.R...L..`);
- a **seal** on a node: a profile the node has no decision for gets nothing from above.

The table at a node for a profile walks up from the node: an adds decision contributes and the walk
goes on, an exact decision contributes and ends it, a seal ends it for a profile the node does not
decide. With only exact decisions and seals this is today's nearest wins exactly.

### 2.3 The edits

| Kind | What the operator does | Decision written | Offered when |
| --- | --- | --- | --- |
| grant | grants a profile verbs at a node | exact, a mask | nothing reaches the profile there |
| widen | adds a letter to the node's rule | the same mode, a letter more | the node has a decision |
| narrow | takes a verb or a switch away | the same mode, a letter less | the node has a decision |
| empty rule | writes `........` | exact, nothing | something reaches the profile there |
| seal | seals the node | seal | an unnamed profile reaches the node |
| remove | removes the node's rule, or its seal | no decision, or no seal | the node has a rule or a seal |
| add below | adds a rule below an inherited rule, one letter wider or narrower | exact | something reaches the profile there |
| adds below | grants a mask of its own below an inherited rule | adds | something reaches the profile there |

Only edits that change the intent are made. In the sample a kind is drawn by weight (grant 3,
widen 1, narrow 1.5, empty rule 1, seal 0.75, remove 1, add below 1.5, adds below 1), then an edit
of that kind; masks come from the design's list of typical masks and letters from all eight. The
exhaustive world's alphabet: grant `.R...L..` or `CRU..L.P`; widen by `U` or `P`; narrow by `R`,
`U`, `L` or `P`; add below, wider or narrower by a letter among those; adds below `..U....P`.

### 2.4 How each model expresses an intent

After every edit each model expresses the new intent in its own records, and its store is changed
by the difference, as its design prescribes. Where a model cannot give an intent exactly, it gives
the widest thing it can within the intent, so that no model is ever wider than the intent without
a loss (control 2 holds that to every state).

- **Today's model**: one document per node, written into the product's own format. A seal is
  `default: deny`; a decision is the widest mask whose cells lie within what the decision wants at
  that node. An adds decision is a rule fixed at the cells the node is meant to have, so it is
  written again when what arrives from above changes. For losses (b) and (c) the document's history
  is kept per node.
- **Grant-only**: rows from the table, with the design's push-down. A node can hold only what every
  node under it may hold (a row reaches everything under its node), so a narrowing takes the letter
  off the row above and puts it on the other children; the group above holds less than intended.
  A row is written on every node with a decision and on every node a push-down reaches. The records
  are the design's section 5.2: letters added are a new grant listing the withdrawals of its row it
  saw; letters removed are a withdrawal; a grant left with nothing is deleted in the same write.
- **Middle form**: every decision is a grant row on its own node; a narrowing, and a seal, is a
  withheld mark on that node for that profile, holding the letters that arrive and the decision
  does not hold, and the node refers back to its marks. A mark that holds too much is lifted, by a
  lift record, and a mark for what is missing is added. Marks are written per profile, never for
  every profile at once.
- **"Row whole"**: the design's section 5.2 and scenarios 02 and 15 of its probe withdraw the
  letters removed; scenario 04 of its probe withdraws a row taken away whole with all eight
  letters, `CRUSDLAP`. The first is the primary form here. The second was run beside it for both
  grant-only forms and is reported as "row whole"; it changes only loss (e).

### 2.5 The losses

Each history ends at its last edit; then each loss is applied to the store of each model, and the
model's table after the loss is compared with the intent after the last edit.

- **(a) The whole store as it was k edits ago**, for every k from 1 to the length of the history: a
  whole file written over a newer file (`RESULTS.md` 3.1). Every model returns to its own
  expression of the earlier intent.
- **(b) One node's rule back to its state before its last change**, the rest as it is, for every
  node whose state in that model changed at least once. Today: the node's document before its last
  change. Grant-only: the records naming the node written at its last change are gone and the
  grants deleted at that change are back. Middle form: the same for grants, withdrawals, marks and
  lifts, and the node's reference as it was before. The previous state is each model's own, so it
  can come from a different edit in each model: a push-down changes nodes the operator did not
  edit, and a mark changes when something above it changes.
- **(b) by the mechanism `RESULTS.md` 2.5 names**, a stale copy of the node itself winning by time:
  what travels with the node goes back and nothing else does. Today the product keeps the document
  in the node's custom data, so this is (b). Grant-only keeps nothing on the node. The middle form
  keeps the reference on the node, and the records stay.
- **(c) The newest records of a node lost**, older records and deletions as they are (a deleted
  newest version, `RESULTS.md` 4.4 scenario 14). Today, read as the versioned store of
  `rules-central`, losing the newest version puts the previous version in force, which is the same
  documents as (b). Grant-only: the records naming the node written at its last change are lost,
  and the grants that change deleted stay deleted. Middle form: the same, and the node's reference
  as it is, so a node that refers to a lost mark refuses.
- **(d) One record of each kind lost**, each record present at the end in turn: today a document;
  grant-only a grant and a withdrawal (both witnesses, entry and key); the middle form a grant, a
  withdrawal, a mark with its reference, a mark whose reference stays (the node refuses), and a
  lift.
- **(e) Two edits of one node made apart**, then both kept. From the state before the last edit,
  every other edit of the same node is made in a second copy (the sample draws an edit of each kind,
  the exhaustive world takes all), and the two copies are combined as each store reads them. Today
  as `rules-central` 3.5 reads two newest versions, "what both allow": a profile both documents
  name keeps the verbs both give it, a profile only one document names keeps nothing, and the
  default is deny when either denies; a node only one copy changed takes that copy's document.
  Grant-only and the middle form: the union of grants, withdrawals, marks and lifts, a grant
  deleted in one copy and held in the other held.

### 2.6 The measure

For every trial and model: whether the table after the loss grants a cell the intent refuses
(**wider**), and how many such cells; otherwise whether it refuses a cell the intent grants
(**narrower only**); otherwise **equal** to the intent. Beside these, whether the loss changed the
table at all. For (e) the intent is what both edits allow, the cells both intents grant, which is
the reference the grant-only design uses for two decisions made apart (its 5.5); the same merges
are also held to what either edit allows.

## 3. The controls

### 3.1 The design's own trial

The design's trial 4, unchanged: its generator, its seed 20261115 and its draws, 400 trees of 30
nodes. Answered here with the product's resolver compiled from the commit and the carried
`GrantOnly`, in the design's 18 answers, and again through this trial's own 14 cell tables.

| Figure | Design's record (`model.txt`) | Here, 18 answers | Here, 14 cells |
| --- | --- | --- | --- |
| a random other document, today's model | 2,428 of 2,802 wider (52,209 answers), narrower in 2,440 | the same | 2,428 of 2,802 (41,135 cells), narrower in 2,440 |
| a random other document, grant-only | 2,280 of 2,802 wider (55,483 answers), narrower in 2,287; unexplained 0 | the same | 2,280 of 2,802 (43,417 cells), narrower in 2,287 |
| one rule lost, today's model | 1,025 of 3,175 wider (12,142 answers), narrower in 2,834 | the same | 1,025 of 3,175 (9,464 cells), narrower in 2,834 |
| one rule lost, grant-only | 0 of 3,175 wider, narrower in 2,809 | the same | 0 of 3,175, narrower in 2,809 |

The trial counts are the same in both layouts; only the count of cells differs, by the strip and
delete answers the 18 count three times.

### 3.2 No loss

After every edit, each model's table read from its own store, against the intent.

| Model | Sample states | Equal to the intent | Narrower | Wider | Exhaustive states | Equal | Narrower | Wider |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today's model | 20,000 | 18,201 | 1,799 (4,124 cells) | 0 | 466,980 | 444,635 | 22,345 (34,092 cells) | 0 |
| grant-only | 20,000 | 11,582 | 8,418 (77,056 cells) | 0 | 466,980 | 249,500 | 217,480 (1,331,426 cells) | 0 |
| middle form | 20,000 | 19,299 | 701 (3,191 cells) | 0 | 466,980 | 439,339 | 27,641 (92,178 cells) | 0 |

The "row whole" forms gave the same rows. In every model, each store read back equals the
expression it was written from (0 states unlike it), and every state that is not the intent is a
state the model cannot give exactly by the test of section 4, and every state that test marks is
narrower: 1,799, 8,418 and 701 in the sample, 22,345, 217,480 and 27,641 in the exhaustive world.
So with no loss each model gives exactly the intent wherever it can express it, and never more.

## 4. What each model cannot express

| Model | What it cannot give | What it gives instead | Example from the exhaustive world |
| --- | --- | --- | --- |
| today's model | a set of cells no single mask grants: a switch bound to the verbs of its own grant below an inherited rule | the widest single mask within it | root `.R...L..`, widened to `.RU..L..` and narrowed to `..U..L..`, then G adds `..U....P`: G, X, S and S1 lack update with protected values |
| grant-only | a node meant to hold less than its parent: a narrowing below a grant, an empty rule, a seal, a narrower rule below an inherited rule, and a grant or a widening above a narrower rule | push-down: the node above loses the letter, its other children get it | root `.R...L..`, widened to `.RU..L..` and `.RU..L.P`, then a rule `..U..L.P` added on G: the root lacks read, with and without protected values |
| middle form | a rule below a mark that grants again a letter the mark withholds (a mark overrides every grant under it) | the letter stays withheld below the mark | root `.R...L..` widened to `.RU..L..`, X given `.RU..L.P`, then an empty rule on G: X lacks everything it was given |

For the middle form the test is a property of the expression used here; a mix of marks and
push-down might give some of these states and was not tried.

Edits that turned a state the model gave exactly into a state it did not, by kind:

| Kind | Sample: today | grant-only | middle | Exhaustive: today | grant-only | middle |
| --- | --- | --- | --- | --- | --- | --- |
| grant | 1 | 208 | 35 | 30 | 13,384 | 3,574 |
| widen | 15 | 39 | 3 | 24 | 1,690 | 310 |
| narrow | 18 | 25 | 7 | 78 | 4,478 | 730 |
| empty rule | 0 | 267 | 22 | 0 | 15,722 | 3,415 |
| seal | 0 | 276 | 33 | 0 | 14,601 | 3,030 |
| remove | 1 | 0 | 0 | 20 | 24 | 0 |
| add below | 0 | 227 | 24 | 23 | 52,378 | 8,931 |
| adds below | 294 | 4 | 14 | 13,129 | 690 | 958 |

Today's model loses exactness only through an adds decision: by adding such a decision, or by an
edit above it that changes what arrives there. Grant-only loses it through every narrowing, and
through a grant or widening above a narrower rule.

## 5. Results by kind of loss

Percentages are of the trials in the same row. Cells are the cells granted beyond the intent,
summed, then their mean per wider trial and the most in a trial.

### 5.1 (a) The whole store as it was k edits ago

| Model | Sample: trials | Wider | Cells, mean, most | Narrower only | Exhaustive: trials | Wider | Cells, mean, most | Narrower only |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today's model | 20,000 | 15,701 (78.5%) | 208,730, 13.3, 420 | 4,221 | 466,980 | 257,458 (55.1%) | 1,737,418, 6.7, 40 | 199,615 |
| grant-only | 20,000 | 15,474 (77.4%) | 196,719, 12.7, 420 | 4,443 | 466,980 | 247,375 (53.0%) | 1,574,580, 6.4, 40 | 210,687 |
| middle form | 20,000 | 15,690 (78.5%) | 209,129, 13.3, 420 | 4,231 | 466,980 | 256,854 (55.0%) | 1,739,070, 6.8, 40 | 199,789 |

For k = 1 alone: 466, 444 and 464 of 1,000 in the sample; 77,271, 69,356 and 76,451 of 116,745 in
the exhaustive world. The "row whole" forms gave the same counts. Every model returns to its own
expression of the earlier intent, so a model is wider here exactly when that expression held a cell
the last intent refuses. Grant-only is wider slightly less often because its expression of the
earlier intent was itself narrower (push-down), not because of anything its store keeps.

### 5.2 (b) One node's rule back to its state before its last change

| Model | Sample: trials | Wider | Cells, mean, most | Narrower only | Exhaustive: trials | Wider | Cells, mean, most | Narrower only |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today's model | 9,922 | 5,019 (50.6%) | 31,096, 6.2, 406 | 4,697 | 344,668 | 175,441 (50.9%) | 768,132, 4.4, 40 | 152,863 |
| grant-only | 12,734 | 5,950 (46.7%) | 37,845, 6.4, 420 | 6,748 | 353,772 | 180,497 (51.0%) | 811,411, 4.5, 40 | 164,674 |
| middle form | 9,922 | 4,721 (47.6%) | 33,285, 7.1, 420 | 4,656 | 344,606 | 159,578 (46.3%) | 865,408, 5.4, 40 | 152,713 |

The "row whole" forms gave the same counts. By the kind of the edit that made the node's last
change, in the sample (trials and wider):

| Last change | Today's model | Grant-only | Middle form |
| --- | --- | --- | --- |
| grant | 2,943, 101 | 3,177, 3 | 3,046, 243 |
| widen | 886, 1 | 1,092, 0 | 971, 143 |
| narrow | 1,435, 1,435 | 1,716, 1,613 | 1,488, 1,290 |
| empty rule | 1,276, 1,221 | 1,991, 1,561 | 1,119, 988 |
| seal | 604, 497 | 1,257, 798 | 348, 322 |
| remove | 1,526, 1,395 | 1,888, 1,661 | 1,656, 1,372 |
| add below | 834, 369 | 1,192, 311 | 762, 334 |
| adds below | 418, 0 | 421, 3 | 532, 29 |

When the state that returns is from before a take-away, each model ends wider in most such trials:
after a narrow in 1,435 of 1,435 today, 1,613 of 1,716 under grant-only and 1,290 of 1,488 in the
middle form. In today's model the take-away is a narrower document; in grant-only a withdrawal,
written at the node or, by push-down, at the group above; in the middle form a withdrawal, a mark,
or a mark that changed because something above it was granted, which is why its rows for a grant
and a widen hold wider trials (243 and 143) where the other two hold almost none.

**By the mechanism named**, a stale copy of the node itself winning by time:

| Model | Sample: trials | Wider | Changed nothing | Exhaustive: trials | Wider | Changed nothing |
| --- | --- | --- | --- | --- | --- | --- |
| today's model | 9,922 | 5,019 (50.6%) | 300 | 344,668 | 175,441 (50.9%) | 19,350 |
| grant-only | 12,734 | 0 | 12,734 | 353,772 | 0 | 353,772 |
| middle form | 9,922 | 0 | 9,922 | 344,606 | 0 | 344,606 |

Grant-only keeps nothing on the node, and in the middle form an older reference changes nothing
while the mark records stay: a record counts wherever it is found, and an older reference names
only marks whose records were written earlier and are never deleted.

### 5.3 (c) The newest records of a node lost

| Model | Sample: trials | Wider | Cells, mean, most | Narrower only | Exhaustive: trials | Wider | Cells, mean, most | Narrower only |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today's model | 9,922 | 5,019 (50.6%) | 31,096, 6.2, 406 | 4,697 | 344,668 | 175,441 (50.9%) | 768,132, 4.4, 40 | 152,863 |
| grant-only | 12,734 | 1,376 (10.8%) | 3,652, 2.7, 96 | 9,946 | 353,772 | 77,438 (21.9%) | 208,026, 2.7, 20 | 246,529 |
| middle form | 9,922 | 858 (8.6%) | 4,487, 5.2, 96 | 5,777 | 344,606 | 31,296 (9.1%) | 163,491, 5.2, 20 | 236,095 |

The "row whole" forms gave the same counts. Today's row is (b) again: losing the newest version
puts the previous version in force. In grant-only a take-away that empties a row deletes the grant
in the same write, so losing the withdrawal alone brings nothing back; in the sample, of the 1,376
wider trials 1,107 follow a narrow and 240 an add below, and an empty rule, a seal, a remove and an
adds below together account for 29. In the middle form a lost mark whose reference stays makes the
node refuse, and a lost lift puts its mark back, so only a lost withdrawal widens: all 858 wider
trials follow a narrow.

### 5.4 (d) One record of each kind lost

| Record lost | Sample: trials | Wider | Cells, mean, most | Narrower only | Exhaustive: trials | Wider | Cells, mean, most | Narrower only |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today's model, a document | 8,525 | 1,917 (22.5%) | 10,484, 5.5, 208 | 5,534 | 327,734 | 125,362 (38.3%) | 530,785, 4.2, 32 | 175,067 |
| grant-only, a grant | 11,915 | 0 | 0 | 11,911 | 283,348 | 0 | 0 | 277,342 |
| grant-only, a withdrawal | 10,897 | 2,246 (20.6%) | 6,160, 2.7, 116 | 6,226 | 265,793 | 103,186 (38.8%) | 308,188, 3.0, 20 | 123,515 |
| middle form, a grant | 7,834 | 0 | 0 | 7,331 | 289,668 | 0 | 0 | 222,493 |
| middle form, a withdrawal | 6,274 | 1,350 (21.5%) | 6,764, 5.0, 116 | 356 | 91,454 | 35,878 (39.2%) | 186,158, 5.2, 20 | 4,145 |
| middle form, a mark and its reference | 2,353 | 2,282 (97.0%) | 11,211, 4.9, 204 | 5 | 143,306 | 140,975 (98.4%) | 590,495, 4.2, 32 | 225 |
| middle form, a mark, its reference kept | 2,353 | 0 | 0 | 1,494 | 143,306 | 0 | 0 | 100,813 |
| middle form, a lift | 1,397 | 0 | 0 | 495 | 45,277 | 0 | 0 | 19,820 |

The "row whole" forms gave the same counts. A lost document in today's model and a lost withdrawal
in either grant-only form widen in about the same share of trials; a lost grant never does; a mark
lost with its reference almost always does, and the same mark lost while its reference remains
never does, because the node then refuses.

### 5.5 (e) Two edits of one node made apart, both kept

Against what both edits allow:

| Model | Sample: trials | Wider | Cells, mean, most | Narrower only | Exhaustive: trials | Wider | Cells, mean, most | Narrower only |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today's model | 4,055 | 19 (0.5%) | 136, 7.2, 20 | 1,214 | 526,850 | 1,374 (0.26%) | 3,522, 2.6, 12 | 54,100 |
| grant-only | 4,055 | 2,761 (68.1%) | 23,447, 8.5, 416 | 888 | 526,850 | 266,360 (50.6%) | 966,282, 3.6, 30 | 172,088 |
| middle form | 4,055 | 2,716 (67.0%) | 24,061, 8.9, 474 | 167 | 526,850 | 177,232 (33.6%) | 582,912, 3.3, 30 | 66,414 |
| grant-only, row whole | 4,055 | 2,434 (60.0%) | 22,498, 9.2, 416 | 1,136 | 526,850 | 229,014 (43.5%) | 889,284, 3.9, 30 | 201,716 |
| middle form, row whole | 4,055 | 2,501 (61.7%) | 23,277, 9.3, 474 | 180 | 526,850 | 161,328 (30.6%) | 550,602, 3.4, 30 | 67,494 |

Against what either edit allows:

| Model | Sample: trials | Wider | Cells, mean, most | Exhaustive: trials | Wider | Cells, mean, most |
| --- | --- | --- | --- | --- | --- | --- |
| today's model | 4,055 | 0 | 0 | 526,850 | 0 | 0 |
| grant-only | 4,055 | 106 (2.6%) | 277, 2.6, 34 | 526,850 | 29,078 (5.5%) | 45,898, 1.6, 12 |
| middle form | 4,055 | 120 (3.0%) | 361, 3.0, 54 | 526,850 | 29,348 (5.6%) | 55,330, 1.9, 12 |

The "row whole" forms gave the same counts as the primary forms against what either edit allows.
Where no policy of the three holds an adds decision (1,841 merges in the sample, 431,716 in the
exhaustive world), today's model is never wider than what both edits allow, and grant-only and the
middle form are in 1,180 (64.1%) and 1,171 (63.6%) of the sample and in 200,392 (46.4%) and
120,868 (28.0%) of the exhaustive world; against what either edit allows, 32 (1.7%) and 29 (1.6%),
and 15,880 (3.7%) and 10,438 (2.4%).

What makes each wider, from the recorded examples:

- **Today's model**, only where an adds decision is in play: a rule written again at another node
  because what arrives there changed, which only one copy did, so the merge takes that copy's
  rule. Example: root `.R...L..` widened to `.RU..L..`, G adds `..U....P`, then the root widened to
  `.RU..L.P` in one copy and narrowed to `.R...L..` in the other: G's rule from the first copy
  stands, and G and everything under it read and list protected values that the second copy's
  intent refuses.
- **Grant-only and the middle form**, by the design's union: a letter added in one copy and not
  taken away in the other stays, whichever copy took other letters away; a letter added on a row
  the other copy took away whole is cut only in the "row whole" form; and two grants on one row,
  the second adding a switch, give that switch to the verbs of the first (the design's own S1).
  Example of the last: the root's rule `.RU..L.P` and G's rule `..U..L.P` in one copy, G's rule
  `.RU..L..` in the other: G and everything under it read protected values, which neither copy
  allowed.

## 6. Where each model ends wider, and whether the evidence separates them

| Loss | Today's model | Grant-only | Middle form |
| --- | --- | --- | --- |
| (a) whole store k edits back | 78.5% / 55.1% | 77.4% / 53.0% | 78.5% / 55.0% |
| (b) a node's rule back | 50.6% / 50.9% | 46.7% / 51.0% | 47.6% / 46.3% |
| (b) a stale copy of the node | 50.6% / 50.9% | 0 / 0 | 0 / 0 |
| (c) a node's newest records lost | 50.6% / 50.9% | 10.8% / 21.9% | 8.6% / 9.1% |
| (d) a document lost | 22.5% / 38.3% | | |
| (d) a grant lost | | 0 / 0 | 0 / 0 |
| (d) a withdrawal lost | | 20.6% / 38.8% | 21.5% / 39.2% |
| (d) a mark and its reference lost | | | 97.0% / 98.4% |
| (d) a mark lost, its reference kept; a lift lost | | | 0 / 0 |
| (e) two edits apart, beyond what both allow | 0.5% / 0.26% | 68.1% / 50.6% | 67.0% / 33.6% |
| (e) the same, beyond what either allows | 0 / 0 | 2.6% / 5.5% | 3.0% / 5.6% |

Each cell is the share of trials wider than the operator's last intent, in the sample and then in
the exhaustive world.

- **A whole store going back, and a node's rule going back as an abstract state**: the models end
  wider about equally often, within 2.1 points in (a) and within 4.7 points in (b), and in (b) the
  order is not the same in both worlds. The evidence does not separate them here.
- **A stale copy of the node winning by time**: today's model ends wider in 50.6% and 50.9% of the
  trials, and neither grant-only form ever does, in 12,734 and 353,772 trials for grant-only and
  9,922 and 344,606 for the middle form. This separates them, by where each keeps a rule.
- **A node's newest records lost**: today's model, read as a store of versions, ends wider in
  50.6% and 50.9%; grant-only in 10.8% and 21.9%, the middle form in 8.6% and 9.1%. This separates
  them, because a grant-only take-away that empties a row deletes the grant it empties.
- **One record lost**: a lost document and a lost withdrawal widen in about the same share of
  trials (22.5% against 20.6% and 21.5%; 38.3% against 38.8% and 39.2%), a lost grant never
  widens, and the middle form's mark widens almost always when its reference goes with it and never
  when the reference remains. Per lost record of the kind that narrows, the evidence does not
  separate them.
- **Two edits made apart**: here the order reverses. Today's reading of two versions is never wider
  than what either edit allows (0 of 4,055 and 0 of 526,850) and is wider than what both allow only
  where it has to write again a rule for a grant-only style decision (19 and 1,374, none without
  such a decision). Grant-only and the middle form are wider than what both allow in 68.1% and
  67.0% of the sample and 50.6% and 33.6% of the exhaustive world (60.0%, 61.7%, 43.5% and 30.6% in
  the "row whole" form), and wider than what either allows in 2.6% and 3.0%, and 5.5% and 5.6%.

So the evidence separates the models by kind of loss and not as a whole: grant-only and its middle
form never end wider when a stale copy of a node wins and end wider less often when a node's newest
records are lost; today's model never ends wider than either of two decisions made apart; and for a
whole store going back, a node's rule going back as a state, and a single record of the narrowing
kind lost, the counts are about the same. Without loss, grant-only cannot give 42.1% and 46.6% of
the intended states and gives less instead; today's model cannot give 9.0% and 4.8%, the middle
form as expressed here 3.5% and 5.9%.

## 7. What the trial does not cover

- **How often each loss happens** to each store. The trial measures what a loss does once it has
  happened. Which programs and merges cause which loss to which carrier is `RESULTS.md` sections 2
  to 4; nothing here weighs the kinds of loss against each other.
- **The tree.** Nodes are never moved, copied, displaced, deleted or added; the design's own point
  that under grant-only a move is a change of policy (its 4.2) is untouched here, and so is a
  narrowing kept by where a node sits.
- **The intent's meaning of a grant.** An edit in today's vocabulary is read as an exact decision,
  so a grant at a group above a narrower rule leaves that rule standing; an operator who means
  "add to everything under it" has another intent, which this trial does not generate.
- **The middle form with push-down**, a mark for every profile at once, and a node whose
  reference names a lost mark when it has other marks: the expression here is marks per profile
  only.
- **Authentication** of grants and lifts, the withdrawal's two witnesses taken apart, the recycle
  bin, the profile list, a client holding several profiles, and the API's listings.
- **Three or more copies**, losses after losses, repeated merges, and histories longer than 20
  edits; the exhaustive world is a single tree of five nodes, one profile and a small alphabet.
- **Today's model in the product's own store under (c)**: the product keeps no older version, so a
  lost document is (d); (c) is today's model read as the store of versions of `rules-central`,
  whose detection of a deleted version (a refusal, its section 3) is not modeled.
- **The plugin**, KeePass, KeePassXC and every phone program: no database was made, and no program
  merged anything.
