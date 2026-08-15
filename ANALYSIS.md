# KeePassGate: the argument, before anything was built

> **This is the argument that was made, not what to build.** Several of its conclusions were
> reversed by later measurement, and the reversals are recorded where they happened rather than
> edited into this text, because the reasoning is the point of keeping it. These matter enough to
> warn about at the top:
>
> - **Certificates no longer name a database, and there is no `any` wildcard.** Section 4 argues for
>   `kpg://<uuid>/<profile>` with `any` reserved. The authority later moved inside each database, so
>   the issuer identifies it and a claim carries only `kpg://profile/<name>`. The wildcard this
>   document recommends is the escalation [`CA.md`](CA.md) §209 now exists to close. Do not
>   build from section 4c or 4d.
> - **There is no local registry.** Sections 5 and 8 describe admission as a list of fingerprints
>   beside the plugin. Admission records now live in the database, keyed by SHA-256 over the
>   certificate DER, and are required on every connection. See [`CA.md`](CA.md) §204 and §217.
> - **No HTTP dependency is taken.** See [`DESIGN.md`](DESIGN.md) §107.
> - **A certificate carries no ceiling.** Section 4a settles that a `kpg://` entry bounds what a
>   client may ask for, and section 8 counts that among the decisions taken. The admission record
>   is the only ceiling instead, and nothing reads a claim: a certificate whose record is missing
>   grants nothing, which is what makes a lost record fail closed. See [`CA.md`](CA.md) §212.
> - **There is no option to trust an outside authority.** Section 5 recommends designing for one.
>   Every database is its own authority instead, and a connection is refused unless its chain ends
>   in that authority's own bytes, which is exactly the door an outside authority would open. See
>   [`CA.md`](CA.md) §202 and §214.
>
> [`DESIGN.md`](DESIGN.md), [`CA.md`](CA.md) and [`PAIRING.md`](PAIRING.md) are current.

An analysis of the proposal, written before anything is built. It exists to test the idea rather
than to plan it, so it argues with the proposal in several places. The conclusion is that the
instinct behind it is correct and better founded than it might feel, that two of its four pillars
should be adopted close to as proposed, and that the third, **authorization carried in certificate
metadata**, should be adopted only in a weaker form than proposed, for a reason that matters.

Conventions: American English, no dashes as punctuation, and `code` in backticks. Claims are
marked **verified** where they were measured or executed on this machine, and everything else
should be read as reasoning that has not yet been tested.

---

## 1. The premise, tested first

The premise is that a KeePass plugin can serve a small, specification-defined API over mutual TLS,
in process, on .NET Framework, and that the authorization model can live inside the database it
governs. Every part of that was doubtful enough to test before anything was designed on top of it.

Four doubts were worth resolving before designing anything around them:

- **Can a plugin serve TLS at all without leaving a key on the disk?** KeePass has no notion of a
  listener, the platform's own TLS may refuse a key that exists only in memory, and a password
  manager that writes its host key into the user's profile has given something away for nothing.
- **Can the authorization model live inside the file it governs?** `CustomData` on groups and
  entries is the only place a policy can travel with its database, and it requires KDBX 4.
- **Can a specification drive the routing rather than describe it afterwards?** An OpenAPI document
  is only worth carrying if an undeclared path has nowhere to go, and .NET Framework has none of the
  tooling that makes that automatic.
- **Can any of this be tested without a human?** A plugin whose security properties can only be
  exercised by clicking is a plugin whose security properties are asserted rather than checked.

The sections below work through them. None turned out to be blocking, but two changed the design
substantially, and the first changed it twice.

### What the premise does not justify

The premise is about the protocol, and the protocol is the smaller half. It says nothing about the
parts that look the same whatever the transport is: an access control model, a resolver, a grant
editor, a database settings page, an audit log, and a full-entry API. Those are the larger half by
volume and by far the larger half by iteration count, and the grant editor alone will take more
attempts to get right than the TLS will.

A from-scratch plugin is therefore not a small project. Its hard half is the part nobody argues
about.

---

## 2. What a new plugin does not escape

The proposal's appeal is partly that a clean start escapes the constraints. Most of them are
KeePass's, so they apply to anything hosted in it, and they follow.

- **In-process, single UI thread.** The plugin runs inside the KeePass process. A deadlock or
  unhandled exception takes the user's password manager with it, so every design rule that follows
  from that applies here whatever the protocol is.
- **Grants live in the kdbx.** `CustomData` on groups and entries, which requires KDBX 4.0. That is
  a property of KeePass's file format and of wanting policy to travel with the database.
- **The local attacker is undefeated, and mTLS does not change this.** Anything running as the
  user defeats subject separation, because whatever protects a client's key protects it against
  other users rather than against the user's own processes. A client certificate's private key sits
  in exactly that position. mTLS changes the authentication mechanism; it does not move that
  boundary by one inch. Any writing about KeePassGate should say so early, or it will imply a
  security gain it does not deliver.
- **The plugin scaffolding has to be built.** Options page, dialog tabs, start-up wiring, KeePass
  version checks, packaging. None of it is hard, and all of it is work.

### The one constraint a new plugin can escape

`LangVersion 5`. **Verified** on this machine: KeePass compiles a `.plgx` with the in-box CodeDOM
provider, which accepts C# 5 and rejects C# 6 string interpolation with "unexpected character '$'".
So a plugin shipped that way is written in C# 5 whether or not anybody chose to be.

A plugin distributed as a **plain DLL** compiled with Roslyn is not subject to it, and can use
C# 7.3, the highest .NET Framework supports. That is a genuine and cheap improvement: pattern
matching, `nameof`, interpolation, tuples, `using` declarations.

The cost is real but small. A `.plgx` is recompiled by each KeePass against its own assemblies,
which makes it resilient across KeePass versions; a DLL is compiled once and must be checked for
compatibility when KeePass changes. For a plugin with a handful of consumers, the DLL is the better
trade, and it removes a packaging step that can fail on a user's machine rather than on ours.

**Recommendation: distribute as a DLL, target .NET Framework 4.7.2 or 4.8, and write C# 7.3.**

---

## 3. mTLS: what was verified, and what it costs

This was tested rather than assumed, because the whole proposal rests on it.

### It works

**Verified** with a C# probe on .NET Framework 4.8, Windows 11:

```text
--- server auth only ---
  Tls12  negotiated Tls12  cipher=Aes256/256bit
  Tls13  negotiated Tls13  cipher=Aes256/256bit
  None   negotiated Tls13  cipher=Aes256/256bit
--- mutual TLS (client certificate required) ---
  Tls12  negotiated Tls12  clientCertSeen=CN=agent-fictitious
  Tls13  negotiated Tls13  clientCertSeen=CN=agent-fictitious
  None   negotiated Tls13  clientCertSeen=CN=agent-fictitious
```

`SslStream` over a `TcpListener` does mutual TLS, needs no administrator rights, and negotiates
TLS 1.3 when both ends can. Passing `SslProtocols.None` and letting the platform choose is the
correct configuration and produced TLS 1.3.

That is a password-authenticated key exchange, its arithmetic, its group negotiation and its
remote-connection rules all replaced by a platform primitive. It is the strongest part of the
proposal.

### HTTP over TLS does not work the easy way

**Verified**: an `HttpListener` with an `https://` prefix **starts** as a normal user and then fails
the moment a request arrives, because no certificate is bound to the port.

```text
Start() succeeded, now attempting a real request
request FAILED: An existing connection was forcibly closed by the remote host
```

Binding one requires `netsh http sslcert add`, which requires administrator. A KeePass plugin runs
as a normal user, so **`HttpListener` is not available for HTTPS** without an elevated installation
step, and an installation step that needs elevation is a step users skip.

### Why not just take a proven library

"Write your own HTTP parser" is a bad instinct in a process holding a password database, so the
alternatives were surveyed before accepting it. The result is not what it would have been five years
ago.

| Candidate | State | Usable here |
| --- | --- | --- |
| `HttpListener`, that is http.sys | In Windows | **No**, see below |
| EmbedIO | **Archived**, last release 2022 | No |
| NetCoreServer | Active, targets **net10.0** only | No |
| WatsonWebserver | Active, targets **netstandard2.1, net8.0, net10.0** | No, .NET Framework caps at netstandard2.0 |
| Kestrel, that is ASP.NET Core | Active, dropped .NET Framework after 2.x, itself out of support since 2021 | No |
| Nowin | Archived 2017 | No |

The pattern matters more than any single row. **The maintained embedded HTTP servers have all moved
to modern .NET.** What is left for .NET Framework is either archived or was never a security
proposition. So the real constraint is not "HTTP is hard", it is **in-process .NET Framework**, which
is where a KeePass plugin has to live.

### http.sys deserved a closer look, and it fails on our own decision

`HttpListener` is the interesting one, because http.sys is a genuinely proven HTTP parser, the same
one IIS sits on, and using it would move parsing out of this project entirely. It can even do mutual
TLS: `HttpListenerRequest.GetClientCertificate()` exists.

It fails for two reasons, and the second is fatal rather than annoying:

- The certificate binding needs `netsh http sslcert add`, which needs administrator. Survivable as a
  one-time elevated install step.
- **Verified**: `HttpListener` exposes no `SslProtocols`, no certificate property, and no protocol
  control of any kind. Version selection belongs to machine-wide Schannel policy.

That collides directly with section 3's decision. If the application cannot say "offer only TLS
1.3", then whether the client certificate crosses the wire encrypted depends on the machine's
registry rather than on this program, and the only way to force it is to disable TLS 1.2 for
everything on the host. Requiring that of a user in order to install a KeePass plugin is not
reasonable. **http.sys is therefore out, because it cannot keep the promise the design just made.**

### What that leaves

Two honest options, and a third that questions the premise.

1. **Hand-written HTTP/1.1 over `SslStream`, in process.** Pins the TLS version, no dependency, no
   elevation. The cost is owning a parser in the process that holds the database.
2. **A sidecar process on modern .NET.** This is the only way to reach any of the maintained
   libraries, for the reason in section 5b: they cannot be loaded into KeePass at all. Kestrel,
   NetCoreServer or WatsonWebserver all become available, all pin TLS per endpoint so the 1.3
   decision holds, and OpenAPI tooling becomes first-class. There is a security argument **for** it
   too, which should not be skipped: the HTTP parsing, the risky part, would no longer be inside the
   process holding the database.

   The costs are equally real. A second process to install, start, stop and clean up after a KeePass
   crash. A .NET runtime dependency, or a self-contained payload of tens of megabytes attached to
   what is meant to be a plugin. Plaintext secrets crossing a pipe and living briefly in a second
   process. And a **new trust boundary that has to be defended**: the plugin would be taking the
   client's identity on the sidecar's word, so the pipe needs an ACL restricted to the user and the
   plugin should verify the connecting process is the host it spawned, or the pipe becomes a simpler
   route to the database than stealing a key.

   The proportion is what decides it, and it is worth stating baldly. **The split is lopsided**:
   KeePass access, the ACL, the resolver, the editor, the audit log and the certificate authority
   all have to stay in process, which is around 4700 lines, while the sidecar would hold only
   transport. So the question is whether a second process, a runtime dependency and a new trust
   boundary are worth avoiding a few hundred lines of deliberately restricted parsing. For the API
   as scoped, they are not. If the API grows streaming, server-sent events, large attachment
   transfers or browser clients, they are, and the answer flips.
3. **Drop HTTP.** It is only required because OpenAPI was chosen. Newline-delimited JSON-RPC over the
   same mTLS socket needs no HTTP parser at all, and can still be described by JSON Schema. What is
   lost is the tooling: OpenAPI is what makes an agent able to consume the thing without bespoke
   work, and that was a stated goal.

### Terminating TLS ourselves and proxying to http.sys

The obvious middle path, and it deserves a straight answer because writing a parser does feel wrong.
The shape is: own the `SslStream` in process, so the TLS 1.3 pin and the client certificate stay
ours, then pump the decrypted bytes at a plain-HTTP `HttpListener` on loopback and let http.sys do
every bit of the parsing.

**It is available.** **Verified** unelevated, which was the open question, since the earlier probe
only tested the `https://` case:

```text
http://localhost:48242/     Start() OK, round trip OK
http://127.0.0.1:48243/     Start() OK, round trip OK
http://+:48234/             Start() FAILED: access denied
```

Only the wildcard prefix needs administrator. Loopback prefixes do not, so the proxy hop costs no
elevation and hands the whole of HTTP to the parser that IIS sits on.

**And it is still the wrong trade, for two reasons that are easy to miss.**

- **It opens a plaintext port that any local process can reach.** Everything the mTLS handshake
  established is in front of the proxy; behind it is unauthenticated HTTP on 127.0.0.1. Conveying
  the client's identity across that hop then needs either an injected header, which means parsing
  and rewriting the header block after all, or an out-of-band mapping from source port to
  certificate, which is fragile. The design that was supposed to remove parsing brings it back.
- **It creates two parsers in a chain, which is what request smuggling actually is.** Desync
  vulnerabilities are not caused by one parser being bad; they are caused by two parsers disagreeing
  about where a message ends. A single in-process reader cannot disagree with anything.

### Why writing it is less wrong than it feels

The instinct is calibrated for public web servers, where the parser is the front line against
unauthenticated internet traffic and where a decade of CVEs has taught everyone not to write one.
Three things make this situation different, and they are worth stating explicitly because they are
what the decision rests on:

1. **The parser sits behind mutual TLS.** Nothing reaches it without a certificate signed by this
   project's own CA. An attacker cannot deliver a single malformed byte to it without first
   obtaining a certificate, which is the thing the whole design is about controlling. Almost all of
   what makes HTTP parsers dangerous is pre-authentication exposure, and here there is none.
2. **There is exactly one parser**, so the entire smuggling and desync family is structurally absent
   rather than defended against.
3. **The grammar can be refused down to almost nothing**: require `Content-Length` and reject
   `Transfer-Encoding` outright, one request per connection with no keep-alive and no pipelining, a
   bounded header count and size, no compression, no multipart, and a total request timeout. What is
   left is a request line, a header block and a byte count.

That is a few hundred lines with no ambiguity in it, reachable only by authenticated clients, and it
should be fuzzed, which is easy for something with this shape.

### Putting the server logic in WebAssembly

The most interesting of the alternatives, because it is the only one that proposes to make writing
the parser *safe* rather than to avoid writing it. The shape: host a WASM runtime in the plugin, put
the HTTP and routing logic inside the sandbox, and import a small set of KeePass operations into it
as the only things it can reach.

**It is technically possible**, which distinguishes it from the modern-library option. **Verified**:
`wasmtime` 44.0.0 declares `.NETStandard2.0` among its targets, so unlike NetCoreServer and
WatsonWebserver it could in principle be consumed by .NET Framework 4.7.2 and later. It does so by
P/Invoking a native `libwasmtime`.

It should still be rejected, for four reasons that compound.

**1. The thing it protects against is already prevented.** The reason not to hand-write an HTTP
parser is the historical record of memory-safety bugs in parsers turning into code execution. A
parser written in C# cannot do that: a bug yields a wrong parse or an exception, not control of the
process. WASM's central value is containing code that is not memory-safe, so wrapping memory-safe
code in it is close to redundant. The logic bugs that remain, a header miscounted or a length
misread, are exactly the bugs the sandbox does not prevent.

**2. The version that does buy something costs more than it saves.** Compiling a battle-tested
parser such as `httparse` or `picohttpparser` to WASM would get both real-world proving and
containment of its unsafety, and that is a coherent design. It also means a Rust or C toolchain in
the build, a native runtime binary of tens of megabytes shipped beside a KeePass plugin, and a
hand-written marshaling layer over the module's linear memory. That last part deserves attention:
moving requests and responses across the boundary is pointer-and-length code, which is the precise
category where off-by-one and truncation bugs live. It would be written to avoid writing parsing
code, and it is not obviously the safer of the two.

**3. The boundary would be in the wrong place.** If the module routes and calls imported functions
to reach KeePass, those imports must enforce the ACL, so the host remains the authorization boundary
exactly as it is now. What the sandbox would contain is code this project wrote and trusts. Sandboxes
earn their keep around code that is *not* trusted, and there is none here.

**4. If isolation is the goal, a process is a stronger boundary than a sandbox.** An escape from a
WASM runtime hosted inside the plugin lands directly in the KeePass process, holding an unlocked
database. An escape from the sidecar lands in a separate process that must then attack an ACL'd
pipe. So on the very axis WASM is being proposed for, the sidecar already dominates it, and without
a native dependency.

There is a fifth, smaller point: a native runtime inside the KeePass UI process brings native crash
modes into the process whose stability this project is supposed to protect.

Worth separating: "not a WASM-hosted plugin" is a decision about where the whole plugin lives, and
this section is about sandboxing a parser. The two arguments are not the same one.

**When this idea would become right:** if KeePassGate ever wanted to run endpoint handlers it did
not write, whether user-supplied or third-party, WASM stops being redundant and becomes the correct
tool, because then there genuinely is untrusted code and the capability import list becomes the
point rather than an accident. Nothing in the current design calls for that, and if it ever does,
this section is the place to start.

### Vendoring a proven implementation and feeding it our stream

This is the option that changes the recommendation, and it is worth saying plainly that it is better
than the hand-written subset argued for above. Every earlier objection to a library was about
**packaging**: target frameworks, runtimes, native dependencies. None of those apply to source.

The candidate is **.NET's own managed `HttpListener`**, the implementation that runs on Linux and
macOS where http.sys does not exist. It lives in `dotnet/runtime` under
`src/libraries/System.Net.HttpListener/src/System/Net/Managed`, is MIT licensed, and is about 136 KB
of C# across seventeen files.

Three things were checked, and all three came out favorably:

- **It is already stream-based.** `HttpConnection` holds a `private readonly Stream _stream` and
  builds it from either a `NetworkStream` or an `SslStream`. Feeding it a stream we already
  authenticated is a change to one constructor, not a redesign.
- **It already does client certificates.** `HttpConnection` exposes `ClientCertificate`,
  `ClientCertificateErrors` and `SslStream`, so the identity plumbing this design needs is present
  rather than bolted on.
- **It has no exotic dependencies.** Its imports are `System.Diagnostics`, `System.IO`,
  `System.Net.Security`, `System.Net.Sockets`, `System.Security.Authentication`,
  `System.Security.Cryptography.X509Certificates`, `System.Text` and `System.Threading`. No
  `Interop`, no `Span<T>`, no `System.Buffers`. All of it exists on .NET Framework.

The porting work is therefore mechanical rather than exploratory: raise `LangVersion` to 8 or above,
which .NET Framework tolerates because nullable annotations are compile-time only, supply the handful
of `System.Diagnostics.CodeAnalysis` attributes the framework lacks, stub the resource strings and
tracing hooks the runtime provides internally, and replace socket construction with an injected
stream and a known client certificate.

**What this buys.** A parser exercised by every .NET process that has ever used `HttpListener` on a
non-Windows machine. In process, so there is one parser and no desync, no plaintext loopback port,
no second process, no native binary. And because it is **source**, CodeQL can scan it, which a
binary dependency never allows, which is the gap a shipped binary with no source leaves open.

**What it costs.** Ownership: vendored code gets no upstream fixes, so `dotnet/runtime` has to be
watched for changes to this component and fixes ported by hand. That obligation is real, though
bounded, since this is a stable component rather than one under active redesign.

### The vendoring rule: minimum divergence, restrict from outside

An earlier draft said to delete the unwanted features so that unsupported surface would be absent
rather than switched off. **That was wrong, for three reasons that outweigh it.**

- **Deleting is itself a dangerous edit, in the most dangerous place.** Removing chunked handling
  from an HTTP implementation means editing body-framing logic, which is exactly where smuggling and
  desync bugs are born. The edit carries more risk than the feature it removes.
- **It destroys the property being bought.** "Proven" applies to the code as it runs everywhere
  else. A copy with a few lines changed keeps most of that; a copy with a third of it cut out does
  not.
- **It makes the maintenance obligation unpayable.** A three-line diff against upstream can be
  re-applied to a newer version mechanically, and reviewed in a minute. A two-thousand-line deletion
  means every future upstream fix has to be manually judged against code that is no longer there,
  which is how vendored components quietly stop being updated.

So the rule is **minimum divergence**, and unwanted behavior is refused from **outside** the
vendored code rather than cut out of it:

1. **Modify only what cannot be done any other way**, and keep each change as a recorded patch:
   injecting the already-authenticated stream, renaming the namespace so it cannot be confused with
   the real `System.Net.HttpListener`, stubbing the internal resource strings and tracing hooks, and
   whatever is needed to compile on .NET Framework.
2. **Use the switches that already exist.** **Verified**: the managed listener exposes
   `TimeoutManager`, which covers slow-read defense without an edit, and WebSocket support is
   **opt-in**, since `AcceptWebSocketAsync` has to be called explicitly and is inert if it never is.
3. **Refuse the rest at our own boundary.** A request carrying `Transfer-Encoding` is rejected by our
   wrapper before its body is read, rather than by removing the decoder. Connection reuse is closed
   from the response side. The reachable surface shrinks just as much, and upstream stays upstream.
4. **Record provenance**: repository, commit, path, license and the patch list, so that re-vendoring
   a later version is a mechanical operation rather than an archaeological one.

### Is there a simpler thing to vendor?

Asked after the port was built, which is the right time, because the work of doing it revealed what
the cost actually consists of. The answer is that simpler options exist and that none of them is
clearly better, for a reason worth stating.

| Candidate | Size | License | Self-contained | Exercised by |
| --- | --- | --- | --- | --- |
| `dotnet/runtime` managed `HttpListener` | 152 KB, plus stubs | MIT | **no**, wants `SR`, `NetEventSource` and BCL internals | Microsoft, Mono lineage |
| `websocket-sharp` `Net/` | **428 KB** | MIT | **yes**, ships its own `Cookie`, `CookieCollection`, `HttpUtility` | 6000 stars, widely used through Unity |
| `uHttpSharp` | **48 KB** | LGPL-2.1 | mostly | 112 stars, untouched since 2021 |
| `NHttp` | 40 KB | **LGPL-3.0** | yes | archived 2017 |

Two of those are decided by facts rather than taste. **`NHttp` is out on license**: LGPL-3.0 cannot
be combined with GPLv2-only code, and a KeePass plugin is arguably derivative of a GPLv2
application. **`websocket-sharp` is not simpler**, it is nearly three times larger, precisely
because being self-contained means carrying reimplementations of BCL types.

`uHttpSharp` is the genuinely simpler one, at a third of the size, and its shape is better: an
`IClient` abstraction with a TCP adapter and an SSL decorator, so it is designed to be fed from
something other than a socket rather than needing a constructor patched in.

**The trade it asks for is the one to notice.** Vendoring means owning the code, and what matters
then is not only how much there is but how much of the world has run it. `uHttpSharp` is 112 stars
and has not been touched since 2021. Choosing it means an HTTP parser in a password manager's
process that is smaller, and also less exercised than the one it replaced. "Fewer lines" and "fewer
bugs" are not the same claim, and for this component the second is the one that matters.

There is a second reason to leave it, which the port itself supplies. **Divergence, not volume, is
what drives the maintenance cost**, and the measurement came out at 116 substantive lines. The work
that a simpler library would have saved, writing the stubs, is already spent, and it does not
recur: taking a newer upstream re-applies scripted changes and re-checks one section.

**Conclusion: keep the managed `HttpListener`.** The conditions that would change it are worth
recording rather than leaving to feel. Move to `uHttpSharp`, or reconsider the hand-written subset,
if taking an upstream update turns out to break the stubs repeatedly, since that is the cost that
recurs; or if the audit burden of 152 KB proves worse in practice than the smaller surface of a
library that does less.

One thing the port has changed is the standing of the hand-written option. It is now measurable
rather than speculative: the proof shows exactly what a request has to yield, which is a method, a
target, a version, a header collection and a bounded body. That is the fallback, and it is better
understood than when it was first proposed.

**Recommendation, revised: vendor the managed `HttpListener` with as few changes as it will
tolerate, and feed it the `SslStream` we authenticated ourselves.** The TLS decision survives, because the handshake stays
ours and the 1.3 pin is applied before the vendored code sees a byte. The interface stays, because
it is what keeps the sidecar available if this proves unpleasant.

The hand-written subset from the previous round is now the fallback rather than the plan, and the
reasoning that justified it still holds: whichever is chosen, the parser sits behind mutual TLS and
there is exactly one of it. The reasoning is that the surface is genuinely small, and that the
dangerous parts of HTTP are exactly the parts an OpenAPI-defined JSON API on loopback does not need
and can refuse outright. If the parser stops being small, that is the signal to take option 2, and
the interface is what makes that a replacement rather than a rewrite.

The cost of option 1 should be stated plainly rather than minimized: header injection, request
smuggling through disagreeing length headers, chunked-encoding edge cases and slow-read denial of
service all become this project's problems.

**Mitigation, and it should be written into the spec rather than left to discipline:** implement a
deliberately minimal HTTP/1.1 subset. One connection per request or strictly bounded keep-alive, no
chunked request bodies, a hard cap on header count and sizes, no compression, no redirects, exactly
the methods the OpenAPI document declares, and a total request timeout. A server that refuses
everything it does not need is much easier to argue about than one that is generally correct.

### Require TLS 1.3, and offer nothing else

The version matters more than it looks, because it decides what a certificate may safely contain:

- **In TLS 1.3 the client certificate is encrypted.** It is sent after handshake keys exist.
- **In TLS 1.2 the client certificate is sent in the clear.** Any observer of the connection sees
  all of it.

An earlier draft treated this as a caveat and concluded that certificates must therefore carry
nothing sensitive. That was the wrong conclusion, because the version is a choice rather than a
condition. **KeePassGate should require TLS 1.3 and offer nothing older.**

The cost of that used to be the objection and no longer is. Microsoft's Schannel table is explicit:
"TLS 1.3 is supported starting in Windows 11 and Windows Server 2022", and every Windows 10 build
including 22H2 is listed as **not supported**, with the note that enabling it on earlier versions
"is not a safe system configuration". Windows 10 reached end of support in October 2025. So
requiring TLS 1.3 excludes only Windows versions that are themselves out of support, which is a
line this project should be drawing anyway.

**One implementation detail decides whether this works, and it is easy to get wrong.** The
requirement must be expressed by **offering only TLS 1.3 in the handshake**, that is by pinning
`SslProtocols.Tls13` on the listener. It must not be expressed as negotiating freely and then
checking `SslStream.SslProtocol` afterwards and dropping the connection, which is the more usual
shape and is useless here: by the time that property can be read, the handshake has finished and
**the client certificate has already been transmitted**, in the clear, under whatever version was
agreed. Checking afterwards detects the leak; refusing to offer prevents it.

The trade is that pinning a version means a future TLS 1.4 needs a code change. That is the correct
way round for this project, and it should be a single named constant with this paragraph beside it.

Note also that this requires targeting **.NET Framework 4.8**, since `SslProtocols.Tls13` does not
exist in earlier versions.

---

## 4. Authorization in certificate metadata: adopt, but weaker

This is the part of the proposal to argue with. The idea is that a client's profiles and its
permitted databases are read from fields in its certificate, so that client management becomes
certificate management. It is coherent, it has real precedent, and as stated it has four problems.

### 4a. Authority moves from the database to the issuer

The access control model proposed here is deliberately built so that **the database owns policy**.
Rules name profiles, profiles are defined per database, and the database's own records say which
clients are in them. A client asserts nothing; it is told what it is.

If the certificate asserts the profile, that inverts: the CA decides, and the database must accept
the CA's word. The consequence is concrete. **Narrowing a client's access by editing the database is no
longer possible.** A certificate has to be reissued, and if the CA is not to hand, that cannot be
done at all. For a password manager, where the database is the asset and the person at the keyboard owns it,
that is the wrong way round.

**Agreed: intersect, do not replace.** The certificate asserts a **ceiling**, and the database's ACL
narrows within it. A call is permitted only if both allow it. This keeps everything the proposal
wants, because a certificate still cannot be widened by its holder and still scopes a client at
issuance, and it keeps the property that matters, because the owner of a database can always take
access away locally and immediately without touching PKI. It also keeps the ACL model whole, which
matters because that model is the larger half of the work.

This was the one objection with real force, and it is settled: **the certificate carries the
ceiling, and authorization is decided in the database.** Everything below follows from that, and in
particular `kpg://<uuid>/<profile>` is read as the outer bound of what a client may ask for, never
as a grant.

### 4b. Certificates are immutable and authorization is not

Every change of mind becomes a reissue. Adding a database to a client's remit, moving a client
between profiles, or narrowing one profile all mean generating, signing, distributing and installing
a new certificate, and revoking the old one. Today each is a dropdown in a dialog.

With the intersection model above this problem mostly dissolves, because day-to-day narrowing
happens in the database and only genuine identity changes touch PKI. Without it, the friction lands
on exactly the operation that should be easiest: taking access away.

### 4c. Database identifiers in a certificate: the confidentiality objection falls, a harder one remains

An earlier draft objected to putting database identifiers in certificates on two grounds. Requiring
TLS 1.3 answers the first one, and it should be conceded plainly rather than defended.

**The confidentiality objection is withdrawn.** With TLS 1.3 required and nothing older offered, the
client certificate is encrypted during the handshake and never crosses the wire in the clear. The
residue is small and worth stating once: the certificate is still a file on the client, still sits in
whatever the issuer records, and is still shown to any server the client is pointed at, so a client
misdirected to a hostile endpoint reveals which databases it may reach. Against that, an identifier
is 16 opaque bytes. It names nothing, reveals no titles, no layout and no contents, and the earlier
draft weighted it too heavily.

**The operational objection stands, and investigation made it worse rather than better.**

> **KeePass has no database UUID.** **Verified** against KeePass 2.61.1: `PwDatabase` exposes
> `DataCipherUuid` and `RecycleBinUuid`, and no identity of its own. The only stable per-file
> identifier is `PwDatabase.RootGroup.Uuid`.

So `kpg://<uuid>/...` cannot mean "this kdbx". It can only mean "the database whose root group is
this", which is a subtly different thing and carries a consequence: the root group's UUID is a
property of a **file's contents**, not of the file. Create a fresh database and import into it, which
is the normal way people migrate or repair one, and the identifier changes while the user's sense of
"my database" does not. Every certificate naming it silently stops matching, and the failure mode is
a client that authenticates perfectly and is then refused everything.

That is survivable, and it must be designed for rather than discovered:

- **Say what it means.** The specification should say root group UUID, not "database UUID", because
  the latter does not exist and implies a stability it does not have.
- **Make the failure legible.** A certificate naming a root group no open database has should produce
  a distinct, audited refusal, not a silent empty intersection. "Your certificate is for a database
  that is not open" and "you have no rights here" are different sentences and the user needs the
  right one.
- ~~**Offer a wildcard.**~~ **Do not.** This document argued for `kpg://any/<profile>` so that a
  single-database user need not name anything. That is the escalation the final design exists to
  close: profile names are per database, so a client meant to be trivial in one gains whatever a
  differently configured database grants the same name. The problem it was solving disappeared
  instead, because one authority per database means a claim never names a database at all.

With those three, naming databases in the certificate is a reasonable design and the proposal's
instinct holds.

### 4d. `OU` is the wrong field

`OU` is a free-text component of the subject name with no defined multiplicity semantics, and
identity semantics have been moving out of the subject DN and into `subjectAltName` for years.
Parsing multiple `OU` values means depending on ordering and encoding decisions that different
tools make differently.

**Recommendation: SAN URI entries with a scheme this project defines**, in the style SPIFFE uses.
The scheme survived; the shape did not.

This section argued that `kpg://<uuid>/<profile>` beat the profile-only form because it binds the
two together, a profile *in that database* rather than one meaning whatever each database decides.
The reasoning was sound and the premise stopped holding: once each database carries its own
authority, a certificate is already bound to one database by whoever signed it, and repeating that
inside the claim only creates a second place to get it wrong. **The profile-only form was adopted
after all**, for a reason this section could not have known.

```text
URI:kpg://profile/ops          <- as built
URI:kpg://profile/readonly
```

**Verified** that .NET Framework can emit these: `SubjectAlternativeNameBuilder.AddUri` exists, so
no third-party library is needed to issue them.

Two representation traps were measured, and both must be pinned in the specification, because each
silently produces a certificate that authenticates and then matches nothing:

- **.NET lowercases a URI host.** `[System.Uri]'kpg://A1B2C3D4-.../ops'` returns a `Host` of
  `a1b2c3d4-...`. Anything comparing the parsed host against an unnormalized string will not match.
- **KeePass does not format a UUID the way people write one.** `PwUuid.ToHexString()` returns **32
  uppercase hex characters with no hyphens**, for example `7656ADC24325684AA3CA8912185C0791`, not
  the dashed `8-4-4-4-12` form.

So the canonical form should be stated once and normalized on both sides: **32 hex characters, no
hyphens, lowercase**, with comparison done on the normalized value rather than on the string as it
appears. The wildcard `any` is safe alongside it precisely because it cannot be confused with 32 hex
characters.

Whatever is chosen must be written down as a certificate profile document before any code reads a
certificate, including what happens when a certificate names a profile the database has never heard
of. The answer should be that it is ignored, because the intersection is empty, and that should fall
out of the design rather than be a special case.

---

## 5. Revocation, and the claim that management is offloaded

The proposal asks for a standard way to revoke, and this is the point where the framing needs
correcting.

The options, honestly assessed:

- **CRL.** Requires a distribution point the plugin can reach, and a refresh policy. Introduces a
  window in which a revoked client still works. Offline hosts make it worse.
- **OCSP.** Requires a responder service. For a plugin that talks to clients on the same machine,
  standing up an OCSP responder is disproportionate by a wide margin.
- **Short-lived certificates.** The modern answer, and revocation becomes "stop renewing". It
  requires an always-available issuer, which a laptop that closes its lid is not, and it turns every
  client into something that must renew or die.
- **A local list of accepted certificates, by fingerprint.** Immediate, offline, no infrastructure,
  and revocation is a delete.

The last is the right default here, and admitting why is what corrects the framing:

> **If a local registry is needed for revocation, client management has not been offloaded to
> certificate management.**

What mTLS genuinely offloads is **authentication**: no pairing protocol, no SRP, no shared secret,
no custom crypto. That is a large and real win. **Authorization and revocation stay in the
database**, because they must be changeable by the person at the keyboard, immediately and while
offline. This section originally said "stay local", meaning a store beside the plugin; the answer to
the objection is unchanged, but the store is the kdbx.

The honest description of the resulting design is therefore three layers, each owned by whoever
should own it. **As finally built** they are: a certificate proves who a client is, the **admission
record inside the database** admits it and carries the profiles the operator approved, and the
database's ACL decides what those profiles reach. This section originally placed the second layer in
a local registry beside the plugin and had the certificate carry the ceiling; both moved into the
database, for reasons [`CA.md`](CA.md) §202 and §204 give. That is a better design than the
proposal as stated, and it is only slightly more machinery.

### Who runs the CA

Unresolved in the proposal, and it decides the entire user experience.

- **An external CA** (`step-ca` or similar) suits a multi-host setup and brings real tooling, at the
  cost of the user running a CA: protecting its key, issuing, renewing.
- **The plugin as its own CA** keeps everything local. Issuing a client certificate becomes a
  KeePass dialog, and the CA key sits beside the database.

The second deserves attention, because it is what the proposal turns into when followed through:
**"pair a client" becomes "issue it a certificate"**. That is the same user gesture as today, with
standard crypto underneath and no bespoke pairing protocol. It also means the CA key is exactly as
exposed as everything else on the host, which the threat model already concedes and should say
again.

**Recommendation: the plugin is the CA by default, with an option to trust an external one.** Design
for the second, ship the first.

---

## 5a. Platform, KeePass SDK and dependencies

What to build against, given the decisions above.

### There is no KeePass SDK to take a newer version of

Worth saying because it sounds like there should be. KeePass 2.x ships no separate library: the
portable archive is `KeePass.exe` plus native helpers and an `XmlSerializers` assembly, and
`KeePassLib` lives **inside the executable**. A plugin references `KeePass.exe` itself, which is why
this project builds against `%USERPROFILE%\KeePassDEV\KeePass.exe`.

"Use the latest" therefore means "build against the newest KeePass and set the minimum accordingly".
**Verified**: the newest stable release is **2.61.1**, which is also what is installed here, so the
reference is already current and there is nothing newer to move to.

Setting a minimum is the real decision, and it should be set high. KeePassGate has no history and
no installed base, so it should demand a recent KeePass and say so at load, rather than carrying
compatibility with versions nobody will run it on.

### Target .NET Framework 4.8, and write C# 7.3

Three things force the floor upward, and they agree:

- `SslProtocols.Tls13` exists only in **4.8**, and section 3 requires it.
- `CertificateRequest` and `SubjectAlternativeNameBuilder` need **4.7.2**.
- `LangVersion 5` is imposed by `.plgx` packaging, and DLL distribution lifts it to **C# 7.3**.

So: DLL distribution, .NET Framework 4.8, C# 7.3.

### Could the plugin simply target a modern .NET and use those libraries?

The obvious question once the survey lands, and the answer is no, for a reason that is structural
rather than a matter of effort.

**A process has exactly one runtime.** KeePass 2.61.1 is a .NET Framework application: its
configuration declares `supportedRuntime v4.0` and `v2.0.50727`, and it runs on CLR 4.0.30319. A
plugin is loaded into that process, so it must be an assembly that CLR can load. `net8.0` and
`net10.0` assemblies need CoreCLR, which is a different runtime, and `netstandard2.1` excludes .NET
Framework by definition, since .NET Framework stopped at `netstandard2.0` and will not move.

So the two candidates are not near misses that a version bump would fix:

- **NetCoreServer** has never published a .NET Framework-consumable target. **Verified** across its
  history: 5.0.0 is `net5.0`, 6.0.0 is `net6.0`, 7.0.0 is `net7.0`, 8.0.7 is `net8.0`.
- **WatsonWebserver** is `netstandard2.1;net8.0;net10.0`, and `netstandard2.1` is precisely the line
  .NET Framework cannot cross.

There is no version of either that loads into KeePass, and there will not be one.

**Modern .NET is therefore available to this project only in a separate process**, which is the
sidecar in section 3. That is not a reason to dismiss the idea, and it is a reason to be honest
about what the idea is: choosing modern .NET means choosing a second process, not choosing a
compiler flag.

One thing would change this entirely, and it is worth watching rather than assuming: if KeePass
itself ever moved to modern .NET, every constraint in this section would lift at once. Nothing here
should be built in a way that makes that migration harder, which is another argument for keeping the
transport behind an interface.

### The certificate work needs no third-party library

The most useful finding of this round, because it removes an obvious dependency before it is added.
**Verified** on .NET Framework 4.8:

```text
CertificateRequest.Create(X509Certificate2, DateTimeOffset, DateTimeOffset, Byte[])
CertificateRequest.Create(X500DistinguishedName, X509SignatureGenerator, DateTimeOffset, DateTimeOffset, Byte[])
SubjectAlternativeNameBuilder.AddUri
```

The first overload signs with an issuing certificate, which is the whole of what a small CA does.
Together with `AddUri` for the `kpg://` entries, the platform covers issuing a CA certificate,
issuing client certificates from it, and stamping the profile claims into them. **BouncyCastle is not
required**, and adding it would put a large cryptography library inside the process holding the
user's database for no gain.

### What dependencies remain

DLL distribution allows shipping assemblies beside the plugin, so dependencies are possible. They
should still be resisted, because a shipped binary with no source in the repository is
something no scan can see.

The honest list is short:

- **JSON.** Unavoidable for an HTTP and OpenAPI design, and the choice is a count rather than a
  preference: one self-contained assembly against nine on this framework. The counts are on the
  `spikes/json-reading` branch.
- **HTTP.** Nothing to take. Section 3 established that the server is hand-written over `SslStream`,
  so this is code rather than a dependency.
- **Cryptography.** Nothing, per above.
- **Testing.** NUnit, which KeePass itself is testable against and which runs on .NET Framework.

## 6. OpenAPI-first

The least controversial pillar, and worth keeping, with one caveat.

The benefits are real for the intended consumers. A machine-readable contract gives generated
clients, request and response validation, and something an agent can be pointed at. Writing the
spec first also forces the authorization model to be explicit per operation, which is exactly where
an access control model earns its keep.

The caveat is tooling. The mature OpenAPI server tooling in .NET targets ASP.NET Core, which is not
available here. In practice the spec will be hand-written and the routing hand-rolled, with the spec
used to generate **clients** and to drive tests rather than the server. That is still worth it, but
it should be planned as "specification as contract and test oracle", not "specification as
implementation".

A test that replays the spec's declared operations against the running plugin, and a test that
checks every operation declares its required verb, are both cheap and would keep the document honest.

---

## 7. Coexistence, which is not optional

A KeePass user runs whatever plugins they like, and this one is always an **additional** plugin in a
shared process rather than the only thing there. Several things follow that a design sketch tends to
skip:

- **Another plugin may already hold a port.** Ours must be configurable and must fail civilly when
  the one it wants is taken.
- **Another plugin may add its own tabs.** If two plugins both add an "Access control" page to the
  entry and group dialogs, the user sees two, and they will not agree. This is the most likely
  source of real confusion, and the only defense is that ours is labeled unmistakably.
- **`CustomData` is one flat namespace shared by everything.** Writing under `KPG` and reading
  nothing else is what keeps this plugin from coupling its schema to anybody else's.
- **Two audit logs**, if something else keeps one, unless one is chosen deliberately.

**Recommendation: our own key, our own log, and no assumption that we are alone.** Coupling to
another plugin through a file format is the kind of decision that is easy to make and very hard to
undo.

---

## 8. Verdict

**The instinct is correct.** A protocol chosen freely costs a fraction of one inherited: no method
gate, no crypto negotiation, no access choice, no migration, because none of those concepts has
anything to express when there is one API generation. The proposal's two strongest pillars, **mTLS
instead of bespoke pairing crypto** and **a specification-first contract**, both survive scrutiny,
and mTLS was verified to work in this environment including TLS 1.3.

**One correction remains**, reduced from three: requiring TLS 1.3 answered the confidentiality one,
and the authority one has been settled by agreement.

1. **Say plainly that management is not fully offloaded.** mTLS offloads authentication. A local
   registry is still needed for admission and revocation, and that is the right place for it. This
   is a documentation correction rather than a design change, and it should reach the README before
   anyone forms the wrong expectation.

**Four decisions were taken during the review** and are recorded above rather than left open:

- **Certificates carry a ceiling and the database decides.** Settled. A `kpg://` entry is the outer
  bound of what a client may ask for, never a grant.

- **Require TLS 1.3 and offer nothing older**, by pinning the listener rather than checking after
  the handshake. Windows 10 is out of support and is the only thing this excludes. This withdraws
  the confidentiality objection to certificate contents.
- ~~**`kpg://<uuid>/<profile>` in SAN URIs**, with the identifier being the **root group** UUID,
  normalized to 32 lowercase hex characters, and with `any` reserved as a wildcard.~~
  **Reversed.** Claims carry profile names only, `kpg://profile/<name>`, because one authority per
  database means the issuer already identifies it. That removed the wildcard, and with it the
  escalation where a client trivial in one database gained whatever another granted the same name.
  The measured facts survive the reversal and are why no normalization is needed any more: KeePass
  has no database UUID, and `PwUuid.ToHexString()` returns 32 uppercase hex characters.
- **DLL distribution, .NET Framework 4.8, C# 7.3, KeePass 2.61.1**, no cryptography dependency and
  one JSON dependency at most.

**Two costs should be accepted with eyes open**, because they are the ones most likely to be
underestimated. The ACL model, the grant editor and the audit log are thousands of lines that
must exist before the plugin does anything a user would notice. And the HTTP layer has to be
**owned**, in one sense or another: no
maintained library packages for .NET Framework, and http.sys, the one proven parser still available
as a package, cannot pin a TLS version from the application and so cannot keep the promise this
design makes about certificate contents.

Owning it does not mean writing it. The answer arrived at is to **vendor the managed
`HttpListener` from `dotnet/runtime`**, which is MIT, already stream-based, already handles client
certificates, and depends on nothing .NET Framework lacks. That converts the cost from "write a
parser" into "carry and track someone else's", which is the better of the two trades and keeps
the code scannable, unlike a binary dependency. The obligation that comes with it is watching
upstream for fixes to that component, and it should be written into the repository rather than
remembered.

**Two free improvements are available**: distributing as a DLL rather than a `.plgx` lifts the C# 5
ceiling the packaging format imposes, and the whole certificate authority can be built on the
platform, with no cryptography library added to a process that holds the user's database.

### What to do next, in order

Nothing here justifies committing to the build yet. Three spikes would settle it, and each is small.

> **Spikes 1 and 2 were superseded before they were run.** The transport question was answered on
> the `spikes/http-lib-measurements` branch and then re-answered when selecting a host certificate by
> name forced us to own the accept loop, so no port is spiked at all. Issuance writes an admission
> record into the database rather than a fingerprint into a local registry. [`DESIGN.md`](DESIGN.md)
> §132 carries the build order that replaced these.

1. ~~**The vendored `HttpListener`, ported and fed our own `SslStream`, inside a running KeePass**~~,
   answering one route. What survived from this spike is its instinct: feeding a listener our own
   authenticated stream is the shape the transport actually took, and the reason no candidate was
   kept is license rather than feasibility.
2. ~~**Certificate issuance as a KeePass dialog**, producing a client certificate and adding its
   fingerprint to a local registry.~~ Replaced by build order step 5, which creates the authority
   inside the database and deliberately exercises the `GroupForm` snapshot hazard, because that is
   the thing most likely to invalidate the storage decision.
3. **Port `Acl/` unchanged** and resolve one call against it through the new transport. This tests
   the claim that the valuable half carries across, which is the assumption the whole plan rests on.

If all three land, the rest is work rather than risk. If the first is unpleasant, the transport
choice should be revisited before anything else is built, because everything else depends on it.
