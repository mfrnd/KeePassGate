# KeePassGate: the TLS and certificate probes

**Measurements, kept for reference and never merged.** KeePassGate serves an API over mutual TLS
from inside the KeePass process, on .NET Framework, with a certificate authority that lives in the
database rather than in a store. Every part of that was doubtful enough to measure before anything
was designed on top of it, and most of what came back contradicts what the documentation suggests.

This branch is taken from `master` and holds nothing else: no plugin, no tests, no design
documents. Those are on the mainline branches, which carry what these probes established and not
the code that established it. What each one settled is in [`RESULTS.md`](RESULTS.md), with the
recorded output beside it.

## Running one

**Windows PowerShell, not PowerShell 7.** PowerShell 7 runs on .NET Core, which answers a different
question and fails these with type forwarding errors. Eight of the nine follow the same shape:

```powershell
Add-Type -TypeDefinition (Get-Content -Raw spikes\<Name>.cs) -ReferencedAssemblies 'System','System.Core'
[<Name>]::Run()
```

They build every certificate in memory, so they touch no certificate store and leave nothing
behind.

## The ninth

`ManagedTlsProbe` is the only one that references anything outside the framework: it serves TLS
with Bouncy Castle rather than SChannel, which is the answer to what `EphemeralKeyProbe` found.
Fetch the pinned version first, into a directory that is not committed:

```powershell
. .\tools\BouncyCastle.ps1
$bc = Install-BouncyCastle
Add-Type -TypeDefinition (Get-Content -Raw spikes\ManagedTlsProbe.cs) `
    -ReferencedAssemblies 'System','System.Core','System.Security',$bc
[ManagedTlsProbe]::Run()
```

`bouncycastle.version` says which version, `bouncycastle.sha256` says what it must arrive as, and
the script refuses anything else. That is the same arrangement the mainline uses for the KeePass it
builds against, and for the same reason: a measurement is about this branch rather than about
whatever happened to be on somebody's machine.

## Why these are not tests

Nothing here is built by a gate and nothing asserts. A test says a thing this repository wrote
still works. These say a thing somebody else wrote still behaves as recorded, which is a question
that has to be asked again when their code changes. The answers are quoted on the mainline and the
code that produced them is here.
