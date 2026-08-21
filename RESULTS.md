# What the probe settled

One question about the machine rather than the product, with the output that answered it. The
output is recorded as it came back, so a re-run that disagrees is a finding rather than a mistake.

[`README.md`](README.md) says how to run it. The TLS and certificate measurements are a different
subject and are on `spikes/tls-and-certificates`.

| Probe | Question | Answer |
| --- | --- | --- |
| `BuildStepProbe` | Can the environment fail the gate while nothing is wrong? | **Twice.** A warning ends the run, and PowerShell 7 takes `Get-FileHash` away |

`build.ps1` is where this project keeps its definition of clean, and a gate that fails while
nothing is wrong teaches whoever runs it to disbelieve the result.

It is a script rather than a type, because what it measures is PowerShell:

```powershell
powershell -NoProfile -File spikes\BuildStepProbe.ps1
```

```text
A step that warns and succeeds
  a warning, under Stop                          threw RemoteException
  a warning, under Continue                      survived
  a real failure, under Continue                 threw

A cmdlet this runtime ships with
  the copy that shadows it                       ...\microsoft.powershell_7.6.5.0_x64__8wekyb3d8bbwe\Modules
  Get-FileHash, as inherited                     not a command at all
  Get-FileHash, own modules first                found
```

**A warning is not a failure**, and under `Stop` it becomes one as soon as anything upstream merges
the streams, which is what capturing a build's output does. Nothing in the script needs a `2>&1` for
this: the merge happens wherever somebody reads the output. `npm ci` warns about any configuration
key it does not recognize, and installing the prose tools is what a fresh clone does first.

**`Get-FileHash` is not missing so much as never was.** `pwsh` leads `PSModulePath` with its own
module directories, a Windows PowerShell started from it inherits them, and
`Microsoft.PowerShell.Utility` then resolves to a copy marked for a runtime this is not. It refuses
to load and takes the cmdlet with it, so the name is not a command at all rather than a command that
errors. `Expand-Archive` survives the same path, which is what makes this look like a broken script.

The probe borrows the path from `pwsh` itself rather than reading it off the current process, since
a Windows PowerShell started from a shortcut has a perfectly good one and would answer that nothing
is wrong.
