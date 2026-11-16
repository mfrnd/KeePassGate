# Critique: hardened custom data, read for security, KeePass's own behavior, the operator and the cost

Key: `hardened-custom-data-security-and-fit`. Written 2026-11-15 against
`option-hardened-custom-data.md` as it stood that day, KeePassGate master `a42e38aa` (worktree
the product at `a42e38aa`), KeePass 2.62 and the ground reports beside the design.

This attacks the design. It does not restate the design's section 10 except where that section
understates a problem.

**How each statement is marked.**

- **[M here]**: measured for this critique by the probe in section 5, over KeePass 2.62's KeePassLib
  and `keepassxc-cli` 2.8.0, console only, invented databases. Where a line says "the design's read
  gives", the probe applied the design's three states of section 2 as written, in code of its own;
  it is not the plugin.
- **[M keepass]**, **[M keepassxc]**, **[M spike]** and so on: measured in that ground report, or on
  `spikes/revocation-merge`, section named.
- **[R file:line]**: read from source. KeePass paths are inside `KeePass-2.62-Source.zip` as the
  `keepass` ground report verified and unpacked it; KeePassXC paths are at the tag named; KeePassGate
  paths are under `src\KeePassGate\` of the product at `a42e38aa` unless another root is named. **[R design
  §n]** is what the design under attack says.
- **[A]**: assumed. Every assumed statement is listed again in section 6.

## 0. The verdict

**Viable with changes.** Read through this lens, nothing in the design is fatal: the API still
reaches none of its data, KeePass's own dialogs and guards still hold, and every problem below has a
change that closes it. But four of them are serious, and each is a widening, where the design's
account is that a disagreement can only narrow and that "nothing a person does to one place widens
what a client reaches" (design §2, §6):

1. **A generation number orders versions; it does not say which version a writer had seen.** Two
   machines that both run the plugin, and nothing else, can lose the later of two narrowings: the
   read calls the narrowed node *behind* and puts the other machine's wider rule in force, and the
   next save erases the narrowing from the node (P1) [M here].
2. **The formula for "every witness must allow" grants what an unrecorded rule says**, while the
   prose and the table of section 6 say such a rule grants nothing (P2) [R design §2].
3. **Pruning the ledger of a node that left the tree** turns the design's own protection into a
   widening when an ordinary KeePassXC merge brings the node back, in both directions (P3) [M here].
4. **Settling a disagreement takes one press too few**: Keep all on the Rules page after a phone
   save, or opening a disagreeing node's KPG tab and pressing OK for another reason, records whatever
   is there as the newest rule, and the record then outranks every copy (P4) [R design §3].

Beyond those: two places where the authority's private key now shows up that it did not before
(P5, P6), one where the design overstates it (KeePass shows a size, not the value), hand deletion
that silently stops working while the plugin runs (P7), and a cost that is larger than section 3's
table, including a test surface the gate's tiers cannot run (P11).

What holds up, and is better than today: a revocation, a retirement and a narrowing survive every
single save measured and the KeePassXC replacement of the dictionary; a stale wide rule under a
newer time is recognized and loses; a copied, imported or hand-written rule fails closed once P2 is
fixed. The residual cases of design section 10 (a merge that carries no custom data, two
foreign programs in a row, a whole stale file) are the survival critique's to weigh; nothing here
makes them worse or better.

## 1. Problems, most severe first

### P1. A higher generation is not a later decision: a fork loses the latest narrowing, silently. Serious

Steps [M here, `attack.txt` S1]:

1. Base: entry N carries `{"#":"1","default":"inherit","agent":["read","list"]}`, recorded as
   generation 1 in both replicas.
2. Copy A, KeePass with the plugin: on day 1 the operator narrows N to
   `{"#":"2","default":"inherit","agent":[]}` (generation 2), and on day 2 widens it back to
   `{"#":"3",...,"agent":["read","list"]}` (generation 3). Both recorded.
3. Copy B, KeePass with the plugin on a second machine, not yet synchronized: on day 3 the operator
   narrows N to `{"#":"2","default":"inherit","agent":["list"]}` after something went wrong. B's head
   was generation 1, so this is generation 2 of its own (`RuleLedger.Record` adds `head + 1`, design
   §3).
4. KeePass 2.62 Synchronize, either direction: N's current version is B's (day 3 is the newest
   entry), with three items in its history; the union holds generations 1, 2 (A's), 2 (B's) and 3
   [M here].
5. The design's read gives: head is generation 3, one version; N holds "a recorded version of a
   lower generation"; state **behind**; generation 3 in force. **`agent` reads N again** [M here].
6. At the save that ends the synchronize, the reconcile writes the head's text into every node in
   the behind state (design §3, "The reconcile"), so B's narrowing leaves N as well. A behind node is
   not a row on the Rules page (design §3 lists only nodes that disagree), so nothing tells the
   operator.

Why the design misses it: section 2 justifies *behind* with "a higher generation was written by
somebody who had seen that one". That is true only along one line of edits. Generation 3 was
written by somebody who had seen A's generation 2, not B's. The ledger records no parent, so a
version on another branch of the history is read as an ancestor of the head. The design's
"two copies that edited one node's rule are in force as their intersection" (§10 case 7) holds only
when both copies made the same number of edits.

The same rule reverses a deliberate act in KeePass's own window. An operator who widened N by
mistake and restores the previous revision from the History tab, to take the widening back, finds
the widening still in force (the restored text is an older recorded version, so *behind*) and the
restored rule overwritten at the next save. The design lists "a restored history revision no longer
rolls a rule back" as a gain (§2); for a restore that narrows, it is the operator's undo being
undone.

No foreign program is involved. Two machines running the plugin are the case the database's merge
design exists for.

Fix, both parts:

- **Record each version's parent**, for example as a fifth part of the key,
  `KPG.Rule.<uuid>.<gen>.<d>.<parent d>`. A node is *behind* only when its version is an ancestor of
  the single head along parent links. A version on another branch makes the node disagree, and the
  Rules page lists it as "two versions".
- **Put the head in force at a behind node only where the head is no wider than the node's text,
  per profile; otherwise take the intersection and list the node.** The save's repair writes the
  head only where that cannot widen. This keeps the measured gains (a stale wide rule under a newer
  time, from KeePassXC or KeePassDX, still loses to a narrower head) and makes a narrowing restore
  hold, at the cost of an operator's press when a stale narrower rule arrives over a widening.

Tests: S1 in the host tier, both directions, and a restore of a narrower revision.

### P2. The design's own formula grants what an unrecorded rule says. Serious

[R design §2, the states table and the formula; §2 "Three consequences"; §6 table.]

1. Section 2 puts "a rule with nothing recorded" in the **disagrees** state, and says the witnesses
   at a disagreeing node "are the node's own document (or its absence) and each head version".
2. With nothing recorded there is no head version, so the node's own document is the only witness,
   and the formula gives "w names p ... w's set for p". The rule grants exactly what it names.
3. The prose two paragraphs later says the opposite: "A rule nobody recorded cannot grant ... each
   narrows as written and grants nothing until the operator presses OK on that node's KPG tab", and
   section 6 relies on it for "type or script a rule into a node's custom data | not recorded:
   narrows as written, grants nothing".

Who produces such a rule: KeePass's Duplicate Entry and copy and paste (new identifiers, rule
carried) [R keepass §9]; File then Import with its preselected Create new IDs, for every ruled node
of the arriving copy [R keepass §2.4]; the trigger "Import into active database", which defaults to
the same [R keepass §2.4]; a script over pykeepass, which writes back the XML tree it holds
[R others §3] and kept every node's custom data on a save [M others §3] (that a script adds a `KPG`
item through it is [A]); a node arriving through a merge that did not carry the ledger; every ruled node of a
0.1.0 database, since 0.1.0 recorded nothing (section 2, (e)).

Outcome if the formula is implemented as written: each of those rules grants as it stands, which is
today's behavior and exactly what the design promises to stop. If the prose is implemented, the
behavior is right but the specification and its tests disagree.

Fix: the absence of a record is a witness of its own, and it is silent (it answers the parent), so
at a node with nothing recorded the answer is the intersection of the node's rule and what is
inherited. One line in the formula, and unit tests for a duplicate, an imported copy and a typed
rule.

### P3. Pruning a gone node's ledger turns the protection into a widening under KeePassXC. Serious

Steps [M here, S2]:

1. Base: N carries generation 1, `agent` `read` and `list`. Copy C is the base, kept somewhere: a
   phone, a backup on a share.
2. Copy A narrows N to generation 2 (`agent` nothing), then deletes N for good (the recycle bin
   emptied). On the plugin's next write the prune removes N's ledger from both replicas, because N is
   "no longer in the tree" (design §3, Pruning).
3. `keepassxc-cli 2.8.0 merge -s`, slow KDF, in either direction. KeePassXC applies no deletion
   [M keepassxc §2], so N comes back from C with generation 1. Into A, the dictionary is C's
   (generation 1) and the mirror A's (nothing for N); into C, the dictionary is A's (nothing) and the
   mirror C's (generation 1).
4. The design's read gives, in both directions: one recorded version, N holds it, **agrees**,
   generation 1 in force: **`agent` reads N** [M here].
5. The same merges without the prune: generation 2 is in the union in both directions, N is
   *behind*, generation 2 in force, `agent` refused [M here]. KeePass's own Synchronize deletes N
   again in both variants [M here].

Outcome: an entry the operator narrowed and then deleted comes back through a routine KeePassXC
merge with the wider rule in force, and the design's own housekeeping is what made the difference.
The test the design states for a prune, "a key whose return does no harm", is half of the test: a
removal must also do no harm when the thing it was about comes back, and in a store where KeePassXC
resurrects deleted entries [M keepassxc §2, §4], nodes come back.

Fix: never prune the head versions of a node, present or not; prune only versions more than eight
generations below a head. The cost is one head per node ever ruled and deleted, about 100 characters
for a typical rule [M inventory §1.2]. A tombstone in place of the head does not work: the returning
node would hold an older recorded version beside a newer tombstone, which is *behind*, and the
tombstone means "no rule here", which inherits.

### P4. Settling a disagreement takes one press too few. Serious

[R design §3, "Opening a node's KPG tab and pressing OK" and "The Rules page".]

**Keep all after a phone save.** Steps:

1. The root grants `agent` `read` and `list`; entry X is narrowed to `{"agent":[]}`.
2. KeePassDroid saves the file: every rule deleted, no time moved [M spike §6]. The desktop
   synchronizes; X's deletion travels, since KeePass takes the arriving node at equal times
   [M keepass §1a, §3].
3. The Rules page lists X as "rule missing", beside every other ruled node. Its two actions,
   Restore and Keep, are each offered "for one row or for all" (design §3).
4. The operator, who on the design's own account presses Restore all daily (§11), presses Keep all.
5. Keep "records the node's present state as the newest version": a tombstone at the next
   generation. X now agrees (no rule, head a tombstone) and inherits the root's grant. **`agent`
   reads X.**
6. The tombstone outranks the narrowing in every copy from then on: a copy that still holds X's rule
   holds an older recorded version, which is *behind*, and the head says "no rule".

**Opening a disagreeing node's KPG tab and pressing OK.** Steps:

1. X holds a rule nobody recorded (P2's list: a duplicate, an imported copy, a script), or two
   versions at the head.
2. The operator opens X to change its password and looks at the KPG tab to see why X was listed.
3. The design: "On a node that disagrees, the tab shows ... the node's own rule otherwise ... and OK
   writes what is shown as the next generation"; only "a dialog whose KPG tab was never opened
   settles nothing".
4. OK. The unrecorded rule, or the version the node happened to hold, is now the head, in force in
   every copy.

Outcome: in both, a press the operator made for another reason widens access, and the record of the
narrowing is overruled for good. The first is the habit section 11 predicts, one row away from the
right button; the second turns looking into deciding.

Fix:

- On the Rules page, **Keep exists only per row, never for all, and never for "rule missing"**: a
  deleted rule is restored, and a rule the operator really wants gone is removed on that node's KPG
  tab as any rule is. Each Keep says before OK what it changes in reach ("`agent` gains `read` on 14
  entries").
- On a node's KPG tab, **a disagreement is settled only by a button on the tab that names what it
  keeps**. Any other OK leaves the node and the record as they were.
- Tests: a live test that presses OK on a disagreeing node with the tab opened and nothing pressed,
  and asserts nothing was recorded.

### P5. A default import leaves the authority's private key in an ordinary group, out of the plugin's reach. Minor

Steps [M here, S3; R `KeePassLib/PwDatabase.cs:792-823`]:

1. The operator restores entries from a backup copy of the same database with File then Import,
   whose preselected method for a KeePass file is Create new IDs [R keepass §2.4].
2. KeePass gives the arriving root a new identifier and adds it as a group under the open root,
   with its properties copied (`AssignProperties`, `:811-813`). Measured: a group named after the
   database now sits under the root holding `KPG`, `KPG.Authority.a1` and `KPG.Client.c1` [M here].
3. The operator presses Delete authority on the KPG tab, which removes the authority from both
   replicas. The dictionary no longer holds it; **the imported group still does** [M here].

Outcome: the authority's private key, the host's and the gate key are in a group no read, prune,
Recreate or Delete looks at. The uninstall instructions promise that after Delete authority "what
stays in the database grants nothing and holds no key" [R `docs\getting-started.md:326-334`]; that
is no longer true after an ordinary import. The group is an ordinary group: KeePassXC shows its
custom data values (P6) and, as a group the other copy lacks, carries it with its custom data into
every copy it merges with [R keepassxc §3, Groups]. Today the same import puts only the root's rule
on that group, because the authority is not in the root group.

Why only minor: nothing is admitted or granted through it, and whoever reads the file could read the
key in the dictionary before the delete.

Fix: on every plugin write, and at Delete and Recreate, remove `KPG.`-prefixed keys from the custom
data of every group but the root (they are never read), and have the Certificate Authority page
count any it found. A host test of S3.

### P6. The private key moves onto a page KeePassXC shows in clear. Minor

[R `src/gui/EditWidgetProperties.cpp:106-119` at 2.8.0, `:109` at 2.4.2, `:118` at 2.6.6, `:117` at
2.7.12; `src/gui/group/EditGroupWidget.cpp:181` at 2.8.0.]

1. In KeePassXC, the operator opens Edit Group on the root, to rename it, and looks at Properties.
2. The page lists every custom data item of the group in two columns, Key and **Value**. With the
   mirror, that is every `KPG.` key with its value: the authority document with its password-less
   PKCS#12 base64, every record, the ledger.

Today no KeePassXC window lists the database's custom data values: the group and entry editors are
the only users of that page [R search of `setCustomData(` at 2.8.0], and the database settings show
only the browser keys [R `DatabaseSettingsWidgetBrowser.cpp:171-185`]. The one place values appear
today is a merge's change list [R `Merger.cpp:738`, `:756` at 2.8.0], which the design names.

The design gets KeePass the other way round. Its section 6 says the root group's Edit Group dialog
"lists the whole mirror on its own plugin data page, the authority document included", with the
page's content assumed. Read: KeePass's list has a Name column and a **Size** column, and the value
is replaced by its size before it is shown [R `KeePass/UI/UIUtil.cs:3839-3880`], in the group dialog
and in Database Maintenance alike. KeePass shows no value. KeePassDX 4.5.8 shows none either: the
entry screen's custom data view is commented out [R
`app/.../activities/fragments/EntryFragment.kt:122` at 4.5.8].

Why only minor: whoever sees the page has the unlocked database. The exposure is to a screen share
or a recording, and the cell is likely cut short [A].

Fix, if wanted: leave `KPG.Authority.*` out of the mirror. Then a KeePassXC replacement that loses a
recreated authority leaves the old one retired (its retirement is in the mirror) and no authority
served, which fails closed and costs a recreate and every client pairing again. That trade is the
operator's.

### P7. Hand deletion stops working while the plugin runs, and the documents say otherwise. Minor

[R design §6 table; R `KeePass/Forms/GroupForm.cs:281-287`; R `CA.md:296-302`, `:324-332`;
`docs\clients.md:149-152`; `docs\getting-started.md:342-347`.]

1. Database Maintenance's Plugin Data tab: a deletion is put back from the mirror at the next save,
   and is not even seen meanwhile, since reads take the union. CA.md names one deletion as the only
   way out: a revocation written before revocations named their authority stays "until it is removed
   by hand under Database Maintenance" (`CA.md:299-301`). It now has to be removed on two tabs.
2. The root's Edit Group, Plugin Data tab: KeePass deletes the selected rows **with no
   confirmation** and allows several at once (`GroupForm.cs:281-287`,
   `UIUtil.cs:3882-3901`). The root's own rule `KPG` is one row among every ledger version, profile
   generation and standing answer of the database: about 200 rows for twenty ruled nodes at eight
   generations. Deleting it by mistake makes the root disagree and turns off every grant the root
   makes, which for most databases is nearly all of them.
3. The uninstall steps tell the operator which keys stay and where to remove them
   (`getting-started.md:333-347`); the list now has ledger and generation keys in both places.

Fix: the documents, and a sentence on the Profiles and Clients pages where hand deletion is
mentioned today. Nothing in the design needs to change.

### P8. The profile list's generations lose a concurrent removal, and a union can push it past the ceiling. Minor

Steps [M here, S4]:

1. Base: `KPG.Profile.writer.in.1`.
2. Copy B removes `writer` and later adds it back: `out.1`, `in.2`.
3. Copy A, not synchronized, removes `writer` after both, because a client holding it misbehaved:
   `out.1`.
4. KeePass 2.62 Synchronize: the union is `in.1`, `in.2`, `out.1`; the highest `in` is above the
   highest `out`; **`writer` is in force** [M here].

The design says "A removal is an added key, so it survives every union" (§1). It survives as a key
and loses as a meaning. A removed name coming back is a widening (inventory D6). Not a regression:
today one copy's whole list wins, which is worse.

Second, the ceiling. Two copies each adding a name to a list of 31 give a union of 33 names, which
"reads as an unreadable list, which is an empty one" (§1): every client stays admitted and reaches
nothing until somebody removes names. Today the merge takes one list whole and the tab enforces the
ceiling at OK. Fail closed, but a dark gate from an ordinary merge.

Fix: give each `out` key a random tag and make each later `in` key name the `out` tags it saw; a
name is in force only when every `out` tag is named by some `in`. A concurrent removal then wins.
For the ceiling, name the overflow on the Profiles page rather than reading the whole list as
empty, or keep the oldest 32.

### P9. "Contested" is not defined for keys whose meaning is in the name. Minor

[R design §2, "A key both hold with different values is contested".] The design defines the outcome
for a record, an authority document, a ledger version, a revocation and a retirement. A profile
generation, a standing answer and a ledger tombstone carry their meaning in the key and the value
`1` or `none`. If "unreadable" is taken to mean "skip it", a hand-edited or forged `out` key with
another value drops a removal and puts a name back in force. Only somebody writing the file can do
it (P23 concedes them), but the design's fail-closed list should not have a gap. Fix: for every key
whose meaning is in its name, presence decides and the value is ignored.

### P10. The reconcile at every save is a new kind of write, and its edges are open. Minor

[R design §3; R `Bridge/OwedAnswerSaves.cs:68-87`; R `Bridge/DatabaseWriter.cs:1910-1912`,
`:2098-2149`.]

- **A ruling.** O4 covers owed standing answers, a datum the operator already gave. The reconcile
  writes node rules and the root group's custom data into every KeePass save, the operator's Ctrl+S
  included. That is a decision for the project, as the same extension was in the rules-on-node
  critique's P1.
- **The root dialog.** The design skips the mirror while the root's Edit Group dialog is open for
  the plugin's writes, but lists only "a save of a copy" and Database Maintenance as exceptions for
  the save-time reconcile. Harmless (the dialog's OK puts its older copy back, which only lags), but
  it should be said.
- **Take-back.** Every write now sets both replicas and repairs nodes. `Undo` restores exactly what a
  write changed in the dictionary (`DatabaseWriter.cs:2098-2149`). If it is not extended to the
  mirror, a pairing whose save failed leaves its record in the mirror, which the union reads: a
  client on the clients page that never existed, the case the code says `Undo` exists to prevent
  (`:1910-1912`). A client's own revocation that failed to save, left in the mirror, refuses a client
  that was told the revocation failed.
- **Coupling.** A repair that throws inside the writer must not fail the pairing or revocation it
  rides on. At `FileSavingPre` the handler already swallows everything and lets the save go on
  (`OwedAnswerSaves.cs:68-87`), so a repair there is best effort, which is right, and can stop half
  way, which is harmless.

Fix: the ruling; the two sentences; `Undo` over both replicas with a host test of a failed save;
repairs that never fail the write.

### P11. The cost is larger than section 3's table, and part of what it buys cannot be tested here. Minor

Section 2, (e), has the list. Three things the design does not say:

- **The protection rests on two behaviors of other programs that no tier of the gate's tests can
  run**: KeePassXC never merging the root group's own fields (its merge starts from the root's
  children; `resolveGroupConflict` is called for child groups only [R `src/core/Merger.cpp:168-174`,
  `:216-247` at 2.8.0], which explains the design's measurement), and KeePassDroid and KeePassDX
  keeping the database's dictionary. The host and live tiers run KeePass only. Either behavior could
  change in a release. `keepassxc-cli` is a console program, so a job that runs the design's probe
  against each pinned KeePassXC release is possible on a hosted runner [A]; without one, the
  measurement that carries the design is a spike that somebody has to remember to rerun.
- **The design's own keys get times after a KeePassXC save of a file that is 4.1 for any other
  reason.** A group tag set in KeePass's own group dialog makes the file 4.1 [M keepass §3]. A
  `keepassxc-cli` 2.8.0 save or merge of such a file gave **every** `KPG.` key in the dictionary a
  modification time, keys that were already there with the same value included [M here, S5 and
  S5b]. So "kdbxweb ... none, as long as no dictionary key in the file carries a time" (§4) and "this
  design only keeps its own keys from being the cause" (§10 case 8) are both understated: after that
  save, a KeeWeb save makes the file unreadable to KeePass [M others §3] because of the plugin's own
  keys. Fix: the save-time reconcile also rewrites every `KPG.` key without a time, which the
  internal setter does by removing the time [R `KeePassLib/Collections/StringDictionaryEx.cs:162-174`];
  the window between a KeePassXC save and the next KeePass save stays open.
- **0.1.0 databases** are not carried over by anything in the design unless the migration runs, and
  without it they fail closed and dark: `KPG.Profiles` is not read (no profile in force), rules
  carry no `#` and nothing is recorded (P2), standing answers under `KPG.RenewalRefused.` are not read
  (renewals go back to the dialog). That is the safe direction, unlike the rules-on-node option, but
  it is an outage the operator has to be told about before upgrading.

## 2. The lens, point by point

### (a) The caller in the threat model

What holds:

- **No route reaches any of this data.** No use of `CustomData` under `Vault\` or in
  `Pipeline\VaultHandlers.cs` [R, search at `a42e38aa`; the one match in `Bridge\KdbxVault.cs:2231`
  is an exception filter on parsing an identifier]. The root mirror is group custom data like a
  node's rule, and the 44 operations are reads and writes of groups, entries, fields and
  attachments, the caller's identity and access, and pairing [R `Spec\openapi.yaml`]. There is no
  move route. A client holding `create` and `update` everywhere writes no ledger key, no record, no
  rule.
- **Field references** are compiled by the reference client over what the gate returns, and custom
  data is not among it; KeePass's own placeholder engine reads no custom data either [R search of
  `KeePass\Util\Spr` for `CustomData`: none].
- **An attacker-supplied file vouches only for itself.** Opened alone, its union is its own. Merged
  in, its dictionary keys are added as today, and its root group either replaces the root's custom
  data whole when it is newer (today too) or, with another root identifier, lands as an ordinary
  group that is not read [R `PwDatabase.cs:792-823`]. A merged file can retire this database's
  authority and bring its own, as it can today; P23 concedes whoever writes the file.

What needs care:

- **A client's own write starts the reconcile and the prune** (design §3: "inside every
  `DatabaseWriter` write"). With P3 unfixed, a client that holds `delete` on a node whose rule
  narrows *another* profile can remove it. Where the database keeps no recycle bin the removal is
  permanent [R `Bridge\KdbxVault.cs:2037-2041`], and the prune riding that same write removes the
  ledger that would refuse the node when a stale copy brings it back; with a bin, the operator's
  emptying it does the same at the next write. The client cannot cause the merge.
- **Reach as a signal.** A client that polls `getGroupAccess` at the root learns when a phone save
  turned every grant off, which is when the operator used the phone. A small leak; the 404 rule
  keeps the reason in the audit as the design says.
- **The read's cost per request.** The ledger index is built from both replicas once per request on
  a thread holding KeePass's thread still (design §2; A6). With nine versions kept per ruled node
  and two replicas, a database with 1000 ruled nodes is some 18,000 keys a request [A, arithmetic].
  A client making many requests multiplies it. Measure before choosing (design §14 item 9).

### (b) Secrets

Where the authority's private key shows up, against today:

| Place | Today | With this design | Basis |
| --- | --- | --- | --- |
| KeePass Database Maintenance, Plugin Data | name and size | the same | [R `UIUtil.cs:3839-3880`; `DatabaseOperationsForm.cs:73-76`] |
| KeePass root Edit Group, Plugin Data | not there | name and size, **no value** (the design says the document is listed; it is, by name) | [R `GroupForm.cs:161-164`; `UIUtil.cs:3864-3873`] |
| KeePassXC root group, Properties | not there | **key and value in clear**, 2.4.2 to 2.8.0 | P6 |
| KeePassXC merge change list, command line and from 2.7.11 the window | values of the dictionary keys it removes or adds | the same; group custom data is never merged, so the mirror adds nothing | [R `Merger.cpp:738`, `:756` at 2.8.0] |
| an ordinary group after File then Import with Create new IDs | not there | **the whole mirror**, outside Delete and Recreate | P5 [M here] |
| KeePass XML export | one copy | two copies | P26; [R design §6] |
| KeePassXC CSV and HTML export | none | none | [M keepassxc §8] |
| search, auto-type, reports, field references | none | none: KeePass's search and placeholders read no custom data | [R search of `PwGroup.Search.cs` and `Util\Spr`] |
| entry history | none | none: groups keep no history and the mirror is on a group | [R keepass §1b, §3] |
| a phone's screen | none known | none known: KeePassDX 4.5.8's custom data view is commented out; Keepass2Android and KeePassDroid not checked | [R `EntryFragment.kt:122` at 4.5.8]; [A] for the others |
| the clipboard | none found | none found | [A] |
| KeePass's memory | one immutable copy per load, conceded (P14) | one more per load, and one more each time the root's Edit Group dialog opens (`CloneDeep`) | [R `GroupForm.cs:161`; `StringDictionaryEx.cs:68-80`] |

### (c) KeePass's own functions and the present guards

| KeePass function | What it does to this design's data | Outcome |
| --- | --- | --- |
| Root Edit Group, OK | copies the root's custom data at load, puts the copy back at OK, and always moves the root's time [R `GroupForm.cs:161`, `:209-231`] | the mirror lags; the design skips the mirror half while it is open; the root becomes the newest, so this copy's mirror wins the next KeePass merge whole, which the union absorbs |
| Root Edit Group, Plugin Data | deletes without confirmation, several rows at once | P7 |
| Group and entry dialogs, `FormClosed` | KeePass's OK puts `m_sdCustomData` back in `OnBtnOK` before the form closes: the group dialog at `GroupForm.cs:230` before `OnFormClosed` at `:194`; the entry dialog in `SaveEntry` at `PwEntryForm.cs:1107`, called from `OnBtnOK` at `:1163`, before `OnFormClosed` at `:1181` | the order design §14 item 4 asks for holds, read; the live test is still needed |
| Database Maintenance | copies with `CloneDeep`, which keeps each key's time or absence of one, and replaces the dictionary when the count differs [R `DatabaseOperationsForm.cs:73`, `:86-88`; `StringDictionaryEx.cs:68-80`] | a timeless key stays timeless (design §14 item 8, read); a deletion there is put back from the mirror (P7) |
| KeePass's own writes to the dictionary | the merge copies the source's time [R `PwDatabase.cs:1603-1623`]; the public timed `Set` is called on it only from a debug menu and the sample database [R `MainForm_Functions.cs:6849-6862`, `MainForm.cs:693-694`] | no ordinary KeePass path stamps a `KPG.` key; KeePassXC does (P11) |
| File then Import, Create new IDs (preselected), and the trigger "Import into active database" | the arriving root becomes an ordinary group with its custom data; every arriving ruled node is a copy under a new identifier | P5; P2 |
| Duplicate Entry, Duplicate Group, copy and paste | new identifiers, rules carried [R keepass §9; `MainForm.cs:2313`] | P2. The root cannot be duplicated (`MainForm_Functions.cs:746`, `:756`: `bChildOps` is false for the root) |
| Restore a history revision | the older text is current under a new time | *behind*: P1's second half |
| Edit Entries | its Advanced list can remove `KPG` from every selected entry at once | no tombstone, so those entries disagree: narrowings hold, grants off; safe |
| Delete, Empty Recycle Bin | a node in the bin is still in the tree; emptying the bin removes it | P3 |
| Synchronize, triggers that synchronize | `MergeIn`, then the normal save with `FileSavingPre` [R keepass §4] | the reconcile runs after the merge; not run in a window (design §14 item 3) |
| XML Replace | edits the database's XML, custom data of the database and the root included | can write both replicas; P23 |
| Search, auto-type, print, reports | no custom data | nothing |

The present guards:

- **`GroupDialogs`** still watches the six dialogs and refuses while it is not watching. It gains one
  question, whether the root's group dialog is open, and the save-time reconcile has to ask every
  per-node question the writer asks (P10).
- **The single writer** stays the only place a request writes. It now also writes the mirror,
  repairs, Rules page restores and prunes over both replicas; `Undo` has to follow (P10).
- **The save that is read back** is unchanged: `Modified` false and the hash of the file, for the
  plugin's own writes. The save-time reconcile inside the operator's own saves is not read back, as
  owed answers are not today.
- **`MergeWatch`** is unchanged; the read now also walks the ledger.

### (d) The operator

What a person sees:

- In KeePass, Database Maintenance and the root's Edit Group list about the same rows: every
  `KPG.Rule.<32 hex>.<generation>.<16 hex>`, every `KPG.Profile.<name>.in.<g>` and `.out.<g>`, every
  `KPG.RenewalAnswer.…`, the records, the authority. About 200 rows for twenty ruled nodes. Names
  only.
- In KeePassXC, the same on the root's Properties page, with values (P6).
- A fourth page on the KPG tab, which after every KeePassDroid save lists every ruled node, and after
  every KeePassDX 3.2 or 3.3 save every ruled group (design §11).
- Nothing outside that page: a dark gate is visible to clients as an empty database and to the
  operator only on the Rules page and in the audit. A count on the plugin's window under Tools, or
  on the tab, would tell them before an agent does [A that operators do not open the page
  unprompted].

What a person can do wrong, with no warning in the program they are using:

- Press Keep all instead of Restore all (P4).
- Open a disagreeing node's KPG tab, then OK for another reason (P4).
- Restore a revision to take a widening back, and have it ignored (P1).
- Use two machines with the plugin and narrow on the one with fewer edits (P1).
- Import a backup with KeePass's preselected method (P5, P2).
- Delete a row in Database Maintenance and believe it gone; delete the root's rule among 200 rows on
  the root's Plugin Data tab, with no confirmation (P7).
- In KeePassXC, "clean up" the root's plugin data: the mirror comes back at the next KeePass save,
  the root's rule does not, and the root's grants are off until restored.

### (e) Cost

Rewritten or changed, beyond the design's table (line counts at `a42e38aa` [M here, `wc -l`]):

- `Acl\ProfileNames.cs` (277 lines) for generations, and P8's tags; `Ui\PendingChanges.cs` (971)
  loses its three reconcilers; `Authority\AdmissionStore.cs` (854), `Authority\AuthorityStore.cs`
  (643), `Authority\AuthorityKeys.cs` (267) and `Authority\RenewalRefusal.cs` (150) for the new key
  families and the replicated store; `Bridge\DatabaseWriter.cs` (2179) for both replicas, the
  reconcile, Rules page writes, prunes over both and `Undo` (P10); `Bridge\OwedAnswerSaves.cs` for the
  save-time reconcile; `Acl\AclResolver.cs` (537), `Acl\AclDocument.cs` (294),
  `Acl\AclInheritance.cs`, `Bridge\KdbxNodes.cs` for the states, the intersection, `#` and P1's
  parent; `Ui\GrantPanel.cs` (2004), `Ui\GrantSheet.cs`, `Bridge\NodeDialog.cs` (457) for the rule in
  force, the one line, P4's explicit button and the record at `FormClosed`; `Ui\GateTab.cs` (486) and
  a new page for the Rules page; `Ui\ClientList.cs` (1688) for red rows read from numbered answers;
  `Bridge\GroupDialogs.cs` (1228) for the root question; `KeePassContract.cs` for the internal setter;
  new `RootMirrorStore`, `ReplicatedStore`, `RuleLedger` and P5's sweep.
- Documents: CLAUDE.md's decided bullets on the store, KDBX 4.1, revocation, the profile list as a
  difference, the standing answers, the grant editor and the trap; `CA.md` §204 and §222;
  `DESIGN.md`'s access model; `docs\clients.md`, `docs\access-control.md`,
  `docs\getting-started.md`'s uninstall; the screenshots, for a fourth page.

Measured facts and tests that fall away or turn round:

- "The authority takes the file to KDBX 4.1": `AdmissionInRealDatabaseTests.cs:84-107` asserts the
  authority alone makes the file 4.1, and turns into an assertion that it stays 4.0 [R].
- `CustomDataMergeRuleTests` stays as a pin but carries nothing; the measured fact about times in a
  KeePass merge stops being load bearing, as the design says.
- `MergeInRealDatabaseTests`' root group case becomes the premise of the mirror (it lags) rather than
  the reason against it.
- The tests of `PendingStore.Reconcile`'s three merges go.

New hazards that need tests: P1 to P4, P5's sweep, P8, P10's `Undo`; the save-time reconcile in a real
File then Synchronize (live); the root dialog skip (live); `FormClosed` after OK on the group, entry
and Add Entry dialogs (live); the Rules page's fit on the tab (`GateTabFitsTests`); the internal
setter's presence (`KeePassContract`); and the KeePassXC and Android behaviors of P11, which need a
job outside the tiers.

0.1.0 databases: P11, third point.

## 3. Where the user's timestamp idea meets this option

The user's idea, JSON in an entry field whose name is derived from a timestamp so that a merge keeps
both, does not hold inside one entry: KeePass, KeePassXC and the Android programs take the newer
entry whole and keep the other only in history, which a tie, a full history and Keep existing lose
[M keepass §1e; M keepassxc §5; R android §1.6]. This option applies the same idea one level up,
where it does hold: a custom data key per version, named by a generation, so that the union of two
copies keeps both, in the dictionary under every KeePass merge [M spike §2] and in the union of the
dictionary and the root under KeePassXC's [M design probe].

P1 is the idea's own trap, and it applies to a timestamp as much as to a counter: a name that orders
versions does not say which version its writer had seen. Two copies that each wrote after the last
synchronize produce two names, and "the larger one wins" then discards a decision nobody overruled.
Any version kept this way needs its parent beside it. The idea's "preprocessing step when loading"
would break O1 (opening changes nothing); this option rides `FileSavingPre` and the plugin's own
writes instead, which is the hook the code base already uses, with the open edges of P10.

## 4. Ratings in one table

| # | Problem | Rating |
| --- | --- | --- |
| P1 | a higher generation is taken for a later decision; a fork loses the latest narrowing, and a narrowing restore is undone | serious |
| P2 | the formula grants what an unrecorded rule says | serious |
| P3 | pruning a gone node's ledger widens under a KeePassXC merge | serious |
| P4 | Keep all, and OK after looking at the tab, record whatever is there | serious |
| P5 | a default import leaves the private key in an ordinary group | minor |
| P6 | KeePassXC shows the private key in clear on the root's Properties | minor |
| P7 | hand deletion silently stops working; the root's rule among 200 unconfirmed rows | minor |
| P8 | a concurrent profile removal loses; a union can pass the ceiling | minor |
| P9 | "contested" undefined for name-only keys | minor |
| P10 | the save-time reconcile: ruling, root dialog, `Undo`, coupling | minor |
| P11 | cost; behaviors no tier can test; the plugin's keys timed by KeePassXC in a 4.1 file; 0.1.0 dark | minor |

None is fatal: each has a change, named above, that brings the option back within E1, E5, E6 and E8.

## 5. What was run

Nothing with a window, no test tier, no `build.ps1`. KeePassLib 2.62 loaded from
`.keepass\2.62\KeePass.exe` in Windows PowerShell 5.1, and `keepassxc-cli` 2.8.0
from the archive the `keepassxc` ground report verified against the spike's pinned hash and the
signature (`.keepassxc\2.8.0\keepassxc-cli.exe`), started with no
window. Databases invented, password `probe-only`; slow KDF where KeePassXC had to load the two
copies in different seconds (Argon2d, 200 iterations, 64 MiB; one merge took 7.4 to 18.2 s).

- Probe:
  `spikes\storage\probes\hardened-custom-data\security-and-fit\scripts\Attack.ps1`.
  It models the design's store (timeless dictionary keys through the internal setter, the same keys
  in the root group, ledger keys `KPG.Rule.<uuid>.<gen>.<16 hex>`) and applies the design's three
  states as section 2 defines them.
- Output:
  `spikes\storage\probes\hardened-custom-data\security-and-fit\out\attack.txt`;
  databases under `spikes\storage\probes\hardened-custom-data\security-and-fit\work\`.
- Scenarios: S1 (P1, KeePass Synchronize, both directions), S2 (P3, KeePassXC 2.8.0 both
  directions, with and without the prune, and KeePass Synchronize as the control), S3 (P5, Create new
  IDs import and Delete authority), S4 (P8, KeePass Synchronize), S5 and S5b (P11, KeePassXC 2.8.0
  merge both directions and a plain save of a file made 4.1 by a group tag).

## 6. Not measured here, and assumed

- That the KeePassXC Properties cell cuts a long value short (P6).
- That Keepass2Android and KeePassDroid show no custom data on screen; that nothing copies a custom
  data value to the clipboard.
- That a job running `keepassxc-cli` against the pinned releases fits a hosted runner (P11).
- The per-request key count of (a) and the "about 200 rows" of P7, which are arithmetic, not
  measurements.
- That a script adds a `KPG` item to a node's custom data through pykeepass's XML tree (P2).
- That operators do not open the Rules page unless prompted (d), and that they press the wrong one of
  two bulk buttons (P4): the falsifier of P4's first half.
- `FileSavingPre` running between KeePass's merge and its write, and `FormClosed` after OK in the
  dialogs: read, not run in a window.
- The design's three states, applied by the probe's own code to stored results; the plugin's
  resolver does not exist yet.
