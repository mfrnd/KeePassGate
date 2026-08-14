# uHttpSharp measurement

Same harness as `../httplistener`, different HTTP implementation. See [`RESULTS.md`](../../RESULTS.md).

```powershell
cd spikes/uhttpsharp
dotnet build
.\bin\Debug\net48\KeePassGate.UHttp.exe
```

**0 substantive changed lines across 62 vendored files.** It already targets `v4.5`, so nothing had
to be ported, and its `IHttpListener` returns an `IClient` whose first member is a `Stream`, so the
authenticated stream is handed over by implementing two interfaces in our code rather than by
patching a constructor.

## Spec-first binding

```powershell
$env:KPG_SPEC=1; .\bin\Debug\net48\KeePassGate.UHttp.exe
```

A working mini-Connexion: the OpenAPI document is the routing table, handlers bind to
`operationId`, path templates capture parameters, and request bodies are validated against the
declared schema before a handler runs. `Microsoft.OpenApi` parses the document and `NJsonSchema`
validates, both on net48. See [`RESULTS.md`](../../RESULTS.md).
