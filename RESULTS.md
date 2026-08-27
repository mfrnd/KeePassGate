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

## The corrections

**uHttpSharp was called the decisive winner on spec-first binding, and it is not.** That judged
websocket-sharp by `HttpServer`, its event-based facade, without looking for another way in. There
is one: `WebSocketSharp.Net.HttpListener` exposes `Prefixes`, `SslConfiguration` and `GetContext`,
so the accept loop and the pipeline can be ours, and the same router passes the same checks on it.

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

Both corrections came from looking for a second way in rather than from taking the first one at its
word, which is the habit worth keeping.

## Two things the exercise turned up that arguing would not have

**Size predicted nothing.** The largest candidate needed ten lines and the smallest needed 116,
because what mattered was whether the code was written to stand alone rather than how much of it
there was.

**A proven parser is not the same parser.** The same hand-written request was parsed happily by two
of them and rejected by the third until a conforming client was used. Strictness is a behavior
difference, not a quality difference, and it only shows up when something drives the thing.

## Spec-first routing

Which of the two supports OpenAPI spec-first binding of the sort Connexion does in Python, built
rather than argued. **Both do**, and the question does not decide the choice.

The router is about 200 lines and is the same on both: the document is the routing table, handlers
bind to `operationId` rather than to paths, a mismatch between declared operations and bound
handlers is a startup error instead of a 404 found later, path templates capture parameters, and
the body is validated against the declared schema before any handler runs. All of it verified over
TLS 1.3 with a client certificate.

The difference is 40 lines, measured rather than estimated: a per-request context to carry the
ceiling, a one-line delegate for a pipeline stage, and the accept loop. uHttpSharp provides all
three through `Handle(context, next)` and a per-request `State` bag, so the glue there is nothing.
Forty lines of our own code, written once and understood completely, is not a reason to choose a
library.

**Nothing in .NET does this off the shelf.** Swashbuckle and NSwag are code-first or build-time
generators; runtime spec-driven routing has no mainstream .NET equivalent, so it is custom work on
any foundation. That argues for the foundation that makes it smallest.

The tooling exists on .NET Framework, which was not obvious: `Microsoft.OpenApi` and its readers
target netstandard2.0, and `NJsonSchema` targets net462 directly.

## So what does decide it

Not the transport, and not spec-first binding, since all three do the first and both survivors do
the second. What is left is a judgment rather than a measurement:

- **uHttpSharp**: zero divergence, 207 KB, and the seams it needs already exist. Against it, 112
  stars and nothing since 2021.
- **websocket-sharp**: ten lines of divergence, 40 lines of glue, and 922 KB including a WebSocket
  implementation the design does not want. For it, 6000 stars and commits that month.

The smallest thing to own, or the one most likely to be fixed by somebody else.

## What the mainline did with it

**It took none of them, and that is not these proofs failing.** Selecting a host certificate by SNI
means owning the accept loop, because .NET Framework exposes no server-side SNI hook, and once the
accept loop and the TLS are ours a candidate is reduced to a parser fed from a stream. What was
left of the winner was small enough to write, and a dependency that small is not worth its license
and liveness risk.

So the measurements decided the question they were asked and then dissolved it, which is the useful
kind of answer. What survived onto the mainline:

- **uHttpSharp is the candidate to reopen**, recorded as such, if this project ever wants WebSocket,
  HTTP/2 or chunked bodies. It fits the seam natively with zero changes and is ruled out on license
  and liveness, not on fit.
- **Spec-first routing**, proven twice here, is how the mainline routes.
- **The transport is ours**, which is the shape the first proof was already testing when it drove a
  vendored parser from a stream it had authenticated itself.

## What the framework already does

The three proofs above settled that the transport is ours. That leaves a smaller question, and
answering it by reading documentation produces an opinion, so `spikes/BclReuseProbe.cs` runs it:
which pieces of a hand-written transport are already in an assembly the plugin loads anyway?

`WebHeaderCollection`, `ContentType` and `Uri` all ship in `System`, which is already referenced for
the sockets. Nothing below is a dependency question. Each one is a question about which behavior is
wanted.

### Take: the media type parser

```text
application/json; charset=utf-8  -> application/json
APPLICATION/JSON                 -> APPLICATION/JSON
nonsense                         -> REFUSED FormatException
application/json;;;              -> REFUSED FormatException
```

`System.Net.Mime.ContentType` strips parameters correctly and, more usefully, refuses a value that
is not a media type at all. Splitting on `;` by hand gets the first line right and reports the last
two as an ordinary wrong type, so a malformed header and a merely unsupported one become the same
answer. It does not fold case, which is right: a comparison still has to be case insensitive,
because RFC 9110 says media types are.

### Take: the header collection, for writing

```text
Bad Name             -> REFUSED ArgumentException
value carrying CRLF  -> REFUSED ArgumentException
```

It refuses a malformed name and a value carrying a line break, which is the grammar. It accepts
`Content-Length`, `Transfer-Encoding` and `Connection` without complaint, which is the policy, and
the policy has to be ours: the transport owns framing, so a caller that could set those could put
a stream it does not know exists out of step with itself. Same split as the JSON reading, where the grammar is
somebody else's and what this wire may carry is not.

### Leave: the header collection, for reading

```text
Add("A: b")       -> b
Add("A: c") again -> b,c
Add(" folded")    -> REFUSED ArgumentException
Add("no-colon")   -> REFUSED ArgumentException
```

It refuses the two malformed lines the reader here refuses, and then merges a repeated header into
one comma separated value. A repeated header is refused rather than merged on purpose, because
merging is how two intermediaries come to disagree about where a request ends. This does the one
thing the reader exists to prevent, so it stays on the writing side.

### Leave: the URI parser, and this is the sharpest of the three

```text
/entries/a%2Fb  AbsolutePath -> /entries/a%2Fb    LocalPath -> /entries/a/b
/entries/../x   AbsolutePath -> /x                LocalPath -> /x
/entries/%2E%2E AbsolutePath -> /entries/%2E%2E   LocalPath -> /entries/..
```

Two behaviors, each disqualifying. `LocalPath` decodes `%2F` into a separator, so one segment
becomes two and an attachment named `a/b` stops being addressable, which is the whole reason the
path is split before it is decoded. And `/entries/../x` normalizes to `/x` in both: the climb a
reader is written to refuse simply disappears, and the path a router matched is no longer the path
the caller sent. A refusal that cannot fire is worse than no refusal, because the tests pass.

### What the mainline took

`ContentType`, for the one place a request's media type is checked. The header collection for
responses, with an allow list of the names this API may vary, so that framing stays where the
framing is written. Nothing else: the percent decoding, the request reader and the path matching
stay hand written, and each now has a measurement behind why rather than an assertion.
