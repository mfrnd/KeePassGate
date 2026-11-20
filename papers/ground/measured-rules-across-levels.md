# Measured: closing rules changed apart across levels (S34), two forms

The run of the forms owed by rules per profile (`measured-rules-owed-forms.md`) found that rules
changed apart at two levels still end with more than either copy gave: across nodes, through a
group between the root and the node, through a group's switch, and in capabilities as well as in
letters. The design `S34-DESIGN.md` (in the session's scratchpad, in no repository) proposes two
forms that close that class, and its section 8 specifies the run that decides between them. This
report is that run, made on 2026-11-20 in the family `spikes\storage\probes\store-as-a-whole\` on
the branch `measure/rules-model`, as `run.ps1 -S34`, with its record in `recorded-s34\`. The
family's `RUN.md`, section "The two forms of S34", has the commands, both runs' times and a second
run's value of every figure. Nothing ran in a window, on a phone or in an emulator; every file was
made and changed by KeePassLib 2.62 and merged by KeePassLib or `keepassxc-cli`.

**How to read it.** As in `measured-rules-owed-forms.md`. A **row** is a profile's rule at a node,
`N/pf`; the **switch** is the node's "Refuse any profile not named here"; a node **lets a profile
through** where it has no row for it, or a version taking the rule off among its versions, and its
switch inherits. **Form B** is the design's form by what arrived (`seen`), **form A** its form by
identity (`above`); **the store as decided** is neither. A merge is **beyond both copies** where
the merged file gives at a node what neither copy gave just before it, held **by letters** and **by
capabilities** (a verb alone, or a verb that reads or writes with a switch of the same rule); **by
capabilities only** is a verb with a switch that neither copy gave while each letter came from a
copy. **The cost** of a form is counted on the same steps as the store as decided, by
capabilities, in the histories where the store as decided had no merge beyond both copies: a copy
giving a profile **less** at a node than the store as decided, of which a profile given
**nothing** where the store as decided gives something, and a form giving **more**; the figure in
parentheses is those still so at the end of the history. Letters are written per profile in the
order of the mask `CRUSDLAP`, `pf:rla`.

## 1. What was implemented

**Both forms, from the design's text, as switches beside the store as decided.** `DEP` is none
(the store as decided, every output as recorded), `seen` (form B) or `above` (form A). Each line
added carries "ADDED (S34 forms)". The designer's scratch code (`s34-check`) was read as a hint
and not copied; where it and the text differ, the text was followed (section 12).

**What a version that lets through holds.** A row's version that takes the rule off holds a
record: under form B the letters that arrived for the profile when it was written, or `deny`
where the node's own switch refused; under form A the identifiers of the versions the walk read
above the node for the profile and of the node's own switch, or `deny`. A version of the switch
that inherits holds a record for each profile: the letters that arrived, or the identifiers above.
`meet` keeps the records of every standing version. On files the texts are those of the design's
2.2 and 2.3 with the probe's letters: `off:` and the letters, `off:deny`, `sw:inherit pf=rw pg=r`;
under form A `off:@` and the identifiers, and `sw:inherit pf=@...`, kept in `doc` where the
design puts them in a member `above` (the probe's own deviation; the summary item restates `doc`
whole, so the reading has the same identifiers).

**The reading.** Where a node lets a profile through, form B cuts what passes to the letters of
each record (`deny` holds none); form A lets it pass only where each record names exactly the
versions standing above now and, for a row's version, the node's own switch. The walk above reads,
at each node up the path, the profile's row and, where the row does not answer by itself, the
node's switch, until a node answers or the root is passed.

**The writes** (the design's 2.2, rules 1 to 4, and section 8). A rule taken off records what
arrives, after the node's switch versions holding another record for the profile are written
anew, or `deny` where the switch refuses and the removal is taken off for real. The switch turned
to inherit records for every profile, and every version at the node that takes a rule off is
written anew. `lift`, the press that settles, writes anew each version at the node that cuts. The
write above follows every act that wrote at a node with nodes below (a row, a table, a version
removed, the switch, `lift`): each node below, the nearer first, that lets the profile through
after the act, where what arrives (form B) or the versions read above (form A) differ before and
after the act, and every version it is cut by held everything that arrived before (form B) or
named what stood before (form A), has those versions written anew by itself, naming the version
it renews and everything that version names. Under form A a switch written anew is followed by
every version at its node that named it. N can be moved between G and the root (`nmove`), the
later move winning a merge.

**Where it runs.** In the model, `store_runs.py --dep` plays the store as decided and the form side
by side on the draws of `beyond`; on files, `s34.py` plays each sequence under the form and under
the store as decided. Every file is read by `read.py`, with the model's read of the same file
compared at every step: no step of any part disagreed, entry by entry or in the read.

## 2. What was run

| Part | What it is | Cells |
| --- | --- | --- |
| `beyond-*` | the draws of `beyond` (seed 34, 4 to 10 steps) in the five configurations of `recorded-owed\`, under each form, a removed row taken off for real; and with two profiles, read and write, the removal written as decided and as `again`, and the controls with no form | 200,000 histories each, 16 parts |
| `long-2-*` | the same with 8 to 14 steps, two profiles, read and write | 200,000 each |
| `moves-3-*` | three levels, with N moved between G and the root in the alphabet | 200,000 each |
| `combine` | a row's versions with recorded sets, held to what all allow by capabilities | 67,502,592 pairs, 200,000 triples |
| `subtree-*` | a write above 20, 200 and 2,000 nodes that let through | 3 each |
| `search-acts-*` | the model's search with the acts on a row and on the switch, under each form | 24 configurations of 20,000, and the exhaustive part |
| `hand-*` | H1 to H4, H9, H10, H11 and W3 on files, under the store as decided, the control and the form | 8 each |
| `cells4-*`, `cells3-*` | O1 to O8 and L1 to L10 on files, every merge both ways by KeePassLib's five methods and four `keepassxc-cli` releases | 144 and 180 each |
| `switch-*` | W1 to W8 on files, every merge both ways, the removal written as decided, as `again` and taken off | 432 each |
| `moves-*` | H7 (row 9 of §639), H5, H13 and H16 on files | 4 each |
| `afters-*` | a version below that lets through, under a rule above edited 400 times in a copy and 200 times apart in two copies | 600 edits each |

Every part ran twice; the second run printed every output of the first line for line, but the
running times, the read's milliseconds and the file's bytes, 37 files. A run took 50 to 60
minutes with four parts at once. A first run 1 was stopped after its first part, to print every
failure of a form by its class and the time of `subtree` where the comparison masks it. **With both forms off, every earlier output prints as recorded**: after the
addition, 15 parts of the earlier records (`beyond`, `beyond-whole`, `beyond-again`, `b-merges`,
`switch`, `same`, `b-random-rows`, `apart`, `states`, `dels` and `growth` of `recorded-rows\`,
`b-merges` and `b-random` of `recorded-decided\`, `store-scenarios` and `store-section6` of
`recorded\`) and the 13 of `recorded-owed\` printed their records line for line. The store as
decided, played beside each form, drew the record's counts in every configuration (67, 33, 129,
44, 44), and the controls with no form drew 68 and 69.

## 3. Run 1: `beyond` in every configuration

| Configuration, 200,000 histories | Store as decided: beyond (of them by capabilities only) | Form B: beyond; cost less (end); nothing (end); more (end) | Form A: beyond; cost less (end); nothing (end); more (end) |
| --- | --- | --- | --- |
| two profiles, read and write | 67 (0) | **0**; 181 (161); 23 (21); 19 (18) | **0**; 263 (241); 263 (241); 19 (18) |
| four profiles, read and write | 33 (0) | **0**; 108 (96); 7 (7); 9 (9) | **0**; 128 (117); 128 (117); 9 (9) |
| two profiles, the full mask | 129 (8) | **0**; 327 (288); 71 (61); 3 (3) | **0**; 424 (377); 424 (377); 3 (3) |
| four profiles, the full mask | 44 (1) | **0**; 129 (124); 28 (28); 0 | **0**; 143 (135); 142 (135); 0 |
| four profiles, the full mask, the root, G and N | 44 (1), in 56 merges | **0**; 115 (107); 25 (23); 0 | **0**; 131 (123); 131 (122); 0 |

**With the removal written otherwise** (two profiles, read and write): written as decided, form B
0 beyond, 177 (157) less, 19 (17) nothing, 0 more, and form A 0, 259 (237), 259 (237), 0; written
as `again`, form B 0, 182 (162), 24 (22), 4 (4), and form A 0, 264 (242), 264 (242), 4 (4). With
no form, `again` draws 68 beyond both and the removal taken off always 69, the record's counts.

**Every count to beat goes to none under both forms, by letters and by capabilities.** The cost
of form B where a removed row is written as decided, 177 (157), is the design's scratch count
exactly, and so are 115 (107) for B and 131 (123) for A at three levels.

## 4. Run 2: the sequences on files

**O6, O7, L8, L9 and L10, every merge both ways.** The store as decided ends beyond both copies in
16 of 16 cells of each (O6 by capabilities only). Under both forms: **0 of 16 in each**, by
letters and by capabilities. What form B ends with is what the design expects: O6 and O7 give pf
`a` alone at N, a switch with no verb, so nothing; L8 gives `rl` at G and N; L9 `rl` at N; L10
nothing for pf at N. Form A gives pf nothing at N (and at G in L8).

**The other cells** (O1 to O5, O8, L1 to L7, and every import cell): none beyond both copies under
either form. Under form B each of them ends as the store as decided's cell, 112 of 112 of the O
cells and 132 of 132 of the L cells with the imports; the cells narrower than the store as decided
are exactly the 80 cells the form closes. Under form A the same, and L3 is narrower in 16 more
cells: N's version names G's version, which copy 1 replaced apart, and A refuses pf at N where B
and the store as decided give `r`.

**W1 to W8**, every merge both ways, with the removal written as decided, as `again` and taken off
for real, under each form beside the store as decided's cell (432 cells a form):

| | Beyond both copies | Narrower than the store as decided | Wider than the store as decided |
| --- | --- | --- | --- |
| Store as decided (written as decided) | W8 16 | | |
| Form B, written as decided / `again` / taken off | 0 / 0 / 0 | W8 16 / 16 / 16 | 0 / 0 / **W6 16 and its import cell** |
| Form A, the same | 0 / 0 / 0 | W8 16 / 16 / 16 | 0 / 0 / **W6 16 and its import cell** |

**W6 differs by design (H9).** With the removal taken off for real, copy 0 writes `off:deny`; copy
1, which holds that version, turns the switch off, and the switch's write renews it with what
arrives. So copy 1 gives pf `rw` before the merge, and the merged file gives `rw`, within what copy
1 gave, where the store as decided keeps the empty rule and gives nothing. Every other W cell ends
as the store as decided's cell, W3 among them with nothing for pf.

**The hand histories on files**, each under the store as decided, the control (no form, every
removal taken off) and the form:

| History | Store as decided | Form B | Form A |
| --- | --- | --- | --- |
| H1 S34 with a row | beyond: N pf `rw` | `r`, what copy 0 gave | nothing, what copy 1 gave |
| H2 S34 with the switch | beyond: `rw` | `r` | nothing |
| H3 a profile N never named | beyond: pg `rw` | pg `r` | pg nothing |
| H4 a narrowing above in a single direction, widened again apart | beyond: `r` | **beyond: `r`** | nothing |
| H9 removed for real, then opened in the same copy (no merge) | nothing | `rw`, wider than decided by design | `rw` |
| H10 removed under a refusing switch, the switch off apart, the root widened | nothing | nothing | nothing |
| H11 the widening copy never saw the node closed | `rw`, within | `r`, narrower | nothing |
| W3 S34 at a single node | nothing | nothing | nothing |

The control (no form) is beyond both in H1 to H4 and W3, and wider than the store as decided in
H9 and H10.

## 5. Run 3: the longer search, H4 and H5

| 200,000 histories | Store as decided | Form B | Form A |
| --- | --- | --- | --- |
| two levels, 8 to 14 steps | 294 | **5**, each of the shape H4; cost 721 (564), nothing 218 (197), more 117 (98) | **0**; cost 1,233 (1,020), nothing 1,232 (1,019), more 116 (97) |
| three levels with the move, 4 to 10 steps | 134 | **80**: 79 with no version that lets through on the walk (H7), and 1 of the shape H4 through G's switch; cost 180 (157), nothing 36 (33), more 5 (5) | **79**, the same 79 histories as form B (H7); cost 315 (287), nothing 314 (286), more 5 (5) |

**The five under form B, shortest each (7 steps), and all at N:**

1. `row 0 N pg (empty) ; merge kp_other 1 0 ; switch 1 N deny ; row 0 N pg off ; merge kp_other 0 1 ; switch 1 N inherit ; merge xc 0 1`: pg `r`.
2. `row 1 root pg (empty) ; switch 0 N deny ; merge kp_other 1 0 ; switch 0 N inherit ; merge xc 0 1 ; row 1 root pg rw ; merge kp_other 0 1`: pg `r`.
3. `row 1 root pg off ; row 0 N pg (empty) ; merge kp_other 1 0 ; row 0 N pg off ; merge kp_other 0 1 ; row 1 root pg r ; merge kp_other 1 0`: pg `r`.
4. `switch 0 N deny ; merge kp_other 1 0 ; switch 1 N inherit ; row 0 root pg off ; merge kp_sync 1 0 ; row 0 root pg rw ; merge kp_other 1 0`: pg `r`.
5. `switch 0 N deny ; row 1 root pf (empty) ; merge kp_other 1 0 ; switch 0 N inherit ; merge kp_sync 0 1 ; row 1 root pf rw ; merge xc 1 0`: pf `r`.

**The sixth, with the move** (7 steps, at G): `switch 1 G deny ; merge kp_other 0 1 ; row 1 root pg
(empty) ; switch 0 G inherit ; merge kp_other 0 1 ; row 1 root pg rw ; merge kp_sync 0 1`: G gives
pg `r`.

**Each ends at no more than its own version's writer saw, and only at more than both copies gave
just before the merge.** In all six, what the node gives lies within every record of the versions
that let it through (`r`, recorded when the rule was taken off or the switch turned to inherit),
so the node gives what its own copy once showed there; it is more than both copies gave because a
narrowing above reached the copy in a single direction and was widened again apart (H4; through
the node's own switch in the first, the fifth and the sixth). None ends at more than a record holds.
Form A closes all six. **The 79 with the move are H7** under both forms: in each the node that gains
the letter holds no version that lets the profile through, on its walk; the shortest is 4 steps, N
moved apart from a change above (`nmove 0 N root ; row 1 G pf r ; row 1 root pf rw ; merge kp_sync
1 0`).

## 6. Run 4: the cost

Section 3 has the counts; per 200,000 histories with 4 to 10 steps, **form B makes a copy give
less in 108 to 327 histories where the store as decided stayed within, of which a profile gets
nothing in 7 to 71; form A in 128 to 424, in all of them but a single history (four profiles,
the full mask) a profile given nothing**. At 8 to 14 steps it is 721 against 1,233. Both forms give
more than the store as decided in 0 to 19 histories (117 and 116 at 8 to 14 steps), each of the
shape H9, none with a merge beyond both.

**The typical shortest histories:**

- **Less, form B, and nothing, form A** (H11, 4 steps): `row 0 N pf (empty) ; row 1 root pf rw ;
  row 0 N pf off ; merge xc 1 0`. Copy 0 takes the rule off at the root's `r`; copy 1, which never
  saw the node closed, widens the root. B gives `r` until settled, A nothing; the store as decided
  gives `rw`.
- **Nothing, form B** (4 steps, the full mask): `row 0 N pf a ; row 1 root pf cula ; row 0 N pf off
  ; merge kp_sync 1 0`: the version recorded the root's `r`, `cula` arrives, nothing is left.
- **More, both forms** (H9, 4 steps, a single copy): `switch 0 N deny ; row 0 N pf (empty) ; row 0
  N pf off ; switch 0 N inherit`: the switch turned off renews `off:deny`, and the profile gets what
  arrives, where the store as decided keeps an empty rule.
- **Nothing, form A, in a single copy** (with the move, 4 steps): `row 0 G pf r ; row 0 N pf (empty)
  ; row 0 N pf off ; nmove 0 N root`: N's version names G's row, the move puts the root above, and
  A refuses; B gives the root's letters cut to `r` (H13).

## 7. Run 5: the single node with the recorded mask

**`combine`**: every pair of a row's versions, among each set of the eight letters granted, each
set recorded by a rule taken off, and `off:deny` (513 values), every set arriving and either
switch: **67,502,592 of 67,502,592 combinations give exactly what both versions allow, by
capabilities**, neither more nor less; the recorded sets cut what passes in 18,275,970 of them;
200,000 random triples give what all three allow. **On files**, every cell of O1 to O5, O8 and L1
to L7 under form B ends as the store as decided's cell, and none at a single node is wider under
either form; W1 to W8 have none beyond both copies (section 4). **Nothing at a single node is newly
wider**, but by the design's H9 (W6 with a real taking off), which is no merge.

## 8. Run 6: what neither form touches

| On files, under the store as decided and under each form | Store as decided | Form B | Form A |
| --- | --- | --- | --- |
| H7 (§639 row 9): a node with no rule, moved apart from a change above | beyond: N pf `rw` | the same | the same |
| H5: a rule taken off, then a move in a single direction and a widening apart | beyond: `rw` | **beyond: `rw`** | nothing |
| H13: a node whose rule was once taken off, moved into a wider group in a single copy | `rw` | `r`, narrower | nothing |
| H16: a name put back on the list apart from a row widened | c1 reaches pg `rw` at the root, beyond both copies | the same | the same |

**Neither form touches H7 and H16**, as the design has it: H7 has no version that lets through,
and H16 is a name on the list, which the rules' reading does not see. Form A closes H5 and refuses
in H13; form B leaves H5 and cuts in H13.

## 9. Run 7: what a write above costs

| Nodes that let pf through under the root, the root's row widened in a copy | Form B | Form A |
| --- | --- | --- |
| 20 | 21 versions, 22 deletion records, 20 of 20 nodes follow | the same |
| 200 | 201 versions, 202 deletion records, 200 of 200 | the same |
| 2,000 | 2,001 versions, 2,002 deletion records, 2,000 of 2,000; 113 s in the model | the same; 139 s in the model |

A single write above adds a version and a deletion record for each node below that lets the
profile through, beside its own version and the replaced summary entry. **The `after` lists grow
by a renewal as by an edit**: 400 edits of the root's row in a copy renewed N's version 400 times,
and its `after` held 401 identifiers (form B: KPG.Data 13,361 characters, its summary item 13,367;
form A 13,393 and 13,399, the names counted), with 1,203 deletion records, three an edit; 200
edits apart in two copies, a round of an edit in each copy and Synchronize both ways, renewed it
once a round, 101 identifiers. At 33 characters an identifier, a version below passes the plugin's
ceiling of 262,144 characters at about 7,940 renewals, with no edit of its own (§643).

## 10. Run 8: the model's own search

`store-search-acts` under each form (the acts on a row and on the switch, a removal taken off for
real): **every line is the record's but the header**, under form B and under form A, in all 24
configurations and the exhaustive part. The search holds a file to its oracle read in the same
form, so it cannot see a merge beyond both copies; nothing else it measures changed. Not run:
`store-search` and `store-search-noauth` under a form, three copies, `lift` in the alphabet.

## 11. What decides

**Form B.** Against the counts to beat: 67, 33, 129 (8), 44 (1), 44 (1), and 68 and 69, all **0**,
by letters and by capabilities; on files O6, O7, L8, L9 and L10 0 of 16 each; W1 to W8 0 in every
way of writing a removal. **What still ends wider**: the H4 class, 5 of 200,000 at 8 to 14 steps
and 1 with the move, each within what its own version recorded, wider only than both copies just
before the merge; H5 on files; and, untouched, H7 and H16. Measured against the store as decided,
it is wider only by H9. Cost: 108 to 327 histories per 200,000 newly narrower, of which 7 to 71 a
profile given nothing.

**Form A.** The same zeros, and the H4 class closed (0 at 8 to 14 steps, H5 closed on files).
**What still ends wider**: H7 (79, the same histories as form B) and H16. Cost: 128 to 424 per
200,000, all but a single history a refusal, and 1,233 at 8 to 14 steps; it refuses after a move in a single copy
and after any change above made apart (L3 on files).

**Which form the numbers support.** If the bar is none beyond both copies in every search, only
form A reaches it, at the cost of refusals: 263 against 23 histories where a profile gets nothing,
two profiles; 424 against 71 with the full mask. If the H4 class may stay (a node gives at most what
its own copy showed there), form B closes everything else with fewer histories narrower and few
refusals, as the design recommends. Neither form closes H7 or H16, and both open H9 by design. The
numbers confirm the design's every scratch count that was repeated here: 177 (157), 721 (564), 115
(107), 131 (123), and the 5 of the H4 shape.

**What stays open.** H4 and H5 under form B; H7 and H16 under both; H9 wider than the store as
decided; the cost of a write above at 2,000 nodes and the `after` growth from renewals; the dialog
cases H6, H8 and H17, which a model that composes at the write cannot run; whether Seal again is an
act above.

## 12. Where the design's text is unclear, wrong, or differs from its scratch code

- **The scratch copy of the project's model renews below only after a row act at the root**
  (`node == 'root' and kind in ('rule', 'row')`); the text has every act above at any node (a
  switch, a version removed, a limit removed, the group G). The text was followed; the counts of
  the scratch copy over two levels were repeated all the same (177 (157)).
- **The scratch copy's form A does not name the node's own switch**; the design states it, and
  its text names the switch. The text was followed; with the switch named the H4 cases through the
  node's own switch are closed (section 5).
- **The small model keeps an empty set for each profile in a switch's record**; the text names only
  profiles something arrived for. The two read alike.
- **Unclear: whether Seal again is an act above.** It writes an empty rule or the switch refusing
  at a node with nodes below, which changes what arrives there; the text does not state whether the
  nodes below are renewed. Not renewed here.
- **Unclear: the X under a refusing switch.** Section 3 has it always take the rule off (`off:deny`);
  section 8 point 3 writes the mark only where `ROWS` is `off` or at the second removal of `again`.
  All three ways were run; the counts to beat are reached under each.
- **Unclear under form A: "what arrives differs"** has no letters to compare; section 8 reads it as
  the versions read above, which is what was run, so form A renews below at every act above, also
  where no letter changes.
- **The member `above`**: the probe keeps the identifiers in `doc`, its own deviation.
- **Not wrong, but stated narrower than measured**: the design's "no sequence ends narrower than
  its decided form, W8 aside" holds for form B on files; under form A L3 also ends narrower (16 of
  16), from a change above made apart.

## 13. Every history that still ends wider

1. **H4 under form B** (model, 5 of 200,000 at 8 to 14 steps, shortest 7 steps, section 5): wider
   than both copies just before the merge, within what the node's own version recorded.
2. **H4 through G's switch under form B** (model, 1 of 200,000 with the move, 7 steps): the same.
3. **H5 under form B** (files, a single sequence): `nmove 0 N root ; row 0 root pf rw ; row 0 G pf
   r ; row 0 N pf (empty) ; merge kp_sync 1 0 ; row 0 N pf off ; nmove 1 N G ; merge kp_sync 0 1 ;
   row 1 G pf rw ; merge kp_sync 1 0`: N gives `rw`, within what its version recorded.
4. **H7 under both forms** (model, 79 of 200,000 with the move; files): `nmove 0 N root ; row 1 G
   pf r ; row 1 root pf rw ; merge kp_sync 1 0`, beyond both copies, no version on the walk.
5. **H16 under both forms** (files): a name put back on the list apart from a row widened, beyond
   both copies at the client's reach.
6. **H9 under both forms** (model 0 to 19 per 200,000, files W6 16 cells and its import cell):
   wider than the store as decided, within both copies; a press in a copy that holds the version.

## 14. What was not run, and why

- The forms in `b-merges`, `apart`, `same`, `states`, `b-deletions`, `b-clear`, `b-history`,
  `b-random` and `b-random-rows`; three copies; `lift` in the alphabet; the longer search over three
  levels: left for time, after the parts the coordinator named.
- `store-search` and `store-search-noauth` under a form; `store-search-acts` showed nothing new.
- The dialog cases H6, H8 and H17: the model composes every act at its write.
- A tree of more than three levels on files; the subtree of 2,000 nodes in the model only.
- The tab, a second KeePass with the plugin, a phone; the slow key derivation.
