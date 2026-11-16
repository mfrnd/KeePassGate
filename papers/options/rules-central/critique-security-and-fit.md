# Critique of `rules-central`: security, KeePass's own behavior, the operator, and the cost

Key `rules-central-security-and-fit`. Written 2026-11-15 against `option-rules-central.md` as it
stands, KeePassGate master `a42e38aa` (the product at `a42e38aa`) and KeePass 2.62. This
attacks the design; it does not edit it.

## How statements are marked

- **[M here]**: measured for this critique, today, by the console probe described in "What I ran".
- **[M probe]**: measured by the design's own probe, as `option-rules-central.md` reports it.
- **[M keepass]**, **[M keepassxc]**, **[M others]**, **[M spike]**: measured in that ground report,
  or in `spikes/revocation-merge` as the ground reports digest it.
- **[R plugin]**: read in the product at `a42e38aa`, file and line given.
- **[R 2.62]**: read in `KeePass-2.62-Source.zip`, the copy `ground-keepass.md` verified, extracted
  under `spikes\storage\probes\ground-keepass\src`, file and line given.
- **[R keepass]**, **[R keepassxc]**, **[R android]**, **[R others]**, **[R inventory]**: read from
  source in that ground report, which names the tag, file and line.
- **[A]**: assumed. Neither run nor read.

## Verdict

**Viable with changes.** Under this lens there are five serious problems and eight minor ones. Each
serious problem has a change that answers it, but one of them, the copy of a node (S4), leaves a
residual that only the operator can accept: if a narrowed secret has to stay narrowed when another
program copies it, nothing inside this option can do that, and the option is then not viable.

What the design gets right under this lens, briefly, so the rest is read in proportion:

- No KeePass dialog holds a rule any more, so the `GroupForm` trap, the entry dialog's write-back and
  Edit Entries cannot touch one, and restoring a revision no longer brings an old rule back.
- Rule entries hold no secret, and KeePass's field references cannot pull `KPG.Data` out of one:
  the wanted field of a reference is only `T`, `U`, `A`, `P`, `N` or `I`
  [R 2.62 `KeePass/Util/Spr/SprEngine.cs:586-606`]; `O` is a field to search in, not to return
  [`:658`].
- Through the API alone, a client cannot read, write, plant or remove a rule (section "Lens (a)").
- The loss of a rule entry becomes visible where today it is silent [M spike], as long as a deletion
  record is left (S2 and S3 are about what happens next).

## What I ran

A console probe over KeePass 2.62's KeePassLib, in Windows PowerShell 5.1, in memory, invented
content, no window, loaded from a copy of `.keepass\2.62\KeePass.exe` whose
SHA-256 matched the original (`AFB89B74…C89C3F3`). Script and output:
`spikes\storage\probes\rules-central\security-and-fit\scripts\probe.ps1`
and `spikes\storage\probes\rules-central\security-and-fit\out\probe.txt`.

- **P1, what KeePass's duplicate paths do to a rule today.** A sealed group with an entry, and an
  entry carrying a narrowing rule, both as node custom data `KPG`, then `PwGroup.Duplicate()` (used
  by Duplicate Group and by a Ctrl-drag copy) and `PwEntry.Duplicate()` (used by Duplicate Entry and
  by Add Entry from a template). Result: new identifiers throughout, **the rule on every copy**, the
  entry's password on the copy [M here].
- **P2, a foreign database plants a rule.** A victim database with the design's store group
  (`FE4040A9…`), a rule entry sealing group `S`, a group `C` under `S` with no rule, an entry `X` in
  `C`. A second database under a **different master key**, sharing nothing with the victim but the
  public store group identifier and `C`'s identifier, holding one rule entry built as §2.2 builds
  one (the mark, `C`'s tag, the digest) and naming `C` with `KPG.After` `none`. Merged into the
  victim with `PwDatabase.MergeIn` under three methods [M here]:

| method | where the rule entry naming `C` ended | the store group | `C` and `X` |
| --- | --- | --- | --- |
| Synchronize | in the store group, marked | **renamed to the foreign group's name, searching and auto-type back to inherit** (the foreign group was newer) | `C`'s and `X`'s modification times unchanged, `X` has no history item |
| Keep existing | in the store group, marked | unchanged | unchanged |
| Create new IDs | an unmarked copy in a new group named after the foreign group | unchanged | unchanged |

## Problems, most severe first

### S1. A rule no longer needs its node, so a file the operator merges can rule any node (serious)

The design separates a rule from the node it governs (§1 points 1, 2 and 6). That separation is
what lets something other than the node carry the node's rule into the file.

**Path A, KeePass's default import.**

1. A prompt-injected agent learns node identifiers: every identifier the API returns is the node's
   UUID [R plugin `Spec/openapi.yaml:2798-2819`], and a field reference such as `{REF:P@I:<uuid>}`
   in an entry it can read names others. It learns its own profile name from `getIdentity`.
2. Somebody outside builds a `.kdbx` under a password of their own holding one entry with
   `KPG.Kind` `rule`, `KPG.Node` the UUID of `C`, `KPG.After` `none`, and `KPG.Data`
   `{"default":"inherit","agent":["list","protected","read"]}`. `C` is a group under a sealed group
   and has no rule of its own.
3. The operator imports it with File then Import, which preselects Create new IDs
   [R keepass 2.4].
4. The entry arrives as an unmarked copy with its four strings intact [M here, P2]. The gate
   answers `503 undecided` and the Rules page offers **Remove copies** (§5.3).
5. The operator presses it and then OK. The copy's derived identifier exists nowhere in the file,
   so "where a copy's original is gone the copy is first put back under its own identifier" (§5.3).
6. `C` now has one version, the planted one. Nearest wins per profile, and a seal "closes the way in
   from above, not the way in from below: a group or entry under this one that names a profile
   itself still grants what its own rule states" [R plugin `docs/access-control.md:403-405`]. The
   agent reads every entry under `C`, protected values included.

**Today** the same import gives the file's nodes new identifiers, and whatever rule the file holds
rides on those new nodes; no node of this database changes [R keepass 2.4; R inventory N1, a rule
lives on its own node].

**Path B, a merge that matches identifiers**: KeePass's Synchronize, Overwrite and Keep existing,
every KeePassXC merge [M keepassxc 4, entries added], Keepass2Android's and KeePassDX's merges
[R android 1.5]. The planted entry carries the mark and its derived identifier, which anyone can
compute (the mark and the formula are in §2.2, the tag is `C`'s first six bytes), inside a group
carrying the store group's fixed identifier. It lands in the store group; `C` and `X` keep their
times and gain no history item; under Synchronize the foreign group also renamed the store group
and turned its searching and auto-type back on [M here, P2]. There is no `503`, and nothing visible
changes on any node. **Today** the same file would have to carry `C` itself with a newer time,
replacing `C`'s name and notes, since a group merges whole and keeps no history [M keepass 3].
§13 records this path as an assumption and treats it as equivalent to today's; it is not
equivalent, because here it is invisible and needs nothing of the node but its UUID.

**Path C, KeeShare [A for the identifiers].** A share is imported in Synchronize mode
[R keepassxc 3, `ShareImport.cpp:95-97`], and an entry the target lacks is cloned with its
identifier [R keepassxc 3, `Merger.cpp:194-199`]; that a share import keeps the container's entry
identifiers is assumed, as the design also assumes. The share's writer holds the share's password,
not this database's master key. A rule-shaped entry in the container lands in the shared group,
and a rule entry "counts wherever it is found" (§1 point 6). Today that writer can change the rules
only of entries it shares, because a rule rides on its node; here it can rule any node whose UUID it
knows.

**What the attack can and cannot do.** A planted version naming `none` is "at most one more newest
version" (§3), so on a node that already has a rule it only narrows, through the meet. It widens
every node that has no rule of its own, which under nearest wins is every node below a seal. Naming
an existing version needs that version's document and chain, which the API does not serve.

**Why it matters.** "A malicious `.kdbx` therefore vouches only for itself" (CLAUDE.md, first
decided bullet); requirement E9 [R inventory]. Here a foreign file vouches for this database's
nodes. Place cannot fix it alone: the fixed store group identifier that makes two copies' groups
one group to a merge [M store-entries, set I] is also what drops a foreign file's entries into it.

**Change.**

1. **Authenticate every rule entry**: a MAC over its four strings, keyed by a secret the database
   holds once and a foreign file cannot supply. The served authority's gate key, already inside
   `AuthorityDocument` [R inventory D1], is the candidate, since a foreign file bringing a second
   authority already ends in `503 undecided` [R plugin `CA.md:249-260`]. A rule entry that fails the
   MAC is damage and its node refuses. The cost: Recreate has to sign every rule again in its own
   write, and a version signed with a retired authority's key counts only as a replaced version.
2. **Remove copies puts back only a copy that verifies and whose derived identifier a deletion
   record in this file names**, which is the Delete Duplicate Entries case §6.4 means, and deletes
   every other copy.
3. Short of a MAC: count a rule entry only in the store group or the recycle bin and refuse its node
   anywhere else. That closes path C and not path B.

### S2. The repair turns a refusal into the older rule on any OK (serious)

§5.2: in the refusing state "the table shows the newest version that still reads", and "OK writes a
repair ... Nothing is repaired without it", OK being the group or entry dialog's.

1. The operator narrows entry `X`: version v1 `{"default":"inherit","agent":["protected","read"]}`
   is replaced by v2 `{"default":"inherit","agent":[]}`.
2. On a phone somebody deletes v2 for good; Keepass2Android's permanent delete leaves a deletion
   record [R android 4.2]. KeePass synchronizes with that copy, the record arrives, `X` refuses
   (§3 point 6). So far the design does better than today, where a deleted rule widens silently
   [M spike].
3. Weeks later the operator opens `X` to change its password and presses OK without looking at the
   KPG tab.
4. The tab held v1, the newest version that still reads. OK writes a repair holding v1's document
   and naming v1 and v2's deletion record. The agent reads `X`'s password again. Had a hand edit
   been the damage, the repair would also have deleted the edited entry, the only trace of what that
   rule said.

**Today** the same kind of state, a rule that will not read, locks the tab until the operator
presses **Discard the unreadable rules**, and the documentation says to write the rules the node
should have before pressing OK [R plugin `docs/access-control.md:424-431`]. The design detects more
than today and then gives the gain back on an unrelated OK.

The two-version state has the milder form of the same flaw: the tab shows the meet, and any OK
writes it, so an unrelated OK makes the narrower meet permanent and drops a grant one copy made on
purpose.

**Change.** A refusing or two-version node is settled only by a press on the tab, as Discard is
today; an OK that did not follow the press adds nothing. In the refusing state the table shows no
rule in force, and the repair starts from a document that grants nothing, or from the meet of every
candidate that reads, the hand-edited entry included. A hand-edited entry is kept until a repair
names it, not deleted by the repair.

### S3. The store shows what looks like duplicates, and the deletions it invites end in silent reverts (serious; §13 understates it)

Every version of one node has the same title, `KPG rule` and eight hex digits of the node, the same
notes, and an empty user name, password and URL (§2.2); replaced versions are never pruned (§5.4).
A node changed ten times is ten entries that look identical in KeePass's list and in a phone's list
[A for what each list shows]. §13 concedes three ways a deletion leaves no trace and adds that "each
needs a deletion of a rule entry first, which no program measured does on its own". The design's own
presentation supplies the person who makes it.

1. Somebody, on a phone or on the desktop, sees ten identical `KPG rule 4F0C91AA` entries and keeps
   one.
2. If the one kept is not the newest:
   - with a deletion record, the node refuses, and S2 follows;
   - deleted by a pykeepass-based program, no record is written [R others];
   - carried by a KeePassDX merge, the record is applied and not kept [R android 3.1];
   - after KeePass's Database Maintenance, **Remove deleted objects information**, no record is left
     [R keepass 5].
3. In the last three, the kept older version is the newest again: the node returns silently to an
   older, possibly wider rule. Database Maintenance is a routine tidy-up, and nothing tells the
   operator that it reopens nodes that refuse.

**Change.** Let the current version tell itself apart: the title carries the version's place in its
chain (`KPG rule 4F0C91AA #3`), and the plugin, at its own writes, moves replaced versions into a
second group with a fixed identifier of its own, so the store group shows one entry per ruled
node. A move changes no content and place means nothing to the read (§3), so nothing else changes.
While any unanswered rule deletion record exists, warn when Database Maintenance opens, which
`GroupDialogs` already watches [R inventory P11].

### S4. A copy carries no rule, and the shape that loses is the shape the documentation recommends (serious; the residual is the project's call)

The design's "honest weakness" names Duplicate Entry, copy and paste, templates, KeePassXC's clone and
imports (§1, §13), and leaves "how often a narrowed node is copied" open as its falsifier (§16.2).
It understates it in two ways.

**It leaves out the group.** Duplicate Group puts the copy beside the original, under the same
parent [R 2.62 `KeePass/Forms/MainForm.cs:2313-2342`]; `PwGroup.Duplicate` gives the group and
everything in it new identifiers [R 2.62 `KeePassLib/PwGroup.cs:1437-1447`]; a Ctrl-drag of a group
does the same [R 2.62 `MainForm.cs:1317-1318`].

1. The root grants `agent` `read` and `list`; group Finance carries a seal, which the documentation
   advises for "a group holding things no program should reach" [R plugin
   `docs/access-control.md:394-407`].
2. The operator duplicates Finance to start Finance 2027.
3. The copy holds every Finance entry with its password [M here, P1], sits under the root, carries no
   rule, and `agent` reads all of it. No KPG tab or page shows anything.

**The falsifier is already answered.** Narrowing below a wider grant is what the seal is documented
for, and the documentation promises today that "a copy of a node carries its rules, whichever way
KeePass makes the copy", templates included [R plugin `docs/access-control.md:452-463`], which P1
confirms on 2.62 [M here]. Both statements become false.

**Remove copies exposes an import.** Create new IDs adds the arriving top-level groups again at the
root [M keepass X1]. After Remove copies, the imported copy of every sealed group sits at the root
with no rule [follows from M probe §6.4]. The page's remedy is the exposure; only closing without
saving is safe.

**Change, partial.** The plugin follows KeePass's own copying commands (Duplicate Entry, Duplicate
Group, the group drag-copy, Paste Entries, Add Entry from a template) and writes a version for each
copy at once, pairing original and copy by position as KeePass itself does
[R 2.62 `MainForm.cs:2332-2338`]. KeePass raises no event for these, so this means hooking menu
items, the tree's drop and the template drop-down: new reflection of the kind §5.1 takes away.
Remove copies is dropped in favor of closing without saving. The documentation reverses "a copy
carries its rules" and recommends granting low over sealing below a grant. **What stays**: copies
made in other programs (KeePassXC's clone, the phones' copies; whether any of them duplicates a
whole group is [A]) and imports of files without rule entries. If the operator holds that a
narrowed secret must stay narrowed whichever program copies it, no change inside this option meets
that.

### S5. Upgrading a 0.1.0 database, or a copy still served by 0.1.0, widens silently (serious)

Rules live today as node custom data `KPG` [R inventory N1]; an absent rule is silence and inherits
[R inventory 1.2, `Acl/AclDocument.cs:110-120`]. The precedent the project would reach for, O7, "an
older stored shape is not read or migrated", fails closed for an authority (no authority, nothing
served) and **fails open for a rule**. §14 leaves the migration undesigned.

1. The operator updates the plugin over a 0.1.0 database. This option leaves data 1 to 6 to another
   option, so the authority and the records still read.
2. The new plugin does not read node custom data: every rule is silence, every node inherits, and
   the first request reaches everything the root grants.
3. Or, after the operator converts (§14's outline), a second machine still running 0.1.0 opens the
   synchronized file: it finds no node custom data, ignores rule entries, and serves the same
   widening.

**Change.** A node that still carries the legacy `KPG` item is a document that will not read, so
it refuses there and below until the operator converts; that is the existing rule for an unreadable
document, not support for the old shape. The conversion has to leave 0.1.0 serving nothing: moving
the authority with option `store-entries` does that by itself, and without it the authority's stored
shape has to change in the same write. Whether 0.1.0 databases exist to carry over is a question for
the project (O7).

### M1. The Escape question cannot be asked right without KeePass's private state (minor)

KeePass's entry dialog asks before closing only when its own entry changed and the close is not the
Cancel button: Cancel sets `m_bForceClosing` unless Escape is held, which KeePass tests with
`GetAsyncKeyState` [R 2.62 `KeePass/Forms/PwEntryForm.cs:1169-1176`, `:2146-2192`]. Today a rule
change is in the dialog's copy, so KeePass sees a changed entry and asks. Here it does not, and the
plugin's `FormClosing` handler sees `DialogResult.Cancel` after the Cancel button, after an Escape
KeePass did not ask about, and after an Escape KeePass asked about and was answered No. It cannot
tell them apart without reading `m_bForceClosing` or `HasModifiedEntryEx` by reflection, which puts
back the dependency §5.1 removes. Asking on all three asks on the Cancel button (Cancel no longer
discards, against ruling O3) and asks twice after KeePass's No; asking on none drops a narrowing the
operator believes was made. The group dialog has no question at all, so a plugin question there
fires on every Cancel. **Change**: copy KeePass's own test, a handler on the Cancel button that
reads the Escape key's state, and accept the second question after KeePass's No.

### M2. Every request walks the whole tree on KeePass's thread (minor)

§4: 1.43 ms for 10,060 entries and 9.38 ms for 50,060, in a console with no gate around it
[M probe]. Inside a hold, KeePass's window waits for the whole of it [R plugin
`Ui/IReadHold.cs:11-25`]; the hold's limit is 5 seconds [R plugin `Ui/IUiThread.cs:148`], far off.
An admitted client granted `create` anywhere grows the tree and so every other client's cost per
request, at about 0.19 microseconds an entry [A, linear from the measured figures]. Today a read of
one entry walks only its ancestors. Nothing to change at ordinary sizes; §16.4's measurement under
the gate stands.

### M3. Everyday copies stop the whole gate, under a word that belongs to another flow (minor)

A duplicate of a rule entry on a phone, KeePassXC's clone, a templates group pointed at the store
group, or the trigger action "Import into active database", which defaults to Create new IDs
[R keepass 9], puts every request at `503 undecided` until the desktop operator acts. That word
today means "choose an authority" and comes with a prompt over KeePass's main window at open,
unlock and the save that ends a synchronize [R plugin `CA.md:260-278`]; the design sends the
operator to a Rules page instead, so one answer has two remedies. **Change**: a word of its own and
the same over-the-window prompt.

### M4. A rule entry in a user's group answers a client's removal with a conflict that reveals it (minor)

`deleteGroup` refuses while `Entries.UCount > 0`, "the group still holds something"
[R plugin `Bridge/KdbxVault.cs:489-498`], while every listing hides an entry carrying a `KPG.`
string (§9 point 2). A client granted `delete` on a group where somebody dragged a rule entry sees
an empty group, is refused, and learns a hidden entry is there. The recycle bin already has its own
sentence for this (`:495-496`). **Change**: the same treatment as the bin.

### M5. A foreign merge takes the store group's name and flags (minor here)

Measured in P2: Synchronize with a newer foreign group of the store group's identifier renamed the
group and set its searching and auto-type back to inherit [M here]. This option reads neither, and
rule entries then appear in search; the password reports still skip them for their empty password
[R keepass 9]. It matters more to option `store-entries`, whose authority entry shares the group.

### M6. The grant editor appears on the store group and on rule entries (minor)

The KPG tab is added to every group and entry dialog. A rule written there governs a node the API
never serves and adds one more look-alike entry. Disable the tab on the store group and on any entry
carrying a `KPG.` string.

### M7. The access policy is in plain view in every program and export (minor)

`KPG.Data` and `KPG.Node` are unprotected custom strings. KeePassXC's HTML export prints every custom
string [M keepassxc 8], KeePass's print can include them [R keepass 9], and a phone lists them. They
name the profiles and the UUIDs of every sealed node, a map of what the operator keeps away from
programs. Whoever reads an export has every password anyway, so this is minor; today the same policy
is in the XML export and on KeePass's own dialogs only [A].

### M8. In a composed design, the operator is sent to browse the group that holds the private key (minor)

§13 says a person has to match hex digits to find a node's rule, in the store group. If option
`store-entries` is taken, that group holds the authority's private key as a protected string, one
reveal or one clipboard copy away in each program [`option-store-entries.md` §11]. A second group
with its own fixed identifier for rules costs one more identifier test in the same walk.

## Lens (a): the caller

What the routes allow, checked against the design's guard (§9):

| attempt | through | outcome | basis |
| --- | --- | --- | --- |
| move a governed node away from its rule, or a node into the store group | no route moves anything: there is no move among the 44 operations | not possible | [R plugin `Spec/openapi.yaml`, every `operationId`] |
| make an entry the read path takes for a rule entry | `createEntry`, `createGroup` draw a random UUID; the design redraws a marked entry UUID | not possible | [R plugin `Bridge/KdbxVault.cs:385`, `:603`] |
| make a copy (`KPG.Kind` on an unmarked entry) | `createField` with a reserved name | refused by §9 point 3, which is new code | design |
| read `KPG.Data` | entry, field and attachment routes | the entry is absent (§9 points 1 and 2) | design |
| read it through a field reference | the gate compiles none; KeePass's wanted fields are `T U A P N I` | not reachable | [R 2.62 `SprEngine.cs:586-606`] |
| remove a rule entry | a rule entry is absent; removing a governed node leaves its rule entries, inert | not possible | [R plugin `KdbxVault.cs:511-569`]; design §6.3 |
| clear a deletion record | no route reaches `DeletedObjects` or Database Maintenance | not possible | [R plugin, `openapi.yaml`] |
| make a node refuse | needs a deletion record bearing the mark and a governed node's 48-bit tag; `createGroup` is not redrawn, so a group's deletion record can bear the mark one time in 2^32, and then needs the tag as well | negligible | design §2.2, §4 |
| learn that a hidden entry exists | `deleteGroup` on a group holding a dragged rule entry | M4 | |
| learn node identifiers | every identifier the API returns, and references in readable fields | the input to S1 | [R plugin `openapi.yaml:2798-2819`] |

So the API's own surface holds, given the guard as designed. The caller's way in is through a file
the operator merges (S1) or through what the operator is led to do (S2, S3, S4).

## Lens (b): secrets

This option puts no secret anywhere: a rule entry holds a policy, empty standard fields, no
attachment, no history. The authority's private key stays where data 1 to 6 put it. Two things to
weigh: the policy map in exports and on screens (M7), and, if option `store-entries` is taken, the
store group becomes the place the operator is sent to inspect rules and also the place the private
key is (M8), with a foreign Synchronize able to switch that group's searching back on (M5).

## Lens (c): KeePass's own functions and the present guards

| function | what it does to this design | basis |
| --- | --- | --- |
| entry and group dialogs, Edit Entries, history restore | cannot touch a rule any more; a real gain | design §5.1; [R 2.62] as the design cites |
| the entry dialog's close | M1 | [R 2.62 `PwEntryForm.cs:1163-1176`, `:2146-2192`] |
| Duplicate Entry, Duplicate Group, group drag-copy, Paste Entries, Add Entry from a template | the copy has no rule | S4; [R 2.62 `MainForm.cs:933-947`, `:1317-1318`, `:2313-2342`, `Util/EntryTemplates.cs:265`]; [M here, P1] for today |
| File then Import, Create new IDs (preselected) | foreign copies promoted by Remove copies (S1); imported copies of sealed groups exposed (S4); the gate stops (M3) | [R keepass 2.4]; [M here, P2] |
| File then Synchronize with a foreign file | a planted rule, invisible on the node (S1) | [M here, P2] |
| Delete Duplicate Entries | deletes nothing of the store as written [M probe]; after an import it can delete originals, which only the `503` holds | design §6.4 |
| Delete Empty Groups | the store group is never empty once a rule was written | [M keepass X10] |
| delete to the recycle bin, Empty Recycle Bin | a rule entry counts in the bin; emptying it leaves deletion records and the node refuses, then S2 | design §8 |
| Database Maintenance, Remove deleted objects information | reopens nodes that refuse, silently | S3; [R keepass 5] |
| triggers, "Import into active database" | Create new IDs by default: the gate stops | M3; [R keepass 9] |
| XML Replace | a `KPG.` string it changes makes the node refuse; it runs under KeePass's progress window, which `GroupDialogs.Working` already reports | design §8; [R inventory P25] |
| search references `{REF:…@O:…}` | can resolve to a rule entry for the operator; returns only fixed or empty fields | [M keepass X11]; [R 2.62 `SprEngine.cs:586-606`] |

**The guards.** `GroupDialogs` stays for the vault's writes to nodes and stops mattering to rules,
as §5.1 says: no dialog holds a rule, and the writes this design adds (the editor's apply at a
dialog's OK, the Rules page at the settings dialog's OK) run while a modal dialog of KeePass's is
closing, when no entry dialog on a rule entry can be open. The single writer is unchanged: the
editor never wrote through it and still does not. The save that is read back is unchanged and
covers none of the rule writes, as today, since the operator saves them. No read starts inside a
merge, unchanged [R inventory P25]. The new hazards are not in the guards; they are S1, which
arrives through a merge the guards are not about, and S2 and S3, which run through the operator's
own presses.

## Lens (d): the operator

- **In KeePass**: a fourth page on the KPG tab; two new states on the node's tab; a refusal shown
  only on that node's tab and as a count on the Rules page. A planted rule (S1) shows on the node's
  tab as the node's own, with nothing to say where it came from. The repair acts on any OK (S2).
  The question on Escape behaves differently from KeePass's (M1).
- **In other programs**: a group of identical-looking entries that grows with every rule change
  (S3); nothing on a governed node, so nobody on a phone can tell an entry is narrowed (conceded by
  §13); a duplicate of a rule entry stops the gate (M3); a copy of a narrowed node is wider (S4).
- **What they can do wrong, in order of likelihood**: tidy the look-alike entries (S3), duplicate a
  sealed group (S4), import a backup or a colleague's file with KeePass's defaults (S1, S4, M3),
  press OK on a refusing node (S2), run Database Maintenance (S3).

## Lens (e): the cost

**Classes.** The design's table (§5.5) is close. It leaves out: the guard reaches every listing, the
flat sweep, `FindEntry`, `FindGroup`, the counts, the tags and `deleteGroup` (M4), not only "the
vault"; `OpenDatabaseTargets` has to look for copies at the connection, and the `503 undecided` flow
needs a second prompt (M3); `GrantPanelFactory` needs KeePass's Escape test (M1); a fourth page means
`GateTabFitsTests` and retaken screenshots. `KeePassContract` keeps the entry, group, database,
merged-selection and mode fields; only `NodeFormCustomDataField` goes. If S1's MAC and S4's hooks
are taken, `RuleStore` signs, Recreate re-signs, and the plugin hooks KeePass's copying commands,
each with tests against the pinned KeePass.

**Documents reversed beyond §12's list**: `docs/access-control.md`'s "Where a rule is stored",
"A copy of a node carries its rules" and "A rule on a template is a rule on every entry made from
it" [R plugin `docs/access-control.md:447-463`], and the seal's advice (S4).

**Tests.** Rules are written as node custom data as a fixture across the host tier. Test attributes
per file in the files that do so or pin the dialog's copy [M here, a search of the test tree; not
all of each file is about rules]: `GrantEditorInRealDialogsTests` 36, `SkippedOnlyForAReaderTests`
29, `DecidedAtTheReadTests` 27, `TagOfTheCallersViewTests` 25, `AclInRealDatabaseTests` 12,
`MergeInRealDatabaseTests` 10, `SnapshotHazardTests` 10, `RemovalMergeTests` 9, `DatabaseNodesTests`
6, `DecidedAtTheWriteTests` 5, `LiveGrantEditorTests` 5, with `KeePassContractTests`,
`DocumentationScreenshots` and the `ThrowawayKeePass` fixture. Facts that fall away: the
`m_sdCustomData` pin and its `FINDINGS.md` entry, the snapshot hazard as far as rules go, and the
reason behind "the editor never writes the same rules in other words". New hazards that need tests:
the guard on every route and `createEntry`'s redraw; the derivation, the meet, the three kinds of
damage, copies and their `503`; the repair (S2); the Rules page; the editor's apply after KeePass's
own `FormClosed` has removed the page; the Escape and Cancel paths (M1); Add Entry and Add Group
accepted and cancelled; and, if taken, the MAC (S1) and the copy hooks (S4).

**0.1.0 databases** cannot be left unread without a widening (S5).

## Claims in the design this critique leans on that nobody has measured

1. §13: "Today such a file brings the node itself with its rule" [A in the design], with the
   comparison read as equivalent; P2 shows the plant changes nothing on the node [M here].
2. KeeShare keeps the container's entry identifiers, and the store group is in no share [A in the
   design].
3. The Android merges add an arriving rule entry and treat deletion records as read [R android].
4. No program trims or normalizes a custom string's value; one changed byte makes a node refuse
   [R android 2, R others; not run for every program].
5. The tab's `FormClosed` handler still has its buffer after KeePass's own handler has taken the page
   away (§16.3, not run).
6. How the tab knows KeePass did not ask on Escape (§5.1 does not say; M1).
7. The pass's cost inside the gate, under the hold (§16.4; console only).
8. Restoring a hand-edited rule entry's revision puts the rule back in force (§8, "not run").
9. That the Remove copies put-back meets only the Delete Duplicate Entries case it was written for;
   a foreign copy was not considered (S1).
10. Whether KeePassXC or the Android programs can duplicate a whole group, which sizes S4's residual
    [A].
