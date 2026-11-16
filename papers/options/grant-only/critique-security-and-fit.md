# Critique of option `grant-only`: the operator, the threat model and the cost

Key `grant-only-security-and-fit`. Written 2026-11-15 against `option-grant-only.md` as it stands,
KeePassGate master `a42e38aa` (the product at `a42e38aa`) and KeePass 2.62. This attacks the
design; it does not edit it. Survival under other programs is the other critic's lens and is
touched here only where it decides something about the operator or the caller.

## How statements are marked

- **[M here]**: measured for this critique, today, by the console probe in "What I ran".
- **[M design]**: measured by the design's own probe, as `option-grant-only.md` reports it, or read
  off that probe's own output files.
- **[M android]**, **[M spike]**: measured in `measured-android.md`, or in `spikes/revocation-merge`
  as the ground reports digest it.
- **[R plugin]**: read in the product at `a42e38aa`, file and line given.
- **[R report]**: read in the named report of this directory.
- **[R ruling]**: a decision of the project as the session's memory records it, with its date.
- **[A]**: assumed. Neither run nor read.

## Verdict

**Viable with changes.** Nothing here is fatal: every problem below has a change that keeps the
survival requirement, and the core claim holds where the design makes it. With the rules as
grants only, a lost grant record, a copy and a node displaced to the root never widen in the
resolver, and nothing of the plugin's has to sit on a user's entry or in a user's group, which is a
real simplification against `rules-linked` and against today's store, whose rules KeePassDroid
deletes at every save [M spike; M android 3.2].

Five problems are serious under this lens, and six minor:

1. The narrowing does not disappear, it moves into where a node sits, and that is the one thing an
   Android program in scope (Keepass2Android's Move), a drag in KeePass and the later of two
   moves in a merge all change with nothing shown. Measured in a worked example: a sealed folder
   dragged under a granted one, and a root credential moved between two project folders, are both
   read by a client under grant-only and by nobody who should not under today's model and the
   middle form [M here].
2. The transition of an existing database is where this option is most likely to widen,
   and the design misstates why. Not reading the 0.1.0 rules grants nothing; it is the operator
   granting the root again, by the habit the README teaches, that opens the folder the old seal
   kept closed [M here]. The design's remedy is migration code the project's standing decisions
   forbid.
3. Without reach at the root, a client cannot find anything: the flat sweeps, the client's
   defaults and the README's first command all need it, and under this model root reach means
   everything forever [M here; R plugin]. The design says something "has to" change and does not
   design it; it reverses a decided rule and the router's one-node contract.
4. Two copies editing one row apart do **not** resolve to what both allow, as the design claims:
   a removal wins only for the letters it named. Measured in 36 merge cells: a row the operator set
   to read-only came out with `S`, and a row from which the operator took `P` came out with `U`
   without `P`, which is the auto-type path to a password [M here]. A one-field change fixes it, and
   was measured.
5. The key that makes grants authentic is carried over Recreate, which is the documented recovery
   from a leaked export, and the export carries the key. In a model where a forged grant is the only
   way to widen, the one recovery does not recover it.

On the four questions asked: (a) the operator loses the exception under a grant, and in the worked
example the price was five moves and three new groups, or thirteen grants against eleven rules;
the failure it adds is worse in practice than `rules-linked`'s residues for an operator who
narrows per node and reorganizes on the phone, and not worse for one who grants low. (b) Through
today's routes a client cannot widen its own reach, given the store guard; a future move or copy
route becomes a policy change for every profile and must be refused on that ground. (c) The
documentation's first story, the client's defaults, the grant editor's one inherited mask and the
`Access` model all change; the counts the design gives for the tests hold. (d) The middle form is
the better product for any operator with a carve-out, provided it ships with the strict form
rather than "later" and the documentation still steers grants low.

## What I ran

A console probe over KeePass 2.62's KeePassLib on .NET Framework 4.8, no window, invented content
only. `Mine.cs` was compiled with the .NET SDK 10.0.401 C# compiler against the .NET Framework 4.8
reference assemblies, a copy of `.keepass\2.62\KeePass.exe` (SHA-256
`AFB89B74AE1B8519F483D600095F4DF8D86628178AF6ACDC5F2B23888C89C3F3`, equal to the original), and the
eight files `AclDocument.cs`, `AclDocuments.cs`, `AclGrant.cs`, `AclInheritance.cs`,
`AclResolver.cs`, `AclVerb.cs`, `VerbMask.cs`, `JsonBody.cs`, each byte-identical to the worktree
at `a42e38aa` (checked by hash). `keepassxc-cli` 2.8.0, from the copy the `keepassxc` ground report
downloaded, was the only other program started. One run, 15 seconds.

Files, under
`spikes\storage\probes\grant-only\security-and-fit\`:
`probe\Mine.cs`, `out\probe.txt` (SHA-256 beginning `5C82C042DF5E9305`), databases under `work2\`
(an earlier identical run under `work\` differs only in one label).

Four parts:

- **store**: the design's grant and withdrawal entries and the withdrawal's custom data key, as its
  section 5 defines them, merged under KeePassLib's five methods in both directions and by
  `keepassxc-cli merge -s` in both directions. Each result read under two folds: **design**, the
  design's rule (a grant's live letters are its mask less the letters of every withdrawal of its row
  it did not see), and **cap**, a variant in which a withdrawal also records the letters its row
  kept and a grant that did not see it is held within those.
- **guard**: `VaultHandlers.CouldPutBackUnprotected` (`Pipeline/VaultHandlers.cs:597-604`
  [R plugin]) re-implemented over the grant-only resolver, before and after one grant record is
  deleted.
- **legacy**: a database carrying 0.1.0 rules in node custom data in the README's shape, read by
  today's `AclResolver` and by the grant-only fold.
- **example**: an invented tree organized by subject, one policy for three profiles under four
  arrangements (today, grant-only with a grant per entry, grant-only with the tree split, the
  middle form), checked against the policy on every entry and then put through five ordinary
  events. In-memory nodes; today's answers from the plugin's own resolver, the others from the
  design's rules as its probe implements them (`Program.cs:205-237` of the design's probe).

What it is not: the plugin, KeePass's window, any Android program, an HMAC (the check is a store
identifier compared for equality, as in the design's probe), a store for the middle form's marks.

## Problems, most severe first

### S1. Where a node sits becomes the policy, and a phone, a drag and a merge all change it silently (serious)

The design states the trade honestly ("The narrowing does not disappear; it moves into the tree",
section 10). This puts it in steps against the programs in scope and an ordinary database.

The worked example [M here, `probe.txt`, "a worked example"]. An invented tree organized by
subject: Personal (Banking, Health, Shopping, Email), Family, Work (two loose entries, ProjA with
six credentials, ProjB with three), Home (four). The policy: `ci` reads the deploy tokens of ProjA
and ProjB with their passwords but not AWS root, DB prod admin or GCP owner; `home` reads the Home
Assistant token and the Wi-Fi with their passwords but not the router or NAS admin; `assistant`
reads everything without passwords but Health and Family. All four arrangements give exactly the
policy on all 26 entries (0 answers differ in each).

| Event | Today (root grant, seals, narrowings) | Grant-only, a grant per entry | Grant-only, tree split | Middle form (grants and marks) |
| --- | --- | --- | --- | --- |
| Records | 11 rule documents on user nodes | 13 grants, nothing on user nodes | 9 grants, 5 moves, 3 new groups | 4 grants, 7 marks with a reference on each marked node |
| E1: a new sensitive entry "AWS billing admin" made in Work/ProjA on the phone | read by `ci` | **not** by `ci` | read by `ci` | read by `ci` |
| E2: a new top-level group "Crypto" made on the phone | read by `assistant` | read by nobody | read by nobody | read by `assistant` |
| E3: Personal/Health dragged into Personal/Shopping | read by nobody | **read by `assistant`** | **read by `assistant`** | read by nobody |
| E4: AWS root moved into Work/ProjB | not by `ci` | not by `ci` | **read by `ci`, with its password** | not by `ci` |
| E5: may list at the root | `assistant` | nobody | nobody | `assistant` |

The steps behind E3 and E4 on a phone:

1. The operator keeps Health closed. Under grant-only that is done by Health having no grant on it
   or above it; the assistant's grants sit on Banking, Shopping and Email instead of on Personal.
2. On Keepass2Android they long-press Health, Move, Shopping, Insert here. Keepass2Android's Move
   was measured to carry a group with everything in it and to keep every custom string, entry and
   custom data item [M android 2.1, column 6].
3. The next Synchronize carries the move into KeePass [M android 5.1; M design 4.2]. From the next
   request the assistant reads the clinic portal and the insurance. Nothing on the phone, in KeePass
   or in the audit says a policy changed; the audit records the reads as permitted.

Under today's model and under `rules-linked` the same move leaves Health closed, because the seal
(today's custom data, kept by Keepass2Android) or the link entry (an entry, kept) travels with the
group. E4 is the same for an entry: under the split arrangement AWS root's only protection was its
folder. KeePassDroid offers no move [M android 3.1], so the other Android program in scope cannot
cause this. A drag in KeePass's own tree can [A: KeePass moves groups and entries by drag and drop
without asking; not driven here], and so can the later of two moves in a merge, where the design
measured KeePass's own Synchronize putting a node back under the grant (its scenario 11).

What the example also shows, and the design does not say: **the per-entry form is the only
arrangement in any model that keeps E1 closed.** A group grant exposes every entry later made in
that group, today as under grant-only, because a phone has no KPG tab and cannot narrow anything.
So grant-only does not make phone creation worse; what it removes is the sealed sub-folder inside a
granted group as a safe place to put a new secret. It also makes the closed default the natural
one for new top-level groups (E2), which today's README shape does not.

**Change.** Design and ship the middle form's mark together with the strict form, for the few
nodes that must stay closed wherever they go, rather than "later if the answer is yes" (section 10).
The day marks arrive, the plugin puts a reference on user nodes, the documentation and the editor
are written a second time, and the promise "nothing of the plugin's on your entries" is broken on
the phone; deciding it once costs less. Separately, put the granted node's path at the time of
writing into each grant entry's title (`KPG grant agent .R...L.. Work/ProjA`), so the operator can
see on the phone which groups are open before filing something there. The title is not
authoritative (the check must not cover it, M6), so a stale path after a rename only misleads, and
the KPG tab stays the authority. And state in `docs/access-control.md` that on Keepass2Android a
Move into a granted group is a grant.

### S2. The transition of an existing database is where this option widens, and the design misstates why (serious)

The design's section 1.4 and section 8 say that not reading the 0.1.0 rules widens, so "an older
shape is not read" cannot be the whole answer. Measured, it is the other way round [M here,
"legacy"]. A database in the README's shape (`reader` `.R...L.P` on the root, the folder Personal
sealed, entries Work/X, Personal/Q and Z at the root):

| | Work/X | Personal/Q | Z at the root | list at the root |
| --- | --- | --- | --- | --- |
| today's resolver | reads | refused | reads | yes |
| grant-only, nothing converted | refused | refused | refused | no |
| grant-only, after the operator grants `.R...L.P` on the root again | reads | **reads** | reads | yes |
| grant-only, after the section 8 push-down (grants on Work and on Z) | reads | refused | reads | no |

So the steps that widen:

1. The operator upgrades to 0.2.0. The gate grants nothing; every client is refused. This is closed.
2. The operator opens the root's KPG tab, sees no rule (the old one is not shown), and grants
   `.R...L.P` there, as the README's own story did (`README.md:114-117`; the story's source grants
   on the root group "Invented Vault", `tests/KeePassGate.Host.Tests/Live/DocumentationLifecycle.cs:286-317`
   and `ThrowawayKeePass.cs:1144` [R plugin]).
3. Personal's old seal sits in Personal's custom data, unread. From the next request the reader
   reads everything in Personal.

The design's remedy is a conversion press, a page that refuses to finish while any narrowing has no
answer, and a refusal on any node still carrying the old key. Three objections under this lens:

- The project decided that 0.1.0 had no users (2026-11-08) and, standing since 2026-09-25 and
  2026-10-03, that no code handles earlier stored data (no migration, no programmatic cleanup, no
  one-time warnings, no reading of old keys), with a one-off cleanup by hand instead [R ruling]. The
  conversion and the refusal are both that code. The open question `EVALUATION.md` 7 question 6
  asks the project is answered by these rulings unless the project reopens them.
- The refusal guards against nothing 0.2.0 does. A stale copy merged in brings old node keys back,
  and grant-only does not read them, so nothing widens through 0.2.0; it matters only while a 0.1.0
  plugin is still running against the same file, which the operator can rule out by upgrading.
- The conversion keeps `AclDocument`, `AclDefault`, `AclDocuments` and today's resolver alive to
  work out what each profile had (section 8 step 1), while section 7 says they go.

**Change.** No conversion in the product. One console script, run once by hand over a copy of an
existing database and kept outside the repository, that prints every narrowing by path (seal,
empty array, a nearer mask lacking a letter the nearest rule above holds) and the grants that
reproduce the rest. That is also the design's own first measurement (section 12, item 1), and the
falsifier of the option. The operator removes the old `KPG` node keys by hand once, as the rulings
say.

### S3. Without reach at the root a client can find nothing, and the design leaves that undesigned (serious)

`listGroups` and `listEntries` are "anchored at the root, so a client that may not list there is
told nothing" (`Spec/openapi.yaml:714-718`, `:1105-1109` [R plugin]). The client defaults to the
root everywhere: `groups()` and `entries()` sweep from it, `create_entry(group=ROOT)`,
`create_group(parent=ROOT)`, `group_access()` asks about the root, and the command line's `add` and
`add-group` put things there unless told otherwise (`client.py:62`, `:649-652`, `:710`, `:807-810`,
`:860-862`; `cli.py:483`, `:567` [R plugin]). The README's story runs `pykeepassgate entries`
before and after the grant (`DocumentationLifecycle.cs:282`, `:396` [R plugin]).

Under grant-only, `L` at the root is `L` on every node there is or will be, imports and displaced
nodes included, with every unprotected title shown, and with `P` beside it every protected title
too (`docs/access-control.md:225-233` [R plugin]). The worked example's E5 [M here]: in both
grant-only arrangements nobody may list at the root. So:

1. The operator grants `ci` `.R...L.P` on Work/ProjA, as the new documentation will advise.
2. The program runs `client.entries()`. It is refused as if the database were empty of everything
   it may reach. `client.create_entry(title=...)` is refused. It has no way to learn ProjA's
   identifier except from the operator, by hand.

The design's section 6 offers two ways, "the sweeps have to answer what the caller may list
wherever it sits, or `whoami` has to name the nodes its grants are on", and stops. The first is a
data route the router cannot authorize against one node, which reverses CLAUDE.md's "a flat sweep is
an operation on the root group" and the router's rule that it, not a handler, decides before
anything runs, because "a handler could forget to". The second tells a client where its grants
are.

**Change.** Take the second, and design it now: `whoami` lists, per profile the client holds, the
identifiers of the nodes carrying a grant for it, and nothing else about them. Every listing stays
anchored at a node the router authorizes; the sweeps stay anchored at the root and are simply
refused under advised shapes; the client's `ROOT` defaults go, and a program takes its anchor from
`whoami`. What this tells a client (which nodes are granted to its own profiles) it can already
learn one guess at a time from the access routes, so the leak is small [A]. Record the reversal in
CLAUDE.md and API.md, re-shoot the README story with a grant on one folder, and say in
`docs/pykeepassgate.md` that a program is given its group rather than starting at the root.

### S4. Two copies editing one row apart keep more than both allow: a removal wins only for the letters it named (serious)

The design claims: "In no cell of scenarios 01 to 05, 07, 15 and 17 did `agent` hold more than the
operator's last decision, or more than both decisions allow where two were made apart" (section
5.5). Its scenarios cover a removal against an addition of the **same** letter (04) and two removals
(03). They do not cover the case the editor makes most: the operator unticks some letters while
another copy ticks a different one. Measured [M here, "store", cases N1 to N3, 10 KeePassLib cells
and 2 `keepassxc-cli` 2.8.0 cells each]:

| Case | Base row | Copy A (the operator narrows) | Copy B, not knowing | What both rows allow | Design fold, every cell | Cap fold, every cell |
| --- | --- | --- | --- | --- | --- | --- |
| N1 | `CRU.DL..` | sets `.R...L..` (withdraws C, U, D) | adds `S` (earlier) | `.R...L..` | **`.R.S.L..`** | `.R...L..` |
| N2 | `CRU.DL..` | the same | adds `S` (later) | `.R...L..` | **`.R.S.L..`** | `.R...L..` |
| N3 | `CR...L.P` | takes `P` away | adds `..U....P` | `CR...L..` | **`CRU..L..`** | `CR...L..` |

The steps of N3, which is the sharper:

1. On the desktop the operator takes `P` away from `agent` on G, because it should no longer see
   passwords there.
2. On the laptop, from a copy taken before, the operator had granted `agent` `..U....P` on G so it
   could rotate passwords.
3. A KeePass Synchronize, any of the four other methods, or a KeePassXC merge, in either direction:
   the new grant did not see the withdrawal, so its `P` is cut and its `U` stays. `agent` holds `U`
   without `P` over everything in G.
4. `U` without `P` is the documented path to a password the rules withhold: rename an entry after a
   window the program controls, and the operator's global auto-type hotkey types the password into
   it (`docs/access-control.md:270-277` [R plugin]). Neither copy granted that combination.

This also departs from the rule every other design in the family converged on, "two decisions that
never saw each other resolve to the closed side ... two rule versions give what both allow"
(`EVALUATION.md` 5.1 rule 4; `option-crdt.md` line 55, "every join moves toward less access"
[R report]). It is rare in practice: it needs the KPG tab used on two KeePass installations on one
row between merges, since no other program writes grants.

**Change, measured.** A withdrawal records, beside the letters it takes, the letters its row kept
(`KPG.Keep`, and in the second-witness key), and a grant that did not see it is held within those
letters instead of losing only the named ones. Under that fold N1 to N3 gave the meet in all 36
cells; the design's 03 and 04 gave the same verdicts as before in all 24; two additions to one row
still unite (12 cells), so concurrent widenings do not cancel each other, which the meet-based
designs cannot say; and a grant made after the merge, listing the withdrawal it saw, held [M here].
Then question 3 of the design's section 10 can be put to the project in the form that is true.

### S5. The key that makes a grant count is carried over Recreate, which is the recovery from a leaked export that carries it (serious)

Section 5.3: "In this model a planted or stale grant is the only way to widen, so `KPG.Check` is
load bearing. The key is a random value held once per database in the authority's document and
carried over by Recreate." The documentation: Recreate "is the recovery from an authority no longer
trusted", and "an exported database needs the same recovery if it goes astray", because KeePass's
XML export writes the authority's signing key in the clear (`docs/clients.md:161-171`
[R plugin]). Under `option-store-entries.md` the authority's document, and so the key, is a
protected string in an entry, which the XML export, KeePassXC's HTML export and KeePassDroid's
entry screen show (`EVALUATION.md` 5.1, "What it costs" [R report]).

1. An export of the database leaks. The operator follows the documentation and presses Recreate.
   The authority, the host certificate and every client are new.
2. The store key is not: whoever holds the export can still write grants that count, naming any
   node identifier in the export and any profile an admitted client holds.
3. They need the operator to merge a file once (a "backup", a colleague's copy). The planted
   `.R...L.P` on the root counts; a client the operator admitted, prompt-injectable by design of
   the threat model, now reaches every secret added since the leak.

**Change.** Key the check to the served authority, as `EVALUATION.md` 5.1 rule 3 already offers
("a MAC under the served authority's gate key ... the MAC also shuts out the holder of an old copy
once the authority has been recreated, at the price of signing every record again at Recreate"). At
Recreate the plugin writes each grant that verifies under the old key again as a new record under
the new one, with the same `KPG.Saw`, and deletes the old record; a stale copy merged later brings
old records that verify under nothing. Two unretired authorities already answer `503 undecided`,
so no second rule for two keys is needed. At Delete then Create the grants stop counting, as the
design says; list them on the tab as text for the operator to grant again by hand, and offer no
press that re-signs them, since that is the forged-evidence trap the `rules-linked` critic measured
(its S2).

### M1. One inherited mask per profile cannot show what a profile may do once switches bind to rows (minor)

The editor keeps one inherited mask per profile (`GrantRow.InheritedMask`,
`Ui/GrantSheet.cs:10-39` [R plugin]), and the client one mask per profile (`Access.grants`,
`pykeepassgate/model.py:828-847` [R plugin]), both exact under nearest wins. Under the design's
row-bound switches, `.R...L..` on G and `..U....P` on entry Y let `agent` update Y's password and not
read it. The design's own probe prints that state as one mask, `G/Y=CRU..L.P` and `.RU....P`
[M design, `out\keepass.txt`, the "A alone" lines], which in the tab's own notation says "reads the
password". The design's section 6 still describes "two masks: what arrives from the groups above, in
gray". Every such merged mask overstates, never understates (a union of letters), so no client gets
more; but the operator cannot answer from the tab the one question the tab exists for, and may
remove a grant that was harmless. **Change:** one gray line per contributing row, naming the group
that grants it, within the tab's 449 by 314; the access routes and `Access` carry a list per
profile, as section 6 says for the routes. Keep the row binding: the union the design rejected
would make `C......P` on a subgroup open every password to an `R` above.

### M2. The strip guard is not monotone, so "a lost rule can only narrow" holds for the resolver and not for every answer (minor)

`CouldPutBackUnprotected` refuses the strip of a protected field when a profile may create there
and may not create a protected value (`Pipeline/VaultHandlers.cs:597-604` [R plugin]). Holding more
`C` refuses more. Measured over the grant-only resolver [M here, "guard"]: `agent` with `C.......`
on the root and `...S....` on G may not strip X's protected password; delete the root's grant
record by hand and it may. What it gains is exactly what `...S....` grants, and the guard's reason
(nowhere to put an unprotected value back) holds once `C` is gone, so this is not a widening in
substance. **Change:** state and test the property as "a lost grant never permits an act the
remaining grants do not grant", not "never turns a refusal into a permission", since the design's
model runs did not include the pipeline's guards.

### M3. A client with `C` or `U` in a granted group can make the operator's map of the tree lie (minor)

Under grant-only the operator keeps a secret out of reach only by where they file it, by name. A
client with `C` in its granted group can create a subgroup named "Health" or "Private", and with `U`
can rename its granted group (`updateGroup` changes name and notes, `docs/pykeepassgate.md:636`
[R plugin]). On the phone, which shows names and no grants, the operator files a new secret into
the look-alike, and the client reads it. The same is possible today for any group without a rule of
its own, so it is not new; it matters more here because nothing else stands between. **Change:** a
warning beside S1's in `docs/access-control.md`, and the grant titles of S1 so the phone shows which
group carries a grant.

### M4. A move or copy route would become a policy change for every profile (minor, for the future)

There is no move route, and `API.md` §407 says why: "the router authorizes exactly one" node, and a
client with `create` in the destination and `delete` in the source can already do it in two calls
(`API.md:53-55` [R plugin]). Under grant-only a move is a change of policy, and not only the
caller's: a client holding `D` on an entry it may not read (`...SD...`) and `C` in a group another,
less trusted profile reads, could hand that profile the entry. Today an entry's own narrowing travels
with it and limits that; here nothing on the node does. A server-side copy route would add the
caller's own read-through: copy an entry whose password it may not read into a group where it holds
`R` with `P`. The two-call path does not have this problem, since a client copies only what it can
read. **Change:** record in §407 that a move or copy route is refused because it changes what every
profile reaches, and that one may only ever be added if no profile reaches more at the destination
than at the source.

### M5. Withdrawals pile up forever, in plain view on the phone (minor)

A withdrawal is written at every untick and never removed, nor can it be, since a stale copy may
still hold the grant it cuts (section 5.4). On the phone the KeePassGate group grows by one
inscrutable entry per change (`KPG withdrawn agent .R...... 4F0C91AA 3b9e1c`). That invites the
tidy-up that, followed by a KeePassXC merge of an older copy, is the one residue the design keeps
(section 4.3); each withdrawal's key keeps the file at KDBX 4.1 for good, with the KeeWeb defect
`EVALUATION.md` 5.1 lists. Growth is per operator act, so small, but unbounded (A5).
**Change:** none needed inside the model; say on the tab and in the documentation that these
entries are deleted only by the plugin, and give them the path-bearing titles of S1 so they read as
what they are.

### M6. What the check covers decides whether a phone edit loses a grant (minor, unmeasured)

The design's check is "an HMAC over the other strings". If that includes the standard strings, then
an accidental title edit on Keepass2Android or KeePassDroid (measured to happen and to be kept,
[M android 2.1, 3.2, column 3]) or KeePassXC adding empty user name, URL and notes to an entry that
lacks them (`EVALUATION.md` 2.1 [R report]) voids the grant. That is closed, but it is a rule lost
because the database was edited elsewhere, which is the survival requirement. The design's
probe compared a plain identifier, so nothing measured it. **Change:** the check covers only the
`KPG.` strings, by name in ordinal order, and is measured through each program's save and edit of a
grant entry.

## Lens (a): what the operator can no longer express, and what that costs

**Cannot be written, by any arrangement**: a node that sits inside a group and holds less than that
group, for a profile that group grants. Everything else can be, at a price, and the price falls on
exactly the shape this lens asked about, a few very sensitive entries scattered among ordinary
ones:

- **Split the group**: in the example, three admin sub-folders and five moves, after which AWS root
  is no longer filed with ProjA, and a move back (E4) opens it. Each split is the tree serving
  policy instead of the operator.
- **Grant per entry**: in the example 13 records against 11 today, and every token the program
  should get later needs a visit to KeePass on Windows, since no phone program can grant. But this
  is the only arrangement, in any model, in which a secret added later on the phone (E1) stays
  closed.
- **Broad reach with carve-outs** (an assistant that reads all but a few folders) costs one grant
  per open sibling at every level above a closed folder, leaves entries filed directly in those
  parents closed, and gives up discovery at the root (S3).

**On the phone**: an entry created in a granted group is reachable at once, under every model,
because the phone cannot narrow it; grant-only only removes the sealed sub-folder as a safe place
inside a granted group. An entry or group **moved** into a granted group on Keepass2Android is
reachable at once under grant-only and stays closed under today's model and `rules-linked` when it
carried its own narrowing (S1). KeePassDroid cannot move anything [M android 3.1].

**Is the new failure worse in practice than the ones it removes?** Against today's store, no:
KeePassDroid deletes every rule on a node at every save [M android 3.2], a failure at the frequency
of phone use. Against `rules-linked`, which is the real alternative, it depends on one
fact only the operator has: the failures grant-only removes there (a version and its record lost
then an older copy merged; Delete Duplicate Entries after a group copy; a forged Confirm) need
uncommon acts, while the failure it adds (a drag, a phone Move, the later of two moves in a merge)
is an everyday act, but only for nodes the operator meant to narrow. So grant-only is the better
model for an operator whose narrowings number zero or a handful that never move, and the worse one
for an operator who carves exceptions out of broad grants and reorganizes on the phone. That is the
design's own falsifier (section 3.3), and S2's script answers it.

One point in the option's favor that the design does not use: when the operator settled nearest
wins on 2026-08-15, the reason given was that "deny at the root, open one group" is the pattern most
likely to be used and a narrowing-only model would forbid it [R ruling]. Grant-only keeps exactly
that pattern (it is the default state) and drops only the narrowing the ruling did not argue for.

## Lens (b): the caller

Through the routes as they are, with the guard the design requires, a client cannot widen its own
reach. Checked against `Spec/openapi.yaml` [R plugin]:

| Attempt | Through | Outcome |
| --- | --- | --- |
| create a node that inherits more than its container | `createGroup`, `createEntry` | not possible: a new node has what its place gives, as its container has |
| create a node under an identifier a grant names | the same | not possible: identifiers are drawn fresh (`Bridge/KdbxVault.cs:385`, `:603`) |
| move a node under a grant | no route | not possible; see M4 for a future route |
| plant a grant | `createField`, `updateField` with a `KPG.` name | refused by the guard; and a grant needs the check |
| plant a withdrawal to close another client's access | the same | refused by the guard. **Without** it, any client with `C` anywhere could close every row of every profile for good, since withdrawals count wherever they are, unauthenticated, and are never removed. The guard does not exist today (no `KPG.` test in the vault) and is load bearing for availability |
| read the store | any route | the store group and every entry carrying `KPG.Kind` must be absent, as the bin is |
| copy a value it cannot read into a place it can | two calls: read, then create | not possible: it copies only what it read |
| mislead the operator's filing | `createGroup`, `updateGroup` names | possible, M3 |
| strip a protected field after losing a grant | `stripField` | permitted where `S` alone is granted, M2 |

Against `rules-linked` the guard is smaller: no reserved name has to be hidden inside entries the API
serves, because the strict form puts nothing on user entries. The middle form brings that surface
back for marked entries.

## Lens (c): documentation, editor, 0.1.0 databases, tests

- **Documentation.** The design's list in section 6 is right and short of three things. The client
  documentation's defaults to the root (S3), the README's first command, and the cost lines of S1
  (a phone Move is a grant). The recommended shapes that go are the three the design names
  (`docs/access-control.md:198-201`, `:389-392`, `:394-407`) plus "A profile just picked arrives
  with exactly that" (`:390-392`), which describes the editor's empty default.
- **The editor.** Smaller in what it writes, larger in what it shows (M1). The check box, the
  struck-through seal and "Discard the unreadable rules" go.
- **0.1.0 databases.** Only an existing database, as the project decided; S2 has what to do.
- **Tests.** The design's counts hold: 18 uses of `Rules.Sealing(` in 8 files and 274 of
  `Rules.Granting(` in 32 [M here, by search]. Beside them, 17 lines name `AclDefault.Deny` directly
  in 9 files (`LiveGrantEditorTests`, `AclInRealDatabaseTests`, `GrantEditorInRealDialogsTests`,
  `GrantSheetTests`, `GrantPickerTests`, `AclDocumentTests` among them), the Live story behind
  `lifecycle.gif`, `GrantSheet`'s inherited mask and its test, and 16 lines in 4 client test files
  that read `Access` as one mask per profile [M here, by search]. New tests the model needs: the
  row-bound switches against the pipeline's guards (M2), the cap fold (S4), the re-signing at
  Recreate (S5), `whoami`'s grant list (S3).

## Lens (d): the middle form against both pure models

The middle form keeps grants that add and adds one record, a mark that only takes away, counts
wherever it is found, overrides every grant above, on and below its node, and is referred to from
the node so that it travels with the node and its copies (section 1.5).

**Against strict grant-only** it restores, in the worked example, the two cases S1 measured:
Health dragged under Shopping stays closed, and AWS root moved into ProjB stays closed to `ci`
[M here]. The machinery is confined to marked nodes: a reference string, a link entry, the reserved
name inside served entries, one refusal state with its Settle press. Its residues are the same class
as `rules-linked`'s (both witnesses lost; a copy by a program that drops the reference), on far
fewer nodes.

**Against today's nearest wins, carried by `rules-linked`**, it is smaller: no versions, no Confirm,
no grant that waits, no limit in the resolver. And an absolute mark is easier to reason about than a
seal that "closes the way in from above, not the way in from below" (`docs/access-control.md:403-405`).
It gives up reopening one node below a mark, which today a seal plus a nearer grant allows.

**What it does not fix, and what it invites.** The middle form mirroring today's shape (a grant on
the root, marks on the exceptions) behaved exactly like today on E1 and E2 in the example: the new
sensitive entry in ProjA and the new top-level group were both read [M here]. Marks make broad
grants with carve-outs comfortable again, and broad grants are what leave every new node open. So
the documentation must still steer grants low and per entry where secrets are scattered, and
reserve marks for the few nodes that must stay closed wherever they go. A mark's scope should
default to every profile, as today's seal refuses every profile it does not name; a mark naming one
profile does not cover a profile granted broadly later.

**Judgment.** The middle form is the better product than either pure model for an operator with any
carve-out, and costs nothing on a database without marks. The strict form is the right base, but
not the right release: ship them together (S1), so the reversal of nearest wins is argued and
documented once.

## Claims this critique leans on that nobody has measured

1. That Keepass2Android's Move and KeePass's drag carry a mark's reference string and link entry.
   The carriers are measured through Keepass2Android's Move [M android 2.1]; the mark's shape is
   not, and KeePass's drag was not driven.
2. That KeePass moves groups and entries by drag and drop without asking [A].
3. That an ordinary database files a few sensitive entries among ordinary ones, as the worked tree
   does. The tree and the policy are invented; the counts follow from them.
4. That `whoami` listing a client's granted nodes tells it nothing it could not learn from the
   access routes one guess at a time [A].
5. The cap fold under KeePassDX's and kdbxweb's merges and through the Android saves; only
   KeePassLib's five methods and `keepassxc-cli` 2.8.0 were run.
6. What an HMAC over the `KPG.` strings does through each program's save and edit of a grant entry
   (M6); the probe, like the design's, compares a store identifier.
7. The middle form's answers in the example come from the design's probe semantics for marks
   (absolute, inherited downward), with no store behind them.
