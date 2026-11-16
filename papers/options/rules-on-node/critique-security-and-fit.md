# Critique: rules on the node, read for security, KeePass's own behavior, the operator and the cost

Key: `rules-on-node-security-and-fit`. Written 2026-11-15 against `option-rules-on-node.md` as it
stood that day, KeePassGate master `a42e38aa` (the product at `a42e38aa`) and KeePass 2.62.

This attacks the design. It does not restate the design's own section 10 except where that section
understates a problem.

**How each statement is marked.**

- **[M here]**: measured for this critique by the probe in section 5, over KeePass 2.62's KeePassLib
  and `keepassxc-cli` 2.8.0, console only, invented databases.
- **[M keepass]**, **[M keepassxc]**, **[M others]**: measured in that ground report, section named.
- **[R file:line]**: read from source. KeePass paths are inside `KeePass-2.62-Source.zip` as the
  `keepass` ground report verified and unpacked it; KeePassGate paths are under
  `src\KeePassGate\` of the product at `a42e38aa`. **[R design §n]** is what the design under attack says.
- **[A]**: assumed. Every assumed statement is listed again in section 6.

## 0. The verdict

**Viable with changes, and narrowly.** The design's central promise, "another program can roll a
rule back, delete it, copy it or edit it by hand, and the gate either still enforces the sealed rule
or refuses; it never enforces the stale, copied or edited text" (design §0.5), is false in two chains
measured here. Both come from the same place: the seal log is made of entries, and entries are
recycled, deleted and resurrected by ordinary acts in every program, and a deletion travels through
KeePass's and Keepass2Android's merges. A database custom data key that no merge removes was the
present store's only-added unit; an entry is not the same thing.

1. **A recycled head seal plus an ordinary rollback puts the older, wider rule back in force**, with
   no refusal (P1) [M here].
2. **The design's own prune turns a refusal into a widening** when KeePassXC resurrects a deleted
   entry from a stale copy (P2) [M here].
3. **Any OK of an entry dialog seals whatever text it finds**, including a hand edit, a template's
   rule and one side of two heads (P3) [R design §4.1; R PwEntryForm.cs].
4. **One rule entry out of place refuses the group it lands in**, and at the root that is every
   root-anchored listing and every grant the root's rule makes, for every client (P4) [M here].

Against that, a good deal holds up and is better than today: a copied entry, a duplicated group, an
import with new identifiers, a hand-edited text, a deleted `KPG` field and a restored revision all
fail closed, where today they carry or restore their rule. The entry dialog mechanism the design
relies on exists in 2.62 as described, and in the order the design needs (section 2, (c)).

With the changes named in P1 to P6 the option meets E5 and E6 except for a residual: a person
recycles the newest seal of a node in a copy where that node was rolled back, and the newer text then
also leaves history before KeePass saves. That is the same kind of concession as removing a
revocation key on Database Maintenance's Plugin Data tab, but easier to reach.

**One observation beyond this lens, because the findings bear on it.** None of P1 and P2 comes from
the text being on the node; P4, P5, P7, the hiding on every route (P11) and most of the operator's
burden do. The design's "honest weakness" paragraph already names the alternative, the text kept
in the seal's entry. These findings strengthen that paragraph rather than weaken it.

## 1. Problems, most severe first

### P1. A recycled or deleted head seal, after an ordinary rollback, enforces the older and wider rule. Serious

Steps [M here, `attack.txt` section S1]:

1. Base: entry X carries `KPG` = R0, `{"default":"inherit","agent":["read","list","protected"]}`,
   sealed by S0.
2. Copy A, KeePass with the plugin, narrows X to R1, `{"default":"inherit","agent":[]}`, at +10 s.
   The plugin adds S1, naming S0.
3. Copy B, not yet synchronized, changes X's password at +20 s, in any program.
4. KeePass Synchronize, A with B arriving: X's current version is B's, carrying R0; A's version with
   R1 is in X's history; S1 arrives. The read: one head, S1; R1 found in history; **R1 in force**.
   This is the design's scenario 01, reproduced.
5. In any copy, somebody presses Delete on the entry "Rule seal 4F0C91AA" that S1 is. KeePass
   recycles it; KeePassXC's `rm` recycles it [M keepassxc §6]; Keepass2Android and KeePassDroid
   recycle it [R android §4.2].
6. The read in that copy: S1 is not in the seal group, and the design reads seals only from that
   group (design §3 step 2), so S0 is the one head, X's current text R0 is named by S0, and **R0 is
   in force, from the node**. The same after a KeePass Synchronize of that copy with the merged one,
   because the synchronize merges the move by its location time [M here; M keepass §2.2]. The same
   when the seal is deleted for good with a deletion record [M here].
7. Had the repair already run on X, the same recycle refuses ("a text no seal names") [M here].

Outcome: `agent` reads X's password again. Nothing refuses, and the audit records a permitted read
under the rule the operator replaced.

Why the design does not see it. Its probe has one seal per node in its base, so scenario 10 ("B
deletes X's seal for good") removes the only seal and refuses [R the design's `Program.cs:188-190`,
`:379-391`]. Section 6.2's row "Delete a seal for good" is that single-seal case. A chain of two,
which every rule change after the first produces, was never run.

Why the window matters. The repair rides only the plugin's own write of that entry and the
operator's OK on its dialog (design §4.1, §4.3), and nothing is written at open (O1). So a node
rolled back by a merge in Keepass2Android or KeePassXC, and never written by the plugin since,
stays one Delete away from its older rule for as long as nobody touches it. The same holds when the
head is `none` and removed a wide rule.

The design concedes "a holder of the master key who sets out to remove a rule and its seals" (§9).
This is one press of Delete, in any program, on an entry whose title says nothing to a person who
does not run the plugin.

Fix, all three:

- **Bind each text to its seal and look at history as well as the node.** Carry the sealing
  seal's identifier beside the text (inside the `KPG` value or in a sibling string), and refuse at a
  node when any version found on its carriers, current or in their history, names a seal that is
  missing or not reachable from the head. In step 6 the history item carrying R1 names S1, which is
  gone: refuse.
- **Repair at `FileSavingPre`.** KeePass's File then Synchronize merges and then saves through the
  normal save path, which raises `FileSavingPre` before either file is written [R keepass §4, not
  run]. Putting the head's text back on every rolled-back carrier there closes the window for every
  merge KeePass runs. Owed standing answers already ride every save this way (P18, O4), so this is
  the precedent, but it is a new kind of write at a save and the operator decides it.
- **Record the residual.** After the newer text has also left history (ten later edits, Database
  Maintenance by age, a history limit of 0, Strongbox's trim at every save [R others §3]), the older
  rule is back in force and nothing in the file can tell.

### P2. The prune of section 4.5 turns a refusal into a widening under a KeePassXC merge. Serious

Steps [M here, S2]:

1. Base: X carries R0, sealed by S0. Copy B is the base, kept and not synchronized: a phone, a
   backup on a share.
2. Copy A narrows X to R1 (S1), then deletes X for good. On the plugin's next write the prune removes
   S0 and S1, because X is no longer in the database (design §4.5).
3. `keepassxc-cli 2.8.0 merge -s` with A the target and B the source. KeePassXC applies no
   deletion [M keepassxc §2], so X comes back from B carrying R0, and S0 comes back from B. S1 is in
   no copy.
4. The read: one head, S0; X carries R0: **R0 in force** [M here]. The same whether the prune left
   deletion records or not [M here].
5. Without the prune, the same merge finds S0 and S1 in A: head S1, R1 nowhere: **refuse** [M here].
   KeePass's Synchronize deletes X again in all three variants [M here].

Outcome: an entry the operator narrowed and then deleted comes back through an ordinary KeePassXC
merge with the wider rule in force. KeePassXC bringing deleted entries back is routine [M keepassxc
§2, §4]; the plugin's own prune is what makes it a widening.

What the design says: "A node that comes back from a stale copy after that returns with a text and,
where the merge does not apply deletions, with its seals too; where it returns without them it
refuses" (§4.5). It returns with the stale copy's seals, which are the older ones.

Fix: never prune a node's seals. A seal is 144 characters (design §2.3), one per rule change, and
section 2.3 already accepts that superseded seals stay. If pruning is wanted later, replace a gone
node's chain by one seal that names every head it had and is itself never pruned, and run S2 in
every program that merges.

### P3. Any OK of an entry dialog seals what it finds. Serious

Steps [R design §4.1; R `KeePass/Forms/PwEntryForm.cs:1066-1149`]:

1. Any of these: in KeePassXC or on a phone somebody widens X's `KPG` string by hand; the operator
   makes an entry from a template that carries a rule, which KeePass opens in its Add Entry dialog
   with the template's strings; two copies each changed X's rule (two heads).
2. The gate refuses X, as designed.
3. The operator opens X in KeePass to change its password. The dialog opens on its first tab, not
   on the KPG tab where the refusal is explained [A: that the operator does not look]. OK.
4. KeePass raises `EntrySaving` and `EntrySaved` on every OK of the entry the dialog edits, Add Entry
   included (`bPri`, `:1072-1079`, `:1148-1149`). The design: at `EntrySaved` "the plugin adds the
   seal, if the text now on the entry is not the head's", and "for two heads, any OK seals the rule
   shown after both".

Outcome: the found text becomes the head. The hand-widened rule is in force; a template's rule is in
force on every entry made from it, which section 6.2 says refuses; for two heads, whichever rule the
tab happens to show wins. The refusal becomes a prompt that the most ordinary OK answers yes. For
the group dialog, section 4.2 applies the buffer at OK and does not say whether an OK with no change
on the tab resolves two heads.

Fix: seal a found text, or resolve two heads, only on an explicit act on the KPG tab, a button that
names what it keeps. On every other OK, write the head's text back at `EntrySaving` (the repair), or
leave the node refusing. For two heads, offer the per-profile intersection, never either side.
Live tests for each.

### P4. One rule entry out of place refuses where it lands, and at the root that is every root-anchored answer. Serious

Steps [M here, S3; R design §3 step 1 and §6.2]:

1. The root's rule entry sits in the root's own entry list (design §2.2); a group's sits among that
   group's entries.
2. Ordinary acts move one: in KeePass, Ctrl+A in a group and a drag or Move to another group;
   deleting what looks like a stray entry; Keepass2Android inserting an arriving entry at the root
   when its group is missing, and KeePassDX 3.2.0 dropping it [R android §1.5].
3. The read: the group it lands in holds a text it has no seal for, and the group it left has a seal
   and no carrier; both refuse. With U's rule entry moved to the root: "root: REFUSE (a text and no
   seal) | U: REFUSE (the sealed text is nowhere)" [M here].

Outcome at the root: the resolver refuses wherever a walk reaches the refusing node
(`Acl/AclResolver.cs:484-491`), so every grant the root's rule makes and every walk that gets to the
root refuses, for every client of the database. `listGroups` and `listEntries` are anchored at the
root [R `Spec/openapi.yaml:713-718`, `:1103-1109`], so every tree listing and every sweep fails.
Rules nearer a node keep deciding.

A related case the design's section 2.2 does not cover: `KPG.Node` keeps Delete Duplicate Entries
from deleting one of two rule entries [M design probe], but after File then Import with Create new
IDs, the copy of a rule entry carries the same `KPG.Node` as the original, and the two are duplicates
again [R `KeePassLib/PwGroup.cs:1437-1447` for the copy; R keepass §9 for the comparison]. Which of
the two KeePass deletes is [A].

Fix, and why not the obvious one. "Count a carrier only at the group its `KPG.Node` names" looks
right and opens a widening: KeePass's Duplicate Group is a deep clone with new identifiers
[R `KeePass/Forms/MainForm.cs:2313-2330`, `PwGroup.cs:1437-1447`], so the copy holds a rule entry
naming the original, and the design's refusal is what closes the copy and every unruled entry in it.
Ignore it there and those entries inherit what is above, where today the copy carries the rule. So:

- **Keep the root's rule out of the root's entry list**, in the KPG group, since the root never
  moves, is never deleted and is never duplicated, which are the reasons for keeping a rule on its
  node; and ignore, and report, any carrier found among the root's entries. Entries moved to the
  root with it then inherit the root's rule, as they do today.
- **Keep the design's refusal everywhere else**, and say on the group's own tab what was found and
  where it belongs.

### P5. Names KeePass cannot tell from `KPG` and `KPG.Group` pass today's name check. Minor

Steps [M here, S4 and `collation.txt`; R `Pipeline/VaultHandlers.cs:1141-1157`,
`Spec/PathTemplate.cs:200-222`]:

1. U+034F COMBINING GRAPHEME JOINER is category Mn, not Cf. `Formats` refuses only Cf and
   `IsCapture` only controls and climbs, so the field routes accept it in a name.
2. KeePass's string dictionary is a `SortedDictionary` under the default comparer
   [R `KeePassLib/Collections/ProtectedStringDictionary.cs:42-43`], and .NET Framework 4.8 collates
   U+034F as nothing in en-US, de-DE, tr-TR and lt-LT [M here]. An entry given only
   `K<U+034F>PG.Group` answers `Strings.Exists("KPG.Group")` true, and an ordinal reserved test does
   not catch the name [M here]. Setting `KPG<U+034F>` on an entry holding `KPG` replaces the value of
   `KPG` and keeps its key [M here].
3. A client holding `create` at the root creates an entry there and gives it that field.
4. Read as the design's probe reads carriers, with `Strings.Exists` [R the design's
   `Program.cs:239`]: the root holds a text and no seal: REFUSE [M here]. That is P4's outcome,
   caused by the least privileged client.

Why minor: the design asks for a reserved test "byte for byte and with the comparer KeePass's string
dictionary uses" (§7), which catches every one of these names if the prefix test is the culture's
(`IsPrefix` measured true) and runs in KeePass's culture.

Fix: the reserved test by KeePass's collation, for prefix and equality, in KeePass's culture, on
`createField`, `createEntry` and every name a route takes; the reader by ordinal match over the keys,
so a look-alike written by another program is never a carrier; tests with U+034F at each position.

### P6. Databases made with 0.1.0 lose every rule, silently. Serious, if any such database is in use

Steps [R design §11; R `Bridge/KdbxNodes.cs:65-73`]: 0.2.0 stops reading node custom data. A 0.1.0
database keeps its rules there. Opened by 0.2.0 before the operator presses a migration, every node
reads as silence and inherits: an entry narrowed to `"agent":[]` under a group granting `agent`
`read` is readable. After a migration, a merge from a copy that was not converted, where that copy's
node wins, brings back an item that means nothing, and nodes ruled only there inherit (the design
says so; the operator being told before pressing does not help a copy merged later).

The precedent O7 cites, an old single `KPG.Authority` key read as no authority, fails closed. This
one fails open, so the precedent does not carry over.

Fix: read a node custom data `KPG` item as a refusal at that node ("a rule in the old place") for as
long as one exists, with the migration as the way out; or the project decides that no 0.1.0 database
is in use.

### P7. Edit Entries changes rules with no seal and no repair. Minor

[R `KeePass/Util/MultipleValues/MultipleValuesEntryContext.cs:61-81`, `:214-267`;
`PwEntryForm.cs:195-199`, `:1072-1079`, `:1148-1149`.] For several entries KeePass builds one
virtual entry, `new PwEntry(true, true)` with a fresh identifier, and opens the entry dialog on it.
`EntrySaving` and `EntrySaved` fire once, for the virtual entry. The dialog's Advanced list then
applies a changed, added or removed `KPG` to every selected entry, each with a backup. A removal is
served from history until it rotates out and then refuses; a change refuses. Closed, but the design's
"several entries edited together stay read only, as today" holds for the tab and not for the
strings. The plugin's handlers must not seal the virtual entry's identifier, and its `EntrySaving`
must not write a rule into the virtual entry, which KeePass would then apply to all of them. Fix:
do nothing when `MultipleValuesEntryContext` is set, and test it.

### P8. A merged-in foreign file can add a rule to an unruled group, more quietly than today. Minor

Steps [A, from the read rule; R keepass §1d, §2.4]:

1. A prompt-injected client lists group identifiers and learns its own profile name; the API gives
   both.
2. An attacker builds a `.kdbx` holding the KPG group and seal group under the identifiers this
   database uses (fixed ones, if the companion design fixes them as the Keepass2Android templates
   precedent suggests), one seal naming an unruled group G with an empty `KPG.After` and the digest
   of a wide rule, and an entry in G carrying that rule.
3. The operator is induced to synchronize with it, or to import it with a method that keeps
   identifiers. Under File then Import's preselected Create new IDs the seals' identifiers stop
   matching their strings and are not read (design §2.3), which is a real strength.

Outcome: the attacker's seal is G's only head and G grants what it says. Today the same file can
replace G's custom data by carrying a newer G, which also replaces G's name and notes where the
operator would see it; here G is untouched and one more "KeePassGate rule" entry appears. Not a
regression, but the seal does not authenticate anything. Fix, as hardening: authenticate each seal
with a key derived from the authority, as the tag key is derived from the host PKCS#12, so that only
a holder of this database's authority makes a seal the plugin reads. A recreated authority then has
to reseal.

### P9. The access map leaves the places custom data stayed. Minor

Rule texts, with profile names and verbs, appear in KeePass's print and HTML export with custom
strings [R keepass §9], KeePassXC's HTML export [M keepassxc §8], every Android program's entry
screen and search [R android §4.2], KeePass's Find (design §6.1), `{S:KPG}` and so auto-type
(design §7), and a copied custom string on the clipboard [A]. None of it is a secret, but it tells a
reader which agent reaches which entries. This option places no secret: the authority stays where
the companion design puts it. One interaction with that design: the seal log grows without bound in
the KPG group, which there also holds the authority's private key, and an operator who goes there
to tidy seals, on a phone or in KeePassXC, whose HTML export prints protected strings in clear
[M keepassxc §8], is beside the key. Fix: seals in a group of their own, nowhere near the authority,
with nothing in it a person has a reason to open.

### P10. "Seal" already means something here. Minor

The code and CLAUDE.md use "seal" for a rule that stops an inherited grant arriving: `KdbxNodes.cs:31`
("a seal on an entry under a Lithuanian KeePass"), CLAUDE.md's account of step 13 ("a seal does not
take away what a group above grants, it stops it arriving"), and the design's own §4.4 ("local,
inherited and sealed off"). Two meanings in one editor and one log is how an operator misreads a
refusal. Rename the new one before any code.

### P11. The cost is larger than section 4.4 lists. Minor

Section 2, (e), has the list. The items section 4.4 leaves out are the field routes' name checks and
`Clashes`, `FindEntry` and every listing, sweep and count, `deleteGroup`'s emptiness test, the
reference client's `refs` and its vectors, the documents and the screenshots, and four new families
of tests for P1 to P5.

## 2. The lens, point by point

### (a) The caller in the threat model

What holds [R design §7; R `Bridge/KdbxVault.cs:2191-2202`, `:243-312`]:

- No route reads history (`API.md` §407), so a rule in a revision is out of reach.
- No route moves a node; the entry routes are create, read, update, delete and the field and
  attachment routes [R `Spec/openapi.yaml`, the 44 `operationId`s]. A rule cannot be carried away
  through the API.
- `updateEntry` takes only the standard fields [R `openapi.yaml:1217-1223`], so it cannot touch
  `KPG`. The repair riding a client's write puts the sealed text back, and a client's own writes
  that roll a rule back in another copy are healed by the seal.
- One projection, `Seen` at `KdbxVault.cs:243-312`, builds every field a read, a listing of fields, a tag and
  the read ceiling see. Hiding the reserved names there covers those at once.
- The gate never compiles references (`KdbxVault.cs:266-269`), and the reference client refuses
  `{S:KPG}`.

What does not, or needs care: P5 (look-alike names, a whole-database refusal from a client holding
only `create`), P8 (seals carry no authentication), and the reserved prefix hiding user data named
`KPG.<anything>`, which the design accepts. A client also learns from `getEntryAccess` going to
`404` that a node it used to reach now refuses, as it does today for an unreadable rule.

### (b) Secrets

This option moves no secret. Compared with today the access map is more widely seen (P9), and the
seal log invites the operator into the group that, in the companion design, holds the authority's
key (P9).

### (c) KeePass's own functions and the present guards

The mechanism the design depends on exists in 2.62 and is in the order it needs [R
`PwEntryForm.cs`]: `EntryStrings` is public (`:141`), `UpdateEntryStrings` is public "for plugins"
(`:357-361`), `EntrySaving` is raised before `CreateBackup` and before KeePass copies the dialog's
strings onto the entry (`:1074-1088`), and the "anything changed" comparison afterwards sees what
the plugin wrote (`:1119-1135`). The standard fields are the only ones `UpdateEntryStrings(true)`
overwrites (`:366-378`), so a custom `KPG` written there survives.

| KeePass function | What it does to this design's data | Outcome |
| --- | --- | --- |
| Entry dialog OK | `EntrySaving`, backup, strings, comparison, `EntrySaved` [R `:1066-1149`] | works; but P3 |
| Restore a revision | restores, touches the time, closes as OK without `SaveEntry` [R `:1474-1499`] | the old text is current under a new time; the sealed text is in the backup; served from history until repaired. The restored entry wins the next merge, so its old text spreads to every copy |
| Edit Entries | P7 | closed, unsealed |
| Group dialog OK | the tab's own buffer at `FormClosed`; `GroupForm`'s write-back carries no entries | works; two heads unspecified (P3) |
| Duplicate Group | deep clone, new identifiers [R `MainForm.cs:2313-2330`] | the copy refuses, which is stricter than today and is why P4's fix is shaped as it is |
| Duplicate Entry, copy and paste | new identifiers [R keepass §9] | the copy refuses |
| File then Import, Create new IDs, then Delete Duplicate Entries | copies of rule entries share `KPG.Node` with the originals | the defense of design §2.2 fails; a group refuses [A for which is deleted] |
| Delete Empty Groups | a ruled group is never empty | a group the operator emptied stays, held open by its rule entry |
| Delete, Empty Recycle Bin | a recycled seal is not read; an emptied node's seals are pruned | P1, P2 |
| Database Maintenance, history by age; history limits | a rule served from history goes | refuses, as the design says |
| Templates | an entry made from a ruled template is sealed at Add Entry's OK | P3 |
| Find, print, export | rule entries and `KPG` lines included | P9 |

The present guards:

- **`GroupDialogs`** stops guarding rules, as the design says; it still guards the API's writes. The
  prune now deletes seal entries on the plugin's own writes, so it has to ask the same question for a
  seal whose entry dialog the operator has open.
- **The single writer** carries the repair and the prune; nothing else changes for requests.
- **The save that is read back** is unchanged. The rule entry the plugin edits at a group dialog's
  OK and the seal it adds at `EntrySaved` are in memory and reach the file with the operator's save,
  as the editor's writes do today.
- **No read inside a merge** (`MergeWatch`) has more to protect: a read now walks entries' history.
  The design lists it as to measure.

### (d) The operator

What a person sees: one "KeePassGate rule" entry in every ruled group, the root's at the top of the
root; a `KPG` line on every ruled entry; one "Rule seal" entry per rule change, never removed while
its node exists. All of it in every program, on Android in search results [R android §4.2].

What a person can do wrong, with no warning in the program they are using:

- Delete a rule entry: that group refuses (design §6.2). At the root: P4.
- Select every entry of a group and move them: P4.
- Delete a seal that looks like clutter: P1. Deleting all of them refuses everything instead.
- Fix a refusal by hand in another program: a typed rule refuses again until sealed, and once
  sealed by any OK it is in force (P3).
- Read two rules for one entry: the Advanced tab shows the stale `KPG` line while the KPG tab shows
  the rule in force from history.
- Restore a revision to get an old rule back: it does not come back (the design's reversal 5).

### (e) Cost

Rewritten or changed, beyond section 4.4 of the design:

- `Bridge/KdbxVault.cs`: the projection `Seen` at `:243-312`; `FindEntry` at `:2191-2202`; every listing,
  the sweep and the counts; the emptiness test beside `HoldsOnlyTheBin` (`:2142-2149`); `Holds` and
  `Clashes` for reserved names (`:1641-1720`).
- `Pipeline/VaultHandlers.cs`: the reserved-name refusal on `createField` and every field route,
  by KeePass's collation (P5); `BodyCheck` cannot say "by collation", so it is a handler check and
  `openapi.yaml` describes it.
- `Bridge/NodeDialog.cs`, `NodeDialogs.cs`, `Ui/GrantPanel.cs`, `GrantPanelFactory.cs`,
  `GrantSheet.cs`, `GrantTab.cs`, `IGrantStore.cs`: two write paths instead of one, a fourth state,
  the explicit keep of P3, the multi-edit exit of P7.
- `pykeepassgate.refs`, `tests/Shared/ReferenceVectors.json`, `test_refs.py`, for `{S:KPG}`.
- `DESIGN.md`, `API.md` §401 and §407, `CA.md:127-138`, `docs/`, CLAUDE.md's decided bullets on
  grants, the editor and the trap, and the screenshots, since rule entries now show in the main
  window's lists.
- Tests: 37 files under the product at `a42e38aa` name a `CustomDataKey` [M here, a search; some are
  `ProfileNames`'], among them `GrantEditorInRealDialogsTests`, `LiveGrantEditorTests`,
  `AclInRealDatabaseTests`, `DatabaseNodesTests`, `KdbxVaultTests`, `RemovalMergeTests`,
  `TagOfTheCallersViewTests` and `DocumentationScreenshots`.

Measured facts that stop mattering: the editor's reflection on `m_sdCustomData` and its FINDINGS
entry; the `GroupForm` trap as a hazard for rules; KeePassXC's `_LAST_MODIFIED` in node custom data.
The Lithuanian collation note in `KdbxNodes.cs:26-33` stays, because entry strings are collated the
same way (P5). KDBX 4.1 is no longer needed for rules.

New hazards that need tests: P1, P2, P3, P4 and P5 as written here; the order of `EntrySaving`
against the backup and the comparison, in a real dialog; Restore; Edit Entries; Add Entry from a
template; the group dialog's buffer at OK and Cancel for Edit Group and Add Group.

0.1.0 databases: not carried over by anything in the design (P6).

## 3. Where the user's timestamp idea meets this option

The user's idea, JSON in an entry field named by a timestamp so that a merge keeps both, does not
hold within one entry: KeePass and KeePassXC take the newer entry whole and keep the other only in
history, which ties, a full history and Keep existing lose [M keepass §1e; M keepassxc §5]. This
option avoids that for rules by keeping one field per node and putting the only-added part, the
seals, into one entry per version, which is the shape the ground reports say merges as a union.
P1 and P2 show what is left: entries merge as a union until something deletes one, and a deletion
travels in KeePass's and Keepass2Android's merges. A preprocessing step that writes at load would
break O1; `FileSavingPre`, which runs after KeePass's merge and before the write, is the hook the
code base already uses (P1).

## 4. Ratings in one table

| # | Problem | Rating |
| --- | --- | --- |
| P1 | recycled head seal after a rollback enforces the older rule | serious |
| P2 | the prune turns a refusal into a widening under KeePassXC | serious |
| P3 | any OK seals what it finds | serious |
| P4 | a displaced rule entry refuses where it lands; at the root, everything root-anchored | serious |
| P5 | look-alike names pass the name check | minor |
| P6 | 0.1.0 rules silently dropped | serious, if any 0.1.0 database is in use |
| P7 | Edit Entries changes rules unsealed | minor |
| P8 | merged-in seals add a rule to an unruled group | minor |
| P9 | the access map is more widely seen | minor |
| P10 | "seal" already has a meaning | minor |
| P11 | cost understated | minor |

None is fatal: each has a change that brings the option back within E5 and E6, with P1's residual
recorded as a concession.

## 5. What was run

Nothing with a window. KeePassLib 2.62 loaded from a copy of
`.keepass\2.62\KeePass.exe`, and `keepassxc-cli` 2.8.0 from the archive the
`keepassxc` ground report verified against its pinned hash and signature, run with no window.

- Probe: `spikes\storage\probes\rules-on-node\security-and-fit\probe\Attack.cs`,
  compiled with the .NET Framework's `csc`. It models the design's store and read rule the way the
  design's own probe does, including the check of a seal's identifier against its strings, and
  reads seals only from the seal group. It is not the plugin's code.
- Output:
  `spikes\storage\probes\rules-on-node\security-and-fit\out\attack.txt`;
  databases under `spikes\storage\probes\rules-on-node\security-and-fit\work2\`.
- Collation: `spikes\storage\probes\rules-on-node\security-and-fit\collation.ps1` and `collation.txt`, Windows
  PowerShell 5.1 on .NET Framework 4.8.
- Scenarios: S1 (P1), S2 (P2, KeePass Synchronize and KeePassXC), S3 (P4), S4 (P5).

## 6. Not measured here, and assumed

- That Keepass2Android and KeePassDroid recycle a seal on Delete, and that Keepass2Android's merge
  carries the move: read in the `android` ground report, not run.
- That people delete seal entries or move rule entries in practice. This is the falsifier of P1 and
  P4, as section 13 item 8 of the design is of its refusals.
- Which of a duplicate pair Delete Duplicate Entries deletes after a Create new IDs import.
- P8 end to end.
- That the gate's routes accept a field name holding U+034F end to end: read from `Formats` and
  `IsCapture`, not sent through the gate.
- That `FileSavingPre` runs between KeePass's merge and its write in File then Synchronize: read in
  the `keepass` ground report, not run.
- That the operator does not open the KPG tab before pressing OK (P3), and that a custom string can
  be copied to the clipboard from KeePass's entry list (P9).
