# Critique of option `crdt`: security, KeePass's own behavior, the operator, and the cost

Key `crdt-security-and-fit`. Written 2026-11-15 against `option-crdt.md` and its model
`crdt-model\crdt_model.py` as they stood at 20:03, KeePassGate master `a42e38aa`
(the product at `a42e38aa`) and KeePass 2.62. This attacks the design. It does not edit it.

## How statements are marked

- **[M here]**: measured for this critique: line and occurrence counts in the product at `a42e38aa` and
  one reading of the model, recorded in
  `spikes\storage\probes\crdt\security-and-fit\counts.txt`.
  Nothing else was run. No program with a window, no emulator, no test tier.
- **[M report]**: measured in that report: `android` is `measured-android.md`, `keepass` is
  `ground-keepass.md`, and so on, with the section.
- **[R report]** or **[R file:line]**: read from source, in that report or by me.
- **[design n]**: what `option-crdt.md` says in its section n. **[model]**: what its model shows.
- **[A]**: assumed. Neither run nor read.

## Verdict

**Viable with changes.** The shape this design stands on, ordinary entries written once in a
group of the plugin's own, a custom string on a ruled entry and a small entry inside a ruled
group, is the shape the emulator runs measured through every operation Keepass2Android and
KeePassDroid offer, and back through KeePass's Synchronize, with nothing lost [M android 2, 3, 5,
7]. Nothing in the design depends on node custom data, per-key times or modification times,
which are exactly what the phone programs destroy. So the survival requirement can be met.
Under this lens, though, there are six serious problems and six minor ones. None is fatal,
because each serious one has a change that closes it or turns it into a refusal. Two of them,
S3 and S4, are about a phone: one gesture there either stops the gate for the
whole database, or, confirmed twice, takes every rule away.

On the cost question: the result is **differently complicated, not simpler**, than the
recommended hand-built family (`EVALUATION.md` 5.1). In an arrangement of one KeePass
with the gate and phones that never write KPG data, there is one writer. Most of what makes this
a CRDT (the meet of concurrent versions, Settle, closed ancestor lists, heights) then never runs.
The parts that do the work are the ones the hand-built designs already had: write-once entries,
an authenticated store, a checkpoint that carries the decisions, and witnesses for the final
refusals. What the formulation buys is a single, checkable account of every merge, and that is
worth having. What it does not touch is where the risk actually sits: loss, overwrite, and keys
sitting in entries.

| # | Rating | Problem |
| --- | --- | --- |
| S1 | serious | Recreate carries the store key forward, so after a leak the operator's one remedy leaves the MAC's promise broken |
| S2 | serious | The authority's private key, and with it now the store key, sits in an ordinary entry that every program shows, prints and exports |
| S3 | serious | One ordinary gesture on a store entry (the head to the recycle bin, a Duplicate, a hand edit) stops the gate for the whole database, everywhere it travels |
| S4 | serious | Deleting the plugin's group on the phone, confirmed twice, removes every rule in every copy the deletion reaches |
| S5 | serious | A rule set on a node dialog becomes a whole-database save after the dialog has closed, and a refused write leaves the operator's rule lost or the node refusing |
| S6 | serious | KeePassDroid writing a whole file over a newer one undoes revocations and narrowings without a trace (shared by every in-file option) |
| M1 | minor | Seal again is pressed under outage pressure, and its preview shows what is left rather than what is known to be lost |
| M2 | minor | Anyone holding a copy of the database can make the store refuse in every copy by writing over an element's identifier |
| M3 | minor | Unspecified edges in the read rule: what "strict" allows, key labels, and the text and the model disagreeing about copies |
| M4 | minor | Growth is several times the design's estimate, though still cheap; lock and unlock cost almost nothing |
| M5 | minor | The concurrency machinery serves a second gate the operator may never run; a simpler rule would be as safe |
| M6 | minor | The API guard is new code on every route, and 0.1.0 rules read as silence unless the migration is built |

## What holds

These were checked, so they get credit and are not raised again below.

1. **Through the routes, a client reaches nothing of the store.** No route moves a node or
   copies one, and every node a client creates gets a fresh random identifier
   [R `store-entries-security-and-fit` "What was checked", 1]. A random group identifier
   matches the store group's eight-byte prefix with probability 2^-64, so no client can make a
   store group. Given the guard of [design 10] (the store group absent, `KPG.` strings hidden and
   refused), a client can read, add, copy or remove no element and no mark. A mark can only take
   away [design 8], so even a missed refusal of a `KPG.Node` string could narrow and never widen.
   Removing a ruled group removes its link and leaves the rule in the store, which is right.
   This holds only if the guard is complete (M6).
2. **A file that never held this database adds nothing.** Every element is MAC'd, its identifier
   comes from the MAC, and deletion records naming an element or a witness cannot be computed from
   outside [design 7; model scenario 13]. This closes the planted-record findings of three earlier
   critiques at their root. It is weaker than it reads: S1 and S2 are about how long the key stays
   secret.
3. **Replay of the database's own old elements is handled by the types**, wherever the newer
   element survives in some carrier. Old versions are named by the closed `after` lists or sit
   beside the gate's own deletion records. A replayed refusal is still true. A replayed addition
   of a profile loses to a removal the file knows. Beyond the 64-ancestor cut, an old version
   counts as concurrent and can only narrow [design 3.4, 9]. What is left is [design 6.4] items 3
   and 4, which the design states.
4. **There are no replica names to collide.** An act is a 16-byte nonce, and a file carrying
   elements "under the same replica" is not a case the design can meet [design 3.3].
5. **Opening, reading and merging write nothing** (O1), and nothing reads node custom data, which
   KeePassDroid deletes at every save [M android 3.2].
6. **At lock and unlock it costs almost nothing.** The store key is one more 32-byte secret, held
   while the database is open and let go with the authority's other material at lock (`CA.md`
   §208 as CLAUDE.md summarizes it). The responder reads no store. After unlock the memo starts
   cold, so the first request verifies each element once: a few dozen HMAC-SHA256 over short
   strings, and over a head of some tens of kilobytes [A: microseconds to about a millisecond, not
   measured].

## Problems

### S1. Recreate carries the store key forward, so a leak outlives its remedy (serious)

**Where.** [design 7]: the key "lives inside the authority's document ... A recreated authority's
document carries the same key forward." The reason given is that "a signature would tie every rule
and every profile element to one authority, so Recreate would have to write them all again".

**Sequence, at the level of the design.**

1. The authority document leaks through any of the surfaces in S2: an export, a print, a copy taken
   from a phone's reveal control, a backup copy handed to somebody.
2. The operator does what `CA.md` offers for a leaked authority: Recreate, then pair every client
   again.
3. The new document holds the same store key [design 7].

**Outcome.** The certificates are safe again, but the store is not. Whoever holds the leaked text
can still make elements that verify, so "a file that has not read this database cannot make an
element of it" no longer covers them, and nothing the operator can press restores it. The
property the evaluation lists as a gain of the MAC, that it "shuts out the holder of an old copy
once the authority has been recreated" [R `EVALUATION.md:518-520`], is exactly the one this
design gives up. The threat model names the caller. A prompt-injected agent never sees the key
through the routes (What holds, 1), but the key does not have to reach it through the routes for
this to matter: the leak paths are the operator's own tools.

**Change.** A new store key at every Recreate, with every element still in force written again
under it in the same write, and nothing under the old key counted from then on. The head already
gathers everything that has to be rewritten, so this is one write. It costs what the design
declined: Recreate becomes a re-seal, and a stale copy's elements stop verifying, which is the
point. Record in `CA.md` that the MAC defends against a stranger's file only, never against the
holder of any copy, and that Recreate is what rotates it.

### S2. The authority's private key, and now the store key, in an ordinary entry (serious)

**Where.** [design 1] (weaknesses) and [design 7]: the authority's document is a protected string
in an entry of the store group, and it now also holds the store key.

**What changed from `store-entries` S3.** That critic showed the key one copy, print or export away
in every program: KeePass's Show all and Find ignore the searching switch, print and the HTML
export write protected custom strings in clear when asked, the KDB 1.x export folds them into
the notes, KeePassXC's HTML export prints them by default, and a phone offers reveal and copy
[R `store-entries-security-and-fit` S3, M2; A for the phone]. This design leaves all of that as
it was [design 11, row S3]. It adds one thing. The same string now gives the power to write any
rule, record or refusal that verifies, besides the power to mint certificates. So one leak gives
two powers, and by S1 Recreate takes back only one of them.

**On the phone**, concretely: the operator opening the KeePassGate group in Keepass2Android or
KeePassDroid sees an entry for the authority beside the others. Those programs kept its
2,861-character protected value and its flag [M android 2.1, 3.2]. That they show it behind a
reveal control and a copy button is [A]. An Android clipboard passes through the keyboard's
history [A].

**Change.** No flag on an entry keeps it out of print or export [R
`store-entries-security-and-fit` S3]. So the choice is between two prices, and the operator has to
make it, because the design does not:

- keep the authority document and the store key in `PwDatabase.CustomData`, as today, where no
  ordinary screen shows them. Then a KeePassXC merge of a copy loaded later, made before Create,
  drops them [M spike]. Nothing verifies and nothing is served (fail closed) until a Synchronize
  with a copy that still holds them. This breaks no rule, since every element is still in the
  file, but it breaks availability under KeePassXC;
- or keep them in the entry, and say in `docs/` and in the group's notes line that the group must
  never be printed, exported or opened on a phone. That is a rule for a person, not a mechanism.

### S3. One ordinary gesture stops the gate for the whole database (serious)

**Where.** [design 5.2] point 5 makes the store *incomplete*, so that it serves nothing, when no
head is in force, and when anything in the group "carries a `KPG.` string and is not an element"
(damage). [design 6.2] accepts this as "the price of the head being the witness that the store
is whole".

**Sequence A, on a phone.**

1. Keepass2Android, the KeePassGate group, long-press the entry titled `KPG head`, Delete, and
   answer **No** to "Press No to recycle" [M android 2.1 for this gesture on an entry].
2. The head is now in the recycle bin, not a direct child of the store group, so no head is in
   force [design 5.2, point 5].
3. Back home, KeePass synchronizes with the phone's file. The move travels, by location [M keepass
   2.2 for a recycled entry], and the copy KeePass serves is incomplete too.

**Outcome.** Every agent gets the `503` word for an incomplete store, on every route, until the
operator is at the Windows machine and presses Database Settings, the KPG tab, Seal again, then OK.
Nothing was lost: the head is intact in the bin and still carries every decision. The rules
survive, so the hard requirement is met, but the gate is down because of one tidy-up gesture on a
phone.

**Sequence B, in KeePass or any program that copies an entry.**

1. Select any store element, say a `KPG profile` entry, and choose Duplicate Entry. The copy lands
   beside the original, in the store group, with a new identifier and the same strings [R keepass
   9, `EntryUtil.cs:258-279`; T12].
2. The copy carries `KPG.` strings, and its identifier is not the one its MAC gives, so by the
   text it is damage [design 5.2, point 1].

**Outcome.** The same database-wide refusal, from a menu item. If Delete Duplicate Entries runs
afterwards and pairs the two, it deletes the older one, the original, with a deletion record
[R keepass 9]. The copy then stays as damage. Whether KeePass's own Duplicate appends "- Copy" to
the title, so that the two no longer pair, is [A].

**The text and the model disagree here.** The model counts as damage only an entry under its own
element's identifier (`crdt_model.py:206-209` [M here]). So for the model a copy under a new
identifier is ignored, while for the text it stops the gate. The model's 480,000 histories
therefore do not cover the text's rule, and "where an entry sits" is outside the model anyway
[design 12.1].

**Change.**

1. An entry whose content verifies under an identifier that is not its own is a *copy*: ignored
   and listed on the tab, never damage. This is what the model already does, and it cannot carry
   anything the real element does not, since a deletion record can name only the real one.
2. A head in the recycle bin that verifies and that nothing names counts as in force. Binning
   loses no information. This has to be run in the model, with the bin modeled, before it is
   adopted [A: that it is safe].
3. Keep the previous head beside the current one, so that one deletion of either is healed with
   nothing refused, as one lost rule entry already is [design 6.1].

Database scope then stays for what one loss cannot explain, which is what the design argues it is
for.

### S4. The plugin's group deleted on the phone takes every rule with it (serious)

**Where.** [design 1] (weaknesses): "The whole group deleted for good, and that deletion
synchronized, removes the store from every copy ... the rules have to be entered again."

**Sequence.**

1. Keepass2Android: long-press the KeePassGate group, Delete, **No** (to the recycle bin). Then
   open the bin, long-press it, Delete, **Yes**: deleted for good, with deletion records [M
   android 2.1 for both steps on an entry; for a group, assumed alike].
2. The next KeePass Synchronize, or a Keepass2Android merge, applies the records in every copy
   [M keepass X4; M android 2.2].

**Outcome.** The marks hold: every ruled node has its mark and no rule, so it refuses there and
below. Nothing is served anyway, since the authority went with the group. So E1 and E6 hold.
But every rule, every client and the authority are gone. The operator enters every rule again and
pairs every client again. Keepass2Android asks twice, so this is a deliberate act. It is still
the case the survival requirement names,
done by somebody tidying up a group of unfamiliar entries, which is
lens (d) exactly.

**Change.** A second plugin group, found by the same prefix ("several are read as one", [design
3.1]), that holds the store key and a copy of the head and nothing else, written with every head.
Deleting one group is then a loss that heals: Seal again rebuilds the rules and refusals from the
other. Deleting both is two separate deliberate acts. The authority and the records do not have to
be copied there, since losing them fails closed (clients pair again, E2). The price: a second
visible group, and the store key in two entries, which adds to S2.

### S5. A rule set on a node dialog becomes a save after the dialog has closed (serious)

**Where.** [design 10], "The grant editor": the change "applies it when the group or entry dialog
closes with OK, through the writer", and "a first rule on an entry puts `KPG.Node` into the dialog's
`EntryStrings`". Every write through the writer ends in `MainForm.SaveDatabase` and a hash of the
file read back (P13). [design 16.7] lists `EntrySaving` cancelling an OK as still to measure.

**Sequence.**

1. The operator opens an entry, goes to the KPG tab, gives `agent` read and list, and presses OK.
   KeePass keeps `KPG.Node` in the entry, because it was written into `EntryStrings`.
2. The writer is asked to add the rule element and a head. It refuses in the cases it refuses
   today: another group or entry dialog open, Edit Entries open, KeePass's progress window,
   KeePass's own block on interaction [R inventory P11, P13; CLAUDE.md].

**Outcome.** The dialog is already gone. The entry carries its mark and the store has no rule for
it, so the entry refuses there and below [design 8, table row 2]. The operator meant to open it.
For a rule change on an entry that already had one, the change is lost after an OK that said it
was kept, against O3. When the writer does accept, every rule OK is a whole-database save: `DESIGN.md` §119
measured 80.6 seconds for 201 MiB of attachments, and a heavy KDF adds eight seconds a save
(CLAUDE.md). Today a rule edit costs nothing until the operator saves.

**Change.** At the node dialog's OK, add the element and the new head to the store group in
memory, mark the database modified, and let the operator's next save write them, the way the
grant editor's write rides the dialog today. The rule then bites at once from memory (E14). The
add itself is refused, with the dialog kept open and a message, while a save or a merge is running
(`GroupDialogs.Working`, `UIIsInteractionBlocked`), because a write that lands inside a save's
serialization makes the file disagree with memory [R `rules-linked-security-and-fit` S4]. Measure
the cancel in `EntrySaving` and the group dialog's closing before relying on it.

### S6. A whole file written over a newer one (serious; common to every option in the file)

**Where.** [design 6.4] item 1, stated by the design.

**Sequence.** KeePassDroid has the database open on the phone. On Windows the gate revokes a
client, or the operator narrows a rule, and the file is saved and synchronized. The person on the
phone edits a password and saves. KeePassDroid writes its whole file over the changed one [R
android 4.2], and the synchronizer carries it home.

**Outcome.** If KeePass still has the database open, its next save meets the changed file and
offers Synchronize [R keepass 9], and the union restores the revocation, provided that whoever
answers chooses Synchronize. If KeePass had closed it, the next open loads the older file: the
revoked client is admitted again and the narrowed rule is wide again. That fails E3 and E5,
undetected. It is an Android program in scope, and it is not rare in principle: every
KeePassDroid save over a changed file does it. It does not separate options, since nothing inside
the file can see it.

**Change.** The design names the only detection: a per-database high-water mark of the heads the
gate has written or served, kept in KeePass's configuration beside `KPG.Port`, so that a file whose
heads are all lower is incomplete until a press [design 6.4]. That is host state about a
database, so it needs a decision of the project against CLAUDE.md's first decided bullet. It could
only ever refuse, and it names no database to an attacker, which is what the bullet guards
against. Without it, `docs/` has to say that KeePassDroid must not save a file that anything else
may have changed while it was open. Note what the mark gives: the refusal survives, the data does
not. The operator revokes again from the audit.

### M1. Seal again is pressed under pressure, and shows what is left (minor)

The press "accepts what is left" [design 6.4 item 5, 12.2]. The moment it is offered is the moment
every agent is answered `503`, so the person pressing it wants service back. Its preview lists
"the authority, the clients admitted, the names, the nodes ruled" [design 5.4], which is what is
left, not what is known to be missing. The identifier layout already lets the gate say, from
deletion records alone, which kind of element was lost, about which subject hash and at what
height [design 3.2]. **Change:** above the press, list what the deletion records and the marks
say was lost, and write "a standing answer to renewals of (client) will be asked again" where that
is the consequence, since it has no third witness [design 6.4 item 5].

### M2. A same-identifier entry with a newer time makes the store refuse everywhere (minor)

An entry under an element's identifier with other content and a newer time wins every merge whole
(T2) and reads as damage, so the store refuses in every copy it reaches [design 6.2, row 2].
Making one needs the identifiers, which no route serves and only a copy of the database holds.
So this is within the concession that whoever holds the master key holds everything [design 7].
It fails closed, and Seal again heals it. Listed so that the availability cost of the damage rule
has its whole scope written down.

### M3. Edges of the read rule that need a sentence each (minor)

- **"Its strings read strictly"** [design 5.2, point 1] does not say whether a string the element
  was not written with, such as one a phone editor or another plugin adds, makes it damage. If it
  does, every such addition is a database-wide outage (S3). If it does not, say which strings the
  MAC covers: today it is kind and data [design 3.2], so a title or notes edit is harmless, which
  is right.
- **Elements under another store key** are "ignored and shown" [design 6.2, last row], while
  point 1 of 5.2 makes anything that does not verify damage. The model tells them apart by a key
  label [design 12.1]. A label is unauthenticated text, so the rule has to be that the label only
  chooses which key to try, and that an element verifying under no key the database holds is
  damage only when its label is this store's own.
- **The entry tag** hashes every string with its name [R `rules-linked-security-and-fit` M5,
  `NodeTags.cs:58-66`]. `KPG.Node` must be left out, or an `If-Match` holder learns when a rule is
  first set.

### M4. Growth, and the cost of reading (minor)

The design estimates "about a hundred" deletion records a year at a pairing a week [design 9]. But
every write of the gate deletes the head it replaces, and renewals and rule edits are writes too.
For a busy database, say twenty clients renewing monthly, twenty pairings, ten revocations with
their witnesses, a hundred rule edits and fifty renewal answers, that is about 400 writes a year:
about 400 records for the heads, 240 for replaced client records, 100 for replaced versions, 10
witnesses. Some 750 a year, before the operator's own deletions [A: arithmetic on the design's
rules]. The store itself holds about 140 entries in that database, plus a link entry in each ruled
group. A head carrying a hundred rules of about 150 characters is some 20 KB [A]. Reading stays
cheap: 5,000 deletion records were read in 0.08 ms [M store-entries 4, as the design cites it].
The one cost not counted is the memo's key, a digest over the deletion records on every request,
since nothing tells the plugin when they change [A: a fraction of a millisecond for 5,000]. Minor,
but the estimate in [design 9] should be corrected so the decision is made with the real number.

### M5. Machinery for a second gate (minor)

The meet lattice in `AclResolver` [design 4.3], Settle, the closed `after` lists and heights all
exist for two gates writing two copies concurrently. With one KeePass running the gate, versions
are a chain, and the only concurrency left is the settings tab's buffer against a pairing or a
renewal answer landing underneath it, which `after` handles on its own [design 10]. Whether the
operator ever runs the gate on two Windows machines decides how much of this ever runs.
**Simplification, if the operator says one gate:** concurrent rule versions make the node refuse
until Settle, rather than meet. Both are safe. Refusing needs no new kind of document in the
resolver, and in a one-gate arrangement it never fires. The limit `L(S)` stays for copies.

### M6. The API guard, and 0.1.0 (minor)

The guard hides a whole group, hides and refuses a family of string names on five field routes,
treats a group holding only its link as empty, and keeps `KPG.Node` out of tags. As a measure of
its size, the recycle bin alone is excluded in 20 places in `KdbxVault.cs` today [M here]. A route
that forgets it reopens E8, so it needs a test per route against the pinned KeePass. Separately,
[design 15] leaves migration undesigned. Unless the conversion is built, a node still carrying a
0.1.0 `KPG` item reads as silence and inherits, so the first request after an update reaches what
the root grants [R `rules-central-security-and-fit` S5 as the design cites it]. Either the
conversion is built, or a node holding the old item refuses until it is removed on the tab.

## The lenses, briefly

**(a) The caller.** Through the routes: nothing, given the guard (What holds, 1; M6). Through a
file the operator merges: a stranger's file adds nothing (What holds, 2); a file from anyone who
ever held a copy or a leaked document can add anything, and Recreate does not stop it (S1). Old
elements of the database itself: handled by the types except [design 6.4] items 3 and 4 (What
holds, 3). Lock and unlock: one more secret held while the database is open, and negligible
verification work (What holds, 6).

**(b) Where things show up.**

| Element | KeePass | Phone (Keepass2Android, KeePassDroid) | Exports and reports |
| --- | --- | --- | --- |
| Authority's private key and the store key | Show all and Find list the entry; copy from the context menu [R `store-entries-security-and-fit` S3, M2] | the entry in the group, value kept [M android]; reveal and copy [A] | print and HTML export with custom strings; KDB 1.x into notes; KeePassXC HTML by default; XML export everything [R] |
| Head (every rule's text, every refusal) | as above; large-entries report | an entry with dozens of `KPG.S.nnnn` fields [A for display] | as above: a full map of the policy |
| Rules, clients, refusals, profile names | one entry each in the store group | one line each, titled `KPG ...` | as above |
| Marks | `KPG.Node` on every ruled entry; a link entry in every ruled group | the same, searchable [A] | as above |
| Auto-type and search | off on the store group [design 3.1]; `{REF:}` ignores searching but returns only standard fields, which are empty [R `store-entries-security-and-fit` "What was checked", 2] | not measured | |

**(c) KeePass's own functions.**

| Function | Effect on this store | Guard |
| --- | --- | --- |
| Entry dialog OK on a store element | a title or notes edit leaves the MAC intact; an edit to a `KPG.` string is damage: the database refuses | the damage rule (S3, M3) |
| Entry or group dialog OK on a ruled node | the mark is kept by KeePass; the rule waits for the writer after the dialog has gone | S5 |
| Duplicate Entry, copy and paste in the store group | a copy beside the original, damage by the text | S3 change 1 |
| Duplicate of a ruled node | the copy carries the mark and is limited; the pin follows at the next save [design 8] | holds, from the next save |
| Templates group set to the store group | entries made from an element land outside the group: ignored and shown | minor |
| History restore | elements have history only if edited by hand; restoring the earlier revision heals the damage | holds |
| Delete Duplicate Entries | elements never pair, because of the nonce; after a Duplicate it deletes the original | S3 |
| Recycle bin | a binned refusal still counts (good); a binned head stops the gate (S3); the group emptied from the bin takes every rule (S4) | S3, S4 |
| Database Maintenance, "Remove deleted objects information" | clears the witnesses and the gate's pruning records; the next write puts back the witnesses of refusals in force [design 5.3]; a loss standing unsealed becomes [design 6.4] item 4 | the tab should say the records were cleared |
| Database Maintenance's custom-data write-back | no longer touches the store | the writer's refusal while it is open can stay for the remaining custom data |
| Triggers, File then Import (new identifiers) | the imported store group has no prefix: ignored and shown [design 11, model scenario 20] | holds |
| Synchronize | the transport the design is built on | holds |

`GroupDialogs`, the single writer and the save that is read back stay as they are. They gain one
case: the writer deletes the replaced head at every write, so a store element the operator has open
in an entry dialog makes the writer refuse every write until that dialog closes [A: that the
present rule about removing an entry under an open dialog applies]. Minor, but somebody who opens
`KPG head` to look at it holds up every pairing while it stays open.

**(d) The operator.** In a one-gate arrangement an ordinary week produces nothing to settle by
hand. Settle needs two gates, Adopt needs the operator to copy a ruled node, and Seal again needs a
gesture on the store or a loss [A: from the design's rules, not measured]. The weeks that cost are
the ones with a tidy-up on the phone: a total outage (S3) or every rule lost (S4). The pages have to
show, inside 449 by 314, the store's state and why it is incomplete, Seal again with what it keeps
and what was lost (M1), deletion records cleared, strays and copies, and on each node's tab four
more states (ruled; refusing, rule gone; a copy limited by its original, with Adopt; two versions
meeting, with Settle). On the phone the person sees a group of some 140 entries titled `KPG ...`,
one of them a private key, plus an extra entry in every ruled group and an extra field on every
ruled entry. Nothing tells them which ones are safe to touch.

**(e) Growth.** M4: about 750 deletion records and a few hundred store entries a year for a busy
database; reading stays well under a millisecond.

**(f) The cost in this code base.**

| Goes, or stops carrying load | Lines today [M here] |
| --- | --- |
| `CustomDataStore` (and `IKeyValueStore`) | 110 |
| `AuthorityKeys`, as key names | 267 |
| `ProfileNames.Merge` and the reconcilers in `PendingChanges` / `PendingStore` | part of 277 and 971 |
| `NodeDialog`'s reflection on `m_sdCustomData` | part of 457 |
| the KDBX 4.1 dependence, and `CustomDataMergeRuleTests` as load-bearing | |

| Changes | Lines today [M here] |
| --- | --- |
| `AuthorityStore`, `AdmissionStore`, `RenewalRefusal` (onto elements) | 643, 854, 150 |
| `AclResolver` (carry a cap up the walk; the meet, unless M5) | 537 |
| `DatabaseWriter` (elements, head, witnesses, pins at `FileSavingPre`) | 2,179 |
| `OpenDatabaseTargets` (the fold, the memo) | 1,642 |
| `GroupDialogs` (S5's in-memory add, the head under an open dialog) | 1,228 |
| `KdbxVault` and the handlers (the guard, M6) | 2,349 |

32 source files mention `CustomData` today [M here]. New code is: the element codec, the MAC and
the identifier layout, the four-condition fold, the head writer, witnesses, the three types,
marks, links and pins, the guard, three presses and their states on the tab, and a `503` word in
the routing document and in PyKeePassGate. The critics counted 49 test files for the database's
data and 27 for the rules to rebuild [R `store-entries-security-and-fit` M8;
`rules-linked-security-and-fit` lens (e)], and this design adds the model's twenty scenarios as
host tests [design 16.4].

**Simpler, or differently complicated?** Against the recommended family, records as entries plus
linked rules [R `EVALUATION.md` 5.1], this design has the same entries, groups, marks and MAC.
It adds three things: the head, as a carrier and as the witness that the store is whole; witnesses
in deletion records; and one type per datum, in place of per-datum fold rules. The head is the
real gain, because one lost rule entry or one lost refusal entry then costs nothing [design 6.1].
It is also the new risk, because it turns small gestures into database-wide outages (S3). The
types are a cleaner account of the same rules, not fewer rules: there are still four conditions
for a whole store, four presses, and an identifier layout that only the gate can read. So this is
differently complicated, with better-checked semantics. The places where risk is left (S1, S2, S4,
S6) are not CRDT problems, and the formulation does not reach them.

## Claims leaned on that nobody has measured

1. KeePassDX merges over this shape (read only, [design 16.1]). Section 4 of `measured-android.md`
   covers KeePassDX's "Merge from" over ordinary entries, but neither its phone operations nor the
   witness.
2. That a phone shows a protected custom string behind reveal and copy controls, and how it shows
   a head of dozens of custom fields (S2; [design 16.6]).
3. That deleting a group in Keepass2Android, then deleting it from the bin, writes deletion records
   for the group and its entries, as it does for an entry (S4).
4. That a head in the recycle bin can safely count as in force (S3 change 2): to be run in the
   model with the bin modeled.
5. Whether KeePass's Duplicate Entry appends "- Copy" to the title by default, which decides
   whether Delete Duplicate Entries pairs the copy with its original (S3).
6. That `EntrySaving`, or the group dialog's closing event, can cancel an OK and keep the dialog
   open (S5; [design 16.7]).
7. The cost of the memo's digest over deletion records, and of the first verification after
   unlock, on KeePass's own thread (M4, What holds 6).
8. That the present rule refusing a removal under an open entry dialog applies to the gate's
   deletion of a replaced head (lens (c)).
9. How often KeePassDroid, in an arrangement with a synchronizer, saves over a file that changed while
   it was open (S6; [design 16.8]).
