# The probe beside the three proofs

`BclReuseProbe.cs` is not a candidate and vendors nothing. The three directories here answer
*"whose HTTP implementation should this take?"* and settled on none, because owning the accept loop
reduced every candidate to a parser fed from a stream. This asks the question that is left over
once that is decided:

> Given the transport is ours, which of its pieces are already in an assembly the plugin loads
> anyway, so that fewer of them have to be?

It is one file because it measures behavior rather than integration, and it references nothing
outside the framework: `WebHeaderCollection`, `ContentType` and `Uri` all ship in `System`, which
the plugin already references for its sockets. So nothing here is a dependency question. It is a
question about which of two behaviors is wanted.

## Running it

**Windows PowerShell, not PowerShell 7.** PowerShell 7 runs on .NET Core, where
`WebHeaderCollection` is type-forwarded to another assembly and the question changes.

```powershell
Add-Type -TypeDefinition (Get-Content -Raw spikes\BclReuseProbe.cs) -ReferencedAssemblies 'System','System.Core'
[BclReuseProbe]::Run()
```

It opens no socket and writes no file.

What it settled is under "What the framework already does" in [`RESULTS.md`](../RESULTS.md).
