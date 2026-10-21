# Testing

Four tiers, and the split is deliberate: each one buys something the others cannot, and each one
fails in a way that names its own cause.

| | Project | Needs | Runs in | What it proves |
| --- | --- | --- | --- | --- |
| **Unit** | `tests/KeePassGate.Tests` | nothing but a compiler, and a loopback socket | under a minute | the logic, and the whole transport, against stand-ins shaped like KeePass |
| **Host** | `tests/KeePassGate.Host.Tests` | a KeePass to reference | about a second | the same code against real KeePass types, dialogs and events |
| **End to end** | the same project, category `Live` | a desktop, and the machine to itself | under a minute | the plugin loading into a KeePass a user could have started |
| **Client** | `tests/pykeepassgate` | Python, and `uv` to arrange it | a few seconds | PyKeePassGate against stand-ins, and its protocol code against vectors the plugin produced |

```powershell
.\build.ps1                     # the prose, the client tier, then unit and host
.\build.ps1 -Task Live          # the end to end tier
.\build.ps1 -Task All           # everything
.\build.ps1 -Task Screenshots   # retake the pictures in docs/
```

**One category inside the unit tier draws rather than asserts.** `Gallery` builds each surface at
the states that change its shape and writes a picture of it, into `.gallery` beside the solution or
wherever `KPG_GALLERY_DIR` says. It runs with the ordinary tests, because it needs no KeePass, no
desktop and no network, and because a picture nobody generates is a picture nobody looks at. Two
things follow from its writing files: the directory is gitignored, and everything in it is rewritten
on every run, so nothing meant to be kept belongs there. `docs/images` is the set that is committed,
and a task of its own draws those.

**The screenshots are a fourth use of the end to end tier rather than a fourth tier.** Same
throwaway KeePass, same driver, and it writes into `docs/images` instead of asserting: the
documentation shows the real tab, so the pictures are generated the same way everything else
about that tab is checked. It is excluded from `Live` because it writes into the working tree,
and it is ignored entirely unless the build task asks for it. [`docs/`](docs/README.md) is what
it feeds.

## Why the unit tier does not reference KeePass

Everything above the KeePass boundary is written against small interfaces: `IWindowEventSource`
announces dialogs, `IFormMatcher` recognizes them, `IFormDecorator` decorates them,
`IGatePanelFactory` fills them, `IKeyValueStore` stores things, `IGateTargets` says what can be
served, `IGateContext` is one request, and `IGateLog` records all of it. Two things touch KeePass:
`KeePassGateExt`, which KeePass itself instantiates, and everything under
`src/KeePassGate/Bridge`. `grep -rlE '^using (KeePass|KeePassLib)[.;]' src` is the check, and it is
a check rather than a claim. Anchored, because the plugin's own namespaces begin `KeePassGate` and a
bare `using KeePass` matches every file that imports one of them. `KeePassContract` also names some
of KeePass's members through `nameof`, which compiles to the string the host tier pins and leaves no
reference to KeePass behind, so the check rightly does not list it.

The consequence worth having: the unit project **cannot** compile against KeePass, so a dependency
that creeps up into the pipeline shows up as a build error rather than as a design argument.

**The transport is tested there too**, over a real loopback socket with real TLS 1.3 and real
certificates, because none of that needs a password manager. `GateListenerTests` is the one to read
first: it is where the pieces stop being separately correct and start being a server.

## What the host tier is for

Two jobs.

**It pins the names.** `KeePassContractTests` asserts every name the plugin reaches for inside
KeePass: the dialog type, the tab control called `m_tabMain`, the window manager's events, the
plugin base class. A KeePass upgrade that moves one of these is caught here, before anybody runs it.
At run time the plugin notices some of them itself and refuses rather than guessing: a dialog the
watch cannot find stops every write and every pairing and the log names it, and a field a tab
reads on a dialog it decorates leaves that tab read only with a sentence saying why. The rest would
break it quietly.

**It uses the real thing.** `DatabaseSettingsForm` can be constructed outside a running KeePass, and
`GlobalWindowManager.AddWindow` is public, so the plugin is driven exactly as KeePass drives it:
initialize, open the real dialog, close it, terminate, and check that nothing of ours is left behind.
The authority is exercised against a real `PwDatabase` as well, written to a real KDBX4 file and
opened again, because a dictionary cannot prove that KeePass keeps what it is given.

The access control rules are split the same way, and the split is worth reading as an example of the
whole arrangement. What a grant **means** is decided against `IAclNode`, a document and a parent, so
`AclResolverTests` builds the interesting trees in three lines each and runs in the unit tier.
`AclInRealDatabaseTests` asks the questions KeePass alone can answer, which are that an entry
carries custom data at all, that an unwritten key reads back as nothing, and that the parent chain
really reaches the root from an entry three groups down. Then it runs the resolver's own rules over
a real tree, to see them hold on what KeePass hands back rather than to settle what they mean:
nearest wins, a seal, a tree with no rule granting nothing, the rules surviving a save and a
reopen, a decision naming its node by identifier, and a walk taken while the rules move.

**What it cannot do is show those dialogs.** `Show` throws inside the message pump outside a running
KeePass, and WinForms answers with its unhandled-exception dialog, which blocks the run until
something kills it. So `SnapshotHazardTests` measures the group dialog's custom data snapshot in two
halves: the mechanism by reflection over the real types, and the behavior by reproducing it with
KeePass's own `CloneDeep` and a real group. Pressing buttons for real belongs to the tier below.

## What the end to end tier is for

The acceptance tests for the build order, stated the way the steps state themselves.

**Step 4**, in `LiveKeePassTests`: the plugin loads into KeePass, shows a KPG tab on the database
settings dialog, and stops cleanly.

**Step 5**, in `LiveAuthorityTests`: an authority created from that tab in a real KeePass is still
there, with the same fingerprint, after the database has been saved and KeePass has been closed and
started again. That second session is the whole point. Everything else in the suite proves the
plugin writes what it means to write; only this proves KeePass keeps it.

**Steps 6 and 7**, in `LiveGateTests`: a real client, in the test process, reaches a real KeePass
over mutual TLS 1.3 and is answered, and a second certificate from the same authority is refused
because the database holds no record for it. It issues itself both certificates from the authority
inside the database file, because it holds the file and its password, which is exactly what the
design says possession of the file means. Nothing about this one uses the desktop.

Note what it does **not** do: write a record while KeePass is running. A running KeePass serves the
database it holds in memory, so a record written to the file underneath it is invisible and would be
overwritten. Pairing writes through the plugin for that reason, which is what `LivePairingTests`
covers.

**The lock**, in `LiveLockTests`: a real KeePass locked from its own shortcut, and a client answered
a moment before is told `503 locked`, by the responder the plugin kept in memory for exactly this.
It is the one place the chain from KeePass's own lock to an answer on a socket runs whole. The host
tier drives the plugin's half with the events in the order KeePass raises them, which is a model
read off one KeePass; this is what notices a KeePass that raises them differently.

**The lock timer**, in `LiveLockTimerTests`: the one session that sets KeePass's own inactivity
lock, ten seconds, and lets it fall due under a renewal dialog nobody answers. The dialog closes as
unanswered and the client is told `503 locked`, `PAIRING.md` §308.

**Step 8**, in `LivePairingTests`, and it is the longest chain in the suite. The operator presses
the invite button on the real tab; the token is read off the real screen; a client holding nothing
but that token and a port number derives its own connection names, proves itself and is proved to;
the approval dialog is a real window, approved by a real button press on a thread that is not the
one waiting for the answer; the certificate that comes back is then used on the very next
connection; and the record is looked for in the `.kdbx` after KeePass has exited. A refusal is
driven the same way, and leaves nothing behind.

**The API**, in `LiveApiTests`: a real client with a certificate the database's own authority issued
creates an entry through the gate, adds a field, reads it back, lists it, and the entry is read out
of the `.kdbx` after KeePass has exited. A second client, whose record names a profile no rule
mentions, is answered exactly as a missing entry would be. The grant and both admission records are
written before KeePass starts, because a running KeePass would overwrite anything put into the file
underneath it.

**The reference client**, in `LiveReferenceClientTests`: PyKeePassGate itself, run through `uv` from
the repository as the client tier runs it, pairs with a real KeePass through an invitation read off
the real tab and approved on the real dialog, then asks for the status, creates an entry, lists it
and reads it back. It is the one test in which the plugin's own PKCS#12 export meets `cryptography`,
Bouncy Castle's TLS meets Python's `ssl`, and the plugin's framing meets the client's reader. Without
`uv` it is ignored, as the tier is without a KeePass.

It goes on to hold the client's answers against the plugin's own. The admission key the client
works out from its certificate is the one the gate names it by, and the gate says which profile
counts. Under a rule granting `P`, the password the client stored reads back through the field
route, and a new one it sets reads back as the new one. The gate says what the client may do at
the entry and at the root, in the tab's notation. It then renews through the real renewal dialog
with `pykeepassgate renew`, which stores the new identity and gives up the certificate it renewed
from, so `renew` and `revoke_certificate` meet the plugin as well; every step after that runs on
the renewed identity. A write made while the entry's dialog is open in KeePass is turned away as
`GateBusy` and changes nothing, and once KeePass is locked the client meets `DatabaseLocked`. What
the command line does not offer, one field on its own route and a changed password, goes through
the library with `tests/pykeepassgate/live_calls.py`, which the client tier runs against its
stand-in as well.

**Step 13**, in `LiveGrantEditorTests`: a rule made on the KPG tab of a real KeePass, kept by the
group dialog's **OK**, and read out of the `.kdbx` after the process has gone. Pressing OK rather
than Escape is what it is for: the dialog writes its own copy of the group's settings back over the
group at that moment, so a rule written anywhere else would be destroyed exactly there.
`LiveAuthorityTests` presses OK too, for the reason the end of this document gives, so this is no
longer the only one that does. It clicks with the pointer, because neither column on that table can
be typed into and a list is not something the accessibility tree can be asked to operate, and
`LiveRenewalTests` below does too, to choose from a menu. The screenshots move it as well, parking
it clear of each window before the picture is taken, so that no tooltip covers what the picture
shows, and choosing from the menu one picture leaves open once it has been taken.

**A client's own certificate**, in `LiveCertificateTests` and `LiveRenewalTests`. A client revokes
the certificate it presents and is refused on its very next connection, and the revocation is in the
`.kdbx` after KeePass has exited. And an admitted client asks to renew: the renewal dialog is a real
window approved by a real button press, and the certificate that comes back opens a route on the
next connection. The new certificate's own renewal is then refused in this session from the Refuse
button's menu, opened and chosen with the pointer in KeePass's own modal loop, and asking again is
answered `409 this session` with nobody asked. After that the client gives the old certificate up
by presenting it and is refused with it, and once KeePass has gone the file holds the new record
and not the old one, and nothing of the refusal in this session.

**The authority chooser**, in `LiveAuthorityChooserTests`: two copies of one database each replace
the authority they share and admit a client of their own, the copy KeePass opens is merged with the
other through File, Synchronize, and the dialog that raises is answered through its own buttons. It
is the one test that runs what the chooser's timing was read off KeePass to be: that a synchronize
announces itself only by the save it ends with. While the dialog is open both clients are told
`503 undecided`; once one authority is kept it serves at once, the other copy's client reaches
nothing, and the database is marked changed and left unsaved until the test saves it.

The client half of pairing lives in `tests/Shared/PairingClient.cs` and is compiled into **both**
test assemblies. That is deliberate: if the loopback tests and the end to end test each had their
own client, either could pass against a protocol only it knew.

It builds a **throwaway KeePass** under `%ProgramData%\KeePassGateLive` rather than the temporary
directory, whose path names the account that ran it: a copy of the program, a local
configuration, the plugin in `plugins`, and a database created with KeePassLib. Nothing it touches is
the developer's own. The database is invented and empty, and the password is passed on the command
line, which is acceptable for exactly this reason and no other.

Then it starts that KeePass. `LiveAuthorityTests`, for one, walks File, Database Settings with the
keyboard, reads the tab through the accessibility tree, presses the buttons on it, accepts the
dialog with OK, saves with the keyboard, closes KeePass, and checks the exit code. It also reads
the plugin's own log, which `KEEPASSGATE_LOG_FILE` puts in the session's own directory: that log is
how a test sees from outside what the plugin did inside a process it does not otherwise hold.

**Dialogs are accepted with OK where the test is about what OK does, and dismissed with Escape
otherwise.** A test that closes a dialog with Cancel can never be the reason something was lost,
which is why Escape is the default. The KPG tab holds everything until the dialog is accepted, so
`LiveAuthorityTests` has to press OK or it would measure nothing; and `LiveGrantEditorTests` presses
OK because the snapshot KeePass writes back is exactly what it is asserting about. What the
snapshot hazard still requires is measured directly in the host tier, against the group dialog
that does carry one.

**It types on the real desktop**, so leave the machine alone while it runs. It is excluded from the
default run for that reason, not because it is optional. [`FINDINGS.md`](FINDINGS.md), under
"Driving KeePass through the desktop", records the traps that make automating a WinForms menu
harder than it looks. Cited by its title rather than its number, because the numbering shifts
as sections are added above it and a number here goes stale without anything noticing.

**A failed run prints what it left behind**, because the console is often all that a run keeps.
A session whose fixture failed keeps its directory rather than deleting it, plugin log and all, and
a KeePass that had to be ended rather than closing leaves a line there saying which windows it was
showing. `build.ps1 -Task Live` writes out each failed test's name, message and stack from
`live.trx`, then the end of every plugin log and every such line a session of that run kept, before
failing as it would have anyway. The next run of the same fixture deletes the directory as it
begins.

## What the end to end tier builds against

The KeePass it starts is the one `keepass.version` pins, provided into `.keepass\<version>\` by
`build.ps1` before anything runs. That matters more here than anywhere else: a live test measures a
real program, so which program it is has to be a fact about the repository rather than about the
machine, or a failure means nothing until somebody asks what was installed.

`KeePassDev.MatchesTheAssemblyUnderTest` is the other half of that, and it stays: the directory the
tests drive has to hold the same KeePass the tests compiled against, or the live tier is measuring
something the rest of the suite is not.

## What the client tier is for

PyKeePassGate has three tiers of its own, and the first two draw the same line the C# ones do.

**Against a gate that is not KeePass.** `pykeepassgate.testing` holds an authority, a host
certificate under it, and a TLS 1.3 server that answers one request per connection, which is what
the plugin's transport does, and the fixtures in `tests/pykeepassgate/conftest.py` build one for
each test. The package ships it, so that a program built on the client can be tested the same way,
and that is why it imports no test framework. That covers everything a client has to get right
about the protocol, including the parts `ssl` decides: that the rotating name arrives as SNI, that
the server's certificate is readable before a body is written, and that loading one authority
really is a pin. One thing it does differently from the plugin, and says so: a client certificate
that is offered is verified in the handshake, since `ssl` cannot ask for one without, so an expired,
not yet valid or foreign one ends the handshake with an alert where the plugin completes it and
answers `401`.

**Against the plugin's own output.** `tests/pykeepassgate/test_protocol.py` pins the token
encoding, the name construction and all three proofs against vectors produced by `PairingToken`,
`SniName` and `PairingProof`. Without those, the two halves could agree on something neither
document describes, because both halves would be code somebody wrote here. `ProtocolVectorsTests`
holds the plugin to the same values over the same inputs, so a change to either half fails in that
half's own tier rather than at the first real pairing.

**Against the document the plugin embeds.** `tests/pykeepassgate/test_reference.py` reads the
same `openapi.yaml` the plugin ships and holds the client to it: a route the document declares
with no call here fails, and a call here with no route fails. It needs no server and no
vectors, because the document is the thing both halves are being compared against. That is
what stops "the reference client" being a claim rather than a check.

**Against the plugin itself, in the end to end tier rather than here.** Every tier above runs
PyKeePassGate against a stand-in, a vector or the document, so three things are held only by
`LiveReferenceClientTests`, which pairs this client with a real KeePass and reads through the gate:
the pairing identity, which the plugin writes with .NET Framework's PKCS#12 export and this client
reads with `cryptography`, since the stand-in's identity is one `cryptography` wrote; the TLS 1.3
handshake between Bouncy Castle's server and Python's `ssl`; and the plugin's framing under the
client's reader. A `cryptography` release that stopped reading what .NET Framework exports would
break every pairing, and that test is the one that would say so. It runs where the end to end tier
does, so a green client tier says nothing about any of the three.

**Seven of them do not run on Windows, and run on Linux in CI.** The client narrows the
directory and the files it keeps a paired identity in, refuses a directory other accounts may
write to, refuses a link where one of those files is about to go, and reads `XDG_CONFIG_HOME`
where Windows reads `LOCALAPPDATA`. Four of the tests ask questions Windows answers with an access
control list rather than with nine bits, two need a link, which takes a privilege on Windows, and
one reads a variable Windows does not, so all seven are skipped there, and a run on the machine
this is developed on reports `7 skipped`. The code is guarded by the same platform test, so what is
skipped is the check rather than the behavior, and the check is not skipped everywhere:
`build.ps1 -ListGates` gives the client's check a second runner, `ubuntu-latest`, where those seven
run and the one test about a Windows roaming profile is skipped instead.

```shell
uv run pytest --cov        # the client, with the coverage floor the gate enforces
```

```powershell
.\build.ps1 -Task Client         # the same thing, and its style and types
```

## Conventions

- One assertion subject per test, and the name is a sentence about behavior rather than about a
  method.
- Tests touching WinForms carry `[Apartment(ApartmentState.STA)]`.
- A test that needs something the machine may not have calls `Assert.Ignore` with the reason, so a
  missing KeePass reads as "not measured here" rather than as a failure.
- Failure messages carry what was actually there: the tabs on the dialog, the windows on screen, the
  text that was read.
- A unit test that touches anything the whole process shares carries `[NonParallelizable]`, on the
  test or on its fixture, with the reason beside it. The next section says what counts.

## What runs at the same time

**The unit tier runs its fixtures side by side**, `[assembly: Parallelizable(ParallelScope.Fixtures)]`
in `Parallelism.cs`, and the tests inside a fixture still run in order. A fixture or a test marked
`[NonParallelizable]` runs with nothing else beside it. These are what earn the mark, because each
is shared by the whole process or the whole account:

- an environment variable the plugin reads;
- `Trace.Listeners`, and anything counting the lines that reach them;
- the time zone, which every chain build and every `NotAfter` reads;
- a shown window, the focus, a posted key or a menu left open, since a window another fixture shows
  takes the activation and closes the menu. Another test process shows its windows on the same
  screen, and no mark reaches it, so such a fixture carries `[Desktop]` as well, which holds the
  screen against every other test process for each of its tests;
- a static the plugin lends out for a while and takes back;
- the account's key directories: a certificate loaded without `EphemeralKeySet` writes its key
  there, which every fixture issuing a client does, so a test asserting that none appeared is
  marked and the writers are not. Another test process writes there too, and no mark reaches it,
  so `PrivateKeyFiles.Lock` is held by a test while it watches and by a helper while it loads;
- a timing written tight enough that a loaded machine misses it, which is better widened than
  marked.

A fake that the gate's threads write while a test reads takes a lock, as `RecordingGateLog` does.
Without one the test reads a list that is being added to, which throws only when the timing is
unlucky and so reads as a flake.

**The host tier stays one test after another**, and that is a choice rather than an omission. Its
fixtures drive KeePass's own statics, which belong to the whole process as much as anything above
does, and the plugin reads them. A mark forgotten there would fail now and then rather than
plainly, and the tier is a minute or two either way.

**And the client tier runs beside both.** `build.ps1` starts it as a process of its own while the
plugin builds and its tiers run, prints its output once they are done, and fails if either half did.

## What the coverage floor holds

`.\build.ps1 -Task Coverage` runs the unit and host tiers and measures what they reach in the
plugin, failing under `-CoverageFloor`, which defaults to 80. Both tiers rather than either,
because neither answers on its own: a line the host tests reach and the unit tests do not is
covered, and read apart the two say far less than they say together. Only the `KeePassGate`
package is counted, since that is the code that ships, and counting the test assemblies would
mostly be the unit tests covering themselves.

The floor sits below where the plugin measures, and that gap is deliberate: a commit that adds a
file before the tests that cover it should not fail a check about the whole. A run that finds no
report fails rather than passing, because a check that is silently measuring nothing is worse than
no check.

**And a run measures only the reports it wrote.** `.cov` is emptied before the tiers run, and each
report is read once, since the logger that writes a tier's results copies every attachment into a
directory of its own. A report an earlier run left behind would count the lines that run reached as
covered now, which can only overstate, and for as long as nothing emptied `.cov` that is what the
figure did.

## Before a commit

There is CI, and there is no branch protection, because none is configured.
`.\build.ps1 -Task All` is the gate, and nothing stops a push that skipped it.

**The fast half can run at the commit itself, once it is installed.** `.pre-commit-config.yaml`
holds the checks cheap enough to run on what is being committed: the client's style and types, the
spelling and the markdown rules, each run by the tool and the configuration the gate uses, and a
refusal of a `.kdbx`, a private key or a merge left half done. Nothing installs it, so a clone has
none of it until somebody runs this once:

```powershell
uv run pre-commit install
```

It is not the gate, and does not try to be: the C# build and every test tier are left to
`build.ps1`, which is what a branch is judged by.
