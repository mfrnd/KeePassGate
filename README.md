# KeePassGate: what KeePass and KeePassXC do to a database's custom data

**Measurements, kept for reference and never merged.** KeePassGate keeps its certificate authority,
its admission records and its revocations in `PwDatabase.CustomData`, and a database that is
synchronized between machines is merged by KeePass key by key. Whether a revocation or a recreated
authority survives that merge is therefore a question about KeePass rather than about the plugin,
and it was answered by running KeePass.

The same file can also be opened by another program. What KeePassXC does to it when it saves and
when it merges, and what KeePass 2.47 does when it saves, was answered the same way: by running
six KeePassXC releases from 2.4.1 to 2.8.0, chosen where its behavior changes, and KeePass 2.47
against throwaway databases shaped like the plugin's.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. What the probe settled is in [`RESULTS.md`](RESULTS.md), with the recorded output beside
it under `spikes/revocation/output`.

## Running it

Provide the pinned KeePass first, into a directory that is not committed:

```powershell
. .\tools\KeePass.ps1
Install-KeePass
```

Then build and run the probe. The first argument is a work directory for the throwaway databases it
creates; the second chooses the question.

```powershell
dotnet build spikes\revocation -c Release -o spikes\revocation\out
spikes\revocation\out\Probe.exe work\rules rules     > rules.txt    # the rule, one key at a time
spikes\revocation\out\Probe.exe work\matrix          > matrix.txt   # revocation designs, about 2.5 minutes
spikes\revocation\out\Probe.exe work\layout layout   > layout.txt   # authority layout, about 4 minutes
```

Every file it writes goes under that work directory, which is not committed either. Point `TMP` and
`TEMP` somewhere disposable as well if the build should leave nothing behind in the profile.

## Running KeePassXC and KeePass 2.47 against it

They are provided the same way, each into a directory named after its version that is not
committed. `keepassxc.sha256` pins the KeePassXC releases and `keepass.sha256` the KeePass ones; a
release with no line there is refused, and each program's Authenticode signature is checked every
time it is asked for. `-Archive <zip>` takes an archive already downloaded in place of a download,
checked exactly as a download is.

```powershell
. .\tools\KeePassXC.ps1
Get-KeePassXCPin | ForEach-Object { Install-KeePassXC -Version $_.Version }
Install-KeePass -Version 2.47
```

Then, with the probe built as above, from Windows PowerShell 5.1:

```powershell
powershell.exe -NoProfile -File spikes\revocation\Measure-KeePassXC.ps1 -Work work\keepassxc
```

That writes `keepassxc-save.txt`, every release saving a copy of P, KeePass 2.47 included, and
`keepassxc-merge.txt`, every release merging C and P both ways, three runs a cell, into
`spikes/revocation/output`. It takes about 50 minutes, nearly all of them the merges with a slow
KDF. `-Scenario save` or `-Scenario merge` runs only that scenario, and `-Release` and `-Runs` run
fewer; a run of fewer than were recorded takes `-Output` somewhere else, since each scenario
replaces its whole file.

## Reading a re-run

The output names times symbolically rather than printing them, so a re-run against the same KeePass
is line for line the recorded output. A difference is a finding: record it in `RESULTS.md` beside
what it contradicts.

The KeePassXC output names them by where they came from. `C` and `P` are the time that copy was
given, `=` a time both copies share, and `none` no time at all. `save`, `touch`, `dry` and `merge`
are a time inside the span in which another program ran: a save, the save that gives C KeePassXC's
own marker before a merge, a dry run and a merge. Each program starts in a second of its own, so a
time stored to the second names a single program. A value is `C`, `P` or `=` in the same way, `?`
a value neither copy holds and `-` absent. KeePassXC's random marker is `<random>`, and its date
marker `<date language, span>`.

Two kinds of cell depend on where a second boundary falls, by design, and every other cell should
come back exactly:

- **The `fast` merges.** Each merge starts just after a second begins and both copies load in a
  small fraction of it, so they load in the same second and KeePassXC changes no custom data. A
  merge slow enough to reach its second load in the next second gives the `slow` answer instead.
- **The `slow` merges** give their answer because each load takes more than a second, so the two
  always fall in different seconds. A machine fast enough to load both inside one second would turn
  them into `fast` cells.

And one value depends on the machine: the language of KeePassXC's date marker is the system's
before 2.8.0, recorded here as `de-DE`, and English from 2.8.0.
