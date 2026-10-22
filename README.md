# KeePassGate: what a version of a group or an entry can be made of

**Measurements, kept for reference and never merged.** KeePassGate answers every read of one group
or one entry with an `ETag` and refuses a write whose `If-Match` no longer describes the node. Whether
KeePass already keeps something that can serve as that tag, a version counter or a modification time,
is a question about KeePass rather than about the plugin, and it was answered by running KeePass.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. What the probe settled is in [`RESULTS.md`](RESULTS.md), with the recorded output beside
it under `spikes/tags/output`.

## Running it

Provide the pinned KeePass first, into a directory that is not committed:

```powershell
. .\tools\KeePass.ps1
Install-KeePass
```

Then build and run the probe. The first argument is a work directory; the second chooses the
question.

```powershell
dotnet build spikes\tags -c Release -o spikes\tags\out
spikes\tags\out\Probe.exe work\times times   > times.txt    # the clock, a save, a merge, a move
spikes\tags\out\Probe.exe work\survey survey > survey.txt   # every method that changes a node, from the IL
spikes\tags\out\Probe.exe work\binary binary > binary.txt   # whether an attachment can change once made
spikes\tags\out\Probe.exe work\cost cost     > cost.txt     # what an HMAC over a node costs
```

No database is written to disk: each one is made in memory and copied through a KDBX 4 stream, which
is what a second machine's copy of the file is.

## Reading a re-run

`binary.txt` and `survey.txt` are read off KeePass itself, so a re-run against the same release is
line for line the recorded output. `times.txt` prints ticks and `cost.txt` milliseconds, which differ
from run to run and machine to machine, and what is compared is the shape: whether a step shares a
time, whether a merge moved one. A difference in shape is a finding: record it in `RESULTS.md` beside
what it contradicts.
