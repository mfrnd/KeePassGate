# Findings

What was established by building things, kept after the proofs themselves were deleted. Everything
here was measured or executed rather than reasoned about, and each entry says how, so that a doubtful
reader can redo it rather than trust it.

[`ANALYSIS.md`](ANALYSIS.md) is the argument. This is the evidence. [`DESIGN.md`](DESIGN.md) is what
was built from it.

---

## 1. The platform

| Fact | How it was established |
| --- | --- |
| `.plgx` packaging forces **C# 5**. KeePass compiles it with the in-box CodeDOM provider, which rejects C# 6 string interpolation with "unexpected character '$'" | compiled both through `CSharpCodeProvider` |
| **DLL distribution lifts that**, allowing modern C# | the packaging format is the only thing imposing it |
| KeePass 2.61.1 is the newest release, and there is **no separate SDK**: `KeePassLib` lives inside `KeePass.exe` | keepass.info, and the portable archive's contents |
| KeePass is a **.NET Framework** application: `supportedRuntime v4.0` and `v2.0.50727`, CLR 4.0.30319 | read `KeePass.exe.config`, queried the runtime |
| Therefore **modern .NET is unreachable in process**. A process has one runtime; `net8.0` needs CoreCLR and `netstandard2.1` excludes .NET Framework | NetCoreServer has never shipped a Framework-consumable target: 5.0.0 is `net5.0` through 8.0.7 at `net8.0` |
| **`AesGcm` is absent from .NET Framework 4.8**, so no version upgrade unlocks an AEAD | tested on the 4.8.1 runtime |
| `CertificateRequest.Create(issuer, …)` and `SubjectAlternativeNameBuilder.AddUri` exist, so **the certificate authority needs no third-party library** | enumerated the API, then issued a real chain with it |

## 2. Transport

| Fact | How it was established |
| --- | --- |
| **mTLS works in process, and TLS 1.3 negotiates** on .NET Framework 4.8 | `SslStream` over `TcpListener`, server saw `CN=agent-fictitious` |
| **TLS 1.3 is Windows 11 and Server 2022 only.** Every Windows 10 build is unsupported | Microsoft's Schannel protocol table |
| Requiring it therefore excludes only Windows versions that are themselves out of support | Windows 10 ended support in October 2025 |
| **`HttpListener` cannot serve HTTPS** without an administrator certificate binding: `Start()` succeeds and the first request dies | ran it unelevated |
| Plain HTTP on loopback **does** work unelevated; only the wildcard prefix is refused | `http://localhost:port/` and `http://127.0.0.1:port/` both round-tripped |
| **`HttpListener` exposes no TLS control at all**, no `SslProtocols`, no certificate. Version selection is machine-wide Schannel policy | enumerated its members |

That last one is why http.sys was rejected: a design that promises to offer only TLS 1.3 cannot
delegate that promise to the user's registry.

## 3. Vendoring, measured

Three HTTP implementations, each vendored, built on net48, driven with TLS 1.3 and mutual TLS, and
asked to parse the same request.

| | files | bytes | **changed lines** | stub files | glue we write | verdict |
| --- | --- | --- | --- | --- | --- | --- |
| `dotnet/runtime` managed `HttpListener` | 27 | 152 KB | **116** | 7 | pipeline written | most expensive |
| uHttpSharp | 62 | 128 KB | **0** | 0 | **0** | smallest to own |
| **websocket-sharp** | 76 | 922 KB | **10** | 0 | 40 | measured, **not taken** |

Notes that cost time to learn:

- The managed `HttpListener` is **runtime-internal code**. It wants `SR`, `NetEventSource`,
  `LazyAsyncResult` and four shared helpers, and its cookie handling cannot be ported at all because
  the BCL keeps `CookieParser` and three `Cookie` members internal.
- Its **TLS path is dead code** by upstream's own comment, so injecting our stream is the only
  option there rather than a preference.
- **uHttpSharp already targets `v4.5`**, needs no port, and its `IHttpListener` returns an `IClient`
  whose first member is a `Stream`, which is the seam this design wants.
- **websocket-sharp's `GetClientCertificate()` throws `NotSupportedException`**, which looked fatal
  and is not: `HttpConnection` publishes the authenticated stream, so the peer certificate is one
  cast away. Five lines of code fix it.
- **Size predicted nothing.** The largest candidate needed ten lines and the smallest needed 116,
  because what mattered was whether the code was written to stand alone.

## 4. Spec-first binding

**Both candidates support it**, and the difference is 40 lines.

A Connexion-style router was built twice, about 200 lines each time: the OpenAPI document is the
routing table, handlers bind to `operationId` rather than to paths, a declared operation with no
handler is a startup error, path templates capture parameters, and the body is validated against the
declared schema before a handler runs. Both passed the same five checks over TLS 1.3 with a client
certificate.

- uHttpSharp supplies the pipeline and per-request state, so the glue is nothing.
- websocket-sharp's `HttpServer` is a facade with `OnGet` and `OnPost` events, but
  `WebSocketSharp.Net.HttpListener` exposes `Prefixes`, `SslConfiguration` and `GetContext`, so the
  accept loop and pipeline are ours: 40 lines. **Superseded**: the accept loop, the TLS and the
  request parser are all ours now, and no HTTP dependency is taken.

The tooling exists on .NET Framework, which was not obvious: **`Microsoft.OpenApi` 1.6.22** and its
readers target netstandard2.0.

**Nothing in .NET does this off the shelf.** Swashbuckle and NSwag are code-first or build-time
generators; runtime spec-driven routing has no mainstream .NET equivalent, so it is custom work on
any foundation.

## 5. Things that turned out to be wrong

Recorded because a reader who only sees conclusions will re-make the same mistakes.

| Claimed | Actually |
| --- | --- |
| TLS 1.3 was a caveat to design around | It is a choice. Requiring it excludes only unsupported Windows, and it is what makes certificate contents safe |
| Certificates should not name databases | With TLS 1.3 required the confidentiality objection falls. What remains is that **KeePass has no database UUID**: `PwDatabase` has only `DataCipherUuid` and `RecycleBinUuid`, so the identifier would have to be the root group's |
| ...and then they should not, after all | The question dissolved rather than being answered. Once each database carries **its own authority**, a certificate is bound to one database by whoever signed it, so naming it again in the claim is a second place to get it wrong. Claims became `kpg://profile/<name>` and the `kpg://any` wildcard went with them, which is what closed the escalation it had introduced |
| websocket-sharp was the right dependency | It was, while it terminated TLS for us. Selecting a host certificate by SNI made that impossible, and a library parsing only a header block does not earn a submodule. `uHttpSharp` fits the new seam natively with zero changes and is ruled out on **license**, LGPL-2.1, not on fit. See `DESIGN.md` §107 |
| Vendored code should be subset hard, deleting what is unwanted | Deleting is a dangerous edit in the most dangerous place, and it makes upstream fixes hard to port. Minimum divergence, refuse from outside |
| The HTTP server would have to be hand-written | Every objection was about packaging, none applies to source |
| websocket-sharp was disqualified | Ten lines |
| uHttpSharp supported spec-first binding "decisively" | Both do. It judged websocket-sharp by one facade |

## 6. Reconstruction

The proofs were deleted once they had answered their questions. To rebuild any of them:

1. **Transport.** `TcpListener` on loopback, `SslStream`, `AuthenticateAsServer(cert, true,
   SslProtocols.Tls13, false)`. Issue a CA, a server certificate and a client certificate with
   `CertificateRequest`, round-tripping each through PKCS#12 so the private key has a usable handle,
   and give the CA a longer validity than what it signs.
2. **Vendoring.** Fetch at a pinned commit, rename the namespace only where it collides, and record
   every patch. Measure divergence by diffing against the pinned upstream and discounting the
   namespace lines.
3. **Spec-first.** `Microsoft.OpenApi` to read the document, a path-template matcher over the
   segments, a checker over the subset the wire carries for the body, and a dictionary from
   `operationId` to handler with a startup assertion that the two agree.
4. **Client.** Use a conforming HTTP client. A hand-written request string was accepted by two of the
   three parsers and rejected by the third, which cost an hour of misattribution.

`TlsProbe.cs` on `spikes/tls-and-certificates` is kept because it answers the TLS question in
isolation and is the cheapest way to re-check it on a new machine.

## 7. The gate, and where it can run

**A warning is not a failure, and under `Stop` it becomes one.** A native command writing to stderr
raises a terminating error as soon as anything upstream merges the streams, which is what capturing
a build's output does. `npm ci` warns about any configuration key it does not recognize, and
installing the prose tools is the first thing a fresh clone does, so the gate ended on a warning and
named nothing while the command it ran had succeeded. Every step therefore runs under `Continue` and
is judged by its exit code alone. `BuildStepProbe.ps1` on `spikes/build-environment` reproduces it.

**A PowerShell 7 on the machine can leave this one unable to find its own cmdlets.** `pwsh` leads
`PSModulePath` with its own module directories, a Windows PowerShell started from it inherits them,
and `Microsoft.PowerShell.Utility` then resolves to a copy marked for a runtime this is not. It
refuses to load and takes `Get-FileHash` with it, so the name is not a command at all rather than a
command that errors, which is what makes it look like a broken script rather than a borrowed path.
Same probe.

**A hosted `windows-latest` runner has a real interactive desktop**, and this reverses what was
asserted before it was measured. The claim was that no hosted runner could drive a GUI and that the
end to end tier would need a self-hosted one. It is wrong: the runner reports `WinSta0` and
`Default`, a window from another process is visible, the foreground can be taken, synthesized
keystrokes and clicks arrive, and UI Automation describes another process. The whole end to end tier
passed there, **47 tests, none skipped**, the same count as locally on the day it was read, and the
pinned KeePass is fetched, hash checked and signature checked on the way. That count is a reading of
the runner rather than a standing claim about the tier: the suite grows, so what it does there at any
later commit is what that commit's run says and nothing here. The screen is 1024x768 with one
monitor, which is enough for the one place the tier moves a real pointer. `spikes/hosted-runner-desktop` holds
the probe and the numbers.

**Containers were never the alternative.** Linux is the wrong operating system for UI Automation,
`keybd_event` and WinForms on .NET Framework; Windows containers have no interactive desktop; and
GitHub does not support container jobs on Windows runners at all.

**Required status checks are available and not configured.** The workflow reports and does not
block, which is a property of the repository's settings rather than of the gate. Requiring the
gate before a merge is one setting and costs nothing.
