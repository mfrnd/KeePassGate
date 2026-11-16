# Option `single-entry`: the whole of KeePassGate's state in one entry, changed in place

Written 2026-11-15 against KeePassGate master `a42e38aa` and KeePass 2.62. The evaluation asks
whether an option on the list keeps the whole rule and meta data handling in one entry, so that
merges would be one or the other, all or nothing, and how that would handle cases where a
non-kpg program changes the tree. No other option on the list changes one entry in place:
`option-wildcard.md` is the nearest (one document, but a new entry for every version), and it
discards this shape in one table row. This report designs the shape in its strongest form and
answers both halves of the question.

## How statements are marked

- **[M probe]**: measured for this report, today, from a console: KeePass 2.62's KeePassLib in
  Windows PowerShell 5.1 and `keepassxc-cli` 2.4.2, 2.6.6, 2.7.12 and 2.8.0, over invented
  databases. Section 14 names the scripts and the outputs.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**, **[M inventory]**: measured in
  the ground report of that key, or in `spikes/revocation-merge` as the ground reports digest it.
  **[M store-entries]**, **[M rules-central]**, **[M wildcard]**, **[M rules-linked]**: measured
  by the probe of that option's design, or of its critique where "critique" is written. An **R**
  in the same place is read from source there.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[R 2.62]**: read by me in `KeePass-2.62-Source.zip` (the copy the `keepass` ground report
  verified), file and line given. **[R plugin]**: read by me in the review4 worktree at `a42e38aa`.
- **[A]**: assumed. Neither run nor read. Every assumption about another program comes back in
  section 15.

**Nothing about an Android program is measured by me.** The emulator run had written no
`measured-android.md` when this was finished. Every Android statement is [R android], [M spike] or
[A].

## 0. The question, answered first

**"One or the other, all or nothing": true, and measured.** In 64 KeePassLib 2.62 cells and 64
`keepassxc-cli` cells the entry was never a mixture: after every merge it held one copy's entry
whole, and never parts of two [M probe]. That part of the
idea holds in every program anybody has measured or read [M keepass 1a; M keepassxc 4; M others 4;
R android 1.1].

**But "one or the other" is chosen by the entry's modification time, so the wrong one can win, and
at a tie the other one is kept nowhere.** A stale copy whose entry was touched later wins. Two
writes in the same second lose one side for good, and KeePass and KeePassXC pick opposite sides
[M probe, S3; M keepass 1a; M keepassxc 4]. So the single entry alone fails the hard requirement.
It passes only with three additions:

1. the document carries its own lineage, and a step in the plugin reads the entry **and its
   history** and puts the right version back or merges two (section 3);
2. a small entry that is only ever added, the **version witness**, makes a lost version noticed,
   so the gate refuses instead of serving the stale one (section 3.5);
3. revocations and retirements are, besides being in the document, **entries of their own that are
   only ever added**, because they must hold even after the operator has accepted a loss
   (section 4).

With those, in every measured cell the gate ended with the complete state of both copies or
refused. It never served a stale or partial state [M probe, 128 cells and 10 chains of twelve
rounds]. The price is the refusals: at a tie merged by a program without the plugin, the gate
answers nothing for that database until the operator has pressed a button, and what the lost
version changed is gone.

**"How would that handle a non-kpg program changing the tree?" Moves, renames and deletions are
handled better than today; copies are the hole.** The document names nodes by identifier, and
nothing another program does to a node can reach its rule. A moved node keeps its rule, a renamed
one is untouched, a deleted one leaves a rule that waits for it. But a copy has a new identifier
and no rule, so where the original's rule narrowed what its group grants, the copy is wider than
the operator meant. The design closes that for copies that land beside the original (section 5.3)
and cannot close it for a copy that lands somewhere else. Today a copy carries its rule
[M rules-central critique, P1], so this is a real step back, shared with every option that keeps
rules off the node.

**The honest comparison** (section 10): everything "all or nothing" buys is also bought by one
entry per version, as `option-wildcard.md` has it, and that shape does not depend on history, on
times or on a tie. What changing one entry in place adds is that the file holds one state entry at
all times and that nothing replaced is ever left behind to be brought back. What it gives up is
that two copies' versions do not both arrive: one arrives and the other has to be found in history.

## 1. The design on one page

1. **One ordinary entry holds the whole state**, in the plugin's own group, under a fixed
   identifier: the authority, every admission record, the profile names, the standing answers to
   renewals, every access rule (named by the node's identifier), the revocations and the
   retirements. One document, in one protected custom string, `KPG.Data`.
2. **The plugin changes that entry in place** and makes no history item when it does. Every merge
   in every program takes the entry whole.
3. **The document does not trust the merge's choice.** Each version has an identifier of its own
   and names the versions it has seen. Each datum carries the version it last changed in.
4. **A reconciling step** runs when the entry is read: it takes the current version, every history
   item that reads, and the version the plugin last served for that open database, and decides:
   the current one is the newest (nothing to do); a newer one is in history or in memory (serve
   it, put it back at the next save); two versions never saw each other (merge them datum by
   datum, with rules that cannot widen).
5. **A version witness**: with every version the plugin adds one tiny entry whose identifier
   derives from the version's, and removes the witnesses of the versions it replaces. Entries
   added in two copies both arrive in every merge. So when a merge lost a version, its witness
   still arrives, and the gate answers `503 undecided` until the version turns up or the operator
   continues without it.
6. **Revocations and retirements are also entries of their own**, added once and never changed.
   They count whatever the document holds.
7. **Nothing is written on a governed node.** A rule is a member of the document.
8. **A copy of a narrowed node is refused where it lands beside the original**: a group that holds
   a narrowed child keeps a list of the children it knows, and a child not on it is refused until
   the operator confirms it (section 5.3).
9. **Opening and reading write nothing.** What the step decides is served from memory and written
   with the next save of that database, as owed answers are today.
10. **The API cannot see or make any of it.**

In the ordinary state the group holds: the state entry, one version witness, one entry per
revocation whose certificate has not expired, one per retired authority.

**The honest weaknesses, first.**

- **It leans on history, the feature every ground report calls "not a store".** It leans on it for
  one item, for the time between a merge and the next time KeePass opens the file, and it knows
  when the item is missing. That is the whole defense, and section 3 measures it.
- **A refusal with nobody at fault.** Two copies that wrote in the same second, merged by a
  program without the plugin: the gate refuses until a press, and one side's changes are gone
  [M probe, S3, rounds]. So does an import with Keep existing, where the witness arrives and the
  document does not [M probe].
- **A copy of a narrowed node that lands away from its original is wider than its original**
  (section 5.3).
- **One deletion removes everything.** It is a clean refusal, and section 6.4 has four ways back,
  but no other shape has a cliff this steep except `wildcard`.
- **The authority's private key sits in an entry**, which every program lists, prints and
  exports (section 6.3).
- **A file written whole over another** (KeePassDroid's every save over a changed file,
  Keepass2Android's "overwrite", a synchronizer's conflict copy) takes everything in it back,
  witnesses included. No store inside the file survives that [R android 5.5].

## 2. The entry, the document, and what sits beside it

### 2.1 The group and the entries

The group is `option-store-entries.md`'s: fixed identifier `FE4040A9E0FAE884FA8BECAEEE9FBB46`
(computed again [M probe]), named `KeePassGate`, under the root, searching and auto-type off, one
line of notes, no subgroups, found by identifier and never by name.

```text
Group  KeePassGate                       (searching off, auto-type off)
  Entry  KeePassGate data                identifier 8AB03BFAEDE536C6078045089CEB0601, fixed
           User name, password, URL      (present, empty)
           Notes      KeePassGate keeps its settings for this database here. Do not edit or
                      delete this entry. Managed on the KPG tab in KeePass.
           KPG.Kind   state
           KPG.Data   (protected) the document
  Entry  KPG version 5d0c2a91            identifier derived from the version's identifier
           KPG.Kind   version
           KPG.Version 5d0c2a91c3e07b12
  Entry  KPG revoked client 9b1c2d3e     identifier derived from the certificate's digest
  Entry  KPG retired authority 3f2a77c0  identifier derived from the authority's digest
```

- **The state entry's identifier is fixed**: the first 16 bytes of SHA-256 over `KPG/1/state/`
  [M probe]. Two copies therefore hold the same entry to every merge, which is what makes the
  merge "one or the other". A state entry under any other identifier is a copy and counts for
  nothing (section 5.4).
- **A version witness's identifier** is the first 16 bytes of SHA-256 over
  `KPG/1/version/<version identifier>`. A witness counts only under the identifier its content
  derives, so a copy of one is not one [M probe, Create new IDs].
- **Revocation and retirement entries** are `option-store-entries.md`'s kinds 2 and 4, unchanged:
  derived identifier, `KPG.Kind` and a small `KPG.Data`.
- **Custom strings** because they are the one carrier every program keeps, flag included
  [M keepassxc 6; R android 2; M others]. Not an attachment: KeePassDX takes two attachments for
  one when 32 bits of their MD5 agree and loses attachments inside arriving history
  [R android 5.1, 5.2], and history is where this design finds a lost version.
- **One protected string, not two.** Protected because the document holds the authority's private
  key, because KeePass then keeps it encrypted in its process memory [R inventory H6], and because
  other programs then show dots and not 20 to 100 KB of text [A for the phones]. Every program
  read keeps a custom string's own protection flag [R android 2; M keepassxc 6], and 1 MiB of
  protected string came through KeePassLib and four KeePassXC releases intact [M wildcard].
- **No line break in the document.** KeePass's entry dialog reads Notes back from a rich text box
  [R keepass 2.3]; what it does to a custom string's line breaks was not read, so the document is
  one line.

### 2.2 The document

One flat JSON object of strings and arrays of strings, which is what `JsonBody` reads and
`JsonWriter` writes today [R inventory P24]. Only the ceilings move. A member's value begins with
its **stamp**, the identifier of the version in which that member last changed.

| Member | Value | Today's datum |
| --- | --- | --- |
| `v` | `1` | |
| `id` | this version's identifier: 16 random bytes, hex. Random and never served by any route, so a file that has not read this database cannot name it | |
| `gen` | one more than the highest generation among the versions this one was made from | |
| `seen` | the identifiers and generations of the versions this one descends from, the newest 256 | |
| `authority.<fp>` | today's `AuthorityDocument`, unchanged [R inventory D1] | D1 |
| `retired.<fp>` | stamp, `retired` | D2 |
| `client.<cert>` | stamp, today's admission record, unchanged | D3 |
| `revoked.<cert>` | stamp, `notAfter` and the issuing authority, as today | D4 |
| `refused.<cert>` | stamp, `forever` or a moment | D5 |
| `profile.<name>` | stamp. One member a name, so two copies that each add a name both keep theirs | D6 |
| `rule.<node>` | stamp, today's rule document as `AclDocument.Write` produces it | N1 |
| `closed.<group>` | stamp, the children that group knows (section 5.3) | new |
| `gone.<node>` | stamp. A rule removed for a node that no longer exists (section 5.2) | new |
| `sum` | SHA-256 over the canonical text of every other member | |

A document whose `sum` does not match does not read. That is what makes an edit by hand, a
truncation and a damaged protected value [R store-entries critique, P6] the same case: a version
that is not there.

**Size.** Today's data for one authority, ten clients, three revocations and rules on twenty nodes
is about 14 KB [M inventory 1.4]. Stamps add 17 characters a member and `seen` at most 6 KB. So
about 20 KB for that database, about 65 KB for one with 30 clients and rules on 200 nodes
(arithmetic on [M inventory 8]), and 16 characters more for every child a closed group lists.

### 2.3 Where each datum lives

| Datum | Where | Why there |
| --- | --- | --- |
| Authority (certificate, private key, host PKCS#12, gate key) | in the document | written once and the same in every copy, so it gains nothing from an entry of its own; inside, a record and the authority it chains to always come from one state. Section 6.3 has the cost |
| Admission records | in the document | a lost record fails closed: the client pairs again [R inventory P7] |
| Standing answers to renewals | in the document | the losing direction refuses and never admits [R inventory D5] |
| Profile names | in the document | a stale list coming back is noticed (section 3.5) or repaired (section 3.3) |
| Access rules | in the document, by node identifier | the point of the option |
| **Revocations** | in the document **and** one entry each, only ever added | section 4 |
| **Retirements** | in the document **and** one entry each, only ever added, never removed | section 4 |
| Which version is the newest | in the document's `seen`, **and** one witness entry | section 3.5 |
| Invitations, held approvals, session answers, owed answers, the responder, the audit, the port | unchanged, outside the file [R inventory 1.3] | |

## 3. The winner of a merge

### 3.1 What a merge leaves of two versions

Copy A and copy B start from one file holding version `D0`. A writes `A1`, B writes `B1`, as the
plugin writes: the string replaced, the time moved, no history item. Then one merge. "current" is
what the entry holds afterwards, "history" what its history holds. KeePass: `PwDatabase.MergeIn`,
four methods, both directions. KeePassXC: `merge -s`, four releases, both directions; the four
releases gave the same result in every cell. All [M probe].

| Case | KeePass Synchronize, Overwrite if newer | KeePass Overwrite existing | KeePass Keep existing | KeePassXC |
| --- | --- | --- | --- | --- |
| S1, S2: both wrote, ten seconds apart | the later current, the earlier in history, both directions | the arriving current, the open copy's in history | the open copy's current; the arriving one in history **only when it is the earlier**, otherwise nowhere | the later current, the earlier in history, both directions |
| S3: both wrote in the same second | **the arriving current, the open copy's nowhere** | the arriving current, the open copy's in history | the open copy's current, the arriving nowhere | **the target's current, the source's nowhere** |
| S4: A wrote; B holds `D0`, and its entry was edited by hand twenty seconds later (the title, with the history item programs make) | **`D0` current**, `A1` in history | into A: `D0` current, `A1` in history; into B: `A1` current | into A: `A1` current; into B: `D0` current, `A1` in history | **`D0` current**, `A1` in history |
| S5: A wrote; B unchanged | `A1` current, `D0` in history | into A: `D0` current, `A1` in history | into B: `D0` current, **`A1` nowhere** | `A1` current, `D0` in history |
| S6: A wrote; in B the document was damaged by hand later | an unreadable current, `A1` and `D0` in history | as S4 | as S4 | an unreadable current, `A1` and `D0` in history |
| S7: A wrote twelve times, B once in between | `A12` current, `B1` in history | | into B: `A12` nowhere | `A12` current, `B1` in history |
| S8: as S7, but A made a history item at each write, as the entry dialog does | `A12` current, ten of A's own versions in history, **`B1` nowhere** | into A: `B1` current, ten of A's in history | | `A12` current, ten of A's own in history, **`B1` nowhere**, all four releases |

What this shows:

- **The stale one wins by its time** (S4): an accidental edit on a phone makes the old state the
  current one. The newer state is then in history, in every cell.
- **A tie keeps one version nowhere** (S3), and the two programs keep opposite sides.
- **The plugin must not make history items.** With none of its own (S7), the loser is kept behind
  twelve later writes in every cell. With one a write (S8), KeePass's trim to ten items and
  KeePassXC's rule that a loser older than ten newer items is not added [R keepassxc 3] both drop
  it. This is why section 7.2 writes in place and lets history go.
- **Keep existing delivers the other copy's witness without its document**, since entries arrive
  under every method [M keepass 1d] and the entry's content does not.

### 3.2 What each program leaves in history, and for how long

| Program | Different times | Same second | When history is cut | Basis |
| --- | --- | --- | --- | --- |
| KeePass 2.62 | the earlier version, one item | nothing | at every load, every save and at the end of every merge, to `HistoryMaxItems` (10) and `HistoryMaxSize` (6 MiB), the oldest by time first; Database Maintenance deletes items older than 365 days on a press; the operator can set the limit to 0 | [M probe]; [M keepass 1b]; [R keepass 1b] |
| KeePassXC 2.4.2 to 2.8.0 | the earlier version, one item, unless the winner already holds ten later items | nothing | when that entry is edited there, in a merge whose joined history differs (2.7.12 and 2.8.0 not when the source wins), when the limits are changed; never at load or save | [M probe]; [M keepassxc 5] |
| Keepass2Android 1.15-r6 | the earlier version, one item, unless history holds an equal one | nothing: the arriving copy wins and the open copy's is lost | **at every load**, after every merge, on an edit; to the file's limits | [R android 1.1, 1.3]; one older version in history in 18 of 18 merges [M spike] |
| KeePassDX 4.5.8, 3.2.0 | the earlier version, unless an item has the same second | nothing: the open copy wins | only when that entry is edited in KeePassDX, or the setting is changed | [R android 1.1, 1.3]; 18 of 18 [M spike] |
| KeePassDroid 2.7 | has no merge; every save writes its copy over the file | | on an edit of that entry | [R android 1.3, 4.2]; [M spike] |
| kdbxweb, Strongbox, KeePassKit, keepass-rs | the earlier version in history | kdbxweb and KeePassKit keep the open copy and lose the other; Strongbox and keepass-rs file it | Strongbox at every save, kdbxweb at every merge by count | [M others 4]; [R others 4] |

**How long does this design need the item?** From the merge to the next time KeePass with the
plugin reads the file. The step then takes the version into the current document and lets the
history go at its next write. In ten chains of twelve rounds the state entry never held more than
one history item [M probe, rounds]. What can remove that one item first:

- a **history limit of 0** set by the operator: the merge's own trim removes the loser before any
  plugin can look [M keepass 1e; M probe, rounds];
- a **document larger than the history size limit**: at 7,000,000 characters under the default
  6 MiB the loser was gone after the save [M probe]. Documents of 100,000 and 1,000,000 characters
  kept it, in KeePass and in KeePassXC 2.4.2 and 2.8.0, both directions [M probe];
- a person **deleting history items** in any program's history view;
- **ten later edits of the state entry by hand** in another program before KeePass sees the file;
- **an item with the same second** already in history, which every merge treats as the same item
  [M keepass 1b; R keepassxc 3; R android 1.3].

Each of these is the case of section 3.5: the witness is still there.

### 3.3 The reconciling step

**Candidates**: the current version if it reads, every history item that reads, and the version
the plugin last served for this open database (it keeps that document in memory while the database
is open and lets it go at lock and close). **Heads**: the candidates no other candidate has seen.
**Witnesses**: the version entries, under their derived identifiers.

| What the step finds | What the gate does |
| --- | --- |
| No candidate reads, or the state entry is absent or in the recycle bin | serves nothing for this database (section 6.4) |
| A witness names a version that is no candidate and that no head has seen | **a version is missing**: `503 undecided` to every request, and the operator is asked (section 3.5) |
| One head, and it is the current version | nothing |
| One head, and it is in history or in memory: **a stale state won** | serves the head at once; puts it back as the current version, with a new time and the same identifier, at the next save |
| Several heads: **both copies wrote** | merges them (section 3.4), serves the result at once, writes it at the next save |

Measured over the 128 cells of section 3.1 [M probe]:

| | Complete state of both copies | Refused, a version missing | Stale or partial state served |
| --- | --- | --- | --- |
| KeePass, the merge ran without the plugin (64 cells) | 47 | 17 | 0 |
| KeePass, the plugin loaded and remembering the open copy (64 cells) | 54 | 10 | 0 |
| KeePassXC, read at the next load in KeePass (64 cells) | 48 | 16 | 0 |

The ten that remain with the plugin loaded: eight Keep existing imports, and S8's two cells where
the arriving loser was trimmed, which the plugin's own way of writing never produces. The sixteen
KeePassXC refusals are S3 and S8, all four releases and both directions.

**What the plugin's memory buys.** In KeePass a tie loses the open copy's version, and that is
exactly the one the plugin holds. With it, twelve rounds of two copies writing in the same second
and synchronizing in KeePass ended with 24 of 24 data and no refusal, and so did twelve rounds
under a history limit of 0 when the arriving version was the later [M probe, rounds]. Memory does
nothing for a merge made by another program.

**Where the step runs.** On every read, which is every request: the entry is found by its
identifier, and when its string object and its history are the ones seen last time the previous
answer stands. KeePass hands back the same string object for an unchanged entry [M probe]. After a
synchronize the first read is the save's: `FileSavingPre` is raised after `MergeIn` and before
either file is written [R keepass 4, not run], so the merged version reaches both files. That is
where owed answers are written today [R plugin, `Bridge/OwedAnswerSaves.cs:12-13`, `:51-52`]. No
read starts inside a merge [R plugin, `Bridge/MergeWatch.cs`]. **At open nothing is written**: the
decision is served from memory and written with the next save or the plugin's next write, so
"opening changes nothing" holds [R inventory O1].

**A merge version is a function of its parents**: its identifier derives from theirs and its
content from theirs. Two copies that reconcile the same pair on their own write the same document,
and the next merge finds nothing to do.

### 3.4 Merging two versions that never saw each other

Datum by datum, by the stamps. `seen(X)` is what version X descends from.

| The two versions hold | Result |
| --- | --- |
| the same stamp | that value |
| different stamps, and one version has seen the other's stamp | the value of the version that saw the other: it is the later decision |
| different stamps, neither seen by the other: **both copies changed it** | by kind, below |
| the datum in one version only, and the other has seen its stamp | absent: the other removed it |
| the datum in one version only, and the other has not seen its stamp | present: it was added there |
| a stamp older than what `seen` still holds (more than 256 versions back) | by kind, the narrow way: a rule's node refuses, a record and a profile name are dropped |

| Kind, when both copies changed it | Result | Can it widen? |
| --- | --- | --- |
| `rule.<node>` | what both allow: a profile both name keeps the words both give, a profile one names keeps nothing, `deny` if either denies (the meet `option-rules-central.md` defines) | no |
| `profile.<name>` | present only if both hold it | no |
| `client.<cert>` | the emptied one if either is emptied | no |
| `refused.<cert>` | the stricter: `forever`, then the later moment | no |
| `revoked.<cert>`, `retired.<fp>`, `gone.<node>` | present if either holds it | no |
| `closed.<group>` | closed if either closes it; the list is the union, since each child on either list was confirmed by an operator's act in that copy | section 5.3 |
| `authority.<fp>` | present if either holds it; two unretired authorities serve nothing, `503 undecided`, and the operator is asked, as today [R inventory P19] | no |

Measured: A narrows `X`, changes `Z`, pairs a client and removes a profile; B widens `Y`, changes
`Z` another way, pairs a client and adds a profile. In every cell that did not refuse, the result
was A's narrowing, B's widening, the meet at `Z`, both clients, B's profile, and A's removed
profile gone [M probe, S1 to S3, S7, S8]. Over all eight scenarios, every one of the 149 output
lines that carries a document carries the complete one. The stamps are what make this a merge of decisions and not of texts: without them a
datum that differs could not be told from one that only one side changed, and every difference
would have to be narrowed.

**A foreign file cannot take the state over.** A document from another database, or one made by
hand, has not seen this database's newest version and cannot name it, since version identifiers
are random and served by no route. Merged in under the fixed identifier with a later time, it is
at most a second head: its rules can only narrow, a second authority ends in `503 undecided`, and
a planted record chains to an authority this database does not serve. If it arrives so that this
database's version is lost (a tie, a crafted history), this database's witness is still there and
the gate refuses. Compare `option-rules-central.md`, where one planted rule entry opens a node
below a seal [M rules-central critique, S1]. What a foreign file can still do is narrow, and plant
a revocation entry (it denies, section 4).

### 3.5 When the loser is nowhere to be found

The witness of the lost version is in the file, because it is an entry only one copy held and
every merge adds those [M keepass 1d; M keepassxc 4; M probe, 128 cells]. Android: read, not
measured [R android 1.5].

- The gate answers every request for that database `503 undecided`, the word a database with two
  authorities answers with today [R inventory P19].
- KeePass asks over its main window, at open, at unlock and after the save that ends a
  synchronize, never from a connection: "A change to KeePassGate's data made in another copy of
  this database did not arrive. Synchronize with that copy, or continue with what is here."
- **Synchronize with that copy** is the remedy when the version still exists somewhere: the
  moment a file holding it is merged in, the step finds it and merges.
- **Continue with what is here** is a press on the KPG tab, applied at the settings dialog's OK
  like everything there. It writes a version that names the missing one as seen. What the missing
  version changed is gone: rules, profile names, pairings, answers. The page says so, and that
  revocations and retirements are not affected. That last sentence is what section 4 is for.

Twelve rounds with every write in the same second, merged by KeePassXC or by a KeePassLib merge
with no plugin: one side's twelve data were lost, and every loss was refused at the next load
before anything was served [M probe, rounds]. With the writes five seconds apart: 24 of 24 and no
refusal, in either order through KeePassXC and in the one order run through KeePassLib.

**Not noticed by anything**: a whole file written over another. The witnesses go with it.

## 4. What must never be undone

A stale document that wins while the newer one is gone takes back everything the newer one
changed. The witness turns that into a refusal. But the operator's answer to the refusal is
"continue with what is here", and at that moment the operator cannot be asked to remember which
client was revoked in the lost version. A revoked certificate is in somebody else's hands, and a
retired authority's certificates are too. Requirements E3 and E4 are absolute [R inventory 5.1].
A rule, a profile name and a pairing are different: the operator can look at them on the tab and
set them again, and nothing was handed out that cannot be taken back.

**So this design departs from "one entry" in exactly two data, and says so:**

- **A revocation** is written as an entry of its own in the same write as the document, with a
  derived identifier, added once and never changed. A certificate is refused when the document
  holds its revocation, **or** that entry is found anywhere in the tree, **or** a deletion record
  names that entry's identifier. The entry goes with the prune once the certificate has expired,
  as the key does today.
- **A retirement** likewise, and it is never removed.

This is `option-store-entries.md`'s refusing record, with one difference that answers its critic's
first problem (one deletion by hand takes away the only witness [M store-entries critique, P1]):
here the entry is the second witness. The first is the document, and deleting the entry by hand
changes nothing while the document holds the revocation. A revoked client comes back only if the
entry is removed with no trace **and** a stale document wins **and** its loser is lost **and** the
operator presses continue.

**The version witness is the third kind of entry beside the state.** It is forced by the same
requirement from the other side: a narrowing lost without notice (E5, E6). It holds no data.

Everything else stays in the one entry.

## 5. The tree changing under the document

The document is merged whole. The tree is merged node by node, by programs that know nothing of
the plugin. So after a merge the tree can be newer or older than the state the document describes.

### 5.1 Case by case

| The tree changed | What the gate answers | Wider than the operator meant? |
| --- | --- | --- |
| **A node moved** (its identifier stays, its ancestors change) | its own rule follows it, measured for a rule kept apart from its node [M rules-central, scenario 10]; what it inherits is its new place's | no more than today: a node with no rule of its own, moved out of a sealed group, is governed by where it lands, today too. Moved **into** a closed group it is refused until confirmed |
| **Renamed** | nothing changes. The document holds identifiers, never names | no |
| **Deleted into the recycle bin** | the API does not see the bin [R inventory P15]. The rule stays in the document | no |
| **Deleted for good** | the rule stays in the document, asked about by nobody | no |
| **Brought back**, from the bin, or by a merge with a copy that still holds it (every KeePassXC merge brings a deleted entry back [M keepassxc 2]) | its rule is still there and applies | no. This is why a rule is not removed with its node |
| **Created on a phone**, in a group that is not closed | inherits, as today | no |
| **Created anywhere in a closed group**, other than through KeePass's Add Entry dialog or the API | refused until confirmed on the group's KPG tab | no; it costs a press |
| **Copied** | section 5.3 | **yes**, when the copy lands outside a closed group and the original's own rule narrowed |
| **A tree newer than the document**: nodes added, moved or deleted on a phone, the document untouched | each node as its row above. Nothing in the document has to change for a move or a deletion | as the rows above |
| **A document newer than the tree**: a stale copy's nodes merged under the newer copy's document | rules for nodes the tree lacks wait; nodes the newer copy deleted and the merge brought back find their rules | no |
| **A document older than the tree**: the stale document won | section 3: put back, merged, or refused. Until then nothing is served from the stale document if its witness says a newer one exists | no |
| **Imported with Create new IDs** | section 5.4 | **yes** for the arriving nodes, unless the root is closed |

### 5.2 A rule for a node that is gone

It is kept. The plugin never removes one on its own, because a merge can bring the node back and
a node that came back without its rule would inherit, which is the widening direction. Each costs
about 130 characters. The operator can remove them on the tab, and removal writes `gone.<node>` in
place of the rule: 50 characters, and a node on that list that comes back **refuses** until the
operator has set its rule again. So removal never turns into silence. `option-rules-central.md`
measured the other choice: a rule removed after its node, and the node back from another copy with
an older rule or none [M rules-central, scenarios 13, 14].

### 5.3 Copies

A copy has a new identifier, and no rule names it.

| How the copy is made | Where it lands | Basis |
| --- | --- | --- |
| KeePass, Duplicate Entry | the original's own group | [R 2.62 `KeePass/Forms/MainForm.cs:941-955`]; `PwEntry.Duplicate` gives a new identifier and a new creation time [R 2.62 `KeePassLib/PwEntry.cs:848-864`] |
| KeePass, Duplicate Group | beside the original, under the same parent, everything in it under new identifiers | [R 2.62 `MainForm.cs:2318-2330`, `KeePassLib/PwGroup.cs:1437-1447`] |
| KeePass, a group dragged with Ctrl | the group it is dropped on | [R 2.62 `MainForm.cs:1317-1321`] |
| KeePass, copy and paste of entries; Add Entry from a template | the group that is open [A] | that the copy has a new identifier [R keepass 9] |
| KeePassXC, Clone | the original's group [A] | that it copies the entry's strings [R rules-linked 2.2] |
| Keepass2Android "copy entry"; KeePassDX copy and paste, a whole group included | the group chosen [A] | that each makes a copy under a new identifier [R rules-linked 2.2] |
| An import with new identifiers | groups added at the root | [M keepass 2.4] |

Today the copy carries the original's rule in every one of these [M rules-central critique, P1,
for KeePass's two `Duplicate` calls]. Here it carries none, so it is wider than its original
wherever the original's own rule narrowed what its place grants. The mitigations, with what each
closes:

1. **Closed groups: the list of known children.** When the operator gives a node a rule that
   narrows what it would inherit, the grant editor closes the node's **parent**: it writes
   `closed.<parent>` holding the identifiers of everything in that group. From then on a node in
   that group, at any depth, that the list does not hold is **refused**, there and below, until
   the operator confirms it on the group's KPG tab ("2 items here are not served yet"). An entry
   made through KeePass's Add Entry dialog is added to the list at that dialog's OK, and one made
   through the API in the write that makes it, so the ordinary ways of adding cost nothing. The
   operator may close any group by hand, the root included.
   - **Closes**: Duplicate Entry, KeePassXC's Clone, a copy made on a phone into the same group,
     a paste into the same group, Duplicate Group of a narrowed group (the copy lands under the
     original's parent, which is the closed group), a node moved in.
   - **Does not close**: a copy pasted or dropped into a group that is not closed; a copy made in
     a closed group and moved out before KeePass next opens the file.
   - **With the root closed**, every node the document has never seen is refused wherever it is,
     and the hole is shut. The price: nothing added on the phone is served until it has been
     confirmed in KeePass, and the list holds every node, 16 characters each, so about 32 KB for
     2000 nodes.
   - **What it costs in the document**: one member a closed group. Two copies that each confirmed
     children merge to the union (section 3.4).
2. **The policy that narrowing belongs on groups.** A copy made inside a narrowed group inherits
   the narrowing. The documentation would advise granting low in the tree and sealing groups, not
   entries, and the grant editor would say so on an entry's narrowing rule. Advice, not a control.
   It does not cover a copy of the narrowed group itself, which mitigation 1 does.
3. **A back reference on the node**, as `option-rules-linked.md` has it: a string on a ruled
   entry, a link entry inside a ruled group, which a copy carries along, so that a copy betrays
   itself and gets the original's rule as a limit [M rules-linked, 16 cells]. It composes with
   this design: the reference would name the node's identifier, and an entry carrying a reference
   to an identifier that is not its own would be limited by that node's rule. It would close the
   copy that lands elsewhere. It is not part of this design, because it writes on governed nodes
   and puts an entry into every ruled group, which is what `single-entry` set out to avoid.
   If copies that land elsewhere have to stay narrowed, this is the piece to add.
4. **A copy's creation time is not used.** KeePass stamps a duplicate as created now
   [R 2.62 `PwEntry.cs:848-854`]; whether the other programs do was not read, and a time is what
   failed before.

### 5.4 A merge under Create new IDs

Copy B, holding a later version, imported into copy A with Create new IDs: the result held two
entries of kind `state` (one under the fixed identifier, one copy), two groups named
`KeePassGate`, and a copy of B's witness under another identifier. Under the fixed identifiers
nothing had changed: A's version, A's witness, no refusal [M probe]. So the import cannot change
the state, and a foreign database imported this way cannot either.

The arriving groups and entries are new nodes at the root [M keepass 2.4]. No rule names them:
they get what the root grants, and are refused only if the root is closed. The gate can see that
it happened, since a state entry under another identifier exists only after such an import or a
duplication of the plugin's own entry. While one exists the KPG tab says so and names the remedy
(close without saving, or delete what the import added). Whether that should also stop the gate,
as `option-rules-central.md` does with `503 undecided`, is a decision for the project; its critic
found the stop too blunt and the "remove copies" button an exposure
[R rules-central critique, S4, M3].

## 6. The entry itself

### 6.1 Found, and what it costs to read

By identifier, anywhere in the tree: 0.03 ms a look-up with the store group first among 10,002
entries, 0.12 ms for the last entry of 10,002 [M probe]. Then the string object is compared with
the one seen last time [M probe: the same object is handed back]. Only when it changed is the
document read: 0.03 ms for SHA-256 over 50,000 bytes, 0.01 ms to take 50,000 characters out of
the string [M probe; PowerShell around .NET calls, so upper bounds]. Parsing 65 KB of JSON in the
plugin was not measured.

Then, a request: the witnesses in the store group, one look-up of the caller's revocation entry
by its derived identifier, and the deletion records for that identifier. `AclResolver` asks the
document's index by the node's identifier where it asks the node's custom data today, as
`option-rules-central.md` section 4 describes. No walk of the whole tree is needed, unlike the
designs that must look for rule entries everywhere.

### 6.2 Size and history growth

The entry is rewritten whole at every pairing, renewal, revocation, rule edit and profile edit:
20 to 65 KB (section 2.2). The plugin's write makes no history item and lets the entry's history
go, so in the ordinary state the file holds the document once. After a merge of two copies that
both wrote it holds it twice until KeePass next saves. A hand edit of the entry in another program
adds one item, which the plugin's next write lets go.

### 6.3 What each program shows of it and does to it

| Program | A save | An edit of the entry by hand | What a person sees | Basis |
| --- | --- | --- | --- | --- |
| KeePass 2.62 | kept; KDBX 4.0 is enough for entries and strings | the entry dialog makes a history item and moves the time; then S4 or S6 | a group with two entries or more; the document as a protected string on the Advanced tab; listed by the large-entries report | [M keepass 2.1, 9]; [M probe] |
| KeePassXC 2.4.2 to 2.8.0 | every string, flag and history item kept; nothing added to an entry that has its standard fields | `edit` makes a history item, moves the time and trims history to the limits | protected attribute, hidden until revealed; HTML export prints it in clear | [M keepassxc 6, 8]; [M probe, every cell is a KeePassXC save] |
| Keepass2Android | custom strings and their flags kept; history cut to the limits at load | a history item, a new time [A] | [A] a hidden field with a reveal | [R android 2, 1.3] |
| KeePassDX 4.5.8, 3.2.0 | custom strings kept | history cut when edited there; moving it to the bin and back moves its time, which is S4 | [A] | [R android 2, 1.4] |
| KeePassDroid 2.7 | custom strings kept; it deletes node custom data, which holds nothing of ours | [A] | [A] | [R android 2]; [M spike] |

- **The authority's private key is in this entry.** KeePass's Copy on a custom string, its print
  with "custom strings" ticked, its KDB export and KeePassXC's HTML export all write a protected
  string in clear [R store-entries critique, S3]. Today the key is in custom data, which only the
  XML export shows [R inventory P26]. This is a real cost of moving the authority into an entry,
  and this option cannot avoid it and stay one entry. The alternative, leaving the authority in
  the database's custom data, fails the hard requirement the other way: a KeePassXC merge
  replaces that whole dictionary from the copy loaded later [M spike section 5].
- **KeePass's own tools**: Delete Duplicate Entries compares strings, and no two of these entries
  have equal strings; Delete Empty Groups finds the group never empty; search, reports, expiry
  warnings and auto-type skip a group with both switches off [M keepass X10, X11; R keepass 9].

### 6.4 Deleted, moved to the bin, edited by hand

| A person, in any program | What the gate does | The way back |
| --- | --- | --- |
| Edits the title, notes or icon | nothing: the document is unchanged, and the lineage is the document's, not the entry's time | none needed [M probe, S4] |
| Edits or cuts the document | that version does not read; the step takes the newest version from history | automatic [M probe, S6: recovered in all 16 cells] |
| Restores an older history item | KeePass makes a history item of the current version first [R 2.62 `KeePassLib/PwEntry.cs:602-615`], so the newer is in history and is put back. Other programs [A] | automatic in KeePass |
| Moves the entry to the recycle bin | **serves nothing for this database.** Counting it in the bin would leave a gate running that the operator believes deleted | restore it from the bin in any program, or **Put it back** on the KPG tab |
| Moves it to another group | nothing: found by identifier | |
| Deletes it for good | serves nothing. The witness and the revocation entries stay | below |
| Deletes the whole group for good | serves nothing | below |
| Duplicates it | the copy counts for nothing | |

Serving nothing is the state of a database with no authority: no host certificate, so no TLS for
that database and nothing to answer. It is a clean refusal. **It is also everything gone at once**:
every client's record, every rule, the authority. What a merge then does [M probe]:

| The state entry deleted for good in copy A | KeePass Synchronize | KeePass Overwrite if newer | KeePassXC 2.8.0 |
| --- | --- | --- | --- |
| B unchanged, or B wrote before the deletion | **gone in both copies** | stays in B | comes back into A, stays in B |
| B wrote after the deletion | back in both, with B's version | back | back |
| A moved it to the bin, B wrote later | in the bin in both, with B's version | | in the bin in A |

In the last row KeePassXC, merging into B, left the entry in an ordinary group named "Recycle
Bin" that is not B's bin [M probe; M keepassxc 4], so that copy goes on serving while A does not.

So one deletion for good followed by a KeePass synchronize removes the state from every copy that
synchronizes. The ways back:

1. **The recycle bin**, when the deletion went there: every program in question moves a deleted
   entry to the bin first [R android 4.2; R keepass 9].
2. **The plugin's memory**, when it happened in KeePass in this session: the KPG tab offers
   **Put it back**.
3. **Another copy or a backup.** A plain synchronize with a backup does **not** bring it back:
   the open file's own deletion record deletes the older arriving entry again [M probe]. So the
   plugin offers **Restore from a file**: it reads the state entry out of a file the operator
   picks and writes it under the fixed identifier with a time after the deletion. Measured: an
   entry put back that way survived a synchronize with the backup in both directions, and the
   deletion record was dropped [M probe].
4. **A KeePassXC merge** with any copy that still holds it brings it back by itself [M probe].

If no copy holds it, the operator creates the authority again and every client pairs again. The
revocation and retirement entries are still there, which matters only for a stale copy turning up
later.

### 6.5 Keeping the API and the reference client out

As `option-store-entries.md` section 9 and `option-rules-central.md` section 9, with less to hide:

1. The store group and everything in it is absent, as the recycle bin is: not listed, counted,
   tagged or addressable, answered with the `404` of a refused node.
2. An entry anywhere that carries a string whose name begins `KPG.` is absent the same way.
3. No route makes or names a string beginning `KPG.`, compared without regard to ASCII case.
4. No route lets a caller choose an identifier, so no client can make the fixed one or a derived
   one.
5. A group that holds the store group cannot be removed through the API.

No route names a version identifier, a stamp or a list of known children. The reference client
changes nothing. A governed group is empty when it is empty, since nothing of the plugin's sits in
it.

## 7. The read path and the write path inside KeePass

### 7.1 Reading

Section 6.1. What grants is still read live on every request: the string object's identity is
checked each time, and a changed object is read again. Nothing is cached that the next request
does not check against the live entry [R inventory E14].

### 7.2 Writing: the single writer

Every write of the plugin's rewrites the one entry. `DatabaseWriter` stays the only place a
request writes [R inventory P13], and a write is one turn on KeePass's thread:

1. **Refuse while something would undo it**: KeePass's busy signals and progress window, as
   today; an entry dialog open on the state entry or on one of the plugin's small entries; Edit
   Entries.
2. **Reconcile**, so that the version to be written descends from every head.
3. **Apply the change to the members.** The classes above the store keep their seam:
   `IKeyValueStore` is `Get`, `Set`, `Remove` and `Keys` [R plugin,
   `Authority/IKeyValueStore.cs:18-30`], and `IGateDatabase.PluginCustomData` hands it out
   [R plugin, `Ui/IGateDatabase.cs:9-38`]. A store over the document's members answers the same
   four calls, so `AuthorityStore`, `AdmissionStore` and `ProfileNames` keep their logic under new
   key names. `ProfileNames` loses its one-value list.
4. **Make the version**: a new identifier, the generation, `seen`, stamps on what changed, `sum`.
5. **Replace the string, move the time, make no history item, let the history go.** The time is
   the later of now and one second past the entry's own and its newest history item's, in whole
   seconds. The time decides nothing in the plugin; it only makes the common case arrive as the
   current version. The store group's own time is not touched [R inventory E13].
6. **Add the witness** of the new version and delete, with deletion records, the witnesses of
   every version it has seen. For a revocation or a retirement, add its entry.
7. **Save, and believe it on two things**, as today: `Modified` false and the file hashing to
   `HashOfLastIO`. Then read the entry back and compare the version's identifier.
8. **A write that did not land is taken back**: the previous string object, time, history and
   witnesses are put back, which is cheaper than today's key-by-key undo.

A pairing and a renewal therefore hand over a certificate only after the document holding the
record is in the file [R inventory E7]. A revocation adds its entry and changes the document in
one save. Witnesses that a merge without deletions brought back are recognized (the current
version has seen them) and removed again at the next write. After twelve rounds the file held one
witness in every chain [M probe, rounds].

### 7.3 The settings tab

`PendingDatabase` buffers until the dialog's OK and replays key by key, with its reconcilers for
the keys that have second writers [R inventory P9]. That stays, over members in place of custom
data keys: the replay at OK is one turn of the writer and one new version. A pairing that lands
while the dialog is open has written a version of its own; the replay is applied on top of it,
member by member, and never as a document written back whole. The page for **Continue with what
is here** and **Put it back** is a fourth page, as `option-rules-central.md` adds one for rules.

### 7.4 The grant editor on the group and entry dialogs

The rule is no longer the node's, so no dialog holds a copy of it and the reflection on
`m_sdCustomData` goes [R inventory P10]. As `option-rules-central.md` section 5.1 works out: the
tab keeps the change in a buffer of its own, a handler on the dialog's `FormClosed` applies it
when the result is OK and drops it otherwise, and the plugin marks the database modified, since
KeePass does not see a changed entry. Applying is a new version in memory, not a save: KeePass
saves when the operator saves, as today. Two gains come with it: a rule change no longer makes a
revision of the user's entry or moves its time, and restoring a revision of an entry no longer
brings an old rule back [R inventory N1]. The cost its critic found stays: the question KeePass
asks on Escape cannot see the tab's buffer [R rules-central critique, M1].

Narrowing a node closes its parent in the same version (section 5.3), and Add Entry's OK in a
closed group adds the new entry to the list.

### 7.5 Somebody opens the state entry in KeePass

KeePass's entry dialog copies the strings and the history when it opens [R keepass 4] and assigns
both back on OK; when nothing was edited it then finds the entry equal to the copy taken at open,
puts the old modification time back and removes its own history item
[R 2.62 `KeePass/Forms/PwEntryForm.cs:1066-1152`]. So a plugin write made while that dialog is
open would be undone by an OK that changed nothing, silently, with the old time: the `GroupForm`
trap, on the one entry that holds everything. Two guards:

- the writer refuses with `503 busy` while an entry dialog is open on the state entry, through
  `GroupDialogs`, which already tracks entry dialogs by node [R inventory P11];
- if it happens all the same, the witness of the undone version is an entry of its own, which the
  dialog does not hold, and the plugin's memory holds the version: the step puts it back.

Read, not run: it needs KeePass's window.

## 8. Program by program

| Program | Save | Merge of the state entry | Basis for the merge | The gate afterwards |
| --- | --- | --- | --- | --- |
| KeePass 2.62, plugin loaded | kept | section 3.1; the open copy's version is always in memory | **measured**, 64 cells and five chains [M probe] | complete, except Keep existing (refuses until a synchronize) |
| KeePass 2.62, no plugin at the merge (another machine) | kept | section 3.1 | **measured** | complete, or refuses at a tie |
| KeePassXC 2.4.2, 2.6.6, 2.7.12, 2.8.0 | kept, 100,000 and 1,000,000 characters included | section 3.1; four releases alike | **measured**, 64 cells, three chains, large documents [M probe] | complete, or refuses at a tie |
| Keepass2Android 1.15-r6, 1.08d-r5 | custom strings kept **read**; entries as wholes **measured** [M spike] | a port of KeePassLib's Synchronize: the later whole, the earlier in history, the arriving copy at a tie; history cut to the limits at every load | **read** [R android 1.1, 1.3]; the newer whole and one older version in history in 18 of 18 [M spike], for an entry that differed in custom data. The KeePassLib chains without a plugin are the shape of it, not a measurement of it | **assumed**: as KeePass without the plugin |
| KeePassDX 4.5.8, 3.2.0 | custom strings kept **read** | the later whole, the earlier in history; the open copy at a tie; arriving deletion records applied and not kept; 3.2.0 adds an arriving entry only if its group exists | **read** [R android 1.1, 1.5, 3.1]; 18 of 18 [M spike] for the same kind of entry | **assumed**: complete, or refuses at a tie. A witness removed by an arriving deletion record is one the newer version has seen |
| KeePassDroid 2.7 | custom strings kept **read**; kept every entry **measured** [M spike] | no merge. Every save writes its copy over a changed file | **read** [R android 4.2] | **not solved**: a stale file over a newer one loses everything since |
| kdbxweb, Strongbox, KeePassKit, keepass-rs | strings kept [M others; R others] | the later whole, the earlier in history | [M others] for kdbxweb, [R others] | **assumed**: as KeePassXC |

## 9. What changes in the plugin

| Class | Change |
| --- | --- |
| new `Authority/StateDocument` | the flat document: read, `sum`, lineage, stamps, the merge of section 3.4 |
| new `Bridge/StateStore` | finds the entry, the step of section 3.3 with its memory, the witnesses, the `IKeyValueStore` over members, the write of section 7.2 |
| `CustomDataStore` | goes for the database's custom data |
| `AuthorityStore`, `AdmissionStore`, `RenewalRefusal` | key names; revocation and retirement also add an entry; presence of a revocation is asked in three places |
| `ProfileNames` | one member a name; `Merge` moves into the document's merge |
| `AclDocument` | `CustomDataKey` goes; gains the meet |
| `AclResolver`, `KdbxNodes` | the document's index answers for a node; the closed-group test on the walk |
| `NodeDialog`, `IGrantStore`, `GrantPanelFactory` | section 7.4 |
| `GroupDialogs` | watches entry dialogs on the plugin's own entries; stops mattering to rules |
| `DatabaseWriter` | section 7.2; the undo is the previous string |
| `PendingDatabase`, `PendingStore`, `PendingChanges` | over members; the fourth page |
| `OpenDatabaseTargets` | `503 undecided` gains a second cause; a database whose state entry is absent or in the bin has no target |
| `OwedAnswerSaves` | also writes what the step decided |
| `KdbxVault`, `VaultHandlers` | the guard of section 6.5 |
| Tests | `CustomDataMergeRuleTests`, `SnapshotHazardTests` and the rule cells of `MergeInRealDatabaseTests` give way to tests pinning section 3.1 against the pinned KeePass |

## 10. Compared with the many-entries designs

**What all-or-nothing buys.**

- **No mixtures.** A rule set from one copy never meets a profile list from another by accident.
  The many-entries designs get consistency from each record's own rules, and their critics found
  the seams: a profile's generation naming two different acts [R store-entries critique, P2], a
  repair that writes the older rule [R rules-central critique, S2].
- **One thing to check.** One `sum` over everything. A person or a program cannot delete one rule,
  undo one revocation or bring back one profile name. They can keep the entry, lose it, or leave
  an older version in its place, and each of those is noticed.
- **One thing to hide, one thing to look at.** One group with a handful of entries, where
  `option-rules-central.md` holds an entry for every change of every rule that the operator is
  told never to delete [R rules-central critique, S3].
- **An unambiguous refusal when it is absent.** No state entry, no gate.
- **Nothing replaced is left behind to return.** The designs that add an entry a version must
  never remove the old ones, or must recognize them when a merge without deletions brings them
  back. Here an old version exists only as a history item, which counts for nothing unless the
  lineage makes it a head.
- **A foreign file cannot take over or open a node** (section 3.4).
- **No walk of the tree on every request.**

**What it gives up.**

- **Two copies that both wrote do not both arrive.** One arrives; the other has to be found in
  the entry's history, and is found there only when the two times differ, history is allowed, and
  nothing cut it first. In the many-entries designs and in `wildcard` both arrive in every merge,
  with no time and no history read at all. This is the whole difference, and it is why this
  design needs the lineage, the memory, the witness and the refusal that the others do not.
- **A refusal where the others just work**: a tie merged by another program; Keep existing.
- **Every write rewrites everything**: 20 to 65 KB at each pairing, against one small entry.
- **The cliff**: one deletion, one damaged string, and the whole gate for that database stops.
- **The copy of a node** is not covered, as in every design that keeps rules off the node.

**Against `option-wildcard.md` in particular**, since it is the same document: `wildcard` writes
each version as a new entry and deletes the one it replaces. It needs no history and has no tie.
It pays with replaced snapshots that come back through merges without deletions and have to be
told from live ones by deletion records, and with a rollback when the newest snapshot is deleted
with no record. This design has neither of those, and has the refusals at a tie that `wildcard`
does not. **If "one entry" means "one state, taken whole", `wildcard` is the stronger way to get
it. If it means "one entry in the file, ever", this is the design, and its price is the refusal at
a tie.** A middle form exists and was not designed here: this design's fixed entry, with the
witness carrying the whole document, so that a lost version is recovered from its witness and not
only noticed.

## 11. Settled decisions: reversed and kept

**Reversed, each to be recorded.** The database's own `CustomData` as the store, and with it the
whole bullet about per-key times, add-only keys and `AuthorityDocument` as one key; "the authority
takes the file to KDBX 4.1" (4.0 is enough [M keepass 2.1]); one document per node under a `KPG`
key, and "the grant editor writes into the dialog's own copy"; "the plugin writes no rule of its
own"; a copy of a node carries its rules (`docs/access-control.md`); `KPG.Profiles` as one value
replayed as a difference (the difference is now the members'); `503 undecided` gains a second
cause; three pages on the KPG tab become four; the authority key leaves custom data for an entry;
a rule that is absent is no longer always silence (a node in a closed group, a node on the `gone`
list).

**Kept.** Every database its own authority, inside itself; no host registry and no sidecar; the
admission record load bearing and read on every request; claims carry profile names only; no
passphrase on the authority key; tokens and held approvals in memory only; nearest wins per
profile, the rule document and its ceilings, `CRUSDLAP`; the profile list decides what a client
holds; fail closed; the single writer and the save believed on two things; everything on the
settings tab waits for OK; OK keeps a rule and Cancel discards it; opening and reading change
nothing; one JSON grammar and one escaper; no route reaches any of it; local code execution and
the holder of the master key conceded.

## 12. What it does not solve

- **A whole file written over another.** KeePassDroid, one of the Android programs in scope, does this at
  every save over a file that changed underneath it [R android 4.2]. Everything written since
  that copy was taken is gone, with no sign.
- **A tie merged without the plugin**: refused, and one side's changes lost.
- **A copy of a narrowed node outside a closed group.**
- **A node with no rule of its own moved out of a sealed group** by another program: governed by
  where it lands, as today.
- **The state entry deleted for good and synchronized**, with no backup: everything is set up
  again.
- **The authority key's exposure** to print, export and the clipboard.
- **A stale copy on its own** serves what it has.
- **A history limit of 0**, or a document over the history size limit: every merge in which the
  arriving version is the earlier one ends in a refusal [M probe]. The KPG tab would warn when it
  sees either setting.
- **The Android merges and saves are read, not measured**, for this entry.

## 13. A migration, in one paragraph

Not designed. On the operator's press and never at open: read today's keys and every node's `KPG`
item, write the first version of the document with the witness, the revocation and retirement
entries, and remove the old keys and items, in one save. Three things would need rulings first.
A node that still carries the old item after the conversion (a copy merged in later) has to refuse
and not inherit, since silence is the widening direction [R rules-central critique, S5]. A machine
still running 0.1.0 on the converted file finds no authority and serves nothing, which is the
closed direction. And whether a database made with 0.1.0 is carried over at all is a question for
the project (O7).

## 14. The probe behind the [M probe] marks

- Scripts: `Lib.ps1` (the document, the witness, the step), `Cells.ps1`, `Rounds.ps1`, under
  `spikes\storage\probes\single-entry\design\scripts\`.
  Run in Windows PowerShell 5.1 against a copy of `.keepass\2.62\KeePass.exe`
  (SHA-256 `AFB89B74…C89C3F3`, equal to the original). No window. `keepassxc-cli` was the only
  other program started, from the archives the `keepassxc` ground report verified.
- Outputs: `spikes\storage\probes\single-entry\design\out\cells.txt`
  and `spikes\storage\probes\single-entry\design\out\rounds.txt`. Databases under `spikes\storage\probes\single-entry\design\work\`.
- `cells.txt`: eight scenarios (S1 to S8), each merged by `PwDatabase.MergeIn` under Synchronize,
  Overwrite if newer, Overwrite existing and Keep existing in both directions (64 cells) and by
  `keepassxc-cli merge -s` in four releases in both directions (64 cells). Each line gives what
  the merge left (current, history, witnesses) and what the step then decides, without and with
  the open copy's version in memory; each KeePass result was saved, reopened and read again.
- `rounds.txt`: ten chains of twelve rounds (KeePass with memory at five seconds either way, at a
  tie, and under a history limit of 0; KeePassLib with no plugin at the merge; `keepassxc-cli`
  2.8.0); documents of 100,000, 1,000,000 and 7,000,000 characters; Create new IDs; the state
  entry deleted for good and into the bin, with the recovery; the cost of a read.
- **What the probe is not**: the plugin's code (the document is a line format of the probe's own,
  unprotected, and the step is PowerShell), KeePass's window (no `FileSavingPre`, no entry
  dialog), KeePassXC's window, or any Android program. The padded documents compress to nothing,
  so the file sizes in `rounds.txt` mean nothing.

## 15. Measurements still needed, most important first

1. **The Android programs in scope, on an emulator.** Keepass2Android 1.15-r6: a merge of the
   state entry changed in both copies at different times (is the earlier version in history, and
   still there after Keepass2Android's own next load); at a tie; with the stale copy's entry
   edited later; that an arriving witness is added and an arriving deletion record removes a
   seen witness. KeePassDroid 2.7 and Keepass2Android: a protected custom string of 20 KB and of
   65 KB through a save, through an edit of another entry and through opening the entry itself,
   and what the screen does with it. KeePassDroid's save over a file changed underneath it, to
   put a number on the one loss nothing covers.
2. **A real File then Synchronize in KeePass's window with a plugin**: that `FileSavingPre` runs
   after `MergeIn` and before either file is written, also when the save itself started the
   synchronize, and that a version written there reaches both files.
3. **KeePass's entry dialog on the state entry**: an OK that changed nothing after a plugin write
   (section 7.5), an OK after viewing a 65 KB protected string, and whether the dialog alters a
   long string in any way.
4. **KeePassDX 4.5.8 and 3.2.0 merges** of the state entry: different times, a tie, the entry
   moved to the bin and back (its time moves [R android 1.4]).
5. **The plugin's own cost**: reading and checking a 65 KB document in C#, the merge of two, and
   a listing's look-ups against the index, inside the gate.
6. **How often a closed group refuses something the operator wanted served**, and how often a
   narrowed node is copied to another group: the two numbers that decide whether section 5.3 is
   enough or `option-rules-linked.md`'s back reference has to be added.
7. **KeePassXC's window**: the merge from its menu, the reload of a file changed on disk, and
   what restoring a history item of the state entry leaves in history.
8. **Clocks**: two machines whose clocks differ by minutes, to confirm that only the place of the
   loser changes (current or history) and never the outcome.
9. **Three copies** that all wrote, merged in every order, through KeePass and KeePassXC.
10. **Other programs' copies**: whether Keepass2Android's and KeePassDX's copy commands keep the
    creation time, and where each puts the copy.
