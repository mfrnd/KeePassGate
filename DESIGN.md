# KeePassGate: design

What to build, and why each part is shaped the way it is. Everything here rests on
[`FINDINGS.md`](FINDINGS.md), which records what was measured rather than assumed;
[`ANALYSIS.md`](ANALYSIS.md) carries the arguments and the rejected alternatives.

The last section, [Challenging this design](#challenging-this-design), attacks it. Read it before
believing the rest.

## 1. What this is, and is not §101

A KeePass 2.x plugin that serves a **small, specification-defined HTTP API over mutual TLS**, so
that automation and AI agents can be given a narrow, revocable, audited slice of a database.

It is not a general secret daemon, and it does not read a `.kdbx` of its own: it works only against
databases the user has already unlocked in KeePass.

**Non-goals, stated so they do not creep in**: no WebSocket surface, no cookies or sessions, no
browser clients, no server-side rendering, no multi-user access control, no network exposure beyond
what a tunnel provides.

## 2. Shape §102

```text
   client (agent)
        |  mutual TLS 1.3, rotating SNI names the database
        v
  +---------------------------------------------------------------+
  |  Gate                 ClientHello peeked, SNI resolved to a    |
  |                       database, BEFORE any TLS work            |
  +---------------------------------------------------------------+
  |  Transport            our accept loop, Bouncy Castle TLS 1.3   |
  |                       request parsing, bounded                 |
  +---------------------------------------------------------------+
        |  IGateContext  <-- the seam: everything above is replaceable
        v
  +---------------------------------------------------------------+
  |  Pipeline             identity -> spec router -> terminal 404  |
  |  SpecRouter           OpenAPI document is the routing table    |
  +---------------------------------------------------------------+
        |
        v
  +---------------------------------------------------------------+
  |  Authorization        record  INTERSECT  ACL, both in the kdbx |
  |  Audit                every decision, by entry UUID            |
  +---------------------------------------------------------------+
        |
        v
  +---------------------------------------------------------------+
  |  KeePass              PwDatabase, on the UI thread             |
  +---------------------------------------------------------------+
```

## 3. `IGateContext`, the seam §103

The measurements produced two working transports and a router that was identical on both. That is
the argument for naming the boundary rather than discovering it later.

```csharp
/// One request, as everything above the transport sees it.
public interface IGateContext
{
    string Method { get; }              // upper case, as sent
    string Path { get; }                // absolute path, query already split off
    IReadOnlyDictionary<string, string> Query { get; }
    string GetHeader(string name);      // case insensitive, null when absent
    string Body { get; }                // read once, bounded, never null for a bodied method

    ClientIdentity Client { get; }      // who, and the ceiling they carry
    IDictionary<string, object> Items { get; }   // per request, for pipeline stages

    void Respond(int status, string json);
    bool Answered { get; }
}
```

Three properties earn it its place:

- **The router, the authorization layer and the audit log never see the transport.** They compile
  against this interface, so replacing it is a new adapter and nothing else. Two adapters already
  existed in prototype, which is how the interface was designed rather than guessed, and the seam
  earned itself when the transport was then replaced for real.
- **`Body` is a string, read once, bounded.** The transport is responsible for enforcing the limit
  and for refusing chunked encoding, so no layer above has to think about framing.
- **`Client` is not optional, but it can be unauthenticated.** Pairing has to serve a stranger, so
  the transport produces a context whose `Client` is present and carries `IsPaired == false`. Only
  `/pair/*` accepts one, refused in a single place before dispatch rather than per handler, so a new
  route is denied by default rather than by remembering.

`ClientIdentity` is equally small:

```csharp
public sealed class ClientIdentity
{
    public bool IsPaired { get; }         // false only on the pairing path
    public string Subject { get; }        // certificate CN, for the audit log
    public string AdmissionKey { get; }   // SHA-256 over the certificate DER, lowercase hex
    public string DatabaseId { get; }     // which open database the SNI resolved to
    public IReadOnlyList<string> Profiles { get; }   // from the ADMISSION RECORD, not the SAN
}
```

## 4. Transport §104

**We own the accept loop and the TLS.** That reverses an earlier decision, and the premise is what
changed rather than the reasoning.

An earlier draft drove `WebSocketSharp.Net.HttpListener`, letting it accept, terminate TLS and parse
HTTP, and reached the client certificate by reflection. That worked and was verified. It stopped
working when the authority moved into the database, because **each open database has its own host
certificate** and the server must choose one before it sees the client certificate. The only
selector available that early is SNI, in the ClientHello.

`ServerSslConfiguration` carries a single `ServerCertificate`. It is read per connection, inside the
`HttpConnection` constructor, so it looks briefly like a seam:

```csharp
internal HttpConnection (Socket socket, EndPointListener listener) {
  ...
  sslStream.AuthenticateAsServer (sslConf.ServerCertificate, ...);
```

It is not one. `EndPointListener.onAccept` restarts `BeginAccept` before constructing the
connection, so accepts overlap and mutating a shared property between them is a race.

Owning the accept loop means owning its ceiling on concurrent connections, and that ceiling keeps
KeePass's thread pool out of a stranger's hands without defending the gate's availability. §110
says what that leaves open, and why it is conceded.

### What we do instead §105

| Step | Ours | Measured in |
| --- | --- | --- |
| Accept | `TcpListener` | |
| Peek the ClientHello, extract `server_name` | ~90 lines, bounds checked, total | `SniProbe` |
| Resolve the name to an open database | fixed time, no match drops pre-TLS | `HmacSniProbe` |
| Terminate TLS 1.3 through a prepending stream | `Org.BouncyCastle.Tls`, replaying the buffered hello | `ManagedTlsProbe` |
| Parse the request line and headers | see below | |
| Read the body | `Content-Length`, capped at 1 MB | |
| Write the response | status line, headers, body, close | |

Owning the stream has a consequence worth naming: **the client-certificate reflection bridge
disappears**. The hack the old transport needed existed only because something else owned the
stream, and it died the moment we took it, one step before the TLS itself stopped being the
platform's.

### Why the TLS is not the platform's §133

**Reversed, on a measurement.** The transport used `SslStream` until it turned out that SChannel
will not serve with a key that has not been written to the user's profile: an `EphemeralKeySet`
certificate is refused with `SEC_E_NO_CREDENTIALS`, as a server and as a client. Serving therefore
meant a host key on the disk for as long as a database was open, which contradicts the one thing
this design says about where an authority lives.

So `Org.BouncyCastle.Tls` terminates TLS instead. It parses the stored PKCS#12 itself, so the key is
an object on the heap and nothing else. Measured in `ManagedTlsProbe`: TLS 1.3 both ways, a client
certificate seen, an OpenSSL client interoperating, and no key file at any point.

| | |
| --- | --- |
| License | MIT, so a compiled release carries no copyleft obligation |
| Liveness | 2.7.0 released 2026-07-30, with releases every few months |
| Reach | targets `net461` directly; the C# port of the most widely deployed cryptography library in the Java world |
| Cost | 4.9 MB beside the plugin, and about 100 lines of `DefaultTlsServer` |

Three things come with it beyond the key. **The offer is decided here**, so TLS 1.3 stops depending
on the operating system's version rather than on our policy. **Whether a client certificate is asked
for is decided here**, so serving a stranger on the pairing path no longer depends on
`ClientCertificateRequired`, whose plain reading is the opposite of what it does. And the platform's
two measured traps stop applying at all.

What is given up: SChannel is more exercised than any managed stack, and a bug in this one is ours.
Against that, the two TLS defects this project has actually hit were both SChannel's.

### Why no HTTP dependency is taken §107

Three candidates were vendored and measured on the `spikes/http-lib-measurements` branch, taken from
`master` and never merged, before the transport changed. Owning TLS changes what we need from them:
not a server, only a parser that accepts an already authenticated stream. That reshuffles the
ranking, so it is worth being explicit rather than inheriting the old answer.

| | License | Reach | Fits the new seam | Cost now |
| --- | --- | --- | --- | --- |
| **uHttpSharp** | **LGPL-2.1** | 112 stars, untouched since 2021 | **natively.** `IClient` exposes a `Stream`; the port needed **0 changed lines** | a copyleft obligation, on an unmaintained tree |
| websocket-sharp | MIT | 6000 stars, widely used | only via `HttpRequest.ReadRequest`, an `internal static` | reflection into the shipped assembly, and it is the wrong parser |
| managed `HttpListener` | MIT | Microsoft | yes, but it needed **116** changed lines and 7 stub files | the most work of the three |

**The websocket-sharp row above said "submodule, from-source build, reflection" and that was
wrong.** Reflection into the released assembly reaches `HttpRequest.ReadRequest` on its own; the
submodule is needed only for the `HttpConnection` path, which builds its own `SslStream` and cannot
be reached any other way. Correcting it makes the option look cheaper, and the real reasons it is
refused are stronger than the price ever was:

- **It is the WebSocket handshake parser, not a server parser.** Its parse is about twenty lines:
  split the request line, take a substring for the version, set headers. No method token check, no
  target length check, and a request line whose third field is shorter than five characters throws
  out of that substring instead of yielding a 400.
- **The body read has no ceiling.** It reads whatever `Content-Length` says. The refusal table below
  promises the opposite, and `/pair/*` is reachable without a client certificate, so the peer that
  could drive that allocation is any process which completes TLS on the loopback port.
- **It accepts folded headers**, which this reader refuses deliberately.
- **It loses the distinction between failures.** Everything becomes one exception type, so
  malformed, timed out and disconnected arrive as the same thing.
- **It does not deliver `Expect: 100-continue`**, which lives only on the server path.

### The libraries that were looked at, and why they do not fit §134

The seam is "parse a request off a stream that is already authenticated", not "serve HTTP". That is
what rules most of them out before license is reached, and it is recorded so that the next person
asking does not repeat the search.

| | License | net48 | Fits the seam |
| --- | --- | --- | --- |
| EmbedIO | MIT | yes | **no.** Its listener is the Mono and websocket-sharp lineage, so it owns socket, accept loop and TLS |
| Watson.Lite | **unverified**; its predecessor `HttpServerLite` is MIT | stated on the package | **no.** Sits on `CavemanTcp` and does its own TLS from a supplied certificate |
| HttpMachine | MIT | original yes, the live fork is netstandard2.0 and later | **yes.** A state machine fed byte buffers, owning no socket |
| managed `HttpListener` | MIT | yes | yes, at 116 changed lines and 7 stub files |

HttpMachine is the only genuine match and still a bad trade: it would replace roughly 90 of the
lines in `HttpRequestReader`, the request line and the header block, and none of the target
splitting, the percent decoding, the token rules or the caps, in exchange for a generated state
machine unmaintained since about 2013. Harder to audit than what it replaces, on the component that
is the whole attack surface behind TLS.

### Keep-alive, and why the tunnel argues against it §135

Considered for the remote-access case and refused. What it would buy is amortizing a TLS 1.3
handshake, roughly two round trips including TCP, which matters for a chatty client, and this API is
deliberately not chatty.

Against it: middleboxes reap idle connections after thirty to a hundred and twenty seconds, so reuse
introduces "connection reset on reuse" as a new failure, and a `POST` that may already have been
received is not safe to retry, which connection-per-request never has to think about. A tunnel
usually wraps the stream again, so two idle policies would have to agree. Connections are served by
thread pool threads doing blocking reads inside the KeePass process, so N idle clients would hold N
of them. And `Connection: close` is what keeps message boundaries from mattering to a hand-written
parser.

**If handshake latency is ever measured to matter, the answer is TLS session resumption**, not
keep-alive, with two caveats: the ticket cache is flushed on both lock transitions for the same
reason every connection is closed there, and 0-RTT early data stays off because it is replayable.
Revocation is unaffected either way, since the admission record is read from the live database per
request rather than per connection. The thing that would genuinely force a held connection is
**push**, and both ways of doing that are already the documented trigger to reopen this question.

**uHttpSharp is the one built for this**, and an earlier draft of this section reached the opposite
conclusion by comparing only against websocket-sharp, whose costs are not uHttpSharp's. Its seam is
the intended one rather than a hole reflected into, and it compiles on net48 untouched.

What rules it out is not fit but **license and liveness**. It is LGPL-2.1, and this project chose MIT
deliberately so that a compiled release package carries no copyleft obligation. Taking it would mean
shipping it as a separate assembly, preserving the ability to relink, and never merging the plugin
into a single file. That is a real constraint on distribution, accepted in exchange for a header
parser, from a tree with no commits since 2021.

websocket-sharp keeps the license and the reach but has lost the reason it was chosen. It was
ranked first because it terminated TLS for us, configured through `SslConfiguration`. We terminate
TLS ourselves now, so that convenience is worth nothing, and what remains is reflection into an
`internal static` for a parser.

**Decision: the parser is ours.** That decision is unchanged by taking a TLS dependency: what ruled
uHttpSharp out was its license and its liveness, and Bouncy Castle fails neither test. A few
hundred lines, bounded the way websocket-sharp bounds its own: read to the CRLF CRLF that ends a head,
under a hard cap, reject `Transfer-Encoding`, cap the body by `Content-Length`,
never offer keep-alive or upgrade. That is a smaller and better understood liability than either
alternative, and it sits behind the gate and behind TLS, unlike the ClientHello parser we already
have to write.

**What would flip this.** Wanting WebSocket, HTTP/2 or chunked bodies makes writing it here a bad
trade immediately. If that happens, uHttpSharp is the candidate to reopen and the license question
is the one to answer first, because the fit was never in doubt.

### Refused at our boundary §110

| Refused | Where |
| --- | --- |
| An unresolvable SNI | the gate, before any TLS work |
| Any request with `Transfer-Encoding` | header parse, before the body is read |
| Bodies over 1 MB | by `Content-Length`, before reading |
| An `Expect` that is not `100-continue` | before the body, 417 |
| A header block over 8 KB | during the parse |
| WebSocket upgrade | never implemented |
| Keep-alive | responses always close |
| Anything the OpenAPI document does not declare | terminal stage, 404 |
| Any route but `/pair/*` without a client certificate and a matching admission record | pipeline, first stage |

The listener binds `https://127.0.0.1:<port>/` only. Nothing binds off loopback, by construction;
reaching it from elsewhere is a tunnel's job. Binding off loopback would put a password
manager's API on a network interface, and no amount of authentication makes that worth doing inside
this process when a tunnel does the same job outside it.

**Availability is not defended at this boundary, and that is a concession rather than an
oversight.** A connection takes one of `GateOptions.MaxConnections` slots when its socket is
accepted, before the ClientHello is read and before any name resolves, so a peer holding no gate
key, no certificate and no invitation takes one by connecting, and enough of them, reopened as each
is cut, keep every admitted client out. A per-source cap would mean nothing here, since the gate
binds loopback and every peer arrives from `127.0.0.1`, whatever arrives through a tunnel included. Closing it
would mean reserving slots for connections whose name has resolved, or evicting the oldest that
has not, and that is a decision this design has not taken. What it costs is availability and
nothing else: no secret and no write is reachable from a slot. `GateListener.Take` carries the same
concession beside the code.

## 5. Identity, and what a certificate means §111

**A certificate proves who a client is. It grants nothing.** [`CA.md`](CA.md) is the reference;
this is what the rest of the design depends on.

- **Every database is its own certificate authority**, stored inside itself under `KPG` keys in the
  database's own `CustomData`. That is what makes a malicious kdbx harmless: it vouches only for
  itself, and a certificate it issued opens exactly one door, its own. CA.md §204 says why that
  dictionary and not a group's.
- **The issuer generates the key pair and builds every extension.** It never accepts a signing
  request, because a request can carry extensions and an issuer that copies them lets the client
  choose its own claims.
- **The anchor must be pinned explicitly.** Measured: the natural chain validation accepts a
  certificate signed by a completely different authority, because `ExtraStore` supplies candidates
  without constraining the anchor and `CustomRootTrust` is .NET 5 and later. The root's DER is
  compared against the resolved database's authority or the connection is refused.
- **The EKU is checked explicitly**, because the same authority signs both hosts and clients, and
  hostname verification is deliberately abandoned. The SNI rotates per connection and can therefore
  never appear in a SAN.
- **The authority key needs no passphrase.** It is reachable only when the database is decrypted,
  and no API path reaches it. An earlier draft wrapped it in Argon2 over AES-CBC with an
  encrypt-then-MAC construction and a DPAPI layer; all of that is deleted, because delegating to
  KeePass's own encryption is strictly better than cryptography written here.
- **Claims carry profile names only**: `kpg://profile/<name>`. The database is not named because the
  issuing authority already identifies it, which removes the wildcard and the escalation it carried.
- **The SAN is a convenience, not the authority.** The permitted profiles are read from the
  **admission record** in the database, checked on every connection. A certificate whose record is
  missing is inert.

That last point is the one that carries the design, and it exists because of a measured failure
rather than a preference. `GroupForm` snapshots a group's `CustomData` when it opens and writes the
snapshot back on OK, so a record written while that dialog is open is silently destroyed. Making the
record load bearing turns that from an invisible, irrevocable certificate into a client that must
pair again. [`PAIRING.md`](PAIRING.md) has the full sequence.

### The gate §112

Before any TLS work, the ClientHello is read off the socket and its `server_name` resolved against
every open database. The name is `nonce . HMAC(key, nonce) . kpg`, rebuilt per connection, matched
in fixed time against each database's gate key and each pairing token held in memory.

An unresolvable name **drops the connection with no response**, so an attacker who cannot compute a
tag never reaches the TLS, certificate selection, or the pairing dialog. It also means an expired
invitation is indistinguishable from one that never existed, which is deliberate and which the
client library has to explain, because nothing on the wire will.

### More than one host serving the same database §113

A replicated kdbx carries its authority, its host certificate and every pairing with it, so a second
machine serving the same file is already trusted by every client. Nothing needs copying and no
per-host certificate is issued.

That works because the name a client verifies is derived from the **authority**, not the host, so it
is identical wherever the database is served. Measured: a client connecting to `127.0.0.1` by
address while passing that name as the target host got no `RemoteCertificateNameMismatch`.

**What must not be shared this way.** An earlier draft moved the admission registry into the kdbx so
it would replicate, then withdrew it as exploitable, then reinstated it once the authority itself
moved inside. The distinction that makes it safe now: the records are only meaningful under an
authority that is in the same file, so a prepared database cannot admit a client to anything but
itself.

**An invitation does not replicate**, because pairing tokens live in plugin memory. A client pairs
against the host that issued its invitation.

**One hazard that is KeePass's, not ours.** Two KeePass instances writing one replicated kdbx is the
ordinary multi-writer problem, and this design does not solve it. Admission records are one key per
client in the database's own `CustomData` precisely so a merge is per client rather than
last-writer-wins, and a revocation is written empty rather than removed so that the merge carries it
too; see CA.md §204. The safe arrangement is still one writer and any number of readers.

**Two copies of one database open at once** share an authority and a `SniKey` and are genuinely
indistinguishable. The plugin refuses to serve while that is true, rather than guessing, and logs
how many copies answered. It does not name their paths.

## 6. Authorization §114

Two independent answers, and a call needs both.

```text
permitted = record(from the kdbx)  INTERSECT  acl(from the kdbx)
```

Both halves live in the database, which is deliberate. The **admission record** carries the profiles
the operator approved at pairing, and it is what the certificate's SAN once did. The SAN still
carries them, but only as a convenience for a human reading a certificate: a client that presents a
valid certificate whose record is missing is refused outright rather than falling back to the SAN.
[`CA.md`](CA.md) §206 says why that is the only arrangement that survives a lost record.

The ACL is `AclDocument`, `AclResolver`, `AclProfiles`, `AclGrant`, `AclVerb`, `AclDefault`,
`AclVerbView` and `KdbxFormat`. It stays authoritative, so access can always be taken away by editing
the database rather than by reissuing a certificate.

Grants live in group and entry `CustomData` under **`KPG`**, a single key holding a JSON document,
which is this plugin's own namespace. Sharing a key with another plugin would couple two schemas
through a file format permanently.

One key holding one document, rather than a key per concern, because `CustomData` is a flat string
dictionary and a document keeps a group's rules readable and atomic to write.

Every failure denies: an unreadable document, an unknown default, a missing chain, an unparseable
claim, a database that is not open.

## 7. Spec-first routing §118

The OpenAPI document is the routing table, and it ships as an embedded resource.

- Handlers bind to **`operationId`**, never to a path.
- At startup, every declared operation must have a handler and every handler must answer to a
  declared operation. A mismatch is a **startup failure**, not a 404 discovered later.
- Path templates capture parameters. Bodies are checked against the declared schema **before a
  handler runs**.
- **No schema library does that checking.** Every candidate costs more than the subset this wire
  carries is worth: fourteen assemblies, twenty, or a license an MIT plugin cannot hold. What checks
  a body instead is a checker over that subset, one flat object of strings and arrays of strings,
  plus a **startup refusal of any schema it does not implement**. That is what keeps a subset from
  becoming a gap: a construct nobody implemented stops the plugin loading, and can never be a body
  that went unchecked because the checker did not recognize its schema.
- **The grammar is `Newtonsoft.Json`'s and the policy is ours**, which is the split worth keeping.
  Parsing JSON correctly is solved better elsewhere than it would ever be solved here, and one
  assembly against nine decides which. Deciding what this wire may carry is a decision and stays
  where it can be argued with. The trap worth naming: refusing a schema library is a decision about
  validation, and letting it become a decision to own a grammar is a much worse trade on the wire an
  unauthenticated caller reaches first. The counts are on `spikes/json-reading`.
- An undeclared path has no handler to reach, so **spec-first routing is default deny** by
  construction, which is the same posture as the ACL.

Each operation declares the verb it needs, and the authorization layer reads that from the document
rather than from an attribute in code, so the specification stays the single account of what the API
permits.

## 8. KeePass integration §119

The part with the most ways to go wrong, because a plugin shares the password manager's process.

**Reads happen on the connection's thread and only writes marshal.** An earlier draft of this
section proposed the opposite, marshaling every database read onto the UI thread, which bought a
safety KeePass's object model does not document anyway and created the deadlock risk it then had to
mitigate.

What the platform actually requires, measured rather than assumed:

| Operation | Thread | Why |
| --- | --- | --- |
| Read databases, entries, strings, binaries | **connection thread** | `DocumentManager.GetOpenDatabases()` answers from a background thread, which the end to end tests exercise from another process |
| Mutate in memory | **connection thread** | the same objects, and nothing about them is thread-affine |
| **Persist** | UI thread | `MainForm.SaveDatabase` blocks interaction, shows warnings and touches the window; disassembling it is what settled that |
| Ask the operator a question | UI thread | it puts a window on the screen |

So the rules here:

- **Reads and in-memory mutations happen on the request thread.** This is what is proven, and
  marshaling them would buy a theoretical safety that KeePass's own object model does not document
  anyway, at the cost of making liveness depend on the UI thread.
- **Persistence marshals with `BeginInvoke` and then waits on the handle**, never `Control.Invoke`,
  which has no timeout. One writer owns every write, so a request thread does wait on the UI thread.
  What that buys is a wait that ends: a marshal that does not complete fails the request rather than
  deadlocking it. Pairing's writer is that writer while pairing is the only thing writing; the
  database API brings `DatabaseWriter` and the same rule.

  **The bound has two halves, and only one of them is a timeout.** Work still sitting in the queue
  when the timeout expires is withdrawn, so the caller's "no" is true of the database as well: the
  alternative is a write that lands minutes after the client was refused and its certificate
  destroyed. Work that has already begun cannot be withdrawn, and there the wait runs to completion
  rather than to the clock, because releasing the writer's lock while its own write is still going
  on is the race the single writer exists to prevent. That half is bounded by the work instead: a
  file save, and a dialog that closes itself.
- **A plugin-wide write lock.** It closes the one race this design can close cheaply: two requests
  mutating the same entry at once. It does nothing about a user editing in the UI at the same
  moment, which stays exactly as unprotected as it is today.
- **No API request ever raises a dialog.** An unattended agent must not be able to prompt somebody's
  screen, so anything behind the spec router that would need a human answer is refused with a
  distinct status instead.
- **Pairing is the one deliberate exception, and it is a real one.** `/pair/request` exists to ask
  the operator, and issuance writes the admission record and saves it before the certificate is
  handed over, because a certificate must not leave here until its record is on disk.
  [`PAIRING.md`](PAIRING.md) phase 5 is the sequence.

  So the deadlock case is **mitigated rather than absent**, and earlier versions of this section
  claimed otherwise twice over: first that it was absent, then that pairing was the only path that
  waited on the UI thread. Neither is true. Every write marshals the same way, through the same
  bounded wait, so **every** write can hold a request thread. What keeps the case closed is that the
  wait ends rather than that most writes avoid it: a write that does not complete fails its request,
  and a pairing attempt that cannot obtain the lock, or whose save does not complete, fails the
  pairing rather than waiting on it.

  **A pairing connection is also given a longer socket deadline**, which is a third correction. The
  transport closes a connection that takes more than `GateOptions.Budget`, and phase 4 exceeds that
  by design because it is waiting on a person rather than on the wire. Closing the socket there does
  not cancel anything: the invitation is still spent and the record still saved, and only the answer
  is lost. A pairing budget of its own covers the whole of what a pairing waits for, and a test is
  to pin it against the four timeouts it has to outlast.
- **Nothing throws to the transport.** A handler failure becomes a 500 with an audited reason,
  because an unhandled exception in this process takes KeePass with it.
- Distribution is a **plain DLL** targeting .NET Framework 4.8, not a `.plgx`, which is what allows
  modern C# and what the packaging measurement showed.

## 9. Audit §120

Every decision, allowed or refused, as JSON Lines outside the database: the admission key,
operationId, entry UUID, decision, reason. Never a title, never a value, because a log of titles is
an inventory of the database sitting beside it. A write failure is reported, not fatal, so a full
disk cannot silently revoke every agent's access.

---

# Challenging this design

Written against the design above, deliberately. Where an objection stands, it says so.

## The objections that stand §121

### C1. Two plugins will both want an "Access control" tab §122

**Answered, by owning one namespace and naming the tab for this plugin.**

The storage question was never the hard half: this plugin writes its own `CustomData` key, `KPG`,
holding a JSON document, and touches nothing another plugin owns. The objection was about the screen.
Two "Access control" pages on the same dialogs, governing different clients, guaranteed to disagree,
with nothing to tell them apart.

**The resolution is that this plugin owns one namespace and claims nothing else.** Grants live
under `KPG` and the tab is labeled for this plugin, so a user running something else that also
edits access control sees two clearly separate things rather than two pages competing to mean the
same thing. What cannot be defended against is another plugin choosing to write into the same key,
and nothing in `CustomData` could prevent that.

### C2. Carrying 922 KB to use perhaps a fifth of it §123

**Dissolved, by removing the dependency.** The objection was that taking websocket-sharp meant
owning a WebSocket implementation, a cookie stack and an authentication stack: code carried and kept
unreachable forever.

Section 4 records why it went, and the reasoning there had to be corrected once. Owning TLS left
websocket-sharp parsing only a header block, which does not justify a submodule and reflection into
an `internal static`. But the candidate that genuinely fits the new seam is **uHttpSharp**, which
needs no changes at all, and it is ruled out on **license** rather than on fit: LGPL-2.1 against a
project that chose MIT so a compiled release carries no copyleft obligation.

What is carried instead is roughly 250 lines of our own bounded parsing, in two pieces: a ClientHello
reader and an HTTP request-head reader. The first is the one that matters, because it faces
unauthenticated bytes, and it is measured in `SniProbe.cs` on `spikes/tls-and-certificates`.

### C3. Reading the database off the UI thread is a data race §124

**This objection was originally the reverse**, and investigating it changed the design rather than
mitigating it. The first draft marshaled every read onto the UI thread and then had to answer for
the bottleneck and the deadlock that created. Neither cost turned out to be necessary: reads answer
from a background thread, and only the save has to marshal.

What remains is the real objection, and it is smaller. KeePass documents no thread safety for
`PwDatabase`, so a request reading an entry while the user edits that entry in KeePass's own dialog
is a race in principle.

**Accepted, for two reasons.** KeePass's dialogs work on a copy and write back on OK, so the
window is narrow. And the alternative, marshaling everything, trades a rare race for a permanent
bottleneck and a deadlock class that would then have to be defended against.

**What is added** is the one race that can be closed cheaply: a plugin-wide write lock, so two
requests cannot interleave mutations of the same entry.

### C4. The plugin as its own CA puts the CA key beside the database §125

**The objection was right, and the answer is now structural rather than defensive.** Its point was
that an authority's key concentrates value: stealing it mints identities rather than borrowing one.

An earlier draft answered with layers, a passphrase over PBKDF2 over AES-CBC over DPAPI, and claimed
two independent factors. That is deleted. It was cryptography written here to protect a key that was
sitting beside an encrypted database anyway, and its backup story was broken: a DPAPI-wrapped blob
cannot be restored on another machine, so the recommended backup restored nowhere.

The key now lives **inside the database**, under the database's own encryption. Three consequences:

1. **Possession of the file is possession of the key**, and that is correct, because possession of
   the file is already possession of every secret the authority protects. There is nothing left to
   defend at that layer.
2. **Backup and restore are backup and restore of the kdbx**, which the operator already does and
   already tests.
3. **The blast radius is one database.** The earlier design had a single authority spanning all of
   them, so a stolen key was a total loss. Now it is not.

What genuinely remains: an authority reachable from any process running as the user while the
database is open. That is the conceded local-code-execution case, and no layering answered it before
either.

## The objections that do not stand, and why §126

### C5. "Terminating TLS ourselves is riskier than letting a library do it" §127

The opposite, here. The server offers TLS 1.3 and nothing else, decided in `GateTlsServer` rather
than by the operating system's version, which is the same offer a library would make with one less
layer between the setting and the wire. §133 records why the stack under it is Bouncy Castle. What
we add is the ClientHello peek in front of it, and that is unavoidable: no library on
.NET Framework can select a certificate by SNI, because the platform exposes no hook for it.

The genuine new risk is the peek itself, which parses unauthenticated bytes. It is bounded, total,
and returns no name rather than throwing; `SniProbe.cs` is the evidence it works and
`HmacSniProbe.cs` that a forged name is dropped before any TLS work.

### C6. "Spec-first routing is slower than a switch statement" §128

It is, and the API has fewer than a dozen operations. The property being bought, that an undeclared
path has nowhere to go and that the document cannot disagree with the implementation, is worth more
than the microseconds.

### C7. "The ceiling in the certificate is redundant if the ACL decides anyway" §129

It is not, though the shape of the answer changed. The certificate no longer names databases, because
the issuing authority does that. What remains is an **admission record**, and it is not redundant
with the ACL: the record says whether this client exists at all and which profiles it was granted,
the ACL says what those profiles reach. Two independent answers that fail independently, and the
record additionally fails **closed** when it goes missing, which is what makes the `GroupForm`
snapshot hazard survivable.

## What would falsify the design §131

Written down so that it is possible to be wrong about this on purpose rather than by drift.

- **If the ACL model does not survive contact with real grants.** The plan assumes a document per
  group resolves cleanly against a real database. If it needs reshaping once it meets one, the cost
  estimate is wrong and the design should be revisited before more is built on it.
- **If the synchronous marshal on the pairing path proves unusable**, the issuance ordering needs
  replacing rather than adjusting. The wait is there because handing over a certificate before its
  record is saved would create a credential nobody can revoke. It is not the only place a request
  thread waits on the UI thread, which this bullet once said: every write waits there the same way,
  through `BeginInvoke` and a bounded wait (§119). If that wait cannot be made reliable, the
  alternative is to issue only from the UI thread outright and let the request poll.
- **If our own request parser accumulates cases.** It is justified only while the HTTP subset stays
  trivial. If chunked bodies, keep-alive or upgrade become necessary, writing it here stops being the
  cheaper trade and `HttpRequest.ReadRequest` is the seam to return to.
- **If the `GroupForm` snapshot hazard proves unavoidable in practice**, rather than merely
  survivable. The design absorbs it by making a lost admission record fail closed, and step 5 of the
  build order exercises it deliberately. If records turn out to be lost often enough to be a support
  burden, `CustomData` is the wrong store and that decision has to reopen.

## Build order §132

Each step is refusable, and each ends with something that runs.

1. **Foundations and the design**: the repository, the prose gate, the license and the design
   itself, standing on measurements taken on branches of their own. Nothing here compiles, and
   that is the point: what this is and why it is shaped this way is settled and written down
   before any of it is built, so the argument can be refused before anybody has paid for it. **Done.**

2. **Continuous integration**: the gate script, the four checks nothing else can make, and the
   workflow that runs whatever a branch declares. Nothing compiles yet, so the only thing to check is
   the prose, and that is the argument for putting it here rather than later: a gate has to exist
   before there is anything for it to judge, and one added afterwards has never refused anything. **Done.**
   `build.ps1 -ListGates` is what the workflow reads, so a task added to the script is checked
   without anybody editing YAML and the two lists cannot disagree. That a hosted runner can run every
   one of them, including the end to end tier that drives a real KeePass across a desktop, is
   measured on `spikes/hosted-runner-desktop` rather than assumed.

3. **Provided KeePass**: the version everything is built and tested against, fetched and
   verified rather than found on the machine. **Done.** `keepass.version` pins the release and `keepass.sha256` pins the
   archive, the directory is named after the version so changing the pin cannot silently reuse the old
   one, and the executable inside is checked for a valid signature from KeePass's author, which is the
   check that does not depend on where the bytes came from.
4. **Plugin skeleton**: loads into KeePass, a KPG tab on the database settings dialog, starts and
   stops cleanly. **Done.** The tab's contents sit behind `IGatePanelFactory` so that step 5 replaces
   them rather than editing around them, and everything above the KeePass boundary is written against
   interfaces small enough to test without KeePass loaded at all: `IWindowEventSource`,
   `IFormMatcher`, `IFormDecorator`, `IGateLog`. Two classes touch KeePass, and both are adapters.
5. **Authority**: create, recreate and delete from that tab, with the certificates in `CustomData`
   and the `GroupForm` snapshot hazard exercised deliberately. Make a grant, press OK, reopen, and
   only then believe it. **Done.** The storage survived: an authority created from the tab is still
   there after a save, a restart of KeePass, and a reopening of the file, proved end to end rather
   than argued. The hazard is now measured rather than remembered, and it cut both ways: the dialog
   the KPG tab lives on carries no snapshot, so those buttons are safe from their own dialog, while
   anything written during a **group** dialog is destroyed by its OK, which is what step 8 has to
   answer for. Two things step 7 inherits early: `AnchorPin`, because telling a stored pair apart by
   issuer name turned out to accept a foreign authority, and the gate key.
6. **Gate and transport**: accept loop, ClientHello peek, SNI resolution, TLS 1.3, our request
   parser, one route. `IGateContext` lands here.
7. **Validation**: anchor pin, EKU check, admission record lookup, and a test that a foreign
   authority's certificate is refused.
8. **Pairing**: invitation in memory, the proof exchange, the approval dialog, issuance with the
   record written and saved before the certificate is handed over.
9. **Lock and unlock**: the responder certificate, dropping the privileged key, closing connections
   on both transitions.
10. **The reference client**: `PyKeePassGate`, pairing and every route the gate serves, with the
   protocol pieces pinned against the plugin's own output. It arrives here rather than at the end
   because a second implementation is what turns a specification into a contract: where the two
   disagree one of them is wrong, and a disagreement should fail a test rather than a pairing.
11. **Spec router**: the document, `operationId` binding, schema validation, startup assertion.
   Decide `HEAD` here: today a `HEAD` on a declared route gets 405, because the router requires the
   declared method exactly. That is honest and fail closed rather than a defect, but HTTP says a
   `HEAD` should be served wherever a `GET` is, so the document is the place to say which methods
   exist.
12. **The ACL**: the model in section 7, resolving against a real database.
13. **The editor**: the grant UI, the largest single piece and the last thing needed.
14. **Intersection and audit**: the two answers combined, every decision recorded.
15. **The database API**: the routes that read and change groups, entries, fields and attachments.
    They arrive with no new machinery, which is the point of everything under them: a verb in the
    document, a node selector beside the handler, and the router does the rest.
    `API.md` is the account of them, and `openapi.yaml` is the contract.
    TODO(step 11): `openapi.yaml` arrives with step 11, and `API.md` with these routes.
16. **Running it**: two launchers that build, deploy and start the pinned KeePass with the plugin in
    it, one per configuration.
17. **A profile gets a home**: the set of names a database knows, stored with the authority, with a page
    of its own on the tab to manage them and a purge that takes a deleted one out of every rule.
18. **The plugin's own window**: one item under KeePass's Tools menu, opening a window that says
    where the gate is listening and sets the port it listens on.
19. **The documentation for whoever runs it**: `docs/`, in the order an operator meets the plugin,
    with the screens.

Steps 5 to 8 are the ones that prove the design. Step 5 is deliberately early, because the
`CustomData` dialog hazard is the thing most likely to invalidate the storage decision, and finding
that out after building five layers on top of it would be expensive.
