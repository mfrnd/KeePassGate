# Critique of option `wildcard`: security, KeePass's own behavior, the operator, the cost

Written 2026-11-15 against `option-wildcard.md` ("one sealed snapshot of everything, replaced
whole, with deletion records as witnesses"), KeePassGate master `a42e38aa`. The lens: (a) a
prompt-injected API client and an attacker-supplied `.kdbx`, (b) where the authority's private key
now shows up, (c) KeePass's own functions and the present guards, (d) what the operator sees and can
get wrong, (e) what it costs in this code base. This attacks the design; it does not restate the
design's own list in its section 14 except where that list understates a problem.

## 0. How statements are marked

- **[M here]**: run by me today from a console. Script
  `spikes\storage\probes\wildcard\security-and-fit\scripts\probe.ps1`,
  output `spikes\storage\probes\wildcard\security-and-fit\out\probe.txt`, files under `...\work2\`. Windows PowerShell
  5.1 over KeePassLib 2.62 loaded from `.keepass\2.62\KeePass.exe`, no window,
  invented content. Section 7 says what it did.
- **[M design]**: in the design's own recorded output, read by me:
  `spikes\storage\probes\wildcard\design\out\summary.txt` and `merges.txt`.
- **[M ground-x]**, **[R ground-x]**: measured or read in that ground report
  (`ground-keepass.md`, `ground-keepassxc.md`, `ground-android.md`, `ground-measured.md`,
  `ground-inventory.md`), which names the tag, file and line.
- **[R here file:line]**: read by me. KeePass 2.62 source is the archive the `keepass` ground report
  verified against keepass.info's published SHA-256, extracted at
  `spikes\storage\probes\ground-keepass\src\`; plugin code is `src\KeePassGate\` of the product at
  `a42e38aa`, or the `v0.1.0` tag where named.
- **[design n]**: what `option-wildcard.md` says in its section n.
- **[A]**: assumed, neither run nor read. Collected in section 6.

## 1. Verdict

**Viable with changes.** Nothing in this lens is fatal: every problem below has a change that
meets the requirement. But six are serious, and three of them are the design's own remedy buttons
or its own write path doing the damage:

| # | Problem | Requirement it breaks |
| --- | --- | --- |
| 2.1 | **Use what is here** folds an imported foreign store, its rules and its profile numbers, into this one | E5, "a malicious `.kdbx` vouches only for itself" |
| 2.2 | **Use what is here** after a deleted head is a rollback that readmits a revoked client and undoes narrowings, and the tab cannot say what it gives back | E3, E5 |
| 2.3 | A duplicate of a ruled node no longer carries its rule, where today KeePass's own Duplicate carries it, at subtree scale | E5 by an everyday command |
| 2.4 | A rule or a settings-tab change can be refused after its dialog has closed, and the design says nothing of it | O2, O3, E5 in intent |
| 2.5 | The silent rollback of its section 14.3 happens through KeePassDX's own merge, not only a script | E3 |
| 2.6 | The `gone` state refuses a readable snapshot that a merge brought back, so one accidental deletion costs every client | availability; the operator |

The changes named below add three things the design does not have: revocations and retirements
as add-only entries of their own, version witnesses for the two kinds of datum whose rollback
widens (rules and profile names), and a copy hint on ruled nodes. Each is cheap next to the rewrite
the design already is (section 5).

## 2. Serious problems

### 2.1 Use what is here folds a foreign store into this one

**Steps.**

1. A paired client holding profile `reader` is prompt-injected. Ordinary listings hand it node
   identifiers: every entry it may list carries `group`, every group `parent`, whether or not it may
   reach that group [R here `Spec\openapi.yaml:2584-2591`].
2. It builds a `.kdbx` with a store of its own, under its own key. The format is in the plugin's
   public source. The snapshot holds `rule.<id>` members for those identifiers, granting `reader`
   every verb and switch, at version numbers well above anything plausible, and `profile.writer`
   at a large odd number. It hands the file over, as an attachment it may create (`createAttachment`
   takes 512 KiB) or as the output of its task, with a note to import it.
3. The operator uses File, Import, KeePass KDBX. The preselected method is Create new IDs
   [R ground-keepass 2.4: `ImportMethodForm.cs:37`, `:69`].
4. The imported snapshot sits under a random identifier, so its `id` member does not match: the
   state is undecided, "a copy of a snapshot under another identifier" [design 5.2; M design:
   `KeePass U into A CreateNewUuids | UNDECIDED: a copy of a snapshot under another identifier`].
   The only head is the real one, so it is not classed as two stores.
5. For a copy the tab offers **Use what is here**, which joins "every snapshot that reads, copies
   and named ones included" [design 5.2]. Nothing restricts it to snapshots sealed under the key of
   the store in force.
6. The join keeps the attacker's rule versions and drops the real ones as "lower than another's by
   eight or more", and takes the higher profile number, so `writer` is on the list [design 5.3].
   The result holds two unretired authorities, so it is undecided once more, and the operator keeps
   the real authority. Keeping retires the other authority. It takes nothing out of the rules or the
   profile list.

**Outcome.** Rules and profile names the client wrote govern the operator's database. Whatever
`reader` reaches at the targeted groups, and every record naming `writer`, is widened. The settled
principle "a malicious `.kdbx` therefore vouches only for itself" holds for authorities and fails
for everything else, through the button the design presents as an acknowledgment.

**Rating: serious.**

**Change.** Every join and every remedy takes only snapshots sealed under the key of the store in
force. A snapshot under any other key, copied or not, is a second store, answered by Keep, which
discards the other store whole. The tab names it as another database's store, never as a copy. Test
it by importing a foreign store with Create new IDs and pressing every button the tab offers.

### 2.2 After a deleted head, Use what is here is a rollback, and the tab cannot say what it gives back

The design's own set C shows it.

**Steps.**

1. Copies A and B both wrote between two synchronizations. A paired `c3` and set E1's rule to
   `{"reader":["read"]}`. B revoked `c2`, set E1's rule to `{"reader":["list"]}` and took `writer`
   off the list [design 8.2].
2. `keepassxc-cli merge` of B into A leaves two heads. Both are titled `KeePassGate store`
   [design 3.1]. The joined state holds `c2` revoked, `writer` off, and E1 at what both versions
   allow, which is `list`. No witness for `c2` reaches A, since a KeePassXC merge copies no deletion
   record [M design: `KeePassXC 2.4.2 C into A ... records S[S1:b71818] R0 ... revoked=[c2] ...
   writer:2(off) ... E1 n2 SIBLINGS`; M ground-keepassxc 2].
3. The operator sees two identical entries, in KeePass or on a phone, and deletes one for good
   (Shift+Delete, or delete and empty the bin). About half the time it is B's.
4. A deletion record now names a snapshot nothing accounts for: `behind`, `503 undecided` to every
   client, and the tab offers Use what is here [design 5.2].
5. Use what is here joins what reads, which is A's head alone. A's own state is clients `c1, c2,
   c3`, nothing revoked, `writer` on, E1 `{"reader":["read"]}` [M design: line `C-A`].

**Outcome.** `c2`, revoked, is admitted again, because this file never held its witness. `writer`
is back on the list, so every record naming it reaches again. E1 goes from `list` to `read`. The tab
can know only that a snapshot with sequence number 2 is missing, from the identifier in the deletion
record, never what it held. E3 and E5 fail at the operator's press of the design's remedy. The same
state arises with no press at all through 2.5.

**Rating: serious.**

**Change.**

- **Version witnesses for the data whose rollback widens.** Each rule version and each profile
  number is also written as a deletion record whose identifier is derived, with the store key, from
  the node or the name and the number. On the walk, a node whose highest witnessed number is above
  the join's version refuses there and below until the operator rules it again. A name whose
  highest witnessed number is above the join's, and even, stays off. This is the same mechanism the
  design already uses for revocations, with the same limits (no KeePassXC merge copies one, and
  Database Maintenance clears them), and it turns a rollback in a file that holds the witnesses
  into a refusal.
- **Revocations as entries of their own** (2.5), so `c2` stays refused.
- **Titles that tell snapshots apart**, such as `KeePassGate store 7`. The title is outside the seal
  and carries no client text, so nothing is lost.
- **The button says what it is**: a return to the state of a named time, after which every rule
  changed since refuses until set again.

### 2.3 A duplicate no longer carries its rule

**Basis.** Today KeePass's Duplicate carries node custom data, so it carries the rule.
`PwEntry.CloneDeep` copies `m_dCustomData` and `Duplicate` only gives the clone a new identifier
[R here `KeePassLib\PwEntry.cs:321-354`, `:856-864`]. `PwGroup.Duplicate` clones the whole subtree,
custom data included, and gives every item in it a new identifier [R here
`KeePassLib\PwGroup.cs:358-396`, `:1437-1446`], and KeePass's Duplicate Group command calls it
[R here `KeePass\Forms\MainForm.cs:2313-2324`].

**Steps.**

1. The operator seals group `Payroll` from `agent` with an empty array.
2. In KeePass the operator uses Duplicate Group on `Payroll` to start `Payroll 2027`.
3. Today the copy carries `KPG` and stays sealed. Under this design the rule is a member keyed by
   `Payroll`'s identifier. `Payroll 2027` and every entry in it have new identifiers and no rule,
   and inherit from the root.

**Outcome.** Every password in the copy is readable by `agent` wherever the root grants it. Nothing
on the node or in any dialog gives the operator a cue, and the grant editor correctly shows the copy
inheriting from above.

The design names copies as a weakness it shares with `option-rules-central.md` [design 1, 14.1] and
leaves the defense to "granting narrowly and sealing little". It does not say that this undoes
what KeePass's own Duplicate does today, that one command copies a whole sealed subtree, or that the
same follows from KeePassXC's clone [A: that it copies custom data].

**Rating: serious.** E5 is undone by an everyday command, with no cue.

**Change.** A copy hint on the node, read only to recognize copies. The grant editor writes
`KPG.Of = <the node's own identifier>` into the dialog's own copy of the node's custom data, exactly
where it writes the rule today, so KeePass keeps it on OK and every duplicate carries it. A node
whose hint names an identifier other than its own is a copy of that node and refuses there and below
until the operator rules it. Losing the hint, as a KeePassDroid save or a KeePassDX 3.2 or 3.3 save
of a group does [M ground-measured 3.3, 3.4], loses only the recognition and never a rule. The cost
is that the reflection on `m_sdCustomData` stays (5.1), and a node's first rule moves its time once,
as an edit in its dialog does today.

### 2.4 A change pressed OK that never lands

**Steps.**

1. The operator narrows a rule on the group dialog and presses OK. The dialog closes.
2. The editor applies its buffer once the dialog has closed, through the single writer, since
   "every change is the same operation" [design 7.1, 7.3]. The settings tab's OK does the same
   [design 7.2].
3. The writer can refuse. On KeePass's thread it cannot wait for its own lock: a connection's write
   holds that lock while it waits for that same thread, so the existing write from KeePass's thread
   takes the lock with no wait and answers "another change is being written" [R here
   `Bridge\DatabaseWriter.cs:1127-1133`]. A pairing's save pumps KeePass's messages while it runs,
   which is when a click on OK elsewhere is processed [A]. The writer also refuses while its
   watch has not started, while KeePass's progress window is open, and while an Edit Entries dialog
   is open on the database (3.6).
4. The dialog is gone. The design does not say what becomes of the change.

**Outcome.** The gate goes on serving the older, wider rule while the operator believes the
narrowing is in place. Today neither path can be refused: the rule goes into the dialog's own copy
and lands with KeePass's OK, and the settings tab's OK writes custom data directly [R here
`Ui\PendingChanges.cs:290-322`].

**Rating: serious.**

**Change.** The change goes into an overlay the reads consult at once, so it is in force from the
press of OK, as in-memory custom data is today. The snapshot is written when the writer is next
free, owed like the standing answers and carried by the next save if nothing else writes it. The
tab shows it as not yet written. Exiting without a save loses it, as exiting without a save loses a
rule today.

### 2.5 The silent rollback of section 14.3 comes from KeePassDX's own merge

**Steps.** Each link is measured or read; the chain as a whole is [A].

1. A, in KeePass, revokes `c1`. Snapshot S2 replaces S1, with a witness for `c1` and a deletion
   record for S1 [design 6, 7.1].
2. L, in KeePassXC, merges A. S2 arrives. No deletion record or witness is copied, and L's S1 stays
   beside S2, replaced [M ground-keepassxc 2; M design set U].
3. P, KeePassDX 4.5.8 on the phone, merges L. S2 arrives, since an entry only the arriving copy holds
   is added [R ground-android 1.5]. P now holds S1 and S2 and no witness.
4. On L the store entry is deleted for good, which in KeePassXC is `rm` twice and leaves a deletion
   record [M design: `KeePassXC 2.8.0 rm of the snapshot, twice`]. L reads `behind`, which is shown.
5. P merges L. KeePassDX applies the arriving record to S2 and keeps no record of its own
   [R ground-android 3.1, `DatabaseKDBXMerger.kt:304-323`]. P holds S1 alone, with no record and
   no witness.
6. P's file is the one KeePass opens next.

**Outcome.** The store is in force at S1, and `c1` is admitted. The design's 14.3 names only a
pykeepass script, and says the witness covers any file that holds it. A KeePassXC merge is the
ordinary way for a file to come to hold a snapshot without its witnesses, and KeePassDX is the
ordinary way for a deletion to arrive with no record left behind.

**Rating: serious (E3).**

**Change.** Revocations and retirements are also written as entries of their own in the store
group, one an item, written once and never changed. The plugin deletes a revocation entry only after
its certificate has expired and never deletes a retirement entry. An entry only one copy holds
arrives in every merge measured or read [M ground-keepass 1d; M ground-keepassxc 5; R ground-android
1.5], so losing a snapshot can no longer take a revocation with it; only deleting that very entry
can. They are read beside the join and the witness. This is the unit of `option-store-entries.md`,
taken for the two tombstones only, where a record per entry costs nothing because they never change.

### 2.6 The gone state refuses a readable snapshot

**Basis.** [M design: `KeePassXC 2.6.6 rm twice, then the stale copy merged in | GONE ... present
[S1:b71818!named]`]. In `gone` the tab offers Create only [design 5.2, 9].

**Steps.**

1. On a phone the operator empties the recycle bin with the store in it, or deletes it for good in
   any program.
2. The file is synchronized. KeePass's Synchronize and Keepass2Android's merge carry the deletion
   record to the other copy, and KeePassDX applies it [R ground-keepass 5; R ground-android 3.1].
3. A stale copy that still holds a good snapshot is merged in. The snapshot arrives, is named by the
   record, and does not count.

**Outcome.** No gate in any copy. Create makes a new authority and every client pairs again, or the
operator restores a whole backup file and loses everything after it. Today the authority survives
every save measured and every merge but a KeePassXC merge from 2.4.2 on, which replaces the whole
dictionary with the copy loaded later [M ground-measured 3.1], and no program but KeePass offers a
command that deletes it, Database Maintenance's Plugin Data tab. The design names the cliff
[design 14.2]; it does not say that a readable copy of the store is sitting in the file and is
refused.

**Rating: serious** (availability, and the operator's only way back is to re-pair everything).

**Change.** In `gone`, offer to adopt the newest named snapshot that reads under this store's key.
On its own that is the rollback of 2.2, so it depends on 2.2's version witnesses and 2.5's
revocation entries to fail closed where it matters.

## 3. Minor problems

### 3.1 The authority's private key shows up where today it shows nowhere

| Where | Today | Under the design | Basis |
| --- | --- | --- | --- |
| KeePass's own windows | nowhere: the Plugin Data tab lists custom data by size, not value | the entry dialog's Advanced tab, opening the field, shows the whole store in clear in a text box, and it can be copied | [R here `KeePass\UI\UIUtil.cs:3864-3873`, `Forms\DatabaseOperationsForm.cs:73-75`; `Forms\EditStringForm.cs:138`] |
| KeePass File, Print and its HTML export | not printed | the root group is printed, and custom strings are printed in clear, protected or not, when that box is ticked (a remembered setting) | [R here `Forms\MainForm.cs:822-825`; `Forms\PrintForm.cs:410`, `:735-739`, `:900-920`; `DataExchange\Formats\KeePassHtml2.cs` uses the print form] |
| KeePass's KDB 1.x export | not written | custom strings appended to the entry's notes | [R here `DataExchange\Formats\KeePassKdb1.Write.cs:280-297`] |
| KeePass XML export | in clear | in clear | [R ground-inventory P26] |
| KeePassXC HTML export | not written (custom data is left out) | protected custom strings in clear | [M ground-keepassxc 8] |
| Keepass2Android, KeePassDX | no screen shows database custom data | the entry view shows custom strings, protected ones behind a toggle, and copies them | [A] |
| Copy and paste of the entry into another database | not possible | carries the key there, where the copy reads as a copy and stops that database's gate | [A for the paste; design 5.2 for the state] |
| History | none | an edit by a person in any program makes a history item holding the whole store | [R ground-keepass 9, entry dialog OK] |
| Search, autofill, references | unreachable | skipped while in the group; a snapshot elsewhere still counts [design 3.1] and is searched as an "other string" by Keepass2Android and KeePassDX [R ground-android 4.2], and KeePass's references search it (3.2) | |

**What contains it, and what the design should say.** The key alone admits nobody, because a
certificate needs a record in the file (E2), and the host key is served on loopback only, where
local code execution is conceded. So this is an exposure of a secret whose misuse needs the file
anyway. The design answers it with "both are the operator's own act" [design 9], which misses that
before this design the key was visible nowhere. Say the containment in `CA.md`, and weigh an
attachment as the carrier of the authority member once the design's 17.2 has measured the Android
views: KeePass's print and KeePassXC's HTML export carry no attachments.

**Rating: minor.**

### 3.2 The API exclusion: four things the design leaves open

- **Decided per entry when it is served, never from the whole pass.** KeePass's File, Import shows
  no progress window by default: `ShowImportStatusDialog` defaults to false, and the import then
  blocks the window and writes to the status bar [R here `DataExchange\ImportUtil.cs:107-111`,
  `UI\OnDemandStatusDialog.cs:173-202`, `App\Configuration\AceUI.cs:171-172`]. So the design's
  trigger "when KeePass's progress window closes" does not fire, and its assumption that an import
  shows that window [design 5.1] is answered no. A Create new IDs copy then lands under a random
  identifier in a `KeePassGate` group with a random identifier, which is not the fixed store group.
  Until the next save, unlock or opening of the tab, the only mark on it is its `KPG.Store` string.
  A vault that hid the snapshots the last pass found, rather than every entry holding that string,
  would serve the authority's key to a client holding `read` and `protected` at the root.
- **`deleteGroup` over a stray snapshot.** A group holding a snapshot somebody moved there has to
  stay "still holds something", `409`, which the raw count gives today [R here
  `Bridge\KdbxVault.cs:489-497`]. That tells a client something it cannot see is there, as the bin
  already does. A refactor that counted the filtered view would move the snapshot to the bin or,
  where there is no bin, delete it for good: `gone`, and every client pairs again. Test both ways.
- **The `KPG.` name refusal changes the documented API.** `createField`'s name rules list every
  reason a name is refused [R here `Spec\openapi.yaml:1336-1400`], so a new reason changes
  `openapi.yaml` and `API.md`, which the design says do not change [design 10]. Which answer it
  gets, `400` or `409`, needs deciding.
- **Field references.** The design calls a reference into the store "the operator's own
  placeholder" [design 10]. Any client holding `update` writes field values. KeePass's
  `{REF:I@O:text}` searches protected custom strings with the searching switch ignored [R here
  `KeePass\Util\Spr\SprEngine.cs:629-664`, `KeePassLib\PwGroup.Search.cs:261-264`]. It is only a
  match oracle, since the field a reference returns is one of T, U, A, P, N and I
  [R here `SprEngine.cs:586-608`], and the result reaches only the operator's clipboard or target
  window.

**Rating: minor**, provided each becomes a test over every route.

### 3.3 A witness is taken away by a node under its identifier

[M here, P2]: a deletion record at +10 s naming nothing, and in the other copy an entry carrying
its identifier at +20 s. After KeePass's Synchronize, in both directions, the record is gone and the
entry is there. With the entry at +5 s the record stays and the entry goes. Witness identifiers are a
public function of the certificate digest [design 4], so whoever can put a node with a chosen
identifier into either copy (pykeepass, or a crafted file the operator synchronizes) can take a
witness away. The plugin's next write puts it back while the join still holds the revocation, so
this matters only together with 2.2 or 2.5.

**Change.** Derive witness identifiers with the store key, an HMAC of the digest, so only a reader of
the store can name one. **Rating: minor.**

### 3.4 The store key outlives Recreate and Delete

The key is made with the store's first snapshot and copied into every later one [design 3.2].
Recreate is the operator's remedy for a copy that has left their hands. Today it makes the old
authority's key useless for anything the new authority accepts. Here the old copy's holder still
holds this store's key and can write snapshots the gate takes as its own, with no "two stores"
warning. Together with Database Maintenance clearing the witnesses, a forged snapshot that drops the
retirement serves the old authority again. Every step needs the file to be written, which is
conceded, so this narrows the gap between what Recreate promises and what it does rather than
opening a new one.

**Change.** A new key at Recreate and Delete, with the old snapshots named in `over`. The cost is that
a stale copy that wrote after the fork becomes two stores (undecided), as two authorities are today.
**Rating: minor.**

### 3.5 The store group's own fields come from the newer group

A group merges whole by its time [M ground-keepass 3]. A stale or crafted file synchronized into the
database can turn searching and auto-type back on for the store group, and replace its notes. With
searching on, the store is in KeePass's search and reports and in the Android programs' search and
autofill [R ground-android 4.2]; the notes can carry any text. **Change**: the plugin's next write
puts the group's flags, name, icon and notes back. The group carries no rule, so its time means
nothing. **Rating: minor.**

### 3.6 An Edit Entries dialog open anywhere stops the store

`GroupDialogs.Blocking(PwEntry)` answers "an Edit Entries dialog is open on entries of this database,
and this may be one of them" for any entry of the database [R here `Bridge\GroupDialogs.cs:617-627`].
Asked about the snapshots a write adds and deletes, it refuses every pairing, renewal and
self-revocation while the operator bulk-edits anything, where today those writes touch no entry and
go ahead. **Change**: name the snapshots in the question so that a bulk edit is checked against its
selection, or accept `503 busy` and say so. **Rating: minor.**

### 3.7 Owed answers become a snapshot written inside somebody else's save

Owed standing answers ride every save at `FileSavingPre` [R here
`Bridge\DatabaseWriter.cs:327-372`]. Under the design each is a snapshot write [design 7.1]: inside
the writer's own save, where a failed save must then take back two snapshot writes in order rather
than keys (`_carried`, `DatabaseWriter.cs:306-324`), and inside the second `FileSavingPre` that a
synchronize raises after its merge [R ground-keepass 4]. Nothing here is wrong in principle; it is
new code on the path that decides E7, and it needs tests for a pairing whose save fails with an owed
answer riding it. **Rating: minor.**

### 3.8 Remove copies can delete the only store

[M here, P1]: a database imported into itself with Create new IDs, then KeePass's Delete Duplicate
Entries. It removed three entries, among them **the original snapshot under its own identifier**,
with a deletion record naming it, and left the copy. The design offers **Remove copies**, which
"deletes the copies alone" [design 5.2]. Pressed here, it deletes the only snapshot left: `gone`,
and every client pairs again. **Change**: Remove copies is not offered, or refuses, when no snapshot
would be left reading under its own identifier.

The design's row for this case [design 9] cites "5 entries removed, the snapshot under its own
identifier among them", but its recorded output shows only the two U-set runs, each "removed 4" with
both snapshots left [M design: `summary.txt` lines 207-210]. The outcome the row states is right
for a self-import [M here]; the run it cites shows something else. **Rating: minor.**

### 3.9 A 0.1.0 plugin anywhere serves what this one revoked

`v0.1.0` stores the same keys as master: `KPG.Authority.`, `KPG.RetiredAuthority.`, `KPG.Client.`,
`KPG.Revoked.` [R here `v0.1.0:src/KeePassGate/Authority/AuthorityKeys.cs:42`, `:55`, `:98`, `:137`].
A migration that removes them removes them in one copy, and a removal of a key travels nowhere
[M ground-inventory P1, P2], so any stale copy brings them back. A database left unmigrated keeps
them anyway. To a 0.1.0 plugin they are a live authority with its records and none of the
revocations made since, which are in the snapshot, so a client revoked under the new plugin is
admitted by an old one. The design says the old plugin must first be gone from every machine
[design 15]; that is a lasting condition, not a step.

**Change.** At migration, and when a store is first made in a database that holds `KPG.Authority.*`
keys, write `KPG.RetiredAuthority.<fp>` for each. It is an add-only key every KeePass merge keeps,
and 0.1.0 reads it as retired, so an old plugin serves nothing. **Rating: minor.**

### 3.10 The protected-string gain is mostly given back

The design counts it a gain that KeePass now holds the store as a protected string [design 12, E9
and E10]. To check the seal and parse the document, the plugin decodes the whole store to text, and
the parsed result it keeps between requests [design 5.1] holds the authority document as an
immutable string for the session. Today's position, conceded in `CA.md` §208 [R ground-inventory
P14], is no worse. **Change**: read through the piecewise reader the plugin already has for
protected values (`DESIGN.md` §145), keep parsed keys in the arrays already overwritten at lock, and
drop the text. Or make no claim. **Rating: minor.**

### 3.11 E8 goes from structural to a property every route must keep

Today no route can reach custom data, by construction [R ground-inventory P15]. Under the design the
exclusion is code in the vault's lookups and enumerations, about a dozen sites in `KdbxVault` and
`DatabaseNodes` beside the bin's [R here `Bridge\KdbxVault.cs:138-345`, `:2173-2202`;
`Bridge\DatabaseNodes.cs:52-57`], and every future route has to be built over them. The design says
so [design 10]. What it needs in addition is one test that walks every operation in the routing
table against a store group, a snapshot in the bin, at the root, in a user's group, and under a
random identifier holding only the string, so that a new route fails the test rather than leaking the
key. **Rating: minor.**

## 4. KeePass's own functions and the present guards

Each function, what it does to the store, and whether anything here goes beyond the design's own
section 9.

| Function | What it does to the store | Basis | Beyond the design |
| --- | --- | --- | --- |
| Entry dialog on a snapshot | its OK writes the strings back; writes are refused while it is open | [R ground-keepass 9; design 7.4] | an edit makes a history item holding the whole store (3.1) |
| Edit Entries | blocks every store write while open | [R here `GroupDialogs.cs:617-627`] | 3.6 |
| Group dialog on the store group | its OK moves the group's time and writes its fields back; nothing of the store is in it | [R ground-keepass 3] | harmless |
| Duplicate Entry, Duplicate Group on a user node | carries custom data today; carries no rule under the design | [R here `PwEntry.cs:321-354`, `PwGroup.cs:1437-1446`] | 2.3 |
| Duplicate Entry on the snapshot, copy and paste | a copy, undecided | [design 9] | paste into another database stops that database's gate (3.1) |
| File, Import (Create new IDs by default) | a copy in a group with a new identifier; no progress window, so not noticed until the next save | [R here `ImportUtil.cs:107-111`] | 3.2; and 2.1 for a foreign store |
| Delete Duplicate Entries after a self-import | deletes the original, keeps the copy | [M here P1] | 3.8 |
| Synchronize | the join holds; deletion records travel | [M design] | witnesses removable by a node under their identifier (3.3) |
| Database Maintenance, remove deleted objects | clears every witness and snapshot record | [R ground-keepass 5] | with 2.2's version witnesses, those go too until the next write |
| Empty Recycle Bin, delete for good | `gone` | [M design] | 2.6 |
| Print, HTML and KDB 1.x export | the key in clear | [R here, 3.1] | 3.1 |
| Field references | a match oracle over the protected store | [R here, 3.2] | 3.2 |
| Templates group pointed at the store group | an entry made from it is a copy, undecided, hidden from the API | [R ground-keepass 9] | minor |
| Triggers importing into the active database | a copy at every run | [R ground-keepass 9] | minor |

**The present guards.**

- **`GroupDialogs`** still holds for the vault's writes. For the store it now has to answer about
  entry dialogs and Edit Entries, at an availability cost (3.6). The refusal while Database
  Maintenance is open goes for the store, correctly, since that dialog no longer copies anything of
  the plugin's.
- **The single writer** still holds, but the dialogs' OK now goes through it and can be refused after
  the dialog has gone (2.4).
- **The save that is read back** still decides E7. Its take-back becomes entry-level and must
  handle snapshot writes nested in the same save (3.7).

## 5. What it costs here

### 5.1 Code

The design's own table [design 7.4] lists the storage classes. Line counts at `a42e38aa`
[M here, `wc -l`]: `CustomDataStore` 110, `AuthorityKeys` 267, `AuthorityStore` 643,
`AdmissionStore` 854, `ProfileNames` 277, `RenewalRefusal` 150, `PendingChanges` 971, `NodeDialog`
457, `NodeDialogs` 84, `GrantPanelFactory` 114, `KdbxNodes` 152, `DatabaseNodes` 93, `AclDocument`
294, `AclResolver` 537, `AuthorityChooser` 537, `UndecidedTarget` 184, with parts of
`DatabaseWriter` 2179, `OpenDatabaseTargets` 1642, `GroupDialogs` 1228, `KdbxVault` 2349 and
`Expected` 289.

**What the table leaves out**: `AuthorityPanel` (2361 lines), for three new states and three new
buttons; `DatabaseProfiles` (489) and `ClientReaches` (96), which count rules from the tree;
`ApprovalBook` (494), whose held approvals are "compared under the writer's lock at the write with
the authority, the profile list and the renewed record", now with the join; `PairingHandler` and
`RenewalHandler`, which read the records and the standing answers; `AuthorityService` (349);
`MainWindowDatabases` (244), where the whole pass would be triggered. Thirty-two plugin files
mention `CustomData` [M here, grep]. The changes in sections 2 and 3 add the revocation entries,
the version witnesses, the copy hint and the overlay, and keep the reflection on `m_sdCustomData`
that the design removes.

### 5.2 Tests and measured facts

Fifty-two test files mention `CustomData` [M here, grep]. `m_sdCustomData` is pinned in
`GrantEditorInRealDialogsTests` and `KeePassContractTests`. Losing their reason as tests of the
plugin's store, though what they measure stays true of KeePass: `CustomDataMergeRuleTests` (338
lines), `MergeInRealDatabaseTests` (329), `AuthorityMergeTests` (295), the plugin side of
`SnapshotHazardTests` (256), the reconcilers in `PendingChangesTests` (886). Whether those stay as
facts about KeePass or go under "no dead code" is a ruling. From CLAUDE.md's measured facts, the
custom data merge rule, the KDBX 4.1 decision and the trap section stop being load bearing for the
plugin's data.

**New hazards that need new tests**: the exclusion over every route (3.11); `deleteGroup` over a
hidden snapshot (3.2); the `KPG.` name refusal; the join's laws with the digests (the design's 17.5);
each of the five states and each button, foreign stores included (2.1, 3.8); witnesses against
nodes under their identifiers (3.3); the grant editor's apply after OK, Add Entry, Cancel and a
refused writer (2.4); a failed save carrying an owed answer (3.7); an import that shows no progress
window (3.2); Delete Duplicate Entries after a self-import (3.8). The end to end tier and the
screenshots change too: KeePass's tree now shows the store group in every picture of the main window.

### 5.3 Documents

CLAUDE.md's decided bullets on custom data, KDBX 4.1, the grant editor and the replay, and its trap
section; `CA.md` §204 and the storage sections; `DESIGN.md` §131, which is the falsifier firing, and
§139; `PAIRING.md` on held approvals; `API.md` and `openapi.yaml` for the field names (3.2); the
operator's `docs/` for a visible group, new states and new buttons.

### 5.4 Carrying over 0.1.0 databases

Possible in one write [design 15], and by the precedent that an older stored shape reads as
nothing, not owed. Either way the old keys stay in the file, so 3.9's retirement keys are needed
whether or not anything is carried over.

## 6. Assumed in this critique

- That a pairing's save pumps messages so that an OK elsewhere is processed while the writer's lock
  is held (2.4).
- The chain in 2.5 as a whole; each link is measured or read.
- That KeePassXC's clone copies node custom data (2.3).
- What Keepass2Android and KeePassDX show of a protected custom string, and that they copy it; that
  pasting the entry into another database carries it (3.1).

## 7. What I ran

`probe.ps1` in Windows PowerShell 5.1 over KeePassLib 2.62 loaded as an assembly from
`.keepass\2.62\KeePass.exe`, AES-KDF at one round, password `probe-only`,
invented content, files under `spikes\storage\probes\wildcard\security-and-fit\work2\`, output in `out\probe.txt`.
No window, no KeePass process, nothing under the worktrees or the main checkout written.

- **P1.** A database with a store group under the root (searching and auto-type off), one snapshot
  entry with an identifier carrying the mark and the strings `KPG.Store` and `KPG.Seal`, and two user
  entries. Saved, opened twice, merged into itself with `CreateNewUuids`, then
  `DeleteDuplicateEntries`. Result: 3 removed, the original snapshot's identifier among the deletion
  records, the copy left.
- **P2.** A deletion record at +10 s for an identifier naming nothing in one database, and an entry
  under that identifier at +20 s, then at +5 s, in another; `MergeIn` with `Synchronize` in both
  directions. At +20 s the record was gone and the entry present in both directions; at +5 s the
  record was kept and the entry gone in both.
