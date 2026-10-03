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
| What the grammar does not allow: a control character in a header value, a header folded onto the next line, a character RFC 3986 says to escape arriving bare, a query name sent twice | during the parse, 400 |
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
last-writer-wins, and a revocation or a retired authority is a key of its own that is only ever
added, because a KeePass merge never removes a key and never reads a value; see CA.md §204. That
replaced a revocation written as an empty record, which a merge carried only while the file kept its
times. The safe arrangement is still one writer and any number of readers.

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
[`CA.md`](CA.md) §206 says why that is the only arrangement that survives a lost record. Of the
record's profiles, only those the database's list still names are handed on, so a name the operator
removed grants nothing whatever rule still carries it; §139 says why that replaced purging the rules.

The ACL is `AclDocument`, `AclResolver`, `AclGrant`, `AclVerb` and `AclDefault`, with `KdbxNodes`
adapting a `PwGroup` or a `PwEntry` to the one seam the rules need. It stays authoritative, so access
can always be taken away by editing the database rather than by reissuing a certificate.

An earlier draft of this list also named `AclProfiles`, `AclVerbView` and `KdbxFormat`. Two of those
turned out not to be types. The format is `AclDocument.Read` and `AclDocument.Write`, which is what
`KdbxFormat` would have held and nothing else, and a set of profiles is an `IEnumerable<string>`
argument rather than a class. `AclVerbView` was real work that had not happened when this was written. The editor does that
job now, with `VerbMask` and `VerbKeys`, because how a person is shown a verb is a different
question from how one is decided.

Grants live in group and entry `CustomData` under **`KPG`**, a single key holding a JSON document,
which is this plugin's own namespace. Sharing a key with another plugin would couple two schemas
through a file format permanently.

One key holding one document, rather than a key per concern, because `CustomData` is a flat string
dictionary and a document keeps a group's rules readable and atomic to write.

### The document §136

One flat object of strings and arrays of strings, which is exactly what `JsonBody` already reads, so
the ACL brings no parser with it:

```json
{"default":"deny","reader":["read"],"editor":["read","update"]}
```

A rule holds **six verbs and two switches**, which is `CRUSDLAP` and is the order the editor shows
them in. The verbs are create, read, update, strip, delete and list. `list` is below `read` because
knowing an entry exists is not knowing what is in it, and an agent that may search a database without
opening it is a useful thing to be able to describe. `strip` sits beside `delete` for a related
reason: taking a field off an entry and taking the entry away are two sizes of loss, and with one
letter for both there is no way to allow the smaller one.

**`strip` is may remove the parts inside this node**, which are an entry's fields and its
attachments. It is one question asked at one node, because a field has no rule of its own.

**`delete` is removals are allowed here**, and removing a node asks for it **on both sides of the
move**: from the group the node is leaving, and from the node itself. A delete is literally a move,
which is what makes that the honest shape rather than a contrivance. A group that grants it has
removable contents; a node that does not grant it cannot be removed, and that is how one node is
held back from a rule that frees its siblings.

So `....D...` on a group is a client that may create and remove entries and subgroups inside it,
including ones it creates after the rule was written, while the group itself stands, because nothing
above it allows removals. And `...S....` written on one entry leaves its fields open to stripping
and the entry itself exempt, while its siblings go on being removed under the group's rule.

**Which means `delete` is granted on the container whose contents may churn**, rather than broadly
at the root. Granting it at the root is what would create the need for exemptions, and under this
reading that instinct is simply wrong: a rule saying removals are allowed here says where.

**A group is removed only once it is already empty.** A recursive delete would destroy entries that
were never individually decided or recorded, so a subtree comes apart one node at a time, each
removal asked on both sides and written to the audit on its own. An entry goes in one act, with its
fields and its attachments.

**Two costs, neither hidden.** A node removal is decided twice, so the audit says which side refused;
the resolver already answers with the node that decided, and the design already intersects two
independent answers for admission, so an AND here is the existing idiom rather than a new one. And
protecting a group freezes what is under it, since the exemption and the permission are the same
letter: the contents' container no longer allows removals. That reading is right for "this subtree is
off limits", and the case it cannot express, a protected group whose contents still churn while its
parent allows removals, is written instead by not granting `delete` on that parent.

**A switch is not a verb.** Each says how far the verbs a rule already grants reach, so a rule
holding switches and no verb grants nothing at all: it is a scope over nothing.

**`attachments` reaches the attachments inside an entry** as well as its fields; without it they
reach the fields only, so an attachment is not listed, read, added or replaced however much else
the rule allows. So `.R...LA.` is read and list over entries and over the attachments inside
them, and `.R...L..` is the same two over the fields alone.

**`protected` reaches the values the database protects.** KeePass marks a string protected and keeps
it encrypted in memory; the password is one by default and any field can be another. Without the
switch, a rule that grants `read` still answers with the entry, its title, its dates, the names of
every field it holds and the values of the ones nothing protects, and the protected values come back
as `null`. With it, they come back as themselves.

**Neither switch is asked about for a removal.** They say how far the verbs a rule grants see or
change inside an entry, and removing something shows nobody anything. A rule that may remove an
entry already takes every attachment and every protected value with it, so asking for a scope before
one of them can be stripped on its own would forbid the smaller act while allowing the larger.
`...S....` strips an attachment and a protected field alike, and a removal takes both away with the
entry around them.

**One removal is asked about, and it is a removal only in form.** A rule holding `strip` and
`create` can take a protected field away and write another under the same name, which is a
substitution rather than a removal: what it ends with is a protected field holding a value the
client chose, and `protected` is exactly the scope that governs writing one. So a strip is refused
when the same rule could create in that field's place and could not have created a protected value
there. The question is asked about `create`, never about `strip`, which is what leaves the paragraph
above standing: a rule that may strip and may not create still takes a protected field off with no
switch at all. The refusal is the `404` every other protected refusal gives, and the audit says
which it was.

**It governs writing too.** A rule holding `update` without `protected` may change a URL and may not
overwrite a password, and one holding `create` without it may add a field but not a protected one.
That half matters more than it first looks: reading a credential is the attack everybody pictures,
and replacing one with a credential the caller already knows is the quieter one, invisible in a log
that records only that an update happened.

**What it is keyed on is the database's own mark, not on what reads like a secret.** A password
somebody unprotected is not covered, and notes full of secrets that nobody protected are not covered
either. The alternative is this plugin deciding what is confidential on somebody's behalf, which it
would get wrong in both directions and could not explain.

**`.R...L.P` is the shape this exists for.** An agent that has to recognize an entry from its context
needs more than `list` gives it and less than a credential: a title, a URL, the field names, the
unprotected values. `read` on its own overshot that, and `list` undershot it, and there was nothing
in between to grant.

Both are stored alongside the verbs rather than in members of their own, because a rule is one list
of what it grants and a reader who has to look in three places for one answer will look in one.

A grant is a **set**, not a rung on a ladder. A ladder where naming a verb carried everything weaker
would make for a smaller editor, and it would leave no way to say `create` without `read`, which is
exactly the shape an append-only agent wants.

`default` is the one reserved member, and it cannot collide with a profile because `default` was
already refused as a profile name by the authority that puts profiles into certificates, before this
format existed. It is `inherit` unless it says `deny`, and it is written out either way so a document
says what it does with what it does not name.

**An empty array is a rule, not an absence.** `{"reader":[]}` says this profile may do nothing from
here down, which is how a grant made at the root is taken away in one group without a deny syntax to
get wrong. A profile the document does not mention is a different thing: that is silence, and silence
is what `default` answers.

The reader's ceilings are the format's ceilings, and reaching one denies rather than truncates: a
node carries the default and 31 profiles, and a 32nd makes the whole document unreadable and
everything under that node refused. That is the right way round, but it is worth knowing about
rather than discovering.

### The set of profiles is written down §139

A profile had no home for most of this design. A name became real by being approved at a pairing and
meant something by being named in a rule, and nothing anywhere held the set. That works until
somebody has to answer "what may I grant here", and the honest answer was "whatever you type,
spelled exactly as somebody else typed it". A typo made a profile that existed, granted nothing, and
looked exactly like the one that was meant.

So the set is `ProfileNames`, under `KPG.Profiles` beside the authority and the records, in the
same grammar as everything else here:

```json
{"profiles":["reader","ops"]}
```

**It holds names and nothing else.** What a name reaches is on the nodes, who holds one is in the
admission records, and the set of them is here. Three places, three questions, none of them
duplicated, and no fourth place where they could disagree.

**A name this list does not hold grants nothing.** A client is given the profiles its admission
record holds that the list still names, read uncached beside the record on every request, and the
resolver is handed only those. So taking a name off the list is the whole of taking it away: a rule
for it that stays on a node, or comes back with a restored revision or a merge, answers for nobody.
**This reverses what this section first said**, that nothing is authorized by the list and the
resolver has never heard of it, which made losing the list cost an operator their pick list rather
than anybody their access. The intersection makes it load bearing, and fails closed the way
everything here does: a list that will not read is an empty set, so every client stays admitted
and reaches nothing until it reads again.

The one rule it applies is the issuer's, `AuthorityIssuer.IsUsableProfileName`, because a name that
cannot travel as a claim is a name no client can ever hold: it would be granted here and refused at
the certificate. `default` is refused by that rule already, so there is no second reservation written
beside it to disagree with the first.

Two things follow from it existing, and both are what it was for:

**The editor picks rather than types**, on both columns. A mask spelled by hand is a rule nobody
meant and a profile spelled by hand is a rule for a name that may not exist, and the second one is
worse: it grants nobody anything and reads exactly like a rule that works. `GrantSheet.Available` is
what is offered, which is this list less every profile already on the table, explicit and inherited
alike, because a profile is one row however it got there and narrowing an inherited one is done on
the row that is already there.

**Removing a profile touches the list and nothing else**, and that reverses the purge this section
used to require. `ProfilePurge` walked the tree and took a removed name out of every rule, because
a name left in the documents went on being obeyed and a profile of the same name created later
would arrive holding permissions nobody wrote for it. Each way the name came back then needed a
rule of its own. The page refused to remove while a group or entry dialog was open, since that
dialog's OK would put the rule back for its node. The walk reached every revision in an entry's
history, since restoring one copies its rules back over the live entry. It rewrote a revision
without touching it, since KeePass matches revisions across a merge by modification time and keeps
the local one where both carry that time. And it ran a second time over the live tree at OK, since
an API update made while the removal waited put a new revision naming the profile into the history
and, at KeePass's history limit, moved every older one down a place. None of that closed a stale
copy merged in, which brings the old rule back with the node's own time.

**The intersection closes all of them at once**, because a rule for a name off the list grants
nothing wherever it comes from: kept by a dialog's OK, restored from a revision, or merged in from
an older copy. The grant editor strikes such a rule through rather than hiding it, removal needs no
walk and is refused under no node dialog, and the page counts the live tree's rules only, since a
revision's rule is in force for nobody. **Adding the name back is a full undo**, rules and clients
alike, so the page asks first, saying how many rules come back into force and naming every client
that regains the name. Approving a pairing for a removed name that clients still hold warns the same
way, because pairing writes the approved name onto the list.

### Resolution §137

**Nearest wins, per profile.** The walk runs from the entry up through its groups to the root and
stops at the first document with a rule about that profile. What that rule says is the answer, even
when something further up said more.

**Each profile is resolved on its own, and then they are combined with or.** A client holding two
profiles is permitted when either of them is. Resolving them together would let the nearest rule
about one profile decide for another, so holding an extra profile could take a permission away.

**Every failure denies**: an unreadable document, an unknown default, an unknown verb, a node with no
chain to a root, a client holding no profiles, a database that is not open. A document that will not
read is refused where it is found rather than skipped, because carrying on up the tree would answer
with a permission the broken rule may have been taking away.

**How far in is asked at the same time as what.** An operation reaching an attachment needs the verb
and the switch from the same rule, so a rule granting `read` without the switch refuses where it is
found rather than sending the walk up to a rule that would have allowed it. `protected` is answered
the same way and at a different moment: an attachment is a route of its own, so the router asks
before dispatch, while a protected value arrives inside an answer to a route that is allowed. That
one question is asked on the way out, in a single place between the handler and the response, for
the reason every other question is asked once: twenty-five call sites write field values, and a
check each of them has to remember is a check one of them will not.

The verb comes from the routing table, as `x-kpg-verb`, and **an operation that declares none stops
the plugin loading**. That is what keeps authorization from being something a new route can be added
without: there is no permissive default to fall into, only a document that does not parse. `none` is
a decision and it is written down, for the status route, which reads nothing, and for the two pairing
routes, which a person authorizes by reading a dialog rather than by a grant.

`AclResolver` returns why as well as whether, naming the node whose rule decided by its identifier
and never by its title, because those reasons reach the audit log in step 14 and a log is meant to be
readable by somebody not entitled to know what the database holds.

## 7. Spec-first routing §118

The OpenAPI document is the routing table, and it ships as an embedded resource.

- Handlers bind to **`operationId`**, never to a path.
- At startup, every declared operation must have a handler and every handler must answer to a
  declared operation. A mismatch is a **startup failure**, not a 404 discovered later.
- Path templates capture parameters. Bodies are validated against the declared schema **before a
  handler runs**.
- **No schema library validates the body**, because every candidate costs more than the subset
  this wire carries is worth: fourteen assemblies, twenty, or a license an MIT plugin cannot take.
  What checks a body instead is a checker over that subset, one flat object of strings and arrays
  of strings, plus a **startup refusal of any schema it does not implement**: a body's, a path
  parameter's or a query parameter's, held keyword by keyword to a list of what each type there may
  say, so that anything the checker would not act on refuses to load. That is what keeps a
  subset from becoming a gap: a construct nobody implemented stops the plugin loading, and can
  never be a body that went unchecked because the checker did not recognize its schema. **A string
  may carry a `pattern`**, compiled as the document is read and held to what JSON Schema means by
  one, which is ECMA-262's: there `$` is the end of the value, where .NET's also matches before a
  final line feed, so the checker writes it as `\z`. A pattern is held to the subset the two
  readers agree on, and one stepping outside it stops the plugin loading: `.`, `\s`, `\a` and `\e`
  mean different things to each, and `\A`, atomic groups, inline options, class subtraction and
  the rest are .NET's alone. `BodyCheck.Portable` names them.
- **The grammar is `Newtonsoft.Json`'s and the policy is ours**, which is the split worth keeping.
  Parsing JSON correctly is solved better elsewhere than it would ever be solved here, and one
  assembly against nine decides which. Deciding what this wire may carry is a decision, and stays
  where it can be argued with. The trap named on the branch that measured this: refusing a schema
  library is a decision about validation, and letting it become a decision to own a grammar is a
  much worse trade on the wire an unauthenticated caller reaches first. The counts are on
  `spikes/json-reading`.
- An undeclared path has no handler to reach, so **spec-first routing is default deny** by
  construction, which is the same posture as the ACL.

Each operation declares the verb it needs, and the authorization layer reads that from the document
rather than from an attribute in code, so the specification stays the single account of what the API
permits.

**HEAD is decided, and the decision is that every GET declares one.** The two are declared
separately and bound to one handler, which is what makes RFC 9110 section 9.3.2's "identical to
GET, except no content" true by construction rather than by two implementations agreeing.
Deriving it instead would have left this document an incomplete account of what the API answers,
which is the one thing it is for; refusing it would have left an HTTP requirement unmet for no
gain. **A pair carries one verb between them**, one reach, one answer about needing a certificate,
one about carrying a body, one set of query parameters and one guard. The plugin refuses to load a
`get` with no `head`, or a pair that disagrees about any of them, because a `head` governed
differently would answer whether
a node exists to a caller its own `get` would have refused, which is an oracle rather than a
route. The answer carries no length, which section 9.3.2 permits: it asks for the same header
fields as GET and allows omitting the ones determined while generating the content.

**And a refusal to a HEAD carries none either, which is the part that was left implicit and was
therefore wrong.** RFC 9112 section 6.3 forbids content on a response to HEAD, and a refusal is a
response: the rule is about the method the client asked with, not about whether a handler ever ran.
So the transport carries the method from the moment the request line yields it, and every refusal
decided below the router answers a HEAD with headers alone, including the ones that judge the target
or the version before the head is believed. A refusal that made it out with a body would have been a
correct route breaking the rule its own answer keeps.

**One refusal is ordered by what it must not reveal.** A caller with no certificate is told the same
thing on a path that does not exist as on one that does, because answering 404 for the first and 401
for the second would let anybody who reaches the port map the API by asking. The exception is a path
whose operations need no certificate: those are declared in a document that ships inside the plugin,
so a 405 there reveals nothing that reading the document would not.

**An operation may have a precondition the document cannot state.** Pairing has one: its routes are
open to a connection that arrived on an invitation and to nothing else, which is neither identity
nor a body. The router asks the operation's own subsystem, before it looks at a body, so a caller
that may not reach a route never learns whether what it sent would have been accepted.

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
| Mutate in memory | **UI thread**, with the save | nothing about the objects is thread-affine, and this row said connection thread until the writer was built: a change and the save that keeps it are one marshaled call, so that nothing can open a dialog on the node between them |
| **Persist** | UI thread | `MainForm.SaveDatabase` blocks interaction, shows warnings and touches the window; disassembling it is what settled that |
| Ask the operator a question | UI thread | it puts a window on the screen |

So the rules here:

- **Reads happen on the request thread.** This is what is proven, and marshaling them would buy a
  theoretical safety that KeePass's own object model does not document anyway, at the cost of
  making liveness depend on the UI thread. **An in-memory mutation travels with its save**, which
  is a correction to an earlier draft of this list rather than a refinement of it: the objects are
  as thread-agnostic as the reads are, but a change made on the request thread and saved on the UI
  thread has an instant between them for a dialog to open on the node, and the whole of the
  writer's argument is that there must not be one.

  **So a read sees a change before its save does.** Reads take no lock against the writer, so a
  change is in the tree for every other connection from the moment it is made, and a save that
  fails takes it back out: a read in between can be handed a node, and with `read` its values,
  that is then withdrawn and answered `404` like anything absent. It shows nothing the rules
  would not, and it is what the read would have returned had the save landed. That is accepted
  rather than locked against, because taking the writer's lock for a read is waiting on the user
  interface thread's save, which is what this rule exists to avoid; `openapi.yaml` §501 says it
  to the client.
- **Persistence marshals with `BeginInvoke` and then waits on the handle**, never `Control.Invoke`,
  which has no timeout. One writer owns every write, so a request thread does wait on the UI thread.
  What that buys is a wait that ends: a marshal that does not complete fails the request rather than
  deadlocking it. `DatabaseWriter` is that writer, and there is one of it rather than one per
  subject: it was pairing's alone while pairing was the only thing writing, and the database API
  took the same writer and the same rule rather than a second one.

  **The bound has two halves, and only one of them is a timeout.** Work still sitting in the queue
  when the timeout expires is withdrawn, so the caller's "no" is true of the database as well: the
  alternative is a write that lands minutes after the client was refused and its certificate
  destroyed. Work that has already begun cannot be withdrawn, and there the wait runs to completion
  rather than to the clock, because releasing the writer's lock while its own write is still going
  on is the race the single writer exists to prevent. That half is bounded by the work instead,
  which for almost every write is a file save and a dialog that closes itself. **Two saves that
  have already gone wrong are the exception**, and they are named under the next rule rather than
  hidden here: both put a message box on the screen and both wait on a person, so a request that
  provokes one holds its thread and the writer's lock until somebody presses OK. Nothing here
  bounds that, and pretending otherwise is what this paragraph used to do.

  **For a database KeePass opened from a URL, the work is the network's.** The writer's check
  before the save reads the file, the save reads it again, uploads it and by default reads it
  back, and the writer's check after it reads it once more: each a download or an upload on
  KeePass's thread, under the writer's lock, bounded only by the network timeouts KeePass gives
  it. For a server that has stopped answering that is minutes, for every write and for every
  other write waiting behind it, and a pairing makes the first read on its connection thread as
  well, before the operator is asked.
- **A plugin-wide write lock.** It closes the one race this design can close cheaply: two requests
  mutating the same entry at once. It does nothing about a user editing in the UI at the same
  moment, which stays exactly as unprotected as it is today.
- **No API request raises a dialog of KeePass's own that could have been foreseen.** An
  unattended agent must not be able to prompt somebody's screen, so anything behind the spec
  router that would need a human answer is refused with a distinct status instead.
  `MainForm.SaveDatabase` asks three questions that can be seen coming, and a write that would
  meet any of them is refused `503 busy` before the save is attempted: saving disallowed by
  policy, a database with no file yet, and a file on disk that is not the one KeePass read. The
  third is the one an ordinary client provokes, because a database on a shared or synced path
  meets it without anybody having misconfigured anything. **It is refused whether KeePass would
  ask or merge.** With `SaveForceSync` on, KeePass asks nothing and synchronizes, and that merge
  still runs inside the request and underneath the write, and can still stop for the other
  file's key or to warn about what it found.

  **Two remain, and both are a save that has already failed.** `MessageService.ShowSaveWarning`
  when `PwDatabase.Save` throws, and the verification warning when the bytes on disk are not the
  bytes written. Neither can be known before the save, and both report damage the operator has to
  be told about, so neither is a dialog worth suppressing even if it could be. What they cost is
  stated rather than denied: the request that provoked one waits on a person, and the earlier
  claim that no request ever raises a dialog was false for exactly these two.

  **And the second cost is that KeePass does not lock while one of them is up**, which is worth
  writing down because it is the one that runs against the operator's own settings rather than
  against a request. `MainForm.SaveDatabase` brackets everything from `PreSaveValidate` to those
  two warnings in `UIBlockInteraction`, and while interaction is blocked the main timer
  restarts the inactivity countdown on every tick instead of locking, and `LockAllDocuments`
  returns without locking and without recording that a lock was wanted. That call is what a
  Windows lock, a user switch, a suspend and a remote-control change all reach, so each of those
  is dropped rather than deferred: after the box is answered, the lock that was asked for is not
  applied and the countdown starts again from zero. No lock means no `FileClosed`, so the gate is
  never told, and every admitted client goes on reading the unlocked database on its own
  connection thread for as long as the box stands. **This is KeePass's behavior for any failed
  save, and what this plugin adds is that an unattended agent can provoke one with nobody at the
  desk.** It cannot be closed from here: nothing in a plugin can make KeePass lock while KeePass
  is refusing to. What narrows it is forestalling the failures that can be seen coming, which is
  what the three refusals above are, and the writer's own guard, which refuses a second write for
  as long as the box is counted.

  **And the operator's own triggers run inside the save**, which makes them the one dialog a
  request can raise that somebody asked for. `SaveDatabase` raises KeePass's Saving database file
  and Saved database file events, so a trigger the operator set on either runs inside an agent's
  write exactly as it does inside their own save: one that shows a message puts a box on the
  screen that the request waits on, and one that uploads, synchronizes or runs a command holds
  KeePass's thread and the writer's lock for as long as it works. That is the operator's
  configuration doing what it was set up to do, so it is run rather than refused, and it is said
  here so that the list of what a write can wait on is not read as closed.

  **So do other plugins' own handlers.** `SaveDatabase` raises `FileSavingPre`, `FileSaving` and
  `FileSaved` to every plugin KeePass has loaded, on its own thread and inside the same marshaled
  call, so a plugin that uploads the file or shows a box of its own from one of them holds the
  request and the writer's lock for as long as it takes, as a trigger does. One that cancels the
  save leaves the database saying it has changes to save, which is what a write is believed on, so
  the change is taken back out and answered `500 not saved`.
- **No write lands inside an operation KeePass is already running.** Being on the UI thread is not
  being alone on it, which the writer's own remarks claimed for a while and which is the one
  assumption in this section that cost data. Everything KeePass takes a noticeable time over pumps
  that thread's queue while it runs: `MainForm.SaveDatabase` pumps before the save and once per
  percent of the serialization, an import or a synchronize pumps from the merge's status logger, an
  export pumps from the exporter's, and a message box pumps by definition. A marshaled call is a
  message, so a write queued by a connection thread is delivered in the middle of whichever of them
  is running, against the tree that operation is walking.

  What that costs, measured rather than reasoned. Inside KeePass's own save, the nested save lands
  and the outer save then commits the serialization it took before the change: the client was
  answered `200` or `201`, the change is in memory, it is not on the disk, and `Modified` is false,
  so nothing will ever write it again. Inside an import, the nested save writes a half-merged
  database and clears `Modified`, so the rest of the operator's import exists only in memory with
  no asterisk in the title and no question on lock, close or exit. Inside an export, the list being
  enumerated moves and KeePass shows the operator a failed export over a truncated file.

  So the writer asks a third question, beside the dialogs and the save's own three, inside the
  marshaled call: **is KeePass part way through something of its own**, and a write that meets it
  is refused `503 busy` before anything is touched. It takes two answers rather than one, because
  the operations do not announce themselves the same way: `MainForm.UIIsInteractionBlocked` covers
  the save and both merges, and an export raises `MessageService.CurrentMessageCount` instead,
  which also covers every message box KeePass is waiting on. **A third operation raises neither**:
  XML Replace, under Tools then Database Tools, merges into the live database with KeePass's
  progress window as its status logger, and that window pumps on every step of the merge. A group
  created under the root there makes the merge throw out of `ApplyDeletions`, with the write's own
  save already on the disk. So the dialog watch also answers whether that window is open, and the
  writer asks it after KeePass's two signals. **Reads are not refused by it**, since
  every one of these hazards is a write. **And a database with unsaved changes is not refused
  either**: an API write still saves what is in memory, the operator's own edits included, which is
  a documented consequence rather than something to guard against.
- **Every `503` this gate sends carries `Retry-After: 2`**, the new one and the ones that were
  always there: `busy`, `locked`, `undecided` and pairing's own. One number for all of them,
  written by the response writer rather than by the four places that answer, so a refusal added
  later cannot be the one that forgets. Which refusal it was is in the body, where a client that treats a lock
  differently from a busy window reads it; the delay is the same because how long any of these
  lasts is a person's business rather than this gate's, and two numbers would invite reading a
  meaning into the difference that nothing here measured.
- **Pairing is the one deliberate exception, and it is a real one.** `/pair/request` exists to ask
  the operator, and issuance writes the admission record and saves it before the certificate is
  handed over, because a certificate must not leave here until its record is on disk.
  [`PAIRING.md`](PAIRING.md) phase 5 is the sequence.

  **Built as `MainForm.SaveDatabase(db, null)` rather than `UIFileSave`**, which is a correction
  to the sentence above rather than a departure from it: `UIFileSave` saves whichever document is
  active, and pairing has to save the database the connection resolved to. Both are the same save
  underneath, and neither tells its caller anything, so what "it worked" is made of is the record
  still being there afterwards and the database no longer saying it has unsaved changes.

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
  is lost. `GateOptions.PairingBudget` covers the whole of what a pairing waits for, and a test pins
  it against the five waits it has to outlast: the screen, because one pairing is put to the
  operator at a time and a second waits as long again just to get in front of them, then the dialog,
  the grace on it, the turn at the writer and the write.

  **And the ordinary budget stops at the end of the request**, which is the half that was got wrong
  while pairing was being counted. A connection's deadline is dropped the moment the whole request
  is off the wire, because that is where the peer stops being able to make the connection take any
  longer: what is left is this plugin's own write and save, and closing the socket under one frees
  neither the thread, which runs on to the end of the save, nor the connection slot, which is given
  back when that thread finishes. All it destroys is the answer to a write that lands anyway,
  leaving a client unable to tell a create that happened from one that did not and holding no
  identifier either way. That is the same loss pairing is protected from, arriving on every other
  route.

  **This section used to claim a floor instead**, `DatabaseWriter.LockTimeout +
  DatabaseWriter.WriteTimeout`, and say that a budget above the sum prevented the loss. It does
  not, and the paragraph above this one says why: `WriteTimeout` bounds the wait for the marshaled
  call to start, and a call that has started is waited out with no clock on it. No sum of this
  plugin's timeouts bounds a save, so no budget could be chosen that outlasts one; raising it from
  thirty seconds to ninety made the loss rarer and nothing else. **Pairing keeps its deadline
  through all of this**, because a pairing is the one connection whose remaining work is a sum
  somebody can write down, and `PairingBudget` is what it is added up against.

- **A write that landed is told to the window as well as to the client.** KeePass's save ends in
  `MainForm.UpdateUIState`, which redraws the title bar and the menus and rebuilds neither the
  group tree nor the entry list, so an operator watching the window sees the database as it was
  before the request: an entry created through the API is not listed, one deleted is still listed,
  and one renamed still reads as it did. `DatabaseWriter` therefore calls
  `MainForm.UpdateUI(false, null, true, null, true, null, false)` after a change that was made and
  saved, on the thread it made it on and inside its own lock, when the database it changed is the
  one in front. For one in another tab it makes the same call with both lists left alone, since
  the tree and the list show the active document only and KeePass rebuilds them when another is
  brought forward.

  **Those seven arguments are `UpdateUISyncPost`'s**, which is the call KeePass makes to itself
  after a synchronize: the same situation, a database changed by something other than somebody
  typing in the window and already written to disk. Its own editing paths pass different arguments
  because they know more, adding one row or removing the rows they deleted. Naming neither a group
  nor an entry source is what keeps the operator's selection and scroll position, since both
  rebuilds fall back to what is selected now, and the last argument is false because the writer has
  saved already.

  **Two costs, stated rather than denied.** A rebuild replaces whatever the entry list is showing
  with the selected group's entries, so an operator looking at search results when a write lands
  in that database loses them; KeePass accepts that for a synchronize, and there is no cheaper call
  that would show an entry that was created or deleted. And it resets the inactivity timer, which
  changes nothing, because `UpdateUIState` inside the save already does. **A redraw that fails
  never fails the request**: by then the change is on disk, and a client told its create failed
  creates a second one under an identifier it was never given.

  **A change taken back out is told as well, and only as far as it needs to be.** The save that
  failed ended in `UpdateUIState` while the change was still in, so the title bar and the save
  button say there is something to save; taking the change out clears the flag, and the window is
  given the call with both lists left alone, which is all that is wrong on it.

  **Not for an admission record.** A record is in the database's own custom data, which the main
  window has no view of, so a pairing that lands redraws nothing, and one taken back out redraws
  the title bar and the menus for the reason a change does.
- **Nothing throws to the transport.** A handler failure becomes `500 internal error` and an
  incident, counted on the plugin's window under Tools and logged at `UNEXPECTED`, because an
  unhandled exception in this process takes KeePass with it. It is not audited, which this bullet
  used to claim: the request's audit line is the permitted one written before the handler ran,
  since the audit records the authorization and not what the request then did (§120).
- Distribution is a **plain DLL** targeting .NET Framework 4.8, not a `.plgx`, which is what allows
  modern C# and what the packaging measurement showed.

## 9. Audit §120

Every decision, allowed or refused, as JSON Lines outside the database: the admission key,
operationId, entry UUID, the node whose rule answered, decision, reason. The node asked about
and the node that decided are both there because nearest wins, so they are usually not the same
one: a line naming only the first sends an operator to an entry that carries no rule. Never a
title, never a value, because a log of titles is an inventory of the database sitting beside it.
A write failure is reported, not fatal, so a full disk cannot silently revoke every agent's
access, and its end is reported too, with how many decisions went unrecorded while it lasted.
A line records the authorization and not what the request then did: it is written before
the body is checked and before the handler runs, so a permitted line also stands for a request
answered afterwards with a malformed body, a conflict, `503 busy` or a failure, none of which gets a
line of its own. **A lock is not a decision either.** A request that meets the database locked or
closed before the router has decided about it, whether it was locked when the request arrived or
locked while the request was being read, is answered `503 locked` and written down nowhere, because
no rule was asked; a node that is simply not there while the database is open is still decided
about, refused and recorded. `openapi.yaml` §502 states the rule for the caller.

Three things step 14 settled about it. It defaults to this user's own application data rather than
beside the `.kdbx`, because a database is often on a share or in a cloud folder and a record of
which agent read which entry belongs in neither; `KEEPASSGATE_AUDIT_FILE` names another. It rolls at
eight megabytes keeping one previous file, which is the smallest thing that stops an unbounded file
while leaving the recent past readable. And **every value it carries is a string**, the decision
included, so a line says `"decision":"refused"` rather than `false` and a person skimming the file
reads a word rather than a flag whose sense they have to remember. A member with nothing to say is
written as an explicit `null` rather than left out, because a reader that has to tell "absent" from
"not applicable" is a reader that will guess, and a missing member reads as a log written by a
different version.

**That null is why `JsonBody` cannot read a line back**, and this corrects a claim made here
earlier. The all-strings shape was argued for partly on the plugin being able to read its own audit
with the reader it already has, and it cannot: that reader takes strings and arrays of strings, so
the lines it refuses are precisely the interesting ones, a question that never reached a node, which
every refused certificate is. A line with no client is written only by a defect, since a caller that
proved nothing to this authority is not written down at all. The shape is kept on its first reason
alone. The plugin reads the audit back for one thing only, when each client last made a request,
which the window asking which authority to keep shows, and it finds those two members by position
rather than with `JsonBody`; the reader the file is written for is whatever the operator already
greps with.

**The file is opened with the right to append and no other**, so that an administrator can make it
append only and every decision is still written. That was measured rather than assumed: on .NET
Framework 4.8, `File.AppendAllText`, which the audit used, asks for every write right at once and is
refused by such a file, so nothing was recorded at all, while a `FileStream` asking for
`FileSystemRights.AppendData` alone is let in, provided the ACL also grants the synchronize and read
attributes rights Windows asks for on every open. Setting the file aside then fails, as the
administrator meant it to, which is the failure the next paragraph already survives. The ACL is
something the plugin works under and not a control it provides: code running as the user is
conceded, and what keeps the file append only is an owner that is not the account.

**Rolling is a bound on the file and not permission to write to it.** A roll fails when the
previous copy is held open by whatever is tailing the audit, and what that costs is the bound rather
than the decision, because an audit that grows past eight megabytes is a smaller failure than an
audit that silently stops.

**A bound by size is a bound the caller can spend, so each client's lines are paced.** Every
decision about a request an admitted client sends writes a line, a lookup of a node that is not
there included, and with nothing holding it back one client rolled the file twice in under two
minutes and took every earlier line out of both files, its own earlier reads among them. The threat
model includes the caller, and a caller covering its tracks is the one the audit exists for. So
`DecisionPace` lets a client be recorded a thousand times at once and ten times a second after that,
per admission key, and a decision past that **waits** on the caller's own connection rather than
being dropped or refused: a line left out is the loss being prevented, and a refusal would be a
status the API does not have. That is a floor rather than a guarantee, since one flooding client now
needs well over an hour to push a decision out of both files, and shipping the lines elsewhere is
still what makes the record durable.

A line carries neither the profiles the client holds nor the verb it asked for. Which profiles a
client has is in the database already, and a log repeating them is a second copy of the thing worth
stealing. A refusal that the client's whole record would have permitted names the profiles the
operator removed, under the reason `profile removed`, and that is not the copy: a removed name
grants nothing, so writing it down says why a request failed without saying what anybody can
reach.

**A certificate this authority issued is written down when an operation refuses it**, by its admission key,
with the reason the credential check gave and no node. It has no admission record, or is outside
its dates, or is not a client certificate, so no rule is asked; but a revoked client still trying is
exactly what the operator who revoked it opens this file to see, and until this it was answered
`401` and left a line nowhere. A caller with no certificate, or with one from another authority,
names nobody the operator issued and is not written down, because a line for it would let anybody
who reaches the port grow the file.

**What the caller is told is less than what is recorded.** A node it may not reach and a node that
is not there answer the same 404, because telling them apart would let a client that may reach
nothing map the database by asking, one identifier at a time, which is what `list` exists to be able
to withhold. The audit says which it was.

**Where it is writing is said as it opens, and so is having nowhere to.** Nothing on a screen names
this file, so a machine where `KEEPASSGATE_AUDIT_FILE` is set, or where the default resolved
elsewhere, left the record of what an agent reached to be guessed at; the path now goes into the
plugin's log as the audit is opened. The case that had to be made audible is the one that made no
sound at all: with no path there is nothing to record to, every request is still answered, nothing
is counted as lost because nothing was attempted, and an audit that is not being written looks
exactly like an audit with nothing in it. That is reported as an error and names the variable that
fixes it. `docs/audit.md` is the same answer written for whoever runs this.

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

Step 13 settled the label: the tab is captioned **"KPG"** with the plugin's icon beside it, and not
"Access control". The caption names the plugin rather than the subject, so two pages that both edit
access control are told apart by the one thing that distinguishes them, and whoever is looking for
these rules can see what installed them. It is short because the strip is shared: every plugin that
adds a page takes room from the next, and a caption spelling the product out would crowd them all to
repeat what the icon beside it already says. `KPG` is also the key these rules are stored under, so
the caption and the namespace are one word rather than two things to learn. The name in full belongs
where there is room for it, which a tab strip is not.

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

**And a write that a dialog would take back is refused rather than raced.** A dialog open on a node
puts its copy back on OK, so a write to that node answers `503 busy` while it is open. A dialog that
is adding something is about a group it does not hold, and is answered for there: a group that Add
Entry or the TAN wizard will fill on OK is not removed while either is open, and nothing is created
in a group whose Add Group dialog is still open, since its Cancel takes the group back out with
whatever was put in it.

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

It is, and the document declares thirty-six operations over thirteen paths, eleven of them the
`HEAD` that answers for a `GET`. The property being bought, that an undeclared path has nowhere to
go and that the document cannot disagree with the implementation, is worth more than the
microseconds.

### C7. "The ceiling in the certificate is redundant if the ACL decides anyway" §129

It is not, though the shape of the answer changed. The certificate no longer names databases, because
the issuing authority does that. What remains is an **admission record**, and it is not redundant
with the ACL: the record says whether this client exists at all and which profiles it was granted,
the ACL says what those profiles reach. Two independent answers that fail independently, and the
record additionally fails **closed** when it goes missing, which is what makes the `GroupForm`
snapshot hazard survivable.

### C8. "A removal is a removal, so the rules have decided everything" §138

Not quite, and this is independent of which letters a rule holds. `KdbxVault.Discard` removes an
entry the way KeePass does: to the recycle bin when the database keeps one, and permanently only
when it does not or when the entry is already in there. So a removal is usually a **move**, and a
move crosses an ACL boundary. The bin is an ordinary group under the root, governed by whatever rule
reaches it there, so a client granted removal on one group and no `read` could relocate an entry it
may not read into a group where a rule at the root may well let it read what it just could not.

**The recycle bin is therefore off limits to this API.** It is not listed, not readable, not
writable and not addressable: a request naming it is answered the way any other node the rules
refuse is answered, with the 404 that tells a client nothing about what is there. That closes the
relocation path, because what a removal moves an entry into is somewhere no client can reach.

It is the right answer for a second reason, which would hold even without the escalation. The bin is
where the operator's own deletions land, and it is the one place in the database whose contents are
there precisely because somebody decided they were finished with them. Nothing about it belongs in an
agent's view.

Two consequences to keep in mind rather than to fix. A removal through the API is not destruction
while the database keeps a bin, so an operator who wants agent removals to be final turns the bin off
in KeePass's own settings, which is a KeePass decision rather than one this plugin should make for
them. And an entry that goes into the bin leaves the reach of every rule, so its rule is no longer
consulted for anything: recovering it is the operator's job, in KeePass, where they can see it.

**One case reopens the question, and it is refused rather than repaired.** A database that has never
had a bin gets one the first time something is removed, and the database settings dialog holds a copy
of that choice from the moment it opens: a bin made underneath it is undone by its OK, which would
leave the entry sitting in a group that no longer is one. So a removal that would make the first bin
is refused with `503 busy` while that dialog is open, the way every other write a dialog would take
back is refused. A database that already has a bin is unaffected, because the dialog is putting back
the same answer it copied.

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
   answer for. That the settings dialog keeps no snapshot is also why the tab now keeps one of its
   own: safe from Cancel turned out to mean unaffected by it, and a tab that has already acted
   inside a dialog offering OK and Cancel is a tab disagreeing with the buttons around it.
   Two things step 7 inherits early: `AnchorPin`, because telling a stored pair apart by
   issuer name turned out to accept a foreign authority, and the gate key.
6. **Gate and transport**: accept loop, ClientHello peek, SNI resolution, TLS 1.3, our request
   parser, one route. `IGateContext` lands here. **Done.** A real client reaches a real KeePass over
   mutual TLS 1.3 and is answered, with the host certificate chosen from the name in the
   ClientHello and read out of the database it belongs to. The refusals are all in place and
   tested: a connection that is not TLS costs five bytes, a name that resolves to nothing is
   dropped without a response, two databases answering to one name are both refused, and a client
   whose certificate does not chain to that database's authority is told it is unpaired rather
   than served. The request parser refuses transfer encodings, folded headers, two content
   lengths, a head over 8 KB, a body over 1 MB and a path that climbs.

   Two things landed early because the transport could not be built without them. `AuthorityIssuer`
   can issue a client certificate, which pairing will call for real in step 8. And identity is
   already checked with the anchor pin and an explicit extended key usage check, which leaves step 7
   the admission record and the validity window rather than the whole of validation.
7. **Validation**: anchor pin, EKU check, admission record lookup, and a test that a foreign
   authority's certificate is refused. **Done.** The pin and the usage check landed with step 6,
   because a transport with no identity is not testable; what this step added is the half that
   grants. A certificate that chains, carries `clientAuth` and is inside its dates now opens nothing
   without a record in the database, the profiles come from that record rather than from the
   certificate's claims, and deleting a record refuses the same certificate on the very next
   connection. Dates are checked here rather than by the chain build, so the allowance for a wrong
   clock is exactly five minutes and only on `notBefore`.
8. **Pairing**: invitation in memory, the proof exchange, the approval dialog, issuance with the
   record written and saved before the certificate is handed over. **Done.** The gate resolves an
   invitation exactly as it resolves a database, so a token reaches `/pair/*` and nothing else, and
   an invitation that has run out is dropped without a word. The proofs need no transport interface
   at all: `tls-server-end-point` is a hash of the certificate in use, which both sides already
   hold. And the ordering is the whole of it, so it is worth naming: a dialog holding a copy of what
   would be written refuses before anybody is disturbed, the certificate is issued, the invitation is spent, the
   record is written and saved on the user interface thread and read back, and only then is anything
   handed over.
9. **Lock and unlock**: the responder certificate, dropping the privileged key, closing connections
   on both transitions. **Done.** A locked database is still served, by a target that has the gate
   key and the responder and nothing else: no authority, so every caller is a stranger by
   construction rather than by a check somebody has to remember, no records, and nothing may be
   written. Every route answers `503 locked`, pairing included, and names nothing else. The
   responder carries its own marker so a client can refuse to send real requests over it, and that
   marker is a `kpg://host/responder` URI rather than a private extension because a custom OID needs
   an arc nobody has allocated us.
10. **The reference client**: `PyKeePassGate`, pairing and every route the gate serves, with the
   protocol pieces pinned against the plugin's own output. **Done.** It arrives here rather than at
   the end
   because a second implementation is what turns a specification into a contract: where the two
   disagree one of them is wrong, and a disagreement should fail a test rather than a pairing.
11. **Spec router**: the document, `operationId` binding, body checking, startup assertion. `HEAD`
   is decided: every `GET` declares one, and both reach the same handler. **Done.** Bodies are
   checked against the declared schema by a checker over the subset this wire carries, which is
   what §118 settles and why no schema library is taken.
12. **The ACL**: the model in §114, resolving against a real database. **Done.** The rules are
    decided against `IAclNode`, a seam of a document and a parent, so what a grant means is settled
    without a password manager in the room and `KdbxNodes` is the only part that needs one. What the
    host tier adds is the two things only KeePass can answer: an entry carries custom data at all,
    which is a KDBX4 feature, and the parent chain really does reach the root from an entry. The
    verb an operation needs is now read out of the routing table as an `AclVerb` rather than a
    string, and an operation that declares none stops the plugin loading. Nothing is enforced yet:
    this step is what a grant means, not where it is asked.
13. **The editor**: the grant UI, and the largest single piece. **Done**, and
    it is what makes everything above it reachable: `CustomData` is not editable in the stock
    KeePass dialogs, so without it no grant can be made at all and the model is inert. A **KPG** tab
    on the group and entry dialogs, one row per profile, its rule a `CRUSDLAP` mask, and what the
    groups above grant shown among the local rules so the table is the whole answer. Neither column
    is typed into: a mask spelled by hand is a rule nobody meant, and a profile spelled by hand is a
    rule for a name that may not exist, which grants nobody anything and reads exactly like one that
    works. Both are chosen from a list. The rule is written into the dialog's own copy of the node's
    custom data, which is what makes that dialog's OK keep it and its Cancel discard it. The end to
    end test makes a rule in a real KeePass with a real pointer and real keystrokes, presses OK, and
    finds it in the `.kdbx` after the process has gone.
14. **Intersection and audit**: the two answers combined, every decision recorded. **Done.** The
    intersection turned out to be structural rather than a step: the profiles handed to the
    resolver are the ones the admission record carries, so there is no place where the two halves
    are compared and none where they could be compared wrongly. **The router asks, not the
    handler**, because a handler could forget to and a route added later would then be a route with
    no rules on it. An operation that declares a verb and names no node stops the plugin loading.
    Nothing this build declares needs any of it, because nothing it declares touches the database:
    what step 14 leaves is the place where the first route that does will be asked.
15. **The database API**: the routes that read and change groups, entries, fields and attachments.
    **Done.** They arrive with no new machinery, which is the point of everything under them: a
    verb in the document, a node selector beside the handler, and the router does the rest.
    [`API.md`](API.md) is the account of them, and `openapi.yaml` is the contract.
16. **Running it**: two launchers that build, deploy and start the pinned KeePass with the plugin in
    it, one per configuration. **Done.** They exist because the step somebody does by hand between
    a build and a look is the step they get wrong: starting whichever KeePass is on the `PATH`
    rather than the one this repository pins and verifies, which is how a plugin comes to be judged
    against a version nobody chose. Deploying is not enough on its own, since a plugin that is
    present and not loaded looks exactly like one that is loaded and doing nothing.
17. **A profile gets a home**: the set of names a database knows, stored with the authority, with a page
    of its own on the tab to manage them, and a name taken off it granting nothing wherever a rule
    still names it.
    **Done.** Before this a profile was whatever somebody had typed, and a misspelling made one that
    existed,
    granted nothing and looked exactly like the one that was meant. The editor stops accepting typed
    names once there is a list to choose from, and pairing marks a name this database has never seen
    so that approving it is visibly two decisions rather than one.
18. **The plugin's own window**: one item under KeePass's Tools menu, opening a window that says
    where the gate is listening and sets the port it listens on. **Done.** That answer was on the
    KPG tab, which was the wrong place for it twice over: it is the same whichever database is open, and
    it is wanted when none is, which is exactly when a tab inside a database's settings cannot be
    reached. Moving it also gives the tab back the lines it was spending on a question it was not
    about. The version is not repeated here at all, because KeePass's own plugins dialog lists every
    plugin with its version and that is where somebody checking one looks. Through
    `Plugin.GetMenuItem`, because that is the seam KeePass offers and it removes the item itself
    when the plugin is unloaded.
19. **The documentation for whoever runs it**: `docs/`, in the order an operator meets the plugin,
    with the screens. **Done.** Everything written before this is written for whoever builds it,
    and the person the design defers to at every decision is the one answering a dialog. **The pictures are
    generated**, by driving a throwaway KeePass through the whole operator flow and screenshotting
    each step where it happens, so a change to a tab is a change to the documentation in the same
    pass rather than a picture that quietly stops being true. A screenshot pasted in by hand is
    correct on the day it is taken and wrong from the next change onwards, with nothing anywhere to
    say which day that was.
20. **The certificate lifecycle**: a lifetime for each client, proposed by the client and picked
    by the operator; the records of expired certificates pruned on the plugin's own writes; a route
    by which a client revokes its own certificate; and renewal through an approval dialog like
    pairing's.

Steps 5 to 8 are the ones that prove the design. Step 5 is deliberately early, because the
`CustomData` dialog hazard is the thing most likely to invalidate the storage decision, and finding
that out after building five layers on top of it would be expensive.
