# Transport proof

The first of the three spikes in `ANALYSIS.md`, and the riskiest, because
every other decision rests on it. It answers one question:

> Can a proven HTTP implementation be vendored onto .NET Framework and driven from a stream this
> project authenticated itself, rather than one it opened?

Yes.

## Run it

```powershell
cd spikes/httplistener
dotnet build
.\bin\Debug\net48\KeePassGate.Spike.exe
```

Output:

```text
runtime: .NET Framework 4.8.9337.0

issued by the platform, no third-party library:
  CA     CN=KeePassGate Pairing Authority
  server CN=localhost
  client CN=agent-fictitious  SAN=URL=kpg://7656adc24325684aa3ca8912185c0791/ops, URL=kpg://any/readonly

listening on 127.0.0.1:1735
  [client] negotiated Tls13

what the VENDORED parser produced from our stream:
  method        POST
  raw url       /v1/entries/lookup?url=example
  path          /v1/entries/lookup
  query         ?url=example
  version       HTTP/1.1
  content type  application/json
  length        17
  custom header yes
  body          {"query":"demo"}
  client cert   CN=agent-fictitious

ALL CHECKS PASSED
```

## What this establishes

- **The port is real.** 27 files of `dotnet/runtime`'s managed `HttpListener` compile on
  .NET Framework 4.8 and parse correctly.
- **Divergence is small enough to maintain.** 116 substantive changed lines, the rest being a
  uniform namespace rename. Every one is catalogued in [`vendor/VENDOR.md`](vendor/VENDOR.md).
- **Stream injection is one additive constructor.** No existing line of `HttpConnection` was
  modified; the coupling to the socket layer turned out to be three calls.
- **TLS 1.3 is pinned by offering nothing else**, not by checking afterwards, so a client
  certificate cannot cross the wire under an older version.
- **Mutual TLS works**, and the server reads the client certificate: `CN=agent-fictitious`.
- **The certificate authority needs no third-party library.** `CertificateRequest.Create` signs
  with an issuer and `SubjectAlternativeNameBuilder.AddUri` writes the `kpg://` claims, both from
  the platform.

## What this does not establish

Worth saying plainly, because a green run invites more confidence than it has earned.

- **Nothing runs inside KeePass yet.** This is a console harness. The plugin scaffolding, the UI
  thread and the packaging are untouched, and that is the next spike.
- **No authorization.** The `kpg://` claims are issued into the certificate and read back, and
  nothing yet intersects them with an ACL. That is the third spike.
- **The restrictions the analysis calls for are not implemented.** `Transfer-Encoding` is not yet
  refused, connection reuse is not yet closed off, and no timeouts are set. The design says to
  refuse these at our own boundary rather than by editing the vendored code, and that boundary does
  not exist yet.
- **One request, one shape.** This is a proof, not a test suite. A real implementation wants the
  parser fuzzed, which is cheap for something with this shape.

## Layout

| | |
| --- | --- |
| `vendor/` | the vendored sources, and [`VENDOR.md`](vendor/VENDOR.md) recording every change |
| `src/TlsHost.cs` | the half this project keeps: accept, terminate TLS 1.3, require a client certificate, hand over the stream |
| `src/HttpEndPointListener.cs` | the socket accept layer, replaced rather than vendored |
| `src/Program.cs` | the certificate authority and the end to end proof |
| `src/*Stubs.cs`, `src/SR.generated.cs` | the runtime internals the vendored code expects |
