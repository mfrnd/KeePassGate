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
.\build.ps1                # the prose, the client tier, then the unit and host tiers
.\build.ps1 -Task Live     # the end to end tier
.\build.ps1 -Task All      # everything
```

## Why the unit tier does not reference KeePass

Everything above the KeePass boundary is written against small interfaces: `IWindowEventSource`
announces dialogs, `IFormMatcher` recognizes them, `IFormDecorator` decorates them,
`IGatePanelFactory` fills them, `IKeyValueStore` stores things, `IGateTargets` says what can be
served, `IGateContext` is one request, and `IGateLog` records all of it. Two things touch KeePass:
`KeePassGateExt`, which KeePass itself instantiates, and everything under
`src/KeePassGate/Bridge`. `grep -rlE '^using (KeePass|KeePassLib)[.;]' src` is the check, and it is
a check rather than a claim. Anchored, because the plugin's own namespaces begin `KeePassGate` and a
bare `using KeePass` matches every file that imports one of them.

The consequence worth having: the unit project **cannot** compile against KeePass, so a dependency
that creeps up into the pipeline shows up as a build error rather than as a design argument.

**The transport is tested there too**, over a real loopback socket with real TLS 1.3 and real
certificates, because none of that needs a password manager. `GateListenerTests` is the one to read
first: it is where the pieces stop being separately correct and start being a server.

## What the host tier is for

Two jobs.

**It pins the names.** `KeePassContractTests` asserts every name the plugin reaches for inside
KeePass: the dialog type, the tab control called `m_tabMain`, the window manager's events, the
plugin base class. A KeePass upgrade that moves one of these breaks the plugin quietly at run time
and loudly here.

**It uses the real thing.** `DatabaseSettingsForm` can be constructed outside a running KeePass, and
`GlobalWindowManager.AddWindow` is public, so the plugin is driven exactly as KeePass drives it:
initialize, open the real dialog, close it, terminate, and check that nothing of ours is left behind.
The authority is exercised against a real `PwDatabase` as well, written to a real KDBX4 file and
opened again, because a dictionary cannot prove that KeePass keeps what it is given.

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

**Step 8**, in `LivePairingTests`, and it is the longest chain in the suite. The operator presses
the invite button on the real tab; the token is read off the real screen; a client holding nothing
but that token and a port number derives its own connection names, proves itself and is proved to;
the approval dialog is a real window, approved by a real button press on a thread that is not the
one waiting for the answer; the certificate that comes back is then used on the very next
connection; and the record is looked for in the `.kdbx` after KeePass has exited. A refusal is
driven the same way, and leaves nothing behind.

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

**The settings dialog is accepted with OK where the test is about what OK does, and dismissed with
Escape otherwise.** A test that closes a dialog with Cancel can never be the reason something was
lost, which is why Escape is the default. The KPG tab holds everything until the dialog is
accepted, so `LiveAuthorityTests` has to press OK or it would measure nothing. What the snapshot
hazard still requires is measured directly, in the host tier, against the group dialog that does
carry one.

**It types on the real desktop**, so leave the machine alone while it runs. It is excluded from the
default run for that reason, not because it is optional. [`FINDINGS.md`](FINDINGS.md), under
"Driving KeePass through the desktop", records the traps that make automating a WinForms menu
harder than it looks. Cited by its title rather than its number, because the numbering shifts
as sections are added above it and a number here goes stale without anything noticing.

**A failed run prints what it left behind**, because the console is often all that a run keeps:
`build.ps1 -Task Live` writes out each failed test's name, message and stack from `live.trx`, and
the end of any plugin log a session of that run did not delete, before failing as it would have
anyway.

## What the end to end tier builds against

The KeePass it starts is the one `keepass.version` pins, provided into `.keepass\<version>\` by
`build.ps1` before anything runs. That matters more here than anywhere else: a live test measures a
real program, so which program it is has to be a fact about the repository rather than about the
machine, or a failure means nothing until somebody asks what was installed.

`KeePassDev.MatchesTheAssemblyUnderTest` is the other half of that, and it stays: the directory the
tests drive has to hold the same KeePass the tests compiled against, or the live tier is measuring
something the rest of the suite is not.

## What the client tier is for

PyKeePassGate has two tiers of its own, and they draw the same line the C# ones do.

**Against a gate that is not KeePass.** `tests/pykeepassgate/conftest.py` builds an authority, a
host certificate under it, and a TLS 1.3 server that answers one request per connection, which is
what the plugin's transport does. That covers everything a client has to get right about the
protocol, including the parts `ssl` decides: that the rotating name arrives as SNI, that the
server's certificate is readable before a body is written, and that loading one authority really is
a pin.

**Against the plugin's own output.** `tests/pykeepassgate/test_protocol.py` pins the token
encoding, the name construction and all three proofs against vectors produced by `PairingToken`,
`SniName` and `PairingProof`. Without those, the two halves could agree on something neither
document describes, because both halves would be code somebody wrote here. `ProtocolVectorsTests`
holds the plugin to the same values over the same inputs, so a change to either half fails in that
half's own tier rather than at the first real pairing.

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
