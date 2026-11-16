# Option `grant-only`: rules that can only add

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. One variant, worked out
in its strongest form, with what it costs. It covers datum 7, the access rules. For data 1 to 6 it
uses the store group and the pass of `option-store-entries.md` and changes nothing in them.

The variant came out of the completeness check of `EVALUATION.md`: every rule design there hardens
the store against the loss of a narrowing. This one changes the rule model so that there is no
narrowing to lose. That reverses a settled decision (CLAUDE.md: "Nearest wins, per profile", "An
empty array is a rule granting nothing from there down", `default`), so section 2 argues it.

## How statements are marked

- **[M here]**: measured for this report, today, from a console. Section 11 names the probe, its
  runs and its files. Today's answers in the model runs come from the plugin's own `AclResolver`
  and `AclDocument`, compiled into the probe from the review4 worktree at `a42e38aa`.
- **[M spike]**, **[M android]**, **[M central]**, **[M linked]**, **[M critic ...]**: measured in
  `spikes/revocation-merge` as the ground reports digest it, in `measured-android.md`, in the probe
  of `option-rules-central.md` or `option-rules-linked.md`, or in the named critique.
- **[R plugin]**: read by me in the review4 worktree at `a42e38aa`, file and line given.
  **[R ...]**: read in the named report.
- **[A]**: assumed. Neither run nor read.

## 1. The model

### 1.1 What a rule is

A rule is one row: **a node, a profile, and a `CRUSDLAP` mask**. Nothing else. There is no
`default`, no seal, and an empty mask is not a rule. A node carries at most one row for a profile;
the row is what the grant editor shows on that node's line for that profile.

### 1.2 How rows combine

For one profile, at node X, asked whether verb `v` may reach as far in as `r`:

> permitted when **some node N on the path from X up to the root** carries a row for the profile
> that holds `v` and, where `r` needs a switch and `v` is not a removal, holds that switch in the
> same row.

Everything that is not granted that way is refused. A node with no row of its own has exactly what
the groups above it give. No row nearer the node can take away what a row above grants.

| Part of today's model | In this model |
| --- | --- |
| Nearest wins, per profile | Gone. Every row on the path counts, for its own letters |
| `default: deny`, the seal | Gone. Nothing stops a grant on its way down |
| The empty array, `........` | No meaning. The editor cannot make one, and a record holding one counts for nothing |
| A profile is resolved on its own, then profiles are combined with or | Unchanged |
| Switches `A` and `P` | Still switches. A switch counts for the verbs **of the row it is written in**, so `.R...L..` on a group and `..U....P` on an entry inside it give that entry read without protected values and update with them. One row's `R` and another row's `P` do not add up to reading a password, as one profile's `R` and another profile's `P` do not today (`Acl/AclResolver.cs:292-296` [R plugin]) |
| Neither switch is asked for `S` or `D` | Unchanged |
| A removal asks `D` twice, from the group the node leaves and from the node, of one profile (`Pipeline/AccessCheck.cs:571-619` [R plugin]) | Unchanged. `D` on a group still makes its contents removable while the group stays, because nothing above the group grants `D`. What goes is the entry that holds less than its group (section 3) |
| The strip guard (`S` with `C` and without `P`) | Unchanged, asked over the same answers |
| Listings pool the profiles; a change needs one profile | Unchanged |
| The profile list decides what a client holds | Unchanged |
| A document that will not read refuses there and below | Gone. A grant that will not read grants nothing, which is the closed direction when no rule can take away |
| The walk's depth bound | Unchanged |
| "Everything that is not an explicit permission is a refusal" | Unchanged |

**The simpler alternative for the switches, not chosen.** Take the union of all letters on the
path and read it as one mask. One sentence explains it, and the answer to "what may I do here" is
one exact mask. But then `C......P` on a subgroup, given so that a program may add protected
fields, also opens every password there to an `R` granted above, which no row stated. I chose the
form in which a grant means what its own row holds and nothing more. The price is in section 6:
the answer to "what may I do here" can need more than one mask per profile.

**On every rule set without a narrowing the two models answer alike.** 400 generated trees, 3,131
rules in which no nearer rule holds less than the rule above it and no seal: 648,000 answers
(node, profile, verb, reach), today's resolver against this one, 0 different [M here]. So an
operator who never narrowed sees no change in what a program gets.

### 1.3 The model itself, not a refusal in the editor

Could today's resolver stay, with an editor that refuses to write a narrowing? No. Under nearest
wins a rule is a narrowing or not depending on what stands above it, and that changes without the
rule being touched: the root grants `R`, a group grants `RU` (wider, allowed), the operator later
adds `D` at the root, and the group's untouched `RU` now takes `D` away below it. The editor would
have to rewrite every rule under each change. And a nearest-wins resolver still honors a narrowing
that a merge, an older copy or a hand edit brings, so a lost rule could still widen. Only a
resolver that adds makes the property hold for whatever the file contains: "by construction" has
to be the resolver's construction.

### 1.4 A narrowing that arrives anyway

- **From a merge or a hand edit of the new store**: the store has no record kind that narrows a
  grant's reach down the tree (section 5), so there is nothing to arrive. A record of a kind the
  plugin does not know counts for nothing.
- **From a database made with 0.1.0**: its rules sit in node custom data, which this design does
  not read as rules. Ignoring them would widen: read as grants, with the seal and the empty array
  ignored, today's documents gave 13,442 answers "permitted" that today's model refuses, in 291 of
  400 generated trees, and none the other way [M here]. So they are neither ignored nor read:
  section 8 has the conversion and the one check that refuses.

### 1.5 A middle form: grants add, and a mark that only takes away

If the operator needs an exception inside a granted group, or a secret that stays closed wherever
it is moved or copied, one more kind of record does it, and only the nodes that carry one pay for
the machinery:

- A **withheld mark** names a node, a profile or every profile, and the letters it withholds (all
  eight for "nothing here"). It **overrides every grant**, above, on and below the node. There is
  still no nearest wins: a mark is absolute for the profiles it names.
- A mark only takes away, so it counts wherever it is found and needs no authentication. Lifting
  one widens, so a lift is a record that must be authentic, like a grant.
- The marked node refers back to its mark as `option-rules-linked.md` has it (a custom string on
  an entry, a link entry in a group), for two jobs: a copy of the node carries the reference and is
  withheld too, and a node whose mark is gone refuses.

Modeled over 400 trees with 1,723 marks [M here]:

| Event | Wider in | Trials |
| --- | --- | --- |
| A node's grants lost | 0 | 3,158 |
| A mark's record lost while the node still refers to it (the node refuses) | 0 | 1,723 |
| A copy beside the original that carries the reference and no grant | 0 | 1,723 |
| A marked node displaced to the root, where no group above it was marked | 0 | 975 |
| A mark's record **and** the node's reference both lost | 847 | 1,723 |
| A copy made by a program that drops the reference | 541 | 1,723 |
| A node displaced to the root out of a **marked group** | 91 | 300 |

So a mark protects the node that carries it, wherever it goes, and what is inside a marked group
only while it is inside. Against `rules-linked` it needs no Confirm, no Adopt, no grant that waits
and no limit in the resolver, because a mark never gives. It brings back a field on marked entries,
a link entry in marked groups, the reserved name inside entries the API serves, and one refusal
state with its Settle press. Sections 3 to 7 are about the strict form; where the middle form
changes an answer it is named.

## 2. The argument for reversing "nearest wins"

1. **New evidence.** Nearest wins was chosen so that "a grant made at the root is taken away in one
   group without a deny syntax to get wrong" (`Acl/AclResolver.cs:122-125` [R plugin]). That
   weighed expressiveness against a syntax error. It did not weigh a rule that another program
   deletes, rolls back or fails to copy, which is now measured [M spike; M android].
2. **The narrowing is what makes those losses dangerous.** One rule lost, in 3,175 trials over
   generated trees: under today's model the answer got wider in 1,025 of them; under today's model
   with rule sets that hold no narrowing, in 0 of 3,136; under this model, in 0 of 3,175 [M here].
   Every witness, reference, refusal and press in `rules-linked` exists to catch the 1,025.
3. **The plugin's stated purpose is the shape this model keeps.** "A narrow, revocable, audited
   slice of a database" (CLAUDE.md) is a grant on a group, low in the tree. The documentation
   already steers there for `D`: "put `D` on the group whose contents may come and go, rather than
   at the top of the database" (`docs/access-control.md:203-205` [R plugin]).
4. **What it gives up is real and is in section 3**: the documentation's own first picture is a
   grant on the top group with one folder sealed.

## 3. What the operator loses

### 3.1 Shape by shape

| Shape in today's model | In this model | How the operator gets the effect, and what it costs |
| --- | --- | --- |
| "Everything in this group except that subgroup" (a seal or `........` on the subgroup) | Cannot be written | Move the subgroup out from under the granted group: one move, and the tree stops following the operator's own order. Or take the grant off the group and put it on every other child: one grant per sibling, a new sibling gets nothing, and the group itself is no longer listed or created in |
| A sealed subtree under a grant at the root (the README's story) | Cannot be written | The root grant goes. Each top-level group but the private one gets the grant. A new top-level group is closed until granted, which is the safe default. A program can then no longer start at the root (section 6) |
| "Read everywhere, but protected values only here" | **Kept.** It is an addition: `.R...L..` above, `.R...L.P` here | Nothing |
| "Protected values everywhere, but not here"; attachments the same | Cannot be written | As the first row |
| A narrower mask on one entry: "this entry cannot be removed while its siblings can" (`docs/access-control.md:200-201`) | Cannot be written | The entry leaves the group whose contents may go. A subgroup does not help, since it inherits the `D`. So: `D` on a subgroup for what may come and go, and what must stay sits directly in the parent, which has no `D` |
| One profile closed off in a subtree, the others not (`........` for one profile) | Cannot be written | As the first row, per profile. Where two profiles need different exceptions that do not nest, no tree serves both: grants go down to single entries, and a new entry gets nothing until granted |
| A private folder that stays private when dragged under a granted group | Cannot be had | Nothing in the strict form. The middle form's mark travels with the folder |
| A narrowed node whose copy stays narrowed (Duplicate, a template with a rule) | A copy has no rule of its own | Beside its original a copy gets at most what the original has; elsewhere it gets what that place gives (section 4) |
| Taking one node away from a profile that reached it | Only by moving it | A move is merged by time and a later move in another copy wins (section 4.2). The durable ways are to take the group's grant away, or the middle form's mark |
| A rule that will not read closes its subtree | Not needed | A damaged grant grants nothing |
| Widening below a grant (`R` above, `RU` here) | Kept | Nothing |
| New: different verbs with different switches at different levels, for one profile | Possible | Today this needs two profiles |

**Cannot be had at all, by any arrangement**: a node that sits inside a group and holds less than
that group. Every static policy over the nodes that exist today can still be written, in the worst
case as one grant per entry, but then nothing covers an entry made later. What is lost is the
exception under a default, not the policy.

### 3.2 How much of what is recommended today is a narrowing

- `docs/access-control.md` recommends **three narrowing shapes**: the rule without `D` on one entry
  (lines 198 to 201), the empty mask on a group (389 to 392), and the seal, "use it on a group
  holding things no program should reach" (394 to 407). It recommends one shape this model keeps:
  `D` low, on the group whose contents may come and go (203 to 205). The sections "Inherited
  rules", "Nearest wins" and "Refusing everything not named" (347 to 407) are about narrowing as a
  whole, and "A copy of a node carries its rules" (452 to 463) exists because of it [R plugin].
- `README.md` opens with `docs/images/lifecycle.gif`, "a program that reads one folder, is refused
  the other". Its source grants the profile on **the top group** and then seals the second folder
  (`tests/KeePassGate.Host.Tests/Live/DocumentationLifecycle.cs:317-366`), and lines 113 to 117 of
  the README describe nearest wins and the seal [R plugin]. So the first shape a reader is shown is
  the one this model cannot express. Under it the story is shorter: grant the one folder.
- In the tests' fixtures: 18 uses of `Rules.Sealing(` in 8 files and 22 rules with an empty mask in
  8 files, against 274 uses of `Rules.Granting(` in 32 files; 66 test methods in 28 files name a
  narrowing in their own name (seal, nearest, narrow, empty rule, struck, closes), of about 4,200
  test attributes; in `AclResolverTests`, 7 of 30 tests pin a narrowing [M here, by search;
  the name pattern is rough].

### 3.3 What nobody can count from here

How many narrowing rules an existing database holds, and whether those nodes can move.
That is the falsifier of this option and only a count over such a database can answer it.

## 4. What becomes of each failure

### 4.1 The table

"Today" is 0.1.0: nearest wins, the rule in the node's custom data. "Linked" is `rules-linked`
with its critics' changes, as `EVALUATION.md` 3.8 and 5.1 have it.

| Failure | Today | Linked | Grant-only |
| --- | --- | --- | --- |
| **A rule lost** (a KeePassDroid save, a deletion by hand, a merge) | Silent; wider wherever the rule narrowed: 1,025 of 3,175 modeled losses [M here; the deletions M spike, M android] | The node refuses while its reference or the version's record remains; a first grant can wait for Confirm | Never wider: 0 of 3,175 [M here]. Nothing refuses. The program has less until the operator grants again. The one loss that widens is the loss of a **withdrawal** (below) |
| **A rule rolled back to an older version** (a stale rule under a newer time; the newest version lost) | Silent, either way [M spike] | The record refuses; version and record lost together, then a KeePassXC merge of an older copy, puts the older rule in force [M critic `rules-linked-survival` P2] | There are no versions. A rollback widens only when the older state held a letter the newer lacks (2,280 modeled rollbacks widened, none without that [M here]), so the whole problem is **a grant that was taken away coming back**. That needs the withdrawal gone from that copy, entry and key, and a merge that brings the grant back: section 4.3 |
| **A copy beside its original** (Duplicate Entry, Duplicate Group) | The copy carries the rule [M critic `rules-central-security-and-fit` P1]; under a store that names nodes it would carry none, and be wider in 1,552 of 4,428 modeled copies [M here] | The copy carries the reference and gets the original's rule as a limit; Delete Duplicate Entries can delete that one reference [M critic `rules-linked-survival` P1] | The copy has what its place gives, never more than the original: 0 of 4,428 [M here]; the copy of an entry with a grant of its own had only the group's, and the copy of a granted group, under the root, had nothing [M here, section 11] |
| **A copy put elsewhere** (paste, a drag that copies, a phone's copy) | Carries its rule | Carries the limit | Gets what the new place gives. A copy of the secret Q put into the granted group G was readable [M here]. The operator copied a secret into a place a program reads; nothing in this model stops that |
| **An import with new identifiers** | Copies carry their rules, at the root | Copies carry their references | Copies sit at the root with no grant: closed unless the root is granted. The copies of grant records name the original nodes and change nothing: 10 of 10 cells [M here]. Delete Duplicate Entries afterwards deleted the originals and left the copies, with no grant: closed, and the program has lost its access [M here] |
| **A node moved into a granted group by the operator**, in KeePass or with Keepass2Android's Move | A node with no rule gets the group's grant; a sealed or narrowed node stays closed | As today | Gets the group's grant. No node stays closed by itself. The middle form's mark does |
| **A node displaced to the root by a program** (Keepass2Android when the entry's group is missing [R `ground-android.md`], KeePassDX's group deletion [R critic `rules-central-survival` C4], an import) | Wider when it leaves a sealed group: 494 of 8,576 modeled displacements [M here] | The same for a node with no rule of its own | Never wider: 0 of 8,576 [M here], because a shorter path has fewer grants. Open only if the root itself is granted |
| **A merge that undoes a move** | Section 4.2. Matters for nodes kept closed only by where they are | The same | **Worse.** Section 4.2 |
| **A planted rule from a merged file** | Only by bringing the node itself under a newer time, which is visible | An entry's planted rule only limits; a group's can be planted from both sides unless versions are authenticated [M linked 5.5] | A grant counts only with this database's store key: a planted grant counted in 0 of 18 cells (10 KeePassLib, 8 KeePassXC) [M here]. A planted **withdrawal** took the grant away in every cell [M here]: a file the operator merges can close access, not open it. Authentication stops being optional (section 5.3) |
| **Two copies changed the same node's rule** | One copy's rule whole, by time; the other's gone [M spike] | What both allow | Additions unite; a letter taken away in either copy stays away; a letter added without knowledge of a removal of that row is cut. 30 of 30 KeePassLib cells and 24 of 24 KeePassXC cells in scenarios 01, 03 and 04 [M here] |
| **A rule edited by hand** | Unreadable: refuses there and below | Refuses | The record no longer verifies and grants nothing |
| **The plugin's group deleted** | Not applicable | No rule text left in either copy after a Synchronize [M critic `rules-linked-survival` P3] | Every grant gone: closed. The operator grants again |
| **A whole file written over another; a stale copy on its own** | Not solved | Not solved | Not solved. The stale file's grants, and its tree, are in force |

### 4.2 The tree is the policy: moves, measured

In this model the place of a node is the only thing that keeps it from a profile, so a move is a
change of policy. A node's place is one value per node, changed in place and merged by time, which
is the kind of carrier `EVALUATION.md` 2.3 found unable to carry the store.

Base: group G granted `.R...L..` to `agent`, with entry X in it and a subgroup S; group H granted
nothing. "Open" means `agent` can read X.

| Scenario | KeePass Synchronize | KeePass, the three other methods that match identifiers | KeePassXC, four releases |
| --- | --- | --- | --- |
| 09: A moves X from G to H; B is a copy from before, saved later | closed in both directions | into A closed; **into B, X stays in G: open** | closed in both |
| 10: the same, and B changed X's password later | closed in both | into A closed; **into B open** | closed in both |
| 11: A moves X to H; B, not knowing, moves X into G/S **later** | **open in both directions** | each copy keeps its own place | **open in both directions** |
| 12: the same, B's move earlier | closed in both | each copy keeps its own place | closed in both |
| 13: A moves the group S out of G to the root; B adds an entry in S later | closed in both, the new entry too | into A closed; **into B open** | closed in both |
| 14: A deletes X for good; B is a copy from before | gone in both | into A gone; **into B, X stays** | **X is in the result in both directions, open**: merged into A it comes back |

All [M here]: the 48 KeePassLib cells of the four methods that match identifiers, and 48 KeePassXC
cells. Under Create new IDs (12 more cells) each original stays where the open copy had it and the
copies arrive at the root with no grant. Beside them, from the emulator: a
Keepass2Android merge carried a move made in one copy, in both directions; a KeePassDX 4.5.8 merge
with the stale copy arriving put the moved entry **back** where the stale copy had it [M android
2.2, 4.3]. KeePassDroid has no move and no merge [M android 3.1].

What this means, plainly:

- **A single move is carried** by KeePass's Synchronize, by KeePassXC and by Keepass2Android. Those
  are the merges the programs in scope make.
- **Two moves of one node in two copies: the later one wins**, in KeePass's own Synchronize, and the
  copy that had moved the node out gets it back under the grant with nothing shown (scenario 11).
  A tidy-up of a folder on the laptop is enough.
- **A deletion is no way to take a node away**: KeePassXC brings the entry back.
- **Does this option make that worse? Yes.** Today and under `rules-linked` the same merges do the
  same things to a node's place, and a node kept closed only by its place is exposed the same way.
  But there the operator has a second tool, a rule on the node, and the documentation points to it.
  Here every narrowing of a node that a profile once reached is a move, so everything the operator
  narrows that way rests on the weak carrier. A node that was never under a grant in any copy is
  not affected: a merge invents no place.
- **What is robust instead**: taking the group's grant away, which is a withdrawal and survived
  every cell (scenario 15, 10 of 10 and 8 of 8 [M here]); changing the secret, which a node that
  a program could read needs anyway; and the middle form's mark.

### 4.3 What the taking away of a grant still needs

A grant is only ever added. Taking letters away is a second record, a withdrawal, also only ever
added (section 5.2). So the one datum whose loss widens is the withdrawal, and it needs exactly
what a revocation needs in `EVALUATION.md` 5.1: an entry and, as a second witness, an add-only key
in the database's custom data.

- **With the withdrawal's entry present**, the grant stayed cut in every cell: a stale copy that
  still held the grant, merged in either direction, under five KeePassLib methods and four
  KeePassXC releases, for KeePassXC with a fast and a slow key derivation (scenarios 02, 05, 15,
  17). Where KeePassXC brought the deleted grant entry back, the withdrawal cut it [M here].
- **With the withdrawal's entry deleted by hand and no deletion record** (what pykeepass does
  [M critic `rules-central-survival`]), and a stale copy merged (scenarios 06, 16):
  - with the key read, the grant stayed cut in 9 of 10 KeePassLib cells and 16 of 16 KeePassXC
    cells. The tenth is Keep existing into the stale copy, which takes nothing, so that copy stays
    as it was [M here];
  - without the key, the grant was back in 5 of 10 KeePassLib cells and in all 16 KeePassXC cells
    [M here]. So the key is not decoration.
- **Both witnesses gone**: an extra run of 8 cells, in which the stale copy had last been saved by
  KeePassXC itself (2.4.2 and 2.8.0, fast and slow key derivation, both directions) [M here]. In
  one cell, 2.8.0 with the slow derivation merging the stale copy into the copy whose withdrawal
  entry had been deleted, the merge removed the key as well and **the withdrawn grant was in force
  again in the copy that had withdrawn it**. In one more, 2.8.0 with the fast derivation, the key
  did not reach the stale copy, which stayed as it was. The other six stayed cut. The first is the
  spike's measured replacement of the whole dictionary [M spike], and it is the residue: a deletion
  by hand of a plugin entry, then a KeePassXC merge of an older copy. It is the same residue the
  evaluation accepts for a revocation.

Nothing else remains of the rollback problem, because nothing else is ever replaced.

## 5. The store this model needs

### 5.1 The records

In the plugin's own group, as `option-store-entries.md` section 2 makes it, with the fresh
identifier each time it is made that `EVALUATION.md` 5.1 rule 8 adds. Ordinary entries, written
once, never changed, user name, password and URL empty, one fixed line of notes.

| Record | Custom strings | Counts |
| --- | --- | --- |
| **Grant** | `KPG.Kind` `grant`; `KPG.Id`, 16 random bytes; `KPG.Node`, the node's identifier; `KPG.Profile`; `KPG.Mask`, the letters this grant adds; `KPG.Saw`, the ids of the withdrawals of that row it was made after, or `none`; `KPG.Check`, an HMAC over the other strings under the store key | with a valid check, outside the recycle bin. Its place otherwise means nothing |
| **Withdrawal** | `KPG.Kind` `withdrawn`; `KPG.Id`; `KPG.Node`; `KPG.Profile`; `KPG.Mask`, the letters taken away | wherever it is found, the recycle bin included, with or without a check. One that reads only in part takes the whole row of its node and profile |
| **The withdrawal's second witness** | database custom data key `KPG.Withdrawn.<id>`, value node, profile and letters | whenever found. Written once, never changed, never removed |

A title carries the profile, the mask and a few digits of the node and of the id (`KPG grant agent
.R...L.. 4F0C91AA 3b9e1c`), so no two records look alike. That answers the "ten identical entries"
finding against `rules-central` [M critic `rules-central-security-and-fit` S3].

### 5.2 What is in force

- A grant's **live letters** are its mask less the letters of every withdrawal of its row (same
  node, same profile) that it does not list in `KPG.Saw`.
- **The row** of a node for a profile is the union of the live letters of its grants.
- The resolver of section 1.2 reads rows. Nothing reads a time, a history item, custom data on a
  node, a deletion record or a group's own fields. Nothing is written at open or after a merge.

So a change on the tab is: letters added, one new grant holding those letters; letters removed,
one withdrawal holding those letters and its key. A grant left with nothing is deleted in the same
write, as an admission record is at a revocation.

**Why a withdrawal names the row and its letters, and a grant lists what it saw.** With whole
grants replaced at each change, two copies that each took a different letter away would both
re-issue the letter the other removed, and the merge would hold the rule from before either change.
Here the first copy's withdrawal of `L` and the second's of `R` both cut the one grant: nothing was
left in 10 of 10 and 8 of 8 cells (scenario 03) [M here]. And a letter added in one copy while
another copy removed the row is cut, because the new grant did not see that withdrawal (scenario
04) [M here]: a removal wins over an addition that did not know of it, the rule `EVALUATION.md`
5.1 has for the profile list. A grant made after the merge lists the withdrawal and holds.

A copy of a grant under another identifier is the same grant (the id is a string, not the entry's
identifier): two copies changed nothing, and a copy of a withdrawn grant stayed cut [M here].

### 5.3 The one piece that has to hold: authentication

In this model a planted or stale grant is the only way to widen, so `KPG.Check` is load bearing.
The key is a random value held once per database in the authority's document and carried over by
Recreate; no route serves it and no file made elsewhere knows it. A foreign authority already ends
in `503 undecided`. This is rule 3 of `EVALUATION.md` 5.1 in its stronger form, and three critics
asked for it independently. Whoever holds the master key can write a valid grant; that is conceded
today. What it shuts out is measured: a foreign file's grant on H, in a group carrying the store
group's identifier, counted for nothing in 18 of 18 cells [M here].

An open point: Delete authority followed by Create makes a new key, so every grant stops counting
until granted again. Carrying the key across a Delete needs a place for it outside the authority,
and a rule for two such places. Not designed.

### 5.4 What falls away, against `rules-linked`, and what stays

| Falls away | Why |
| --- | --- |
| Rule versions, `KPG.After`, the newest version, "what both allow" | Nothing is replaced. Two records that never saw each other unite |
| The record as second witness for every version | A lost grant narrows. Only a withdrawal has a witness |
| The anchor on a ruled entry, the link entry in a ruled group | Nothing of the plugin's sits on a user's entry or in a user's group |
| Limits in the resolver; the states "not confirmed", "a copy", "moved link" | A rule the node does not confirm cannot exist |
| Confirm, Adopt, Settle, Put back; grants that wait | Nothing refuses, so nothing is settled |
| Refusing nodes, "discard the unreadable rules", refusals with nobody at fault | A damaged grant grants nothing |
| `503 undecided` for copies of rule entries (`rules-central`) | A copy of a grant is that grant again |
| The reserved name hidden inside entries the API serves; `deleteGroup` over a group that holds only its link | No string on user entries, no entry in user groups |
| A fourth page on the KPG tab | Counts on the Profiles page are enough: grants, grants of nodes that are gone, records that do not verify |
| `AclDocument`, `AclDefault`, the document's JSON and its ceilings | A row is a mask |

| Stays | Note |
| --- | --- |
| The plugin's group and entries written once | The one carrier every program keeps [M android; M here: four KeePassXC plain saves left every record's identifier, strings, flags and times unchanged] |
| The pass over every entry on each request | 4.0 to 4.8 ms for 10,070 entries with 60 grants and 10 withdrawals, in a console, not tuned (two string lookups an entry) [M here]; `rules-central` measured 1.4 ms for the same walk [M central]. Not measured on KeePass's thread |
| The tab's own buffer, applied when the dialog closes with OK; the plugin marks the database modified | As `rules-central` 5.1, with its open point about Escape [M critic `rules-central-security-and-fit` M1] |
| The API guard for the group and for any entry carrying `KPG.Kind`; no route makes a string beginning `KPG.` | Otherwise a client could plant a withdrawal and close another client's access |
| Withdrawals and their keys, for as long as the database lives | Growth: one small record per change on the tab. The key keeps the file at KDBX 4.1 as today (`EVALUATION.md` question 5); the probe's results were 4.0 without a key and 4.1 with one [M here] |
| Authentication of grants | Section 5.3 |
| A grant found in the recycle bin does not count, and the plugin's next write turns it into a withdrawal | Recycling a grant by hand took it out of force [M here]; a KeePassXC merge can bring another copy's bin back as an ordinary group [M `ground-keepassxc.md`, as `EVALUATION.md` 2.2 has it], hence the withdrawal |

The plugin may remove the grants of a node that is gone: a grant removed can only narrow. It never
removes a withdrawal.

### 5.5 Merges of the store, measured

15 scenarios, both directions, KeePassLib 2.62's five methods (150 cells), and 17 scenarios, both
directions, `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0 (136 cells; the four releases gave the
same verdict in every cell) [M here]:

| Scenario | Result |
| --- | --- |
| 01: grants added in both copies | the union, every cell |
| 02, 17: A takes a grant away; B changes the governed entry later | the grant stays away, every cell; KeePassXC brought the deleted grant entry back and it stayed cut |
| 03: both copies take a different letter from one row | neither letter, every cell |
| 04: A takes a row away; B, not knowing, adds a letter to it | nothing from that row, every cell |
| 05: A takes a grant away and grants anew; B is a copy from before | the new grant only, every cell |
| 06, 16: the withdrawal's entry deleted with no record, then a stale copy | section 4.3 |
| 07: a foreign file plants a grant | counts for nothing, every cell |
| 08: a foreign file plants a withdrawal | the row is closed, every cell |
| 09 to 14: moves and a deletion of governed nodes | section 4.2 |
| 15: A takes the group's grant away; B changed an entry in it later | closed, every cell |

In no cell of scenarios 01 to 05, 07, 15 and 17 did `agent` hold more than the operator's last
decision, or more than both decisions allow where two were made apart.

## 6. The grant editor, the API and the documentation

**The table on the KPG tab.** One line per profile, with two masks: what arrives from the groups
above, in gray and not editable, and what this node adds, chosen from the same picker as today. A
profile can have both, which today's table cannot show (one row per profile, the nearer replacing
the other).

| Today | In this model |
| --- | --- |
| The check box "Refuse any profile not named here" | Gone |
| An inherited row struck through because a seal stops it | Gone. A line through the mask stays for a profile the database no longer names |
| A profile just picked arrives with `........` | A line with nothing ticked is no rule and is not kept |
| Clicking an inherited row's mask writes a rule of this node's own that replaces it | It opens the picker for what this node adds. Nothing here can lower the gray mask, and its tooltip names the group that grants it and the two ways out: change the grant there, or move this node |
| "Discard the unreadable rules", and the tab locked until then | Gone. Records that do not verify are counted on the Profiles page |
| OK keeps a rule, Cancel discards it | Kept, through the tab's own buffer |
| A revision opened from History shows the rules that version had | It shows the node's rules; a revision has none of its own |
| Restoring a revision brings its rule back | No longer |

One addition worth its room: on the root group, a line that a grant here reaches every group and
entry there is or will be, including whatever a merge leaves at the root.

**The API.** Two changes, both to routes 0.1.0 serves:

- `listGroups` and `listEntries` are "anchored at the root, so a client that may not list there is
  told nothing" (`Spec/openapi.yaml:714-718`, `:1105-1109` [R plugin]). With grants low in the tree
  and no narrowing, `L` at the root would be `L` on everything for ever, so no sensible database
  grants it, and a program then has no way to find its groups. The sweeps have to answer what the
  caller may list wherever it sits, or `whoami` has to name the nodes its grants are on.
- The two routes that tell a program what it may do at a node answer one mask per profile. With
  switches bound to their row (section 1.2), one mask can overstate: `.RU....P` where `R` does not
  reach protected values. The answer becomes a list of masks per profile, one per row on the path.

No route moves a node (`API.md` §"Moving an entry or a group between parents" [R plugin]), so a
program cannot change the policy by rearranging the tree.

**The documentation.** `docs/access-control.md`: "Where the rules go" states that rules add up;
"Inherited rules" loses the struck-through seal; "Nearest wins" becomes "Rules add up";
"Refusing everything not named" goes, and a section takes its place on keeping something out of
reach by where it is: grant low, never at the root what is not meant for everything; a private
folder is a folder with no grant on it or above it; a node moved under a grant is granted; to take
a node away for good, take the grant away or change the secret, because a merge with a copy from
before a move can put the node back. "A copy of a node carries its rules" becomes "a copy has what
its place gives", and the warning about templates goes. The `D` paragraph loses its last sentence
and keeps its advice. `README.md` lines 113 to 117 and `lifecycle.gif` are redone (grant one
folder; the other is simply not granted), and `group-access.png` and `entry-access.png` are
retaken without the check box. `DESIGN.md` and `API.md` record the reversal with this argument.

## 7. The plugin, in short

| Class | Change |
| --- | --- |
| `AclResolver` | The walk of section 1.2: the first row on the path that grants answers; no refusal ends it. `Above` returns the rows on the path |
| `AclDocument`, `AclDefault`, `AclDocuments` | Go |
| `AclGrant`, `AclVerb`, `VerbMask`, `AclInheritance` | Stay; the inheritance loses its "readable" and "sealed" states |
| `IAclNode` | A row for a profile, in place of a document |
| new `Acl/GrantRecord`, `Bridge/RuleIndex`, `Bridge/RuleStore` | The records, the fold of section 5.2 once per hold, the only writer |
| `GrantSheet`, `GrantPanel`, `GrantTab` | Section 6. Smaller than today |
| `NodeDialog`, `IGrantStore`, `KeePassContract.NodeFormCustomDataField` | The reflection on `m_sdCustomData` goes |
| `AccessCheck`, `VaultHandlers` | The sweeps' anchor; the access routes' answer |
| Tests | The seal, empty mask and nearest-wins tests go (section 3.2 has the counts); the merge pins move to the records |

## 8. Databases made with 0.1.0 that hold narrowings

Rules there are `KPG` items in node custom data. Three facts shape the answer:

- Not reading them widens wherever one narrowed (section 1.4), so "an older shape is not read"
  cannot be the whole answer.
- **A 0.1.0 plugin that opens a converted database finds no rule and grants nothing.** With no
  narrowing in the new model, the old plugin's reading of an empty tree is the closed one. This is
  the part that is simpler than under the other rule designs.
- Whether such a database exists is a question for the project
  (`EVALUATION.md` question 6).

**The conversion**, on a press on the KPG tab, never at open:

1. With today's resolver, work out what each profile has at every node.
2. Every rule that only adds becomes grants on the same node.
3. **Every narrowing is listed, by path, and nothing is converted until each has an answer.** The
   operator chooses per narrowing: move the node out (done in KeePass, then press again); push the
   grant down (the plugin replaces the grant above by grants on the other children, and the page
   states that the group itself and anything added to it later get nothing); or, in the middle
   form, a withheld mark.
4. The grants are written, the old items removed, in one save. The page states what each profile
   reached before and reaches after, by count, and refuses to finish if any profile would reach a
   node it did not reach before.

Pushing a grant down is exact for the nodes below the group. It is not exact for the group itself:
a row on a group reaches the group and everything in it, so a group whose own rule holds a letter
that one of its children must not have loses that letter (`D` on a group with one entry held back
is the case). The page names those.

**A node that still carries the old item after the conversion** (a copy last saved by 0.1.0,
merged in) refuses, there and below, until the operator converts again. That is the one refusal
this design keeps, and it is a check on a stored shape, not support for it. It is weak where it
matters least: KeePassDroid deletes node custom data at a save [M android], and then the node has
what the converted grants give it, which is the operator's last decision in the new model.

## 9. What it does not solve

- A whole file written over another, and a stale copy on its own.
- A node moved under a grant, by the operator, on the phone, or by the later of two moves in a
  merge (section 4.2). In the strict form nothing travels with a node.
- A withdrawal that lost both witnesses in one copy, then a merge that brings the grant back
  (section 4.3).
- A file the operator merges can close access with a planted withdrawal [M here].
- The pass over every entry on each request; the visible group of records in every program.
- An operator who needs exceptions under a broad grant: they get the middle form or today's model,
  not this.

## 10. Verdict

**Is it the simplification it looks like?** For the store, yes, and more than it looks. Against
`rules-linked` it removes the versions, the second witness of every version, both references on
user nodes, the limits, four presses, every refusal state, the hidden field in served entries and
the fourth page, and it turns three failure classes (a rule lost, a copy, a node displaced) from
"detected, then refused or settled" into "cannot widen", measured at 0 of 3,175, 0 of 4,428 and
0 of 8,576. What remains load bearing is small and already needed elsewhere: authenticated
records, and a withdrawal with the two witnesses a revocation has.

It is not free. The narrowing does not disappear; it moves into the tree, and the tree is the
weakest carrier in the file: the later of two moves wins in KeePass's own Synchronize, KeePassDX
puts a moved entry back, KeePassXC brings a deleted entry back. The documentation's first picture
has to change, two routes change, and an entry can no longer hold less than its group.

**For which operator.** One whose programs get a few groups low in the tree and nothing else,
which is what the plugin is described as being for, and who edits on a phone: nothing a phone
program saves or merges can lose a rule in the widening direction, and Keepass2Android's and
KeePassDroid's handling of ordinary entries is measured [M android]. It is the wrong model for an
operator who grants broadly and carves out exceptions, or who relies on a private folder staying
private when it is dragged.

**How I would put it to the project.** As the model, not as an optional strict mode. A mode means
two resolvers, two stores, two sets of documents and tests, and the operator still pays for the
linked machinery. The strict form is also a subset of the middle form: starting with grants only
forecloses nothing, and the withheld mark can be added later as one more record kind if the
answer to the first question below is yes. Going the other way, from `rules-linked` to this, would
change what stored rules mean.

Three questions decide it, and only the project can answer them:

1. Must a database be able to hold a node that a profile must not reach inside a group
   that the profile is granted? If yes for more than a handful that can move: the middle form, or
   stay with `rules-linked`.
2. Must a private folder stay private when it is moved or copied under a granted group, in any
   program? If yes: the middle form's mark on that folder.
3. Is "a removal wins over an addition made without knowledge of it" acceptable for letters, as
   for profiles?

## 11. The probe behind the [M here] marks

- Source: `spikes\storage\probes\grant-only\design\probe\Program.cs`,
  compiled with the .NET SDK's C# compiler for .NET Framework against a copy of
  `.keepass\2.62\KeePass.exe` (SHA-256 beginning `afb89b74ae1b8519f483`) and
  copies of `Acl\AclDocument.cs`, `AclDocuments.cs`, `AclGrant.cs`, `AclInheritance.cs`,
  `AclResolver.cs`, `AclVerb.cs`, `VerbMask.cs` and `Transport\JsonBody.cs` from the worktree at
  `a42e38aa` (under `grant-only\src\`). No window. `keepassxc-cli` from the archives the `keepassxc`
  ground report holds was the only other program started.
- Outputs under `spikes\storage\probes\grant-only\design\out\`: `model.txt`, `keepass.txt`,
  `xc-2.4.2.txt`, `xc-2.6.6.txt`, `xc-2.7.12.txt`, `xc-2.8.0.txt`, `xc-touched.txt`. Databases
  under `spikes\storage\probes\grant-only\design\work\`.
- **The model**: 400 generated trees of 30 nodes, three profiles, rules on about three nodes in
  ten, a seal on 15 in 100 of those and an empty mask in 12 in 100 rows, seed 20261115. An answer
  is one (node, profile, verb, reach), 6 verbs by 3 reaches. The numbers describe that generator,
  not an existing database; what carries over is which counts are zero.
- **The store**: one base (G granted, X and S/S1 in it, Y with a grant of its own, H private with
  Q, a recycle bin, the store group), copies A and B changed apart, `PwDatabase.MergeIn` under
  Synchronize, Overwrite if newer, Overwrite existing, Keep existing and Create new IDs, and
  `keepassxc-cli merge -s`, each in both directions, each result saved and read back with
  KeePassLib 2.62. Scenarios 16 and 17 use Argon2d at 128 MiB and 14 passes, so that a load takes
  more than a second (a merge took 2.4 s). `xc-touched.txt` is the extra run of section 4.3.
- **Copies**: `PwEntry.Duplicate`, `PwGroup.Duplicate`, an import of the base into itself with
  Create new IDs, then `DeleteDuplicateEntries`, in memory.
- What the probe is not: the plugin, KeePass's window, KeePassXC's window, any Android program.
  It writes the check as a plain store identifier compared for equality, not an HMAC. It models
  the middle form in the resolver only, with no store.

## 12. Measurements still needed

Most important first.

1. **An existing database**: how many rules narrow, on which nodes, and whether those nodes
   can move. A count the KPG tab or a console script over a copy can print, run by the operator.
2. **Moves on the phone**: Keepass2Android's Move of an entry across a granted group's border,
   merged in both directions with a KeePass copy that moved the same entry, and that moved another;
   its copy command, if it has one, and where the copy lands. On the emulator.
3. **The records on the phone**: grant and withdrawal entries through Keepass2Android and
   KeePassDroid saves, edits and deletions, and the `KPG.Withdrawn.` key through both. The carriers
   are measured [M android]; these exact shapes are not.
4. **A KeePassLib probe of the real check**: an HMAC under a key in the authority's document,
   through Recreate, with a foreign file, an older copy of the same database, and three copies
   merged in every order with removals and new grants between them.
5. **When KeePassXC removes the key**: the conditions behind the one cell of section 4.3, in its
   window merge and its reload of a changed file, for 2.4.2 to 2.8.0.
6. **The dialogs in a real KeePass 2.62** (the Live tier): the buffer applied at OK on both
   dialogs, Cancel and Escape, Add Entry and Add Group, the modified mark, and a rule write while a
   save is running.
7. **The pass on KeePass's thread**, tuned, for 1,000, 10,000 and 50,000 entries.
8. **The two route changes** as host tests: a sweep by a client with no grant at the root, and the
   list answer of the access routes.
9. **KeePassDX and kdbxweb merges** of the records: KeePassDX applies arriving deletions and keeps
   no record, and kdbxweb removes whatever a deletion record names; both remove grants the plugin
   deleted, which narrows, but neither is run.
10. **The conversion** over generated 0.1.0 databases: that no profile reaches a node after it
    that it did not reach before, for every kind of narrowing.
