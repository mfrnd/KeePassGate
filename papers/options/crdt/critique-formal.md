# Critique of option `crdt`: the mathematics and the model

Key: `crdt-formal`. Written 2026-11-15 against `option-crdt.md` as it stood at 20:03 and the ground
reports of this directory. The lens: the data types, the read rule as a function, and the design's
own search. I wrote a model of my own from the design's text alone, without reading the design's
`crdt_model.py` beyond one search of it for `bin`, `recycle`, `location` and `64` (none found), and
searched histories the design's search did not reach.

## 0. How statements are marked

- **[model here]**: a result of my model, `crdt-model\critic\crdt_formal.py`. It shows what the
  design's rules do over the transport as I modeled it. It measures no program.
- **[design §n]**: the design's own text, section `n`, quoted or paraphrased.
- **[M report]**, **[R report]**: measured or read in the report named (`keepass`, `keepassxc`,
  `android`, `inventory`, `measured-android`), which names its own basis.
- **[A]**: assumed. Neither run nor read.

Nothing was run against KeePass, KeePassXC or an Android program. No window was shown.

## 1. Verdict

**Viable with changes.** The data types are sound as mathematics: every value function is a
function of the set of elements, and the document lattice is closed, commutative, associative,
idempotent and monotone through the walk [model here, section 2]. The identity of an act (a fresh
nonce inside content that the identifier digests) is the right answer to the replica problem: a
restored backup writing again, two gate installations from one copied directory, and a repeated
nonce all come out harmless [model here; by construction].

What breaks is not the types but **the state the read applies them to**. The design treats the
file as a grow-only set of elements, and then reads it as something else: an element counts only
where it sits (the store group, and for two kinds the recycle bin), deletion records count by the
times KeePass keeps or drops them, and records below a 64-height window are assumed to be
ancestors. Each of those three makes the read **non-monotone in evidence**: a file can hold more
narrowing information than before and answer wider. The first has counterexamples of four and five
steps through programs in scope, one of which undoes a revocation; the third has one
that 65 ordinary writes make; the second needs a script as well, once the first is changed.

| # | Severity | Problem | Shortest history |
| --- | --- | --- | --- |
| P1 | serious | Where an element sits decides whether it counts, and Delete (to the recycle bin) or Move changes that in every program without a record. A narrowing is undone silently in both files by KeePass's own Synchronize; a revocation and a retirement are undone through KeePassXC; one deletion widens; a detected loss heals wider | 5 steps (CE1), 4 steps (CE3) |
| P2 | serious | The 64-height window assumes a record far below is an ancestor. A lagging copy's narrowing, lost with both records, is not detected, against the design's own claim | 70 steps, 65 of them ordinary writes (CE5) |
| P3 | serious, shared by every option inside the file | A whole file written over a newer one: no function of the file can see it, so E3 fails under KeePassDroid's save over a changed file. The change that helps is a replica outside the file, which needs a decision of the project | 2 steps (stated by the design) |
| P4 | serious if the security critic's S1 is taken | A new store key at Recreate splits the state: a concurrent narrowing or profile removal under the old key is dropped rather than met | 4 and 5 steps (CE11, CE12) |
| P5 | minor | T3 (an unchanged save, a title edit) is not harmless: it makes KeePass drop the gate's pruning records, which defeats the detection of 6.2 row 5 when the newest elements went without a trace | 6 steps (CE6) |
| P6 | minor | Two files holding the same elements can answer differently, because the value depends on records and locations; after P1's change only in the refusing direction | 3 steps (CE8) |
| P7 | minor | 56 of 128 meets of two documents are limits no `AclDocument` can state, and "every join moves toward less access" is not what the model checks | (laws) |
| P8 | minor | Two bytes of height in the identifier wrap; moot if P2's change drops heights | (arithmetic) |

None is fatal **with the changes named**. P3 is fatal to the hard requirement for this option and
for every other option that keeps its data only inside the file, unless the project decides that a
copy of the gate's last head may live outside it (P3 in section 3). That ruling does not separate the
options; it decides whether any of them can pass.

## 2. What holds, checked

| Claim of the design | What I did | Result |
| --- | --- | --- |
| The meet table of 4.3 is closed, commutative, associative, idempotent, at most each argument for every input | 200,000 random triples of the 512 per-profile functions over 8 words, with random inputs; also: monotone in the input, a narrower parent never widens the child | 0 violations [model here] |
| The meet is exactly "what both give" | the same triples, pointwise against the intersection of grants (switches without a verb grant nothing) | equal [model here] |
| The remove-wins set and the registers are functions of the union | 20,000 random triples of states | 0 violations [model here] |
| An identifier never comes back; a dot needs no replica | a backup restored over the file and written to again with two of six acts, then merged back by each of the five merges, both directions: 3,600 histories | no widening, no revocation undone [model here, `probe_restore.py`] |
| Two gate installations from one copied KeePass directory | nothing in the design is per installation: no counter, no replica name, no host state | holds by construction. A repeated nonce changes nothing either: the identifier digests the whole content, so two acts collide only when they are the same act |
| Merges alone never stop the gate | 1,500 histories of gate acts and merges by all five programs, three and four files, for each of three reads (as written, with the window, with P1's change) | no file ever incomplete [model here, `probe_liveness.py`] |
| A concurrent removal beats a re-addition that did not see it; two additions on their own both survive | the random search, whose gate acts remove and add names in every file, and the union check above | no failure that did not go through P1 [model here] |

So the formulation is right where the design says it is a CRDT. The problems below are all in the
step from "a set of elements" to "what the read takes as the set".

## 3. The problems, each as a history

Notation of the model: files are numbered from 0. Every history starts from one common file
copied to every file: authority `A0`; clients `c0` (profiles `p`, `q`) and `c1` (`p`); the names
`p` and `q` on the list; the root's rule `r` (read and list for `p`), node `N` under the root with
rule `rw` (read, update, list for `p`); one head carrying both rules; both nodes marked. `gate f X`
is the gate of file `f` doing X through the design's write path (section 10 of the design, with
pruning per section 9 and a head per write). `bin`, `move`, `delr`, `del0` and `touch` are a Delete
to the recycle bin, a Move to another group, a deletion for good with a record, one with none, and
a save that moves the modification time without changing content. `sync`, `k2a`, `kpo`, `xc`, `dx`
are KeePass's Synchronize (both files written), Keepass2Android's merge, KeePass's other four
methods, KeePassXC and KeePassDX, each into the first file named. Every history is replayed by
`crdt_formal.py ce`, with every file's value after every step; the output is
`crdt-model\critic\results-critic.txt`.

### P1. Where an element sits decides whether it counts (serious)

**Where.** An element counts only as a direct child of the store group [design §3.1, §5.2 point 1];
the recycle bin is read for refusals and removals only [design §5.2 point 3, §10 step 2]; a version
or a head anywhere else is "ignored and shown", which the design calls failing closed [design
§3.1]. At the same time the design lists "where an entry sits" and "which group is the recycle
bin" among the things it does **not** rely on [design §2, after the table], and its model leaves
both out [design §12.1].

**The fact the design missed.** A deletion that leaves no record is not only a script's. A Delete
with the recycle bin on moves the entry and writes no record in KeePass [R keepass 2.2], KeePassXC
[R keepassxc, `Database.cpp:1031-1069`], Keepass2Android when "No" is answered [M measured-android
2.1] and KeePassDroid every time, with no question [M measured-android 3.1, 3.2]. Move to another
group writes none either, in KeePass [R keepass 2.2] and Keepass2Android [M measured-android 2.1,
step 6]; KeePassXC has a move too [R keepassxc, its `mv` command], with no record [A]. For a head
or a version, both are a deletion without a trace in the design's own terms. The design's sentence
that two such deletions need "a script over pykeepass" and that "KeePass, KeePassXC,
Keepass2Android and KeePassDroid do not" [design §1, weaknesses] is wrong for the register types
and for the head.

**And location is not part of the grow-only set.** It is a register of its own per entry, merged
by a different rule in each program: by `LocationChanged` in Synchronize [R keepass 2.2], not at all
for an entry both copies hold in KeePass's other methods [R keepass 2.2], toward a group named
"Recycle Bin" that is *not* the bin when KeePassXC merges a bin the target does not share [M
keepassxc 10], and toward the arriving copy at equal times in KeePassDX [M measured-android 4.3].
A value that depends on it is not a function of the union of elements.

**CE1: a narrowing undone in both files by KeePass's Synchronize** (5 steps).

1. `gate 0 rule N r`: file 0 narrows `N` to read and list (`v2` naming `v1`, head `H2`; `v1` and
   the old head deleted with records).
2. `gate 1 pair new p`: file 1, a copy the narrowing never reached, pairs a client. Its head
   `H_B` carries `v1`, the version standing there.
3. `bin 0 rule(new)`: in file 0, Delete on `v2`'s entry.
4. `bin 0 head(new)`: Delete on `H2`. File 0 is **incomplete**: no head in force. Detected.
5. `sync 0 1`: KeePass Synchronize.

Outcome, both files [model here]: whole, `N` gives `p` read, update and list. The operator's
narrowing is in both files, in `v2`'s entry and in `H2`, both in the recycle bin, and counts
nowhere. File 0's record of `v1` reached file 1, where `H_B` carries `v1`, so point 4's refusal
does not fire; the old head's record is an ancestor of `H_B`. **A detected loss healed to a wider
value**, which is the design's 6.2 row 1 ("healed by a merge that brings a head covering what is
present") read literally: `H_B` covers what is present, because `v2` is not counted as present.

The steps are the programs in scope: two gates (a second machine, or the PC writing to the
phone's older copy) and a Delete on the phone. Under another reading of 5.2 point 4, where a
binned head's versions still name what they replace, CE1 turns into a refusal (`CE1-otherreading`);
**the same history with Move in place of Delete fails under both readings** (`CE1-move`).

**CE3: a revocation undone** (4 steps; KeePassXC).

1. `gate 0 revoke c0`: the revocation `R`, its witness, head `H2`; `c0`'s record deleted with a record.
2. `move 0 revoked(new)`: `R` moved to another group.
3. `move 0 head(new)`: `H2` moved too. File 0 still refuses `c0` (witness) and is incomplete.
4. `xc 1 0`: a KeePassXC merge of file 0 into file 1, which still has `c0`'s record.

Outcome [model here]: file 1 admits `c0`. KeePassXC copies no record, so no witness; `R` and `H2`
arrive in the other group, where nothing reads them. The design's 6.1 row 1 says a refusal's entry
"moved out of the group" is "still refused: the head carries it, and the witness". The second move
takes the head; the merge does not carry the witness. `CE3-bin` is the same with Delete in place of
Move, where the target does not share the bin (KeePassXC merges no bin [M keepassxc 10]).
`CE4` is the same for a retirement: after Delete authority, the retired authority is served with
all its clients. `CE13` needs only **one Move in each of two copies** (6 steps: a revocation spread
by an import and a KeePassXC merge, a Move of `R` in one copy, a Move of the head in another, a
Keepass2Android merge): no single copy ever lost two carriers.

**CE10: one deletion widens** (4 steps). `gate 1 rule N none; bin 1 rule(new); xc 0 1; bin 0
head(new)`. File 0 took `v2` only in the bin; one Delete of `H2` there makes it serve the older
`rw`. This refutes the property the design checks as `one-loss` ("no single deletion ... made a copy
wider") [design §6.1, §12.3], which its model could not see because it has no bin.

**The security critic's change does not close it.** `critique-crdt-security-and-fit.md` S3
proposes that a binned head count as in force. It closes CE1 (`CE1-binheads`) and not CE3
(`CE3-binheads`), because a Move puts the head outside the bin.

**Change.** Make the read a function of the set of verifying elements **wherever they sit in the
tree**: an entry is an element when its identifier is the one its MAC gives, in any group, the bin
included; a copy under another identifier stays ignored. Location then means nothing, which is
what section 2 of the design already claims. Seal again collects from the same set (the random
search found it sealing a renewal answer as "ask" while the head that carried "forever" sat in
another group). The API's guard follows: an element moved into a user group is still the plugin's
and must be absent on every route, not only inside the store group [design §10, "The API"].
**Cost**: finding elements means walking the tree's entries; `option-store-entries.md` measured
0.26 ms for 10,054 entries by identifier [design §10], and the memo of §10 still applies.

**With the change** [model here, `results-fixed.txt`]: every history of this section holds (CE1,
CE1-move, CE2, CE3, CE3-bin, CE4, CE7, CE10, CE13), and the random search below finds failures only
in histories with a deletion that leaves no record at all (`del0`, a script) combined with KeePassDX
merges, which is the design's own 6.4 item 3. With P1's and P2's changes together, the only
counterexamples of this critique still failing are CE6 (P5, a script) and CE11 and CE12 (P4, a change
the design has not made).

### P2. The 64-height window is fail-open (serious)

**Where.** "A record more than 64 heights below the highest head in force is taken for an
ancestor" [design §5.2 point 5], and "a record more than 64 heights below the standing versions is
taken for one long replaced" [design §5.2 point 4]. Height "decides nothing between two elements"
[design §3.4], but here it decides exactly that. Not in the design's model [design §12.1].

**CE5** (70 steps; 65 of them are ordinary gate writes, here renewal answers).

- Step 1, `gate 0 rule N r`: file 0 narrows `N` at height 1.
- Steps 2 to 66: file 1, which never got it, makes 65 writes (heights 1 to 65).
- Step 67, `gate 1 rule N rw`: file 1 touches `N`'s rule (height 66), naming only `v1`.
- Steps 68 and 69, `delr 0 rule(new)` and `delr 0 head(new)`: in file 0, `v2` and its head
  deleted for good, both leaving records. File 0 is incomplete.
- Step 70, `sync 0 1`.

Outcome [model here]: both files whole, `N` gives read, update, list; the meet of the two copies'
decisions is read and list. Both records arrived. The head's record is 65 heights below `H_B` and
is taken for an ancestor; `v2`'s record is 65 below `v_B` and taken for long replaced. The design
says of this case that "through Synchronize or Keepass2Android the head's deletion record travels
and the receiving copy is incomplete too" [design §6.4 item 2]. With the window removed
(`CE5-unbounded`) it is.

**Why it is not a corner.** The window is crossed by about 65 writes, and every pairing, renewal,
revocation, rule edit and owed answer is one. At a renewal a week for a few clients that is a few
months, which is how stale a second machine's copy can be. Height cannot tell a head that was
replaced from a concurrent one; the design's own counterexample 3 [design §12.4] is that sentence,
and the window brings it back for anything 65 writes away.

**Change.** No window anywhere: a head names every head it replaces, directly or through another,
for good, and a version names every earlier version of its object, so every record of the gate's
own is recognized exactly, every other record means a loss, and both tolerances go. Heights then
decide nothing and can leave the identifier (P8). The cost is growth: 16 bytes of identifier per
gate write in the head, written again at every write: 80 KB of identifiers after 5,000 writes,
twice that as hex text [A, arithmetic], measured nowhere. A probabilistic set (a Bloom filter) is
not a substitute: its false positive is "taken for an ancestor", the widening direction.

### P3. A whole file written over a newer one (serious, shared; fatal without a ruling)

**Where.** The design states it [design §1 weaknesses, §6.4 item 1].

**Stated as what it is.** Let `V` be any read function of the file alone. If the file at time `t`
is replaced by a byte copy of an earlier state `F_s`, then `V` after the replacement equals
`V(F_s)`. If a revocation was made between `s` and `t`, and `F_s` admitted the client, the client is
admitted. No rule of reading, head, witness or MAC changes this, because all of them are in the
file. The transport includes a step that is not an inflation, and a state-based replicated type is
correct only over inflations.

**An Android program in scope does it**: every KeePassDroid save over a file that changed while it
was open [R android 4.2, `edit/SaveDB.java:54-75`]. KeePass puts the union back only if its next
save meets the changed file while the database is still open in memory [R keepass 9]. Lock and
unlock reload the file from disk [R CLAUDE.md, "a locked document's close": the lock runs
`PwDatabase.Close`], so a lock between the gate's write and the operator's next save is enough to
lose it [A that the reload reads the overwritten file; it reads whatever is on disk].

**Change, needing a decision of the project.** A replica that never goes back: the gate keeps the last
head it wrote or served for each database (keyed by the authority's digest, MAC'd under the store
key) in KeePass's configuration, where `KPG.Port` lives, and **reads it as one more carrier**: its
refusals and removals join by union, its versions join the register like any head's, and its
height (or, after P2, its ancestry) tells the read that the file went back. In the terms of the
field this is a third replica whose state only grows, and the join with it is the same join as
any other: a stale host head is replaced by the file's newer versions, which name it; an older
file is replaced by the host head's. It writes nothing into the file at open (O1 holds); the next
gate write puts the union back. It goes further than the high-water mark of design §6.4, which
only detects: this one also keeps the decisions, so the revocation survives on the machine that
made it.

It is state on the host about a database, which CLAUDE.md's first decided bullet and E9 rule out
as written. It names nothing to an attacker that the file does not already hold. It carries no
admission record and no authority, so it admits nobody; its versions replace a file's only where
they name them, which is the operator's later word, and a newer file's versions replace its own. **Without that
ruling, no option that keeps its data only in the file meets the hard requirement under
KeePassDroid**, this one included.

### P4. A new store key at Recreate splits the state (serious if S1 is taken)

**Where.** Not in the design: the design carries the key forward [design §7]. The security critic
proposes a new key at every Recreate with every element in force written again under it
(`critique-crdt-security-and-fit.md` S1). After the change, "nothing under the other key is read"
[design §7].

**CE11** (4 steps). `gate 0 rule N r; gate 1 recreate; gate 1 pair new pq; sync 0 1`. File 1
recreated with a new key and wrote `N`'s rule `rw` again under it; file 0's concurrent narrowing is
under the old key. Outcome [model here]: the new client gets read, update, list at `N`, where the
meet is read and list. **CE12** (5 steps): the same with a profile removal in file 0; the name is
back on the list. With the key carried forward, as the design has it, both hold
(`CE11-design`, `CE12-design`).

**Why.** "Unknown means concurrent, and concurrent means narrower" [design §3.4] is the rule that
makes every join safe. A key change turns "unknown" into "unreadable", and an unreadable element is
dropped, not met.

**Change, if S1 is adopted.** Keep the retired keys (each retirement can carry its authority's
store key, or the new document the chain of old ones) and read under every old key the elements
that only narrow (refusals, removals, and register versions, taken as concurrent and never as
replacing anything under the new key); read additions, records and authorities under the served
key only. A leaked old key then lets an outsider add only narrowing, which S1 is content to allow.

### P5. T3 is not harmless (minor once P1 is changed)

**Where.** "A program can save an entry unchanged under a new time ... Harmless here: the content is
the same, and identity is by content" [design §2 T3]. And: through Synchronize or Keepass2Android,
"the older elements do not arrive at all: the copy's own records keep them out" [design §6.2 row 5].

**The fact.** KeePass drops a deletion record when its entry is present and newer [R keepass 5,
`PwDatabase.cs:1083-1096`; M keepass X3, X4]. An unchanged save in KeePassDroid's editor moves the
time [M measured-android 3.3]; so does an accidental title edit in Keepass2Android or KeePassDroid
[M measured-android 2.1, 3.2 step 3], and a KeePassDX Delete [R android 4.2]. Whether the title is
part of "its strings read strictly" [design §5.2 point 1] is not said; if it is not, a title edit is
T3, if it is, it is damage.

**CE6** (6 steps). `gate 0 rule N r; touch 1 rule(old); touch 1 head(old); del0 0 rule(new);
del0 0 head(new); sync 0 1`. Outcome [model here]: whole, `rw`. Without the two touches
(`CE6-no-touch`), incomplete, as the design says.

**Severity.** It needs the newest elements removed without any trace, which after P1's change is a
script's only. So minor; but the design's row 5 should say that a stale copy's elements, saved on
the phone after the gate pruned them, arrive and take their records with them.

### P6. The same elements, different answers (minor)

The value is a function of the elements, the deletion records, and (as written) the locations.
KeePassXC, KeePassDX and KeePass's other four methods leave the records where they were, so two
files that exchanged everything that way hold the same elements and answer differently. CE8
(3 steps): a client's record deleted for good in file 0, then KeePassXC both ways: both files hold
the record, file 1 admits the client, file 0 does not. In 1,500 histories with every fault but an
overwrite, 101 pairs of files held identical element sets and answered differently, 34 of them
both serving; with P1's change, 3. The one the probe printed came from a record and refused; I did
not read the other two [model here, `results-liveness.txt`]. The design's convergence check runs Synchronize twice around [design
§12.2], which equalizes records and locations and so cannot see this. After P1's change the
divergence is in the refusing direction only, since by 5.2 a record can only take an element out of
force, refuse by its tag or stop the store, which is why this is minor; `docs/` should still
say that copies exchanged by KeePassXC converge only after a Synchronize.

### P7. Meets no document can state, and the wording of the join (minor)

- Of the 128 per-profile meets of two of the eight documents in my model, 56 are limits `L(S)`
  with `S` not everything [model here]. No `AclDocument` writes a limit. The design keeps the meet
  in the resolver [design §4.3], which works; but **Settle** writes a document, so it can never
  write "keep what applies now" when what applies is a limit, and the tab has to say so rather than
  offer it.
- "Every join moves toward less access" [design §1 point 4] is not what holds or what the model
  checks: a join also delivers the operator's widening that named everything it replaced, which is
  intended. What holds is narrower: the value of a join is at most the meet of the decisions that
  nothing names. The sentence should say that.

### P8. Two bytes of height (minor)

The identifier carries the height in two bytes [design §3.2]. At 65,536 gate writes it wraps; with
daily renewals for ten clients that is about 18 years [A, arithmetic]. After the wrap the window
comparisons of P2 invert. If P2's change is made, heights decide nothing and the two bytes can go
to the MAC.

## 4. The model

### 4.1 What it is

`crdt-model\critic\crdt_formal.py`, about 1,100 lines of Python with no dependency, written from the
design's text. One line runs everything:

```text
uv run --no-project python -I -X utf8 spikes\storage\model\critic\crdt_formal.py all 1500
```

`ce` replays the counterexamples with every file's value after every step; `laws` checks the
lattice and the set types; `search N` runs the random search. `probe_liveness.py` and
`probe_restore.py` are the two probes of section 2 and P6, and `probe_fixed.py` replays every
counterexample under P1's change and under P1's and P2's together. Outputs: `results-critic.txt`,
`results-liveness.txt`, `results-restore.txt`, `results-fixed.txt`. The same files are under my scratch directory,
`storage\crdt-formal`.

- **A file**: entries (an element, a modification time, a location among store group, recycle bin
  and other group, a location time), deletion records with times, the marked nodes, and for the
  oracle every element ever in its lineage.
- **Elements**: authority (with its store key), retirement, client record, revocation, profile
  addition (naming the removals it saw), removal, rule version and renewal answer (naming what
  they replace), head (carrying every refusal, every removal and every standing version). The
  identifier is kind, subject, height, nonce and key, as the UUID layout of design §3.2 exposes the
  first three to a deletion record.
- **The read**: section 5.2 point by point, the node rule of section 8 reduced to "a ruled node is
  marked", and the walk of 4.3 over two profiles and two nodes. Switches: the window `K`, counting
  elements anywhere (P1's change), binned heads in force (the security critic's S3), the other
  reading of point 4, and key rotation (S1).
- **The gate**: rule, renewal answer, revocation, pairing (always a new certificate), profile
  removal and addition, Recreate, Delete authority, Seal again; each reads through the design's
  read, refuses while incomplete (except Seal again), writes, prunes per section 9, writes the
  witnesses and the head.
- **Merges**, as section 2's table of the design and the ground reports: Synchronize (both files
  written; records joined; a record dropped where its entry is newer; locations by location time),
  Keepass2Android (the same, into one file), KeePass's other methods (no arriving record; the
  target's own applied to arrivals; no move merged), KeePassXC (nothing applied or copied; a
  foreign bin optionally an ordinary group), KeePassDX (arriving records applied and dropped; an
  arrival older than the target's own record kept out; location to the arriving copy at equal
  times).
- **Faults**: Delete to the bin, KeePassDX's Delete (bin and a new time), Move, deletion with and
  without a record, a new time without a change (T3), records cleared, a file written over another.

### 4.2 Two oracles

- **IK**: every element ever written into the file's lineage, by its own gate or through any merge
  into it, read with no loss, no records, every location, and the full causal order. A failure
  against IK alone can be a decision the file was never physically given.
- **IE**: every element physically present in the file now, in any group, read the same way with
  the order the file itself writes down. **A design value wider than IE means the design threw away
  narrowing evidence that is still in the file**, which is a defect of the read and not a limit of
  the transport.

CE1, CE2, CE3, CE4, CE7, CE10, CE13 fail against IE: the evidence was in the file. CE5 and CE6
fail against IK only: the evidence was a deletion record, which the design itself treats as
evidence and then discounts.

### 4.3 Where my reading of the text may differ from the design's

- **5.2 point 4, "the ones nothing names"**: I read "nothing" as no version of the register as just
  defined (entries in force, heads in force). Under the other reading, a binned head's versions
  name too; CE1 and CE2 then refuse instead of widening, while CE1-move (run under both readings)
  still fails, and CE3, CE4 and CE13, which are about refusals and a head outside every place the
  read looks, are not touched by that reading.
- **"Present"** (a head present, for refusals): the store group and the bin.
- **Seal again** names in its head every head named by a record, so that the store is whole again;
  for a marked node with no rule and for an object refused by point 4 it writes a refusal naming
  the deleted versions [design §5.4].
- **The height of a non-head element** is the height of the head written with it.
- **The window**: a head's `after` keeps the ancestors within 64 heights, inclusive; the tolerance
  takes records strictly more than 64 below. An off-by-one there made honest histories incomplete
  in my first run; the design should state both bounds together.

### 4.4 What the search found

1,500 random histories for each configuration and each of three and four files, 4 to 14 steps,
gate acts 40%, merges 30%, faults 30%; every step checked against both oracles on every file;
shortest failures reduced by dropping steps. The full table is `results-critic.txt`.

Configurations: A merges only, by all five programs; B adds Delete to the bin, KeePassDX's Delete
and Move; C adds deletions with a record; D adds T3; E is Synchronize and Keepass2Android only,
with Delete to the bin, deletions with a record and T3; F is every fault but an overwrite, `del0`
and records cleared included; G adds overwrites and restores.

| Read | A | B, C, D | E | F | G |
| --- | --- | --- | --- | --- | --- |
| as written | none | failures in 4 of 6 runs, every one through a Delete or a Move (P1); in C a revocation undone in 6 steps (CE13) and a narrowing healed wider through Synchronize in 5 (`gate 2 rule N deny; bin 2 head(old); move 2 rule(new); gate 1 prem q; sync 2 1`) | none found, though CE1 lies in this space | one, through a Move | one, through a Delete |
| as written, `K=3` standing for 64 | none | failures in 5 of 6 runs, through P1, the window, and once the design's own 6.4 item 2 (two deletions with records, then KeePassXC) | none found | failures, one of them Seal again sealing without a head that sat in another group | none |
| P1's change | none | none | none | one: `del0` twice with KeePassDX merges (design 6.4 item 3) | none |
| the critic's S3 change | none | failures through a Move | none | one, through a Move | none |

Each cell is 1,500 histories for three files and 1,500 for four.

The random search reaches few failures of any kind; the targeted histories are the evidence, and
the search's job here is to show which classes remain once a change is made. That is also the
honest reading of the design's own 480,000 histories: they could not contain a failure of P1 or P2,
because the model they ran has neither a location nor a window.

## 5. Claims leaned on that nobody has measured

1. That KeePassXC's Delete moves to the bin without a record and its Move keeps the identifier:
   read [R keepassxc] and measured for `rm`, not run here.
2. That a Synchronize moves an entry by its location time when both copies hold it: read and
   measured for KeePass [R, M keepass 2.2]; for Keepass2Android measured for one move
   [M measured-android 2.2 `Y3`].
3. That KeePassXC merges an entry's location by location time: [A]. CE3 does not depend on it (the
   arriving entries are new to the target).
4. P1's change costs a walk of the tree's entries per change of the file: [A] from the 0.26 ms of
   `option-store-entries.md`, not measured on KeePass's thread.
5. P2's change costs about 16 bytes per gate write in every head: [A], arithmetic.
6. P3's change: that KeePass's configuration is per Windows user and survives the database being
   overwritten [A]; that lock and unlock reload the file from disk [A, from CLAUDE.md's account of
   the lock running `PwDatabase.Close`].
