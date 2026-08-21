# KeePassGate: the build environment probe

**A measurement, kept for reference and never merged.** `build.ps1` is the only definition of clean
this project has, so a gate that fails while nothing is wrong is worse than no gate: it teaches
whoever runs it to disbelieve the result. This asks whether that can happen, and the answer is
twice.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. The subject is the machine and the shell rather than the product, and depending on the
product would make the answer harder to trust and the branch harder to keep.

What it settled is in [`RESULTS.md`](RESULTS.md), with the recorded output beside it.

## Running it

**Windows PowerShell, not PowerShell 7.** It is a script rather than a type, because what it
measures is PowerShell:

```powershell
powershell -NoProfile -File spikes\BuildStepProbe.ps1
```

It changes nothing outside its own process and leaves nothing behind.

## What it found

**A warning is not a failure**, and under `Stop` it becomes one as soon as anything upstream merges
the streams, which is what capturing a build's output does. `npm ci` warns about any configuration
key it does not recognize, and installing the prose tools is what a fresh clone does first, so the
gate failed on a clean machine and reported a `RemoteException` naming nothing.

**`Get-FileHash` is not missing so much as never was.** `pwsh` leads `PSModulePath` with its own
module directories, a Windows PowerShell started from it inherits them, and
`Microsoft.PowerShell.Utility` then resolves to a copy marked for a runtime this is not. It refuses
to load and takes the cmdlet with it, so the name is not a command at all rather than a command
that errors.

Both are fixed at the source on the mainline. This is kept because what it measures is somebody
else's behavior: a later PowerShell could make either false, and the fixes would then be guarding
against nothing.

## Why these are not tests

Nothing here is built by a gate and nothing asserts. A test says a thing this repository wrote
still works. These say a thing somebody else wrote still behaves as recorded, which is a question
that has to be asked again when their code changes.
