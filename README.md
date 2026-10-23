# KeePassGate: why a test domain that drove KeePass cannot be unloaded

**Measurements, kept for reference and never merged.** KeePassGate's end to end tests drive a real
KeePass through `System.Windows.Automation`, and NUnit runs them in an application domain of its own
that it unloads when the run ends. Runs that read a dialog ended with "Exception encountered
unloading application domain", fifteen seconds after their last test. Which automation call leaves
the domain unable to unload, what it leaves behind, and what ends it are questions about .NET
Framework rather than about the plugin, and they were answered by running it against KeePass.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. What the probe settled is in [`RESULTS.md`](RESULTS.md), with the recorded output beside
it under `spikes/unload/output`.

## Running it

Provide the pinned KeePass first, into a directory that is not committed:

```powershell
. .\tools\KeePass.ps1
Install-KeePass
```

The probe compiles against nothing of KeePass's: it talks to a KeePass running in another process,
and the client side of UI automation comes from the framework's WPF reference assemblies. Build it,
then let `run.ps1` copy the pinned KeePass into a work directory beside a configuration that turns
off the update check, start it with no database, and ask every question in a process of its own:

```powershell
dotnet build spikes\unload -c Release -o spikes\unload\out
.\spikes\unload\run.ps1 -Out spikes\unload\output -Work work
```

Each question can be asked on its own as well, with the main window's handle in decimal:

```powershell
spikes\unload\out\Probe.exe menu <scenario> <handle>       # what the desktop tests do to a menu
spikes\unload\out\Probe.exe minimal <scenario> <handle>    # the smallest call that leaves the domain stuck
spikes\unload\out\Probe.exe tree <default|empty> <handle>  # the tree, with and without the managed providers
spikes\unload\out\Probe.exe stop <scenario> <handle>       # the queue thread ended before the unload
spikes\unload\out\Probe.exe members                        # what the tracker and the queue declare
spikes\unload\out\Probe.exe items                          # what a queue item is
spikes\unload\out\Probe.exe il                             # the IL that starts, waits on and feeds the queue
```

`Program.cs` lists the scenarios each one takes. The questions that drive KeePass run their
automation inside a child domain and then unload it, the way NUnit unloads a test domain. The tree
with a menu opened walks the whole desktop, which shows whatever else is open on it, so `run.ps1`
does not ask it and nothing of it is recorded.

## The per fixture runner

`spikes/unload/each-fixture.ps1` runs each `Live` fixture of a **KeePassGate checkout** on its own
and records whether that run ended with the unload failure. It runs against the checkout named by
`-Repo`, whose host tests must already be built in Debug and whose pinned KeePass must already be
provided, and it uses the desktop for several minutes.

**Run it unelevated**, as the desktop tier is meant to run. The first line of its `summary.txt` says
which it was.

## Reading a re-run

`members.txt`, `items.txt`, `il.txt` and the two trees are read off the framework and KeePass, so a
re-run on the same .NET Framework and the same KeePass is line for line the recorded output, but for
the unload times at the end of each tree and the numbers in the tree's `id` column, which are window
handles. The other files print milliseconds, which differ from run to run, and what is compared is
the shape: whether an unload succeeds, and whether it fails after about fifteen seconds. A difference
in shape is a finding: record it in `RESULTS.md` beside what it contradicts.
