# Vendored sources

Provenance and every change made to them, so that taking a later upstream version is mechanical
rather than archaeological. This file is the obligation the analysis accepted when it chose to
vendor rather than to depend or to write.

| | |
| --- | --- |
| Upstream | [`dotnet/runtime`](https://github.com/dotnet/runtime) |
| Commit | `7cb858d4c2cc6c0d0eccc93d17644c92ea7bb1a3` |
| Paths | `src/libraries/System.Net.HttpListener/src/System/Net/` and `.../Managed/`, plus `src/libraries/Common/src/System/Net/` and `src/libraries/Common/src/System/HexConverter.cs` |
| License | MIT, retained in every file header |
| Files | 27 |

## How far this has moved from upstream

Measured against the pinned commit:

```text
changed lines total:       319
of which namespace/usings: 203
SUBSTANTIVE changed lines: 116
```

The 203 are the uniform namespace rename and the `using` lines it makes necessary, applied to every
file by script. The 116 that matter are listed below. No file was deleted, and no feature was
removed.

## What was changed, and why

### 1. Namespace, in all 27 files

`System.Net` becomes `KeePassGate.Vendor.Http`. Without this the vendored types collide with the
real `System.Net.HttpListenerRequest` and friends in `System.dll`. The build fails loudly if this is
ever undone, which is the correct behavior.

### 2. Stream injection, in `HttpConnection.cs`

One **additive** constructor that accepts a stream the caller has already authenticated, alongside
an already-accepted socket, so that TLS is terminated outside the vendored code. No existing line
was modified. This is the whole point of the exercise.

Upstream's own comment records that the managed implementation never negotiates TLS, so the HTTPS
path here is dead code; this constructor replaces exactly that dead path.

### 3. Framework compatibility

Mechanical substitutions where the source uses APIs newer than .NET Framework:

| Upstream | Replacement | Where |
| --- | --- | --- |
| `SearchValues<char>` and `ContainsAnyExcept` | same character set, index lookup | `HttpListenerRequest.Managed.cs` |
| `Span<Range>` and `MemoryExtensions.Split` | `string.Split(char[], 3)`, equivalent semantics | `HttpListenerRequest.Managed.cs` |
| `X509CertificateLoader.LoadCertificate` | `new X509Certificate2(...)` | `HttpConnection.cs` |
| `Marshal.GetLastPInvokeError` | `Marshal.GetLastWin32Error` | `HttpListenerException.cs` |
| `ArgumentNullException.ThrowIfNull` and friends | explicit guards | 7 files |
| `Encoding.Latin1` | `Encoding.GetEncoding(28591)` | `HttpListenerResponse.Managed.cs` |
| `Encoding.Preamble` | `Encoding.GetPreamble()` | `HttpListenerResponse.Managed.cs` |
| `ExceptionDispatchInfo.Throw(e)` | `Capture(e).Throw()` | request and response streams |
| span overloads of `int.Parse`, `Version.Parse`, `StringBuilder.Append`, `string.StartsWith`, interpolation | string equivalents | 4 files |
| `Stream.ValidateBufferArguments` | `StreamArguments.ValidateBufferArguments` | request and response streams |

**The request line split deserves its own note**, because it is the most security-relevant line in
the port. Upstream splits into three `Range` values, where a destination of three means the third
absorbs any remainder. `string.Split(new char[] { ' ' }, 3)` has exactly that behavior, including
for repeated separators, so the set of accepted and rejected request lines is unchanged.

### 4. Runtime internals, stubbed or restated in `../src`

The vendored code reaches for things the runtime provides to its own libraries and .NET Framework
does not expose.

| Internal | Treatment |
| --- | --- |
| `SR` | **generated from upstream's own `Strings.resx`** at the pinned commit, 88 strings, so the messages are upstream's rather than invented |
| `HttpKnownHeaderNames`, `HttpStatusDescription`, `UriScheme`, `HexConverter` | **vendored** from `Common`, same treatment as the rest |
| `NetEventSource` | no-op. Every call site is a trace, so this is behavior-preserving except for diagnostics, and an `EventSource` has no place in a password manager's process |
| `LazyAsyncResult` | minimal `IAsyncResult`, reached only by the client certificate path this port supplies directly |
| `HttpEndPointListener` | **replaced**, not vendored: it is the socket accept layer this design exists to take over. Its whole coupling to `HttpConnection` is three calls |
| `HttpListener` | placeholder. The vendored context refers to the type; its one method is only reached from the socket constructor this port does not use |
| `WebSocketValidate`, `HttpWebSocket` | present so the code compiles, throwing if ever reached. WebSocket upgrade is opt-in upstream, so it is unreachable unless a handler asks for it |

### 5. Two behavioral divergences, both in cookies

These are the only places where this copy behaves differently from upstream, and both exist because
the BCL keeps the machinery internal.

- **`CookieCollection.InternalAdd(cookie, isStrict: true)`** distinguishes "stored" from "an
  equivalent existed and could not be replaced". The public `Add` replaces silently, so setting a
  duplicate cookie succeeds here where upstream would raise.
- **`CookieParser`, `Cookie.ToServerString`, `Cookie.IsRfc2965Variant` and `Cookie.Clone`** are all
  internal. Request cookies are therefore not surfaced at all, `Request.Cookies` is always empty,
  and `Set-Cookie` serialization is reimplemented for the attributes the public surface exposes.

Neither matters for what this project is: an mTLS-authenticated API that carries no cookie state.
Both are recorded because a future reader will otherwise assume upstream behavior.

## Taking a newer upstream

1. Re-fetch the file list at the new commit.
2. Re-apply sections 1 and 3, which are scripted and uniform.
3. Re-apply section 2, one constructor.
4. Re-check section 4 against whatever the runtime now expects internally, which is where a
   surprise would appear.
5. Run the proof in `..`, which exercises the parser end to end over TLS 1.3 with a client
   certificate.
