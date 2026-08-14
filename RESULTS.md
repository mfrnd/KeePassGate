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
| [`spikes/wssharp`](spikes/wssharp/) | 76 | 922 KB | **10** | 0 | yes, once one empty method is filled in | yes |

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

**websocket-sharp, 10 lines.** It compiles untouched and pins TLS 1.3 through configuration rather
than a patch. The ten lines implement one method upstream left empty, and five of them are the
comment.

## The correction

**websocket-sharp was called disqualified, and it is not.** That verdict is above in the history of
this file, and it is worth more than the claim it replaced.
`HttpListenerRequest.GetClientCertificate()` does throw `NotSupportedException` upstream, and for a
design whose authorization reads `kpg://` claims out of the client certificate that looked fatal.
The certificate is not missing, only unexposed: `HttpConnection` keeps the authenticated stream and
publishes it as `public Stream Stream`, so the peer certificate is one cast away. Before the
correction the proof recorded the certificate in the validation callback instead, which is racy
under concurrency and is a workaround nobody should ship.

It also reorders the comparison. The managed `HttpListener`, ported here first, is now plainly the
most expensive of the three, because it is runtime-internal code and the other two are libraries
written to be used from outside.

## Two things the exercise turned up that arguing would not have

**Size predicted nothing.** The largest candidate needed ten lines and the smallest needed 116,
because what mattered was whether the code was written to stand alone rather than how much of it
there was.

**A proven parser is not the same parser.** The same hand-written request was parsed happily by two
of them and rejected by the third until a conforming client was used. Strictness is a behavior
difference, not a quality difference, and it only shows up when something drives the thing.

## Spec-first routing

Which of the two supports OpenAPI spec-first binding of the sort Connexion does in Python, built
rather than argued. The answer is uHttpSharp, decisively.

`spikes/uhttpsharp` now carries a working spec-first router in about 200 lines over the vendored
library: the document is the routing table, handlers bind to `operationId` rather than to paths, a
mismatch between declared operations and bound handlers is a startup error instead of a 404 found
later, path templates capture parameters, and the body is validated against the declared schema
before any handler runs. All of it verified over TLS 1.3 with a client certificate.

The reason is architectural. Spec-first binding is layered work, and uHttpSharp's
`Handle(context, next)` makes each layer one handler in a list, with a per-request `State` bag as
the natural carrier for the `kpg://` ceiling on its way from the handshake to the handler.
websocket-sharp offers `OnGet` and `OnPost` events and no context, which is what forced the earlier
certificate workaround into a field on the listener.

**Nothing in .NET does this off the shelf.** Swashbuckle and NSwag are code-first or build-time
generators; runtime spec-driven routing has no mainstream .NET equivalent, so it is custom work on
any foundation. That argues for the foundation that makes it smallest.

The tooling exists on .NET Framework, which was not obvious: `Microsoft.OpenApi` and its readers
target netstandard2.0, and `NJsonSchema` targets net462 directly.
