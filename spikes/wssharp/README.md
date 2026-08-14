# websocket-sharp measurement

Same harness as `../httplistener`, different HTTP implementation. See [`RESULTS.md`](../../RESULTS.md).

```powershell
cd spikes/wssharp
dotnet build
.\bin\Debug\net48\KeePassGate.WsSharp.exe      # TLS 1.3 and mutual TLS
$env:KPG_PLAIN=1; .\bin\Debug\net48\KeePassGate.WsSharp.exe   # no TLS, to attribute failures
```

**10 changed lines, in one file.** It compiles untouched and pins TLS 1.3 through
`ServerSslConfiguration` rather than by patching anything, because `EnabledSslProtocols` reaches
`AuthenticateAsServer` directly.

The one patch is `HttpListenerRequest.GetClientCertificate()`, which upstream leaves throwing
`NotSupportedException`. The certificate is not missing, only unexposed: `HttpConnection` publishes
the authenticated stream, so the peer certificate is one cast away. Five lines of code and five of
comment.
