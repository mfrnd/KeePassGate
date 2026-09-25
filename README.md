# KeePassGate: how KeePass merges a database's custom data

**Measurements, kept for reference and never merged.** KeePassGate keeps its certificate authority,
its admission records and its revocations in `PwDatabase.CustomData`, and a database that is
synchronized between machines is merged by KeePass key by key. Whether a revocation or a recreated
authority survives that merge is therefore a question about KeePass rather than about the plugin,
and it was answered by running KeePass.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. Those are on the mainline branches, which carry what this probe established and not the
code that established it.

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
