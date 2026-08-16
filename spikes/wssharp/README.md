# websocket-sharp measurement

Same harness as `../httplistener`, different HTTP implementation. See [`RESULTS.md`](../../RESULTS.md).

```powershell
cd spikes/wssharp
dotnet build
.\bin\Debug\net48\KeePassGate.WsSharp.exe      # TLS 1.3 and mutual TLS
$env:KPG_PLAIN=1; .\bin\Debug\net48\KeePassGate.WsSharp.exe   # no TLS, to attribute failures
```

**0 changed lines to compile**, and TLS 1.3 is pinned through `ServerSslConfiguration` rather than
by patching anything. The problem is elsewhere: `HttpListenerRequest.GetClientCertificate()` throws
`NotSupportedException`, so the certificate is demanded and validated during the handshake and then
never shown to the handler. The proof works around it by recording the certificate in the validation
callback, which is racy under concurrency and is the reason this option is not recommended.
