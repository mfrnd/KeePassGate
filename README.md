# Holding KeePass's thread still

**Can a connection thread read KeePass's objects while KeePass's own thread changes them, and if not,
what does it cost to stop KeePass's thread for the length of a read?** KeePassGate serves its API on
connection threads and read the database there with nothing held, on the argument that a read raised
nothing a retry could not take again. Review 4 found that argument answered the wrong question. This
branch measures what a read meets, what each way of keeping the two apart costs, and which way of
taking a turn on KeePass's thread leaves the operator's input and KeePass's timers alone.

It is a spike: taken from `master`, never merged, and nothing here ships. The mainline keeps what it
settled, in `DESIGN.md` §119 and `FINDINGS.md` §21, and a pointer to it.

## The probes

Each is one C# file, built on its own with the compiler .NET Framework 4.8 carries, and run from
Windows PowerShell. Those that use KeePass are built against KeePass 2.61.1's own `KeePass.exe`,
which holds KeePassLib, and none of them depends on the plugin, except `HoldCost`, which is about
nothing else.

| probe | what it asks |
| --- | --- |
| `GroupRace` | what `FindEntry`, `FindGroup`, `GetEntries` and `GetGroups` meet while another thread puts groups in at the front of a list and takes them off it, with the node sought last in its list |
| `NullAdd` | whether KeePass's lists accept a null, and where `PwObjectList` keeps its items |
| `VersionWalk` | whether a reader can tell it met a list in motion by reading `List<T>._version` before and after its walk |
| `ParkWalk` | whether holding KeePass's thread in a `BeginInvoke` callback, while the read runs on the reader's own thread, keeps the read exact, and how long the hold takes to begin |
| `Immutable` | whether a `ProtectedString` or a `ProtectedBinary` can change once made |
| `Mutators` | every method in `KeePass.exe` that calls one of the in-place mutators of an entry's dictionaries, or with names given, of those methods instead |
| `FieldWriters` | the same, over the methods that store to those fields |
| `UiPriority` | whether 32 readers holding a window's thread starve the operator's input and the window's timers, under four ways of taking the turn, and inside a message box, a WinForms dialog and a `DoEvents` loop |
| `NativeLoop` | whether another thread can tell, without asking, that a window's thread is inside a native dialog's loop |
| `HoldCost` | how long a whole request holds KeePass's thread, now that a request that changes nothing is decided and served inside one hold |

How long each step of the plugin's reads holds the thread was measured against the plugin's own
`KdbxVault` rather than by a probe here, since that is a claim about the product. `RESULTS.md` cites
it. **`HoldCost` is a claim about the product too, and is kept here all the same**, because the
figures it produced are the ones the mainline's documents give: it is a fixture for the plugin's
host test project and builds only there, against a mainline that decides and serves a read inside
one hold.

## Running them

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force bin | Out-Null
Copy-Item <a KeePass 2.61.1 folder>\KeePass.exe bin\

# The ones that use KeePass.
foreach ($probe in 'GroupRace', 'NullAdd', 'VersionWalk', 'ParkWalk', 'Immutable', 'Mutators', 'FieldWriters') {
    & $csc -nologo -platform:x64 -out:bin\$probe.exe -r:bin\KeePass.exe -r:System.Windows.Forms.dll "probes\$probe.cs"
}

# The two about the window's own thread.
foreach ($probe in 'UiPriority', 'NativeLoop') {
    & $csc -nologo -platform:x64 -out:bin\$probe.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll "probes\$probe.cs"
}

bin\GroupRace.exe
bin\ParkWalk.exe 10 0 0         # seconds, every how many changes one is slow, and how slow, in ms
bin\UiPriority.exe idle 32 17 30
bin\UiPriority.exe plain 32 17 60
bin\UiPriority.exe priority 32 17 60
bin\UiPriority.exe queued 32 17 100
bin\UiPriority.exe queued 32 17 0 box
bin\UiPriority.exe queued 32 17 0 dialog
bin\UiPriority.exe queued 32 17 0 doevents
bin\NativeLoop.exe
bin\Mutators.exe Application.DoEvents UIUtil.DoEventsByTime
```

**`HoldCost` runs from a checkout of the mainline**, copied into its host tests, and is taken out
again afterwards. It does nothing unless `KPG_HOLD_COST_OUT` names the file its table goes to:

```powershell
Copy-Item probes\HoldCost.cs <the mainline checkout>\tests\KeePassGate.Host.Tests\
Set-Location <the mainline checkout>
$env:KPG_HOLD_COST_OUT = "$PWD\hold-cost.txt"
dotnet test tests\KeePassGate.Host.Tests\KeePassGate.Host.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~HoldCost"
Remove-Item tests\KeePassGate.Host.Tests\HoldCost.cs
```

**`UiPriority` and `NativeLoop` need a desktop**, and a session that takes input: they open a window
at the top left of the primary screen and send it mouse moves through `SendInput`, at a point above
y = 78. `UiPriority`'s `queued` mode is the one the plugin was built on: holds in one queue, each
started on `Application.Idle`.
