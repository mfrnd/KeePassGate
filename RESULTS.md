# What the three proofs settled

Three HTTP implementations, the same harness, the same request, the same certificate authority.
Each is vendored, built on .NET Framework 4.8, driven with TLS 1.3 and mutual TLS, and asked to
parse one request. The point is to replace a judgment about which is cheaper to carry with a number.

Every number here was produced by running the code in this branch. Nothing is estimated.

## The numbers

| | files | bytes | **changed lines** | extra stub files | certificate reaches the handler | serves |
| --- | --- | --- | --- | --- | --- | --- |
| [`spikes/httplistener`](spikes/httplistener/) | 27 | 194 KB | **116** | 7 | yes, supplied by the port | yes |
| [`spikes/uhttpsharp`](spikes/uhttpsharp/) | 62 | 207 KB | **0** | 0 | yes, carried on our `IClient` | yes |
| [`spikes/wssharp`](spikes/wssharp/) | 76 | 922 KB | **0** | 0 | **no**, see below | with a workaround |

Files and bytes are the `.cs` under each `vendor/`, which is the vendored library and nothing
of the harness around it, with a thousand bytes to the KB. Changed lines exclude the namespace
rename where one was needed, and count only substantive edits.

## What each one needed

**`dotnet/runtime`'s managed `HttpListener`, 116 lines.** The most work, and foreseeable in
hindsight: it is **runtime-internal code**, so it reaches for what the runtime gives its own
libraries. `SR` had to be generated from upstream's `Strings.resx`, `NetEventSource` stubbed,
`LazyAsyncResult` restated, four shared helpers vendored separately, and modern APIs substituted in
a dozen places. Two things do not port at all, because the BCL keeps them internal: `CookieParser`
and three `Cookie` members, so request cookies are never surfaced. Its own TLS path is **dead
code** by upstream's comment, so injecting an authenticated stream is not a preference here, it is
the only option.

**uHttpSharp, 0 lines.** It already targets `v4.5`, so there was nothing to port, and it was
designed to be fed from something other than a socket: `IHttpListener` returns `IClient`, `IClient`
is four members and one of them is a `Stream`. Handing it an authenticated stream is two small
interfaces in our own code. The only file not carried is its `Properties/AssemblyInfo.cs`, which is
a project artifact rather than source.

**websocket-sharp, 0 lines, and a problem.** It compiles untouched and pins TLS 1.3 through
configuration rather than a patch, and then fails on the thing the whole design rests on:
`HttpListenerRequest.GetClientCertificate()` throws `NotSupportedException`. It will demand a
client certificate and validate it during the handshake, and never tell the handler who presented
it. The proof works around that by recording the certificate in the validation callback, which is
racy under concurrency, so this candidate is not recommended on what is measured so far.

## Two things the exercise turned up that arguing would not have

**Size predicted nothing.** The largest candidate needed ten lines and the smallest needed 116,
because what mattered was whether the code was written to stand alone rather than how much of it
there was.

**A proven parser is not the same parser.** The same hand-written request was parsed happily by two
of them and rejected by the third until a conforming client was used. Strictness is a behavior
difference, not a quality difference, and it only shows up when something drives the thing.

## What is not settled

- Whether websocket-sharp's certificate problem is really fatal, or only unexposed. It is being
  read through the facade the library advertises, and nobody has looked for another way in.
- Whether either survivor supports spec-first routing, which is how this API is meant to be
  described. Nothing here has tried it.
